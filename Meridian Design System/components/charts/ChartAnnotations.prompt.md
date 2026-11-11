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
a pane, callouts are omitted because no value scale is defined.

The overlay sits inside the frame's image-role SVG, so it can never take focus
or be read item by item. Whenever an annotation carries meaning, render
`ChartAnnotations.List` beside the chart with the same arrays:

```jsx
const [active, setActive] = React.useState(null);
<ChartFrame ariaLabel="Synthetic price" …>
  {g => <ChartAnnotations g={g} pane="price" events={events} callouts={callouts} activeId={active} />}
</ChartFrame>
<ChartAnnotations.List events={events} callouts={callouts}
  ariaLabel="Annotations on synthetic price"
  formatIndex={i => dates[i]} formatValue={v => "$" + v.toFixed(2)}
  activeId={active} onActiveChange={setActive} />
```

The list is ordered by x-index. Each entry is a button naming where it is, what
it says, its type and its semantic kind in words. Arrow keys, Home and End move
between entries; Enter or Space opens an entry's `detail`; Escape closes it and
focus stays on the entry. Focusing or hovering an entry emphasises its mark on
the chart through `activeId`. Give annotations an `id` when the arrays can be
reordered. Pass `formatIndex`, or entries read as bare positions. The enclosing
chart still needs its own `ariaLabel`.
