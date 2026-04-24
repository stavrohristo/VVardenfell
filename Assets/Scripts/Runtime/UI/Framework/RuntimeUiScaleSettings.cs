using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace VVardenfell.Runtime.UI.Framework
{
    public static class RuntimeUiScaleSettings
    {
        public const float DefaultGlobalScale = 1f;
        public const float DefaultHudScale = 2f;
        const float MinimumGlobalScale = 0.25f;
        const float MinimumHudScale = 0.5f;

        static float _globalScale = DefaultGlobalScale;
        static float _hudScale = DefaultHudScale;

        /// <summary>
        /// Fired after either <see cref="GlobalScale"/> or <see cref="HudScale"/> is
        /// written, so views that cache scaled dimensions (e.g. the HUD bars, open
        /// windows) can rebuild. Always invoked on the main thread â€” caller is
        /// Options â†’ config apply. Safe to ignore if the view rebuilds
        /// incrementally on open instead of on change.
        /// </summary>
        public static event System.Action OnScaleChanged;

        /// <summary>Menu / window / dialog scale. Applied to every pixel that flows
        /// through <see cref="ScalePixels(float)"/> / <see cref="ScaleFont(float)"/>.</summary>
        public static float GlobalScale
        {
            get => _globalScale;
            set
            {
                float clamped = Mathf.Max(MinimumGlobalScale, value);
                if (Mathf.Approximately(_globalScale, clamped))
                    return;
                _globalScale = clamped;
                OnScaleChanged?.Invoke();
            }
        }

        /// <summary>HUD scale. Applied independently of <see cref="GlobalScale"/>
        /// â€” the HUD uses its own multiplier so players can size menus and HUD
        /// separately (matches vanilla MW's convention). Defaults to 2 for
        /// backward-compat with the previously hardcoded HUD multiplier.</summary>
        public static float HudScale
        {
            get => _hudScale;
            set
            {
                float clamped = Mathf.Max(MinimumHudScale, value);
                if (Mathf.Approximately(_hudScale, clamped))
                    return;
                _hudScale = clamped;
                OnScaleChanged?.Invoke();
            }
        }

        // Scale knobs are now applied at the CanvasScaler level (via
        // RuntimeUiScaleBinding setting referenceResolution = Base / scale) rather
        // than at per-widget pixel math, so a scale change live-resizes every
        // widget on the canvas without needing to rebuild. These helpers are kept
        // as pass-throughs for the many call sites that already wrap reference
        // pixels through them; the multiplication has moved up one layer.
        public static float ScalePixels(float value) => value;

        public static Vector2 ScalePixels(Vector2 value) => value;

        public static float ScaleFont(float value) => value;

        public static int ScaleFontSize(int value) => Mathf.Max(1, value);

        /// <summary>HUD pixel scaling. HUD lives on its own Canvas with its
        /// reference resolution divided by <see cref="HudScale"/>, so identity here
        /// preserves today's default 2Ã— visual at HudScale=2 while letting the
        /// knob live-resize when the user drags it.</summary>
        public static float ScaleHudPixels(float value) => value;

        public static Vector2 ScaleHudPixels(Vector2 value) => value;
    }

    /// <summary>
    /// Attach to a GameObject that carries a <see cref="CanvasScaler"/>. Binds the
    /// scaler's <see cref="CanvasScaler.referenceResolution"/> to one of the two
    /// user-facing UI scale knobs in <see cref="RuntimeUiScaleSettings"/>, so when
    /// the player drags UI Scale / HUD Scale in Options the canvas live-rescales
    /// everything underneath it without any per-widget rebuild.
    /// </summary>
    public sealed class RuntimeUiScaleBinding : MonoBehaviour
    {
        public enum ScaleKind
        {
            Global,
            Hud,
        }

        [SerializeField] ScaleKind _kind = ScaleKind.Global;
        CanvasScaler _scaler;
        Vector2 _baseReference = RuntimeUiFactory.ReferenceResolution;

        public void Configure(CanvasScaler scaler, ScaleKind kind, Vector2 baseReference)
        {
            _scaler = scaler;
            _kind = kind;
            _baseReference = baseReference.sqrMagnitude > 0.0001f ? baseReference : RuntimeUiFactory.ReferenceResolution;
            Apply();
        }

        void Awake()
        {
            if (_scaler == null)
                _scaler = GetComponent<CanvasScaler>();
        }

        void OnEnable()
        {
            RuntimeUiScaleSettings.OnScaleChanged += Apply;
            Apply();
        }

        void OnDisable()
        {
            RuntimeUiScaleSettings.OnScaleChanged -= Apply;
        }

        void Apply()
        {
            if (_scaler == null)
                _scaler = GetComponent<CanvasScaler>();
            if (_scaler == null)
                return;

            float scale = _kind == ScaleKind.Hud
                ? RuntimeUiScaleSettings.HudScale
                : RuntimeUiScaleSettings.GlobalScale;
            if (scale < 0.01f)
                scale = 0.01f;

            _scaler.referenceResolution = _baseReference / scale;
        }
    }
}
