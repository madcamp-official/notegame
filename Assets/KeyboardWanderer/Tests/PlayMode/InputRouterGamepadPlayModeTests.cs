using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Client.UI;
using KeyboardWanderer.Demo;
using KeyboardWanderer.Gameplay;
using KeyboardWanderer.Runtime;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace KeyboardWanderer.Tests.PlayMode
{
    public sealed class InputRouterGamepadPlayModeTests : InputTestFixture
    {
        private GameObject _routerObject;
        private GameObject _inputObject;
        private GameObject _ownedEventSystem;
        private KeyboardWandererInputRouter _router;
        private Gamepad _gamepad;
        private Keyboard _keyboard;
        private Mouse _mouse;

        public override void Setup()
        {
            base.Setup();
            _gamepad = InputSystem.AddDevice<Gamepad>();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            _routerObject = new GameObject("Virtual Gamepad Input Router");
            _router = _routerObject.AddComponent<KeyboardWandererInputRouter>();
        }

        public override void TearDown()
        {
            if (_inputObject != null) UnityEngine.Object.DestroyImmediate(_inputObject);
            if (_routerObject != null) UnityEngine.Object.DestroyImmediate(_routerObject);
            if (_ownedEventSystem != null) UnityEngine.Object.DestroyImmediate(_ownedEventSystem);
            base.TearDown();
        }

        [Test]
        public void Gamepad_ChoicePauseAndFocusOwnershipAreSingleDispatch()
        {
            int movement = 0;
            int confirmations = 0;
            int pauses = 0;
            _router.NarrativeChoiceMoveRequested += value => movement += value;
            _router.NarrativeChoiceConfirmRequested += () => confirmations++;
            _router.PauseRequested += () => pauses++;
            _router.SetNarrativeChoiceMode(true);

            Press(_gamepad.dpad.down);
            InvokeReadGamepad();
            Assert.That(movement, Is.EqualTo(1));
            Release(_gamepad.dpad.down);

            Press(_gamepad.buttonSouth);
            InvokeReadGamepad();
            Assert.That(confirmations, Is.EqualTo(1));
            Release(_gamepad.buttonSouth);

            Press(_gamepad.startButton);
            InvokeReadGamepad();
            Assert.That(pauses, Is.EqualTo(1));
            Release(_gamepad.startButton);

            _router.SetUiOverlayMode(true);
            Press(_gamepad.dpad.up);
            InvokeReadGamepad();
            Assert.That(movement, Is.EqualTo(1), "Overlay navigation must not leak into narrative choices.");
            Release(_gamepad.dpad.up);

            _router.SetUiOverlayMode(false);
            _router.SetNarrativeChoiceMode(true);
            EnsureEventSystem();
            _inputObject = new GameObject("Focused Gamepad Text Input", typeof(RectTransform),
                typeof(TMP_InputField), typeof(InputFocusTracker));
            EventSystem.current.SetSelectedGameObject(_inputObject);
            Assert.That(InputFocusTracker.HasFocusedTextField, Is.True);
            Press(_gamepad.dpad.down);
            InvokeReadGamepad();
            Release(_gamepad.dpad.down);
            Press(_gamepad.buttonSouth);
            InvokeReadGamepad();
            Assert.That(movement, Is.EqualTo(1));
            Assert.That(confirmations, Is.EqualTo(1),
                "A focused text field must own gamepad input just as it owns Return/W/S.");
        }

        [Test]
        public void ChoiceModeTransition_DoesNotReuseTheHeldPageAdvanceGesture()
        {
            int confirmations = 0;
            _router.NarrativeChoiceConfirmRequested += () => confirmations++;

            Press(_keyboard.spaceKey);
            _router.SetNarrativeChoiceMode(true);
            InvokeReadKeyboard();
            Assert.That(confirmations, Is.Zero,
                "선택지가 표시된 프레임의 Space를 보이지 않던 첫 선택 확정에 재사용하면 안 됩니다.");
            Release(_keyboard.spaceKey);
            InvokeReadKeyboard();
            Press(_keyboard.enterKey);
            InvokeReadKeyboard();
            Assert.That(confirmations, Is.EqualTo(1));
            Release(_keyboard.enterKey);

            _router.SetNarrativeChoiceMode(false);
            Press(_gamepad.buttonSouth);
            _router.SetNarrativeChoiceMode(true);
            InvokeReadGamepad();
            Assert.That(confirmations, Is.EqualTo(1),
                "선택지가 나타난 프레임의 게임패드 확인 입력도 재사용하면 안 됩니다.");
            Release(_gamepad.buttonSouth);
            InvokeReadGamepad();
            Press(_gamepad.buttonSouth);
            InvokeReadGamepad();
            Assert.That(confirmations, Is.EqualTo(2));
        }

        [Test]
        public void ArrowKeys_CyclePoiEvenWhenNarrativeChoicesAreVisible()
        {
            int poiDirection = 0;
            int choiceMovement = 0;
            _router.PoiCycleRequested += value => poiDirection += value;
            _router.NarrativeChoiceMoveRequested += value => choiceMovement += value;
            _router.SetNarrativeChoiceMode(true);

            Press(_keyboard.rightArrowKey);
            InvokeReadKeyboard();
            Assert.That(poiDirection, Is.EqualTo(1));
            Assert.That(choiceMovement, Is.Zero,
                "좌우 방향키는 선택지 이동이 아니라 명시적인 월드 목적지 전환이어야 합니다.");
            Release(_keyboard.rightArrowKey);

            Press(_keyboard.leftArrowKey);
            InvokeReadKeyboard();
            Assert.That(poiDirection, Is.Zero);
            Release(_keyboard.leftArrowKey);
        }

        [Test]
        public void PrimarySingleKeyAbilityShortcuts_DispatchExactlyOnceAndRespectTextFocus()
        {
            int search = 0;
            int restore = 0;
            _router.AbilityRequested += ability =>
            {
                if (ability == AbilityKind.Search) search++;
                if (ability == AbilityKind.Restore) restore++;
            };

            Press(_keyboard.fKey);
            InvokeReadKeyboard();
            Release(_keyboard.fKey);
            Press(_keyboard.xKey);
            InvokeReadKeyboard();
            Release(_keyboard.xKey);
            Assert.That(search, Is.EqualTo(1));
            Assert.That(restore, Is.EqualTo(1));

            EnsureEventSystem();
            _inputObject = new GameObject("Focused Shortcut Text Input", typeof(RectTransform),
                typeof(TMP_InputField), typeof(InputFocusTracker));
            EventSystem.current.SetSelectedGameObject(_inputObject);
            Press(_keyboard.fKey);
            InvokeReadKeyboard();
            Release(_keyboard.fKey);
            Press(_keyboard.xKey);
            InvokeReadKeyboard();
            Release(_keyboard.xKey);
            Assert.That(search, Is.EqualTo(1), "입력 중 F는 조사로 새어 나가면 안 됩니다.");
            Assert.That(restore, Is.EqualTo(1), "입력 중 X는 복구로 새어 나가면 안 됩니다.");
        }

        [Test]
        public void WorldMode_WasdDispatchesFourOneTileDirectionsAndNeverLeaksFromTextInput()
        {
            var directions = new List<Vector2Int>();
            int choiceMovement = 0;
            int releases = 0;
            _router.DirectionalMoveRequested += direction => directions.Add(direction);
            _router.NarrativeChoiceMoveRequested += direction => choiceMovement += direction;
            _router.DirectionalMoveReleased += () => releases++;

            _router.SetNarrativeChoiceMode(true);
            InvokeReadKeyboard();
            PressAndRead(_keyboard.wKey);
            PressAndRead(_keyboard.aKey);
            PressAndRead(_keyboard.sKey);
            PressAndRead(_keyboard.dKey);

            CollectionAssert.AreEqual(new[]
            {
                Vector2Int.up, Vector2Int.left, Vector2Int.down, Vector2Int.right
            }, directions, "WASD must map to exactly one cardinal tile per fresh key press.");
            Assert.That(choiceMovement, Is.Zero,
                "Visible choices use arrow keys; W/S must not silently change a choice instead of moving.");
            Assert.That(releases, Is.EqualTo(4),
                "Each released movement key must clear any queued continuation tile.");

            EnsureEventSystem();
            _inputObject = new GameObject("Focused WASD Text Input", typeof(RectTransform),
                typeof(TMP_InputField), typeof(InputFocusTracker));
            EventSystem.current.SetSelectedGameObject(_inputObject);
            PressAndRead(_keyboard.wKey);
            PressAndRead(_keyboard.aKey);
            PressAndRead(_keyboard.sKey);
            PressAndRead(_keyboard.dKey);

            Assert.That(directions.Count, Is.EqualTo(4),
                "WASD typed in the natural-language field must not move the world behind it.");
        }

        [Test]
        public void ReturnFrame_DeselectedTextFieldStillOwnsSubmitAndChoiceShortcuts()
        {
            int worldSubmits = 0;
            int choiceConfirms = 0;
            _router.SubmitRequested += () => worldSubmits++;
            _router.NarrativeChoiceConfirmRequested += () => choiceConfirms++;
            _router.SetNarrativeChoiceMode(true);
            EnsureEventSystem();
            _inputObject = new GameObject("Return Ownership Text Input", typeof(RectTransform),
                typeof(TMP_InputField), typeof(InputFocusTracker));

            EventSystem.current.SetSelectedGameObject(_inputObject);
            Assert.That(InputFocusTracker.HasFocusedTextField, Is.True);
            Press(_keyboard.enterKey);
            EventSystem.current.SetSelectedGameObject(null);
            Assert.That(InputFocusTracker.HasFocusedTextField, Is.False,
                "Fixture must reproduce TMP deselection before the gameplay router reads Return.");
            Assert.That(InputFocusTracker.OwnsTextInputThisFrame, Is.True);

            InvokeReadKeyboard();

            Assert.That(worldSubmits, Is.Zero,
                "TMP onSubmit과 같은 Return이 월드 실행으로 재사용되면 안 됩니다.");
            Assert.That(choiceConfirms, Is.Zero,
                "TMP onSubmit과 같은 Return이 선택지 확정으로 재사용되면 안 됩니다.");
        }

        [UnityTest]
        public IEnumerator ImmediateMoveAndClick_OnVisibleInputIsNeverDispatchedToWorld()
        {
            EnsureEventSystem();
            _inputObject = new GameObject("Immediate Pointer UI", typeof(RectTransform),
                typeof(Canvas), typeof(GraphicRaycaster));
            Canvas canvas = _inputObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var input = new GameObject("Visible Text Input", typeof(RectTransform), typeof(Image));
            input.transform.SetParent(_inputObject.transform, false);
            RectTransform inputRect = (RectTransform)input.transform;
            inputRect.anchorMin = inputRect.anchorMax = new Vector2(0.5f, 0.5f);
            inputRect.sizeDelta = new Vector2(320f, 80f);
            input.GetComponent<Image>().raycastTarget = true;
            yield return null;
            Canvas.ForceUpdateCanvases();

            int worldClicks = 0;
            _router.WorldClickRequested += _ => worldClicks++;
            Vector2 inputCenter = RectTransformUtility.WorldToScreenPoint(null,
                inputRect.TransformPoint(inputRect.rect.center));
            InputSystem.QueueDeltaStateEvent(_mouse.position, inputCenter);
            InputSystem.Update();
            Press(_mouse.leftButton);

            InvokeReadPointer();

            Assert.That(worldClicks, Is.Zero,
                "현재 프레임에 UI로 이동해 클릭한 포인터가 월드 선택으로 먼저 전달되면 안 됩니다.");
            var hits = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = inputCenter }, hits);
            Assert.That(hits, Has.Some.Matches<RaycastResult>(hit => hit.gameObject == input),
                "Fixture must hit the visible input graphic at the exact current pointer position.");
        }

        [Test]
        public void NarrativeOverlay_BlocksWorldPointerEvenBeforeTheInputFieldReceivesFocus()
        {
            int worldClicks = 0;
            _router.WorldClickRequested += _ => worldClicks++;
            _router.SetNarrativeOverlayMode(true);

            Press(_mouse.leftButton);
            InvokeReadPointer();
            Release(_mouse.leftButton);

            Assert.That(worldClicks, Is.Zero,
                "선택지/자유입력 모달을 누른 첫 프레임이 뒤의 월드 타일로 전달되면 안 됩니다.");

            _router.SetNarrativeOverlayMode(false);
            InputSystem.QueueDeltaStateEvent(_mouse.position, new Vector2(-100f, -100f));
            InputSystem.Update();
            Press(_mouse.leftButton);
            InvokeReadPointer();
            Release(_mouse.leftButton);
            Assert.That(worldClicks, Is.EqualTo(1));
        }

        [Test]
        public void ChoiceMode_MatchingSkillShortcutDispatchesAbilityWithoutConfirmingAChoiceTwice()
        {
            int search = 0;
            int delete = 0;
            int confirmations = 0;
            int choiceMovement = 0;
            _router.AbilityRequested += ability =>
            {
                if (ability == AbilityKind.Search) search++;
                if (ability == AbilityKind.Delete) delete++;
            };
            _router.NarrativeChoiceConfirmRequested += () => confirmations++;
            _router.NarrativeChoiceMoveRequested += value => choiceMovement += value;
            _router.SetNarrativeChoiceMode(true);

            Press(_keyboard.fKey);
            InvokeReadKeyboard();
            Release(_keyboard.fKey);
            Press(_keyboard.rKey);
            InvokeReadKeyboard();
            Release(_keyboard.rKey);

            Assert.That(search, Is.EqualTo(1));
            Assert.That(delete, Is.EqualTo(1),
                "첫 전투의 R 공격은 선택 확정과 중복되지 않고 정확히 한 번 전달돼야 합니다.");
            Assert.That(confirmations, Is.Zero);
            Assert.That(choiceMovement, Is.Zero);
        }

        [Test]
        public void UiOverlay_BlocksEveryBehindHudShortcutAndNarrativeAction()
        {
            int inventory = 0;
            int quests = 0;
            int abilities = 0;
            int poiCycles = 0;
            int submits = 0;
            int narrativeChoices = 0;
            int narrativeConfirms = 0;
            int directionalMoves = 0;
            _router.InventoryRequested += () => inventory++;
            _router.QuestRequested += () => quests++;
            _router.AbilityRequested += _ => abilities++;
            _router.PoiCycleRequested += _ => poiCycles++;
            _router.SubmitRequested += () => submits++;
            _router.NarrativeChoiceRequested += _ => narrativeChoices++;
            _router.NarrativeChoiceConfirmRequested += () => narrativeConfirms++;
            _router.DirectionalMoveRequested += _ => directionalMoves++;
            _router.SetNarrativeChoiceMode(true);
            _router.SetUiOverlayMode(true);

            PressAndRead(_keyboard.iKey);
            PressAndRead(_keyboard.qKey);
            PressAndRead(_keyboard.fKey);
            PressAndRead(_keyboard.rightArrowKey);
            PressAndRead(_keyboard.enterKey);
            PressAndRead(_keyboard.digit1Key);
            PressAndRead(_keyboard.wKey);

            Assert.That(inventory, Is.Zero, "결과 모달 뒤에서 인벤토리가 열리면 안 됩니다.");
            Assert.That(quests, Is.Zero, "결과 모달 뒤에서 퀘스트 패널이 열리면 안 됩니다.");
            Assert.That(abilities, Is.Zero, "결과 모달 뒤에서 스킬이 제출되면 안 됩니다.");
            Assert.That(poiCycles, Is.Zero, "결과 모달의 방향 입력이 월드 POI로 새면 안 됩니다.");
            Assert.That(submits, Is.Zero, "결과 모달의 Return은 숨은 HUD 실행 버튼에 전달되면 안 됩니다.");
            Assert.That(narrativeChoices, Is.Zero);
            Assert.That(narrativeConfirms, Is.Zero,
                "결과 모달의 Return은 숨은 대화 선택을 확정하면 안 됩니다.");
            Assert.That(directionalMoves, Is.Zero, "결과 모달 뒤에서 플레이어가 이동하면 안 됩니다.");
        }

        [Test]
        public void ClosingOverlay_DoesNotReuseMenuActivationOnGameplayBehindIt()
        {
            int submits = 0;
            int narrativeConfirms = 0;
            int worldClicks = 0;
            _router.SubmitRequested += () => submits++;
            _router.NarrativeChoiceConfirmRequested += () => narrativeConfirms++;
            _router.WorldClickRequested += _ => worldClicks++;

            _router.SetUiOverlayMode(true);
            Press(_keyboard.enterKey);
            _router.SetUiOverlayMode(false);
            InvokeReadKeyboard();
            Assert.That(submits, Is.Zero,
                "일시정지 메뉴를 닫은 Return이 선택된 장거리 이동까지 제출하면 안 됩니다.");
            Release(_keyboard.enterKey);
            InvokeReadKeyboard();
            Press(_keyboard.enterKey);
            InvokeReadKeyboard();
            Assert.That(submits, Is.EqualTo(1), "키를 놓은 뒤의 새 Return은 즉시 동작해야 합니다.");
            Release(_keyboard.enterKey);

            _router.SetNarrativeChoiceMode(true);
            _router.SetUiOverlayMode(true);
            Press(_gamepad.buttonSouth);
            _router.SetUiOverlayMode(false);
            InvokeReadGamepad();
            Assert.That(narrativeConfirms, Is.Zero,
                "메뉴의 gamepad South가 뒤의 첫 선택지를 확정하면 안 됩니다.");
            Release(_gamepad.buttonSouth);
            InvokeReadGamepad();
            Press(_gamepad.buttonSouth);
            InvokeReadGamepad();
            Assert.That(narrativeConfirms, Is.EqualTo(1));
            Release(_gamepad.buttonSouth);

            _router.SetNarrativeChoiceMode(false);
            _router.SetUiOverlayMode(true);
            Press(_mouse.leftButton);
            _router.SetUiOverlayMode(false);
            InvokeReadPointer();
            Assert.That(worldClicks, Is.Zero,
                "계속하기 버튼 클릭이 같은 위치의 월드 타일까지 함께 클릭하면 안 됩니다.");
            Release(_mouse.leftButton);
            InvokeReadPointer();
        }

        [Test]
        public void ScreenFlow_TabAndShiftTabCycleOnlyTheActiveModalControls()
        {
            EnsureEventSystem();
            var root = new GameObject("Modal Navigation Fixture", typeof(RectTransform),
                typeof(KeyboardWandererScreenFlowView));
            try
            {
                GameObject title = CreateUiChild(root.transform, "Title");
                GameObject hud = CreateUiChild(root.transform, "HUD");
                GameObject settings = CreateUiChild(root.transform, "Settings");
                GameObject pause = CreateUiChild(root.transform, "Pause");
                GameObject ending = CreateUiChild(root.transform, "Ending");
                Button newRun = CreateButton(ending.transform, "New Run");
                Button goToTitle = CreateButton(ending.transform, "Title");
                CreateButton(hud.transform, "Behind HUD Button");
                var flow = root.GetComponent<KeyboardWandererScreenFlowView>();
                flow.Configure(title, hud, settings, pause, ending);

                flow.Present(false, false, true, false, true);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(newRun.gameObject));

                Press(_keyboard.tabKey);
                InvokeUpdate(flow);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(goToTitle.gameObject),
                    "Tab은 활성 결과 모달 안에서 다음 버튼으로 이동해야 합니다.");
                Release(_keyboard.tabKey);
                InvokeUpdate(flow);

                Press(_keyboard.leftShiftKey);
                Press(_keyboard.tabKey);
                InvokeUpdate(flow);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(newRun.gameObject),
                    "Shift+Tab은 활성 결과 모달 안에서 이전 버튼으로 돌아가야 합니다.");
                Release(_keyboard.tabKey);
                Release(_keyboard.leftShiftKey);
            }
            finally
            {
                EventSystem.current?.SetSelectedGameObject(null);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator ScreenFlow_FastDirectionalTapsFallbackOnceWithoutDoublingEventSystemMoves()
        {
            // AuthoredUiPlayModeTests intentionally leaves SampleScene loaded. Isolate
            // this focused navigation fixture from that scene's controller/ScreenFlow;
            // otherwise both presentation owners repair EventSystem.current every frame
            // and the result depends on script execution order rather than navigation.
            KeyboardWandererDemoController[] existingControllers =
                UnityEngine.Object.FindObjectsByType<KeyboardWandererDemoController>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            bool[] existingControllerStates = new bool[existingControllers.Length];
            for (int i = 0; i < existingControllers.Length; i++)
            {
                existingControllerStates[i] = existingControllers[i].enabled;
                existingControllers[i].enabled = false;
            }
            KeyboardWandererScreenFlowView[] existingFlows =
                UnityEngine.Object.FindObjectsByType<KeyboardWandererScreenFlowView>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            bool[] existingFlowStates = new bool[existingFlows.Length];
            for (int i = 0; i < existingFlows.Length; i++)
            {
                existingFlowStates[i] = existingFlows[i].enabled;
                existingFlows[i].enabled = false;
            }

            EnsureEventSystem();
            var root = new GameObject("Fast Modal Navigation Fixture", typeof(RectTransform),
                typeof(KeyboardWandererScreenFlowView));
            try
            {
                GameObject title = CreateUiChild(root.transform, "Title");
                GameObject hud = CreateUiChild(root.transform, "HUD");
                GameObject settings = CreateUiChild(root.transform, "Settings");
                GameObject pause = CreateUiChild(root.transform, "Pause");
                GameObject ending = CreateUiChild(root.transform, "Ending");
                Button newRun = CreateButton(ending.transform, "New Run");
                Button goToTitle = CreateButton(ending.transform, "Title");
                SetPairedNavigation(newRun, goToTitle);
                var flow = root.GetComponent<KeyboardWandererScreenFlowView>();
                flow.Configure(title, hud, settings, pause, ending);
                flow.Present(false, false, true, false, true);

                Key[] fastKeys =
                {
                    Key.RightArrow, Key.LeftArrow, Key.UpArrow, Key.DownArrow
                };
                for (int i = 0; i < fastKeys.Length; i++)
                {
                    EventSystem.current.SetSelectedGameObject(newRun.gameObject);
                    InvokeUpdate(flow);
                    QueueFastKeyboardTap(_keyboard, fastKeys[i]);
                    InvokeUpdate(flow);
                    Assert.That(PrivateField(flow, "_pendingNavigationOrigin"), Is.EqualTo(newRun.gameObject),
                        "raw InputSystem event에서 빠른 방향 탭의 원래 선택을 기록해야 합니다. index=" + i);
                    Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(newRun.gameObject),
                        "fallback은 입력 프레임에 즉시 Submit 대상을 바꾸면 안 됩니다.");
                    yield return null;
                    // UnityTest coroutine resumption and MonoBehaviour.Update ordering can
                    // differ when this test follows a scene-loading fixture. Drive the
                    // next-frame ScreenFlow pass explicitly so the assertion always
                    // observes the fallback after its one-frame deferral.
                    InvokeUpdate(flow);
                    Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(goToTitle.gameObject),
                        "빠른 Arrow/D-pad tap도 다음 프레임에 정확히 한 번 이동해야 합니다. index=" + i);
                }


                GamepadButton[] dpadButtons =
                {
                    GamepadButton.DpadRight, GamepadButton.DpadLeft,
                    GamepadButton.DpadUp, GamepadButton.DpadDown
                };
                for (int i = 0; i < dpadButtons.Length; i++)
                {
                    EventSystem.current.SetSelectedGameObject(newRun.gameObject);
                    InvokeUpdate(flow);
                    QueueFastGamepadButtonTap(_gamepad, dpadButtons[i]);
                    InvokeUpdate(flow);
                    Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(newRun.gameObject));
                    yield return null;
                    InvokeUpdate(flow);
                    Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(goToTitle.gameObject),
                        "빠른 D-pad tap도 다음 프레임에 정확히 한 번 이동해야 합니다. index=" + i);
                }

                Vector2[] stickDirections = { Vector2.right, Vector2.left, Vector2.up, Vector2.down };
                for (int i = 0; i < stickDirections.Length; i++)
                {
                    EventSystem.current.SetSelectedGameObject(newRun.gameObject);
                    InvokeUpdate(flow);
                    QueueFastStickTap(_gamepad, stickDirections[i]);
                    InvokeUpdate(flow);
                    Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(newRun.gameObject));
                    yield return null;
                    InvokeUpdate(flow);
                    Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(goToTitle.gameObject),
                        "빠른 left-stick tap도 다음 프레임에 정확히 한 번 이동해야 합니다. index=" + i);
                }

                // Simulate the ordinary InputSystemUIInputModule processing after the
                // ScreenFlow recorded the press. The next-frame fallback must see that
                // selection already moved and must not wrap to the other button again.
                EventSystem.current.SetSelectedGameObject(newRun.gameObject);
                InvokeUpdate(flow);
                QueueFastKeyboardTap(_keyboard, Key.RightArrow);
                InvokeUpdate(flow);
                EventSystem.current.SetSelectedGameObject(goToTitle.gameObject);
                yield return null;
                InvokeUpdate(flow);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(goToTitle.gameObject),
                    "정상 EventSystem 이동 뒤 fallback이 두 번째 이동을 만들면 안 됩니다.");
            }
            finally
            {
                EventSystem.current?.SetSelectedGameObject(null);
                UnityEngine.Object.DestroyImmediate(root);
                for (int i = 0; i < existingFlows.Length; i++)
                    if (existingFlows[i] != null)
                        existingFlows[i].enabled = existingFlowStates[i];
                for (int i = 0; i < existingControllers.Length; i++)
                    if (existingControllers[i] != null)
                        existingControllers[i].enabled = existingControllerStates[i];
            }
        }

        private void PressAndRead(KeyControl key)
        {
            Press(key);
            InvokeReadKeyboard();
            Release(key);
            InvokeReadKeyboard();
        }

        private static GameObject CreateUiChild(Transform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return child;
        }

        private static Button CreateButton(Transform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            child.transform.SetParent(parent, false);
            return child.GetComponent<Button>();
        }

        private static void SetPairedNavigation(Button first, Button second)
        {
            first.navigation = ExplicitNavigation(second);
            second.navigation = ExplicitNavigation(first);
        }

        private static Navigation ExplicitNavigation(Selectable other)
        {
            return new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = other,
                selectOnDown = other,
                selectOnLeft = other,
                selectOnRight = other
            };
        }

        private static void QueueFastKeyboardTap(Keyboard keyboard, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
        }

        private static void QueueFastGamepadButtonTap(Gamepad gamepad, GamepadButton button)
        {
            InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(button));
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            InputSystem.Update();
        }

        private static void QueueFastStickTap(Gamepad gamepad, Vector2 direction)
        {
            InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = direction });
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            InputSystem.Update();
        }

        private static void InvokeUpdate(KeyboardWandererScreenFlowView flow)
        {
            MethodInfo method = typeof(KeyboardWandererScreenFlowView).GetMethod("Update",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(flow, null);
        }

        private static object PrivateField(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return field.GetValue(target);
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;
            _ownedEventSystem = new GameObject("Gamepad Test EventSystem", typeof(EventSystem));
        }

        private void InvokeReadGamepad()
        {
            MethodInfo method = typeof(KeyboardWandererInputRouter).GetMethod("ReadGamepad",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(_router, null);
        }

        private void InvokeReadKeyboard()
        {
            MethodInfo method = typeof(KeyboardWandererInputRouter).GetMethod("ReadKeyboard",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(_router, null);
        }

        private void InvokeReadPointer()
        {
            MethodInfo method = typeof(KeyboardWandererInputRouter).GetMethod("ReadPointer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(_router, null);
        }
    }
}
