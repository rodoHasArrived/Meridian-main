ColumnManager — keyboard- and screen-reader-accessible column controls: the counterpart to DenseDataTable's mouse-only header drag/resize/pin. A popover listing every column in order with move up/down, pin, show/hide, and width steppers as real buttons. Binds directly to a `useTableColumns()` return value (from TableHooks) — no separate wiring.

```jsx
const cols = useTableColumns(BASE, { persistKey: "blotter.cols" });
<ColumnManager cols={cols} />
<DenseDataTable columns={cols.visibleColumns} onColumnResize={cols.resize}
  onColumnReorder={cols.reorder} onColumnPin={cols.togglePin} … />
```
