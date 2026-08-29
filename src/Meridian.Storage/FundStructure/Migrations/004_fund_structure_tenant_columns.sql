-- Fund-structure tenant partition columns and attribution quarantine (W9-GOV-008 criterion 2).
--
-- 001_fund_structure.sql carries the whole hierarchy -- organizations, businesses, clients, funds,
-- sleeves, vehicles, entities, portfolios, ownership links, assignments -- with no tenant column and
-- no predicate anywhere. PostgresFundStructureService.LoadSnapshotAsync consequently takes no scope
-- and loads every node, /api/fund-structure/graph serves that snapshot, and the mutations resolve
-- their parent nodes from the same global view. RequireFundScopedWriteTenant proves only that the
-- caller has *some* tenant, so a tenant-A administrator can read tenant-B structure and link or
-- mutate tenant-B nodes by id. This migration adds the column the scoped reader needs.
--
-- WHY THE COLUMN IS NULLABLE AND INERT HERE. Stamping and enforcement are deliberately NOT in this
-- migration. On a populated database every attribution option is wrong on its own: stamping rows to
-- the upgrading caller misassigns shared ancestors, leaving them null preserves exactly the leak
-- being closed, and rejecting null hides the retained graph from everyone. The attribution is
-- therefore derived -- downward from what fund_profile_tenancy can attribute, upward only where
-- descendants agree -- by FundStructureTenantAttribution, and whatever it cannot attribute lands in
-- the quarantine table below rather than being guessed. A null tenant_id means "not yet attributed"
-- and stays visible until the deployment switches the reader to its fail-closed mode, which is what
-- makes the ordering safe: the backfill lands before the tightening, so a scoped reader never loses
-- rows that were simply never stamped.
--
-- WHY THE ATTRIBUTION IS NOT SQL. fund_profile_tenancy lives in the ledger schema, reached through
-- its own connection string (FundStructureStartup.ConnectionStringVariable is separate from
-- LedgerStartup's), and the bridge from a fund profile to a structure node is ledger_books
-- (fund_profile_id, fund_structure_node_id). A deployment may point both at one database, but the
-- code must not assume it, so a cross-schema SQL backfill would be correct only sometimes and
-- silently do nothing otherwise. The derivation runs in the application, over the graph, where it is
-- unit-testable without a database -- the same reason TenantReadPredicate was factored out of the
-- stores whose integration suites CI cannot run.

alter table __SCHEMA__.organization add column if not exists tenant_id text null;
alter table __SCHEMA__.business add column if not exists tenant_id text null;
alter table __SCHEMA__.client add column if not exists tenant_id text null;
alter table __SCHEMA__.fund add column if not exists tenant_id text null;
alter table __SCHEMA__.sleeve add column if not exists tenant_id text null;
alter table __SCHEMA__.vehicle add column if not exists tenant_id text null;
alter table __SCHEMA__.legal_entity add column if not exists tenant_id text null;
alter table __SCHEMA__.investment_portfolio add column if not exists tenant_id text null;
alter table __SCHEMA__.ownership_link add column if not exists tenant_id text null;
alter table __SCHEMA__.fund_structure_assignment add column if not exists tenant_id text null;
alter table __SCHEMA__.fund_structure_linked_account add column if not exists tenant_id text null;

-- Partial indexes cover stamped rows only: an unattributed graph pays nothing for them, and the
-- lower(trim(...)) form matches the write-side stamp and TenantReadPredicate's comparison.
create index if not exists ix_organization_tenant
    on __SCHEMA__.organization (lower(trim(tenant_id))) where tenant_id is not null;
create index if not exists ix_business_tenant
    on __SCHEMA__.business (lower(trim(tenant_id))) where tenant_id is not null;
create index if not exists ix_client_tenant
    on __SCHEMA__.client (lower(trim(tenant_id))) where tenant_id is not null;
create index if not exists ix_fund_tenant
    on __SCHEMA__.fund (lower(trim(tenant_id))) where tenant_id is not null;
create index if not exists ix_sleeve_tenant
    on __SCHEMA__.sleeve (lower(trim(tenant_id))) where tenant_id is not null;
create index if not exists ix_vehicle_tenant
    on __SCHEMA__.vehicle (lower(trim(tenant_id))) where tenant_id is not null;
create index if not exists ix_legal_entity_tenant
    on __SCHEMA__.legal_entity (lower(trim(tenant_id))) where tenant_id is not null;
create index if not exists ix_investment_portfolio_tenant
    on __SCHEMA__.investment_portfolio (lower(trim(tenant_id))) where tenant_id is not null;
create index if not exists ix_ownership_link_tenant
    on __SCHEMA__.ownership_link (lower(trim(tenant_id))) where tenant_id is not null;
create index if not exists ix_fund_structure_assignment_tenant
    on __SCHEMA__.fund_structure_assignment (lower(trim(tenant_id))) where tenant_id is not null;
create index if not exists ix_fund_structure_linked_account_tenant
    on __SCHEMA__.fund_structure_linked_account (lower(trim(tenant_id))) where tenant_id is not null;

-- Quarantine for what the derivation refuses to guess.
--
-- Two distinct cases, kept distinct because they need different operator action:
--   'MixedOwnership'  -- an ancestor whose descendants resolve to more than one tenant. A genuinely
--                        shared node. Attributing it to either tenant would grant that tenant the
--                        other's subtree; splitting it is a modelling decision no migration can make.
--   'Underivable'     -- a node with no attributed descendant and no attributed ancestor, so
--                        fund_profile_tenancy says nothing about it either way.
-- candidate_tenant_ids records what the derivation *saw*, so an operator resolving a MixedOwnership
-- row does not have to re-walk the graph to find out who the claimants were.
create table if not exists __SCHEMA__.fund_structure_tenant_quarantine (
    node_id             uuid            not null primary key,
    node_kind           text            not null,
    reason              text            not null,
    candidate_tenant_ids jsonb          not null default '[]'::jsonb,
    detected_at_utc     timestamptz     not null default now(),
    resolved_at_utc     timestamptz     null,
    resolved_tenant_id  text            null,
    resolution_note     text            null
);

create index if not exists ix_fund_structure_tenant_quarantine_open
    on __SCHEMA__.fund_structure_tenant_quarantine (reason, detected_at_utc desc)
    where resolved_at_utc is null;

comment on table __SCHEMA__.fund_structure_tenant_quarantine is
    'Fund-structure nodes the W9-GOV-008 tenant attribution declined to attribute: MixedOwnership for a genuinely shared ancestor whose descendants resolve to more than one tenant, Underivable for a node the fund_profile_tenancy registry cannot reach in either direction. Rows here are an operator decision, not a defect - resolving one records the chosen tenant and the reason.';
