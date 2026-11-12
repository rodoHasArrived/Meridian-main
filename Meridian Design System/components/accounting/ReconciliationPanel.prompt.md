Two-sided statement-vs-ledger comparison with its own filter/search toolbar and a summary bar that totals the *full* data set (not just the filtered view) and calls out "Out by …" past tolerance. Reach for this instead of two side-by-side `DenseDataTable`s any time the point is proving a match.

```jsx
<ReconciliationPanel
  left={{ title: "Statement", items: statementLines }}
  right={{ title: "Ledger", items: ledgerLines }}
  currency="USD"
  tolerance={0.01}
/>
```

Matched rows carry a green rail, unmatched an amber wash — the same trio pattern as `SeverityBadge`, just applied per-row.

Pass `onToggleItem(id, matched)` to enable exact row match changes and
`onToggleAll(side, matched)` for all loaded items on that side. These callbacks
receive booleans, not DOM events. Filtering does not change the all-items scope.
Without mutation callbacks the match controls stay disabled; the panel still
supports read-only filtering and summaries. Stable item IDs are required to edit.
