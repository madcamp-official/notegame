#!/usr/bin/env bash
#
# camp-11 게임 서버 재배포.
# Server/ 코드가 갱신됐을 때(= main 을 pull 한 뒤) 이 스크립트 하나면 됩니다.
#   - Server/ 코드를 camp-11:/opt/notegame-server 로 rsync (원격 .env 는 보존)
#   - systemd 서비스 재시작 + /health 확인
#
# 사전: KAIST VPN ON, ~/.ssh/qwen_key 보유 (camp-11 root 접속키)
# 사용: BuildSupport/distribute/deploy-server.sh
#
set -euo pipefail

REMOTE="${QWEN_REMOTE:-root@172.10.5.138}"
KEY="${QWEN_SSH_KEY:-$HOME/.ssh/qwen_key}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

[[ -f "$KEY" ]] || { echo "✗ SSH 키 없음: $KEY"; exit 1; }

echo "▶ 코드 rsync -> $REMOTE:/opt/notegame-server (원격 .env 보존)"
rsync -az --delete -e "ssh -i $KEY -o ConnectTimeout=15" \
  --exclude 'node_modules' --exclude '.env' --exclude '.git' \
  --exclude 'test' --exclude 'coverage' --exclude 'logs' --exclude '*.log' \
  --exclude 'run.sh' --exclude 'run.ps1' --exclude 'run.bat' \
  "$REPO_ROOT/Server/" "$REMOTE:/opt/notegame-server/"

echo "▶ 서비스 재시작 + 헬스체크"
ssh -i "$KEY" "$REMOTE" '
  systemctl restart notegame-server.service
  sleep 2
  printf "  service: "; systemctl is-active notegame-server.service
  curl -s -o /dev/null -w "  health: HTTP %{http_code}\n" http://127.0.0.1:8787/health
'
echo "✓ 서버 재배포 완료."
