What changes if the proposed action proceeds — the forward-looking twin of `EvidenceRail`.

```jsx
<ConsequenceRail
  object={{ type: "journal", id: "JE-20418", version: 3 }}
  proposal={{ id: "CS-1042", changes: staged }}
  consequences={{
    financial: [{ label: "STAT income", delta: 17256.38, currency: "USD", formatted: "17,256.38" }],
    controls: [{ id: "C-204", label: "Price approval", effect: "Re-runs" }],
    approvals: [{ id: "July close", label: "July close attestation", effect: "Superseded" }],
    reversibility: "compensatable",
  }}
  guards={guards}
  onInspect={(kind, item) => openImpact(kind, item)} />
```

- **Reversibility first.** `reversible · compensatable · supersedable · irreversible` — the class
  decides how much of the rest matters, so it is the first thing read, with its consequence spelled
  out in words. The vocabulary is imported from `DecisionFlow`; this rail declares none.
- **Grouped by who feels it** — financial · accounting · controls · reports · approvals. Each group
  states its own count; an empty group is not rendered.
- **Effects are words** (`Re-runs`, `Reopens`, `Superseded`), never a bare colour.
- **Guards sit with the consequence.** A blocking guard is listed and counted here rather than only
  disabling the primary button, so the operator can see what to go fix.
- **No consequence is not "no rail".** With nothing declared it says so: an action with no stated
  consequence is not ready for review.
- Pair with `DecisionSnapshot` — the consequences shown here are part of what gets frozen.
