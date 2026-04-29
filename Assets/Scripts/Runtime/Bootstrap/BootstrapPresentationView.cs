using System;
using System.Collections.Generic;
using System.IO;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.Video;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Audio;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.UI.Assets;
using VVardenfell.Runtime.UI.Framework;
using VVardenfell.Runtime.UI.Shell;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.Bootstrap
{
    public sealed partial class BootstrapPresentationView : MonoBehaviour
    {
        enum PresentationPhase
        {
            IntroCompany,
            Loading,
            IntroLogo,
            Menu,
            Dismissed,
        }

        enum MoviePlaybackState
        {
            Idle,
            Preparing,
            Prepared,
            Playing,
        }

        enum BootstrapMenuActionId
        {
            Continue,
            NewGame,
            LoadGame,
            Options,
            Credits,
            ExitGame,
        }

        readonly struct BootstrapMenuDefinition
        {
            public BootstrapMenuDefinition(
                BootstrapMenuActionId action,
                string normalKey,
                string highlightedKey,
                string pressedKey)
            {
                Action = action;
                NormalKey = normalKey;
                HighlightedKey = highlightedKey;
                PressedKey = pressedKey;
            }

            public BootstrapMenuActionId Action { get; }
            public string NormalKey { get; }
            public string HighlightedKey { get; }
            public string PressedKey { get; }
        }

        sealed class BootstrapMenuButtonView
        {
            public BootstrapMenuActionId Action;
            public RectTransform Rect;
            public Image Image;
            public Button Button;
        }

        readonly struct BootstrapMenuActionState
        {
            public BootstrapMenuActionState(
                BootstrapMenuActionId action,
                bool visible,
                bool enabled,
                string unavailableTitle,
                string unavailableBody)
            {
                Action = action;
                Visible = visible;
                Enabled = enabled;
                UnavailableTitle = unavailableTitle ?? string.Empty;
                UnavailableBody = unavailableBody ?? string.Empty;
            }

            public BootstrapMenuActionId Action { get; }
            public bool Visible { get; }
            public bool Enabled { get; }
            public string UnavailableTitle { get; }
            public string UnavailableBody { get; }
        }

        static readonly BootstrapMenuDefinition[] k_MenuDefinitions =
        {
            new(BootstrapMenuActionId.Continue, UiBootstrapAssetKeys.MenuReturnNormal, UiBootstrapAssetKeys.MenuReturnHighlight, UiBootstrapAssetKeys.MenuReturnPressed),
            new(BootstrapMenuActionId.NewGame, UiBootstrapAssetKeys.MenuNewGameNormal, UiBootstrapAssetKeys.MenuNewGameHighlight, UiBootstrapAssetKeys.MenuNewGamePressed),
            new(BootstrapMenuActionId.LoadGame, UiBootstrapAssetKeys.MenuLoadGameNormal, UiBootstrapAssetKeys.MenuLoadGameHighlight, UiBootstrapAssetKeys.MenuLoadGamePressed),
            new(BootstrapMenuActionId.Options, UiBootstrapAssetKeys.MenuOptionsNormal, UiBootstrapAssetKeys.MenuOptionsHighlight, UiBootstrapAssetKeys.MenuOptionsPressed),
            new(BootstrapMenuActionId.Credits, UiBootstrapAssetKeys.MenuCreditsNormal, UiBootstrapAssetKeys.MenuCreditsHighlight, UiBootstrapAssetKeys.MenuCreditsPressed),
            new(BootstrapMenuActionId.ExitGame, UiBootstrapAssetKeys.MenuExitGameNormal, UiBootstrapAssetKeys.MenuExitGameHighlight, UiBootstrapAssetKeys.MenuExitGamePressed),
        };

        const float CompanyIntroSeconds = 1.8f;
        const float LogoIntroSeconds = 2.3f;
        const float MinimumLoadingSeconds = 0.75f;
        const float MoviePrepareWarningSeconds = 5f;
        const float LoadingDialogMinWidth = 300f;
        const float LoadingDialogHeight = 48f;
        const float LoadingDialogBottomMargin = 8f;
        const float LoadingBarHeight = 6f;
        const float MenuBottomPadding = 24f;
        // Stretch the background image to fill the whole canvas during Loading + Menu
        // (in addition to the intro phases which always stretch). Letterboxing the
        // splash during Loading only to full-stretch it for the intro logo caused a
        // jarring aspect-ratio swap between phases; keeping it stretched throughout
        // gives the player a stable visual frame from pre-bake through the main menu.
        const bool StretchMenuBackground = true;
        static readonly ProfilerMarker k_MoviePrepare = new("VV.Runtime.BootstrapMovie.Prepare");
        static readonly ProfilerMarker k_MovieStart = new("VV.Runtime.BootstrapMovie.Start");
        static readonly ProfilerMarker k_MovieStop = new("VV.Runtime.BootstrapMovie.Stop");

        readonly List<BootstrapMenuButtonView> _menuButtons = new();

        RuntimeUiTheme _theme;
        RuntimeLoadProgress _progress;
        string _installPath;
        Action _onLoadingPhaseReady;
        PresentationPhase _phase;
        float _phaseStartTime;
        bool _bootstrapComplete;
        bool _dismissed;
        bool _runtimeLoading;
        bool _activeMovieOwnsPhase;
        bool _phaseWaitingForMovieCompletion;
        bool _loadingPhaseSignaled;

        Canvas _canvas;
        RectTransform _rootRect;
        Image _backgroundMatte;
        RawImage _backgroundImage;
        RawImage _videoImage;
        AudioSource _videoAudio;
        EventSystem _eventSystem;

        RectTransform _introFallbackGroup;
        BitmapTextGraphic _introFallbackTitle;
        BitmapTextGraphic _introFallbackSubtitle;

        RectTransform _loadingRoot;
        RectTransform _loadingDialogRect;
        BorderFrameView _loadingDialogFrame;
        BitmapTextGraphic _loadingText;
        RectTransform _loadingBarRect;
        BorderFrameView _loadingBarFrame;
        RectTransform _loadingBarFillRect;
        Image _loadingBarFill;

        RectTransform _menuRoot;
        RectTransform _menuButtonBox;
        BitmapTextGraphic _versionText;
        RectTransform _menuDialogRoot;
        Image _menuDialogBlocker;
        RectTransform _menuDialogRect;
        BorderFrameView _menuDialogFrame;
        BitmapTextGraphic _menuDialogTitle;
        BitmapTextGraphic _menuDialogBody;
        BitmapTextGraphic _menuDialogFooter;
        int _menuDialogOpenedFrame = -1;
        BootstrapMenuActionId _lastSelectedMenuAction;
        SaveLoadBrowserView _menuSaveLoadBrowser;
        OptionsWindowView _menuOptionsView;
        VVardenfell.Core.Config.MorrowindConfig _config;
        bool _menuOptionsVisible;
        bool _menuSaveLoadVisible;
        string _menuSaveLoadSelectedSlotId = string.Empty;
        string _menuSaveLoadStatus = string.Empty;
        string _menuSaveLoadConfirmation = string.Empty;
        SaveLoadBrowserPendingAction _menuSaveLoadConfirmAction;

        VideoPlayer _videoPlayer;
        RenderTexture _videoTexture;
        UiImageAsset _activeBackgroundImage;
        UiImageAsset _activeLoadingSplash;
        UiMovieRuntimeInfo _activeMovie;
        MoviePlaybackState _movieState;
        string _activeMovieSlot;
        string _activeMoviePath;
        bool _activeMovieLoop;
        float _moviePrepareStartTime;
        bool _moviePrepareWarningLogged;
        bool _movieStartQueued;
        Vector2 _lastCanvasSize;

        public bool IsDismissed => _dismissed;

        public void Initialize(RuntimeUiTheme theme, RuntimeLoadProgress progress, string installPath, Action onLoadingPhaseReady = null)
            => Initialize(theme, progress, installPath, onLoadingPhaseReady, startAtMenu: false);

        public void InitializeMenuOverlay(RuntimeUiTheme theme, RuntimeLoadProgress progress, string installPath)
            => Initialize(theme, progress, installPath, null, startAtMenu: true);

        void Initialize(RuntimeUiTheme theme, RuntimeLoadProgress progress, string installPath, Action onLoadingPhaseReady, bool startAtMenu)
        {
            _theme = theme;
            _progress = progress;
            _installPath = installPath;
            _onLoadingPhaseReady = onLoadingPhaseReady;
            _loadingPhaseSignaled = false;
            _bootstrapComplete = startAtMenu;
            _dismissed = false;
            BootstrapPresentationGate.BlocksGameplayInput = true;
            BuildCanvas();
            SwitchPhase(startAtMenu ? PresentationPhase.Menu : PresentationPhase.IntroCompany);
        }

        public void NotifyBootstrapComplete()
        {
            _bootstrapComplete = true;
        }

        private void Update()
        {
            if (_theme == null)
                return;

            UpdateMoviePlaybackState();
            RefreshScreenDependentLayout();
            HandleIntroSkipInput();
            HandleMenuSaveLoadInput();
            HandleMenuOptionsInput();
            HandleMenuDialogInput();

            switch (_phase)
            {
                case PresentationPhase.IntroCompany:
                    if (ShouldAdvanceIntroPhase(CompanyIntroSeconds))
                        SwitchPhase(PresentationPhase.Loading);
                    break;
                case PresentationPhase.Loading:
                    UpdateLoadingVisuals();
                    if (_runtimeLoading)
                    {
                        UpdateRuntimeLoading();
                    }
                    else if (_bootstrapComplete && Time.unscaledTime - _phaseStartTime >= MinimumLoadingSeconds)
                    {
                        SwitchPhase(PresentationPhase.IntroLogo);
                    }
                    break;
                case PresentationPhase.IntroLogo:
                    if (ShouldAdvanceIntroPhase(LogoIntroSeconds))
                        SwitchPhase(PresentationPhase.Menu);
                    break;
            }

            if (_phase == PresentationPhase.Menu)
                SyncMenuSaveLoadBrowser();
        }

        private void OnDestroy()
        {
            BootstrapPresentationGate.BlocksGameplayInput = false;

            if (_videoPlayer != null)
            {
                _videoPlayer.Stop();
                _videoPlayer.prepareCompleted -= OnVideoPrepared;
                _videoPlayer.errorReceived -= OnVideoError;
                _videoPlayer.loopPointReached -= OnVideoLoopPointReached;
            }

            if (_videoAudio != null)
                _videoAudio.Stop();

            if (_videoTexture != null)
            {
                _videoTexture.Release();
                Destroy(_videoTexture);
            }

            _theme?.Dispose();
        }

        void BeginRuntimeLoading(string label)
        {
            CloseMenuDialog();
            CloseMenuSaveLoadBrowser();
            CloseMenuOptions();
            _runtimeLoading = true;
            _progress?.Reset();
            _progress?.BeginStage("Loading", label, 1);
            _progress?.Report(label, 0, 1);
            SwitchPhase(PresentationPhase.Loading);
        }

        void UpdateRuntimeLoading()
        {
            if (!IsRuntimeActive())
                return;

            _progress?.Complete("Loading", "Loading complete");
            if (Time.unscaledTime - _phaseStartTime >= MinimumLoadingSeconds)
                Dismiss();
        }

        static bool IsRuntimeActive()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;

            var entityManager = world.EntityManager;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MorrowindRuntimeActive>());
            return !query.IsEmptyIgnoreFilter;
        }

        void BuildCanvas()
        {
            var canvasView = RuntimeUiFactory.CreateCanvasRoot(gameObject, "Root", short.MaxValue);
            _canvas = canvasView.Canvas;
            _eventSystem = canvasView.EventSystem;
            _rootRect = canvasView.Root;

            _backgroundMatte = RuntimeUiFactory.CreateImage("BackgroundMatte", _rootRect, Color.black);
            RuntimeUiFactory.Stretch(_backgroundMatte.rectTransform);
            _backgroundMatte.raycastTarget = false;

            _backgroundImage = RuntimeUiFactory.CreateRawImage("BackgroundImage", _rootRect, Color.white);
            _backgroundImage.raycastTarget = false;

            _videoImage = RuntimeUiFactory.CreateRawImage("VideoImage", _rootRect, Color.white);
            _videoImage.enabled = false;
            _videoImage.raycastTarget = false;

            _videoAudio = gameObject.AddComponent<AudioSource>();
            _videoAudio.playOnAwake = false;
            _videoAudio.loop = false;
            _videoAudio.spatialBlend = 0f;

            BuildIntroFallback();
            BuildLoadingView();
            BuildMenuView();
            BuildVideoPlayer();

            _lastCanvasSize = _rootRect.rect.size;
        }

        void SwitchPhase(PresentationPhase phase)
        {
            _phase = phase;
            _phaseStartTime = Time.unscaledTime;
            _activeMovieOwnsPhase = false;
            _phaseWaitingForMovieCompletion = false;
            _introFallbackGroup.gameObject.SetActive(false);
            _loadingRoot.gameObject.SetActive(false);
            _menuRoot.gameObject.SetActive(false);
            if (_menuDialogRoot != null)
                _menuDialogRoot.gameObject.SetActive(false);
            _menuDialogOpenedFrame = -1;
            CloseMenuSaveLoadBrowser();
            StopMovie();
            BootstrapPresentationAudioState.SetPhase(ToAudioPhase(phase));

            switch (phase)
            {
                case PresentationPhase.IntroCompany:
                    _backgroundImage.texture = null;
                    _backgroundImage.color = Color.clear;
                    RuntimeUiFactory.Stretch(_backgroundImage.rectTransform);
                    _backgroundMatte.color = Color.black;
                    ConfigureIntroFallback("Bethesda Softworks", "A new telling begins", ScaleText(1.05f), ScaleText(0.8f), show: true);
                    BeginIntroMoviePhase("Company Logo");
                    break;

                case PresentationPhase.Loading:
                    _backgroundMatte.color = Color.black;
                    _loadingRoot.gameObject.SetActive(true);
                    _activeLoadingSplash = PickSplashImage();
                    SetBackgroundImage(_activeLoadingSplash ?? _theme.MenuBackground, stretchToFill: StretchMenuBackground);
                    UpdateLoadingVisuals();
                    SignalLoadingPhaseReady();
                    break;

                case PresentationPhase.IntroLogo:
                    _backgroundMatte.color = Color.black;
                    ConfigureIntroFallback("MORROWIND", "The Elder Scrolls III", ScaleText(1.7f), ScaleText(0.82f), show: true);
                    SetBackgroundImage(_theme.MenuBackground ?? _activeLoadingSplash ?? PickSplashImage(), stretchToFill: true);
                    BeginIntroMoviePhase("Morrowind Logo");
                    break;

                case PresentationPhase.Menu:
                    _backgroundMatte.color = Color.black;
                    _menuRoot.gameObject.SetActive(true);
                    SetBackgroundImage(_theme.MenuBackground ?? _activeLoadingSplash ?? PickSplashImage(), stretchToFill: StretchMenuBackground);
                    RefreshMenuButtons();
                    SyncMenuSaveLoadBrowser();
                    break;
            }
        }











    }
}
