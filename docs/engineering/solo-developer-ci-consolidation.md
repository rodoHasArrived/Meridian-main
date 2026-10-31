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

Record the first completed candidate run and subsequent comparable PR runs in the
PR discussion and existing [CI operational metrics](ci-cd-optimization.md#measurement-and-promotion).
Use the existing `collect-actions.py` and `ci-metrics.py`, not another reporting job.
Preserve all pages, retries, unsuccessful work, event type, SHA and run/attempt IDs.
Compare documentation runner-minutes and creation-to-both-results latency separately
from Meridian quality-gate latency and end-to-end PR feedback. Do not infer all-required-check
completion when the applicable protection policy is unavailable.

The new short verdict adds a scheduling boundary; moving unique checks also changes
parallelism. Therefore reduced runner work does **not** guarantee lower PR feedback
latency. Keep baseline/candidate outcomes and queue time visible; do not declare a
performance win from removed commands alone. At implementation time no post-change
hosted measurement has yet been established.

## Remaining work, only where measurements justify it

Review cross-workflow generation reuse after identifying its consumers and exact
required checks. Keep Roadmap Source Docs phase/schema ownership, financial/security
coverage and release-specific evidence intact. No release-cadence or branch-rule
change is authorized by this tranche.

Follow [solo-developer workflow](../ai/solo-developer-workflow.md): focused validation,
synthetic data, honest results and the developer's merge/activation decision.
