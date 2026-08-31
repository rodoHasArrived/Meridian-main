-- Repairs the currency detail V_ledger_026 left null on historical journal legs.
--
-- Two populations carry those nulls. Legs written before V_ledger_026 predate the columns
-- entirely. Legs written after it, but before the normalization fix in #2800, were built with
-- currency detail that the shared validator's dimension rebuild silently dropped on the way to
-- the store -- so what was persisted for them is missing its currency detail too. The append path
-- is fixed going forward; this repairs what is already retained.
--
-- What a repair may assert. The leg's debit/credit are the functional amounts and stay untouched:
-- this migration never rewrites an amount the books balance on. The only fill it applies is the
-- identity translation -- transaction currency equal to the functional currency, transaction
-- amounts equal to the functional amounts, FX rate 1 -- which records the denomination without
-- inventing a rate. It never fabricates a foreign rate, because the original rate is not
-- recoverable from anything retained.
--
-- When that assertion is earned. Only for a leg whose ledger book carries positive, corroborating
-- evidence of single-currency operation: at least one currency-bearing leg, and every one of them
-- an identity translation at the book's own base currency. Silence is not evidence -- a book with
-- no currency-bearing leg at all could have transacted in anything, so it is left null and
-- surfaced instead of guessed. The remaining dispositions in the view below name why each
-- unrepaired leg was refused.

-- Currency-blind legs are the working set for the survey and for every re-run of the repair, and
-- they shrink toward empty as books are repaired. A partial index keeps both cheap.
create index if not exists ix_journal_legs_currency_blind
    on __SCHEMA__.journal_legs (period_id)
    where transaction_currency is null;

-- One row per currency-blind leg, with the disposition that decides whether it can be repaired
-- from retained evidence. The repair below selects from this view rather than restating the rule,
-- so what an operator surveys and what the backfill acts on cannot drift apart.
create or replace view __SCHEMA__.journal_leg_currency_backfill_status as
with book_currency_evidence as (
    select
        b.ledger_book_id,
        -- Base currency is stored as supplied, so normalize it and refuse anything that is not a
        -- three-letter code: the currency check constraint would reject it anyway.
        case
            when upper(trim(b.base_currency)) ~ '^[A-Z]{3}$' then upper(trim(b.base_currency))
        end as base_currency,
        count(l.entry_id) filter (
            where l.transaction_currency is not null
        ) as currency_bearing_legs,
        -- Legs that name a functional currency other than what the book says its base is. The
        -- label itself is then in question, so nothing in the book is safe to stamp with it.
        count(l.entry_id) filter (
            where l.transaction_currency is not null
              and l.functional_currency is distinct from upper(trim(b.base_currency))
        ) as mismatched_functional_legs,
        -- Legs booked in a currency other than the functional one, or translated at a rate other
        -- than 1. One of these anywhere in the book means the book does transact in foreign
        -- currency, and its blind legs may be foreign legs whose rate is gone.
        count(l.entry_id) filter (
            where l.transaction_currency is not null
              and (l.transaction_currency is distinct from l.functional_currency
                   or l.fx_rate_to_functional <> 1)
        ) as foreign_currency_legs
    from __SCHEMA__.ledger_books b
    left join __SCHEMA__.accounting_periods p
        on p.ledger_book_id = b.ledger_book_id
    left join __SCHEMA__.journal_legs l
        on l.period_id = p.period_id
    group by b.ledger_book_id, b.base_currency
)
select
    l.entry_id,
    l.journal_entry_id,
    l.period_id,
    p.ledger_book_id,
    e.base_currency,
    -- Left-joined below, so a leg whose period is missing reports Unknown rather than vanishing.
    coalesce(p.status, 'Unknown') as period_status,
    l.occurred_at,
    case
        -- A period that was never scoped to a ledger book has no authoritative functional
        -- currency to resolve, so there is nothing to stamp these legs with.
        when p.ledger_book_id is null then 'UnresolvedLedgerBook'
        when e.base_currency is null then 'UnusableBaseCurrency'
        when e.mismatched_functional_legs > 0 then 'FunctionalCurrencyMismatch'
        when e.foreign_currency_legs > 0 then 'ForeignCurrencyEvidence'
        -- Nothing contradicts single-currency operation, but nothing corroborates it either.
        -- Repairable only against an operator's explicit affirmation, never automatically.
        when e.currency_bearing_legs = 0 then 'UnaffirmedSingleCurrency'
        else 'Repairable'
    end as disposition
from __SCHEMA__.journal_legs l
-- Left, not inner: journal_legs.period_id carries no foreign key, so an inner join would drop a
-- leg whose period is missing out of the one surface whose job is to make missing detail visible.
-- Such a leg resolves no ledger book and lands in UnresolvedLedgerBook, which is the truth.
left join __SCHEMA__.accounting_periods p
    on p.period_id = l.period_id
left join book_currency_evidence e
    on e.ledger_book_id = p.ledger_book_id
where l.transaction_currency is null;

-- An operator's assertion that a ledger book with no retained currency evidence transacted only
-- in its base currency, recorded as the authority for the legs it completed. Append-only: a book
-- can be affirmed again if legacy legs reappear from a restore, and each attempt keeps its own
-- row with what it actually repaired.
create table if not exists __SCHEMA__.journal_leg_currency_affirmations (
    affirmation_id uuid primary key,
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    affirmed_currency text not null,
    actor text not null,
    rationale text not null,
    affirmed_at timestamptz not null default now(),
    legs_repaired integer not null,
    constraint ck_journal_leg_currency_affirmations_currency
        check (affirmed_currency ~ '^[A-Z]{3}$'),
    constraint ck_journal_leg_currency_affirmations_legs
        check (legs_repaired >= 0)
);

create index if not exists ix_journal_leg_currency_affirmations_book
    on __SCHEMA__.journal_leg_currency_affirmations (ledger_book_id, affirmed_at desc);

comment on table __SCHEMA__.journal_leg_currency_affirmations is
    'Operator assertions that a ledger book with no retained currency evidence transacted only in '
    'its base currency, and the currency-blind journal legs each assertion completed. Append-only: '
    'this is the authority for a repair the data alone could not determine.';

-- The repair. Guarded by transaction_currency is null, so re-running it is a no-op, and scoped to
-- the legs the view judged Repairable. debit/credit are read, never written.
update __SCHEMA__.journal_legs l
set transaction_currency = s.base_currency,
    functional_currency = s.base_currency,
    transaction_debit = l.debit,
    transaction_credit = l.credit,
    fx_rate_to_functional = 1
from __SCHEMA__.journal_leg_currency_backfill_status s
where s.entry_id = l.entry_id
  and s.disposition = 'Repairable'
  and l.transaction_currency is null;
