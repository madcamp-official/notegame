using System;
using System.Collections;
using KeyboardWanderer.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KeyboardWanderer.Tests.PlayMode
{
    /// <summary>
    /// Live integration test: drives the real <see cref="SceneTransitionClient"/> against a running
    /// game service so the whole loop — Unity request, HTTP, server, model, Unity parse+validate —
    /// is exercised for real instead of against a hand-written fixture.
    ///
    /// It is skipped (not failed) when no server answers, so ordinary local and CI runs stay green.
    /// Point it somewhere else with the KW_GAME_SERVER_URL environment variable.
    /// </summary>
    public sealed class SceneTransitionLiveIntegrationTests
    {
        private static string BaseUrl =>
            Environment.GetEnvironmentVariable("KW_GAME_SERVER_URL") ?? SceneTransitionClient.DefaultBaseUrl;

        private static SceneTransitionRequest MakeRequest()
        {
            return new SceneTransitionRequest
            {
                requestId = SceneTransitionRequest.NewRequestId(),
                run = new SceneTransitionRun { runId = "run-live-01", turnId = "turn-live-01", turnNo = 7, expectedRunVersion = 3 },
                context = new SceneTransitionContext
                {
                    worldLayoutHash = "world-live-hash",
                    contextHash = "context-live-hash",
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

        [UnityTest]
        public IEnumerator RequestScenePlan_AgainstLiveServer_ReturnsAServerAuthoredPlan()
        {
            var request = MakeRequest();
            var client = new SceneTransitionClient(BaseUrl);
            SceneTransitionClient.Result result = null;

            yield return client.RequestScenePlan(request, value => result = value);

            Assert.That(result, Is.Not.Null, "The client never invoked its completion callback.");
            if (!result.FromServer && result.StatusCode == 0)
            {
                Assert.Ignore($"No game service answered at {BaseUrl}; skipping the live integration test.");
            }

            Assert.That(result.FromServer, Is.True,
                $"The server answered but the plan was rejected: {result.ErrorMessage}");

            var plan = result.Plan;
            var allowlist = SceneTransitionClient.BuildAllowlist(request);
            Assert.That(plan.requestId, Is.EqualTo(request.requestId));
            Assert.That(plan.echo.runId, Is.EqualTo(request.run.runId));
            Assert.That(plan.echo.expectedRunVersion, Is.EqualTo(request.run.expectedRunVersion));
            Assert.That(allowlist, Does.Contain(plan.selection.destinationAreaId));
            Assert.That(allowlist, Does.Contain(plan.selection.routeId));
            Assert.That(allowlist, Does.Contain(plan.selection.entrySlotId));
            Assert.That(allowlist, Does.Contain(plan.transition.bgmCueId));
            Assert.That(plan.scenePlan.suggestedChoices.Length, Is.EqualTo(2));
            Assert.That(plan.transition.summary, Is.Not.Empty);

            Debug.Log($"[live] {plan.selection.destinationAreaId} · {plan.selection.routeId} · {plan.transition.bgmCueId} " +
                      $"| {plan.transition.summary} | {plan.usage.modelId} {plan.usage.latencyMs}ms");
        }

        [UnityTest]
        public IEnumerator RequestScenePlan_WhenServerUnreachable_YieldsAValidLocalFallback()
        {
            var request = MakeRequest();
            // Reserved-for-documentation port that nothing listens on.
            var client = new SceneTransitionClient("http://127.0.0.1:9");
            SceneTransitionClient.Result result = null;

            yield return client.RequestScenePlan(request, value => result = value);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.FromServer, Is.False);
            Assert.That(result.Plan, Is.Not.Null, "A fallback plan must still be supplied.");

            var validation = SceneTransitionPlanValidator.Validate(
                result.Plan,
                expectedRequestId: request.requestId,
                expectedEcho: SceneTransitionClient.ExpectedEcho(request),
                allowedIds: SceneTransitionClient.BuildAllowlist(request),
                expectedChoiceCount: 2);
            Assert.That(validation.IsValid, Is.True, validation.ErrorSummary);
        }
    }
}
