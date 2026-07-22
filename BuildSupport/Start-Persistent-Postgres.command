#!/bin/zsh
set -euo pipefail

script_dir="${0:A:h}"
project_root="${script_dir:h}"
compose_file="$script_dir/docker-compose.release.yml"
state_dir="${NINJA_RELEASE_STATE_DIR:-$HOME/.ninja-adventure}"
if [[ "$state_dir" != /* || "$state_dir" == "/" ]]; then
  print -u2 "NINJA_RELEASE_STATE_DIR은 루트가 아닌 절대 경로여야 합니다: $state_dir"
  exit 1
fi
state_dir="${state_dir:A}"
env_file="${NINJA_RELEASE_DB_ENV_FILE:-$state_dir/postgres.env}"
if [[ "$env_file" != /* ]]; then
  print -u2 "NINJA_RELEASE_DB_ENV_FILE은 절대 경로여야 합니다: $env_file"
  exit 1
fi
env_file="${env_file:A}"
password_file="$state_dir/postgres-password"
volume_name="ninja_adventure_release_postgres_data"
project_name="ninja-adventure-release"

for required in docker mktemp openssl sed stat tr; do
  if ! command -v "$required" >/dev/null 2>&1; then
    print -u2 "영구 저장소 준비 도구를 찾지 못했습니다: $required"
    exit 1
  fi
done
if ! docker compose version >/dev/null 2>&1; then
  print -u2 "Docker Compose v2가 필요합니다. Docker Desktop을 설치하고 실행하세요."
  exit 1
fi
if ! docker info >/dev/null 2>&1; then
  print -u2 "Docker 엔진에 연결할 수 없습니다. Docker Desktop을 시작한 뒤 다시 실행하세요."
  exit 1
fi
if [[ ! -f "$compose_file" ]]; then
  print -u2 "PostgreSQL Compose 구성이 없습니다: $compose_file"
  exit 1
fi

mkdir -p "$state_dir"
chmod 700 "$state_dir"
if [[ "$(stat -f '%Lp' "$state_dir")" != "700" ]]; then
  print -u2 "영구 저장소 설정 디렉터리 권한을 0700으로 제한하지 못했습니다: $state_dir"
  exit 1
fi
mkdir -p "${env_file:h}"
if [[ ! -f "$env_file" ]]; then
  if docker volume inspect "$volume_name" >/dev/null 2>&1; then
    print -u2 "기존 저장 volume은 있지만 접속 설정이 없습니다: $volume_name"
    print -u2 "데이터를 보호하기 위해 새 비밀번호를 만들지 않았습니다. $env_file 백업을 복원하세요."
    exit 1
  fi
  configured_port="${NINJA_POSTGRES_PORT:-55433}"
  if [[ "$configured_port" != <-> || "$configured_port" -lt 1 || "$configured_port" -gt 65535 ]]; then
    print -u2 "NINJA_POSTGRES_PORT는 1~65535 범위의 정수여야 합니다: $configured_port"
    exit 1
  fi
  temporary_env="$(mktemp "$state_dir/postgres.env.XXXXXX")"
  temporary_password="$(mktemp "$state_dir/postgres-password.XXXXXX")"
  cleanup_partial_config() {
    [[ -f "${temporary_env:-}" ]] && rm -f "$temporary_env"
    [[ -f "${temporary_password:-}" ]] && rm -f "$temporary_password"
  }
  trap cleanup_partial_config EXIT INT TERM
  openssl rand -hex 24 >"$temporary_password"
  chmod 600 "$temporary_password"
  {
    print -r -- "NINJA_POSTGRES_USER=keyboard_wanderer"
    print -r -- "NINJA_POSTGRES_DB=ninja_adventure"
    print -r -- "NINJA_POSTGRES_PORT=$configured_port"
    print -r -- "NINJA_POSTGRES_PASSWORD_FILE=$password_file"
  } >"$temporary_env"
  chmod 600 "$temporary_env"
  mv "$temporary_password" "$password_file"
  mv "$temporary_env" "$env_file"
  temporary_env=""
  temporary_password=""
  trap - EXIT INT TERM
fi

chmod 600 "$env_file"
read_env_value() {
  local key="$1"
  sed -n "s/^${key}=//p" "$env_file" | tail -1
}

postgres_user="$(read_env_value NINJA_POSTGRES_USER)"
postgres_database="$(read_env_value NINJA_POSTGRES_DB)"
postgres_port="$(read_env_value NINJA_POSTGRES_PORT)"
configured_password_file="$(read_env_value NINJA_POSTGRES_PASSWORD_FILE)"
identifier_pattern='^[A-Za-z_][A-Za-z0-9_]*$'
if [[ ! "$postgres_user" =~ $identifier_pattern ||
      ! "$postgres_database" =~ $identifier_pattern ||
      "$postgres_port" != <-> || "$postgres_port" -lt 1 || "$postgres_port" -gt 65535 ||
      "$configured_password_file" != "$state_dir/"* || ! -s "$configured_password_file" ]]; then
  print -u2 "영구 저장소 설정 형식이 올바르지 않습니다: $env_file"
  exit 1
fi
postgres_password="$(tr -d '\r\n' <"$configured_password_file")"
password_pattern='^[0-9a-f]{48}$'
if [[ ! "$postgres_password" =~ $password_pattern ]]; then
  print -u2 "PostgreSQL 비밀번호는 48자리 hexadecimal 값이어야 합니다."
  exit 1
fi
chmod 600 "$configured_password_file"
if [[ "$(stat -f '%Lp' "$env_file")" != "600" ||
      "$(stat -f '%Lp' "$configured_password_file")" != "600" ]]; then
  print -u2 "PostgreSQL 접속 설정과 비밀번호 권한을 0600으로 제한하지 못했습니다."
  exit 1
fi

compose_command=(
  env
  -u NINJA_POSTGRES_USER
  -u NINJA_POSTGRES_DB
  -u NINJA_POSTGRES_PORT
  -u NINJA_POSTGRES_PASSWORD_FILE
  docker compose
  --project-name "$project_name"
  --env-file "$env_file"
  --file "$compose_file"
)
"${compose_command[@]}" config --quiet
if ! "${compose_command[@]}" up --detach --wait --wait-timeout 180 postgres; then
  print -u2 "로컬 PostgreSQL이 준비되지 않았습니다. 최근 컨테이너 로그를 확인하세요."
  "${compose_command[@]}" logs --no-color --tail 100 postgres >&2 || true
  exit 1
fi

print "Ninja Adventure 영구 저장소 준비 완료: PostgreSQL 127.0.0.1:$postgres_port"
print "설정: $env_file"
