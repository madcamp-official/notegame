# Ninja Adventure — Unity 클라이언트 v4

코드리아(`WORLD_CODRIA`)의 붕괴 원인을 추적하고 관리자 키보드(`ARTIFACT_ADMIN_KEYBOARD`)의 세 접근 권한을 되찾는 픽셀 어드벤처입니다. 주인공은 넙죽이(`PROTAGONIST_NUPJUKYI`)이며, 현재 화면 표현만 NinjaAdventure의 `NinjaGreen` 에셋을 임시 사용합니다.

`KeyboardWanderer`는 기존 Unity 폴더·namespace 이름일 뿐 제품명이나 세계관이 아닙니다.

## 플레이 계약

- 새 런은 160×160 월드를 한 번 생성하고 `layoutHash`와 함께 봉인합니다. 일반 턴, 저장·재개, Restore와 Undo는 geometry를 바꾸지 않습니다.
- 여섯 고정 지역 축은 `REGION_BUG_FOREST`, `REGION_BUFFER_VILLAGE`, `REGION_DEADLOCK_CITY`, `REGION_DATA_GRAND_LIBRARY`, `REGION_LEGACY_CITADEL`, `REGION_ROOT_SYSTEM`입니다.
- 여섯 지역 축과 여섯 물리 바이옴은 별도 데이터입니다. 축은 장소의 서사 정체성이고 바이옴은 지형·팔레트·이동 규칙입니다.
- 진행은 도착/각성부터 최종 배포/결말까지 정확히 아홉 비트입니다.
- 관리자 권한은 `ADMIN_ACCESS_LEVEL_1`부터 `3`까지 정확히 세 단계입니다. 각 단계에는 서로 다른 영역·행동 맥락의 후보가 최소 두 개 있습니다.
- 루트 시스템은 세 권한과 내부 관리자 통제 시스템에 관한 핵심 단서를 모두 확보해야 열립니다.
- 기술 부채는 원인과 후속 결과를 가진 ledger입니다. 일반 성공으로 자동 감소하지 않습니다.

첨부 레퍼런스 이미지는 큰 중앙 맵, 상단 상태, 우측 정보 레일, 하단 명령 덱과 따뜻한 픽셀 재질 같은 인터페이스 감각만 참고합니다. 이미지의 세계, 인물, 적, 퀘스트, 문구, 아이콘 의미와 레벨 배치는 제품 콘텐츠가 아닙니다.

## 실행

```bash
cd Server
npm install
npm start
```

Unity 6000.5.4f1에서 `Assets/Scenes/SampleScene.unity`를 열고 Play Mode를 시작합니다. 서버에 연결할 수 없거나 Gemini 응답이 유효하지 않으면 결정적 로컬 폴백으로 규칙 진행을 유지해야 합니다.

에셋 경로가 바뀌면 Unity 메뉴의 **Keyboard Wanderer > Rebuild Ninja Adventure Manifest**를 실행합니다. manifest는 넙죽이의 임시 `NinjaGreen` 표현과 환경 스프라이트만 해결하며 제품 식별자를 바꾸지 않습니다.

## Unity 저작 에셋 구조

고정 오브젝트 구성은 런타임 코드가 아니라 Unity 에셋에 저장합니다.

- `SampleScene.unity`: `Authored World`, Camera, `Authored Audio`, EventSystem과 UI Prefab 인스턴스
- `Prefabs/UI/AuthoredUI.prefab`: 타이틀, HUD, 설정, 일시정지, 결말 화면
- `Prefabs/World/EntityVisual.prefab`: Actor, 체력 바, 결말 라벨 계층
- `Prefabs/World/Landmark.prefab`: 캠페인 랜드마크 표현
- `ScriptableObjects/KeyboardWandererAuthoringSettings.asset`: Prefab 참조와 이동·카메라·표현 크기 설정

씬 또는 프리팹 구성을 다시 만들려면 Play Mode를 종료하고 Unity 메뉴의 **Keyboard Wanderer > Convert Runtime Composition to Authored Assets**를 실행합니다. 변환기는 기존 에셋을 같은 경로에 갱신하고 Scene 참조를 다시 연결합니다.

절차적으로 달라지는 160×160 타일 배치와 서버가 내려주는 엔티티 상태는 런타임 데이터이므로 코드에 남습니다. 다만 런타임은 오브젝트를 직접 조립하는 대신 씬의 `KeyboardWandererWorldView`와 저장된 Prefab을 채웁니다. 테스트·복구용 씬에서만 기존 코드 생성 폴백을 사용합니다.

## 입력

클라이언트가 전송하는 신규 입력은 두 종류뿐입니다.

| 입력 | 선택 | 턴 규칙 |
| --- | --- | --- |
| `MOVE` | 검증 가능한 목적지 | 안전 이동은 D20·캠페인 턴을 소비하지 않으며 위험 이동은 조우만 활성화할 수 있음 |
| `USE_SKILL` | `COPY`, `DELETE`, `CONNECT`, `RESTORE`, `UNDO`, `SEARCH`, `SELECT_ALL` | 전투·조사·협상·배치 맥락에서 의미 있는 결과를 확정하며 정확히 한 턴 소비 |

화면 스킬명은 단축키를 우선합니다. `Delete`만 키 이름을 그대로 쓰며, 복제는 `Ctrl C`로 원본을 캡처하면 `Ctrl V` 상태로 전환되고 빈 배치 타일을 고른 뒤 실행됩니다. `Ctrl Z`는 Undo, `Ctrl F`는 주변 6칸 검색, `Ctrl A`는 주변 4칸 관리자 영역 전개입니다.

Attack, Interact, Negotiate, Rest는 공개 기술이나 별도 입력이 아닙니다. 서버는 선택한 기술과 대상을 `COMBAT`, `INVESTIGATION`, `NEGOTIATION`, `DEPLOYMENT` 중 하나로 분류합니다. `playerNote`는 선택 사항이며, 자연어 없이도 합법적인 턴이 완성되어야 합니다.

## 화면 계약

- 중앙: 봉인 월드 카메라와 선택 경로
- 상단: 장소, 물리 바이옴, 9비트 진행, 의미 턴, 관리자 권한 `0/3`, 핵심 지표
- 우측: 주 목표 1개, 보조 목표 최대 2개, 추천 행동 2–3개, 판정 결과와 상태 변화
- 하단: MOVE와 관리자 키보드 단축키, 대상/목적지, 선택적 메모, 확정 버튼
- 사용할 수 없는 기술은 숨기지 않고 비활성화하며 이유를 제공합니다.
- 확정 전 대상, 기술, 턴 소비 여부와 위험을 보여 줍니다.
- 결과는 `판정 → 상태 변화 → 2–4문장 서술` 순서로 표시합니다.

## 진행과 저장

아홉 비트는 다음 순서를 보장합니다: 도착과 키보드 각성, 첫 붕괴 문제, 권한 I, 권한 II, 내부 통제 원인 확인, 기술 부채 역류, 권한 III, 루트 진입, 최종 배포와 결말. Rule Engine이 권한·핵심 단서·결말 ID를 확정하며 Gemini는 확정된 결말의 에필로그만 작성합니다.

저장은 seed/version/`layoutHash`, 위치, 아홉 비트, 권한 획득 근거, `majorChoices`, `regionOutcomes`, `npcRelationships`, `canonicalFacts`, `unresolvedHooks`, `abilityUsageHistory`, `adminAccessAcquisitionHistory`, `technicalDebtEntries`를 왕복해야 합니다.

Gemini 기본 프로필은 비용 절감을 위해 `gemini-2.5-flash-lite`, thinking budget 0, 작은 구조화 출력, 최대 1회 재시도와 결정적 폴백을 사용합니다. API 키는 Unity나 저장 파일이 아니라 서버 환경 변수 `GEMINI_API_KEY`에만 둡니다.
