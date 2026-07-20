using System;
using KeyboardWanderer.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace KeyboardWanderer.Runtime
{
    /// <summary>
    /// Input System의 키보드와 포인터 입력을 게임 의미 이벤트로 변환한다.
    /// 게임 상태와 규칙은 알지 못하며 Pause, 스킬 선택, 실행, 월드 클릭 요청만 전달한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererInputRouter : MonoBehaviour
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

            // 숫자 키는 접근성용 보조 입력이고, 실제 키보드 스킬 조합은 Ctrl 계열을 우선한다.
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
            // UI 위 클릭은 Button이 처리하므로 월드 선택 이벤트로 중복 전달하지 않는다.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;
            WorldClickRequested?.Invoke(mouse.position.ReadValue());
        }
    }
}
