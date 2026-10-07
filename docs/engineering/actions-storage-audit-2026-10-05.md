# Actions artifact storage audit — 2026-10-05

Status: proposed workflow correction; human governance review required before merge.

## Scope and evidence

Source baseline: `600cde87be9de99c615594cb1a2b09f507cd2b2f` on `main`.
The [machine-readable snapshot](actions-storage-audit-2026-10-05.json) records all 44 direct
`actions/upload-artifact` steps across 23 workflow files, plus a bounded sample of 26 completed
runs and 63 non-expired artifacts observed on October 5. The sample totals **900,914,434 bytes
(859.18 MiB)**. It includes failed runs; their retained files do not establish successful
certification. A separate older Windows run was inspected afterward as a cleanup candidate.

This is not an account-wide billing reconciliation. The connector cannot list repository-wide
artifacts or read account billing. Run selection used first pages of recent successful, failed,
main-push and manual runs; it is neither random nor exhaustive. Per-run responses contained at
most five artifacts. Publish Smoke, installer/evaluation releases, nightly coverage, and several
manual workflows were not represented. Their sizes remain unmeasured. Third-party actions can
upload additional artifacts: sampled ordinary CI runs contain a Gitleaks SARIF artifact with
90-day expiry even though it is not a direct upload step in this repository.

Units below use MiB = 2^20 bytes and GiB = 2^30 bytes. GitHub calls the latter GB for billing.
An artifact's API `size_in_bytes` is the uploaded archive size, not an account billing total.
No artifacts, runs, budgets, repository settings, or release assets were deleted or changed by
this audit. Only the two workflow changes below are proposed.

## Ranked reductions

| Priority | Observed artifact | Measured size | Assessment and proposed action |
| --- | --- | --- | --- |
| 1 | Successful Windows Desktop Build smoke binaries | Four October 5 mainline packages: 189,522,708–189,683,955 bytes each; mean 180.82 MiB | Change retention 14 → 7 days when the job is successful at upload time. Preserve already-failed/cancelled-job retention at 14 days, publish checks, and the separate 14-day WPF test evidence. These routine binaries are not consumed by release promotion. |
| 2 | Duplicate desktop screenshot catalog | Run 35870133393: catalog 14,734,871 bytes; complete archive 30,278,005 bytes | Remove only the catalog-only upload. The complete archive already copies the catalog under `catalog/`, retains workflow captures under `workflow-runs/`, and adds a manifest. Keep it for 14 days, including failure paths. Saves 14.05 MiB per comparable capture, 32.7% of that run's uploaded bytes. |
| 3 | Web screenshot PNGs | Manual and PR samples: 24,716,580 and 24,749,049 bytes; manifests about 6.6 KB | A later 14 → 7 day change would save about 23.6 MiB × 7 days per capture. Defer until screenshot reviewers confirm the review window; keep manifests and failed captures. Current draft preserves 14 days. |
| 4 | Routine Meridian CI .NET, browser and integration evidence | Three successful runs: roughly 5.78 MB .NET + 1.57 MB browser + 1.69 MB integrations per run; docs/workflow summaries about 27 KB | A 14 → 7 day policy could save about 60 MiB of steady storage per daily successful run. Preserve for now: TRX, coverage and build logs diagnose active cross-branch failures. Browser bundles may be redundant with tracked generated output, but failed freshness checks can produce useful differing bytes. Measure archive contents/compression before excluding paths. |
| 5 | Schema, WPF validation, acceptance and smoke receipts | Schema 2.26–2.50 MB; current WPF validation about 0.739 MB; combined Golden Path evidence about 17 KB; Demo Smoke about 7 KB | Much smaller than routine binary packages. Keep the existing 14/30-day windows; these provide diagnostic or acceptance evidence. |
| 6 | Default-retention uploads and Gitleaks | Two sampled Gitleaks archives are 6,854 bytes each, expiring after 90 days; benchmark/roadmap defaults unmeasured | Explicit retention is worth a separate policy review but is not the dominant measured saving. Keep security evidence and benchmark decisions pending consumer review. |

The four Windows smoke archives total **758,413,152 bytes (723.28 MiB)**, or **84.2%** of the
bounded sample. Runs: [37351971803](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37351971803),
[37346186937](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37346186937),
[37336411334](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37336411334), and
[37332730385](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37332730385).
The duplicate screenshot evidence is in
[35870133393](https://github.com/rodoHasArrived/Meridian-main/actions/runs/35870133393).

### Retention scenarios, not a usage forecast

For an average archive size S and a stable r successful uploads per day, steady retained storage
is approximately `S × r × retention days`. At the observed 180.82 MiB average:

| Successful Windows smoke uploads/day | Current 14 days | Proposed 7 days | Reduction |
| --- | --- | --- | --- |
| 1 | 2.47 GiB | 1.24 GiB | 1.24 GiB (50%) |
| 5 | 12.36 GiB | 6.18 GiB | 6.18 GiB (50%) |

Each comparable future success avoids about **29.67 GiB-hours** over its retention lifetime.
This is storage exposure, not verified billable usage or a promised dollar saving. Failures known at upload time retain 14 days and reduce the aggregate percentage saving.
A later post-job failure cannot retroactively extend an artifact already uploaded for seven days. A seven-day window alone cannot fit
one such daily upload inside a 0.5 GB allowance. If that cap is a firm objective, a subsequent
review must consider keeping compact smoke receipts for every run and full binary downloads
only for failures or explicitly requested diagnostic builds, after identifying all consumers.
No tests or publish checks need to be removed to make that future decision.

Retention changes apply to new artifacts; they do not shorten existing expiration dates.
Deleting retained files stops future storage accrual but does not erase already accrued usage.
The November 1 billing-cycle reset does not itself delete retained files. Actions caches have
a separate allowance from artifact/Packages storage. The account's billing view is required to
attribute the notice, including other repositories and Packages; this sample cannot do so.

### Existing cleanup candidates, not deletion approval

Two individually identified files merit owner review if immediate space is required:

| Artifact ID | Candidate | Bytes | Existing expiry | Required check before deletion |
| --- | --- | --- | --- | --- |
| 10755857555 | `desktop-screenshots-62`, run 35870133393 | 14,734,871 | October 7 | Confirm the still-retained complete archive 10755592720 is downloadable and contains the catalog; external links to the old artifact will stop working. |
| 10987075441 | `windows-desktop-smoke-4413`, successful main run 36456258519 | 187,805,797 | October 12 | This September 28 upload is older than seven days. Confirm no investigation or reproduction needs its exact binaries. Preserve WPF evidence 10986666700 and all certification/release evidence. |

Together these candidates are **202,540,668 bytes (193.16 MiB)**. They were not deleted. The
older run is [36456258519](https://github.com/rodoHasArrived/Meridian-main/actions/runs/36456258519).
Avoid deleting entire workflow runs: doing so can remove unrelated evidence and useful run history.

## Evidence that retains its current window

- **Production Certification, 90 days:** deterministic integrations/TRX/Cobertura/skip and
  schema inventories; dependency reports; documentation evidence; encrypted recovery backup,
  authenticated manifest, checkpoint and receipt. The
  [recovery runbook](../operators/failover-and-recovery.md#recovery-drill-and-objectives)
  expressly requires the 90-day recovery package. Four sampled certification runs total only
  about 1.7 MB per run; their recovery artifacts are only about 4–6 KB each. Shortening these
  governed receipts is a poor trade against the 190 MB routine binary target.
- **Publish Smoke, 90 days:** this is both manual and a reusable release gate. The published
  bytes, authenticated installed-startup receipt/probes and release manifest are used to bind
  promotion to the exact commit. Its size is unmeasured here. Do not conflate this workflow
  with `windows-desktop-smoke-*`, or reduce manual invocations indiscriminately: a manual run
  may be required release evidence.
- **Installer lifecycle and validated release/rehearsal evidence, 90 days:** retain MSIX and
  consumer EXE lifecycle receipts, predecessor evidence, exact package hashes and promotion
  evidence. Preserve packaged installers/consumer setup at their existing 14 days and
  GitHub Release assets as the separate published channel.
- **Evaluation artifacts, 30 days:** not production certification, but contain distributable
  packages that evaluators may still need. Inspect size and active usage before shortening.
- **Pilot/provider acceptance, 30 days:** small or unmeasured, and tied to human acceptance.
  Do not trade away these review windows to save a few KB.

## Complete direct-upload workflow inventory at the baseline

The snapshot includes step names, conditions, paths, artifact names, compression settings and
retention expressions. The table groups outputs by workflow; a missing explicit value means
the repository default applies, not that retention was verified as 90 days.

| Workflow file | Direct upload steps | Retention at baseline | Role |
| --- | --- | --- | --- |
| `ci-concurrency-benchmark.yml` | 2 | default | Benchmark samples and decisions |
| `ci.yml` | 2 | 14 / 30 | Commit secret scan / nightly coverage; Gitleaks also uploads internally |
| `demo-smoke.yml` | 1 | 30 | Demo test evidence |
| `desktop-evaluation-prerelease.yml` | 3 | 14 / 30 | Diagnostics / evaluation packages |
| `desktop-installer-packaging.yml` | 7 | 14 / 90 | Preflight/packages / lifecycle and promotion evidence |
| `desktop-screenshot-capture.yml` | 2 | 14 | Catalog and redundant complete set; draft removes catalog-only copy |
| `desktop-standalone-publish.yml` | 1 | 14 | Standalone download |
| `desktop-user-manual.yml` | 1 | 14 | Manual plus screenshots |
| `desktop-workflow-runner.yml` | 1 | 14 | Desktop workflow diagnostics |
| `golden-path-validation.yml` | 2 | 30 | WPF and pilot acceptance |
| `meridian-ci.yml` | 4 | 14 | .NET/browser/docs/workflow evidence |
| `production-certification.yml` | 3 | 90 | Dependency/recovery/docs; integration upload delegated below |
| `provider-validation.yml` | 1 | 30 | Provider review and operator sign-off packet |
| `publish-smoke.yml` | 1 | 90 | Published artifact and installed-startup release evidence |
| `roadmap-tools-manual.yml` | 1 | default | Normalized roadmap output |
| `robinhood-options-smoke.yml` | 1 | 14 | Provider smoke evidence |
| `schema-control.yml` | 1 | 14 | Schema policy, manifests and diffs |
| `service-backed-integrations.yml` | 1 | caller input: CI 14 / certification 90 | Shared deterministic integration evidence |
| `targeted-test.yml` | 1 | 14 | Targeted diagnostics and optional desktop smoke output |
| `web-screenshot-capture.yml` | 4 | 14 | Manual/PR PNGs and manifests |
| `windows-desktop-build.yml` | 2 | 14 | WPF tests and routine smoke binaries |
| `wpf-dev-validation.yml` | 1 | 14 | Manual dev-loop evidence |
| `wpf-route-validation.yml` | 1 | 14 | Manual route evidence |

## Validation and governance

The correction changes only the routine Windows binary retention and removes one redundant
desktop screenshot upload. Focused workflow regressions verify publish/executable checks remain
mandatory, WPF results retain their full window, screenshot preparation still copies both sets
and the manifest, and failure-path uploads remain enabled. Existing certification and release
workflow tests are also run. Exact commands and hosted check results belong in the draft PR.

`.github/workflows/**` requires explicit human governance review under `AGENTS.md`. Keep the
PR draft; do not self-approve, merge, activate a release rehearsal, or alter billing budgets.
Rollback restores the duplicate upload and 14-day successful-smoke policy for future artifacts;
it cannot recover artifacts that have already expired.

Sources: [GitHub Actions billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions),
[retention policy](https://docs.github.com/en/organizations/managing-organization-settings/configuring-the-retention-period-for-github-actions-artifacts-and-logs-in-your-organization),
[removing workflow artifacts](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/remove-workflow-artifacts),
and the [upload action](https://github.com/actions/upload-artifact).
