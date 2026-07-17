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
        private static readonly AbilityKind[] ArrivalAbilities =
            { AbilityKind.Interact, AbilityKind.Negotiate };
        private static readonly AbilityKind[] AdaptationAbilities =
            { AbilityKind.Copy, AbilityKind.Interact };
        private static readonly AbilityKind[] ExpansionAbilities =
            { AbilityKind.Connect, AbilityKind.Negotiate };
        private static readonly AbilityKind[] TruthAbilities =
            { AbilityKind.Interact, AbilityKind.Negotiate };
        private static readonly AbilityKind[] BackflowAbilities =
            { AbilityKind.Restore, AbilityKind.Interact };
        private static readonly AbilityKind[] FinaleAbilities =
            { AbilityKind.Connect, AbilityKind.Delete };

        public static CampaignConstraints Evaluate(int meaningfulTurns, int turnLimit)
        {
            if (turnLimit < 30 || turnLimit > 50)
                throw new ArgumentOutOfRangeException(nameof(turnLimit), "Campaign turn limit must be between 30 and 50.");
            meaningfulTurns = Math.Max(0, meaningfulTurns);
            int remaining = Math.Max(0, turnLimit - meaningfulTurns);
            CampaignPhase phase;
            int deadline;
            if (meaningfulTurns <= 4) { phase = CampaignPhase.Introduction; deadline = 4; }
            else if (meaningfulTurns <= 15) { phase = CampaignPhase.Adaptation; deadline = 15; }
            else if (meaningfulTurns <= 25) { phase = CampaignPhase.Expansion; deadline = 25; }
            else if (meaningfulTurns <= 35) { phase = CampaignPhase.Truth; deadline = 35; }
            else if (meaningfulTurns <= 42) { phase = CampaignPhase.Backflow; deadline = 42; }
            else { phase = CampaignPhase.Finale; deadline = turnLimit; }

            CampaignAct act = phase == CampaignPhase.Introduction ? CampaignAct.Introduction
                : phase == CampaignPhase.Adaptation || phase == CampaignPhase.Expansion ? CampaignAct.Exploration
                : phase == CampaignPhase.Truth ? CampaignAct.Pressure
                : phase == CampaignPhase.Backflow ? CampaignAct.Convergence
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

        public static void ProcessCommittedTurn(RunState state, TurnRequest request, RuleOutcome outcome,
            int turnNo, List<string> events)
        {
            if (state == null || request == null || events == null) return;
            bool applied = outcome == RuleOutcome.PartialSuccess || outcome == RuleOutcome.Success ||
                           outcome == RuleOutcome.CriticalSuccess;
            CampaignBeatState beat = state.CurrentBeat;
            if (beat != null && applied && AbilityAllowedForBeat(beat.Id, request.Ability) &&
                IsRequiredCampaignRole(state, beat.Id) && HasDesignatedEvidence(state, beat.Id, request, events))
                CompleteBeat(state, beat, request, turnNo, events);

            ApplyMetricDelta(state, request.Ability, outcome, events);
            state.TurnPressure = RunState.ClampMetric((int)Math.Round(turnNo * 100d / Math.Max(1, state.TurnLimit)));
            state.QuestStage = state.CurrentBeatIndex;
            UpdateEndingEligibility(state);
        }

        public static string SelectEnding(RunState state)
        {
            if (state == null) return CampaignCatalog.FallbackEndingCode;
            if (state.WorldStability <= 5 || state.MilestoneProgress < 3 ||
                (state.CurrentTurn >= state.TurnLimit && state.CurrentBeat != null))
                return CampaignCatalog.FallbackEndingCode;

            bool anchorToSafeguard = HasConnection(state, "finale.anchor", "finale.safeguard");
            bool safeguardToPassage = HasConnection(state, "finale.safeguard", "finale.passage");
            bool safeguardToWitness = HasConnection(state, "finale.safeguard", "finale.witness");
            bool playerToAnchor = HasPlayerConnectionToAsset(state, "finale.anchor");
            bool anchorToFreedom = HasConnection(state, "finale.anchor", "finale.freedom");
            bool anchorToMemory = HasConnection(state, "finale.anchor", "finale.memory");
            bool memoryToSafeguard = HasConnection(state, "finale.memory", "finale.safeguard");
            bool threatActive = HasActiveAsset(state, "finale.threat");
            bool freedomActive = HasActiveAsset(state, "finale.freedom");

            if (anchorToSafeguard && safeguardToPassage && !threatActive &&
                state.WorldStability >= 45 && state.TechnicalDebt <= 60 &&
                HasEndingCandidate(state, "SAFE_PASSAGE"))
                return "SAFE_PASSAGE";
            if (anchorToSafeguard && safeguardToWitness && threatActive && !playerToAnchor &&
                state.WorldStability >= 25 && state.PublicTrust >= 35 &&
                HasEndingCandidate(state, "SHARED_GUARDIANSHIP"))
                return "SHARED_GUARDIANSHIP";
            if (playerToAnchor && anchorToFreedom && freedomActive &&
                state.WorldAutonomy >= 45 && state.PublicTrust >= 40 &&
                HasEndingCandidate(state, "FREE_WORLD"))
                return "FREE_WORLD";
            if (anchorToMemory && memoryToSafeguard &&
                state.CompanionBond >= 35 && state.TechnicalDebt >= 15 &&
                HasEndingCandidate(state, "MEMORY_REWEAVE"))
                return "MEMORY_REWEAVE";
            if (!threatActive && safeguardToWitness && !safeguardToPassage &&
                HasEndingCandidate(state, "THREAT_SEAL"))
                return "THREAT_SEAL";
            return CampaignCatalog.FallbackEndingCode;
        }

        public static void UpdateEndingEligibility(RunState state)
        {
            for (int i = 0; i < state.EndingCandidates.Count; i++)
            {
                EndingCandidateState candidate = state.EndingCandidates[i];
                candidate.IsEligible = candidate.Code == CampaignCatalog.FallbackEndingCode ||
                    (state.MilestoneProgress >= 3 && state.CompletedBeatCount >= candidate.MinimumCompletedBeats);
            }
        }

        private static void CompleteBeat(RunState state, CampaignBeatState beat, TurnRequest request,
            int turnNo, List<string> events)
        {
            beat.IsCompleted = true;
            beat.ResolvedTurn = turnNo;
            beat.Resolution = string.IsNullOrWhiteSpace(request.IntentText)
                ? request.Ability + " 명령으로 해결했다."
                : "\"" + request.IntentText.Trim() + "\"라는 의도로 해결했다.";
            state.ResolveOpenLoop(beat.Objective);
            state.AddCanonicalFact(beat.Title + " — " + beat.Resolution);
            state.QuestAccepted = true;
            events.Add("CAMPAIGN_BEAT_COMPLETED:" + beat.Id);
            if (!string.IsNullOrWhiteSpace(beat.MilestoneTokenId) && state.MilestoneProgress < 3)
            {
                state.MilestoneProgress++;
                string token = beat.MilestoneTokenId;
                if (!state.HasItem(token)) state.Inventory.Add(token);
                state.AddCanonicalFact("핵심 표식 " + state.MilestoneProgress + "/3을 획득했다: " + token);
                events.Add("MILESTONE_TOKEN_ACQUIRED:" + state.MilestoneProgress + ":" + token);
            }
            if (beat.Id == "truth")
                state.AddCanonicalFact("감춰진 기록과 증언이 이번 세계의 위기가 내부 선택에서 비롯되었음을 확정했다.");
            AdvanceBeatPointer(state);
        }

        private static void ApplyMetricDelta(RunState state, AbilityKind ability, RuleOutcome outcome,
            List<string> events)
        {
            bool success = outcome == RuleOutcome.PartialSuccess || outcome == RuleOutcome.Success ||
                           outcome == RuleOutcome.CriticalSuccess;
            int stability = 0, autonomy = 0, trust = 0, debt = 0, bond = 0;
            if (!success) { stability = -2; trust = -1; debt = 2; }
            else
            {
                switch (ability)
                {
                    case AbilityKind.Copy: stability = -1; autonomy = 2; debt = 3; break;
                    case AbilityKind.Delete: stability = 1; autonomy = -1; trust = -1; debt = 2; break;
                    case AbilityKind.Connect: stability = 2; autonomy = 2; trust = 1; break;
                    case AbilityKind.Restore: stability = 3; debt = -2; trust = 1; break;
                    case AbilityKind.Undo: stability = 1; debt = 3; trust = -1; break;
                    case AbilityKind.Attack: stability = -1; debt = 1; break;
                    case AbilityKind.Interact: trust = 2; bond = 2; autonomy = 1; break;
                    case AbilityKind.Rest: stability = 1; bond = 1; break;
                    case AbilityKind.Negotiate: trust = 3; bond = 2; autonomy = 1; break;
                }
            }
            state.WorldStability = RunState.ClampMetric(state.WorldStability + stability);
            state.WorldAutonomy = RunState.ClampMetric(state.WorldAutonomy + autonomy);
            state.PublicTrust = RunState.ClampMetric(state.PublicTrust + trust);
            state.TechnicalDebt = RunState.ClampMetric(state.TechnicalDebt + debt);
            state.CompanionBond = RunState.ClampMetric(state.CompanionBond + bond);
            events.Add("METRICS_CHANGED:stability=" + stability + ":autonomy=" + autonomy + ":trust=" + trust +
                       ":debt=" + debt + ":bond=" + bond);
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
                case "arrival": return ArrivalAbilities;
                case "adaptation": return AdaptationAbilities;
                case "expansion": return ExpansionAbilities;
                case "truth": return TruthAbilities;
                case "backflow": return BackflowAbilities;
                case "finale": return FinaleAbilities;
                default: return Array.Empty<AbilityKind>();
            }
        }

        private static bool AbilityAllowedForBeat(string beatId, AbilityKind ability)
        {
            IReadOnlyList<AbilityKind> allowed = AllowedAbilitiesForBeat(beatId);
            for (int i = 0; i < allowed.Count; i++)
                if (allowed[i] == ability) return true;
            return false;
        }

        private static bool IsRequiredCampaignRole(RunState state, string beatId)
        {
            if (!state.Spatial.TryGetEntity(state.PlayerEntityId, out EntityState player)) return false;
            WorldArea area = state.Region.AreaAt(player.Position);
            if (area == null) return false;
            string required = state.CurrentBeat != null && state.CurrentBeat.Id == beatId
                ? state.CurrentBeat.RoleId
                : string.Empty;
            if (string.IsNullOrWhiteSpace(required))
            {
                switch (beatId)
                {
                    case "arrival": required = CampaignCatalog.ArrivalCatalystRole; break;
                    case "adaptation": required = CampaignCatalog.LocalStakesRole; break;
                    case "expansion": required = CampaignCatalog.RelationshipConflictRole; break;
                    case "truth": required = CampaignCatalog.HiddenTruthRole; break;
                    case "backflow": required = CampaignCatalog.ConsequenceReturnRole; break;
                    case "finale": required = CampaignCatalog.FinalConvergenceRole; break;
                    default: return false;
                }
            }
            return string.Equals(area.CampaignRole, required, StringComparison.Ordinal);
        }

        private static bool HasDesignatedEvidence(RunState state, string beatId, TurnRequest request,
            List<string> events)
        {
            if (beatId == "finale")
                return !string.Equals(SelectEnding(state), CampaignCatalog.FallbackEndingCode,
                    StringComparison.Ordinal);

            EntityState first = null, second = null;
            if (request.TargetEntityId.HasValue) state.Spatial.TryGetEntity(request.TargetEntityId.Value, out first);
            if (request.SecondaryTargetEntityId.HasValue) state.Spatial.TryGetEntity(request.SecondaryTargetEntityId.Value, out second);
            if (beatId == "arrival")
            {
                if (first != null && first.AssetId == "artifact.keyboard")
                    return ContainsEvent(events, "CATALYST_AWAKENED:") ||
                           ContainsEvent(events, "KEYBOARD_ARTIFACT_AWAKENED:");
                return first != null && first.Kind == EntityKind.Npc &&
                       string.Equals(state.Region.AreaAt(first.Position)?.CampaignRole,
                           CampaignCatalog.ArrivalCatalystRole, StringComparison.Ordinal) &&
                       ContainsEvent(events, "NEGOTIATION_RESOLVED:");
            }

            if ((beatId == "expansion" || beatId == "truth") && request.Ability == AbilityKind.Negotiate)
            {
                string requiredRole = beatId == "expansion"
                    ? CampaignCatalog.RelationshipConflictRole
                    : CampaignCatalog.HiddenTruthRole;
                return first != null && first.Kind == EntityKind.Npc &&
                       string.Equals(state.Region.AreaAt(first.Position)?.CampaignRole, requiredRole, StringComparison.Ordinal) &&
                       ContainsEvent(events, "NEGOTIATION_RESOLVED:");
            }

            if (beatId == "backflow" && request.Ability == AbilityKind.Restore)
                return first != null && first.AssetId == "story.milestone-token-3.echo" &&
                       ContainsEvent(events, "ENTITY_RESTORED:");

            string requiredAsset;
            switch (beatId)
            {
                case "adaptation": requiredAsset = "story.milestone-token-1"; break;
                case "expansion": requiredAsset = "story.milestone-token-2"; break;
                case "truth": requiredAsset = "story.hidden-truth"; break;
                case "backflow": requiredAsset = "story.milestone-token-3"; break;
                default: return false;
            }
            bool designated = beatId == "truth"
                ? first != null && first.AssetId.StartsWith(requiredAsset, StringComparison.Ordinal) ||
                  second != null && second.AssetId.StartsWith(requiredAsset, StringComparison.Ordinal)
                : first != null && first.AssetId == requiredAsset || second != null && second.AssetId == requiredAsset;
            return designated && (ContainsEvent(events, "MILESTONE_ANCHOR_INSPECTED:") ||
                                  ContainsEvent(events, "HIDDEN_TRUTH_CONFIRMED:") ||
                                  ContainsEvent(events, "ENTITY_SPAWNED:") ||
                                  ContainsEvent(events, "CONNECTION_CREATED:"));
        }

        private static bool ContainsEvent(List<string> events, string prefix)
        {
            for (int i = 0; i < events.Count; i++)
                if (events[i].StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool HasCommitEvidence(AbilityKind ability, List<string> events)
        {
            string prefix;
            switch (ability)
            {
                case AbilityKind.Move: prefix = "ENTITY_MOVED:"; break;
                case AbilityKind.Copy: prefix = "ENTITY_SPAWNED:"; break;
                case AbilityKind.Delete: prefix = "ENTITY_REMOVED:"; break;
                case AbilityKind.Connect: prefix = "CONNECTION_CREATED:"; break;
                case AbilityKind.Restore: prefix = "ENTITY_RESTORED:"; break;
                case AbilityKind.Undo: prefix = "UNDO_COMPENSATION:"; break;
                case AbilityKind.Interact: prefix = "NPC_"; break;
                case AbilityKind.Attack: prefix = "ENTITY_DAMAGED:"; break;
                case AbilityKind.Rest: prefix = "PLAYER_HEALED:"; break;
                case AbilityKind.Negotiate: prefix = "NEGOTIATION_RESOLVED:"; break;
                default: return false;
            }
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].StartsWith(prefix, StringComparison.Ordinal)) return true;
                if (ability == AbilityKind.Interact &&
                    (events[i].StartsWith("ENTITY_INSPECTED:", StringComparison.Ordinal) ||
                     events[i].StartsWith("ITEM_ACQUIRED:", StringComparison.Ordinal))) return true;
            }
            return false;
        }
    }
}
