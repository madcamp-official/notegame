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

rollback;
