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

For the current product framing and capability thesis, use the [Meridian Design Document (Draft v1.0)](../product/meridian-design-document.md) before planning changes.

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

Plain Windows/PowerShell path:

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --help
python build/python/cli/buildctl.py --help
```

Optional Make wrapper path:

```powershell
where.exe make
make help
```

If `where.exe make` finds nothing, skip Make and use the underlying `dotnet`, `npm`, `pwsh`, or
`python` command shown in [Engineering](../engineering/README.md).

## Choose A Launch Path

| Goal | Command | Notes |
| --- | --- | --- |
| Local host and browser-served workstation | `dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080` | Serves the host and, after assets are built, `http://localhost:8080/workstation/`. |
| Desktop-local host mode | `dotnet run --project src/Meridian/Meridian.csproj -- --mode desktop --http-port 8080` | Use when intentionally running the desktop-local host and streaming collector together. |
| Browser workstation development | `npm --prefix src/Meridian.Ui/dashboard run dev` | Use for active React/TypeScript workstation work. |
| WPF desktop development shell | `pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Development` | Builds Debug artifacts and explicitly opts into the local Development/in-memory governance profile. |
| WPF deterministic fixture shell | `pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Development -Fixture` | Use for offline UI inspection with fixture data. |
| WPF production build | `pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Production -BuildOnly` | Builds Release host and desktop artifacts without starting the host. |
| WPF production shell | `pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Production` | Requires persistence-backed governance connection strings before host startup. |
| Headless collector | `dotnet run --project src/Meridian/Meridian.csproj -- --mode headless` | Use for non-UI collection scenarios. |

The WPF desktop startup screen prompts for the environment-backed Meridian operator profile. Configure
`MDC_USERS` with `passwordHash` values for multi-user login, or use the legacy `MDC_USERNAME` /
`MDC_PASSWORD_HASH` bootstrap pair for a single local admin. Development launches can continue
without configured credentials when auth mode is optional; production, packaged, and customer builds
fail closed until a user profile is configured. After sign-in, the desktop shell header shows the
active operator and its `Log out` command returns to the same startup credential prompt without
persisting the password in WPF config files.

## First Validation Paths

Use the narrowest command that covers the surface you touched:

For local .NET tests, use the contention-aware runner when another agent, desktop shell, or test
lane may be active:

```powershell
python build/python/cli/buildctl.py validation-status --summary
python build/python/cli/buildctl.py test --project tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~<TestClassOrMethod>" --queue
```

If local machine limits make the relevant proof lane unreliable, push the branch and use the
manual GitHub-hosted `Targeted Test` workflow before retrying broad local scripts. The .NET lane
requires a repo-relative test project under `tests/` plus `dotnet_filter` so it runs the failing
slice instead of a whole test project:

```powershell
gh workflow run targeted-test.yml --ref <branch> -f lane=dotnet -f dotnet_project=tests/Meridian.Tests/Meridian.Tests.csproj -f dotnet_filter="FullyQualifiedName~<TestClassOrMethod>"
gh workflow run targeted-test.yml --ref <branch> -f lane=browser-dashboard -f browser_script=test:vitest -f vitest_file=src/screens/<screen>.test.tsx
```

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --validate-config
dotnet run --project src/Meridian/Meridian.csproj -- --quick-check
python build/python/cli/buildctl.py test --project tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --queue
npm --prefix src/Meridian.Ui/dashboard run test
python build/scripts/docs/check-ai-inventory.py --summary
python build/scripts/docs/check-ai-handoff.py --output docs/ai/generated/ai-handoff-checklist-report.md
python build/scripts/docs/check-ai-contract-drift.py --canonical docs/ai/contract-policy.json --mirror docs/ai/copilot/contract-policy.mirror.json --mirror docs/ai/claude/contract-policy.mirror.json
python build/scripts/docs/run-docs-automation.py --scripts check-ai-handoff-strict
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

- [Getting Started](../../archive/docs/getting-started/README.md)
- [Developer Setup](../../archive/docs/developer/setup.md)
- [Build, Test, Run](../../archive/docs/developer/build-test-run.md)
- [Pilot Operator Quickstart](../../archive/docs/getting-started/pilot-operator-quickstart.md)
- [HELP](../HELP.md)
