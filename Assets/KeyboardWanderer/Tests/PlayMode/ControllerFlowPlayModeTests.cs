using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using KeyboardWanderer.Core;
using KeyboardWanderer.Demo;
using KeyboardWanderer.Gameplay;
using KeyboardWanderer.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.TestTools;

namespace KeyboardWanderer.Tests.PlayMode
{
    public sealed class ControllerFlowPlayModeTests
    {
        private GameObject _controllerObject;
        private KeyboardWandererAuthoringSettings _authoringSettings;

        [SetUp]
        public void SetUp()
        {
            LocalRunSaveService.Delete();
        }

        [TearDown]
        public void TearDown()
        {
            LocalRunSaveService.Delete();
            if (_controllerObject != null) UnityEngine.Object.DestroyImmediate(_controllerObject);
            if (_authoringSettings != null) UnityEngine.Object.DestroyImmediate(_authoringSettings);
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
            SetField(controller, "_gmEnabled", false);
            SetField(controller, "_ability", AbilityKind.Copy);
            SetField(controller, "_selectedTarget", (Guid?)keyboard.EntityId);
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
        public IEnumerator Controller_OffersTwoOrThreeCanonicalRecommendationsAndAtMostTwoSecondaryObjectives()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(7302);
            KeyboardWandererDemoController controller = CreateAuthoredController("Codria Low Cognitive UI Test");
            yield return null;

            Invoke(controller, "StartRun", service, false);
            var actions = (AbilityKind[])Invoke(controller, "RecommendedActions", service.CurrentView);
            string secondary = (string)Invoke(controller, "SecondaryObjectiveText", service.CurrentView);

            Assert.That(actions.Length, Is.InRange(2, 3));
            Assert.That(actions.All(action => action == AbilityKind.Move ||
                TurnRequest.IsPublicKeyboardSkill(action)), Is.True);
            Assert.That(secondary.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Length,
                Is.LessThanOrEqualTo(2));
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
            SetField(controller, "_gmEnabled", false);
            controller.UiSetAbility(AbilityKind.Delete);
            SetField(controller, "_selectedTarget", (Guid?)enemy.EntityId);
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
            Assert.That(GetField(controller, "_lastOutcome"), Is.EqualTo("SELECTION REQUIRED"));
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

            GameObject cameraObject = new GameObject(
                "Authored Camera", typeof(Camera), typeof(AudioListener), typeof(KeyboardWandererCameraController));
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
            KeyboardWandererInputController input = _controllerObject.AddComponent<KeyboardWandererInputController>();
            KeyboardWandererDemoController controller = _controllerObject.AddComponent<KeyboardWandererDemoController>();
            controller.ConfigureAuthoredContent(_authoringSettings, null, world, cameraObject.GetComponent<Camera>(),
                cameraController, music, sfx, audio, input);
            _controllerObject.SetActive(true);
            return controller;
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
