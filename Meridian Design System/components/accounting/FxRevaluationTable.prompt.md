Period-end FX reval — foreign balances at booked vs. current rates, per-row unrealized gain/loss, and a net G/L footer. All arithmetic is `Money.fxRevalue` (half-even), so a consumer recomputing the journal entry lands on the same cents.

```jsx
const { FxRevaluationTable } = window.MeridianDesignSystem_4f61be;

<FxRevaluationTable
  base="USD"
  rows={[
    { currency: "EUR", account: "Prime — cash",  local: 1250000,  bookedRate: 1.0842, currentRate: 1.0918 },
    { currency: "JPY", account: "Margin posted", local: 98000000, bookedRate: 0.006701, currentRate: 0.006645 },
  ]}
  onRowClick={(row) => openAccount(row)}
/>
```

Non-obvious: rates are base units per 1 local unit (EUR→USD 1.0842, JPY→USD 0.0067 — not 149.3). Local balances format at their ISO minor units automatically (JPY shows 0dp). The G/L column is signed P&L-toned; the footer nets it, which is the amount the reval journal posts.
