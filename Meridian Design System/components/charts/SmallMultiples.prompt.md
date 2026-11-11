Use `SmallMultiples` for different metrics on one entity. Use `PlateGrid` when the
same metric must be compared across entities on a common domain.

```jsx
<SmallMultiples ariaLabel="Synthetic fund risk metrics" columns={2}
  panels={[{ id: "beta", label: "Beta", values: [0.8, 0.9, 1.1], baseline: 1 },
    { id: "spread", label: "Spread", values: [120, 140, 135],
      fmt: value => value.toFixed(0) + " bp", sub: "OAS" }]}
  caption="Synthetic observations; one entity and common monthly window." />
```

Each panel scales independently by default. State units, time window, baseline,
and source; local scaling must not suggest comparable absolute magnitudes.
Enable `sharedScale` only for commensurate panels. Select actual/prior/scenario/
estimated roles according to data meaning. Retain null gaps; do not imply a
continuous observed path across missing periods.

The component supplies no filter, keyboard selection, loading, or recovery flow.
An empty panel set requires an explanation from the parent. `ariaLabel` emits a
hidden metric/last-value list; provide an adjacent full time-series table when
intermediate observations are consequential. Mini traces are static figures,
not focusable controls.
