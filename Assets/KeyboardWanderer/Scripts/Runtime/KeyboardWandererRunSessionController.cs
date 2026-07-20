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

        private GameApiClient _api;

        public bool IsPending { get; private set; }
        public string Status { get; private set; } = "권위 서버 확인 전";
        public long NextSeed => BaseSeed + PlayerPrefs.GetInt(RunCounterKey, 0) + 1;
        public bool HasContinue => LocalRunSaveService.HasSave ||
                                   !string.IsNullOrWhiteSpace(PlayerPrefs.GetString(ServerRunIdKey, string.Empty));
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
                int counter = PlayerPrefs.GetInt(RunCounterKey, 0) + 1;
                PlayerPrefs.SetInt(RunCounterKey, counter);
                PlayerPrefs.Save();
                long seed = BaseSeed + counter;

                SetStatus("권위 서버 확인 중");
                GameApiClient.Result<bool> health = null;
                yield return Api.CheckHealth(value => health = value);
                if (health == null || !health.IsSuccess)
                {
                    CompleteLocal(seed, false, "서버 미실행 · 로컬 연속성 폴백", completed);
                    yield break;
                }

                SetStatus("Seed 기반 캠페인 생성 중");
                GameApiClient.Result<GameApiClient.CampaignSnapshot> campaign = null;
                yield return Api.CreateCampaign(seed, LocalTurnService.CampaignTurnLimit,
                    value => campaign = value);
                if (campaign == null || !campaign.IsSuccess)
                {
                    CompleteLocal(seed, false, "캠페인 생성 실패 · 로컬 연속성 폴백", completed);
                    yield break;
                }

                SetStatus("권위 런 초기화 중");
                GameApiClient.Result<GameApiClient.RunSnapshot> run = null;
                yield return Api.CreateRun(campaign.Value.id, value => run = value);
                if (run == null || !run.IsSuccess)
                {
                    CompleteLocal(seed, false, "런 생성 실패 · 로컬 연속성 폴백", completed);
                    yield break;
                }

                string status = "권위 서버 연결 · layout " + ShortHash(run.Value.world?.layoutHash);
                RememberServerRun(run.Value.id);
                Complete(new KeyboardWandererRunSessionResult(
                    LocalTurnService.CreateDemo(seed), false, true, campaign.Value, run.Value, status), completed);
            }
            finally
            {
                IsPending = false;
            }
        }

        /// <summary>저장된 서버 런을 우선 복원하고, 불가능하면 기존 로컬 JSON 저장을 사용한다.</summary>
        public IEnumerator Continue(Action<KeyboardWandererRunSessionResult> completed)
        {
            if (IsPending)
                yield break;

            IsPending = true;
            try
            {
                SetStatus("저장된 권위 런 동기화 중");
                LocalTurnService restored = LocalRunSaveService.Load();
                string serverRunId = PlayerPrefs.GetString(ServerRunIdKey, string.Empty);
                if (!string.IsNullOrWhiteSpace(serverRunId))
                {
                    GameApiClient.Result<GameApiClient.RunSnapshot> server = null;
                    yield return Api.GetRun(serverRunId, value => server = value);
                    if (server != null && server.IsSuccess)
                    {
                        if (string.Equals(server.Value.status, "abandoned", StringComparison.OrdinalIgnoreCase))
                        {
                            GameApiClient.Result<GameApiClient.RunSnapshot> resumed = null;
                            yield return Api.ResumeRun(serverRunId, server.Value.version, value => resumed = value);
                            if (resumed == null || !resumed.IsSuccess)
                            {
                                ClearServerRunPointer();
                                if (restored != null)
                                {
                                    Complete(new KeyboardWandererRunSessionResult(
                                        restored, true, false, null, null,
                                        "권위 런 재개 실패 · 로컬 스냅샷 폴백"), completed);
                                    yield break;
                                }
                                CompleteLocal(server.Value.world?.worldSeed ?? BaseSeed, false,
                                    "권위 런 재개 실패 · 새 로컬 폴백 시작", completed);
                                yield break;
                            }
                            server = resumed;
                        }

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

                if (restored != null)
                {
                    Complete(new KeyboardWandererRunSessionResult(
                        restored, true, false, null, null, "서버 상태 없음 · 로컬 스냅샷 폴백"), completed);
                    yield break;
                }

                int counter = PlayerPrefs.GetInt(RunCounterKey, 0) + 1;
                PlayerPrefs.SetInt(RunCounterKey, counter);
                PlayerPrefs.Save();
                CompleteLocal(BaseSeed + counter, false, "복원할 런 없음 · 새 로컬 폴백 시작", completed);
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
            PlayerPrefs.SetString(ServerRunIdKey, runId);
            PlayerPrefs.Save();
        }

        /// <summary>로컬 JSON 저장과 서버 런 포인터를 함께 제거한다.</summary>
        public void DeleteSave()
        {
            LocalRunSaveService.Delete();
            ClearServerRunPointer();
        }

        private void CompleteLocal(long seed, bool resumed, string status,
            Action<KeyboardWandererRunSessionResult> completed)
        {
            ClearServerRunPointer();
            Complete(new KeyboardWandererRunSessionResult(
                LocalTurnService.CreateDemo(seed), resumed, false, null, null, status), completed);
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
            PlayerPrefs.DeleteKey(ServerRunIdKey);
            PlayerPrefs.Save();
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

        public KeyboardWandererRunSessionResult(LocalTurnService service, bool resumed, bool serverOnline,
            GameApiClient.CampaignSnapshot serverCampaign, GameApiClient.RunSnapshot serverRun, string status)
        {
            Service = service ?? throw new ArgumentNullException(nameof(service));
            Resumed = resumed;
            ServerOnline = serverOnline;
            ServerCampaign = serverCampaign;
            ServerRun = serverRun;
            Status = status ?? string.Empty;
        }
    }
}
