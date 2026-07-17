# notegame
몰입캠프 26s-w3-c3-03 프로젝트 repository

# 기능명세서 (초안, 추후 수정 예정)

---

# 1. 시스템(Core System)

| 기능명 | 설명 | 입력 | 출력 | 예상 응답 | 비고 |
| --- | --- | --- | --- | --- | --- |
| Game Initializer | 새 게임 생성 | New Game | Campaign 생성 | World Seed 반환 | 최초 1회 |
| Campaign Director | 캠페인 진행 관리 | WorldState | 현재 Story Beat | 다음 진행 단계 | Unity 내부 |
| Rule Engine | 게임 규칙 계산 | Action | 판정 결과 | Success / Fail | LLM 접근 불가 |
| World Manager | 월드 관리 | Seed | WorldMap | 생성 완료 | 읽기 전용 맵 |
| Save Manager | 저장/로드 | Save 요청 | Save Data | 완료 여부 | PostgreSQL |
| LLM Gateway | LLM 통신 | Prompt | JSON | Parsed Response | API Gateway |

---

# 2. 월드 생성(World Generation)

| 기능명 | 설명 | 입력 | 출력 | 예상 응답 | 비고 |
| --- | --- | --- | --- | --- | --- |
| Seed Generator | 월드 Seed 생성 | Random | Seed | Integer | 재현 가능 |
| Biome Generator | 바이옴 생성 | Seed | Biome Map | Forest, Desert... | Procedural |
| Road Generator | 길 생성 | Map | Road | Connected |  |
| POI Generator | 관심지점 생성 | Map | Temple, Village | POI 목록 |  |
| Dungeon Generator | 던전 생성 | Seed | Dungeon | Dungeon ID |  |
| Validation | 월드 검증 | WorldMap | Pass/Fail | Validation Result | 실패 시 재생성 |
| World Export | LLM용 요약 생성 | WorldMap | JSON | Area Summary | 읽기 전용 |

---

# 3. 플레이어 시스템(Player)

| 기능명 | 설명 | 입력 | 출력 | 예상 응답 | 비고 |
| --- | --- | --- | --- | --- | --- |
| Character Creator | 플레이어 생성 | 이름 | Character | PlayerID |  |
| Movement | 이동 | 클릭 | 좌표 | Move Success |  |
| Keyboard Ability | 키보드 능력 | Ability | Effect | Accepted Effect | Rule Engine |
| Inventory | 인벤토리 | Item | Inventory | Updated |  |
| Status Manager | 상태 관리 | Damage | Status | Updated |  |

---

# 4. 턴 시스템(Turn)

| 기능명 | 설명 | 입력 | 출력 | 예상 응답 | 비고 |
| --- | --- | --- | --- | --- | --- |
| Turn Start | 턴 시작 | Next Turn | Turn Context | Ready |  |
| Player Action | 행동 입력 | Mouse | Intent | Pending |  |
| D20 Roll | 주사위 판정 | Ability | Dice Result | 1~20 |  |
| Intent Calculator | 의도 강도 계산 | Dice | Strength | Low/Medium/High |  |
| Rule Resolution | 규칙 계산 | Action | Outcome | Validated |  |
| Turn End | 턴 종료 | Apply | Next Turn | Saved |  |

---

# 5. LLM 시스템

| 기능명 | 설명 | 입력 | 출력 | 예상 응답 | 비고 |
| --- | --- | --- | --- | --- | --- |
| Prompt Builder | Prompt 생성 | WorldState | Prompt | JSON |  |
| Context Compressor | Context 축약 | History | Summary | Context |  |
| GM Generator | 사건 생성 | Prompt | Proposal | JSON |  |
| Dialogue Generator | 대사 생성 | NPC | Dialogue | Text |  |
| Description Generator | 서술 생성 | Event | Narrative | Text |  |
| JSON Validator | 응답 검증 | JSON | Pass/Fail | Retry |  |
| Asset Validator | 에셋 검증 | AssetID | Result | Exists |  |
| Rule Validator | 규칙 검증 | Proposal | Accepted | Valid Effect |  |

---

# 6. NPC 시스템

| 기능명 | 설명 | 입력 | 출력 | 예상 응답 | 비고 |
| --- | --- | --- | --- | --- | --- |
| NPC Generator | NPC 생성 | Role Slot | NPC | NPC ID |  |
| Memory Manager | 기억 관리 | Event | Memory | Updated |  |
| Affinity Manager | 호감도 | Choice | Affinity | Updated |  |
| Behavior Planner | 행동 계획 | State | Action | Intent |  |
| Death Manager | 생존 여부 | Damage | Alive/Dead | Updated |  |

---

# 7. 이벤트 시스템

| 기능명 | 설명 | 입력 | 출력 | 예상 응답 | 비고 |
| --- | --- | --- | --- | --- | --- |
| Event Generator | 사건 생성 | Campaign | Event | Proposal |  |
| Quest Generator | 퀘스트 생성 | Story Beat | Quest | Objective |  |
| Encounter Builder | 전투 생성 | Area | Encounter | Enemy List |  |
| Reward Generator | 보상 생성 | Difficulty | Reward | Loot |  |
| Consequence Manager | 결과 적용 | Choice | World Change | Updated |  |

---

# 8. 키보드 능력 시스템

| 기능명 | 설명 | 입력 | 출력 | 예상 응답 | 비고 |
| --- | --- | --- | --- | --- | --- |
| Move | 이동 | Target | Position | Applied |  |
| Copy | 복제 | Object | Clone | Success |  |
| Delete | 삭제 | Object | Removed | Success |  |
| Connect | 연결 | A,B | Link | Connected |  |
| Restore | 복구 | Object | Restored | Success |  |
| Undo | 되돌리기 | Snapshot | Restored | Cost 발생 | Unity 판정 |

---

# 9. 저장 시스템

| 기능명 | 설명 | 입력 | 출력 | 예상 응답 | 비고 |
| --- | --- | --- | --- | --- | --- |
| Auto Save | 자동 저장 | Turn End | Save | OK |  |
| Manual Save | 수동 저장 | User | Save | OK |  |
| Load Game | 불러오기 | Slot | World | Loaded |  |
| Snapshot | Undo 저장 | Current State | Snapshot | Stored |  |

---

# 10. 데이터베이스(PostgreSQL) - 초안을 보충하는 방식으로 추가 가능

| 테이블 | 설명 | 주요 컬럼 | 예상 데이터 |  |
| --- | --- | --- | --- | --- |
| campaigns | 캠페인 | id, phase | 진행상황 |  |
| worlds | 월드 | seed | 맵 |  |
| areas | 지역 | biome | Area 정보 |  |
| actors | 플레이어/NPC | hp | 캐릭터 |  |
| npc_memories | NPC 기억 | npc_id | Memory |  |
| quests | 퀘스트 | state | 진행도 |  |
| events | 이벤트 | event_type | 사건 |  |
| world_facts | 확정 사실 | key | Fact |  |
| rumors | 소문 | text | Rumor |  |
| turn_logs | 턴 로그 | turn | Action |  |
| save_slots | 저장 | slot | Save Data |  |
| llm_logs | LLM 로그 | prompt | Response |  |

---

# 11. 서버 API

| API | 기능 | 입력 | 출력 | 예상 응답 |
| --- | --- | --- | --- | --- |
| POST /campaign/new | 새 게임 | Config | Campaign | CampaignID |
| GET /campaign | 캠페인 조회 | ID | Campaign | JSON |
| POST /turn/start | 턴 시작 | State | Context | Ready |
| POST /action | 행동 | Intent | Outcome | Result |
| POST /turn/end | 턴 종료 | Outcome | Saved | OK |
| POST /llm/generate | LLM 호출 | Prompt | JSON | Proposal |
| POST /save | 저장 | Save | Result | Success |
| POST /load | 로드 | Slot | SaveData | Loaded |

---

# 12. Unity 모듈 구성

| 모듈 | 기능 | 담당 |
| --- | --- | --- |
| UI | 화면 | Unity |
| Input | 입력 | Unity |
| Rendering | 렌더링 | Unity |
| Animation | 애니메이션 | Unity |
| Audio | 사운드 | Unity |
| TurnSystem | 턴 진행 | Unity |
| RuleEngine | 규칙 | Unity |
| CampaignDirector | 캠페인 | Unity |
| SaveSystem | 저장 | Unity |
| Networking | 서버 통신 | Unity |

---

# 13. LLM 서버 모듈

| 모듈 | 기능 | 입력 | 출력 | 예상 응답 |
| --- | --- | --- | --- | --- |
| Prompt Builder | 프롬프트 조합 | Context | Prompt | JSON |
| Context Builder | 컨텍스트 생성 | WorldState | Context | Summary |
| Narrative Generator | 사건 생성 | Prompt | Story | Proposal |
| Dialogue Generator | NPC 대사 | NPC | Dialogue | Text |
| Description Generator | 결과 묘사 | Outcome | Narrative | Text |
| Validator | JSON 검증 | Response | Pass/Fail | Retry |
| Asset Resolver | 에셋 확인 | Tag | AssetID | Exists |

---

## 전체 요청·응답 흐름 요약

| 단계 | 호출 주체 | 요청(Request) | 응답(Response) |
| --- | --- | --- | --- |
| 1 | Unity → Campaign Director | 새 턴 시작 | TurnContext |
| 2 | Unity → Rule Engine | 행동 입력 | Dice Result + 규칙 판정 |
| 3 | Unity → Prompt Builder | 현재 상태 전달 | Prompt |
| 4 | Prompt Builder → LLM | Prompt | GM Proposal(JSON) |
| 5 | Validator | JSON 검증 | Pass / Retry |
| 6 | Rule Engine | 제안 효과 적용 | ValidatedOutcome |
| 7 | LLM | 확정 결과 서술 | Narrative + NPC 반응 |
| 8 | Save Manager | 상태 저장 | Save Complete |
| 9 | Campaign Director | 진행도 갱신 | 다음 Story Beat |