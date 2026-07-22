Blotter — orders blotter preset over `DenseDataTable`. Desk conventions encoded once: side as washed green/red text (never fills), mono right-aligned qty/price, order status through `SeverityBadge` normalization, UTC time-of-day via `Timestamp`.

```jsx
<Blotter orders={[
  { id: "ORD-1204", time: t, symbol: "AAPL", side: "Buy", qty: "400", type: "Limit", limit: "201.0000", filled: "400", status: "Filled" },
]} onRowClick={(o) => inspect(o)} />
```
