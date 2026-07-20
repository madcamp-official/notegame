using System;
using System.Collections.Generic;
using System.Linq;
using KeyboardWanderer.Core;
using KeyboardWanderer.Gameplay;
using NUnit.Framework;

namespace KeyboardWanderer.Tests
{
    /// <summary>코드 리뷰에서 확인된 캠페인 중단과 결말 도달 불가 문제를 고정하는 회귀 테스트다.</summary>
    public sealed class GameValueRegressionTests
    {
        [Test]
        public void SeedVariant_EnemyDependenciesAreDeterministicAndVaryAcrossSeeds()
        {
            Guid enemyId = Guid.Parse("12345678-1234-5678-9abc-def012345678");
            EnemyDependencyArchetype first = EnemyArchetypeCatalog.Resolve("enemy.slime.v1", 100L, enemyId);
            Assert.That(EnemyArchetypeCatalog.Resolve("enemy.slime.v1", 100L, enemyId), Is.EqualTo(first));

            var variants = new HashSet<EnemyDependencyArchetype>();
            for (long seed = 100; seed < 160; seed++)
                variants.Add(EnemyArchetypeCatalog.Resolve("enemy.slime.v1", seed, enemyId));
            Assert.That(variants.Count, Is.GreaterThan(1));
            Assert.That(EnemyArchetypeCatalog.Resolve("enemy.dragon.v1", 100L, enemyId),
                Is.EqualTo(EnemyDependencyArchetype.RootProcess), "명시적 적 정체성은 시드 변형보다 우선해야 합니다.");
        }

        [Test]
        public void CapabilityCatalog_DistinguishesWorldEditingFromEntityKind()
        {
            EntityCapabilities removableSystem = EntityCapabilityCatalog.Resolve(EntityKind.Prop, true,
                false, false, false, "finale.threat");
            EntityCapabilities ordinaryProp = EntityCapabilityCatalog.Resolve(EntityKind.Prop, true,
                false, false, true, "world.crate");
            EntityCapabilities hostile = EntityCapabilityCatalog.Resolve(EntityKind.Enemy, true,
                true, false, false, "enemy.test");

            Assert.That(removableSystem.CanDelete, Is.True);
            Assert.That(removableSystem.RequiredAdminAccess, Is.EqualTo(3));
            Assert.That(removableSystem.GrantsDefeatReward, Is.False);
            Assert.That(ordinaryProp.CanCopy, Is.True);
            Assert.That(ordinaryProp.CanInteract, Is.True);
            Assert.That(hostile.CanDelete, Is.True);
            Assert.That(hostile.CanConnect, Is.False);
            Assert.That(hostile.GrantsDefeatReward, Is.True);
        }

        [Test]
        public void PartialSearch_UsesAbilitySpecificNoiseConsequence()
        {
            RunState state = LocalTurnService.CreateDemo(20260720L).CreateSnapshot();
            EntityState player = state.Spatial.Entities.First(entity => entity.EntityId == state.PlayerEntityId);
            EntityState target = state.Spatial.Entities.First(entity => entity.IsActive &&
                entity.EntityId != state.PlayerEntityId && player.Position.ManhattanDistance(entity.Position) <= 6);
            var request = TurnRequest.UseSkill("partial-search", state.Version, AbilityKind.Search,
                target.EntityId);
            RulePreparation preparation = new RuleEngine().Prepare(state, request);

            Assert.That(preparation.IsValid, Is.True, preparation.ErrorMessage);
            List<string> events = new RuleEngine().Apply(state, request, preparation,
                RuleOutcome.PartialSuccess, state.CurrentTurn + 1, 2);

            Assert.That(events.Any(value => value.StartsWith("PARTIAL_SEARCH_NOISE:",
                StringComparison.Ordinal)), Is.True);
            Assert.That(events.Any(value => value.StartsWith("STATUS_ADDED:",
                StringComparison.Ordinal)), Is.False,
                "부분 성공은 더 이상 모든 스킬에 같은 generic 이벤트를 사용하지 않아야 합니다.");
            Assert.That(state.IsExposed, Is.True);
        }

        [Test]
        public void TechnicalDebtThresholds_TriggerOnceAndMutateTheWorld()
        {
            RunState state = LocalTurnService.CreateDemo(20260720L).CreateSnapshot();
            state.TechnicalDebt = 75;
            EntityState player = state.Spatial.Entities.First(entity => entity.EntityId == state.PlayerEntityId);
            int healthBefore = player.Health;
            var request = TurnRequest.UseSkill("debt-threshold", state.Version, AbilityKind.Search);
            var events = new List<string>();

            CampaignDirector.ProcessCommittedTurn(state, request, ActionContext.Investigation,
                RuleOutcome.Success, state.CurrentTurn + 1, events);

            Assert.That(events.Any(value => value.StartsWith("DEBT_BACKFLOW_CLONE_DRIFT:", StringComparison.Ordinal)), Is.True);
            Assert.That(events.Any(value => value.StartsWith("DEBT_BACKFLOW_HOSTILE_SPAWNED:", StringComparison.Ordinal) ||
                                            value.StartsWith("DEBT_BACKFLOW_BLOCKED:", StringComparison.Ordinal)), Is.True);
            Assert.That(events.Any(value => value.StartsWith("DEBT_PARADOX_SURGE:", StringComparison.Ordinal)), Is.True);
            Assert.That(player.Health, Is.LessThan(healthBefore));
            Assert.That(state.IsExposed, Is.True);

            var repeatedEvents = new List<string>();
            CampaignDirector.ProcessCommittedTurn(state, request, ActionContext.Investigation,
                RuleOutcome.Success, state.CurrentTurn + 2, repeatedEvents);
            Assert.That(repeatedEvents.Any(value => value.StartsWith("DEBT_BACKFLOW_", StringComparison.Ordinal) ||
                                                    value.StartsWith("DEBT_PARADOX_", StringComparison.Ordinal)), Is.False,
                "저장되는 threshold marker가 같은 부채 사건의 반복 발동을 막아야 합니다.");
        }

        [Test]
        public void ReopenedCrate_SpendsGoldForFocusAndEnemyXpRaisesMastery()
        {
            RunState state = LocalTurnService.CreateDemo(20260720L).CreateSnapshot();
            EntityState crate = state.Spatial.Entities.First(entity => entity.IsActive &&
                entity.AssetId.StartsWith("item.crate", StringComparison.Ordinal));
            var interact = TurnRequest.UseSkill("supply-first", state.Version, AbilityKind.Interact,
                crate.EntityId);
            var engine = new RuleEngine();
            RulePreparation interaction = RulePreparation.Valid("test interaction", 0, 0, 0, null,
                crate.EntityId);
            engine.Apply(state, interact, interaction, RuleOutcome.Success, 1);
            state.Focus = Math.Max(0, state.MaxFocus - 3);
            state.Gold = 5;
            List<string> purchaseEvents = engine.Apply(state, interact, interaction, RuleOutcome.Success, 2);
            Assert.That(state.Gold, Is.EqualTo(3));
            Assert.That(state.Focus, Is.EqualTo(state.MaxFocus - 1));
            Assert.That(purchaseEvents.Any(value => value.StartsWith("SUPPLY_PURCHASED:",
                StringComparison.Ordinal)), Is.True);

            EntityState enemy = state.Spatial.Entities.First(entity => entity.IsActive &&
                entity.Kind == EntityKind.Enemy);
            state.Spatial.TryDamage(enemy.EntityId, Math.Max(0, enemy.Health - 1), out _, out _, out _);
            state.AddCanonicalFact(EnemyArchetypeCatalog.RevealedFact(enemy.EntityId));
            state.Experience = 9;
            int maxFocusBefore = state.MaxFocus;
            var delete = TurnRequest.UseSkill("mastery-kill", state.Version, AbilityKind.Delete, enemy.EntityId);
            RulePreparation deletion = RulePreparation.Valid("test delete", 0, 0, 0, null, enemy.EntityId);
            List<string> rewardEvents = engine.Apply(state, delete, deletion, RuleOutcome.Success, 3);
            Assert.That(state.MaxFocus, Is.EqualTo(maxFocusBefore + 1));
            Assert.That(rewardEvents.Any(value => value.StartsWith("MASTERY_RANK_INCREASED:",
                StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void EndingBoard_UsesActualRecipeInsteadOfOnlyRootAccess()
        {
            RunState state = LocalTurnService.CreateDemo(20260720L).CreateSnapshot();
            state.AdminAccess = 3;
            for (int i = 0; i < CampaignCatalog.AdminAccessLevelIds.Length; i++)
                if (!state.Inventory.Contains(CampaignCatalog.AdminAccessLevelIds[i]))
                    state.Inventory.Add(CampaignCatalog.AdminAccessLevelIds[i]);
            state.AddCanonicalFact("붕괴 원인이 관리자 통제 시스템 내부에 있음을 확정했다.");
            for (int i = 0; i < state.RequiredBeats.Count; i++) state.RequiredBeats[i].IsCompleted = true;

            IReadOnlyList<EndingConditionReport> reports = CampaignDirector.EvaluateEndingBoard(state);
            CampaignDirector.UpdateEndingEligibility(state);

            Assert.That(CampaignDirector.CanEnterRootSystem(state), Is.True);
            Assert.That(reports.Count, Is.EqualTo(6));
            Assert.That(reports.All(report => !report.IsEligible), Is.True,
                "Root 접근만으로 연결·노드·메트릭 결말 조건이 충족됐다고 표시하면 안 됩니다.");
            Assert.That(reports[0].Conditions.Any(condition => !condition.IsSatisfied), Is.True);
            Assert.That(state.EndingCandidates.Where(candidate =>
                candidate.Code != CampaignCatalog.FallbackEndingCode).All(candidate => !candidate.IsEligible), Is.True);
        }

        [Test]
        public void NpcPromise_PersistsInMemoryAndGrantsNearbySupport()
        {
            RunState state = LocalTurnService.CreateDemo(93L).CreateSnapshot();
            EntityState npc = state.Spatial.Entities.First(entity => entity.Kind == EntityKind.Npc && entity.IsActive);
            MovePlayerNear(state, npc, 4);
            var engine = new RuleEngine();
            var search = TurnRequest.UseSkill("promise-search-before", state.Version, AbilityKind.Search, npc.EntityId);
            int modifierBefore = engine.Prepare(state, search).Modifier;
            var connect = TurnRequest.UseSkill("promise-connect", state.Version, AbilityKind.Connect,
                state.PlayerEntityId, npc.EntityId);
            RulePreparation connectPreparation = engine.Prepare(state, connect);
            List<string> events = engine.Apply(state, connect, connectPreparation, RuleOutcome.Success, 1);
            CampaignDirector.ProcessCommittedTurn(state, connect, ActionContext.Negotiation,
                RuleOutcome.Success, 1, events);
            int modifierAfter = engine.Prepare(state, search).Modifier;

            NpcStoryState story = state.NpcStories.Single(value => value.EntityId == npc.EntityId);
            Assert.That(story.Memories.Any(memory => memory.StartsWith("[약속]", StringComparison.Ordinal)), Is.True);
            Assert.That(events.Any(value => value.StartsWith("NPC_PROMISE_MADE:", StringComparison.Ordinal)), Is.True);
            Assert.That(modifierAfter, Is.EqualTo(modifierBefore + 1));
        }

        [Test]
        public void NpcInvestigation_RevealsStoryOnceAndDoesNotDuplicateRewards()
        {
            RunState state = LocalTurnService.CreateDemo(20260720L).CreateSnapshot();
            EntityState npc = state.Spatial.Entities.First(entity => entity.Kind == EntityKind.Npc && entity.IsActive);
            MovePlayerNear(state, npc, 4);
            var engine = new RuleEngine();
            var first = TurnRequest.UseSkill("npc-story-first", state.Version, AbilityKind.Search, npc.EntityId);
            RulePreparation preparation = engine.Prepare(state, first);

            Assert.That(preparation.IsValid, Is.True, preparation.ErrorMessage);
            List<string> firstEvents = engine.Apply(state, first, preparation, RuleOutcome.Success, 1);
            NpcStoryState story = state.FindNpcStory(npc.EntityId);
            int trustAfterFirst = story.Trust;

            Assert.That(firstEvents.Any(value => value.StartsWith("NPC_CLUE_REVEALED:", StringComparison.Ordinal)), Is.True);
            Assert.That(story.RevealedClues, Does.Contain("personal-secret"));
            Assert.That(story.Memories.Last(), Does.Contain("“"));
            Assert.That(state.CanonicalFacts.Any(value => value.Contains(story.Secret)), Is.True);

            var repeat = TurnRequest.UseSkill("npc-story-repeat", state.Version, AbilityKind.Search, npc.EntityId);
            List<string> repeatEvents = engine.Apply(state, repeat, engine.Prepare(state, repeat),
                RuleOutcome.CriticalSuccess, 2);

            Assert.That(repeatEvents.Any(value => value.StartsWith("NPC_INVESTIGATION_REPEAT:", StringComparison.Ordinal)), Is.True);
            Assert.That(story.Trust, Is.EqualTo(trustAfterFirst), "같은 단서를 반복 조사해 신뢰 보상을 파밍하면 안 됩니다.");
            Assert.That(story.RevealedClues.Count, Is.EqualTo(1));
        }

        [Test]
        public void RootProcess_RequiresSearchBeforeDelete()
        {
            RunState state = LocalTurnService.CreateDemo(20260720L).CreateSnapshot();
            EntityState target = state.Spatial.Entities.First(entity => entity.IsActive &&
                entity.Kind == EntityKind.Enemy && EnemyArchetypeCatalog.Resolve(entity.AssetId) ==
                EnemyDependencyArchetype.RootProcess);
            MovePlayerNear(state, target, 3);
            var engine = new RuleEngine();
            var delete = TurnRequest.UseSkill("root-delete", state.Version, AbilityKind.Delete, target.EntityId);
            RulePreparation denied = engine.Prepare(state, delete);
            var search = TurnRequest.UseSkill("root-search", state.Version, AbilityKind.Search, target.EntityId);
            RulePreparation searchPreparation = engine.Prepare(state, search);
            engine.Apply(state, search, searchPreparation, RuleOutcome.Success, 1);
            RulePreparation allowed = engine.Prepare(state, delete);

            Assert.That(denied.IsValid, Is.False);
            Assert.That(denied.ErrorCode, Is.EqualTo(TurnErrorCode.QuestConditionMissing));
            Assert.That(searchPreparation.IsValid, Is.True);
            Assert.That(allowed.IsValid, Is.True);
        }

        [Test]
        public void UnsearchedCacheEnemy_ReplicatesWhenDeleted()
        {
            RunState state = LocalTurnService.CreateDemo(20260720L).CreateSnapshot();
            EntityState target = state.Spatial.Entities.First(entity => entity.IsActive &&
                entity.Kind == EntityKind.Enemy && EnemyArchetypeCatalog.Resolve(entity.AssetId) ==
                EnemyDependencyArchetype.CacheReplicator);
            state.Spatial.TryDamage(target.EntityId, Math.Max(0, target.Health - 1), out _, out _, out _);
            MovePlayerNear(state, target, 3);
            int enemiesBefore = state.Spatial.Entities.Count(entity => entity.IsActive && entity.Kind == EntityKind.Enemy);
            var request = TurnRequest.UseSkill("cache-delete", state.Version, AbilityKind.Delete, target.EntityId);
            RulePreparation preparation = new RuleEngine().Prepare(state, request);
            List<string> events = new RuleEngine().Apply(state, request, preparation, RuleOutcome.Success, 1);

            Assert.That(events.Any(value => value.StartsWith("CACHE_ENEMY_REPLICATED:", StringComparison.Ordinal)), Is.True);
            Assert.That(state.Spatial.Entities.Count(entity => entity.IsActive && entity.Kind == EntityKind.Enemy),
                Is.EqualTo(enemiesBefore), "원본이 쓰러져도 조사하지 않은 Cache 복제본 하나가 남아야 합니다.");
        }

        [Test]
        public void AdminAccessTwo_DeleteRoute_CompletesWhenDesignatedEnemyIsDefeated()
        {
            long seed = FindSeedForSecondAccessDeleteRoute();
            RunState state = LocalTurnService.CreateDemo(seed).CreateSnapshot();
            for (int i = 0; i < 3; i++)
                state.RequiredBeats[i].IsCompleted = true;
            state.CurrentBeatIndex = 3;
            state.AdminAccess = 1;
            state.Inventory.Add(CampaignCatalog.AdminAccessLevelIds[0]);
            EntityState candidate = state.Spatial.Entities.Single(entity => entity.IsActive &&
                entity.AssetId == "story.admin-access-2.combat");
            MovePlayerNear(state, candidate, 3);
            var engine = new RuleEngine();

            TurnRequest firstRequest = TurnRequest.UseSkill("access-two-delete-1", state.Version,
                AbilityKind.Delete, candidate.EntityId);
            RulePreparation firstPreparation = engine.Prepare(state, firstRequest);
            Assert.That(firstPreparation.IsValid, Is.True);
            List<string> firstEvents = engine.Apply(state, firstRequest, firstPreparation, RuleOutcome.Success, 1);
            CampaignDirector.ProcessCommittedTurn(state, firstRequest, ActionContext.Combat,
                RuleOutcome.Success, 1, firstEvents);
            Assert.That(state.AdminAccess, Is.EqualTo(1), "첫 타격만으로는 권한을 주면 안 된다.");

            TurnRequest secondRequest = TurnRequest.UseSkill("access-two-delete-2", state.Version,
                AbilityKind.Delete, candidate.EntityId);
            RulePreparation secondPreparation = engine.Prepare(state, secondRequest);
            Assert.That(secondPreparation.IsValid, Is.True);
            List<string> secondEvents = engine.Apply(state, secondRequest, secondPreparation,
                RuleOutcome.Success, 2);
            CampaignDirector.ProcessCommittedTurn(state, secondRequest, ActionContext.Combat,
                RuleOutcome.Success, 2, secondEvents);

            Assert.That(secondEvents.Count(value => value.StartsWith("ENEMY_DEFEATED:",
                StringComparison.Ordinal)), Is.EqualTo(1));
            Assert.That(state.RequiredBeats[3].IsCompleted, Is.True);
            Assert.That(state.AdminAccess, Is.EqualTo(2));
            Assert.That(state.Inventory, Does.Contain(CampaignCatalog.AdminAccessLevelIds[1]));
        }

        [TestCase("finale.threat")]
        [TestCase("finale.freedom")]
        public void RootSystemRemovableComponent_RequiresAdminThreeAndCanBeDeleted(string assetId)
        {
            RunState state = LocalTurnService.CreateDemo(20260720).CreateSnapshot();
            EntityState component = state.Spatial.Entities.Single(entity => entity.AssetId == assetId);
            MovePlayerNear(state, component, 3);
            var engine = new RuleEngine();
            TurnRequest request = TurnRequest.UseSkill("remove-" + assetId, state.Version,
                AbilityKind.Delete, component.EntityId);

            RulePreparation denied = engine.Prepare(state, request);
            state.AdminAccess = 3;
            RulePreparation allowed = engine.Prepare(state, request);
            int experienceBefore = state.Experience;
            int goldBefore = state.Gold;
            List<string> events = engine.Apply(state, request, allowed, RuleOutcome.Success, 1);

            Assert.That(denied.IsValid, Is.False);
            Assert.That(denied.ErrorCode, Is.EqualTo(TurnErrorCode.QuestConditionMissing));
            Assert.That(allowed.IsValid, Is.True);
            Assert.That(component.IsActive, Is.False);
            Assert.That(events.Any(value => value.StartsWith("ENTITY_REMOVED:" + component.EntityId,
                StringComparison.OrdinalIgnoreCase)), Is.True);
            Assert.That(events.Any(value => value.StartsWith("ENEMY_DEFEATED:",
                StringComparison.Ordinal)), Is.False);
            Assert.That(state.Experience, Is.EqualTo(experienceBefore));
            Assert.That(state.Gold, Is.EqualTo(goldBefore));
        }

        [Test]
        public void ConnectWithNpc_IncreasesCompanionBondAndExplainsWhy()
        {
            RunState state = LocalTurnService.CreateDemo(93).CreateSnapshot();
            EntityState npc = state.Spatial.Entities.First(entity => entity.Kind == EntityKind.Npc && entity.IsActive);
            MovePlayerNear(state, npc, 4);
            var engine = new RuleEngine();
            TurnRequest request = TurnRequest.UseSkill("bond-through-connect", state.Version,
                AbilityKind.Connect, state.PlayerEntityId, npc.EntityId);
            RulePreparation preparation = engine.Prepare(state, request);
            int before = state.CompanionBond;

            Assert.That(preparation.IsValid, Is.True);
            List<string> events = engine.Apply(state, request, preparation, RuleOutcome.Success, 1);
            CampaignDirector.ProcessCommittedTurn(state, request, ActionContext.Negotiation,
                RuleOutcome.Success, 1, events);

            Assert.That(state.CompanionBond, Is.EqualTo(before + 5));
            Assert.That(events, Does.Contain("COMPANION_BOND_CHANGED:+5:reason=NPC_CONNECTION"));
            Assert.That(state.NpcStories.Single(story => story.EntityId == npc.EntityId).Affinity,
                Is.EqualTo(1));
        }

        private static long FindSeedForSecondAccessDeleteRoute()
        {
            for (long seed = 0; seed < 500; seed++)
                if (CampaignCatalog.Create(seed).AdminAccessBindings[1].SelectedSkill == AbilityKind.Delete)
                    return seed;
            Assert.Fail("권한 II Delete 경로를 만드는 시드를 찾지 못했다.");
            return 0;
        }

        private static void MovePlayerNear(RunState state, EntityState target, int maximumDistance)
        {
            for (int distance = 1; distance <= maximumDistance; distance++)
                for (int y = target.Position.Y - distance; y <= target.Position.Y + distance; y++)
                    for (int x = target.Position.X - distance; x <= target.Position.X + distance; x++)
                    {
                        var candidate = new GridCoord(x, y);
                        if (candidate.ManhattanDistance(target.Position) != distance ||
                            !state.Region.IsWalkable(candidate) ||
                            state.Spatial.IsBlockingOccupied(state.Region.RegionId, candidate, 0,
                                state.PlayerEntityId))
                            continue;
                        MoveResult moved = state.Spatial.TryMove(state.PlayerEntityId, state.Region.RegionId,
                            candidate, 0, (regionId, coord) =>
                                regionId == state.Region.RegionId && state.Region.IsWalkable(coord));
                        Assert.That(moved.IsSuccess, Is.True, moved.ErrorCode);
                        return;
                    }
            Assert.Fail("대상 근처의 빈 타일을 찾지 못했다: " + target.DisplayName);
        }
    }
}
