using System;
using System.Collections.Generic;
using KeyboardWanderer.Core;

namespace KeyboardWanderer.Gameplay
{
    public interface ID20Source
    {
        int Roll();
    }

    public sealed class SeededD20Source : ID20Source
    {
        private readonly Random _random;

        public SeededD20Source(int seed) : this(seed, 0) { }

        public SeededD20Source(int seed, int rollsToSkip)
        {
            _random = new Random(seed);
            for (int i = 0; i < Math.Max(0, rollsToSkip); i++)
                _random.Next(1, 21);
        }

        public int Roll() { return _random.Next(1, 21); }
    }

    public sealed class SequenceD20Source : ID20Source
    {
        private readonly int[] _values;
        private int _index;

        public SequenceD20Source(params int[] values)
        {
            _values = values == null || values.Length == 0 ? new[] { 10 } : values;
        }

        public int Roll()
        {
            int value = _values[_index % _values.Length];
            _index++;
            if (value < 1 || value > 20)
                throw new InvalidOperationException("D20 source returned a value outside 1..20.");
            return value;
        }
    }

    public sealed class RulePreparation
    {
        public bool IsValid { get; }
        public TurnErrorCode ErrorCode { get; }
        public string ErrorMessage { get; }
        public string NormalizedAttempt { get; }
        public int Difficulty { get; }
        public int Modifier { get; }
        public int FocusCost { get; }
        public int IntentAlignment { get; }
        public Guid? ResolvedTargetEntityId { get; }
        public IReadOnlyList<GridCoord> Path { get; }

        private RulePreparation(bool isValid, TurnErrorCode errorCode, string errorMessage,
            string normalizedAttempt, int difficulty, int modifier, int focusCost, int intentAlignment,
            Guid? resolvedTargetEntityId, IReadOnlyList<GridCoord> path)
        {
            IsValid = isValid;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            NormalizedAttempt = normalizedAttempt;
            Difficulty = difficulty;
            Modifier = modifier;
            FocusCost = focusCost;
            IntentAlignment = intentAlignment;
            ResolvedTargetEntityId = resolvedTargetEntityId;
            Path = path ?? Array.Empty<GridCoord>();
        }

        public static RulePreparation Invalid(TurnErrorCode code, string message)
        {
            return new RulePreparation(false, code, message, string.Empty, 0, 0, 0, 0, null, null);
        }

        public static RulePreparation Valid(string normalizedAttempt, int difficulty, int baseModifier,
            int focusCost, IReadOnlyList<GridCoord> path = null, Guid? resolvedTargetEntityId = null)
        {
            return new RulePreparation(true, TurnErrorCode.None, null, normalizedAttempt, difficulty,
                baseModifier, focusCost, 0, resolvedTargetEntityId, path);
        }

        public RulePreparation WithIntentAlignment(int alignment)
        {
            if (!IsValid)
                return this;
            alignment = Math.Max(0, Math.Min(3, alignment));
            return new RulePreparation(true, TurnErrorCode.None, null, NormalizedAttempt, Difficulty,
                Modifier + alignment, FocusCost, alignment, ResolvedTargetEntityId, Path);
        }

        public RulePreparation WithModifier(int bonus)
        {
            if (!IsValid || bonus == 0) return this;
            return new RulePreparation(true, TurnErrorCode.None, null, NormalizedAttempt, Difficulty,
                Modifier + bonus, FocusCost, IntentAlignment, ResolvedTargetEntityId, Path);
        }
    }

    /// <summary>
    /// Sole authority for legal attempts and mechanical state. Narrative providers can describe the
    /// emitted events but cannot mutate coordinates, resources, campaign facts, snapshots, or endings.
    /// </summary>
    public sealed class RuleEngine
    {
        public RulePreparation Prepare(RunState state, TurnRequest request)
        {
            if (state == null || request == null || string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
                request.IntentText.Length > 500)
                return RulePreparation.Invalid(TurnErrorCode.InvalidRequest,
                    "Idempotency key is required; an optional flavour note is limited to 500 characters.");
            if ((request.Ability == AbilityKind.Move && request.InputType != PlayerInputType.MOVE) ||
                (request.Ability != AbilityKind.Move && request.InputType != PlayerInputType.USE_SKILL) ||
                (request.InputType == PlayerInputType.USE_SKILL &&
                 !TurnRequest.IsPublicKeyboardSkill(request.Ability) && request.Ability != AbilityKind.Interact))
                return RulePreparation.Invalid(TurnErrorCode.InvalidRequest,
                    "Public input is MOVE or USE_SKILL with a keyboard skill or INTERACT.");
            if (!state.Spatial.TryGetEntity(state.PlayerEntityId, out EntityState player) ||
                !player.IsActive || player.Health <= 0)
                return RulePreparation.Invalid(TurnErrorCode.EntityNotFound, "Player entity is missing or defeated.");

            RulePreparation preparation;
            switch (request.Ability)
            {
                case AbilityKind.Move: preparation = PrepareMove(state, request, player); break;
                case AbilityKind.Copy: preparation = PrepareCopy(state, request, player); break;
                case AbilityKind.Delete: preparation = PrepareDelete(state, request, player); break;
                case AbilityKind.Connect: preparation = PrepareConnect(state, request, player); break;
                case AbilityKind.Restore: preparation = PrepareRestore(state, request, player); break;
                case AbilityKind.Undo: preparation = PrepareUndo(state); break;
                case AbilityKind.Interact: preparation = PrepareInteract(state, request, player); break;
                case AbilityKind.Search: preparation = PrepareSearch(state, request, player); break;
                case AbilityKind.SelectAll: preparation = PrepareAreaAttack(state, request, player); break;
                default: preparation = RulePreparation.Invalid(TurnErrorCode.InvalidRequest, "Unknown ability."); break;
            }
            // Free text is never rules authority. Skill, targets, location, and scene state alone
            // determine legality, modifier, difficulty, and outcome.
            if (preparation.IsValid && request.Ability != AbilityKind.Move)
                preparation = preparation.WithModifier(CompanionSupportBonus(state, player));
            return preparation;
        }

        private static int CompanionSupportBonus(RunState state, EntityState player)
        {
            for (int i = 0; i < state.NpcStories.Count; i++)
            {
                NpcStoryState story = state.NpcStories[i];
                bool promised = story.Memories.Exists(memory =>
                    memory.StartsWith("[약속]", StringComparison.Ordinal));
                if (promised && state.Spatial.TryGetEntity(story.EntityId, out EntityState npc) && npc.IsActive &&
                    player.Position.ManhattanDistance(npc.Position) <= 4) return 1;
            }
            return 0;
        }

        public static ActionContext ClassifyAction(RunState state, TurnRequest request)
        {
            if (request == null) return ActionContext.None;
            if (request.InputType == PlayerInputType.MOVE || request.Ability == AbilityKind.Move)
                return ActionContext.SafeTravel;
            switch (request.Ability)
            {
                case AbilityKind.Search:
                    return ActionContext.Investigation;
                case AbilityKind.Interact:
                    if (TryTarget(state, request.TargetEntityId, out EntityState interactTarget) &&
                        interactTarget.Kind == EntityKind.Npc) return ActionContext.Negotiation;
                    return ActionContext.Investigation;
                case AbilityKind.Restore:
                case AbilityKind.Undo:
                case AbilityKind.Copy: return ActionContext.Deployment;
                case AbilityKind.SelectAll: return ActionContext.Combat;
                case AbilityKind.Delete:
                    if (TryTarget(state, request.TargetEntityId, out EntityState deleteTarget) &&
                        deleteTarget.Kind == EntityKind.Enemy) return ActionContext.Combat;
                    return ActionContext.Deployment;
                case AbilityKind.Connect:
                    if (TryTarget(state, request.TargetEntityId, out EntityState first) && first.Kind == EntityKind.Npc ||
                        TryTarget(state, request.SecondaryTargetEntityId, out EntityState second) && second.Kind == EntityKind.Npc)
                        return ActionContext.Negotiation;
                    return ActionContext.Deployment;
                default: return ActionContext.None;
            }
        }

        public static bool ConsumesCampaignTurn(ActionContext context)
        {
            return context == ActionContext.Combat || context == ActionContext.Investigation ||
                   context == ActionContext.Negotiation || context == ActionContext.Deployment;
        }

        public static RuleOutcome ResolveOutcome(int mechanicalScore, int rawD20)
        {
            if (rawD20 == 1) return RuleOutcome.CriticalFailure;
            if (mechanicalScore >= 10 && rawD20 == 20) return RuleOutcome.CriticalSuccess;
            if (mechanicalScore >= 1) return RuleOutcome.Success;
            if (mechanicalScore >= -4) return RuleOutcome.PartialSuccess;
            if (mechanicalScore >= -9) return RuleOutcome.Failure;
            return RuleOutcome.CriticalFailure;
        }

        public static int ConsequenceBudget(int rawD20)
        {
            if (rawD20 == 1) return 4;
            if (rawD20 <= 5) return 3;
            if (rawD20 <= 10) return 2;
            if (rawD20 <= 15) return 1;
            return 0;
        }

        public static string ExplainOutcome(int rawD20, RulePreparation preparation, RuleOutcome outcome)
        {
            int score = rawD20 + preparation.Modifier - preparation.Difficulty;
            return "D20 " + rawD20 + " + 수정 " + preparation.Modifier + " - 난이도 " +
                   preparation.Difficulty + " = " + score + "; 선택된 스킬·대상·장면 상태로 " +
                   outcome + " 판정.";
        }

        public List<string> Apply(RunState state, TurnRequest request, RulePreparation preparation,
            RuleOutcome outcome, int turnNo, int consequenceBudget = 0)
        {
            var events = new List<string>();
            bool primary = IsApplied(outcome);

            if (request.Ability != AbilityKind.Undo && preparation.FocusCost > 0)
            {
                state.Focus = Math.Max(0, state.Focus - preparation.FocusCost);
                events.Add("RESOURCE_CHANGED:focus:-" + preparation.FocusCost);
            }

            if (primary)
            {
                switch (request.Ability)
                {
                    case AbilityKind.Move: ApplyMove(state, request, events); break;
                    case AbilityKind.Copy: ApplyCopy(state, request, turnNo, events); break;
                    case AbilityKind.Delete: ApplyDelete(state, request, turnNo, events); break;
                    case AbilityKind.Connect: ApplyConnect(state, request, events); break;
                    case AbilityKind.Restore: ApplyRestore(state, preparation, events); break;
                    case AbilityKind.Undo: ApplyUndo(state, preparation.FocusCost, events); break;
                    case AbilityKind.Interact: ApplyInteract(state, request, events); break;
                    case AbilityKind.Search: ApplySearch(state, request, outcome, turnNo, events); break;
                    case AbilityKind.SelectAll: ApplyAreaAttack(state, events); break;
                }
            }
            else if (request.Ability == AbilityKind.Undo && preparation.FocusCost > 0)
            {
                state.Focus = Math.Max(0, state.Focus - preparation.FocusCost);
                events.Add("RESOURCE_CHANGED:focus:-" + preparation.FocusCost);
            }
            else if (request.Ability == AbilityKind.Search)
            {
                ApplyNpcInvestigation(state, request, outcome, turnNo, events);
            }

            if (outcome == RuleOutcome.PartialSuccess)
            {
                ApplyPartialConsequence(state, request, Math.Max(1, consequenceBudget), events);
            }
            else if (outcome == RuleOutcome.CriticalSuccess)
            {
                state.IsExposed = false;
                state.Focus = Math.Min(state.MaxFocus, state.Focus + 1);
                events.Add("RESOURCE_CHANGED:focus:+1");
            }
            return events;
        }

        private static void ApplyPartialConsequence(RunState state, TurnRequest request, int severity,
            List<string> events)
        {
            switch (request.Ability)
            {
                case AbilityKind.Copy:
                    state.TechnicalDebt = RunState.ClampMetric(state.TechnicalDebt + severity);
                    events.Add("PARTIAL_COPY_DRIFT:severity=" + severity + ":clone=unstable");
                    break;
                case AbilityKind.Delete:
                    state.TechnicalDebt = RunState.ClampMetric(state.TechnicalDebt + severity);
                    state.IsExposed = true;
                    events.Add("PARTIAL_DELETE_BACKFLOW:severity=" + severity + ":player=exposed");
                    break;
                case AbilityKind.Connect:
                    state.TechnicalDebt = RunState.ClampMetric(state.TechnicalDebt + severity);
                    state.IsExposed = true;
                    events.Add("PARTIAL_CONNECT_HAZARD:severity=" + severity + ":bidirectional=true");
                    break;
                case AbilityKind.Restore:
                    state.TechnicalDebt = RunState.ClampMetric(state.TechnicalDebt + 1);
                    events.Add("PARTIAL_RESTORE_DEFECT:severity=1:past-defect=restored");
                    break;
                case AbilityKind.Undo:
                    state.TechnicalDebt = RunState.ClampMetric(state.TechnicalDebt + severity);
                    events.Add("PARTIAL_UNDO_PARADOX:severity=" + severity);
                    break;
                case AbilityKind.SelectAll:
                    int overload = Math.Min(1, state.Focus);
                    state.Focus -= overload;
                    events.Add("PARTIAL_SELECT_ALL_OVERLOAD:focus=-" + overload);
                    break;
                case AbilityKind.Search:
                    state.IsExposed = true;
                    events.Add("PARTIAL_SEARCH_NOISE:player=exposed");
                    break;
                case AbilityKind.Interact:
                    state.IsExposed = true;
                    events.Add("PARTIAL_INTERACTION_ATTENTION:player=exposed");
                    break;
                default:
                    state.IsExposed = true;
                    events.Add("PARTIAL_MOVE_EXPOSURE:player=exposed");
                    break;
            }
        }

        private static RulePreparation PrepareMove(RunState state, TurnRequest request, EntityState player)
        {
            if (!request.Destination.HasValue || !state.Region.IsWalkable(request.Destination.Value))
                return RulePreparation.Invalid(TurnErrorCode.DestinationInvalid, "Choose a walkable destination.");
            WorldArea destinationArea = state.Region.AreaAt(request.Destination.Value);
            if (destinationArea != null && destinationArea.RequiredAdminAccess > state.AdminAccess)
                return RulePreparation.Invalid(TurnErrorCode.QuestConditionMissing,
                    "ROOT_SYSTEM remains sealed until all three administrator access levels are acquired.");
            if (IsRootSystemArea(destinationArea) && !CampaignDirector.CanEnterRootSystem(state))
                return RulePreparation.Invalid(TurnErrorCode.QuestConditionMissing,
                    "ROOT_SYSTEM requires all three administrator access IDs and the internal-cause clue.");

            EntityState mover = player;
            if (request.TargetEntityId.HasValue && request.TargetEntityId.Value != player.EntityId)
            {
                if (!TryTarget(state, request.TargetEntityId, out mover))
                    return RulePreparation.Invalid(TurnErrorCode.EntityNotFound, "Choose an active movable target.");
                if (mover.IsProtected || mover.IsHostile || mover.Kind == EntityKind.Npc ||
                    (!mover.IsCloneable && mover.Kind != EntityKind.Effect))
                    return RulePreparation.Invalid(TurnErrorCode.ProtectedEntity, "That target cannot be relocated.");
                if (player.Position.ManhattanDistance(mover.Position) > 2)
                    return RulePreparation.Invalid(TurnErrorCode.OutOfRange, "Movable targets must be within 2 tiles.");
            }

            if (request.Destination.Value == mover.Position)
                return RulePreparation.Invalid(TurnErrorCode.DestinationInvalid, "The target is already on that tile.");
            List<GridCoord> path = GridPathfinder.FindPath(state.Region, mover.Position, request.Destination.Value,
                coord => state.Spatial.IsBlockingOccupied(state.Region.RegionId, coord, mover.Layer, mover.EntityId) ||
                         (state.Region.AreaAt(coord) != null &&
                          (state.Region.AreaAt(coord).RequiredAdminAccess > state.AdminAccess ||
                           IsRootSystemArea(state.Region.AreaAt(coord)) &&
                           !CampaignDirector.CanEnterRootSystem(state))));
            if (path.Count == 0)
                return RulePreparation.Invalid(TurnErrorCode.PathBlocked, "No legal path reaches that tile.");
            int budget = mover.EntityId == player.EntityId ? 7 : 4;
            if (path.Count - 1 > budget)
                return RulePreparation.Invalid(TurnErrorCode.OutOfRange, "Destination exceeds the movement budget.");
            int danger = state.Region.GetTile(request.Destination.Value).Kind == TileKind.Hazard ? 2 : 0;
            string subject = mover.EntityId == player.EntityId ? "player" : mover.DisplayName;
            return RulePreparation.Valid("Move " + subject + " to " + request.Destination.Value + " via " +
                (path.Count - 1) + " steps", 8 + danger, 4, 0, path, mover.EntityId);
        }

        private static RulePreparation PrepareCopy(RunState state, TurnRequest request, EntityState player)
        {
            if (state.Focus < 1)
                return RulePreparation.Invalid(TurnErrorCode.InsufficientResource, "Copy requires 1 focus.");
            if (!TryTarget(state, request.TargetEntityId, out EntityState target))
                return RulePreparation.Invalid(TurnErrorCode.EntityNotFound, "Choose an active source object.");
            if (!EntityCapabilityCatalog.CanCopy(target.Kind, target.IsProtected, target.IsCloneable))
                return RulePreparation.Invalid(TurnErrorCode.NotCloneable, "This object is not cloneable.");
            if (!request.Destination.HasValue || !state.Region.IsWalkable(request.Destination.Value) ||
                state.Spatial.IsBlockingOccupied(state.Region.RegionId, request.Destination.Value, target.Layer))
                return RulePreparation.Invalid(TurnErrorCode.DestinationInvalid, "Choose an empty walkable destination.");
            if (player.Position.ManhattanDistance(target.Position) > 4 ||
                player.Position.ManhattanDistance(request.Destination.Value) > 4)
                return RulePreparation.Invalid(TurnErrorCode.OutOfRange, "Copy source and destination must be within 4 tiles.");
            return RulePreparation.Valid("Copy " + target.DisplayName + " to " + request.Destination.Value,
                11, 4, 1, null, target.EntityId);
        }

        private static RulePreparation PrepareDelete(RunState state, TurnRequest request, EntityState player)
        {
            if (state.Focus < 1)
                return RulePreparation.Invalid(TurnErrorCode.InsufficientResource, "Delete requires 1 focus.");
            if (!TryTarget(state, request.TargetEntityId, out EntityState target))
                return RulePreparation.Invalid(TurnErrorCode.EntityNotFound, "Choose an active target.");
            if (!EntityCapabilityCatalog.CanDelete(target.Kind, target.IsHostile, target.AssetId))
                return RulePreparation.Invalid(TurnErrorCode.InvalidTarget,
                    "Delete requires a hostile enemy or a removable ROOT_SYSTEM component.");
            if (EnemyArchetypeCatalog.Resolve(target.AssetId, state.WorldSeed, target.EntityId) == EnemyDependencyArchetype.RootProcess &&
                !state.CanonicalFacts.Contains(EnemyArchetypeCatalog.RevealedFact(target.EntityId)))
                return RulePreparation.Invalid(TurnErrorCode.QuestConditionMissing,
                    "Root Process dependency must be revealed with Search before Delete.");
            int requiredAdminAccess = EntityCapabilityCatalog.RequiredAdminAccessForDelete(target.AssetId);
            if (state.AdminAccess < requiredAdminAccess)
                return RulePreparation.Invalid(TurnErrorCode.QuestConditionMissing,
                    "ROOT_SYSTEM components require administrator access 3/3 before removal.");
            if (player.Position.ManhattanDistance(target.Position) > 3)
                return RulePreparation.Invalid(TurnErrorCode.OutOfRange, "Delete target must be within 3 tiles.");
            return RulePreparation.Valid("Single-target attack on " + target.DisplayName, 13, 4, 1, null,
                target.EntityId);
        }

        private static RulePreparation PrepareConnect(RunState state, TurnRequest request, EntityState player)
        {
            if (state.Focus < 2)
                return RulePreparation.Invalid(TurnErrorCode.InsufficientResource, "Connect requires 2 focus.");
            if (!TryTarget(state, request.TargetEntityId, out EntityState first) ||
                !TryTarget(state, request.SecondaryTargetEntityId, out EntityState second) ||
                first.EntityId == second.EntityId)
                return RulePreparation.Invalid(TurnErrorCode.InvalidTarget, "Choose two distinct active endpoints.");
            if (!EntityCapabilityCatalog.CanConnect(first.IsActive, first.IsHostile) ||
                !EntityCapabilityCatalog.CanConnect(second.IsActive, second.IsHostile))
                return RulePreparation.Invalid(TurnErrorCode.InvalidTarget, "Hostile endpoints must be pacified first.");
            if ((first.AssetId.StartsWith("finale.", StringComparison.Ordinal) ||
                 second.AssetId.StartsWith("finale.", StringComparison.Ordinal)) && state.AdminAccess < 3)
                return RulePreparation.Invalid(TurnErrorCode.QuestConditionMissing,
                    "ROOT_SYSTEM links require all three administrator access levels.");
            if (player.Position.ManhattanDistance(first.Position) > 4 &&
                player.Position.ManhattanDistance(second.Position) > 4)
                return RulePreparation.Invalid(TurnErrorCode.OutOfRange, "Stand within 4 tiles of an endpoint.");
            if (first.Position.ManhattanDistance(second.Position) > 12)
                return RulePreparation.Invalid(TurnErrorCode.OutOfRange, "Endpoints are too far apart for a stable relation.");
            string connection = ConnectionKey(first.EntityId, second.EntityId);
            if (state.Connections.Contains(connection))
                return RulePreparation.Invalid(TurnErrorCode.InvalidTarget, "That relation already exists.");
            return RulePreparation.Valid("Connect " + first.DisplayName + " to " + second.DisplayName,
                12, 5, 2, null, first.EntityId);
        }

        private static RulePreparation PrepareRestore(RunState state, TurnRequest request, EntityState player)
        {
            if (state.Focus < 2)
                return RulePreparation.Invalid(TurnErrorCode.InsufficientResource, "Restore requires 2 focus.");
            if (TryTarget(state, request.TargetEntityId, out EntityState accessCandidate) &&
                EntityCapabilityCatalog.IsAdministratorAccessCandidate(accessCandidate.AssetId))
            {
                if (player.Position.ManhattanDistance(accessCandidate.Position) > 4)
                    return RulePreparation.Invalid(TurnErrorCode.OutOfRange,
                        "Administrator access repair target must be within 4 tiles.");
                return RulePreparation.Valid("Repair " + accessCandidate.DisplayName + " with RESTORE",
                    12, 5, 2, null, accessCandidate.EntityId);
            }
            RestorationRecord record = state.FindRestoration(request.TargetEntityId);
            if (record == null)
                return RulePreparation.Invalid(TurnErrorCode.NoRestorableSnapshot,
                    "No unconsumed damage/removal snapshot from the last 6 turns matches that target.");
            if (player.Position.ManhattanDistance(record.Snapshot.Position) > 4)
                return RulePreparation.Invalid(TurnErrorCode.OutOfRange, "Restore snapshot must be within 4 tiles.");
            if (!state.Spatial.CanRestore(record.Snapshot,
                    (regionId, coord) => regionId == state.Region.RegionId && state.Region.IsWalkable(coord),
                    out string error))
                return RulePreparation.Invalid(error == "TILE_OCCUPIED" ? TurnErrorCode.DestinationInvalid : TurnErrorCode.InvalidTarget,
                    "Snapshot cannot be restored: " + error);
            if (state.Spatial.TryGetEntity(record.Snapshot.EntityId, out EntityState current) &&
                current.IsActive == record.Snapshot.IsActive && current.Health == record.Snapshot.Health &&
                current.Position == record.Snapshot.Position && current.IsOpened == record.Snapshot.IsOpened)
                return RulePreparation.Invalid(TurnErrorCode.InvalidTarget, "The target already matches that snapshot.");
            return RulePreparation.Valid("Restore " + record.Snapshot.EntityId.ToString("N") +
                " from turn " + record.CapturedTurn, 13, 5, 2, null, record.Snapshot.EntityId);
        }

        private static RulePreparation PrepareUndo(RunState state)
        {
            if (state.Focus < 3)
                return RulePreparation.Invalid(TurnErrorCode.InsufficientResource, "Undo requires 3 focus.");
            int available = 0;
            int newestTurn = 0;
            int oldestTurn = 0;
            for (int i = state.ReversibleHistory.Count - 1; i >= 0 && available < 2; i--)
            {
                ReversibleTurnRecord candidate = state.ReversibleHistory[i];
                if (candidate.IsConsumed) continue;
                if (available == 0) newestTurn = candidate.SourceTurn;
                oldestTurn = candidate.SourceTurn;
                available++;
            }
            if (available < 2)
                return RulePreparation.Invalid(TurnErrorCode.UndoUnavailable,
                    "Ctrl Z requires two unconsumed meaningful turns to rewind.");
            return RulePreparation.Valid("Rewind turns " + oldestTurn + "-" + newestTurn +
                " and restore their mechanical state", 14, 5, 3);
        }

        private static RulePreparation PrepareSearch(RunState state, TurnRequest request, EntityState player)
        {
            if (state.Focus < 1)
                return RulePreparation.Invalid(TurnErrorCode.InsufficientResource, "Search requires 1 focus.");
            if (!TryTarget(state, request.TargetEntityId, out EntityState target))
                return RulePreparation.Invalid(TurnErrorCode.EntityNotFound,
                    "Choose one active object, enemy, or NPC to investigate.");
            if (player.Position.ManhattanDistance(target.Position) > 6)
                return RulePreparation.Invalid(TurnErrorCode.OutOfRange, "Search target must be within 6 tiles.");
            return RulePreparation.Valid("Investigate " + target.DisplayName + " with SEARCH",
                9, 5, 1, null, target.EntityId);
        }

        private static RulePreparation PrepareAreaAttack(RunState state, TurnRequest request, EntityState player)
        {
            if (state.Focus < 3)
                return RulePreparation.Invalid(TurnErrorCode.InsufficientResource, "Ctrl A requires 3 focus.");
            if (request.TargetEntityId.HasValue || request.SecondaryTargetEntityId.HasValue || request.Destination.HasValue)
                return RulePreparation.Invalid(TurnErrorCode.InvalidTarget,
                    "Ctrl A attacks every nearby enemy and does not take a selected target.");
            bool found = false;
            foreach (EntityState entity in state.Spatial.Entities)
                if (entity.IsActive && entity.IsHostile && entity.Kind == EntityKind.Enemy &&
                    player.Position.ManhattanDistance(entity.Position) <= 4) { found = true; break; }
            if (!found)
                return RulePreparation.Invalid(TurnErrorCode.InvalidTarget,
                    "No hostile enemy is within the 4-tile attack area.");
            return RulePreparation.Valid("Area attack within 4 tiles of " + player.Position, 14, 4, 3);
        }

        private static void ApplySearch(RunState state, TurnRequest request, RuleOutcome outcome, int turnNo, List<string> events)
        {
            if (!state.Spatial.TryGetEntity(request.TargetEntityId.Value, out EntityState target)) return;
            events.Add("SEARCH_REVEALED:" + target.EntityId);
            if (target.Kind == EntityKind.Npc)
                ApplyNpcInvestigation(state, request, outcome, turnNo, events);
            if (target.Kind == EntityKind.Enemy)
            {
                string fact = EnemyArchetypeCatalog.RevealedFact(target.EntityId);
                if (!state.CanonicalFacts.Contains(fact)) state.AddCanonicalFact(fact);
                events.Add("ENEMY_DEPENDENCY_REVEALED:" + target.EntityId + ":archetype=" +
                           EnemyArchetypeCatalog.Resolve(target.AssetId, state.WorldSeed, target.EntityId));
            }
            if (target.AssetId == CampaignCatalog.AdministratorKeyboardId)
            {
                state.AddCanonicalFact("관리자 키보드는 이미 존재하는 대상과 관계만 편집한다.");
                events.Add("ADMIN_KEYBOARD_AWAKENED:" + target.EntityId);
                events.Add("CATALYST_AWAKENED:" + target.EntityId);
            }
            if (target.AssetId.StartsWith("story.internal-failure", StringComparison.Ordinal))
            {
                state.AddCanonicalFact("붕괴 원인이 관리자 통제 시스템 내부에 있음을 확정했다.");
                events.Add("INTERNAL_FAILURE_CONFIRMED:" + target.EntityId);
            }
            if (target.AssetId.StartsWith("story.admin-access", StringComparison.Ordinal))
                events.Add("ADMIN_ACCESS_CANDIDATE_INSPECTED:" + target.EntityId);
        }

        private static void ApplyNpcInvestigation(RunState state, TurnRequest request, RuleOutcome outcome,
            int turnNo, List<string> events)
        {
            if (!request.TargetEntityId.HasValue ||
                !state.Spatial.TryGetEntity(request.TargetEntityId.Value, out EntityState target) ||
                target.Kind != EntityKind.Npc)
                return;
            NpcStoryState npc = state.FindNpcStory(target.EntityId);
            if (npc == null) return;
            npc.LastConversationTurn = turnNo;
            string clueId = "personal-secret";
            bool alreadyKnown = npc.RevealedClues.Contains(clueId);
            if (alreadyKnown)
            {
                npc.Remember("이미 털어놓은 이야기를 다시 묻자, " + npc.NpcName + "은 지금은 새로 덧붙일 말이 없다고 했다.");
                events.Add("NPC_INVESTIGATION_REPEAT:" + target.EntityId);
                return;
            }
            if (outcome == RuleOutcome.Success || outcome == RuleOutcome.CriticalSuccess)
            {
                npc.RevealedClues.Add(clueId);
                int trustDelta = outcome == RuleOutcome.CriticalSuccess ? 3 : 2;
                npc.Trust = Math.Min(10, npc.Trust + trustDelta);
                npc.Affinity = Math.Min(5, npc.Affinity + 1);
                string fact = npc.NpcName + "의 증언: " + npc.Secret;
                state.AddCanonicalFact(fact);
                npc.Remember(npc.NpcName + "은 잠시 주위를 살핀 뒤 털어놓았다. “" + npc.Secret + ".”");
                events.Add("NPC_CLUE_REVEALED:" + target.EntityId + ":trust=+" + trustDelta);
                return;
            }
            if (outcome == RuleOutcome.PartialSuccess)
            {
                npc.RevealedClues.Add(clueId);
                npc.Trust = Math.Min(10, npc.Trust + 1);
                npc.Fear = Math.Min(10, npc.Fear + 1);
                state.AddRumor(npc.Secret);
                npc.Remember(npc.NpcName + "은 확신하지 못한 채 목소리를 낮췄다. “" + npc.Secret + "… 직접 확인하기 전에는 믿지 마.”");
                events.Add("NPC_RUMOR_REVEALED:" + target.EntityId + ":trust=+1:fear=+1");
                return;
            }
            int fearDelta = outcome == RuleOutcome.CriticalFailure ? 2 : 1;
            npc.Fear = Math.Min(10, npc.Fear + fearDelta);
            if (outcome == RuleOutcome.CriticalFailure) npc.Trust = Math.Max(-10, npc.Trust - 1);
            npc.Remember(npc.NpcName + "은 시선을 피했다. “지금은 그 이야기를 할 수 없어. 먼저 내가 널 믿을 이유를 보여 줘.”");
            events.Add("NPC_INVESTIGATION_REFUSED:" + target.EntityId + ":fear=+" + fearDelta);
        }

        private static void ApplyAreaAttack(RunState state, List<string> events)
        {
            if (!state.Spatial.TryGetEntity(state.PlayerEntityId, out EntityState player)) return;
            var targets = new List<Guid>();
            foreach (EntityState entity in state.Spatial.Entities)
                if (entity.IsActive && entity.IsHostile && entity.Kind == EntityKind.Enemy &&
                    player.Position.ManhattanDistance(entity.Position) <= 4) targets.Add(entity.EntityId);
            int defeated = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                if (state.Spatial.TryGetEntity(targets[i], out EntityState snapshotTarget))
                    state.RecordRestorable(snapshotTarget, state.CurrentTurn + 1, "area_attack");
                if (!state.Spatial.TryDamage(targets[i], 3, out int health, out bool wasDefeated, out string error))
                    throw new InvalidOperationException("Validated area attack failed: " + error);
                events.Add("ENTITY_DAMAGED:" + targets[i] + ":3:hp=" + health);
                if (wasDefeated)
                {
                    defeated++;
                    if (state.Spatial.TryGetEntity(targets[i], out EntityState defeatedEnemy))
                        RewardEnemy(state, defeatedEnemy, events);
                }
            }
            events.Add("AREA_ATTACK_HIT_COUNT:" + targets.Count + ":DEFEATED=" + defeated);
        }

        private static RulePreparation PrepareInteract(RunState state, TurnRequest request, EntityState player)
        {
            if (request.SecondaryTargetEntityId.HasValue || request.Destination.HasValue ||
                !TryTarget(state, request.TargetEntityId, out EntityState target))
                return RulePreparation.Invalid(TurnErrorCode.InvalidTarget,
                    "Choose one active object or non-hostile NPC to interact with.");
            if ((target.Kind != EntityKind.Prop && target.Kind != EntityKind.Npc) || target.IsHostile)
                return RulePreparation.Invalid(TurnErrorCode.InvalidTarget,
                    "Only objects and non-hostile NPCs can be interacted with.");
            if (player.Position.ManhattanDistance(target.Position) > 2)
                return RulePreparation.Invalid(TurnErrorCode.OutOfRange,
                    "Interaction target must be within 2 tiles.");
            return RulePreparation.Valid("Interact with " + target.DisplayName, 8, 3, 0, null,
                target.EntityId);
        }

        private static void ApplyMove(RunState state, TurnRequest request, List<string> events)
        {
            Guid moverId = request.TargetEntityId.HasValue ? request.TargetEntityId.Value : state.PlayerEntityId;
            if (!state.Spatial.TryGetEntity(moverId, out EntityState mover) || !mover.IsActive)
                moverId = state.PlayerEntityId;
            MoveResult move = state.Spatial.TryMove(moverId, state.Region.RegionId, request.Destination.Value,
                mover.Layer, (regionId, coord) => regionId == state.Region.RegionId && state.Region.IsWalkable(coord));
            if (!move.IsSuccess)
                throw new InvalidOperationException("Validated move failed during commit: " + move.ErrorCode);
            events.Add("ENTITY_MOVED:" + moverId + ":" + move.From + "->" + move.To);
            WorldArea area = state.Region.AreaAt(move.To);
            if (moverId == state.PlayerEntityId && area != null)
                events.Add("AREA_ENTERED:" + area.Id);
        }

        private static void ApplyCopy(RunState state, TurnRequest request, int turnNo, List<string> events)
        {
            state.Spatial.TryGetEntity(request.TargetEntityId.Value, out EntityState source);
            Guid cloneId = DeterministicGuid.Create(state.RunId + ":" + turnNo + ":" + request.IdempotencyKey + ":copy");
            var clone = new EntityState(cloneId, source.Kind, source.AssetId, source.DisplayName + " Copy",
                source.IsBlocking, false, source.IsCloneable, false, source.MaxHealth,
                state.Region.RegionId, request.Destination.Value, source.Layer);
            if (!state.Spatial.Register(clone, out string error))
                throw new InvalidOperationException("Validated copy failed during commit: " + error);
            events.Add("ENTITY_SPAWNED:" + cloneId + ":" + source.AssetId);
        }

        private static void ApplyDelete(RunState state, TurnRequest request, int turnNo, List<string> events)
        {
            state.Spatial.TryGetEntity(request.TargetEntityId.Value, out EntityState target);
            state.RecordRestorable(target, turnNo, "single_target_attack");
            if (!state.Spatial.TryDamage(target.EntityId, 5, out int health, out bool defeated, out string error))
                throw new InvalidOperationException("Validated single-target attack failed during commit: " + error);
            events.Add("ENTITY_DAMAGED:" + target.EntityId + ":5:hp=" + health);
            if (defeated)
            {
                TryReplicateCacheEnemy(state, target, turnNo, request.IdempotencyKey, events);
                if (EntityCapabilityCatalog.GrantsDefeatReward(target.Kind, target.IsHostile))
                    RewardEnemy(state, target, events);
                else
                    events.Add("ENTITY_REMOVED:" + target.EntityId + ":asset=" + target.AssetId);
            }
        }

        private static void TryReplicateCacheEnemy(RunState state, EntityState target, int turnNo,
            string idempotencyKey, List<string> events)
        {
            if (target == null || EnemyArchetypeCatalog.Resolve(target.AssetId, state.WorldSeed, target.EntityId) !=
                EnemyDependencyArchetype.CacheReplicator ||
                state.CanonicalFacts.Contains(EnemyArchetypeCatalog.RevealedFact(target.EntityId)) ||
                state.CanonicalFacts.Contains(EnemyArchetypeCatalog.ReplicatedFact(target.EntityId))) return;
            GridCoord[] directions =
            {
                new GridCoord(1, 0), new GridCoord(0, 1), new GridCoord(-1, 0), new GridCoord(0, -1)
            };
            for (int i = 0; i < directions.Length; i++)
            {
                var position = new GridCoord(target.Position.X + directions[i].X,
                    target.Position.Y + directions[i].Y);
                if (!state.Region.IsWalkable(position) ||
                    state.Spatial.IsBlockingOccupied(state.Region.RegionId, position, target.Layer)) continue;
                Guid cloneId = DeterministicGuid.Create(state.RunId + ":cache-replica:" + turnNo + ":" + idempotencyKey);
                var replica = new EntityState(cloneId, EntityKind.Enemy, target.AssetId,
                    target.DisplayName + " Cache Copy", true, false, false, true,
                    Math.Max(2, target.MaxHealth - 1), state.Region.RegionId, position, target.Layer);
                if (!state.Spatial.Register(replica, out _)) continue;
                state.AddCanonicalFact(EnemyArchetypeCatalog.ReplicatedFact(target.EntityId));
                events.Add("CACHE_ENEMY_REPLICATED:" + target.EntityId + ":clone=" + cloneId +
                           ":reason=dependency-not-revealed");
                return;
            }
        }

        private static void ApplyConnect(RunState state, TurnRequest request, List<string> events)
        {
            string connection = ConnectionKey(request.TargetEntityId.Value, request.SecondaryTargetEntityId.Value);
            state.Connections.Add(connection);
            events.Add("CONNECTION_CREATED:" + connection);
            RecordConnectedNpc(state, request.TargetEntityId.Value, events);
            RecordConnectedNpc(state, request.SecondaryTargetEntityId.Value, events);
        }

        private static void ApplyRestore(RunState state, RulePreparation preparation, List<string> events)
        {
            RestorationRecord record = state.FindRestoration(preparation.ResolvedTargetEntityId);
            if (record == null && preparation.ResolvedTargetEntityId.HasValue &&
                state.Spatial.TryGetEntity(preparation.ResolvedTargetEntityId.Value, out EntityState accessCandidate) &&
                accessCandidate.AssetId.StartsWith("story.admin-access", StringComparison.Ordinal))
            {
                events.Add("ADMIN_ACCESS_CANDIDATE_REPAIRED:" + accessCandidate.EntityId);
                return;
            }
            if (record == null)
                throw new InvalidOperationException("Validated restoration snapshot disappeared during commit.");
            if (!state.Spatial.TryRestore(record.Snapshot,
                    (regionId, coord) => regionId == state.Region.RegionId && state.Region.IsWalkable(coord),
                    out string error))
                throw new InvalidOperationException("Validated restore failed during commit: " + error);
            record.IsConsumed = true;
            events.Add("ENTITY_RESTORED:" + record.Snapshot.EntityId + ":from-turn=" + record.CapturedTurn);
        }

        private static void ApplyInteract(RunState state, TurnRequest request, List<string> events)
        {
            if (!state.Spatial.TryGetEntity(request.TargetEntityId.Value, out EntityState target))
                throw new InvalidOperationException("Validated interaction target disappeared during commit.");
            bool firstInteraction = !target.IsOpened;
            if (firstInteraction && !state.Spatial.TryOpen(target.EntityId, out string error))
                throw new InvalidOperationException("Validated interaction failed during commit: " + error);
            events.Add("ENTITY_INTERACTED:" + target.EntityId + ":" + target.DisplayName);
            if (firstInteraction && target.AssetId.StartsWith("item.crate", StringComparison.Ordinal))
            {
                int before = state.Focus;
                state.Focus = Math.Min(state.MaxFocus, state.Focus + 1);
                if (state.Focus > before) events.Add("RESOURCE_CHANGED:focus:+1");
            }
            else if (!firstInteraction && target.AssetId.StartsWith("item.crate", StringComparison.Ordinal))
            {
                const int price = 2;
                if (state.Gold >= price && state.Focus < state.MaxFocus)
                {
                    int before = state.Focus;
                    state.Gold -= price;
                    state.Focus = Math.Min(state.MaxFocus, state.Focus + 2);
                    events.Add("RESOURCE_CHANGED:gold:-" + price);
                    events.Add("RESOURCE_CHANGED:focus:+" + (state.Focus - before));
                    events.Add("SUPPLY_PURCHASED:focus:price=" + price);
                }
                else
                {
                    events.Add(state.Gold < price
                        ? "SUPPLY_PURCHASE_REJECTED:gold-required=" + price
                        : "SUPPLY_PURCHASE_REJECTED:focus-full");
                }
            }
        }

        private static void ApplyUndo(RunState state, int focusCost, List<string> events)
        {
            var records = new List<ReversibleTurnRecord>();
            for (int i = state.ReversibleHistory.Count - 1; i >= 0 && records.Count < 2; i--)
                if (!state.ReversibleHistory[i].IsConsumed) records.Add(state.ReversibleHistory[i]);
            if (records.Count < 2)
                throw new InvalidOperationException("Validated two-turn rewind history disappeared during commit.");

            for (int i = 0; i < records.Count; i++)
                ApplyReversibleRecord(state, records[i], events);

            ReversibleTurnRecord oldest = records[records.Count - 1];
            state.Focus = Math.Max(0, oldest.FocusBefore - focusCost);
            events.Add("UNDO_COMPENSATION_COMPLETED:turns=2:newest=" + records[0].SourceTurn + ":oldest=" + oldest.SourceTurn);
            events.Add("RESOURCE_CHANGED:focus:-" + focusCost);
        }

        private static void ApplyReversibleRecord(RunState state, ReversibleTurnRecord record, List<string> events)
        {

            var snapshotsById = new Dictionary<Guid, EntityRuntimeSnapshot>();
            for (int i = 0; i < record.EntitySnapshots.Count; i++)
                snapshotsById[record.EntitySnapshots[i].EntityId] = record.EntitySnapshots[i];
            var excluded = new HashSet<Guid>(record.IrreversibleEntityIds);
            var current = new List<EntityState>(state.Spatial.Entities);

            // Clear reversible occupancy first so entities can return to their prior cells atomically.
            for (int i = 0; i < current.Count; i++)
            {
                EntityState entity = current[i];
                bool existedBefore = snapshotsById.ContainsKey(entity.EntityId);
                bool spawnedDuringTurn = !existedBefore;
                if (!entity.IsActive || excluded.Contains(entity.EntityId) || (!existedBefore && !spawnedDuringTurn))
                    continue;
                if (entity.IsProtected && spawnedDuringTurn)
                    continue;
                if (!state.Spatial.TryRemove(entity.EntityId, out string removeError))
                    throw new InvalidOperationException("Undo occupancy clear failed: " + removeError);
            }

            for (int i = 0; i < record.EntitySnapshots.Count; i++)
            {
                EntityRuntimeSnapshot snapshot = record.EntitySnapshots[i];
                if (excluded.Contains(snapshot.EntityId))
                    continue;
                if (!state.Spatial.TryRestore(snapshot,
                        (regionId, coord) => regionId == state.Region.RegionId && state.Region.IsWalkable(coord),
                        out string restoreError))
                    throw new InvalidOperationException("Undo compensation failed: " + restoreError);
            }

            state.Focus = record.FocusBefore;
            state.IsExposed = record.WasExposedBefore;
            if (record.RestoreEconomy)
            {
                state.Gold = record.GoldBefore;
                state.Experience = record.ExperienceBefore;
            }
            if (record.RestoreInventory)
            {
                state.Inventory.Clear();
                state.Inventory.AddRange(record.InventoryBefore);
            }
            if (record.RestoreConnections)
            {
                state.Connections.Clear();
                state.Connections.AddRange(record.ConnectionsBefore);
            }
            record.IsConsumed = true;
            events.Add("TURN_COMPENSATED:source-turn=" + record.SourceTurn + ":ability=" + record.SourceAbility);
        }

        private static void RewardEnemy(RunState state, EntityState enemy, List<string> events)
        {
            bool firstReward = state.RewardedEnemyIds.Add(enemy.EntityId);
            if (!firstReward)
            {
                events.Add("ENEMY_DEFEATED:" + enemy.EntityId + ":reward=already-claimed");
                return;
            }
            state.EnemiesDefeated++;
            int experience = enemy.AssetId.Contains("mushroom") ? 4 : 3;
            int masteryBefore = state.Experience / 10;
            state.Experience += experience;
            state.Gold += 2;
            int masteryAfter = state.Experience / 10;
            if (masteryAfter > masteryBefore)
            {
                int increase = masteryAfter - masteryBefore;
                state.MaxFocus += increase;
                state.Focus = Math.Min(state.MaxFocus, state.Focus + increase);
                events.Add("MASTERY_RANK_INCREASED:" + masteryAfter + ":max-focus=+" + increase);
            }
            events.Add("ENEMY_DEFEATED:" + enemy.EntityId);
            events.Add("DEFEAT_REWARD_GRANTED:" + enemy.EntityId);
            events.Add("IRREVERSIBLE_ENTITY:" + enemy.EntityId);
            events.Add("RESOURCE_CHANGED:xp:+" + experience);
            events.Add("RESOURCE_CHANGED:gold:+2");
        }

        private static bool IsApplied(RuleOutcome outcome)
        {
            return outcome == RuleOutcome.PartialSuccess || outcome == RuleOutcome.Success ||
                   outcome == RuleOutcome.CriticalSuccess;
        }

        private static void RecordConnectedNpc(RunState state, Guid entityId, List<string> events)
        {
            if (!state.Spatial.TryGetEntity(entityId, out EntityState entity) || entity.Kind != EntityKind.Npc)
                return;
            state.RecordNpcMemory(entity.EntityId,
                "넙죽이가 강제 대신 CONNECT로 공동 해결 관계를 만들었다.", 1);
            events.Add("NPC_RELATIONSHIP_CHANGED:" + entity.EntityId + ":affinity=+1");
        }

        private static bool IsRootSystemArea(WorldArea area)
        {
            return area != null && string.Equals(area.CampaignRole, CampaignCatalog.RootSystemAxis,
                StringComparison.Ordinal);
        }

        private static bool TryTarget(RunState state, Guid? id, out EntityState target)
        {
            target = null;
            return id.HasValue && state.Spatial.TryGetEntity(id.Value, out target) && target.IsActive;
        }

        private static string ConnectionKey(Guid first, Guid second)
        {
            string left = first.ToString("N");
            string right = second.ToString("N");
            return string.CompareOrdinal(left, right) <= 0 ? left + ":" + right : right + ":" + left;
        }
    }
}
