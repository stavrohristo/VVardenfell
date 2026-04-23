using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Inventory
{
    public readonly struct CarryableMetadata
    {
        public readonly ContentReferenceKind Kind;
        public readonly uint RecordTag;
        public readonly string DisplayName;
        public readonly string IconPath;
        public readonly float Weight;
        public readonly int Value;
        public readonly bool HasMagicCategory;

        public CarryableMetadata(
            ContentReferenceKind kind,
            uint recordTag,
            string displayName,
            string iconPath,
            float weight,
            int value,
            bool hasMagicCategory)
        {
            Kind = kind;
            RecordTag = recordTag;
            DisplayName = displayName;
            IconPath = iconPath;
            Weight = weight;
            Value = value;
            HasMagicCategory = hasMagicCategory;
        }
    }

    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellStateSystem))]
    [UpdateBefore(typeof(RuntimeShellInputSystem))]
    public partial class InventoryWindowStateSystem : SystemBase
    {
        static readonly uint WeapTag = MakeTag('W', 'E', 'A', 'P');
        static readonly uint ArmoTag = MakeTag('A', 'R', 'M', 'O');
        static readonly uint ClotTag = MakeTag('C', 'L', 'O', 'T');
        static readonly uint BookTag = MakeTag('B', 'O', 'O', 'K');
        static readonly uint AlchTag = MakeTag('A', 'L', 'C', 'H');
        static readonly uint AppaTag = MakeTag('A', 'P', 'P', 'A');
        static readonly uint IngrTag = MakeTag('I', 'N', 'G', 'R');

        protected override void OnCreate()
        {
            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<InventoryWindowState>();
            RequireForUpdate<InventoryWindowRequest>();
            RequireForUpdate<PlayerInventoryItem>();
        }

        protected override void OnUpdate()
        {
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var state = ref SystemAPI.GetSingletonRW<InventoryWindowState>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<InventoryWindowRequest>().ValueRW;
            var inventory = SystemAPI.GetSingletonBuffer<PlayerInventoryItem>();
            var contentDb = RuntimeContentDatabase.Active;

            ApplyRequests(ref state, ref request);

            state.Visible = shell.InventoryOpen;

            if (state.Visible == 0)
            {
                state.SelectedItemDetailsText = default;
                return;
            }

            state.ActiveCategory = (byte)ClampCategory((InventoryWindowCategory)state.ActiveCategory);

            int selectedIndex = ValidateSelectedIndex(contentDb, inventory, ref state);
            if (selectedIndex < 0)
                selectedIndex = FindFirstVisibleInventoryIndex(contentDb, inventory, state);

            state.SelectedInventoryIndex = selectedIndex;
            state.SelectedItemDetailsText = ToFixedDetails(BuildSelectedItemDetails(contentDb, inventory, selectedIndex));
        }

        static void ApplyRequests(ref InventoryWindowState state, ref InventoryWindowRequest request)
        {
            if (request.PendingRectUpdate != 0)
            {
                state.NormalizedX = Clamp01(request.NormalizedX);
                state.NormalizedY = Clamp01(request.NormalizedY);
                state.NormalizedWidth = ClampDimension(request.NormalizedWidth, state.NormalizedWidth);
                state.NormalizedHeight = ClampDimension(request.NormalizedHeight, state.NormalizedHeight);

                if (state.NormalizedX + state.NormalizedWidth > 1f)
                    state.NormalizedX = Math.Max(0f, 1f - state.NormalizedWidth);
                if (state.NormalizedY + state.NormalizedHeight > 1f)
                    state.NormalizedY = Math.Max(0f, 1f - state.NormalizedHeight);
            }

            if (request.PendingCategoryChange != 0)
                state.ActiveCategory = (byte)ClampCategory((InventoryWindowCategory)request.ActiveCategory);

            if (request.PendingFilterTextChange != 0)
                state.FilterText = request.FilterText;

            if (request.PendingSelectionChange != 0)
                state.SelectedInventoryIndex = request.SelectedInventoryIndex;

            request = default;
        }

        static int ValidateSelectedIndex(RuntimeContentDatabase contentDb, DynamicBuffer<PlayerInventoryItem> inventory, ref InventoryWindowState state)
        {
            int selectedIndex = state.SelectedInventoryIndex;
            if (selectedIndex < 0 || selectedIndex >= inventory.Length)
                return -1;

            return MatchesFilters(contentDb, inventory[selectedIndex], state) ? selectedIndex : -1;
        }

        static int FindFirstVisibleInventoryIndex(RuntimeContentDatabase contentDb, DynamicBuffer<PlayerInventoryItem> inventory, in InventoryWindowState state)
        {
            for (int i = 0; i < inventory.Length; i++)
            {
                if (MatchesFilters(contentDb, inventory[i], state))
                    return i;
            }

            return -1;
        }

        public static bool MatchesFilters(RuntimeContentDatabase contentDb, PlayerInventoryItem entry, in InventoryWindowState state)
        {
            if (contentDb == null || !entry.Content.IsValid)
                return false;

            if (!TryResolveCarryableMetadata(contentDb, entry.Content, out var metadata))
                return false;

            if (!MatchesCategory(metadata, (InventoryWindowCategory)state.ActiveCategory))
                return false;

            string filter = state.FilterText.ToString();
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            return metadata.DisplayName.IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool TryResolveCarryableMetadata(
            RuntimeContentDatabase contentDb,
            ContentReference content,
            out CarryableMetadata metadata)
        {
            metadata = default;
            if (contentDb == null || !content.IsValid)
                return false;

            switch (content.Kind)
            {
                case ContentReferenceKind.Item:
                {
                    var handle = new ItemDefHandle { Value = content.HandleValue };
                    if (!handle.IsValid)
                        return false;

                    ref readonly var item = ref contentDb.Get(handle);
                    metadata = new CarryableMetadata(
                        ContentReferenceKind.Item,
                        item.RecordTag,
                        ResolveDisplayName(item),
                        item.Icon ?? string.Empty,
                        item.Float0 > 0f ? item.Float0 : -1f,
                        item.Int0 > 0 ? item.Int0 : -1,
                        HasMagicCategory(item));
                    return true;
                }
                case ContentReferenceKind.Light:
                {
                    var handle = new LightDefHandle { Value = content.HandleValue };
                    if (!handle.IsValid)
                        return false;

                    ref readonly var light = ref contentDb.Get(handle);
                    metadata = new CarryableMetadata(
                        ContentReferenceKind.Light,
                        light.RecordTag,
                        ResolveDisplayName(light),
                        light.Icon ?? string.Empty,
                        light.Weight > 0f ? light.Weight : -1f,
                        light.Value > 0 ? light.Value : -1,
                        false);
                    return true;
                }
                default:
                    return false;
            }
        }

        static bool MatchesCategory(in CarryableMetadata metadata, InventoryWindowCategory category)
        {
            if (metadata.Kind == ContentReferenceKind.Light)
                return category == InventoryWindowCategory.All || category == InventoryWindowCategory.Misc;

            return category switch
            {
                InventoryWindowCategory.All => true,
                InventoryWindowCategory.Weapons => metadata.RecordTag == WeapTag,
                InventoryWindowCategory.Apparel => metadata.RecordTag == ArmoTag || metadata.RecordTag == ClotTag,
                InventoryWindowCategory.Magic => metadata.HasMagicCategory,
                InventoryWindowCategory.Misc => metadata.RecordTag != WeapTag
                    && metadata.RecordTag != ArmoTag
                    && metadata.RecordTag != ClotTag
                    && !metadata.HasMagicCategory,
                _ => true,
            };
        }

        static bool HasMagicCategory(in BaseDef item)
        {
            return item.RecordTag == AlchTag
                || item.RecordTag == AppaTag
                || item.RecordTag == IngrTag
                || item.RecordTag == BookTag
                || !string.IsNullOrWhiteSpace(item.EnchantId);
        }

        static string BuildSelectedItemDetails(RuntimeContentDatabase contentDb, DynamicBuffer<PlayerInventoryItem> inventory, int selectedIndex)
        {
            if (contentDb == null || selectedIndex < 0 || selectedIndex >= inventory.Length)
                return "Select an item to inspect.";

            var entry = inventory[selectedIndex];
            if (!TryResolveCarryableMetadata(contentDb, entry.Content, out var metadata))
                return "Select an item to inspect.";

            return BuildCarryableDetails(metadata, entry.Count);
        }

        public static string BuildCarryableDetails(in CarryableMetadata metadata, int count)
        {
            string countLabel = count > 1 ? $" x{count}" : string.Empty;
            string weightLabel = metadata.Weight >= 0f ? (metadata.Weight * count).ToString("0.0") : "--";
            string valueLabel = metadata.Value >= 0 ? (metadata.Value * count).ToString() : "--";
            return $"{metadata.DisplayName}{countLabel}   wt {weightLabel}   val {valueLabel}";
        }

        public static string ResolveDisplayName(in BaseDef item)
        {
            if (!string.IsNullOrWhiteSpace(item.Name))
                return item.Name.Trim();
            if (!string.IsNullOrWhiteSpace(item.Id))
                return item.Id.Trim();
            return "Unknown item";
        }

        public static string ResolveDisplayName(in LightDef light)
        {
            if (!string.IsNullOrWhiteSpace(light.Name))
                return light.Name.Trim();
            if (!string.IsNullOrWhiteSpace(light.Id))
                return light.Id.Trim();
            return "Unknown light";
        }

        static InventoryWindowCategory ClampCategory(InventoryWindowCategory category)
        {
            return category is >= InventoryWindowCategory.All and <= InventoryWindowCategory.Misc
                ? category
                : InventoryWindowCategory.All;
        }

        static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0f;
            return Math.Clamp(value, 0f, 1f);
        }

        static float ClampDimension(float requested, float fallback)
        {
            if (float.IsNaN(requested) || float.IsInfinity(requested) || requested <= 0f)
                requested = fallback > 0f ? fallback : 0.1f;

            return Math.Clamp(requested, 0.1f, 1f);
        }

        static FixedString512Bytes ToFixedDetails(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return default;

            if (value.Length > 511)
                value = value.Substring(0, 511);

            return new FixedString512Bytes(value);
        }

        static uint MakeTag(char a, char b, char c, char d)
            => (uint)a | ((uint)b << 8) | ((uint)c << 16) | ((uint)d << 24);
    }
}
