using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bake;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.UI.Assets;
using VVardenfell.Runtime.UI.Framework;
using Stopwatch = System.Diagnostics.Stopwatch;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Bootstrap
{
    /// <summary>
    /// Boot state machine:
    /// PickPath -> user selects the Morrowind install folder if no valid config exists.
    /// Baking -> one-time conversion from BSA/ESM to the DOTS cache.
    /// Loading -> staged cache hydration + world install, hidden behind presentation.
    /// Ready -> world is loaded and waits behind the visual main menu until dismissed.
    /// </summary>
    public class BootstrapController : MonoBehaviour
    {
        [System.Serializable]
        public sealed class PlayerMovementSettings
        {
            [Header("Capsule")]
            [Min(0.05f)] public float StandingHeight = 1.8f;
            [Min(0.05f)] public float CrouchingHeight = 1.15f;
            [Min(0.01f)] public float Radius = 0.35f;

            [Header("View")]
            public float StandingEyeHeight = 1.65f;
            public float CrouchingEyeHeight = 1.0f;
            public float MinPitch = -89f;
            public float MaxPitch = 89f;
            [Min(0.001f)] public float LookSensitivity = 0.12f;

            public PlayerCharacterComponent Build()
            {
                Validate();

                float resolvedStandingEyeHeight = StandingEyeHeight > 0f ? StandingEyeHeight : StandingHeight - 0.15f;
                float resolvedCrouchingEyeHeight = CrouchingEyeHeight > 0f ? CrouchingEyeHeight : CrouchingHeight - 0.15f;
                float resolvedMinPitch = math.min(MinPitch, MaxPitch);
                float resolvedMaxPitch = math.max(MinPitch, MaxPitch);

                return new PlayerCharacterComponent
                {
                    StandingHeight = StandingHeight,
                    CrouchingHeight = CrouchingHeight,
                    StandingEyeHeight = resolvedStandingEyeHeight,
                    CrouchingEyeHeight = resolvedCrouchingEyeHeight,
                    Radius = Radius,
                    MinPitch = resolvedMinPitch,
                    MaxPitch = resolvedMaxPitch,
                    LookSensitivity = LookSensitivity,
                };
            }

            public void Validate()
            {
                StandingHeight = math.max(0.05f, StandingHeight);
                CrouchingHeight = math.clamp(CrouchingHeight, 0.05f, StandingHeight);
                Radius = math.max(0.01f, Radius);
                StandingEyeHeight = math.max(0.01f, StandingEyeHeight);
                CrouchingEyeHeight = math.max(0.01f, CrouchingEyeHeight);
                LookSensitivity = math.max(0.001f, LookSensitivity);
            }
        }

        private enum Stage { PickPath, PickMode, PickProjectTamrielPath, PickBattleground, Baking, Loading, Ready, Failed }
        public static BootstrapController Active { get; private set; }

        [Header("Player Movement")]
        [SerializeField] private PlayerMovementSettings _playerMovement = new PlayerMovementSettings();

        private Stage _stage = Stage.PickPath;
        private string _path = "";
        private string _pathError;
        private string _projectTamrielPath = "";
        private string _projectTamrielPathError;
        private string _fatalError;
        private string _lastLoggedDisplayedError;
        private MorrowindConfig _config;
        private readonly BakeProgress _progress = new BakeProgress();
        private readonly RuntimeLoadProgress _loadProgress = new RuntimeLoadProgress();
        private Coroutine _loadCoroutine;
        private BootstrapPresentationView _presentation;
        private BootstrapFallbackView _fallbackView;
        private bool _presentationReady;
        private bool _loadStartRequested;
        private BootstrapRuntimeMode _selectedRuntimeMode = BootstrapRuntimeMode.Vanilla;
        private WorldBootstrapOptions _bootstrapOptions = WorldBootstrapOptions.Vanilla;
        private MorrowindContentProfile _selectedContentProfile;
        private bool _pendingCombatBattlegroundSelection;
        private string _combatBattlegroundFilter = string.Empty;
        private (int X, int Y)[] _combatBattlegroundCells = Array.Empty<(int X, int Y)>();
        private World _runtimeLifecycleWorld;
        private EntityQuery _runtimeActiveQuery;
        private EntityQuery _runtimePausedQuery;
        private bool _runtimeLifecycleQueriesCreated;

        public static PlayerCharacterComponent ResolvePlayerMovementSettings()
        {
            if (Active != null)
                return Active._playerMovement.Build();

            return new PlayerMovementSettings().Build();
        }

        public static bool TryShowRuntimeMainMenu(out string error)
        {
            error = null;
            if (Active == null)
            {
                error = "Bootstrap controller is not available.";
                return false;
            }

            return Active.ShowRuntimeMainMenu(out error);
        }

        private void Awake()
        {
            if (Active != null && Active != this)
            {
                Destroy(gameObject);
                return;
            }

            Active = this;
            DontDestroyOnLoad(gameObject);
            BootstrapPresentationGate.Reset();
            BootstrapPresentationAudioState.Reset();
            _playerMovement?.Validate();
        }

        private void OnValidate()
        {
            _playerMovement ??= new PlayerMovementSettings();
            _playerMovement.Validate();
        }

        private void Start()
        {
            EnsureFallbackView();
            if (ConfigStorage.TryLoad(out var c) && c.IsValid(out _))
            {
                _config = c;
                ApplyPersistedSettings(c);
                ShowModeSelection();
            }
            else
            {
                _stage = Stage.PickPath;
                _path = GuessDefaultInstallPath();
            }

            RefreshFallbackView();
        }

        /// <summary>
        /// Apply persisted player settings from config to the matching runtime
        /// knobs before any UI is built or the first intro frame is rendered.
        /// Audio scalars are picked up inside <c>RuntimeAudioService</c>'s ctor
        /// (it reads config directly) so we don't touch them here; the rest of
        /// the settings need to be pushed here so e.g. an earlier-saved UI scale
        /// applies to the intro captions, not only after the main menu opens.
        /// </summary>
        static void ApplyPersistedSettings(MorrowindConfig config)
        {
            if (config == null)
                return;

            RuntimeUiScaleSettings.GlobalScale = config.UiScale;
            RuntimeUiScaleSettings.HudScale = config.HudScale;
            RuntimeVideoSettingsUtility.ApplyFogDistanceScale(config.FogDistanceScale);

            Screen.brightness = config.Gamma;
            QualitySettings.vSyncCount = Mathf.Clamp(config.VSync, 0, 2);
            Screen.fullScreenMode = config.WindowMode switch
            {
                1 => FullScreenMode.ExclusiveFullScreen,
                2 => FullScreenMode.FullScreenWindow,
                _ => FullScreenMode.Windowed,
            };

            if (config.ResolutionWidth > 0 && config.ResolutionHeight > 0)
            {
                VVardenfell.Runtime.UI.Shell.RuntimeScreenResolutionUtility.SetResolution(
                    config.ResolutionWidth,
                    config.ResolutionHeight,
                    Screen.fullScreenMode,
                    config.RefreshRate);
            }

            // FOV can't apply yet because MainCameraSingleton is not registered
            // until the world scene camera authoring component has loaded.
            // RuntimeHudShellView reapplies this when the HUD comes up, and the
            // Options window itself writes FOV live when moved.
        }

        public static BootstrapRuntimeMode CurrentRuntimeMode
            => Active != null ? Active._selectedRuntimeMode : BootstrapRuntimeMode.Vanilla;

        private void ShowModeSelection()
        {
            _stage = Stage.PickMode;
            _loadStartRequested = false;
            _loadCoroutine = null;
            _presentationReady = false;
        }

        private void BeginCacheFlow(BootstrapRuntimeMode mode)
        {
            _selectedRuntimeMode = mode;
            if (!ResolveContentProfileForMode(mode, out _selectedContentProfile, out string profileError))
            {
                if (mode == BootstrapRuntimeMode.ProjectTamriel)
                {
                    ShowProjectTamrielPathSelection(profileError);
                    return;
                }

                SetFatalError(profileError);
                _stage = Stage.Failed;
                return;
            }
            CachePaths.UseContentProfile(_selectedContentProfile);

            _pendingCombatBattlegroundSelection = mode == BootstrapRuntimeMode.CombatSandbox;
            if (!_pendingCombatBattlegroundSelection)
                _bootstrapOptions = BuildBootstrapOptions(mode);

            _progress.Stage = "";
            _progress.Label = "";
            _progress.Current = 0;
            _progress.Total = 0;
            _progress.Error = null;
            _progress.Done = false;

            string esmPath = _selectedContentProfile.ContentFiles.Length > 0 ? _selectedContentProfile.ContentFiles[0] : Path.Combine(_config.InstallPath, "Data Files", "Morrowind.esm");
            string bsaPath = _selectedContentProfile.Archives.Length > 0 ? _selectedContentProfile.Archives[0] : Path.Combine(_config.InstallPath, "Data Files", "Morrowind.bsa");
            string[] gameplayRecordSources = InstalledContentSources.ResolveGameplayRecordSources(_selectedContentProfile);
            string[] gameplaySources = InstalledContentSources.ResolveGameplayDependencySources(_selectedContentProfile);

            bool worldCacheValid = BakeManifest.TryRead(CachePaths.Manifest, out var worldManifest)
                                   && worldManifest.SourcesMatch(esmPath, bsaPath, gameplaySources)
                                   && WorldCachePipelineMatchesRuntime(worldManifest);
            bool uiCacheValid = UiCacheManifest.TryRead(CachePaths.UiManifest, out var uiManifest)
                                && uiManifest.SourcesMatch(_config.InstallPath)
                                && uiManifest.HasRequiredBootstrapImages(out _)
                                && MovieTranscodeBridge.CacheMatches(uiManifest, _config.InstallPath, out _);
            bool gameplayCacheValid = File.Exists(CachePaths.GameplayContent)
                                      && File.Exists(CachePaths.RuntimeContentBlob)
                                      && GameplayContentManifest.TryRead(CachePaths.GameplayContentManifest, out var gameplayManifest)
                                      && gameplayManifest.SourcesMatch(gameplaySources);

            if (worldCacheValid && uiCacheValid && gameplayCacheValid)
            {
                ContinueAfterCacheReady();
            }
            else if (worldCacheValid && uiCacheValid)
            {
                _stage = Stage.Baking;
                StartCoroutine(BakeCoordinator.BakeGameplayOnly(_config, _selectedContentProfile, _progress));
            }
            else if (worldCacheValid && gameplayCacheValid)
            {
                _stage = Stage.Baking;
                StartCoroutine(BakeCoordinator.BakeUiOnly(_config, _progress));
            }
            else if (worldCacheValid)
            {
                _stage = Stage.Baking;
                StartCoroutine(BakeCoordinator.BakeUiAndGameplayOnly(_config, _selectedContentProfile, _progress));
            }
            else
            {
                _stage = Stage.Baking;
                StartCoroutine(BakeCoordinator.Bake(_config, _selectedContentProfile, _progress));
            }
        }

        private bool ResolveContentProfileForMode(BootstrapRuntimeMode mode, out MorrowindContentProfile profile, out string error)
        {
            if (mode != BootstrapRuntimeMode.ProjectTamriel)
            {
                profile = _config.CreateVanillaContentProfile();
                error = null;
                return true;
            }

            profile = _config.ProjectTamrielProfile;
            if (profile != null && profile.IsValid(out _))
            {
                if (InstalledContentSources.TryCreateProjectTamrielProfile(
                        _config.InstallPath,
                        profile.InstallPath,
                        out MorrowindContentProfile refreshedProfile,
                        out _)
                    && !ContentProfilesMatch(profile, refreshedProfile))
                {
                    profile = refreshedProfile;
                    _config.ProjectTamrielProfile = profile;
                    ConfigStorage.Save(_config);
                }

                error = null;
                return true;
            }

            if (!InstalledContentSources.TryCreateProjectTamrielProfile(_config.InstallPath, out profile, out error))
                return false;

            _config.ProjectTamrielProfile = profile;
            ConfigStorage.Save(_config);
            error = null;
            return true;
        }

        private static bool ContentProfilesMatch(MorrowindContentProfile a, MorrowindContentProfile b)
        {
            if (a == null || b == null)
                return false;

            string aKey = string.IsNullOrWhiteSpace(a.ProfileCacheKey)
                ? MorrowindContentProfile.BuildCacheKey(a)
                : a.ProfileCacheKey;
            string bKey = string.IsNullOrWhiteSpace(b.ProfileCacheKey)
                ? MorrowindContentProfile.BuildCacheKey(b)
                : b.ProfileCacheKey;

            return string.Equals(aKey, bKey, StringComparison.OrdinalIgnoreCase)
                   && StringArraysMatch(a.ContentFiles, b.ContentFiles)
                   && StringArraysMatch(a.Archives, b.Archives)
                   && StringArraysMatch(a.DataRoots, b.DataRoots);
        }

        private static bool StringArraysMatch(string[] a, string[] b)
        {
            int aLength = a?.Length ?? 0;
            int bLength = b?.Length ?? 0;
            if (aLength != bLength)
                return false;

            for (int i = 0; i < aLength; i++)
                if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
                    return false;

            return true;
        }

        private void ShowProjectTamrielPathSelection(string error)
        {
            _stage = Stage.PickProjectTamrielPath;
            _projectTamrielPath = ResolveInitialProjectTamrielPickerPath();
            _projectTamrielPathError = error;
        }

        private string ResolveInitialProjectTamrielPickerPath()
        {
            if (!string.IsNullOrWhiteSpace(_projectTamrielPath))
                return _projectTamrielPath;

            string savedInstallPath = _config?.ProjectTamrielProfile?.InstallPath;
            if (!string.IsNullOrWhiteSpace(savedInstallPath))
                return savedInstallPath;

            return _config?.InstallPath ?? _path ?? string.Empty;
        }

        private static bool WorldCachePipelineMatchesRuntime(BakeManifest manifest)
        {
            var states = manifest?.CellStates;
            if (states == null || states.Length == 0)
                return false;

            for (int i = 0; i < states.Length; i++)
            {
                var state = states[i];
                if (state == null || state.PipelineVersion != CacheFormat.WorldBakePipelineVersion)
                    return false;
            }

            return true;
        }

        private void Update()
        {
            if (_stage == Stage.Baking && _progress.Done)
            {
                if (!string.IsNullOrEmpty(_progress.Error))
                {
                    SetFatalError($"Bake failed: {_progress.Error}");
                    _stage = Stage.Failed;
                    return;
                }

                ContinueAfterCacheReady();
            }

            if ((_stage == Stage.Loading || _stage == Stage.Ready) && _presentation != null && _presentation.IsDismissed)
            {
                DestroyPresentation();
                enabled = false;
            }

            RefreshFallbackView();
        }

        private void BeginLoading()
        {
            _loadProgress.Reset();
            _stage = Stage.Loading;

            if (ShouldSkipPresentationForMode(_selectedRuntimeMode))
            {
                _loadStartRequested = false;
                if (_loadCoroutine == null)
                    _loadCoroutine = StartCoroutine(LoadRoutine());
                return;
            }

            if (!EnsurePresentation())
                return;

            _loadProgress.BeginStage("Boot Sequence", "Waiting for intro sequence", 1);
            _loadProgress.Report("Waiting for intro sequence", 0, 1);
            _loadStartRequested = true;
        }

        private void ContinueAfterCacheReady()
        {
            if (_pendingCombatBattlegroundSelection)
            {
                ShowCombatBattlegroundSelection();
                return;
            }

            BeginLoading();
        }

        private void ShowCombatBattlegroundSelection()
        {
            if (!BakeManifest.TryRead(CachePaths.Manifest, out var manifest)
                || manifest.CellGrid == null
                || manifest.CellGrid.Length == 0)
            {
                SetFatalError("[VVardenfell][CombatSandbox] baked exterior cell grid is unavailable; rebuild world caches before selecting a battleground.");
                _stage = Stage.Failed;
                return;
            }

            var contentBlob = RuntimeContentBlobFile.Read(CachePaths.RuntimeContentBlob);
            try
            {
                if (!contentBlob.IsCreated)
                    throw new InvalidDataException("runtime content blob unreadable");

                ref RuntimeContentBlob content = ref contentBlob.Value;
                var pathGridCells = new List<(int X, int Y)>(manifest.CellGrid.Length);
                for (int i = 0; i < manifest.CellGrid.Length; i++)
                {
                    var cell = manifest.CellGrid[i];
                    if (RuntimeContentBlobUtility.TryGetExteriorPathGridHandle(ref content, cell.X, cell.Y, out var handle) && handle.IsValid)
                        pathGridCells.Add(cell);
                }

                if (pathGridCells.Count == 0)
                {
                    SetFatalError("[VVardenfell][CombatSandbox] no baked exterior cells have pathgrids; combat movement cannot run without pathgrid-backed battlegrounds.");
                    _stage = Stage.Failed;
                    return;
                }

                _combatBattlegroundCells = pathGridCells.ToArray();
            }
            finally
            {
                if (contentBlob.IsCreated)
                    contentBlob.Dispose();
            }

            _combatBattlegroundFilter ??= string.Empty;
            _stage = Stage.PickBattleground;
            RefreshFallbackView();
        }

        private void OnPresentationLoadingPhaseReady()
        {
            if (!_loadStartRequested || _loadCoroutine != null || _stage != Stage.Loading)
                return;

            _loadProgress.BeginStage("Loading World", "Starting cache hydration", 1);
            _loadProgress.Report("Starting cache hydration", 0, 1);
            _loadCoroutine = StartCoroutine(LoadRoutine());
        }

        private IEnumerator LoadRoutine()
        {
            _loadProgress.Reset();
            var sw = Stopwatch.StartNew();

            var loader = new CacheLoader();
            var routines = new IEnumerator[]
            {
                loader.LoadIncremental(_loadProgress),
                WorldBootstrap.InstallIncremental(loader, _loadProgress, _bootstrapOptions),
            };

            for (int i = 0; i < routines.Length; i++)
            {
                var stack = new System.Collections.Generic.Stack<IEnumerator>();
                stack.Push(routines[i]);

                while (stack.Count > 0)
                {
                    var top = stack.Peek();
                    bool movedNext;
                    object current = null;

                    try
                    {
                        movedNext = top.MoveNext();
                        if (movedNext)
                            current = top.Current;
                    }
                    catch (System.Exception ex)
                    {
                        sw.Stop();
                        _loadProgress.Fail(ex.Message);
                        WorldBootstrap.Uninstall();
                        DestroyPresentation();

                        string stage = string.IsNullOrEmpty(_loadProgress.Stage) ? "bootstrap" : _loadProgress.Stage;
                        Debug.LogError(
                            $"[VVardenfell][BootstrapFail] stage='{stage}' message='{ex.Message}' inner='{ex.InnerException?.Message ?? "<none>"}'");
                        SetFatalError($"Bootstrap failed during '{stage}': {ex.Message}");
                        _stage = Stage.Failed;
                        _loadCoroutine = null;
                        yield break;
                    }

                    if (!movedNext)
                    {
                        stack.Pop();
                        continue;
                    }

                    if (current is IEnumerator nested)
                    {
                        stack.Push(nested);
                        continue;
                    }

                    yield return current;
                }
            }

            sw.Stop();
            _loadProgress.Complete();
            _presentation?.NotifyBootstrapComplete();
            _loadCoroutine = null;
            _loadStartRequested = false;
            _stage = Stage.Ready;
            if (_presentation == null)
                Destroy(gameObject);
        }

        private static string GuessDefaultInstallPath()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            string persistentRoot = Application.persistentDataPath;
            string[] androidCandidates =
            {
                Path.Combine(persistentRoot, "Morrowind"),
                persistentRoot,
                "/storage/emulated/0/Morrowind",
                "/storage/emulated/1/Morrowind",
            };

            foreach (var c in androidCandidates)
                if (Directory.Exists(c))
                    return c;

            return androidCandidates[0];
#else
            string[] candidates =
            {
                @"C:\Program Files (x86)\Steam\steamapps\common\Morrowind",
                @"C:\Program Files\Steam\steamapps\common\Morrowind",
                @"E:\SteamLibrary\steamapps\common\Morrowind",
                @"C:\GOG Games\Morrowind",
                @"C:\Program Files (x86)\Bethesda Softworks\Morrowind",
            };
            foreach (var c in candidates)
                if (Directory.Exists(c))
                    return c;
            return "";
#endif
        }

        private bool EnsurePresentation()
        {
            if (_presentation != null)
                return true;

            try
            {
                var theme = RuntimeUiTheme.FromAssets(new UiAssetLoader().Load());
                var go = new GameObject("VVardenfell.Presentation");
                DontDestroyOnLoad(go);
                _presentation = go.AddComponent<BootstrapPresentationView>();
                _presentation.Initialize(theme, _loadProgress, _config.InstallPath, OnPresentationLoadingPhaseReady);
                _presentationReady = true;
                return true;
            }
            catch (System.Exception ex)
            {
                SetFatalError($"Failed to load presentation UI: {ex.Message}");
                _stage = Stage.Failed;
                _presentationReady = false;
                return false;
            }
        }

        private bool ShowRuntimeMainMenu(out string error)
        {
            error = null;
            enabled = true;
            _stage = Stage.Ready;
            _loadStartRequested = false;
            if (_loadCoroutine != null)
            {
                StopCoroutine(_loadCoroutine);
                _loadCoroutine = null;
            }

            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                EnsureRuntimeLifecycleQueries(world);
                MorrowindRuntimeLifecycleUtility.RemoveRuntimeLifecycle(world.EntityManager, _runtimePausedQuery, _runtimeActiveQuery);
            }

            DestroyPresentation();
            try
            {
                var theme = RuntimeUiTheme.FromAssets(new UiAssetLoader().Load());
                var go = new GameObject("VVardenfell.Presentation");
                DontDestroyOnLoad(go);
                _presentation = go.AddComponent<BootstrapPresentationView>();
                _presentation.InitializeMenuOverlay(theme, _loadProgress, _config.InstallPath);
                _presentation.NotifyBootstrapComplete();
                _presentationReady = true;
                return true;
            }
            catch (System.Exception ex)
            {
                error = $"Failed to show main menu: {ex.Message}";
                SetFatalError(error);
                _stage = Stage.Failed;
                _presentationReady = false;
                return false;
            }
        }

        private void DestroyPresentation()
        {
            if (_presentation != null)
                Destroy(_presentation.gameObject);
            _presentation = null;
            _presentationReady = false;
        }

        private void EnsureFallbackView()
        {
            if (_fallbackView != null)
                return;

            var go = new GameObject("VVardenfell.BootstrapFallback");
            DontDestroyOnLoad(go);
            _fallbackView = go.AddComponent<BootstrapFallbackView>();
            _fallbackView.Initialize(OnFallbackPathChanged, ContinueFromFallbackPicker, GetBrowseCallback());
        }

        private void RefreshFallbackView()
        {
            EnsureFallbackView();

            switch (_stage)
            {
                case Stage.PickPath:
                    _fallbackView.ShowPathPicker(_path, _pathError);
                    break;
                case Stage.PickMode:
                    _fallbackView.ShowModePicker(_config?.InstallPath, OnFallbackModeSelected);
                    break;
                case Stage.PickProjectTamrielPath:
                    _fallbackView.ShowPathPicker(
                        _projectTamrielPath,
                        _projectTamrielPathError,
                        "VVardenfell - Locate Project Tamriel Data",
                        "Path to Project Tamriel install or Data Files");
                    break;
                case Stage.PickBattleground:
                    _fallbackView.ShowCombatBattlegroundPicker(
                        _combatBattlegroundCells,
                        _combatBattlegroundFilter,
                        OnFallbackBattlegroundFilterChanged,
                        OnFallbackBattlegroundSelected);
                    break;
                case Stage.Baking:
                    _fallbackView.ShowProgress(
                        "Optimizing",
                        _progress.Stage,
                        _progress.Label,
                        _progress.Current,
                        _progress.Total,
                        _progress.Fraction,
                        "You can leave this window open while the setup finishes.");
                    break;
                case Stage.Loading:
                    if (!_presentationReady)
                    {
                        _fallbackView.ShowProgress(
                            "VVardenfell - Loading World",
                            _loadProgress.Stage,
                            _loadProgress.Label,
                            _loadProgress.Current,
                            _loadProgress.Total,
                            _loadProgress.Fraction,
                            $"Elapsed: {_loadProgress.StageElapsedMs} ms");
                    }
                    else
                    {
                        _fallbackView.Hide();
                    }
                    break;
                case Stage.Failed:
                    _fallbackView.ShowError("VVardenfell - Error", _fatalError);
                    break;
                default:
                    _fallbackView.Hide();
                    break;
            }
        }

        private void OnFallbackPathChanged(string value)
        {
            if (_stage == Stage.PickProjectTamrielPath)
            {
                _projectTamrielPath = value ?? string.Empty;
                _projectTamrielPathError = null;
                return;
            }

            _path = value ?? string.Empty;
            _pathError = null;
        }

        private void ContinueFromFallbackPicker()
        {
            if (_stage == Stage.PickProjectTamrielPath)
            {
                ContinueFromProjectTamrielPicker();
                return;
            }

            var cfg = new MorrowindConfig { InstallPath = _path?.Trim() };
            if (cfg.IsValid(out var err))
            {
                ConfigStorage.Save(cfg);
                _config = cfg;
                _pathError = null;
                ApplyPersistedSettings(cfg);
                ShowModeSelection();
            }
            else
            {
                SetPathError(err);
            }
        }

        private void ContinueFromProjectTamrielPicker()
        {
            if (_config == null)
            {
                SetProjectTamrielPathError("Morrowind install must be configured before Project Tamriel.");
                return;
            }

            if (!InstalledContentSources.TryCreateProjectTamrielProfile(
                    _config.InstallPath,
                    _projectTamrielPath,
                    out MorrowindContentProfile profile,
                    out string error))
            {
                SetProjectTamrielPathError(error);
                return;
            }

            _config.ProjectTamrielProfile = profile;
            ConfigStorage.Save(_config);
            _projectTamrielPathError = null;
            BeginCacheFlow(BootstrapRuntimeMode.ProjectTamriel);
        }

        private void OnFallbackModeSelected(BootstrapRuntimeMode mode)
        {
            BeginCacheFlow(mode);
        }

        private void OnFallbackBattlegroundFilterChanged(string value)
        {
            _combatBattlegroundFilter = value ?? string.Empty;
        }

        private void OnFallbackBattlegroundSelected(int2 cell)
        {
            _pendingCombatBattlegroundSelection = false;
            _bootstrapOptions = BuildBootstrapOptions(
                BootstrapRuntimeMode.CombatSandbox,
                new BattleSimulatorBootSelection(cell));
            BeginLoading();
        }

        private static WorldBootstrapOptions BuildBootstrapOptions(BootstrapRuntimeMode mode)
        {
            return BuildBootstrapOptions(mode, null);
        }

        private static WorldBootstrapOptions BuildBootstrapOptions(BootstrapRuntimeMode mode, BattleSimulatorBootSelection? selection)
        {
            if (BootstrapRuntimeModeUtility.IsSandboxMode(mode))
            {
                var profile = selection.HasValue
                    ? SandboxWorldFixtures.Get(mode, selection.Value)
                    : SandboxWorldFixtures.Get(mode);
                if (profile != null)
                    return new WorldBootstrapOptions(mode, profile.PlayerStartPosition, profile.PlayerStartRotation, profile);
            }

            return new WorldBootstrapOptions(
                mode,
                WorldBootstrap.DefaultPlayerSpawnPosition(),
                quaternion.identity);
        }

        private static bool ShouldSkipPresentationForMode(BootstrapRuntimeMode mode)
        {
            return BootstrapRuntimeModeUtility.IsSandboxMode(mode);
        }

        private Action GetBrowseCallback()
        {
#if UNITY_EDITOR
            return BrowseForActivePath;
#else
            return null;
#endif
        }

#if UNITY_EDITOR
        private void BrowseForActivePath()
        {
            string title = _stage == Stage.PickProjectTamrielPath
                ? "Select Project Tamriel install or Data Files folder"
                : "Select Morrowind folder";
            string startPath = _stage == Stage.PickProjectTamrielPath
                ? ResolveInitialProjectTamrielPickerPath()
                : _path;
            var picked = UnityEditor.EditorUtility.OpenFolderPanel(title, startPath, "");
            if (!string.IsNullOrEmpty(picked))
            {
                if (_stage == Stage.PickProjectTamrielPath)
                {
                    _projectTamrielPath = picked;
                    _projectTamrielPathError = null;
                    return;
                }

                _path = picked;
                _pathError = null;
            }
        }
#endif

        private void SetPathError(string error)
        {
            _pathError = error;
            LogDisplayedError("Path", error);
        }

        private void SetProjectTamrielPathError(string error)
        {
            _projectTamrielPathError = error;
            LogDisplayedError("ProjectTamrielPath", error);
        }

        private void SetFatalError(string error)
        {
            _fatalError = error;
            LogDisplayedError("Fatal", error);
        }

        private void LogDisplayedError(string category, string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                return;

            string formatted = $"[VVardenfell][{category}] {error}";
            if (string.Equals(_lastLoggedDisplayedError, formatted, System.StringComparison.Ordinal))
                return;

            _lastLoggedDisplayedError = formatted;
            Debug.LogError(formatted);
        }

        private void OnDestroy()
        {
            DisposeRuntimeLifecycleQueries();

            if (_fallbackView != null)
                Destroy(_fallbackView.gameObject);

            if (Active == this)
                Active = null;
        }

        private void EnsureRuntimeLifecycleQueries(World world)
        {
            if (_runtimeLifecycleWorld == world)
                return;

            DisposeRuntimeLifecycleQueries();
            _runtimeLifecycleWorld = world;
            _runtimeActiveQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<MorrowindRuntimeActive>());
            _runtimePausedQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<MorrowindRuntimePaused>());
            _runtimeLifecycleQueriesCreated = true;
        }

        private void DisposeRuntimeLifecycleQueries()
        {
            if (_runtimeLifecycleQueriesCreated)
            {
                _runtimeActiveQuery.Dispose();
                _runtimePausedQuery.Dispose();
            }
            _runtimeLifecycleWorld = null;
            _runtimeLifecycleQueriesCreated = false;
        }
    }
}
