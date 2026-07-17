begin;

set local search_path = keyboard_wanderer, public;

create table campaign_template_catalog (
    code text primary key,
    version text not null,
    display_name text not null,
    is_enabled boolean not null default true,
    metadata jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint campaign_template_code_format check (code ~ '^[a-z][a-z0-9-]{2,63}$'),
    constraint campaign_template_version_not_blank check (btrim(version) <> ''),
    constraint campaign_template_display_name_not_blank check (btrim(display_name) <> ''),
    constraint campaign_template_metadata_object check (jsonb_typeof(metadata) = 'object')
);

create table campaign_story_beats (
    id uuid primary key default gen_random_uuid(),
    campaign_id uuid not null,
    owner_id uuid not null,
    beat_key text not null,
    title text not null,
    description text not null,
    required_ability text not null,
    target_turn smallint not null,
    display_order smallint not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint campaign_story_beats_campaign_fk
        foreign key (campaign_id, owner_id) references campaigns(id, owner_id) on delete cascade,
    constraint campaign_story_beats_campaign_key_unique unique (campaign_id, beat_key),
    constraint campaign_story_beats_campaign_order_unique unique (campaign_id, display_order),
    constraint campaign_story_beats_key_format check (beat_key ~ '^[a-z][a-z0-9-]{2,63}$'),
    constraint campaign_story_beats_text check (btrim(title) <> '' and btrim(description) <> ''),
    constraint campaign_story_beats_turn check (target_turn between 0 and 50),
    constraint campaign_story_beats_order check (display_order between 0 and 31)
);

create index campaign_story_beats_owner_campaign_idx on campaign_story_beats (owner_id, campaign_id, display_order);

create table placement_slots (
    id uuid primary key default gen_random_uuid(),
    slot_key text not null,
    owner_id uuid not null,
    world_id uuid not null,
    area_id uuid not null,
    slot_kind text not null,
    x integer not null,
    y integer not null,
    tags jsonb not null default '[]'::jsonb,
    allowed_asset_ids jsonb not null default '[]'::jsonb,
    created_at timestamptz not null default now(),
    constraint placement_slots_world_fk
        foreign key (world_id, owner_id) references worlds(id, owner_id) on delete cascade,
    constraint placement_slots_area_fk
        foreign key (area_id, owner_id, world_id) references areas(id, owner_id, world_id) on delete cascade,
    constraint placement_slots_world_key_unique unique (world_id, slot_key),
    constraint placement_slots_id_owner_world_unique unique (id, owner_id, world_id),
    constraint placement_slots_key_format check (slot_key ~ '^slot\.[a-f0-9]{20}$'),
    constraint placement_slots_kind check (slot_kind in ('npc', 'enemy', 'prop', 'quest', 'loot')),
    constraint placement_slots_tags_array check (jsonb_typeof(tags) = 'array'),
    constraint placement_slots_assets_array check (jsonb_typeof(allowed_asset_ids) = 'array' and jsonb_array_length(allowed_asset_ids) > 0)
);

create index placement_slots_owner_world_area_idx on placement_slots (owner_id, world_id, area_id, slot_kind);

create table run_director_states (
    run_id uuid primary key,
    owner_id uuid not null,
    current_act text not null,
    current_story_beat jsonb not null,
    required_story_beats jsonb not null default '[]'::jsonb,
    ending_candidates jsonb not null default '[]'::jsonb,
    canonical_facts jsonb not null default '[]'::jsonb,
    open_loops jsonb not null default '[]'::jsonb,
    rumors jsonb not null default '[]'::jsonb,
    npc_memories jsonb not null default '[]'::jsonb,
    npc_relationships jsonb not null default '[]'::jsonb,
    active_quests jsonb not null default '[]'::jsonb,
    connections jsonb not null default '[]'::jsonb,
    slot_enrichments jsonb not null default '[]'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint run_director_states_run_fk
        foreign key (run_id, owner_id) references runs(id, owner_id) on delete cascade,
    constraint run_director_states_act check (current_act in ('introduction', 'exploration', 'pressure', 'convergence', 'ending')),
    constraint run_director_states_beat_object check (jsonb_typeof(current_story_beat) = 'object'),
    constraint run_director_states_arrays check (
        jsonb_typeof(required_story_beats) = 'array'
        and jsonb_typeof(ending_candidates) = 'array'
        and jsonb_typeof(canonical_facts) = 'array'
        and jsonb_typeof(open_loops) = 'array'
        and jsonb_typeof(rumors) = 'array'
        and jsonb_typeof(npc_memories) = 'array'
        and jsonb_typeof(npc_relationships) = 'array'
        and jsonb_typeof(active_quests) = 'array'
        and jsonb_typeof(connections) = 'array'
        and jsonb_typeof(slot_enrichments) = 'array'
    )
);

create index run_director_states_owner_act_idx on run_director_states (owner_id, current_act, updated_at desc);
create index run_director_states_facts_gin on run_director_states using gin (canonical_facts jsonb_path_ops);
create index run_director_states_loops_gin on run_director_states using gin (open_loops jsonb_path_ops);

create table reversible_actions (
    id uuid primary key default gen_random_uuid(),
    run_id uuid not null,
    owner_id uuid not null,
    turn_no smallint not null,
    ability text not null,
    inverse_ops jsonb not null,
    consumed boolean not null default false,
    consumed_turn smallint,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint reversible_actions_run_fk
        foreign key (run_id, owner_id) references runs(id, owner_id) on delete cascade,
    constraint reversible_actions_run_turn_unique unique (run_id, turn_no),
    constraint reversible_actions_turn check (turn_no >= 1 and (consumed_turn is null or consumed_turn > turn_no)),
    constraint reversible_actions_consumed_shape check (consumed = (consumed_turn is not null)),
    constraint reversible_actions_inverse_array check (jsonb_typeof(inverse_ops) = 'array' and jsonb_array_length(inverse_ops) > 0)
);

create index reversible_actions_owner_run_idx on reversible_actions (owner_id, run_id, turn_no desc);

create or replace function keyboard_wanderer.reject_placement_slot_mutation()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
begin
    -- Parent campaign/profile deletion is the reviewed lifecycle path and must
    -- be allowed to cascade. Direct slot mutation remains forbidden.
    if pg_trigger_depth() > 1 then
        return old;
    end if;
    raise exception using errcode = '55000', message = 'placement slots are immutable after world generation';
end
$$;

create trigger placement_slots_immutable
before update or delete on placement_slots
for each row execute function keyboard_wanderer.reject_placement_slot_mutation();

do $$
declare
    table_name text;
begin
    foreach table_name in array array['campaign_story_beats', 'placement_slots', 'run_director_states', 'reversible_actions']
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

revoke all on campaign_template_catalog, campaign_story_beats, placement_slots, run_director_states, reversible_actions from public;

commit;
