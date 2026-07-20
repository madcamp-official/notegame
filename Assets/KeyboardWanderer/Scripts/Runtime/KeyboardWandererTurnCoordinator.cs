using System;
using System.Collections;
using KeyboardWanderer.Core;
using KeyboardWanderer.Gameplay;
using UnityEngine;

namespace KeyboardWanderer.Runtime
{
    /// <summary>
    /// 로컬 규칙과 서버 API의 차이를 숨기고 한 번에 하나의 턴 요청만 제출한다.
    /// 어떤 게이트웨이를 쓸지는 런 시작 시 주입받으며, UI나 월드 오브젝트는 직접 변경하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererTurnCoordinator : MonoBehaviour
    {
        private ITurnGateway _gateway;

        public bool IsPending { get; private set; }
        public TurnRequest ActiveRequest { get; private set; }
        public bool IsReady => _gateway != null;

        public event Action<bool> PendingChanged;

        /// <summary>새 런의 실행 경로를 설정한다. 저장 형식이나 서버 DTO는 이 경계를 넘지 않는다.</summary>
        public void Configure(ITurnGateway gateway)
        {
            if (IsPending)
                throw new InvalidOperationException("진행 중인 턴이 끝나기 전에 게이트웨이를 바꿀 수 없습니다.");
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            ActiveRequest = null;
        }

        /// <summary>현재 선택 상태를 로컬·서버가 공통으로 이해하는 TurnRequest로 변환한다.</summary>
        public static TurnRequest BuildRequest(
            KeyboardWandererSelectionController selection,
            long expectedVersion,
            string idempotencyPrefix)
        {
            if (selection == null)
                throw new ArgumentNullException(nameof(selection));

            string prefix = string.IsNullOrWhiteSpace(idempotencyPrefix)
                ? "turn"
                : idempotencyPrefix.Trim();
            string requestId = prefix + "-" + Guid.NewGuid().ToString("N");
            AbilityKind ability = selection.Ability;
            GridCoord? destination = ability == AbilityKind.Move || ability == AbilityKind.Copy
                ? selection.SelectedCoord
                : null;

            if (ability == AbilityKind.Move)
            {
                if (!destination.HasValue)
                    throw new InvalidOperationException("이동 요청에는 목적지가 필요합니다.");
                return TurnRequest.Move(requestId, expectedVersion, destination.Value);
            }

            Guid? target = ability == AbilityKind.Undo || ability == AbilityKind.SelectAll
                ? null
                : selection.SelectedTarget;
            Guid? secondary = ability == AbilityKind.Connect
                ? selection.SelectedSecondaryTarget
                : null;
            return TurnRequest.UseSkill(requestId, expectedVersion, ability, target, secondary, destination);
        }

        /// <summary>게이트웨이 종류와 무관하게 동일한 Pending·완료 계약으로 턴을 실행한다.</summary>
        public IEnumerator Submit(TurnRequest request, Action<TurnGatewayResult> completed)
        {
            if (request == null)
            {
                completed?.Invoke(TurnGatewayResult.Failure("INVALID_REQUEST", "턴 요청이 없습니다."));
                yield break;
            }
            if (_gateway == null)
            {
                completed?.Invoke(TurnGatewayResult.Failure("GATEWAY_NOT_READY", "턴 실행 경로가 준비되지 않았습니다."));
                yield break;
            }
            if (IsPending)
            {
                completed?.Invoke(TurnGatewayResult.Failure("TURN_ALREADY_PENDING", "이전 턴이 아직 처리 중입니다."));
                yield break;
            }

            IsPending = true;
            ActiveRequest = request;
            PendingChanged?.Invoke(true);
            TurnGatewayResult result = null;
            try
            {
                yield return _gateway.Submit(request, value => result = value);
                if (result == null)
                    result = TurnGatewayResult.Failure("NO_COMPLETION", "턴 실행이 결과 없이 끝났습니다.");
            }
            finally
            {
                ActiveRequest = null;
                IsPending = false;
                PendingChanged?.Invoke(false);
            }
            completed?.Invoke(result);
        }
    }
}
