# CLAUDE.actions.md - GitHub Actions & CI/CD Guide

This guide covers the current Meridian GitHub Actions surface. Keep it aligned with
`.github/workflows/README.md` and `docs/development/github-actions-summary.md`.

## Workflow Inventory

The active workflow set is deliberately small:

| Workflow | File | Trigger | Purpose |
| --- | --- | --- | --- |
| CI | `ci.yml` | Pull requests, pushes to `main`, manual | Restore, format check, Release build, non-integration .NET tests, dashboard tests, dashboard build |
| Windows Desktop Build | `windows-desktop-build.yml` | Pull requests, pushes to `main`, manual | Full WPF build and tests on Windows, plus desktop publish smoke |
| Publish Smoke | `publish-smoke.yml` | Manual | Uploads standalone Windows publish output for `collector` or `desktop` |
| Maintenance | `maintenance.yml` | Workflow/docs/tooling changes, weekly, manual | Workflow hygiene and Action YAML linting |

## Common Tasks

### Validate CI Locally

```bash
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet format Meridian.sln --verify-no-changes --verbosity minimal --no-restore
dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true
dotnet test Meridian.sln -c Release --no-build --filter "Category!=Integration&Category!=Performance" /p:EnableWindowsTargeting=true
npm ci --prefix src/Meridian.Ui/dashboard
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
- [GitHub Actions Summary](../../development/github-actions-summary.md)
- [GitHub Actions Testing Checklist](../../development/github-actions-testing.md)
- [Build, Test, Run](../../developer/build-test-run.md)
- [Publish Standalone EXE](../../developer/publish-standalone-exe.md)

*Last Updated: 2026-05-18*
