# Ninja Adventure — 자연어 의도 실행 시스템 구현 계획

상태: 구현 계획 v1  
작성일: 2026-07-21  
상위 계약: `NARRATIVE_RUNTIME_IMPLEMENTATION_DIRECTIVE.md`

## 1. 목표

플레이어가 입력한 자연어를 단순 대사로만 처리하지 않고, 서버가 실행 가능한 게임 의도로 분류한다. 공격, 이동, 탐색, 아이템 사용, 아이템 조합, 관리자 키보드 스킬은 서버 검증과 D20 판정을 거쳐 권위 상태를 변경한다. 그 뒤 LLM은 확정된 결과만 자연어 장면으로 표현한다.

```text
자연어 입력
→ 의도 분류
→ 참조 대상·아이템 해석
→ 서버 합법성 검증
→ D20 판정
→ 상태 변경
→ Unity 이벤트 생성
→ LLM 서사 생성
→ 원자적 커밋
```

현재 임시로 구현된 SEARCH 정규식 분기는 이 공통 파이프라인으로 교체한다.

## 2. 지원 의도

| 의도 | 설명 | D20 |
| --- | --- | --- |
| `DIALOGUE` | NPC에게 말하거나 질문 | 없음 |
| `ATTACK` | 대상 공격 | 필수 |
| `MOVE` | 장소나 대상 방향으로 이동 | 안전 이동은 없음, 위험 이동은 필수 |
| `SEARCH` | 주변, 대상, 아이템 탐색 | 필수 |
| `ITEM_USE` | 소지 아이템 사용 | 필수 |
| `COMBINE` | 둘 이상의 소지 아이템 조합 | 필수 |
| `COPY` | 관리자 키보드 복제 | 필수 |
| `DELETE` | 관리자 키보드 삭제 | 필수 |
| `CONNECT` | 대상 연결 | 필수 |
| `RESTORE` | 대상 복구 | 필수 |
| `UNDO` | 직전의 되돌릴 수 있는 결과 취소 | 필수 |

대화 외의 행동은 LLM 서술만으로 성공할 수 없다.

## 3. 요청 계약

웹과 Unity는 자연어 외에 사용자가 UI에서 명시적으로 선택한 정보를 함께 보낸다.

```json
{
  "inputType": "PLAYER_FREEFORM",
  "text": "붉은 결정을 낡은 검에 붙인 다음 슬라임을 공격한다.",
  "selectedInventoryInstanceIds": [
    "inventory.red_crystal.1",
    "inventory.old_sword.1"
  ],
  "selectedSkillId": null,
  "selectedTargetIds": ["enemy.slime.1"],
  "selectedDestination": null,
  "expectedRunVersion": 8,
  "idempotencyKey": "message-00000009"
}
```

규칙:

- 자연어는 의도와 사용 방법을 설명한다.
- 선택한 ID는 실제 실행 대상의 권위 있는 참조다.
- 자연어에만 언급되고 선택되지 않은 아이템은 사용할 수 없다.
- 선택된 아이템이 인벤토리에 없으면 요청을 거절한다.
- 자연어와 선택 정보가 충돌하면 기계적 실행을 중단하고 재선택을 요구한다.
- 한 요청에서 주 행동은 하나만 실행한다.
- “조합한 뒤 공격한다”처럼 둘 이상의 행동이면 서버가 첫 실행 가능한 행동만 제안하거나 사용자에게 단계 분리를 요구한다.

## 4. 의도 분석 결과

의도 분석기는 상태를 변경하지 않고 제안만 반환한다.

```json
{
  "intentType": "COMBINE",
  "confidence": 0.94,
  "actionText": "붉은 결정을 낡은 검에 붙인다",
  "referencedInventoryInstanceIds": [
    "inventory.red_crystal.1",
    "inventory.old_sword.1"
  ],
  "referencedTargetIds": [],
  "skillId": null,
  "requiresResolution": true,
  "ambiguities": []
}
```

분류 원칙:

1. UI에서 명시한 `selectedSkillId`와 선택 아이템을 최우선 신호로 사용한다.
2. 규칙 기반 분류로 명확한 명령을 먼저 처리한다.
3. 규칙만으로 모호할 때 제한된 JSON LLM 분류기를 사용한다.
4. LLM 결과는 허용된 의도 enum과 전달된 ID만 사용할 수 있다.
5. 낮은 확신도나 복수 행동은 임의로 실행하지 않는다.
6. 분류 실패 시 `DIALOGUE`로 조용히 낮추지 않고 `CLARIFICATION_REQUIRED`를 반환한다.

## 5. 공통 D20 판정

초기 버전은 힘, 지능, 민첩 같은 캐릭터 능력치를 사용하지 않는다.

서버는 유효한 플레이어 입력을 승인한 직후 D20을 하나 사전 생성한다. 의도 분석 결과가 `DIALOGUE` 또는 안전 `MOVE`이면 준비한 값은 상태 변경에 사용하지 않고 응답과 LLM에도 노출하지 않는다. 판정이 필요한 행동이면 같은 값을 끝까지 사용한다. 멱등 재전송은 커밋된 결과를 반환하며 새로운 값을 생성하지 않는다.

```text
입력 검증 → D20 사전 생성 → 의도 분석 → 필요할 때만 적용
```

```text
최종 점수 = D20 + 아이템 보정 + 스킬 보정 + 서버 상황 보정 - 난이도
```

결과 단계:

| 결과 | 의미 |
| --- | --- |
| `CRITICAL_FAILURE` | 실패하며 명시적인 부작용 발생 |
| `FAILURE` | 효과 없음 또는 최소한의 소모만 발생 |
| `PARTIAL_SUCCESS` | 목적 일부 달성, 약한 결과나 부작용 동반 |
| `SUCCESS` | 의도한 결과 정상 달성 |
| `STRONG_SUCCESS` | 강화된 결과 또는 추가 이점 |

모든 판정에는 다음 감사 정보가 남아야 한다.

```json
{
  "rollId": "roll.9",
  "d20": 16,
  "modifier": 2,
  "difficulty": 11,
  "total": 7,
  "resultTier": "SUCCESS",
  "modifierSources": [
    { "type": "ITEM", "sourceId": "inventory.old_sword.1", "value": 2 }
  ]
}
```

## 6. 의도별 규칙

### 6.1 공격

검증:

- 대상이 현재 장면에 존재하는가
- 공격 가능한 상태인가
- 선택 무기나 아이템을 실제로 소유하는가
- 대상까지의 거리와 공격 범위가 유효한가

결과:

- D20 단계로 피해, 공격 범위, 속도, 속성, 효과 강도를 결정한다.
- 체력은 서버가 변경한다.
- LLM은 확정 피해와 대상 상태를 받은 뒤 대사와 묘사만 생성한다.

```json
{
  "type": "ATTACK",
  "payload": {
    "damage": 12,
    "range": 2.4,
    "radius": 0.8,
    "speed": 1.1,
    "element": "NONE",
    "critical": false
  }
}
```

### 6.2 아이템 사용

모든 아이템 사용에 D20을 적용한다. 아이템 정의에는 결과 단계별 효과 테이블을 둔다.

```json
{
  "itemId": "item.healing_potion",
  "usePolicy": {
    "consumeOn": ["FAILURE", "PARTIAL_SUCCESS", "SUCCESS", "STRONG_SUCCESS"],
    "effects": {
      "CRITICAL_FAILURE": { "healthDelta": -2, "statusId": "NAUSEA" },
      "FAILURE": { "healthDelta": 0 },
      "PARTIAL_SUCCESS": { "healthDelta": 4 },
      "SUCCESS": { "healthDelta": 8 },
      "STRONG_SUCCESS": { "healthDelta": 12, "clearStatusIds": ["POISON"] }
    }
  }
}
```

소비 여부는 아이템별 정책으로 결정한다. 기본 정책은 치명적 실패를 제외한 모든 시도에서 1개 소비이며, 특수 아이템은 별도 설정할 수 있다.

처리 순서:

1. 소유권과 수량 확인
2. 사용 가능한 아이템인지 확인
3. D20 판정
4. 결과 단계에 맞는 효과 계산
5. 아이템 소비와 체력·상태 변경을 같은 트랜잭션으로 적용
6. `ITEM_USE` Unity 이벤트 생성

### 6.3 아이템 조합

조합은 최소 2개의 서로 다른 인벤토리 인스턴스를 요구하고 항상 D20을 사용한다.

조합 레시피는 서버가 관리한다.

```json
{
  "recipeId": "recipe.flame_sword",
  "ingredientItemIds": ["item.old_sword", "item.red_crystal"],
  "resultByTier": {
    "CRITICAL_FAILURE": null,
    "FAILURE": null,
    "PARTIAL_SUCCESS": "item.unstable_flame_sword",
    "SUCCESS": "item.flame_sword",
    "STRONG_SUCCESS": "item.refined_flame_sword"
  },
  "consumptionByTier": {
    "CRITICAL_FAILURE": ["item.red_crystal"],
    "FAILURE": [],
    "PARTIAL_SUCCESS": ["item.old_sword", "item.red_crystal"],
    "SUCCESS": ["item.old_sword", "item.red_crystal"],
    "STRONG_SUCCESS": ["item.old_sword", "item.red_crystal"]
  }
}
```

규칙:

- 존재하지 않는 레시피는 임의의 결과 아이템을 생성하지 않는다.
- 레시피가 없으면 `RECIPE_NOT_FOUND` 또는 발견형 조합 정책으로 분기한다.
- 발견형 조합을 허용하더라도 결과 후보는 서버 카탈로그 안에서만 선택한다.
- 같은 인스턴스를 두 재료로 중복 사용할 수 없다.
- 소비와 결과 아이템 생성은 원자적으로 처리한다.
- 실패 단계에서 어떤 재료가 보존·소비되는지는 레시피가 결정한다.

### 6.4 탐색

- 주변, 특정 대상, 아이템 탐색을 구분한다.
- 아이템 탐색 성공 시 서버 카탈로그와 현재 지역의 loot table에서 결과를 선택한다.
- LLM이 아이템 이름을 발명한 뒤 인벤토리에 넣는 구조를 금지한다.
- 같은 고정 loot source는 중복 획득할 수 없다.

### 6.5 이동

- 안전하고 연결된 위치로의 이동은 D20 없이 처리한다.
- 위험 지역, 봉쇄 지역, 추격 중 이동은 D20을 사용한다.
- 자연어 장소명은 현재 서버가 제공한 목적지 후보와 매칭한다.
- 매칭되지 않은 장소를 LLM이 새 좌표로 만들 수 없다.
- Unity에는 확정 목적지 ID와 이동 결과만 전달한다.

### 6.6 관리자 키보드 스킬

- 자연어와 `selectedSkillId` 모두 지원한다.
- 명시적으로 선택한 스킬이 자연어보다 우선한다.
- 대상 수, 거리, 사용 가능 여부를 서버가 검증한다.
- COPY, DELETE, CONNECT, RESTORE, UNDO는 모두 D20을 사용한다.
- 없는 객체 생성, 보호 아이템 삭제, 확정되지 않은 복구는 거절한다.

## 7. 서버 상태 변경 계약

```json
{
  "resolution": {
    "required": true,
    "roll": {},
    "healthChanges": [],
    "inventoryChanges": [
      {
        "type": "CONSUMED",
        "instanceId": "inventory.red_crystal.1",
        "quantity": 1
      },
      {
        "type": "CREATED",
        "instanceId": "inventory.flame_sword.1",
        "itemId": "item.flame_sword",
        "quantity": 1
      }
    ],
    "statusChanges": []
  }
}
```

상태 변경은 설명 문자열이 아니라 타입이 있는 구조화 데이터로 기록한다. 응답의 `runVersion`과 최신 run snapshot이 최종 권위다.

## 8. LLM 서사 경계

LLM에 전달할 결과 예시:

```json
{
  "confirmedIntent": "COMBINE",
  "resultTier": "PARTIAL_SUCCESS",
  "consumedItems": ["붉은 결정", "낡은 검"],
  "createdItem": "불안정한 화염검",
  "confirmedHealthChanges": [],
  "confirmedStatusChanges": []
}
```

LLM은 이 범위를 벗어난 획득, 소비, 피해, 이동, 사망, 상태 변화를 묘사하면 안 된다. 출력 검증기는 다음 표현을 기계적 결과와 대조한다.

- 주웠다, 얻었다, 획득했다
- 사용했다, 마셨다, 소모했다
- 합쳤다, 제작했다, 완성했다
- 공격했다, 맞았다, 쓰러졌다
- 이동했다, 도착했다
- 복제했다, 삭제했다, 복구했다

확정 이벤트가 없는데 이런 결과를 단정하면 응답을 거절하고 문맥형 fallback을 사용한다.

## 9. 구현 단계

### 단계 1 — 공통 요청 및 의도 분류

- `/messages` 요청에 선택 아이템·스킬·대상·목적지 필드 추가
- `IntentProposal` 스키마와 enum 구현
- 규칙 기반 분류기와 제한형 LLM 분류기 구현
- 복수 행동 및 낮은 확신도 처리
- SEARCH 전용 임시 정규식 제거

완료 조건:

- 같은 입력이 항상 동일한 의도 후보로 정규화된다.
- 명확한 행동이 일반 대화로 조용히 처리되지 않는다.

### 단계 2 — 인벤토리 카탈로그와 트랜잭션

- 아이템 정의와 소지 인스턴스 분리
- 아이템 사용 정책 추가
- 조합 레시피와 결과 단계 테이블 추가
- 소비·생성·변형 원자 처리
- 중복 요청 방지

완료 조건:

- 없는 아이템 사용과 조합이 모두 거절된다.
- 실패를 포함한 모든 결과에서 수량이 정책과 정확히 일치한다.

### 단계 3 — 공격과 체력

- 공격 대상 검증
- 결과 단계별 공격 파라미터 테이블
- 체력 변경과 최소 상태 효과
- ATTACK Unity 이벤트

완료 조건:

- 높은 판정과 낮은 판정의 피해와 연출 값이 서버 규칙대로 달라진다.
- LLM의 임의 피해 수치는 무시된다.

### 단계 4 — 이동과 전체 키보드 스킬

- 자연어 목적지 매칭
- 안전·위험 이동 분리
- 자연어 COPY, DELETE, CONNECT, RESTORE, UNDO 연결
- 대상 개수와 거리 검증

완료 조건:

- 이동은 서버에 존재하는 목적지만 사용한다.
- 모든 스킬이 자연어와 명시적 선택 양쪽에서 같은 판정 경로를 사용한다.

### 단계 5 — LLM 결과 일치 검증

- 확정 결과 전용 narration context 구성
- 획득·소비·피해·이동 주장 검증
- 질문에는 NPC 직접 답변 우선
- 문맥형 fallback

완료 조건:

- 인벤토리 이벤트 없이 “아이템을 주웠다”는 문장이 화면에 나오지 않는다.
- 실패한 공격을 성공한 것처럼 묘사하지 않는다.

### 단계 6 — 웹과 Unity 연동

- 웹에 아이템 다중 선택 UI 추가
- 대상과 목적지 선택 UI 추가
- 의도·판정·상태 변경·Unity JSON 디버그 표시
- Unity C# DTO와 이벤트 매핑
- eventId 중복 재생 방지

완료 조건:

- 웹에서 자연어와 선택 항목을 함께 보내 모든 의도를 재현할 수 있다.
- 일반 대화에는 Unity 이벤트가 없고 실행 행동에는 검증된 이벤트가 있다.

## 10. 필수 테스트 시나리오

1. “코멘트에게 세계에 관해 묻는다” → 대화, D20 없음, Unity 이벤트 없음.
2. “주변에서 아이템을 찾는다” → SEARCH, D20, 성공 시 실제 인벤토리 추가.
3. “없는 전설의 검을 사용한다” → `INVENTORY_ITEM_NOT_OWNED`.
4. 회복약 사용 → D20 단계별 회복량과 소비 정책 검증.
5. 검과 결정을 조합 → D20 단계별 결과 아이템 및 재료 소비 검증.
6. 같은 조합 요청 재전송 → 아이템이 두 번 생성되지 않음.
7. 적 공격 → D20, 체력 변경, ATTACK 이벤트.
8. 존재하지 않는 적 공격 → 대상 검증 오류.
9. 연결된 안전 지역 이동 → D20 없음.
10. 위험 지역 이동 → D20과 MOVE 이벤트.
11. 자연어 COPY와 버튼 COPY → 동일한 서버 판정 경로.
12. 확정 획득 이벤트 없는 LLM의 아이템 획득 서술 → 검증 실패 및 fallback.

## 11. 구현 우선순위

가장 먼저 단계 1과 단계 2를 완료한다. 현재 가장 큰 오류는 자연어 행동이 대화로 처리되거나 LLM 서술과 인벤토리 상태가 불일치하는 것이다. 그 다음 공격과 체력, 이동, 전체 키보드 스킬 순서로 확장한다.

초기 완료 범위는 다음과 같다.

```text
DIALOGUE
SEARCH
ITEM_USE
COMBINE
ATTACK
MOVE
COPY / DELETE / CONNECT / RESTORE / UNDO
```

위 의도 모두가 공통 분류·검증·D20·상태 변경·Unity 이벤트·서사 생성 파이프라인을 통과해야 자연어 의도 실행 시스템을 완료한 것으로 본다.
