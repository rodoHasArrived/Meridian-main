The investment income schedule: coupon accrual, accretion/amortization, income, ending book value, and a state per period — the synchronized instrument that replaces a decorative income chart.

```jsx
const rows = Money.effectiveYieldSchedule({
  par: 10_000_000, cost: 9_875_000, couponRate: 0.0525, effectiveYield: 0.05418,
  periods: 3, labels: ["June 2026","July 2026","August 2026"],
}).map((r, i) => ({ ...r, state: i ? "scheduled" : "accrued" }));

<IncomeSchedule rows={rows} selectedId={period} onSelect={setPeriod} />
```

Selecting a period should drive the journal preview and `RollforwardTrace` — the schedule is the navigation, not a report. Rename the columns for premium lots or equity income; keep one row per period and never blank a cell (a period with no accretion shows `—` via the zero-dash rule).
