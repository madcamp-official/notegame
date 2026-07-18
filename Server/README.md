# 《넙죽이와 붕괴한 코드 왕국》 authoritative server

이 서버는 코드리아 v4 캠페인의 규칙 권위를 소유합니다. LLM은 확정된 판정의 짧은 서사만 제안하며 월드 geometry, 이동, D20, 비용, 관리자 권한, 기술 부채와 결말 ID를 바꾸지 못합니다.

## 고정 제품 계약

- 게임: `넙죽이와 붕괴한 코드 왕국`
- 세계: `WORLD_CODRIA` / 코드리아
- 주인공: `PROTAGONIST_NUPJUKYI` / 넙죽이
- 유물: `ARTIFACT_ADMIN_KEYBOARD` / 관리자 키보드
- 입력: `MOVE`, `USE_SKILL`
- 스킬: `COPY`, `DELETE`, `CONNECT`, `RESTORE`, `UNDO`
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

`MOVE`는 안전 이동입니다. D20과 캠페인 턴을 소비하지 않고 run version만 증가시킵니다. 위험 구간에서는 안전 지점까지만 이동하고 encounter를 활성화합니다. 실제 `USE_SKILL` 행동이 D20과 의미 턴을 소비합니다.

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

`playerNote`는 선택적 분위기 메모입니다. 입력하지 않아도 전체 캠페인을 완료할 수 있고, 입력해도 합법성·난이도·효과에 영향을 주지 않습니다. `/travel`은 기존 HTTP 클라이언트의 `TRAVEL` 표기를 `MOVE` 호환 별칭으로만 허용합니다.

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

Gemini 요청은 구조화 JSON, 작은 출력, thinking budget 0, 최대 1회 repair로 제한합니다. 실패하면 deterministic fallback을 사용하며 게임 상태 commit은 서버 검증 뒤에만 일어납니다.

기본값은 비용 우선 설정인 `gemini-2.5-flash-lite`입니다. 이 모델은 2026-10-16 종료 예정이므로 배포 시 환경 변수만 바꿔 `gemini-3.1-flash-lite`로 전환할 수 있습니다.

- 가격: <https://ai.google.dev/gemini-api/docs/pricing>
- 모델 수명주기: <https://ai.google.dev/gemini-api/docs/deprecations>

Gemini 키는 `GEMINI_API_KEY` 환경 변수에만 둡니다. `.env`, Unity 클라이언트, API 응답, 로그, PostgreSQL plan과 snapshot에 키를 기록하지 않습니다.

## 실행

```bash
cp .env.example .env
npm install
npm start
```

외부 서비스 없이 시작하려면 `STORAGE=memory`와 빈 `GEMINI_API_KEY`를 유지합니다. PostgreSQL을 사용하려면 migration 001→008과 seed를 적용한 뒤 다음을 설정합니다.

```dotenv
STORAGE=postgres
DATABASE_URL=postgresql://user:password@127.0.0.1:5432/database
```

## 검증

```bash
npm test
npm run world:validate
find src test scripts -name '*.js' -print0 | xargs -0 -n1 node --check
```

PostgreSQL integration을 포함하려면 다음 환경 변수를 추가합니다.

```bash
TEST_DATABASE_URL=postgresql://user:password@127.0.0.1:5432/test_database npm test
```

## API

| Method | Path | 역할 |
| --- | --- | --- |
| `GET` | `/health` | 제품 계약·storage·model profile |
| `POST` | `/v1/campaigns` | 코드리아 캠페인 preview |
| `POST` | `/v1/campaigns/:id/runs` | run 월드 생성·검증·봉인 |
| `GET` | `/v1/runs/:id` | 현재 권위 상태 |
| `POST` | `/v1/runs/:id/travel` | 턴 미소비 `MOVE` |
| `POST` | `/v1/runs/:id/actions` | 턴 소비 `USE_SKILL` |
| `POST` | `/v1/runs/:id/turns` | `/actions` 호환 경로 |
| `GET` | `/v1/runs/:id/turns` | 커밋된 행동 목록 |
| `GET` | `/v1/runs/:id/turns/:turnNo` | 특정 행동 결과 |
| `POST` | `/v1/runs/:id/abandon` | 런 중단 |
| `POST` | `/v1/runs/:id/resume` | checksum·plan hash·layout hash 검증 후 재개 |

요청은 `x-user-id` UUID로 격리됩니다. 로컬 모드는 `DEFAULT_USER_ID`를 사용하고 운영은 `AUTH_MODE=required`로 두어야 합니다.
