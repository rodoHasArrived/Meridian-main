# Meridian

Meridian is a .NET 10 fund-management and trading-platform codebase in active delivery. The current solution spans market-data ingestion and backfill, tiered storage, backtesting, execution and risk seams, portfolio and ledger workflows, QuantScript tooling, MCP surfaces, a local API host, a Windows WPF desktop workstation shell, and a browser-based workstation dashboard. The current delivery focus is turning that breadth into an evidence-backed investment operations product: trusted data, research, paper validation, books, reconciliation, approvals, and governed reports in one explainable chain.

> **Active operator UI lanes:** Both the desktop and browser workstations are first-class operator surfaces. The Windows desktop shell (`src/Meridian.Wpf/`) and the browser workstation (`src/Meridian.Ui/dashboard/`) are developed in parallel. New product behavior should land behind shared contracts, local/web API endpoints, or shared read models before expanding into either client.

Current local project path: `D:\Meridian-main`.

## Start Here

- [Documentation Index](docs/README.md)
- [Project Structure](docs/architecture/project-structure.md)
- [Module Map](docs/architecture/module-map.md)
- [Developer Setup](docs/developer/setup.md)
- [Build, Test, Run](docs/developer/build-test-run.md)
- [Publish Standalone EXE](docs/developer/publish-standalone-exe.md)
- [Project Roadmap](docs/status/ROADMAP.md)
- [Current Direction and Status](docs/plans/current-direction-and-status.md)
- [Feature Inventory](docs/status/FEATURE_INVENTORY.md)
- [Improvements Tracker](docs/status/IMPROVEMENTS.md)
- [Cleanup and Maintenance](docs/operations/cleanup-and-maintenance.md)
- [Prompt and Agent Guidance](docs/prompts/repo-maintenance-prompts.md)
- [Evidence-Backed Investment Operations Plan](docs/plans/evidence-backed-investment-operations-plan.md)
- [Web UI Development Pivot](docs/plans/web-ui-development-pivot.md)
- [Roadmap Registry](docs/roadmap/README.md)
- [Source Documentation Mesh](docs/source/README.md)

## Current Product Direction

Meridian's intended end state is a self-hosted fund management system where operators can move through one connected lifecycle:

- discover and validate data
- run research and compare results
- manage accounts, entities, and strategy structures
- implement portfolio decisions and trade workflows
- inspect portfolio and ledger outcomes
- model cash movement and trial-balance state
- reconcile internal and external records
- generate governance, investor, and compliance reports
- promote safely into paper and later live workflows

## Solution Map

The solution currently includes these major areas:

- `src/Meridian`, `src/Meridian.Application`, `src/Meridian.Domain`, `src/Meridian.Core`, `src/Meridian.Contracts`, `src/Meridian.Infrastructure`, and `src/Meridian.Storage` for the main host plus core application/domain/infrastructure/storage layers
- `src/Meridian.ProviderSdk`, `src/Meridian.Execution`, `src/Meridian.Execution.Sdk`, `src/Meridian.Risk`, and `src/Meridian.Strategies` for provider, execution, risk, and strategy seams
- `src/Meridian.Backtesting` and `src/Meridian.Backtesting.Sdk` for replay and backtesting
- `src/Meridian.Ledger`, `src/Meridian.FSharp.Ledger`, and `src/Meridian.FSharp.DirectLending.Aggregates` for accounting and direct-lending/domain-specialized work
- `src/Meridian.QuantScript` for scripting and charting-oriented tooling
- `src/Meridian.Mcp` and `src/Meridian.McpServer` for Model Context Protocol integration surfaces
- `src/Meridian.Ui/dashboard`, `src/Meridian.Ui/wwwroot/workstation`, `src/Meridian.Ui.Services`, and `src/Meridian.Ui.Shared` for the active web workstation and shared UI/API layers
- `src/Meridian.Wpf` for the active Windows desktop workstation shell
- `tests/` and `benchmarks/` for automated validation and performance work

## Verified Entry Points

### Main CLI host — `src/Meridian`

The primary runnable project. Supports multiple modes via `--mode <mode>`:

| Mode / flag | What runs |
| --- | --- |
| `--mode desktop` | Collector + desktop-local API host on `http://localhost:8080` |
| `--mode headless` | Collector only, no HTTP server |
| `--backfill` | Historical data backfill (combine with `--backfill-provider`, `--backfill-symbols`, `--backfill-from`, `--backfill-to`) |
| `--selftest` | Wiring self-test; exits with pass/fail |
| `--simulate-feed` | Synthetic feed simulation for offline development |
| `--validate-config` / `--check-config` | Config validation without starting any services |
| `--recommend-providers` | Print a provider recommendation report and exit |
| `--symbols` / `--symbols-add` / `--symbols-remove` | Symbol management commands |
| `--dry-run` | Validate configuration and connectivity without side effects |
| `--setup` / `--first-run` / `--quickstart` | First-run bootstrap: auto-detect providers, validate credentials when available, and write `config/appsettings.json` |
| `ledger -f <journal-file> <report>` | Read a Ledger-compatible journal and print balance/register/accounts reports |

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --help
dotnet run --project src/Meridian/Meridian.csproj -- --setup
dotnet run --project src/Meridian/Meridian.csproj -- --help ledger
dotnet run --project src/Meridian/Meridian.csproj -- --mode desktop --http-port 8080
dotnet run --project src/Meridian/Meridian.csproj -- --backfill --backfill-symbols AAPL,MSFT --backfill-from 2024-01-01 --backfill-to 2024-12-31
dotnet run --project src/Meridian/Meridian.csproj -- ledger -f ledger.dat balance
```

When you launch the desktop-local API host from the repository root, Meridian binds to `http://localhost:8080` by default, so you do not need to `cd` into `src/Meridian` first.

Config path resolution: `--config <path>` → `MDC_CONFIG_PATH` env var → `config/appsettings.json`.

### Web workstation dashboard — `src/Meridian.Ui/dashboard`

The browser-based operator dashboard is one of Meridian's active operator UI lanes. It builds
static workstation assets that are served from `src/Meridian.Ui/wwwroot/workstation/`.

```bash
cd src/Meridian.Ui/dashboard
npm install
npm run dev
npm run preview
npm run test
npm run build
```

`npm run dev` serves the shell at `/workstation/`; `npm run preview` serves the built assets from
`src/Meridian.Ui/wwwroot/workstation/`. Both commands proxy `/api` to `MERIDIAN_API_BASE_URL` when
set, or `http://localhost:8080` by default. When the local API host is not running, development
builds fall back to typed fixture data for the initial dashboard bootstrap GETs only; command and
mutation workflows still require the Meridian API.

Current web evidence support includes the `/reporting/evidence` workbench plus shared evidence
APIs for subjects, packet/graph inspection, validation, manifest export, and retained manifest
lookup through the evidence-vault index. Treat that as browser-visible support for run, readiness,
reconciliation, report-pack, provider-trust, and export evidence, not as completion of the full
Evidence Vault or report-line provenance roadmap.

Current browser workstation support also includes Overview portfolio-at-a-glance and Today-panel
market posture with view-model owned status-banner and refresh-command presentation, Portfolio position and run-evidence tables
that use shared dense row-selection/detail-panel semantics plus brokerage-sync next actions into
provider repair, Trading readiness, and cockpit review, Live Quotes recent-trade rows with a
selectable dense-table detail inspector, quick-trade accepted/rejected ticket states that hand off
to Trading readiness, browser Watchlist empty/no-quote/busy labels owned by the view model,
local Price Alerts at `/data/alerts` with UTC trigger/poll timing, richer
sidebar/menu/command grouping with grouped command-palette sections, app-shell query-string
route focus for subject/symbol handoffs such as `/data/quotes?symbol=AAPL`, and hash-aware
`/settings#alpaca-provider-setup` routing, browser Security Master search-result selection,
identity drill-ins, details/lots/operator overrides with selectable lot-detail state plus a
conflict refresh/retry command, provider-setup success handoffs into live quotes, backfill preview
with dense-table queue selection, Trading readiness, or Security Master coverage based on
configured capabilities, a full-console Operator Readiness checkpoint gate whose BrokerageSync blockers
route to provider setup repair, a view-model-owned
Trading loading panel plus selectable Recent Fills detail state, Quant Lab plot rendering through
a reusable view-model module, Quant Notebook cell/data-fetch helpers, a visual
Strategy Designer at `/strategy/designer`, Covered Call support at `/strategy/covered-call`
with dense chain preview, saved-run history, and selectable trade-timeline detail state, browser Accounting reconciliation detail-queue dense-table selection
with keyboard-accessible expanded rows, seeded no-host break-queue fixtures, and accessible
empty/detail states, Reporting
report-pack profile actions for preview and export analysis plus endpoint links that separate
concrete GET navigation from reference-only POST mutations, and expanded UFL/reference-data
endpoints for bonds, options, equities, futures, FX spot, swaps, commodities, crypto, deposits,
money-market funds, and certificates of deposit. Shared
UI-service JSON persistence now routes config, export preset, and quality archive writes through
`AtomicFileWriter`, and the Meridian Design System now has reference workbench and tokenized-color
support evidence. Treat these as support evidence for Waves 2-4, not completion of durable
reconciliation casework, report-pack, cockpit, or live-readiness gates.

Current governance support also includes ledger `posting_kind` preservation and closed-period
posting guards, structured report-pack validation/lifecycle metadata, hardened reconciliation case
storage/audit/status transitions, account-sync history/readiness DTOs, a Security Master
validation-gate/snapshot slice, and a shared operations-continuity workflow for account-period close
lanes. The continuity API covers broker import/normalization, Security Master resolution and
override approval, ledger draft/validate/post, reconciliation, approval, close, reopen, hash-chained
timeline reads, trusted-actor mutation handling, and optional transactional commit of journal,
audit, and workflow snapshot writes. These are Wave 3/Wave 4 support evidence, not approval of live
trading, report publication, full close workflow readiness, or Evidence Vault completion.

Current browser workflow hardening also includes provider-routing visibility after provider setup:
the Data setup dialog reports created connection, binding, credential-source, environment, and
warning metadata, and Settings refreshes provider-routing connections, bindings, and trust snapshots
after setup and during its lightweight refresh cycle. Strategy Designer backend actions now
distinguish browser-openable GET routes from reference-only POST mutations for validation, preview,
and backtest commands. Reporting export commands now pass abort signals and suppress superseded
same-profile or profile-switch export results. Treat these as operator-safety support evidence, not
completion of provider governance, Backtest Studio, or governed report-pack lifecycle gates.

Current May 20 hardening adds fail-closed brokerage order-placement gates, ledger/promotion
endpoint authorization checks, execution metadata sanitization, web-workstation installer repair
for preserved configs that point at missing provider sections, browser session/role API recovery
messages, and accessible disabled-field support text. Security Master accounting support also now
treats MBS, ABS, loan, and amortizing-loan instruments as factor-schedule fixed-income families.
Treat these as risk-reduction and operator-trust support evidence, not W2-W4 readiness exits.

Current May 21 support evidence adds a shared Strategy Engine pre-run foundation: explicit strategy
definitions, typed parameter schemas, data dependency policy, run-request validation, evidence
hashing, and workstation definitions/validate-run endpoints for Covered Call and visual designer
flows. The repo also now has structured roadmap and source documentation registries under
`docs/roadmap/` and `docs/source/`, generated source README coverage, stale-doc/hash validation,
and a provider capability matrix for adapter readiness. Treat these as contract, planning, and
documentation-control support evidence; they do not close cockpit, shared-continuity,
governance/reporting, or live-readiness gates.

### MCP server (minimal) — `src/Meridian.Mcp`

A lightweight [Model Context Protocol](https://modelcontextprotocol.io/) server. Loads tools, prompts, and resources from the assembly and communicates over stdio. Intended for repo-navigation and code-review AI tooling. All diagnostic output goes to stderr; stdout is reserved for the MCP protocol.

```bash
dotnet run --project src/Meridian.Mcp/Meridian.Mcp.csproj
```

### MCP server (market data) — `src/Meridian.McpServer`

A full-featured MCP server that exposes market data capabilities (provider queries, backfill, storage catalog, symbol management) as MCP tools, resources, and prompts. Lets LLMs interact with live provider data over the stdio transport.

```bash
dotnet run --project src/Meridian.McpServer/Meridian.McpServer.csproj -- --config config/appsettings.json
```

Config path resolution: `--config <path>` → `MDC_CONFIG_PATH` env var → `config/appsettings.json`.

### Windows WPF desktop app — `src/Meridian.Wpf`

The active Windows desktop workstation shell. Runs alongside the browser workstation as a first-class operator surface, sharing contracts, read models, and workstation API seams. Requires Windows and the full WPF build flag. On non-Windows the project builds as a stub for CI compatibility.

The desktop shell is built around feature-owned modules: the Data shell lives under `src/Meridian.Wpf/Features/Data/` with a feature registration module, snapshot service, presentation adapter, view model, and thin WPF page.

```bash
pwsh ./scripts/dev/run-desktop.ps1
```

The launcher starts the local Meridian host on `http://localhost:8080` if needed, waits for `/healthz`, opens the desktop shell, and shuts down the host when the desktop app exits.

Manual fallback:

```bash
dotnet run --project src/Meridian.Wpf/Meridian.Wpf.csproj /p:EnableFullWpfBuild=true
```


### Canonical validation lanes

| Lane | Use when | Maps to |
| --- | --- | --- |
| `bootstrap` | Setting up a fresh or repaired local dev environment. | `make setup-dev` |
| `verify-fast` | Pre-commit or tight-loop validation for most code changes. | `make pre-pr` |
| `verify-full` | Pre-PR broad validation and coverage collection. | `make pre-pr-full` |
| `verify-docs` | Changing docs, workflow docs, AI/TODO governance references, or lane vocabulary. | `make docs-lint`, `make check-workflow-docs-parity`, `make check-status-delivery-claims`, `python3 build/scripts/docs/check-known-lanes.py` |
| `verify-desktop` | Touching WPF desktop shell, routing, or shared contracts that need desktop confidence. | `make desktop-build`, `make desktop-test` |
| `verify-release` | Validating publish outputs or release packaging paths. | `make publish` |

### Makefile shortcuts

```bash
make help           # List all task targets
make bootstrap      # Canonical lane: local bootstrap
make verify-fast    # Canonical lane: fast validation
make verify-full    # Canonical lane: full validation + coverage
make verify-docs    # Canonical lane: docs/workflow/lane checks
make verify-desktop # Canonical lane: desktop validation
make verify-release # Canonical lane: publish validation
make build-quick    # Shared restore-once, sequential Debug build
npm run ui:dashboard:test   # Web workstation Vitest suite
npm run ui:dashboard:build  # Web workstation production build
pwsh ./scripts/dev/run-desktop.ps1  # WPF desktop + local host (Windows)
make desktop-build  # Build WPF desktop project
make desktop-test   # Run WPF desktop tests
make run            # Collector with config hot-reload (--mode desktop)
make run-backfill   # Historical backfill
make run-selftest   # Wiring self-test
make benchmark      # Full BenchmarkDotNet suite
make bench-quick    # Quick bottleneck benchmarks (~10 min)
make setup-dev      # One-shot local dev setup (hooks, config, restore, build)
```

Desktop workflow automation (workflow runs, manual generation, screenshot capture) is PowerShell-script based; see [docs/development/desktop-command-surface-migration.md](docs/development/desktop-command-surface-migration.md).

See [docs/HELP.md](docs/HELP.md) for the full operator/developer quick reference including environment variables, configuration schema, and provider credential setup.

Build safety note:

- Prefer one solution or project build at a time when multiple entrypoints share the same referenced projects.
- `python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Release` now restores once and builds with a single MSBuild node.
- For automation or concurrent local runs, pass `--isolation-key <name>` so the build graph
  writes under `artifacts/bin/<name>/` and `artifacts/obj/<name>/` instead of shared project
  `bin/obj` folders. `buildctl.py` prunes stale isolated output directories older than 14 days
  and trims excess same-day output beyond the latest 10 runs per artifact root before isolated
  builds; use `--isolation-retention-days <days>` and `--isolation-retain-latest <count>` to tune
  those limits, or set both to `0` to disable cleanup for a run.
- Keep the F# test project's transitive `xunit.v3` runtime pin aligned with
  `xunit.runner.visualstudio`; Linux/macOS VSTest discovery depends on the v3 JSON handshake.
- For standalone publish-size checks, use `pwsh ./build/scripts/publish/publish.ps1 -Platform win-x64 -Project web-workstation -SizeOptimized -OutputDir artifacts/publish/local-size`, then inspect common output roots with `pwsh ./build/scripts/publish/measure-size.ps1`.

## Planning Source of Truth

Use these documents together when planning or implementing new work:

- [docs/plans/current-direction-and-status.md](docs/plans/current-direction-and-status.md) for the consolidated current direction, status, and plan-file roles
- [docs/status/ROADMAP_COMBINED.md](docs/status/ROADMAP_COMBINED.md) for the shortest roadmap and target-state summary
- [docs/status/ROADMAP.md](docs/status/ROADMAP.md) for the canonical wave order: Wave 1 provider confidence and checkpoint evidence, Wave 2 paper-trading cockpit hardening, Wave 3 shared run / portfolio / ledger continuity, and Wave 4 governance and fund-operations productization on top of the delivered Security Master baseline
- [docs/plans/evidence-backed-investment-operations-plan.md](docs/plans/evidence-backed-investment-operations-plan.md) for the product-category filter, commercial differentiation bets, sequencing rule, and archive-placement rule
- [docs/plans/meridian-6-week-roadmap.md](docs/plans/meridian-6-week-roadmap.md) for the current time-boxed Waves 1-4 execution slice
- [docs/status/provider-validation-matrix.md](docs/status/provider-validation-matrix.md) for the current provider-confidence evidence gate; the completed Wave 1 blueprint is archived at [archive/docs/plans/provider-reliability-data-confidence-wave-1-blueprint.md](archive/docs/plans/provider-reliability-data-confidence-wave-1-blueprint.md)
- [docs/plans/trading-workstation-migration-blueprint.md](docs/plans/trading-workstation-migration-blueprint.md) for the Wave 2-3 workstation and shared-model implementation shape; the evidence-backed investment-operations plan supersedes older generic workstation positioning
- [docs/plans/governance-fund-ops-blueprint.md](docs/plans/governance-fund-ops-blueprint.md) for Wave 4 governance and fund-operations depth on top of the delivered Security Master baseline
- [docs/status/FEATURE_INVENTORY.md](docs/status/FEATURE_INVENTORY.md) for current-vs-target capability status
- [docs/status/IMPROVEMENTS.md](docs/status/IMPROVEMENTS.md) for tracked implementation themes
<!-- readme-tree start -->

The full repository tree is generated in [docs/generated/repository-structure.md](docs/generated/repository-structure.md). For maintained ownership and dependency boundaries, use [docs/architecture/project-structure.md](docs/architecture/project-structure.md) and [docs/architecture/module-map.md](docs/architecture/module-map.md).

<!-- readme-tree end -->

