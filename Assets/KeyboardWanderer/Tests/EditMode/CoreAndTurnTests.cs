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
        public void MultipleSeeds_SatisfyGenericProgressionAndImmutableLayoutInvariants()
        {
            long[] seeds = { 0, 1, 2, 77, 20260717 };
            foreach (long seed in seeds)
            {
                RegionMap map = DeterministicRegionGenerator.Generate(seed, "seeded-world");
                var areas = map.Areas.ToDictionary(area => area.Id);
                var slots = map.PlacementSlots.ToDictionary(slot => slot.Id);
                WorldArea finaleArea = map.Areas.Single(area =>
                    area.CampaignRole == CampaignCatalog.FinalConvergenceRole);

                Assert.That(map.GeneratorVersion, Is.EqualTo(DeterministicRegionGenerator.CurrentVersion),
                    "seed " + seed);
                Assert.That(map.Progression.Nodes.Select(node => node.Id), Is.EqualTo(ProgressionNodeIds),
                    "seed " + seed);
                Assert.That(map.Progression.Nodes.Select(node => node.CampaignRole),
                    Is.EqualTo(CampaignCatalog.RoleIds), "seed " + seed);
                Assert.That(map.Progression.RootRequiredAccessTokens,
                    Is.EquivalentTo(CampaignCatalog.MilestoneTokenIds), "seed " + seed);
                Assert.That(map.Progression.Edges.Select(edge => edge.From + ">" + edge.To), Is.EqualTo(new[]
                {
                    "catalyst>milestone1", "milestone1>conflict", "conflict>hidden-truth",
                    "hidden-truth>milestone3", "milestone3>convergence"
                }), "seed " + seed);

                Assert.That(map.Routes.Count(route => route.IsLoop), Is.GreaterThanOrEqualTo(2));
                Assert.That(map.Routes.Where(route => route.Kind == "major").All(route => route.Width >= 3),
                    Is.True);
                Assert.That(map.Routes.Where(route => route.IsGated).All(route =>
                    route.RequiredAdminAccess == 3 &&
                    route.RequiredAccessTokens.ToArray().OrderBy(value => value)
                        .SequenceEqual(CampaignCatalog.MilestoneTokenIds.OrderBy(value => value))), Is.True);

                Assert.That(map.Progression.Nodes.All(node =>
                    areas.TryGetValue(node.AreaId, out WorldArea area) &&
                    slots.TryGetValue(node.SlotId, out PlacementSlot slot) &&
                    slot.AreaId == area.Id && area.CampaignRole == node.CampaignRole &&
                    node.CandidatePaths.Count > 0), Is.True);
                Assert.That(map.Areas.Where(area => area.Id != finaleArea.Id).All(area =>
                    GridPathfinder.FindPath(map, map.Start, area.Center,
                        coord => map.AreaAt(coord)?.Id == finaleArea.Id).Count > 0), Is.True);
                Assert.That(map.GenerationReport.IsValid, Is.True);
                Assert.That(DeterministicRegionGenerator.ComputeLayoutHash(map), Is.EqualTo(map.LayoutHash));
            }
        }

        [Test]
        public void Demo_UsesNinjaAdventureHeroAndSeededCampaignData()
        {
            const long seed = 13;
            LocalTurnService service = LocalTurnService.CreateDemo(seed);
            RunView view = service.CurrentView;
            CampaignBlueprint blueprint = CampaignCatalog.Create(seed);
            EntityView player = view.Entities.Single(entity => entity.EntityId == view.PlayerEntityId);

            Assert.That(view.CampaignId, Is.EqualTo(blueprint.Id));
            Assert.That(view.CampaignTitle, Is.EqualTo(blueprint.Title));
            Assert.That(view.CampaignPremise, Is.EqualTo(blueprint.Premise));
            Assert.That(player.DisplayName, Is.EqualTo(blueprint.PlayerName));
            Assert.That(player.AssetId, Is.EqualTo(blueprint.PlayerAssetId));
            Assert.That(view.Region.Width, Is.EqualTo(160));
            Assert.That(view.Region.Height, Is.EqualTo(160));
            Assert.That(view.Region.Biomes, Has.Count.EqualTo(6));
        }

        [Test]
        public void SafeExplorationMove_DoesNotConsumeD20OrCampaignTurn()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(41, new SequenceD20Source(20));
            RunView before = service.CurrentView;
            GridCoord destination = FirstSafeDestination(before);
            TurnResponse moved = service.Submit(new TurnRequest("safe-travel", before.Version,
                AbilityKind.Move, null, destination, "이미 확인된 안전한 길을 따라 이동한다"));

            Assert.That(moved.IsSuccess, Is.True);
            Assert.That(moved.ConsumesCampaignTurn, Is.False);
            Assert.That(moved.D20, Is.Zero);
            Assert.That(moved.Run.CurrentTurn, Is.EqualTo(before.CurrentTurn));
            Assert.That(moved.Run.Region.LayoutHash, Is.EqualTo(before.Region.LayoutHash));
        }

        [Test]
        public void MilestoneProgress_RequiresCorrectRoleDesignatedEvidenceAndSuccess()
        {
            LocalTurnService service = StateAtBeatNear(44, 1, "slot-milestone-1", out EntityView token,
                new SequenceD20Source(1, 20, 20));

            TurnResponse failed = service.Submit(new TurnRequest("failed-token", service.CurrentView.Version,
                AbilityKind.Interact, token.EntityId, null, "첫 표식의 흔적을 조사한다"));
            Assert.That(failed.Run.MilestoneProgress, Is.Zero);

            TurnResponse granted = service.Submit(new TurnRequest("successful-token", failed.Run.Version,
                AbilityKind.Interact, token.EntityId, null, "지역의 위기와 연결된 첫 표식을 확인한다"));
            Assert.That(granted.Run.MilestoneProgress, Is.EqualTo(1));
            Assert.That(granted.Run.Inventory, Does.Contain("MILESTONE_TOKEN_1"));
            Assert.That(granted.Events.Any(value =>
                value.StartsWith("MILESTONE_TOKEN_ACQUIRED:1:MILESTONE_TOKEN_1", StringComparison.Ordinal)), Is.True);

            TurnResponse repeated = service.Submit(new TurnRequest("repeat-token", granted.Run.Version,
                AbilityKind.Interact, token.EntityId, null, "이미 확인한 표식을 다시 조사한다"));
            Assert.That(repeated.Run.MilestoneProgress, Is.EqualTo(1));
        }

        [Test]
        public void SaveRoundTrip_PreservesSeededRolesTokensMetricsAndImmutableMap()
        {
            LocalTurnService service = StateAtBeatNear(45, 1, "slot-milestone-1", out EntityView token,
                new SequenceD20Source(20));
            service.Submit(new TurnRequest("save-token", service.CurrentView.Version, AbilityKind.Interact,
                token.EntityId, null, "첫 핵심 표식과 지역의 이해관계를 확정한다"));

            string json = LocalRunSaveService.Serialize(service);
            LocalTurnService loaded = LocalRunSaveService.Deserialize(json);
            RunView restored = loaded.CurrentView;

            Assert.That(restored.Region.LayoutHash, Is.EqualTo(service.CurrentView.Region.LayoutHash));
            Assert.That(restored.CampaignId, Is.EqualTo(service.CurrentView.CampaignId));
            Assert.That(restored.MilestoneProgress, Is.EqualTo(1));
            Assert.That(restored.Inventory, Does.Contain("MILESTONE_TOKEN_1"));
            Assert.That(restored.RequiredBeats.Select(beat => beat.RoleId),
                Is.EqualTo(CampaignCatalog.RoleIds));
            Assert.That(restored.RequiredBeats[1].MilestoneTokenId, Is.EqualTo("MILESTONE_TOKEN_1"));
        }

        [TestCase("SAFE_PASSAGE")]
        [TestCase("SHARED_GUARDIANSHIP")]
        [TestCase("FREE_WORLD")]
        [TestCase("MEMORY_REWEAVE")]
        [TestCase("THREAT_SEAL")]
        public void GenericFinaleRecipe_SelectsCandidateFromSpatialState(string endingCode)
        {
            long seed = FindSeedForEnding(endingCode);
            RunState state = FinaleReadyState(seed);
            ConfigureEndingRecipe(state, endingCode);

            Assert.That(state.EndingCandidates.Select(value => value.Code), Does.Contain(endingCode));
            Assert.That(CampaignDirector.SelectEnding(state), Is.EqualTo(endingCode));
        }

        [Test]
        public void IncompleteFinale_FallsBackToLastResort()
        {
            RunState state = FinaleReadyState(46);
            Assert.That(CampaignDirector.SelectEnding(state), Is.EqualTo(CampaignCatalog.FallbackEndingCode));
        }

        [Test]
        public void CampaignDirector_ExposesSixPhasesAndFortyTurnDefault()
        {
            Assert.That(CampaignDirector.Evaluate(0, 40).Phase, Is.EqualTo(CampaignPhase.Introduction));
            Assert.That(CampaignDirector.Evaluate(5, 40).Phase, Is.EqualTo(CampaignPhase.Adaptation));
            Assert.That(CampaignDirector.Evaluate(16, 40).Phase, Is.EqualTo(CampaignPhase.Expansion));
            Assert.That(CampaignDirector.Evaluate(26, 40).Phase, Is.EqualTo(CampaignPhase.Truth));
            Assert.That(CampaignDirector.Evaluate(36, 40).Phase, Is.EqualTo(CampaignPhase.Backflow));
            Assert.That(CampaignDirector.Evaluate(43, 50).Phase, Is.EqualTo(CampaignPhase.Finale));
            Assert.That(LocalTurnService.CampaignTurnLimit, Is.EqualTo(40));
            Assert.That(LocalTurnService.MaximumCampaignTurnLimit, Is.EqualTo(50));
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

        private static string BlueprintFingerprint(CampaignBlueprint blueprint)
        {
            return blueprint.Id + "|" + blueprint.Title + "|" + blueprint.Premise + "|" +
                   string.Join(";", blueprint.NpcNames) + "|" + string.Join(";", blueprint.QuestSeeds) + "|" +
                   string.Join(";", blueprint.Endings.Select(value => value.Code));
        }

        private static string RoleMapping(RegionMap map)
        {
            return string.Join("|", map.Areas.Where(area => !string.IsNullOrEmpty(area.CampaignRole))
                .OrderBy(area => area.CampaignRole)
                .Select(area => area.CampaignRole + "=" + area.Id + "@" + area.Center));
        }

        private static GridCoord FirstSafeDestination(RunView view)
        {
            for (int distance = 1; distance <= 12; distance++)
            {
                for (int y = view.PlayerPosition.Y - distance; y <= view.PlayerPosition.Y + distance; y++)
                {
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
                }
            }
            Assert.Fail("No safe travel destination found.");
            return default;
        }

        private static LocalTurnService StateAtBeatNear(long seed, int beatIndex, string slotId,
            out EntityView selected, ID20Source rolls)
        {
            RunState state = LocalTurnService.CreateDemo(seed).CreateSnapshot();
            for (int index = 0; index < beatIndex; index++) state.RequiredBeats[index].IsCompleted = true;
            state.CurrentBeatIndex = beatIndex;
            Assert.That(state.Region.TryGetPlacementSlot(slotId, out PlacementSlot slot), Is.True);
            EntityState target = state.Spatial.Entities.First(entity => entity.IsActive && entity.Position == slot.Coord);
            MovePlayerNear(state, target);
            selected = new EntityView(target);
            return new LocalTurnService(state, rolls);
        }

        private static void MovePlayerNear(RunState state, EntityState target)
        {
            GridCoord destination = default;
            bool found = false;
            for (int distance = 1; distance <= 2 && !found; distance++)
            {
                for (int y = target.Position.Y - distance; y <= target.Position.Y + distance && !found; y++)
                {
                    for (int x = target.Position.X - distance; x <= target.Position.X + distance; x++)
                    {
                        var candidate = new GridCoord(x, y);
                        if (candidate.ManhattanDistance(target.Position) != distance ||
                            !state.Region.IsWalkable(candidate)) continue;
                        if (!state.Spatial.IsBlockingOccupied(state.Region.RegionId, candidate, 0,
                                state.PlayerEntityId))
                        {
                            destination = candidate;
                            found = true;
                            break;
                        }
                    }
                }
            }
            Assert.That(found, Is.True, "fixture needs an empty tile near " + target.DisplayName);
            MoveResult moved = state.Spatial.TryMove(state.PlayerEntityId, state.Region.RegionId,
                destination, 0, (regionId, coord) =>
                    regionId == state.Region.RegionId && state.Region.IsWalkable(coord));
            Assert.That(moved.IsSuccess, Is.True);
        }

        private static long FindSeedForEnding(string code)
        {
            for (long seed = 0; seed < 1000; seed++)
                if (CampaignCatalog.Create(seed).Endings.Any(candidate => candidate.Code == code)) return seed;
            Assert.Fail("No deterministic seed selected ending candidate " + code);
            return 0;
        }

        private static RunState FinaleReadyState(long seed)
        {
            RunState state = LocalTurnService.CreateDemo(seed).CreateSnapshot();
            state.MilestoneProgress = 3;
            state.WorldStability = 55;
            state.WorldAutonomy = 55;
            state.PublicTrust = 55;
            state.TechnicalDebt = 20;
            state.CompanionBond = 55;
            for (int i = 0; i < state.RequiredBeats.Count; i++) state.RequiredBeats[i].IsCompleted = true;
            state.CurrentBeatIndex = state.RequiredBeats.Count;
            return state;
        }

        private static void ConfigureEndingRecipe(RunState state, string code)
        {
            switch (code)
            {
                case "SAFE_PASSAGE":
                    RemoveAsset(state, "finale.threat");
                    Link(state, "finale.anchor", "finale.safeguard");
                    Link(state, "finale.safeguard", "finale.passage");
                    break;
                case "SHARED_GUARDIANSHIP":
                    Link(state, "finale.anchor", "finale.safeguard");
                    Link(state, "finale.safeguard", "finale.witness");
                    break;
                case "FREE_WORLD":
                    LinkPlayer(state, "finale.anchor");
                    Link(state, "finale.anchor", "finale.freedom");
                    break;
                case "MEMORY_REWEAVE":
                    Link(state, "finale.anchor", "finale.memory");
                    Link(state, "finale.memory", "finale.safeguard");
                    break;
                case "THREAT_SEAL":
                    RemoveAsset(state, "finale.threat");
                    Link(state, "finale.safeguard", "finale.witness");
                    break;
                default:
                    Assert.Fail("Unknown finale recipe " + code);
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
