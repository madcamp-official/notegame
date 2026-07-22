using System;
using System.Collections.Generic;
using System.Linq;
using KeyboardWanderer.Core;
using KeyboardWanderer.Gameplay;
using NUnit.Framework;

namespace KeyboardWanderer.Tests
{
    /// <summary>
    /// A release-path playthrough which treats LocalTurnService exactly like a player-facing server:
    /// it reads RunView, submits MOVE / USE_SKILL requests, and uses the public save codec. It never
    /// edits RunState, spatial state, campaign beats, access, metrics, or an ending recipe directly.
    /// </summary>
    public sealed class FullCampaignPlaythroughTests
    {
        private const long CampaignSeed = 6L;
        private const string ExpectedEnding = "ENDING_OPEN_FRONTIER";
        private int _commandSequence;

        [Test]
        [Timeout(60000)]
        public void FreshRun_PublicCommands_ReachesNormalEnding_AndRoundTripsAuthoritativeSave()
        {
            var service = LocalTurnService.CreateDemo(CampaignSeed, new SequenceD20Source(20), 30);
            string immutableLayoutHash = service.CurrentView.Region.LayoutHash;
            AssertFreshRun(service.CurrentView);

            EntityView keyboard = FindAsset(service.CurrentView, CampaignCatalog.AdministratorKeyboardId);
            MoveWithinRange(service, keyboard.EntityId, 6, "arrival-keyboard");
            TurnResponse arrival = SubmitSkill(service, AbilityKind.Search, keyboard.EntityId,
                null, "arrival-search");
            AssertBeatCompleted(service.CurrentView, 0, "arrival");
            Assert.That(arrival.Events, Has.Some.StartsWith("ADMIN_KEYBOARD_AWAKENED:"));

            SubmitSkill(service, AbilityKind.Search, keyboard.EntityId, null, "collapse-search");
            AssertBeatCompleted(service.CurrentView, 1, "collapse");

            EntityView firstAccess = FindAsset(service.CurrentView,
                "story.admin-access-1.investigation");
            MoveWithinRange(service, firstAccess.EntityId, 6, "access-one");
            SubmitSkill(service, AbilityKind.Search, firstAccess.EntityId, null, "access-one-search");
            AssertAccess(service.CurrentView, 1, AbilityKind.Search);
            AssertBeatCompleted(service.CurrentView, 2, "admin-access-1");

            // A real combat detour proves damage, enemy response, death, rewards, and victory state.
            EntityView enemy = service.CurrentView.Entities
                .Where(entity => entity.Kind == EntityKind.Enemy && entity.IsHostile)
                .OrderBy(entity => entity.Position.ManhattanDistance(service.CurrentView.PlayerPosition))
                .ThenBy(entity => entity.EntityId)
                .First();
            int enemyMaxHealth = enemy.MaxHealth;
            int experienceBeforeCombat = service.CurrentView.Experience;
            int goldBeforeCombat = service.CurrentView.Gold;
            MoveWithinRange(service, enemy.EntityId, 6, "combat-investigation");
            TurnResponse investigated = SubmitSkill(service, AbilityKind.Search, enemy.EntityId,
                null, "combat-search");
            Assert.That(investigated.Events, Has.Some.StartsWith("ENEMY_DEPENDENCY_REVEALED:"));
            MoveWithinRange(service, enemy.EntityId, 3, "combat-approach");
            TurnResponse damaged = SubmitSkill(service, AbilityKind.Delete, enemy.EntityId,
                null, "combat-delete-hit");
            EntityView wounded = FindEntity(service.CurrentView, enemy.EntityId);
            Assert.That(wounded.Health, Is.EqualTo(enemyMaxHealth - 5));
            Assert.That(damaged.Events, Has.Some.StartsWith("ENTITY_DAMAGED:" + enemy.EntityId));
            Assert.That(investigated.Events.Concat(damaged.Events).Any(value =>
                value.StartsWith("ENEMY_MOVED:", StringComparison.Ordinal) ||
                value.StartsWith("PLAYER_DAMAGED:", StringComparison.Ordinal)), Is.True,
                "The combat phase must visibly move or attack with the engaged enemy.");

            TurnResponse defeated = SubmitSkill(service, AbilityKind.Delete, enemy.EntityId,
                null, "combat-delete-finish");
            Assert.That(service.CurrentView.Entities.Any(entity => entity.EntityId == enemy.EntityId), Is.False);
            Assert.That(defeated.Events, Has.Some.StartsWith("ENEMY_DEFEATED:" + enemy.EntityId));
            Assert.That(defeated.Events, Has.Some.StartsWith("DEFEAT_REWARD_GRANTED:" + enemy.EntityId));
            Assert.That(service.CurrentView.EnemiesDefeated, Is.EqualTo(1));
            Assert.That(service.CurrentView.Experience, Is.GreaterThan(experienceBeforeCombat));
            Assert.That(service.CurrentView.Gold, Is.GreaterThan(goldBeforeCombat));
            Assert.That(service.CurrentView.Health, Is.GreaterThan(0));
            AssertBeatCompleted(service.CurrentView, 2, "admin-access-1");

            EntityView secondAccess = FindAsset(service.CurrentView,
                "story.admin-access-2.negotiation");
            MoveWithinRange(service, secondAccess.EntityId, 4, "access-two");
            TurnResponse accessTwo = SubmitSkill(service, AbilityKind.Connect, secondAccess.EntityId,
                service.CurrentView.PlayerEntityId, "access-two-connect");
            Assert.That(accessTwo.Events, Has.Some.StartsWith("CONNECTION_CREATED:"));
            AssertAccess(service.CurrentView, 2, AbilityKind.Connect);
            AssertBeatCompleted(service.CurrentView, 3, "admin-access-2");

            EntityView internalFailure = FindAsset(service.CurrentView, "story.internal-failure");
            MoveWithinRange(service, internalFailure.EntityId, 6, "truth");
            TurnResponse truth = SubmitSkill(service, AbilityKind.Search, internalFailure.EntityId,
                null, "truth-search");
            Assert.That(truth.Events, Has.Some.StartsWith("INTERNAL_FAILURE_CONFIRMED:"));
            Assert.That(service.CurrentView.CanonicalFacts,
                Has.Some.Contains("붕괴 원인이 관리자 통제 시스템 내부"));
            AssertBeatCompleted(service.CurrentView, 4, "truth");

            // Even with the clue, the root is still a hard public-command gate at access 2/3.
            GridCoord lockedRootTile = FindEmptyRootTile(service.CurrentView);
            RunView beforeRejectedRootMove = service.CurrentView;
            TurnResponse rejectedRootMove = service.Submit(TurnRequest.Move(NextKey("root-locked"),
                beforeRejectedRootMove.Version, lockedRootTile));
            Assert.That(rejectedRootMove.IsSuccess, Is.False);
            Assert.That(rejectedRootMove.ErrorCode, Is.EqualTo(TurnErrorCode.QuestConditionMissing));
            Assert.That(service.CurrentView.Version, Is.EqualTo(beforeRejectedRootMove.Version));
            Assert.That(service.CurrentView.PlayerPosition, Is.EqualTo(beforeRejectedRootMove.PlayerPosition));

            EntityView legacyNpc = FindNpcInRole(service.CurrentView, CampaignCatalog.LegacyCitadelAxis);
            MoveWithinRange(service, legacyNpc.EntityId, 4, "debt-backflow");
            SubmitSkill(service, AbilityKind.Connect, legacyNpc.EntityId,
                service.CurrentView.PlayerEntityId, "debt-connect");
            AssertBeatCompleted(service.CurrentView, 5, "debt-backflow");

            EntityView thirdAccess = FindAsset(service.CurrentView,
                "story.admin-access-3.investigation");
            MoveWithinRange(service, thirdAccess.EntityId, 6, "access-three");
            SubmitSkill(service, AbilityKind.Search, thirdAccess.EntityId, null,
                "access-three-search");
            AssertAccess(service.CurrentView, 3, AbilityKind.Search);
            AssertBeatCompleted(service.CurrentView, 6, "admin-access-3");

            // One additional, visible NPC relationship provides the autonomy required by the chosen
            // Open Frontier recipe without touching metrics directly.
            EntityView libraryNpc = FindNpcInRole(service.CurrentView,
                CampaignCatalog.DataGrandLibraryAxis);
            MoveWithinRange(service, libraryNpc.EntityId, 4, "library-alliance");
            SubmitSkill(service, AbilityKind.Connect, libraryNpc.EntityId,
                service.CurrentView.PlayerEntityId, "library-connect");

            EntityView anchor = FindAsset(service.CurrentView, "finale.anchor");
            EntityView freedom = FindAsset(service.CurrentView, "finale.freedom");
            MoveWithinRange(service, anchor.EntityId, 4, "root-entry");
            Assert.That(service.CurrentView.Region.AreaAt(service.CurrentView.PlayerPosition).CampaignRole,
                Is.EqualTo(CampaignCatalog.RootSystemAxis));
            TurnResponse rootEntry = SubmitSkill(service, AbilityKind.Connect, anchor.EntityId,
                freedom.EntityId, "root-anchor-freedom");
            Assert.That(rootEntry.Events, Has.Some.StartsWith("CONNECTION_CREATED:"));
            AssertBeatCompleted(service.CurrentView, 7, "root-entry");

            EntityView threat = FindAsset(service.CurrentView, "finale.threat");
            MoveWithinRange(service, threat.EntityId, 3, "final-threat");
            TurnResponse deployed = SubmitSkill(service, AbilityKind.Delete, threat.EntityId,
                null, "final-threat-delete");
            Assert.That(deployed.Events, Has.Some.StartsWith("ENTITY_REMOVED:" + threat.EntityId));
            Assert.That(service.CurrentView.Entities.Any(entity => entity.EntityId == threat.EntityId), Is.False);
            AssertBeatCompleted(service.CurrentView, 8, "final-deployment");
            Assert.That(service.CurrentView.RequiredBeats.All(beat => beat.IsCompleted), Is.True);
            Assert.That(service.CurrentView.RequiredBeats.Select(beat => beat.ResolvedTurn),
                Is.Ordered.Ascending.And.All.GreaterThan(0));
            Assert.That(service.CurrentView.EndingCandidates.Single(candidate =>
                candidate.Code == ExpectedEnding).IsEligible, Is.True);

            // The normal-ending window opens at turn 18 in a 30-turn campaign. Repeated investigation
            // is a legal player action; natural 20s keep focus stable and must not corrupt the recipe.
            while (service.CurrentView.Status == RunStatus.Playing)
            {
                Assert.That(service.CurrentView.CurrentTurn, Is.LessThan(18));
                SubmitSkill(service, AbilityKind.Search, anchor.EntityId, null, "finale-observe");
            }

            RunView completed = service.CurrentView;
            Assert.That(completed.CurrentTurn, Is.EqualTo(18));
            Assert.That(completed.Status, Is.EqualTo(RunStatus.Completed));
            Assert.That(completed.EndingCode, Is.EqualTo(ExpectedEnding));
            Assert.That(completed.EndingCode, Is.Not.EqualTo(CampaignCatalog.FallbackEndingCode));
            Assert.That(completed.Region.LayoutHash, Is.EqualTo(immutableLayoutHash));
            Assert.That(completed.AdminAccessAcquisitionHistory.Select(record => record.Level),
                Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(completed.Inventory, Is.SupersetOf(CampaignCatalog.AdminAccessLevelIds));
            AssertSpatialProjection(completed);

            string json = LocalRunSaveService.Serialize(service);
            Assert.That(json, Does.Contain("\"checksum\":"));
            LocalTurnService resumed = LocalRunSaveService.Deserialize(json);
            RunView restored = resumed.CurrentView;
            Assert.That(restored.RunId, Is.EqualTo(completed.RunId));
            Assert.That(restored.WorldSeed, Is.EqualTo(completed.WorldSeed));
            Assert.That(restored.Version, Is.EqualTo(completed.Version));
            Assert.That(restored.CurrentTurn, Is.EqualTo(completed.CurrentTurn));
            Assert.That(restored.Status, Is.EqualTo(completed.Status));
            Assert.That(restored.EndingCode, Is.EqualTo(completed.EndingCode));
            Assert.That(restored.PlayerPosition, Is.EqualTo(completed.PlayerPosition));
            Assert.That(restored.CurrentAreaId, Is.EqualTo(completed.CurrentAreaId));
            Assert.That(restored.Region.LayoutHash, Is.EqualTo(immutableLayoutHash));
            Assert.That(restored.AdminAccess, Is.EqualTo(3));
            Assert.That(restored.Inventory, Is.EquivalentTo(completed.Inventory));
            Assert.That(restored.Connections, Is.EquivalentTo(completed.Connections));
            Assert.That(restored.RequiredBeats.Select(beat => new
                { beat.Id, beat.IsCompleted, beat.ResolvedTurn }),
                Is.EqualTo(completed.RequiredBeats.Select(beat => new
                { beat.Id, beat.IsCompleted, beat.ResolvedTurn })));
            Assert.That(restored.Entities.Any(entity => entity.EntityId == enemy.EntityId), Is.False);
            Assert.That(restored.Entities.Any(entity => entity.EntityId == threat.EntityId), Is.False);
            AssertSpatialProjection(restored);

            TurnResponse terminalRejection = resumed.Submit(TurnRequest.UseSkill(NextKey("after-ending"),
                restored.Version, AbilityKind.Search, anchor.EntityId));
            Assert.That(terminalRejection.IsSuccess, Is.False);
            Assert.That(terminalRejection.ErrorCode, Is.EqualTo(TurnErrorCode.RunNotPlaying));
            Assert.That(resumed.CurrentView.Version, Is.EqualTo(restored.Version));

            TestContext.Out.WriteLine("FULL_CAMPAIGN_PLAYTHROUGH|seed=" + CampaignSeed +
                "|meaningful_turns=" + completed.CurrentTurn + "|ending=" + completed.EndingCode +
                "|beats=" + string.Join(",", completed.RequiredBeats.Select(beat =>
                    beat.Id + "@T" + beat.ResolvedTurn)) +
                "|access=" + string.Join(",", completed.AdminAccessAcquisitionHistory.Select(record =>
                    record.Level + ":" + record.Skill + "@T" + record.TurnNo)) +
                "|enemies_defeated=" + completed.EnemiesDefeated +
                "|inventory=" + string.Join(",", completed.Inventory) +
                "|position=" + completed.PlayerPosition + "|area=" + completed.CurrentAreaId +
                "|save_roundtrip=passed");
        }

        private static void AssertFreshRun(RunView view)
        {
            Assert.That(view.Status, Is.EqualTo(RunStatus.Playing));
            Assert.That(view.CurrentTurn, Is.Zero);
            Assert.That(view.AdminAccess, Is.Zero);
            Assert.That(view.RequiredBeats, Has.Count.EqualTo(9));
            Assert.That(view.RequiredBeats.All(beat => !beat.IsCompleted), Is.True);
            Assert.That(view.Inventory, Does.Contain(CampaignCatalog.AdministratorKeyboardName));
            Assert.That(view.Inventory.Intersect(CampaignCatalog.AdminAccessLevelIds), Is.Empty);
            AssertSpatialProjection(view);
        }

        private void MoveWithinRange(LocalTurnService service, Guid targetId, int range, string label)
        {
            int attempts = 0;
            while (true)
            {
                RunView before = service.CurrentView;
                EntityView target = FindEntity(before, targetId);
                if (before.PlayerPosition.ManhattanDistance(target.Position) <= range)
                    return;
                Assert.That(++attempts, Is.LessThanOrEqualTo(80),
                    "Navigation did not converge for " + label + " at " + target.Position);

                List<GridCoord> path = FindPublicPath(before, target.Position, range);
                Assert.That(path.Count, Is.GreaterThan(1),
                    "No public walkable path reaches " + label + " at " + target.Position);
                int destinationIndex = before.HasActiveEncounter
                    ? Math.Min(7, path.Count - 1)
                    : path.Count - 1;
                GridCoord destination = path[destinationIndex];
                TurnResponse moved = service.Submit(TurnRequest.Move(NextKey("move-" + label),
                    before.Version, destination));
                Assert.That(moved.IsSuccess, Is.True,
                    label + " move to " + destination + " failed: " + moved.ErrorCode + " " + moved.ErrorMessage);
                Assert.That(moved.ConsumesCampaignTurn, Is.False);
                Assert.That(moved.D20, Is.Zero);
                Assert.That(moved.Run.CurrentTurn, Is.EqualTo(before.CurrentTurn));
                Assert.That(moved.Run.Version, Is.EqualTo(before.Version + 1));
                Assert.That(moved.Run.Region.LayoutHash, Is.EqualTo(before.Region.LayoutHash));
                Assert.That(moved.Events, Has.Some.EqualTo("EXPLORATION_TRAVEL:NO_CAMPAIGN_TURN"));
                Assert.That(moved.Run.PlayerPosition != before.PlayerPosition || moved.Run.HasActiveEncounter,
                    Is.True, "Travel may stand still only when it opens an explicit encounter.");
                AssertSpatialProjection(moved.Run);
            }
        }

        private TurnResponse SubmitSkill(LocalTurnService service, AbilityKind ability, Guid? target,
            Guid? secondary, string label)
        {
            RunView before = service.CurrentView;
            TurnResponse response = service.Submit(TurnRequest.UseSkill(NextKey(label), before.Version,
                ability, target, secondary));
            Assert.That(response.IsSuccess, Is.True,
                label + " failed: " + response.ErrorCode + " " + response.ErrorMessage);
            Assert.That(response.ConsumesCampaignTurn, Is.True);
            Assert.That(response.D20, Is.EqualTo(20));
            Assert.That(response.Outcome, Is.EqualTo(RuleOutcome.CriticalSuccess));
            Assert.That(response.Run.CurrentTurn, Is.EqualTo(before.CurrentTurn + 1));
            Assert.That(response.Run.Version, Is.EqualTo(before.Version + 1));
            Assert.That(response.Run.Region.LayoutHash, Is.EqualTo(before.Region.LayoutHash));
            Assert.That(response.Run.HasActiveEncounter, Is.False,
                "A meaningful action must resolve the active travel encounter.");
            Assert.That(response.Events, Has.Some.StartsWith("TURN_COMMITTED:"));
            AssertSpatialProjection(response.Run);
            return response;
        }

        private string NextKey(string label)
        {
            _commandSequence++;
            return "full-play-" + _commandSequence.ToString("D3") + "-" + label;
        }

        private static List<GridCoord> FindPublicPath(RunView view, GridCoord target, int range)
        {
            var blocked = new HashSet<GridCoord>(view.Entities
                .Where(entity => entity.EntityId != view.PlayerEntityId)
                .Select(entity => entity.Position));
            var queue = new Queue<GridCoord>();
            var parent = new Dictionary<GridCoord, GridCoord>();
            queue.Enqueue(view.PlayerPosition);
            parent[view.PlayerPosition] = view.PlayerPosition;
            GridCoord goal = default;
            bool found = false;
            GridCoord[] directions =
            {
                new GridCoord(1, 0), new GridCoord(0, 1),
                new GridCoord(-1, 0), new GridCoord(0, -1)
            };
            while (queue.Count > 0)
            {
                GridCoord current = queue.Dequeue();
                if (current.ManhattanDistance(target) <= range && !blocked.Contains(current))
                {
                    goal = current;
                    found = true;
                    break;
                }
                for (int i = 0; i < directions.Length; i++)
                {
                    GridCoord next = new GridCoord(current.X + directions[i].X,
                        current.Y + directions[i].Y);
                    if (parent.ContainsKey(next) || blocked.Contains(next) || !view.Region.IsWalkable(next) ||
                        !CanPlayerEnter(view, next))
                        continue;
                    parent[next] = current;
                    queue.Enqueue(next);
                }
            }
            if (!found) return new List<GridCoord>();

            var reversed = new List<GridCoord> { goal };
            while (reversed[reversed.Count - 1] != view.PlayerPosition)
                reversed.Add(parent[reversed[reversed.Count - 1]]);
            reversed.Reverse();
            return reversed;
        }

        private static bool CanPlayerEnter(RunView view, GridCoord coord)
        {
            WorldArea area = view.Region.AreaAt(coord);
            if (area == null) return true;
            if (area.RequiredAdminAccess > view.AdminAccess) return false;
            if (area.CampaignRole != CampaignCatalog.RootSystemAxis) return true;
            return view.AdminAccess == 3 &&
                   CampaignCatalog.AdminAccessLevelIds.All(id => view.Inventory.Contains(id)) &&
                   view.CanonicalFacts.Any(fact => fact.Contains("관리자 통제 시스템 내부"));
        }

        private static GridCoord FindEmptyRootTile(RunView view)
        {
            WorldArea root = view.Region.Areas.Single(area =>
                area.CampaignRole == CampaignCatalog.RootSystemAxis);
            var occupied = new HashSet<GridCoord>(view.Entities.Select(entity => entity.Position));
            for (int y = root.Min.Y; y <= root.Max.Y; y++)
                for (int x = root.Min.X; x <= root.Max.X; x++)
                {
                    var candidate = new GridCoord(x, y);
                    if (view.Region.IsWalkable(candidate) && !occupied.Contains(candidate))
                        return candidate;
                }
            Assert.Fail("The generated ROOT_SYSTEM has no empty walkable tile.");
            return default;
        }

        private static EntityView FindAsset(RunView view, string assetId)
        {
            EntityView entity = view.Entities.SingleOrDefault(value => value.AssetId == assetId);
            Assert.That(entity, Is.Not.Null, "Missing active asset " + assetId);
            return entity;
        }

        private static EntityView FindEntity(RunView view, Guid entityId)
        {
            EntityView entity = view.Entities.SingleOrDefault(value => value.EntityId == entityId);
            Assert.That(entity, Is.Not.Null, "Missing active entity " + entityId);
            return entity;
        }

        private static EntityView FindNpcInRole(RunView view, string role)
        {
            EntityView npc = view.Entities.FirstOrDefault(entity => entity.Kind == EntityKind.Npc &&
                view.Region.AreaAt(entity.Position)?.CampaignRole == role);
            Assert.That(npc, Is.Not.Null, "Missing NPC in " + role);
            return npc;
        }

        private static void AssertBeatCompleted(RunView view, int index, string id)
        {
            Assert.That(view.RequiredBeats[index].Id, Is.EqualTo(id));
            Assert.That(view.RequiredBeats[index].IsCompleted, Is.True, id + " was not completed.");
            for (int i = index + 1; i < view.RequiredBeats.Count; i++)
                Assert.That(view.RequiredBeats[i].IsCompleted, Is.False,
                    "Campaign beats must complete in order; " + view.RequiredBeats[i].Id + " completed early.");
        }

        private static void AssertAccess(RunView view, int level, AbilityKind ability)
        {
            Assert.That(view.AdminAccess, Is.EqualTo(level));
            Assert.That(view.Inventory, Does.Contain(CampaignCatalog.AdminAccessLevelIds[level - 1]));
            Assert.That(view.AdminAccessAcquisitionHistory, Has.Count.EqualTo(level));
            AdminAccessAcquisitionRecord record = view.AdminAccessAcquisitionHistory[level - 1];
            Assert.That(record.Level, Is.EqualTo(level));
            Assert.That(record.Skill, Is.EqualTo(ability));
            Assert.That(record.AccessId, Is.EqualTo(CampaignCatalog.AdminAccessLevelIds[level - 1]));
        }

        private static void AssertSpatialProjection(RunView view)
        {
            Assert.That(view.Region.Contains(view.PlayerPosition), Is.True);
            Assert.That(view.Region.IsWalkable(view.PlayerPosition), Is.True);
            WorldArea area = view.Region.AreaAt(view.PlayerPosition);
            Assert.That(area, Is.Not.Null, "Every committed player coordinate must project into an authored area.");
            Assert.That(view.CurrentAreaId, Is.EqualTo(area.Id));
            int localX = view.PlayerPosition.X - area.Min.X;
            int localY = view.PlayerPosition.Y - area.Min.Y;
            Assert.That(localX, Is.InRange(0, area.Max.X - area.Min.X));
            Assert.That(localY, Is.InRange(0, area.Max.Y - area.Min.Y));
            Assert.That(new GridCoord(area.Min.X + localX, area.Min.Y + localY),
                Is.EqualTo(view.PlayerPosition), "Area-local coordinates must round-trip to world coordinates.");
            EntityView player = FindEntity(view, view.PlayerEntityId);
            Assert.That(player.Position, Is.EqualTo(view.PlayerPosition),
                "Player entity, public player position, and area projection must be synchronized.");
        }
    }
}
