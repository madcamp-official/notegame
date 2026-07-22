# Ninja Adventure — Source of Truth v4

이 문서는 과거의 일반 생성형 판타지 계약과 UI 레퍼런스가 현재 제품 계약으로 오인되지 않도록 권위를 정리합니다.

## 권위 순서

1. 제품 소유자의 최신 명시적 지시
2. 연결된 Notion의 현재 제품 문서
3. 이 폴더의 v4 문서와 저장소 루트 README
4. PostgreSQL, 서버, Unity의 코드와 자동 테스트
5. 첨부 이미지는 인터페이스 감각에만 사용

충돌하면 상위 항목을 따릅니다. 기존 폴더·namespace의 `KeyboardWanderer`는 기술적 호환 이름일 뿐 제품명이 아닙니다.

## 변경할 수 없는 제품 사실

- 제품명: `Ninja Adventure`
- 세계: `WORLD_CODRIA` / 코드리아
- 주인공: `PROTAGONIST_NUPJUKYI` / 넙죽이
- 유물: `ARTIFACT_ADMIN_KEYBOARD` / 관리자 키보드
- NinjaAdventure `NinjaGreen`: 현재의 임시 표현 에셋일 뿐 캐릭터 정체성이 아님
- 월드: 런 시작 시 한 번 생성·검증·봉인하는 160×160 맵; 이후에는 재생성 없이 이동·활성화만 수행
- 지역 축: `REGION_BUG_FOREST`, `REGION_BUFFER_VILLAGE`, `REGION_DEADLOCK_CITY`, `REGION_DATA_GRAND_LIBRARY`, `REGION_LEGACY_CITADEL`, `REGION_ROOT_SYSTEM`
- 여섯 지역 축과 여섯 물리 바이옴은 독립된 차원
- 저장되는 권위 입력: `MOVE`, `USE_SKILL`, `NARRATIVE_CHOICE`
- 기술: `COPY`, `DELETE`, `CONNECT`, `RESTORE`, `UNDO`, `SEARCH`, `SELECT_ALL`만 사용하며 MOVE는 기술이 아님
- 소비 맥락: `COMBAT`, `INVESTIGATION`, `NEGOTIATION`, `DEPLOYMENT`
- 안전 MOVE: D20과 캠페인 턴을 소비하지 않음; 위험 이동은 조우 활성화와 실제 행동을 분리
- `playerNote`: 선택적 flavor이며 규칙 권위가 없음
- 자유 입력: 문장 자체는 권위 상태가 아니며 서버의 제한된 행동 제안과 Rule Engine 검증을 거쳐 `NARRATIVE_CHOICE` 또는 `USE_SKILL`로만 커밋
- 관리자 권한: `ADMIN_ACCESS_LEVEL_1..3` 정확히 세 단계; 단계별로 서로 다른 영역·맥락의 후보 최소 2개
- 루트 게이트: 권한 3/3과 내부 통제 원인 핵심 단서 모두 필요

## 고정 캠페인 구조

진행은 정확히 아홉 비트입니다.

1. 도착과 관리자 키보드 각성
2. 첫 붕괴 문제
3. 관리자 권한 I
4. 관리자 권한 II
5. 내부 관리자 통제 시스템이 원인임을 확인
6. 기술 부채 역류
7. 관리자 권한 III
8. 루트 시스템 진입
9. 최종 배포와 결말

## 상태와 권위

저장·재개는 `majorChoices`, `regionOutcomes`, `npcRelationships`, `canonicalFacts`, `unresolvedHooks`, `abilityUsageHistory`, `adminAccessAcquisitionHistory`, `technicalDebtEntries`를 보존합니다. 기술 부채는 원인·지연 결과·해소 근거를 가진 ledger이며 일반 성공으로 자동 감소하지 않습니다.

Rule Engine이 geometry, 경로, 점유, 합법성, D20, 자원, 권한, 사실, 부채와 `endingId`를 확정합니다. Gemini는 bounded structured player-action proposal과 확정 결과의 짧은 서술만 제안하며, 최대 1회 repair 후 결정적 폴백을 사용합니다. 비용 기본값은 `gemini-3.1-flash-lite`, 최소 thinking level, 작은 context와 출력입니다.

## 레퍼런스 이미지 경계

큰 중앙 맵, 상단 상태, 우측 정보 레일, 하단 명령 덱, 따뜻한 픽셀 재질과 정보 밀도만 참고할 수 있습니다. 이미지의 세계, 캐릭터, 적, 퀘스트, 문구, 아이콘 의미, 재화와 레벨 배치는 복사하거나 제품 사실로 추론하지 않습니다.

## 금지되는 구현

- 매 턴 LLM이 타일, 경로, POI 또는 지역을 생성하는 구조
- 지역 축과 물리 바이옴을 같은 열거형으로 취급하는 구조
- Attack, Interact, Negotiate, Rest 또는 자유 입력이 서버 검증을 우회해 상태를 직접 바꾸는 구조
- 자연어 메모가 없으면 행동할 수 없는 UI
- LLM이 권한, 핵심 사실, 기술 부채 해소, D20 또는 결말을 직접 확정하는 구조
- `NinjaGreen`이나 레퍼런스 이미지의 콘텐츠를 제품 컨셉으로 사용하는 구조
