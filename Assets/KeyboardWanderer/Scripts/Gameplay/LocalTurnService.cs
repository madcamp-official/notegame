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

    /// <summary>
    /// Offline authoritative turn service. It generates one immutable 160x160 world per run and then
    /// commits only entity/story state changes. No LLM output is required for a legal turn or ending.
    /// </summary>
    public sealed class LocalTurnService
    {
        public const int DemoWorldWidth = 160;
        public const int DemoWorldHeight = 160;
        public const int CampaignTurnLimit = 40;
        public const int MaximumCampaignTurnLimit = 50;

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

        public RunState CreateSnapshot() { return _state.Clone(); }

        public TurnResponse Submit(TurnRequest request)
        {
            if (request == null)
                return TurnResponse.Failure(TurnErrorCode.InvalidRequest, "Request is required.", CurrentView);

            string fingerprint = request.Fingerprint();
            if (_idempotency.TryGetValue(request.IdempotencyKey ?? string.Empty, out StoredTurn stored))
            {
                if (stored.Fingerprint != fingerprint)
                    return TurnResponse.Failure(TurnErrorCode.IdempotencyConflict,
                        "The idempotency key was reused with a different payload.", CurrentView);
                return stored.Response.AsCached();
            }

            if (_state.Status != RunStatus.Playing)
                return StoreFailure(request, fingerprint, TurnErrorCode.RunNotPlaying, "The run no longer accepts turns.");
            if (request.ExpectedRunVersion != _state.Version)
                return StoreFailure(request, fingerprint, TurnErrorCode.RunVersionConflict,
                    "Run version is stale. Refresh authoritative state.");

            if (IsPlayerExplorationMove(_state, request) && !_state.HasActiveEncounter)
            {
                if (TryPrepareSafeExplorationMove(_state, request, out RulePreparation safeTravel))
                    return CommitSafeTravel(request, fingerprint, safeTravel);
                if (CanOpenTravelEncounter(_state, request))
                    return CommitTravelEncounter(request, fingerprint);
            }

            RulePreparation preparation = _ruleEngine.Prepare(_state, request);
            if (!preparation.IsValid)
                return StoreFailure(request, fingerprint, preparation.ErrorCode, preparation.ErrorMessage);

            int rawD20 = _d20.Roll();
            int mechanicalScore = rawD20 + preparation.Modifier - preparation.Difficulty;
            RuleOutcome outcome = RuleEngine.ResolveOutcome(mechanicalScore, rawD20);
            string outcomeExplanation = RuleEngine.ExplainOutcome(rawD20, preparation, outcome);
            int nextTurn = _state.CurrentTurn + 1;

            // Clone-and-swap keeps the entire authoritative commit atomic.
            RunState working = _state.Clone();
            string immutableLayoutHash = working.Region.LayoutHash;
            ReversibleTurnRecord reversible = request.Ability == AbilityKind.Undo
                ? null
                : working.CaptureReversibleTurn(nextTurn, request.Ability);
            GridCoord beforePosition = GetPlayer(working).Position;
            string beforeArea = AreaId(working, beforePosition);

            working.RecordIntent(nextTurn, request.Ability, request.IntentText);
            List<string> events = _ruleEngine.Apply(working, request, preparation, outcome, nextTurn);
            ApplyHazard(working, events);
            ApplyEnemyPhase(working, nextTurn, events);

            working.CurrentTurn = nextTurn;
            working.Version++;
            CampaignDirector.ProcessCommittedTurn(working, request, outcome, nextTurn, events);
            if (working.HasActiveEncounter && IsEncounterResolutionAction(request.Ability))
            {
                working.HasActiveEncounter = false;
                working.ActiveEncounterReason = string.Empty;
                events.Add("ACTIVE_ENCOUNTER_RESOLVED:" + request.Ability);
            }
            if (reversible != null)
            {
                FinalizeReversibleRecord(reversible, events);
                working.LastReversibleTurn = reversible;
            }
            events.Add("TURN_COMMITTED:" + nextTurn);

            working.LastNormalizedAttempt = preparation.NormalizedAttempt;
            working.LastRollRaw = rawD20;
            working.LastRollModifier = preparation.Modifier;
            working.LastRollDifficulty = preparation.Difficulty;
            working.LastMechanicalScore = mechanicalScore;
            working.LastIntentAlignment = preparation.IntentAlignment;
            working.LastOutcome = outcome;
            working.LastOutcomeExplanation = outcomeExplanation;

            EntityState player = GetPlayer(working);
            if (player.Health <= 0)
            {
                working.Status = RunStatus.Dead;
                working.EndingCode = "FALLEN_IN_" + working.CampaignId.ToUpperInvariant().Replace('.', '_').Replace('-', '_');
                events.Add("RUN_FAILED:" + working.EndingCode);
            }
            else
            {
                string selectedEnding = CampaignDirector.SelectEnding(working);
                bool finaleWindowOpen = working.CurrentTurn >= Math.Max(6, working.TurnLimit - 12);
                bool finaleResolved = finaleWindowOpen && working.MilestoneProgress >= 3 &&
                                      working.CurrentBeat == null &&
                                      !string.Equals(selectedEnding, CampaignCatalog.FallbackEndingCode,
                                          StringComparison.Ordinal);
                if (finaleResolved || working.CurrentTurn >= working.TurnLimit)
                {
                    working.Status = RunStatus.Completed;
                    working.EndingCode = finaleResolved ? selectedEnding : CampaignCatalog.FallbackEndingCode;
                    working.AddCanonicalFact(working.CurrentTurn + "번째 의미 있는 턴에 결말이 확정되었다: " + working.EndingCode);
                    events.Add("RUN_COMPLETED:" + working.EndingCode);
                }
            }

            string afterArea = AreaId(working, player.Position);
            if (!string.Equals(beforeArea, afterArea, StringComparison.Ordinal))
                working.AddLog("새 지역에 들어섰다: " +
                    (working.Region.AreaAt(player.Position)?.DisplayName ?? afterArea));

            CampaignConstraints constraints = CampaignDirector.Evaluate(working.CurrentTurn, working.TurnLimit);
            string narrative = FallbackNarrative.Create(request.Ability, outcome, rawD20,
                preparation.NormalizedAttempt, constraints, working.CampaignTitle,
                working.CurrentBeat == null ? "결말" : working.CurrentBeat.Title, events);
            working.AddLog("[" + nextTurn + "턴 · D20 " + rawD20 + "] " + narrative);

            if (!string.Equals(immutableLayoutHash, working.Region.LayoutHash, StringComparison.Ordinal))
                throw new InvalidOperationException("A turn attempted to replace immutable world geometry.");
            List<string> integrityErrors = working.Spatial.Validate((regionId, coord) =>
                regionId == working.Region.RegionId && working.Region.IsWalkable(coord));
            if (integrityErrors.Count > 0)
                throw new InvalidOperationException("Commit plan violated spatial invariants: " +
                    string.Join(",", integrityErrors));

            _state = working;
            var response = TurnResponse.Success(nextTurn, rawD20, preparation.Modifier, preparation.Difficulty,
                mechanicalScore, preparation.IntentAlignment, outcome, outcomeExplanation,
                preparation.NormalizedAttempt, narrative, RuleEngine.ConsequenceBudget(rawD20), events, CurrentView);
            _idempotency.Add(request.IdempotencyKey, new StoredTurn(fingerprint, response));
            return response;
        }

        private static bool IsPlayerExplorationMove(RunState state, TurnRequest request)
        {
            return request.Ability == AbilityKind.Move &&
                   (!request.TargetEntityId.HasValue || request.TargetEntityId.Value == state.PlayerEntityId) &&
                   request.Destination.HasValue;
        }

        private static bool TryPrepareSafeExplorationMove(RunState state, TurnRequest request,
            out RulePreparation preparation)
        {
            preparation = null;
            if (!IsPlayerExplorationMove(state, request) || string.IsNullOrWhiteSpace(request.IntentText) ||
                !state.Region.IsWalkable(request.Destination.Value))
                return false;
            if (!state.Spatial.TryGetEntity(state.PlayerEntityId, out EntityState player) ||
                player.Position == request.Destination.Value)
                return false;
            WorldArea destinationArea = state.Region.AreaAt(request.Destination.Value);
            if (destinationArea != null && destinationArea.RequiredAdminAccess > state.MilestoneProgress)
                return false;

            List<GridCoord> path = GridPathfinder.FindPath(state.Region, player.Position, request.Destination.Value,
                coord => state.Spatial.IsBlockingOccupied(state.Region.RegionId, coord, player.Layer, player.EntityId) ||
                         !IsSafeTravelTile(state, coord));
            if (path.Count == 0)
                return false;
            preparation = RulePreparation.Valid("Travel safely to " + request.Destination.Value + " through " +
                (path.Count - 1) + " immutable-world steps", 0, 0, 0, path, player.EntityId)
                .WithIntentAlignment(request.IntentText.Length >= 8 ? 2 : 1);
            return true;
        }

        private static bool IsSafeTravelTile(RunState state, GridCoord coord)
        {
            TileKind kind = state.Region.GetTile(coord).Kind;
            if (kind == TileKind.Hazard || kind == TileKind.Ruin)
                return false;
            foreach (EntityState entity in state.Spatial.Entities)
            {
                if (entity.IsActive && entity.IsHostile && entity.Position.ManhattanDistance(coord) <= 2)
                    return false;
            }
            WorldArea area = state.Region.AreaAt(coord);
            if (area != null && area.RequiredAdminAccess > state.MilestoneProgress)
                return false;
            bool discovered = area != null && state.VisitedAreaIds.Contains(area.Id);
            return discovered || kind == TileKind.Dirt || kind == TileKind.Bridge;
        }

        private static bool CanOpenTravelEncounter(RunState state, TurnRequest request)
        {
            if (!IsPlayerExplorationMove(state, request) || string.IsNullOrWhiteSpace(request.IntentText) ||
                !state.Region.IsWalkable(request.Destination.Value))
                return false;
            WorldArea area = state.Region.AreaAt(request.Destination.Value);
            return area == null || area.RequiredAdminAccess <= state.AdminAccess;
        }

        private TurnResponse CommitTravelEncounter(TurnRequest request, string fingerprint)
        {
            RunState working = _state.Clone();
            EntityState player = GetPlayer(working);
            GridCoord destination = request.Destination.Value;
            string reason = TravelEncounterReason(working, destination);
            GridCoord staging = FindEncounterStaging(working, player, destination, out int travelSteps);
            if (staging != player.Position)
            {
                MoveResult moved = working.Spatial.TryMove(player.EntityId, working.Region.RegionId, staging,
                    player.Layer, (regionId, coord) => regionId == working.Region.RegionId && working.Region.IsWalkable(coord));
                if (!moved.IsSuccess)
                    throw new InvalidOperationException("Validated encounter staging move failed: " + moved.ErrorCode);
            }
            working.HasActiveEncounter = true;
            working.ActiveEncounterReason = reason;
            working.EncounterStagingPosition = staging;
            WorldArea stagingArea = working.Region.AreaAt(staging);
            if (stagingArea != null && !working.VisitedAreaIds.Contains(stagingArea.Id))
                working.VisitedAreaIds.Add(stagingArea.Id);
            working.TravelTime += travelSteps;
            working.Version++;
            working.RecordIntent(working.CurrentTurn, request.Ability, request.IntentText);
            working.LastNormalizedAttempt = "Safe travel stopped at " + staging + " before " + destination;
            working.LastRollRaw = 0;
            working.LastRollModifier = 0;
            working.LastRollDifficulty = 0;
            working.LastMechanicalScore = 0;
            working.LastIntentAlignment = request.IntentText.Length >= 8 ? 2 : 1;
            working.LastOutcome = RuleOutcome.Success;
            working.LastOutcomeExplanation = "안전 이동이 사건 앞에서 멈췄으며 D20과 의미 있는 캠페인 턴은 아직 소비하지 않았다.";
            var events = new List<string>
            {
                "TRAVEL_ENCOUNTER_REQUIRED:" + reason,
                "ACTIVE_ENCOUNTER_OPENED:" + reason + ":staging=" + staging,
                "TRAVEL_TIME_CHANGED:+" + travelSteps,
                "EXPLORATION_TRAVEL:NO_CAMPAIGN_TURN"
            };
            working.AddLog("[사건 발견] " + reason + " · 배치/전투/조사/협상부터 의미 있는 턴 소비");
            _state = working;
            var response = TurnResponse.Success(working.CurrentTurn, 0, 0, 0, 0,
                working.LastIntentAlignment, RuleOutcome.Success, working.LastOutcomeExplanation,
                working.LastNormalizedAttempt, "안전 구간 끝에서 사건이 열렸다.", 0, events, CurrentView, false);
            _idempotency.Add(request.IdempotencyKey, new StoredTurn(fingerprint, response));
            return response;
        }

        private static GridCoord FindEncounterStaging(RunState state, EntityState player, GridCoord destination,
            out int travelSteps)
        {
            var queue = new Queue<GridCoord>();
            var distance = new Dictionary<GridCoord, int>();
            queue.Enqueue(player.Position);
            distance[player.Position] = 0;
            GridCoord best = player.Position;
            int bestDistance = best.ManhattanDistance(destination);
            GridCoord[] directions =
            {
                new GridCoord(1, 0), new GridCoord(-1, 0), new GridCoord(0, 1), new GridCoord(0, -1)
            };
            while (queue.Count > 0)
            {
                GridCoord current = queue.Dequeue();
                int goalDistance = current.ManhattanDistance(destination);
                if (goalDistance < bestDistance)
                {
                    best = current;
                    bestDistance = goalDistance;
                }
                for (int i = 0; i < directions.Length; i++)
                {
                    GridCoord next = new GridCoord(current.X + directions[i].X, current.Y + directions[i].Y);
                    if (distance.ContainsKey(next) || !state.Region.IsWalkable(next) || !IsSafeTravelTile(state, next) ||
                        state.Spatial.IsBlockingOccupied(state.Region.RegionId, next, player.Layer, player.EntityId))
                        continue;
                    WorldArea area = state.Region.AreaAt(next);
                    if (area != null && area.RequiredAdminAccess > state.AdminAccess)
                        continue;
                    distance[next] = distance[current] + 1;
                    queue.Enqueue(next);
                }
            }
            travelSteps = distance[best];
            return best;
        }

        private static string TravelEncounterReason(RunState state, GridCoord destination)
        {
            TileKind kind = state.Region.GetTile(destination).Kind;
            if (kind == TileKind.Hazard || kind == TileKind.Ruin) return "hazardous_tile";
            foreach (EntityState entity in state.Spatial.Entities)
                if (entity.IsActive && entity.IsHostile && entity.Position.ManhattanDistance(destination) <= 2)
                    return "hostile_proximity";
            WorldArea area = state.Region.AreaAt(destination);
            if (area != null && !state.VisitedAreaIds.Contains(area.Id)) return "unknown_off_route";
            return "unsafe_or_blocked_route";
        }

        private static bool IsEncounterResolutionAction(AbilityKind ability)
        {
            return ability == AbilityKind.Move || ability == AbilityKind.Attack ||
                   ability == AbilityKind.Interact || ability == AbilityKind.Negotiate;
        }

        private TurnResponse CommitSafeTravel(TurnRequest request, string fingerprint,
            RulePreparation preparation)
        {
            RunState working = _state.Clone();
            string immutableLayoutHash = working.Region.LayoutHash;
            EntityState beforePlayer = GetPlayer(working);
            WorldArea beforeArea = working.Region.AreaAt(beforePlayer.Position);
            working.RecordIntent(working.CurrentTurn, request.Ability, request.IntentText);
            List<string> events = _ruleEngine.Apply(working, request, preparation, RuleOutcome.Success,
                working.CurrentTurn);
            WorldArea afterArea = working.Region.AreaAt(GetPlayer(working).Position);
            if (afterArea != null && (beforeArea == null || beforeArea.Id != afterArea.Id))
            {
                working.TravelTime += afterArea.TravelCost;
                if (!working.VisitedAreaIds.Contains(afterArea.Id)) working.VisitedAreaIds.Add(afterArea.Id);
                events.Add("SAFE_AREA_DISCOVERED:" + afterArea.Id);
                events.Add("TRAVEL_TIME_CHANGED:+" + afterArea.TravelCost);
            }
            events.Add("EXPLORATION_TRAVEL:NO_CAMPAIGN_TURN");
            working.Version++;
            working.LastNormalizedAttempt = preparation.NormalizedAttempt;
            working.LastRollRaw = 0;
            working.LastRollModifier = 0;
            working.LastRollDifficulty = 0;
            working.LastMechanicalScore = 0;
            working.LastIntentAlignment = preparation.IntentAlignment;
            working.LastOutcome = RuleOutcome.Success;
            working.LastOutcomeExplanation = "검증된 안전 경로의 탐색 이동이므로 D20과 의미 있는 캠페인 턴을 소비하지 않았다.";
            working.AddLog("[탐색 이동] " + preparation.NormalizedAttempt + " · 의미 있는 턴 " + working.CurrentTurn + " 유지");
            if (!string.Equals(immutableLayoutHash, working.Region.LayoutHash, StringComparison.Ordinal))
                throw new InvalidOperationException("Safe travel attempted to replace immutable world geometry.");
            _state = working;
            var response = TurnResponse.Success(working.CurrentTurn, 0, 0, 0, 0,
                preparation.IntentAlignment, RuleOutcome.Success, working.LastOutcomeExplanation,
                preparation.NormalizedAttempt, "안전한 길을 따라 이미 생성된 같은 월드 안에서 이동했다.",
                0, events, CurrentView, false);
            _idempotency.Add(request.IdempotencyKey, new StoredTurn(fingerprint, response));
            return response;
        }

        public static LocalTurnService CreateDemo(long worldSeed = 20260717, ID20Source d20Source = null,
            int turnLimit = CampaignTurnLimit)
        {
            if (turnLimit < 30 || turnLimit > MaximumCampaignTurnLimit)
                throw new ArgumentOutOfRangeException(nameof(turnLimit), "Turn limit must be between 30 and 50.");
            RegionMap region = DeterministicRegionGenerator.Generate(worldSeed, "wanderer-world",
                DemoWorldWidth, DemoWorldHeight);
            CampaignBlueprint campaign = CampaignCatalog.Create(worldSeed);
            Guid runId = DeterministicGuid.Create("run:" + worldSeed + ":" + CampaignCatalog.RulesVersion);
            Guid playerId = DeterministicGuid.Create("player:" + worldSeed);
            var spatial = new SpatialIndex();

            RegisterOrThrow(spatial, new EntityState(playerId, EntityKind.Player, campaign.PlayerAssetId,
                campaign.PlayerName, true, true, false, false, 12, region.RegionId, region.Start));

            var npcs = new List<EntityState>();
            foreach (PlacementSlot slot in region.PlacementSlots)
            {
                if (slot.Id == "slot-player-entry" || slot.Id == "slot-world-exit")
                    continue;
                EntityState entity = CreateEntityForSlot(worldSeed, region, slot, campaign);
                if (entity == null)
                    continue;
                RegisterOrThrow(spatial, entity);
                if (entity.Kind == EntityKind.Npc)
                    npcs.Add(entity);
            }

            var state = new RunState(runId, worldSeed, 1, 0, turnLimit, 8, RunStatus.Playing,
                null, region, spatial, playerId)
            {
                MaxFocus = 8,
                Gold = 5
            };
            state.Inventory.Add("키보드 편집 유물");
            CampaignDirector.Install(state, campaign, npcs);
            state.AddLog("160x160 월드와 6개 지형 바이옴, 12개 관심 지점이 한 번 생성되었다. 이후에는 레이아웃을 다시 만들지 않는다.");
            state.AddLog(campaign.Title + " — " + campaign.Premise);
            return new LocalTurnService(state,
                d20Source ?? new SeededD20Source(unchecked((int)worldSeed)));
        }

        private static EntityState CreateEntityForSlot(long worldSeed, RegionMap region, PlacementSlot slot,
            CampaignBlueprint campaign)
        {
            Guid id = DeterministicGuid.Create(slot.Id + ":" + worldSeed);
            switch (slot.Id)
            {
                case "slot-catalyst": return NewEntity(id, EntityKind.Prop, "artifact.keyboard", "키보드 편집 유물", false, true, false, false, 1, region, slot);
                case "slot-milestone-1": return NewEntity(id, EntityKind.Prop, "story.milestone-token-1", "첫 번째 핵심 표식", false, true, false, false, 1, region, slot);
                case "slot-milestone-2": return NewEntity(id, EntityKind.Prop, "story.milestone-token-2", "두 번째 핵심 표식", false, true, false, false, 1, region, slot);
                case "slot-milestone-3": return NewEntity(id, EntityKind.Prop, "story.milestone-token-3", "세 번째 핵심 표식", false, true, false, false, 1, region, slot);
                case "slot-milestone-3-echo": return NewEntity(id, EntityKind.Prop, "story.milestone-token-3.echo", "손상된 선택의 메아리", true, false, true, false, 2, region, slot);
                case "slot-hidden-truth-primary": return NewEntity(id, EntityKind.Prop, "story.hidden-truth", "감춰진 진실의 주 기록", false, true, false, false, 1, region, slot);
                case "slot-hidden-truth-backup": return NewEntity(id, EntityKind.Prop, "story.hidden-truth.backup", "남겨진 증언", false, true, false, false, 1, region, slot);
                case "slot-finale-anchor": return NewEntity(id, EntityKind.Prop, "finale.anchor", "수렴의 닻", true, true, false, false, 10, region, slot);
                case "slot-finale-safeguard": return NewEntity(id, EntityKind.Prop, "finale.safeguard", "보호막", true, true, false, false, 6, region, slot);
                case "slot-finale-passage": return NewEntity(id, EntityKind.Prop, "finale.passage", "다음 세계의 통로", true, true, false, false, 6, region, slot);
                case "slot-finale-freedom": return NewEntity(id, EntityKind.Prop, "finale.freedom", "자유의 핵", true, false, false, false, 2, region, slot);
                case "slot-finale-threat": return NewEntity(id, EntityKind.Prop, "finale.threat", "남은 위협", true, false, false, false, 2, region, slot);
                case "slot-finale-memory": return NewEntity(id, EntityKind.Prop, "finale.memory", "세계의 기억", true, true, false, false, 6, region, slot);
                case "slot-finale-witness": return NewEntity(id, EntityKind.Prop, "finale.witness", "함께할 목격자", true, true, false, false, 4, region, slot);
            }

            if (slot.Type == "npc")
            {
                int nameKey = 0;
                for (int character = 0; character < slot.Id.Length; character++) nameKey += slot.Id[character];
                int number = campaign.NpcNames.Count == 0 ? 0 : nameKey % campaign.NpcNames.Count;
                string name = campaign.NpcNames.Count == 0 ? "이름 없는 여행자" : campaign.NpcNames[number];
                return NewEntity(id, EntityKind.Npc, number % 3 == 1 ? "npc.merchant" : "npc.villager.quest", name,
                    true, true, false, false, 8, region, slot);
            }
            if (slot.Type == "prop")
                return NewEntity(id, EntityKind.Prop, "item.crate", "재배치 가능한 여행 상자", true, false, true, false, 2, region, slot);
            if (slot.Type == "enemy")
                return NewEntity(id, EntityKind.Enemy, "enemy.mushroom", "뒤틀린 야생의 잔재", true, false, false, true, 6, region, slot);
            return null;
        }

        private static EntityState NewEntity(Guid id, EntityKind kind, string assetId, string displayName,
            bool blocking, bool protectedEntity, bool cloneable, bool hostile, int maxHealth,
            RegionMap region, PlacementSlot slot)
        {
            return new EntityState(id, kind, assetId, displayName, blocking, protectedEntity, cloneable,
                hostile, maxHealth, region.RegionId, slot.Coord);
        }

        private static bool HasTag(PlacementSlot slot, string tag)
        {
            for (int i = 0; i < slot.Tags.Length; i++)
                if (string.Equals(slot.Tags[i], tag, StringComparison.Ordinal)) return true;
            return false;
        }

        private static void ApplyHazard(RunState state, List<string> events)
        {
            EntityState player = GetPlayer(state);
            if (state.Region.GetTile(player.Position).Kind != TileKind.Hazard)
                return;
            state.RecordRestorable(player, state.CurrentTurn + 1, "hazard_damage");
            if (!state.Spatial.TryDamage(player.EntityId, 1, out int health, out _, out string error))
                throw new InvalidOperationException("Hazard damage failed: " + error);
            events.Add("PLAYER_DAMAGED:hazard:1:hp=" + health);
        }

        private static void ApplyEnemyPhase(RunState state, int turnNo, List<string> events)
        {
            EntityState player = GetPlayer(state);
            var enemies = new List<EntityState>();
            foreach (EntityState entity in state.Spatial.Entities)
                if (entity.IsActive && entity.IsHostile && entity.Kind == EntityKind.Enemy) enemies.Add(entity);
            enemies.Sort((left, right) =>
                string.CompareOrdinal(left.EntityId.ToString("N"), right.EntityId.ToString("N")));

            for (int i = 0; i < enemies.Count && player.Health > 0; i++)
            {
                EntityState enemy = enemies[i];
                int distance = enemy.Position.ManhattanDistance(player.Position);
                if (distance > 7) continue;
                if (distance <= 1)
                {
                    int damage = state.IsExposed || (turnNo + i) % 5 == 0 ? 2 : 1;
                    state.RecordRestorable(player, turnNo, "enemy_damage");
                    if (!state.Spatial.TryDamage(player.EntityId, damage, out int health, out _, out string error))
                        throw new InvalidOperationException("Enemy damage failed: " + error);
                    events.Add("PLAYER_DAMAGED:" + enemy.EntityId + ":" + damage + ":hp=" + health);
                    state.IsExposed = false;
                    continue;
                }

                List<GridCoord> path = BestEnemyPath(state, enemy, player);
                if (path.Count < 2) continue;
                MoveResult move = state.Spatial.TryMove(enemy.EntityId, state.Region.RegionId, path[1], 0,
                    (regionId, coord) => regionId == state.Region.RegionId && state.Region.IsWalkable(coord));
                if (move.IsSuccess)
                    events.Add("ENEMY_MOVED:" + enemy.EntityId + ":" + move.From + "->" + move.To);
            }
        }

        private static List<GridCoord> BestEnemyPath(RunState state, EntityState enemy, EntityState player)
        {
            GridCoord[] goals =
            {
                new GridCoord(player.Position.X + 1, player.Position.Y),
                new GridCoord(player.Position.X - 1, player.Position.Y),
                new GridCoord(player.Position.X, player.Position.Y + 1),
                new GridCoord(player.Position.X, player.Position.Y - 1)
            };
            List<GridCoord> best = null;
            for (int i = 0; i < goals.Length; i++)
            {
                GridCoord goal = goals[i];
                if (!state.Region.IsWalkable(goal) ||
                    state.Spatial.IsBlockingOccupied(state.Region.RegionId, goal, 0, enemy.EntityId)) continue;
                List<GridCoord> candidate = GridPathfinder.FindPath(state.Region, enemy.Position, goal,
                    coord => state.Spatial.IsBlockingOccupied(state.Region.RegionId, coord, 0, enemy.EntityId));
                if (candidate.Count > 0 && (best == null || candidate.Count < best.Count)) best = candidate;
            }
            return best ?? new List<GridCoord>();
        }

        private static void FinalizeReversibleRecord(ReversibleTurnRecord record, List<string> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                string entry = events[i];
                if (entry.StartsWith("IRREVERSIBLE_ENTITY:", StringComparison.Ordinal))
                {
                    string idText = entry.Substring("IRREVERSIBLE_ENTITY:".Length);
                    if (Guid.TryParse(idText, out Guid id) && !record.IrreversibleEntityIds.Contains(id))
                        record.IrreversibleEntityIds.Add(id);
                }
                if (entry.StartsWith("ITEM_ACQUIRED:", StringComparison.Ordinal))
                    record.RestoreInventory = false;
                if (entry.StartsWith("RESOURCE_CHANGED:gold:", StringComparison.Ordinal) ||
                    entry.StartsWith("RESOURCE_CHANGED:xp:", StringComparison.Ordinal))
                    record.RestoreEconomy = false;
                if (entry.StartsWith("CAMPAIGN_BEAT_COMPLETED:", StringComparison.Ordinal))
                    record.RestoreConnections = false;
            }
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

        private static EntityState GetPlayer(RunState state)
        {
            if (!state.Spatial.TryGetEntity(state.PlayerEntityId, out EntityState player))
                throw new InvalidOperationException("Player entity is missing.");
            return player;
        }

        private static string AreaId(RunState state, GridCoord position)
        {
            return state.Region.AreaAt(position)?.Id ?? "unknown";
        }
    }

    public static class FallbackNarrative
    {
        public static string Create(AbilityKind ability, RuleOutcome outcome, int d20, string attempt,
            CampaignConstraints constraints, string campaignTitle, string currentBeat,
            IReadOnlyList<string> events = null)
        {
            string result;
            switch (outcome)
            {
                case RuleOutcome.CriticalSuccess:
                    result = "명령이 완벽히 맞물려 다음 선택을 위한 여지가 생긴다."; break;
                case RuleOutcome.Success:
                    result = "세계가 합법적인 명령을 받아들이고 상태를 안정시킨다."; break;
                case RuleOutcome.PartialSuccess:
                    result = "명령은 적용됐지만 주변 위협이 플레이어의 흔적을 붙잡는다."; break;
                case RuleOutcome.CriticalFailure:
                    result = "명령이 거칠게 튕겨 나가고 대가가 가까워진다."; break;
                default:
                    result = "명령은 실패했지만 세계의 고정 레이아웃과 사실은 훼손되지 않았다."; break;
            }
            string pacing = constraints.MustAdvanceMainPlot
                ? " 남은 턴이 줄어들며 ‘" + currentBeat + "’ 비트와 가능한 결말이 좁혀진다."
                : string.Empty;
            return "[" + campaignTitle + "] D20 " + d20 + " · " + ability + " — " + attempt + ". " +
                   result + pacing;
        }

        // Compatibility overload for older callers while the UI/network layer migrates.
        public static string Create(AbilityKind ability, RuleOutcome outcome, int d20, string attempt,
            CampaignConstraints constraints, IReadOnlyList<string> events = null)
        {
            return Create(ability, outcome, d20, attempt, constraints, "생성 캠페인", "현재 이야기", events);
        }
    }
}
