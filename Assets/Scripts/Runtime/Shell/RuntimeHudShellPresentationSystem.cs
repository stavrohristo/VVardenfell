using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    public partial class RuntimeHudShellPresentationSystem : SystemBase
    {
        EntityQuery _playerStatsQuery;
        EntityQuery _playerInventoryQuery;
        RuntimeHudShellView _view;
        readonly RuntimeHudViewModel _hudModel = new();
        readonly LocalMapViewModel _hudLocalMapModel = LocalMapPresentationCache.CreateReusableViewModel();
        LocationPresentation _location = LocationPresentation.Unavailable;
        FixedString128Bytes _lastFocusText;
        FixedString128Bytes _lastNotificationText;
        FixedString512Bytes _lastSubtitleText;
        FixedString128Bytes _lastModalTitle;
        FixedString512Bytes _lastModalBody;
        string _cachedFocusText;
        string _cachedNotificationText;
        string _cachedSubtitleText;
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
        FixedString128Bytes _lastLocationCellId;
        ulong _lastLocationRegionHash;
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
                ComponentType.ReadOnly<ActorKnownSpell>(),
                ComponentType.ReadOnly<ActorActiveMagicEffect>());
            _playerInventoryQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<PlayerInventoryItem>());

            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<InteractionPresentationState>();
            RequireForUpdate<InventoryWindowState>();
            RequireForUpdate<ContainerWindowState>();
            RequireForUpdate<StatsWindowState>();
            RequireForUpdate<SpellWindowState>();
            RequireForUpdate<MapWindowState>();
            RequireForUpdate<JournalWindowState>();
            RequireForUpdate<MorrowindQuestJournalState>();
            RequireForUpdate<MorrowindDialogueState>();
            RequireForUpdate<MorrowindTimeState>();
            RequireForUpdate<RuntimeContentBlobReference>();
            RequireForUpdate<RuntimeWorldCellBlobReference>();
            RequireForUpdate(_playerInventoryQuery);
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

            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            var shell = SystemAPI.GetSingleton<RuntimeShellState>();
            var subtitle = SystemAPI.TryGetSingleton<RuntimeSubtitleState>(out var subtitleState)
                ? subtitleState
                : default;
            var interaction = SystemAPI.GetSingleton<InteractionPresentationState>();
            var inventoryState = SystemAPI.GetSingleton<InventoryWindowState>();
            var containerState = SystemAPI.GetSingleton<ContainerWindowState>();
            var statsState = SystemAPI.GetSingleton<StatsWindowState>();
            var spellState = SystemAPI.GetSingleton<SpellWindowState>();
            var mapState = SystemAPI.GetSingleton<MapWindowState>();
            var journalState = SystemAPI.GetSingleton<JournalWindowState>();
            var saveLoadState = SystemAPI.GetSingleton<SaveLoadBrowserState>();
            Entity inventoryEntity = _playerInventoryQuery.GetSingletonEntity();
            var inventory = EntityManager.GetBuffer<PlayerInventoryItem>(inventoryEntity, true);
            var containerItems = SystemAPI.GetSingletonBuffer<ContainerSessionItem>();
            var enemyHealth = SystemAPI.TryGetSingleton<RuntimeEnemyHealthBarState>(out var enemyHealthState)
                ? enemyHealthState
                : default;
            var playerStats = BuildPlayerPresentationStats();
            var location = BuildLocationPresentation(ref contentBlob);
            bool suiteOpen = shell.InventoryOpen != 0 && shell.ContainerOpen == 0;
            bool inventoryVisible = (suiteOpen || (shell.ContainerOpen == 0 && inventoryState.Pinned != 0)) && shell.InventoryMenuDisabled == 0;
            bool statsVisible = (suiteOpen || (shell.ContainerOpen == 0 && statsState.Pinned != 0)) && shell.StatsMenuDisabled == 0;
            bool spellVisible = (suiteOpen || (shell.ContainerOpen == 0 && spellState.Pinned != 0)) && shell.MagicMenuDisabled == 0;
            bool mapVisible = (suiteOpen || (shell.ContainerOpen == 0 && mapState.Pinned != 0)) && shell.MapMenuDisabled == 0;

            InventoryWindowViewModel inventoryModel = inventoryVisible
                ? BuildInventoryModel(
                    ref contentBlob,
                    inventoryState,
                    inventory,
                    playerStats,
                    playerStats.HasPlayer
                    && EntityManager.Exists(playerStats.PlayerEntity)
                    && EntityManager.HasBuffer<ActorEquipmentSlot>(playerStats.PlayerEntity)
                        ? EntityManager.GetBuffer<ActorEquipmentSlot>(playerStats.PlayerEntity, true)
                        : default)
                : null;
            if (inventoryModel != null)
                inventoryModel.Pinned = inventoryState.Pinned != 0;
            ContainerWindowViewModel containerModel = shell.ContainerOpen != 0
                ? BuildContainerModel(ref contentBlob, containerState, containerItems)
                : null;
            bool visible = !BootstrapPresentationGate.BlocksGameplayInput;
            bool showHud = shell.HudVisible != 0;
            RuntimeHudViewModel hudModel = BuildHudModel(
                showHud,
                ref contentBlob,
                interaction,
                playerStats,
                location,
                inventoryState,
                inventory,
                spellState,
                subtitle,
                enemyHealth);
            StatsWindowViewModel statsModel = statsVisible
                ? BuildStatsModel(ref contentBlob, EntityManager, statsState, playerStats)
                : null;
            if (statsModel != null)
                statsModel.Pinned = statsState.Pinned != 0;
            SpellWindowViewModel spellModel = spellVisible
                ? BuildSpellModel(ref contentBlob, spellState, playerStats)
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
            JournalWindowViewModel journalModel = shell.JournalOpen != 0
                ? BuildJournalModel(
                    ref contentBlob,
                    journalState,
                    SystemAPI.GetSingletonBuffer<MorrowindQuestJournalIndex>(true),
                    SystemAPI.GetSingletonBuffer<MorrowindQuestJournalEntry>(true),
                    SystemAPI.GetSingletonBuffer<MorrowindTopicJournalEntry>(true))
                : null;
            DialogueWindowViewModel dialogueModel = shell.DialogueOpen != 0
                ? BuildDialogueModel(
                    ref contentBlob,
                    EntityManager,
                    SystemAPI.GetSingleton<MorrowindDialogueSession>(),
                    SystemAPI.GetSingletonBuffer<MorrowindDialogueSessionLine>(true),
                    SystemAPI.GetSingletonBuffer<MorrowindKnownDialogueTopic>(true),
                    SystemAPI.GetSingletonBuffer<MorrowindTopicJournalEntry>(true),
                    SystemAPI.GetSingletonBuffer<MorrowindDialogueChoice>(true))
                : null;
            RestMenuViewModel restMenuModel = shell.RestMenuOpen != 0 || shell.RestMenuAdvancing != 0
                ? BuildRestMenuModel(ref contentBlob, shell, SystemAPI.GetSingleton<MorrowindTimeState>(), playerStats)
                : null;
            MoviePlaybackViewModel movieModel = shell.MovieOpen != 0
                ? new MoviePlaybackViewModel
                {
                    MovieName = shell.MovieName.ToString(),
                    AllowSkipping = shell.MovieAllowSkipping != 0,
                }
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
                journalModel,
                dialogueModel,
                restMenuModel,
                movieModel,
                (RuntimeShellMenuActionId)shell.SelectedAction,
                shell.PauseMenuOpen != 0,
                shell.ModalOpen != 0,
                shell.OptionsOpen != 0,
                shell.ScreenFadeAlpha,
                shell.HitOverlayAlpha,
                ResolveModalTitle(shell.ModalOpen != 0, shell.ModalTitle),
                ResolveModalBody(shell.ModalOpen != 0, shell.ModalBody),
                ResolveModalButtons(shell.ModalOpen != 0, shell));
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

        RestMenuViewModel BuildRestMenuModel(
            ref RuntimeContentBlob contentBlob,
            in RuntimeShellState shell,
            in MorrowindTimeState time,
            in PlayerPresentationStats playerStats)
        {
            bool stuntedMagicka = false;
            if (playerStats.HasPlayer
                && EntityManager.Exists(playerStats.PlayerEntity)
                && EntityManager.HasBuffer<ActorActiveMagicEffect>(playerStats.PlayerEntity))
            {
                stuntedMagicka = RuntimeRestUtility.HasStuntedMagicka(
                    EntityManager.GetBuffer<ActorActiveMagicEffect>(playerStats.PlayerEntity, true));
            }

            bool canUntilHealed = shell.RestMenuCanSleep != 0
                && playerStats.HasPlayer
                && RuntimeRestUtility.NeedsHealing(playerStats.Vitals);

            int selectedHours = RuntimeRestUtility.ClampHours(shell.RestMenuSelectedHours <= 0 ? 1 : shell.RestMenuSelectedHours);
            int targetHours = RuntimeRestUtility.ClampHours(shell.RestMenuTargetHours <= 0 ? selectedHours : shell.RestMenuTargetHours);
            int progressHours = Math.Clamp(shell.RestMenuProgressHours, 0, targetHours);
            return new RestMenuViewModel
            {
                CanSleep = shell.RestMenuCanSleep != 0,
                CanUntilHealed = canUntilHealed,
                Advancing = shell.RestMenuAdvancing != 0,
                SelectedHours = selectedHours,
                ProgressHours = progressHours,
                TargetHours = targetHours,
                DateText = FormatRestDate(ref contentBlob, time),
                TimeText = FormatRestTime(time),
                HoursText = selectedHours == 1 ? "1 hour" : $"{selectedHours} hours",
                ProgressText = BuildRestProgressText(shell.RestMenuSleeping != 0, progressHours, targetHours, stuntedMagicka),
            };
        }

        static string FormatRestDate(ref RuntimeContentBlob contentBlob, in MorrowindTimeState time)
        {
            int month = Math.Clamp(time.Month, 0, k_DefaultMonthNames.Length - 1);
            string monthName = ResolveMonthName(ref contentBlob, month);
            return $"{time.Day} {monthName}, {time.Year}";
        }

        static string FormatRestTime(in MorrowindTimeState time)
        {
            float normalizedHour = MorrowindDayCycleUtility.NormalizeGameHour(time.GameHour);
            int hour = (int)Math.Floor(normalizedHour);
            int minute = (int)Math.Floor((normalizedHour - hour) * 60f);
            if (minute >= 60)
            {
                minute = 0;
                hour = (hour + 1) % 24;
            }

            return $"{hour:00}:{minute:00}";
        }

        static string BuildRestProgressText(bool sleeping, int progressHours, int targetHours, bool stuntedMagicka)
        {
            string mode = sleeping ? "Resting" : "Waiting";
            string suffix = stuntedMagicka && sleeping ? " (stunted magicka)" : string.Empty;
            return $"{mode}: {progressHours}/{targetHours} hours{suffix}";
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

        string ResolveSubtitleText(in RuntimeSubtitleState subtitle)
        {
            if (subtitle.Visible == 0)
                return null;

            if (!_lastSubtitleText.Equals(subtitle.Text))
            {
                _lastSubtitleText = subtitle.Text;
                _cachedSubtitleText = subtitle.Text.ToString();
            }

            return _cachedSubtitleText;
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

        static string[] ResolveModalButtons(bool modalOpen, in RuntimeShellState shell)
        {
            if (!modalOpen || shell.ModalButtonCount == 0)
                return Array.Empty<string>();

            int count = Math.Min((int)shell.ModalButtonCount, 10);
            var buttons = new string[count];
            for (int i = 0; i < count; i++)
                buttons[i] = GetModalButton(shell, i).ToString();
            return buttons;
        }

        static FixedString128Bytes GetModalButton(in RuntimeShellState shell, int index)
            => index switch
            {
                0 => shell.ModalButton0,
                1 => shell.ModalButton1,
                2 => shell.ModalButton2,
                3 => shell.ModalButton3,
                4 => shell.ModalButton4,
                5 => shell.ModalButton5,
                6 => shell.ModalButton6,
                7 => shell.ModalButton7,
                8 => shell.ModalButton8,
                9 => shell.ModalButton9,
                _ => default,
            };
    }
}
