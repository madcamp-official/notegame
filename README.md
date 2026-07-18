# 넙죽이와 붕괴한 코드 왕국

몰입캠프 26s-w3-c3-03 프로젝트입니다. 현실에서 코드리아로 추락한 개발자 **넙죽이**가 관리자 키보드로 세계의 객체와 관계를 편집하고, 관리자 권한 3단계를 획득해 루트 시스템에 진입하는 저인지부하 생성형 어드벤처입니다.

Seed는 코드리아나 주인공을 바꾸지 않습니다. 런마다 필수 지역 축의 실제 위치, 물리 바이옴, 경로, NPC, 사건, 권한 획득 방식과 에필로그를 바꿉니다.

## 팀원

| 이름 | 학교 | GitHub | 역할 |
| --- | --- | --- | --- |
| 양호성 | 한국과학기술원 |  |  |
| 박지호 | 울산과학기술원 |  |  |
| 서영빈 | 한국과학기술원 |  |  |

## 제품 계약 v4

| 항목 | 고정값 |
| --- | --- |
| 게임 | 《넙죽이와 붕괴한 코드 왕국》 |
| 세계 | `WORLD_CODRIA` · 코드리아 |
| 주인공 | `PROTAGONIST_NUPJUKYI` · 넙죽이 |
| 핵심 유물 | `ARTIFACT_ADMIN_KEYBOARD` · 관리자 키보드 |
| 캠페인 길이 | 기본 40턴, 허용 범위 30~50턴 |
| 관리자 권한 | `ADMIN_ACCESS_LEVEL_1`–`3` |
| 최종 목적지 | `REGION_ROOT_SYSTEM` |
| 핵심 입력 | `MOVE`, `USE_SKILL` |

관리자 권한 단계의 의미는 고정됩니다.

1. 관찰·읽기 권한
2. 제한된 편집·연결 권한
3. 시스템 배치와 루트 게이트 접근 권한

각 단계는 서로 다른 지역과 행동 문맥에 연결된 획득 후보를 최소 두 개 갖습니다. Seed와 플레이어 선택이 실제 경로를 결정하며, 세 단계와 붕괴 원인 단서 없이는 루트 시스템에 진입할 수 없습니다.

## 월드 계약

런 시작 시 160×160 월드를 한 번 생성하고 검증한 뒤 `layoutHash`로 봉인합니다. 턴 진행, 저장·재개, Restore와 Undo는 geometry를 바꾸지 않습니다. LLM은 타일, 좌표, 길, 출구, 건물과 바이옴을 생성하거나 수정할 수 없습니다.

필수 캠페인 지역 축은 물리 지형 바이옴과 별도 레이어입니다.

| 지역 축 ID | 표시 이름 |
| --- | --- |
| `REGION_BUG_FOREST` | 버그 숲 |
| `REGION_BUFFER_VILLAGE` | 버퍼 마을 |
| `REGION_DEADLOCK_CITY` | 데드락 도시 |
| `REGION_DATA_GRAND_LIBRARY` | 데이터 대도서관 |
| `REGION_LEGACY_CITADEL` | 레거시 성채 |
| `REGION_ROOT_SYSTEM` | 루트 시스템 |

현재 generator가 사용하는 물리 지형은 다음 여섯 종류입니다.

- `temperate_forest_field`
- `river_wetland`
- `arid_desert`
- `frost_highland`
- `subterranean_cavern`
- `ancient_ruins`

`CampaignRegionAxis → Area → TerrainBiome → POI → NPC/Quest slot → Admin access candidate` 순으로 매핑되며, 동일 Seed와 generator version은 동일한 layout hash를 만듭니다.

## 입력과 턴

플레이어의 권위 입력은 이동 또는 스킬과 대상 선택뿐입니다. 자연어 메모를 추가하더라도 규칙 판정의 권위 입력으로 사용하지 않습니다.

안전 이동 예시:

```json
{
  "inputType": "MOVE",
  "destination": {
    "areaId": "area.buffer-village",
    "x": 12,
    "y": 7
  },
  "expectedRunVersion": 27,
  "idempotencyKey": "move-00000001"
}
```

스킬 행동 예시:

```json
{
  "inputType": "USE_SKILL",
  "skillId": "CONNECT",
  "targetIds": [
    "11111111-1111-4111-8111-111111111111",
    "22222222-2222-4222-8222-222222222222"
  ],
  "expectedRunVersion": 27,
  "idempotencyKey": "skill-00000001"
}
```

안전 타일 이동, 발견된 POI 간 이동, UI 열람, 대상 확인, 경로 미리보기, 취소, 모호한 대상 재선택과 불법 입력 거절은 캠페인 턴과 D20을 소비하지 않습니다.

HTTP에서는 이 `MOVE` 명령을 `/travel` 경로로 전송하며, 기존 클라이언트의 `TRAVEL` 표기는 호환 별칭으로 정규화됩니다.

캠페인 턴을 소비하는 행동 문맥은 정확히 네 종류입니다.

- `COMBAT`
- `INVESTIGATION`
- `NEGOTIATION`
- `DEPLOYMENT`

키보드 스킬 `COPY`, `DELETE`, `CONNECT`, `RESTORE`, `UNDO`는 이 문맥 안에서 사용됩니다. 위험 지역 이동은 안전 구간 도착과 조우 활성화까지만 처리하고, 이후 실제 행동에서 D20과 캠페인 턴을 소비합니다.

## 캠페인 골격

모든 런은 다음 거시 골격을 유지합니다.

1. 코드리아 추락과 관리자 키보드 각성
2. 붕괴 현상과 첫 지역 문제 발견
3. 관리자 권한 I 획득
4. 관리자 권한 II 획득
5. 붕괴 원인이 관리자 통제 시스템 내부에 있음을 확인
6. 기술 부채와 과거 선택의 역류
7. 관리자 권한 III 획득
8. 루트 시스템 진입
9. 최종 배치와 결말

이 골격은 특정 지역의 고정 장면이나 하나의 보스와 1:1로 묶이지 않습니다. 방문 순서, NPC, 사건, 권한 획득 문맥과 구체적 인과관계는 Seed와 선택에 따라 달라집니다.

## 선택과 기술 부채

서버와 PostgreSQL은 다음 권위 상태를 저장합니다.

- `majorChoices`
- `regionOutcomes`
- `npcRelationships`
- `canonicalFacts`
- `unresolvedHooks`
- `abilityUsageHistory`
- `adminAccessAcquisitionHistory`
- `technicalDebtEntries`

기술 부채는 합계와 원장을 함께 유지합니다. 원장에는 스킬, 대상, 강제 override, 증감량, 지연 결과와 해결 시점이 기록됩니다. 일반 성공만으로 부채가 자동 감소하지 않으며, 복구·책임 수용·자원 지불·NPC 협력처럼 명시적인 해결 행동이 필요합니다.

선택은 즉시 수치 변화, 중기 NPC·지역 반응, 장기 레거시 성채·루트 시스템·에필로그의 세 단계에서 회수됩니다.

## LLM 경계와 비용

Rule Engine이 이동, 합법성, D20, 피해, 비용, 기술 부채, 관리자 권한과 결말 범주를 확정합니다. Campaign Director가 캠페인 단계, 사건 후보와 남은 턴 수렴을 관리합니다. LLM은 확정된 결과를 2~4문장의 장면, 대사, 선택 회수와 에필로그로 표현합니다.

기본 모델은 [현재 공식 가격표](https://ai.google.dev/gemini-api/docs/pricing)에서 비용이 가장 낮은 안정 Flash-Lite 설정인 `gemini-2.5-flash-lite`입니다. thinking budget 0, 작은 구조화 출력, 최대 1회 repair와 deterministic fallback을 사용합니다. 모델과 키는 서버 환경 변수로만 설정하며 Unity, 응답, DB plan과 저장소에 노출하지 않습니다.

`gemini-2.5-flash-lite`의 [공식 종료 예정일](https://ai.google.dev/gemini-api/docs/deprecations)은 2026-10-16입니다. 배포 환경에서는 `GEMINI_FAST_MODEL`과 `GEMINI_QUALITY_MODEL`을 `gemini-3.1-flash-lite`로 교체할 수 있습니다.

## 저인지부하 UI

- 메인 목표 1개
- 보조 목표 최대 2개
- 현재 추천 행동 2~3개
- 동시에 강조하는 대상 최대 5개
- 실행 전 대상, 스킬, 행동 문맥, 턴 소비 여부와 위험 표시
- 사용할 수 없는 스킬 비활성화
- 결과는 판정 → 상태 변화 → 2~4문장 서사 순서
- 긴 기록은 로그와 저널에 저장

현재 전용 넙죽이 픽셀 스프라이트가 저장소에 없으므로, NinjaAdventure `NinjaGreen`을 넙죽이의 임시 렌더링 에셋으로 사용합니다. 도메인 ID와 표시 이름은 항상 넙죽이로 유지합니다.

## 빠른 실행

### Server

```bash
cd Server
npm install
npm test
npm start
```

환경 변수는 [Server/.env.example](Server/.env.example)을 참고합니다. `.env`, Gemini 키와 PostgreSQL 비밀번호는 커밋하지 않습니다.

### World validation

```bash
cd Server
npm run world:generate -- --seed 20260717 --output ./generated/world-20260717
npm run world:validate
```

### Unity

1. Unity 6000.5.4f1에서 `Assets/Scenes/SampleScene.unity`를 엽니다.
2. 서버를 실행하고 Play Mode에서 새 런을 시작합니다.
3. 서버가 없으면 로컬 deterministic fallback으로 동일한 제품 계약을 플레이합니다.
4. NinjaAdventure 경로가 바뀌었다면 **Keyboard Wanderer > Rebuild Ninja Adventure Manifest**를 실행합니다.

## API

| Method | Path | 기능 |
| --- | --- | --- |
| `GET` | `/health` | 권위·storage·model profile 확인 |
| `POST` | `/v1/campaigns` | 코드리아 캠페인 preview 생성 |
| `POST` | `/v1/campaigns/:id/runs` | 월드 생성·검증·봉인 |
| `GET` | `/v1/runs/:id` | 현재 run 조회 |
| `POST` | `/v1/runs/:id/travel` | 턴 미소비 안전 이동 |
| `POST` | `/v1/runs/:id/actions` | 스킬·대상 기반 권위 행동 |
| `GET` | `/v1/runs/:id/turns` | 커밋된 캠페인 행동 조회 |
| `POST` | `/v1/runs/:id/resume` | snapshot 기반 재개 |

## PostgreSQL

실행 가능한 스키마 권위는 [Database/migrations](Database/migrations)입니다. 사용자 제공 `v1.1.sql` ERD의 핵심 run/turn/world 원장을 PostgreSQL로 옮기고, 코드리아 지역 축, 관리자 권한 이력, 선택 회수와 기술 부채 원장을 순차 migration으로 확장합니다.

## 문서 권위

1. 제품 소유자의 최신 명시적 지시
2. Notion `08_제품_계약_넙죽이와_붕괴한_코드_왕국_v4.0`
3. 서비스 PRD와 턴 처리 워크플로우
4. LLM 게임 디렉터 명세
5. 구현 정합화 계획
6. [SOURCE_OF_TRUTH.md](Assets/KeyboardWanderer/Design/SOURCE_OF_TRUTH.md)
7. 서버 코드, PostgreSQL migration과 자동화 테스트

첨부 레퍼런스 이미지는 UI의 분위기와 정보 배치만 제공합니다. 시나리오, 캐릭터, 퀘스트와 경제 구조를 복제하지 않습니다.
