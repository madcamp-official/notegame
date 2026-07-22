# 넙죽이와 붕괴한 코드 왕국 — PostgreSQL

이 디렉터리는 Codria v4 서버의 영속성 기준입니다. PostgreSQL 15+와 `pgcrypto`를 사용하며, 실행 스키마의 유일한 권위는 `Database/migrations/`입니다. 별도 ERD SQL은 설계 참고 자료일 뿐 마이그레이션 위에 실행하지 않습니다.

## 설치와 검증

PostgreSQL 15 이상에서 대상 database를 만든 뒤 저장소 루트에서 `DATABASE_URL`을 설정하고 아래 순서를 유지합니다. migration 역할에는 스키마 생성과 `pgcrypto` extension 설치 권한이 필요합니다.

```bash
export DATABASE_URL='postgresql://user:password@127.0.0.1:5432/database'
```

```bash
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/001_core_schema.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/002_row_security_and_views.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/003_campaign_director_state.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/004_codria_world_and_travel.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/005_generative_run_state.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/006_codria_product_contract.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/007_codria_actions_and_access.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/008_codria_narrative_history.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/009_codria_narrative_choice_turns.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/010_soft_turn_horizon_and_slot_reservations.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/011_player_action_skill_contract.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/012_request_idempotency_and_schema_readiness.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/seeds/001_reference_catalogs.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/tests/smoke.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/tests/codria_v4_contract.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/tests/request_idempotency_contract.sql
```

`001`부터 `012`까지 생략하거나 순서를 바꾸지 않습니다. `010`은 soft turn horizon과 슬롯 예약을, `011`은 자유 입력에서 정규화된 행동까지 포함하는 skill contract를, `012`는 LLM 실행 전 idempotency lease와 fail-closed readiness ledger를 추가합니다. 시드는 재실행 가능하고 세 SQL 검증은 생성 데이터를 롤백합니다. 테스트 SQL을 운영 트래픽이 있는 database에서 실행하지 않습니다.

## 고정 제품 식별자

모든 신규 런은 다음 값을 저장합니다.

- 세계: `WORLD_CODRIA` / 코드리아
- 주인공: `PROTAGONIST_NUPJUKYI` / 넙죽이
- 유물: `ARTIFACT_ADMIN_KEYBOARD` / 관리자 키보드
- 관리자 권한: `ADMIN_ACCESS_LEVEL_1`, `ADMIN_ACCESS_LEVEL_2`, `ADMIN_ACCESS_LEVEL_3`

NinjaAdventure의 `NinjaGreen`은 현재 Unity 표현용 임시 에셋이며 주인공의 제품 정체성을 대체하지 않습니다.

## 월드 계약

런 생성 시 160×160 월드를 한 번 만들고 `layoutHash`와 생성 계획을 봉인합니다. 타일, 영역, 경로, POI, 배치 슬롯과 축 바인딩은 일반 턴, 저장·재개, Restore, Undo에서 바뀌지 않습니다. 이후 플레이는 기존 슬롯의 활성화와 엔티티 이동만 기록합니다.

Codria에는 정확히 여섯 개의 고정 지역 축이 있습니다.

- `REGION_BUG_FOREST`
- `REGION_BUFFER_VILLAGE`
- `REGION_DEADLOCK_CITY`
- `REGION_DATA_GRAND_LIBRARY`
- `REGION_LEGACY_CITADEL`
- `REGION_ROOT_SYSTEM`

지역 축은 서사·진행상의 장소 정체성입니다. 물리 바이옴은 지형·팔레트·이동 규칙이며 별도 차원입니다. 한 월드 전체에 여섯 물리 바이옴이 모두 있어야 하지만, 축과 바이옴을 같은 열거형이나 고정된 일대일 순서로 취급하지 않습니다. `world_region_axis_bindings`는 seed별 축의 실제 영역과 대표 POI를 봉인합니다.

## 입력과 턴

신규 입력은 세 종류입니다.

- `MOVE`: 안전 이동은 D20과 캠페인 턴을 소비하지 않습니다. 위험 목적지는 안전 지점까지 이동한 뒤 조우를 활성화할 수 있습니다. HTTP의 `TRAVEL`은 전송 호환 별칭이며 저장값은 `MOVE`입니다.
- `USE_SKILL`: `COPY`, `DELETE`, `CONNECT`, `RESTORE`, `UNDO`, `SEARCH`, `SELECT_ALL` 중 하나를 사용합니다. 서버가 `COMBAT`, `INVESTIGATION`, `NEGOTIATION`, `DEPLOYMENT` 중 하나로 맥락을 분류하고 정확히 한 의미 턴을 확정합니다.
- `NARRATIVE_CHOICE`: 서버가 봉인한 대사·독백·태도 선택입니다. `skill_id` 없이 빈 대상 목록과 전용 `NARRATIVE` 맥락을 사용해 정확히 한 의미 턴을 확정합니다. 순수 서사 선택에는 D20 `turn_rule_resolutions` 행을 만들지 않습니다.

`playerNote`는 구조화 `MOVE`/`USE_SKILL`에 붙는 선택적 서술 힌트일 뿐입니다. 별도의 `/v1/runs/:id/messages` 자유 입력 경로는 문장을 제한된 행동 제안으로 분류한 뒤 서버가 다시 검증합니다. 대화·태도는 `NARRATIVE_CHOICE`, 판정 행동은 `USE_SKILL`로 저장되며, 자연어 원문이나 LLM 제안이 좌표, 소유권, 합법성, 비용, D20 또는 결과를 직접 확정할 수 없습니다.

자유 입력에서 정규화될 수 있는 판정 행동은 `SEARCH`, `ACQUIRE`(저장 시 `SEARCH`), `ATTACK`, `MOVE`, `INTERACT`, `NEGOTIATE`, `REST`, `USE_ITEM`, `COMBINE`과 일곱 관리자 키보드 행동입니다. 그래서 migration 011 이후 `turn_records`와 `ability_usage_history`는 공개 키보드 스킬 외에 `INTERACT`, `ATTACK`, `MOVE`, `NEGOTIATE`, `REST`, `USE_ITEM`, `COMBINE`도 허용합니다. 이는 새 권위 계층이 아니라 서버 Rule Engine이 검증한 내부 정규형입니다.

## 9개 캠페인 비트와 접근 권한

진행 구조는 정확히 다음 아홉 비트입니다.

1. 도착과 관리자 키보드 각성
2. 첫 붕괴 문제
3. 관리자 권한 I 획득
4. 관리자 권한 II 획득
5. 내부 관리자 통제 시스템이 원인임을 확인
6. 기술 부채의 역류
7. 관리자 권한 III 획득
8. 루트 시스템 진입
9. 최종 배포와 결말

각 권한 단계에는 서로 다른 영역과 서로 다른 행동 맥락에 걸친 획득 후보가 최소 두 개 있어야 합니다. `admin_access_acquisition_history`는 단계별 획득 근거를 순서대로 한 번만 기록합니다. `REGION_ROOT_SYSTEM` 진입은 세 단계 전체와 핵심 단서가 모두 있어야 합니다. 핵심 단서의 DB 기준은 `REGION_DATA_GRAND_LIBRARY / ROOT_CAUSE_ESSENTIAL_CLUE_ACQUIRED / {"acquired":true}`입니다.

## 저장해야 하는 상태

| 관심사 | 주요 객체 |
| --- | --- |
| 소유권 | `profiles`, owner RLS, `campaigns`, `runs` |
| 봉인 월드 | `worlds`, `regions`, `areas`, `area_connections`, `world_area_descriptors`, `world_pois`, `placement_slots` |
| 지역 축 | `campaign_region_axis_catalog`, `world_region_axis_bindings` |
| 생성 계획 | `run_generation_plans`, `run_slot_bindings`, `run_progress_states` |
| 이동·행동 | `request_idempotency`, `safe_travels`, `turn_records`, `structured_action_history`, `turn_rule_resolutions` |
| 권한 | `admin_access_level_catalog`, `admin_access_acquisition_history`, `run_admin_access_states` |
| 선택과 결과 | `major_choices`, `region_outcomes`, `world_facts`, `unresolved_hooks` |
| 관계와 능력 | `npc_relationships`, `npc_relationship_history`, `npc_memories`, `ability_usage_history` |
| 기술 부채 | `technical_debt_entries`, `technical_debt_summaries` |
| 복원과 저장 | `reversible_actions`, `save_slots`, `save_snapshots`, `resume_validation_records` |
| LLM 감사 | 비밀이 제거된 append-only `llm_logs` |

`technical_debt_entries`는 단순 수치가 아니라 원인 턴, 기술, 대상, 강제 우회 여부, 증감량, 지연 결과와 해소 근거를 가진 인과 ledger입니다. 일반 성공은 기존 부채를 자동 감소시키지 않습니다. 회복, 책임 수용, 자원 지불 또는 NPC 협력으로 확정된 행동만 해소 근거가 됩니다.

## 권위와 트랜잭션

Rule Engine이 좌표, 경로, 점유, 합법성, D20, 피해, 자원, 권한, 핵심 단서, 진행과 `endingId`를 확정합니다. Gemini는 서버가 제공한 ID·가시 상태 안에서 짧은 구조화 후보와 확정 결과의 서술을 제안할 수 있습니다. 자유 입력의 아이템 획득·조합 제안도 이름·종류·설명만 담는 제한된 schema이며, 실제 소유권과 효과는 서버 commit이 확정합니다. 스키마·의미 검증 실패 또는 공급자 장애에는 최대 1회 repair 후 결정적 폴백을 사용합니다.

의미 행동은 모델 호출 전에 `request_idempotency`의 짧은 lease를 선점합니다. 동일 owner·operation·key·fingerprint 요청은 완료 결과를 기다려 재생하고, 다른 fingerprint는 `409`로 거절합니다. 실제 커밋은 짧은 serializable 트랜잭션에서 run을 잠그고 예상 버전을 검증한 뒤 턴·규칙 판정·이벤트·관련 이력을 함께 저장합니다. PostgreSQL의 일시적인 serialization failure(`40001`)와 deadlock(`40P01`)은 새 연결의 전체 owner transaction으로 최대 세 번 재시도합니다. 재시도 한도를 넘으면 클라이언트가 최신 run을 다시 읽을 수 있도록 충돌 응답을 반환합니다. 모델 네트워크 호출은 DB lock 밖에서 수행하며 lease는 처리 중 갱신되고 작업자 장애 뒤에는 만료되어 안전하게 인계됩니다. Undo는 보상 이벤트를 추가할 뿐 과거 턴, D20, 불변 사실이나 geometry를 되감지 않습니다.

Rule Engine이 허용 결말을 선택한 뒤 Gemini는 저장된 사실과 이력으로 에필로그만 작성합니다. 모델 출력은 `endingId`를 변경할 수 없습니다.

## 재개와 보안

snapshot은 world ID, `layoutHash`, 생성 계획 hash, 마지막 턴·이벤트 cursor와 상태 checksum을 저장합니다. 재개 시 모두 재검증하고 일치할 때만 상태를 적용합니다.

요청 트랜잭션은 검증된 profile UUID를 transaction-local `app.user_id`로 설정합니다. 연결 풀에서 session-scoped `SET`을 사용하지 않습니다. Unity는 DB에 직접 접속하지 않고 HTTP 서버만 사용합니다. `GEMINI_API_KEY`, authorization header, `resolution_seed`, 원본 system prompt와 개인 정보는 DB 응답·snapshot·로그에 저장하지 않습니다.

비용 기본 프로필은 `gemini-3.1-flash-lite`, 최소 thinking level, 작은 구조화 입력·출력, 최대 1회 repair와 결정적 폴백입니다. 모델 버전과 사용량은 감사용으로 기록하되 규칙 상태에는 영향을 주지 않습니다.

## 서버 integration test

integration test에는 migration 001~012와 seed를 적용한 전용 database를 사용합니다. `TEST_DATABASE_URL`이 없으면 PostgreSQL test는 건너뜁니다.

`runs.world_state`가 게임 도메인의 단일 권위 상태입니다. `entities`, `actors`, `entity_positions`, `inventories`, `items`는 검색·감사용 projection이며 run 생성과 모든 turn, navigation, ambient 이동, inventory mutation transaction 안에서 함께 동기화됩니다. actor HP/energy, area-local 좌표와 facing, item 수량·slot·원본 state가 권위 JSON과 다르면 transaction 전체가 실패합니다.

```bash
cd Server
TEST_DATABASE_URL='postgresql://user:password@127.0.0.1:5432/test_database' npm test
```
