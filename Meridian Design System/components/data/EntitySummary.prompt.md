Identity fact band for an entity header (security, run, account) — like `KeyValueGrid` but with per-item `mono`/`color` control and ellipsis truncation.

```jsx
<EntitySummary columns={4} items={[
  { label: "Name", value: "Apple Inc.", mono: false },
  { label: "FIGI", value: "BBG000B9XRY4" },
  { label: "Status", value: "Active", color: "#26BF86" },
  { label: "Last bar", value: "2026-06-09 20:00Z" },
]} />
```
