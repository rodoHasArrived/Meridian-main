# Start Here

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-05-30

Use this page for the fastest safe orientation in a fresh Meridian checkout. It replaces the old split between first-run, developer quickstart, and operator quickstart indexes.

Current local project path: `D:\Meridian-main`.

## What Meridian Is

Meridian is a .NET 10 fund-management and trading-platform codebase focused on evidence-backed investment operations: trusted data, research, paper validation, books, reconciliation, approvals, and governed reporting.

The active user-facing surfaces are:

- Windows desktop workstation: `src/Meridian.Wpf/`
- Browser workstation dashboard: `src/Meridian.Ui/dashboard/`
- Built browser assets served by the host: `src/Meridian.Ui/wwwroot/workstation/`
- Shared workstation API/read-model support: `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/`

## Prerequisites

- .NET SDK pinned by [global.json](../../global.json)
- PowerShell 7 for Windows helper scripts
- Node.js and npm for the browser workstation in `src/Meridian.Ui/dashboard`
- Python 3 for build and documentation automation
- Git
- Optional: GNU Make. On Windows, use the explicit commands below when `make` is unavailable.
- Optional: one configured data provider for live or credentialed historical data.

## First Local Setup

If `make` is available:

```powershell
make setup-dev
dotnet run --project src/Meridian/Meridian.csproj -- --quickstart
```

Plain Windows/PowerShell path:

```powershell
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
npm install
npm --prefix src/Meridian.Ui/dashboard install
dotnet run --project src/Meridian/Meridian.csproj -- --quickstart
```

`--quickstart` delegates to the configuration pipeline and prepares the app for a local workstation launch. Use `config/appsettings.sample.json` as the template for local runtime configuration. Do not commit local `appsettings*.json`, secrets, provider credentials, logs, or generated data.

## First Help Commands

```powershell
make help
dotnet run --project src/Meridian/Meridian.csproj -- --help
python build/python/cli/buildctl.py --help
```

If `make` is unavailable, use the underlying `dotnet`, `npm`, or `python` command shown in [Engineering](../engineering/README.md).

## Choose A Launch Path

| Goal | Command | Notes |
| --- | --- | --- |
| Local host and browser-served workstation | `dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080` | Serves the host and, after assets are built, `http://localhost:8080/workstation/`. |
| Desktop-local host mode | `dotnet run --project src/Meridian/Meridian.csproj -- --mode desktop --http-port 8080` | Use when intentionally running the desktop-local host and streaming collector together. |
| Browser workstation development | `npm --prefix src/Meridian.Ui/dashboard run dev` | Use for active React/TypeScript workstation work. |
| WPF desktop shell | `pwsh ./scripts/dev/run-desktop.ps1` | Recommended Windows desktop launcher. |
| WPF deterministic fixture shell | `pwsh ./scripts/dev/run-desktop.ps1 -Fixture` | Use for offline UI inspection with fixture data. |
| Headless collector | `dotnet run --project src/Meridian/Meridian.csproj -- --mode headless` | Use for non-UI collection scenarios. |

## First Validation Paths

Use the narrowest command that covers the surface you touched:

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --validate-config
dotnet run --project src/Meridian/Meridian.csproj -- --quick-check
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
npm --prefix src/Meridian.Ui/dashboard run test
python build/scripts/docs/check-ai-inventory.py --summary
python build/scripts/docs/check-ai-handoff.py --output docs/status/ai-handoff-checklist-report.md
```

Broaden to full solution, WPF, UI-service, MCP, integration, or performance lanes only when the changed layer requires it.

## Choose Your Next Path

| Audience | Continue with |
| --- | --- |
| Developer or coding agent | [Engineering](../engineering/README.md) |
| Product/stakeholder reviewer | [Product](../product/README.md) |
| Operator or support user | [Operators](../operators/README.md) |
| API/config/provider lookup | [Reference](../reference/README.md) |
| Assistant-safe execution | [AI workflow](../ai/assistant-workflow-contract.md) |

## Legacy Source Material

The older pages below remain source material during migration. Prefer this page and the canonical lanes above for new links.

- [Getting Started](../getting-started/README.md)
- [Developer Setup](../developer/setup.md)
- [Build, Test, Run](../developer/build-test-run.md)
- [Pilot Operator Quickstart](../getting-started/pilot-operator-quickstart.md)
- [HELP](../HELP.md)
