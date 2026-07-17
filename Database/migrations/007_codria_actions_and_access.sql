begin;

set local search_path = keyboard_wanderer, public;

-- v1.1 TURN_ACTIONS is preserved as structured columns on the two existing
-- idempotency ledgers. MOVE remains safe_travels; USE_SKILL remains turn_records.
alter table safe_travels
    add column command_schema_version text,
    add column input_type text,
    add column world_id uuid,
    add column destination_area_id uuid,
    add column turn_context jsonb,
    add constraint safe_travels_structured_run_fk
        foreign key (run_id, owner_id, world_id)
        references runs(id, owner_id, world_id) on delete cascade,
    add constraint safe_travels_destination_area_fk
        foreign key (destination_area_id, owner_id, world_id)
        references areas(id, owner_id, world_id) on delete restrict,
    add constraint safe_travels_v4_command_shape check (
        (
            command_schema_version is null and input_type is null
            and world_id is null and destination_area_id is null and turn_context is null
        )
        or (
            command_schema_version = 'codria-action.v4'
            and input_type = 'MOVE'
            and world_id is not null and destination_area_id is not null
            and jsonb_typeof(turn_context) = 'object'
            and campaign_turn_consumed = false
            and campaign_turn_before = campaign_turn_after
        )
    );

alter table turn_records
    add column command_schema_version text,
    add column input_type text,
    add column skill_id text references ability_catalog(code),
    add column target_ids jsonb,
    add column action_context text,
    add column turn_context jsonb,
    add column campaign_turn_before smallint,
    add column campaign_turn_after smallint,
    add column campaign_turn_consumed boolean,
    add constraint turn_records_v4_command_shape check (
        (
            command_schema_version is null and input_type is null and skill_id is null
            and target_ids is null and action_context is null and turn_context is null
            and campaign_turn_before is null and campaign_turn_after is null
            and campaign_turn_consumed is null
        )
        or (
            command_schema_version = 'codria-action.v4'
            and input_type = 'USE_SKILL'
            and skill_id in ('COPY', 'DELETE', 'CONNECT', 'RESTORE', 'UNDO')
            and jsonb_typeof(target_ids) = 'array'
            and action_context in ('COMBAT', 'INVESTIGATION', 'NEGOTIATION', 'DEPLOYMENT')
            and jsonb_typeof(turn_context) = 'object'
            and campaign_turn_before between 0 and 49
            and (
                (
                    status in ('pending', 'rejected')
                    and campaign_turn_consumed = false
                    and campaign_turn_after = campaign_turn_before
                )
                or (
                    status = 'committed'
                    and campaign_turn_consumed = true
                    and campaign_turn_after = campaign_turn_before + 1
                    and turn_no = campaign_turn_after
                )
            )
        )
    );

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
           or new.command_schema_version is distinct from old.command_schema_version
           or new.input_type is distinct from old.input_type
           or new.skill_id is distinct from old.skill_id
           or new.target_ids is distinct from old.target_ids
           or new.action_context is distinct from old.action_context
           or new.turn_context is distinct from old.turn_context
           or new.campaign_turn_before is distinct from old.campaign_turn_before
           or new.received_at is distinct from old.received_at
           or new.created_at is distinct from old.created_at then
            raise exception using errcode = '55000', message = 'turn request identity and payload are immutable';
        end if;
        if old.status <> 'pending' then
            raise exception using
                errcode = '55000',
                message = 'terminal turn records are immutable after the authoritative commit';
        end if;
        if new.status not in ('pending', 'committed', 'rejected') then
            raise exception using errcode = '23514', message = 'invalid turn record transition';
        end if;
    end if;

    if new.status = 'committed' then
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

create view structured_action_history
with (security_invoker = true, security_barrier = true)
as
select
    id as action_id,
    run_id,
    owner_id,
    command_schema_version,
    input_type,
    idempotency_key,
    request_fingerprint,
    'committed'::text as status,
    null::text as skill_id,
    '[]'::jsonb as target_ids,
    jsonb_build_object('areaId', destination_area_id, 'x', requested_x, 'y', requested_y) as destination,
    null::text as action_context,
    turn_context,
    expected_run_version,
    committed_run_version,
    campaign_turn_before,
    campaign_turn_after,
    campaign_turn_consumed,
    created_at,
    created_at as completed_at
from safe_travels
where command_schema_version = 'codria-action.v4'
union all
select
    id,
    run_id,
    owner_id,
    command_schema_version,
    input_type,
    idempotency_key,
    request_fingerprint,
    status,
    skill_id,
    target_ids,
    null::jsonb,
    action_context,
    turn_context,
    expected_run_version,
    committed_run_version,
    campaign_turn_before,
    campaign_turn_after,
    campaign_turn_consumed,
    created_at,
    completed_at
from turn_records
where command_schema_version = 'codria-action.v4';

create table admin_access_acquisition_history (
    id uuid primary key default gen_random_uuid(),
    run_id uuid not null,
    owner_id uuid not null,
    world_id uuid not null,
    turn_id uuid not null,
    turn_no smallint not null,
    admin_access_code text not null references admin_access_level_catalog(code),
    region_axis_code text not null references campaign_region_axis_catalog(code),
    area_id uuid not null,
    action_context text not null,
    acquisition_method text not null,
    skill_id text not null references ability_catalog(code),
    evidence jsonb not null default '{}'::jsonb,
    acquired_at timestamptz not null default now(),
    constraint admin_access_acquisition_history_run_fk
        foreign key (run_id, owner_id, world_id)
        references runs(id, owner_id, world_id) on delete cascade,
    constraint admin_access_acquisition_history_turn_fk
        foreign key (turn_id, owner_id, run_id)
        references turn_records(id, owner_id, run_id) on delete cascade,
    constraint admin_access_acquisition_history_axis_fk
        foreign key (world_id, owner_id, region_axis_code)
        references world_region_axis_bindings(world_id, owner_id, region_axis_code) on delete cascade,
    constraint admin_access_acquisition_history_area_fk
        foreign key (area_id, owner_id, world_id)
        references areas(id, owner_id, world_id) on delete restrict,
    constraint admin_access_acquisition_history_run_level_unique
        unique (run_id, admin_access_code),
    constraint admin_access_acquisition_history_run_turn_unique unique (run_id, turn_id),
    constraint admin_access_acquisition_history_turn check (turn_no between 1 and 50),
    constraint admin_access_acquisition_history_context check (
        action_context in ('COMBAT', 'INVESTIGATION', 'NEGOTIATION', 'DEPLOYMENT')
    ),
    constraint admin_access_acquisition_history_method check (btrim(acquisition_method) <> ''),
    constraint admin_access_acquisition_history_evidence check (jsonb_typeof(evidence) = 'object')
);

create index admin_access_acquisition_history_owner_run_idx
    on admin_access_acquisition_history (owner_id, run_id, turn_no);

create or replace function keyboard_wanderer.assert_committed_v4_action(
    checked_turn_id uuid,
    checked_run_id uuid,
    checked_owner_id uuid,
    checked_turn_no smallint
)
returns void
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
declare
    stored_status text;
    stored_turn_no smallint;
    stored_schema text;
    stored_input_type text;
begin
    select status, turn_no, command_schema_version, input_type
      into strict stored_status, stored_turn_no, stored_schema, stored_input_type
      from keyboard_wanderer.turn_records
     where id = checked_turn_id
       and run_id = checked_run_id
       and owner_id = checked_owner_id;

    if stored_status <> 'committed'
       or stored_turn_no is distinct from checked_turn_no
       or stored_schema <> 'codria-action.v4'
       or stored_input_type <> 'USE_SKILL' then
        raise exception using
            errcode = '23514',
            message = 'Codria history rows require the matching committed v4 USE_SKILL action';
    end if;
end
$$;

create or replace function keyboard_wanderer.validate_admin_access_acquisition()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
declare
    acquired_level smallint;
    prior_level_count integer;
    latest_prior_turn smallint;
    turn_skill text;
    turn_context text;
begin
    perform keyboard_wanderer.assert_committed_v4_action(
        new.turn_id, new.run_id, new.owner_id, new.turn_no
    );

    select access_level
      into strict acquired_level
      from keyboard_wanderer.admin_access_level_catalog
     where code = new.admin_access_code;

    select count(*), max(h.turn_no)
      into prior_level_count, latest_prior_turn
      from keyboard_wanderer.admin_access_acquisition_history h
      join keyboard_wanderer.admin_access_level_catalog c
        on c.code = h.admin_access_code
     where h.run_id = new.run_id and h.owner_id = new.owner_id
       and c.access_level < acquired_level;

    if prior_level_count <> acquired_level - 1
       or (latest_prior_turn is not null and latest_prior_turn >= new.turn_no) then
        raise exception using
            errcode = '23514',
            message = 'administrator access levels must be acquired exactly once and in chronological order';
    end if;

    select skill_id, action_context
      into strict turn_skill, turn_context
      from keyboard_wanderer.turn_records
     where id = new.turn_id and run_id = new.run_id and owner_id = new.owner_id;

    if turn_skill <> new.skill_id or turn_context <> new.action_context then
        raise exception using
            errcode = '23514',
            message = 'administrator access evidence must match its authoritative skill action';
    end if;
    return new;
end
$$;

commit;
