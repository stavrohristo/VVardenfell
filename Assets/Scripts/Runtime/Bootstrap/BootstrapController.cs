using System.Collections;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bake;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.UI;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace VVardenfell.Runtime.Bootstrap
{
    /// <summary>
    /// Boot state machine:
    /// PickPath -> user selects the Morrowind install folder if no valid config exists.
    /// Baking -> one-time conversion from BSA/ESM to the DOTS cache.
    /// Loading -> staged cache hydration + world install, hidden behind presentation.
    /// Ready -> world is loaded and waits behind the visual main menu until dismissed.
    /// Uses IMGUI to stay tiny and decoupled from URP/UI Toolkit.
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
            [Min(0f)] public float SkinWidth = 0.015f;
            [Min(0f)] public float MaxStepHeight = 0.35f;
            [Min(0f)] public float GroundProbeDistance = 0.3f;
            [Range(0f, 89f)] public float MaxSlopeDegrees = 50f;

            [Header("View")]
            public float StandingEyeHeight = 1.65f;
            public float CrouchingEyeHeight = 1.0f;
            public float MinPitch = -89f;
            public float MaxPitch = 89f;
            [Min(0.001f)] public float LookSensitivity = 0.12f;

            [Header("Ground")]
            [Min(0f)] public float GroundMaxSpeed = 5f;
            [Min(0f)] public float SprintSpeedMultiplier = 1.45f;
            [Min(0f)] public float CrouchSpeedMultiplier = 0.5f;
            [Min(0f)] public float GroundedMovementSharpness = 18f;

            [Header("Air")]
            [Min(0f)] public float AirAcceleration = 14f;
            [Min(0f)] public float AirMaxSpeed = 6f;
            [Min(0f)] public float AirDrag = 0.05f;
            public float Gravity = -20f;

            [Header("Jump")]
            [Min(0f)] public float JumpSpeed = 5f;
            [Min(0f)] public float CoyoteTime = 0.12f;
            [Min(0f)] public float JumpBufferTime = 0.12f;

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
                    SkinWidth = SkinWidth,
                    MaxStepHeight = MaxStepHeight,
                    GroundProbeDistance = GroundProbeDistance,
                    MaxSlopeCosine = math.cos(math.radians(MaxSlopeDegrees)),
                    GroundMaxSpeed = GroundMaxSpeed,
                    SprintSpeedMultiplier = SprintSpeedMultiplier,
                    CrouchSpeedMultiplier = CrouchSpeedMultiplier,
                    GroundedMovementSharpness = GroundedMovementSharpness,
                    AirAcceleration = AirAcceleration,
                    AirMaxSpeed = AirMaxSpeed,
                    AirDrag = AirDrag,
                    JumpSpeed = JumpSpeed,
                    Gravity = Gravity,
                    CoyoteTime = CoyoteTime,
                    JumpBufferTime = JumpBufferTime,
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
                SkinWidth = math.max(0f, SkinWidth);
                MaxStepHeight = math.max(0f, MaxStepHeight);
                GroundProbeDistance = math.max(0f, GroundProbeDistance);
                StandingEyeHeight = math.max(0.01f, StandingEyeHeight);
                CrouchingEyeHeight = math.max(0.01f, CrouchingEyeHeight);
                GroundMaxSpeed = math.max(0f, GroundMaxSpeed);
                SprintSpeedMultiplier = math.max(0f, SprintSpeedMultiplier);
                CrouchSpeedMultiplier = math.max(0f, CrouchSpeedMultiplier);
                GroundedMovementSharpness = math.max(0f, GroundedMovementSharpness);
                AirAcceleration = math.max(0f, AirAcceleration);
                AirMaxSpeed = math.max(0f, AirMaxSpeed);
                AirDrag = math.max(0f, AirDrag);
                JumpSpeed = math.max(0f, JumpSpeed);
                CoyoteTime = math.max(0f, CoyoteTime);
                JumpBufferTime = math.max(0f, JumpBufferTime);
                LookSensitivity = math.max(0.001f, LookSensitivity);
            }
        }

        private enum Stage { PickPath, Baking, Loading, Ready, Failed }
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
        private bool _presentationReady;
        private bool _loadStartRequested;

        public static PlayerCharacterComponent ResolvePlayerMovementSettings()
        {
            if (Active != null)
                return Active._playerMovement.Build();

            return new PlayerMovementSettings().Build();
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
            _playerMovement?.Validate();
        }

        private void OnValidate()
        {
            _playerMovement ??= new PlayerMovementSettings();
            _playerMovement.Validate();
        }

        private void Start()
        {
            if (ConfigStorage.TryLoad(out var c) && c.IsValid(out _))
            {
                _config = c;
                BeginCacheFlow();
            }
            else
            {
                _stage = Stage.PickPath;
                _path = GuessDefaultInstallPath();
            }
        }

        private void BeginCacheFlow()
        {
            _progress.Stage = "";
            _progress.Label = "";
            _progress.Current = 0;
            _progress.Total = 0;
            _progress.Error = null;
            _progress.Done = false;

            string esmPath = Path.Combine(_config.InstallPath, "Data Files", "Morrowind.esm");
            string bsaPath = Path.Combine(_config.InstallPath, "Data Files", "Morrowind.bsa");

            bool worldCacheValid = BakeManifest.TryRead(CachePaths.Manifest, out var worldManifest)
                                   && worldManifest.SourcesMatch(esmPath, bsaPath);
            bool uiCacheValid = UiCacheManifest.TryRead(CachePaths.UiManifest, out var uiManifest)
                                && uiManifest.SourcesMatch(_config.InstallPath)
                                && uiManifest.HasRequiredBootstrapImages(out _)
                                && MovieTranscodeBridge.CacheMatches(uiManifest, _config.InstallPath, out _);

            if (worldCacheValid && uiCacheValid)
            {
                BeginLoading();
            }
            else if (worldCacheValid)
            {
                _stage = Stage.Baking;
                StartCoroutine(BakeCoordinator.BakeUiOnly(_config, _progress));
            }
            else
            {
                _stage = Stage.Baking;
                StartCoroutine(BakeCoordinator.Bake(_config, _progress));
            }
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
        }

        private void BeginLoading()
        {
            if (!EnsurePresentation())
                return;

            _loadProgress.Reset();
            _loadProgress.BeginStage("Boot Sequence", "Waiting for intro sequence", 1);
            _loadProgress.Report("Waiting for intro sequence", 0, 1);
            _stage = Stage.Loading;
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
                WorldBootstrap.InstallIncremental(loader, _loadProgress),
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
            Debug.Log($"[VVardenfell] bootstrap completed in {sw.ElapsedMilliseconds}ms");
            _loadCoroutine = null;
            _loadStartRequested = false;
            _stage = Stage.Ready;
            if (_presentation == null)
                Destroy(gameObject);
        }

        private static string GuessDefaultInstallPath()
        {
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
        }

        private void OnGUI()
        {
            switch (_stage)
            {
                case Stage.PickPath: DrawPicker(); break;
                case Stage.Baking: DrawProgress(); break;
                case Stage.Loading:
                    if (!_presentationReady) DrawLoadProgress();
                    break;
                case Stage.Failed: DrawFailed(); break;
            }
        }

        private void DrawPicker()
        {
            const int w = 640, h = 220;
            var rect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(rect, "VVardenfell - Locate Morrowind Installation");

            GUILayout.BeginArea(new Rect(rect.x + 16, rect.y + 32, rect.width - 32, rect.height - 48));
            GUILayout.Label("Path to your Morrowind installation folder\n(the one containing 'Data Files'):");
            _path = GUILayout.TextField(_path ?? "", GUILayout.Height(22));

#if UNITY_EDITOR
            if (GUILayout.Button("Browse...", GUILayout.Width(100)))
            {
                var picked = UnityEditor.EditorUtility.OpenFolderPanel("Select Morrowind folder", _path, "");
                if (!string.IsNullOrEmpty(picked))
                    _path = picked;
            }
#endif

            GUILayout.Space(8);
            if (GUILayout.Button("Continue", GUILayout.Height(28)))
            {
                var cfg = new MorrowindConfig { InstallPath = _path?.Trim() };
                if (cfg.IsValid(out var err))
                {
                    ConfigStorage.Save(cfg);
                    _config = cfg;
                    _pathError = null;
                    BeginCacheFlow();
                }
                else
                {
                    SetPathError(err);
                }
            }

            if (!string.IsNullOrEmpty(_pathError))
            {
                var style = new GUIStyle(GUI.skin.label) { normal = { textColor = Color.red }, wordWrap = true };
                GUILayout.Label(_pathError, style);
            }
            GUILayout.EndArea();
        }

        private void DrawProgress()
        {
            const int w = 640, h = 180;
            var rect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(rect, "VVardenfell - Baking cache");

            GUILayout.BeginArea(new Rect(rect.x + 16, rect.y + 32, rect.width - 32, rect.height - 48));
            GUILayout.Label($"Stage: {_progress.Stage}");
            GUILayout.Label($"{_progress.Label}   {_progress.Current}/{_progress.Total}");

            var barOuter = GUILayoutUtility.GetRect(w - 32, 18);
            GUI.Box(barOuter, GUIContent.none);
            float f = _progress.Fraction;
            var barInner = new Rect(barOuter.x, barOuter.y, barOuter.width * f, barOuter.height);
            GUI.Box(barInner, GUIContent.none);

            GUILayout.Label("First boot only - subsequent boots load the cache directly.",
                new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11 });
            GUILayout.EndArea();
        }

        private void DrawLoadProgress()
        {
            const int w = 640, h = 180;
            var rect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(rect, "VVardenfell - Loading world");

            GUILayout.BeginArea(new Rect(rect.x + 16, rect.y + 32, rect.width - 32, rect.height - 48));
            GUILayout.Label($"Stage: {_loadProgress.Stage}");
            GUILayout.Label($"{_loadProgress.Label}   {_loadProgress.Current}/{_loadProgress.Total}");

            var barOuter = GUILayoutUtility.GetRect(w - 32, 18);
            GUI.Box(barOuter, GUIContent.none);
            float f = _loadProgress.Fraction;
            var barInner = new Rect(barOuter.x, barOuter.y, barOuter.width * f, barOuter.height);
            GUI.Box(barInner, GUIContent.none);

            GUILayout.Label($"Elapsed: {_loadProgress.StageElapsedMs} ms",
                new GUIStyle(GUI.skin.label) { fontSize = 11 });
            GUILayout.EndArea();
        }

        private void DrawFailed()
        {
            const int w = 640, h = 180;
            var rect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(rect, "VVardenfell - Error");
            var style = new GUIStyle(GUI.skin.label) { normal = { textColor = Color.red }, wordWrap = true };
            GUI.Label(new Rect(rect.x + 16, rect.y + 32, rect.width - 32, rect.height - 48), _fatalError, style);
        }

        private bool EnsurePresentation()
        {
            if (_presentation != null)
                return true;

            try
            {
                var uiAssets = new UiAssetLoader().Load();
                var go = new GameObject("VVardenfell.Presentation");
                DontDestroyOnLoad(go);
                _presentation = go.AddComponent<BootstrapPresentationView>();
                _presentation.Initialize(uiAssets, _loadProgress, _config.InstallPath, OnPresentationLoadingPhaseReady);
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

        private void DestroyPresentation()
        {
            if (_presentation != null)
                Destroy(_presentation.gameObject);
            _presentation = null;
            _presentationReady = false;
        }

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
            if (Active == this)
                Active = null;
        }
    }
}
