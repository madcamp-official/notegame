using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using KeyboardWanderer.Core;
using KeyboardWanderer.Demo;
using KeyboardWanderer.Gameplay;
using KeyboardWanderer.Networking;
using KeyboardWanderer.Presentation;
using KeyboardWanderer.Runtime;
using KeyboardWanderer.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Tilemaps;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace KeyboardWanderer.Tests.PlayMode
{
    public sealed class ControllerFlowPlayModeTests
    {
        private const string GmEnabledKey = "keyboard-wanderer.gm-enabled";
        private static readonly Guid StandardDeleteEnemyId =
            Guid.Parse("00000000-0000-0000-0000-000000000001");
        private static readonly Guid RootProcessEnemyId =
            Guid.Parse("00000000-0000-0000-0000-000000000002");
        private GameObject _controllerObject;
        private KeyboardWandererAuthoringSettings _authoringSettings;
        private bool _hadGmSetting;
        private int _oldGmSetting;

        [SetUp]
        public void SetUp()
        {
            LocalRunSaveService.Delete();
            _hadGmSetting = KeyboardWandererPreferences.HasKey(GmEnabledKey);
            _oldGmSetting = KeyboardWandererPreferences.GetInt(GmEnabledKey, 1);
        }

        [TearDown]
        public void TearDown()
        {
            LocalRunSaveService.Delete();
            if (_controllerObject != null) UnityEngine.Object.DestroyImmediate(_controllerObject);
            if (_authoringSettings != null) UnityEngine.Object.DestroyImmediate(_authoringSettings);
            if (_hadGmSetting) KeyboardWandererPreferences.SetInt(GmEnabledKey, _oldGmSetting);
            else KeyboardWandererPreferences.DeleteKey(GmEnabledKey);
            KeyboardWandererPreferences.Save();
        }

        [UnityTest]
        public IEnumerator Controller_SubmitsCanonicalSkill_AndResumePreservesAuthoritativeState()
        {
            RunState state = LocalTurnService.CreateDemo(7301).CreateSnapshot();
            EntityState keyboard = state.Spatial.Entities.Single(entity =>
                entity.AssetId == CampaignCatalog.AdministratorKeyboardId);
            var service = new LocalTurnService(state, new SequenceD20Source(20));

            KeyboardWandererDemoController controller = CreateAuthoredController("Codria Controller PlayMode Test");
            yield return null;

            Invoke(controller, "StartRun", service, false);
            controller.UiSetGmEnabled(false);
            MoveToLegacyChoiceBoundary(controller);
            KeyboardWandererSelectionController selection = Selection(controller);
            selection.ResetSelection(AbilityKind.Search);
            selection.SelectPrimary(keyboard.EntityId);
            Invoke(controller, "Submit");
            yield return null;

            Assert.That(service.CurrentView.CurrentTurn, Is.EqualTo(1));
            Assert.That(service.CurrentView.LastIntentText, Does.Contain("관리자 키보드"));
            Assert.That(service.CurrentView.LastIntentAlignment, Is.Zero);
            Assert.That(service.CurrentView.Region.LayoutHash, Is.Not.Empty);

            string json = LocalRunSaveService.Serialize(service);
            LocalTurnService resumed = LocalRunSaveService.Deserialize(json);
            Assert.That(resumed.CurrentView.Version, Is.EqualTo(service.CurrentView.Version));
            Assert.That(resumed.CurrentView.CurrentTurn, Is.EqualTo(service.CurrentView.CurrentTurn));
            Assert.That(resumed.CurrentView.CampaignId, Is.EqualTo(service.CurrentView.CampaignId));
            Assert.That(resumed.CurrentView.Region.LayoutHash, Is.EqualTo(service.CurrentView.Region.LayoutHash));
        }

        [Test]
        public void PlayModeStorageIsolation_IsActiveBeforeRuntimeSaveOperations()
        {
            Assert.That(KeyboardWandererTestStorage.IsActive, Is.True,
                "PlayMode SetUpFixture must isolate storage before test-owned runtime objects execute.");
            const string runIdKey = "keyboard-wanderer.server-run-id";
            string productionSavePath = System.IO.Path.Combine(
                Application.persistentDataPath, "codria-save-v4.json");

            Assert.That(KeyboardWandererPreferences.ResolveKeyForEditorTest(runIdKey),
                Is.Not.EqualTo(runIdKey));
            Assert.That(LocalRunSaveService.SavePath, Is.Not.EqualTo(productionSavePath));
            Assert.That(System.IO.Path.GetDirectoryName(LocalRunSaveService.SavePath),
                Is.EqualTo(KeyboardWandererTestStorage.ActiveDirectory));

            var sessionObject = new GameObject("PlayMode Isolated Storage");
            var session = sessionObject.AddComponent<KeyboardWandererRunSessionController>();
            try
            {
                session.RememberServerRun("play-mode-isolated-run");
                LocalRunSaveService.Save(LocalTurnService.CreateDemo(7602));
                Assert.That(session.HasContinue, Is.True);

                session.DeleteSave();
                Assert.That(session.HasContinue, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sessionObject);
            }
        }

        [UnityTest]
        public IEnumerator Controller_OffersCanonicalRecommendationsAndBoundedSecondaryObjectives()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7302);
            KeyboardWandererDemoController controller = CreateAuthoredController("Codria Low Cognitive UI Test");
            yield return null;

            Invoke(controller, "StartRun", service, false);
            var actions = (AbilityKind[])Invoke(controller, "RecommendedActions", service.CurrentView);
            string secondary = (string)Invoke(controller, "SecondaryObjectiveText", service.CurrentView);

            Assert.That(actions.Length, Is.InRange(2, 3));
            Assert.That(actions[0], Is.EqualTo(service.CurrentView.RequiredBeats[0].TriggerAbility));
            Assert.That(actions.All(action => action == AbilityKind.Move ||
                TurnRequest.IsPublicKeyboardSkill(action)), Is.True);
            Assert.That(secondary.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Length,
                Is.LessThanOrEqualTo(8));
        }

        [UnityTest]
        public IEnumerator Controller_FirstRunSelectsTheObjectiveSkillAndBuildsPlayerFacingGuidance()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7303);
            KeyboardWandererDemoController controller = CreateAuthoredController("Codria First Run Guidance Test");
            yield return null;

            Invoke(controller, "StartRun", service, false);
            AbilityKind selected = Selection(controller).Ability;
            string objective = (string)Invoke(controller, "ObjectiveHudText", service.CurrentView);
            string questHint = (string)Invoke(controller, "QuestActionHintText", service.CurrentView);
            RunPresentationModel presentationModel =
                (RunPresentationModel)Invoke(controller, "PresentationModel", service.CurrentView);
            string statusValues = KeyboardWandererHudTextComposer.StatusValues(presentationModel);
            string guidance = (string)Invoke(controller, "ActionGuidanceText", service.CurrentView);

            Assert.That(selected, Is.EqualTo(AbilityKind.Move),
                "A new run must not look as if a skill was preselected before the first dialogue choice.");
            Assert.That(objective, Does.Contain(service.CurrentView.CurrentStoryBeatObjective));
            Assert.That(questHint, Does.Contain("추천"));
            Assert.That(statusValues, Does.Contain("0 / 3"));
            Assert.That(guidance, Does.Contain("의미 턴 1회"));
        }

        [UnityTest]
        public IEnumerator MovementSelectionDetail_PrioritizesTheLatestConcreteFailureReason()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(73031);
            KeyboardWandererDemoController controller = CreateAuthoredController("Movement Feedback Priority Test");
            yield return null;
            Invoke(controller, "StartRun", service, false);
            KeyboardWandererSelectionController selection = Selection(controller);
            selection.ResetSelection(AbilityKind.Move);
            selection.Reject("그곳까지 이어지는 통행 가능한 경로가 없습니다.");

            string detail = (string)Invoke(controller, "SelectionStatusDetail", service.CurrentView);

            Assert.That(detail, Is.EqualTo("그곳까지 이어지는 통행 가능한 경로가 없습니다."));
        }

        [UnityTest]
        public IEnumerator Controller_ServerRecommendationTracksTheCurrentAuthoritativeBeat()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7304);
            KeyboardWandererDemoController controller = CreateAuthoredController("Codria Server Guidance Test");
            yield return null;

            Invoke(controller, "StartRun", service, false);
            SetField(controller, "_serverOnline", true);
            SetField(controller, "_serverRun", new GameApiClient.RunSnapshot
            {
                currentStoryBeat = new GameApiClient.StoryBeatSnapshot { requiredAbility = "DELETE" }
            });

            var actions = (AbilityKind[])Invoke(controller, "RecommendedActions", service.CurrentView);
            Assert.That(actions[0], Is.EqualTo(AbilityKind.Delete));
        }

        [UnityTest]
        public IEnumerator Controller_DeleteSelection_CommitsDamageAndRefreshesPresentation()
        {
            RunState state = LocalTurnService.CreateDemo(7304).CreateSnapshot();
            EntityState player = state.Spatial.Entities.Single(entity => entity.EntityId == state.PlayerEntityId);
            GridCoord enemyCoord = FindAvailableNeighbour(state, player.Position);
            Assert.That(EnemyArchetypeCatalog.Resolve("test.enemy", state.WorldSeed, StandardDeleteEnemyId),
                Is.EqualTo(EnemyDependencyArchetype.Standard),
                "성공 경로 fixture는 Search 선행 조건이 없는 결정적 적이어야 합니다.");
            var enemy = new EntityState(StandardDeleteEnemyId, EntityKind.Enemy, "test.enemy", "테스트 적",
                true, false, false, true, 4, state.Region.RegionId, enemyCoord);
            Assert.That(state.Spatial.Register(enemy, out string registrationError), Is.True, registrationError);
            var service = new LocalTurnService(state, new SequenceD20Source(20));
            KeyboardWandererDemoController controller = CreateAuthoredController("Codria Delete Interaction Test");
            yield return null;

            Invoke(controller, "StartRun", service, false);
            controller.UiSetGmEnabled(false);
            SetField(controller, "_lastSuggestedSkillIds", new[] { "DELETE" });
            MoveToLegacyChoiceBoundary(controller);
            controller.UiSetAbility(AbilityKind.Delete);
            Selection(controller).SelectPrimary(enemy.EntityId);
            controller.UiSubmit();
            Assert.That(service.CurrentView.CurrentTurn, Is.Zero,
                "첫 입력은 파괴적 행동 확인만 열고 턴을 소비하지 않아야 합니다.");
            controller.UiSubmit();
            // StartCoroutine may need a different number of player-loop ticks to
            // unwind the controller -> coordinator -> gateway nesting depending on
            // the surrounding PlayMode test schedule. Wait for the observable turn
            // contract instead of assuming that two rendered frames are sufficient.
            RunCoordinator coordinator = (RunCoordinator)GetField(controller, "_runCoordinator");
            KeyboardWandererTurnCoordinator turnCoordinator =
                (KeyboardWandererTurnCoordinator)GetField(controller, "_turnCoordinator");
            float deadline = Time.realtimeSinceStartup + 2f;
            while ((service.CurrentView.CurrentTurn == 0 ||
                    coordinator.State.Turn == 0 || turnCoordinator.IsPending) &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;

            EntityView committed = service.CurrentView.Entities.SingleOrDefault(entity => entity.EntityId == enemy.EntityId);
            Assert.That(service.CurrentView.CurrentTurn, Is.EqualTo(1),
                "Delete turn did not finish before the bounded PlayMode deadline. Feedback: " +
                Selection(controller).Feedback);
            Assert.That(committed == null || committed.Health < committed.MaxHealth, Is.True);
            Assert.That(coordinator.State.Turn, Is.EqualTo(1));
            Assert.That(coordinator.State.SelectedTarget, Is.Null);
        }

        [UnityTest]
        public IEnumerator Controller_RootProcessDeleteRequiresSearchBeforeConfirmAndUnlocksAfterSearch()
        {
            RunState state = LocalTurnService.CreateDemo(7304).CreateSnapshot();
            EntityState player = state.Spatial.Entities.Single(entity => entity.EntityId == state.PlayerEntityId);
            GridCoord enemyCoord = FindAvailableNeighbour(state, player.Position);
            var rootEnemy = new EntityState(RootProcessEnemyId, EntityKind.Enemy, "enemy.dragon.v1",
                "루트 프로세스", true, false, false, true, 4, state.Region.RegionId, enemyCoord);
            Assert.That(state.Spatial.Register(rootEnemy, out string registrationError), Is.True,
                registrationError);
            Assert.That(EnemyArchetypeCatalog.Resolve(rootEnemy.AssetId, state.WorldSeed, rootEnemy.EntityId),
                Is.EqualTo(EnemyDependencyArchetype.RootProcess));
            var service = new LocalTurnService(state, new SequenceD20Source(20));
            KeyboardWandererDemoController controller =
                CreateAuthoredController("Codria Root Process Availability Test");
            yield return null;

            Invoke(controller, "StartRun", service, false);
            controller.UiSetGmEnabled(false);
            MoveToLegacyChoiceBoundary(controller);
            controller.UiSetAbility(AbilityKind.Delete);
            Selection(controller).SelectPrimary(rootEnemy.EntityId);

            Assert.That((bool)Invoke(controller, "CanSubmitCurrentSelection"), Is.False,
                "Search 전 Root Process는 confirm/selectionReady가 비활성 상태여야 합니다.");
            string blockedDetail = (string)Invoke(controller, "SelectionStatusDetail", service.CurrentView);
            Assert.That(blockedDetail, Does.Contain("F 조사"));
            controller.UiSubmit();
            Assert.That(service.CurrentView.CurrentTurn, Is.Zero);
            Assert.That(Selection(controller).Feedback, Does.Contain("F로"),
                "키보드로 비활성 행동을 제출해도 필요한 Search 조건을 즉시 설명해야 합니다.");

            TurnResponse search = service.Submit(TurnRequest.UseSkill("root-search-unlock",
                service.CurrentView.Version, AbilityKind.Search, rootEnemy.EntityId));
            Assert.That(search.IsSuccess, Is.True, search.ErrorMessage);
            Assert.That(service.CurrentView.CanonicalFacts,
                Does.Contain(EnemyArchetypeCatalog.RevealedFact(rootEnemy.EntityId)));
            Selection(controller).ResetSelection(AbilityKind.Delete);
            Selection(controller).SelectPrimary(rootEnemy.EntityId);

            Assert.That((bool)Invoke(controller, "CanSubmitCurrentSelection"), Is.True,
                "권위 상태에 Search fact가 생기면 같은 대상의 Delete가 즉시 활성화돼야 합니다.");
            string unlockedDetail = (string)Invoke(controller, "SelectionStatusDetail", service.CurrentView);
            Assert.That(unlockedDetail, Does.StartWith("선택됨"));
        }

        [UnityTest]
        public IEnumerator Controller_InvalidSubmitIsRejectedWithoutException_AndNewRunClearsWalking()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7307);
            KeyboardWandererDemoController controller = CreateAuthoredController("Codria Invalid Submit Test");
            yield return null;

            SetField(controller, "_playerWalking", true);
            Invoke(controller, "StartRun", service, false);
            Assert.That(GetField(controller, "_playerWalking"), Is.False);
            long version = service.CurrentView.Version;

            Assert.DoesNotThrow(controller.UiSubmit);
            Assert.That(service.CurrentView.Version, Is.EqualTo(version));
            Assert.That(Selection(controller).Feedback, Does.Contain("대화"),
                "Blocked gameplay input must immediately explain why it was not accepted.");
            DialoguePresenter dialogue = (DialoguePresenter)GetField(controller, "_dialoguePresenter");
            Assert.That(dialogue.IsDismissed, Is.False,
                "A blocked gameplay shortcut must not dismiss the current dialogue page.");
        }

        [UnityTest]
        public IEnumerator Controller_DirectionalMove_CommitsThroughGatewayAndPersistsEveryTile()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7341);
            KeyboardWandererDemoController controller = CreateAuthoredController("Authoritative Directional Move Test");
            yield return null;
            Invoke(controller, "StartRun", service, false);
            ((TutorialPresenter)GetField(controller, "_tutorialPresenter")).Start(false);
            MoveToLegacyChoiceBoundary(controller);

            GridCoord before = service.CurrentView.PlayerPosition;
            Vector2Int direction = FindSafeDirectionalStep(service, before, out GridCoord destination);
            long versionBefore = service.CurrentView.Version;
            Invoke(controller, "HandleDirectionalMoveRequested", direction);
            yield return null;
            yield return null;

            Assert.That(service.CurrentView.PlayerPosition, Is.EqualTo(destination));
            Assert.That(service.CurrentView.Version, Is.EqualTo(versionBefore + 1));
            Assert.That(service.CurrentView.CurrentTurn, Is.Zero,
                "One exploration tile must not consume a semantic campaign turn.");
            LocalTurnService restored = LocalRunSaveService.Load();
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.CurrentView.PlayerPosition, Is.EqualTo(destination),
                "The committed tile must be present in the save immediately, not at a later checkpoint.");

            GridCoord afterFirstStep = service.CurrentView.PlayerPosition;
            Vector2Int nextDirection = FindSafeDirectionalStep(service, afterFirstStep,
                out GridCoord nextDestination);
            long committedVersion = service.CurrentView.Version;
            Invoke(controller, "HandleDirectionalMoveRequested", nextDirection);
            Assert.That(service.CurrentView.Version, Is.EqualTo(committedVersion),
                "A held direction must buffer at most one tile while the first tile is animating.");
            Assert.That(Selection(controller).Feedback, Does.Contain("이동"));

            float timeout = Time.realtimeSinceStartup + 2f;
            GameFlowStateMachine moveFlow = (GameFlowStateMachine)GetField(controller, "_flowStateMachine");
            do
            {
                Invoke(controller, "RefreshFlowPhase");
                if (!(bool)GetField(controller, "_playerWalking") &&
                    moveFlow.Phase == GameFlowPhase.AwaitingChoice &&
                    service.CurrentView.Version == committedVersion + 1)
                    break;
                yield return null;
            }
            while (Time.realtimeSinceStartup < timeout);
            Assert.That(GetField(controller, "_playerWalking"), Is.False);
            Assert.That(moveFlow.Phase, Is.EqualTo(GameFlowPhase.AwaitingChoice));
            Assert.That(service.CurrentView.PlayerPosition, Is.EqualTo(nextDestination),
                "누르고 있는 방향은 첫 칸이 끝나는 즉시 버퍼의 다음 한 칸으로 이어져야 합니다.");
            Assert.That(service.CurrentView.Version, Is.EqualTo(committedVersion + 1));
            DialoguePresenter dialogue = (DialoguePresenter)GetField(controller, "_dialoguePresenter");
            Assert.That(dialogue.IsDismissed, Is.True,
                "한 칸 이동마다 대화창을 다시 열어 연속 WASD를 막으면 안 됩니다.");
        }

        [UnityTest]
        public IEnumerator OpeningAttackTutorial_BlocksMovementAndFreeformUntilRChoice()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(73410);
            KeyboardWandererDemoController controller = CreateAuthoredController("Opening Attack Gate Test");
            yield return null;
            Invoke(controller, "StartRun", service, false);
            ((TutorialPresenter)GetField(controller, "_tutorialPresenter")).Start(false);
            SetField(controller, "_lastNarrativeChoices", new[]
            {
                new NarrativeChoiceOption("opening.attack", "R 키로 몬스터를 공격한다.", "SKILL",
                    skillId: "DELETE")
            });

            long versionBefore = service.CurrentView.Version;
            Invoke(controller, "HandleDirectionalMoveRequested", Vector2Int.right);
            Assert.That(service.CurrentView.Version, Is.EqualTo(versionBefore));
            Assert.That(Selection(controller).Feedback, Does.Contain("R 키"));

            Invoke(controller, "HandleNaturalLanguageRequested");
            Assert.That(GetField(controller, "_naturalLanguageComposeMode"), Is.False,
                "첫 전투 중 T 입력이 자유입력으로 우회되면 안 됩니다.");
            Assert.That(Selection(controller).Feedback, Does.Contain("R 키"));
        }

        [UnityTest]
        public IEnumerator Controller_ResumeRestoresAnActionableBoundaryWithoutTutorialSoftlock()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7342);
            KeyboardWandererDemoController controller = CreateAuthoredController("Resume Choice Boundary Test");
            yield return null;

            Invoke(controller, "StartRun", service, true);
            Invoke(controller, "RefreshFlowPhase");
            GameFlowStateMachine flow = (GameFlowStateMachine)GetField(controller, "_flowStateMachine");
            DialoguePresenter dialogue = (DialoguePresenter)GetField(controller, "_dialoguePresenter");
            string[] pages = (string[])Invoke(controller, "BuildDialoguePages", "fallback");

            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.AwaitingChoice));
            Assert.That(flow.CanIssueAbility(AbilityKind.Move), Is.True);
            Assert.That(dialogue.Page, Is.EqualTo(pages.Length - 1));
            Assert.That(((TutorialPresenter)GetField(controller, "_tutorialPresenter")).IsActive, Is.False);
        }

        [UnityTest]
        public IEnumerator Controller_PauseBlocksMovementAmbientAndReturnsFromSettingsStillPaused()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7343);
            KeyboardWandererDemoController controller = CreateAuthoredController("Pause Simulation Boundary Test");
            yield return null;
            Invoke(controller, "StartRun", service, true);
            SetField(controller, "_nextAmbientWanderAt", -10f);
            long version = service.CurrentView.Version;

            Invoke(controller, "HandlePauseRequested");
            Assert.That(GetField(controller, "_showPause"), Is.True);
            Invoke(controller, "Update");
            Assert.That(service.CurrentView.Version, Is.EqualTo(version));
            Assert.That((float)GetField(controller, "_nextAmbientWanderAt"), Is.EqualTo(-10f));

            Invoke(controller, "HandleDirectionalMoveRequested", Vector2Int.right);
            Assert.That(service.CurrentView.Version, Is.EqualTo(version));
            Assert.That(Selection(controller).Feedback, Does.Contain("일시정지"));

            controller.UiOpenSettingsFromPause();
            Assert.That(GetField(controller, "_showPause"), Is.True,
                "Settings opened from pause are part of the same paused simulation interval.");
            Assert.That(GetField(controller, "_screenMode").ToString(), Is.EqualTo("Settings"));
            Invoke(controller, "HandlePauseRequested");
            Assert.That(GetField(controller, "_showPause"), Is.True,
                "Escape from pause settings should return to the pause menu, not resume gameplay.");
            Assert.That(GetField(controller, "_screenMode").ToString(), Is.EqualTo("Playing"));
            controller.UiResume();
            Assert.That(GetField(controller, "_showPause"), Is.False);
        }

        [TestCase("NOT_FOUND", null, true)]
        [TestCase("RUN_NOT_FOUND", "", true)]
        [TestCase(null, "Run was not found.", true)]
        [TestCase("ENTITY_NOT_FOUND", "Entity was not found.", false)]
        [TestCase("NETWORK_ERROR", "Run was not found.", false)]
        public void MissingRunClassifier_OnlyMatchesAuthoritativeRunLoss(
            string code, string message, bool expected)
        {
            Assert.That(InvokeStatic(typeof(KeyboardWandererDemoController), "IsMissingServerRun", code, message),
                Is.EqualTo(expected));
        }

        [TestCase("saved-run", false, false, true)]
        [TestCase("saved-run", true, false, false)]
        [TestCase("", false, false, false)]
        [TestCase("saved-run", false, true, true)]
        public void ContinuePolicy_DoesNotCreateDivergentLocalRunDuringTransientOutage(
            string runId, bool missing, bool hasCheckpoint, bool shouldWait)
        {
            Assert.That(InvokeStatic(typeof(KeyboardWandererRunSessionController),
                    "ShouldWaitForServerRetry", runId, missing, hasCheckpoint),
                Is.EqualTo(shouldWait));
        }

        [UnityTest]
        public IEnumerator Continue_TransientNetworkFailurePreservesAuthoritativePointerDespiteLocalCheckpoint()
        {
            LocalTurnService localCheckpoint = LocalTurnService.CreateDemo(7343);
            LocalRunSaveService.Save(localCheckpoint);
            _controllerObject = new GameObject("Continue Retry Boundary Test");
            KeyboardWandererRunSessionController session =
                _controllerObject.AddComponent<KeyboardWandererRunSessionController>();
            session.RememberServerRun("authoritative-run-to-retry");
            SetField(session, "_api", new GameApiClient("http://127.0.0.1:1"));
            KeyboardWandererRunSessionResult resumed = null;

            yield return session.Continue(value => resumed = value);

            Assert.That(resumed, Is.Null,
                "전송 실패를 로컬 체크포인트 성공으로 위장하면 권위 타임라인이 분기됩니다.");
            Assert.That(session.HasContinue, Is.True);
            Assert.That(KeyboardWandererPreferences.GetString("keyboard-wanderer.server-run-id"),
                Is.EqualTo("authoritative-run-to-retry"));
            Assert.That(session.Status, Does.Contain("다시 시도"));
            session.DeleteSave();
        }

        [UnityTest]
        public IEnumerator MissingServerRun_AtomicallySwitchesToSavedLocalCheckpoint()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7344);
            KeyboardWandererDemoController controller = CreateAuthoredController("Missing Run Recovery Test");
            yield return null;
            Invoke(controller, "StartRun", service, true);
            long checkpointVersion = service.CurrentView.Version;
            KeyboardWandererRunSessionController session =
                controller.GetComponent<KeyboardWandererRunSessionController>();
            session.RememberServerRun("gone-run");
            var serverRun = new GameApiClient.RunSnapshot
            {
                id = "gone-run", version = 9, status = "active"
            };
            SetField(controller, "_serverOnline", true);
            SetField(controller, "_serverRun", serverRun);
            SetField(controller, "_serverCampaign", new GameApiClient.CampaignSnapshot());
            SetField(controller, "_turnGateway", new ServerTurnGateway(new GameApiClient(), () => "gone-run"));
            SetField(controller, "_serverPending", true);
            SetField(controller, "_gmPending", true);
            SetField(controller, "_choiceSubmissionPending", true);
            SetField(controller, "_preparedD20", 17);
            SetField(controller, "_lastChoiceSetId", "stale-set");
            SetField(controller, "_lastNarrativeChoices", new[]
            {
                new NarrativeChoiceOption("stale", "이전 서버 선택", "DIALOGUE")
            });
            SetField(controller, "_lastD20", 20);
            DestructiveActionConfirmation confirmation =
                (DestructiveActionConfirmation)GetField(controller, "_actionConfirmation");
            confirmation.RequiresConfirmation(AbilityKind.Delete, Guid.NewGuid(), Time.unscaledTime);

            bool handled = (bool)Invoke(controller, "RecoverMissingServerRun", "NOT_FOUND", "Run was not found.");

            Assert.That(handled, Is.True);
            Assert.That(GetField(controller, "_serverOnline"), Is.False);
            Assert.That(GetField(controller, "_serverRun"), Is.Null);
            Assert.That(GetField(controller, "_serverCampaign"), Is.Null);
            Assert.That(GetField(controller, "_serverPending"), Is.False);
            Assert.That(GetField(controller, "_gmPending"), Is.False);
            Assert.That(GetField(controller, "_choiceSubmissionPending"), Is.False);
            Assert.That(GetField(controller, "_preparedD20"), Is.EqualTo(0));
            Assert.That(GetField(controller, "_lastD20"), Is.EqualTo(0));
            Assert.That(GetField(controller, "_turnGateway"), Is.TypeOf<LocalTurnGateway>());
            Assert.That(GetField(controller, "_runPresentationAdapter"), Is.TypeOf<LocalRunPresentationAdapter>());
            Assert.That(GetField(controller, "_lastChoiceSetId"), Is.EqualTo(string.Empty));
            Assert.That((NarrativeChoiceOption[])GetField(controller, "_lastNarrativeChoices"), Is.Empty);
            Assert.That(Selection(controller).Feedback, Does.Contain("로컬 체크포인트"));
            Assert.That(confirmation.IsArmed(Time.unscaledTime), Is.False);
            LocalTurnService restored = LocalRunSaveService.Load();
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.CurrentView.WorldSeed, Is.EqualTo(service.CurrentView.WorldSeed));
            Assert.That(restored.CurrentView.Version, Is.EqualTo(checkpointVersion));
            LocalRunSaveService.Delete();
            Assert.That(session.HasContinue, Is.False,
                "The dead server pointer must be removed rather than retried forever.");
        }

        [Test]
        public void NewSessionCreation_ReusesSeedAndBothIdempotencyKeysUntilAttemptEnds()
        {
            const string counterKey = "keyboard-wanderer.run-counter";
            bool hadCounter = KeyboardWandererPreferences.HasKey(counterKey);
            int oldCounter = KeyboardWandererPreferences.GetInt(counterKey, 0);
            try
            {
                InvokeStatic(typeof(KeyboardWandererRunSessionController), "ClearPendingNewOperation");
                long firstSeed = (long)InvokeStatic(typeof(KeyboardWandererRunSessionController), "ReserveNewOperation");
                string campaignKey = KeyboardWandererPreferences.GetString("keyboard-wanderer.pending-campaign-key");
                string runKey = KeyboardWandererPreferences.GetString("keyboard-wanderer.pending-run-key");

                long retriedSeed = (long)InvokeStatic(typeof(KeyboardWandererRunSessionController), "ReserveNewOperation");
                Assert.That(retriedSeed, Is.EqualTo(firstSeed));
                Assert.That(KeyboardWandererPreferences.GetString("keyboard-wanderer.pending-campaign-key"), Is.EqualTo(campaignKey));
                Assert.That(KeyboardWandererPreferences.GetString("keyboard-wanderer.pending-run-key"), Is.EqualTo(runKey));
                Assert.That(campaignKey.Length, Is.InRange(8, 128));
                Assert.That(runKey.Length, Is.InRange(8, 128));

                InvokeStatic(typeof(KeyboardWandererRunSessionController), "ClearPendingNewOperation");
                long nextSeed = (long)InvokeStatic(typeof(KeyboardWandererRunSessionController), "ReserveNewOperation");
                Assert.That(nextSeed, Is.EqualTo(firstSeed + 1),
                    "새 attempt가 명시적으로 시작될 때만 counter가 한 번 증가해야 합니다.");
                Assert.That(KeyboardWandererPreferences.GetString("keyboard-wanderer.pending-campaign-key"), Is.Not.EqualTo(campaignKey));
                Assert.That(KeyboardWandererPreferences.GetString("keyboard-wanderer.pending-run-key"), Is.Not.EqualTo(runKey));
            }
            finally
            {
                InvokeStatic(typeof(KeyboardWandererRunSessionController), "ClearPendingNewOperation");
                if (hadCounter) KeyboardWandererPreferences.SetInt(counterKey, oldCounter);
                else KeyboardWandererPreferences.DeleteKey(counterKey);
                KeyboardWandererPreferences.Save();
            }
        }

        [Test]
        public void GameApiClient_AppliesStandardIdempotencyHeaderAndRejectsInvalidLengths()
        {
            using var request = new UnityWebRequest("http://127.0.0.1/test", "POST");
            const string key = "kw-campaign-0123456789abcdef";
            InvokeStatic(typeof(GameApiClient), "ApplyIdempotencyKey", request, key);

            Assert.That(request.GetRequestHeader("Idempotency-Key"), Is.EqualTo(key));
            Assert.That(InvokeStatic(typeof(GameApiClient), "IsValidOptionalIdempotencyKey", key), Is.True);
            Assert.That(InvokeStatic(typeof(GameApiClient), "IsValidOptionalIdempotencyKey", "short"), Is.False);
            Assert.That(InvokeStatic(typeof(GameApiClient), "IsValidOptionalIdempotencyKey", new string('x', 129)),
                Is.False);
        }

        [UnityTest]
        public IEnumerator FreeformRetry_ReusesTheOriginalRequestIdentityAndExpectedVersion()
        {
            KeyboardWandererDemoController controller = CreateAuthoredController("Freeform Retry Identity Test");
            yield return null;

            Invoke(controller, "ReservePlayerMessageSubmission", "문을 조사한다", 41L);
            string firstKey = (string)GetField(controller, "_pendingPlayerMessageIdempotencyKey");
            Invoke(controller, "ReservePlayerMessageSubmission", "문을 조사한다", 99L);

            Assert.That(GetField(controller, "_pendingPlayerMessageIdempotencyKey"), Is.EqualTo(firstKey));
            Assert.That(GetField(controller, "_pendingPlayerMessageExpectedVersion"), Is.EqualTo(41L),
                "모호한 전송 실패 재시도는 새 버전으로 다른 요청을 만들면 안 됩니다.");

            Invoke(controller, "ClearPlayerMessageSubmission");
            Invoke(controller, "ReservePlayerMessageSubmission", "문을 조사한다", 99L);
            Assert.That(GetField(controller, "_pendingPlayerMessageIdempotencyKey"), Is.Not.EqualTo(firstKey));
            Assert.That(GetField(controller, "_pendingPlayerMessageExpectedVersion"), Is.EqualTo(99L));
        }

        [UnityTest]
        public IEnumerator OptionalLocalNarration_DoesNotBlockTheNextCommittedChoice()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7345);
            KeyboardWandererDemoController controller = CreateAuthoredController("Nonblocking Optional GM Test");
            yield return null;
            Invoke(controller, "StartRun", service, false);
            MoveToLegacyChoiceBoundary(controller);
            SetField(controller, "_gmPending", true);

            Invoke(controller, "RefreshFlowPhase");
            GameFlowStateMachine flow = (GameFlowStateMachine)GetField(controller, "_flowStateMachine");
            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.AwaitingChoice));
            Assert.That(flow.CanIssueAbility(AbilityKind.Move), Is.True,
                "선택적 로컬 GM 문장을 기다리는 동안 다음 권위 행동을 막으면 안 됩니다.");
        }

        [UnityTest]
        public IEnumerator OnlineIdle_DoesNotStartCosmeticServerPolling()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7346);
            KeyboardWandererDemoController controller = CreateAuthoredController("No Online Ambient Polling Test");
            yield return null;
            Invoke(controller, "StartRun", service, false);
            SetField(controller, "_serverOnline", true);
            SetField(controller, "_serverRun", new GameApiClient.RunSnapshot { id = Guid.NewGuid().ToString() });
            SetField(controller, "_nextAmbientWanderAt", -100f);

            Invoke(controller, "Update");

            Assert.That(GetField(controller, "_nextAmbientWanderAt"), Is.EqualTo(-100f),
                "온라인 유휴 상태는 장식 이동 POST 주기를 예약하지 않아야 합니다.");
            Assert.That(GetField(controller, "_serverPending"), Is.False);
        }

        [Test]
        public void NewTurnContent_ReopensDismissedDialogue()
        {
            KeyboardWandererDemoController controller = CreateAuthoredController("Codria Dialogue Reopen Test");
            DialoguePresenter dialogue = (DialoguePresenter)GetField(controller, "_dialoguePresenter");
            dialogue.Dismiss();

            Invoke(controller, "ReopenDialogueForNewTurnContent");

            Assert.That(dialogue.IsDismissed, Is.False,
                "새 턴 결과나 AI 문장이 도착하면 이전에 닫은 대화창도 다시 열려야 합니다.");
        }

        [UnityTest]
        public IEnumerator CombatImpact_OwnsTheFieldUntilPresentationCompletes_ThenReleasesInput()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7347);
            KeyboardWandererDemoController controller = CreateAuthoredController("Combat Impact Field Ownership Test");
            yield return null;
            Invoke(controller, "StartRun", service, false);
            MoveToLegacyChoiceBoundary(controller);
            DialoguePresenter dialogue = (DialoguePresenter)GetField(controller, "_dialoguePresenter");
            int pageBefore = dialogue.Page;

            // The fixture intentionally has no elemental frames. The coordinator
            // must still claim one rendered frame so the result UI cannot flash over
            // the authoritative attack/reaction sequence queued in that submit frame.
            Invoke(controller, "BeginTurnImpactPresentation", "TEST_MISSING_EFFECT");
            Assert.That(GetField(controller, "_turnImpactPresentationPlaying"), Is.True);
            controller.UiAdvanceDialogue();
            Assert.That(dialogue.Page, Is.EqualTo(pageBefore),
                "dialogue input must not advance while the attack owns the field");
            Invoke(controller, "RefreshFlowPhase");
            GameFlowStateMachine flow = (GameFlowStateMachine)GetField(controller, "_flowStateMachine");
            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.PresentingWorldAction));

            yield return null;
            yield return null;
            Assert.That(GetField(controller, "_turnImpactPresentationPlaying"), Is.False);
            Invoke(controller, "RefreshFlowPhase");
            Assert.That(flow.Phase, Is.Not.EqualTo(GameFlowPhase.PresentingWorldAction),
                "input ownership must be released as soon as the bounded impact presentation ends");
        }

        [UnityTest]
        public IEnumerator CombatEffect_UsesNonInteractiveOverlayAboveHudAndBelowDice()
        {
            KeyboardWandererDemoController controller = CreateAuthoredController("Combat Effect Overlay Test");
            yield return null;
            KeyboardWandererCombatEffectOverlay overlay =
                controller.GetComponent<KeyboardWandererCombatEffectOverlay>();
            Assert.That(overlay, Is.Not.Null);

            var texture = new Texture2D(8, 12);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, 8f, 12f), new Vector2(0.5f, 0.5f), 12f);
            try
            {
                controller.StartCoroutine(overlay.PlayAndWait(new[] { sprite }, Vector3.zero, 1f));
                yield return null;

                Assert.That(overlay.IsVisible, Is.True);
                Canvas effectCanvas = controller.GetComponentsInChildren<Canvas>(true)
                    .Single(value => value.gameObject.name == "Combat Effect UI Overlay");
                Assert.That(effectCanvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
                Assert.That(effectCanvas.sortingOrder,
                    Is.EqualTo(KeyboardWandererCombatEffectOverlay.OverlaySortingOrder));
                Assert.That(effectCanvas.sortingOrder, Is.GreaterThan(20),
                    "공격 연출은 authored HUD보다 위에 있어야 합니다.");
                Image effectImage = effectCanvas.GetComponentInChildren<Image>(true);
                Assert.That(effectImage.raycastTarget, Is.False,
                    "시각 피드백 레이어가 다음 입력을 가로채면 안 됩니다.");
            }
            finally
            {
                overlay.Hide();
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void DialoguePages_ReusesPagesAndSignatureUntilVisibleContentChanges()
        {
            KeyboardWandererDemoController controller = CreateAuthoredController("Codria Dialogue Cache Test");
            SetField(controller, "_lastStorySequence", new[]
            {
                new StorySequencePage("MONOLOGUE", "넙죽이", "같은 장면을 유지한다.")
            });
            SetField(controller, "_lastNextInterventionReason", "다음 행동을 선택한다.");

            string[] first = (string[])Invoke(controller, "BuildDialoguePages", "사용되지 않는 서술 A");
            string firstSignature = (string)GetField(controller, "_cachedDialogueSignature");
            int firstRevision = (int)GetField(controller, "_dialoguePageCacheRevision");
            string[] repeated = (string[])Invoke(controller, "BuildDialoguePages", "사용되지 않는 서술 B");

            Assert.That(repeated, Is.SameAs(first),
                "story sequence가 화면 내용을 소유할 때 무관한 narrative 변화는 페이지 배열을 다시 만들면 안 됩니다.");
            Assert.That(GetField(controller, "_cachedDialogueSignature"), Is.EqualTo(firstSignature));
            Assert.That(GetField(controller, "_dialoguePageCacheRevision"), Is.EqualTo(firstRevision));

            SetField(controller, "_lastStorySequence", new[]
            {
                new StorySequencePage("MONOLOGUE", "다른 화자", "같은 장면을 유지한다.")
            });
            string[] equivalent = (string[])Invoke(controller, "BuildDialoguePages", "사용되지 않는 서술 C");
            Assert.That(equivalent, Is.SameAs(first),
                "화자 메타데이터만 바뀌고 페이지 텍스트가 같으면 페이지/서명 캐시는 유지해야 합니다.");

            SetField(controller, "_lastStorySequence", new[]
            {
                new StorySequencePage("MONOLOGUE", "넙죽이", "실제로 달라진 다음 장면이다.")
            });
            string[] changed = (string[])Invoke(controller, "BuildDialoguePages", "사용되지 않는 서술 D");
            Assert.That(changed, Is.Not.SameAs(first));
            Assert.That(GetField(controller, "_dialoguePageCacheRevision"), Is.EqualTo(firstRevision + 1));
            Assert.That(GetField(controller, "_cachedDialogueSignature"), Is.Not.EqualTo(firstSignature));
        }

        [Test]
        public void DialoguePages_DetectsInPlaceSearchDialogueMutation()
        {
            KeyboardWandererDemoController controller = CreateAuthoredController("Codria Dialogue Mutation Test");
            Selection(controller).ResetSelection(AbilityKind.Search);
            SetField(controller, "_lastStorySequence", Array.Empty<StorySequencePage>());
            var dialogueLines = new[] { "처음 조사 결과" };
            SetField(controller, "_lastDialogue", dialogueLines);

            string[] first = (string[])Invoke(controller, "BuildDialoguePages", "fallback");
            int revision = (int)GetField(controller, "_dialoguePageCacheRevision");
            dialogueLines[0] = "변경된 조사 결과";
            string[] changed = (string[])Invoke(controller, "BuildDialoguePages", "fallback");

            Assert.That(changed, Is.Not.SameAs(first));
            Assert.That(changed[0], Is.EqualTo("변경된 조사 결과"));
            Assert.That(GetField(controller, "_dialoguePageCacheRevision"), Is.EqualTo(revision + 1));
        }

        [UnityTest]
        public IEnumerator InterventionPage_RemainsVisibleAndOpensContextualInput()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7311);
            KeyboardWandererDemoController controller = CreateAuthoredController("Codria Intervention Page Test");
            yield return null;
            Invoke(controller, "StartRun", service, false);
            ((TutorialPresenter)GetField(controller, "_tutorialPresenter")).Start(false);
            SetField(controller, "_lastStorySequence", new[]
            {
                new StorySequencePage("MONOLOGUE", "넙죽이", "이제 다음 선택을 해야 해.")
            });
            SetField(controller, "_lastNextInterventionReason", "주변의 흐름이 잠시 멈췄다.");
            SetField(controller, "_lastSuggestedSkillIds", new[] { "SEARCH", "CONNECT" });

            string[] pages = (string[])Invoke(controller, "BuildDialoguePages", "fallback");
            DialoguePresenter dialogue = (DialoguePresenter)GetField(controller, "_dialoguePresenter");
            dialogue.Restore(pages.Length - 1, false);
            Invoke(controller, "RefreshFlowPhase");

            GameFlowStateMachine flow = (GameFlowStateMachine)GetField(controller, "_flowStateMachine");
            Assert.That(pages[^1], Does.Contain("주변의 흐름이 잠시 멈췄다"));
            NarrativeChoiceOption[] choices = (NarrativeChoiceOption[])Invoke(controller, "CurrentNarrativeChoices");
            Assert.That(choices, Has.Some.Matches<NarrativeChoiceOption>(value => value.SkillId == "SEARCH"));
            Assert.That(choices, Has.Some.Matches<NarrativeChoiceOption>(value => value.SkillId == "CONNECT"));
            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.AwaitingChoice));
            Assert.That(flow.CanIssueAbility(AbilityKind.Search), Is.True);

            controller.UiAdvanceDialogue();
            Assert.That(dialogue.IsDismissed, Is.False,
                "개입 페이지는 닫히지 않고 사용자가 문맥 선택지를 고를 때까지 유지되어야 합니다.");
            Assert.That(GetField(controller, "_naturalLanguageComposeMode"), Is.EqualTo(true),
                "화면의 직접 입력 버튼은 T 단축키와 동일한 자유입력 상태로 전환해야 합니다.");
        }

        [UnityTest]
        public IEnumerator FallbackIntervention_PoiNavigationDismissesVisibleChoiceSurface()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7312);
            KeyboardWandererDemoController controller = CreateAuthoredController("Fallback Choice POI Test");
            yield return null;
            Invoke(controller, "StartRun", service, false);
            SetField(controller, "_lastStorySequence", new[]
            {
                new StorySequencePage("MONOLOGUE", "넙죽이", "다음 이동을 정하자.")
            });
            SetField(controller, "_lastNextInterventionReason", "복구 선택지가 열린 상태다.");
            SetField(controller, "_lastChoiceSetId", string.Empty);
            SetField(controller, "_lastNarrativeChoices", Array.Empty<NarrativeChoiceOption>());
            string[] pages = (string[])Invoke(controller, "BuildDialoguePages", "fallback");
            DialoguePresenter dialogue = (DialoguePresenter)GetField(controller, "_dialoguePresenter");
            dialogue.Restore(pages.Length - 1, false);
            Invoke(controller, "RefreshFlowPhase");

            GameFlowStateMachine flow = (GameFlowStateMachine)GetField(controller, "_flowStateMachine");
            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.AwaitingChoice),
                "복구 선택지는 sealed 선택지가 아니므로 일반 행동 단계여야 합니다.");

            Selection(controller).ChangeAbility(AbilityKind.Delete);
            Assert.That(Selection(controller).Ability, Is.EqualTo(AbilityKind.Delete));

            Invoke(controller, "HandlePoiCycleRequested", 1);

            Assert.That(dialogue.IsDismissed, Is.True,
                "화면에 보이는 복구 선택지는 월드 목적지 입력이 소유권을 가져갈 때 닫혀야 합니다.");
            Assert.That(Selection(controller).SelectedCoord.HasValue, Is.True);
            Assert.That(Selection(controller).Ability, Is.EqualTo(AbilityKind.Move));
        }

        [UnityTest]
        public IEnumerator SealedIntervention_MapNavigationClaimsInputBeforeTravelConfirmation()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7317);
            KeyboardWandererDemoController controller = CreateAuthoredController("Sealed Choice Map Navigation Test");
            yield return null;
            Invoke(controller, "StartRun", service, false);
            SetField(controller, "_lastStorySequence", new[]
            {
                new StorySequencePage("MONOLOGUE", "넙죽이", "선택하거나 다른 지역으로 이동하자.")
            });
            SetField(controller, "_lastNextInterventionReason", "대화와 이동 중 하나를 고른다.");
            SetField(controller, "_lastChoiceSetId", "sealed-map-navigation");
            SetField(controller, "_lastNarrativeChoices", new[]
            {
                new NarrativeChoiceOption("listen", "조금 더 듣는다.", "ATTITUDE"),
                new NarrativeChoiceOption("ask", "의도를 묻는다.", "DIALOGUE")
            });
            DialoguePresenter dialogue = (DialoguePresenter)GetField(controller, "_dialoguePresenter");
            string[] pages = (string[])Invoke(controller, "BuildDialoguePages", "fallback");
            dialogue.Restore(pages.Length - 1, false);
            Invoke(controller, "RefreshFlowPhase");

            GameFlowStateMachine flow = (GameFlowStateMachine)GetField(controller, "_flowStateMachine");
            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.AwaitingNarrativeChoice));

            Invoke(controller, "ClaimWorldNavigationInput");

            Assert.That(dialogue.IsDismissed, Is.True,
                "맵 클릭이 목적지를 고르기 전에 선택지 모달의 Enter 소유권을 해제해야 합니다.");
            Invoke(controller, "RefreshFlowPhase");
            Assert.That(flow.CanIssueAbility(AbilityKind.Move), Is.True);
        }

        [UnityTest]
        public IEnumerator DismissedNarrativeSuggestions_DoNotBecomeAWorldAbilityAllowlist()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7314);
            KeyboardWandererDemoController controller = CreateAuthoredController("Dismissed Suggestion Ownership Test");
            yield return null;
            Invoke(controller, "StartRun", service, false);
            SetField(controller, "_serverOnline", true);
            SetField(controller, "_serverRun", new GameApiClient.RunSnapshot { status = "active" });
            SetField(controller, "_lastStorySequence", new[]
            {
                new StorySequencePage("MONOLOGUE", "넙죽이", "조사할지 이동할지 선택한다.")
            });
            SetField(controller, "_lastChoiceSetId", "sealed-suggestion-set");
            SetField(controller, "_lastNarrativeChoices", new[]
            {
                new NarrativeChoiceOption("search", "흔적을 조사한다.", "SKILL", skillId: "SEARCH"),
                new NarrativeChoiceOption("leave", "다른 곳으로 이동한다.", "TRAVEL")
            });
            SetField(controller, "_lastSuggestedSkillIds", new[] { "SEARCH" });
            DialoguePresenter dialogue = (DialoguePresenter)GetField(controller, "_dialoguePresenter");
            string[] pages = (string[])Invoke(controller, "BuildDialoguePages", "fallback");
            dialogue.Restore(pages.Length - 1, false);

            Assert.That(Invoke(controller, "IsOfferedAbility", AbilityKind.Delete), Is.EqualTo(false));
            dialogue.Dismiss();

            Assert.That(Invoke(controller, "IsOfferedAbility", AbilityKind.Delete), Is.EqualTo(true),
                "숨긴 선택지의 추천 스킬이 이후 월드 탐험을 잠그면 안 됩니다.");
            Assert.That(Invoke(controller, "TrySubmitSealedSkillChoice", AbilityKind.Search), Is.EqualTo(false),
                "숨긴 서버 선택지는 같은 단축키의 월드 행동을 가로채면 안 됩니다.");
        }

        [UnityTest]
        public IEnumerator SealedSkillChoice_SynchronizesPendingHudAbilityWithoutSubmittingTwice()
        {
            KeyboardWandererDemoController controller = CreateAuthoredController("Sealed Choice Pending HUD Test");
            yield return null;
            KeyboardWandererSelectionController selection = Selection(controller);
            selection.ResetSelection(AbilityKind.Move);
            var choice = new NarrativeChoiceOption("search", "흔적을 조사한다.", "SKILL", skillId: "SEARCH");

            Invoke(controller, "SynchronizeSelectionWithNarrativeChoice", choice);

            Assert.That(selection.Ability, Is.EqualTo(AbilityKind.Search),
                "처리 중 HUD는 서버에 제출한 선택지의 기술을 즉시 표시해야 합니다.");
            Assert.That(selection.SelectedTarget.HasValue, Is.False,
                "서버가 자동 선택할 대상을 클라이언트가 임의로 표시하면 안 됩니다.");
        }

        [UnityTest]
        public IEnumerator DismissedPendingServerChoice_ReopensInsteadOfSendingRejectedWorldSkill()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7315);
            KeyboardWandererDemoController controller = CreateAuthoredController("Pending Choice Reopen Test");
            yield return null;
            Invoke(controller, "StartRun", service, false);
            SetField(controller, "_serverOnline", true);
            SetField(controller, "_serverRun", new GameApiClient.RunSnapshot
            {
                status = "active",
                pendingChoiceSet = new GameApiClient.NextInterventionSnapshot
                {
                    choiceSetId = "pending-root-choice",
                    choices = new[]
                    {
                        new GameApiClient.NarrativeChoiceSnapshot { choiceId = "enter", text = "커널로 들어간다." },
                        new GameApiClient.NarrativeChoiceSnapshot { choiceId = "ask", text = "동료에게 묻는다." }
                    }
                }
            });
            SetField(controller, "_lastStorySequence", new[]
            {
                new StorySequencePage("MONOLOGUE", "넙죽이", "루트 커널이 눈앞에서 열린다.")
            });
            SetField(controller, "_lastNextInterventionReason", "어떻게 진입할지 결정한다.");
            SetField(controller, "_lastChoiceSetId", "pending-root-choice");
            SetField(controller, "_lastNarrativeChoices", new[]
            {
                new NarrativeChoiceOption("enter", "커널로 들어간다.", "ATTITUDE"),
                new NarrativeChoiceOption("ask", "동료에게 묻는다.", "DIALOGUE")
            });
            DialoguePresenter dialogue = (DialoguePresenter)GetField(controller, "_dialoguePresenter");
            string[] pages = (string[])Invoke(controller, "BuildDialoguePages", "fallback");
            dialogue.Restore(pages.Length - 1, true);
            GameplayTelemetry telemetry = (GameplayTelemetry)GetField(controller, "_gameplayTelemetry");
            int submitsBefore = telemetry.SubmitAttempts;

            Assert.That(Invoke(controller, "TrySubmitSealedSkillChoice", AbilityKind.Connect), Is.EqualTo(true));
            Assert.That(dialogue.IsDismissed, Is.False,
                "숨겨 둔 권위 선택지가 남아 있으면 실패할 스킬을 전송하지 말고 선택 화면을 복구해야 합니다.");
            Assert.That(dialogue.Page, Is.EqualTo(pages.Length - 1));
            Assert.That((string)GetField(controller, "_choiceStatusMessage"), Does.Contain("이야기 선택"));
            Assert.That(telemetry.SubmitAttempts, Is.EqualTo(submitsBefore),
                "CHOICE_REQUIRED가 확실한 요청은 네트워크 제출 카운터조차 증가시키면 안 됩니다.");
        }

        [UnityTest]
        public IEnumerator SealedSkillShortcut_DoesNotSkipEarlierStoryPage()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7316);
            KeyboardWandererDemoController controller = CreateAuthoredController("Story Page Shortcut Boundary Test");
            yield return null;
            Invoke(controller, "StartRun", service, false);
            SetField(controller, "_serverOnline", true);
            SetField(controller, "_serverRun", new GameApiClient.RunSnapshot
            {
                status = "active",
                pendingChoiceSet = new GameApiClient.NextInterventionSnapshot { choiceSetId = "story-boundary" }
            });
            SetField(controller, "_lastStorySequence", new[]
            {
                new StorySequencePage("MONOLOGUE", "넙죽이", "첫 번째 중요한 설명이다."),
                new StorySequencePage("MONOLOGUE", "캐시", "두 번째 중요한 설명이다.")
            });
            SetField(controller, "_lastNextInterventionReason", "설명을 들은 뒤 결정한다.");
            SetField(controller, "_lastChoiceSetId", "story-boundary");
            SetField(controller, "_lastNarrativeChoices", new[]
            {
                new NarrativeChoiceOption("search", "흔적을 조사한다.", "SKILL", skillId: "SEARCH"),
                new NarrativeChoiceOption("wait", "조금 더 듣는다.", "ATTITUDE")
            });
            DialoguePresenter dialogue = (DialoguePresenter)GetField(controller, "_dialoguePresenter");
            string[] pages = (string[])Invoke(controller, "BuildDialoguePages", "fallback");
            dialogue.Restore(0, false);

            Assert.That(Invoke(controller, "TrySubmitSealedSkillChoice", AbilityKind.Search), Is.EqualTo(false));
            Assert.That(dialogue.Page, Is.Zero,
                "스킬 단축키가 보이는 최종 선택 페이지 전에 서사 페이지를 건너뛰면 안 됩니다.");
            Assert.That(dialogue.IsDismissed, Is.False);
            Assert.That(pages.Length, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void NewTurnContent_WhileWalking_DefersDialogueUntilMovementCompletes()
        {
            KeyboardWandererDemoController controller = CreateAuthoredController("Codria Dialogue Walking Test");
            DialoguePresenter dialogue = (DialoguePresenter)GetField(controller, "_dialoguePresenter");
            dialogue.Dismiss();
            SetField(controller, "_playerWalking", true);

            Invoke(controller, "ReopenDialogueForNewTurnContent");

            Assert.That(dialogue.IsDismissed, Is.True);
            Assert.That(GetField(controller, "_reopenDialogueAfterWalk"), Is.True,
                "이동 중 도착한 메시지는 버리지 않고 이동 완료 뒤 표시하도록 예약해야 합니다.");
        }

        [UnityTest]
        public IEnumerator OneTileMovement_DoesNotReopenAChangedTravelNarrativeAfterWalk()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7313);
            KeyboardWandererDemoController controller = CreateAuthoredController("One Tile Dialogue Suppression Test");
            yield return null;
            Invoke(controller, "StartRun", service, false);
            ((TutorialPresenter)GetField(controller, "_tutorialPresenter")).Start(false);
            DialoguePresenter dialogue = (DialoguePresenter)GetField(controller, "_dialoguePresenter");
            dialogue.Synchronize("이전 서명", false);
            SetField(controller, "_lastNarrative", "한 칸 이동 뒤 새로 도착한 이동 서술");
            SetField(controller, "_lastStorySequence", Array.Empty<StorySequencePage>());
            SetField(controller, "_lastNextInterventionReason", "이동을 계속하거나 주변을 조사하세요.");

            Invoke(controller, "BeginWalkingPresentation", false);
            Invoke(controller, "UpdateAuthoredUi");
            Invoke(controller, "CompletePlayerPathAnimation");
            Invoke(controller, "RefreshFlowPhase");

            Assert.That(dialogue.IsDismissed, Is.True,
                "새 이동 서명이 동기화돼도 1칸 이동은 대화창을 다시 열면 안 됩니다.");
            Assert.That(GetField(controller, "_reopenDialogueAfterWalk"), Is.False);
            Assert.That(GetField(controller, "_suppressDialogueReopenAfterWalk"), Is.False,
                "이동 완료 후 억제 플래그는 다음 장거리 서사를 위해 반드시 해제해야 합니다.");
            GameFlowStateMachine flow = (GameFlowStateMachine)GetField(controller, "_flowStateMachine");
            Assert.That(flow.Phase, Is.EqualTo(GameFlowPhase.AwaitingChoice),
                "숨긴 이동 서술의 페이지 수가 바뀌어도 월드 화면은 즉시 다음 입력을 받아야 합니다.");
            Assert.That(flow.CanIssueAbility(AbilityKind.Move), Is.True);
        }

        [Test]
        public void RunDto_ParsesCodriaCampaignAndSharedEndingState()
        {
            const string json = "{\"campaignTitle\":\"NUPJUK : The Last Commit\"," +
                                "\"premise\":\"코드리아 붕괴 복구\",\"safeTravelCount\":4," +
                                "\"currentBeat\":\"관리자 통제 시스템 내부 원인 확인\"," +
                                "\"endingCode\":\"ENDING_PRESERVE_THE_SCARS\"}";
            GameApiClient.RunSnapshot run = JsonUtility.FromJson<GameApiClient.RunSnapshot>(json);

            Assert.That(run.campaignTitle, Is.EqualTo("NUPJUK : The Last Commit"));
            Assert.That(run.premise, Is.EqualTo("코드리아 붕괴 복구"));
            Assert.That(run.safeTravelCount, Is.EqualTo(4));
            Assert.That(run.currentBeat, Does.Contain("내부 원인"));
            Assert.That(run.endingCode, Is.EqualTo("ENDING_PRESERVE_THE_SCARS"));
        }

        [UnityTest]
        public IEnumerator Controller_DoesNotTreatAnEmptyServerEncounterDtoAsActive()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7305);
            KeyboardWandererDemoController controller = CreateAuthoredController("Empty Encounter DTO Test");
            yield return null;

            Invoke(controller, "StartRun", service, false);
            SetField(controller, "_serverOnline", true);
            SetField(controller, "_serverRun", new GameApiClient.RunSnapshot
            {
                activeEncounter = new GameApiClient.ActiveEncounterSnapshot()
            });
            Invoke(controller, "SyncEncounterStateFromServer");

            Assert.That(GetField(controller, "_encounterMoveRequired"), Is.False);
            Assert.That(GetField(controller, "_encounterStagingCoord"), Is.Null);
        }

        [UnityTest]
        public IEnumerator Controller_ShowsAndValidatesTheSelectedServerTarget()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7306);
            KeyboardWandererDemoController controller = CreateAuthoredController("Server Target Presentation Test");
            yield return null;

            Guid playerId = Guid.NewGuid();
            Guid targetId = Guid.NewGuid();
            Invoke(controller, "StartRun", service, false);
            SetField(controller, "_serverOnline", true);
            SetField(controller, "_serverRun", new GameApiClient.RunSnapshot
            {
                status = "active",
                focus = 5,
                playerEntityId = playerId.ToString(),
                entities = new[]
                {
                    new GameApiClient.EntitySnapshot
                    {
                        id = playerId.ToString(), kind = "player", name = "넙죽이",
                        position = new GameApiClient.PositionSnapshot { x = 10, y = 10 },
                        state = new GameApiClient.EntityStateSnapshot { hp = 10, maxHp = 10 }
                    },
                    new GameApiClient.EntitySnapshot
                    {
                        id = targetId.ToString(), kind = "npc", name = "코멘트",
                        position = new GameApiClient.PositionSnapshot { x = 12, y = 11 },
                        state = new GameApiClient.EntityStateSnapshot { hp = 4, maxHp = 4 }
                    }
                }
            });
            Selection(controller).ResetSelection(AbilityKind.Search);
            Selection(controller).SelectPrimary(targetId);

            Assert.That(Invoke(controller, "IsSkillEnabledForCurrentTarget", AbilityKind.Search), Is.True);
            string detail = (string)Invoke(controller, "SelectionStatusDetail", service.CurrentView);
            Assert.That(detail, Does.Contain("AI가 개입 대상"));
            Assert.That(detail, Does.Not.Contain("선택됨"));
        }

        [UnityTest]
        public IEnumerator ServerSnapshot_ShowsActiveHealthRatioAndHidesInactiveActors()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7347);
            KeyboardWandererDemoController controller = CreateAuthoredController("Server Entity Visibility Test");
            yield return null;
            Invoke(controller, "StartRun", service, false);

            Guid playerId = Guid.NewGuid();
            Guid activeEnemyId = Guid.NewGuid();
            Guid disabledEnemyId = Guid.NewGuid();
            Guid fledEnemyId = Guid.NewGuid();
            GridCoord origin = service.CurrentView.PlayerPosition;
            var run = new GameApiClient.RunSnapshot
            {
                id = Guid.NewGuid().ToString(),
                status = "active",
                playerEntityId = playerId.ToString(),
                entities = new[]
                {
                    ServerEntity(playerId, "player", "넙죽이", origin.X, origin.Y, 10, 10),
                    ServerEntity(activeEnemyId, "enemy", "활성 적", origin.X + 1, origin.Y, 2, 4),
                    ServerEntity(disabledEnemyId, "enemy", "비활성 적", origin.X + 2, origin.Y, 0, 4,
                        disabled: true),
                    ServerEntity(fledEnemyId, "enemy", "이탈 적", origin.X + 3, origin.Y, 3, 4,
                        fled: true)
                }
            };
            SetField(controller, "_serverOnline", true);
            SetField(controller, "_serverRun", run);
            SetField(controller, "_lastServerSceneSequence", Array.Empty<GameApiClient.SceneActionSnapshot>());
            SetField(controller, "_pendingRuntimeRenderSequence", Array.Empty<GameApiClient.SceneActionSnapshot>());

            Invoke(controller, "SyncServerEntityVisuals", run);

            IDictionary visuals = (IDictionary)GetField(controller, "_entityVisuals");
            object activeVisual = visuals[activeEnemyId];
            GameObject activeRoot = (GameObject)GetRuntimeField(activeVisual, "Root");
            GameObject healthBack = (GameObject)GetRuntimeField(activeVisual, "HealthBack");
            GameObject healthFill = (GameObject)GetRuntimeField(activeVisual, "HealthFill");
            Assert.That(activeRoot.activeSelf, Is.True);
            Assert.That(healthBack.activeSelf, Is.True);
            Assert.That(healthFill.activeSelf, Is.True);
            Assert.That(healthFill.transform.localScale.x, Is.EqualTo(0.37f).Within(0.001f),
                "2 / 4 HP must render as exactly half of the authored fill width.");

            GameObject disabledRoot = (GameObject)GetRuntimeField(visuals[disabledEnemyId], "Root");
            GameObject fledRoot = (GameObject)GetRuntimeField(visuals[fledEnemyId], "Root");
            Assert.That(disabledRoot.activeSelf, Is.False,
                "재접속 스냅샷의 disabled 엔티티가 월드에 다시 나타나면 안 됩니다.");
            Assert.That(fledRoot.activeSelf, Is.False,
                "이미 fled 상태인 엔티티가 재동기화 후 남아 있으면 안 됩니다.");
        }

        private static void MoveToLegacyChoiceBoundary(KeyboardWandererDemoController controller)
        {
            // This helper owns the whole actionable boundary, not only the dialogue page.
            // Leaving TutorialPresenter active makes the same test depend on a preference
            // written by whichever fixture happened to run before it.
            ((TutorialPresenter)GetField(controller, "_tutorialPresenter")).Start(false);
            string[] pages = (string[])Invoke(controller, "BuildDialoguePages", "fallback");
            DialoguePresenter dialogue = (DialoguePresenter)GetField(controller, "_dialoguePresenter");
            dialogue.Restore(pages.Length - 1, false);
            Invoke(controller, "RefreshFlowPhase");
        }

        private static object Invoke(object target, string name, params object[] values)
        {
            MethodInfo method = target.GetType().GetMethod(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Missing private method " + name);
            return method.Invoke(target, values);
        }

        private KeyboardWandererDemoController CreateAuthoredController(string name)
        {
            _controllerObject = new GameObject(name);
            _controllerObject.SetActive(false);

            Transform fixtures = Child(_controllerObject.transform, "Authored Test Fixtures");
            KeyboardWandererEntityView entityPrefab = BuildEntityFixture(fixtures);
            GameObject landmarkPrefab = new GameObject("Landmark Fixture", typeof(SpriteRenderer));
            landmarkPrefab.transform.SetParent(fixtures, false);

            GameObject worldObject = new GameObject("Authored World", typeof(Grid), typeof(KeyboardWandererWorldView));
            worldObject.transform.SetParent(_controllerObject.transform, false);
            GameObject terrainObject = new GameObject("Terrain", typeof(Tilemap), typeof(TilemapRenderer));
            terrainObject.transform.SetParent(worldObject.transform, false);
            Transform staticRoot = Child(worldObject.transform, "Static Objects");
            Transform runtimeRoot = Child(worldObject.transform, "Runtime Objects");
            Transform entities = Child(runtimeRoot, "Entities");
            Transform landmarks = Child(runtimeRoot, "Landmarks");
            Transform effects = Child(runtimeRoot, "Effects");
            GameObject cursorObject = new GameObject("Selection Cursor", typeof(SpriteRenderer));
            cursorObject.transform.SetParent(worldObject.transform, false);
            KeyboardWandererWorldView world = worldObject.GetComponent<KeyboardWandererWorldView>();
            world.Configure(terrainObject.GetComponent<Tilemap>(), staticRoot, entities, landmarks, effects,
                cursorObject.GetComponent<SpriteRenderer>());
            KeyboardWandererVisualAssetLibrary visualAssets =
                worldObject.AddComponent<KeyboardWandererVisualAssetLibrary>();
            visualAssets.ConfigureManifest(null);
            KeyboardWandererMinimapRenderer minimapRenderer =
                worldObject.AddComponent<KeyboardWandererMinimapRenderer>();
            KeyboardWandererPathPlanner pathPlanner =
                worldObject.AddComponent<KeyboardWandererPathPlanner>();
            Transform decorationPool = Child(worldObject.transform, "Decoration Pool");
            decorationPool.gameObject.SetActive(false);
            KeyboardWandererBiomeDecorationRenderer decorationRenderer =
                worldObject.AddComponent<KeyboardWandererBiomeDecorationRenderer>();
            decorationRenderer.Configure(landmarks, decorationPool);
            Transform entityPool = Child(worldObject.transform, "Entity Pool");
            entityPool.gameObject.SetActive(false);
            KeyboardWandererEntityVisualFactory entityVisualFactory =
                worldObject.AddComponent<KeyboardWandererEntityVisualFactory>();
            entityVisualFactory.Configure(entityPrefab, entities, entityPool);
            KeyboardWandererEntityAnimationDriver entityAnimationDriver =
                worldObject.AddComponent<KeyboardWandererEntityAnimationDriver>();

            // SampleScene의 Main Camera가 이미 AudioListener를 가지므로 테스트 카메라에는 중복 생성하지 않는다.
            GameObject cameraObject = new GameObject(
                "Authored Camera", typeof(Camera), typeof(KeyboardWandererCameraController));
            cameraObject.transform.SetParent(_controllerObject.transform, false);
            KeyboardWandererCameraController cameraController =
                cameraObject.GetComponent<KeyboardWandererCameraController>();
            cameraController.Configure(cameraObject.GetComponent<Camera>());
            GameObject audioObject = new GameObject("Authored Audio", typeof(KeyboardWandererAudioController));
            audioObject.transform.SetParent(_controllerObject.transform, false);
            AudioSource music = new GameObject("Music", typeof(AudioSource)).GetComponent<AudioSource>();
            music.transform.SetParent(audioObject.transform, false);
            AudioSource sfx = new GameObject("SFX", typeof(AudioSource)).GetComponent<AudioSource>();
            sfx.transform.SetParent(audioObject.transform, false);
            KeyboardWandererAudioController audio = audioObject.GetComponent<KeyboardWandererAudioController>();
            audio.Configure(music, sfx);

            _authoringSettings = ScriptableObject.CreateInstance<KeyboardWandererAuthoringSettings>();
            _authoringSettings.Configure(null, entityPrefab, landmarkPrefab);
            KeyboardWandererInputRouter input = _controllerObject.AddComponent<KeyboardWandererInputRouter>();
            KeyboardWandererSelectionController selection =
                _controllerObject.AddComponent<KeyboardWandererSelectionController>();
            KeyboardWandererAbilityAvailability abilityAvailability =
                _controllerObject.AddComponent<KeyboardWandererAbilityAvailability>();
            KeyboardWandererTurnCoordinator turnCoordinator =
                _controllerObject.AddComponent<KeyboardWandererTurnCoordinator>();
            KeyboardWandererRunSessionController runSession =
                _controllerObject.AddComponent<KeyboardWandererRunSessionController>();
            KeyboardWandererSettingsController userSettings =
                _controllerObject.AddComponent<KeyboardWandererSettingsController>();
            userSettings.Configure(audio);
            KeyboardWandererDemoController controller = _controllerObject.AddComponent<KeyboardWandererDemoController>();
            controller.ConfigureAuthoredContent(_authoringSettings, null, world, cameraObject.GetComponent<Camera>(),
                cameraController, music, sfx, audio, input, selection, abilityAvailability, turnCoordinator, runSession, userSettings,
                visualAssets, minimapRenderer, pathPlanner, decorationRenderer,
                entityVisualFactory, entityAnimationDriver);
            _controllerObject.SetActive(true);
            return controller;
        }

        private static KeyboardWandererSelectionController Selection(KeyboardWandererDemoController controller)
        {
            KeyboardWandererSelectionController selection =
                controller.GetComponent<KeyboardWandererSelectionController>();
            Assert.That(selection, Is.Not.Null);
            return selection;
        }

        private static KeyboardWandererEntityView BuildEntityFixture(Transform parent)
        {
            GameObject root = new GameObject("Entity Fixture", typeof(KeyboardWandererEntityView));
            root.transform.SetParent(parent, false);
            SpriteRenderer actor = new GameObject("Actor", typeof(SpriteRenderer), typeof(Animator))
                .GetComponent<SpriteRenderer>();
            actor.transform.SetParent(root.transform, false);
            SpriteRenderer healthBack = new GameObject("Health Back", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
            healthBack.transform.SetParent(root.transform, false);
            SpriteRenderer healthFill = new GameObject("Health Fill", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
            healthFill.transform.SetParent(root.transform, false);
            TextMesh label = new GameObject("Finale Label", typeof(TextMesh)).GetComponent<TextMesh>();
            label.transform.SetParent(root.transform, false);
            KeyboardWandererEntityView view = root.GetComponent<KeyboardWandererEntityView>();
            view.Configure(actor, healthBack, healthFill, label);
            return view;
        }

        private static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static GridCoord FindAvailableNeighbour(RunState state, GridCoord origin)
        {
            GridCoord[] candidates =
            {
                new GridCoord(origin.X + 1, origin.Y), new GridCoord(origin.X - 1, origin.Y),
                new GridCoord(origin.X, origin.Y + 1), new GridCoord(origin.X, origin.Y - 1)
            };
            foreach (GridCoord candidate in candidates)
                if (state.Region.Contains(candidate) && state.Region.GetTile(candidate).IsWalkable &&
                    !state.Spatial.IsBlockingOccupied(state.Region.RegionId, candidate, 0))
                    return candidate;
            Assert.Fail("No available adjacent tile for the interaction fixture.");
            return origin;
        }

        private static Vector2Int FindSafeDirectionalStep(LocalTurnService service, GridCoord origin,
            out GridCoord destination)
        {
            Vector2Int[] directions =
            {
                Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down
            };
            string snapshot = LocalRunSaveService.Serialize(service);
            for (int i = 0; i < directions.Length; i++)
            {
                var candidate = new GridCoord(origin.X + directions[i].x, origin.Y + directions[i].y);
                LocalTurnService probe = LocalRunSaveService.Deserialize(snapshot);
                TurnResponse response = probe.Submit(TurnRequest.Move("direction-probe-" + i,
                    probe.CurrentView.Version, candidate));
                if (response.IsSuccess && probe.CurrentView.PlayerPosition == candidate)
                {
                    destination = candidate;
                    return directions[i];
                }
            }
            Assert.Fail("The deterministic fixture has no safe adjacent exploration tile.");
            destination = origin;
            return Vector2Int.zero;
        }

        private static object GetField(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing private field " + name);
            return field.GetValue(target);
        }

        private static object GetRuntimeField(object target, string name)
        {
            Assert.That(target, Is.Not.Null);
            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing runtime field " + name);
            return field.GetValue(target);
        }

        private static GameApiClient.EntitySnapshot ServerEntity(Guid id, string kind, string name,
            int x, int y, int hp, int maxHp, bool disabled = false, bool defeated = false, bool fled = false)
        {
            return new GameApiClient.EntitySnapshot
            {
                id = id.ToString(),
                kind = kind,
                name = name,
                assetId = kind + "-fixture",
                position = new GameApiClient.PositionSnapshot { x = x, y = y },
                state = new GameApiClient.EntityStateSnapshot
                {
                    hp = hp,
                    maxHp = maxHp,
                    disabled = disabled,
                    defeated = defeated,
                    fled = fled
                }
            };
        }

        private static object InvokeStatic(Type type, string name, params object[] arguments)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Missing private static method " + name);
            return method.Invoke(null, arguments);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing private field " + name);
            field.SetValue(target, value);
        }

    }
}
