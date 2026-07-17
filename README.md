# Keyboard Wanderer

몰입캠프 26s-w3-c3-03 프로젝트입니다. Keyboard Wanderer는 매 런 새로운 세계와 캠페인 조합을 만드는 서버 권위형 30~50턴 TRPG 로그라이크입니다. 플레이어는 NinjaAdventure의 `NinjaGreen` 캐릭터로 시작하며, 키보드의 여섯 편집 능력과 자연어 의도를 함께 사용합니다.

## 팀원

| 이름 | 학교 | GitHub | 역할 |
| --- | --- | --- | --- |
| 양호성 | 한국과학기술원 |  |  |
| 박지호 | 울산과학기술원 |  |  |
| 서영빈 | 한국과학기술원 |  |  |

현재 제품 계약은 v3입니다. 세계관·인물·지역·갈등·퀘스트·결말 후보는 seed 기반 캠페인 genome과 검증된 LLM 보강으로 달라집니다. 특정 왕국, 특정 주인공 이름, 특정 권한 체계나 고정 결말을 제품 전제로 사용하지 않습니다.

## 문서 권위

1. 제품 소유자의 최신 명시적 지시
2. 연결된 Notion 제품 문서
3. [`SOURCE_OF_TRUTH.md`](Assets/KeyboardWanderer/Design/SOURCE_OF_TRUTH.md)
4. [`PRODUCT_CONCEPT_KO.md`](Assets/KeyboardWanderer/Design/PRODUCT_CONCEPT_KO.md)
5. 서버 코드·SQL migration·자동화 테스트

첨부 레퍼런스 이미지는 UI의 분위기와 정보 배치만 제공합니다. 중앙의 큰 맵, 상단 상태, 우측 로그/목표/D20, 하단 명령 덱, 따뜻한 목재·황동 픽셀 재질을 참고하되 이미지의 시나리오, 퀘스트, 적, 경제, 캐릭터 배치와 텍스트는 게임 콘텐츠가 아닙니다.

## v3 핵심 계약

- 캠페인 선택 화면은 LLM을 호출하지 않는 deterministic preview를 사용합니다.
- 새 런은 자체 `worldSeed`, `turnLimit`, 선택적 `themeHint`로 월드와 캠페인 계획을 생성합니다.
- 기본 월드는 160×160이며, 12개 area와 정확히 6개 terrain biome을 포함합니다.
- 월드의 타일, area, route, POI, 배치 slot은 런 시작 시 한 번 생성·검증·봉인됩니다.
- 플레이 중에는 기존 월드 안에서 이동하거나 entity/run 상태만 바뀝니다. 매 턴 맵을 다시 만들지 않습니다.
- 캠페인은 6개 일반 역할과 3개 milestone으로 수렴하며 30~50개의 의미 있는 턴 안에 종료됩니다.
- Rule Engine이 좌표, 이동, 점유, D20, 피해, 자원, 진행 조건, 지표와 결말 레시피를 소유합니다.
- Gemini는 기존 ID에 연결된 짧은 서사 후보만 제안하며 검증 실패나 장애 시 deterministic fallback을 사용합니다.
- API 키는 서버 환경 변수에만 존재하고 Unity, 응답, 로그, DB plan JSON에 포함되지 않습니다.

### Terrain biome 6개

| ID | 표현 |
| --- | --- |
| `temperate_forest_field` | 온대 숲과 들판 |
| `river_wetland` | 강과 습지 |
| `arid_desert` | 건조 사막 |
| `frost_highland` | 설원 고지 |
| `subterranean_cavern` | 지하 동굴 |
| `ancient_ruins` | 고대 유적 |

### Campaign role 6개

| ID | 서사 기능 |
| --- | --- |
| `ARRIVAL_CATALYST` | 도착과 첫 변화의 촉발 |
| `LOCAL_STAKES` | 지역 주민의 구체적인 이해관계 |
| `RELATIONSHIP_CONFLICT` | 인물·집단 사이 약속의 충돌 |
| `HIDDEN_TRUTH` | 감춰진 원인의 발견 |
| `CONSEQUENCE_RETURN` | 이전 선택의 결과 귀환 |
| `FINAL_CONVERGENCE` | 모든 갈래와 선택의 최종 수렴 |

Role은 biome과 별도 레이어입니다. Seed는 각 역할을 서로 다른 area, 랜드마크, NPC, 증거와 해결 방식에 배정합니다.

### Progress milestone

서버가 검증하는 ID는 `MILESTONE_TOKEN_1`, `MILESTONE_TOKEN_2`, `MILESTONE_TOKEN_3`입니다. 표시 이름과 의미는 런의 genome에 따라 달라집니다. 세 milestone을 모두 획득해야 최종 수렴 area의 논리 게이트를 통과할 수 있습니다.

## 월드 생성 파이프라인

| 단계 | 입력 | 결과 | 검증 |
| --- | --- | --- | --- |
| Progression contract | Seed + generic roles | 6-node DAG + finale gate | 순환 없음, milestone 3개 |
| Area anchors | Seed + graph | 12개 area | 모든 biome 정확히 2개 anchor |
| Routes | Area anchors | MST + 우회 loop | finale 전/후 단계별 도달성 |
| Terrain raster | Seed + biome map | 160×160 tile/area/biome layer | 군집 지형, 도로 폭, 경계 |
| POI/slots | Graph + walkable map | 조우 공간과 의미 기반 slot | 9×9 clearing, 중복 없음 |
| Validation/repair | 전체 world | generation report | 결정적 국소 보정 후 실패 시 중단 |
| Seal | 검증된 world | `layoutHash` + public RLE DTO | 이후 geometry 불변 |

## 플레이 흐름

안전 탐색과 의미 있는 턴은 분리됩니다.

- 안전 탐색: `POST /v1/runs/:id/travel`, D20과 캠페인 턴을 소비하지 않음
- 위험 진입: 안전 staging 위치까지만 이동하고 active encounter 생성
- 의미 있는 행동: 전투, 조사, 협상, 퍼즐, 복구 또는 키보드 편집을 D20으로 판정하고 정확히 한 턴 커밋
- 결말: 서버가 공간 관계, 제거/활성 상태, 누적 지표와 seed가 선택한 recipe subset을 평가
- hard limit: 남은 조합이 없으면 emergency 후보로 런을 안전하게 종료

### 여섯 키보드 능력

| 능력 | 역할 |
| --- | --- |
| Move | 조우 안에서 합법적인 위치로 이동 |
| Copy | 허용된 객체의 사본 생성 |
| Delete | 보호되지 않은 entity 제거 |
| Connect | 두 entity 사이 임시 관계 생성 |
| Restore | 최근 손상·제거의 권위 스냅샷 복구 |
| Undo | 직전 가역 결과에 보상 연산 적용 |

Attack, Interact/Investigate, Negotiate, Rest는 상황 행동입니다. 자유 입력은 의도 정렬과 서술에 사용되지만 불법 대상·좌표·비용·D20 결과를 우회하지 못합니다.

## LLM 경계와 비용

런 생성 시 `CAMPAIGN_PLAN`은 기존 beat/NPC/quest/ending/area ID를 대상으로 제목, 설명, tone, area flavor만 제안합니다. 좌표, slot 선택, asset 경로, 능력 조건, 수치, 보상과 결말 recipe는 스키마에서 거부됩니다.

일반 턴에서는 서버가 제공한 entity/quest/slot/asset ID 범위 안에서 대사, 사실, 소문, NPC 기억, 짧은 퀘스트 hook과 시각 의도를 제안할 수 있습니다. 모든 제안은 커밋 전에 다시 검증됩니다.

기본 모델은 `gemini-2.5-flash-lite`, thinking budget은 0, 출력은 작은 구조화 JSON, 재시도는 최대 1회입니다. 캠페인 preview에는 LLM 비용이 발생하지 않습니다.

## 빠른 실행

### Server

```bash
cd Server
npm install
npm test
npm start
```

환경 변수 예시는 [`Server/.env.example`](Server/.env.example)을 참고합니다. `.env`와 API 키는 commit하지 않습니다.

### World CLI

```bash
cd Server
npm run world:generate -- --seed 20260717 --output ./generated/world-20260717
npm run world:validate
```

### Unity

1. Unity 6000.5.4f1에서 `Assets/Scenes/SampleScene.unity`를 엽니다.
2. 서버를 실행한 뒤 Play Mode에서 새 런을 시작합니다.
3. NinjaAdventure 경로가 바뀌었다면 **Keyboard Wanderer > Rebuild Ninja Adventure Manifest**를 실행합니다.
4. 서버가 없으면 `seeded-local-world.v7` 로컬 generator가 같은 역할·biome·turn 계약의 별도 deterministic fallback을 만듭니다.

온라인 서버와 로컬 generator는 같은 제품 계약을 따르지만 byte-identical layout을 보장하지 않습니다.

## 주요 API

| Method | Path | 기능 |
| --- | --- | --- |
| `GET` | `/health` | 권위·storage·model profile 상태 |
| `POST` | `/v1/campaigns` | deterministic 캠페인 preview 생성 |
| `GET` | `/v1/campaigns` | 소유 캠페인 목록 |
| `GET` | `/v1/campaigns/:id` | preview와 world 조회 |
| `POST` | `/v1/campaigns/:id/runs` | 새 run world/plan 생성 및 봉인 |
| `GET` | `/v1/runs/:id` | 현재 run 조회 |
| `POST` | `/v1/runs/:id/travel` | 안전 탐색 이동 |
| `POST` | `/v1/runs/:id/turns` | 권위 턴 판정·커밋 |
| `GET` | `/v1/runs/:id/turns` | 커밋된 턴 목록 |
| `POST` | `/v1/runs/:id/abandon` | 런 중단 |
| `POST` | `/v1/runs/:id/resume` | 런 재개 |

런 생성 예시:

```json
{
  "worldSeed": 20260718,
  "turnLimit": 40,
  "themeHint": "달빛 아래 이어지는 민담과 공동체의 약속"
}
```

## PostgreSQL

실행 가능한 스키마 권위는 [`Database/migrations`](Database/migrations)입니다. 파일을 lexical order로 적용하고 [`Database/README.md`](Database/README.md)의 owner RLS, run-scoped world, generation plan, generic progress, deep-resume 계약을 따릅니다. 별도 ERD export는 설계 참고용이며 migration 위에 덮어쓰지 않습니다.

## 완료 기준

- 동일 seed/version은 동일한 layout/content hash를 만든다.
- 서로 다른 seed는 세계 이름, premise, NPC, quest, ending subset과 배치를 실질적으로 바꾼다.
- 모든 런에 biome 6개, generic role 6개, milestone 3개가 존재한다.
- 30·40·50턴 구성에서 유효한 수렴 또는 emergency 종료가 가능하다.
- 일반 턴, Restore, Undo, 저장/재개는 sealed `layoutHash`를 바꾸지 않는다.
- 잘못된 LLM JSON은 규칙 상태를 바꾸지 않고 deterministic fallback으로 대체된다.
- UI는 레퍼런스 이미지의 분위기만 사용하고 해당 이미지의 콘텐츠를 복제하지 않는다.
- 플레이어 sprite는 현재 NinjaAdventure `NinjaGreen` 자산을 사용한다.
