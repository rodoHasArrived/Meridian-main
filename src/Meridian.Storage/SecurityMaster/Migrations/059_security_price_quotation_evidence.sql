-- Legacy observations remain unspecified. New metadata is part of same-timestamp idempotence.
alter table __SCHEMA__.security_raw_prices
    add column if not exists quotation_evidence jsonb;
comment on column __SCHEMA__.security_raw_prices.quotation_evidence is
    'Provider-declared clean/dirty basis, quotation convention, quote status and explicit settlement facts. Null means unknown.';

-- New retained quotations preserve the CLR decimal exactly. Old rounded observations remain
-- original evidence and are never reconstructed or replaced by this widening migration.
alter table __SCHEMA__.security_raw_prices alter column price type numeric;
