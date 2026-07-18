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
using UnityEngine.TestTools;

namespace KeyboardWanderer.Tests.PlayMode
{
    public sealed class ControllerFlowPlayModeTests
    {
        private GameObject _controllerObject;

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
        }

        [UnityTest]
        public IEnumerator Controller_SubmitsCanonicalSkill_AndResumePreservesAuthoritativeState()
        {
            RunState state = LocalTurnService.CreateDemo(7301).CreateSnapshot();
            EntityState keyboard = state.Spatial.Entities.Single(entity =>
                entity.AssetId == CampaignCatalog.AdministratorKeyboardId);
            var service = new LocalTurnService(state, new SequenceD20Source(20));

            _controllerObject = new GameObject("Codria Controller PlayMode Test");
            KeyboardWandererDemoController controller =
                _controllerObject.AddComponent<KeyboardWandererDemoController>();
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
            _controllerObject = new GameObject("Codria Low Cognitive UI Test");
            KeyboardWandererDemoController controller =
                _controllerObject.AddComponent<KeyboardWandererDemoController>();
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

        [Test]
        public void RunDto_ParsesCodriaCampaignAndSharedEndingState()
        {
            const string json = "{\"campaignTitle\":\"넙죽이와 붕괴한 코드 왕국\"," +
                                "\"premise\":\"코드리아 붕괴 복구\",\"safeTravelCount\":4," +
                                "\"currentBeat\":\"관리자 통제 시스템 내부 원인 확인\"," +
                                "\"endingCode\":\"ENDING_PRESERVE_THE_SCARS\"}";
            GameApiClient.RunSnapshot run = JsonUtility.FromJson<GameApiClient.RunSnapshot>(json);

            Assert.That(run.campaignTitle, Is.EqualTo("넙죽이와 붕괴한 코드 왕국"));
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

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing private field " + name);
            field.SetValue(target, value);
        }
    }
}
