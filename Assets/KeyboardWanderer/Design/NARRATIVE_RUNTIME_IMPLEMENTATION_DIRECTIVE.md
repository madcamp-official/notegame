# Ninja Adventure — 자연어 서사·판정·Unity 이벤트 구현 디렉팅

상태: 구현 기준 초안 v1  
작성일: 2026-07-21

## 1. 목표

플레이어는 넙죽이로서 자연어로 자유롭게 말하고 행동한다. 게임은 평소에는 비주얼노벨처럼 NPC 대사, 넙죽이의 독백, 나레이션을 이어간다. 공격, 탐색, 아이템 사용, 조합, 관리자 키보드 스킬처럼 월드에 시각적 결과가 발생하는 턴에만 서버가 Unity 실행용 구조화 이벤트를 추가한다.

핵심 흐름은 다음과 같다.

```text
자연어 입력 + 명시적으로 선택한 아이템/스킬
→ 의도 해석
→ 서버의 소유권·합법성 검증
→ 필요할 때 서버 D20 판정
→ 서버가 기계적 결과 확정
→ LLM이 확정 결과에 맞는 자연어 장면 생성
→ narrative + resolution + unityEvents 응답
```

이 문서는 제품 소유자의 최신 지시를 기록한다. 기존 `SOURCE_OF_TRUTH.md`와 충돌하는 경우 다음 항목은 이 문서가 우선한다.

- 자유 자연어 입력을 주 입력으로 허용한다.
- 고정 40턴 종료 대신 준비된 엔딩 조건의 충족 여부로 결말을 활성화한다.
- 일반 대화는 D20을 요구하지 않는다.
- 공격·아이템·스킬 등 판정 가능한 행동은 서버 규칙으로 처리한다.

## 2. 구현 원칙

### 2.1 LLM의 역할

LLM은 다음만 담당한다.

- 플레이어 자연어의 서사적 의도 제안
- NPC 대사, 넙죽이의 독백, 나레이션 생성
- 서버가 확정한 판정 결과를 자연스럽게 장면으로 표현
- 다음 대화나 행동으로 이어질 선택지 제안

LLM은 다음을 확정할 수 없다.

- D20 값과 성공 단계
- 체력과 피해량
- 공격 범위, 속성, 속도 같은 실제 실행 수치
- 아이템의 존재, 생성, 소비 및 수량 변경
- 스킬 사용 가능 여부
- 위치, 대상, 거리, 게임 상태 변경
- 엔딩 도달 여부와 최종 `endingId`

### 2.2 서버의 역할

서버는 게임 상태의 유일한 권위자다.

- 인벤토리 소유권 및 수량 검증
- 선택한 스킬과 대상의 유효성 검증
- D20 생성 및 결과 단계 결정
- 피해, 범위, 속성, 강도 등 실행 파라미터 계산
- 아이템 소비·생성·변형을 원자적으로 반영
- 체력과 상태 효과 변경
- Unity 이벤트 생성
- 변경 완료 후 상태 커밋

자연어에 “전설의 검을 꺼낸다”라고 적혀 있어도 해당 인스턴스가 인벤토리에 없으면 사용할 수 없다. 사용하려는 아이템은 반드시 요청의 `selectedInventoryInstanceIds`에 포함되어야 한다.

### 2.3 Unity의 역할

Unity는 서버가 확정한 이벤트를 표현한다.

- 캐릭터 애니메이션
- 이동과 공격 연출
- 투사체, 범위, 속성 및 파티클
- 아이템 사용·조합 연출
- 탐색·복제·삭제·연결·복구 효과
- 피격, 체력 변화 및 상태 효과 표시

Unity는 자연어를 다시 해석해 결과를 만들지 않는다. `unity.events`에 있는 타입과 파라미터를 프로젝트 내부 프리팹·애니메이션·VFX 매핑으로 재생한다.

### 2.4 엔티티 활성화와 공간 권위

- NPC·몬스터·보스의 런 전용 후보는 런 생성 시 서버가 고정 ID, 에셋, 슬롯, 성향과 함께 `DORMANT` 상태로 미리 만든다.
- 사건이 발생하면 새 엔티티를 즉석 생성하지 않고, 현재 지역·캠페인 단계·조우 규칙을 만족하는 휴면 후보만 `ACTIVE`로 전환한다.
- LLM은 휴면 후보 목록이나 원시 좌표를 받지 않는다. 현재 지역, 바이옴, 지형, 방향, 거리, 상호작용 가능 여부와 서버 발급 목적지 참조만 받는다.
- 서버는 플레이어 좌표와 방향을 권위 상태로 보관한다. 클라이언트가 보고한 현재 좌표로 서버 위치를 덮어쓰지 않는다.
- Unity에는 `runtime.spatial`로 정확한 플레이어 좌표, 바라보는 방향, 확정 이동 경로, 가시 엔티티 좌표를 제공한다.
- 자연어 이동은 LLM이 서버 발급 `destinationRef`를 고르는 방식이고, 직접 월드 조작은 서버가 검증한 목적지 좌표 요청을 사용한다.

## 3. 입력 계약

하나의 메시지 엔드포인트에서 자연어와 선택적 실행 컨텍스트를 받는다.

```json
{
  "inputType": "PLAYER_FREEFORM",
  "text": "붉은 결정을 낡은 검에 붙이고 Ctrl+K 복제를 실행한다.",
  "selectedInventoryInstanceIds": [
    "inventory.red_crystal.1",
    "inventory.old_sword.1"
  ],
  "selectedSkillId": "COPY",
  "targetIds": ["enemy.slime.1"],
  "expectedRunVersion": 12,
  "idempotencyKey": "message-00000013"
}
```

규칙:

- `text`는 자유 입력이며 서사 의도를 제공한다.
- 아이템 사용은 자연어 언급만으로 승인하지 않는다.
- `selectedInventoryInstanceIds`는 실제 소지품 인스턴스만 허용한다.
- `selectedSkillId`는 UI에서 함께 선택한 관리자 키보드 스킬이다.
- 서버는 텍스트와 명시적 선택이 모순될 경우 실행하지 않고 서사적으로 실패 또는 재확인을 반환한다.
- 일반 대화는 아이템, 스킬, 대상 필드 없이 제출할 수 있다.

## 4. 플레이어 최소 메타데이터

전통적인 RPG 능력치인 힘, 지능, 민첩, 레벨, 경험치는 현재 범위에서 제외한다.

```json
{
  "id": "player.nupjukyi",
  "displayName": "넙죽이",
  "health": {
    "current": 100,
    "max": 100
  },
  "inventory": [
    {
      "instanceId": "inventory.admin_keyboard.1",
      "itemId": "item.admin_keyboard",
      "quantity": 1
    }
  ],
  "availableSkillIds": ["SEARCH", "COPY", "DELETE", "CONNECT", "RESTORE", "UNDO"],
  "statusEffects": [],
  "location": {
    "areaId": "area.fading_forest",
    "sceneId": "scene.erased_path"
  },
  "narrative": {
    "knownFacts": [],
    "majorChoices": []
  }
}
```

필수 상태는 체력, 인벤토리, 사용 가능 스킬, 상태 효과, 현재 위치, 최소 서사 기억뿐이다. 새 메타데이터는 실제 게임 규칙에서 필요성이 확인된 뒤 추가한다.

## 5. 공통 응답 계약

모든 턴은 동일한 최상위 형태를 사용한다.

```json
{
  "turn": {
    "turnNo": 13,
    "intent": "COMBINE_AND_SKILL",
    "model": "gemini-3.1-flash-lite",
    "fallbackUsed": false
  },
  "narrative": {
    "storySequence": [],
    "nextIntervention": null
  },
  "resolution": {
    "required": true,
    "roll": null,
    "healthChanges": [],
    "inventoryChanges": [],
    "statusChanges": []
  },
  "unity": {
    "renderRequired": true,
    "events": []
  },
  "runVersion": 13
}
```

### 5.1 자연어 장면

`narrative.storySequence`는 표시 순서를 보존한다.

```json
[
  {
    "type": "NARRATION",
    "speakerId": null,
    "text": "넙죽이가 붉은 결정을 낡은 검의 홈에 밀어 넣었다."
  },
  {
    "type": "MONOLOGUE",
    "speakerId": null,
    "text": "빛은 붙잡았지만 형태가 불안정해. 오래 유지되지는 않겠어."
  },
  {
    "type": "DIALOGUE",
    "speakerId": "npc.comment",
    "text": "복제는 됐어. 하지만 저 균열이 검에도 묻어났어."
  }
]
```

허용 타입은 우선 `NARRATION`, `MONOLOGUE`, `DIALOGUE`로 제한한다. Unity 월드 액션을 자연어 배열 안에서 실행하지 않고 `unity.events`로 분리한다.

### 5.2 일반 대화 응답

대사와 독백만 있는 턴에는 판정과 Unity 렌더링이 없다.

```json
{
  "resolution": {
    "required": false,
    "roll": null,
    "healthChanges": [],
    "inventoryChanges": [],
    "statusChanges": []
  },
  "unity": {
    "renderRequired": false,
    "events": []
  }
}
```

Unity 클라이언트는 `renderRequired=false`이면 게임 월드 연출을 시작하지 않고 비주얼노벨 대화 UI만 갱신한다.

## 6. 판정 계약

초기 판정은 능력치 없이 구성한다.

```text
D20 + 아이템 보정 + 스킬 보정 + 서버가 확인한 상황 보정
```

결과 단계는 다음 다섯 개로 고정한다.

- `CRITICAL_FAILURE`
- `FAILURE`
- `PARTIAL_SUCCESS`
- `SUCCESS`
- `STRONG_SUCCESS`

```json
{
  "rollId": "roll.13",
  "d20": 17,
  "modifier": 2,
  "total": 19,
  "resultTier": "STRONG_SUCCESS"
}
```

판정 구간과 보정값은 서버 설정 테이블에서 관리한다. LLM 프롬프트에는 확정된 `resultTier`와 서술에 필요한 결과 요약만 전달한다.

## 7. Unity 이벤트 계약

모든 이벤트는 공통 필드를 가진다.

```json
{
  "eventId": "unity-event.13.1",
  "type": "ATTACK",
  "actorId": "player.nupjukyi",
  "targetIds": ["enemy.slime.1"],
  "resultTier": "STRONG_SUCCESS",
  "sequence": 0,
  "payload": {}
}
```

초기 이벤트 타입:

- `ATTACK`
- `ITEM_USE`
- `COMBINE`
- `SEARCH`
- `COPY`
- `DELETE`
- `CONNECT`
- `RESTORE`
- `UNDO`
- `STATUS_EFFECT`

### 7.1 공격

```json
{
  "eventId": "unity-event.14.1",
  "type": "ATTACK",
  "actorId": "player.nupjukyi",
  "targetIds": ["enemy.slime.1"],
  "resultTier": "STRONG_SUCCESS",
  "sequence": 0,
  "payload": {
    "attackStyle": "SLASH",
    "damage": 20,
    "range": 3.5,
    "radius": 1.8,
    "speed": 1.35,
    "element": "FIRE",
    "critical": true,
    "effectId": "fx.fire_slash"
  }
}
```

공격의 강도, 피해, 범위, 속성은 서버가 D20 단계와 선택 아이템을 바탕으로 확정한다. LLM이 숫자를 만들지 않는다.

### 7.2 아이템 사용 및 조합

```json
{
  "eventId": "unity-event.15.1",
  "type": "COMBINE",
  "actorId": "player.nupjukyi",
  "targetIds": [],
  "resultTier": "PARTIAL_SUCCESS",
  "sequence": 0,
  "payload": {
    "sourceInstanceIds": [
      "inventory.red_crystal.1",
      "inventory.old_sword.1"
    ],
    "skillId": "COPY",
    "createdInstanceIds": ["inventory.unstable_flame_sword.1"],
    "consumed": [
      {
        "instanceId": "inventory.red_crystal.1",
        "quantity": 1
      }
    ],
    "element": "FIRE",
    "intensity": "MEDIUM",
    "effectId": "fx.unstable_fusion"
  }
}
```

`resolution.inventoryChanges`가 권위 있는 상태 변경이고, Unity 이벤트의 아이템 정보는 표현을 위한 사본이다. 둘이 다르면 클라이언트는 `resolution`과 이후 run snapshot을 신뢰한다.

### 7.3 탐색과 관리자 키보드 스킬

탐색·복제·삭제·연결·복구·되돌리기도 같은 공통 이벤트 형태를 사용한다. 각 `payload`에는 Unity가 실제로 표현할 수 있는 값만 넣는다.

- 대상 또는 중심 참조 ID
- 범위와 강도
- 속성
- 지속 시간
- 프로젝트 내부 효과 매핑 ID
- 생성·삭제·변형이 확정된 엔티티 ID

좌표나 프리팹 파일 경로를 LLM이 출력하게 하지 않는다. 서버가 검증된 대상·슬롯 ID를 Unity가 가진 매핑으로 전달한다.

### 7.4 스킬별 gameplayResult

`runtime.gameplayResult`는 Unity가 `confirmedEffects`를 다시 해석하지 않도록 서버가 확정한 실행 결과를 제공한다. 일반 대화에서는 `null`이다.

```json
{
  "schemaVersion": "1.0",
  "actionType": "DELETE",
  "context": "COMBAT",
  "outcome": "STRONG_SUCCESS",
  "succeeded": true,
  "rollId": "turn-id:roll",
  "fx": {
    "scaleTier": "SCREEN",
    "element": "ROCK_SPIKE",
    "effectId": "ELEMENTAL_ROCK_SPIKE"
  },
  "result": {}
}
```

`outcome`은 기존 5단계 판정을 그대로 사용한다. `succeeded`는 편의용 파생값이며 `PARTIAL_SUCCESS`, `SUCCESS`, `STRONG_SUCCESS`에서만 참이다. Unity는 세부 판정을 boolean으로 축소하지 않고 항상 `outcome`을 우선한다.

FX 크기는 서버 판정에서 다음처럼 파생한다.

- `CRITICAL_FAILURE` → `TILE`
- `FAILURE` → `SMALL`
- `PARTIAL_SUCCESS` → `MEDIUM`
- `SUCCESS` → `LARGE`
- `STRONG_SUCCESS` → `SCREEN`

공격 논리 속성은 실제 Ninja Adventure 에셋에 맞춰 다음 enum으로 고정한다.

- `EXPLOSION`
- `FLAM`
- `ICE`
- `PLANT`
- `ROCK`
- `ROCK_SPIKE`
- `THUNDER`
- `WATER`
- `WATER_PILLAR`

논리 속성은 Unity 분기용이고 `effectId`는 실제 매니페스트 조회용이다. `ELEMENTAL_ICE_B`, `ELEMENTAL_ICE_FLAKE`는 `ICE`, `ELEMENTAL_PLANT_B`는 `PLANT`, `ELEMENTAL_ROCK_B`는 `ROCK`으로 정규화한다.

스킬별 `result`의 핵심 필드는 다음과 같다.

- `ATTACK`: 대상, 명중 여부, 피해, 전투 불능 여부, 사거리, 범위, 속도
- `COPY`: 원본, 생성된 복제본, 복제 계보 루트, 재복제 잠금 및 거절 사유
- `DELETE`: 대상, 적중 여부, 제거 여부, `RESISTED`·`PRESSURED`·`actor_withdrew` 등의 해결 상태
- `CONNECT`: 정확히 두 대상과 서버가 만든 연결 ID·관계·만료 턴
- `RESTORE`: 대상, 복구 단계, 복구 필드, 근거가 된 서버 스냅샷 턴
- `UNDO`: 보상 취소한 두 원본 턴과 `runTurnRewound=false`
- `SEARCH`: 대상, 반복 조사 여부, 새로 확인된 증거 ID
- `SELECT_ALL`: 판정으로 정해진 반경 1~6, 실제 영향 대상과 수
- `USE_ITEM`: 실제 소유 아이템과 소비 여부
- `COMBINE`: 실제 소비 재료와 서버가 생성한 결과 아이템

엔티티 참조 타입은 `PLAYER`, `NPC`, `ENEMY`, `PROP`, `INVENTORY_ITEM`을 사용한다. `UNDO`는 과거 결과를 보상 취소하지만 턴 번호와 run version을 역행시키지 않는다. `RESTORE`는 서버의 가역 스냅샷에 존재하는 손상이나 제거만 복구하며 임의 부활을 허용하지 않는다.

`MOVE`에는 두 경로가 있다.

- 자연어 행동 `MOVE`: D20 판정과 턴을 소비하고 `gameplayResult`를 반환한다.
- 안전 이동 `TRAVEL`: 캠페인 턴과 D20을 소비하지 않으며 이동·조우 이벤트만 반환한다.

## 8. 처리 순서와 실패 원칙

1. 요청 형식과 run version을 검증한다.
2. 자연어에서 `DIALOGUE`, `ATTACK`, `ITEM_USE`, `COMBINE`, `SKILL`, `SEARCH` 등의 의도를 제안받는다.
3. 명시적으로 선택된 아이템·스킬·대상과 대조한다.
4. 실행 행동이면 서버가 소유권과 합법성을 검사한다.
5. 필요한 행동만 D20을 굴린다.
6. 서버가 상태 변화와 Unity 이벤트를 먼저 확정한다.
7. 확정 결과를 LLM에 제공해 자연어 장면을 생성한다.
8. LLM 출력은 스키마와 화자 ID를 검증한다.
9. 상태와 턴을 한 트랜잭션으로 커밋한다.
10. 공통 응답을 반환한다.

실패 시 원칙:

- 없는 아이템은 생성해서 사용하지 않는다.
- 유효하지 않은 대상은 공격하거나 조작하지 않는다.
- 판정 실패도 자연스러운 장면과 약한/실패 Unity 이벤트로 표현할 수 있다.
- LLM 장애가 게임 상태의 이중 커밋을 일으키면 안 된다.
- LLM timeout 시 기존의 일반 문구 대신 의도·NPC·확정 결과를 반영하는 문맥형 fallback을 사용한다.
- 응답에는 항상 `fallbackUsed`와 실제 `model`을 표시한다.

## 9. LLM 요청 운영값

- `GEMINI_TIMEOUT_MS=15000`
- 전체 턴 무한 재시도 금지
- timeout, HTTP 오류, JSON 오류, 스키마 오류를 서로 구분해 기록
- 각 요청의 종류와 지연 시간을 기록
- 프롬프트, API 키, 전체 사용자 입력은 일반 서버 로그에 기록하지 않음

후속 구현에서는 생성 요청과 검토 요청의 시간을 별도로 기록해야 한다. 현재처럼 동일한 `gemini_director_attempt_failed` 이벤트만 남기면 어느 단계가 느렸는지 구분할 수 없다.

## 10. 구현 단계

### 1단계 — API 계약

- 공통 메시지 요청 DTO 추가
- `narrative`, `resolution`, `unity` 응답 DTO 추가
- 일반 대화에서 빈 Unity 이벤트 보장
- 기존 클라이언트를 위한 호환 변환 계층 유지

### 2단계 — 의도와 서버 판정

- 의도 분류와 명시적 UI 선택 대조
- D20 결과 단계 구현
- 공격·탐색·기본 키보드 스킬 결과 테이블 구현
- 체력과 상태 변경 구현

### 3단계 — 권위 인벤토리

- 아이템 정의와 소지 인스턴스 분리
- 소유권·수량 검증
- 사용·소비·생성·조합의 원자적 변경
- 없는 아이템 사용 거부 테스트

### 4단계 — 문맥형 서사 생성

- 확정 결과만 LLM 프롬프트에 전달
- 대사·독백·나레이션 스키마 적용
- 질문 입력에는 현재 NPC의 직접 답변을 우선
- 문맥형 deterministic fallback 구현

### 5단계 — Unity 어댑터

- 이벤트 타입별 C# DTO
- `effectId`와 실제 VFX/애니메이션 매핑
- `sequence` 순서 재생
- 중복 `eventId` 재생 방지
- 알 수 없는 이벤트를 안전하게 무시하고 기록

## 11. 완료 기준

- “이 세계에 대해 알려줘”는 NPC의 직접 대화로 이어지고 `renderRequired=false`이다.
- “검으로 공격한다”는 서버 D20 뒤 결과 단계에 맞는 공격 JSON과 자연어 장면을 함께 반환한다.
- 높은 결과와 낮은 결과에서 피해·범위·속도·연출 강도가 서버 규칙대로 달라진다.
- 선택하지 않았거나 보유하지 않은 아이템은 자연어에 적혀 있어도 사용할 수 없다.
- 아이템과 Ctrl+K 스킬을 함께 선택하면 검증·판정·인벤토리 변경·조합 이벤트가 일관되게 반환된다.
- 동일 `idempotencyKey` 재전송으로 체력이나 아이템이 두 번 변경되지 않는다.
- LLM이 임의의 피해량, 아이템, 좌표 또는 엔티티를 제안해도 서버 상태에 반영되지 않는다.
- LLM timeout에도 확정된 게임 결과는 보존되며, 현재 장면에 맞는 fallback 문장이 반환된다.
