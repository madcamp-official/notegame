begin;

set local search_path = keyboard_wanderer, public;

-- The v4 catalogs used globally unique phase ordinals for one fixed scenario.
-- Generative campaigns use stable semantic keys; legacy rows may coexist
-- during upgrade, so ordinal columns are now descriptive rather than unique.
alter table campaign_region_role_catalog
    drop constraint if exists campaign_region_role_catalog_phase_no_key;
alter table campaign_phase_catalog
    drop constraint if exists campaign_phase_catalog_display_order_key;
alter table ending_catalog drop constraint if exists ending_catalog_category;
alter table ending_catalog
    add constraint ending_catalog_category check (
        category in ('reconciliation', 'freedom', 'guardianship', 'release', 'memory', 'return', 'emergency',
                     'recovery', 'administrator', 'reset', 'shutdown')
    );
alter table campaign_story_beats drop constraint if exists campaign_story_beats_key_format;
alter table campaign_story_beats
    add constraint campaign_story_beats_key_format
    check (beat_key ~ '^[a-z][a-z0-9_.:-]{2,127}$');

-- A campaign may have one lightweight selection preview plus any number of
-- sealed run-start worlds. run_scope_key is preallocated as the future run UUID
-- so the world can be inserted before its runs row in the same transaction.
alter table worlds drop constraint if exists worlds_one_per_campaign;

alter table worlds
    add column if not exists world_scope text not null default 'campaign_preview',
    add column if not exists run_scope_key uuid;

do $$
begin
    if not exists (
        select 1 from pg_catalog.pg_constraint
        where conrelid = 'keyboard_wanderer.worlds'::regclass
          and conname = 'worlds_scope_shape'
    ) then
        alter table keyboard_wanderer.worlds
            add constraint worlds_scope_shape check (
                (world_scope = 'campaign_preview' and run_scope_key is null)
                or (world_scope = 'run' and run_scope_key is not null)
            );
    end if;
end
$$;

create unique index if not exists worlds_campaign_preview_unique
    on worlds (campaign_id) where world_scope = 'campaign_preview';
create unique index if not exists worlds_run_scope_key_unique
    on worlds (run_scope_key) where world_scope = 'run';
create index if not exists worlds_owner_campaign_scope_idx
    on worlds (owner_id, campaign_id, world_scope, generated_at desc);

-- One sealed, validated narrative plan is created for each run. The immutable
-- world geometry remains in worlds/regions/areas/placement_slots; this plan
-- varies campaign beats, NPC roles, quests, hooks, and ending candidates.
create table if not exists run_generation_plans (
    id uuid primary key default gen_random_uuid(),
    run_id uuid not null,
    owner_id uuid not null,
    world_id uuid not null,
    schema_version text not null,
    generator_version text not null,
    generation_seed bigint not null,
    plan_hash text not null,
    source text not null,
    provider text,
    model text,
    prompt_version text,
    prompt_hash text,
    fallback_used boolean not null default false,
    validation_status text not null,
    validation_report jsonb not null default '{}'::jsonb,
    validation_errors jsonb not null default '[]'::jsonb,
    plan_json jsonb not null,
    validated_at timestamptz not null,
    created_at timestamptz not null default now(),
    constraint run_generation_plans_run_fk
        foreign key (run_id, owner_id, world_id)
        references runs(id, owner_id, world_id) on delete cascade,
    constraint run_generation_plans_run_unique unique (run_id),
    constraint run_generation_plans_identity_unique unique (id, owner_id, run_id),
    constraint run_generation_plans_run_hash_unique unique (run_id, plan_hash),
    constraint run_generation_plans_versions check (
        btrim(schema_version) <> '' and btrim(generator_version) <> ''
    ),
    constraint run_generation_plans_hash check (plan_hash ~ '^[0-9a-f]{64}$'),
    constraint run_generation_plans_source check (
        source in ('deterministic', 'model', 'hybrid', 'fallback')
    ),
    constraint run_generation_plans_model_provenance check (
        (
            source in ('model', 'hybrid')
            and provider is not null and btrim(provider) <> ''
            and model is not null and btrim(model) <> ''
            and prompt_version is not null and btrim(prompt_version) <> ''
            and prompt_hash is not null and char_length(btrim(prompt_hash)) between 8 and 128
        )
        or (
            source in ('deterministic', 'fallback')
            and (
                (
                    provider is null and model is null
                    and prompt_version is null and prompt_hash is null
                )
                or (
                    provider is not null and btrim(provider) <> ''
                    and model is not null and btrim(model) <> ''
                    and prompt_version is not null and btrim(prompt_version) <> ''
                    and prompt_hash is not null
                    and char_length(btrim(prompt_hash)) between 8 and 128
                )
            )
        )
    ),
    constraint run_generation_plans_fallback_shape check (
        fallback_used = (source = 'fallback')
        and validation_status = case
            when fallback_used then 'fallback_validated'
            else 'validated'
        end
    ),
    constraint run_generation_plans_validation_json check (
        jsonb_typeof(validation_report) = 'object'
        and jsonb_typeof(validation_errors) = 'array'
        and (
            (not fallback_used and jsonb_array_length(validation_errors) = 0)
            or fallback_used
        )
    ),
    constraint run_generation_plans_plan_object check (
        jsonb_typeof(plan_json) = 'object' and plan_json <> '{}'::jsonb
    ),
    constraint run_generation_plans_validation_time check (validated_at <= created_at)
);

create index if not exists run_generation_plans_owner_created_idx
    on run_generation_plans (owner_id, created_at desc);
create index if not exists run_generation_plans_plan_gin
    on run_generation_plans using gin (plan_json jsonb_path_ops);

-- Bind run-specific plan nodes to the pre-generated semantic slots. Bindings
-- may change status during play, but never create or move world geometry.
create table if not exists run_slot_bindings (
    id uuid primary key default gen_random_uuid(),
    run_id uuid not null,
    owner_id uuid not null,
    world_id uuid not null,
    generation_plan_id uuid not null,
    slot_id uuid not null,
    binding_key text not null,
    binding_kind text not null,
    plan_node_key text not null,
    entity_id uuid,
    quest_id uuid,
    status text not null default 'reserved',
    activation_turn smallint,
    released_turn smallint,
    binding_payload jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint run_slot_bindings_run_fk
        foreign key (run_id, owner_id, world_id)
        references runs(id, owner_id, world_id) on delete cascade,
    constraint run_slot_bindings_plan_fk
        foreign key (generation_plan_id, owner_id, run_id)
        references run_generation_plans(id, owner_id, run_id) on delete cascade,
    constraint run_slot_bindings_slot_fk
        foreign key (slot_id, owner_id, world_id)
        references placement_slots(id, owner_id, world_id) on delete cascade,
    constraint run_slot_bindings_entity_fk
        foreign key (entity_id, owner_id, run_id)
        references entities(id, owner_id, run_id) on delete set null (entity_id),
    constraint run_slot_bindings_quest_fk
        foreign key (quest_id, owner_id, run_id)
        references quests(id, owner_id, run_id) on delete set null (quest_id),
    constraint run_slot_bindings_run_key_unique unique (run_id, binding_key),
    constraint run_slot_bindings_key_format check (
        binding_key ~ '^[a-z][a-z0-9_.:-]{2,127}$'
        and binding_kind ~ '^[a-z][a-z0-9_.:-]{1,63}$'
        and btrim(plan_node_key) <> ''
    ),
    constraint run_slot_bindings_status check (
        status in ('reserved', 'active', 'fulfilled', 'released')
    ),
    constraint run_slot_bindings_turns check (
        (activation_turn is null or activation_turn between 0 and 50)
        and (released_turn is null or released_turn between 0 and 50)
        and (released_turn is null or activation_turn is null or released_turn >= activation_turn)
    ),
    constraint run_slot_bindings_status_shape check (
        (status = 'reserved' and activation_turn is null and released_turn is null)
        or (status in ('active', 'fulfilled') and activation_turn is not null and released_turn is null)
        or (status = 'released' and released_turn is not null)
    ),
    constraint run_slot_bindings_payload_object check (jsonb_typeof(binding_payload) = 'object')
);

create index if not exists run_slot_bindings_owner_run_status_idx
    on run_slot_bindings (owner_id, run_id, status, binding_kind);
create index if not exists run_slot_bindings_slot_idx on run_slot_bindings (slot_id);
create index if not exists run_slot_bindings_entity_idx
    on run_slot_bindings (entity_id) where entity_id is not null;
create index if not exists run_slot_bindings_quest_idx
    on run_slot_bindings (quest_id) where quest_id is not null;
create unique index if not exists run_slot_bindings_live_slot_unique
    on run_slot_bindings (run_id, slot_id) where status <> 'released';

-- Generic, queryable projection of campaign progress and Rule Engine state.
-- It deliberately has no scenario-specific privilege levels, fixed acts, or
-- fixed ending codes. runs.world_state remains the complete authority.
create table if not exists run_progress_states (
    run_id uuid primary key,
    owner_id uuid not null,
    generation_plan_id uuid not null,
    status text not null default 'active',
    current_node_key text,
    state_version bigint not null default 1,
    last_turn_no smallint not null default 0,
    completed_node_keys jsonb not null default '[]'::jsonb,
    failed_node_keys jsonb not null default '[]'::jsonb,
    ending_candidate_keys jsonb not null default '[]'::jsonb,
    open_threads jsonb not null default '[]'::jsonb,
    progress_state jsonb not null default '{}'::jsonb,
    rule_state jsonb not null default '{}'::jsonb,
    convergence_state jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint run_progress_states_run_fk
        foreign key (run_id, owner_id) references runs(id, owner_id) on delete cascade,
    constraint run_progress_states_plan_fk
        foreign key (generation_plan_id, owner_id, run_id)
        references run_generation_plans(id, owner_id, run_id) on delete cascade,
    constraint run_progress_states_status check (
        status in ('active', 'converging', 'completed', 'recovery_required')
    ),
    constraint run_progress_states_current_node check (
        current_node_key is null or btrim(current_node_key) <> ''
    ),
    constraint run_progress_states_version check (state_version >= 1),
    constraint run_progress_states_turn check (last_turn_no between 0 and 50),
    constraint run_progress_states_json check (
        jsonb_typeof(completed_node_keys) = 'array'
        and jsonb_typeof(failed_node_keys) = 'array'
        and jsonb_typeof(ending_candidate_keys) = 'array'
        and jsonb_typeof(open_threads) = 'array'
        and jsonb_typeof(progress_state) = 'object'
        and jsonb_typeof(rule_state) = 'object'
        and jsonb_typeof(convergence_state) = 'object'
    )
);

create index if not exists run_progress_states_owner_status_idx
    on run_progress_states (owner_id, status, updated_at desc);
create index if not exists run_progress_states_open_threads_gin
    on run_progress_states using gin (open_threads jsonb_path_ops);

-- One append-only mechanical resolution for each committed meaningful turn.
-- LLM output is intentionally absent: only the server Rule Engine may write
-- the roll, modifiers, DC, outcome, budgets, and state hashes below.
create table if not exists turn_rule_resolutions (
    turn_record_id uuid primary key,
    run_id uuid not null,
    owner_id uuid not null,
    turn_no smallint not null,
    ruleset_version text not null,
    normalized_attempt jsonb not null,
    d20_raw smallint not null,
    modifier_total smallint not null default 0,
    modifier_breakdown jsonb not null default '[]'::jsonb,
    roll_total smallint not null,
    difficulty_class smallint not null,
    outcome text not null,
    consequence_budget smallint not null default 0,
    costs_json jsonb not null default '{}'::jsonb,
    guaranteed_operations jsonb not null default '[]'::jsonb,
    allowed_effects jsonb not null default '[]'::jsonb,
    state_delta jsonb not null default '{}'::jsonb,
    state_hash_before text not null,
    state_hash_after text not null,
    rng_audit jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    constraint turn_rule_resolutions_turn_fk
        foreign key (turn_record_id, owner_id, run_id)
        references turn_records(id, owner_id, run_id) on delete cascade,
    constraint turn_rule_resolutions_run_turn_unique unique (run_id, turn_no),
    constraint turn_rule_resolutions_turn check (turn_no between 1 and 50),
    constraint turn_rule_resolutions_ruleset check (btrim(ruleset_version) <> ''),
    constraint turn_rule_resolutions_d20 check (d20_raw between 1 and 20),
    constraint turn_rule_resolutions_modifier check (modifier_total between -100 and 100),
    constraint turn_rule_resolutions_roll_total check (roll_total = d20_raw + modifier_total),
    constraint turn_rule_resolutions_dc check (difficulty_class between 1 and 100),
    constraint turn_rule_resolutions_outcome check (
        outcome in ('critical_failure', 'failure', 'partial_success', 'success', 'critical_success')
    ),
    constraint turn_rule_resolutions_budget check (consequence_budget between 0 and 100),
    constraint turn_rule_resolutions_hashes check (
        state_hash_before ~ '^[0-9a-f]{64}$'
        and state_hash_after ~ '^[0-9a-f]{64}$'
    ),
    constraint turn_rule_resolutions_json check (
        jsonb_typeof(normalized_attempt) = 'object'
        and jsonb_typeof(modifier_breakdown) = 'array'
        and jsonb_typeof(costs_json) = 'object'
        and jsonb_typeof(guaranteed_operations) = 'array'
        and jsonb_typeof(allowed_effects) = 'array'
        and jsonb_typeof(state_delta) = 'object'
        and jsonb_typeof(rng_audit) = 'object'
    )
);

create index if not exists turn_rule_resolutions_owner_run_idx
    on turn_rule_resolutions (owner_id, run_id, turn_no desc);

-- Extend the existing save format without invalidating legacy snapshots. A
-- snapshot is "deep" when all generation-contract columns are present; those
-- rows can prove the exact world, plan, event cursor, and canonical state.
alter table save_snapshots
    add column if not exists snapshot_kind text not null default 'legacy',
    add column if not exists world_id uuid,
    add column if not exists generation_plan_id uuid,
    add column if not exists plan_hash text,
    add column if not exists layout_hash text,
    add column if not exists last_turn_record_id uuid,
    add column if not exists last_event_id bigint,
    add column if not exists resume_metadata jsonb not null default '{}'::jsonb;

do $$
begin
    if not exists (
        select 1 from pg_catalog.pg_constraint
        where conrelid = 'keyboard_wanderer.save_snapshots'::regclass
          and conname = 'save_snapshots_id_owner_run_unique'
    ) then
        alter table keyboard_wanderer.save_snapshots
            add constraint save_snapshots_id_owner_run_unique unique (id, owner_id, run_id);
    end if;

    if not exists (
        select 1 from pg_catalog.pg_constraint
        where conrelid = 'keyboard_wanderer.save_snapshots'::regclass
          and conname = 'save_snapshots_run_world_fk'
    ) then
        alter table keyboard_wanderer.save_snapshots
            add constraint save_snapshots_run_world_fk
            foreign key (run_id, owner_id, world_id)
            references keyboard_wanderer.runs(id, owner_id, world_id)
            deferrable initially deferred;
    end if;

    if not exists (
        select 1 from pg_catalog.pg_constraint
        where conrelid = 'keyboard_wanderer.save_snapshots'::regclass
          and conname = 'save_snapshots_generation_plan_fk'
    ) then
        alter table keyboard_wanderer.save_snapshots
            add constraint save_snapshots_generation_plan_fk
            foreign key (generation_plan_id, owner_id, run_id)
            references keyboard_wanderer.run_generation_plans(id, owner_id, run_id)
            deferrable initially deferred;
    end if;

    if not exists (
        select 1 from pg_catalog.pg_constraint
        where conrelid = 'keyboard_wanderer.save_snapshots'::regclass
          and conname = 'save_snapshots_last_turn_record_fk'
    ) then
        alter table keyboard_wanderer.save_snapshots
            add constraint save_snapshots_last_turn_record_fk
            foreign key (last_turn_record_id, owner_id, run_id)
            references keyboard_wanderer.turn_records(id, owner_id, run_id)
            deferrable initially deferred;
    end if;

    if not exists (
        select 1 from pg_catalog.pg_constraint
        where conrelid = 'keyboard_wanderer.save_snapshots'::regclass
          and conname = 'save_snapshots_last_event_fk'
    ) then
        alter table keyboard_wanderer.save_snapshots
            add constraint save_snapshots_last_event_fk
            foreign key (last_event_id) references keyboard_wanderer.turn_events(id)
            deferrable initially deferred;
    end if;

    if not exists (
        select 1 from pg_catalog.pg_constraint
        where conrelid = 'keyboard_wanderer.save_snapshots'::regclass
          and conname = 'save_snapshots_generation_contract'
    ) then
        alter table keyboard_wanderer.save_snapshots
            add constraint save_snapshots_generation_contract check (
                (
                    snapshot_kind = 'legacy'
                    and world_id is null
                    and generation_plan_id is null
                    and plan_hash is null
                    and layout_hash is null
                    and last_turn_record_id is null
                    and last_event_id is null
                )
                or (
                    snapshot_kind in ('manual', 'autosave', 'checkpoint', 'recovery')
                    and world_id is not null
                    and generation_plan_id is not null
                    and plan_hash ~ '^[0-9a-f]{64}$'
                    and char_length(layout_hash) between 8 and 128
                    and (
                        (current_turn = 0 and last_turn_record_id is null and last_event_id is null)
                        or (current_turn > 0 and last_turn_record_id is not null and last_event_id is not null)
                    )
                )
            );
    end if;

    if not exists (
        select 1 from pg_catalog.pg_constraint
        where conrelid = 'keyboard_wanderer.save_snapshots'::regclass
          and conname = 'save_snapshots_resume_metadata_object'
    ) then
        alter table keyboard_wanderer.save_snapshots
            add constraint save_snapshots_resume_metadata_object
            check (jsonb_typeof(resume_metadata) = 'object');
    end if;
end
$$;

create index if not exists save_snapshots_deep_resume_idx
    on save_snapshots (owner_id, run_id, current_turn desc, run_version desc)
    where generation_plan_id is not null;
create index if not exists save_snapshots_last_turn_idx
    on save_snapshots (last_turn_record_id) where last_turn_record_id is not null;
create index if not exists save_snapshots_last_event_idx
    on save_snapshots (last_event_id) where last_event_id is not null;

-- Every resume attempt is independently auditable. Acceptance means the
-- canonical state checksum and the sealed plan/layout contract were verified;
-- rejected or quarantined attempts retain structured failure reasons.
create table if not exists resume_validation_records (
    id uuid primary key default gen_random_uuid(),
    snapshot_id uuid not null,
    run_id uuid not null,
    owner_id uuid not null,
    attempt_no integer not null,
    validation_status text not null,
    observed_checksum_sha256 text,
    observed_plan_hash text,
    observed_layout_hash text,
    checks_json jsonb not null default '{}'::jsonb,
    errors_json jsonb not null default '[]'::jsonb,
    created_at timestamptz not null default now(),
    constraint resume_validation_records_snapshot_fk
        foreign key (snapshot_id, owner_id, run_id)
        references save_snapshots(id, owner_id, run_id) on delete cascade,
    constraint resume_validation_records_attempt_unique unique (snapshot_id, attempt_no),
    constraint resume_validation_records_attempt check (attempt_no >= 1),
    constraint resume_validation_records_status check (
        validation_status in ('accepted', 'rejected', 'quarantined')
    ),
    constraint resume_validation_records_hashes check (
        (observed_checksum_sha256 is null or observed_checksum_sha256 ~ '^[0-9a-f]{64}$')
        and (observed_plan_hash is null or observed_plan_hash ~ '^[0-9a-f]{64}$')
        and (observed_layout_hash is null or char_length(observed_layout_hash) between 8 and 128)
    ),
    constraint resume_validation_records_json check (
        jsonb_typeof(checks_json) = 'object' and jsonb_typeof(errors_json) = 'array'
    ),
    constraint resume_validation_records_result_shape check (
        (
            validation_status = 'accepted'
            and observed_checksum_sha256 is not null
            and jsonb_array_length(errors_json) = 0
        )
        or (
            validation_status in ('rejected', 'quarantined')
            and jsonb_array_length(errors_json) > 0
        )
    )
);

create index if not exists resume_validation_records_owner_run_idx
    on resume_validation_records (owner_id, run_id, created_at desc);

create or replace function keyboard_wanderer.validate_run_scoped_world_link()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
begin
    if new.world_scope = 'run' and not exists (
        select 1
        from keyboard_wanderer.runs
        where id = new.run_scope_key
          and world_id = new.id
          and owner_id = new.owner_id
          and campaign_id = new.campaign_id
    ) then
        raise exception using
            errcode = '23503',
            message = 'run-scoped world must be linked to the run identified by run_scope_key';
    end if;
    return new;
end
$$;

create or replace function keyboard_wanderer.validate_run_world_link()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
declare
    stored_scope text;
    stored_run_scope_key uuid;
begin
    select world_scope, run_scope_key
      into strict stored_scope, stored_run_scope_key
    from keyboard_wanderer.worlds
    where id = new.world_id
      and owner_id = new.owner_id
      and campaign_id = new.campaign_id;

    if stored_scope = 'run' and stored_run_scope_key is distinct from new.id then
        raise exception using
            errcode = '23503',
            message = 'run must use the run-scoped world reserved for its own UUID';
    end if;
    return new;
end
$$;

create or replace function keyboard_wanderer.validate_generation_plan_world_scope()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
begin
    if not exists (
        select 1
        from keyboard_wanderer.worlds
        where id = new.world_id
          and owner_id = new.owner_id
          and world_scope = 'run'
          and run_scope_key = new.run_id
    ) then
        raise exception using
            errcode = '23514',
            message = 'generation plans require a sealed world scoped to the same run';
    end if;
    return new;
end
$$;

-- Replace the earlier polymorphic trigger body with table-specific branches.
-- PostgreSQL may reorder boolean expressions, so referencing NEW.world_id from
-- a worlds row inside an `and` expression is not a safe short-circuit guard.
create or replace function keyboard_wanderer.prevent_layout_regeneration()
returns trigger
language plpgsql
set search_path = pg_catalog
as $$
begin
    if tg_table_name = 'worlds' then
        if new.campaign_id is distinct from old.campaign_id
           or new.owner_id is distinct from old.owner_id
           or new.generator_version is distinct from old.generator_version
           or new.layout_hash is distinct from old.layout_hash
           or new.width is distinct from old.width
           or new.height is distinct from old.height
           or new.map_json is distinct from old.map_json
           or new.generation_metadata is distinct from old.generation_metadata
           or new.generated_at is distinct from old.generated_at then
            raise exception using
                errcode = '55000',
                message = 'world layout is immutable after generation';
        end if;
    elsif tg_table_name = 'regions' then
        if new.world_id is distinct from old.world_id
           or new.owner_id is distinct from old.owner_id
           or new.region_key is distinct from old.region_key
           or new.origin_x is distinct from old.origin_x
           or new.origin_y is distinct from old.origin_y
           or new.width is distinct from old.width
           or new.height is distinct from old.height
           or new.layout_hash is distinct from old.layout_hash
           or new.map_json is distinct from old.map_json then
            raise exception using
                errcode = '55000',
                message = 'region layout is immutable after generation';
        end if;
    elsif tg_table_name = 'areas' then
        if new.world_id is distinct from old.world_id
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
           or new.tile_json is distinct from old.tile_json then
            raise exception using
                errcode = '55000',
                message = 'area layout is immutable after generation';
        end if;
    else
        raise exception using
            errcode = '55000',
            message = 'layout immutability trigger attached to an unsupported table';
    end if;
    return new;
end
$$;

create or replace function keyboard_wanderer.prevent_world_scope_reassignment()
returns trigger
language plpgsql
set search_path = pg_catalog
as $$
begin
    if new.world_scope is distinct from old.world_scope
       or new.run_scope_key is distinct from old.run_scope_key then
        raise exception using
            errcode = '55000',
            message = 'world scope and run reservation are immutable after generation';
    end if;
    return new;
end
$$;

create or replace function keyboard_wanderer.validate_run_progress_state()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
declare
    committed_turn smallint;
begin
    select current_turn
      into strict committed_turn
    from keyboard_wanderer.runs
    where id = new.run_id and owner_id = new.owner_id;

    if new.last_turn_no > committed_turn then
        raise exception using
            errcode = '23514',
            message = 'progress state cannot lead the authoritative run turn';
    end if;

    if tg_op = 'UPDATE' then
        if new.run_id is distinct from old.run_id
           or new.owner_id is distinct from old.owner_id
           or new.generation_plan_id is distinct from old.generation_plan_id then
            raise exception using
                errcode = '55000',
                message = 'progress state ownership and generation plan are immutable';
        end if;
        if new.state_version <> old.state_version + 1 then
            raise exception using
                errcode = '23514',
                message = 'progress state version must increment by exactly one';
        end if;
        if new.last_turn_no < old.last_turn_no then
            raise exception using
                errcode = '23514',
                message = 'progress state turn cannot move backward';
        end if;
    end if;
    return new;
end
$$;

create or replace function keyboard_wanderer.validate_turn_rule_resolution()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
declare
    record_status text;
    record_turn smallint;
begin
    select status, turn_no
      into strict record_status, record_turn
    from keyboard_wanderer.turn_records
    where id = new.turn_record_id
      and owner_id = new.owner_id
      and run_id = new.run_id;

    if record_status <> 'committed' or record_turn is distinct from new.turn_no then
        raise exception using
            errcode = '23514',
            message = 'rule resolution must match a committed turn record';
    end if;
    return new;
end
$$;

create or replace function keyboard_wanderer.validate_deep_save_snapshot()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
declare
    stored_plan_hash text;
    stored_layout_hash text;
    record_status text;
    record_turn smallint;
    event_record_id uuid;
    event_owner_id uuid;
    event_run_id uuid;
begin
    if new.generation_plan_id is null then
        if exists (
            select 1
            from keyboard_wanderer.run_generation_plans
            where run_id = new.run_id and owner_id = new.owner_id
        ) then
            raise exception using
                errcode = '23514',
                message = 'a generative run requires a deep snapshot with plan and layout provenance';
        end if;
        return new;
    end if;

    select plan_hash
      into strict stored_plan_hash
    from keyboard_wanderer.run_generation_plans
    where id = new.generation_plan_id
      and owner_id = new.owner_id
      and run_id = new.run_id;

    if new.plan_hash <> stored_plan_hash then
        raise exception using
            errcode = '23514',
            message = 'snapshot plan hash does not match its sealed generation plan';
    end if;

    select layout_hash
      into strict stored_layout_hash
    from keyboard_wanderer.worlds
    where id = new.world_id and owner_id = new.owner_id;

    if new.layout_hash <> stored_layout_hash then
        raise exception using
            errcode = '23514',
            message = 'snapshot layout hash does not match its sealed world';
    end if;

    if new.current_turn > 0 then
        select status, turn_no
          into strict record_status, record_turn
        from keyboard_wanderer.turn_records
        where id = new.last_turn_record_id
          and owner_id = new.owner_id
          and run_id = new.run_id;

        if record_status <> 'committed' or record_turn is distinct from new.current_turn then
            raise exception using
                errcode = '23514',
                message = 'snapshot turn cursor does not match its committed turn record';
        end if;

        select turn_record_id, owner_id, run_id
          into strict event_record_id, event_owner_id, event_run_id
        from keyboard_wanderer.turn_events
        where id = new.last_event_id;

        if event_record_id <> new.last_turn_record_id
           or event_owner_id <> new.owner_id
           or event_run_id <> new.run_id then
            raise exception using
                errcode = '23514',
                message = 'snapshot event cursor does not belong to its committed turn';
        end if;

        if exists (
            select 1
            from keyboard_wanderer.turn_events
            where owner_id = new.owner_id
              and run_id = new.run_id
              and id > new.last_event_id
        ) then
            raise exception using
                errcode = '23514',
                message = 'snapshot event cursor is not the latest committed run event';
        end if;
    end if;
    return new;
end
$$;

create or replace function keyboard_wanderer.validate_resume_record()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
declare
    expected_checksum text;
    expected_plan_hash text;
    expected_layout_hash text;
begin
    select checksum_sha256, plan_hash, layout_hash
      into strict expected_checksum, expected_plan_hash, expected_layout_hash
    from keyboard_wanderer.save_snapshots
    where id = new.snapshot_id
      and owner_id = new.owner_id
      and run_id = new.run_id;

    if new.validation_status = 'accepted' then
        if expected_plan_hash is null then
            raise exception using
                errcode = '23514',
                message = 'legacy snapshots cannot be accepted as deep resume state';
        end if;

        if new.observed_checksum_sha256 <> expected_checksum then
            raise exception using
                errcode = '23514',
                message = 'accepted resume checksum must match its snapshot';
        end if;

        if expected_plan_hash is not null and (
            new.observed_plan_hash is distinct from expected_plan_hash
            or new.observed_layout_hash is distinct from expected_layout_hash
        ) then
            raise exception using
                errcode = '23514',
                message = 'accepted resume plan and layout hashes must match its snapshot';
        end if;
    end if;
    return new;
end
$$;

create or replace function keyboard_wanderer.reject_generative_history_mutation()
returns trigger
language plpgsql
set search_path = pg_catalog
as $$
begin
    if tg_op = 'DELETE' and pg_trigger_depth() > 1 then
        return old;
    end if;
    raise exception using
        errcode = '55000',
        message = format('%s rows are append-only', tg_table_name);
end
$$;

do $$
begin
    if not exists (
        select 1 from pg_catalog.pg_trigger
        where tgrelid = 'keyboard_wanderer.worlds'::regclass
          and tgname = 'worlds_run_scope_link_constraint'
          and not tgisinternal
    ) then
        create constraint trigger worlds_run_scope_link_constraint
        after insert or update on keyboard_wanderer.worlds
        deferrable initially deferred
        for each row execute function keyboard_wanderer.validate_run_scoped_world_link();
    end if;

    if not exists (
        select 1 from pg_catalog.pg_trigger
        where tgrelid = 'keyboard_wanderer.runs'::regclass
          and tgname = 'runs_world_scope_link_constraint'
          and not tgisinternal
    ) then
        create constraint trigger runs_world_scope_link_constraint
        after insert or update on keyboard_wanderer.runs
        deferrable initially deferred
        for each row execute function keyboard_wanderer.validate_run_world_link();
    end if;

    if not exists (
        select 1 from pg_catalog.pg_trigger
        where tgrelid = 'keyboard_wanderer.worlds'::regclass
          and tgname = 'worlds_prevent_scope_reassignment'
          and not tgisinternal
    ) then
        create trigger worlds_prevent_scope_reassignment
        before update on keyboard_wanderer.worlds
        for each row execute function keyboard_wanderer.prevent_world_scope_reassignment();
    end if;

    if not exists (
        select 1 from pg_catalog.pg_trigger
        where tgrelid = 'keyboard_wanderer.run_generation_plans'::regclass
          and tgname = 'run_generation_plans_validate_world_scope'
          and not tgisinternal
    ) then
        create trigger run_generation_plans_validate_world_scope
        before insert on keyboard_wanderer.run_generation_plans
        for each row execute function keyboard_wanderer.validate_generation_plan_world_scope();
    end if;

    if not exists (
        select 1 from pg_catalog.pg_trigger
        where tgrelid = 'keyboard_wanderer.run_progress_states'::regclass
          and tgname = 'run_progress_states_validate'
          and not tgisinternal
    ) then
        create trigger run_progress_states_validate
        before insert or update on keyboard_wanderer.run_progress_states
        for each row execute function keyboard_wanderer.validate_run_progress_state();
    end if;

    if not exists (
        select 1 from pg_catalog.pg_trigger
        where tgrelid = 'keyboard_wanderer.run_progress_states'::regclass
          and tgname = 'run_progress_states_set_updated_at'
          and not tgisinternal
    ) then
        create trigger run_progress_states_set_updated_at
        before update on keyboard_wanderer.run_progress_states
        for each row execute function keyboard_wanderer.set_updated_at();
    end if;

    if not exists (
        select 1 from pg_catalog.pg_trigger
        where tgrelid = 'keyboard_wanderer.run_slot_bindings'::regclass
          and tgname = 'run_slot_bindings_set_updated_at'
          and not tgisinternal
    ) then
        create trigger run_slot_bindings_set_updated_at
        before update on keyboard_wanderer.run_slot_bindings
        for each row execute function keyboard_wanderer.set_updated_at();
    end if;

    if not exists (
        select 1 from pg_catalog.pg_trigger
        where tgrelid = 'keyboard_wanderer.turn_rule_resolutions'::regclass
          and tgname = 'turn_rule_resolutions_validate'
          and not tgisinternal
    ) then
        create trigger turn_rule_resolutions_validate
        before insert on keyboard_wanderer.turn_rule_resolutions
        for each row execute function keyboard_wanderer.validate_turn_rule_resolution();
    end if;

    if not exists (
        select 1 from pg_catalog.pg_trigger
        where tgrelid = 'keyboard_wanderer.save_snapshots'::regclass
          and tgname = 'save_snapshots_validate_deep_contract'
          and not tgisinternal
    ) then
        create trigger save_snapshots_validate_deep_contract
        before insert on keyboard_wanderer.save_snapshots
        for each row execute function keyboard_wanderer.validate_deep_save_snapshot();
    end if;

    if not exists (
        select 1 from pg_catalog.pg_trigger
        where tgrelid = 'keyboard_wanderer.resume_validation_records'::regclass
          and tgname = 'resume_validation_records_validate'
          and not tgisinternal
    ) then
        create trigger resume_validation_records_validate
        before insert on keyboard_wanderer.resume_validation_records
        for each row execute function keyboard_wanderer.validate_resume_record();
    end if;
end
$$;

do $$
declare
    table_name text;
begin
    foreach table_name in array array[
        'run_generation_plans', 'turn_rule_resolutions',
        'resume_validation_records', 'save_snapshots'
    ]
    loop
        if not exists (
            select 1 from pg_catalog.pg_trigger
            where tgrelid = format('keyboard_wanderer.%I', table_name)::regclass
              and tgname = table_name || '_append_only'
              and not tgisinternal
        ) then
            execute format(
                'create trigger %I before update or delete on keyboard_wanderer.%I for each row execute function keyboard_wanderer.reject_generative_history_mutation()',
                table_name || '_append_only', table_name
            );
        end if;
    end loop;
end
$$;

-- Mutable run projections receive owner ALL policies. Sealed plans, mechanical
-- resolutions, and resume audits expose only SELECT/INSERT. save_snapshots
-- already has the matching policies from 002_row_security_and_views.sql.
do $$
declare
    table_name text;
    policy_name text;
begin
    foreach table_name in array array['run_slot_bindings', 'run_progress_states']
    loop
        execute format('alter table keyboard_wanderer.%I enable row level security', table_name);
        execute format('alter table keyboard_wanderer.%I force row level security', table_name);
        policy_name := table_name || '_owner_all';
        if not exists (
            select 1 from pg_catalog.pg_policies
            where schemaname = 'keyboard_wanderer'
              and tablename = table_name
              and policyname = policy_name
        ) then
            execute format(
                'create policy %I on keyboard_wanderer.%I for all using (owner_id = (select keyboard_wanderer.current_app_user_id())) with check (owner_id = (select keyboard_wanderer.current_app_user_id()))',
                policy_name, table_name
            );
        end if;
    end loop;

    foreach table_name in array array[
        'run_generation_plans', 'turn_rule_resolutions', 'resume_validation_records'
    ]
    loop
        execute format('alter table keyboard_wanderer.%I enable row level security', table_name);
        execute format('alter table keyboard_wanderer.%I force row level security', table_name);

        policy_name := table_name || '_owner_select';
        if not exists (
            select 1 from pg_catalog.pg_policies
            where schemaname = 'keyboard_wanderer'
              and tablename = table_name
              and policyname = policy_name
        ) then
            execute format(
                'create policy %I on keyboard_wanderer.%I for select using (owner_id = (select keyboard_wanderer.current_app_user_id()))',
                policy_name, table_name
            );
        end if;

        policy_name := table_name || '_owner_insert';
        if not exists (
            select 1 from pg_catalog.pg_policies
            where schemaname = 'keyboard_wanderer'
              and tablename = table_name
              and policyname = policy_name
        ) then
            execute format(
                'create policy %I on keyboard_wanderer.%I for insert with check (owner_id = (select keyboard_wanderer.current_app_user_id()))',
                policy_name, table_name
            );
        end if;
    end loop;
end
$$;

comment on table run_generation_plans is
    'Immutable, schema-validated plan for exactly one run and its run-start sealed world. No resolution seed or API secret may be stored here.';
comment on column worlds.world_scope is
    'campaign_preview is the single selection-time preview; run is a sealed world generated for one run start.';
comment on column worlds.run_scope_key is
    'Preallocated run UUID for a run-scoped world. A deferred constraint requires the matching runs row by commit.';
comment on table run_slot_bindings is
    'Run-specific binding of generated plan nodes to immutable world placement slots.';
comment on table run_progress_states is
    'Generic query projection for progress, convergence, and server-owned rule state; runs.world_state is complete authority.';
comment on table turn_rule_resolutions is
    'Append-only authoritative D20 and Rule Engine resolution for a committed meaningful turn.';
comment on table resume_validation_records is
    'Append-only audit of deep snapshot verification and resume acceptance or rejection.';
comment on column turn_rule_resolutions.rng_audit is
    'Redacted RNG proof metadata only. Never store resolution_seed or other secret material.';
comment on column save_snapshots.resume_metadata is
    'Projection/schema cursors required to rebuild a run; secrets such as resolution_seed are forbidden.';

revoke all on run_generation_plans, run_slot_bindings, run_progress_states,
    turn_rule_resolutions, resume_validation_records from public;
revoke execute on function keyboard_wanderer.validate_run_scoped_world_link() from public;
revoke execute on function keyboard_wanderer.validate_run_world_link() from public;
revoke execute on function keyboard_wanderer.validate_generation_plan_world_scope() from public;
revoke execute on function keyboard_wanderer.prevent_world_scope_reassignment() from public;
revoke execute on function keyboard_wanderer.validate_run_progress_state() from public;
revoke execute on function keyboard_wanderer.validate_turn_rule_resolution() from public;
revoke execute on function keyboard_wanderer.validate_deep_save_snapshot() from public;
revoke execute on function keyboard_wanderer.validate_resume_record() from public;
revoke execute on function keyboard_wanderer.reject_generative_history_mutation() from public;

commit;
