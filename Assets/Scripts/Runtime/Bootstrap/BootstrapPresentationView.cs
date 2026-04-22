using System;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.UI;

namespace VVardenfell.Runtime.Bootstrap
{
    public sealed class BootstrapPresentationView : MonoBehaviour
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

        sealed class BorderFrameView
        {
            public RectTransform Root;
            public RectTransform Client;
            public Image Center;
            public Image Top;
            public Image Bottom;
            public Image Left;
            public Image Right;
            public Image TopLeft;
            public Image TopRight;
            public Image BottomLeft;
            public Image BottomRight;
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
        const bool StretchMenuBackground = false;
        const float PresentationTextScaleMultiplier = 1.5f;
        const float LoadingScaleMultiplier = 2f;
        const float MenuVisualScaleMultiplier = 2f;
        const string MainMenuMusicRelativePath = @"Data Files\Music\Special\morrowind title.mp3";

        static readonly ProfilerMarker k_MoviePrepare = new("VV.Runtime.BootstrapMovie.Prepare");
        static readonly ProfilerMarker k_MovieStart = new("VV.Runtime.BootstrapMovie.Start");
        static readonly ProfilerMarker k_MovieStop = new("VV.Runtime.BootstrapMovie.Stop");

        readonly List<BootstrapMenuButtonView> _menuButtons = new();

        UiRuntimeAssets _assets;
        RuntimeLoadProgress _progress;
        string _installPath;
        Action _onLoadingPhaseReady;
        PresentationPhase _phase;
        float _phaseStartTime;
        bool _bootstrapComplete;
        bool _dismissed;
        bool _activeMovieOwnsPhase;
        bool _phaseWaitingForMovieCompletion;
        bool _loadingPhaseSignaled;

        Canvas _canvas;
        RectTransform _rootRect;
        Image _backgroundMatte;
        RawImage _backgroundImage;
        RawImage _videoImage;
        AudioSource _videoAudio;
        AudioSource _menuMusicAudio;
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
        AudioClip _menuMusicClip;
        Coroutine _menuMusicLoadRoutine;
        string _menuMusicPath;
        bool _menuMusicWarningLogged;

        public bool IsDismissed => _dismissed;

        public void Initialize(UiRuntimeAssets assets, RuntimeLoadProgress progress, string installPath, Action onLoadingPhaseReady = null)
        {
            _assets = assets;
            _progress = progress;
            _installPath = installPath;
            _onLoadingPhaseReady = onLoadingPhaseReady;
            _loadingPhaseSignaled = false;
            BootstrapPresentationGate.BlocksGameplayInput = true;
            BuildCanvas();
            SwitchPhase(PresentationPhase.IntroCompany);
        }

        public void NotifyBootstrapComplete()
        {
            _bootstrapComplete = true;
        }

        private void Update()
        {
            if (_assets == null)
                return;

            UpdateMoviePlaybackState();
            RefreshScreenDependentLayout();
            HandleIntroSkipInput();

            switch (_phase)
            {
                case PresentationPhase.IntroCompany:
                    if (ShouldAdvanceIntroPhase(CompanyIntroSeconds))
                        SwitchPhase(PresentationPhase.Loading);
                    break;
                case PresentationPhase.Loading:
                    UpdateLoadingVisuals();
                    if (_bootstrapComplete && Time.unscaledTime - _phaseStartTime >= MinimumLoadingSeconds)
                        SwitchPhase(PresentationPhase.IntroLogo);
                    break;
                case PresentationPhase.IntroLogo:
                    if (ShouldAdvanceIntroPhase(LogoIntroSeconds))
                        SwitchPhase(PresentationPhase.Menu);
                    break;
            }
        }

        void HandleIntroSkipInput()
        {
            if (_phase != PresentationPhase.IntroCompany && _phase != PresentationPhase.IntroLogo)
                return;

            bool escapePressed = Keyboard.current?.escapeKey.wasPressedThisFrame ?? false;
            bool mousePressed = Mouse.current?.leftButton.wasPressedThisFrame ?? false;
            if (!escapePressed && !mousePressed)
                return;

            AdvanceFromCurrentIntroPhase();
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

            StopMenuMusic();
            if (_menuMusicClip != null)
                Destroy(_menuMusicClip);

            if (_videoTexture != null)
            {
                _videoTexture.Release();
                Destroy(_videoTexture);
            }

            _assets?.Dispose();
        }

        void BuildCanvas()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = short.MaxValue;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();

            _rootRect = CreateStretchRect("Root", transform);

            _backgroundMatte = CreateImage("BackgroundMatte", _rootRect, Color.black);
            Stretch(_backgroundMatte.rectTransform);
            _backgroundMatte.raycastTarget = false;

            _backgroundImage = CreateRawImage("BackgroundImage", _rootRect, Color.white);
            _backgroundImage.raycastTarget = false;

            _videoImage = CreateRawImage("VideoImage", _rootRect, Color.white);
            _videoImage.enabled = false;
            _videoImage.raycastTarget = false;

            _videoAudio = gameObject.AddComponent<AudioSource>();
            _videoAudio.playOnAwake = false;
            _videoAudio.loop = false;
            _videoAudio.spatialBlend = 0f;

            _menuMusicAudio = gameObject.AddComponent<AudioSource>();
            _menuMusicAudio.playOnAwake = false;
            _menuMusicAudio.loop = true;
            _menuMusicAudio.spatialBlend = 0f;
            _menuMusicAudio.ignoreListenerPause = true;

            BuildIntroFallback();
            BuildLoadingView();
            BuildMenuView();
            BuildVideoPlayer();

            _lastCanvasSize = _rootRect.rect.size;
        }

        void BuildIntroFallback()
        {
            _introFallbackGroup = CreateAnchorRect(
                "IntroFallback",
                _rootRect,
                new Vector2(0.08f, 0.56f),
                new Vector2(0.92f, 0.86f),
                Vector2.zero,
                Vector2.zero,
                Vector2.zero);
            _introFallbackTitle = CreateBitmapText(
                "IntroFallbackTitle",
                _introFallbackGroup,
                _assets.TitleFont ?? _assets.DefaultFont,
                ScaleText(1.6f),
                new Color(0.94f, 0.82f, 0.53f),
                BitmapTextAlignment.Center);
            Stretch(_introFallbackTitle.rectTransform);

            _introFallbackSubtitle = CreateBitmapText(
                "IntroFallbackSubtitle",
                _introFallbackGroup,
                _assets.DefaultFont,
                ScaleText(0.82f),
                new Color(0.92f, 0.87f, 0.74f),
                BitmapTextAlignment.Center);
            SetInset(_introFallbackSubtitle.rectTransform, 0f, -108f, 0f, 0f);
        }

        void BuildLoadingView()
        {
            _loadingRoot = CreateStretchRect("LoadingRoot", _rootRect);
            _loadingRoot.gameObject.SetActive(false);

            _loadingDialogRect = CreateAnchoredRect(
                "LoadingDialog",
                _loadingRoot,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, ScaleLoadingLayout(LoadingDialogBottomMargin)),
                new Vector2(ScaleLoadingLayout(LoadingDialogMinWidth), ScaleLoadingLayout(LoadingDialogHeight)));
            _loadingDialogFrame = CreateBorderFrame(
                "LoadingDialogFrame",
                _loadingDialogRect,
                ResolveThickFrame(),
                new Color(0f, 0f, 0f, 0.92f));
            Stretch(_loadingDialogFrame.Root);

            _loadingText = CreateBitmapText(
                "LoadingText",
                _loadingDialogRect,
                _assets.DefaultFont,
                ScaleLoadingText(0.72f),
                new Color(0.93f, 0.88f, 0.75f),
                BitmapTextAlignment.Center);
            _loadingText.rectTransform.anchorMin = new Vector2(0f, 1f);
            _loadingText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _loadingText.rectTransform.pivot = new Vector2(0.5f, 1f);
            _loadingText.rectTransform.anchoredPosition = new Vector2(0f, -ScaleLoadingLayout(5f));
            _loadingText.rectTransform.sizeDelta = new Vector2(-ScaleLoadingLayout(32f), ScaleLoadingLayout(18f));

            _loadingBarRect = CreateAnchoredRect(
                "LoadingBar",
                _loadingDialogRect,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, ScaleLoadingLayout(16f)),
                new Vector2(ScaleLoadingLayout(LoadingDialogMinWidth - 32f), ScaleLoadingLayout(LoadingBarHeight)));
            _loadingBarFrame = CreateBorderFrame(
                "LoadingBarFrame",
                _loadingBarRect,
                ResolveThinFrame(),
                Color.clear);
            Stretch(_loadingBarFrame.Root);

            _loadingBarFillRect = CreateAnchoredRect(
                "LoadingBarFillRect",
                _loadingBarRect,
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                Vector2.zero,
                Vector2.zero);
            _loadingBarFillRect.pivot = new Vector2(0f, 0.5f);
            _loadingBarFillRect.SetAsFirstSibling();

            _loadingBarFill = CreateImage("LoadingBarFill", _loadingBarFillRect, new Color(0f, 0.815f, 0.82f, 1f));
            _loadingBarFill.raycastTarget = false;
            var fillSprite = _assets.GetBootstrapImage(UiBootstrapAssetKeys.LoadingBarGray)?.Sprite;
            _loadingBarFill.sprite = fillSprite;
            Stretch(_loadingBarFill.rectTransform);
        }

        void BuildMenuView()
        {
            _menuRoot = CreateStretchRect("MenuRoot", _rootRect);
            _menuRoot.gameObject.SetActive(false);

            _menuButtonBox = CreateAnchoredRect(
                "MenuButtonBox",
                _menuRoot,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, ScaleMenuLayout(MenuBottomPadding)),
                new Vector2(1f, 1f));

            _versionText = CreateBitmapText(
                "VersionText",
                _menuRoot,
                _assets.DefaultFont,
                ScaleMenuText(0.6f),
                new Color(0.93f, 0.88f, 0.75f),
                BitmapTextAlignment.Right);
            _versionText.Text = BuildVersionText();
            _versionText.rectTransform.anchorMin = new Vector2(1f, 0f);
            _versionText.rectTransform.anchorMax = new Vector2(1f, 0f);
            _versionText.rectTransform.pivot = new Vector2(1f, 0f);
            _versionText.rectTransform.anchoredPosition = new Vector2(-ScaleMenuLayout(20f), ScaleMenuLayout(16f));
            _versionText.rectTransform.sizeDelta = new Vector2(ScaleMenuLayout(320f), ScaleMenuLayout(42f));
        }

        void BuildVideoPlayer()
        {
            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.isLooping = false;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.waitForFirstFrame = true;
            _videoPlayer.skipOnDrop = false;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            _videoPlayer.prepareCompleted += OnVideoPrepared;
            _videoPlayer.errorReceived += OnVideoError;
            _videoPlayer.loopPointReached += OnVideoLoopPointReached;
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
            StopMovie();
            if (phase != PresentationPhase.IntroLogo && phase != PresentationPhase.Menu)
                StopMenuMusic();

            switch (phase)
            {
                case PresentationPhase.IntroCompany:
                    _backgroundImage.texture = null;
                    _backgroundImage.color = Color.clear;
                    Stretch(_backgroundImage.rectTransform);
                    _backgroundMatte.color = Color.black;
                    ConfigureIntroFallback("Bethesda Softworks", "A new telling begins", ScaleText(1.05f), ScaleText(0.8f), show: true);
                    BeginIntroMoviePhase("Company Logo");
                    break;

                case PresentationPhase.Loading:
                    _backgroundMatte.color = Color.black;
                    _loadingRoot.gameObject.SetActive(true);
                    _activeLoadingSplash = PickSplashImage();
                    SetBackgroundImage(_activeLoadingSplash ?? _assets.MenuBackground, stretchToFill: StretchMenuBackground);
                    PreloadMenuMusic();
                    UpdateLoadingVisuals();
                    SignalLoadingPhaseReady();
                    break;

                case PresentationPhase.IntroLogo:
                    _backgroundMatte.color = Color.black;
                    ConfigureIntroFallback("MORROWIND", "The Elder Scrolls III", ScaleText(1.7f), ScaleText(0.82f), show: true);
                    SetBackgroundImage(_assets.MenuBackground ?? _activeLoadingSplash ?? PickSplashImage(), stretchToFill: true);
                    StartMenuMusic();
                    BeginIntroMoviePhase("Morrowind Logo");
                    break;

                case PresentationPhase.Menu:
                    _backgroundMatte.color = Color.black;
                    _menuRoot.gameObject.SetActive(true);
                    SetBackgroundImage(_assets.MenuBackground ?? _activeLoadingSplash ?? PickSplashImage(), stretchToFill: StretchMenuBackground);
                    RefreshMenuButtons();
                    StartMenuMusic();
                    break;
            }
        }

        void UpdateLoadingVisuals()
        {
            if (_loadingText == null || _progress == null)
                return;

            string label = BuildLoadingLabel();
            _loadingText.Text = label;

            float width = Mathf.Max(
                ScaleLoadingLayout(LoadingDialogMinWidth),
                Mathf.Ceil(MeasureLineWidth(_assets.DefaultFont, label, _loadingText.FontScale) + ScaleLoadingLayout(40f)));
            _loadingDialogRect.sizeDelta = new Vector2(width, ScaleLoadingLayout(LoadingDialogHeight));
            _loadingBarRect.sizeDelta = new Vector2(width - ScaleLoadingLayout(32f), ScaleLoadingLayout(LoadingBarHeight));
            UpdateLoadingBarFill(Mathf.Clamp01(_progress.Fraction));
        }

        void RefreshMenuButtons()
        {
            for (int i = 0; i < _menuButtons.Count; i++)
            {
                if (_menuButtons[i].Rect != null)
                    Destroy(_menuButtons[i].Rect.gameObject);
            }

            _menuButtons.Clear();

            var visibleDefinitions = new List<BootstrapMenuDefinition>(k_MenuDefinitions.Length);
            for (int i = 0; i < k_MenuDefinitions.Length; i++)
            {
                if (IsMenuActionVisible(k_MenuDefinitions[i].Action))
                    visibleDefinitions.Add(k_MenuDefinitions[i]);
            }

            if (visibleDefinitions.Count == 0)
            {
                _menuButtonBox.sizeDelta = Vector2.zero;
                return;
            }

            float maxWidth = 0f;
            float totalHeight = 0f;
            var sizes = new Vector2[visibleDefinitions.Count];

            for (int i = 0; i < visibleDefinitions.Count; i++)
            {
                var sprite = _assets.GetBootstrapImage(visibleDefinitions[i].NormalKey)?.Sprite;
                if (sprite == null)
                    continue;

                float requestedWidth = sprite.rect.width;
                float requestedHeight = sprite.rect.height;
                float scale = requestedHeight / 64f;
                if (scale <= 0f)
                    scale = 1f;

                float width = (requestedWidth / scale) * MenuVisualScaleMultiplier;
                float height = Mathf.Max(1f, (requestedHeight / scale - 16f) * MenuVisualScaleMultiplier);
                sizes[i] = new Vector2(width, height);
                maxWidth = Mathf.Max(maxWidth, width);
                totalHeight += height;
            }

            _menuButtonBox.sizeDelta = new Vector2(maxWidth, totalHeight);

            float curY = 0f;
            for (int i = 0; i < visibleDefinitions.Count; i++)
            {
                var definition = visibleDefinitions[i];
                var normal = _assets.GetBootstrapImage(definition.NormalKey)?.Sprite;
                var highlighted = _assets.GetBootstrapImage(definition.HighlightedKey)?.Sprite ?? normal;
                var pressed = _assets.GetBootstrapImage(definition.PressedKey)?.Sprite ?? highlighted ?? normal;
                if (normal == null)
                    continue;

                var rect = CreateAnchoredRect(
                    $"MenuButton_{definition.Action}",
                    _menuButtonBox,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2((maxWidth - sizes[i].x) * 0.5f, -curY),
                    sizes[i]);
                var image = CreateImage($"Image_{definition.Action}", rect, Color.white);
                Stretch(image.rectTransform);
                image.sprite = normal;
                image.type = Image.Type.Simple;
                image.preserveAspect = true;
                image.rectTransform.localScale = new Vector3(1f, -1f, 1f);

                var button = rect.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.SpriteSwap;
                button.targetGraphic = image;
                button.spriteState = new SpriteState
                {
                    highlightedSprite = highlighted,
                    pressedSprite = pressed,
                    selectedSprite = highlighted,
                    disabledSprite = normal,
                };

                var navigation = new Navigation { mode = Navigation.Mode.None };
                button.navigation = navigation;

                var action = definition.Action;
                button.onClick.AddListener(() => OnMenuButtonPressed(action));

                _menuButtons.Add(new BootstrapMenuButtonView
                {
                    Action = definition.Action,
                    Rect = rect,
                    Image = image,
                    Button = button,
                });

                curY += sizes[i].y;
            }
        }

        void OnMenuButtonPressed(BootstrapMenuActionId action)
        {
            switch (action)
            {
                case BootstrapMenuActionId.Continue:
                    if (GameInitializationRequestBridge.TryRequestContinue(out var continueError))
                        Dismiss();
                    else
                        Debug.LogWarning($"[VVardenfell] continue request failed: {continueError}");
                    break;

                case BootstrapMenuActionId.NewGame:
                    if (GameInitializationRequestBridge.TryRequestNewGame(out var newGameError))
                        Dismiss();
                    else
                        Debug.LogWarning($"[VVardenfell] new game request failed: {newGameError}");
                    break;

                case BootstrapMenuActionId.LoadGame:
                    Debug.Log("[VVardenfell] Load Game is not wired into runtime bootstrap yet.");
                    break;

                case BootstrapMenuActionId.Options:
                    Debug.Log("[VVardenfell] Options is presentation-only in this pass.");
                    break;

                case BootstrapMenuActionId.Credits:
                    Debug.Log("[VVardenfell] Credits is presentation-only in this pass.");
                    break;

                case BootstrapMenuActionId.ExitGame:
                    Debug.Log("[VVardenfell] Exit Game requested from bootstrap menu.");
                    Application.Quit();
                    break;
            }
        }

        void Dismiss()
        {
            _dismissed = true;
            _phase = PresentationPhase.Dismissed;
            BootstrapPresentationGate.BlocksGameplayInput = false;
            gameObject.SetActive(false);
        }

        bool IsMenuActionVisible(BootstrapMenuActionId action)
        {
            return action switch
            {
                BootstrapMenuActionId.Continue => GameInitializationRequestBridge.CanRequestContinue(out _),
                BootstrapMenuActionId.NewGame => GameInitializationRequestBridge.CanRequestNewGame(out _),
                BootstrapMenuActionId.LoadGame => GameInitializationRequestBridge.CanRequestLoadGame(out _),
                BootstrapMenuActionId.Options => true,
                BootstrapMenuActionId.Credits => true,
                BootstrapMenuActionId.ExitGame => true,
                _ => false,
            };
        }

        void EnsureEventSystem()
        {
            _eventSystem = FindAnyObjectByType<EventSystem>();
            if (_eventSystem != null)
                return;

            var go = new GameObject("VVardenfell.EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(go);
            _eventSystem = go.GetComponent<EventSystem>();
        }

        void SetBackgroundImage(UiImageAsset image, bool stretchToFill)
        {
            _activeBackgroundImage = image;
            if (image?.Texture == null)
            {
                _backgroundImage.texture = null;
                _backgroundImage.color = Color.clear;
                Stretch(_backgroundImage.rectTransform);
                return;
            }

            _backgroundImage.texture = image.Texture;
            _backgroundImage.color = Color.white;
            _backgroundImage.uvRect = new Rect(0f, 1f, 1f, -1f);
            ApplyTextureLayout(_backgroundImage.rectTransform, image.Texture.width, image.Texture.height, stretchToFill);
        }

        UiImageAsset PickSplashImage()
        {
            if (_assets.SplashImages.Count == 0)
                return null;

            int index = UnityEngine.Random.Range(0, _assets.SplashImages.Count);
            return _assets.SplashImages[index];
        }

        bool TryPlayMovie(string slot, bool loop)
        {
            var movie = _assets.GetMovie(slot);
            if (movie == null || !movie.HasPlayableClip)
            {
                ApplyMovieFallback(movie);
                StopMovie();
                return false;
            }

            if (IsSameMovieRequest(movie, loop))
                return true;

            if (_movieState != MoviePlaybackState.Idle)
            {
                Debug.LogWarning(
                    $"[VVardenfell] restarting bootstrap movie playback with slot '{slot}' while '{_activeMovieSlot ?? "<none>"}' was still {_movieState}.");
            }

            EnsureVideoTexture();
            ResetMoviePlaybackObjects();

            _activeMovie = movie;
            _activeMovieSlot = slot;
            _activeMoviePath = movie.CachedClipPath;
            _activeMovieLoop = loop;
            _activeMovieOwnsPhase = !loop && (_phase == PresentationPhase.IntroCompany || _phase == PresentationPhase.IntroLogo);
            _movieState = MoviePlaybackState.Preparing;
            _moviePrepareStartTime = Time.unscaledTime;
            _moviePrepareWarningLogged = false;
            _movieStartQueued = false;
            _videoImage.enabled = false;
            _videoImage.texture = null;

            using (k_MoviePrepare.Auto())
            {
                _videoPlayer.source = VideoSource.Url;
                _videoPlayer.url = movie.CachedClipPath;
                _videoPlayer.isLooping = loop;
                _videoPlayer.targetTexture = _videoTexture;
                ConfigureMovieAudio(movie.HasAudio);
                _videoPlayer.Prepare();
            }

            return true;
        }

        void StopMovie()
        {
            using (k_MovieStop.Auto())
            {
                ResetMoviePlaybackObjects();
            }

            _activeMovie = null;
            _activeMovieSlot = null;
            _activeMoviePath = null;
            _activeMovieLoop = false;
            _activeMovieOwnsPhase = false;
            _movieState = MoviePlaybackState.Idle;
            _moviePrepareWarningLogged = false;
            _movieStartQueued = false;
        }

        void OnVideoPrepared(VideoPlayer source)
        {
            if (source == null || source != _videoPlayer)
                return;

            if (_movieState != MoviePlaybackState.Preparing
                || _videoTexture == null
                || _activeMovie == null
                || !PathMatchesActiveMovie(source.url))
            {
                Debug.LogWarning(
                    $"[VVardenfell] stale bootstrap movie prepare callback ignored for slot '{_activeMovieSlot ?? "<none>"}' in state {_movieState}.");
                return;
            }

            _videoImage.texture = _videoTexture;
            _videoImage.enabled = true;
            source.targetTexture = _videoTexture;
            ConfigureMovieAudio(_activeMovie.HasAudio);
            UpdateVideoLayout();
            ConfigureIntroFallback(
                _introFallbackTitle.Text,
                _introFallbackSubtitle.Text,
                _introFallbackTitle.FontScale,
                _introFallbackSubtitle.FontScale,
                show: !_activeMovieOwnsPhase);
            _movieState = MoviePlaybackState.Prepared;
            _movieStartQueued = true;
        }

        void OnVideoError(VideoPlayer source, string message)
        {
            Debug.LogWarning(
                $"[VVardenfell] bootstrap movie error for slot '{_activeMovieSlot ?? _activeMovie?.Slot ?? "<none>"}' in state {_movieState}: {message}");
            ApplyMovieFallback(_activeMovie);
            StopMovie();
            BeginTimedIntroFallbackIfNeeded();
        }

        void OnVideoLoopPointReached(VideoPlayer source)
        {
            if (source == null || source != _videoPlayer || !_activeMovieOwnsPhase || _movieState != MoviePlaybackState.Playing)
                return;

            AdvanceFromCurrentIntroPhase();
        }

        void ApplyMovieFallback(UiMovieRuntimeInfo movie)
        {
            if (movie == null || string.IsNullOrWhiteSpace(movie.FallbackImageId))
                return;

            var fallback = _assets.GetImage(movie.FallbackImageId);
            if (fallback != null)
                SetBackgroundImage(fallback, stretchToFill: ShouldStretchBackgroundForCurrentPhase());
        }

        void EnsureVideoTexture()
        {
            int width = Mathf.Max(2, Screen.width);
            int height = Mathf.Max(2, Screen.height);
            if (_videoTexture != null && _videoTexture.width == width && _videoTexture.height == height)
                return;

            if (_videoTexture != null)
            {
                _videoTexture.Release();
                Destroy(_videoTexture);
            }

            _videoTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "VV.BootstrapVideo",
            };
        }

        void UpdateMoviePlaybackState()
        {
            if (_movieState == MoviePlaybackState.Preparing
                && !_moviePrepareWarningLogged
                && Time.unscaledTime - _moviePrepareStartTime >= MoviePrepareWarningSeconds)
            {
                _moviePrepareWarningLogged = true;
                Debug.LogWarning(
                    $"[VVardenfell] bootstrap movie prepare is taking longer than expected for slot '{_activeMovieSlot ?? "<none>"}' in state {_movieState}.");
            }

            if (_movieState == MoviePlaybackState.Prepared && _movieStartQueued)
                StartPreparedMovie();
        }

        void SignalLoadingPhaseReady()
        {
            if (_loadingPhaseSignaled)
                return;

            _loadingPhaseSignaled = true;
            _onLoadingPhaseReady?.Invoke();
        }

        void StartPreparedMovie()
        {
            if (_videoPlayer == null || _activeMovie == null || _movieState != MoviePlaybackState.Prepared)
                return;

            using (k_MovieStart.Auto())
            {
                _videoPlayer.targetTexture = _videoTexture;
                ConfigureMovieAudio(_activeMovie.HasAudio);
                _videoPlayer.Play();
            }

            _movieStartQueued = false;
            _movieState = MoviePlaybackState.Playing;
        }

        void ResetMoviePlaybackObjects()
        {
            if (_videoPlayer != null)
            {
                _videoPlayer.Stop();
                _videoPlayer.targetTexture = null;
                _videoPlayer.url = string.Empty;
                _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            }

            if (_videoAudio != null)
                _videoAudio.Stop();

            if (_videoImage != null)
            {
                _videoImage.texture = null;
                _videoImage.enabled = false;
                Stretch(_videoImage.rectTransform);
            }
        }

        bool IsSameMovieRequest(UiMovieRuntimeInfo movie, bool loop)
        {
            return movie != null
                && _activeMovie != null
                && _movieState != MoviePlaybackState.Idle
                && string.Equals(_activeMovieSlot, movie.Slot, StringComparison.Ordinal)
                && string.Equals(_activeMoviePath, movie.CachedClipPath, StringComparison.OrdinalIgnoreCase)
                && _activeMovieLoop == loop;
        }

        bool PathMatchesActiveMovie(string path)
        {
            return string.Equals(_activeMoviePath ?? string.Empty, path ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        void BeginIntroMoviePhase(string slot)
        {
            _phaseWaitingForMovieCompletion = TryPlayMovie(slot, loop: false);
            if (!_phaseWaitingForMovieCompletion)
                BeginTimedIntroFallback();
        }

        void BeginTimedIntroFallbackIfNeeded()
        {
            if (_phase == PresentationPhase.IntroCompany || _phase == PresentationPhase.IntroLogo)
                BeginTimedIntroFallback();
        }

        void BeginTimedIntroFallback()
        {
            _phaseWaitingForMovieCompletion = false;
            _activeMovieOwnsPhase = false;
            _phaseStartTime = Time.unscaledTime;
        }

        bool ShouldAdvanceIntroPhase(float fallbackSeconds)
        {
            if (_phaseWaitingForMovieCompletion)
                return false;

            return Time.unscaledTime - _phaseStartTime >= fallbackSeconds;
        }

        void AdvanceFromCurrentIntroPhase()
        {
            switch (_phase)
            {
                case PresentationPhase.IntroCompany:
                    SwitchPhase(PresentationPhase.Loading);
                    break;
                case PresentationPhase.IntroLogo:
                    SwitchPhase(PresentationPhase.Menu);
                    break;
            }
        }

        void ConfigureMovieAudio(bool hasAudio)
        {
            if (_videoPlayer == null)
                return;

            if (!hasAudio)
            {
                _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
                if (_videoAudio != null)
                    _videoAudio.Stop();
                return;
            }

            _videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
            _videoPlayer.EnableAudioTrack(0, true);
            _videoPlayer.SetDirectAudioMute(0, false);
            _videoPlayer.SetDirectAudioVolume(0, 1f);
        }

        void StartMenuMusic()
        {
            if (_menuMusicAudio == null)
                return;

            string musicPath = ResolveMainMenuMusicPath();
            if (string.IsNullOrWhiteSpace(musicPath))
                return;

            if (_menuMusicClip != null && string.Equals(_menuMusicPath, musicPath, StringComparison.OrdinalIgnoreCase))
            {
                _menuMusicAudio.clip = _menuMusicClip;
                if (!_menuMusicAudio.isPlaying)
                    _menuMusicAudio.Play();
                return;
            }

            if (_menuMusicLoadRoutine != null)
                return;

            _menuMusicLoadRoutine = StartCoroutine(LoadAndPlayMenuMusic(musicPath));
        }

        void PreloadMenuMusic()
        {
            if (_menuMusicClip != null || _menuMusicLoadRoutine != null)
                return;

            string musicPath = ResolveMainMenuMusicPath();
            if (string.IsNullOrWhiteSpace(musicPath))
                return;

            _menuMusicLoadRoutine = StartCoroutine(LoadAndPlayMenuMusic(musicPath));
        }

        void StopMenuMusic()
        {
            if (_menuMusicLoadRoutine != null)
            {
                StopCoroutine(_menuMusicLoadRoutine);
                _menuMusicLoadRoutine = null;
            }

            if (_menuMusicAudio != null)
                _menuMusicAudio.Stop();
        }

        string ResolveMainMenuMusicPath()
        {
            if (string.IsNullOrWhiteSpace(_installPath))
                return null;

            string path = Path.Combine(_installPath, MainMenuMusicRelativePath);
            if (File.Exists(path))
                return path;

            if (!_menuMusicWarningLogged)
            {
                _menuMusicWarningLogged = true;
                Debug.LogWarning($"[VVardenfell] main menu music was not found at '{path}'.");
            }

            return null;
        }

        System.Collections.IEnumerator LoadAndPlayMenuMusic(string path)
        {
            _menuMusicPath = path;
            using var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.MPEG);
            yield return request.SendWebRequest();

            _menuMusicLoadRoutine = null;

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[VVardenfell] failed loading main menu music '{path}': {request.error}");
                yield break;
            }

            var clip = DownloadHandlerAudioClip.GetContent(request);
            if (clip == null)
            {
                Debug.LogWarning($"[VVardenfell] menu music request completed but produced no clip for '{path}'.");
                yield break;
            }

            if (_menuMusicClip != null && _menuMusicClip != clip)
                Destroy(_menuMusicClip);

            _menuMusicClip = clip;
            _menuMusicPath = path;

            if ((_phase != PresentationPhase.IntroLogo && _phase != PresentationPhase.Menu) || _menuMusicAudio == null)
                yield break;

            _menuMusicAudio.clip = clip;
            _menuMusicAudio.Play();
        }

        void ConfigureIntroFallback(string title, string subtitle, float titleScale, float subtitleScale, bool show)
        {
            _introFallbackTitle.Font = _assets.TitleFont ?? _assets.DefaultFont;
            _introFallbackTitle.FontScale = titleScale;
            _introFallbackTitle.Text = title ?? string.Empty;

            _introFallbackSubtitle.Font = _assets.DefaultFont;
            _introFallbackSubtitle.FontScale = subtitleScale;
            _introFallbackSubtitle.Text = subtitle ?? string.Empty;

            _introFallbackGroup.gameObject.SetActive(show);
        }

        void RefreshScreenDependentLayout()
        {
            if (_rootRect == null)
                return;

            Vector2 size = _rootRect.rect.size;
            if ((size - _lastCanvasSize).sqrMagnitude <= 0.01f)
                return;

            _lastCanvasSize = size;

            if (_activeBackgroundImage?.Texture != null)
                ApplyTextureLayout(_backgroundImage.rectTransform, _activeBackgroundImage.Texture.width, _activeBackgroundImage.Texture.height, StretchMenuBackground || _phase == PresentationPhase.IntroCompany || _phase == PresentationPhase.IntroLogo);

            UpdateVideoLayout();
        }

        void UpdateVideoLayout()
        {
            if (_videoImage == null || !_videoImage.enabled || _activeMovie == null)
                return;

            bool stretch = _phase != PresentationPhase.Menu || StretchMenuBackground;
            ApplyTextureLayout(_videoImage.rectTransform, Mathf.Max(1, _activeMovie.Width), Mathf.Max(1, _activeMovie.Height), stretch);
        }

        void ApplyTextureLayout(RectTransform rect, int textureWidth, int textureHeight, bool stretch)
        {
            if (stretch || textureWidth <= 0 || textureHeight <= 0 || _rootRect == null)
            {
                Stretch(rect);
                return;
            }

            float rootWidth = Mathf.Max(1f, _rootRect.rect.width);
            float rootHeight = Mathf.Max(1f, _rootRect.rect.height);
            float textureAspect = textureWidth / (float)textureHeight;
            float viewAspect = rootWidth / rootHeight;

            float width;
            float height;
            if (textureAspect > viewAspect)
            {
                width = rootWidth;
                height = width / textureAspect;
            }
            else
            {
                height = rootHeight;
                width = height * textureAspect;
            }

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);
        }

        BorderFrameView CreateBorderFrame(string name, Transform parent, BorderTextureSet set, Color centerColor)
        {
            var root = CreateStretchRect(name, parent);
            var result = new BorderFrameView
            {
                Root = root,
            };

            float left = set.Left?.rect.width ?? 0f;
            float right = set.Right?.rect.width ?? 0f;
            float top = set.Top?.rect.height ?? 0f;
            float bottom = set.Bottom?.rect.height ?? 0f;

            result.Center = CreateImage("Center", root, centerColor);
            result.Center.raycastTarget = false;
            result.Center.rectTransform.anchorMin = Vector2.zero;
            result.Center.rectTransform.anchorMax = Vector2.one;
            result.Center.rectTransform.offsetMin = new Vector2(left, bottom);
            result.Center.rectTransform.offsetMax = new Vector2(-right, -top);

            result.Client = result.Center.rectTransform;

            result.Top = CreateEdgeImage("Top", root, set.Top, Image.Type.Simple);
            result.Top.rectTransform.anchorMin = new Vector2(0f, 1f);
            result.Top.rectTransform.anchorMax = new Vector2(1f, 1f);
            result.Top.rectTransform.pivot = new Vector2(0.5f, 1f);
            result.Top.rectTransform.anchoredPosition = new Vector2(0f, 0f);
            result.Top.rectTransform.sizeDelta = new Vector2(-(left + right), top);

            result.Bottom = CreateEdgeImage("Bottom", root, set.Bottom, Image.Type.Simple);
            result.Bottom.rectTransform.anchorMin = new Vector2(0f, 0f);
            result.Bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
            result.Bottom.rectTransform.pivot = new Vector2(0.5f, 0f);
            result.Bottom.rectTransform.anchoredPosition = new Vector2(0f, 0f);
            result.Bottom.rectTransform.sizeDelta = new Vector2(-(left + right), bottom);

            result.Left = CreateEdgeImage("Left", root, set.Left, Image.Type.Simple);
            result.Left.rectTransform.anchorMin = new Vector2(0f, 0f);
            result.Left.rectTransform.anchorMax = new Vector2(0f, 1f);
            result.Left.rectTransform.pivot = new Vector2(0f, 0.5f);
            result.Left.rectTransform.anchoredPosition = new Vector2(0f, 0f);
            result.Left.rectTransform.sizeDelta = new Vector2(left, -(top + bottom));

            result.Right = CreateEdgeImage("Right", root, set.Right, Image.Type.Simple);
            result.Right.rectTransform.anchorMin = new Vector2(1f, 0f);
            result.Right.rectTransform.anchorMax = new Vector2(1f, 1f);
            result.Right.rectTransform.pivot = new Vector2(1f, 0.5f);
            result.Right.rectTransform.anchoredPosition = new Vector2(0f, 0f);
            result.Right.rectTransform.sizeDelta = new Vector2(right, -(top + bottom));

            result.TopLeft = CreateCornerImage("TopLeft", root, set.TopLeft, new Vector2(0f, 1f), new Vector2(0f, 1f));
            result.TopRight = CreateCornerImage("TopRight", root, set.TopRight, new Vector2(1f, 1f), new Vector2(1f, 1f));
            result.BottomLeft = CreateCornerImage("BottomLeft", root, set.BottomLeft, new Vector2(0f, 0f), new Vector2(0f, 0f));
            result.BottomRight = CreateCornerImage("BottomRight", root, set.BottomRight, new Vector2(1f, 0f), new Vector2(1f, 0f));

            return result;
        }

        BorderTextureSet ResolveThinFrame()
        {
            return new BorderTextureSet(
                _assets.GetBootstrapImage(UiBootstrapAssetKeys.ThinBorderTop)?.Sprite,
                _assets.GetBootstrapImage(UiBootstrapAssetKeys.ThinBorderBottom)?.Sprite,
                _assets.GetBootstrapImage(UiBootstrapAssetKeys.ThinBorderLeft)?.Sprite,
                _assets.GetBootstrapImage(UiBootstrapAssetKeys.ThinBorderRight)?.Sprite,
                _assets.GetBootstrapImage(UiBootstrapAssetKeys.ThinBorderTopLeft)?.Sprite,
                _assets.GetBootstrapImage(UiBootstrapAssetKeys.ThinBorderTopRight)?.Sprite,
                _assets.GetBootstrapImage(UiBootstrapAssetKeys.ThinBorderBottomLeft)?.Sprite,
                _assets.GetBootstrapImage(UiBootstrapAssetKeys.ThinBorderBottomRight)?.Sprite);
        }

        BorderTextureSet ResolveThickFrame()
        {
            return new BorderTextureSet(
                _assets.GetBootstrapImage(UiBootstrapAssetKeys.ThickBorderTop)?.Sprite,
                _assets.GetBootstrapImage(UiBootstrapAssetKeys.ThickBorderBottom)?.Sprite,
                _assets.GetBootstrapImage(UiBootstrapAssetKeys.ThickBorderLeft)?.Sprite,
                _assets.GetBootstrapImage(UiBootstrapAssetKeys.ThickBorderRight)?.Sprite,
                _assets.GetBootstrapImage(UiBootstrapAssetKeys.ThickBorderTopLeft)?.Sprite,
                _assets.GetBootstrapImage(UiBootstrapAssetKeys.ThickBorderTopRight)?.Sprite,
                _assets.GetBootstrapImage(UiBootstrapAssetKeys.ThickBorderBottomLeft)?.Sprite,
                _assets.GetBootstrapImage(UiBootstrapAssetKeys.ThickBorderBottomRight)?.Sprite);
        }

        string BuildLoadingLabel()
        {
            if (_progress == null)
                return "Loading";

            string stage = string.IsNullOrWhiteSpace(_progress.Stage) ? "Loading" : _progress.Stage;
            string detail = string.IsNullOrWhiteSpace(_progress.Label) ? stage : $"{stage}: {_progress.Label}";
            if (_progress.Total > 0)
                return $"{detail}  {_progress.Current}/{_progress.Total}";
            return detail;
        }

        string BuildVersionText()
        {
            string version = string.IsNullOrWhiteSpace(Application.version) ? "dev" : Application.version;
            return $"VVardenfell {version}";
        }

        bool ShouldStretchBackgroundForCurrentPhase()
        {
            return _phase == PresentationPhase.IntroCompany || _phase == PresentationPhase.IntroLogo || StretchMenuBackground;
        }

        float ScaleText(float baseScale)
        {
            return baseScale * PresentationTextScaleMultiplier;
        }

        float ScaleLoadingText(float baseScale)
        {
            return baseScale * PresentationTextScaleMultiplier * LoadingScaleMultiplier;
        }

        float ScaleMenuText(float baseScale)
        {
            return baseScale * PresentationTextScaleMultiplier * MenuVisualScaleMultiplier;
        }

        float ScaleLoadingLayout(float value)
        {
            return value * LoadingScaleMultiplier;
        }

        float ScaleMenuLayout(float value)
        {
            return value * MenuVisualScaleMultiplier;
        }

        void UpdateLoadingBarFill(float fraction)
        {
            if (_loadingBarFillRect == null)
                return;

            RectTransform frameRect = _loadingBarFrame?.Root;
            float totalWidth = frameRect != null ? frameRect.rect.width : _loadingBarRect.rect.width;
            float fillWidth = Mathf.Max(0f, totalWidth * fraction);
            _loadingBarFillRect.sizeDelta = new Vector2(fillWidth, 0f);
        }

        float MeasureLineWidth(BitmapFontAsset font, string text, float scale)
        {
            if (font == null || string.IsNullOrEmpty(text))
                return 0f;

            float width = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                if (font.TryGetGlyph(text[i], out var glyph))
                    width += glyph.Advance * scale;
            }

            return width;
        }

        static RectTransform CreateStretchRect(string name, Transform parent)
        {
            var rect = CreateRect(name, parent);
            Stretch(rect);
            return rect;
        }

        static RectTransform CreateAnchoredRect(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            var rect = CreateRect(name, parent);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = anchorMin;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
            return rect;
        }

        static RectTransform CreateAnchorRect(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            var rect = CreateRect(name, parent);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = Vector3.one;
            return rect;
        }

        static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static RawImage CreateRawImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<RawImage>();
            image.color = color;
            return image;
        }

        static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        static BitmapTextGraphic CreateBitmapText(
            string name,
            Transform parent,
            BitmapFontAsset font,
            float scale,
            Color color,
            BitmapTextAlignment alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(BitmapTextGraphic));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<BitmapTextGraphic>();
            text.Font = font;
            text.FontScale = scale;
            text.color = color;
            text.Alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        static Image CreateEdgeImage(string name, Transform parent, Sprite sprite, Image.Type type)
        {
            var image = CreateImage(name, parent, Color.white);
            image.sprite = sprite;
            image.type = type;
            image.raycastTarget = false;
            return image;
        }

        static Image CreateCornerImage(string name, Transform parent, Sprite sprite, Vector2 anchor, Vector2 pivot)
        {
            var image = CreateImage(name, parent, Color.white);
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.raycastTarget = false;
            image.rectTransform.anchorMin = anchor;
            image.rectTransform.anchorMax = anchor;
            image.rectTransform.pivot = pivot;
            image.rectTransform.anchoredPosition = Vector2.zero;
            image.rectTransform.sizeDelta = sprite != null ? sprite.rect.size : Vector2.zero;
            return image;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static void SetInset(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(right, top);
        }

        readonly struct BorderTextureSet
        {
            public BorderTextureSet(Sprite top, Sprite bottom, Sprite left, Sprite right, Sprite topLeft, Sprite topRight, Sprite bottomLeft, Sprite bottomRight)
            {
                Top = top;
                Bottom = bottom;
                Left = left;
                Right = right;
                TopLeft = topLeft;
                TopRight = topRight;
                BottomLeft = bottomLeft;
                BottomRight = bottomRight;
            }

            public Sprite Top { get; }
            public Sprite Bottom { get; }
            public Sprite Left { get; }
            public Sprite Right { get; }
            public Sprite TopLeft { get; }
            public Sprite TopRight { get; }
            public Sprite BottomLeft { get; }
            public Sprite BottomRight { get; }
        }
    }
}
