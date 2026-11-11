The compound container for consequential analysis. It standardizes the analytical question, title, unit, as-of, source, data condition, plot, selected-observation readout, caveat/method, and nonvisual fallback.

```jsx
<AnalyticalFigure question="How has the curve changed?" title="Statutory yield curve"
  unit="Yield, %" asOf="2026-07-31" source="Validated market composite"
  condition="confirmed" fallback={<CurveDataTable rows={rows}/>}> 
  <YieldCurve data={data}/>
</AnalyticalFigure>
```

Use `continuityState` for operational freshness. Essential values must remain visible without hover; complex plots require `fallback`.

**3.2 —** `variant="small-multiple"` compacts the figure for tiling and drops the per-panel question and exact-values disclosure, because `AnalyticalFigureGrid` declares them once for the set — and the grid must state its `sharedScale` explicitly or it prints the standing warning that scales differ. `variant="print"` flattens to paper white and expands the exact-value table.

```jsx
<AnalyticalFigureGrid title="Yield by sector" sharedScale="3.00–9.00%, common y-axis"
  columns={3} source="Composite pricing · validated holdings">
  {sectors.map(s => (
    <AnalyticalFigure key={s.k} variant="small-multiple" title={s.k} condition={s.c}>
      <Sparkline data={s.v} />
    </AnalyticalFigure>
  ))}
</AnalyticalFigureGrid>
```
