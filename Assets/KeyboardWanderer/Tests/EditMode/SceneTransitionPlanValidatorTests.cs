using System.Collections.Generic;
using KeyboardWanderer.Networking;
using NUnit.Framework;
using UnityEngine;

namespace KeyboardWanderer.Tests
{
    public sealed class SceneTransitionPlanValidatorTests
    {
        [Test]
        public void FromJson_CamelCasePayload_PopulatesEveryField()
        {
            var plan = JsonUtility.FromJson<SceneTransitionPlan>(SamplePlanJson);

            Assert.That(plan, Is.Not.Null);
            Assert.That(plan.requestId, Is.EqualTo("req-001"));
            Assert.That(plan.echo.turnNo, Is.EqualTo(31));
            Assert.That(plan.echo.expectedRunVersion, Is.EqualTo(18L));
            Assert.That(plan.selection.destinationAreaId, Is.EqualTo("AREA_MOON_MARKET"));
            Assert.That(plan.transition.sfxCueIds, Is.EqualTo(new[] { "SFX_RAIN", "SFX_FOOTSTEP" }));
            Assert.That(plan.scenePlan.suggestedChoices.Length, Is.EqualTo(2));
            Assert.That(plan.scenePlan.suggestedChoices[1].intentTag, Is.EqualTo("회피"));
            Assert.That(plan.usage.finishReason, Is.EqualTo("stop"));
        }

        [Test]
        public void Validate_WellFormedPlan_IsValid()
        {
            var result = SceneTransitionPlanValidator.Validate(MakeValidPlan());

            Assert.That(result.IsValid, Is.True, result.ErrorSummary);
        }

        [Test]
        public void Validate_NullPlan_ReportsError()
        {
            var result = SceneTransitionPlanValidator.Validate(null);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("plan is null"));
        }

        [Test]
        public void Validate_MissingSelectionId_ReportsError()
        {
            var plan = MakeValidPlan();
            plan.selection.entrySlotId = "";

            var result = SceneTransitionPlanValidator.Validate(plan);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("selection.entrySlotId"));
        }

        [Test]
        public void Validate_MismatchedRequestId_ReportsError()
        {
            var result = SceneTransitionPlanValidator.Validate(MakeValidPlan(), expectedRequestId: "req-999");

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("requestId"));
        }

        [Test]
        public void Validate_StaleEcho_ReportsError()
        {
            var expected = new SceneTransitionEcho
            {
                runId = "run-06",
                turnId = "turn-031",
                turnNo = 31,
                expectedRunVersion = 18,
            };

            var plan = MakeValidPlan();
            plan.echo.expectedRunVersion = 17; // an older response arriving late

            var result = SceneTransitionPlanValidator.Validate(plan, expectedEcho: expected);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("expectedRunVersion"));
        }

        [Test]
        public void Validate_MatchingEcho_IsValid()
        {
            var expected = new SceneTransitionEcho
            {
                runId = "run-06",
                turnId = "turn-031",
                turnNo = 31,
                expectedRunVersion = 18,
            };

            var result = SceneTransitionPlanValidator.Validate(MakeValidPlan(), expectedEcho: expected);

            Assert.That(result.IsValid, Is.True, result.ErrorSummary);
        }

        [Test]
        public void Validate_OverlongSummary_ReportsError()
        {
            var plan = MakeValidPlan();
            plan.transition.summary = new string('가', SceneTransitionPlanValidator.MaxSummaryLength + 1);

            var result = SceneTransitionPlanValidator.Validate(plan);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("transition.summary"));
        }

        [Test]
        public void Validate_UnexpectedChoiceCount_ReportsError()
        {
            var result = SceneTransitionPlanValidator.Validate(MakeValidPlan(), expectedChoiceCount: 3);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("suggestedChoices"));
        }

        [Test]
        public void Validate_IdOutsideAllowlist_ReportsError()
        {
            var allowlist = new HashSet<string>
            {
                "AREA_MOON_MARKET", "ROUTE_BRIDGE", "ENTRY_GATE", "BEAT_FACE_RIVAL", "SCENE_DISCOVERY",
                "TRANSITION_FADE", "BGM_SUSPENSE_LOW", "SFX_RAIN", "SFX_FOOTSTEP",
                // REVEAL_SIGIL deliberately omitted so the reveal id fails the allowlist check.
            };

            var result = SceneTransitionPlanValidator.Validate(MakeValidPlan(), allowedIds: allowlist);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("REVEAL_SIGIL"));
        }

        [Test]
        public void Validate_IdsWithinAllowlist_IsValid()
        {
            var allowlist = new HashSet<string>
            {
                "AREA_MOON_MARKET", "ROUTE_BRIDGE", "ENTRY_GATE", "BEAT_FACE_RIVAL", "SCENE_DISCOVERY",
                "TRANSITION_FADE", "BGM_SUSPENSE_LOW", "SFX_RAIN", "SFX_FOOTSTEP", "REVEAL_SIGIL",
            };

            var result = SceneTransitionPlanValidator.Validate(MakeValidPlan(), allowedIds: allowlist);

            Assert.That(result.IsValid, Is.True, result.ErrorSummary);
        }

        private static SceneTransitionPlan MakeValidPlan()
        {
            return new SceneTransitionPlan
            {
                protocolVersion = "1.0",
                schemaVersion = "1.0",
                requestType = "SCENE_TRANSITION_PLAN",
                requestId = "req-001",
                status = "OK",
                echo = new SceneTransitionEcho
                {
                    runId = "run-06",
                    turnId = "turn-031",
                    turnNo = 31,
                    expectedRunVersion = 18,
                    worldLayoutHash = "world-34140fe6",
                    contextHash = "context-dffe69e0",
                },
                selection = new SceneSelection
                {
                    destinationAreaId = "AREA_MOON_MARKET",
                    routeId = "ROUTE_BRIDGE",
                    entrySlotId = "ENTRY_GATE",
                    storyBeatId = "BEAT_FACE_RIVAL",
                    sceneTemplateId = "SCENE_DISCOVERY",
                },
                transition = new SceneTransition
                {
                    transitionStyleId = "TRANSITION_FADE",
                    bgmCueId = "BGM_SUSPENSE_LOW",
                    sfxCueIds = new[] { "SFX_RAIN", "SFX_FOOTSTEP" },
                    cameraCue = "CAMERA_FOLLOW",
                    summary = "시장으로 이동해 신호를 다시 찾는다.",
                    body = "시장으로 이동해 신호를 다시 찾는다.",
                },
                scenePlan = new ScenePlanDetail
                {
                    sceneGoal = "무전 신호를 추적한다.",
                    conflict = "습격자가 접근한다.",
                    revealIds = new[] { "REVEAL_SIGIL" },
                    suggestedChoices = new[]
                    {
                        new SuggestedChoice { choiceId = "choice-1", label = "신호 추적", intentTag = "조사" },
                        new SuggestedChoice { choiceId = "choice-2", label = "몸을 숨긴다", intentTag = "회피" },
                    },
                },
                usage = new SceneUsage
                {
                    modelProfile = "compact-decision-v1",
                    modelId = "game-director",
                    inputTokens = 399,
                    outputTokens = 134,
                    latencyMs = 1534,
                    finishReason = "stop",
                },
            };
        }

        private const string SamplePlanJson = @"{
            ""protocolVersion"": ""1.0"",
            ""schemaVersion"": ""1.0"",
            ""requestType"": ""SCENE_TRANSITION_PLAN"",
            ""requestId"": ""req-001"",
            ""status"": ""OK"",
            ""echo"": {
                ""runId"": ""run-06"",
                ""turnId"": ""turn-031"",
                ""turnNo"": 31,
                ""expectedRunVersion"": 18,
                ""worldLayoutHash"": ""world-34140fe6"",
                ""contextHash"": ""context-dffe69e0""
            },
            ""selection"": {
                ""destinationAreaId"": ""AREA_MOON_MARKET"",
                ""routeId"": ""ROUTE_BRIDGE"",
                ""entrySlotId"": ""ENTRY_GATE"",
                ""storyBeatId"": ""BEAT_FACE_RIVAL"",
                ""sceneTemplateId"": ""SCENE_DISCOVERY""
            },
            ""transition"": {
                ""transitionStyleId"": ""TRANSITION_FADE"",
                ""bgmCueId"": ""BGM_SUSPENSE_LOW"",
                ""sfxCueIds"": [""SFX_RAIN"", ""SFX_FOOTSTEP""],
                ""cameraCue"": ""CAMERA_FOLLOW"",
                ""summary"": ""시장으로 이동해 신호를 다시 찾는다."",
                ""body"": ""시장으로 이동해 신호를 다시 찾는다.""
            },
            ""scenePlan"": {
                ""sceneGoal"": ""무전 신호를 추적한다."",
                ""conflict"": ""습격자가 접근한다."",
                ""revealIds"": [""REVEAL_SIGIL""],
                ""suggestedChoices"": [
                    { ""choiceId"": ""choice-1"", ""label"": ""신호 추적"", ""intentTag"": ""조사"" },
                    { ""choiceId"": ""choice-2"", ""label"": ""몸을 숨긴다"", ""intentTag"": ""회피"" }
                ]
            },
            ""usage"": {
                ""modelProfile"": ""compact-decision-v1"",
                ""modelId"": ""game-director"",
                ""inputTokens"": 399,
                ""outputTokens"": 134,
                ""latencyMs"": 1534,
                ""finishReason"": ""stop""
            }
        }";
    }
}
