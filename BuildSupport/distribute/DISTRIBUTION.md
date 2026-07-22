# 배포 가이드 (팀 내부용)

이 문서는 **게임을 다른 사람에게 실행 가능한 형태로 나눠주는 방법**을 설명합니다.
대상은 두 종류이고, 방법이 다릅니다.

```
                       ┌─────────────────────────────────────────────┐
                       │  camp-11 (172.10.5.138) — KAIST 내부망         │
   [게임 클라이언트]     │                                             │
   127.0.0.1:8787  ──►  │  SSH(22) ──tunnel──►  게임서버 :8787          │
        ▲               │                          │                  │
        │               │                          ▼                  │
   (SSH 터널)            │                      vLLM :8000 (Qwen)       │
                       └─────────────────────────────────────────────┘
```

> **왜 터널?** VPN 을 켜도 camp-11 은 **22번(SSH) 포트만** 열려 있습니다.
> 8787 은 게이트웨이가 막아서 게임이 직접 못 붙습니다. 그래서 SSH 터널로
> `localhost:8787 → camp-11:8787` 통로를 뚫고, 게임은 `127.0.0.1:8787` 로 붙습니다.

---

## 대상 1. 개발 팀원 (Unity 로 직접 실행)

Unity 에디터로 프로젝트를 열어 Play 하는 사람. 게임 서버를 **자기 컴퓨터에서** 돌립니다.

→ **[Server/실행가이드.md](../../Server/실행가이드.md)** 를 보세요. (`Server/run.sh` / `run.bat`)

---

## 대상 2. 일반 유저 (설치 없이 게임만 실행)

Unity 없이 게임 실행파일만 받는 사람. 서버는 **camp-11 의 공용 서버**를 씁니다.
받는 것은 **zip 하나**뿐이고, VPN 켜고 런처 더블클릭이면 끝입니다.

### 배포판 만들기

```bash
BuildSupport/distribute/build-and-package.sh
```

실행하면 `~/notegame-dist/packages/` 에 두 개가 생깁니다:
- `NinjaAdventure-Mac.zip`
- `NinjaAdventure-Windows.zip`

각 zip 안에는 [게임 + `tunnel/notegame_tunnel` 키 + `게임 실행하기` 런처 + `읽어보세요.txt`] 가 들어있습니다.
이 zip 을 유저에게 전달하면 됩니다. 유저 쪽 사용법은 zip 안 `읽어보세요.txt` 에 있습니다.

**사전 준비 (배포판 만드는 사람 1회):**
- Unity 6000.5.4f1 + **Windows Build Support (Mono)** + Mac Build Support 모듈
- 배포용 터널 개인키를 `~/.ssh/notegame_tunnel` 에 두기 (아래 "비밀값" 참고)
- 빌드 중에는 Unity 에디터로 이 프로젝트를 **닫아두기** (프로젝트 락 방지)

macOS와 Windows 빌드는 서로 다른 Unity 프로세스로 실행됩니다. Unity는 활성
빌드 타깃의 `UNITY_STANDALONE_*` 조건부 컴파일 심볼을 프로세스 안에서 유지할 수
있으므로, 한 프로세스에서 두 플랫폼을 연속 빌드하면 잘못된 플랫폼 코드가 포함될 수
있습니다. `build-and-package.sh`가 이 경계를 자동으로 보장합니다.

실제 실행 파일은 두 OS 모두 파일시스템에 안전한 이름을 사용합니다.

- macOS: `NUPJUK-The-Last-Commit.app`
- Windows: `NUPJUK-The-Last-Commit.exe`

게임 창과 타이틀 화면의 정식 표기는 `NUPJUK : The Last Commit`으로 유지됩니다.

---

## main 이 갱신될 때 (중요)

게임/서버 코드는 계속 바뀝니다. 갱신 반영은 **항상 아래 두 스크립트**로 끝납니다.

```bash
git pull

# 서버 코드(Server/)가 바뀌었으면:
BuildSupport/distribute/deploy-server.sh          # camp-11 재배포 + 헬스체크

# 게임(Assets/ 등)이 바뀌었으면:
BuildSupport/distribute/build-and-package.sh      # 배포판 zip 다시 생성
```

둘 다 바뀌었으면 둘 다 실행하면 됩니다. 이 방식은 코드가 계속 바뀌어도 그대로 유효합니다.

---

## 서버는 지금 어디서 어떻게 도는가

- 위치: `camp-11:/opt/notegame-server` (systemd 서비스 `notegame-server.service`)
- 실행: `/opt/node20/bin/node src/index.js` (camp-11 기본 node 는 v12 라 Node20 을 `/opt/node20` 에 별도 설치)
- 설정: `0.0.0.0:8787`, `STORAGE=memory`, `LLM_PROVIDER=vllm` → `127.0.0.1:8000/v1` (같은 머신이라 터널 불필요)
- 자동 재시작 + 재부팅 생존. 로그: `camp-11:/var/log/notegame-server.log`
- 상태 확인:
  ```bash
  ssh -i ~/.ssh/qwen_key root@172.10.5.138 'systemctl status notegame-server --no-pager; tail /var/log/notegame-server.log'
  ```

> `STORAGE=memory` 라 서버 재시작 시 진행 중 런은 초기화됩니다. 지속 저장이 필요하면
> postgres 로 전환하고 `Server/.env.example` 의 DATABASE_URL 을 설정하세요.

---

## 터널 보안 (왜 키를 배포판에 넣어도 되는가)

배포판에 들어가는 `notegame_tunnel` 개인키는 camp-11 의 전용 유저 `gametunnel` 로만 접속되며,
서버의 `authorized_keys` 에서 아래처럼 **강하게 제한**되어 있습니다:

```
restrict,port-forwarding,permitopen="127.0.0.1:8787" ssh-ed25519 AAAA...
```

즉 이 키로 할 수 있는 것은 **오직 `127.0.0.1:8787` 로컬 포워딩 하나**뿐입니다.
셸 접속, 다른 포트(예: vLLM 8000) 포워딩, 파일 접근 전부 차단됩니다. (검증 완료)
그래도 키는 최소한으로만 공유하세요.

---

## 비밀값 (리포에 커밋하지 않음)

| 값 | 위치 | 용도 |
|---|---|---|
| `qwen_key` | `~/.ssh/qwen_key` | camp-11 root 접속 (서버 배포용) |
| `notegame_tunnel` | `~/.ssh/notegame_tunnel` | 배포판에 넣는 제한 터널 키 |
| `VLLM_API_KEY` | `Server/.env` (로컬) / `camp-11:/opt/notegame-server/.env` | vLLM 인증 |

이 세 개는 `.gitignore` 로 보호되거나 리포 밖에 있습니다. 새 팀원에겐 안전한 채널로 따로 전달하세요.

---

## 리포에 커밋되는 것

- `Server/run.sh|ps1|bat`, `Server/실행가이드.md` — 개발 팀원용 실행기
- `Assets/Editor/DistBuilder.cs` — 배포 빌드 스크립트
- `BuildSupport/distribute/*` — 재배포 스크립트, 런처 템플릿, 안내문, 이 문서

빌드 산출물(`~/notegame-dist`)과 비밀키는 커밋하지 않습니다.
