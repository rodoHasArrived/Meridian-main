The single most-reused status chip in the operator readiness layer — collapses any domain status string onto five canonical severities (ready · review · action · blocked · info) via `normalizeSeverity`, so you never hand-map a status to a color yourself.

```jsx
<SeverityBadge status="ReviewRequired" />
<SeverityBadge status="Blocked" label="3 breaks" />
```

Reach for this before `Badge` whenever the value is a *readiness/gate/validation* status rather than a generic tag — it's what keeps "Ready," "Passed," "Healthy," and "Approved" all rendering identically across every workstation.
