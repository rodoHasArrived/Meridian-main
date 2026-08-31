Cost-basis lot table — acquisition date, days held, quantity, basis, market value/proceeds, and gain/loss, with an automatic short/long-term badge (amber/teal) derived from `longTermDays`.

```jsx
<TaxLotTable
  lots={[{ acquired: "2025-02-14", quantity: 100, costBasis: 8400, marketValue: 9650 }]}
  mode="unrealized"
  longTermDays={365}
/>
```

Switch `mode="realized"` (and pass `proceeds` instead of `marketValue`) for a closed-lot / trade-history view rather than a live holdings view.
