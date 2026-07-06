# Corporate-Action Known-Defect Registry

Each defect below is pinned by a **characterization test** carrying
`[Trait("Category", "KnownDefect")]`: the test asserts today's defective behavior so CI stays
green, and carries a `// TARGET:` comment stating the correct behavior. When a fix lands, the
characterization test fails — the fix PR must flip the assertion to the target expectation and
update this registry in the same PR.

Run the pinned suite alone:

```bash
dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true --filter "Category=KnownDefect"
dotnet test tests/Meridian.Backtesting.Tests -c Release /p:EnableWindowsTargeting=true --filter "Category=KnownDefect"
```

| ID | Location | Current behavior (pinned) | Target behavior | Fixing lane | Fixture |
|----|----------|---------------------------|-----------------|-------------|---------|
| CA-DEF-001 | `src/Meridian.Backtesting/CorporateActionAdjustmentService.cs` (`BuildDividendFactors`) | Dividend factor silently skipped when no prior-close bar exists before an ex-date; series returned unadjusted with no signal | Explicit degradation: warning + machine-readable adjustment report | Idea #2 remainder (factor engine) | `dividend-missing-prior-bar.json` |
| CA-DEF-002 | `src/Meridian.Application/SecurityMaster/SecurityMasterLedgerBridge.cs` (`PostNonCashLifecycleMemo`) | Spinoffs, mergers, and distributions post symbolic 1-unit memos; no basis allocation, lot conversion, or new position | Entitlement-driven position transformation with basis conservation | Idea #3 (entitlement engine) | `t-wbd-spinoff-2022.json`, `cash-stock-merger-2018.json` |
| CA-DEF-003 | `src/Meridian.Application/SecurityMaster/SecurityMasterLedgerBridge.cs` (`PostFactorPaydown`) | The pool-factor delta ratio is booked as a literal cash amount | Cash = factor delta × face value of held lots | Idea #3 | `mbs-factor-paydown.json` |
| CA-DEF-004 | `src/Meridian.Backtesting/CorporateActionAdjustmentService.cs` (`AdjustPositionAsync`) | `DistributionRatio` subtracted from cost basis as an absolute amount | Basis reduction proportional to factor × face | Idea #3 | `mbs-factor-paydown.json` |
| CA-DEF-005 | `src/Meridian.Application/SecurityMaster/SecurityMasterLedgerBridge.cs` (`PostRedemptionMemo`) | Percent-of-par redemption price posted as literal proxy cash | Cash = price% × par × face quantity | Idea #3 | `bond-call-101-5.json` |
| CA-DEF-006 | `src/Meridian.Application/SecurityMaster/SecurityMasterLedgerBridge.cs` (dividend branch) | All dividends skipped with a warning when `CorporateActionLedgerPostingContext.PositionQuantity` is not supplied (defaults to 0) | Record-date holdings seam supplies the position quantity | Idea #3 | `dividend-no-position-context.json` |

## Fixture schema

One JSON file per scenario, camelCase (Web) naming, deserialized into
`Meridian.TestSupport.CorporateActions.GoldenCorporateActionScenario`
(`tests/Shared/CorporateActions/`). `actions` entries carry the full 18-field
`CorporateActionDto` shape. Prices in real-name scenarios are plausible synthetic
approximations — assertions ride invariants, never point prices.

## Invariant tags

| Tag | Enforced by |
|-----|-------------|
| `series-continuity` | Adjusted day-over-day returns equal raw returns off ex-dates; ex-date jumps match declared action factors |
| `factor-monotonicity` | Implied factor (adjusted/raw close) piecewise-constant, jumps only at effective ex-dates, exactly 1 after the last ex-date |
| `notional-conservation` | Split-only: close × volume conserved within volume-rounding tolerance |
| `journal-balanced` | Every posted journal entry balances (debits == credits) |
| `receivable-settled` | Dividend receivable nets to zero after pay-date receipt |
| `idempotent-posting` | Re-posting the same actions adds zero new entries |
| `position-basis-conserved` | Split-only: quantity × cost basis conserved through `AdjustPositionAsync` |
| `supersede-fold` | Consumers act on the chain tip; cancelled chains vanish |
| `unknown-event-type` | Loader opt-out: scenario intentionally contains an unresolvable event type |
