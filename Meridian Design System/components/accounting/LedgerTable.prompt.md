General-ledger grid — Debit/Credit columns plus a running Balance, with a totals footer that turns red the moment Σdebit ≠ Σcredit. This is what proves a ledger to an operator; don't hand-roll the footer math elsewhere.

```jsx
<LedgerTable
  rows={postings}                       // rows may carry status: "pending" | "void"
  opening={40210.0}
  normalSide="debit"
  onRowClick={(row, i) => setInspected(i)}
  selectedIndex={inspected}
  caption="Account 1100 · Operating cash · 2026-06"
/>
```

Set `showAccount` when the table spans multiple accounts (a journal view) rather than one account's history. Non-obvious: `status: "void"` rows are struck through and **excluded** from totals and the running balance (the footer notes how many); the running balance is computed in source (chronological) order and does not re-derive when the operator sorts a column. Headers sort in place — pass `onSort` only when the data source sorts server-side.

When onRowClick is provided, data rows have one roving tab stop, starting at selectedIndex (or the first visible row). Up/Down and Home/End move actual focus without inspecting or changing records; Enter/Space calls the same inspect handler as a click with the exact original row and source index, even after sorting. Nested interactive controls retain their own keys and clicks. Voided rows stay readable and inspectable while remaining excluded from financial totals.
