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
        Action<int> _selectItem;
        Action<int> _rightClickItem;
        Func<int, bool> _isSelected;
        Action<Vector2> _dragPositionChanged;
        Func<bool> _hasHeldItem;
        int _itemIndex;
        bool _dragStarted;

        public void Initialize(
            int itemIndex,
            Action<int> selectItem,
            Action<int, InventoryItemClickContext> beginItemDrag,
            Action<int> rightClickItem,
            Func<int, bool> isSelected,
            Func<bool> hasHeldItem,
            Action<Vector2> dragPositionChanged)
        {
            _itemIndex = itemIndex;
            _selectItem = selectItem;
            _beginItemDrag = beginItemDrag;
            _rightClickItem = rightClickItem;
            _isSelected = isSelected;
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
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            if (_hasHeldItem != null && _hasHeldItem())
                return;

            if (_isSelected == null || !_isSelected(_itemIndex))
            {
                _selectItem?.Invoke(_itemIndex);
                return;
            }

            _beginItemDrag?.Invoke(_itemIndex, CaptureClickContext(eventData));
            if (_hasHeldItem != null && _hasHeldItem())
                _dragPositionChanged?.Invoke(eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_hasHeldItem != null && _hasHeldItem())
                _dragPositionChanged?.Invoke(eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _dragStarted = false;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_dragStarted)
                return;

            if (eventData.button == PointerEventData.InputButton.Right)
            {
                _rightClickItem?.Invoke(_itemIndex);
                return;
            }

            if (eventData.button == PointerEventData.InputButton.Left)
                _selectItem?.Invoke(_itemIndex);
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
