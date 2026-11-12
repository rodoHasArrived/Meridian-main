Order-entry primitive — side toggle (washed green/red, never solid), qty, market/limit, TIF, live notional estimate, submit. When `environment="live"` a red confirm gate appears and submit stays disabled until the operator explicitly acknowledges real capital — never remove or pre-check that gate.

```jsx
<OrderTicket symbol="AAPL" lastPrice={201.12} environment="live"
  prefill={{ side: "buy", type: "limit", limitPrice: 201.05, token: clickId }}
  guards={preTradeChecks}
  onDraftChange={setDraft}
  onSubmit={(o) => api.placeOrder(o)} />
// o: { symbol, side, qty, type, limitPrice, tif, environment, notional }
```

`prefill` seeds the draft **once per distinct `token`** — a depth-ladder click or an amend of a
working order can fill the ticket without fighting the operator's typing. `guards` renders an
`ActionGuardList` of pre-trade checks above the gate; any `blocking` guard disables submit and the
first blocking reason prints under the button. `onDraftChange` streams the live draft (with
notional) so a risk panel can price the order before it is sent. The draft clears on a successful
submit.

The ticket owns its draft state; you get the composed order once, on submit. Pair with `Badge variant="live"` context elsewhere on the screen — the environment should never be ambiguous around order entry.

Live acknowledgement belongs to the exact draft and clears after submitted-field edits, identity/environment or prefill-token changes, and submit. Quantity and limit entries must be complete positive decimals; malformed numeric prefixes remain invalid on blur. Unknown environments, missing symbols, invalid side/TIF and absent submit handlers disable submission. This UI reference does not replace server-side authorization, risk checks, validation or idempotency.
