# CI/CD validation ownership and rollout

**Status:** staged implementation; human governance review required  
**Owner:** core-team  
**Reviewed:** 2026-10-20

## Coverage ownership

The canonical local command remains `bash scripts/ci.sh`. GitHub Actions is the merge
authority. Consolidation removes duplicate execution, never a validation purpose.

| Validation before consolidation | Owner after consolidation |
| --- | --- |
| Legacy CI and Meridian CI restore, formatting, warning/file-size gates, web build and complete non-integration test roster | Meridian CI `verify-dotnet`, through `scripts/ci.sh` |
| Legacy browser install, generated contracts, lint, strict types, tests, production bundle and bundle freshness | Meridian CI `verify-browser`, through `scripts/ci.sh` |
| Legacy delivery/staleness claims, provider tooling tests, TODO registry, AI contract/navigation/handoff checks, generated-doc drift and escalation/floor guards | Meridian CI `verify-docs`, through `scripts/ci.sh` |
| Legacy roadmap/source determinism | Meridian CI `verify-docs`; Roadmap Source Docs retains phase scope and diagram validation |
| Secret Scan | CI on PR, main, merge group and standalone/reusable invocations |
| Nightly full coverage/scenarios | CI nightly/manual main; retained filters and coverage evidence |
| WPF desktop, dev-loop and both route filters | One automatic Windows build, separate named test results; manual wrappers retained |
| Lifecycle supervisor and installer setup tests | Automatic Windows validation; release preflight remains |
| Golden Path, schema control, providers and deterministic integrations/recovery | Existing specialist owners; production certification also callable by releases |
| Workflow syntax, hygiene, manifest, skips and script tests | Meridian CI `verify-workflows` |
| Design-system package freshness, export integrity, dashboard asset projection and Chromium regressions (previously run by hand) | Design System Package `package-integrity`, path-filtered; not a required check |
| Design-system whole-release validator and retained Python suite | Design System Package `release-governance`, path-filtered; exits with the gates' real status; not a required check |

Meridian CI retains the four verification lanes and both gate identities. `verify-docs` owns
all automatic docs rules, memory validation, maintained generation, dashboard regression and
freshness checks on PRs to any branch, main pushes and merge groups. Documentation Automation
is now an explicit manual refresh using the same catalog and checker. The dashboard baseline
uses the preceding commit; a `health_score` drop of more than 10 points still fails when the
baseline exists. New generated files and deletions remain part of the drift check, with repair
patches and generation evidence uploaded by the docs lane.

For PRs, a complete local git diff classifies only root Markdown and recognized narrative,
diagram and image assets under `docs/` and `archive/docs/` as documentation-only. Source,
build inputs, workflows, metadata and unknown paths still run .NET, as do empty diffs.
Main pushes, merge groups, manual and reusable release gates always run the full .NET lane.
Change detection is a required dependency of the always-reporting quality gate: detection
failure, a missing decision, failed/cancelled lanes or unexpected skips cannot turn green.
Only a successful docs-only classification permits the .NET lane's skipped result.

Maintenance PR/push runs delegate workflow hygiene, AI contract drift and navigation validation to
the canonical lanes. Scheduled/manual Maintenance still repeats those checks; tooling metadata
and its distinct actionlint configuration remain on every Maintenance run. Roadmap Source Docs
retains phase scope, schema and diagram evidence. Similar names alone do not establish duplicate
coverage.

CI and Maintenance concurrency separates event types. Scheduled/manual runs are not cancelled by
ordinary pushes; manual runs have distinct run IDs. This avoids losing scheduled coverage while
preserving cancellation of superseded PR/push validation. It does not establish additional runner
capacity or alter release serialization. Meridian CI additionally never cancels a running `main`
push run (see the November 25 runner-saturation entry below).

Meridian CI retains exactly its four canonical `scripts/ci.sh` lanes and the existing
`quality-gate` aggregation. Its additional `service-backed-integrations` job calls the same
`service-backed-integrations.yml` implementation used by Production Certification. The
always-reported `integration-gate` companion requires success and fails for failed, cancelled
or skipped integration execution, with one exception: a pull request that the integration job
itself classifies as documentation-only skips the suite's steps, as `verify-dotnet` skips its
.NET steps, and the gate accepts exactly that result when both lanes' decisions agree. It uses no path filters; main pushes,
merge groups and manual/reusable calls always classify as requiring the tests.

The shared implementation uses the certification `postgres:17` service, health checks and
eight domain connection strings. The database and credentials are disposable within each
job; no production secrets or privileged PR event are used. Checkout uses the event SHA with
credentials persistence disabled and `contents: read`. Meridian, Direct Lending and Demo project
restores and test runs are attempted independently, so one failure cannot hide another result. The
Meridian suite is built once and run as two concurrent processes (see the November 25 record
below). TRX validation
runs after failures and rejects missing required suite prefixes, failed/skipped/unknown
outcomes and every file with zero discovered tests. Coverage and schema evidence retain the
existing certification artifact contract (90 days); CI companion evidence is retained 14 days.

Demo Smoke uses the focused `Meridian.Demo.Tests` project for the same 26 demo cases, avoiding
compilation of the unrelated monolithic test sources. The solution and canonical runner retain
the ordinary cases, while the shared PostgreSQL gate retains all eight demo integration cases
in its required `demo-integrations` TRX and coverage suite. Evidence creation and reuse both
require that suite. The CLI seed keeps normal restore/build behavior; serve and reset reuse
the Release output with `--no-build`. Runner class, NuGet caching and check identities stay
the same. Compare equivalent hosted runs before claiming a timing improvement.

Administrators may add `integration-gate` as a required companion check after human governance
review and a successful PR/merge-group rollout. This change does not alter repository rulesets
or replace the existing `quality-gate` requirement.

### Design System Package

`design-system-package.yml` runs when a pull request or `main` push changes
`Meridian Design System/**`, the dashboard asset synchronization script, or the workflow itself.
It adds hosted coverage that did not exist; it replaces no lane and leaves the four canonical
`scripts/ci.sh` lanes, `quality-gate` and `integration-gate` unchanged. It uses `contents: read`,
no secrets, and a checkout without persisted credentials.

`package-integrity` runs `build:check`, `release:check`, the release integrity tests, the
dashboard asset projection check and its tests, then the Chromium regressions (`test:patches`,
`test:agency`, `test:browser`). Chromium comes from the Playwright version in the package
lockfile, installed with up to three attempts and cached by lockfile hash. It runs no apt
step, so a package-mirror outage cannot fail the job before a test starts.

`release-governance` runs the whole-release validator and the retained Python suite and exits
with their real status. It carries no `continue-on-error`, baseline or skip: a green result
there has to mean the gates passed. Both passed with zero findings when this workflow was
last revised.

Neither job is a required check. Administrators may add `package-integrity` as a required
check for design-system paths after human governance review, and `release-governance` once
it has stayed green on `main`. Rollback is deleting the workflow file.

## Local verification execution

The local aggregate `scripts/ci.sh` schedules .NET and browser verification with at most two
concurrent lanes and a resource-aware serial fallback; `--jobs 1` selects serial execution.
Docs generation and workflow/script tests run after both lanes, preserving their shared-file
ordering. Each aggregate invocation owns separate artifact roots and records every required lane
result. The four canonical check identities remain stable; hosted .NET execution follows the PR
change classification above.

`--cold` measures a full gate with independent .NET build outputs while keeping dependency caches.
Warm-profile recommendations and explicit edit-start timing are documented in
[build observability](../development/build-observability.md#local-feedback-latency-and-observed-retries).
`make verify-fast` delegates to the same full .NET/browser aggregate as `scripts/ci.sh`.
`make test` and `make pre-pr` restore and invoke the default CI test runner roster; use
`test-unit` for focused C# iteration.
The canonical .NET roster includes `Meridian.Contracts.UnitTests` and `Meridian.Core.UnitTests`;
the moved cases retain their namespaces and assertions, and host-dependent tests remain in
`Meridian.Tests`. These execution changes require human governance review before merge.

## Desktop workflow reuse

SDK/cache setup already lives in `.github/actions/setup-dotnet-nuget`, and the automatic
Windows build shares test slices through `run-windows-ci-tests.py`. A further reusable
read-only validation workflow could expose full, dev-loop and routes modes. Keep signing,
evaluation releases and screenshot PR publication separate: their permissions, protected
environments and artifact/evidence contracts differ. This incremental change keeps those
boundaries and does not combine release workflows.

## Measurement and promotion

The October 20 implementation adds policy-driven generated conflict recovery and version 2
reviewed README fingerprints. The coverage report's protected handwritten suffix is merged
three ways rather than replaced with an incoming generated file. The shared maintained-generation
catalog now owns canonical and hosted documentation generation; Roadmap Source Docs retains
phase/schema validation and uses shared generation for standalone manual runs.

Meridian CI and Documentation Automation separate manual execution from ordinary push cancellation.
Coordinated releases reuse Meridian CI's integration receipt through the service-backed workflow,
which revalidates raw test results, coverage, schema evidence, file digests, exact commit, caller
workflow, run and attempt. Standalone certification still executes its integrations. Production
certification waits for the coordinated CI result; reduced execution duplication does not by itself
prove reduced release latency. A partial rerun cannot reuse an earlier attempt's receipt.

Generated-repair publisher and refresh results retain a structured `outcome` separately from
legacy routing `status`: `published`, `noop`, `ineligible`, `review-required`, `stale-retry`,
`trusted-generation-requested`, `configuration-missing`, and `failed` distinguish operational reasons. Readiness/generation and
refresh results remain distinct. Outcomes are retained for 30 days in
`generated-repair-outcome-publish-<run>-<attempt>` and
`generated-repair-outcome-refresh-<run>-<attempt>` artifacts. A published repair is not proof
that its resulting revision passed authoritative CI.

`CI Operational Metrics` runs daily at 04:43 UTC and on manual dispatch. It collects every attempt
from the previous complete UTC day and retains raw API evidence and reports for 30 days.
Missing workflow, attempt, job or check evidence fails reporting. Unreadable protection policy
and unavailable historical change boundaries remain explicit unavailable data with the original
collection errors retained; they cannot establish required-check success or comparative savings.
Repair outcome counts verify each publisher artifact's run,
attempt and revision. Repair success is evaluated only when the published commit can be linked to
complete, applicable, App-bound required-check evidence. Reports identify the observed cohort,
policy provenance, evaluated denominator and unavailable observations; publication success is not
a substitute. Backlog age is a lower bound from consecutive observed head/base pairs within the
collected window, not an inferred timestamp for when a PR first became stale.

Dependabot groups weekly pip updates across the four maintained requirements directories.
`Dependency Lock Candidates` validates restore-input PRs and manual runs on Linux portable,
Windows portable, Windows x64 and Windows ARM64 profiles. It generates candidates and proves
locked restore in disposable checkouts before uploading manifests, lockfiles and logs.
Ordinary restores remain unchanged until native profiles are reviewed and adopted:

```bash
python build/scripts/ci/dependency-locks.py generate --profile linux-portable --output artifacts/locks-candidate
python build/scripts/ci/dependency-locks.py adopt --candidate artifacts/locks-candidate
python build/scripts/ci/dependency-locks.py verify --profile linux-portable --output artifacts/locks-verified
```

Use a fresh output directory for each command. Adoption checks the pinned revision, all tracked
restore inputs, complete solution coverage and every lock digest. After adoption, the explicit
`-p:MeridianNuGetLockProfile=linux-portable` selects that profile; each native Windows profile
needs its own proof before adoption or enforcement. Caches include the profile lockfile names.

The approved rollout still requires administrator verification of the App/environment and all
effective protection settings, plus a successful eligible repair and merge-group check.
Configuration changes, protected-rule changes, and production releases are not performed by these
implementation workflows.

The [October 5 Actions storage audit](actions-storage-audit-2026-10-05.md) inventories workflow
uploads, measures recent artifacts, and separates routine retention savings from protected
certification/recovery evidence. Its snapshot is bounded and does not reconcile account billing.
The [October 5 rollout measurement](actions-rollout-measurement-2026-10-05.md) retains 24
comparable completed post-rollout PR observations, unsuccessful-attempt accounting and the
strict matched-cohort decision. Six baseline matches do not establish either improvement target.
The [October 15 follow-up](actions-workflow-followup-2026-10-15.md) retains a fresh 59-run,
attempt-complete diagnostic sample, live integration-gate cancellation evidence, and assigned
administrator follow-ups. Current protection enforcement and merge-group success remain
unverified; the sample does not establish savings or justify higher concurrency defaults.

Collect a bounded Actions history with `build/scripts/ci/collect-actions.py`, then pass its
snapshot to `ci-metrics.py`. Collection is read-only: it does not dispatch, rerun, cancel or
delete workflows, modify checks or protections, or change concurrency. Keep the raw snapshot
with the report so workflow/run/attempt/job IDs and collection gaps can be audited. A first-page
or latest-attempt-only export is insufficient for rollout acceptance.

The report separates PRs, main pushes and other events. It measures time from the first workflow
creation for a change until all configured required checks finish, quality-gate latency,
quality-gate execution, job queue delay and total runner minutes. Median and nearest-rank p95
describe latency distributions; runner minutes sum job execution intervals across workflows and
attempts, including unsuccessful work. They are elapsed runner usage, not a billable-minutes or
dollar estimate. Missing timestamps remain unavailable rather than becoming zero-duration jobs.

Quality-gate execution is the union of execution intervals for the configured gate-job roster up
to completion of `quality-gate`, across all attempts. Without a roster, all jobs in its workflow
provide a proxy. Overlapping intervals count once; idle queue and manual-retry gaps do not count
as execution. It is distinct from the brief aggregation job
itself and from creation-to-completion latency. Required-check completion includes checks owned by
other workflows. Unfinished, failed and cancelled attempts remain in waste and reliability
accounting even when a later retry succeeds. Retry time overlaps failure/cancellation time;
use the report's failure/cancellation/retry union instead of adding overlapping categories.

Before evaluating rollout targets, supply disjoint baseline and rollout windows and the
required-check policy that actually applied in those windows. A current branch-rule read
does not prove historical settings. Match changes within each event using complete change
categories and equivalent check, runner, attempt and optional-workflow evidence; report unmatched
and excluded samples explicitly. Dropping a specialist workflow must not count as savings. If an
intentional consolidation changes that workflow set, retain the mismatch until its coverage
equivalence is established separately. Similar categories are observational cohorts, not identical-commit
benchmarks or proof that optimization alone caused a difference.

Run from the repository root with GitHub CLI read access to Actions, checks and the relevant
repository rules:

```bash
python build/scripts/ci/collect-actions.py \
  --repo rodoHasArrived/Meridian-main \
  --since 2026-09-26T00:00:00Z --until 2026-10-04T23:59:59Z \
  --output artifacts/actions-rollout/runs.json
python build/scripts/ci/ci-metrics.py \
  --input artifacts/actions-rollout/runs.json \
  --comparison artifacts/actions-rollout/comparison-policy.json \
  --output artifacts/actions-rollout/report.json \
  --markdown artifacts/actions-rollout/report.md
```

The collector's `--since` and `--until` bounds are inclusive UTC timestamps. It paginates
repository runs and attempt-specific jobs, splits windows that reach the Actions 1,000-run
search cap, and reports incomplete collection with a nonzero exit. The snapshot is still written
with separate completeness flags and access errors, preserving successfully collected run evidence.
`--api-cache` replays an endpoint-to-JSON map exported through another read-only API client,
with no network fallback
for missing responses. Preserve API failures in that map as `{"error": "reason"}`.
`--push-base-map` accepts verified push head-to-before SHA mappings: Actions run metadata
alone does not identify the full diff of a multi-commit push.

Historical Actions PR associations can reflect a later head or a reused branch. PR categories
therefore require the actual tested merge commit, recovered from a reusable-workflow reference
or retained checkout evidence, with its parents verified against the run's head SHA. A live PR
file list or the current base branch is not a substitute for that boundary.
Use `--checkout-evidence` for a saved-log manifest when reusable references are unavailable;
the collector verifies the log digest, exact fetch and checked-out commit, source job ownership,
and merge parents. Log paths resolve relative to the manifest. Failed verification remains
unavailable evidence and cannot establish a comparison category.
Each manifest entry supplies `runId`, `jobId`, `headSha`, `pullNumber`, `checkoutMergeSha`,
`checkoutRef`, `jobLogUrl`, `logSha256` and `logPath`; the same log bytes may instead come from
the API cache. None of these assertions replaces the collector's ownership and commit checks.

The comparison policy contains `baseline` and `rollout` objects with timezone-qualified
`start` and `end` (half-open intervals), plus `requiredChecks` entries containing `name`,
`workflow` and, when required, `appId`. `qualityGate` selects the gate's name and workflow;
its `executionJobs` can identify the four canonical lanes and `quality-gate`, excluding the
independent integration companion from quality-gate execution. `requiredChecksEvidence`
must cite verified historical check equivalence. Omit it when that equivalence is unknown:
the report still shows observations and match counts but does not evaluate savings targets.

### Queue, execution, retries and cancellations

`python build/scripts/ci/ci-metrics.py --input runs.json --output artifacts/ci-metrics.json`
accepts `{"observed_at": "<UTC export timestamp>", "workflow_runs": [...]}`. Embed a `jobs`
array on **each run attempt**, retaining the original API fields, including job `status`,
`conclusion`, timestamps, `run_attempt`, `labels`, runner identity and `steps`. Export all job
pages from `/repos/{owner}/{repo}/actions/runs/{id}/attempts/{attempt}/jobs`; pair them with
metadata from `/actions/runs/{id}/attempts/{attempt}`. Enumerate attempts 1 through the latest
`run_attempt` for each selected run. Do not attach latest-attempt jobs to earlier attempts or
assume the default jobs endpoint contains retry history. Use a bounded creation window and
record its limits; GitHub's run search can cap matching results at 1,000, so split larger windows.

For example, collect one attempt with an authenticated, read-only GitHub CLI session:

```bash
gh api repos/rodoHasArrived/Meridian-main/actions/runs/RUN_ID/attempts/ATTEMPT > attempt.json
gh api --paginate --slurp \
  'repos/rodoHasArrived/Meridian-main/actions/runs/RUN_ID/attempts/ATTEMPT/jobs?per_page=100' \
  > job-pages.json
```

Flatten each page's `jobs` array, verify its length against `total_count`, and attach it to the
attempt metadata before adding that object to `workflow_runs`. Failed API reads are missing
evidence, never an empty successful export. Include cancelled, skipped, pending and zero-job
runs. `jobs: []` records an explicitly empty API result; omitted/null `jobs` means unavailable.
A fixed, timezone-qualified `observed_at` permits repeatable waiting-time measurements for
still-queued jobs. No system clock is substituted when it is absent.

The timing report is **schema version 2**; the independent `--paired-benchmark` report stays at
version 1. Existing workflow/event/attempt groups, `runs`, `successfulSamples` and
`medianRunnerSeconds` remain, with these semantics:

| Evidence | Interpretation |
| --- | --- |
| `queueSeconds` | Job creation to an evidenced start. This observed interval can include scheduling and dependency waits; it is not a pure runner-scheduler delay or proof of runner saturation. |
| `waitSeconds`, `waitEnd` | Creation to an evidenced start, completion of a never-started job, or fixed observation for a still-queued job. `waitEnd` distinguishes `started`, `completed` and `observed`; these populations must not be treated as interchangeable queue samples. |
| `executionSeconds` | Valid start-to-completion time for fresh execution in this attempt. In-progress, never-started, invalid and carried-forward execution is null. |
| `startState`, `timingIssue` | Distinguish `started`, `not_started` and `unknown`, and preserve invalid-start or start-before-creation evidence. GitHub can synthesize `started_at` for jobs with no assigned runner and no executed steps. |
| `runnerSeconds` | Sum only when every job has measurable execution; otherwise null, including empty jobs. This is aggregate job time, not wall-clock latency or billed runner time. |
| `knownRunnerSeconds` | Sum of available fresh execution, or null when none is measurable. Partial evidence, never a complete-cost claim. |
| `summary` | Top-level and per-group sample counts/medians for queue, wait and execution; run-attempt/retry/cancellation counts; job, queued, never-started and unavailable-execution counts. Mixed-outcome descriptive distributions are separate from the successful performance cohort. |
| `cancelledRunKnownRunnerSeconds`, `retryKnownRunnerSeconds` | Available fresh execution in cancelled run attempts or attempts numbered above 1. These can overlap; neither is a complete waste total or an attribution of cancellation cause. |
| `retryHistory` | Observed and missing attempt numbers per run. `retryAttempts` counts exported retry attempts, not inferred missing attempts, retried steps or job retries. |

The rollout cost ledger is separate from raw measured execution. `accountedExecutionSeconds`,
`accountedRunnerSeconds` and `accountedKnownRunnerSeconds` feed `runnerMinutes` and
`knownRunnerMinutes`. Proven terminal jobs that never started, explicit skips, and verified
inherited work add zero new runner cost while their raw execution timing remains null.
An inherited row retains `inheritedFromAttempt` and must match prior execution evidence;
missing evidence never becomes measured execution or a zero-cost assertion.

Rows retain job IDs, raw timestamps, status, conclusion, runner identity and requested labels.
Legacy timestamp-only exports remain readable but cannot distinguish synthetic starts; use the
original runner/step metadata before drawing availability or waste conclusions.
Partial reruns can copy successful results under **new job IDs and the new attempt number**,
with their execution timestamps before their new creation timestamp. These rows remain visible
as `started_before_created` and contribute no timing to the new attempt. Missing, malformed,
timezone-naive or reversed timestamps remain unavailable; they are never clamped to zero.
Duplicate run/attempt rows are rejected. Earlier-attempt jobs must be bound to matching prior-attempt evidence before being treated as inherited; other mismatches are rejected. The tool cannot
prove that an externally prepared job list is fully paginated or that later attempts were not
omitted: retain export coverage alongside the report. Successful medians require a successful
run and successful, measurable jobs; cancelled/incomplete/inherited rows cannot improve them.

### Runner and concurrency review — October 5, 2026

Read-only review of workflow declarations at `73030f0634d045faf65fc5ef6ab3dfe2b93f46ee`,
with separate Actions queries around 20:28–20:29 UTC (13:28–13:29 Arizona time):

- The queued-run endpoint returned all **58 queued runs**; a separate query returned four
  in-progress runs. These are workflow statuses, not counts of active or available runners,
  and the requests are not an atomic snapshot.
- [IB runtime run 37269613950](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37269613950)
  had an unassigned `self-hosted, Windows` job created at 05:51:16 UTC, about **14h38m waiting**.
  Its runner service health, labels/group access and protected-environment eligibility require
  administrator inspection; the queue alone does not identify the cause.
- Two March 27 queued runs,
  [23662997318](https://github.com/rodoHasArrived/Meridian-main/actions/runs/23662997318) and
  [23662972493](https://github.com/rodoHasArrived/Meridian-main/actions/runs/23662972493),
  returned zero job rows. They remain run-only evidence and merit separate inspection.
- Runner inventory and account-wide hosted concurrency/usage were unavailable through the
  authorized interfaces. The connector does not support the runners endpoint, and shell API
  access was unavailable. No runner outage, purchased capacity or binding quota is established.

A bounded sample selected the first 20 completed runs from the latest-100 listing (API creation
order), then fetched every latest-attempt job page and verified the returned counts. Those runs
were created between 19:07:42 and 20:20:19 UTC: nine succeeded, seven failed and four were
cancelled. This includes only latest attempts, so historical retry coverage is incomplete.

| Completed-sample evidence | Observed value |
| --- | --- |
| Job rows | 42: 20 fresh executions, one carried-forward success, 15 cancelled without a runner/steps, six skipped |
| Queue, fresh executions | n=20; median 618.5s, minimum 45s, maximum 2,370s |
| Ubuntu fresh executions | n=17; median queue 820s; median execution 98s |
| Windows fresh executions | n=3; median queue 125s; median execution 812s |
| Available fresh execution | 6,759s across the mixed-outcome sample |

This small selected cohort shows queue delay and cancellations before assignment. It is neither
an account-capacity measurement nor the successful benchmark cohort needed to promote tuning.
API edge cases were verified against
[cancelled run 37366708311](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37366708311)
(992s waiting with a synthetic start) and
[partial retry 37361146289](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37361146289/attempts/2)
(five copied successful jobs). Its attempt-1 `integration-gate` queued 759s and executed for 4s;
the required gate remains necessary even when its scheduling delay dominates its execution.

Configured scheduling explains demand, without establishing available capacity:

- Meridian CI can make four Ubuntu validation lanes and one reusable Ubuntu integration job
  runnable together. Secret Scan and two CodeQL language jobs can bring the ordinary CI/security
  graph to eight initial Ubuntu jobs, before path-filtered specialists. The two Meridian gates
  are additional dependent jobs.
- PR/ref concurrency groups with `cancel-in-progress: true` cancel superseded work within each
  group. They neither add runners nor cap repository/owner-wide demand. Legacy CI's main push,
  scheduled and manual invocations shared a group at the October 5 snapshot: a main push could
  cancel nightly/manual coverage. The October 15 event-specific groups address that configuration
  risk; the historical sample does not establish it as the cause of any recorded cancellation.
- IB runtime is the sole self-hosted Windows requirement and retains its protected paper
  environment. Production release serialization across tags and certification's non-interruption
  policy must be preserved. Cancellation disabled does not imply durable FIFO queuing of every
  pending request. Hosted `windows-11-arm` availability also needs administrator confirmation
  before signed release rehearsals.
- At the October 5 snapshot, benchmark `max-parallel: 2` applied within one dispatch, so repeated
  benchmark dispatches could overlap. Targeted Test and Roadmap Tools included `github.run_id`
  in their concurrency keys, allowing separate dispatches to overlap. The dispatch isolation
  below now bounds repeated work. In-runner .NET/Vitest parallelism is a different layer and
  does not resolve scheduler waits.

**Human governance follow-up:** inspect the unassigned IB job and effective owner-wide hosted
capacity first; review the two stale queued records; gather attempt-complete metrics by workflow,
event and runner label. Consider event-specific legacy coverage groups and repeated-dispatch
limits only as separate reviewed scheduling changes. This metrics change does not alter runner
configuration, workflow fan-out, concurrency, required checks, protections or release policy.

### Dispatch isolation and duplicate work

Legacy `CI` concurrency now separates events as well as PR/ref identity. A main push
cannot cancel nightly or manually requested coverage. PR, push and merge-group runs
still cancel superseded work within their own group; scheduled and manual runs finish
once started. Each manual CI coverage request also includes its run ID in the group,
so another manual request cannot replace it while pending.

`Targeted Test` and `Roadmap Tools (Manual)` compute a SHA-256 key from the exact ref,
commit and complete input selection before scheduling their expensive job. Identical
requests share one running slot and one pending slot; distinct filters, projects,
runners, modes, refs or commits remain independent. Hashing preserves case distinctions
despite GitHub's case-insensitive concurrency groups and bounds the group length.
Inputs enter through environment variables and JSON, never interpolated shell code.

The concurrency benchmark uses one repository-wide group so separate dispatches cannot
overlap and contaminate paired measurements. These three workflows use
`cancel-in-progress: false`: running work completes, but GitHub may replace an older
pending request with a newer request in the same concurrency group. For benchmarks,
that includes distinct subjects, refs and commits. This is not a durable FIFO queue. Operators
who need every request executed must wait for completion before submitting the next.
Production certification and release serialization retain their existing policies.

### Runner saturation relief — November 25, 2026 (ci-health-013)

On November 25 every one of 16 pushes to `main` was cancelled by the next merge before
`quality-gate` ran, so `main` had no completed Meridian CI run after November 24 12:16Z.
Hosted jobs waited 60–76 minutes for a runner while 23 open PRs created about 300 workflow
runs in 85 minutes, and the median wall-clock of a successful run grew from 29–43 to 160
minutes. Three changes reduce demand without changing any check identity:

- *Main is always verified.* Meridian CI no longer cancels in-progress `push` runs. Each merge's
  run completes; GitHub still replaces an older *pending* run in the group with the newest, so a
  burst of merges cannot queue without bound. PR and merge-group runs still cancel superseded work.
- *One fewer runner wait per run.* The `changes` job is gone. The reusable integration job
  classifies the change in-job when Meridian CI passes `skip-when-docs-only: true`, using the same
  detector as `verify-dotnet`; `integration-gate` requires the two decisions to agree, and
  `evidence-gate` accepts a missing evidence artifact only for that opted-in docs-only decision.
  The integration path drops from four serial runner waits to three. A docs-only PR now starts the
  integration job (and its PostgreSQL service container) before its steps skip.
- *Drafts skip high-volume workflows.* CodeQL, `Secret Scan`, Windows Desktop Build and Roadmap
  Source Docs skip draft PRs at job level and run on `ready_for_review`. This deliberately accepts
  what the step-level CodeQL skip below avoids for ready PRs: on a draft, the skipped matrix job
  reports an unexpanded name, so the required `Analyze <language>` contexts stay expected until the
  PR is marked ready. A draft cannot merge, so no merge can pass on a skipped scan. Meridian CI
  and Generated Repair continue to run on drafts.

Confirm with: a completed `quality-gate` for every merge to `main`, runner waits under five
minutes, and successful-run wall-clock back in the 30–40 minute range.

### Runner demand and hand-off cuts — November 26, 2026 (ci-health-013, -014, -019, -020, -021)

The November 25 relief did not clear the queue. Every queued job waited 20 minutes to 3 hours,
the two post-fix `main` runs took 7.2 h and 2.6 h for about 23 minutes of work, and 4 of 6 `main`
merges went unchecked. The working hypothesis (medium confidence) is that PR fan-out saturates
the account's concurrent-job limit: Generated Repair ran about 100 times in 9 hours and Meridian
CI ran 341 PR runs on November 25. Each dependent-job hand-off adds a full queue wait.

- *Generated Repair runs on request only (ci-health-013).* A PR update no longer starts it. It runs
  for a ready PR labelled `generated-repair`, or when a PR's Meridian CI run fails because
  `verify-docs` found stale generated files. The new `verify-docs` step
  `Flag stale generated files for Generated Repair` fails only in that case, and Generated Repair's
  `workflow_run` trigger reads it through the API. Drafts are skipped, extending #3781's draft skip.
  The job condition is evaluated without a runner. The publisher ignores skipped runs and reapplies
  the same rules (`generated_repair.relay_skip_reason`) before requesting a trusted dispatch, which
  previously happened for every PR update. The main-advance refresh also skips drafts.
- *One fewer integration hand-off (ci-health-013).* `integration-gate` verifies the fresh
  integration job's completion (`fresh-succeeded`, set by a final implicit-success step, since
  reusable-workflow outputs cannot expose a job result) and evidence outputs itself
  (`caller-verifies-evidence: true`), so
  `service-backed-integrations / require verified integration evidence` skips in Meridian CI.
  The integration path goes from three serial runner waits to two. No check is renamed.
- *`review_required` is neutral (ci-health-019).* About 35% of Generated Repair runs failed on the
  sandbox's own `review_required` status, and every failure re-queued work. The sandbox now
  reports it in the job summary and exits 0. The publisher records a `review-required` outcome and
  publishes nothing. The cause was confirmed: a branch created before #3778 (9293ac60) keeps
  reviewed hashes in `source-hash-manifest.json`, which the merger did not read. Such a branch
  that refreshed a module's hash also hit a modify/delete conflict outside the per-module
  directory. The merger now reads the legacy file as the same evidence and retires it when the
  incoming branch has the per-module layout. A combination that matches neither reviewed parent
  still requires review.
- *Nightly secret scan covers `main` only (ci-health-014).* Scheduled runs use
  `scan-commit-secrets.py` on the checked-out `main` tip
  (`--log-opts=--full-history -m <sha>`) instead of the action's all-refs scan. The fixture
  finding from an unmerged branch is addressed in its own PR (#3807).
- *No single-package Microsoft.Extensions/AspNetCore majors (ci-health-020).* Every NuGet
  Dependabot entry ignores `version-update:semver-major` for `Microsoft.Extensions.*` and
  `Microsoft.AspNetCore.*` until the planned .NET 11 move.
- *Testing-group update (ci-health-021).* #3795 bumps Moq 4.20.72 to 4.21.0. Moq 4.21 annotates
  `IInvocation.Arguments` as nullable, so one test's `Arguments[2].ToString()!` became CS8602 and
  the whole default roster failed to build (no test started). That line is now null-safe at the
  source. Runtime results for the Castle.Core 5.2 proxy change are tracked on the PR; the
  version is not pinned back.

Confirm with: `main` runs finishing in 30–40 minutes, job creation-to-start gaps under five
minutes, a `quality-gate` for every `main` merge, a Generated Repair failure rate near 0% with
`review_required` only in run summaries, a passing nightly `Secret Scan`, and no new
single-package Microsoft.Extensions 11 PRs.

### Critical-path restructuring — November 25, 2026

Job timings from pull-request run
[45483483086](https://github.com/rodoHasArrived/Meridian-main/actions/runs/45483483086)
(31 minutes to `quality-gate`) showed that the gate's length was scheduling, not work:
`verify-dotnet` (17 minutes) declared `needs: verify-browser` (13 minutes) only to obtain
the workstation bundle; its 11.75 minutes of tests ran as about 1,420 process-seconds
through two processes on one runner; and about 11 of the browser lane's 13 minutes were 84
sequential ten-second Vitest batches. The changes below follow from those observations;
none removes or narrows a validation.

- **`build-workstation`** builds and validates the bundle with
  `build/scripts/ensure-workstation-assets.py` and uploads it by immutable artifact ID. The
  .NET shards consume it and start without waiting for browser tests. The browser lane still
  builds, validates and retains its own bundle, and `quality-gate` still requires both lanes.
- **`dotnet-shards`** runs the unchanged roster on two runners, each a complete
  `scripts/ci.sh --lane verify-dotnet` restricted to a deterministic slice by
  `MERIDIAN_CI_DOTNET_SHARD`. Shard 1 owns formatting, ratchets and the web-host build.
- **`browser-shards`** runs the unchanged test files on two runners, each a complete
  `scripts/ci.sh --lane verify-browser` restricted by `MERIDIAN_CI_BROWSER_SHARD`. Shard 1
  owns lint, the strict typecheck and the production bundle.
- **`verify-dotnet` and `verify-browser`** keep their names, their evidence artifact names
  and their places in `quality-gate`. Each is now the verdict over its shards and fails
  unless they together ran the complete roster, or every test file, exactly once.
- **Pipeline Budget Benchmark** cancels the superseded run when a pull request receives a
  newer push. Main pushes and manual dispatches still run to completion.

This is runner fan-out, not the process-count or batch-size promotion governed below: every
.NET runner keeps two test processes, Vitest keeps two workers and eight files per batch,
and `tests/xunit.runner.json` is unchanged.

**Shard counts are deliberately two.** A lane cannot finish before its longest test process
(about 360 seconds for `core-ui-workstation-endpoints-map`), and two runners with two
processes each already reach that floor; a third .NET runner would repeat the restore and
build for no gain. Each additional shard repeats setup (about four minutes of restore and
build for .NET, under a minute of install for the browser lane).

**Fan-out helps only when runners are available.** While this change was prepared, the
account had about 120 queued and 16 running workflow runs across roughly twenty open pull
requests, and this change's own jobs waited in the queue. Under that saturation, run latency
is set by total runner-seconds demanded, which this change raises slightly, not by any one
run's critical path. A run that needs .NET now occupies up to five runners at once in these
lanes instead of two, plus `build-workstation` and two short verdict jobs. If queueing
dominates again, set a lane's `shard` matrix, its `K/N` suffix, its outputs and
`--expected-shards` back to one together; the workflow contract tests keep them consistent.
Rollback is reverting this change. The expected gate time is an estimate from one run until
post-merge runs are measured.

**Integration lane.** `service-backed-integrations` took 19 minutes in the same run: a
five-minute build and one 11.5-minute `Meridian.Tests` step. Its TRX showed 1,290
test-seconds, of which one class, `Meridian.Tests.Storage.AtomicTaxLotJournalStoreTests`
(196 tests), took 445; xUnit runs a class serially, so that class alone was two thirds of
the step's wall time. Two changes follow.

- The suite now runs as two complementary processes declared once in
  `integration_evidence.MERIDIAN_PROCESSES`: that class against a second database,
  `meridian_tax_lot`, and every other test against `meridian`. The project is built once;
  `run-meridian-integrations.py` gives the second process a copy of the built output, because
  the coverage collector instruments assemblies in place, plus a private temporary directory
  and rewritten connection strings. The receipt requires TRX and coverage from both
  processes, rejects a test that ran in the wrong process, and requires schema evidence for
  both databases. Expected test time is about 7.5 minutes (estimate; the class remains the
  floor), for a job of about 15.
- Documentation-only pull requests skip the job through the existing change classification.

The schema evidence deserves a separate note. In the measured run the retained
`database-table-inventory.csv` and `migration-ledger-inventory.csv` held only their header
rows and `database-schema.sql` contained no `CREATE TABLE`: the tests create and remove
their own schemas, so the dump records an empty database. Splitting the suite across two
databases therefore changes the file set, not what the evidence proves. Whether
certification should instead dump a freshly migrated database is an open owner decision
that this change does not make.

**Shard counts set to one — November 25, 2026.** After the change merged, none of the 40
most recent Meridian CI runs (10:38 to 13:11 UTC) had completed: every run was cancelled,
queued or pending, and the merge commit's own run was superseded before it started. With
no free runner, a second shard buys no wall-clock time and repeats setup, so both lanes now
run one shard. `build-workstation`, the verdict jobs, the two-process integration suite and
the documentation-only skip are unchanged, and `quality-gate` no longer waits for browser
tests before starting .NET. Restore two shards per lane, as described above, once runs start
within a minute or two of being created.

Two causes were visible and neither is addressed here. Pushes outpaced the queue: `main`
received five pushes in about thirty minutes, each cancelling the previous run. And a
cancelled run does not release its concurrency group until its `if: always()` gate jobs
have themselves obtained a runner, so each supersession costs a full queue wait before the
next run for that branch can start. The verdict jobs added on November 25 are two more such
jobs per run. Skipping gates on cancellation is not a safe fix, because GitHub treats a
skipped required check as passing.

### Benchmark promotion criteria

Hosted defaults remain two .NET test processes and eight browser files per batch; local .NET
remains sequential. Compare 2/4 processes and 8/16 files using five distinct paired runs on
identical commits, runners and selected test identities. Record passed/skipped/failed counts,
test durations and hang/OOM evidence. Evaluate with `--paired-benchmark`; adoption requires
at least 15% median paired improvement and no coverage or reliability regression. Promotion
is a separate reviewed change, never an automatic reaction to timing noise.

Dispatch **CI Concurrency Benchmark** at a fixed commit with `subject=dotnet`, `browser`, or
`both`. Five pairs alternate execution order on the same hosted runner within each pair.
Decision artifacts include discovered-test digests, counts and test-only wall time. Failed or
incomplete samples reject promotion. Vitest retains two workers and process recycling.

The .NET candidate depends on roster shape as well as process count. A lane cannot finish
before its longest shard, and `WorkstationEndpointsTests` ran as one serial process for about
800 of the 867 test seconds in main run 43964213788, so four processes could not have met the
15% rule there. The class now runs as three disjoint process shards (see the
[workflow README](../../.github/workflows/README.md)); the hosted default stays at two until a
paired benchmark on a commit that contains the split is reviewed.

For local diagnostics, `benchmark-ci.py --local` records the actual checkout, local runner
identity, worktree state and fresh per-variant evidence. Local measurements cannot authorize
hosted-default promotion. Keep local failures and missing discovery/timing in the decision
artifacts; never substitute synthetic Actions IDs or call the local machine `ubuntu-latest`.

The workflow lane requires actionlint 1.7.12 on PATH and Python dependencies from
`build/scripts/ci/requirements.txt`. Hosted installation verifies the actionlint archive digest.
External actions use verified full commit SHAs with version comments; Dependabot maintains
the pins. CodeQL keeps manual C# extraction and its measured cold-restore policy.
SVGs, the dashboard HTML input and generated workstation text assets use LF checkout line
endings so embedded bytes, bundle hashes and freshness checks match on Windows and Linux.

Required .NET/Windows slices need fresh TRX evidence and nonzero passing discovery. Browser
batches need fresh JSON evidence for every selected file. Existing summary fields are retained,
with added counts, identities, durations, attempts and cache status. Missing cache and queue
information is explicitly unavailable, never reported as a hit or zero.

## Script-test quarantine

`build/scripts/ci/script-test-quarantine.json` remains the tracked defect register, with
no excluded modules after the October 15 restoration. All seven former modules execute
in the ordinary script lane. Expired, missing or untracked entries still fail the lane.

- Source README validation already passed; its stale exclusion is removed.
- Desktop checklist validation points to an active twenty-screen reference rather than
  an archive redirect, with negative fixtures for missing headings and redirect stubs.
- Manual-override route checks read the canonical provider capability reference.
- Archive tombstone checks exercise valid and executable-code fixtures even when the
  historical archive tree is absent; the build-graph exclusion remains checked.
- Screenshot dependency checks follow the maintained CI lane and pinned Pillow install.
- Contract compatibility fixtures use isolated temporary repository roots, including
  invocation outside the checkout, without changing the production gate.
- Export checks enforce the maintained commit-bound publish outputs and reject revival
  of the retired generic repository-export workflow.

Pillow is now explicit. Screenshot diff and screenshot capture validation suites run in the
ordinary script lane. New exclusions require an owner, reason, deadline, tracked defect and
human governance review; a test failure cannot add an exclusion automatically.

After rollout, require at least twenty comparable completed successful change observations in
each evaluated event cohort, with an equally sized matched baseline. Count a change once across
its relevant workflows; retries do not increase the sample count. Targets remain 25% lower
median quality-gate execution and 30% fewer aggregate runner minutes across those matched
observations, including their earlier attempts. These targets are separate from the 15%
five-pair concurrency adoption rule. Insufficient, incomplete or unmatched samples produce an
unevaluated target, never a savings claim. Failed and cancelled final outcomes remain separately
visible and cannot enter successful performance cohorts.

## Unimplemented-surface register

`build/scripts/ci/check-unimplemented-surfaces.py` runs in the workflows lane next to the test
skip register. A route or task that refuses honestly because nothing implements it is an
unimplemented surface. That covers `ApiProblemDetails.NotImplemented(...)`, a
`BuildNotImplementedResult(...)` maintenance task, or a 501 whose title or detail says "not
implemented". The source file of each one must be listed as `evidence.path` on a roadmap row in
`docs/roadmap/data/roadmap-items.yml`. Otherwise it needs an entry with a reason in
`build/scripts/ci/unimplemented-surface-baseline.json`.

A 501 returned only because an optional service is not registered is a host configuration
refusal and is out of scope. Baseline entries that stop matching, or that a roadmap row now
covers, fail the gate. Removing an entry from the baseline needs a roadmap row first.

## Review sequence and rollback

The initial consolidation was organized into baseline/ownership, duplicate-work removal,
scheduling/Windows reuse, quality controls, and release orchestration stages. The generated-repair
follow-up adds isolated repair, reviewed-hash reconciliation, split hosted generation/publication,
coordinated maintenance and release readiness. Review the final implementation and validation
evidence together; workflow changes remain designated for human governance review.
Never bypass a failed check. Revert the responsible optimization if selected test identities,
reliability or released package hashes regress; keep the previous evidence until equivalence
has been demonstrated.

## Administrator rollout

After checks are green, a repository administrator inspects all existing rules and protection
settings and adds required GitHub Actions contexts `quality-gate`, `Secret Scan`, `Analyze csharp`
and `Analyze javascript-typescript`. Preserve other protections. Do not require a path-filtered
specialist check that may never report. Keep human governance review without introducing a
blanket non-author approval rule. These instructions do not change repository settings.
Bind each context to the GitHub Actions application's ID from its successful check-run record,
then verify PR and merge-group events report all four contexts. Inspect classic branch protection
as an administrator as well as rulesets; the planning account received HTTP 403 for classic
protection, so readable rulesets are not a complete inventory of existing requirements.

### Generated repair and maintenance activation

The maintained producer catalog covers the workstation bundle (a CI artifact, not committed),
generated UI contracts, source-derived schema-control contract/dependency manifests and database
documentation, roadmap views, structured production readiness, and program-state summaries. PR authors edit source and
registry fields; the configured repair bot batches deterministic output into one commit. The
schema-control physical catalog and migration/policy snapshots still use their existing review
and promotion workflow. Reviewed source-module hashes remain attestations, not a mechanical
freshness timestamp. See [repair ownership and exceptions](generated-merge-recovery.md#hosted-pr-repair).

Catalog and output-policy changes need human governance review before adoption. This change does
not modify required checks, repository rules, bot credentials, or environment restrictions. A PR
that changes a generator or its policy uses the contributor path until those changes are reviewed
and available on the default branch. Same-commit drift checks still reject stale output.

The local [repair command](generated-merge-recovery.md) works without a GitHub App and never writes
to a remote. Hosted automatic writes require the reviewed workflows to be on the default branch
and the following configuration:

| Setting | Purpose |
| --- | --- |
| Repository variable `GENERATED_REPAIR_APP_ID` | Nonsecret ID of the GitHub App installed only on the intended repository; visible to the read-only generator as a dispatch capability signal. |
| Environment `generated-repair` | Restrict allowed deployment branches to the default branch (`main`); both trusted publishers use this environment. |
| Environment secret `GENERATED_REPAIR_APP_PRIVATE_KEY` | App private key stored only in `generated-repair`, so unreviewed branch workflows cannot access it. |
| App repository permissions | Contents: write and Pull requests: write for repair/maintenance commits and maintenance PRs; Actions: write for bounded repair re-dispatch. |

PR repair itself needs only Pull requests: read; the coordinated maintenance publisher additionally
creates or updates its maintenance PR. Restrict installation and minted tokens to this repository.
Do not store the private key as a repository-wide secret. Per-run environment approval is optional
administrator policy; the default-branch deployment restriction is required.
Do not use a default `GITHUB_TOKEN` push as a fallback: its commits do not trigger the fresh checks
required on repaired revisions. No App permission allows a workflow to bypass branch protections.

`generated-repair.yml` accepts read-only requests from ready (non-draft) PRs labelled
`generated-repair` on open, synchronize, reopen, ready-for-review and labelling, including
conflicted PRs, and from completed Meridian CI PR runs whose `verify-docs` flagged stale generated
files. With the repository App variable configured, an eligible same-repository request event
requests generation dispatched on the default branch. Forks and unconfigured requests
still produce artifact-only candidates. The publisher never publishes a target-event candidate.
Only a default-branch dispatch whose server-owned execution SHA/repository/ref match the version 2
run metadata and trusted workflow bytes can authorize publication. PR head/base inputs remain
separate from that execution identity; the target event's REST `head_sha` is not workflow-origin
proof. Orchestration checkout pins `github.workflow_sha`.

Candidate dependencies and generators run in a restricted container with only a disposable
clone and raw output mounts, excluding host Actions/cache credentials and the Docker socket. The
trusted host exports only bounded regular files without links after the container exits.
`generated-repair-publish.yml` validates the completed run and artifact using trusted base tooling,
recreates the pinned merge, independently derives the generated delta and rereads head/base before a
normal branch push. App writes require these checks; the initial trusted-generation dispatch
does not consume a stale-ref retry. A post-push base reread catches a base
advance in the final write window and requeues within the retry bound; GitHub offers no atomic
comparison-and-update transaction across both refs. Stale revisions allow at most two automatic
requeues. Repair commit markers suppress self-triggered loops only for an already-current base;
later base advances can repair the branch again. Publisher concurrency is scoped to each source
run so an unrelated PR cannot evict pending repair work from a global group.

On PR merges into `main`, main pushes, daily at 05:23 UTC and on manual dispatch, the publisher
scans all open same-repository PRs for a stale base, including drafts and conflicted PRs. Unmerged
closures are excluded. Each scan reads the latest base, follows API pagination and dispatches every
eligible head/base pair, pacing writes one second apart instead of leaving a daily backlog.
Overlapping refresh jobs are serialized; pending scans can coalesce because each scans the latest
base and all open PRs. Candidate publication retains independent per-source-run concurrency.
Only active or successful trusted default-branch generation requests suppress duplicates; failed
or cancelled requests remain eligible for a later scan. API failures are explicit. This discovery
is separate from each repair's two stale-ref retries.

Without App configuration, PR repair provides artifacts only. Forks, source/handwritten conflicts,
uncertain reviewed module hashes, and changes to generators/dependencies/automation policy retain
the local contributor path. A valid candidate includes `repair.patch`; a refusal includes its
reason in `report.json`. Changes to the reviewed source-hash baseline are not blindly recomputed.

The weekly/manual AI Navigation Refresh coordinates navigation, recent changes, repository
structure, workflow overview and documentation health output. Its trusted publisher maintains
`automation/ai-navigation-refresh` through ordinary non-force updates and an App-created PR.
Generation and publishing remain separate. The 14-day navigation freshness requirement still
applies; missing App configuration leaves downloadable evidence and an explicit publication
failure rather than a maintenance PR whose required checks never run. Handwritten edits or stale
branch/base evidence refuse publication. A branch/base race allows one automatic fresh-base
maintenance re-dispatch (`retry_count` 0 then 1); further races require inspection instead of a loop.

Before enabling writes, exercise an eligible same-repository PR, a fork fallback, changes to the
head and base during generation, and unauthorized file/prose changes. Check that the first repaired
revision receives fresh required checks. Keep freshness failing for stale synthetic merge groups;
refresh constituent branches and rebuild the group. Administrators may evaluate one-PR groups if
combined output repeatedly conflicts, but no workflow changes queue policy or bypasses its checks.

Report automatic success, manual-review reasons, stale retries and non-convergence separately from
queue delay and execution cost. The main repair measure is the fraction of first repaired
revisions passing authoritative CI without manual regeneration, with unchanged validation coverage.

## Release evidence

Release promotion retains a digest-bound upstream evidence bundle alongside the certified
packages. Tag publication stages a draft, verifies the exact asset inventory and downloaded bytes,
and verifies package attestations before making the release visible. Existing conflicting bytes
are refused rather than overwritten. Failed publication retries must reuse the original validated
artifact and certification attempt; a publication attempt is recorded separately. Manual and
scheduled signed rehearsals cannot publish production releases.

Rerun the failed tag-publication job to restore its original same-run `validated-release` archive.
Missing or expired certification evidence stops recovery. An empty GitHub `starter` asset from an
interrupted upload also stops publication with its asset ID: a release owner must inspect and
remove that incomplete upload before retrying. Automation never deletes or replaces a release
asset. The retained bundle includes upstream CI/security/certification archives and package
attestations, so the published release remains auditable after Actions retention expires.

`release-readiness.yml` runs Monday at 08:23 UTC and on manual dispatch. It checks signing
certificate metadata through the existing protected `desktop-release-signing` environment and
observes the last 14 days of successful, assigned native x64/ARM64 lifecycle and consumer
packaging jobs through the read-only Actions API. Historical runner assignment is evidence of a
recent capability, not a reservation or proof of current spare capacity. Missing, stale or
inaccessible evidence is `unknown`, not a success.

Set `MDC_SIGNING_CERT_WARNING_DAYS` to a value from 1 to 365 to change the default 30-day renewal
warning. Invalid, expired or missing signing evidence blocks readiness; an approaching-expiry
warning remains visible without failing otherwise valid prerequisites. No private key or password
is retained in the report. Existing signing environment restrictions and approvals still apply.

The release coordinator also runs `release-readiness-summary` after its dependencies finish,
including failures and skips. It identifies each missing gate or receipt and the next action,
binding the summary to the exact commit, workflow run and attempt. Both workflows retain
`release-readiness-<run>-<attempt>` JSON/Markdown and an Actions summary for 90 days. A summary is
diagnostic evidence, not a replacement for the existing promotion and package-digest checks.

The release coordinator must bind CI, security, deterministic production certification and
web-workstation/win-x64 installed-startup proof to the tag's exact commit. Publication depends
on those checks plus packaging and native x64/ARM64 lifecycle evidence. Packages are promoted
without rebuilding; family/runtime-qualified evidence names prevent release asset collisions.
Evaluation prereleases remain separate and do not establish production certification.

The coordinator is `desktop-installer-packaging.yml`. Same-repository reusable workflow
references and explicit SHA checkouts bind the called implementation and validation to the
coordinator commit. Production certification retains weekly, manual and reusable release entry points (the push-to-`main` trigger was removed under the solo-developer cadence change; see [consolidation](solo-developer-ci-consolidation.md));
the coordinator replaces its independent tag invocation. Signing and release publication
permissions stay in the release jobs; validation workflows receive no signing secrets.

The early eligibility job requires the tag commit to be an ancestor of main, validates the PFX,
password, private key, publisher and validity period, and compares MSIX identity versions from
published production packages. Production prereleases count in this comparison. `v1.2.3-rc.1`
and `v1.2.3` both map to `1.2.3.0`, so the second is rejected before compilation; use a higher
package version. `eval-v*` artifacts remain in their separate evaluation channel.
One release concurrency group serializes eligibility through publication across tags so two
concurrent candidates cannot both pass the version check against the same older release.

For a rehearsal, manually dispatch the coordinator on the reviewed branch with a valid
`rehearsal_version` greater than existing package versions. It requires the existing protected
`desktop-release-signing` environment and signing secrets, runs every release dependency,
and retains `validated-release-<run>-<attempt>` without publishing. The old unsigned manual
lifecycle shortcut is replaced by this full signed rehearsal; the standalone evaluation channel
still provides self-signed evaluation packages.

Weekly signed rehearsals run Sunday at 04:17 UTC only when repository variable
`MDC_ENABLE_RELEASE_REHEARSALS` is exactly `true`. Set `MDC_RELEASE_REHEARSAL_VERSION` to an
eligible SemVer whose MSIX identity is greater than every published production package. The
scheduled event pins the default branch's event SHA and executes the same signing, install,
upgrade, repair, rollback and digest-bound validation graph. Scheduling never publishes a release;
publication remains restricted to eligible `v*` tag pushes. Scheduled runs use the separate
`desktop-installer-rehearsal` concurrency group, including when opt-in is disabled, so they cannot
evict a pending production release. Production tags and manual rehearsals retain
`desktop-installer-release`; production publication remains serialized. Enable the schedule after
human governance review and a successful manual rehearsal, with the existing signing environment
available.

Publication verifies every gate, both native MSIX architecture receipts, the consumer EXE receipt, installed-startup evidence,
source commits, run IDs, run attempts and SHA-256 digests, then copies the certified MSIX files
and certified consumer package into a fresh flat directory. It performs no builds or signing.
Each package family/runtime has its own SBOM, checksum file and release manifest. The gate
manifest links validation results and each package's lifecycle receipt to the exact promoted bytes.
`certify-installed-consumer` downloads `Meridian-Setup.exe` from the current run attempt onto a
fresh Windows x64 runner. It validates its production Authenticode publisher and payload, installs
that EXE, exercises authenticated startup and the installed bundled PostgreSQL process, damages
and repairs the installed payload, restarts, uninstalls, and verifies preserved configuration,
file data and database state. Existing PostgreSQL installations on the runner cannot supply the
bundled-database proof.

`consumer-setup-win-x64-lifecycle.json` must pass and match the consumer family, x64 runtime,
EXE SHA-256, source commit, workflow run and attempt. Promotion rejects a missing, failed,
incomplete or mismatched consumer receipt even when both MSIX receipts and publish smoke pass.
MSIX-only lifecycle evidence cannot promote the EXE.

The consumer predecessor lookup includes published production `v*` releases, including signed
release candidates, and excludes the evaluation channel. It downloads the previous consumer
EXE and verifies its release checksum before exercising upgrade and rollback. If no published
consumer predecessor exists (including a history containing only MSIX releases), it records an
explicit first-consumer-release exception for each N-1 leg and retains the repository lookup
receipt. Failed queries, downloads or checksum verification remain fatal. A configured prior tag
must identify an eligible published consumer release; it cannot force a first-release exception.

The consumer certification workflow and promotion dependency changes are designated for human
governance review. Human approval of the workflow changes precedes signed rehearsal activation.

The administrator should dispatch a signed rehearsal after human review and before enabling
publication for a new production tag. A successful rehearsal is required operational evidence;
static workflow tests cannot establish certificate availability or native installation success.

The pinned Gitleaks action does not support merge-group events and may skip tag pushes with
empty commit arrays. Those events use the same Gitleaks 8.25.1 scanner and repository config
through `scan-commit-secrets.py`, with a verified download checksum. It verifies the checkout
SHA and scans complete reachable history, including merge-resolution changes. Fresh SARIF
evidence is mandatory; unsupported events and empty webhook arrays cannot produce a false pass.

## Runner-queue findings and docs-only CodeQL skip (2026-11-25)

**Evidence.** The 60 most recent completed Meridian CI runs at the time of review were 52
cancelled, 6 successful and 2 failed. The "has job failures" percentage in the Actions usage
export counts cancellations, so its 55% figure for Meridian CI is mostly superseded pushes, not
red builds. A representative successful PR run (45612764201) took about 95 minutes wall time for
about 50 minutes of job execution: each job waited 8–32 minutes for a hosted runner, and the
one-second `quality-gate` waited 17 minutes. Latency is dominated by runner queueing caused by
per-push fan-out across workflows, not by test duration.

**Changes.**

- *CodeQL per-language skip.* Pull requests are classified per language by
  `detect-codeql-languages.py`, which builds on the Meridian CI docs-only rule. Documentation
  never requires analysis; dashboard-only changes skip C#; .NET source and project files skip
  JavaScript/TypeScript. Unknown paths, workflows, scripts, Razor/cshtml and empty diffs analyze
  both. The required `Analyze <language>` checks still run and report success; main pushes,
  merge groups, schedules, manual runs and the reusable release gate always analyze, so release
  SARIF evidence is unchanged. The skip is step-level on purpose: a skipped matrix job reports an
  unexpanded check name and would leave the required contexts pending.
- *Meridian CI lane jobs.* `build-workstation`, `dotnet-shard (1/1)` and `browser-shard (1/1)`
  were folded into `verify-dotnet` and `verify-browser`. Each lane job runs the lane and the same
  roster verdict (`--expected-shards 1`) over its own summary and publishes the same evidence
  names. `verify-dotnet` classifies the PR in-job instead of waiting for `changes`; on docs-only
  PRs it reports success with its .NET steps skipped, and `quality-gate` fails unless its decision
  matches `changes`. The .NET path to `quality-gate` went from five queued jobs to two; check
  identities `verify-dotnet`, `verify-browser` and `quality-gate` are unchanged.
- *Golden Path WPF.* `WPF W4 Acceptance` (Windows) now runs only on manual dispatch, and WPF-only
  paths no longer trigger Golden Path. Windows Desktop Build's `full` slice already runs every
  `Category=W4Acceptance` test. Its path filter was missing 13 projects in WPF's reference graph
  (including `Meridian.Ledger`, `Meridian.Risk`, `Meridian.Domain` and `Meridian.ProviderSdk`);
  they were added, and a test now walks the `ProjectReference` graph so the filter cannot drift.
- Roadmap Source Docs' phase-scope checkout uses a blobless full-history clone; the gate only
  compares changed path names.

**Still open (owner decision).** Larger or self-hosted runners raise the concurrency ceiling
directly. Re-introduce .NET sharding only when runner capacity, not queueing, is the limit.
