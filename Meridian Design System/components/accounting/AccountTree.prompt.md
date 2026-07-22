Chart-of-accounts tree — expandable hierarchy with roll-up balances (a parent with no explicit `balance` sums its descendants). Use over `TreeView` whenever nodes are literally accounts.

```jsx
<AccountTree
  nodes={[{ code: "4000", name: "Revenue", children: [
    { code: "4010", name: "Product sales", balance: 182400 },
    { code: "4020", name: "Services", balance: 41200 },
  ]}]}
  selectedCode={selected}
  onSelect={(node) => setSelected(node.code)}
/>
```

Parents render bold, leaves muted; balances are mono/right-aligned. Pair with `KeyValueGrid` in a `Drawer` for a selected-account inspector.
