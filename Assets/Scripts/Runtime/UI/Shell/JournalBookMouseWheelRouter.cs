using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VVardenfell.Runtime.UI.Shell
{
    sealed class JournalBookMouseWheelRouter : MonoBehaviour, IScrollHandler
    {
        Action _previous;
        Action _next;

        public void Initialize(Action previous, Action next)
        {
            _previous = previous;
            _next = next;
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (eventData.scrollDelta.y < 0f)
                _next?.Invoke();
            else if (eventData.scrollDelta.y > 0f)
                _previous?.Invoke();
        }
    }
}
