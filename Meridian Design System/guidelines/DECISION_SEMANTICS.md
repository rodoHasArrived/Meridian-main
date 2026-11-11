# Decision semantics

Status: **staged, unreleased.** Additive to 3.4.0; no existing API changes behaviour.

Meridian's spine is one sentence: **context → object → evidence → consequence → decision**. Everything
in this document exists so that a workstation expresses that sentence with shared contracts instead of
local screen logic. Five surfaces answer five different questions, and they must not blur:

| Question | Owner |
|---|---|
| Where am I, and as of when? | `WorkspaceContextProvider`, `TimeContextProvider` |
| What object am I operating on? | `ObjectHeader` + the object model |
| Why is this value in this state? | evidence family, `CalculationTrace`, `ValueAuthority` |
| How consequential is it? | `MaterialityAssessment` |
| May I act, and what happens if I do? | `ActionGuard`, consequence surfaces, `DecisionFlow` |

## ValueAuthority — which version of a value is in force

A financial value legitimately exists in up to six versions: **source, calculated, override,
approved, posted, reported**. Components must not guess which one is "current", and Meridian never
says *true* — it says **authoritative**, because authority is contextual. An approved statutory
amount and a later custodian file can both be valid in their own roles.

```jsx
<AuthorityStack unit="USD" authority={{
  authoritativeStage:"approved", policyRef:"AC-14 §4.2",
  rationale:"The approved July amount stays authoritative until the reopened period closes.",
  candidates:[
    {stage:"source",value:10248100,sourceRef:"custodian:2026-07-31",condition:"confirmed"},
    {stage:"calculated",value:10249840,version:"EIR-4.2"},
    {stage:"override",value:10251000,actorId:"a.patel",condition:"overridden"},
    {stage:"approved",value:10251000,version:18,actorId:"m.chen"},
    {stage:"posted",value:10251000,version:18}]}} />
```

Rules. Every candidate carries its own version, actor, effective date and condition — a stage with
no provenance is not a candidate. The authoritative stage is stated in words next to the value, never
by color alone. A surface that offers a consequential action must be able to name the authority stage
of every value the action depends on.

## MaterialityAssessment — how consequential, independent of tolerance

`materiality = abs(variance) > tolerance` is wrong and Meridian will not ship it. Tolerance is a
measurement rule; materiality is a judgment with quantitative *and* qualitative inputs. The four
classifications are **immaterial · operational · financial · reporting**, and the assessment carries
its own policy id and version, its benchmark, and a factor checklist with rationales.

Four conclusions may legitimately disagree, and Meridian renders them as four statements:

```text
Variance       $331.68
Tolerance      Within $500 policy REC-CASH-01
Materiality    Operational — third recurrence this quarter
Evidence       Complete · 3 of 3 primary sources confirmed
Control state  Review required
```

## ActionGuard — eligibility as one contract

Guards are `satisfied`, `warning` or `blocking`, and they are visible **before** the attempt. Each
carries a label, a reason, the policy it comes from, the evidence it rests on, and — where one
exists — a named remediation with the authority it requires.

Neither hide a blocked action nor ship an unexplained disabled button. State the reason, the
resolution and the authority:

```text
Post journal · Unavailable
Reason              July STAT period is hard closed.
Resolution          Request adjustment period
Required authority  Controller
```

Blocking guards stay discoverable and focusable so their presence and state can be perceived by
keyboard and assistive technology. Guards render at **review density** — `--row-review`, the tallest
row rhythm in whichever density scope is active (44 px at standard density) — never compressed to
ledger density because they happen to live in an accounting workstation.

## DecisionFlow — ordered, guarded, version-aware

`DecisionFlowProvider` owns step order, the requirement graph, guard results, URL and reload
restoration, transition history, and version invalidation. Templates must not keep their own
`step` / `isValid` / `hasReviewed` state.

The state machine templates should assume:

```text
Draft → Validating → Blocked ⇄ Validating → Ready → UnderReview
UnderReview → Conflict (object version changed) → Validating
UnderReview → AwaitingAuthority → Committed
Committed → Superseded (material underlying change) → Draft (reopen / new version)
```

Version change is the case local step state always gets wrong. When the object's version moves, the
provider clears every completed step except those declared `stableAcrossVersions`, drops cached guard
results, and surfaces an acknowledgement notice. An impact preview or approval generated against an
earlier version is **stale by definition**, not merely old.

## Reversibility — declared, not implied

Every consequential action declares one of four classes, and the consequence surface explains the
actual recovery path rather than the word "undo".

| Class | Meaning | Example |
|---|---|---|
| `reversible` | Undone without a compensating record | Draft field edit |
| `compensatable` | Original remains; reversal creates a new record | Posted journal |
| `supersedable` | New version replaces the effective state; prior version remains | Attestation, approved override |
| `irreversible` | Cannot practically be reversed — strongest friction | Rare |

## EvidenceRail and ConsequenceRail — patterns, not new cards

Both are **compositions of components that already exist**. Do not build a generic "evidence card"
or "warning drawer".

**EvidenceRail** answers *why is this value or object in this state?* Content order: coverage →
primary sources → derivation → authority → lineage → decision and audit history. It is driven by the
committed selection, through `LinkedSelectionProvider` — not by its own local state.
Parts: `SourceCoverageSummary`, `EvidencePreview`, `EvidenceInspector`, `CalculationTrace`,
`AuthorityStack`, `LineageGraph`, `EventTimeline`.

**ConsequenceRail** answers *what changes if I proceed?* Content order: financial effect → records
affected → controls → approvals → reversibility.
Parts: `PostingImpactPreview`, `ControlDependencyGraph`, `ActionGuardList`, `DecisionConsequenceBar`.

Keep them separate. One substantiates the present state; the other predicts the effect of a proposal.
A rail that mixes both makes it impossible to tell what is true from what is proposed.

## Bulk actions need population proof

Selection is not evidence of homogeneity. Before a bulk consequential action, prove the population:
scope (`selected / filtered / total`), a shared root-cause classification, one accounting period lock
state, one authority requirement, comparable consequences, and evidence completeness. Block the
action when the set crosses any of those boundaries, and say which boundary it crossed.

## Decision provenance

A committed decision persists a snapshot rather than being reconstructed from UI logs later: object
ref and version, context and time-context snapshot ids, evidence refs (and set hash), calculation and
policy refs, guard results, materiality, the decision verb, rationale, actor with authority, and
timestamp. A materially relevant underlying change marks a prior attestation **superseded** — both
records survive in the audit history.

These are design-governance patterns for traceability and reproducibility. They do not by themselves
make any product compliant with any audit standard or regulation.
