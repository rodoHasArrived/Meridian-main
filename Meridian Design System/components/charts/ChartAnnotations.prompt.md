Use `ChartAnnotations` inside a `ChartFrame` render prop to align events, regimes,
and explanatory callouts to the chart's actual geometry. It returns SVG groups,
not a standalone chart. Use the pane key for value-positioned callouts.

```jsx
<ChartFrame n={prices.length} panes={[{ key: "price", domain: [90, 110] }]}>
  {g => <ChartAnnotations g={g} pane="price"
    bands={[{ from: 2, to: 5, label: "Scenario window", kind: "scenario" }]}
    events={[{ index: 4, badge: "A", label: "Synthetic policy change" }]}
    callouts={[{ index: 4, value: 101, text: "Observed price", dx: 24, dy: -20 }]} />}
</ChartFrame>
```

Indices must match the frame's data ordering. Values must share the selected
pane's units and domain. Bands describe regimes, not confidence intervals.
Use event/semantic kinds according to financial meaning; do not reuse a series
color merely to decorate a label. The caller resolves label collisions and
validates finite values; the overlay does not infer dates, evidence, or causality.

Missing geometry renders nothing; empty annotation arrays draw nothing. Without
a pane, callouts are omitted because no value scale is defined. The overlay has
no keyboard controls or independent accessible name. Each overlay registers a
textual summary of its bands, events, and callouts with the owning SVG description,
preserving the frame description and other overlays across rerenders and cleanup.
Give the enclosing chart an `ariaLabel`, `ariaDescription`, and accessible data
that explain the consequential annotations and financial context. Tooltip-only explanations are insufficient.
