-- Source facts commit with their immutable journal; delivery leases are not accounting authority.
create table if not exists __SCHEMA__.intercompany_proposals (
    proposal_id uuid primary key,
    tenant_id text not null check (length(btrim(tenant_id)) > 0),
    source_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    source_journal_id uuid not null references __SCHEMA__.journal_entries(journal_entry_id),
    source_payload text not null,
    source_digest text not null,
    status text not null default 'Pending' check (status in ('Pending','NeedsFix','Drafted','Linked')),
    attempts integer not null default 0,
    recorded_at timestamptz not null default now(),
    next_attempt_at timestamptz not null default now(),
    claim_token text null,
    lease_until timestamptz null,
    target_book_id uuid null,
    target_journal_id uuid null,
    last_error text null
);
create index if not exists ix_intercompany_proposals_pending
    on __SCHEMA__.intercompany_proposals(next_attempt_at, recorded_at)
    where status in ('Pending','NeedsFix');
create index if not exists ix_intercompany_proposals_book
    on __SCHEMA__.intercompany_proposals(tenant_id,source_book_id);

create table if not exists __SCHEMA__.intercompany_proposal_retries (
    tenant_id text not null, request_id uuid not null,
    proposal_id uuid not null references __SCHEMA__.intercompany_proposals(proposal_id),
    actor text not null, reason text not null, recorded_at timestamptz not null default now(),
    primary key(tenant_id,request_id)
);

create table if not exists __SCHEMA__.intercompany_proposal_links (
    tenant_id text not null,
    request_id uuid not null,
    proposal_id uuid not null references __SCHEMA__.intercompany_proposals(proposal_id),
    target_book_id uuid not null,
    target_journal_id uuid not null,
    actor text not null,
    reason text not null,
    recorded_at timestamptz not null default now(),
    primary key(tenant_id,request_id)
);
alter table __SCHEMA__.intercompany_proposal_links add column if not exists rejected_draft_id uuid null;
alter table __SCHEMA__.intercompany_proposal_links add column if not exists rejected_draft_version integer null;

-- Shared, immutable target ownership covers both delivery receipts and later manual resolution.
create table if not exists __SCHEMA__.intercompany_counterpart_reservations (
    tenant_id text not null,
    target_book_id uuid not null,
    target_journal_id uuid not null,
    proposal_id uuid not null references __SCHEMA__.intercompany_proposals(proposal_id),
    recorded_at timestamptz not null default now(),
    primary key(tenant_id,target_book_id,target_journal_id)
);
insert into __SCHEMA__.intercompany_counterpart_reservations(tenant_id,target_book_id,target_journal_id,proposal_id)
select tenant_id,target_book_id,target_journal_id,proposal_id from __SCHEMA__.intercompany_proposals
where status in ('Drafted','Linked') and target_book_id is not null and target_journal_id is not null
union
select tenant_id,target_book_id,target_journal_id,proposal_id from __SCHEMA__.intercompany_proposal_links where true
on conflict do nothing;
do $$ begin
    if exists (
        select 1 from (
            select tenant_id,target_book_id,target_journal_id,proposal_id from __SCHEMA__.intercompany_proposals
                where status in ('Drafted','Linked') and target_book_id is not null and target_journal_id is not null
            union all select tenant_id,target_book_id,target_journal_id,proposal_id from __SCHEMA__.intercompany_proposal_links
        ) facts join __SCHEMA__.intercompany_counterpart_reservations r
            using (tenant_id,target_book_id,target_journal_id)
        where facts.proposal_id <> r.proposal_id
    ) then raise exception 'Existing counterpart target ownership is ambiguous; reviewed repair is required'; end if;
end $$;
create or replace function __SCHEMA__.protect_intercompany_proposal_source() returns trigger
language plpgsql as $$
begin
    if tg_op <> 'UPDATE' then raise exception 'Intercompany proposal source is immutable'; end if;
    if (old.proposal_id,old.tenant_id,old.source_book_id,old.source_journal_id,old.source_payload,old.source_digest,old.recorded_at)
       is distinct from
       (new.proposal_id,new.tenant_id,new.source_book_id,new.source_journal_id,new.source_payload,new.source_digest,new.recorded_at)
    then raise exception 'Intercompany proposal source is immutable'; end if;
    if old.status in ('Drafted','Linked') and to_jsonb(old) is distinct from to_jsonb(new)
    then raise exception 'Terminal intercompany delivery receipt is immutable'; end if;
    return new;
end $$;
drop trigger if exists intercompany_proposals_protect on __SCHEMA__.intercompany_proposals;
create trigger intercompany_proposals_protect before update or delete on __SCHEMA__.intercompany_proposals
    for each row execute function __SCHEMA__.protect_intercompany_proposal_source();
create or replace function __SCHEMA__.protect_intercompany_proposal_links() returns trigger
language plpgsql as $$ begin raise exception 'Counterpart linkage evidence is immutable'; end $$;
drop trigger if exists intercompany_reservations_protect on __SCHEMA__.intercompany_counterpart_reservations;
create trigger intercompany_reservations_protect before update or delete on __SCHEMA__.intercompany_counterpart_reservations
    for each row execute function __SCHEMA__.protect_intercompany_proposal_links();
drop trigger if exists intercompany_reservations_no_truncate on __SCHEMA__.intercompany_counterpart_reservations;
create trigger intercompany_reservations_no_truncate before truncate on __SCHEMA__.intercompany_counterpart_reservations
    for each statement execute function __SCHEMA__.protect_intercompany_proposal_links();
drop trigger if exists intercompany_links_protect on __SCHEMA__.intercompany_proposal_links;
create trigger intercompany_links_protect before update or delete on __SCHEMA__.intercompany_proposal_links
    for each row execute function __SCHEMA__.protect_intercompany_proposal_links();
drop trigger if exists intercompany_retries_protect on __SCHEMA__.intercompany_proposal_retries;
create trigger intercompany_retries_protect before update or delete on __SCHEMA__.intercompany_proposal_retries
    for each row execute function __SCHEMA__.protect_intercompany_proposal_links();

-- One posted counterpart journal cannot discharge multiple retained source events.
create unique index if not exists ux_intercompany_proposal_target
    on __SCHEMA__.intercompany_proposals(tenant_id,target_book_id,target_journal_id)
    where status in ('Drafted','Linked');
drop trigger if exists intercompany_proposals_no_truncate on __SCHEMA__.intercompany_proposals;
create trigger intercompany_proposals_no_truncate before truncate on __SCHEMA__.intercompany_proposals
    for each statement execute function __SCHEMA__.protect_intercompany_proposal_source();
drop trigger if exists intercompany_links_no_truncate on __SCHEMA__.intercompany_proposal_links;
create trigger intercompany_links_no_truncate before truncate on __SCHEMA__.intercompany_proposal_links
    for each statement execute function __SCHEMA__.protect_intercompany_proposal_links();
drop trigger if exists intercompany_retries_no_truncate on __SCHEMA__.intercompany_proposal_retries;
create trigger intercompany_retries_no_truncate before truncate on __SCHEMA__.intercompany_proposal_retries
    for each statement execute function __SCHEMA__.protect_intercompany_proposal_links();
