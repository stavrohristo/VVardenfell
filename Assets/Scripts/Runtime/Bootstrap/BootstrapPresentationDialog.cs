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
using VVardenfell.Runtime.UI.Assets;
using VVardenfell.Runtime.UI.Framework;
using VVardenfell.Runtime.UI.Shell;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.Bootstrap
{
    public sealed partial class BootstrapPresentationView
    {
        void HandleMenuDialogInput()
        {
            if (!IsMenuDialogVisible() || Time.frameCount == _menuDialogOpenedFrame)
                return;

            bool confirmPressed = (Keyboard.current?.enterKey.wasPressedThisFrame ?? false)
                || (Keyboard.current?.numpadEnterKey.wasPressedThisFrame ?? false)
                || (Keyboard.current?.spaceKey.wasPressedThisFrame ?? false)
                || (Gamepad.current?.buttonSouth.wasPressedThisFrame ?? false);
            bool cancelPressed = (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false)
                || (Gamepad.current?.buttonEast.wasPressedThisFrame ?? false);
            if (!confirmPressed && !cancelPressed)
                return;

            CloseMenuDialog();
        }


        void BuildMenuDialogView()
        {
            _menuDialogRoot = RuntimeUiFactory.CreateStretchRect("MenuDialogRoot", _menuRoot);
            _menuDialogRoot.gameObject.SetActive(false);

            _menuDialogBlocker = RuntimeUiFactory.CreateImage("MenuDialogBlocker", _menuDialogRoot, new Color(0f, 0f, 0f, 0.72f));
            RuntimeUiFactory.Stretch(_menuDialogBlocker.rectTransform);
            _menuDialogBlocker.raycastTarget = true;

            var blockerButton = _menuDialogBlocker.gameObject.AddComponent<Button>();
            blockerButton.transition = Selectable.Transition.None;
            blockerButton.targetGraphic = _menuDialogBlocker;
            blockerButton.onClick.AddListener(CloseMenuDialog);
            blockerButton.navigation = new Navigation { mode = Navigation.Mode.None };

            _menuDialogRect = RuntimeUiFactory.CreateAnchoredRect(
                "MenuDialog",
                _menuDialogRoot,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(ScaleMenuLayout(520f), ScaleMenuLayout(176f)));
            _menuDialogRect.pivot = new Vector2(0.5f, 0.5f);

            _menuDialogFrame = RuntimeUiFactory.CreateBorderFrame(
                "MenuDialogFrame",
                _menuDialogRect,
                RuntimeUiFactory.ResolveThickFrame(_theme),
                new Color(0f, 0f, 0f, 0.94f));
            RuntimeUiFactory.Stretch(_menuDialogFrame.Root);

            _menuDialogTitle = RuntimeUiFactory.CreateBitmapText(
                "MenuDialogTitle",
                _menuDialogRect,
                _theme.DefaultFont,
                ScaleMenuText(0.7f),
                new Color(0.94f, 0.82f, 0.53f),
                BitmapTextAlignment.Center);
            _menuDialogTitle.rectTransform.anchorMin = new Vector2(0f, 1f);
            _menuDialogTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
            _menuDialogTitle.rectTransform.pivot = new Vector2(0.5f, 1f);
            _menuDialogTitle.rectTransform.anchoredPosition = new Vector2(0f, -ScaleMenuLayout(18f));
            _menuDialogTitle.rectTransform.sizeDelta = new Vector2(-ScaleMenuLayout(48f), ScaleMenuLayout(28f));

            _menuDialogBody = RuntimeUiFactory.CreateBitmapText(
                "MenuDialogBody",
                _menuDialogRect,
                _theme.DefaultFont,
                ScaleMenuText(0.52f),
                new Color(0.93f, 0.88f, 0.75f),
                BitmapTextAlignment.Center);
            _menuDialogBody.WrapMode = BitmapTextWrapMode.Word;
            _menuDialogBody.rectTransform.anchorMin = new Vector2(0f, 0f);
            _menuDialogBody.rectTransform.anchorMax = new Vector2(1f, 1f);
            _menuDialogBody.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _menuDialogBody.rectTransform.anchoredPosition = new Vector2(0f, -ScaleMenuLayout(6f));
            _menuDialogBody.rectTransform.sizeDelta = new Vector2(-ScaleMenuLayout(72f), -ScaleMenuLayout(88f));

            _menuDialogFooter = RuntimeUiFactory.CreateBitmapText(
                "MenuDialogFooter",
                _menuDialogRect,
                _theme.DefaultFont,
                ScaleMenuText(0.42f),
                new Color(0.76f, 0.73f, 0.66f),
                BitmapTextAlignment.Center);
            _menuDialogFooter.rectTransform.anchorMin = new Vector2(0f, 0f);
            _menuDialogFooter.rectTransform.anchorMax = new Vector2(1f, 0f);
            _menuDialogFooter.rectTransform.pivot = new Vector2(0.5f, 0f);
            _menuDialogFooter.rectTransform.anchoredPosition = new Vector2(0f, ScaleMenuLayout(14f));
            _menuDialogFooter.rectTransform.sizeDelta = new Vector2(-ScaleMenuLayout(48f), ScaleMenuLayout(18f));
            _menuDialogFooter.Text = "Press Enter, Escape, or B to return";
        }


        void ShowMenuDialog(string title, string body)
        {
            if (_menuDialogRoot == null)
                return;

            _menuDialogTitle.Text = string.IsNullOrWhiteSpace(title) ? "Unavailable" : title.Trim();
            _menuDialogBody.Text = string.IsNullOrWhiteSpace(body)
                ? "This action is not available in the current bootstrap slice."
                : body.Trim();
            _menuDialogRoot.gameObject.SetActive(true);
            _menuDialogOpenedFrame = Time.frameCount;

            if (_eventSystem != null)
                _eventSystem.SetSelectedGameObject(null);
        }

        void CloseMenuDialog()
        {
            if (_menuDialogRoot == null || !_menuDialogRoot.gameObject.activeSelf)
                return;

            _menuDialogRoot.gameObject.SetActive(false);
            _menuDialogOpenedFrame = -1;
            RestoreMenuSelection();
        }

        bool IsMenuDialogVisible()
        {
            return _menuDialogRoot != null && _menuDialogRoot.gameObject.activeSelf;
        }

    }
}
