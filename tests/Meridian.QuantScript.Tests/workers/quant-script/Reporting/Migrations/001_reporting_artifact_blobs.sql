create table if not exists __SCHEMA__.reporting_artifact_blobs (
    tenant_id text not null,
    content_hash_sha256 text not null,
    byte_size bigint not null,
    content bytea not null,
    stored_at_utc timestamptz not null default now(),
    primary key (tenant_id, content_hash_sha256),
    constraint ck_reporting_artifact_tenant_trimmed
        check (tenant_id = btrim(tenant_id) and length(tenant_id) between 1 and 256),
    constraint ck_reporting_artifact_hash_sha256
        check (content_hash_sha256 ~ '^[0-9a-f]{64}$'),
    constraint ck_reporting_artifact_positive_size
        check (byte_size > 0),
    constraint ck_reporting_artifact_content_size
        check (octet_length(content) = byte_size)
);

create index if not exists ix_reporting_artifact_blobs_stored_at
    on __SCHEMA__.reporting_artifact_blobs (tenant_id, stored_at_utc desc);

create or replace function __SCHEMA__.reject_reporting_artifact_blob_mutation()
returns trigger
language plpgsql
as $$
begin
    raise exception 'reporting artifact blobs are immutable'
        using errcode = '55000';
end;
$$;

drop trigger if exists trg_reporting_artifact_blobs_immutable
    on __SCHEMA__.reporting_artifact_blobs;

create trigger trg_reporting_artifact_blobs_immutable
before update or delete on __SCHEMA__.reporting_artifact_blobs
for each row execute function __SCHEMA__.reject_reporting_artifact_blob_mutation();
