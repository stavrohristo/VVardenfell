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
        /// Creates an additional Screen-Space-Overlay canvas as a child GameObject
        /// under <paramref name="parent"/>. Used by the in-game shell to host the
        /// HUD on its own canvas so it can scale by <c>HudScale</c> while the
        /// menus on the primary canvas scale by <c>GlobalScale</c> independently.
        /// </summary>
        public static RuntimeUiCanvasView CreateChildCanvasRoot(
            Transform parent,
            string canvasName,
            string rootName,
            int sortingOrder,
            RuntimeUiScaleBinding.ScaleKind scaleKind)
        {
            var go = new GameObject(canvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(RuntimeUiScaleBinding));
            go.transform.SetParent(parent, false);

            // Nested Screen-Space-Overlay canvases are treated as sub-canvases
            // and do NOT auto-size their RectTransform to the screen the way
            // root canvases do. Stretch it to match the parent rect explicitly
            // so children anchored to corners land on screen corners rather
            // than collapsing into the default 100Ã—100 centered rect.
            var selfRect = (RectTransform)go.transform;
            Stretch(selfRect);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            canvas.overrideSorting = true;

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
}
