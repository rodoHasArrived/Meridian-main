-- Migration: ledger / onboarding_workspaces
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- One retained document per bounded onboarding workspace (W11-SHADOW-001). The application verifies the
-- document's hash chains and refuses history rewrites before every save. The partial unique index makes
-- shadow-book exclusivity atomic across processes: at most one workspace per tenant can hold a ledger
-- book in shadow mode or have promoted it. A withdrawn trial releases the book.
create table if not exists __SCHEMA__.onboarding_workspaces (
    workspace_id text primary key check (length(btrim(workspace_id)) > 0),
    tenant_id text not null check (length(btrim(tenant_id)) > 0),
    company_id text not null check (length(btrim(company_id)) > 0),
    ledger_book_id uuid null,
    version integer not null check (version > 0),
    shadow_state text null check (shadow_state in ('Shadow', 'Promoted', 'Withdrawn')),
    created_at timestamptz not null,
    updated_at timestamptz not null,
    payload_json text not null check (jsonb_typeof(payload_json::jsonb) = 'object')
);

comment on table __SCHEMA__.onboarding_workspaces is
    'Retained bounded onboarding workspaces, including shadow-mode designation, certificates and promotion (W11-SHADOW-001).';

create index if not exists ix_onboarding_workspaces_scope
    on __SCHEMA__.onboarding_workspaces (tenant_id, company_id, updated_at desc);

create index if not exists ix_onboarding_workspaces_ledger_book
    on __SCHEMA__.onboarding_workspaces (tenant_id, ledger_book_id, created_at);

create unique index if not exists ux_onboarding_workspaces_active_shadow_book
    on __SCHEMA__.onboarding_workspaces (tenant_id, ledger_book_id)
    where shadow_state in ('Shadow', 'Promoted') and ledger_book_id is not null;

create or replace function __SCHEMA__.reject_onboarding_workspace_delete()
returns trigger language plpgsql as $$
begin
    raise exception 'Retained onboarding workspaces cannot be deleted.'
        using errcode = '55000';
end;
$$;

do $migration$
begin
    if not exists (select 1 from pg_trigger
        where tgrelid = '__SCHEMA__.onboarding_workspaces'::regclass
          and tgname = 'onboarding_workspace_retained') then
        create trigger onboarding_workspace_retained
            before delete on __SCHEMA__.onboarding_workspaces
            for each row execute function __SCHEMA__.reject_onboarding_workspace_delete();
    end if;
end;
$migration$;
