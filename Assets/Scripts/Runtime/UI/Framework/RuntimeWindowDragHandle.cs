using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VVardenfell.Runtime.UI.Framework
{
    public sealed class RuntimeWindowDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        RectTransform _target;
        RectTransform _viewport;
        Action _onDragged;
        Vector2 _pointerOffset;

        public bool IsDragging { get; private set; }

        public void Initialize(RectTransform target, RectTransform viewport, Action onDragged)
        {
            _target = target;
            _viewport = viewport;
            _onDragged = onDragged;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_target == null || _viewport == null)
                return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, eventData.position, eventData.pressEventCamera, out var localPoint))
                return;

            _pointerOffset = localPoint - _target.anchoredPosition;
            IsDragging = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_target == null || _viewport == null)
                return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, eventData.position, eventData.pressEventCamera, out var localPoint))
                return;

            Vector2 anchored = localPoint - _pointerOffset;
            float maxX = Mathf.Max(0f, _viewport.rect.width - _target.rect.width);
            float minY = Mathf.Min(0f, -(_viewport.rect.height - _target.rect.height));
            anchored.x = Mathf.Clamp(anchored.x, 0f, maxX);
            anchored.y = Mathf.Clamp(anchored.y, minY, 0f);
            _target.anchoredPosition = anchored;
            _onDragged?.Invoke();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            IsDragging = false;
        }
    }
}
