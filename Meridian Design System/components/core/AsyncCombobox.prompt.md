AsyncCombobox — type-ahead picker over a large or server-backed option set (the "one of 40k instruments" control). Debounced async loading, a windowed option list (only visible rows render — handles tens of thousands), keyboard nav, and a quiet loading/empty/error footer. Controlled: you own `value` and the `fetchOptions` fetcher.

```jsx
<AsyncCombobox
  value={sym}
  onChange={setSym}
  fetchOptions={async (q, signal) => (await api.searchSymbols(q, { signal }))}
  getKey={(o) => o.symbol} getLabel={(o) => o.name} getSecondary={(o) => o.name}
  minChars={1} placeholder="Symbol…" />
```
