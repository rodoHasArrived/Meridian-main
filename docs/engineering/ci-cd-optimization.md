# CI/CD validation ownership and rollout

**Status:** staged implementation; human governance review required  
**Owner:** core-team  
**Reviewed:** 2026-09-28

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

## Measurement and promotion

Export Actions run metadata with embedded `jobs` from the attempt-specific jobs endpoint.
`python build/scripts/ci/ci-metrics.py --input runs.json --output artifacts/ci-metrics.json`
separates job queue time, execution time, workflow/event and run attempt. Missing job timestamps
are unavailable evidence, not zero duration. Aggregate runner time is not wall-clock latency.

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

For a rehearsal, manually dispatch the coordinator on the reviewed branch with a valid
`rehearsal_version` greater than existing package versions. It requires the existing protected
`desktop-release-signing` environment and signing secrets, runs every release dependency,
and retains `validated-release-<run>-<attempt>` without publishing. The old unsigned manual
lifecycle shortcut is replaced by this full signed rehearsal; the standalone evaluation channel
still provides self-signed evaluation packages.

Publication verifies every gate, both native architecture receipts, installed-startup evidence,
source commits, run IDs, run attempts and SHA-256 digests, then copies the certified MSIX files
and verified consumer package into a fresh flat directory. It performs no builds or signing.
Each package family/runtime has its own SBOM, checksum file and release manifest. The gate
manifest links validation results and native lifecycle receipts to the exact promoted bytes.
Consumer setup retains its embedded-payload verification and the separate required
web-workstation installed-startup proof; native MSIX lifecycle receipts explicitly describe
only the desktop MSIX packages.

The administrator should dispatch a signed rehearsal after human review and before enabling
publication for a new production tag. A successful rehearsal is required operational evidence;
static workflow tests cannot establish certificate availability or native installation success.

The pinned Gitleaks action does not support merge-group events and may skip tag pushes with
empty commit arrays. Those events use the same Gitleaks 8.25.1 scanner and repository config
through `scan-commit-secrets.py`, with a verified download checksum. It verifies the checkout
SHA and scans complete reachable history, including merge-resolution changes. Fresh SARIF
evidence is mandatory; unsupported events and empty webhook arrays cannot produce a false pass.
