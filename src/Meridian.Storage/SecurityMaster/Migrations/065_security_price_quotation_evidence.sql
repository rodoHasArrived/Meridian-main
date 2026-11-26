-- Additive evidence only: legacy observations retain their original bounded price and unknown facts.
alter table __SCHEMA__.security_raw_prices
    add column if not exists quotation_evidence jsonb;
comment on column __SCHEMA__.security_raw_prices.quotation_evidence is
    'Provider-declared clean/dirty basis, quotation convention, quote status and explicit settlement facts. Null means unknown.';

-- New writers retain exact CLR decimal input separately from the numeric(28,10) compatibility
-- mirror. Existing rows remain null: no historical digits are inferred or backfilled.
alter table __SCHEMA__.security_raw_prices
    add column if not exists price_exact numeric;
comment on column __SCHEMA__.security_raw_prices.price_exact is
    'Exact price retained by the current writer. Null selects the unchanged legacy price; never backfilled from rounded observations.';
