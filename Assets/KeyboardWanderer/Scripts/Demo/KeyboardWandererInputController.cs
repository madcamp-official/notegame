using System;
using KeyboardWanderer.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace KeyboardWanderer.Demo
{
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererInputController : MonoBehaviour
    {
        public event Action PauseRequested;
        public event Action<AbilityKind> AbilityRequested;
        public event Action PasteRequested;
        public event Action<int> PoiCycleRequested;
        public event Action SubmitRequested;
        public event Action<Vector2> WorldClickRequested;

        private void Update()
        {
            ReadKeyboard();
            ReadPointer();
        }

        private void ReadKeyboard()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                PauseRequested?.Invoke();
                return;
            }

            bool ctrl = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
            if (keyboard.digit1Key.wasPressedThisFrame || !ctrl && keyboard.wKey.wasPressedThisFrame)
                AbilityRequested?.Invoke(AbilityKind.Move);
            if (keyboard.digit2Key.wasPressedThisFrame || !ctrl && keyboard.eKey.wasPressedThisFrame)
                AbilityRequested?.Invoke(AbilityKind.Copy);
            if (keyboard.digit3Key.wasPressedThisFrame || !ctrl && keyboard.rKey.wasPressedThisFrame)
                AbilityRequested?.Invoke(AbilityKind.Delete);
            if (keyboard.digit4Key.wasPressedThisFrame || !ctrl && keyboard.cKey.wasPressedThisFrame)
                AbilityRequested?.Invoke(AbilityKind.Connect);
            if (keyboard.digit5Key.wasPressedThisFrame || !ctrl && keyboard.qKey.wasPressedThisFrame)
                AbilityRequested?.Invoke(AbilityKind.Restore);
            if (keyboard.digit6Key.wasPressedThisFrame || !ctrl && keyboard.zKey.wasPressedThisFrame)
                AbilityRequested?.Invoke(AbilityKind.Undo);

            if (keyboard.deleteKey.wasPressedThisFrame) AbilityRequested?.Invoke(AbilityKind.Delete);
            if (ctrl && keyboard.cKey.wasPressedThisFrame) AbilityRequested?.Invoke(AbilityKind.Copy);
            if (ctrl && keyboard.vKey.wasPressedThisFrame) PasteRequested?.Invoke();
            if (ctrl && keyboard.kKey.wasPressedThisFrame) AbilityRequested?.Invoke(AbilityKind.Connect);
            if (ctrl && keyboard.rKey.wasPressedThisFrame) AbilityRequested?.Invoke(AbilityKind.Restore);
            if (ctrl && keyboard.zKey.wasPressedThisFrame) AbilityRequested?.Invoke(AbilityKind.Undo);
            if (ctrl && keyboard.fKey.wasPressedThisFrame) AbilityRequested?.Invoke(AbilityKind.Search);
            if (ctrl && keyboard.aKey.wasPressedThisFrame) AbilityRequested?.Invoke(AbilityKind.SelectAll);
            if (keyboard.leftBracketKey.wasPressedThisFrame) PoiCycleRequested?.Invoke(-1);
            if (keyboard.rightBracketKey.wasPressedThisFrame) PoiCycleRequested?.Invoke(1);
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                SubmitRequested?.Invoke();
        }

        private void ReadPointer()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
                return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;
            WorldClickRequested?.Invoke(mouse.position.ReadValue());
        }
    }
}
