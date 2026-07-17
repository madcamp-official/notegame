# 넙죽이와 붕괴한 코드 왕국 — PostgreSQL

이 디렉터리는 Codria v4 서버의 영속성 기준입니다. PostgreSQL 15+와 `pgcrypto`를 사용하며, 실행 스키마의 유일한 권위는 `Database/migrations/`입니다. 별도 ERD SQL은 설계 참고 자료일 뿐 마이그레이션 위에 실행하지 않습니다.

## 설치와 검증

아래 순서를 유지합니다.

```bash
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/001_core_schema.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/002_row_security_and_views.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/003_campaign_director_state.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/004_codria_world_and_travel.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/005_generative_run_state.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/006_codria_product_contract.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/007_codria_actions_and_access.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/008_codria_narrative_history.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/seeds/001_reference_catalogs.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/tests/smoke.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/tests/codria_v4_contract.sql
```

마이그레이션 역할에는 스키마 생성과 `pgcrypto` 설치 권한이 필요합니다. 시드는 재실행 가능하고, smoke test는 생성 데이터를 롤백합니다.

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

신규 입력은 두 종류뿐입니다.

- `MOVE`: 안전 이동은 D20과 캠페인 턴을 소비하지 않습니다. 위험 목적지는 안전 지점까지 이동한 뒤 조우를 활성화할 수 있습니다. HTTP의 `TRAVEL`은 전송 호환 별칭이며 저장값은 `MOVE`입니다.
- `USE_SKILL`: `COPY`, `DELETE`, `CONNECT`, `RESTORE`, `UNDO` 중 하나를 사용합니다. 서버가 `COMBAT`, `INVESTIGATION`, `NEGOTIATION`, `DEPLOYMENT` 중 하나로 맥락을 분류하고 정확히 한 의미 턴을 확정합니다.

`playerNote`는 선택적 서술 힌트일 뿐입니다. 자연어가 없어도 모든 합법성, 대상, 비용, D20과 결과를 결정할 수 있어야 하며, 자연어가 규칙을 우회할 수 없습니다.

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
| 이동·행동 | `safe_travels`, `turn_records`, `structured_action_history`, `turn_rule_resolutions` |
| 권한 | `admin_access_level_catalog`, `admin_access_acquisition_history`, `run_admin_access_states` |
| 선택과 결과 | `major_choices`, `region_outcomes`, `world_facts`, `unresolved_hooks` |
| 관계와 능력 | `npc_relationships`, `npc_relationship_history`, `npc_memories`, `ability_usage_history` |
| 기술 부채 | `technical_debt_entries`, `technical_debt_summaries` |
| 복원과 저장 | `reversible_actions`, `save_slots`, `save_snapshots`, `resume_validation_records` |
| LLM 감사 | 비밀이 제거된 append-only `llm_logs` |

`technical_debt_entries`는 단순 수치가 아니라 원인 턴, 기술, 대상, 강제 우회 여부, 증감량, 지연 결과와 해소 근거를 가진 인과 ledger입니다. 일반 성공은 기존 부채를 자동 감소시키지 않습니다. 회복, 책임 수용, 자원 지불 또는 NPC 협력으로 확정된 행동만 해소 근거가 됩니다.

## 권위와 트랜잭션

Rule Engine이 좌표, 경로, 점유, 합법성, D20, 피해, 자원, 권한, 핵심 단서, 진행과 `endingId`를 확정합니다. Gemini는 서버가 제공한 ID만 사용해 짧은 구조화 후보와 확정 결과의 서술을 제안할 수 있습니다. 스키마·의미 검증 실패 또는 공급자 장애에는 재시도 1회 후 결정적 폴백을 사용합니다.

의미 행동은 짧은 serializable 트랜잭션에서 run을 잠그고, idempotency fingerprint와 예상 버전을 검증한 뒤 턴·규칙 판정·이벤트·관련 이력을 함께 커밋합니다. 모델 네트워크 호출은 DB lock 밖에서 수행합니다. Undo는 보상 이벤트를 추가할 뿐 과거 턴, D20, 불변 사실이나 geometry를 되감지 않습니다.

Rule Engine이 허용 결말을 선택한 뒤 Gemini는 저장된 사실과 이력으로 에필로그만 작성합니다. 모델 출력은 `endingId`를 변경할 수 없습니다.

## 재개와 보안

snapshot은 world ID, `layoutHash`, 생성 계획 hash, 마지막 턴·이벤트 cursor와 상태 checksum을 저장합니다. 재개 시 모두 재검증하고 일치할 때만 상태를 적용합니다.

요청 트랜잭션은 검증된 profile UUID를 transaction-local `app.user_id`로 설정합니다. 연결 풀에서 session-scoped `SET`을 사용하지 않습니다. Unity는 DB에 직접 접속하지 않고 HTTP 서버만 사용합니다. `GEMINI_API_KEY`, authorization header, `resolution_seed`, 원본 system prompt와 개인 정보는 DB 응답·snapshot·로그에 저장하지 않습니다.

비용 기본 프로필은 `gemini-2.5-flash-lite`, thinking budget 0, 작은 입력·출력, 최대 1회 재시도와 결정적 폴백입니다. 모델 버전과 사용량은 감사용으로 기록하되 규칙 상태에는 영향을 주지 않습니다.
