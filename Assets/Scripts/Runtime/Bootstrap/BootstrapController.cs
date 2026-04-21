using System.IO;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bake;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.Bootstrap
{
    /// <summary>
    /// Boot state machine:
    ///   PickPath  → user selects Morrowind install folder (only if no valid config)
    ///   Baking    → one-time conversion from BSA/ESM to the DOTS cache
    ///   Loading   → hydrate cache, spawn world
    ///   Done      → destroy self
    /// Uses IMGUI to stay tiny and decoupled from URP/UI Toolkit.
    /// </summary>
    public class BootstrapController : MonoBehaviour
    {
        private enum Stage { PickPath, Baking, Loading, Failed }

        private Stage _stage = Stage.PickPath;
        private string _path = "";
        private string _pathError;
        private string _fatalError;
        private MorrowindConfig _config;
        private readonly BakeProgress _progress = new BakeProgress();

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
            string esmPath = Path.Combine(_config.InstallPath, "Data Files", "Morrowind.esm");
            string bsaPath = Path.Combine(_config.InstallPath, "Data Files", "Morrowind.bsa");

            bool cacheValid = BakeManifest.TryRead(CachePaths.Manifest, out var manifest)
                              && manifest.SourcesMatch(esmPath, bsaPath);

            if (cacheValid)
            {
                _stage = Stage.Loading;
                DoLoad();
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
                    _fatalError = $"Bake failed: {_progress.Error}";
                    _stage = Stage.Failed;
                    return;
                }
                _stage = Stage.Loading;
                DoLoad();
            }
        }

        private void DoLoad()
        {
            var loader = new CacheLoader();
            if (!loader.TryLoad(out var err))
            {
                _fatalError = $"Cache load failed: {err}";
                _stage = Stage.Failed;
                return;
            }
            try
            {
                WorldBootstrap.Install(loader);
                Destroy(gameObject);
            }
            catch (System.Exception ex)
            {
                _fatalError = $"World spawn failed: {ex.Message}";
                _stage = Stage.Failed;
            }
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
                if (Directory.Exists(c)) return c;
            return "";
        }

        private void OnGUI()
        {
            switch (_stage)
            {
                case Stage.PickPath: DrawPicker(); break;
                case Stage.Baking: DrawProgress(); break;
                case Stage.Loading: DrawSimple("Loading cache…"); break;
                case Stage.Failed: DrawFailed(); break;
            }
        }

        private void DrawPicker()
        {
            const int w = 640, h = 220;
            var rect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(rect, "VVardenfell — Locate Morrowind Installation");

            GUILayout.BeginArea(new Rect(rect.x + 16, rect.y + 32, rect.width - 32, rect.height - 48));
            GUILayout.Label("Path to your Morrowind installation folder\n(the one containing 'Data Files'):");
            _path = GUILayout.TextField(_path ?? "", GUILayout.Height(22));

#if UNITY_EDITOR
            if (GUILayout.Button("Browse...", GUILayout.Width(100)))
            {
                var picked = UnityEditor.EditorUtility.OpenFolderPanel("Select Morrowind folder", _path, "");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
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
                    _pathError = err;
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
            GUI.Box(rect, "VVardenfell — Baking cache");

            GUILayout.BeginArea(new Rect(rect.x + 16, rect.y + 32, rect.width - 32, rect.height - 48));
            GUILayout.Label($"Stage: {_progress.Stage}");
            GUILayout.Label($"{_progress.Label}   {_progress.Current}/{_progress.Total}");

            // Simple progress bar using the GUI box.
            var barOuter = GUILayoutUtility.GetRect(w - 32, 18);
            GUI.Box(barOuter, GUIContent.none);
            float f = _progress.Fraction;
            var barInner = new Rect(barOuter.x, barOuter.y, barOuter.width * f, barOuter.height);
            GUI.Box(barInner, GUIContent.none);

            GUILayout.Label("First boot only — subsequent boots load the cache directly.",
                new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11 });
            GUILayout.EndArea();
        }

        private void DrawSimple(string msg)
        {
            const int w = 400, h = 80;
            var rect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(rect, "VVardenfell");
            GUI.Label(new Rect(rect.x + 16, rect.y + 32, rect.width - 32, 24), msg);
        }

        private void DrawFailed()
        {
            const int w = 640, h = 180;
            var rect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(rect, "VVardenfell — Error");
            var style = new GUIStyle(GUI.skin.label) { normal = { textColor = Color.red }, wordWrap = true };
            GUI.Label(new Rect(rect.x + 16, rect.y + 32, rect.width - 32, rect.height - 48), _fatalError, style);
        }
    }
}
