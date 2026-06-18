Candlestick price chart mirroring the desktop ChartingPage candlestick pane — price/time axes with labels, gridlines, MA overlays, an optional volume histogram subpane, and a crosshair with a right-axis price chip. Fill its container (give the parent a fixed height).

```jsx
<div style={{ height: 460 }}>
  <CandleChart
    bars={bars /* [{t:"10:48", o,h,l,c,v}] */}
    overlays={[{ label: "MA20", color: "var(--accent)", win: 20 }, { label: "MA50", color: "var(--orange)", win: 50 }]}
    crosshairIndex={44}
  />
</div>
```

Up candles are hollow green (`--chart-equity`), down filled red (`--chart-drawdown`). Set `showVolume={false}` to drop the volume subpane.
