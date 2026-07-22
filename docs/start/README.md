# Start Here

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-07-19

Use this page for the fastest safe orientation in a fresh Meridian checkout. Run commands from the
repository root unless a command says otherwise.

## What Meridian Is

Meridian is a .NET 10 operational-finance and trading-platform codebase focused on evidence-backed
investment operations: trusted data, reconciliation, accounting records, approvals, governed
reporting, research continuity, and paper-first execution controls. Fund management is a
first-class specialization, not the root model for every workflow.

The active user-facing surfaces are:

- Windows desktop workstation: `src/Meridian.Wpf/`
- Browser workstation dashboard: `src/Meridian.Ui/dashboard/`
- Generated browser assets served by the host after a dashboard build:
  `src/Meridian.Ui/wwwroot/workstation/`
- Shared workstation API/read-model support: `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/`

For the current product framing and capability thesis, use the [Meridian Design Document](../product/meridian-design-document.md) before planning changes.

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

## See It Working: One-Command Demo

To evaluate Meridian end-to-end without wiring up providers or a database, seed a demo workspace and
open the populated browser workstation with a single command:

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --seed-demo
```

This provisions a durable, clearly-labelled **Seeded** demo workspace and then starts the browser
workstation on it, so the first screen you see is a populated one — reconciliation casework in the
control tower and a completed paper strategy run on the Strategy desk — not an empty shell or a
fabricated default watchlist. Every seeded record carries `Seeded` provenance (source system
`Meridian Seeded Demo`) so it is always distinguishable from real operator data.

Key properties:

- **Isolated.** All demo data lives in a dedicated root, `{dataRoot}/demo-workspace`, that is never
  mixed with your real data root.
- **Durable.** The reconciliation and strategy desks are written to durable, file-backed stores under
  the demo root and survive a restart with zero configuration. Money-path stores such as the ledger
  become durable only when PostgreSQL is configured — set
  `MERIDIAN_DATABASE_URL=postgres://user:password@localhost:5432/meridian` before seeding for a
  fully database-backed demo.
- **Idempotent.** Re-running `--seed-demo` never duplicates casework or runs.
- **Reversible and safe.** `--reset-demo` deletes only the demo workspace; a teardown guard refuses to
  touch any directory that is not the dedicated, sentinel-marked demo root.

```powershell
# Seed without launching the workstation (used by CI and scripted checks)
dotnet run --project src/Meridian/Meridian.csproj -- --seed-demo --seed-only

# Re-open the already-seeded demo later
dotnet run --project src/Meridian/Meridian.csproj -- --demo

# Tear the demo workspace down (only the demo root is ever removed)
dotnet run --project src/Meridian/Meridian.csproj -- --reset-demo
```

The seeded workspace is the same sample workspace the browser onboarding wizard's "Use sample data"
choice provisions, so the demo and the guided first-run experience walk through identical data. See
`dotnet run --project src/Meridian/Meridian.csproj -- --help demo` for the full command reference.

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
| Seeded end-to-end demo (fastest evaluation) | `dotnet run --project src/Meridian/Meridian.csproj -- --seed-demo` | Seeds an isolated, durable, `Seeded`-labelled demo workspace and opens the populated workstation. See [See It Working](#see-it-working-one-command-demo). |
| Local host and browser-served workstation | `dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080` | Serves the host and, after assets are built, `http://localhost:8080/workstation/`. |
| Desktop-local host mode | `dotnet run --project src/Meridian/Meridian.csproj -- --mode desktop --http-port 8080` | Use when intentionally running the desktop-local host and streaming collector together. |
| Browser workstation development | `npm --prefix src/Meridian.Ui/dashboard run dev` | Use for active React/TypeScript workstation work. |
| WPF desktop development shell | `pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Development` | Builds Debug artifacts and explicitly opts into the local Development/in-memory governance profile. |
| WPF deterministic fixture shell | `pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Development -Fixture` | Use for offline UI inspection with fixture data. |
| WPF production build | `pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Production -BuildOnly` | Builds Release host and desktop artifacts without starting the host. |
| WPF production shell | `pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Production` | Requires persistence-backed governance connection strings before host startup. |
| Headless collector | `dotnet run --project src/Meridian/Meridian.csproj -- --mode headless` | Use for non-UI collection scenarios. |

### Persistence and simulation defaults

Two defaults matter before you trust what a local launch shows you:

- **Persistence.** Without database configuration, every money-path store (ledger, fund
  accounts, banking, reporting, and more) runs in-memory — journal entries, reconciliations,
  and approvals are lost on restart. Set one variable to persist all store domains to
  PostgreSQL: `MERIDIAN_DATABASE_URL=postgres://user:password@localhost:5432/meridian`
  (per-domain `MERIDIAN_*_CONNECTION_STRING` variables override it individually). Hosts log
  `PERSISTENCE: NONE`/`PARTIAL` at startup, report it on `/readyz`, and the browser
  workstation shows a persistent red banner until persistence is configured.
- **Market data.** The default `ib` streaming source runs as a random-walk **simulator** in
  standard builds (no IBAPI reference), and the `synthetic` source is always simulated.
  Simulated data is flagged in `/api/status` (`degradedMode.marketDataMode`) and by the same
  red workstation banner — never treat quotes, fills, or P&L from a simulated source as real.

The WPF desktop startup screen prompts for the environment-backed Meridian operator profile. Configure
`MDC_USERS` with `passwordHash` values for multi-user login, or use the legacy `MDC_USERNAME` /
`MDC_PASSWORD_HASH` bootstrap pair for a single local admin. Development launches can continue
without configured credentials when auth mode is optional; production, packaged, and customer builds
fail closed until a user profile is configured. After sign-in, the desktop shell header shows the
active operator and its `Log out` command returns to the same startup credential prompt without
persisting the password in WPF config files.

## First Validation Paths

Use the narrowest command that covers the surface you touched:

For completed PR-ready work, use the canonical repository gate:

```powershell
bash scripts/ci.sh
```

GitHub Actions `Meridian CI / quality-gate` remains the authoritative merge result after the branch
is pushed and the pull request is opened.

For local .NET tests, use the contention-aware runner when another agent, desktop shell, or test
lane may be active:

```powershell
python build/python/cli/buildctl.py validation-status --summary
python build/python/cli/buildctl.py test --project tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~<TestClassOrMethod>" --queue
```

After a timed-out generation, build, or test attempt, run `python build/python/cli/buildctl.py
validation-status --summary`, then `dotnet build-server shutdown`. Stop only abandoned repo-owned
`dotnet`, `MSBuild`, `testhost`, `csc`, or `VBCSCompiler` PIDs after confirming their command lines
point at this checkout.

If local machine limits make the relevant proof lane unreliable, push the branch and use the
manual GitHub-hosted `Targeted Test` workflow before retrying broad local scripts. Select a
whitelisted `mode`; `mode=dotnet-filtered` requires a repo-relative test project under `tests/`
plus `dotnet_filter` so it runs the failing slice instead of a whole test project. Use a positive
class, method, trait, or fully qualified name selector rather than a negative-only or broad CI
filter:

```powershell
gh workflow run targeted-test.yml --ref <branch> -f mode=dotnet-filtered -f dotnet_project=tests/Meridian.Tests/Meridian.Tests.csproj -f dotnet_filter="FullyQualifiedName~<TestClassOrMethod>"
```

The validated dispatcher wrapper builds the same hosted command and can wait for the run:

```powershell
python build/scripts/ci/dispatch-targeted-test.py --ref <branch> --mode dotnet-filtered --dotnet-project tests/Meridian.Tests/Meridian.Tests.csproj --dotnet-filter "FullyQualifiedName~<TestClassOrMethod>" --wait
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
