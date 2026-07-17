# Keyboard Wanderer — Unity client v3

Keyboard Wanderer는 seed와 검증된 LLM 기획으로 매 런의 장소, 인물, 갈등, 비밀과 결말 맥락이 달라지는 30–50 의미 턴 판타지 어드벤처입니다. 현재 주인공은 NinjaAdventure의 `NinjaGreen`을 사용합니다.

## 현재 게임 계약

- 새 런은 기본 160×160 월드를 한 번 생성·검증하고 `layoutHash`와 함께 봉인합니다. 플레이 중 LLM과 턴 처리기는 타일, 바이옴, 경로, 영역, POI를 재생성하거나 이동하지 않습니다.
- 월드에는 `temperate_forest_field`, `river_wetland`, `arid_desert`, `frost_highland`, `subterranean_cavern`, `ancient_ruins`의 여섯 바이옴이 모두 존재합니다.
- 생성된 영역에는 `ARRIVAL_CATALYST`, `LOCAL_STAKES`, `RELATIONSHIP_CONFLICT`, `HIDDEN_TRUTH`, `CONSEQUENCE_RETURN`, `FINAL_CONVERGENCE`의 여섯 서사 역할이 seed별로 배정됩니다. 역할은 바이옴과 별도 데이터입니다.
- 캠페인 진척은 `MILESTONE_TOKEN_1`, `MILESTONE_TOKEN_2`, `MILESTONE_TOKEN_3`으로 기록합니다. 각 토큰의 이름, 의미, 획득 방법은 이번 런의 검증된 기획에서 결정됩니다.
- 마지막 역할은 `anchor`, `safeguard`, `memory`, `freedom`, `threat`, `passage`, `witness` 중 이번 런에 바인딩된 구성 요소로 공간 퍼즐과 결말을 만듭니다.
- 캠페인 미리보기는 seed 기반 메타데이터만 보여 주며 LLM을 호출하지 않습니다. 실제 런 생성 시 월드와 캠페인 기획을 한 번 확정해 저장합니다.
- 안전한 탐색 이동은 시간과 발견 상태만 바꾸며 D20이나 의미 턴을 소비하지 않습니다. 전투, 조사, 협상, 퍼즐, 복구와 확정된 배치만 의미 턴을 소비합니다.

첨부 레퍼런스 이미지는 중앙의 큰 맵, 상단 상태, 우측 정보 레일, 하단 명령 덱, 따뜻한 픽셀 재질 같은 인터페이스 감각만 참고합니다. 이미지의 지역, 인물, 적, 퀘스트, 문구와 배치는 게임 콘텐츠가 아닙니다.

## 실행

```bash
cd Server
npm install
npm start
```

Unity 6000.5.4f1에서 `Assets/Scenes/SampleScene.unity`를 열고 Play Mode를 시작합니다. 온라인 런은 서버 월드 `keyboard-wanderer-world.v6`를 사용합니다. 서버에 연결할 수 없으면 독립적인 결정적 폴백 `seeded-local-world.v7`이 같은 봉인 월드·진척 계약으로 실행되며, 온라인 월드와 동일한 바이트 배열을 보장하지는 않습니다.

에셋 경로가 바뀌었다면 Unity 메뉴의 **Keyboard Wanderer > Rebuild Ninja Adventure Manifest**를 실행합니다. 주인공은 manifest가 가리키는 `NinjaGreen` 스프라이트로 렌더링합니다.

## 조작

| 키 | 명령 | 역할 |
| --- | --- | --- |
| `1` / `W` | Move | 탐색 이동 또는 조우 안의 합법적 재배치 |
| `2` / `E` | Copy | 허용된 객체를 빈 타일에 복제 |
| `3` / `R` | Delete | 보호되지 않은 객체나 임시 효과 제거 |
| `4` / `C` | Connect | 두 대상 사이의 검증 가능한 관계 생성 |
| `5` / `Q` | Restore | 최근 손상·삭제 상태를 권위 스냅샷으로 복구 |
| `6` / `Z` | Undo | 직전 가역 결과에 보상 이벤트 추가 |
| `T` | Attack | 인접 대상과 전투 |
| `Space` | Interact / Investigate | NPC·소품·증거와 상호작용 또는 조사 |
| `N` | Negotiate | 비적대 인물과 협상 |
| `I` | Rest | 조건부 회복 |

Move, Copy, Delete, Connect, Restore, Undo는 여섯 주 명령입니다. 자유 입력은 의도와 서술에 반영되지만 서버가 확정한 좌표, 대상, 비용, D20과 진행 조건을 우회하지 못합니다.

## 진행과 결말

상단 HUD는 현재 장소·바이옴, 캠페인 phase, 의미 턴, `마일스톤 0/3`과 주요 지표를 표시합니다. 마일스톤은 해당 런의 올바른 역할·증거·선택을 만족했을 때만 획득합니다. 마지막 영역은 세 토큰과 기획된 선행 조건을 충족하기 전까지 잠깁니다.

최종 공간 퍼즐은 이번 캠페인에 실제로 등장한 대상만 finale 구성 요소에 바인딩합니다. Rule Engine은 배치, 연결, 보호·제거 상태와 누적 지표로 허용된 결말을 확정하고, Gemini는 확정 결과 안에서만 에필로그를 작성합니다. 공급자 실패나 50턴 도달 시에도 결정적 대체 경로가 런을 종료합니다.

## 권위와 저장

서버는 월드 geometry, 이동, 점유, 보호 상태, D20, 자원, 마일스톤, 지표와 결말을 확정합니다. Gemini의 기본 모델은 비용을 줄이기 위해 `gemini-2.5-flash-lite`이며, 런 생성 기획과 확정 턴의 짧은 서술만 담당합니다. API 키는 Unity나 저장 파일이 아니라 서버 환경 변수 `GEMINI_API_KEY`로만 주입합니다.

저장은 seed/version/layout hash, 발견 상태, 플레이어 위치, 의미 턴, 세 마일스톤, 캠페인 기획, NPC 기억, 미해결 갈등, 복원 ledger와 finale 바인딩을 round-trip 합니다.
