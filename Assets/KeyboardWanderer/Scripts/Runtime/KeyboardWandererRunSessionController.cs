using System;
using System.Collections;
using KeyboardWanderer.Gameplay;
using KeyboardWanderer.Networking;
using UnityEngine;

namespace KeyboardWanderer.Runtime
{
    /// <summary>
    /// 새 게임과 이어하기 과정에서 서버 연결을 시도하고, 실패하면 기존 로컬 저장 경로로 전환한다.
    /// 화면·월드·선택 상태는 변경하지 않으며 런 시작에 필요한 결과만 조립해 호출자에게 전달한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererRunSessionController : MonoBehaviour
    {
        private const long BaseSeed = 20260717L;
        private const string RunCounterKey = "keyboard-wanderer.run-counter";
        private const string ServerRunIdKey = "keyboard-wanderer.server-run-id";
        private const string PendingNewSeedKey = "keyboard-wanderer.pending-new-seed";
        private const string PendingCampaignIdempotencyKey = "keyboard-wanderer.pending-campaign-key";
        private const string PendingRunIdempotencyKey = "keyboard-wanderer.pending-run-key";
        private const string PendingCampaignIdKey = "keyboard-wanderer.pending-campaign-id";

        private GameApiClient _api;

        public bool IsPending { get; private set; }
        public string Status { get; private set; } = "권위 서버 확인 전";
        public long NextSeed => BaseSeed + KeyboardWandererPreferences.GetInt(RunCounterKey, 0) + 1;
        public bool HasContinue => LocalRunSaveService.HasSave ||
                                   !string.IsNullOrWhiteSpace(KeyboardWandererPreferences.GetString(ServerRunIdKey, string.Empty));
        public GameApiClient Api => _api ?? (_api = new GameApiClient());

        public event Action<string> StatusChanged;

        /// <summary>새 시드를 확정하고 서버 캠페인·런을 만들거나 같은 시드의 로컬 런을 반환한다.</summary>
        public IEnumerator StartNew(Action<KeyboardWandererRunSessionResult> completed)
        {
            if (IsPending)
                yield break;

            IsPending = true;
            try
            {
                bool retryingAmbiguousCreate = HasPendingNewOperation();
                long seed = ReserveNewOperation();
                string campaignIdempotencyKey = KeyboardWandererPreferences.GetString(PendingCampaignIdempotencyKey);
                string runIdempotencyKey = KeyboardWandererPreferences.GetString(PendingRunIdempotencyKey);

                SetStatus("권위 서버 확인 중");
                GameApiClient.Result<bool> health = null;
                yield return Api.CheckHealth(value => health = value);
                if (health == null || !health.IsSuccess)
                {
                    if (retryingAmbiguousCreate)
                    {
                        WaitForServerRetry("생성 요청 확인 실패 · 새 게임을 다시 눌러 재시도하세요", completed);
                        yield break;
                    }
                    ClearPendingNewOperation();
                    CompleteLocal(seed, false, "서버 미실행 · 로컬 연속성 폴백", completed);
                    yield break;
                }

                SetStatus("Seed 기반 캠페인 생성 중");
                GameApiClient.Result<GameApiClient.CampaignSnapshot> campaign = null;
                yield return Api.CreateCampaign(seed, LocalTurnService.CampaignTurnLimit, campaignIdempotencyKey,
                    value => campaign = value);
                if (campaign == null || !campaign.IsSuccess)
                {
                    if (IsAmbiguousTransportFailure(campaign))
                    {
                        WaitForServerRetry("캠페인 생성 응답 확인 실패 · 같은 요청으로 재시도하세요", completed);
                        yield break;
                    }
                    ClearPendingNewOperation();
                    CompleteLocal(seed, false, "캠페인 생성 실패 · 로컬 연속성 폴백", completed);
                    yield break;
                }
                if (string.IsNullOrWhiteSpace(campaign.Value.id))
                {
                    ClearPendingNewOperation();
                    CompleteLocal(seed, false, "캠페인 식별자 누락 · 로컬 연속성 폴백", completed);
                    yield break;
                }
                string previouslyCreatedCampaignId = KeyboardWandererPreferences.GetString(PendingCampaignIdKey, string.Empty);
                if (!string.IsNullOrWhiteSpace(previouslyCreatedCampaignId) &&
                    !string.Equals(previouslyCreatedCampaignId, campaign.Value.id, StringComparison.Ordinal))
                {
                    ClearPendingNewOperation();
                    CompleteLocal(seed, false, "캠페인 멱등 재생 불일치 · 로컬 연속성 폴백", completed);
                    yield break;
                }
                KeyboardWandererPreferences.SetString(PendingCampaignIdKey, campaign.Value.id);
                KeyboardWandererPreferences.Save();

                SetStatus("권위 런 초기화 중");
                GameApiClient.Result<GameApiClient.RunSnapshot> run = null;
                yield return Api.CreateRun(campaign.Value.id, runIdempotencyKey, value => run = value);
                if (run == null || !run.IsSuccess)
                {
                    if (IsAmbiguousTransportFailure(run))
                    {
                        WaitForServerRetry("런 생성 응답 확인 실패 · 같은 요청으로 재시도하세요", completed);
                        yield break;
                    }
                    ClearPendingNewOperation();
                    CompleteLocal(seed, false, "런 생성 실패 · 로컬 연속성 폴백", completed);
                    yield break;
                }

                string status = "권위 서버 연결 · layout " + ShortHash(run.Value.world?.layoutHash);
                ClearPendingNewOperation();
                RememberServerRun(run.Value.id);
                Complete(new KeyboardWandererRunSessionResult(
                    LocalTurnService.CreateDemo(seed), false, true, campaign.Value, run.Value, status), completed);
            }
            finally
            {
                IsPending = false;
            }
        }

        /// <summary>
        /// 저장된 서버 런을 우선 복원한다. 권위 포인터가 있는 동안 일시적 통신 실패는
        /// 로컬 체크포인트로의 암묵적 타임라인 분기가 아니라 같은 런의 재시도 경계다.
        /// 서버가 명시적으로 NOT_FOUND를 반환했거나 서버 포인터가 없을 때만 로컬 저장을 사용한다.
        /// </summary>
        public IEnumerator Continue(Action<KeyboardWandererRunSessionResult> completed)
        {
            if (IsPending)
                yield break;

            IsPending = true;
            try
            {
                SetStatus("저장된 권위 런 동기화 중");
                LocalTurnService restored = LocalRunSaveService.Load();
                string serverRunId = KeyboardWandererPreferences.GetString(ServerRunIdKey, string.Empty);
                bool missingServerRun = false;
                if (!string.IsNullOrWhiteSpace(serverRunId))
                {
                    GameApiClient.Result<GameApiClient.RunSnapshot> server = null;
                    yield return Api.GetRun(serverRunId, value => server = value);
                    missingServerRun = IsRunNotFound(server?.ErrorCode, server?.ErrorMessage);
                    if (missingServerRun)
                        ClearServerRunPointer();
                    if (server != null && server.IsSuccess)
                    {
                        if (string.Equals(server.Value.status, "abandoned", StringComparison.OrdinalIgnoreCase))
                        {
                            GameApiClient.Result<GameApiClient.RunSnapshot> resumed = null;
                            yield return Api.ResumeRun(serverRunId, server.Value.version, value => resumed = value);
                            if (resumed == null || !resumed.IsSuccess)
                            {
                                bool resumeMissing = IsRunNotFound(resumed?.ErrorCode, resumed?.ErrorMessage);
                                if (!resumeMissing)
                                {
                                    WaitForServerRetry("권위 런 재개 실패 · 이어하기를 다시 시도하세요", completed);
                                    yield break;
                                }

                                // Resume can race with an authoritative deletion. Only the
                                // explicit NOT_FOUND response permits the pointer handoff.
                                missingServerRun = true;
                                ClearServerRunPointer();
                                server = null;
                            }
                            else
                            {
                                server = resumed;
                            }
                        }

                        if (!missingServerRun && server != null)
                        {
                            long seed = server.Value.world != null ? server.Value.world.worldSeed : BaseSeed;
                            bool snapshotMatchesServer = restored != null && restored.CurrentView.WorldSeed == seed;
                            LocalTurnService boundService = snapshotMatchesServer
                                ? restored
                                : LocalTurnService.CreateDemo(seed);
                            string status = snapshotMatchesServer
                                ? "권위 런 재동기화 완료"
                                : "권위 런 재동기화 완료 · 로컬 투영 재생성";
                            Complete(new KeyboardWandererRunSessionResult(
                                boundService, true, true, null, server.Value, status), completed);
                            yield break;
                        }
                    }

                    if (ShouldWaitForServerRetry(serverRunId, missingServerRun, restored != null))
                    {
                        WaitForServerRetry("서버 연결 실패 · 이어하기를 다시 시도하세요", completed);
                        yield break;
                    }
                }

                if (restored != null)
                {
                    Complete(new KeyboardWandererRunSessionResult(
                        restored, true, false, null, null,
                        string.IsNullOrWhiteSpace(serverRunId)
                            ? "서버 상태 없음 · 로컬 스냅샷 폴백"
                            : "권위 런 소실 · 로컬 스냅샷 폴백"), completed);
                    yield break;
                }

                if (ShouldWaitForServerRetry(serverRunId, missingServerRun, restored != null))
                {
                    WaitForServerRetry("서버 연결 실패 · 이어하기를 다시 시도하세요", completed);
                    yield break;
                }

                int counter = KeyboardWandererPreferences.GetInt(RunCounterKey, 0) + 1;
                KeyboardWandererPreferences.SetInt(RunCounterKey, counter);
                KeyboardWandererPreferences.Save();
                bool replacedMissingContinue = !string.IsNullOrWhiteSpace(serverRunId) && missingServerRun;
                CompleteLocal(BaseSeed + counter, false, "복원할 런 없음 · 새 로컬 폴백 시작", completed,
                    replacedMissingContinue);
            }
            finally
            {
                IsPending = false;
            }
        }

        /// <summary>권위 서버에서 받은 최신 런 ID를 기존 PlayerPrefs 키에 보존한다.</summary>
        public void RememberServerRun(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId))
                return;
            KeyboardWandererPreferences.SetString(ServerRunIdKey, runId);
            KeyboardWandererPreferences.Save();
        }

        /// <summary>
        /// 서버가 저장된 런을 더 이상 보유하지 않을 때 재접속 루프를 막기 위해
        /// 포인터만 폐기한다. 이미 만들어 둔 로컬 체크포인트는 유지한다.
        /// </summary>
        public void ForgetServerRun()
        {
            ClearServerRunPointer();
            SetStatus("서버 런 소실 · 로컬 체크포인트 전환");
        }

        /// <summary>로컬 JSON 저장과 서버 런 포인터를 함께 제거한다.</summary>
        public void DeleteSave()
        {
            LocalRunSaveService.Delete();
            ClearServerRunPointer();
            ClearPendingNewOperation();
        }

        private void CompleteLocal(long seed, bool resumed, string status,
            Action<KeyboardWandererRunSessionResult> completed, bool replacedMissingContinue = false,
            bool clearServerPointer = true)
        {
            if (clearServerPointer)
                ClearServerRunPointer();
            Complete(new KeyboardWandererRunSessionResult(
                LocalTurnService.CreateDemo(seed), resumed, false, null, null, status,
                replacedMissingContinue), completed);
        }

        private void Complete(KeyboardWandererRunSessionResult result,
            Action<KeyboardWandererRunSessionResult> completed)
        {
            IsPending = false;
            SetStatus(result.Status);
            completed?.Invoke(result);
        }

        private void SetStatus(string value)
        {
            Status = value ?? string.Empty;
            StatusChanged?.Invoke(Status);
        }

        private static void ClearServerRunPointer()
        {
            KeyboardWandererPreferences.DeleteKey(ServerRunIdKey);
            KeyboardWandererPreferences.Save();
        }

        private static bool HasPendingNewOperation()
        {
            return long.TryParse(KeyboardWandererPreferences.GetString(PendingNewSeedKey, string.Empty), out _) &&
                   IsValidCreationKey(KeyboardWandererPreferences.GetString(PendingCampaignIdempotencyKey, string.Empty)) &&
                   IsValidCreationKey(KeyboardWandererPreferences.GetString(PendingRunIdempotencyKey, string.Empty));
        }

        private static long ReserveNewOperation()
        {
            if (HasPendingNewOperation() &&
                long.TryParse(KeyboardWandererPreferences.GetString(PendingNewSeedKey, string.Empty), out long pendingSeed))
                return pendingSeed;

            ClearPendingNewOperation();
            int counter = KeyboardWandererPreferences.GetInt(RunCounterKey, 0) + 1;
            long seed = BaseSeed + counter;
            KeyboardWandererPreferences.SetInt(RunCounterKey, counter);
            KeyboardWandererPreferences.SetString(PendingNewSeedKey, seed.ToString());
            KeyboardWandererPreferences.SetString(PendingCampaignIdempotencyKey,
                "kw-campaign-" + Guid.NewGuid().ToString("N"));
            KeyboardWandererPreferences.SetString(PendingRunIdempotencyKey,
                "kw-run-" + Guid.NewGuid().ToString("N"));
            KeyboardWandererPreferences.Save();
            return seed;
        }

        private static void ClearPendingNewOperation()
        {
            KeyboardWandererPreferences.DeleteKey(PendingNewSeedKey);
            KeyboardWandererPreferences.DeleteKey(PendingCampaignIdempotencyKey);
            KeyboardWandererPreferences.DeleteKey(PendingRunIdempotencyKey);
            KeyboardWandererPreferences.DeleteKey(PendingCampaignIdKey);
            KeyboardWandererPreferences.Save();
        }

        private static bool IsValidCreationKey(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Length >= 8 && value.Length <= 128;
        }

        private static bool IsAmbiguousTransportFailure<T>(GameApiClient.Result<T> result)
        {
            return result == null || string.Equals(result.ErrorCode, "NETWORK_ERROR", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRunNotFound(string errorCode, string errorMessage)
        {
            string code = (errorCode ?? string.Empty).Trim().ToUpperInvariant();
            if (code == "NOT_FOUND" || code == "RUN_NOT_FOUND")
                return true;
            if (!string.IsNullOrWhiteSpace(code))
                return false;
            return (errorMessage ?? string.Empty).IndexOf("Run was not found", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ShouldWaitForServerRetry(string serverRunId, bool missingServerRun,
            bool hasLocalCheckpoint)
        {
            // hasLocalCheckpoint is deliberately ignored. An online run's local file is
            // only a deterministic fallback seed and is not a mirror of authoritative
            // position, history, inventory or progression.
            return !string.IsNullOrWhiteSpace(serverRunId) && !missingServerRun;
        }

        private void WaitForServerRetry(string status, Action<KeyboardWandererRunSessionResult> completed)
        {
            SetStatus(status);
            completed?.Invoke(null);
        }

        private static string ShortHash(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "--------";
            return value.Length <= 8 ? value : value.Substring(0, 8);
        }
    }

    /// <summary>서버·로컬 어느 경로로 시작했는지와 실제 런 데이터를 함께 전달하는 불변 결과다.</summary>
    public sealed class KeyboardWandererRunSessionResult
    {
        public LocalTurnService Service { get; }
        public bool Resumed { get; }
        public bool ServerOnline { get; }
        public GameApiClient.CampaignSnapshot ServerCampaign { get; }
        public GameApiClient.RunSnapshot ServerRun { get; }
        public string Status { get; }
        public bool ReplacedMissingContinue { get; }

        public KeyboardWandererRunSessionResult(LocalTurnService service, bool resumed, bool serverOnline,
            GameApiClient.CampaignSnapshot serverCampaign, GameApiClient.RunSnapshot serverRun, string status,
            bool replacedMissingContinue = false)
        {
            Service = service ?? throw new ArgumentNullException(nameof(service));
            Resumed = resumed;
            ServerOnline = serverOnline;
            ServerCampaign = serverCampaign;
            ServerRun = serverRun;
            Status = status ?? string.Empty;
            ReplacedMissingContinue = replacedMissingContinue;
        }
    }
}
