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

        public SeededD20Source(int seed)
        {
            _random = new Random(seed);
        }

        public int Roll()
        {
            return _random.Next(1, 21);
        }
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
        public IReadOnlyList<GridCoord> Path { get; }

        private RulePreparation(bool isValid, TurnErrorCode errorCode, string errorMessage, string normalizedAttempt,
            int difficulty, int modifier, int focusCost, IReadOnlyList<GridCoord> path)
        {
            IsValid = isValid;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            NormalizedAttempt = normalizedAttempt;
            Difficulty = difficulty;
            Modifier = modifier;
            FocusCost = focusCost;
            Path = path ?? Array.Empty<GridCoord>();
        }

        public static RulePreparation Invalid(TurnErrorCode code, string message)
        {
            return new RulePreparation(false, code, message, string.Empty, 0, 0, 0, null);
        }

        public static RulePreparation Valid(string normalizedAttempt, int difficulty, int modifier, int focusCost, IReadOnlyList<GridCoord> path = null)
        {
            return new RulePreparation(true, TurnErrorCode.None, null, normalizedAttempt, difficulty, modifier, focusCost, path);
        }
    }

    public sealed class RuleEngine
    {
        public RulePreparation Prepare(RunState state, TurnRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IntentText.Length > 500)
                return RulePreparation.Invalid(TurnErrorCode.InvalidRequest, "Idempotency key and an intent under 500 characters are required.");
            if (!state.Spatial.TryGetEntity(state.PlayerEntityId, out EntityState player) || !player.IsActive)
                return RulePreparation.Invalid(TurnErrorCode.EntityNotFound, "Player entity is missing.");

            switch (request.Ability)
            {
                case AbilityKind.Move:
                    return PrepareMove(state, request, player);
                case AbilityKind.Copy:
                    return PrepareCopy(state, request, player);
                case AbilityKind.Delete:
                    return PrepareDelete(state, request, player);
                default:
                    return RulePreparation.Invalid(TurnErrorCode.InvalidRequest, "Unknown ability.");
            }
        }

        public static RuleOutcome ResolveOutcome(int mechanicalScore, int rawD20)
        {
            if (mechanicalScore >= 10 && rawD20 == 20)
                return RuleOutcome.CriticalSuccess;
            if (mechanicalScore >= 1)
                return RuleOutcome.Success;
            if (mechanicalScore >= -4)
                return RuleOutcome.PartialSuccess;
            if (mechanicalScore >= -9)
                return RuleOutcome.Failure;
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

        public List<string> Apply(
            RunState state,
            TurnRequest request,
            RulePreparation preparation,
            RuleOutcome outcome,
            int turnNo)
        {
            var events = new List<string>();
            if (preparation.FocusCost > 0)
            {
                state.Focus -= preparation.FocusCost;
                events.Add("RESOURCE_CHANGED:focus:-" + preparation.FocusCost);
            }

            bool appliesPrimaryEffect = outcome == RuleOutcome.PartialSuccess || outcome == RuleOutcome.Success || outcome == RuleOutcome.CriticalSuccess;
            if (appliesPrimaryEffect)
            {
                switch (request.Ability)
                {
                    case AbilityKind.Move:
                    {
                        MoveResult move = state.Spatial.TryMove(state.PlayerEntityId, state.Region.RegionId, request.Destination.Value, 0,
                            (regionId, coord) => regionId == state.Region.RegionId && state.Region.IsWalkable(coord));
                        if (!move.IsSuccess)
                            throw new InvalidOperationException("Validated move failed during commit: " + move.ErrorCode);
                        events.Add("ENTITY_MOVED:" + state.PlayerEntityId + ":" + move.From + "->" + move.To);
                        break;
                    }
                    case AbilityKind.Copy:
                    {
                        state.Spatial.TryGetEntity(request.TargetEntityId.Value, out EntityState source);
                        Guid cloneId = DeterministicGuid.Create(state.RunId + ":" + turnNo + ":" + request.IdempotencyKey + ":copy");
                        var clone = new EntityState(cloneId, source.Kind, source.AssetId, source.DisplayName + " Copy", source.IsBlocking,
                            false, source.IsCloneable, state.Region.RegionId, request.Destination.Value);
                        if (!state.Spatial.Register(clone, out string error))
                            throw new InvalidOperationException("Validated copy failed during commit: " + error);
                        events.Add("ENTITY_SPAWNED:" + cloneId + ":" + source.AssetId);
                        break;
                    }
                    case AbilityKind.Delete:
                    {
                        if (!state.Spatial.TryRemove(request.TargetEntityId.Value, out string error))
                            throw new InvalidOperationException("Validated delete failed during commit: " + error);
                        events.Add("ENTITY_REMOVED:" + request.TargetEntityId.Value);
                        break;
                    }
                }
            }

            if (outcome == RuleOutcome.PartialSuccess)
            {
                state.IsExposed = true;
                events.Add("STATUS_ADDED:player:exposed:1");
            }

            return events;
        }

        private static RulePreparation PrepareMove(RunState state, TurnRequest request, EntityState player)
        {
            if (!request.Destination.HasValue || !state.Region.IsWalkable(request.Destination.Value))
                return RulePreparation.Invalid(TurnErrorCode.DestinationInvalid, "Choose a walkable destination.");
            if (request.Destination.Value == player.Position)
                return RulePreparation.Invalid(TurnErrorCode.DestinationInvalid, "Player is already on that tile.");

            List<GridCoord> path = GridPathfinder.FindPath(state.Region, player.Position, request.Destination.Value,
                coord => state.Spatial.IsBlockingOccupied(state.Region.RegionId, coord, 0, state.PlayerEntityId));
            if (path.Count == 0)
                return RulePreparation.Invalid(TurnErrorCode.PathBlocked, "No legal path reaches that tile.");
            if (path.Count - 1 > 5)
                return RulePreparation.Invalid(TurnErrorCode.OutOfRange, "Destination is outside the 5-tile movement budget.");

            return RulePreparation.Valid("Move to " + request.Destination.Value + " via " + (path.Count - 1) + " steps", 9, 3, 0, path);
        }

        private static RulePreparation PrepareCopy(RunState state, TurnRequest request, EntityState player)
        {
            if (state.Focus < 1)
                return RulePreparation.Invalid(TurnErrorCode.InsufficientResource, "Copy requires 1 focus.");
            if (!request.TargetEntityId.HasValue || !state.Spatial.TryGetEntity(request.TargetEntityId.Value, out EntityState target) || !target.IsActive)
                return RulePreparation.Invalid(TurnErrorCode.EntityNotFound, "Choose an active source object.");
            if (target.IsProtected)
                return RulePreparation.Invalid(TurnErrorCode.ProtectedEntity, "Protected objects cannot be copied.");
            if (!target.IsCloneable)
                return RulePreparation.Invalid(TurnErrorCode.NotCloneable, "This object is not cloneable.");
            if (!request.Destination.HasValue || !state.Region.IsWalkable(request.Destination.Value) ||
                state.Spatial.IsBlockingOccupied(state.Region.RegionId, request.Destination.Value, 0))
                return RulePreparation.Invalid(TurnErrorCode.DestinationInvalid, "Choose an empty walkable destination.");
            if (player.Position.ManhattanDistance(target.Position) > 4 || player.Position.ManhattanDistance(request.Destination.Value) > 4)
                return RulePreparation.Invalid(TurnErrorCode.OutOfRange, "Copy source and destination must be within 4 tiles.");

            return RulePreparation.Valid("Copy " + target.DisplayName + " to " + request.Destination.Value, 11, 3, 1);
        }

        private static RulePreparation PrepareDelete(RunState state, TurnRequest request, EntityState player)
        {
            if (state.Focus < 1)
                return RulePreparation.Invalid(TurnErrorCode.InsufficientResource, "Delete requires 1 focus.");
            if (!request.TargetEntityId.HasValue || !state.Spatial.TryGetEntity(request.TargetEntityId.Value, out EntityState target) || !target.IsActive)
                return RulePreparation.Invalid(TurnErrorCode.EntityNotFound, "Choose an active target.");
            if (target.EntityId == state.PlayerEntityId || target.IsProtected)
                return RulePreparation.Invalid(TurnErrorCode.ProtectedEntity, "Protected entities cannot be deleted.");
            if (player.Position.ManhattanDistance(target.Position) > 3)
                return RulePreparation.Invalid(TurnErrorCode.OutOfRange, "Delete target must be within 3 tiles.");

            return RulePreparation.Valid("Delete " + target.DisplayName, 12, 3, 1);
        }
    }
}
