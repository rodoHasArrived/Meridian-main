Use `DotMatrix` for continuous magnitudes across rows and columns: desk exposure,
contribution, or allocation. Use `CoverageMatrix` for categorical status coverage.

```jsx
<DotMatrix ariaLabel="Synthetic sector exposure" diverging shape="square"
  columns={["Energy", "Technology"]}
  rows={[{ id: "macro", label: "Macro", cells: [12, -4] },
    { id: "credit", label: "Credit", cells: [null, 8] }]}
  fmt={value => value.toFixed(1) + "%"}
  caption="Synthetic exposures; rows share valuation time and net-asset basis." />
```

Use `diverging` when sign matters, and state units, scope, period, and denominator.
`domainMax` is the absolute saturation magnitude; use one consistent scale for
comparisons. Preserve null as missing instead of replacing it with zero. The
parent owns loading, empty, stale, and error explanations.

`ariaLabel` names the grid and emits a hidden data table. With `onCellClick`, cells
are native buttons: Tab then Enter/Space opens the caller's drilldown. Do not
assume arrow-grid navigation. Keep a text/number view available so size, tint,
and pointer-only titles are not the only route to the values or evidence notes.
