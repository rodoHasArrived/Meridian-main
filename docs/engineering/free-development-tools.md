# Free Development Tools

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-06-16

This page captures the free or open-source tooling Meridian developers should prefer before adding
paid services or duplicative frameworks.

## Local Quality Lane

Use the repo-managed local quality wrapper for a focused pre-PR pass:

```powershell
pwsh ./scripts/dev/run-local-quality.ps1
```

Useful options:

```powershell
pwsh ./scripts/dev/run-local-quality.ps1 -DotnetTestFilter "FullyQualifiedName~ReportPackWorkflowServiceTests"
pwsh ./scripts/dev/run-local-quality.ps1 -IncludePlaywrightSmoke
pwsh ./scripts/dev/run-local-quality.ps1 -IncludeDocs
```

The script runs existing free tooling already used by Meridian where available: `dotnet format`,
the warning-suppression inventory check, dashboard Vitest, dashboard build, optional Playwright
workstation smoke validation, optional docs registry validation, and optional local `gitleaks`.
If `gitleaks` is not installed, the script warns because CI still runs the secret scan.

## Browser Workstation Smoke Check

The dashboard package includes a Playwright smoke check that starts the Vite workstation, mocks
registered API fixture responses, verifies the canonical root navigation labels, checks for browser
errors, and writes a screenshot plus manifest under `artifacts/browser-workstation-smoke/`.

```powershell
npm --prefix src/Meridian.Ui/dashboard run smoke:workstation
```

Use this for fast UI proof after workstation shell, routing, or CSS changes. It complements, rather
than replaces, package-local Vitest and targeted screenshot capture.

## Release SBOM

Generate a release software bill of materials after publishing an artifact:

```powershell
pwsh ./build/scripts/publish/generate-sbom.ps1 `
  -BuildDropPath artifacts/publish/publish-smoke/win-x64/web-workstation `
  -PackageName Meridian `
  -PackageVersion 1.0.0-smoke
```

The wrapper uses Microsoft's free open-source SBOM tool if it is available on `PATH`. Install it
with:

```powershell
dotnet tool install --global Microsoft.Sbom.DotNetTool
```

## Data Inspection

Prefer local, free inspection tools for Meridian operational data:

| Tool | Use |
| --- | --- |
| DBeaver Community | Inspect PostgreSQL, SQLite, and other SQL stores during provider, accounting, and reporting debugging. |
| DB Browser for SQLite | Inspect local SQLite files and exported workbooks without writing one-off scripts. |
| DuckDB | Query exported Parquet, CSV, and package artifacts for report-line provenance and operational-evidence checks. |

These tools are for local inspection and evidence verification. They do not change Meridian's
source-of-truth rules: ledger truth remains Meridian-owned, and external stores or exports remain
read-only evidence unless a governed publishing workflow says otherwise.
