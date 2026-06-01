# Desktop Development Testing Guide

**Status:** active
**Owner:** core-team
**Last Updated:** 2026-06-01

Use this guide for the current WPF desktop validation loop.

## Quick commands

```bash
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/desktop-dev.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-wpf-dev.ps1
make desktop-test-dev
```

## Default WPF validation lane

Run the repeatable Release build and focused desktop workflow slice with:

```bash
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-wpf-dev.ps1
```

The wrapper keeps the serialized build defaults that avoid shared-output contention:

```bash
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release --no-restore --no-dependencies /m:1 /nr:false /p:BuildInParallel=false /p:UseSharedCompilation=false /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:WindowsPackageType=None -v:minimal
```

Use `make desktop-test-dev` for the default wrapper. Pass `-Restore` when packages or generated assets changed, and pass `-AllowConcurrentDotnet` only when overlapping repo-owned dotnet work is intentional.

Common variants:

```bash
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-wpf-dev.ps1 -BuildOnly
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-wpf-dev.ps1 -Restore
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-wpf-dev.ps1 -AllowConcurrentDotnet
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-wpf-dev.ps1 -Filter "Category!=Integration&FullyQualifiedName!~Integration"
```

## Related references

- [Desktop support policy](./policies/desktop-support-policy.md)
- [WPF implementation notes](./wpf-implementation-notes.md)
- [Archived historical copy](../../archive/docs/summaries/desktop-testing-guide.md)
