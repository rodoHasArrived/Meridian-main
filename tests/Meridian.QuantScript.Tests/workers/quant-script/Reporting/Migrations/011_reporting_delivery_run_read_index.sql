create index if not exists ix_reporting_delivery_jobs_tenant_run
    on __SCHEMA__.reporting_delivery_jobs (
        tenant_id,
        (release_authorization ->> 'runId'),
        created_at_utc desc,
        job_id);
