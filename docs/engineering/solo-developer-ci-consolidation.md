# Solo-developer CI consolidation: measured migration

**Status:** Documentation Automation consolidation implemented in PR #3336; current-head verification and owner review remain explicit.
**Owner:** core-team
**Reviewed:** 2026-10-31 (combined with the workflow inventory from merged PR #3337)
**Scope:** remove duplicate execution without deleting required check identities or financial/security validation.

## Documentation Automation: one worker, two existing results

`documentation.yml` keeps its workflow name, `validate-docs` and `regenerate-docs`
job names, read-only permissions, concurrency policy, and PR/main/merge-group/manual
triggers. It uses no path filters and no privileged PR event.

- `regenerate-docs` is the single checkout/Python/dependency/generation worker.
- The rules-engine command and all four Codex-memory commands move unchanged into
  that worker because they are not in the maintained-generation core profile.
- Examples, AI inventory, strict AI handoff and AI contract drift already run in
  the required core profile. Their separate workflow invocations are removed.
- `validate-docs` becomes a small `needs: regenerate-docs` / `if: always()` result
  check. Only `success` passes; failure, cancellation, skips and missing results
  cannot become green. It does not check out code, install tools, poll another run,
  download another run's artifacts, or rely on a cached verdict.
- Generation, dashboard-regression detection, generated-output freshness,
  whitespace checks and the existing repair artifact remain required/retained.

This removes one redundant **heavyweight worker**, not a required status check.
The total remains two jobs: one worker and one short compatibility verdict.
`tests/scripts/test_documentation_workflow_consolidation.py` protects the commands,
profile ownership, trigger coverage and failure propagation in the existing script-test lane.
It creates no new workflow or required check.

## Coverage ownership

| Validation | Execution owner in Documentation Automation |
| --- | --- |
| Rules engine; memory, task and goal checks; memory unit tests | Explicit commands in `regenerate-docs` |
| Examples; AI inventory; strict handoff; AI contract drift | Required `core` profile in `run-maintained-generation.py` |
| Roadmap/source rendering, diagrams and other core validators | Existing maintained-generation catalog, unchanged |
| Generated freshness, dashboard delta, whitespace, repair evidence | Existing remainder of `regenerate-docs`, unchanged |
| Required validation result | Same-run `validate-docs` verdict, never a successful skip |

Meridian CI's complete `verify-docs` lane and its `quality-gate` are unchanged.
Its financial tests, PostgreSQL integration and `integration-gate`, Secret Scan,
CodeQL and repository protections are also unchanged by this consolidation.
The separate release-certification cadence change from PR #3337 is retained below.
Cross-workflow duplication with Meridian CI still exists: this bounded change
removes internal duplication without introducing cross-run trust or waiting machinery.

## Separate release cadence from merged PR #3337

PR #3337 implemented the owner-requested removal of the push-to-`main` trigger
from `production-certification.yml`. Its Sunday schedule, manual dispatch and
reusable call remain; `desktop-installer-packaging.yml` calls it for a `v*` release
on that release's commit. A certification failure remains release-evidence status,
not an ordinary Level 1 or Level 2 development prerequisite. This Documentation
Automation change preserves that separately merged behavior.

Remaining owner verification is explicit: confirm that no enforced ruleset requires
a `Production Certification` context, and exercise a manual release-candidate
dispatch to confirm the required same-commit evidence and recovery/signing
acceptance. Exact-SHA certification of an arbitrary main commit is now on demand.
The merge itself does not prove these owner checks or release acceptance passed.
Other workflow or branch-rule changes still require their own explicit owner decision.

**Consumer inventory retained from PR #3337 (2026-10-31).** No consumer depends on a per-push run:

- `desktop-installer-packaging.yml` (`production` job, `uses: ./.github/workflows/production-certification.yml`) calls it as a reusable workflow on the tag's own commit and passes the coordinator's integration receipt; it does not read any prior `main` run.
- `build/scripts/ci/verify-release-promotion.py` (`required_evidence`) requires `production-certification-<run_number>-<attempt>`, `production-dependency-evidence-*`, `production-documentation-evidence-*` and `production-recovery-drill-<run_id>-<attempt>` from the coordinator run itself.
- No workflow uses `workflow_run` on it (the only `workflow_run` consumer is `generated-repair-publish.yml`, which watches the repair workflow), and `release-readiness.yml`/`release-readiness.py` query only `desktop-installer-packaging.yml` runs.
- No script or doc generator queries its runs through the Actions API; operator docs (`deployment-packaging.md`, `failover-and-recovery.md`) describe the evidence the release run produces. Roadmap and security evidence files cite earlier certification runs as historical snapshots only.
- The documented required status checks are `quality-gate`, `Secret Scan`, `Analyze csharp` and `Analyze javascript-typescript` (see [CI/CD ownership and rollout](ci-cd-optimization.md#administrator-rollout)); Production Certification is not among them. The app cannot read classic branch protection or rulesets, so the owner must confirm in repository settings that no ruleset requires a `Production Certification` context if that confirmation has not already been recorded; a required but never-reporting context would leave main-targeting PRs pending.

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

## Measurement: elapsed runner usage is not billing

The pre-change observation below is one completed PR run, not a representative
cohort, an identical-commit experiment, or proof of rollout savings.

| Baseline | Observation |
| --- | --- |
| Run / attempt | [41171030506 / 1](https://github.com/rodoHasArrived/Meridian-main/actions/runs/41171030506) |
| Revision | `097a5bd5f43cf4b1232d36d94bac135815f82e7d` (PR #3332) |
| Validation worker | Job `124448825443`: 05:03:27–05:03:58 UTC, 31 seconds |
| Generation worker | Job `124448825612`: 05:03:27–05:04:56 UTC, 89 seconds |
| Total elapsed runner usage | 120 seconds = 2.00 runner-minutes |
| Documentation feedback | 92 seconds: workflow creation 05:03:24 to last result 05:04:56 UTC, October 31, 2026 |

The [attempt-specific jobs response](https://api.github.com/repos/rodoHasArrived/Meridian-main/actions/runs/41171030506/attempts/1/jobs?per_page=100)
contains both jobs (`total_count=2`); the run metadata records one attempt.
These are start-to-completion intervals, not rounded billable minutes or costs.

The first candidate completed with a generated-output freshness failure. Both
documentation jobs failed, as required; the compatibility verdict did not hide the
producer's failure. A nearby successful pre-change run helps expose queue variation:

| Observation / attempt 1 | Outcome | Elapsed runner time | Creation to both results |
| --- | --- | --- | --- |
| [Recorded baseline 41171030506](https://github.com/rodoHasArrived/Meridian-main/actions/runs/41171030506) | Success | 31 + 89 = 120 seconds (2.00 minutes) | 92 seconds |
| [Nearby pre-change 41173179254](https://github.com/rodoHasArrived/Meridian-main/actions/runs/41173179254) | Success | 27 + 122 = 149 seconds (2.48 minutes) | 915 seconds |
| [First candidate 41173208592](https://github.com/rodoHasArrived/Meridian-main/actions/runs/41173208592) | Failure | 3 + 153 = 156 seconds (2.60 minutes) | 910 seconds |
| [Corrected candidate 41176458457](https://github.com/rodoHasArrived/Meridian-main/actions/runs/41176458457) | Success | 2 + 119 = 121 seconds (2.02 minutes) | 126 seconds |

All four are pull-request runs with two jobs. The nearby control at
`e64d6dbda7d2997f1d1b824e9b4df949e8e8ecbe` was created 23 seconds before the
candidate at `7598d5f96fe244b734271d651a1c2761d093c21c`. Their attempt-specific
[control jobs](https://api.github.com/repos/rodoHasArrived/Meridian-main/actions/runs/41173179254/attempts/1/jobs?per_page=100)
and [candidate jobs](https://api.github.com/repos/rodoHasArrived/Meridian-main/actions/runs/41173208592/attempts/1/jobs?per_page=100)
retain the timestamps. Candidate generation took two convergence passes and 92
seconds, compared with 39 seconds in the recorded baseline and 55 in the nearby
control. The candidate worker waited 673 seconds to start; its dependent verdict
waited another 81 seconds and executed for 3 seconds. These observations establish
neither runner savings nor faster feedback. Preserve the failed attempt when adding
the repaired run; do not compare unsuccessful extra work as a clean performance trial.

The unmodified `ci-metrics.py` confirms 120, 149, 156 and 121 runner-seconds from these
attempts. This scoped export does not cover every workflow or required check, so
global completeness and required-check completeness remain unverified. Broad
promotion targets are not an additional gate for this focused change.

Record the corrected candidate and subsequent comparable PR runs in the existing
PR description and [CI operational metrics](ci-cd-optimization.md#measurement-and-promotion).
Use the existing `collect-actions.py` and `ci-metrics.py`, not another reporting job.
Preserve all pages, retries, unsuccessful work, event type, SHA and run/attempt IDs.
Compare documentation runner-minutes and creation-to-both-results latency separately
from Meridian quality-gate latency and end-to-end PR feedback. Do not infer all-required-check
completion when the applicable protection policy is unavailable.

The new short verdict adds a scheduling boundary; moving unique checks also changes
parallelism. Therefore reduced runner work does **not** guarantee lower PR feedback
latency. Keep baseline/candidate outcomes and queue time visible; do not declare a
performance win from removed commands alone. The corrected sample at
`dfccbfffdea8326c45402b221e592db485085cf7` passed both documentation jobs and the
[Meridian quality gate](https://github.com/rodoHasArrived/Meridian-main/actions/runs/41176458416).
It used one more runner-second and 34 seconds more documentation feedback than the
original baseline. Its quality gate finished in 17:02 versus the original 17:26;
the unchanged .NET lane determined completion in both runs. These observations do
not establish a consistent causal speedup. Both PR revisions' documentation work,
including the failure, totals 277 seconds (4.62 elapsed runner-minutes).

That successful sample predates the integration of merged PR #3337. Any refreshed
head needs its own applicable hosted checks; do not carry the earlier pass forward
as current-head evidence. Keep subsequent timing results in the existing PR and
metrics tooling without adding a new report workflow.

## Remaining work, only where measurements justify it

Review cross-workflow generation reuse after identifying its consumers and exact
required checks. Keep Roadmap Source Docs phase/schema ownership, financial/security
coverage and release-specific evidence intact. No release-cadence or branch-rule
change is authorized by this tranche.

Follow [solo-developer workflow](../ai/solo-developer-workflow.md): focused validation,
synthetic data, honest results and the developer's merge/activation decision.
