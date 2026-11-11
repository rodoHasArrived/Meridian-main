# Recurring Journal

**Status:** active guidance
**Owner:** Accounting and Ledger
**Reviewed:** 2026-11-11

A recurring journal schedules a versioned, balanced journal template for a cadence and an exact
tenant, company, fund profile, ledger book, entity and currency. Each effective date identifies one
logical occurrence. That occurrence owns one retained approval draft and its source evidence;
neither a worker restart nor a later definition version creates another draft for the same occurrence.

## Definitions, claims and evidence

`RecurringJournalSchedule` remains the pure calendar primitive. Financial Operations owns
`RecurringScheduleDefinition`, `RecurringTemplateDefinition`, `RecurringOccurrenceRecord` and
`IRecurringJournalStore`. A retained claim copies the full schedule, template, parameters,
dimensions, ownership and source references, including their exact versions and content digests.
Schedule and template versions are part of the claim's provenance, not part of its logical identity.

The file store serializes writers with an OS-exclusive lease across claim, intake and completion.
It writes each state transition before returning. A stopped process releases the lease; a new
runner resumes the claim and asks the existing journal-intake path for the same deterministic draft.
The existing workbench retains draft and audit recovery independently, so interruption after draft
save does not authorize a second draft. A previously completed occurrence whose draft has been lost
blocks until retained draft state is restored.

Every source reference must identify retained evidence, its location, source system, kind, retaining
actor, retention time and a canonical content digest or source version. The draft retains a digest
of its complete recurring provenance. The workbench preserves this provenance during resave and
requires its source references at validation, submission and subsequent lifecycle boundaries.
Retained evidence metadata identifies the source; it does not independently certify source bytes.
Submission and lifecycle commands also acquire the recurring registry lease and compare the draft
with the retained occurrence and active definitions. Missing or corrupt registry state and definition
drift block these commands, including retries of a previously retained command.

## Human approval and locked periods

`AutomatedJournalScheduledWorker` calls `RecurringJournalRunner`, which uses
`AutomatedJournalDraftIntakeService` to enter the existing manual journal workbench. The worker
does not submit, approve or post. Browser and WPF queues consume the same
`RecurringJournalQueueDto`, including exact versions, source references, current approval status,
blockers, period lock owner and governed reopen route.

The PostgreSQL ledger is the authoritative period store. Before intake the runner resolves fund
ownership, book currency, active tenant-owned entity/account membership from retained fund structure,
and exactly one covering period. Missing, inconsistent or unavailable
authority blocks generation. A closed period retains its close actor and points operators to the
existing Accounting Close reopen workflow. Reopening a period requires that workflow's evidence
and authorization; retrying an occurrence cannot reopen it. A later period version never rewrites
the period evidence retained when the draft was created.

## Calculated journals

A template either defines fixed lines or names a posting calculation (`calculation.kind`, with
optional string `inputs`). A calculated template has no lines; the runner asks the registered
calculation for them when the occurrence first becomes a draft. Numeric terms stay in the
schedule's parameters, so a rate or high-water-mark change is a new schedule version. The draft is
an ordinary recurring draft: same occurrence identity, source evidence, approval and posting
flow. Its memo states the inputs the amount came from.

| Kind | Reads | Schedule terms and template inputs |
| --- | --- | --- |
| `management-fee-accrual` | Ledger net assets at the close of the day before the accrual window | `annualRate`, `yearBasisDays` |
| `performance-fee-accrual` | Ledger net assets and the performance fee already payable at the effective date | `performanceFeeRate`, `highWaterMark`, `hurdleAmount`, `crystallize` (0 or 1) |
| `fund-expense-accrual` | Nothing | `annualAmount`, `yearBasisDays`; input `expenseId` |
| `fixed-asset-depreciation` | The fixed-asset register for the fund book | none |
| `fixed-income-accrual` | Security Master cash-flow projection and the position's face-value lots at the effective date | inputs `securityId`, `ticker`, `bookPositionId`, optional `financialAccountId` |
| `fx-revaluation` | Foreign-currency journal legs, prior posted revaluations and the dated FX rate for the effective date | `maxRateAgeDays` |

Rules that keep an amount from being drafted twice:

- **Posted history first.** Management fee, performance fee and FX revaluation read balances
  that earlier occurrences post. Such an occurrence is calculated only after the previous one is
  posted, rejected or needed no draft; until then it is blocked. Two periods that come due
  together therefore cannot both be measured against a ledger that holds neither. A previous
  occurrence that was reversed or rebooked counts only once its correction journal is posted. The previous
  occurrence's retained draft decides, so one that was posted and whose period has since closed
  still counts as settled.
- **Closing balances wait for the day to end.** The runner waits until the effective day ends
  (UTC) before drafting management fees, performance fees, FX revaluations and bond accruals.
  Calculations that read that day's closing balances then include later postings from the same
  day. The accounting date is unchanged.
- **Schedule dimensions apply.** A calculated line keeps the dimensions its calculation set and
  takes the schedule's cost center, strategy and other dimensions for anything left unset.
- **One accrual window per occurrence.** The window ends on the effective date and starts the day
  after the previous occurrence; the first window starts at the covering period's start date.
- **Calculated once.** A retained draft is never recalculated. An occurrence that correctly has
  nothing to post is recorded as `NoDraftRequired` with its reason and is not retried.
- **Performance fee is cumulative.** The fee above the high-water mark and hurdle is compared with
  the performance fee already payable, and only the difference is drafted (a reversal when it has
  fallen). After a fee is crystallized and settled, register a schedule version with the new mark.
- **FX revaluation covers monetary accounts.** That is accounts with no symbol and
  currency-scoped cash accounts such as `Cash (EUR)`. Accounts with an instrument symbol are
  excluded because they can include book-currency fair-value marks. Carrying value combines the
  book-currency amounts of foreign legs with prior posted revaluations for the same account.
  Functional-currency activity on that account is excluded. An account with legs in multiple
  foreign currencies is blocked.
- **A depreciation period belongs to one window.** Each registered asset's scheduled monthly
  charge is drafted by the occurrence whose window contains its period end, and stops at
  retirement. Assets are registered through `/api/ledger/fixed-assets`. A registered asset's cost,
  salvage value, life, method, currency and in-service date cannot be changed, because periods
  already depreciated are not recomputed; retire it and register the revised asset under a new id.
- **A coupon accrues across windows, once.** The bond calculation drafts the day-count share of
  each coupon period that falls inside the window and the premium or discount amortized over the
  same days. Each share is a difference of rounded cumulative amounts, so the windows covering a
  coupon period sum to the coupon exactly. A projection that states no accrual interval for a
  coupon period blocks the occurrence instead of recognizing the coupon whole on its payment
  date, and a bond whose Security Master currency is not the book currency is blocked, because
  its amounts would be drafted unconverted. Coupon accrues only on the lots the book holds: each
  lot earns its face share of the projection's interest from the day it was acquired, and a book
  with no open lot drafts nothing. The runner uses the template's `bookPositionId` to reconstruct
  the lots held at the effective date. Scheduled bond accruals run the following day or later,
  so a template without a book position is blocked.

Terms are never defaulted. A missing term, an unknown kind, an unavailable ledger, a stale or
non-postable cash-flow projection, or an FX rate that is missing, has no quote date and source, or
is older than `maxRateAgeDays` blocks the occurrence with its reason.

**FX rate evidence.** The revaluation memo records, for every rate used, the pair, the rate, the
quote date, whether it was direct, inverse or triangulated, and the rate table with its content
digest. A triangulated rate is dated by its older leg.

**Period close.** Close readiness has a "Recurring and calculated journals" lane whenever the
period has recurring occurrences. It is ready only when each one is posted or recorded as
needing no draft; an unreadable queue is not ready. An occurrence the scheduled worker has not
yet claimed is not visible to the lane. Run the worker after the effective day ends and before
closing the period.

Current limits. Projected principal paydowns are not drafted. The FX rate table
(`reconciliation/fx-rates.json`) is read once at startup and is operator-maintained; it is not a
governed rate store with approvals. Revaluation needs journal legs that record a transaction
currency, and no production posting path writes a foreign-currency leg yet (manual journals are
single-currency and asset postings are functional-only), so until `W12-BOOK-001` adds governed
foreign-currency posting the calculation reports nothing due. The high-water mark and hurdle are
schedule terms an operator maintains; there is no fee-terms store.

`PostingCalculationConnectionTests` fails when a method that builds journal drafts, lines or
ledger postings has no production caller and is not marked
`[PostingCalculationNotYetConnected("reason")]`. Three markers remain, each stating what it is
waiting for: the corporate-action ledger bridge, the short-lot tuple reversal, and the
whole-coupon ledger-postings builder.

## Event-driven calculations

Two posting calculations are events, not recurring accruals, and run through journal-automation
intakes that admit approval drafts the same way:

- **Capital-call default interest**
  (`/api/ledger/journal-automation/capital-call-default-interest-intake`). The called amount and
  receipts come from posted ledger lines, each receipt dated by its own funding journal. The due
  date, rate, day-count convention and grace days
  are operator-attested with retained evidence. Interest is accrued to its cumulative amount
  through the requested date and only the part not already posted is drafted; an earlier
  default-interest draft still awaiting approval blocks a further accrual, a posted reversal of
  an earlier accrual is netted off what counts as posted, a call with a correction journal
  against its receivable (such as a reversed receipt) is blocked, and requests are
  handled one at a time on a host.
- **Distribution waterfall** (`/api/ledger/journal-automation/distribution-intake`). Each
  investor's distribution is split by the European waterfall. The tiers already paid come from
  the distribution register for posted distributions, so return of capital, preferred return and
  GP catch-up are each paid once. An earlier pending distribution blocks the next. Rejected
  distributions and distributions with a posted reversal consume no tiers. A posted distribution
  or rebook with amounts that differ from its retained waterfall blocks the next declaration
  until it is reversed and redeclared under a new id. The draft retains the declaration terms
  and tiers. After an interruption, repeating the original terms restores the register from that
  draft. A legacy draft missing both its retained terms and its register record requires recovery
  of the original register record. Hosts using the same register serialize declarations through
  its file lease. Private-capital activity tracks each investor's draft and posting separately
  under the shared declaration.

## Definition correction and recovery

Updating a schedule or template appends a new definition. If an existing occurrence names different
versions, the next claim becomes `DefinitionChanged` and returns a blocker. It preserves the first
draft and never silently replaces its economics. Explicitly reactivating the exact prior definitions
requires actor and reason and retains an activation record; subsequent retries can recover that
same draft. This restoration path does not mutate a posted journal or create an automatic rebook.
Governed reversal and rebook drafts retain the original recurring evidence and receive their own
posting identity. They can use an unlocked adjustment period while preserving the original effective
date and period in that evidence. The workbench verifies their retained source-and-target relationship;
client-provided correction links cannot substitute for it.

The recurring store requires explicit administrator initialization through the recurring-journal
initialization endpoint. Runtime reads never initialize it. Its persistent initialization marker
distinguishes initial provisioning from loss of an existing snapshot; restore the retained snapshot
and marker together if either becomes unavailable. The supported concurrency boundary is processes
sharing the same filesystem data root. Separate data roots are separate authorities and are not a
distributed scheduling deployment.

## Implementation and regression references

- [Durable definitions and occurrence store](../../src/Meridian.FinancialOperations/FundAdministration/FileRecurringJournalStore.cs)
- [Runner and shared queue projection](../../src/Meridian.Ui.Shared/Services/RecurringJournalRunner.cs)
- [Posting calculations](../../src/Meridian.Ui.Shared/Services/RecurringJournalCalculations.cs)
- [Calculated draft scenarios](../../tests/Meridian.Tests/Ui/RecurringJournalCalculationTests.cs)
- [PostgreSQL golden-file run](../../tests/Meridian.Tests/Storage/RecurringJournalCalculationPostgresTests.cs)
- [Posting-calculation connection guard](../../tests/Meridian.Tests/Architecture/PostingCalculationConnectionTests.cs)
- [Authoritative period resolution](../../src/Meridian.Ui.Shared/Services/RecurringJournalPeriodAuthority.cs)
- [Authoritative subject membership](../../src/Meridian.Ui.Shared/Services/RecurringJournalSubjectAuthority.cs)
- [Recurring evidence guard](../../src/Meridian.Ledger/RecurringJournalEvidence.cs)
- [Lifecycle registry and correction ancestry guard](../../src/Meridian.Ui.Shared/Services/ManualJournalEntryWorkbenchService.Recurring.cs)
- [Store regression scenarios](../../tests/Meridian.Tests/FinancialOperations/RecurringJournalStoreTests.cs)
- [Runner recovery and intake scenarios](../../tests/Meridian.Tests/Ui/RecurringJournalRunnerTests.cs)
- [Lifecycle durable-state and definition checks](../../tests/Meridian.Tests/Ui/RecurringJournalLifecycleTests.cs)
- [PostgreSQL period lock recovery](../../tests/Meridian.Tests/Storage/RecurringJournalPeriodAuthorityPostgresTests.cs)
- [Canonical roadmap status and evidence](../roadmap/data/roadmap-items.yml), item `W10-JRNL-001`.

These references identify the implementation and its regression scope. The pull request carries
actual validation results; implementation evidence does not constitute operator acceptance.
