using System;
using System.Collections;
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

        private enum Stage { PickPath, PickMode, Baking, Loading, Ready, Failed }
        public static BootstrapController Active { get; private set; }

        [Header("Player Movement")]
        [SerializeField] private PlayerMovementSettings _playerMovement = new PlayerMovementSettings();

        private Stage _stage = Stage.PickPath;
        private string _path = "";
        private string _pathError;
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
            RuntimeShellPresentationGate.Reset();
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
            VVardenfell.Runtime.UI.Shell.HudUserPreferences.ShowCrosshair = config.ShowCrosshair;
            VVardenfell.Runtime.UI.Shell.HudUserPreferences.ShowSubtitles = config.ShowSubtitles;

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
            _bootstrapOptions = BuildBootstrapOptions(mode);
            _progress.Stage = "";
            _progress.Label = "";
            _progress.Current = 0;
            _progress.Total = 0;
            _progress.Error = null;
            _progress.Done = false;

            string esmPath = Path.Combine(_config.InstallPath, "Data Files", "Morrowind.esm");
            string bsaPath = Path.Combine(_config.InstallPath, "Data Files", "Morrowind.bsa");
            string[] gameplayRecordSources = InstalledContentSources.ResolveGameplayRecordSources(_config.InstallPath);
            string[] gameplaySources = InstalledContentSources.ResolveGameplayDependencySources(_config.InstallPath);

            bool worldCacheValid = BakeManifest.TryRead(CachePaths.Manifest, out var worldManifest)
                                   && worldManifest.SourcesMatch(esmPath, bsaPath, gameplayRecordSources)
                                   && WorldCachePipelineMatchesRuntime(worldManifest);
            bool uiCacheValid = UiCacheManifest.TryRead(CachePaths.UiManifest, out var uiManifest)
                                && uiManifest.SourcesMatch(_config.InstallPath)
                                && uiManifest.HasRequiredBootstrapImages(out _)
                                && MovieTranscodeBridge.CacheMatches(uiManifest, _config.InstallPath, out _);
            bool gameplayCacheValid = File.Exists(CachePaths.GameplayContent)
                                      && GameplayContentManifest.TryRead(CachePaths.GameplayContentManifest, out var gameplayManifest)
                                      && gameplayManifest.SourcesMatch(gameplaySources);

            if (worldCacheValid && uiCacheValid && gameplayCacheValid)
            {
                BeginLoading();
            }
            else if (worldCacheValid && uiCacheValid)
            {
                _stage = Stage.Baking;
                StartCoroutine(BakeCoordinator.BakeGameplayOnly(_config, _progress));
            }
            else if (worldCacheValid && gameplayCacheValid)
            {
                _stage = Stage.Baking;
                StartCoroutine(BakeCoordinator.BakeUiOnly(_config, _progress));
            }
            else if (worldCacheValid)
            {
                _stage = Stage.Baking;
                StartCoroutine(BakeCoordinator.BakeUiAndGameplayOnly(_config, _progress));
            }
            else
            {
                _stage = Stage.Baking;
                StartCoroutine(BakeCoordinator.Bake(_config, _progress));
            }
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

                BeginLoading();
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
                MorrowindRuntimeLifecycleUtility.RemoveRuntimeLifecycle(world.EntityManager);

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
            _path = value ?? string.Empty;
            _pathError = null;
        }

        private void ContinueFromFallbackPicker()
        {
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

        private void OnFallbackModeSelected(BootstrapRuntimeMode mode)
        {
            BeginCacheFlow(mode);
        }

        private static WorldBootstrapOptions BuildBootstrapOptions(BootstrapRuntimeMode mode)
        {
            if (mode == BootstrapRuntimeMode.Sandbox)
            {
                var profile = SandboxWorldFixtures.Active;
                if (profile != null)
                    return new WorldBootstrapOptions(mode, profile.PlayerStartPosition, profile.PlayerStartRotation, profile);
            }

            if (mode == BootstrapRuntimeMode.VegetationSandbox)
            {
                var profile = SandboxWorldFixtures.VegetationStress;
                if (profile != null)
                    return new WorldBootstrapOptions(mode, profile.PlayerStartPosition, profile.PlayerStartRotation, profile);
            }

            return WorldBootstrapOptions.Vanilla;
        }

        private static bool ShouldSkipPresentationForMode(BootstrapRuntimeMode mode)
        {
            return BootstrapRuntimeModeUtility.IsSandboxMode(mode);
        }

        private Action GetBrowseCallback()
        {
#if UNITY_EDITOR
            return BrowseForInstallPath;
#else
            return null;
#endif
        }

#if UNITY_EDITOR
        private void BrowseForInstallPath()
        {
            var picked = UnityEditor.EditorUtility.OpenFolderPanel("Select Morrowind folder", _path, "");
            if (!string.IsNullOrEmpty(picked))
            {
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
            if (_fallbackView != null)
                Destroy(_fallbackView.gameObject);

            if (Active == this)
                Active = null;
        }
    }
}
