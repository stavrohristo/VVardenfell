using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.UI.Shell
{
    sealed class RuntimeInventoryItemDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        Action<int, InventoryItemClickContext> _beginItemDrag;
        Action<Vector2> _dragPositionChanged;
        Func<bool> _hasHeldItem;
        int _itemIndex;
        bool _dragStarted;

        public void Initialize(
            int itemIndex,
            Action<int, InventoryItemClickContext> beginItemDrag,
            Func<bool> hasHeldItem,
            Action<Vector2> dragPositionChanged)
        {
            _itemIndex = itemIndex;
            _beginItemDrag = beginItemDrag;
            _hasHeldItem = hasHeldItem;
            _dragPositionChanged = dragPositionChanged;
        }

        public void SetItemIndex(int itemIndex)
        {
            _itemIndex = itemIndex;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _dragStarted = true;
            if (_hasHeldItem != null && _hasHeldItem())
                return;

            _beginItemDrag?.Invoke(_itemIndex, CaptureClickContext(eventData));
            _dragPositionChanged?.Invoke(eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            _dragPositionChanged?.Invoke(eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _dragStarted = false;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_dragStarted || eventData.button != PointerEventData.InputButton.Left)
                return;

            _beginItemDrag?.Invoke(_itemIndex, CaptureClickContext(eventData));
        }

        static InventoryItemClickContext CaptureClickContext(PointerEventData eventData)
        {
            var keyboard = Keyboard.current;
            bool control = keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
            bool shift = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            bool alt = keyboard != null && (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);
            return new InventoryItemClickContext(control, shift, alt, eventData.position, true);
        }
    }
}
