using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Game.Client.UI;
using KeyboardWanderer.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace KeyboardWanderer.Runtime
{
    /// <summary>
    /// Records game-window inputs as structured JSONL in addition to Player.log.
    /// Each launch gets its own append-only file under Application.persistentDataPath/InputAudit.
    /// </summary>
    public static class KeyboardWandererInputAudit
    {
        private static readonly object Gate = new object();
        private static readonly string SessionId = Guid.NewGuid().ToString("N");
        private static long _sequence;
        private static StreamWriter _writer;
        private static string _logPath;
        private static bool _writeFailureReported;

        public static long CurrentInputId { get; private set; }
        public static string CurrentSessionId => SessionId;
        public static string CurrentLogPath => _logPath;

        public static long Record(string device, string control, string phase, string semantic,
            string context = null, string value = null)
        {
            long inputId = Interlocked.Increment(ref _sequence);
            CurrentInputId = inputId;
            string json = "{\"sessionId\":\"" + SessionId + "\",\"inputId\":" + inputId +
                          ",\"utc\":\"" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                          "\",\"realtime\":" + Time.realtimeSinceStartupAsDouble.ToString("0.000000", CultureInfo.InvariantCulture) +
                          ",\"frame\":" + Time.frameCount + ",\"device\":\"" + Escape(device) +
                          "\",\"control\":\"" + Escape(control) + "\",\"phase\":\"" + Escape(phase) +
                          "\",\"semantic\":\"" + Escape(semantic) + "\",\"context\":\"" + Escape(context) +
                          "\",\"value\":\"" + Escape(value) + "\"}";
            Debug.Log("[KW.Input.Raw] " + json);
            Append(json);
            return inputId;
        }

        public static void RecordTextSubmission(string source, string text, string context)
        {
            Record("UI", source, "Submitted", "PlayerText", context,
                text == null ? string.Empty : "chars=" + text.Length + ";text=" + text);
        }

        private static void Append(string json)
        {
            try
            {
                lock (Gate)
                {
                    if (_writer == null)
                    {
                        string directory = Path.Combine(Application.persistentDataPath, "InputAudit");
                        Directory.CreateDirectory(directory);
                        _logPath = Path.Combine(directory, "input-audit-" +
                            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
                            SessionId + ".jsonl");
                        _writer = new StreamWriter(_logPath, true, new UTF8Encoding(false)) { AutoFlush = true };
                        Application.quitting += Close;
                        Debug.Log("[KW.Input.Audit] sessionId=" + SessionId + " path=" + _logPath);
                    }
                    _writer.WriteLine(json);
                }
            }
            catch (Exception error)
            {
                if (_writeFailureReported) return;
                _writeFailureReported = true;
                Debug.LogWarning("[KW.Input.Audit] result=write-failed error=" + error.GetType().Name);
            }
        }

        private static void Close()
        {
            lock (Gate)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        }
    }

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
        public event Action<Vector2Int> DirectionalMoveRequested;
        public event Action DirectionalMoveReleased;
        public event Action NaturalLanguageRequested;
        private bool _narrativeChoiceMode;
        private bool _narrativeOverlayMode;
        private bool _uiOverlayMode;
        private bool _suppressNarrativeKeyboardConfirmUntilRelease;
        private bool _suppressNarrativeGamepadConfirmUntilRelease;
        private bool _suppressKeyboardAfterOverlayUntilRelease;
        private bool _suppressGamepadAfterOverlayUntilRelease;
        private bool _suppressPointerAfterOverlayUntilRelease;
        private Vector2Int _heldMoveDirection;
        private float _nextDirectionalMoveAt;
        private bool _suppressDirectionalUntilRelease;
        private readonly List<RaycastResult> _pointerRaycastResults = new List<RaycastResult>(16);
        private PointerEventData _pointerEventData;
        private EventSystem _pointerEventSystem;
        private const float InitialMoveRepeatDelay = 0.18f;
        private const float HeldMoveRepeatInterval = 0.1f;

        public void SetNarrativeChoiceMode(bool active)
        {
            if (active && !_narrativeChoiceMode)
            {
                ResetDirectionalMovement(true);
                Keyboard keyboard = Keyboard.current;
                _suppressNarrativeKeyboardConfirmUntilRelease = keyboard != null &&
                    (keyboard.enterKey.isPressed || keyboard.enterKey.wasPressedThisFrame ||
                     keyboard.numpadEnterKey.isPressed || keyboard.numpadEnterKey.wasPressedThisFrame ||
                     keyboard.spaceKey.isPressed || keyboard.spaceKey.wasPressedThisFrame);
                Gamepad gamepad = Gamepad.current;
                _suppressNarrativeGamepadConfirmUntilRelease = gamepad != null &&
                    (gamepad.buttonSouth.isPressed || gamepad.buttonSouth.wasPressedThisFrame);
            }
            else if (!active)
            {
                _suppressNarrativeKeyboardConfirmUntilRelease = false;
                _suppressNarrativeGamepadConfirmUntilRelease = false;
            }
            _narrativeChoiceMode = active;
        }

        public void SetNarrativeOverlayMode(bool active)
        {
            _narrativeOverlayMode = active;
            if (active)
                ResetDirectionalMovement(true);
        }

        public void SetUiOverlayMode(bool active)
        {
            if (active && !_uiOverlayMode)
                ResetDirectionalMovement(true);
            else if (!active && _uiOverlayMode)
            {
                // EventSystem button callbacks may close Title/Pause/Settings before
                // this router's Update runs. The same Return, gamepad South, or mouse
                // press must not then confirm a selected world action behind the menu.
                // Keep each device quarantined until its activation gesture is fully
                // released; a fresh press remains responsive immediately afterwards.
                _suppressKeyboardAfterOverlayUntilRelease = true;
                _suppressGamepadAfterOverlayUntilRelease = true;
                _suppressPointerAfterOverlayUntilRelease = true;
                ResetDirectionalMovement(true);
            }
            _uiOverlayMode = active;
        }

        private void Update()
        {
            ReadKeyboard();
            ReadGamepad();
            ReadPointer();
        }

        private void ReadKeyboard()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            bool textInputFocused = IsTextInputFocused();
            RecordKeyboardEdges(keyboard, textInputFocused);

            // Escape는 텍스트 입력 상태에서도 항상 일시정지/닫기 동작으로 전달한다.
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                ResetDirectionalMovement(true);
                PauseRequested?.Invoke();
                return;
            }

            // TMP 입력창이 선택된 동안 문자키, 숫자키, Space, Enter는 모두
            // 텍스트 편집기가 소유한다. 선택지 모드가 이전 프레임에 켜져
            // 있더라도 W/S 이동이나 선택 확정으로 가로채지 않는다.
            if (textInputFocused)
            {
                ResetDirectionalMovement(true);
                return;
            }

            // Title/settings/pause/ending are owned exclusively by the EventSystem.
            // Check this before I/Q and every gameplay shortcut so a modal cannot
            // open an inventory panel or mutate the hidden HUD behind itself.
            if (_uiOverlayMode)
            {
                ResetDirectionalMovement(true);
                return;
            }

            if (_suppressKeyboardAfterOverlayUntilRelease)
            {
                if (!keyboard.anyKey.isPressed)
                    _suppressKeyboardAfterOverlayUntilRelease = false;
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
            if (keyboard.tKey.wasPressedThisFrame)
            {
                NaturalLanguageRequested?.Invoke();
                return;
            }

            if (_narrativeChoiceMode)
            {
                // A visible sealed skill choice and the matching action-bar shortcut
                // are two presentations of the same authoritative action. Dispatch
                // the semantic ability once so the controller can submit that exact
                // sealed choice instead of forcing pointer/number-key-only play.
                if (ReadNarrativeAbilityShortcut(keyboard))
                    return;
                // POI cycling is the explicit keyboard escape from a sealed narrative
                // choice into the equally valid world-movement path. The controller
                // closes the choice surface before Enter is allowed to submit travel.
                if (keyboard.leftBracketKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame)
                {
                    PoiCycleRequested?.Invoke(-1);
                    return;
                }
                if (keyboard.rightBracketKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame)
                {
                    PoiCycleRequested?.Invoke(1);
                    return;
                }
                // WASD always means world movement.  The controller dismisses this
                // optional choice surface before committing the tile, while arrow
                // keys remain available for explicit choice navigation.  Sharing W/S
                // between these two meanings made movement appear unresponsive.
                bool choiceCtrl = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
                ReadDirectionalMovement(keyboard, choiceCtrl);
                // Controller.Update can advance the final story page before this
                // router's Update runs in the same frame. Never reuse that held
                // Return/Space press to confirm a choice that only just became visible.
                if (_suppressNarrativeKeyboardConfirmUntilRelease)
                {
                    if (keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed ||
                        keyboard.spaceKey.isPressed || keyboard.enterKey.wasPressedThisFrame ||
                        keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)
                        return;
                    _suppressNarrativeKeyboardConfirmUntilRelease = false;
                }
                // 숫자 키 1-4는 현재 대화 선택지만 고른다. 선택 모드 밖에서
                // 처리하면 보이지 않는 이전 선택지를 다시 제출할 수 있다.
                if (keyboard.digit1Key.wasPressedThisFrame) NarrativeChoiceRequested?.Invoke(0);
                if (keyboard.digit2Key.wasPressedThisFrame) NarrativeChoiceRequested?.Invoke(1);
                if (keyboard.digit3Key.wasPressedThisFrame) NarrativeChoiceRequested?.Invoke(2);
                if (keyboard.digit4Key.wasPressedThisFrame) NarrativeChoiceRequested?.Invoke(3);
                if (keyboard.upArrowKey.wasPressedThisFrame)
                    NarrativeChoiceMoveRequested?.Invoke(-1);
                if (keyboard.downArrowKey.wasPressedThisFrame)
                    NarrativeChoiceMoveRequested?.Invoke(1);
                if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame ||
                    keyboard.spaceKey.wasPressedThisFrame)
                    NarrativeChoiceConfirmRequested?.Invoke();
                return;
            }

            bool ctrl = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
            ReadDirectionalMovement(keyboard, ctrl);
            if (!ctrl && keyboard.eKey.wasPressedThisFrame)
                AbilityRequested?.Invoke(AbilityKind.Copy);
            if (!ctrl && keyboard.rKey.wasPressedThisFrame)
                AbilityRequested?.Invoke(AbilityKind.Delete);
            if (!ctrl && keyboard.cKey.wasPressedThisFrame)
                AbilityRequested?.Invoke(AbilityKind.Connect);
            if (!ctrl && keyboard.xKey.wasPressedThisFrame)
                AbilityRequested?.Invoke(AbilityKind.Restore);
            if (!ctrl && keyboard.zKey.wasPressedThisFrame)
                AbilityRequested?.Invoke(AbilityKind.Undo);
            if (!ctrl && keyboard.fKey.wasPressedThisFrame)
                AbilityRequested?.Invoke(AbilityKind.Search);

            if (keyboard.deleteKey.wasPressedThisFrame) AbilityRequested?.Invoke(AbilityKind.Delete);
            if (ctrl && keyboard.cKey.wasPressedThisFrame) AbilityRequested?.Invoke(AbilityKind.Copy);
            if (ctrl && keyboard.vKey.wasPressedThisFrame) PasteRequested?.Invoke();
            if (ctrl && keyboard.kKey.wasPressedThisFrame) AbilityRequested?.Invoke(AbilityKind.Connect);
            if (ctrl && keyboard.rKey.wasPressedThisFrame) AbilityRequested?.Invoke(AbilityKind.Restore);
            if (ctrl && keyboard.zKey.wasPressedThisFrame) AbilityRequested?.Invoke(AbilityKind.Undo);
            if (ctrl && keyboard.fKey.wasPressedThisFrame) AbilityRequested?.Invoke(AbilityKind.Search);
            if (ctrl && keyboard.aKey.wasPressedThisFrame) AbilityRequested?.Invoke(AbilityKind.SelectAll);
            if (keyboard.leftBracketKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame)
                PoiCycleRequested?.Invoke(-1);
            if (keyboard.rightBracketKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame)
                PoiCycleRequested?.Invoke(1);
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                SubmitRequested?.Invoke();
        }

        private bool ReadNarrativeAbilityShortcut(Keyboard keyboard)
        {
            bool ctrl = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
            AbilityKind? ability = null;
            if ((!ctrl && keyboard.eKey.wasPressedThisFrame) || (ctrl && keyboard.cKey.wasPressedThisFrame))
                ability = AbilityKind.Copy;
            else if ((!ctrl && keyboard.rKey.wasPressedThisFrame) || keyboard.deleteKey.wasPressedThisFrame)
                ability = AbilityKind.Delete;
            else if ((!ctrl && keyboard.cKey.wasPressedThisFrame) || (ctrl && keyboard.kKey.wasPressedThisFrame))
                ability = AbilityKind.Connect;
            else if ((!ctrl && keyboard.xKey.wasPressedThisFrame) || (ctrl && keyboard.rKey.wasPressedThisFrame))
                ability = AbilityKind.Restore;
            else if ((!ctrl && keyboard.zKey.wasPressedThisFrame) || (ctrl && keyboard.zKey.wasPressedThisFrame))
                ability = AbilityKind.Undo;
            else if ((!ctrl && keyboard.fKey.wasPressedThisFrame) || (ctrl && keyboard.fKey.wasPressedThisFrame))
                ability = AbilityKind.Search;
            else if (ctrl && keyboard.aKey.wasPressedThisFrame)
                ability = AbilityKind.SelectAll;

            if (!ability.HasValue)
                return false;
            AbilityRequested?.Invoke(ability.Value);
            return true;
        }

        private void ReadGamepad()
        {
            Gamepad gamepad = Gamepad.current;
            if (gamepad == null)
                return;
            string context = InputContext(IsTextInputFocused());
            RecordButtonEdge(gamepad.startButton, "Start", context);
            RecordButtonEdge(gamepad.selectButton, "Select", context);
            RecordButtonEdge(gamepad.buttonSouth, "South", context);
            RecordButtonEdge(gamepad.buttonNorth, "North", context);
            RecordButtonEdge(gamepad.buttonEast, "East", context);
            RecordButtonEdge(gamepad.buttonWest, "West", context);
            RecordButtonEdge(gamepad.dpad.up, "DpadUp", context);
            RecordButtonEdge(gamepad.dpad.down, "DpadDown", context);
            RecordButtonEdge(gamepad.dpad.left, "DpadLeft", context);
            RecordButtonEdge(gamepad.dpad.right, "DpadRight", context);
            RecordButtonEdge(gamepad.leftStick.up, "LeftStickUp", context);
            RecordButtonEdge(gamepad.leftStick.down, "LeftStickDown", context);
            RecordButtonEdge(gamepad.leftStick.left, "LeftStickLeft", context);
            RecordButtonEdge(gamepad.leftStick.right, "LeftStickRight", context);
            if (gamepad.startButton.wasPressedThisFrame)
            {
                ResetDirectionalMovement(true);
                PauseRequested?.Invoke();
                return;
            }
            if (_uiOverlayMode || IsTextInputFocused() || !_narrativeChoiceMode)
                return;

            if (_suppressGamepadAfterOverlayUntilRelease)
            {
                bool activationHeld = gamepad.buttonSouth.isPressed || gamepad.buttonNorth.isPressed ||
                                      gamepad.buttonEast.isPressed || gamepad.buttonWest.isPressed ||
                                      gamepad.startButton.isPressed || gamepad.selectButton.isPressed ||
                                      gamepad.dpad.IsPressed() || gamepad.leftStick.ReadValue().sqrMagnitude > 0.01f;
                if (!activationHeld)
                    _suppressGamepadAfterOverlayUntilRelease = false;
                return;
            }

            if (_suppressNarrativeGamepadConfirmUntilRelease)
            {
                if (gamepad.buttonSouth.isPressed || gamepad.buttonSouth.wasPressedThisFrame)
                    return;
                _suppressNarrativeGamepadConfirmUntilRelease = false;
            }

            // Choice buttons deliberately use Navigation.None so the EventSystem cannot
            // dispatch the same D-pad/submit gesture a second time. The router owns this
            // small gamepad surface just as it owns keyboard W/S and Return.
            if (gamepad.dpad.up.wasPressedThisFrame || gamepad.leftStick.up.wasPressedThisFrame)
                NarrativeChoiceMoveRequested?.Invoke(-1);
            if (gamepad.dpad.down.wasPressedThisFrame || gamepad.leftStick.down.wasPressedThisFrame)
                NarrativeChoiceMoveRequested?.Invoke(1);
            if (gamepad.buttonSouth.wasPressedThisFrame)
                NarrativeChoiceConfirmRequested?.Invoke();
        }

        private void ReadDirectionalMovement(Keyboard keyboard, bool ctrl)
        {
            bool anyDirectionPressed = keyboard.wKey.isPressed || keyboard.sKey.isPressed ||
                                       keyboard.aKey.isPressed || keyboard.dKey.isPressed;
            if (_suppressDirectionalUntilRelease)
            {
                if (!anyDirectionPressed)
                {
                    _suppressDirectionalUntilRelease = false;
                    ResetDirectionalMovement(false);
                }
                return;
            }

            if (ctrl)
            {
                ResetDirectionalMovement(anyDirectionPressed);
                return;
            }

            Vector2Int direction = Vector2Int.zero;
            bool newlyPressed = false;
            // 마지막으로 누른 방향을 우선해 포켓몬식 4방향 이동을 유지한다.
            if (keyboard.wKey.wasPressedThisFrame) { direction = Vector2Int.up; newlyPressed = true; }
            else if (keyboard.sKey.wasPressedThisFrame) { direction = Vector2Int.down; newlyPressed = true; }
            else if (keyboard.aKey.wasPressedThisFrame) { direction = Vector2Int.left; newlyPressed = true; }
            else if (keyboard.dKey.wasPressedThisFrame) { direction = Vector2Int.right; newlyPressed = true; }
            else if (_heldMoveDirection == Vector2Int.up && keyboard.wKey.isPressed) direction = Vector2Int.up;
            else if (_heldMoveDirection == Vector2Int.down && keyboard.sKey.isPressed) direction = Vector2Int.down;
            else if (_heldMoveDirection == Vector2Int.left && keyboard.aKey.isPressed) direction = Vector2Int.left;
            else if (_heldMoveDirection == Vector2Int.right && keyboard.dKey.isPressed) direction = Vector2Int.right;
            else if (keyboard.wKey.isPressed) direction = Vector2Int.up;
            else if (keyboard.sKey.isPressed) direction = Vector2Int.down;
            else if (keyboard.aKey.isPressed) direction = Vector2Int.left;
            else if (keyboard.dKey.isPressed) direction = Vector2Int.right;

            if (direction == Vector2Int.zero)
            {
                bool wasHeld = _heldMoveDirection != Vector2Int.zero;
                _heldMoveDirection = Vector2Int.zero;
                _nextDirectionalMoveAt = 0f;
                if (wasHeld) DirectionalMoveReleased?.Invoke();
                return;
            }

            float now = Time.unscaledTime;
            bool changedDirection = direction != _heldMoveDirection;
            if (newlyPressed || changedDirection)
            {
                _heldMoveDirection = direction;
                _nextDirectionalMoveAt = now + InitialMoveRepeatDelay;
                DirectionalMoveRequested?.Invoke(direction);
            }
            else if (now >= _nextDirectionalMoveAt)
            {
                _nextDirectionalMoveAt = now + HeldMoveRepeatInterval;
                KeyboardWandererInputAudit.Record("Keyboard", DirectionName(direction), "Repeated",
                    "DirectionalMoveRequested", InputContext(false), direction.ToString());
                DirectionalMoveRequested?.Invoke(direction);
            }
        }

        private void RecordKeyboardEdges(Keyboard keyboard, bool textInputFocused)
        {
            string context = InputContext(textInputFocused);
            RecordKeyEdge(keyboard.escapeKey, "Escape", context);
            RecordKeyEdge(keyboard.wKey, "W", context);
            RecordKeyEdge(keyboard.aKey, "A", context);
            RecordKeyEdge(keyboard.sKey, "S", context);
            RecordKeyEdge(keyboard.dKey, "D", context);
            RecordKeyEdge(keyboard.eKey, "E", context);
            RecordKeyEdge(keyboard.rKey, "R", context);
            RecordKeyEdge(keyboard.cKey, "C", context);
            RecordKeyEdge(keyboard.xKey, "X", context);
            RecordKeyEdge(keyboard.zKey, "Z", context);
            RecordKeyEdge(keyboard.fKey, "F", context);
            RecordKeyEdge(keyboard.iKey, "I", context);
            RecordKeyEdge(keyboard.qKey, "Q", context);
            RecordKeyEdge(keyboard.tKey, "T", context);
            RecordKeyEdge(keyboard.vKey, "V", context);
            RecordKeyEdge(keyboard.kKey, "K", context);
            RecordKeyEdge(keyboard.deleteKey, "Delete", context);
            RecordKeyEdge(keyboard.enterKey, "Enter", context);
            RecordKeyEdge(keyboard.numpadEnterKey, "NumpadEnter", context);
            RecordKeyEdge(keyboard.spaceKey, "Space", context);
            RecordKeyEdge(keyboard.digit1Key, "1", context);
            RecordKeyEdge(keyboard.digit2Key, "2", context);
            RecordKeyEdge(keyboard.digit3Key, "3", context);
            RecordKeyEdge(keyboard.digit4Key, "4", context);
            RecordKeyEdge(keyboard.leftArrowKey, "LeftArrow", context);
            RecordKeyEdge(keyboard.rightArrowKey, "RightArrow", context);
            RecordKeyEdge(keyboard.upArrowKey, "UpArrow", context);
            RecordKeyEdge(keyboard.downArrowKey, "DownArrow", context);
            RecordKeyEdge(keyboard.leftBracketKey, "LeftBracket", context);
            RecordKeyEdge(keyboard.rightBracketKey, "RightBracket", context);
            RecordKeyEdge(keyboard.leftCtrlKey, "LeftCtrl", context);
            RecordKeyEdge(keyboard.rightCtrlKey, "RightCtrl", context);
        }

        private static void RecordKeyEdge(KeyControl key, string control, string context)
        {
            if (key == null) return;
            if (key.wasPressedThisFrame)
                KeyboardWandererInputAudit.Record("Keyboard", control, "Pressed", "RawKey", context);
            if (key.wasReleasedThisFrame)
                KeyboardWandererInputAudit.Record("Keyboard", control, "Released", "RawKey", context);
        }

        private static void RecordButtonEdge(ButtonControl button, string control, string context)
        {
            if (button == null) return;
            if (button.wasPressedThisFrame)
                KeyboardWandererInputAudit.Record("Gamepad", control, "Pressed", "RawButton", context);
            if (button.wasReleasedThisFrame)
                KeyboardWandererInputAudit.Record("Gamepad", control, "Released", "RawButton", context);
        }

        private string InputContext(bool textInputFocused)
        {
            return "textFocused=" + textInputFocused + ";uiOverlay=" + _uiOverlayMode +
                   ";narrativeOverlay=" + _narrativeOverlayMode +
                   ";choiceMode=" + _narrativeChoiceMode;
        }

        private static string DirectionName(Vector2Int direction)
        {
            if (direction == Vector2Int.up) return "W";
            if (direction == Vector2Int.down) return "S";
            if (direction == Vector2Int.left) return "A";
            if (direction == Vector2Int.right) return "D";
            return "Unknown";
        }

        private void ResetDirectionalMovement(bool requireRelease)
        {
            bool wasHeld = _heldMoveDirection != Vector2Int.zero;
            _heldMoveDirection = Vector2Int.zero;
            _nextDirectionalMoveAt = 0f;
            if (requireRelease)
                _suppressDirectionalUntilRelease = true;
            if (wasHeld) DirectionalMoveReleased?.Invoke();
        }

        internal static bool IsTextInputFocused()
        {
            if (InputFocusTracker.OwnsTextInputThisFrame)
                return true;

            // EventSystem이 InputField를 "selected"로 유지하고 있어도
            // 실제로 텍스트 커서가 활성화(isFocused)된 경우에만 차단합니다.
            // isActiveAndEnabled+interactable만 보면 대화 패널이 열린 동안 WASD가 계속 막힙니다.
            GameObject selected = EventSystem.current?.currentSelectedGameObject;
            TMP_InputField input = selected != null ? selected.GetComponentInParent<TMP_InputField>() : null;
            return input != null && input.isFocused;
        }

        private void ReadPointer()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;
            if (mouse.leftButton.wasPressedThisFrame)
                KeyboardWandererInputAudit.Record("Mouse", "LeftButton", "Pressed", "Pointer",
                    InputContext(IsTextInputFocused()), mouse.position.ReadValue().ToString());
            if (mouse.leftButton.wasReleasedThisFrame)
                KeyboardWandererInputAudit.Record("Mouse", "LeftButton", "Released", "Pointer",
                    InputContext(IsTextInputFocused()), mouse.position.ReadValue().ToString());
            if (_uiOverlayMode || _narrativeOverlayMode)
                return;
            if (_suppressPointerAfterOverlayUntilRelease)
            {
                if (!mouse.leftButton.isPressed)
                    _suppressPointerAfterOverlayUntilRelease = false;
                return;
            }
            if (!mouse.leftButton.wasPressedThisFrame)
                return;
            Vector2 position = mouse.position.ReadValue();
            // IsPointerOverGameObject() reads the EventSystem's previous hover cache.
            // A fast move+click can therefore dispatch a world click before the UI
            // module sees the current pointer position. Raycast the current position
            // synchronously so every visible uGUI control owns its click atomically.
            if (IsPointerOverUi(position))
                return;
            WorldClickRequested?.Invoke(position);
        }

        private bool IsPointerOverUi(Vector2 position)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
                return false;
            if (_pointerEventData == null || _pointerEventSystem != eventSystem)
            {
                _pointerEventSystem = eventSystem;
                _pointerEventData = new PointerEventData(eventSystem) { pointerId = -1 };
            }
            _pointerEventData.position = position;
            _pointerEventData.delta = Vector2.zero;
            _pointerRaycastResults.Clear();
            eventSystem.RaycastAll(_pointerEventData, _pointerRaycastResults);
            for (int i = 0; i < _pointerRaycastResults.Count; i++)
            {
                RaycastResult result = _pointerRaycastResults[i];
                if (result.gameObject != null && result.module is GraphicRaycaster)
                {
                    _pointerRaycastResults.Clear();
                    return true;
                }
            }
            _pointerRaycastResults.Clear();
            return false;
        }
    }
}
