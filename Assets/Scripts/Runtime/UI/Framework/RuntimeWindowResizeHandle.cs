using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VVardenfell.Runtime.UI.Framework
{
    public sealed class RuntimeWindowResizeHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        RectTransform _target;
        RectTransform _viewport;
        Action _onResized;
        Vector2 _minimumSize;
        Vector2 _startSize;
        Vector2 _startPointer;

        public bool IsDragging { get; private set; }

        public void Initialize(RectTransform target, RectTransform viewport, Vector2 minimumSize, Action onResized)
        {
            _target = target;
            _viewport = viewport;
            _minimumSize = minimumSize;
            _onResized = onResized;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_target == null || _viewport == null)
                return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, eventData.position, eventData.pressEventCamera, out _startPointer))
                return;

            _startSize = _target.sizeDelta;
            IsDragging = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_target == null || _viewport == null)
                return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, eventData.position, eventData.pressEventCamera, out var currentPointer))
                return;

            Vector2 delta = currentPointer - _startPointer;
            float maxWidth = Mathf.Max(_minimumSize.x, _viewport.rect.width - _target.anchoredPosition.x);
            float maxHeight = Mathf.Max(_minimumSize.y, _viewport.rect.height + _target.anchoredPosition.y);
            float width = Mathf.Clamp(_startSize.x + delta.x, _minimumSize.x, maxWidth);
            float height = Mathf.Clamp(_startSize.y - delta.y, _minimumSize.y, maxHeight);
            _target.sizeDelta = new Vector2(width, height);
            _onResized?.Invoke();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            IsDragging = false;
        }
    }
}
