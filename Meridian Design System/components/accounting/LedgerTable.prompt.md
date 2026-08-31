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
