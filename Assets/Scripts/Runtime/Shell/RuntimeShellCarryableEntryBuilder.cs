using System;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.Shell
{
    static class RuntimeShellCarryableEntryBuilder
    {
        public static InventoryWindowEntryViewModel Build(
            ref RuntimeContentBlob contentBlob,
            ContentReference content,
            int count,
            int index,
            bool selected,
            bool equipped = false)
        {
            string name = "Unknown item";
            string iconPath = string.Empty;
            string weightText = "--";
            string valueText = "--";

            if (RuntimeContentMetadataResolver.TryResolveCarryable(ref contentBlob, content, out var metadata))
            {
                name = metadata.DisplayName;
                iconPath = metadata.IconPath ?? string.Empty;
                if (metadata.Weight >= 0f)
                    weightText = metadata.Weight.ToString("0.0");
                if (metadata.Value >= 0)
                    valueText = metadata.Value.ToString();
            }

            return new InventoryWindowEntryViewModel
            {
                InventoryIndex = index,
                Name = name,
                IconPath = iconPath,
                CountText = Math.Max(1, count).ToString(),
                WeightText = weightText,
                ValueText = valueText,
                SecondaryLeftText = $"wt {weightText}",
                SecondaryRightText = $"val {valueText}",
                EquippedText = equipped ? "Equipped" : string.Empty,
                Selected = selected,
                Equipped = equipped,
            };
        }
    }
}
