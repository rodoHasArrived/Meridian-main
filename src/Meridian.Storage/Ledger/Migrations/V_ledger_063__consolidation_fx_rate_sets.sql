-- Approved FX authority is append-only. Book configuration revisions select exact rate/policy evidence.
create table if not exists __SCHEMA__.consolidation_fx_rate_sets (
    tenant_id text not null check (length(btrim(tenant_id)) > 0),
    company_id text not null check (length(btrim(company_id)) > 0),
    rate_set_id uuid not null,
    revision integer not null check (revision > 0),
    content_hash text not null check (length(content_hash) = 64),
    document jsonb not null check (jsonb_typeof(document) = 'object'),
    primary key (tenant_id,company_id,rate_set_id,revision),
    check (document->>'tenantId' = tenant_id and document->>'companyId' = company_id
        and (document->>'rateSetId')::uuid = rate_set_id and (document->>'revision')::integer = revision
        and document->>'contentHash' = content_hash)
);
create table if not exists __SCHEMA__.consolidation_fx_book_configurations (
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    revision integer not null check (revision > 0),
    tenant_id text not null,
    company_id text not null,
    effective_from date not null,
    effective_to date null,
    rate_set_id uuid not null,
    rate_set_revision integer not null,
    content_hash text not null check (length(content_hash) = 64),
    document jsonb not null check (jsonb_typeof(document) = 'object'),
    primary key (ledger_book_id,revision),
    foreign key (tenant_id,company_id,rate_set_id,rate_set_revision)
        references __SCHEMA__.consolidation_fx_rate_sets(tenant_id,company_id,rate_set_id,revision),
    check (effective_to is null or effective_to > effective_from),
    check ((document->>'ledgerBookId')::uuid = ledger_book_id and (document->>'revision')::integer = revision
        and document->>'tenantId' = tenant_id and document->>'companyId' = company_id
        and (document->>'effectiveFrom')::date = effective_from
        and (document->>'rateSetId')::uuid = rate_set_id
        and (document->>'rateSetRevision')::integer = rate_set_revision
        and document->>'contentHash' = content_hash)
);
create or replace function __SCHEMA__.protect_consolidation_fx_authority()
returns trigger language plpgsql as $function$
begin
    raise exception 'Approved consolidation FX authority is immutable; retain a new revision';
end
$function$;
create or replace function __SCHEMA__.sequence_consolidation_fx_authority()
returns trigger language plpgsql as $function$
declare
    prior_revision integer;
    prior_hash text;
    prior_tenant text;
    prior_company text;
begin
    -- The same lock is held shared by elimination posting until its ledger commit.
    perform pg_advisory_xact_lock(714293004001::bigint);
    if tg_table_name = 'consolidation_fx_rate_sets' then
        select revision,content_hash into prior_revision,prior_hash
        from __SCHEMA__.consolidation_fx_rate_sets
        where tenant_id=new.tenant_id and company_id=new.company_id and rate_set_id=new.rate_set_id
        order by revision desc limit 1;
    else
        select revision,content_hash,tenant_id,company_id into prior_revision,prior_hash,prior_tenant,prior_company
        from __SCHEMA__.consolidation_fx_book_configurations
        where ledger_book_id=new.ledger_book_id order by revision desc limit 1;
        if prior_revision is not null and (prior_tenant <> new.tenant_id or prior_company <> new.company_id) then
            raise exception 'FX configuration ownership cannot change';
        end if;
    end if;
    if new.revision <> coalesce(prior_revision,0)+1
        or (new.document->>'previousHash') is distinct from prior_hash then
        raise exception 'FX authority requires a contiguous revision and exact predecessor hash';
    end if;
    return new;
end
$function$;
create or replace trigger sequence_consolidation_fx_rates
before insert on __SCHEMA__.consolidation_fx_rate_sets
for each row execute function __SCHEMA__.sequence_consolidation_fx_authority();
create or replace trigger sequence_consolidation_fx_configuration
before insert on __SCHEMA__.consolidation_fx_book_configurations
for each row execute function __SCHEMA__.sequence_consolidation_fx_authority();
create or replace trigger protect_consolidation_fx_rates
before update or delete on __SCHEMA__.consolidation_fx_rate_sets
for each row execute function __SCHEMA__.protect_consolidation_fx_authority();
create or replace trigger protect_consolidation_fx_configuration
before update or delete on __SCHEMA__.consolidation_fx_book_configurations
for each row execute function __SCHEMA__.protect_consolidation_fx_authority();
create or replace trigger protect_consolidation_fx_rates_truncate
before truncate on __SCHEMA__.consolidation_fx_rate_sets
for each statement execute function __SCHEMA__.protect_consolidation_fx_authority();
create or replace trigger protect_consolidation_fx_configuration_truncate
before truncate on __SCHEMA__.consolidation_fx_book_configurations
for each statement execute function __SCHEMA__.protect_consolidation_fx_authority();
