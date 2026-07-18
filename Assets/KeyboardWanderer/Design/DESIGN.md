# Ninja Adventure — Gameplay HUD Design System v5

## 1. Visual Theme & Atmosphere

PC 16:9용 top-down pixel adventure HUD다. 월드는 화면의 주인공이며 UI는 가장자리의 얇은 프레임 안에 머문다. 제공 레퍼런스에서 중앙 맵 우선순위와 가장자리 패널 리듬만 유지하고, 캐릭터·아이콘·퀘스트·정확한 프레임 형태는 복사하지 않는다.

## 2. Color

- 배경 패널 `#17130F` 92%, 안쪽 패널 `#261B14`, 경계 `#715630`
- 강조 금색 `#F0BA55`, 본문 `#F2DFC0`, 보조 `#BFA57B`
- 성공 `#77A94B`, 경고 `#D98A35`, 위험 `#C7513E`
- 월드 위에 올라가는 패널은 불투명도 88–94%로 유지한다.
- blur, glass, neon bloom, 큰 gradient를 사용하지 않는다.

## 3. Typography

NinjaAdventure 픽셀 폰트를 기본으로 사용한다. 한국어 본문은 14–18px 상당, 패널 제목은 16–20px, 핵심 수치는 18–22px다. 한 줄 제목은 말줄임, 설명은 최대 2줄이며 긴 서술은 하단 대화창 안에서만 줄바꿈한다.

## 4. Spacing & Grid

8px 공간 단위를 사용한다. 화면 안전 여백은 가로 2%, 세로 2.5%다. 패널 내부 padding은 12–16px, 패널 사이 간격은 10–16px다. 최소 논리 입력 영역은 44×44px이며 모든 픽셀 이미지는 정수 좌표와 point filtering을 사용한다.

## 5. Layout & Composition

- 중앙 `World Viewport`: 화면의 최소 62%를 방해 없이 유지한다.
- 좌상단 `Player Status`: 현재 지역과 장면을 한 패널에 표시한다.
- 좌측 `Objective Panel`: 현재 목표와 진행 힌트를 2단으로 표시한다.
- 우측 `Skill Rail`: MOVE, COPY, DELETE, CONNECT, RESTORE, UNDO, SEARCH, SELECT ALL을 세로로 배치한다.
- 좌하단 `Minimap Panel`: 월드맵 자리와 현재 지역명을 제공하는 편집 가능한 placeholder다.
- 하단 중앙 `Story Panel`: 대화가 있을 때만 시선을 끄는 낮은 가로 패널이다.
- 우상단 `Menu Hint`, 우하단 `Confirm Action`은 가장자리 조작으로 분리한다.

## 6. Components

- `Player Status`: 초상 자리, 지역, 장면 제목. 게임 데이터가 Text만 갱신한다.
- `Objective Panel`: 현재 목표와 간단한 안내. Inspector에서 문구와 크기를 편집할 수 있다.
- `Skill Rail`: 모든 기술을 숨기지 않고 disabled 상태까지 유지한다. 각 버튼은 독립 RectTransform이다.
- `Minimap Panel`: 실제 지도 기능 전의 placeholder이며 이후 RenderTexture나 지도 sprite로 교체 가능하다.
- `Story Panel`: 초상, 화자, 본문, 다음 버튼을 포함한다.
- `Confirm Action`: 선택한 기술과 대상을 확정하는 독립 버튼이다.

## 7. Motion & Interaction

hover/pressed sprite swap은 160–220ms 체감으로 짧게 유지한다. 선택 기술은 색 또는 얇은 outline 하나로만 강조한다. 대화와 판정 Emote는 상태 변경 때만 갱신한다. reduced motion에서는 모든 전환을 즉시 처리한다.

## 8. Voice & Brand

한국어 시스템 문구는 짧고 구체적으로 쓴다. 고정 명칭은 `코드리아`, `넙죽이`, `관리자 키보드`, `관리자 권한`, `기술 부채`다. 레퍼런스 이미지의 지역·적·재화·레벨 문구를 제품 설정으로 가져오지 않는다.

## 9. Anti-patterns

- 중앙 월드를 가리는 대형 상시 패널
- 하단 전체 높이의 과도한 대화창
- 스킬 버튼을 한 줄에 압축해 키캡을 읽을 수 없게 만드는 구성
- 런타임 스크립트가 Inspector의 sprite, anchor, color를 일괄 덮어쓰는 구조
- 같은 위치에 초상 프레임이나 대화 프레임을 중복 적용하는 구성
- 참고 화면의 캐릭터, 지도, 아이콘, 퀘스트와 정확한 좌표를 그대로 복사하는 구성
