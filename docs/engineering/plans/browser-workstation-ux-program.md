# Browser workstation UX improvement program

**Status:** implemented; verified with synthetic data  
**Owner:** core-team

**Reviewed:** 2026-11-11
**Approved direction:** P1–P10 review, refined against source on 2026-11-11.  
**Evidence scope:** deterministic synthetic data and scoped automated checks; no claim of live-provider verification.

## Delivery contract

Preserve seven workspaces, financial meaning, shared API ownership, retained evidence,
authorization and fail-closed behavior. Browser presentation may advance ahead of WPF;
record differences in the [parity plan](wpf-web-ui-alignment-plan.md), without changing WPF.

Measure task-region coordinates and scroll extent. Fixed viewport PNG height does not
establish route length. Standard fixtures target task content within 140 pixels and initial
task-region height within 2,200 pixels. Mandatory warnings, zoom and expanded evidence stay reachable.

## Implementation groups

1. **Shell and truth:** persistent integrated provenance, one route heading, combined Context,
   consolidated global status and neutral selection. Keep Ready/Review/Action/Blocked/Info,
   with precise domain labels. Seeded/unknown evidence cannot inherit a Live session label.
2. **Figures and browsing:** shared unit-aware formatters, explicit comparison intervals and
   visible bases. Differing figures expose exact operands and context. TableWorkbench composes
   DenseDataTable; financial statements and editable grids retain their appropriate structures.
   Full filtered exports and personal server-backed views precede dependent controls.
3. **Today and overview:** authorized queue by default, scope as filter, actual person ownership,
   durable assignment/personal acknowledgement/snooze and per-item bulk outcomes. Blocked work
   stays visible. Data leads with provider evidence; Settings with setup completion. Factor Paydown
   and Consolidation become separate, scoped Accounting routes.
4. **Investigation:** typed entity inspector, existing amount-proof API, reconciliation discussion,
   governed approval collaboration, keyboard triage and counted confirmations. Posted-period
   comparisons use equivalent elapsed fiscal windows for open periods.
5. **Charts:** retained run trends, risk utilization, provider verification history, backfill ranges,
   account balance components, break occurrence aging and retained close-task history. Missing
   history and currency never become invented observations or totals.

## Shared state and compatibility

- Personal views belong to authenticated tenant/operator/table. Ownerless legacy views remain
  read-only tenant views with an explicit copy action. Views never contain rows or permissions.
- Triage stores metadata rather than duplicate source records. Source assignment uses existing
  command authority. Acknowledgement does not resolve work. Snooze is personal, reasoned and
  expiring; escalation invalidates it. Production persistence uses PostgreSQL; fixture adapters
  remain explicitly nonproduction.
- Discussion cannot approve a workflow. Preserve version, trusted actor, rationale, history,
  retry identity and independent-review authority through Operations Continuity.
- Comparisons retain source identities, currency, basis, date windows and filters. Missing prior
  periods are unavailable, never zero or a substituted strategy run.
- General passports belong to `W11-PASSPORT-001`, after `W10-PROV-001`. Reuse subject-addressed
  evidence. Implementation does not grant roadmap acceptance.
- Presentation-only route changes belong to `W8-UX-CONSOL-001`; new contracts do not. Reuse
  existing mark-freshness and break-lineage owners rather than duplicating initiatives.

## Validation and remaining-work accounting

The dashboard build invokes `scripts/workstation-governance.mjs`. In `src/screens/**/*.tsx`,
the AST check detects raw browse tables, JSX calls to `.toFixed`/`.toLocaleString`, and selected
literal status labels on `Badge`. Statement/editor exceptions are explicit. Its current legacy
allowlist is empty; review policy prohibits expanding it. Zero describes these checked patterns,
not universal formatter or status coverage.

Use focused Vitest/axe checks, builds, backend tests and rendered Chromium workflows. Cover scope
changes, malformed/delayed responses, full CSV populations, view ownership, replay/version conflicts,
snooze rules, fiscal boundaries and exact financial signs/units. Persistence changes also require
transaction/concurrency/isolation checks and separate-agent review.

Refresh affected captures and audit findings after integration stabilizes. Report remaining
violations and unsupported source capabilities. Never promote synthetic evidence to live
verification or rewrite historical acceptance. No notifications, publication or WPF changes.

## Source limits preserved by the implementation

- Missing currency, price history, account equity or performance statistics remain unavailable.
  Account-balance component history is not an equity curve; a strategy run's retained equity
  observations are not portfolio-wide history.
- A headline's context is inspectable even when its source has not supplied a retained passport
  subject. Such figures say **unverified**. The optional versioned reference does not certify
  W11, and report amounts continue to use their existing exact-contributor proof drawer.
- Upload and workbook results export their complete returned preview population under
  **Export preview CSV**. This does not claim to export an unparsed original file.
- SQL exports refer to one completed, retained execution. The preview row limit does not truncate
  its export; query-authored limits still apply. Owner-bound execution tokens expire, and quota,
  integrity or incomplete-spool failures produce no partial export.
- Keyboard shortcuts open review and assignment controls only where the domain supports them.
  They do not convert a generic resolve/dismiss gesture into approval, posting or cancellation.
- Legacy audit entries without a retained close-task snapshot remain visible gaps in the chart.
  No historical task observations are reconstructed from today's state.

## Source branch verification record — 2026-11-11

These results describe PR #3497 before integration. They do not establish the state of
PR #3501's combined tree; its integration checks are recorded separately below.

| Check | Result and scope |
| --- | --- |
| Dashboard source suite | 400 files, 4,398 tests passed; subsequent Ledger-link, audit-export and quote-unit regressions also passed in focused runs. |
| Dashboard production build | `npm run build` passed, including TypeScript and browse governance; tracked browser assets were rebuilt. |
| Shared production dependency graph | Contracts, Storage and Ui.Shared built with zero errors. Existing compiler/analyzer warnings remain. |
| Backend regression harness | 259 passed, zero failed/skipped against current production DLLs. Checked-in tests include native DuckDB execution, real PostgreSQL triage, recovery, ownership, comparisons, complete audit exports and audit persistence. |
| Browse governance and Node tooling | Empty checked-pattern baseline; six tooling tests passed. |
| Changed frontend lint | Zero errors; hook warnings remain. |
| Screenshot contracts | 41 Python tests passed. |
| Rendered catalog | 82/82 synthetic route captures passed at 1440 × 1100; freshness matched 82 PNGs. Focused desktop layout and interaction evidence is detailed in the [audit](../../screenshots/web-audit.md). |
| Table theme/overflow inspection | Four Chromium checks passed: light/dark at 1440/1366, visible input borders, opaque menus, selected sticky-cell backgrounds and both scroll-edge states; zero page errors. |
| Documentation CI | `bash scripts/ci.sh --lane verify-docs` passed, including generated-output, contract-parity, roadmap and source-documentation checks. |

The backend run used a source-linked focused harness; the full canonical test assembly did not
complete in this session. This is not a claim that the
entire backend suite or aggregate quality gate passed. No live providers, Windows Chrome/Edge,
WPF runtime or assistive technology were exercised. Local checks do not replace hosted checks.
