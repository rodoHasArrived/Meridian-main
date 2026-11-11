A shared reference axis for selection rails, current-time markers, zero lines, targets, and comparison baselines. Place it inside a positioned chart, timeline, row, or workflow surface and pass a normalized `position` from 0–1.

```jsx
<div style={{position:"relative",height:240}}>
  <Datum role="current-time" orientation="vertical" position={.72}
    label="Current" value="11:42 UTC" marker />
</div>
```

Use `selection` for committed state, `current-time` for temporal orientation, `target` for thresholds, and `zero` for analytical baselines. Do not use the terracotta signal as the default datum color.

**3.2 —** Pass `svg` to render inside a plot's own `<svg>` as a `<g>` of `<line>`/`<rect>`/`<text>`, so the datum shares the chart's coordinate space and prints with it. Pass `to` for a range band (embargo windows, feed gaps, tolerance corridors) — model uncertainty is `IntervalBar`, not a datum. Wrap crowded sets in `DatumGroup`, which measures the container and assigns label lanes so labels stack instead of overprinting.

```jsx
<svg viewBox="0 0 660 230">
  <svg x={30} y={30} width={600} height={170} overflow="visible">
    <Datum svg role="warning" position={.11} to={.23} label="Embargo" />
    <Datum svg role="selection" position={.56} label="Selected" value="62 bp" marker />
  </svg>
</svg>
```
