using System;
using System.Collections;
using System.Collections.Generic;
using KeyboardWanderer.Gameplay;

namespace KeyboardWanderer.Networking
{
    /// <summary>Maps normalized turn requests to the existing HTTP DTO contract without changing that contract.</summary>
    public sealed class ServerTurnGateway : ITurnGateway
    {
        private readonly GameApiClient _client;
        private readonly Func<string> _runId;
        public bool IsPending { get; private set; }

        public ServerTurnGateway(GameApiClient client, Func<string> runId)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _runId = runId ?? throw new ArgumentNullException(nameof(runId));
        }

        public IEnumerator Submit(TurnRequest request, Action<TurnGatewayResult> completed)
        {
            if (request == null)
            {
                completed?.Invoke(TurnGatewayResult.Failure("INVALID_REQUEST", "Turn request is missing."));
                yield break;
            }
            string runId = _runId();
            if (string.IsNullOrWhiteSpace(runId))
            {
                completed?.Invoke(TurnGatewayResult.Failure("RUN_NOT_READY", "Authoritative run is missing."));
                yield break;
            }

            IsPending = true;
            try
            {
                if (request.Ability == AbilityKind.Move)
                {
                    if (!request.Destination.HasValue)
                    {
                        completed?.Invoke(TurnGatewayResult.Failure("DESTINATION_REQUIRED", "Travel destination is missing."));
                        yield break;
                    }
                    var destination = new GameApiClient.PositionSnapshot
                    {
                        x = request.Destination.Value.X,
                        y = request.Destination.Value.Y
                    };
                    GameApiClient.Result<GameApiClient.CommittedNavigation> response = null;
                    yield return _client.SubmitTravel(runId, request.IdempotencyKey, request.ExpectedRunVersion,
                        destination, value => response = value);
                    Complete(response, completed);
                    yield break;
                }

                var targetIds = new List<string>();
                if (request.TargetEntityId.HasValue) targetIds.Add(request.TargetEntityId.Value.ToString());
                if (request.SecondaryTargetEntityId.HasValue) targetIds.Add(request.SecondaryTargetEntityId.Value.ToString());
                GameApiClient.PositionSnapshot actionDestination = request.Destination.HasValue
                    ? new GameApiClient.PositionSnapshot { x = request.Destination.Value.X, y = request.Destination.Value.Y }
                    : null;
                GameApiClient.Result<GameApiClient.CommittedTurn> action = null;
                yield return _client.SubmitAction(runId, request.IdempotencyKey, request.ExpectedRunVersion,
                    SkillId(request.Ability), targetIds.ToArray(), actionDestination, request.PreparedD20,
                    value => action = value);
                Complete(action, completed);
            }
            finally
            {
                IsPending = false;
            }
        }

        private static void Complete<T>(GameApiClient.Result<T> response, Action<TurnGatewayResult> completed)
            where T : class
        {
            if (response != null && response.IsSuccess && response.Value != null)
                completed?.Invoke(TurnGatewayResult.FromPayload(response.Value));
            else
                completed?.Invoke(TurnGatewayResult.Failure(response?.ErrorCode ?? "NETWORK_ERROR",
                    response?.ErrorMessage ?? "Authoritative server returned no response.", response));
        }

        private static string SkillId(AbilityKind ability)
        {
            switch (ability)
            {
                case AbilityKind.SelectAll: return "SELECT_ALL";
                default: return ability.ToString().ToUpperInvariant();
            }
        }
    }
}
