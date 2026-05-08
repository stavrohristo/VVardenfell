using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Magic;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateBefore(typeof(SpellWindowStateSystem))]
    public partial struct SpellWindowSelectionActionSystem : ISystem
    {
        EntityQuery _playerQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<ActorKnownSpell>(),
                ComponentType.ReadWrite<PlayerInventoryItem>(),
                ComponentType.ReadWrite<ActorEquipmentSlot>());

            systemState.RequireForUpdate(_playerQuery);
            systemState.RequireForUpdate<SpellWindowState>();
            systemState.RequireForUpdate<SpellWindowRequest>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            ref var request = ref SystemAPI.GetSingletonRW<SpellWindowRequest>().ValueRW;
            if (request.PendingSelectionChange == 0)
                return;

            request.PendingSelectionChange = 0;
            ref var state = ref SystemAPI.GetSingletonRW<SpellWindowState>().ValueRW;
            var sourceKind = (RuntimeMagicSourceKind)request.SelectedSourceKind;
            if (sourceKind == RuntimeMagicSourceKind.None)
            {
                ClearSelection(ref state);
                return;
            }

            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][MagicUI] Spell selection requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;
            Entity player = _playerQuery.GetSingletonEntity();

            if (sourceKind == RuntimeMagicSourceKind.Spell)
            {
                CommitSpellSelection(ref systemState, ref content, player, ref state, request.SelectedSpellIndex, request.SelectedSpell);
                return;
            }

            if (sourceKind == RuntimeMagicSourceKind.EnchantedItem)
            {
                CommitEnchantedItemSelection(ref systemState, ref content, player, ref state, request.SelectedInventoryIndex, request.SelectedItemContent, request.SelectedEnchantment);
                return;
            }

            throw new InvalidOperationException($"[VVardenfell][MagicUI] Unsupported selected magic source kind {request.SelectedSourceKind}.");
        }

        static void CommitSpellSelection(
            ref SystemState systemState,
            ref RuntimeContentBlob content,
            Entity player,
            ref SpellWindowState state,
            int knownSpellIndex,
            SpellDefHandle spellHandle)
        {
            if (!spellHandle.IsValid || spellHandle.Index < 0 || spellHandle.Index >= content.Spells.Length)
                throw new InvalidOperationException("[VVardenfell][MagicUI] Spell selection requested an invalid spell handle.");

            ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref content, spellHandle);
            if (spell.SpellType != MorrowindSpellCostUtility.SpellTypeSpell
                && spell.SpellType != MorrowindSpellCostUtility.SpellTypePower)
            {
                throw new InvalidOperationException($"[VVardenfell][MagicUI] Passive spell '{spell.Id.ToString()}' cannot be selected as castable magic.");
            }

            var knownSpells = systemState.EntityManager.GetBuffer<ActorKnownSpell>(player, true);
            if (knownSpellIndex < 0 || knownSpellIndex >= knownSpells.Length || knownSpells[knownSpellIndex].Spell.Value != spellHandle.Value)
            {
                knownSpellIndex = -1;
                for (int i = 0; i < knownSpells.Length; i++)
                {
                    if (knownSpells[i].Spell.Value == spellHandle.Value)
                    {
                        knownSpellIndex = i;
                        break;
                    }
                }
            }

            if (knownSpellIndex < 0)
                throw new InvalidOperationException($"[VVardenfell][MagicUI] Player does not know selected spell '{spell.Id.ToString()}'.");

            state.SelectedSourceKind = (byte)RuntimeMagicSourceKind.Spell;
            state.SelectedSpellIndex = knownSpellIndex;
            state.SelectedSpell = spellHandle;
            state.SelectedInventoryIndex = -1;
            state.SelectedItemContent = default;
            state.SelectedEnchantment = default;
        }

        static void CommitEnchantedItemSelection(
            ref SystemState systemState,
            ref RuntimeContentBlob content,
            Entity player,
            ref SpellWindowState state,
            int inventoryIndex,
            ContentReference itemContent,
            EnchantmentDefHandle enchantmentHandle)
        {
            if (inventoryIndex < 0)
                return;

            var inventory = systemState.EntityManager.GetBuffer<PlayerInventoryItem>(player);
            if (inventoryIndex >= inventory.Length)
                return;

            var item = inventory[inventoryIndex];
            if (item.Count <= 0 || item.Content.Kind != ContentReferenceKind.Item)
                return;
            if (item.Content.Kind != itemContent.Kind || item.Content.HandleValue != itemContent.HandleValue)
                return;

            ref RuntimeBaseDefBlob itemDef = ref RuntimeContentBlobUtility.Get(ref content, new ItemDefHandle { Value = item.Content.HandleValue });
            if (itemDef.EnchantIdHash == 0UL)
                throw new InvalidOperationException($"[VVardenfell][MagicUI] Selected magic item '{itemDef.Id.ToString()}' has no enchantment.");
            if (!RuntimeContentBlobUtility.TryGetEnchantmentHandleByIdHash(ref content, itemDef.EnchantIdHash, out var resolvedEnchantment)
                || !resolvedEnchantment.IsValid)
            {
                throw new InvalidOperationException($"[VVardenfell][MagicUI] Item {itemDef.Id.ToString()} references missing enchantment {itemDef.EnchantId.ToString()}.");
            }
            if (resolvedEnchantment.Value != enchantmentHandle.Value)
                throw new InvalidOperationException($"[VVardenfell][MagicUI] Selected magic item '{itemDef.Id.ToString()}' enchantment handle changed.");

            ref RuntimeEnchantmentDefBlob enchantment = ref RuntimeContentBlobUtility.Get(ref content, resolvedEnchantment);
            if (!MorrowindEnchantmentUtility.IsUsableMagicItemType(enchantment.EnchantmentType))
                throw new InvalidOperationException($"[VVardenfell][MagicUI] Enchantment '{enchantment.Id.ToString()}' is not a selectable magic item source.");

            var equipment = systemState.EntityManager.GetBuffer<ActorEquipmentSlot>(player);
            bool equipped = IsEquipped(equipment, inventoryIndex, item.Content);
            if (enchantment.EnchantmentType == MorrowindEnchantmentUtility.WhenUsed && !equipped)
            {
                if (!RuntimeContentBlobUtility.TryGetItemEquipment(ref content, new ItemDefHandle { Value = item.Content.HandleValue }, out var itemEquipment)
                    || !CanEquip(ref systemState, ref content, player, itemEquipment))
                {
                    return;
                }

                Equip(equipment, inventoryIndex, item, itemEquipment);
                equipped = IsEquipped(equipment, inventoryIndex, item.Content);
                if (!equipped)
                    return;
            }

            state.SelectedSourceKind = (byte)RuntimeMagicSourceKind.EnchantedItem;
            state.SelectedSpellIndex = -1;
            state.SelectedSpell = default;
            state.SelectedInventoryIndex = inventoryIndex;
            state.SelectedItemContent = item.Content;
            state.SelectedEnchantment = resolvedEnchantment;
        }

        static bool CanEquip(ref SystemState systemState, ref RuntimeContentBlob content, Entity player, in ItemEquipmentDef equipment)
        {
            if (equipment.Slot == ItemEquipmentSlot.None)
                return false;
            if (equipment.Kind == ItemEquipmentKind.Armor && equipment.Health == 0)
                return false;
            if (equipment.Kind != ItemEquipmentKind.Weapon
                && equipment.Kind != ItemEquipmentKind.Armor
                && equipment.Kind != ItemEquipmentKind.Clothing)
            {
                return false;
            }

            if (!systemState.EntityManager.HasComponent<PlayerRaceAppearance>(player))
                throw new InvalidOperationException("[VVardenfell][MagicUI] Player magic item equip requires PlayerRaceAppearance.");
            PlayerRaceAppearance appearance = systemState.EntityManager.GetComponentData<PlayerRaceAppearance>(player);
            if (ActorEquipmentRuntimeUtility.IsBeastRace(ref content, RuntimeContentStableHash.HashId(appearance.RaceId.ToString()))
                && HasBeastForbiddenPart(ref content, equipment))
            {
                return false;
            }

            return true;
        }

        static bool HasBeastForbiddenPart(ref RuntimeContentBlob content, in ItemEquipmentDef equipment)
        {
            RuntimeContentBlobUtility.RequireRange(equipment.FirstBodyPartIndex, equipment.BodyPartCount, content.ItemEquipmentBodyParts.Length, "item equipment body part");
            for (int i = 0; i < equipment.BodyPartCount; i++)
            {
                var part = content.ItemEquipmentBodyParts[equipment.FirstBodyPartIndex + i].Part;
                if (part == ItemEquipmentPartReference.Head
                    || part == ItemEquipmentPartReference.LeftFoot
                    || part == ItemEquipmentPartReference.RightFoot)
                {
                    return true;
                }
            }

            return false;
        }

        static void Equip(
            DynamicBuffer<ActorEquipmentSlot> equipment,
            int inventoryIndex,
            in PlayerInventoryItem item,
            in ItemEquipmentDef itemEquipment)
        {
            RemoveConflictingSlots(equipment, itemEquipment.Slot);
            equipment.Add(new ActorEquipmentSlot
            {
                Slot = itemEquipment.Slot,
                Content = item.Content,
                InventoryIndex = inventoryIndex,
                Condition = ActorEquipmentConditionUtility.ResolveInitialCondition(
                    itemEquipment,
                    item.Count,
                    item.Condition,
                    item.Content),
                VisualMode = ActorEquipmentRuntimeUtility.ResolveEquipmentVisualMode(itemEquipment),
            });
        }

        static void RemoveConflictingSlots(DynamicBuffer<ActorEquipmentSlot> equipment, ItemEquipmentSlot slot)
        {
            for (int i = equipment.Length - 1; i >= 0; i--)
            {
                if (Conflicts(equipment[i].Slot, slot))
                    equipment.RemoveAt(i);
            }
        }

        static bool Conflicts(ItemEquipmentSlot equipped, ItemEquipmentSlot candidate)
        {
            if (equipped == candidate)
                return true;
            return (equipped == ItemEquipmentSlot.Boots && candidate == ItemEquipmentSlot.Shoes)
                   || (equipped == ItemEquipmentSlot.Shoes && candidate == ItemEquipmentSlot.Boots);
        }

        static bool IsEquipped(DynamicBuffer<ActorEquipmentSlot> equipment, int inventoryIndex, ContentReference content)
        {
            for (int i = 0; i < equipment.Length; i++)
            {
                var slot = equipment[i];
                if (slot.InventoryIndex == inventoryIndex && slot.Content.Kind == content.Kind && slot.Content.HandleValue == content.HandleValue)
                    return true;
            }

            return false;
        }

        static void ClearSelection(ref SpellWindowState state)
        {
            state.SelectedSourceKind = (byte)RuntimeMagicSourceKind.None;
            state.SelectedSpellIndex = -1;
            state.SelectedSpell = default;
            state.SelectedInventoryIndex = -1;
            state.SelectedItemContent = default;
            state.SelectedEnchantment = default;
        }
    }
}
