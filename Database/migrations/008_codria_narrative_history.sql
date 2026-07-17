begin;

set local search_path = keyboard_wanderer, public;

alter table npc_relationships
    add constraint npc_relationships_identity_unique unique (id, owner_id, run_id);

create table major_choices (
    id uuid primary key default gen_random_uuid(),
    run_id uuid not null,
    owner_id uuid not null,
    turn_id uuid not null,
    turn_no smallint not null,
    choice_key text not null,
    option_key text not null,
    region_axis_code text references campaign_region_axis_catalog(code),
    action_context text not null,
    immediate_effects jsonb not null default '{}'::jsonb,
    long_term_tags jsonb not null default '[]'::jsonb,
    created_at timestamptz not null default now(),
    constraint major_choices_run_fk
        foreign key (run_id, owner_id) references runs(id, owner_id) on delete cascade,
    constraint major_choices_turn_fk
        foreign key (turn_id, owner_id, run_id)
        references turn_records(id, owner_id, run_id) on delete cascade,
    constraint major_choices_run_key_unique unique (run_id, choice_key),
    constraint major_choices_turn check (turn_no between 1 and 50),
    constraint major_choices_keys check (
        choice_key ~ '^[a-z][a-z0-9_.:-]{2,159}$'
        and option_key ~ '^[a-z][a-z0-9_.:-]{1,159}$'
    ),
    constraint major_choices_context check (
        action_context in ('COMBAT', 'INVESTIGATION', 'NEGOTIATION', 'DEPLOYMENT')
    ),
    constraint major_choices_json check (
        jsonb_typeof(immediate_effects) = 'object'
        and jsonb_typeof(long_term_tags) = 'array'
    )
);

create index major_choices_owner_run_turn_idx on major_choices (owner_id, run_id, turn_no);

create table region_outcomes (
    id uuid primary key default gen_random_uuid(),
    run_id uuid not null,
    owner_id uuid not null,
    turn_id uuid not null,
    turn_no smallint not null,
    region_axis_code text not null references campaign_region_axis_catalog(code),
    sequence_no integer not null,
    outcome_key text not null,
    outcome_status text not null,
    outcome_state jsonb not null default '{}'::jsonb,
    ending_tags jsonb not null default '[]'::jsonb,
    created_at timestamptz not null default now(),
    constraint region_outcomes_run_fk
        foreign key (run_id, owner_id) references runs(id, owner_id) on delete cascade,
    constraint region_outcomes_turn_fk
        foreign key (turn_id, owner_id, run_id)
        references turn_records(id, owner_id, run_id) on delete cascade,
    constraint region_outcomes_sequence_unique unique (run_id, region_axis_code, sequence_no),
    constraint region_outcomes_turn_axis_unique unique (run_id, turn_id, region_axis_code),
    constraint region_outcomes_turn check (turn_no between 1 and 50),
    constraint region_outcomes_sequence check (sequence_no >= 1),
    constraint region_outcomes_key check (outcome_key ~ '^[a-z][a-z0-9_.:-]{2,159}$'),
    constraint region_outcomes_status check (
        outcome_status in ('UNRESOLVED', 'STABILIZED', 'TRANSFORMED', 'PRESERVED', 'DESTABILIZED')
    ),
    constraint region_outcomes_json check (
        jsonb_typeof(outcome_state) = 'object' and jsonb_typeof(ending_tags) = 'array'
    )
);

create index region_outcomes_owner_run_axis_idx
    on region_outcomes (owner_id, run_id, region_axis_code, sequence_no desc);

create table npc_relationship_history (
    id uuid primary key default gen_random_uuid(),
    relationship_id uuid not null,
    run_id uuid not null,
    owner_id uuid not null,
    turn_id uuid not null,
    turn_no smallint not null,
    affinity_delta smallint not null default 0,
    trust_delta smallint not null default 0,
    fear_delta smallint not null default 0,
    affinity_after smallint not null,
    trust_after smallint not null,
    fear_after smallint not null,
    relationship_state_after text not null,
    reason_code text not null,
    context jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    constraint npc_relationship_history_relationship_fk
        foreign key (relationship_id, owner_id, run_id)
        references npc_relationships(id, owner_id, run_id) on delete cascade,
    constraint npc_relationship_history_turn_fk
        foreign key (turn_id, owner_id, run_id)
        references turn_records(id, owner_id, run_id) on delete cascade,
    constraint npc_relationship_history_run_turn_relationship_unique
        unique (run_id, turn_id, relationship_id),
    constraint npc_relationship_history_turn check (turn_no between 1 and 50),
    constraint npc_relationship_history_deltas check (
        affinity_delta between -200 and 200
        and trust_delta between -200 and 200
        and fear_delta between -100 and 100
        and (affinity_delta <> 0 or trust_delta <> 0 or fear_delta <> 0)
    ),
    constraint npc_relationship_history_scores check (
        affinity_after between -100 and 100
        and trust_after between -100 and 100
        and fear_after between 0 and 100
    ),
    constraint npc_relationship_history_text check (
        btrim(relationship_state_after) <> '' and btrim(reason_code) <> ''
    ),
    constraint npc_relationship_history_context check (jsonb_typeof(context) = 'object')
);

create index npc_relationship_history_owner_run_turn_idx
    on npc_relationship_history (owner_id, run_id, turn_no desc);

commit;
