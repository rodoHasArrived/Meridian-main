# Meridian Reporting: A More Focused Operating Model (2026-09)

**Status:** active working design input
**Owner:** core-team
**Reviewed:** 2026-09-14
**Scope:** reporting product semantics, object model, navigation, and release boundary
**Roadmap anchors:** `W4-RPT-001`, `W9-REPORT-005`
**Reconciled against:** `docs/architecture/reporting-workstation-model.md` (reviewed 2026-09-12, landed in PR #2963)

> This is a **working design input**, not a canonical status source. Live status stays in the
> roadmap registry (`docs/roadmap/data/roadmap-items.yml`). Every numerical example below is
> illustrative. Where this document describes current behaviour it cites the owning source file;
> where it describes a target it says so explicitly.

## 1. Why This Refinement Exists

The preceding reporting proposal added capability faster than it added coherence. Its capabilities
are worth keeping, but they are spread across too many destinations, too many overlapping statuses,
and objects whose boundaries are not stated precisely enough to reason about.

This refinement therefore adds almost no new reporting features. It makes the concepts Meridian
already has behave as one product.

**Refined product promise:**

> Prepare a report from a known institutional state, understand every consequential change, and
> publish exactly what was approved.

**North star:** the reporting workspace should look simpler than the earlier proposal while
behaving more rigorously. The distinctive experience is:

> The scope is explicit. The number is explainable. The change is reviewable. The publication stays
> fixed.

### Design-source traceability note

The originating critique cited a design-styles source (`Branch · Esoteric Design Styles.txt`) and a
five-primitive interface vocabulary (Datum, Field, Trace, Plate, Capsule). **Neither the cited file
nor that primitive vocabulary is checked into this repository.** A repo-wide search finds only one
incidental mention of "Datum-level labeling" in
`.claude/skills/meridian-brainstorm/references/competitive-landscape.md`.

The in-repo design authority is the `Meridian Design System/` package (`VISUAL_FOUNDATIONS.md`,
`PATTERNS.md`, `CONTENT_FUNDAMENTALS.md`, `BRAND_GUIDELINES.md`) plus the charter in
`docs/product/meridian-design-document.md`. Section 11 below therefore states the primitive mapping
as a **proposal to be reconciled against the design system**, not as a citation of existing
normative text. Adopting Section 11 requires either checking the design-styles source into
`Meridian Design System/` or restating the five primitives in `PATTERNS.md` first.

## 2. Current Source Evidence

The refinement is grounded in what the repository actually contains today.

| Concern | Current owner in source | Observed shape |
| --- | --- | --- |
| Central object | `src/Meridian.Reporting/ReportingContracts.cs` | `ReportingOutputManifest` keyed by `RunId`; the *run* is the central object |
| Lifecycle | `ReportingRunStatus` | One enum: `Draft`, `InReview`, `Approved`, `Released`, `Failed` |
| Approval | `ReportingApprovalAction`, `ReportingApprovalDecision` | `SubmitForReview`, `Approve`, `Release` |
| Scope fragments | `ReportingOperationalScope`, `ReportingAccessScope`, `ReportingCertifiedSnapshotScope`, `ReportingAuthoritativeSourceCheckpoint`, `ReportingRunParametersDto` | Several partial scope carriers, no single contract |
| Readiness / controls | `ReportingRunReadinessDto` (`src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs`) | `Status`, `Checks`, `BlockingReasons`, `CanGenerateDraft`, `CanGenerateFinal`, `EvidenceHash` |
| Delivery | `ReportingDeliveryState` (`ReportingDistributionContracts.cs`) | Already separate: `Queued`, `Dispatching`, `RetryScheduled`, `Sent`, `Delivered`, `Blocked`, `Failed` |
| Change detection | `ReportSnapshotDiffEngine`, `ReportWriterGridDiffDto` | Grid-cell diffing only |
| Immutable capture | `CertifiedReportingSnapshot`, `CertifiedReportingSnapshotBuilder`, `ReportingReleaseConsistencyGate` | Nearest existing release seam |
| Navigation | `src/Meridian.Ui/dashboard/src/components/meridian/workspace-nav.view-model.ts` | **Eight** Reporting subroutes |

Three findings drive most of this document.

**Finding 1 — the run, not the edition, is the central object.** Everything hangs off `RunId`.
Proto-edition concepts exist but are optional fields on the run manifest: `RunSeriesId`,
`RunAttemptOrdinal`, `PriorRunId`, `RetryReason`, `AllowRestatement`. There is no first-class
`Report`, `Edition`, or `Publication`.

**Finding 2 — `Revision` is already taken, and it means something else.**
`ReportingRunStoreRevision.Compute(...)` produces a **content hash used for optimistic
concurrency**. It flows through `IReportingRunStore.SaveAsync(..., expectedRevision, ...)` and
`ReportingRunConcurrencyException.ExpectedRevision`/`ActualRevision`. This is a storage concurrency
token, unrelated to the operator-facing idea of "revision 08". Section 3 records the naming decision
this forces.

**Finding 3 — `ReportingOutputManifest` is carrying the whole subsystem.** It is a single record
with **32 constructor parameters, 24 of them optional** and defaulted to `null` or `default`. That
width is the mechanical form of the "loosely defined objects" complaint: template identity, resolved
parameters, branding, access policy, retry lineage, grid artifacts, rendered grids, grid diffs,
readiness, operational scope, access scope, certified snapshot, authoritative source, certified
dataset rows, and a partners-capital projection all live on one type. Decomposing it is the
enabling refactor for nearly everything below.

## 2a. Reconciliation with the Landed Reporting Workstation Model

**This section supersedes parts of what follows.** After this document was first drafted, PR #2963
merged a reporting workstation implementation into `main`, documented in
`docs/architecture/reporting-workstation-model.md` and owned by Workstation Platform. Several
proposals below were gaps when written and are now substantially implemented. Leaving them stated as
gaps would misrepresent current state, so they are corrected here and flagged in place.

### What now exists that this document proposed

| Proposal | Now implemented in | Notes |
| --- | --- | --- |
| Four independent status dimensions (Section 9) | `src/Meridian.Ui/dashboard/src/lib/reporting-lifecycle.ts` | Implemented as **four** separate axes, and more developed than proposed |
| Exception acceptance distinct from passing (Section 10) | same, `REPORTING_CONTROL_STATES` | `Waived` and `WithinTolerance` are both distinct from `Passed` |
| Change review against the reviewed revision (Section 8) | `lib/report-change-since-review.ts` | `ChangeAdmissionAction` covers review, apply, remain frozen, unfreeze |
| Governance proportional to the report (Section 5) | `lib/report-health.ts` | Per-class gate policies via `defaultGatePolicyForClass` |
| Measurable source coverage (Section 10) | `lib/report-health.ts` | `buildSourceCoverage` with a per-bucket breakdown and share |
| An edition-like period object (Section 3) | `lib/reporting-period-object.ts` | `ReportingPeriodModel` with milestones, snapshots, close state |

The landed work is in several respects **better than this document proposed**, and those choices
should be treated as settled rather than reopened:

- It carries a **fourth axis this document lacks**: `REPORTING_FREEZE_STATES` of `Open`,
  `SoftFrozen`, `HardFrozen`, `Published`. `SoftFrozen` detects upstream change without applying it,
  which is a cleaner expression of the proposed-change-set idea in Section 8 than that section's own
  wording.
- Its control axis distinguishes `WithinTolerance` from `Passed`, a distinction Section 10 argues
  for but does not name.
- Health is reported per dimension with `percent: number | null`, where null means the dimension
  does not apply. That satisfies Section 9's requirement that not-applicable stay distinguishable,
  and it avoids the single overall health percentage Section 9 asks to retire, while keeping a
  separate pass/fail publication gate.

### What remains genuinely open

The C# contract layer is **unchanged** by that work. Verified against `src/Meridian.Reporting` at
the merge of `main` at `21788fdf`:

| Finding | Still true |
| --- | --- |
| `ReportingOutputManifest` has 32 constructor parameters, 24 optional | yes |
| `ReportingRunStatus` is one enum mixing lifecycle and outcome | yes |
| `Revision` means the store concurrency hash, not an edition revision | yes |
| No `Publication` type exists | yes |

So the four axes, the change model, and the gate policies currently live **only in the browser
workstation's TypeScript modules**. They are not expressed in the shared contracts, read models, or
API seam.

**That relocates the remaining work rather than removing it.** `CLAUDE.md` requires both the browser
and WPF workflows to be backed by shared contracts and API seams so neither client forks product
state. A vocabulary this load-bearing existing on one client only is exactly that fork. The
remaining gap is therefore to push these now-proven shapes down into the shared seam, and to
decompose `ReportingOutputManifest` and settle the `Revision` naming so they can land there. That is
the Stage 0 work in Section 15, and it is now better justified than when first written, because the
target vocabulary is no longer speculative.

### Where this document and the landed model disagree

**Navigation.** Section 4 proposes consolidating to Workbench, Reports, Publications and Manage. The
landed model instead organises Reporting as a seven-lane pipeline of Plan, Prepare, Review, Approve,
Publish and Preserve. Both are consolidations of the same eight nav subroutes, which are unchanged
in `workspace-nav.view-model.ts`, but they are different consolidations and cannot both be adopted.

This document does **not** override a just-merged architecture doc owned by another lane. Section 4
should be read as the alternative it is, and the choice between the two belongs to the owners of
both documents. It is recorded as an open decision in Section 16.

## 3. Simplify the Central Object: the Report Edition

The earlier concept used *report*, *instance*, *snapshot*, *version*, *publication*, and *package*
close to interchangeably. These need distinct meanings, and only three belong in front of users.

| Object | What it means | Example |
| --- | --- | --- |
| **Report** | The recurring reporting product: purpose, structure, ownership, requirements | Monthly Investment Report |
| **Edition** | That report prepared for a specific scope and period | August 2026 · General Account · Statutory |
| **Publication** | The approved, released output set from one exact edition revision | August edition, revision 08, published September 4 |

Supporting concepts, deliberately secondary:

- **Revision** — editing within an edition creates revisions. Monotonic, per edition.
- **Packet** — several reports optionally assembled together, such as an investment committee packet.
- **Cycle** — the work due together. A cycle groups work; it must **not** imply that every report in
  it shares an identical data cutoff.

### Why the distinction matters

A user opening the August edition must immediately know whether they are looking at its working
draft, a review revision, or its published record. Those are related views, not the same thing.

A publication must preserve its original content even when the report template or the underlying
data later changes. There is established precedent for this separation: reporting snapshots that
retain the result set, report definition, parameters, and embedded resources as they existed when
captured.

**Meridian refinement:** preserve both the approved analytical state *and* the actual released
files. Historical reconstruction and retrieving the original publication are two different
operations and should be exposed as two different operations.

### Naming decision (requires sign-off)

`Revision` cannot mean two things in one subsystem. Two options:

| Option | Change | Cost | Risk |
| --- | --- | --- | --- |
| **A (preferred)** | Rename the storage token to `StoreConcurrencyStamp`; reserve `Revision` for the edition ordinal | Mechanical rename across `IReportingRunStore`, `ReportingRunStoreRevision`, `ReportingRunConcurrencyException`, and their tests | Touches a durability seam; rename only, no behaviour change |
| **B** | Keep `Revision` for storage; name the operator concept `EditionRevision` everywhere | Cheaper | Leaves a permanent vocabulary trap: `run.Revision` and `edition.EditionRevision` differ in kind |

Option A is preferred because the operator vocabulary is the one that appears in publications,
approvals, and audit records, and should therefore own the shorter name. Either way the rename is
mechanical and must not alter concurrency semantics. Per repository guardrails, do not weaken
optimistic-concurrency or atomic-write behaviour while renaming.

### Gap summary

- Introduce `Report`, `Edition`, `Publication` as first-class objects.
- Decompose `ReportingOutputManifest` so each object owns its own fields.
- Promote `RunSeriesId`/`PriorRunId`/`RunAttemptOrdinal` into explicit edition and revision lineage.
- Keep the run: a run remains the *execution* that produces a revision. It stops being the identity.

## 4. Reduce Navigation to Three Daily Destinations
> **Superseded in part by Section 2a.** The landed reporting workstation model consolidates the
> same eight subroutes into a seven-lane pipeline instead. This section is the alternative
> proposal, not current direction; the choice is an open decision in Section 16.


**Constraint first.** Top-level operator navigation is fixed at `Trading`, `Portfolio`,
`Accounting`, `Reporting`, `Strategy`, `Data`, `Settings`. That seven-root invariant is normative in
both `CLAUDE.md` and the design charter. **This consolidation happens inside the Reporting root.**
It adds no top-level destination.

Today `workspace-nav.view-model.ts` gives Reporting eight subroutes: Overview, Report Library,
Scheduled Reports, Run Report, Operations record, Report packs, Evidence, Exports.

### Proposed destinations

| Destination | Primary question | What belongs here |
| --- | --- | --- |
| **Workbench** | What needs my attention? | Preparation, review, blockers, deadlines, cycle status |
| **Reports** | What reporting products exist? | Report catalog, edition history, creation |
| **Publications** | What was released? | Approved outputs, corrections, distribution records |
| **Manage** (secondary, permission-based) | How is reporting configured? | Templates, schedules, policies, reusable sections |

### Mapping from today's eight

| Today | Becomes |
| --- | --- |
| Overview | Workbench |
| Report Library | Reports |
| Run Report | Reports (edition creation) — a destination becomes an action |
| Operations record | Workbench saved view, plus the Publications distribution record |
| Report packs | Reports (packets are an assembly of reports, not a parallel catalog) |
| Evidence | Contextual inspector inside Prepare/Review — not a destination |
| Exports | Publications (outputs are a property of a publication, not a separate place) |
| Scheduled Reports | Manage |

Two of these are the substantive simplifications. **Evidence stops being a place** and becomes the
inspector that follows the current selection. **Exports stop being a place** and become the output
set bound to a publication, which is what Section 12 requires anyway.

Inside Workbench, use saved views rather than further destinations:

`My work · This cycle · Awaiting review · Blocked · All work`

Calendar and dependency views become alternative representations of the same work, not separate
systems.

### Change the dominant table

The production register should lead with **next action**, not status.

| Report edition | Next action | Reason | Responsible person | Due |
| --- | --- | --- | --- | --- |
| Investment Report · August | Review changes | Two financial values changed | Reviewer | Today |
| Investment Income · August | Resolve tie-out | Unexplained difference | Accounting owner | Today |
| Credit Review · August | Approve publication | Required checks completed | Approver | Tomorrow |
| Committee Packet · Q3 | Complete commentary | Two sections remain incomplete | Preparer | September 18 |

Selecting a row opens the contextual inspector; the register stays visible.

**Aggregate attention by root cause.** One pricing exception affecting three report editions is one
problem, not three. The register must group by cause and show the affected editions beneath it.

## 5. Make Governance Proportional to the Report
> **Largely implemented.** Per-class publication gate policies now exist in
> `lib/report-health.ts` via `defaultGatePolicyForClass`. Read this section as the rationale
> behind that, and see Section 2a for what remains.


A research note should not carry the same production ceremony as a controlled external report.

| Profile | Typical use | Default behaviour |
| --- | --- | --- |
| **Exploratory** | Analyst notes, scenario summaries, internal working papers | Fast composition; explicit draft status; captured context when shared |
| **Institutional** | Recurring portfolio, risk, and management reporting | Required checks, named review, pinned publication |
| **Controlled** | Reports subject to formal organizational or filing requirements | Explicit control policy, approval authority, evidence retention, release checks |

These are **product profiles, not substitutes** for applicable accounting or regulatory
requirements.

The load-bearing rule: **promoting an exploratory report does not simply remove its draft label.**
Promotion runs a check that resolves data bindings, declares scope, identifies missing evidence, and
applies the destination profile. This protects the fast analyst path without making controlled
reporting permissive.

`ReportingRunReadinessDto` (`CanGenerateDraft`/`CanGenerateFinal`, `Checks`, `BlockingReasons`) is
the natural evaluation seam for the promotion check.

## 6. Replace the Generic Context Bar with a Reporting Scope Contract

A date, a portfolio, and a currency do not establish what a financial report means. Every edition
carries a **scope contract**: a compact definition of its population, measurement conventions, and
reference state.

**Visible in the header:**

```
August 2026 · Meridian Life · General Account · Statutory · USD
Revision 08 · Pinned to close snapshot 04
```

**Available in an expandable context panel:**

| Context dimension | Examples |
| --- | --- |
| Population | Included entities, accounts, portfolios, exclusions |
| Measurement | Carrying value versus market value; clean versus dirty price |
| Accounting | Book, basis, period, ledger version |
| Performance | Gross or net; return methodology; benchmark version |
| Time | Reporting period, valuation date, accepted-data cutoff |
| Conversion | Reporting currency, FX source, conversion convention |
| Presentation | Units, precision, rounding, sign convention |
| Provenance | Dataset versions, calculation versions, manual adjustments |

The header stays compact. The complete definition stays inspectable.

### Separate two kinds of time

- **Effective time** — the date or period the information describes.
- **Recorded time** — when Meridian received or accepted that information.

A journal received on September 3 may affect August reporting. "As of August 31" alone does not say
whether the edition includes it. Both axes belong in the scope contract, and the accepted-data cutoff
is what makes a pinned edition reproducible.

### Separate report context from browsing context

Changing the application's global portfolio selector must **not** silently alter an open report
edition. Offer instead:

> Open the corresponding edition · Create a scoped variant

Similarly, a prior-period comparison must state whether it uses the **originally published** edition
or a **later restated** edition.

### Interpret freshness relative to the report

A pinned August-close dataset does not become "stale" because September data exists. Use:

> Current for August close. A later revision is available.

This is the reporting expression of the existing requirement that interface states and domain data
states stay distinguishable. The broader provenance model can follow the W3C PROV distinction
between entities, the activities that produce them, and the agents responsible for those activities.

### Gap summary

Consolidate `ReportingOperationalScope`, `ReportingAccessScope`,
`ReportingCertifiedSnapshotScope`, `ReportingAuthoritativeSourceCheckpoint`, and
`ReportingRunParametersDto` into one canonical scope contract. `ReportingCanonicalParameterSerializer`
already establishes canonical serialization, which the scope contract needs for stable hashing.

## 7. Reuse Governed Workpapers, Not Just Visual Blocks

The earlier concept emphasized reusable report *modules*. The more important distinction:

> Reuse the analytical result separately from its presentation.

An Investment Income workpaper can support a report table, a summary sentence, a management slide,
and an Excel schedule. Those must not be four separately maintained calculations.

**Proposed relationship:**

```
source systems  ->  governed workpaper  ->  report presentations
```

A workpaper contains the scoped result, calculation references, supporting schedules, reconciliation
results, and a responsible owner. **It consumes Meridian's accounting and analytics services. It
does not become a second ledger or an independent calculation engine.** Existing services such as
`NavAttributionService` and `PartnersCapitalProjection` are consumed by workpapers, not duplicated
inside them.

Linked data has useful precedent in tools where a source value feeds other files and subsequent
changes are explicitly published to their destinations. **Meridian refinement:** bind reusable
values to financial meaning, scope, controls, and publication state, not only to source-cell
locations.

### Simplify the authoring block library

Begin with five recognizable authoring families instead of dozens of domain components:

| Family | Purpose |
| --- | --- |
| **Finding** | A supported conclusion with the evidence needed to interpret it |
| **Figure** | A chart or comparison plate |
| **Schedule** | A structured table, rollforward, or reconciliation |
| **Commentary** | Authored explanation with linked measures |
| **Disclosure** | Methodology, limitation, exception, or required note |

Yield curves, attribution, income rollforwards, and basis comparisons become specialized
configurations of these families. `ReportingTemplateFamily` today enumerates ten document-level
families (`InvestorStatement` through `CustomReport`); those are *report* kinds and remain valid.
The five families above are *block* kinds and are a new, orthogonal axis.

Every block answers three questions:

> What question does this address? What data supports it? What qualifications must travel with it?

That is a more useful starting point than "add a chart".

## 8. Make Change Review the Signature Interaction
> **Largely implemented.** `lib/report-change-since-review.ts` now models change against the
> reviewed revision, and the freeze axis in `lib/reporting-lifecycle.ts` expresses the
> proposed-change-set idea more cleanly than this section does. See Section 2a.


The strongest differentiator is not an elaborate report builder. It is a clear answer to:

> What changed, why did it change, and what must now be reconsidered?

Change review becomes a central surface of every edition.

### Classify changes by meaning

| Change type | Example | Review implication |
| --- | --- | --- |
| **Value** | Investment income increased by $42,118 | Review affected figures and commentary |
| **Population** | An account entered or left the report | Reassess totals and comparability |
| **Methodology** | Return calculation changed | Review even when displayed values barely move |
| **Source** | A manual input was replaced by a certified dataset | Review evidence and source suitability |
| **Narrative** | The stated performance driver changed | Review the interpretation |
| **Presentation** | A chart scale, unit, or footnote changed | Determine whether meaning changed |

**"Formatting only" must not automatically mean "no review".** Changing units or removing a
qualification can materially change interpretation without changing any stored value.

Current state: `ReportSnapshotDiffEngine` produces a `ReportWriterGridDiffDto`. It diffs grid cells.
It does not classify change *meaning*, and it has no concept of methodology or population change.
This taxonomy is the extension it needs.

### Show the cause before the consequences

For a late accounting adjustment, the inspector shows:

```
Late income accrual · JE-0842

Net investment income: 18,329,324 -> 18,371,442 USD
Change: +42,118 USD

Affected content:
  Investment Income schedule · Executive Summary · Committee Packet

Required action:
  Review the revised schedule and its linked commentary.
```

One change event, several downstream effects, presented in that order.

### Apply coherent change sets

For controlled editions, a source update first creates a **proposed change set**. The preparer
inspects the impact before accepting it. Related changes needed to preserve reconciliation are
applied **together**, never selectively accepted because some make the report look better.
Acceptance creates a new revision.

### Preserve historical approvals

This corrects the most important weakness in the earlier concept.

> An approval must not be retroactively erased because new data exists.

Approval of revision 07 remains a historical fact about revision 07. It simply does not approve
revision 08. Review credit for unchanged sections may carry forward **only** through an explicit
policy that checks their content and dependencies. Where impact cannot be established reliably,
require the broader review.

## 9. Separate Lifecycle, Data Condition, Controls, and Delivery
> **Implemented, and extended.** `lib/reporting-lifecycle.ts` now separates four axes, adding a
> freeze axis this section does not propose. The remaining gap is that the C# contracts still
> carry one overloaded `ReportingRunStatus`. See Section 2a.


One overloaded status cannot describe a reporting edition. Today `ReportingRunStatus` mixes
lifecycle and outcome in a single enum, with `Failed` sitting beside `Draft`/`InReview`/`Approved`/
`Released`.

Use four independent dimensions internally:

| Dimension | Example states |
| --- | --- |
| **Lifecycle** | Preparing, In review, Approved, Published |
| **Data condition** | Current for scope, Update available, Incomplete |
| **Control outcome** | Passed, Failed, Accepted exception, Not evaluated |
| **Delivery** | Not started, Processing, Delivered, Partially failed |

`ReportingDeliveryState` already models the delivery dimension separately, which is the precedent
for the other three. `ReportingRunReadinessDto` is the nearest existing carrier for control outcome.

**The interface must not show four equally prominent badges everywhere.** It derives a plain-language
task summary:

> Review required — one accepted data update affects two sections.

or:

> Approved — publication is blocked by an incomplete recipient check.

### Retire the overall health percentage

A report that is "98% healthy" can still contain one decisive failure. Prefer:

```
Not ready to publish
One required tie-out failed. All required sections are complete.
```

Percentages stay useful for genuinely bounded quantities, such as "8 of 10 sections reviewed". They
must not become a proxy for correctness.

This extends the existing requirement that missing, zero, no-activity, and not-applicable conditions
remain distinguishable from each other.

## 10. Strengthen Controls Without Creating a Parallel Accounting System
> **Partly implemented.** `Waived` and `WithinTolerance` are already distinct from `Passed`, and
> `buildSourceCoverage` already reports coverage with a breakdown. The comparison contract and
> reconciliation bridge below remain proposals. See Section 2a.


Reporting exposes and evaluates evidence. It must not offer a shortcut around the systems that
establish that evidence.

### A tie-out needs a comparison contract

Before comparing two numbers, establish that they represent compatible populations, periods,
measurement bases, currencies, and inclusion rules. Otherwise "the numbers match" can be misleading,
and a difference can be entirely legitimate.

A refined Tie-Out Panel shows:

- Report balance
- Reference balance
- Documented reconciling items
- **Unexplained residual**
- Control rule and result

The **residual**, not the gross difference, is the primary investigation target. Expected differences
are supported by a reconciliation bridge, not dismissed in a free-text note.

### Evaluate controls on precise values

Two rounded figures can appear equal while their underlying amounts differ. Evaluate the control on
the approved precision and show the exact difference in the details.

A percentage difference requires a meaningful denominator. Where the denominator is missing, near
zero, or inappropriate for the population, show **Unable to evaluate**, never a reassuring
percentage.

### Make coverage measurable

"Source coverage 98%" is incomplete without stating what is counted. Useful alternatives:

- 98% of positions have an accepted price.
- 96% of absolute market-value exposure has an accepted price.

The definition travels with the metric. Unknown or unvalued exposure stays visible rather than
quietly leaving the denominator.

### Keep exception acceptance distinct from passing

A failed control with an authorized disposition remains:

> Exception accepted for this edition

It never becomes:

> Passed

The disposition carries a reason, owner, scope, authority, and an expiry or review condition.

### Remove "exclude to resolve" as a generic action

Excluding an item is a **governed scope change** with its own impact review, not an ordinary way to
clear a reporting exception. Likewise, a posting issue links to the appropriate Accounting workflow;
Reporting does not offer an unqualified "post adjustment" button inside a report.

## 11. Design Review as a Focused Evidence Task

The reviewer must not land in a general-purpose editing environment. Use three modes within an
edition:

`Prepare · Review · Release`

Report identity, scope, outline, and current selection stay stable across modes. Only the controls
change.

### Review layout

```
Investment Report · August 2026 · Revision 08
Compared with reviewed revision 07

Outline / changes       Report section                 Inspector
-----------------------------------------------------------------
Executive summary       Rendered section               What changed
Investment income       Selected value highlighted     Why it changed
Risk                                                   Control results
Appendix                                               Comments

Next required review: Investment income
```

**Only one inspector is open at a time.** Evidence, comments, and change details are views within
it, not separate drawers piled over one another. This follows the existing evidence-drawer pattern:
reveal sources, timestamps, notes, history, and downstream effects without replacing the main
workspace. A version-specific, read-only review also has established product precedent in tools that
let reviewers comment on a particular document version without editing the source.

### Review a finding, not merely a page

For each selected item, make three questions prominent:

1. **What is being asserted?** The value or statement under review.
2. **What supports it?** The relevant schedule, calculation, source, and qualification.
3. **What decision is required?** Accept, request a change, or escalate within the reviewer's
   authority.

Comments attach to **stable** section, block, measure, or record identifiers. A comment about an
issuer must not migrate to a different issuer when a table is sorted.

### Be more careful with AI support labels

Replace a broad "Supported" badge with specific checks:

- Referenced values match revision 08.
- Required evidence links are present.
- Causal interpretation requires author review.

A sentence can cite accurate values and still offer an unsupported explanation of *why* they changed.

**AI drafts or suggests revisions. It does not certify claims, waive exceptions, or publish.** The
same evidence requirements apply to human-written and generated commentary. This is the reporting
expression of the existing governed-autonomy boundary.

## 12. Treat Publication as a Release Process, Not an Export Button

The earlier concept correctly emphasized immutable publication, but the release boundary needs to be
more precise.

### Build an explicit release candidate

A release candidate **binds**:

```
edition revision + data versions + calculation versions + template version
  + control results + audience scope + required output files
```

The files are rendered and checked **before** final publication approval. The release action then
publishes **the approved candidate**, never a fresh regeneration against whatever data happens to be
current at the moment someone clicks.

`ReportingReleaseConsistencyGate` and `CertifiedReportingSnapshotBuilder` are the existing seams to
build this on.

### Separate semantic fidelity from layout fidelity

The same underlying result legitimately appears differently in PDF, web, Excel, and slides. It must
retain the same scope, values, qualifications, and interpretation. Paginated-report practice
distinguishes screen-oriented from physical-page renderers and warns that export formats change
pagination and layout behaviour.

| Output | Required treatment |
| --- | --- |
| **PDF** | Stable pagination, repeated table headers, readable figures, source notes, accessibility checks |
| **Web** | Clear published context; optional inspection without silently switching to current data |
| **Excel** | Structured schedules, precise values, units, metadata, intentional formulas where appropriate |
| **Slides** | Approved selection and composition, with essential qualifications retained |

**"One source" means shared meaning, not identical layout.**

### Add a final release review

Before confirming publication, show the exact edition, audience, required outputs, accepted
exceptions, and approval scope. This follows error-prevention guidance for consequential
submissions: provide a way to check, correct, confirm, or reverse the action.

### Keep publication and delivery separate

A released edition can exist while one delivery channel fails:

```
Published · Distribution incomplete
Internal archive completed. Two recipient deliveries require attention.
```

Retries reuse the **same** publication and prevent duplicate delivery where the channel supports it.
Clicking Publish twice must not create two publications. `ReportingDeliveryState` and
`ReportingDeliveryReceiptKind` already separate delivery from release; the idempotency requirement is
what needs stating.

### Make audience variants explicit

An internal report and an externally redacted report are distinct output variants with declared
policies. **Apply access restrictions before generating output.** Do not rely on hiding a worksheet,
collapsing a section, or removing a visible link.

Meridian must also avoid promising that ordinary downloaded files remain subject to application
permissions. Once a file leaves, its distribution requires a separate organizational control model,
and the interface should say so rather than imply otherwise.

### Preserve corrections without rewriting history

A correction creates a **successor publication** with a stated reason and affected scope. Whether it
is labeled a correction, reissue, or restatement follows organizational policy, **not** an automatic
choice based only on the size of a numerical change.

Retention is likewise policy-driven. A universal seven-year default must not be embedded as an
assumed requirement.

## 13. Make the Interface Unmistakably Meridian

> **Reconciliation required.** As Section 1 notes, the five-primitive vocabulary below is not
> currently checked into this repository. Treat this section as the proposed reporting mapping,
> contingent on restating those primitives in `Meridian Design System/PATTERNS.md` first.

The reporting workspace preserves the five primitives rather than introducing a sixth. **Gate is
better treated as a workflow pattern** composed from existing state and control components.

| Primitive | Reporting expression |
| --- | --- |
| **Datum** | Reporting cutoff, selected revision, comparison baseline |
| **Field** | Explicit populations: sections reviewed, reports due, source coverage |
| **Trace** | Source-to-publication history and change propagation |
| **Plate** | Comparable editions, entities, bases, and scenarios |
| **Capsule** | A genuinely reusable, configurable reporting unit |

This follows the stated definitions: Plates preserve comparison geometry, while Capsules require a
function, a data contract, predictable controls, and saved state.

### Per-mode composition

- **Workbench** — one continuous register, a compact cycle summary, and a contextual inspector.
  Minimal branding.
- **Prepare** — a quiet outline, a dominant report canvas, one properties/evidence inspector.
  Constrained layouts over arbitrary positioning.
- **Review** — more reading space, larger controls. Emphasize changed content and required decisions
  over authoring tools. Review is already a distinct density mode for approvals and reports.
- **Publication** — remove application chrome. Stronger narrative headings, annotations, source
  notes, explicit dates, restrained editorial branding.

### Responsive behaviour

On narrower screens, preserve the selected report section and switch between **Report** and
**Evidence** views. Do not compress an outline, a page canvas, and an inspector into three unreadable
columns. A narrow-width review supports one understandable decision at a time; complex authoring
stays desktop-oriented.

**This is responsive browser behaviour only.** It is not a mobile lane, and it introduces no mobile
application, no mobile-specific product surface, and no mobile-first workflow. That boundary is
normative in `CLAUDE.md` and the design charter.

Across all modes: graphite primary actions, one dominant action per region.

## 14. Test the Model with One Complete Scenario

Before adding more features, validate the whole concept against a single scenario.

### August Investment Report: late income adjustment

| Step | What happens |
| --- | --- |
| **Starting state** | Revision 07 has been reviewed. Investment income is 18,329,324 USD. |
| **Accounting change** | A late accrual of 42,118 USD becomes available in the approved accounting source. |
| **Detection** | Meridian identifies the affected income schedule, executive-summary value, and committee-packet section. **Revision 07 remains unchanged.** |
| **Preparation** | The preparer opens one proposed change set. The candidate income value is 18,371,442 USD. Related controls rerun on the candidate dataset. |
| **Review** | Accepting the change creates revision 08. Affected reviews reopen; unaffected review credit is retained only where policy permits. The reviewer sees the numerical difference and the exact supporting journal. |
| **Release** | The required PDF and Excel outputs are rendered and checked. The authorized approver approves that exact candidate. |
| **Publication** | Released files, source references, approvals, and control results are preserved together. |
| **Subsequent change** | Another adjustment creates a new proposal. It cannot silently alter the publication. |

This one scenario exercises context, source linkage, change propagation, accounting evidence, review
scope, rendering, and immutability. It is a better first proof than eight unrelated polished screens.

## 15. Narrow the Implementation Scope

Organize delivery around a reliable reporting loop, not a reporting feature catalog.

| Stage | Build | Evidence of success |
| --- | --- | --- |
| **1. Controlled edition** | Scope contract, revisions, pinned datasets, review, publication record | A reviewer can identify exactly what was approved and retrieve the unchanged output |
| **2. Reusable reporting** | Governed workpapers, linked commentary, change sets, schedules, packets | A source change produces a coherent, inspectable set of reporting effects |
| **3. Advanced governance** | Cross-report controls, audience variants, richer policy configuration, AI drafting | Added automation preserves scope, evidence, and release controls |

**Stage 0 (enabling, unglamorous).** Decompose `ReportingOutputManifest`, settle the `Revision`
naming decision from Section 3, and consolidate the scope fragments from Section 6. Stage 1 is not
safely buildable on a 32-parameter manifest with a contested vocabulary.

### Deliberately deferred for the first implementation

- Unconstrained page design
- Autonomous commentary
- Sophisticated process analytics
- Automatic decision-to-trade workflows

### Acceptance tests that matter

A small set of demanding tests reveals more than another component showcase.

| # | Test | Proves |
| --- | --- | --- |
| 1 | Change a source value after review; verify the reviewed revision does not move | Revision immutability (Section 8) |
| 2 | Change a methodology without changing the rounded number; verify review is still requested | Change classification (Section 8) |
| 3 | Reorder a schedule; verify comments stay attached to the correct records | Stable identifiers (Section 11) |
| 4 | Accept an exception; verify it does not become a passed control | Control-outcome separation (Section 10) |
| 5 | Fail one delivery channel; verify the publication is not regenerated or duplicated | Release/delivery separation and idempotency (Section 12) |
| 6 | Open an old publication after template and data changes; verify its original files are unchanged | Publication immutability (Section 3) |

Each maps to a named section, so a failure identifies which part of the model is not yet real.

### Measurement

Measure preparation effort, review effort per consequential change, unresolved manual
reconciliations, publication failures, and time required to explain a reported value.

**Establish baselines before assigning improvement targets.** Publishing a target without a baseline
is a truth-discipline violation.

## 16. What This Document Does Not Do

- It does not change any behaviour. No source file is modified by this document.
- It does not set roadmap status. Rows stay owned by the roadmap registry.
- It does not authorize the `Revision` rename in Section 3, which needs explicit sign-off because it
  touches a durability seam.
- It does not adopt Section 13, which is blocked on reconciling the five-primitive vocabulary with
  the in-repo design system.
- It does not override `docs/architecture/reporting-workstation-model.md`. Where the two disagree,
  Section 2a records the disagreement and leaves the choice to both documents' owners.

### Open decisions requiring sign-off

| Decision | Section | Blocking |
| --- | --- | --- |
| `Revision` naming: rename the storage concurrency token, or accept a two-meaning vocabulary | 3 | Stage 0 |
| Whether the design-styles source is checked into `Meridian Design System/` or its primitives restated in `PATTERNS.md` | 1, 13 | Section 13 adoption |
| Whether `ReportingRunStatus` is decomposed in place or superseded by four dimensions on `Edition` | 9 | Stage 1 |
| Review-credit carry-forward policy: which unchanged-section conditions permit retaining review credit | 8 | Stage 1 |
| **Navigation model**: adopt this document's four destinations, the landed seven-lane pipeline, or a reconciliation of both | 4, 2a | Any navigation work |
| Whether the four axes now in `lib/reporting-lifecycle.ts` are promoted into the shared contract seam, and on what schedule | 2a, 9 | Browser/WPF parity |
