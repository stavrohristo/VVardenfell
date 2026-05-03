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
        Func<bool> _hasHeldItem;
        int _itemIndex;
        bool _dragStarted;

        public void Initialize(
            int itemIndex,
            Action<int, InventoryItemClickContext> beginItemDrag,
            Func<bool> hasHeldItem)
        {
            _itemIndex = itemIndex;
            _beginItemDrag = beginItemDrag;
            _hasHeldItem = hasHeldItem;
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

            _beginItemDrag?.Invoke(_itemIndex, CaptureClickContext());
        }

        public void OnDrag(PointerEventData eventData)
        {
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _dragStarted = false;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_dragStarted || eventData.button != PointerEventData.InputButton.Left)
                return;

            _beginItemDrag?.Invoke(_itemIndex, CaptureClickContext());
        }

        static InventoryItemClickContext CaptureClickContext()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return default;

            bool control = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
            bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            bool alt = keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed;
            return new InventoryItemClickContext(control, shift, alt);
        }
    }
}
