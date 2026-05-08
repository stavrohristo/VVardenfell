using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Magic;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.Shell
{
    public partial class RuntimeHudShellPresentationSystem
    {
        const int MagicEffectFlagTargetSkill = 0x1;
        const int MagicEffectFlagTargetAttribute = 0x2;
        const int MagicEffectFlagNoMagnitude = 0x8;
        const uint BookRecordTag = (uint)'B' | ((uint)'O' << 8) | ((uint)'O' << 16) | ((uint)'K' << 24);

        SpellWindowViewModel BuildSpellModel(ref RuntimeContentBlob contentBlob, in SpellWindowState state, in PlayerPresentationStats playerStats)
        {
            int spellCount = contentBlob.Spells.Length;
            var model = new SpellWindowViewModel
            {
                NormalizedRect = RuntimeWindowGeometryUtility.ToUnityRect(state.Rect),
                Title = ResolveSelectedMagicTitle(ref contentBlob, state),
                FilterText = state.FilterText.ToString(),
                FooterButtonText = RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, "sDelete", "Delete"),
                EmptyStateText = "None",
                SpellSummaryText = $"Known spells: 0   Cached definitions: {spellCount}",
                EffectSummaryText = "No selected spell",
                ActiveEffects = BuildActiveEffectIcons(ref contentBlob, playerStats),
            };

            if (!playerStats.HasPlayer || !EntityManager.Exists(playerStats.PlayerEntity) || !EntityManager.HasBuffer<ActorKnownSpell>(playerStats.PlayerEntity))
                return model;

            var knownSpells = EntityManager.GetBuffer<ActorKnownSpell>(playerStats.PlayerEntity, true);
            var activeEffects = EntityManager.HasBuffer<ActorActiveMagicEffect>(playerStats.PlayerEntity)
                ? EntityManager.GetBuffer<ActorActiveMagicEffect>(playerStats.PlayerEntity, true)
                : default;
            var entries = new List<SpellWindowEntryViewModel>(knownSpells.Length);
            string filter = state.FilterText.ToString();
            AddSpellGroup(ref contentBlob, entries, knownSpells, activeEffects, filter, state, playerStats, MorrowindSpellCostUtility.SpellTypePower, RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, "sPowers", "Powers"), string.Empty);
            AddSpellGroup(ref contentBlob, entries, knownSpells, activeEffects, filter, state, playerStats, MorrowindSpellCostUtility.SpellTypeSpell, RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, "sSpells", "Spells"), RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, "sCostChance", "Cost/Chance"));
            AddEnchantedItemGroup(ref contentBlob, entries, playerStats, filter, state);
            model.Entries = entries.ToArray();
            model.SpellSummaryText = $"Magic sources: {CountSelectableEntries(entries)}   Cached definitions: {spellCount}";
            model.EmptyStateText = entries.Count == 0 && !string.IsNullOrWhiteSpace(filter)
                ? $"No spells match \"{filter.Trim()}\""
                : "None";
            if (state.SelectedSourceKind == (byte)RuntimeMagicSourceKind.Spell && state.SelectedSpell.IsValid)
            {
                var spellHandle = state.SelectedSpell;
                if (spellHandle.IsValid && spellHandle.Index >= 0 && spellHandle.Index < contentBlob.Spells.Length)
                {
                    ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref contentBlob, spellHandle);
                    model.EffectSummaryText = $"{RuntimeContentMetadataResolver.ResolveSpellName(ref spell)}   Cost {spell.Cost}   {RuntimeContentMetadataResolver.ResolveSpellTypeName(spell.SpellType)}";
                    model.Effects = BuildSpellEffectRows(ref contentBlob, ref spell);
                }
            }
            else if (state.SelectedSourceKind == (byte)RuntimeMagicSourceKind.EnchantedItem && state.SelectedEnchantment.IsValid)
            {
                ref RuntimeEnchantmentDefBlob enchantment = ref RuntimeContentBlobUtility.Get(ref contentBlob, state.SelectedEnchantment);
                model.EffectSummaryText = $"Magic Item   Charge {enchantment.Charge}";
                model.Effects = BuildEnchantmentEffectRows(ref contentBlob, ref enchantment);
            }

            return model;
        }

        void AddSpellGroup(
            ref RuntimeContentBlob contentBlob,
            List<SpellWindowEntryViewModel> entries,
            DynamicBuffer<ActorKnownSpell> knownSpells,
            DynamicBuffer<ActorActiveMagicEffect> activeEffects,
            string filter,
            in SpellWindowState state,
            in PlayerPresentationStats playerStats,
            int spellType,
            string groupName,
            string rightHeader)
        {
            var groupEntries = new List<SpellWindowEntryViewModel>();
            for (int i = 0; i < knownSpells.Length; i++)
            {
                var spellHandle = knownSpells[i].Spell;
                if (!spellHandle.IsValid || spellHandle.Index < 0 || spellHandle.Index >= contentBlob.Spells.Length)
                    continue;

                ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref contentBlob, spellHandle);
                if (spell.SpellType != spellType || !MatchesSpellFilter(ref contentBlob, ref spell, filter))
                    continue;

                groupEntries.Add(new SpellWindowEntryViewModel
                {
                    SpellIndex = i,
                    SourceKind = RuntimeMagicSourceKind.Spell,
                    Spell = spellHandle,
                    InventoryIndex = -1,
                    Name = RuntimeContentMetadataResolver.ResolveSpellName(ref spell),
                    CostText = spell.SpellType == MorrowindSpellCostUtility.SpellTypeSpell
                        ? $"{MorrowindSpellCostUtility.CalculateSpellCost(ref contentBlob, ref spell)}/{ResolveSpellChance(ref contentBlob, ref spell, playerStats, activeEffects)}"
                        : string.Empty,
                    TypeText = RuntimeContentMetadataResolver.ResolveSpellTypeName(spell.SpellType),
                    EffectTooltipText = BuildSpellEffectTooltip(ref contentBlob, ref spell),
                    SpellTooltip = BuildSpellTooltip(ref contentBlob, ref spell),
                    Count = 1,
                    Active = true,
                    Selected = state.SelectedSourceKind == (byte)RuntimeMagicSourceKind.Spell && state.SelectedSpell.Value == spellHandle.Value,
                });
            }

            AddSortedGroup(entries, groupName, rightHeader, groupEntries);
        }

        void AddEnchantedItemGroup(
            ref RuntimeContentBlob contentBlob,
            List<SpellWindowEntryViewModel> entries,
            in PlayerPresentationStats playerStats,
            string filter,
            in SpellWindowState state)
        {
            Entity player = playerStats.PlayerEntity;
            if (!EntityManager.HasBuffer<PlayerInventoryItem>(player))
                return;

            var inventory = EntityManager.GetBuffer<PlayerInventoryItem>(player, true);
            bool hasEquipment = EntityManager.HasBuffer<ActorEquipmentSlot>(player);
            var equipment = hasEquipment ? EntityManager.GetBuffer<ActorEquipmentSlot>(player, true) : default;
            var groupEntries = new List<SpellWindowEntryViewModel>();
            for (int i = 0; i < inventory.Length; i++)
            {
                var item = inventory[i];
                if (item.Count <= 0 || item.Content.Kind != ContentReferenceKind.Item)
                    continue;
                ref RuntimeBaseDefBlob itemDef = ref RuntimeContentBlobUtility.Get(ref contentBlob, new ItemDefHandle { Value = item.Content.HandleValue });
                if (itemDef.EnchantIdHash == 0UL)
                    continue;
                bool hasItemEquipment = RuntimeContentBlobUtility.TryGetItemEquipment(ref contentBlob, item.Content, out var itemEquipment);
                bool isScrollRecord = itemDef.RecordTag == BookRecordTag;
                if (!hasItemEquipment && !isScrollRecord)
                    continue;
                if (!RuntimeContentBlobUtility.TryGetEnchantmentHandleByIdHash(ref contentBlob, itemDef.EnchantIdHash, out var enchantmentHandle))
                    throw new InvalidOperationException($"[VVardenfell][Magic] Item {itemDef.Id.ToString()} references missing enchantment {itemDef.EnchantId.ToString()}.");
                ref RuntimeEnchantmentDefBlob enchantment = ref RuntimeContentBlobUtility.Get(ref contentBlob, enchantmentHandle);
                if (!MorrowindEnchantmentUtility.IsUsableMagicItemType(enchantment.EnchantmentType))
                    continue;
                if (enchantment.EnchantmentType == MorrowindEnchantmentUtility.WhenUsed && !hasItemEquipment)
                    continue;
                if (enchantment.EnchantmentType == MorrowindEnchantmentUtility.WhenUsed && !CanEquipMagicItem(ref contentBlob, playerStats, itemEquipment))
                    continue;
                if (enchantment.EnchantmentType == MorrowindEnchantmentUtility.CastOnce && !isScrollRecord)
                    throw new InvalidOperationException($"[VVardenfell][Magic] Cast-once enchantment {enchantment.Id.ToString()} is attached to non-scroll item {itemDef.Id.ToString()}.");
                if (!MatchesEnchantmentFilter(ref contentBlob, ref itemDef, ref enchantment, filter))
                    continue;

                bool equipped = hasEquipment && IsEquipped(equipment, i, item.Content);
                int castCost = enchantment.EnchantmentType == MorrowindEnchantmentUtility.CastOnce
                    ? 100
                    : MorrowindEnchantmentUtility.CalculateCastCost(ref contentBlob, ref enchantment, playerStats.Skills);
                float maxCharge = enchantment.EnchantmentType == MorrowindEnchantmentUtility.CastOnce
                    ? 100
                    : MorrowindEnchantmentUtility.CalculateCharge(ref contentBlob, ref enchantment);
                float currentCharge = enchantment.EnchantmentType == MorrowindEnchantmentUtility.CastOnce
                    ? maxCharge
                    : MorrowindEnchantmentUtility.ResolveCurrentCharge(ref contentBlob, ref enchantment, item.EnchantmentCharge);
                int charge = (int)currentCharge;
                string name = RuntimeContentMetadataResolver.ResolveDisplayName(ref itemDef, "Unknown item");
                groupEntries.Add(new SpellWindowEntryViewModel
                {
                    SpellIndex = -1,
                    SourceKind = RuntimeMagicSourceKind.EnchantedItem,
                    InventoryIndex = i,
                    ItemContent = item.Content,
                    Enchantment = enchantmentHandle,
                    Name = item.Count > 1 ? $"{name} ({item.Count})" : name,
                    CostText = $"{castCost}/{charge}",
                    TypeText = enchantment.EnchantmentType == MorrowindEnchantmentUtility.CastOnce ? "Cast Once" : "Magic Item",
                    EffectTooltipText = BuildEnchantmentEffectTooltip(ref contentBlob, ref enchantment),
                    Count = item.Count,
                    Active = enchantment.EnchantmentType == MorrowindEnchantmentUtility.WhenUsed && equipped,
                    ShowChargeBar = true,
                    ChargeFillNormalized = maxCharge > 0f ? Math.Clamp(currentCharge / maxCharge, 0f, 1f) : 0f,
                    Selected = state.SelectedSourceKind == (byte)RuntimeMagicSourceKind.EnchantedItem
                               && state.SelectedInventoryIndex == i
                               && state.SelectedItemContent.Kind == item.Content.Kind
                               && state.SelectedItemContent.HandleValue == item.Content.HandleValue
                               && state.SelectedEnchantment.Value == enchantmentHandle.Value,
                });
            }

            AddSortedGroup(entries, RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, "sMagicItem", "Magic Item"), RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, "sCostCharge", "Cost/Charge"), groupEntries);
        }

        static int CountSelectableEntries(List<SpellWindowEntryViewModel> entries)
        {
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
                if (!entries[i].IsGroupHeader)
                    count++;
            return count;
        }

        static void AddSortedGroup(List<SpellWindowEntryViewModel> entries, string groupName, string rightHeader, List<SpellWindowEntryViewModel> groupEntries)
        {
            if (groupEntries.Count == 0)
                return;
            groupEntries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            entries.Add(new SpellWindowEntryViewModel { IsGroupHeader = true, HasGroupSeparator = entries.Count > 0, Active = true, Name = groupName, CostText = rightHeader });
            entries.AddRange(groupEntries);
        }

        string ResolveSelectedMagicTitle(ref RuntimeContentBlob contentBlob, in SpellWindowState state)
        {
            if (state.SelectedSourceKind == (byte)RuntimeMagicSourceKind.Spell
                && state.SelectedSpell.IsValid
                && state.SelectedSpell.Index >= 0
                && state.SelectedSpell.Index < contentBlob.Spells.Length)
            {
                ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref contentBlob, state.SelectedSpell);
                return RuntimeContentMetadataResolver.ResolveSpellName(ref spell);
            }

            if (state.SelectedSourceKind == (byte)RuntimeMagicSourceKind.EnchantedItem
                && state.SelectedItemContent.Kind == ContentReferenceKind.Item
                && state.SelectedItemContent.HandleValue > 0)
            {
                ref RuntimeBaseDefBlob item = ref RuntimeContentBlobUtility.Get(ref contentBlob, new ItemDefHandle { Value = state.SelectedItemContent.HandleValue });
                return RuntimeContentMetadataResolver.ResolveDisplayName(ref item, "Magic Item");
            }

            return "None";
        }

        static bool MatchesSpellFilter(ref RuntimeContentBlob contentBlob, ref RuntimeSpellDefBlob spell, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            string needle = filter.Trim();
            string name = spell.Name.ToString();
            if (!string.IsNullOrWhiteSpace(name) && name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return SpellEffectsMatchFilter(ref contentBlob, spell.EffectStartIndex, spell.EffectCount, needle);
        }

        static int ResolveSpellChance(
            ref RuntimeContentBlob contentBlob,
            ref RuntimeSpellDefBlob spell,
            in PlayerPresentationStats playerStats,
            DynamicBuffer<ActorActiveMagicEffect> activeEffects)
            => (int)Math.Clamp(MorrowindSpellCostUtility.CalculateSuccessChance(
                ref contentBlob,
                ref spell,
                playerStats.Attributes,
                playerStats.Skills,
                playerStats.Vitals,
                playerStats.DerivedMovement,
                activeEffects,
                checkMagicka: false,
                out _), 0f, 100f);

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

        static bool MatchesEnchantmentFilter(
            ref RuntimeContentBlob contentBlob,
            ref RuntimeBaseDefBlob item,
            ref RuntimeEnchantmentDefBlob enchantment,
            string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            string needle = filter.Trim();
            string name = item.Name.ToString();
            if (!string.IsNullOrWhiteSpace(name) && name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return SpellEffectsMatchFilter(ref contentBlob, enchantment.EffectStartIndex, enchantment.EffectCount, needle);
        }

        static bool SpellEffectsMatchFilter(ref RuntimeContentBlob contentBlob, int effectStartIndex, int effectCount, string needle)
        {
            if (effectStartIndex < 0 || effectCount <= 0)
                return false;
            int available = Math.Max(0, contentBlob.MagicEffectInstances.Length - effectStartIndex);
            int count = Math.Min(effectCount, available);
            for (int i = 0; i < count; i++)
            {
                var effect = contentBlob.MagicEffectInstances[effectStartIndex + i];
                string name = BuildSpellTooltipEffectText(ref contentBlob, effect);
                if (!string.IsNullOrWhiteSpace(name) && name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        bool CanEquipMagicItem(ref RuntimeContentBlob contentBlob, in PlayerPresentationStats playerStats, in ItemEquipmentDef equipment)
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

            if (!playerStats.HasPlayer
                || !EntityManager.Exists(playerStats.PlayerEntity)
                || !EntityManager.HasComponent<PlayerRaceAppearance>(playerStats.PlayerEntity))
            {
                throw new InvalidOperationException("[VVardenfell][Magic] Magic item equip filtering requires player race appearance.");
            }

            PlayerRaceAppearance appearance = EntityManager.GetComponentData<PlayerRaceAppearance>(playerStats.PlayerEntity);
            if (ActorEquipmentRuntimeUtility.IsBeastRace(ref contentBlob, RuntimeContentStableHash.HashId(appearance.RaceId.ToString()))
                && HasBeastForbiddenPart(ref contentBlob, equipment))
            {
                return false;
            }

            return true;
        }

        static bool HasBeastForbiddenPart(ref RuntimeContentBlob contentBlob, in ItemEquipmentDef equipment)
        {
            RuntimeContentBlobUtility.RequireRange(equipment.FirstBodyPartIndex, equipment.BodyPartCount, contentBlob.ItemEquipmentBodyParts.Length, "item equipment body part");
            for (int i = 0; i < equipment.BodyPartCount; i++)
            {
                var part = contentBlob.ItemEquipmentBodyParts[equipment.FirstBodyPartIndex + i].Part;
                if (part == ItemEquipmentPartReference.Head
                    || part == ItemEquipmentPartReference.LeftFoot
                    || part == ItemEquipmentPartReference.RightFoot)
                {
                    return true;
                }
            }

            return false;
        }

        static SpellWindowEffectRow[] BuildSpellEffectRows(ref RuntimeContentBlob contentBlob, ref RuntimeSpellDefBlob spell)
        {
            if (spell.EffectStartIndex < 0 || spell.EffectCount <= 0)
                return Array.Empty<SpellWindowEffectRow>();

            int available = Math.Max(0, contentBlob.MagicEffectInstances.Length - spell.EffectStartIndex);
            int count = Math.Min(spell.EffectCount, available);
            var rows = new SpellWindowEffectRow[count];
            for (int i = 0; i < count; i++)
            {
                var effect = contentBlob.MagicEffectInstances[spell.EffectStartIndex + i];
                rows[i] = new SpellWindowEffectRow
                {
                    EffectId = effect.EffectId,
                    Name = RuntimeContentMetadataResolver.ResolveMagicEffectName(ref contentBlob, effect.EffectId),
                    DetailText = BuildEffectDetail(ref contentBlob, effect),
                    IconPath = RuntimeContentMetadataResolver.ResolveMagicEffectIconPath(ref contentBlob, effect.EffectId),
                };
            }

            return rows;
        }

        static SpellWindowEffectRow[] BuildEnchantmentEffectRows(ref RuntimeContentBlob contentBlob, ref RuntimeEnchantmentDefBlob enchantment)
        {
            if (enchantment.EffectStartIndex < 0 || enchantment.EffectCount <= 0)
                return Array.Empty<SpellWindowEffectRow>();

            int available = Math.Max(0, contentBlob.MagicEffectInstances.Length - enchantment.EffectStartIndex);
            int count = Math.Min(enchantment.EffectCount, available);
            var rows = new SpellWindowEffectRow[count];
            for (int i = 0; i < count; i++)
            {
                var effect = contentBlob.MagicEffectInstances[enchantment.EffectStartIndex + i];
                rows[i] = new SpellWindowEffectRow
                {
                    EffectId = effect.EffectId,
                    Name = RuntimeContentMetadataResolver.ResolveMagicEffectName(ref contentBlob, effect.EffectId),
                    DetailText = BuildEffectDetail(ref contentBlob, effect),
                    IconPath = RuntimeContentMetadataResolver.ResolveMagicEffectIconPath(ref contentBlob, effect.EffectId),
                };
            }

            return rows;
        }

        RuntimeMagicEffectIconViewModel[] BuildActiveEffectIcons(ref RuntimeContentBlob contentBlob, in PlayerPresentationStats playerStats)
        {
            if (!playerStats.HasPlayer
                || !EntityManager.Exists(playerStats.PlayerEntity)
                || !EntityManager.HasBuffer<ActorActiveMagicEffect>(playerStats.PlayerEntity))
            {
                return Array.Empty<RuntimeMagicEffectIconViewModel>();
            }

            return BuildActiveEffectIcons(ref contentBlob, EntityManager.GetBuffer<ActorActiveMagicEffect>(playerStats.PlayerEntity, true));
        }

        RuntimeMagicEffectIconViewModel[] BuildHudActiveEffectIcons(ref RuntimeContentBlob contentBlob, in PlayerPresentationStats playerStats)
        {
            if (!playerStats.HasPlayer
                || !EntityManager.Exists(playerStats.PlayerEntity)
                || !EntityManager.HasBuffer<ActorActiveMagicEffect>(playerStats.PlayerEntity))
            {
                _hasCachedHudActiveEffectSignature = false;
                _cachedHudActiveEffects = Array.Empty<RuntimeMagicEffectIconViewModel>();
                return _cachedHudActiveEffects;
            }

            var activeEffects = EntityManager.GetBuffer<ActorActiveMagicEffect>(playerStats.PlayerEntity, true);
            ulong signature = ComputeActiveEffectSignature(activeEffects);
            if (signature == 0UL)
            {
                _hasCachedHudActiveEffectSignature = false;
                _cachedHudActiveEffects = Array.Empty<RuntimeMagicEffectIconViewModel>();
                return _cachedHudActiveEffects;
            }

            if (!_hasCachedHudActiveEffectSignature || signature != _cachedHudActiveEffectSignature)
            {
                _cachedHudActiveEffects = BuildActiveEffectIcons(ref contentBlob, activeEffects);
                _cachedHudActiveEffectSignature = signature;
                _hasCachedHudActiveEffectSignature = true;
                return _cachedHudActiveEffects;
            }

            UpdateCachedActiveEffectAlpha(ref contentBlob, activeEffects, _cachedHudActiveEffects);
            return _cachedHudActiveEffects;
        }

        static ulong ComputeActiveEffectSignature(DynamicBuffer<ActorActiveMagicEffect> activeEffects)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            int count = 0;

            for (int i = 0; i < activeEffects.Length; i++)
            {
                var active = activeEffects[i];
                if (active.Applied == 0)
                    continue;
                if (active.DurationSeconds >= 0f && active.TimeLeftSeconds <= 0f)
                    continue;

                count++;
                hash = (hash ^ (ushort)active.EffectId) * prime;
                hash = (hash ^ (byte)active.Skill) * prime;
                hash = (hash ^ (byte)active.Attribute) * prime;
                hash = (hash ^ (byte)active.SourceKind) * prime;
                hash = (hash ^ (uint)active.SourceName.GetHashCode()) * prime;
                hash = (hash ^ (uint)active.SourceId.GetHashCode()) * prime;
                hash = (hash ^ (uint)active.Magnitude.GetHashCode()) * prime;
                hash = (hash ^ (uint)active.DurationSeconds.GetHashCode()) * prime;
            }

            return count == 0 ? 0UL : (hash ^ (uint)count) * prime;
        }

        static void UpdateCachedActiveEffectAlpha(
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<ActorActiveMagicEffect> activeEffects,
            RuntimeMagicEffectIconViewModel[] cachedEffects)
        {
            if (cachedEffects == null || cachedEffects.Length == 0)
                return;

            float fadeTime = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref contentBlob, RuntimeContentKnownHashes.fMagicStartIconBlink);

            for (int i = 0; i < cachedEffects.Length; i++)
            {
                var cached = cachedEffects[i];
                if (cached == null)
                    continue;

                float lowestFadeTimeLeft = float.PositiveInfinity;
                for (int j = 0; j < activeEffects.Length; j++)
                {
                    var active = activeEffects[j];
                    if (active.Applied == 0 || active.EffectId != cached.EffectId)
                        continue;
                    if (active.DurationSeconds >= 0f && active.TimeLeftSeconds <= 0f)
                        continue;
                    if (active.DurationSeconds >= 0f && active.TimeLeftSeconds >= 0f)
                        lowestFadeTimeLeft = Math.Min(lowestFadeTimeLeft, active.TimeLeftSeconds);
                }

                cached.Alpha = fadeTime <= 0f || float.IsPositiveInfinity(lowestFadeTimeLeft)
                    ? 1f
                    : Math.Clamp(lowestFadeTimeLeft / fadeTime, 0f, 1f);
            }
        }

        static RuntimeMagicEffectIconViewModel[] BuildActiveEffectIcons(
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<ActorActiveMagicEffect> activeEffects)
        {
            if (activeEffects.Length == 0)
                return Array.Empty<RuntimeMagicEffectIconViewModel>();

            var ordered = new List<short>();
            var groups = new Dictionary<short, ActiveEffectIconGroup>();
            for (int i = 0; i < activeEffects.Length; i++)
            {
                var active = activeEffects[i];
                if (active.Applied == 0)
                    continue;
                if (active.DurationSeconds >= 0f && active.TimeLeftSeconds <= 0f)
                    continue;

                if (!groups.TryGetValue(active.EffectId, out var group))
                {
                    group = new ActiveEffectIconGroup(active.EffectId);
                    groups.Add(active.EffectId, group);
                    ordered.Add(active.EffectId);
                }

                group.Add(ref contentBlob, active);
            }

            if (ordered.Count == 0)
                return Array.Empty<RuntimeMagicEffectIconViewModel>();

            float fadeTime = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref contentBlob, RuntimeContentKnownHashes.fMagicStartIconBlink);

            var result = new RuntimeMagicEffectIconViewModel[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                short effectId = ordered[i];
                var group = groups[effectId];
                string displayName = RuntimeContentMetadataResolver.ResolveMagicEffectName(ref contentBlob, effectId);
                var descriptionLines = group.BuildDescriptionLines(displayName);
                string iconPath = RuntimeContentMetadataResolver.ResolveMagicEffectIconPath(ref contentBlob, effectId);
                result[i] = new RuntimeMagicEffectIconViewModel
                {
                    EffectId = effectId,
                    IconPath = iconPath,
                    DisplayName = displayName,
                    TooltipText = BuildActiveEffectPlainTooltip(displayName, descriptionLines),
                    Tooltip = new RuntimeMagicEffectTooltipViewModel
                    {
                        IconPath = iconPath,
                        DisplayName = displayName,
                        DescriptionLines = descriptionLines,
                    },
                    Alpha = group.ComputeAlpha(fadeTime),
                    SourceLines = descriptionLines,
                };
            }

            return result;
        }

        sealed class ActiveEffectIconGroup
        {
            readonly short _effectId;
            readonly List<string> _sourceLines = new();
            float _lowestFadeTimeLeft = float.PositiveInfinity;

            public ActiveEffectIconGroup(short effectId)
            {
                _effectId = effectId;
            }

            public void Add(ref RuntimeContentBlob contentBlob, in ActorActiveMagicEffect active)
            {
                string source = active.SourceName.ToString();
                if (string.IsNullOrWhiteSpace(source))
                    source = active.SourceId.ToString();
                if (string.IsNullOrWhiteSpace(source))
                    source = $"Effect {_effectId}";

                _sourceLines.Add(BuildActiveEffectSourceLine(ref contentBlob, _effectId, active, source));
                if (active.DurationSeconds >= 0f && active.TimeLeftSeconds >= 0f)
                    _lowestFadeTimeLeft = Math.Min(_lowestFadeTimeLeft, active.TimeLeftSeconds);
            }

            public float ComputeAlpha(float fadeTime)
            {
                if (fadeTime <= 0f || float.IsPositiveInfinity(_lowestFadeTimeLeft))
                    return 1f;

                return Math.Clamp(_lowestFadeTimeLeft / fadeTime, 0f, 1f);
            }

            public string[] BuildDescriptionLines(string displayName)
                => CollapseRedundantDescriptionLines(_sourceLines, displayName);
        }

        static RuntimeSpellTooltipViewModel BuildSpellTooltip(ref RuntimeContentBlob contentBlob, ref RuntimeSpellDefBlob spell)
        {
            string title = RuntimeContentMetadataResolver.ResolveSpellName(ref spell);
            var effects = BuildSpellTooltipEffects(ref contentBlob, ref spell);
            return new RuntimeSpellTooltipViewModel
            {
                Title = title,
                SchoolText = spell.SpellType == 0 ? BuildSpellSchoolText(ref contentBlob, ref spell) : null,
                Effects = effects,
            };
        }

        static RuntimeSpellTooltipEffectRow[] BuildSpellTooltipEffects(ref RuntimeContentBlob contentBlob, ref RuntimeSpellDefBlob spell)
        {
            if (spell.EffectStartIndex < 0 || spell.EffectCount <= 0)
                return Array.Empty<RuntimeSpellTooltipEffectRow>();

            int available = Math.Max(0, contentBlob.MagicEffectInstances.Length - spell.EffectStartIndex);
            int count = Math.Min(spell.EffectCount, available);
            var rows = new RuntimeSpellTooltipEffectRow[count];
            for (int i = 0; i < count; i++)
            {
                var effect = contentBlob.MagicEffectInstances[spell.EffectStartIndex + i];
                rows[i] = new RuntimeSpellTooltipEffectRow
                {
                    EffectId = effect.EffectId,
                    IconPath = RuntimeContentMetadataResolver.ResolveMagicEffectIconPath(ref contentBlob, effect.EffectId),
                    Text = BuildSpellTooltipEffectText(ref contentBlob, effect),
                };
            }

            return rows;
        }

        static string BuildSpellSchoolText(ref RuntimeContentBlob contentBlob, ref RuntimeSpellDefBlob spell)
        {
            if (spell.EffectStartIndex < 0 || spell.EffectCount <= 0)
                return null;

            int available = Math.Max(0, contentBlob.MagicEffectInstances.Length - spell.EffectStartIndex);
            if (available <= 0)
                return null;

            short effectId = contentBlob.MagicEffectInstances[spell.EffectStartIndex].EffectId;
            int school = -1;
            if (RuntimeContentBlobUtility.TryGetMagicEffectHandleByIndex(ref contentBlob, effectId, out var handle))
            {
                ref RuntimeMagicEffectDefBlob def = ref RuntimeContentBlobUtility.Get(ref contentBlob, handle);
                school = def.School;
            }

            string schoolName = RuntimeContentMetadataResolver.ResolveSchoolName(ref contentBlob, school);
            string schoolLabel = RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, "sSchool", "School");
            return string.IsNullOrWhiteSpace(schoolName) ? null : $"{schoolLabel}: {schoolName}";
        }

        static string BuildEffectDetail(ref RuntimeContentBlob contentBlob, in MagicEffectInstanceDef effect)
        {
            var parts = new List<string>(4);
            if (effect.MagnitudeMin != 0 || effect.MagnitudeMax != 0)
                parts.Add(effect.MagnitudeMin == effect.MagnitudeMax
                    ? $"{effect.MagnitudeMin} {Pluralize(effect.MagnitudeMin, "pt", "pts")}"
                    : $"{effect.MagnitudeMin} to {effect.MagnitudeMax} pts");
            if (effect.Duration > 0)
                parts.Add($"for {effect.Duration} {Pluralize(effect.Duration, "sec", "secs")}");
            if (effect.Area > 0)
                parts.Add($"in {effect.Area} ft");
            parts.Add(effect.Range switch
            {
                0 => "on Self",
                1 => "on Touch",
                2 => "on Target",
                _ => "range ?",
            });
            return string.Join(" ", parts);
        }

        static string BuildSpellTooltipEffectText(ref RuntimeContentBlob contentBlob, in MagicEffectInstanceDef effect)
        {
            string name = RuntimeContentMetadataResolver.ResolveMagicEffectName(ref contentBlob, effect.EffectId);
            string argument = ResolveEffectArgumentName(effect);
            if (!string.IsNullOrWhiteSpace(argument))
                name = $"{name} {argument}";

            string detail = BuildEffectDetail(ref contentBlob, effect);
            return string.IsNullOrWhiteSpace(detail) ? name : $"{name} {detail}";
        }

        static string BuildSpellEffectTooltip(ref RuntimeContentBlob contentBlob, ref RuntimeSpellDefBlob spell)
        {
            var effects = BuildSpellEffectRows(ref contentBlob, ref spell);
            return BuildEffectRowsTooltip(effects);
        }

        static string BuildEnchantmentEffectTooltip(ref RuntimeContentBlob contentBlob, ref RuntimeEnchantmentDefBlob enchantment)
        {
            var effects = BuildEnchantmentEffectRows(ref contentBlob, ref enchantment);
            return BuildEffectRowsTooltip(effects);
        }

        static string BuildEffectRowsTooltip(SpellWindowEffectRow[] effects)
        {
            if (effects.Length == 0)
                return string.Empty;

            var lines = new List<string>(effects.Length);
            for (int i = 0; i < effects.Length; i++)
            {
                string name = string.IsNullOrWhiteSpace(effects[i].Name) ? "Effect" : effects[i].Name.Trim();
                string detail = string.IsNullOrWhiteSpace(effects[i].DetailText) ? string.Empty : effects[i].DetailText.Trim();
                lines.Add(string.IsNullOrEmpty(detail) ? name : $"{name} {detail}");
            }

            return string.Join("\n", lines);
        }

        static string ResolveEffectArgumentName(in MagicEffectInstanceDef effect)
        {
            if (effect.Attribute >= 0)
                return RuntimeContentMetadataResolver.ResolveAttributeName(effect.Attribute);
            if (effect.Skill >= 0)
                return RuntimeContentMetadataResolver.ResolveSkillName(effect.Skill);
            return string.Empty;
        }

        static string Pluralize(int value, string singular, string plural)
            => Math.Abs(value) == 1 ? singular : plural;

        static string BuildActiveEffectSourceLine(
            ref RuntimeContentBlob contentBlob,
            short effectId,
            in ActorActiveMagicEffect active,
            string source)
        {
            string displayName = RuntimeContentMetadataResolver.ResolveMagicEffectName(ref contentBlob, effectId);
            string line = string.IsNullOrWhiteSpace(source)
                ? displayName
                : source.Trim();
            string sourceLabel = line;
            string detail = string.Empty;

            if (RuntimeContentMetadataResolver.TryGetMagicEffectFlags(ref contentBlob, effectId, out int flags))
            {
                if ((flags & MagicEffectFlagTargetSkill) != 0 && active.Skill >= 0)
                {
                    string skillName = RuntimeContentMetadataResolver.ResolveSkillName(active.Skill);
                    if (!string.IsNullOrWhiteSpace(skillName))
                        line += $" ({skillName})";
                }

                if ((flags & MagicEffectFlagTargetAttribute) != 0 && active.Attribute >= 0)
                {
                    string attributeName = RuntimeContentMetadataResolver.ResolveAttributeName(active.Attribute);
                    if (!string.IsNullOrWhiteSpace(attributeName))
                        line += $" ({attributeName})";
                }

                sourceLabel = line;
                detail = FormatActiveEffectMagnitude(ref contentBlob, effectId, active.Magnitude, flags).TrimStart(':', ' ');
            }
            else if (Math.Abs(active.Magnitude) > 0.0001f)
            {
                detail = $"{(int)active.Magnitude} {RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, Math.Abs((int)active.Magnitude) == 1 ? "sPoint" : "sPoints", "pts")}";
            }

            if (active.DurationSeconds >= 0f && active.TimeLeftSeconds >= 0f)
                detail = string.IsNullOrWhiteSpace(detail)
                    ? BuildActiveEffectDuration(ref contentBlob, active.TimeLeftSeconds).Trim()
                    : $"{detail} {BuildActiveEffectDuration(ref contentBlob, active.TimeLeftSeconds).Trim()}";

            if (string.Equals(sourceLabel, displayName, StringComparison.OrdinalIgnoreCase))
                return detail;
            return string.IsNullOrWhiteSpace(detail) ? sourceLabel : $"{sourceLabel}: {detail}";
        }

        static string[] CollapseRedundantDescriptionLines(List<string> lines, string displayName)
        {
            if (lines == null || lines.Count == 0)
                return Array.Empty<string>();

            var result = new List<string>(lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i]?.Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                if (string.Equals(line, displayName, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(line);
            }

            return result.ToArray();
        }

        static string BuildActiveEffectPlainTooltip(string displayName, string[] descriptionLines)
        {
            var lines = new List<string>(1 + (descriptionLines?.Length ?? 0));
            if (!string.IsNullOrWhiteSpace(displayName))
                lines.Add(displayName.Trim());
            if (descriptionLines != null)
            {
                for (int i = 0; i < descriptionLines.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(descriptionLines[i]))
                        lines.Add(descriptionLines[i].Trim());
                }
            }

            return string.Join("\n", lines);
        }

        static string FormatActiveEffectMagnitude(ref RuntimeContentBlob contentBlob, short effectId, float magnitude, int flags)
        {
            var displayType = ResolveMagnitudeDisplayType(effectId, flags);
            if (displayType == ActiveEffectMagnitudeDisplayType.None)
                return string.Empty;

            int integerMagnitude = (int)magnitude;
            if (displayType == ActiveEffectMagnitudeDisplayType.TimesInt)
            {
                string unit = RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, "sXTimesINT", "x INT");
                return $" {(integerMagnitude / 10f):0.0}{unit}";
            }

            string result = $": {integerMagnitude}";
            if (displayType != ActiveEffectMagnitudeDisplayType.Percentage)
                result += " ";

            result += displayType switch
            {
                ActiveEffectMagnitudeDisplayType.Feet => RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, "sFeet", "ft"),
                ActiveEffectMagnitudeDisplayType.Level => RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, Math.Abs(integerMagnitude) == 1 ? "sLevel" : "sLevels", "levels"),
                ActiveEffectMagnitudeDisplayType.Percentage => RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, "sPercent", "%"),
                _ => RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, Math.Abs(integerMagnitude) == 1 ? "sPoint" : "sPoints", "pts"),
            };

            return result;
        }

        static string BuildActiveEffectDuration(ref RuntimeContentBlob contentBlob, float timeLeftSeconds)
        {
            int seconds = Math.Max(0, (int)Math.Ceiling(timeLeftSeconds));
            string durationLabel = RuntimeContentMetadataResolver.ResolveGameSettingString(ref contentBlob, "sDuration", "Duration");
            return $" {durationLabel}: {seconds}";
        }

        enum ActiveEffectMagnitudeDisplayType
        {
            None,
            Feet,
            Level,
            Percentage,
            Points,
            TimesInt,
        }

        static ActiveEffectMagnitudeDisplayType ResolveMagnitudeDisplayType(short effectId, int flags)
        {
            if ((flags & MagicEffectFlagNoMagnitude) != 0)
                return ActiveEffectMagnitudeDisplayType.None;
            if (effectId == 84)
                return ActiveEffectMagnitudeDisplayType.TimesInt;
            if (effectId == 59 || effectId is >= 64 and <= 66)
                return ActiveEffectMagnitudeDisplayType.Feet;
            if (effectId == 118 || effectId == 119)
                return ActiveEffectMagnitudeDisplayType.Level;
            if (effectId is >= 28 and <= 36
                || effectId is >= 90 and <= 99
                || effectId == 40
                || effectId == 47
                || effectId == 57
                || effectId == 68)
            {
                return ActiveEffectMagnitudeDisplayType.Percentage;
            }

            return ActiveEffectMagnitudeDisplayType.Points;
        }
    }
}

