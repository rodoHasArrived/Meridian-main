-- Migration 064: profile-keyed relational projection for CustomAsset profile fields.
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- CustomAsset terms are a governed profile envelope ({customProfileId, profileVersion,
-- profileFields:{...}}) whose field set is declared per profile, so the class has no fixed column
-- set. This table projects one row per profileFields key instead, written by the specialized
-- CustomAsset writer in PostgresSecurityMasterStore in the same transaction as the securities row.
-- It is an additive read model over securities.asset_specific_terms; the blob stays the source of
-- truth and is not changed.
--
-- Values keep their stored meaning. raw_value holds the stored JSON value; value_kind is its
-- jsonb_typeof. The typed columns are derived from raw_value only: text_value for strings,
-- numeric_value for numbers (unbounded numeric, no precision loss), boolean_value for booleans.
-- A string that looks like a date or number stays a string; the profile definition, not this
-- table, governs field types. Objects, arrays and JSON null are carried in raw_value only.
--
-- A CustomAsset row without a complete envelope (legacy or unmapped terms) projects no rows.
--
-- ROLLOUT: the table is created empty and populates as CustomAsset records are persisted or
-- rebuilt; no existing data is read or rewritten here.
-- REVIEW: requires the repository's independent schema/storage review before merge.

create table if not exists __SCHEMA__.custom_asset_profile_field_projection (
    security_id        uuid    not null,
    custom_profile_id  text    not null,
    profile_version    integer not null,
    field_key          text    not null,
    value_kind         text    not null,
    text_value         text,
    numeric_value      numeric,
    boolean_value      boolean,
    raw_value          jsonb   not null,
    version            bigint  not null,
    constraint custom_asset_profile_field_projection_pkey primary key (security_id, field_key),
    constraint custom_asset_profile_field_projection_value_kind_check
        check (value_kind in ('string', 'number', 'boolean', 'null', 'object', 'array')),
    constraint custom_asset_profile_field_projection_raw_kind_check
        check (jsonb_typeof(raw_value) = value_kind),
    constraint custom_asset_profile_field_projection_typed_value_check
        check ((text_value is not null) = (value_kind = 'string')
               and (numeric_value is not null) = (value_kind = 'number')
               and (boolean_value is not null) = (value_kind = 'boolean')),
    constraint custom_asset_profile_field_projection_profile_id_check
        check (btrim(custom_profile_id) <> '')
);

create index if not exists custom_asset_profile_field_projection_profile_idx
    on __SCHEMA__.custom_asset_profile_field_projection (custom_profile_id, profile_version);

-- Text values are unbounded (profile Text fields carry no length limit), so the lookup index keys
-- on md5(text_value) rather than the value itself, which could exceed the B-tree entry size limit and
-- fail the projection write. Readers match the hash and then recheck the exact value.
create index if not exists custom_asset_profile_field_projection_field_text_idx
    on __SCHEMA__.custom_asset_profile_field_projection (custom_profile_id, field_key, md5(text_value));

comment on table __SCHEMA__.custom_asset_profile_field_projection is
    'Profile-keyed projection of CustomAsset profileFields: one row per field key, keyed by (security_id, field_key). Additive read model over securities.asset_specific_terms.';
