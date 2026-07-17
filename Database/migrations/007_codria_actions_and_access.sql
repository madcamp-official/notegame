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

commit;
