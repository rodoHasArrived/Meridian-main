Inline micro-trend — a wordless line/area/bar sketch sized to sit in a table cell, metric card, or list row. No axes, no labels; it shows shape, not values.

```jsx
<Sparkline points={[3,5,4,8,7,9]} width={80} height={24} variant="area" />
<Sparkline points={pnl} variant="line" baseline={0} />
```

Set `baseline` (e.g. 0) to split above/below coloring for P&L. Keep it small — if you need axes or a readout, reach for a real chart (`EquityCurve`, `Histogram`).
