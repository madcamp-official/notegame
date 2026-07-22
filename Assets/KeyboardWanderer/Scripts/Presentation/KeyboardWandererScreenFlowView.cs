using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace KeyboardWanderer.Demo
{
    /// <summary>
    /// Authored UI 루트가 다섯 화면의 활성 상태만 관리한다.
    /// 개별 화면의 텍스트와 버튼은 각 화면 View가 소유한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererScreenFlowView : MonoBehaviour
    {
        [SerializeField] private GameObject titleScreen;
        [SerializeField] private GameObject gameHud;
        [SerializeField] private GameObject settingsScreen;
        [SerializeField] private GameObject pauseScreen;
        [SerializeField] private GameObject endingScreen;
        private readonly Dictionary<GameObject, GameObject> _lastSelectionByScreen =
            new Dictionary<GameObject, GameObject>();
        private GameObject _activeNavigationScreen;
        private Selectable[] _activeSelectables = System.Array.Empty<Selectable>();
        private KeyboardWandererButtonStateView[] _activeSelectionVisuals =
            System.Array.Empty<KeyboardWandererButtonStateView>();
        private GameObject _lastVisualSelection;
        private GameObject _pendingNavigationScreen;
        private GameObject _pendingNavigationOrigin;
        private MoveDirection _pendingNavigationDirection = MoveDirection.None;
        private int _pendingNavigationFrame = -1;

        public bool IsReady => titleScreen != null && gameHud != null && settingsScreen != null &&
                               pauseScreen != null && endingScreen != null;

        private void OnEnable()
        {
            // Raw state events preserve a very short down/up pair even when the UI
            // Navigate action has already returned to zero before module.Process.
            InputSystem.onEvent += OnInputEvent;
        }

        private void OnDisable()
        {
            InputSystem.onEvent -= OnInputEvent;
            ClearPendingNavigationFallback();
        }

        public void Present(bool title, bool settings, bool playing, bool paused, bool ended)
        {
            // Settings opened from pause keep the simulation paused, but only the
            // topmost modal may remain visible or receive navigation focus.
            GameObject nextNavigationScreen = ended && playing ? endingScreen :
                settings ? settingsScreen : paused && playing ? pauseScreen : title ? titleScreen : null;
            RememberCurrentSelection();
            SetActive(titleScreen, title);
            SetActive(gameHud, playing);
            SetActive(settingsScreen, settings);
            SetActive(pauseScreen, playing && paused && !settings);
            SetActive(endingScreen, playing && ended);
            if (_activeNavigationScreen != nextNavigationScreen)
            {
                ClearNavigationVisuals();
                _activeNavigationScreen = nextNavigationScreen;
                CacheNavigationControls(nextNavigationScreen);
                RestoreSelection(nextNavigationScreen);
            }
            else
            {
                EnsureValidSelection(nextNavigationScreen);
            }
            RefreshNavigationVisuals();
        }

        private void Update()
        {
            if (_activeNavigationScreen == null || EventSystem.current == null)
                return;
            ResolvePendingNavigationFallback();
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
            {
                ClearPendingNavigationFallback();
                bool backwards = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                CycleNavigationSelection(backwards ? -1 : 1);
            }
            EnsureValidSelection(_activeNavigationScreen);
            RefreshNavigationVisuals();
        }

        private void OnInputEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (_activeNavigationScreen == null || EventSystem.current == null ||
                (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>()))
                return;

            MoveDirection direction = MoveDirection.None;
            if (device is Keyboard keyboard)
            {
                if (PressedInEvent(keyboard.rightArrowKey, eventPtr)) direction = MoveDirection.Right;
                else if (PressedInEvent(keyboard.leftArrowKey, eventPtr)) direction = MoveDirection.Left;
                else if (PressedInEvent(keyboard.upArrowKey, eventPtr)) direction = MoveDirection.Up;
                else if (PressedInEvent(keyboard.downArrowKey, eventPtr)) direction = MoveDirection.Down;
            }
            else if (device is Gamepad gamepad)
            {
                if (PressedInEvent(gamepad.dpad.right, eventPtr)) direction = MoveDirection.Right;
                else if (PressedInEvent(gamepad.dpad.left, eventPtr)) direction = MoveDirection.Left;
                else if (PressedInEvent(gamepad.dpad.up, eventPtr)) direction = MoveDirection.Up;
                else if (PressedInEvent(gamepad.dpad.down, eventPtr)) direction = MoveDirection.Down;
                else if (gamepad.leftStick.ReadValueFromEvent(eventPtr, out Vector2 stick))
                    direction = DirectionFromVector(stick);
            }
            if (direction != MoveDirection.None)
                RecordPendingNavigationFallback(EventSystem.current.currentSelectedGameObject, direction);
        }

        private static bool PressedInEvent(ButtonControl control, InputEventPtr eventPtr)
        {
            return control != null && control.ReadValueFromEvent(eventPtr, out float value) &&
                   value >= InputSystem.settings.defaultButtonPressPoint;
        }

        private static MoveDirection DirectionFromVector(Vector2 value)
        {
            if (value.sqrMagnitude < InputSystem.settings.defaultButtonPressPoint *
                                     InputSystem.settings.defaultButtonPressPoint)
                return MoveDirection.None;
            if (Mathf.Abs(value.x) > Mathf.Abs(value.y))
                return value.x > 0f ? MoveDirection.Right : MoveDirection.Left;
            return value.y > 0f ? MoveDirection.Up : MoveDirection.Down;
        }

#if UNITY_EDITOR
        public void Configure(GameObject title, GameObject hud, GameObject settings,
            GameObject pause, GameObject ending)
        {
            titleScreen = title;
            gameHud = hud;
            settingsScreen = settings;
            pauseScreen = pause;
            endingScreen = ending;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
                target.SetActive(active);
        }

        private void RememberCurrentSelection()
        {
            if (_activeNavigationScreen == null || EventSystem.current == null)
                return;
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(_activeNavigationScreen.transform))
                _lastSelectionByScreen[_activeNavigationScreen] = selected;
        }

        private void RestoreSelection(GameObject screen)
        {
            if (EventSystem.current == null)
                return;
            if (screen == null)
            {
                EventSystem.current.SetSelectedGameObject(null);
                return;
            }
            if (_lastSelectionByScreen.TryGetValue(screen, out GameObject previous) &&
                IsValidSelection(previous, screen))
            {
                EventSystem.current.SetSelectedGameObject(previous);
                return;
            }
            EventSystem.current.SetSelectedGameObject(FirstSelectable(screen));
        }

        private void EnsureValidSelection(GameObject screen)
        {
            if (screen == null || EventSystem.current == null)
                return;
            if (!IsValidSelection(EventSystem.current.currentSelectedGameObject, screen))
                EventSystem.current.SetSelectedGameObject(FirstSelectable(screen));
        }

        private void CacheNavigationControls(GameObject screen)
        {
            _activeSelectables = screen == null
                ? System.Array.Empty<Selectable>()
                : screen.GetComponentsInChildren<Selectable>(false);
            _activeSelectionVisuals = screen == null
                ? System.Array.Empty<KeyboardWandererButtonStateView>()
                : screen.GetComponentsInChildren<KeyboardWandererButtonStateView>(false);
            _lastVisualSelection = null;
        }

        private void CycleNavigationSelection(int direction)
        {
            if (direction == 0 || EventSystem.current == null || _activeSelectables.Length == 0)
                return;
            var candidates = new List<Selectable>(_activeSelectables.Length);
            for (int i = 0; i < _activeSelectables.Length; i++)
            {
                Selectable selectable = _activeSelectables[i];
                if (selectable != null && selectable.isActiveAndEnabled && selectable.IsInteractable())
                    candidates.Add(selectable);
            }
            if (candidates.Count == 0)
                return;

            GameObject selected = EventSystem.current.currentSelectedGameObject;
            Selectable current = selected == null ? null : selected.GetComponentInParent<Selectable>();
            int currentIndex = candidates.IndexOf(current);
            int nextIndex = currentIndex < 0
                ? (direction < 0 ? candidates.Count - 1 : 0)
                : (currentIndex + (direction < 0 ? -1 : 1) + candidates.Count) % candidates.Count;
            Selectable next = candidates[nextIndex];
            EventSystem.current.SetSelectedGameObject(next.gameObject);
            next.Select();
            RefreshNavigationVisuals();
        }

        private void RecordPendingNavigationFallback(GameObject origin, MoveDirection direction)
        {
            if (!IsValidSelection(origin, _activeNavigationScreen))
                return;
            _pendingNavigationScreen = _activeNavigationScreen;
            _pendingNavigationOrigin = origin;
            _pendingNavigationDirection = direction;
            _pendingNavigationFrame = Time.frameCount;
        }

        private void ResolvePendingNavigationFallback()
        {
            if (_pendingNavigationOrigin == null || Time.frameCount <= _pendingNavigationFrame)
                return;
            GameObject screen = _pendingNavigationScreen;
            GameObject originObject = _pendingNavigationOrigin;
            MoveDirection direction = _pendingNavigationDirection;
            ClearPendingNavigationFallback();

            // A normal EventSystem move wins. The fallback exists only for a tap whose
            // performed and canceled values collapsed to zero before module.Process.
            if (screen != _activeNavigationScreen ||
                EventSystem.current.currentSelectedGameObject != originObject ||
                !IsValidSelection(originObject, screen))
                return;

            Selectable origin = originObject.GetComponentInParent<Selectable>();
            Selectable target = NavigationTarget(origin, direction);
            if (target == null || !IsValidSelection(target.gameObject, screen))
                return;
            EventSystem.current.SetSelectedGameObject(target.gameObject);
            target.Select();
            RefreshNavigationVisuals();
        }

        private static Selectable NavigationTarget(Selectable origin, MoveDirection direction)
        {
            if (origin == null)
                return null;
            Navigation navigation = origin.navigation;
            if (navigation.mode == Navigation.Mode.None)
                return null;
            if (navigation.mode == Navigation.Mode.Explicit)
            {
                switch (direction)
                {
                    case MoveDirection.Left: return navigation.selectOnLeft;
                    case MoveDirection.Right: return navigation.selectOnRight;
                    case MoveDirection.Up: return navigation.selectOnUp;
                    case MoveDirection.Down: return navigation.selectOnDown;
                    default: return null;
                }
            }
            switch (direction)
            {
                case MoveDirection.Left: return origin.FindSelectableOnLeft();
                case MoveDirection.Right: return origin.FindSelectableOnRight();
                case MoveDirection.Up: return origin.FindSelectableOnUp();
                case MoveDirection.Down: return origin.FindSelectableOnDown();
                default: return null;
            }
        }

        private void ClearPendingNavigationFallback()
        {
            _pendingNavigationScreen = null;
            _pendingNavigationOrigin = null;
            _pendingNavigationDirection = MoveDirection.None;
            _pendingNavigationFrame = -1;
        }

        private void RefreshNavigationVisuals()
        {
            GameObject selected = EventSystem.current?.currentSelectedGameObject;
            if (_lastVisualSelection == selected)
                return;
            _lastVisualSelection = selected;
            for (int i = 0; i < _activeSelectionVisuals.Length; i++)
            {
                KeyboardWandererButtonStateView visual = _activeSelectionVisuals[i];
                if (visual == null) continue;
                bool isSelected = selected != null &&
                                  (selected == visual.gameObject || selected.transform.IsChildOf(visual.transform));
                visual.SetSelected(isSelected);
            }
        }

        private void ClearNavigationVisuals()
        {
            ClearPendingNavigationFallback();
            for (int i = 0; i < _activeSelectionVisuals.Length; i++)
                if (_activeSelectionVisuals[i] != null)
                    _activeSelectionVisuals[i].SetSelected(false);
            _activeSelectables = System.Array.Empty<Selectable>();
            _activeSelectionVisuals = System.Array.Empty<KeyboardWandererButtonStateView>();
            _lastVisualSelection = null;
        }

        private static bool IsValidSelection(GameObject candidate, GameObject screen)
        {
            if (candidate == null || screen == null || !candidate.activeInHierarchy ||
                !candidate.transform.IsChildOf(screen.transform)) return false;
            Selectable selectable = candidate.GetComponentInParent<Selectable>();
            return selectable != null && selectable.isActiveAndEnabled && selectable.IsInteractable();
        }

        private static GameObject FirstSelectable(GameObject screen)
        {
            if (screen == null) return null;
            // A screen-specific presenter may establish a semantic default that is
            // not the first control in hierarchy order (the title prefers Continue
            // when a resumable run exists). Selection can legitimately become null
            // while an overlay or input module releases focus. Reacquiring focus must
            // honor that established default; otherwise the following presentation
            // pass mistakes the hierarchy fallback for deliberate player navigation.
            GameObject preferred = EventSystem.current?.firstSelectedGameObject;
            if (IsValidSelection(preferred, screen))
                return preferred;
            Selectable[] candidates = screen.GetComponentsInChildren<Selectable>(false);
            for (int i = 0; i < candidates.Length; i++)
                if (candidates[i] != null && candidates[i].isActiveAndEnabled && candidates[i].IsInteractable())
                    return candidates[i].gameObject;
            return null;
        }
    }
}
