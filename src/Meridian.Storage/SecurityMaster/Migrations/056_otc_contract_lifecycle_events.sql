-- Retained bilateral-contract servicing events. No instrument terms or existing contracts change.
create table if not exists __SCHEMA__.otc_contract_lifecycle_events (
    event_id uuid not null primary key,
    contract_id uuid not null references __SCHEMA__.otc_contracts (contract_id),
    contract_version bigint not null check (contract_version > 1),
    event_type text not null check (event_type in ('RateReset', 'Compression', 'Novation', 'CreditEvent', 'AuctionSettlement')),
    effective_date date not null,
    actor text not null check (length(trim(actor)) > 0),
    recorded_at timestamptz not null,
    payload_version integer not null check (payload_version >= 1),
    payload jsonb not null check (jsonb_typeof(payload) = 'object'),
    unique (contract_id, contract_version)
);
create unique index if not exists otc_contract_lifecycle_novation_successor_idx
    on __SCHEMA__.otc_contract_lifecycle_events ((payload #>> '{data,successorContractId}'))
    where event_type = 'Novation';

create function __SCHEMA__.reject_otc_contract_lifecycle_mutation() returns trigger
language plpgsql as $$
begin
    raise exception 'OTC contract lifecycle events are append-only' using errcode = '23514';
end;
$$;
create trigger otc_contract_lifecycle_append_only
    before update or delete on __SCHEMA__.otc_contract_lifecycle_events
    for each row execute function __SCHEMA__.reject_otc_contract_lifecycle_mutation();
create trigger otc_contract_lifecycle_no_truncate
    before truncate on __SCHEMA__.otc_contract_lifecycle_events
    for each statement execute function __SCHEMA__.reject_otc_contract_lifecycle_mutation();

comment on table __SCHEMA__.otc_contract_lifecycle_events is
    'Immutable versioned contract servicing facts, with explicit event identity, source and actor, and exact predecessor/successor snapshots. Written atomically with the contract version; no ledger posting or cash movement is implied.';

-- Older deployed CRUD writers do not know the application-level lifecycle refusal. Fence their
-- version increments at the database boundary while allowing a new writer to update and append
-- atomically. Deferred execution reads the final row rather than a queued intermediate NEW.version,
-- so several valid lifecycle transitions can share one transaction.
create function __SCHEMA__.require_otc_contract_lifecycle_version() returns trigger
language plpgsql as $$
declare
    current_version bigint;
    retained_version bigint;
begin
    select version into current_version
        from __SCHEMA__.otc_contracts where contract_id = new.contract_id;
    select max(contract_version) into retained_version
        from __SCHEMA__.otc_contract_lifecycle_events where contract_id = new.contract_id;
    if retained_version is not null and current_version is distinct from retained_version then
        raise exception 'OTC contract % must advance with its retained lifecycle history', new.contract_id
            using errcode = '23514', constraint = 'otc_contract_lifecycle_version_guard';
    end if;
    return null;
end;
$$;
create constraint trigger otc_contract_lifecycle_version_guard
    after update on __SCHEMA__.otc_contracts
    deferrable initially deferred
    for each row execute function __SCHEMA__.require_otc_contract_lifecycle_version();
