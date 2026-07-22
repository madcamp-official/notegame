# Ninja Adventure macOS 실행 안내

## 필요한 환경

- macOS 12 이상
- Intel 64-bit 또는 Apple silicon Mac
- Node.js 20 이상
- Docker Desktop와 Docker Compose v2
- 최초 PostgreSQL 이미지 다운로드를 위한 네트워크 연결

게임 서버의 고정된 production Node 패키지는 실행 번들에 포함되어 있습니다.
API 키나 PostgreSQL 비밀번호는 번들에 포함되지 않습니다.

## 실행

1. Docker Desktop을 시작하고 엔진 준비가 끝날 때까지 기다립니다.
2. 최상위의 `Ninja Adventure.command`를 Finder에서 더블 클릭합니다.
3. 처음 실행하면 PostgreSQL 16 이미지와 스키마를 준비한 뒤 게임이 열립니다.

런처는 PostgreSQL schema version 012와 게임 서버 health 계약을 확인한 뒤에만 앱을
엽니다. 준비에 실패하면 Terminal에 원인과 복구 방법을 표시하고 memory 저장소로
조용히 전환하지 않습니다.

## 저장 위치

- 접속 설정: `~/.ninja-adventure/postgres.env`
- 비밀번호: `~/.ninja-adventure/postgres-password`
- 게임 데이터: Docker volume `ninja_adventure_release_postgres_data`
- 서버 로그: 실행 번들의 `Logs/NinjaAdventureServer.log`

게임을 종료해도 PostgreSQL volume은 유지됩니다. 해당 volume을 삭제하면 저장도
삭제되므로 백업 없이 제거하지 마세요. 기본 DB 포트 `55433`이 사용 중이면 최초
실행 전에 Terminal에서 다음과 같이 빈 포트를 지정합니다.

```bash
cd "/NinjaAdventure-1.0.0이 있는 폴더"
NINJA_POSTGRES_PORT=55434 "./NinjaAdventure-1.0.0/Ninja Adventure.command"
```

## 이미 운영 중인 서버 사용

게임 서버 전체를 원격으로 운영한다면 다음처럼 health endpoint가 있는 HTTPS 주소를
지정합니다.

```bash
KW_GAME_SERVER_URL=https://game.example.com \
  "./NinjaAdventure-1.0.0/Ninja Adventure.command"
```

로컬 Node 서버와 별도 PostgreSQL을 조합하려면 `NINJA_DATABASE_URL`을 지정하고,
TLS가 필요한 DB에는 `NINJA_DATABASE_SSL=true`를 함께 설정합니다.

```bash
NINJA_DATABASE_URL='postgresql://USER:PASSWORD@HOST:5432/DATABASE' \
NINJA_DATABASE_SSL=true \
  "./NinjaAdventure-1.0.0/Ninja Adventure.command"
```

`NINJA_ALLOW_EPHEMERAL_STORAGE=1`은 서버 종료 후 저장 복구를 검증하지 않는 로컬
QA 전용입니다. 일반 플레이나 출시 운영에는 사용하지 마세요.

## 문제 해결

- Docker 오류: Docker Desktop을 시작하고 `docker info`와 `docker compose version`을
  확인합니다.
- 서버 준비 실패: `Logs/NinjaAdventureServer.log`의 마지막 오류를 확인합니다.
- 기존 memory 서버 거부: 해당 서버를 종료하거나 PostgreSQL 서버 주소를 설정합니다.
- 기존 volume과 접속 설정 불일치: 데이터 보호를 위해 새 비밀번호를 만들지 않으므로
  `~/.ninja-adventure/postgres.env` 백업을 복원합니다.
