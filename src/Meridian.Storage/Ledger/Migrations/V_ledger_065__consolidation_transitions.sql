-- Reporting cutovers use fresh dedicated elimination books; posted history is never rewritten.
create table if not exists __SCHEMA__.consolidation_transition_previews (
    preview_id uuid primary key,
    transition_id uuid not null,
    organization_id uuid not null,
    ownership_root_id uuid not null,
    tenant_id text not null check (tenant_id=lower(trim(tenant_id)) and tenant_id<>'' and tenant_id<>'all'),
    company_id text null,
    fingerprint text not null check (fingerprint ~ '^[a-f0-9]{64}$'),
    prepared_at timestamptz not null,
    document jsonb not null check (jsonb_typeof(document)='object'),
    check ((document->>'previewId')::uuid=preview_id and (document#>>'{request,transitionId}')::uuid=transition_id
        and (document#>>'{request,organizationId}')::uuid=organization_id
        and (document#>>'{request,ownershipRootId}')::uuid=ownership_root_id
        and document->>'tenantId'=tenant_id and (document->>'companyId') is not distinct from company_id
        and document->>'fingerprint'=fingerprint)
);
create index if not exists ix_consolidation_transition_previews_root
    on __SCHEMA__.consolidation_transition_previews(tenant_id,organization_id,ownership_root_id,prepared_at);

create table if not exists __SCHEMA__.consolidation_transition_receipts (
    transition_id uuid primary key,
    preview_id uuid not null unique references __SCHEMA__.consolidation_transition_previews(preview_id),
    organization_id uuid not null,
    ownership_root_id uuid not null,
    root_version bigint not null check (root_version>0),
    previous_transition_id uuid null references __SCHEMA__.consolidation_transition_receipts(transition_id),
    previous_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    new_book_id uuid not null unique references __SCHEMA__.ledger_books(ledger_book_id),
    cutover_date date not null,
    tenant_id text not null check (tenant_id=lower(trim(tenant_id)) and tenant_id<>'' and tenant_id<>'all'),
    company_id text null,
    content_hash text not null check (content_hash ~ '^[a-f0-9]{64}$'),
    document jsonb not null check (jsonb_typeof(document)='object'),
    unique(tenant_id,organization_id,ownership_root_id,root_version),
    check (previous_book_id<>new_book_id),
    check ((document->>'transitionId')::uuid=transition_id and (document->>'previewId')::uuid=preview_id
        and (document->>'rootVersion')::bigint=root_version
        and ((document->>'previousTransitionId')::uuid) is not distinct from previous_transition_id
        and (document#>>'{request,transitionId}')::uuid=transition_id
        and (document#>>'{request,organizationId}')::uuid=organization_id
        and (document#>>'{request,ownershipRootId}')::uuid=ownership_root_id
        and (document#>>'{request,previousEliminationBookId}')::uuid=previous_book_id
        and (document#>>'{request,newEliminationBookId}')::uuid=new_book_id
        and (document#>>'{request,cutoverDate}')::date=cutover_date
        and document->>'tenantId'=tenant_id and (document->>'companyId') is not distinct from company_id
        and document->>'contentHash'=content_hash)
);
create unique index if not exists ux_consolidation_transition_successor
    on __SCHEMA__.consolidation_transition_receipts(previous_transition_id) where previous_transition_id is not null;

create or replace function __SCHEMA__.protect_consolidation_transition_history()
returns trigger language plpgsql as $$
begin
    raise exception 'Reporting transition evidence is immutable; prepare a new reviewed transition';
end;
$$;

create or replace function __SCHEMA__.sequence_consolidation_transition_receipt()
returns trigger language plpgsql as $$
declare
    head __SCHEMA__.consolidation_transition_receipts%rowtype;
    reviewed __SCHEMA__.consolidation_transition_previews%rowtype;
    book_count integer;
begin
    -- Same order as application approval: mappings, route fence, root CAS, ledger append head.
    lock table __SCHEMA__.ledger_books,__SCHEMA__.fund_profile_tenancy in share mode;
    perform pg_advisory_xact_lock(714293004002::bigint);
    perform pg_advisory_xact_lock(hashtextextended('consolidation-transition-root:'||new.tenant_id||':'||new.organization_id::text||':'||new.ownership_root_id::text,0));
    perform chain_id from __SCHEMA__.ledger_event_audit_head where chain_id=1 for update;
    if not found then raise exception 'The ledger append barrier is unavailable'; end if;
    select * into reviewed from __SCHEMA__.consolidation_transition_previews where preview_id=new.preview_id;
    if not found or reviewed.tenant_id<>new.tenant_id or reviewed.company_id is distinct from new.company_id
        or reviewed.transition_id<>new.transition_id or reviewed.organization_id<>new.organization_id
        or reviewed.ownership_root_id<>new.ownership_root_id
        or reviewed.document->'blockers' is distinct from '[]'::jsonb
        or (new.document->>'previewFingerprint') is distinct from reviewed.fingerprint
        or (new.document->>'preparedBy') is distinct from (reviewed.document->>'preparedBy')
        or nullif(trim(new.document->>'approvedBy'),'') is null
        or lower(trim(new.document->>'approvedBy'))=lower(trim(new.document->>'preparedBy'))
        or (new.document->'request') is distinct from (reviewed.document->'request')
        or (new.document->'newEvidence') is distinct from (reviewed.document->'newEvidence')
        or (new.document->'previousPostedJournalIds') is distinct from (reviewed.document->'previousPostedJournalIds')
        or (new.document->'previousEntityIds') is distinct from (reviewed.document->'previousEntityIds')
        or (new.document->>'previousBookEvidenceDigest') is distinct from (reviewed.document->>'previousBookEvidenceDigest')
        or (new.document->>'previousLatestPostedAsOf') is distinct from (reviewed.document->>'previousLatestPostedAsOf') then
        raise exception 'Reporting transition approval must match the independently reviewed retained preview';
    end if;
    select count(*) into book_count from __SCHEMA__.ledger_books b
        join __SCHEMA__.fund_profile_tenancy t on lower(trim(b.fund_profile_id))=t.fund_profile_id
        where b.ledger_book_id in(new.previous_book_id,new.new_book_id)
          and b.fund_structure_node_id=new.ownership_root_id and b.fund_structure_node_kind='Fund'
          and b.accounting_basis='Primary' and lower(trim(b.tenant_id))=new.tenant_id
          and lower(trim(t.tenant_id))=new.tenant_id
          and nullif(lower(trim(t.company_id)),'all') is not distinct from lower(trim(new.company_id));
    if book_count<>2 then raise exception 'Reporting transition books require one authorized fund root and owner scope'; end if;
    select * into head from __SCHEMA__.consolidation_transition_receipts
        where tenant_id=new.tenant_id and organization_id=new.organization_id and ownership_root_id=new.ownership_root_id
        order by root_version desc limit 1;
    if new.root_version<>coalesce(head.root_version,0)+1 or new.previous_transition_id is distinct from head.transition_id
        or (head.transition_id is not null and (new.previous_book_id<>head.new_book_id or new.cutover_date<=head.cutover_date
            or new.company_id is distinct from head.company_id)) then
        raise exception 'Reporting root revision changed or transition does not continue the current route';
    end if;
    if exists(select 1 from __SCHEMA__.consolidation_transition_receipts
        where tenant_id=new.tenant_id and organization_id=new.organization_id and ownership_root_id=new.ownership_root_id
            and (previous_book_id=new.new_book_id or new_book_id=new.new_book_id)) then
        raise exception 'A historical elimination book cannot be reused';
    end if;
    return new;
end;
$$;
create or replace trigger sequence_consolidation_transition_receipt
before insert on __SCHEMA__.consolidation_transition_receipts
for each row execute function __SCHEMA__.sequence_consolidation_transition_receipt();
create or replace trigger protect_consolidation_transition_previews
before update or delete on __SCHEMA__.consolidation_transition_previews
for each row execute function __SCHEMA__.protect_consolidation_transition_history();
create or replace trigger protect_consolidation_transition_previews_truncate
before truncate on __SCHEMA__.consolidation_transition_previews
for each statement execute function __SCHEMA__.protect_consolidation_transition_history();
create or replace trigger protect_consolidation_transition_receipts
before update or delete on __SCHEMA__.consolidation_transition_receipts
for each row execute function __SCHEMA__.protect_consolidation_transition_history();
create or replace trigger protect_consolidation_transition_receipts_truncate
before truncate on __SCHEMA__.consolidation_transition_receipts
for each statement execute function __SCHEMA__.protect_consolidation_transition_history();
