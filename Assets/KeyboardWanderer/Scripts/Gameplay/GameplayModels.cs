using System;
using System.Collections.Generic;
using KeyboardWanderer.Core;

namespace KeyboardWanderer.Gameplay
{
    public enum AbilityKind
    {
        Move,
        Copy,
        Delete
    }

    public enum RuleOutcome
    {
        CriticalFailure,
        Failure,
        PartialSuccess,
        Success,
        CriticalSuccess
    }

    public enum RunStatus
    {
        Playing,
        Completed,
        Abandoned,
        RecoveryRequired
    }

    public enum CampaignAct
    {
        Introduction,
        Exploration,
        Pressure,
        Convergence,
        Ending
    }

    public enum TurnErrorCode
    {
        None,
        InvalidRequest,
        IdempotencyConflict,
        RunVersionConflict,
        RunNotPlaying,
        EntityNotFound,
        ProtectedEntity,
        NotCloneable,
        DestinationInvalid,
        PathBlocked,
        OutOfRange,
        InsufficientResource
    }

    public sealed class TurnRequest
    {
        public string IdempotencyKey { get; }
        public long ExpectedRunVersion { get; }
        public AbilityKind Ability { get; }
        public Guid? TargetEntityId { get; }
        public GridCoord? Destination { get; }
        public string IntentText { get; }

        public TurnRequest(
            string idempotencyKey,
            long expectedRunVersion,
            AbilityKind ability,
            Guid? targetEntityId,
            GridCoord? destination,
            string intentText)
        {
            IdempotencyKey = idempotencyKey;
            ExpectedRunVersion = expectedRunVersion;
            Ability = ability;
            TargetEntityId = targetEntityId;
            Destination = destination;
            IntentText = intentText ?? string.Empty;
        }

        public string Fingerprint()
        {
            return ExpectedRunVersion + "|" + Ability + "|" + TargetEntityId + "|" + Destination + "|" + IntentText.Trim();
        }
    }

    public sealed class EntityView
    {
        public Guid EntityId { get; }
        public EntityKind Kind { get; }
        public string AssetId { get; }
        public string DisplayName { get; }
        public GridCoord Position { get; }
        public bool IsProtected { get; }
        public bool IsCloneable { get; }

        public EntityView(EntityState state)
        {
            EntityId = state.EntityId;
            Kind = state.Kind;
            AssetId = state.AssetId;
            DisplayName = state.DisplayName;
            Position = state.Position;
            IsProtected = state.IsProtected;
            IsCloneable = state.IsCloneable;
        }
    }

    public sealed class RunView
    {
        public Guid RunId { get; }
        public long Version { get; }
        public int CurrentTurn { get; }
        public int TurnLimit { get; }
        public int RemainingTurns => Math.Max(0, TurnLimit - CurrentTurn);
        public int Focus { get; }
        public RunStatus Status { get; }
        public CampaignAct Act { get; }
        public string EndingCode { get; }
        public RegionMap Region { get; }
        public Guid PlayerEntityId { get; }
        public IReadOnlyList<EntityView> Entities { get; }

        public RunView(RunState state)
        {
            RunId = state.RunId;
            Version = state.Version;
            CurrentTurn = state.CurrentTurn;
            TurnLimit = state.TurnLimit;
            Focus = state.Focus;
            Status = state.Status;
            Act = CampaignDirector.Evaluate(state.CurrentTurn, state.TurnLimit).Act;
            EndingCode = state.EndingCode;
            Region = state.Region;
            PlayerEntityId = state.PlayerEntityId;
            var entities = new List<EntityView>();
            foreach (EntityState entity in state.Spatial.Entities)
            {
                if (entity.IsActive)
                    entities.Add(new EntityView(entity));
            }
            Entities = entities;
        }
    }

    public sealed class TurnResponse
    {
        public bool IsSuccess { get; }
        public bool FromIdempotencyCache { get; }
        public TurnErrorCode ErrorCode { get; }
        public string ErrorMessage { get; }
        public int TurnNo { get; }
        public int D20 { get; }
        public int MechanicalScore { get; }
        public RuleOutcome Outcome { get; }
        public string NormalizedAttempt { get; }
        public string Narrative { get; }
        public int ConsequenceBudget { get; }
        public IReadOnlyList<string> Events { get; }
        public RunView Run { get; }

        private TurnResponse(
            bool isSuccess,
            bool fromCache,
            TurnErrorCode errorCode,
            string errorMessage,
            int turnNo,
            int d20,
            int mechanicalScore,
            RuleOutcome outcome,
            string normalizedAttempt,
            string narrative,
            int consequenceBudget,
            IReadOnlyList<string> events,
            RunView run)
        {
            IsSuccess = isSuccess;
            FromIdempotencyCache = fromCache;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            TurnNo = turnNo;
            D20 = d20;
            MechanicalScore = mechanicalScore;
            Outcome = outcome;
            NormalizedAttempt = normalizedAttempt;
            Narrative = narrative;
            ConsequenceBudget = consequenceBudget;
            Events = events ?? Array.Empty<string>();
            Run = run;
        }

        public static TurnResponse Failure(TurnErrorCode code, string message, RunView run)
        {
            return new TurnResponse(false, false, code, message, 0, 0, 0, RuleOutcome.Failure, string.Empty, string.Empty, 0, Array.Empty<string>(), run);
        }

        public static TurnResponse Success(
            int turnNo,
            int d20,
            int mechanicalScore,
            RuleOutcome outcome,
            string normalizedAttempt,
            string narrative,
            int consequenceBudget,
            IReadOnlyList<string> events,
            RunView run)
        {
            return new TurnResponse(true, false, TurnErrorCode.None, null, turnNo, d20, mechanicalScore, outcome,
                normalizedAttempt, narrative, consequenceBudget, events, run);
        }

        public TurnResponse AsCached()
        {
            return new TurnResponse(IsSuccess, true, ErrorCode, ErrorMessage, TurnNo, D20, MechanicalScore, Outcome,
                NormalizedAttempt, Narrative, ConsequenceBudget, Events, Run);
        }
    }

    public sealed class RunState
    {
        public Guid RunId { get; }
        public long WorldSeed { get; }
        public long Version { get; set; }
        public int CurrentTurn { get; set; }
        public int TurnLimit { get; }
        public int Focus { get; set; }
        public bool IsExposed { get; set; }
        public RunStatus Status { get; set; }
        public string EndingCode { get; set; }
        public RegionMap Region { get; }
        public SpatialIndex Spatial { get; }
        public Guid PlayerEntityId { get; }

        public RunState(
            Guid runId,
            long worldSeed,
            long version,
            int currentTurn,
            int turnLimit,
            int focus,
            RunStatus status,
            string endingCode,
            RegionMap region,
            SpatialIndex spatial,
            Guid playerEntityId)
        {
            RunId = runId;
            WorldSeed = worldSeed;
            Version = version;
            CurrentTurn = currentTurn;
            TurnLimit = turnLimit;
            Focus = focus;
            Status = status;
            EndingCode = endingCode;
            Region = region;
            Spatial = spatial;
            PlayerEntityId = playerEntityId;
        }

        public RunState Clone()
        {
            return new RunState(RunId, WorldSeed, Version, CurrentTurn, TurnLimit, Focus, Status, EndingCode,
                Region, Spatial.Clone(), PlayerEntityId)
            {
                IsExposed = IsExposed
            };
        }
    }
}
