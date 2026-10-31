-- Preserve the shared schedule parser's full decimal values in relational read models.
-- The old numeric(28,10)/numeric(18,10) limits silently rounded fractional payments and
-- factors, and rejected principal amounts representable by the canonical decimal contract.
-- Removing the typmod widens both columns without changing retained values or source JSON.
-- Previously rounded projections are not reconstructed by DDL; a normal governed replay
-- from retained source terms supplies their exact values.
alter table __SCHEMA__.direct_loan_principal_schedule_projection
    alter column amount type numeric;

alter table __SCHEMA__.structured_credit_factor_schedule_projection
    alter column factor type numeric;
