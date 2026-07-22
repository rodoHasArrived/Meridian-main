ChartTooltip — floating readout for the chart interaction layer. Renders label/value rows in a small card that follows the crosshair index (auto-flips near the right edge). Pairs with `useChartCrosshair` / `useSyncedCursor` from `useChartCrosshair.js`. Place it inside the crosshair-bound wrapper (which is `position:relative` via the hook's `bind`).

```jsx
const cx = useSyncedCursor(bars.length);
<div {...cx.bind}>
  <CandleChart bars={bars} crosshairIndex={cx.index} />
  <ChartTooltip index={cx.index} count={bars.length}
    title={bars[cx.index]?.t}
    rows={cx.index != null ? [
      { label: "O", value: bars[cx.index].o.toFixed(2) },
      { label: "C", value: bars[cx.index].c.toFixed(2), color: "var(--green-dim)" },
    ] : []} />
</div>
```
