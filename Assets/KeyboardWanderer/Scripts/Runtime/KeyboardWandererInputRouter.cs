using System;
using KeyboardWanderer.Gameplay;
using TMPro;
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
        public event Action<int> NarrativeChoiceRequested;
        public event Action<int> NarrativeChoiceMoveRequested;
        public event Action NarrativeChoiceConfirmRequested;
        public event Action<AbilityKind> AbilityRequested;
        public event Action PasteRequested;
        public event Action<int> PoiCycleRequested;
        public event Action SubmitRequested;
        public event Action InventoryRequested;
        public event Action QuestRequested;
        public event Action<Vector2> WorldClickRequested;
        private bool _narrativeChoiceMode;
        private bool _uiOverlayMode;

        public void SetNarrativeChoiceMode(bool active) => _narrativeChoiceMode = active;
        public void SetUiOverlayMode(bool active) => _uiOverlayMode = active;

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

            // TMP 입력창이 선택된 동안 문자키, 숫자키, Space, Enter는 모두
            // 텍스트 편집기가 소유한다. 선택지 모드가 이전 프레임에 켜져
            // 있더라도 W/S 이동이나 선택 확정으로 가로채지 않는다.
            if (IsTextInputFocused())
                return;

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                PauseRequested?.Invoke();
                return;
            }

            if (keyboard.iKey.wasPressedThisFrame)
            {
                InventoryRequested?.Invoke();
                return;
            }
            if (keyboard.qKey.wasPressedThisFrame)
            {
                QuestRequested?.Invoke();
                return;
            }
            if (_uiOverlayMode)
                return;

            // 숫자 키 1-4는 현재 대화 선택지만 고른다. 스킬 단축키로도
            // 해석하면 한 키 입력이 선택과 월드 행동을 동시에 발생시킬 수 있다.
            if (keyboard.digit1Key.wasPressedThisFrame) NarrativeChoiceRequested?.Invoke(0);
            if (keyboard.digit2Key.wasPressedThisFrame) NarrativeChoiceRequested?.Invoke(1);
            if (keyboard.digit3Key.wasPressedThisFrame) NarrativeChoiceRequested?.Invoke(2);
            if (keyboard.digit4Key.wasPressedThisFrame) NarrativeChoiceRequested?.Invoke(3);

            if (_narrativeChoiceMode)
            {
                if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame)
                    NarrativeChoiceMoveRequested?.Invoke(-1);
                if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame)
                    NarrativeChoiceMoveRequested?.Invoke(1);
                if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame ||
                    keyboard.spaceKey.wasPressedThisFrame)
                    NarrativeChoiceConfirmRequested?.Invoke();
                return;
            }

            bool ctrl = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
            if (!ctrl && keyboard.wKey.wasPressedThisFrame)
                AbilityRequested?.Invoke(AbilityKind.Move);
            if (!ctrl && keyboard.eKey.wasPressedThisFrame)
                AbilityRequested?.Invoke(AbilityKind.Copy);
            if (!ctrl && keyboard.rKey.wasPressedThisFrame)
                AbilityRequested?.Invoke(AbilityKind.Delete);
            if (!ctrl && keyboard.cKey.wasPressedThisFrame)
                AbilityRequested?.Invoke(AbilityKind.Connect);
            if (!ctrl && keyboard.zKey.wasPressedThisFrame)
                AbilityRequested?.Invoke(AbilityKind.Undo);

            if (keyboard.deleteKey.wasPressedThisFrame) AbilityRequested?.Invoke(AbilityKind.Delete);
            if (ctrl && keyboard.cKey.wasPressedThisFrame) AbilityRequested?.Invoke(AbilityKind.Copy);
            if (ctrl && keyboard.vKey.wasPressedThisFrame) PasteRequested?.Invoke();
            if (ctrl && keyboard.kKey.wasPressedThisFrame) AbilityRequested?.Invoke(AbilityKind.Connect);
            if (ctrl && keyboard.rKey.wasPressedThisFrame) AbilityRequested?.Invoke(AbilityKind.Restore);
            if (ctrl && keyboard.zKey.wasPressedThisFrame) AbilityRequested?.Invoke(AbilityKind.Undo);
            if (ctrl && keyboard.fKey.wasPressedThisFrame)
            {
                Debug.Log("[KW.Input] shortcut=Ctrl+F event=AbilityRequested ability=Search");
                AbilityRequested?.Invoke(AbilityKind.Search);
            }
            if (ctrl && keyboard.aKey.wasPressedThisFrame) AbilityRequested?.Invoke(AbilityKind.SelectAll);
            if (keyboard.leftBracketKey.wasPressedThisFrame) PoiCycleRequested?.Invoke(-1);
            if (keyboard.rightBracketKey.wasPressedThisFrame) PoiCycleRequested?.Invoke(1);
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                SubmitRequested?.Invoke();
        }

        internal static bool IsTextInputFocused()
        {
            GameObject selected = EventSystem.current?.currentSelectedGameObject;
            TMP_InputField input = selected != null ? selected.GetComponentInParent<TMP_InputField>() : null;
            return input != null && input.isActiveAndEnabled && input.interactable;
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
