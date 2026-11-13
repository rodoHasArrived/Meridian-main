create table if not exists __SCHEMA__.legacy_intercompany_attributions (
    attribution_id uuid primary key,
    revision bigint not null check(revision > 0),
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    journal_entry_id uuid not null,
    line_id uuid not null,
    tenant_id text not null check(length(btrim(tenant_id))>0 and lower(btrim(tenant_id))<>'all'),
    company_id text null,
    payload jsonb not null,
    unique(tenant_id,ledger_book_id,journal_entry_id,line_id,revision)
);
create index if not exists ix_legacy_intercompany_attribution_scope
    on __SCHEMA__.legacy_intercompany_attributions(tenant_id,ledger_book_id,journal_entry_id,line_id,revision);
create or replace function __SCHEMA__.refuse_legacy_intercompany_attribution_rewrite()
returns trigger language plpgsql as $$
begin raise exception 'Legacy intercompany attribution is append-only; retain a reviewed successor'; end;
$$;
drop trigger if exists tr_legacy_intercompany_attribution_immutable on __SCHEMA__.legacy_intercompany_attributions;
create trigger tr_legacy_intercompany_attribution_immutable
before update or delete on __SCHEMA__.legacy_intercompany_attributions
for each row execute function __SCHEMA__.refuse_legacy_intercompany_attribution_rewrite();
drop trigger if exists tr_legacy_intercompany_attribution_no_truncate on __SCHEMA__.legacy_intercompany_attributions;
create trigger tr_legacy_intercompany_attribution_no_truncate
before truncate on __SCHEMA__.legacy_intercompany_attributions
for each statement execute function __SCHEMA__.refuse_legacy_intercompany_attribution_rewrite();
