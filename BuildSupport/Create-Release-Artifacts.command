#!/bin/zsh
set -euo pipefail

script_dir="${0:A:h}"
project_root="${script_dir:h}"
release_dir="$project_root/ReleaseArtifacts"
app_path="$project_root/Builds/NinjaAdventure.app"
version="1.0.0"
require_notarized="${NINJA_REQUIRE_NOTARIZED:-1}"

if [[ "$require_notarized" != "0" && "$require_notarized" != "1" ]]; then
  print -u2 "NINJA_REQUIRE_NOTARIZED는 0 또는 1이어야 합니다."
  exit 1
fi

for required in rsync ditto rg shasum plutil lipo codesign unzip node npm; do
  if ! command -v "$required" >/dev/null 2>&1; then
    print -u2 "필요한 도구를 찾지 못했습니다: $required"
    exit 1
  fi
done
node_major="$(node -p 'Number(process.versions.node.split(".")[0])')"
if [[ "$node_major" != <-> || "$node_major" -lt 20 ]]; then
  print -u2 "실행 ZIP 의존성을 고정하려면 Node.js 20 이상이 필요합니다: $node_major"
  exit 1
fi
if [[ ! -d "$app_path/Contents/MacOS" ]]; then
  print -u2 "최종 빌드를 찾지 못했습니다: $app_path"
  exit 1
fi

mkdir -p "$release_dir"
temp_root="$(mktemp -d "${TMPDIR:-/tmp}/ninja-adventure-release.XXXXXX")"
cleanup() {
  if [[ -n "${temp_root:-}" && -d "$temp_root" &&
        "$temp_root" == "${TMPDIR:-/tmp}/ninja-adventure-release."* ]]; then
    rm -rf "$temp_root"
  fi
}
trap cleanup EXIT INT TERM

common_excludes=(
  --exclude='.DS_Store'
  --include='.env.example'
  --exclude='.env'
  --exclude='.env.*'
  --exclude='node_modules/'
  --exclude='logs/'
  --exclude='generated/'
  --exclude='coverage/'
  --exclude='.nyc_output/'
  --exclude='*.zip'
  --exclude='*.unitypackage'
  --exclude='REPAIR_NOTES.md'
  --exclude='REPAIR_NOTES.md.meta'
  --exclude='Screenshots/'
  --exclude='Screenshots.meta'
  --exclude='_Recovery/'
  --exclude='_Recovery.meta'
  --exclude='InitTestScene*.unity'
  --exclude='InitTestScene*.unity.meta'
)

source_root="$temp_root/NinjaAdventure-Source-$version"
mkdir -p "$source_root"
for entry in Assets Packages ProjectSettings Server Database BuildSupport public; do
  rsync -a "${common_excludes[@]}" "$project_root/$entry" "$source_root/"
done
for entry in README.md .gitignore IcosahedronDice.cs; do
  cp "$project_root/$entry" "$source_root/$entry"
done

runtime_root="$temp_root/NinjaAdventure-$version"
mkdir -p "$runtime_root/Builds" "$runtime_root/BuildSupport"
ditto "$app_path" "$runtime_root/Builds/NinjaAdventure.app"
rsync -a "${common_excludes[@]}" "$project_root/Server" "$runtime_root/"
rsync -a "${common_excludes[@]}" "$project_root/Database" "$runtime_root/"
(
  cd "$runtime_root/Server"
  npm ci --omit=dev --ignore-scripts --no-audit --no-fund
)
if [[ ! -s "$runtime_root/Server/node_modules/pg/package.json" ]]; then
  print -u2 "실행 번들에 고정된 PostgreSQL 클라이언트 의존성을 설치하지 못했습니다."
  exit 1
fi
if find "$runtime_root/Server/node_modules" -type f -name '*.node' -print -quit | grep -q .; then
  print -u2 "실행 번들에 아키텍처 종속 Node native addon이 포함됐습니다."
  exit 1
fi
cp "$project_root/BuildSupport/Run-Ninja-Adventure.command" "$runtime_root/BuildSupport/"
cp "$project_root/BuildSupport/Start-Persistent-Postgres.command" "$runtime_root/BuildSupport/"
cp "$project_root/BuildSupport/docker-compose.release.yml" "$runtime_root/BuildSupport/"
cp "$project_root/BuildSupport/Start-Ninja-Adventure-Package.command" \
  "$runtime_root/Ninja Adventure.command"
cp "$project_root/BuildSupport/RUNTIME-README.md" "$runtime_root/README.md"
cp "$project_root/BuildSupport/THIRD-PARTY-NOTICES.md" "$runtime_root/THIRD-PARTY-NOTICES.md"
if [[ -f "$release_dir/FINAL-VALIDATION.md" ]]; then
  cp "$release_dir/FINAL-VALIDATION.md" "$runtime_root/FINAL-VALIDATION.md"
fi

for staged_root in "$source_root" "$runtime_root"; do
  if find "$staged_root" -type f \( \( -name '.env*' ! -name '.env.example' \) -o -name '*.zip' \) \
      -print -quit | grep -q .; then
    print -u2 "배포 제외 파일이 staging에 포함됐습니다: $staged_root"
    exit 1
  fi
  if rg -q --hidden --pcre2 \
      '(?:AIza[0-9A-Za-z_-]{20,}|github_pat_[0-9A-Za-z_]{20,}|gh[pousr]_[0-9A-Za-z]{20,}|AKIA[0-9A-Z]{16}|xox[baprs]-[0-9A-Za-z-]{10,}|sk-(?:proj-)?[0-9A-Za-z_-]{20,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)' \
      "$staged_root"; then
    print -u2 "staging에서 비밀키 또는 API token 형태의 문자열을 감지했습니다. 배포를 중단합니다."
    exit 1
  fi
done

info_plist="$app_path/Contents/Info.plist"
game_binary="$(find "$app_path/Contents/MacOS" -type f -perm +111 -print -quit)"
if [[ -z "$game_binary" || ! -f "$info_plist" ]]; then
  print -u2 "앱 bundle에 실행 파일 또는 Info.plist가 없습니다."
  exit 1
fi
if [[ "$(plutil -extract CFBundleIdentifier raw "$info_plist")" != "com.madcamp.ninjaadventure" ||
      "$(plutil -extract CFBundleShortVersionString raw "$info_plist")" != "$version" ||
      "$(plutil -extract CFBundleVersion raw "$info_plist")" != "1" ]]; then
  print -u2 "앱 bundle ID·version·build metadata가 출시 계약과 다릅니다."
  exit 1
fi
architectures="$(lipo -archs "$game_binary")"
if [[ " $architectures " != *" x86_64 "* || " $architectures " != *" arm64 "* ]]; then
  print -u2 "앱이 Universal(x86_64 + arm64) 빌드가 아닙니다: $architectures"
  exit 1
fi
codesign --verify --deep --strict --verbose=2 "$app_path"
codesign --verify --deep --strict --verbose=2 "$runtime_root/Builds/NinjaAdventure.app"
if [[ "$require_notarized" == "1" ]]; then
  for required in xcrun spctl; do
    if ! command -v "$required" >/dev/null 2>&1; then
      print -u2 "공증 검증에 필요한 도구를 찾지 못했습니다: $required"
      exit 1
    fi
  done
  xcrun stapler validate "$app_path"
  spctl --assess --type execute --verbose=4 "$app_path"
  xcrun stapler validate "$runtime_root/Builds/NinjaAdventure.app"
  spctl --assess --type execute --verbose=4 "$runtime_root/Builds/NinjaAdventure.app"
fi
for notice in NeoDunggeunmoPro-LICENSE.txt 'LiberationSans - OFL.txt' LICENSE.txt THIRD-PARTY-NOTICES.md; do
  if [[ ! -f "$app_path/Contents/Resources/ThirdPartyLicenses/$notice" ]]; then
    print -u2 "앱에 제3자 고지가 누락됐습니다: $notice"
    exit 1
  fi
done

source_zip_tmp="$temp_root/NinjaAdventure-Source-$version.zip"
runtime_zip_tmp="$temp_root/NinjaAdventure-macOS-$version.zip"
ditto -c -k --sequesterRsrc --keepParent "$source_root" "$source_zip_tmp"
ditto -c -k --sequesterRsrc --keepParent "$runtime_root" "$runtime_zip_tmp"
unzip -tq "$source_zip_tmp" >/dev/null
unzip -tq "$runtime_zip_tmp" >/dev/null

runtime_check_root="$temp_root/runtime-archive-check"
mkdir -p "$runtime_check_root"
ditto -x -k "$runtime_zip_tmp" "$runtime_check_root"
archived_app="$runtime_check_root/NinjaAdventure-$version/Builds/NinjaAdventure.app"
codesign --verify --deep --strict --verbose=2 "$archived_app"
if [[ "$require_notarized" == "1" ]]; then
  xcrun stapler validate "$archived_app"
  spctl --assess --type execute --verbose=4 "$archived_app"
fi

mv -f "$source_zip_tmp" "$release_dir/NinjaAdventure-Source-$version.zip"
mv -f "$runtime_zip_tmp" "$release_dir/NinjaAdventure-macOS-$version.zip"
(
  cd "$release_dir"
  shasum -a 256 "NinjaAdventure-Source-$version.zip" \
    "NinjaAdventure-macOS-$version.zip" > SHA256SUMS.txt
)

print "배포 산출물 생성 완료:"
print "  $release_dir/NinjaAdventure-Source-$version.zip"
print "  $release_dir/NinjaAdventure-macOS-$version.zip"
print "  $release_dir/SHA256SUMS.txt"
