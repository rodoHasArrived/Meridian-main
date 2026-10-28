-- Migration 045: typed tranche terms on the structured-credit projection.
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- StructuredCredit asset-class terms version 2 adds the securitisation family, the capital-structure
-- position, the loss attachment/detachment points, the collateral-pool statistics (WAC, WAM, WALA)
-- and the CLO period dates as typed, optional terms. The SecurityTermsProjectionRegistry descriptor
-- projects each as a nullable column so the tranche can be partitioned and filtered on them
-- relationally. Additive only: existing rows keep nulls until their next projection write.

alter table __SCHEMA__.structured_credit_projection
    add column if not exists structured_credit_kind text,
    add column if not exists seniority text,
    add column if not exists attachment_point numeric(18, 10),
    add column if not exists detachment_point numeric(18, 10),
    add column if not exists weighted_average_coupon numeric(18, 10),
    add column if not exists weighted_average_maturity_months integer,
    add column if not exists weighted_average_loan_age_months integer,
    add column if not exists reinvestment_period_end date,
    add column if not exists non_call_period_end date;

create index if not exists ix_structured_credit_projection_kind
    on __SCHEMA__.structured_credit_projection (structured_credit_kind);

comment on column __SCHEMA__.structured_credit_projection.structured_credit_kind is
    'Securitisation family (structured-credit-kind taxonomy); null for records written before terms version 2.';
comment on column __SCHEMA__.structured_credit_projection.attachment_point is
    'Loss attachment point as a fraction of the deal balance (0..1).';
comment on column __SCHEMA__.structured_credit_projection.detachment_point is
    'Loss detachment point as a fraction of the deal balance (0..1), at or above attachment_point.';
