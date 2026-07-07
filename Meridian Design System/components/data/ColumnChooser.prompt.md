Show/hide toggle for table columns — a compact popover listing every column with a checkbox. Binds to your visible-keys state.

```jsx
<ColumnChooser columns={COLS} visible={visibleKeys} onChange={setVisibleKeys} />
```

For full column control — reorder, pin, resize, width steppers — use `ColumnManager` bound to `useTableColumns()`. Reach for ColumnChooser when you only need visibility.
