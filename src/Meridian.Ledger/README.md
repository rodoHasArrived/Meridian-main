---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-LEDGER
path: src/Meridian.Ledger
status: active
owner_lane: Governance and Ledger
last_reviewed: 2026-05-20
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

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-LEDGER -->
| Roadmap item | Title |
| --- | --- |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W4-RPT-001` | Governed report pack readiness |
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
