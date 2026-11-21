CREATE SCHEMA IF NOT EXISTS __SCHEMA__;
CREATE TABLE IF NOT EXISTS __SCHEMA__.item_metadata (
    tenant_id text NOT NULL,
    company_id text NOT NULL,
    work_item_id text NOT NULL,
    state jsonb NOT NULL,
    PRIMARY KEY (tenant_id, company_id, work_item_id)
);
