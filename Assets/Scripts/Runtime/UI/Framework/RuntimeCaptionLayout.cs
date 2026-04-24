using UnityEngine;

namespace VVardenfell.Runtime.UI.Framework
{
    /// <summary>
    /// Runtime caption-band layout helper. Mirrors the vanilla Morrowind caption shape:
    /// two filigree end-cap panels flank a dark backdrop sized to the title text, and the
    /// end-caps stretch flexibly to fill whatever caption width remains. When the title
    /// changes, the backdrop resizes to hug the new text and the filigree panels slide
    /// outward to keep filling the remaining space.
    ///
    /// Install one of these on the caption root. It observes the bound
    /// <see cref="BitmapTextGraphic.Text"/> and re-lays out on change. Captions rarely
    /// retitle (usually only on window re-open), so the check is essentially free.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RuntimeCaptionLayout : MonoBehaviour
    {
        RectTransform _leftFiligree;
        RectTransform _rightFiligree;
        RectTransform _backdrop;
        BitmapTextGraphic _titleText;
        float _horizontalPadding;
        float _minBackdropWidth;
        string _lastMeasuredText;
        float _lastBackdropWidth = -1f;

        /// <summary>
        /// Wire the caption parts. Both filigree panels must already be child rects of
        /// the caption root (anchored + stretched vertically); their horizontal anchors
        /// will be overwritten every time the title changes so callers can leave them at
        /// any initial value. <paramref name="horizontalPadding"/> is the gap on each
        /// side of the title text inside the backdrop (total backdrop width = text width
        /// + 2 * padding). <paramref name="minBackdropWidth"/> is a floor so the
        /// backdrop never collapses smaller than this even for very short titles.
        /// </summary>
        public void Bind(
            RectTransform leftFiligree,
            RectTransform rightFiligree,
            RectTransform backdrop,
            BitmapTextGraphic titleText,
            float horizontalPadding,
            float minBackdropWidth)
        {
            _leftFiligree = leftFiligree;
            _rightFiligree = rightFiligree;
            _backdrop = backdrop;
            _titleText = titleText;
            _horizontalPadding = horizontalPadding;
            _minBackdropWidth = minBackdropWidth;
            _lastMeasuredText = null;
            _lastBackdropWidth = -1f;
            UpdateLayout(force: true);
        }

        void OnEnable() => UpdateLayout(force: true);

        void LateUpdate()
        {
            if (_titleText == null)
                return;

            // Fast path: bail out unless the title text actually changed.
            string current = _titleText.Text ?? string.Empty;
            if (string.Equals(current, _lastMeasuredText))
                return;

            _lastMeasuredText = current;
            UpdateLayout(force: false);
        }

        void UpdateLayout(bool force)
        {
            if (_leftFiligree == null || _rightFiligree == null || _backdrop == null || _titleText == null)
                return;

            float textWidth = _titleText.PreferredWidth;
            float backdropWidth = Mathf.Max(_minBackdropWidth, textWidth + _horizontalPadding * 2f);

            if (!force && Mathf.Approximately(backdropWidth, _lastBackdropWidth))
                return;

            _lastBackdropWidth = backdropWidth;
            float halfWidth = backdropWidth * 0.5f;

            // Backdrop: centered on the caption, full vertical fill.
            _backdrop.anchorMin = new Vector2(0.5f, 0f);
            _backdrop.anchorMax = new Vector2(0.5f, 1f);
            _backdrop.pivot = new Vector2(0.5f, 0.5f);
            _backdrop.anchoredPosition = Vector2.zero;
            _backdrop.sizeDelta = new Vector2(backdropWidth, 0f);

            // Left filigree: anchored from caption.left to caption horizontal center,
            // offsetMax pulls its right edge in to meet the backdrop's left edge.
            _leftFiligree.anchorMin = new Vector2(0f, 0f);
            _leftFiligree.anchorMax = new Vector2(0.5f, 1f);
            _leftFiligree.pivot = new Vector2(0f, 0.5f);
            _leftFiligree.offsetMin = Vector2.zero;
            _leftFiligree.offsetMax = new Vector2(-halfWidth, 0f);

            // Right filigree: mirror of left.
            _rightFiligree.anchorMin = new Vector2(0.5f, 0f);
            _rightFiligree.anchorMax = new Vector2(1f, 1f);
            _rightFiligree.pivot = new Vector2(1f, 0.5f);
            _rightFiligree.offsetMin = new Vector2(halfWidth, 0f);
            _rightFiligree.offsetMax = Vector2.zero;
        }
    }
}
