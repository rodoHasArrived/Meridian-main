The numeric authority. `core/format.js` is the only module in the system that constructs an `Intl` number formatter; `FormatProvider` declares what surface it is formatting for, and `useFormat()` hands a component the formatters already bound to it. A bare `Intl.NumberFormat` anywhere else is a governance violation (`tests/conformance.html` rule C-01) — the same shape of rule as "never route a calendar date through `toISOString()`".

```jsx
<FormatProvider locale="de-DE" currency="EUR" negative="parens">
  <WorkstationShell>…</WorkstationShell>
</FormatProvider>

const fx = useFormat();                       // safe with no provider — process defaults
fx.money(-1234.5)                             // (1.234,50 €) under the provider above
fx.price(101.257, { tick: 0.0001 })           // 101.2570 — precision from the instrument
fx.percent(0.0421, { signed: true })          // +4.21%
fx.bps(0.00124)                               // +12.4 bp
MeridianDesignSystem_205d17.Format.money(v)   // templates and consumer code
```

Reach for it whenever a number becomes text: money, quantity, price, percent, basis points, compact axis labels, signed deltas, accounting negatives. Locale and currency come from the surface, decimals come from the instrument — never from the call site. `Money` keeps the accounting arithmetic (rounding policy, cent-exact allocation, FX, trial balance) and formats through this authority, so consumer-computed figures still agree to the cent with what the tables render.
