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
using UnityEngine.Tilemaps;
using UnityEngine.TestTools;

namespace KeyboardWanderer.Tests.PlayMode
{
    public sealed class ControllerFlowPlayModeTests
    {
        private const string GmEnabledKey = "keyboard-wanderer.gm-enabled";
        private GameObject _controllerObject;
        private KeyboardWandererAuthoringSettings _authoringSettings;
        private bool _hadGmSetting;
        private int _oldGmSetting;

        [SetUp]
        public void SetUp()
        {
            LocalRunSaveService.Delete();
            _hadGmSetting = PlayerPrefs.HasKey(GmEnabledKey);
            _oldGmSetting = PlayerPrefs.GetInt(GmEnabledKey, 1);
        }

        [TearDown]
        public void TearDown()
        {
            LocalRunSaveService.Delete();
            if (_controllerObject != null) UnityEngine.Object.DestroyImmediate(_controllerObject);
            if (_authoringSettings != null) UnityEngine.Object.DestroyImmediate(_authoringSettings);
            if (_hadGmSetting) PlayerPrefs.SetInt(GmEnabledKey, _oldGmSetting);
            else PlayerPrefs.DeleteKey(GmEnabledKey);
            PlayerPrefs.Save();
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
            var enemy = new EntityState(Guid.NewGuid(), EntityKind.Enemy, "test.enemy", "테스트 적",
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
            yield return null;
            yield return null;

            EntityView committed = service.CurrentView.Entities.SingleOrDefault(entity => entity.EntityId == enemy.EntityId);
            Assert.That(service.CurrentView.CurrentTurn, Is.EqualTo(1));
            Assert.That(committed == null || committed.Health < committed.MaxHealth, Is.True);
            RunCoordinator coordinator = (RunCoordinator)GetField(controller, "_runCoordinator");
            Assert.That(coordinator.State.Turn, Is.EqualTo(1));
            Assert.That(coordinator.State.SelectedTarget, Is.Null);
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
            Assert.That(Selection(controller).Feedback, Is.Empty,
                "Gameplay submit is ignored while manually reading the opening pages.");
            DialoguePresenter dialogue = (DialoguePresenter)GetField(controller, "_dialoguePresenter");
            Assert.That(dialogue.IsDismissed, Is.False,
                "A blocked gameplay shortcut must not dismiss the current dialogue page.");
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
        public IEnumerator InterventionPage_RemainsVisibleAndOpensContextualInput()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7311);
            KeyboardWandererDemoController controller = CreateAuthoredController("Codria Intervention Page Test");
            yield return null;
            Invoke(controller, "StartRun", service, false);
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

        [Test]
        public void RunDto_ParsesCodriaCampaignAndSharedEndingState()
        {
            const string json = "{\"campaignTitle\":\"Ninja Adventure\"," +
                                "\"premise\":\"코드리아 붕괴 복구\",\"safeTravelCount\":4," +
                                "\"currentBeat\":\"관리자 통제 시스템 내부 원인 확인\"," +
                                "\"endingCode\":\"ENDING_PRESERVE_THE_SCARS\"}";
            GameApiClient.RunSnapshot run = JsonUtility.FromJson<GameApiClient.RunSnapshot>(json);

            Assert.That(run.campaignTitle, Is.EqualTo("Ninja Adventure"));
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

        private static void MoveToLegacyChoiceBoundary(KeyboardWandererDemoController controller)
        {
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

        private static object GetField(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing private field " + name);
            return field.GetValue(target);
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
