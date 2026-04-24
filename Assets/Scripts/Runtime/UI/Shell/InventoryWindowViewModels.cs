using System;
using UnityEngine;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.UI.Shell
{
    public sealed class RuntimeHudViewModel
    {
        public bool Visible;
        public bool ShowCrosshair;
        public string FocusText;
        public string NotificationText;
        public string WeaponSpellText;
        public string CellNameText;
        public float HealthFillNormalized;
        public float MagickaFillNormalized;
        public float FatigueFillNormalized;
        public float WeaponStatusNormalized;
        public float SpellStatusNormalized;
        public float SneakStatusNormalized;
        public string WeaponLabel;
        public string SpellLabel;
        public string SneakLabel;
        // Vanilla MW shows a yellow health bar above the player vitals while targeting
        // a hostile actor (openmw_hud.layout "EnemyHealth"). Hidden until the targeting
        // pipeline decides to surface it.
        public bool ShowEnemyHealth;
        public float EnemyHealthFillNormalized;
        // Vanilla MW shows the sneak eye icon in the bottom-left quick-slot row only
        // while the player is sneaking (openmw_hud.layout "SneakBox"). Hidden otherwise.
        public bool ShowSneakIndicator;
    }

    public sealed class InventoryWindowEntryViewModel
    {
        public int InventoryIndex;
        public string Name;
        public string IconPath;
        public string CountText;
        public string WeightText;
        public string ValueText;
        public string SecondaryLeftText;
        public string SecondaryRightText;
        public string EquippedText;
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
        // MW_Window_Pinnable state: drives the pin button's visual and whether
        // the window stays on screen after the inventory group closes.
        public bool Pinned;
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

    public sealed class StatsWindowAttributeRow
    {
        public string Name;
        public string Value;
    }

    public sealed class StatsWindowSkillRow
    {
        public string Name;
        public string Value;
    }

    public sealed class StatsWindowFactionRow
    {
        public string Name;     // faction name
        public string Rank;     // current rank within the faction (e.g. "Apprentice")
    }

    /// <summary>
    /// Mirrors the vanilla Morrowind Stats window. Left column shows Health/Magicka/Fatigue
    /// bars, level/race/class identity, and the 8 attributes. Right column is a scrollable
    /// list of sections: Major / Minor / Misc skills, then Faction memberships, Birthsign,
    /// and overall Reputation. <see cref="CharacterName"/> drives the caption bar.
    /// </summary>
    public sealed class StatsWindowViewModel
    {
        public Rect NormalizedRect;
        public bool Pinned;
        public string CharacterName;
        public float HealthFillNormalized;
        public string HealthText;
        public float MagickaFillNormalized;
        public string MagickaText;
        public float FatigueFillNormalized;
        public string FatigueText;
        public string LevelText;
        public string RaceText;
        public string ClassText;
        public StatsWindowAttributeRow[] Attributes = Array.Empty<StatsWindowAttributeRow>();
        public StatsWindowSkillRow[] MajorSkills = Array.Empty<StatsWindowSkillRow>();
        public StatsWindowSkillRow[] MinorSkills = Array.Empty<StatsWindowSkillRow>();
        public StatsWindowSkillRow[] MiscSkills = Array.Empty<StatsWindowSkillRow>();
        public StatsWindowFactionRow[] Factions = Array.Empty<StatsWindowFactionRow>();
        public string BirthSignName;    // e.g. "The Lady" - empty = section hidden
        public string ReputationText;   // e.g. "0" - empty = section hidden
    }

    public sealed class SpellWindowViewModel
    {
        public Rect NormalizedRect;
        public bool Pinned;
        public string Title;
        public string FilterText;
        public string FooterButtonText;
        public string EffectSummaryText;
        public string SpellSummaryText;
        public string EmptyStateText;
        public SpellWindowEntryViewModel[] Entries = Array.Empty<SpellWindowEntryViewModel>();
        public SpellWindowEffectRow[] Effects = Array.Empty<SpellWindowEffectRow>();
    }

    public sealed class SpellWindowEntryViewModel
    {
        public string Name;
        public string CostText;
        public string TypeText;
        public bool Selected;
    }

    public sealed class SpellWindowEffectRow
    {
        public string Name;
        public string DetailText;
    }

    public sealed class MapWindowViewModel
    {
        public Rect NormalizedRect;
        public bool Pinned;
        public string Title;
        public string ToggleButtonText;
        public string ViewSummaryText;
        public string LocationText;
        public string RegionText;
        public string CellText;
        public string StreamingText;
        public bool InteriorActive;
    }

    public sealed class SaveSlotRowViewModel
    {
        public string SlotId;
        public string Name;
        public string TimestampText;
        public string CharacterText;
        public string LocationText;
        public string VersionText;
        public bool Valid;
        public bool Legacy;
        public bool Selected;
        public string ErrorText;
    }

    public sealed class SaveLoadBrowserViewModel
    {
        public SaveLoadBrowserMode Mode;
        public string Title;
        public string DraftSaveName;
        public string StatusText;
        public string ConfirmationText;
        public bool Confirming;
        public string PrimaryButtonText;
        public bool CanPrimary;
        public bool CanOverwrite;
        public bool CanDelete;
        public SaveSlotRowViewModel[] Slots = Array.Empty<SaveSlotRowViewModel>();
    }
}
