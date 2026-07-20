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
                    story.Remember("런 시작 전 확정된 장소에서 " + role + " 역할을 맡았다.");
                    state.NpcStories.Add(story);
                }
            }
            UpdateEndingEligibility(state);
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
            state.TurnPressure = RunState.ClampMetric((int)Math.Round(turnNo * 100d / Math.Max(1, state.TurnLimit)));
            state.QuestStage = state.CurrentBeatIndex;
            UpdateEndingEligibility(state);
        }

        public static string SelectEnding(RunState state)
        {
            if (state == null) return CampaignCatalog.FallbackEndingCode;
            if (state.WorldStability <= 5 || !CanEnterRootSystem(state) ||
                (state.CurrentTurn >= state.TurnLimit && state.CurrentBeat != null))
                return CampaignCatalog.FallbackEndingCode;

            bool threatActive = HasActiveAsset(state, "finale.threat");
            bool freedomActive = HasActiveAsset(state, "finale.freedom");
            if (HasConnection(state, "finale.anchor", "finale.safeguard") &&
                HasPlayerConnectionToAsset(state, "finale.memory") && !threatActive &&
                !HasPlayerConnectionToAsset(state, "finale.freedom") && state.PublicTrust >= 45 &&
                state.TechnicalDebt <= 65 && HasEndingCandidate(state, "ENDING_REWEAVE_TOGETHER"))
                return "ENDING_REWEAVE_TOGETHER";
            if (HasConnection(state, "finale.anchor", "finale.freedom") && !threatActive &&
                HasActiveAsset(state, "finale.passage") &&
                !HasConnection(state, "finale.anchor", "finale.safeguard") && state.WorldAutonomy >= 50 &&
                HasEndingCandidate(state, "ENDING_OPEN_FRONTIER"))
                return "ENDING_OPEN_FRONTIER";
            if (HasPlayerConnectionToAsset(state, "finale.safeguard") &&
                HasConnection(state, "finale.memory", "finale.anchor") && !threatActive &&
                !HasPlayerConnectionToAsset(state, "finale.passage") && state.CompanionBond >= 45 &&
                HasEndingCandidate(state, "ENDING_KEEP_THE_PROMISE"))
                return "ENDING_KEEP_THE_PROMISE";
            if (HasConnection(state, "finale.anchor", "finale.passage") && !threatActive &&
                !freedomActive && HasActiveAsset(state, "finale.witness") &&
                !HasPlayerConnectionToAsset(state, "finale.safeguard") && state.TechnicalDebt <= 55 &&
                HasEndingCandidate(state, "ENDING_CUT_THE_CYCLE"))
                return "ENDING_CUT_THE_CYCLE";
            if (HasConnection(state, "finale.memory", "finale.safeguard") &&
                HasPlayerConnectionToAsset(state, "finale.witness") && !freedomActive && threatActive &&
                !HasConnection(state, "finale.anchor", "finale.threat") && state.WorldStability >= 35 &&
                state.PublicTrust >= 40 && HasEndingCandidate(state, "ENDING_PRESERVE_THE_SCARS"))
                return "ENDING_PRESERVE_THE_SCARS";
            if (HasPlayerConnectionToAsset(state, "finale.passage") &&
                HasConnection(state, "finale.anchor", "finale.safeguard") && !threatActive &&
                !HasPlayerConnectionToAsset(state, "finale.freedom") && state.WorldStability >= 45 &&
                state.CompanionBond >= 35 && HasEndingCandidate(state, "ENDING_WALK_BETWEEN_WORLDS"))
                return "ENDING_WALK_BETWEEN_WORLDS";
            return CampaignCatalog.FallbackEndingCode;
        }

        public static void UpdateEndingEligibility(RunState state)
        {
            for (int i = 0; i < state.EndingCandidates.Count; i++)
            {
                EndingCandidateState candidate = state.EndingCandidates[i];
                candidate.IsEligible = candidate.Code == CampaignCatalog.FallbackEndingCode ||
                    (CanEnterRootSystem(state) &&
                     state.CompletedBeatCount >= candidate.MinimumCompletedBeats);
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
                    case AbilityKind.Connect: stability = 2; autonomy = 2; trust = 1; break;
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
                return designated && (ContainsEvent(events, "ADMIN_ACCESS_CANDIDATE_INSPECTED:") ||
                    ContainsEvent(events, "ADMIN_ACCESS_CANDIDATE_REPAIRED:") ||
                    ContainsEvent(events, "ENTITY_REMOVED:") || ContainsEvent(events, "CONNECTION_CREATED:"));
            }
            return first != null || second != null;
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
