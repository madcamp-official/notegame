#!/usr/bin/env bash
# Build and package the external macOS and Windows releases.
# Each platform intentionally runs in a separate Unity process so its built-in
# UNITY_STANDALONE_* compiler symbols match the player being produced.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SUPPORT_DIR="$REPO_ROOT/BuildSupport/distribute"
UNITY="${UNITY_BIN:-/Applications/Unity/Hub/Editor/6000.5.4f1/Unity.app/Contents/MacOS/Unity}"
KEY="${TUNNEL_KEY:-$HOME/.ssh/notegame_tunnel}"
DIST="${DIST_OUT:-$HOME/notegame-dist}"
PORTABLE_NAME="NUPJUK-The-Last-Commit"

[[ -x "$UNITY" ]] || { echo "✗ Unity 없음: $UNITY (UNITY_BIN 으로 지정 가능)"; exit 1; }
[[ -f "$KEY" ]] || { echo "✗ 터널 키 없음: $KEY (팀장에게 notegame_tunnel 받으세요)"; exit 1; }
if [[ -f "$REPO_ROOT/Temp/UnityLockfile" ]]; then
  echo "✗ Unity 에디터가 프로젝트를 열고 있습니다. 에디터를 닫고 다시 실행하세요."
  exit 1
fi

mkdir -p "$DIST"
DIST="$(cd "$DIST" && pwd -P)"
case "$DIST" in
  /|"$HOME"|"$REPO_ROOT")
    echo "✗ DIST_OUT이 너무 넓은 경로를 가리킵니다: $DIST"
    exit 1
    ;;
esac

UNITY_INSTALL_ROOT="$(cd "$(dirname "$UNITY")/../../.." && pwd -P)"
WINDOWS_SUPPORT="$UNITY_INSTALL_ROOT/PlaybackEngines/WindowsStandaloneSupport"
if [[ ! -d "$WINDOWS_SUPPORT" ]]; then
  echo "✗ Unity Windows Build Support (Mono)가 설치되지 않았습니다."
  echo "  Unity Hub > Installs > 6000.5.4f1 > Add modules > Windows Build Support (Mono)"
  echo "  또는 Hub CLI에서 module id windows-mono를 설치하세요."
  exit 1
fi

export DIST_OUT="$DIST"
rm -rf "$DIST/mac" "$DIST/windows"
mkdir -p "$DIST"

MAC_LOG="$DIST/unity-build-macos.log"
WINDOWS_LOG="$DIST/unity-build-windows.log"

echo "▶ macOS 빌드 중... 로그: $MAC_LOG"
"$UNITY" -batchmode -quit -nographics \
  -projectPath "$REPO_ROOT" \
  -buildTarget StandaloneOSX \
  -executeMethod DistBuilder.BuildMac \
  -logFile "$MAC_LOG"
grep -E "\[DistBuilder\]|Keyboard Wanderer" "$MAC_LOG" || true

echo "▶ Windows x64 빌드 중... 로그: $WINDOWS_LOG"
"$UNITY" -batchmode -quit -nographics \
  -projectPath "$REPO_ROOT" \
  -buildTarget StandaloneWindows64 \
  -executeMethod DistBuilder.BuildWindows \
  -logFile "$WINDOWS_LOG"
grep -E "\[DistBuilder\]|Keyboard Wanderer" "$WINDOWS_LOG" || true

MAC_APP="$DIST/mac/$PORTABLE_NAME.app"
WINDOWS_EXE="$DIST/windows/$PORTABLE_NAME.exe"
[[ -d "$MAC_APP/Contents/MacOS" ]] || { echo "✗ macOS 앱이 생성되지 않았습니다: $MAC_APP"; exit 1; }
[[ -f "$WINDOWS_EXE" ]] || { echo "✗ Windows 실행 파일이 생성되지 않았습니다: $WINDOWS_EXE"; exit 1; }
codesign --verify --deep --strict "$MAC_APP"

PKG="$DIST/packages"
rm -rf "$PKG"
mkdir -p "$PKG"

# macOS: ditto preserves the signed app bundle metadata and executable bits.
MAC_PACKAGE="$PKG/NinjaAdventure-Mac"
mkdir -p "$MAC_PACKAGE/tunnel"
ditto "$MAC_APP" "$MAC_PACKAGE/$PORTABLE_NAME.app"
cp "$KEY" "$MAC_PACKAGE/tunnel/notegame_tunnel"
chmod 600 "$MAC_PACKAGE/tunnel/notegame_tunnel"
cp "$SUPPORT_DIR/게임 실행하기.command" "$MAC_PACKAGE/"
chmod +x "$MAC_PACKAGE/게임 실행하기.command"
cp "$SUPPORT_DIR/읽어보세요.txt" "$MAC_PACKAGE/"
ditto -c -k --sequesterRsrc --keepParent \
  "$MAC_PACKAGE" "$PKG/NinjaAdventure-Mac.zip"

# Windows: omit Unity's non-shipping Burst debug directory.
WINDOWS_PACKAGE="$PKG/NinjaAdventure-Windows"
mkdir -p "$WINDOWS_PACKAGE/tunnel"
rsync -a --exclude '*_BurstDebugInformation_DoNotShip' "$DIST/windows/" "$WINDOWS_PACKAGE/"
cp "$KEY" "$WINDOWS_PACKAGE/tunnel/notegame_tunnel"
chmod 600 "$WINDOWS_PACKAGE/tunnel/notegame_tunnel"
cp "$SUPPORT_DIR/게임 실행하기.bat" "$WINDOWS_PACKAGE/"
cp "$SUPPORT_DIR/읽어보세요.txt" "$WINDOWS_PACKAGE/"
( cd "$PKG" && zip -qr -X NinjaAdventure-Windows.zip NinjaAdventure-Windows )

echo "✓ 완료:"
du -sh "$PKG/NinjaAdventure-Mac.zip" "$PKG/NinjaAdventure-Windows.zip"
echo "  두 ZIP을 각 운영체제 사용자에게 전달하세요."
