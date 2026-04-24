using UnityEngine;
using UnityEngine.UI;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    public sealed partial class OptionsWindowView
    {
        void BuildTabStrip()
        {
            float stripHeight = RuntimeClassicUiMetrics.Ui(TabStripHeight);
            float spacing = RuntimeClassicUiMetrics.Ui(TabSpacing);

            var strip = RuntimeUiFactory.CreateAnchorRect(
                "TabStrip",
                _window.Client,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -stripHeight),
                Vector2.zero);

            int count = k_Tabs.Length;
            float widthPerTab = 1f / count;
            for (int i = 0; i < count; i++)
            {
                var def = k_Tabs[i];
                float min = i * widthPerTab;
                float max = (i + 1) * widthPerTab;

                var tabRoot = RuntimeUiFactory.CreateAnchorRect(
                    $"Tab_{def.Id}",
                    strip,
                    new Vector2(min, 0f),
                    new Vector2(max, 1f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(i == 0 ? 0f : spacing * 0.5f, 0f),
                    new Vector2(i == count - 1 ? 0f : -spacing * 0.5f, 0f));

                var button = RuntimeUiFactory.CreateMorrowindButton(
                    "Button",
                    tabRoot,
                    _theme,
                    def.Label,
                    1f,
                    BodyTextColor,
                    TabCenterColor);
                RuntimeUiFactory.Stretch(button.Root);
                button.Label.PixelHeight = RuntimeClassicUiMetrics.Ui(TabTextPixelHeight);
                button.Label.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
                button.Button.transition = Selectable.Transition.None;

                TabId captured = def.Id;
                button.Button.onClick.AddListener(() => SetActiveTab(captured));
                _tabButtons.Add(new TabButtonView { Id = captured, View = button });
            }
        }

        void SetActiveTab(TabId tab)
        {
            _activeTab = tab;
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                bool selected = _tabButtons[i].Id == tab;
                _tabButtons[i].View.Frame.Center.color = selected ? TabSelectedCenterColor : TabCenterColor;
                _tabButtons[i].View.Label.color = selected ? SelectedTextColor : BodyTextColor;
            }

            foreach (var kvp in _tabContents)
                kvp.Value.gameObject.SetActive(kvp.Key == tab);
        }

        void BuildTabContents()
        {
            float stripHeight = RuntimeClassicUiMetrics.Ui(TabStripHeight);
            float stripGap = RuntimeClassicUiMetrics.Ui(TabStripBottomGap);
            float footerHeight = RuntimeClassicUiMetrics.Ui(FooterHeight);
            float footerGap = RuntimeClassicUiMetrics.Ui(FooterTopGap);

            var contentArea = RuntimeUiFactory.CreateAnchorRect(
                "TabContent",
                _window.Client,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, footerHeight + footerGap),
                new Vector2(0f, -(stripHeight + stripGap)));

            foreach (var def in k_Tabs)
            {
                var tabRoot = RuntimeUiFactory.CreateStretchRect($"Content_{def.Id}", contentArea);
                _tabContents[def.Id] = tabRoot;
            }

            BuildPreferencesTab(_tabContents[TabId.Preferences]);
            BuildAudioTab(_tabContents[TabId.Audio]);
            BuildVideoTab(_tabContents[TabId.Video]);
            BuildStubTab(_tabContents[TabId.Controls], "Controls rebinding", "input-rebinding");
            BuildStubTab(_tabContents[TabId.Scripts], "Lua script catalog", "script");
            BuildStubTab(_tabContents[TabId.Language], "Localization picker", "localization");
        }

        void BuildStubTab(RectTransform root, string topicLabel, string systemHint)
        {
            var text = RuntimeUiFactory.CreateBitmapText(
                "StubText",
                root,
                _theme?.DefaultFont,
                1f,
                SubtleTextColor,
                BitmapTextAlignment.Center);
            text.PixelHeight = RuntimeClassicUiMetrics.Ui(BodyPixelHeight);
            text.VerticalAlignment = BitmapTextVerticalAlignment.Middle;
            text.WrapMode = BitmapTextWrapMode.Word;
            text.Text = $"{topicLabel} is not yet implemented.\nComing with the {systemHint} phase.";
            text.raycastTarget = false;
            RuntimeUiFactory.Stretch(text.rectTransform);
        }
    }
}
