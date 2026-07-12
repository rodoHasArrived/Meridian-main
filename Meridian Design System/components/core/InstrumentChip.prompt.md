Identity micro-chip for a tradable instrument — mono symbol, small-caps venue, and an asset-class block letter on a washed semantic tint (E equity · F ETF · U future · O option · X fx · C crypto · B bond). Use it wherever a symbol appears outside a table cell: watchlists, order tickets, inspectors, breadcrumbs.

```jsx
<InstrumentChip symbol="AAPL" venue="XNAS" assetClass="eq" />
<InstrumentChip symbol="ESU6" venue="CME" assetClass="fut" size="sm" onClick={select} />
```

With `onClick` it renders as a real button; `selected` gives it the accent border + wash for the current pick. Accepts long or short asset-class forms ("equity"/"eq"). Don't hand-roll symbol badges — this encodes the asset-class color convention once. Inside dense table cells, prefer a plain mono symbol; the chip is for standalone placements.
