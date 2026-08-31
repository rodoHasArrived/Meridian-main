Hover/focus tooltip for terse supplementary text — a full timestamp behind a relative one, a definition, a truncated value. Wraps any trigger element.

```jsx
<Tooltip content="2026-07-02 14:32:08 UTC" placement="above">
  <span>2m ago</span>
</Tooltip>
```

Keep `content` to a phrase — it's a hint, not a panel. For rich or interactive content use `Popover`; for an action menu use `ContextMenu`.
