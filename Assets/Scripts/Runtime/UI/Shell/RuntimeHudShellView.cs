using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VVardenfell.Runtime.Audio;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.UI.Assets;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    public sealed class RuntimeHudShellView : MonoBehaviour
    {
        readonly struct PauseMenuButtonDefinition
        {
            public PauseMenuButtonDefinition(RuntimeShellMenuActionId action, string label, bool available)
            {
                Action = action;
                Label = label;
                Available = available;
            }

            public RuntimeShellMenuActionId Action { get; }
            public string Label { get; }
            public bool Available { get; }
        }

        sealed class PauseMenuButtonView
        {
            public RuntimeShellMenuActionId Action;
            public bool Available;
            public Button Button;
            public Image Background;
            public BitmapTextGraphic Label;
        }

        static readonly PauseMenuButtonDefinition[] k_ButtonDefinitions =
        {
            new(RuntimeShellMenuActionId.Resume, "Resume", true),
            new(RuntimeShellMenuActionId.Inventory, "Inventory", true),
            new(RuntimeShellMenuActionId.SaveGame, "Save Game", true),
            new(RuntimeShellMenuActionId.LoadGame, "Load Game", true),
            new(RuntimeShellMenuActionId.Options, "Options", true),
            new(RuntimeShellMenuActionId.MainMenu, "Main Menu", true),
            new(RuntimeShellMenuActionId.ExitGame, "Exit Game", true),
        };

        // Pause menu geometry. Vanilla MW has no window chrome here — the buttons
        // float on a dim backdrop, stacked vertically, centered on the screen. The
        // column width is just wide enough for the widest localized caption.
        const float PauseButtonHeight = 28f;
        const float PauseButtonSpacing = 8f;
        const float PauseButtonWidth = 240f;

        // Modal dialog geometry. Vanilla uses MW_Dialog (thin MW_Box border) for the
        // small transient confirmation / message modals — not the thick outer+inner
        // border reserved for full windows (Stats, Inventory, etc.).
        const float ModalWidth = 380f;
        const float ModalHeight = 160f;
        const float ModalTitlePixelHeight = 14f;
        const float ModalBodyPixelHeight = 13f;

        RuntimeUiTheme _theme;
        RuntimeInventoryIconService _iconService;
        EventSystem _eventSystem;
        RectTransform _rootRect;
        RectTransform _suiteRoot;
        RuntimeHudView _hudView;
        InventoryWindowView _inventoryView;
        ContainerWindowView _containerView;
        StatsWindowView _statsView;
        SpellWindowView _spellView;
        MapWindowView _mapView;
        SaveLoadBrowserView _saveLoadBrowserView;
        OptionsWindowView _optionsView;
        VVardenfell.Core.Config.MorrowindConfig _config;

        RectTransform _pauseRoot;
        RectTransform _modalRoot;
        BitmapTextGraphic _modalTitle;
        BitmapTextGraphic _modalBody;
        bool _inventoryVisible;
        bool _containerVisible;
        bool _pauseMenuOpen;
        bool _modalOpen;
        bool _optionsOpen;

        readonly List<PauseMenuButtonView> _buttons = new();

        public static RuntimeHudShellView Create()
        {
            var go = new GameObject("VVardenfell.RuntimeHudShell");
            DontDestroyOnLoad(go);
            var view = go.AddComponent<RuntimeHudShellView>();
            view.Initialize();
            return view;
        }

        void Initialize()
        {
            _theme = RuntimeUiTheme.FromAssets(new UiAssetLoader().Load());
            _iconService = new RuntimeInventoryIconService();

            // Primary canvas hosts every menu / window / pause / modal and scales
            // with GlobalScale (via the RuntimeUiScaleBinding that CreateCanvasRoot
            // attaches automatically). HUD gets its own child canvas below so it
            // can scale independently via HudScale without touching the menus.
            var canvasView = RuntimeUiFactory.CreateCanvasRoot(gameObject, "Root", short.MaxValue - 32);
            _eventSystem = canvasView.EventSystem;
            _rootRect = canvasView.Root;

            // Dedicated HUD canvas. Lower sortingOrder so menus draw on top when
            // opened, and bound to HudScale so the Video tab's HUD Scale slider
            // live-resizes gauges / crosshair / status bars.
            var hudCanvas = RuntimeUiFactory.CreateChildCanvasRoot(
                transform,
                "HudCanvas",
                "HudRoot",
                short.MaxValue - 96,
                RuntimeUiScaleBinding.ScaleKind.Hud);
            _hudView = new RuntimeHudView(hudCanvas.Root, _theme);
            _suiteRoot = RuntimeUiFactory.CreateStretchRect("SuiteRoot", _rootRect);
            _inventoryView = new InventoryWindowView(
                _suiteRoot,
                _suiteRoot,
                _theme,
                _iconService,
                RequestInventoryWindowRectUpdate,
                RequestInventoryCategory,
                RequestInventorySelection,
                RequestInventoryFilterText,
                onPinToggled: () => RequestTogglePin(VVardenfell.Runtime.Components.RuntimeShellPinnableWindow.Inventory));
            _containerView = new ContainerWindowView(
                _suiteRoot,
                _suiteRoot,
                _theme,
                _iconService,
                RequestContainerWindowRectUpdate,
                RequestContainerSelection,
                RequestTakeSelected,
                RequestTakeAll,
                RequestCloseContainer);
            _statsView = new StatsWindowView(
                _suiteRoot,
                _suiteRoot,
                _theme,
                RequestStatsWindowRectUpdate,
                onPinToggled: () => RequestTogglePin(VVardenfell.Runtime.Components.RuntimeShellPinnableWindow.Stats));
            _spellView = new SpellWindowView(
                _suiteRoot,
                _suiteRoot,
                _theme,
                RequestSpellWindowRectUpdate,
                RequestSpellSelection,
                onPinToggled: () => RequestTogglePin(VVardenfell.Runtime.Components.RuntimeShellPinnableWindow.Spell));
            _mapView = new MapWindowView(
                _suiteRoot,
                _suiteRoot,
                _theme,
                RequestMapWindowRectUpdate,
                onPinToggled: () => RequestTogglePin(VVardenfell.Runtime.Components.RuntimeShellPinnableWindow.Map));
            _saveLoadBrowserView = new SaveLoadBrowserView(
                _rootRect,
                _theme,
                RequestSaveLoadSelectSlot,
                RequestSaveLoadName,
                RequestSaveLoadNew,
                RequestSaveLoadOverwrite,
                RequestSaveLoadLoad,
                RequestSaveLoadDelete,
                RequestSaveLoadCancel,
                RequestSaveLoadConfirm,
                RequestSaveLoadCancelConfirm);

            // Load or create the persistent config — the Options window reads/writes
            // through this shared instance so every slider change is persisted on
            // close without re-loading from disk.
            if (!VVardenfell.Core.Config.ConfigStorage.TryLoad(out _config) || _config == null)
                _config = new VVardenfell.Core.Config.MorrowindConfig();

            _optionsView = new OptionsWindowView(
                _rootRect,
                _theme,
                _config,
                BuildOptionsCallbacks());

            BuildPauseMenu();
            BuildModal();
            gameObject.SetActive(false);
        }

        public void Sync(
            bool visible,
            RuntimeHudViewModel hudModel,
            InventoryWindowViewModel inventoryModel,
            ContainerWindowViewModel containerModel,
            StatsWindowViewModel statsModel,
            SpellWindowViewModel spellModel,
            MapWindowViewModel mapModel,
            SaveLoadBrowserViewModel saveLoadModel,
            RuntimeShellMenuActionId selectedAction,
            bool pauseMenuOpen,
            bool modalOpen,
            bool optionsOpen,
            string modalTitle,
            string modalBody)
        {
            if (_rootRect == null)
                return;

            gameObject.SetActive(visible);
            if (!visible)
            {
                ClearSelectionIfOwned();
                _inventoryVisible = false;
                _containerVisible = false;
                _pauseMenuOpen = false;
                _modalOpen = false;
                _optionsOpen = false;
                _optionsView?.SetVisible(false);
                return;
            }

            // With MW_Window_Pinnable, each subwindow's visibility is now
            // independent: the presentation system passes non-null models only
            // for the ones that should render (group-open OR individually
            // pinned). The container owns its own flag and never participates
            // in the pin group. We keep _suiteRoot active any time any
            // group-subwindow OR the container is showing so the shared parent
            // isn't hiding individual children.
            bool inventoryVisible = inventoryModel != null;
            bool containerVisible = containerModel != null;
            bool statsVisible = statsModel != null;
            bool spellVisible = spellModel != null;
            bool mapVisible = mapModel != null;
            bool saveLoadVisible = saveLoadModel != null;
            bool inventoryOpened = !_inventoryVisible && inventoryVisible;
            bool containerOpened = !_containerVisible && containerVisible;
            bool pauseOpened = !_pauseMenuOpen && pauseMenuOpen;
            bool modalClosed = _modalOpen && !modalOpen;

            _inventoryVisible = inventoryVisible;
            _containerVisible = containerVisible;
            _pauseMenuOpen = pauseMenuOpen;
            _modalOpen = modalOpen;

            _hudView.Sync(hudModel);
            _suiteRoot.gameObject.SetActive(
                inventoryVisible || containerVisible || statsVisible || spellVisible || mapVisible);

            _inventoryView.Sync(inventoryModel);
            _containerView.Sync(containerModel);
            _statsView.Sync(statsModel);
            _spellView.Sync(spellModel);
            _mapView.Sync(mapModel);

            // Options sits on top of the pause menu while open; while it's visible
            // the pause backdrop + any save/load overlay stay hidden so the user
            // sees a single focused dialog.
            bool optionsTransitioning = _optionsOpen != optionsOpen;
            _optionsOpen = optionsOpen;
            if (_optionsView != null)
            {
                _optionsView.SetVisible(optionsOpen);
                if (optionsTransitioning && !optionsOpen)
                    SaveConfig();
            }

            _pauseRoot.gameObject.SetActive(pauseMenuOpen && !saveLoadVisible && !optionsOpen);
            _modalRoot.gameObject.SetActive(pauseMenuOpen && modalOpen && !optionsOpen);
            _saveLoadBrowserView.Sync(optionsOpen ? null : saveLoadModel);

            if (_modalRoot.gameObject.activeSelf)
            {
                _modalTitle.Text = string.IsNullOrWhiteSpace(modalTitle) ? "Unavailable" : modalTitle.Trim();
                _modalBody.Text = string.IsNullOrWhiteSpace(modalBody)
                    ? "This action is not available in the current runtime shell slice."
                    : modalBody.Trim();
                _eventSystem?.SetSelectedGameObject(null);
            }
            else if (pauseMenuOpen && (pauseOpened || modalClosed))
            {
                RestorePauseSelection(selectedAction);
            }
            else if (inventoryOpened || containerOpened)
            {
                ClearSelectionIfOwned();
            }

            if (!pauseMenuOpen && !inventoryVisible && !containerVisible && !saveLoadVisible)
                ClearSelectionIfOwned();
        }

        // Vanilla MW pause/main menu has no window chrome. The buttons float on a dim
        // backdrop, centered vertically as a stack, one per action. OpenMW renders
        // this in C++ (no layout XML for the pause menu), and vanilla MW's bitmap
        // menus do the same. We match that: backdrop + centered button column,
        // nothing else.
        void BuildPauseMenu()
        {
            _pauseRoot = RuntimeUiFactory.CreateStretchRect("PauseRoot", _rootRect);
            _pauseRoot.gameObject.SetActive(false);

            var blocker = RuntimeUiFactory.CreateImage("PauseBackdrop", _pauseRoot, new Color(0f, 0f, 0f, 0.66f));
            blocker.raycastTarget = true;
            RuntimeUiFactory.Stretch(blocker.rectTransform);

            float buttonWidth = RuntimeClassicUiMetrics.Ui(PauseButtonWidth);
            float buttonHeight = RuntimeClassicUiMetrics.Ui(PauseButtonHeight);
            float buttonSpacing = RuntimeClassicUiMetrics.Ui(PauseButtonSpacing);
            float totalHeight = k_ButtonDefinitions.Length * buttonHeight
                                + (k_ButtonDefinitions.Length - 1) * buttonSpacing;

            // Centered button column. Anchors to screen center; pivot top-center so we
            // can stack downward from the top of the column.
            var buttonRoot = RuntimeUiFactory.CreateAnchoredRect(
                "PauseButtons",
                _pauseRoot,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, totalHeight * 0.5f),
                new Vector2(buttonWidth, totalHeight));
            buttonRoot.pivot = new Vector2(0.5f, 1f);

            float y = 0f;
            for (int i = 0; i < k_ButtonDefinitions.Length; i++)
            {
                var definition = k_ButtonDefinitions[i];
                var buttonRect = RuntimeUiFactory.CreateAnchoredRect(
                    $"PauseButton_{definition.Action}",
                    buttonRoot,
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, -y),
                    new Vector2(0f, buttonHeight));

                // Invisible backdrop gives the button a full-rect hit area even though
                // nothing is drawn — raycastTarget=true lets pointer clicks through
                // transparent pixels (vs. relying on just the text glyphs).
                var hitArea = RuntimeUiFactory.CreateImage("HitArea", buttonRect, new Color(0f, 0f, 0f, 0f));
                hitArea.raycastTarget = true;
                RuntimeUiFactory.Stretch(hitArea.rectTransform);

                // Floating centered label — vanilla MW's pause/main menu entries are
                // just text with no border or background. Color transitions drive
                // hover/select feedback via Selectable.Transition.ColorTint targeting
                // the label directly.
                var label = RuntimeUiFactory.CreateBitmapText(
                    "Label",
                    buttonRect,
                    _theme.DefaultFont,
                    1f,
                    Color.white,
                    BitmapTextAlignment.Center);
                label.PixelHeight = RuntimeClassicUiMetrics.Ui(ModalBodyPixelHeight + 1f);
                label.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
                label.Text = definition.Label;
                label.raycastTarget = false;
                RuntimeUiFactory.Stretch(label.rectTransform);

                var button = buttonRect.gameObject.AddComponent<Button>();
                button.targetGraphic = label;
                button.transition = Selectable.Transition.ColorTint;
                button.colors = CreatePauseTextColors(definition.Available);
                button.interactable = definition.Available;
                button.navigation = new Navigation { mode = Navigation.Mode.Explicit };
                RuntimeShellMenuActionId action = definition.Action;
                button.onClick.AddListener(() => RequestAction(action));

                _buttons.Add(new PauseMenuButtonView
                {
                    Action = definition.Action,
                    Available = definition.Available,
                    Button = button,
                    Background = hitArea,
                    Label = label,
                });

                y += buttonHeight + buttonSpacing;
            }

            ConfigurePauseNavigation();
        }

        // Vanilla message / confirmation modal uses the MW_Dialog skin — a thin MW_Box
        // border, not the thick outer+inner border reserved for full windows. Title
        // sits at the top of the panel, body wraps in the center, a click-anywhere
        // backdrop dismisses it (no explicit footer hint, matching vanilla).
        void BuildModal()
        {
            _modalRoot = RuntimeUiFactory.CreateStretchRect("ModalRoot", _pauseRoot);
            _modalRoot.gameObject.SetActive(false);

            var blocker = RuntimeUiFactory.CreateImage("ModalBackdrop", _modalRoot, new Color(0f, 0f, 0f, 0.72f));
            blocker.raycastTarget = true;
            RuntimeUiFactory.Stretch(blocker.rectTransform);
            var blockerButton = blocker.gameObject.AddComponent<Button>();
            blockerButton.transition = Selectable.Transition.None;
            blockerButton.targetGraphic = blocker;
            blockerButton.navigation = new Navigation { mode = Navigation.Mode.None };
            blockerButton.onClick.AddListener(RequestModalDismiss);

            var rect = RuntimeUiFactory.CreateAnchoredRect(
                "ModalDialog",
                _modalRoot,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(ModalWidth, ModalHeight)));
            rect.pivot = new Vector2(0.5f, 0.5f);

            // Thin MW_Box border (MW_Dialog equivalent) rather than the thick window frame.
            var frame = RuntimeUiFactory.CreateBorderFrame(
                "ModalFrame",
                rect,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                new Color(0f, 0f, 0f, 0.94f));
            RuntimeUiFactory.Stretch(frame.Root);

            _modalTitle = RuntimeUiFactory.CreateBitmapText(
                "ModalTitle",
                frame.Client,
                _theme.DefaultFont,
                1f,
                new Color(0.94f, 0.82f, 0.53f),
                BitmapTextAlignment.Center);
            _modalTitle.PixelHeight = RuntimeClassicUiMetrics.Ui(ModalTitlePixelHeight);
            _modalTitle.rectTransform.anchorMin = new Vector2(0f, 1f);
            _modalTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
            _modalTitle.rectTransform.pivot = new Vector2(0.5f, 1f);
            _modalTitle.rectTransform.anchoredPosition = new Vector2(0f, RuntimeClassicUiMetrics.Ui(-10f));
            _modalTitle.rectTransform.sizeDelta = new Vector2(RuntimeClassicUiMetrics.Ui(-24f), RuntimeClassicUiMetrics.Ui(20f));
            _modalTitle.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            _modalTitle.raycastTarget = false;

            _modalBody = RuntimeUiFactory.CreateBitmapText(
                "ModalBody",
                frame.Client,
                _theme.DefaultFont,
                1f,
                new Color(0.93f, 0.88f, 0.75f),
                BitmapTextAlignment.Center);
            _modalBody.PixelHeight = RuntimeClassicUiMetrics.Ui(ModalBodyPixelHeight);
            _modalBody.WrapMode = BitmapTextWrapMode.Word;
            _modalBody.rectTransform.anchorMin = new Vector2(0f, 0f);
            _modalBody.rectTransform.anchorMax = new Vector2(1f, 1f);
            _modalBody.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _modalBody.rectTransform.offsetMin = new Vector2(RuntimeClassicUiMetrics.Ui(20f), RuntimeClassicUiMetrics.Ui(16f));
            _modalBody.rectTransform.offsetMax = new Vector2(-RuntimeClassicUiMetrics.Ui(20f), -RuntimeClassicUiMetrics.Ui(40f));
            _modalBody.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            _modalBody.raycastTarget = false;
        }

        void ConfigurePauseNavigation()
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                var button = _buttons[i].Button;
                if (button == null)
                    continue;

                button.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = _buttons[(i - 1 + _buttons.Count) % _buttons.Count].Button,
                    selectOnDown = _buttons[(i + 1) % _buttons.Count].Button,
                };
            }
        }

        void RestorePauseSelection(RuntimeShellMenuActionId selectedAction)
        {
            if (_eventSystem == null || !_pauseRoot.gameObject.activeSelf || _modalRoot.gameObject.activeSelf)
                return;

            RuntimeShellMenuActionId targetAction = selectedAction == RuntimeShellMenuActionId.None
                ? RuntimeShellMenuActionId.Resume
                : selectedAction;

            for (int i = 0; i < _buttons.Count; i++)
            {
                if (_buttons[i].Action != targetAction || _buttons[i].Button == null)
                    continue;

                _eventSystem.SetSelectedGameObject(_buttons[i].Button.gameObject);
                return;
            }

            if (_buttons.Count > 0 && _buttons[0].Button != null)
                _eventSystem.SetSelectedGameObject(_buttons[0].Button.gameObject);
        }

        void ClearSelectionIfOwned()
        {
            if (_eventSystem == null)
                return;

            var selected = _eventSystem.currentSelectedGameObject;
            if (selected == null)
                return;

            if ((_pauseRoot != null && selected.transform.IsChildOf(_pauseRoot))
                || (_suiteRoot != null && selected.transform.IsChildOf(_suiteRoot)))
            {
                _eventSystem.SetSelectedGameObject(null);
            }
        }

        void RequestInventoryWindowRectUpdate()
        {
            Rect rect = RuntimeWindowSurfaceUtility.CaptureNormalizedRect(_inventoryView.Root, _suiteRoot);
            if (!RuntimeShellRequestBridge.TrySetInventoryWindowRect(rect, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed updating inventory window rect: {error}");
        }

        void RequestContainerWindowRectUpdate()
        {
            Rect rect = RuntimeWindowSurfaceUtility.CaptureNormalizedRect(_containerView.Root, _suiteRoot);
            if (!RuntimeShellRequestBridge.TrySetContainerWindowRect(rect, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed updating container window rect: {error}");
        }

        void RequestStatsWindowRectUpdate()
        {
            Rect rect = RuntimeWindowSurfaceUtility.CaptureNormalizedRect(_statsView.Root, _suiteRoot);
            if (!RuntimeShellRequestBridge.TrySetStatsWindowRect(rect, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed updating stats window rect: {error}");
        }

        void RequestSpellWindowRectUpdate()
        {
            Rect rect = RuntimeWindowSurfaceUtility.CaptureNormalizedRect(_spellView.Root, _suiteRoot);
            if (!RuntimeShellRequestBridge.TrySetSpellWindowRect(rect, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed updating spell window rect: {error}");
        }

        void RequestSpellSelection(int spellIndex)
        {
            if (!RuntimeShellRequestBridge.TrySelectSpell(spellIndex, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed selecting spell {spellIndex}: {error}");
        }

        void RequestMapWindowRectUpdate()
        {
            Rect rect = RuntimeWindowSurfaceUtility.CaptureNormalizedRect(_mapView.Root, _suiteRoot);
            if (!RuntimeShellRequestBridge.TrySetMapWindowRect(rect, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed updating map window rect: {error}");
        }

        void RequestInventoryFilterText(string value)
        {
            if (!RuntimeShellRequestBridge.TrySetInventoryFilterText(value, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed setting inventory filter text: {error}");
        }

        void RequestInventoryCategory(InventoryWindowCategory category)
        {
            if (!RuntimeShellRequestBridge.TrySetInventoryCategory(category, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed setting inventory category '{category}': {error}");
        }

        void RequestInventorySelection(int inventoryIndex)
        {
            if (!RuntimeShellRequestBridge.TrySelectInventoryItem(inventoryIndex, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed selecting inventory item {inventoryIndex}: {error}");
        }

        void RequestContainerSelection(int itemIndex)
        {
            if (!RuntimeShellRequestBridge.TrySelectContainerItem(itemIndex, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed selecting container item {itemIndex}: {error}");
        }

        void RequestTakeSelected()
        {
            if (!RuntimeShellRequestBridge.TryTakeSelectedContainerItem(out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed taking selected container item: {error}");
        }

        void RequestTakeAll()
        {
            if (!RuntimeShellRequestBridge.TryTakeAllContainerItems(out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed taking all container items: {error}");
        }

        void RequestCloseContainer()
        {
            if (!RuntimeShellRequestBridge.TryCloseContainer(out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed closing container window: {error}");
        }

        void RequestAction(RuntimeShellMenuActionId action)
        {
            if (!RuntimeShellRequestBridge.TryRequestAction(action, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed requesting runtime shell action '{action}': {error}");
        }

        void RequestTogglePin(RuntimeShellPinnableWindow window)
        {
            if (!RuntimeShellRequestBridge.TryTogglePinnedWindow(window, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed toggling pin for {window}: {error}");
        }

        void RequestModalDismiss()
        {
            if (!RuntimeShellRequestBridge.TryDismissModal(out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed dismissing runtime shell modal: {error}");
        }

        void RequestSaveLoadSelectSlot(string slotId)
        {
            if (!RuntimeShellRequestBridge.TrySaveLoadSelectSlot(slotId, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed selecting save slot: {error}");
        }

        void RequestSaveLoadName(string value)
        {
            if (!RuntimeShellRequestBridge.TrySaveLoadSetName(value, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed setting save name: {error}");
        }

        void RequestSaveLoadNew()
        {
            if (!RuntimeShellRequestBridge.TrySaveLoadAction(SaveLoadBrowserPendingAction.NewSave, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed requesting new save: {error}");
        }

        void RequestSaveLoadOverwrite()
        {
            if (!RuntimeShellRequestBridge.TrySaveLoadAction(SaveLoadBrowserPendingAction.Overwrite, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed requesting overwrite: {error}");
        }

        void RequestSaveLoadLoad()
        {
            if (!RuntimeShellRequestBridge.TrySaveLoadAction(SaveLoadBrowserPendingAction.Load, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed requesting load: {error}");
        }

        void RequestSaveLoadDelete()
        {
            if (!RuntimeShellRequestBridge.TrySaveLoadAction(SaveLoadBrowserPendingAction.Delete, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed requesting delete: {error}");
        }

        void RequestSaveLoadCancel()
        {
            if (!RuntimeShellRequestBridge.TrySaveLoadAction(SaveLoadBrowserPendingAction.Cancel, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed closing save browser: {error}");
        }

        void RequestSaveLoadConfirm()
        {
            if (!RuntimeShellRequestBridge.TrySaveLoadAction(SaveLoadBrowserPendingAction.Confirm, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed confirming save browser action: {error}");
        }

        void RequestSaveLoadCancelConfirm()
        {
            if (!RuntimeShellRequestBridge.TrySaveLoadAction(SaveLoadBrowserPendingAction.CancelConfirm, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed cancelling save browser confirmation: {error}");
        }

        static ColorBlock CreateButtonColors(bool available)
        {
            var block = ColorBlock.defaultColorBlock;
            block.colorMultiplier = 1f;
            block.fadeDuration = 0.08f;

            if (available)
            {
                block.normalColor = new Color(0.2f, 0.16f, 0.11f, 0.9f);
                block.highlightedColor = new Color(0.34f, 0.28f, 0.18f, 0.96f);
                block.pressedColor = new Color(0.42f, 0.34f, 0.2f, 1f);
                block.selectedColor = new Color(0.34f, 0.28f, 0.18f, 0.96f);
                block.disabledColor = block.normalColor;
            }
            else
            {
                block.normalColor = new Color(0.14f, 0.12f, 0.1f, 0.76f);
                block.highlightedColor = new Color(0.24f, 0.21f, 0.18f, 0.9f);
                block.pressedColor = new Color(0.29f, 0.25f, 0.2f, 0.94f);
                block.selectedColor = block.highlightedColor;
                block.disabledColor = block.normalColor;
            }

            return block;
        }

        // ColorBlock for pause-menu labels. Targets the BitmapText directly (via
        // Selectable.targetGraphic = label, transition = ColorTint), so these colors
        // replace the label's drawn color in each state — off-cream at rest, bright
        // gold on hover/select, more saturated on press. Vanilla MW's pause/main menu
        // does the same text-tint highlight with no background.
        static ColorBlock CreatePauseTextColors(bool available)
        {
            var block = ColorBlock.defaultColorBlock;
            block.colorMultiplier = 1f;
            block.fadeDuration = 0.08f;

            if (available)
            {
                block.normalColor = new Color(0.82f, 0.76f, 0.62f, 1f);
                block.highlightedColor = new Color(1f, 0.92f, 0.68f, 1f);
                block.pressedColor = new Color(1f, 0.84f, 0.48f, 1f);
                block.selectedColor = block.highlightedColor;
                block.disabledColor = new Color(0.56f, 0.52f, 0.46f, 1f);
            }
            else
            {
                Color dim = new(0.56f, 0.52f, 0.46f, 1f);
                block.normalColor = dim;
                block.highlightedColor = dim;
                block.pressedColor = dim;
                block.selectedColor = dim;
                block.disabledColor = dim;
            }

            return block;
        }

        OptionsWindowView.Callbacks BuildOptionsCallbacks()
        {
            // Options-window callbacks route live-applied values to the matching
            // runtime knob. The OptionsWindowView has already written the new value
            // into the shared MorrowindConfig by the time we're called; the hooks
            // below just apply it so the change is visible before Close saves to
            // disk. Callbacks for settings with no live runtime (Footsteps/Voice
            // volumes, Difficulty) are intentionally left null — the values still
            // persist.
            return new OptionsWindowView.Callbacks
            {
                UiScale = v => RuntimeUiScaleSettings.GlobalScale = v,
                HudScale = v => RuntimeUiScaleSettings.HudScale = v,
                MasterVolume = v => RuntimeAudioService.Active?.SetMasterVolume(v),
                MusicVolume = v => RuntimeAudioService.Active?.SetMusicVolume(v),
                EffectsVolume = v => RuntimeAudioService.Active?.SetEffectsVolume(v),
                FootstepsVolume = null,
                VoiceVolume = null,
                ShowCrosshair = v =>
                {
                    // HUD crosshair visibility is gated by a static preference that
                    // BuildHudModel reads — the next presentation tick picks it up
                    // automatically, so no need to poke the view directly.
                    HudUserPreferences.ShowCrosshair = v;
                },
                ShowSubtitles = v => HudUserPreferences.ShowSubtitles = v,
                MenuTransparency = null,
                Difficulty = null,
                Fov = v =>
                {
                    if (Camera.main != null)
                        Camera.main.fieldOfView = v;
                },
                Gamma = v => Screen.brightness = v,
                Resolution = (w, h, refresh) =>
                {
                    if (w <= 0 || h <= 0)
                        return;
                    Screen.SetResolution(w, h, Screen.fullScreenMode, refresh > 0 ? refresh : Screen.currentResolution.refreshRate);
                },
                WindowMode = mode => Screen.fullScreenMode = mode switch
                {
                    1 => FullScreenMode.ExclusiveFullScreen,
                    2 => FullScreenMode.FullScreenWindow,
                    _ => FullScreenMode.Windowed,
                },
                VSync = v => QualitySettings.vSyncCount = Mathf.Clamp(v, 0, 2),
                Close = () =>
                {
                    if (!RuntimeShellRequestBridge.TryCloseOptions(out string error))
                        Debug.LogWarning($"[VVardenfell][UI] failed closing Options: {error}");
                },
                ResetToDefaults = () =>
                {
                    if (_config == null)
                        return;
                    _config.ResetPlayerSettingsToDefaults();
                    ApplyAllConfigValuesLive();
                    _optionsView.SyncFromConfig();
                },
            };
        }

        void ApplyAllConfigValuesLive()
        {
            if (_config == null)
                return;

            RuntimeUiScaleSettings.GlobalScale = _config.UiScale;
            RuntimeUiScaleSettings.HudScale = _config.HudScale;
            RuntimeAudioService.Active?.SetMasterVolume(_config.MasterVolume);
            RuntimeAudioService.Active?.SetMusicVolume(_config.MusicVolume);
            RuntimeAudioService.Active?.SetEffectsVolume(_config.EffectsVolume);
            HudUserPreferences.ShowCrosshair = _config.ShowCrosshair;
            HudUserPreferences.ShowSubtitles = _config.ShowSubtitles;

            if (Camera.main != null)
                Camera.main.fieldOfView = _config.Fov;
            Screen.brightness = _config.Gamma;
            QualitySettings.vSyncCount = Mathf.Clamp(_config.VSync, 0, 2);
            Screen.fullScreenMode = _config.WindowMode switch
            {
                1 => FullScreenMode.ExclusiveFullScreen,
                2 => FullScreenMode.FullScreenWindow,
                _ => FullScreenMode.Windowed,
            };
            if (_config.ResolutionWidth > 0 && _config.ResolutionHeight > 0)
            {
                Screen.SetResolution(
                    _config.ResolutionWidth,
                    _config.ResolutionHeight,
                    Screen.fullScreenMode,
                    _config.RefreshRate > 0 ? _config.RefreshRate : Screen.currentResolution.refreshRate);
            }
        }

        void SaveConfig()
        {
            if (_config == null)
                return;
            if (!VVardenfell.Core.Config.ConfigStorage.TrySave(_config, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed saving config: {error}");
        }

        void OnDestroy()
        {
            _iconService?.Dispose();
            _iconService = null;
            _theme?.Dispose();
            _theme = null;
        }
    }
}
