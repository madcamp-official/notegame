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
                    "Idempotency key and an intent of at most 500 characters are required.");
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
                case AbilityKind.Attack: preparation = PrepareAttack(state, request, player); break;
                case AbilityKind.Interact: preparation = PrepareInteract(state, request, player); break;
                case AbilityKind.Rest: preparation = PrepareRest(state, player); break;
                case AbilityKind.Negotiate: preparation = PrepareNegotiate(state, request, player); break;
                default: preparation = RulePreparation.Invalid(TurnErrorCode.InvalidRequest, "Unknown ability."); break;
            }
            return preparation.WithIntentAlignment(ScoreIntentAlignment(state, request, preparation));
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
                   preparation.Difficulty + " = " + score + "; 의도 정렬 " + preparation.IntentAlignment +
                   "/3으로 " + outcome + " 판정.";
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
                    case AbilityKind.Attack: ApplyAttack(state, request, outcome, turnNo, events); break;
                    case AbilityKind.Interact: ApplyInteraction(state, request, outcome, events); break;
                    case AbilityKind.Rest: ApplyRest(state, outcome, events); break;
                    case AbilityKind.Negotiate: ApplyNegotiate(state, request, outcome, turnNo, events); break;
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
            if (destinationArea != null && destinationArea.RequiredAdminAccess > state.MilestoneProgress)
                return RulePreparation.Invalid(TurnErrorCode.QuestConditionMissing,
                    "The final convergence remains sealed until all three milestone tokens are acquired.");

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
                          state.Region.AreaAt(coord).RequiredAdminAccess > state.MilestoneProgress));
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
            bool designatedMilestoneCopy = target.AssetId == "story.milestone-token-1";
            if (target.IsProtected && !designatedMilestoneCopy)
                return RulePreparation.Invalid(TurnErrorCode.ProtectedEntity, "Protected objects cannot be copied.");
            if ((!target.IsCloneable && !designatedMilestoneCopy) || target.Kind == EntityKind.Player || target.Kind == EntityKind.Npc)
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
            if (target.EntityId == state.PlayerEntityId || target.IsProtected || target.Kind == EntityKind.Npc)
                return RulePreparation.Invalid(TurnErrorCode.ProtectedEntity, "Protected entities cannot be deleted.");
            if ((target.AssetId == "finale.threat" || target.AssetId == "finale.freedom") &&
                state.MilestoneProgress < 3)
                return RulePreparation.Invalid(TurnErrorCode.QuestConditionMissing,
                    "Finale components require milestone progress 3/3 before removal.");
            if (target.Kind == EntityKind.Enemy && target.Health > 2)
                return RulePreparation.Invalid(TurnErrorCode.TargetTooHealthy, "Damage this enemy to 2 HP or less first.");
            if (target.Kind == EntityKind.Prop && !target.IsCloneable && target.Health > 2)
                return RulePreparation.Invalid(TurnErrorCode.InvalidTarget, "Only temporary or weakened props can be deleted.");
            if (player.Position.ManhattanDistance(target.Position) > 3)
                return RulePreparation.Invalid(TurnErrorCode.OutOfRange, "Delete target must be within 3 tiles.");
            return RulePreparation.Valid("Delete " + target.DisplayName, 12, 4, 1, null, target.EntityId);
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
                 second.AssetId.StartsWith("finale.", StringComparison.Ordinal)) && state.MilestoneProgress < 3)
                return RulePreparation.Invalid(TurnErrorCode.QuestConditionMissing,
                    "Finale links require all three milestone tokens.");
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

        private static RulePreparation PrepareAttack(RunState state, TurnRequest request, EntityState player)
        {
            if (!TryTarget(state, request.TargetEntityId, out EntityState target) ||
                !target.IsHostile || target.Kind != EntityKind.Enemy)
                return RulePreparation.Invalid(TurnErrorCode.InvalidTarget, "Choose a living hostile target.");
            if (player.Position.ManhattanDistance(target.Position) > 1)
                return RulePreparation.Invalid(TurnErrorCode.OutOfRange, "Attack targets must be adjacent.");
            return RulePreparation.Valid("Strike " + target.DisplayName, 9, 5, 0, null, target.EntityId);
        }

        private static RulePreparation PrepareInteract(RunState state, TurnRequest request, EntityState player)
        {
            if (!TryTarget(state, request.TargetEntityId, out EntityState target) || target.IsHostile)
                return RulePreparation.Invalid(TurnErrorCode.InvalidTarget,
                    "Choose a nearby person, container, clue, or campaign anchor.");
            if (player.Position.ManhattanDistance(target.Position) > 2)
                return RulePreparation.Invalid(TurnErrorCode.OutOfRange, "Interaction target must be within 2 tiles.");
            if (target.IsOpened)
                return RulePreparation.Invalid(TurnErrorCode.AlreadyOpened, "That container has already been opened.");
            if (target.AssetId.StartsWith("finale.", StringComparison.Ordinal) && state.MilestoneProgress < 3)
                return RulePreparation.Invalid(TurnErrorCode.QuestConditionMissing,
                    "The final convergence rejects interaction before milestone progress reaches 3/3.");
            return RulePreparation.Valid("Interact with " + target.DisplayName, 8, 5, 0, null, target.EntityId);
        }

        private static RulePreparation PrepareRest(RunState state, EntityState player)
        {
            if (player.Health >= player.MaxHealth && state.Focus >= state.MaxFocus)
                return RulePreparation.Invalid(TurnErrorCode.InvalidRequest, "Health and focus are already full.");
            return RulePreparation.Valid("Catch a breath and restore strength", 7, 5, 0);
        }

        private static RulePreparation PrepareNegotiate(RunState state, TurnRequest request, EntityState player)
        {
            if (!TryTarget(state, request.TargetEntityId, out EntityState target) || target.Kind != EntityKind.Npc || target.IsHostile)
                return RulePreparation.Invalid(TurnErrorCode.InvalidTarget,
                    "Choose a nearby non-hostile NPC to negotiate with.");
            if (player.Position.ManhattanDistance(target.Position) > 2)
                return RulePreparation.Invalid(TurnErrorCode.OutOfRange,
                    "Negotiation target must be within 2 tiles.");
            return RulePreparation.Valid("Negotiate terms with " + target.DisplayName, 10, 4, 0, null,
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
        }

        private static void ApplyRestore(RunState state, RulePreparation preparation, List<string> events)
        {
            RestorationRecord record = state.FindRestoration(preparation.ResolvedTargetEntityId);
            if (record == null)
                throw new InvalidOperationException("Validated restoration snapshot disappeared during commit.");
            if (!state.Spatial.TryRestore(record.Snapshot,
                    (regionId, coord) => regionId == state.Region.RegionId && state.Region.IsWalkable(coord),
                    out string error))
                throw new InvalidOperationException("Validated restore failed during commit: " + error);
            record.IsConsumed = true;
            events.Add("ENTITY_RESTORED:" + record.Snapshot.EntityId + ":from-turn=" + record.CapturedTurn);
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

        private static void ApplyAttack(RunState state, TurnRequest request, RuleOutcome outcome, int turnNo,
            List<string> events)
        {
            int damage = outcome == RuleOutcome.CriticalSuccess ? 4 : outcome == RuleOutcome.Success ? 3 : 1;
            state.Spatial.TryGetEntity(request.TargetEntityId.Value, out EntityState target);
            state.RecordRestorable(target, turnNo, "damaged");
            if (!state.Spatial.TryDamage(target.EntityId, damage, out int remaining, out bool defeated,
                    out string error))
                throw new InvalidOperationException("Validated attack failed during commit: " + error);
            events.Add("ENTITY_DAMAGED:" + target.EntityId + ":" + damage + ":hp=" + remaining);
            if (defeated)
                RewardEnemy(state, target, events);
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

        private static void ApplyInteraction(RunState state, TurnRequest request, RuleOutcome outcome,
            List<string> events)
        {
            state.Spatial.TryGetEntity(request.TargetEntityId.Value, out EntityState target);
            if (target.Kind == EntityKind.Npc)
            {
                int affinity = outcome == RuleOutcome.CriticalSuccess ? 2 : outcome == RuleOutcome.Success ? 1 : 0;
                state.RecordNpcMemory(target.EntityId, string.IsNullOrWhiteSpace(request.IntentText)
                    ? "플레이어가 말없이 다가왔다."
                    : "플레이어가 \"" + request.IntentText.Trim() + "\"라고 의도를 밝혔다.", affinity);
                events.Add("NPC_INTERACTED:" + target.EntityId + ":affinity=+" + affinity);
            }

            switch (target.AssetId)
            {
                case "artifact.keyboard":
                    state.AddCanonicalFact("키보드 유물은 이미 존재하는 대상의 상태와 관계만 편집할 수 있다.");
                    events.Add("CATALYST_AWAKENED:" + target.EntityId);
                    break;
                case "story.hidden-truth":
                case "story.hidden-truth.backup":
                    state.AddCanonicalFact("감춰진 기록은 현재 위기가 외부 침입이 아니라 세계 내부의 과거 선택에서 비롯되었음을 보여 준다.");
                    events.Add("HIDDEN_TRUTH_CONFIRMED:" + target.EntityId);
                    break;
                case "finale.anchor":
                    events.Add("FINALE_PUZZLE_INSPECTED:" + target.EntityId);
                    break;
                default:
                    events.Add(target.AssetId.StartsWith("story.milestone-token", StringComparison.Ordinal)
                        ? "MILESTONE_ANCHOR_INSPECTED:" + target.EntityId
                        : "ENTITY_INSPECTED:" + target.EntityId);
                    break;
            }
        }

        private static void ApplyRest(RunState state, RuleOutcome outcome, List<string> events)
        {
            int healing = outcome == RuleOutcome.CriticalSuccess ? 5 : outcome == RuleOutcome.Success ? 3 : 2;
            state.Spatial.TryHeal(state.PlayerEntityId, healing, out int health, out _);
            int focusGain = outcome == RuleOutcome.CriticalSuccess ? 3 : 2;
            state.Focus = Math.Min(state.MaxFocus, state.Focus + focusGain);
            events.Add("PLAYER_HEALED:hp=" + health);
            events.Add("RESOURCE_CHANGED:focus:+" + focusGain);
        }

        private static void ApplyNegotiate(RunState state, TurnRequest request, RuleOutcome outcome, int turnNo,
            List<string> events)
        {
            if (!TryTarget(state, request.TargetEntityId, out EntityState target))
                throw new InvalidOperationException("Validated negotiation target disappeared.");
            int affinity = outcome == RuleOutcome.CriticalSuccess ? 2 : 1;
            string actorName = state.Spatial.TryGetEntity(state.PlayerEntityId, out EntityState player)
                ? player.DisplayName
                : "방랑자";
            state.RecordNpcMemory(target.EntityId,
                actorName + "가 " + turnNo + "번째 의미 있는 턴에 강제가 아닌 협상으로 해결책을 제안했다.", affinity);
            events.Add("NEGOTIATION_RESOLVED:" + target.EntityId + ":affinity=+" + affinity);
        }

        private static int ScoreIntentAlignment(RunState state, TurnRequest request, RulePreparation preparation)
        {
            if (!preparation.IsValid || string.IsNullOrWhiteSpace(request.IntentText))
                return 0;
            string intent = request.IntentText.Trim().ToLowerInvariant();
            int score = intent.Length >= 8 ? 1 : 0;
            string ability = request.Ability.ToString().ToLowerInvariant();
            if (intent.Contains(ability) || ContainsKoreanAbility(intent, request.Ability))
                score++;
            Guid? targetId = preparation.ResolvedTargetEntityId ?? request.TargetEntityId;
            if (targetId.HasValue && state.Spatial.TryGetEntity(targetId.Value, out EntityState target))
            {
                string name = (target.DisplayName ?? string.Empty).ToLowerInvariant();
                if (name.Length > 0 && intent.Contains(name))
                    score++;
            }
            else if (request.Destination.HasValue && (intent.Contains("좌표") || intent.Contains("가") || intent.Contains("move")))
            {
                score++;
            }
            return Math.Max(0, Math.Min(3, score));
        }

        private static bool ContainsKoreanAbility(string intent, AbilityKind ability)
        {
            switch (ability)
            {
                case AbilityKind.Move: return intent.Contains("이동") || intent.Contains("옮");
                case AbilityKind.Copy: return intent.Contains("복사") || intent.Contains("복제");
                case AbilityKind.Delete: return intent.Contains("삭제") || intent.Contains("지우");
                case AbilityKind.Connect: return intent.Contains("연결") || intent.Contains("잇");
                case AbilityKind.Restore: return intent.Contains("복구") || intent.Contains("회복") || intent.Contains("되돌");
                case AbilityKind.Undo: return intent.Contains("실행 취소") || intent.Contains("취소") || intent.Contains("보상");
                case AbilityKind.Attack: return intent.Contains("공격") || intent.Contains("때리");
                case AbilityKind.Interact: return intent.Contains("대화") || intent.Contains("상호작용") || intent.Contains("조사");
                case AbilityKind.Rest: return intent.Contains("휴식") || intent.Contains("쉬");
                case AbilityKind.Negotiate: return intent.Contains("협상") || intent.Contains("타협") || intent.Contains("설득");
                default: return false;
            }
        }

        private static bool IsApplied(RuleOutcome outcome)
        {
            return outcome == RuleOutcome.PartialSuccess || outcome == RuleOutcome.Success ||
                   outcome == RuleOutcome.CriticalSuccess;
        }

        private static void OpenOrThrow(RunState state, EntityState target)
        {
            if (!state.Spatial.TryOpen(target.EntityId, out string error))
                throw new InvalidOperationException("Validated interaction failed during commit: " + error);
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
