using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Game.Client.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_InputField))]
    public sealed class InputFocusTracker : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        public static int FocusedFieldCount { get; private set; }
        public static bool HasFocusedTextField => FocusedFieldCount > 0;
        // TMP_InputField can deselect itself while dispatching Return. Depending on
        // script execution order, gameplay input routers may observe that same key
        // later in the frame and submit a world action as well. Keep ownership only
        // for a frame where deselection actually happened under a Return key; a broad
        // frame latch would incorrectly swallow an unrelated key after a panel closes.
        public static bool OwnsTextInputThisFrame =>
            HasFocusedTextField || _lastReturnSubmitFrame == Time.frameCount;

        private static int _lastReturnSubmitFrame = -1;

        private bool _counted;

        public void OnSelect(BaseEventData eventData)
        {
            if (_counted) return;
            _counted = true;
            FocusedFieldCount++;
        }

        public void OnDeselect(BaseEventData eventData)
        {
            Release();
        }

        private void OnDisable()
        {
            Release();
        }

        private void Release()
        {
            if (!_counted) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.enterKey.isPressed || keyboard.enterKey.wasPressedThisFrame ||
                 keyboard.numpadEnterKey.isPressed || keyboard.numpadEnterKey.wasPressedThisFrame))
                _lastReturnSubmitFrame = Time.frameCount;
            _counted = false;
            FocusedFieldCount = Math.Max(0, FocusedFieldCount - 1);
        }
    }
}
