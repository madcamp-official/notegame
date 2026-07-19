using System;
using System.Collections;

namespace KeyboardWanderer.Gameplay
{
    public sealed class TurnGatewayResult
    {
        public TurnResponse LocalResponse { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }
        public bool IsSuccess => LocalResponse != null && LocalResponse.IsSuccess;

        private TurnGatewayResult(TurnResponse response, string errorCode, string errorMessage)
        {
            LocalResponse = response;
            ErrorCode = errorCode ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public static TurnGatewayResult FromLocal(TurnResponse response) =>
            new TurnGatewayResult(response, response != null && !response.IsSuccess ? response.ErrorCode.ToString() : null,
                response?.ErrorMessage);
        public static TurnGatewayResult Failure(string code, string message) => new TurnGatewayResult(null, code, message);
    }

    /// <summary>
    /// Common asynchronous turn boundary. Local rules complete on the first MoveNext; server adapters
    /// may yield while preserving the same completion/error contract.
    /// </summary>
    public interface ITurnGateway
    {
        bool IsPending { get; }
        IEnumerator Submit(TurnRequest request, Action<TurnGatewayResult> completed);
    }

    public sealed class LocalTurnGateway : ITurnGateway
    {
        private readonly LocalTurnService _service;
        public bool IsPending { get; private set; }

        public LocalTurnGateway(LocalTurnService service) => _service = service ?? throw new ArgumentNullException(nameof(service));

        public IEnumerator Submit(TurnRequest request, Action<TurnGatewayResult> completed)
        {
            IsPending = true;
            TurnResponse response = _service.Submit(request);
            IsPending = false;
            completed?.Invoke(TurnGatewayResult.FromLocal(response));
            yield break;
        }
    }

    /// <summary>Testable adapter for HTTP-backed or delayed gateways without exposing their DTOs.</summary>
    public sealed class DelegatingTurnGateway : ITurnGateway
    {
        private readonly Func<TurnRequest, Action<TurnGatewayResult>, IEnumerator> _submit;
        public bool IsPending { get; private set; }

        public DelegatingTurnGateway(Func<TurnRequest, Action<TurnGatewayResult>, IEnumerator> submit) =>
            _submit = submit ?? throw new ArgumentNullException(nameof(submit));

        public IEnumerator Submit(TurnRequest request, Action<TurnGatewayResult> completed)
        {
            IsPending = true;
            bool completedOnce = false;
            IEnumerator operation = _submit(request, result =>
            {
                if (completedOnce) return;
                completedOnce = true;
                IsPending = false;
                completed?.Invoke(result ?? TurnGatewayResult.Failure("EMPTY_RESPONSE", "Turn gateway returned no result."));
            });
            if (operation != null)
                while (operation.MoveNext()) yield return operation.Current;
            if (!completedOnce)
            {
                IsPending = false;
                completed?.Invoke(TurnGatewayResult.Failure("NO_COMPLETION", "Turn gateway completed without a response."));
            }
        }
    }
}
