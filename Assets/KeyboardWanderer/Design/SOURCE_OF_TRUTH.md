# Keyboard Wanderer — source of truth v3

이 문서는 고정 시나리오 초안이나 UI 레퍼런스가 현재 제품 계약으로 오인되지 않도록 구현 권위를 정리합니다.

## 권위 순서

1. 제품 소유자의 최신 명시적 지시
2. 사용자가 연결한 Notion 컬렉션의 현재 제품 문서
3. 이 폴더의 v3 제품·디자인 문서와 프로젝트 README
4. 서버·Unity의 현재 코드, 스키마와 자동 테스트
5. 첨부 이미지는 인터페이스의 구성·밀도·재질을 판단할 때만 참고

충돌하면 더 높은 항목을 따릅니다. 이전 고정 세계관의 명칭, 주인공 설정, 정해진 세 지역 또는 정해진 결말은 제품 사실로 유지하지 않습니다.

## 구속력 있는 제품 사실

- 제품명은 Keyboard Wanderer입니다. 매 런의 구체적인 세계와 시나리오는 seed와 검증된 LLM 기획의 조합으로 생성됩니다.
- 목표 길이는 30–50 의미 턴입니다. 안전한 탐색 이동은 의미 턴과 D20을 소비하지 않습니다.
- 캠페인 미리보기는 LLM을 호출하지 않습니다. 새 런을 확정할 때만 월드와 캠페인 기획을 만들고 저장합니다.
- 기본 월드는 160×160이며 런 시작 시 한 번 생성·검증해 봉인합니다. 이후에는 발견·점유·상태 같은 허용된 희소 변경만 적용합니다.
- 정확히 여섯 바이옴을 포함합니다: `temperate_forest_field`, `river_wetland`, `arid_desert`, `frost_highland`, `subterranean_cavern`, `ancient_ruins`.
- 정확히 여섯 일반 역할을 포함합니다: `ARRIVAL_CATALYST`, `LOCAL_STAKES`, `RELATIONSHIP_CONFLICT`, `HIDDEN_TRUTH`, `CONSEQUENCE_RETURN`, `FINAL_CONVERGENCE`.
- 진척 토큰은 `MILESTONE_TOKEN_1`, `MILESTONE_TOKEN_2`, `MILESTONE_TOKEN_3`입니다. 이름과 의미는 런마다 달라질 수 있습니다.
- 마지막 공간 퍼즐은 `anchor`, `safeguard`, `memory`, `freedom`, `threat`, `passage`, `witness` 구성 요소를 이번 런의 entity에 바인딩해 구성합니다.
- 현재 플레이어 표현은 NinjaAdventure의 `NinjaGreen`입니다. LLM은 파일 경로나 스프라이트 ID를 만들지 않습니다.
- Rule Engine은 geometry, 경로, 점유, 합법성, D20, 자원, 진척과 결말의 유일한 권위입니다. Gemini는 검증 가능한 기획과 확정 결과의 서술만 제안합니다.
- 비용 기본값은 서버의 `gemini-2.5-flash-lite`, thinking budget 0, 작은 컨텍스트·출력, 제한 재시도와 결정적 폴백입니다.

## 레퍼런스 이미지 경계

유지할 수 있는 것은 큰 중앙 맵, 상단 상태 바, 우측 로그·목표·D20 레일, 하단 명령 덱, 따뜻한 목재·금속 픽셀 감각입니다. 이미지의 세계, 캐릭터, 지형 구성, 적, 퀘스트, 재화, 문구와 정확한 좌표는 복사하거나 제품 설정으로 추론하지 않습니다.

## 허용되지 않는 구현 가정

- 런마다 이름만 바뀌는 단일 고정 줄거리
- 매 턴 LLM이 새 타일·경로·출구·POI를 만드는 방식
- 이동 한 칸마다 의미 턴과 D20을 소비하는 방식
- 바이옴과 서사 역할을 같은 열거형으로 취급하는 방식
- LLM이 좌표, 에셋 경로, 주사위, 보상, 마일스톤 또는 결말을 직접 확정하는 방식
- 레퍼런스 이미지의 콘텐츠를 프로젝트의 세계관이나 레벨 설계로 사용하는 방식
