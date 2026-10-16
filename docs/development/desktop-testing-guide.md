# Desktop Development Testing Guide

**Status:** active
**Owner:** core-team
**Last Updated:** 2026-06-01

Use this guide for the current WPF desktop validation loop.

**Maintenance check (2026-10-05):** commands and platform/startup requirements below were checked
against the launcher and validation scripts; this is not new WPF runtime test evidence.

## Prerequisites and expected result

Use Windows, PowerShell 7 (`pwsh`), the .NET SDK selected by `global.json`, and a restored checkout.
Run commands from the repository root. Start with `-Restore` on a fresh checkout or after dependency
changes. GNU Make is optional; the PowerShell scripts are the direct entrypoints.

The WPF project and its tests compile as empty `net10.0` stubs on non-Windows hosts. A successful
Linux/macOS solution build does not verify the desktop UI; run WPF validation on Windows or use
the hosted Windows validation lane linked from [Engineering](../engineering/README.md#desktop-slices).

## Quick commands

```bash
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/desktop-dev.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-wpf-dev.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/run-desktop.ps1 -LaunchMode Development -BuildOnly
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/run-desktop.ps1 -LaunchMode Production -BuildOnly
make desktop-test-dev
```

On Windows, `desktop-dev.ps1` restores the desktop projects, builds the WPF shell once, and writes
`desktop-build-receipt.json` beside the shell executable. The receipt records the repository,
project, isolation key, configuration, framework, output directory, and exact executable,
assembly, dependency manifest, and runtime configuration paths. The test-project build uses
`--no-dependencies` after that successful shell build, then the fixture startup smoke passes
the receipt to `run-desktop.ps1` so it launches those same binaries.

The launcher checks the receipt's schema, repository/project identity, configuration/framework,
output paths, and artifact existence before building or launching. An explicitly supplied missing
or incompatible receipt fails instead of falling back to another output tree. Without a receipt,
the launcher builds the shell as usual; `-NoBuild` keeps its existing prebuilt-output behavior.
A healthy existing host is reused without requiring a local host executable. Otherwise the launcher
builds the host only if its executable is missing from the selected output tree; `-BuildOnly` ensures
the host artifact exists without starting either process. Host health polling, fixture overrides,
desktop window detection, and owned-process cleanup also apply when using a receipt.

To reuse a receipt in a later launch, supply its path and matching build settings:

```powershell
pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Development -Fixture -StartupSmoke `
    -BuildReceiptPath '<output-directory>/desktop-build-receipt.json' -Configuration Debug `
    -Framework net10.0-windows10.0.19041.0
```

`desktop-dev.ps1 -EmitJson` includes `desktopBuildReceiptPath` in its summary. Receipts also work
with `-NoIsolation`. A skipped shell build does not issue a receipt, and `-SkipRestore` or
`-SkipBuild` continues to skip the bootstrap's launch smoke.

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

For manual shell runs, keep development and production lanes explicit:

```bash
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/run-desktop.ps1 -LaunchMode Development
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/run-desktop.ps1 -LaunchMode Development -Fixture
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/run-desktop.ps1 -LaunchMode Production -BuildOnly
```

Production launch without `-BuildOnly` requires `MERIDIAN_DATABASE_URL`, or both
`MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING` and `MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING`.
This satisfies the launcher's governance configuration gate; the backend must still pass its
startup checks. Development launch sets the local Development environment and in-memory
governance opt-in for the launch and restores the caller's settings afterward.

Common variants:

```bash
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-wpf-dev.ps1 -BuildOnly
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-wpf-dev.ps1 -Restore
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-wpf-dev.ps1 -AllowConcurrentDotnet
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-wpf-dev.ps1 -Filter "Category!=Integration&FullyQualifiedName!~Integration"
```

## Results and recovery

Successful validation exits zero and writes logs/results beneath
`artifacts/wpf-validation/dev-loop/`. If validation refuses concurrent work, let the repo-owned
build finish and retry; use [process lifecycle diagnostics](process-lifecycle-diagnostics.md) to
identify stale processes. Restore missing assets with `-Restore` instead of treating a skipped
build or an empty non-Windows test run as desktop evidence.

## Related references

- [Desktop support policy](./policies/desktop-support-policy.md)
- [WPF implementation notes](./wpf-implementation-notes.md)
- [Archived historical copy](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/summaries/desktop-testing-guide.md)
