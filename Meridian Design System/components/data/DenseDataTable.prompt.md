Dense mono data grid — white paper, small-caps muted headers, hairline row borders with off-white zebra striping, and a teal-blue left rail + blue wash on the hovered/selected row. The workhorse of every Meridian screen.

```jsx
<DenseDataTable
  selectedIndex={2}
  sortKey="pnl" sortDir="desc" onSort={setSort}
  onRowClick={(row, i) => select(i)}
  columns={[
    { key: "symbol", label: "Symbol" },
    { key: "qty", label: "Qty", align: "right" },
    { key: "pnl", label: "P&L", align: "right", render: (r) => <span style={{ color: r.pnl[0] === "-" ? "var(--red-dim)" : "var(--green-dim)" }}>{r.pnl}</span> },
  ]}
  rows={positions}
/>
```

Numbers right-aligned, tabular, signed. Pass `onSort` for header sort arrows.
