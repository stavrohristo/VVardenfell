using System;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.UI.Assets;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    public sealed class CharacterGenerationWindowView
    {
        static readonly Color BackdropColor = new(0f, 0f, 0f, 0.68f);
        static readonly Color BodyTextColor = new(0.94f, 0.85f, 0.68f);
        static readonly Color MutedTextColor = new(0.65f, 0.58f, 0.46f);
        static readonly Color ButtonColor = new(0.12f, 0.10f, 0.08f, 0.90f);

        const float DialogWidth = 780f;
        const float DialogHeight = 620f;
        const float NameDialogWidth = 320f;
        const float NameDialogHeight = 97f;
        const float ButtonHeight = 24f;
        const float RowHeight = 26f;
        const float Margin = 18f;

        readonly RuntimeUiTheme _theme;
        readonly RuntimeInventoryIconService _iconService;
        readonly RectTransform _root;
        readonly RectTransform _dialogRoot;
        readonly RectTransform _client;
        readonly BitmapTextGraphic _title;
        readonly RuntimeUiTextInputView _nameInput;
        readonly RectTransform _nameDialogRoot;
        readonly RectTransform _nameClient;
        readonly BitmapTextGraphic _nameLabel;
        readonly BitmapTextGraphic _nameStatus;
        readonly RectTransform _nameButtonRect;
        readonly MorrowindButtonView _nameDoneButton;
        readonly RectTransform _contentRoot;
        readonly RectTransform _footerRoot;
        readonly CharacterGenerationRacePreviewRenderer _racePreviewRenderer = new();

        enum CustomClassOverlay
        {
            None,
            Description,
            Specialization,
            Attribute,
            Skill,
        }

        string _lastSyncedName = string.Empty;
        string _nameEmptyMessage = "Name cannot be empty.";
        string _lastBuildSignature;
        float _racePreviewAngleDegrees;
        CharacterGenerationMenuView _lastBuiltMenu = CharacterGenerationMenuView.None;
        bool _nameWasVisible;
        CustomClassOverlay _customOverlay;
        int _customOverlaySlot;
        RuntimeUiTextInputView _customNameInput;
        RuntimeUiTextInputView _customDescriptionInput;
        string _customDraftName;
        bool _customDraftNameActive;

        public CharacterGenerationWindowView(Transform parent, RuntimeUiTheme theme, RuntimeInventoryIconService iconService)
        {
            _theme = theme;
            _iconService = iconService;
            _root = RuntimeUiFactory.CreateStretchRect("CharacterGeneration", parent);
            _root.gameObject.SetActive(false);

            var blocker = RuntimeUiFactory.CreateImage("Backdrop", _root, BackdropColor);
            blocker.raycastTarget = true;
            RuntimeUiFactory.Stretch(blocker.rectTransform);

            _dialogRoot = RuntimeUiFactory.CreateAnchoredRect(
                "Dialog",
                _root,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(DialogWidth, DialogHeight)));
            _dialogRoot.pivot = new Vector2(0.5f, 0.5f);
            var background = RuntimeUiFactory.CreateImage("Background", _dialogRoot, new Color(0f, 0f, 0f, 0.94f));
            background.raycastTarget = true;
            RuntimeUiFactory.Stretch(background.rectTransform);
            var frame = RuntimeUiFactory.CreateBorderFrame("Frame", _dialogRoot, RuntimeUiFactory.ResolveThickFrame(_theme), Color.clear);
            RuntimeUiFactory.Stretch(frame.Root);

            _client = RuntimeUiFactory.CreateAnchorRect(
                "Client",
                frame.Client,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                RuntimeClassicUiMetrics.Ui(new Vector2(Margin, Margin)),
                RuntimeClassicUiMetrics.Ui(new Vector2(-Margin, -Margin)));

            _title = CreateText("Title", _client, 0f, 0f, DialogWidth - Margin * 2f, 32f, BodyTextColor, BitmapTextAlignment.Center);
            _title.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Caption);

            _nameDialogRoot = RuntimeUiFactory.CreateAnchoredRect(
                "NameDialog",
                _root,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(NameDialogWidth, NameDialogHeight)));
            _nameDialogRoot.pivot = new Vector2(0.5f, 0.5f);
            var nameBackground = RuntimeUiFactory.CreateImage("Background", _nameDialogRoot, new Color(0f, 0f, 0f, 0.94f));
            nameBackground.raycastTarget = true;
            RuntimeUiFactory.Stretch(nameBackground.rectTransform);
            var nameFrame = RuntimeUiFactory.CreateBorderFrame("Frame", _nameDialogRoot, RuntimeUiFactory.ResolveThickFrame(_theme), Color.clear);
            RuntimeUiFactory.Stretch(nameFrame.Root);
            _nameClient = nameFrame.Client;

            _nameLabel = CreateText("LabelT", _nameClient, 6f, 6f, 300f, 18f, BodyTextColor, BitmapTextAlignment.Left);
            _nameLabel.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);

            _nameInput = RuntimeUiFactory.CreateBitmapInputField("TextEdit", _nameClient, _theme, 1f, BodyTextColor, new Color(0f, 0f, 0f, 0.7f), string.Empty);
            _nameInput.Root.anchorMin = new Vector2(0f, 1f);
            _nameInput.Root.anchorMax = new Vector2(0f, 1f);
            _nameInput.Root.pivot = new Vector2(0f, 1f);
            _nameInput.Root.anchoredPosition = RuntimeClassicUiMetrics.Ui(new Vector2(6f, -28f));
            _nameInput.Root.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(300f, 30f));
            _nameInput.OverlayText.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            _nameInput.OverlayText.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            _nameInput.InputField.onValueChanged.AddListener(OnNameInputChanged);
            _nameInput.InputField.onSubmit.AddListener(_ => SubmitName());

            _nameStatus = CreateText("Status", _nameClient, 6f, 60f, 220f, 23f, MutedTextColor, BitmapTextAlignment.Left);
            _nameStatus.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Small);

            _nameButtonRect = RuntimeUiFactory.CreateAnchoredRect(
                "OKButton",
                _nameClient,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                RuntimeClassicUiMetrics.Ui(new Vector2(264f, -60f)),
                RuntimeClassicUiMetrics.Ui(new Vector2(42f, 23f)));
            _nameButtonRect.pivot = new Vector2(0f, 1f);
            _nameDoneButton = BuildButton(_nameButtonRect, "OK", SubmitName);
            _nameDoneButton.Label.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            _nameDialogRoot.gameObject.SetActive(false);

            _contentRoot = RuntimeUiFactory.CreateAnchorRect(
                "Content",
                _client,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
                RuntimeClassicUiMetrics.Ui(new Vector2(0f, 58f)),
                RuntimeClassicUiMetrics.Ui(new Vector2(0f, -64f)));
            _contentRoot.pivot = new Vector2(0f, 1f);

            _footerRoot = RuntimeUiFactory.CreateAnchorRect(
                "Footer",
                _client,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 0f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(0f, 42f)));
        }

        public void Sync(CharacterGenerationViewModel model)
        {
            bool visible = model != null && model.Menu != CharacterGenerationMenuView.None;
            _root.gameObject.SetActive(visible);
            if (!visible)
            {
                _nameWasVisible = false;
                _lastBuiltMenu = CharacterGenerationMenuView.None;
                _lastBuildSignature = null;
                _customOverlay = CustomClassOverlay.None;
                _customDraftNameActive = false;
                return;
            }

            bool nameVisible = model.Menu == CharacterGenerationMenuView.Name;
            if (model.Menu != CharacterGenerationMenuView.ClassCreate)
            {
                _customOverlay = CustomClassOverlay.None;
                _customDraftNameActive = false;
            }
            _dialogRoot.gameObject.SetActive(!nameVisible);
            _nameDialogRoot.gameObject.SetActive(nameVisible);
            ConfigureMainDialog(model.Menu);

            string buildSignature = BuildModelSignature(model);
            bool rebuild = model.Menu != _lastBuiltMenu || !string.Equals(buildSignature, _lastBuildSignature, StringComparison.Ordinal);
            if (rebuild)
            {
                Clear(_contentRoot);
                Clear(_footerRoot);
            }

            if (rebuild || nameVisible)
            {
                switch (model.Menu)
                {
                    case CharacterGenerationMenuView.Name:
                        BuildName(model);
                        break;
                    case CharacterGenerationMenuView.Race:
                        BuildRace(model);
                        break;
                    case CharacterGenerationMenuView.ClassChoice:
                        BuildClassChoice(model);
                        break;
                    case CharacterGenerationMenuView.ClassPick:
                        BuildPickClass(model);
                        break;
                    case CharacterGenerationMenuView.ClassCreate:
                        BuildCreateClass(model);
                        break;
                    case CharacterGenerationMenuView.ClassGenerateQuestion:
                        BuildGenerateQuestion(model);
                        break;
                    case CharacterGenerationMenuView.ClassGenerateResult:
                        BuildGenerateResult(model);
                        break;
                    case CharacterGenerationMenuView.Birth:
                        BuildBirthsign(model);
                        break;
                    case CharacterGenerationMenuView.Review:
                        BuildReview(model);
                        break;
                }
            }
            else if (model.Menu == CharacterGenerationMenuView.Race)
            {
                _racePreviewRenderer.Render(_racePreviewAngleDegrees);
            }

            _lastBuiltMenu = model.Menu;
            _lastBuildSignature = buildSignature;
            _nameWasVisible = nameVisible;
        }

        void BuildName(CharacterGenerationViewModel model)
        {
            _nameLabel.Text = string.IsNullOrWhiteSpace(model.NameLabel) ? "Name" : model.NameLabel.Trim();
            _nameEmptyMessage = string.IsNullOrWhiteSpace(model.NameEmptyMessage) ? "Name cannot be empty." : model.NameEmptyMessage.Trim();
            if (!_nameWasVisible)
                _nameStatus.Text = string.Empty;

            string value = _nameWasVisible ? _lastSyncedName : model.Name ?? string.Empty;
            _lastSyncedName = value;
            RuntimeUiFactory.SetBitmapInputDisplay(_nameInput, value, string.Empty, BodyTextColor, MutedTextColor);
            SetNameButtonText(string.IsNullOrWhiteSpace(model.NameButtonText) ? "OK" : model.NameButtonText.Trim());

            if (!_nameWasVisible)
                FocusNameInput();
        }

        void OnNameInputChanged(string value)
        {
            _lastSyncedName = value ?? string.Empty;
            _nameStatus.Text = string.Empty;
            RuntimeUiFactory.SetBitmapInputDisplay(_nameInput, _lastSyncedName, string.Empty, BodyTextColor, MutedTextColor);
        }

        void SubmitName()
        {
            string value = (_nameInput.InputField?.text ?? _lastSyncedName ?? string.Empty).Trim();
            if (!ValidateName(value, out string error))
            {
                _nameStatus.Text = error;
                FocusNameInput();
                return;
            }

            if (!RuntimeShellRequestBridge.TryCharacterGenerationAction(
                    CharacterGenerationAction.ChooseName,
                    CharacterGenerationMenu.None,
                    null,
                    value,
                    0,
                    0,
                    0,
                    out error))
            {
                _nameStatus.Text = error ?? "Character generation is not ready.";
                FocusNameInput();
            }
        }

        bool ValidateName(string value, out string error)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                error = _nameEmptyMessage;
                return false;
            }

            int byteCount = Encoding.UTF8.GetByteCount(value);
            if (byteCount > FixedString64Bytes.UTF8MaxLengthInBytes)
            {
                error = $"Name is too long. Maximum is {FixedString64Bytes.UTF8MaxLengthInBytes} UTF-8 bytes.";
                return false;
            }

            error = null;
            return true;
        }

        void FocusNameInput()
        {
            if (_nameInput?.InputField == null || !_nameInput.Root.gameObject.activeInHierarchy)
                return;

            EventSystem.current?.SetSelectedGameObject(_nameInput.InputField.gameObject);
            _nameInput.InputField.ActivateInputField();
        }

        void SetNameButtonText(string label)
        {
            label = string.IsNullOrWhiteSpace(label) ? "OK" : label.Trim();
            _nameDoneButton.Label.Text = label;
            float width = RuntimeClassicUiMetrics.Ui(Mathf.Max(42f, label.Length * 9f + 18f));
            _nameButtonRect.anchoredPosition = RuntimeClassicUiMetrics.Ui(new Vector2(306f, -60f)) - new Vector2(width, 0f);
            _nameButtonRect.sizeDelta = new Vector2(width, RuntimeClassicUiMetrics.Ui(23f));
        }

        void BuildRace(CharacterGenerationViewModel model)
        {
            CreateText("AppearanceT", _contentRoot, 8f, 16f, 241f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = Label(model.RaceAppearanceLabel, "Appearance");
            RectTransform previewBox = CreateBox("PreviewBox", _contentRoot, 8f, 39f, 241f, 220f);
            var preview = RuntimeUiFactory.CreateRawImage("PreviewImage", previewBox, Color.white);
            preview.texture = _racePreviewRenderer.Texture;
            preview.raycastTarget = true;
            preview.rectTransform.anchorMin = Vector2.zero;
            preview.rectTransform.anchorMax = Vector2.one;
            preview.rectTransform.offsetMin = RuntimeClassicUiMetrics.Ui(new Vector2(2f, 2f));
            preview.rectTransform.offsetMax = RuntimeClassicUiMetrics.Ui(new Vector2(-2f, -2f));
            AttachPreviewScroll(preview.gameObject);
            _racePreviewRenderer.Render(_racePreviewAngleDegrees);

            AddRaceSlider("HeadRotate", 8f, 270f, 241f);
            AddRaceCycleRow("Gender", 294f, Label(model.RaceGenderLabel, "Change Sex"), () => ToggleGender(model), () => ToggleGender(model));
            AddRaceCycleRow("Face", 316f, IndexedLabel(model.RaceFaceLabel, "Change Face", model.HeadIndex, model.Heads.Length),
                () => SelectBodyPart(model.Heads, model.HeadIndex - 1, CharacterGenerationAction.AdjustHead),
                () => SelectBodyPart(model.Heads, model.HeadIndex + 1, CharacterGenerationAction.AdjustHead));
            AddRaceCycleRow("Hair", 338f, IndexedLabel(model.RaceHairLabel, "Change Hair", model.HairIndex, model.Hairs.Length),
                () => SelectBodyPart(model.Hairs, model.HairIndex - 1, CharacterGenerationAction.AdjustHair),
                () => SelectBodyPart(model.Hairs, model.HairIndex + 1, CharacterGenerationAction.AdjustHair));

            CreateText("RaceT", _contentRoot, 261f, 16f, 160f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = Label(model.RaceListLabel, "Race");
            BuildRaceList(model.Races, 264f, 39f, 160f, 150f);

            CreateText("SpellPowerT", _contentRoot, 261f, 210f, 160f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = Label(model.RaceSpecialsLabel, "Specials");
            BuildRacePowers(model.RacePowers, 261f, 230f, 350f, 140f);

            CreateText("SkillsT", _contentRoot, 432f, 39f, 190f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = Label(model.RaceSkillBonusLabel, "Skill Bonus");
            BuildRaceSkills(model.RaceSkillBonuses, 432f, 59f, 190f);

            AddRaceFooterButton(Label(model.RaceBackButtonText, "Back"), 471f, () => RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.Back, out _));
            AddRaceFooterButton(Label(model.RaceOkButtonText, "OK"), 532f, () => SubmitRace(model));
        }

        void BuildClassChoice(CharacterGenerationViewModel model)
        {
            CreateInfoBoxButtons(new (string, UnityEngine.Events.UnityAction)[]
            {
                (Label(model.ClassChoiceGenerateText, "Generate Class"), () => ChooseClassPath(CharacterGenerationClassChoice.Generate)),
                (Label(model.ClassChoicePickText, "Pick Class"), () => ChooseClassPath(CharacterGenerationClassChoice.Pick)),
                (Label(model.ClassChoiceCreateText, "Create Custom Class"), () => ChooseClassPath(CharacterGenerationClassChoice.Create)),
                (Label(model.BackText, "Back"), () => ChooseClassPath(CharacterGenerationClassChoice.Back)),
            });
        }

        void BuildPickClass(CharacterGenerationViewModel model)
        {
            BuildClassList(model.Classes, 8f, 8f, 194f, 138f);
            RectTransform imageBox = CreateBox("ClassImageBox", _contentRoot, 210f, 8f, 265f, 138f);
            CreateTextureImage("ClassImage", imageBox, RequireClassImageSprite(model.SelectedClass), preserveAspect: true);
            BuildClassDetails(model.SelectedClass, 8f, 156f, model);
            AddDialogButton(Label(model.BackText, "Back"), 310f, 276f, 76f, () => RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.Back, out _));
            AddDialogButton(Label(model.ClassOkText, "OK"), 394f, 276f, 76f, () =>
                RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.PickClass, CharacterGenerationMenu.None, model.ClassId, null, 0, 0, 1, out _));
        }

        void BuildCreateClass(CharacterGenerationViewModel model)
        {
            var custom = model.CustomClass;
            string customName = _customDraftNameActive ? _customDraftName : custom.Name;
            CreateText("LabelT", _contentRoot, 8f, 8f, 52f, 23f, BodyTextColor, BitmapTextAlignment.Left).Text = $"{Label(model.CustomClassNameLabel, "Name")}:";
            _customNameInput = CreateInlineInput("EditName", 72f, 8f, 410f, 23f, customName, Label(model.CustomClassDefaultName, "Adventurer"));
            _customNameInput.InputField.onValueChanged.AddListener(value =>
            {
                _customDraftName = value ?? string.Empty;
                _customDraftNameActive = true;
            });

            CreateText("SpecializationT", _contentRoot, 8f, 38f, 156f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = Label(model.ClassSpecializationLabel, "Specialization");
            RuntimeUiPopupUtility.SetTooltip(
                AddSandTextButton("SpecializationName", SpecializationName(custom.Specialization), 8f, 56f, 166f, () => ShowCustomOverlay(model, CustomClassOverlay.Specialization, 0)).Root.gameObject,
                custom.SpecializationTooltipText);

            CreateText("FavoriteAttributesT", _contentRoot, 8f, 79f, 166f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = Label(model.ClassFavoredAttributesLabel, "Favorite Attributes");
            RuntimeUiPopupUtility.SetTooltip(
                AddSandTextButton("FavoriteAttribute0", AttributeName(custom.FavoredAttribute0), 8f, 97f, 166f, () => ShowCustomOverlay(model, CustomClassOverlay.Attribute, 0)).Root.gameObject,
                custom.FavoredAttribute0TooltipText);
            RuntimeUiPopupUtility.SetTooltip(
                AddSandTextButton("FavoriteAttribute1", AttributeName(custom.FavoredAttribute1), 8f, 115f, 166f, () => ShowCustomOverlay(model, CustomClassOverlay.Attribute, 1)).Root.gameObject,
                custom.FavoredAttribute1TooltipText);

            CreateText("MajorSkillT", _contentRoot, 174f, 38f, 166f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = Label(model.SkillClassMajorLabel, "Major Skills");
            CreateText("MinorSkillT", _contentRoot, 340f, 38f, 150f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = Label(model.SkillClassMinorLabel, "Minor Skills");
            for (int i = 0; i < 5; i++)
            {
                int majorSlot = i;
                int minorSlot = i + 5;
                RuntimeUiPopupUtility.SetTooltip(
                    AddSandTextButton($"MajorSkill{i}", RuntimeContentMetadataResolver.ResolveSkillName(custom.MajorSkills[i]), 174f, 56f + i * 18f, 166f, () => ShowCustomOverlay(model, CustomClassOverlay.Skill, majorSlot)).Root.gameObject,
                    GetString(custom.MajorSkillTooltips, i));
                RuntimeUiPopupUtility.SetTooltip(
                    AddSandTextButton($"MinorSkill{i}", RuntimeContentMetadataResolver.ResolveSkillName(custom.MinorSkills[i]), 340f, 56f + i * 18f, 150f, () => ShowCustomOverlay(model, CustomClassOverlay.Skill, minorSlot)).Root.gameObject,
                    GetString(custom.MinorSkillTooltips, i));
            }

            AddDialogButton(Label(model.ClassDescriptionButtonText, "Description"), 246f, 158f, 110f, () => ShowCustomOverlay(model, CustomClassOverlay.Description, 0));
            AddDialogButton(Label(model.BackText, "Back"), 362f, 158f, 56f, () => RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.Back, out _));
            AddDialogButton(Label(model.ClassOkText, "OK"), 426f, 158f, 56f, () =>
                RuntimeShellRequestBridge.TryCharacterGenerationAction(
                    CharacterGenerationAction.AcceptCustomClass,
                    CharacterGenerationMenu.None,
                    null,
                    _customNameInput?.InputField?.text ?? customName,
                    0,
                    0,
                    1,
                    out _));
            BuildCustomOverlay(model);
        }

        void BuildGenerateQuestion(CharacterGenerationViewModel model)
        {
            if (model.GenerateQuestion == null)
                return;

            CreateWrappedText("QuestionText", _contentRoot, 18f, 18f, 508f, 62f, model.GenerateQuestion.Question, BitmapTextAlignment.Left);
            for (int i = 0; i < model.GenerateQuestion.Answers.Length; i++)
            {
                var answer = model.GenerateQuestion.Answers[i];
                AddWrappedDialogButton(answer.Name, 72f, 98f + i * 36f, 450f, () =>
                    RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.AnswerGenerateQuestion, CharacterGenerationMenu.None, null, null, answer.IntValue, 0, 0, out _));
            }
        }

        void BuildGenerateResult(CharacterGenerationViewModel model)
        {
            RectTransform imageBox = CreateBox("GeneratedClassImage", _contentRoot, 8f, 8f, 265f, 138f);
            CreateTextureImage("GeneratedClassImageTexture", imageBox, RequireClassImageSprite(model.GeneratedClass), preserveAspect: true);
            CreateWrappedText("ReflectT", _contentRoot, 8f, 152f, 265f, 40f, Label(model.GeneratedClassReflectText, "You have reflected upon your choices."), BitmapTextAlignment.Center);
            CreateText("ClassName", _contentRoot, 8f, 196f, 265f, 23f, BodyTextColor, BitmapTextAlignment.Center).Text = model.GeneratedClass?.Name ?? model.GeneratedClassName;
            AddDialogButton(Label(model.GeneratedClassBackText, model.BackText), 8f, 216f, 126f, () => RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.Back, out _));
            AddDialogButton(Label(model.GeneratedClassOkText, model.OkText), 146f, 216f, 126f, () => RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.AcceptGeneratedClass, out _));
        }

        void BuildBirthsign(CharacterGenerationViewModel model)
        {
            BuildBirthsignList(model.Birthsigns, 8f, 8f, 232f, 137f);
            RectTransform imageBox = CreateBox("BirthsignImageBox", _contentRoot, 248f, 8f, 263f, 137f);
            CreateTextureImage("BirthsignImage", imageBox, RequireTextureSprite(model.SelectedBirthsign?.ImagePath, "birthsign"), preserveAspect: true);
            RectTransform spellContent = CreateScrollableContentPane("SpellArea", _contentRoot, 8f, 160f, 507f, 170f, 166f);
            float y = 0f;
            y = AddSpellCategory(spellContent, Label(model.BirthsignAbilitiesLabel, "Abilities"), model.SelectedBirthsign?.Abilities, y, 495f, true);
            y = AddSpellCategory(spellContent, Label(model.BirthsignPowersLabel, "Powers"), model.SelectedBirthsign?.Powers, y, 495f, true);
            y = AddSpellCategory(spellContent, Label(model.BirthsignSpellsLabel, "Spells"), model.SelectedBirthsign?.Spells, y, 495f, true);
            SetScrollableContentHeight(spellContent, Mathf.Max(166f, y));
            AddDialogButton(Label(model.BackText, "Back"), 354f, 338f, 72f, () => RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.Back, out _));
            AddDialogButton(Label(model.BirthsignOkText, "OK"), 434f, 338f, 72f, () =>
                RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.ChooseBirthsign, CharacterGenerationMenu.None, model.BirthsignId, null, 0, 0, 1, out _));
        }

        void BuildReview(CharacterGenerationViewModel model)
        {
            RectTransform identityBox = CreateBox("IdentityBox", _contentRoot, 8f, 8f, 265f, 126f);
            AddReviewIdentityRow(identityBox, Label(model.ReviewNameLabel, "Name"), model.Name, null, 8f, () => RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.ReviewOpenName, out _));
            AddReviewIdentityRow(identityBox, Label(model.ReviewRaceLabel, "Race"), model.RaceName, model.RaceTooltipText, 37f, () => RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.ReviewOpenRace, out _));
            AddReviewIdentityRow(identityBox, Label(model.ReviewClassLabel, "Class"), model.ClassName, model.ClassTooltipText, 66f, () => RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.ReviewOpenClass, out _));
            AddReviewIdentityRow(identityBox, Label(model.ReviewBirthsignLabel, "Sign"), model.BirthsignName, model.BirthsignTooltipText, 95f, () => RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.ReviewOpenBirthsign, out _));

            RectTransform vitalsBox = CreateBox("VitalsBox", _contentRoot, 8f, 144f, 265f, 72f);
            CreateText("Health", vitalsBox, 8f, 8f, 249f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = $"{Label(model.ReviewHealthLabel, "Health")}  {model.ReviewStats?.HealthText}";
            CreateText("Magicka", vitalsBox, 8f, 27f, 249f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = $"{Label(model.ReviewMagickaLabel, "Magicka")}  {model.ReviewStats?.MagickaText}";
            CreateText("Fatigue", vitalsBox, 8f, 46f, 249f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = $"{Label(model.ReviewFatigueLabel, "Fatigue")}  {model.ReviewStats?.FatigueText}";

            RectTransform attrBox = CreateBox("Attributes", _contentRoot, 8f, 224f, 265f, 156f);
            if (model.ReviewStats != null)
            {
                for (int i = 0; i < model.ReviewStats.Attributes.Length; i++)
                {
                    var attr = model.ReviewStats.Attributes[i];
                    CreateText($"ReviewAttr{i}", attrBox, 8f, 4f + i * 18f, 249f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = $"{attr.Name}  {attr.Value}";
                    AddChildTooltipHit($"ReviewAttrTip{i}", attrBox, 8f, 4f + i * 18f, 249f, 18f, attr.TooltipText);
                }
            }

            RectTransform skillBox = CreateBox("Skills", _contentRoot, 281f, 8f, 244f, 372f);
            RectTransform skillContent = CreateScrollableContentPane("SkillView", skillBox, 8f, 6f, 232f, 362f, 362f);
            float y = 0f;
            y = AddReviewSkillRows(skillContent, Label(model.SkillClassMajorLabel, "Major Skills"), model.ReviewStats?.MajorSkills, y);
            y = AddReviewSkillRows(skillContent, Label(model.SkillClassMinorLabel, "Minor Skills"), model.ReviewStats?.MinorSkills, y);
            y = AddReviewSkillRows(skillContent, Label(model.SkillClassMiscLabel, "Misc Skills"), model.ReviewStats?.MiscSkills, y);
            y = AddSpellCategory(skillContent, Label(model.ReviewAbilitiesLabel, "Abilities"), model.ReviewSpells?.Abilities, y, 220f, false);
            y = AddSpellCategory(skillContent, Label(model.ReviewPowersLabel, "Powers"), model.ReviewSpells?.Powers, y, 220f, false);
            y = AddSpellCategory(skillContent, Label(model.ReviewSpellsLabel, "Spells"), model.ReviewSpells?.Spells, y, 220f, false);
            SetScrollableContentHeight(skillContent, Mathf.Max(362f, y));

            AddDialogButton(Label(model.BackText, "Back"), 370f, 388f, 70f, () => RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.Back, out _));
            AddDialogButton(Label(model.DoneText, "Done"), 448f, 388f, 70f, () => RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.AcceptReview, out _));
        }

        void CreateInfoBoxButtons((string Label, UnityEngine.Events.UnityAction Action)[] buttons)
        {
            float y = 58f;
            for (int i = 0; i < buttons.Length; i++)
            {
                float height = AddWrappedDialogButton(buttons[i].Label, 72f, y, 450f, buttons[i].Action);
                y += height + 12f;
            }
        }

        void BuildClassList(CharacterGenerationChoiceViewModel[] classes, float x, float y, float width, float height)
        {
            RectTransform box = CreateBox("ClassList", _contentRoot, x, y, width, height);
            RectTransform content = CreateScrollableListContent("ClassListRows", box, classes?.Length ?? 0, 18f, height);
            int count = classes?.Length ?? 0;
            for (int i = 0; i < count; i++)
            {
                var classChoice = classes[i];
                string label = classChoice.Selected ? $"> {classChoice.Name}" : classChoice.Name;
                AddChildTextButton($"Class{i}", content, label, 2f, i * 18f, width - 8f, 18f, () =>
                    RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.PickClass, CharacterGenerationMenu.None, classChoice.Id, null, 0, 0, 0, out _));
            }
        }

        void BuildBirthsignList(CharacterGenerationChoiceViewModel[] signs, float x, float y, float width, float height)
        {
            RectTransform box = CreateBox("BirthsignList", _contentRoot, x, y, width, height);
            RectTransform content = CreateScrollableListContent("BirthsignListRows", box, signs?.Length ?? 0, 18f, height);
            int count = signs?.Length ?? 0;
            for (int i = 0; i < count; i++)
            {
                var sign = signs[i];
                string label = sign.Selected ? $"> {sign.Name}" : sign.Name;
                AddChildTextButton($"Birth{i}", content, label, 2f, i * 18f, width - 8f, 18f, () =>
                    RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.ChooseBirthsign, CharacterGenerationMenu.None, sign.Id, null, 0, 0, 0, out _));
            }
        }

        RectTransform CreateScrollableListContent(string name, RectTransform box, int rowCount, float rowHeight, float height)
        {
            var viewport = RuntimeUiFactory.CreateStretchRect("Viewport", box);
            viewport.offsetMin = RuntimeClassicUiMetrics.Ui(new Vector2(2f, 2f));
            viewport.offsetMax = RuntimeClassicUiMetrics.Ui(new Vector2(-2f, -2f));
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = RuntimeUiFactory.CreateAnchoredRect(
                name,
                viewport,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(0f, Mathf.Max(height - 4f, rowCount * rowHeight))));
            content.pivot = new Vector2(0f, 1f);

            var scroll = box.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = RuntimeClassicUiMetrics.Ui(rowHeight);
            return content;
        }

        RectTransform CreateScrollableContentPane(
            string name,
            Transform parent,
            float x,
            float y,
            float width,
            float height,
            float contentHeight)
        {
            RectTransform box = parent == _contentRoot
                ? CreateBox(name, parent, x, y, width, height)
                : RuntimeUiFactory.CreateAnchoredRect(
                    name,
                    parent,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    RuntimeClassicUiMetrics.Ui(new Vector2(x, -y)),
                    RuntimeClassicUiMetrics.Ui(new Vector2(width, height)));
            box.pivot = new Vector2(0f, 1f);

            var viewport = RuntimeUiFactory.CreateStretchRect("Viewport", box);
            viewport.offsetMin = RuntimeClassicUiMetrics.Ui(new Vector2(2f, 2f));
            viewport.offsetMax = RuntimeClassicUiMetrics.Ui(new Vector2(-2f, -2f));
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = RuntimeUiFactory.CreateAnchoredRect(
                name + "Content",
                viewport,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(0f, Mathf.Max(height - 4f, contentHeight))));
            content.pivot = new Vector2(0f, 1f);

            var scroll = box.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = RuntimeClassicUiMetrics.Ui(18f);
            return content;
        }

        void SetScrollableContentHeight(RectTransform content, float height)
        {
            if (content == null)
                return;
            content.sizeDelta = new Vector2(content.sizeDelta.x, RuntimeClassicUiMetrics.Ui(Mathf.Max(1f, height)));
        }

        void BuildClassDetails(CharacterGenerationClassDetailViewModel detail, float x, float y, CharacterGenerationViewModel model)
        {
            if (detail == null)
                return;

            CreateText("SpecializationT", _contentRoot, x, y, 166f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = Label(model.ClassSpecializationLabel, "Specialization");
            CreateText("SpecializationName", _contentRoot, x, y + 18f, 166f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = detail.SpecializationName;
            AddTooltipHit("SpecializationTip", x, y + 18f, 166f, 18f, detail.SpecializationTooltipText);
            CreateText("FavoriteAttributesT", _contentRoot, x, y + 39f, 166f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = Label(model.ClassFavoredAttributesLabel, "Favorite Attributes");
            for (int i = 0; i < detail.FavoredAttributes.Length; i++)
            {
                CreateText($"FavAttr{i}", _contentRoot, x, y + 57f + i * 18f, 166f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = detail.FavoredAttributes[i];
                AddTooltipHit($"FavAttrTip{i}", x, y + 57f + i * 18f, 166f, 18f, GetString(detail.FavoredAttributeTooltips, i));
            }

            CreateText("MajorSkillT", _contentRoot, x + 166f, y, 162f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = Label(model.ClassMajorSkillsLabel, "Major Skills");
            CreateText("MinorSkillT", _contentRoot, x + 332f, y, 150f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = Label(model.ClassMinorSkillsLabel, "Minor Skills");
            for (int i = 0; i < 5; i++)
            {
                string major = i < detail.MajorSkills.Length ? detail.MajorSkills[i] : string.Empty;
                string minor = i < detail.MinorSkills.Length ? detail.MinorSkills[i] : string.Empty;
                CreateText($"MajorSkill{i}", _contentRoot, x + 166f, y + 18f + i * 18f, 166f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = major;
                CreateText($"MinorSkill{i}", _contentRoot, x + 332f, y + 18f + i * 18f, 150f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = minor;
                AddTooltipHit($"MajorSkillTip{i}", x + 166f, y + 18f + i * 18f, 166f, 18f, GetString(detail.MajorSkillTooltips, i));
                AddTooltipHit($"MinorSkillTip{i}", x + 332f, y + 18f + i * 18f, 150f, 18f, GetString(detail.MinorSkillTooltips, i));
            }
        }

        RuntimeUiTextInputView CreateInlineInput(string name, float x, float y, float width, float height, string value, string placeholder)
        {
            var input = RuntimeUiFactory.CreateBitmapInputField(name, _contentRoot, _theme, 1f, BodyTextColor, new Color(0f, 0f, 0f, 0.7f), placeholder);
            input.Root.anchorMin = new Vector2(0f, 1f);
            input.Root.anchorMax = new Vector2(0f, 1f);
            input.Root.pivot = new Vector2(0f, 1f);
            input.Root.anchoredPosition = RuntimeClassicUiMetrics.Ui(new Vector2(x, -y));
            input.Root.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(width, height));
            RuntimeUiFactory.SetBitmapInputDisplay(input, value, placeholder, BodyTextColor, MutedTextColor);
            input.InputField.onValueChanged.AddListener(text => RuntimeUiFactory.SetBitmapInputDisplay(input, text, placeholder, BodyTextColor, MutedTextColor));
            return input;
        }

        MorrowindButtonView AddSandTextButton(string name, string label, float x, float y, float width, UnityEngine.Events.UnityAction onClick)
        {
            var button = AddContentButton(name, label, x, y, width, onClick);
            button.Label.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Small);
            return button;
        }

        void ShowCustomOverlay(CharacterGenerationViewModel model, CustomClassOverlay overlay, int slot)
        {
            _customOverlay = overlay;
            _customOverlaySlot = slot;
            Clear(_contentRoot);
            Clear(_footerRoot);
            BuildCreateClass(model);
        }

        void BuildCustomOverlay(CharacterGenerationViewModel model)
        {
            switch (_customOverlay)
            {
                case CustomClassOverlay.Description:
                    BuildDescriptionOverlay(model);
                    break;
                case CustomClassOverlay.Specialization:
                    BuildSpecializationOverlay(model);
                    break;
                case CustomClassOverlay.Attribute:
                    BuildAttributeOverlay(model);
                    break;
                case CustomClassOverlay.Skill:
                    BuildSkillOverlay(model);
                    break;
            }
        }

        void BuildDescriptionOverlay(CharacterGenerationViewModel model)
        {
            RectTransform root = CreateOverlayRoot("DescriptionDialog", 244f, 248f);
            _customDescriptionInput = RuntimeUiFactory.CreateBitmapInputField("TextEdit", root, _theme, 1f, BodyTextColor, new Color(0f, 0f, 0f, 0.7f), string.Empty);
            _customDescriptionInput.Root.anchorMin = new Vector2(0f, 1f);
            _customDescriptionInput.Root.anchorMax = new Vector2(0f, 1f);
            _customDescriptionInput.Root.pivot = new Vector2(0f, 1f);
            _customDescriptionInput.Root.anchoredPosition = RuntimeClassicUiMetrics.Ui(new Vector2(10f, -10f));
            _customDescriptionInput.Root.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(218f, 190f));
            RuntimeUiFactory.SetBitmapInputDisplay(_customDescriptionInput, model.CustomClass.Description, string.Empty, BodyTextColor, MutedTextColor);
            _customDescriptionInput.InputField.lineType = InputField.LineType.MultiLineNewline;
            _customDescriptionInput.InputField.onValueChanged.AddListener(text => RuntimeUiFactory.SetBitmapInputDisplay(_customDescriptionInput, text, string.Empty, BodyTextColor, MutedTextColor));
            AddChildTextButton("DescriptionOk", root, Label(model.OkText, "OK"), 171f, 208f, 57f, 24f, () =>
            {
                RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.SetCustomClassDescription, CharacterGenerationMenu.None, null, _customDescriptionInput.InputField.text, 0, 0, 0, out _);
                ShowCustomOverlay(model, CustomClassOverlay.None, 0);
            });
        }

        void BuildSpecializationOverlay(CharacterGenerationViewModel model)
        {
            RectTransform root = CreateOverlayRoot("SpecializationDialog", 247f, 144f);
            CreateText("SpecLabel", root, 14f, 14f, 216f, 18f, BodyTextColor, BitmapTextAlignment.Center).Text = "Specialization";
            for (int i = 0; i < 3; i++)
            {
                int value = i;
                AddChildTextButton($"Spec{value}", root, SpecializationName(value), 14f, 42f + i * 18f, 216f, 18f, () =>
                {
                    RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.SetCustomClassSpecialization, CharacterGenerationMenu.None, null, null, value, 0, 0, out _);
                    _customOverlay = CustomClassOverlay.None;
                });
            }
            AddChildTextButton("SpecCancel", root, "Cancel", 158f, 102f, 72f, 24f, () => ShowCustomOverlay(model, CustomClassOverlay.None, 0));
        }

        void BuildAttributeOverlay(CharacterGenerationViewModel model)
        {
            RectTransform root = CreateOverlayRoot("AttributeDialog", 247f, 231f);
            CreateText("AttrLabel", root, 14f, 14f, 216f, 18f, BodyTextColor, BitmapTextAlignment.Center).Text = "Attributes";
            for (int i = 0; i < 8; i++)
            {
                int value = i;
                AddChildTextButton($"Attr{value}", root, AttributeName(value), 14f, 42f + i * 18f, 216f, 18f, () =>
                {
                    RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.SetCustomClassAttribute, CharacterGenerationMenu.None, null, null, _customOverlaySlot, value, 0, out _);
                    _customOverlay = CustomClassOverlay.None;
                });
            }
            AddChildTextButton("AttrCancel", root, "Cancel", 158f, 189f, 72f, 24f, () => ShowCustomOverlay(model, CustomClassOverlay.None, 0));
        }

        void BuildSkillOverlay(CharacterGenerationViewModel model)
        {
            RectTransform root = CreateOverlayRoot("SkillDialog", 487f, 275f);
            CreateText("SkillLabel", root, 17f, 14f, 457f, 18f, BodyTextColor, BitmapTextAlignment.Center).Text = "Skills";
            for (int i = 0; i < 27; i++)
            {
                int value = i;
                int column = i < 9 ? 0 : i < 18 ? 1 : 2;
                int row = column == 0 ? i : column == 1 ? i - 9 : i - 18;
                float x = 17f + column * 158f;
                AddChildTextButton($"Skill{value}", root, RuntimeContentMetadataResolver.ResolveSkillName(value), x, 50f + row * 18f, 150f, 18f, () =>
                {
                    RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.SetCustomClassSkill, CharacterGenerationMenu.None, null, null, _customOverlaySlot, value, 0, out _);
                    _customOverlay = CustomClassOverlay.None;
                });
            }
            AddChildTextButton("SkillCancel", root, "Cancel", 398f, 232f, 72f, 24f, () => ShowCustomOverlay(model, CustomClassOverlay.None, 0));
        }

        RectTransform CreateOverlayRoot(string name, float width, float height)
        {
            RectTransform root = RuntimeUiFactory.CreateAnchoredRect(
                name,
                _contentRoot,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                RuntimeClassicUiMetrics.Ui(new Vector2(width, height)));
            root.pivot = new Vector2(0.5f, 0.5f);
            var frame = RuntimeUiFactory.CreateBorderFrame("Frame", root, RuntimeUiFactory.ResolveThickFrame(_theme), new Color(0f, 0f, 0f, 0.96f));
            RuntimeUiFactory.Stretch(frame.Root);
            return root;
        }

        float AddReviewSkillRows(RectTransform parent, string title, StatsWindowSkillRow[] rows, float y)
        {
            if (rows == null || rows.Length == 0)
                return y;
            if (y > 0f)
                y = AddHorizontalSeparator(parent, y, 216f);
            CreateText(title + "Title", parent, 10f, y, 220f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = title;
            y += 18f;
            for (int i = 0; i < rows.Length; i++)
            {
                CreateText(title + i, parent, 10f, y, 170f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = rows[i].Name;
                CreateText(title + "Value" + i, parent, 178f, y, 40f, 18f, BodyTextColor, BitmapTextAlignment.Right).Text = rows[i].Value;
                AddChildTooltipHit($"{title}SkillTip{i}", parent, 10f, y, 208f, 18f, rows[i].TooltipText);
                y += 18f;
            }
            return y + 4f;
        }

        float AddSpellCategory(RectTransform parent, string title, CharacterGenerationSpellRowViewModel[] rows, float y, float width, bool showEffects)
        {
            if (rows == null || rows.Length == 0)
                return y;
            if (!showEffects && y > 0f)
                y = AddHorizontalSeparator(parent, y, width - 4f);
            CreateText(title + "SpellTitle", parent, 8f, y, width, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = title;
            y += 18f;
            for (int i = 0; i < rows.Length; i++)
            {
                CreateText(title + "Spell" + i, parent, 16f, y, width - 16f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = rows[i].Name;
                AddChildSpellTooltipHit($"{title}SpellTip{i}", parent, 16f, y, width - 16f, 18f, rows[i].SpellTooltip);
                y += 18f;
                if (showEffects)
                    y = AddSpellEffectRows(parent, rows[i].SpellTooltip?.Effects, y, width);
            }
            return y + 4f;
        }

        float AddSpellEffectRows(RectTransform parent, RuntimeSpellTooltipEffectRow[] effects, float y, float width)
        {
            if (effects == null || effects.Length == 0)
                return y;

            for (int i = 0; i < effects.Length; i++)
            {
                RuntimeSpellTooltipEffectRow effect = effects[i];
                if (string.IsNullOrWhiteSpace(effect?.Text))
                    continue;

                var icon = RuntimeUiFactory.CreateImage("EffectIcon" + i + "_" + y, parent, Color.white);
                icon.sprite = _iconService?.GetMagicEffectSprite(effect.IconPath);
                icon.color = icon.sprite == null ? new Color(1f, 1f, 1f, 0f) : Color.white;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.rectTransform.anchorMin = new Vector2(0f, 1f);
                icon.rectTransform.anchorMax = new Vector2(0f, 1f);
                icon.rectTransform.pivot = new Vector2(0f, 1f);
                icon.rectTransform.anchoredPosition = RuntimeClassicUiMetrics.Ui(new Vector2(30f, -y));
                icon.rectTransform.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(16f, 16f));

                var text = CreateText("EffectText" + i + "_" + y, parent, 50f, y, width - 58f, 24f, BodyTextColor, BitmapTextAlignment.Left);
                text.Text = effect.Text.Trim();
                text.WrapMode = BitmapTextWrapMode.Word;
                text.VerticalAlignment = BitmapTextVerticalAlignment.Top;
                float lineHeight = RuntimeClassicUiFontSizes.Body + 2f;
                float height = Mathf.Max(24f, CountWrappedLines(effect.Text, width - 58f) * lineHeight);
                text.rectTransform.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(width - 58f, height));
                y += height;
            }

            return y;
        }

        float AddHorizontalSeparator(RectTransform parent, float y, float width)
        {
            var line = RuntimeUiFactory.CreateImage("Separator" + y, parent, new Color(0.55f, 0.46f, 0.30f, 0.85f));
            line.raycastTarget = false;
            line.rectTransform.anchorMin = new Vector2(0f, 1f);
            line.rectTransform.anchorMax = new Vector2(0f, 1f);
            line.rectTransform.pivot = new Vector2(0f, 1f);
            line.rectTransform.anchoredPosition = RuntimeClassicUiMetrics.Ui(new Vector2(10f, -y));
            line.rectTransform.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(width, 1f));
            return y + 18f;
        }

        void AddReviewIdentityRow(RectTransform parent, string label, string value, string tooltip, float y, UnityEngine.Events.UnityAction action)
        {
            AddChildTextButton(label + "Button", parent, label, 8f, y, 72f, 23f, action);
            CreateText(label + "Text", parent, 97f, y + 2f, 161f, 18f, BodyTextColor, BitmapTextAlignment.Right).Text = value ?? string.Empty;
            AddChildTooltipHit(label + "Tooltip", parent, 97f, y + 2f, 161f, 18f, tooltip);
        }

        void AddDialogButton(string label, float x, float y, float width, UnityEngine.Events.UnityAction onClick)
            => AddContentButton(label + x + y, label, x, y, width, onClick);

        float AddWrappedDialogButton(string label, float x, float y, float width, UnityEngine.Events.UnityAction onClick)
        {
            label = label ?? string.Empty;
            float height = Mathf.Max(ButtonHeight, CountWrappedLines(label, width - 12f) * (RuntimeClassicUiFontSizes.Body + 2f) + 6f);
            var rect = RuntimeUiFactory.CreateAnchoredRect(
                label + x + y,
                _contentRoot,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                RuntimeClassicUiMetrics.Ui(new Vector2(x, -y)),
                RuntimeClassicUiMetrics.Ui(new Vector2(width, height)));
            rect.pivot = new Vector2(0f, 1f);
            MorrowindButtonView button = BuildButton(rect, label, onClick);
            button.Label.WrapMode = BitmapTextWrapMode.Word;
            button.Label.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            return height;
        }

        void AddChildTextButton(string name, Transform parent, string label, float x, float y, float width, float height, UnityEngine.Events.UnityAction onClick)
        {
            RectTransform rect = RuntimeUiFactory.CreateAnchoredRect(
                name,
                parent,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                RuntimeClassicUiMetrics.Ui(new Vector2(x, -y)),
                RuntimeClassicUiMetrics.Ui(new Vector2(width, height)));
            rect.pivot = new Vector2(0f, 1f);
            BuildButton(rect, label, onClick);
        }

        void ChooseClassPath(CharacterGenerationClassChoice choice)
            => RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.ChooseClassChoice, CharacterGenerationMenu.None, null, null, 0, 0, (byte)choice, out _);

        MorrowindButtonView AddContentButton(string name, string label, float x, float y, float width, UnityEngine.Events.UnityAction onClick)
        {
            var rect = RuntimeUiFactory.CreateAnchoredRect(
                name,
                _contentRoot,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                RuntimeClassicUiMetrics.Ui(new Vector2(x, -y)),
                RuntimeClassicUiMetrics.Ui(new Vector2(width, ButtonHeight)));
            rect.pivot = new Vector2(0f, 1f);
            return BuildButton(rect, label, onClick);
        }

        void AddFooterButton(string label, UnityEngine.Events.UnityAction onClick, int rightSlot)
        {
            float width = RuntimeClassicUiMetrics.Ui(88f);
            float spacing = RuntimeClassicUiMetrics.Ui(10f);
            var rect = RuntimeUiFactory.CreateAnchoredRect(
                label + "Footer",
                _footerRoot,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-(width + spacing) * rightSlot, RuntimeClassicUiMetrics.Ui(8f)),
                new Vector2(width, RuntimeClassicUiMetrics.Ui(ButtonHeight)));
            rect.pivot = new Vector2(1f, 0f);
            BuildButton(rect, label, onClick);
        }

        MorrowindButtonView BuildButton(RectTransform rect, string label, UnityEngine.Events.UnityAction onClick)
        {
            var button = RuntimeUiFactory.CreateMorrowindButton("Button", rect, _theme, label ?? string.Empty, 1f, BodyTextColor, ButtonColor);
            RuntimeUiFactory.Stretch(button.Root);
            button.Label.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            button.Label.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            button.Button.transition = Selectable.Transition.ColorTint;
            button.Button.onClick.AddListener(onClick);
            return button;
        }

        void ConfigureMainDialog(CharacterGenerationMenuView menu)
        {
            Vector2 size = menu switch
            {
                CharacterGenerationMenuView.Race => new Vector2(640f, 433f),
                CharacterGenerationMenuView.ClassChoice => new Vector2(545f, 265f),
                CharacterGenerationMenuView.ClassPick => new Vector2(491f, 316f),
                CharacterGenerationMenuView.ClassCreate => new Vector2(498f, 198f),
                CharacterGenerationMenuView.ClassGenerateQuestion => new Vector2(545f, 265f),
                CharacterGenerationMenuView.ClassGenerateResult => new Vector2(289f, 256f),
                CharacterGenerationMenuView.Birth => new Vector2(527f, 378f),
                CharacterGenerationMenuView.Review => new Vector2(541f, 428f),
                _ => new Vector2(DialogWidth, DialogHeight),
            };

            bool parityDialog = menu != CharacterGenerationMenuView.None;
            _dialogRoot.sizeDelta = RuntimeClassicUiMetrics.Ui(size);
            _client.offsetMin = RuntimeClassicUiMetrics.Ui(parityDialog ? Vector2.zero : new Vector2(Margin, Margin));
            _client.offsetMax = RuntimeClassicUiMetrics.Ui(parityDialog ? Vector2.zero : new Vector2(-Margin, -Margin));
            _title.gameObject.SetActive(false);
            _footerRoot.gameObject.SetActive(false);
            _contentRoot.anchorMin = Vector2.zero;
            _contentRoot.anchorMax = Vector2.one;
            _contentRoot.pivot = new Vector2(0f, 1f);
            _contentRoot.offsetMin = Vector2.zero;
            _contentRoot.offsetMax = Vector2.zero;
        }

        RectTransform CreateBox(string name, Transform parent, float x, float y, float width, float height)
        {
            var root = RuntimeUiFactory.CreateAnchoredRect(
                name,
                parent,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                RuntimeClassicUiMetrics.Ui(new Vector2(x, -y)),
                RuntimeClassicUiMetrics.Ui(new Vector2(width, height)));
            root.pivot = new Vector2(0f, 1f);
            var frame = RuntimeUiFactory.CreateBorderFrame("Frame", root, RuntimeUiFactory.ResolveThinFrame(_theme), new Color(0f, 0f, 0f, 0.70f));
            RuntimeUiFactory.Stretch(frame.Root);
            return root;
        }

        void CreateTextureImage(string name, RectTransform parent, Sprite sprite, bool preserveAspect)
        {
            var image = RuntimeUiFactory.CreateImage(name, parent, Color.white);
            image.sprite = sprite;
            image.preserveAspect = preserveAspect;
            image.raycastTarget = false;
            RuntimeUiFactory.Stretch(image.rectTransform);
            image.rectTransform.offsetMin = RuntimeClassicUiMetrics.Ui(new Vector2(2f, 2f));
            image.rectTransform.offsetMax = RuntimeClassicUiMetrics.Ui(new Vector2(-2f, -2f));
            image.rectTransform.localScale = new Vector3(1f, -1f, 1f);
        }

        Sprite RequireClassImageSprite(CharacterGenerationClassDetailViewModel detail)
        {
            string path = detail?.ImagePath;
            if (!string.IsNullOrWhiteSpace(path) && _iconService != null && _iconService.TryGetTextureSprite(path, out var sprite))
                return sprite;

            if (_iconService != null && _iconService.TryGetTextureSprite(@"textures\levelup\warrior.dds", out var fallback))
                return fallback;

            throw new InvalidOperationException($"[VVardenfell][CharGen] Missing class image texture '{path}' and OpenMW fallback 'textures\\levelup\\warrior.dds'.");
        }

        Sprite RequireTextureSprite(string path, string label)
        {
            if (_iconService == null)
                throw new InvalidOperationException($"[VVardenfell][CharGen] Cannot load {label} texture without a runtime icon service.");
            return _iconService.RequireTextureSprite(path, label);
        }

        void AddRaceCycleRow(string name, float y, string label, UnityEngine.Events.UnityAction previous, UnityEngine.Events.UnityAction next)
        {
            AddRaceArrow(name + "Prev", 8f, y + 4f, "<", previous);
            CreateText(name + "ChoiceT", _contentRoot, 25f, y, 205f, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = label;
            AddRaceArrow(name + "Next", 234f, y + 4f, ">", next);
        }

        void AddRaceArrow(string name, float x, float y, string label, UnityEngine.Events.UnityAction onClick)
        {
            RectTransform box = CreateBox(name + "Box", _contentRoot, x, y, 15f, 14f);
            var rect = RuntimeUiFactory.CreateAnchoredRect(
                name,
                box,
                Vector2.zero,
                Vector2.one,
                RuntimeClassicUiMetrics.Ui(new Vector2(2f, -2f)),
                RuntimeClassicUiMetrics.Ui(new Vector2(-2f, 2f)));
            rect.offsetMin = RuntimeClassicUiMetrics.Ui(new Vector2(2f, 2f));
            rect.offsetMax = RuntimeClassicUiMetrics.Ui(new Vector2(-2f, -2f));
            BuildButton(rect, label, onClick);
        }

        void AddRaceSlider(string name, float x, float y, float width)
        {
            RectTransform root = RuntimeUiFactory.CreateAnchoredRect(
                name,
                _contentRoot,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                RuntimeClassicUiMetrics.Ui(new Vector2(x, -y)),
                RuntimeClassicUiMetrics.Ui(new Vector2(width, 14f)));
            root.pivot = new Vector2(0f, 1f);
            var background = RuntimeUiFactory.CreateImage("Track", root, new Color(0f, 0f, 0f, 0.75f));
            RuntimeUiFactory.Stretch(background.rectTransform);
            var slider = root.gameObject.AddComponent<Slider>();
            slider.minValue = -180f;
            slider.maxValue = 180f;
            slider.value = _racePreviewAngleDegrees;
            var handle = RuntimeUiFactory.CreateImage("Handle", root, new Color(0.72f, 0.58f, 0.30f, 1f));
            handle.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            handle.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            handle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            handle.rectTransform.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(10f, 14f));
            slider.targetGraphic = handle;
            slider.handleRect = handle.rectTransform;
            slider.onValueChanged.AddListener(value => _racePreviewAngleDegrees = value);
        }

        void BuildRaceList(CharacterGenerationChoiceViewModel[] races, float x, float y, float width, float height)
        {
            RectTransform box = CreateBox("RaceList", _contentRoot, x, y, width, height);
            int count = races?.Length ?? 0;
            const float rowHeight = 14f;
            for (int i = 0; i < count; i++)
            {
                CharacterGenerationChoiceViewModel race = races[i];
                string label = race.Selected ? $"> {race.Name}" : race.Name;
                AddRaceButton($"Race{i}", box, label, 2f, 2f + i * rowHeight, width - 4f, rowHeight, () =>
                    RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.ChooseRace, CharacterGenerationMenu.None, race.Id, null, 0, 0, 0, out _));
            }
        }

        void BuildRaceSkills(CharacterGenerationRaceSkillBonusViewModel[] skills, float x, float y, float width)
        {
            int count = skills?.Length ?? 0;
            for (int i = 0; i < count; i++)
            {
                CharacterGenerationRaceSkillBonusViewModel skill = skills[i];
                string text = $"{skill.SkillName}  {skill.Bonus:+#;-#;0}";
                CreateText($"RaceSkill{i}", _contentRoot, x, y + i * 18f, width, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = text;
                AddTooltipHit($"RaceSkillTip{i}", x, y + i * 18f, width, 18f, skill.TooltipText);
            }
        }

        void BuildRacePowers(CharacterGenerationRacePowerViewModel[] powers, float x, float y, float width, float height)
        {
            int count = powers?.Length ?? 0;
            for (int i = 0; i < count; i++)
            {
                CharacterGenerationRacePowerViewModel power = powers[i];
                CreateText($"RacePower{i}", _contentRoot, x, y + i * 18f, width, 18f, BodyTextColor, BitmapTextAlignment.Left).Text = power.Name;
                AddTooltipHit($"RacePowerTip{i}", x, y + i * 18f, width, 18f, power.TooltipText);
            }
        }

        void AddTooltipHit(string name, float x, float y, float width, float height, string tooltip)
            => AddChildTooltipHit(name, _contentRoot, x, y, width, height, tooltip);

        void AddChildTooltipHit(string name, Transform parent, float x, float y, float width, float height, string tooltip)
        {
            if (string.IsNullOrWhiteSpace(tooltip))
                return;

            var hit = RuntimeUiFactory.CreateImage(name, parent, new Color(1f, 1f, 1f, 0.001f));
            hit.raycastTarget = true;
            hit.rectTransform.anchorMin = new Vector2(0f, 1f);
            hit.rectTransform.anchorMax = new Vector2(0f, 1f);
            hit.rectTransform.pivot = new Vector2(0f, 1f);
            hit.rectTransform.anchoredPosition = RuntimeClassicUiMetrics.Ui(new Vector2(x, -y));
            hit.rectTransform.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(width, height));
            RuntimeUiPopupUtility.SetTooltip(hit.gameObject, tooltip);
        }

        void AddChildSpellTooltipHit(string name, Transform parent, float x, float y, float width, float height, RuntimeSpellTooltipViewModel tooltip)
        {
            if (tooltip == null)
                return;

            var hit = RuntimeUiFactory.CreateImage(name, parent, new Color(1f, 1f, 1f, 0.001f));
            hit.raycastTarget = true;
            hit.rectTransform.anchorMin = new Vector2(0f, 1f);
            hit.rectTransform.anchorMax = new Vector2(0f, 1f);
            hit.rectTransform.pivot = new Vector2(0f, 1f);
            hit.rectTransform.anchoredPosition = RuntimeClassicUiMetrics.Ui(new Vector2(x, -y));
            hit.rectTransform.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(width, height));
            RuntimeUiPopupUtility.SetSpellTooltip(hit.gameObject, tooltip);
        }

        void AddRaceFooterButton(string label, float x, UnityEngine.Events.UnityAction onClick)
        {
            float width = Mathf.Max(label.Length * 9f + 18f, 42f);
            RectTransform rect = RuntimeUiFactory.CreateAnchoredRect(
                label + "RaceFooter",
                _contentRoot,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                RuntimeClassicUiMetrics.Ui(new Vector2(x, -397f)),
                RuntimeClassicUiMetrics.Ui(new Vector2(width, 23f)));
            rect.pivot = new Vector2(0f, 1f);
            BuildButton(rect, label, onClick);
        }

        void AddRaceButton(string name, Transform parent, string label, float x, float y, float width, float height, UnityEngine.Events.UnityAction onClick)
        {
            RectTransform rect = RuntimeUiFactory.CreateAnchoredRect(
                name,
                parent,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                RuntimeClassicUiMetrics.Ui(new Vector2(x, -y)),
                RuntimeClassicUiMetrics.Ui(new Vector2(width, height)));
            rect.pivot = new Vector2(0f, 1f);
            MorrowindButtonView button = BuildButton(rect, label, onClick);
            button.Label.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Small);
        }

        void SubmitRace(CharacterGenerationViewModel model)
            => RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.ChooseRace, CharacterGenerationMenu.None, model.RaceId, null, 0, 0, 1, out _);

        void ToggleGender(CharacterGenerationViewModel model)
            => RuntimeShellRequestBridge.TryCharacterGenerationAction(CharacterGenerationAction.AdjustGender, CharacterGenerationMenu.None, null, null, 0, 0, model.Male ? (byte)0 : (byte)1, out _);

        void SelectBodyPart(CharacterGenerationChoiceViewModel[] choices, int index, CharacterGenerationAction action)
        {
            if (choices == null || choices.Length == 0)
                return;

            int wrapped = ((index % choices.Length) + choices.Length) % choices.Length;
            RuntimeShellRequestBridge.TryCharacterGenerationAction(action, CharacterGenerationMenu.None, choices[wrapped].Id, null, 0, 0, 0, out _);
        }

        void AttachPreviewScroll(GameObject preview)
        {
            var trigger = preview.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.Scroll };
            entry.callback.AddListener(data =>
            {
                if (data is PointerEventData pointer)
                    _racePreviewAngleDegrees = Mathf.Repeat(_racePreviewAngleDegrees + pointer.scrollDelta.y * 18f + 180f, 360f) - 180f;
            });
            trigger.triggers.Add(entry);
        }

        static string Label(string value, string fallback)
            => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        static string GetString(string[] values, int index)
            => values != null && (uint)index < (uint)values.Length ? values[index] : null;

        static string IndexedLabel(string value, string fallback, int index, int count)
        {
            string label = Label(value, fallback);
            return count <= 0 || index < 0 ? label : $"{label} {index + 1}/{count}";
        }

        BitmapTextGraphic CreateText(string name, Transform parent, float x, float y, float width, float height, Color color, BitmapTextAlignment align)
        {
            var text = RuntimeUiFactory.CreateBitmapText(name, parent, _theme?.DefaultFont, 1f, color, align);
            text.PixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            text.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = new Vector2(0f, 1f);
            text.rectTransform.anchorMax = new Vector2(0f, 1f);
            text.rectTransform.pivot = new Vector2(0f, 1f);
            text.rectTransform.anchoredPosition = RuntimeClassicUiMetrics.Ui(new Vector2(x, -y));
            text.rectTransform.sizeDelta = RuntimeClassicUiMetrics.Ui(new Vector2(width, height));
            return text;
        }

        void CreateWrappedText(string name, Transform parent, float x, float y, float width, float height, string value, BitmapTextAlignment alignment)
        {
            var text = CreateText(name, parent, x, y, width, height, BodyTextColor, alignment);
            text.Text = value ?? string.Empty;
            text.WrapMode = BitmapTextWrapMode.Word;
            text.VerticalAlignment = BitmapTextVerticalAlignment.Top;
        }

        int CountWrappedLines(string text, float width)
        {
            if (_theme?.DefaultFont == null || _theme.DefaultFont.LineHeight <= 0f || string.IsNullOrWhiteSpace(text))
                return 1;

            float pixelHeight = RuntimeClassicUiMetrics.Ui(RuntimeClassicUiFontSizes.Body);
            float scale = pixelHeight / _theme.DefaultFont.LineHeight;
            float maxWidth = RuntimeClassicUiMetrics.Ui(Mathf.Max(1f, width));
            int lines = 0;
            string[] paragraphs = text.Replace("\r", string.Empty).Split('\n');
            for (int p = 0; p < paragraphs.Length; p++)
            {
                string paragraph = paragraphs[p];
                if (string.IsNullOrWhiteSpace(paragraph))
                {
                    lines++;
                    continue;
                }

                string[] words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0)
                {
                    lines++;
                    continue;
                }

                string currentLine = words[0];
                lines++;
                for (int i = 1; i < words.Length; i++)
                {
                    string candidate = currentLine.Length == 0 ? words[i] : currentLine + " " + words[i];
                    if (RuntimeUiFactory.MeasureLineWidth(_theme.DefaultFont, candidate, scale) <= maxWidth)
                    {
                        currentLine = candidate;
                    }
                    else
                    {
                        lines++;
                        currentLine = words[i];
                    }
                }
            }

            return Math.Max(1, lines);
        }

        static string SpecializationName(int value)
            => value switch { 0 => "Combat", 1 => "Magic", 2 => "Stealth", _ => value.ToString() };

        static string AttributeName(int value)
            => RuntimeContentMetadataResolver.ResolveAttributeName(value);

        static string BuildModelSignature(CharacterGenerationViewModel model)
        {
            if (model == null)
                return string.Empty;

            var builder = new StringBuilder(1024);
            builder.Append((int)model.Menu).Append('|')
                .Append(model.Name).Append('|')
                .Append(model.NameLabel).Append('|')
                .Append(model.NameButtonText).Append('|')
                .Append(model.RaceId).Append('|')
                .Append(model.RaceName).Append('|')
                .Append(model.RaceTooltipText).Append('|')
                .Append(model.ClassId).Append('|')
                .Append(model.ClassName).Append('|')
                .Append(model.ClassTooltipText).Append('|')
                .Append(model.BirthsignId).Append('|')
                .Append(model.BirthsignName).Append('|')
                .Append(model.BirthsignTooltipText).Append('|')
                .Append(model.Male).Append('|')
                .Append(model.HeadIndex).Append('|')
                .Append(model.HairIndex).Append('|')
                .Append(model.GenerateStep).Append('|')
                .Append(model.GeneratedClassName).Append('|')
                .Append(model.RaceAppearanceLabel).Append('|')
                .Append(model.RaceGenderLabel).Append('|')
                .Append(model.RaceFaceLabel).Append('|')
                .Append(model.RaceHairLabel).Append('|')
                .Append(model.RaceListLabel).Append('|')
                .Append(model.RaceSkillBonusLabel).Append('|')
                .Append(model.RaceSpecialsLabel).Append('|')
                .Append(model.RaceBackButtonText).Append('|')
                .Append(model.RaceOkButtonText).Append('|');
            builder.Append(model.BackText).Append('|')
                .Append(model.OkText).Append('|')
                .Append(model.NextText).Append('|')
                .Append(model.DoneText).Append('|')
                .Append(model.ClassChoiceGenerateText).Append('|')
                .Append(model.ClassChoicePickText).Append('|')
                .Append(model.ClassChoiceCreateText).Append('|')
                .Append(model.ClassSpecializationLabel).Append('|')
                .Append(model.ClassFavoredAttributesLabel).Append('|')
                .Append(model.ClassMajorSkillsLabel).Append('|')
                .Append(model.ClassMinorSkillsLabel).Append('|')
                .Append(model.SkillClassMajorLabel).Append('|')
                .Append(model.SkillClassMinorLabel).Append('|')
                .Append(model.SkillClassMiscLabel).Append('|')
                .Append(model.ClassOkText).Append('|')
                .Append(model.ClassDescriptionButtonText).Append('|')
                .Append(model.CustomClassNameLabel).Append('|')
                .Append(model.CustomClassDefaultName).Append('|')
                .Append(model.GeneratedClassReflectText).Append('|')
                .Append(model.GeneratedClassBackText).Append('|')
                .Append(model.GeneratedClassOkText).Append('|')
                .Append(model.BirthsignAbilitiesLabel).Append('|')
                .Append(model.BirthsignPowersLabel).Append('|')
                .Append(model.BirthsignSpellsLabel).Append('|')
                .Append(model.BirthsignOkText).Append('|');
            AppendChoices(builder, model.Races);
            AppendChoices(builder, model.Classes);
            AppendChoices(builder, model.Birthsigns);
            AppendChoices(builder, model.Heads);
            AppendChoices(builder, model.Hairs);
            AppendRaceSkills(builder, model.RaceSkillBonuses);
            AppendRacePowers(builder, model.RacePowers);
            AppendClassDetail(builder, model.SelectedClass);
            AppendClassDetail(builder, model.GeneratedClass);
            AppendBirthsignDetail(builder, model.SelectedBirthsign);
            AppendReviewSpells(builder, model.ReviewSpells);
            if (model.GenerateQuestion != null)
            {
                builder.Append(model.GenerateQuestion.Question).Append('|');
                AppendChoices(builder, model.GenerateQuestion.Answers);
            }
            if (model.CustomClass != null)
            {
                builder.Append(model.CustomClass.Name).Append('|')
                    .Append(model.CustomClass.Description).Append('|')
                    .Append(model.CustomClass.Specialization).Append('|')
                    .Append(model.CustomClass.SpecializationTooltipText).Append('|')
                    .Append(model.CustomClass.FavoredAttribute0).Append('|')
                    .Append(model.CustomClass.FavoredAttribute1).Append('|')
                    .Append(model.CustomClass.FavoredAttribute0TooltipText).Append('|')
                    .Append(model.CustomClass.FavoredAttribute1TooltipText).Append('|');
                AppendInts(builder, model.CustomClass.MajorSkills);
                AppendStrings(builder, model.CustomClass.MajorSkillTooltips);
                AppendInts(builder, model.CustomClass.MinorSkills);
                AppendStrings(builder, model.CustomClass.MinorSkillTooltips);
            }
            if (model.ReviewStats != null)
            {
                foreach (var attribute in model.ReviewStats.Attributes)
                    builder.Append(attribute.Name).Append(':').Append(attribute.Value).Append(':').Append(attribute.TooltipText).Append('|');
                AppendStatsSkillRows(builder, model.ReviewStats.MajorSkills);
                AppendStatsSkillRows(builder, model.ReviewStats.MinorSkills);
                AppendStatsSkillRows(builder, model.ReviewStats.MiscSkills);
            }

            return builder.ToString();
        }

        static void AppendChoices(StringBuilder builder, CharacterGenerationChoiceViewModel[] choices)
        {
            int count = choices?.Length ?? 0;
            builder.Append(count).Append('[');
            for (int i = 0; i < count; i++)
            {
                CharacterGenerationChoiceViewModel choice = choices[i];
                builder.Append(choice.Id).Append(':')
                    .Append(choice.Name).Append(':')
                    .Append(choice.Description).Append(':')
                    .Append(choice.TooltipText).Append(':')
                    .Append(choice.Selected).Append(':')
                    .Append(choice.IntValue).Append(';');
            }
            builder.Append(']');
        }

        static void AppendRaceSkills(StringBuilder builder, CharacterGenerationRaceSkillBonusViewModel[] skills)
        {
            int count = skills?.Length ?? 0;
            builder.Append(count).Append('[');
            for (int i = 0; i < count; i++)
                builder.Append(skills[i].SkillName).Append(':').Append(skills[i].Bonus).Append(';');
            builder.Append(']');
        }

        static void AppendRacePowers(StringBuilder builder, CharacterGenerationRacePowerViewModel[] powers)
        {
            int count = powers?.Length ?? 0;
            builder.Append(count).Append('[');
            for (int i = 0; i < count; i++)
                builder.Append(powers[i].Id).Append(':').Append(powers[i].Name).Append(':').Append(powers[i].TooltipText).Append(';');
            builder.Append(']');
        }

        static void AppendClassDetail(StringBuilder builder, CharacterGenerationClassDetailViewModel detail)
        {
            if (detail == null)
            {
                builder.Append("class:null|");
                return;
            }

            builder.Append(detail.Id).Append(':')
                .Append(detail.Name).Append(':')
                .Append(detail.Description).Append(':')
                .Append(detail.ImagePath).Append(':')
                .Append(detail.TooltipText).Append(':')
                .Append(detail.SpecializationName).Append('|');
            builder.Append(detail.SpecializationTooltipText).Append('|');
            AppendStrings(builder, detail.FavoredAttributes);
            AppendStrings(builder, detail.FavoredAttributeTooltips);
            AppendStrings(builder, detail.MajorSkills);
            AppendStrings(builder, detail.MajorSkillTooltips);
            AppendStrings(builder, detail.MinorSkills);
            AppendStrings(builder, detail.MinorSkillTooltips);
        }

        static void AppendBirthsignDetail(StringBuilder builder, CharacterGenerationBirthsignDetailViewModel detail)
        {
            if (detail == null)
            {
                builder.Append("birth:null|");
                return;
            }

            builder.Append(detail.Id).Append(':')
                .Append(detail.Name).Append(':')
                .Append(detail.Description).Append(':')
                .Append(detail.ImagePath).Append('|');
            AppendSpellRows(builder, detail.Abilities);
            AppendSpellRows(builder, detail.Powers);
            AppendSpellRows(builder, detail.Spells);
        }

        static void AppendReviewSpells(StringBuilder builder, CharacterGenerationReviewSpellRowsViewModel spells)
        {
            if (spells == null)
            {
                builder.Append("reviewspells:null|");
                return;
            }

            AppendSpellRows(builder, spells.Abilities);
            AppendSpellRows(builder, spells.Powers);
            AppendSpellRows(builder, spells.Spells);
        }

        static void AppendSpellRows(StringBuilder builder, CharacterGenerationSpellRowViewModel[] rows)
        {
            int count = rows?.Length ?? 0;
            builder.Append(count).Append('[');
            for (int i = 0; i < count; i++)
                builder.Append(rows[i].Id).Append(':').Append(rows[i].Name).Append(':').Append(rows[i].Type).Append(';');
            builder.Append(']');
        }

        static void AppendStatsSkillRows(StringBuilder builder, StatsWindowSkillRow[] rows)
        {
            int count = rows?.Length ?? 0;
            builder.Append(count).Append('[');
            for (int i = 0; i < count; i++)
                builder.Append(rows[i].Name).Append(':').Append(rows[i].Value).Append(':').Append(rows[i].TooltipText).Append(';');
            builder.Append(']');
        }

        static void AppendStrings(StringBuilder builder, string[] values)
        {
            int count = values?.Length ?? 0;
            builder.Append(count).Append('[');
            for (int i = 0; i < count; i++)
                builder.Append(values[i]).Append(';');
            builder.Append(']');
        }

        static void AppendInts(StringBuilder builder, int[] values)
        {
            int count = values?.Length ?? 0;
            builder.Append(count).Append('[');
            for (int i = 0; i < count; i++)
                builder.Append(values[i]).Append(';');
            builder.Append(']');
        }

        static void Clear(Transform transform)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(transform.GetChild(i).gameObject);
        }
    }
}
