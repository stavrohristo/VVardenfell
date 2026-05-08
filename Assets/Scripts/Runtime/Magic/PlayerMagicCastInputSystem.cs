using System;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Magic
{
    [UpdateInGroup(typeof(MorrowindGameplayInputSystemGroup))]
    [UpdateAfter(typeof(PlayerInputReceivingSystem))]
    public partial struct PlayerMagicCastInputSystem : ISystem
    {
        const uint BookRecordTag = (uint)'B' | ((uint)'O' << 8) | ((uint)'O' << 16) | ((uint)'K' << 24);

        EntityQuery _playerQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalPlayerPresentationState>(),
                ComponentType.ReadWrite<PlayerCharacterControl>(),
                ComponentType.ReadWrite<ActorMagicCastState>(),
                ComponentType.ReadWrite<ActorVitalSet>(),
                ComponentType.ReadOnly<ActorAttributeSet>(),
                ComponentType.ReadOnly<ActorSkillSet>(),
                ComponentType.ReadOnly<ActorDerivedMovementStats>(),
                ComponentType.ReadOnly<ActorActiveMagicEffect>(),
                ComponentType.ReadOnly<ActorKnownSpell>(),
                ComponentType.ReadWrite<PlayerInventoryItem>(),
                ComponentType.ReadWrite<ActorUsedPower>());

            systemState.RequireForUpdate(_playerQuery);
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<SpellWindowState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][ContentBlob] Player magic input requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            Entity player = _playerQuery.GetSingletonEntity();
            var controlRef = _playerQuery.GetSingletonRW<PlayerCharacterControl>();
            var magicRef = _playerQuery.GetSingletonRW<ActorMagicCastState>();
            ref var control = ref controlRef.ValueRW;
            ref var magic = ref magicRef.ValueRW;

            ref var spellState = ref SystemAPI.GetSingletonRW<SpellWindowState>().ValueRW;
            NormalizeSelectedSource(ref systemState, ref content, player, ref spellState);

            bool hasCastableSelected = TryGetSelectedCastableSource(ref systemState, ref content, player, spellState, out var selectedSource);
            if (!hasCastableSelected)
                ClearMagicReady(ref magic);

            if (control.ReadyMagicTogglePressed)
            {
                control.ReadyMagicTogglePressed = false;
                if (magic.MagicReadied != 0)
                {
                    ClearMagicReady(ref magic);
                }
                else if (hasCastableSelected)
                {
                    magic.MagicReadied = 1;
                }
            }

            if (!control.CastMagicPressed)
                return;

            control.CastMagicPressed = false;
            if (magic.MagicReadied == 0 || magic.CastInProgress != 0)
                return;
            if (!hasCastableSelected)
                return;

            if (selectedSource.Kind == RuntimeMagicSourceKind.EnchantedItem)
            {
                CastEnchantedItemImmediately(ref systemState, player, selectedSource);
                return;
            }

            if (!IsActiveVisualSpellReady(ref systemState))
                return;

            BeginPreparedMagicCast(ref systemState, ref content, player, selectedSource, ref magic);
        }

        bool IsActiveVisualSpellReady(ref SystemState systemState)
        {
            var presentation = _playerQuery.GetSingleton<LocalPlayerPresentationState>();
            Entity visual = presentation.Mode == PlayerViewMode.FirstPerson
                ? presentation.FirstPersonVisual
                : presentation.ThirdPersonVisual;
            if (visual == Entity.Null || !systemState.EntityManager.Exists(visual))
                throw new InvalidOperationException("[VVardenfell][Magic] Player has no active visual for spell casting.");
            if (!systemState.EntityManager.HasComponent<ActorWeaponAnimationState>(visual))
                throw new InvalidOperationException($"[VVardenfell][Magic] Active player visual entity={visual.Index}:{visual.Version} has no ActorWeaponAnimationState.");

            var weaponState = systemState.EntityManager.GetComponentData<ActorWeaponAnimationState>(visual);
            return weaponState.WeaponType == ActorWeaponAnimationUtility.SpellWeaponType
                   && weaponState.Drawn != 0
                   && weaponState.Phase == ActorWeaponAnimationPhase.Equipped;
        }

        static void NormalizeSelectedSource(ref SystemState systemState, ref RuntimeContentBlob content, Entity player, ref SpellWindowState state)
        {
            if (TryGetSelectedCastableSource(ref systemState, ref content, player, state, out _))
                return;

            if (FindFirstCastableSpell(ref systemState, ref content, player, out var source))
            {
                ApplySelectedSource(ref state, source);
                return;
            }

            state.SelectedSourceKind = (byte)RuntimeMagicSourceKind.None;
            state.SelectedSpellIndex = -1;
            state.SelectedSpell = default;
            state.SelectedInventoryIndex = -1;
            state.SelectedItemContent = default;
            state.SelectedEnchantment = default;
        }

        static bool FindFirstCastableSpell(ref SystemState systemState, ref RuntimeContentBlob content, Entity player, out SelectedMagicSource source)
        {
            var knownSpells = systemState.EntityManager.GetBuffer<ActorKnownSpell>(player, true);
            for (int i = 0; i < knownSpells.Length; i++)
            {
                var spellHandle = knownSpells[i].Spell;
                if (TryBuildSpellSource(ref content, spellHandle, i, out source))
                    return true;
            }

            source = default;
            return false;
        }

        static bool TryGetSelectedCastableSource(
            ref SystemState systemState,
            ref RuntimeContentBlob content,
            Entity player,
            in SpellWindowState state,
            out SelectedMagicSource source)
        {
            source = default;
            if (state.SelectedSourceKind == (byte)RuntimeMagicSourceKind.Spell)
            {
                if (!TryBuildSpellSource(ref content, state.SelectedSpell, state.SelectedSpellIndex, out source))
                    return false;
                var knownSpells = systemState.EntityManager.GetBuffer<ActorKnownSpell>(player, true);
                return MorrowindActorMagicUtility.HasKnownSpell(knownSpells, source.Spell);
            }

            if (state.SelectedSourceKind != (byte)RuntimeMagicSourceKind.EnchantedItem)
                return false;

            var inventory = systemState.EntityManager.GetBuffer<PlayerInventoryItem>(player, true);
            return TryBuildEnchantedItemSource(ref content, inventory, state.SelectedInventoryIndex, state.SelectedItemContent, state.SelectedEnchantment, out source);
        }

        static bool TryBuildSpellSource(ref RuntimeContentBlob content, SpellDefHandle spellHandle, int knownSpellIndex, out SelectedMagicSource source)
        {
            source = default;
            if (!spellHandle.IsValid || spellHandle.Index < 0 || spellHandle.Index >= content.Spells.Length)
                throw new InvalidOperationException($"[VVardenfell][Magic] Selected spell references invalid spell handle {spellHandle.Value}.");
            ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref content, spellHandle);
            if (spell.SpellType != MorrowindSpellCostUtility.SpellTypeSpell
                && spell.SpellType != MorrowindSpellCostUtility.SpellTypePower)
            {
                return false;
            }

            if (spell.EffectStartIndex < 0 || spell.EffectCount <= 0)
                throw new InvalidOperationException($"[VVardenfell][Magic] Castable spell contentId=0x{spell.ContentId.Value:X16} has no effects.");

            source.Kind = RuntimeMagicSourceKind.Spell;
            source.KnownSpellIndex = knownSpellIndex;
            source.Spell = spellHandle;
            source.Range = (byte)content.MagicEffectInstances[spell.EffectStartIndex].Range;
            return true;
        }

        static bool TryBuildEnchantedItemSource(
            ref RuntimeContentBlob content,
            DynamicBuffer<PlayerInventoryItem> inventory,
            int inventoryIndex,
            ContentReference itemContent,
            EnchantmentDefHandle enchantmentHandle,
            out SelectedMagicSource source)
        {
            source = default;
            if (inventoryIndex < 0 || inventoryIndex >= inventory.Length)
                return false;
            var item = inventory[inventoryIndex];
            if (item.Count <= 0 || item.Content.Kind != itemContent.Kind || item.Content.HandleValue != itemContent.HandleValue)
                return false;
            if (!enchantmentHandle.IsValid || enchantmentHandle.Index < 0 || enchantmentHandle.Index >= content.Enchantments.Length)
                throw new InvalidOperationException($"[VVardenfell][Magic] Selected item references invalid enchantment handle {enchantmentHandle.Value}.");

            ref RuntimeBaseDefBlob itemDef = ref RuntimeContentBlobUtility.Get(ref content, new ItemDefHandle { Value = item.Content.HandleValue });
            bool hasItemEquipment = RuntimeContentBlobUtility.TryGetItemEquipment(ref content, item.Content, out _);
            bool isScrollRecord = itemDef.RecordTag == BookRecordTag;
            if (!hasItemEquipment && !isScrollRecord)
                return false;
            if (itemDef.EnchantIdHash == 0UL)
                return false;
            if (!RuntimeContentBlobUtility.TryGetEnchantmentHandleByIdHash(ref content, itemDef.EnchantIdHash, out var actualEnchantment)
                || actualEnchantment.Value != enchantmentHandle.Value)
            {
                return false;
            }

            ref RuntimeEnchantmentDefBlob enchantment = ref RuntimeContentBlobUtility.Get(ref content, enchantmentHandle);
            if (!MorrowindEnchantmentUtility.IsUsableMagicItemType(enchantment.EnchantmentType))
                throw new InvalidOperationException($"[VVardenfell][Magic] Selected item enchantment type {enchantment.EnchantmentType} is not castable from the Magic window.");
            if (enchantment.EnchantmentType == MorrowindEnchantmentUtility.WhenUsed && !hasItemEquipment)
                return false;
            if (enchantment.EnchantmentType == MorrowindEnchantmentUtility.CastOnce && !isScrollRecord)
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast-once enchantment {enchantment.Id.ToString()} is attached to non-scroll item {itemDef.Id.ToString()}.");
            RuntimeContentBlobUtility.RequireRange(enchantment.EffectStartIndex, enchantment.EffectCount, content.MagicEffectInstances.Length, $"enchantment contentId=0x{enchantment.ContentId.Value:X16} effects");

            source.Kind = RuntimeMagicSourceKind.EnchantedItem;
            source.InventoryIndex = inventoryIndex;
            source.ItemContent = item.Content;
            source.Enchantment = enchantmentHandle;
            source.Range = (byte)ResolveEnchantmentEffectRange(ref content, ref enchantment);
            return true;
        }

        static void ApplySelectedSource(ref SpellWindowState state, in SelectedMagicSource source)
        {
            state.SelectedSourceKind = (byte)source.Kind;
            state.SelectedSpellIndex = source.KnownSpellIndex;
            state.SelectedSpell = source.Spell;
            state.SelectedInventoryIndex = source.InventoryIndex;
            state.SelectedItemContent = source.ItemContent;
            state.SelectedEnchantment = source.Enchantment;
        }

        void BeginPreparedMagicCast(
            ref SystemState systemState,
            ref RuntimeContentBlob content,
            Entity player,
            SelectedMagicSource source,
            ref ActorMagicCastState magic)
        {
            SpellDefHandle spellHandle = source.Spell;
            ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref content, spellHandle);
            MorrowindActorMagicUtility.RequireSpellEffectRange(ref content, ref spell);
            var activeEffects = systemState.EntityManager.GetBuffer<ActorActiveMagicEffect>(player, true);
            if (MorrowindMagicEffectApplicationUtility.SumEffectMagnitude(activeEffects, MorrowindMagicEffectIds.Silence) > 0f)
            {
                QueuePlayerMessage(ref systemState, RuntimeContentBlobUtility.RequireGameSettingStringByIdHash(ref content, RuntimeContentKnownHashes.sMagicSkillFail));
                return;
            }

            var vitals = systemState.EntityManager.GetComponentData<ActorVitalSet>(player);
            int spellCost = MorrowindSpellCostUtility.CalculateSpellCost(ref content, ref spell);
            if (spellCost > 0 && vitals.CurrentMagicka < spellCost)
            {
                QueuePlayerMessage(ref systemState, RuntimeContentBlobUtility.RequireGameSettingStringByIdHash(ref content, RuntimeContentKnownHashes.sMagicInsufficientSP));
                return;
            }

            if (spell.SpellType == MorrowindSpellCostUtility.SpellTypePower)
            {
                float totalHours = ResolveTotalGameHours(ref systemState);
                var usedPowers = systemState.EntityManager.GetBuffer<ActorUsedPower>(player, true);
                for (int i = 0; i < usedPowers.Length; i++)
                {
                    if (usedPowers[i].Spell.Value == spellHandle.Value && totalHours - usedPowers[i].LastUsedTotalGameHours < 24f)
                    {
                        QueuePlayerMessage(ref systemState, RuntimeContentBlobUtility.RequireGameSettingStringByIdHash(ref content, RuntimeContentKnownHashes.sPowerAlreadyUsed));
                        return;
                    }
                }
            }

            if (spellCost > 0)
            {
                var derived = systemState.EntityManager.GetComponentData<ActorDerivedMovementStats>(player);
                vitals.CurrentMagicka -= spellCost;
                vitals.CurrentFatigue -= spellCost * (RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fFatigueSpellBase)
                                        + derived.NormalizedEncumbrance * RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fFatigueSpellMult));
                systemState.EntityManager.SetComponentData(player, vitals);
            }

            ref MagicEffectInstanceDef firstEffect = ref content.MagicEffectInstances[spell.EffectStartIndex];
            magic.CastInProgress = 1;
            magic.CastRequested = 1;
            magic.CastRange = source.Range;
            magic.CastingSourceKind = (byte)RuntimeMagicSourceKind.Spell;
            magic.CastingSpell = spellHandle;
            magic.CastingEnchantment = default;
            magic.CastingItemContent = default;
            magic.CastingInventoryIndex = -1;
        }

        void CastEnchantedItemImmediately(
            ref SystemState systemState,
            Entity player,
            in SelectedMagicSource source)
        {
            Entity target = Entity.Null;
            uint targetPlacedRefId = 0u;
            if (SystemAPI.TryGetSingleton<PlayerInteractionFocus>(out var focus) && focus.HasTarget != 0)
            {
                target = focus.TargetEntity;
                targetPlacedRefId = focus.PlacedRefId;
            }

            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            systemState.EntityManager.GetBuffer<ActorSpellCastRequest>(runtimeEntity).Add(new ActorSpellCastRequest
            {
                CasterEntity = player,
                TargetEntity = target,
                TargetPlacedRefId = targetPlacedRefId,
                SourceKind = (byte)RuntimeMagicSourceKind.EnchantedItem,
                Enchantment = source.Enchantment,
                SourceContent = source.ItemContent,
                SourceInventoryIndex = source.InventoryIndex,
            });
        }

        float ResolveTotalGameHours(ref SystemState systemState)
            => SystemAPI.TryGetSingleton<MorrowindTimeState>(out var time)
                ? (time.DaysPassed * 24f) + time.GameHour
                : throw new InvalidOperationException("[VVardenfell][Magic] Power cooldown requires MorrowindTimeState.");

        void QueuePlayerMessage(ref SystemState systemState, string message)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            systemState.EntityManager.GetBuffer<ShellMessageBoxRequest>(runtimeEntity).Add(new ShellMessageBoxRequest
            {
                Body = RuntimeFixedStringUtility.ToFixed512OrDefault(message),
            });
        }

        static void ClearMagicReady(ref ActorMagicCastState magic)
        {
            magic.MagicReadied = 0;
            magic.CastInProgress = 0;
            magic.CastRequested = 0;
            magic.CastRange = 0;
            magic.CastingSourceKind = 0;
            magic.CastingSpell = default;
            magic.CastingEnchantment = default;
            magic.CastingItemContent = default;
            magic.CastingInventoryIndex = -1;
        }

        static int ResolveEnchantmentEffectRange(ref RuntimeContentBlob content, ref RuntimeEnchantmentDefBlob enchantment)
        {
            RuntimeContentBlobUtility.RequireRange(enchantment.EffectStartIndex, enchantment.EffectCount, content.MagicEffectInstances.Length, $"enchantment contentId=0x{enchantment.ContentId.Value:X16} effects");
            return content.MagicEffectInstances[enchantment.EffectStartIndex].Range;
        }

        struct SelectedMagicSource
        {
            public RuntimeMagicSourceKind Kind;
            public int KnownSpellIndex;
            public SpellDefHandle Spell;
            public int InventoryIndex;
            public ContentReference ItemContent;
            public EnchantmentDefHandle Enchantment;
            public byte Range;
        }
    }
}
