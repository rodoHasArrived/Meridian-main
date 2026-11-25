# CI health ledger

Scheduled task: CI health and test-suite review. Written by the scheduled run; read by the Flaky-test fixer.
Status values: open, resolved (no recurrence 7 days or fix merged), changed.

## Scorecard history

Pass rate = success / (success + failure), cancelled and in-progress runs excluded. Median durations are from successful jobs sampled across 60 runs (main push and PR).

| Run date | main SHA | Window | Meridian CI main pass | Meridian CI PR pass | quality-gate fail share | verify-dotnet median | PostgreSQL integration median | verify-browser median | verify-docs median | verify-workflows median |
|---|---|---|---|---|---|---|---|---|---|---|
| 2026-11-25 | 63a615c73 | 2026-11-18 to 2026-11-25 | 28/35 = 80% (30 cancelled, 2 running) | 88/165 = 53% | 82/202 = 41% | 21.0 min | 16.4 min | 11.4 min | 6.4 min | 6.2 min |

## Open findings

| ID | Flaky test or failure cause | Files or job | First seen | Last seen | Occurrences | Status | Run IDs |
|---|---|---|---|---|---|---|---|
| ci-health-001 | Design-system ratchet rejects screens with hand-styled status chips, raw Intl or money formatters, raw tables | src/design-system/design-system-ratchets.test.ts; verify-browser | 2026-11-18 | 2026-11-21 | 39 failing runs (PR branches plus main run 44847246050) | open (PR-level) | 44824443959, 44847246050 |
| ci-health-002 | WHITESPACE format errors and File-size ratchet failures | src/Meridian.Core/Config/CrossProviderTolerancePolicy.cs; verify-dotnet | 2026-11-18 | 2026-11-20 | 27 failing runs (main run 44782663509) | open (PR-level) | 44231835686, 44226612908, 44782663509 |
| ci-health-003 | Docs check: hardcoded-API-key and ADR status-field errors | verify-docs (docs check) | 2026-11-18 | 2026-11-21 | 35 failing runs (main 44219547228, 44363767596, 44420923964, 44435649476, 44854111290) | resolved on main (no main failure since 2026-11-21 03:56Z); still seen on PR branches | 44216615718 |
| ci-health-004 | Workflow ratchet baseline drift: unbaselined inline SHA-256 site, tracked helper absent from baseline | verify-workflows (ratchet) | 2026-11-19 | 2026-11-20 | 14 failing runs | open (PR-level) | 44425616053 |
| ci-health-005 | PostgreSQL integration evidence gate: compile errors (CS1503, CS0104 ambiguous SymbolBackfillStatus/BackfillJobStatus) on a branch | service-backed-integrations; integration-gate | 2026-11-20 | 2026-11-21 | 27 integration-gate failures; main 44847246050 and 44854111290 | resolved on main; branch-specific | 44834275985, 44847246050, 44854111290 |
| ci-health-006 | Production Certification: same-commit documentation evidence step fails | production-certification.yml, step "Run deterministic documentation automation" | 2026-11-22 | 2026-11-22 | 1 (scheduled) | open | 44980438289 |
| ci-health-007 | Provider Validation: "Prepare provider validation review packet" step fails | provider-validation.yml, job "Wave 1 Provider Validation + DK1 Review Packet" | 2026-11-23 | 2026-11-23 | 1 (scheduled) | open | 45123485451 |
| ci-health-008 | Release Readiness: signing prerequisite unavailable (signing-secrets-unavailable) | release-readiness.yml, job report-release-prerequisites; environment desktop-release-signing | 2026-11-23 | 2026-11-23 | 1 (scheduled) | open (environment, not code) | 45139261913 |
| ci-health-009 | Web Screenshot Capture failures on codex/* branches (not diagnosed) | web-screenshot-capture.yml | 2026-11-20 | 2026-11-21 | 20 failing runs | open (undiagnosed) | not examined |
| ci-health-010 | PR run churn: most PR runs cancelled by concurrency (not a failure, but adds friction) | meridian-ci.yml concurrency | 2026-11-18 | 2026-11-25 | 167 cancelled PR runs of 372 | open (structural) | n/a |

No flaky test was verified in this window: zero commit SHAs had both passing and failing runs, and 5 runs needed a second attempt. No flaky-test PR is open from the Flaky-test fixer (ledger/flaky-fix.md not present).

## Run log

- 2026-11-25 | main 63a615c73 | window 2026-11-18 to 2026-11-25 (UTC days) | Meridian CI: 442 runs (372 PR, 67 push, 3 dispatch). Main push: 28 success, 7 failure (all 2026-11-18 to 2026-11-21), 30 cancelled, 2 running. Main green since 2026-11-21 03:56Z apart from cancellations. Failures dominated by PR-branch guard tests (ci-health-001, -002, -004). Scheduled: Production Certification, Provider Validation and Release Readiness each 1 failure (ci-health-006 to -008). Web Screenshot Capture 20 failures (ci-health-009). First recorded run; no earlier scorecard to compare.
