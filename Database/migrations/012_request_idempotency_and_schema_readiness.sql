begin;

set local search_path = keyboard_wanderer, public;

-- Global migration metadata is intentionally not owner-scoped. Readiness uses
-- it before accepting traffic and never writes it from a player request.
create table schema_migrations (
    version text primary key,
    applied_at timestamptz not null default clock_timestamp(),
    constraint schema_migrations_version_format check (version ~ '^[0-9]{3}_[a-z0-9_]+$')
);

-- A short-lived lease is acquired before any model/network work. The terminal
-- row is durable, while an expired pending row can be recovered after a worker
-- crash without holding a database transaction open during inference.
create table request_idempotency (
    owner_id uuid not null references profiles(id) on delete cascade,
    operation text not null,
    idempotency_key text not null,
    request_fingerprint text not null,
    status text not null default 'pending',
    lease_token uuid,
    lease_expires_at timestamptz,
    response_json jsonb,
    completed_at timestamptz,
    created_at timestamptz not null default clock_timestamp(),
    updated_at timestamptz not null default clock_timestamp(),
    primary key (owner_id, operation, idempotency_key),
    constraint request_idempotency_operation check (
        operation ~ '^[a-z][a-z0-9._:-]{2,159}$'
    ),
    constraint request_idempotency_key check (
        char_length(idempotency_key) between 8 and 128
        and idempotency_key ~ '^[A-Za-z0-9][A-Za-z0-9_.:-]+$'
    ),
    constraint request_idempotency_fingerprint check (
        request_fingerprint ~ '^[a-f0-9]{64}$'
    ),
    constraint request_idempotency_status check (status in ('pending', 'completed')),
    constraint request_idempotency_response_object check (
        response_json is null or jsonb_typeof(response_json) = 'object'
    ),
    constraint request_idempotency_state_shape check (
        (
            status = 'pending'
            and lease_token is not null
            and lease_expires_at is not null
            and response_json is null
            and completed_at is null
        )
        or (
            status = 'completed'
            and lease_token is null
            and lease_expires_at is null
            and completed_at is not null
        )
    )
);

create index request_idempotency_expired_lease_idx
    on request_idempotency (lease_expires_at)
    where status = 'pending';

comment on table request_idempotency is
    'Owner-scoped lease and replay ledger acquired before LLM work and creation side effects.';

create trigger request_idempotency_set_updated_at
before update on request_idempotency
for each row execute function keyboard_wanderer.set_updated_at();

create or replace function keyboard_wanderer.protect_request_idempotency_terminal()
returns trigger
language plpgsql
set search_path = pg_catalog
as $$
declare
    ledger_run_id uuid;
begin
    if old.owner_id is distinct from new.owner_id
       or old.operation is distinct from new.operation
       or old.idempotency_key is distinct from new.idempotency_key
       or old.request_fingerprint is distinct from new.request_fingerprint
       or old.created_at is distinct from new.created_at then
        raise exception using errcode = '23514', message = 'idempotency request identity is immutable';
    end if;
    if old.status = 'completed' then
        raise exception using errcode = '23514', message = 'completed idempotency results are immutable';
    end if;
    if new.status not in ('pending', 'completed') then
        raise exception using errcode = '23514', message = 'invalid idempotency request transition';
    end if;
    if new.status = 'completed' and new.response_json is null then
        if new.operation ~* '^turn:[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$' then
            ledger_run_id := split_part(new.operation, ':', 2)::uuid;
            if not exists (
                select 1
                  from keyboard_wanderer.turn_records
                 where owner_id = new.owner_id
                   and run_id = ledger_run_id
                   and idempotency_key = new.idempotency_key
                   and status = 'committed'
            ) then
                raise exception using errcode = '23514', message = 'turn idempotency completion requires a committed authoritative turn';
            end if;
        elsif new.operation ~* '^travel:[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$' then
            ledger_run_id := split_part(new.operation, ':', 2)::uuid;
            if not exists (
                select 1
                  from keyboard_wanderer.safe_travels
                 where owner_id = new.owner_id
                   and run_id = ledger_run_id
                   and idempotency_key = new.idempotency_key
            ) then
                raise exception using errcode = '23514', message = 'travel idempotency completion requires an authoritative travel result';
            end if;
        else
            raise exception using errcode = '23514', message = 'creation idempotency completion requires a replay response';
        end if;
    end if;
    return new;
end
$$;

create trigger request_idempotency_protect_terminal
before update on request_idempotency
for each row execute function keyboard_wanderer.protect_request_idempotency_terminal();

alter table request_idempotency enable row level security;
alter table request_idempotency force row level security;

create policy request_idempotency_owner_select on request_idempotency
for select
using (owner_id = (select keyboard_wanderer.current_app_user_id()));

create policy request_idempotency_owner_insert on request_idempotency
for insert
with check (owner_id = (select keyboard_wanderer.current_app_user_id()));

create policy request_idempotency_owner_update on request_idempotency
for update
using (owner_id = (select keyboard_wanderer.current_app_user_id()))
with check (owner_id = (select keyboard_wanderer.current_app_user_id()));

create policy request_idempotency_owner_delete_pending on request_idempotency
for delete
using (
    owner_id = (select keyboard_wanderer.current_app_user_id())
    and status = 'pending'
);

-- Runtime-generated natural-language inventory items share a deliberately
-- generic catalog contract; their concrete name/effects remain in state_json.
insert into item_catalog (
    code, display_name, description, asset_id, is_stackable, max_stack, base_properties
)
values (
    'RUNTIME_ITEM', 'Runtime Item',
    'A server-validated item generated during an authoritative run.',
    'item.runtime.v1', true, 500, '{"runtimeGenerated":true}'::jsonb
)
on conflict (code) do update set
    display_name = excluded.display_name,
    description = excluded.description,
    asset_id = excluded.asset_id,
    is_stackable = excluded.is_stackable,
    max_stack = excluded.max_stack,
    base_properties = excluded.base_properties;

insert into schema_migrations (version)
values
    ('001_core_schema'),
    ('002_row_security_and_views'),
    ('003_campaign_director_state'),
    ('004_codria_world_and_travel'),
    ('005_generative_run_state'),
    ('006_codria_product_contract'),
    ('007_codria_actions_and_access'),
    ('008_codria_narrative_history'),
    ('009_codria_narrative_choice_turns'),
    ('010_soft_turn_horizon_and_slot_reservations'),
    ('011_player_action_skill_contract'),
    ('012_request_idempotency_and_schema_readiness');

commit;
