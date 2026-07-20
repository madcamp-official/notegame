using System;
using KeyboardWanderer.Gameplay;

namespace KeyboardWanderer.Runtime
{
    /// <summary>파괴적 행동이 우발적인 한 번의 입력으로 실행되지 않도록 짧은 재확인 창을 관리한다.</summary>
    public sealed class DestructiveActionConfirmation
    {
        private string _armedKey = string.Empty;
        private float _expiresAt;

        public bool IsArmed(float now) => !string.IsNullOrEmpty(_armedKey) && now <= _expiresAt;

        public bool RequiresConfirmation(AbilityKind ability, Guid? target, float now, float duration = 5f)
        {
            if (!IsDestructive(ability))
            {
                Cancel();
                return false;
            }
            string key = ability + ":" + (target?.ToString("N") ?? "area");
            if (IsArmed(now) && string.Equals(_armedKey, key, StringComparison.Ordinal))
            {
                Cancel();
                return false;
            }
            _armedKey = key;
            _expiresAt = now + Math.Max(1f, duration);
            return true;
        }

        public void Cancel()
        {
            _armedKey = string.Empty;
            _expiresAt = 0f;
        }

        public static bool IsDestructive(AbilityKind ability) =>
            ability == AbilityKind.Delete || ability == AbilityKind.SelectAll || ability == AbilityKind.Undo;
    }

    /// <summary>외부 전송 없이 현재 플레이 세션의 UX 마찰만 집계하는 익명 진단 카운터다.</summary>
    public sealed class GameplayTelemetry
    {
        public int SubmitAttempts { get; private set; }
        public int InvalidSelections { get; private set; }
        public int DestructiveConfirmations { get; private set; }
        public int CancelledConfirmations { get; private set; }

        public void RecordSubmit() => SubmitAttempts++;
        public void RecordInvalidSelection() => InvalidSelections++;
        public void RecordConfirmation() => DestructiveConfirmations++;
        public void RecordCancellation() => CancelledConfirmations++;

        public string DiagnosticSummary => "실행 " + SubmitAttempts + " · 선택 오류 " + InvalidSelections +
                                           " · 안전 확인 " + DestructiveConfirmations +
                                           " · 확인 취소 " + CancelledConfirmations;
    }
}
