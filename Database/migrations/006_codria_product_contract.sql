begin;

set local search_path = keyboard_wanderer, public;

alter table campaign_phase_catalog
    drop constraint if exists campaign_phase_order;
alter table campaign_phase_catalog
    add constraint campaign_phase_order check (display_order between 1 and 9);

-- These identities are product constants, not seed-defined narrative content.
create table product_identity_catalog (
    code text primary key,
    identity_kind text not null unique,
    display_name_ko text not null,
    metadata jsonb not null default '{}'::jsonb,
    constraint product_identity_catalog_code check (
        code in ('WORLD_CODRIA', 'PROTAGONIST_NUPJUKYI', 'ARTIFACT_ADMIN_KEYBOARD')
    ),
    constraint product_identity_catalog_kind check (
        identity_kind in ('world', 'protagonist', 'artifact')
    ),
    constraint product_identity_catalog_name check (btrim(display_name_ko) <> ''),
    constraint product_identity_catalog_metadata check (jsonb_typeof(metadata) = 'object')
);

insert into product_identity_catalog (code, identity_kind, display_name_ko, metadata)
values
    ('WORLD_CODRIA', 'world', '코드리아', '{"fixedAcrossRuns":true}'::jsonb),
    ('PROTAGONIST_NUPJUKYI', 'protagonist', '넙죽이', '{"fixedAcrossRuns":true,"origin":"reality"}'::jsonb),
    ('ARTIFACT_ADMIN_KEYBOARD', 'artifact', '관리자 키보드', '{"fixedAcrossRuns":true}'::jsonb);

create table campaign_region_axis_catalog (
    code text primary key,
    display_order smallint not null unique,
    display_name_ko text not null,
    narrative_purpose text not null,
    metadata jsonb not null default '{}'::jsonb,
    constraint campaign_region_axis_catalog_code check (
        code in (
            'REGION_BUG_FOREST', 'REGION_BUFFER_VILLAGE', 'REGION_DEADLOCK_CITY',
            'REGION_DATA_GRAND_LIBRARY', 'REGION_LEGACY_CITADEL', 'REGION_ROOT_SYSTEM'
        )
    ),
    constraint campaign_region_axis_catalog_order check (display_order between 1 and 6),
    constraint campaign_region_axis_catalog_text check (
        btrim(display_name_ko) <> '' and btrim(narrative_purpose) <> ''
    ),
    constraint campaign_region_axis_catalog_metadata check (jsonb_typeof(metadata) = 'object')
);

insert into campaign_region_axis_catalog (
    code, display_order, display_name_ko, narrative_purpose, metadata
) values
    ('REGION_BUG_FOREST', 1, '버그 숲', '오류 제거와 공존의 첫 선택', '{"fixedAxis":true}'::jsonb),
    ('REGION_BUFFER_VILLAGE', 2, '버퍼 마을', '생존 자원과 공공 신뢰', '{"fixedAxis":true}'::jsonb),
    ('REGION_DEADLOCK_CITY', 3, '데드락 시티', '엉킨 관계와 협상', '{"fixedAxis":true}'::jsonb),
    ('REGION_DATA_GRAND_LIBRARY', 4, '데이터 대도서관', '붕괴 원인의 기록과 필수 단서', '{"fixedAxis":true}'::jsonb),
    ('REGION_LEGACY_CITADEL', 5, '레거시 성채', '기술 부채와 과거 선택의 역류', '{"fixedAxis":true}'::jsonb),
    ('REGION_ROOT_SYSTEM', 6, '루트 시스템', '최종 배치와 결말', '{"fixedAxis":true,"requiresAllAdminAccess":true}'::jsonb);

create table admin_access_level_catalog (
    code text primary key,
    access_level smallint not null unique,
    display_name_ko text not null,
    metadata jsonb not null default '{}'::jsonb,
    constraint admin_access_level_catalog_code check (
        code in ('ADMIN_ACCESS_LEVEL_1', 'ADMIN_ACCESS_LEVEL_2', 'ADMIN_ACCESS_LEVEL_3')
    ),
    constraint admin_access_level_catalog_level check (access_level between 1 and 3),
    constraint admin_access_level_catalog_name check (btrim(display_name_ko) <> ''),
    constraint admin_access_level_catalog_metadata check (jsonb_typeof(metadata) = 'object')
);

insert into admin_access_level_catalog (code, access_level, display_name_ko, metadata)
values
    ('ADMIN_ACCESS_LEVEL_1', 1, '관리자 권한 I', '{"fixedAcrossRuns":true}'::jsonb),
    ('ADMIN_ACCESS_LEVEL_2', 2, '관리자 권한 II', '{"fixedAcrossRuns":true}'::jsonb),
    ('ADMIN_ACCESS_LEVEL_3', 3, '관리자 권한 III', '{"fixedAcrossRuns":true}'::jsonb);

alter table runs
    add column world_contract_code text not null default 'WORLD_CODRIA'
        references product_identity_catalog(code),
    add column protagonist_contract_code text not null default 'PROTAGONIST_NUPJUKYI'
        references product_identity_catalog(code),
    add column artifact_contract_code text not null default 'ARTIFACT_ADMIN_KEYBOARD'
        references product_identity_catalog(code),
    add column product_contract_version text not null default 'codria.v4',
    add constraint runs_fixed_product_contract check (
        world_contract_code = 'WORLD_CODRIA'
        and protagonist_contract_code = 'PROTAGONIST_NUPJUKYI'
        and artifact_contract_code = 'ARTIFACT_ADMIN_KEYBOARD'
        and product_contract_version = 'codria.v4'
    );

-- Region axes are semantic product anchors. Areas, terrain biomes, and POIs are
-- still selected by the seed, then this six-row permutation is sealed.
create table world_region_axis_bindings (
    world_id uuid not null,
    owner_id uuid not null,
    region_axis_code text not null references campaign_region_axis_catalog(code),
    area_id uuid not null,
    terrain_biome_id text not null references biome_catalog(code),
    primary_poi_id uuid not null,
    binding_seed bigint not null,
    binding_metadata jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    primary key (world_id, region_axis_code),
    constraint world_region_axis_bindings_world_fk
        foreign key (world_id, owner_id) references worlds(id, owner_id) on delete cascade,
    constraint world_region_axis_bindings_area_fk
        foreign key (area_id, owner_id, world_id)
        references areas(id, owner_id, world_id) on delete cascade,
    constraint world_region_axis_bindings_poi_fk
        foreign key (primary_poi_id, owner_id, world_id)
        references world_pois(id, owner_id, world_id) on delete cascade,
    constraint world_region_axis_bindings_identity_unique
        unique (world_id, owner_id, region_axis_code),
    constraint world_region_axis_bindings_area_unique unique (world_id, area_id),
    constraint world_region_axis_bindings_poi_unique unique (world_id, primary_poi_id),
    constraint world_region_axis_bindings_metadata check (jsonb_typeof(binding_metadata) = 'object')
);

create index world_region_axis_bindings_owner_world_idx
    on world_region_axis_bindings (owner_id, world_id, region_axis_code);

create or replace function keyboard_wanderer.validate_region_axis_binding()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
declare
    poi_area_id uuid;
    poi_biome_id text;
begin
    select area_id, biome_id
      into strict poi_area_id, poi_biome_id
      from keyboard_wanderer.world_pois
     where id = new.primary_poi_id
       and owner_id = new.owner_id
       and world_id = new.world_id;

    if poi_area_id <> new.area_id or poi_biome_id <> new.terrain_biome_id then
        raise exception using
            errcode = '23514',
            message = 'region axis area, biome, and primary POI must describe the same sealed location';
    end if;

    if not exists (
        select 1
          from keyboard_wanderer.world_area_descriptors
         where area_id = new.area_id
           and owner_id = new.owner_id
           and world_id = new.world_id
           and biome_id = new.terrain_biome_id
    ) then
        raise exception using
            errcode = '23514',
            message = 'region axis binding must match the sealed area biome descriptor';
    end if;
    return new;
end
$$;

create trigger world_region_axis_bindings_validate
before insert or update on world_region_axis_bindings
for each row execute function keyboard_wanderer.validate_region_axis_binding();

create trigger world_region_axis_bindings_immutable
before update or delete on world_region_axis_bindings
for each row execute function keyboard_wanderer.reject_generated_world_mutation();

create or replace function keyboard_wanderer.validate_run_codria_axis_contract()
returns trigger
language plpgsql
set search_path = keyboard_wanderer, pg_catalog
as $$
declare
    stored_scope text;
    binding_count integer;
    world_biome_count integer;
begin
    select world_scope
      into strict stored_scope
      from keyboard_wanderer.worlds
     where id = new.world_id
       and owner_id = new.owner_id
       and campaign_id = new.campaign_id;

    if stored_scope <> 'run' then
        raise exception using
            errcode = '23514',
            message = 'Codria v4 runs require a world sealed for that run';
    end if;

    select count(*)
      into binding_count
      from keyboard_wanderer.world_region_axis_bindings
     where world_id = new.world_id and owner_id = new.owner_id;

    select count(distinct biome_id)
      into world_biome_count
      from keyboard_wanderer.world_area_descriptors
     where world_id = new.world_id and owner_id = new.owner_id;

    if binding_count <> 6 or world_biome_count <> 6 then
        raise exception using
            errcode = '23514',
            message = 'a Codria run world must bind all six region axes and contain all six terrain biomes';
    end if;
    return new;
end
$$;

create constraint trigger runs_validate_codria_axis_contract
after insert or update of world_id, owner_id, campaign_id on runs
deferrable initially deferred
for each row execute function keyboard_wanderer.validate_run_codria_axis_contract();

alter table world_region_axis_bindings enable row level security;
alter table world_region_axis_bindings force row level security;

create policy world_region_axis_bindings_owner_select on world_region_axis_bindings
for select using (owner_id = (select keyboard_wanderer.current_app_user_id()));

create policy world_region_axis_bindings_owner_insert on world_region_axis_bindings
for insert with check (owner_id = (select keyboard_wanderer.current_app_user_id()));

comment on table product_identity_catalog is
    'Fixed Codria product identities; seeds may never replace the world, protagonist, or administrator keyboard.';
comment on table campaign_region_axis_catalog is
    'The six fixed semantic region axes, kept separate from seeded physical terrain biomes.';
comment on table admin_access_level_catalog is
    'Exactly three ordered administrator access levels required by every run.';
comment on table world_region_axis_bindings is
    'Immutable per-world binding from each Codria region axis to a seeded area, its terrain biome, and primary POI; axes and biomes remain separate layers.';
revoke all on product_identity_catalog, campaign_region_axis_catalog,
    admin_access_level_catalog, world_region_axis_bindings from public;
revoke execute on function keyboard_wanderer.validate_region_axis_binding() from public;
revoke execute on function keyboard_wanderer.validate_run_codria_axis_contract() from public;

commit;
