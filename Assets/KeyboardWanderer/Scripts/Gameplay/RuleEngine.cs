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
                case AbilityKind.Search: preparation = PrepareAreaSkill(state, request, player, false); break;
                case AbilityKind.SelectAll: preparation = PrepareAreaSkill(state, request, player, true); break;
                default: preparation = RulePreparation.Invalid(TurnErrorCode.InvalidRequest, "Unknown ability."); break;
            }
            // Free text is never rules authority. Skill, targets, location, and scene state alone
            // determine legality, modifier, difficulty, and outcome.
            return preparation;
        }

        public static ActionContext ClassifyAction(RunState state, TurnRequest request)
        {
            if (request == null) return ActionContext.None;
            if (request.InputType == PlayerInputType.MOVE || request.Ability == AbilityKind.Move)
                return ActionContext.SafeTravel;
            switch (request.Ability)
            {
                case AbilityKind.Copy:
                case AbilityKind.Search:
                    return ActionContext.Investigation;
                case AbilityKind.Interact:
                    if (TryTarget(state, request.TargetEntityId, out EntityState interactTarget) &&
                        interactTarget.Kind == EntityKind.Npc) return ActionContext.Negotiation;
                    return ActionContext.Investigation;
                case AbilityKind.Restore:
                case AbilityKind.SelectAll:
                case AbilityKind.Undo: return ActionContext.Deployment;
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
            RuleOutcome outcome, int turnNo)
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
                    case AbilityKind.Search: ApplyAreaScan(state, 6, "SEARCH_REVEALED", events); break;
                    case AbilityKind.SelectAll: ApplyAreaScan(state, 4, "AREA_SELECTED", events); break;
                }
            }
            else if (request.Ability == AbilityKind.Undo && preparation.FocusCost > 0)
            {
                state.Focus = Math.Max(0, state.Focus - preparation.FocusCost);
                events.Add("RESOURCE_CHANGED:focus:-" + preparation.FocusCost);
            }

            if (outcome == RuleOutcome.PartialSuccess)
            {
                state.IsExposed = true;
                events.Add("STATUS_ADDED:player:exposed:1");
            }
            else if (outcome == RuleOutcome.CriticalSuccess)
            {
                state.IsExposed = false;
                state.Focus = Math.Min(state.MaxFocus, state.Focus + 1);
                events.Add("RESOURCE_CHANGED:focus:+1");
            }
            return events;
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
            bool investigationCopy = target.AssetId == CampaignCatalog.AdministratorKeyboardId ||
                                     target.AssetId.StartsWith("story.admin-access", StringComparison.Ordinal) ||
                                     target.AssetId.StartsWith("story.internal-failure", StringComparison.Ordinal);
            if (target.IsProtected && !investigationCopy)
                return RulePreparation.Invalid(TurnErrorCode.ProtectedEntity, "Protected objects cannot be copied.");
            if ((!target.IsCloneable && !investigationCopy) || target.Kind == EntityKind.Player || target.Kind == EntityKind.Npc)
                return RulePreparation.Invalid(TurnErrorCode.NotCloneable, "This object is not cloneable.");
            if (investigationCopy)
            {
                if (player.Position.ManhattanDistance(target.Position) > 4)
                    return RulePreparation.Invalid(TurnErrorCode.OutOfRange,
                        "Investigation target must be within 4 tiles.");
                return RulePreparation.Valid("Inspect " + target.DisplayName + " with COPY without changing geometry",
                    10, 4, 1, null, target.EntityId);
            }
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
            if (target.EntityId == state.PlayerEntityId || target.IsProtected || target.Kind == EntityKind.Npc)
                return RulePreparation.Invalid(TurnErrorCode.ProtectedEntity, "Protected entities cannot be deleted.");
            if ((target.AssetId == "finale.threat" || target.AssetId == "finale.freedom") &&
                state.AdminAccess < 3)
                return RulePreparation.Invalid(TurnErrorCode.QuestConditionMissing,
                    "ROOT_SYSTEM components require administrator access 3/3 before removal.");
            if (target.Kind == EntityKind.Prop && !target.IsCloneable && target.Health > 2)
                return RulePreparation.Invalid(TurnErrorCode.InvalidTarget, "Only temporary or weakened props can be deleted.");
            if (player.Position.ManhattanDistance(target.Position) > 3)
                return RulePreparation.Invalid(TurnErrorCode.OutOfRange, "Delete target must be within 3 tiles.");
            int difficulty = target.Kind == EntityKind.Enemy ? 13 : 12;
            return RulePreparation.Valid("Delete " + target.DisplayName, difficulty, 4, 1, null,
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
            if (first.IsHostile || second.IsHostile)
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
                accessCandidate.AssetId.StartsWith("story.admin-access", StringComparison.Ordinal))
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
            ReversibleTurnRecord record = state.LastReversibleTurn;
            if (record == null || record.IsConsumed || record.SourceTurn != state.CurrentTurn)
                return RulePreparation.Invalid(TurnErrorCode.UndoUnavailable,
                    "Only the immediately preceding unconsumed reversible turn can be compensated.");
            return RulePreparation.Valid("Append compensation for turn " + record.SourceTurn +
                " without rewinding time, rolls, layout, or story facts", 14, 5, 3);
        }

        private static RulePreparation PrepareAreaSkill(RunState state, TurnRequest request,
            EntityState player, bool selectAll)
        {
            int cost = selectAll ? 3 : 1;
            if (state.Focus < cost)
                return RulePreparation.Invalid(TurnErrorCode.InsufficientResource,
                    (selectAll ? "Ctrl A" : "Ctrl F") + " requires " + cost + " focus.");
            if (request.TargetEntityId.HasValue || request.SecondaryTargetEntityId.HasValue || request.Destination.HasValue)
                return RulePreparation.Invalid(TurnErrorCode.InvalidTarget,
                    (selectAll ? "Ctrl A" : "Ctrl F") + " does not require a target.");
            int radius = selectAll ? 4 : 6;
            return RulePreparation.Valid((selectAll ? "Select administrator area" : "Search nearby entities") +
                " within " + radius + " tiles of " + player.Position,
                selectAll ? 14 : 9, selectAll ? 4 : 5, cost);
        }

        private static void ApplyAreaScan(RunState state, int radius, string eventName, List<string> events)
        {
            if (!state.Spatial.TryGetEntity(state.PlayerEntityId, out EntityState player)) return;
            int count = 0;
            foreach (EntityState entity in state.Spatial.Entities)
            {
                if (!entity.IsActive || entity.EntityId == state.PlayerEntityId ||
                    player.Position.ManhattanDistance(entity.Position) > radius) continue;
                events.Add(eventName + ":" + entity.EntityId);
                count++;
            }
            events.Add(eventName + "_COUNT:" + count);
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
            if (source.AssetId == CampaignCatalog.AdministratorKeyboardId)
            {
                state.AddCanonicalFact("관리자 키보드는 이미 존재하는 대상과 관계만 편집한다.");
                events.Add("ADMIN_KEYBOARD_AWAKENED:" + source.EntityId);
                events.Add("CATALYST_AWAKENED:" + source.EntityId);
                return;
            }
            if (source.AssetId.StartsWith("story.internal-failure", StringComparison.Ordinal))
            {
                state.AddCanonicalFact("붕괴 원인이 관리자 통제 시스템 내부에 있음을 확정했다.");
                events.Add("INTERNAL_FAILURE_CONFIRMED:" + source.EntityId);
                return;
            }
            if (source.AssetId.StartsWith("story.admin-access", StringComparison.Ordinal))
            {
                events.Add("ADMIN_ACCESS_CANDIDATE_INSPECTED:" + source.EntityId);
                return;
            }
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
            state.RecordRestorable(target, turnNo, "deleted");
            if (!state.Spatial.TryRemove(target.EntityId, out string error))
                throw new InvalidOperationException("Validated delete failed during commit: " + error);
            if (target.Kind == EntityKind.Enemy)
                RewardEnemy(state, target, events);
            events.Add("ENTITY_REMOVED:" + target.EntityId);
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
        }

        private static void ApplyUndo(RunState state, int focusCost, List<string> events)
        {
            ReversibleTurnRecord record = state.LastReversibleTurn;
            if (record == null)
                throw new InvalidOperationException("Validated undo record disappeared during commit.");

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

            state.Focus = Math.Max(0, record.FocusBefore - focusCost);
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
            events.Add("UNDO_COMPENSATION:source-turn=" + record.SourceTurn + ":ability=" + record.SourceAbility);
            events.Add("RESOURCE_CHANGED:focus:-" + focusCost);
        }

        private static void RewardEnemy(RunState state, EntityState enemy, List<string> events)
        {
            state.EnemiesDefeated++;
            int experience = enemy.AssetId.Contains("mushroom") ? 4 : 3;
            state.Experience += experience;
            state.Gold += 2;
            events.Add("ENEMY_DEFEATED:" + enemy.EntityId);
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
