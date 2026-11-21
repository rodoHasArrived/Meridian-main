# Financial values and Amount passports

**Status:** active
**Placement:** supporting
**Owner:** Workstation Shell and UX
**Reviewed:** 2026-11-21

`FinancialValue` is the browser entry point for a financial value, its source condition,
and optional exact scoped Amount passport. New consumers should use this component rather
than attach an independent evidence interaction to formatted text.

Pass retained decimal text where available. The formatter and trial-balance presentation
sum decimal coefficients with `BigInt`; decimal strings never pass through `Number`.
Presentation rounding is half away from zero and follows the shared
`CurrencyMinorUnits` rules. Unit quantities should pass their retained precision explicitly.
Legacy numeric reads remain supported but cannot recover precision already lost in transit.

`value: null` renders **Not recorded**, distinct from numeric zero. Use `not-applicable`
only when the source establishes that condition. Conditions must come from source-backed
assessment, never from the presence of a value. Omitted conditions render **Unavailable**
with an explanation. Mark eligibility is not reconciliation or accounting approval.

A passport requires an exact `LedgerAmountSelection` identifying retained subject, fund,
book, period, amount, and currency. The displayed value and currency must match the selection
before a trigger is enabled; the existing shared proof drawer revalidates returned scope and
retained evidence. Record links, symbols, account labels, and equal amounts cannot establish
an amount binding. Existing record-level Number Passports remain explicitly unverified.

The Holdings and trial-balance reads expose additive exact fields alongside compatible
display/numeric fields. Trial-balance row passports bind the full authenticated accounting scope,
highest immutable journal sequence, row/dimension identity, population digest, and exact contributor
IDs. Opening the passport reproduces that checkpoint through the canonical storage projection and
revalidates every contributor's retained source evidence. Later postings do not replace the
population already selected. Missing support blocks the complete amount; a verified subset cannot
stand in for the whole balance. Derived table totals retain explicit unavailable evidence state.
Holdings requires a source-owned retained position/mark snapshot before an amount passport can be
enabled; no amount-proof identity is created from a name, symbol, or displayed value.

During migration, legacy `AmountCell` supports its existing statement conventions, but new
nullable values must use `FinancialValue`. Do not use zero-as-dash for nullable amounts.

Focused coverage: `exact-financial-value.test.ts`, `financial-value.test.tsx`,
`TrialBalanceTable.test.tsx`, and `FinancialValueReadContractTests`.
