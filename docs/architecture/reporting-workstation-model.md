# Reporting Workstation Model

**Status:** Active
**Owner:** Workstation Platform
**Reviewed:** 2026-09-12

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
