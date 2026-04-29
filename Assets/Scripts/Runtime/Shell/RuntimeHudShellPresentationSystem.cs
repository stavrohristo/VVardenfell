using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    public partial class RuntimeHudShellPresentationSystem : SystemBase
    {
        EntityQuery _playerStatsQuery;
        RuntimeHudShellView _view;
        readonly RuntimeHudViewModel _hudModel = new();
        readonly LocalMapViewModel _hudLocalMapModel = LocalMapPresentationCache.CreateReusableViewModel();
        LocationPresentation _location = LocationPresentation.Unavailable;
        FixedString128Bytes _lastFocusText;
        FixedString128Bytes _lastNotificationText;
        FixedString128Bytes _lastModalTitle;
        FixedString512Bytes _lastModalBody;
        string _cachedFocusText;
        string _cachedNotificationText;
        string _cachedModalTitle = string.Empty;
        string _cachedModalBody = string.Empty;
        string _lastWeaponLabel = string.Empty;
        string _lastSpellLabel = string.Empty;
        string _cachedWeaponSpellText = string.Empty;
        RuntimeMagicEffectIconViewModel[] _cachedHudActiveEffects = Array.Empty<RuntimeMagicEffectIconViewModel>();
        ulong _cachedHudActiveEffectSignature;
        bool _hasCachedHudActiveEffectSignature;
        FixedString128Bytes _lastInteriorCellId;
        int2 _lastLocationExteriorCell = new(int.MinValue, int.MinValue);
        CellData _lastLocationCellData;
        int _lastLocationLoadedCount = -1;
        int _lastLocationActiveCount = -1;
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
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<PlayerKnownSpell>(),
                ComponentType.ReadOnly<ActorActiveMagicEffect>());

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
            bool showHud = shell.HudVisible != 0;
            RuntimeHudViewModel hudModel = BuildHudModel(
                showHud,
                RuntimeContentDatabase.Active,
                interaction,
                playerStats,
                location,
                inventoryState,
                inventory,
                spellState);
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
                ? BuildMapModel(mapState, location, playerStats)
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
                ResolveModalTitle(shell.ModalOpen != 0, shell.ModalTitle),
                ResolveModalBody(shell.ModalOpen != 0, shell.ModalBody));
        }

        PlayerPresentationStats BuildPlayerPresentationStats()
        {
            if (_playerStatsQuery.IsEmptyIgnoreFilter)
                return default;

            var vitals = _playerStatsQuery.GetSingleton<ActorVitalSet>();
            var derived = _playerStatsQuery.GetSingleton<ActorDerivedMovementStats>();
            var transform = _playerStatsQuery.GetSingleton<LocalTransform>();
            var playerEntity = _playerStatsQuery.GetSingletonEntity();
            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            var exteriorCell = new int2(
                (int)math.floor(transform.Position.x / cellMeters),
                (int)math.floor(transform.Position.z / cellMeters));
            var cellNormalized = new float2(
                math.saturate((transform.Position.x - exteriorCell.x * cellMeters) / cellMeters),
                math.saturate((transform.Position.z - exteriorCell.y * cellMeters) / cellMeters));
            float3 forward = math.mul(transform.Rotation, new float3(0f, 0f, 1f));
            float headingDegrees = math.degrees(math.atan2(forward.x, forward.z));

            return new PlayerPresentationStats(
                true,
                playerEntity,
                _playerStatsQuery.GetSingleton<ActorIdentitySet>(),
                _playerStatsQuery.GetSingleton<ActorAttributeSet>(),
                _playerStatsQuery.GetSingleton<ActorSkillSet>(),
                vitals,
                derived,
                transform.Position,
                transform.Rotation,
                cellNormalized,
                exteriorCell,
                headingDegrees,
                Normalize(vitals.CurrentHealth, vitals.ModifiedHealthBase),
                Normalize(vitals.CurrentMagicka, vitals.ModifiedMagickaBase),
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

        string ResolveFocusText(in InteractionPresentationState interaction)
        {
            if (interaction.ShowFocus == 0)
                return null;

            if (!_lastFocusText.Equals(interaction.FocusText))
            {
                _lastFocusText = interaction.FocusText;
                _cachedFocusText = interaction.FocusText.ToString();
            }

            return _cachedFocusText;
        }

        string ResolveNotificationText(in InteractionPresentationState interaction)
        {
            if (interaction.ShowNotification == 0)
                return null;

            if (!_lastNotificationText.Equals(interaction.NotificationText))
            {
                _lastNotificationText = interaction.NotificationText;
                _cachedNotificationText = interaction.NotificationText.ToString();
            }

            return _cachedNotificationText;
        }

        string ResolveModalTitle(bool modalOpen, FixedString128Bytes value)
        {
            if (!modalOpen)
                return string.Empty;

            if (!_lastModalTitle.Equals(value))
            {
                _lastModalTitle = value;
                _cachedModalTitle = value.ToString();
            }

            return _cachedModalTitle;
        }

        string ResolveModalBody(bool modalOpen, FixedString512Bytes value)
        {
            if (!modalOpen)
                return string.Empty;

            if (!_lastModalBody.Equals(value))
            {
                _lastModalBody = value;
                _cachedModalBody = value.ToString();
            }

            return _cachedModalBody;
        }
    }
}
