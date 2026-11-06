-- Adds explicit transaction-currency detail to journal legs so currency no longer has to be
-- inferred from account symbols. The existing debit/credit columns remain the functional (base)
-- amounts the ledger balances and reports on; these columns are nullable so legacy legs stay
-- currency-blind without a backfill.
alter table __SCHEMA__.journal_legs
    add column if not exists transaction_currency text null,
    add column if not exists functional_currency text null,
    add column if not exists transaction_debit numeric(38, 10) null,
    add column if not exists transaction_credit numeric(38, 10) null,
    add column if not exists fx_rate_to_functional numeric(38, 10) null;

-- When currency detail is present it must be internally consistent: three-letter codes, a
-- single-sided non-negative transaction amount, and a positive FX rate. Either the whole detail
-- is present or none of it is (legacy rows).
alter table __SCHEMA__.journal_legs
    drop constraint if exists ck_journal_legs_currency_detail;
alter table __SCHEMA__.journal_legs
    add constraint ck_journal_legs_currency_detail check (
        (transaction_currency is null
            and functional_currency is null
            and transaction_debit is null
            and transaction_credit is null
            and fx_rate_to_functional is null)
        or (transaction_currency ~ '^[A-Z]{3}$'
            and functional_currency ~ '^[A-Z]{3}$'
            and transaction_debit is not null and transaction_debit >= 0
            and transaction_credit is not null and transaction_credit >= 0
            and ((transaction_debit > 0 and transaction_credit = 0)
                or (transaction_debit = 0 and transaction_credit > 0))
            and fx_rate_to_functional is not null and fx_rate_to_functional > 0)
    );

create index if not exists ix_journal_legs_transaction_currency
    on __SCHEMA__.journal_legs (transaction_currency)
    where transaction_currency is not null;
