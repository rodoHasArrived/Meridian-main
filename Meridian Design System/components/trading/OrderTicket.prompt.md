Order-entry primitive — side toggle (washed green/red, never solid), qty, market/limit, TIF, live notional estimate, submit. When `environment="live"` a red confirm gate appears and submit stays disabled until the operator explicitly acknowledges real capital — never remove or pre-check that gate.

```jsx
<OrderTicket symbol="AAPL" lastPrice={201.12} environment="live"
  onSubmit={(o) => api.placeOrder(o)} />
// o: { symbol, side, qty, type, limitPrice, tif, environment }
```

The ticket owns its draft state; you get the composed order once, on submit. Pair with `Badge variant="live"` context elsewhere on the screen — the environment should never be ambiguous around order entry.
