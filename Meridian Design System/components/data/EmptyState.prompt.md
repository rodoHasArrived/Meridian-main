The empty slot of the data-state ladder — a labeled icon, a one-line explanation, and an optional recovery action. Use it for no-data, no-results, and no-selection — never a blank panel.

```jsx
<EmptyState icon="inbox" title="No open alerts"
  detail="All rules are quiet. New alerts appear here the moment one fires."
  action="Review alert rules" onAction={openRules} />
```

Match the `icon` to the surface (`search` for no-match, plus `table`/`chart`/`docs`/`inbox`). Use `compact` inside inspectors and small panes. Give it an `action` whenever there's a sensible next step.
