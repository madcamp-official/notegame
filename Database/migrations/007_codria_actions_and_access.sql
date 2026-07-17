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

commit;
