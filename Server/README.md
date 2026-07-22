# 《Ninja Adventure》 authoritative server

이 서버는 코드리아 v4 캠페인의 규칙 권위를 소유합니다. LLM은 제한된 player-action·장면 제안과 확정 판정의 짧은 서사만 만들며, 모든 제안은 스키마·의미·규칙 검증을 통과해야 합니다. 월드 geometry, 이동, D20, 비용, 관리자 권한, 기술 부채와 결말 ID는 LLM이 바꾸지 못합니다.

## 고정 제품 계약

- 게임: `Ninja Adventure`
- 세계: `WORLD_CODRIA` / 코드리아
- 주인공: `PROTAGONIST_NUPJUKYI` / 넙죽이
- 유물: `ARTIFACT_ADMIN_KEYBOARD` / 관리자 키보드
- 저장되는 입력: `MOVE`, `USE_SKILL`, `NARRATIVE_CHOICE`
- 관리자 키보드 스킬: `COPY`, `DELETE`, `CONNECT`, `RESTORE`, `UNDO`, `SEARCH`, `SELECT_ALL`
- 행동 문맥: `COMBAT`, `INVESTIGATION`, `NEGOTIATION`, `DEPLOYMENT`
- 권한: `ADMIN_ACCESS_LEVEL_1`, `ADMIN_ACCESS_LEVEL_2`, `ADMIN_ACCESS_LEVEL_3`
- 캠페인 길이: 30–50 의미 턴, 기본 40턴

Seed는 위 항목을 바꾸지 않습니다. 여섯 지역 축의 실제 area·terrain biome·POI 배치, 경로, NPC 이름과 역할, 사건, 퀘스트, 권한 획득 후보 및 에필로그 요소만 바꿉니다.

## 월드와 지역

160×160 월드는 런 시작 시 한 번 생성·검증되고 `layoutHash`로 봉인됩니다. 턴, 저장·재개, Restore와 Undo는 geometry를 재생성하지 않습니다.

고정 지역 축은 물리 바이옴과 다른 레이어입니다.

1. `REGION_BUG_FOREST`
2. `REGION_BUFFER_VILLAGE`
3. `REGION_DEADLOCK_CITY`
4. `REGION_DATA_GRAND_LIBRARY`
5. `REGION_LEGACY_CITADEL`
6. `REGION_ROOT_SYSTEM`

월드 전체에는 다음 여섯 물리 바이옴이 모두 존재합니다.

- `temperate_forest_field`
- `river_wetland`
- `arid_desert`
- `frost_highland`
- `subterranean_cavern`
- `ancient_ruins`

각 관리자 권한은 서로 다른 area와 행동 문맥에 걸친 후보를 최소 두 개 갖습니다. 루트 시스템은 세 권한과 데이터 대도서관의 필수 붕괴 원인 단서가 모두 있어야 이동할 수 있습니다.

## 턴 계약

`MOVE`는 안전 이동입니다. D20과 캠페인 턴을 소비하지 않고 run version만 증가시킵니다. 좌표 커밋과 함께 제공되는 장면은 서버 후보 목록에서 결정론적으로 생성되므로 원격 LLM 응답을 기다리지 않습니다. 위험 구간에서는 안전 지점까지만 이동하고 encounter를 활성화합니다. 실제 `USE_SKILL` 행동이 D20과 의미 턴을 소비합니다.

```json
{
  "inputType": "MOVE",
  "destination": { "areaId": "area-id", "x": 18, "y": 27 },
  "expectedRunVersion": 4,
  "idempotencyKey": "move-00000001"
}
```

```json
{
  "inputType": "USE_SKILL",
  "skillId": "CONNECT",
  "targetIds": ["entity-uuid-1", "entity-uuid-2"],
  "expectedRunVersion": 5,
  "idempotencyKey": "skill-00000001"
}
```

`playerNote`는 구조화 명령에 붙는 선택적 분위기 메모입니다. 입력하지 않아도 전체 캠페인을 완료할 수 있고, 입력해도 합법성·난이도·효과를 직접 바꾸지 않습니다. `/travel`은 기존 HTTP 클라이언트의 `TRAVEL` 표기를 `MOVE` 호환 별칭으로만 허용합니다.

## 선택지와 자유 입력

서버가 봉인한 선택지는 `/v1/runs/:id/choices`로 제출합니다. 대화·태도 선택은 D20 없이 `NARRATIVE_CHOICE`로, 스킬 선택은 검증된 `USE_SKILL`로 커밋됩니다.

자유 입력은 `/v1/runs/:id/messages`로 제출합니다. 서버는 문장 전체와 현재 위치, 8칸 안의 가시 엔티티, 소유 아이템과 가능한 목적지만 모델 또는 결정적 의미 분석기에 제공해 다음 제한된 제안 중 하나로 분류합니다.

- 비판정: `DIALOGUE`, `ATTITUDE`
- 일반 판정 행동: `ACQUIRE`, `ATTACK`, `MOVE`, `INTERACT`, `NEGOTIATE`, `REST`, `USE_ITEM`, `COMBINE`
- 관리자 키보드: `COPY`, `DELETE`, `CONNECT`, `RESTORE`, `UNDO`, `SEARCH`, `SELECT_ALL`

자유 입력 텍스트나 모델 제안은 권위 상태가 아닙니다. Rule Engine이 가시성, 거리, 점유, 소유권, 목적지, 대상 수, D20과 비용을 다시 검증한 뒤 비판정 입력은 `NARRATIVE_CHOICE`, 판정 행동은 `USE_SKILL` envelope로 정규화해 저장합니다. 합법 경계에서 거절된 시도는 요청한 기계 효과 없이 거절 이유를 담은 `NARRATIVE_CHOICE` 한 턴으로 기록하고 플레이어에게 구체적인 이유와 대안을 돌려줍니다. 모델 타임아웃, 잘못된 JSON 또는 미설정 상태에는 동일한 경계를 지키는 결정적 폴백을 사용합니다.

```json
{
  "text": "가까운 안전한 방향으로 한 걸음 이동한다",
  "idempotencyKey": "message-00000001",
  "expectedRunVersion": 4
}
```

`text`는 1~1000자, `idempotencyKey`는 8~128자의 안전한 문자여야 합니다. 판정 UI가 먼저 서버에서 받은 값을 표시하는 경우에만 선택적 `preparedD20`(1~20)을 함께 보낼 수 있으며, 서버가 같은 run version에 준비한 값과 다르면 거절합니다.

## 캠페인 골격

모든 런은 다음 9개 거시 비트를 유지합니다.

1. 코드리아 추락과 관리자 키보드 각성
2. 붕괴 현상과 첫 지역 문제
3. 관리자 권한 I
4. 관리자 권한 II
5. 관리자 통제 시스템 내부 원인 확인
6. 기술 부채와 과거 선택의 역류
7. 관리자 권한 III
8. 루트 시스템 진입
9. 최종 배치와 결말

서버는 `majorChoices`, `regionOutcomes`, `npcRelationships`, `canonicalFacts`, `unresolvedHooks`, `abilityUsageHistory`, `adminAccessAcquisitionHistory`, `technicalDebtEntries`를 권위 상태로 유지합니다. 일반 성공은 기술 부채를 자동 감소시키지 않습니다. 명시적인 복구 또는 보상 행동만 기존 원장을 해결합니다.

## LLM 경계

Gemini 요청은 구조화 JSON, 제한된 출력, 최소 thinking level, 최대 1회 repair로 제한합니다. 실패하면 deterministic fallback을 사용하며 게임 상태 commit은 서버 검증 뒤에만 일어납니다.

기본값은 현재 API에서 신규 프로젝트 호출이 가능한 비용 우선 설정인 `gemini-3.1-flash-lite`입니다. 모델 교체는 `GEMINI_FAST_MODEL`과 `GEMINI_QUALITY_MODEL` 환경 변수로만 수행합니다.

- 가격: <https://ai.google.dev/gemini-api/docs/pricing>
- 모델 수명주기: <https://ai.google.dev/gemini-api/docs/deprecations>

Gemini 키는 `GEMINI_API_KEY` 환경 변수에만 둡니다. `.env`, Unity 클라이언트, API 응답, 로그, PostgreSQL plan과 snapshot에 키를 기록하지 않습니다.

## 실행

### 요구 환경

- Node.js 20 이상
- npm 10 이상 권장
- PostgreSQL을 사용할 때 PostgreSQL 15 이상과 `pgcrypto` 설치 권한

### 외부 서비스 없는 로컬 서버

```bash
test -e .env || cp .env.example .env
npm ci
npm run check
npm test
npm start
```

`npm start`는 `Server/.env`를 자동으로 읽습니다. 이미 shell이나 프로세스에 설정된 환경 변수가 같은 이름의 `.env` 값보다 우선합니다.

기본 예시는 `STORAGE=memory`와 빈 `GEMINI_API_KEY`이므로 외부 서비스 없이 시작됩니다. 규칙, 자유 입력 분류와 서술은 결정적 폴백으로 계속 동작하지만 memory 저장은 프로세스 수명에만 존재하므로 서버 재시작 후 런을 이어갈 수 없습니다.

`/public/test-client.html`, `/public/narrative-lab.html`과 run debug API는 개발 도구입니다. 로컬 개발에서만 `NODE_ENV=development`와 `ENABLE_DEBUG_ROUTES=true`를 함께 설정해 명시적으로 활성화합니다. 기본값은 404이며, `NODE_ENV=production`에서는 `ENABLE_DEBUG_ROUTES=true`를 전달해도 항상 404로 닫힙니다.

```bash
curl --fail http://127.0.0.1:8787/health
```

### PostgreSQL 영속 서버

저장소 루트에서 [migration 001~012와 seed](../Database/README.md)를 순서대로 적용한 뒤 `Server/.env`를 다음처럼 바꿉니다.

```dotenv
STORAGE=postgres
DATABASE_URL=postgresql://user:password@127.0.0.1:5432/database
DATABASE_SSL=false
```

`DATABASE_SSL=true`는 인증서 검증을 켠 TLS 연결을 사용합니다. 자체 서명 인증서의 검증을 끄는 옵션은 제공하지 않으므로 운영 인증서는 런타임의 신뢰 저장소에 연결해야 합니다.

### LLM 선택

`LLM_PROVIDER=gemini`에서 `GEMINI_API_KEY`가 비어 있거나 공급자가 실패하면 결정적 폴백을 사용합니다. 기본 fast/quality 모델은 모두 `gemini-3.1-flash-lite`이며 `GEMINI_FAST_MODEL`, `GEMINI_QUALITY_MODEL`로만 교체합니다. 한 플레이어 턴의 모든 분류·계획·검수·서술 호출은 기본 20초/6회라는 하나의 예산을 공유합니다. 예산이 끝나면 진행 중 요청을 취소하고 남은 단계는 즉시 결정적 폴백으로 끝냅니다.

로컬 OpenAI 호환 vLLM을 쓰려면 `LLM_PROVIDER=vllm`, `VLLM_BASE_URL=http://127.0.0.1:8000/v1`, `VLLM_MODEL=game-director`를 설정합니다. URL이 비어 있거나 요청이 실패해도 결정적 폴백으로 진행합니다. 반복 전송 장애에는 cooldown circuit breaker가 열리고, cooldown 뒤에는 단 하나의 half-open 복구 probe만 허용합니다. 모든 공급자의 동시 요청 수도 전역 상한으로 제한됩니다. Gemini와 vLLM 키는 Unity, 응답 payload, DB snapshot 또는 저장소에 넣지 않습니다.

### 환경 변수

| 변수 | 기본값 | 설명 |
| --- | --- | --- |
| `HOST`, `PORT` | `127.0.0.1`, `8787` | HTTP listen 주소와 포트 |
| `NODE_ENV`, `LOG_LEVEL` | `development`, `info` | 실행 환경과 서버 로그 수준 |
| `STORAGE` | `memory` | `memory` 또는 `postgres` |
| `DATABASE_URL` | 빈 값 (`.env.example`은 placeholder) | `STORAGE=postgres`일 때 필수 |
| `DATABASE_SSL` | `false` | `true`면 서버 인증서를 검증하는 TLS 사용 |
| `IDEMPOTENCY_LEASE_MS` | `30000` | leader lease와 heartbeat 갱신 기준(5000~300000ms) |
| `IDEMPOTENCY_WAIT_TIMEOUT_MS` | `25000` | follower가 동일 결과를 기다리는 최대 시간(1000~600000ms) |
| `AUTH_MODE` | `local` | `local` 또는 `required` |
| `DEFAULT_USER_ID` | 고정 로컬 UUID | `AUTH_MODE=local`에서 header가 없을 때 사용할 owner |
| `CORS_ORIGINS` | localhost 개발 origin | 비어 있으면 기본 localhost 목록, 설정하면 이를 대체하는 쉼표 구분 목록 |
| `ENABLE_DEBUG_ROUTES` | `false` | `NODE_ENV=development`에서만 run debug API와 `/public/*` 개발 도구 활성화. 다른 환경에서는 무시 |
| `LLM_PROVIDER` | `gemini` | `gemini` 또는 `vllm` |
| `LLM_TURN_DEADLINE_MS` | `20000` | 한 턴의 전체 LLM 처리 상한(1000~120000ms) |
| `LLM_TURN_MAX_CALLS` | `6` | 한 턴에서 허용하는 실제 공급자 호출 수(1~20) |
| `LLM_MAX_CONCURRENT_REQUESTS` | `2` | 한 서버 프로세스의 공급자 동시 요청 상한(1~16) |
| `GEMINI_API_KEY` | 빈 값 | 비어 있으면 결정적 폴백 사용 |
| `GEMINI_TIMEOUT_MS` | `15000` | 250~15000ms 공급자 timeout |
| `GEMINI_CIRCUIT_COOLDOWN_MS` | `30000` | 연속 공급자 장애 뒤 1000~120000ms 차단 시간 |
| `GEMINI_FAST_MODEL`, `GEMINI_QUALITY_MODEL` | `gemini-3.1-flash-lite` | fast/quality 모델 ID |
| `GEMINI_FAST_OUTPUT_TOKENS`, `GEMINI_QUALITY_OUTPUT_TOKENS` | `1024`, `1536` | 각 모델 출력 상한 |
| `VLLM_BASE_URL`, `VLLM_MODEL` | 빈 값, `game-director` | OpenAI 호환 `/v1` 주소와 모델 ID |
| `VLLM_API_KEY` | 빈 값 | 선택적 Bearer token |
| `VLLM_TIMEOUT_MS` | `8000` | 250~30000ms 공급자 timeout |
| `VLLM_CIRCUIT_COOLDOWN_MS` | `30000` | vLLM 전송 장애 뒤 1000~120000ms 차단 시간 |
| `VLLM_FAST_OUTPUT_TOKENS`, `VLLM_QUALITY_OUTPUT_TOKENS` | `768`, `1024` | vLLM 출력 상한 |
| `LLM_RESPONSE_TRACE` | 개발 환경에서 `true` | Gemini 응답 trace 기록 여부 |
| `LLM_RESPONSE_TRACE_FILE` | `logs/llm-responses.jsonl` | 저장소에서 제외되는 trace 경로 |

허용 범위와 정확한 예시는 [.env.example](.env.example)이 권위입니다.

## 검증

```bash
npm run check
npm test
npm run world:validate
```

`npm test`는 `TEST_DATABASE_URL`이 없으면 PostgreSQL integration test를 건너뜁니다. 전용 test database에 migration 001~012와 seed를 먼저 적용한 뒤 다음처럼 전체 suite를 실행합니다. 개발 데이터나 운영 database를 test 대상으로 사용하지 않습니다.

```bash
TEST_DATABASE_URL=postgresql://user:password@127.0.0.1:5432/test_database npm test
```

## API

| Method | Path | 역할 |
| --- | --- | --- |
| `GET` | `/health` | 제품 계약·storage·model profile |
| `POST` | `/v1/campaigns` | 코드리아 캠페인 preview |
| `GET` | `/v1/campaigns` | owner의 캠페인 목록 |
| `GET` | `/v1/campaigns/:id` | 캠페인 조회 |
| `POST` | `/v1/campaigns/:id/runs` | run 월드 생성·검증·봉인 |
| `GET` | `/v1/runs/:id` | 현재 권위 상태 |
| `POST` | `/v1/runs/:id/travel` | 턴 미소비 `MOVE` |
| `POST` | `/v1/runs/:id/navigation` | `/travel` 호환 경로 |
| `POST` | `/v1/runs/:id/actions` | 턴 소비 `USE_SKILL` |
| `POST` | `/v1/runs/:id/turns` | `/actions` 호환 경로 |
| `POST` | `/v1/runs/:id/choices` | 서버 봉인 선택지 제출 |
| `POST` | `/v1/runs/:id/messages` | 자유 입력 분류·검증·커밋 |
| `POST` | `/v1/runs/:id/dice` | 현재 version의 권위 D20 준비 |
| `GET` | `/v1/runs/:id/turns` | 커밋된 행동 목록 |
| `GET` | `/v1/runs/:id/turns/:turnNo` | 특정 행동 결과 |
| `POST` | `/v1/runs/:id/abandon` | 런 중단 |
| `POST` | `/v1/runs/:id/resume` | checksum·plan hash·layout hash 검증 후 재개 |

`/v1/runs/:id/ambient-wander`는 화면에 보이는 NPC의 제한된 비턴 이동, `/v1/gm/narrate`와 `/v1/gm/scene-transitions`는 Unity 서사 표현용 내부 경로입니다. `/v1/runs/:id/inventory`와 debug 경로는 개발·테스트 지원용이므로 일반 플레이 클라이언트에서 호출하지 않습니다.

모든 `/v1` 요청은 owner별로 격리됩니다. 로컬 모드는 `x-user-id`가 없으면 `DEFAULT_USER_ID`를 사용합니다. 운영은 identity-aware gateway 뒤에서 `AUTH_MODE=required`로 실행하고, 검증된 UUID를 `x-user-id`로 전달해야 합니다. 동일한 `idempotencyKey`와 동일 payload를 재전송하면 기존 결과를 반환하고, 같은 키에 다른 payload를 보내면 `409 IDEMPOTENCY_CONFLICT`로 거절합니다.

캠페인과 run 생성은 이전 클라이언트와 호환되도록 key 없이도 동작합니다. 응답 유실 재시도가 가능한 출시 클라이언트는 `Idempotency-Key` 헤더(또는 같은 값의 body `idempotencyKey`)에 8~128자의 안전한 고유 값을 보내야 합니다. 첫 생성은 `201`과 `fromIdempotencyCache:false`, 동일 요청 재생은 `200`과 `fromIdempotencyCache:true`를 반환하며 PostgreSQL 재시작 뒤에도 최초 resource를 재생합니다. 턴·선택지·자유 입력·이동은 기존 body `idempotencyKey`를 사용하며, 동시 요청도 LLM을 한 번만 실행합니다.

기본 시간 예산은 LLM turn deadline 20초, 동일 요청 follower wait 25초, leader lease 30초 순서입니다. follower는 Unity의 30초 HTTP timeout 전에 안전하게 재시도 응답을 받고, 실행 중 leader는 10초마다 lease를 갱신합니다.

PostgreSQL 시작 검사와 `/health`는 단순 연결 확인이 아니라 migration `012_request_idempotency_and_schema_readiness`, 필수 relation, 애플리케이션 역할 권한을 검사합니다. 누락·구버전·권한 부족이면 pool을 닫고 시작에 실패하거나 readiness가 `503`으로 실패하므로, 정상화되기 전에는 트래픽을 전달하지 않습니다.
