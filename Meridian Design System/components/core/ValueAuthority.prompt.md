Name which of several legitimate versions of a value Meridian is using, and why. Use when a value can exist as a source figure, a calculated figure, a manual override, an approved amount, a posted amount and a reported amount. `AuthorityStack` for the lineage rail, `AuthorityChip` inline beside a value, `AuthorityPanel` for the full candidate table with actor, version and condition.

```jsx
<AuthorityStack unit="USD" authority={{
  authoritativeStage:"approved", policyRef:"AC-14 §4.2",
  rationale:"The approved July amount stays authoritative until the reopened period closes.",
  candidates:[
    {stage:"source",value:10248100,sourceRef:"custodian:2026-07-31",condition:"confirmed"},
    {stage:"calculated",value:10249840,version:"EIR-4.2"},
    {stage:"override",value:10251000,actorId:"a.patel",condition:"overridden"},
    {stage:"approved",value:10251000,version:18,effectiveAt:"2026-08-01",actorId:"m.chen"},
    {stage:"posted",value:10251000,version:18}]}} />
```

Say "authoritative", never "true" — authority is contextual. Never render a bare stage color; the stage is always named. Do not use this for data condition, freshness or materiality: those are separate channels.
