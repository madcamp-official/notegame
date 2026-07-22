using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KeyboardWanderer.Core;
using KeyboardWanderer.Gameplay;
using KeyboardWanderer.Networking;
using NUnit.Framework;

namespace KeyboardWanderer.Tests
{
    public sealed class CoreAndTurnTests
    {
        private static readonly string[] BiomeIds =
        {
            "temperate_forest_field", "river_wetland", "arid_desert", "frost_highland",
            "subterranean_cavern", "ancient_ruins"
        };

        private static readonly string[] BeatIds =
        {
            "arrival", "collapse", "admin-access-1", "admin-access-2", "truth",
            "debt-backflow", "admin-access-3", "root-entry", "final-deployment"
        };

        private static readonly string[] ProgressionNodeIds =
        {
            "keyboard-awakening", "admin-access-1", "admin-access-2", "internal-failure",
            "admin-access-3", "root-system"
        };

        private static readonly string[] EndingCodes =
        {
            "ENDING_REWEAVE_TOGETHER", "ENDING_OPEN_FRONTIER", "ENDING_KEEP_THE_PROMISE",
            "ENDING_CUT_THE_CYCLE", "ENDING_PRESERVE_THE_SCARS",
            "ENDING_WALK_BETWEEN_WORLDS", "ENDING_EMERGENCY_WITHDRAWAL"
        };

        [Test]
        public void PublicInputContract_ExposesMoveInteractionAndSevenKeyboardSkills()
        {
            AbilityKind[] values = Enum.GetValues(typeof(AbilityKind)).Cast<AbilityKind>().ToArray();

            Assert.That(values, Is.EqualTo(new[]
            {
                AbilityKind.Move, AbilityKind.Copy, AbilityKind.Delete, AbilityKind.Connect,
                AbilityKind.Restore, AbilityKind.Undo, AbilityKind.Interact,
                AbilityKind.Search, AbilityKind.SelectAll
            }));
            Assert.That(values.Count(TurnRequest.IsPublicKeyboardSkill), Is.EqualTo(7));
            Assert.Throws<ArgumentException>(() => TurnRequest.UseSkill("legacy", 1, (AbilityKind)4));

            RunState state = LocalTurnService.CreateDemo(19).CreateSnapshot();
            RulePreparation rejected = new RuleEngine().Prepare(state,
                new TurnRequest("legacy-raw", state.Version, (AbilityKind)4, null, null, string.Empty));
            Assert.That(rejected.IsValid, Is.False);
            Assert.That(rejected.ErrorCode, Is.EqualTo(TurnErrorCode.InvalidRequest));
        }

        [Test]
        public void GridPathfinder_HeapFindsDeterministicPathAndHonorsExpansionBudget()
        {
            RunView view = LocalTurnService.CreateDemo(20260720L).CurrentView;
            GridCoord start = view.PlayerPosition;
            List<GridCoord> path = null;
            for (int y = 0; y < view.Region.Height && path == null; y++)
            {
                for (int x = 0; x < view.Region.Width; x++)
                {
                    var candidate = new GridCoord(x, y);
                    if (start.ManhattanDistance(candidate) < 12 || !view.Region.IsWalkable(candidate))
                        continue;
                    List<GridCoord> candidatePath = GridPathfinder.FindPath(view.Region, start, candidate);
                    if (candidatePath.Count > 2)
                        path = candidatePath;
                }
            }

            Assert.That(path, Is.Not.Null, "테스트 월드에서 충분히 먼 도달 가능 타일을 찾아야 합니다.");
            Assert.That(path[0], Is.EqualTo(start));
            for (int i = 1; i < path.Count; i++)
                Assert.That(path[i - 1].ManhattanDistance(path[i]), Is.EqualTo(1));

            List<GridCoord> repeated = GridPathfinder.FindPath(view.Region, start, path[path.Count - 1]);
            Assert.That(repeated, Is.EqualTo(path), "동일 월드의 동률 경로 선택은 결정론적이어야 합니다.");
            Assert.That(GridPathfinder.FindPath(view.Region, start, path[path.Count - 1], null, 1), Is.Empty,
                "확장 예산을 소진하면 부분 경로를 커밋하지 않아야 합니다.");

            List<GridCoord> nearest = GridPathfinder.FindShortestPathToAny(view.Region, start,
                new[] { path[path.Count - 1], path[2] });
            Assert.That(nearest.Count, Is.EqualTo(3));
            Assert.That(nearest[nearest.Count - 1], Is.EqualTo(path[2]),
                "다중 목표 탐색은 한 번의 순회로 가장 가까운 목표를 선택해야 합니다.");
        }

        [Test]
        public void SpatialIndex_CopyAtReusesCallerBufferAndMoveRejectsMissingValidator()
        {
            RunState state = LocalTurnService.CreateDemo(20260720L).CreateSnapshot();
            EntityState player = state.Spatial.Entities.Single(entity => entity.EntityId == state.PlayerEntityId);
            var buffer = new List<EntityState>();

            int firstCount = state.Spatial.CopyAt(state.Region.RegionId, player.Position, buffer, player.Layer);
            int secondCount = state.Spatial.CopyAt(state.Region.RegionId, player.Position, buffer, player.Layer);

            Assert.That(firstCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(secondCount, Is.EqualTo(firstCount));
            Assert.That(buffer.Any(entity => entity.EntityId == player.EntityId), Is.True);
            MoveResult rejected = state.Spatial.TryMove(player.EntityId, state.Region.RegionId,
                player.Position, player.Layer, null);
            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(rejected.ErrorCode, Is.EqualTo("WALKABILITY_CHECK_REQUIRED"));
        }

        [Test]
        public void AmbientWander_MovesNpcAuthoritativelyWithinTwoTilesWithoutOverlap()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(20260720L);
            RunView cachedBefore = service.CurrentView;
            Assert.That(service.CurrentView, Is.SameAs(cachedBefore));
            Dictionary<Guid, GridCoord> origins = service.CurrentView.Entities
                .Where(entity => entity.Kind == EntityKind.Npc)
                .ToDictionary(entity => entity.EntityId, entity => entity.Position);

            bool moved = false;
            for (int step = 0; step < origins.Count * 8; step++)
                moved |= service.TryAdvanceAmbientWander(2);

            RunView view = service.CurrentView;
            Assert.That(moved, Is.True);
            Assert.That(view, Is.Not.SameAs(cachedBefore));
            Assert.That(view.Version, Is.GreaterThan(cachedBefore.Version));
            Assert.That(service.CurrentView, Is.SameAs(view));
            foreach (EntityView npc in view.Entities.Where(entity => entity.Kind == EntityKind.Npc))
                Assert.That(npc.Position.ManhattanDistance(origins[npc.EntityId]), Is.LessThanOrEqualTo(2));

            GridCoord[] blockingPositions = service.CreateSnapshot().Spatial.Entities
                .Where(entity => entity.IsBlocking)
                .Select(entity => entity.Position)
                .ToArray();
            Assert.That(blockingPositions.Distinct().Count(), Is.EqualTo(blockingPositions.Length));
        }

        [Test]
        public void AmbientWander_SaveResumePreservesDeterministicScheduleAndOrigin()
        {
            LocalTurnService uninterrupted = LocalTurnService.CreateDemo(20260721L);
            for (int step = 0; step < 12; step++) uninterrupted.TryAdvanceAmbientWander(2);
            LocalTurnService resumed = LocalRunSaveService.Deserialize(LocalRunSaveService.Serialize(uninterrupted));

            for (int step = 0; step < 20; step++)
            {
                uninterrupted.TryAdvanceAmbientWander(2);
                resumed.TryAdvanceAmbientWander(2);
                Dictionary<Guid, GridCoord> expected = uninterrupted.CurrentView.Entities
                    .Where(entity => entity.Kind == EntityKind.Npc)
                    .ToDictionary(entity => entity.EntityId, entity => entity.Position);
                Dictionary<Guid, GridCoord> actual = resumed.CurrentView.Entities
                    .Where(entity => entity.Kind == EntityKind.Npc)
                    .ToDictionary(entity => entity.EntityId, entity => entity.Position);
                Assert.That(actual, Is.EqualTo(expected));
            }
        }

        [Test]
        public void SearchRequiresOneTarget_AndSelectAllIsBoundedCombat()
        {
            RunState searchState = LocalTurnService.CreateDemo(119).CreateSnapshot();
            EntityState searchTarget = searchState.Spatial.Entities.First(entity =>
                entity.AssetId == CampaignCatalog.AdministratorKeyboardId);
            MovePlayerNear(searchState, searchTarget);
            var engine = new RuleEngine();
            RulePreparation search = engine.Prepare(searchState,
                TurnRequest.UseSkill("ctrl-f-search", searchState.Version, AbilityKind.Search,
                    searchTarget.EntityId));
            Assert.That(search.IsValid, Is.True);
            Assert.That(search.FocusCost, Is.EqualTo(1));
            Assert.That(RuleEngine.ClassifyAction(searchState,
                TurnRequest.UseSkill("ctrl-f-context", searchState.Version, AbilityKind.Search,
                    searchTarget.EntityId)),
                Is.EqualTo(ActionContext.Investigation));

            RunState areaState = LocalTurnService.CreateDemo(120).CreateSnapshot();
            EntityState nearbyEnemy = areaState.Spatial.Entities.First(entity => entity.Kind == EntityKind.Enemy);
            MovePlayerNear(areaState, nearbyEnemy);
            RulePreparation area = engine.Prepare(areaState,
                TurnRequest.UseSkill("ctrl-a-area", areaState.Version, AbilityKind.SelectAll));
            Assert.That(area.IsValid, Is.True);
            Assert.That(area.FocusCost, Is.EqualTo(3));
            Assert.That(RuleEngine.ClassifyAction(areaState,
                TurnRequest.UseSkill("ctrl-a-context", areaState.Version, AbilityKind.SelectAll)),
                Is.EqualTo(ActionContext.Combat));
        }

        [Test]
        public void NetworkClient_RejectsLegacySkillBeforeSendingRequest()
        {
            var client = new GameApiClient();
            GameApiClient.Result<GameApiClient.CommittedTurn> result = null;
            var request = client.SubmitAction("run", "legacy-network", 1, "ATTACK",
                Array.Empty<string>(), null, 0, value => result = value);

            while (request.MoveNext()) { }

            Assert.That(result, Is.Not.Null);
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("INVALID_SKILL"));
        }

        [Test]
        public void WorldContract_Seals160SquareSixBiomesAndSixIndependentRegionAxes()
        {
            RegionMap map = DeterministicRegionGenerator.Generate(991, "codria-world");

            Assert.That(map.Width, Is.EqualTo(160));
            Assert.That(map.Height, Is.EqualTo(160));
            Assert.That(map.Biomes.Select(value => value.Id), Is.EquivalentTo(BiomeIds));
            Assert.That(map.Areas, Has.Count.EqualTo(12));
            Assert.That(map.Areas.GroupBy(value => value.Biome).Select(group => group.Count()),
                Is.All.EqualTo(2));
            Assert.That(map.Areas.Where(value => !string.IsNullOrWhiteSpace(value.CampaignRole))
                .Select(value => value.CampaignRole), Is.EquivalentTo(CampaignCatalog.RegionAxisIds));
            Assert.That(map.Areas.All(value => !CampaignCatalog.RegionAxisIds.Contains(value.Biome)), Is.True);
            Assert.That(map.GenerationReport.IsValid, Is.True);
            Assert.That(DeterministicRegionGenerator.ComputeLayoutHash(map), Is.EqualTo(map.LayoutHash));
        }

        [Test]
        public void WorldContract_GivesEveryAdminLevelTwoAreasAndTwoContexts()
        {
            RegionMap map = DeterministicRegionGenerator.Generate(992, "codria-world");
            PlacementSlot[] candidates = map.PlacementSlots
                .Where(slot => slot.Tags.Contains("admin_access_candidate")).ToArray();

            Assert.That(candidates, Has.Length.EqualTo(6));
            for (int level = 1; level <= 3; level++)
            {
                PlacementSlot[] levelCandidates = candidates
                    .Where(slot => slot.Tags.Contains("admin_level_" + level)).ToArray();
                Assert.That(levelCandidates, Has.Length.EqualTo(2));
                Assert.That(levelCandidates.Select(slot => slot.AreaId).Distinct().Count(), Is.EqualTo(2));
                Assert.That(levelCandidates.SelectMany(slot => slot.Tags)
                    .Where(tag => tag.StartsWith("context_", StringComparison.Ordinal)).Distinct().Count(),
                    Is.EqualTo(2));
            }
        }

        [Test]
        public void SameSeed_IsDeterministic_AndLayoutNeverDependsOnCampaignTurns()
        {
            RegionMap first = DeterministicRegionGenerator.Generate(20260717, "codria-world");
            RegionMap repeated = DeterministicRegionGenerator.Generate(20260717, "codria-world");
            RegionMap different = DeterministicRegionGenerator.Generate(20260718, "codria-world");
            LocalTurnService service = LocalTurnService.CreateDemo(20260717, new SequenceD20Source(20));
            string sealedHash = service.CurrentView.Region.LayoutHash;
            GridCoord destination = FirstSafeDestination(service.CurrentView);

            TurnResponse moved = service.Submit(TurnRequest.Move("safe", service.CurrentView.Version,
                destination));

            Assert.That(repeated.LayoutHash, Is.EqualTo(first.LayoutHash));
            Assert.That(different.LayoutHash, Is.Not.EqualTo(first.LayoutHash));
            Assert.That(moved.IsSuccess, Is.True);
            Assert.That(moved.ConsumesCampaignTurn, Is.False);
            Assert.That(moved.D20, Is.Zero);
            Assert.That(moved.Run.CurrentTurn, Is.Zero);
            Assert.That(moved.Run.Region.LayoutHash, Is.EqualTo(sealedHash));
        }

        [Test]
        public void Catalog_FixesCodriaNupjukyiKeyboardNineBeatsAndSharedEndings()
        {
            CampaignBlueprint blueprint = CampaignCatalog.Create(73);

            Assert.That(CampaignCatalog.WorldId, Is.EqualTo("WORLD_CODRIA"));
            Assert.That(CampaignCatalog.ProtagonistId, Is.EqualTo("PROTAGONIST_NUPJUKYI"));
            Assert.That(CampaignCatalog.AdministratorKeyboardId, Is.EqualTo("ARTIFACT_ADMIN_KEYBOARD"));
            Assert.That(blueprint.Title, Is.EqualTo("Ninja Adventure"));
            Assert.That(blueprint.WorldName, Is.EqualTo("코드리아"));
            Assert.That(blueprint.PlayerName, Is.EqualTo("넙죽이"));
            Assert.That(blueprint.PlayerAssetId, Is.EqualTo("player.ninja-green.v1"));
            Assert.That(blueprint.Beats.Select(beat => beat.Id), Is.EqualTo(BeatIds));
            Assert.That(blueprint.Endings.Select(ending => ending.Code), Is.EquivalentTo(EndingCodes));
            Assert.That(blueprint.AdminAccessBindings.Select(binding => binding.AccessId),
                Is.EqualTo(CampaignCatalog.AdminAccessLevelIds));
            Assert.That(blueprint.AdminAccessBindings.All(binding =>
                binding.CandidateRegionAxes.Distinct().Count() >= 2 &&
                binding.CandidateContexts.Distinct().Count() >= 2), Is.True);
        }

        [Test]
        public void MultipleSeeds_PreserveSpatialProgressionAndThreePartRootGate()
        {
            foreach (long seed in new long[] { 0, 1, 2, 77, 20260717 })
            {
                RegionMap map = DeterministicRegionGenerator.Generate(seed, "codria-world");
                Assert.That(map.Progression.Nodes.Select(node => node.Id), Is.EqualTo(ProgressionNodeIds));
                Assert.That(map.Progression.RootRequiredAdminAccess, Is.EqualTo(3));
                Assert.That(map.Progression.RootRequiredAccessTokens,
                    Is.EquivalentTo(CampaignCatalog.AdminAccessLevelIds));
                Assert.That(map.Routes.Where(route => route.IsGated).All(route =>
                    route.RequiredAdminAccess == 3 &&
                    route.RequiredAccessTokens.OrderBy(value => value)
                        .SequenceEqual(CampaignCatalog.AdminAccessLevelIds.OrderBy(value => value))), Is.True);
                Assert.That(map.Routes.Count(route => route.IsLoop), Is.GreaterThanOrEqualTo(2));
            }
        }

        [Test]
        public void RootMovement_RequiresAllThreeAccessIdsAndInternalCauseClue()
        {
            RunState state = LocalTurnService.CreateDemo(80).CreateSnapshot();
            FindRootBoundaryPair(state.Region, out GridCoord outside, out GridCoord inside);
            MovePlayerTo(state, outside);
            state.AdminAccess = 3;
            foreach (string accessId in CampaignCatalog.AdminAccessLevelIds) state.Inventory.Add(accessId);
            var engine = new RuleEngine();

            RulePreparation missingClue = engine.Prepare(state,
                TurnRequest.Move("root-no-clue", state.Version, inside));
            state.AddCanonicalFact("붕괴 원인이 관리자 통제 시스템 내부에 있음을 확정했다.");
            RulePreparation allowed = engine.Prepare(state,
                TurnRequest.Move("root-ready", state.Version, inside));

            Assert.That(missingClue.IsValid, Is.False);
            Assert.That(missingClue.ErrorCode, Is.EqualTo(TurnErrorCode.QuestConditionMissing));
            Assert.That(allowed.IsValid, Is.True);
        }

        [Test]
        public void NaturalLanguageNote_IsOptionalAndNeverChangesRulesPreparation()
        {
            RunState state = LocalTurnService.CreateDemo(81).CreateSnapshot();
            EntityState keyboard = state.Spatial.Entities.Single(entity =>
                entity.AssetId == CampaignCatalog.AdministratorKeyboardId);
            MovePlayerNear(state, keyboard);
            var engine = new RuleEngine();

            RulePreparation empty = engine.Prepare(state, new TurnRequest("note-empty", state.Version,
                AbilityKind.Search, keyboard.EntityId, null, string.Empty));
            RulePreparation elaborate = engine.Prepare(state, new TurnRequest("note-long", state.Version,
                AbilityKind.Search, keyboard.EntityId, null,
                "이 문장은 분위기만 설명하며 주사위나 난이도를 바꾸지 않는다"));

            Assert.That(empty.IsValid, Is.True);
            Assert.That(elaborate.IsValid, Is.True);
            Assert.That(elaborate.Difficulty, Is.EqualTo(empty.Difficulty));
            Assert.That(elaborate.Modifier, Is.EqualTo(empty.Modifier));
            Assert.That(elaborate.IntentAlignment, Is.Zero);
        }

        [Test]
        public void AdminAccess_AcquiresOnlyFromSelectedRegionContextSkillAndEvidence()
        {
            long seed = FindSeedForAccessSkill(0, AbilityKind.Search);
            RunState state = LocalTurnService.CreateDemo(seed).CreateSnapshot();
            CampaignBeatState beat = state.RequiredBeats[2];
            for (int i = 0; i < 2; i++) state.RequiredBeats[i].IsCompleted = true;
            state.CurrentBeatIndex = 2;
            EntityState candidate = state.Spatial.Entities.Single(entity => entity.IsActive &&
                entity.AssetId.StartsWith("story.admin-access-1.investigation", StringComparison.Ordinal));
            MovePlayerNear(state, candidate);
            var service = new LocalTurnService(state, new SequenceD20Source(20));

            TurnResponse response = service.Submit(TurnRequest.UseSkill("access-one",
                service.CurrentView.Version, AbilityKind.Search, candidate.EntityId));

            Assert.That(beat.TriggerAbility, Is.EqualTo(AbilityKind.Search));
            Assert.That(response.IsSuccess, Is.True);
            Assert.That(response.Run.AdminAccess, Is.EqualTo(1));
            Assert.That(response.Run.Inventory, Does.Contain("ADMIN_ACCESS_LEVEL_1"));
            Assert.That(response.Run.AdminAccessAcquisitionHistory, Has.Count.EqualTo(1));
            Assert.That(response.Run.AdminAccessAcquisitionHistory[0].Context,
                Is.EqualTo(ActionContext.Investigation));
            Assert.That(response.Events.Any(value => value.StartsWith("ADMIN_ACCESS_ACQUIRED:1:",
                StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void TechnicalDebt_RecordsCausalEntryWithoutAutomaticResolution()
        {
            RunState state = LocalTurnService.CreateDemo(82).CreateSnapshot();
            EntityState source = state.Spatial.Entities.First(entity => entity.IsActive && entity.IsCloneable &&
                !entity.IsProtected && entity.Kind != EntityKind.Player && entity.Kind != EntityKind.Npc);
            MovePlayerNear(state, source);
            GridCoord destination = EmptyCoordinateNear(state, source.Position, 4);
            var service = new LocalTurnService(state, new SequenceD20Source(20));

            TurnResponse copied = service.Submit(TurnRequest.UseSkill("copy-object",
                service.CurrentView.Version, AbilityKind.Copy, source.EntityId, null, destination));

            Assert.That(copied.IsSuccess, Is.True);
            Assert.That(copied.Run.TechnicalDebtEntries, Has.Count.EqualTo(1));
            Assert.That(copied.Run.TechnicalDebtEntries[0].OperationType, Is.EqualTo("COPY"));
            Assert.That(copied.Run.TechnicalDebtEntries[0].TargetId, Is.Not.Empty);
            Assert.That(copied.Run.TechnicalDebtEntries[0].IsResolved, Is.False);
        }

        [Test]
        public void Undo_CompensatesTwoMeaningfulTurnsWithoutRewindingTurnCounter()
        {
            RunState state = LocalTurnService.CreateDemo(821).CreateSnapshot();
            EntityState target = state.Spatial.Entities.Single(entity =>
                entity.AssetId == CampaignCatalog.AdministratorKeyboardId);
            MovePlayerNear(state, target);
            var service = new LocalTurnService(state, new SequenceD20Source(20, 20, 20));

            TurnResponse first = service.Submit(TurnRequest.UseSkill("search-one", service.CurrentView.Version,
                AbilityKind.Search, target.EntityId));
            TurnResponse second = service.Submit(TurnRequest.UseSkill("search-two", service.CurrentView.Version,
                AbilityKind.Search, target.EntityId));
            TurnResponse rewind = service.Submit(TurnRequest.UseSkill("rewind-two", service.CurrentView.Version,
                AbilityKind.Undo));

            Assert.That(first.IsSuccess && second.IsSuccess && rewind.IsSuccess, Is.True);
            Assert.That(rewind.Run.CurrentTurn, Is.EqualTo(3));
            Assert.That(rewind.Run.RollCount, Is.EqualTo(3));
            Assert.That(rewind.Run.Focus, Is.EqualTo(state.Focus - 2),
                "The skill costs 3 focus and this fixed natural 20 returns the normal +1 critical reward.");
            Assert.That(rewind.Events.Count(value => value.StartsWith("TURN_COMPENSATED:", StringComparison.Ordinal)),
                Is.EqualTo(2));
            Assert.That(rewind.Events.Any(value => value.StartsWith("UNDO_COMPENSATION_COMPLETED:",
                StringComparison.Ordinal)), Is.True);
            Assert.That(rewind.Events.Any(value => value.StartsWith("UNDO_COMPENSATED:",
                StringComparison.Ordinal)), Is.True);
            Assert.That(rewind.Run.TechnicalDebt, Is.GreaterThan(state.TechnicalDebt));
        }

        [Test]
        public void SaveResume_UsesPersistedRollCountAfterCompensatingUndo()
        {
            const long seed = 824;
            LocalTurnService uninterrupted = LocalTurnService.CreateDemo(seed);
            EntityView target = uninterrupted.CurrentView.Entities.First(entity =>
                entity.AssetId == CampaignCatalog.AdministratorKeyboardId);
            RunState positioned = uninterrupted.CreateSnapshot();
            MovePlayerNear(positioned, positioned.Spatial.Entities.Single(entity => entity.EntityId == target.EntityId));
            uninterrupted = new LocalTurnService(positioned, new SeededD20Source((int)seed));

            uninterrupted.Submit(TurnRequest.UseSkill("rng-one", uninterrupted.CurrentView.Version,
                AbilityKind.Search, target.EntityId));
            uninterrupted.Submit(TurnRequest.UseSkill("rng-two", uninterrupted.CurrentView.Version,
                AbilityKind.Search, target.EntityId));
            LocalTurnService resumed = LocalRunSaveService.Deserialize(LocalRunSaveService.Serialize(uninterrupted));

            TurnResponse expected = uninterrupted.Submit(TurnRequest.UseSkill("rng-three-a",
                uninterrupted.CurrentView.Version, AbilityKind.Search, target.EntityId));
            TurnResponse actual = resumed.Submit(TurnRequest.UseSkill("rng-three-b",
                resumed.CurrentView.Version, AbilityKind.Search, target.EntityId));

            Assert.That(actual.D20, Is.EqualTo(expected.D20));
            Assert.That(actual.Run.RollCount, Is.EqualTo(3));
        }

        [Test]
        public void RestoredEnemy_CannotGrantDefeatRewardTwice()
        {
            RunState state = LocalTurnService.CreateDemo(825).CreateSnapshot();
            EntityState enemy = state.Spatial.Entities.First(entity => entity.IsActive && entity.Kind == EntityKind.Enemy);
            state.AddCanonicalFact(EnemyArchetypeCatalog.RevealedFact(enemy.EntityId));
            MovePlayerNear(state, enemy);
            var service = new LocalTurnService(state, new SequenceD20Source(20));

            service.Submit(TurnRequest.UseSkill("reward-hit", service.CurrentView.Version,
                AbilityKind.Delete, enemy.EntityId));
            TurnResponse defeated = service.Submit(TurnRequest.UseSkill("reward-kill", service.CurrentView.Version,
                AbilityKind.Delete, enemy.EntityId));
            TurnResponse restored = service.Submit(TurnRequest.UseSkill("reward-restore", service.CurrentView.Version,
                AbilityKind.Restore, enemy.EntityId));
            TurnResponse defeatedAgain = service.Submit(TurnRequest.UseSkill("reward-rekill", service.CurrentView.Version,
                AbilityKind.Delete, enemy.EntityId));

            Assert.That(defeated.IsSuccess && restored.IsSuccess && defeatedAgain.IsSuccess, Is.True);
            Assert.That(defeatedAgain.Run.Gold, Is.EqualTo(defeated.Run.Gold));
            Assert.That(defeatedAgain.Run.Experience, Is.EqualTo(defeated.Run.Experience));
            Assert.That(defeatedAgain.Run.EnemiesDefeated, Is.EqualTo(defeated.Run.EnemiesDefeated));
            Assert.That(defeatedAgain.Events.Any(value => value.Contains("reward=already-claimed")), Is.True);
        }

        [Test]
        public void SaveResume_PreservesV4LedgersAndImmutableLayout()
        {
            RunState state = LocalTurnService.CreateDemo(83).CreateSnapshot();
            state.AdminAccess = 2;
            state.Inventory.Add(CampaignCatalog.AdminAccessLevelIds[0]);
            state.Inventory.Add(CampaignCatalog.AdminAccessLevelIds[1]);
            state.MajorChoices.Add("T1|REGION_BUG_FOREST|INVESTIGATION|COPY|SUCCESS");
            state.RegionOutcomes.Add("REGION_BUG_FOREST|INVESTIGATION|SUCCESS|T1");
            state.TechnicalDebtEntries.Add(new TechnicalDebtEntry("debt-1", 1, AbilityKind.Copy,
                "COPY", "ARTIFACT_ADMIN_KEYBOARD", false, 3, "DUPLICATED_STATE_DRIFT"));
            var service = new LocalTurnService(state, new SequenceD20Source(20));

            string json = LocalRunSaveService.Serialize(service);
            RunView restored = LocalRunSaveService.Deserialize(json).CurrentView;

            Assert.That(restored.Region.LayoutHash, Is.EqualTo(service.CurrentView.Region.LayoutHash));
            Assert.That(restored.AdminAccess, Is.EqualTo(2));
            Assert.That(restored.MajorChoices, Is.EqualTo(state.MajorChoices));
            Assert.That(restored.RegionOutcomes, Is.EqualTo(state.RegionOutcomes));
            Assert.That(restored.TechnicalDebtEntries, Has.Count.EqualTo(1));
            Assert.That(restored.RequiredBeats, Has.Count.EqualTo(9));
        }

        [Test]
        public void SaveChecksum_RejectsModifiedPayload()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(831);
            string json = LocalRunSaveService.Serialize(service);
            string tampered = json.Replace("\"gold\":5", "\"gold\":500");

            Assert.That(tampered, Is.Not.EqualTo(json));
            Assert.Throws<System.IO.InvalidDataException>(() => LocalRunSaveService.Deserialize(tampered));
        }

        [Test]
        public void IdempotencyCache_EvictsOldResponsesAtFixedLimit()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(832);
            long staleVersion = service.CurrentView.Version + 1000;
            for (int index = 0; index < LocalTurnService.IdempotencyCacheLimit + 20; index++)
            {
                TurnResponse response = service.Submit(TurnRequest.UseSkill("bounded-cache-" + index,
                    staleVersion, AbilityKind.Undo));
                Assert.That(response.IsSuccess, Is.False);
            }

            FieldInfo field = typeof(LocalTurnService).GetField("_idempotency",
                BindingFlags.Instance | BindingFlags.NonPublic);
            object cache = field?.GetValue(service);
            int count = (int)cache?.GetType().GetProperty("Count")?.GetValue(cache);

            Assert.That(count, Is.EqualTo(LocalTurnService.IdempotencyCacheLimit));
        }

        [Test]
        public void CampaignDirector_ExposesExactlyNineMacroPhases()
        {
            CampaignPhase[] phases = Enumerable.Range(0, 40)
                .Select(turn => CampaignDirector.Evaluate(turn, 40).Phase).Distinct().ToArray();

            Assert.That(phases, Is.EqualTo(new[]
            {
                CampaignPhase.ArrivalAndKeyboardAwakening, CampaignPhase.FirstRegionProblem,
                CampaignPhase.AdminAccessOne, CampaignPhase.AdminAccessTwo,
                CampaignPhase.InternalFailureTruth, CampaignPhase.TechnicalDebtBackflow,
                CampaignPhase.AdminAccessThree, CampaignPhase.RootSystemEntry,
                CampaignPhase.FinalDeployment
            }));
            Assert.That(LocalTurnService.CampaignTurnLimit, Is.EqualTo(40));
            Assert.That(LocalTurnService.MaximumCampaignTurnLimit, Is.EqualTo(50));
        }

        [TestCase("ENDING_REWEAVE_TOGETHER")]
        [TestCase("ENDING_OPEN_FRONTIER")]
        [TestCase("ENDING_KEEP_THE_PROMISE")]
        [TestCase("ENDING_CUT_THE_CYCLE")]
        [TestCase("ENDING_PRESERVE_THE_SCARS")]
        [TestCase("ENDING_WALK_BETWEEN_WORLDS")]
        public void RuleEngine_SelectsSharedEndingIdsFromAuthoritativeState(string endingCode)
        {
            RunState state = FinaleReadyState(90);
            ConfigureEnding(state, endingCode);

            Assert.That(CampaignDirector.SelectEnding(state), Is.EqualTo(endingCode));
        }

        [Test]
        public void EndingFallsBackWhenRootRequirementsAreIncomplete()
        {
            RunState state = FinaleReadyState(91);
            state.Inventory.Remove(CampaignCatalog.AdminAccessLevelIds[2]);

            Assert.That(CampaignDirector.SelectEnding(state),
                Is.EqualTo(CampaignCatalog.FallbackEndingCode));
        }

        [Test]
        public void ServerWorldDto_DecodesAreaAndBiomeMasks()
        {
            var world = new GameApiClient.WorldSnapshot
            {
                width = 3,
                height = 2,
                tileLegend = new[] { "floor" },
                areaMapLegend = new[] { "area-west", "area-east" },
                biomeMapLegend = new[] { "forest", "wetland" }
            };
            const string json = "{\"tilesRle\":[[0,6]],\"areaMapRle\":[[0,2],[1,1],[0,1],[1,2]]," +
                                "\"biomeMapRle\":[[0,1],[1,2],[0,2],[1,1]]}";
            MethodInfo decode = typeof(GameApiClient).GetMethod("PopulateTileCodes",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(decode, Is.Not.Null);
            decode.Invoke(null, new object[] { world, json });
            Assert.That(world.HasCompleteAreaMap, Is.True);
            Assert.That(world.HasCompleteBiomeMap, Is.True);
            Assert.That(world.AreaIdAt(2, 0), Is.EqualTo("area-east"));
            Assert.That(world.BiomeIdAt(1, 0), Is.EqualTo("wetland"));
        }

        private static GridCoord FirstSafeDestination(RunView view)
        {
            for (int distance = 1; distance <= 12; distance++)
                for (int y = view.PlayerPosition.Y - distance; y <= view.PlayerPosition.Y + distance; y++)
                    for (int x = view.PlayerPosition.X - distance; x <= view.PlayerPosition.X + distance; x++)
                    {
                        var candidate = new GridCoord(x, y);
                        if (candidate.ManhattanDistance(view.PlayerPosition) != distance ||
                            !view.Region.Contains(candidate) || !view.Region.IsWalkable(candidate) ||
                            view.Region.GetTile(candidate).Kind == TileKind.Hazard ||
                            view.Region.GetTile(candidate).Kind == TileKind.Ruin ||
                            view.Entities.Any(entity => entity.Position == candidate)) continue;
                        if (GridPathfinder.FindPath(view.Region, view.PlayerPosition, candidate).Count > 0)
                            return candidate;
                    }
            Assert.Fail("No safe travel destination found.");
            return default;
        }

        private static long FindSeedForAccessSkill(int bindingIndex, AbilityKind skill)
        {
            for (long seed = 0; seed < 500; seed++)
                if (CampaignCatalog.Create(seed).AdminAccessBindings[bindingIndex].SelectedSkill == skill)
                    return seed;
            Assert.Fail("No seed selected the requested administrator access skill.");
            return 0;
        }

        private static void MovePlayerNear(RunState state, EntityState target)
        {
            for (int distance = 1; distance <= 4; distance++)
                for (int y = target.Position.Y - distance; y <= target.Position.Y + distance; y++)
                    for (int x = target.Position.X - distance; x <= target.Position.X + distance; x++)
                    {
                        var candidate = new GridCoord(x, y);
                        if (candidate.ManhattanDistance(target.Position) != distance ||
                            !state.Region.IsWalkable(candidate) ||
                            state.Spatial.IsBlockingOccupied(state.Region.RegionId, candidate, 0,
                                state.PlayerEntityId)) continue;
                        MovePlayerTo(state, candidate);
                        return;
                    }
            Assert.Fail("No empty coordinate near " + target.DisplayName);
        }

        private static void MovePlayerTo(RunState state, GridCoord destination)
        {
            MoveResult moved = state.Spatial.TryMove(state.PlayerEntityId, state.Region.RegionId,
                destination, 0, (regionId, coord) =>
                    regionId == state.Region.RegionId && state.Region.IsWalkable(coord));
            Assert.That(moved.IsSuccess, Is.True, moved.ErrorCode);
        }

        private static GridCoord EmptyCoordinateNear(RunState state, GridCoord origin, int radius)
        {
            for (int distance = 1; distance <= radius; distance++)
                for (int y = origin.Y - distance; y <= origin.Y + distance; y++)
                    for (int x = origin.X - distance; x <= origin.X + distance; x++)
                    {
                        var candidate = new GridCoord(x, y);
                        if (candidate.ManhattanDistance(origin) != distance || !state.Region.IsWalkable(candidate) ||
                            state.Spatial.IsBlockingOccupied(state.Region.RegionId, candidate, 0)) continue;
                        return candidate;
                    }
            Assert.Fail("No empty coordinate near " + origin);
            return default;
        }

        private static void FindRootBoundaryPair(RegionMap map, out GridCoord outside, out GridCoord inside)
        {
            WorldArea root = map.Areas.Single(area => area.CampaignRole == CampaignCatalog.RootSystemAxis);
            for (int y = root.Min.Y; y <= root.Max.Y; y++)
                for (int x = root.Min.X; x <= root.Max.X; x++)
                {
                    var candidate = new GridCoord(x, y);
                    if (!map.IsWalkable(candidate)) continue;
                    foreach (GridCoord direction in new[]
                    {
                        new GridCoord(1, 0), new GridCoord(-1, 0),
                        new GridCoord(0, 1), new GridCoord(0, -1)
                    })
                    {
                        var neighbor = new GridCoord(x + direction.X, y + direction.Y);
                        if (map.Contains(neighbor) && map.IsWalkable(neighbor) && !root.Contains(neighbor))
                        {
                            outside = neighbor;
                            inside = candidate;
                            return;
                        }
                    }
                }
            Assert.Fail("No walkable ROOT_SYSTEM boundary pair found.");
            outside = default;
            inside = default;
        }

        private static RunState FinaleReadyState(long seed)
        {
            RunState state = LocalTurnService.CreateDemo(seed).CreateSnapshot();
            state.AdminAccess = 3;
            foreach (string accessId in CampaignCatalog.AdminAccessLevelIds)
                if (!state.HasItem(accessId)) state.Inventory.Add(accessId);
            state.AddCanonicalFact("붕괴 원인이 관리자 통제 시스템 내부에 있음을 확정했다.");
            state.WorldStability = 60;
            state.WorldAutonomy = 60;
            state.PublicTrust = 60;
            state.TechnicalDebt = 20;
            state.CompanionBond = 60;
            for (int i = 0; i < state.RequiredBeats.Count; i++) state.RequiredBeats[i].IsCompleted = true;
            state.CurrentBeatIndex = state.RequiredBeats.Count;
            return state;
        }

        private static void ConfigureEnding(RunState state, string code)
        {
            switch (code)
            {
                case "ENDING_REWEAVE_TOGETHER":
                    RemoveAsset(state, "finale.threat");
                    Link(state, "finale.anchor", "finale.safeguard");
                    LinkPlayer(state, "finale.memory");
                    break;
                case "ENDING_OPEN_FRONTIER":
                    RemoveAsset(state, "finale.threat");
                    Link(state, "finale.anchor", "finale.freedom");
                    break;
                case "ENDING_KEEP_THE_PROMISE":
                    RemoveAsset(state, "finale.threat");
                    LinkPlayer(state, "finale.safeguard");
                    Link(state, "finale.memory", "finale.anchor");
                    break;
                case "ENDING_CUT_THE_CYCLE":
                    RemoveAsset(state, "finale.threat");
                    RemoveAsset(state, "finale.freedom");
                    Link(state, "finale.anchor", "finale.passage");
                    break;
                case "ENDING_PRESERVE_THE_SCARS":
                    RemoveAsset(state, "finale.freedom");
                    Link(state, "finale.memory", "finale.safeguard");
                    LinkPlayer(state, "finale.witness");
                    break;
                case "ENDING_WALK_BETWEEN_WORLDS":
                    RemoveAsset(state, "finale.threat");
                    LinkPlayer(state, "finale.passage");
                    Link(state, "finale.anchor", "finale.safeguard");
                    break;
                default:
                    Assert.Fail("Unknown ending recipe " + code);
                    break;
            }
        }

        private static void Link(RunState state, string leftAsset, string rightAsset)
        {
            EntityState left = state.Spatial.Entities.Single(entity => entity.AssetId == leftAsset);
            EntityState right = state.Spatial.Entities.Single(entity => entity.AssetId == rightAsset);
            AddConnection(state, left.EntityId, right.EntityId);
        }

        private static void LinkPlayer(RunState state, string rightAsset)
        {
            EntityState right = state.Spatial.Entities.Single(entity => entity.AssetId == rightAsset);
            AddConnection(state, state.PlayerEntityId, right.EntityId);
        }

        private static void AddConnection(RunState state, Guid left, Guid right)
        {
            string leftId = left.ToString("N");
            string rightId = right.ToString("N");
            state.Connections.Add(string.CompareOrdinal(leftId, rightId) <= 0
                ? leftId + ":" + rightId
                : rightId + ":" + leftId);
        }

        private static void RemoveAsset(RunState state, string assetId)
        {
            EntityState entity = state.Spatial.Entities.Single(value => value.AssetId == assetId);
            Assert.That(state.Spatial.TryRemove(entity.EntityId, out string error), Is.True, error);
        }
    }
}
