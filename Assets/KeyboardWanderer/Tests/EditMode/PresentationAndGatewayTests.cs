using System.Collections;
using KeyboardWanderer.Core;
using KeyboardWanderer.Demo;
using KeyboardWanderer.Gameplay;
using NUnit.Framework;

namespace KeyboardWanderer.Tests.EditMode
{
    public sealed class PresentationAndGatewayTests
    {
        [Test]
        public void Coordinator_OnlyPublishesChangedPresentationSections()
        {
            var coordinator = new RunCoordinator();
            PresentationChange observed = PresentationChange.None;
            int notifications = 0;
            coordinator.PresentationChanged += (_, changes) =>
            {
                observed = changes;
                notifications++;
            };
            var initial = State(new GridCoord(2, 3), null, 0);

            coordinator.Publish(initial);
            Assert.That(observed, Is.EqualTo(PresentationChange.All));

            coordinator.Publish(initial);
            Assert.That(notifications, Is.EqualTo(1));

            coordinator.Publish(State(new GridCoord(3, 3), null, 0));
            Assert.That((observed & PresentationChange.Minimap) != 0, Is.True);
            Assert.That((observed & PresentationChange.Dialogue) == 0, Is.True);

            coordinator.Publish(State(new GridCoord(3, 3), new GridCoord(7, 8), 0));
            Assert.That((observed & PresentationChange.Minimap) != 0, Is.True);
            Assert.That((observed & PresentationChange.Selection) != 0, Is.True);
        }

        [Test]
        public void LocalGateway_ProducesSameCommittedRunAsLocalService()
        {
            LocalTurnService service = LocalTurnService.CreateDemo(20260719L);
            var gateway = new LocalTurnGateway(service);
            GridCoord destination = FindWalkableNeighbour(service.CurrentView);
            TurnGatewayResult result = null;

            Drain(gateway.Submit(TurnRequest.Move("gateway-test", service.CurrentView.Version, destination),
                value => result = value));

            Assert.That(result, Is.Not.Null);
            Assert.That(result.IsSuccess, Is.True, result?.ErrorMessage);
            Assert.That(result.LocalResponse.Run.Version, Is.EqualTo(service.CurrentView.Version));
            Assert.That(result.LocalResponse.Run.PlayerPosition, Is.EqualTo(service.CurrentView.PlayerPosition));
            Assert.That(gateway.IsPending, Is.False);
        }

        [Test]
        public void DelegatingGateway_NormalizesDelayedAndMissingResponses()
        {
            var delayed = new DelegatingTurnGateway((_, done) => CompleteAfterFrame(done));
            TurnGatewayResult delayedResult = null;
            Drain(delayed.Submit(null, value => delayedResult = value));
            Assert.That(delayedResult.ErrorCode, Is.EqualTo("TEST_ERROR"));
            Assert.That(delayed.IsPending, Is.False);

            var missing = new DelegatingTurnGateway((_, __) => Empty());
            TurnGatewayResult missingResult = null;
            Drain(missing.Submit(null, value => missingResult = value));
            Assert.That(missingResult.ErrorCode, Is.EqualTo("NO_COMPLETION"));
        }

        private static RunPresentationState State(GridCoord player, GridCoord? selected, int page)
        {
            return new RunPresentationState(1, 1, "layout", player, selected, null, AbilityKind.Move,
                1, page, "dialogue", false, false, false);
        }

        private static GridCoord FindWalkableNeighbour(RunView view)
        {
            GridCoord origin = view.PlayerPosition;
            GridCoord[] candidates =
            {
                new GridCoord(origin.X + 1, origin.Y), new GridCoord(origin.X - 1, origin.Y),
                new GridCoord(origin.X, origin.Y + 1), new GridCoord(origin.X, origin.Y - 1)
            };
            foreach (GridCoord candidate in candidates)
                if (view.Region.Contains(candidate) && view.Region.GetTile(candidate).IsWalkable)
                    return candidate;
            Assert.Fail("The generated player spawn has no walkable neighbour.");
            return origin;
        }

        private static IEnumerator CompleteAfterFrame(System.Action<TurnGatewayResult> done)
        {
            yield return null;
            done(TurnGatewayResult.Failure("TEST_ERROR", "simulated"));
        }

        private static IEnumerator Empty() { yield break; }

        private static void Drain(IEnumerator operation)
        {
            while (operation.MoveNext())
            {
                if (operation.Current is IEnumerator nested)
                    Drain(nested);
            }
        }
    }
}
