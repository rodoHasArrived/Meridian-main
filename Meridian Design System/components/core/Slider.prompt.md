Single-value range control — flat square thumb over a filled institutional track, not a rounded consumer dial. Wraps a native `<input type="range">` so keyboard and focus behavior come free. Good for backtest parameter sweeps and risk thresholds.

```jsx
<Slider label="Min yield" value={minYield} onChange={setMinYield} min={0} max={10} step={0.25} showValue valueFmt={(v) => v.toFixed(2) + "%"} />
<Slider label="Max drawdown tolerance" value={maxDD} onChange={setMaxDD} min={0} max={30} variant="danger" showValue valueFmt={(v) => "\u2212" + v + "%"} />
```

Arrow keys, Home/End, Page Up/Down, and the focus ring all come from the native element — only the visuals are overridden. The thumb is a flat 12×16px rectangle (`border-radius: 0`), never a circle. `marks` renders tick labels under the track for display only — it doesn't snap the thumb.
