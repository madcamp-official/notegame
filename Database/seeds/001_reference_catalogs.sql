begin;

set local search_path = keyboard_wanderer, public;

insert into campaign_template_catalog (code, version, display_name, is_enabled, metadata)
values
    (
        'keyboard-wanderer-generative',
        'generative-run.v1',
        'Keyboard Wanderer: Generative TRPG',
        true,
        '{"language":"ko-KR","turnRange":[30,50],"defaultTurns":40,"biomeCount":6,"sealedWorld":true,"runScopedPlan":true}'::jsonb
    )
on conflict (code) do update set
    version = excluded.version,
    display_name = excluded.display_name,
    is_enabled = excluded.is_enabled,
    metadata = excluded.metadata,
    updated_at = now();

insert into ability_catalog (
    code, display_name, description, target_mode, focus_cost,
    min_range, max_range, is_enabled, display_order, metadata
)
values
    ('MOVE', 'Move', 'Move the player over a legal path costing at most five movement points.', 'tile', 0, 0, 5, true, 10, '{"mvp":true}'::jsonb),
    ('COPY', 'Copy', 'Clone an eligible entity into an unoccupied destination tile.', 'entity_and_tile', 1, 0, 4, true, 20, '{"mvp":true}'::jsonb),
    ('DELETE', 'Delete', 'Remove an unprotected entity from the current run.', 'entity', 1, 0, 3, true, 30, '{"mvp":true}'::jsonb),
    ('CONNECT', 'Connect', 'Create a temporary semantic connection between eligible world objects.', 'entity', 2, 0, 5, true, 40, '{"mvp":true,"temporary":true}'::jsonb),
    ('RESTORE', 'Restore', 'Restore permitted recent damage or removal from an authoritative snapshot.', 'entity', 3, 0, null, true, 50, '{"mvp":true,"compensating":true}'::jsonb),
    ('UNDO', 'Undo', 'Append compensation for the immediately preceding reversible result without rewinding history.', 'none', 3, 0, null, true, 60, '{"mvp":true,"compensating":true}'::jsonb),
    ('ATTACK', 'Attack', 'Deal bounded damage to one adjacent hostile actor.', 'entity', 0, 1, 1, true, 70, '{"contextual":true}'::jsonb),
    ('INTERACT', 'Interact / Investigate', 'Inspect a nearby NPC or evidence object; investigate is an API alias.', 'entity', 0, 0, 2, true, 80, '{"contextual":true,"aliases":["investigate"]}'::jsonb),
    ('NEGOTIATE', 'Negotiate', 'Resolve one bounded agreement with a nearby non-hostile NPC.', 'entity', 0, 0, 2, true, 90, '{"contextual":true}'::jsonb),
    ('REST', 'Rest', 'Spend one meaningful turn to recover bounded focus and health.', 'none', 0, 0, null, true, 100, '{"contextual":true}'::jsonb)
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
    ('LOCAL_STAKES',2,'지역의 삶과 대가','{"generative":true,"milestone":"MILESTONE_TOKEN_1"}'),
    ('RELATIONSHIP_CONFLICT',3,'관계와 약속의 충돌','{"generative":true,"milestone":"MILESTONE_TOKEN_2"}'),
    ('HIDDEN_TRUTH',4,'감춰진 원인의 발견','{"generative":true}'),
    ('CONSEQUENCE_RETURN',5,'되돌아온 선택의 결과','{"generative":true,"milestone":"MILESTONE_TOKEN_3"}'),
    ('FINAL_CONVERGENCE',6,'선택의 최종 수렴','{"generative":true}')
on conflict (code) do update set phase_no=excluded.phase_no, display_name_ko=excluded.display_name_ko, metadata=excluded.metadata;

insert into campaign_phase_catalog (code, display_order, display_name_ko) values
    ('arrival',1,'낯선 세계의 징후'), ('local_stakes',2,'눈앞의 삶과 대가'),
    ('relationship_conflict',3,'얽힌 약속의 충돌'), ('hidden_truth',4,'감춰진 원인의 발견'),
    ('consequence_return',5,'되돌아온 선택'), ('final_convergence',6,'모든 갈래의 수렴')
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
    ('QUEST_CLOSED', 'Quest Closed', 'A bounded quest was completed, failed, or closed during convergence.', true)
on conflict (code) do update set
    display_name = excluded.display_name,
    description = excluded.description,
    is_system = excluded.is_system;

commit;
