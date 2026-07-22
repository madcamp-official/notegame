begin;

set local search_path = keyboard_wanderer, public;

-- Free-form player messages are normalized into the same authoritative
-- USE_SKILL command envelope as the seven keyboard abilities.  Keep the
-- database contract aligned with Server/src/domain/turn-engine.js so those
-- committed actions cannot succeed in memory and then fail only in PostgreSQL.
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
                        'SEARCH', 'SELECT_ALL', 'INTERACT', 'ATTACK', 'MOVE',
                        'NEGOTIATE', 'REST', 'USE_ITEM', 'COMBINE'
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

alter table ability_usage_history
    drop constraint ability_usage_history_skill,
    add constraint ability_usage_history_skill check (
        skill_id in (
            'COPY', 'DELETE', 'CONNECT', 'RESTORE', 'UNDO',
            'SEARCH', 'SELECT_ALL', 'INTERACT', 'ATTACK', 'MOVE',
            'NEGOTIATE', 'REST', 'USE_ITEM', 'COMBINE'
        )
    );

comment on constraint turn_records_v4_command_shape on turn_records is
    'Authoritative keyboard, free-form player-action, and narrative-choice command envelope.';

comment on constraint ability_usage_history_skill on ability_usage_history is
    'All server-normalized consuming skills, including free-form player actions.';

commit;
