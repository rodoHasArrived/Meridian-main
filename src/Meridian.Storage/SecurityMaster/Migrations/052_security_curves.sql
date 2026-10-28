-- Migration 052: stored, versioned market curves (security-master / security_curves).
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- A curve's identity (key, kind, currency, index, tenor, day count, interpolation, source) is
-- write-once in security_curves. Its content is published as append-only, immutable versions in
-- security_curve_versions: a version carries the economic as-of date, the knowledge time it was
-- recorded, the vendor timestamp when known, the actor, the dated points as jsonb and a canonical
-- SHA-256 over the as-of and sorted points. A new version never overwrites an earlier one; the
-- same content for the same curve and as-of is one row. Update, delete and truncate are refused on
-- both tables by one reject function, so neither a curve's identity nor a version a valuation
-- names can change after it is written.
--
-- The TRUNCATE clauses below are the guard triggers that REJECT truncation; this migration
-- truncates nothing.

create table if not exists __SCHEMA__.security_curves (
    curve_id       uuid        not null primary key,
    curve_key      text        not null,
    kind           text        not null,
    currency       text        not null,
    index_name     text        null,
    tenor          text        null,
    day_count      text        not null,
    interpolation  text        not null,
    source         text        not null,
    description    text        null,
    created_by     text        not null,
    created_at     timestamptz not null,
    constraint security_curves_kind_check          check (kind in ('Discount', 'Forward', 'Inflation')),
    constraint security_curves_interpolation_check check (interpolation in ('Linear')),
    constraint security_curves_index_check         check ((kind = 'Discount') = (index_name is null)),
    constraint security_curves_currency_check      check (currency = upper(currency) and length(currency) = 3)
);

create unique index if not exists ux_security_curves_key
    on __SCHEMA__.security_curves (upper(curve_key));

create index if not exists ix_security_curves_kind_currency_index
    on __SCHEMA__.security_curves (kind, currency, upper(index_name));

comment on table __SCHEMA__.security_curves is
    'Write-once identity of a stored market curve (discount, forward or inflation): key, kind, currency, index, tenor, day count, interpolation and source.';

create table if not exists __SCHEMA__.security_curve_versions (
    curve_version_id uuid        not null primary key,
    curve_id         uuid        not null references __SCHEMA__.security_curves (curve_id),
    version          bigint      not null,
    as_of            date        not null,
    sourced_at       timestamptz null,
    recorded_at      timestamptz not null default now(),
    published_by     text        not null,
    source           text        not null,
    points           jsonb       not null,
    point_count      integer     not null,
    content_sha256   char(64)    not null,
    payload_version  integer     not null default 1,
    constraint security_curve_versions_version_check check (version >= 1),
    constraint security_curve_versions_points_check  check (point_count >= 1),
    constraint security_curve_versions_curve_version_key unique (curve_id, version),
    constraint security_curve_versions_content_key       unique (curve_id, as_of, content_sha256)
);

create index if not exists ix_security_curve_versions_as_of
    on __SCHEMA__.security_curve_versions (curve_id, as_of desc, version desc);

create index if not exists ix_security_curve_versions_recorded
    on __SCHEMA__.security_curve_versions (curve_id, recorded_at);

comment on table __SCHEMA__.security_curve_versions is
    'Append-only, immutable published versions of a stored curve: economic as-of, knowledge time, actor, dated points and a canonical content hash.';

create function __SCHEMA__.reject_security_curve_mutation() returns trigger
language plpgsql as $$
begin
    raise exception 'Security Master stored curves are write-once: % refuses %', tg_table_name, tg_op
        using errcode = '23514';
end;
$$;

create trigger security_curves_write_once
    before update or delete on __SCHEMA__.security_curves
    for each row execute function __SCHEMA__.reject_security_curve_mutation();

create trigger security_curves_no_truncate
    before truncate on __SCHEMA__.security_curves
    for each statement execute function __SCHEMA__.reject_security_curve_mutation();

create trigger security_curve_versions_immutable
    before update or delete on __SCHEMA__.security_curve_versions
    for each row execute function __SCHEMA__.reject_security_curve_mutation();

create trigger security_curve_versions_no_truncate
    before truncate on __SCHEMA__.security_curve_versions
    for each statement execute function __SCHEMA__.reject_security_curve_mutation();
