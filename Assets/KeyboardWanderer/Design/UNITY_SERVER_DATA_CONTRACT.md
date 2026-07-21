# Unity 전달 데이터 계약

문서 버전: 1.0  
기준일: 2026-07-21  
기준 구현: `Server/src/domain/turn-engine.js`, `GameApiClient.cs`

## 1. 범위

이 문서는 서버 응답 중 Unity 클라이언트가 소비하는 데이터만 정의한다.

Unity는 자연어를 다시 해석하거나 판정하지 않는다. 다음 우선순위로 서버 데이터를 사용한다.

1. 최신 `run` 스냅샷으로 실제 게임 상태를 동기화한다.
2. `turn.runtime.spatial`로 좌표와 가시 상태를 동기화한다.
3. `turn.runtime.gameplayResult`로 확정된 행동 결과를 읽는다.
4. `turn.runtime.unity.events`를 순서대로 재생한다.
5. `turn.runtime.narrative`를 대화 UI에 표시한다.

LLM 프롬프트, 장기 기억, 퀘스트 생성 문맥, 내부 휴면 엔티티 목록은 Unity 전달 계약에 포함하지 않는다.

## 2. 턴 응답 최상위 구조

자연어 메시지, 선택지, 스킬 실행 결과는 다음 형태로 반환된다.

```json
{
  "turn": {
    "id": "turn-uuid",
    "runId": "run-uuid",
    "turnNo": 3,
    "committedRunVersion": 4,
    "runtime": {}
  },
  "run": {},
  "fromIdempotencyCache": false
}
```

Unity에서 기본적으로 사용할 필드는 다음과 같다.

| 경로 | 용도 |
|---|---|
| `turn.id` | 턴 및 연출 중복 실행 방지 |
| `turn.turnNo` | 대화·판정 순서 |
| `turn.committedRunVersion` | 턴이 반영된 서버 버전 |
| `turn.runtime` | 대화, 판정, 공간, Unity 연출 데이터 |
| `run` | 체력·인벤토리·엔티티 등 최종 권위 상태 |
| `fromIdempotencyCache` | 같은 요청의 재전송 응답 여부 |

`fromIdempotencyCache=true`인 응답은 같은 `turn.id`의 연출을 다시 재생하지 않는다.

## 3. `turn.runtime`

```json
{
  "turn": {
    "turnNo": 3,
    "intent": "ATTACK",
    "model": "gemini-3.1-flash-lite",
    "fallbackUsed": false
  },
  "narrative": {},
  "resolution": {},
  "gameplayResult": {},
  "spatial": {},
  "unity": {},
  "runVersion": 4
}
```

### `runtime.turn`

| 필드 | 타입 | 설명 |
|---|---|---|
| `turnNo` | integer | 현재 캠페인 턴 번호 |
| `intent` | string | 실행 행동. 일반 대화는 `DIALOGUE` |
| `model` | string | 자연어 장면 생성에 사용된 모델 또는 폴백 이름 |
| `fallbackUsed` | boolean | LLM 대신 서버 폴백 문장을 사용했는지 여부 |

### `runtime.runVersion`

해당 턴이 반영된 서버 run version이다. `run.version` 및 `turn.committedRunVersion`과 같아야 한다.

버전이 다르면 연출을 시작하지 않고 최신 run을 다시 요청한다.

## 4. 대화 데이터: `runtime.narrative`

```json
{
  "storySequence": [
    {
      "type": "NARRATION",
      "speakerId": null,
      "actionId": null,
      "text": "균열의 빛이 흔들리며 숲의 그림자를 밀어낸다."
    },
    {
      "type": "DIALOGUE",
      "speakerId": "npc-uuid",
      "actionId": null,
      "text": "방금 그 빛, 네가 만든 거야?"
    }
  ],
  "nextIntervention": {
    "reason": "NPC가 답을 기다린다.",
    "suggestedSkillIds": ["SEARCH"]
  }
}
```

`storySequence` 타입:

| 타입 | Unity 처리 |
|---|---|
| `NARRATION` | 나레이션 박스에 표시 |
| `MONOLOGUE` | 플레이어 독백으로 표시 |
| `DIALOGUE` | `speakerId`에 해당하는 인물 대사로 표시 |

서버의 `WORLD_ACTION`은 공개 응답에서 `NARRATION`으로 정규화된다. `storySequence`의 문장을 보고 공격·이동·아이템 변경을 실행하면 안 된다.

## 5. 판정 데이터: `runtime.resolution`

```json
{
  "required": true,
  "roll": {
    "rollId": "turn-uuid:roll",
    "d20": 17,
    "modifier": 3,
    "total": 20,
    "mechanicalScore": 8,
    "resultTier": "SUCCESS"
  },
  "healthChanges": [],
  "inventoryChanges": [],
  "skillChanges": [],
  "statusChanges": [],
  "movementChanges": [],
  "relationshipChanges": [],
  "worldChanges": [],
  "confirmedEffects": []
}
```

`resultTier` 값:

- `CRITICAL_FAILURE`
- `FAILURE`
- `PARTIAL_SUCCESS`
- `SUCCESS`
- `STRONG_SUCCESS`

일반 대화에서는 `required=false`, `roll=null`이다.

변경 배열의 역할:

| 배열 | 포함되는 대표 이벤트 |
|---|---|
| `healthChanges` | 체력 감소·회복 |
| `inventoryChanges` | 아이템 획득·사용·조합·수량 변경 |
| `skillChanges` | 특수 스킬 획득·소비 |
| `statusChanges` | 노출·기절·화상 등 상태 변경 |
| `movementChanges` | 엔티티 이동과 확정 경로 |
| `relationshipChanges` | 신뢰·공포·관계·협상 변화 |
| `worldChanges` | 생성·활성화·삭제·복구·연결 변화 |
| `confirmedEffects` | 해당 턴의 전체 서버 확정 이벤트 |

각 배열은 감사와 UI 갱신에 사용한다. 실제 연출 분기는 `gameplayResult`를 우선한다.

## 6. 행동 결과: `runtime.gameplayResult`

일반 대화에서는 `null`이다. 판정 행동에서는 다음 공통 구조를 사용한다.

```json
{
  "schemaVersion": "1.0",
  "actionType": "ATTACK",
  "context": "COMBAT",
  "outcome": "SUCCESS",
  "succeeded": true,
  "rollId": "turn-uuid:roll",
  "fx": {
    "scaleTier": "TARGET",
    "element": "ICE",
    "effectId": "ELEMENTAL_ICE"
  },
  "result": {}
}
```

### 공통 필드

| 필드 | 설명 |
|---|---|
| `actionType` | Unity가 실행할 행동 타입 |
| `context` | `COMBAT`, `INVESTIGATION`, `NEGOTIATION`, `DEPLOYMENT` 중 하나 |
| `outcome` | 5단계 판정 결과 |
| `succeeded` | `PARTIAL_SUCCESS` 이상이면 `true`인 편의 필드 |
| `rollId` | `resolution.roll.rollId`와 동일해야 함 |
| `fx` | 이펙트 크기, 논리 속성, 에셋 매니페스트 ID |
| `result` | 행동별 확정 결과 |

Unity는 `succeeded`만 보고 연출을 단순화하지 않고 `outcome`을 우선한다.

### 행동별 `result`

| `actionType` | 주요 필드 |
|---|---|
| `ATTACK` | `target`, `hit`, `damage`, `destroyed`, `attackStyle`, `range`, `radius`, `speed` |
| `MOVE` | `actor`, `moved`, `from`, `to`, `path`, `facing`, `arrived` |
| `COPY` | `target`, `clone`, `lineageRootId`, `copyLocked`, `rejectionReason` |
| `DELETE` | `target`, `hit`, `destroyed`, `resolution` |
| `CONNECT` | `targets`, `connections` |
| `RESTORE` | `target`, `restorationDegree`, `restoredFields`, `sourceSnapshotTurn` |
| `UNDO` | `sourceTurns`, `compensatedTurns`, `runTurnRewound` |
| `SEARCH` | `target`, `alreadyInvestigated`, `revealedEvidenceIds`, `newInformation`, `discoveryType`, `informationTitle` |
| `SELECT_ALL` | `radius`, `affectedTargets`, `affectedCount` |
| `USE_ITEM` | `item`, `consumed`, `quantity` |
| `COMBINE` | `sources`, `createdItem`, `consumed` |
| `INTERACT`, `NEGOTIATE`, `REST` | `targetRefs`와 `confirmedEffects`를 사용 |

엔티티 참조 구조:

```json
{
  "id": "entity-or-item-id",
  "entityType": "ENEMY"
}
```

`entityType` 값:

- `PLAYER`
- `NPC`
- `ENEMY`
- `PROP`
- `INVENTORY_ITEM`

### 공격 속성

`fx.element` 값:

- `EXPLOSION`
- `FLAM`
- `ICE`
- `PLANT`
- `ROCK`
- `ROCK_SPIKE`
- `THUNDER`
- `WATER`
- `WATER_PILLAR`

`fx.effectId`는 Unity의 `NinjaAdventureAssetManifest` 조회 키다. `element`는 논리 분기용이고 실제 프리팹·스프라이트 선택에는 `effectId`를 사용한다.

`fx.scaleTier` 값:

- `TILE`
- `TARGET`
- `AREA`
- `SCREEN`

## 7. 좌표와 시야: `runtime.spatial`

```json
{
  "authoritative": true,
  "areaId": "area-id",
  "biomeId": "frost_highland",
  "player": {
    "entityId": "player-entity-uuid",
    "position": { "x": 42, "y": 17 },
    "facing": "EAST"
  },
  "movement": {
    "from": { "x": 41, "y": 17 },
    "to": { "x": 42, "y": 17 },
    "path": [
      { "x": 41, "y": 17 },
      { "x": 42, "y": 17 }
    ],
    "facing": "EAST",
    "arrived": true
  },
  "visibility": [
    {
      "entityId": "npc-uuid",
      "position": { "x": 44, "y": 17 },
      "distance": 2,
      "direction": "EAST",
      "visible": true
    }
  ],
  "activeEncounter": null
}
```

방향 값:

- `NORTH`
- `SOUTH`
- `EAST`
- `WEST`
- `HERE`

처리 규칙:

- `authoritative`가 `true`인 좌표만 사용한다.
- 이동 연출은 `movement.path` 순서대로 실행한다.
- 이동 완료 후 Transform 또는 타일 좌표를 `player.position`에 강제로 맞춘다.
- `movement=null`이면 현재 턴에 플레이어 이동이 없었다는 뜻이다.
- `visibility`는 현재 플레이어 기준 최대 12칸 이내의 활성 엔티티를 담는다.
- Unity가 계산한 좌표로 서버 위치를 덮어쓰지 않는다.

`runtime.unity.events[*].actorId`는 현재 `PROTAGONIST_NUPJUKYI` 같은 논리 주인공 ID일 수 있다. 실제 런 엔티티 UUID가 필요할 때는 `runtime.spatial.player.entityId` 또는 `run.playerEntityId`를 사용한다.

## 8. Unity 연출 큐: `runtime.unity`

```json
{
  "renderRequired": true,
  "events": [
    {
      "eventId": "turn-uuid:unity:0",
      "type": "ATTACK",
      "actorId": "PROTAGONIST_NUPJUKYI",
      "targetIds": ["enemy-uuid"],
      "resultTier": "SUCCESS",
      "sequence": 0,
      "payload": {
        "skillId": "ATTACK",
        "intensity": "MEDIUM",
        "effectId": "ELEMENTAL_ICE",
        "element": "ICE",
        "fxScaleTier": "TARGET",
        "gameplayResult": {},
        "confirmedEvents": []
      }
    }
  ]
}
```

처리 규칙:

- `renderRequired=false`이면 월드 연출 큐를 실행하지 않는다.
- `events`는 `sequence` 오름차순으로 재생한다.
- 재생 완료한 `eventId`는 기록하여 중복 재생을 막는다.
- `payload.gameplayResult`는 상위 `runtime.gameplayResult`의 사본이다.
- `payload.confirmedEvents`는 디버깅·부가 UI용이다. 자연어로 재해석하지 않는다.
- 대화만 있는 턴은 `renderRequired=false`, `events=[]`이다.

`payload.intensity` 값:

- `LOW`
- `MEDIUM`
- `HIGH`

## 9. 최신 상태 동기화: `run`

Unity는 턴 연출 후 반드시 같이 반환된 최신 `run`을 적용한다.

필수 동기화 필드:

| 경로 | 설명 |
|---|---|
| `run.version` | 현재 서버 상태 버전 |
| `run.health`, `run.maxHealth` | 플레이어 체력 |
| `run.focus`, `run.maxFocus` | 관리자 키보드 자원 |
| `run.inventory` | 서버 권위 인벤토리 |
| `run.playerEntityId` | 실제 플레이어 엔티티 UUID |
| `run.entities` | 현재 활성 엔티티 목록 |
| `run.activeEncounter` | 진행 중인 조우 |
| `run.spatialContext` | UI용 의미 공간 정보 |
| `run.world` | 불변 월드 레이아웃과 매핑 |

`run.entities`에는 활성 상태의 엔티티만 포함된다. 서버에서 미리 만든 `DORMANT` NPC·몬스터·보스 후보는 공개되지 않는다.

새로운 활성 엔티티가 등장하면:

1. `runtime.resolution.worldChanges` 또는 이동 응답의 `events`에서 `entity_activated`를 받을 수 있다.
2. 실제 생성 여부와 최종 속성은 최신 `run.entities`로 확인한다.
3. 같은 ID의 Unity 오브젝트가 없으면 `assetId` 매핑으로 인스턴스화한다.
4. 이미 존재하면 중복 생성하지 않고 상태와 위치만 갱신한다.

삭제·비활성화된 엔티티가 최신 `run.entities`에 없으면 해당 Unity 오브젝트를 비활성화한다.

## 10. 안전 이동 응답

캠페인 턴을 소비하지 않는 월드 안전 이동은 `turn.runtime`이 아니라 `navigation`으로 반환된다.

```json
{
  "navigation": {
    "id": "navigation-uuid",
    "runId": "run-uuid",
    "sequence": 5,
    "expectedRunVersion": 7,
    "committedRunVersion": 8,
    "from": { "x": 20, "y": 30 },
    "to": { "areaId": "area-id", "x": 25, "y": 30 },
    "requestedDestination": { "x": 28, "y": 30 },
    "path": [
      { "x": 20, "y": 30 },
      { "x": 21, "y": 30 },
      { "x": 22, "y": 30 }
    ],
    "facing": "EAST",
    "pathCost": 2,
    "enteredAreaId": "area-id",
    "enteredBiomeId": "temperate_forest_field",
    "campaignRole": "ARRIVAL_CATALYST",
    "traversedAreaIds": ["area-id"],
    "reachedPoiIds": [],
    "encounterOpened": true,
    "encounter": {},
    "campaignTurnConsumed": false,
    "events": [],
    "sceneSequence": [],
    "narrative": {}
  },
  "run": {}
}
```

`requestedDestination`과 `to`가 다를 수 있다. 도중에 조우가 발생하면 서버는 안전한 지점에서 이동을 멈추므로 Unity는 반드시 `to`와 `path`를 사용한다.

안전 이동 완료 후에도 최신 `run.entities`, `run.activeEncounter`, `run.version`을 적용한다.

## 11. 일반 대화 응답 예시

```json
{
  "runtime": {
    "turn": {
      "turnNo": 2,
      "intent": "DIALOGUE",
      "model": "gemini-3.1-flash-lite",
      "fallbackUsed": false
    },
    "narrative": {
      "storySequence": [
        {
          "type": "DIALOGUE",
          "speakerId": "npc-uuid",
          "actionId": null,
          "text": "그 균열은 오래전부터 우리를 지켜보고 있었어."
        }
      ],
      "nextIntervention": {}
    },
    "resolution": {
      "required": false,
      "roll": null,
      "healthChanges": [],
      "inventoryChanges": [],
      "skillChanges": [],
      "statusChanges": [],
      "movementChanges": [],
      "relationshipChanges": [],
      "worldChanges": [],
      "confirmedEffects": []
    },
    "gameplayResult": null,
    "spatial": {
      "authoritative": true,
      "player": {
        "entityId": "player-uuid",
        "position": { "x": 42, "y": 17 },
        "facing": "SOUTH"
      },
      "movement": null,
      "visibility": [],
      "activeEncounter": null
    },
    "unity": {
      "renderRequired": false,
      "events": []
    },
    "runVersion": 3
  }
}
```

Unity는 이 경우 월드 애니메이션을 실행하지 않고 `storySequence`만 대화 UI에 표시한다.

## 12. 클라이언트 적용 순서

```text
응답 수신
→ idempotency/turn.id 중복 확인
→ run.version 검증
→ run 엔티티·체력·인벤토리 동기화
→ runtime.spatial 좌표·방향 동기화
→ runtime.narrative 대화 UI 등록
→ renderRequired 확인
→ unity.events를 sequence 순으로 재생
→ 최신 run 상태로 최종 보정
```

서버 데이터가 서로 다르게 보일 경우 최종 권위는 다음 순서다.

```text
최신 run 스냅샷
> runtime.spatial
> runtime.gameplayResult
> runtime.resolution.confirmedEffects
> runtime.narrative 문장
```
