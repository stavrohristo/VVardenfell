using System;
using UnityEngine;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.Shell
{
    public partial class RuntimeHudShellPresentationSystem
    {
        static RuntimeHudViewModel BuildHudModel(bool showHud, in InteractionPresentationState interaction, in PlayerPresentationStats playerStats, in LocationPresentation location)
        {
            return new RuntimeHudViewModel
            {
                Visible = showHud,
                // Final crosshair visibility is gameplay state AND the user
                // preference — the Options "Show Crosshair" checkbox flips the
                // preference, hiding the crosshair even when the gameplay
                // interaction state would normally display it.
                ShowCrosshair = showHud && interaction.ShowCrosshair != 0 && HudUserPreferences.ShowCrosshair,
                FocusText = showHud && interaction.ShowFocus != 0 ? interaction.FocusText.ToString() : null,
                NotificationText = showHud && interaction.ShowNotification != 0 ? interaction.NotificationText.ToString() : null,
                WeaponSpellText = string.Empty,
                CellNameText = showHud ? location.DisplayName : string.Empty,
                HealthFillNormalized = 0f,
                MagickaFillNormalized = 0f,
                FatigueFillNormalized = playerStats.HasPlayer ? playerStats.FatigueFill : 0f,
                WeaponStatusNormalized = 0.72f,
                SpellStatusNormalized = 0.68f,
                SneakStatusNormalized = 0.6f,
                WeaponLabel = "W",
                SpellLabel = "S",
                SneakLabel = "Sneak",
                ShowEnemyHealth = false,
                EnemyHealthFillNormalized = 0f,
                ShowSneakIndicator = false,
            };
        }

        static MapWindowViewModel BuildMapModel(in MapWindowState state, in LocationPresentation location)
        {
            return new MapWindowViewModel
            {
                NormalizedRect = new Rect(state.NormalizedX, state.NormalizedY, state.NormalizedWidth, state.NormalizedHeight),
                Title = "Map",
                ToggleButtonText = location.InteriorActive ? "Local" : "World",
                ViewSummaryText = location.InteriorActive ? "Interior local map data is not rendered yet." : "Exterior world map rendering is not rendered yet.",
                LocationText = string.IsNullOrWhiteSpace(location.DisplayName) ? "--" : location.DisplayName,
                RegionText = location.RegionText,
                CellText = location.CellText,
                StreamingText = location.StreamingText,
                InteriorActive = location.InteriorActive,
            };
        }
    }
}

