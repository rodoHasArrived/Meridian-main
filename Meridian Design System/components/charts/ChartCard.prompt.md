Chart card mirroring `ChartCardStyle` — a header row (title + optional small-caps OHLC/stat readout + actions) over a framed plot area. The standard host for `CandleChart` / `EquityCurve`.

```jsx
<ChartCard
  title="AAPL · 1D"
  readout={[
    { label: "O", value: "182.44" },
    { label: "H", value: "203.10", color: "var(--chart-equity)" },
    { label: "L", value: "178.02", color: "var(--chart-drawdown)" },
    { label: "Vol", value: "1.92M" },
  ]}
  actions={<Button variant="ghost" size="sm">1D</Button>}
  height={420}
>
  <CandleChart bars={bars} crosshairIndex={44} />
</ChartCard>
```
