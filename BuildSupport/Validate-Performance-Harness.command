#!/bin/zsh
set -euo pipefail

script_dir="${0:A:h}"
sampler="$script_dir/Measure-Ninja-Adventure-Performance.command"
summarizer="$script_dir/Summarize-Ninja-Performance.mjs"
guide="$script_dir/PERFORMANCE-QA.md"

for required_file in "$sampler" "$summarizer" "$guide"; do
  if [[ ! -s "$required_file" ]]; then
    print -u2 "성능 harness 필수 파일이 없거나 비어 있습니다: $required_file"
    exit 1
  fi
done

zsh -n "$sampler"
node --check "$summarizer"
node "$summarizer" --self-test
env NINJA_PERF_VALIDATE_ONLY=1 "$sampler"

for contract in \
  'ps -p "$player_pid" -o comm=' \
  'ps -p "$player_pid" -o %cpu=,rss=,vsz=,time=' \
  'ps -M -p "$player_pid"' \
  'Device Utilization %' \
  'Game Performance' \
  'ca-client-present-request' \
  'metal-gpu-intervals' \
  'device-thermal-state-intervals' \
  'NINJA_PERF_METAL_HUD_SCHEMA' \
  'metal-HUD:' \
  '--metal-hud-log' \
  'playerLogWindowExact' \
  'player-log-window.txt'; do
  if ! grep -Fq -- "$contract" "$sampler"; then
    print -u2 "성능 측정 계약이 누락됐습니다: $contract"
    exit 1
  fi
done

for contract in \
  'parseProcessPid(row[6]) === pid' \
  'parseProcessPid(row[10]) !== pid' \
  'frameStatus = fpsWithinTarget' \
  'parseMetalHudRecord' \
  'summarizeMetalHudLog' \
  'source: "metal-hud-log"' \
  'preferredSource: "xctrace"' \
  'framesByNumber.set(frame.frameNumber, frame)' \
  'conflictingOverlapCount' \
  'newest-log-line-wins' \
  'graphicsMemoryMaxMB' \
  'gpuTimeP95Ms' \
  'Performance-Index.md'; do
  if ! grep -Fq -- "$contract" "$summarizer"; then
    print -u2 "성능 요약 계약이 누락됐습니다: $contract"
    exit 1
  fi
done

for contract in \
  'MTL_HUD_ENABLED=1' \
  'MTL_HUD_LOG_ENABLED=1' \
  'https://developer.apple.com/documentation/xcode/monitoring-your-metal-apps-graphics-performance'; do
  if ! grep -Fq -- "$contract" "$guide"; then
    print -u2 "성능 QA 가이드 계약이 누락됐습니다: $contract"
    exit 1
  fi
done

print "Ninja Adventure 성능 harness 검증 완료."
