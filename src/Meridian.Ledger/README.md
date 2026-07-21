---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-LEDGER
path: src/Meridian.Ledger
status: active
owner_lane: Accounting and Ledger
last_reviewed: 2026-07-10
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
income-statement and balance-sheet rows with net-income and accounting-equation checks;
`BuildForPeriod` additionally derives a direct-method `LedgerCashFlowStatement` (operating /
investing / financing, reconciled to beginning and ending cash) and a
`LedgerPartnersCapitalStatement` roll-forward (beginning capital, contributions, distributions,
allocated result, ending capital per equity account) from the period's journal activity.
`LedgerReportPackBuilder` emits those statements as CSV/JSON pack artifacts, and
`LedgerScheduledReportExportPackageBuilder` now honors every declared `LedgerReportExportFormat`:
Csv, Json, RegulatoryXml natively plus real binary Xlsx/Pdf through the
`ILedgerReportBinaryRenderer` seam (the dependency-free `BuiltInLedgerReportBinaryRenderer` by
default; `Meridian.Documents.FinancialReportDocumentRenderer` supplies branded, deterministic
QuestPDF/ClosedXML output for client delivery).
Ledger legs can carry explicit `LedgerEntryCurrency` (transaction currency, transaction amounts,
and FX rate) alongside the functional debit/credit so currency no longer has to be inferred from
account symbols; the currency-aware `Ledger.PostLines` overload and
`MultiCurrencyJournalProjection.ToCurrencyAwareLedgerLines` post that detail durably.

Fund-ops economics are modeled as pure calculators: `PreferredReturnCalculator` (compounding or
simple preferred return on a contribution timeline), `EuropeanDistributionWaterfall` (return of
capital → preferred return → automatic GP catch-up → carried-interest split),
`CarriedInterestClawbackCalculator` (end-of-life GP giveback), and the share-class/unit register
(`ShareClass`, `ShareClassUnitRegisterProjector`, `NavPerUnitCalculator`, `EqualizationCalculator`)
for unitized NAV-per-unit with single-NAV equalisation. `PrivateCapitalCommitments` plus
`CapitalCallDraftFactory` and `CapitalCallPlanBuilder` add the LP commitment register,
uncalled-commitment roll-forward invariant, and governed capital-call drafting; the
`CommitmentRollForwardCalculator` and `DefaultInterestCalculator` live in
`Meridian.FinancialOperations/PrivateCapital`.
`Ledger.CalculateNetBalance` exposes the ledger-owned normal-balance calculation over the shared
F# posting kernel so storage and reporting projections do not duplicate account-type math.
`MultiCurrencyLedgerTranslator` translates local-currency balances to a base currency and prepares
balanced unrealized FX revaluation journal lines for monetary asset/liability accounts.
`MultiCurrencyJournalProjector` converts local-currency debit/credit journal inputs into balanced
base-currency ledger posting lines while preserving local amount, currency, and FX-rate evidence.
`DailyPortfolioPricingProjector` applies fund-specific valuation policies to listed and OTC daily
marks, preserves price-source/evidence references, and prepares balanced fair-value adjustment
lines against unrealized gain/loss accounts. Valuation policies carry a first-class ASC 820
`FairValueLevel` (Level 1/2/3) and a `StalePricePolicy` (allow/flag/block by max price age); the
Application-layer `WaterfallMarkPriceSource` composes ordered price-source tiers (exchange close →
vendor composite → matrix/model → manual) and stamps the resolving tier's fair-value level.
`DailyPortfolioPricingDraftBuilder` converts that projection into a governed
`AutomatedJournalDraft` (kind `FairValueMarkAdjustment`) with per-mark price evidence so daily
fair-value adjustments flow through `AutomatedJournalApproval` before posting; the
Application-layer `DailyMarkToMarketService` wires provider-chain close prices into this path.
`FixedIncomeAmortizationProjector` produces balanced coupon accrual, discount accretion, and
premium amortization lines for bond accounting workflows before persistence or approval posting.
`FixedAssetDepreciationProjector` is the depreciation analogue: it projects balanced
depreciation-expense/accumulated-depreciation (contra-asset) lines scoped per fixed asset.
`DepreciationScheduleCalculator` builds the period-by-period schedule (straight-line,
declining-balance with an auto-switch to straight-line and salvage floor, and usage-driven
units-of-production), always depreciating to salvage value with the final period absorbing rounding.
`FixedAssetDepreciationDraftBuilder` batches all of a period's per-asset projections into a single
governed `AutomatedJournalDraft` (kind `DepreciationPosted`) so one approval covers the whole
depreciation run, mirroring `DailyPortfolioPricingDraftBuilder`.
`LedgerAccountTaxLotPolicyBook` resolves FIFO/LIFO/HIFO/SpecificId/AverageCost relief methods at the
ledger account level so accounting statements can follow front-office lot-relief policy.
`LedgerTaxLotReliefProjector` applies those account-level relief methods to open tax lots and
prepares balanced cash, security cost-basis, and realized gain/loss lines before durable posting.
For `AverageCost` it pools every open lot into a single average unit cost while still depleting lots
oldest-first for deterministic lot closing. When a `WashSalePolicy` and replacement acquisitions are
supplied, it defers the proportional disallowed loss on a realizing sale (US IRC §1091), recognizing
only the allowed portion and capitalizing the deferred amount into the replacement lot's basis
(`WashSaleOutcome`), so the entry still balances and no premature loss is booked.
`LedgerTaxLot` carries an optional `SecurityId` so cost-basis lots link to Security Master
reference data. `LedgerTaxLotBasisAdjuster` (fed via `LedgerTaxLotReliefInput.BasisAdjustments`)
restates open lots by reference-data-derived `LedgerTaxLotBasisAdjustment`s — corporate-action
splits and return of capital, pool-factor paydowns, and day-count premium/discount amortization —
before relief, keeping the ledger engine free of Security Master contracts while relieving basis
against the effective lots. The Application-layer `SecurityMasterCostBasisAdjustmentService` reads
the master and produces those adjustments; `SecurityMasterAmortizationLedgerBridge` posts the
matching coupon-accrual, premium-amortization/discount-accretion, and principal-paydown journal
entries so cash flow projections are booked instead of remaining display-only.
`AutomatedJournalDraftProjector` produces balanced drafts for dividend declarations, dividend
receipts, cash-interest credits, corporate-action cash events, and recurring accrual obligations
such as management fees, performance fees, commissions, and withholding taxes before workflow
approval/posting.
`AutomatedJournalApproval` governs those drafts through submit, approve, reject, and post
transitions, requiring approval/posting evidence and preserving approval metadata on the posted
ledger journal entry.
`LedgerJournalReversal` is the general correction primitive: it books a balanced reversing entry
for any posted journal by swapping every line's debit and credit under a new journal id, linking
back to the original via a `reversal.of` tag — so corrections stay immutable (reverse/rebook) rather
than mutating posted history. The Application-layer `SecurityMasterLedgerBridge` uses it (opt-in via
`CorporateActionLedgerPostingContext.AutoReverseSupersededPostings`) to automatically reverse a
previously-posted corporate action when it is cancelled, and to reverse-then-rebook when it is
amended.
`IAutomatedJournalPostingTarget` is the shared target contract for approved automated journal
projections: backtests and what-if runs can post through `InMemoryAutomatedJournalPostingTarget`,
while durable implementations can append the same approved projection to the governed journal
store without forking projector output.
`LockedAccountingPeriodBook` records book-scoped accounting period locks and rejects late journal
postings that fall inside a locked range, preserving published NAV and close evidence while still
allowing separate books such as shadow-NAV ledgers to continue independently.
`PeriodCloseProjector` turns a point-in-time trial balance into balanced closing entries: every
revenue and expense account is zeroed and the net income is rolled into retained earnings, scoped
per financial account. `PeriodCloseDraftBuilder` wraps that projection in a governed
`AutomatedJournalDraft` (kind `PeriodCloseClosingEntries`) so closes post through
`AutomatedJournalApproval` instead of remaining status-only. Its idempotency key includes a stable
fingerprint of the temporary-account residual and full line-dimension scope: an unchanged retry
reuses the retained draft, while a late adjustment produces a distinct draft for only the remaining
closing delta without collapsing fund, entity, sleeve, or other dimensions.
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
and supplied realized-gain tax-lot projections. `LedgerReportPackRequest` can carry a
`LedgerLineDimensionSet` so generated statements, manifests, and line-provenance artifacts remain
fund/entity/sleeve plus organization, portfolio, book, account, customer, vendor, project, and
external-GL scoped instead of emitting all same-account ledger entries.
`LedgerReportSchedulePlanner` projects monthly, quarterly, or annual report schedules into
period-bounded export occurrences and dimension-scoped report-pack requests for regulatory,
investor, or internal stakeholder delivery workflows.
`LedgerScheduledReportExportPackageBuilder` turns a signed report pack plus one scheduled
occurrence into a delivery manifest and regulator-facing XML summary artifact when requested by the
schedule, preserving recipients, due date, requested formats, report-pack signature, dimension
scope, and statement totals without claiming full XBRL/iXBRL coverage.
`JournalEntry` remains the authoritative accounting aggregate after posting and owns its balanced
child `LedgerEntry` lines. Broader business `Transaction` and operational-event records can explain
or initiate posting intent, but they are not aliases for the journal and do not become accounting
truth without the governed append path.
Asset Accounting lifecycle terminology follows the same boundary: Expected, Projected, Drafted,
and Approved do not imply journal impact. Posted requires the immutable journal id, ledger book,
period, basis, currency, balanced line amounts, and Posted status. Reconciled and Reported are later
evidence-bearing states over that journal fact, not replacements for it; reporting publication
remains a separate governed outcome.
`JournalEntryMetadata`, `JournalEvidenceReference`, and `LedgerQuery` now carry treasury-ledger audit context for private-capital
and payment-linked postings: effective date, idempotency key, fund event, capital account, investor,
payment intent, settlement references, and typed retained evidence references. Keep those fields additive and metadata-owned so ledger
consumers can reconstruct capital-call, distribution, subscription, redemption, LP-transfer, and
management-fee postings without introducing UI- or storage-specific query forks.
`LedgerEntry` also carries an optional `LedgerLineDimensionSet` so fund, entity, strategy,
investor, capital-account, instrument, book-position, tax-lot, cost-center, counterparty,
organization, portfolio, book, customer/vendor/project, and external-GL scope can live on the
immutable ledger line itself instead of only in journal-level metadata. Storage and reporting
surfaces should prefer the line-level dimension set when present and use metadata-derived dimensions
only as legacy fallback.
`LedgerLineDimensionSet.PositionId` is additive beside `InstrumentId` and participates in
normalization, matching, report-pack scope, scheduled exports, and balance/report filtering. It
identifies the book-position lineage of a line; it does not make a position projection or balance
snapshot a second ledger.
`LedgerQuery`, `TrialBalance`, `TrialBalanceAsOf`, and `LedgerFinancialStatementBuilder` accept
optional `LedgerLineDimensionSet` filters so core journal, trial-balance, and statement reads can be
scoped by fund/entity/strategy/instrument/counterparty, organization, portfolio, book, account,
customer, vendor, project, and external-GL dimensions without building a UI- or storage-specific
reporting fork.
Ledger-owned dimension normalization trims scope values, removes empty dimensions, and
deduplicates external-GL fields before matching, report-pack manifests, and scheduled-export
manifests so in-memory reporting behavior stays aligned with durable journal storage.
This semantic extension uses existing journal and dimension envelopes. It adds no ledger aggregate,
lineage table, balance store, or posting API; candidate and projection records remain drafts or
rebuildable views over immutable journal facts.
`ProjectLedgerBook` and `FundLedgerBook` propagate the same line-dimension scope through
consolidated trial balances, point-in-time snapshots, reconciliation snapshots, account summaries,
and consolidated journals so multi-book reporting can stay ledger-book-native without dropping
fund/entity/sleeve, neutral operational, or external-GL dimensional filters.
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
| `W9-ASSET-010` | Asset Accounting Event Spine and atomic lot posting |
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
