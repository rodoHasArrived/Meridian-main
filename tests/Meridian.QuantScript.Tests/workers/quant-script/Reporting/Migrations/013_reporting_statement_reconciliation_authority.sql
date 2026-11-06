create table if not exists __SCHEMA__.reporting_statement_reconciliation_documents (
    tenant_id text not null,
    company_id text not null,
    workflow_id text not null,
    document_key text not null,
    content_hash_sha256 text not null,
    byte_size bigint not null,
    is_immutable boolean not null,
    document_version bigint not null default 1,
    stored_at_utc timestamptz not null default clock_timestamp(),
    updated_at_utc timestamptz not null default clock_timestamp(),
    primary key (tenant_id, company_id, workflow_id, document_key),
    constraint fk_reporting_statement_document_blob
        foreign key (tenant_id, content_hash_sha256)
        references __SCHEMA__.reporting_artifact_blobs (tenant_id, content_hash_sha256)
        on update restrict
        on delete restrict,
    constraint ck_reporting_statement_document_tenant
        check (tenant_id = btrim(tenant_id)
            and length(tenant_id) between 1 and 256
            and tenant_id !~ '[[:cntrl:]]'),
    constraint ck_reporting_statement_document_company
        check (company_id = btrim(company_id)
            and length(company_id) between 1 and 256
            and company_id !~ '[[:cntrl:]]'),
    constraint ck_reporting_statement_document_workflow
        check (workflow_id = btrim(workflow_id)
            and length(workflow_id) between 1 and 256
            and workflow_id !~ '[[:cntrl:]]'),
    constraint ck_reporting_statement_document_key
        check (document_key = btrim(document_key)
            and length(document_key) between 1 and 1024
            and left(document_key, 1) <> '/'
            and right(document_key, 1) <> '/'
            and strpos(document_key, '//') = 0
            and strpos(document_key, chr(92)) = 0
            and document_key !~ '(^|/)\.\.?(/|$)'
            and document_key !~ '[[:cntrl:]]'),
    constraint ck_reporting_statement_document_identity_utf8_bytes
        check (octet_length(tenant_id)
            + octet_length(company_id)
            + octet_length(workflow_id)
            + octet_length(document_key) <= 2048),
    constraint ck_reporting_statement_document_hash
        check (content_hash_sha256 ~ '^[0-9a-f]{64}$'),
    constraint ck_reporting_statement_document_size
        check (byte_size > 0),
    constraint ck_reporting_statement_document_version
        check (document_version > 0),
    constraint ck_reporting_statement_document_timestamps
        check (updated_at_utc >= stored_at_utc)
);

create index if not exists ix_reporting_statement_documents_prefix
    on __SCHEMA__.reporting_statement_reconciliation_documents (
        tenant_id,
        company_id,
        workflow_id,
        document_key text_pattern_ops);

create index if not exists ix_reporting_statement_documents_updated
    on __SCHEMA__.reporting_statement_reconciliation_documents (
        tenant_id,
        company_id,
        workflow_id,
        updated_at_utc desc);

create table if not exists __SCHEMA__.reporting_statement_reconciliation_document_revisions (
    tenant_id text not null,
    company_id text not null,
    workflow_id text not null,
    document_key text not null,
    document_version bigint not null,
    previous_content_hash_sha256 text null,
    previous_byte_size bigint null,
    previous_updated_at_utc timestamptz null,
    content_hash_sha256 text not null,
    byte_size bigint not null,
    is_immutable boolean not null,
    mapping_stored_at_utc timestamptz not null,
    mapping_updated_at_utc timestamptz not null,
    recorded_at_utc timestamptz not null default clock_timestamp(),
    primary key (
        tenant_id,
        company_id,
        workflow_id,
        document_key,
        document_version),
    constraint fk_reporting_statement_revision_blob
        foreign key (tenant_id, content_hash_sha256)
        references __SCHEMA__.reporting_artifact_blobs (tenant_id, content_hash_sha256)
        on update restrict
        on delete restrict,
    constraint fk_reporting_statement_revision_previous_blob
        foreign key (tenant_id, previous_content_hash_sha256)
        references __SCHEMA__.reporting_artifact_blobs (tenant_id, content_hash_sha256)
        on update restrict
        on delete restrict,
    constraint ck_reporting_statement_revision_tenant
        check (tenant_id = btrim(tenant_id)
            and length(tenant_id) between 1 and 256
            and tenant_id !~ '[[:cntrl:]]'),
    constraint ck_reporting_statement_revision_company
        check (company_id = btrim(company_id)
            and length(company_id) between 1 and 256
            and company_id !~ '[[:cntrl:]]'),
    constraint ck_reporting_statement_revision_workflow
        check (workflow_id = btrim(workflow_id)
            and length(workflow_id) between 1 and 256
            and workflow_id !~ '[[:cntrl:]]'),
    constraint ck_reporting_statement_revision_key
        check (document_key = btrim(document_key)
            and length(document_key) between 1 and 1024
            and left(document_key, 1) <> '/'
            and right(document_key, 1) <> '/'
            and strpos(document_key, '//') = 0
            and strpos(document_key, chr(92)) = 0
            and document_key !~ '(^|/)\.\.?(/|$)'
            and document_key !~ '[[:cntrl:]]'),
    constraint ck_reporting_statement_revision_identity_utf8_bytes
        check (octet_length(tenant_id)
            + octet_length(company_id)
            + octet_length(workflow_id)
            + octet_length(document_key) <= 2048),
    constraint ck_reporting_statement_revision_hash
        check (content_hash_sha256 ~ '^[0-9a-f]{64}$'),
    constraint ck_reporting_statement_revision_previous_hash
        check (previous_content_hash_sha256 is null
            or previous_content_hash_sha256 ~ '^[0-9a-f]{64}$'),
    constraint ck_reporting_statement_revision_size
        check (byte_size > 0),
    constraint ck_reporting_statement_revision_chain
        check ((document_version = 1
                and previous_content_hash_sha256 is null
                and previous_byte_size is null
                and previous_updated_at_utc is null)
            or (document_version > 1
                and previous_content_hash_sha256 is not null
                and previous_byte_size > 0
                and previous_updated_at_utc is not null)),
    constraint ck_reporting_statement_revision_timestamps
        check (mapping_updated_at_utc >= mapping_stored_at_utc
            and (previous_updated_at_utc is null
                or (previous_updated_at_utc >= mapping_stored_at_utc
                    and previous_updated_at_utc <= mapping_updated_at_utc))
            and recorded_at_utc >= mapping_updated_at_utc)
);

create index if not exists ix_reporting_statement_revision_current_hash
    on __SCHEMA__.reporting_statement_reconciliation_document_revisions (
        tenant_id,
        content_hash_sha256);

create or replace function __SCHEMA__.guard_reporting_statement_document_mutation()
returns trigger
language plpgsql
as $$
begin
    if tg_op = 'DELETE' then
        raise exception 'statement reconciliation authority mappings cannot be deleted'
            using errcode = '55000';
    end if;

    if old.tenant_id is distinct from new.tenant_id
        or old.company_id is distinct from new.company_id
        or old.workflow_id is distinct from new.workflow_id
        or old.document_key is distinct from new.document_key
        or old.is_immutable is distinct from new.is_immutable
        or old.stored_at_utc is distinct from new.stored_at_utc then
        raise exception 'statement reconciliation authority identity and retention policy cannot be changed'
            using errcode = '55000';
    end if;

    if old.is_immutable then
        raise exception 'immutable statement reconciliation authority mappings cannot be updated'
            using errcode = '55000';
    end if;

    if new.document_version <> old.document_version + 1
        or new.updated_at_utc < old.updated_at_utc then
        raise exception 'mutable statement reconciliation authority mappings require a monotonic version and timestamp'
            using errcode = '55000';
    end if;

    return new;
end;
$$;

create or replace function __SCHEMA__.retain_reporting_statement_document_revision()
returns trigger
language plpgsql
as $$
begin
    insert into __SCHEMA__.reporting_statement_reconciliation_document_revisions (
        tenant_id,
        company_id,
        workflow_id,
        document_key,
        document_version,
        previous_content_hash_sha256,
        previous_byte_size,
        previous_updated_at_utc,
        content_hash_sha256,
        byte_size,
        is_immutable,
        mapping_stored_at_utc,
        mapping_updated_at_utc)
    values (
        new.tenant_id,
        new.company_id,
        new.workflow_id,
        new.document_key,
        new.document_version,
        case when tg_op = 'UPDATE' then old.content_hash_sha256 else null end,
        case when tg_op = 'UPDATE' then old.byte_size else null end,
        case when tg_op = 'UPDATE' then old.updated_at_utc else null end,
        new.content_hash_sha256,
        new.byte_size,
        new.is_immutable,
        new.stored_at_utc,
        new.updated_at_utc);

    return new;
end;
$$;

create or replace function __SCHEMA__.guard_reporting_statement_revision_mutation()
returns trigger
language plpgsql
as $$
begin
    raise exception 'statement reconciliation document revisions are append-only'
        using errcode = '55000';
    return null;
end;
$$;

create or replace function __SCHEMA__.reject_reporting_statement_document_truncate()
returns trigger
language plpgsql
as $$
begin
    raise exception 'statement reconciliation authority mappings cannot be truncated'
        using errcode = '55000';
    return null;
end;
$$;

create or replace function __SCHEMA__.reject_reporting_statement_revision_truncate()
returns trigger
language plpgsql
as $$
begin
    raise exception 'statement reconciliation document revisions cannot be truncated'
        using errcode = '55000';
    return null;
end;
$$;

create or replace function __SCHEMA__.validate_reporting_statement_revision_append()
returns trigger
language plpgsql
as $$
declare
    current_mapping __SCHEMA__.reporting_statement_reconciliation_documents%rowtype;
    previous_revision __SCHEMA__.reporting_statement_reconciliation_document_revisions%rowtype;
begin
    select *
    into current_mapping
    from __SCHEMA__.reporting_statement_reconciliation_documents
    where tenant_id = new.tenant_id
      and company_id = new.company_id
      and workflow_id = new.workflow_id
      and document_key = new.document_key;

    if not found
        or new.document_version is distinct from current_mapping.document_version
        or new.content_hash_sha256 is distinct from current_mapping.content_hash_sha256
        or new.byte_size is distinct from current_mapping.byte_size
        or new.is_immutable is distinct from current_mapping.is_immutable
        or new.mapping_stored_at_utc is distinct from current_mapping.stored_at_utc
        or new.mapping_updated_at_utc is distinct from current_mapping.updated_at_utc then
        raise exception 'statement reconciliation document revision does not match the current authority mapping'
            using errcode = '55000';
    end if;

    if new.document_version = 1 then
        if exists (
            select 1
            from __SCHEMA__.reporting_statement_reconciliation_document_revisions
            where tenant_id = new.tenant_id
              and company_id = new.company_id
              and workflow_id = new.workflow_id
              and document_key = new.document_key) then
            raise exception 'initial statement reconciliation document revision is not the first retained revision'
                using errcode = '55000';
        end if;
    else
        select *
        into previous_revision
        from __SCHEMA__.reporting_statement_reconciliation_document_revisions
        where tenant_id = new.tenant_id
          and company_id = new.company_id
          and workflow_id = new.workflow_id
          and document_key = new.document_key
          and document_version = new.document_version - 1;

        if not found
            or new.previous_content_hash_sha256 is distinct from previous_revision.content_hash_sha256
            or new.previous_byte_size is distinct from previous_revision.byte_size
            or new.previous_updated_at_utc is distinct from previous_revision.mapping_updated_at_utc
            or new.mapping_stored_at_utc is distinct from previous_revision.mapping_stored_at_utc
            or new.is_immutable is distinct from previous_revision.is_immutable then
            raise exception 'statement reconciliation document revision does not extend the retained revision chain'
                using errcode = '55000';
        end if;
    end if;

    return new;
end;
$$;

drop trigger if exists trg_reporting_statement_document_guard
    on __SCHEMA__.reporting_statement_reconciliation_documents;

create trigger trg_reporting_statement_document_guard
before update or delete on __SCHEMA__.reporting_statement_reconciliation_documents
for each row execute function __SCHEMA__.guard_reporting_statement_document_mutation();

drop trigger if exists trg_reporting_statement_document_truncate_guard
    on __SCHEMA__.reporting_statement_reconciliation_documents;

create trigger trg_reporting_statement_document_truncate_guard
before truncate on __SCHEMA__.reporting_statement_reconciliation_documents
for each statement execute function __SCHEMA__.reject_reporting_statement_document_truncate();

drop trigger if exists trg_reporting_statement_document_revision
    on __SCHEMA__.reporting_statement_reconciliation_documents;

create trigger trg_reporting_statement_document_revision
after insert or update on __SCHEMA__.reporting_statement_reconciliation_documents
for each row execute function __SCHEMA__.retain_reporting_statement_document_revision();

drop trigger if exists trg_reporting_statement_revision_append
    on __SCHEMA__.reporting_statement_reconciliation_document_revisions;

create trigger trg_reporting_statement_revision_append
before insert on __SCHEMA__.reporting_statement_reconciliation_document_revisions
for each row execute function __SCHEMA__.validate_reporting_statement_revision_append();

drop trigger if exists trg_reporting_statement_revision_guard
    on __SCHEMA__.reporting_statement_reconciliation_document_revisions;

create trigger trg_reporting_statement_revision_guard
before update or delete on __SCHEMA__.reporting_statement_reconciliation_document_revisions
for each row execute function __SCHEMA__.guard_reporting_statement_revision_mutation();

drop trigger if exists trg_reporting_statement_revision_truncate_guard
    on __SCHEMA__.reporting_statement_reconciliation_document_revisions;

create trigger trg_reporting_statement_revision_truncate_guard
before truncate on __SCHEMA__.reporting_statement_reconciliation_document_revisions
for each statement execute function __SCHEMA__.reject_reporting_statement_revision_truncate();
