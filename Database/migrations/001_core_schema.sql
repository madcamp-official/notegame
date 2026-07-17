begin;

create extension if not exists pgcrypto;

create schema if not exists keyboard_wanderer;
set local search_path = keyboard_wanderer, public;

create or replace function keyboard_wanderer.current_app_user_id()
returns uuid
language sql
stable
set search_path = ''
as $$
    select nullif(pg_catalog.current_setting('app.user_id', true), '')::uuid
$$;

comment on function keyboard_wanderer.current_app_user_id() is
    'Returns the request-scoped profile UUID from app.user_id. NULL when the setting is absent; malformed UUIDs fail closed.';

create or replace function keyboard_wanderer.set_updated_at()
returns trigger
language plpgsql
set search_path = pg_catalog
as $$
begin
    new.updated_at := clock_timestamp();
    return new;
end
$$;

create table ability_catalog (
    code text primary key,
    display_name text not null,
    description text not null,
    target_mode text not null,
    focus_cost smallint not null default 0,
    min_range smallint not null default 0,
    max_range smallint,
    is_enabled boolean not null default true,
    display_order smallint not null,
    metadata jsonb not null default '{}'::jsonb,
    constraint ability_catalog_code_format check (code ~ '^[A-Z][A-Z0-9_]{1,31}$'),
    constraint ability_catalog_target_mode check (target_mode in ('none', 'entity', 'tile', 'entity_and_tile')),
    constraint ability_catalog_focus_cost check (focus_cost >= 0),
    constraint ability_catalog_range check (min_range >= 0 and (max_range is null or max_range >= min_range)),
    constraint ability_catalog_metadata_object check (jsonb_typeof(metadata) = 'object'),
    constraint ability_catalog_display_order_unique unique (display_order)
);

create table entity_kind_catalog (
    code text primary key,
    display_name text not null,
    is_actor boolean not null default false,
    can_occupy_tile boolean not null default true,
    metadata jsonb not null default '{}'::jsonb,
    constraint entity_kind_catalog_code_format check (code ~ '^[A-Z][A-Z0-9_]{1,31}$'),
    constraint entity_kind_catalog_metadata_object check (jsonb_typeof(metadata) = 'object')
);

create table item_catalog (
    code text primary key,
    display_name text not null,
    description text not null default '',
    asset_id text not null,
    is_stackable boolean not null default false,
    max_stack integer not null default 1,
    base_properties jsonb not null default '{}'::jsonb,
    constraint item_catalog_code_format check (code ~ '^[A-Z][A-Z0-9_]{1,63}$'),
    constraint item_catalog_asset_id_not_blank check (btrim(asset_id) <> ''),
    constraint item_catalog_stack check (
        max_stack >= 1
        and ((is_stackable and max_stack > 1) or (not is_stackable and max_stack = 1))
    ),
    constraint item_catalog_properties_object check (jsonb_typeof(base_properties) = 'object')
);

create table event_type_catalog (
    code text primary key,
    display_name text not null,
    description text not null default '',
    is_system boolean not null default false,
    constraint event_type_catalog_code_format check (code ~ '^[A-Z][A-Z0-9_]{1,63}$')
);

create table profiles (
    id uuid primary key default gen_random_uuid(),
    identity_provider text,
    external_subject text,
    display_name text not null,
    locale text not null default 'ko-KR',
    preferences jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    deleted_at timestamptz,
    constraint profiles_display_name_not_blank check (btrim(display_name) <> ''),
    constraint profiles_identity_pair check (
        (identity_provider is null and external_subject is null)
        or (
            identity_provider is not null
            and external_subject is not null
            and btrim(identity_provider) <> ''
            and btrim(external_subject) <> ''
        )
    ),
    constraint profiles_preferences_object check (jsonb_typeof(preferences) = 'object'),
    constraint profiles_external_identity_unique unique (identity_provider, external_subject)
);

create table campaigns (
    id uuid primary key default gen_random_uuid(),
    owner_id uuid not null references profiles(id) on delete cascade,
    title text not null,
    world_seed bigint not null,
    turn_limit smallint not null default 40,
    status text not null default 'draft',
    ruleset_version text not null default '1.0',
    premise text not null default '',
    settings jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    archived_at timestamptz,
    constraint campaigns_title_not_blank check (btrim(title) <> ''),
    constraint campaigns_turn_limit check (turn_limit between 30 and 50),
    constraint campaigns_status check (status in ('draft', 'active', 'completed', 'archived')),
    constraint campaigns_archive_state check ((status = 'archived') = (archived_at is not null)),
    constraint campaigns_settings_object check (jsonb_typeof(settings) = 'object'),
    constraint campaigns_id_owner_unique unique (id, owner_id)
);

create index campaigns_owner_status_updated_idx
    on campaigns (owner_id, status, updated_at desc);

create table worlds (
    id uuid primary key default gen_random_uuid(),
    campaign_id uuid not null,
    owner_id uuid not null,
    generator_version text not null,
    layout_hash text not null,
    width integer not null,
    height integer not null,
    map_json jsonb not null,
    generation_metadata jsonb not null default '{}'::jsonb,
    generated_at timestamptz not null default now(),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint worlds_campaign_owner_fk
        foreign key (campaign_id, owner_id) references campaigns(id, owner_id) on delete cascade,
    constraint worlds_one_per_campaign unique (campaign_id),
    constraint worlds_id_owner_unique unique (id, owner_id),
    constraint worlds_id_owner_campaign_unique unique (id, owner_id, campaign_id),
    constraint worlds_generator_version_not_blank check (btrim(generator_version) <> ''),
    constraint worlds_layout_hash check (char_length(layout_hash) between 8 and 128),
    constraint worlds_dimensions check (width between 1 and 32767 and height between 1 and 32767),
    constraint worlds_map_object check (jsonb_typeof(map_json) = 'object'),
    constraint worlds_generation_metadata_object check (jsonb_typeof(generation_metadata) = 'object')
);

create index worlds_owner_campaign_idx on worlds (owner_id, campaign_id);

create table regions (
    id uuid primary key default gen_random_uuid(),
    world_id uuid not null,
    owner_id uuid not null,
    region_key text not null,
    display_name text not null,
    region_kind text not null default 'overworld',
    origin_x integer not null,
    origin_y integer not null,
    width integer not null,
    height integer not null,
    layout_hash text not null,
    map_json jsonb not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint regions_world_owner_fk
        foreign key (world_id, owner_id) references worlds(id, owner_id) on delete cascade,
    constraint regions_id_owner_world_unique unique (id, owner_id, world_id),
    constraint regions_world_key_unique unique (world_id, region_key),
    constraint regions_key_format check (region_key ~ '^[a-z0-9][a-z0-9._-]{1,63}$'),
    constraint regions_name_not_blank check (btrim(display_name) <> ''),
    constraint regions_kind_not_blank check (btrim(region_kind) <> ''),
    constraint regions_origin check (origin_x >= 0 and origin_y >= 0),
    constraint regions_dimensions check (width between 1 and 32767 and height between 1 and 32767),
    constraint regions_layout_hash check (char_length(layout_hash) between 8 and 128),
    constraint regions_map_object check (jsonb_typeof(map_json) = 'object')
);

create index regions_owner_world_idx on regions (owner_id, world_id);

create table areas (
    id uuid primary key default gen_random_uuid(),
    world_id uuid not null,
    region_id uuid not null,
    owner_id uuid not null,
    area_key text not null,
    display_name text not null,
    area_kind text not null default 'field',
    origin_x integer not null,
    origin_y integer not null,
    width smallint not null,
    height smallint not null,
    entry_x smallint not null,
    entry_y smallint not null,
    exit_x smallint not null,
    exit_y smallint not null,
    layout_hash text not null,
    tile_json jsonb not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint areas_region_owner_world_fk
        foreign key (region_id, owner_id, world_id)
        references regions(id, owner_id, world_id) on delete cascade,
    constraint areas_id_owner_world_unique unique (id, owner_id, world_id),
    constraint areas_world_key_unique unique (world_id, area_key),
    constraint areas_key_format check (area_key ~ '^[a-z0-9][a-z0-9._-]{1,63}$'),
    constraint areas_name_not_blank check (btrim(display_name) <> ''),
    constraint areas_kind_not_blank check (btrim(area_kind) <> ''),
    constraint areas_origin check (origin_x >= 0 and origin_y >= 0),
    constraint areas_dimensions check (width between 1 and 1024 and height between 1 and 1024),
    constraint areas_entry_inside check (entry_x between 0 and width - 1 and entry_y between 0 and height - 1),
    constraint areas_exit_inside check (exit_x between 0 and width - 1 and exit_y between 0 and height - 1),
    constraint areas_layout_hash check (char_length(layout_hash) between 8 and 128),
    constraint areas_tile_object check (jsonb_typeof(tile_json) = 'object')
);

create index areas_owner_region_idx on areas (owner_id, region_id);
create index areas_owner_world_idx on areas (owner_id, world_id);

create table area_connections (
    id uuid primary key default gen_random_uuid(),
    owner_id uuid not null,
    world_id uuid not null,
    from_area_id uuid not null,
    to_area_id uuid not null,
    from_x smallint not null,
    from_y smallint not null,
    to_x smallint not null,
    to_y smallint not null,
    direction text not null default 'bidirectional',
    traversal_kind text not null default 'walk',
    requirement_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    constraint area_connections_from_fk
        foreign key (from_area_id, owner_id, world_id)
        references areas(id, owner_id, world_id) on delete cascade,
    constraint area_connections_to_fk
        foreign key (to_area_id, owner_id, world_id)
        references areas(id, owner_id, world_id) on delete cascade,
    constraint area_connections_distinct check (from_area_id <> to_area_id),
    constraint area_connections_direction check (direction in ('one_way', 'bidirectional')),
    constraint area_connections_traversal_not_blank check (btrim(traversal_kind) <> ''),
    constraint area_connections_requirement_object check (jsonb_typeof(requirement_json) = 'object'),
    constraint area_connections_edge_unique unique (from_area_id, to_area_id, traversal_kind)
);

create index area_connections_owner_world_idx on area_connections (owner_id, world_id);
create index area_connections_to_area_idx on area_connections (to_area_id);

create table runs (
    id uuid primary key default gen_random_uuid(),
    campaign_id uuid not null,
    world_id uuid not null,
    owner_id uuid not null,
    status text not null default 'playing',
    version bigint not null default 1,
    current_turn smallint not null default 0,
    turn_limit smallint not null,
    focus integer not null default 8,
    pressure integer not null default 0,
    active_area_id uuid,
    player_entity_id uuid,
    world_state jsonb not null default '{}'::jsonb,
    resolution_seed text not null,
    ending_code text,
    started_at timestamptz not null default now(),
    completed_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint runs_campaign_owner_fk
        foreign key (campaign_id, owner_id) references campaigns(id, owner_id) on delete cascade,
    constraint runs_world_owner_campaign_fk
        foreign key (world_id, owner_id, campaign_id)
        references worlds(id, owner_id, campaign_id) on delete restrict,
    constraint runs_active_area_fk
        foreign key (active_area_id, owner_id, world_id)
        references areas(id, owner_id, world_id) on delete restrict,
    constraint runs_id_owner_unique unique (id, owner_id),
    constraint runs_id_owner_world_unique unique (id, owner_id, world_id),
    constraint runs_id_owner_campaign_unique unique (id, owner_id, campaign_id),
    constraint runs_status check (status in ('playing', 'completed', 'abandoned', 'recovery_required')),
    constraint runs_version check (version >= 1 and version >= current_turn::bigint + 1),
    constraint runs_turns check (turn_limit between 30 and 50 and current_turn between 0 and turn_limit),
    constraint runs_resources check (focus >= 0 and pressure >= 0),
    constraint runs_world_state_object check (jsonb_typeof(world_state) = 'object'),
    constraint runs_resolution_seed_not_blank check (btrim(resolution_seed) <> ''),
    constraint runs_completion_state check (
        (status in ('playing', 'recovery_required') and completed_at is null and ending_code is null)
        or (
            status = 'completed'
            and completed_at is not null
            and ending_code is not null
            and btrim(ending_code) <> ''
        )
        or (status = 'abandoned' and completed_at is not null and ending_code is null)
    )
);

comment on column runs.resolution_seed is
    'Server-only deterministic resolution secret. Never expose in client DTOs, logs, snapshots, or LLM prompts.';

create index runs_owner_status_updated_idx on runs (owner_id, status, updated_at desc);
create index runs_campaign_created_idx on runs (campaign_id, created_at desc);
create index runs_world_idx on runs (world_id);
create index runs_active_area_idx on runs (active_area_id) where active_area_id is not null;

create table entities (
    id uuid primary key default gen_random_uuid(),
    owner_id uuid not null,
    run_id uuid not null,
    world_id uuid not null,
    entity_kind text not null references entity_kind_catalog(code) on update cascade,
    asset_id text not null,
    display_name text not null,
    source_entity_id uuid,
    is_protected boolean not null default false,
    is_cloneable boolean not null default false,
    is_active boolean not null default true,
    state_json jsonb not null default '{}'::jsonb,
    spawned_turn smallint not null default 0,
    despawned_turn smallint,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint entities_run_owner_world_fk
        foreign key (run_id, owner_id, world_id)
        references runs(id, owner_id, world_id) on delete cascade,
    constraint entities_source_fk
        foreign key (source_entity_id, owner_id, run_id)
        references entities(id, owner_id, run_id) on delete set null (source_entity_id),
    constraint entities_id_owner_run_unique unique (id, owner_id, run_id),
    constraint entities_asset_id_not_blank check (btrim(asset_id) <> ''),
    constraint entities_display_name_not_blank check (btrim(display_name) <> ''),
    constraint entities_state_object check (jsonb_typeof(state_json) = 'object'),
    constraint entities_turn_lifecycle check (
        spawned_turn >= 0 and (despawned_turn is null or despawned_turn >= spawned_turn)
    )
);

create index entities_owner_run_active_idx on entities (owner_id, run_id, entity_kind) where is_active;
create index entities_run_owner_world_idx on entities (run_id, owner_id, world_id);
create index entities_run_source_idx on entities (run_id, source_entity_id) where source_entity_id is not null;
create index entities_world_idx on entities (world_id);

alter table runs
    add constraint runs_player_entity_fk
    foreign key (player_entity_id, owner_id, id)
    references entities(id, owner_id, run_id)
    deferrable initially deferred;

create index runs_player_entity_idx on runs (player_entity_id) where player_entity_id is not null;

create table actors (
    entity_id uuid primary key,
    owner_id uuid not null,
    run_id uuid not null,
    actor_role text not null,
    faction_code text,
    level integer not null default 1,
    hp integer not null default 1,
    max_hp integer not null default 1,
    energy integer not null default 0,
    max_energy integer not null default 0,
    stats_json jsonb not null default '{}'::jsonb,
    ai_state_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint actors_entity_owner_run_fk
        foreign key (entity_id, owner_id, run_id)
        references entities(id, owner_id, run_id) on delete cascade,
    constraint actors_entity_owner_run_unique unique (entity_id, owner_id, run_id),
    constraint actors_role check (actor_role in ('player', 'companion', 'npc', 'enemy')),
    constraint actors_level check (level >= 1),
    constraint actors_health check (max_hp >= 1 and hp between 0 and max_hp),
    constraint actors_energy check (max_energy >= 0 and energy between 0 and max_energy),
    constraint actors_stats_object check (jsonb_typeof(stats_json) = 'object'),
    constraint actors_ai_state_object check (jsonb_typeof(ai_state_json) = 'object')
);

create index actors_owner_run_role_idx on actors (owner_id, run_id, actor_role);

create table entity_positions (
    entity_id uuid primary key,
    owner_id uuid not null,
    run_id uuid not null,
    world_id uuid not null,
    area_id uuid not null,
    layer smallint not null default 0,
    x integer not null,
    y integer not null,
    facing text not null default 'south',
    blocks_movement boolean not null default true,
    revision bigint not null default 1,
    removed_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint entity_positions_entity_fk
        foreign key (entity_id, owner_id, run_id)
        references entities(id, owner_id, run_id) on delete cascade,
    constraint entity_positions_run_world_fk
        foreign key (run_id, owner_id, world_id)
        references runs(id, owner_id, world_id) on delete cascade,
    constraint entity_positions_area_world_fk
        foreign key (area_id, owner_id, world_id)
        references areas(id, owner_id, world_id) on delete restrict,
    constraint entity_positions_layer check (layer between 0 and 31),
    constraint entity_positions_facing check (facing in ('north', 'south', 'east', 'west', 'none')),
    constraint entity_positions_revision check (revision >= 1)
);

create unique index entity_positions_blocking_tile_unique
    on entity_positions (run_id, area_id, layer, x, y)
    where blocks_movement and removed_at is null;
create index entity_positions_owner_run_area_idx
    on entity_positions (owner_id, run_id, area_id)
    where removed_at is null;
create index entity_positions_run_owner_world_idx
    on entity_positions (run_id, owner_id, world_id);
create index entity_positions_area_idx on entity_positions (area_id);

create table inventories (
    id uuid primary key default gen_random_uuid(),
    owner_id uuid not null,
    run_id uuid not null,
    actor_id uuid not null,
    inventory_kind text not null default 'backpack',
    capacity smallint not null default 20,
    metadata jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint inventories_run_owner_fk
        foreign key (run_id, owner_id) references runs(id, owner_id) on delete cascade,
    constraint inventories_actor_fk
        foreign key (actor_id, owner_id, run_id)
        references actors(entity_id, owner_id, run_id) on delete cascade,
    constraint inventories_id_owner_run_unique unique (id, owner_id, run_id),
    constraint inventories_actor_kind_unique unique (actor_id, inventory_kind),
    constraint inventories_kind_not_blank check (btrim(inventory_kind) <> ''),
    constraint inventories_capacity check (capacity between 1 and 500),
    constraint inventories_metadata_object check (jsonb_typeof(metadata) = 'object')
);

create index inventories_owner_run_idx on inventories (owner_id, run_id);

create table items (
    id uuid primary key default gen_random_uuid(),
    owner_id uuid not null,
    run_id uuid not null,
    item_code text not null references item_catalog(code) on update cascade,
    inventory_id uuid,
    slot_index smallint,
    world_entity_id uuid,
    quantity integer not null default 1,
    durability integer,
    state_json jsonb not null default '{}'::jsonb,
    acquired_turn smallint not null default 0,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint items_run_owner_fk
        foreign key (run_id, owner_id) references runs(id, owner_id) on delete cascade,
    constraint items_inventory_fk
        foreign key (inventory_id, owner_id, run_id)
        references inventories(id, owner_id, run_id) on delete cascade,
    constraint items_world_entity_fk
        foreign key (world_entity_id, owner_id, run_id)
        references entities(id, owner_id, run_id) on delete cascade,
    constraint items_location check (
        (inventory_id is not null and slot_index is not null and world_entity_id is null)
        or (inventory_id is null and slot_index is null and world_entity_id is not null)
    ),
    constraint items_slot check (slot_index is null or slot_index >= 0),
    constraint items_quantity check (quantity >= 1),
    constraint items_durability check (durability is null or durability >= 0),
    constraint items_state_object check (jsonb_typeof(state_json) = 'object'),
    constraint items_acquired_turn check (acquired_turn >= 0)
);

create unique index items_inventory_slot_unique
    on items (inventory_id, slot_index) where inventory_id is not null;
create unique index items_world_entity_unique
    on items (world_entity_id) where world_entity_id is not null;
create index items_owner_run_code_idx on items (owner_id, run_id, item_code);

create table turn_records (
    id uuid primary key default gen_random_uuid(),
    run_id uuid not null,
    owner_id uuid not null,
    turn_no smallint,
    idempotency_key text not null,
    request_fingerprint text not null,
    expected_run_version bigint not null,
    committed_run_version bigint,
    status text not null default 'pending',
    request_json jsonb not null,
    result_json jsonb,
    narrative_json jsonb,
    fallback_used boolean,
    model text,
    error_code text,
    received_at timestamptz not null default now(),
    completed_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint turn_records_run_owner_fk
        foreign key (run_id, owner_id) references runs(id, owner_id) on delete cascade,
    constraint turn_records_id_owner_run_unique unique (id, owner_id, run_id),
    constraint turn_records_run_idempotency_unique unique (run_id, idempotency_key),
    constraint turn_records_run_turn_unique unique (run_id, turn_no),
    constraint turn_records_idempotency_key check (
        char_length(btrim(idempotency_key)) between 8 and 128
        and idempotency_key !~ '[[:space:]]'
    ),
    constraint turn_records_fingerprint check (char_length(btrim(request_fingerprint)) between 8 and 512),
    constraint turn_records_expected_version check (expected_run_version >= 1),
    constraint turn_records_status check (status in ('pending', 'committed', 'rejected')),
    constraint turn_records_request_object check (jsonb_typeof(request_json) = 'object'),
    constraint turn_records_result_object check (result_json is null or jsonb_typeof(result_json) = 'object'),
    constraint turn_records_narrative_object check (narrative_json is null or jsonb_typeof(narrative_json) = 'object'),
    constraint turn_records_terminal_shape check (
        (
            status = 'pending'
            and turn_no is null
            and committed_run_version is null
            and result_json is null
            and narrative_json is null
            and fallback_used is null
            and model is null
            and error_code is null
            and completed_at is null
        )
        or (
            status = 'committed'
            and turn_no >= 1
            and committed_run_version = expected_run_version + 1
            and result_json is not null
            and narrative_json is not null
            and fallback_used is not null
            and error_code is null
            and completed_at is not null
        )
        or (
            status = 'rejected'
            and turn_no is null
            and committed_run_version is null
            and result_json is not null
            and narrative_json is null
            and fallback_used is null
            and model is null
            and error_code is not null
            and btrim(error_code) <> ''
            and completed_at is not null
        )
    )
);

comment on table turn_records is
    'Authoritative idempotency ledger. One row starts pending and may transition exactly once to committed or rejected.';

create index turn_records_owner_run_created_idx on turn_records (owner_id, run_id, created_at desc);
create index turn_records_pending_idx on turn_records (received_at) where status = 'pending';
create unique index turn_records_run_committed_version_unique
    on turn_records (run_id, committed_run_version) where status = 'committed';

create table turn_events (
    id bigint generated always as identity primary key,
    turn_record_id uuid not null,
    run_id uuid not null,
    owner_id uuid not null,
    event_index smallint not null,
    event_type text not null references event_type_catalog(code) on update cascade,
    payload jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    constraint turn_events_record_fk
        foreign key (turn_record_id, owner_id, run_id)
        references turn_records(id, owner_id, run_id) on delete cascade,
    constraint turn_events_index check (event_index >= 0),
    constraint turn_events_payload_object check (jsonb_typeof(payload) = 'object'),
    constraint turn_events_record_index_unique unique (turn_record_id, event_index)
);

create index turn_events_owner_run_idx on turn_events (owner_id, run_id, id);
create index turn_events_record_idx on turn_events (turn_record_id);
create index turn_events_event_type_idx on turn_events (event_type);

create table turn_logs (
    id bigint generated always as identity primary key,
    turn_record_id uuid not null,
    run_id uuid not null,
    owner_id uuid not null,
    stage text not null,
    level text not null default 'info',
    message text not null,
    details jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    constraint turn_logs_record_fk
        foreign key (turn_record_id, owner_id, run_id)
        references turn_records(id, owner_id, run_id) on delete cascade,
    constraint turn_logs_stage_not_blank check (btrim(stage) <> ''),
    constraint turn_logs_level check (level in ('debug', 'info', 'warning', 'error')),
    constraint turn_logs_message_not_blank check (btrim(message) <> ''),
    constraint turn_logs_details_object check (jsonb_typeof(details) = 'object')
);

create index turn_logs_owner_run_created_idx on turn_logs (owner_id, run_id, created_at);
create index turn_logs_record_idx on turn_logs (turn_record_id);

create table world_events (
    id uuid primary key default gen_random_uuid(),
    owner_id uuid not null,
    run_id uuid not null,
    world_id uuid not null,
    turn_record_id uuid,
    turn_no smallint not null,
    event_key text not null,
    event_type text not null references event_type_catalog(code) on update cascade,
    actor_entity_id uuid,
    area_id uuid,
    summary text not null,
    payload jsonb not null default '{}'::jsonb,
    occurred_at timestamptz not null default now(),
    created_at timestamptz not null default now(),
    constraint world_events_run_world_fk
        foreign key (run_id, owner_id, world_id)
        references runs(id, owner_id, world_id) on delete cascade,
    constraint world_events_turn_record_fk
        foreign key (turn_record_id, owner_id, run_id)
        references turn_records(id, owner_id, run_id) on delete cascade,
    constraint world_events_actor_fk
        foreign key (actor_entity_id, owner_id, run_id)
        references entities(id, owner_id, run_id) on delete set null (actor_entity_id),
    constraint world_events_area_fk
        foreign key (area_id, owner_id, world_id)
        references areas(id, owner_id, world_id) on delete restrict,
    constraint world_events_id_owner_run_unique unique (id, owner_id, run_id),
    constraint world_events_run_key_unique unique (run_id, event_key),
    constraint world_events_turn check (turn_no >= 0),
    constraint world_events_key_not_blank check (btrim(event_key) <> ''),
    constraint world_events_summary_not_blank check (btrim(summary) <> ''),
    constraint world_events_payload_object check (jsonb_typeof(payload) = 'object')
);

create index world_events_owner_run_turn_idx on world_events (owner_id, run_id, turn_no, occurred_at);
create index world_events_world_idx on world_events (world_id);
create index world_events_event_type_idx on world_events (event_type);
create index world_events_turn_record_idx on world_events (turn_record_id) where turn_record_id is not null;
create index world_events_actor_idx on world_events (actor_entity_id) where actor_entity_id is not null;
create index world_events_area_idx on world_events (area_id) where area_id is not null;
create index world_events_payload_gin on world_events using gin (payload jsonb_path_ops);

create table world_facts (
    id uuid primary key default gen_random_uuid(),
    owner_id uuid not null,
    run_id uuid not null,
    subject_key text not null,
    predicate text not null,
    object_json jsonb not null,
    confidence numeric(4,3) not null default 1.000,
    source_event_id uuid,
    valid_from_turn smallint not null default 0,
    valid_until_turn smallint,
    superseded_by_fact_id uuid,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint world_facts_run_owner_fk
        foreign key (run_id, owner_id) references runs(id, owner_id) on delete cascade,
    constraint world_facts_source_event_fk
        foreign key (source_event_id, owner_id, run_id)
        references world_events(id, owner_id, run_id) on delete set null (source_event_id),
    constraint world_facts_id_owner_run_unique unique (id, owner_id, run_id),
    constraint world_facts_superseded_fk
        foreign key (superseded_by_fact_id, owner_id, run_id)
        references world_facts(id, owner_id, run_id)
        deferrable initially deferred,
    constraint world_facts_subject_not_blank check (btrim(subject_key) <> ''),
    constraint world_facts_predicate_not_blank check (btrim(predicate) <> ''),
    constraint world_facts_confidence check (confidence between 0 and 1),
    constraint world_facts_validity check (
        valid_from_turn >= 0 and (valid_until_turn is null or valid_until_turn >= valid_from_turn)
    ),
    constraint world_facts_not_self_superseded check (superseded_by_fact_id is null or superseded_by_fact_id <> id)
);

create unique index world_facts_current_unique
    on world_facts (run_id, subject_key, predicate)
    where superseded_by_fact_id is null;
create index world_facts_owner_run_idx on world_facts (owner_id, run_id, subject_key);
create index world_facts_source_event_idx on world_facts (source_event_id) where source_event_id is not null;
create index world_facts_superseded_idx on world_facts (superseded_by_fact_id) where superseded_by_fact_id is not null;
create index world_facts_object_gin on world_facts using gin (object_json jsonb_path_ops);

create table rumors (
    id uuid primary key default gen_random_uuid(),
    owner_id uuid not null,
    run_id uuid not null,
    origin_fact_id uuid,
    origin_event_id uuid,
    content text not null,
    reliability numeric(4,3) not null default 0.500,
    spread_count integer not null default 0,
    status text not null default 'active',
    first_heard_turn smallint not null,
    expires_turn smallint,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint rumors_run_owner_fk
        foreign key (run_id, owner_id) references runs(id, owner_id) on delete cascade,
    constraint rumors_origin_fact_fk
        foreign key (origin_fact_id, owner_id, run_id)
        references world_facts(id, owner_id, run_id) on delete set null (origin_fact_id),
    constraint rumors_origin_event_fk
        foreign key (origin_event_id, owner_id, run_id)
        references world_events(id, owner_id, run_id) on delete set null (origin_event_id),
    constraint rumors_id_owner_run_unique unique (id, owner_id, run_id),
    constraint rumors_origin check (origin_fact_id is not null or origin_event_id is not null),
    constraint rumors_content_not_blank check (btrim(content) <> ''),
    constraint rumors_reliability check (reliability between 0 and 1),
    constraint rumors_spread_count check (spread_count >= 0),
    constraint rumors_status check (status in ('active', 'confirmed', 'disproved', 'expired')),
    constraint rumors_turns check (first_heard_turn >= 0 and (expires_turn is null or expires_turn >= first_heard_turn))
);

create index rumors_owner_run_status_idx on rumors (owner_id, run_id, status, first_heard_turn);
create index rumors_origin_fact_idx on rumors (origin_fact_id) where origin_fact_id is not null;
create index rumors_origin_event_idx on rumors (origin_event_id) where origin_event_id is not null;

create table rumor_knowledge (
    rumor_id uuid not null,
    actor_id uuid not null,
    owner_id uuid not null,
    run_id uuid not null,
    belief numeric(4,3) not null default 0.500,
    heard_turn smallint not null,
    source_actor_id uuid,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    primary key (rumor_id, actor_id),
    constraint rumor_knowledge_rumor_fk
        foreign key (rumor_id, owner_id, run_id)
        references rumors(id, owner_id, run_id) on delete cascade,
    constraint rumor_knowledge_actor_fk
        foreign key (actor_id, owner_id, run_id)
        references actors(entity_id, owner_id, run_id) on delete cascade,
    constraint rumor_knowledge_source_actor_fk
        foreign key (source_actor_id, owner_id, run_id)
        references actors(entity_id, owner_id, run_id) on delete set null (source_actor_id),
    constraint rumor_knowledge_belief check (belief between 0 and 1),
    constraint rumor_knowledge_heard_turn check (heard_turn >= 0),
    constraint rumor_knowledge_source_not_self check (source_actor_id is null or source_actor_id <> actor_id)
);

create index rumor_knowledge_owner_actor_idx on rumor_knowledge (owner_id, run_id, actor_id);
create index rumor_knowledge_source_actor_idx on rumor_knowledge (source_actor_id) where source_actor_id is not null;

create table npc_memories (
    id uuid primary key default gen_random_uuid(),
    owner_id uuid not null,
    run_id uuid not null,
    npc_actor_id uuid not null,
    source_event_id uuid,
    dedupe_key text not null,
    memory_kind text not null,
    summary text not null,
    details jsonb not null default '{}'::jsonb,
    salience smallint not null default 50,
    observed_turn smallint not null,
    expires_turn smallint,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint npc_memories_run_owner_fk
        foreign key (run_id, owner_id) references runs(id, owner_id) on delete cascade,
    constraint npc_memories_actor_fk
        foreign key (npc_actor_id, owner_id, run_id)
        references actors(entity_id, owner_id, run_id) on delete cascade,
    constraint npc_memories_source_event_fk
        foreign key (source_event_id, owner_id, run_id)
        references world_events(id, owner_id, run_id) on delete set null (source_event_id),
    constraint npc_memories_actor_dedupe_unique unique (npc_actor_id, dedupe_key),
    constraint npc_memories_dedupe_not_blank check (btrim(dedupe_key) <> ''),
    constraint npc_memories_kind_not_blank check (btrim(memory_kind) <> ''),
    constraint npc_memories_summary_not_blank check (btrim(summary) <> ''),
    constraint npc_memories_details_object check (jsonb_typeof(details) = 'object'),
    constraint npc_memories_salience check (salience between 0 and 100),
    constraint npc_memories_turns check (observed_turn >= 0 and (expires_turn is null or expires_turn >= observed_turn))
);

create index npc_memories_owner_actor_salience_idx
    on npc_memories (owner_id, npc_actor_id, salience desc, observed_turn desc);
create index npc_memories_run_owner_idx on npc_memories (run_id, owner_id);
create index npc_memories_source_event_idx on npc_memories (source_event_id) where source_event_id is not null;

create table npc_relationships (
    id uuid primary key default gen_random_uuid(),
    owner_id uuid not null,
    run_id uuid not null,
    subject_actor_id uuid not null,
    object_actor_id uuid not null,
    affinity smallint not null default 0,
    trust smallint not null default 0,
    fear smallint not null default 0,
    relationship_state text not null default 'neutral',
    notes jsonb not null default '{}'::jsonb,
    last_changed_turn smallint not null default 0,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint npc_relationships_run_owner_fk
        foreign key (run_id, owner_id) references runs(id, owner_id) on delete cascade,
    constraint npc_relationships_subject_fk
        foreign key (subject_actor_id, owner_id, run_id)
        references actors(entity_id, owner_id, run_id) on delete cascade,
    constraint npc_relationships_object_fk
        foreign key (object_actor_id, owner_id, run_id)
        references actors(entity_id, owner_id, run_id) on delete cascade,
    constraint npc_relationships_pair_unique unique (run_id, subject_actor_id, object_actor_id),
    constraint npc_relationships_distinct check (subject_actor_id <> object_actor_id),
    constraint npc_relationships_scores check (
        affinity between -100 and 100 and trust between -100 and 100 and fear between 0 and 100
    ),
    constraint npc_relationships_state_not_blank check (btrim(relationship_state) <> ''),
    constraint npc_relationships_notes_object check (jsonb_typeof(notes) = 'object'),
    constraint npc_relationships_turn check (last_changed_turn >= 0)
);

create index npc_relationships_owner_subject_idx on npc_relationships (owner_id, run_id, subject_actor_id);
create index npc_relationships_object_idx on npc_relationships (object_actor_id);

create table quests (
    id uuid primary key default gen_random_uuid(),
    owner_id uuid not null,
    run_id uuid not null,
    quest_key text not null,
    title text not null,
    description text not null default '',
    status text not null default 'available',
    quest_kind text not null default 'side',
    giver_actor_id uuid,
    parent_quest_id uuid,
    start_turn smallint,
    deadline_turn smallint,
    completed_turn smallint,
    reward_json jsonb not null default '{}'::jsonb,
    state_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint quests_run_owner_fk
        foreign key (run_id, owner_id) references runs(id, owner_id) on delete cascade,
    constraint quests_giver_fk
        foreign key (giver_actor_id, owner_id, run_id)
        references actors(entity_id, owner_id, run_id) on delete set null (giver_actor_id),
    constraint quests_id_owner_run_unique unique (id, owner_id, run_id),
    constraint quests_parent_fk
        foreign key (parent_quest_id, owner_id, run_id)
        references quests(id, owner_id, run_id) deferrable initially deferred,
    constraint quests_run_key_unique unique (run_id, quest_key),
    constraint quests_key_format check (quest_key ~ '^[A-Z][A-Z0-9_.-]{1,95}$'),
    constraint quests_title_not_blank check (btrim(title) <> ''),
    constraint quests_status check (status in ('available', 'active', 'completed', 'failed', 'abandoned', 'locked')),
    constraint quests_kind check (quest_kind in ('main', 'side', 'emergent')),
    constraint quests_not_own_parent check (parent_quest_id is null or parent_quest_id <> id),
    constraint quests_turns check (
        (start_turn is null or start_turn >= 0)
        and (deadline_turn is null or (start_turn is not null and deadline_turn >= start_turn))
        and (completed_turn is null or (start_turn is not null and completed_turn >= start_turn))
    ),
    constraint quests_completion_shape check ((status = 'completed') = (completed_turn is not null)),
    constraint quests_reward_object check (jsonb_typeof(reward_json) = 'object'),
    constraint quests_state_object check (jsonb_typeof(state_json) = 'object')
);

create index quests_owner_run_status_idx on quests (owner_id, run_id, status, quest_kind);
create index quests_giver_idx on quests (giver_actor_id) where giver_actor_id is not null;
create index quests_parent_idx on quests (parent_quest_id) where parent_quest_id is not null;

create table quest_objectives (
    id uuid primary key default gen_random_uuid(),
    quest_id uuid not null,
    owner_id uuid not null,
    run_id uuid not null,
    objective_index smallint not null,
    description text not null,
    status text not null default 'pending',
    progress_current integer not null default 0,
    progress_required integer not null default 1,
    state_json jsonb not null default '{}'::jsonb,
    completed_turn smallint,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint quest_objectives_quest_fk
        foreign key (quest_id, owner_id, run_id)
        references quests(id, owner_id, run_id) on delete cascade,
    constraint quest_objectives_index_unique unique (quest_id, objective_index),
    constraint quest_objectives_index check (objective_index >= 0),
    constraint quest_objectives_description_not_blank check (btrim(description) <> ''),
    constraint quest_objectives_status check (status in ('pending', 'active', 'completed', 'failed', 'skipped')),
    constraint quest_objectives_progress check (
        progress_required >= 1 and progress_current between 0 and progress_required
    ),
    constraint quest_objectives_completion_shape check ((status = 'completed') = (completed_turn is not null)),
    constraint quest_objectives_state_object check (jsonb_typeof(state_json) = 'object')
);

create index quest_objectives_owner_quest_idx on quest_objectives (owner_id, quest_id, objective_index);
create index quest_objectives_run_idx on quest_objectives (run_id);

create table save_slots (
    id uuid primary key default gen_random_uuid(),
    owner_id uuid not null,
    campaign_id uuid not null,
    slot_no smallint not null,
    title text not null,
    latest_snapshot_id uuid,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint save_slots_campaign_owner_fk
        foreign key (campaign_id, owner_id) references campaigns(id, owner_id) on delete cascade,
    constraint save_slots_id_owner_campaign_unique unique (id, owner_id, campaign_id),
    constraint save_slots_owner_campaign_slot_unique unique (owner_id, campaign_id, slot_no),
    constraint save_slots_slot_no check (slot_no between 1 and 10),
    constraint save_slots_title_not_blank check (btrim(title) <> '')
);

create index save_slots_owner_campaign_idx on save_slots (owner_id, campaign_id);
create index save_slots_latest_snapshot_idx
    on save_slots (latest_snapshot_id) where latest_snapshot_id is not null;

create table save_snapshots (
    id uuid primary key default gen_random_uuid(),
    slot_id uuid not null,
    owner_id uuid not null,
    campaign_id uuid not null,
    run_id uuid not null,
    run_version bigint not null,
    current_turn smallint not null,
    schema_version text not null,
    state_json jsonb not null,
    checksum_sha256 text not null,
    created_at timestamptz not null default now(),
    constraint save_snapshots_slot_owner_campaign_fk
        foreign key (slot_id, owner_id, campaign_id)
        references save_slots(id, owner_id, campaign_id) on delete cascade,
    constraint save_snapshots_run_owner_campaign_fk
        foreign key (run_id, owner_id, campaign_id)
        references runs(id, owner_id, campaign_id) on delete cascade,
    constraint save_snapshots_id_slot_owner_unique unique (id, slot_id, owner_id),
    constraint save_snapshots_slot_run_version_unique unique (slot_id, run_id, run_version),
    constraint save_snapshots_version check (run_version >= 1 and current_turn >= 0 and run_version >= current_turn::bigint + 1),
    constraint save_snapshots_schema_version_not_blank check (btrim(schema_version) <> ''),
    constraint save_snapshots_state_object check (jsonb_typeof(state_json) = 'object'),
    constraint save_snapshots_checksum check (checksum_sha256 ~ '^[0-9a-f]{64}$')
);

alter table save_slots
    add constraint save_slots_latest_snapshot_fk
    foreign key (latest_snapshot_id, id, owner_id)
    references save_snapshots(id, slot_id, owner_id)
    deferrable initially deferred;

create index save_snapshots_owner_run_created_idx on save_snapshots (owner_id, run_id, created_at desc);
create index save_snapshots_campaign_idx on save_snapshots (campaign_id);

create table llm_logs (
    id uuid primary key default gen_random_uuid(),
    owner_id uuid not null,
    run_id uuid not null,
    turn_record_id uuid,
    purpose text not null,
    provider text not null,
    model text not null,
    provider_request_id text,
    prompt_version text not null,
    prompt_hash text not null,
    status text not null,
    fallback_used boolean not null default false,
    input_tokens integer,
    output_tokens integer,
    latency_ms integer,
    error_code text,
    redacted_input_json jsonb not null default '{}'::jsonb,
    redacted_output_json jsonb,
    created_at timestamptz not null default now(),
    constraint llm_logs_run_owner_fk
        foreign key (run_id, owner_id) references runs(id, owner_id) on delete cascade,
    constraint llm_logs_turn_record_fk
        foreign key (turn_record_id, owner_id, run_id)
        references turn_records(id, owner_id, run_id) on delete cascade,
    constraint llm_logs_purpose_not_blank check (btrim(purpose) <> ''),
    constraint llm_logs_provider_not_blank check (btrim(provider) <> ''),
    constraint llm_logs_model_not_blank check (btrim(model) <> ''),
    constraint llm_logs_prompt_version_not_blank check (btrim(prompt_version) <> ''),
    constraint llm_logs_prompt_hash check (char_length(prompt_hash) between 8 and 128),
    constraint llm_logs_status check (status in ('succeeded', 'failed', 'timed_out', 'fallback')),
    constraint llm_logs_usage check (
        (input_tokens is null or input_tokens >= 0)
        and (output_tokens is null or output_tokens >= 0)
        and (latency_ms is null or latency_ms >= 0)
    ),
    constraint llm_logs_error_shape check (
        (status in ('succeeded', 'fallback') and error_code is null)
        or (
            status in ('failed', 'timed_out')
            and error_code is not null
            and btrim(error_code) <> ''
        )
    ),
    constraint llm_logs_fallback_shape check ((status = 'fallback') = fallback_used),
    constraint llm_logs_input_object check (jsonb_typeof(redacted_input_json) = 'object'),
    constraint llm_logs_output_object check (redacted_output_json is null or jsonb_typeof(redacted_output_json) = 'object')
);

comment on table llm_logs is
    'Redacted observability only. API keys, authorization headers, resolution_seed, and unredacted player PII are forbidden.';

create index llm_logs_owner_run_created_idx on llm_logs (owner_id, run_id, created_at desc);
create index llm_logs_turn_record_idx on llm_logs (turn_record_id) where turn_record_id is not null;
create index llm_logs_provider_request_idx on llm_logs (provider, provider_request_id) where provider_request_id is not null;

create or replace function keyboard_wanderer.validate_region_bounds()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
declare
    world_width integer;
    world_height integer;
begin
    select width, height into strict world_width, world_height
    from keyboard_wanderer.worlds
    where id = new.world_id and owner_id = new.owner_id;

    if new.origin_x + new.width > world_width or new.origin_y + new.height > world_height then
        raise exception using
            errcode = '23514',
            message = 'region bounds must be contained by its world';
    end if;
    return new;
end
$$;

create or replace function keyboard_wanderer.validate_area_bounds()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
declare
    region_x integer;
    region_y integer;
    region_width integer;
    region_height integer;
begin
    select origin_x, origin_y, width, height
      into strict region_x, region_y, region_width, region_height
    from keyboard_wanderer.regions
    where id = new.region_id and owner_id = new.owner_id and world_id = new.world_id;

    if new.origin_x < region_x
       or new.origin_y < region_y
       or new.origin_x + new.width > region_x + region_width
       or new.origin_y + new.height > region_y + region_height then
        raise exception using
            errcode = '23514',
            message = 'area bounds must be contained by its region';
    end if;
    return new;
end
$$;

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
    select width, height into strict from_width, from_height
    from keyboard_wanderer.areas
    where id = new.from_area_id and owner_id = new.owner_id and world_id = new.world_id;

    select width, height into strict to_width, to_height
    from keyboard_wanderer.areas
    where id = new.to_area_id and owner_id = new.owner_id and world_id = new.world_id;

    if new.from_x not between 0 and from_width - 1
       or new.from_y not between 0 and from_height - 1
       or new.to_x not between 0 and to_width - 1
       or new.to_y not between 0 and to_height - 1 then
        raise exception using
            errcode = '23514',
            message = 'area connection endpoint is outside its area';
    end if;
    return new;
end
$$;

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

create or replace function keyboard_wanderer.enforce_run_transition()
returns trigger
language plpgsql
set search_path = pg_catalog
as $$
declare
    gameplay_changed boolean;
begin
    if new.id is distinct from old.id
       or new.owner_id is distinct from old.owner_id
       or new.campaign_id is distinct from old.campaign_id
       or new.world_id is distinct from old.world_id
       or new.turn_limit is distinct from old.turn_limit
       or new.resolution_seed is distinct from old.resolution_seed
       or new.created_at is distinct from old.created_at
       or new.started_at is distinct from old.started_at then
        raise exception using errcode = '55000', message = 'run identity, limits, and resolution seed are immutable';
    end if;

    if new.version < old.version or new.version > old.version + 1 then
        raise exception using errcode = '23514', message = 'run version must stay unchanged or increment by exactly one';
    end if;
    if new.current_turn < old.current_turn or new.current_turn > old.current_turn + 1 then
        raise exception using errcode = '23514', message = 'current_turn must stay unchanged or increment by exactly one';
    end if;

    gameplay_changed :=
        new.current_turn is distinct from old.current_turn
        or new.focus is distinct from old.focus
        or new.pressure is distinct from old.pressure
        or new.active_area_id is distinct from old.active_area_id
        or new.player_entity_id is distinct from old.player_entity_id
        or new.world_state is distinct from old.world_state
        or new.status is distinct from old.status
        or new.ending_code is distinct from old.ending_code
        or new.completed_at is distinct from old.completed_at;

    if gameplay_changed and new.version <> old.version + 1 then
        raise exception using errcode = '23514', message = 'gameplay state changes require a one-step run version increment';
    end if;
    if not gameplay_changed and new.version <> old.version then
        raise exception using errcode = '23514', message = 'run version cannot change without a gameplay state change';
    end if;
    if new.current_turn = old.current_turn + 1 and new.version <> old.version + 1 then
        raise exception using errcode = '23514', message = 'a committed turn and run version must advance together';
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
    select width, height into strict area_width, area_height
    from keyboard_wanderer.areas
    where id = new.area_id and owner_id = new.owner_id and world_id = new.world_id;

    if new.x not between 0 and area_width - 1 or new.y not between 0 and area_height - 1 then
        raise exception using errcode = '23514', message = 'entity position is outside its area';
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

create or replace function keyboard_wanderer.validate_item_limits()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
declare
    allowed_stack integer;
    inventory_capacity integer;
begin
    select max_stack into allowed_stack
    from keyboard_wanderer.item_catalog
    where code = new.item_code;

    if allowed_stack is not null and new.quantity > allowed_stack then
        raise exception using errcode = '23514', message = 'item quantity exceeds catalog max_stack';
    end if;

    if new.inventory_id is not null then
        select capacity into inventory_capacity
        from keyboard_wanderer.inventories
        where id = new.inventory_id and owner_id = new.owner_id and run_id = new.run_id;

        if inventory_capacity is not null and new.slot_index >= inventory_capacity then
            raise exception using errcode = '23514', message = 'item slot_index exceeds inventory capacity';
        end if;
    end if;
    return new;
end
$$;

create or replace function keyboard_wanderer.enforce_turn_record_transition()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
declare
    run_turn_limit integer;
    authoritative_version bigint;
    authoritative_turn integer;
begin
    select turn_limit, version, current_turn
      into strict run_turn_limit, authoritative_version, authoritative_turn
    from keyboard_wanderer.runs
    where id = new.run_id and owner_id = new.owner_id;

    if new.turn_no is not null and new.turn_no > run_turn_limit then
        raise exception using errcode = '23514', message = 'turn_no exceeds the run turn limit';
    end if;

    if tg_op = 'UPDATE' then
        if new.id is distinct from old.id
           or new.run_id is distinct from old.run_id
           or new.owner_id is distinct from old.owner_id
           or new.idempotency_key is distinct from old.idempotency_key
           or new.request_fingerprint is distinct from old.request_fingerprint
           or new.expected_run_version is distinct from old.expected_run_version
           or new.request_json is distinct from old.request_json
           or new.received_at is distinct from old.received_at
           or new.created_at is distinct from old.created_at then
            raise exception using errcode = '55000', message = 'turn request identity and payload are immutable';
        end if;
        if old.status <> 'pending' then
            raise exception using
                errcode = '55000',
                message = 'terminal turn records are immutable after the authoritative commit';
        end if;
        if old.status = 'pending' and new.status not in ('pending', 'committed', 'rejected') then
            raise exception using errcode = '23514', message = 'invalid turn record transition';
        end if;
    end if;

    if new.status = 'committed' and tg_op = 'INSERT' then
        if authoritative_version <> new.committed_run_version
           or authoritative_turn <> new.turn_no then
            raise exception using
                errcode = '40001',
                message = 'commit the optimistic run update before finalizing its turn record';
        end if;
    elsif new.status = 'committed' and tg_op = 'UPDATE' and old.status = 'pending' then
        if authoritative_version <> new.committed_run_version
           or authoritative_turn <> new.turn_no then
            raise exception using
                errcode = '40001',
                message = 'commit the optimistic run update before finalizing its turn record';
        end if;
    end if;
    return new;
end
$$;

create trigger regions_validate_bounds
before insert or update on regions
for each row execute function keyboard_wanderer.validate_region_bounds();

create trigger areas_validate_bounds
before insert or update on areas
for each row execute function keyboard_wanderer.validate_area_bounds();

create trigger area_connections_validate_endpoints
before insert or update on area_connections
for each row execute function keyboard_wanderer.validate_area_connection_endpoints();

create trigger worlds_prevent_layout_regeneration
before update on worlds
for each row execute function keyboard_wanderer.prevent_layout_regeneration();

create trigger regions_prevent_layout_regeneration
before update on regions
for each row execute function keyboard_wanderer.prevent_layout_regeneration();

create trigger areas_prevent_layout_regeneration
before update on areas
for each row execute function keyboard_wanderer.prevent_layout_regeneration();

create trigger runs_enforce_transition
before update on runs
for each row execute function keyboard_wanderer.enforce_run_transition();

create trigger entity_positions_validate
before insert or update on entity_positions
for each row execute function keyboard_wanderer.validate_entity_position();

create trigger items_validate_limits
before insert or update on items
for each row execute function keyboard_wanderer.validate_item_limits();

create trigger turn_records_enforce_transition
before insert or update on turn_records
for each row execute function keyboard_wanderer.enforce_turn_record_transition();

do $$
declare
    table_name text;
begin
    foreach table_name in array array[
        'profiles', 'campaigns', 'worlds', 'regions', 'areas', 'runs', 'entities',
        'actors', 'entity_positions', 'inventories', 'items', 'turn_records',
        'world_facts', 'rumors', 'rumor_knowledge', 'npc_memories',
        'npc_relationships', 'quests', 'quest_objectives', 'save_slots'
    ]
    loop
        execute format(
            'create trigger %I before update on keyboard_wanderer.%I for each row execute function keyboard_wanderer.set_updated_at()',
            table_name || '_set_updated_at',
            table_name
        );
    end loop;
end
$$;

comment on table worlds is
    'The complete generated world layout. Layout columns are immutable; turns move entities through pre-generated areas.';
comment on table entity_positions is
    'Mutable current positions within immutable generated areas. Blocking occupancy is unique per run/area/layer/tile.';
comment on table save_snapshots is
    'Server-authored state snapshots. The checksum covers canonical state_json; resolution_seed must be stored separately in runs.';

revoke all on schema keyboard_wanderer from public;
revoke all on all tables in schema keyboard_wanderer from public;
revoke all on all sequences in schema keyboard_wanderer from public;
revoke all on all functions in schema keyboard_wanderer from public;

alter default privileges in schema keyboard_wanderer revoke all on tables from public;
alter default privileges in schema keyboard_wanderer revoke all on sequences from public;
alter default privileges in schema keyboard_wanderer revoke execute on functions from public;

commit;
