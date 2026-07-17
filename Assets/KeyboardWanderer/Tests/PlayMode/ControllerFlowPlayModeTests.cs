using System;
using System.Linq;
using System.Reflection;
using System.Collections;
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
        public IEnumerator Controller_SubmitsFreeTextIntent_ThenSaveResumePreservesAuthoritativeState()
        {
            RunState state = LocalTurnService.CreateDemo(7301).CreateSnapshot();
            EntityState npc = state.Spatial.Entities.First(entity => entity.IsActive &&
                entity.Kind == EntityKind.Npc);
            MovePlayerNear(state, npc);
            var service = new LocalTurnService(state, new SequenceD20Source(20));

            _controllerObject = new GameObject("KeyboardWanderer Controller PlayMode Test");
            KeyboardWandererDemoController controller =
                _controllerObject.AddComponent<KeyboardWandererDemoController>();
            yield return null;

            Invoke(controller, "StartRun", service, false);
            SetField(controller, "_gmEnabled", false);
            SetField(controller, "_ability", AbilityKind.Negotiate);
            SetField(controller, "_selectedTarget", (Guid?)npc.EntityId);
            SetField(controller, "_intent", "서로 지킬 조건을 먼저 듣고 강제 없는 해결책을 제안한다");
            Invoke(controller, "Submit");
            yield return null;

            Assert.That(service.CurrentView.CurrentTurn, Is.EqualTo(1));
            Assert.That(service.CurrentView.LastIntentText,
                Is.EqualTo("서로 지킬 조건을 먼저 듣고 강제 없는 해결책을 제안한다"));
            Assert.That(service.CurrentView.IntentHistory.Last(), Does.Contain("강제 없는 해결책"));

            string json = LocalRunSaveService.Serialize(service);
            LocalTurnService resumed = LocalRunSaveService.Deserialize(json);
            Assert.That(resumed.CurrentView.Version, Is.EqualTo(service.CurrentView.Version));
            Assert.That(resumed.CurrentView.CurrentTurn, Is.EqualTo(service.CurrentView.CurrentTurn));
            Assert.That(resumed.CurrentView.CampaignId, Is.EqualTo(service.CurrentView.CampaignId));
            Assert.That(resumed.CurrentView.Region.LayoutHash, Is.EqualTo(service.CurrentView.Region.LayoutHash));
        }

        [Test]
        public void RunDto_ParsesGeneratedCampaignAndNavigationState()
        {
            const string json = "{\"campaignTitle\":\"Seed 이야기\",\"premise\":\"생성된 전제\"," +
                                "\"safeTravelCount\":4,\"currentBeat\":\"숨은 진실\"," +
                                "\"endingCode\":\"MEMORY_REWEAVE\"}";
            GameApiClient.RunSnapshot run = JsonUtility.FromJson<GameApiClient.RunSnapshot>(json);

            Assert.That(run.campaignTitle, Is.EqualTo("Seed 이야기"));
            Assert.That(run.premise, Is.EqualTo("생성된 전제"));
            Assert.That(run.safeTravelCount, Is.EqualTo(4));
            Assert.That(run.currentBeat, Is.EqualTo("숨은 진실"));
            Assert.That(run.endingCode, Is.EqualTo("MEMORY_REWEAVE"));
        }

        private static void MovePlayerNear(RunState state, EntityState target)
        {
            for (int distance = 1; distance <= 2; distance++)
            {
                for (int y = target.Position.Y - distance; y <= target.Position.Y + distance; y++)
                {
                    for (int x = target.Position.X - distance; x <= target.Position.X + distance; x++)
                    {
                        var candidate = new GridCoord(x, y);
                        if (candidate.ManhattanDistance(target.Position) != distance ||
                            !state.Region.IsWalkable(candidate) ||
                            state.Spatial.IsBlockingOccupied(state.Region.RegionId, candidate, 0,
                                state.PlayerEntityId)) continue;
                        MoveResult result = state.Spatial.TryMove(state.PlayerEntityId,
                            state.Region.RegionId, candidate, 0, (regionId, coord) =>
                                regionId == state.Region.RegionId && state.Region.IsWalkable(coord));
                        Assert.That(result.IsSuccess, Is.True);
                        return;
                    }
                }
            }
            Assert.Fail("No empty coordinate near NPC for PlayMode fixture.");
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
