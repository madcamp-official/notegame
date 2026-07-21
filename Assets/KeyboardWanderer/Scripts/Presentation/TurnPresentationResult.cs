using System;
using System.Collections.Generic;
using KeyboardWanderer.Gameplay;

namespace KeyboardWanderer.Presentation
{
    /// <summary>
    /// 로컬 규칙 응답과 서버 DTO를 화면에 표시할 하나의 결과 형식으로 정규화한 값이다.
    /// Presenter와 컨트롤러는 원본 응답 종류를 확인하지 않고 이 값만 읽는다.
    /// </summary>
    public sealed class TurnPresentationResult
    {
        public int D20 { get; }
        public int Modifier { get; }
        public int Difficulty { get; }
        public int MechanicalScore { get; }
        public string ActionContext { get; }
        public string ModifierBreakdown { get; }
        public string Outcome { get; }
        public string Attempt { get; }
        public string Explanation { get; }
        public string Narrative { get; }
        public string StateChanges { get; }
        public string[] Dialogue { get; }
        /// <summary>대화 페이지 전체를 말하는 화자 이름. NPC 조사처럼 화자가 확정된 경우에만 채워진다.</summary>
        public string DialogueSpeaker { get; }
        public bool NarrativeFallbackUsed { get; }
        public string NarrativeModel { get; }
        public float ActionDuration { get; }
        public string[] LogEntries { get; }

        public TurnPresentationResult(
            int d20,
            int modifier,
            int difficulty,
            int mechanicalScore,
            string actionContext,
            string modifierBreakdown,
            string outcome,
            string attempt,
            string explanation,
            string narrative,
            string stateChanges,
            string[] dialogue,
            bool narrativeFallbackUsed,
            string narrativeModel,
            float actionDuration,
            string[] logEntries,
            string dialogueSpeaker = null)
        {
            D20 = d20;
            Modifier = modifier;
            Difficulty = difficulty;
            MechanicalScore = mechanicalScore;
            ActionContext = actionContext ?? "--";
            ModifierBreakdown = modifierBreakdown ?? "--";
            Outcome = outcome ?? "--";
            Attempt = attempt ?? string.Empty;
            Explanation = explanation ?? string.Empty;
            Narrative = narrative ?? string.Empty;
            StateChanges = stateChanges ?? "상태 변화 없음";
            Dialogue = dialogue ?? Array.Empty<string>();
            DialogueSpeaker = string.IsNullOrWhiteSpace(dialogueSpeaker) ? null : dialogueSpeaker.Trim();
            NarrativeFallbackUsed = narrativeFallbackUsed;
            NarrativeModel = narrativeModel ?? "deterministic";
            ActionDuration = Math.Max(0f, actionDuration);
            LogEntries = logEntries ?? Array.Empty<string>();
        }
    }

    /// <summary>로컬 규칙 엔진의 TurnResponse를 공통 화면 결과로 변환한다.</summary>
    public static class LocalTurnPresentationAdapter
    {
        public static TurnPresentationResult Create(TurnResponse response)
        {
            if (response == null)
                throw new ArgumentNullException(nameof(response));

            string modifierBreakdown = "로컬 능력·상태 합계 " + TurnPresentationText.Signed(response.Modifier);
            string outcome = TurnPresentationText.KoreanOutcome(response.Outcome);
            string stateChanges = TurnPresentationText.StateChangeSummary(response.Events);
            string[] dialogue = BuildNpcDialogue(response, out string dialogueSpeaker);
            var logs = new List<string>
            {
                "D20 " + response.D20 + " · " + outcome + " · " + response.NormalizedAttempt
            };
            for (int i = 0; i < response.Events.Count; i++)
                logs.Add(TurnPresentationText.HumanizeEvent(response.Events[i]));

            return new TurnPresentationResult(
                response.D20,
                response.Modifier,
                response.Difficulty,
                response.MechanicalScore,
                CampaignCatalog.ContextLabel(response.ActionContext),
                modifierBreakdown,
                outcome,
                response.NormalizedAttempt,
                response.OutcomeExplanation + " · " + modifierBreakdown,
                response.Narrative,
                stateChanges,
                dialogue,
                true,
                "deterministic",
                response.ActionContext == ActionContext.Combat ? 0.5f : 0.22f,
                logs.ToArray(),
                dialogueSpeaker);
        }

        private static string[] BuildNpcDialogue(TurnResponse response, out string speaker)
        {
            speaker = null;
            Guid npcId = Guid.Empty;
            bool hasNpcScene = false;
            for (int i = 0; i < response.Events.Count; i++)
            {
                string value = response.Events[i] ?? string.Empty;
                if (!value.StartsWith("NPC_CLUE_REVEALED:", StringComparison.Ordinal) &&
                    !value.StartsWith("NPC_RUMOR_REVEALED:", StringComparison.Ordinal) &&
                    !value.StartsWith("NPC_INVESTIGATION_REFUSED:", StringComparison.Ordinal) &&
                    !value.StartsWith("NPC_INVESTIGATION_REPEAT:", StringComparison.Ordinal)) continue;
                string[] parts = value.Split(':');
                hasNpcScene = parts.Length > 1 && Guid.TryParse(parts[1], out npcId);
                if (hasNpcScene) break;
            }
            if (!hasNpcScene || response.Run == null) return Array.Empty<string>();
            for (int i = 0; i < response.Run.NpcMemories.Count; i++)
            {
                NpcMemoryView npc = response.Run.NpcMemories[i];
                if (npc.EntityId != npcId) continue;
                speaker = npc.NpcName;
                var pages = new List<string>();
                pages.Add(npc.NpcName + " · " + npc.Role + "\n" +
                          (string.IsNullOrWhiteSpace(npc.CurrentConcern)
                              ? "상대는 쉽게 입을 열지 못하고 있다."
                              : "지금 마음에 걸리는 일 · " + npc.CurrentConcern));
                if (!string.IsNullOrWhiteSpace(npc.LatestMemory)) pages.Add(npc.LatestMemory);
                pages.Add("관계 변화 · 신뢰 " + SignedRelationship(npc.Trust) + " / 두려움 " + npc.Fear +
                          (npc.RevealedClues.Count > 0 ? "\n새로 확인한 단서가 인물 기록에 남았습니다." : string.Empty));
                return pages.ToArray();
            }
            return Array.Empty<string>();
        }

        private static string SignedRelationship(int value) => value >= 0 ? "+" + value : value.ToString();
    }

    /// <summary>두 실행 경로가 함께 쓰는 짧은 결과 문자열 규칙이다.</summary>
    internal static class TurnPresentationText
    {
        public static string Signed(int value) => value >= 0 ? "+" + value : value.ToString();

        public static string KoreanOutcome(RuleOutcome outcome)
        {
            switch (outcome)
            {
                case RuleOutcome.CriticalSuccess: return "대성공";
                case RuleOutcome.Success: return "성공";
                case RuleOutcome.PartialSuccess: return "부분 성공";
                case RuleOutcome.CriticalFailure: return "대실패";
                default: return "실패";
            }
        }

        public static string KoreanOutcome(string outcome)
        {
            string value = (outcome ?? string.Empty).ToLowerInvariant();
            if (value.Contains("critical_success")) return "대성공";
            if (value == "success") return "성공";
            if (value.Contains("partial")) return "부분 성공";
            if (value.Contains("critical_failure")) return "대실패";
            if (value == "failure") return "실패";
            return string.IsNullOrWhiteSpace(outcome) ? "--" : outcome;
        }

        public static string HumanizeEvent(string eventCode)
        {
            if (string.IsNullOrWhiteSpace(eventCode)) return string.Empty;
            if (eventCode.StartsWith("PARTIAL_COPY_DRIFT:", StringComparison.Ordinal)) return "복제본에 불안정한 상태 편차가 생김";
            if (eventCode.StartsWith("PARTIAL_DELETE_BACKFLOW:", StringComparison.Ordinal)) return "삭제한 의존성이 역류해 플레이어가 노출됨";
            if (eventCode.StartsWith("PARTIAL_CONNECT_HAZARD:", StringComparison.Ordinal)) return "연결을 따라 위험이 양방향으로 전파됨";
            if (eventCode.StartsWith("PARTIAL_RESTORE_DEFECT:", StringComparison.Ordinal)) return "복구 과정에서 과거 결함도 함께 돌아옴";
            if (eventCode.StartsWith("PARTIAL_UNDO_PARADOX:", StringComparison.Ordinal)) return "상쇄되지 않은 시간 역설이 기술 부채로 남음";
            if (eventCode.StartsWith("PARTIAL_SELECT_ALL_OVERLOAD:", StringComparison.Ordinal)) return "광역 명령 과부하로 집중력을 추가 소모함";
            if (eventCode.StartsWith("PARTIAL_SEARCH_NOISE:", StringComparison.Ordinal)) return "조사 소음으로 플레이어 위치가 노출됨";
            if (eventCode.StartsWith("PARTIAL_INTERACTION_ATTENTION:", StringComparison.Ordinal)) return "불완전한 상호작용이 주변의 주의를 끎";
            if (eventCode.StartsWith("DEBT_BACKFLOW_CLONE_DRIFT:", StringComparison.Ordinal)) return "기술 부채가 복제 드리프트로 역류함";
            if (eventCode.StartsWith("DEBT_BACKFLOW_HOSTILE_SPAWNED:", StringComparison.Ordinal)) return "삭제된 의존성이 적대 프로세스로 재생성됨";
            if (eventCode.StartsWith("DEBT_BACKFLOW_BLOCKED:", StringComparison.Ordinal)) return "의존성 역류가 세계 안정성을 훼손함";
            if (eventCode.StartsWith("DEBT_PARADOX_SURGE:", StringComparison.Ordinal)) return "Undo 역설이 플레이어에게 피해를 줌";
            if (eventCode.StartsWith("DEBT_PARADOX_EXPOSURE:", StringComparison.Ordinal)) return "시간 역설로 플레이어 위치가 노출됨";
            if (eventCode.StartsWith("SUPPLY_PURCHASED:", StringComparison.Ordinal)) return "Gold를 사용해 Focus 보급품을 구매함";
            if (eventCode.StartsWith("SUPPLY_PURCHASE_REJECTED:", StringComparison.Ordinal)) return "Gold 부족 또는 Focus 최대치로 구매하지 못함";
            if (eventCode.StartsWith("MASTERY_RANK_INCREASED:", StringComparison.Ordinal)) return "XP 숙련 등급 상승으로 최대 Focus가 증가함";
            if (eventCode.StartsWith("NPC_PROMISE_MADE:", StringComparison.Ordinal)) return "동료와 ROOT_SYSTEM까지 함께 가기로 약속함";
            if (eventCode.StartsWith("NPC_PROMISE_FULFILLED:", StringComparison.Ordinal)) return "동료와의 약속을 지켜 유대와 신뢰가 상승함";
            if (eventCode.StartsWith("NPC_CLUE_REVEALED:", StringComparison.Ordinal)) return "NPC가 숨겨 둔 증언을 털어놓음 · 신뢰 상승";
            if (eventCode.StartsWith("NPC_RUMOR_REVEALED:", StringComparison.Ordinal)) return "불확실한 증언을 확보함 · 신뢰와 두려움 상승";
            if (eventCode.StartsWith("NPC_INVESTIGATION_REFUSED:", StringComparison.Ordinal)) return "NPC가 답변을 거부함 · 두려움 상승";
            if (eventCode.StartsWith("NPC_INVESTIGATION_REPEAT:", StringComparison.Ordinal)) return "이미 들은 이야기를 다시 확인함 · 새 단서 없음";
            if (eventCode.StartsWith("ENEMY_DEPENDENCY_REVEALED:", StringComparison.Ordinal)) return "적의 의존성을 밝혀 특수 반응을 차단함";
            if (eventCode.StartsWith("CACHE_ENEMY_REPLICATED:", StringComparison.Ordinal)) return "미조사 Cache 적이 인접 타일에 복제됨";
            if (eventCode.StartsWith("SEARCH_REVEALED:", StringComparison.Ordinal)) return string.Empty;
            return eventCode.Replace('_', ' ').Replace(":", " · ");
        }

        public static string StateChangeSummary(IReadOnlyList<string> events)
        {
            if (events == null || events.Count == 0) return "상태 변화 없음";
            var values = new List<string>();
            for (int i = 0; i < events.Count && values.Count < 3; i++)
            {
                string label = HumanizeEvent(events[i]);
                if (!string.IsNullOrWhiteSpace(label)) values.Add("• " + label);
            }
            return values.Count == 0 ? "상태 변화 없음" : string.Join("\n", values);
        }
    }
}
