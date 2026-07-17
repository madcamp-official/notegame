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


commit;
