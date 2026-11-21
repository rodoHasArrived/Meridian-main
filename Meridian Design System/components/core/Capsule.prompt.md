The canonical configurable workspace module. Use standard heights (`xs` 144, `sm` 240, `md` 336, `lg` 432), a fixed header, a single function, explicit data state, and a provenance-oriented footer.

```jsx
<Capsule title="Portfolio pulse" context="General Account · STAT · July 2026"
  size="sm" state="ready" collapsible storageKey="portfolio-pulse"
  footer="Source: validated holdings composite">
  <MetricCard label="Book yield" value="4.72%" />
</Capsule>
```

`movable` and `resizable` expose affordances only; the consuming workspace owns drag/grid behavior. Do not turn every bordered region into a capsule.
