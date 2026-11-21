Use `WatchlistStrip` for dense instrument rails with aligned last-price, signed
change, sparkline, and day-range context. Use a full `DataGrid` for sorting,
column management, and richer evidence.

```jsx
<WatchlistStrip ariaLabel="Synthetic watchlist" selectedId={selected}
  onSelect={setSelected}
  items={[{ id: "syn-1", symbol: "SYN", name: "Synthetic instrument",
    last: 101.25, change: 1.25, changePct: 1.25,
    dayRange: [99.5, 102], spark: [100, 100.5, 101.25] }]}
  priceFmt={value => value == null ? "Missing" : value.toFixed(2)} />
```

Pass stable ids; the symbol is the fallback identity. `changePct` is percentage
points and takes display precedence over `change`; when both are present their
signs must agree. Declare currency, trading session, as-of time, adjustment basis,
and feed freshness beside the strip. The item shape does not render a
`condition` property: use an adjacent data-condition/freshness surface instead.

No data retrieval, stale-state handling, validation, or empty-state explanation
is built in. Validate finite prices and ordered day ranges. Missing change
renders a dash; a missing range is omitted. Multiple columns fill in row order.

With `onSelect`, each item is a native button with option semantics, supporting
Tab and native Enter/Space activation. Without it, the strip is a static list.
The implementation does not provide listbox arrow navigation or a complete
quote-by-quote accessible data table; test that behavior in the consuming rail.
