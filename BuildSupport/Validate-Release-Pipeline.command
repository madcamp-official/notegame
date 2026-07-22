#!/bin/zsh
set -euo pipefail

script_dir="${0:A:h}"
project_root="${script_dir:h}"
app_path="${NINJA_ADVENTURE_APP:-$project_root/Builds/NinjaAdventure.app}"
require_app="${NINJA_REQUIRE_APP:-0}"
require_notarized="${NINJA_REQUIRE_NOTARIZED:-0}"
validate_mutation_recovery="${NINJA_VALIDATE_MUTATION_RECOVERY:-1}"
version="1.0.0"

for flag_name in require_app require_notarized validate_mutation_recovery; do
  flag_value="${(P)flag_name}"
  if [[ "$flag_value" != "0" && "$flag_value" != "1" ]]; then
    print -u2 "$flag_name 값은 0 또는 1이어야 합니다."
    exit 1
  fi
done

for required in awk codesign ditto docker file find lipo mktemp node npm plutil rg sips zsh; do
  if ! command -v "$required" >/dev/null 2>&1; then
    print -u2 "릴리스 검증 도구를 찾지 못했습니다: $required"
    exit 1
  fi
done
node_major="$(node -p 'Number(process.versions.node.split(".")[0])')"
if [[ "$node_major" != <-> || "$node_major" -lt 20 ]]; then
  print -u2 "릴리스 검증에는 Node.js 20 이상이 필요합니다: $node_major"
  exit 1
fi

for script in \
  "$script_dir/Create-Release-Artifacts.command" \
  "$script_dir/Run-Ninja-Adventure.command" \
  "$script_dir/Sign-and-Verify-macOS.command" \
  "$script_dir/Start-Ninja-Adventure-Package.command" \
  "$script_dir/Start-Persistent-Postgres.command" \
  "$script_dir/Validate-Release-Pipeline.command"; do
  zsh -n "$script"
done
bash -n "$script_dir/distribute/build-and-package.sh"
plutil -lint "$script_dir/NinjaAdventure.entitlements" >/dev/null
if ! docker compose version >/dev/null 2>&1; then
  print -u2 "Docker Compose v2가 필요합니다."
  exit 1
fi
env \
  NINJA_POSTGRES_USER=keyboard_wanderer \
  NINJA_POSTGRES_DB=ninja_adventure \
  NINJA_POSTGRES_PORT=55433 \
  NINJA_POSTGRES_PASSWORD_FILE=/tmp/ninja-adventure-validation-password \
  docker compose --project-name ninja-adventure-validation \
    --file "$script_dir/docker-compose.release.yml" config --quiet

compose_file="$script_dir/docker-compose.release.yml"
if ! rg -q 'image: postgres:[0-9]+\.[0-9]+-alpine@sha256:[0-9a-f]{64}$' "$compose_file"; then
  print -u2 "PostgreSQL 이미지는 release와 multi-architecture digest로 고정해야 합니다."
  exit 1
fi
for migration_path in "$project_root/Database/migrations/"*.sql(N); do
  migration_name="${migration_path:t}"
  if ! rg -Fq "../Database/migrations/$migration_name" "$compose_file"; then
    print -u2 "release PostgreSQL 초기화에 migration이 누락됐습니다: $migration_name"
    exit 1
  fi
done
if ! rg -Fq '../Database/seeds/001_reference_catalogs.sql' "$compose_file"; then
  print -u2 "release PostgreSQL 초기화에 reference seed가 누락됐습니다."
  exit 1
fi

for required_file in \
  "$script_dir/THIRD-PARTY-NOTICES.md" \
  "$script_dir/RUNTIME-README.md" \
  "$project_root/Assets/KeyboardWanderer/Resources/Fonts/NeoDunggeunmoPro-LICENSE.txt" \
  "$project_root/Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt" \
  "$project_root/Assets/NinjaAdventure/LICENSE.txt"; do
  if [[ ! -s "$required_file" ]]; then
    print -u2 "필수 릴리스 고지가 없거나 비어 있습니다: $required_file"
    exit 1
  fi
done

launcher_script="$script_dir/Run-Ninja-Adventure.command"
for launcher_contract in \
  'NINJA_ALLOW_EPHEMERAL_STORAGE' \
  'Start-Persistent-Postgres.command' \
  '012_request_idempotency_and_schema_readiness' \
  'memory 서버는 출시 실행에서 사용할 수 없습니다'; do
  if ! rg -Fq "$launcher_contract" "$launcher_script"; then
    print -u2 "출시 런처의 영구 저장 계약이 누락됐습니다: $launcher_contract"
    exit 1
  fi
done

artifact_script="$script_dir/Create-Release-Artifacts.command"
for artifact_contract in \
  'npm ci --omit=dev --ignore-scripts --no-audit --no-fund' \
  'RUNTIME-README.md' \
  'Start-Ninja-Adventure-Package.command' \
  'Start-Persistent-Postgres.command' \
  'docker-compose.release.yml'; do
  if ! rg -Fq "$artifact_contract" "$artifact_script"; then
    print -u2 "실행 ZIP 계약이 누락됐습니다: $artifact_contract"
    exit 1
  fi
done

build_script="$project_root/Assets/KeyboardWanderer/Editor/KeyboardWandererBuild.cs"
for build_contract in \
  'BuildOptions.CleanBuildCache | BuildOptions.StrictMode' \
  'BuildTarget.StandaloneWindows64' \
  'BuildPipeline.IsBuildTargetSupported' \
  'InstallThirdPartyNotices(outputPath, buildTarget);' \
  'SignAndVerifyLocalBuild(outputPath);' \
  'THIRD-PARTY-NOTICES.md'; do
  if ! rg -Fq "$build_contract" "$build_script"; then
    print -u2 "Unity 빌드 계약이 누락됐습니다: $build_contract"
    exit 1
  fi
done

distribution_script="$script_dir/distribute/build-and-package.sh"
for distribution_contract in \
  '-buildTarget StandaloneOSX' \
  '-executeMethod DistBuilder.BuildMac' \
  '-buildTarget StandaloneWindows64' \
  '-executeMethod DistBuilder.BuildWindows' \
  'WindowsStandaloneSupport' \
  'PORTABLE_NAME="NUPJUK-The-Last-Commit"' \
  'WINDOWS_EXE="$DIST/windows/$PORTABLE_NAME.exe"' \
  'ditto -c -k --sequesterRsrc --keepParent'; do
  if ! rg -Fq -- "$distribution_contract" "$distribution_script"; then
    print -u2 "데스크톱 배포 계약이 누락됐습니다: $distribution_contract"
    exit 1
  fi
done

icon_path="$project_root/Assets/KeyboardWanderer/Art/AppIcon/NinjaAdventureAppIcon.png"
icon_width="$(sips -g pixelWidth "$icon_path" 2>/dev/null | awk '/pixelWidth:/ { print $2 }')"
icon_height="$(sips -g pixelHeight "$icon_path" 2>/dev/null | awk '/pixelHeight:/ { print $2 }')"
if [[ "$icon_width" != <-> || "$icon_height" != <-> ||
      "$icon_width" -ne "$icon_height" || "$icon_width" -lt 1024 ]]; then
  print -u2 "앱 아이콘은 최소 1024px 정사각형이어야 합니다: ${icon_width:-?}x${icon_height:-?}"
  exit 1
fi

if [[ ! -d "$app_path/Contents/MacOS" ]]; then
  if [[ "$require_app" == "1" ]]; then
    print -u2 "검증할 NinjaAdventure.app이 없습니다: $app_path"
    exit 1
  fi
  print "릴리스 파이프라인 정적 검증 완료 (앱 검증은 빌드 후 실행 필요)."
  exit 0
fi

info_plist="$app_path/Contents/Info.plist"
if [[ "$(plutil -extract CFBundleIdentifier raw "$info_plist")" != "com.madcamp.ninjaadventure" ||
      "$(plutil -extract CFBundleShortVersionString raw "$info_plist")" != "$version" ||
      "$(plutil -extract CFBundleVersion raw "$info_plist")" != "1" ]]; then
  print -u2 "앱 bundle ID·version·build metadata가 출시 계약과 다릅니다."
  exit 1
fi

for notice in NeoDunggeunmoPro-LICENSE.txt 'LiberationSans - OFL.txt' LICENSE.txt THIRD-PARTY-NOTICES.md; do
  if [[ ! -s "$app_path/Contents/Resources/ThirdPartyLicenses/$notice" ]]; then
    print -u2 "앱에 제3자 고지가 누락됐습니다: $notice"
    exit 1
  fi
done

native_code=()
while IFS= read -r -d '' candidate; do
  if [[ "$(file -b "$candidate")" == *"Mach-O"* ]]; then
    native_code+=("$candidate")
  fi
done < <(find "$app_path/Contents" -type f -print0)
if (( ${#native_code[@]} == 0 )); then
  print -u2 "앱 bundle에서 Mach-O 코드를 찾지 못했습니다."
  exit 1
fi
for candidate in "${native_code[@]}"; do
  architectures="$(lipo -archs "$candidate")"
  if [[ " $architectures " != *" x86_64 "* || " $architectures " != *" arm64 "* ]]; then
    print -u2 "Universal(x86_64 + arm64) 코드가 아닙니다: $candidate ($architectures)"
    exit 1
  fi
done
codesign --verify --deep --strict --verbose=2 "$app_path"
signature_details="$(codesign -dvvv "$app_path" 2>&1)"
if [[ "$signature_details" != *"flags="*"runtime"* ]]; then
  print -u2 "앱에 Hardened Runtime 서명이 없습니다."
  exit 1
fi

if [[ "$require_notarized" == "1" ]]; then
  xcrun stapler validate "$app_path"
  spctl --assess --type execute --verbose=4 "$app_path"
fi

if [[ "$validate_mutation_recovery" == "1" ]]; then
  regression_root="$(mktemp -d "${TMPDIR:-/tmp}/ninja-release-regression.XXXXXX")"
  cleanup() {
    if [[ -n "${regression_root:-}" && -d "$regression_root" &&
          "$regression_root" == "${TMPDIR:-/tmp}/ninja-release-regression."* ]]; then
      rm -rf "$regression_root"
    fi
  }
  trap cleanup EXIT INT TERM
  regression_app="$regression_root/NinjaAdventure.app"
  ditto "$app_path" "$regression_app"
  cp "$script_dir/THIRD-PARTY-NOTICES.md" \
    "$regression_app/Contents/Resources/ThirdPartyLicenses/PIPELINE-REGRESSION.txt"
  if codesign --verify --deep --strict "$regression_app" >/dev/null 2>&1; then
    print -u2 "리소스 변조가 예상대로 기존 서명을 무효화하지 않았습니다."
    exit 1
  fi
  env NINJA_ADVENTURE_APP="$regression_app" NINJA_CODESIGN_IDENTITY=- \
    zsh "$script_dir/Sign-and-Verify-macOS.command" >/dev/null
  codesign --verify --deep --strict --verbose=2 "$regression_app"
fi

print "릴리스 파이프라인 검증 완료: $app_path"
