begin;

set local search_path = keyboard_wanderer, public;

-- A sealed placement slot may reserve several mutually exclusive dormant
-- candidates.  Runtime occupancy remains exclusive: only one binding may be
-- active (or fulfilled) in a slot at a time.
drop index if exists run_slot_bindings_live_slot_unique;
create unique index run_slot_bindings_live_slot_unique
    on run_slot_bindings (run_id, slot_id)
    where status in ('active', 'fulfilled');

comment on index run_slot_bindings_live_slot_unique is
    'One runtime occupant per run slot; multiple dormant reservations are allowed.';

-- turn_limit is a pacing target, not a hard stop.  Turn-bearing ledgers retain
-- non-negative ordering constraints without rejecting a valid soft-horizon
-- continuation.  smallint columns still provide a finite storage bound.
alter table runs
    drop constraint runs_turns,
    add constraint runs_turns check (
        turn_limit between 30 and 50 and current_turn >= 0
    );

alter table turn_records
    drop constraint turn_records_v4_command_shape,
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
            and campaign_turn_before >= 0
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
    authoritative_version bigint;
    authoritative_turn integer;
begin
    select version, current_turn
      into strict authoritative_version, authoritative_turn
      from keyboard_wanderer.runs
     where id = new.run_id and owner_id = new.owner_id;

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

alter table run_slot_bindings
    drop constraint run_slot_bindings_turns,
    add constraint run_slot_bindings_turns check (
        (activation_turn is null or activation_turn >= 0)
        and (released_turn is null or released_turn >= 0)
        and (released_turn is null or activation_turn is null or released_turn >= activation_turn)
    );

alter table run_progress_states
    drop constraint run_progress_states_turn,
    add constraint run_progress_states_turn check (last_turn_no >= 0);

alter table turn_rule_resolutions
    drop constraint turn_rule_resolutions_turn,
    add constraint turn_rule_resolutions_turn check (turn_no >= 1);

alter table admin_access_acquisition_history
    drop constraint admin_access_acquisition_history_turn,
    add constraint admin_access_acquisition_history_turn check (turn_no >= 1);

alter table major_choices
    drop constraint major_choices_turn,
    add constraint major_choices_turn check (turn_no >= 1);

alter table region_outcomes
    drop constraint region_outcomes_turn,
    add constraint region_outcomes_turn check (turn_no >= 1);

alter table npc_relationship_history
    drop constraint npc_relationship_history_turn,
    add constraint npc_relationship_history_turn check (turn_no >= 1);

alter table ability_usage_history
    drop constraint ability_usage_history_turn,
    add constraint ability_usage_history_turn check (turn_no >= 1);

alter table unresolved_hooks
    drop constraint unresolved_hooks_turns,
    add constraint unresolved_hooks_turns check (
        introduced_turn_no >= 0
        and (
            (introduced_turn_no = 0 and introduced_turn_id is null)
            or (introduced_turn_no > 0 and introduced_turn_id is not null)
        )
        and (deadline_turn is null or deadline_turn >= introduced_turn_no)
        and (resolution_turn_no is null or resolution_turn_no >= introduced_turn_no)
    );

alter table technical_debt_entries
    drop constraint technical_debt_entries_turn,
    add constraint technical_debt_entries_turn check (turn_no >= 1);

commit;
