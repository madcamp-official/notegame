begin;

set local search_path = keyboard_wanderer, public;

-- Stable Codria reference vocabularies. They are populated by
-- seeds/001_reference_catalogs.sql after this migration is applied.
create table biome_catalog (
    code text primary key,
    display_name text not null,
    display_name_ko text not null,
    metadata jsonb not null default '{}'::jsonb,
    constraint biome_catalog_code check (code ~ '^[a-z][a-z0-9_]{2,63}$'),
    constraint biome_catalog_metadata check (jsonb_typeof(metadata) = 'object')
);

create table campaign_region_role_catalog (
    code text primary key,
    phase_no smallint not null unique,
    display_name_ko text not null,
    metadata jsonb not null default '{}'::jsonb,
    constraint campaign_region_role_code check (code ~ '^[A-Z][A-Z0-9_]{2,63}$'),
    constraint campaign_region_role_phase check (phase_no between 1 and 6),
    constraint campaign_region_role_metadata check (jsonb_typeof(metadata) = 'object')
);

create table campaign_phase_catalog (
    code text primary key,
    display_order smallint not null unique,
    display_name_ko text not null,
    constraint campaign_phase_code check (code ~ '^[a-z][a-z0-9_]{2,63}$'),
    constraint campaign_phase_order check (display_order between 1 and 6)
);

create table access_fragment_catalog (
    code text primary key,
    admin_level smallint not null unique,
    source_campaign_role text not null references campaign_region_role_catalog(code),
    display_name_ko text not null,
    constraint access_fragment_code check (code ~ '^[A-Z][A-Z0-9_]{2,63}$'),
    constraint access_fragment_level check (admin_level between 1 and 3)
);

create table ending_catalog (
    code text primary key,
    category text not null,
    display_name_ko text not null,
    description_ko text not null,
    constraint ending_catalog_code check (code ~ '^[A-Z][A-Z0-9_]{2,63}$'),
    constraint ending_catalog_category check (category in ('return', 'recovery', 'administrator', 'reset', 'shutdown'))
);

-- Placement slots are immutable world-generation output. Like POIs and runtime
-- entity projections, their coordinates are local to the owning area; map_json
-- and the HTTP API retain global world coordinates.
alter table placement_slots
    add column biome_id text,
    add column campaign_role text,
    add column purpose text not null default 'ambient',
    add column reserved_for text,
    add column is_gated boolean not null default false,
    add column gate_requirements jsonb not null default '{}'::jsonb,
    add constraint placement_slots_biome_fk foreign key (biome_id) references biome_catalog(code),
    add constraint placement_slots_campaign_role_fk foreign key (campaign_role) references campaign_region_role_catalog(code),
    add constraint placement_slots_purpose check (purpose in ('ambient', 'campaign_candidate')),
    add constraint placement_slots_reserved_for check (reserved_for is null or btrim(reserved_for) <> ''),
    add constraint placement_slots_gate_requirements check (jsonb_typeof(gate_requirements) = 'object');

create index placement_slots_owner_role_idx
    on placement_slots (owner_id, world_id, campaign_role, purpose);
create index placement_slots_reserved_for_idx
    on placement_slots (world_id, reserved_for) where reserved_for is not null;

-- Upgrade legacy director phases before replacing the old five-act check.
alter table run_director_states drop constraint run_director_states_act;
alter table run_director_states no force row level security;

update run_director_states
set current_act = case current_act
    when 'introduction' then 'awakening'
    when 'exploration' then 'permission_one'
    when 'pressure' then 'permission_two'
    when 'convergence' then 'truth_index'
    when 'ending' then 'root_resolution'
    else current_act
end
where current_act in ('introduction', 'exploration', 'pressure', 'convergence', 'ending');

alter table run_director_states force row level security;

alter table run_director_states
    add constraint run_director_states_act check (
        current_act in (
            'awakening',
            'permission_one',
            'permission_two',
            'truth_index',
            'legacy_judgment',
            'root_resolution'
        )
    );

create table world_area_descriptors (
    area_id uuid primary key,
    world_id uuid not null,
    owner_id uuid not null,
    area_key text not null,
    biome_id text not null references biome_catalog(code),
    campaign_role text references campaign_region_role_catalog(code),
    descriptor_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    constraint world_area_descriptors_area_fk
        foreign key (area_id, owner_id, world_id)
        references areas(id, owner_id, world_id) on delete cascade,
    constraint world_area_descriptors_key_unique unique (world_id, area_key),
    constraint world_area_descriptors_identity_unique unique (area_id, owner_id, world_id),
    constraint world_area_descriptors_key_not_blank check (btrim(area_key) <> ''),
    constraint world_area_descriptors_json check (jsonb_typeof(descriptor_json) = 'object')
);

create table world_pois (
    id uuid primary key default gen_random_uuid(),
    world_id uuid not null,
    owner_id uuid not null,
    area_id uuid not null,
    poi_key text not null,
    poi_kind text not null,
    display_name text not null,
    x integer not null,
    y integer not null,
    biome_id text not null references biome_catalog(code),
    campaign_role text references campaign_region_role_catalog(code),
    visual_intent text not null,
    is_gated boolean not null default false,
    gate_requirements jsonb not null default '{}'::jsonb,
    tags jsonb not null default '[]'::jsonb,
    created_at timestamptz not null default now(),
    constraint world_pois_area_fk
        foreign key (area_id, owner_id, world_id)
        references areas(id, owner_id, world_id) on delete cascade,
    constraint world_pois_key_unique unique (world_id, poi_key),
    constraint world_pois_identity_unique unique (id, owner_id, world_id),
    constraint world_pois_text check (
        btrim(poi_key) <> '' and btrim(poi_kind) <> ''
        and btrim(display_name) <> '' and btrim(visual_intent) <> ''
    ),
    constraint world_pois_gate_requirements check (jsonb_typeof(gate_requirements) = 'object'),
    constraint world_pois_tags check (jsonb_typeof(tags) = 'array')
);

-- Queryable projection of Codria-specific state. runs.world_state remains the
-- complete resume authority; this row is synchronized in the same transaction.
create table run_codria_states (
    run_id uuid primary key,
    owner_id uuid not null,
    campaign_phase text not null references campaign_phase_catalog(code),
    admin_level smallint not null default 0,
    access_tokens jsonb not null default '[]'::jsonb,
    metrics jsonb not null default '{}'::jsonb,
    root_puzzle jsonb not null default '{}'::jsonb,
    root_resolution jsonb,
    navigation_sequence integer not null default 0,
    safe_travel_count integer not null default 0,
    travel_time_units bigint not null default 0,
    travel_distance bigint not null default 0,
    discovered_area_ids jsonb not null default '[]'::jsonb,
    visited_poi_ids jsonb not null default '[]'::jsonb,
    active_encounter jsonb,
    encounter_history jsonb not null default '[]'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint run_codria_states_run_fk
        foreign key (run_id, owner_id) references runs(id, owner_id) on delete cascade,
    constraint run_codria_states_admin_level check (admin_level between 0 and 3),
    constraint run_codria_states_counters check (
        navigation_sequence >= 0 and safe_travel_count >= 0
        and travel_time_units >= 0 and travel_distance >= 0
        and safe_travel_count <= navigation_sequence
    ),
    constraint run_codria_states_json check (
        jsonb_typeof(access_tokens) = 'array'
        and jsonb_typeof(metrics) = 'object'
        and jsonb_typeof(root_puzzle) = 'object'
        and (root_resolution is null or jsonb_typeof(root_resolution) = 'object')
        and jsonb_typeof(discovered_area_ids) = 'array'
        and jsonb_typeof(visited_poi_ids) = 'array'
        and (active_encounter is null or jsonb_typeof(active_encounter) = 'object')
        and jsonb_typeof(encounter_history) = 'array'
    )
);

-- Safe travel advances the optimistic run version but never the campaign turn.
-- Rows are append-only and retain both the requested destination and the actual
-- committed endpoint (which may be a safe staging tile before an encounter).
create table safe_travels (
    id uuid primary key,
    run_id uuid not null,
    owner_id uuid not null,
    sequence_no integer not null,
    idempotency_key text not null,
    request_fingerprint text not null,
    expected_run_version bigint not null,
    committed_run_version bigint not null,
    from_x integer not null,
    from_y integer not null,
    requested_x integer not null,
    requested_y integer not null,
    to_x integer not null,
    to_y integer not null,
    path_cost integer not null,
    travel_time_units integer not null,
    cumulative_travel_time_units bigint not null,
    entered_area_key text not null,
    entered_biome_id text not null references biome_catalog(code),
    campaign_role text references campaign_region_role_catalog(code),
    traversed_area_ids jsonb not null default '[]'::jsonb,
    reached_poi_ids jsonb not null default '[]'::jsonb,
    path_json jsonb not null,
    encounter_opened boolean not null default false,
    encounter_json jsonb,
    campaign_turn_consumed boolean not null default false,
    campaign_turn_before smallint not null,
    campaign_turn_after smallint not null,
    layout_hash text not null,
    created_at timestamptz not null default now(),
    constraint safe_travels_run_fk
        foreign key (run_id, owner_id) references runs(id, owner_id) on delete cascade,
    constraint safe_travels_run_sequence_unique unique (run_id, sequence_no),
    constraint safe_travels_run_idempotency_unique unique (run_id, idempotency_key),
    constraint safe_travels_identity_unique unique (id, owner_id, run_id),
    constraint safe_travels_text check (
        btrim(idempotency_key) <> '' and btrim(request_fingerprint) <> ''
        and btrim(entered_area_key) <> '' and btrim(layout_hash) <> ''
    ),
    constraint safe_travels_sequence check (sequence_no >= 1),
    constraint safe_travels_versions check (
        expected_run_version >= 1
        and committed_run_version = expected_run_version + 1
    ),
    constraint safe_travels_cost check (
        path_cost >= 0 and travel_time_units >= 0
        and cumulative_travel_time_units >= travel_time_units
    ),
    constraint safe_travels_no_campaign_turn check (campaign_turn_consumed = false and campaign_turn_before = campaign_turn_after),
    constraint safe_travels_layout_hash check (char_length(layout_hash) between 8 and 128),
    constraint safe_travels_json check (
        jsonb_typeof(traversed_area_ids) = 'array'
        and jsonb_typeof(reached_poi_ids) = 'array'
        and jsonb_typeof(path_json) = 'array'
        and (encounter_json is null or jsonb_typeof(encounter_json) = 'object')
        and (encounter_opened = (encounter_json is not null))
    )
);

create index world_area_descriptors_owner_world_idx
    on world_area_descriptors (owner_id, world_id, campaign_role);
create index world_pois_owner_world_area_idx
    on world_pois (owner_id, world_id, area_id, poi_kind);
create index world_pois_gate_idx
    on world_pois (owner_id, world_id, campaign_role) where is_gated;
create index run_codria_states_owner_phase_idx
    on run_codria_states (owner_id, campaign_phase, updated_at desc);
create index run_codria_states_tokens_gin
    on run_codria_states using gin (access_tokens jsonb_path_ops);
create index safe_travels_owner_run_created_idx
    on safe_travels (owner_id, run_id, created_at desc);
create index safe_travels_entered_area_idx
    on safe_travels (owner_id, entered_area_key, created_at desc);

-- API/world_state coordinates are global. Normalized rows owned by an area use
-- area-local coordinates, and the server performs the boundary conversion.
create or replace function keyboard_wanderer.validate_area_connection_endpoints()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
declare
    from_width integer;
    from_height integer;
    to_width integer;
    to_height integer;
begin
    select width, height
      into strict from_width, from_height
    from keyboard_wanderer.areas
    where id = new.from_area_id and owner_id = new.owner_id and world_id = new.world_id;

    select width, height
      into strict to_width, to_height
    from keyboard_wanderer.areas
    where id = new.to_area_id and owner_id = new.owner_id and world_id = new.world_id;

    if new.from_x not between 0 and from_width - 1
       or new.from_y not between 0 and from_height - 1
       or new.to_x not between 0 and to_width - 1
       or new.to_y not between 0 and to_height - 1 then
        raise exception using
            errcode = '23514',
            message = 'local area connection endpoint is outside its declared area';
    end if;
    return new;
end
$$;

create or replace function keyboard_wanderer.validate_entity_position()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
declare
    area_width integer;
    area_height integer;
begin
    select width, height
      into strict area_width, area_height
    from keyboard_wanderer.areas
    where id = new.area_id and owner_id = new.owner_id and world_id = new.world_id;

    if new.x not between 0 and area_width - 1
       or new.y not between 0 and area_height - 1 then
        raise exception using errcode = '23514', message = 'local entity position is outside its declared area';
    end if;
    if tg_op = 'UPDATE' then
        if new.entity_id is distinct from old.entity_id
           or new.owner_id is distinct from old.owner_id
           or new.run_id is distinct from old.run_id
           or new.world_id is distinct from old.world_id then
            raise exception using errcode = '55000', message = 'position ownership and entity identity are immutable';
        end if;
        if new.revision <> old.revision + 1 then
            raise exception using errcode = '23514', message = 'position revision must increment by exactly one';
        end if;
    end if;
    return new;
end
$$;

create or replace function keyboard_wanderer.validate_generated_world_point()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
declare
    area_width integer;
    area_height integer;
begin
    select width, height
      into strict area_width, area_height
    from keyboard_wanderer.areas
    where id = new.area_id and owner_id = new.owner_id and world_id = new.world_id;

    if new.x not between 0 and area_width - 1
       or new.y not between 0 and area_height - 1 then
        raise exception using errcode = '23514', message = 'local generated point is outside its declared area';
    end if;
    return new;
end
$$;

create trigger placement_slots_validate_local_point
before insert or update on placement_slots
for each row execute function keyboard_wanderer.validate_generated_world_point();

create trigger world_pois_validate_local_point
before insert or update on world_pois
for each row execute function keyboard_wanderer.validate_generated_world_point();

-- Seal all world-generation products. Cascades from reviewed parent lifecycle
-- deletion are allowed, while direct UPDATE/DELETE remains forbidden.
create or replace function keyboard_wanderer.reject_generated_world_mutation()
returns trigger
language plpgsql
set search_path = pg_catalog
as $$
begin
    if tg_op = 'DELETE' and pg_trigger_depth() > 1 then
        return old;
    end if;
    raise exception using errcode = '55000', message = 'generated world records are immutable after generation';
end
$$;

create trigger area_connections_immutable
before update or delete on area_connections
for each row execute function keyboard_wanderer.reject_generated_world_mutation();

create trigger world_area_descriptors_immutable
before update or delete on world_area_descriptors
for each row execute function keyboard_wanderer.reject_generated_world_mutation();

create trigger world_pois_immutable
before update or delete on world_pois
for each row execute function keyboard_wanderer.reject_generated_world_mutation();

create or replace function keyboard_wanderer.reject_safe_travel_mutation()
returns trigger
language plpgsql
set search_path = pg_catalog
as $$
begin
    if tg_op = 'DELETE' and pg_trigger_depth() > 1 then
        return old;
    end if;
    raise exception using errcode = '55000', message = 'safe travel records are append-only';
end
$$;

create trigger safe_travels_append_only
before update or delete on safe_travels
for each row execute function keyboard_wanderer.reject_safe_travel_mutation();

-- generation_metadata participates in the sealed layout contract too.
create or replace function keyboard_wanderer.prevent_layout_regeneration()
returns trigger
language plpgsql
set search_path = pg_catalog
as $$
begin
    if tg_table_name = 'worlds' and (
        new.campaign_id is distinct from old.campaign_id
        or new.owner_id is distinct from old.owner_id
        or new.generator_version is distinct from old.generator_version
        or new.layout_hash is distinct from old.layout_hash
        or new.width is distinct from old.width
        or new.height is distinct from old.height
        or new.map_json is distinct from old.map_json
        or new.generation_metadata is distinct from old.generation_metadata
        or new.generated_at is distinct from old.generated_at
    ) then
        raise exception using errcode = '55000', message = 'world layout is immutable after generation';
    elsif tg_table_name = 'regions' and (
        new.world_id is distinct from old.world_id
        or new.owner_id is distinct from old.owner_id
        or new.region_key is distinct from old.region_key
        or new.origin_x is distinct from old.origin_x
        or new.origin_y is distinct from old.origin_y
        or new.width is distinct from old.width
        or new.height is distinct from old.height
        or new.layout_hash is distinct from old.layout_hash
        or new.map_json is distinct from old.map_json
    ) then
        raise exception using errcode = '55000', message = 'region layout is immutable after generation';
    elsif tg_table_name = 'areas' and (
        new.world_id is distinct from old.world_id
        or new.region_id is distinct from old.region_id
        or new.owner_id is distinct from old.owner_id
        or new.area_key is distinct from old.area_key
        or new.origin_x is distinct from old.origin_x
        or new.origin_y is distinct from old.origin_y
        or new.width is distinct from old.width
        or new.height is distinct from old.height
        or new.entry_x is distinct from old.entry_x
        or new.entry_y is distinct from old.entry_y
        or new.exit_x is distinct from old.exit_x
        or new.exit_y is distinct from old.exit_y
        or new.layout_hash is distinct from old.layout_hash
        or new.tile_json is distinct from old.tile_json
    ) then
        raise exception using errcode = '55000', message = 'area layout is immutable after generation';
    end if;
    return new;
end
$$;

create trigger run_codria_states_set_updated_at
before update on run_codria_states
for each row execute function keyboard_wanderer.set_updated_at();

-- Owner-scoped runtime access. The travel ledger intentionally exposes only
-- SELECT and INSERT policies because committed travel rows cannot be rewritten.
do $$
declare
    table_name text;
begin
    foreach table_name in array array['world_area_descriptors', 'world_pois', 'run_codria_states']
    loop
        execute format('alter table keyboard_wanderer.%I enable row level security', table_name);
        execute format('alter table keyboard_wanderer.%I force row level security', table_name);
        execute format(
            'create policy %I on keyboard_wanderer.%I for all using (owner_id = (select keyboard_wanderer.current_app_user_id())) with check (owner_id = (select keyboard_wanderer.current_app_user_id()))',
            table_name || '_owner_all', table_name
        );
    end loop;
end
$$;

alter table safe_travels enable row level security;
alter table safe_travels force row level security;

create policy safe_travels_owner_select on safe_travels
for select using (owner_id = (select keyboard_wanderer.current_app_user_id()));

create policy safe_travels_owner_insert on safe_travels
for insert with check (owner_id = (select keyboard_wanderer.current_app_user_id()));

comment on table world_area_descriptors is
    'Immutable semantic descriptors for generated areas; area geometry remains in areas/map_json.';
comment on table world_pois is
    'Immutable generated POIs in coordinates local to their owning area.';
comment on table run_codria_states is
    'Queryable Codria projection synchronized transactionally with runs.world_state.';
comment on table safe_travels is
    'Append-only global-coordinate travel ledger. Travel advances run version but not campaign turn.';
comment on column placement_slots.x is 'X coordinate local to area_id.';
comment on column placement_slots.y is 'Y coordinate local to area_id.';
comment on column entity_positions.x is 'X coordinate local to area_id.';
comment on column entity_positions.y is 'Y coordinate local to area_id.';
comment on column area_connections.from_x is 'X coordinate local to from_area_id.';
comment on column area_connections.from_y is 'Y coordinate local to from_area_id.';
comment on column area_connections.to_x is 'X coordinate local to to_area_id.';
comment on column area_connections.to_y is 'Y coordinate local to to_area_id.';

revoke all on biome_catalog, campaign_region_role_catalog, campaign_phase_catalog,
    access_fragment_catalog, ending_catalog, world_area_descriptors, world_pois,
    run_codria_states, safe_travels from public;
revoke execute on function keyboard_wanderer.validate_generated_world_point() from public;
revoke execute on function keyboard_wanderer.reject_generated_world_mutation() from public;
revoke execute on function keyboard_wanderer.reject_safe_travel_mutation() from public;

commit;
