Selection action bar for a table — appears when rows are selected, shows the count, and offers batch actions. `BulkSelectCheckbox` is the tri-state header box that pairs with it.

```jsx
<BulkActionBar selectedCount={n} onAction={run} actions={[
  { id: "export", label: "Export" },
  { id: "delete", label: "Delete", danger: true },
]} />
<BulkSelectCheckbox checked={allSelected} indeterminate={someSelected} onChange={toggleAll} />
```

Mark destructive actions `danger`. Render nothing when `selectedCount` is 0 — the bar exists to act on a live selection.
