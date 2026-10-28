# Start Here

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-28

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
- Windows and PowerShell 7 for WPF desktop development and its helper scripts; non-Windows
  builds compile a stub and do not validate WPF behavior
- Node.js 24 and npm for browser development, pinned by [.nvmrc](../../.nvmrc) and browser CI
- Python 3.11 or newer for build and documentation automation
- Git
- Bash for the canonical `scripts/ci.sh` validation lanes (Git Bash or WSL on Windows)
- Optional: GNU Make with a POSIX shell and `python3` on `PATH`. On Windows, use Git Bash or WSL
  for Make recipes, or use the direct PowerShell commands below.
- Optional: one configured data provider for live or credentialed historical data.

Install only the tools needed for your task: the tracked workstation demo needs .NET but no
Node.js; fixture-only browser development needs Node.js and npm but no .NET or database.
Backend-connected browser development needs both .NET and Node.js. The dev container supplies
PostgreSQL 16; database-backed integration checks use the version specified by their lane.
The database-less demo does not need PostgreSQL.

## First Local Setup

Check the prerequisites for the work you plan to run before restoring or building:

```powershell
python build/python/cli/buildctl.py doctor --profile unit-test
python build/python/cli/buildctl.py doctor --profile browser
python build/python/cli/buildctl.py doctor --profile desktop
python build/python/cli/buildctl.py doctor --profile full-quality-gate
```

These profiles only inspect prerequisites and print fixes. Unit-test checks do not require provider
credentials or running services. Desktop checks require Windows and PowerShell 7. The full gate also
checks the pinned Python packages, actionlint, and PowerShell needed by the script suite. See
[engineering prerequisite checks](../engineering/README.md#prerequisite-checks) for details.

### .NET setup

```powershell
dotnet restore Meridian.sln -p:EnableWindowsTargeting=true
dotnet build Meridian.WebWorkstation.slnf -c Debug --no-restore -p:EnableWindowsTargeting=true -p:UseAppHost=false
```

These commands work in PowerShell or Bash from the repository root. They restore the solution
and build the browser host and its dependencies. Choose a launch path below after the build.
For a first evaluation with no provider configuration, use the [seeded demo](#see-it-working-one-command-demo).

For your own configuration, run auto-configuration:

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --quickstart
```

`--quickstart` writes `config/appsettings.json`. Rerunning it backs up the existing file as
`config/appsettings.json.backup-<timestamp>` and replaces it with the newly detected configuration.
It does not prove that persistence, operator authentication, or provider connectivity is ready;
complete [operator preflight](../operators/preflight-checklist.md) before a non-demo launch.
Use `config/appsettings.sample.json` as the template when configuring manually. Keep local
configuration, configuration backups, secrets, provider credentials, logs, and generated data out
of commits.

### Browser dependencies

For browser source development, install both lockfiles, then choose a mode:

```sh
npm ci
npm --prefix src/Meridian.Ui/dashboard ci
```

Before launching, run `npm run dev:doctor` for backend-connected prerequisites and port checks,
or `npm run dev:fixtures -- --doctor` for fixture-only prerequisites. These commands report
problems and suggested fixes without installing dependencies or starting services.

Use `npm run dev` for the seeded backend and Vite together, or `npm run dev:fixtures` for Vite
with labelled sample responses and no backend. Each launcher stays running until Ctrl+C.
The backend-connected launcher creates its own configuration; neither mode needs `--quickstart`.
See the [browser development guide](../engineering/web-development.md) for readiness and recovery.

### Optional Make setup

In a POSIX shell with GNU Make and `python3` available:

```bash
make setup-dev
```

This installs Git hooks, prepares configuration, restores/builds .NET, and runs a .NET test slice.
It does **not** install browser dependencies; browser contributors must also run the two npm
install commands above. Use the direct .NET commands when you only need the host build.

### Dev Container

Open the repository with VS Code **Reopen in Container** or GitHub Codespaces and wait for the
creation hooks to finish. The [dev container](../../.devcontainer/devcontainer.json) uses Node.js 24
to match browser CI and installs the root and dashboard dependencies from their lockfiles,
including the optional native packages needed by Vite.

Start browser development from the repository root:

```bash
npm run dev
```

This starts the seeded backend in watch mode and Vite together. For browser fixtures without a
backend, use `npm run dev:fixtures`. See the [browser development launcher guide](../engineering/web-development.md)
for modes, readiness checks, and shutdown behavior.

Open the URL printed after `[dev] Ready`, normally `http://127.0.0.1:5173/workstation/`, through
the forwarded **Browser Workstation (Vite)** port. The host/API port 8080 and PostgreSQL port 5432
are also forwarded. The dev container's [Compose configuration](../../.devcontainer/docker-compose.yml)
starts PostgreSQL 16, waits for its health check, and supplies database connection variables to the
development shell. The launcher respects those variables; outside the dev container it does not
provision a database. If using Vite directly, start the configured host separately. A forwarded
port alone does not establish a running service.
Run a focused browser component test in another terminal; no additional dependency install is needed:

```bash
npm --prefix src/Meridian.Ui/dashboard run test -- src/components/ui/button.test.tsx
```

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
- **Durable.** Reconciliation casework, the paper strategy run, market history for the Data desk,
  the sample fund account and portfolio position snapshot, balanced draft journal entries, and a
  review-required sample report pack are all written to durable, file-backed stores under the demo
  root and survive a restart with zero configuration. Accounting records seed as drafts for human
  review — the demo never posts ledger entries. Posted money-path stores such as the ledger become
  durable only when PostgreSQL is configured — set
  `MERIDIAN_DATABASE_URL=postgres://user:password@localhost:5432/meridian` before seeding for a
  fully database-backed demo.
- **Idempotent.** Re-running `--seed-demo` never duplicates casework or runs, and re-seeding
  converges the seeded market history back on its documented session window rather than
  stacking sessions onto the previous seed. Seeded market history is written in the same
  `{SYMBOL}/{eventType}/{date}.jsonl` layout a live capture produces, so the Data desk reads
  it through its ordinary discovery rules; every seeded print carries the `SAMPLE` source.
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

### The workstation bundle the demo serves

The browser workstation is served from one canonical, git-tracked bundle:
`src/Meridian.Ui/wwwroot/workstation`. A fresh clone already carries it, so the demo reaches a
populated `/workstation/` screen without Node.js installed, and the host resolves that same tree
regardless of the directory you launch from. When you change dashboard source, regenerate and
commit the bundle with:

```powershell
npm --prefix src/Meridian.Ui/dashboard run build
```

CI runs a freshness gate that fails when the tracked bundle lags `src/Meridian.Ui/dashboard`, so
the served assets always match the same commit's dashboard source.

When updating a feature branch causes generated bundle, documentation or hash conflicts, use the
[isolated repair command](../engineering/generated-merge-recovery.md). It prepares pinned merge
and generation evidence without changing your branch or local edits. Reviewed documentation hashes
are reused only when they match the final module; combined source/prose changes still need review.

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
| Local host and browser-served workstation | `dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080` | Requires a persistence decision first — see [Persistence and simulation defaults](#persistence-and-simulation-defaults); bare, it fails closed at startup. Once configured, serves the host and `http://localhost:8080/workstation/` from the tracked canonical bundle (`src/Meridian.Ui/wwwroot/workstation`), independent of launch directory. |
| Desktop-local host mode | `dotnet run --project src/Meridian/Meridian.csproj -- --mode desktop --http-port 8080` | Use when intentionally running the desktop-local host and streaming collector together. |
| Browser development with seeded backend | `npm run dev` | Coordinates seeding, backend watch/restarts, and Vite hot reload. See [browser development](../engineering/web-development.md) for prerequisites and options. |
| Browser fixture-only development | `npm run dev:fixtures` | Starts Vite with explicit demo fixtures and no backend requests; .NET is not required. See [browser development](../engineering/web-development.md). |
| WPF desktop development shell | `pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Development` | Builds Debug artifacts and explicitly opts into the local Development/file-backed governance profile. |
| WPF deterministic fixture shell | `pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Development -Fixture` | Use for offline UI inspection with fixture data. |
| WPF production build | `pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Production -BuildOnly` | Builds Release host and desktop artifacts without starting the host. |
| WPF production shell | `pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Production` | Requires persistence-backed governance connection strings before host startup. |
| Headless collector | `dotnet run --project src/Meridian/Meridian.csproj -- --mode headless` | Use for non-UI collection scenarios. |

### Persistence and simulation defaults

Two defaults matter before you trust what a local launch shows you:

- **Persistence.** `--seed-demo`, `--demo`, and the seeded `npm run dev` launcher select a
  database-less local profile when settings are unset. Normal
  launches require configured fund-account and fund-structure stores and fail closed when those
  connections are missing. `MERIDIAN_DATABASE_URL` can supply PostgreSQL across store domains;
  per-domain connection variables take precedence. The explicitly non-production
  `MERIDIAN_USE_INMEMORY_GOVERNANCE=true` option selects **file-backed** governance stores,
  despite its name. Other money-path stores still need PostgreSQL for durability. See the
  [environment reference](../reference/environment-variables.md) for exact precedence, affected
  stores, and startup behavior. Confirm the persistence summary and `/readyz` before relying on
  a configured host.
- **Market data.** The default `ib` streaming source runs as a random-walk **simulator** in
  standard builds (no IBAPI reference), and the `synthetic` source is always simulated.
  Simulated data is flagged in `/api/status` (`degradedMode.marketDataMode`) and by the same
  red workstation banner — never treat quotes, fills, or P&L from a simulated source as real.

Authentication defaults to required outside Development, so a non-demo launch also needs a
credential path. Packaged installs get their first account through the one-use
`MDC_BOOTSTRAP_TOKEN` and the `/setup/account` page: the lifecycle supervisor generates the token
and passes it to the host it starts, while a from-source launch must set it explicitly (see
[environment variables](../reference/environment-variables.md)).

The WPF desktop startup screen prompts for the environment-backed Meridian operator profile. Configure
`MDC_USERS` with `passwordHash` values for multi-user login, or use the legacy `MDC_USERNAME` /
`MDC_PASSWORD_HASH` bootstrap pair for a single local admin. Development launches can continue
without configured credentials when auth mode is optional; production, packaged, and customer builds
fail closed until a user profile is configured. After sign-in, the desktop shell header shows the
active operator and its `Log out` command returns to the same startup credential prompt without
persisting the password in WPF config files.

## First Validation Paths

Start with environment and active-validation diagnostics from the repository root:

```powershell
python build/python/cli/buildctl.py doctor --quick
python build/python/cli/buildctl.py validation-status --summary
```

Doctor skips restore in quick mode and exits nonzero for warnings as well as errors; read the
reported capability before deciding which prerequisites the selected check needs.

Follow the [contributor validation path](../engineering/README.md#contributor-validation-path) to
choose `verify-dotnet`, `verify-browser`, `verify-docs`, or `verify-workflows` and install that lane's
dependencies. For a local .NET edit/test loop, use the contention-aware runner with a focused filter:

```powershell
python build/python/cli/buildctl.py test --project tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~<TestClassOrMethod>" --queue
```

After a lane fails, open `artifacts/ci-summary/<lane>/summary.md` for the failed step and reproduction
commands. Correct the failure, rerun that lane, then run `bash scripts/ci.sh` before handing off
PR-ready work. GitHub Actions remains the authoritative merge result.

If local machine limits make a lane unreliable, follow the
[recovery and hosted Targeted Test guidance](../engineering/README.md#buildtestrun).
Once the branch is pushed, use the validated dispatcher for a focused hosted run:

```powershell
python build/scripts/ci/dispatch-targeted-test.py --ref <branch> --mode dotnet-filtered --dotnet-project tests/Meridian.Tests/Meridian.Tests.csproj --dotnet-filter "FullyQualifiedName~<TestClassOrMethod>" --wait
```

The full [engineering validation guide](../engineering/README.md#buildtestrun) also covers persistent
build profiles and per-surface lanes for WPF, schema control, integration, and performance checks.

## Choose Your Next Path

| Audience | Continue with |
| --- | --- |
| Developer or coding agent | [Engineering](../engineering/README.md) |
| Product/stakeholder reviewer | [Product](../product/README.md) |
| Operator or support user | [Operators](../operators/README.md) |
| API/config/provider lookup | [Reference](../reference/README.md) |
| Assistant-safe execution | [AI workflow](../ai/assistant-workflow-contract.md) |

## Historical Setup Material

The pinned pages below preserve earlier setup guidance. Use this page and the audience paths above for current commands. The active [command help](../HELP.md) is maintained separately.

- [Getting Started](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/getting-started/README.md)
- [Developer Setup](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/developer/setup.md)
- [Build, Test, Run](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/developer/build-test-run.md)
- [Pilot Operator Quickstart](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/getting-started/pilot-operator-quickstart.md)

CI/CD validation ownership and administrator rollout are maintained in
[CI/CD ownership and rollout](../engineering/ci-cd-optimization.md). Meridian CI owns the four canonical
`scripts/ci.sh` lanes; legacy CI owns Secret Scan and nightly/manual coverage.
The four-lane `Meridian CI / quality-gate` keeps its identity. The always-reported
`Meridian CI / integration-gate` companion reports service-backed PostgreSQL tests shared with
Production Certification, including pull requests and merge groups.
All CI governance changes require human review.
