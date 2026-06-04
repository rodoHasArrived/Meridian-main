# CLAUDE.actions.md - GitHub Actions & CI/CD Guide

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-06-04

This guide covers the current Meridian GitHub Actions surface. Keep it aligned with
`.github/workflows/README.md` and `docs/engineering/README.md`.

## Workflow Inventory

The active workflow set is documented in `.github/workflows/README.md`; use that file
as the source of truth before editing workflow behavior.

| Workflow | File | Trigger | Purpose |
| --- | --- | --- | --- |
| CI | `ci.yml` | Pull requests, pushes to `main`, nightly, manual | `verify-fast`, source-doc determinism, browser workstation validation, and scheduled/manual `verify-full` coverage. |
| CodeQL | `codeql.yml` | Pull requests, pushes to `main`, weekly, manual | CodeQL analysis for C# and JavaScript/TypeScript; C# uses an explicit .NET 10 restore/build. |
| Golden Path Validation | `golden-path-validation.yml` | Golden-path surface changes, pushes to `main`, manual | Browser W4 parity, WPF W4 acceptance, pilot-acceptance evidence, and readiness dashboard generation. |
| Windows Desktop Build | `windows-desktop-build.yml` | Pull requests, pushes to `main`, manual | Full WPF build and tests on Windows, plus desktop publish smoke. |
| Publish Smoke | `publish-smoke.yml` | Manual | Uploads standalone Windows publish output for collector, desktop, or web workstation smoke inspection. |
| Desktop Installer Packaging | `desktop-installer-packaging.yml` | Tag pushes, manual | Builds desktop installer packages and attaches release assets for tag runs. |
| Maintenance | `maintenance.yml` | Workflow/docs/tooling changes, weekly, manual | Workflow hygiene, tooling metadata validation, Action YAML linting, and AI contract freshness checks. |

## Common Tasks

### Validate CI Locally

```bash
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet format Meridian.sln --verify-no-changes --verbosity minimal --no-restore
dotnet build Meridian.WebWorkstation.slnf -c Release --no-restore /p:EnableWindowsTargeting=true /p:UseAppHost=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore --filter "Category!=Integration&Category!=Performance" /p:EnableWindowsTargeting=true
npm install --prefix src/Meridian.Ui/dashboard --include=optional
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
```

### Validate Desktop Locally

Run these from a Windows shell:

```powershell
dotnet restore tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release --no-restore /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:WindowsPackageType=None
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -c Release --no-restore --filter "Category!=Integration&FullyQualifiedName!~Integration" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
```

### Validate Workflow Hygiene

```bash
python build/scripts/ci/check-workflow-hygiene.py
```

## Standards

- Use repository-relative paths only.
- Use read-only workflow permissions unless a write is explicitly required.
- Use explicit restore/build/test phases.
- Keep publish workflows manual and artifact-only unless a release/deployment workflow is deliberately reintroduced.
- Keep generated build, test, and publish outputs out of Git.

## Troubleshooting

- **NETSDK1100**: Ensure `EnableWindowsTargeting=true` is set for non-Windows solution builds.
- **NU1008**: Remove package versions from project files; central package management lives in `Directory.Packages.props`.
- **Format check fails**: Run `dotnet format Meridian.sln` locally and review the diff.
- **Workflow hygiene fails**: Remove stale workflow references, local absolute paths, duplicated workflow names, or tracked generated artifacts.

## Related Documentation

- [Workflow README](../../../.github/workflows/README.md)
- [Engineering entrypoint](../../engineering/README.md)
- [Build, Test, Run](../../engineering/README.md)
- [Start guide](../../start/README.md)

*Last Updated: 2026-06-01*
