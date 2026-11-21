# The workstation hierarchy — context, object, evidence, consequence, action

**Normative.** Every major Meridian workstation reads top to bottom in the same five layers. The
layers are not a visual template — a screen may lay them out as columns, rails, or sheets — but
their order and their ownership never change. An operator who has learned one workstation has
learned where everything lives in all of them.

```
GLOBAL CONTEXT      Entity · portfolio · book · basis · period · currency · as-of · scenario
        ↓
ACTIVE OBJECT       Security · position · account · journal · reconciliation · exception · control
        ↓
EVIDENCE            Sources · calculation · lineage · documents · audit history
        ↓
CONSEQUENCE         Difference · impact · dependency · what this decision invalidates
        ↓
ACTION              Resolve · post · attest · approve
```

---

## What each layer owns

| Layer | Answers | Owner | Declared |
| --- | --- | --- | --- |
| Context | Where am I working? | `WorkspaceContextProvider` + `WorkspaceContextBar` | Once, in the chrome |
| Object | What am I working on? | `ObjectHeader` + `LinkedSelectionProvider` | Once, under the context bar |
| Evidence | Why should I believe it? | `EvidenceInspector`, `CalculationTrace`, `EvidencePreview`, `SourceCoverageSummary`, `ProvenanceChip` | A persistent rail or inspector |
| Consequence | What happens if I act? | `PostingImpactPreview`, `ControlDependencyGraph`, `Delta`, `VarianceCell` | Adjacent to the action, never behind it |
| Action | What do I do? | `Button` (one primary), `ResolutionDrawer`, `AttestationPanel` | A sticky consequence bar |

## The rules that make it hold

**Context is declared once and inherited.** No panel below the bar carries its own period,
currency, or basis control. A number whose scope cannot be answered from the context bar is lying by
omission.

**The object is named once.** One `ObjectHeader` per screen, between the context bar and the work
plane. When the committed selection changes, the header changes — which is why selection lives in
`LinkedSelectionProvider` and not in a screen's local state.

**Evidence is persistent, not modal.** The reason to believe a figure must be reachable without
leaving the decision. An evidence rail that only opens as a dialog forces the operator to choose
between the claim and its support.

**Consequence precedes action.** The screen states what the action changes — which approvals are
invalidated, which controls reopen, which downstream tasks release — before the action is
available. An enabled primary button with no stated consequence is the system's most expensive
defect.

**One primary action per plane.** Everything else is secondary or lives in the object's own menu.
If a screen genuinely offers two equal primary actions, it is two screens.

**Every layer degrades honestly.** Context reports its own data condition. The object header reports
coverage and exceptions. Evidence retains its last valid snapshot under `ContinuityFrame` rather
than blanking. Actions lock when the evidence beneath them is stale or the guard is unmet, and say
which guard.

---

## Reading the layers in a real screen

| Workstation | Context | Object | Evidence | Consequence | Action |
| --- | --- | --- | --- | --- | --- |
| Reconciliation exception | Entity · book · period · as-of | The exception, its residual and version | Both sources, tie-out trace, audit history | Which control the residual blocks | Resolve, or escalate |
| Close management | Entity · period · close deadline | The selected control | Execution record, sign-off, source coverage | Downstream controls released, hours off the critical path | Unblock, or reassign |
| Asset-liability review | Entity · book · basis · scenario · as-of | The selected horizon bucket | Cash-flow classification, model version, assumptions | Base versus scenario change in the gap | Publish scenario, or escalate |
| Posting review | Entity · book · period | The journal entry and its version | Supporting documents and calculation | Trial balance before/after, approvals invalidated | Post, or return |
| Attestation | Entity · book · reporting period | The attestation and its scope | Evidence-set snapshot, changes since prior attestation | Gates unsatisfied, what re-attestation reopens | Sign |

## Layout is free; order is not

The five layers may be arranged as:

- **Standard workstation** — context bar, object header, three columns (queue · plane · evidence), sticky consequence bar.
- **Narrow workstation** — context bar, object header, plane with the evidence rail as an inspector tab.
- **Tablet** — context summary, object header, mutually exclusive views, evidence as a focused panel.
- **Mobile** — context summary, object header, evidence before configuration, consequence and action as a bottom bar.

What may not change: context above object, evidence before consequence, consequence before action.
A mobile layout that puts filters above the evidence has inverted the hierarchy, not compressed it.

---

## Review checklist

A screen is not ready until:

1. Its scope is declared once, in the context bar, and inherited everywhere below.
2. Its object is named once, with version, condition, as-of, coverage, and open exceptions.
3. Committed selection lives in `LinkedSelectionProvider`, never in template state.
4. Evidence for the dominant plane is reachable without dismissing the decision.
5. The consequence of the primary action is stated before the action is enabled.
6. Exactly one primary action is present, and every blocked action names its guard.
7. Degradation retains evidence rather than blanking it.
8. The same five layers are identifiable at 390 px and at 1920 px.
