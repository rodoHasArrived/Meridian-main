# Build Observability System

This repository includes a build observability toolkit that turns local and CI builds into structured, diagnosable workflows.

For runtime OTLP collector setup and trace visualization, see [otlp-trace-visualization.md](otlp-trace-visualization.md).

## Quick Start

```bash
# Build with structured events + metrics
make build

# Build with isolated output for automation or concurrent local runs
python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Release --isolation-key automation-run

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

```bash
.github/workflows/build-observability.yml
```

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
runs per artifact root so repeated local automation does not fill the disk before age-based cleanup
can run. Override the age window with `--isolation-retention-days <days>` and the count guard with
`--isolation-retain-latest <count>`, or set both to `0` for a run that must skip cleanup.

`build/scripts/publish/publish.ps1` keeps the default `./dist` publish behavior unchanged. When
automation points `-OutputDir` under `artifacts/publish/<run-name>`, the script prunes sibling
generated publish directories older than 14 days or beyond the latest 5 runs before publishing.
Tune that with `-OutputRetentionDays <days>` and `-OutputRetainLatest <count>`, or set both to `0`
to skip publish-output retention for a run.

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
