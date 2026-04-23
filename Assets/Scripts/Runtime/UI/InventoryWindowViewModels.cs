using System;
using UnityEngine;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.UI
{
    public sealed class InventoryWindowEntryViewModel
    {
        public int InventoryIndex;
        public string Name;
        public string IconPath;
        public string CountText;
        public string WeightText;
        public string ValueText;
        public bool Selected;
    }

    public sealed class InventoryWindowViewModel
    {
        public Rect NormalizedRect;
        public string Title;
        public string WeightLabel;
        public float WeightBarFillNormalized;
        public string ArmorSummary;
        public string FilterText;
        public InventoryWindowCategory Category;
        public string DetailText;
        public InventoryWindowEntryViewModel[] Entries = Array.Empty<InventoryWindowEntryViewModel>();
    }

    public sealed class ContainerWindowViewModel
    {
        public Rect NormalizedRect;
        public string Title;
        public string DetailText;
        public bool CanTakeSelected;
        public bool CanTakeAll;
        public string EmptyStateText;
        public InventoryWindowEntryViewModel[] Entries = Array.Empty<InventoryWindowEntryViewModel>();
    }
}
