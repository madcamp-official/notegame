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

create table ability_usage_history (
    id uuid primary key default gen_random_uuid(),
    run_id uuid not null,
    owner_id uuid not null,
    turn_id uuid not null,
    turn_no smallint not null,
    skill_id text not null references ability_catalog(code),
    action_context text not null,
    target_ids jsonb not null,
    outcome text not null,
    effects_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    constraint ability_usage_history_run_fk
        foreign key (run_id, owner_id) references runs(id, owner_id) on delete cascade,
    constraint ability_usage_history_turn_fk
        foreign key (turn_id, owner_id, run_id)
        references turn_records(id, owner_id, run_id) on delete cascade,
    constraint ability_usage_history_run_turn_unique unique (run_id, turn_id),
    constraint ability_usage_history_turn check (turn_no between 1 and 50),
    constraint ability_usage_history_context check (
        action_context in ('COMBAT', 'INVESTIGATION', 'NEGOTIATION', 'DEPLOYMENT')
    ),
    constraint ability_usage_history_skill check (
        skill_id in ('COPY', 'DELETE', 'CONNECT', 'RESTORE', 'UNDO')
    ),
    constraint ability_usage_history_targets check (jsonb_typeof(target_ids) = 'array'),
    constraint ability_usage_history_outcome check (
        outcome in ('critical_failure', 'failure', 'partial_success', 'success', 'critical_success')
    ),
    constraint ability_usage_history_effects check (jsonb_typeof(effects_json) = 'object')
);

create index ability_usage_history_owner_run_skill_idx
    on ability_usage_history (owner_id, run_id, skill_id, turn_no);

create table unresolved_hooks (
    id uuid primary key default gen_random_uuid(),
    run_id uuid not null,
    owner_id uuid not null,
    hook_key text not null,
    region_axis_code text references campaign_region_axis_catalog(code),
    npc_actor_id uuid,
    introduced_turn_id uuid,
    introduced_turn_no smallint not null,
    summary text not null,
    hook_payload jsonb not null default '{}'::jsonb,
    status text not null default 'OPEN',
    deadline_turn smallint,
    resolution_turn_id uuid,
    resolution_turn_no smallint,
    resolution_kind text,
    resolved_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint unresolved_hooks_run_fk
        foreign key (run_id, owner_id) references runs(id, owner_id) on delete cascade,
    constraint unresolved_hooks_npc_fk
        foreign key (npc_actor_id, owner_id, run_id)
        references actors(entity_id, owner_id, run_id) on delete set null (npc_actor_id),
    constraint unresolved_hooks_introduced_turn_fk
        foreign key (introduced_turn_id, owner_id, run_id)
        references turn_records(id, owner_id, run_id) on delete cascade,
    constraint unresolved_hooks_resolution_turn_fk
        foreign key (resolution_turn_id, owner_id, run_id)
        references turn_records(id, owner_id, run_id) on delete restrict,
    constraint unresolved_hooks_run_key_unique unique (run_id, hook_key),
    constraint unresolved_hooks_key check (hook_key ~ '^[a-z][a-z0-9_.:-]{2,159}$'),
    constraint unresolved_hooks_turns check (
        introduced_turn_no between 0 and 50
        and (
            (introduced_turn_no = 0 and introduced_turn_id is null)
            or (introduced_turn_no > 0 and introduced_turn_id is not null)
        )
        and (deadline_turn is null or deadline_turn between introduced_turn_no and 50)
        and (resolution_turn_no is null or resolution_turn_no between introduced_turn_no and 50)
    ),
    constraint unresolved_hooks_summary check (btrim(summary) <> ''),
    constraint unresolved_hooks_payload check (jsonb_typeof(hook_payload) = 'object'),
    constraint unresolved_hooks_status check (status in ('OPEN', 'RESOLVED', 'EXPIRED')),
    constraint unresolved_hooks_resolution_shape check (
        (
            status = 'OPEN' and resolution_turn_id is null and resolution_turn_no is null
            and resolution_kind is null and resolved_at is null
        )
        or (
            status in ('RESOLVED', 'EXPIRED') and resolution_turn_id is not null
            and resolution_turn_no is not null and resolution_kind is not null
            and btrim(resolution_kind) <> '' and resolved_at is not null
        )
    )
);

create index unresolved_hooks_owner_open_idx
    on unresolved_hooks (owner_id, run_id, deadline_turn) where status = 'OPEN';

commit;
