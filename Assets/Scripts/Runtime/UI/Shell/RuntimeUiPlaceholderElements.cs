using UnityEngine;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    static class RuntimeUiPlaceholderElements
    {
        public static void PopulatePlaceholderPanel(
            RectTransform parent,
            RuntimeUiTheme theme,
            RuntimeUiPlaceholderId placeholderId,
            string summaryText)
        {
            RuntimeUiPlaceholderDescriptor descriptor = RuntimeUiPlaceholderCatalog.Describe(placeholderId);

            var title = RuntimeUiFactory.CreateBitmapText(
                "PlaceholderTitle",
                parent,
                theme.DefaultFont,
                RuntimeClassicUiMetrics.WindowText(0.48f),
                new Color(0.94f, 0.82f, 0.53f),
                BitmapTextAlignment.Center);
            title.Text = descriptor.Title;
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.anchoredPosition = RuntimeClassicUiMetrics.Layout(new Vector2(0f, -10f));
            title.rectTransform.sizeDelta = new Vector2(0f, RuntimeClassicUiMetrics.Layout(24f));
            title.VerticalAlignment = BitmapTextVerticalAlignment.Middle;

            var summary = RuntimeUiFactory.CreateBitmapText(
                "PlaceholderSummary",
                parent,
                theme.DefaultFont,
                RuntimeClassicUiMetrics.WindowText(0.4f),
                new Color(0.76f, 0.73f, 0.66f),
                BitmapTextAlignment.Center);
            summary.WrapMode = BitmapTextWrapMode.Word;
            summary.Text = summaryText ?? descriptor.Body;
            RuntimeUiFactory.SetInsetText(
                summary.rectTransform,
                summary,
                10f,
                8f,
                -10f,
                -28f,
                BitmapTextVerticalAlignment.Middle);
        }
    }
}
