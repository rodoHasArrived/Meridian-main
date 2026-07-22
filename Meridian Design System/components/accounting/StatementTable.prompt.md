P&L / balance-sheet / cash-flow layout — grouped sections, indented line items, per-section subtotals, and a double-ruled grand total. Use this instead of `DenseDataTable` whenever the shape is "statement," not "rows."

```jsx
<StatementTable
  sections={[{ label: "Revenue", rows: [
    { label: "Product sales", value: 182400 },
    { label: "Services", value: 41200 },
  ], subtotal: { label: "Total revenue", value: 223600 } }]}
  total={{ label: "Net income", value: 68210 }}
  pnl
/>
```

Pass `columns` (e.g. `[{key:"q1",label:"Q1"},{key:"q2",label:"Q2"}]`) and `values` instead of `value` on each row for a period-comparison statement.
