-- Fund Structure PostgreSQL schema
-- Schema placeholder: __SCHEMA__

CREATE SCHEMA IF NOT EXISTS __SCHEMA__;

-- Organizations
CREATE TABLE IF NOT EXISTS __SCHEMA__.organization (
    organization_id     uuid            NOT NULL PRIMARY KEY,
    code                text            NOT NULL,
    name                text            NOT NULL,
    base_currency       text            NOT NULL,
    is_active           boolean         NOT NULL DEFAULT true,
    effective_from      timestamptz     NOT NULL,
    effective_to        timestamptz     NULL,
    business_ids        jsonb           NOT NULL DEFAULT '[]'::jsonb,
    description         text            NULL,
    updated_at          timestamptz     NOT NULL DEFAULT now()
);

-- Businesses
CREATE TABLE IF NOT EXISTS __SCHEMA__.business (
    business_id             uuid            NOT NULL PRIMARY KEY,
    organization_id         uuid            NOT NULL,
    business_kind           text            NOT NULL,
    code                    text            NOT NULL,
    name                    text            NOT NULL,
    base_currency           text            NOT NULL,
    is_active               boolean         NOT NULL DEFAULT true,
    effective_from          timestamptz     NOT NULL,
    effective_to            timestamptz     NULL,
    client_ids              jsonb           NOT NULL DEFAULT '[]'::jsonb,
    fund_ids                jsonb           NOT NULL DEFAULT '[]'::jsonb,
    investment_portfolio_ids jsonb          NOT NULL DEFAULT '[]'::jsonb,
    description             text            NULL,
    updated_at              timestamptz     NOT NULL DEFAULT now()
);

-- Clients
CREATE TABLE IF NOT EXISTS __SCHEMA__.client (
    client_id               uuid            NOT NULL PRIMARY KEY,
    business_id             uuid            NOT NULL,
    code                    text            NOT NULL,
    name                    text            NOT NULL,
    base_currency           text            NOT NULL,
    is_active               boolean         NOT NULL DEFAULT true,
    effective_from          timestamptz     NOT NULL,
    effective_to            timestamptz     NULL,
    investment_portfolio_ids jsonb          NOT NULL DEFAULT '[]'::jsonb,
    description             text            NULL,
    client_segment_kind     text            NOT NULL DEFAULT 'Unspecified',
    updated_at              timestamptz     NOT NULL DEFAULT now()
);

-- Funds
CREATE TABLE IF NOT EXISTS __SCHEMA__.fund (
    fund_id                 uuid            NOT NULL PRIMARY KEY,
    business_id             uuid            NULL,
    code                    text            NOT NULL,
    name                    text            NOT NULL,
    base_currency           text            NOT NULL,
    is_active               boolean         NOT NULL DEFAULT true,
    effective_from          timestamptz     NOT NULL,
    effective_to            timestamptz     NULL,
    sleeve_ids              jsonb           NOT NULL DEFAULT '[]'::jsonb,
    vehicle_ids             jsonb           NOT NULL DEFAULT '[]'::jsonb,
    entity_ids              jsonb           NOT NULL DEFAULT '[]'::jsonb,
    investment_portfolio_ids jsonb          NOT NULL DEFAULT '[]'::jsonb,
    account_ids             jsonb           NOT NULL DEFAULT '[]'::jsonb,
    description             text            NULL,
    updated_at              timestamptz     NOT NULL DEFAULT now()
);

-- Sleeves
CREATE TABLE IF NOT EXISTS __SCHEMA__.sleeve (
    sleeve_id               uuid            NOT NULL PRIMARY KEY,
    fund_id                 uuid            NOT NULL,
    code                    text            NOT NULL,
    name                    text            NOT NULL,
    mandate                 text            NULL,
    is_active               boolean         NOT NULL DEFAULT true,
    effective_from          timestamptz     NOT NULL,
    effective_to            timestamptz     NULL,
    strategy_ids            jsonb           NOT NULL DEFAULT '[]'::jsonb,
    investment_portfolio_ids jsonb          NOT NULL DEFAULT '[]'::jsonb,
    account_ids             jsonb           NOT NULL DEFAULT '[]'::jsonb,
    updated_at              timestamptz     NOT NULL DEFAULT now()
);

-- Vehicles
CREATE TABLE IF NOT EXISTS __SCHEMA__.vehicle (
    vehicle_id              uuid            NOT NULL PRIMARY KEY,
    fund_id                 uuid            NOT NULL,
    legal_entity_id         uuid            NOT NULL,
    code                    text            NOT NULL,
    name                    text            NOT NULL,
    base_currency           text            NOT NULL,
    is_active               boolean         NOT NULL DEFAULT true,
    effective_from          timestamptz     NOT NULL,
    effective_to            timestamptz     NULL,
    investment_portfolio_ids jsonb          NOT NULL DEFAULT '[]'::jsonb,
    account_ids             jsonb           NOT NULL DEFAULT '[]'::jsonb,
    description             text            NULL,
    updated_at              timestamptz     NOT NULL DEFAULT now()
);

-- Legal Entities
CREATE TABLE IF NOT EXISTS __SCHEMA__.legal_entity (
    entity_id               uuid            NOT NULL PRIMARY KEY,
    entity_type             text            NOT NULL,
    legal_form              text            NOT NULL DEFAULT 'Unspecified',
    lifecycle_status        text            NOT NULL DEFAULT 'Active',
    code                    text            NOT NULL,
    name                    text            NOT NULL,
    jurisdiction            text            NOT NULL,
    base_currency           text            NOT NULL,
    is_active               boolean         NOT NULL DEFAULT true,
    effective_from          timestamptz     NOT NULL,
    effective_to            timestamptz     NULL,
    registration_number     text            NULL,
    beneficial_owners       jsonb           NOT NULL DEFAULT '[]'::jsonb,
    lifecycle_events        jsonb           NOT NULL DEFAULT '[]'::jsonb,
    description             text            NULL,
    updated_at              timestamptz     NOT NULL DEFAULT now()
);

ALTER TABLE __SCHEMA__.legal_entity
    ADD COLUMN IF NOT EXISTS legal_form text NOT NULL DEFAULT 'Unspecified',
    ADD COLUMN IF NOT EXISTS lifecycle_status text NOT NULL DEFAULT 'Active',
    ADD COLUMN IF NOT EXISTS registration_number text NULL,
    ADD COLUMN IF NOT EXISTS beneficial_owners jsonb NOT NULL DEFAULT '[]'::jsonb,
    ADD COLUMN IF NOT EXISTS lifecycle_events jsonb NOT NULL DEFAULT '[]'::jsonb;

-- Investment Portfolios
CREATE TABLE IF NOT EXISTS __SCHEMA__.investment_portfolio (
    investment_portfolio_id uuid            NOT NULL PRIMARY KEY,
    business_id             uuid            NOT NULL,
    code                    text            NOT NULL,
    name                    text            NOT NULL,
    base_currency           text            NOT NULL,
    is_active               boolean         NOT NULL DEFAULT true,
    effective_from          timestamptz     NOT NULL,
    effective_to            timestamptz     NULL,
    client_id               uuid            NULL,
    fund_id                 uuid            NULL,
    sleeve_id               uuid            NULL,
    vehicle_id              uuid            NULL,
    entity_id               uuid            NULL,
    account_ids             jsonb           NOT NULL DEFAULT '[]'::jsonb,
    description             text            NULL,
    updated_at              timestamptz     NOT NULL DEFAULT now()
);

-- Ownership Links
CREATE TABLE IF NOT EXISTS __SCHEMA__.ownership_link (
    ownership_link_id       uuid            NOT NULL PRIMARY KEY,
    parent_node_id          uuid            NOT NULL,
    child_node_id           uuid            NOT NULL,
    relationship_type       text            NOT NULL,
    ownership_percent       numeric(18,8)   NULL,
    is_primary              boolean         NOT NULL DEFAULT false,
    effective_from          timestamptz     NOT NULL,
    effective_to            timestamptz     NULL,
    notes                   text            NULL,
    updated_at              timestamptz     NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_ownership_link_parent ON __SCHEMA__.ownership_link (parent_node_id);
CREATE INDEX IF NOT EXISTS ix_ownership_link_child  ON __SCHEMA__.ownership_link (child_node_id);

-- Fund Structure Assignments
CREATE TABLE IF NOT EXISTS __SCHEMA__.fund_structure_assignment (
    assignment_id           uuid            NOT NULL PRIMARY KEY,
    node_id                 uuid            NOT NULL,
    assignment_type         text            NOT NULL,
    assignment_reference    text            NOT NULL,
    effective_from          timestamptz     NOT NULL,
    effective_to            timestamptz     NULL,
    is_primary              boolean         NOT NULL DEFAULT false,
    updated_at              timestamptz     NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_fund_structure_assignment_node ON __SCHEMA__.fund_structure_assignment (node_id);
