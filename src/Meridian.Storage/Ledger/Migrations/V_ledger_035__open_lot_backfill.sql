-- Governed enrichment of legacy rows. Source bytes and every decision survive restart.
create table if not exists __SCHEMA__.open_lot_backfill_evidence (
    evidence_record_id uuid primary key,
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    tax_lot_record_id uuid not null references __SCHEMA__.tax_lots(tax_lot_record_id),
    source_system text not null,
    source_reference text not null,
    source_uri text not null,
    content bytea not null check (octet_length(content) between 1 and 262144),
    content_hash_sha256 text not null check (content_hash_sha256 ~ '^[0-9a-f]{64}$'),
    retention_fingerprint text not null,
    retained_by text not null check (length(trim(retained_by)) > 0),
    retained_at timestamptz not null
);

create table if not exists __SCHEMA__.open_lot_backfill_reviews (
    evidence_record_id uuid primary key references __SCHEMA__.open_lot_backfill_evidence(evidence_record_id),
    accepted boolean not null,
    reviewed_by text not null check (length(trim(reviewed_by)) > 0),
    reviewed_at timestamptz not null,
    rationale text not null check (length(trim(rationale)) > 0)
);

create table if not exists __SCHEMA__.open_lot_backfill_receipts (
    receipt_id uuid primary key,
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    tax_lot_record_id uuid not null unique references __SCHEMA__.tax_lots(tax_lot_record_id),
    evidence_record_id uuid not null references __SCHEMA__.open_lot_backfill_reviews(evidence_record_id),
    idempotency_key text not null,
    request_fingerprint text not null,
    expected_lot_version bigint not null,
    resulting_lot_version bigint not null,
    snapshot_before jsonb not null,
    snapshot_after jsonb not null,
    receipt jsonb not null,
    transaction_id bigint not null default txid_current(),
    unique (ledger_book_id, idempotency_key),
    check (resulting_lot_version = expected_lot_version + 1)
);

create table if not exists __SCHEMA__.open_lot_backfill_exceptions (
    tax_lot_record_id uuid primary key references __SCHEMA__.tax_lots(tax_lot_record_id),
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    lot_id text not null,
    lot_version bigint not null,
    issues jsonb not null,
    version bigint not null default 1 check (version > 0),
    first_observed_at timestamptz not null,
    last_observed_at timestamptz not null,
    resolution_receipt_id uuid null references __SCHEMA__.open_lot_backfill_receipts(receipt_id)
);
create index if not exists ix_open_lot_backfill_exceptions_open
    on __SCHEMA__.open_lot_backfill_exceptions(ledger_book_id, first_observed_at)
    where resolution_receipt_id is null;

create or replace function __SCHEMA__.protect_open_lot_backfill_evidence()
returns trigger language plpgsql as $function$
begin
    raise exception 'Retained open-lot backfill evidence, reviews and receipts are immutable';
end
$function$;

do $migration$
declare target text;
begin
    foreach target in array array['open_lot_backfill_evidence', 'open_lot_backfill_reviews', 'open_lot_backfill_receipts'] loop
        if not exists (select 1 from pg_trigger where tgrelid = ('__SCHEMA__.' || target)::regclass
            and tgname = 'protect_open_lot_backfill_evidence') then
            execute format('create trigger protect_open_lot_backfill_evidence before update or delete on __SCHEMA__.%I '
                || 'for each row execute function __SCHEMA__.protect_open_lot_backfill_evidence()', target);
        end if;
    end loop;
end
$migration$;

create or replace function __SCHEMA__.guard_open_lot_backfill_review()
returns trigger language plpgsql as $function$
begin
    if exists (select 1 from __SCHEMA__.open_lot_backfill_evidence e
        where e.evidence_record_id = new.evidence_record_id
        and (lower(trim(e.retained_by)) = lower(trim(new.reviewed_by)) or e.retained_at > new.reviewed_at)) then
        raise exception 'Open-lot backfill review must be independent and follow source retention';
    end if;
    return new;
end
$function$;
do $migration$
begin
    if not exists (select 1 from pg_trigger where tgrelid = '__SCHEMA__.open_lot_backfill_reviews'::regclass
        and tgname = 'guard_open_lot_backfill_review') then
        create trigger guard_open_lot_backfill_review before insert on __SCHEMA__.open_lot_backfill_reviews
            for each row execute function __SCHEMA__.guard_open_lot_backfill_review();
    end if;
end
$migration$;

create or replace function __SCHEMA__.guard_open_lot_backfill_exception()
returns trigger language plpgsql as $function$
begin
    if tg_op = 'DELETE' then
        raise exception 'Open-lot backfill exceptions cannot be dismissed or deleted';
    end if;
    if old.resolution_receipt_id is not null or new.tax_lot_record_id <> old.tax_lot_record_id
        or new.ledger_book_id <> old.ledger_book_id then
        raise exception 'Resolved open-lot backfill exceptions and their scope are immutable';
    end if;
    if new.resolution_receipt_id is not null and not exists (
        select 1 from __SCHEMA__.open_lot_backfill_receipts r
        join __SCHEMA__.tax_lots l on l.tax_lot_record_id = r.tax_lot_record_id
        where r.receipt_id = new.resolution_receipt_id and r.ledger_book_id = new.ledger_book_id
          and r.tax_lot_record_id = new.tax_lot_record_id and r.transaction_id = txid_current()
          and l.acquisition_terms is not null and l.version = r.resulting_lot_version) then
        raise exception 'Only an atomic evidenced lot backfill can resolve its exception';
    end if;
    return new;
end
$function$;
do $migration$
begin
    if not exists (select 1 from pg_trigger where tgrelid = '__SCHEMA__.open_lot_backfill_exceptions'::regclass
        and tgname = 'guard_open_lot_backfill_exception') then
        create trigger guard_open_lot_backfill_exception before update or delete on __SCHEMA__.open_lot_backfill_exceptions
            for each row execute function __SCHEMA__.guard_open_lot_backfill_exception();
    end if;
end
$migration$;

-- Preserve the prior immutable contract, allowing exactly one approved null-to-known enrichment
-- whose full old/new SQL snapshots match an immutable receipt in this same transaction.
create or replace function __SCHEMA__.protect_open_lot_acquisition()
returns trigger language plpgsql as $function$
begin
    if old.acquisition_terms is distinct from new.acquisition_terms then
        if old.acquisition_terms is not null or new.acquisition_terms is null or not exists (
            select 1 from __SCHEMA__.open_lot_backfill_receipts r
            join __SCHEMA__.open_lot_backfill_reviews v on v.evidence_record_id = r.evidence_record_id
            join __SCHEMA__.open_lot_backfill_evidence e on e.evidence_record_id = v.evidence_record_id
            where r.tax_lot_record_id = old.tax_lot_record_id and r.ledger_book_id = old.ledger_book_id
              and e.tax_lot_record_id = old.tax_lot_record_id and e.ledger_book_id = old.ledger_book_id
              and r.expected_lot_version = old.version and r.resulting_lot_version = new.version
              and r.snapshot_before = to_jsonb(old) and r.snapshot_after = to_jsonb(new)
              and r.transaction_id = txid_current() and v.accepted) then
            raise exception 'Retained open-lot acquisition facts are immutable; use governed correction lineage or approved legacy backfill';
        end if;
    end if;
    if old.acquisition_terms is not null and (
        old.security_id is distinct from new.security_id or old.book_position_id is distinct from new.book_position_id
        or old.ledger_book_id is distinct from new.ledger_book_id or old.acquired_date is distinct from new.acquired_date
        or old.original_quantity is distinct from new.original_quantity or old.currency is distinct from new.currency
        or old.unit_cost is distinct from new.unit_cost or old.original_face is distinct from new.original_face
        or old.booked_factor is distinct from new.booked_factor or old.par_basis is distinct from new.par_basis) then
        raise exception 'Canonical lot identity and acquisition economics require append-only correction lineage';
    end if;
    return new;
end
$function$;
