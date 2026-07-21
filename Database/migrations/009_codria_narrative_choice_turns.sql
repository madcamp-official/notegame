begin;

set local search_path = keyboard_wanderer, public;

-- Pure dialogue and attitude choices are authoritative campaign turns, but they
-- do not masquerade as keyboard skill usage. Keep the existing USE_SKILL branch
-- unchanged and add a deliberately narrower NARRATIVE_CHOICE branch.
alter table turn_records
    drop constraint turn_records_v4_command_shape;

alter table turn_records
    add constraint turn_records_v4_command_shape check (
        (
            command_schema_version is null and input_type is null and skill_id is null
            and target_ids is null and action_context is null and turn_context is null
            and campaign_turn_before is null and campaign_turn_after is null
            and campaign_turn_consumed is null
        )
        or (
            command_schema_version = 'codria-action.v4'
            and (
                (
                    input_type = 'USE_SKILL'
                    and skill_id in (
                        'COPY', 'DELETE', 'CONNECT', 'RESTORE', 'UNDO',
                        'SEARCH', 'SELECT_ALL'
                    )
                    and jsonb_typeof(target_ids) = 'array'
                    and action_context in ('COMBAT', 'INVESTIGATION', 'NEGOTIATION', 'DEPLOYMENT')
                )
                or (
                    input_type = 'NARRATIVE_CHOICE'
                    and skill_id is null
                    and target_ids = '[]'::jsonb
                    and action_context = 'NARRATIVE'
                )
            )
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

-- SEARCH and SELECT_ALL are first-class keyboard skills in the runtime and may
-- be issued only through the same sealed SKILL-choice path as the original five.
alter table ability_usage_history
    drop constraint ability_usage_history_skill;

alter table ability_usage_history
    add constraint ability_usage_history_skill check (
        skill_id in (
            'COPY', 'DELETE', 'CONNECT', 'RESTORE', 'UNDO',
            'SEARCH', 'SELECT_ALL'
        )
    );

alter table major_choices
    drop constraint major_choices_context;

alter table major_choices
    add constraint major_choices_context check (
        action_context in ('COMBAT', 'INVESTIGATION', 'NEGOTIATION', 'DEPLOYMENT', 'NARRATIVE')
    );

-- Generic narrative ledgers may be caused by either a skill turn or a pure
-- narrative choice. Callers that normalize a concrete ability use the stricter
-- helper below instead of relying on nullable skill comparisons.
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
       or stored_input_type not in ('USE_SKILL', 'NARRATIVE_CHOICE') then
        raise exception using
            errcode = '23514',
            message = 'Codria history rows require a matching committed v4 campaign action';
    end if;
end
$$;

create or replace function keyboard_wanderer.assert_committed_v4_skill_action(
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
    stored_input_type text;
begin
    perform keyboard_wanderer.assert_committed_v4_action(
        checked_turn_id, checked_run_id, checked_owner_id, checked_turn_no
    );

    select input_type
      into strict stored_input_type
      from keyboard_wanderer.turn_records
     where id = checked_turn_id
       and run_id = checked_run_id
       and owner_id = checked_owner_id;

    if stored_input_type <> 'USE_SKILL' then
        raise exception using
            errcode = '23514',
            message = 'Codria ability history requires a matching committed v4 USE_SKILL action';
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
    perform keyboard_wanderer.assert_committed_v4_skill_action(
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

create or replace function keyboard_wanderer.validate_ability_usage_history()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
declare
    turn_skill text;
    turn_action_context text;
    turn_targets jsonb;
begin
    perform keyboard_wanderer.assert_committed_v4_skill_action(
        new.turn_id, new.run_id, new.owner_id, new.turn_no
    );

    select skill_id, action_context, target_ids
      into strict turn_skill, turn_action_context, turn_targets
      from keyboard_wanderer.turn_records
     where id = new.turn_id and run_id = new.run_id and owner_id = new.owner_id;

    if turn_skill <> new.skill_id
       or turn_action_context <> new.action_context
       or turn_targets <> new.target_ids then
        raise exception using
            errcode = '23514',
            message = 'ability history must reproduce the authoritative structured action';
    end if;
    return new;
end
$$;

create or replace function keyboard_wanderer.validate_turn_rule_resolution()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
begin
    perform keyboard_wanderer.assert_committed_v4_skill_action(
        new.turn_record_id, new.run_id, new.owner_id, new.turn_no
    );
    return new;
end
$$;

create or replace function keyboard_wanderer.enforce_technical_debt_entry()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
declare
    turn_skill text;
    resolving_turn_no smallint;
begin
    if tg_op = 'DELETE' then
        if pg_trigger_depth() > 1 then
            return old;
        end if;
        raise exception using errcode = '55000', message = 'technical debt entries cannot be deleted directly';
    end if;

    if tg_op = 'INSERT' then
        perform keyboard_wanderer.assert_committed_v4_skill_action(
            new.turn_id, new.run_id, new.owner_id, new.turn_no
        );
        select skill_id
          into strict turn_skill
          from keyboard_wanderer.turn_records
         where id = new.turn_id and run_id = new.run_id and owner_id = new.owner_id;
        if turn_skill <> new.skill_id then
            raise exception using errcode = '23514', message = 'technical debt skill must match its action';
        end if;
        if new.resolved_at is not null then
            raise exception using errcode = '23514', message = 'technical debt entries cannot start resolved';
        end if;
        return new;
    end if;

    if new.id is distinct from old.id
       or new.run_id is distinct from old.run_id
       or new.owner_id is distinct from old.owner_id
       or new.turn_id is distinct from old.turn_id
       or new.turn_no is distinct from old.turn_no
       or new.skill_id is distinct from old.skill_id
       or new.operation_type is distinct from old.operation_type
       or new.target_id is distinct from old.target_id
       or new.forced_override is distinct from old.forced_override
       or new.debt_delta is distinct from old.debt_delta
       or new.deferred_consequence_type is distinct from old.deferred_consequence_type
       or new.metadata is distinct from old.metadata
       or new.created_at is distinct from old.created_at then
        raise exception using errcode = '55000', message = 'technical debt cause and delta are immutable';
    end if;
    if old.resolved_at is not null or new.resolved_at is null then
        raise exception using errcode = '55000', message = 'technical debt consequence may be resolved exactly once';
    end if;

    select turn_no
      into strict resolving_turn_no
      from keyboard_wanderer.turn_records
     where id = new.resolved_by_turn_id and run_id = new.run_id and owner_id = new.owner_id;
    perform keyboard_wanderer.assert_committed_v4_action(
        new.resolved_by_turn_id, new.run_id, new.owner_id, resolving_turn_no
    );
    if resolving_turn_no < new.turn_no then
        raise exception using errcode = '23514', message = 'technical debt cannot resolve before it is created';
    end if;
    return new;
end
$$;

comment on column turn_records.input_type is
    'Committed v4 campaign action kind: USE_SKILL or pure NARRATIVE_CHOICE; MOVE remains in safe_travels.';
comment on column major_choices.action_context is
    'Authoritative context of the recorded choice; NARRATIVE is reserved for pure dialogue or attitude turns.';
comment on view structured_action_history is
    'Unified read model over MOVE safe travel and committed USE_SKILL or NARRATIVE_CHOICE campaign turns.';
comment on table turn_rule_resolutions is
    'Append-only deterministic D20 resolution for committed USE_SKILL turns; pure NARRATIVE_CHOICE turns intentionally have no row.';

revoke execute on function keyboard_wanderer.assert_committed_v4_action(uuid, uuid, uuid, smallint) from public;
revoke execute on function keyboard_wanderer.assert_committed_v4_skill_action(uuid, uuid, uuid, smallint) from public;
revoke execute on function keyboard_wanderer.validate_admin_access_acquisition() from public;
revoke execute on function keyboard_wanderer.validate_ability_usage_history() from public;
revoke execute on function keyboard_wanderer.validate_turn_rule_resolution() from public;
revoke execute on function keyboard_wanderer.enforce_technical_debt_entry() from public;

commit;
