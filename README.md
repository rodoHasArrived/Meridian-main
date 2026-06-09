# Meridian

Meridian is a .NET 10 fund-management and trading-platform codebase in active delivery. It brings market and account data, research, paper validation, books, reconciliation, approvals, and governed reports into one evidence-backed investment-operations workflow.

Meridian is intended to be the system a fund team can use to run operations with confidence: fewer disconnected tools, fewer manual handoffs, retained source evidence, and a clear chain from data intake to decisions, accounting records, reconciliation, and reports.

> **Active operator UI lanes:** Both the Windows desktop shell (`src/Meridian.Wpf/`) and the browser workstation (`src/Meridian.Ui/dashboard/`) are first-class operator surfaces. New behavior should land behind shared contracts, local/web API endpoints, or shared read models before either client grows UI-specific logic.

Current local project path for this workspace: `D:\Meridian-main`.

## Start Here

Use the documentation front door and audience paths for current guidance:

- [Documentation Index](docs/README.md)
- [Start Here](docs/start/README.md)
- [Product Direction](docs/product/README.md)
- [Design Charter](docs/product/meridian-design-document.md)
- [Engineering Guide](docs/engineering/README.md)
- [Operator Guide](docs/operators/README.md)
- [Reference Lookup](docs/reference/README.md)
- [Roadmap Registry](docs/roadmap/README.md)
- [Source Documentation Mesh](docs/source/README.md)
- [AI Guide](docs/ai/README.md)
- [Documentation Ownership Contract](docs/documentation-ownership.md)

## Current Product Direction

The active delivery focus is the W1-W5 operational record baseline:

- trusted data confidence and retained source evidence,
- research and paper-first validation,
- account, portfolio, cash, and ledger workflows,
- reconciliation between Meridian records and external statements,
- approvals, period governance, and audit trails,
- governed report packs and export evidence,
- visible operator navigation limited to `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`.

Deferred unless the roadmap explicitly reopens them: broad Backtesting Studio expansion, live-readiness beyond paper-first governance, full treasury payment execution, enterprise risk, client portal, no-code workflow design, mobile applications, and other lanes that do not directly strengthen the operational-record workflow.

## Repository Map

Major solution areas include:

| Area | Paths |
| --- | --- |
| Host, application, domain, contracts, infrastructure, and storage | `src/Meridian`, `src/Meridian.Application`, `src/Meridian.Domain`, `src/Meridian.Core`, `src/Meridian.Contracts`, `src/Meridian.Infrastructure`, `src/Meridian.Storage` |
| Providers, execution, risk, and strategies | `src/Meridian.ProviderSdk`, `src/Meridian.Execution`, `src/Meridian.Execution.Sdk`, `src/Meridian.Risk`, `src/Meridian.Strategies` |
| Backtesting and replay | `src/Meridian.Backtesting`, `src/Meridian.Backtesting.Sdk` |
| Accounting and F# domain work | `src/Meridian.Ledger`, `src/Meridian.FSharp.Ledger`, `src/Meridian.FSharp.DirectLending.Aggregates` |
| Quant tooling | `src/Meridian.QuantScript` |
| MCP tooling | `src/Meridian.Mcp` |
| Browser workstation and shared UI/API read models | `src/Meridian.Ui/dashboard`, `src/Meridian.Ui/wwwroot/workstation`, `src/Meridian.Ui.Services`, `src/Meridian.Ui.Shared` |
| Windows desktop workstation | `src/Meridian.Wpf` |
| Tests and benchmarks | `tests/`, `benchmarks/` |

For maintained ownership and dependency boundaries, use [Project Structure](docs/architecture/project-structure.md), [Module Map](docs/architecture/module-map.md), and the generated [repository structure](docs/generated/repository-structure.md).

## Quick Commands

Start with the maintained command references when in doubt: [Start Here](docs/start/README.md), [Engineering Guide](docs/engineering/README.md), [Operator Guide](docs/operators/README.md), and [docs/HELP.md](docs/HELP.md).

```bash
make help
make bootstrap
make verify-fast
make verify-full
make verify-docs
make verify-desktop
make verify-release
```

### Main CLI host — `src/Meridian`

The primary runnable project supports collector, desktop-local API host, setup, self-test, backfill, provider recommendation, symbol-management, and ledger-reporting modes.

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --help
dotnet run --project src/Meridian/Meridian.csproj -- --setup
dotnet run --project src/Meridian/Meridian.csproj -- --mode desktop --http-port 8080
dotnet run --project src/Meridian/Meridian.csproj -- --backfill --backfill-symbols AAPL,MSFT --backfill-from 2024-01-01 --backfill-to 2024-12-31
dotnet run --project src/Meridian/Meridian.csproj -- ledger -f ledger.dat balance
```

When launched from the repository root, the desktop-local API host binds to `http://localhost:8080` by default. Config path resolution is `--config <path>` → `MDC_CONFIG_PATH` → `config/appsettings.json`.

### Browser workstation — `src/Meridian.Ui/dashboard`

The browser workstation builds static assets served from `src/Meridian.Ui/wwwroot/workstation/`.

```bash
cd src/Meridian.Ui/dashboard
npm install
npm run dev
npm run preview
npm run test
npm run build
```

`npm run dev` serves `/workstation/`; `npm run preview` serves the built workstation assets. Both commands proxy `/api` to `MERIDIAN_API_BASE_URL` when set, or `http://localhost:8080` by default.

### MCP server — `src/Meridian.Mcp`

A lightweight Model Context Protocol server for repo-navigation and code-review AI tooling. Diagnostic output goes to stderr; stdout is reserved for the MCP protocol.

```bash
dotnet run --project src/Meridian.Mcp/Meridian.Mcp.csproj
```

### Windows WPF desktop app — `src/Meridian.Wpf`

The WPF desktop shell is an active Windows operator workstation. It shares contracts, read models, and API seams with the browser workstation. On non-Windows, the project builds as a stub for CI compatibility unless the full WPF build flag is enabled on Windows.

```bash
pwsh ./scripts/dev/run-desktop.ps1
```

Manual fallback:

```bash
dotnet run --project src/Meridian.Wpf/Meridian.Wpf.csproj /p:EnableFullWpfBuild=true
```

## Validation Lanes

| Lane | Use when | Command |
| --- | --- | --- |
| `bootstrap` | Setting up or repairing a local development environment. | `make bootstrap` |
| `verify-fast` | Tight-loop validation for most code changes. | `make verify-fast` |
| `verify-full` | Broad pre-PR validation and coverage collection. | `make verify-full` |
| `verify-docs` | Changing docs, workflow docs, AI/TODO governance references, or lane vocabulary. | `make verify-docs` |
| `verify-desktop` | Touching WPF desktop shell, routing, or shared contracts that need desktop confidence. | `make verify-desktop` |
| `verify-release` | Validating publish outputs or release packaging paths. | `make verify-release` |

Build safety notes:

- Prefer one solution or project build at a time when multiple entrypoints share referenced projects.
- Use `python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Release` for a restore-once, single-node solution build.
- For automation or concurrent local runs, pass `--isolation-key <name>` to `buildctl.py` so outputs write under isolated `artifacts/bin/<name>/` and `artifacts/obj/<name>/` roots.
- Keep the F# test project's transitive `xunit.v3` runtime pin aligned with `xunit.runner.visualstudio`; Linux/macOS VSTest discovery depends on the v3 JSON handshake.

## Planning Source of Truth

Use these documents together when planning or implementing new work:

- [Roadmap Registry](docs/roadmap/README.md) and `docs/roadmap/data/*.yml` for active wave and gate records.
- [Generated Roadmap Summary](docs/roadmap/generated/ROADMAP_SUMMARY.md) for rendered roadmap state.
- [Design Charter](docs/product/meridian-design-document.md) for the canonical product framing and evidence-backed operations model.
- [Provider Capability Matrix](docs/reference/provider-capability-matrix.md) and [Provider Validation Matrix](docs/reference/provider-validation-matrix.md) for provider-confidence scope.
- [Engineering Guide](docs/engineering/README.md) for execution architecture and shared-model guidance.
- [Operator Guide](docs/operators/README.md) for governance and support posture.
- [Source Documentation Mesh](docs/source/README.md) and `docs/source/data/source-modules.yml` for source README ownership and module traceability.

<!-- readme-tree start -->

The full repository tree is generated in [docs/generated/repository-structure.md](docs/generated/repository-structure.md). For maintained ownership and dependency boundaries, use [docs/architecture/project-structure.md](docs/architecture/project-structure.md) and [docs/architecture/module-map.md](docs/architecture/module-map.md).

<!-- readme-tree end -->
