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
        public void PublicInputContract_ExposesMoveAndExactlyFiveKeyboardSkills()
        {
            AbilityKind[] values = Enum.GetValues(typeof(AbilityKind)).Cast<AbilityKind>().ToArray();

            Assert.That(values, Is.EqualTo(new[]
            {
                AbilityKind.Move, AbilityKind.Copy, AbilityKind.Delete, AbilityKind.Connect,
                AbilityKind.Restore, AbilityKind.Undo
            }));
            Assert.That(values.Count(TurnRequest.IsPublicKeyboardSkill), Is.EqualTo(5));
            Assert.Throws<ArgumentException>(() => TurnRequest.UseSkill("legacy", 1, (AbilityKind)4));

            RunState state = LocalTurnService.CreateDemo(19).CreateSnapshot();
            RulePreparation rejected = new RuleEngine().Prepare(state,
                new TurnRequest("legacy-raw", state.Version, (AbilityKind)4, null, null, string.Empty));
            Assert.That(rejected.IsValid, Is.False);
            Assert.That(rejected.ErrorCode, Is.EqualTo(TurnErrorCode.InvalidRequest));
        }

        [Test]
        public void NetworkClient_RejectsLegacySkillBeforeSendingRequest()
        {
            var client = new GameApiClient();
            GameApiClient.Result<GameApiClient.CommittedTurn> result = null;
            var request = client.SubmitAction("run", "legacy-network", 1, "ATTACK",
                Array.Empty<string>(), null, value => result = value);

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
            Assert.That(blueprint.Title, Is.EqualTo("넙죽이와 붕괴한 코드 왕국"));
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
                AbilityKind.Copy, keyboard.EntityId, null, string.Empty));
            RulePreparation elaborate = engine.Prepare(state, new TurnRequest("note-long", state.Version,
                AbilityKind.Copy, keyboard.EntityId, null,
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
            long seed = FindSeedForAccessSkill(0, AbilityKind.Copy);
            RunState state = LocalTurnService.CreateDemo(seed).CreateSnapshot();
            CampaignBeatState beat = state.RequiredBeats[2];
            for (int i = 0; i < 2; i++) state.RequiredBeats[i].IsCompleted = true;
            state.CurrentBeatIndex = 2;
            EntityState candidate = state.Spatial.Entities.Single(entity => entity.IsActive &&
                entity.AssetId.StartsWith("story.admin-access-1.investigation", StringComparison.Ordinal));
            MovePlayerNear(state, candidate);
            var service = new LocalTurnService(state, new SequenceD20Source(20));

            TurnResponse response = service.Submit(TurnRequest.UseSkill("access-one",
                service.CurrentView.Version, AbilityKind.Copy, candidate.EntityId));

            Assert.That(beat.TriggerAbility, Is.EqualTo(AbilityKind.Copy));
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
            EntityState keyboard = state.Spatial.Entities.Single(entity =>
                entity.AssetId == CampaignCatalog.AdministratorKeyboardId);
            MovePlayerNear(state, keyboard);
            var service = new LocalTurnService(state, new SequenceD20Source(20));

            TurnResponse copied = service.Submit(TurnRequest.UseSkill("copy-keyboard",
                service.CurrentView.Version, AbilityKind.Copy, keyboard.EntityId));

            Assert.That(copied.IsSuccess, Is.True);
            Assert.That(copied.Run.TechnicalDebtEntries, Has.Count.EqualTo(1));
            Assert.That(copied.Run.TechnicalDebtEntries[0].OperationType, Is.EqualTo("COPY"));
            Assert.That(copied.Run.TechnicalDebtEntries[0].TargetId, Is.Not.Empty);
            Assert.That(copied.Run.TechnicalDebtEntries[0].IsResolved, Is.False);
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
