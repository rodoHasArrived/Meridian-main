Expandable hierarchy — chart of accounts, security taxonomies, entity trees, file trees. Full ARIA tree keyboard model over a flat DOM (each row announces level, setsize and posinset): Up/Down move, Right expand-then-descend, Left collapse-then-ascend, Home/End, `*` expands every sibling at the level, printable typeahead jumps, Enter selects, Space toggles. **Selection and expansion are independent** — clicking a row selects it, clicking the caret (or double-clicking the row) toggles it — so a branch account can be inspected without collapsing what is under it.

Row height reads `--theme-row-height`, so the tree re-densifies with `DensityToggle` (terminal 26 · compact 28 · standard 36 · spacious 44); override this component alone with `--tree-row-height`. Hairline depth guides are on by default (`guides={false}` to drop them).

Uncontrolled:

```jsx
<TreeView
  items={accounts}
  defaultExpanded={["1000"]}
  selectedId={accountId}
  onSelect={(n) => setAccountId(n.id)}
  aria-label="Chart of accounts"
/>
```

Controlled — expansion in URL/store state, children loaded on demand:

```jsx
<TreeView
  items={nodes}
  expanded={openIds}
  onExpandedChange={setOpenIds}
  onToggle={(node, open) => open && !node.children && loadChildren(node.id)}
  selectedId={sel}
  onSelect={(n) => setSel(n.id)}
/>
```

Lazy and disabled nodes: `{ id, label, hasChildren: true }` declares a branch before its children exist, `loading: true` marks it busy, `childCount` shows a count on a collapsed branch, `disabled: true` removes a node from selection and every keyboard motion. Give `text` when `label` is not a plain string so typeahead still works.
