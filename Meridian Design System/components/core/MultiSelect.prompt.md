MultiSelect — batch selection toolbar for tables.

Manages a Set of selected row IDs. Shows a toolbar with select-all checkbox, selection count, and contextual action buttons (delete, export, reassign, etc.). Wire SelectCheckbox into each table row.

```jsx
const [selected, setSelected] = useState(new Set());
<MultiSelect
  selectedIds={selected}
  onSelectionChange={setSelected}
  totalCount={rows.length}
  actions={[
    { id: 'delete', label: 'Delete', icon: '⌫', dangerous: true,
      onClick: (ids) => deleteRows(Array.from(ids)) }
  ]}
/>
```
