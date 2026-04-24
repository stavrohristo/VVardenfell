using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace VVardenfell.Runtime.UI.Framework
{
    public static partial class RuntimeUiFactory
    {
        public static EventSystem EnsureEventSystem()
        {
            var eventSystem = Object.FindAnyObjectByType<EventSystem>();
            if (eventSystem != null)
                return eventSystem;

            var go = new GameObject("VVardenfell.EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            Object.DontDestroyOnLoad(go);
            return go.GetComponent<EventSystem>();
        }

        public static RuntimeUiCanvasView CreateCanvasRoot(GameObject owner, string rootName, int sortingOrder)
        {
            var canvas = owner.GetComponent<Canvas>();
            if (canvas == null)
                canvas = owner.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = owner.GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = owner.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = MatchWidthOrHeight;

            var raycaster = owner.GetComponent<GraphicRaycaster>();
            if (raycaster == null)
                raycaster = owner.AddComponent<GraphicRaycaster>();

            // Attach a live-scale binding so UI Scale changes from the Options
            // menu rescale this canvas without rebuilding. The main-menu /
            // generic canvas binds to GlobalScale; the in-game shell creates a
            // second child canvas for the HUD and rebinds it to HudScale.
            var binding = owner.GetComponent<RuntimeUiScaleBinding>();
            if (binding == null)
                binding = owner.AddComponent<RuntimeUiScaleBinding>();
            binding.Configure(scaler, RuntimeUiScaleBinding.ScaleKind.Global, ReferenceResolution);

            var eventSystem = EnsureEventSystem();
            var root = CreateStretchRect(rootName, owner.transform);

            return new RuntimeUiCanvasView
            {
                Canvas = canvas,
                Scaler = scaler,
                Raycaster = raycaster,
                Root = root,
                EventSystem = eventSystem,
            };
        }

        /// <summary>
        /// Creates a second Screen-Space-Overlay canvas as a scene-root
        /// GameObject (sibling to <paramref name="lifetime"/>, not nested under
        /// it). Unity treats a Canvas nested inside another Canvas as a
        /// sub-canvas and silently ignores its own <see cref="CanvasScaler"/>
        /// — the child inherits the parent canvas's scale factor. To give the
        /// HUD an independent scale knob via <c>HudScale</c> we need a true
        /// root canvas. A <see cref="RuntimeCanvasLifetimeLink"/> companion
        /// destroys the canvas GameObject alongside the owner so callers keep
        /// a one-object lifecycle.
        /// </summary>
        public static RuntimeUiCanvasView CreateSiblingCanvasRoot(
            GameObject lifetime,
            string canvasName,
            string rootName,
            int sortingOrder,
            RuntimeUiScaleBinding.ScaleKind scaleKind)
        {
            var go = new GameObject(canvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(RuntimeUiScaleBinding));
            // Intentionally leave the transform at scene root — nested
            // canvases share their root's scale factor, which would defeat
            // this helper's whole purpose.

            if (lifetime != null)
            {
                var link = go.AddComponent<RuntimeCanvasLifetimeLink>();
                link.Bind(lifetime);
            }

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = MatchWidthOrHeight;

            var binding = go.GetComponent<RuntimeUiScaleBinding>();
            binding.Configure(scaler, scaleKind, ReferenceResolution);

            var raycaster = go.GetComponent<GraphicRaycaster>();
            var eventSystem = EnsureEventSystem();
            var root = CreateStretchRect(rootName, go.transform);

            return new RuntimeUiCanvasView
            {
                Canvas = canvas,
                Scaler = scaler,
                Raycaster = raycaster,
                Root = root,
                EventSystem = eventSystem,
            };
        }
    }

    /// <summary>
    /// Binds a scene-root canvas to an owning GameObject. When the owner is
    /// destroyed, this component destroys its own GameObject. When the owner
    /// toggles between active and inactive in the scene hierarchy, this
    /// component mirrors the state onto the local <see cref="Canvas.enabled"/>
    /// flag — NOT <c>gameObject.SetActive</c>, because deactivating our own
    /// GameObject would stop <c>LateUpdate</c> from running, trapping us in
    /// the off state forever. Used by
    /// <see cref="RuntimeUiFactory.CreateSiblingCanvasRoot"/> so a
    /// scene-root HUD canvas still tracks its owning shell's lifecycle.
    /// </summary>
    public sealed class RuntimeCanvasLifetimeLink : MonoBehaviour
    {
        [SerializeField] GameObject _owner;
        Canvas _canvas;
        GraphicRaycaster _raycaster;
        bool _lastOwnerActive = true;

        public void Bind(GameObject owner)
        {
            _owner = owner;
            _canvas = GetComponent<Canvas>();
            _raycaster = GetComponent<GraphicRaycaster>();
            if (_owner != null)
            {
                _lastOwnerActive = _owner.activeInHierarchy;
                Apply(_lastOwnerActive);
            }
        }

        void LateUpdate()
        {
            if (_owner == null)
            {
                UnityEngine.Object.Destroy(gameObject);
                return;
            }

            bool ownerActive = _owner.activeInHierarchy;
            if (ownerActive != _lastOwnerActive)
            {
                Apply(ownerActive);
                _lastOwnerActive = ownerActive;
            }
        }

        void Apply(bool active)
        {
            if (_canvas != null)
                _canvas.enabled = active;
            if (_raycaster != null)
                _raycaster.enabled = active;
        }
    }
}
