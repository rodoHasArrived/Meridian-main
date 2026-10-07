# `tests/scripts/` — repository tooling and workflow tests

Python `unittest` suites that verify the repository's **build/CI tooling and workflow contracts** —
the scripts under `build/` and `tools/`, the GitHub Actions workflows under `.github/workflows/`,
and the generated-artifact shapes those produce (e.g. screenshot diff reports, DK1 pilot/parity
packets, roadmap source-doc rendering, contract-review packets).

These are intentionally **separate from the .NET xUnit projects** (`tests/Meridian.Tests`,
`tests/Meridian.FSharp.Tests`, etc.): they exercise Python tooling and YAML workflows, not compiled
Meridian assemblies, so they run without the .NET toolchain.

## Conventions

- **Framework:** `unittest` only (every file here uses `unittest.TestCase`). Do not introduce
  `pytest`-specific fixtures/markers — CI invokes files directly with `python3 -m unittest`.
- **Naming:** one file per tool/workflow under test, named `test_<subject>.py`.
- **Fixtures:** shared input fixtures live under `tests/scripts/fixtures/`.
- **No collection side effects:** tests shell out to the tool under test (via `subprocess`) or import
  it by path; they must not mutate the working tree.
- **Portable report paths:** repository-relative diagnostic paths use `/` on every platform;
  fixtures should distinguish those paths from absolute temporary paths outside the checkout.

## Where each kind of Python test lives

| Location | Scope |
| --- | --- |
| `tests/scripts/` | Repo-wide tooling, CI workflow contracts, and generated-artifact shapes. |
| `build/scripts/docs/tests/` | Tests co-located with the documentation-automation scripts in `build/scripts/docs/`. |
| `build/scripts/tests/` | `pytest` tests for the general build scripts in `build/scripts/`. |

When adding a test for a repo tooling script, put it here unless it belongs to the docs-automation
or general build-script suites above.

## Running

PowerShell 7 (`pwsh`) must be on `PATH` for the consumer certification process tests.
Those tests exercise native process deadlines and inherited output handles without installing
Meridian; the Windows release workflow owns installed consumer lifecycle evidence.

```bash
# a single suite
python3 -m unittest tests/scripts/test_screenshot_diff_report.py

# recovery receipt production and objective validation (requires PowerShell 7)
python3 -m unittest tests/scripts/test_production_recovery.py tests/scripts/test_recovery_evidence.py

# every suite in this directory
python3 -m unittest discover -s tests/scripts -p 'test_*.py'
```

The `verify-workflows` lane of `scripts/ci.sh` discovers these suites through
`build/scripts/ci/run-script-tests.py`; its quarantine register makes exclusions explicit.
By default, the runner uses at most two fresh module processes, starts the slower recovery suites first,
and reports each module's result and duration. Modules retain their full tests and fixtures;
process isolation keeps mocks, environment changes, and fixture summary files separate.
Use `--workers 1` for sequential execution with the same discovery and quarantine rules.
The recovery suites execute the PowerShell producer and validator. PostgreSQL tool stubs exercise
receipt behavior with real encryption and file restoration; database integration and operator
acceptance still require separate operational evidence.
