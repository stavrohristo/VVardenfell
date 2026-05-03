using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VVardenfell.Runtime.UI.Shell
{
    sealed class RuntimeInventoryDropTarget : MonoBehaviour, IDropHandler, IPointerClickHandler
    {
        Action _drop;
        Func<bool> _hasHeldItem;

        public void Initialize(Action drop, Func<bool> hasHeldItem)
        {
            _drop = drop;
            _hasHeldItem = hasHeldItem;
        }

        public void OnDrop(PointerEventData eventData)
        {
            DropIfHeld();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                DropIfHeld();
        }

        void DropIfHeld()
        {
            if (_hasHeldItem != null && !_hasHeldItem())
                return;

            _drop?.Invoke();
        }
    }
}
