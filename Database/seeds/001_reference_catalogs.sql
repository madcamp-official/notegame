begin;

set local search_path = keyboard_wanderer, public;

insert into product_identity_catalog (code, identity_kind, display_name_ko, metadata)
values
    ('WORLD_CODRIA', 'world', '코드리아', '{"fixedAcrossRuns":true}'::jsonb),
    ('PROTAGONIST_NUPJUKYI', 'protagonist', '넙죽이', '{"fixedAcrossRuns":true,"origin":"reality"}'::jsonb),
    ('ARTIFACT_ADMIN_KEYBOARD', 'artifact', '관리자 키보드', '{"fixedAcrossRuns":true}'::jsonb)
on conflict (code) do update set
    identity_kind = excluded.identity_kind,
    display_name_ko = excluded.display_name_ko,
    metadata = excluded.metadata;

insert into campaign_region_axis_catalog (
    code, display_order, display_name_ko, narrative_purpose, metadata
) values
    ('REGION_BUG_FOREST', 1, '버그 숲', '오류 제거와 공존의 첫 선택', '{"fixedAxis":true}'::jsonb),
    ('REGION_BUFFER_VILLAGE', 2, '버퍼 마을', '생존 자원과 공공 신뢰', '{"fixedAxis":true}'::jsonb),
    ('REGION_DEADLOCK_CITY', 3, '데드락 시티', '엉킨 관계와 협상', '{"fixedAxis":true}'::jsonb),
    ('REGION_DATA_GRAND_LIBRARY', 4, '데이터 대도서관', '붕괴 원인의 기록과 필수 단서', '{"fixedAxis":true}'::jsonb),
    ('REGION_LEGACY_CITADEL', 5, '레거시 성채', '기술 부채와 과거 선택의 역류', '{"fixedAxis":true}'::jsonb),
    ('REGION_ROOT_SYSTEM', 6, '루트 시스템', '최종 배치와 결말', '{"fixedAxis":true,"requiresAllAdminAccess":true}'::jsonb)
on conflict (code) do update set
    display_order = excluded.display_order,
    display_name_ko = excluded.display_name_ko,
    narrative_purpose = excluded.narrative_purpose,
    metadata = excluded.metadata;

insert into admin_access_level_catalog (code, access_level, display_name_ko, metadata)
values
    ('ADMIN_ACCESS_LEVEL_1', 1, '관리자 권한 I', '{"fixedAcrossRuns":true}'::jsonb),
    ('ADMIN_ACCESS_LEVEL_2', 2, '관리자 권한 II', '{"fixedAcrossRuns":true}'::jsonb),
    ('ADMIN_ACCESS_LEVEL_3', 3, '관리자 권한 III', '{"fixedAcrossRuns":true}'::jsonb)
on conflict (code) do update set
    access_level = excluded.access_level,
    display_name_ko = excluded.display_name_ko,
    metadata = excluded.metadata;

insert into campaign_template_catalog (code, version, display_name, is_enabled, metadata)
values
    (
        'codria-v4',
        'codria-campaign.v4',
        '넙죽이와 붕괴한 코드 왕국',
        true,
        '{"language":"ko-KR","turnRange":[30,50],"defaultTurns":40,"biomeCount":6,"regionAxisCount":6,"sealedWorld":true,"runScopedPlan":true,"worldContractCode":"WORLD_CODRIA","protagonistContractCode":"PROTAGONIST_NUPJUKYI","artifactContractCode":"ARTIFACT_ADMIN_KEYBOARD","adminAccessCodes":["ADMIN_ACCESS_LEVEL_1","ADMIN_ACCESS_LEVEL_2","ADMIN_ACCESS_LEVEL_3"],"inputTypes":["MOVE","USE_SKILL","NARRATIVE_CHOICE"]}'::jsonb
    )
on conflict (code) do update set
    version = excluded.version,
    display_name = excluded.display_name,
    is_enabled = excluded.is_enabled,
    metadata = excluded.metadata,
    updated_at = now();

update campaign_template_catalog
   set is_enabled = false,
       updated_at = now()
 where code <> 'codria-v4'
   and is_enabled;

-- Keep this seed safely re-runnable when upgrading a database whose disabled
-- compatibility rows previously occupied display orders 70-100.
update ability_catalog
   set display_order = display_order + 1000
 where code in ('SEARCH', 'SELECT_ALL', 'ATTACK', 'INTERACT', 'NEGOTIATE', 'REST');

insert into ability_catalog (
    code, display_name, description, target_mode, focus_cost,
    min_range, max_range, is_enabled, display_order, metadata
)
values
    ('MOVE', 'Move', 'Traverse a legal safe path without a D20 or campaign-turn cost.', 'tile', 0, 0, 5, true, 10, '{"canonicalInputType":"MOVE","campaignTurnConsumed":false,"d20":false}'::jsonb),
    ('COPY', 'Copy', 'Duplicate one eligible trace or bounded world state.', 'entity_and_tile', 1, 0, 4, true, 20, '{"canonicalInputType":"USE_SKILL","technicalDebtTracked":true}'::jsonb),
    ('DELETE', 'Delete', 'Assert a boundary or sever one eligible influence without implying mandatory combat.', 'entity', 1, 0, 3, true, 30, '{"canonicalInputType":"USE_SKILL","technicalDebtTracked":true}'::jsonb),
    ('CONNECT', 'Connect', 'Attempt understanding, alliance, or a bounded semantic connection.', 'entity', 2, 0, 5, true, 40, '{"canonicalInputType":"USE_SKILL","temporary":true,"technicalDebtTracked":true}'::jsonb),
    ('RESTORE', 'Restore', 'Append an explicit reconciliation or recovery of a permitted prior state.', 'entity', 3, 0, null, true, 50, '{"canonicalInputType":"USE_SKILL","compensating":true,"technicalDebtTracked":true}'::jsonb),
    ('UNDO', 'Undo', 'Append compensation for the immediately preceding reversible result without rewinding history.', 'none', 3, 0, null, true, 60, '{"canonicalInputType":"USE_SKILL","compensating":true,"technicalDebtTracked":true}'::jsonb),
    ('SEARCH', 'Search', 'Investigate one server-selected trace, actor, or bounded ambient event.', 'none', 1, 0, null, true, 70, '{"canonicalInputType":"USE_SKILL","ambientTargeting":true}'::jsonb),
    ('SELECT_ALL', 'Select All', 'Apply broad attention to one server-bounded local scene.', 'none', 3, 0, null, true, 80, '{"canonicalInputType":"USE_SKILL","ambientTargeting":true}'::jsonb),
    ('ATTACK', 'Attack (legacy)', 'Disabled compatibility row. Combat is a possible story context, not a required verb.', 'entity', 0, 1, 1, false, 90, '{"legacyCompatibility":true,"canonical":false}'::jsonb),
    ('INTERACT', 'Interact (legacy)', 'Disabled compatibility row. Investigation is handled by a sealed narrative choice.', 'entity', 0, 0, 2, false, 100, '{"legacyCompatibility":true,"canonical":false}'::jsonb),
    ('NEGOTIATE', 'Negotiate (legacy)', 'Disabled compatibility row. Dialogue is handled by a sealed narrative choice.', 'entity', 0, 0, 2, false, 110, '{"legacyCompatibility":true,"canonical":false}'::jsonb),
    ('REST', 'Rest (legacy)', 'Disabled compatibility row. It is not a Codria v4 canonical input.', 'none', 0, 0, null, false, 120, '{"legacyCompatibility":true,"canonical":false}'::jsonb)
on conflict (code) do update set
    display_name = excluded.display_name,
    description = excluded.description,
    target_mode = excluded.target_mode,
    focus_cost = excluded.focus_cost,
    min_range = excluded.min_range,
    max_range = excluded.max_range,
    is_enabled = excluded.is_enabled,
    display_order = excluded.display_order,
    metadata = excluded.metadata;

insert into biome_catalog (code, display_name, display_name_ko, metadata) values
    ('temperate_forest_field','Temperate Forest Field','온대 숲과 들판','{}'),
    ('river_wetland','River Wetland','강과 습지','{}'),
    ('arid_desert','Arid Desert','건조 사막','{}'),
    ('frost_highland','Frost Highland','설원 고지','{}'),
    ('subterranean_cavern','Subterranean Cavern','지하 동굴','{}'),
    ('ancient_ruins','Ancient Ruins','고대 유적','{}')
on conflict (code) do update set display_name=excluded.display_name, display_name_ko=excluded.display_name_ko, metadata=excluded.metadata;

insert into campaign_region_role_catalog (code, phase_no, display_name_ko, metadata) values
    ('ARRIVAL_CATALYST',1,'도착의 촉매','{"generative":true}'),
    ('LOCAL_STAKES',2,'지역의 삶과 대가','{"generative":true,"adminAccess":"ADMIN_ACCESS_LEVEL_1"}'),
    ('RELATIONSHIP_CONFLICT',3,'관계와 약속의 충돌','{"generative":true,"adminAccess":"ADMIN_ACCESS_LEVEL_2"}'),
    ('HIDDEN_TRUTH',4,'감춰진 원인의 발견','{"generative":true}'),
    ('CONSEQUENCE_RETURN',5,'되돌아온 선택의 결과','{"generative":true,"adminAccess":"ADMIN_ACCESS_LEVEL_3"}'),
    ('FINAL_CONVERGENCE',6,'선택의 최종 수렴','{"generative":true}')
on conflict (code) do update set phase_no=excluded.phase_no, display_name_ko=excluded.display_name_ko, metadata=excluded.metadata;

insert into campaign_phase_catalog (code, display_order, display_name_ko) values
    ('codria_crash',1,'코드리아 추락과 관리자 키보드 각성'),
    ('first_region_problem',2,'붕괴 현상과 첫 지역 문제'),
    ('admin_access_1',3,'관리자 권한 I 획득'),
    ('admin_access_2',4,'관리자 권한 II 획득'),
    ('internal_cause',5,'관리자 통제 시스템 내부 원인 확인'),
    ('technical_debt_return',6,'기술 부채와 과거 선택의 역류'),
    ('admin_access_3',7,'관리자 권한 III 획득'),
    ('root_system_entry',8,'루트 시스템 진입'),
    ('final_deployment',9,'최종 배치와 결말')
on conflict (code) do update set display_order=excluded.display_order, display_name_ko=excluded.display_name_ko;

insert into ending_catalog (code, category, display_name_ko, description_ko) values
    ('ENDING_REWEAVE_TOGETHER','reconciliation','함께 다시 잇기','관계와 세계의 상처를 함께 엮어 새 약속을 만든다.'),
    ('ENDING_OPEN_FRONTIER','freedom','열린 변경','세계가 위험과 선택권을 함께 품도록 경계를 연다.'),
    ('ENDING_KEEP_THE_PROMISE','guardianship','약속을 지키는 이','한 약속의 책임을 받아들이고 세계의 수호자로 남는다.'),
    ('ENDING_CUT_THE_CYCLE','release','되풀이 끊기','오래된 순환의 핵심을 끊고 다음 세대에 빈자리를 돌려준다.'),
    ('ENDING_PRESERVE_THE_SCARS','memory','상처를 기억하기','완전한 복구 대신 세계가 겪은 상처와 증언을 보존한다.'),
    ('ENDING_WALK_BETWEEN_WORLDS','return','세계 사이를 걷기','두 세계를 잇는 통로와 책임을 함께 선택한다.'),
    ('ENDING_EMERGENCY_WITHDRAWAL','emergency','긴급 이탈','턴 한계에서 더 큰 붕괴를 막기 위해 안전하게 이탈한다.')
on conflict (code) do update set category=excluded.category, display_name_ko=excluded.display_name_ko, description_ko=excluded.description_ko;

insert into entity_kind_catalog (code, display_name, is_actor, can_occupy_tile, metadata)
values
    ('PLAYER', 'Player', true, true, '{}'::jsonb),
    ('COMPANION', 'Companion', true, true, '{}'::jsonb),
    ('NPC', 'NPC', true, true, '{}'::jsonb),
    ('ENEMY', 'Enemy', true, true, '{}'::jsonb),
    ('PROP', 'Prop', false, true, '{}'::jsonb),
    ('ITEM', 'World Item', false, true, '{}'::jsonb),
    ('HAZARD', 'Hazard', false, true, '{}'::jsonb),
    ('PORTAL', 'Portal', false, false, '{}'::jsonb),
    ('DECORATION', 'Decoration', false, false, '{}'::jsonb)
on conflict (code) do update set
    display_name = excluded.display_name,
    is_actor = excluded.is_actor,
    can_occupy_tile = excluded.can_occupy_tile,
    metadata = excluded.metadata;

insert into item_catalog (
    code, display_name, description, asset_id, is_stackable, max_stack, base_properties
)
values
    ('RUNE_BOOK', 'Unwritten Ledger', 'A small copyable memory ledger whose meaning is supplied by the seeded campaign.', 'item.rune-book.v1', false, 1, '{"quest_item":true}'::jsonb),
    ('CRATE', 'Supply Crate', 'A movable container represented by the Ninja Adventure asset pack.', 'item.crate.v1', false, 1, '{"container":true}'::jsonb),
    ('FOCUS_SHARD', 'Focus Shard', 'Restores one point of focus when consumed.', 'item.focus-shard.v1', true, 20, '{"consumable":true,"focus_delta":1}'::jsonb),
    ('FIELD_RATION', 'Field Ration', 'A compact restorative ration.', 'item.field-ration.v1', true, 10, '{"consumable":true,"hp_delta":2}'::jsonb)
on conflict (code) do update set
    display_name = excluded.display_name,
    description = excluded.description,
    asset_id = excluded.asset_id,
    is_stackable = excluded.is_stackable,
    max_stack = excluded.max_stack,
    base_properties = excluded.base_properties;

insert into event_type_catalog (code, display_name, description, is_system)
values
    ('TURN_COMMITTED', 'Turn Committed', 'An authoritative turn was committed.', true),
    ('GENERATION_PLAN_VALIDATED', 'Generation Plan Validated', 'A run-scoped campaign plan passed schema and semantic validation.', true),
    ('GENERATION_PLAN_FALLBACK', 'Generation Plan Fallback', 'A deterministic run plan replaced an invalid or unavailable model proposal.', true),
    ('SLOT_BOUND', 'Placement Slot Bound', 'A generated plan node was bound to an immutable world placement slot.', true),
    ('PROGRESS_STATE_CHANGED', 'Progress State Changed', 'Generic campaign progress or convergence state advanced.', true),
    ('RESUME_VALIDATED', 'Resume Validated', 'A deep save snapshot passed checksum, plan, layout, and cursor validation.', true),
    ('RESUME_REJECTED', 'Resume Rejected', 'A save snapshot failed one or more deep-resume validations.', true),
    ('RUN_COMPLETED', 'Run Completed', 'The campaign run reached an ending.', true),
    ('RUN_ABANDONED', 'Run Abandoned', 'The player abandoned the run.', true),
    ('AREA_ENTERED', 'Area Entered', 'An actor moved into another pre-generated area.', false),
    ('ENTITY_MOVED', 'Entity Moved', 'An entity changed its current position.', false),
    ('ENTITY_COPIED', 'Entity Copied', 'An eligible entity was cloned.', false),
    ('ENTITY_DELETED', 'Entity Deleted', 'An eligible entity was deactivated.', false),
    ('ENTITY_INTERACTED', 'Entity Interacted', 'A nearby entity was inspected through a validated meaningful action.', false),
    ('ITEM_ACQUIRED', 'Item Acquired', 'An item entered an inventory.', false),
    ('ITEM_CONSUMED', 'Item Consumed', 'An item stack or instance was consumed.', false),
    ('QUEST_STARTED', 'Quest Started', 'A quest became active.', false),
    ('QUEST_UPDATED', 'Quest Updated', 'Quest or objective progress changed.', false),
    ('QUEST_COMPLETED', 'Quest Completed', 'A quest was completed.', false),
    ('NPC_MEMORY_ADDED', 'NPC Memory Added', 'A durable NPC memory was recorded.', true),
    ('NPC_MEMORY_EXPIRED', 'NPC Memory Expired', 'A bounded NPC memory reached its declared horizon.', true),
    ('RELATIONSHIP_CHANGED', 'Relationship Changed', 'NPC relationship values changed.', false),
    ('RUMOR_SPREAD', 'Rumor Spread', 'A rumor moved between actors.', false),
    ('FACT_ESTABLISHED', 'Fact Established', 'A canonical world fact was written.', true),
    ('CONSEQUENCE_APPLIED', 'Consequence Applied', 'A mechanical consequence budget was spent.', true),
    ('CONNECTION_CREATED', 'Connection Created', 'Two eligible entities received a temporary connection.', false),
    ('CONNECTION_REMOVED', 'Connection Removed', 'A temporary connection expired or was compensated.', false),
    ('CONNECTION_EXPIRED', 'Connection Expired', 'A temporary semantic connection reached its expiry turn.', true),
    ('ENTITY_RESTORED', 'Entity Restored', 'Recent permitted damage or removal was compensated.', false),
    ('STORY_BEAT_CHANGED', 'Story Beat Changed', 'The authoritative campaign director advanced a required beat.', true),
    ('OPEN_LOOP_CREATED', 'Open Loop Created', 'A bounded unresolved hook was committed.', true),
    ('OPEN_LOOP_CLOSED', 'Open Loop Closed', 'A bounded hook was resolved or closed during convergence.', true),
    ('RUMOR_ADDED', 'Rumor Added', 'A validated noncanonical rumor was committed.', false),
    ('RUMOR_CLOSED', 'Rumor Closed', 'A bounded rumor was closed during convergence.', true),
    ('SLOT_ENRICHED', 'Slot Enriched', 'A validated asset was placed into a pre-generated slot.', false),
    ('REVERSAL_APPLIED', 'Reversal Applied', 'Restore or Undo appended a compensating event.', true),
    ('CAMPAIGN_METRICS_CHANGED', 'Campaign Metrics Changed', 'Authoritative campaign metrics changed after a meaningful action.', true),
    ('ENCOUNTER_RESOLVED', 'Encounter Resolved', 'A staged travel encounter consumed one meaningful D20 turn.', true),
    ('NEGOTIATION_RESOLVED', 'Negotiation Resolved', 'A bounded NPC negotiation changed authoritative relationship state.', false),
    ('VISUAL_INTENT_RECORDED', 'Visual Intent Recorded', 'Narrative visual intent was recorded without changing geometry.', false),
    ('QUEST_CLOSED', 'Quest Closed', 'A bounded quest was completed, failed, or closed during convergence.', true),
    ('ADMIN_ACCESS_ACQUIRED', 'Administrator Access Acquired', 'One ordered administrator access level was acquired.', true),
    ('MAJOR_CHOICE_RECORDED', 'Major Choice Recorded', 'A player choice was retained for later callbacks.', true),
    ('REGION_OUTCOME_RECORDED', 'Region Outcome Recorded', 'A fixed Codria region axis received a new outcome revision.', true),
    ('ABILITY_USAGE_RECORDED', 'Ability Usage Recorded', 'A committed keyboard skill use was normalized for ending inputs.', true),
    ('TECHNICAL_DEBT_CHANGED', 'Technical Debt Changed', 'A causal technical-debt delta and deferred consequence were recorded.', true),
    ('DEFERRED_CONSEQUENCE_RESOLVED', 'Deferred Consequence Resolved', 'An explicit recovery action resolved a deferred consequence.', true)
on conflict (code) do update set
    display_name = excluded.display_name,
    description = excluded.description,
    is_system = excluded.is_system;

commit;
