# Numeric authority — the law for turning a number into text

**Normative.** Rule ids `N-1…N-8`. Enforced by `tests/conformance.html` rule **C-01**. Companion to `TIME_SEMANTICS.md` (the same argument for time) and `CONTENT_FUNDAMENTALS.md › Numbers` (the house conventions this file makes reachable).

## Why this exists

`localDate.js` became the calendar-date authority after a defect that made every date a day early for every operator west of Greenwich. The same shape of defect was sitting in the numbers: ten modules each wrote `new Intl.NumberFormat("en-US", …)` — `BasisComparisonMatrix`, `RollforwardMatrix`, `PostingImpactPreview`, `ScheduleLadder`, `VarianceCell`, `TieOutPanel`, `CommitmentBar`, `MaturityLadder`, `CashFlowLadder`, `MaterialityAssessment` — while `money.js` and `Timestamp` took a `locale`. The accounting tier, the tier that formats money for a living, was the tier that hardcoded a locale.

It was never only an i18n gap. Precision had no owner either: 4.1 fixed decimals by hand twice — a stale bundle serving an old `priceDecimals` default, then `trading-desk` typing 2dp in four places. A call site that types a decimal count is not making a decision; it is guessing at one the instrument already knows.

## The rules

**N-1 · One constructor.** `components/core/format.js` is the only module in the system that may construct an `Intl` number formatter. A bare `Intl.NumberFormat`, `Intl.PluralRules`, or `toLocaleString("xx-XX")` anywhere else — component, template, card, consumer snippet — is a governance violation, not a style preference.

**N-2 · Locale and currency come from the surface.** A screen declares them once with `FormatProvider`; formatters read them. A call site passes `locale`/`currency` only when that region genuinely differs (a source-currency column, a custodian statement) — and then it does so by nesting a provider, so the override is visible in the tree rather than buried in an argument.

**N-3 · Decimals come from the instrument.** `formatPrice(v, { tick })` derives precision from the tick; `currencyDecimals(code)` gives the ISO-4217 minor unit (JPY 0, BHD 3). Typing `decimals: 2` beside a price is the defect class N-3 exists to retire.

**N-4 · Negatives are U+2212, or parentheses when the surface says so.** Never a hyphen. `negative: "parens"` on the provider switches the whole surface to the statement convention; `parens: true` switches one figure. Zero renders as an em dash only where `zeroDash` is asked for.

**N-5 · Deltas carry an explicit sign.** `formatSignedDelta` (or `signed: true`) — a change figure without a sign is unreadable at a glance and the content rules have always said so.

**N-6 · Compact notation never appears in a ledger column.** `formatCompact`/`formatMoneyCompact` are for metric cards, axis labels and ladders. A column that must tie to a total shows the full figure.

**N-7 · Arithmetic and formatting are different jobs.** `accounting/money.js` owns the arithmetic the books prove — rounding policy, remainder-exact allocation, FX translation, normal-balance classification, trial-balance collapse. It formats through the authority and re-exports none of it. The `Money` carrier composes both halves, so a consumer's computed figure still agrees to the cent with what the table renders.

**N-8 · The authority is reachable from everywhere.** Components use `useFormat()`; module-level helpers call `format.js` directly (the provider also sets the process defaults, so they follow the surface); templates and consumer code use `MeridianDesignSystem_205d17.Format`. There is no surface with an excuse.

## Using it

```jsx
// Shell — declare the surface once.
<FormatProvider locale="de-DE" currency="EUR" negative="parens">
  <WorkstationShell>…</WorkstationShell>
</FormatProvider>

// Component — formatters already bound to the surface.
const fx = useFormat();
fx.money(-1234.5)                     // (1.234,50 €)
fx.price(101.257, { tick: 0.0001 })   // 101,2570
fx.percent(0.0421, { signed: true })  // +4,21 %
fx.bps(0.00124)                       // +12,4 bp
fx.compact(4_120_000)                 // 4,1 Mio.

// One region in the source currency, declared in the tree rather than in an argument.
<FormatProvider currency="JPY" applyGlobal={false}><LedgerTable rows={source} /></FormatProvider>

// Template / consumer code.
MeridianDesignSystem_205d17.Format.money(v, { currency: "USD" });
```

## What moved in 4.2

The sweep normalized three strays toward the house style, deliberately: negatives that Intl rendered as a hyphen now render as U+2212 (`RollforwardMatrix`, the compact ladders), and an unknown ISO code now takes the house narrow-space prefix rather than Intl's non-breaking space. Everything else is byte-identical under `en-US`, which is what the harness in `CONFORMANCE.md` was built first to establish.

## The anti-patterns

| Don't | Why | Do |
| --- | --- | --- |
| `new Intl.NumberFormat("en-US", …)` in a component | The surface can never be told otherwise | `formatMoney` / `useFormat()` |
| `v.toFixed(2)` in JSX | No grouping, no locale, no sign rule, hyphen minus | `fx.number(v, { decimals: 2 })` |
| `decimals: 2` beside a price | Precision belongs to the instrument | `fx.price(v, { tick })` |
| `-1,234.00` | Hyphen is not a minus | `−1,234.00` or `(1,234.00)` |
| `$1.2M` in a ledger column | A tying column cannot be rounded | full figure; compact belongs on metrics |
| A second formatting helper in a domain folder | Two authorities are none | extend `core/format.js` |
