The canonical exception-resolution surface: evidence, cause, correction, impact, ownership, and
rationale — in a contextual side region that keeps the originating row or figure on screen.

```jsx
<ResolutionDrawer caseId="PX-00418" version={7} severity="exception"
  statement="Evaluated price differs from the approved source by 84 bp of market value."
  fields={priceFacts} evidenceItems={documents}
  rootCauses={causes} rootCause={cause} onRootCauseChange={setCause}
  correctionFacts={correction} impactItems={impact}
  ownership={{assignee:"A. Rivera", team:"Valuation control", due:"2026-08-12", age:"2 days"}}
  requireReason primaryAction="resolve" onResolve={reason => ...} onEscalate={...} />
```

- Severity: `blocked` · `action` · `exception` · `review` · `ready` · `info`.
- Root cause is a controlled classification (so exceptions aggregate) plus optional free text.
- Exactly one primary action; everything else is secondary. Resolution rationale can be required.
- Evidence items mark what is **missing**, not only what is attached.
- On narrow viewports the drawer becomes a full-width region with an explicit return path.
