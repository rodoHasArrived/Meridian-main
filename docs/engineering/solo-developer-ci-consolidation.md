# Solo-developer CI consolidation: measured migration

**Status:** proposed. **Scope:** reduce repeated CI work without removing required status checks or hiding financial/security failures.

## Current duplication (source inspection at merged PR #3334)

| Workflow | Current triggers | Repeated work / distinct role |
| --- | --- | --- |
| `meridian-ci.yml` | PR, main push, merge group, dispatch, reusable call | Four full quality lanes (including docs/source/AI) plus service-backed PostgreSQL integrations; produces named `quality-gate` and `integration-gate` jobs |
| `documentation.yml` | PR, main push, merge group, dispatch | Repeats AI inventory/handoff and documentation-related validation and performs maintained documentation convergence. Its generation/freshness checks are a distinct useful responsibility |
| `roadmap-source-docs.yml` | Scoped PR/main paths, dispatch | Phase-scope and roadmap/source schema validation; manual dispatch also performs generation (automatic generation already owned by Documentation Automation) |
| `ci.yml` | PR, main push, merge group, scheduled, dispatch, reusable call | Secret scan on ordinary changes; full coverage tests already restricted to scheduled/manual main runs |
| `production-certification.yml` | Main push, Sunday schedule, dispatch, reusable call | Release-oriented integrations, dependency evidence and recovery drill; not an ordinary developer completion requirement even though it runs on every main push |

These are overlapping validation **pipelines**, not proof that the checks are safe to delete. In particular, changes to an ordinary source module may affect generated source docs, and a skipped GitHub workflow can leave a required check pending.

## Migration plan, in order

1. **Baseline cost and enforcement before changing triggers.** For a representative set of docs-only, UI-only, domain/ledger, and workflow PRs, capture elapsed time, billed job minutes, failure modes, and jobs that GitHub actually requires. The GitHub app may not be permitted to read repository rulesets; the owner must confirm required check names in repository settings.
2. **Deduplicate *execution*, not verdicts.** Prefer one authoritative documentation validation/generation execution with a reusable workflow or a retained artifact/receipt. Preserve a stable compatibility check for any existing required job while migrating. Receipt reuse must bind to the exact commit and inputs; do not treat a skip as a pass.
3. **Treat change classification as an optimization hint, never a safety authority.** Run focused fast feedback when a change is obviously isolated, but conservatively expand to full impacted validation when shared contracts, build scripts, generated docs, workflow files, or dependency graph change.
4. **Retain security and finance guards.** Secret scan, authorization/tenancy, accounting/ledger invariants, storage migrations and PostgreSQL integration coverage must not be silently removed. Keep required `quality-gate` and `integration-gate` until branch-protection dependency is verified.
5. **Decouple release *cadence* from development completion.** Production certification should be release-specific and/or scheduled rather than a condition for a docs or UI PR. Before changing its main-push trigger, inventory consumers of exact-SHA artifacts and recovery receipts, and verify a manually dispatched release candidate still gets full evidence.
6. **Measure after migration.** Compare median PR feedback latency, total duplicated runner minutes and first-attempt failures. Revert if required checks become pending, docs freshness is missed, or financial/security coverage falls.

## First proposed executable tranche (requires separate governance approval)

- Investigate converging `documentation.yml:validate-docs` with `meridian-ci.yml:verify-docs` while preserving both externally observed job verdicts during transition.
- Keep `documentation.yml:regenerate-docs` until confirmed reproducible generation is owned by one lane.
- Keep `roadmap-source-docs.yml:scope-gate` independent: it validates PR phase policy, not merely documentation syntax.
- Move release-only certification to scheduled/manual operation **only after** dependency and branch-rule inventory. No change is authorized by this document alone.

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
