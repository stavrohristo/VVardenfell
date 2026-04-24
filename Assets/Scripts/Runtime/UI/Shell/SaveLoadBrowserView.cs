using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    /// <summary>
    /// Vanilla Morrowind save/load dialog.
    ///
    /// Mirrors <c>openmw_savegame_dialog.layout</c> (see <c>docs/ui-reference/openmw-ui-skins.md</c>)
    /// at the reference 600x400 dialog size:
    /// <list type="bullet">
    ///   <item>MW_Window chassis with the caption <c>"Save Game"</c> / <c>"Load Game"</c>
    ///     (vanilla uses <c>MW_DialogNoTransp</c>, which is the same chassis without the
    ///     semi-transparent overlay; we reuse <see cref="RuntimeUiFactory.CreateMorrowindWindow"/>
    ///     with a caption since vanilla renders the mode as a caption).</item>
    ///   <item>Two-column content area: save list on the left (MW_Box + scroll), right
    ///     column with a reserved screenshot panel (MW_Box, 137 tall) stacked above a
    ///     word-wrapped info text panel.</item>
    ///   <item>Footer row (32 tall) with: Delete (left), save-name edit (stretched center,
    ///     only visible in Save mode), OK (primary), Cancel (right).</item>
    ///   <item>Confirm sub-dialog - a smaller MW_Box-framed panel that overlays the main
    ///     dialog for "Overwrite existing save?" / "Return to main menu?" prompts.</item>
    /// </list>
    ///
    /// Screenshot preview is reserved but blank - the screenshot pipeline lands later.
    /// The public ctor + <see cref="Sync"/> signature is unchanged from the previous
    /// implementation so <c>RuntimeHudShellView</c> needs no edits.
    /// </summary>
    public sealed class SaveLoadBrowserView
    {
        // Palette.
        static readonly Color BackdropColor = new(0f, 0f, 0f, 0.68f);
        static readonly Color BodyTextColor = new(0.94f, 0.85f, 0.68f);
        static readonly Color SubtleTextColor = new(0.76f, 0.73f, 0.66f);
        static readonly Color DimTextColor = new(0.58f, 0.54f, 0.48f);
        static readonly Color InvalidTextColor = new(0.78f, 0.42f, 0.34f);
        static readonly Color SectionFrameColor = new(0f, 0f, 0f, 0.72f);
        static readonly Color ScreenshotBackdropColor = new(0.02f, 0.02f, 0.02f, 0.94f);
        static readonly Color RowColor = new(0.08f, 0.07f, 0.05f, 0.76f);
        static readonly Color RowSelectedColor = new(0.42f, 0.32f, 0.15f, 0.92f);
        static readonly Color ButtonCenterColor = new(0.12f, 0.1f, 0.08f, 0.88f);
        static readonly Color ConfirmBackdropColor = new(0f, 0f, 0f, 0.72f);

        // Pixel heights — sourced from the OpenMW-faithful canonical table.
        const float CaptionPixelHeight = RuntimeClassicUiFontSizes.Caption;
        const float BodyTextPixelHeight = RuntimeClassicUiFontSizes.Body;
        const float RowTitlePixelHeight = RuntimeClassicUiFontSizes.Body;
        const float RowMetaPixelHeight = RuntimeClassicUiFontSizes.Subtle;
        const float ButtonTextPixelHeight = RuntimeClassicUiFontSizes.Body;

        // Dialog geometry (matches openmw_savegame_dialog.layout "0 0 600 400").
        const float DefaultWindowWidth = 600f;
        const float DefaultWindowHeight = 400f;
        const float CaptionHeight = 20f;
        const float ClientInset = 8f;

        // Interior layout.
        const float ContentBottomGap = 6f;        // between content area and footer
        const float MiddleColumnGap = 8f;         // between save list and right column
        const float FooterHeight = 32f;
        const float RightColumnWidth = 263f;      // matches vanilla layout
        const float ScreenshotHeight = 137f;      // matches vanilla MW_Box size
        const float ScreenshotInfoGap = 8f;
        const float RowHeight = 52f;
        const float RowSpacing = 2f;

        // Footer buttons.
        const float ButtonHeight = 22f;
        const float ButtonMinWidth = 82f;
        const float DeleteButtonWidth = 96f;
        const float OkButtonWidth = 82f;
        const float OverwriteButtonWidth = 96f;
        const float CancelButtonWidth = 82f;
        const float ButtonSpacing = 4f;

        // Confirm sub-dialog.
        const float ConfirmWidth = 420f;
        const float ConfirmHeight = 170f;
        const float ConfirmButtonWidth = 96f;

        readonly RuntimeUiTheme _theme;
        readonly Action<string> _selectSlot;
        readonly Action<string> _setName;
        readonly Action _newSave;
        readonly Action _overwrite;
        readonly Action _load;
        readonly Action _delete;
        readonly Action _cancel;
        readonly Action _confirm;
        readonly Action _cancelConfirm;

        readonly RectTransform _root;
        readonly MorrowindWindowView _window;
        readonly RuntimeUiTextInputView _nameInput;
        readonly RectTransform _listContent;
        readonly BitmapTextGraphic _infoText;
        readonly BitmapTextGraphic _status;
        readonly RectTransform _confirmRoot;
        readonly BitmapTextGraphic _confirmText;
        readonly MorrowindButtonView _deleteButton;
        readonly MorrowindButtonView _overwriteButton;
        readonly MorrowindButtonView _primaryButton;
        readonly MorrowindButtonView _cancelButton;
        readonly MorrowindButtonView _confirmButton;
        readonly MorrowindButtonView _cancelConfirmButton;
        readonly List<GameObject> _rows = new();
        string _rowSignature;

        public SaveLoadBrowserView(
            Transform parent,
            RuntimeUiTheme theme,
            Action<string> selectSlot,
            Action<string> setName,
            Action newSave,
            Action overwrite,
            Action load,
            Action delete,
            Action cancel,
            Action confirm,
            Action cancelConfirm)
        {
            _theme = theme;
            _selectSlot = selectSlot;
            _setName = setName;
            _newSave = newSave;
            _overwrite = overwrite;
            _load = load;
            _delete = delete;
            _cancel = cancel;
            _confirm = confirm;
            _cancelConfirm = cancelConfirm;

            _root = RuntimeUiFactory.CreateStretchRect("SaveLoadBrowser", parent);
            _root.gameObject.SetActive(false);

            // Full-screen backdrop dims the game behind the dialog. Button on it blocks
            // raycasts from falling through to gameplay.
            var blocker = RuntimeUiFactory.CreateImage("Backdrop", _root, BackdropColor);
            blocker.raycastTarget = true;
            RuntimeUiFactory.Stretch(blocker.rectTransform);

            // Center the dialog with the MW_Window chassis.
            var windowHolder = RuntimeUiFactory.CreateAnchoredRect(
                "DialogHolder",
                _root,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(DefaultWindowWidth, DefaultWindowHeight)));
            windowHolder.pivot = new Vector2(0.5f, 0.5f);

            _window = RuntimeUiFactory.CreateMorrowindWindow(
                "Dialog",
                windowHolder,
                theme,
                "Save Game",
                RuntimeClassicUiMetrics.Ui(CaptionHeight),
                RuntimeClassicUiMetrics.Ui(ClientInset),
                0.92f,
                RuntimeClassicUiMetrics.Ui(CaptionPixelHeight),
                new Color(0.94f, 0.82f, 0.53f));
            RuntimeUiFactory.Stretch(_window.Root);

            (_listContent, _infoText, _status, _nameInput, _deleteButton, _overwriteButton, _primaryButton, _cancelButton) = BuildClient();
            (_confirmRoot, _confirmText, _confirmButton, _cancelConfirmButton) = BuildConfirmOverlay();
        }

        public void Sync(SaveLoadBrowserViewModel model)
        {
            bool visible = model != null;
            _root.gameObject.SetActive(visible);
            if (!visible)
            {
                _rowSignature = null;
                return;
            }

            _window.Title.Text = string.IsNullOrWhiteSpace(model.Title) ? "Save Game" : model.Title.Trim();

            // Name edit is only meaningful in Save mode (vanilla: Load has no edit box).
            bool saveMode = model.Mode == SaveLoadBrowserMode.Save;
            _nameInput.Root.gameObject.SetActive(saveMode);
            RuntimeUiFactory.SetBitmapInputDisplay(
                _nameInput,
                model.DraftSaveName ?? string.Empty,
                "Save name",
                BodyTextColor,
                DimTextColor);

            // Primary button caption + action. Save mode defaults to "Save" (new slot);
            // Load mode defaults to "Load"; main-menu confirm uses the primary button
            // as "Yes".
            _primaryButton.Label.Text = model.PrimaryButtonText ?? (saveMode ? "Save" : "Load");
            _primaryButton.Button.onClick.RemoveAllListeners();
            _primaryButton.Button.onClick.AddListener(
                model.Mode == SaveLoadBrowserMode.Load ? () => _load?.Invoke()
                : model.Mode == SaveLoadBrowserMode.MainMenuConfirm ? () => _confirm?.Invoke()
                : () => _newSave?.Invoke());
            SetButtonEnabled(_primaryButton, model.CanPrimary);

            // Overwrite + Delete visibility follows mode (hidden for main-menu confirm).
            _overwriteButton.Root.gameObject.SetActive(saveMode);
            SetButtonEnabled(_overwriteButton, model.CanOverwrite);
            _deleteButton.Root.gameObject.SetActive(model.Mode != SaveLoadBrowserMode.MainMenuConfirm);
            SetButtonEnabled(_deleteButton, model.CanDelete);

            // Status line at bottom. Use the explicit status text if present, else derive
            // a friendly fallback from mode + slot count.
            _status.Text = string.IsNullOrWhiteSpace(model.StatusText) ? BuildFooter(model) : model.StatusText.Trim();

            // Hide the slot list entirely for the main-menu confirm variant.
            _listContent.parent.parent.gameObject.SetActive(model.Mode != SaveLoadBrowserMode.MainMenuConfirm);
            RebuildRows(model);
            SyncSelectedSlotInfo(model);

            _confirmRoot.gameObject.SetActive(model.Confirming);
            _confirmText.Text = string.IsNullOrWhiteSpace(model.ConfirmationText)
                ? "Are you sure?"
                : model.ConfirmationText.Trim();
        }

        // ----- Build client ---------------------------------------------------

        (RectTransform listContent, BitmapTextGraphic infoText, BitmapTextGraphic status, RuntimeUiTextInputView nameInput,
            MorrowindButtonView deleteButton, MorrowindButtonView overwriteButton, MorrowindButtonView primaryButton, MorrowindButtonView cancelButton)
        BuildClient()
        {
            float footerHeight = RuntimeClassicUiMetrics.Ui(FooterHeight);
            float contentBottomGap = RuntimeClassicUiMetrics.Ui(ContentBottomGap);
            float rightWidth = RuntimeClassicUiMetrics.Ui(RightColumnWidth);
            float middleGap = RuntimeClassicUiMetrics.Ui(MiddleColumnGap);
            float shotHeight = RuntimeClassicUiMetrics.Ui(ScreenshotHeight);
            float shotGap = RuntimeClassicUiMetrics.Ui(ScreenshotInfoGap);

            // Content area fills everything above the footer.
            var contentRoot = RuntimeUiFactory.CreateAnchorRect(
                "Content",
                _window.Client,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, footerHeight + contentBottomGap),
                Vector2.zero);

            // Right column (screenshot + info). Pinned right.
            var rightColumn = RuntimeUiFactory.CreateAnchorRect(
                "RightColumn",
                contentRoot,
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0.5f),
                new Vector2(-rightWidth, 0f),
                Vector2.zero);

            // Screenshot at the top - reserved MW_Box, renders nothing until the
            // screenshot pipeline lands (same policy as the inventory portrait).
            var shotRoot = RuntimeUiFactory.CreateAnchorRect(
                "Screenshot",
                rightColumn,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -shotHeight),
                Vector2.zero);
            var shotFrame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                shotRoot,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                ScreenshotBackdropColor);
            RuntimeUiFactory.Stretch(shotFrame.Root);

            // Info text panel below the screenshot. Thin frame + word-wrapped multiline
            // text. Populated from the selected slot's metadata in SyncSelectedSlotInfo.
            var infoRoot = RuntimeUiFactory.CreateAnchorRect(
                "Info",
                rightColumn,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(0f, -(shotHeight + shotGap)));
            var infoFrame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                infoRoot,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                SectionFrameColor);
            RuntimeUiFactory.Stretch(infoFrame.Root);

            var infoText = RuntimeUiFactory.CreateBitmapText(
                "Text",
                infoFrame.Client,
                _theme?.DefaultFont,
                1f,
                BodyTextColor,
                BitmapTextAlignment.Left);
            infoText.PixelHeight = RuntimeClassicUiMetrics.Ui(BodyTextPixelHeight);
            infoText.WrapMode = BitmapTextWrapMode.Word;
            infoText.VerticalAlignment = BitmapTextVerticalAlignment.Top;
            infoText.raycastTarget = false;
            RuntimeUiFactory.SetInset(
                infoText.rectTransform,
                RuntimeClassicUiMetrics.Ui(8f),
                RuntimeClassicUiMetrics.Ui(6f),
                -RuntimeClassicUiMetrics.Ui(8f),
                -RuntimeClassicUiMetrics.Ui(6f));

            // Save list - fills the remaining width left of the right column.
            var listRoot = RuntimeUiFactory.CreateAnchorRect(
                "SaveList",
                contentRoot,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(-(rightWidth + middleGap), 0f));
            var listFrame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                listRoot,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                SectionFrameColor);
            RuntimeUiFactory.Stretch(listFrame.Root);

            var viewportRect = RuntimeUiFactory.CreateStretchRect("Viewport", listFrame.Client);
            var viewportImage = viewportRect.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
            viewportImage.raycastTarget = true;
            viewportRect.gameObject.AddComponent<RectMask2D>();

            var listContent = RuntimeUiFactory.CreateAnchoredRect(
                "Content",
                viewportRect,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                Vector2.zero);
            listContent.pivot = new Vector2(0.5f, 1f);

            var scroll = listRoot.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = listContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            // Footer row - pinned to the bottom of the client, full width.
            var footerRoot = RuntimeUiFactory.CreateAnchorRect(
                "Footer",
                _window.Client,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                Vector2.zero,
                new Vector2(0f, footerHeight));
            var (deleteButton, nameInput, overwriteButton, primaryButton, cancelButton, status) = BuildFooterRow(footerRoot);

            return (listContent, infoText, status, nameInput, deleteButton, overwriteButton, primaryButton, cancelButton);
        }

        (MorrowindButtonView delete, RuntimeUiTextInputView name, MorrowindButtonView overwrite, MorrowindButtonView primary, MorrowindButtonView cancel, BitmapTextGraphic status)
        BuildFooterRow(RectTransform footer)
        {
            float btnHeight = RuntimeClassicUiMetrics.Ui(ButtonHeight);
            float btnSpacing = RuntimeClassicUiMetrics.Ui(ButtonSpacing);
            float deleteWidth = RuntimeClassicUiMetrics.Ui(DeleteButtonWidth);
            float overwriteWidth = RuntimeClassicUiMetrics.Ui(OverwriteButtonWidth);
            float okWidth = RuntimeClassicUiMetrics.Ui(OkButtonWidth);
            float cancelWidth = RuntimeClassicUiMetrics.Ui(CancelButtonWidth);

            // Row layout, left to right:
            //   [Delete] [NameEdit ...stretches...] [Overwrite] [Primary] [Cancel]
            // Each button's width is fixed, the name edit absorbs the remaining width.

            // Delete - anchored to the left edge.
            var delete = BuildFooterButton(footer, "DeleteButton", "Delete", anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(0f, 1f),
                offsetMin: new Vector2(0f, 0f), offsetMax: new Vector2(deleteWidth, 0f), clickAction: () => _delete?.Invoke());

            // Cancel - anchored to the right edge.
            var cancel = BuildFooterButton(footer, "CancelButton", "Cancel", anchorMin: new Vector2(1f, 0f), anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(-cancelWidth, 0f), offsetMax: new Vector2(0f, 0f), clickAction: () => _cancel?.Invoke());

            // Primary - left of Cancel.
            float primaryRightOffset = -(cancelWidth + btnSpacing);
            var primary = BuildFooterButton(footer, "PrimaryButton", "Save", anchorMin: new Vector2(1f, 0f), anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(primaryRightOffset - okWidth, 0f), offsetMax: new Vector2(primaryRightOffset, 0f), clickAction: null);

            // Overwrite - left of Primary (visible only in Save mode).
            float overwriteRightOffset = primaryRightOffset - okWidth - btnSpacing;
            var overwrite = BuildFooterButton(footer, "OverwriteButton", "Overwrite", anchorMin: new Vector2(1f, 0f), anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(overwriteRightOffset - overwriteWidth, 0f), offsetMax: new Vector2(overwriteRightOffset, 0f),
                clickAction: () => _overwrite?.Invoke());

            // Name edit - fills the gap between Delete and Overwrite (or Primary when
            // Overwrite is hidden). We anchor left to the right of Delete and right to
            // Overwrite's left edge; Overwrite visibility toggling makes the gap larger
            // but the edit will visually line up regardless.
            float leftAnchor = deleteWidth + btnSpacing;
            float rightAnchor = overwriteRightOffset - overwriteWidth - btnSpacing;
            var nameInputFrameRoot = RuntimeUiFactory.CreateAnchorRect(
                "NameEditFrame",
                footer,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(leftAnchor, 0f),
                new Vector2(rightAnchor, 0f));
            var nameFrame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                nameInputFrameRoot,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                SectionFrameColor);
            RuntimeUiFactory.Stretch(nameFrame.Root);

            var nameInput = RuntimeUiFactory.CreateBitmapInputField(
                "NameInput",
                nameFrame.Client,
                _theme,
                1f,
                BodyTextColor,
                Color.clear,
                "Save name",
                8f,
                4f,
                32);
            RuntimeUiFactory.Stretch(nameInput.Root);
            nameInput.OverlayText.PixelHeight = RuntimeClassicUiMetrics.Ui(BodyTextPixelHeight);
            nameInput.OverlayText.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            nameInput.InputField.onValueChanged.AddListener(value => _setName?.Invoke(value));

            // Status text line floats above the footer (sits inside the footer vertical
            // band but at the top edge). We pin it to the footer's top edge + a small
            // offset so it sits just below the content area.
            var status = RuntimeUiFactory.CreateBitmapText(
                "Status",
                footer,
                _theme?.DefaultFont,
                1f,
                SubtleTextColor,
                BitmapTextAlignment.Center);
            status.PixelHeight = RuntimeClassicUiMetrics.Ui(BodyTextPixelHeight);
            status.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            status.raycastTarget = false;
            status.rectTransform.anchorMin = new Vector2(0f, 1f);
            status.rectTransform.anchorMax = new Vector2(1f, 1f);
            status.rectTransform.pivot = new Vector2(0.5f, 0f);
            status.rectTransform.anchoredPosition = new Vector2(0f, RuntimeClassicUiMetrics.Ui(4f));
            status.rectTransform.sizeDelta = new Vector2(0f, RuntimeClassicUiMetrics.Ui(18f));

            // Wire primary click after mutation by Sync(...). We leave it bare here.
            _ = nameFrame;

            return (delete, nameInput, overwrite, primary, cancel, status);
        }

        MorrowindButtonView BuildFooterButton(
            RectTransform footer,
            string name,
            string label,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax,
            UnityEngine.Events.UnityAction clickAction)
        {
            var rect = RuntimeUiFactory.CreateAnchorRect(
                name + "Rect",
                footer,
                anchorMin,
                anchorMax,
                new Vector2(0.5f, 0.5f),
                offsetMin,
                offsetMax);

            var button = RuntimeUiFactory.CreateMorrowindButton(
                "Button",
                rect,
                _theme,
                label,
                1f,
                BodyTextColor,
                ButtonCenterColor);
            RuntimeUiFactory.Stretch(button.Root);
            button.Label.PixelHeight = RuntimeClassicUiMetrics.Ui(ButtonTextPixelHeight);
            button.Label.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            button.Button.transition = Selectable.Transition.ColorTint;
            if (clickAction != null)
                button.Button.onClick.AddListener(clickAction);
            return button;
        }

        // ----- Confirm overlay -----------------------------------------------

        (RectTransform confirmRoot, BitmapTextGraphic text, MorrowindButtonView confirm, MorrowindButtonView cancelConfirm)
        BuildConfirmOverlay()
        {
            var confirmRoot = RuntimeUiFactory.CreateStretchRect("ConfirmOverlay", _root);
            var blocker = RuntimeUiFactory.CreateImage("Backdrop", confirmRoot, ConfirmBackdropColor);
            blocker.raycastTarget = true;
            RuntimeUiFactory.Stretch(blocker.rectTransform);

            var holder = RuntimeUiFactory.CreateAnchoredRect(
                "Panel",
                confirmRoot,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(ConfirmWidth, ConfirmHeight)));
            holder.pivot = new Vector2(0.5f, 0.5f);

            // Confirm dialog uses the thin MW_Box border (vanilla MW_Dialog skin) rather
            // than the window's outer + inner thick borders - it's a transient prompt.
            var frame = RuntimeUiFactory.CreateBorderFrame(
                "Frame",
                holder,
                RuntimeUiFactory.ResolveThinFrame(_theme),
                new Color(0f, 0f, 0f, 0.94f));
            RuntimeUiFactory.Stretch(frame.Root);

            var text = RuntimeUiFactory.CreateBitmapText(
                "Text",
                frame.Client,
                _theme?.DefaultFont,
                1f,
                BodyTextColor,
                BitmapTextAlignment.Center);
            text.PixelHeight = RuntimeClassicUiMetrics.Ui(BodyTextPixelHeight);
            text.WrapMode = BitmapTextWrapMode.Word;
            text.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = new Vector2(0f, 0f);
            text.rectTransform.anchorMax = new Vector2(1f, 1f);
            text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            text.rectTransform.offsetMin = new Vector2(RuntimeClassicUiMetrics.Ui(24f), RuntimeClassicUiMetrics.Ui(48f));
            text.rectTransform.offsetMax = new Vector2(-RuntimeClassicUiMetrics.Ui(24f), -RuntimeClassicUiMetrics.Ui(24f));

            float btnWidth = RuntimeClassicUiMetrics.Ui(ConfirmButtonWidth);
            float btnHeight = RuntimeClassicUiMetrics.Ui(ButtonHeight);
            float btnSpacing = RuntimeClassicUiMetrics.Ui(ButtonSpacing);

            // Centered Yes/No button pair at the bottom.
            var yesRect = RuntimeUiFactory.CreateAnchoredRect(
                "YesRect",
                frame.Client,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(-(btnWidth + btnSpacing * 0.5f), RuntimeClassicUiMetrics.Ui(14f)),
                new Vector2(btnWidth, btnHeight));
            var confirmButton = RuntimeUiFactory.CreateMorrowindButton(
                "Yes",
                yesRect,
                _theme,
                "Yes",
                1f,
                BodyTextColor,
                ButtonCenterColor);
            RuntimeUiFactory.Stretch(confirmButton.Root);
            confirmButton.Label.PixelHeight = RuntimeClassicUiMetrics.Ui(ButtonTextPixelHeight);
            confirmButton.Label.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            confirmButton.Button.onClick.AddListener(() => _confirm?.Invoke());

            var noRect = RuntimeUiFactory.CreateAnchoredRect(
                "NoRect",
                frame.Client,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(btnSpacing * 0.5f, RuntimeClassicUiMetrics.Ui(14f)),
                new Vector2(btnWidth, btnHeight));
            var cancelConfirm = RuntimeUiFactory.CreateMorrowindButton(
                "No",
                noRect,
                _theme,
                "No",
                1f,
                BodyTextColor,
                ButtonCenterColor);
            RuntimeUiFactory.Stretch(cancelConfirm.Root);
            cancelConfirm.Label.PixelHeight = RuntimeClassicUiMetrics.Ui(ButtonTextPixelHeight);
            cancelConfirm.Label.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            cancelConfirm.Button.onClick.AddListener(() => _cancelConfirm?.Invoke());

            confirmRoot.gameObject.SetActive(false);
            return (confirmRoot, text, confirmButton, cancelConfirm);
        }

        // ----- Row list --------------------------------------------------------

        void RebuildRows(SaveLoadBrowserViewModel model)
        {
            string signature = BuildRowsSignature(model);
            if (string.Equals(signature, _rowSignature, StringComparison.Ordinal))
                return;

            _rowSignature = signature;
            for (int i = 0; i < _rows.Count; i++)
                UnityEngine.Object.Destroy(_rows[i]);
            _rows.Clear();

            float rowHeight = RuntimeClassicUiMetrics.Ui(RowHeight);
            float rowSpacing = RuntimeClassicUiMetrics.Ui(RowSpacing);
            float y = 0f;

            for (int i = 0; i < model.Slots.Length; i++)
            {
                var slot = model.Slots[i];

                var root = RuntimeUiFactory.CreateAnchoredRect(
                    $"Slot_{i}",
                    _listContent,
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, -y),
                    new Vector2(0f, rowHeight));
                root.pivot = new Vector2(0f, 1f);

                var background = RuntimeUiFactory.CreateImage("Background", root, slot.Selected ? RowSelectedColor : RowColor);
                background.raycastTarget = true;
                RuntimeUiFactory.Stretch(background.rectTransform);

                var button = root.gameObject.AddComponent<Button>();
                button.targetGraphic = background;
                button.transition = Selectable.Transition.None;
                string slotId = slot.SlotId;
                button.onClick.AddListener(() => _selectSlot?.Invoke(slotId));

                bool valid = slot.Valid;
                Color primaryColor = valid ? BodyTextColor : InvalidTextColor;
                Color metaColor = valid ? SubtleTextColor : InvalidTextColor;

                CreateRowText(root, "Name", valid
                    ? (slot.Legacy ? $"{slot.Name}  (legacy)" : slot.Name)
                    : slot.Name,
                    new Vector2(10f, -6f), new Vector2(-130f, 18f),
                    RowTitlePixelHeight, BitmapTextAlignment.Left, primaryColor);
                CreateRowText(root, "Version", slot.VersionText,
                    new Vector2(0f, -6f), new Vector2(-12f, 18f),
                    RowMetaPixelHeight, BitmapTextAlignment.Right, metaColor,
                    anchorRight: true);
                string detailRow = valid
                    ? string.Join("    ", Trim(slot.CharacterText), Trim(slot.LocationText), Trim(slot.TimestampText)).Trim()
                    : (string.IsNullOrWhiteSpace(slot.ErrorText) ? "Save is not readable." : slot.ErrorText);
                CreateRowText(root, "Detail", detailRow,
                    new Vector2(10f, -28f), new Vector2(-20f, 18f),
                    RowMetaPixelHeight, BitmapTextAlignment.Left, metaColor);

                _rows.Add(root.gameObject);
                y += rowHeight + rowSpacing;
            }

            _listContent.sizeDelta = new Vector2(0f, Mathf.Max(rowHeight, y));
        }

        static string Trim(string s) => s?.Trim() ?? string.Empty;

        void SyncSelectedSlotInfo(SaveLoadBrowserViewModel model)
        {
            SaveSlotRowViewModel selected = null;
            if (model.Slots != null)
            {
                for (int i = 0; i < model.Slots.Length; i++)
                {
                    if (model.Slots[i].Selected) { selected = model.Slots[i]; break; }
                }
            }

            if (selected == null || !selected.Valid)
            {
                _infoText.Text = selected == null
                    ? "Select a save slot for details."
                    : (string.IsNullOrWhiteSpace(selected.ErrorText) ? "Save is not readable." : selected.ErrorText);
                return;
            }

            // Vanilla info panel: one line per field, plain text with the values. We
            // concatenate with newlines so BitmapText word-wrap + multi-line rendering
            // flows like MyGUI's MultiLine SandText.
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrWhiteSpace(selected.Name)) sb.Append(selected.Name.Trim()).Append('\n');
            if (!string.IsNullOrWhiteSpace(selected.CharacterText)) sb.Append(selected.CharacterText.Trim()).Append('\n');
            if (!string.IsNullOrWhiteSpace(selected.LocationText)) sb.Append(selected.LocationText.Trim()).Append('\n');
            if (!string.IsNullOrWhiteSpace(selected.TimestampText)) sb.Append(selected.TimestampText.Trim()).Append('\n');
            if (!string.IsNullOrWhiteSpace(selected.VersionText)) sb.Append(selected.VersionText.Trim());
            _infoText.Text = sb.ToString().TrimEnd('\n');
        }

        void CreateRowText(
            RectTransform parent,
            string name,
            string text,
            Vector2 anchorPos,
            Vector2 sizeDelta,
            float pixelHeight,
            BitmapTextAlignment alignment,
            Color color,
            bool anchorRight = false)
        {
            var graphic = RuntimeUiFactory.CreateBitmapText(
                name,
                parent,
                _theme?.DefaultFont,
                1f,
                color,
                alignment);
            graphic.PixelHeight = RuntimeClassicUiMetrics.Ui(pixelHeight);
            graphic.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            graphic.raycastTarget = false;
            graphic.Text = text ?? string.Empty;

            if (anchorRight)
            {
                graphic.rectTransform.anchorMin = new Vector2(1f, 1f);
                graphic.rectTransform.anchorMax = new Vector2(1f, 1f);
                graphic.rectTransform.pivot = new Vector2(1f, 1f);
            }
            else
            {
                graphic.rectTransform.anchorMin = new Vector2(0f, 1f);
                graphic.rectTransform.anchorMax = new Vector2(1f, 1f);
                graphic.rectTransform.pivot = new Vector2(0f, 1f);
            }

            graphic.rectTransform.anchoredPosition = RuntimeClassicUiMetrics.Ui(anchorPos);
            graphic.rectTransform.sizeDelta = RuntimeClassicUiMetrics.Ui(sizeDelta);
        }

        string BuildRowsSignature(SaveLoadBrowserViewModel model)
        {
            int width = Mathf.RoundToInt(Mathf.Max(1f, _listContent.rect.width));
            var parts = new System.Text.StringBuilder();
            parts.Append((byte)model.Mode).Append('|').Append(width).Append('|');
            if (model.Slots == null)
                return parts.ToString();

            for (int i = 0; i < model.Slots.Length; i++)
            {
                var row = model.Slots[i];
                parts.Append(row.SlotId).Append(';')
                    .Append(row.Name).Append(';')
                    .Append(row.TimestampText).Append(';')
                    .Append(row.CharacterText).Append(';')
                    .Append(row.LocationText).Append(';')
                    .Append(row.VersionText).Append(';')
                    .Append(row.Valid ? '1' : '0')
                    .Append(row.Legacy ? '1' : '0')
                    .Append(row.Selected ? '1' : '0')
                    .Append(row.ErrorText).Append('|');
            }

            return parts.ToString();
        }

        static void SetButtonEnabled(MorrowindButtonView view, bool enabled)
        {
            view.Button.interactable = enabled;
            view.Label.color = enabled ? BodyTextColor : DimTextColor;
        }

        static string BuildFooter(SaveLoadBrowserViewModel model)
        {
            if (model.Mode == SaveLoadBrowserMode.MainMenuConfirm)
                return "Confirm to return to the main menu.";
            if (model.Slots == null || model.Slots.Length == 0)
                return "No save slots are available.";
            return model.Mode == SaveLoadBrowserMode.Save
                ? "Create a new save or select a slot to overwrite."
                : "Select a save slot to load.";
        }
    }
}
