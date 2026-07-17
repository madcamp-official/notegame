begin;

set local search_path = keyboard_wanderer, public;

do $$
declare
    actual_tables text[];
    actual_columns text[];
    actual_axes text[];
    actual_access_levels text[];
    actual_phases text[];
    forced_rls_count integer;
begin
    select array_agg(code order by display_order)
      into actual_axes
      from campaign_region_axis_catalog;
    if actual_axes is distinct from array[
        'REGION_BUG_FOREST', 'REGION_BUFFER_VILLAGE', 'REGION_DEADLOCK_CITY',
        'REGION_DATA_GRAND_LIBRARY', 'REGION_LEGACY_CITADEL', 'REGION_ROOT_SYSTEM'
    ]::text[] then
        raise exception 'fixed region-axis catalog mismatch: %', actual_axes;
    end if;

    select array_agg(code order by access_level)
      into actual_access_levels
      from admin_access_level_catalog;
    if actual_access_levels is distinct from array[
        'ADMIN_ACCESS_LEVEL_1', 'ADMIN_ACCESS_LEVEL_2', 'ADMIN_ACCESS_LEVEL_3'
    ]::text[] then
        raise exception 'fixed administrator-access catalog mismatch: %', actual_access_levels;
    end if;

    select array_agg(code order by display_order)
      into actual_columns
      from ability_catalog
     where is_enabled;
    if actual_columns is distinct from array[
        'MOVE', 'COPY', 'DELETE', 'CONNECT', 'RESTORE', 'UNDO'
    ]::text[] then
        raise exception 'enabled Codria input/skill catalog mismatch: %', actual_columns;
    end if;

    select array_agg(code order by display_order)
      into actual_phases
      from campaign_phase_catalog
     where code = any (array[
        'codria_crash', 'first_region_problem', 'admin_access_1',
        'admin_access_2', 'internal_cause', 'technical_debt_return',
        'admin_access_3', 'root_system_entry', 'final_deployment'
     ]::text[]);
    if actual_phases is distinct from array[
        'codria_crash', 'first_region_problem', 'admin_access_1',
        'admin_access_2', 'internal_cause', 'technical_debt_return',
        'admin_access_3', 'root_system_entry', 'final_deployment'
    ]::text[] then
        raise exception 'nine-beat Codria phase catalog mismatch: %', actual_phases;
    end if;

    select array_agg(code order by code)
      into actual_columns
      from campaign_template_catalog
     where is_enabled;
    if actual_columns is distinct from array['codria-v4']::text[] then
        raise exception 'enabled campaign template must be exactly codria-v4: %', actual_columns;
    end if;

    if (select display_name_ko from product_identity_catalog where code = 'WORLD_CODRIA') <> '코드리아'
       or (select display_name_ko from product_identity_catalog where code = 'PROTAGONIST_NUPJUKYI') <> '넙죽이'
       or not exists (
           select 1 from product_identity_catalog where code = 'ARTIFACT_ADMIN_KEYBOARD'
       ) then
        raise exception 'fixed product identity catalog mismatch';
    end if;
end
$$;

do $$
declare
    actual_tables text[];
    actual_columns text[];
begin

    select array_agg(tablename order by tablename)
      into actual_tables
      from pg_catalog.pg_tables
     where schemaname = 'keyboard_wanderer'
       and tablename = any (array[
           'world_region_axis_bindings', 'admin_access_acquisition_history',
           'major_choices', 'region_outcomes', 'npc_relationship_history',
           'ability_usage_history', 'unresolved_hooks', 'technical_debt_entries'
       ]::text[]);
    if actual_tables is distinct from array[
        'ability_usage_history', 'admin_access_acquisition_history', 'major_choices',
        'npc_relationship_history', 'region_outcomes', 'technical_debt_entries',
        'unresolved_hooks', 'world_region_axis_bindings'
    ]::text[] then
        raise exception 'Codria v4 authoritative tables are incomplete: %', actual_tables;
    end if;

    select array_agg(column_name order by column_name)
      into actual_columns
      from information_schema.columns
     where table_schema = 'keyboard_wanderer'
       and table_name = 'technical_debt_entries';
    if not array[
        'id', 'run_id', 'turn_id', 'skill_id', 'operation_type', 'target_id',
        'forced_override', 'debt_delta', 'deferred_consequence_type', 'resolved_at'
    ]::text[] <@ actual_columns then
        raise exception 'technical_debt_entries is missing a required causal-ledger field: %', actual_columns;
    end if;

    select array_agg(column_name order by column_name)
      into actual_columns
      from information_schema.columns
     where table_schema = 'keyboard_wanderer'
       and table_name = 'turn_records';
    if not array[
        'command_schema_version', 'input_type', 'skill_id', 'target_ids',
        'action_context', 'turn_context', 'campaign_turn_before',
        'campaign_turn_after', 'campaign_turn_consumed', 'idempotency_key'
    ]::text[] <@ actual_columns then
        raise exception 'turn_records is missing structured USE_SKILL authority: %', actual_columns;
    end if;

    select array_agg(column_name order by column_name)
      into actual_columns
      from information_schema.columns
     where table_schema = 'keyboard_wanderer'
       and table_name = 'safe_travels';
    if not array[
        'command_schema_version', 'input_type', 'world_id',
        'destination_area_id', 'turn_context', 'idempotency_key',
        'campaign_turn_before', 'campaign_turn_after', 'campaign_turn_consumed'
    ]::text[] <@ actual_columns then
        raise exception 'safe_travels is missing structured MOVE authority: %', actual_columns;
    end if;

    if to_regclass('keyboard_wanderer.turn_actions') is not null then
        raise exception 'v1.1 TURN_ACTIONS must not be duplicated beside the two authoritative idempotency ledgers';
    end if;
    if to_regclass('keyboard_wanderer.structured_action_history') is null
       or to_regclass('keyboard_wanderer.run_admin_access_states') is null
       or to_regclass('keyboard_wanderer.current_region_outcomes') is null
       or to_regclass('keyboard_wanderer.technical_debt_summaries') is null then
        raise exception 'one or more Codria v4 read projections are missing';
    end if;
end
$$;

rollback;
