using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Game.Client.UI;
using KeyboardWanderer.Demo;
using KeyboardWanderer.Gameplay;
using KeyboardWanderer.Presentation;
using KeyboardWanderer.Runtime;
using KeyboardWanderer.World;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KeyboardWanderer.Tests.PlayMode
{
    public sealed class AuthoredUiPlayModeTests
    {
        private KeyboardWandererSceneUI _sceneUi;
        private KeyboardWandererDemoController _controller;

        [UnitySetUp]
        public IEnumerator LoadAuthoredScene()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return null;
            _sceneUi = Object.FindAnyObjectByType<KeyboardWandererSceneUI>(FindObjectsInactive.Include);
            _controller = Object.FindAnyObjectByType<KeyboardWandererDemoController>(FindObjectsInactive.Include);
            Assert.That(_sceneUi, Is.Not.Null);
            Assert.That(_controller, Is.Not.Null);
            Assert.That(_controller.GetComponent<KeyboardWandererInputRouter>(), Is.Not.Null,
                "게임 루트에 키보드·포인터 입력 전용 InputRouter가 있어야 합니다.");
            Assert.That(_controller.GetComponent<KeyboardWandererSelectionController>(), Is.Not.Null,
                "게임 루트에 선택 상태 전용 SelectionController가 있어야 합니다.");
            Assert.That(_controller.GetComponent<KeyboardWandererAbilityAvailability>(), Is.Not.Null,
                "게임 루트에 로컬·서버 공통 스킬 판정 컴포넌트가 있어야 합니다.");
            Assert.That(Object.FindAnyObjectByType<KeyboardWandererMinimapRenderer>(FindObjectsInactive.Include),
                Is.Not.Null, "Authored World에 미니맵 렌더링 전용 컴포넌트가 있어야 합니다.");
            Assert.That(Object.FindAnyObjectByType<KeyboardWandererPathPlanner>(FindObjectsInactive.Include),
                Is.Not.Null, "Authored World에 이동 경로 계산 전용 컴포넌트가 있어야 합니다.");
            Assert.That(_controller.GetComponent<KeyboardWandererRunSessionController>(), Is.Not.Null,
                "게임 루트에 새 게임·이어하기 전용 RunSessionController가 있어야 합니다.");
            Assert.That(_controller.GetComponent<KeyboardWandererSettingsController>(), Is.Not.Null,
                "게임 루트에 사용자 설정 저장 전용 SettingsController가 있어야 합니다.");
            Assert.That(Object.FindAnyObjectByType<KeyboardWandererVisualAssetLibrary>(FindObjectsInactive.Include),
                Is.Not.Null, "Authored World에 시각 에셋 생성 전용 VisualAssetLibrary가 있어야 합니다.");
        }

        [Test]
        public void AuthoredUi_UsesSerializedTmpMinimapAndButtonStateViews()
        {
            Assert.That(_sceneUi.IsReady, Is.True);
            Assert.That(_sceneUi.GetComponentsInChildren<Text>(true), Is.Empty);

            Assert.That(_sceneUi.GetComponent<KeyboardWandererScreenFlowView>()?.IsReady, Is.True,
                "Authored UI 루트가 화면 활성 상태만 소유해야 합니다.");
            Assert.That(Find(_sceneUi.transform, "Title Screen")
                ?.GetComponent<KeyboardWandererTitleView>()?.IsReady, Is.True);
            Assert.That(Find(_sceneUi.transform, "Game HUD")
                ?.GetComponent<KeyboardWandererGameHudView>()?.IsReady, Is.True);
            Assert.That(Find(_sceneUi.transform, "Pause Screen")
                ?.GetComponent<KeyboardWandererPauseView>()?.IsReady, Is.True);
            Assert.That(Find(_sceneUi.transform, "Ending Screen")
                ?.GetComponent<KeyboardWandererEndingView>()?.IsReady, Is.True);

            TMP_Text[] texts = _sceneUi.GetComponentsInChildren<TMP_Text>(true);
            Assert.That(texts.Length, Is.GreaterThan(20));
            Assert.That(texts, Has.All.Matches<TMP_Text>(text =>
                text.font != null && text.font.name.Contains("NeoDunggeunmoPro")));
            TMP_FontAsset persistentFont = Resources.Load<TMP_FontAsset>(
                "Fonts/NeoDunggeunmoPro-Regular SDF");
            Assert.That(persistentFont, Is.Not.Null);
            Assert.That(persistentFont.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Static));
            Assert.That(texts[0].font, Is.Not.SameAs(persistentFont),
                "PlayMode의 임의 한글은 프로젝트 TMP 에셋을 직접 변경하면 안 됩니다.");
            Assert.That(texts[0].font.atlasTextures[0], Is.Not.SameAs(persistentFont.atlasTextures[0]));
            Assert.That(texts[0].font.material, Is.Not.SameAs(persistentFont.material));
            Assert.That((texts[0].font.hideFlags & HideFlags.DontSave) != 0, Is.True);

            Transform minimap = Find(_sceneUi.transform, "Minimap Map");
            Assert.That(minimap, Is.Not.Null);
            Assert.That(minimap.GetComponent<Image>(), Is.Not.Null);
            Assert.That(Find(_sceneUi.transform, "Runtime Minimap"), Is.Null);
            Assert.That(Find(_sceneUi.transform, "Minimap Panel")
                .GetComponent<KeyboardWandererMinimapView>()?.IsReady, Is.True,
                "미니맵 패널이 자체 표시 컴포넌트와 직렬화 참조를 소유해야 합니다.");

            var dialogueView = Find(_sceneUi.transform, "Story Panel")
                .GetComponent<KeyboardWandererDialogueView>();
            Assert.That(dialogueView, Is.Not.Null,
                "대화 상태를 소유하는 컴포넌트가 Story Panel에 있어야 합니다.");
            Assert.That(dialogueView.IsReady, Is.True,
                "대화 텍스트와 Next 버튼은 Inspector 참조로 연결되어야 합니다.");
            Assert.That(Find(_sceneUi.transform, "Story Panel")
                ?.GetComponent<KeyboardWandererTutorialView>()?.IsReady, Is.True,
                "튜토리얼 문구는 Story Panel 컴포넌트에서 편집할 수 있어야 합니다.");

            var inventoryHudView = Find(_sceneUi.transform, "Inventory Panel")
                ?.GetComponent<KeyboardWandererInventoryHudView>();
            Assert.That(inventoryHudView, Is.Not.Null,
                "Game HUD에 상시 노출되는 소지품 요약 Inventory Panel이 있어야 합니다.");
            Assert.That(inventoryHudView.IsReady, Is.True,
                "Inventory Panel의 아이콘 슬롯은 Inspector 참조로 연결되어야 합니다.");
            Assert.That(Find(_sceneUi.transform, "Story Header"), Is.Null,
                "좌측 최상단 캐릭터 상태창은 Game HUD에 남아 있으면 안 됩니다.");

            var skillBarView = Find(_sceneUi.transform, "Action Bar")
                ?.GetComponent<KeyboardWandererSkillBarView>();
            Assert.That(skillBarView, Is.Not.Null,
                "Action Bar가 스킬 버튼 상태를 직접 소유해야 합니다.");
            Assert.That(skillBarView.IsReady, Is.True,
                "Action Bar의 버튼, 상태 표시, 단축키 참조는 Inspector에서 연결되어야 합니다.");

            var settingsView = Find(_sceneUi.transform, "Settings Screen")
                ?.GetComponent<KeyboardWandererSettingsView>();
            Assert.That(settingsView, Is.Not.Null,
                "Settings Screen이 슬라이더와 GM 토글을 직접 소유해야 합니다.");
            Assert.That(settingsView.IsReady, Is.True,
                "Settings Screen의 설정 컨트롤은 Inspector에서 연결되어야 합니다.");

            Button delete = Find(_sceneUi.transform, "Delete Skill Button").GetComponent<Button>();
            var stateView = delete.GetComponent<KeyboardWandererButtonStateView>();
            Outline outline = delete.targetGraphic.GetComponent<Outline>();
            Assert.That(stateView, Is.Not.Null);
            Assert.That(outline, Is.Not.Null);

            int componentCount = delete.GetComponents<Component>().Length;
            _sceneUi.SetAbilityState(AbilityKind.Delete, true, true);
            Assert.That(outline.enabled, Is.True);
            Assert.That(delete.transform.localScale.x, Is.GreaterThan(1f));
            _sceneUi.SetAbilityState(AbilityKind.Delete, false, false);
            Assert.That(outline.enabled, Is.False);
            Assert.That(delete.GetComponents<Component>().Length, Is.EqualTo(componentCount));
        }

        [UnityTest]
        public IEnumerator Title_WithSavedRun_DefaultsToContinueAndHasDeterministicNavigation()
        {
            const string serverRunIdKey = "keyboard-wanderer.server-run-id";
            bool hadServerRunId = KeyboardWandererPreferences.HasKey(serverRunIdKey);
            string originalServerRunId = KeyboardWandererPreferences.GetString(serverRunIdKey, string.Empty);
            KeyboardWandererTitleView title = Find(_sceneUi.transform, "Title Screen")
                .GetComponent<KeyboardWandererTitleView>();
            Button newRun = Find(title.transform, "New Run Button").GetComponent<Button>();
            Button continueRun = Find(title.transform, "Continue Button").GetComponent<Button>();
            Button settings = Find(title.transform, "Settings Button").GetComponent<Button>();
            KeyboardWandererRunSessionController session =
                _controller.GetComponent<KeyboardWandererRunSessionController>();
            try
            {
                // Exercise the complete Controller -> ScreenFlow -> TitleView order.
                // Earlier tests may have created a real checkpoint, so use a temporary
                // authoritative pointer and restore the original editor preference.
                session.RememberServerRun("title-focus-regression-fixture");
                title.gameObject.SetActive(false);
                EventSystem.current.SetSelectedGameObject(null);
                title.gameObject.SetActive(true);
                // The authored EventSystem starts on New Run. The first controller
                // presentation must override that stale prefab default.
                EventSystem.current.SetSelectedGameObject(newRun.gameObject);
                Invoke(_controller, "UpdateAuthoredUi");
                yield return null;

                Assert.That(session.HasContinue, Is.True);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(continueRun.gameObject),
                    "저장 런이 있으면 Enter의 기본 행동은 새 게임이 아니라 이어하기여야 합니다.");
                Assert.That(EventSystem.current.firstSelectedGameObject, Is.EqualTo(continueRun.gameObject),
                    "Input System의 첫 갱신도 이어하기 선택을 되돌리면 안 됩니다.");
                Assert.That(newRun.navigation.mode, Is.EqualTo(Navigation.Mode.Explicit));
                Assert.That(newRun.navigation.selectOnDown, Is.EqualTo(continueRun));
                Assert.That(continueRun.navigation.selectOnUp, Is.EqualTo(newRun));
                Assert.That(continueRun.navigation.selectOnDown, Is.EqualTo(settings));
                Assert.That(settings.navigation.selectOnUp, Is.EqualTo(continueRun));

                // Focus can be released by an overlay or the Input System between
                // frames. ScreenFlow must recover the semantic Continue default, not
                // the hierarchy's first (New Run) button.
                EventSystem.current.SetSelectedGameObject(null);
                Invoke(_controller, "UpdateAuthoredUi");
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(continueRun.gameObject),
                    "포커스 재획득 시 저장 런의 기본 이어하기 선택을 유지해야 합니다.");

                // Once the default is established, ordinary presentation refreshes
                // must not fight deliberate keyboard/gamepad navigation.
                EventSystem.current.SetSelectedGameObject(newRun.gameObject);
                Invoke(_controller, "UpdateAuthoredUi");
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(newRun.gameObject),
                    "반복 UI 갱신이 플레이어가 이동한 타이틀 선택을 되돌리면 안 됩니다.");
            }
            finally
            {
                if (hadServerRunId) KeyboardWandererPreferences.SetString(serverRunIdKey, originalServerRunId);
                else KeyboardWandererPreferences.DeleteKey(serverRunIdKey);
                KeyboardWandererPreferences.Save();
            }
        }

        [UnityTest]
        public IEnumerator EndingModal_RightThenSubmitUsesTitleAndBlocksTheBehindHud()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7399);
            var state = (RunState)GetField(service, "_state");
            state.Status = RunStatus.Completed;
            state.EndingCode = "ENDING_REWEAVE_TOGETHER";
            Invoke(_controller, "StartRun", service, false);
            Invoke(_controller, "UpdateAuthoredUi");
            yield return null;

            Transform ending = Find(_sceneUi.transform, "Ending Screen");
            Transform hud = Find(_sceneUi.transform, "Game HUD");
            Button newRun = Find(ending, "Ending New Run Button").GetComponent<Button>();
            Button title = Find(ending, "Ending Title Button").GetComponent<Button>();
            Button dialogueAdvance = Find(hud, "Next Dialogue Button").GetComponent<Button>();
            int leakedDialogueSubmits = 0;
            dialogueAdvance.onClick.AddListener(() => leakedDialogueSubmits++);

            Assert.That(ending.gameObject.activeSelf, Is.True);
            Assert.That(hud.gameObject.activeSelf, Is.True,
                "결과 화면은 HUD 위 모달이므로 뒤 HUD가 활성 상태여도 입력은 격리되어야 합니다.");
            Assert.That((bool)GetField(_controller.GetComponent<KeyboardWandererInputRouter>(), "_uiOverlayMode"),
                Is.True, "종료 모달은 게임플레이 입력 라우터보다 높은 우선순위를 가져야 합니다.");
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(newRun.gameObject));
            Assert.That(newRun.navigation.mode, Is.EqualTo(Navigation.Mode.Explicit));
            Assert.That(newRun.navigation.selectOnRight, Is.EqualTo(title));
            Assert.That(title.navigation.selectOnLeft, Is.EqualTo(newRun));

            var move = new AxisEventData(EventSystem.current)
            {
                moveDir = MoveDirection.Right,
                moveVector = Vector2.right
            };
            ExecuteEvents.Execute(newRun.gameObject, move, ExecuteEvents.moveHandler);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(title.gameObject),
                "Right/D-pad Right 뒤 실제 Submit 대상이 타이틀 버튼으로 바뀌어야 합니다.");
            yield return null;

            KeyboardWandererButtonStateView newRunVisual = newRun.GetComponent<KeyboardWandererButtonStateView>();
            KeyboardWandererButtonStateView titleVisual = title.GetComponent<KeyboardWandererButtonStateView>();
            Assert.That(newRunVisual, Is.Not.Null);
            Assert.That(titleVisual, Is.Not.Null);
            Assert.That(title.transform.localScale.x, Is.GreaterThan(newRun.transform.localScale.x),
                "화면의 강조 표시가 EventSystem의 실제 Submit 대상과 같아야 합니다.");

            ExecuteEvents.Execute(title.gameObject, new BaseEventData(EventSystem.current),
                ExecuteEvents.submitHandler);
            Invoke(_controller, "UpdateAuthoredUi");

            Assert.That(Find(_sceneUi.transform, "Title Screen").gameObject.activeSelf, Is.True,
                "오른쪽 버튼을 선택한 Return은 새 여정이 아니라 타이틀로 이동해야 합니다.");
            Assert.That(ending.gameObject.activeSelf, Is.False);
            Assert.That(leakedDialogueSubmits, Is.Zero,
                "모달 Submit이 뒤 대화 버튼에도 함께 전달되면 안 됩니다.");
        }

        [UnityTest]
        public IEnumerator PendingServerRequest_ShowsImmediateUnambiguousFeedback()
        {
            KeyboardWandererPreferences.SetInt("keyboard-wanderer.tutorial-v1-complete", 1);
            Invoke(_controller, "StartRun", LocalTurnService.CreateDemo(7361), false);
            yield return null;
            SetField(_controller, "_serverStatus", "안전 탐색 경로 검증 중");
            SetField(_controller, "_serverPending", true);

            Invoke(_controller, "UpdateAuthoredUi");

            TMP_Text actionHint = Find(_sceneUi.transform, "Action Hint").GetComponent<TMP_Text>();
            TMP_Text confirm = Find(_sceneUi.transform, "Confirm Action Label").GetComponent<TMP_Text>();
            Button confirmButton = Find(_sceneUi.transform, "Confirm Action Button").GetComponent<Button>();
            Assert.That(actionHint.text, Does.Contain("처리 중"));
            Assert.That(actionHint.text, Does.Contain("입력이 접수되었습니다"));
            Assert.That(actionHint.text, Does.Contain("안전 탐색 경로 검증 중"));
            Assert.That(confirm.text, Is.EqualTo("처리 중…"));
            Assert.That(confirmButton.interactable, Is.False);
        }

        [UnityTest]
        public IEnumerator AuthoredUi_RefreshesOneFlowSnapshotAndReusesDialogueContentCache()
        {
            KeyboardWandererPreferences.SetInt("keyboard-wanderer.tutorial-v1-complete", 1);
            Invoke(_controller, "StartRun", LocalTurnService.CreateDemo(7362), false);
            yield return null;
            ((TutorialPresenter)GetField(_controller, "_tutorialPresenter")).Start(false);
            SetField(_controller, "_lastStorySequence", new[]
            {
                new StorySequencePage("MONOLOGUE", "넙죽이", "캐시가 유지되는 장면")
            });
            SetField(_controller, "_lastNextInterventionReason", string.Empty);

            // Warm the presentation cache, then observe one complete UI pass without
            // yielding a frame where the controller's own Update could add revisions.
            Invoke(_controller, "UpdateAuthoredUi");
            int flowRevision = (int)GetField(_controller, "_flowPhaseRefreshRevision");
            int dialogueRevision = (int)GetField(_controller, "_dialoguePageCacheRevision");
            string[] dialoguePages = (string[])GetField(_controller, "_cachedDialoguePages");

            Invoke(_controller, "UpdateAuthoredUi");

            Assert.That(GetField(_controller, "_flowPhaseRefreshRevision"), Is.EqualTo(flowRevision + 1),
                "한 UI 렌더 패스에서 HUD·8개 스킬·확인 버튼이 각자 flow를 다시 계산하면 안 됩니다.");
            Assert.That(GetField(_controller, "_dialoguePageCacheRevision"), Is.EqualTo(dialogueRevision),
                "동일한 대화 내용을 다시 표시할 때 페이지와 signature를 다시 만들면 안 됩니다.");
            Assert.That(GetField(_controller, "_cachedDialoguePages"), Is.SameAs(dialoguePages));

            DialoguePresenter presenter = (DialoguePresenter)GetField(_controller, "_dialoguePresenter");
            presenter.Dismiss();
            Invoke(_controller, "UpdateAuthoredUi");
            Assert.That(presenter.IsDismissed, Is.True,
                "동일한 캐시 서명을 다시 표시하는 것만으로 사용자가 닫은 대화를 열면 안 됩니다.");

            SetField(_controller, "_playerWalking", true);
            SetField(_controller, "_lastStorySequence", new[]
            {
                new StorySequencePage("MONOLOGUE", "넙죽이", "이동 중 도착한 새 장면")
            });
            Invoke(_controller, "UpdateAuthoredUi");
            Assert.That(presenter.IsDismissed, Is.True);
            Assert.That(GetField(_controller, "_reopenDialogueAfterWalk"), Is.True,
                "이동 중 달라진 대화 내용은 즉시 열지 않고 보행 완료 뒤 reopen하도록 예약해야 합니다.");
        }

        [UnityTest]
        public IEnumerator RuntimeFontProvider_GrowsOnlyNonPersistentMultiAtlasForArbitraryHangul()
        {
            KeyboardWandererPreferences.SetInt("keyboard-wanderer.tutorial-v1-complete", 1);
            Invoke(_controller, "StartRun", LocalTurnService.CreateDemo(7360), false);
            yield return null;
            TMP_FontAsset persistentFont = Resources.Load<TMP_FontAsset>(
                "Fonts/NeoDunggeunmoPro-Regular SDF");
            Assert.That(persistentFont, Is.Not.Null);
            int persistentCharacterCount = persistentFont.characterTable.Count;
            int persistentAtlasCount = persistentFont.atlasTextureCount;
            Texture2D persistentAtlas = persistentFont.atlasTextures[0];
            Material persistentMaterial = persistentFont.material;

            TMP_FontAsset runtimeFont = _sceneUi.GetComponentsInChildren<TMP_Text>(true)[0].font;
            Assert.That(runtimeFont, Is.Not.SameAs(persistentFont));
            Assert.That(runtimeFont.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Dynamic));
            Assert.That(runtimeFont.isMultiAtlasTexturesEnabled, Is.True);
            var prewarmed = new HashSet<uint>(persistentFont.characterTable.Select(value => value.unicode));
            var characters = new StringBuilder(600);
            for (uint unicode = 0xAC00; unicode <= 0xD7A3 && characters.Length < 600; unicode++)
                if (!prewarmed.Contains(unicode))
                    characters.Append((char)unicode);
            Assert.That(characters.Length, Is.EqualTo(600));

            KeyboardWandererDialogueView dialogue =
                Find(_sceneUi.transform, "Story Panel").GetComponent<KeyboardWandererDialogueView>();
            dialogue.PresentChoices(true, Array.Empty<NarrativeChoiceOption>(), true);
            TMP_InputField freeform = Find(dialogue.transform, "Input").GetComponent<TMP_InputField>();
            freeform.text = characters.ToString();
            freeform.textComponent.ForceMeshUpdate();
            Canvas.ForceUpdateCanvases();
            yield return null;

            Assert.That(runtimeFont.atlasTextureCount, Is.GreaterThan(1),
                "두 번째 atlas를 실제로 만드는 입력으로 persistent 격리를 검증해야 합니다.");
            Assert.That(runtimeFont.atlasTextures[0], Is.Not.SameAs(persistentAtlas));
            Assert.That(runtimeFont.material, Is.Not.SameAs(persistentMaterial));
            Assert.That(runtimeFont.material.mainTexture, Is.SameAs(runtimeFont.atlasTextures[0]));
            Assert.That(persistentFont.characterTable.Count, Is.EqualTo(persistentCharacterCount));
            Assert.That(persistentFont.atlasTextureCount, Is.EqualTo(persistentAtlasCount));
            Assert.That(persistentFont.atlasTextures[0], Is.SameAs(persistentAtlas));
            Assert.That(persistentFont.material, Is.SameAs(persistentMaterial));
            Assert.That(_sceneUi.GetComponentsInChildren<TMP_Text>(true),
                Has.All.Matches<TMP_Text>(text => text.font != persistentFont),
                "실행 중인 UI 텍스트가 persistent 템플릿 폰트를 직접 참조하면 안 됩니다.");
        }

        [UnityTest]
        public IEnumerator LargeStorySubject_ReplacesCompactPortraitInsteadOfStackingDuplicateImages()
        {
            KeyboardWandererDialogueView dialogue =
                Find(_sceneUi.transform, "Story Panel").GetComponent<KeyboardWandererDialogueView>();
            Transform compactFrame = Find(dialogue.transform, "Speaker Portrait Frame");
            Transform largeStage = Find(dialogue.transform, "Encounter Subject Stage");
            Transform cutinBackdrop = Find(dialogue.transform, "Speaker Cutin Backdrop");
            Sprite portrait = _sceneUi.GetComponentsInChildren<Image>(true)
                .Select(image => image.sprite)
                .First(sprite => sprite != null);

            dialogue.Present(true, "화자", "장면의 중심 인물이 말한다.", "다음", true, portrait, true);
            Canvas.ForceUpdateCanvases();
            yield return null;

            Assert.That(largeStage.gameObject.activeSelf, Is.True);
            Assert.That(compactFrame.gameObject.activeSelf, Is.False,
                "큰 장면 초상과 같은 스프라이트를 쓰는 380px 화자 초상이 동시에 남으면 안 됩니다.");
            Assert.That(cutinBackdrop.gameObject.activeSelf, Is.False);

            dialogue.Present(true, "화자", "작은 대화 초상으로 돌아온다.", "다음", true, portrait, false);
            Canvas.ForceUpdateCanvases();
            yield return null;

            Assert.That(largeStage.gameObject.activeSelf, Is.False);
            Assert.That(compactFrame.gameObject.activeSelf, Is.True);
            Assert.That(cutinBackdrop.gameObject.activeSelf, Is.True);
        }

        [UnityTest]
        public IEnumerator NewRunButtonAndDialogue_ChangeVisibleAuthoredState()
        {
            // 머신의 PlayerPrefs 상태와 무관하게 튜토리얼이 대화 패널을 가리지 않도록 고정한다.
            KeyboardWandererPreferences.SetInt("keyboard-wanderer.tutorial-v1-complete", 1);
            Button newRun = Find(_sceneUi.transform, "New Run Button").GetComponent<Button>();
            newRun.onClick.Invoke();
            yield return null;

            GameObject gameHud = Find(_sceneUi.transform, "Game HUD").gameObject;
            Assert.That(gameHud.activeSelf || (bool)GetField(_controller, "_serverPending"), Is.True,
                "New Run button did not start either a server request or a local run.");
            if (!gameHud.activeSelf)
            {
                _controller.StopAllCoroutines();
                Invoke(_controller, "StartRun", LocalTurnService.CreateDemo(7303), false);
                yield return null;
            }
            Assert.That(gameHud.activeSelf, Is.True);
            // Search가 목표 스킬인 seed에서는 스토리 대화 페이지가 서사를 대신하므로 스킬을 고정한다.
            _controller.GetComponent<KeyboardWandererSelectionController>().ResetSelection(AbilityKind.Move);
            SetField(_controller, "_lastOutcome", "SUCCESS");
            SetField(_controller, "_lastNarrative", "첫 번째 이야기");
            SetField(_controller, "_lastDialogue", new[] { "두 번째 대화", "세 번째 대화" });
            SetField(_controller, "_lastStorySequence", System.Array.Empty<StorySequencePage>());
            SetField(_controller, "_lastNextInterventionReason", string.Empty);
            ((DialoguePresenter)GetField(_controller, "_dialoguePresenter")).Reset();
            Invoke(_controller, "UpdateAuthoredUi");

            TMP_Text story = Find(_sceneUi.transform, "Story Text").GetComponent<TMP_Text>();
            Assert.That(story.text, Is.EqualTo("첫 번째 이야기"));
            Find(_sceneUi.transform, "Next Dialogue Button").GetComponent<Button>().onClick.Invoke();
            Invoke(_controller, "UpdateAuthoredUi");
            Assert.That(((DialoguePresenter)GetField(_controller, "_dialoguePresenter")).IsDismissed, Is.True);
            Assert.That(Find(_sceneUi.transform, "Story Panel").gameObject.activeSelf, Is.False,
                "A completed one-page narrative should dismiss instead of revealing stale story-sequence data.");
        }

        [UnityTest]
        public IEnumerator PendingAction_HidesPreviousStoryAndChoiceSurfaces_UntilResultArrives()
        {
            KeyboardWandererPreferences.SetInt("keyboard-wanderer.tutorial-v1-complete", 1);
            Invoke(_controller, "StartRun", LocalTurnService.CreateDemo(7391), false);
            yield return null;
            ((TutorialPresenter)GetField(_controller, "_tutorialPresenter")).Start(false);
            SetField(_controller, "_lastOutcome", "SUCCESS");
            SetField(_controller, "_lastNarrative", "처리 직전의 대형 화자 컷인");
            SetField(_controller, "_lastStorySequence", Array.Empty<StorySequencePage>());
            SetField(_controller, "_lastNextInterventionReason", "다음 행동을 선택한다.");
            ((DialoguePresenter)GetField(_controller, "_dialoguePresenter")).Reset();
            Invoke(_controller, "UpdateAuthoredUi");

            GameObject storyPanel = Find(_sceneUi.transform, "Story Panel").gameObject;
            Assert.That(storyPanel.activeSelf, Is.True);

            SetField(_controller, "_serverPending", true);
            Invoke(_controller, "UpdateAuthoredUi");
            Assert.That(storyPanel.activeSelf, Is.False,
                "D20/서버 처리 중에는 이전 화자 컷인이 공격·조사 연출을 가리면 안 됩니다.");
            Assert.That(Find(storyPanel.transform, "Choice Strip").gameObject.activeSelf, Is.False,
                "처리 중인 이전 선택지는 보이지도, 클릭되지도 않아야 합니다.");

            SetField(_controller, "_serverPending", false);
            Invoke(_controller, "UpdateAuthoredUi");
            Assert.That(storyPanel.activeSelf, Is.True,
                "권위 결과가 도착하면 같은 서사 위치에서 패널이 복구되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator EventSystem_UsesProjectUiActionsAndRestoresScreenSelection()
        {
            InputSystemUIInputModule inputModule = Object.FindAnyObjectByType<InputSystemUIInputModule>();
            Assert.That(inputModule, Is.Not.Null);
            Assert.That(inputModule.actionsAsset, Is.Not.Null,
                "EventSystem must not retain a missing InputActionAsset GUID.");
            Assert.That(inputModule.move?.action?.name, Is.EqualTo("Navigate"));
            Assert.That(inputModule.submit?.action?.name, Is.EqualTo("Submit"));
            Assert.That(inputModule.cancel?.action?.name, Is.EqualTo("Cancel"));

            KeyboardWandererScreenFlowView flow = _sceneUi.GetComponent<KeyboardWandererScreenFlowView>();
            Assert.That(flow, Is.Not.Null);
            flow.Present(true, false, false, false, false);
            yield return null;
            GameObject titleSelection = EventSystem.current.currentSelectedGameObject;
            Assert.That(titleSelection, Is.Not.Null);
            Assert.That(titleSelection.transform.IsChildOf(Find(_sceneUi.transform, "Title Screen")), Is.True);

            flow.Present(false, true, false, false, false);
            GameObject settingsSelection = EventSystem.current.currentSelectedGameObject;
            Assert.That(settingsSelection, Is.Not.Null);
            Assert.That(settingsSelection.transform.IsChildOf(Find(_sceneUi.transform, "Settings Screen")), Is.True);

            flow.Present(true, false, false, false, false);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(titleSelection),
                "Returning to a screen should restore its last valid keyboard/gamepad selection.");

            flow.Present(false, false, true, true, false);
            GameObject pauseSelection = EventSystem.current.currentSelectedGameObject;
            Assert.That(Find(_sceneUi.transform, "Pause Screen").gameObject.activeSelf, Is.True);
            Assert.That(pauseSelection, Is.Not.Null);
            Assert.That(pauseSelection.transform.IsChildOf(Find(_sceneUi.transform, "Pause Screen")), Is.True);

            flow.Present(false, true, false, true, false);
            Assert.That(Find(_sceneUi.transform, "Settings Screen").gameObject.activeSelf, Is.True);
            Assert.That(Find(_sceneUi.transform, "Pause Screen").gameObject.activeSelf, Is.False,
                "Settings opened from pause must be the only raycastable top modal.");

            flow.Present(false, false, true, true, false);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(pauseSelection),
                "Escape/back from settings should restore the exact pause-menu selection.");
        }

        [UnityTest]
        public IEnumerator FourShortNarrativeChoices_AreAllVisibleWithoutUndisclosedScrolling()
        {
            KeyboardWandererPreferences.SetInt("keyboard-wanderer.tutorial-v1-complete", 1);
            Invoke(_controller, "StartRun", LocalTurnService.CreateDemo(7330), false);
            yield return null;
            KeyboardWandererDialogueView dialogue =
                Find(_sceneUi.transform, "Story Panel").GetComponent<KeyboardWandererDialogueView>();
            dialogue.PresentChoices(true, new[]
            {
                new NarrativeChoiceOption("choice-1", "첫 번째 행동을 선택한다.", "DIALOGUE"),
                new NarrativeChoiceOption("choice-2", "두 번째 행동을 선택한다.", "DIALOGUE"),
                new NarrativeChoiceOption("choice-3", "세 번째 행동을 선택한다.", "DIALOGUE"),
                new NarrativeChoiceOption("choice-4", "네 번째 공격 행동을 선택한다.", "SKILL", skillId: "DELETE")
            }, true);
            Canvas.ForceUpdateCanvases();

            Transform strip = Find(dialogue.transform, "Choice Strip");
            ScrollRect scroll = Find(strip, "Choice Scroll").GetComponent<ScrollRect>();
            Bounds viewportBounds = new Bounds(scroll.viewport.rect.center, scroll.viewport.rect.size);
            for (int i = 1; i <= 4; i++)
            {
                RectTransform choice = Find(strip, "Choice " + i) as RectTransform;
                Bounds choiceBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, choice);
                Assert.That(choiceBounds.min.y, Is.GreaterThanOrEqualTo(viewportBounds.min.y - 1f),
                    "짧은 네 개 선택지 중 아래 항목이 첫 화면에서 숨으면 안 됩니다: " + i);
                Assert.That(choiceBounds.max.y, Is.LessThanOrEqualTo(viewportBounds.max.y + 1f));
            }
        }

        [UnityTest]
        public IEnumerator LongNarrativeChoices_ExposeEveryCharacterInScrollableVariableHeightTargets()
        {
            KeyboardWandererPreferences.SetInt("keyboard-wanderer.tutorial-v1-complete", 1);
            Invoke(_controller, "StartRun", LocalTurnService.CreateDemo(7331), false);
            yield return null;
            KeyboardWandererDialogueView dialogue =
                Find(_sceneUi.transform, "Story Panel").GetComponent<KeyboardWandererDialogueView>();
            string longKorean = "이 선택지는 예상보다 훨씬 긴 한국어 문장이어도 잘리거나 옆 선택지와 겹치지 않고 두 줄 안에서 읽혀야 합니다.";
            dialogue.PresentChoices(true, new[]
            {
                new NarrativeChoiceOption("long-1", longKorean, "DIALOGUE"),
                new NarrativeChoiceOption("long-2", longKorean + " 두 번째 문장", "DIALOGUE"),
                new NarrativeChoiceOption("long-3", longKorean + " 세 번째 문장", "DIALOGUE"),
                new NarrativeChoiceOption("long-4", longKorean + " 네 번째 문장", "DIALOGUE")
            }, true);
            Canvas.ForceUpdateCanvases();

            Transform strip = Find(dialogue.transform, "Choice Strip");
            Assert.That(strip, Is.Not.Null);
            ScrollRect scroll = Find(strip, "Choice Scroll").GetComponent<ScrollRect>();
            Assert.That(scroll, Is.Not.Null);
            Assert.That(scroll.vertical, Is.True);
            Assert.That(scroll.horizontal, Is.False);
            for (int i = 1; i <= 4; i++)
            {
                RectTransform rect = Find(strip, "Choice " + i) as RectTransform;
                TMP_Text label = rect.GetComponentInChildren<TMP_Text>(true);
                Assert.That(rect.rect.height, Is.GreaterThanOrEqualTo(44f), "Choice " + i + " hit target is too short.");
                Assert.That(label.textWrappingMode, Is.EqualTo(TextWrappingModes.Normal));
                Assert.That(label.overflowMode, Is.EqualTo(TextOverflowModes.Overflow));
                Assert.That(rect.rect.height, Is.GreaterThanOrEqualTo(label.preferredHeight + 10f),
                    "선택지 원문 전체 높이가 클릭 영역 안에 포함되어야 합니다.");
            }
            Assert.That(scroll.content.rect.height, Is.GreaterThan(0f));
            // A taller viewport can expose all four long choices without scrolling.
            // When the content does overflow, the same ScrollRect must still bring
            // the keyboard-selected final choice into view below.

            dialogue.MoveChoiceSelection(1);
            dialogue.MoveChoiceSelection(1);
            dialogue.MoveChoiceSelection(1);
            Canvas.ForceUpdateCanvases();
            RectTransform fourth = Find(strip, "Choice 4") as RectTransform;
            Bounds viewportBounds = new Bounds(scroll.viewport.rect.center, scroll.viewport.rect.size);
            Bounds fourthBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, fourth);
            Assert.That(fourthBounds.min.y, Is.GreaterThanOrEqualTo(viewportBounds.min.y - 1f),
                "키보드로 선택한 마지막 항목은 자동 스크롤되어 화면 안에 보여야 합니다.");
            Assert.That(fourthBounds.max.y, Is.LessThanOrEqualTo(viewportBounds.max.y + 1f));
            RectTransform input = Find(strip, "Input") as RectTransform;
            RectTransform send = Find(strip, "Send") as RectTransform;
            Assert.That(input.rect.height, Is.GreaterThanOrEqualTo(44f));
            Assert.That(send.rect.height, Is.GreaterThanOrEqualTo(44f));
            RectTransform freeformRow = Find(strip, "Freeform Input") as RectTransform;
            Bounds choiceScrollBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                strip, scroll.transform);
            Bounds freeformBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                strip, freeformRow);
            Assert.That(freeformBounds.max.y, Is.LessThanOrEqualTo(choiceScrollBounds.min.y + 0.5f),
                "작은 화면에서도 선택지 ScrollRect와 자유입력 영역이 겹치면 안 됩니다.");
            Bounds inputBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(freeformRow, input);
            Bounds sendBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(freeformRow, send);
            Assert.That(inputBounds.max.x, Is.LessThanOrEqualTo(sendBounds.min.x + 0.5f),
                "다중행 입력 viewport가 전송 버튼의 클릭 영역을 침범하면 안 됩니다.");

            TMP_InputField freeform = input.GetComponent<TMP_InputField>();
            Assert.That(freeform.lineType, Is.EqualTo(TMP_InputField.LineType.MultiLineSubmit));
            Assert.That(freeform.textViewport, Is.Not.Null);
            Assert.That(freeform.textViewport.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(freeform.verticalScrollbar, Is.Not.Null);
            Assert.That(freeform.textComponent.textWrappingMode, Is.EqualTo(TextWrappingModes.Normal));
            TMP_FontAsset persistentFont = Resources.Load<TMP_FontAsset>(
                "Fonts/NeoDunggeunmoPro-Regular SDF");
            Assert.That(persistentFont, Is.Not.Null);
            uint absentUnicode = 0;
            for (uint unicode = 0xAC00; unicode <= 0xD7A3; unicode++)
            {
                if (persistentFont.characterTable.Any(character => character.unicode == unicode)) continue;
                absentUnicode = unicode;
                break;
            }
            Assert.That(absentUnicode, Is.Not.Zero,
                "Fixture needs a Hangul glyph that is not already in the prewarmed project atlas.");
            int persistentCharacterCount = persistentFont.characterTable.Count;
            string longDraft = new string((char)absentUnicode, 133);
            freeform.text = longDraft;
            freeform.textComponent.ForceMeshUpdate();
            Canvas.ForceUpdateCanvases();
            TMP_Text counter = Find(input, "Character Count").GetComponent<TMP_Text>();
            Assert.That(counter.text, Is.EqualTo("133/1000자"));
            Assert.That(freeform.textComponent.textInfo.lineCount, Is.GreaterThanOrEqualTo(4),
                "133자 한국어 초안은 단일 행 가로 스크롤이 아니라 실제 여러 줄로 감겨야 합니다.");
            float singleLineHeight = freeform.textComponent.GetPreferredValues("한", 0f, 0f).y;
            Assert.That(freeform.textViewport.rect.height, Is.GreaterThanOrEqualTo(singleLineHeight * 2f),
                "입력 viewport는 최소 두 줄을 동시에 검토할 수 있어야 합니다.");
            Assert.That(freeform.textComponent.preferredHeight,
                Is.GreaterThan(freeform.textViewport.rect.height),
                "긴 초안이 viewport를 넘을 때 세로 스크롤할 실제 내용이 있어야 합니다.");
            Assert.That(freeform.textComponent.font, Is.Not.SameAs(persistentFont));
            Assert.That(freeform.textComponent.font.characterTable,
                Has.Some.Matches<TMP_Character>(character => character.unicode == absentUnicode),
                "사용자가 입력한 미사전생성 한글은 런타임 전용 atlas에 추가되어야 합니다.");
            Assert.That(persistentFont.characterTable.Count, Is.EqualTo(persistentCharacterCount),
                "입력 렌더링이 프로젝트 TMP 에셋의 글리프 테이블을 변경하면 안 됩니다.");
            float scrollBefore = freeform.verticalScrollbar.value;
            var scrollEvent = new PointerEventData(EventSystem.current)
            {
                scrollDelta = new Vector2(0f, -1f)
            };
            freeform.OnScroll(scrollEvent);
            Assert.That(freeform.verticalScrollbar.value, Is.GreaterThan(scrollBefore),
                "마우스 휠로 긴 자유입력의 아래 문장까지 검토할 수 있어야 합니다.");

            dialogue.FocusFreeformInput();
            int selectedBeforeTyping = dialogue.KeyboardChoiceIndex;
            dialogue.MoveChoiceSelection(1);
            dialogue.ConfirmChoiceSelection();
            Assert.That(dialogue.KeyboardChoiceIndex, Is.EqualTo(selectedBeforeTyping),
                "자유입력 포커스 중 W/S/Enter 소유권이 선택지로 새면 안 됩니다.");
        }

        [UnityTest]
        public IEnumerator StartRun_HardResetsStaleChoiceTextFocusAndRaycastModal()
        {
            const string tutorialKey = "keyboard-wanderer.tutorial-v1-complete";
            bool hadTutorial = KeyboardWandererPreferences.HasKey(tutorialKey);
            int oldTutorial = KeyboardWandererPreferences.GetInt(tutorialKey, 0);
            try
            {
                KeyboardWandererPreferences.SetInt(tutorialKey, 1);
                Invoke(_controller, "StartRun", LocalTurnService.CreateDemo(7332), false);
                yield return null;
                KeyboardWandererDialogueView dialogue =
                    Find(_sceneUi.transform, "Story Panel").GetComponent<KeyboardWandererDialogueView>();
                var staleChoices = new[]
                {
                    new NarrativeChoiceOption("stale-1", "이전 서버 선택 하나", "DIALOGUE"),
                    new NarrativeChoiceOption("stale-2", "이전 서버 선택 둘", "DIALOGUE")
                };
                dialogue.PresentChoices(true, staleChoices, true);
                Transform strip = Find(dialogue.transform, "Choice Strip");
                TMP_InputField input = Find(strip, "Input").GetComponent<TMP_InputField>();
                Assert.That(input.characterLimit, Is.EqualTo(1000));
                input.text = "이전 런에서 작성 중이던 문장";
                dialogue.FocusFreeformInput();
                Assert.That(InputFocusTracker.HasFocusedTextField, Is.True,
                    "Fixture failed to establish a real focused TMP input before the run reset.");

                KeyboardWandererPreferences.SetInt(tutorialKey, 0);
                Invoke(_controller, "StartRun", LocalTurnService.CreateDemo(7333), false);

                Assert.That(strip.gameObject.activeSelf, Is.False,
                    "The complete choice modal root must be disabled so it cannot intercept HUD raycasts.");
                Assert.That(input.text, Is.Empty);
                Assert.That(input.interactable, Is.False);
                Assert.That(InputFocusTracker.HasFocusedTextField, Is.False,
                    "A hidden input must release the global text-focus lock used by WASD.");
                GameObject selected = EventSystem.current.currentSelectedGameObject;
                Assert.That(selected == null || !selected.transform.IsChildOf(strip), Is.True,
                    "EventSystem selection still belongs to the hidden previous-run choice modal.");
                Assert.That(GetField(_controller.GetComponent<KeyboardWandererInputRouter>(), "_narrativeChoiceMode"),
                    Is.False);
                Assert.That((NarrativeChoiceOption[])GetField(dialogue, "_choiceOptions"),
                    Has.All.Null);

                dialogue.PresentChoices(true, staleChoices, false);
                Assert.That(strip.gameObject.activeSelf, Is.True,
                    "Fixture failed to reintroduce the stale choice modal for tutorial defense.");
                Invoke(_controller, "UpdateAuthoredUi");
                Assert.That(strip.gameObject.activeSelf, Is.False,
                    "The tutorial render path must defensively hide any stale choice blocker.");
            }
            finally
            {
                if (hadTutorial) KeyboardWandererPreferences.SetInt(tutorialKey, oldTutorial);
                else KeyboardWandererPreferences.DeleteKey(tutorialKey);
                KeyboardWandererPreferences.Save();
            }
        }

        [UnityTest]
        public IEnumerator IntroCutscene_LetterboxesFourByThreeAndUltrawideInsideSafeInputLayer()
        {
            var fixture = new GameObject("Intro Aspect Fixture");
            var texture = new Texture2D(1672, 941);
            Sprite sprite = null;
            try
            {
                sprite = Sprite.Create(texture, new Rect(0f, 0f, 1672f, 941f), Vector2.one * 0.5f);
                KeyboardWandererCutsceneOverlayView.Play(fixture.transform, new[] { sprite }, () => { });
                yield return null;

                Transform overlay = Find(fixture.transform, "Cutscene Overlay");
                Image frame = Find(overlay, "Frame Bottom").GetComponent<Image>();
                Image top = Find(overlay, "Frame Top").GetComponent<Image>();
                Image background = Find(overlay, "Letterbox Background").GetComponent<Image>();
                Transform safeArea = Find(overlay, "Safe Area Content");
                RectTransform catcher = Find(overlay, "Click Catcher") as RectTransform;
                RectTransform hint = Find(overlay, "Continue Hint") as RectTransform;
                Assert.That(frame.preserveAspect, Is.True);
                Assert.That(top.preserveAspect, Is.True);
                Assert.That(background.color, Is.EqualTo(Color.black));
                Assert.That(catcher.IsChildOf(safeArea), Is.True);
                Assert.That(hint.IsChildOf(safeArea), Is.True);
                Assert.That(safeArea.GetSiblingIndex(), Is.GreaterThan(top.transform.GetSiblingIndex()),
                    "계속 힌트는 전체 화면 컷신 프레임보다 위에서 렌더링되어야 합니다.");
                Assert.That(safeArea.GetComponent<KeyboardWandererSafeAreaFitter>(), Is.Not.Null);
                Assert.That(catcher.anchorMin, Is.EqualTo(Vector2.zero));
                Assert.That(catcher.anchorMax, Is.EqualTo(Vector2.one));
                Assert.That(hint.anchorMin.x, Is.GreaterThanOrEqualTo(0f));
                Assert.That(hint.anchorMax.x, Is.LessThanOrEqualTo(1f));

                AssertPreservedFrameBounds(frame, 1024f, 768f, 1672f / 941f);
                AssertPreservedFrameBounds(frame, 2520f, 1080f, 1672f / 941f);
            }
            finally
            {
                Object.DestroyImmediate(fixture);
                if (sprite != null) Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }

        [UnityTest]
        public IEnumerator InventoryAndQuests_ExposeAllAuthoritativeEntriesDetailsStatusesAndDialogueInsert()
        {
            const string tutorialKey = "keyboard-wanderer.tutorial-v1-complete";
            bool hadTutorial = KeyboardWandererPreferences.HasKey(tutorialKey);
            int oldTutorial = KeyboardWandererPreferences.GetInt(tutorialKey, 0);
            try
            {
                KeyboardWandererPreferences.SetInt(tutorialKey, 1);
                Invoke(_controller, "StartRun", LocalTurnService.CreateDemo(7350), false);
                yield return null;
                // Freeze the authored run after its normal startup pass. This fixture
                // intentionally supplies a large authoritative snapshot directly to
                // the view; leaving Update enabled would replace it on the next frame
                // with the demo service's one-item snapshot and make the assertion
                // inspect deferred, inactive rows from two different snapshots.
                _controller.enabled = false;
                KeyboardWandererInventoryQuestView view =
                    _sceneUi.GetComponentInChildren<KeyboardWandererInventoryQuestView>(true);
                KeyboardWandererDialogueView dialogue =
                    Find(_sceneUi.transform, "Story Panel").GetComponent<KeyboardWandererDialogueView>();
                Assert.That(view, Is.Not.Null);

                var items = new List<RunPresentationItem>();
                for (int i = 0; i < 24; i++)
                {
                    bool equipment = i < 5;
                    string name = i == 23
                        ? "아주 긴 이름을 가진 마지막 복구용 관리자 도구와 기억의 파편"
                        : "테스트 아이템 " + (i + 1);
                    string description = i == 23
                        ? "이 도구는 마지막 장면에서 왜곡된 연결을 복구하고 플레이어가 선택한 대화의 근거를 남깁니다. " +
                          new string('길', 260)
                        : "아이템 효과와 용도를 설명하는 문장 " + (i + 1);
                    items.Add(PresentationItem("item-" + i, equipment ? "tool" : "material",
                        name, description, i + 1, equipment));
                }
                var quests = new List<RunPresentationQuest>();
                string[] statuses = { "active", "completed", "abandoned", "failed", "active", "completed", "active", "completed" };
                for (int i = 0; i < 8; i++)
                    quests.Add(PresentationQuest("quest-" + i, "퀘스트 " + (i + 1),
                        i == 7 ? "마지막 퀘스트의 긴 권위 요약 " + new string('설', 220) : "권위 서버 요약 " + i,
                        i % 2 == 0 ? "discover" : "completed", "story", statuses[i]));
                view.Present(items, quests);
                dialogue.PresentChoices(true, Array.Empty<NarrativeChoiceOption>(), true);
                TMP_InputField freeform = Find(dialogue.transform, "Input").GetComponent<TMP_InputField>();

                _controller.UiToggleInventory();
                yield return null;
                Transform overlay = Find(_sceneUi.transform, "Inventory Quest Overlay");
                ScrollRect listScroll = Find(overlay, "Inventory Quest Scroll").GetComponent<ScrollRect>();
                Transform[] inventoryTransforms = listScroll.content.GetComponentsInChildren<Transform>(true);
                Assert.That(inventoryTransforms.Count(value => value.name.StartsWith("Item ")), Is.EqualTo(24),
                    "서버 public inventory의 24개 항목을 하나도 버리면 안 됩니다.");
                for (int i = 0; i < 5; i++)
                    Assert.That(Find(listScroll.content, "Item item-" + i), Is.Not.Null,
                        "다섯 번째 이상 장비도 목록에서 접근 가능해야 합니다.");
                Canvas.ForceUpdateCanvases();
                Assert.That(listScroll.content.rect.height, Is.GreaterThan(listScroll.viewport.rect.height));

                Transform lastItem = Find(listScroll.content, "Item item-23");
                lastItem.GetComponent<Button>().onClick.Invoke();
                TMP_Text rowName = Find(lastItem, "Name").GetComponent<TMP_Text>();
                TMP_FontAsset persistentFont = Resources.Load<TMP_FontAsset>(
                    "Fonts/NeoDunggeunmoPro-Regular SDF");
                Assert.That(rowName.font, Is.SameAs(dialogue.StoryText.font));
                Assert.That(rowName.font, Is.Not.SameAs(persistentFont),
                    "서버 이름으로 생성되는 동적 인벤토리 행도 runtime-only 폰트를 사용해야 합니다.");
                Assert.That(rowName.font.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Dynamic));
                Assert.That(rowName.font.HasCharacter('테'), Is.True,
                    "persistent 템플릿에 없던 서버 이름 글리프를 runtime clone이 추가해야 합니다.");
                TMP_Text detailTitle = Find(overlay, "Detail Title").GetComponent<TMP_Text>();
                TMP_Text detailBody = Find(overlay, "Detail Body").GetComponent<TMP_Text>();
                Assert.That(detailTitle.text, Does.Contain(items[23].Name));
                Assert.That(detailBody.text, Does.Contain("수량 · 24"));
                Assert.That(detailBody.text, Does.Contain(items[23].Description));
                Assert.That(detailBody.overflowMode, Is.EqualTo(TextOverflowModes.Overflow));
                Canvas.ForceUpdateCanvases();
                ScrollRect detailScroll = Find(overlay, "Detail Scroll").GetComponent<ScrollRect>();
                Assert.That(detailScroll.content.rect.height, Is.GreaterThan(detailScroll.viewport.rect.height),
                    "긴 아이템 설명은 상세 패널 안에서 스크롤해 끝까지 읽을 수 있어야 합니다.");

                Find(overlay, "Insert Into Dialogue").GetComponent<Button>().onClick.Invoke();
                Assert.That(view.IsOpen, Is.False);
                Assert.That(freeform.text, Does.Contain(items[23].Name),
                    "명시적인 대화 삽입 버튼 뒤에만 선택 아이템 이름이 자유입력에 들어가야 합니다.");

                _controller.UiToggleQuests();
                yield return null;
                Transform[] questTransforms = listScroll.content.GetComponentsInChildren<Transform>(true);
                Assert.That(questTransforms.Count(value => value.name.StartsWith("Quest ")), Is.EqualTo(8),
                    "완료/포기 항목을 포함한 전체 퀘스트 기록을 보여야 합니다.");
                TMP_Text[] questTexts = listScroll.content.GetComponentsInChildren<TMP_Text>(true);
                Assert.That(questTexts, Has.Some.Matches<TMP_Text>(text => text.text == "[완료]"));
                Assert.That(questTexts, Has.Some.Matches<TMP_Text>(text => text.text == "[포기]"));
                Assert.That(questTexts, Has.Some.Matches<TMP_Text>(text => text.text == "[실패]"));
                Find(listScroll.content, "Quest 7 · quest-7").GetComponent<Button>().onClick.Invoke();
                Assert.That(detailTitle.text, Does.StartWith("[완료]"));
                Assert.That(detailBody.text, Does.Contain(quests[7].Summary));
                Assert.That(Find(overlay, "Insert Into Dialogue").gameObject.activeSelf, Is.False,
                    "퀘스트 상세에는 아이템 삽입 행동을 잘못 노출하면 안 됩니다.");
            }
            finally
            {
                if (hadTutorial) KeyboardWandererPreferences.SetInt(tutorialKey, oldTutorial);
                else KeyboardWandererPreferences.DeleteKey(tutorialKey);
                KeyboardWandererPreferences.Save();
            }
        }

        [Test]
        public void SafeAreaFitter_NormalizesSixteenByNineFourByThreeAndUltrawideWithoutRebuildLoop()
        {
            string[] screenNames = { "Title Screen", "Game HUD", "Settings Screen", "Pause Screen", "Ending Screen" };
            foreach (string screenName in screenNames)
                Assert.That(Find(_sceneUi.transform, screenName).GetComponent<KeyboardWandererSafeAreaFitter>(),
                    Is.Not.Null, screenName + " is missing safe-area protection.");

            var fixture = new GameObject("Safe Area Fixture", typeof(RectTransform),
                typeof(KeyboardWandererSafeAreaFitter));
            try
            {
                RectTransform rect = fixture.GetComponent<RectTransform>();
                KeyboardWandererSafeAreaFitter fitter = fixture.GetComponent<KeyboardWandererSafeAreaFitter>();
                Assert.That(fitter.ApplySafeArea(new Rect(0f, 0f, 1920f, 1080f), new Vector2Int(1920, 1080)), Is.True);
                Assert.That(rect.anchorMin, Is.EqualTo(Vector2.zero));
                Assert.That(rect.anchorMax, Is.EqualTo(Vector2.one));
                Assert.That(fitter.ApplySafeArea(new Rect(0f, 0f, 1920f, 1080f), new Vector2Int(1920, 1080)), Is.False,
                    "Unchanged metrics must not dirty the RectTransform every frame.");

                Assert.That(fitter.ApplySafeArea(new Rect(0f, 0f, 1024f, 768f), new Vector2Int(1024, 768)), Is.True);
                Assert.That(rect.anchorMin, Is.EqualTo(Vector2.zero));
                Assert.That(rect.anchorMax, Is.EqualTo(Vector2.one));

                Assert.That(fitter.ApplySafeArea(new Rect(120f, 0f, 3200f, 1440f), new Vector2Int(3440, 1440)), Is.True);
                Assert.That(rect.anchorMin.x, Is.EqualTo(120f / 3440f).Within(0.0001f));
                Assert.That(rect.anchorMax.x, Is.EqualTo(3320f / 3440f).Within(0.0001f));
                Assert.That(rect.anchorMin.y, Is.Zero);
                Assert.That(rect.anchorMax.y, Is.EqualTo(1f));
                Assert.That(rect.offsetMin, Is.EqualTo(Vector2.zero));
                Assert.That(rect.offsetMax, Is.EqualTo(Vector2.zero));
            }
            finally
            {
                Object.DestroyImmediate(fixture);
            }
        }

        [UnityTest]
        public IEnumerator Minimap_RedrawsWhenEnemySelectionChanges()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7306);
            Invoke(_controller, "StartRun", service, false);
            yield return null;
            KeyboardWandererMinimapRenderer renderer =
                Object.FindAnyObjectByType<KeyboardWandererMinimapRenderer>(FindObjectsInactive.Include);
            Assert.That(renderer, Is.Not.Null);
            string before = renderer.Signature;
            EntityView enemy = service.CurrentView.Entities.First(entity => entity.IsHostile);

            _controller.GetComponent<KeyboardWandererSelectionController>().SelectPrimary(enemy.EntityId);
            yield return null;

            Assert.That(renderer.Signature, Is.Not.EqualTo(before));
            Assert.That(renderer.Signature, Does.Contain(enemy.EntityId.ToString("N")));
        }

        [UnityTest]
        public IEnumerator AuthoredHud_CapturesSixteenByNineAndFourByThree()
        {
            Invoke(_controller, "StartRun", LocalTurnService.CreateDemo(7305), false);
            yield return null;
            Camera camera = Object.FindAnyObjectByType<Camera>();
            Canvas canvas = _sceneUi.GetComponentInParent<Canvas>();
            Assert.That(camera, Is.Not.Null);
            Assert.That(canvas, Is.Not.Null);
            CaptureAtResolution(camera, canvas, 960, 540, "/tmp/KeyboardWanderer-small-16x9.png");
            CaptureAtResolution(camera, canvas, 1600, 900, "/tmp/KeyboardWanderer-16x9.png");
            CaptureAtResolution(camera, canvas, 2560, 1440, "/tmp/KeyboardWanderer-high-16x9.png");
            CaptureAtResolution(camera, canvas, 1024, 768, "/tmp/KeyboardWanderer-4x3.png");
            CaptureAtResolution(camera, canvas, 2560, 1080, "/tmp/KeyboardWanderer-ultrawide.png");
        }

        private static void CaptureAtResolution(Camera camera, Canvas canvas, int width, int height, string path)
        {
            if (File.Exists(path)) File.Delete(path);
            RenderMode originalMode = canvas.renderMode;
            Camera originalCanvasCamera = canvas.worldCamera;
            bool originalOverrideSorting = canvas.overrideSorting;
            int originalSortingOrder = canvas.sortingOrder;
            RenderTexture originalTarget = camera.targetTexture;
            RenderTexture originalActive = RenderTexture.active;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.overrideSorting = true;
                canvas.sortingOrder = 2000;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = originalTarget;
                canvas.worldCamera = originalCanvasCamera;
                canvas.renderMode = originalMode;
                canvas.overrideSorting = originalOverrideSorting;
                canvas.sortingOrder = originalSortingOrder;
                RenderTexture.active = originalActive;
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(target);
            }
            Assert.That(File.Exists(path), Is.True, "Screenshot was not written: " + path);
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(1024));
        }

        private static Transform Find(Transform root, string name)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform item in transforms)
                if (item.name == name)
                    return item;
            return null;
        }

        private static void AssertPreservedFrameBounds(Image image, float width, float height,
            float expectedAspect)
        {
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = Vector2.one * 0.5f;
            rect.sizeDelta = new Vector2(width, height);
            Canvas.ForceUpdateCanvases();
            MethodInfo drawingMethod = typeof(Image).GetMethod("GetDrawingDimensions",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(drawingMethod, Is.Not.Null);
            Vector4 dimensions = (Vector4)drawingMethod.Invoke(image, new object[] { true });
            float drawnWidth = dimensions.z - dimensions.x;
            float drawnHeight = dimensions.w - dimensions.y;
            Assert.That(drawnWidth, Is.LessThanOrEqualTo(width + 0.5f));
            Assert.That(drawnHeight, Is.LessThanOrEqualTo(height + 0.5f));
            Assert.That(drawnWidth / drawnHeight, Is.EqualTo(expectedAspect).Within(0.002f),
                "컷신 원본 비율은 화면 비율과 무관하게 유지되어야 합니다.");
            Assert.That(Mathf.Abs(drawnWidth - width) > 0.5f || Mathf.Abs(drawnHeight - height) > 0.5f,
                Is.True, "4:3/21:9에서는 검은 letterbox/pillarbox 여백이 있어야 합니다.");
        }

        private static RunPresentationItem PresentationItem(string id, string kind, string name,
            string description, int quantity, bool isProtected)
        {
            var item = new RunPresentationItem();
            SetModelProperty(item, "Id", id);
            SetModelProperty(item, "Kind", kind);
            SetModelProperty(item, "Name", name);
            SetModelProperty(item, "Description", description);
            SetModelProperty(item, "Quantity", quantity);
            SetModelProperty(item, "IsProtected", isProtected);
            return item;
        }

        private static RunPresentationQuest PresentationQuest(string id, string title, string summary,
            string step, string kind, string status)
        {
            var quest = new RunPresentationQuest();
            SetModelProperty(quest, "Id", id);
            SetModelProperty(quest, "Title", title);
            SetModelProperty(quest, "Summary", summary);
            SetModelProperty(quest, "CurrentStep", step);
            SetModelProperty(quest, "Kind", kind);
            SetModelProperty(quest, "Status", status);
            return quest;
        }

        private static void SetModelProperty(object target, string name, object value)
        {
            PropertyInfo property = target.GetType().GetProperty(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null, "Missing model property " + name);
            property.SetValue(target, value);
        }

        private static object Invoke(object target, string methodName, params object[] values)
        {
            MethodInfo method = target.GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return method.Invoke(target, values);
        }

        private static object GetField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return field.GetValue(target);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
