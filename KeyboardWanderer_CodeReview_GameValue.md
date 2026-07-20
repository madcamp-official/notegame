# Keyboard Wanderer / Ninja Adventure 코드 리뷰 및 게임 가치 제안

## 1. 검토 범위

- 대상: 업로드된 `Scripts.zip`
- 규모: C# 53개 파일, 총 16,931줄
- 주요 계층: `Core`, `Gameplay`, `Networking`, `Runtime`, `Presentation`, `World`, `Demo`
- 가장 큰 파일:
  - `Demo/KeyboardWandererDemoController.cs` — 4,203줄
  - `Core/DeterministicRegionGenerator.cs` — 1,662줄
  - `Networking/GameApiClient.cs` — 1,617줄
  - `Gameplay/GameplayModels.cs` — 1,036줄
  - `Gameplay/LocalTurnService.cs` — 731줄
  - `Gameplay/RuleEngine.cs` — 701줄

이번 검토는 정적 코드 리뷰다. Unity 씬·프리팹·애니메이터·ScriptableObject의 실제 설정, 서버 구현, 패키지 버전, 플레이 모드 빌드, Profiler 측정값은 제공되지 않았다. 따라서 프레임 비용은 코드 경로상 발생 가능성과 할당 구조를 기준으로 판단했으며, 씬 작성 상태에 따라 일부 표현 계층 이슈의 재현 정도는 달라질 수 있다.

---

## 2. 종합 판단

현재 코드는 **서버 권위, 결정론적 월드, 규칙과 서사의 분리**라는 방향은 명확하다. 그러나 로컬 규칙 경로에는 캠페인 진행과 정식 엔딩을 막는 논리 결함이 있고, Undo·RNG·Restore 보상 처리에도 상태 일관성 문제가 있다. 현재 상태는 콘텐츠 확장보다 먼저 규칙 정확성을 고정해야 하는 버티컬 슬라이스 단계다.

출시 판단 기준으로는 다음과 같다.

- 규칙 아키텍처 방향: 타당함
- 로컬 캠페인 완주 가능성: 차단 결함 존재
- 저장·복구 신뢰성: 불충분
- 프레임 안정성: 메인 루프의 반복 깊은 복사 때문에 위험
- 게임성: 키보드 스킬 콘셉트는 차별화되지만, 현재 대부분 독립 버튼형 효과에 머묾
- 회귀 방지: 업로드된 코드에 테스트가 없음

---

## 3. 확인된 강점

### 3.1 규칙과 생성형 서사의 권위 분리

`RuleEngine`이 합법성·D20·자원·좌표·엔딩을 결정하고, GM 내러티브는 이미 커밋된 이벤트를 표현하는 용도로 제한돼 있다. `Gameplay/RuleEngine.cs:99-137`, `Networking/GmNarrativeClient.cs:98-176`의 의도는 명확하다. 생성형 텍스트가 좌표나 규칙을 직접 바꾸지 않는 구조는 유지해야 한다.

### 3.2 결정론적 월드와 레이아웃 검증

시드 기반 월드 생성, `LayoutHash` 검증, 저장 복원 시 동일 월드 재생성 구조가 있다. `Gameplay/LocalRunSaveService.cs:224-233`에서 레이아웃과 캠페인 ID를 확인한다. 재현 가능한 버그 리포트와 시드 챌린지에 유리하다.

### 3.3 로컬 턴의 clone-and-swap

`Gameplay/LocalTurnService.cs:155-160`에서 작업 상태를 복제하고, 커밋 전 `Spatial.Validate`를 수행한다(`237-243`). 검증 실패 시 원본 상태를 보존하려는 방향은 맞다.

### 3.4 로컬·서버 실행 경계

`ITurnGateway`, 서버/로컬 어댑터, 프레젠테이션 정규화 계층이 존재한다. 네트워크 DTO가 모든 화면 계층으로 직접 누출되지 않도록 한 점은 확장에 유리하다.

### 3.5 일부 기록의 상한

GM 로그, 사실, 메모리, Restore/Undo 기록에 상한이 있다. `Gameplay/GameplayModels.cs:882-936`의 bounded history는 장기 런에서 무한 누적을 막는다. 다만 idempotency 캐시는 예외다.

---

## 4. P0: 콘텐츠 추가 전 반드시 수정할 문제

## 4.1 관리자 권한 II의 전투 경로가 완료되지 않음

**근거**

- 관리자 권한 II는 시드에 따라 `Combat/Delete` 또는 `Negotiation/Connect`로 선택된다: `Gameplay/CampaignCatalog.cs:237-265`.
- 권한 비트 완료 판정은 `ADMIN_ACCESS_CANDIDATE_INSPECTED`, `ADMIN_ACCESS_CANDIDATE_REPAIRED`, `ENTITY_REMOVED`, `CONNECTION_CREATED`만 인정한다: `Gameplay/CampaignDirector.cs:424-432`.
- 단일 Delete로 적을 쓰러뜨리면 `ENEMY_DEFEATED`가 발생하고 `ENTITY_REMOVED`는 발생하지 않는다: `Gameplay/RuleEngine.cs:524-535`.
- 전체 업로드 코드에서 `ENTITY_REMOVED`를 실제로 생성하는 로컬 규칙 코드는 없다.

**재현성**

`KeyboardWandererRunSessionController`의 새 게임 시드는 `20260717 + counter`다. 새 게임 기준 세 번째 런인 seed `20260720`은 권한 II에서 전투/Delete 경로가 선택된다. 이 경로에서는 지정 적을 처치해도 비트가 완료되지 않는다.

**영향**

- 특정 시드에서 캠페인 진행 정지
- 플레이어는 올바른 행동을 했는데도 목표가 갱신되지 않음
- 시드 반복 플레이에 대한 신뢰 붕괴

**수정**

문자열 prefix 비교 대신 타입이 있는 도메인 이벤트를 사용한다. 최소 수정으로는 권한 II의 전투 바인딩에서 지정 대상의 `ENEMY_DEFEATED`를 인정해야 한다. `ENTITY_REMOVED`와 `ENEMY_DEFEATED`의 의미를 합칠지는 별도로 결정해야 한다.

---

## 4.2 로컬 규칙에서 모든 정식 엔딩이 사실상 도달 불가능

**근거**

- `finale.freedom`, `finale.threat`는 `EntityKind.Prop`, 비적대 상태로 생성된다: `Gameplay/LocalTurnService.cs:515-521`.
- Delete는 먼저 `Enemy && IsHostile`만 허용한다: `Gameplay/RuleEngine.cs:313-329`.
- 따라서 그 아래의 `finale.threat/freedom` 관리자 권한 검사(`322-325`)까지 도달하지 못한다.
- 모든 정식 엔딩은 `threatActive == false` 또는 `freedomActive == false`를 요구한다: `Gameplay/CampaignDirector.cs:147-186`.

정식 엔딩별 조건은 다음과 같다.

- `ENDING_REWEAVE_TOGETHER`: 위협 비활성
- `ENDING_OPEN_FRONTIER`: 위협 비활성
- `ENDING_KEEP_THE_PROMISE`: 위협 비활성
- `ENDING_CUT_THE_CYCLE`: 위협과 자유 핵 모두 비활성
- `ENDING_PRESERVE_THE_SCARS`: 자유 핵 비활성
- `ENDING_WALK_BETWEEN_WORLDS`: 위협 비활성

**영향**

로컬 런에서는 최종 배치 비트가 완료되지 않고, 턴 제한에서 긴급 이탈 엔딩으로 종료될 가능성이 사실상 고정된다.

**수정**

`EntityKind`로 행동 가능성을 추론하지 말고 capability를 둔다.

- `CanDelete`
- `CanConnect`
- `CanCopy`
- `CanRestore`
- `RequiresAdminAccess`
- `GrantsRewardOnDefeat`

`finale.threat`와 `finale.freedom`은 적이 아니라 시스템 컴포넌트이므로, `CanDelete=true`, `RequiresAdminAccess=3`인 별도 `SystemNode` 또는 Prop capability로 처리하는 편이 의미상 맞다.

---

## 4.3 `CompanionBond`가 런 동안 변하지 않음

**근거**

- 초기값은 20이다: `Gameplay/GameplayModels.cs:863-869`.
- `CampaignDirector.ApplyMetricDelta`는 `bond=0`으로 시작하고 모든 능력 분기에서 값을 바꾸지 않은 채 적용한다: `Gameplay/CampaignDirector.cs:232-256`.
- `ENDING_KEEP_THE_PROMISE`는 45 이상, `ENDING_WALK_BETWEEN_WORLDS`는 35 이상을 요구한다: `Gameplay/CampaignDirector.cs:166-184`.
- Connect는 NPC affinity만 +1 한다: `Gameplay/RuleEngine.cs:673-679`.

**영향**

두 엔딩은 finale 삭제 문제를 고친 뒤에도 도달할 수 없다.

**수정**

- 특정 동행 NPC를 명시하고 `CompanionBond`를 해당 NPC affinity·약속 상태·구조 행동에서 파생한다.
- 또는 `CompanionBond`를 별도 메트릭으로 유지하되 Connect, Restore, 약속 이행, NPC 구출에서 명시적 delta를 준다.
- UI에는 숫자만 아니라 “신뢰가 오른 이유/다음 단계”를 표시한다.

---

## 4.4 Undo의 시간 모델이 혼합되어 상태 일관성이 깨짐

**근거**

- Undo는 최근 두 `ReversibleTurnRecord`의 기계 상태를 복원한다: `Gameplay/RuleEngine.cs:583-652`.
- 그러나 `CurrentTurn`은 실제로 2 감소한다: `Gameplay/LocalTurnService.cs:166-176`.
- 캠페인 비트 완료, canonical facts, 기술 부채, NPC 기억, 메트릭, RNG 진행도는 완전히 되돌리지 않는다.
- Undo인 경우 `CampaignDirector.ProcessCommittedTurn` 자체를 호출하지 않는다: `Gameplay/LocalTurnService.cs:173-176`.
- 따라서 설계돼 있던 Undo 비용인 안정성 +1, 부채 +3, 신뢰 -1도 적용되지 않는다: `Gameplay/CampaignDirector.cs:241-248`.

**발생 가능한 결과**

- 완료 비트의 `ResolvedTurn`이 현재 턴보다 큰 상태
- 같은 턴 번호가 로그에 다시 사용됨
- 캠페인 마감·TurnPressure는 뒤로 가지만 서사 사실은 남음
- Undo가 의도된 기술 부채 비용을 회피함
- Restore 기록의 나이 계산이 음수가 될 수 있음

**수정 방향은 둘 중 하나만 선택해야 한다.**

1. **보상형 Undo 권장**: `CurrentTurn`과 버전은 계속 단조 증가한다. 최근 두 명령의 허용된 기계 효과만 상쇄하고, `UNDO_COMPENSATED` 이벤트 및 부채를 추가한다.
2. **진짜 타임라인 되감기**: 캠페인 상태, RNG, 로그, 메트릭, 사실, NPC 기억, 이벤트 스트림까지 완전 스냅샷 또는 브랜치로 복원한다.

현재의 “기계 상태만 복원하면서 턴 번호는 뒤로 돌리는” 혼합 모델은 피해야 한다.

---

## 4.5 저장 후 RNG가 재현되지 않음

**근거**

- `SeededD20Source`는 시드와 skip 횟수로 RNG를 복원한다: `Gameplay/RuleEngine.cs:12-25`.
- 저장 로드는 `currentTurn`만큼 skip한다: `Gameplay/LocalRunSaveService.cs:298-299`.
- Undo는 D20을 한 번 소비한 뒤 턴 번호를 2 낮춘다.

따라서 Undo 이후 저장·로드하면 “총 D20 소비 횟수”와 `CurrentTurn`이 달라져 과거 주사위 구간이 반복되거나 건너뛴다. 저장/로드로 결과를 예측하거나 재굴림하는 악용도 가능하다.

**수정**

- `RollCount` 또는 PRNG 내부 state를 저장한다.
- `System.Random`의 구현 세부에 의존하지 말고, PCG/xoshiro 등 명시적으로 고정한 PRNG와 버전을 사용한다.
- save/load 전후 동일 명령 trace가 같은 결과를 만드는 회귀 테스트를 둔다.

---

## 4.6 Restore로 적 처치 보상을 반복 획득할 수 있음

**근거**

- Delete 전에 적 스냅샷을 Restore 원장에 기록한다: `Gameplay/RuleEngine.cs:524-528`.
- 처치 시 XP/Gold를 지급한다: `Gameplay/RuleEngine.cs:655-664`.
- Restore 원장에는 “이 적의 보상을 이미 받았음”을 나타내는 영구 플래그가 없다: `Gameplay/GameplayModels.cs:931-950`.
- Undo의 irreversible 처리와 Restore 허용은 별도다.

**악용 순서**

1. 적 처치
2. Restore로 적을 되살림
3. 다시 처치
4. XP/Gold 반복 획득

**추가 불일치**

`SelectAll` 처치는 `EnemiesDefeated`만 증가시키고 XP/Gold·`IRREVERSIBLE_ENTITY`를 처리하지 않는다: `Gameplay/RuleEngine.cs:461-479`. 단일 Delete는 `RewardEnemy` 내부와 호출부에서 `ENEMY_DEFEATED`를 중복 생성한다(`524-535`, `655-664`).

**수정**

`DefeatEnemy` 단일 함수로 다음을 원자적으로 처리한다.

- 비활성화
- 최초 처치 여부 확인
- 보상 원장 기록
- XP/Gold 지급
- 처치 카운터
- 도메인 이벤트 1회 발행
- Undo/Restore 정책 적용

적의 복원은 허용하되 이미 받은 보상은 다시 주지 않는 방식이 키보드 콘셉트와 가장 잘 맞는다.

---

## 4.7 매 프레임 전체 `RunView`를 여러 번 깊은 복사함

**근거**

- `CurrentView` 호출마다 `new RunView(_state)`를 만든다: `Gameplay/LocalTurnService.cs:51`.
- `RunView`는 기술 부채, 권한 기록, 비트, 사실, NPC 메모리, 엔딩, 로그, 인벤토리, 연결, 모든 활성 엔티티를 새 리스트/객체로 복사한다: `Gameplay/GameplayModels.cs:546-655`.
- 메인 Update에서 다음 경로가 매 프레임 호출된다.
  - `PublishPresentationState`: `Demo/KeyboardWandererDemoController.cs:294-327`
  - `UpdateAnimatedVisuals`: `1847-1858`
  - `UpdateCameraFollow`: `2272-2289`
  - `UpdateDecorationOcclusion`: `2791-2797`

정상 게임 화면 구성에서는 60fps 기준 초당 약 240개의 전체 `RunView` 스냅샷 생성 경로가 생긴다. 실제 할당량은 엔티티 수와 기록 수에 비례한다.

**영향**

- 지속적인 GC 할당
- 저사양·모바일에서 프레임 스파이크
- 입력·카메라·애니메이션의 체감 지연
- 서버 모드에서도 fallback `RunView`를 먼저 생성하는 낭비

**수정**

- `RunView`를 version별로 캐시하고 커밋 시에만 무효화한다.
- 카메라·애니메이션에는 월드 원점, 플레이어 위치, 엔티티 렌더 상태 같은 경량 구조체만 전달한다.
- `PublishPresentationState`는 매 Update가 아니라 상태 변경 이벤트에서 호출한다.
- 한 프레임/한 작업에서 하나의 view만 만들고 하위 호출에 전달한다.
- Ambient Wander를 권위 상태에서 분리한 뒤 캐시 일관성을 확보한다.

---

## 5. P1: 높은 우선순위 문제

## 5.1 Ambient NPC Wander가 버전 없이 권위 상태를 변경

`Gameplay/LocalTurnService.cs:67-103`은 `SpatialIndex` 안의 NPC 좌표를 바꾸지만 `Version`을 증가시키지 않고 저장하지 않는다. `Demo/KeyboardWandererDemoController.cs:301-305`에서 오프라인 플레이 중 약 1.8초마다 실행된다.

NPC가 blocking이면 동일한 `ExpectedRunVersion`에서 경로·사거리·타일 점유가 바뀐다. 낙관적 동시성 계약과 idempotency 응답의 의미가 깨진다. `_wanderOrigins`, `_wanderStep`도 저장되지 않고, 방향 계산에 `Guid.GetHashCode()`를 사용한다(`LocalTurnService.cs:88`).

권장 방식은 배회를 **표현 전용 애니메이션**으로 만드는 것이다. 규칙상 위치가 필요하다면 simulation tick, version, event, RNG state, 저장을 모두 포함해야 한다.

## 5.2 idempotency 캐시가 무제한이며 전체 과거 `RunView`를 보존

`LocalTurnService`의 `_idempotency`는 eviction이 없다: `Gameplay/LocalTurnService.cs:46`, `107-125`, `250`, `363`, `454`. 각 값은 `TurnResponse`와 당시의 깊은 `RunView`를 잡고 있다. 안전 이동은 캠페인 턴을 소비하지 않으므로 캐시는 사실상 무한 증가할 수 있다.

- 최근 N개만 보존하는 LRU
- compact response snapshot
- save/load 시 필요한 idempotency key 또는 커밋 ID 유지
- 요청 fingerprint는 문자열 연결 대신 canonical serialization/hash 사용

으로 변경해야 한다.

## 5.3 저장 파일 교체가 원자적이지 않음

`Gameplay/LocalRunSaveService.cs:21-31`은 temp를 쓴 뒤 기존 파일을 삭제하고 temp를 이동한다. 삭제와 이동 사이에 종료되면 유일한 저장 파일을 잃는다.

또한:

- backup/checksum 없음
- schema가 정확히 같지 않으면 모두 거부: `224-230`
- 손상 파일 격리·복구 UI 없음
- 저장된 width/height에 상한이 없어 비정상 값으로 큰 월드 할당 가능: `224-229`, `Core/DeterministicRegionGenerator.cs:70-75`
- enum, metric, turn, entity position, player 존재, 문자열/리스트 길이 검증 부족
- 로드 후 `Spatial.Validate`를 수행하지 않음

새 파일을 durable write한 뒤 `File.Replace` 또는 세대별 파일 + `.bak` + checksum을 사용하고, untrusted DTO 검증 후 상태를 구성해야 한다.

## 5.4 네트워크 version을 `long`에서 `int`로 축소

`TurnRequest.ExpectedRunVersion`은 `long`인데 `Networking/ServerTurnGateway.cs:50`, `64`에서 `int`로 캐스팅한다. 계약 타입을 end-to-end로 통일해야 한다.

## 5.5 abandoned 서버 런의 resume 실패를 성공처럼 취급

`Runtime/KeyboardWandererRunSessionController.cs:89-105`에서 abandoned 런을 resume한 뒤 실패해도 기존 abandoned snapshot으로 `ServerOnline=true` 결과를 반환한다. 이어지는 턴이 거부될 가능성이 높다.

resume 성공을 필수 조건으로 하고, 실패 시 서버 포인터를 정리한 뒤 검증된 로컬 fallback 또는 명시적 오류 화면으로 전환해야 한다.

## 5.6 서버 snapshot과 로컬 fallback이 같은 런인지 검증하지 않음

Continue는 로컬 저장과 서버 런을 독립적으로 읽은 뒤 `restored ?? CreateDemo(seed)`와 서버 snapshot을 결합한다: `Runtime/KeyboardWandererRunSessionController.cs:85-104`. run ID, world seed, layout hash, campaign/rules version의 결합 검증이 없다.

온라인 UI와 월드 코드 상당 부분이 `_service.CurrentView`와 `_serverRun`을 혼용한다: `Demo/KeyboardWandererDemoController.cs:1414-1477`. 서버 상태와 로컬 읽기 모델이 다르면 목표·월드·엔티티가 분리될 수 있다.

## 5.7 pending 상태가 예외에서 고정될 수 있음

`RunSessionController`, `TurnCoordinator`, `ServerTurnGateway`, Demo의 서버 coroutine은 `try/finally`가 없다. 네트워크/파싱/콜백 예외가 나면 `IsPending`, `_serverPending`이 true로 남을 수 있다. 이미 pending인 세션 시작 함수는 callback 없이 `yield break`한다: `Runtime/KeyboardWandererRunSessionController.cs:32-35`, `78-81`.

완료 콜백 exactly-once와 pending reset을 `finally`로 보장해야 한다.

## 5.8 Connect 요청에 의미 없는 destination이 포함됨

`Runtime/KeyboardWandererSelectionController.cs:119-132`는 마지막 클릭 좌표를 저장하고, `KeyboardWandererTurnCoordinator.cs:47-50`은 Connect에도 destination을 넣는다. 로컬 RuleEngine은 이를 무시하지만 서버는 받는다. 계약을 단순화하려면 destination은 Move/Copy에만 포함해야 한다.

## 5.9 경로 탐색이 160×160 월드에 비효율적

`Core/GridPathfinder.cs:26-65`는 open set을 `List`로 관리하고, 매 반복에서 최솟값 선형 탐색 및 `Contains`를 수행한다. 안전 이동의 각 노드마다 `IsSafeTravelTile`이 모든 엔티티를 순회한다: `Gameplay/LocalTurnService.cs:277-304`. 적은 최대 네 개의 A*를 각각 실행한다: `LocalTurnService.cs:605-624`.

권장:

- binary heap priority queue
- 배열 기반 `gScore/closed/cameFrom` 버퍼 재사용
- hostile influence map과 blocking raster 캐시
- 적 페이즈당 플레이어 기준 reverse BFS/flow field 1회
- path search node budget

## 5.10 `SpatialIndex`의 반복 할당과 불변식 방어 부족

- `FindAt`마다 새 리스트: `Core/SpatialIndex.cs:264-279`
- `TryMove`의 `isWalkable` null 방어 없음: `281-299`
- blocking cell 제거 시 해당 entity ID인지 확인하지 않고 null 처리: `506-518`
- runtime snapshot 정렬에서 Guid를 문자열로 변환: `421-427`

non-alloc query, caller-provided buffer, blocking owner 검증, 구조체 비교로 바꾼다.

## 5.11 문자열 이벤트 프로토콜이 규칙 계층 내부까지 침투

`"ENEMY_DEFEATED:"`, `"CAMPAIGN_BEAT_COMPLETED:"`, `"ENTITY_REMOVED:"` 같은 문자열을 여러 클래스가 prefix로 해석한다. 관리자 권한 II 버그가 이 구조에서 발생했다.

내부에서는 다음과 같은 타입 이벤트를 사용하고 네트워크/로그 경계에서만 문자열 또는 DTO로 직렬화해야 한다.

- `EnemyDefeated(entityId, rewardGranted)`
- `EntityRemoved(entityId, reason)`
- `ConnectionCreated(firstId, secondId)`
- `CampaignBeatCompleted(beatId)`
- `MetricChanged(metric, delta)`

## 5.12 대형 클래스에 책임이 과도하게 집중

- `KeyboardWandererDemoController.cs` 4,203줄: 세션, 입력, 선택, 서버/로컬 턴, 월드 렌더, 카메라, 오디오, 미니맵, 내러티브, 저장, UI 모두 담당
- `GameApiClient.cs` 1,617줄: DTO, 수동 JSON, HTTP, RLE 파싱, 오류 해석 모두 담당
- `DeterministicRegionGenerator.cs` 1,662줄: 생성 단계와 검증을 한 파일에 집중

권장 분리:

- `RunSessionOrchestrator`
- `TurnSubmissionPresenter`
- `WorldRenderController`
- `MinimapRenderer`
- `ObjectivePresenter`
- `SceneActionPlayer`
- `GameApiTransport`, `GameApiDtos`, `GameApiSerializer`, `GameApiResponseValidator`
- `AreaGenerator`, `RouteGenerator`, `TileGenerator`, `PlacementGenerator`, `GenerationValidator`

## 5.13 업로드된 코드에 테스트가 없음

최소한 다음 자동화가 필요하다.

- 다수 시드에서 모든 필수 비트 완료 가능성
- 각 정식 엔딩 reachability
- save/load 전후 명령 trace 동일성
- Undo의 turn/version/캠페인 불변식
- Restore 반복 보상 방지
- idempotency replay와 conflict
- generator golden hash
- 손상 save 및 서버 JSON fuzz
- idle frame allocation 및 턴 커밋 성능 예산

---

## 6. 게임 설계 관점의 핵심 진단

## 6.1 차별화 포인트는 강하지만 현재 효과는 대부분 독립 버튼형

키보드 스킬의 이름은 분명한 차별점이다.

- Copy
- Delete
- Connect
- Restore
- Undo
- Search
- Select All

하지만 현재 규칙은 대체로 다음 수준에 머문다.

- Delete = 단일 공격
- Select All = 범위 공격
- Copy = 오브젝트 하나 생성
- Connect = 관계 문자열 하나 추가
- Restore = 과거 snapshot 복구
- Undo = 두 snapshot 복원
- Search = 이벤트/단서 생성

스킬끼리의 조합, 대상의 capability, 의존성, 전이 효과가 거의 없다. 따라서 이름은 새롭지만 플레이 감각은 일반 RPG 행동을 키보드 용어로 바꾼 것에 가까워질 위험이 있다.

## 6.2 많은 상태값이 실제 피드백 루프를 만들지 못함

### 경험치·골드

`Experience`와 `Gold`는 처치 시 증가하고 저장되지만, 업로드된 코드에서 소비·성장·상점·능력치에 사용되지 않는다. 플레이어가 얻어도 다음 의사결정이 바뀌지 않는다.

### 기술 부채

`DeferredConsequenceType`으로 `DUPLICATED_STATE_DRIFT`, `REMOVED_DEPENDENCY_BACKFLOW`, `COMPENSATION_CONFLICT` 등을 기록하지만 실제로 발동하는 코드가 없다. 현재는 엔딩 조건용 숫자와 로그에 가깝다.

### NPC affinity와 CompanionBond

Connect로 affinity가 오르지만 도움, 대사 분기, 전투 지원, 경로 해제, 엔딩 bond로 연결되지 않는다.

### IntentAlignment

`RulePreparation.WithIntentAlignment`은 안전 이동에서 0으로만 쓰이고 일반 행동에서는 적용되지 않는다. `IntentAlignmentLabel` 함수도 호출되지 않는다. 현재 규칙상 사실상 죽은 개념이다.

### ConsequenceBudget

D20에서 계산돼 응답에 포함되지만 로컬 규칙에서 실제 합병증을 구매하거나 배치하지 않는다. 숫자만 존재한다.

## 6.3 D20 판정의 체감 차이가 약함

`PartialSuccess`도 주효과를 전부 적용하고 공통적으로 `Exposed`만 붙인다: `Gameplay/RuleEngine.cs:203-247`.

현재 기본 난이도/수정치에서 주효과 적용 확률은 다음과 같다.

| 능력 | Partial 이상으로 주효과 적용 |
|---|---:|
| Move | 95% |
| Search | 95% |
| Interact | 95% |
| Copy | 90% |
| Connect | 90% |
| Restore | 85% |
| Delete | 80% |
| Undo | 80% |
| Select All | 75% |

XP나 장비가 수정치를 바꾸지 않으므로 이 확률은 런 전체에서 거의 고정된다. Partial과 Success의 기계적 차이가 작기 때문에 D20은 핵심 의사결정보다 연출 숫자로 느껴질 수 있다.

## 6.4 160×160 월드의 탐험 가치가 안전 이동에 의해 약해질 수 있음

안전 경로가 있으면 긴 거리를 D20과 캠페인 턴 없이 한 번에 이동한다: `Gameplay/LocalTurnService.cs:261-305`, `418-455`. 편의성은 높지만, 대형 맵의 거리·경로·위험·발견이 의사결정으로 전환되지 않으면 160×160 크기가 시각적 비용만 남는다.

## 6.5 캠페인 페이싱 모델이 비트 진행과 분리됨

캠페인 phase는 턴 수 임계값으로 계산된다: `Gameplay/CampaignDirector.cs:47-89`. 반면 비트는 현재 목표를 성공할 때마다 즉시 전진한다. 9개 비트를 일찍 해결해도 `LocalTurnService.cs:211-220`의 `finaleWindowOpen`이 기본 턴 28 이전에는 종료를 막는다.

결과적으로:

- 현재 비트는 최종 배치인데 phase는 초중반으로 표시될 수 있음
- 필수 비트를 일찍 소진한 뒤 28턴까지 반복 행동을 요구할 수 있음
- 40턴 압박이 긴장감보다 filler로 작동할 수 있음

phase는 현재 비트에서 파생하고, 턴 수는 각 비트의 deadline/압박으로 사용하는 편이 자연스럽다.

## 6.6 엔딩 자격 UI가 실제 조건을 설명하지 않음

`UpdateEndingEligibility`는 Root 진입 가능 여부와 최소 완료 비트만 본다: `Gameplay/CampaignDirector.cs:189-197`. 실제 엔딩 선택은 연결 구조, 활성 컴포넌트, 메트릭을 훨씬 많이 검사한다. UI는 “가능”으로 보이지만 최종 판정은 fallback일 수 있다.

각 엔딩에 하나의 공통 predicate를 두고 다음을 함께 반환해야 한다.

- 현재 가능 여부
- 만족한 조건
- 미충족 조건 2~3개
- 상충되는 선택

---

## 7. 게임 가치를 높이는 우선 제안

## 7.1 스킬을 조합 가능한 세계 조작 언어로 만들기

가장 높은 가치의 개선이다. 엔티티에 capability와 상태 태그를 부여하고 스킬 간 상호작용을 만든다.

예시 capability:

- `Cloneable`
- `Deletable`
- `Restorable`
- `Connectable`
- `Conductive`
- `Volatile`
- `Protected`
- `DependencyRoot`
- `MemoryBearing`
- `UnstableClone`

예시 조합:

- Search로 적 서비스의 parent dependency를 발견
- Copy로 손상된 bridge process를 복제
- Connect로 복제본을 전력 노드에 연결
- Delete로 원본 corruption을 제거
- Restore로 NPC 기억을 되돌리되 과거 bug도 함께 복원
- Undo로 최근 조합을 취소하지만 paradox debt가 발생

핵심은 “스킬 하나가 정답”이 아니라, 세계의 상태와 의존성을 읽고 2~4개의 명령으로 해법을 만드는 것이다.

## 7.2 기술 부채를 실제로 역류시키기

현재 기록만 되는 부채를 반복 플레이의 핵심 압박으로 바꾼다.

예시 threshold:

- 25: Copy clone이 일정 확률이 아니라 결정론적으로 drift
- 50: 삭제된 dependency의 대체 프로세스가 hostile로 생성
- 75: Undo paradox ghost가 이전 행동을 재연

각 `TechnicalDebtEntry`는 실제 target/dependency와 연결하고, Restore는 임의의 첫 항목이 아니라 관련 부채만 해결하도록 한다. 플레이어는 부채 그래프를 보고 “당장 편한 강제 편집”과 “나중의 복구 비용”을 판단해야 한다.

## 7.3 Partial Success를 능력별 합병증으로 분리

공통 `Exposed` 대신 `ConsequenceBudget`을 실제로 사용한다.

- Copy: 복제본이 2턴 뒤 붕괴하거나 원본 상태를 공유
- Delete: 인접 dependency에 collateral damage
- Connect: 링크가 양방향이라 hazard도 전달
- Restore: 이전 상태와 함께 과거 결함도 복귀
- Undo: 기억·관계는 남아 paradox debt 생성
- Search: 진짜 단서와 불완전한 단서를 함께 제시
- Select All: 아군/환경에 과부하 또는 focus debt

합병증은 서버/로컬 모두 동일한 seed와 budget으로 결정해 재현 가능해야 한다.

## 7.4 경험치와 골드를 의사결정에 연결

둘을 유지하려면 최소 한 가지 반복 루프가 필요하다.

- XP: 키보드 스킬별 mastery node 해금
- Gold: camp에서 focus cell, 탐색 도구, 단기 패치 구매
- 선택형 업그레이드: Copy 안정성 vs 사거리, Delete 피해 vs 부채, Connect 거리 vs 위험 전이

전투를 강요하지 않으려면 비전투 해결에도 동등한 성장 자원을 지급해야 한다. 그렇지 않으면 XP/Gold를 제거해 시스템 복잡도를 줄이는 편이 낫다.

## 7.5 NPC를 관계 시스템으로 전환

한 명 이상의 반복 등장 동료를 지정한다.

- 요청·약속·금기
- affinity, trust, fear, obligation을 구분
- 전투 지원, 판정 보정, 지름길, 추가 엔딩 조건
- 플레이어의 과거 Delete/Restore/Undo를 기억하고 이후 행동 변경

`CompanionBond`는 이러한 상태에서 파생해야 한다. 숫자만 올리는 방식보다 “어떤 약속을 지켰는가”가 엔딩에 직접 연결되는 편이 서사 가치가 높다.

## 7.6 이동을 탐험 선택으로 재설계

안전 이동 편의는 유지하되 다음을 추가한다.

- 발견한 road/camp만 장거리 이동
- fog-of-war 또는 정보 레이어
- 경로 미리보기: 시간, 위험, 사건 가능성
- 알려진 안전 구간은 즉시 이동
- 미지 구간은 staging event 또는 선택 이벤트
- active encounter 안에서는 짧은 전술 이동

이렇게 하면 160×160 맵이 단순 배경이 아니라 경로 계획 자원이 된다.

## 7.7 키보드 테마의 적 아키타입 설계

- Deadlock Pair: 두 적의 연결을 끊거나 순서대로 처리
- Cache Slime: 일정 조건에서 자신을 Copy
- Legacy Daemon: 바로 Delete하면 부채가 크고, 환경 Restore로 약화 가능
- Root Process: parent service가 남아 있으면 재생성
- Corrupt Relay: Connect 경로를 따라 피해/상태 전파

적 의도를 턴 전에 표시해야 한다. 플레이어는 피해량보다 의존성 그래프와 다음 명령 순서를 판단하게 된다.

## 7.8 Root System을 명시적 결말 퍼즐로 만들기

현재 7개 finale component를 실제 보드로 시각화한다.

- 연결/비활성/보호 상태를 그래프로 표시
- 각 노드의 capability와 관리자 권한 요구 표시
- “현재 가장 가까운 결말”과 미충족 조건 제공
- `Link`, `Isolate`, `Purge`, `Preserve` 같은 의미 있는 조작을 기존 키보드 스킬에 매핑

모든 엔딩 predicate는 UI, 규칙, 테스트가 같은 구현을 사용해야 한다.

## 7.9 시드 다양성을 경로 선택 이상으로 확장

현재 핵심 캠페인은 9개 고정 비트이고 관리자 권한 경로 세 개가 이진 선택되는 정도다. 시드가 다음을 바꾸게 한다.

- objective 대상과 dependency 구조
- faction 동맹/적대
- NPC motive와 약속
- debt mutation
- boss rule set
- route blockage
- finale component 초기 상태

결정론을 이용해 daily/weekly seed challenge, 명령 타임라인 공유, 실패 재현을 제공할 수 있다.

## 7.10 입력·접근성·피드백

- Input Actions와 rebinding
- gamepad/touch 지원
- 파괴적 행동 확인 및 선택 취소
- 대상 hover 시 사거리·focus·적용 확률·가능한 합병증 표시
- 텍스트 크기, 고대비, 색각 보조, 화면 흔들림, 애니메이션 속도
- 오디오 믹서, biome/combat layer, crossfade, 위험 stinger

현재 `KeyboardWandererInputRouter.cs:23-78`은 키 polling과 mouse click에 고정돼 있고, 설정 slider는 매 변경마다 `PlayerPrefs.Save()`를 호출한다(`Runtime/KeyboardWandererSettingsController.cs:39-82`).

---

## 8. 권장 구현 순서

## Gate 0 — 규칙 정확성

1. 문자열 이벤트를 타입 이벤트로 교체
2. 관리자 권한 II 전투 evidence 수정
3. finale component capability 수정
4. CompanionBond 경로 구현
5. 모든 엔딩 reachability 테스트
6. Undo 의미를 보상형 또는 완전 rewind 중 하나로 확정
7. RNG state 저장
8. 적 처치/보상/Restore 정책 통합

이 단계가 통과되기 전에는 새 콘텐츠를 추가해도 회귀와 막힘이 누적된다.

## Gate 1 — 안정성과 프레임

1. `RunView` version cache 및 이벤트 기반 UI
2. ambient wander 표현 전용화
3. bounded idempotency cache
4. 경로 탐색 heap/flow field
5. 원자적 save + backup + validation
6. 네트워크 long version, resume, run binding 검증
7. pending state `finally`

## Gate 2 — 핵심 게임 루프

1. capability 기반 스킬 조합
2. 능력별 partial consequence
3. 기술 부채 실제 발동
4. XP/Gold 또는 해당 시스템 제거
5. Root System 결말 보드

## Gate 3 — 콘텐츠와 리플레이 가치

1. NPC 동료·약속
2. dependency 기반 적 아키타입
3. 탐험/경로 정보 시스템
4. 시드 변형 확대
5. 접근성, 오디오, 피드백, 텔레메트리

---

## 9. 완료 기준으로 사용할 자동 검증

- 연속 다수 시드에서 모든 필수 캠페인 비트가 완료 가능
- 여섯 정식 엔딩 각각에 도달하는 최소 명령 trace 존재
- save 직전과 load 직후 동일 입력 trace가 동일 D20·이벤트·해시 생성
- Undo 이후 turn/version/ResolvedTurn/ledger 불변식 위반 없음
- Restore 반복으로 보상 증가 불가
- 단일 처치와 Select All 처치가 동일한 보상 정책 사용
- idle 플레이 화면에서 게임 코드 유발 GC allocation 0B 목표
- 경로 탐색과 턴 커밋에 플랫폼별 성능 예산 설정
- 손상·구버전 save에서 backup 복구 또는 명확한 실패
- 서버 run ID, seed, layout hash, rules version이 로컬 캐시와 일치하지 않으면 결합 거부
- idempotency key replay는 정확히 같은 결과, 다른 payload는 conflict

---

## 10. 최종 우선순위

### 즉시 수정

- 관리자 권한 II event mismatch
- finale 삭제 불가
- CompanionBond 불변
- Undo/RNG 불일치
- Restore 보상 파밍 및 Select All 처치 불일치
- 매 프레임 `RunView` 깊은 복사

### 그 다음

- 저장 원자성·검증
- ambient wander version 문제
- idempotency memory
- 서버/로컬 run 결합 검증
- pathfinding
- typed event 및 God object 분해

### 게임 가치 상승 작업

- capability와 스킬 조합
- 기술 부채 역류
- 능력별 partial consequence
- NPC 약속/동료
- 탐험 경로 선택
- Root System 결말 퍼즐
- XP/Gold의 실제 쓰임 또는 제거

핵심 방향은 **“키보드 단축키 이름을 붙인 RPG”가 아니라 “세계의 객체·관계·시간을 키보드 명령으로 편집하는 시스템 퍼즐 RPG”**로 규칙을 밀어붙이는 것이다. 현재 코드의 결정론·도메인 상태·복구 원장은 이 방향을 구현할 기반이 있지만, 먼저 P0 규칙 결함과 상태 일관성을 제거해야 한다.
