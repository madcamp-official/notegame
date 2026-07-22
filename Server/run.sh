#!/usr/bin/env bash
#
# 게임 서버 원클릭 실행 스크립트 (팀원 배포용)
#
# 하는 일:
#   1) camp-11(vLLM)로 가는 SSH 터널이 안 떠 있으면 연다  (127.0.0.1:8000 -> camp-11:8000)
#   2) .env 가 없으면 .env.example 을 복사해준다 (VLLM_API_KEY 는 직접 채워야 함)
#   3) node_modules 가 없으면 npm install 한다
#   4) 서버(npm start, 포트 8787)를 띄운다
#
# 사전 준비 (팀원이 딱 한 번만 하면 됨):
#   - KAIST VPN 접속 (이 스크립트는 VPN 켜져 있다고 가정)
#   - SSH 개인키 qwen_key 를  ~/.ssh/qwen_key  에 두기  (chmod 600)
#   - Server/.env 의 VLLM_API_KEY 채우기 (팀장이 따로 전달)
#
# 사용법:
#   cd Server && ./run.sh
#
set -euo pipefail

# ── 설정 (필요하면 환경변수로 덮어쓰기 가능) ────────────────────────────
REMOTE_HOST="${QWEN_REMOTE:-root@172.10.5.138}"   # camp-11 GPU 박스
SSH_KEY="${QWEN_SSH_KEY:-$HOME/.ssh/qwen_key}"     # 터널용 개인키
LOCAL_PORT="${VLLM_LOCAL_PORT:-8000}"              # 로컬로 포워딩할 포트
REMOTE_PORT="${VLLM_REMOTE_PORT:-8000}"            # camp-11 안쪽 vLLM 포트

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "▶ notegame 서버 실행 준비..."

# ── 1) SSH 터널 확인 & 열기 ──────────────────────────────────────────
if lsof -iTCP:"$LOCAL_PORT" -sTCP:LISTEN >/dev/null 2>&1; then
  echo "  ✓ 터널 이미 떠 있음 (localhost:$LOCAL_PORT)"
else
  if [[ ! -f "$SSH_KEY" ]]; then
    echo "  ✗ SSH 키가 없습니다: $SSH_KEY"
    echo "    팀장에게 qwen_key 를 받아 ~/.ssh/qwen_key 에 두고 'chmod 600' 하세요."
    exit 1
  fi
  echo "  … 터널 여는 중: localhost:$LOCAL_PORT -> $REMOTE_HOST:$REMOTE_PORT"
  ssh -f -N \
      -o ExitOnForwardFailure=yes \
      -o ServerAliveInterval=30 \
      -i "$SSH_KEY" \
      -L "${LOCAL_PORT}:127.0.0.1:${REMOTE_PORT}" \
      "$REMOTE_HOST"
  echo "  ✓ 터널 열림 (백그라운드). 끄려면:  pkill -f '${LOCAL_PORT}:127.0.0.1:${REMOTE_PORT}'"
fi

# ── 2) .env 준비 ────────────────────────────────────────────────────
if [[ ! -f .env ]]; then
  cp .env.example .env
  echo "  ! .env 를 새로 만들었습니다. 열어서 VLLM_API_KEY 를 채운 뒤 다시 실행하세요."
  exit 1
fi

if ! grep -qE '^VLLM_API_KEY=.+' .env; then
  echo "  ✗ .env 의 VLLM_API_KEY 가 비어 있습니다. 값을 채운 뒤 다시 실행하세요."
  exit 1
fi

# ── 3) 의존성 (main 이 갱신돼 새 패키지가 생겨도 매번 자동 반영) ──────────
echo "  … npm install (최신 의존성 반영)"
npm install --no-audit --no-fund

# ── 4) 서버 실행 ────────────────────────────────────────────────────
echo "▶ 서버 시작 (http://127.0.0.1:8787).  Ctrl+C 로 종료."
exec npm start
