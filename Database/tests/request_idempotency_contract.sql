begin;

set local search_path = keyboard_wanderer, public;

do $$
declare
    test_owner uuid := gen_random_uuid();
    test_run uuid := gen_random_uuid();
begin
    perform set_config('app.user_id', test_owner::text, true);
    insert into profiles (id, display_name) values (test_owner, 'Idempotency Contract');

    if not exists (
        select 1 from schema_migrations
         where version = '012_request_idempotency_and_schema_readiness'
    ) then
        raise exception 'required schema migration version is missing';
    end if;

    insert into request_idempotency (
        owner_id, operation, idempotency_key, request_fingerprint,
        status, lease_token, lease_expires_at
    ) values (
        test_owner, 'campaign.create', 'sql-create-idempotency-0001', repeat('a', 64),
        'pending', gen_random_uuid(), clock_timestamp() + interval '30 seconds'
    );

    begin
        update request_idempotency
           set status = 'completed', lease_token = null, lease_expires_at = null,
               completed_at = clock_timestamp()
         where owner_id = test_owner
           and operation = 'campaign.create'
           and idempotency_key = 'sql-create-idempotency-0001';
        raise exception 'creation completion without response_json must fail';
    exception
        when check_violation then null;
    end;

    update request_idempotency
       set status = 'completed', lease_token = null, lease_expires_at = null,
           response_json = '{"resourceType":"campaign","resourceId":"replayable"}'::jsonb,
           completed_at = clock_timestamp()
     where owner_id = test_owner
       and operation = 'campaign.create'
       and idempotency_key = 'sql-create-idempotency-0001';

    if not exists (
        select 1 from request_idempotency
         where owner_id = test_owner
           and operation = 'campaign.create'
           and idempotency_key = 'sql-create-idempotency-0001'
           and status = 'completed'
           and response_json is not null
    ) then
        raise exception 'creation response was not durably replayable';
    end if;

    insert into request_idempotency (
        owner_id, operation, idempotency_key, request_fingerprint,
        status, lease_token, lease_expires_at
    ) values (
        test_owner, 'turn:' || test_run::text, 'sql-turn-idempotency-000001', repeat('b', 64),
        'pending', gen_random_uuid(), clock_timestamp() + interval '30 seconds'
    );

    begin
        update request_idempotency
           set status = 'completed', lease_token = null, lease_expires_at = null,
               completed_at = clock_timestamp()
         where owner_id = test_owner
           and operation = 'turn:' || test_run::text
           and idempotency_key = 'sql-turn-idempotency-000001';
        raise exception 'turn completion without an authoritative turn ledger must fail';
    exception
        when check_violation then null;
    end;
end
$$;

rollback;
