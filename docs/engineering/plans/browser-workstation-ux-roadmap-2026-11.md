# Browser workstation UX implementation

**Status:** active
**Placement:** supporting
**Owner:** Workstation Shell and UX
**Reviewed:** 2026-11-21
**Baseline:** `86a1689f20`; branch `codex/workstation-ux-roadmap`

This record implements the user-approved end-to-end UX roadmap. Initial implementation used
the baseline above. Publication integrates `main` at `83235ca09b`, retaining its newer shell,
record search, financial workflows, scoped passports and reviewed bulk-command protocol.
Roadmap registry statuses remain authoritative; this document records this change’s work and evidence.

## Delivery and acceptance

| Phase | Capability | Acceptance |
| --- | --- | --- |
| 0 | Cockpit Resolve/Dismiss, explicit report subject, journal confirmation, import links | Visible decision form; no arbitrary report fallback; cancellation sends no command |
| 1 | Shared guarded case inspector, conflict review, receipts, bulk version binding | Consistent case data/actions; stale writes rejected; partial results visible |
| 2 | Exact FinancialValue and one amount passport interaction | Missing differs from zero; currency precision and source conditions explicit |
| 3 | Shell Today, Accounting Close hub, Settings diagnostics | Seven root workspaces; governed continuity preserved; compatible scoped URLs |
| 4 | Instrument dossier: Reference, Positions, Ledger, Cases | Stable instrument identity; URL tabs; preserve asset capabilities |
| 5 | Retained report review comparison and approval evidence | Comparable prior-approved baseline; exact diff; authoritative assessment or NotAssessed |
| 6 | Retained import preview, row dispositions and outcomes | Stable source rows; audited exclusions; stale preview refused; complete outcomes |

## Parallel implementation manifest

Mode: **Deep Review** (shared financial contracts and multiple UI journeys). Coordinator and final
validation owner: root agent. First proof: focused journal lifecycle controls Vitest suite.

| Lane | Owned areas | Initial proof |
| --- | --- | --- |
| Root | Journal confirmation/extraction, import integration, documentation, final QA | Journal controls and journal view-model tests |
| Casework | Reconciliation components, case contracts/endpoints/repository | Case inspector and stale-command tests |
| Values | FinancialValue, Holdings, trial balance, additive exact fields | Exact decimal/null/currency tests |
| Close | Shell routing/navigation, Today/Close/diagnostics, continuity URL state | Route, redirect, shell and continuity tests |
| Dossier | Instrument record route component and identity-filtered reads | Dossier/client filter tests |
| Reporting | Preview subject, comparison evidence, governance binding | Preview/comparison/governance tests |

Shared-file changes are coordinated: Close owns route registration; root edits journal sections
and subsequently extracts helpers; Casework owns cockpit callbacks and reconciliation state.
Integrate primitives and contracts before consumers, then routing, then screenshot fixtures.
A failed slice is repaired or reverted as a coherent unit; do not bypass financial checks.

Required context: root AGENTS, solo-developer workflow, dashboard/shared source READMEs,
accounting and operational-evidence context, approved roadmap. Agent handoffs report touched
files, exact validation, unresolved risks, and released file claims. Evidence is invalidated by
subsequent changes to the tested behavior.

## Initial implementation verification

Validation uses deterministic synthetic fixtures; there are no live-provider or production claims.

| Check | Evidence |
| --- | --- |
| Browser production build | TypeScript no-emit check and Vite build to an external output directory |
| Frontend workflows | Focused casework, journal, financial-value/proof, trial-balance, dossier, palette, portfolio, reporting, Close/Today and import suites; keyboard and axe assertions; final journal guard slice **38/38 passed** |
| Backend financial contracts | **440/440 passed**: reconciliation repository, statement import/review/workflow, retained amount provenance, report review/materiality, exact read contracts, fractional/derivative Holdings and statement authority/handoff tests |
| Screenshot contracts | 32 Node and 45 Python tests; catalog has 90 captures with zero route gaps; 15 changed-state captures plus dossier recapture |
| Actual browser | Chromium at 1280×720, 1440×900 and journal confirmation at 1920×1080; real rendered interactions, focus restoration, conflict, URL tabs, imports and overflow checks |
| Documentation | Source README validation clean; documentation structure passed with existing lifecycle warnings; API route mirror current; whitespace checks clean |

The full local `scripts/ci.sh` and hosted CI were not run. Focused builds/tests provide the scoped
local evidence; required hosted checks remain the merge authority. Windows Chrome/Edge scaling,
NVDA/Narrator and a live backend browser session were not exercised here. Fixture-only mode has
known missing non-case API fixtures; explicit synthetic response interception was used for positive
journal/report/import/dossier states and conflict workflows, not as evidence of live execution.

The backend verification builds `tests/Meridian.Tests/Meridian.Tests.csproj` with
`/p:EnableWindowsTargeting=true /m:1` and selects `ReconciliationBreakQueueRepositoryTests`,
`StatementImportServiceTests`, `StatementReconciliationReportWorkflowServiceTests`,
`StatementReconciliationCaseworkHandoffTests`, `ReportingRunReviewTests`,
`FinancialValueReadContractTests`, `TrialBalanceAmountProvenanceTests`,
`PostedLedgerAmountProvenanceTests`, `ReportLedgerAmountProvenanceTests`, and the changed
statement-authority/Holdings endpoint methods. The final assembled test run passed all 440 cases.
No tests were skipped. Reporting policies have 31 cases, including exact threshold parsing and
boundaries. The compiler ran against the final sources before the assembled run.

Browser verification uses the dashboard’s `tsc --noEmit` check (with its incremental cache outside
the repository) and `vite build --outDir <external scratch directory>`. Targeted Vitest and ESLint
cover changed workflows. Existing hook warnings, CSS import-order/chunk-size warnings and repository
documentation lifecycle warnings remain; no lint errors or new whitespace errors remain.

Journal conflict browser checks at 1280×720 and 1440×900 submitted exactly one command, reread
once after synthetic HTTP 409, displayed before/after fields, preserved notes through an explicit
Review latest choice, and required a fresh confirmation without automatic retry. Both checks had
no page errors or document horizontal overflow. Late scope/draft/session responses and
same-version local edits have focused regression coverage.

## Current-main integration verification

Integration preserves current-main record scope, attention/triage, queue keyboard navigation,
accounting domain panels, durable bulk preview identities and native scoped amount passports.
Obsolete parallel panel extractions were removed. The accounting entry component has 1,990 lines.

Fresh checks after dependency installation and contract generation include the production Vite
bundle, zero new workstation-governance violations, matching design-system assets, 216 shell/Close
tests, 212 financial/journal tests, five passport navigation tests, 98 reporting tests, 151 dossier
and Portfolio tests, and 44 import tests. These are focused suite counts, not the full repository
test suite. Targeted lint passed for each frontend lane. Source README validation passed; document
structure validation passed with 615 existing lifecycle warnings.

Actual Chromium checks at 1280×720 and 1440×900 exercised Post, Reverse and Lock confirmation
and cancellation, with no request before confirmation and restored focus. Journal 409 checks
retained notes through explicit review, with one submission, one reread and no automatic retry;
all eight flows had no page errors or document horizontal overflow. These use deterministic
synthetic responses and do not establish live-backend or Windows accessibility behavior.

The six-route axe smoke passed against its unchanged baseline, with no new serious or critical
findings. Ready inspector focus checks passed for 18 expanded controls at 1440×1100, 1440×900
and 1280×720. Six additional rendered task walkthroughs passed; nine handled fixture-only 404s
are recorded in their evidence, so they are not described as console-clean.

Upgrade regressions retain original legacy statement-command identities and typed-measure
migration hashes. Exact display fields remain outside the durable reconciliation serialization
contract. Trial-balance proof preserves contributing preparer-attestation classifications even
when a source proof is blocked, and applies the existing authorized disclosure projection.

## Known source limitations

Live Holdings values use exact quantities and derivative multipliers. Where their mutable portfolio
and quote reads supply no retained owner-scoped position-and-mark checkpoint, existing durable
position snapshots omit mark/event identity; shared symbols or equal amounts cannot establish
provenance. These live cells show an explicit unavailable/provisional condition and do not claim
amount-level proof. Existing source-owned passport references are retained where available.
Trial-balance aggregates have immutable contributor/checkpoint passports.

Case comparison measures do not expose retained side-specific passport subjects; missing proof is
explicit rather than replaced by generic record navigation. Generic reconciliation comparisons
retain Expected/Actual labels unless their source identifies
statement/ledger sides. Missing comparison evidence remains missing; compatibility variance zero
must not masquerade as a retained pair. Legacy unversioned mutation adapters retain their earlier
concurrency behavior; guarded browser requests supply versions.

## Boundaries

Preserve posted-record immutability, tenant access, server-owned readiness/permissions, approval
separation and idempotency. No WPF edits. Existing legacy mutation contracts remain compatible;
guarded browser commands must not claim to fix unversioned legacy clients. Do not invent proof,
materiality thresholds, a price importer, or instrument identity from symbol similarity.
