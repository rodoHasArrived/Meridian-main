-- New tenant-scoped authority. Existing entity/ledger history is not rewritten.
create table if not exists __SCHEMA__.intercompany_accounting_arrangement_events (
    tenant_id text not null check (tenant_id = lower(trim(tenant_id)) and tenant_id <> '' and tenant_id <> 'all'),
    arrangement_id uuid not null,
    aggregate_version bigint not null check (aggregate_version > 0),
    request_id uuid not null,
    payload jsonb not null,
    primary key (tenant_id, arrangement_id, aggregate_version),
    unique (tenant_id, arrangement_id, request_id)
);

create or replace function __SCHEMA__.refuse_intercompany_accounting_arrangement_history_mutation() returns trigger language plpgsql as $$
begin
    raise exception 'Retained intercompany agreement history is immutable';
end;
$$;

drop trigger if exists intercompany_accounting_arrangement_history_immutable on __SCHEMA__.intercompany_accounting_arrangement_events;
create trigger intercompany_accounting_arrangement_history_immutable before update or delete
on __SCHEMA__.intercompany_accounting_arrangement_events for each row
execute function __SCHEMA__.refuse_intercompany_accounting_arrangement_history_mutation();

drop trigger if exists intercompany_accounting_arrangement_history_no_truncate on __SCHEMA__.intercompany_accounting_arrangement_events;
create trigger intercompany_accounting_arrangement_history_no_truncate before truncate
on __SCHEMA__.intercompany_accounting_arrangement_events for each statement
execute function __SCHEMA__.refuse_intercompany_accounting_arrangement_history_mutation();
