using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using KeyboardWanderer.Gameplay;
using KeyboardWanderer.Runtime;
using UnityEngine;
using UnityEngine.Networking;

#pragma warning disable 0649 // JsonUtility populates serialized DTO fields through reflection.

namespace KeyboardWanderer.Networking
{
    /// <summary>
    /// Optional narration-only bridge. A settings key is forwarded only to the local service as a header;
    /// the deterministic fallback remains available on error.
    /// </summary>
    public sealed class GmNarrativeClient
    {
        private const string DefaultEndpoint = "http://127.0.0.1:8787/v1/gm/narrate";

        [Serializable]
        private sealed class NarrativeRequest
        {
            public int turnNo;
            public int remainingTurns;
            public string area;
            public string intent;
            public string ability;
            public int d20;
            public string outcome;
            public string normalizedAttempt;
            public string[] allowedEffects;
            public string[] recentFacts;
        }

        [Serializable]
        private sealed class ProposedOperation
        {
            public string type;
            public string text;
        }

        [Serializable]
        private sealed class NarrativeResponse
        {
            public string summary;
            public string body;
            public string dialogue;
            public ProposedOperation[] proposedOps;
            public ProposedOperation[] appliedOps;
            public ProposedOperation[] rejectedOps;
            public bool fallbackUsed;
            public string model;
        }

        public sealed class Result
        {
            public bool IsSuccess { get; }
            public string Summary { get; }
            public string Narrative { get; }
            public string Error { get; }
            public bool FallbackUsed { get; }
            public string Model { get; }
            public IReadOnlyList<string> ProposedHints { get; }
            public IReadOnlyList<string> AppliedHints { get; }
            public IReadOnlyList<string> RejectedHints { get; }

            public Result(
                bool isSuccess,
                string summary,
                string narrative,
                string error,
                bool fallbackUsed = false,
                string model = null,
                IReadOnlyList<string> proposedHints = null,
                IReadOnlyList<string> appliedHints = null,
                IReadOnlyList<string> rejectedHints = null)
            {
                IsSuccess = isSuccess;
                Summary = summary;
                Narrative = narrative;
                Error = error;
                FallbackUsed = fallbackUsed;
                Model = model;
                ProposedHints = proposedHints ?? Array.Empty<string>();
                AppliedHints = appliedHints ?? Array.Empty<string>();
                RejectedHints = rejectedHints ?? Array.Empty<string>();
            }
        }

        private readonly string _endpoint;

        public GmNarrativeClient(string endpoint = null)
        {
            _endpoint = KeyboardWandererEndpointResolver.ResolveGmEndpoint(endpoint, DefaultEndpoint);
        }

        public IEnumerator RequestNarrative(
            RunView view,
            TurnResponse response,
            AbilityKind ability,
            string normalizedSelection,
            string areaName,
            Action<Result> completed)
        {
            if (view == null || response == null || !response.IsSuccess)
            {
                completed?.Invoke(new Result(false, null, null, "A committed turn is required."));
                yield break;
            }

            var facts = new List<string>
            {
                "WORLD_CODRIA · 코드리아",
                "PROTAGONIST_NUPJUKYI · 넙죽이",
                "ARTIFACT_ADMIN_KEYBOARD · 관리자 키보드",
                "HP " + view.Health + "/" + view.MaxHealth,
                "Focus " + view.Focus + "/" + view.MaxFocus,
                "Quest: " + view.QuestProgress
            };
            int factStart = Math.Max(0, view.GmLog.Count - 4);
            for (int i = factStart; i < view.GmLog.Count; i++)
                facts.Add(view.GmLog[i]);
            for (int i = 0; i < response.Events.Count && facts.Count < 12; i++)
                facts.Add(response.Events[i]);

            var payload = new NarrativeRequest
            {
                turnNo = response.TurnNo,
                remainingTurns = view.RemainingTurns,
                area = areaName ?? string.Empty,
                ability = ability.ToString(),
                // The legacy transport field carries a rules-derived selection summary, never
                // player-authored natural language or a mechanical instruction.
                intent = string.IsNullOrWhiteSpace(normalizedSelection)
                    ? response.NormalizedAttempt
                    : normalizedSelection.Trim(),
                d20 = response.D20,
                outcome = SnakeCase(response.Outcome.ToString()),
                normalizedAttempt = response.NormalizedAttempt ?? string.Empty,
                allowedEffects = new[] { "fact_hint", "rumor_hint", "npc_memory_hint", "quest_hint", "ambient_cue" },
                recentFacts = facts.ToArray()
            };

            byte[] body = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
            using (var request = new UnityWebRequest(_endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Accept", "application/json");
                string geminiApiKey = KeyboardWandererGeminiKeyStore.Current;
                if (!string.IsNullOrWhiteSpace(geminiApiKey))
                    request.SetRequestHeader("x-gemini-api-key", geminiApiKey);
                request.timeout = 65;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    completed?.Invoke(new Result(false, null, null,
                        "GM service unavailable (" + request.responseCode + "): " + request.error));
                    yield break;
                }

                string json = request.downloadHandler.text ?? string.Empty;
                NarrativeResponse envelope = ParseResponse(json);
                if (envelope == null || string.IsNullOrWhiteSpace(envelope.body))
                {
                    completed?.Invoke(new Result(false, null, null, "GM response did not contain narrative text."));
                    yield break;
                }

                string narrative = ClampSentences(ClampText(envelope.body, 700), 4);
                if (!string.IsNullOrWhiteSpace(envelope.dialogue))
                    narrative += "\n\n“" + ClampText(envelope.dialogue, 400) + "”";
                completed?.Invoke(new Result(true, ClampText(envelope.summary, 160), narrative, null,
                    envelope.fallbackUsed, ClampText(envelope.model, 80),
                    SafeHints(envelope.proposedOps), SafeHints(envelope.appliedOps), SafeHints(envelope.rejectedOps)));
            }
        }

        private static IReadOnlyList<string> SafeHints(ProposedOperation[] operations)
        {
            if (operations == null || operations.Length == 0)
                return Array.Empty<string>();
            var hints = new List<string>();
            for (int i = 0; i < operations.Length && hints.Count < 8; i++)
            {
                ProposedOperation operation = operations[i];
                if (operation == null || !IsAllowedHint(operation.type) || string.IsNullOrWhiteSpace(operation.text))
                    continue;
                hints.Add(operation.type + " · " + ClampText(operation.text, 240));
            }
            return hints;
        }

        private static bool IsAllowedHint(string type)
        {
            return type == "fact_hint" || type == "rumor_hint" || type == "npc_memory_hint" ||
                   type == "quest_hint" || type == "ambient_cue";
        }

        private static string ClampText(string value, int maximum)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            string text = value.Trim();
            return text.Length <= maximum ? text : text.Substring(0, maximum);
        }

        private static string ClampSentences(string value, int maximum)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            int count = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c != '.' && c != '!' && c != '?') continue;
                count++;
                if (count == maximum) return value.Substring(0, i + 1);
            }
            return value;
        }

        private static NarrativeResponse ParseResponse(string json)
        {
            try
            {
                NarrativeResponse envelope = JsonUtility.FromJson<NarrativeResponse>(json);
                if (envelope != null && !string.IsNullOrWhiteSpace(envelope.body))
                    return envelope;
            }
            catch (ArgumentException)
            {
                // The deterministic fallback remains authoritative when an optional response is malformed.
            }

            Match match = Regex.Match(json,
                "\\\"body\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"])*)\\\"",
                RegexOptions.CultureInvariant);
            if (!match.Success)
                return null;

            try
            {
                return new NarrativeResponse { body = Regex.Unescape(match.Groups[1].Value) };
            }
            catch (ArgumentException)
            {
                return new NarrativeResponse { body = match.Groups[1].Value };
            }
        }

        private static string SnakeCase(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            return Regex.Replace(value, "(?<!^)([A-Z])", "_$1").ToLowerInvariant();
        }
    }
}
#pragma warning restore 0649
