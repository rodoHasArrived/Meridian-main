# Solo-developer CI consolidation: measured migration

**Status:** first executable tranche implemented; hosted rollout under review.
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
CodeQL, release certification, and repository protections are also unchanged.
Cross-workflow duplication with Meridian CI still exists: this bounded change
removes internal duplication without introducing cross-run trust or waiting machinery.

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

All three are pull-request runs with two jobs. The nearby control at
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

The unmodified `ci-metrics.py` confirms 120, 149 and 156 runner-seconds from these
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
performance win from removed commands alone. The first post-change observation above
is diagnostic; successful hosted validation and a comparable timing observation
remain necessary before claiming this increment is complete.

## Remaining work, only where measurements justify it

Review cross-workflow generation reuse after identifying its consumers and exact
required checks. Keep Roadmap Source Docs phase/schema ownership, financial/security
coverage and release-specific evidence intact. No release-cadence or branch-rule
change is authorized by this tranche.

Follow [solo-developer workflow](../ai/solo-developer-workflow.md): focused validation,
synthetic data, honest results and the developer's merge/activation decision.
