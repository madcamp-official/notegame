using System;
using System.Collections.Generic;
using KeyboardWanderer.Core;

namespace KeyboardWanderer.Gameplay
{
    internal sealed class StoredTurn
    {
        public string Fingerprint { get; }
        public TurnResponse Response { get; }

        public StoredTurn(string fingerprint, TurnResponse response)
        {
            Fingerprint = fingerprint;
            Response = response;
        }
    }

    public sealed class LocalTurnService
    {
        private readonly RuleEngine _ruleEngine;
        private readonly ID20Source _d20;
        private readonly Dictionary<string, StoredTurn> _idempotency = new Dictionary<string, StoredTurn>();
        private RunState _state;

        public RunView CurrentView => new RunView(_state);

        public LocalTurnService(RunState initialState, ID20Source d20Source)
        {
            _state = initialState ?? throw new ArgumentNullException(nameof(initialState));
            _d20 = d20Source ?? throw new ArgumentNullException(nameof(d20Source));
            _ruleEngine = new RuleEngine();
        }

        public TurnResponse Submit(TurnRequest request)
        {
            if (request == null)
                return TurnResponse.Failure(TurnErrorCode.InvalidRequest, "Request is required.", CurrentView);

            string fingerprint = request.Fingerprint();
            if (_idempotency.TryGetValue(request.IdempotencyKey ?? string.Empty, out StoredTurn stored))
            {
                if (stored.Fingerprint != fingerprint)
                    return TurnResponse.Failure(TurnErrorCode.IdempotencyConflict, "The idempotency key was reused with a different payload.", CurrentView);
                return stored.Response.AsCached();
            }

            if (_state.Status != RunStatus.Playing)
                return StoreFailure(request, fingerprint, TurnErrorCode.RunNotPlaying, "The run no longer accepts turns.");
            if (request.ExpectedRunVersion != _state.Version)
                return StoreFailure(request, fingerprint, TurnErrorCode.RunVersionConflict, "Run version is stale. Refresh authoritative state.");

            RulePreparation preparation = _ruleEngine.Prepare(_state, request);
            if (!preparation.IsValid)
                return StoreFailure(request, fingerprint, preparation.ErrorCode, preparation.ErrorMessage);

            int rawD20 = _d20.Roll();
            int mechanicalScore = rawD20 + preparation.Modifier - preparation.Difficulty;
            RuleOutcome outcome = RuleEngine.ResolveOutcome(mechanicalScore, rawD20);
            int nextTurn = _state.CurrentTurn + 1;

            // Work on a complete clone and swap only after all commit operations succeed.
            RunState working = _state.Clone();
            List<string> events = _ruleEngine.Apply(working, request, preparation, outcome, nextTurn);
            working.CurrentTurn = nextTurn;
            working.Version++;
            events.Add("TURN_COMMITTED:" + nextTurn);

            CampaignConstraints constraints = CampaignDirector.Evaluate(working.CurrentTurn, working.TurnLimit);
            if (constraints.ForceEnding || working.CurrentTurn >= working.TurnLimit)
            {
                working.Status = RunStatus.Completed;
                working.EndingCode = SelectEnding(working);
                events.Add("RUN_COMPLETED:" + working.EndingCode);
            }

            List<string> integrityErrors = working.Spatial.Validate((regionId, coord) =>
                regionId == working.Region.RegionId && working.Region.IsWalkable(coord));
            if (integrityErrors.Count > 0)
                throw new InvalidOperationException("Commit plan violated spatial invariants: " + string.Join(",", integrityErrors));

            _state = working;
            string narrative = FallbackNarrative.Create(request.Ability, outcome, rawD20, preparation.NormalizedAttempt, constraints);
            var response = TurnResponse.Success(nextTurn, rawD20, mechanicalScore, outcome, preparation.NormalizedAttempt,
                narrative, RuleEngine.ConsequenceBudget(rawD20), events, CurrentView);
            _idempotency.Add(request.IdempotencyKey, new StoredTurn(fingerprint, response));
            return response;
        }

        public static LocalTurnService CreateDemo(long worldSeed = 20260717, ID20Source d20Source = null)
        {
            RegionMap region = DeterministicRegionGenerator.Generate(worldSeed, "forgotten-terminal", 11, 9);
            Guid runId = DeterministicGuid.Create("run:" + worldSeed);
            Guid playerId = DeterministicGuid.Create("player:" + worldSeed);
            var spatial = new SpatialIndex();

            RegisterOrThrow(spatial, new EntityState(playerId, EntityKind.Player, "player.green.v1", "Keyboard Wanderer",
                true, true, false, region.RegionId, region.Start));
            RegisterOrThrow(spatial, new EntityState(DeterministicGuid.Create("book:" + worldSeed), EntityKind.Prop, "item.rune-book.v1",
                "Rune Book", false, false, true, region.RegionId, region.PlacementSlots[2].Coord));
            RegisterOrThrow(spatial, new EntityState(DeterministicGuid.Create("crate:" + worldSeed), EntityKind.Prop, "item.crate.v1",
                "Crate", true, false, true, region.RegionId, region.PlacementSlots[3].Coord));
            RegisterOrThrow(spatial, new EntityState(DeterministicGuid.Create("warden:" + worldSeed), EntityKind.Npc, "npc.warden.v1",
                "Archive Warden", true, true, false, region.RegionId, region.PlacementSlots[4].Coord));

            var state = new RunState(runId, worldSeed, 1, 0, 40, 8, RunStatus.Playing, null, region, spatial, playerId);
            return new LocalTurnService(state, d20Source ?? new SeededD20Source(unchecked((int)worldSeed)));
        }

        private TurnResponse StoreFailure(TurnRequest request, string fingerprint, TurnErrorCode code, string message)
        {
            TurnResponse response = TurnResponse.Failure(code, message, CurrentView);
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
                _idempotency[request.IdempotencyKey] = new StoredTurn(fingerprint, response);
            return response;
        }

        private static void RegisterOrThrow(SpatialIndex spatial, EntityState entity)
        {
            if (!spatial.Register(entity, out string error))
                throw new InvalidOperationException("Demo entity registration failed: " + error);
        }

        private static string SelectEnding(RunState state)
        {
            return state.Focus >= 3 ? "SEAL_THE_ARCHIVE" : "ESCAPE_WITH_THE_INDEX";
        }
    }

    public static class FallbackNarrative
    {
        public static string Create(AbilityKind ability, RuleOutcome outcome, int d20, string attempt, CampaignConstraints constraints)
        {
            string result;
            switch (outcome)
            {
                case RuleOutcome.CriticalSuccess:
                    result = "The command lands exactly as intended, with a brief opening for the next move.";
                    break;
                case RuleOutcome.Success:
                    result = "The world accepts the command and settles into its new state.";
                    break;
                case RuleOutcome.PartialSuccess:
                    result = "The command works, but the terminal glow exposes your position.";
                    break;
                case RuleOutcome.CriticalFailure:
                    result = "The command is rejected violently; the state holds, but pressure closes in.";
                    break;
                default:
                    result = "The command fails cleanly. Nothing illegal is written into the world.";
                    break;
            }

            string pacing = constraints.MustAdvanceMainPlot
                ? " The shrinking turn budget pulls the scene toward the archive core."
                : string.Empty;
            return "D20 " + d20 + " · " + ability + " · " + attempt + ". " + result + pacing;
        }
    }
}
