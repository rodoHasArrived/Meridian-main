The money atom — every currency figure in a Meridian surface goes through it (mono, tabular, right-aligned by the parent cell). Encodings are props, not hand-formatting:

```jsx
<AmountCell value={-58200} currency="USD" mode="accounting" />   {/* ($58,200.00) */}
<AmountCell value={12500} currency="USD" drcr />                 {/* $12,500.00 Dr */}
<AmountCell value={-4118.22} currency="USD" mode="pnl" signed /> {/* red −$4,118.22 */}
<AmountCell value={98400000} currency="USD" compact />           {/* $98.4M, full value in tooltip */}
<AmountCell value={125000} currency="JPY" decimals="auto" />     {/* ¥125,000 — 0dp minor units */}
```

Non-obvious: `drcr` drops the sign and appends a small-caps Dr/Cr suffix (debit-positive convention) — use it where a column mixes sides, never alongside `parens`. `compact` is for metric cards only; ledger columns always show full precision. `mode="pnl"` colors by sign; zero and non-numbers stay muted. Strings like "(1,234.00)" parse as negatives.
