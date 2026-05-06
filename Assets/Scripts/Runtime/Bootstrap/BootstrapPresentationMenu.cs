using System;
using System.Collections.Generic;
using System.IO;
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
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.UI.Assets;
using VVardenfell.Runtime.UI.Framework;
using VVardenfell.Runtime.UI.Shell;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.Bootstrap
{
    public sealed partial class BootstrapPresentationView
    {
        void HandleMenuOptionsInput()
        {
            if (!_menuOptionsVisible || _phase != PresentationPhase.Menu)
                return;

            bool cancelPressed = (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false)
                || (Gamepad.current?.buttonEast.wasPressedThisFrame ?? false);
            if (cancelPressed)
                CloseMenuOptions();
        }


        void BuildMenuView()
        {
            _menuRoot = RuntimeUiFactory.CreateStretchRect("MenuRoot", _rootRect);
            _menuRoot.gameObject.SetActive(false);

            _menuButtonBox = RuntimeUiFactory.CreateAnchoredRect(
                "MenuButtonBox",
                _menuRoot,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, ScaleMenuLayout(MenuBottomPadding)),
                new Vector2(1f, 1f));

            _versionText = RuntimeUiFactory.CreateBitmapText(
                "VersionText",
                _menuRoot,
                _theme.DefaultFont,
                ScaleMenuText(0.6f),
                new Color(0.93f, 0.88f, 0.75f),
                BitmapTextAlignment.Right);
            _versionText.Text = BuildVersionText();
            _versionText.rectTransform.anchorMin = new Vector2(1f, 0f);
            _versionText.rectTransform.anchorMax = new Vector2(1f, 0f);
            _versionText.rectTransform.pivot = new Vector2(1f, 0f);
            _versionText.rectTransform.anchoredPosition = new Vector2(-ScaleMenuLayout(20f), ScaleMenuLayout(16f));
            _versionText.rectTransform.sizeDelta = new Vector2(ScaleMenuLayout(320f), ScaleMenuLayout(42f));

            BuildMenuDialogView();
            BuildMenuSaveLoadBrowserView();
            BuildMenuOptionsView();
        }

        void BuildMenuOptionsView()
        {
            // Bootstrap main menu shares config with the in-game Hud shell (both
            // load/save the same JSON on disk via ConfigStorage). Reading it here
            // gives the Options sliders their persisted values on first open; the
            // view mutates this shared instance and we save on Close.
            if (!VVardenfell.Core.Config.ConfigStorage.TryLoad(out _config) || _config == null)
                _config = new VVardenfell.Core.Config.MorrowindConfig();

            Camera cam = MainCameraUtility.GetRequiredCamera();
            _menuOptionsView = new OptionsWindowView(
                _menuRoot,
                _theme,
                _config,
                BuildMenuOptionsCallbacks(cam));
        }

        OptionsWindowView.Callbacks BuildMenuOptionsCallbacks(Camera camera)
        {
            return new OptionsWindowView.Callbacks
            {
                UiScale = v => RuntimeUiScaleSettings.GlobalScale = v,
                HudScale = v => RuntimeUiScaleSettings.HudScale = v,
                MasterVolume = v => RuntimeAudioService.Active?.SetMasterVolume(v),
                MusicVolume = v => RuntimeAudioService.Active?.SetMusicVolume(v),
                EffectsVolume = v => RuntimeAudioService.Active?.SetEffectsVolume(v),
                FootstepsVolume = null,
                VoiceVolume = null,
                ShowCrosshair = v => RuntimeShellRequestBridge.TrySetHudShowCrosshair(v, out _),
                ShowSubtitles = v => RuntimeShellRequestBridge.TrySetHudShowSubtitles(v, out _),
                MenuTransparency = null,
                Difficulty = VVardenfell.Runtime.Combat.MorrowindCombatSettingsBridge.PublishDifficultyInDefaultWorld,
                Fov = v => { camera.fieldOfView = v; },
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
                Close = CloseMenuOptions,
                ResetToDefaults = () =>
                {
                    if (_config == null) return;
                    _config.ResetPlayerSettingsToDefaults();
                    ApplyMenuConfigValuesLive(camera);
                    _menuOptionsView.SyncFromConfig();
                },
            };
        }

        void OpenMenuOptions()
        {
            if (_menuOptionsView == null)
                return;
            _menuOptionsVisible = true;
            _menuOptionsView.SetVisible(true);
        }

        void CloseMenuOptions()
        {
            if (_menuOptionsView == null)
                return;
            _menuOptionsVisible = false;
            _menuOptionsView.SetVisible(false);
            if (_config != null && !VVardenfell.Core.Config.ConfigStorage.TrySave(_config, out string error))
                Debug.LogWarning($"[VVardenfell][Bootstrap] failed saving config: {error}");
        }

        void ApplyMenuConfigValuesLive(Camera camera)
        {
            if (_config == null) return;
            RuntimeUiScaleSettings.GlobalScale = _config.UiScale;
            RuntimeUiScaleSettings.HudScale = _config.HudScale;
            RuntimeAudioService.Active?.SetMasterVolume(_config.MasterVolume);
            RuntimeAudioService.Active?.SetMusicVolume(_config.MusicVolume);
            RuntimeAudioService.Active?.SetEffectsVolume(_config.EffectsVolume);
            RuntimeShellRequestBridge.TrySetHudShowCrosshair(_config.ShowCrosshair, out _);
            RuntimeShellRequestBridge.TrySetHudShowSubtitles(_config.ShowSubtitles, out _);
            camera.fieldOfView = _config.Fov;
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


        void RefreshMenuButtons()
        {
            for (int i = 0; i < _menuButtons.Count; i++)
            {
                if (_menuButtons[i].Rect != null)
                    Destroy(_menuButtons[i].Rect.gameObject);
            }

            _menuButtons.Clear();

            var visibleStates = new List<BootstrapMenuActionState>(k_MenuDefinitions.Length);
            for (int i = 0; i < k_MenuDefinitions.Length; i++)
            {
                var state = BuildMenuActionState(k_MenuDefinitions[i].Action);
                if (state.Visible)
                    visibleStates.Add(state);
            }

            if (visibleStates.Count == 0)
            {
                _menuButtonBox.sizeDelta = Vector2.zero;
                return;
            }

            float maxWidth = 0f;
            float totalHeight = 0f;
            var sizes = new Vector2[visibleStates.Count];

            for (int i = 0; i < visibleStates.Count; i++)
            {
                var definition = GetMenuDefinition(visibleStates[i].Action);
                var sprite = _theme.GetBootstrapSprite(definition.NormalKey);
                if (sprite == null)
                    continue;

                float requestedWidth = sprite.rect.width;
                float requestedHeight = sprite.rect.height;
                float scale = requestedHeight / 64f;
                if (scale <= 0f)
                    scale = 1f;

                float width = (requestedWidth / scale) * RuntimeUiFactory.MenuVisualScaleMultiplier;
                float height = Mathf.Max(1f, (requestedHeight / scale - 16f) * RuntimeUiFactory.MenuVisualScaleMultiplier);
                sizes[i] = new Vector2(width, height);
                maxWidth = Mathf.Max(maxWidth, width);
                totalHeight += height;
            }

            _menuButtonBox.sizeDelta = new Vector2(maxWidth, totalHeight);

            float curY = 0f;
            for (int i = 0; i < visibleStates.Count; i++)
            {
                var state = visibleStates[i];
                var definition = GetMenuDefinition(state.Action);
                var normal = _theme.GetBootstrapSprite(definition.NormalKey);
                var highlighted = _theme.GetBootstrapSprite(definition.HighlightedKey) ?? normal;
                var pressed = _theme.GetBootstrapSprite(definition.PressedKey) ?? highlighted ?? normal;
                if (normal == null)
                    continue;

                var buttonView = RuntimeUiFactory.CreateSpriteButton(
                    $"MenuButton_{definition.Action}",
                    _menuButtonBox,
                    normal,
                    highlighted,
                    pressed,
                    enabled: state.Enabled,
                    preserveAspect: true,
                    flipVertical: true);
                var rect = buttonView.Root;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2((maxWidth - sizes[i].x) * 0.5f, -curY);
                rect.sizeDelta = sizes[i];

                var action = state.Action;
                buttonView.Button.onClick.AddListener(() => OnMenuButtonPressed(action));

                _menuButtons.Add(new BootstrapMenuButtonView
                {
                    Action = state.Action,
                    Rect = rect,
                    Image = buttonView.Image,
                    Button = buttonView.Button,
                });

                curY += sizes[i].y;
            }

            ConfigureMenuNavigation();
        }

        void OnMenuButtonPressed(BootstrapMenuActionId action)
        {
            _lastSelectedMenuAction = action;
            var state = BuildMenuActionState(action);
            if (!state.Enabled)
            {
                ShowMenuDialog(state.UnavailableTitle, state.UnavailableBody);
                return;
            }

            switch (action)
            {
                case BootstrapMenuActionId.Continue:
                    if (GameInitializationRequestBridge.TryRequestContinue(out var continueError))
                        BeginRuntimeLoading("Resuming game");
                    else
                        ShowMenuDialog("Continue Unavailable", continueError);
                    break;

                case BootstrapMenuActionId.NewGame:
                    if (GameInitializationRequestBridge.TryRequestNewGame(out var newGameError))
                        BeginRuntimeLoading("Starting new game");
                    else
                        ShowMenuDialog("New Game Unavailable", newGameError);
                    break;

                case BootstrapMenuActionId.LoadGame:
                    OpenMenuSaveLoadBrowser();
                    break;

                case BootstrapMenuActionId.Options:
                    OpenMenuOptions();
                    break;

                case BootstrapMenuActionId.ExitGame:
                    Application.Quit();
                    break;
            }
        }

        void Dismiss()
        {
            CloseMenuDialog();
            _runtimeLoading = false;
            _dismissed = true;
            _phase = PresentationPhase.Dismissed;
            BootstrapPresentationAudioState.SetPhase(BootstrapAudioPhase.Dismissed);
            BootstrapPresentationGate.BlocksGameplayInput = false;
            gameObject.SetActive(false);
        }

        BootstrapMenuActionState BuildMenuActionState(BootstrapMenuActionId action)
        {
            return action switch
            {
                BootstrapMenuActionId.Continue => BuildUnavailableAwareAction(
                    action,
                    GameInitializationRequestBridge.GetContinueAvailability(),
                    "Continue Unavailable"),
                BootstrapMenuActionId.NewGame => BuildUnavailableAwareAction(
                    action,
                    GameInitializationRequestBridge.GetNewGameAvailability(),
                    "New Game Unavailable"),
                BootstrapMenuActionId.LoadGame => BuildUnavailableAwareAction(
                    action,
                    GameInitializationRequestBridge.GetLoadGameAvailability(),
                    "Load Game Unavailable"),
                BootstrapMenuActionId.Options => new BootstrapMenuActionState(
                    action,
                    visible: true,
                    enabled: true,
                    unavailableTitle: string.Empty,
                    unavailableBody: string.Empty),
                BootstrapMenuActionId.Credits => BuildUnavailableAction(
                    action,
                    "Credits Deferred",
                    "A dedicated Credits panel is deferred. This bootstrap slice is focused on completing the startup-to-world menu flow."),
                BootstrapMenuActionId.ExitGame => new BootstrapMenuActionState(
                    action,
                    visible: true,
                    enabled: true,
                    unavailableTitle: string.Empty,
                    unavailableBody: string.Empty),
                _ => default,
            };
        }

        BootstrapMenuActionState BuildUnavailableAwareAction(
            BootstrapMenuActionId action,
            GameInitializationRequestBridge.RequestAvailability availability,
            string unavailableTitle)
        {
            return new BootstrapMenuActionState(
                action,
                visible: true,
                enabled: availability.Available,
                unavailableTitle: unavailableTitle,
                unavailableBody: availability.Reason);
        }

        static BootstrapMenuActionState BuildUnavailableAction(
            BootstrapMenuActionId action,
            string title,
            string body)
        {
            return new BootstrapMenuActionState(
                action,
                visible: true,
                enabled: false,
                unavailableTitle: title,
                unavailableBody: body);
        }

        static BootstrapMenuDefinition GetMenuDefinition(BootstrapMenuActionId action)
        {
            for (int i = 0; i < k_MenuDefinitions.Length; i++)
            {
                if (k_MenuDefinitions[i].Action == action)
                    return k_MenuDefinitions[i];
            }

            throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown bootstrap menu action.");
        }

        void ConfigureMenuNavigation()
        {
            if (_menuButtons.Count == 0)
                return;

            for (int i = 0; i < _menuButtons.Count; i++)
            {
                var button = _menuButtons[i].Button;
                if (button == null)
                    continue;

                var navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = _menuButtons[(i - 1 + _menuButtons.Count) % _menuButtons.Count].Button,
                    selectOnDown = _menuButtons[(i + 1) % _menuButtons.Count].Button,
                };
                button.navigation = navigation;
            }

            RestoreMenuSelection();
        }

        void RestoreMenuSelection()
        {
            if (_eventSystem == null || _menuButtons.Count == 0)
                return;

            for (int i = 0; i < _menuButtons.Count; i++)
            {
                if (_menuButtons[i].Action != _lastSelectedMenuAction || _menuButtons[i].Button == null)
                    continue;

                _eventSystem.SetSelectedGameObject(_menuButtons[i].Button.gameObject);
                return;
            }

            if (_menuButtons[0].Button != null)
                _eventSystem.SetSelectedGameObject(_menuButtons[0].Button.gameObject);
        }


        string BuildVersionText()
        {
            string version = string.IsNullOrWhiteSpace(Application.version) ? "dev" : Application.version;
            return $"VVardenfell {version}";
        }

    }
}
