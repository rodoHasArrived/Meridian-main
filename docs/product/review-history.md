# Product Review and Decision History

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

Use this index for dated reviews, brainstorms, design inputs, and acceptance records. Return to
[Product](README.md) for product direction and current status sources. Findings describe the source
and evidence available to each author; they do not establish current delivery or release readiness.

## Planning and Acceptance Records

- [Product Plans](plans/README.md#next-work-determinations) owns the latest prioritization pointer
  and retained next-work determinations, including [2026-10-02](plans/next-work-determination-2026-10-02.md)
  and [2026-09-27](plans/next-work-determination-2026-09-27.md).
- Earlier determinations: [2026-09-23](../../archive/docs/plans/next-work-determination-2026-09-23.md)
  and [2026-09-20](../../archive/docs/plans/next-work-determination-2026-09-20.md).
- [2026-08-29 W9 Operator Acceptance Record](w9-operator-acceptance-2026-08-29.md) records the
  acceptance and reassessment chronology. Follow its evidence and the roadmap registry for each
  decision's bounded scope.
- [W9-DEMO-002 Closure Record (2026-09-16)](w9-demo-002-closure-2026-09-16.md) preserves the
  lane-owned evidence behind `DEC-W9-DONE-001`.
- [First-Order Improvement Slate](plans/product-roadmap-priorities-2026-07.md),
  [W9 Close-Out Delivery Plan](plans/w9-close-out-delivery-plan-2026-08.md), and
  [Depth Slate](plans/w10-depth-slate-2026-07.md) preserve delivery rationale. These plans do not
  replace registry status or release evidence.

## Dated Review and Design Annotations

The annotations below are preserved from the product landing page. Terms such as "landed," "open,"
and "remains" describe those dated reviews and their named addenda. Follow the original document
for commit anchors, qualifications, and later addenda; recheck current source before turning a
finding into implementation work.

- [Reporting Operating Model (2026-09)](reporting-operating-model-2026-09.md) — refined
  reporting semantics: the `Report`/`Edition`/`Publication` object split, the four-destination
  consolidation inside the existing Reporting root, the scope contract with separated effective
  and recorded time, change classification by meaning, four independent status dimensions, and
  the release-candidate publication boundary. Grounded in current `src/Meridian.Reporting`
  source evidence with a named gap list, six open decisions, and six acceptance tests
  (`W4-RPT-001`, `W9-REPORT-005`); changes no behaviour and sets no roadmap status. Its
  Section 2a reconciles the document against the landed
  [Reporting Workstation Model](../architecture/reporting-workstation-model.md): the four
  lifecycle axes, change-since-review and per-class gate policies now exist in the browser
  workstation's TypeScript modules, so the remaining gap is promoting them into the shared
  contract seam, and the two documents' competing navigation models are left as an open decision
- [Adversarial Program Review (2026-08-25)](adversarial-program-review-2026-08-25.md) —
  independent whole-program adverse review; re-tests the 2026-08-24 open items against the 39
  commits landed since, then extends into cross-catalog consistency between the authorization
  model, the shared API surface, and the client surface. Findings are anchored at `e232ece1`;
  the document's addendum records that PR #2824 has since wired the posted-journal trial balance
  and P&L into `AccountingPostedLedgerSection`, so **that half is no longer current state**. A
  third addendum records `main` at `3eb6961a` (PR #2828) and rechecks five open items that remain
  unchanged; the second addendum's citations stay in the `bb43e0e6` frame its heading declares.
  That second addendum records `main` at `bb43e0e6`: the `ViewLedgerReports`/`ManageLedgerReports` split
  landed and the posted-ledger panel no longer depends on `ManageDirectLending`, and the run-scoped
  explorer was retired from Accounting to `/strategy/run-ledger`. What remains open is
  assigned-fund scoping (the new tenant filter is cross-tenant and fail-open, and the client sends
  no fund id), one run-scoped binding under `/accounting/accounts/detail`, the missing
  `ViewCompliance` read grant (which must also be subtracted from the `Developer` role, defined as
  `Admin` minus a list), the compliance read surfaces that need server work beyond that grant —
  `controls/attestation` evaluates none of the four controls it names, neither read route is
  tenant-scoped, and the chain records policy evaluations rather than actions (the evaluate route
  never dispatches, and the caller supplies the object identity and both state snapshots) — the option contract multiplier that reaches the two aggregate
  exposure projections but none of the paper transaction, valuation, persistence or margin paths
  — nor the Trading screen's own exposure and P&L arithmetic, which the book-side fix does not
  reach — and an estimated 29%
  of route constants (250 of 862) referenced by no client layer — a reference-based measure the
  review itself qualifies as an estimate with error in both directions, not a settled count. The
  denominator is itself imperfect: at least two catalog constants are registered by no server
  route and reach the browser through the generated mirror, and the review states plainly that
  the scale of that class is unmeasured. A fourth addendum records `main` at `7d675f40`
  (PR #2831) and rechecks no claims at all: it measures which of the 79 files the review cites are
  byte-identical between the anchor and that tree — **59 are**, including all three carrying the
  headline finding — so those citations resolve in both frames unchanged, and the same four counts
  held one merge earlier at `8c0c6e36`. `main` has advanced nine times since the anchor and all
  nine are merged in here, but **only three of the nine have a claim-level addendum**; the fourth
  states that limit rather than covering it over.
  **Completeness caveat:** the 41-round adversarial loop
  behind this document stopped when the reviewer hit a usage limit, not on convergence — the last
  three rounds produced two new codebase defects and two self-inflicted contradictions — so every
  claim in it has been checked but the set of claims is not exhausted
- [Adversarial Program Review (2026-08-24)](adversarial-program-review-2026-08-24.md) —
  independent whole-program adverse review; re-tests every headline finding of the 2026-08-18
  pass against the 218 commits landed since, verifies the three built-but-dead flagship fixes
  and the authorization burn-down as genuinely wired, documents the instance-vs-class
  remediation pattern with `file:line` evidence, and re-ranks improvement areas by end-user
  value uplift
- [Adversarial Program Review (2026-08-18)](adversarial-program-review-2026-08-18.md) — follow-up
  independent whole-program adverse review that re-tests all 25 headline findings of the
  2026-08-10 pass against the ~321 commits landed since, documents the built-but-unwired
  remediation pattern with `file:line` evidence, and re-ranks improvement areas by end-user
  value uplift
- [Adversarial Program Review (2026-08)](adversarial-program-review-2026-08.md) — independent
  whole-program adverse review of end-user functionality with `file:line` evidence; re-tests the
  2026-07 review's headline, documents acceptance-vs-wired drift, and ranks improvement areas by
  end-user value uplift
- [Adversarial Review 2026-08 Remediation Plan](plans/adversarial-review-2026-08-remediation-plan.md) —
  every finding from that review as a tracked todo with a code-ready implementation plan
  (evidence, change, verification, effort, dependencies) across thirteen sequenced workstreams;
  a working plan, not a status source
- [Production-Readiness Backlog (2026-08)](plans/production-readiness-backlog-2026-08.md) — the
  ten-item production-readiness ordering of that estate, re-verified against source on
  2026-08-18 with corrections for stale claims and one new blocker finding (fixed-income
  booking); records the three built-but-dead fixes landed on the same branch
- [Adversarial Program Review (2026-07)](adversarial-program-review-2026-07.md) — prior
  independent review pass that motivated the activation-over-expansion and truth-discipline
  doctrines; see its [2026-07-26 follow-up](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/assessments/adversarial-program-review-2026-07-26.md) (removed from the tree by the 2026-09-11 archive cleanup; the link is its last version)
- [High-Value Code Brainstorm (2026-07)](high-value-code-brainstorm-2026-07.md) — market-researched
  prioritization snapshot; use the roadmap registry, not this dated sequencing, for live status
- [Data Provider & Accounting Code Brainstorm (2026-07)](data-provider-accounting-brainstorm-2026-07.md) —
  code-grounded improvement lanes for the provider and accounting subsystems with a dated status
  table tracking which lanes have since shipped
- [Portfolio Cash Ladder Blueprint (2026-07)](../engineering/blueprints/portfolio-cash-ladder-blueprint-2026-07.md) —
  code-ready design for the wave-8 portfolio cash-flow forecasting and liquidity ladder engine,
  aggregating per-security projection runs into scenario-aware, per-currency cash ladders; the
  first vertical slice has landed and the persisted-run phases remain open
- [Quote-stream Fan-out Blueprint (2026-07)](../../archive/docs/plans/web-ui-stream-fan-out-blueprint-2026-07.md) —
  delivered design for event-driven SSE fan-out, per-session stream caps, and companion-pane
  stream sharing (PRs A–C shipped); archived once delivered
- [Report-run Status Stream Blueprint (2026-07)](../engineering/blueprints/web-ui-report-run-stream-blueprint-2026-07.md) —
  delivered design for the `report-run:<id>` stream and the generic `StreamBroadcaster<TPayload>`;
  supersedes the fan-out blueprint's `workspace` / `inbox` topic proposal
- Every plan and blueprint is catalogued in the single
  [Plans and Blueprints Register](../engineering/blueprints/README.md), which names each plan's
  home folder and also records the shared migration-ordinal, precision, route-prefix, and
  cross-blueprint contracts
- [Browser Workstation UI Improvements Brainstorm (2026-07)](web-ui-improvements-brainstorm-2026-07.md) —
  nine grounded browser-workstation UX ideas with effort/impact triage, platform-bet analysis,
  and sequencing
- [Browser Workstation UI Improvements Implementation Plan (2026-07)](../engineering/plans/web-ui-improvements-implementation-plan-2026-07.md) —
  phased, code-ready implementation plan for the nine brainstorm ideas with per-phase file
  anchors, contracts, test plans, and validation commands
- [Excel Onboarding Workbook & Provider Connection UX Brainstorm (2026-07)](excel-onboarding-workbook-brainstorm-2026-07.md) —
  five grounded ideas for populating security-master, entity, ledger, and account data via a
  prepared Excel workbook (download → fill → upload → review → governed commit) and tying
  provider connection setup to the imported instrument universe
- [Functionality Deepening Brainstorm (2026-07)](functionality-deepening-brainstorm-2026-07.md) —
  eleven code-grounded ideas for deepening existing subsystems (risk, alerting, promotion
  governance, reconciliation matching, approvals, close evidence, backfill, marks, order
  lifecycle), anchored to a depth survey of declared-but-dead seams and sequenced around the
  W9 slate

## Archived Product Material

The 2026-09-11 archive cleanup removed the following material from the working tree. These links
are pinned to its last retained commit and are historical sources only:

- [Design charter v0.25](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/design/meridian-design-document-v0.25.md)
- [Current Direction and Status](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/plans/current-direction-and-status.md)
- [Evidence-Backed Investment Operations Plan](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/plans/evidence-backed-investment-operations-plan.md)
- [Feature Inventory](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/status/FEATURE_INVENTORY.md)
- [Target End Product](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/status/TARGET_END_PRODUCT.md)
- [Pre-cleanup plans index](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/plans/README.md)
- [Pre-cleanup archive index](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/README.md)

For material archived after that cleanup, use the current [Archived Plans](../../archive/docs/plans/README.md).
The [Status index](../status/README.md) explains the remaining automation-owned and compatibility
paths. Current product status belongs in the [Roadmap Registry](../roadmap/README.md).
