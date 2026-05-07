using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;
using VVardenfell.Runtime.Audio;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Streaming;
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
        const float ModalTitlePixelHeight = RuntimeClassicUiFontSizes.Caption;
        const float ModalBodyPixelHeight = RuntimeClassicUiFontSizes.Body;
        const float CountDialogWidth = 330f;
        const float CountDialogHeight = 150f;
        const float DragIconSize = 42f;

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
        JournalWindowView _journalView;
        DialogueWindowView _dialogueView;
        SaveLoadBrowserView _saveLoadBrowserView;
        OptionsWindowView _optionsView;
        RestMenuWindowView _restMenuView;
        CharacterGenerationWindowView _characterGenerationView;
        BookReaderWindowView _bookReaderView;
        RuntimeUiPopupLayer _popupLayer;
        VVardenfell.Core.Config.MorrowindConfig _config;

        RectTransform _pauseRoot;
        RectTransform _modalRoot;
        RectTransform _modalDialogRect;
        RectTransform _modalButtonRoot;
        BitmapTextGraphic _modalTitle;
        BitmapTextGraphic _modalBody;
        RectTransform _countRoot;
        BitmapTextGraphic _countTitle;
        BitmapTextGraphic _countValueText;
        InputField _countInput;
        MorrowindButtonView _countOkButton;
        MorrowindButtonView _countCancelButton;
        RectTransform _dragLayerRoot;
        RectTransform _dragIconRoot;
        Image _dragIcon;
        Image _dragIconShadow;
        Image _screenFade;
        Image _hitOverlay;
        RectTransform _movieRoot;
        RawImage _movieImage;
        VideoPlayer _moviePlayer;
        RenderTexture _movieTexture;
        string _activeMovieName;
        string _activeMoviePath;
        bool _activeMovieAllowSkipping;
        bool _movieCloseRequested;
        BitmapTextGraphic _dragCount;
        BitmapTextGraphic _dragCountShadow;
        bool _inventoryVisible;
        bool _containerVisible;
        bool _pauseMenuOpen;
        bool _modalOpen;
        bool _optionsOpen;
        bool _journalVisible;
        bool _dialogueVisible;
        InventoryWindowViewModel _inventoryModel;
        ContainerWindowViewModel _containerModel;
        InventoryItemActionKind _pendingCountAction;
        InventoryItemOwnerKind _pendingCountOwner;
        int _pendingCountIndex = -1;
        int _pendingCountMax;
        int _pendingCountValue;
        Vector2 _pendingCountScreenPosition;
        bool _pendingCountHasScreenPosition;
        InventoryWindowEntryViewModel _pendingCountEntry;
        bool _suppressCountInput;
        bool _dragActive;
        InventoryWindowEntryViewModel _dragEntry;

        readonly List<PauseMenuButtonView> _buttons = new();
        readonly List<MorrowindButtonView> _modalButtons = new();
        readonly List<RaycastResult> _focusRaycastResults = new();

        public static RuntimeHudShellView Create()
        {
            var go = new GameObject("VVardenfell.RuntimeHudShell");
            DontDestroyOnLoad(go);
            var view = go.AddComponent<RuntimeHudShellView>();
            view.Initialize();
            return view;
        }

        void Update()
        {
            HandleMovieInput();
            FocusWindowUnderPointer();
            SyncDragIconPosition();
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

            // Dedicated HUD canvas at scene root. It must NOT be nested under
            // the shell GameObject because Unity treats a Canvas inside
            // another Canvas as a sub-canvas and silently ignores its own
            // CanvasScaler — which would make the HUD Scale slider a no-op.
            // CreateSiblingCanvasRoot wires a RuntimeCanvasLifetimeLink back
            // to the shell GameObject so the HUD canvas still shares the
            // shell's activation and destruction.
            var hudCanvas = RuntimeUiFactory.CreateSiblingCanvasRoot(
                gameObject,
                "VVardenfell.HudCanvas",
                "HudRoot",
                short.MaxValue - 96,
                RuntimeUiScaleBinding.ScaleKind.Hud);
            _hudView = new RuntimeHudView(hudCanvas.Root, _theme, _iconService);
            _suiteRoot = RuntimeUiFactory.CreateStretchRect("SuiteRoot", _rootRect);
            _inventoryView = new InventoryWindowView(
                _suiteRoot,
                _suiteRoot,
                _theme,
                _iconService,
                RequestInventoryWindowRectUpdate,
                RequestInventoryCategory,
                RequestInventorySelection,
                RequestInventoryItemDragged,
                RequestInventoryItemRightClicked,
                IsInventoryItemSelected,
                RequestDropHeldToInventory,
                RequestUseHeldOnAvatar,
                HasManagedDrag,
                SetDragIconScreenPosition,
                RequestInventoryFilterText,
                onPinToggled: () => RequestTogglePin(VVardenfell.Runtime.Components.RuntimeShellPinnableWindow.Inventory));
            _containerView = new ContainerWindowView(
                _suiteRoot,
                _suiteRoot,
                _theme,
                _iconService,
                RequestContainerWindowRectUpdate,
                RequestContainerSelection,
                RequestContainerItemDragged,
                IsContainerItemSelected,
                RequestDropHeldToContainer,
                HasManagedDrag,
                SetDragIconScreenPosition,
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
                _iconService,
                RequestSpellWindowRectUpdate,
                RequestSpellSelection,
                RequestSpellFilterText,
                onPinToggled: () => RequestTogglePin(VVardenfell.Runtime.Components.RuntimeShellPinnableWindow.Spell));
            _mapView = new MapWindowView(
                _suiteRoot,
                _suiteRoot,
                _theme,
                RequestMapWindowRectUpdate,
                onPinToggled: () => RequestTogglePin(VVardenfell.Runtime.Components.RuntimeShellPinnableWindow.Map));
            _journalView = new JournalWindowView(
                _rootRect,
                _theme,
                RequestJournalQuestSelection,
                RequestJournalShowAll,
                RequestJournalPage,
                RequestJournalOverlay,
                RequestJournalMainBook,
                RequestJournalClose);
            _dialogueView = new DialogueWindowView(
                _rootRect,
                _theme,
                RequestDialogueTopicSelection,
                RequestDialogueChoiceSelection,
                RequestDialogueGoodbye);
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
            _restMenuView = new RestMenuWindowView(_rootRect, _theme);
            _characterGenerationView = new CharacterGenerationWindowView(_rootRect, _theme, _iconService);
            _bookReaderView = new BookReaderWindowView(_rootRect, _theme, _iconService);

            BuildPauseMenu();
            BuildModal();
            BuildCountDialog();
            BuildDragIcon();
            _popupLayer = new RuntimeUiPopupLayer(_rootRect, _theme, _iconService);
            BuildScreenFade();
            BuildHitOverlay();
            BuildMoviePlayer();
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
            JournalWindowViewModel journalModel,
            DialogueWindowViewModel dialogueModel,
            RestMenuViewModel restMenuModel,
            CharacterGenerationViewModel characterGenerationModel,
            BookReaderViewModel bookReaderModel,
            MoviePlaybackViewModel movieModel,
            RuntimeShellMenuActionId selectedAction,
            bool pauseMenuOpen,
            bool modalOpen,
            bool optionsOpen,
            float screenFadeAlpha,
            float hitOverlayAlpha,
            string modalTitle,
            string modalBody,
            string[] modalButtons)
        {
            if (_rootRect == null)
                return;

            SetActiveIfChanged(gameObject, visible);
            if (!visible)
            {
                bool optionsWasOpen = _optionsOpen;
                ClearSelectionIfOwned();
                _inventoryVisible = false;
                _containerVisible = false;
                _pauseMenuOpen = false;
                _modalOpen = false;
                _optionsOpen = false;
                _journalVisible = false;
                _dialogueVisible = false;
                SyncMovie(null);
                if (optionsWasOpen)
                    _optionsView?.SetVisible(false);
                _journalView?.SetVisible(false);
                _dialogueView?.SetVisible(false);
                _restMenuView?.Sync(null);
                _characterGenerationView?.Sync(null);
                _bookReaderView?.Sync(null);
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
            bool journalVisible = journalModel != null;
            bool dialogueVisible = dialogueModel != null;
            bool restMenuVisible = restMenuModel != null;
            bool characterGenerationVisible = characterGenerationModel != null;
            bool bookReaderVisible = bookReaderModel != null;
            bool inventoryOpened = !_inventoryVisible && inventoryVisible;
            bool containerOpened = !_containerVisible && containerVisible;
            bool pauseOpened = !_pauseMenuOpen && pauseMenuOpen;
            bool modalClosed = _modalOpen && !modalOpen;
            bool journalOpened = !_journalVisible && journalVisible;
            bool dialogueOpened = !_dialogueVisible && dialogueVisible;

            _inventoryVisible = inventoryVisible;
            _containerVisible = containerVisible;
            _inventoryModel = inventoryModel;
            _containerModel = containerModel;
            _pauseMenuOpen = pauseMenuOpen;
            _modalOpen = modalOpen;
            _journalVisible = journalVisible;
            _dialogueVisible = dialogueVisible;

            _hudView.Sync(hudModel);
            SetActiveIfChanged(
                _suiteRoot.gameObject,
                inventoryVisible || containerVisible || statsVisible || spellVisible || mapVisible);

            _inventoryView.Sync(inventoryModel);
            _containerView.Sync(containerModel);
            _statsView.Sync(statsModel);
            _spellView.Sync(spellModel);
            _mapView.Sync(mapModel);
            _journalView.Sync(journalModel);
            _dialogueView.Sync(dialogueModel);
            _restMenuView.Sync(restMenuModel);
            _characterGenerationView.Sync(characterGenerationModel);
            _bookReaderView.Sync(bookReaderModel);
            SyncMovie(movieModel);

            // Options sits on top of the pause menu while open; while it's visible
            // the pause backdrop + any save/load overlay stay hidden so the user
            // sees a single focused dialog.
            bool optionsTransitioning = _optionsOpen != optionsOpen;
            _optionsOpen = optionsOpen;
            if (_optionsView != null)
            {
                if (optionsTransitioning)
                    _optionsView.SetVisible(optionsOpen);
                if (optionsTransitioning && !optionsOpen)
                    SaveConfig();
            }

            SetActiveIfChanged(_pauseRoot.gameObject, pauseMenuOpen && !saveLoadVisible && !optionsOpen && !restMenuVisible && !characterGenerationVisible && !bookReaderVisible);
            SetActiveIfChanged(_modalRoot.gameObject, pauseMenuOpen && modalOpen && !optionsOpen && !restMenuVisible && !characterGenerationVisible && !bookReaderVisible);
            _saveLoadBrowserView.Sync(optionsOpen ? null : saveLoadModel);
            _popupLayer?.Sync();
            if (!inventoryVisible && !containerVisible)
                ClearManagedDrag();
            SyncDragIconPosition();

            if (_modalRoot.gameObject.activeSelf)
            {
                _modalTitle.Text = string.IsNullOrWhiteSpace(modalTitle) ? "Unavailable" : modalTitle.Trim();
                _modalBody.Text = string.IsNullOrWhiteSpace(modalBody)
                    ? "This action is not available in the current runtime shell slice."
                    : modalBody.Trim();
                SyncModalButtons(modalButtons);
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

            if (journalOpened || dialogueOpened)
                ClearSelectionIfOwned();

            if (!pauseMenuOpen && !inventoryVisible && !containerVisible && !saveLoadVisible && !journalVisible && !dialogueVisible)
                ClearSelectionIfOwned();

            SyncScreenFade(screenFadeAlpha);
            SyncHitOverlay(hitOverlayAlpha);
            SyncDragIconPosition();
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

            _modalDialogRect = RuntimeUiFactory.CreateAnchoredRect(
                "ModalDialog",
                _modalRoot,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(ModalWidth, ModalHeight)));
            _modalDialogRect.pivot = new Vector2(0.5f, 0.5f);

            // Thin MW_Box border (MW_Dialog equivalent) rather than the thick window frame.
            var frame = RuntimeUiFactory.CreateBorderFrame(
                "ModalFrame",
                _modalDialogRect,
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

            _modalButtonRoot = RuntimeUiFactory.CreateAnchoredRect(
                "ModalButtons",
                frame.Client,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                RuntimeClassicUiMetrics.Ui(new Vector2(0f, 18f)),
                RuntimeClassicUiMetrics.Ui(new Vector2(-32f, 78f)));
            _modalButtonRoot.pivot = new Vector2(0.5f, 0f);
            for (int i = 0; i < 10; i++)
            {
                var button = BuildModalButton(_modalButtonRoot, $"ModalButton{i}", i);
                button.Root.gameObject.SetActive(false);
                _modalButtons.Add(button);
            }
        }

        MorrowindButtonView BuildModalButton(RectTransform parent, string name, int buttonIndex)
        {
            var rect = RuntimeUiFactory.CreateAnchoredRect(
                $"{name}Rect",
                parent,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(120f, 24f)));
            rect.pivot = new Vector2(0f, 1f);
            var button = RuntimeUiFactory.CreateMorrowindButton(
                name,
                rect,
                _theme,
                string.Empty,
                RuntimeClassicUiMetrics.WindowText(0.46f),
                new Color(0.94f, 0.85f, 0.68f),
                new Color(0.12f, 0.1f, 0.08f, 0.9f));
            RuntimeUiFactory.Stretch(button.Root);
            button.Button.onClick.AddListener(() => RequestModalButton(buttonIndex));
            button.Root = rect;
            return button;
        }

        void SyncModalButtons(string[] labels)
        {
            int count = Math.Min(labels?.Length ?? 0, _modalButtons.Count);
            int columns = count <= 1 ? 1 : 2;
            int rows = count <= 0 ? 0 : (count + columns - 1) / columns;
            float width = count > 1 ? 520f : ModalWidth;
            float height = ModalHeight + Math.Max(0, rows - 1) * 34f;
            _modalDialogRect.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(width, height));
            _modalBody.rectTransform.offsetMin = new Vector2(
                RuntimeClassicUiMetrics.Ui(20f),
                RuntimeClassicUiMetrics.Ui(count > 0 ? 44f + rows * 30f : 16f));

            float gap = RuntimeClassicUiMetrics.Ui(8f);
            float buttonHeight = RuntimeClassicUiMetrics.Ui(24f);
            float rootWidth = RuntimeClassicUiMetrics.Ui(width - 48f);
            float buttonWidth = columns == 1 ? Math.Min(RuntimeClassicUiMetrics.Ui(180f), rootWidth) : (rootWidth - gap) * 0.5f;
            float totalWidth = columns == 1 ? buttonWidth : rootWidth;
            _modalButtonRoot.sizeDelta = new Vector2(RuntimeClassicUiMetrics.Ui(-32f), RuntimeClassicUiMetrics.Ui(Math.Max(0, rows * 30f)));

            for (int i = 0; i < _modalButtons.Count; i++)
            {
                var button = _modalButtons[i];
                bool active = i < count;
                SetActiveIfChanged(button.Root.gameObject, active);
                if (!active)
                    continue;

                int row = i / columns;
                int column = i % columns;
                button.Label.Text = string.IsNullOrWhiteSpace(labels[i]) ? "OK" : labels[i].Trim();
                button.Root.anchorMin = new Vector2(0.5f, 1f);
                button.Root.anchorMax = new Vector2(0.5f, 1f);
                button.Root.pivot = new Vector2(0f, 1f);
                button.Root.sizeDelta = new Vector2(buttonWidth, buttonHeight);
                button.Root.anchoredPosition = new Vector2(
                    -totalWidth * 0.5f + column * (buttonWidth + gap),
                    -row * RuntimeClassicUiMetrics.Ui(30f));
            }
        }

        void BuildCountDialog()
        {
            _countRoot = RuntimeUiFactory.CreateAnchoredRect(
                "InventoryCountDialog",
                _rootRect,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(CountDialogWidth, CountDialogHeight)));
            _countRoot.pivot = new Vector2(0.5f, 0.5f);
            _countRoot.gameObject.SetActive(false);

            var frame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                _countRoot,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                new Color(0f, 0f, 0f, 0.88f));
            RuntimeUiFactory.Stretch(frame.Root);

            _countTitle = RuntimeUiFactory.CreateBitmapText(
                "Title",
                frame.Client,
                _theme.DefaultFont,
                1f,
                new Color(0.94f, 0.82f, 0.53f),
                BitmapTextAlignment.Center);
            _countTitle.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Caption);
            _countTitle.rectTransform.anchorMin = new Vector2(0f, 1f);
            _countTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
            _countTitle.rectTransform.offsetMin = RuntimeClassicUiMetrics.Ui(new Vector2(12f, -36f));
            _countTitle.rectTransform.offsetMax = RuntimeClassicUiMetrics.Ui(new Vector2(-12f, -10f));

            _countValueText = RuntimeUiFactory.CreateBitmapText(
                "CountValue",
                frame.Client,
                _theme.DefaultFont,
                1f,
                new Color(0.94f, 0.85f, 0.68f),
                BitmapTextAlignment.Center);
            _countValueText.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            _countValueText.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            _countValueText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _countValueText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _countValueText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _countValueText.rectTransform.anchoredPosition = RuntimeClassicUiMetrics.Ui(new Vector2(0f, 12f));
            _countValueText.rectTransform.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(180f, 24f));

            var inputView = RuntimeUiFactory.CreateBitmapInputField(
                "CountInput",
                frame.Client,
                _theme,
                1f,
                new Color(0.94f, 0.85f, 0.68f),
                new Color(0f, 0f, 0f, 0.7f),
                "1",
                8f,
                4f,
                6);
            inputView.Root.anchorMin = new Vector2(0.5f, 0.5f);
            inputView.Root.anchorMax = new Vector2(0.5f, 0.5f);
            inputView.Root.pivot = new Vector2(0.5f, 0.5f);
            inputView.Root.anchoredPosition = RuntimeClassicUiMetrics.Ui(new Vector2(0f, -18f));
            inputView.Root.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(86f, 24f));
            inputView.InputField.contentType = InputField.ContentType.IntegerNumber;
            inputView.InputField.onValueChanged.AddListener(OnCountInputChanged);
            _countInput = inputView.InputField;

            BuildCountButton(frame.Client, "Minus", "-", new Vector2(-72f, -18f), () => SetCountValue(_pendingCountValue - 1));
            BuildCountButton(frame.Client, "Plus", "+", new Vector2(72f, -18f), () => SetCountValue(_pendingCountValue + 1));
            _countOkButton = BuildCountButton(frame.Client, "OK", "OK", new Vector2(-52f, -58f), ConfirmCountDialog, 72f);
            _countCancelButton = BuildCountButton(frame.Client, "Cancel", "Cancel", new Vector2(52f, -58f), CancelCountDialog, 72f);
        }

        MorrowindButtonView BuildCountButton(RectTransform parent, string name, string label, Vector2 position, UnityEngine.Events.UnityAction onClick, float width = 42f)
        {
            var rect = RuntimeUiFactory.CreateAnchoredRect(
                $"{name}Rect",
                parent,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                RuntimeClassicUiMetrics.Ui(position),
                RuntimeClassicUiMetrics.Ui(new Vector2(width, 24f)));
            rect.pivot = new Vector2(0.5f, 0.5f);
            var button = RuntimeUiFactory.CreateMorrowindButton(
                name,
                rect,
                _theme,
                label,
                RuntimeClassicUiMetrics.WindowText(0.46f),
                new Color(0.94f, 0.85f, 0.68f),
                new Color(0.12f, 0.1f, 0.08f, 0.9f));
            RuntimeUiFactory.Stretch(button.Root);
            button.Button.onClick.AddListener(onClick);
            return button;
        }

        void BuildDragIcon()
        {
            var dragCanvas = RuntimeUiFactory.CreateSiblingCanvasRoot(
                gameObject,
                "VVardenfell.DragCanvas",
                "DragRoot",
                short.MaxValue - 1,
                RuntimeUiScaleBinding.ScaleKind.Global);
            _dragLayerRoot = dragCanvas.Root;

            _dragIconRoot = RuntimeUiFactory.CreateAnchoredRect(
                "InventoryDragIcon",
                _dragLayerRoot,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(DragIconSize, DragIconSize)));
            _dragIconRoot.pivot = new Vector2(0f, 1f);
            _dragIconRoot.gameObject.SetActive(false);
            _dragIconRoot.SetAsLastSibling();

            var frame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                _dragIconRoot,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                new Color(0f, 0f, 0f, 0.62f));
            RuntimeUiFactory.Stretch(frame.Root);
            SetFrameRaycastTarget(frame, false);
            _dragIconShadow = RuntimeInventoryItemIconLayoutUtility.CreateItemImage("ItemShadow", frame.Root, new Color(0f, 0f, 0f, 0.5f), shadow: true, flipVertical: true);
            _dragIcon = RuntimeInventoryItemIconLayoutUtility.CreateItemImage("Item", frame.Root, Color.white, shadow: false, flipVertical: true);
            RuntimeInventoryItemIconLayoutUtility.BringBorderToFront(frame);

            _dragCountShadow = CreateDragCountText(frame.Client, "CountShadow", new Color(0f, 0f, 0f, 0.82f), new Vector2(-2f, 1f));
            _dragCount = CreateDragCountText(frame.Client, "Count", new Color(0.94f, 0.85f, 0.68f), new Vector2(-3f, 2f));
        }

        void BuildScreenFade()
        {
            _screenFade = RuntimeUiFactory.CreateImage("ScreenFade", _rootRect, Color.clear);
            RuntimeUiFactory.Stretch(_screenFade.rectTransform);
            _screenFade.raycastTarget = false;
            _screenFade.gameObject.SetActive(false);
            _screenFade.rectTransform.SetAsLastSibling();
        }

        void SyncScreenFade(float alpha)
        {
            if (_screenFade == null)
                return;

            alpha = Mathf.Clamp01(alpha);
            bool active = alpha > 0.0001f;
            SetActiveIfChanged(_screenFade.gameObject, active);
            if (!active)
                return;

            _screenFade.color = new Color(0f, 0f, 0f, alpha);
            _screenFade.rectTransform.SetAsLastSibling();
        }

        void BuildHitOverlay()
        {
            _hitOverlay = RuntimeUiFactory.CreateImage("HitOverlay", _rootRect, Color.clear);
            RuntimeUiFactory.Stretch(_hitOverlay.rectTransform);
            _hitOverlay.raycastTarget = false;
            _hitOverlay.gameObject.SetActive(false);
            _hitOverlay.rectTransform.SetAsLastSibling();
        }

        void SyncHitOverlay(float alpha)
        {
            if (_hitOverlay == null)
                return;

            alpha = Mathf.Clamp01(alpha);
            bool active = alpha > 0.0001f;
            SetActiveIfChanged(_hitOverlay.gameObject, active);
            if (!active)
                return;

            _hitOverlay.color = new Color(0f, 0f, 0f, alpha);
            _hitOverlay.rectTransform.SetAsLastSibling();
        }

        void BuildMoviePlayer()
        {
            _movieRoot = RuntimeUiFactory.CreateStretchRect("MovieRoot", _rootRect);
            _movieRoot.gameObject.SetActive(false);

            var blocker = RuntimeUiFactory.CreateImage("MovieBackdrop", _movieRoot, Color.black);
            blocker.raycastTarget = true;
            RuntimeUiFactory.Stretch(blocker.rectTransform);

            _movieImage = RuntimeUiFactory.CreateRawImage("MovieImage", _movieRoot, Color.white);
            _movieImage.raycastTarget = false;
            RuntimeUiFactory.Stretch(_movieImage.rectTransform);

            _moviePlayer = gameObject.AddComponent<VideoPlayer>();
            _moviePlayer.playOnAwake = false;
            _moviePlayer.isLooping = false;
            _moviePlayer.renderMode = VideoRenderMode.RenderTexture;
            _moviePlayer.waitForFirstFrame = true;
            _moviePlayer.skipOnDrop = false;
            _moviePlayer.audioOutputMode = VideoAudioOutputMode.Direct;
            _moviePlayer.prepareCompleted += OnMoviePrepared;
            _moviePlayer.errorReceived += OnMovieError;
            _moviePlayer.loopPointReached += OnMovieFinished;
        }

        void SyncMovie(MoviePlaybackViewModel model)
        {
            if (_movieRoot == null)
                return;

            if (model == null || string.IsNullOrWhiteSpace(model.MovieName))
            {
                StopMovie();
                return;
            }

            string movieName = model.MovieName.Trim();
            var movie = _theme.GetMovie(movieName);
            if (movie == null || !movie.HasPlayableClip)
                throw new InvalidOperationException($"[VVardenfell][UI] PlayBink movie '{movieName}' has no playable transcoded clip. Re-bake UI movies.");

            if (_movieRoot.gameObject.activeSelf
                && string.Equals(_activeMovieName, movieName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_activeMoviePath, movie.CachedClipPath, StringComparison.OrdinalIgnoreCase)
                && _activeMovieAllowSkipping == model.AllowSkipping)
            {
                _movieRoot.SetAsLastSibling();
                return;
            }

            EnsureMovieTexture();
            _activeMovieName = movieName;
            _activeMoviePath = movie.CachedClipPath;
            _activeMovieAllowSkipping = model.AllowSkipping;
            _movieCloseRequested = false;
            _movieImage.texture = _movieTexture;
            _moviePlayer.Stop();
            _moviePlayer.source = VideoSource.Url;
            _moviePlayer.url = movie.CachedClipPath;
            _moviePlayer.isLooping = false;
            _moviePlayer.targetTexture = _movieTexture;
            ConfigureMovieAudio(movie.HasAudio);
            SetActiveIfChanged(_movieRoot.gameObject, true);
            _movieRoot.SetAsLastSibling();
            _moviePlayer.Prepare();
        }

        void StopMovie()
        {
            _activeMovieName = null;
            _activeMoviePath = null;
            _activeMovieAllowSkipping = false;
            _movieCloseRequested = false;
            if (_moviePlayer != null)
            {
                _moviePlayer.Stop();
                _moviePlayer.targetTexture = null;
                _moviePlayer.url = string.Empty;
                _moviePlayer.audioOutputMode = VideoAudioOutputMode.None;
            }

            if (_movieImage != null)
                _movieImage.texture = null;
            if (_movieRoot != null)
                SetActiveIfChanged(_movieRoot.gameObject, false);
        }

        void EnsureMovieTexture()
        {
            int width = Mathf.Max(2, Screen.width);
            int height = Mathf.Max(2, Screen.height);
            if (_movieTexture != null && _movieTexture.width == width && _movieTexture.height == height)
                return;

            if (_movieTexture != null)
            {
                _movieTexture.Release();
                Destroy(_movieTexture);
            }

            _movieTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "VV.ScriptMovie",
            };
        }

        void ConfigureMovieAudio(bool hasAudio)
        {
            if (_moviePlayer == null)
                return;

            if (!hasAudio)
            {
                _moviePlayer.audioOutputMode = VideoAudioOutputMode.None;
                return;
            }

            _moviePlayer.audioOutputMode = VideoAudioOutputMode.Direct;
            _moviePlayer.EnableAudioTrack(0, true);
            _moviePlayer.SetDirectAudioMute(0, false);
            _moviePlayer.SetDirectAudioVolume(0, 1f);
        }

        void OnMoviePrepared(VideoPlayer source)
        {
            if (source == null || source != _moviePlayer || !_movieRoot.gameObject.activeSelf)
                return;

            source.Play();
        }

        void OnMovieError(VideoPlayer source, string message)
        {
            throw new InvalidOperationException($"[VVardenfell][UI] PlayBink movie '{_activeMovieName ?? "<none>"}' failed: {message}");
        }

        void OnMovieFinished(VideoPlayer source)
        {
            if (source == null || source != _moviePlayer)
                return;

            RequestCloseMovie();
        }

        void HandleMovieInput()
        {
            if (_movieRoot == null || !_movieRoot.gameObject.activeSelf || !_activeMovieAllowSkipping)
                return;

            bool skip = Keyboard.current?.anyKey.wasPressedThisFrame == true
                        || Mouse.current?.leftButton.wasPressedThisFrame == true
                        || Mouse.current?.rightButton.wasPressedThisFrame == true;
            if (skip)
                RequestCloseMovie();
        }

        void RequestCloseMovie()
        {
            if (_movieCloseRequested)
                return;

            _movieCloseRequested = true;
            if (!RuntimeShellRequestBridge.TryCloseMovie(out string error))
                throw new InvalidOperationException($"[VVardenfell][UI] failed to close PlayBink movie: {error}");
        }

        static void SetFrameRaycastTarget(BorderFrameView frame, bool value)
        {
            if (frame?.Center != null) frame.Center.raycastTarget = value;
            if (frame?.Top != null) frame.Top.raycastTarget = value;
            if (frame?.Bottom != null) frame.Bottom.raycastTarget = value;
            if (frame?.Left != null) frame.Left.raycastTarget = value;
            if (frame?.Right != null) frame.Right.raycastTarget = value;
            if (frame?.TopLeft != null) frame.TopLeft.raycastTarget = value;
            if (frame?.TopRight != null) frame.TopRight.raycastTarget = value;
            if (frame?.BottomLeft != null) frame.BottomLeft.raycastTarget = value;
            if (frame?.BottomRight != null) frame.BottomRight.raycastTarget = value;
        }

        BitmapTextGraphic CreateDragCountText(RectTransform parent, string name, Color color, Vector2 offset)
        {
            var text = RuntimeUiFactory.CreateBitmapText(
                name,
                parent,
                _theme.DefaultFont,
                1f,
                color,
                BitmapTextAlignment.Right);
            text.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Count);
            text.VerticalAlignment = BitmapTextVerticalAlignment.Bottom;
            text.rectTransform.anchorMin = new Vector2(1f, 0f);
            text.rectTransform.anchorMax = new Vector2(1f, 0f);
            text.rectTransform.pivot = new Vector2(1f, 0f);
            RuntimeInventoryItemIconLayoutUtility.ApplyCountRect(text.rectTransform);
            text.rectTransform.anchoredPosition += RuntimeClassicUiMetrics.Ui(offset);
            text.raycastTarget = false;
            return text;
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
                return;
            }

            if (_journalView != null && _journalView.OwnsSelection(selected))
            {
                _eventSystem.SetSelectedGameObject(null);
            }

            if (_dialogueView != null && _dialogueView.OwnsSelection(selected))
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

        void RequestSpellFilterText(string value)
        {
            if (!RuntimeShellRequestBridge.TrySetSpellFilterText(value, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed setting spell filter text: {error}");
        }

        void RequestMapWindowRectUpdate()
        {
            Rect rect = RuntimeWindowSurfaceUtility.CaptureNormalizedRect(_mapView.Root, _suiteRoot);
            if (!RuntimeShellRequestBridge.TrySetMapWindowRect(rect, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed updating map window rect: {error}");
        }

        void RequestJournalQuestSelection(int dialogueIndex)
        {
            if (!RuntimeShellRequestBridge.TryOpenJournalQuest(dialogueIndex, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed opening journal quest {dialogueIndex}: {error}");
        }

        void RequestJournalShowAll(bool showAll)
        {
            if (!RuntimeShellRequestBridge.TrySetJournalShowAll(showAll, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed setting journal filter: {error}");
        }

        void RequestJournalScroll(float questScrollY, float entryScrollY)
        {
            if (!RuntimeShellRequestBridge.TrySetJournalScroll(questScrollY, entryScrollY, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed updating journal scroll: {error}");
        }

        void RequestJournalPage(int page)
        {
            if (!RuntimeShellRequestBridge.TrySetJournalPage(page, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed updating journal page: {error}");
        }

        void RequestJournalOverlay(bool open)
        {
            if (!RuntimeShellRequestBridge.TrySetJournalOverlay(open, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed updating journal overlay: {error}");
        }

        void RequestJournalMainBook()
        {
            if (!RuntimeShellRequestBridge.TryOpenJournalMainBook(out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed opening main journal book: {error}");
        }

        void RequestJournalClose()
        {
            if (!RuntimeShellRequestBridge.TryCloseJournal(out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed closing journal: {error}");
        }

        void RequestDialogueTopicSelection(int dialogueIndex)
        {
            if (!RuntimeShellRequestBridge.TrySelectDialogueTopic(dialogueIndex, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed selecting dialogue topic {dialogueIndex}: {error}");
        }

        void RequestDialogueChoiceSelection(int choiceValue)
        {
            if (!RuntimeShellRequestBridge.TryAnswerDialogueChoice(choiceValue, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed selecting dialogue choice {choiceValue}: {error}");
        }

        void RequestDialogueGoodbye()
        {
            if (!RuntimeShellRequestBridge.TryDialogueGoodbye(out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed requesting dialogue goodbye: {error}");
        }

        void RequestDialogueClose()
        {
            if (!RuntimeShellRequestBridge.TryCloseDialogue(out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed closing dialogue: {error}");
        }

        void RequestInventoryFilterText(string value)
        {
            if (!RuntimeShellRequestBridge.TrySetInventoryFilterText(value, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed setting inventory filter text: {error}");
        }

        void RequestInventoryItemDragged(int inventoryIndex, InventoryItemClickContext context)
        {
            if (_dragActive)
            {
                RequestDropHeldToInventory();
                return;
            }

            var entry = FindEntry(_inventoryModel?.Entries, inventoryIndex);
            if (entry == null)
                return;

            if (context.Alt)
                RequestInventorySelection(inventoryIndex);
            BeginItemActionWithCount(
                InventoryItemOwnerKind.PlayerInventory,
                context.Alt ? InventoryItemActionKind.DirectTransfer : InventoryItemActionKind.BeginDrag,
                inventoryIndex,
                entry,
                context);
        }

        void RequestInventoryItemRightClicked(int inventoryIndex)
        {
            var entry = FindEntry(_inventoryModel?.Entries, inventoryIndex);
            if (entry == null || !entry.Equipped)
                return;

            if (!RuntimeShellRequestBridge.TryUnequipInventoryItem(inventoryIndex, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed unequipping inventory item {inventoryIndex}: {error}");
        }

        bool IsInventoryItemSelected(int inventoryIndex)
            => FindEntry(_inventoryModel?.Entries, inventoryIndex)?.Selected == true;

        void RequestContainerItemDragged(int itemIndex, InventoryItemClickContext context)
        {
            if (_dragActive)
            {
                RequestDropHeldToContainer();
                return;
            }

            var entry = FindEntry(_containerModel?.Entries, itemIndex);
            if (entry == null)
                return;

            if (context.Alt)
                RequestContainerSelection(itemIndex);
            BeginItemActionWithCount(
                InventoryItemOwnerKind.Container,
                context.Alt ? InventoryItemActionKind.DirectTransfer : InventoryItemActionKind.BeginDrag,
                itemIndex,
                entry,
                context);
        }

        bool IsContainerItemSelected(int itemIndex)
            => FindEntry(_containerModel?.Entries, itemIndex)?.Selected == true;

        void BeginItemActionWithCount(
            InventoryItemOwnerKind owner,
            InventoryItemActionKind action,
            int index,
            InventoryWindowEntryViewModel entry,
            InventoryItemClickContext context)
        {
            int max = Mathf.Max(1, ParseCount(entry.CountText));
            int count = context.Control ? 1 : max;
            if (max > 1 && !context.Shift && !context.Control)
            {
                OpenCountDialog(owner, action, index, entry, max, context);
                return;
            }

            ExecuteItemAction(owner, action, index, count, entry, context);
        }

        void OpenCountDialog(
            InventoryItemOwnerKind owner,
            InventoryItemActionKind action,
            int index,
            InventoryWindowEntryViewModel entry,
            int max,
            InventoryItemClickContext context)
        {
            _pendingCountOwner = owner;
            _pendingCountAction = action;
            _pendingCountIndex = index;
            _pendingCountMax = Mathf.Max(1, max);
            _pendingCountEntry = entry;
            _pendingCountScreenPosition = context.ScreenPosition;
            _pendingCountHasScreenPosition = context.HasScreenPosition;
            _countTitle.Text = string.IsNullOrWhiteSpace(entry?.Name) ? "Take" : entry.Name.Trim();
            _countValueText.Text = action == InventoryItemActionKind.DirectTransfer ? "Transfer Count" : "Take Count";
            SetCountValue(_pendingCountMax);
            _countRoot.gameObject.SetActive(true);
            _countRoot.SetAsLastSibling();
            _eventSystem?.SetSelectedGameObject(_countInput.gameObject);
        }

        void ConfirmCountDialog()
        {
            _countRoot.gameObject.SetActive(false);
            ExecuteItemAction(
                _pendingCountOwner,
                _pendingCountAction,
                _pendingCountIndex,
                _pendingCountValue,
                _pendingCountEntry,
                new InventoryItemClickContext(false, false, false, _pendingCountScreenPosition, _pendingCountHasScreenPosition));
            _pendingCountOwner = InventoryItemOwnerKind.None;
            _pendingCountAction = InventoryItemActionKind.None;
            _pendingCountIndex = -1;
            _pendingCountEntry = null;
            _pendingCountHasScreenPosition = false;
        }

        void CancelCountDialog()
        {
            _countRoot.gameObject.SetActive(false);
            _pendingCountOwner = InventoryItemOwnerKind.None;
            _pendingCountAction = InventoryItemActionKind.None;
            _pendingCountIndex = -1;
            _pendingCountEntry = null;
            _pendingCountHasScreenPosition = false;
        }

        void OnCountInputChanged(string value)
        {
            if (_suppressCountInput)
                return;

            if (!int.TryParse(value, out int parsed))
                parsed = 1;
            SetCountValue(parsed);
        }

        void SetCountValue(int value)
        {
            _pendingCountValue = Mathf.Clamp(value, 1, Mathf.Max(1, _pendingCountMax));
            if (_countInput != null)
            {
                _suppressCountInput = true;
                _countInput.SetTextWithoutNotify(_pendingCountValue.ToString());
                _suppressCountInput = false;
            }
        }

        void ExecuteItemAction(
            InventoryItemOwnerKind owner,
            InventoryItemActionKind action,
            int index,
            int count,
            InventoryWindowEntryViewModel entry,
            InventoryItemClickContext context)
        {
            bool success;
            string error;
            if (owner == InventoryItemOwnerKind.PlayerInventory)
            {
                success = action == InventoryItemActionKind.DirectTransfer
                    ? RuntimeShellRequestBridge.TryDirectTransferInventoryItem(index, count, out error)
                    : RuntimeShellRequestBridge.TryBeginInventoryItemDrag(index, count, out error);
            }
            else
            {
                success = action == InventoryItemActionKind.DirectTransfer
                    ? RuntimeShellRequestBridge.TryDirectTransferContainerItem(index, count, out error)
                    : RuntimeShellRequestBridge.TryBeginContainerItemDrag(index, count, out error);
            }

            if (!success)
            {
                Debug.LogWarning($"[VVardenfell][UI] failed requesting inventory item action: {error}");
                return;
            }

            if (action == InventoryItemActionKind.BeginDrag)
                BeginManagedDrag(entry, count, context);
        }

        void BeginManagedDrag(InventoryWindowEntryViewModel entry, int count, InventoryItemClickContext context)
        {
            _dragActive = true;
            _dragEntry = entry;
            RuntimeInventoryItemIconLayoutUtility.SyncSprite(_dragIcon, _dragIconShadow, _iconService.GetSprite(entry?.IconPath));
            string countText = count > 1 ? count.ToString() : string.Empty;
            bool hasCount = !string.IsNullOrEmpty(countText);
            _dragCount.gameObject.SetActive(hasCount);
            _dragCountShadow.gameObject.SetActive(hasCount);
            _dragCount.Text = countText;
            _dragCountShadow.Text = countText;
            _dragIconRoot.gameObject.SetActive(true);
            _dragIconRoot.SetAsLastSibling();
            if (context.HasScreenPosition)
                SetDragIconScreenPosition(context.ScreenPosition);
            else
                SyncDragIconPosition();
        }

        void ClearManagedDrag()
        {
            _dragActive = false;
            _dragEntry = null;
            if (_dragIconRoot != null)
                _dragIconRoot.gameObject.SetActive(false);
        }

        void SyncDragIconPosition()
        {
            if (!_dragActive || _dragIconRoot == null || Mouse.current == null)
                return;

            SetDragIconScreenPosition(Mouse.current.position.ReadValue());
        }

        void FocusWindowUnderPointer()
        {
            if (_eventSystem == null || Mouse.current == null)
                return;

            if (!Mouse.current.leftButton.wasPressedThisFrame && !Mouse.current.rightButton.wasPressedThisFrame)
                return;

            _focusRaycastResults.Clear();
            var pointer = new PointerEventData(_eventSystem)
            {
                position = Mouse.current.position.ReadValue(),
            };
            _eventSystem.RaycastAll(pointer, _focusRaycastResults);
            for (int i = 0; i < _focusRaycastResults.Count; i++)
            {
                var hitObject = _focusRaycastResults[i].gameObject;
                if (hitObject == null)
                    continue;

                RectTransform root = ResolveFocusableWindowRoot(hitObject.transform);
                if (root == null || !root.gameObject.activeInHierarchy)
                    continue;

                BringWindowToFront(root);
                return;
            }
        }

        RectTransform ResolveFocusableWindowRoot(Transform hit)
        {
            if (hit == null)
                return null;

            if (IsHitInWindow(hit, _inventoryView?.Root, out var root)
                || IsHitInWindow(hit, _statsView?.Root, out root)
                || IsHitInWindow(hit, _spellView?.Root, out root)
                || IsHitInWindow(hit, _mapView?.Root, out root)
                || IsHitInWindow(hit, _containerView?.Root, out root)
                || IsHitInWindow(hit, _dialogueView?.Root, out root)
                || IsHitInWindow(hit, _journalView?.Root, out root)
                || IsHitInWindow(hit, _optionsView?.Root, out root)
                || IsHitInWindow(hit, _saveLoadBrowserView?.Root, out root)
                || IsHitInWindow(hit, _restMenuView?.Root, out root))
            {
                return root;
            }

            return null;
        }

        static bool IsHitInWindow(Transform hit, RectTransform root, out RectTransform resolved)
        {
            resolved = null;
            if (root == null || !root.gameObject.activeInHierarchy)
                return false;

            if (!hit.IsChildOf(root))
                return false;

            resolved = root;
            return true;
        }

        void BringWindowToFront(RectTransform windowRoot)
        {
            if (windowRoot == null)
                return;

            if (_suiteRoot != null && windowRoot.IsChildOf(_suiteRoot))
                _suiteRoot.SetAsLastSibling();

            windowRoot.SetAsLastSibling();
            if (_countRoot != null && _countRoot.gameObject.activeSelf)
                _countRoot.SetAsLastSibling();
            if (_modalRoot != null && _modalRoot.gameObject.activeSelf)
                _modalRoot.SetAsLastSibling();
            if (_screenFade != null && _screenFade.gameObject.activeSelf)
                _screenFade.rectTransform.SetAsLastSibling();
            if (_movieRoot != null && _movieRoot.gameObject.activeSelf)
                _movieRoot.SetAsLastSibling();
        }

        void SetDragIconScreenPosition(Vector2 screenPosition)
        {
            if (!_dragActive || _dragIconRoot == null || _dragLayerRoot == null)
                return;

            _dragIconRoot.SetAsLastSibling();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_dragLayerRoot, screenPosition, null, out Vector2 local);
            _dragIconRoot.anchoredPosition = local + RuntimeClassicUiMetrics.Ui(new Vector2(8f, -8f));
        }

        static InventoryWindowEntryViewModel FindEntry(InventoryWindowEntryViewModel[] entries, int inventoryIndex)
        {
            if (entries == null)
                return null;

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].InventoryIndex == inventoryIndex)
                    return entries[i];
            }

            return null;
        }

        static int ParseCount(string countText)
        {
            return int.TryParse(countText, out int value) && value > 0 ? value : 1;
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

        void RequestDropHeldToInventory()
        {
            if (!_dragActive)
                return;

            if (!RuntimeShellRequestBridge.TryDropHeldItemToInventory(out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed dropping held item to inventory: {error}");
            ClearManagedDrag();
        }

        void RequestUseHeldOnAvatar()
        {
            if (!_dragActive)
                return;

            if (!RuntimeShellRequestBridge.TryUseHeldInventoryItem(out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed using held inventory item: {error}");
            ClearManagedDrag();
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

        void RequestDropHeldToContainer()
        {
            if (!_dragActive)
                return;

            if (!RuntimeShellRequestBridge.TryDropHeldItemToContainer(out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed dropping held item to container: {error}");
            ClearManagedDrag();
        }

        bool HasManagedDrag()
            => _dragActive;

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

        void RequestModalButton(int buttonIndex)
        {
            if (!RuntimeShellRequestBridge.TryDismissModal(buttonIndex, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed dismissing runtime shell modal button {buttonIndex}: {error}");
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
            // volumes) are intentionally left null — the values still
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
                    if (!RuntimeShellRequestBridge.TrySetHudShowCrosshair(v, out string error))
                        Debug.LogWarning($"[VVardenfell][UI] failed updating crosshair preference: {error}");
                },
                ShowSubtitles = v =>
                {
                    if (!RuntimeShellRequestBridge.TrySetHudShowSubtitles(v, out string error))
                        Debug.LogWarning($"[VVardenfell][UI] failed updating subtitle preference: {error}");
                },
                MenuTransparency = null,
                Difficulty = VVardenfell.Runtime.Combat.MorrowindCombatSettingsBridge.PublishDifficultyInDefaultWorld,
                Fov = v =>
                {
                    MainCameraUtility.GetRequiredCamera().fieldOfView = v;
                },
                FogDistanceScale = RuntimeVideoSettingsUtility.ApplyFogDistanceScale,
                Gamma = v => Screen.brightness = v,
                Resolution = (w, h, refresh) =>
                {
                    RuntimeScreenResolutionUtility.SetResolution(w, h, Screen.fullScreenMode, refresh);
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
            if (!RuntimeShellRequestBridge.TrySetHudShowCrosshair(_config.ShowCrosshair, out string crosshairError))
                Debug.LogWarning($"[VVardenfell][UI] failed updating crosshair preference: {crosshairError}");
            if (!RuntimeShellRequestBridge.TrySetHudShowSubtitles(_config.ShowSubtitles, out string subtitleError))
                Debug.LogWarning($"[VVardenfell][UI] failed updating subtitle preference: {subtitleError}");

            MainCameraUtility.GetRequiredCamera().fieldOfView = _config.Fov;
            RuntimeVideoSettingsUtility.ApplyFogDistanceScale(_config.FogDistanceScale);
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
                RuntimeScreenResolutionUtility.SetResolution(
                    _config.ResolutionWidth,
                    _config.ResolutionHeight,
                    Screen.fullScreenMode,
                    _config.RefreshRate);
            }
        }

        void SaveConfig()
        {
            if (_config == null)
                return;
            if (!VVardenfell.Core.Config.ConfigStorage.TrySave(_config, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed saving config: {error}");
        }

        static void SetActiveIfChanged(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active)
                go.SetActive(active);
        }

        void OnDestroy()
        {
            if (_moviePlayer != null)
            {
                _moviePlayer.prepareCompleted -= OnMoviePrepared;
                _moviePlayer.errorReceived -= OnMovieError;
                _moviePlayer.loopPointReached -= OnMovieFinished;
            }

            if (_movieTexture != null)
            {
                _movieTexture.Release();
                Destroy(_movieTexture);
                _movieTexture = null;
            }

            _inventoryView?.Dispose();
            _iconService?.Dispose();
            _iconService = null;
            _theme?.Dispose();
            _theme = null;
        }
    }
}
