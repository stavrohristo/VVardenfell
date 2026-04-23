using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.UI
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

        readonly struct InventoryCategoryDefinition
        {
            public InventoryCategoryDefinition(InventoryWindowCategory category, string label)
            {
                Category = category;
                Label = label;
            }

            public InventoryWindowCategory Category { get; }
            public string Label { get; }
        }

        sealed class PauseMenuButtonView
        {
            public RuntimeShellMenuActionId Action;
            public bool Available;
            public Button Button;
            public Image Background;
            public BitmapTextGraphic Label;
        }

        sealed class InventoryCategoryButtonView
        {
            public InventoryWindowCategory Category;
            public MorrowindButtonView View;
        }

        sealed class InventoryRowView
        {
            public int InventoryIndex;
            public RectTransform Root;
            public BorderFrameView Frame;
            public Button Button;
            public Image Icon;
            public BitmapTextGraphic Name;
            public BitmapTextGraphic Count;
            public BitmapTextGraphic Weight;
            public BitmapTextGraphic Value;
        }

        sealed class ContainerRowView
        {
            public int ItemIndex;
            public RectTransform Root;
            public BorderFrameView Frame;
            public Button Button;
            public Image Icon;
            public BitmapTextGraphic Name;
            public BitmapTextGraphic Count;
            public BitmapTextGraphic Weight;
            public BitmapTextGraphic Value;
        }

        static readonly PauseMenuButtonDefinition[] k_ButtonDefinitions =
        {
            new(RuntimeShellMenuActionId.Resume, "Resume", true),
            new(RuntimeShellMenuActionId.Inventory, "Inventory", true),
            new(RuntimeShellMenuActionId.SaveGame, "Save Game", true),
            new(RuntimeShellMenuActionId.LoadGame, "Load Game", false),
            new(RuntimeShellMenuActionId.Options, "Options", false),
            new(RuntimeShellMenuActionId.MainMenu, "Main Menu", false),
            new(RuntimeShellMenuActionId.ExitGame, "Exit Game", true),
        };

        static readonly InventoryCategoryDefinition[] k_CategoryDefinitions =
        {
            new(InventoryWindowCategory.All, "All"),
            new(InventoryWindowCategory.Weapons, "Weapons"),
            new(InventoryWindowCategory.Apparel, "Apparel"),
            new(InventoryWindowCategory.Magic, "Magic"),
            new(InventoryWindowCategory.Misc, "Misc"),
        };

        const float UiScale = 2f;
        const float TextScale = 1.5f;
        const float PausePanelWidth = 272f;
        const float PausePanelHeight = 238f;
        const float PauseButtonHeight = 18f;
        const float PauseButtonSpacing = 6f;
        const float PauseButtonWidth = 196f;
        const float ModalWidth = 252f;
        const float ModalHeight = 104f;
        const float FocusWidth = 320f;
        const float NotificationWidth = 360f;
        const float InventoryCaptionHeight = 18f;
        const float InventoryCaptionInset = 6f;
        const float InventoryClientInset = 8f;
        const float InventoryBackgroundOpacity = 0.84f;
        const float InventoryRowHeight = 26f;
        const float InventoryRowSpacing = 4f;
        const float ContainerButtonHeight = 18f;

        UiRuntimeAssets _assets;
        RuntimeInventoryIconService _iconService;
        EventSystem _eventSystem;
        RectTransform _rootRect;
        RectTransform _hudRoot;
        RectTransform _inventoryRoot;
        MorrowindWindowView _inventoryWindow;
        RuntimeWindowDragHandle _inventoryDragHandle;
        MorrowindWindowView _containerWindow;
        RuntimeWindowDragHandle _containerDragHandle;
        RectTransform _inventoryLeftPane;
        RectTransform _inventoryRightPane;
        BitmapTextGraphic _containerDetailsText;
        BitmapTextGraphic _containerEmptyText;
        RectTransform _containerListViewport;
        RectTransform _containerListContent;
        ScrollRect _containerScrollRect;
        MorrowindButtonView _takeButton;
        MorrowindButtonView _takeAllButton;
        MorrowindButtonView _closeContainerButton;
        BitmapTextGraphic _crosshairText;
        BitmapTextGraphic _focusText;
        BitmapTextGraphic _notificationText;
        Image _weightBarFill;
        BitmapTextGraphic _weightBarText;
        BitmapTextGraphic _armorSummaryText;
        BitmapTextGraphic _searchOverlayText;
        InputField _searchInputField;
        BitmapTextGraphic _inventoryDetailsText;
        RectTransform _inventoryListViewport;
        RectTransform _inventoryListContent;
        ScrollRect _inventoryScrollRect;
        RectTransform _pauseRoot;
        RectTransform _pausePanelRect;
        RectTransform _modalRoot;
        BitmapTextGraphic _pauseTitle;
        BitmapTextGraphic _pauseFooter;
        BitmapTextGraphic _modalTitle;
        BitmapTextGraphic _modalBody;
        BitmapTextGraphic _modalFooter;
        bool _containerOpen;
        bool _inventoryOpen;
        bool _pauseMenuOpen;
        bool _modalOpen;
        bool _suppressInventoryFieldEvents;

        readonly List<PauseMenuButtonView> _buttons = new();
        readonly List<InventoryCategoryButtonView> _categoryButtons = new();
        readonly List<InventoryRowView> _inventoryRows = new();
        readonly List<ContainerRowView> _containerRows = new();

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
            _assets = new UiAssetLoader().Load();
            _iconService = new RuntimeInventoryIconService();
            _eventSystem = RuntimeUiFactory.EnsureEventSystem();

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue - 32;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            _rootRect = RuntimeUiFactory.CreateStretchRect("Root", transform);
            BuildHud();
            BuildInventoryWindow();
            BuildPauseMenu();
            BuildModal();
            gameObject.SetActive(false);
        }

        void BuildHud()
        {
            _hudRoot = RuntimeUiFactory.CreateStretchRect("HudRoot", _rootRect);

            _crosshairText = RuntimeUiFactory.CreateBitmapText(
                "Crosshair",
                _hudRoot,
                _assets.DefaultFont,
                0.95f * TextScale,
                new Color(0.96f, 0.93f, 0.85f),
                BitmapTextAlignment.Center);
            _crosshairText.Text = "+";
            _crosshairText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _crosshairText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _crosshairText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _crosshairText.rectTransform.anchoredPosition = new Vector2(0f, Scale(-8f));
            _crosshairText.rectTransform.sizeDelta = new Vector2(Scale(28f), Scale(28f));

            var focusBackdrop = RuntimeUiFactory.CreateImage("FocusBackdrop", _hudRoot, new Color(0f, 0f, 0f, 0.52f));
            focusBackdrop.raycastTarget = false;
            focusBackdrop.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            focusBackdrop.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            focusBackdrop.rectTransform.pivot = new Vector2(0.5f, 0f);
            focusBackdrop.rectTransform.anchoredPosition = new Vector2(0f, Scale(24f));
            focusBackdrop.rectTransform.sizeDelta = new Vector2(Scale(FocusWidth), Scale(18f));

            _focusText = RuntimeUiFactory.CreateBitmapText(
                "FocusText",
                focusBackdrop.transform,
                _assets.DefaultFont,
                0.52f * TextScale,
                new Color(0.94f, 0.88f, 0.76f),
                BitmapTextAlignment.Center);
            RuntimeUiFactory.Stretch(_focusText.rectTransform);

            var notificationBackdrop = RuntimeUiFactory.CreateImage("NotificationBackdrop", _hudRoot, new Color(0f, 0f, 0f, 0.62f));
            notificationBackdrop.raycastTarget = false;
            notificationBackdrop.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            notificationBackdrop.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            notificationBackdrop.rectTransform.pivot = new Vector2(0.5f, 1f);
            notificationBackdrop.rectTransform.anchoredPosition = new Vector2(0f, Scale(-72f));
            notificationBackdrop.rectTransform.sizeDelta = new Vector2(Scale(NotificationWidth), Scale(22f));

            _notificationText = RuntimeUiFactory.CreateBitmapText(
                "NotificationText",
                notificationBackdrop.transform,
                _assets.DefaultFont,
                0.58f * TextScale,
                new Color(0.96f, 0.92f, 0.74f),
                BitmapTextAlignment.Center);
            RuntimeUiFactory.Stretch(_notificationText.rectTransform);
        }

        void BuildInventoryWindow()
        {
            _inventoryRoot = RuntimeUiFactory.CreateStretchRect("InventoryRoot", _rootRect);
            _inventoryRoot.gameObject.SetActive(false);

            var blocker = RuntimeUiFactory.CreateImage("InventoryBackdrop", _inventoryRoot, new Color(0f, 0f, 0f, 0.12f));
            blocker.raycastTarget = true;
            RuntimeUiFactory.Stretch(blocker.rectTransform);

            _inventoryWindow = RuntimeUiFactory.CreateMorrowindWindow(
                "InventoryWindow",
                _inventoryRoot,
                _assets,
                _assets.TitleFont ?? _assets.DefaultFont,
                "Inventory",
                Scale(InventoryCaptionHeight),
                Scale(InventoryClientInset),
                Scale(InventoryCaptionInset),
                InventoryBackgroundOpacity,
                0.56f * TextScale,
                new Color(0.94f, 0.82f, 0.53f));

            _inventoryWindow.Root.anchorMin = new Vector2(0f, 1f);
            _inventoryWindow.Root.anchorMax = new Vector2(0f, 1f);
            _inventoryWindow.Root.pivot = new Vector2(0f, 1f);

            _inventoryDragHandle = _inventoryWindow.DragSurface.gameObject.AddComponent<RuntimeWindowDragHandle>();
            _inventoryDragHandle.Initialize(_inventoryWindow.Root, _inventoryRoot, RequestInventoryWindowRectUpdate);

            BuildInventoryPanes();
            BuildContainerWindow();
        }

        void BuildInventoryPanes()
        {
            _inventoryLeftPane = RuntimeUiFactory.CreateAnchorRect(
                "LeftPane",
                _inventoryWindow.Client,
                new Vector2(0f, 0f),
                new Vector2(0.37f, 1f),
                new Vector2(0f, 1f),
                Vector2.zero,
                new Vector2(-Scale(6f), 0f));

            _inventoryRightPane = RuntimeUiFactory.CreateAnchorRect(
                "RightPane",
                _inventoryWindow.Client,
                new Vector2(0.37f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
                new Vector2(Scale(6f), 0f),
                Vector2.zero);

            BuildInventoryLeftPane();
            BuildInventoryRightPane();
        }

        void BuildContainerWindow()
        {
            _containerWindow = RuntimeUiFactory.CreateMorrowindWindow(
                "ContainerWindow",
                _inventoryRoot,
                _assets,
                _assets.TitleFont ?? _assets.DefaultFont,
                "Container",
                Scale(InventoryCaptionHeight),
                Scale(InventoryClientInset),
                Scale(InventoryCaptionInset),
                InventoryBackgroundOpacity,
                0.56f * TextScale,
                new Color(0.94f, 0.82f, 0.53f));

            _containerWindow.Root.anchorMin = new Vector2(0f, 1f);
            _containerWindow.Root.anchorMax = new Vector2(0f, 1f);
            _containerWindow.Root.pivot = new Vector2(0f, 1f);
            _containerWindow.Root.gameObject.SetActive(false);

            _containerDragHandle = _containerWindow.DragSurface.gameObject.AddComponent<RuntimeWindowDragHandle>();
            _containerDragHandle.Initialize(_containerWindow.Root, _inventoryRoot, RequestContainerWindowRectUpdate);

            BuildContainerClient();
        }

        void BuildContainerClient()
        {
            float detailsHeight = Scale(28f);
            float footerHeight = Scale(ContainerButtonHeight);

            var detailsRoot = RuntimeUiFactory.CreateAnchoredRect(
                "ContainerDetailsRoot",
                _containerWindow.Client,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, footerHeight + Scale(6f)),
                new Vector2(0f, detailsHeight));

            var detailsFrame = RuntimeUiFactory.CreateBorderFrame(
                "ContainerDetailsFrame",
                detailsRoot,
                RuntimeUiFactory.ResolveThinFrame(_assets),
                new Color(0f, 0f, 0f, 0.72f));
            RuntimeUiFactory.Stretch(detailsFrame.Root);

            _containerDetailsText = RuntimeUiFactory.CreateBitmapText(
                "ContainerDetailsText",
                detailsRoot,
                _assets.DefaultFont,
                0.38f * TextScale,
                new Color(0.94f, 0.88f, 0.76f),
                BitmapTextAlignment.Left);
            RuntimeUiFactory.SetInset(_containerDetailsText.rectTransform, Scale(8f), Scale(5f), Scale(-8f), Scale(-5f));

            var listRoot = RuntimeUiFactory.CreateAnchorRect(
                "ContainerListRoot",
                _containerWindow.Client,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, detailsHeight + footerHeight + Scale(12f)),
                new Vector2(0f, 0f));

            var listFrame = RuntimeUiFactory.CreateBorderFrame(
                "ContainerListFrame",
                listRoot,
                RuntimeUiFactory.ResolveThinFrame(_assets),
                new Color(0f, 0f, 0f, 0.72f));
            RuntimeUiFactory.Stretch(listFrame.Root);

            _containerListViewport = RuntimeUiFactory.CreateStretchRect("Viewport", listFrame.Client);
            var maskImage = _containerListViewport.gameObject.AddComponent<Image>();
            maskImage.color = new Color(1f, 1f, 1f, 0.01f);
            maskImage.raycastTarget = true;
            _containerListViewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            _containerListContent = RuntimeUiFactory.CreateAnchoredRect(
                "Content",
                _containerListViewport,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                new Vector2(0f, 0f));
            _containerListContent.pivot = new Vector2(0.5f, 1f);

            _containerScrollRect = listRoot.gameObject.AddComponent<ScrollRect>();
            _containerScrollRect.viewport = _containerListViewport;
            _containerScrollRect.content = _containerListContent;
            _containerScrollRect.horizontal = false;
            _containerScrollRect.vertical = true;
            _containerScrollRect.movementType = ScrollRect.MovementType.Clamped;
            _containerScrollRect.scrollSensitivity = 24f;

            _containerEmptyText = RuntimeUiFactory.CreateBitmapText(
                "ContainerEmptyText",
                listFrame.Client,
                _assets.DefaultFont,
                0.46f * TextScale,
                new Color(0.76f, 0.73f, 0.66f),
                BitmapTextAlignment.Center);
            RuntimeUiFactory.Stretch(_containerEmptyText.rectTransform);

            var footerRoot = RuntimeUiFactory.CreateAnchoredRect(
                "ContainerFooterRoot",
                _containerWindow.Client,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                Vector2.zero,
                new Vector2(0f, footerHeight));

            BuildContainerButtons(footerRoot);
        }

        void BuildContainerButtons(RectTransform parent)
        {
            const int buttonCount = 3;
            float spacing = Scale(6f);
            for (int i = 0; i < buttonCount; i++)
            {
                float min = i / (float)buttonCount;
                float max = (i + 1) / (float)buttonCount;
                var rect = RuntimeUiFactory.CreateAnchorRect(
                    $"ContainerButton_{i}",
                    parent,
                    new Vector2(min, 0f),
                    new Vector2(max, 1f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(i == 0 ? 0f : spacing * 0.5f, 0f),
                    new Vector2(i == buttonCount - 1 ? 0f : -spacing * 0.5f, 0f));

                string label = i switch
                {
                    0 => "Take",
                    1 => "Take All",
                    _ => "Close",
                };

                var buttonView = RuntimeUiFactory.CreateMorrowindButton(
                    "Button",
                    rect,
                    _assets,
                    _assets.DefaultFont,
                    label,
                    0.36f * TextScale,
                    new Color(0.9f, 0.85f, 0.75f),
                    new Color(0.12f, 0.1f, 0.08f, 0.88f));
                RuntimeUiFactory.Stretch(buttonView.Root);
                buttonView.Button.transition = Selectable.Transition.ColorTint;
                buttonView.Button.colors = CreateButtonColors(true);

                switch (i)
                {
                    case 0:
                        _takeButton = buttonView;
                        buttonView.Button.onClick.AddListener(RequestTakeSelected);
                        break;
                    case 1:
                        _takeAllButton = buttonView;
                        buttonView.Button.onClick.AddListener(RequestTakeAll);
                        break;
                    default:
                        _closeContainerButton = buttonView;
                        buttonView.Button.onClick.AddListener(RequestCloseContainer);
                        break;
                }
            }
        }

        void BuildInventoryLeftPane()
        {
            var weightRoot = RuntimeUiFactory.CreateAnchoredRect(
                "WeightRoot",
                _inventoryLeftPane,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                new Vector2(0f, Scale(24f)));
            weightRoot.pivot = new Vector2(0f, 1f);

            var weightFrame = RuntimeUiFactory.CreateBorderFrame(
                "WeightFrame",
                weightRoot,
                RuntimeUiFactory.ResolveThinFrame(_assets),
                new Color(0f, 0f, 0f, 0.78f));
            RuntimeUiFactory.Stretch(weightFrame.Root);

            var weightFillRoot = RuntimeUiFactory.CreateAnchorRect(
                "WeightFillRoot",
                weightFrame.Client,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                new Vector2(Scale(2f), Scale(2f)),
                new Vector2(Scale(-2f), Scale(-2f)));

            _weightBarFill = RuntimeUiFactory.CreateImage("WeightFill", weightFillRoot, new Color(0.65f, 0.52f, 0.21f, 0.92f));
            _weightBarFill.rectTransform.anchorMin = new Vector2(0f, 0f);
            _weightBarFill.rectTransform.anchorMax = new Vector2(0f, 1f);
            _weightBarFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            _weightBarFill.rectTransform.anchoredPosition = Vector2.zero;
            _weightBarFill.rectTransform.sizeDelta = new Vector2(0f, 0f);
            _weightBarFill.raycastTarget = false;

            _weightBarText = RuntimeUiFactory.CreateBitmapText(
                "WeightText",
                weightRoot,
                _assets.DefaultFont,
                0.44f * TextScale,
                new Color(0.94f, 0.88f, 0.76f),
                BitmapTextAlignment.Center);
            RuntimeUiFactory.Stretch(_weightBarText.rectTransform);

            var dollRoot = RuntimeUiFactory.CreateAnchorRect(
                "PaperDollRoot",
                _inventoryLeftPane,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, Scale(46f)),
                new Vector2(0f, Scale(-32f)));

            var dollFrame = RuntimeUiFactory.CreateBorderFrame(
                "PaperDollFrame",
                dollRoot,
                RuntimeUiFactory.ResolveThinFrame(_assets),
                new Color(0f, 0f, 0f, 0.72f));
            RuntimeUiFactory.Stretch(dollFrame.Root);

            BuildPaperDollPlaceholder(dollFrame.Client);

            var armorRoot = RuntimeUiFactory.CreateAnchoredRect(
                "ArmorSummaryRoot",
                _inventoryLeftPane,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                Vector2.zero,
                new Vector2(0f, Scale(28f)));

            var armorFrame = RuntimeUiFactory.CreateBorderFrame(
                "ArmorSummaryFrame",
                armorRoot,
                RuntimeUiFactory.ResolveThinFrame(_assets),
                new Color(0f, 0f, 0f, 0.72f));
            RuntimeUiFactory.Stretch(armorFrame.Root);

            _armorSummaryText = RuntimeUiFactory.CreateBitmapText(
                "ArmorSummaryText",
                armorRoot,
                _assets.DefaultFont,
                0.38f * TextScale,
                new Color(0.76f, 0.73f, 0.66f),
                BitmapTextAlignment.Center);
            RuntimeUiFactory.Stretch(_armorSummaryText.rectTransform);
        }

        void BuildPaperDollPlaceholder(RectTransform parent)
        {
            var silhouette = RuntimeUiFactory.CreateImage("Silhouette", parent, new Color(0.18f, 0.16f, 0.14f, 0.9f));
            silhouette.rectTransform.anchorMin = new Vector2(0.5f, 0.12f);
            silhouette.rectTransform.anchorMax = new Vector2(0.5f, 0.84f);
            silhouette.rectTransform.pivot = new Vector2(0.5f, 0f);
            silhouette.rectTransform.sizeDelta = new Vector2(Scale(44f), 0f);
            silhouette.raycastTarget = false;

            CreateDollBox(parent, "HeadSlot", new Vector2(0.5f, 0.82f), new Vector2(24f, 18f));
            CreateDollBox(parent, "ChestSlot", new Vector2(0.5f, 0.57f), new Vector2(36f, 28f));
            CreateDollBox(parent, "LegSlot", new Vector2(0.5f, 0.28f), new Vector2(30f, 40f));
            CreateDollBox(parent, "LeftHandSlot", new Vector2(0.2f, 0.5f), new Vector2(22f, 22f));
            CreateDollBox(parent, "RightHandSlot", new Vector2(0.8f, 0.5f), new Vector2(22f, 22f));
            CreateDollBox(parent, "BootSlot", new Vector2(0.34f, 0.1f), new Vector2(20f, 14f));
            CreateDollBox(parent, "BootSlotRight", new Vector2(0.66f, 0.1f), new Vector2(20f, 14f));

            var label = RuntimeUiFactory.CreateBitmapText(
                "PaperDollLabel",
                parent,
                _assets.DefaultFont,
                0.34f * TextScale,
                new Color(0.74f, 0.69f, 0.6f),
                BitmapTextAlignment.Center);
            label.Text = "Paper Doll Placeholder";
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(1f, 0f);
            label.rectTransform.pivot = new Vector2(0.5f, 0f);
            label.rectTransform.anchoredPosition = new Vector2(0f, Scale(6f));
            label.rectTransform.sizeDelta = new Vector2(0f, Scale(12f));
        }

        void CreateDollBox(RectTransform parent, string name, Vector2 anchor, Vector2 size)
        {
            var rect = RuntimeUiFactory.CreateAnchoredRect(
                name,
                parent,
                anchor,
                anchor,
                Vector2.zero,
                Scale(size));
            rect.pivot = new Vector2(0.5f, 0.5f);

            var frame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                rect,
                RuntimeUiFactory.ResolveThinFrame(_assets),
                new Color(0.06f, 0.05f, 0.05f, 0.82f));
            RuntimeUiFactory.Stretch(frame.Root);
        }

        void BuildInventoryRightPane()
        {
            float categoryHeight = Scale(22f);
            float searchHeight = Scale(22f);
            float detailsHeight = Scale(28f);

            var categoryRoot = RuntimeUiFactory.CreateAnchoredRect(
                "CategoryRoot",
                _inventoryRightPane,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                new Vector2(0f, categoryHeight));
            categoryRoot.pivot = new Vector2(0f, 1f);

            BuildInventoryCategories(categoryRoot);

            var searchRoot = RuntimeUiFactory.CreateAnchoredRect(
                "SearchRoot",
                _inventoryRightPane,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -categoryHeight - Scale(6f)),
                new Vector2(0f, searchHeight));
            searchRoot.pivot = new Vector2(0f, 1f);

            BuildInventorySearchField(searchRoot);

            var detailsRoot = RuntimeUiFactory.CreateAnchoredRect(
                "DetailsRoot",
                _inventoryRightPane,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                Vector2.zero,
                new Vector2(0f, detailsHeight));

            var detailsFrame = RuntimeUiFactory.CreateBorderFrame(
                "DetailsFrame",
                detailsRoot,
                RuntimeUiFactory.ResolveThinFrame(_assets),
                new Color(0f, 0f, 0f, 0.72f));
            RuntimeUiFactory.Stretch(detailsFrame.Root);

            _inventoryDetailsText = RuntimeUiFactory.CreateBitmapText(
                "DetailsText",
                detailsRoot,
                _assets.DefaultFont,
                0.38f * TextScale,
                new Color(0.94f, 0.88f, 0.76f),
                BitmapTextAlignment.Left);
            RuntimeUiFactory.SetInset(_inventoryDetailsText.rectTransform, Scale(8f), Scale(5f), Scale(-8f), Scale(-5f));

            var listRoot = RuntimeUiFactory.CreateAnchorRect(
                "ItemListRoot",
                _inventoryRightPane,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, detailsHeight + Scale(6f)),
                new Vector2(0f, -(categoryHeight + searchHeight + Scale(16f))));

            var listFrame = RuntimeUiFactory.CreateBorderFrame(
                "ItemListFrame",
                listRoot,
                RuntimeUiFactory.ResolveThinFrame(_assets),
                new Color(0f, 0f, 0f, 0.72f));
            RuntimeUiFactory.Stretch(listFrame.Root);

            _inventoryListViewport = RuntimeUiFactory.CreateStretchRect("Viewport", listFrame.Client);
            var maskImage = _inventoryListViewport.gameObject.AddComponent<Image>();
            maskImage.color = new Color(1f, 1f, 1f, 0.01f);
            maskImage.raycastTarget = true;
            _inventoryListViewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            _inventoryListContent = RuntimeUiFactory.CreateAnchoredRect(
                "Content",
                _inventoryListViewport,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                new Vector2(0f, 0f));
            _inventoryListContent.pivot = new Vector2(0.5f, 1f);

            _inventoryScrollRect = listRoot.gameObject.AddComponent<ScrollRect>();
            _inventoryScrollRect.viewport = _inventoryListViewport;
            _inventoryScrollRect.content = _inventoryListContent;
            _inventoryScrollRect.horizontal = false;
            _inventoryScrollRect.vertical = true;
            _inventoryScrollRect.movementType = ScrollRect.MovementType.Clamped;
            _inventoryScrollRect.scrollSensitivity = 24f;
        }

        void BuildInventoryCategories(RectTransform parent)
        {
            float spacing = Scale(4f);
            for (int i = 0; i < k_CategoryDefinitions.Length; i++)
            {
                var definition = k_CategoryDefinitions[i];
                float min = i / (float)k_CategoryDefinitions.Length;
                float max = (i + 1) / (float)k_CategoryDefinitions.Length;
                var rect = RuntimeUiFactory.CreateAnchorRect(
                    $"Category_{definition.Category}",
                    parent,
                    new Vector2(min, 0f),
                    new Vector2(max, 1f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(i == 0 ? 0f : spacing * 0.5f, 0f),
                    new Vector2(i == k_CategoryDefinitions.Length - 1 ? 0f : -spacing * 0.5f, 0f));

                var buttonView = RuntimeUiFactory.CreateMorrowindButton(
                    "Button",
                    rect,
                    _assets,
                    _assets.DefaultFont,
                    definition.Label,
                    0.34f * TextScale,
                    new Color(0.9f, 0.85f, 0.75f),
                    new Color(0.12f, 0.1f, 0.08f, 0.88f));
                RuntimeUiFactory.Stretch(buttonView.Root);
                buttonView.Button.transition = Selectable.Transition.None;
                InventoryWindowCategory category = definition.Category;
                buttonView.Button.onClick.AddListener(() => RequestInventoryCategory(category));

                _categoryButtons.Add(new InventoryCategoryButtonView
                {
                    Category = category,
                    View = buttonView,
                });
            }
        }

        void BuildInventorySearchField(RectTransform root)
        {
            var searchFrame = RuntimeUiFactory.CreateBorderFrame(
                "SearchFrame",
                root,
                RuntimeUiFactory.ResolveThinFrame(_assets),
                new Color(0f, 0f, 0f, 0.74f));
            RuntimeUiFactory.Stretch(searchFrame.Root);

            var inputRoot = RuntimeUiFactory.CreateStretchRect("InputRoot", searchFrame.Client);
            var inputImage = inputRoot.gameObject.AddComponent<Image>();
            inputImage.color = new Color(1f, 1f, 1f, 0f);
            inputImage.raycastTarget = true;

            _searchInputField = inputRoot.gameObject.AddComponent<InputField>();
            _searchInputField.lineType = InputField.LineType.SingleLine;
            _searchInputField.contentType = InputField.ContentType.Standard;
            _searchInputField.targetGraphic = inputImage;

            Font hiddenFont = GetHiddenInputFont();

            var hiddenTextGo = new GameObject("HiddenText", typeof(RectTransform), typeof(Text));
            hiddenTextGo.transform.SetParent(inputRoot, false);
            var hiddenText = hiddenTextGo.GetComponent<Text>();
            hiddenText.font = hiddenFont;
            hiddenText.fontSize = 16;
            hiddenText.supportRichText = false;
            hiddenText.alignment = TextAnchor.MiddleLeft;
            hiddenText.color = new Color(1f, 1f, 1f, 0f);
            RuntimeUiFactory.SetInset((RectTransform)hiddenText.transform, Scale(6f), 0f, Scale(-6f), 0f);

            var hiddenPlaceholderGo = new GameObject("HiddenPlaceholder", typeof(RectTransform), typeof(Text));
            hiddenPlaceholderGo.transform.SetParent(inputRoot, false);
            var hiddenPlaceholder = hiddenPlaceholderGo.GetComponent<Text>();
            hiddenPlaceholder.font = hiddenFont;
            hiddenPlaceholder.fontSize = 16;
            hiddenPlaceholder.supportRichText = false;
            hiddenPlaceholder.alignment = TextAnchor.MiddleLeft;
            hiddenPlaceholder.color = new Color(1f, 1f, 1f, 0f);
            hiddenPlaceholder.text = "Filter";
            RuntimeUiFactory.SetInset((RectTransform)hiddenPlaceholder.transform, Scale(6f), 0f, Scale(-6f), 0f);

            _searchInputField.textComponent = hiddenText;
            _searchInputField.placeholder = hiddenPlaceholder;
            _searchInputField.caretColor = new Color(0.94f, 0.82f, 0.53f, 1f);
            _searchInputField.selectionColor = new Color(0.38f, 0.28f, 0.12f, 0.72f);
            _searchInputField.onValueChanged.AddListener(OnInventoryFilterChanged);

            _searchOverlayText = RuntimeUiFactory.CreateBitmapText(
                "SearchOverlayText",
                inputRoot,
                _assets.DefaultFont,
                0.38f * TextScale,
                new Color(0.94f, 0.88f, 0.76f),
                BitmapTextAlignment.Left);
            RuntimeUiFactory.SetInset(_searchOverlayText.rectTransform, Scale(6f), Scale(4f), Scale(-6f), Scale(-4f));
            _searchOverlayText.Text = string.Empty;
        }

        void BuildPauseMenu()
        {
            _pauseRoot = RuntimeUiFactory.CreateStretchRect("PauseRoot", _rootRect);
            _pauseRoot.gameObject.SetActive(false);

            var blocker = RuntimeUiFactory.CreateImage("PauseBackdrop", _pauseRoot, new Color(0f, 0f, 0f, 0.58f));
            blocker.raycastTarget = true;
            RuntimeUiFactory.Stretch(blocker.rectTransform);

            _pausePanelRect = RuntimeUiFactory.CreateAnchoredRect(
                "PausePanel",
                _pauseRoot,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Scale(new Vector2(PausePanelWidth, PausePanelHeight)));
            _pausePanelRect.pivot = new Vector2(0.5f, 0.5f);

            var frame = RuntimeUiFactory.CreateBorderFrame(
                "PauseFrame",
                _pausePanelRect,
                RuntimeUiFactory.ResolveThickFrame(_assets),
                new Color(0f, 0f, 0f, 0.94f));
            RuntimeUiFactory.Stretch(frame.Root);

            _pauseTitle = RuntimeUiFactory.CreateBitmapText(
                "PauseTitle",
                _pausePanelRect,
                _assets.TitleFont ?? _assets.DefaultFont,
                0.72f * TextScale,
                new Color(0.94f, 0.82f, 0.53f),
                BitmapTextAlignment.Center);
            _pauseTitle.Text = "Paused";
            _pauseTitle.rectTransform.anchorMin = new Vector2(0f, 1f);
            _pauseTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
            _pauseTitle.rectTransform.pivot = new Vector2(0.5f, 1f);
            _pauseTitle.rectTransform.anchoredPosition = new Vector2(0f, Scale(-14f));
            _pauseTitle.rectTransform.sizeDelta = new Vector2(Scale(-36f), Scale(18f));

            var buttonRoot = RuntimeUiFactory.CreateAnchoredRect(
                "PauseButtons",
                _pausePanelRect,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, Scale(-44f)),
                Scale(new Vector2(PauseButtonWidth, 1f)));
            buttonRoot.pivot = new Vector2(0.5f, 1f);

            float y = 0f;
            for (int i = 0; i < k_ButtonDefinitions.Length; i++)
            {
                var definition = k_ButtonDefinitions[i];
                var buttonRect = RuntimeUiFactory.CreateAnchoredRect(
                    $"PauseButton_{definition.Action}",
                    buttonRoot,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, -y),
                    Scale(new Vector2(PauseButtonWidth, PauseButtonHeight)));

                var background = RuntimeUiFactory.CreateImage(
                    "Background",
                    buttonRect,
                    definition.Available
                        ? new Color(0.2f, 0.16f, 0.11f, 0.9f)
                        : new Color(0.14f, 0.12f, 0.1f, 0.76f));
                RuntimeUiFactory.Stretch(background.rectTransform);
                background.raycastTarget = true;

                var button = buttonRect.gameObject.AddComponent<Button>();
                button.targetGraphic = background;
                button.transition = Selectable.Transition.ColorTint;
                button.colors = CreateButtonColors(definition.Available);
                button.navigation = new Navigation { mode = Navigation.Mode.None };

                RuntimeShellMenuActionId action = definition.Action;
                button.onClick.AddListener(() => RequestAction(action));

                var label = RuntimeUiFactory.CreateBitmapText(
                    "Label",
                    buttonRect,
                    _assets.DefaultFont,
                    0.58f * TextScale,
                    definition.Available
                        ? new Color(0.94f, 0.88f, 0.76f)
                        : new Color(0.7f, 0.67f, 0.61f),
                    BitmapTextAlignment.Center);
                label.Text = definition.Label;
                RuntimeUiFactory.Stretch(label.rectTransform);

                _buttons.Add(new PauseMenuButtonView
                {
                    Action = definition.Action,
                    Available = definition.Available,
                    Button = button,
                    Background = background,
                    Label = label,
                });

                y += Scale(PauseButtonHeight + PauseButtonSpacing);
            }

            _pauseFooter = RuntimeUiFactory.CreateBitmapText(
                "PauseFooter",
                _pausePanelRect,
                _assets.DefaultFont,
                0.42f * TextScale,
                new Color(0.76f, 0.73f, 0.66f),
                BitmapTextAlignment.Center);
            _pauseFooter.Text = "Press Escape or Start to resume";
            _pauseFooter.rectTransform.anchorMin = new Vector2(0f, 0f);
            _pauseFooter.rectTransform.anchorMax = new Vector2(1f, 0f);
            _pauseFooter.rectTransform.pivot = new Vector2(0.5f, 0f);
            _pauseFooter.rectTransform.anchoredPosition = new Vector2(0f, Scale(12f));
            _pauseFooter.rectTransform.sizeDelta = new Vector2(Scale(-36f), Scale(14f));

            ConfigurePauseNavigation();
        }

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
                Scale(new Vector2(ModalWidth, ModalHeight)));
            rect.pivot = new Vector2(0.5f, 0.5f);

            var frame = RuntimeUiFactory.CreateBorderFrame(
                "ModalFrame",
                rect,
                RuntimeUiFactory.ResolveThickFrame(_assets),
                new Color(0f, 0f, 0f, 0.94f));
            RuntimeUiFactory.Stretch(frame.Root);

            _modalTitle = RuntimeUiFactory.CreateBitmapText(
                "ModalTitle",
                rect,
                _assets.TitleFont ?? _assets.DefaultFont,
                0.62f * TextScale,
                new Color(0.94f, 0.82f, 0.53f),
                BitmapTextAlignment.Center);
            _modalTitle.rectTransform.anchorMin = new Vector2(0f, 1f);
            _modalTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
            _modalTitle.rectTransform.pivot = new Vector2(0.5f, 1f);
            _modalTitle.rectTransform.anchoredPosition = new Vector2(0f, Scale(-12f));
            _modalTitle.rectTransform.sizeDelta = new Vector2(Scale(-24f), Scale(16f));

            _modalBody = RuntimeUiFactory.CreateBitmapText(
                "ModalBody",
                rect,
                _assets.DefaultFont,
                0.5f * TextScale,
                new Color(0.93f, 0.88f, 0.75f),
                BitmapTextAlignment.Center);
            _modalBody.rectTransform.anchorMin = new Vector2(0f, 0f);
            _modalBody.rectTransform.anchorMax = new Vector2(1f, 1f);
            _modalBody.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _modalBody.rectTransform.anchoredPosition = new Vector2(0f, Scale(-2f));
            _modalBody.rectTransform.sizeDelta = new Vector2(Scale(-34f), Scale(-54f));

            _modalFooter = RuntimeUiFactory.CreateBitmapText(
                "ModalFooter",
                rect,
                _assets.DefaultFont,
                0.38f * TextScale,
                new Color(0.76f, 0.73f, 0.66f),
                BitmapTextAlignment.Center);
            _modalFooter.Text = "Press Escape, B, or click to return";
            _modalFooter.rectTransform.anchorMin = new Vector2(0f, 0f);
            _modalFooter.rectTransform.anchorMax = new Vector2(1f, 0f);
            _modalFooter.rectTransform.pivot = new Vector2(0.5f, 0f);
            _modalFooter.rectTransform.anchoredPosition = new Vector2(0f, Scale(10f));
            _modalFooter.rectTransform.sizeDelta = new Vector2(Scale(-24f), Scale(12f));
        }

        public void Sync(
            bool visible,
            bool showHud,
            bool showCrosshair,
            string focusText,
            string notificationText,
            InventoryWindowViewModel inventoryModel,
            ContainerWindowViewModel containerModel,
            RuntimeShellMenuActionId selectedAction,
            bool pauseMenuOpen,
            bool modalOpen,
            string modalTitle,
            string modalBody)
        {
            if (_rootRect == null)
                return;

            gameObject.SetActive(visible);
            if (!visible)
            {
                ClearSelectionIfOwned();
                _containerOpen = false;
                _inventoryOpen = false;
                _pauseMenuOpen = false;
                _modalOpen = false;
                return;
            }

            bool inventoryOpen = inventoryModel != null;
            bool containerOpen = containerModel != null;
            bool containerOpened = !_containerOpen && containerOpen;
            bool inventoryOpened = !_inventoryOpen && inventoryOpen;
            bool pauseOpened = !_pauseMenuOpen && pauseMenuOpen;
            bool modalClosed = _modalOpen && !modalOpen;

            _containerOpen = containerOpen;
            _inventoryOpen = inventoryOpen;
            _pauseMenuOpen = pauseMenuOpen;
            _modalOpen = modalOpen;

            _hudRoot.gameObject.SetActive(showHud);
            _crosshairText.gameObject.SetActive(showHud && showCrosshair);
            _focusText.transform.parent.gameObject.SetActive(showHud && !string.IsNullOrWhiteSpace(focusText));
            _notificationText.transform.parent.gameObject.SetActive(showHud && !string.IsNullOrWhiteSpace(notificationText));
            _focusText.Text = focusText ?? string.Empty;
            _notificationText.Text = notificationText ?? string.Empty;

            _inventoryRoot.gameObject.SetActive(inventoryOpen || containerOpen);
            if (inventoryOpen)
                SyncInventoryWindow(inventoryModel);
            if (_containerWindow != null)
                _containerWindow.Root.gameObject.SetActive(containerOpen);
            if (containerOpen)
                SyncContainerWindow(containerModel);

            _pauseRoot.gameObject.SetActive(pauseMenuOpen);
            _modalRoot.gameObject.SetActive(pauseMenuOpen && modalOpen);

            if (_modalRoot.gameObject.activeSelf)
            {
                _modalTitle.Text = string.IsNullOrWhiteSpace(modalTitle) ? "Unavailable" : modalTitle.Trim();
                _modalBody.Text = RuntimeUiFactory.WrapText(
                    _assets.DefaultFont,
                    string.IsNullOrWhiteSpace(modalBody) ? "This action is not available in the current runtime shell slice." : modalBody.Trim(),
                    _modalBody.FontScale,
                    Scale(ModalWidth - 48f));
                _eventSystem?.SetSelectedGameObject(null);
            }
            else if (pauseMenuOpen && (pauseOpened || modalClosed))
            {
                RestorePauseSelection(selectedAction);
            }
            else if ((inventoryOpen && inventoryOpened) || (containerOpen && containerOpened))
            {
                ClearSelectionIfOwned();
            }

            if (!pauseMenuOpen && !inventoryOpen && !containerOpen)
                ClearSelectionIfOwned();
        }

        void SyncInventoryWindow(InventoryWindowViewModel model)
        {
            _inventoryWindow.Title.Text = string.IsNullOrWhiteSpace(model.Title) ? "Inventory" : model.Title.Trim();

            if (_inventoryDragHandle == null || !_inventoryDragHandle.IsDragging)
                ApplyInventoryRect(model.NormalizedRect);

            _weightBarText.Text = model.WeightLabel ?? "Weight -- / --";
            _armorSummaryText.Text = model.ArmorSummary ?? string.Empty;
            _inventoryDetailsText.Text = RuntimeUiFactory.WrapText(
                _assets.DefaultFont,
                model.DetailText ?? string.Empty,
                _inventoryDetailsText.FontScale,
                _inventoryDetailsText.rectTransform.rect.width > 0f
                    ? _inventoryDetailsText.rectTransform.rect.width
                    : Scale(260f));

            float fillWidth = Mathf.Max(0f, _weightBarFill.transform.parent.GetComponent<RectTransform>().rect.width * Mathf.Clamp01(model.WeightBarFillNormalized));
            _weightBarFill.rectTransform.sizeDelta = new Vector2(fillWidth, 0f);

            SyncInventoryFilter(model.FilterText);
            SyncInventoryCategories(model.Category);
            SyncInventoryRows(model.Entries);
        }

        void SyncContainerWindow(ContainerWindowViewModel model)
        {
            _containerWindow.Title.Text = string.IsNullOrWhiteSpace(model.Title) ? "Container" : model.Title.Trim();

            if (_containerDragHandle == null || !_containerDragHandle.IsDragging)
                ApplyContainerRect(model.NormalizedRect);

            _containerDetailsText.Text = RuntimeUiFactory.WrapText(
                _assets.DefaultFont,
                model.DetailText ?? string.Empty,
                _containerDetailsText.FontScale,
                _containerDetailsText.rectTransform.rect.width > 0f
                    ? _containerDetailsText.rectTransform.rect.width
                    : Scale(220f));

            _containerEmptyText.Text = model.EmptyStateText ?? string.Empty;
            _containerEmptyText.gameObject.SetActive(model.Entries == null || model.Entries.Length == 0);
            SyncContainerRows(model.Entries);
            SyncContainerButtonState(_takeButton, model.CanTakeSelected);
            SyncContainerButtonState(_takeAllButton, model.CanTakeAll);
            SyncContainerButtonState(_closeContainerButton, true);
        }

        void SyncContainerButtonState(MorrowindButtonView buttonView, bool enabled)
        {
            if (buttonView?.Button == null)
                return;

            buttonView.Button.interactable = enabled;
            buttonView.Frame.Center.color = enabled
                ? new Color(0.12f, 0.1f, 0.08f, 0.88f)
                : new Color(0.08f, 0.07f, 0.06f, 0.66f);
            buttonView.Label.color = enabled
                ? new Color(0.9f, 0.85f, 0.75f)
                : new Color(0.58f, 0.55f, 0.5f);
        }

        void SyncInventoryFilter(string filterText)
        {
            string value = filterText ?? string.Empty;
            _searchOverlayText.Text = string.IsNullOrEmpty(value) ? "Filter items" : value;
            _searchOverlayText.color = string.IsNullOrEmpty(value)
                ? new Color(0.62f, 0.58f, 0.52f)
                : new Color(0.94f, 0.88f, 0.76f);

            if (_searchInputField == null || _searchInputField.text == value)
                return;

            _suppressInventoryFieldEvents = true;
            _searchInputField.SetTextWithoutNotify(value);
            _suppressInventoryFieldEvents = false;
        }

        void SyncInventoryCategories(InventoryWindowCategory activeCategory)
        {
            for (int i = 0; i < _categoryButtons.Count; i++)
            {
                var categoryButton = _categoryButtons[i];
                bool selected = categoryButton.Category == activeCategory;
                categoryButton.View.Frame.Center.color = selected
                    ? new Color(0.38f, 0.28f, 0.14f, 0.96f)
                    : new Color(0.12f, 0.1f, 0.08f, 0.88f);
                categoryButton.View.Label.color = selected
                    ? new Color(0.98f, 0.93f, 0.8f)
                    : new Color(0.9f, 0.85f, 0.75f);
            }
        }

        void SyncInventoryRows(InventoryWindowEntryViewModel[] entries)
        {
            int count = entries?.Length ?? 0;
            EnsureInventoryRowCount(count);

            float y = Scale(4f);
            float rowHeight = Scale(InventoryRowHeight);
            float spacing = Scale(InventoryRowSpacing);
            float width = _inventoryListViewport.rect.width;
            if (width <= 0f)
                width = _inventoryListViewport.sizeDelta.x;

            for (int i = 0; i < _inventoryRows.Count; i++)
            {
                bool active = i < count;
                var row = _inventoryRows[i];
                row.Root.gameObject.SetActive(active);
                if (!active)
                    continue;

                var entry = entries[i];
                row.InventoryIndex = entry.InventoryIndex;
                row.Name.Text = entry.Name ?? "Unknown item";
                row.Count.Text = string.IsNullOrWhiteSpace(entry.CountText) ? string.Empty : $"x{entry.CountText}";
                row.Weight.Text = $"Wt {entry.WeightText ?? "--"}";
                row.Value.Text = $"Val {entry.ValueText ?? "--"}";
                row.Icon.sprite = _iconService.GetSprite(entry.IconPath);
                row.Icon.preserveAspect = true;

                row.Frame.Center.color = entry.Selected
                    ? new Color(0.36f, 0.27f, 0.12f, 0.95f)
                    : new Color(0.08f, 0.07f, 0.06f, 0.86f);
                Color labelColor = entry.Selected
                    ? new Color(0.98f, 0.93f, 0.82f)
                    : new Color(0.9f, 0.85f, 0.75f);
                row.Name.color = labelColor;
                row.Count.color = labelColor;
                row.Weight.color = entry.Selected
                    ? new Color(0.94f, 0.88f, 0.76f)
                    : new Color(0.76f, 0.73f, 0.66f);
                row.Value.color = row.Weight.color;

                row.Root.anchorMin = new Vector2(0f, 1f);
                row.Root.anchorMax = new Vector2(1f, 1f);
                row.Root.pivot = new Vector2(0.5f, 1f);
                row.Root.anchoredPosition = new Vector2(0f, -y);
                row.Root.sizeDelta = new Vector2(0f, rowHeight);

                y += rowHeight + spacing;
            }

            float totalHeight = count > 0 ? y : Scale(8f);
            _inventoryListContent.sizeDelta = new Vector2(0f, totalHeight);
        }

        void EnsureInventoryRowCount(int count)
        {
            while (_inventoryRows.Count < count)
                _inventoryRows.Add(CreateInventoryRow(_inventoryListContent, _inventoryRows.Count));
        }

        void SyncContainerRows(InventoryWindowEntryViewModel[] entries)
        {
            int count = entries?.Length ?? 0;
            EnsureContainerRowCount(count);

            float y = Scale(4f);
            float rowHeight = Scale(InventoryRowHeight);
            float spacing = Scale(InventoryRowSpacing);

            for (int i = 0; i < _containerRows.Count; i++)
            {
                bool active = i < count;
                var row = _containerRows[i];
                row.Root.gameObject.SetActive(active);
                if (!active)
                    continue;

                var entry = entries[i];
                row.ItemIndex = entry.InventoryIndex;
                row.Name.Text = entry.Name ?? "Unknown item";
                row.Count.Text = string.IsNullOrWhiteSpace(entry.CountText) ? string.Empty : $"x{entry.CountText}";
                row.Weight.Text = $"Wt {entry.WeightText ?? "--"}";
                row.Value.Text = $"Val {entry.ValueText ?? "--"}";
                row.Icon.sprite = _iconService.GetSprite(entry.IconPath);
                row.Icon.preserveAspect = true;

                row.Frame.Center.color = entry.Selected
                    ? new Color(0.36f, 0.27f, 0.12f, 0.95f)
                    : new Color(0.08f, 0.07f, 0.06f, 0.86f);
                Color labelColor = entry.Selected
                    ? new Color(0.98f, 0.93f, 0.82f)
                    : new Color(0.9f, 0.85f, 0.75f);
                row.Name.color = labelColor;
                row.Count.color = labelColor;
                row.Weight.color = entry.Selected
                    ? new Color(0.94f, 0.88f, 0.76f)
                    : new Color(0.76f, 0.73f, 0.66f);
                row.Value.color = row.Weight.color;

                row.Root.anchorMin = new Vector2(0f, 1f);
                row.Root.anchorMax = new Vector2(1f, 1f);
                row.Root.pivot = new Vector2(0.5f, 1f);
                row.Root.anchoredPosition = new Vector2(0f, -y);
                row.Root.sizeDelta = new Vector2(0f, rowHeight);

                y += rowHeight + spacing;
            }

            float totalHeight = count > 0 ? y : Scale(8f);
            _containerListContent.sizeDelta = new Vector2(0f, totalHeight);
        }

        void EnsureContainerRowCount(int count)
        {
            while (_containerRows.Count < count)
                _containerRows.Add(CreateContainerRow(_containerListContent, _containerRows.Count));
        }

        InventoryRowView CreateInventoryRow(Transform parent, int index)
        {
            var root = RuntimeUiFactory.CreateAnchoredRect(
                $"InventoryRow_{index}",
                parent,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                new Vector2(0f, Scale(InventoryRowHeight)));
            root.pivot = new Vector2(0.5f, 1f);
            var frame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                root,
                RuntimeUiFactory.ResolveThinFrame(_assets),
                new Color(0.08f, 0.07f, 0.06f, 0.86f));
            RuntimeUiFactory.Stretch(frame.Root);
            frame.Center.raycastTarget = true;

            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = frame.Center;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            var iconFrameRoot = RuntimeUiFactory.CreateAnchoredRect(
                "IconFrameRoot",
                frame.Client,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(Scale(4f), 0f),
                new Vector2(Scale(24f), Scale(24f)));
            iconFrameRoot.pivot = new Vector2(0f, 0.5f);

            var iconFrame = RuntimeUiFactory.CreateBorderFrame(
                "IconFrame",
                iconFrameRoot,
                RuntimeUiFactory.ResolveThinFrame(_assets),
                new Color(0.02f, 0.02f, 0.02f, 0.88f));
            RuntimeUiFactory.Stretch(iconFrame.Root);

            var icon = RuntimeUiFactory.CreateImage("Icon", iconFrame.Client, Color.white);
            RuntimeUiFactory.SetInset(icon.rectTransform, Scale(2f), Scale(2f), Scale(-2f), Scale(-2f));
            icon.raycastTarget = false;

            var name = RuntimeUiFactory.CreateBitmapText(
                "Name",
                frame.Client,
                _assets.DefaultFont,
                0.38f * TextScale,
                new Color(0.9f, 0.85f, 0.75f),
                BitmapTextAlignment.Left);
            name.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            name.rectTransform.anchorMax = new Vector2(1f, 1f);
            name.rectTransform.pivot = new Vector2(0f, 1f);
            name.rectTransform.anchoredPosition = new Vector2(Scale(34f), Scale(-2f));
            name.rectTransform.sizeDelta = new Vector2(Scale(-124f), Scale(10f));

            var countText = RuntimeUiFactory.CreateBitmapText(
                "Count",
                frame.Client,
                _assets.DefaultFont,
                0.34f * TextScale,
                new Color(0.9f, 0.85f, 0.75f),
                BitmapTextAlignment.Right);
            countText.rectTransform.anchorMin = new Vector2(1f, 0.5f);
            countText.rectTransform.anchorMax = new Vector2(1f, 1f);
            countText.rectTransform.pivot = new Vector2(1f, 1f);
            countText.rectTransform.anchoredPosition = new Vector2(Scale(-6f), Scale(-2f));
            countText.rectTransform.sizeDelta = new Vector2(Scale(42f), Scale(10f));

            var weightText = RuntimeUiFactory.CreateBitmapText(
                "Weight",
                frame.Client,
                _assets.DefaultFont,
                0.32f * TextScale,
                new Color(0.76f, 0.73f, 0.66f),
                BitmapTextAlignment.Left);
            weightText.rectTransform.anchorMin = new Vector2(0f, 0f);
            weightText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            weightText.rectTransform.pivot = new Vector2(0f, 0f);
            weightText.rectTransform.anchoredPosition = new Vector2(Scale(34f), Scale(2f));
            weightText.rectTransform.sizeDelta = new Vector2(Scale(64f), Scale(10f));

            var valueText = RuntimeUiFactory.CreateBitmapText(
                "Value",
                frame.Client,
                _assets.DefaultFont,
                0.32f * TextScale,
                new Color(0.76f, 0.73f, 0.66f),
                BitmapTextAlignment.Right);
            valueText.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            valueText.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            valueText.rectTransform.pivot = new Vector2(1f, 0f);
            valueText.rectTransform.anchoredPosition = new Vector2(Scale(-6f), Scale(2f));
            valueText.rectTransform.sizeDelta = new Vector2(Scale(80f), Scale(10f));

            var row = new InventoryRowView
            {
                InventoryIndex = -1,
                Root = root,
                Frame = frame,
                Button = button,
                Icon = icon,
                Name = name,
                Count = countText,
                Weight = weightText,
                Value = valueText,
            };

            button.onClick.AddListener(() => RequestInventorySelection(row.InventoryIndex));
            return row;
        }

        ContainerRowView CreateContainerRow(Transform parent, int index)
        {
            var root = RuntimeUiFactory.CreateAnchoredRect(
                $"ContainerRow_{index}",
                parent,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                new Vector2(0f, Scale(InventoryRowHeight)));
            root.pivot = new Vector2(0.5f, 1f);
            var frame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                root,
                RuntimeUiFactory.ResolveThinFrame(_assets),
                new Color(0.08f, 0.07f, 0.06f, 0.86f));
            RuntimeUiFactory.Stretch(frame.Root);
            frame.Center.raycastTarget = true;

            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = frame.Center;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            var iconFrameRoot = RuntimeUiFactory.CreateAnchoredRect(
                "IconFrameRoot",
                frame.Client,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(Scale(4f), 0f),
                new Vector2(Scale(24f), Scale(24f)));
            iconFrameRoot.pivot = new Vector2(0f, 0.5f);

            var iconFrame = RuntimeUiFactory.CreateBorderFrame(
                "IconFrame",
                iconFrameRoot,
                RuntimeUiFactory.ResolveThinFrame(_assets),
                new Color(0.02f, 0.02f, 0.02f, 0.88f));
            RuntimeUiFactory.Stretch(iconFrame.Root);

            var icon = RuntimeUiFactory.CreateImage("Icon", iconFrame.Client, Color.white);
            RuntimeUiFactory.SetInset(icon.rectTransform, Scale(2f), Scale(2f), Scale(-2f), Scale(-2f));
            icon.raycastTarget = false;

            var name = RuntimeUiFactory.CreateBitmapText(
                "Name",
                frame.Client,
                _assets.DefaultFont,
                0.38f * TextScale,
                new Color(0.9f, 0.85f, 0.75f),
                BitmapTextAlignment.Left);
            name.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            name.rectTransform.anchorMax = new Vector2(1f, 1f);
            name.rectTransform.pivot = new Vector2(0f, 1f);
            name.rectTransform.anchoredPosition = new Vector2(Scale(34f), Scale(-2f));
            name.rectTransform.sizeDelta = new Vector2(Scale(-124f), Scale(10f));

            var countText = RuntimeUiFactory.CreateBitmapText(
                "Count",
                frame.Client,
                _assets.DefaultFont,
                0.34f * TextScale,
                new Color(0.9f, 0.85f, 0.75f),
                BitmapTextAlignment.Right);
            countText.rectTransform.anchorMin = new Vector2(1f, 0.5f);
            countText.rectTransform.anchorMax = new Vector2(1f, 1f);
            countText.rectTransform.pivot = new Vector2(1f, 1f);
            countText.rectTransform.anchoredPosition = new Vector2(Scale(-6f), Scale(-2f));
            countText.rectTransform.sizeDelta = new Vector2(Scale(42f), Scale(10f));

            var weightText = RuntimeUiFactory.CreateBitmapText(
                "Weight",
                frame.Client,
                _assets.DefaultFont,
                0.32f * TextScale,
                new Color(0.76f, 0.73f, 0.66f),
                BitmapTextAlignment.Left);
            weightText.rectTransform.anchorMin = new Vector2(0f, 0f);
            weightText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            weightText.rectTransform.pivot = new Vector2(0f, 0f);
            weightText.rectTransform.anchoredPosition = new Vector2(Scale(34f), Scale(2f));
            weightText.rectTransform.sizeDelta = new Vector2(Scale(64f), Scale(10f));

            var valueText = RuntimeUiFactory.CreateBitmapText(
                "Value",
                frame.Client,
                _assets.DefaultFont,
                0.32f * TextScale,
                new Color(0.76f, 0.73f, 0.66f),
                BitmapTextAlignment.Right);
            valueText.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            valueText.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            valueText.rectTransform.pivot = new Vector2(1f, 0f);
            valueText.rectTransform.anchoredPosition = new Vector2(Scale(-6f), Scale(2f));
            valueText.rectTransform.sizeDelta = new Vector2(Scale(80f), Scale(10f));

            var row = new ContainerRowView
            {
                ItemIndex = -1,
                Root = root,
                Frame = frame,
                Button = button,
                Icon = icon,
                Name = name,
                Count = countText,
                Weight = weightText,
                Value = valueText,
            };

            button.onClick.AddListener(() => RequestContainerSelection(row.ItemIndex));
            return row;
        }

        void ConfigurePauseNavigation()
        {
            if (_buttons.Count == 0)
                return;

            for (int i = 0; i < _buttons.Count; i++)
            {
                var button = _buttons[i].Button;
                if (button == null)
                    continue;

                var navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = _buttons[(i - 1 + _buttons.Count) % _buttons.Count].Button,
                    selectOnDown = _buttons[(i + 1) % _buttons.Count].Button,
                };
                button.navigation = navigation;
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
                || (_inventoryRoot != null && selected.transform.IsChildOf(_inventoryRoot)))
            {
                _eventSystem.SetSelectedGameObject(null);
            }
        }

        void ApplyInventoryRect(Rect normalizedRect)
        {
            if (_inventoryWindow?.Root == null || _inventoryRoot == null)
                return;

            float rootWidth = Mathf.Max(1f, _inventoryRoot.rect.width);
            float rootHeight = Mathf.Max(1f, _inventoryRoot.rect.height);
            _inventoryWindow.Root.anchoredPosition = new Vector2(normalizedRect.x * rootWidth, -normalizedRect.y * rootHeight);
            _inventoryWindow.Root.sizeDelta = new Vector2(normalizedRect.width * rootWidth, normalizedRect.height * rootHeight);
        }

        void ApplyContainerRect(Rect normalizedRect)
        {
            if (_containerWindow?.Root == null || _inventoryRoot == null)
                return;

            float rootWidth = Mathf.Max(1f, _inventoryRoot.rect.width);
            float rootHeight = Mathf.Max(1f, _inventoryRoot.rect.height);
            _containerWindow.Root.anchoredPosition = new Vector2(normalizedRect.x * rootWidth, -normalizedRect.y * rootHeight);
            _containerWindow.Root.sizeDelta = new Vector2(normalizedRect.width * rootWidth, normalizedRect.height * rootHeight);
        }

        void RequestInventoryWindowRectUpdate()
        {
            if (_inventoryWindow?.Root == null || _inventoryRoot == null)
                return;

            float rootWidth = Mathf.Max(1f, _inventoryRoot.rect.width);
            float rootHeight = Mathf.Max(1f, _inventoryRoot.rect.height);
            var rect = new Rect(
                Mathf.Clamp01(_inventoryWindow.Root.anchoredPosition.x / rootWidth),
                Mathf.Clamp01(-_inventoryWindow.Root.anchoredPosition.y / rootHeight),
                Mathf.Clamp(_inventoryWindow.Root.rect.width / rootWidth, 0.1f, 1f),
                Mathf.Clamp(_inventoryWindow.Root.rect.height / rootHeight, 0.1f, 1f));

            if (!RuntimeShellRequestBridge.TrySetInventoryWindowRect(rect, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed updating inventory window rect: {error}");
        }

        void RequestContainerWindowRectUpdate()
        {
            if (_containerWindow?.Root == null || _inventoryRoot == null)
                return;

            float rootWidth = Mathf.Max(1f, _inventoryRoot.rect.width);
            float rootHeight = Mathf.Max(1f, _inventoryRoot.rect.height);
            var rect = new Rect(
                Mathf.Clamp01(_containerWindow.Root.anchoredPosition.x / rootWidth),
                Mathf.Clamp01(-_containerWindow.Root.anchoredPosition.y / rootHeight),
                Mathf.Clamp(_containerWindow.Root.rect.width / rootWidth, 0.1f, 1f),
                Mathf.Clamp(_containerWindow.Root.rect.height / rootHeight, 0.1f, 1f));

            if (!RuntimeShellRequestBridge.TrySetContainerWindowRect(rect, out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed updating container window rect: {error}");
        }

        void OnInventoryFilterChanged(string value)
        {
            if (_suppressInventoryFieldEvents)
                return;

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

        void RequestModalDismiss()
        {
            if (!RuntimeShellRequestBridge.TryDismissModal(out string error))
                Debug.LogWarning($"[VVardenfell][UI] failed dismissing runtime shell modal: {error}");
        }

        static Font GetHiddenInputFont()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
                return font;

            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        ColorBlock CreateButtonColors(bool available)
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

        Vector2 Scale(Vector2 value)
        {
            return value * UiScale;
        }

        float Scale(float value)
        {
            return value * UiScale;
        }

        void OnDestroy()
        {
            _iconService?.Dispose();
            _iconService = null;
            _assets?.Dispose();
            _assets = null;
        }
    }
}
