using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

#pragma warning disable 0649 // JsonUtility populates serialized DTO fields through reflection.

namespace KeyboardWanderer.Networking
{
    /// <summary>
    /// Request half of the SCENE_TRANSITION_PLAN contract: the game's current state plus the
    /// allowlist of candidate ids the model may choose from. Everything outside these candidates
    /// is structurally impossible for the model to select — the server sizes its response schema
    /// to exactly these arrays.
    /// </summary>
    [Serializable]
    public sealed class SceneTransitionRequest
    {
        public string protocolVersion = "1.0";
        public string schemaVersion = "1.0";
        public string requestType = "SCENE_TRANSITION_PLAN";
        public string requestId;
        public SceneTransitionRun run;
        public SceneTransitionContext context;
        public SceneTransitionCandidates candidates;

        public static string NewRequestId() => "req-" + Guid.NewGuid().ToString("N");
    }

    [Serializable]
    public sealed class SceneTransitionRun
    {
        public string runId;
        public string turnId;
        public int turnNo;
        public long expectedRunVersion;
    }

    [Serializable]
    public sealed class SceneTransitionContext
    {
        public string worldLayoutHash;
        public string contextHash;
        public string currentAreaId;
        public string playerIntent;
        public string storySummary;
    }

    [Serializable]
    public sealed class SceneTransitionCandidates
    {
        public SceneDestinationCandidate[] destinations;
        public string[] storyBeatIds;
        public string[] sceneTemplateIds;
        public string[] transitionStyleIds;
        public string[] bgmCueIds;
        public string[] sfxCueIds;
        public string[] revealIds;
    }

    /// <summary>Compatible route/entry combinations for one destination (the server flattens these into paths).</summary>
    [Serializable]
    public sealed class SceneDestinationCandidate
    {
        public string destinationAreaId;
        public string[] routeIds;
        public string[] entrySlotIds;
    }

    /// <summary>
    /// Everything worth warming up while the scene plan request is in flight. Because the game
    /// authors the candidate allowlist itself, these are known at 0ms — before the model answers —
    /// so maps, music, and effects for every possible pick can start loading immediately and the
    /// chosen one is already resident when the plan arrives. Whatever the model selects is
    /// guaranteed to be one of these.
    /// </summary>
    public sealed class ScenePreloadTargets
    {
        public string[] destinationAreaIds;
        public string[] bgmCueIds;
        public string[] sfxCueIds;
        public string[] transitionStyleIds;
    }

    /// <summary>
    /// Client for <c>POST /v1/gm/scene-transitions</c> on the authoritative game service.
    /// The caller always receives a plan that passed <see cref="SceneTransitionPlanValidator"/>:
    /// a network failure, stale echo, or contract violation degrades to the same deterministic
    /// first-candidate fallback the server uses, so scene flow never stalls on the LLM. The client
    /// holds no model key and treats every response as untrusted data.
    /// </summary>
    public sealed class SceneTransitionClient
    {
        public const string DefaultBaseUrl = "http://127.0.0.1:8787";
        private const string EndpointPath = "/v1/gm/scene-transitions";
        private const int TimeoutSeconds = 30;

        /// <summary>Outcome of a plan request. <see cref="Plan"/> is always non-null and validated.</summary>
        public sealed class Result
        {
            public bool FromServer { get; }
            public SceneTransitionPlan Plan { get; }
            public long StatusCode { get; }
            public string ErrorMessage { get; }

            internal Result(bool fromServer, SceneTransitionPlan plan, long statusCode, string errorMessage)
            {
                FromServer = fromServer;
                Plan = plan;
                StatusCode = statusCode;
                ErrorMessage = errorMessage;
            }
        }

        private readonly string _baseUrl;

        public SceneTransitionClient(string baseUrl = null)
        {
            _baseUrl = KeyboardWandererEndpointResolver.ResolveBaseUrl(baseUrl, DefaultBaseUrl);
        }

        /// <summary>
        /// Same as <see cref="RequestScenePlan(SceneTransitionRequest, Action{Result})"/>, but fires
        /// <paramref name="preloadStarted"/> synchronously at call time — before any network
        /// traffic — so the caller can begin loading every candidate map and audio cue while the
        /// model decides. By the time the plan lands (~1-3s), the selected assets are warm.
        /// </summary>
        public IEnumerator RequestScenePlan(
            SceneTransitionRequest request,
            Action<ScenePreloadTargets> preloadStarted,
            Action<Result> completed)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            preloadStarted?.Invoke(BuildPreloadTargets(request));
            return RequestScenePlan(request, completed);
        }

        public IEnumerator RequestScenePlan(SceneTransitionRequest request, Action<Result> completed)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string body = JsonUtility.ToJson(request);

            using (var www = new UnityWebRequest(_baseUrl + EndpointPath, UnityWebRequest.kHttpVerbPOST))
            {
                www.downloadHandler = new DownloadHandlerBuffer();
                www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Accept", "application/json");
                www.timeout = TimeoutSeconds;
                yield return www.SendWebRequest();

                long statusCode = www.responseCode;
                bool httpOk = www.result == UnityWebRequest.Result.Success && statusCode >= 200 && statusCode < 300;
                if (!httpOk)
                {
                    string message = string.IsNullOrWhiteSpace(www.error) ? "장면 전환 서버 요청에 실패했습니다." : www.error;
                    completed?.Invoke(new Result(false, BuildLocalFallbackPlan(request), statusCode, message));
                    yield break;
                }

                SceneTransitionPlan plan = ParsePlan(www.downloadHandler?.text);
                SceneTransitionPlanValidation validation = plan == null
                    ? null
                    : SceneTransitionPlanValidator.Validate(
                        plan,
                        expectedRequestId: request.requestId,
                        expectedEcho: ExpectedEcho(request),
                        allowedIds: BuildAllowlist(request),
                        expectedChoiceCount: 2);
                if (validation == null || !validation.IsValid)
                {
                    string message = validation == null ? "장면 전환 응답을 해석할 수 없습니다." : validation.ErrorSummary;
                    completed?.Invoke(new Result(false, BuildLocalFallbackPlan(request), statusCode, message));
                    yield break;
                }

                completed?.Invoke(new Result(true, plan, statusCode, null));
            }
        }

        /// <summary>The response echo this request must come back with; anything else is stale and discarded.</summary>
        public static SceneTransitionEcho ExpectedEcho(SceneTransitionRequest request)
        {
            return new SceneTransitionEcho
            {
                runId = request.run?.runId,
                turnId = request.run?.turnId,
                turnNo = request.run?.turnNo ?? 0,
                expectedRunVersion = request.run?.expectedRunVersion ?? 0,
                worldLayoutHash = request.context?.worldLayoutHash,
                contextHash = request.context?.contextHash,
            };
        }

        /// <summary>
        /// Distinct asset ids worth warming for this request, available before the request is even
        /// sent. Preloading all of them bounds the worst case: the plan can only pick from here.
        /// </summary>
        public static ScenePreloadTargets BuildPreloadTargets(SceneTransitionRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var areas = new List<string>();
            var seenAreas = new HashSet<string>();
            var destinations = request.candidates?.destinations;
            if (destinations != null)
            {
                foreach (var destination in destinations)
                {
                    var areaId = destination?.destinationAreaId;
                    if (!string.IsNullOrEmpty(areaId) && seenAreas.Add(areaId)) areas.Add(areaId);
                }
            }

            return new ScenePreloadTargets
            {
                destinationAreaIds = areas.ToArray(),
                bgmCueIds = Distinct(request.candidates?.bgmCueIds),
                sfxCueIds = Distinct(request.candidates?.sfxCueIds),
                transitionStyleIds = Distinct(request.candidates?.transitionStyleIds),
            };
        }

        /// <summary>Every id the request offered — the only ids a valid plan may reference.</summary>
        public static HashSet<string> BuildAllowlist(SceneTransitionRequest request)
        {
            var ids = new HashSet<string>();
            var candidates = request.candidates;
            if (candidates == null) return ids;
            if (candidates.destinations != null)
            {
                foreach (var destination in candidates.destinations)
                {
                    if (destination == null) continue;
                    Add(ids, destination.destinationAreaId);
                    AddRange(ids, destination.routeIds);
                    AddRange(ids, destination.entrySlotIds);
                }
            }

            AddRange(ids, candidates.storyBeatIds);
            AddRange(ids, candidates.sceneTemplateIds);
            AddRange(ids, candidates.transitionStyleIds);
            AddRange(ids, candidates.bgmCueIds);
            AddRange(ids, candidates.sfxCueIds);
            AddRange(ids, candidates.revealIds);
            return ids;
        }

        /// <summary>
        /// Mirror of the server's deterministic fallback: first candidate of every list, generic
        /// safe prose. Used when the server is unreachable or its response fails validation.
        /// </summary>
        public static SceneTransitionPlan BuildLocalFallbackPlan(SceneTransitionRequest request)
        {
            var destination = FirstDestination(request);
            string intent = request.context?.playerIntent ?? "다음 장면으로 이동한다";
            string summary = intent.Length > 120 ? intent.Substring(0, 120) : intent;
            return new SceneTransitionPlan
            {
                protocolVersion = request.protocolVersion,
                schemaVersion = request.schemaVersion,
                requestType = request.requestType,
                requestId = request.requestId,
                status = "OK",
                echo = ExpectedEcho(request),
                selection = new SceneSelection
                {
                    destinationAreaId = destination?.destinationAreaId,
                    routeId = First(destination?.routeIds),
                    entrySlotId = First(destination?.entrySlotIds),
                    storyBeatId = First(request.candidates?.storyBeatIds),
                    sceneTemplateId = First(request.candidates?.sceneTemplateIds),
                },
                transition = new SceneTransition
                {
                    transitionStyleId = First(request.candidates?.transitionStyleIds),
                    bgmCueId = First(request.candidates?.bgmCueIds),
                    sfxCueIds = Array.Empty<string>(),
                    cameraCue = "CAMERA_FOLLOW",
                    summary = summary,
                    body = summary,
                },
                scenePlan = new ScenePlanDetail
                {
                    sceneGoal = intent.Length > 80 ? intent.Substring(0, 80) : intent,
                    conflict = "예상치 못한 기척이 느껴진다.",
                    revealIds = Array.Empty<string>(),
                    suggestedChoices = new[]
                    {
                        new SuggestedChoice { choiceId = "choice-1", label = "계속 나아간다", intentTag = "전진" },
                        new SuggestedChoice { choiceId = "choice-2", label = "주변을 살핀다", intentTag = "경계" },
                    },
                },
                usage = new SceneUsage
                {
                    modelProfile = "deterministic",
                    modelId = "local-scene-fallback-v1",
                    inputTokens = 0,
                    outputTokens = 0,
                    latencyMs = 0,
                    finishReason = "local_fallback",
                },
            };
        }

        private static SceneTransitionPlan ParsePlan(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonUtility.FromJson<SceneTransitionPlan>(json);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static SceneDestinationCandidate FirstDestination(SceneTransitionRequest request)
        {
            var destinations = request.candidates?.destinations;
            return destinations != null && destinations.Length > 0 ? destinations[0] : null;
        }

        private static string First(string[] values) => values != null && values.Length > 0 ? values[0] : null;

        private static string[] Distinct(string[] values)
        {
            if (values == null || values.Length == 0) return Array.Empty<string>();
            var seen = new HashSet<string>();
            var result = new List<string>(values.Length);
            foreach (var value in values)
            {
                if (!string.IsNullOrEmpty(value) && seen.Add(value)) result.Add(value);
            }

            return result.ToArray();
        }

        private static void Add(ISet<string> ids, string value)
        {
            if (!string.IsNullOrEmpty(value)) ids.Add(value);
        }

        private static void AddRange(ISet<string> ids, string[] values)
        {
            if (values == null) return;
            foreach (var value in values) Add(ids, value);
        }
    }
}
