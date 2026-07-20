using System;
using System.Collections.Generic;
using KeyboardWanderer.Core;

namespace KeyboardWanderer.Gameplay
{
    public sealed class CampaignConstraints
    {
        public CampaignAct Act { get; }
        public CampaignPhase Phase { get; }
        public int RemainingTurns { get; }
        public int BeatDeadline { get; }
        public bool MustAdvanceMainPlot { get; }
        public bool AllowNewLongQuest { get; }
        public bool AllowUnrelatedNpcOrRegion { get; }
        public bool ForceEnding { get; }

        public CampaignConstraints(CampaignAct act, CampaignPhase phase, int remainingTurns, int beatDeadline,
            bool mustAdvanceMainPlot, bool allowNewLongQuest, bool allowUnrelatedNpcOrRegion, bool forceEnding)
        {
            Act = act;
            Phase = phase;
            RemainingTurns = remainingTurns;
            BeatDeadline = beatDeadline;
            MustAdvanceMainPlot = mustAdvanceMainPlot;
            AllowNewLongQuest = allowNewLongQuest;
            AllowUnrelatedNpcOrRegion = allowUnrelatedNpcOrRegion;
            ForceEnding = forceEnding;
        }
    }

    public static class CampaignDirector
    {
        private static readonly AbilityKind[] InvestigationAbilities =
            { AbilityKind.Search };
        private static readonly AbilityKind[] AccessOneAbilities =
            { AbilityKind.Connect, AbilityKind.Search };
        private static readonly AbilityKind[] AccessTwoAbilities =
            { AbilityKind.Delete, AbilityKind.Connect };
        private static readonly AbilityKind[] AccessThreeAbilities =
            { AbilityKind.Restore, AbilityKind.Search };
        private static readonly AbilityKind[] DeploymentAbilities =
            { AbilityKind.Copy, AbilityKind.Connect, AbilityKind.Restore, AbilityKind.Undo };
        private static readonly AbilityKind[] FinaleAbilities =
            { AbilityKind.Connect, AbilityKind.Delete };

        public static CampaignConstraints Evaluate(int meaningfulTurns, int turnLimit)
        {
            if (turnLimit < 30 || turnLimit > 50)
                throw new ArgumentOutOfRangeException(nameof(turnLimit), "Campaign turn limit must be between 30 and 50.");
            meaningfulTurns = Math.Max(0, meaningfulTurns);
            int remaining = Math.Max(0, turnLimit - meaningfulTurns);
            int[] starts =
            {
                0,
                Math.Max(3, (int)Math.Round(turnLimit * 0.10d)),
                Math.Max(5, (int)Math.Round(turnLimit * 0.18d)),
                Math.Max(8, (int)Math.Round(turnLimit * 0.30d)),
                Math.Max(12, (int)Math.Round(turnLimit * 0.45d)),
                Math.Max(17, (int)Math.Round(turnLimit * 0.60d)),
                Math.Max(21, (int)Math.Round(turnLimit * 0.72d)),
                Math.Max(25, (int)Math.Round(turnLimit * 0.84d)),
                Math.Max(28, (int)Math.Round(turnLimit * 0.94d))
            };
            CampaignPhase[] phases =
            {
                CampaignPhase.ArrivalAndKeyboardAwakening,
                CampaignPhase.FirstRegionProblem,
                CampaignPhase.AdminAccessOne,
                CampaignPhase.AdminAccessTwo,
                CampaignPhase.InternalFailureTruth,
                CampaignPhase.TechnicalDebtBackflow,
                CampaignPhase.AdminAccessThree,
                CampaignPhase.RootSystemEntry,
                CampaignPhase.FinalDeployment
            };
            int phaseIndex = 0;
            for (int i = 1; i < starts.Length; i++)
                if (meaningfulTurns >= starts[i]) phaseIndex = i;
            CampaignPhase phase = phases[phaseIndex];
            int deadline = phaseIndex + 1 < starts.Length ? starts[phaseIndex + 1] - 1 : turnLimit;
            CampaignAct act = phaseIndex == 0 ? CampaignAct.Introduction
                : phaseIndex <= 3 ? CampaignAct.Exploration
                : phaseIndex <= 5 ? CampaignAct.Pressure
                : phaseIndex <= 7 ? CampaignAct.Convergence
                : CampaignAct.Ending;
            bool nearDeadline = deadline - meaningfulTurns <= 2;
            return new CampaignConstraints(act, phase, remaining, deadline, nearDeadline || remaining <= 10,
                remaining > 8 && !nearDeadline, remaining > 3 && !nearDeadline, meaningfulTurns >= turnLimit);
        }

        public static void Install(RunState state, CampaignBlueprint blueprint, IList<EntityState> npcs)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (blueprint == null) throw new ArgumentNullException(nameof(blueprint));
            state.CampaignId = blueprint.Id;
            state.CampaignTitle = blueprint.Title;
            state.CampaignPremise = blueprint.Premise;
            state.CurrentBeatIndex = 0;
            state.RequiredBeats.Clear();
            for (int i = 0; i < blueprint.Beats.Count; i++)
            {
                CampaignBeatState beat = blueprint.Beats[i].Clone();
                state.RequiredBeats.Add(beat);
                state.AddOpenLoop(beat.Objective);
            }
            state.EndingCandidates.Clear();
            for (int i = 0; i < blueprint.Endings.Count; i++) state.EndingCandidates.Add(blueprint.Endings[i].Clone());
            state.CanonicalFacts.Clear();
            for (int i = 0; i < blueprint.InitialFacts.Count; i++) state.AddCanonicalFact(blueprint.InitialFacts[i]);
            state.Rumors.Clear();
            for (int i = 0; i < blueprint.InitialRumors.Count; i++) state.AddRumor(blueprint.InitialRumors[i]);
            state.ForbiddenEvents.Clear();
            state.ForbiddenEvents.AddRange(blueprint.ForbiddenEvents);
            state.NpcStories.Clear();
            if (npcs != null)
            {
                for (int i = 0; i < npcs.Count; i++)
                {
                    string role = blueprint.NpcRoles[i % blueprint.NpcRoles.Count];
                    var story = new NpcStoryState(npcs[i].EntityId, npcs[i].DisplayName, role);
                    ConfigureNpcStory(story, state.WorldSeed);
                    story.Remember("런 시작 전 확정된 장소에서 " + role + " 역할을 맡았다.");
                    state.NpcStories.Add(story);
                }
            }
            UpdateEndingEligibility(state);
        }

        private static void ConfigureNpcStory(NpcStoryState story, long worldSeed)
        {
            int variant = (int)((ulong)(worldSeed ^ story.EntityId.GetHashCode()) % 3u);
            string[] motivations = { "사라진 동료의 행방을 확인하는 것", "붕괴 전 기록을 안전한 곳에 남기는 것", "자신이 지키는 구역의 주민을 대피시키는 것" };
            string[] concerns = { "누군가 통제 기록을 고쳐 쓰고 있다", "최근 같은 오류가 의도적으로 반복되고 있다", "관리자 권한을 흉내 내는 신호가 돌아다닌다" };
            string[] secrets = { "붕괴 직전 관리자 통로에서 낯선 접속 흔적을 보았다", "삭제된 기록 조각에 내부 통제 시스템의 서명이 남아 있었다", "안전하다고 알려진 경로가 사실은 ROOT_SYSTEM으로 이어진다" };
            story.Motivation = motivations[variant];
            story.CurrentConcern = concerns[variant];
            story.Secret = secrets[variant];
        }

        public static void ProcessCommittedTurn(RunState state, TurnRequest request, ActionContext actionContext,
            RuleOutcome outcome, int turnNo, List<string> events)
        {
            if (state == null || request == null || events == null) return;
            bool applied = outcome == RuleOutcome.PartialSuccess || outcome == RuleOutcome.Success ||
                           outcome == RuleOutcome.CriticalSuccess;
            CampaignBeatState beat = state.CurrentBeat;
            if (beat != null && applied && IsCampaignAction(actionContext) &&
                AbilityAllowedForBeat(beat, request.Ability, actionContext) &&
                IsRequiredCampaignRegion(state, beat) && HasDesignatedEvidence(state, beat, request, events))
                CompleteBeat(state, beat, request, actionContext, turnNo, events);

            ApplyMetricDelta(state, request, actionContext, outcome, turnNo, events);
            ProcessNpcPromises(state, request, turnNo, events);
            TriggerTechnicalDebtConsequences(state, turnNo, events);
            state.TurnPressure = RunState.ClampMetric((int)Math.Round(turnNo * 100d / Math.Max(1, state.TurnLimit)));
            state.QuestStage = state.CurrentBeatIndex;
            UpdateEndingEligibility(state);
        }

        private static void ProcessNpcPromises(RunState state, TurnRequest request, int turnNo,
            List<string> events)
        {
            if (request.Ability == AbilityKind.Connect)
            {
                RecordNpcPromise(state, request.TargetEntityId, turnNo, events);
                RecordNpcPromise(state, request.SecondaryTargetEntityId, turnNo, events);
            }
            if (!state.Spatial.TryGetEntity(state.PlayerEntityId, out EntityState player) ||
                state.Region.AreaAt(player.Position)?.CampaignRole != CampaignCatalog.RootSystemAxis ||
                !CanEnterRootSystem(state)) return;
            for (int i = 0; i < state.NpcStories.Count; i++)
            {
                NpcStoryState story = state.NpcStories[i];
                bool made = story.Memories.Exists(memory => memory.StartsWith("[약속]", StringComparison.Ordinal));
                bool fulfilled = story.Memories.Exists(memory => memory.StartsWith("[약속 이행]", StringComparison.Ordinal));
                if (!made || fulfilled) continue;
                story.Affinity += 5;
                story.Remember("[약속 이행] 넙죽이와 함께 ROOT_SYSTEM에 도달했다.");
                state.CompanionBond = RunState.ClampMetric(state.CompanionBond + 10);
                state.PublicTrust = RunState.ClampMetric(state.PublicTrust + 2);
                events.Add("NPC_PROMISE_FULFILLED:" + story.EntityId + ":bond=+10:trust=+2");
            }
        }

        private static void RecordNpcPromise(RunState state, Guid? entityId, int turnNo, List<string> events)
        {
            if (!entityId.HasValue) return;
            NpcStoryState story = state.NpcStories.Find(value => value.EntityId == entityId.Value);
            if (story == null || story.Memories.Exists(memory => memory.StartsWith("[약속]", StringComparison.Ordinal))) return;
            story.Remember("[약속] 넙죽이와 함께 ROOT_SYSTEM까지 동행하기로 했다.");
            events.Add("NPC_PROMISE_MADE:" + story.EntityId + ":turn=" + turnNo + ":support=+1");
        }

        private static void TriggerTechnicalDebtConsequences(RunState state, int turnNo, List<string> events)
        {
            int[] thresholds = { 25, 50, 75 };
            for (int i = 0; i < thresholds.Length; i++)
            {
                int threshold = thresholds[i];
                if (state.TechnicalDebt < threshold || HasTriggeredDebtThreshold(state, threshold)) continue;
                MarkDebtThresholdTriggered(state, threshold, turnNo);
                if (threshold == 25) TriggerCloneDrift(state, events);
                else if (threshold == 50) TriggerDependencyBackflow(state, turnNo, events);
                else TriggerParadoxSurge(state, events);
            }
        }

        private static bool HasTriggeredDebtThreshold(RunState state, int threshold)
        {
            string marker = "DEBT_THRESHOLD_TRIGGERED_" + threshold;
            for (int i = 0; i < state.TechnicalDebtEntries.Count; i++)
                if (string.Equals(state.TechnicalDebtEntries[i].DeferredConsequenceType, marker,
                    StringComparison.Ordinal)) return true;
            return false;
        }

        private static void MarkDebtThresholdTriggered(RunState state, int threshold, int turnNo)
        {
            state.TechnicalDebtEntries.Add(new TechnicalDebtEntry("debt-threshold-" + threshold, turnNo,
                AbilityKind.Undo, "SYSTEM", "WORLD_CODRIA", false, 0,
                "DEBT_THRESHOLD_TRIGGERED_" + threshold) { ResolvedTurn = turnNo });
        }

        private static void TriggerCloneDrift(RunState state, List<string> events)
        {
            EntityState clone = null;
            foreach (EntityState entity in state.Spatial.Entities)
                if (entity.IsActive && entity.DisplayName.EndsWith(" Copy", StringComparison.Ordinal) &&
                    (clone == null || entity.EntityId.CompareTo(clone.EntityId) < 0)) clone = entity;
            if (clone != null)
            {
                state.Spatial.TryDamage(clone.EntityId, 1, out int health, out bool disabled, out _);
                events.Add("DEBT_BACKFLOW_CLONE_DRIFT:" + clone.EntityId + ":hp=" + health +
                           ":disabled=" + disabled);
            }
            else
            {
                state.WorldStability = RunState.ClampMetric(state.WorldStability - 2);
                events.Add("DEBT_BACKFLOW_CLONE_DRIFT:world-stability=-2:no-clone");
            }
        }

        private static void TriggerDependencyBackflow(RunState state, int turnNo, List<string> events)
        {
            if (!state.Spatial.TryGetEntity(state.PlayerEntityId, out EntityState player)) return;
            GridCoord[] directions =
            {
                new GridCoord(1, 0), new GridCoord(0, 1), new GridCoord(-1, 0), new GridCoord(0, -1)
            };
            for (int i = 0; i < directions.Length; i++)
            {
                var position = new GridCoord(player.Position.X + directions[i].X,
                    player.Position.Y + directions[i].Y);
                if (!state.Region.IsWalkable(position) ||
                    state.Spatial.IsBlockingOccupied(state.Region.RegionId, position, player.Layer)) continue;
                Guid id = DeterministicGuid.Create(state.RunId + ":debt-backflow:" + turnNo);
                var enemy = new EntityState(id, EntityKind.Enemy, "enemy.dependency-backflow",
                    "Dependency Backflow", true, false, false, true, 3, state.Region.RegionId,
                    position, player.Layer);
                if (state.Spatial.Register(enemy, out _))
                {
                    events.Add("DEBT_BACKFLOW_HOSTILE_SPAWNED:" + id + ":position=" + position);
                    return;
                }
            }
            state.WorldStability = RunState.ClampMetric(state.WorldStability - 5);
            events.Add("DEBT_BACKFLOW_BLOCKED:world-stability=-5");
        }

        private static void TriggerParadoxSurge(RunState state, List<string> events)
        {
            if (state.Spatial.TryGetEntity(state.PlayerEntityId, out EntityState player) && player.Health > 1)
            {
                int damage = Math.Min(2, player.Health - 1);
                state.Spatial.TryDamage(player.EntityId, damage, out int health, out _, out _);
                events.Add("DEBT_PARADOX_SURGE:player-damage=" + damage + ":hp=" + health);
            }
            state.IsExposed = true;
            events.Add("DEBT_PARADOX_EXPOSURE:player=exposed");
        }

        public static string SelectEnding(RunState state)
        {
            IReadOnlyList<EndingConditionReport> reports = EvaluateEndingBoard(state);
            string[] priority =
            {
                "ENDING_REWEAVE_TOGETHER", "ENDING_OPEN_FRONTIER", "ENDING_KEEP_THE_PROMISE",
                "ENDING_CUT_THE_CYCLE", "ENDING_PRESERVE_THE_SCARS", "ENDING_WALK_BETWEEN_WORLDS"
            };
            for (int p = 0; p < priority.Length; p++)
                for (int i = 0; i < reports.Count; i++)
                    if (reports[i].Code == priority[p] && reports[i].IsEligible) return reports[i].Code;
            return CampaignCatalog.FallbackEndingCode;
        }

        public static IReadOnlyList<EndingConditionReport> EvaluateEndingBoard(RunState state)
        {
            var reports = new List<EndingConditionReport>();
            if (state == null) return reports;
            bool threatActive = HasActiveAsset(state, "finale.threat");
            bool freedomActive = HasActiveAsset(state, "finale.freedom");
            AddEndingReport(reports, state, "ENDING_REWEAVE_TOGETHER",
                Condition("닻—수호 연결", HasConnection(state, "finale.anchor", "finale.safeguard")),
                Condition("플레이어—기억 연결", HasPlayerConnectionToAsset(state, "finale.memory")),
                Condition("위협 비활성", !threatActive), Condition("자유 핵과 연결하지 않음", !HasPlayerConnectionToAsset(state, "finale.freedom")),
                Condition("공공 신뢰 45 이상", state.PublicTrust >= 45), Condition("기술 부채 65 이하", state.TechnicalDebt <= 65));
            AddEndingReport(reports, state, "ENDING_OPEN_FRONTIER",
                Condition("닻—자유 연결", HasConnection(state, "finale.anchor", "finale.freedom")), Condition("위협 비활성", !threatActive),
                Condition("통로 활성", HasActiveAsset(state, "finale.passage")), Condition("닻—수호 연결 없음", !HasConnection(state, "finale.anchor", "finale.safeguard")),
                Condition("세계 자율성 50 이상", state.WorldAutonomy >= 50));
            AddEndingReport(reports, state, "ENDING_KEEP_THE_PROMISE",
                Condition("플레이어—수호 연결", HasPlayerConnectionToAsset(state, "finale.safeguard")), Condition("기억—닻 연결", HasConnection(state, "finale.memory", "finale.anchor")),
                Condition("위협 비활성", !threatActive), Condition("플레이어—통로 연결 없음", !HasPlayerConnectionToAsset(state, "finale.passage")),
                Condition("동료 유대 45 이상", state.CompanionBond >= 45));
            AddEndingReport(reports, state, "ENDING_CUT_THE_CYCLE",
                Condition("닻—통로 연결", HasConnection(state, "finale.anchor", "finale.passage")), Condition("위협 비활성", !threatActive),
                Condition("자유 핵 비활성", !freedomActive), Condition("증언 활성", HasActiveAsset(state, "finale.witness")),
                Condition("플레이어—수호 연결 없음", !HasPlayerConnectionToAsset(state, "finale.safeguard")), Condition("기술 부채 55 이하", state.TechnicalDebt <= 55));
            AddEndingReport(reports, state, "ENDING_PRESERVE_THE_SCARS",
                Condition("기억—수호 연결", HasConnection(state, "finale.memory", "finale.safeguard")), Condition("플레이어—증언 연결", HasPlayerConnectionToAsset(state, "finale.witness")),
                Condition("자유 핵 비활성", !freedomActive), Condition("위협 활성", threatActive), Condition("닻—위협 연결 없음", !HasConnection(state, "finale.anchor", "finale.threat")),
                Condition("세계 안정성 35 이상", state.WorldStability >= 35), Condition("공공 신뢰 40 이상", state.PublicTrust >= 40));
            AddEndingReport(reports, state, "ENDING_WALK_BETWEEN_WORLDS",
                Condition("플레이어—통로 연결", HasPlayerConnectionToAsset(state, "finale.passage")), Condition("닻—수호 연결", HasConnection(state, "finale.anchor", "finale.safeguard")),
                Condition("위협 비활성", !threatActive), Condition("플레이어—자유 연결 없음", !HasPlayerConnectionToAsset(state, "finale.freedom")),
                Condition("세계 안정성 45 이상", state.WorldStability >= 45), Condition("동료 유대 35 이상", state.CompanionBond >= 35));
            reports.Sort((left, right) => right.SatisfiedCount != left.SatisfiedCount
                ? right.SatisfiedCount.CompareTo(left.SatisfiedCount)
                : string.CompareOrdinal(left.Code, right.Code));
            return reports;
        }

        private static EndingCondition Condition(string label, bool satisfied) => new EndingCondition(label, satisfied);

        private static void AddEndingReport(List<EndingConditionReport> reports, RunState state, string code,
            params EndingCondition[] specific)
        {
            EndingCandidateState candidate = state.EndingCandidates.Find(value => value.Code == code);
            if (candidate == null) return;
            var conditions = new List<EndingCondition>
            {
                Condition("관리자 권한과 내부 원인 단서", CanEnterRootSystem(state)),
                Condition("세계 안정성 붕괴 방지", state.WorldStability > 5),
                Condition("필수 비트 " + candidate.MinimumCompletedBeats + "개 완료", state.CompletedBeatCount >= candidate.MinimumCompletedBeats),
                Condition("턴 제한 시 미완료 비트 없음", state.CurrentTurn < state.TurnLimit || state.CurrentBeat == null)
            };
            conditions.AddRange(specific);
            reports.Add(new EndingConditionReport(code, candidate.Title, conditions));
        }

        public static void UpdateEndingEligibility(RunState state)
        {
            IReadOnlyList<EndingConditionReport> reports = EvaluateEndingBoard(state);
            for (int i = 0; i < state.EndingCandidates.Count; i++)
            {
                EndingCandidateState candidate = state.EndingCandidates[i];
                candidate.IsEligible = candidate.Code == CampaignCatalog.FallbackEndingCode;
                for (int j = 0; j < reports.Count; j++)
                    if (reports[j].Code == candidate.Code) candidate.IsEligible = reports[j].IsEligible;
            }
        }

        private static void CompleteBeat(RunState state, CampaignBeatState beat, TurnRequest request,
            ActionContext actionContext, int turnNo, List<string> events)
        {
            beat.IsCompleted = true;
            beat.ResolvedTurn = turnNo;
            beat.Resolution = CampaignCatalog.ContextLabel(actionContext) + " 문맥에서 " +
                request.Ability + " 스킬과 선택된 대상만으로 해결했다.";
            state.ResolveOpenLoop(beat.Objective);
            state.AddCanonicalFact(beat.Title + " — " + beat.Resolution);
            state.QuestAccepted = true;
            events.Add("CAMPAIGN_BEAT_COMPLETED:" + beat.Id);
            if (!string.IsNullOrWhiteSpace(beat.AdminAccessRewardId) && state.AdminAccess < 3)
            {
                int level = Array.IndexOf(CampaignCatalog.AdminAccessLevelIds, beat.AdminAccessRewardId) + 1;
                if (level == state.AdminAccess + 1)
                {
                    state.AdminAccess = level;
                    string accessId = beat.AdminAccessRewardId;
                    if (!state.HasItem(accessId)) state.Inventory.Add(accessId);
                    state.AdminAccessAcquisitionHistory.Add(new AdminAccessAcquisitionRecord(level,
                        accessId, beat.RoleId, actionContext, request.Ability, turnNo));
                    state.AddCanonicalFact("관리자 권한 " + level + "/3 획득: " + accessId + " · " +
                        CampaignCatalog.RegionLabel(beat.RoleId) + " · " + CampaignCatalog.ContextLabel(actionContext));
                    events.Add("ADMIN_ACCESS_ACQUIRED:" + level + ":" + accessId + ":" +
                        beat.RoleId + ":" + actionContext.ToString().ToUpperInvariant());
                }
            }
            if (beat.Id == "truth")
                state.AddCanonicalFact("붕괴 원인이 관리자 통제 시스템 내부에 있음을 확정했다.");
            AdvanceBeatPointer(state);
        }

        private static void ApplyMetricDelta(RunState state, TurnRequest request, ActionContext actionContext,
            RuleOutcome outcome, int turnNo, List<string> events)
        {
            bool success = outcome == RuleOutcome.PartialSuccess || outcome == RuleOutcome.Success ||
                           outcome == RuleOutcome.CriticalSuccess;
            int stability = 0, autonomy = 0, trust = 0, debt = 0, bond = 0;
            if (!success) { stability = -2; trust = -1; debt = 2; }
            else
            {
                switch (request.Ability)
                {
                    case AbilityKind.Copy: stability = -1; autonomy = 2; debt = 3; break;
                    case AbilityKind.Delete: stability = 1; autonomy = -1; trust = -1; debt = 2; break;
                    case AbilityKind.Connect:
                        stability = 2;
                        autonomy = 2;
                        trust = 1;
                        // NPC와 만든 새 연결은 실제 동료 관계로 이어진다. 한 연결에 NPC가 둘이면 둘 다 반영한다.
                        bond = 5 * CountNpcTargets(state, request);
                        break;
                    case AbilityKind.Restore: stability = 3; debt = -2; trust = 1; break;
                    case AbilityKind.Undo: stability = 1; debt = 3; trust = -1; break;
                }
            }
            state.WorldStability = RunState.ClampMetric(state.WorldStability + stability);
            state.WorldAutonomy = RunState.ClampMetric(state.WorldAutonomy + autonomy);
            state.PublicTrust = RunState.ClampMetric(state.PublicTrust + trust);
            state.TechnicalDebt = RunState.ClampMetric(state.TechnicalDebt + debt);
            state.CompanionBond = RunState.ClampMetric(state.CompanionBond + bond);
            events.Add("METRICS_CHANGED:stability=" + stability + ":autonomy=" + autonomy + ":trust=" + trust +
                       ":debt=" + debt + ":bond=" + bond);
            if (bond > 0)
                events.Add("COMPANION_BOND_CHANGED:+" + bond + ":reason=NPC_CONNECTION");
            RecordDebt(state, request, actionContext, turnNo, debt, events);
            RecordChoiceAndRegionOutcome(state, request, actionContext, outcome, turnNo);
        }

        private static void RecordDebt(RunState state, TurnRequest request, ActionContext context,
            int turnNo, int debtDelta, List<string> events)
        {
            string targetId = request.TargetEntityId?.ToString("N") ?? string.Empty;
            if (request.Ability == AbilityKind.Restore && debtDelta < 0)
            {
                for (int i = 0; i < state.TechnicalDebtEntries.Count; i++)
                {
                    TechnicalDebtEntry entry = state.TechnicalDebtEntries[i];
                    if (!entry.IsResolved)
                    {
                        entry.ResolvedTurn = turnNo;
                        events.Add("TECHNICAL_DEBT_RESOLVED:" + entry.Id + ":turn=" + turnNo);
                        break;
                    }
                }
            }
            if (debtDelta <= 0) return;
            string id = "debt-" + turnNo + "-" + state.TechnicalDebtEntries.Count;
            string consequence = request.Ability == AbilityKind.Delete ? "REMOVED_DEPENDENCY_BACKFLOW"
                : request.Ability == AbilityKind.Copy ? "DUPLICATED_STATE_DRIFT"
                : request.Ability == AbilityKind.Undo ? "COMPENSATION_CONFLICT" : "UNRESOLVED_OVERRIDE";
            var entryToAdd = new TechnicalDebtEntry(id, turnNo, request.Ability,
                request.Ability.ToString().ToUpperInvariant(), string.IsNullOrEmpty(targetId) ? "WORLD_CODRIA" : targetId,
                request.Ability == AbilityKind.Delete ||
                request.Ability == AbilityKind.Undo, debtDelta, consequence);
            state.TechnicalDebtEntries.Add(entryToAdd);
            events.Add("TECHNICAL_DEBT_RECORDED:" + id + ":delta=" + debtDelta + ":" + consequence);
        }

        private static void RecordChoiceAndRegionOutcome(RunState state, TurnRequest request,
            ActionContext context, RuleOutcome outcome, int turnNo)
        {
            string region = state.Spatial.TryGetEntity(state.PlayerEntityId, out EntityState player)
                ? state.Region.AreaAt(player.Position)?.CampaignRole ?? string.Empty
                : string.Empty;
            string choice = "T" + turnNo + "|" + region + "|" + context + "|" + request.Ability + "|" + outcome;
            state.MajorChoices.Add(choice);
            while (state.MajorChoices.Count > 24) state.MajorChoices.RemoveAt(0);
            if (!string.IsNullOrEmpty(region))
            {
                state.RegionOutcomes.RemoveAll(value => value.StartsWith(region + "|", StringComparison.Ordinal));
                state.RegionOutcomes.Add(region + "|" + context + "|" + outcome + "|T" + turnNo);
            }
        }

        private static bool HasConnection(RunState state, string leftAsset, string rightAsset)
        {
            EntityState left = null, right = null;
            foreach (EntityState entity in state.Spatial.Entities)
            {
                if (entity.AssetId == leftAsset) left = entity;
                if (entity.AssetId == rightAsset) right = entity;
            }
            if (left == null || right == null) return false;
            string leftId = left.EntityId.ToString("N");
            string rightId = right.EntityId.ToString("N");
            string key = string.CompareOrdinal(leftId, rightId) <= 0 ? leftId + ":" + rightId : rightId + ":" + leftId;
            return state.Connections.Contains(key);
        }

        private static bool HasPlayerConnectionToAsset(RunState state, string assetId)
        {
            if (!state.Spatial.TryGetEntity(state.PlayerEntityId, out EntityState player)) return false;
            EntityState target = null;
            foreach (EntityState entity in state.Spatial.Entities)
                if (entity.AssetId == assetId) { target = entity; break; }
            if (target == null) return false;
            string playerId = player.EntityId.ToString("N");
            string targetId = target.EntityId.ToString("N");
            string key = string.CompareOrdinal(playerId, targetId) <= 0
                ? playerId + ":" + targetId
                : targetId + ":" + playerId;
            return state.Connections.Contains(key);
        }

        private static bool HasEndingCandidate(RunState state, string code)
        {
            for (int i = 0; i < state.EndingCandidates.Count; i++)
                if (string.Equals(state.EndingCandidates[i].Code, code, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool HasActiveAsset(RunState state, string assetId)
        {
            foreach (EntityState entity in state.Spatial.Entities)
                if (entity.AssetId == assetId && entity.IsActive) return true;
            return false;
        }

        private static void AdvanceBeatPointer(RunState state)
        {
            while (state.CurrentBeatIndex < state.RequiredBeats.Count &&
                   (state.RequiredBeats[state.CurrentBeatIndex].IsCompleted || state.RequiredBeats[state.CurrentBeatIndex].IsSkipped))
                state.CurrentBeatIndex++;
        }

        public static IReadOnlyList<AbilityKind> AllowedAbilitiesForBeat(string beatId)
        {
            switch (beatId)
            {
                case "arrival":
                case "collapse":
                case "truth": return InvestigationAbilities;
                case "admin-access-1": return AccessOneAbilities;
                case "admin-access-2": return AccessTwoAbilities;
                case "admin-access-3": return AccessThreeAbilities;
                case "debt-backflow": return DeploymentAbilities;
                case "root-entry":
                case "final-deployment": return FinaleAbilities;
                default: return Array.Empty<AbilityKind>();
            }
        }

        private static bool AbilityAllowedForBeat(CampaignBeatState beat, AbilityKind ability,
            ActionContext context)
        {
            if (beat.RequiredContext != ActionContext.None && beat.RequiredContext != context)
                return false;
            if (beat.Id.StartsWith("admin-access-", StringComparison.Ordinal))
                return beat.TriggerAbility == ability && IsCampaignAction(context);
            IReadOnlyList<AbilityKind> allowed = AllowedAbilitiesForBeat(beat.Id);
            for (int i = 0; i < allowed.Count; i++)
                if (allowed[i] == ability) return true;
            return false;
        }

        private static bool IsRequiredCampaignRegion(RunState state, CampaignBeatState beat)
        {
            if (!state.Spatial.TryGetEntity(state.PlayerEntityId, out EntityState player)) return false;
            WorldArea area = state.Region.AreaAt(player.Position);
            if (area == null) return false;
            return string.Equals(area.CampaignRole, beat.RoleId, StringComparison.Ordinal);
        }

        private static bool HasDesignatedEvidence(RunState state, CampaignBeatState beat, TurnRequest request,
            List<string> events)
        {
            if (beat.Id == "root-entry")
                return CanEnterRootSystem(state);
            if (beat.Id == "final-deployment")
                return !string.Equals(SelectEnding(state), CampaignCatalog.FallbackEndingCode,
                    StringComparison.Ordinal);

            EntityState first = null, second = null;
            if (request.TargetEntityId.HasValue) state.Spatial.TryGetEntity(request.TargetEntityId.Value, out first);
            if (request.SecondaryTargetEntityId.HasValue) state.Spatial.TryGetEntity(request.SecondaryTargetEntityId.Value, out second);
            if (beat.Id == "arrival")
            {
                if (first != null && first.AssetId == CampaignCatalog.AdministratorKeyboardId)
                    return ContainsEvent(events, "CATALYST_AWAKENED:") ||
                           ContainsEvent(events, "ADMIN_KEYBOARD_AWAKENED:");
                return false;
            }
            if (beat.Id == "truth")
                return (first != null && first.AssetId.StartsWith("story.internal-failure", StringComparison.Ordinal) ||
                        second != null && second.AssetId.StartsWith("story.internal-failure", StringComparison.Ordinal)) &&
                       ContainsEvent(events, "INTERNAL_FAILURE_CONFIRMED:");
            if (beat.Id == "debt-backflow")
                return request.Ability == AbilityKind.Connect &&
                       (first != null && first.Kind == EntityKind.Npc ||
                        second != null && second.Kind == EntityKind.Npc) &&
                       ContainsEvent(events, "CONNECTION_CREATED:");
            if (beat.Id.StartsWith("admin-access-", StringComparison.Ordinal))
            {
                string level = beat.Id.Substring("admin-access-".Length);
                string prefix = "story.admin-access-" + level;
                bool designated = first != null && first.AssetId.StartsWith(prefix, StringComparison.Ordinal) ||
                                  second != null && second.AssetId.StartsWith(prefix, StringComparison.Ordinal);
                return designated && (ContainsEntityEvent(events, "ADMIN_ACCESS_CANDIDATE_INSPECTED:", first, second) ||
                    ContainsEntityEvent(events, "ADMIN_ACCESS_CANDIDATE_REPAIRED:", first, second) ||
                    ContainsEntityEvent(events, "ENEMY_DEFEATED:", first, second) ||
                    ContainsEntityEvent(events, "ENTITY_REMOVED:", first, second) ||
                    ContainsEvent(events, "CONNECTION_CREATED:"));
            }
            return first != null || second != null;
        }

        /// <summary>Connect에 참여한 NPC 수를 세어 동료 신뢰도 증가량을 결정한다.</summary>
        private static int CountNpcTargets(RunState state, TurnRequest request)
        {
            int count = 0;
            if (request.TargetEntityId.HasValue &&
                state.Spatial.TryGetEntity(request.TargetEntityId.Value, out EntityState first) &&
                first.Kind == EntityKind.Npc)
                count++;
            if (request.SecondaryTargetEntityId.HasValue &&
                state.Spatial.TryGetEntity(request.SecondaryTargetEntityId.Value, out EntityState second) &&
                second.Kind == EntityKind.Npc)
                count++;
            return count;
        }

        /// <summary>문자열 이벤트라도 지정된 대상 ID까지 일치할 때만 캠페인 증거로 인정한다.</summary>
        private static bool ContainsEntityEvent(List<string> events, string prefix,
            EntityState first, EntityState second)
        {
            string firstId = first?.EntityId.ToString() ?? string.Empty;
            string secondId = second?.EntityId.ToString() ?? string.Empty;
            for (int i = 0; i < events.Count; i++)
            {
                string value = events[i];
                if (string.IsNullOrEmpty(value) || !value.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                string payload = value.Substring(prefix.Length);
                if (!string.IsNullOrEmpty(firstId) && payload.StartsWith(firstId, StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrEmpty(secondId) && payload.StartsWith(secondId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static bool HasInternalFailureClue(RunState state)
        {
            for (int i = 0; i < state.CanonicalFacts.Count; i++)
                if (state.CanonicalFacts[i].Contains("관리자 통제 시스템 내부")) return true;
            return false;
        }

        public static bool CanEnterRootSystem(RunState state)
        {
            if (state == null || state.AdminAccess != 3 || !HasInternalFailureClue(state)) return false;
            for (int i = 0; i < CampaignCatalog.AdminAccessLevelIds.Length; i++)
                if (!state.HasItem(CampaignCatalog.AdminAccessLevelIds[i])) return false;
            return true;
        }

        private static bool IsCampaignAction(ActionContext context)
        {
            return context == ActionContext.Combat || context == ActionContext.Investigation ||
                   context == ActionContext.Negotiation || context == ActionContext.Deployment;
        }

        private static bool ContainsEvent(List<string> events, string prefix)
        {
            for (int i = 0; i < events.Count; i++)
                if (events[i].StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

    }
}
