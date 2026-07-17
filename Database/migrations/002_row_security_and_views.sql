begin;

set local search_path = keyboard_wanderer, public;

alter table profiles enable row level security;
alter table profiles force row level security;

create policy profiles_owner_all on profiles
for all
using (id = (select keyboard_wanderer.current_app_user_id()))
with check (id = (select keyboard_wanderer.current_app_user_id()));

do $$
declare
    table_name text;
begin
    -- Mutable owner-scoped state. Direct owner_id columns keep policy checks cheap
    -- and make tenant ownership explicit on every foreign-key path.
    foreach table_name in array array[
        'campaigns', 'worlds', 'regions', 'areas', 'area_connections', 'runs',
        'entities', 'actors', 'entity_positions', 'inventories', 'items',
        'world_facts', 'rumors', 'rumor_knowledge', 'npc_memories',
        'npc_relationships', 'quests', 'quest_objectives', 'save_slots'
    ]
    loop
        execute format('alter table keyboard_wanderer.%I enable row level security', table_name);
        execute format('alter table keyboard_wanderer.%I force row level security', table_name);
        execute format(
            'create policy %I on keyboard_wanderer.%I for all using (owner_id = (select keyboard_wanderer.current_app_user_id())) with check (owner_id = (select keyboard_wanderer.current_app_user_id()))',
            table_name || '_owner_all',
            table_name
        );
    end loop;

    -- The idempotency ledger is insert/update/read, but never directly deleted.
    -- Account/campaign/run deletion can still cascade it under an authorized parent delete.
    table_name := 'turn_records';
    execute format('alter table keyboard_wanderer.%I enable row level security', table_name);
    execute format('alter table keyboard_wanderer.%I force row level security', table_name);
    execute format(
        'create policy %I on keyboard_wanderer.%I for select using (owner_id = (select keyboard_wanderer.current_app_user_id()))',
        table_name || '_owner_select', table_name
    );
    execute format(
        'create policy %I on keyboard_wanderer.%I for insert with check (owner_id = (select keyboard_wanderer.current_app_user_id()))',
        table_name || '_owner_insert', table_name
    );
    execute format(
        'create policy %I on keyboard_wanderer.%I for update using (owner_id = (select keyboard_wanderer.current_app_user_id())) with check (owner_id = (select keyboard_wanderer.current_app_user_id()))',
        table_name || '_owner_update', table_name
    );

    -- Append-only event, audit, snapshot, and model-observability rows.
    foreach table_name in array array[
        'turn_events', 'turn_logs', 'world_events', 'save_snapshots', 'llm_logs'
    ]
    loop
        execute format('alter table keyboard_wanderer.%I enable row level security', table_name);
        execute format('alter table keyboard_wanderer.%I force row level security', table_name);
        execute format(
            'create policy %I on keyboard_wanderer.%I for select using (owner_id = (select keyboard_wanderer.current_app_user_id()))',
            table_name || '_owner_select', table_name
        );
        execute format(
            'create policy %I on keyboard_wanderer.%I for insert with check (owner_id = (select keyboard_wanderer.current_app_user_id()))',
            table_name || '_owner_insert', table_name
        );
    end loop;
end
$$;

create view run_summaries
with (security_invoker = true, security_barrier = true)
as
select
    id,
    campaign_id,
    world_id,
    owner_id,
    status,
    version,
    current_turn,
    turn_limit,
    focus,
    pressure,
    active_area_id,
    player_entity_id,
    world_state,
    ending_code,
    started_at,
    completed_at,
    created_at,
    updated_at
from runs;

comment on view run_summaries is
    'Client-safe run projection. Intentionally excludes the server-only resolution_seed.';

create view turn_requests
with (security_invoker = true, security_barrier = true)
as
select
    id,
    run_id,
    owner_id,
    idempotency_key,
    request_fingerprint,
    expected_run_version,
    status,
    request_json,
    received_at,
    created_at,
    updated_at
from turn_records;

create view turn_results
with (security_invoker = true, security_barrier = true)
as
select
    id,
    run_id,
    owner_id,
    turn_no,
    expected_run_version,
    committed_run_version,
    status,
    result_json,
    narrative_json,
    fallback_used,
    model,
    error_code,
    completed_at,
    created_at
from turn_records
where status in ('committed', 'rejected');

comment on view turn_requests is
    'Read projection of immutable request fields in turn_records.';
comment on view turn_results is
    'Read projection of terminal result fields in turn_records.';

revoke all on run_summaries, turn_requests, turn_results from public;

commit;
