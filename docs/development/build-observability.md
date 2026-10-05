# Build Observability and Diagnostics

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

Use the repository's build controller for local build/test diagnostics and the workflow artifacts
for hosted evidence. For runtime OTLP setup, see [OTLP trace visualization](otlp-trace-visualization.md).

## Prerequisites and working directory

Run from the repository root with Python 3.11 or newer, Git, and the .NET SDK selected by
`global.json`. Platform-specific WPF validation requires Windows; see the
[desktop testing guide](desktop-testing-guide.md). GNU Make is optional.

## Check and run the relevant operation

```bash
# Inspect validation ownership before starting another local test run
python build/python/cli/buildctl.py validation-status --summary

# Inspect environment requirements
python build/python/cli/buildctl.py doctor --quick

# Restore and build with isolated output
python build/python/cli/buildctl.py build --project Meridian.sln --configuration Release --isolation-key automation-run

# Run a focused test slice through the serialized local runner
python build/python/cli/buildctl.py test --project tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj --filter "FullyQualifiedName~FixtureDataServiceTests" --queue

# Capture local tool/checkout metadata for diagnosis
python build/python/cli/buildctl.py collect-debug --project Meridian.sln --configuration Release
```

A successful build/test command exits zero. The test runner prints its run-receipt path and result
location; preserve those with the command and commit when reporting a failure. If another validation
owns the checkout, wait for it or inspect [process lifecycle diagnostics](process-lifecycle-diagnostics.md)
before retrying. A missing SDK/package restore or Windows runtime is a validation limitation, not a
passing test.

## Current outputs

These paths/behaviors follow `build/python/cli/buildctl.py` and the current workflow scripts:

| Operation | Evidence produced |
| --- | --- |
| `build --isolation-key <key>` | Build stdout/stderr and isolated `artifacts/bin/<key>/`, `artifacts/obj/<key>/` outputs |
| `test` | `.ai/validation-runs/` JSON receipts plus the result/log paths printed for that run |
| `collect-debug` | `debug-bundle/debug-info.json`, containing tool/platform/checkout metadata |
| `fingerprint` | Tool/checkout fingerprint printed to stdout |
| `build-profile` | A new timed build; it does not analyze a previous build |
| `build-graph` | The underlying .NET graph-build command's output and exit status |
| `metrics` | A stdout summary of source/test file and recent-commit counts |
| `history` | Recent Git commits printed to stdout |

The reusable emitter in `build/python/core/events.py` can write `build-events.jsonl` and
`build-events.log` when explicitly used. The current CLI does not promise those files, a
`.build-system/` telemetry directory, Prometheus metrics, or a historical build database for every
command. Consult the invoked command's output before looking for artifacts.

Use `python build/python/cli/buildctl.py --help` and subcommand `--help` for current options.
Review captured diagnostics before sharing them outside the repository's normal review process.

## Hosted CI evidence

The canonical local gate is `bash scripts/ci.sh`; [CI/CD ownership](../engineering/ci-cd-optimization.md)
identifies the hosted gate owners. `Meridian CI` uploads lane-specific logs/results from locations
such as `artifacts/build-logs/`, `artifacts/test-results/`, and `artifacts/ci-summary/`. There is no
separate build-observability workflow that automatically runs every diagnostic command above.
Use the [workflow guide](../../.github/workflows/README.md) for exact artifact names and retention.

## Output retention

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

## Extending diagnostics

The CLI entrypoint is `build/python/cli/buildctl.py`; reusable support lives under
`build/python/core/`. Update the owning command's tests and this guide when its output contract
changes. Keep diagnostics distinct from the tracked generated documentation and browser bundle;
follow [documentation ownership](../documentation-ownership.md) for those outputs.
