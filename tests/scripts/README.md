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

```bash
# a single suite
python3 -m unittest tests/scripts/test_screenshot_diff_report.py

# every suite in this directory
python3 -m unittest discover -s tests/scripts -p 'test_*.py'
```

CI runs targeted suites from `.github/workflows/ci.yml` and
`.github/workflows/golden-path-validation.yml`; keep any new CI-gating suite wired into the relevant
workflow.
