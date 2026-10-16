-- Close review extends the existing hierarchy and immutable selection receipts. No live quote
-- or mutable lifecycle row can replace an approved input; corrections append linked versions.
create table if not exists __SCHEMA__.security_close_price_snapshots (
    snapshot_id uuid primary key,
    account_id text not null check (account_id <> '' and account_id = btrim(account_id)),
    book_id text not null check (book_id <> '' and book_id = btrim(book_id)),
    valuation_date date not null,
    version integer not null check (version > 0),
    corrects_snapshot_id uuid,
    payload text not null,
    payload_sha256 char(64) not null,
    recorded_at timestamptz not null default now(),
    unique (account_id, book_id, valuation_date, version),
    unique (snapshot_id, account_id, book_id, valuation_date),
    unique (corrects_snapshot_id, account_id, book_id, valuation_date),
    foreign key (corrects_snapshot_id, account_id, book_id, valuation_date)
        references __SCHEMA__.security_close_price_snapshots (snapshot_id, account_id, book_id, valuation_date),
    check ((version = 1 and corrects_snapshot_id is null) or (version > 1 and corrects_snapshot_id is not null)),
    check (octet_length(account_id) <= 256 and octet_length(book_id) <= 256)
);
create table if not exists __SCHEMA__.security_close_price_approvals (
    snapshot_id uuid primary key references __SCHEMA__.security_close_price_snapshots(snapshot_id),
    payload text not null,
    payload_sha256 char(64) not null,
    recorded_at timestamptz not null default now()
);
comment on table __SCHEMA__.security_close_price_snapshots is
    'Immutable exact account/book/date price-set versions retaining selected receipts, quote units, overrides, source rows and blockers.';
comment on table __SCHEMA__.security_close_price_approvals is
    'Immutable independent review receipt bound to the exact close price snapshot SHA-256.';

create function __SCHEMA__.guard_close_price_snapshot_append() returns trigger
language plpgsql as $$
begin
    perform pg_advisory_xact_lock(hashtextextended(json_build_array(new.account_id, new.book_id, new.valuation_date)::text, 0));
    if new.corrects_snapshot_id is not null and not exists (
        select 1 from __SCHEMA__.security_close_price_snapshots previous
        where previous.snapshot_id = new.corrects_snapshot_id and previous.account_id = new.account_id
            and previous.book_id = new.book_id and previous.valuation_date = new.valuation_date
            and previous.version + 1 = new.version
    ) then
        raise exception 'Close price corrections require a contiguous same-scope predecessor' using errcode = '23514';
    end if;
    return new;
end;
$$;
create trigger security_close_price_snapshots_append_guard before insert
    on __SCHEMA__.security_close_price_snapshots for each row
    execute function __SCHEMA__.guard_close_price_snapshot_append();

create function __SCHEMA__.guard_close_price_approval_append() returns trigger
language plpgsql as $$
declare
    snapshot __SCHEMA__.security_close_price_snapshots%rowtype;
    approval_json jsonb := new.payload::jsonb;
    snapshot_json jsonb;
begin
    select * into strict snapshot from __SCHEMA__.security_close_price_snapshots where snapshot_id = new.snapshot_id;
    perform pg_advisory_xact_lock(hashtextextended(json_build_array(snapshot.account_id, snapshot.book_id, snapshot.valuation_date)::text, 0));
    snapshot_json := snapshot.payload::jsonb;
    if exists (select 1 from __SCHEMA__.security_close_price_snapshots where corrects_snapshot_id = new.snapshot_id)
        or jsonb_array_length(snapshot_json->'Blockers') <> 0
        or lower(btrim(approval_json->>'Reviewer')) = lower(btrim(snapshot_json->>'CreatedBy'))
        or coalesce(btrim(approval_json->>'Reviewer'), '') = ''
        or coalesce(btrim(approval_json->>'Rationale'), '') = ''
        or approval_json->>'SnapshotSha256' is distinct from snapshot.payload_sha256::text
        or (approval_json->>'SnapshotId')::uuid is distinct from new.snapshot_id then
        raise exception 'Close approval requires independent review of an unblocked current immutable snapshot' using errcode = '23514';
    end if;
    return new;
end;
$$;
create trigger security_close_price_approvals_append_guard before insert
    on __SCHEMA__.security_close_price_approvals for each row
    execute function __SCHEMA__.guard_close_price_approval_append();

create trigger security_close_price_snapshots_immutable before update or delete
    on __SCHEMA__.security_close_price_snapshots for each row
    execute function __SCHEMA__.reject_price_selection_receipt_mutation();
create trigger security_close_price_snapshots_no_truncate before truncate
    on __SCHEMA__.security_close_price_snapshots for each statement
    execute function __SCHEMA__.reject_price_selection_receipt_mutation();
create trigger security_close_price_approvals_immutable before update or delete
    on __SCHEMA__.security_close_price_approvals for each row
    execute function __SCHEMA__.reject_price_selection_receipt_mutation();
create trigger security_close_price_approvals_no_truncate before truncate
    on __SCHEMA__.security_close_price_approvals for each statement
    execute function __SCHEMA__.reject_price_selection_receipt_mutation();
