# Production Readiness and Test-Debt Audit — 2026-07-27

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-07-27
**Scope:** reporting deployment readiness composition, recovery tooling, PostgreSQL test-harness
debt, and browser-workstation dependency advisories.

This record captures the ranked findings of the July 2026 production-readiness review, what was
remediated on `claude/prod-readiness-test-debt-nebv2w`, and the debt that remains open with owners
and recommended lanes. Numbers reported from earlier investigation runs are labeled with their
provenance; re-verify before relying on them for release decisions.

## 1. Reporting deployment readiness composition (remediated)

The fail-closed reporting deployment gate
(`src/Meridian.Ui.Shared/Services/ReportingDeploymentReadinessService.cs`) requires the production
DI graph to share exact instances: one statement workflow composed with the one durable PostgreSQL
statement authority, and one reconciliation break-queue authority referenced by the casework
handoff, the Operations bridge, the accounting-close bridge, and the final-certification evidence
source. An earlier review observed a production DI graph that failed these exact-instance checks,
with the scheduling-worker failure downstream of the same blockers. On current `main` (after the
statement-to-delivery authority hardening of PR #2528), the real host composition passes every
exact-instance check; the gap this change closes is that nothing pinned that invariant against
regression and resolution failures were silently swallowed.

Remediation in this change:

- `ReportingProductionCompositionReadinessTests` now builds the real host composition (`UiServer`
  with a configured reporting connection string) and asserts every exact-instance invariant the
  gate enforces, so composition drift fails a test instead of only failing at deployment.
- The gate's service resolution no longer swallows dependency-injection exceptions silently. A
  resolution failure is reported as an explicit blocking reason naming the service and cause, and
  readiness stays fail-closed. The exact-instance checks are intentionally preserved; do not relax
  them to make an environment "go green".

## 2. PostgreSQL test-harness debt (open, main-wide)

Evidence from the July 2026 investigation runs (provenance: pre-merge feature SHA and the then
current `main`, both executed against a live PostgreSQL harness; not re-run in this change's
environment, which has no PostgreSQL service):

- Both revisions produced identical results: **541 failed / 218 passed / 759 total** in the
  PostgreSQL-backed test population — the failures are main-wide harness debt, not regressions
  introduced by any single branch.
- An older `main` run had already shown 405 failures, so the debt is growing as PostgreSQL-backed
  coverage grows.

Failure classes identified by the investigation:

1. **Shared-fixture poisoning** — tests share database state through common fixtures and leak
   writes into each other's assumptions, so results depend on execution order and parallelism.
2. **Destructive tests without cleanup** — tests that truncate, drop, or mutate schema/state and
   do not restore it, poisoning every later test in the same database.
3. **Record assertions using reference equality** — assertions that compare C# records by
   reference (or rely on identity) instead of value equality, failing once rows round-trip through
   the database.

Recommended remediation lanes (in order):

1. Introduce per-test-class isolated databases or schemas (create/drop per fixture) so no state is
   shared between classes; this addresses classes 1 and 2 structurally.
2. Sweep destructive tests and require them to run against their own disposable database.
3. Replace reference-equality record assertions with value-based assertions
   (`Should().BeEquivalentTo` or record value equality on rehydrated instances).
4. Only after the harness is deterministic, burn down remaining behavioral failures and make the
   PostgreSQL lane a required gate.

Do not treat the 541 count as a release blocker attributable to a feature branch; do treat it as a
blocker for making PostgreSQL-backed tests part of any required gate.

## 3. Recovery tooling (remediated)

`build/scripts/recovery/invoke-production-recovery.ps1` parsed its `-ConnectionString` by assigning
the `ConnectionString` property of `DbConnectionStringBuilder` in PowerShell. PowerShell adapts
that type as a dictionary, so the assignment created a single literal `ConnectionString` entry and
lost Host/Port/Database/Username/Password — every backup, restore, and drill failed with a
misleading "connection string must include Database and Username" error. The script now invokes the
CLR setter (`set_ConnectionString(...)`), and
`tests/Meridian.Tests/Scripts/ProductionRecoveryScriptTests.cs` runs the script end-to-end in
Backup mode with a stubbed `pg_dump` to prove every connection component reaches the PostgreSQL
tooling (the test no-ops on machines without `pwsh`).

## 4. Browser-workstation dependency advisories (partially remediated, one accepted)

`npm audit` on `src/Meridian.Ui/dashboard` reported four high-severity advisories inherited from
`main`. Remediated in this change and validated with the dashboard build, strict typecheck, and
vitest suite:

- `brace-expansion` DoS (GHSA-mh99-v99m-4gvg): resolved in-range via lockfile update.
- `postcss` source-map path traversal (GHSA-r28c-9q8g-f849): resolved by pinning `postcss` to
  8.5.23 (devDependency + `overrides`, covering Vite's nested copy).
- `react-router-dom` updated 7.18.0 → 7.18.1 (latest 7.x patch).

Accepted risk (still flagged by `npm audit`):

- `react-router` 7.12.0–8.2.0, "RSC Mode CSRF Bypass" (GHSA-qwww-vcr4-c8h2). The vulnerable code
  path is React Router's server-side RSC/framework mode. The dashboard is a client-only Vite SPA
  with no React Router server runtime, so the path is unreachable in the shipped artifact. There
  is no patched 7.x: `react-router-dom` ends at 7.18.1, the fix line is `react-router` 8.3.0, and
  npm's suggested "fix" is a downgrade to `react-router-dom` 7.11.0 (pre-RSC), which would drop
  seven minors of fixes. Do **not** apply `npm audit fix --force` here. Exit paths: migrate the
  dashboard to `react-router` v8 (73 importing files) as its own change, or drop the advisory when
  a patched 7.x appears.

## Validation evidence for this change

- `dotnet test tests/Meridian.Tests --filter FullyQualifiedName~ReportingProductionCompositionReadinessTests|FullyQualifiedName~ReportingDeploymentReadinessServiceTests|FullyQualifiedName~ProductionRecoveryScriptTests`
- `npm --prefix src/Meridian.Ui/dashboard run build` and `npm --prefix src/Meridian.Ui/dashboard run test`
- `bash scripts/ci.sh --lane verify-docs` (also regenerated `docs/status/TODO.md`, which had been
  committed with machine-local `.ai/build` scan entries)
