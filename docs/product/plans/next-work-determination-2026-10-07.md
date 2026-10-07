# What To Work On Next — Meridian (2026-10-07)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-07
**Baseline:** `main` at `2bc041f14477c68622e0e08e9922092a52f241af`
**Supersedes:** [2026-10-05](next-work-determination-2026-10-05.md)
**Method:** re-verified every carried-forward claim against **current source on `main`**; read live
GitHub Actions runs with the `event: push` filter after confirming the staleness caveat below still
reproduces; reproduced the failing gate locally with `npm audit --audit-level=high --json`;
independently checked the upstream registry with `npm view braces versions`; measured each candidate
branch with `git merge-tree --write-tree --name-only origin/main origin/<branch>`; read the roadmap
registry (`docs/roadmap/data/*.yml`) and the readiness tracker
(`docs/product/implementation-todo-list.md`).

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; the tracker remains authoritative for the
> P0 release gate. Recheck conflict counts and CI results against the live head before acting.

## Why this document exists when the bar was not met

The 2026-10-05 determination set the bar for the next one: **"a green certification run, or the
RC."** Neither has happened, and an eighth was written anyway — **#3095**, `docs(product): rank next
work for 2026-10-06`, which is still an **unmerged draft**. That is the predicted failure mode, and
repeating it would add nothing.

This document is not another re-ranking. It exists because **one fact changed that inverts the P0**:
for four consecutive determinations the top recommendation has been *"register a time-boxed
acceptance for `GHSA-vfj7-8cjw-p6xm`"* — explicitly an **owned governance decision** that
`CLAUDE.md` requires a human to make. That decision has not been made in five days.

**It is no longer required.** The true remediation is implemented, evidenced, and zero-conflict in an
open PR. The P0 moves from *a governance decision nobody has made* to *engineering work that is
already done and needs review*. Everything else below is verification and three corrections.

## The lane is still red, and the register is still empty

Verified today, not restated:

- **Certification is red on every scored run since the predecessor.** Runs **#185** (`691f3700`),
  **#186** (`5c6e222c`), **#188** (`dba63aba`) and **#189** (`2da026b5`) are all `failure`; **#187**
  was `cancelled` by concurrency and not scored. **#190**
  ([37644054830](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37644054830)) is
  `queued` on today's head `2bc041f1` as of writing. The last green run is unchanged.
- **Still exactly one failing job.** In #189 the documentation-evidence, PostgreSQL integration and
  encrypted backup/restore jobs are green; only `NuGet and npm dependency evidence` fails, at
  `Dependency evidence failed for: npm (validate-npm-audit.py)`.
- **The acceptance register is still `"accepted": []`**
  (`build/config/security/npm-audit-accepted-advisories.json`).
- **No `v*` tag has ever existed.** `git tag --list` → `eval-v0.1.0-eval.1`, and nothing else.
- **No upstream `braces` fix exists.** Re-checked directly: `npm view braces versions` ends at
  **3.0.3**, which is the advisory's vulnerable version, and `npm view braces version` → `3.0.3`.
  On `main` `braces` is `3.0.3`, `"dev": true`; `overrides` still carries only `postcss` and
  `nanoid`, so no override can patch it.

**Tailwind moved but did not help.** `main` went `3.4.15 → 3.4.19` (#3090), and `braces` is
untouched — commit `5c6e222c` says so in its own message: *"Main's Tailwind 3.4.19 dashboard pipeline
is kept unchanged."* A patch bump inside Tailwind 3 cannot clear this; only leaving Tailwind 3 can.

### One measurement correction: five "high" entries, one advisory

`npm audit --audit-level=high` reports **five** high entries — `braces`, `chokidar`, `fast-glob`,
`micromatch`, `tailwindcss` — but only **one** carries a GHSA
(`GHSA-vfj7-8cjw-p6xm` on `braces`); the other four are its transitive dependents, and
`tailwindcss` is flagged `isDirect: true`. So **a single acceptance entry would have satisfied the
gate**, which is consistent with the predecessor's reading of
`validate-npm-audit.py:38-47`. It also shows why the dependents cannot be patched independently:
the chain terminates at Tailwind 3 itself.

## Tier 0 — Land the Tailwind 4 migration. It deletes the advisory instead of accepting it.

**#3109** (`codex/workstation-and-lot-posting`, *"Complete Tailwind 4 migration and atomic
corporate-action lot posting"*) and **#3096** (`codex/tailwind-v4-postcss-repair`) both move the
dashboard to `tailwindcss` / `@tailwindcss/postcss` **4.3.3** and `vite` **8.0.16**. Measured on both
branches:

```
braces/micromatch entries in package-lock.json: NONE
```

**The vulnerable subtree does not exist after the migration.** `#3109` carries its own recorded
evidence at `docs/security/evidence/2026-10-06-tailwind4/`:

| Evidence | Result |
| --- | --- |
| `npm-audit-gate.json` | `"passed": true`, `"unaccepted": []`, all severities **0**, `total: 0` |
| `npm-audit.json` | `vulnerabilities: {}` — zero across the full dependency graph |
| `manifest.json` | `npm ci` passed; `npm run build` passed; `npm run typecheck:strict` passed; `npm run lint` passed (0 errors, 105 pre-existing warnings); `npm test` **3555 passed, zero failed or skipped**, 327 files |

The manifest records the hash of the **unmodified empty** acceptance register, so the gate goes green
with the register still empty. **No acceptance is registered, and none needs to be** — which also
removes the accept-now/retire-later churn the predecessor flagged as a cost of the acceptance route.

**#3109 is the one to land, not #3096:**

| PR | Total conflicts | Hand-written | Note |
| --- | --- | --- | --- |
| **#3109** | **0** | **0** | Already merged `2bc041f1` into itself (`d8fe48f2f`) |
| #3096 | 80 | 1 (`package-lock.json`) | Same 4.3.3 target; superseded in substance |

### Two things to get right before merging it

1. **#3109 is 289 files and bundles the P0 with critical-row feature work.** It carries the Tailwind 4
   migration *and* W10-LOT-002 atomic corporate-action lot posting, across `src/Meridian.Ledger`,
   `src/Meridian.Contracts`, the database manifest and the roadmap registry. Coupling a CI unblock to
   `critical`-row accounting work means **any review iteration on the lot posting holds the
   certification lane red.** Prefer splitting the dashboard/dependency slice out and landing it
   first; if it ships whole, review it as the P0 it is.
2. **Its evidence was recorded on an older baseline.** `manifest.json` names
   `baseline_commit: 9c96c3f7`, not today's head, and the inputs are hashed. Re-run the dependency
   gate on the merge result before treating the attachment as release evidence.

**Status of its checks: in flight.** At the time of writing #3109's 21 check runs are mostly `queued`
or `in_progress` (`schema-control` is the one `success`; `verify-full` and screenshot capture are
`skipped`). **This document does not predict them** — read the check runs on the live head before
merging.

**If #3109 cannot land quickly, the acceptance route from the predecessor is still correct and still
unmade.** It is the fallback, not the plan.

## Late correction — the lane now fails on three jobs, not one, and #3109 fixes all three

Everything above about "exactly one failing job" was measured on run **#189** (`2da026b5`) and was
accurate there. Run **#190** on today's head `2bc041f1` completed afterwards and **three of the four
certification jobs fail**. Read from the run's own jobs and its uploaded `production-certification-190-1`
evidence artifact:

| Job on `2bc041f1` | Result | Failing step |
| --- | --- | --- |
| `same-commit documentation evidence` | **failure** | `Reject generated documentation drift` |
| `NuGet and npm dependency evidence` | **failure** | `Assert both dependency gates passed` |
| `deterministic PostgreSQL integration and coverage evidence` | **failure** | `Run Meridian service-backed integration tests`, then `Reject missing, failed, skipped or empty deterministic test evidence` |
| `encrypted backup and clean restore drill` | success | — |

**The PostgreSQL failure is one test and the message is exact.** From the artifact's
`meridian-integrations_*.trx` — 1089 executed, 1088 passed, **1 failed**, 0 skipped:

```
Meridian.Tests.Integration.EndpointStubDetectionTests.UiApiRoutes_CategorizedCorrectly
Expected categories["Other"] to be less than or equal to 10 because Too many uncategorized
routes - add new categories as needed, but found 21 (difference of 11).
```

**Both new failures trace to one merge.** The only thing between `2da026b5` (PostgreSQL green) and
`2bc041f1` is **#3106**, bounded accounting onboarding. It adds **11** route constants under
`/api/accounting/onboarding/` to `UiApiRoutes`, none matched by an existing category in
`EndpointStubDetectionTests` — `AccountingSystem` matches `/api/accounting-system`, with a hyphen,
not `/api/accounting/`. Pre-existing uncategorized routes were therefore 10, and 10 + 11 = the 21
observed. The same merge added `OnboardingEndpoints.cs` without regenerating the API contract
dashboard, which is the documentation-evidence failure.

**This strengthens Tier 0 rather than weakening it.** #3109 carries a fix for **all three**:

- the npm advisory, via the Tailwind 4 `package.json`/`package-lock.json` move;
- the documentation drift, since it regenerates `coverage-report.md`,
  `api-contract-coverage-dashboard.{md,json}` and `doc-health-dashboard.{md,json}`;
- the PostgreSQL failure, via commit `7b575c3b2`, which adds the `Onboarding` category.

One caveat on that last fix, worth knowing before it lands: categorizing 11 routes takes `Other`
from 21 back to **exactly 10** against a `<= 10` assertion. It passes with **zero headroom**, so the
next uncategorized route added anywhere re-breaks this test. Raising the cap, or asserting that
`Other` is empty with an explicit allow-list, would be a more durable shape than a magic number that
is now exactly full.

## Tier 1 — Two open PRs implement the same W10-LOT-002 criterion with incompatible contracts

This is new and it will cost real work if it is not caught before a merge.

`W10-LOT-002` is the slate's only `critical` row, and its named remainder is append-only
predecessor/successor corporate-action lot mutations. On `main` today
`grep -rn "Successor" src/Meridian.Contracts/Accounting/Lots/` still returns **nothing**. But **two
independent open branches now add it, with different shapes**:

| PR | Branch | Contract added |
| --- | --- | --- |
| **#3109** | `codex/workstation-and-lot-posting` | `OpenLotCorporateActionSuccessorDto`, `CorporateActionSuccessorRoleDto`, `OpenLotCorporateActionSuccessorProjectionDto` in `OpenLotCorporateAction.cs` |
| **#3102** | `codex/lot-successor-posting` | `OpenLotSuccessorInstructionDto`, `OpenLotSuccessorTargetDto` in `OpenLotSuccessors.cs`, plus `OpenLotDto.CorporateAction` |

`git merge-base --is-ancestor` confirms **neither contains the other**. These are two competing
designs for one criterion on the only `critical` row. #3102's three conflicts are all generated
(`source-hash-manifest.json`, `doc-health-dashboard.*`), so it is *textually* mechanical — which is
the trap: **merging both in sequence produces two parallel successor contracts that compile.**

**Decide which contract shape is canonical before either merges**, and close or re-cut the other.
Related and overlapping: **#3093** (`W10-LOT-002: prove discrete relief of amortized lots`),
**#3067** (`Correct retained lot basis and chained corporate-action provenance`), **#3070**
(amortization posting composition), **#3074** (acquisition evidence by value) are all open against the
same row. Five open PRs on one `critical` row is how a row stays `in_progress`.

## Tier 2 — The statement/reconciliation PostgreSQL round trip is still absent

Carried since 2026-09-27 and **re-measured today, unchanged**:

```
$ grep -rl "LedgerDatabaseFact" tests/ | grep -iE "statement|reconcil|ingest"
(no matches)        # 22 files use the attribute; none is statement, reconciliation, or ingestion
```

This remains **the highest-value pure engineering item on the list** and the only one that is neither
blocked on a governance decision nor duplicated across branches. The precedent is unchanged and
specific: `W9-CORPACT-011` sat blocked on exactly this shape of gap until
`CorporateActionAccountingPostgresRoundTripTests` closed it, and **that round trip found two defects
in the shared spine posting path** that file-store evidence had not surfaced. `W9-INGEST-009` carries
the same boundary in its own record.

Add `StatementReconciliationPostgresRoundTripTests`: camt.053 and BAI2 import → the journal-sourced
ledger population → deterministic split match → casework projection, over the real Postgres stores.
It joins the certification lane automatically. On the evidence it produces, `W9-INGEST-009` is a
candidate for `ready_for_acceptance`.

## Corrections to the standing record

**1. The registry YAML break and the silent fallback parser are both fixed.** The predecessor ranked
"merge #3048" as a P0 because `yaml.safe_load` raised `ScannerError` at line 797 while
`validate-roadmap-registry.py` exited 0. Verified today:

```
$ python3 -c "import yaml; yaml.safe_load(open('docs/roadmap/data/roadmap-items.yml'))"
PARSES CLEAN
$ grep -n "fallback\|_simple_parse\|subset" build/scripts/docs/common.py
(no output)
```

Both defects are gone from `main`. **This P0 is closed** — the analysis in this document was able to
parse the registry directly, which is how the row-hygiene table below was produced.

**2. "Tenancy enforcement is selectable only by an environment variable" is now stale.** Carried from
2026-09-27 as part of the `W9-GOV-008` remainder. A real configuration key exists:
`TenantScopeEnforcementOptions.ConfigurationKey = "TenantScopeEnforcement"`
(`src/Meridian.Contracts/Tenancy/TenantScopeEnforcement.cs:43`), resolved from `IConfiguration` and
`ConfigStore` in `TenantScopeServiceRegistration.ResolveConfiguration`, which also refuses a
misspelled override. `PostgresTenantCutoverReadinessCheck` and `TenantCutoverGuardService` are
registered alongside it.

**What remains of that criterion is narrower than the record says:** the default is still
`DeploymentBoundary = 0` (`TenantScopeEnforcement.cs:17`), so unattributed legacy rows stay visible
under the shipped default. The work is the **posture flip plus the support-matrix statement**, not
building a configuration surface. `PRD-000` must declare this posture regardless.

**3. `W10-PROV-001` is reconciled.** The predecessor flagged it as `planned` since 2026-07-31 with an
open implementation PR. It is now `in_progress`, last reviewed 2026-10-02.

## Row hygiene, computed from the registry today

| Row | Status | Priority | Days since review |
| --- | --- | --- | --- |
| `W8-WPF-PARITY-001` | `in_progress` | high | **93** |
| `W8-UX-CONSOL-001` | `in_progress` | medium | **80** |
| `W9-CORPACT-011` | `ready_for_acceptance` | high | **15** |
| `W10-SEAM-001` | `in_progress` | high | 14 |
| `W10-MARK-001` | `in_progress` | high | 14 |
| `W9-INGEST-009` | `in_progress` | high | 12 |
| `W9-GOV-008` | `in_progress` | high | 9 |

Both W8 rows are **named productization targets** in `program-state.yml`, one of them an active
co-equal UI lane, and neither has been reviewed in roughly three months. `W9-CORPACT-011` has now sat
at `ready_for_acceptance` for **15 days** with its precondition met on PostgreSQL; it costs no
engineering, and **declining is a result too**. Nine rows are `in_progress` across three waves.

`"ShadowOperation"` still returns **nothing** anywhere in `src/` or `tests/`. A criterion on a
`critical` row that exists nowhere in the tree still cannot be burned down: **define it or drop it.**

## The Actions API staleness caveat reproduces — use `event: push`

Worth recording because it changed shape again. Asking for `production-certification.yml` with
`status: completed` returned `total_count: 95` with **#111 (2026-09-29)** as newest — stale by nine
days and 79 runs. The same query with **`event: push`** returned `total_count: 112` and **#190
(today)**. The predecessor saw the inverse split, so **the filter that is stale is not stable.**

**Always cross-check the newest run's `created_at` against the head commit's date before drawing any
conclusion from a run listing.** The predecessor's two earlier cautions still hold: read **check
runs**, not commit statuses; and in the dependency lane read the job **log**, because both gate steps
report `conclusion: success` while the job fails (they are `continue-on-error`).

## What not to work on

- **Registering the `braces` acceptance as the first move.** It is the fallback now, not the plan.
  #3109 deletes the advisory; accepting it would mean registering a risk, then retiring it days later.
- **#3096.** Same 4.3.3 target as #3109, 80 conflicts against 0.
- **Merging #3102 and #3109 both.** Resolve the successor-contract collision first.
- **Re-implementing the registry YAML fix or "fixing" the three tracker links.** Both are closed; the
  links were never broken and the checker that reported them is fixed.
- **Re-running the certification lane hoping for green.** Deterministic against the live advisory
  database until either the migration lands or the register changes.
- **New product surface.** The `program-state.yml` `scope_gate` is explicit and nine rows are open.
- **Majors before the freeze.** #3058 (`lucide-react` 0 → 1) and #2587 (`react-router`) wait. Note
  #3109 already moves `vite` to 8.0.16, so that major is inside the P0 and needs review as such.
- **Resolving #2789's conflicts.** Still self-labelled `[DRAFT — RE-CUT REQUIRED]`, 41 conflicts /
  28 hand-written at last measure. Re-cut or close.
- **Untracking the built asset tree.** `PRD-013` and `PRD-018` require one tracked canonical tree.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | **Review and land #3109 — it is the only change that clears all three failing certification jobs** | Deletes `braces`/`micromatch` (zero vulnerabilities, gate `passed: true`, 3555 tests green, register still empty), regenerates the drifted documentation artifacts, **and** fixes the `UiApiRoutes_CategorizedCorrectly` failure #3106 introduced. Replaces a 5-day-old unmade governance decision with reviewable engineering |
| **P0** | Re-run the dependency gate on the merge result | #3109's evidence is hashed against baseline `9c96c3f7`, not today's head |
| **P0** | Decide the canonical W10-LOT-002 successor contract before #3109 or #3102 merges | Two incompatible shapes, neither containing the other, on the only `critical` row; both merge cleanly enough to land by accident |
| **P0** | Then: signing secret, the ~1 GB installer decision, freeze a green head, tag `v0.1.0-rc.1` | Unchanged and not engineering work. One tag run mints the same-commit evidence five P0 rows wait on; no `v*` tag has ever existed |
| **P1** | Add `StatementReconciliationPostgresRoundTripTests` | Re-measured absent today: 22 `LedgerDatabaseFact` files, none statement/reconciliation/ingestion. The identical gap held `W9-CORPACT-011`, and its round trip found two real defects |
| **P1** | Take or decline `W9-CORPACT-011` (**15 days**) | Zero engineering; precondition met on PostgreSQL |
| **P1** | Consolidate the five open W10-LOT-002 PRs (#3109, #3102, #3093, #3067, #3070, #3074) | Five PRs on one `critical` row is why it stays `in_progress` |
| **P1** | Refresh `W8-WPF-PARITY-001` (**93 days**) and `W8-UX-CONSOL-001` (**80 days**) | Both are named productization targets; one is an active co-equal UI lane |
| **P2** | Flip the tenancy default to `FailClosed`, add the rejection regressions, record the posture in the support matrix | The configuration key already exists — only the default and the declaration remain. `PRD-000` must declare it |
| **P2** | Close #3096 (superseded) and the unmerged determination #3095 | #3096 is the same migration at 80 conflicts; #3095 is an unlanded eighth ranking |
| **P2** | Scope or drop `"shadow-operation acceptance"` | Still zero source presence; it cannot be burned down as written |
| **P3** | Implement the `.gitattributes` merge driver / documented regenerate-on-merge | Recurring by construction; still the best-value P3 |
| **P3** | Gate link integrity in CI | The checker no longer emits false positives, so a gate would mean something |
| **P3** | Re-cut or close #2789 | Carried its own `RE-CUT REQUIRED` label for weeks |

## The bar for the next determination

**Unchanged, and now concrete: a green certification run, or the RC.** The route to green is no longer
a decision nobody has made — it is a PR with evidence attached. If the next determination opens on a
red lane *and* #3109 unmerged, the thing to examine is the review path, not the backlog.

## On archiving the predecessors

Convention (`docs/product/plans/README.md`) says to move a superseded determination into
`archive/docs/plans/`. **2026-09-27 is again left in place**: `program-state.yml` cites it by path as
the recorded operator-session plan and the tracker deep-links its section anchors. Archiving
2026-09-27, 2026-10-02, 2026-10-04 and 2026-10-05 with the `program-state.yml` and tracker references
updated in the same pass remains a separate governance-reviewed step.
