Who owns what — the family-office / fund entity structure as a layered top-down diagram: households and individuals at the top, trusts and LLCs between, funds and custody accounts at the leaves. Nodes are hairline chips (type eyebrow · name · jurisdiction/currency mono); edges carry the ownership % on the line, relationship type on hover.

```jsx
<OwnershipGraph
  nodes={overview.ownershipGraph.nodes}   // FamilyOwnershipGraphDto passes straight through
  edges={overview.ownershipGraph.edges}
  selectedId={sel} onSelectNode={setSel}
/>
```

Layout is automatic (level = deepest parent + 1, cycle-safe) — don't pre-sort. `onSelectNode` makes nodes clickable; pair with a `KeyValueGrid` or `Drawer` inspector showing the selected entity's accounts and evidence links. For flat parent→child trees with no percentages, prefer `TreeView`.
