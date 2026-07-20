using KeyboardWanderer.Networking;
using NUnit.Framework;
using UnityEngine;

namespace KeyboardWanderer.Tests
{
    public sealed class SceneTransitionClientTests
    {
        private static SceneTransitionRequest MakeRequest()
        {
            return new SceneTransitionRequest
            {
                requestId = "req-001-ab12cd34",
                run = new SceneTransitionRun { runId = "run-06", turnId = "turn-031", turnNo = 31, expectedRunVersion = 18 },
                context = new SceneTransitionContext
                {
                    worldLayoutHash = "world-34140fe6",
                    contextHash = "context-dffe69e0",
                    currentAreaId = "AREA_OLD_STATION",
                    playerIntent = "단서를 추적한다",
                    storySummary = "플레이어는 끊긴 무전 신호의 발신자를 찾고 있다.",
                },
                candidates = new SceneTransitionCandidates
                {
                    destinations = new[]
                    {
                        new SceneDestinationCandidate
                        {
                            destinationAreaId = "AREA_MOON_MARKET",
                            routeIds = new[] { "ROUTE_BRIDGE", "ROUTE_ALLEY" },
                            entrySlotIds = new[] { "ENTRY_GATE", "ENTRY_BELL" },
                        },
                    },
                    storyBeatIds = new[] { "BEAT_TRACE_SIGNAL", "BEAT_FACE_RIVAL" },
                    sceneTemplateIds = new[] { "SCENE_EXPLORE", "SCENE_DISCOVERY" },
                    transitionStyleIds = new[] { "TRANSITION_FADE", "TRANSITION_WIPE" },
                    bgmCueIds = new[] { "BGM_SUSPENSE_LOW", "BGM_MYSTERY_RAIN" },
                    sfxCueIds = new[] { "SFX_RAIN", "SFX_RADIO", "SFX_FOOTSTEP" },
                    revealIds = new[] { "REVEAL_SIGIL", "REVEAL_FALSE_MAP" },
                },
            };
        }

        [Test]
        public void ToJson_ProducesTheServerContractShape()
        {
            string json = JsonUtility.ToJson(MakeRequest());

            StringAssert.Contains("\"requestType\":\"SCENE_TRANSITION_PLAN\"", json);
            StringAssert.Contains("\"protocolVersion\":\"1.0\"", json);
            StringAssert.Contains("\"destinationAreaId\":\"AREA_MOON_MARKET\"", json);
            StringAssert.Contains("\"routeIds\":[\"ROUTE_BRIDGE\",\"ROUTE_ALLEY\"]", json);
            StringAssert.Contains("\"expectedRunVersion\":18", json);
            StringAssert.Contains("\"storyBeatIds\":[\"BEAT_TRACE_SIGNAL\",\"BEAT_FACE_RIVAL\"]", json);
        }

        [Test]
        public void BuildAllowlist_ContainsEveryOfferedId()
        {
            var allowlist = SceneTransitionClient.BuildAllowlist(MakeRequest());

            Assert.That(allowlist, Does.Contain("AREA_MOON_MARKET"));
            Assert.That(allowlist, Does.Contain("ROUTE_ALLEY"));
            Assert.That(allowlist, Does.Contain("ENTRY_BELL"));
            Assert.That(allowlist, Does.Contain("SFX_FOOTSTEP"));
            Assert.That(allowlist, Does.Contain("REVEAL_FALSE_MAP"));
            Assert.That(allowlist.Count, Is.EqualTo(18));
        }

        [Test]
        public void LocalFallbackPlan_PassesTheSameValidationAsAServerPlan()
        {
            var request = MakeRequest();
            var plan = SceneTransitionClient.BuildLocalFallbackPlan(request);

            var validation = SceneTransitionPlanValidator.Validate(
                plan,
                expectedRequestId: request.requestId,
                expectedEcho: SceneTransitionClient.ExpectedEcho(request),
                allowedIds: SceneTransitionClient.BuildAllowlist(request),
                expectedChoiceCount: 2);

            Assert.That(validation.IsValid, Is.True, validation.ErrorSummary);
            Assert.That(plan.selection.destinationAreaId, Is.EqualTo("AREA_MOON_MARKET"));
            Assert.That(plan.selection.routeId, Is.EqualTo("ROUTE_BRIDGE"));
            Assert.That(plan.selection.entrySlotId, Is.EqualTo("ENTRY_GATE"));
            Assert.That(plan.usage.modelId, Is.EqualTo("local-scene-fallback-v1"));
        }

        [Test]
        public void ServerShapedResponse_RoundTripsAndValidatesAgainstTheRequest()
        {
            var request = MakeRequest();
            const string serverJson = @"{
                ""protocolVersion"": ""1.0"",
                ""schemaVersion"": ""1.0"",
                ""requestType"": ""SCENE_TRANSITION_PLAN"",
                ""requestId"": ""req-001-ab12cd34"",
                ""status"": ""OK"",
                ""fallbackUsed"": false,
                ""echo"": { ""runId"": ""run-06"", ""turnId"": ""turn-031"", ""turnNo"": 31, ""expectedRunVersion"": 18,
                            ""worldLayoutHash"": ""world-34140fe6"", ""contextHash"": ""context-dffe69e0"" },
                ""selection"": { ""destinationAreaId"": ""AREA_MOON_MARKET"", ""routeId"": ""ROUTE_BRIDGE"",
                                 ""entrySlotId"": ""ENTRY_GATE"", ""storyBeatId"": ""BEAT_FACE_RIVAL"",
                                 ""sceneTemplateId"": ""SCENE_DISCOVERY"" },
                ""transition"": { ""transitionStyleId"": ""TRANSITION_FADE"", ""bgmCueId"": ""BGM_SUSPENSE_LOW"",
                                  ""sfxCueIds"": [""SFX_RAIN"", ""SFX_FOOTSTEP""], ""cameraCue"": ""CAMERA_FOLLOW"",
                                  ""summary"": ""시장으로 이동해 신호를 다시 찾는다."", ""body"": ""시장으로 이동해 신호를 다시 찾는다."" },
                ""scenePlan"": { ""sceneGoal"": ""무전 신호를 추적한다."", ""conflict"": ""습격자가 접근한다."",
                                 ""revealIds"": [""REVEAL_SIGIL""],
                                 ""suggestedChoices"": [
                                    { ""choiceId"": ""choice-1"", ""label"": ""신호 추적"", ""intentTag"": ""조사"" },
                                    { ""choiceId"": ""choice-2"", ""label"": ""몸을 숨긴다"", ""intentTag"": ""회피"" } ] },
                ""proposedOps"": [],
                ""memoryCandidates"": [],
                ""usage"": { ""modelProfile"": ""compact-decision-v1"", ""modelId"": ""game-director"",
                             ""inputTokens"": 349, ""outputTokens"": 194, ""latencyMs"": 2148, ""finishReason"": ""stop"" }
            }";

            var plan = JsonUtility.FromJson<SceneTransitionPlan>(serverJson);
            var validation = SceneTransitionPlanValidator.Validate(
                plan,
                expectedRequestId: request.requestId,
                expectedEcho: SceneTransitionClient.ExpectedEcho(request),
                allowedIds: SceneTransitionClient.BuildAllowlist(request),
                expectedChoiceCount: 2);

            Assert.That(validation.IsValid, Is.True, validation.ErrorSummary);
            Assert.That(plan.selection.storyBeatId, Is.EqualTo("BEAT_FACE_RIVAL"));
            Assert.That(plan.usage.latencyMs, Is.EqualTo(2148));
        }

        [Test]
        public void StaleServerResponse_FailsValidationAgainstANewerRequest()
        {
            var request = MakeRequest();
            request.run.expectedRunVersion = 19; // the run moved on; an in-flight response is now stale

            var stalePlan = SceneTransitionClient.BuildLocalFallbackPlan(MakeRequest());
            var validation = SceneTransitionPlanValidator.Validate(
                stalePlan,
                expectedRequestId: request.requestId,
                expectedEcho: SceneTransitionClient.ExpectedEcho(request));

            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.ErrorSummary, Does.Contain("expectedRunVersion"));
        }
    }
}
