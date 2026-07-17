begin;

set local search_path = keyboard_wanderer, public;

do $$
declare
    test_owner uuid := gen_random_uuid();
    test_campaign uuid := gen_random_uuid();
    test_run uuid := gen_random_uuid();
    test_world uuid := gen_random_uuid();
    test_region uuid := gen_random_uuid();
    area_arrival uuid := gen_random_uuid();
    area_stakes uuid := gen_random_uuid();
    area_relationship uuid := gen_random_uuid();
    area_truth uuid := gen_random_uuid();
    area_consequence uuid := gen_random_uuid();
    area_finale uuid := gen_random_uuid();
    poi_bug_forest uuid := gen_random_uuid();
    poi_buffer_village uuid := gen_random_uuid();
    poi_deadlock_city uuid := gen_random_uuid();
    poi_data_library uuid := gen_random_uuid();
    poi_legacy_citadel uuid := gen_random_uuid();
    poi_root_system uuid := gen_random_uuid();
    test_slot uuid := gen_random_uuid();
    test_player uuid := gen_random_uuid();
    test_npc uuid := gen_random_uuid();
    test_relationship uuid := gen_random_uuid();
    test_safe_travel uuid := gen_random_uuid();
    test_plan uuid := gen_random_uuid();
    test_turn uuid := gen_random_uuid();
    test_hook uuid := gen_random_uuid();
    test_debt_entry uuid := gen_random_uuid();
    test_save_slot uuid := gen_random_uuid();
    test_snapshot uuid := gen_random_uuid();
    test_last_event bigint;
    test_time timestamptz := clock_timestamp();
    expected_layout_hash constant text := repeat('a', 64);
    expected_plan_hash constant text := repeat('b', 64);
    state_hash_before constant text := repeat('c', 64);
    state_hash_after constant text := repeat('d', 64);
    snapshot_checksum constant text := repeat('e', 64);
    biome_codes text[];
    role_codes text[];
    axis_codes text[];
    access_codes text[];
begin
    perform set_config('app.user_id', test_owner::text, true);

    select array_agg(code order by code)
      into biome_codes
      from biome_catalog;
    if biome_codes is distinct from array[
        'ancient_ruins',
        'arid_desert',
        'frost_highland',
        'river_wetland',
        'subterranean_cavern',
        'temperate_forest_field'
    ]::text[] then
        raise exception 'the authoritative biome catalog must contain exactly the six supported biomes: %', biome_codes;
    end if;

    select array_agg(code order by phase_no, code)
      into role_codes
      from campaign_region_role_catalog
      where code in (
          'ARRIVAL_CATALYST', 'LOCAL_STAKES', 'RELATIONSHIP_CONFLICT',
          'HIDDEN_TRUTH', 'CONSEQUENCE_RETURN', 'FINAL_CONVERGENCE'
      );
    if role_codes is distinct from array[
        'ARRIVAL_CATALYST', 'LOCAL_STAKES', 'RELATIONSHIP_CONFLICT',
        'HIDDEN_TRUTH', 'CONSEQUENCE_RETURN', 'FINAL_CONVERGENCE'
    ]::text[] then
        raise exception 'the six generic progression roles are incomplete: %', role_codes;
    end if;

    select array_agg(code order by display_order)
      into axis_codes
      from campaign_region_axis_catalog;
    if axis_codes is distinct from array[
        'REGION_BUG_FOREST', 'REGION_BUFFER_VILLAGE', 'REGION_DEADLOCK_CITY',
        'REGION_DATA_GRAND_LIBRARY', 'REGION_LEGACY_CITADEL', 'REGION_ROOT_SYSTEM'
    ]::text[] then
        raise exception 'the six fixed Codria region axes are incomplete: %', axis_codes;
    end if;

    select array_agg(code order by access_level)
      into access_codes
      from admin_access_level_catalog;
    if access_codes is distinct from array[
        'ADMIN_ACCESS_LEVEL_1', 'ADMIN_ACCESS_LEVEL_2', 'ADMIN_ACCESS_LEVEL_3'
    ]::text[] then
        raise exception 'the three fixed administrator access levels are incomplete: %', access_codes;
    end if;

    if (select display_name_ko from product_identity_catalog where code = 'PROTAGONIST_NUPJUKYI') <> '넙죽이'
       or not exists (select 1 from product_identity_catalog where code = 'WORLD_CODRIA') then
        raise exception 'the fixed Codria and Nupjukyi product identities are incomplete';
    end if;

    insert into profiles (id, display_name, locale)
    values (test_owner, 'Generative Schema Smoke', 'ko-KR');

    insert into campaigns (
        id, owner_id, title, world_seed, turn_limit, status,
        ruleset_version, premise, settings, created_at, updated_at
    ) values (
        test_campaign, test_owner, '넙죽이와 붕괴한 코드 왕국', 20260717, 40, 'active',
        'codria-campaign.v4', '넙죽이가 코드리아의 관리자 권한 세 단계를 획득한다.',
        '{"templateId":"codria-v4","sealedWorld":true,"worldContractCode":"WORLD_CODRIA"}'::jsonb,
        test_time, test_time
    );

    -- The run UUID is preallocated. The run-scoped world is legal before the run
    -- row only because the matching relationship is checked by a deferred trigger.
    insert into worlds (
        id, campaign_id, owner_id, world_scope, run_scope_key,
        generator_version, layout_hash, width, height, map_json,
        generation_metadata, generated_at, created_at, updated_at
    ) values (
        test_world, test_campaign, test_owner, 'run', test_run,
        'codria-world.v7', expected_layout_hash, 160, 160,
        '{"format":"generative-smoke-v5","coordinateSpace":"world_global","sealed":true}'::jsonb,
        '{"generatedOnce":true,"placementSlotsSealed":true,"routesSealed":true}'::jsonb,
        test_time, test_time, test_time
    );

    insert into regions (
        id, world_id, owner_id, region_key, display_name, region_kind,
        origin_x, origin_y, width, height, layout_hash, map_json,
        created_at, updated_at
    ) values (
        test_region, test_world, test_owner, 'world.main', 'Generated Boundary', 'overworld',
        0, 0, 160, 160, repeat('1', 64),
        '{"sealed":true,"areaCount":6}'::jsonb, test_time, test_time
    );

    insert into areas (
        id, world_id, region_id, owner_id, area_key, display_name, area_kind,
        origin_x, origin_y, width, height, entry_x, entry_y, exit_x, exit_y,
        layout_hash, tile_json, created_at, updated_at
    ) values
    (area_arrival, test_world, test_region, test_owner, 'area.bug-forest', '버그 숲', 'campaign_region',
     0, 0, 20, 20, 1, 1, 18, 18, repeat('2', 64), '{"biomeId":"temperate_forest_field","sealed":true}'::jsonb, test_time, test_time),
    (area_stakes, test_world, test_region, test_owner, 'area.buffer-village', '버퍼 마을', 'campaign_region',
     30, 0, 20, 20, 1, 1, 18, 18, repeat('3', 64), '{"biomeId":"river_wetland","sealed":true}'::jsonb, test_time, test_time),
    (area_relationship, test_world, test_region, test_owner, 'area.deadlock-city', '데드락 시티', 'campaign_region',
     60, 0, 20, 20, 1, 1, 18, 18, repeat('4', 64), '{"biomeId":"arid_desert","sealed":true}'::jsonb, test_time, test_time),
    (area_truth, test_world, test_region, test_owner, 'area.data-grand-library', '데이터 대도서관', 'campaign_region',
     0, 30, 20, 20, 1, 1, 18, 18, repeat('5', 64), '{"biomeId":"frost_highland","sealed":true}'::jsonb, test_time, test_time),
    (area_consequence, test_world, test_region, test_owner, 'area.legacy-citadel', '레거시 성채', 'campaign_region',
     30, 30, 20, 20, 1, 1, 18, 18, repeat('6', 64), '{"biomeId":"subterranean_cavern","sealed":true}'::jsonb, test_time, test_time),
    (area_finale, test_world, test_region, test_owner, 'area.root-system', '루트 시스템', 'campaign_region',
     60, 30, 20, 20, 1, 1, 18, 18, repeat('7', 64), '{"biomeId":"ancient_ruins","sealed":true}'::jsonb, test_time, test_time);

    insert into world_area_descriptors (
        area_id, world_id, owner_id, area_key, biome_id, campaign_role, descriptor_json
    ) values
    (area_arrival, test_world, test_owner, 'area.bug-forest', 'temperate_forest_field', 'ARRIVAL_CATALYST', '{"axis":"REGION_BUG_FOREST"}'::jsonb),
    (area_stakes, test_world, test_owner, 'area.buffer-village', 'river_wetland', 'LOCAL_STAKES', '{"axis":"REGION_BUFFER_VILLAGE"}'::jsonb),
    (area_relationship, test_world, test_owner, 'area.deadlock-city', 'arid_desert', 'RELATIONSHIP_CONFLICT', '{"axis":"REGION_DEADLOCK_CITY"}'::jsonb),
    (area_truth, test_world, test_owner, 'area.data-grand-library', 'frost_highland', 'HIDDEN_TRUTH', '{"axis":"REGION_DATA_GRAND_LIBRARY"}'::jsonb),
    (area_consequence, test_world, test_owner, 'area.legacy-citadel', 'subterranean_cavern', 'CONSEQUENCE_RETURN', '{"axis":"REGION_LEGACY_CITADEL"}'::jsonb),
    (area_finale, test_world, test_owner, 'area.root-system', 'ancient_ruins', 'FINAL_CONVERGENCE', '{"axis":"REGION_ROOT_SYSTEM"}'::jsonb);

    insert into area_connections (
        owner_id, world_id, from_area_id, to_area_id,
        from_x, from_y, to_x, to_y, direction, traversal_kind, requirement_json
    ) values
    (test_owner, test_world, area_arrival, area_stakes, 18, 18, 1, 1, 'bidirectional', 'safe_route',
     '{"routeId":"route.arrival.stakes","gated":false,"requirements":{"requiresProgressLevel":0,"requiresProgressTokens":[]},"coordinateSpace":"area_local"}'::jsonb),
    (test_owner, test_world, area_consequence, area_finale, 18, 18, 1, 1, 'bidirectional', 'safe_route',
     '{"routeId":"route.legacy.root","gated":true,"requirements":{"requiresAdminAccess":["ADMIN_ACCESS_LEVEL_1","ADMIN_ACCESS_LEVEL_2","ADMIN_ACCESS_LEVEL_3"],"requiresEssentialClue":true},"coordinateSpace":"area_local"}'::jsonb);

    insert into world_pois (
        id, world_id, owner_id, area_id, poi_key, poi_kind, display_name,
        x, y, biome_id, campaign_role, visual_intent,
        is_gated, gate_requirements, tags
    ) values
    (poi_bug_forest, test_world, test_owner, area_arrival, 'bug-forest-anchor', 'axis_anchor', '버그 숲 앵커',
     1, 1, 'temperate_forest_field', 'ARRIVAL_CATALYST', 'navigation anchor',
     false, '{}'::jsonb, '["entry"]'::jsonb),
    (poi_buffer_village, test_world, test_owner, area_stakes, 'buffer-village-anchor', 'axis_anchor', '버퍼 마을 앵커',
     1, 1, 'river_wetland', 'LOCAL_STAKES', 'settlement anchor',
     false, '{}'::jsonb, '["settlement"]'::jsonb),
    (poi_deadlock_city, test_world, test_owner, area_relationship, 'deadlock-city-anchor', 'axis_anchor', '데드락 시티 앵커',
     1, 1, 'arid_desert', 'RELATIONSHIP_CONFLICT', 'negotiation anchor',
     false, '{}'::jsonb, '["negotiation"]'::jsonb),
    (poi_data_library, test_world, test_owner, area_truth, 'data-library-anchor', 'axis_anchor', '데이터 대도서관 앵커',
     1, 1, 'frost_highland', 'HIDDEN_TRUTH', 'investigation anchor',
     false, '{}'::jsonb, '["essential-clue"]'::jsonb),
    (poi_legacy_citadel, test_world, test_owner, area_consequence, 'legacy-citadel-anchor', 'axis_anchor', '레거시 성채 앵커',
     1, 1, 'subterranean_cavern', 'CONSEQUENCE_RETURN', 'technical debt anchor',
     false, '{}'::jsonb, '["technical-debt"]'::jsonb),
    (poi_root_system, test_world, test_owner, area_finale, 'root-system-anchor', 'axis_anchor', '루트 시스템 앵커',
     1, 1, 'ancient_ruins', 'FINAL_CONVERGENCE', 'sealed finale anchor',
     true, '{"requiresAdminAccess":["ADMIN_ACCESS_LEVEL_1","ADMIN_ACCESS_LEVEL_2","ADMIN_ACCESS_LEVEL_3"],"requiresEssentialClue":true}'::jsonb,
     '["finale","gated"]'::jsonb);

    insert into world_region_axis_bindings (
        world_id, owner_id, region_axis_code, area_id, terrain_biome_id,
        primary_poi_id, binding_seed, binding_metadata
    ) values
    (test_world, test_owner, 'REGION_BUG_FOREST', area_arrival, 'temperate_forest_field', poi_bug_forest, 101, '{"seedBound":true}'::jsonb),
    (test_world, test_owner, 'REGION_BUFFER_VILLAGE', area_stakes, 'river_wetland', poi_buffer_village, 102, '{"seedBound":true}'::jsonb),
    (test_world, test_owner, 'REGION_DEADLOCK_CITY', area_relationship, 'arid_desert', poi_deadlock_city, 103, '{"seedBound":true}'::jsonb),
    (test_world, test_owner, 'REGION_DATA_GRAND_LIBRARY', area_truth, 'frost_highland', poi_data_library, 104, '{"seedBound":true}'::jsonb),
    (test_world, test_owner, 'REGION_LEGACY_CITADEL', area_consequence, 'subterranean_cavern', poi_legacy_citadel, 105, '{"seedBound":true}'::jsonb),
    (test_world, test_owner, 'REGION_ROOT_SYSTEM', area_finale, 'ancient_ruins', poi_root_system, 106, '{"seedBound":true}'::jsonb);

    insert into placement_slots (
        id, slot_key, owner_id, world_id, area_id, slot_kind,
        x, y, tags, allowed_asset_ids, biome_id, campaign_role,
        purpose, reserved_for, is_gated, gate_requirements
    ) values (
        test_slot, 'slot.0123456789abcdefabcd', test_owner, test_world, area_arrival, 'npc',
        3, 3, '["ambient","witness"]'::jsonb, '["npc.villager.green.v1"]'::jsonb,
        'temperate_forest_field', 'ARRIVAL_CATALYST', 'ambient', null, false, '{}'::jsonb
    );

    -- Dot-separated keys are the canonical generative beat identifiers.
    insert into campaign_story_beats (
        campaign_id, owner_id, beat_key, title, description,
        required_ability, target_turn, display_order, created_at, updated_at
    ) values (
        test_campaign, test_owner, 'beat.arrival_catalyst', 'The First Witness',
        'Confirm one bounded local stake.', 'connect', 1, 0, test_time, test_time
    );

    insert into runs (
        id, campaign_id, world_id, owner_id, status, version,
        current_turn, turn_limit, focus, pressure, active_area_id,
        world_state, resolution_seed, started_at, created_at, updated_at
    ) values (
        test_run, test_campaign, test_world, test_owner, 'playing', 1,
        0, 40, 8, 0, area_arrival,
        jsonb_build_object(
            'campaignTitle', '넙죽이와 붕괴한 코드 왕국',
            'worldContractCode', 'WORLD_CODRIA',
            'protagonistContractCode', 'PROTAGONIST_NUPJUKYI',
            'campaignContentHash', expected_plan_hash,
            'currentAct', 'arrival',
            'progressLevel', 0,
            'progressTokens', '[]'::jsonb,
            'world', jsonb_build_object('layoutHash', expected_layout_hash, 'generatedOnce', true)
        ),
        'server-only-generative-smoke-seed', test_time, test_time, test_time
    );

    insert into run_generation_plans (
        id, run_id, owner_id, world_id, schema_version, generator_version,
        generation_seed, plan_hash, source, fallback_used, validation_status,
        validation_report, validation_errors, plan_json, validated_at, created_at
    ) values (
        test_plan, test_run, test_owner, test_world, 'codria-run-plan.v4',
        'codria-world.v7', 20260717, expected_plan_hash, 'deterministic', false, 'validated',
        '{"schemaValid":true,"immutableGeometryValidated":true,"placementSlotsValidated":true}'::jsonb,
        '[]'::jsonb,
        '{"worldContractCode":"WORLD_CODRIA","protagonistContractCode":"PROTAGONIST_NUPJUKYI","regionAxes":["REGION_BUG_FOREST","REGION_BUFFER_VILLAGE","REGION_DEADLOCK_CITY","REGION_DATA_GRAND_LIBRARY","REGION_LEGACY_CITADEL","REGION_ROOT_SYSTEM"],"adminAccessCodes":["ADMIN_ACCESS_LEVEL_1","ADMIN_ACCESS_LEVEL_2","ADMIN_ACCESS_LEVEL_3"],"beats":["beat.arrival_catalyst"],"endings":["ENDING_EMERGENCY_WITHDRAWAL"]}'::jsonb,
        test_time, test_time
    );

    insert into run_progress_states (
        run_id, owner_id, generation_plan_id, status, current_node_key,
        state_version, last_turn_no, completed_node_keys, failed_node_keys,
        ending_candidate_keys, open_threads, progress_state, rule_state,
        convergence_state, created_at, updated_at
    ) values (
        test_run, test_owner, test_plan, 'active', 'beat.arrival_catalyst',
        1, 0, '[]'::jsonb, '[]'::jsonb,
        '["ENDING_EMERGENCY_WITHDRAWAL"]'::jsonb, '[]'::jsonb,
        '{"level":0,"tokens":[]}'::jsonb,
        '{"focus":8,"pressure":0}'::jsonb,
        '{"selectedEndingId":null,"finaleResolution":null}'::jsonb,
        test_time, test_time
    );

    insert into entities (
        id, owner_id, run_id, world_id, entity_kind, asset_id,
        display_name, is_protected, is_cloneable, is_active, state_json
    ) values
    (test_player, test_owner, test_run, test_world, 'PLAYER', 'player.ninja.green.v1',
     '넙죽이', true, false, true, '{"canonicalIdentity":"PROTAGONIST_NUPJUKYI","hp":10,"maxHp":10,"blocking":true}'::jsonb),
    (test_npc, test_owner, test_run, test_world, 'NPC', 'npc.villager.green.v1',
     'Arrival Witness', true, false, true,
     jsonb_build_object('slotId', 'slot.0123456789abcdefabcd', 'campaignRole', 'ARRIVAL_CATALYST', 'blocking', false));

    insert into actors (
        entity_id, owner_id, run_id, actor_role, hp, max_hp, energy, max_energy
    ) values
    (test_player, test_owner, test_run, 'player', 10, 10, 8, 8),
    (test_npc, test_owner, test_run, 'npc', 8, 8, 0, 0);

    insert into npc_relationships (
        id, owner_id, run_id, subject_actor_id, object_actor_id,
        affinity, trust, fear, relationship_state, notes, last_changed_turn,
        created_at, updated_at
    ) values (
        test_relationship, test_owner, test_run, test_npc, test_player,
        0, 0, 0, 'neutral', '{}'::jsonb, 0, test_time, test_time
    );

    insert into entity_positions (
        entity_id, owner_id, run_id, world_id, area_id, x, y, blocks_movement
    ) values
    (test_player, test_owner, test_run, test_world, area_arrival, 1, 1, true),
    (test_npc, test_owner, test_run, test_world, area_arrival, 3, 3, false);

    insert into run_slot_bindings (
        run_id, owner_id, world_id, generation_plan_id, slot_id,
        binding_key, binding_kind, plan_node_key, entity_id,
        status, activation_turn, binding_payload, created_at, updated_at
    ) values (
        test_run, test_owner, test_world, test_plan, test_slot,
        'entity:arrival-witness', 'entity', 'npc.arrival.witness', test_npc,
        'active', 0, '{"geometryOwnedByWorld":true}'::jsonb, test_time, test_time
    );

    update runs
       set player_entity_id = test_player,
           version = 2,
           world_state = world_state || jsonb_build_object('playerEntityId', test_player)
     where id = test_run and owner_id = test_owner and version = 1;
    if not found then
        raise exception 'the player binding did not advance the run version';
    end if;

    insert into safe_travels (
        id, run_id, owner_id, sequence_no, idempotency_key, request_fingerprint,
        expected_run_version, committed_run_version, from_x, from_y,
        requested_x, requested_y, to_x, to_y, path_cost, travel_time_units,
        cumulative_travel_time_units, entered_area_key, entered_biome_id,
        campaign_role, traversed_area_ids, reached_poi_ids, path_json,
        encounter_opened, encounter_json, campaign_turn_consumed,
        campaign_turn_before, campaign_turn_after, layout_hash,
        command_schema_version, input_type, world_id, destination_area_id,
        turn_context, created_at
    ) values (
        test_safe_travel, test_run, test_owner, 1, 'codria-smoke-move-0001', repeat('8', 64),
        2, 3, 1, 1, 2, 1, 2, 1, 1, 1, 1,
        'area.bug-forest', 'temperate_forest_field', 'ARRIVAL_CATALYST',
        jsonb_build_array(area_arrival::text), '[]'::jsonb,
        '[[1,1],[2,1]]'::jsonb, false, null, false, 0, 0, expected_layout_hash,
        'codria-action.v4', 'MOVE', test_world, area_arrival,
        '{"safe":true,"encounterOpened":false}'::jsonb, test_time
    );

    update entity_positions
       set x = 2, revision = revision + 1
     where entity_id = test_player and owner_id = test_owner and run_id = test_run;

    update runs
       set version = 3,
           world_state = world_state || '{"currentTurn":0,"version":3,"lastInputType":"MOVE"}'::jsonb
     where id = test_run and owner_id = test_owner and version = 2;
    if not found then
        raise exception 'safe MOVE did not advance the run version';
    end if;

    insert into turn_records (
        id, run_id, owner_id, idempotency_key, request_fingerprint,
        expected_run_version, request_json, command_schema_version, input_type,
        skill_id, target_ids, action_context, turn_context,
        campaign_turn_before, campaign_turn_after, campaign_turn_consumed,
        received_at, created_at, updated_at
    ) values (
        test_turn, test_run, test_owner, 'codria-smoke-action-0001', repeat('f', 64),
        3,
        jsonb_build_object(
            'inputType', 'USE_SKILL', 'skillId', 'CONNECT',
            'targetIds', jsonb_build_array(test_npc::text)
        ),
        'codria-action.v4', 'USE_SKILL', 'CONNECT',
        jsonb_build_array(test_npc::text), 'NEGOTIATION',
        jsonb_build_object('areaId', area_arrival, 'regionAxis', 'REGION_BUG_FOREST'),
        0, 0, false, test_time, test_time, test_time
    );

    update runs
       set current_turn = 1,
           version = 4,
           world_state = world_state || '{"currentTurn":1,"version":4,"lastInputType":"USE_SKILL","lastSkillId":"CONNECT"}'::jsonb
     where id = test_run and owner_id = test_owner and version = 3;
    if not found then
        raise exception 'the meaningful USE_SKILL action did not advance the campaign turn';
    end if;

    update turn_records
       set status = 'committed',
           turn_no = 1,
           committed_run_version = 4,
           campaign_turn_after = 1,
           campaign_turn_consumed = true,
           result_json = jsonb_build_object(
               'outcome', 'success',
               'd20', 12,
               'rulesetVersion', 'codria-rules.v4',
               'stateHashBefore', state_hash_before,
               'stateHashAfter', state_hash_after
           ),
           narrative_json = '{"summary":"넙죽이의 CONNECT가 버그 숲의 목격자와 신뢰를 연결했다.","proposedOps":[],"appliedOps":[],"rejectedOps":[]}'::jsonb,
           fallback_used = false,
           model = 'smoke-rule-director',
           completed_at = test_time
     where id = test_turn and owner_id = test_owner;

    insert into turn_rule_resolutions (
        turn_record_id, run_id, owner_id, turn_no, ruleset_version,
        normalized_attempt, d20_raw, modifier_total, modifier_breakdown,
        roll_total, difficulty_class, outcome, consequence_budget,
        costs_json, guaranteed_operations, allowed_effects, state_delta,
        state_hash_before, state_hash_after, rng_audit, created_at
    ) values (
        test_turn, test_run, test_owner, 1, 'keyboard-wanderer-rules.v3',
        '{"ability":"move","abilitySource":"explicit_selection","legalExecution":"move to area-local tile (2,1)"}'::jsonb,
        12, 3, '[{"source":"keyboard_affinity","value":3}]'::jsonb,
        15, 9, 'success', 0,
        '{}'::jsonb, '[{"op":"MOVE","entityId":"player"}]'::jsonb,
        '["entity_moved"]'::jsonb, '{"position":{"from":[1,1],"to":[2,1]}}'::jsonb,
        state_hash_before, state_hash_after,
        '{"algorithm":"sha256_modulo_d20.v2","secretRedacted":true}'::jsonb,
        test_time
    );

    insert into turn_events (
        turn_record_id, run_id, owner_id, event_index, event_type, payload, created_at
    ) values
    (test_turn, test_run, test_owner, 0, 'ENTITY_MOVED',
     '{"type":"entity_moved","from":{"x":1,"y":1},"to":{"x":2,"y":1}}'::jsonb, test_time),
    (test_turn, test_run, test_owner, 1, 'TURN_COMMITTED',
     '{"type":"turn_committed","turnNo":1,"runVersion":3}'::jsonb, test_time);

    select max(id)
      into strict test_last_event
      from turn_events
     where turn_record_id = test_turn;

    update run_progress_states
       set state_version = 2,
           last_turn_no = 1,
           completed_node_keys = '["beat.arrival_catalyst"]'::jsonb,
           current_node_key = 'beat.local_stakes',
           progress_state = '{"level":1,"tokens":["MILESTONE_TOKEN_1"]}'::jsonb,
           rule_state = '{"focus":8,"pressure":0,"lastOutcome":"success"}'::jsonb
     where run_id = test_run and owner_id = test_owner;

    insert into save_slots (
        id, owner_id, campaign_id, slot_no, title, created_at, updated_at
    ) values (
        test_save_slot, test_owner, test_campaign, 1, 'Generative Smoke Autosave', test_time, test_time
    );

    insert into save_snapshots (
        id, slot_id, owner_id, campaign_id, run_id, run_version,
        current_turn, schema_version, state_json, checksum_sha256,
        snapshot_kind, world_id, generation_plan_id, plan_hash, layout_hash,
        last_turn_record_id, last_event_id, resume_metadata, created_at
    ) values (
        test_snapshot, test_save_slot, test_owner, test_campaign, test_run, 3,
        1, 'keyboard-wanderer-save.v3',
        jsonb_build_object(
            'campaignContentHash', expected_plan_hash,
            'currentTurn', 1,
            'version', 3,
            'world', jsonb_build_object('layoutHash', expected_layout_hash),
            'secretFieldsRedacted', jsonb_build_array('resolutionSeed')
        ),
        snapshot_checksum, 'autosave', test_world, test_plan, expected_plan_hash, expected_layout_hash,
        test_turn, test_last_event,
        '{"authoritative":true,"worldGeneratedOnce":true,"secretFieldsRedacted":["resolutionSeed"]}'::jsonb,
        test_time
    );

    update save_slots
       set latest_snapshot_id = test_snapshot
     where id = test_save_slot and owner_id = test_owner;

    insert into resume_validation_records (
        snapshot_id, run_id, owner_id, attempt_no, validation_status,
        observed_checksum_sha256, observed_plan_hash, observed_layout_hash,
        checks_json, errors_json, created_at
    ) values (
        test_snapshot, test_run, test_owner, 1, 'accepted',
        snapshot_checksum, expected_plan_hash, expected_layout_hash,
        '{"checksum":true,"canonicalState":true,"planHash":true,"layoutHash":true,"runVersion":true,"currentTurn":true}'::jsonb,
        '[]'::jsonb, test_time
    );

    if (select count(*) from world_area_descriptors where world_id = test_world) <> 6 then
        raise exception 'the sealed run world did not persist all six biome areas';
    end if;
    if not exists (
        select 1 from worlds
         where id = test_world and world_scope = 'run' and run_scope_key = test_run
    ) then
        raise exception 'the run-scoped world reservation was not persisted';
    end if;
    if not exists (
        select 1 from run_generation_plans
         where id = test_plan and run_id = test_run and plan_hash = expected_plan_hash
           and source = 'deterministic' and validation_status = 'validated'
    ) then
        raise exception 'the sealed generation plan was not persisted';
    end if;
    if not exists (
        select 1 from run_progress_states
         where run_id = test_run and state_version = 2 and last_turn_no = 1
           and current_node_key = 'beat.local_stakes'
    ) then
        raise exception 'the generic run progress projection did not advance';
    end if;
    if not exists (
        select 1 from turn_records tr
        join turn_rule_resolutions rr on rr.turn_record_id = tr.id
         where tr.id = test_turn and tr.status = 'committed'
           and tr.committed_run_version = 3 and rr.d20_raw = 12
           and rr.roll_total = 15 and rr.outcome = 'success'
    ) then
        raise exception 'the committed turn is missing its authoritative Rule Engine resolution';
    end if;
    if not exists (
        select 1 from save_snapshots ss
        join resume_validation_records rv on rv.snapshot_id = ss.id
         where ss.id = test_snapshot and ss.snapshot_kind = 'autosave'
           and ss.plan_hash = expected_plan_hash and ss.layout_hash = expected_layout_hash
           and ss.last_turn_record_id = test_turn and ss.last_event_id = test_last_event
           and rv.validation_status = 'accepted'
    ) then
        raise exception 'the deep snapshot and accepted resume audit are incomplete';
    end if;
    if not exists (
        select 1 from run_summaries
         where id = test_run and version = 3 and current_turn = 1
    ) then
        raise exception 'the client-safe run summary did not expose committed state';
    end if;

    begin
        update worlds
           set layout_hash = repeat('9', 64)
         where id = test_world;
        raise exception 'immutable world layout was unexpectedly updated';
    exception when sqlstate '55000' then
        null;
    end;

    begin
        update placement_slots
           set x = 4
         where id = test_slot;
        raise exception 'immutable placement slot was unexpectedly updated';
    exception when sqlstate '55000' then
        null;
    end;

    begin
        update run_generation_plans
           set plan_json = '{"tampered":true}'::jsonb
         where id = test_plan;
        raise exception 'append-only generation plan was unexpectedly updated';
    exception when sqlstate '55000' then
        null;
    end;

    begin
        update turn_rule_resolutions
           set outcome = 'failure'
         where turn_record_id = test_turn;
        raise exception 'append-only Rule Engine resolution was unexpectedly updated';
    exception when sqlstate '55000' then
        null;
    end;
end
$$;

-- Force both sides of the world-before-run reservation and every deferred
-- snapshot/player reference to execute before the smoke transaction rolls back.
set constraints all immediate;

rollback;
