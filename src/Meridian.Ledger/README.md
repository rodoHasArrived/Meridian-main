---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-LEDGER
path: src/Meridian.Ledger
status: active
owner_lane: Accounting and Ledger
last_reviewed: 2026-06-08
---

# src/Meridian.Ledger

## Purpose

Ledger provides accounting and books support for reconciliation, governed reporting, and fund operations.

## Layer responsibility

This layer should model ledger behavior and accounting evidence without owning UI presentation or storage engine mechanics.

## Key folders and files

- `Meridian.Ledger.csproj` - ledger project boundary.
- Ledger models, accounting services, and reconciliation support files.

## Important workflows

Use this module for books, ledger behavior, reconciliation evidence, and accounting workflow support.
`ChartOfAccounts` supports customizable colon-delimited account hierarchies such as
`Assets:Cash:Brokerage` and can roll flat trial-balance output up to parent accounts for
fund/accounting reports.
`LedgerFinancialStatementBuilder` projects current or point-in-time trial balances into
income-statement and balance-sheet rows with net-income and accounting-equation checks.
`MultiCurrencyLedgerTranslator` translates local-currency balances to a base currency and prepares
balanced unrealized FX revaluation journal lines for monetary asset/liability accounts.
`MultiCurrencyJournalProjector` converts local-currency debit/credit journal inputs into balanced
base-currency ledger posting lines while preserving local amount, currency, and FX-rate evidence.
`DailyPortfolioPricingProjector` applies fund-specific valuation policies to listed and OTC daily
marks, preserves price-source/evidence references, and prepares balanced fair-value adjustment
lines against unrealized gain/loss accounts.
`FixedIncomeAmortizationProjector` produces balanced coupon accrual, discount accretion, and
premium amortization lines for bond accounting workflows before persistence or approval posting.
`LedgerAccountTaxLotPolicyBook` resolves FIFO/LIFO/HIFO/SpecificId relief methods at the ledger
account level so accounting statements can follow front-office lot-relief policy.
`LedgerTaxLotReliefProjector` applies those account-level relief methods to open tax lots and
prepares balanced cash, security cost-basis, and realized gain/loss lines before durable posting.
`AutomatedJournalDraftProjector` produces balanced drafts for dividend declarations, dividend
receipts, cash-interest credits, corporate-action cash events, and recurring accrual obligations
such as management fees, performance fees, commissions, and withholding taxes before workflow
approval/posting.
`AutomatedJournalApproval` governs those drafts through submit, approve, reject, and post
transitions, requiring approval/posting evidence and preserving approval metadata on the posted
ledger journal entry.
`LockedAccountingPeriodBook` records book-scoped accounting period locks and rejects late journal
postings that fall inside a locked range, preserving published NAV and close evidence while still
allowing separate books such as shadow-NAV ledgers to continue independently.
`ShadowNavValidator` compares actual and shadow ledger books at a point in time, reports
account-level and NAV variances against configured tolerances, and prepares a governed override
draft when independent fund-admin validation requires review.
`PartnershipInvestorAccountingProjector` projects management fees, performance fees, high-water
marks, and investor/feeder/SPV capital allocations into balanced journal lines for partnership
accounting review.
`PartnershipWaterfallProjector` allocates distributable profit through ordered preferred-return,
catch-up, carry, or residual tiers, preserving tier-level investor evidence and producing balanced
investor-capital journal lines.
`LedgerReportPackBuilder` turns point-in-time financial statements into signed trial-balance,
income-statement, balance-sheet, financial-statements JSON, tax-lot realized-gains CSV,
line-provenance, and manifest artifacts for locked-period financial reporting handoff to
persistence, approval, publication, restatement, archive, or export workflows. Report packs retain
lifecycle events, restatement approvals, changed-line keys, and evidence links so published values
can be traced back to ledger entries, source runs/sessions, reconciliation or approval evidence,
and supplied realized-gain tax-lot projections.
`LedgerReportSchedulePlanner` projects monthly, quarterly, or annual report schedules into
period-bounded export occurrences and report-pack requests for regulatory, investor, or internal
stakeholder delivery workflows.
`LedgerScheduledReportExportPackageBuilder` turns a signed report pack plus one scheduled
occurrence into a delivery manifest and regulator-facing XML summary artifact when requested by the
schedule, preserving recipients, due date, requested formats, report-pack signature, and statement
totals without claiming full XBRL/iXBRL coverage.
`JournalEntryMetadata`, `JournalEvidenceReference`, and `LedgerQuery` now carry treasury-ledger audit context for private-capital
and payment-linked postings: effective date, idempotency key, fund event, capital account, investor,
payment intent, settlement references, and typed retained evidence references. Keep those fields additive and metadata-owned so ledger
consumers can reconstruct capital-call, distribution, subscription, redemption, LP-transfer, and
management-fee postings without introducing UI- or storage-specific query forks.
`LedgerEntry` also carries an optional `LedgerLineDimensionSet` so fund, entity, strategy,
investor, capital-account, instrument, tax-lot, cost-center, counterparty, organization, portfolio,
book, customer/vendor/project, and external-GL scope can live on the immutable ledger line itself
instead of only in journal-level metadata. Storage and reporting surfaces should prefer the
line-level dimension set when present and use metadata-derived dimensions only as legacy fallback.
`LedgerQuery`, `TrialBalance`, `TrialBalanceAsOf`, and `LedgerFinancialStatementBuilder` accept
optional `LedgerLineDimensionSet` filters so core journal, trial-balance, and statement reads can be
scoped by fund/entity/strategy/instrument/counterparty and external-GL dimensions without building a
UI- or storage-specific reporting fork.
`ProjectLedgerBook` and `FundLedgerBook` propagate the same line-dimension scope through
consolidated trial balances, point-in-time snapshots, reconciliation snapshots, account summaries,
and consolidated journals so multi-book reporting can stay ledger-book-native without dropping
fund/entity/sleeve or external-GL dimensional filters.
`PrivateCapitalFundEventLedgerProjector` reconstructs a posted Fund Event Ledger view from those
journal entries and optional ledger report packs. It groups private-capital journal entries by fund
event, exposes balanced ledger impact rows, capital-account subledger impact, retained evidence,
approval metadata, reconstruction issues, and report-output links so downstream Reporting, WPF,
and browser surfaces can consume one ledger-owned model instead of rebuilding private-capital
event state locally. Published report-pack links remain publication facts, but the projector only
marks a fund event report-ready when the event is also posting-ready across approval, evidence,
balanced ledger impact, capital-account impact, and at least one published report output with
retained report evidence. If journal entries grouped under one fund-event id disagree on event type,
capital account, investor, effective date, approval state, idempotency key, payment intent, or
settlement reference, the projector emits critical reconstruction issues and keeps the event out of
posting-ready status instead of silently merging incompatible capital-account subledger rows.
Capital-account impact rows preserve each journal entry's
capital-account and investor metadata before falling back to the event-level identity, so blocked
conflicts remain auditable without making them report-ready.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-LEDGER -->
| Roadmap item | Title |
| --- | --- |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W4-RPT-001` | Governed report pack readiness |
| `W5-ACCT-001` | Accounting records and operational evidence |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-LEDGER -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
```

## Change rules

Preserve auditability and evidence links when changing ledger or reconciliation behavior.

## Related docs

- `docs/status/contract-compatibility-matrix.md`
- `docs/source/generated/source-roadmap-traceability.md`
