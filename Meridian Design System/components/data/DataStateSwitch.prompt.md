The whole data-state ladder in one wrapper. Wrap the dominant work plane; children render only when `state === "ready"`, and loading / empty / error each paint a full-surface slot with the same header strip, so the screen never jumps between states.

```jsx
<DataStateSwitch state={dataState} label="Positions" actionLabel="Retry" onAction={reload}>
  <DataGrid columns={cols} rows={rows} />
</DataStateSwitch>
```

Every `templates/` entry wires this to its `dataState` tweak — that is how a copied template ships all four states instead of the happy path alone. Use `EmptyState` instead when the zero-data case lives *inside* a populated screen (an empty panel beside full ones); use `DataStateSwitch` when the whole surface is in one state.

A11y (3.5): non-ready states announce via `announce` ("polite" | "assertive" | "off"; error defaults assertive). Ready children are returned unwrapped — no live region around working content.
