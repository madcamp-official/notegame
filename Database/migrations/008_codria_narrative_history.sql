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

create table technical_debt_entries (
    id uuid primary key default gen_random_uuid(),
    run_id uuid not null,
    owner_id uuid not null,
    turn_id uuid not null,
    turn_no smallint not null,
    skill_id text not null references ability_catalog(code),
    operation_type text not null,
    target_id text not null,
    forced_override boolean not null default false,
    debt_delta integer not null,
    deferred_consequence_type text not null,
    resolved_at timestamptz,
    resolved_by_turn_id uuid,
    resolution_type text,
    metadata jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    constraint technical_debt_entries_run_fk
        foreign key (run_id, owner_id) references runs(id, owner_id) on delete cascade,
    constraint technical_debt_entries_turn_fk
        foreign key (turn_id, owner_id, run_id)
        references turn_records(id, owner_id, run_id) on delete cascade,
    constraint technical_debt_entries_resolution_turn_fk
        foreign key (resolved_by_turn_id, owner_id, run_id)
        references turn_records(id, owner_id, run_id) on delete restrict,
    constraint technical_debt_entries_dedupe_unique
        unique (run_id, turn_id, operation_type, target_id, deferred_consequence_type),
    constraint technical_debt_entries_turn check (turn_no between 1 and 50),
    constraint technical_debt_entries_operation check (
        operation_type in ('COPY', 'DELETE', 'CONNECT', 'RESTORE', 'UNDO')
        and skill_id = operation_type
    ),
    constraint technical_debt_entries_target check (btrim(target_id) <> ''),
    constraint technical_debt_entries_delta check (
        debt_delta between -100 and 100 and debt_delta <> 0
        and (not forced_override or debt_delta > 0)
    ),
    constraint technical_debt_entries_consequence check (btrim(deferred_consequence_type) <> ''),
    constraint technical_debt_entries_resolution_shape check (
        (
            resolved_at is null and resolved_by_turn_id is null and resolution_type is null
        )
        or (
            resolved_at is not null and resolved_by_turn_id is not null
            and resolution_type in ('RECOVERY', 'ACCEPT_RESPONSIBILITY', 'RESOURCE_PAYMENT', 'NPC_COOPERATION')
        )
    ),
    constraint technical_debt_entries_metadata check (jsonb_typeof(metadata) = 'object')
);

create index technical_debt_entries_owner_run_turn_idx
    on technical_debt_entries (owner_id, run_id, turn_no);
create index technical_debt_entries_owner_unresolved_idx
    on technical_debt_entries (owner_id, run_id, deferred_consequence_type)
    where debt_delta > 0 and resolved_at is null;

create or replace function keyboard_wanderer.validate_codria_action_history()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
begin
    perform keyboard_wanderer.assert_committed_v4_action(
        new.turn_id, new.run_id, new.owner_id, new.turn_no
    );
    return new;
end
$$;

create or replace function keyboard_wanderer.validate_npc_relationship_history()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
declare
    projected_affinity smallint;
    projected_trust smallint;
    projected_fear smallint;
    projected_state text;
    projected_turn smallint;
begin
    perform keyboard_wanderer.assert_committed_v4_action(
        new.turn_id, new.run_id, new.owner_id, new.turn_no
    );

    select affinity, trust, fear, relationship_state, last_changed_turn
      into strict projected_affinity, projected_trust, projected_fear, projected_state, projected_turn
      from keyboard_wanderer.npc_relationships
     where id = new.relationship_id and run_id = new.run_id and owner_id = new.owner_id;

    if projected_affinity <> new.affinity_after
       or projected_trust <> new.trust_after
       or projected_fear <> new.fear_after
       or projected_state <> new.relationship_state_after
       or projected_turn <> new.turn_no then
        raise exception using
            errcode = '23514',
            message = 'NPC relationship history must match the current projection written in the same transaction';
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
    perform keyboard_wanderer.assert_committed_v4_action(
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

create or replace function keyboard_wanderer.enforce_unresolved_hook_transition()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
begin
    if tg_op = 'DELETE' then
        if pg_trigger_depth() > 1 then
            return old;
        end if;
        raise exception using errcode = '55000', message = 'hook history cannot be deleted directly';
    end if;

    if tg_op = 'INSERT' then
        if new.introduced_turn_no > 0 then
            perform keyboard_wanderer.assert_committed_v4_action(
                new.introduced_turn_id, new.run_id, new.owner_id, new.introduced_turn_no
            );
        end if;
        if new.status <> 'OPEN' then
            raise exception using errcode = '23514', message = 'new hooks must start open';
        end if;
        return new;
    end if;

    if new.id is distinct from old.id
       or new.run_id is distinct from old.run_id
       or new.owner_id is distinct from old.owner_id
       or new.hook_key is distinct from old.hook_key
       or new.region_axis_code is distinct from old.region_axis_code
       or new.npc_actor_id is distinct from old.npc_actor_id
       or new.introduced_turn_id is distinct from old.introduced_turn_id
       or new.introduced_turn_no is distinct from old.introduced_turn_no
       or new.summary is distinct from old.summary
       or new.hook_payload is distinct from old.hook_payload
       or new.deadline_turn is distinct from old.deadline_turn
       or new.created_at is distinct from old.created_at then
        raise exception using errcode = '55000', message = 'hook identity and introduction are immutable';
    end if;
    if old.status <> 'OPEN' or new.status not in ('RESOLVED', 'EXPIRED') then
        raise exception using errcode = '55000', message = 'a hook may transition from open exactly once';
    end if;

    perform keyboard_wanderer.assert_committed_v4_action(
        new.resolution_turn_id, new.run_id, new.owner_id, new.resolution_turn_no
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
        perform keyboard_wanderer.assert_committed_v4_action(
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

create trigger major_choices_validate
before insert on major_choices
for each row execute function keyboard_wanderer.validate_codria_action_history();

create trigger region_outcomes_validate
before insert on region_outcomes
for each row execute function keyboard_wanderer.validate_codria_action_history();

create trigger npc_relationship_history_validate
before insert on npc_relationship_history
for each row execute function keyboard_wanderer.validate_npc_relationship_history();

create trigger ability_usage_history_validate
before insert on ability_usage_history
for each row execute function keyboard_wanderer.validate_ability_usage_history();

create trigger unresolved_hooks_transition_guard
before insert or update or delete on unresolved_hooks
for each row execute function keyboard_wanderer.enforce_unresolved_hook_transition();

create trigger unresolved_hooks_set_updated_at
before update on unresolved_hooks
for each row execute function keyboard_wanderer.set_updated_at();

create trigger technical_debt_entries_guard
before insert or update or delete on technical_debt_entries
for each row execute function keyboard_wanderer.enforce_technical_debt_entry();

do $$
declare
    table_name text;
begin
    foreach table_name in array array[
        'major_choices', 'region_outcomes', 'npc_relationship_history',
        'ability_usage_history'
    ]
    loop
        execute format(
            'create trigger %I before update or delete on keyboard_wanderer.%I for each row execute function keyboard_wanderer.reject_generative_history_mutation()',
            table_name || '_append_only', table_name
        );
    end loop;
end
$$;

do $$
declare
    table_name text;
begin
    foreach table_name in array array[
        'major_choices', 'region_outcomes', 'npc_relationship_history',
        'ability_usage_history'
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

    foreach table_name in array array['unresolved_hooks', 'technical_debt_entries']
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
        execute format(
            'create policy %I on keyboard_wanderer.%I for update using (owner_id = (select keyboard_wanderer.current_app_user_id())) with check (owner_id = (select keyboard_wanderer.current_app_user_id()))',
            table_name || '_owner_update', table_name
        );
    end loop;
end
$$;

commit;
