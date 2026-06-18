Equity / performance curve — primary line with area fill, dashed benchmark overlay, value-axis labels, gridlines, a legend, a crosshair price chip, and an optional drawdown subpane. Fill its container (give the parent a fixed height).

```jsx
<div style={{ height: 360 }}>
  <EquityCurve
    series={[
      { label: "Strategy", color: "var(--chart-equity)", points: equity },
      { label: "SPX", color: "var(--chart-secondary)", points: bench, dashed: true },
    ]}
    labels={["Jan","Feb","Mar","Apr","May","Jun"]}
    drawdown={dd /* values ≤ 0 */}
    valueFmt={(v) => "$" + (v/1000).toFixed(0) + "k"}
    crosshairIndex={18}
  />
</div>
```

First series is primary (area fill + crosshair chip). Pass `drawdown` for the red subpane; `showLegend={false}` / `fill={false}` to simplify for compact report tiles.
