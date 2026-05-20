# Build, Test, Run

**Status:** Active
**Owner:** Core Team
**Reviewed:** 2026-05-19

Use the narrowest command that covers your change.

## Canonical lane matrix

| Lane | Use when | Maps to |
| --- | --- | --- |
| `bootstrap` | Initial setup, machine drift recovery, or local environment refresh. | `make setup-dev` |
| `verify-fast` | Most day-to-day validation before pushing commits. | `make pre-pr` |
| `verify-full` | Broad confidence checks before requesting review. | `make pre-pr-full` |
| `verify-docs` | Docs/workflow-doc updates and lane-vocabulary drift checks. | `make docs-lint`, `make check-workflow-docs-parity`, `make check-status-delivery-claims`, `python3 build/scripts/docs/check-known-lanes.py` |
| `verify-desktop` | Retained desktop shell changes or shared-contract checks needing WPF confidence. | `make desktop-build`, `make desktop-test` |
| `verify-release` | Publish smoke and release packaging validation work. | `make publish` |

## Build

```powershell
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet build Meridian.sln /p:EnableWindowsTargeting=true
```

For the active browser workstation lane, prefer the focused solution filter. It builds the
host-served workstation without WPF, benchmarks, MCP hosts, or optional integration projects:

```powershell
dotnet restore Meridian.WebWorkstation.slnf /p:EnableWindowsTargeting=true
dotnet build Meridian.WebWorkstation.slnf -c Debug --no-restore /p:EnableWindowsTargeting=true /p:UseAppHost=false
python3 build/python/cli/buildctl.py build --project Meridian.WebWorkstation.slnf --configuration Debug --isolation-key web-workstation-dev --property UseAppHost=false --verbosity quiet
```

For automation or concurrent local builds, prefer isolated output:

```powershell
python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Release --isolation-key cleanup-pass
```

To preview and remove local generated output without touching tracked files:

```powershell
pwsh ./scripts/dev/cleanup-generated.ps1 -IncludeNodeModules -IncludeTemp -IncludeLogs
pwsh ./scripts/dev/cleanup-generated.ps1 -IncludeNodeModules -IncludeTemp -IncludeLogs -Execute
```

If a cleanup stops on a locked `bin/` or `obj/` file, close the running
`dotnet`/Meridian process that owns the output and rerun the preview.

## Test

```powershell
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj --logger "console;verbosity=normal"
npm --prefix src/Meridian.Ui/dashboard run test
npm run ui:dashboard:test
```

Broaden to WPF, UI-service, MCP, or integration tests only when the touched
files require it.

## CI Equivalents

The automatic `CI` workflow mirrors these commands:

```powershell
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet format Meridian.sln --verify-no-changes --verbosity minimal --no-restore
dotnet build Meridian.WebWorkstation.slnf -c Release --no-restore /p:EnableWindowsTargeting=true /p:UseAppHost=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore --filter "Category!=Integration&Category!=Performance" /p:EnableWindowsTargeting=true
npm ci --prefix src/Meridian.Ui/dashboard
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
```

The `Maintenance` workflow also enforces a **metadata integrity gate** for tooling manifests via:

```bash
python3 build/scripts/validate-tooling-metadata.py
```

PR branch protection should require the `Maintenance / Workflow Hygiene` check so dependency and
tooling metadata changes (including `.github/dependabot.yml`, `package.json`, `Makefile`, and
`make/*.mk`) cannot merge without this validation.

The `Windows Desktop Build` workflow mirrors a Windows-only WPF build and test pass:

```powershell
dotnet restore tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release --no-restore /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:WindowsPackageType=None
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -c Release --no-restore --filter "Category!=Integration&FullyQualifiedName!~Integration" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
```

## Run

Main local host and CLI:

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080
```

Use `--mode desktop` only when you intentionally want the retained desktop-local host to start
the UI server and the streaming collector together.

Browser workstation development server:

```powershell
npm --prefix src/Meridian.Ui/dashboard run dev
```

The browser workstation route is `http://localhost:<vite-port>/workstation/`
for Vite and `http://localhost:8080/workstation/` when served by the Meridian
host.

Retained WPF shell:

```powershell
pwsh ./scripts/dev/run-desktop.ps1
```

## AI/TODO governance checks

Use these deterministic remediation commands when CI reports policy drift:

```bash
python3 build/scripts/docs/scan-todos.py --json-output docs/status/todo-scan-results.json
python3 build/scripts/docs/validate-todo-registry.py --scan-json docs/status/todo-scan-results.json --registry docs/source/todo-registry.json --enforce-prefix docs/source/
python3 build/scripts/docs/check-ai-contract-drift.py --canonical docs/ai/contract-policy.json --mirror docs/ai/copilot/contract-policy.mirror.json --mirror docs/ai/claude/contract-policy.mirror.json
```
