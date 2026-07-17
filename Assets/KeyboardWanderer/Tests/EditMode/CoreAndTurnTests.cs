using System;
using System.Linq;
using KeyboardWanderer.Core;
using KeyboardWanderer.Gameplay;
using NUnit.Framework;

namespace KeyboardWanderer.Tests
{
    public sealed class CoreAndTurnTests
    {
        [Test]
        public void GridCoord_PackRoundTrip_PreservesSignedValues()
        {
            var original = new GridCoord(-17, 42);
            Assert.That(GridCoord.Unpack(original.Pack()), Is.EqualTo(original));
        }

        [Test]
        public void RegionGenerator_SameSeedAndVersion_ProducesSameLayout()
        {
            RegionMap first = DeterministicRegionGenerator.Generate(99, "alpha", 20, 14);
            RegionMap second = DeterministicRegionGenerator.Generate(99, "alpha", 20, 14);

            Assert.That(second.LayoutHash, Is.EqualTo(first.LayoutHash));
            Assert.That(GridPathfinder.FindPath(first, first.Start, first.Exit), Is.Not.Empty);
        }

        [Test]
        public void SpatialIndex_FailedMove_DoesNotMutatePositionOrOccupancy()
        {
            RegionMap map = DeterministicRegionGenerator.Generate(7, "space", 9, 9);
            var spatial = new SpatialIndex();
            Guid firstId = Guid.NewGuid();
            Guid blockerId = Guid.NewGuid();
            var first = new EntityState(firstId, EntityKind.Player, "p", "P", true, true, false, map.RegionId, map.Start);
            var blocker = new EntityState(blockerId, EntityKind.Prop, "b", "B", true, false, true, map.RegionId, new GridCoord(2, 1));
            Assert.That(spatial.Register(first, out _), Is.True);
            Assert.That(spatial.Register(blocker, out _), Is.True);

            MoveResult result = spatial.TryMove(firstId, map.RegionId, blocker.Position, 0,
                (regionId, coord) => regionId == map.RegionId && map.IsWalkable(coord));

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(first.Position, Is.EqualTo(map.Start));
            Assert.That(spatial.FindAt(map.RegionId, map.Start).Single().EntityId, Is.EqualTo(firstId));
            Assert.That(spatial.Validate((regionId, coord) => regionId == map.RegionId && map.IsWalkable(coord)), Is.Empty);
        }

        [Test]
        public void Submit_SameIdempotencyKey_CommitsExactlyOnce()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(100, new SequenceD20Source(20));
            RunView before = service.CurrentView;
            GridCoord destination = FirstReachableEmptyNeighbor(before);
            var request = new TurnRequest("same-key", before.Version, AbilityKind.Move, null, destination, "move");

            TurnResponse first = service.Submit(request);
            TurnResponse duplicate = service.Submit(request);

            Assert.That(first.IsSuccess, Is.True);
            Assert.That(duplicate.IsSuccess, Is.True);
            Assert.That(duplicate.FromIdempotencyCache, Is.True);
            Assert.That(service.CurrentView.CurrentTurn, Is.EqualTo(1));
            Assert.That(service.CurrentView.Version, Is.EqualTo(before.Version + 1));
        }

        [Test]
        public void Submit_SameIdempotencyKeyDifferentPayload_ReturnsConflict()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(101, new SequenceD20Source(20));
            RunView before = service.CurrentView;
            GridCoord destination = FirstReachableEmptyNeighbor(before);
            service.Submit(new TurnRequest("reused", before.Version, AbilityKind.Move, null, destination, "first"));

            TurnResponse conflict = service.Submit(new TurnRequest("reused", before.Version, AbilityKind.Move, null, destination, "different"));

            Assert.That(conflict.IsSuccess, Is.False);
            Assert.That(conflict.ErrorCode, Is.EqualTo(TurnErrorCode.IdempotencyConflict));
            Assert.That(service.CurrentView.CurrentTurn, Is.EqualTo(1));
        }

        [Test]
        public void Delete_ProtectedNpc_IsRejectedWithoutConsumingTurn()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(102, new SequenceD20Source(20));
            RunView view = service.CurrentView;
            EntityView protectedNpc = view.Entities.First(entity => entity.Kind == EntityKind.Npc && entity.IsProtected);
            var request = new TurnRequest("protected", view.Version, AbilityKind.Delete, protectedNpc.EntityId, null, "delete the warden");

            TurnResponse response = service.Submit(request);

            Assert.That(response.IsSuccess, Is.False);
            Assert.That(response.ErrorCode == TurnErrorCode.ProtectedEntity || response.ErrorCode == TurnErrorCode.OutOfRange, Is.True);
            Assert.That(service.CurrentView.CurrentTurn, Is.Zero);
        }

        [Test]
        public void CampaignDirector_EnforcesConvergenceWindows()
        {
            CampaignConstraints tenLeft = CampaignDirector.Evaluate(30, 40);
            CampaignConstraints fiveLeft = CampaignDirector.Evaluate(35, 40);
            CampaignConstraints threeLeft = CampaignDirector.Evaluate(37, 40);
            CampaignConstraints last = CampaignDirector.Evaluate(39, 40);

            Assert.That(tenLeft.MustAdvanceMainPlot, Is.True);
            Assert.That(fiveLeft.AllowNewLongQuest, Is.False);
            Assert.That(threeLeft.AllowUnrelatedNpcOrRegion, Is.False);
            Assert.That(last.ForceEnding, Is.True);
        }

        private static GridCoord FirstReachableEmptyNeighbor(RunView view)
        {
            EntityView player = view.Entities.Single(entity => entity.EntityId == view.PlayerEntityId);
            GridCoord[] candidates =
            {
                new GridCoord(player.Position.X + 1, player.Position.Y),
                new GridCoord(player.Position.X, player.Position.Y + 1),
                new GridCoord(player.Position.X - 1, player.Position.Y),
                new GridCoord(player.Position.X, player.Position.Y - 1)
            };

            foreach (GridCoord candidate in candidates)
            {
                if (view.Region.IsWalkable(candidate) && view.Entities.All(entity => entity.Position != candidate))
                    return candidate;
            }
            Assert.Fail("Demo region has no reachable empty neighbor.");
            return default;
        }
    }
}
