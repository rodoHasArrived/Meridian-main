# Investor performance and capital-statement pack blueprint

**Roadmap rows:** `W10-PERF-001` (primary), connecting `W9-NAV-006`, `W12-CAPITAL-001`,
`W9-REPORT-005`; follow-on `W11-STAKE-001`.
**Owner lane:** Accounting and Ledger (performance); Platform Security and Governance (stakeholder access).
**Status:** Slice 1 implemented. Slices 2–5 are design only. This document confers no acceptance.

## Outcome

A fund manager produces one quarterly investor pack in which returns, capital account, fees and NAV
agree because every figure comes from the same posted books. An LP who sees a 2.3% Q3 net return can
open the delivered package and see it tie to their capital-account movements. They can then drill,
read-only, from that line to its proof without emailing the operator.

## Non-negotiables carried from the roadmap

- **One performance seam.** The brokerage endpoint `GET /api/fund-accounts/{id}/performance` is
  *extended*, not replaced or paralleled. Fund, class and investor figures reuse the same kernel and
  the same `PerformanceReturnFigureDto` contract.
- **Every figure is labeled:** convention, status (`Reported` / `ProForma` / `ReviewRequired`),
  period, currency, source, reasons. `Value` is null whenever no single number may be shown.
- **Fail closed.** A figure is review-required, never silently wrong, if any of these hold:
  - inputs are in mixed currencies,
  - a translation rate is missing,
  - the series has no money-weighted rate, or more than one,
  - the opening value or capital base is not positive,
  - there are duplicate valuation dates,
  - a cash item is unclassified.

  Unposted activity is labeled pro-forma.
- **Mark discipline.** Valuation points must come from valuations that passed the `W10-MARK-001`
  freshness policy. A period with a mark gap blocks instead of interpolating (tracker `PRD-005`).

## Slice 1 — shared kernel and brokerage-seam disposition (implemented)

| Piece | Path |
| --- | --- |
| Return kernel (pure F#) | `src/Meridian.FSharp.Ledger/ReturnMeasurement.fs` |
| Contract | `src/Meridian.Contracts/Workstation/PerformanceReturnDtos.cs` |
| Kernel → contract mapper, brokerage flow classification | `src/Meridian.Ui.Shared/Services/PerformanceReturnFigures.cs` |
| Seam extension (`ReturnFigures` on `BrokeragePortfolioPerformanceDto`) | `src/Meridian.Ui.Shared/Services/BrokeragePortfolioSyncService.cs` |
| Golden kernel tests | `tests/Meridian.FSharp.Tests/ReturnMeasurementTests.fs` |
| Seam tests | `tests/Meridian.Tests/Ui/PerformanceReturnFiguresTests.cs`, `BrokeragePortfolioSyncServiceTests.cs` |

**Why `Meridian.FSharp.Ledger`.** It already hosts the accepted `FundEconomics` kernel, it has no
dependencies, and `Meridian.Ledger`, `Meridian.FinancialOperations` and `Meridian.Ui.Shared` already
reference it. The existing XIRR (`Meridian.Backtesting/Metrics/XirrCalculator.cs`) is `internal` to
backtesting, so those lanes can't reach it. It also returns the first root it reaches, so it can't
report that a series has more than one valid rate. It is left in place for backtests. Moving
backtests onto the shared kernel is a later cleanup.

**Conventions (declared in the kernel header):**

- **Timing.** Valuations and flows are end-of-day. The sub-period return is
  `(V_end − F) / V_start − 1`. It is exact when every flow date has a valuation. Otherwise the
  affected sub-period uses day-weighted Modified Dietz, and the figure is labeled
  `LinkedModifiedDietz` with the dates named.
- **Money-weighted.** XIRR, Actual/365, from the investor's side. Every root in [−99%, +10,000%] is
  located by a grid scan and bisection. The period figure is de-annualized from the rate.
- **Currency.** Translation is explicit (`translateValuations`/`translateFlows`, each at its own
  as-of rate). An item that can't be translated keeps its currency, so the figure blocks.

**Brokerage disposition.** `CashAdjustedReturn` keeps its original arithmetic, which subtracts every
cash transaction, dividends included. This preserves existing behaviour. The new `ReturnFigures`
treat only `Deposit`, `Withdrawal` and `Transfer` as external capital. Dividends, interest, fees,
premium and trade cash count as return, and an `Other` transaction blocks the figure. Every
brokerage figure carries the source `brokerage-balance-snapshots` and states that it is not
ledger-derived. Retiring `CashAdjustedReturn` is a candidate follow-up once the consumers move.

## Slice 2 — fund, share-class and investor returns from the books

Read side only, in `Meridian.FinancialOperations`, which owns the investor and capital-account
activity projection (depth-slate constraint). It is exposed through one route family beside the
brokerage route, not a second performance API: the same DTO and the same kernel, with a different
scope.

| Scope | Valuation points | External flows | Figures |
| --- | --- | --- | --- |
| Fund | Posted NAV at each dealing/flow date (persisted daily series — see gap below) | Subscriptions and redemptions dealt, distributions paid (posted) | TWR, MWR |
| Share class | NAV per unit from `ShareClassUnitRegister` at each dealing date | Distributions per unit (unit-neutral dealing needs no flow) | TWR (gross/net per fee policy) |
| Investor (unitized) | `Units × NavPerUnit` from `ShareClassUnitHolding` at period start and end | That investor's `ShareClassUnitMovement`s at dealing dates | MWR; TWR = class TWR over the holding window |
| Investor (partnership) | Posted ending capital from the partners'-capital statement | Capital-account subledger contributions and distributions (posted only) | MWR |

Key decisions:

- **Residual value.** The unit register supplies the investor's ending value for unitized classes,
  which closes the depth-slate gap there. Partnership capital accounts use the posted ending balance
  from the `W9-REPORT-005` statement layout (`PartnersCapitalStatementLayout`). An unposted
  allocation makes the figure pro-forma.
- **Gross vs net.** Net figures use NAV after `FundEconomics` fee accruals. Gross figures add back
  the period's posted management and performance fees. Each figure states which it is through its
  convention reason. This does not create a second fee calculation.
- **Currency.** The capital-account projection partitions by currency. Measure per currency, or
  translate through the existing FX rate source into a declared presentation currency. A missing
  rate blocks.
- **Gap to close first.** The daily pricing projection is a single-day snapshot. A chainable,
  persisted fund-NAV series keyed by (fund, class, as-of) is a prerequisite for fund TWR. Until it
  exists, fund figures fall back to the labeled Modified Dietz approximation between posted NAV
  strikes.

## Slice 3 — the agreeing pack

- Add a **performance section** to the partners'-capital / client report produced by
  `ReportingCertifiedArtifactProducer` and rendered by `FinancialReportDocumentRenderer` (PDF/XLSX).
  For each investor and class it shows period TWR, MWR, the roll-forward, fees and NAV per unit.
- **Tie-out gate before render:**

  ```text
  opening capital + contributions − distributions + allocated P&L − fees = closing capital
  ```

  This must hold to the cent for each investor. Investor MWR must be computed from exactly those
  flows and that closing value, and class NAV × units must equal the sum of holdings. A failed tie
  blocks certification of the pack. It does not render a footnote.
- Every performance line carries its convention, status and reasons into the document. A
  `ReviewRequired` figure renders as "review required", never as a number.

## Slice 4 — operator surface (both lanes)

Show the figures on the browser Portfolio/Reporting surfaces and on the WPF parity surface, from the
same DTO, with a figure chip showing convention, status, period and currency. No client-side
recomputation, per the shared-contract rule.

## Slice 5 — `W11-STAKE-001` stakeholder drill-through

Blocked on `W11-PASSPORT-001` (planned). A stakeholder grant is a `UserAccessAssignmentDto` with:

- a new `AccessScopeKind` for one delivered package version,
- a read-only role,
- `EffectiveTo` as the expiry,
- `GrantedBy` and `Rationale` set.

It reuses the existing assignment store, audit events and revocation, with no parallel grant model.
The read-only route resolves only lines inside the frozen package version. Each line links to its
Number Passport. Every view and download is audited against the package's delivery record. The
route exposes no write verbs. Required tests:

- cross-package isolation,
- an amendment that wasn't granted stays invisible,
- revocation takes effect immediately,
- every write route rejects the grant.

## Validation

```bash
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj -p:FSharpTestSlice=Ledger --filter "FullyQualifiedName~ReturnMeasurement"
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj /p:EnableWindowsTargeting=true --filter "FullyQualifiedName~PerformanceReturnFiguresTests|FullyQualifiedName~BrokeragePortfolioSyncServiceTests"
```
