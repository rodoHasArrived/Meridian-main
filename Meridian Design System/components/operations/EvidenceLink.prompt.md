A clickable reference to an evidence artifact — status dot, label, mono route, open arrow. Renders as a link when `href` is set, a button otherwise. Use inside `ReadinessPanel`/`GateRail` contexts to back a readiness claim with something an operator can actually open.

```jsx
<EvidenceLink label="Recon pack" status="Ready" route="evidence://recon/2026-06" href="/evidence/recon" />
<EvidenceLink label="Approval" status="Missing" route="evidence://approval/pending" onOpen={openApproval} />
```

`status` shares the same five-severity vocabulary as `SeverityBadge` — pass the raw domain status string (e.g. `"ReviewRequired"`), don't remap it yourself.
