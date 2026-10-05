# Build Observability System

This repository includes a build observability toolkit that turns local and CI builds into structured, diagnosable workflows.

For runtime OTLP collector setup and trace visualization, see [otlp-trace-visualization.md](otlp-trace-visualization.md).

## Quick Start

```bash
# Build with structured events + metrics
make build

# Build with isolated output for automation
python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Release --isolation-key automation-run --queue

# Reuse compatible outputs during a local edit/test loop
python3 build/python/cli/buildctl.py test --project tests/Meridian.Tests/Meridian.Tests.csproj --profile worktree --queue

# Obtain fresh isolated test evidence before handoff
python3 build/python/cli/buildctl.py test --project tests/Meridian.Tests/Meridian.Tests.csproj --fresh --queue

# Run environment doctor
make doctor

# Generate dependency graph
make build-graph

# Generate build fingerprint
make fingerprint

# Collect a debug bundle
make collect-debug
```

## CI Workflow

Use the GitHub Actions workflow to run the same observability toolkit in CI and upload artifacts for debugging:

Build observability is now local tooling rather than a dedicated GitHub Actions workflow. Use the
commands in this guide directly when diagnostics are needed.

The workflow executes:

- `make doctor`
- `make build`
- `make build-graph`
- `make fingerprint`
- `make metrics`
- `make collect-debug-minimal`

Artifacts are uploaded from `.build-system/` for each run.

## Output Artifacts

Artifacts are written to `.build-system/`:

- `build-events.jsonl` – machine-readable event stream
- `build-events.log` – human-readable event log
- `build-fingerprint.json` – deterministic fingerprint
- `dependency-graph.json` / `dependency-graph.dot` – dependency graph
- `metrics.json` / `metrics.prom` – build metrics
- `history.db` – build history database
- `logs/` – raw build logs

## CLI Commands

All commands are available via `make` or `python3 build/python/cli/buildctl.py`.

```bash
make doctor                  # Environment validation
make build                   # Build with observability
make build-profile           # Profile the last build
make build-graph             # Dependency graph
make collect-debug           # Debug bundle
make env-capture NAME=local  # Snapshot environment
make env-diff ENV1=local ENV2=ci  # Compare environments
make impact FILE=path/to/file.cs  # Impact analysis
make bisect GOOD=x BAD=y     # Automated build bisect
make metrics                 # Build metrics
make history                 # Build history summary
```

When `buildctl.py build` runs with `--isolation-key`, it writes generated MSBuild output under
`artifacts/bin/<key>/` and `artifacts/obj/<key>/` and prunes stale isolated output directories older
than 14 days before starting the build. It also trims excess same-day output beyond the latest 10
runs per artifact root, and prunes oldest generated runs when either root exceeds 4096 MB, so
repeated local automation does not fill the disk before age-based cleanup can run. Override the age
window with `--isolation-retention-days <days>`, the count guard with
`--isolation-retain-latest <count>`, and the size guard with
`--isolation-max-root-size-mb <mb>`, or set all three to `0` for a run that must skip cleanup.
Retention skips symlink, junction, and other reparse-point artifact roots or child directories before
recursive deletion. It also verifies resolved Python build roots remain inside the repository so a
count-based or size-based cleanup cannot follow a linked artifact route to an external target.
The reserved `profile-*` directories described below are excluded from automatic age, count, and
size retention in both `buildctl.py` and `scripts/dev/SharedBuild.ps1`; retention budgets cover
temporary isolated runs. Persistent profiles therefore need explicit cleanup when no longer used.

## Persistent Build Profiles

Use one profile for repeated builds and tests of the same project and build settings:

```bash
# Stable outputs for this checkout/worktree
python3 build/python/cli/buildctl.py build --project tests/Meridian.Tests/Meridian.Tests.csproj --profile worktree --queue
python3 build/python/cli/buildctl.py test --project tests/Meridian.Tests/Meridian.Tests.csproj --profile worktree --filter "FullyQualifiedName~<TestClassOrMethod>" --queue

# A separate named development session within this worktree
python3 build/python/cli/buildctl.py test --project tests/Meridian.Tests/Meridian.Tests.csproj --profile session:accounting --queue

# Fresh validation uses a new isolation key and never reuses a profile
python3 build/python/cli/buildctl.py test --project tests/Meridian.Tests/Meridian.Tests.csproj --fresh --queue
```

`--profile worktree` uses a stable key scoped to the checkout path. `--profile session:<name>` adds
a stable session name within that worktree; use different names for separate projects or build
settings. Both keep outputs in `artifacts/bin/profile-<hash>/` and
`artifacts/obj/profile-<hash>/`, backed by `.ai/build-profiles/profile-<hash>.json`. These files are
local generated state. The `profile-` isolation-key prefix is reserved for the profile runner.

Before reuse, the runner compares the selected .NET SDK, project and build definitions (including
framework declarations), configuration, target framework, runtime, and effective build properties
with the saved manifest. A mismatch fails with a diagnostic before restore/build/test; choose a
new session profile or explicitly reset the old one. Ordinary source edits do not invalidate the
profile: MSBuild decides what needs recompilation. Changing a test filter does not require another
profile. Build and test can share a profile when they use the same project and compatible settings.
Normal profile runs still execute restore and build incrementally. `--skip-restore` and
`--no-build` require a previously successful profile build and existing output roots. For tests,
`--no-build` also skips restore and runs the existing assemblies; use the normal profile command
after source edits. Use the dedicated configuration/framework/runtime options and
`--full-wpf-build` for those settings; `--property Name=Value` arguments cannot override managed
output paths or combine multiple assignments in one value, in any build/test mode (including
fresh validation).

Profiles are opt-in. `test` continues to allocate fresh isolated outputs by default; `--fresh`
explicitly requests that behavior for `build` or `test`. Do not combine `--fresh` with `--profile`.
Reused output makes the development loop incremental; retain a fresh isolated validation run for
final evidence. Fresh output roots still use the machine's installed SDK and package cache.

Build and test serialize through `.ai/locks/validation.lock` before retention, restore, build, or
test work. Diagnostic `doctor` restores, `build-profile`, and `build-graph` also acquire this lock.
`--queue` waits for the lock on build/test; without it a busy runner fails. The test command's
`--allow-concurrent` option only skips the additional external build-process guard and never
bypasses this lock. The lock protects shared observability outputs as well as generated build
files. Direct `dotnet` commands and other tools must coordinate with this runner; they do not
automatically acquire its lock.

Test evidence is per invocation even when compiled outputs are reused:

- Run metadata: `.ai/validation-runs/<run-id>.json`.
- Test reports: `.ai/test-results/<run-id>/`, or `<custom-root>/<run-id>/` when passing
  `--results-directory <custom-root>`.
- A supplied `--run-id` names that invocation exactly and must be unused; existing run IDs are
  rejected to preserve earlier evidence.
- Logger `LogFileName` and `LogFilePrefix` values must be filenames within that run directory;
  absolute paths and traversal are rejected. Build properties cannot redirect VSTest reports.

To reset a profile, first wait for all repo-owned build/test work to stop and inspect
`python3 build/python/cli/buildctl.py validation-status --summary`. Remove that profile's manifest
and both corresponding `artifacts/bin/profile-<hash>/` and `artifacts/obj/profile-<hash>/`
directories together. Removing only the manifest leaves incompatible output behind. Automatic
retention never removes a persistent profile, even if it is old or exceeds the temporary-run size
budget.

To measure the development loop, warm a profile once, then record two unchanged invocations and a
third after a reversible single-source-file edit using identical command options. Report restore,
build, test, and total duration from each run's evidence alongside the selected SDK and project.
Restore the source edit, then run `--fresh` separately and keep that evidence as the final isolated
check; it is a different measurement from profile reuse.

### Measured incremental loop

On 2026-10-05, a Linux x64 workspace with .NET SDK 10.0.100 ran
`buildctl.py build --project src/Meridian.Contracts/Meridian.Contracts.csproj --configuration Release
--framework net10.0 --verbosity normal`, using one named session profile for the first four runs
and `--fresh` for the final run. Each measurement includes runner startup, compatibility checks,
restore, and build; the NuGet package cache remained available.

| Run | Elapsed | Assembly observation |
| --- | ---: | --- |
| Profile seed | 15.09 s | New output |
| Unchanged 1 | 1.97 s | Same assembly timestamp and hash |
| Unchanged 2 | 2.03 s | Same assembly timestamp and hash |
| One source-file edit | 15.00 s | Assembly recompiled |
| Fresh isolated build | 15.30 s | New independent output root |

The edit appended a temporary comment to `src/Meridian.Contracts/Text/TextPrimitives.cs`; the
original bytes were restored before the fresh build. This is one local sample, not a CI performance
threshold. Independent test validation used `Meridian.Setup.Tests`: 13 tests passed in each of two
persistent runs and a separate fresh run, with three distinct TRX directories. The persistent test
build step fell from 7.89 s to 0.97 s; the fresh test build took 7.43 s.

## Other Generated Output Retention

`build/scripts/publish/publish.ps1` keeps the default `./dist` publish behavior unchanged. When
automation points `-OutputDir` under `artifacts/publish/<run-name>`, the script prunes sibling
generated publish directories older than 14 days or beyond the latest 5 runs before publishing.
Tune that with `-OutputRetentionDays <days>` and `-OutputRetainLatest <count>`, or set both to `0`
to skip publish-output retention for a run.

Use `-SizeOptimized` when investigating standalone publish size or running under tight local disk
constraints. It keeps single-file publishing, disables publish-only debug/doc output, disables
ReadyToRun, and lowers MSBuild parallelism so size-focused publish runs are less likely to create
large temporary output bursts. Use `build/scripts/publish/measure-size.ps1` to compare common
repo-local output roots such as `artifacts/publish`, dashboard `node_modules`, built workstation
assets, `src`, and `tests`.

`scripts/dev/cleanup-generated.ps1` previews untracked generated `bin`, `obj`, `TestResults`, and
BenchmarkDotNet output by default. The scan skips Node dependency trees so package `bin` folders are
not treated as .NET build output. Add `-IncludeNodeModules` only when you explicitly want to include
repo-local dependency installs such as `src/Meridian.Ui/dashboard/node_modules` in the preview or
execution pass.

## Event Schema

Each event follows the schema below, stored in `build-events.jsonl`:

```json
{
  "event_id": "uuid",
  "timestamp": "2026-01-08T12:34:56.789Z",
  "phase": "restore|build|test|custom",
  "project": "src/Meridian/Meridian.csproj",
  "event_type": "started|completed|failed|warning|skipped",
  "duration_ms": 1234,
  "context": {"key": "value"},
  "error_code": "exit-1",
  "error_message": "Build phase failed",
  "tags": ["restore"]
}
```

## Extending the System

- Add error definitions in `build-system/knowledge/errors/*.json`.
- Add new diagnostics in `build-system/diagnostics/`.
- Extend adapters in `build-system/adapters/`.
- Keep outputs inside `.build-system/` for easy cleanup.
