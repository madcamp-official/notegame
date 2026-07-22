#!/bin/zsh
set -euo pipefail

script_dir="${0:A:h}"
project_root="${script_dir:h}"
app_path="${NINJA_ADVENTURE_APP:-$project_root/Builds/NinjaAdventure.app}"
entitlements="$script_dir/NinjaAdventure.entitlements"
identity="${NINJA_CODESIGN_IDENTITY:-}"

for required in codesign file find lipo plutil security; do
  if ! command -v "$required" >/dev/null 2>&1; then
    print -u2 "필요한 도구를 찾지 못했습니다: $required"
    exit 1
  fi
done

if [[ ! -d "$app_path/Contents/MacOS" ]]; then
  print -u2 "서명할 앱을 찾지 못했습니다: $app_path"
  exit 1
fi
plutil -lint "$entitlements" >/dev/null

if [[ -z "$identity" ]]; then
  identity="$(security find-identity -v -p codesigning 2>/dev/null |
    sed -n 's/.*"\(Developer ID Application:[^"]*\)".*/\1/p' | head -1)"
fi
if [[ -z "$identity" ]]; then
  print -u2 "Developer ID Application 인증서가 없습니다."
  print -u2 "로컬 검증은 NINJA_CODESIGN_IDENTITY에 사용할 서명 ID를 지정하세요."
  exit 1
fi

game_binary="$(find "$app_path/Contents/MacOS" -type f -perm +111 -print -quit)"
if [[ -z "$game_binary" ]]; then
  print -u2 "앱 bundle의 주 실행 파일을 찾지 못했습니다."
  exit 1
fi

# Apple recommends signing nested code from the inside out. --deep remains a
# verification option only; using it to sign can miss or incorrectly re-sign
# nested code in non-standard layouts.
sign_arguments=(--force --options runtime --sign "$identity")
if [[ "$identity" == "Developer ID Application:"* ]]; then
  sign_arguments+=(--timestamp)
fi

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
  if [[ "$candidate" != "$game_binary" ]]; then
    codesign "${sign_arguments[@]}" "$candidate"
  fi
done
codesign "${sign_arguments[@]}" --entitlements "$entitlements" "$app_path"
codesign --verify --deep --strict --verbose=2 "$app_path"
signature_details="$(codesign -dvvv "$app_path" 2>&1)"
if [[ "$signature_details" != *"flags="*"runtime"* ]]; then
  print -u2 "Hardened Runtime 서명 flag가 없습니다."
  exit 1
fi
embedded_entitlements="$(codesign -d --entitlements :- "$app_path" 2>&1)"
if [[ "$embedded_entitlements" == *"com.apple.security.get-task-allow"* ]]; then
  print -u2 "배포 서명에 금지된 get-task-allow entitlement가 포함됐습니다."
  exit 1
fi
for required_entitlement in \
  com.apple.security.cs.disable-library-validation \
  com.apple.security.cs.disable-executable-page-protection; do
  if [[ "$embedded_entitlements" != *"<$required_entitlement>"* &&
        "$embedded_entitlements" != *"<key>$required_entitlement</key>"* ]]; then
    print -u2 "Unity Hardened Runtime entitlement가 누락됐습니다: $required_entitlement"
    exit 1
  fi
done

for candidate in "${native_code[@]}"; do
  architectures="$(lipo -archs "$candidate")"
  if [[ " $architectures " != *" x86_64 "* || " $architectures " != *" arm64 "* ]]; then
    print -u2 "Universal(x86_64 + arm64) 코드가 아닙니다: $candidate ($architectures)"
    exit 1
  fi
done
architectures="$(lipo -archs "$game_binary")"

if [[ -n "${NINJA_NOTARY_PROFILE:-}" ]]; then
  if [[ "$identity" != "Developer ID Application:"* ]]; then
    print -u2 "Apple 공증은 Developer ID Application 서명으로만 진행할 수 있습니다."
    exit 1
  fi
  notary_temp="$(mktemp -d "${TMPDIR:-/tmp}/ninja-adventure-notary.XXXXXX")"
  archive="$notary_temp/NinjaAdventure.zip"
  notary_result="$notary_temp/notary-result.json"
  cleanup() {
    if [[ -d "$notary_temp" && "$notary_temp" == "${TMPDIR:-/tmp}/ninja-adventure-notary."* ]]; then
      rm -rf "$notary_temp"
    fi
  }
  trap cleanup EXIT INT TERM
  ditto -c -k --sequesterRsrc --keepParent "$app_path" "$archive"
  xcrun notarytool submit "$archive" --keychain-profile "$NINJA_NOTARY_PROFILE" \
    --wait --output-format json >"$notary_result"
  notary_status="$(plutil -extract status raw "$notary_result" 2>/dev/null || true)"
  if [[ "$notary_status" != "Accepted" ]]; then
    print -u2 "Apple 공증이 승인되지 않았습니다: ${notary_status:-unknown}"
    plutil -p "$notary_result" >&2 || true
    exit 1
  fi
  xcrun stapler staple "$app_path"
  xcrun stapler validate "$app_path"
  codesign --verify --deep --strict --verbose=2 "$app_path"
  spctl --assess --type execute --verbose=4 "$app_path"
fi

print "서명·Universal 검증 완료: $app_path"
print "서명 ID: $identity"
print "아키텍처: $architectures"
