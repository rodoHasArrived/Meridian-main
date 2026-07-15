ExpandableDataTable — DenseDataTable with collapsible detail rows.

Each row can expand to show a full-width detail panel (audit trail, allocations, related records, etc.). Detail content is a render function (slot). Smooth slide animation on expand/collapse. Supports sorting, selection, and custom cell renderers.

```jsx
<ExpandableDataTable
  columns={[
    { key: 'date', label: 'Date' },
    { key: 'amount', label: 'Amount', align: 'right' }
  ]}
  rows={transactions}
  expandable={(row, index) => (
    <div>
      <h4>{row.description}</h4>
      <p>Account: {row.account}</p>
    </div>
  )}
  onSort={(key) => handleSort(key)}
/>
```
