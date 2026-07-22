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
                ? PlayerFacingFallbackNarrative(turn, run)
                : IsTechnicalNarrative(serverNarrative)
                    ? PlayerFacingFallbackNarrative(turn, run)
                    : serverNarrative.Trim();
            GameApiClient.EventSnapshot[] events = turn.events ?? turn.stateDelta?.events;
            string stateChanges = StateChangeSummary(events);
            string[] authoritativeDialogue = NpcInvestigationDialogue(events);
            string[] generatedDialogue = GeneratedDialogue(turn.narrative, run);
            StorySequencePage[] storySequence = BuildStorySequence(turn.narrative, run, narrative);
            bool movementStory = turn.narrative?.continuesWithMovement == true;
            GameApiClient.NextInterventionSnapshot intervention = movementStory
                ? null
                : turn.narrative?.nextIntervention;
            NarrativeChoiceOption[] choices = movementStory
                ? Array.Empty<NarrativeChoiceOption>()
                : BuildNarrativeChoices(intervention,
                    intervention?.suggestedSkillIds, !IsOpenEncounter(run?.activeEncounter));
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
                    : narrativeEnabled ? generatedDialogue : Array.Empty<string>(),
                turn.narrative == null || turn.narrative.fallbackUsed,
                turn.narrative?.model ?? "deterministic",
                0.28f,
                logs.ToArray(),
                storySequence,
                movementStory
                    ? "첫 전투가 끝나 길이 열렸습니다. WASD로 이동해 다음 사건을 찾으세요."
                    : intervention?.reason,
                intervention?.suggestedSkillIds,
                turn.runtime?.gameplayResult?.fx?.effectId ?? turn.narrative?.elementalEffectId,
                IsValidSealedChoiceSet(intervention) ? intervention.choiceSetId : null,
                choices,
                continuesWithMovement: movementStory);
        }

        public static StorySequencePage[] BuildStorySequence(GameApiClient.NarrativeSnapshot narrative,
            GameApiClient.RunSnapshot run, string fallback)
        {
            if (narrative?.storySequence == null || narrative.storySequence.Length == 0)
                return new[] { new StorySequencePage("MONOLOGUE", run?.protagonistName ?? "넙죽이", fallback) };
            var pages = new List<StorySequencePage>();
            for (int i = 0; i < narrative.storySequence.Length; i++)
            {
                GameApiClient.StorySequenceSnapshot item = narrative.storySequence[i];
                if (item == null || string.IsNullOrWhiteSpace(item.text)) continue;
                string type = (item.type ?? "NARRATION").ToUpperInvariant();
                string speaker = type == "MONOLOGUE"
                    ? run?.protagonistName ?? "넙죽이"
                    : type == "DIALOGUE" ? EntityName(run, item.speakerId) : string.Empty;
                pages.Add(new StorySequencePage(type, speaker, item.text.Trim(), item.actionId, item.speakerId));
            }
            return pages.Count > 0 ? pages.ToArray() : new[] { new StorySequencePage("MONOLOGUE", "넙죽이", fallback) };
        }

        private static string[] GeneratedDialogue(GameApiClient.NarrativeSnapshot narrative,
            GameApiClient.RunSnapshot run)
        {
            if (narrative?.dialogueDetails != null && narrative.dialogueDetails.Length > 0)
            {
                var values = new List<string>();
                for (int i = 0; i < narrative.dialogueDetails.Length; i++)
                {
                    GameApiClient.DialogueSnapshot item = narrative.dialogueDetails[i];
                    if (item == null || string.IsNullOrWhiteSpace(item.line)) continue;
                    string speaker = EntityName(run, item.speakerId);
                    values.Add(string.IsNullOrWhiteSpace(speaker)
                        ? item.line.Trim()
                        : speaker + "\n“" + item.line.Trim() + "”");
                }
                if (values.Count > 0) return values.ToArray();
            }
            return narrative?.dialogue ?? Array.Empty<string>();
        }

        /// <summary>
        /// Converts the server-owned Unity event contract into the existing animation
        /// command type. No narrative text or client-side keyword inference participates.
        /// </summary>
        public static GameApiClient.SceneActionSnapshot[] BuildRuntimeRenderSequence(
            GameApiClient.TurnSnapshot turn, GameApiClient.RunSnapshot run)
        {
            GameApiClient.RuntimeUnitySnapshot unity = turn?.runtime?.unity;
            if (unity?.renderRequired != true || unity.events == null || unity.events.Length == 0)
                return Array.Empty<GameApiClient.SceneActionSnapshot>();

            var actions = new List<GameApiClient.SceneActionSnapshot>();
            for (int i = 0; i < unity.events.Length; i++)
            {
                GameApiClient.RuntimeUnityEventSnapshot item = unity.events[i];
                if (item == null) continue;
                GameApiClient.GameplayResultSnapshot gameplay = item.payload?.gameplayResult;
                GameApiClient.GameplayResultDetailSnapshot detail = gameplay?.result;
                string sourceType = (item.type ?? gameplay?.actionType ?? string.Empty).Trim().ToUpperInvariant();
                string actorId = ResolveRuntimeEntityId(item.actorId, run);
                string targetId = item.targetIds != null && item.targetIds.Length > 0
                    ? ResolveRuntimeEntityId(item.targetIds[0], run)
                    : ResolveRuntimeEntityId(detail?.target?.id, run);
                string animationType = sourceType == "MOVE" ? "MOVE"
                    : sourceType == "ATTACK" || sourceType == "DELETE" || sourceType == "SELECT_ALL" ? "ATTACK"
                    : "ASSIST";
                actions.Add(new GameApiClient.SceneActionSnapshot
                {
                    actionId = "runtime:" + (item.eventId ?? i.ToString()),
                    sequence = item.sequence * 3,
                    type = animationType,
                    actorId = actorId,
                    targetId = targetId,
                    hit = detail?.hit ?? gameplay?.succeeded == true,
                    damage = detail?.damage ?? 0,
                    from = detail?.from,
                    to = detail?.to,
                    text = string.Empty
                });

                if (animationType == "ATTACK" && !string.IsNullOrWhiteSpace(targetId) && (detail?.hit ?? false))
                {
                    actions.Add(new GameApiClient.SceneActionSnapshot
                    {
                        actionId = "runtime:" + (item.eventId ?? i.ToString()) + ":reaction",
                        sequence = item.sequence * 3 + 1,
                        type = detail?.destroyed == true ? "DEFEATED" : "DAMAGE",
                        actorId = targetId,
                        targetId = actorId,
                        hit = true,
                        damage = detail?.damage ?? 0,
                        text = string.Empty
                    });
                }

                GameApiClient.EventSnapshot[] confirmed = item.payload?.confirmedEvents;
                if (confirmed == null) continue;
                for (int j = 0; j < confirmed.Length; j++)
                {
                    GameApiClient.EventSnapshot effect = confirmed[j];
                    if (effect == null || (!string.Equals(effect.type, "entity_activated", StringComparison.OrdinalIgnoreCase) &&
                                           !string.Equals(effect.type, "entity_spawned", StringComparison.OrdinalIgnoreCase)))
                        continue;
                    actions.Add(new GameApiClient.SceneActionSnapshot
                    {
                        actionId = "runtime:" + (item.eventId ?? i.ToString()) + ":spawn:" + j,
                        sequence = item.sequence * 3 + 2 + j,
                        type = "SPAWN",
                        actorId = ResolveRuntimeEntityId(effect.entityId, run),
                        to = effect.position,
                        text = string.Empty
                    });
                }
            }
            actions.Sort((left, right) => left.sequence.CompareTo(right.sequence));
            return actions.ToArray();
        }

        private static string ResolveRuntimeEntityId(string entityId, GameApiClient.RunSnapshot run)
        {
            if (string.Equals(entityId, "PROTAGONIST_NUPJUKYI", StringComparison.OrdinalIgnoreCase))
                return run?.playerEntityId ?? entityId;
            return entityId;
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
            bool movementStory = navigation?.storyEventTriggered == true && !encounterOpened;
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
            StorySequencePage[] storySequence = BuildStorySequence(navigation?.narrative, run, narrative);
            string nextInterventionReason = navigation?.narrative?.nextIntervention?.reason;
            if (string.IsNullOrWhiteSpace(nextInterventionReason))
            {
                nextInterventionReason = encounterOpened
                    ? FirstNonEmpty(run?.activeEncounter?.description,
                        "눈앞의 존재가 반응을 기다리고 있다. 어떤 방식으로 말을 걸거나 개입할까?")
                    : movementStory
                        ? "새로 드러난 흔적을 따라 계속 이동하세요. 다음 탐색 사건은 다시 15~20칸 뒤에 이어집니다."
                        : "주변의 변화를 확인했다. 다음에는 어디로 이동하거나 어떤 방식으로 개입할까?";
            }
            string[] suggestedSkillIds = navigation?.narrative?.nextIntervention?.suggestedSkillIds;
            if (suggestedSkillIds == null || suggestedSkillIds.Length == 0)
                suggestedSkillIds = encounterOpened && run?.activeEncounter?.suggestedSkillIds != null
                    ? run.activeEncounter.suggestedSkillIds
                    : new[] { "SEARCH", "CONNECT" };
            GameApiClient.NextInterventionSnapshot intervention = navigation?.narrative?.nextIntervention;
            NarrativeChoiceOption[] choices = movementStory
                ? Array.Empty<NarrativeChoiceOption>()
                : BuildNarrativeChoices(intervention, suggestedSkillIds, !encounterOpened);

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
                new[] { "안전 탐색 · 비용 " + pathCost + " · 의미 턴 " + campaignTurnBefore + " 유지 · D20 없음" },
                storySequence,
                nextInterventionReason,
                suggestedSkillIds,
                null,
                IsValidSealedChoiceSet(intervention) ? intervention.choiceSetId : null,
                choices,
                continuesWithMovement: movementStory);
        }

        /// <summary>
        /// Converts the untrusted server contract into bounded presentation values. A
        /// response without the new sealed contract is represented by readable legacy
        /// options and an empty ChoiceSetId; the controller then uses the old action flow.
        /// </summary>
        public static NarrativeChoiceOption[] BuildNarrativeChoices(
            GameApiClient.NextInterventionSnapshot intervention,
            string[] legacySuggestedSkillIds,
            bool includeLegacyTravel)
        {
            if (!string.IsNullOrWhiteSpace(intervention?.choiceSetId) && intervention.choices != null)
            {
                var sealedChoices = new List<NarrativeChoiceOption>(4);
                var ids = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < intervention.choices.Length && sealedChoices.Count < 4; i++)
                {
                    GameApiClient.NarrativeChoiceSnapshot item = intervention.choices[i];
                    if (item == null || string.IsNullOrWhiteSpace(item.choiceId) ||
                        string.IsNullOrWhiteSpace(item.text) || !ids.Add(item.choiceId.Trim())) continue;
                    sealedChoices.Add(new NarrativeChoiceOption(item.choiceId.Trim(), item.text.Trim(),
                        item.choiceKind, item.intentTag, item.skillId, item.destinationRef,
                        item.resolutionMode, item.targetEntityId));
                }
                if (sealedChoices.Count >= 2 || IsMandatoryOpeningAttackChoice(sealedChoices))
                    return sealedChoices.ToArray();
            }

            var legacy = new List<NarrativeChoiceOption>(4);
            if (includeLegacyTravel)
                legacy.Add(new NarrativeChoiceOption("legacy-travel", "다른 장소로 이동해 주변을 더 살펴본다.",
                    "TRAVEL", "EXPLORE", destinationRef: "LEGACY_MAP", resolutionMode: "NONE"));
            if (legacySuggestedSkillIds != null)
            {
                for (int i = 0; i < legacySuggestedSkillIds.Length && legacy.Count < 4; i++)
                {
                    string skillId = (legacySuggestedSkillIds[i] ?? string.Empty).Trim().ToUpperInvariant();
                    if (string.IsNullOrEmpty(skillId)) continue;
                    bool duplicate = false;
                    for (int j = 0; j < legacy.Count; j++)
                        if (string.Equals(legacy[j].SkillId, skillId, StringComparison.OrdinalIgnoreCase))
                        {
                            duplicate = true;
                            break;
                        }
                    if (duplicate) continue;
                    legacy.Add(new NarrativeChoiceOption("legacy-skill-" + skillId.ToLowerInvariant(),
                        LegacySkillChoiceText(skillId), "SKILL", "ACT", skillId: skillId,
                        resolutionMode: "D20"));
                }
            }
            return legacy.ToArray();
        }

        public static bool IsValidSealedChoiceSet(GameApiClient.NextInterventionSnapshot intervention)
        {
            if (string.IsNullOrWhiteSpace(intervention?.choiceSetId) || intervention.choices == null ||
                intervention.choices.Length < 1 || intervention.choices.Length > 4) return false;
            if (intervention.choices.Length == 1 &&
                !string.Equals(intervention.choices[0]?.choiceId, "opening.attack", StringComparison.Ordinal))
                return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < intervention.choices.Length; i++)
            {
                GameApiClient.NarrativeChoiceSnapshot item = intervention.choices[i];
                if (item == null || string.IsNullOrWhiteSpace(item.choiceId) ||
                    string.IsNullOrWhiteSpace(item.text) || !ids.Add(item.choiceId.Trim())) return false;
            }
            return true;
        }

        private static bool IsMandatoryOpeningAttackChoice(List<NarrativeChoiceOption> choices)
        {
            return choices.Count == 1 &&
                   string.Equals(choices[0].ChoiceId, "opening.attack", StringComparison.Ordinal) &&
                   string.Equals(choices[0].SkillId, "DELETE", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOpenEncounter(GameApiClient.ActiveEncounterSnapshot encounter)
        {
            return encounter != null && !string.Equals(encounter.status, "RESOLVED", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(encounter.status, "CLOSED", StringComparison.OrdinalIgnoreCase);
        }

        private static string LegacySkillChoiceText(string skillId)
        {
            switch (skillId)
            {
                case "COPY": return "E 복제로 가능성을 하나 더 만들어 본다.";
                case "DELETE": return "R 공격으로 이 흐름을 단호하게 밀어낸다.";
                case "CONNECT": return "C 연결로 상대와 흔적 사이의 관계를 이어 본다.";
                case "RESTORE": return "X 복구로 잃어버린 상태를 되돌려 본다.";
                case "UNDO": return "Z 되돌리기로 직전 선택의 흔적을 상쇄한다.";
                case "SELECT_ALL": return "Ctrl A 전체 개입으로 장면 전체에 손을 댄다.";
                case "SEARCH": return "F 조사로 아직 드러나지 않은 사정을 살펴본다.";
                default: return skillId + "을 사용해 상황에 개입한다.";
            }
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
                case "npc_memory_added": return string.Empty;
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
                case "llm_discovery_event":
                case "llm_skill_event": return !string.IsNullOrWhiteSpace(value.title)
                    ? value.title + (string.IsNullOrWhiteSpace(value.description) ? string.Empty : " · " + value.description)
                    : "현재 장면에서 새로운 사건이 발생함";
                case "ambient_fallback_applied": return value.reward > 0
                    ? "주변 개입 결과 · 보상 " + value.reward
                    : "주변 개입의 결과가 남음";
                case "arc_question_resolved": return !string.IsNullOrWhiteSpace(value.summary)
                    ? "이야기 수렴 · " + value.summary
                    : "이번 구간의 이야기 결과가 정해짐";
                case "turn_committed": return string.Empty;
                default: return string.Empty;
            }
        }

        private static string[] NpcInvestigationDialogue(GameApiClient.EventSnapshot[] events)
        {
            if (events == null) return Array.Empty<string>();
            for (int i = 0; i < events.Length; i++)
            {
                GameApiClient.EventSnapshot item = events[i];
                string type = item?.type ?? string.Empty;
                if (!type.StartsWith("npc_investigation_", StringComparison.OrdinalIgnoreCase) &&
                    !type.StartsWith("npc_clue_", StringComparison.OrdinalIgnoreCase) &&
                    !type.StartsWith("npc_rumor_", StringComparison.OrdinalIgnoreCase)) continue;
                var pages = new List<string>();
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
                    return outcome.Contains("실패")
                        ? "……샅샅이 살펴봤지만 아무것도 찾지 못했어. 괜히 힘만 빠졌군."
                        : "!! " + WithObjectParticle(target) + " 살피다가 흔적을 발견했어. 이건 다음 이야기가 움직이기 시작했다는 뜻이야.";
                case AbilityKind.Delete:
                    return outcome.Contains("실패") ? "……지우지 못했어. 반동만 남았군." : "좋아, " + WithObjectParticle(target) + " 끊어 냈어. 이 변화가 어디까지 번질지 지켜보자.";
                case AbilityKind.SelectAll:
                    return outcome.Contains("실패") ? "……한꺼번에 붙잡으려 했지만 모두 흩어졌어." : "!! 주변의 움직임이 한 번에 멎었어. 잠깐이지만 흐름을 바꿨군.";
                case AbilityKind.Undo: return "……시간이 되감기고 있어. 방금 전의 선택들이 없었던 일처럼 사라졌어.";
                case AbilityKind.Copy: return outcome.Contains("실패") ? "……복제할 만한 흔적을 붙잡지 못했어." : "!! " + target + "의 데이터가 하나 더 생겼어. 원본과는 다른 가능성이 열렸군.";
                case AbilityKind.Connect: return outcome.Contains("실패") ? "……연결이 닿기 전에 끊어졌어." : "이어졌다. 이제 두 존재의 변화가 서로에게 영향을 주겠어.";
                case AbilityKind.Restore: return outcome.Contains("실패") ? "……이 흔적은 아직 되돌릴 수 없어." : "돌아왔어. 사라졌던 " + target + "의 흔적이 다시 이어지고 있어.";
                default: return "……내 개입이 코드리아에 작은 흔적을 남겼어. 다음 장면이 어떻게 변할지 지켜보자.";
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
                case AbilityKind.Copy: return "E 복제";
                case AbilityKind.Delete: return "R 단일 공격";
                case AbilityKind.Connect: return "C 연결";
                case AbilityKind.Restore: return "X 복구";
                case AbilityKind.Undo: return "Z 2턴 역행";
                case AbilityKind.Search: return "F 조사";
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
