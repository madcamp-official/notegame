#!/bin/zsh
set -euo pipefail

script_dir="${0:A:h}"
project_root="${script_dir:h}"
server_dir="${NINJA_ADVENTURE_SERVER_DIR:-$project_root/Server}"
app_path="${NINJA_ADVENTURE_APP:-$project_root/Builds/NinjaAdventure.app}"

for required in curl plutil sed tr; do
  if ! command -v "$required" >/dev/null 2>&1; then
    print -u2 "필요한 macOS 도구를 찾지 못했습니다: $required"
    exit 1
  fi
done

if [[ ! -d "$app_path" && -d "$project_root/NinjaAdventure.app" ]]; then
  app_path="$project_root/NinjaAdventure.app"
fi
if [[ ! -d "$app_path" && -d "$script_dir/NinjaAdventure.app" ]]; then
  app_path="$script_dir/NinjaAdventure.app"
fi

if [[ ! -d "$app_path/Contents/MacOS" ]]; then
  print -u2 "NinjaAdventure.app을 찾지 못했습니다: $app_path"
  print -u2 "먼저 Unity에서 Keyboard Wanderer > Build macOS Player를 실행하세요."
  read -k 1 "?아무 키나 누르면 종료합니다."
  exit 1
fi

game_executable=""
for candidate in "$app_path/Contents/MacOS/"*(N); do
  if [[ -f "$candidate" && -x "$candidate" ]]; then
    game_executable="$candidate"
    break
  fi
done
if [[ -z "$game_executable" ]]; then
  print -u2 "실행 가능한 macOS 플레이어가 없습니다: $app_path/Contents/MacOS"
  read -k 1 "?아무 키나 누르면 종료합니다."
  exit 1
fi

server_port="${KW_GAME_SERVER_PORT:-8787}"
if [[ "$server_port" != <-> || "$server_port" -lt 1 || "$server_port" -gt 65535 ]]; then
  print -u2 "KW_GAME_SERVER_PORT는 1~65535 범위의 정수여야 합니다: $server_port"
  exit 1
fi
server_url="${KW_GAME_SERVER_URL:-http://127.0.0.1:$server_port}"
while [[ "$server_url" == */ ]]; do
  server_url="${server_url%/}"
done
if [[ "$server_url" != http://* && "$server_url" != https://* ]]; then
  print -u2 "KW_GAME_SERVER_URL은 http:// 또는 https:// 주소여야 합니다: $server_url"
  exit 1
fi
allow_ephemeral_storage="${NINJA_ALLOW_EPHEMERAL_STORAGE:-0}"
if [[ "$allow_ephemeral_storage" != "0" && "$allow_ephemeral_storage" != "1" ]]; then
  print -u2 "NINJA_ALLOW_EPHEMERAL_STORAGE는 0 또는 1이어야 합니다."
  exit 1
fi
started_server_pid=""
active_storage=""
active_schema_version=""

health_contract_ok() {
  local health_payload health_service health_status health_storage health_schema
  health_payload="$(curl --fail --silent --show-error --max-time 2 "$server_url/health" 2>/dev/null)" || return 1
  health_service="$(print -rn -- "$health_payload" | plutil -extract service raw - 2>/dev/null)" || return 1
  health_status="$(print -rn -- "$health_payload" | plutil -extract status raw - 2>/dev/null)" || return 1
  health_storage="$(print -rn -- "$health_payload" | plutil -extract storage raw - 2>/dev/null)" || return 1
  [[ "$health_service" == "codria-v4-game-server" && "$health_status" == "ok" &&
     ( "$health_storage" == "postgres" || "$health_storage" == "memory" ) ]] || return 1
  health_schema="$(print -rn -- "$health_payload" | plutil -extract schemaVersion raw - 2>/dev/null || true)"
  if [[ "$health_storage" == "postgres" &&
        "$health_schema" != "012_request_idempotency_and_schema_readiness" ]]; then
    return 1
  fi
  active_storage="$health_storage"
  active_schema_version="$health_schema"
}

storage_contract_ok() {
  if [[ "$active_storage" == "postgres" ]]; then
    return 0
  fi
  if [[ "$active_storage" == "memory" && "$allow_ephemeral_storage" == "1" ]]; then
    print -u2 "주의: 명시적으로 허용된 임시 memory 저장소를 사용합니다. 서버 종료 후 진행은 복구되지 않습니다."
    return 0
  fi
  print -u2 "영구 저장이 없는 memory 서버는 출시 실행에서 사용할 수 없습니다: $server_url"
  print -u2 "PostgreSQL 서버를 사용하거나, 로컬 QA에만 NINJA_ALLOW_EPHEMERAL_STORAGE=1을 명시하세요."
  return 1
}

read_release_env_value() {
  local file="$1"
  local key="$2"
  sed -n "s/^${key}=//p" "$file" | tail -1
}

cleanup() {
  if [[ -n "$started_server_pid" ]] && kill -0 "$started_server_pid" 2>/dev/null; then
    kill "$started_server_pid" 2>/dev/null || true
    wait "$started_server_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

if health_contract_ok; then
  if ! storage_contract_ok; then
    read -k 1 "?아무 키나 누르면 종료합니다."
    exit 1
  fi
else
  if [[ "$server_url" != "http://127.0.0.1:$server_port" &&
        "$server_url" != "http://localhost:$server_port" ]]; then
    print -u2 "설정한 서버가 응답하지 않습니다: $server_url"
    print -u2 "KW_GAME_SERVER_URL을 확인한 뒤 다시 실행하세요."
    read -k 1 "?아무 키나 누르면 종료합니다."
    exit 1
  fi
  if [[ ! -f "$server_dir/package.json" ]]; then
    print -u2 "로컬 서버를 찾지 못했습니다: $server_dir"
    read -k 1 "?아무 키나 누르면 종료합니다."
    exit 1
  fi
  if ! command -v node >/dev/null 2>&1; then
    print -u2 "Node.js 20 이상이 필요합니다. README의 실행 환경을 확인하세요."
    read -k 1 "?아무 키나 누르면 종료합니다."
    exit 1
  fi
  node_major="$(node -p 'Number(process.versions.node.split(".")[0])')"
  if [[ "$node_major" != <-> || "$node_major" -lt 20 ]]; then
    print -u2 "Node.js 20 이상이 필요합니다. 현재 major version: $node_major"
    read -k 1 "?아무 키나 누르면 종료합니다."
    exit 1
  fi
  if [[ ! -d "$server_dir/node_modules/pg" ]]; then
    print -u2 "서버 의존성이 설치되지 않았습니다."
    print -u2 "Terminal에서 'cd \"$server_dir\" && npm ci'를 한 번 실행하세요."
    read -k 1 "?아무 키나 누르면 종료합니다."
    exit 1
  fi

  server_storage=""
  database_url=""
  server_database_ssl="false"
  if [[ -n "${NINJA_DATABASE_URL:-}" ]]; then
    server_storage="postgres"
    database_url="$NINJA_DATABASE_URL"
    server_database_ssl="${NINJA_DATABASE_SSL:-false}"
  elif [[ "${STORAGE:-}" == "postgres" && -n "${DATABASE_URL:-}" ]]; then
    server_storage="postgres"
    database_url="$DATABASE_URL"
    server_database_ssl="${DATABASE_SSL:-false}"
  elif [[ -f "$server_dir/.env" ]]; then
    local_env_config="$(
      (
        cd "$server_dir"
        env -u STORAGE -u DATABASE_URL node --input-type=module -e '
          import { loadLocalEnv } from "./src/load-env.js";
          loadLocalEnv();
          process.stdout.write(JSON.stringify({
            storage: process.env.STORAGE || "",
            databaseUrl: process.env.DATABASE_URL || "",
            databaseSsl: process.env.DATABASE_SSL || "false"
          }));
        '
      ) 2>/dev/null || true
    )"
    configured_storage="$(print -rn -- "$local_env_config" | plutil -extract storage raw - 2>/dev/null || true)"
    configured_database_url="$(print -rn -- "$local_env_config" | plutil -extract databaseUrl raw - 2>/dev/null || true)"
    configured_database_ssl="$(print -rn -- "$local_env_config" | plutil -extract databaseSsl raw - 2>/dev/null || true)"
    if [[ "$configured_storage" == "postgres" && -n "$configured_database_url" ]]; then
      server_storage="postgres"
      database_url="$configured_database_url"
      server_database_ssl="$configured_database_ssl"
    fi
  fi

  if [[ -z "$server_storage" && "$allow_ephemeral_storage" == "1" ]]; then
    server_storage="memory"
  fi

  if [[ -z "$server_storage" ]]; then
    persistent_helper="$script_dir/Start-Persistent-Postgres.command"
    persistent_env_file="${NINJA_RELEASE_DB_ENV_FILE:-${NINJA_RELEASE_STATE_DIR:-$HOME/.ninja-adventure}/postgres.env}"
    if [[ ! -x "$persistent_helper" ]]; then
      print -u2 "영구 저장 설정이 없습니다. STORAGE=postgres와 DATABASE_URL을 설정하세요."
      print -u2 "원클릭 PostgreSQL 준비 도구도 찾지 못했습니다: $persistent_helper"
      read -k 1 "?아무 키나 누르면 종료합니다."
      exit 1
    fi
    print "영구 저장 설정이 없어 로컬 PostgreSQL 준비를 시작합니다. 최초 실행은 이미지 다운로드로 시간이 걸릴 수 있습니다."
    if ! "$persistent_helper"; then
      print -u2 "영구 저장소 자동 준비에 실패했습니다. Docker Desktop을 확인하거나"
      print -u2 "Server/.env에 STORAGE=postgres와 DATABASE_URL을 설정하세요."
      read -k 1 "?아무 키나 누르면 종료합니다."
      exit 1
    fi
    if [[ ! -f "$persistent_env_file" ]]; then
      print -u2 "PostgreSQL 접속 설정 파일이 생성되지 않았습니다: $persistent_env_file"
      exit 1
    fi
    database_user="$(read_release_env_value "$persistent_env_file" NINJA_POSTGRES_USER)"
    database_name="$(read_release_env_value "$persistent_env_file" NINJA_POSTGRES_DB)"
    database_port="$(read_release_env_value "$persistent_env_file" NINJA_POSTGRES_PORT)"
    database_password_file="$(read_release_env_value "$persistent_env_file" NINJA_POSTGRES_PASSWORD_FILE)"
    if [[ "$database_password_file" != /* || ! -s "$database_password_file" ]]; then
      print -u2 "PostgreSQL 비밀번호 파일이 없거나 비어 있습니다: ${database_password_file:-설정 없음}"
      exit 1
    fi
    database_password="$(tr -d '\r\n' <"$database_password_file")"
    identifier_pattern='^[A-Za-z_][A-Za-z0-9_]*$'
    password_pattern='^[0-9a-f]{48}$'
    if [[ ! "$database_user" =~ $identifier_pattern || ! "$database_name" =~ $identifier_pattern ||
          "$database_port" != <-> || "$database_port" -lt 1 || "$database_port" -gt 65535 ||
          ! "$database_password" =~ $password_pattern ]]; then
      print -u2 "자동 생성된 PostgreSQL 접속 설정이 올바르지 않습니다."
      exit 1
    fi
    server_storage="postgres"
    database_url="postgresql://${database_user}:${database_password}@127.0.0.1:${database_port}/${database_name}"
    server_database_ssl="false"
  fi

  if [[ "$server_storage" == "postgres" ]]; then
    if [[ "$database_url" != postgres://* && "$database_url" != postgresql://* ]]; then
      print -u2 "DATABASE_URL은 postgres:// 또는 postgresql:// 접속 문자열이어야 합니다."
      exit 1
    fi
    if [[ "$server_database_ssl" != "true" && "$server_database_ssl" != "false" ]]; then
      print -u2 "DATABASE_SSL은 true 또는 false여야 합니다: $server_database_ssl"
      exit 1
    fi
  else
    database_url=""
    server_database_ssl="false"
  fi

  mkdir -p "$project_root/Logs"
  print "게임 서버를 시작하고 저장소 연결을 확인하는 중입니다…"
  (
    cd "$server_dir"
    exec env \
      NODE_ENV=production \
      ENABLE_DEBUG_ROUTES=false \
      LLM_RESPONSE_TRACE="${NINJA_LLM_RESPONSE_TRACE:-false}" \
      STORAGE="$server_storage" \
      DATABASE_URL="$database_url" \
      DATABASE_SSL="$server_database_ssl" \
      HOST=127.0.0.1 \
      PORT="$server_port" \
      node src/index.js
  ) >>"$project_root/Logs/NinjaAdventureServer.log" 2>&1 &
  started_server_pid=$!

  server_ready=false
  for attempt_index in {1..30}; do
    if health_contract_ok; then
      server_ready=true
      break
    fi
    if ! kill -0 "$started_server_pid" 2>/dev/null; then
      break
    fi
    sleep 1
  done
  if [[ "$server_ready" != true ]]; then
    print -u2 "로컬 서버가 30초 안에 준비되지 않았습니다."
    print -u2 "로그: $project_root/Logs/NinjaAdventureServer.log"
    read -k 1 "?아무 키나 누르면 종료합니다."
    exit 1
  fi
  if ! storage_contract_ok; then
    read -k 1 "?아무 키나 누르면 종료합니다."
    exit 1
  fi
fi

print "Ninja Adventure를 시작합니다. 서버: $server_url · 저장소: $active_storage"
KW_GAME_SERVER_URL="$server_url" "$game_executable"
