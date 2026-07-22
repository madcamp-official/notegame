#!/usr/bin/env bash
#
# 외부 유저 배포판(맥/윈도우) 빌드 + 패키징.
# 게임(Unity)이 갱신됐을 때(= main 을 pull 한 뒤) 이 스크립트 하나면 됩니다.
#   1) Unity batchmode 로 Mac(.app) + Windows(.exe) 빌드
#   2) 각 OS 폴더에 [게임 + tunnel키 + 런처 + 안내문] 을 묶어 zip 생성
#
# 사전:
#   - Unity 6000.5.4f1 + Windows/Mac Build Support 모듈 설치
#   - 배포용 터널 개인키를  ~/.ssh/notegame_tunnel  에 보유 (팀장 전달)
#   - Unity 에디터로 이 프로젝트가 열려있지 않아야 함(프로젝트 락 방지)
#
# 사용: BuildSupport/distribute/build-and-package.sh
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
D="$REPO_ROOT/BuildSupport/distribute"
UNITY="${UNITY_BIN:-/Applications/Unity/Hub/Editor/6000.5.4f1/Unity.app/Contents/MacOS/Unity}"
KEY="${TUNNEL_KEY:-$HOME/.ssh/notegame_tunnel}"
DIST="${DIST_OUT:-$HOME/notegame-dist}"

[[ -x "$UNITY" ]] || { echo "✗ Unity 없음: $UNITY (UNITY_BIN 으로 지정 가능)"; exit 1; }
[[ -f "$KEY"   ]] || { echo "✗ 터널 키 없음: $KEY (팀장에게 notegame_tunnel 받으세요)"; exit 1; }
if [[ -f "$REPO_ROOT/Temp/UnityLockfile" ]]; then
  echo "✗ Unity 에디터가 프로젝트를 열고 있습니다. 에디터를 닫고 다시 실행하세요."; exit 1
fi

# ── 1) Unity 빌드 ───────────────────────────────────────────────────
export DIST_OUT="$DIST"
rm -rf "$DIST/mac" "$DIST/windows"
LOG="$DIST/unity-build.log"; mkdir -p "$DIST"
echo "▶ Unity 빌드 중 (Mac + Windows)... 로그: $LOG"
"$UNITY" -batchmode -quit -nographics \
  -projectPath "$REPO_ROOT" \
  -executeMethod DistBuilder.BuildAll \
  -logFile "$LOG" -buildTarget OSXUniversal
grep -E "\[DistBuilder\]" "$LOG" || true

# ── 2) 패키징 ───────────────────────────────────────────────────────
PKG="$DIST/packages"; rm -rf "$PKG"; mkdir -p "$PKG"

# Mac
M="$PKG/NinjaAdventure-Mac"; mkdir -p "$M/tunnel"
cp -R "$DIST/mac/Ninja Adventure.app" "$M/"
cp "$KEY" "$M/tunnel/notegame_tunnel"; chmod 600 "$M/tunnel/notegame_tunnel"
cp "$D/게임 실행하기.command" "$M/"; chmod +x "$M/게임 실행하기.command"
cp "$D/읽어보세요.txt" "$M/"

# Windows (BurstDebugInformation_DoNotShip 제외)
W="$PKG/NinjaAdventure-Windows"; mkdir -p "$W/tunnel"
rsync -a --exclude '*_BurstDebugInformation_DoNotShip' "$DIST/windows/" "$W/"
cp "$KEY" "$W/tunnel/notegame_tunnel"; chmod 600 "$W/tunnel/notegame_tunnel"
cp "$D/게임 실행하기.bat" "$W/"
cp "$D/읽어보세요.txt" "$W/"

# zip
( cd "$PKG"
  zip -qr -X NinjaAdventure-Mac.zip NinjaAdventure-Mac
  zip -qr -X NinjaAdventure-Windows.zip NinjaAdventure-Windows )

echo "✓ 완료:"
du -sh "$PKG"/NinjaAdventure-Mac.zip "$PKG"/NinjaAdventure-Windows.zip
echo "  이 zip 두 개를 외부 유저에게 전달하면 됩니다."
