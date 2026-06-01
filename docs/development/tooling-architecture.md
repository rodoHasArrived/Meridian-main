# Tooling Architecture

**Status:** Active  
**Owner:** Core Team  
**Reviewed:** 2026-05-20

This guide explains Meridian tooling as a layered system so contributors can pick the right command path quickly and understand how local commands map to CI.

## 1) Command layering and ownership

| Layer | Purpose | Authoritative entrypoints | Convenience entrypoints | Owner |
| --- | --- | --- | --- | --- |
| Runtime/build primitives | Canonical build, test, run, and publish operations | `dotnet`, `python3 build/python/cli/buildctl.py`, `npm --prefix src/Meridian.Ui/dashboard`, `pwsh ./scripts/dev/*.ps1` | n/a | Core Team |
| Local orchestration | Human-friendly local workflows that compose primitives | `make/*.mk` targets | `make` aliases such as `build-quick`, `test-coverage`, `pre-pr` | Core Team |
| CI orchestration | Pull-request and branch gates | `.github/workflows/ci.yml`, `windows-desktop-build.yml`, `golden-path-validation.yml`, `maintenance.yml`, `publish-smoke.yml` | manual `workflow_dispatch` entrypoints | Core Team |
| Generated documentation and inventories | Deterministic repo metadata outputs | `python3 build/scripts/docs/*.py`, `make gen-*`, `make docs*` | summary docs in `docs/generated/` | Core Team |

## 2) Authoritative vs convenience command policy

Use authoritative commands for scripting, CI parity work, and incident/debug sessions. Use convenience aliases for local speed.

| Use case | Authoritative | Convenience aliases |
| --- | --- | --- |
| Restore/build | `dotnet restore Meridian.sln /p:EnableWindowsTargeting=true` and `dotnet build ...` or `python3 build/python/cli/buildctl.py build ...` | `make build`, `make build-quick` |
| Tests | `dotnet test ...` and `npm --prefix src/Meridian.Ui/dashboard run test` | `make test`, `make test-unit`, `make test-fsharp` |
| Documentation generation | `python3 build/scripts/docs/...` and `dotnet run --project build/dotnet/DocGenerator/...` | `make docs`, `make docs-all`, `make gen-*` |
| Desktop validation | `dotnet test tests/Meridian.Wpf.Tests/...` and `pwsh ./scripts/dev/*.ps1` | `make desktop-build`, `make desktop-test*` |
| Environment diagnostics | `python3 build/python/cli/buildctl.py ...` | `make doctor*`, `make diagnose*`, `make collect-debug*` |

## 3) Generated artifacts and ownership

| Artifact root | Produced by | Typical commands | Owner |
| --- | --- | --- | --- |
| `artifacts/bin/`, `artifacts/obj/` | isolated build outputs | `python3 build/python/cli/buildctl.py build --isolation-key ...` | Core Team |
| `artifacts/test-results/` and `TestResults/` | test logs and coverage | `dotnet test ... --results-directory ...`, `make test-all` | Core Team |
| `artifacts/publish/` | publish smoke outputs | `pwsh ./build/scripts/publish/publish.ps1 ...`, `make publish*` | Core Team |
| `artifacts/docs/` and `docs/generated/` | generated docs and parity reports | `make gen-*`, `make check-workflow-docs-parity`, docs automation scripts | Core Team |
| `artifacts/build-logs/` | CI build logs and warning reporting | `.github/workflows/ci.yml` | Core Team |

Generated outputs are disposable unless explicitly designated as governance evidence.

## 4) Local-to-CI mapping

| CI workflow | Required local equivalent |
| --- | --- |
| `CI` (`.github/workflows/ci.yml`) | `dotnet restore Meridian.sln /p:EnableWindowsTargeting=true`, `dotnet format Meridian.sln --verify-no-changes --verbosity minimal --no-restore`, `dotnet build Meridian.WebWorkstation.slnf -c Release --no-restore /p:EnableWindowsTargeting=true /p:UseAppHost=false`, non-integration `dotnet test` lanes, dashboard `npm ... test/build` |
| `Windows Desktop Build` | WPF restore/build/test commands in `archive/docs/developer/build-test-run.md` |
| `Golden Path Validation` | pilot-acceptance test + dashboard generation commands in `.github/workflows/README.md` |
| `Maintenance` | `python3 build/scripts/ci/check-workflow-hygiene.py` and related docs/tooling validation |
| `Publish Smoke` | `pwsh ./build/scripts/publish/publish.ps1 ...` smoke invocation |

## 5) Warning suppression policy (MW-008)

`Directory.Build.props` is the authoritative suppression inventory. Every global suppression entry now requires:

- `Owner`
- `Justification`
- `RatchetPlan`

The CI lane runs `python3 build/scripts/ci/check-warning-suppressions.py` so new suppressions are rejected unless they are registered with ownership and a retirement plan. The same CI lane also reports build warning counts in the run summary.

Project-level migration policy:

1. Keep only cross-cutting suppressions global.
2. Move category-specific suppressions (for example XML-doc or trim-analysis categories) into project or target-specific scopes as soon as owning teams complete cleanup.
3. Remove global entries once project-level ownership is complete.

## 6) AI automation lanes (MW-009)

### Required quality gates

- `make ai-verify`
- `make ai-arch-check`
- CI step: `Validate AI contract drift` in `.github/workflows/ci.yml`

### Advisory tooling

- `make ai-audit*`
- `make ai-report`
- `make ai-docs-freshness`
- `make ai-docs-drift`
- `make ai-docs-sync-report`
- `make ai-arch-check-summary`
- `make ai-arch-check-json`

### Maintenance/reporting

- `make ai-maintenance-light`
- `make ai-maintenance-full`
- `make ai-docs-archive`
- `make ai-docs-archive-execute`

The root `make help` output mirrors this split so contributors can distinguish blocking gates from optional tooling.

## Related docs

- [Developer Quick Guides](../../archive/docs/developer/README.md)
- [Build, Test, Run](../../archive/docs/developer/build-test-run.md)
- [GitHub Actions Workflows - Summary](github-actions-summary.md)
- [Tooling & Workflow Backlog](tooling-workflow-backlog.md)
