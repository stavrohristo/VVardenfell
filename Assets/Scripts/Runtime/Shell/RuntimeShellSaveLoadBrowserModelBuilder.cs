using System;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.UI.Shell;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.Shell
{
    public partial class RuntimeHudShellPresentationSystem
    {
        static SaveLoadBrowserViewModel BuildSaveLoadBrowserModel(in SaveLoadBrowserState state)
        {
            var mode = (SaveLoadBrowserMode)state.Mode;
            var summaries = mode == SaveLoadBrowserMode.MainMenuConfirm
                ? Array.Empty<SaveGameSlotSummary>()
                : WorldSaveStorage.EnumerateSlots();
            var rows = new SaveSlotRowViewModel[summaries.Length];
            string selectedId = state.SelectedSlotId.ToString();
            bool selectedValid = false;
            bool selectedLegacy = false;
            for (int i = 0; i < summaries.Length; i++)
            {
                var slot = summaries[i];
                bool selected = string.Equals(selectedId, slot.SlotId, StringComparison.OrdinalIgnoreCase);
                if (selected)
                {
                    selectedValid = slot.IsValid;
                    selectedLegacy = slot.IsLegacy;
                }

                rows[i] = new SaveSlotRowViewModel
                {
                    SlotId = slot.SlotId ?? string.Empty,
                    Name = string.IsNullOrWhiteSpace(slot.DisplayName) ? "Save" : slot.DisplayName,
                    TimestampText = slot.LastModifiedUtcTicks > 0
                        ? new DateTime(slot.LastModifiedUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                        : "--",
                    CharacterText = $"{(string.IsNullOrWhiteSpace(slot.CharacterName) ? "Player" : slot.CharacterName)}  Lv {Math.Max(1, slot.PlayerLevel)}",
                    LocationText = string.IsNullOrWhiteSpace(slot.LocationName) ? "--" : slot.LocationName,
                    VersionText = slot.IsValid ? $"v{slot.PayloadVersion}" : "Invalid",
                    Valid = slot.IsValid,
                    Legacy = slot.IsLegacy,
                    Selected = selected,
                    ErrorText = slot.Error ?? string.Empty,
                };
            }

            bool hasSelection = !string.IsNullOrWhiteSpace(selectedId);
            bool isLoad = mode == SaveLoadBrowserMode.Load;
            bool isSave = mode == SaveLoadBrowserMode.Save;
            bool isMainMenu = mode == SaveLoadBrowserMode.MainMenuConfirm;
            string title = isMainMenu ? "Main Menu" : isLoad ? "Load Game" : "Save Game";
            return new SaveLoadBrowserViewModel
            {
                Mode = mode,
                Title = title,
                DraftSaveName = state.DraftSaveName.ToString(),
                StatusText = state.StatusText.ToString(),
                ConfirmationText = state.ConfirmationText.ToString(),
                Confirming = state.ConfirmAction != 0,
                PrimaryButtonText = isMainMenu ? "Main Menu" : isLoad ? "Load" : "New Save",
                CanPrimary = isMainMenu || isSave || (hasSelection && selectedValid),
                CanOverwrite = isSave && hasSelection && selectedValid && !selectedLegacy,
                CanDelete = hasSelection && !selectedLegacy,
                Slots = rows,
            };
        }
    }
}

