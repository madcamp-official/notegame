using System;
using System.Collections.Generic;
using KeyboardWanderer.Gameplay;
using KeyboardWanderer.Presentation;

namespace KeyboardWanderer.Networking
{
    /// <summary>
    /// 서버의 턴·이동 DTO를 화면 공통 결과로 변환한다.
    /// DTO 필드명, 서버 스킬 ID, 이벤트 코드를 해석하는 책임을 Networking 폴더에 한정한다.
    /// </summary>
    public static class ServerTurnPresentationAdapter
    {
        public static TurnPresentationResult FromTurn(
            GameApiClient.TurnSnapshot turn,
            GameApiClient.RunSnapshot run,
            bool narrativeEnabled)
        {
            if (turn == null)
                throw new ArgumentNullException(nameof(turn));

            GameApiClient.DiceSnapshot dice = turn.dice;
            int modifier = 0;
            var modifierParts = new List<string>();
            if (dice?.modifiers != null)
            {
                for (int i = 0; i < dice.modifiers.Length; i++)
                {
                    GameApiClient.DiceModifierSnapshot item = dice.modifiers[i];
                    if (item == null) continue;
                    modifier += item.value;
                    if (modifierParts.Count < 3)
                        modifierParts.Add(ModifierSourceLabel(item.source) + " " +
                                          TurnPresentationText.Signed(item.value));
                }
            }
            if (modifierParts.Count == 0 && dice != null && dice.modifier != 0)
            {
                modifier = dice.modifier;
                modifierParts.Add("기본 수정치 " + TurnPresentationText.Signed(dice.modifier));
            }

            int d20 = dice?.raw ?? 0;
            int difficulty = dice?.difficulty ?? 0;
            int mechanicalScore = dice?.mechanicalScore ?? turn.mechanicalScore;
            string modifierBreakdown = modifierParts.Count == 0
                ? "수정치 없음"
                : string.Join(", ", modifierParts);
            string actionContext = ActionContextLabel(turn.actionContext);
            string outcome = TurnPresentationText.KoreanOutcome(turn.outcome);
            string attempt = PlayerFacingAttempt(turn, run);
            string roll = d20 > 0
                ? "D20 " + d20 + " " + TurnPresentationText.Signed(modifier) + " / 난이도 " + difficulty
                : "주사위 판정 없음";
            string cost = turn.consequenceBudget > 0
                ? " · 결과에 따른 합병증 " + turn.consequenceBudget
                : string.Empty;
            string explanation = "판정 · " + actionContext + " · " + roll + " · " + modifierBreakdown + cost;
            string serverNarrative = !string.IsNullOrWhiteSpace(turn.narrative?.body)
                ? turn.narrative.body
                : turn.narrative?.summary;
            string narrative = !narrativeEnabled
                ? "생성형 장면·대사 표시가 꺼져 있습니다. 권위 규칙 이벤트와 세계 기억은 그대로 적용되었습니다."
                : IsTechnicalNarrative(serverNarrative)
                    ? PlayerFacingFallbackNarrative(turn, run)
                    : serverNarrative.Trim();
            GameApiClient.EventSnapshot[] events = turn.events ?? turn.stateDelta?.events;
            string stateChanges = StateChangeSummary(events);
            string[] authoritativeDialogue = NpcInvestigationDialogue(events, out string dialogueSpeaker);
            var logs = new List<string>
            {
                "D20 " + d20 + TurnPresentationText.Signed(modifier) + " vs " + difficulty + " · " + outcome,
                "실제 시도 · " + attempt
            };
            if (events != null)
                for (int i = 0; i < events.Length; i++)
                {
                    string entry = HumanizeEvent(events[i]);
                    if (!string.IsNullOrWhiteSpace(entry)) logs.Add(entry);
                }

            return new TurnPresentationResult(
                d20,
                modifier,
                difficulty,
                mechanicalScore,
                actionContext,
                modifierBreakdown,
                outcome,
                attempt,
                explanation,
                narrative,
                stateChanges,
                authoritativeDialogue.Length > 0
                    ? authoritativeDialogue
                    : narrativeEnabled ? turn.narrative?.dialogue ?? Array.Empty<string>() : Array.Empty<string>(),
                turn.narrative == null || turn.narrative.fallbackUsed,
                turn.narrative?.model ?? "deterministic",
                0.28f,
                logs.ToArray(),
                authoritativeDialogue.Length > 0 ? dialogueSpeaker : null);
        }

        public static TurnPresentationResult FromNavigation(
            GameApiClient.NavigationSnapshot navigation,
            GameApiClient.RunSnapshot run,
            int campaignTurnBefore,
            int destinationX,
            int destinationY,
            bool encounterOpened,
            string encounterReason,
            bool invariantHeld,
            bool narrativeEnabled,
            string playerName)
        {
            string outcome = encounterOpened ? "사건 발견" : invariantHeld ? "안전 이동" : "이동 상태 확인";
            string attempt = "고정 월드의 (" + destinationX + ", " + destinationY + ")까지 안전 경로로 이동";
            string explanation = encounterOpened
                ? "서버가 안전 구간 이동 뒤 사건을 열었습니다. 아직 D20과 의미 있는 턴은 쓰지 않았으며 다음 사건 행동이 이를 소비합니다."
                : invariantHeld
                    ? "서버 /travel이 위치와 탐색 시간만 갱신했습니다. D20과 의미 있는 캠페인 턴은 소비하지 않았습니다."
                    : "서버 이동 결과를 다시 동기화했습니다. 캠페인 턴 또는 레이아웃 불변식 표시를 확인하세요.";
            string actor = FirstNonEmpty(playerName, run?.protagonistName, "넙죽이");
            string narrative = narrativeEnabled && !string.IsNullOrWhiteSpace(navigation?.narrative?.body)
                ? navigation.narrative.body
                : encounterOpened
                    ? actor + "는(은) 안전 구간 끝에서 " + EncounterReasonLabel(encounterReason) +
                      " 사건과 마주쳤다. 이제 배치·전투·조사·협상 중 하나로 해결해야 한다."
                    : actor + "는(은) 이미 생성된 월드 안에서 안전 경로를 따라 이동했다. 사건이 시작되기 전까지 세계 지형은 바뀌지 않는다.";
            string stateChanges = navigation?.events != null && navigation.events.Length > 0
                ? StateChangeSummary(navigation.events)
                : encounterOpened
                    ? "위치: 안전 지점까지 이동 · 사건 활성화 · 캠페인 턴 유지"
                    : "위치와 이동 시간만 변경 · 캠페인 턴 유지 · D20 없음";
            int pathCost = navigation?.pathCost ?? 0;

            return new TurnPresentationResult(
                0,
                0,
                0,
                0,
                "안전 이동",
                "탐색 이동에는 판정 수정치 없음",
                outcome,
                attempt,
                explanation,
                narrative,
                stateChanges,
                narrativeEnabled ? navigation?.narrative?.dialogue ?? Array.Empty<string>() : Array.Empty<string>(),
                navigation?.narrative == null || navigation.narrative.fallbackUsed,
                navigation?.narrative?.model ?? "deterministic",
                0f,
                new[] { "안전 탐색 · 비용 " + pathCost + " · 의미 턴 " + campaignTurnBefore + " 유지 · D20 없음" });
        }

        private static string StateChangeSummary(GameApiClient.EventSnapshot[] events)
        {
            if (events == null || events.Length == 0) return "상태 변화 없음";
            var values = new List<string>();
            for (int i = 0; i < events.Length && values.Count < 3; i++)
            {
                string label = HumanizeEvent(events[i]);
                if (!string.IsNullOrWhiteSpace(label)) values.Add("• " + label);
            }
            return values.Count == 0 ? "상태 변화 없음" : string.Join("\n", values);
        }

        private static string HumanizeEvent(GameApiClient.EventSnapshot value)
        {
            if (value == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(value.text)) return value.text;
            switch ((value.type ?? string.Empty).ToLowerInvariant())
            {
                case "entity_investigated": return string.Empty;
                case "search_completed": return "조사를 완료함";
                case "resource_changed": return value.delta == 0 ? "자원을 사용함" : "자원 " + TurnPresentationText.Signed(value.delta);
                case "health_changed": return value.delta < 0 ? "대상에게 " + (-value.delta) + " 피해" : "체력 " + TurnPresentationText.Signed(value.delta);
                case "area_attack_resolved": return "주변 적 광범위 공격 완료";
                case "turn_compensated": return "이전 행동의 변화를 상쇄함";
                case "undo_compensation_completed": return "최근 2개 행동의 보상형 Undo 완료";
                case "turn_counter_rewound": return "턴 카운터를 2턴 되돌림";
                case "entity_restored":
                case "entity_state_restored": return "대상 상태 복구 완료";
                case "entity_removed": return "대상 제거 완료";
                case "partial_copy_drift": return "복제본에 불안정한 상태 편차가 생김";
                case "partial_delete_backflow": return "삭제한 의존성이 역류해 플레이어가 노출됨";
                case "partial_connect_hazard": return "연결을 따라 위험이 양방향으로 전파됨";
                case "partial_restore_defect": return "복구 과정에서 과거 결함도 함께 돌아옴";
                case "partial_undo_paradox": return "상쇄되지 않은 시간 역설이 기술 부채로 남음";
                case "partial_select_all_overload": return "광역 명령 과부하로 집중력을 추가 소모함";
                case "partial_search_noise": return "조사 소음으로 플레이어 위치가 노출됨";
                case "partial_interaction_attention": return "불완전한 상호작용이 주변의 주의를 끎";
                case "debt_backflow_clone_drift": return "기술 부채가 복제 드리프트로 역류함";
                case "debt_backflow_hostile_spawned": return "삭제된 의존성이 적대 프로세스로 재생성됨";
                case "debt_backflow_blocked": return "의존성 역류가 세계 안정성을 훼손함";
                case "debt_paradox_surge": return "Undo 역설이 플레이어에게 피해와 노출을 남김";
                case "supply_purchased": return "Gold를 사용해 Focus 보급품을 구매함";
                case "supply_purchase_rejected": return "Gold 부족 또는 Focus 최대치로 구매하지 못함";
                case "mastery_rank_increased": return "XP 숙련 등급 상승으로 최대 Focus가 증가함";
                case "defeat_reward_granted": return "처치 보상으로 XP와 Gold를 획득함";
                case "npc_promise_made": return "동료와 ROOT_SYSTEM까지 함께 가기로 약속함";
                case "npc_promise_fulfilled": return "동료와의 약속을 지켜 유대와 신뢰가 상승함";
                case "npc_clue_revealed": return !string.IsNullOrWhiteSpace(value.clueTitle)
                    ? "새 단서 · " + value.clueTitle
                    : "NPC의 증언을 확보함";
                case "npc_rumor_revealed": return !string.IsNullOrWhiteSpace(value.clueTitle)
                    ? "확인이 필요한 단서 · " + value.clueTitle
                    : "확인이 필요한 증언을 확보함";
                case "npc_investigation_refused": return "NPC가 답변을 거부함 · 두려움 상승";
                case "npc_investigation_repeat": return "이미 들은 이야기를 다시 확인함 · 새 단서 없음";
                case "companion_support_applied": return "가까운 동료의 지원으로 판정 보정 +1";
                case "cache_enemy_replicated": return "미조사 Cache 적이 인접 타일에 복제됨";
                case "turn_committed": return string.Empty;
                default: return TurnPresentationText.HumanizeEvent(value.type);
            }
        }

        private static string[] NpcInvestigationDialogue(GameApiClient.EventSnapshot[] events, out string speaker)
        {
            speaker = null;
            if (events == null) return Array.Empty<string>();
            for (int i = 0; i < events.Length; i++)
            {
                GameApiClient.EventSnapshot item = events[i];
                string type = item?.type ?? string.Empty;
                if (!type.StartsWith("npc_investigation_", StringComparison.OrdinalIgnoreCase) &&
                    !type.StartsWith("npc_clue_", StringComparison.OrdinalIgnoreCase) &&
                    !type.StartsWith("npc_rumor_", StringComparison.OrdinalIgnoreCase)) continue;
                var pages = new List<string>();
                speaker = FirstNonEmpty(item.npcName, "NPC");
                pages.Add(FirstNonEmpty(item.npcName, "NPC") + "\n지금 마음에 걸리는 일 · " +
                          FirstNonEmpty(item.concern, "쉽게 말할 수 없는 문제가 있다"));
                if (!string.IsNullOrWhiteSpace(item.line)) pages.Add(item.line.Trim());
                if (!string.IsNullOrWhiteSpace(item.clueContent))
                    pages.Add("새 단서 · " + FirstNonEmpty(item.clueTitle, "이름 없는 증언") + "\n" + item.clueContent.Trim());
                if (!string.IsNullOrWhiteSpace(item.clueMeaning) || !string.IsNullOrWhiteSpace(item.storyConnection))
                    pages.Add("이 단서가 뜻하는 것\n" + FirstNonEmpty(item.clueMeaning, item.storyConnection) +
                              (!string.IsNullOrWhiteSpace(item.storyConnection) && item.storyConnection != item.clueMeaning
                                  ? "\n\n이야기 연결 · " + item.storyConnection.Trim()
                                  : string.Empty));
                if (!string.IsNullOrWhiteSpace(item.nextObjective))
                    pages.Add("다음 조사\n" + item.nextObjective.Trim());
                else
                    pages.Add("관계 변화 · 신뢰 " + TurnPresentationText.Signed(item.trust) + " / 두려움 " + item.fear +
                              "\n지금은 새로 확인된 단서가 없습니다.");
                return pages.ToArray();
            }
            return Array.Empty<string>();
        }

        private static string PlayerFacingAttempt(GameApiClient.TurnSnapshot turn, GameApiClient.RunSnapshot run)
        {
            string skill = AbilityLabel(AbilityFromServerName(TurnSkillId(turn)));
            string first = EntityName(run, TurnTargetId(turn, false));
            string second = EntityName(run, TurnTargetId(turn, true));
            if (!string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second))
                return first + "와(과) " + second + "에 " + skill + " 사용";
            if (!string.IsNullOrWhiteSpace(first))
                return first + "에 " + skill + " 사용";
            return skill + " 사용";
        }

        private static string PlayerFacingFallbackNarrative(
            GameApiClient.TurnSnapshot turn, GameApiClient.RunSnapshot run)
        {
            AbilityKind ability = AbilityFromServerName(TurnSkillId(turn));
            string target = EntityName(run, TurnTargetId(turn, false));
            if (string.IsNullOrWhiteSpace(target)) target = "주변 상황";
            string outcome = TurnPresentationText.KoreanOutcome(turn?.outcome);
            switch (ability)
            {
                case AbilityKind.Search:
                    return "넙죽이는 " + WithObjectParticle(target) + " 자세히 조사했다. " + outcome +
                           "으로 숨겨진 단서가 세계 기록에 남았다.";
                case AbilityKind.Delete:
                    return "넙죽이는 " + target + "에게 단일 공격을 가했다. 전투 판정은 " + outcome + "으로 끝났다.";
                case AbilityKind.SelectAll:
                    return "넙죽이는 주변의 적들을 한꺼번에 공격했다. 광범위 공격은 " + outcome + "으로 끝났다.";
                case AbilityKind.Undo: return "넙죽이가 시간을 되감자 최근 두 턴의 변화가 차례로 사라졌다.";
                case AbilityKind.Copy: return "넙죽이는 " + target + "의 복제본을 선택한 위치에 배치했다.";
                case AbilityKind.Connect: return "넙죽이는 선택한 두 대상 사이에 새로운 연결을 만들었다.";
                case AbilityKind.Restore: return "넙죽이는 " + target + "의 이전 상태를 복구했다.";
                default: return "선택한 행동의 결과가 코드리아의 현재 상태에 반영되었다.";
            }
        }

        private static string EntityName(GameApiClient.RunSnapshot run, string id)
        {
            if (run?.entities == null || string.IsNullOrWhiteSpace(id)) return string.Empty;
            for (int i = 0; i < run.entities.Length; i++)
            {
                GameApiClient.EntitySnapshot entity = run.entities[i];
                if (entity != null && string.Equals(entity.id, id, StringComparison.OrdinalIgnoreCase))
                    return FirstNonEmpty(entity.name, entity.assetId, "선택한 대상");
            }
            return string.Empty;
        }

        private static string TurnSkillId(GameApiClient.TurnSnapshot turn) =>
            FirstNonEmpty(turn?.skillId, turn?.request?.skillId, turn?.request?.ability);

        private static string TurnTargetId(GameApiClient.TurnSnapshot turn, bool secondary)
        {
            if (secondary)
                return FirstNonEmpty(turn?.targetIds != null && turn.targetIds.Length > 1 ? turn.targetIds[1] : null,
                    turn?.request?.secondaryTargetEntityId);
            return FirstNonEmpty(turn?.targetIds != null && turn.targetIds.Length > 0 ? turn.targetIds[0] : null,
                turn?.request?.targetEntityId);
        }

        private static string ActionContextLabel(string context)
        {
            switch ((context ?? string.Empty).ToUpperInvariant())
            {
                case "COMBAT": return "전투";
                case "INVESTIGATION": return "조사";
                case "NEGOTIATION": return "협상";
                case "DEPLOYMENT": return "배치";
                case "SAFE_TRAVEL":
                case "MOVE": return "안전 이동";
                default: return string.IsNullOrWhiteSpace(context) ? "--" : context;
            }
        }

        private static string ModifierSourceLabel(string source)
        {
            switch ((source ?? string.Empty).ToLowerInvariant())
            {
                case "keyboard_affinity": return "키보드 친화도";
                case "focus": return "집중력";
                case "admin_access": return "관리자 권한";
                case "special_skill": return "특수 스킬";
                case "encounter": return "사건 효과";
                default: return string.IsNullOrWhiteSpace(source) ? "수정치" : source.Replace('_', ' ');
            }
        }

        private static string AbilityLabel(AbilityKind ability)
        {
            switch (ability)
            {
                case AbilityKind.Copy: return "Ctrl C 복제";
                case AbilityKind.Delete: return "Delete 단일 공격";
                case AbilityKind.Connect: return "Ctrl K 연결";
                case AbilityKind.Restore: return "Ctrl R 복구";
                case AbilityKind.Undo: return "Ctrl Z 2턴 역행";
                case AbilityKind.Search: return "Ctrl F 조사";
                case AbilityKind.SelectAll: return "Ctrl A 범위 공격";
                case AbilityKind.Interact: return "상호작용";
                default: return "W 이동";
            }
        }

        private static AbilityKind AbilityFromServerName(string value)
        {
            switch ((value ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "COPY": return AbilityKind.Copy;
                case "DELETE": return AbilityKind.Delete;
                case "CONNECT": return AbilityKind.Connect;
                case "RESTORE": return AbilityKind.Restore;
                case "UNDO": return AbilityKind.Undo;
                case "INTERACT": return AbilityKind.Interact;
                case "SEARCH": return AbilityKind.Search;
                case "SELECT_ALL": return AbilityKind.SelectAll;
                default: return AbilityKind.Move;
            }
        }

        private static bool IsTechnicalNarrative(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            string text = value.Trim();
            return text.StartsWith("Stated goal:", StringComparison.OrdinalIgnoreCase) ||
                   text.IndexOf("Server-authorized execution:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("Intent fit:", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string WithObjectParticle(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "대상을";
            string text = value.Trim();
            char last = text[text.Length - 1];
            if (last >= '\uAC00' && last <= '\uD7A3')
                return text + ((last - '\uAC00') % 28 == 0 ? "를" : "을");
            return text + "을(를)";
        }

        private static string EncounterReasonLabel(string reason)
        {
            switch ((reason ?? string.Empty).ToLowerInvariant())
            {
                case "hostile_proximity": return "적대 개체";
                case "hazardous_tile": return "위험 지형";
                case "unknown_off_route": return "미개척 경로";
                case "unsafe_or_blocked_route": return "차단된 경로";
                default: return "예상치 못한 사건";
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null) return null;
            for (int i = 0; i < values.Length; i++)
                if (!string.IsNullOrWhiteSpace(values[i])) return values[i].Trim();
            return null;
        }
    }
}
