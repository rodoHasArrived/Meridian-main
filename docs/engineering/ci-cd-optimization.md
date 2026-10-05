# CI/CD validation ownership and rollout

**Status:** staged implementation; human governance review required  
**Owner:** core-team  
**Reviewed:** 2026-10-05

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

Meridian CI retains exactly its four canonical `scripts/ci.sh` lanes and the existing
`quality-gate` aggregation. Its additional `service-backed-integrations` job calls the same
`service-backed-integrations.yml` implementation used by Production Certification. The
always-reported `integration-gate` companion requires success and fails for failed, cancelled
or skipped integration execution. It runs without path filters on pull requests (including
forks), merge groups, main pushes and manual/reusable calls.

The shared implementation uses the certification `postgres:17` service, health checks and
eight domain connection strings. The database and credentials are disposable within each
job; no production secrets or privileged PR event are used. Checkout uses the event SHA with
credentials persistence disabled and `contents: read`. Both project restores and test runs
are attempted independently, so one failure cannot hide the other's result. TRX validation
runs after failures and rejects missing required suite prefixes, failed/skipped/unknown
outcomes and every file with zero discovered tests. Coverage and schema evidence retain the
existing certification artifact contract (90 days); CI companion evidence is retained 14 days.

Administrators may add `integration-gate` as a required companion check after human governance
review and a successful PR/merge-group rollout. This change does not alter repository rulesets
or replace the existing `quality-gate` requirement.

## Measurement and promotion

The [October 5 Actions storage audit](actions-storage-audit-2026-10-05.md) inventories workflow
uploads, measures recent artifacts, and separates routine retention savings from protected
certification/recovery evidence. Its snapshot is bounded and does not reconcile account billing.

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
| `queueSeconds` | Job creation to an evidenced start. Includes scheduler delay after job creation; not workflow trigger/dependency time or proof of runner saturation. |
| `waitSeconds`, `waitEnd` | Creation to an evidenced start, completion of a never-started job, or fixed observation for a still-queued job. `waitEnd` distinguishes `started`, `completed` and `observed`; these populations must not be treated as interchangeable queue samples. |
| `executionSeconds` | Valid start-to-completion time for fresh execution in this attempt. In-progress, never-started, invalid and carried-forward execution is null. |
| `startState`, `timingIssue` | Distinguish `started`, `not_started` and `unknown`, and preserve invalid-start or start-before-creation evidence. GitHub can synthesize `started_at` for jobs with no assigned runner and no executed steps. |
| `runnerSeconds` | Sum only when every job has measurable execution; otherwise null, including empty jobs. This is aggregate job time, not wall-clock latency or billed runner time. |
| `knownRunnerSeconds` | Sum of available fresh execution, or null when none is measurable. Partial evidence, never a complete-cost claim. |
| `summary` | Top-level and per-group sample counts/medians for queue, wait and execution; run-attempt/retry/cancellation counts; job, queued, never-started and unavailable-execution counts. Mixed-outcome descriptive distributions are separate from the successful performance cohort. |
| `cancelledRunKnownRunnerSeconds`, `retryKnownRunnerSeconds` | Available fresh execution in cancelled run attempts or attempts numbered above 1. These can overlap; neither is a complete waste total or an attribution of cancellation cause. |
| `retryHistory` | Observed and missing attempt numbers per run. `retryAttempts` counts exported retry attempts, not inferred missing attempts, retried steps or job retries. |

Rows retain job IDs, raw timestamps, status, conclusion, runner identity and requested labels.
Legacy timestamp-only exports remain readable but cannot distinguish synthetic starts; use the
original runner/step metadata before drawing availability or waste conclusions.
Partial reruns can copy successful results under **new job IDs and the new attempt number**,
with their execution timestamps before their new creation timestamp. These rows remain visible
as `started_before_created` and contribute no timing to the new attempt. Missing, malformed,
timezone-naive or reversed timestamps remain unavailable; they are never clamped to zero.
Duplicate run/attempt rows and explicitly mismatched job attempts are rejected. The tool cannot
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
  scheduled and manual invocations share a group: a main push can cancel nightly/manual coverage.
  This is a configuration risk, not an established cause for the sampled cancellations.
- IB runtime is the sole self-hosted Windows requirement and retains its protected paper
  environment. Production release serialization across tags and certification's non-interruption
  policy must be preserved. Cancellation disabled does not imply durable FIFO queuing of every
  pending request. Hosted `windows-11-arm` availability also needs administrator confirmation
  before signed release rehearsals.
- Benchmark `max-parallel: 2` applies within one dispatch; repeated benchmark dispatches can
  overlap. Targeted Test and Roadmap Tools include `github.run_id` in their concurrency keys, so
  separate dispatches are not serialized. In-runner .NET/Vitest parallelism is a different layer
  and does not resolve scheduler waits.

**Human governance follow-up:** inspect the unassigned IB job and effective owner-wide hosted
capacity first; review the two stale queued records; gather attempt-complete metrics by workflow,
event and runner label. Consider event-specific legacy coverage groups and repeated-dispatch
limits only as separate reviewed scheduling changes. This metrics change does not alter runner
configuration, workflow fan-out, concurrency, required checks, protections or release policy.

### Benchmark promotion

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

`build/scripts/ci/script-test-quarantine.json` is the tracked defect register. Each remaining
module names `@rodoHasArrived` as owner and a review deadline of 2026-10-28. Expired, missing,
or untracked entries fail the lane; each run publishes the register in its summary.

- `test_validate_source_readmes`: path and valid-fixture expectations differ from the validator.
- `test_desktop_screen_blueprint_checklist`: screen coverage and summary expectations drifted.
- `test_live_execution_controls_route_consistency`: manual-override routes differ from contracts.
- `test_archive_code_tombstones`: the archive tree and tombstone expectations differ.
- `test_python_package_conda_dependencies`: the workflow no longer uses the expected conda file.
- `test_check_contract_compatibility_gate`: security-master reference contract fixtures differ.
- `test_export_project_artifact_workflow`: the expected workflow is absent.

Pillow is now explicit. Screenshot diff and screenshot capture validation suites run in the
ordinary script lane. New exclusions require an owner, reason, deadline, tracked defect and
human governance review; a test failure cannot add an exclusion automatically.

After rollout, compare at least twenty completed runs by event and attempt. Targets are 25%
lower median quality-gate execution and 30% fewer total runner minutes, not certified savings.
Retain cancelled runs for waste accounting but exclude them from successful performance cohorts.

## Review sequence and rollback

Five staged PRs target main: baseline/ownership, duplicate-work removal, scheduling/Windows
reuse, quality controls, and release orchestration. Later PRs include unmerged earlier stages.
Review the stage commit; merge in order and refresh later branches after each human merge.
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

## Release evidence

The release coordinator must bind CI, security, deterministic production certification and
web-workstation/win-x64 installed-startup proof to the tag's exact commit. Publication depends
on those checks plus packaging and native x64/ARM64 lifecycle evidence. Packages are promoted
without rebuilding; family/runtime-qualified evidence names prevent release asset collisions.
Evaluation prereleases remain separate and do not establish production certification.

The coordinator is `desktop-installer-packaging.yml`. Same-repository reusable workflow
references and explicit SHA checkouts bind the called implementation and validation to the
coordinator commit. Production certification retains main, weekly and manual entry points;
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
