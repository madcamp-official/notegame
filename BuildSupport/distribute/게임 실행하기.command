#!/bin/bash
# Ninja Adventure 실행기 (Mac)
# 서버로 가는 안전한 통로(터널)를 연 뒤 게임을 켭니다.
cd "$(dirname "$0")"

KEY="./tunnel/notegame_tunnel"
SERVER_HOST="172.10.5.138"

echo "▶ Ninja Adventure 준비 중..."

# 0) VPN 확인 (camp-11 SSH 포트에 닿는지)
if ! nc -z -w 6 "$SERVER_HOST" 22 2>/dev/null; then
  echo ""
  echo "  ✗ 게임 서버에 연결할 수 없습니다."
  echo "    KAIST VPN 이 켜져 있는지 확인한 뒤 다시 실행해 주세요."
  echo ""
  read -n 1 -s -r -p "아무 키나 누르면 닫힙니다..."
  exit 1
fi

# 1) 키 권한 정리 (압축 해제 시 느슨해질 수 있음)
chmod 600 "$KEY" 2>/dev/null

# 2) 터널이 없으면 연다 (localhost:8787 -> 서버:8787, 이 통로로만 이동 가능)
if ! nc -z 127.0.0.1 8787 2>/dev/null; then
  echo "  … 서버 통로 여는 중"
  ssh -f -N -i "$KEY" \
      -o StrictHostKeyChecking=accept-new \
      -o ExitOnForwardFailure=yes \
      -o ServerAliveInterval=30 \
      -L 8787:127.0.0.1:8787 gametunnel@"$SERVER_HOST"
fi

# 3) 게임 실행 (창이 닫힐 때까지 대기)
echo "  ✓ 게임을 시작합니다."
open -W "Ninja Adventure.app"

# 4) 게임 종료 후 통로 정리
pkill -f "8787:127.0.0.1:8787" 2>/dev/null
echo "  게임을 종료했습니다."
