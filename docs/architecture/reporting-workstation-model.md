# Reporting Workstation Model

**Status:** Active
**Owner:** Workstation Platform
**Reviewed:** 2026-09-16

The reporting workstation treats Reporting as a production environment rather than a page that
lists reports. Its purpose is to convert governed analytical state into controlled institutional
communication, so the surface is organised around a pipeline and a controlled vocabulary instead of
a report gallery.

This document describes the browser-workstation domain modules that implement that model. They are
pure TypeScript, free of React, and unit-tested directly, so the same shapes can back the WPF parity
lane over the shared read-model seam without either client forking product state.

## Production Pipeline

Reporting runs as **Plan → Prepare → Review → Approve → Publish → Preserve**, surfaced as seven
lanes. Each lane answers a different operator job and routes to an existing workstation route.

| Lane | Stage | Job | Route |
| --- | --- | --- | --- |
| 01 Library | Plan | Find everything Meridian can produce | `/reporting/library` |
| 02 Production | Prepare | Monitor reports being prepared this period | `/reporting/run-status` |
| 03 Builder | Prepare | Construct or modify a report | `/reporting/report-builder` |
| 04 Review | Review | Check data, commentary and changes before publication | `/reporting/preview` |
| 05 Published | Preserve | Canonical immutable output history | `/reporting/report-packs` |
| 06 Schedules | Plan | Recurring reporting obligations | `/reporting/scheduled` |
| 07 Templates | Approve | Governed report and module definitions | `/reporting/governance` |

`lib/reporting-production.ts` builds the control surface from workstation run, template and schedule
rows. The production register is the dominant plane: one row per report (repeated run attempts are
collapsed to the current attempt), ranked blocked first, then past due, then by lifecycle position,
so the rows needing attention sort themselves to the top.

## Controlled Vocabularies

`lib/reporting-lifecycle.ts` keeps four axes separate that are otherwise collapsed onto the word
"status". This separation is what prevents semantic confusion downstream: a report can legitimately
be `Approved` (workflow) while an element behind it is `Provisional` (data) and its reconciliation
is `WithinTolerance` (control).

- **Workflow state** — `NotStarted`, `Preparing`, `Blocked`, `ReadyForReview`, `InReview`,
  `ChangesRequested`, `ReadyForApproval`, `Approved`, `Publishing`, `Published`, `Superseded`,
  `Restated`.
- **Data state** — `Confirmed`, `Provisional`, `Estimated`, `Stale`, `Overridden`, `Missing`,
  `Exception`.
- **Control state** — `Passed`, `WithinTolerance`, `Failed`, `NotTested`, `Waived`.
- **Freeze state** — `Open`, `SoftFrozen`, `HardFrozen`, `Published`.

Report blocks carry their own narrower state (`Live`, `Snapshot`, `Stale`, `Changed`, `Overridden`,
`Blocked`, `Missing`) because a block additionally distinguishes whether it tracks its source or was
pinned to a captured value.

Every vocabulary normalizes onto the five canonical operator severities in `design-system/status`,
so no new status tokens are introduced. Normalization is deliberately pessimistic: an unrecognized
workflow string resolves to `Preparing`, an unrecognized data state to `Provisional`, an
unrecognized control state to `NotTested`, and an unrecognized block state to `Stale`. An unknown
value never reads as safe to publish.

Report classes (`Accounting`, `Analytical`, `Portfolio`, `Management`, `Regulatory`) carry their own
governance requirements so templates, review rules and publication gates vary by class.

## Reporting Period

`lib/reporting-period-object.ts` models the reporting period as a first-class object rather than a
global date picker. It tracks five governed milestones — period end, valuation date, accounting
close, reporting cutoff and publication — and the snapshots that make a period reproducible
(portfolio, benchmark, FX, pricing cutoff, ledger version).

A report published for June must stay reproducible from June's governed state, so the model reports
`isReproducible` only when every required snapshot is bound, and names each unbound one.

The period also integrates with the accounting close. When a controlled reopen occurs, the ledger
version moves after a report freeze, or the close lands after the freeze, the model sets
`requiresRefreezeReview` with the reason. A period whose ledger basis is being revised can never
read as `Published`, even when the source system declares it so.

## Report Health and Publication Gates

`lib/report-health.ts` separates progress from permission.

- **Dimensions** (content, data, reconciliation, commentary, review, approval) report progress and
  are purely informational. A dimension that does not yet apply renders an em dash rather than 0%.
- **Gates** decide publication and are evaluated independently. The overall state is the worst gate
  outcome — never an average — so a single material reconciliation failure cannot be diluted by
  everything else being green.

Default gate policies vary by report class (a statutory schedule requires 100% data coverage; an
ad-hoc analytical note requires neither review nor approval) and a template may override individual
thresholds without discarding the rest of the policy.

`buildSourceCoverage` supplies the data dimension by counting report elements against the data-state
vocabulary and breaking them down by source. Coverage counts elements that resolved to a usable
value: stale and overridden elements still carry a value, missing ones do not. Materiality is
explicit — an exception counts as material when the template flags it or when it sits on an element
the template marked critical.

## Change Since Review

`lib/report-change-since-review.ts` answers "what moved since this report was reviewed?" without
asking the reviewer to reread the report. It maps upstream changes that landed after the review
timestamp onto the report elements they moved, with the value before and after.

The freeze state decides what happens to those changes:

- `Open` applies change in place; the model exists to disclose it, and affected blocks read
  `Changed`.
- `SoftFrozen` holds change for a preparer decision (review, apply, or remain frozen). Affected
  blocks read `Stale`, because what the reader sees no longer matches the source.
- `HardFrozen` records change against the frozen basis and requires an explicit unfreeze.
- `Published` routes change toward a restatement assessment.

A report that has never been reviewed has no baseline to diff against, so it reports no changes
rather than reporting every change as new.

## Materiality Policy

Materiality is a stated policy rather than an upstream boolean. `reporting-materiality.ts` holds
five independently optional thresholds — absolute variance, portfolio percentage, performance
impact in basis points, missing-position share, and source freshness — so Meridian can distinguish
a $847 difference that is within tolerance from a $284,711 difference that is a material exception,
and name the threshold that decided it.

Three properties matter:

- **An unassessable difference is not within tolerance.** When no threshold governs a dimension, or
  the inputs needed to apply it are missing, the outcome is `NotAssessed`, which carries a `review`
  severity. Silence has to look different from a pass, because the two mean opposite things to a
  preparer deciding whether to publish.
- **Any single breach is material.** Dimensions are not averaged or voted on: a variance trivial in
  dollars can still move performance past its threshold.
- **A threshold admits its own value.** Comparison is `>`, not `>=`, so a $100,000 tolerance admits
  a $100,000 difference.

Default policies vary by report class — regulatory output is governed tightly, analytical output
deliberately loosely — and a template overrides individual thresholds without discarding the rest.
`resolveMaterialityPolicy` distinguishes an omitted override from an explicit `null`, which
deliberately ungoverns a dimension.

`buildSourceCoverage` accepts a policy as an option. With one, element variances are measured
against the stated thresholds and a computed breach counts alongside the declared ones; without
one, the previous declaration-only behaviour is unchanged. Elements carrying a variance the policy
cannot assess are counted in `unassessedMaterialityCount`, which is not the same as immaterial.

## Reporting Datum

A number on a published page is not just a value. `reporting-datum.ts` models the atomic governed
value with the four provenance attributes that travel with it to publication — source, state,
as of, owner — plus its movement against a baseline and the materiality of that movement. Absent
attributes render as "Not set" rather than being omitted, because a block with no recorded owner is
a governance gap worth seeing.

Block state is derived when the caller does not assert one: a moved value is `Changed`, an absent
value is `Missing`, and an overridden or stale data state carries through. Defaulting to `Live`
would let a moved number present as settled.

The module also builds the **coverage field**, the repeated-mark summary used for completion,
source coverage, and review coverage. It is capped at twenty marks; beyond that, marks become
proportional and the label carries the real figures. Proportional marks round *down*, so a full
field means the population is genuinely complete — rounding up would let 99% read as finished.

## Lineage

`reporting-trace.ts` orders a value's lineage across six stages: source, normalization,
calculation, reconciliation, report block, publication. Two properties carry the model:

- **Gaps are stated, not skipped.** A trace with no reconciliation is a six-stage trace with a hole
  in it, not a five-stage trace. Source and report block are always required; callers name any
  further stage their report class requires. A step at an unrecognised stage is retained as
  unresolved rather than dropped or placed by guesswork.
- **Calculations must tie.** `buildCalculationTrace` checks a component breakdown against the total
  it claims to explain and reports any residual explicitly, at `action` severity. A component with
  no usable value is excluded and named rather than treated as zero, which would manufacture a
  tie-out that is not real.

`buildDownstreamUsage` answers the other direction — which reports consume a value — and sorts
published consumers first, because a change touching a published report is a restatement decision
rather than an edit.

`reporting-provenance-adapter.ts` maps the workspace's existing record graph
(`reportLineProvenanceExplorer`) onto these stages. That graph already carries real provenance; what
it lacks is stage ordering. The read service emits a placeholder node for every relationship slot it
cannot fill, so the adapter reports those as gaps rather than as completed stages — a node counts as
a placeholder only when its href is empty *and* its label is identical to its node type.

`ReportingLineageSummary` renders that adapted lineage inside the existing Report-Line Provenance
Explorer, which previously had no children. The explorer already draws the record graph; what it
could not show is *where a chain stops*, since a record with no reconciliation looks structurally
identical to one that has it. The summary places each record on the six-stage order and names the
stages that produced nothing. It is the only one of these modules currently rendered — the rest are
model-layer ahead of their surfaces, because the reporting workspace read model does not yet return
report-element-level data.

## Impact

`reporting-impact.ts` shows what a change touches before it is committed: a source replacement
(#71) or an upstream event propagating into the reporting estate (#115). The controlling rule is
that **undeterminable impact counts as affected** — a block whose dependency cannot be evaluated is
reported as impacted and flagged unresolved, never quietly dropped. A preview that understates its
own blind spots is worse than none, because it is acted on with confidence.

Blocks are ranked published-first, then unresolved, then expected. A replacement naming the same
source on both sides reports as no change rather than as an impact-free change. Event impact counts
distinct reports that had already reached a reviewed state, since those are where a moved value
invalidates work somebody already signed off.

## Approval and Attestation

Approval means something only when it binds to a specific version of specific data. An approval
that survives the data moving underneath it is not a control — the approver signed off on numbers
that no longer exist. So `reporting-approval.ts` records the version and the data fingerprint an
approval was given against, and `evaluateApproval` compares both with the report's current state:

- **Approved** — the approved version and data are still what is on the page.
- **ChangesRequireReview** — somebody approved something, but the version or the data has moved.
  The approval neither stands nor vanishes; both facts are stated.
- **Indeterminate** — an approval exists that cannot be tied to a version or a data fingerprint,
  so its currency is unknowable. This carries `action` severity, not a pass.

The data *fingerprint* is what currency is judged on, not the data *state*: a report can stay
"Confirmed" while every number underneath it moves.

Attestation follows the same principle. Every required statement must be explicitly affirmed — an
unanswered requirement blocks attestation rather than being assumed satisfied, and an attestation
with no named approver is unsigned and therefore not an attestation. Requirements are set by report
class, and `resolveAttestationRequirements` unions rather than replaces, so a template can require
more than its class floor but never less.

## Publication and Restatement

Publishing creates an immutable record fixing what was said, when, by whom, against which data
cutoff and which ledger version. A correction does not edit v09; it publishes v10 with an explicit
relationship to the version it supersedes and a stated reason. Overwriting would destroy the only
evidence of what recipients actually received.

`buildRestatementChain` orders versions along their supersede relationships and reports breaks
rather than smoothing them over — a missing predecessor, two versions superseding the same one, a
chain that closes on itself with no origin, or records the walk never reaches. A reader needs to
know when the history they are looking at is not the whole history.

The internal/external boundary is fail-closed. An output whose distribution class was never stated,
or stated as something unrecognised, is treated as **internal**; evidence and archive outputs stay
internal whatever a caller declares, because they carry reviewer comments, accepted exceptions and
manual overrides. Wrongly withholding an output costs a question; wrongly releasing one cannot be
recalled.

`buildDraftStateMarking` supplies the header/footer state treatment. A report is approved for
distribution only once it is published — `Approved` means signed off but not yet released, and an
approved-but-unpublished document leaving the building is exactly the accident this marking exists
to prevent.

## Distribution and Retention

`reporting-distribution.ts` keeps the published/distributed/delivered/archived/superseded timeline.
An event whose timestamp cannot be read is retained in `unplacedEvents` rather than dropped or
sorted to the front: a distribution that happened is a fact even when its clock reading is unusable,
and losing it would understate who holds a copy.

Retention is where the fail-closed rule matters most. `isDisposable` is true only for `Eligible`.
A record with **no** retention policy is `Unclassified` and undisposable — treating "no policy" as
"no obligation" would let the least-governed records be destroyed first, which is precisely
backwards. A record whose retention clock cannot be read is likewise undisposable.

## Dataset Certification

`reporting-certification.ts` applies the version-binding rule one layer down. A certified dataset is
an assertion by an accountable team that a body of data is fit to report on; reports consuming it
inherit that assertion, which is both the useful part and the dangerous part, because an inherited
certification is invisible until it is wrong. Certification is bound to a fingerprint, so a dataset
that changes afterwards is **invalidated** rather than quietly carried forward.

Inheritance takes the worst state across a report's datasets, never an average — one invalidated
dataset is enough to make a report's numbers unsupported. A report consuming no dataset at all is
`Uncertified` rather than trivially certified, and `propagateCertificationToBlocks` marks a block
naming a dataset it cannot see as `Indeterminate`: an unseen dependency is not a certified one.

## Surface Composition

`components/meridian/reporting-production-surface.tsx` renders the control surface, and
`screens/reporting-screen.production-surface.ts` adapts the existing reporting view-model onto it.
The adapter projects only data the workspace already returns. Where a governed fact is genuinely
absent — report owner, accounting close, reporting cutoff — it is left unset so the surface reports
"not set" rather than implying a milestone or owner no system has asserted. The owner column is
omitted entirely until at least one report has a known owner.

## Files

| Concern | Module |
| --- | --- |
| Controlled vocabularies, report classes, block and freeze states | `src/Meridian.Ui/dashboard/src/lib/reporting-lifecycle.ts` |
| Reporting period, milestones, snapshots, close integration | `src/Meridian.Ui/dashboard/src/lib/reporting-period-object.ts` |
| Report health, source coverage, publication gates | `src/Meridian.Ui/dashboard/src/lib/report-health.ts` |
| Change since review, freeze admission | `src/Meridian.Ui/dashboard/src/lib/report-change-since-review.ts` |
| Production pipeline lanes and control-surface model | `src/Meridian.Ui/dashboard/src/lib/reporting-production.ts` |
| Materiality policy and threshold assessment | `src/Meridian.Ui/dashboard/src/lib/reporting-materiality.ts` |
| Reporting datum, provenance, coverage field | `src/Meridian.Ui/dashboard/src/lib/reporting-datum.ts` |
| Lineage stages, calculation tie-out, downstream usage | `src/Meridian.Ui/dashboard/src/lib/reporting-trace.ts` |
| Record-graph to lineage adapter | `src/Meridian.Ui/dashboard/src/lib/reporting-provenance-adapter.ts` |
| Source-replacement and event impact | `src/Meridian.Ui/dashboard/src/lib/reporting-impact.ts` |
| Approval currency and attestation | `src/Meridian.Ui/dashboard/src/lib/reporting-approval.ts` |
| Publication records, immutability, restatement chain | `src/Meridian.Ui/dashboard/src/lib/reporting-publication.ts` |
| Distribution history and retention | `src/Meridian.Ui/dashboard/src/lib/reporting-distribution.ts` |
| Dataset certification and inheritance | `src/Meridian.Ui/dashboard/src/lib/reporting-certification.ts` |
| Lineage summary surface | `src/Meridian.Ui/dashboard/src/components/meridian/reporting-lineage-summary.tsx` |
| Control surface component | `src/Meridian.Ui/dashboard/src/components/meridian/reporting-production-surface.tsx` |
| View-model adapter | `src/Meridian.Ui/dashboard/src/screens/reporting-screen.production-surface.ts` |

Each module has a colocated `*.test.ts` covering its vocabulary mapping, boundary conditions, and
the fail-safe defaults described above.

## Related

- [Evidence Workflow Fabric](evidence-workflow-fabric.md) — evidence packets and lineage that
  reporting elements reference.
- [Ledger Architecture](ledger-architecture.md) — the accounting close and ledger versions the
  reporting period binds to.
- [Governed Reporting Operations](../operators/governed-reporting-operations.md) — operator-facing
  reporting procedures.
