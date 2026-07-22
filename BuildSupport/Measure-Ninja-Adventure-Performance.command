#!/bin/zsh
set -euo pipefail
setopt extendedglob
zmodload zsh/datetime

script_dir="${0:A:h}"
project_root="${script_dir:h}"
app_path="${NINJA_PERF_APP:-${NINJA_ADVENTURE_APP:-$project_root/Builds/NinjaAdventure.app}}"
scenario="${NINJA_PERF_SCENARIO:-}"
duration_seconds="${NINJA_PERF_DURATION_SECONDS:-30}"
interval_milliseconds="${NINJA_PERF_INTERVAL_MILLISECONDS:-1000}"
xctrace_mode="${NINJA_PERF_XCTRACE:-auto}"
metal_hud_schema="${NINJA_PERF_METAL_HUD_SCHEMA:-auto}"
capture_stacks="${NINJA_PERF_CAPTURE_STACKS:-0}"
target_fps="${NINJA_PERF_TARGET_FPS:-30}"
output_root="${NINJA_PERF_OUTPUT_ROOT:-$project_root/ReleaseArtifacts/Evidence/Performance}"
notes="${NINJA_PERF_NOTES:-}"
explicit_player_log="${NINJA_PERF_PLAYER_LOG:-}"
validate_only="${NINJA_PERF_VALIDATE_ONLY:-0}"
summary_script="$script_dir/Summarize-Ninja-Performance.mjs"

for required in awk date grep head ioreg node plutil ps sed sort stat uname; do
  if ! command -v "$required" >/dev/null 2>&1; then
    print -u2 "성능 측정 도구를 찾지 못했습니다: $required"
    exit 1
  fi
done
if [[ ! -f "$summary_script" ]]; then
  print -u2 "성능 요약기를 찾지 못했습니다: $summary_script"
  exit 1
fi
node --check "$summary_script" >/dev/null

if [[ "$validate_only" != "0" && "$validate_only" != "1" ]]; then
  print -u2 "NINJA_PERF_VALIDATE_ONLY는 0 또는 1이어야 합니다."
  exit 1
fi
if [[ "$validate_only" == "1" ]]; then
  node "$summary_script" --self-test
  print "성능 harness 정적 검증 완료."
  exit 0
fi

if [[ -z "$scenario" || ! "$scenario" =~ '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$' ]]; then
  print -u2 "NINJA_PERF_SCENARIO를 영문/숫자/._- 조합으로 지정하세요(예: menu, dialogue, movement, combat, llm-wait, idle)."
  exit 1
fi
if [[ "$duration_seconds" != <-> || "$duration_seconds" -lt 5 || "$duration_seconds" -gt 86400 ]]; then
  print -u2 "NINJA_PERF_DURATION_SECONDS는 5~86400 범위의 정수여야 합니다."
  exit 1
fi
if [[ "$interval_milliseconds" != <-> || "$interval_milliseconds" -lt 250 ||
      "$interval_milliseconds" -gt 60000 || "$interval_milliseconds" -gt $((duration_seconds * 1000)) ]]; then
  print -u2 "NINJA_PERF_INTERVAL_MILLISECONDS는 250~60000 범위이고 전체 구간 이하여야 합니다."
  exit 1
fi
if [[ "$target_fps" != <-> || "$target_fps" -lt 1 || "$target_fps" -gt 240 ]]; then
  print -u2 "NINJA_PERF_TARGET_FPS는 1~240 범위의 정수여야 합니다."
  exit 1
fi
if [[ "$xctrace_mode" != "auto" && "$xctrace_mode" != "required" &&
      "$xctrace_mode" != "0" && "$xctrace_mode" != "1" ]]; then
  print -u2 "NINJA_PERF_XCTRACE는 auto, required, 0, 1 중 하나여야 합니다."
  exit 1
fi
if [[ "$metal_hud_schema" != "auto" && "$metal_hud_schema" != "current" &&
      "$metal_hud_schema" != "legacy" ]]; then
  print -u2 "NINJA_PERF_METAL_HUD_SCHEMA는 auto, current, legacy 중 하나여야 합니다."
  exit 1
fi
if [[ "$capture_stacks" != "0" && "$capture_stacks" != "1" ]]; then
  print -u2 "NINJA_PERF_CAPTURE_STACKS는 0 또는 1이어야 합니다."
  exit 1
fi

app_path="${app_path:A}"
if [[ ! -f "$app_path/Contents/Info.plist" ]]; then
  print -u2 "측정할 macOS 앱을 찾지 못했습니다: $app_path"
  exit 1
fi
bundle_executable="$(plutil -extract CFBundleExecutable raw "$app_path/Contents/Info.plist" 2>/dev/null || true)"
bundle_identifier="$(plutil -extract CFBundleIdentifier raw "$app_path/Contents/Info.plist" 2>/dev/null || true)"
if [[ -z "$bundle_executable" ]]; then
  print -u2 "CFBundleExecutable을 읽지 못했습니다: $app_path/Contents/Info.plist"
  exit 1
fi
game_executable="$app_path/Contents/MacOS/$bundle_executable"
if [[ ! -x "$game_executable" ]]; then
  print -u2 "실행 가능한 Unity Player를 찾지 못했습니다: $game_executable"
  exit 1
fi

player_pid="${NINJA_PERF_PID:-}"
if [[ -n "$player_pid" ]]; then
  if [[ "$player_pid" != <-> || "$player_pid" -lt 2 ]]; then
    print -u2 "NINJA_PERF_PID는 실행 중인 Player의 정수 PID여야 합니다."
    exit 1
  fi
else
  matching_pids=()
  while read -r candidate_pid candidate_command; do
    if [[ -n "$candidate_command" && "${candidate_command:A}" == "$game_executable" ]]; then
      matching_pids+=("$candidate_pid")
    fi
  done < <(ps -axo pid=,comm=)
  if (( ${#matching_pids[@]} == 0 )); then
    print -u2 "실행 중인 Player를 찾지 못했습니다. 먼저 빌드 앱을 실행하고 실제 GUI 상태를 준비하세요."
    exit 1
  fi
  if (( ${#matching_pids[@]} > 1 )); then
    print -u2 "같은 빌드의 Player가 여러 개입니다: ${matching_pids[*]}"
    print -u2 "대상을 명확히 NINJA_PERF_PID로 지정하세요."
    exit 1
  fi
  player_pid="${matching_pids[1]}"
fi

if ! kill -0 "$player_pid" 2>/dev/null; then
  print -u2 "Player PID가 실행 중이 아닙니다: $player_pid"
  exit 1
fi
actual_command="$(ps -p "$player_pid" -o comm= | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"
actual_uid="$(ps -p "$player_pid" -o uid= | sed 's/[[:space:]]//g')"
if [[ -n "$actual_command" ]]; then
  actual_command="${actual_command:A}"
fi
if [[ "$actual_command" != "$game_executable" ]]; then
  print -u2 "PID가 지정한 앱의 실행 파일과 일치하지 않습니다."
  print -u2 "기대: $game_executable"
  print -u2 "실제: ${actual_command:-프로세스 없음}"
  exit 1
fi
if [[ "$actual_uid" != "$(id -u)" ]]; then
  print -u2 "현재 사용자 소유가 아닌 프로세스는 측정하지 않습니다: PID $player_pid"
  exit 1
fi

xctrace_path=""
if command -v xctrace >/dev/null 2>&1; then
  xctrace_path="$(command -v xctrace)"
elif command -v xcrun >/dev/null 2>&1; then
  xctrace_path="$(xcrun -f xctrace 2>/dev/null || true)"
fi
if [[ -n "$xctrace_path" ]] && ! "$xctrace_path" version >/dev/null 2>&1; then
  xctrace_path=""
fi
if [[ -z "$xctrace_path" && -x /Applications/Xcode.app/Contents/Developer/usr/bin/xctrace ]]; then
  xctrace_path=/Applications/Xcode.app/Contents/Developer/usr/bin/xctrace
fi
if [[ -n "$xctrace_path" ]] && ! "$xctrace_path" version >/dev/null 2>&1; then
  xctrace_path=""
fi

use_xctrace=0
if [[ "$xctrace_mode" == "1" || "$xctrace_mode" == "required" ]]; then
  if [[ -z "$xctrace_path" ]]; then
    print -u2 "필수 xctrace를 찾지 못했습니다. 전체 Xcode를 설치하거나 NINJA_PERF_XCTRACE=0으로 저오버헤드 패스를 실행하세요."
    exit 1
  fi
  use_xctrace=1
elif [[ "$xctrace_mode" == "auto" && -n "$xctrace_path" ]]; then
  use_xctrace=1
fi

started_at_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
run_stamp="$(date -u +%Y%m%dT%H%M%SZ)"
output_root="${output_root:A}"
run_directory="$output_root/${run_stamp}-${scenario}-pid${player_pid}"
if [[ -e "$run_directory" ]]; then
  print -u2 "기존 성능 증거를 덮어쓰지 않습니다: $run_directory"
  exit 1
fi
mkdir -p "$run_directory"
samples_csv="$run_directory/samples.csv"
metadata_json="$run_directory/metadata.json"
trace_path="$run_directory/game-performance.trace"
trace_export_directory="$run_directory/xctrace-export"

player_log=""
for log_candidate in \
  "$explicit_player_log" \
  "$HOME/Library/Logs/MadCamp/Ninja Adventure/Player.log" \
  "$HOME/Library/Logs/MadCamp3/Ninja Adventure/Player.log" \
  "$HOME/Library/Logs/Unity/Player.log"; do
  if [[ -n "$log_candidate" && -f "$log_candidate" ]]; then
    player_log="${log_candidate:A}"
    break
  fi
done
player_log_start_size=0
if [[ -n "$player_log" ]]; then
  player_log_start_size="$(stat -f %z "$player_log" 2>/dev/null || print 0)"
fi

trace_process_pid=""
notification_wait_pid=""
stack_sample_pid=""
cleanup() {
  for helper_pid in "$notification_wait_pid" "$trace_process_pid" "$stack_sample_pid"; do
    if [[ -n "$helper_pid" ]] && kill -0 "$helper_pid" 2>/dev/null; then
      kill "$helper_pid" 2>/dev/null || true
      wait "$helper_pid" 2>/dev/null || true
    fi
  done
}
trap cleanup INT TERM EXIT

trace_recorded=0
if [[ "$use_xctrace" == "1" ]]; then
  notification_key="com.madcamp.ninjaadventure.performance.${player_pid}.${run_stamp}"
  /usr/bin/notifyutil -1 "$notification_key" >"$run_directory/xctrace-notification.log" 2>&1 &
  notification_wait_pid=$!
  trace_limit_seconds=$((duration_seconds + 2))
  "$xctrace_path" record --quiet --no-prompt \
    --template "Game Performance" \
    --attach "$player_pid" \
    --time-limit "${trace_limit_seconds}s" \
    --notify-tracing-started "$notification_key" \
    --output "$trace_path" >"$run_directory/xctrace.log" 2>&1 &
  trace_process_pid=$!

  trace_started=0
  for _ in {1..150}; do
    if ! kill -0 "$trace_process_pid" 2>/dev/null; then
      break
    fi
    if ! kill -0 "$notification_wait_pid" 2>/dev/null; then
      trace_started=1
      break
    fi
    sleep 0.1
  done
  if [[ "$trace_started" != "1" ]]; then
    if [[ -n "$notification_wait_pid" ]] && kill -0 "$notification_wait_pid" 2>/dev/null; then
      kill "$notification_wait_pid" 2>/dev/null || true
      wait "$notification_wait_pid" 2>/dev/null || true
    fi
    notification_wait_pid=""
    if [[ "$xctrace_mode" == "required" || "$xctrace_mode" == "1" ]]; then
      print -u2 "xctrace가 15초 안에 시작되지 않았습니다. $run_directory/xctrace.log를 확인하세요."
      exit 1
    fi
    if [[ -n "$trace_process_pid" ]] && kill -0 "$trace_process_pid" 2>/dev/null; then
      kill "$trace_process_pid" 2>/dev/null || true
      wait "$trace_process_pid" 2>/dev/null || true
    fi
    trace_process_pid=""
    use_xctrace=0
    print -u2 "주의: xctrace를 시작하지 못해 ps/ioreg 저오버헤드 측정만 계속합니다."
  else
    wait "$notification_wait_pid" 2>/dev/null || true
    notification_wait_pid=""
    trace_recorded=1
  fi
fi

if [[ "$capture_stacks" == "1" ]]; then
  stack_seconds=$((duration_seconds < 10 ? duration_seconds : 10))
  /usr/bin/sample "$player_pid" "$stack_seconds" 5 -file "$run_directory/stack-sample.txt" \
    >"$run_directory/stack-sample.log" 2>&1 &
  stack_sample_pid=$!
fi

if command -v pmset >/dev/null 2>&1; then
  pmset -g therm >"$run_directory/thermal-start.txt" 2>&1 || true
fi

print "timestamp_utc,elapsed_seconds,pid,ps_cpu_percent,interval_cpu_percent,rss_kib,vsz_kib,threads,system_gpu_device_percent,system_gpu_renderer_percent,system_gpu_tiler_percent,process_cpu_time_seconds" >"$samples_csv"

cpu_seconds_from_ps_time() {
  awk -v raw="$1" 'BEGIN {
    days = 0;
    if (index(raw, "-") > 0) {
      split(raw, day_parts, "-");
      days = day_parts[1] + 0;
      raw = day_parts[2];
    }
    count = split(raw, parts, ":");
    if (count == 3) {
      seconds = parts[1] * 3600 + parts[2] * 60 + parts[3];
    } else if (count == 2) {
      seconds = parts[1] * 60 + parts[2];
    } else {
      seconds = parts[1] + 0;
    }
    printf "%.6f", days * 86400 + seconds;
  }'
}

gpu_stat_max() {
  local raw="$1"
  local key="$2"
  local value
  value="$(print -r -- "$raw" | sed -nE "s/.*\"${key}\"=([0-9]+).*/\\1/p" | sort -nr | head -1)"
  print -r -- "$value"
}

interval_seconds="$(awk -v milliseconds="$interval_milliseconds" 'BEGIN { printf "%.3f", milliseconds / 1000 }')"
player_log_start_inode=""
if [[ -n "$player_log" && -f "$player_log" ]]; then
  player_log_start_size="$(stat -f %z "$player_log" 2>/dev/null || print 0)"
  player_log_start_inode="$(stat -f %i "$player_log" 2>/dev/null || true)"
fi
sampling_started_at_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
start_epoch="$EPOCHREALTIME"
previous_wall=""
previous_cpu=""
process_exited=0

while true; do
  now_epoch="$EPOCHREALTIME"
  elapsed="$(awk -v now="$now_epoch" -v start="$start_epoch" 'BEGIN { printf "%.3f", now - start }')"
  current_command="$(ps -p "$player_pid" -o comm= 2>/dev/null | sed 's/^[[:space:]]*//;s/[[:space:]]*$//' || true)"
  if [[ -n "$current_command" ]]; then
    current_command="${current_command:A}"
  fi
  if [[ "$current_command" != "$game_executable" ]]; then
    process_exited=1
    print -u2 "측정 중 Player가 종료됐거나 PID가 다른 프로세스에 재사용됐습니다: PID $player_pid"
    break
  fi
  ps_values="$(ps -p "$player_pid" -o %cpu=,rss=,vsz=,time= 2>/dev/null | awk 'NF >= 4 { print $1, $2, $3, $4; exit }')"
  if [[ -z "$ps_values" ]]; then
    process_exited=1
    print -u2 "측정 중 Player가 종료됐습니다: PID $player_pid"
    break
  fi
  read -r ps_cpu rss_kib vsz_kib ps_cpu_time <<<"$ps_values"
  cpu_seconds="$(cpu_seconds_from_ps_time "$ps_cpu_time")"
  interval_cpu=""
  if [[ -n "$previous_wall" && -n "$previous_cpu" ]]; then
    interval_cpu="$(awk -v now="$now_epoch" -v previous_wall="$previous_wall" \
      -v cpu="$cpu_seconds" -v previous_cpu="$previous_cpu" 'BEGIN {
        wall_delta = now - previous_wall;
        if (wall_delta > 0) printf "%.3f", ((cpu - previous_cpu) / wall_delta) * 100;
      }')"
  fi
  previous_wall="$now_epoch"
  previous_cpu="$cpu_seconds"
  threads="$(ps -M -p "$player_pid" 2>/dev/null | awk 'NR > 1 { count++ } END { print count + 0 }')"
  gpu_raw="$(ioreg -l -w0 -r -c IOAccelerator 2>/dev/null | grep -F '"PerformanceStatistics"' || true)"
  gpu_device="$(gpu_stat_max "$gpu_raw" "Device Utilization %")"
  gpu_renderer="$(gpu_stat_max "$gpu_raw" "Renderer Utilization %")"
  gpu_tiler="$(gpu_stat_max "$gpu_raw" "Tiler Utilization %")"
  timestamp_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  print -r -- "$timestamp_utc,$elapsed,$player_pid,$ps_cpu,$interval_cpu,$rss_kib,$vsz_kib,$threads,$gpu_device,$gpu_renderer,$gpu_tiler,$cpu_seconds" >>"$samples_csv"

  if awk -v elapsed="$elapsed" -v duration="$duration_seconds" 'BEGIN { exit !(elapsed >= duration) }'; then
    break
  fi
  sleep "$interval_seconds"
done

player_log_end_size=0
player_log_end_inode=""
if [[ -n "$player_log" && -f "$player_log" ]]; then
  player_log_end_size="$(stat -f %z "$player_log" 2>/dev/null || print 0)"
  player_log_end_inode="$(stat -f %i "$player_log" 2>/dev/null || true)"
fi

if command -v pmset >/dev/null 2>&1; then
  pmset -g therm >"$run_directory/thermal-end.txt" 2>&1 || true
fi

if [[ -n "$stack_sample_pid" ]]; then
  wait "$stack_sample_pid" 2>/dev/null || true
  stack_sample_pid=""
fi

if [[ "$trace_recorded" == "1" && -n "$trace_process_pid" ]]; then
  if ! wait "$trace_process_pid"; then
    trace_recorded=0
    if [[ "$xctrace_mode" == "required" || "$xctrace_mode" == "1" ]]; then
      print -u2 "xctrace 기록에 실패했습니다. $run_directory/xctrace.log를 확인하세요."
      trace_process_pid=""
      exit 1
    fi
  fi
  trace_process_pid=""
fi

player_log_captured=0
player_log_window_exact=0
runtime_error_count=0
metal_hud_log_line_count=0
if [[ -n "$player_log" && -f "$player_log" ]]; then
  if [[ -n "$player_log_start_inode" && "$player_log_start_inode" == "$player_log_end_inode" &&
        "$player_log_end_size" == <-> && "$player_log_start_size" == <-> &&
        "$player_log_end_size" -ge "$player_log_start_size" ]]; then
    player_log_window_bytes=$((player_log_end_size - player_log_start_size))
    if dd if="$player_log" bs=1 skip="$player_log_start_size" count="$player_log_window_bytes" \
        of="$run_directory/player-log-window.txt" 2>/dev/null; then
      captured_log_size="$(stat -f %z "$run_directory/player-log-window.txt" 2>/dev/null || print -1)"
      if [[ "$captured_log_size" == "$player_log_window_bytes" ]]; then
        player_log_window_exact=1
      fi
    fi
  else
    cp "$player_log" "$run_directory/player-log-window.txt"
  fi
  if [[ -f "$run_directory/player-log-window.txt" ]]; then
    player_log_captured=1
    runtime_error_count="$(grep -Eic '(^|[^A-Za-z])(error|exception|assert)([^A-Za-z]|$)' "$run_directory/player-log-window.txt" || true)"
    metal_hud_log_line_count="$(grep -Fc 'metal-HUD:' "$run_directory/player-log-window.txt" || true)"
  fi
fi

if [[ "$trace_recorded" == "1" && -d "$trace_path" ]]; then
  mkdir -p "$trace_export_directory"
  "$xctrace_path" export --input "$trace_path" --toc --output "$trace_export_directory/toc.xml" >/dev/null
  if ! grep -Eq "<process type=\"attached\"[^>]*pid=\"$player_pid\"" "$trace_export_directory/toc.xml"; then
    print -u2 "xctrace 대상 PID가 요청한 Player와 일치하지 않습니다: $player_pid"
    exit 1
  fi
  for schema in ca-client-present-request metal-gpu-intervals metal-current-allocated-size potential-hangs device-thermal-state-intervals; do
    "$xctrace_path" export --input "$trace_path" \
      --xpath "/trace-toc/run[@number='1']/data/table[@schema='$schema']" \
      --output "$trace_export_directory/$schema.xml" >/dev/null
  done
fi

hardware_model="$(sysctl -n hw.model 2>/dev/null || uname -m)"
hardware_memory_bytes="$(sysctl -n hw.memsize 2>/dev/null || print 0)"
os_version="$(sw_vers -productVersion 2>/dev/null || uname -r)"
process_started="$(ps -p "$player_pid" -o lstart= 2>/dev/null | sed 's/^[[:space:]]*//' || true)"

METADATA_PATH="$metadata_json" \
META_SCENARIO="$scenario" \
META_STARTED_AT="$started_at_utc" \
META_SAMPLING_STARTED_AT="$sampling_started_at_utc" \
META_APP_PATH="$app_path" \
META_EXECUTABLE_PATH="$game_executable" \
META_BUNDLE_ID="$bundle_identifier" \
META_PID="$player_pid" \
META_DURATION="$duration_seconds" \
META_INTERVAL="$interval_milliseconds" \
META_TARGET_FPS="$target_fps" \
META_NOTES="$notes" \
META_XCTRACE_RECORDED="$trace_recorded" \
META_XCTRACE_MODE="$xctrace_mode" \
META_METAL_HUD_SCHEMA="$metal_hud_schema" \
META_METAL_HUD_LOG_LINES="$metal_hud_log_line_count" \
META_STACKS="$capture_stacks" \
META_PLAYER_LOG="$player_log" \
META_PLAYER_LOG_CAPTURED="$player_log_captured" \
META_PLAYER_LOG_WINDOW_EXACT="$player_log_window_exact" \
META_RUNTIME_ERRORS="$runtime_error_count" \
META_PROCESS_STARTED="$process_started" \
META_HARDWARE_MODEL="$hardware_model" \
META_HARDWARE_MEMORY="$hardware_memory_bytes" \
META_OS_VERSION="$os_version" \
node --input-type=module -e '
  import fs from "node:fs";
  const env = process.env;
  const metadata = {
    scenario: env.META_SCENARIO,
    startedAtUtc: env.META_STARTED_AT,
    samplingStartedAtUtc: env.META_SAMPLING_STARTED_AT,
    appPath: env.META_APP_PATH,
    executablePath: env.META_EXECUTABLE_PATH,
    bundleIdentifier: env.META_BUNDLE_ID,
    pid: Number(env.META_PID),
    requestedDurationSeconds: Number(env.META_DURATION),
    intervalMilliseconds: Number(env.META_INTERVAL),
    targetFps: Number(env.META_TARGET_FPS),
    notes: env.META_NOTES,
    xctraceRecorded: env.META_XCTRACE_RECORDED === "1",
    xctraceMode: env.META_XCTRACE_MODE,
    metalHudSchema: env.META_METAL_HUD_SCHEMA,
    metalHudLogLineCount: Number(env.META_METAL_HUD_LOG_LINES),
    stackSampleCaptured: env.META_STACKS === "1",
    playerLogPath: env.META_PLAYER_LOG,
    playerLogCaptured: env.META_PLAYER_LOG_CAPTURED === "1",
    playerLogWindowExact: env.META_PLAYER_LOG_WINDOW_EXACT === "1",
    runtimeErrorCount: Number(env.META_RUNTIME_ERRORS),
    processStarted: env.META_PROCESS_STARTED,
    hardwareModel: env.META_HARDWARE_MODEL,
    hardwareMemoryBytes: Number(env.META_HARDWARE_MEMORY),
    osVersion: env.META_OS_VERSION,
  };
  fs.writeFileSync(env.METADATA_PATH, `${JSON.stringify(metadata, null, 2)}\n`);
'

summary_arguments=(
  --metadata "$metadata_json"
  --samples "$samples_csv"
  --output "$run_directory"
  --metal-hud-schema "$metal_hud_schema"
)
if [[ "$player_log_captured" == "1" && "$player_log_window_exact" == "1" ]]; then
  summary_arguments+=(--metal-hud-log "$run_directory/player-log-window.txt")
fi
if [[ "$trace_recorded" == "1" && -d "$trace_export_directory" ]]; then
  summary_arguments+=(--trace-export "$trace_export_directory")
fi
node "$summary_script" "${summary_arguments[@]}"
node "$summary_script" --index "$output_root"

trap - INT TERM EXIT
print "성능 측정 완료: $run_directory"
print "요약: $run_directory/summary.md"
print "전체 인덱스: $output_root/Performance-Index.md"
if [[ "$process_exited" == "1" ]]; then
  exit 1
fi
