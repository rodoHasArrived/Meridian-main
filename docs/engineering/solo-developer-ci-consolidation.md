# Solo-developer CI consolidation: measured migration

**Status:** audit of current workflow triggers. The owner explicitly requested the production-certification cadence change (migration step 5) in PR #3337; it is included there as a draft for owner review. Other follow-ups still require explicit owner approval. **Scope:** reduce repeated CI work without removing required status checks or hiding financial/security failures.
**Owner:** core-team
**Reviewed:** 2026-10-31 (triggers re-read from `.github/workflows/*.yml`)

## Current duplication (source inspection at merged PR #3334)

| Workflow | Current triggers | Repeated work / distinct role |
| --- | --- | --- |
| `meridian-ci.yml` | PR, main push, merge group, dispatch, reusable call | Four full quality lanes (including docs/source/AI) plus service-backed PostgreSQL integrations; produces named `quality-gate` and `integration-gate` jobs |
| `documentation.yml` | PR, main push, merge group, dispatch | Repeats AI inventory/handoff and documentation-related validation and performs maintained documentation convergence. Its generation/freshness checks are a distinct useful responsibility |
| `roadmap-source-docs.yml` | Scoped PR/main paths, dispatch | Phase-scope and roadmap/source schema validation; manual dispatch also performs generation (automatic generation already owned by Documentation Automation) |
| `ci.yml` | PR, main push, merge group, scheduled, dispatch, reusable call | Secret scan on ordinary changes; full coverage tests already restricted to scheduled/manual main runs |
| `production-certification.yml` | Sunday schedule, dispatch, reusable call (PR #3337 removes the former push-to-`main` trigger) | Release-oriented integrations, dependency evidence and recovery drill; not an ordinary developer completion requirement. Called by `desktop-installer-packaging.yml` on `v*` tags |

These are overlapping validation **pipelines**, not proof that the checks are safe to delete. In particular, changes to an ordinary source module may affect generated source docs, and a skipped GitHub workflow can leave a required check pending.

**Current behavior versus target.** Before PR #3337, `production-certification.yml` triggered on every push to `main`, on a Sunday cron, on manual dispatch, and as a reusable workflow. The target model, now implemented in the PR, runs it only for explicit release candidates (the reusable call on `v*` tags), the Sunday schedule and manual dispatch. A red certification run is release-evidence status, not a reason to block an ordinary Level 1 or Level 2 PR.

**Consumer inventory (migration step 5, 2026-10-31).** No consumer depends on a per-push run:

- `desktop-installer-packaging.yml` (`production` job, `uses: ./.github/workflows/production-certification.yml`) calls it as a reusable workflow on the tag's own commit and passes the coordinator's integration receipt; it does not read any prior `main` run.
- `build/scripts/ci/verify-release-promotion.py` (`required_evidence`) requires `production-certification-<run_number>-<attempt>`, `production-dependency-evidence-*`, `production-documentation-evidence-*` and `production-recovery-drill-<run_id>-<attempt>` from the coordinator run itself.
- No workflow uses `workflow_run` on it (the only `workflow_run` consumer is `generated-repair-publish.yml`, which watches the repair workflow), and `release-readiness.yml`/`release-readiness.py` query only `desktop-installer-packaging.yml` runs.
- No script or doc generator queries its runs through the Actions API; operator docs (`deployment-packaging.md`, `failover-and-recovery.md`) describe the evidence the release run produces. Roadmap and security evidence files cite earlier certification runs as historical snapshots only.
- Required status checks are `quality-gate`, `Secret Scan`, `Analyze csharp` and `Analyze javascript-typescript` (see [CI/CD ownership and rollout](ci-cd-optimization.md#administrator-rollout)); Production Certification is not among them. The app cannot read classic branch protection or rulesets, so the owner must confirm in repository settings that no ruleset requires a `Production Certification` context before merging; a required but never-reporting context would leave main-targeting PRs pending.

## What runs when (from workflow triggers)

| Event | Workflows |
| --- | --- |
| Pull request (every PR) | `meridian-ci.yml` (`quality-gate`, `integration-gate`), `ci.yml`, `documentation.yml`, `codeql.yml` |
| Pull request (path-filtered) | `roadmap-source-docs.yml`, `schema-control.yml`, `golden-path-validation.yml`, `demo-smoke.yml`, `ibapi-smoke.yml`, `windows-desktop-build.yml`, `pipeline-benchmark.yml`, `dependency-locks.yml`, `maintenance.yml`, `web-screenshot-capture.yml`, `copilot-setup-steps.yml` |
| Merge group | `meridian-ci.yml`, `ci.yml`, `documentation.yml`, `codeql.yml` |
| Push to `main` | The every-PR set above, `generated-repair-publish.yml`, plus the path-filtered `roadmap-source-docs`, `golden-path-validation`, `demo-smoke`, `ibapi-smoke`, `windows-desktop-build`, `pipeline-benchmark`, `maintenance` |
| Scheduled | `ci.yml` (daily), `ci-metrics.yml` (daily), `generated-repair-publish.yml` (daily), `production-certification.yml` (Sunday), `desktop-installer-packaging.yml` (Sunday), `codeql.yml` (Tuesday), and, each on a Monday, `maintenance.yml`, `ai-navigation-refresh.yml`, `provider-validation.yml`, `ibapi-runtime.yml` and `release-readiness.yml` |
| `v*` tag | `desktop-installer-packaging.yml`, which calls `meridian-ci`, `ci`, `codeql`, `production-certification` and `publish-smoke` |
| Manual only | `targeted-test.yml`, `wpf-dev-validation.yml`, `wpf-route-validation.yml`, `robinhood-options-smoke.yml`, `roadmap-tools-manual.yml`, `branch-cleanup.yml`, `ci-concurrency-benchmark.yml`, desktop screenshot/manual/prerelease/publish runners, and `workflow_dispatch` on most others |
| Private / live provider | `provider-validation.yml` and `ibapi-runtime.yml` are scheduled/manual only (never PR-triggered); live-provider evidence belongs here or in the owner's private records. Missing live access yields `live-untested`, never a blocked PR. |

Path filters, exact job conditions, and event sub-types are in each workflow file and [`.github/workflows/README.md`](../../.github/workflows/README.md); this table is a navigation aid and the files remain authoritative.

## Migration plan, in order

1. **Baseline cost and enforcement before changing triggers.** For a representative set of docs-only, UI-only, domain/ledger, and workflow PRs, capture elapsed time, billed job minutes, failure modes, and jobs that GitHub actually requires. The GitHub app may not be permitted to read repository rulesets; the owner must confirm required check names in repository settings.
2. **Deduplicate *execution*, not verdicts.** Prefer one authoritative documentation validation/generation execution with a reusable workflow or a retained artifact/receipt. Preserve a stable compatibility check for any existing required job while migrating. Receipt reuse must bind to the exact commit and inputs; do not treat a skip as a pass.
3. **Treat change classification as an optimization hint, never a safety authority.** Run focused fast feedback when a change is obviously isolated, but conservatively expand to full impacted validation when shared contracts, build scripts, generated docs, workflow files, or dependency graph change.
4. **Retain security and finance guards.** Secret scan, authorization/tenancy, accounting/ledger invariants, storage migrations and PostgreSQL integration coverage must not be silently removed. Keep required `quality-gate` and `integration-gate` until branch-protection dependency is verified.
5. **Decouple release *cadence* from development completion (done in PR #3337, pending owner review).** Production certification is release-specific and scheduled rather than a condition for a docs or UI PR. The consumer inventory above found no per-push dependency, so the push-to-`main` trigger was removed while `workflow_call`, `workflow_dispatch` and the Sunday schedule remain. Remaining owner check: confirm no ruleset requires a `Production Certification` context, and run one `workflow_dispatch` on a release-candidate commit to confirm full evidence (acceptance test 6). Exact-SHA certification of an arbitrary `main` commit is now on demand rather than automatic.
6. **Measure after migration.** Compare median PR feedback latency, total duplicated runner minutes and first-attempt failures. Revert if required checks become pending, docs freshness is missed, or financial/security coverage falls.

## First proposed executable tranche (requires separate governance approval)

- Investigate converging `documentation.yml:validate-docs` with `meridian-ci.yml:verify-docs` while preserving both externally observed job verdicts during transition.
- Keep `documentation.yml:regenerate-docs` until confirmed reproducible generation is owned by one lane.
- Keep `roadmap-source-docs.yml:scope-gate` independent: it validates PR phase policy, not merely documentation syntax.
- Release-only certification moved to scheduled/manual/release-call operation in PR #3337 after the dependency inventory above; branch-rule confirmation remains an owner action. Other items here still need separate approval.

## Acceptance tests for any future workflow edit

1. A docs-only PR still gets a decisive required-status result and detects stale generated output.
2. A ledger change still exercises finance contract tests and persisted PostgreSQL behavior.
3. A secret introduced into a test branch is detected by the scanner (using safe synthetic canary data).
4. Merge-group and main-push events retain applicable validation and exact-SHA evidence.
5. A cancelled or failed upstream job cannot yield a successful compatibility gate.
6. Manual release certification produces the same required recovery/signing evidence as before.
7. All conditional GitHub expressions and reusable workflow input/output contracts pass actionlint and workflow verification.

## Policy alignment

Follow [solo-developer workflow](../ai/solo-developer-workflow.md): fast targeted checks for routine work, stronger invariants for financial/security changes, and no requirement for live financial accounts. Never confuse simulated success with provider certification. The owner decides if and when to migrate branch protections; no agent is authorized to change those rules implicitly.
