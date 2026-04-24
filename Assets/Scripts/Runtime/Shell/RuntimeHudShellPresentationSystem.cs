using System;
using Unity.Entities;
using UnityEngine;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.UI.Shell;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    public partial class RuntimeHudShellPresentationSystem : SystemBase
    {
        EntityQuery _playerStatsQuery;
        RuntimeHudShellView _view;
        bool _creationFailed;

        protected override void OnCreate()
        {
            _playerStatsQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<ActorIdentitySet>(),
                ComponentType.ReadOnly<ActorAttributeSet>(),
                ComponentType.ReadOnly<ActorSkillSet>(),
                ComponentType.ReadOnly<ActorVitalSet>(),
                ComponentType.ReadOnly<ActorDerivedMovementStats>(),
                ComponentType.ReadOnly<PlayerKnownSpell>());

            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<InteractionPresentationState>();
            RequireForUpdate<InventoryWindowState>();
            RequireForUpdate<ContainerWindowState>();
            RequireForUpdate<StatsWindowState>();
            RequireForUpdate<SpellWindowState>();
            RequireForUpdate<MapWindowState>();
            RequireForUpdate<PlayerInventoryItem>();
            RequireForUpdate<ContainerSessionItem>();
        }

        protected override void OnDestroy()
        {
            if (_view != null)
                UnityEngine.Object.Destroy(_view.gameObject);
            _view = null;
            RuntimeShellPresentationGate.BlocksGameplayInput = false;
        }

        protected override void OnUpdate()
        {
            if (_view == null && !_creationFailed)
            {
                try
                {
                    _view = RuntimeHudShellView.Create();
                }
                catch (Exception ex)
                {
                    _creationFailed = true;
                    Debug.LogError($"[VVardenfell][UI] failed creating runtime HUD shell: {ex.Message}");
                    return;
                }
            }

            if (_view == null)
                return;

            var shell = SystemAPI.GetSingleton<RuntimeShellState>();
            var interaction = SystemAPI.GetSingleton<InteractionPresentationState>();
            var inventoryState = SystemAPI.GetSingleton<InventoryWindowState>();
            var containerState = SystemAPI.GetSingleton<ContainerWindowState>();
            var statsState = SystemAPI.GetSingleton<StatsWindowState>();
            var spellState = SystemAPI.GetSingleton<SpellWindowState>();
            var mapState = SystemAPI.GetSingleton<MapWindowState>();
            var saveLoadState = SystemAPI.GetSingleton<SaveLoadBrowserState>();
            var inventory = SystemAPI.GetSingletonBuffer<PlayerInventoryItem>();
            var containerItems = SystemAPI.GetSingletonBuffer<ContainerSessionItem>();
            var playerStats = BuildPlayerPresentationStats();
            var location = BuildLocationPresentation(RuntimeContentDatabase.Active);
            // MW_Window_Pinnable rule: the inventory-group subwindows
            // (Inventory / Stats / Spell / Map) render whenever the group is
            // open, OR — if the group is closed — whenever their own Pinned
            // byte is set. Pinned subwindows stay visible through the menu
            // canvas while the player runs around. Container is not part of
            // the pin group.
            bool suiteOpen = shell.InventoryOpen != 0 && shell.ContainerOpen == 0;
            bool inventoryVisible = suiteOpen || (shell.ContainerOpen == 0 && inventoryState.Pinned != 0);
            bool statsVisible = suiteOpen || (shell.ContainerOpen == 0 && statsState.Pinned != 0);
            bool spellVisible = suiteOpen || (shell.ContainerOpen == 0 && spellState.Pinned != 0);
            bool mapVisible = suiteOpen || (shell.ContainerOpen == 0 && mapState.Pinned != 0);

            InventoryWindowViewModel inventoryModel = inventoryVisible
                ? BuildInventoryModel(RuntimeContentDatabase.Active, inventoryState, inventory, playerStats)
                : null;
            if (inventoryModel != null)
                inventoryModel.Pinned = inventoryState.Pinned != 0;
            ContainerWindowViewModel containerModel = shell.ContainerOpen != 0
                ? BuildContainerModel(RuntimeContentDatabase.Active, containerState, containerItems)
                : null;
            bool visible = !BootstrapPresentationGate.BlocksGameplayInput;
            // HUD lives on its own canvas at a lower sortingOrder than the menu
            // canvas, so windows that open (inventory, pause, options, save/load,
            // modals, etc.) naturally draw over the HUD while the gauges stay
            // visible behind them. The only gate that fully hides the HUD is
            // HudVisible itself, which the shell state system drops to 0 during
            // bootstrap / loading screens.
            bool showHud = shell.HudVisible != 0;
            RuntimeHudViewModel hudModel = BuildHudModel(showHud, interaction, playerStats, location);
            StatsWindowViewModel statsModel = statsVisible
                ? BuildStatsModel(RuntimeContentDatabase.Active, statsState, playerStats)
                : null;
            if (statsModel != null)
                statsModel.Pinned = statsState.Pinned != 0;
            SpellWindowViewModel spellModel = spellVisible
                ? BuildSpellModel(RuntimeContentDatabase.Active, spellState, playerStats)
                : null;
            if (spellModel != null)
                spellModel.Pinned = spellState.Pinned != 0;
            MapWindowViewModel mapModel = mapVisible
                ? BuildMapModel(mapState, location)
                : null;
            if (mapModel != null)
                mapModel.Pinned = mapState.Pinned != 0;
            SaveLoadBrowserViewModel saveLoadModel = shell.SaveLoadBrowserOpen != 0
                ? BuildSaveLoadBrowserModel(saveLoadState)
                : null;

            _view.Sync(
                visible,
                hudModel,
                inventoryModel,
                containerModel,
                statsModel,
                spellModel,
                mapModel,
                saveLoadModel,
                (RuntimeShellMenuActionId)shell.SelectedAction,
                shell.PauseMenuOpen != 0,
                shell.ModalOpen != 0,
                shell.OptionsOpen != 0,
                shell.ModalTitle.ToString(),
                shell.ModalBody.ToString());
        }

        PlayerPresentationStats BuildPlayerPresentationStats()
        {
            if (_playerStatsQuery.IsEmptyIgnoreFilter)
                return default;

            var vitals = _playerStatsQuery.GetSingleton<ActorVitalSet>();
            var derived = _playerStatsQuery.GetSingleton<ActorDerivedMovementStats>();
            var playerEntity = _playerStatsQuery.GetSingletonEntity();
            return new PlayerPresentationStats(
                true,
                playerEntity,
                _playerStatsQuery.GetSingleton<ActorIdentitySet>(),
                _playerStatsQuery.GetSingleton<ActorAttributeSet>(),
                _playerStatsQuery.GetSingleton<ActorSkillSet>(),
                vitals,
                derived,
                Normalize(vitals.CurrentFatigue, vitals.ModifiedFatigueBase),
                derived.CarryCapacity > 0f
                    ? Math.Clamp(derived.Encumbrance / derived.CarryCapacity, 0f, 1f)
                    : 0f);
        }
        static float Normalize(float current, float max)
        {
            if (max <= 0f)
                return 0f;

            return Math.Clamp(current / max, 0f, 1f);
        }
    }
}

