# Meridian

Meridian is a .NET 10 fund-management and trading-platform codebase in active delivery. The current solution spans market-data ingestion and backfill, tiered storage, backtesting, execution and risk seams, portfolio and ledger workflows, QuantScript tooling, MCP surfaces, a local API host, a retained Windows WPF workstation shell, and a browser-based workstation dashboard. The current delivery focus is turning that breadth into an evidence-backed investment operations product: trusted data, research, paper validation, books, reconciliation, approvals, and governed reports in one explainable chain.

> **Web UI active direction:** New operator UI development is focused on `src/Meridian.Ui/dashboard/` and the built `src/Meridian.Ui/wwwroot/workstation/` assets. `src/Meridian.Wpf/` is retained for compatibility, regression fixes, and shared-contract support rather than new desktop-first feature work.

Current local project path: `C:\Dev\Meridian-main`.

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
- `src/Meridian.Wpf` for the retained Windows desktop shell
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

The browser-based operator dashboard is the active UI delivery lane. It builds static workstation
assets that are served from `src/Meridian.Ui/wwwroot/workstation/`.

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
APIs for subjects, packet/graph inspection, validation, and manifest export. Treat that as
browser-visible support for run, readiness, reconciliation, report-pack, provider-trust, and export
evidence, not as completion of the full Evidence Vault or report-line provenance roadmap.

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

### Retained Windows WPF desktop app — `src/Meridian.Wpf`

The retained Windows workstation shell. Use it for compatibility support, regression fixes, and shared-contract validation. Requires Windows and the full WPF build flag. On non-Windows the project builds as a stub for CI compatibility.

The retained shell is also moving compatibility work toward feature-owned modules: the Data shell now lives under `src/Meridian.Wpf/Features/Data/` with a feature registration module, snapshot service, presentation adapter, view model, and thin WPF page.

```bash
pwsh ./scripts/dev/run-desktop.ps1
```

The launcher starts the local Meridian host on `http://localhost:8080` if needed, waits for `/healthz`, opens the desktop shell, and shuts down the host when the desktop app exits.

Manual fallback:

```bash
dotnet run --project src/Meridian.Wpf/Meridian.Wpf.csproj /p:EnableFullWpfBuild=true
```

### Makefile shortcuts

```bash
make help           # List all task targets
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
```
.
├── .agents
│   └── skills
│       ├── _shared
│       │   └── project-context.md
│       ├── meridian-archive-organizer
│       │   ├── SKILL.md
│       │   ├── agents
│       │   │   └── openai.yaml
│       │   ├── evals
│       │   │   └── evals.json
│       │   ├── fixtures
│       │   │   └── superseded-adr
│       │   │       └── docs
│       │   │           ├── adr
│       │   │           │   ├── ADR-015-platform-restructuring.md
│       │   │           │   └── README.md
│       │   │           └── generated
│       │   │               └── repository-structure.md
│       │   ├── references
│       │   │   ├── archive-placement-guide.md
│       │   │   └── evaluation-harness.md
│       │   └── scripts
│       │       ├── run_evals.py
│       │       ├── score_eval.py
│       │       └── trace_archive_candidates.py
│       ├── meridian-blueprint
│       │   ├── CHANGELOG.md
│       │   ├── SKILL.md
│       │   └── references
│       │       ├── blueprint-patterns.md
│       │       └── pipeline-position.md
│       ├── meridian-brainstorm
│       │   ├── CHANGELOG.md
│       │   ├── SKILL.md
│       │   ├── brainstorm-history.jsonl
│       │   └── references
│       │       ├── competitive-landscape.md
│       │       └── idea-dimensions.md
│       ├── meridian-code-review
│       │   ├── CHANGELOG.md
│       │   ├── SKILL.md
│       │   ├── agents
│       │   │   └── grader.md
│       │   ├── eval-viewer
│       │   │   ├── generate_review.py
│       │   │   └── viewer.html
│       │   ├── evals
│       │   │   ├── benchmark_baseline.json
│       │   │   └── evals.json
│       │   ├── references
│       │   │   ├── architecture.md
│       │   │   └── schemas.md
│       │   └── scripts
│       │       ├── __init__.py
│       │       ├── aggregate_benchmark.py
│       │       ├── package_skill.py
│       │       ├── quick_validate.py
│       │       ├── run_eval.py
│       │       └── utils.py
│       ├── meridian-implementation-assurance
│       │   ├── SKILL.md
│       │   ├── references
│       │   │   ├── documentation-routing.md
│       │   │   └── evaluation-harness.md
│       │   └── scripts
│       │       ├── doc_route.py
│       │       └── score_eval.py
│       ├── meridian-provider-builder
│       │   ├── CHANGELOG.md
│       │   ├── SKILL.md
│       │   └── references
│       │       └── provider-patterns.md
│       ├── meridian-repo-navigation
│       │   ├── SKILL.md
│       │   └── agents
│       │       └── openai.yaml
│       ├── meridian-roadmap-strategist
│       │   ├── SKILL.md
│       │   ├── agents
│       │   │   └── openai.yaml
│       │   └── references
│       │       └── roadmap-source-map.md
│       ├── meridian-simulated-user-panel
│       │   ├── SKILL.md
│       │   ├── agents
│       │   │   └── grader.md
│       │   ├── assets
│       │   │   ├── bundles
│       │   │   │   ├── roadmap-review.manifest.json
│       │   │   │   ├── screen-review.manifest.json
│       │   │   │   ├── ship-readiness.manifest.json
│       │   │   │   └── workflow-walkthrough.manifest.json
│       │   │   ├── eval-result.schema.json
│       │   │   └── review-manifest.schema.json
│       │   ├── evals
│       │   │   ├── benchmark_baseline.json
│       │   │   ├── evals.json
│       │   │   └── golden
│       │   │       ├── eval-01-welcome-onboarding-design-partner.md
│       │   │       ├── eval-02-provider-onboarding-release-gate.md
│       │   │       ├── eval-03-fund-ledger-controls-review.md
│       │   │       ├── eval-04-analysis-export-power-user-review.md
│       │   │       ├── eval-05-research-promotion-roadmap-review.md
│       │   │       └── eval-06-provider-health-usability-lab.md
│       │   ├── references
│       │   │   ├── artifact-bundles.md
│       │   │   ├── personas.md
│       │   │   ├── review-contract.md
│       │   │   ├── review-modes.md
│       │   │   └── sample-prompts.md
│       │   └── scripts
│       │       ├── __init__.py
│       │       └── run_eval.py
│       └── meridian-test-writer
│           ├── CHANGELOG.md
│           ├── SKILL.md
│           └── references
│               └── test-patterns.md
├── .artifacts
│   └── link-repair-report.md
├── .claude
│   ├── agents
│   │   ├── meridian-archive-organizer.md
│   │   ├── meridian-blueprint.md
│   │   ├── meridian-cleanup.md
│   │   ├── meridian-docs.md
│   │   ├── meridian-navigation.md
│   │   ├── meridian-repo-navigation.md
│   │   ├── meridian-roadmap-strategist.md
│   │   └── meridian-user-panel.md
│   ├── plugins
│   │   ├── csharp-dotnet-development
│   │   │   ├── .github
│   │   │   │   └── plugin
│   │   │   │       └── plugin.json
│   │   │   ├── README.md
│   │   │   ├── agents
│   │   │   │   └── expert-dotnet-software-engineer.md
│   │   │   └── skills
│   │   │       ├── aspnet-minimal-api-openapi
│   │   │       │   └── SKILL.md
│   │   │       ├── csharp-async
│   │   │       │   └── SKILL.md
│   │   │       ├── csharp-mstest
│   │   │       │   └── SKILL.md
│   │   │       ├── csharp-nunit
│   │   │       │   └── SKILL.md
│   │   │       ├── csharp-tunit
│   │   │       │   └── SKILL.md
│   │   │       ├── csharp-xunit
│   │   │       │   └── SKILL.md
│   │   │       ├── dotnet-best-practices
│   │   │       │   └── SKILL.md
│   │   │       └── dotnet-upgrade
│   │   │           └── SKILL.md
│   │   └── frontend-web-dev
│   │       ├── .github
│   │       │   └── plugin
│   │       │       └── plugin.json
│   │       ├── README.md
│   │       ├── agents
│   │       │   ├── electron-angular-native.md
│   │       │   └── expert-react-frontend-engineer.md
│   │       └── skills
│   │           ├── playwright-explore-website
│   │           │   └── SKILL.md
│   │           └── playwright-generate-test
│   │               └── SKILL.md
│   ├── settings.json
│   ├── settings.local.json
│   └── skills
│       ├── _shared
│       │   └── project-context.md
│       ├── meridian-archive-organizer
│       │   ├── SKILL.md
│       │   ├── agents
│       │   │   └── openai.yaml
│       │   ├── evals
│       │   │   └── evals.json
│       │   ├── fixtures
│       │   │   └── superseded-adr
│       │   │       └── docs
│       │   │           ├── adr
│       │   │           │   ├── ADR-015-platform-restructuring.md
│       │   │           │   └── README.md
│       │   │           └── generated
│       │   │               └── repository-structure.md
│       │   ├── references
│       │   │   ├── archive-placement-guide.md
│       │   │   └── evaluation-harness.md
│       │   └── scripts
│       │       ├── run_evals.py
│       │       ├── score_eval.py
│       │       └── trace_archive_candidates.py
│       ├── meridian-blueprint
│       │   ├── CHANGELOG.md
│       │   ├── SKILL.md
│       │   └── references
│       │       ├── blueprint-patterns.md
│       │       └── pipeline-position.md
│       ├── meridian-brainstorm
│       │   ├── CHANGELOG.md
│       │   ├── SKILL.md
│       │   ├── brainstorm-history.jsonl
│       │   └── references
│       │       ├── competitive-landscape.md
│       │       └── idea-dimensions.md
│       ├── meridian-code-review
│       │   ├── CHANGELOG.md
│       │   ├── SKILL.md
│       │   ├── agents
│       │   │   └── grader.md
│       │   ├── eval-viewer
│       │   │   ├── generate_review.py
│       │   │   └── viewer.html
│       │   ├── evals
│       │   │   ├── benchmark_baseline.json
│       │   │   └── evals.json
│       │   ├── references
│       │   │   ├── architecture.md
│       │   │   └── schemas.md
│       │   └── scripts
│       │       ├── __init__.py
│       │       ├── aggregate_benchmark.py
│       │       ├── package_skill.py
│       │       ├── quick_validate.py
│       │       ├── run_eval.py
│       │       └── utils.py
│       ├── meridian-implementation-assurance
│       │   ├── SKILL.md
│       │   ├── references
│       │   │   ├── documentation-routing.md
│       │   │   └── evaluation-harness.md
│       │   └── scripts
│       │       ├── doc_route.py
│       │       └── score_eval.py
│       ├── meridian-provider-builder
│       │   ├── CHANGELOG.md
│       │   ├── SKILL.md
│       │   └── references
│       │       └── provider-patterns.md
│       ├── meridian-repo-navigation
│       │   ├── SKILL.md
│       │   └── agents
│       │       └── openai.yaml
│       ├── meridian-roadmap-strategist
│       │   ├── SKILL.md
│       │   ├── agents
│       │   │   └── openai.yaml
│       │   └── references
│       │       └── roadmap-source-map.md
│       ├── meridian-simulated-user-panel
│       │   ├── SKILL.md
│       │   ├── agents
│       │   │   └── grader.md
│       │   ├── assets
│       │   │   ├── bundles
│       │   │   │   ├── roadmap-review.manifest.json
│       │   │   │   ├── screen-review.manifest.json
│       │   │   │   ├── ship-readiness.manifest.json
│       │   │   │   └── workflow-walkthrough.manifest.json
│       │   │   ├── eval-result.schema.json
│       │   │   └── review-manifest.schema.json
│       │   ├── evals
│       │   │   ├── benchmark_baseline.json
│       │   │   ├── evals.json
│       │   │   └── golden
│       │   │       ├── eval-01-welcome-onboarding-design-partner.md
│       │   │       ├── eval-02-provider-onboarding-release-gate.md
│       │   │       ├── eval-03-fund-ledger-controls-review.md
│       │   │       ├── eval-04-analysis-export-power-user-review.md
│       │   │       ├── eval-05-research-promotion-roadmap-review.md
│       │   │       └── eval-06-provider-health-usability-lab.md
│       │   ├── references
│       │   │   ├── artifact-bundles.md
│       │   │   ├── personas.md
│       │   │   ├── review-contract.md
│       │   │   ├── review-modes.md
│       │   │   └── sample-prompts.md
│       │   └── scripts
│       │       ├── __init__.py
│       │       └── run_eval.py
│       ├── meridian-test-writer
│       │   ├── CHANGELOG.md
│       │   ├── SKILL.md
│       │   └── references
│       │       └── test-patterns.md
│       └── skills_provider.py
├── .codex
│   ├── agents
│   │   ├── meridian-archive-organizer.toml
│   │   ├── meridian-blueprint.toml
│   │   ├── meridian-cleanup.toml
│   │   ├── meridian-docs.toml
│   │   ├── meridian-navigation.toml
│   │   ├── meridian-repo-navigation.toml
│   │   ├── meridian-roadmap-strategist.toml
│   │   └── meridian-user-panel.toml
│   ├── config.toml
│   ├── environments
│   │   ├── README.md
│   │   └── environment.toml
│   └── skills
│       ├── README.md
│       ├── _shared
│       │   └── project-context.md
│       ├── meridian-archive-organizer
│       │   ├── SKILL.md
│       │   ├── agents
│       │   │   └── openai.yaml
│       │   ├── evals
│       │   │   └── evals.json
│       │   ├── fixtures
│       │   │   └── superseded-adr
│       │   │       └── docs
│       │   │           ├── adr
│       │   │           │   ├── ADR-015-platform-restructuring.md
│       │   │           │   └── README.md
│       │   │           └── generated
│       │   │               └── repository-structure.md
│       │   ├── references
│       │   │   ├── archive-placement-guide.md
│       │   │   └── evaluation-harness.md
│       │   └── scripts
│       │       ├── run_evals.py
│       │       ├── score_eval.py
│       │       └── trace_archive_candidates.py
│       ├── meridian-blueprint
│       │   ├── SKILL.md
│       │   ├── agents
│       │   │   └── openai.yaml
│       │   └── references
│       │       └── blueprint-patterns.md
│       ├── meridian-brainstorm
│       │   ├── SKILL.md
│       │   ├── agents
│       │   │   └── openai.yaml
│       │   └── references
│       │       └── competitive-landscape.md
│       ├── meridian-cleanup
│       │   ├── SKILL.md
│       │   ├── agents
│       │   │   └── openai.yaml
│       │   └── scripts
│       │       └── repo-updater.ps1
│       ├── meridian-code-review
│       │   ├── SKILL.md
│       │   └── agents
│       │       └── openai.yaml
│       ├── meridian-implementation-assurance
│       │   ├── SKILL.md
│       │   ├── agents
│       │   │   └── openai.yaml
│       │   ├── evals
│       │   │   ├── benchmark_baseline.json
│       │   │   ├── evals.json
│       │   │   ├── meridian-implementation-assurance.prompts.csv
│       │   │   └── style-rubric.schema.json
│       │   ├── references
│       │   │   ├── documentation-routing.md
│       │   │   └── evaluation-harness.md
│       │   └── scripts
│       │       ├── doc_route.py
│       │       ├── run_evals.py
│       │       └── score_eval.py
│       ├── meridian-provider-builder
│       │   ├── SKILL.md
│       │   ├── agents
│       │   │   └── openai.yaml
│       │   └── references
│       │       └── provider-patterns.md
│       ├── meridian-repo-navigation
│       │   ├── SKILL.md
│       │   └── agents
│       │       └── openai.yaml
│       ├── meridian-roadmap-strategist
│       │   ├── SKILL.md
│       │   ├── agents
│       │   │   └── openai.yaml
│       │   └── references
│       │       └── roadmap-source-map.md
│       ├── meridian-simulated-user-panel
│       │   ├── SKILL.md
│       │   ├── agents
│       │   │   └── openai.yaml
│       │   ├── assets
│       │   │   ├── bundles
│       │   │   │   ├── roadmap-review.manifest.json
│       │   │   │   ├── screen-review.manifest.json
│       │   │   │   ├── ship-readiness.manifest.json
│       │   │   │   └── workflow-walkthrough.manifest.json
│       │   │   ├── eval-result.schema.json
│       │   │   └── review-manifest.schema.json
│       │   └── references
│       │       ├── artifact-bundles.md
│       │       ├── personas.md
│       │       ├── review-contract.md
│       │       └── review-modes.md
│       └── meridian-test-writer
│           ├── SKILL.md
│           ├── agents
│           │   └── openai.yaml
│           └── references
│               └── test-patterns.md
├── .devcontainer
│   ├── Dockerfile
│   ├── devcontainer.json
│   └── docker-compose.yml
├── .editorconfig
├── .flake8
├── .gitattributes
├── .githooks
│   └── pre-commit
├── .github
│   ├── ISSUE_TEMPLATE
│   │   ├── .gitkeep
│   │   ├── bug_report.yml
│   │   ├── config.yml
│   │   └── feature_request.yml
│   ├── PULL_REQUEST_TEMPLATE.md
│   ├── actions
│   │   └── setup-dotnet-cache
│   │       └── action.yml
│   ├── agents
│   │   ├── adr-generator.agent.md
│   │   ├── blueprint-agent.md
│   │   ├── brainstorm-agent.md
│   │   ├── bug-fix-agent.md
│   │   ├── cleanup-agent.md
│   │   ├── code-review-agent.md
│   │   ├── documentation-agent.md
│   │   ├── implementation-assurance-agent.md
│   │   ├── performance-agent.md
│   │   ├── provider-builder-agent.md
│   │   ├── repo-navigation-agent.md
│   │   ├── simulated-user-panel-agent.md
│   │   ├── software-engineer-agent-v1.agent.md
│   │   └── test-writer-agent.md
│   ├── copilot-instructions.md
│   ├── dependabot.yml
│   ├── instructions
│   │   ├── csharp.instructions.md
│   │   ├── docs.instructions.md
│   │   ├── dotnet-tests.instructions.md
│   │   └── wpf.instructions.md
│   ├── labeler.yml
│   ├── labels.yml
│   ├── markdown-link-check-config.json
│   ├── prompts
│   │   ├── README.md
│   │   ├── add-data-provider.prompt.yml
│   │   ├── add-export-format.prompt.yml
│   │   ├── code-review.prompt.yml
│   │   ├── configure-deployment.prompt.yml
│   │   ├── explain-architecture.prompt.yml
│   │   ├── fix-build-errors.prompt.yml
│   │   ├── fix-code-quality.prompt.yml
│   │   ├── fix-test-failures.prompt.yml
│   │   ├── optimize-performance.prompt.yml
│   │   ├── project-context.prompt.yml
│   │   ├── provider-implementation-guide.prompt.yml
│   │   ├── simulate-user-panel-choose-mode.prompt.yml
│   │   ├── simulate-user-panel-design-partner.prompt.yml
│   │   ├── simulate-user-panel-release-gate.prompt.yml
│   │   ├── simulate-user-panel-usability-lab.prompt.yml
│   │   ├── simulate-user-panel.prompt.yml
│   │   ├── troubleshoot-issue.prompt.yml
│   │   ├── workflow-results-code-quality.prompt.yml
│   │   ├── workflow-results-test-matrix.prompt.yml
│   │   ├── wpf-debug-improve.prompt.yml
│   │   ├── wpf-design-system-screen-impact.prompt.yml
│   │   └── write-unit-tests.prompt.yml
│   ├── pull_request_template_desktop.md
│   ├── spellcheck-config.yml
│   └── workflows
│       ├── README.md
│       ├── ci.yml
│       ├── maintenance.yml
│       ├── publish-smoke.yml
│       └── windows-desktop-build.yml
├── .gitignore
├── .gitleaks.toml
├── .globalconfig
├── .markdownlint.json
├── .tools
│   ├── .store
│   │   └── dotnet-dump
│   │       └── 9.0.661903
│   │           ├── dotnet-dump
│   │           │   └── 9.0.661903
│   │           │       ├── .nupkg.metadata
│   │           │       ├── .signature.p7s
│   │           │       ├── Icon.png
│   │           │       ├── THIRD-PARTY-NOTICES.txt
│   │           │       ├── dotnet-dump.9.0.661903.nupkg.sha512
│   │           │       ├── dotnet-dump.nuspec
│   │           │       └── tools
│   │           │           └── net8.0
│   │           │               └── any
│   │           │                   ├── Azure.Core.dll
│   │           │                   ├── Azure.Identity.dll
│   │           │                   ├── DotnetToolSettings.xml
│   │           │                   ├── Microsoft.Bcl.AsyncInterfaces.dll
│   │           │                   ├── Microsoft.Diagnostics.DebugServices.Implementation.dll
│   │           │                   ├── Microsoft.Diagnostics.DebugServices.Implementation.pdb
│   │           │                   ├── Microsoft.Diagnostics.DebugServices.Implementation.xml
│   │           │                   ├── Microsoft.Diagnostics.DebugServices.dll
│   │           │                   ├── Microsoft.Diagnostics.DebugServices.pdb
│   │           │                   ├── Microsoft.Diagnostics.DebugServices.xml
│   │           │                   ├── Microsoft.Diagnostics.ExtensionCommands.dll
│   │           │                   ├── Microsoft.Diagnostics.ExtensionCommands.pdb
│   │           │                   ├── Microsoft.Diagnostics.ExtensionCommands.xml
│   │           │                   ├── Microsoft.Diagnostics.NETCore.Client.dll
│   │           │                   ├── Microsoft.Diagnostics.NETCore.Client.pdb
│   │           │                   ├── Microsoft.Diagnostics.NETCore.Client.xml
│   │           │                   ├── Microsoft.Diagnostics.Repl.dll
│   │           │                   ├── Microsoft.Diagnostics.Repl.pdb
│   │           │                   ├── Microsoft.Diagnostics.Repl.xml
│   │           │                   ├── Microsoft.Diagnostics.Runtime.dll
│   │           │                   ├── Microsoft.Extensions.DependencyInjection.Abstractions.dll
│   │           │                   ├── Microsoft.Extensions.Logging.Abstractions.dll
│   │           │                   ├── Microsoft.FileFormats.dll
│   │           │                   ├── Microsoft.FileFormats.pdb
│   │           │                   ├── Microsoft.Identity.Client.Extensions.Msal.dll
│   │           │                   ├── Microsoft.Identity.Client.dll
│   │           │                   ├── Microsoft.IdentityModel.Abstractions.dll
│   │           │                   ├── Microsoft.SymbolStore.dll
│   │           │                   ├── Microsoft.SymbolStore.pdb
│   │           │                   ├── SOS.Hosting.dll
│   │           │                   ├── SOS.Hosting.pdb
│   │           │                   ├── SOS.InstallHelper.dll
│   │           │                   ├── SOS.InstallHelper.pdb
│   │           │                   ├── System.ClientModel.dll
│   │           │                   ├── System.Collections.Immutable.dll
│   │           │                   ├── System.CommandLine.dll
│   │           │                   ├── System.Memory.Data.dll
│   │           │                   ├── System.Security.Cryptography.ProtectedData.dll
│   │           │                   ├── cs
│   │           │                   │   └── System.CommandLine.resources.dll
│   │           │                   ├── de
│   │           │                   │   └── System.CommandLine.resources.dll
│   │           │                   ├── dotnet-dump.deps.json
│   │           │                   ├── dotnet-dump.dll
│   │           │                   ├── dotnet-dump.pdb
│   │           │                   ├── dotnet-dump.runtimeconfig.json
│   │           │                   ├── es
│   │           │                   │   └── System.CommandLine.resources.dll
│   │           │                   ├── fr
│   │           │                   │   └── System.CommandLine.resources.dll
│   │           │                   ├── it
│   │           │                   │   └── System.CommandLine.resources.dll
│   │           │                   ├── ja
│   │           │                   │   └── System.CommandLine.resources.dll
│   │           │                   ├── ko
│   │           │                   │   └── System.CommandLine.resources.dll
│   │           │                   ├── linux-arm
│   │           │                   │   └── sosdocsunix.txt
│   │           │                   ├── linux-arm64
│   │           │                   │   └── sosdocsunix.txt
│   │           │                   ├── linux-musl-arm
│   │           │                   │   └── sosdocsunix.txt
│   │           │                   ├── linux-musl-arm64
│   │           │                   │   └── sosdocsunix.txt
│   │           │                   ├── linux-musl-x64
│   │           │                   │   └── sosdocsunix.txt
│   │           │                   ├── linux-x64
│   │           │                   │   └── sosdocsunix.txt
│   │           │                   ├── osx-arm64
│   │           │                   │   ├── libsos.dylib
│   │           │                   │   ├── libsosplugin.dylib
│   │           │                   │   └── sosdocsunix.txt
│   │           │                   ├── osx-x64
│   │           │                   │   ├── libsos.dylib
│   │           │                   │   ├── libsosplugin.dylib
│   │           │                   │   └── sosdocsunix.txt
│   │           │                   ├── pl
│   │           │                   │   └── System.CommandLine.resources.dll
│   │           │                   ├── pt-BR
│   │           │                   │   └── System.CommandLine.resources.dll
│   │           │                   ├── ru
│   │           │                   │   └── System.CommandLine.resources.dll
│   │           │                   ├── runtimes
│   │           │                   │   └── win
│   │           │                   │       └── lib
│   │           │                   │           └── netstandard2.0
│   │           │                   │               └── System.Security.Cryptography.ProtectedData.dll
│   │           │                   ├── shims
│   │           │                   │   ├── osx-x64
│   │           │                   │   │   └── dotnet-dump
│   │           │                   │   ├── win-x64
│   │           │                   │   │   └── dotnet-dump.exe
│   │           │                   │   └── win-x86
│   │           │                   │       └── dotnet-dump.exe
│   │           │                   ├── tr
│   │           │                   │   └── System.CommandLine.resources.dll
│   │           │                   ├── win-arm64
│   │           │                   │   ├── Microsoft.DiaSymReader.Native.arm64.dll
│   │           │                   │   └── sos.dll
│   │           │                   ├── win-x64
│   │           │                   │   ├── Microsoft.DiaSymReader.Native.amd64.dll
│   │           │                   │   └── sos.dll
│   │           │                   ├── win-x86
│   │           │                   │   ├── Microsoft.DiaSymReader.Native.x86.dll
│   │           │                   │   └── sos.dll
│   │           │                   ├── zh-Hans
│   │           │                   │   └── System.CommandLine.resources.dll
│   │           │                   └── zh-Hant
│   │           │                       └── System.CommandLine.resources.dll
│   │           └── project.assets.json
│   └── dotnet-dump.exe
├── .vsconfig
├── AGENTS.md
├── CLAUDE.md
├── Directory.Build.props
├── Directory.Packages.props
├── LICENSE
├── Makefile
├── Meridian Design System
│   ├── BRAND_GUIDELINES.md
│   ├── CONTENT_FUNDAMENTALS.md
│   ├── ICONOGRAPHY.md
│   ├── INSPIRATION_BRIEF.md
│   ├── README.md
│   ├── SKILL.md
│   ├── VISUAL_FOUNDATIONS.md
│   ├── assets
│   │   ├── app.ico
│   │   ├── brand
│   │   │   ├── README.md
│   │   │   ├── meridian-hero.svg
│   │   │   ├── meridian-mark-light.svg
│   │   │   ├── meridian-mark-monochrome.svg
│   │   │   ├── meridian-mark.svg
│   │   │   ├── meridian-symbol.svg
│   │   │   ├── meridian-tile-256.png
│   │   │   ├── meridian-tile.svg
│   │   │   ├── meridian-wordmark-stacked.svg
│   │   │   └── meridian-wordmark.svg
│   │   └── icons
│   │       ├── README.md
│   │       ├── account-portfolio.svg
│   │       ├── admin-maintenance.svg
│   │       ├── aggregate-portfolio.svg
│   │       ├── archive-health.svg
│   │       ├── backfill.svg
│   │       ├── backtest.svg
│   │       ├── charting.svg
│   │       ├── collection-sessions.svg
│   │       ├── dashboard.svg
│   │       ├── data-browser.svg
│   │       ├── data-calendar.svg
│   │       ├── data-export.svg
│   │       ├── data-operations.svg
│   │       ├── data-quality.svg
│   │       ├── data-sampling.svg
│   │       ├── data-sources.svg
│   │       ├── diagnostics.svg
│   │       ├── event-replay.svg
│   │       ├── governance.svg
│   │       ├── help.svg
│   │       ├── index-subscription.svg
│   │       ├── keyboard-shortcuts.svg
│   │       ├── lean-integration.svg
│   │       ├── live-data.svg
│   │       ├── order-book.svg
│   │       ├── portfolio-import.svg
│   │       ├── provider-health.svg
│   │       ├── research.svg
│   │       ├── retention-assurance.svg
│   │       ├── run-detail.svg
│   │       ├── run-ledger.svg
│   │       ├── run-mat.svg
│   │       ├── run-portfolio.svg
│   │       ├── schedule-manager.svg
│   │       ├── security-master.svg
│   │       ├── service-manager.svg
│   │       ├── settings.svg
│   │       ├── storage-optimization.svg
│   │       ├── storage.svg
│   │       ├── strategy-runs.svg
│   │       ├── symbol-storage.svg
│   │       ├── symbols.svg
│   │       ├── system-health.svg
│   │       ├── trading-hours.svg
│   │       ├── trading.svg
│   │       └── watchlist.svg
│   ├── colors_and_type.css
│   ├── governance-baseline.json
│   ├── index.html
│   ├── preview
│   │   ├── brand-icons.html
│   │   ├── brand-marks.html
│   │   ├── chart-table-standards.html
│   │   ├── charts-candlestick.html
│   │   ├── charts-correlation.html
│   │   ├── charts-equity-print.html
│   │   ├── charts-equity.html
│   │   ├── charts-heatmap.html
│   │   ├── charts-histogram.html
│   │   ├── charts-orderbook.html
│   │   ├── charts-scatter.html
│   │   ├── charts-sparklines.html
│   │   ├── charts-volsurface.html
│   │   ├── charts-yieldcurve.html
│   │   ├── colors-ambient.html
│   │   ├── colors-brand.html
│   │   ├── colors-semantic.html
│   │   ├── colors-surfaces.html
│   │   ├── component-state-matrix.html
│   │   ├── components-badges.html
│   │   ├── components-banners.html
│   │   ├── components-buttons.html
│   │   ├── components-inputs.html
│   │   ├── components-metrics.html
│   │   ├── components-nav.html
│   │   ├── components-table.html
│   │   ├── design-standards.html
│   │   ├── index.html
│   │   ├── institutional-workstation.html
│   │   ├── preview-common.css
│   │   ├── reference-workbench.html
│   │   ├── screen-recipes.html
│   │   ├── spacing-radii.html
│   │   ├── spacing-scale.html
│   │   ├── spacing-shadows.html
│   │   ├── state-patterns.html
│   │   ├── type-body.html
│   │   ├── type-display.html
│   │   └── type-mono.html
│   ├── scripts
│   │   └── check_design_system_governance.py
│   ├── tests
│   │   └── test_design_system_governance.py
│   ├── ui_kits
│   │   ├── dashboard
│   │   │   ├── README.md
│   │   │   └── components.jsx
│   │   ├── plottool_workstation.html
│   │   ├── security_master-company.html
│   │   ├── security_master-print.html
│   │   └── security_master.html
│   └── uploads
│       └── ChatGPT Image Apr 24, 2026, 03_58_29 PM.png
├── Meridian.sln
├── NuGet.Config
├── README.md
├── archive
│   ├── code
│   │   ├── README.md
│   │   └── src
│   │       ├── Meridian.Backtesting.Sdk
│   │       │   └── Ledger
│   │       │       ├── BacktestLedger.cs
│   │       │       ├── JournalEntry.cs
│   │       │       ├── LedgerAccount.cs
│   │       │       ├── LedgerAccountType.cs
│   │       │       ├── LedgerAccounts.cs
│   │       │       └── LedgerEntry.cs
│   │       └── Meridian.QuantScript
│   │           └── Compilation
│   │               └── Contracts.cs
│   └── docs
│       ├── INDEX.md
│       ├── README.md
│       ├── assessments
│       │   ├── ARTIFACT_ACTIONS_DOWNGRADE.md
│       │   ├── AUDIT_REPORT_2026_03_20.md
│       │   ├── CLEANUP_OPPORTUNITIES.md
│       │   ├── CLEANUP_SUMMARY.md
│       │   ├── CODE_REVIEW_2026-03-16.md
│       │   ├── CONFIG_CONSOLIDATION_REPORT.md
│       │   ├── DUPLICATE_CODE_ANALYSIS.md
│       │   ├── H3_DEBUG_CODE_ANALYSIS.md
│       │   ├── UWP_COMPREHENSIVE_AUDIT.md
│       │   ├── canonicalization-drift-report.local.md
│       │   ├── desktop-devex-high-value-improvements.md
│       │   ├── desktop-end-user-improvements-shortlist.md
│       │   ├── desktop-end-user-improvements.md
│       │   ├── desktop-ui-alternatives-evaluation.md
│       │   ├── duplicate-deprecated-implementation-audit-2026-05-17.md
│       │   ├── high-impact-improvement-brainstorm-2026-03.md
│       │   ├── high-impact-improvements-brainstorm.md
│       │   ├── meridian-main-differentiation-report-2026-04-29.md
│       │   └── ui-redesign.md
│       ├── c4-context-legacy.png
│       ├── c4-context-legacy.puml
│       ├── migrations
│       │   ├── ADR-015-platform-restructuring.md
│       │   ├── desktop-app-xaml-compiler-errors.md
│       │   ├── uwp-development-roadmap.md
│       │   ├── uwp-release-checklist.md
│       │   └── uwp-to-wpf-migration.md
│       ├── plans
│       │   ├── QUICKSTART_2026-01-08.md
│       │   ├── REPOSITORY_REORGANIZATION_PLAN.md
│       │   ├── WORKFLOW_IMPROVEMENTS_2026-01-08.md
│       │   ├── backtesting-quantscript-improvement-plan-2026-04.md
│       │   ├── consolidation.md
│       │   ├── meridian-analytics-productization-blueprint.md
│       │   ├── provider-reliability-data-confidence-wave-1-blueprint.md
│       │   ├── quant-script-environment-blueprint.md
│       │   ├── quant-script-page-implementation-guide.md
│       │   ├── readability-refactor-baseline.md
│       │   ├── readability-refactor-roadmap.md
│       │   ├── readability-refactor-technical-design-pack.md
│       │   ├── repository-cleanup-action-plan.md
│       │   ├── security-master-productization-roadmap.md
│       │   ├── workstation-release-readiness-blueprint.md
│       │   └── workstation-sprint-1-implementation-backlog.md
│       └── summaries
│           ├── 2026-02_PR_SUMMARY.md
│           ├── 2026-02_UI_IMPROVEMENTS_SUMMARY.md
│           ├── 2026-02_VISUAL_CODE_EXAMPLES.md
│           ├── CHANGES_SUMMARY.md
│           ├── CS0101_FIX_SUMMARY.md
│           ├── DOCUMENTATION_TRIAGE_2026_03_21.md
│           ├── IMPROVEMENTS_2026-02.md
│           ├── REDESIGN_IMPROVEMENTS.md
│           ├── ROADMAP_NOW_NEXT_LATER_2026_03_25.md
│           ├── ROADMAP_UPDATE_SUMMARY.md
│           ├── STRUCTURAL_IMPROVEMENTS_2026-02.md
│           ├── TEST_MATRIX_FIX_SUMMARY.md
│           ├── WAVE2_IMPLEMENTATION_SUMMARY.md
│           └── desktop-improvements-executive-summary.md
├── benchmarks
│   ├── BOTTLENECK_REPORT.md
│   ├── Meridian.Benchmarks
│   │   ├── Budget
│   │   │   ├── BenchmarkResultStore.cs
│   │   │   ├── IPerformanceBudget.cs
│   │   │   ├── PerformanceBudget.cs
│   │   │   └── PerformanceBudgetRegistry.cs
│   │   ├── CanonicalizationBenchmarks.cs
│   │   ├── CollectorBenchmarks.cs
│   │   ├── CompositeSinkBenchmarks.cs
│   │   ├── DeduplicationKeyBenchmarks.cs
│   │   ├── EndToEndPipelineBenchmarks.cs
│   │   ├── EventPipelineBenchmarks.cs
│   │   ├── IndicatorBenchmarks.cs
│   │   ├── JsonSerializationBenchmarks.cs
│   │   ├── Meridian.Benchmarks.csproj
│   │   ├── NewlineScanBenchmarks.cs
│   │   ├── Program.cs
│   │   ├── StorageSinkBenchmarks.cs
│   │   ├── StrategyRunReadBenchmarks.cs
│   │   └── WalChecksumBenchmarks.cs
│   └── run-bottleneck-benchmarks.sh
├── build
│   ├── dotnet
│   │   ├── DocGenerator
│   │   │   ├── DocGenerator.csproj
│   │   │   └── Program.cs
│   │   └── FSharpInteropGenerator
│   │       ├── FSharpInteropGenerator.csproj
│   │       └── Program.cs
│   ├── node
│   │   ├── generate-diagrams.mjs
│   │   └── generate-icons.mjs
│   ├── python
│   │   ├── __init__.py
│   │   ├── adapters
│   │   │   ├── __init__.py
│   │   │   └── dotnet.py
│   │   ├── analytics
│   │   │   ├── __init__.py
│   │   │   ├── history.py
│   │   │   ├── metrics.py
│   │   │   └── profile.py
│   │   ├── cli
│   │   │   └── buildctl.py
│   │   ├── core
│   │   │   ├── __init__.py
│   │   │   ├── events.py
│   │   │   ├── fingerprint.py
│   │   │   ├── graph.py
│   │   │   └── utils.py
│   │   ├── diagnostics
│   │   │   ├── __init__.py
│   │   │   ├── doctor.py
│   │   │   ├── env_diff.py
│   │   │   ├── error_matcher.py
│   │   │   ├── preflight.py
│   │   │   └── validate_data.py
│   │   └── knowledge
│   │       └── errors
│   │           ├── msbuild.json
│   │           └── nuget.json
│   ├── rules
│   │   └── doc-rules.yaml
│   └── scripts
│       ├── ai-architecture-check.py
│       ├── ai-repo-updater.py
│       ├── docs
│       │   ├── README.md
│       │   ├── add-todos.py
│       │   ├── ai-docs-maintenance.py
│       │   ├── check-ai-inventory.py
│       │   ├── create-todo-issues.py
│       │   ├── dashboard_rendering.py
│       │   ├── generate-ai-navigation.py
│       │   ├── generate-api-contract-coverage-dashboard.py
│       │   ├── generate-changelog.py
│       │   ├── generate-coverage.py
│       │   ├── generate-dependency-graph.py
│       │   ├── generate-evidence-continuity-dashboard.py
│       │   ├── generate-governance-readiness-dashboard.py
│       │   ├── generate-health-dashboard.py
│       │   ├── generate-metrics-dashboard.py
│       │   ├── generate-paper-replay-reliability-dashboard.py
│       │   ├── generate-pilot-readiness-dashboard.py
│       │   ├── generate-prompts.py
│       │   ├── generate-structure-docs.py
│       │   ├── generate-workflow-manifest.py
│       │   ├── lint-command-snippets.py
│       │   ├── repair-links.py
│       │   ├── rules-engine.py
│       │   ├── run-docs-automation.py
│       │   ├── scan-todos.py
│       │   ├── sync-readme-badges.py
│       │   ├── test-scripts.py
│       │   ├── tests
│       │   │   ├── test_check_ai_inventory.py
│       │   │   ├── test_markdown_generation_lint.py
│       │   │   ├── test_pilot_readiness_dashboard.py
│       │   │   └── test_scan_todos.py
│       │   ├── update-claude-md.py
│       │   ├── validate-api-docs.py
│       │   ├── validate-docs-structure.py
│       │   ├── validate-examples.py
│       │   ├── validate-golden-path.sh
│       │   └── validate-skill-packages.py
│       ├── hooks
│       │   ├── commit-msg
│       │   ├── install-hooks.sh
│       │   └── pre-commit
│       ├── install
│       │   ├── install-web-workstation.ps1
│       │   ├── install.ps1
│       │   ├── install.sh
│       │   └── smoke-web-workstation-install.ps1
│       ├── lib
│       │   ├── ArtifactRetention.psm1
│       │   └── BuildNotification.psm1
│       ├── publish
│       │   ├── publish.ps1
│       │   └── publish.sh
│       ├── run
│       │   ├── start-collector.ps1
│       │   ├── start-collector.sh
│       │   ├── stop-collector.ps1
│       │   └── stop-collector.sh
│       ├── tests
│       │   ├── test_generate_ai_navigation.py
│       │   └── test_validate_budget.py
│       ├── validate-tooling-metadata.py
│       └── validate_budget.py
├── config
│   ├── appsettings.sample.json
│   ├── appsettings.schema.json
│   ├── condition-codes.json
│   ├── score-reason-registry.json
│   └── venue-mapping.json
├── deploy
│   ├── docker
│   │   ├── .dockerignore
│   │   ├── Dockerfile
│   │   ├── docker-compose.override.yml
│   │   └── docker-compose.yml
│   ├── k8s
│   │   ├── configmap.yaml
│   │   ├── deployment.yaml
│   │   ├── kustomization.yaml
│   │   ├── namespace.yaml
│   │   ├── pvc.yaml
│   │   ├── secret.yaml
│   │   ├── service.yaml
│   │   └── serviceaccount.yaml
│   ├── monitoring
│   │   ├── alert-rules.yml
│   │   ├── grafana
│   │   │   └── provisioning
│   │   │       ├── dashboards
│   │   │       │   ├── dashboards.yml
│   │   │       │   └── json
│   │   │       │       ├── meridian-overview.json
│   │   │       │       └── meridian-trades.json
│   │   │       └── datasources
│   │   │           └── datasources.yml
│   │   └── prometheus.yml
│   └── systemd
│       └── meridian.service
├── docfx.json
├── docs
│   ├── DEPENDENCIES.md
│   ├── HELP.md
│   ├── README.md
│   ├── adr
│   │   ├── 001-provider-abstraction.md
│   │   ├── 002-tiered-storage-architecture.md
│   │   ├── 003-microservices-decomposition.md
│   │   ├── 004-async-streaming-patterns.md
│   │   ├── 005-attribute-based-discovery.md
│   │   ├── 006-domain-events-polymorphic-payload.md
│   │   ├── 007-write-ahead-log-durability.md
│   │   ├── 008-multi-format-composite-storage.md
│   │   ├── 009-fsharp-interop.md
│   │   ├── 010-httpclient-factory.md
│   │   ├── 011-centralized-configuration-and-credentials.md
│   │   ├── 012-monitoring-and-alerting-pipeline.md
│   │   ├── 013-bounded-channel-policy.md
│   │   ├── 014-json-source-generators.md
│   │   ├── 015-strategy-execution-contract.md
│   │   ├── 016-custody-cash-reconciliation-break-typing.md
│   │   ├── 016-platform-architecture-migration.md
│   │   ├── README.md
│   │   └── _template.md
│   ├── ai
│   │   ├── README.md
│   │   ├── agents
│   │   │   └── README.md
│   │   ├── ai-known-errors.md
│   │   ├── assistant-workflow-contract.md
│   │   ├── claude
│   │   │   ├── CLAUDE.actions.md
│   │   │   ├── CLAUDE.api.md
│   │   │   ├── CLAUDE.domain-naming.md
│   │   │   ├── CLAUDE.fsharp.md
│   │   │   ├── CLAUDE.providers.md
│   │   │   ├── CLAUDE.repo-updater.md
│   │   │   ├── CLAUDE.roadmap-learning-log.md
│   │   │   ├── CLAUDE.storage.md
│   │   │   ├── CLAUDE.structure.md
│   │   │   └── CLAUDE.testing.md
│   │   ├── copilot
│   │   │   ├── ai-sync-workflow.md
│   │   │   └── instructions.md
│   │   ├── generated
│   │   │   ├── repo-navigation.json
│   │   │   └── repo-navigation.md
│   │   ├── instructions
│   │   │   └── README.md
│   │   ├── navigation
│   │   │   └── README.md
│   │   ├── prompts
│   │   │   └── README.md
│   │   └── skills
│   │       └── README.md
│   ├── architecture
│   │   ├── README.md
│   │   ├── c4-diagrams.md
│   │   ├── crystallized-storage-format.md
│   │   ├── desktop-layers.md
│   │   ├── deterministic-canonicalization.md
│   │   ├── domains.md
│   │   ├── environment-designer-runtime-projection-and-wpf-admin-surface.md
│   │   ├── evidence-workflow-fabric.md
│   │   ├── layer-boundaries.md
│   │   ├── ledger-architecture.md
│   │   ├── module-map.md
│   │   ├── mvvm-guidelines.md
│   │   ├── overview.md
│   │   ├── project-structure.md
│   │   ├── provider-management.md
│   │   ├── storage-design.md
│   │   ├── strategy-builder-integration.md
│   │   ├── why-this-architecture.md
│   │   ├── workflow-library.md
│   │   ├── wpf-shell-mvvm.md
│   │   └── wpf-workstation-shell-ux.md
│   ├── archive
│   │   ├── README.md
│   │   └── documentation-consolidation-inventory-2026-05-17.md
│   ├── audits
│   │   ├── AUDIT_REPORT.md
│   │   ├── BACKTEST_ENGINE_CODE_REVIEW_2026_03_25.md
│   │   ├── CODE_REVIEW_2026-03-16.md
│   │   ├── FURTHER_SIMPLIFICATION_OPPORTUNITIES.md
│   │   ├── README.md
│   │   ├── audit-architecture-results.txt
│   │   ├── audit-code-results.json
│   │   ├── audit-results-full.json
│   │   ├── prompt-generation-results.json
│   │   └── workspace-visual-audit-checklist-2026-04-22.md
│   ├── design
│   │   ├── README.md
│   │   └── design-system-usage.md
│   ├── developer
│   │   ├── README.md
│   │   ├── build-test-run.md
│   │   ├── publish-standalone-exe.md
│   │   └── setup.md
│   ├── development
│   │   ├── README.md
│   │   ├── adding-custom-rules.md
│   │   ├── build-observability.md
│   │   ├── central-package-management.md
│   │   ├── desktop-command-surface-migration.md
│   │   ├── desktop-testing-guide.md
│   │   ├── desktop-workflow-automation.md
│   │   ├── documentation-automation.md
│   │   ├── documentation-contribution-guide.md
│   │   ├── expanding-scripts.md
│   │   ├── fsharp-decision-rule.md
│   │   ├── fund-account-traversal.md
│   │   ├── git-hooks.md
│   │   ├── github-actions-summary.md
│   │   ├── github-actions-testing.md
│   │   ├── otlp-trace-visualization.md
│   │   ├── policies
│   │   │   ├── desktop-support-policy.md
│   │   │   └── promotion-policy-matrix.md
│   │   ├── provider-implementation.md
│   │   ├── refactor-map.md
│   │   ├── repository-organization-guide.md
│   │   ├── repository-rule-set.md
│   │   ├── rule-evaluation-contracts.md
│   │   ├── score-reason-taxonomy.md
│   │   ├── tooling-workflow-backlog.md
│   │   ├── ui-fixture-mode-guide.md
│   │   └── wpf-implementation-notes.md
│   ├── diagrams
│   │   ├── README.md
│   │   ├── analytics
│   │   │   ├── README.md
│   │   │   ├── backtesting-engine.dot
│   │   │   ├── backtesting-engine.png
│   │   │   └── backtesting-engine.svg
│   │   ├── architecture
│   │   │   ├── README.md
│   │   │   ├── c4
│   │   │   │   ├── c4-level1-context.dot
│   │   │   │   ├── c4-level1-context.png
│   │   │   │   ├── c4-level1-context.svg
│   │   │   │   ├── c4-level2-containers.dot
│   │   │   │   ├── c4-level2-containers.png
│   │   │   │   ├── c4-level2-containers.svg
│   │   │   │   ├── c4-level3-components.dot
│   │   │   │   ├── c4-level3-components.png
│   │   │   │   └── c4-level3-components.svg
│   │   │   └── platform
│   │   │       ├── domain-event-model.dot
│   │   │       ├── domain-event-model.png
│   │   │       ├── domain-event-model.svg
│   │   │       ├── fsharp-domain.dot
│   │   │       ├── fsharp-domain.png
│   │   │       ├── fsharp-domain.svg
│   │   │       ├── mcp-server.dot
│   │   │       ├── mcp-server.png
│   │   │       ├── mcp-server.svg
│   │   │       ├── project-dependencies.dot
│   │   │       ├── project-dependencies.png
│   │   │       ├── project-dependencies.svg
│   │   │       ├── provider-architecture.dot
│   │   │       ├── provider-architecture.png
│   │   │       ├── provider-architecture.svg
│   │   │       ├── runtime-hosts.dot
│   │   │       ├── runtime-hosts.png
│   │   │       ├── runtime-hosts.svg
│   │   │       ├── storage-architecture.dot
│   │   │       ├── storage-architecture.png
│   │   │       ├── storage-architecture.svg
│   │   │       ├── workstation-delivery.dot
│   │   │       ├── workstation-delivery.png
│   │   │       └── workstation-delivery.svg
│   │   ├── operations
│   │   │   ├── README.md
│   │   │   ├── data-quality-monitoring.dot
│   │   │   ├── data-quality-monitoring.png
│   │   │   ├── data-quality-monitoring.svg
│   │   │   ├── deployment-options.dot
│   │   │   ├── deployment-options.png
│   │   │   ├── deployment-options.svg
│   │   │   ├── resilience-patterns.dot
│   │   │   ├── resilience-patterns.png
│   │   │   └── resilience-patterns.svg
│   │   ├── reference
│   │   │   ├── README.md
│   │   │   ├── cli-commands.dot
│   │   │   ├── cli-commands.png
│   │   │   ├── cli-commands.svg
│   │   │   ├── configuration-management.dot
│   │   │   ├── configuration-management.png
│   │   │   ├── configuration-management.svg
│   │   │   ├── symbol-search-resolution.dot
│   │   │   ├── symbol-search-resolution.png
│   │   │   └── symbol-search-resolution.svg
│   │   ├── ui
│   │   │   ├── README.md
│   │   │   ├── ui-implementation-flow.dot
│   │   │   ├── ui-implementation-flow.png
│   │   │   ├── ui-implementation-flow.svg
│   │   │   ├── ui-navigation-map.dot
│   │   │   ├── ui-navigation-map.png
│   │   │   └── ui-navigation-map.svg
│   │   ├── ui-implementation-flow.dot
│   │   ├── ui-implementation-flow.png
│   │   ├── ui-implementation-flow.svg
│   │   ├── ui-navigation-map.dot
│   │   ├── ui-navigation-map.png
│   │   ├── ui-navigation-map.svg
│   │   ├── uml
│   │   │   ├── Activity Diagram - Data Collection Process Flow.png
│   │   │   ├── Activity Diagram - Data Collection Process Flow.svg
│   │   │   ├── Activity Diagram - Historical Backfill Process.png
│   │   │   ├── Activity Diagram - Historical Backfill Process.svg
│   │   │   ├── Class Diagram - WPF MVVM Architecture.png
│   │   │   ├── Class Diagram - WPF MVVM Architecture.svg
│   │   │   ├── Communication Diagram - Component Message Exchange.png
│   │   │   ├── Communication Diagram - Component Message Exchange.svg
│   │   │   ├── Interaction Overview Diagram - System Workflow.png
│   │   │   ├── Interaction Overview Diagram - System Workflow.svg
│   │   │   ├── README.md
│   │   │   ├── Sequence Diagram - Backtesting Engine.png
│   │   │   ├── Sequence Diagram - Backtesting Engine.svg
│   │   │   ├── Sequence Diagram - Historical Backfill Flow.png
│   │   │   ├── Sequence Diagram - Historical Backfill Flow.svg
│   │   │   ├── Sequence Diagram - Paper Trading Order Execution.png
│   │   │   ├── Sequence Diagram - Paper Trading Order Execution.svg
│   │   │   ├── Sequence Diagram - Real-Time Data Collection Flow.png
│   │   │   ├── Sequence Diagram - Real-Time Data Collection Flow.svg
│   │   │   ├── Sequence Diagram - Strategy Promotion Lifecycle.png
│   │   │   ├── Sequence Diagram - Strategy Promotion Lifecycle.svg
│   │   │   ├── Sequence Diagram - WAL Durability and Crash-Safe Writes.png
│   │   │   ├── Sequence Diagram - WAL Durability and Crash-Safe Writes.svg
│   │   │   ├── State Diagram - Backfill Request States.png
│   │   │   ├── State Diagram - Backfill Request States.svg
│   │   │   ├── State Diagram - Order Book Stream States.png
│   │   │   ├── State Diagram - Order Book Stream States.svg
│   │   │   ├── State Diagram - Provider Connection States.png
│   │   │   ├── State Diagram - Provider Connection States.svg
│   │   │   ├── State Diagram - Trade Sequence Validation States.png
│   │   │   ├── State Diagram - Trade Sequence Validation States.svg
│   │   │   ├── Timing Diagram - Backfill Operation Timeline.png
│   │   │   ├── Timing Diagram - Backfill Operation Timeline.svg
│   │   │   ├── Timing Diagram - Event Processing Timeline.png
│   │   │   ├── Timing Diagram - Event Processing Timeline.svg
│   │   │   ├── Use Case Diagram - Meridian.png
│   │   │   ├── Use Case Diagram - Meridian.svg
│   │   │   ├── activity-diagram-backfill.png
│   │   │   ├── activity-diagram-backfill.puml
│   │   │   ├── activity-diagram.png
│   │   │   ├── activity-diagram.puml
│   │   │   ├── class-diagram-wpf-mvvm.puml
│   │   │   ├── communication-diagram.png
│   │   │   ├── communication-diagram.puml
│   │   │   ├── interaction-overview-diagram.png
│   │   │   ├── interaction-overview-diagram.puml
│   │   │   ├── sequence-diagram-backfill.png
│   │   │   ├── sequence-diagram-backfill.puml
│   │   │   ├── sequence-diagram-backtesting.puml
│   │   │   ├── sequence-diagram-paper-trading.puml
│   │   │   ├── sequence-diagram-strategy-promotion.puml
│   │   │   ├── sequence-diagram-wal-durability.puml
│   │   │   ├── sequence-diagram.png
│   │   │   ├── sequence-diagram.puml
│   │   │   ├── state-diagram-backfill.png
│   │   │   ├── state-diagram-backfill.puml
│   │   │   ├── state-diagram-orderbook.png
│   │   │   ├── state-diagram-orderbook.puml
│   │   │   ├── state-diagram-trade-sequence.png
│   │   │   ├── state-diagram-trade-sequence.puml
│   │   │   ├── state-diagram.png
│   │   │   ├── state-diagram.puml
│   │   │   ├── timing-diagram-backfill.png
│   │   │   ├── timing-diagram-backfill.puml
│   │   │   ├── timing-diagram.png
│   │   │   ├── timing-diagram.puml
│   │   │   ├── use-case-diagram.png
│   │   │   └── use-case-diagram.puml
│   │   └── workflows
│   │       ├── README.md
│   │       └── operations
│   │           ├── backfill-workflow.dot
│   │           ├── backfill-workflow.png
│   │           ├── backfill-workflow.svg
│   │           ├── data-flow.dot
│   │           ├── data-flow.png
│   │           ├── data-flow.svg
│   │           ├── event-pipeline-sequence.dot
│   │           ├── event-pipeline-sequence.png
│   │           ├── event-pipeline-sequence.svg
│   │           ├── execution-layer.dot
│   │           ├── execution-layer.png
│   │           ├── execution-layer.svg
│   │           ├── fund-ops-reconciliation.dot
│   │           ├── fund-ops-reconciliation.png
│   │           ├── fund-ops-reconciliation.svg
│   │           ├── onboarding-flow.dot
│   │           ├── onboarding-flow.png
│   │           ├── onboarding-flow.svg
│   │           ├── security-master-lifecycle.dot
│   │           ├── security-master-lifecycle.png
│   │           ├── security-master-lifecycle.svg
│   │           ├── strategy-lifecycle.dot
│   │           ├── strategy-lifecycle.png
│   │           └── strategy-lifecycle.svg
│   ├── docfx
│   │   ├── README.md
│   │   ├── api
│   │   │   └── index.md
│   │   └── filterConfig.yml
│   ├── evaluations
│   │   ├── 2026-03-brainstorm-next-frontier.md
│   │   ├── README.md
│   │   ├── assembly-performance-opportunities.md
│   │   ├── competitive-analysis-2026-03.md
│   │   ├── data-quality-monitoring-evaluation.md
│   │   ├── desktop-platform-improvements-implementation-guide.md
│   │   ├── high-value-low-cost-improvements-brainstorm.md
│   │   ├── historical-data-providers-evaluation.md
│   │   ├── ingestion-orchestration-evaluation.md
│   │   ├── nautilus-inspired-restructuring-proposal.md
│   │   ├── operational-readiness-evaluation.md
│   │   ├── quant-script-blueprint-brainstorm.md
│   │   ├── realtime-streaming-architecture-evaluation.md
│   │   ├── storage-architecture-evaluation.md
│   │   └── windows-desktop-provider-configurability-assessment.md
│   ├── examples
│   │   ├── README.md
│   │   └── provider-template
│   │       ├── README.md
│   │       ├── TemplateConfig.cs
│   │       ├── TemplateConstants.cs
│   │       ├── TemplateFactory.cs
│   │       ├── TemplateHistoricalDataProvider.cs
│   │       ├── TemplateMarketDataClient.cs
│   │       └── TemplateSymbolSearchProvider.cs
│   ├── generated
│   │   ├── README.md
│   │   ├── adr-index.md
│   │   ├── configuration-schema.md
│   │   ├── documentation-coverage.md
│   │   ├── interfaces.md
│   │   ├── project-context.md
│   │   ├── project-dependencies.md
│   │   ├── provider-registry.md
│   │   ├── repository-structure.md
│   │   ├── workflow-command-reference.md
│   │   └── workflows-overview.md
│   ├── getting-started
│   │   ├── README.md
│   │   └── pilot-operator-quickstart.md
│   ├── integrations
│   │   ├── README.md
│   │   ├── fsharp-integration.md
│   │   ├── language-strategy.md
│   │   └── lean-integration.md
│   ├── operations
│   │   ├── README.md
│   │   ├── cleanup-and-maintenance.md
│   │   ├── deployment.md
│   │   ├── disk-space-hygiene.md
│   │   ├── governance-operator-workflow.md
│   │   ├── high-availability.md
│   │   ├── live-execution-controls.md
│   │   ├── msix-packaging.md
│   │   ├── operator-runbook.md
│   │   ├── performance-tuning.md
│   │   ├── portable-data-packager.md
│   │   ├── preflight-checklist.md
│   │   ├── provider-credential-management.md
│   │   ├── provider-degradation-calibration.md
│   │   ├── reconciliation-operations.md
│   │   ├── reconciliation-policy-operations.md
│   │   ├── reconciliation-runbook.md
│   │   ├── service-level-objectives.md
│   │   └── web-workstation-installer.md
│   ├── plans
│   │   ├── README.md
│   │   ├── assembly-performance-roadmap.md
│   │   ├── backtest-studio-unification-blueprint.md
│   │   ├── backtest-studio-unification-pr-sequenced-roadmap.md
│   │   ├── brokerage-portfolio-sync-blueprint.md
│   │   ├── codebase-audit-cleanup-roadmap.md
│   │   ├── covered-call-writing-slice-1-blueprint.md
│   │   ├── current-direction-and-status.md
│   │   ├── evidence-backed-investment-operations-plan.md
│   │   ├── fund-management-module-implementation-backlog.md
│   │   ├── fund-management-pr-sequenced-roadmap.md
│   │   ├── fund-management-product-vision-and-capability-matrix.md
│   │   ├── governance-fund-ops-blueprint.md
│   │   ├── kernel-parity-migration-blueprint.md
│   │   ├── l3-inference-implementation-plan.md
│   │   ├── ledger.md
│   │   ├── meridian-6-week-roadmap.md
│   │   ├── meridian-database-blueprint.md
│   │   ├── meridian-pilot-workflow.md
│   │   ├── options-roadmap.md
│   │   ├── paper-trading-cockpit-reliability-sprint.md
│   │   ├── portfolio-level-backtesting-composer-blueprint.md
│   │   ├── quantscript-l3-multiinstance-round2-roadmap.md
│   │   ├── research-backtest-trust-and-velocity-blueprint.md
│   │   ├── runbook-template-registry-modernization-plan.md
│   │   ├── trading-workstation-migration-blueprint.md
│   │   ├── ufl-bond-target-state-v2.md
│   │   ├── ufl-cash-sweep-target-state-v2.md
│   │   ├── ufl-certificate-of-deposit-target-state-v2.md
│   │   ├── ufl-cfd-target-state-v2.md
│   │   ├── ufl-commercial-paper-target-state-v2.md
│   │   ├── ufl-commodity-target-state-v2.md
│   │   ├── ufl-crypto-target-state-v2.md
│   │   ├── ufl-deposit-target-state-v2.md
│   │   ├── ufl-direct-lending-implementation-roadmap.md
│   │   ├── ufl-direct-lending-target-state-v2.md
│   │   ├── ufl-equity-target-state-v2.md
│   │   ├── ufl-future-target-state-v2.md
│   │   ├── ufl-fx-spot-target-state-v2.md
│   │   ├── ufl-money-market-fund-target-state-v2.md
│   │   ├── ufl-option-target-state-v2.md
│   │   ├── ufl-other-security-target-state-v2.md
│   │   ├── ufl-repo-target-state-v2.md
│   │   ├── ufl-supported-assets-index.md
│   │   ├── ufl-swap-target-state-v2.md
│   │   ├── ufl-treasury-bill-target-state-v2.md
│   │   ├── ufl-warrant-target-state-v2.md
│   │   ├── waves-2-4-operator-readiness-addendum.md
│   │   └── web-ui-development-pivot.md
│   ├── prompts
│   │   ├── README.md
│   │   ├── automation-prompts.md
│   │   └── repo-maintenance-prompts.md
│   ├── providers
│   │   ├── README.md
│   │   ├── alpaca-setup.md
│   │   ├── backfill-guide.md
│   │   ├── data-sources.md
│   │   ├── interactive-brokers-free-equity-reference.md
│   │   ├── interactive-brokers-setup.md
│   │   ├── provider-comparison.md
│   │   ├── provider-confidence-baseline.md
│   │   ├── security-master-guide.md
│   │   └── stocksharp-connectors.md
│   ├── reference
│   │   ├── README.md
│   │   ├── api-reference.md
│   │   ├── backtest-preflight-and-stage-telemetry.md
│   │   ├── brand-assets.md
│   │   ├── data-dictionary.md
│   │   ├── data-uniformity.md
│   │   ├── design-review-memo.md
│   │   ├── edgar-reference-data.md
│   │   ├── environment-variables.md
│   │   ├── export-preflight-rules.md
│   │   ├── governance-report-packs.md
│   │   ├── ledger-journal-store.md
│   │   ├── open-source-references.md
│   │   ├── reconciliation-break-taxonomy.md
│   │   ├── research-briefing-workflow.md
│   │   └── strategy-promotion-history.md
│   ├── screenshots
│   │   ├── README.md
│   │   └── desktop
│   │       └── manuals
│   │           ├── manual-data-operations
│   │           │   ├── 01-data-operations-shell.png
│   │           │   ├── 02-providers.png
│   │           │   ├── 03-provider-health.png
│   │           │   ├── 04-backfill.png
│   │           │   ├── 05-data-sources.png
│   │           │   ├── 06-storage.png
│   │           │   └── 07-data-quality.png
│   │           ├── manual-governance
│   │           │   ├── 01-governance-shell.png
│   │           │   ├── 02-fund-ledger.png
│   │           │   ├── 03-fund-reconciliation.png
│   │           │   ├── 04-fund-report-pack.png
│   │           │   ├── 05-security-master.png
│   │           │   └── 06-settings.png
│   │           ├── manual-overview
│   │           │   ├── 01-research-workspace.png
│   │           │   ├── 02-workspace-layouts.png
│   │           │   ├── 03-research-workspace.png
│   │           │   ├── 04-trading-workspace.png
│   │           │   ├── 05-data-operations-workspace.png
│   │           │   ├── 06-governance-workspace.png
│   │           │   └── 07-help.png
│   │           └── manual-research-and-trading
│   │               ├── 01-research-shell.png
│   │               ├── 02-backtest.png
│   │               ├── 03-strategy-runs.png
│   │               ├── 04-quant-script.png
│   │               ├── 05-trading-shell.png
│   │               ├── 06-position-blotter.png
│   │               └── 07-run-risk.png
│   ├── security
│   │   ├── README.md
│   │   └── known-vulnerabilities.md
│   ├── status
│   │   ├── CHANGELOG.md
│   │   ├── EVALUATIONS_AND_AUDITS.md
│   │   ├── FEATURE_INVENTORY.md
│   │   ├── FULL_IMPLEMENTATION_TODO_2026_03_20.md
│   │   ├── IMPROVEMENTS.md
│   │   ├── KERNEL_PARITY_STATUS.md
│   │   ├── OPPORTUNITY_SCAN.md
│   │   ├── PROGRAM_STATE.md
│   │   ├── README.md
│   │   ├── ROADMAP.md
│   │   ├── ROADMAP_COMBINED.md
│   │   ├── TARGET_END_PRODUCT.md
│   │   ├── TODO.md
│   │   ├── api-docs-report.md
│   │   ├── badge-sync-report.md
│   │   ├── contract-compatibility-matrix.md
│   │   ├── coverage-report.md
│   │   ├── dk1-baseline-trust-thresholds.md
│   │   ├── dk1-pilot-parity-runbook.md
│   │   ├── dk1-trust-rationale-mapping.md
│   │   ├── docs-automation-summary.json
│   │   ├── docs-automation-summary.md
│   │   ├── example-validation.md
│   │   ├── health-dashboard.md
│   │   ├── kernel-readiness-dashboard.md
│   │   ├── link-repair-report.md
│   │   ├── metrics-dashboard.md
│   │   ├── production-status.md
│   │   ├── program-state-summary.json
│   │   ├── program-state-summary.md
│   │   ├── provider-validation-matrix.md
│   │   ├── rules-report.md
│   │   ├── run-contract.schema.json
│   │   ├── wave4-evidence-template.md
│   │   ├── workflow-drift-report.md
│   │   ├── workflow-manifest.json
│   │   └── workflow-validation-summary.json
│   ├── testing
│   │   ├── README.md
│   │   ├── WAVE2_ACCEPTANCE_GATE_CHECKLIST.md
│   │   └── WAVE2_ACCEPTANCE_TESTS.md
│   ├── toc.yml
│   └── ui
│       ├── README.md
│       └── components.md
├── environment.yml
├── global.json
├── make
│   ├── ai.mk
│   ├── build.mk
│   ├── desktop.mk
│   ├── diagnostics.mk
│   ├── docs.mk
│   ├── install.mk
│   └── test.mk
├── native
│   └── cpptrader-host
│       ├── CMakeLists.txt
│       ├── README.md
│       └── src
│           └── main.cpp
├── package-lock.json
├── package.json
├── plugins
│   ├── README.md
│   ├── frontend-web-dev
│   │   ├── .github
│   │   │   └── plugin
│   │   │       └── plugin.json
│   │   ├── README.md
│   │   ├── agents
│   │   │   ├── electron-angular-native.md
│   │   │   └── expert-react-frontend-engineer.md
│   │   └── skills
│   │       ├── playwright-explore-website
│   │       │   └── SKILL.md
│   │       └── playwright-generate-test
│   │           └── SKILL.md
│   ├── security-best-practices
│   │   ├── .github
│   │   │   └── plugin
│   │   │       └── plugin.json
│   │   ├── README.md
│   │   └── skills
│   │       └── ai-prompt-engineering-safety-review
│   │           └── SKILL.md
│   └── testing-automation
│       ├── .github
│       │   └── plugin
│       │       └── plugin.json
│       ├── README.md
│       ├── agents
│       │   ├── playwright-tester.md
│       │   ├── tdd-green.md
│       │   ├── tdd-red.md
│       │   └── tdd-refactor.md
│       └── skills
│           ├── ai-prompt-engineering-safety-review
│           │   └── SKILL.md
│           ├── csharp-nunit
│           │   └── SKILL.md
│           ├── java-junit
│           │   └── SKILL.md
│           ├── playwright-explore-website
│           │   └── SKILL.md
│           └── playwright-generate-test
│               └── SKILL.md
├── scripts
│   ├── ai
│   │   ├── cleanup.sh
│   │   ├── common.sh
│   │   ├── maintenance-full.sh
│   │   ├── maintenance-light.sh
│   │   ├── maintenance.sh
│   │   ├── route-maintenance.sh
│   │   ├── setup-ai-agent.sh
│   │   └── setup.sh
│   ├── check_contract_compatibility_gate.py
│   ├── check_program_state_consistency.py
│   ├── check_workflow_docs_parity.py
│   ├── compare_benchmarks.py
│   ├── compare_run_contract.py
│   ├── dev
│   │   ├── SharedBuild.ps1
│   │   ├── SharedCheckpoint.ps1
│   │   ├── SharedPreflight.ps1
│   │   ├── SharedWorkflowProfiles.ps1
│   │   ├── build-ibapi-smoke.ps1
│   │   ├── capture-desktop-screenshots.ps1
│   │   ├── capture-web-screenshots.mjs
│   │   ├── cleanup-generated.ps1
│   │   ├── desktop-dev.ps1
│   │   ├── desktop-workflows.json
│   │   ├── diagnose-uwp-xaml.ps1
│   │   ├── fixtures
│   │   │   └── robinhood-options-smoke.seed.json
│   │   ├── generate-desktop-user-manual.ps1
│   │   ├── generate-dk1-pilot-parity-packet.ps1
│   │   ├── install-git-hooks.sh
│   │   ├── preflight_runner.py
│   │   ├── prepare-dk1-operator-signoff.ps1
│   │   ├── robinhood-options-smoke.ps1
│   │   ├── run-desktop-workflow.ps1
│   │   ├── run-desktop.ps1
│   │   ├── run-wave1-provider-validation.ps1
│   │   ├── screenshot-diff-config.json
│   │   ├── screenshot_diff_report.py
│   │   ├── screenshot_workflow_plan.py
│   │   ├── shared
│   │   │   └── retry.ps1
│   │   ├── summarize-desktop-workflow-bundle.ps1
│   │   ├── validate-operator-inbox-route.ps1
│   │   ├── validate-position-blotter-route.ps1
│   │   ├── validate-screenshot-contract.py
│   │   ├── validate-workflow-profile.ps1
│   │   ├── web-screenshot-fixtures.json
│   │   ├── web-screenshot-routes.json
│   │   └── workflow-profiles
│   │       ├── debug-startup.json
│   │       ├── manual-data-operations.json
│   │       ├── manual-governance.json
│   │       ├── manual-overview.json
│   │       ├── manual-research-and-trading.json
│   │       └── screenshot-catalog.json
│   ├── example-sharpe.csx
│   ├── generate-diagrams.mjs
│   ├── generate_contract_review_packet.py
│   ├── generate_program_state_summary.py
│   ├── lib
│   │   ├── ui-diagram-generator.mjs
│   │   └── ui-diagram-generator.test.mjs
│   ├── report_canonicalization_drift.py
│   └── wpf_finance_ux_checks.py
├── src
│   ├── Meridian
│   │   ├── DashboardServerBridge.cs
│   │   ├── GlobalUsings.cs
│   │   ├── HostedBrokerageGatewayServiceCollectionExtensions.cs
│   │   ├── Integrations
│   │   │   └── Lean
│   │   │       ├── MeridianDataProvider.cs
│   │   │       ├── MeridianQuoteData.cs
│   │   │       ├── MeridianTradeData.cs
│   │   │       ├── README.md
│   │   │       └── SampleLeanAlgorithm.cs
│   │   ├── Meridian.csproj
│   │   ├── Program.cs
│   │   ├── Tools
│   │   │   └── DataValidator.cs
│   │   ├── UiServer.cs
│   │   ├── app.ico
│   │   ├── app.manifest
│   │   └── runtimeconfig.template.json
│   ├── Meridian.Application
│   │   ├── Accounts
│   │   │   ├── IAccountManagementService.cs
│   │   │   └── IAccountQueryService.cs
│   │   ├── Backfill
│   │   │   ├── AutoGapRemediationService.cs
│   │   │   ├── BackfillCoordinatorExecutionGateway.cs
│   │   │   ├── BackfillCostEstimator.cs
│   │   │   ├── BackfillRequest.cs
│   │   │   ├── BackfillResult.cs
│   │   │   ├── BackfillStatusStore.cs
│   │   │   ├── BackfillStatusStoreJsonContext.cs
│   │   │   ├── GapBackfillService.cs
│   │   │   ├── HistoricalBackfillService.cs
│   │   │   ├── IBackfillExecutionGateway.cs
│   │   │   └── SymbolValidationSignal.cs
│   │   ├── Backtesting
│   │   │   ├── BacktestPreflightService.cs
│   │   │   └── BacktestStudioContracts.cs
│   │   ├── Banking
│   │   │   ├── BankingException.cs
│   │   │   ├── IBankingService.cs
│   │   │   └── InMemoryBankingService.cs
│   │   ├── Canonicalization
│   │   │   ├── CanonicalizationMetrics.cs
│   │   │   ├── CanonicalizingPublisher.cs
│   │   │   ├── ConditionCodeMapper.cs
│   │   │   ├── EventCanonicalizer.cs
│   │   │   ├── IEventCanonicalizer.cs
│   │   │   └── VenueMicMapper.cs
│   │   ├── CertificatesOfDeposit
│   │   │   ├── CertificateOfDepositProjectionService.cs
│   │   │   └── ICertificateOfDepositReferenceService.cs
│   │   ├── Commands
│   │   │   ├── CatalogCommand.cs
│   │   │   ├── CliArguments.cs
│   │   │   ├── CommandDispatcher.cs
│   │   │   ├── ConfigCommands.cs
│   │   │   ├── ConfigPresetCommand.cs
│   │   │   ├── DiagnosticsCommands.cs
│   │   │   ├── DryRunCommand.cs
│   │   │   ├── EtlCommands.cs
│   │   │   ├── GenerateLoaderCommand.cs
│   │   │   ├── HelpCommand.cs
│   │   │   ├── ICliCommand.cs
│   │   │   ├── LedgerCliCommand.cs
│   │   │   ├── PackageCommands.cs
│   │   │   ├── ProviderCalibrationCommand.cs
│   │   │   ├── QueryCommand.cs
│   │   │   ├── RunbookCommands.cs
│   │   │   ├── SchemaCheckCommand.cs
│   │   │   ├── SecurityMasterCommands.cs
│   │   │   ├── SelfTestCommand.cs
│   │   │   ├── StatementCommands.cs
│   │   │   ├── StatementImportCommands.cs
│   │   │   ├── SymbolCommands.cs
│   │   │   ├── ValidateConfigCommand.cs
│   │   │   └── WalRepairCommand.cs
│   │   ├── Commodities
│   │   │   ├── CommodityProjectionService.cs
│   │   │   └── ICommodityReferenceService.cs
│   │   ├── Composition
│   │   │   ├── CircuitBreakerCallbackRouter.cs
│   │   │   ├── DirectLendingStartup.cs
│   │   │   ├── Features
│   │   │   │   ├── BackfillFeatureRegistration.cs
│   │   │   │   ├── CanonicalizationFeatureRegistration.cs
│   │   │   │   ├── CollectorFeatureRegistration.cs
│   │   │   │   ├── ConfigurationFeatureRegistration.cs
│   │   │   │   ├── CoordinationFeatureRegistration.cs
│   │   │   │   ├── CredentialFeatureRegistration.cs
│   │   │   │   ├── DiagnosticsFeatureRegistration.cs
│   │   │   │   ├── EtlFeatureRegistration.cs
│   │   │   │   ├── HttpClientFeatureRegistration.cs
│   │   │   │   ├── IServiceFeatureRegistration.cs
│   │   │   │   ├── LedgerFeatureRegistration.cs
│   │   │   │   ├── MaintenanceFeatureRegistration.cs
│   │   │   │   ├── PipelineFeatureRegistration.cs
│   │   │   │   ├── ProviderFeatureRegistration.cs
│   │   │   │   ├── ProviderRoutingFeatureRegistration.cs
│   │   │   │   ├── StorageFeatureRegistration.cs
│   │   │   │   └── SymbolManagementFeatureRegistration.cs
│   │   │   ├── FundAccountsStartup.cs
│   │   │   ├── HostAdapters.cs
│   │   │   ├── HostStartup.cs
│   │   │   ├── SecurityMasterStartup.cs
│   │   │   ├── ServiceCompositionRoot.cs
│   │   │   └── Startup
│   │   │       ├── ApplicationLifecycleCoordinator.cs
│   │   │       ├── ModeRunners
│   │   │       │   ├── BackfillModeRunner.cs
│   │   │       │   ├── CollectorModeRunner.cs
│   │   │       │   ├── CommandModeRunner.cs
│   │   │       │   └── DesktopModeRunner.cs
│   │   │       ├── SharedStartupBootstrapper.cs
│   │   │       ├── StartupModels
│   │   │       │   ├── HostMode.cs
│   │   │       │   ├── StartupContext.cs
│   │   │       │   ├── StartupPlan.cs
│   │   │       │   ├── StartupRequest.cs
│   │   │       │   └── StartupValidationResult.cs
│   │   │       └── StartupOrchestrator.cs
│   │   ├── Config
│   │   │   ├── AppConfigJsonOptions.cs
│   │   │   ├── ConfigDtoMapper.cs
│   │   │   ├── ConfigJsonSchemaGenerator.cs
│   │   │   ├── ConfigValidationHelper.cs
│   │   │   ├── ConfigValidatorCli.cs
│   │   │   ├── ConfigWatcher.cs
│   │   │   ├── ConfigurationPipeline.cs
│   │   │   ├── CredentialPlaceholderDetector.cs
│   │   │   ├── Credentials
│   │   │   │   ├── CredentialStatus.cs
│   │   │   │   ├── CredentialTestingService.cs
│   │   │   │   ├── FileProviderCredentialStore.cs
│   │   │   │   ├── IProviderCredentialStore.cs
│   │   │   │   ├── OAuthToken.cs
│   │   │   │   ├── OAuthTokenRefreshService.cs
│   │   │   │   ├── ProviderCredentialCatalog.cs
│   │   │   │   └── ProviderCredentialResolver.cs
│   │   │   ├── DefaultConfigPathResolver.cs
│   │   │   ├── DeploymentContext.cs
│   │   │   ├── IConfigValidator.cs
│   │   │   ├── SensitiveValueMasker.cs
│   │   │   ├── StorageConfigExtensions.cs
│   │   │   └── StorageConfigRules.cs
│   │   ├── Coordination
│   │   │   ├── ClusterCoordinatorService.cs
│   │   │   ├── CoordinationSnapshot.cs
│   │   │   ├── IClusterCoordinator.cs
│   │   │   ├── ICoordinationStore.cs
│   │   │   ├── ILeaseManager.cs
│   │   │   ├── IScheduledWorkOwnershipService.cs
│   │   │   ├── ISubscriptionOwnershipService.cs
│   │   │   ├── LeaseAcquireResult.cs
│   │   │   ├── LeaseManager.cs
│   │   │   ├── LeaseRecord.cs
│   │   │   ├── ScheduledWorkOwnershipService.cs
│   │   │   ├── SharedStorageCoordinationStore.cs
│   │   │   ├── SplitBrainDetector.cs
│   │   │   └── SubscriptionOwnershipService.cs
│   │   ├── Credentials
│   │   │   └── ICredentialStore.cs
│   │   ├── CryptoCurrency
│   │   │   ├── CryptoProjectionService.cs
│   │   │   └── ICryptoReferenceService.cs
│   │   ├── Deposits
│   │   │   ├── DepositProjectionService.cs
│   │   │   └── IDepositReferenceService.cs
│   │   ├── Derivatives
│   │   │   ├── ISwapReferenceService.cs
│   │   │   └── SwapProjectionService.cs
│   │   ├── DirectLending
│   │   │   ├── AccrualLedgerService.cs
│   │   │   ├── DailyAccrualWorker.cs
│   │   │   ├── DirectLendingEventRebuilder.cs
│   │   │   ├── DirectLendingOutboxDispatcher.cs
│   │   │   ├── DirectLendingServiceSupport.cs
│   │   │   ├── DirectLendingWorkflowSupport.cs
│   │   │   ├── DirectLendingWorkflowTopics.cs
│   │   │   ├── IAccrualLedgerService.cs
│   │   │   ├── IDirectLendingCommandService.cs
│   │   │   ├── IDirectLendingQueryService.cs
│   │   │   ├── IDirectLendingService.cs
│   │   │   ├── InMemoryDirectLendingService.Workflows.cs
│   │   │   ├── InMemoryDirectLendingService.cs
│   │   │   ├── LoanAccountingProjector.cs
│   │   │   ├── PostgresDirectLendingCommandService.cs
│   │   │   ├── PostgresDirectLendingQueryService.cs
│   │   │   └── PostgresDirectLendingService.cs
│   │   ├── EnvironmentDesign
│   │   │   ├── EnvironmentDesignerService.cs
│   │   │   ├── IEnvironmentDesignService.cs
│   │   │   ├── IEnvironmentPublishService.cs
│   │   │   ├── IEnvironmentRuntimeProjectionService.cs
│   │   │   └── IEnvironmentValidationService.cs
│   │   ├── Equity
│   │   │   ├── EquityProjectionService.cs
│   │   │   └── IEquityReferenceService.cs
│   │   ├── Etl
│   │   │   ├── EtlAbstractions.cs
│   │   │   └── EtlServices.cs
│   │   ├── Filters
│   │   │   └── MarketEventFilter.cs
│   │   ├── FixedIncome
│   │   │   ├── BondProjectionService.cs
│   │   │   └── IBondReferenceService.cs
│   │   ├── FundAccounts
│   │   │   ├── IFundAccountService.cs
│   │   │   └── InMemoryFundAccountService.cs
│   │   ├── FundStructure
│   │   │   ├── FundAccountTraversalQueryService.cs
│   │   │   ├── GovernanceSharedDataAccessService.cs
│   │   │   ├── IFundAccountTraversalQueryService.cs
│   │   │   ├── IFundStructureService.cs
│   │   │   ├── IGovernanceSharedDataAccessService.cs
│   │   │   ├── InMemoryFundStructureService.cs
│   │   │   └── LedgerGroupingRules.cs
│   │   ├── Futures
│   │   │   ├── FutureProjectionService.cs
│   │   │   └── IFutureReferenceService.cs
│   │   ├── FxSpot
│   │   │   ├── FxSpotProjectionService.cs
│   │   │   └── IFxSpotReferenceService.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Http
│   │   │   ├── BackfillCoordinator.cs
│   │   │   ├── ConfigStore.cs
│   │   │   └── Endpoints
│   │   │       ├── ArchiveMaintenanceEndpoints.cs
│   │   │       ├── DataQualityEndpoints.cs
│   │   │       ├── PackagingEndpoints.cs
│   │   │       └── StatusEndpointHandlers.cs
│   │   ├── Indicators
│   │   │   └── TechnicalIndicatorService.cs
│   │   ├── Ledger
│   │   │   ├── AccountingPolicyService.cs
│   │   │   └── TextJournal
│   │   │       ├── LedgerTextJournalDocument.cs
│   │   │       ├── LedgerTextJournalException.cs
│   │   │       ├── LedgerTextJournalParser.cs
│   │   │       ├── LedgerTextJournalReportService.cs
│   │   │       ├── LedgerTextReportOptions.cs
│   │   │       ├── LedgerTextReportRenderer.cs
│   │   │       └── LedgerTextTransaction.cs
│   │   ├── Meridian.Application.csproj
│   │   ├── MoneyMarketFunds
│   │   │   ├── IMoneyMarketFundReferenceService.cs
│   │   │   └── MoneyMarketFundProjectionService.cs
│   │   ├── Monitoring
│   │   │   ├── BackpressureAlertService.cs
│   │   │   ├── BadTickFilter.cs
│   │   │   ├── CircuitBreakerStatusService.cs
│   │   │   ├── ClockSkewEstimator.cs
│   │   │   ├── ConnectionHealthMonitor.cs
│   │   │   ├── ConnectionStatusWebhook.cs
│   │   │   ├── Core
│   │   │   │   ├── AlertDispatcher.cs
│   │   │   │   ├── AlertRunbookRegistry.cs
│   │   │   │   ├── HealthCheckAggregator.cs
│   │   │   │   └── SloDefinitionRegistry.cs
│   │   │   ├── DataLossAccounting.cs
│   │   │   ├── DataQuality
│   │   │   │   ├── AnomalyDetector.cs
│   │   │   │   ├── CompletenessScoreCalculator.cs
│   │   │   │   ├── CrossProviderComparisonService.cs
│   │   │   │   ├── DataFreshnessSlaMonitor.cs
│   │   │   │   ├── DataQualityModels.cs
│   │   │   │   ├── DataQualityMonitoringService.cs
│   │   │   │   ├── DataQualityReportGenerator.cs
│   │   │   │   ├── GapAnalyzer.cs
│   │   │   │   ├── IQualityAnalyzer.cs
│   │   │   │   ├── LatencyHistogram.cs
│   │   │   │   ├── LiquidityProfileProvider.cs
│   │   │   │   ├── PriceContinuityChecker.cs
│   │   │   │   └── SequenceErrorTracker.cs
│   │   │   ├── DetailedHealthCheck.cs
│   │   │   ├── ErrorRingBuffer.cs
│   │   │   ├── IEventMetrics.cs
│   │   │   ├── Metrics.cs
│   │   │   ├── PrometheusMetrics.cs
│   │   │   ├── ProviderDegradationCalibration.cs
│   │   │   ├── ProviderDegradationScorer.cs
│   │   │   ├── ProviderLatencyService.cs
│   │   │   ├── ProviderMetricsStatus.cs
│   │   │   ├── SchemaValidationService.cs
│   │   │   ├── SpreadMonitor.cs
│   │   │   ├── StatusHttpServer.cs
│   │   │   ├── StatusSnapshot.cs
│   │   │   ├── StatusWriter.cs
│   │   │   ├── SystemHealthChecker.cs
│   │   │   ├── TickSizeValidator.cs
│   │   │   ├── TimestampMonotonicityChecker.cs
│   │   │   └── ValidationMetrics.cs
│   │   ├── Options
│   │   │   ├── IOptionChainImportService.cs
│   │   │   ├── IOptionReferenceService.cs
│   │   │   └── OptionProjectionService.cs
│   │   ├── Pipeline
│   │   │   ├── DeadLetterSink.cs
│   │   │   ├── DroppedEventAuditTrail.cs
│   │   │   ├── DualPathEventPipeline.cs
│   │   │   ├── EventPipeline.cs
│   │   │   ├── FSharpEventValidator.cs
│   │   │   ├── HotPathBatchSerializer.cs
│   │   │   ├── IDedupStore.cs
│   │   │   ├── IEventValidator.cs
│   │   │   ├── IngestionJobService.cs
│   │   │   ├── PersistentDedupLedger.cs
│   │   │   └── SchemaUpcasterRegistry.cs
│   │   ├── ProviderRouting
│   │   │   ├── BestOfBreedProviderSelector.cs
│   │   │   ├── KernelObservabilityService.cs
│   │   │   ├── ProviderBindingService.cs
│   │   │   ├── ProviderConnectionService.cs
│   │   │   ├── ProviderOperationsSupportServices.cs
│   │   │   ├── ProviderRoutingEngine.cs
│   │   │   └── ProviderRoutingMapper.cs
│   │   ├── Reconciliation
│   │   │   └── StatementReconciliationService.cs
│   │   ├── Results
│   │   │   ├── ErrorCode.cs
│   │   │   ├── OperationError.cs
│   │   │   └── Result.cs
│   │   ├── Runbooks
│   │   │   ├── RunbookExecutor.cs
│   │   │   ├── RunbookModels.cs
│   │   │   └── RunbookStore.cs
│   │   ├── Scheduling
│   │   │   ├── BackfillExecutionLog.cs
│   │   │   ├── BackfillSchedule.cs
│   │   │   ├── BackfillScheduleManager.cs
│   │   │   ├── IOperationalScheduler.cs
│   │   │   ├── OperationalScheduler.cs
│   │   │   └── ScheduledBackfillService.cs
│   │   ├── SecurityMaster
│   │   │   ├── EdgarIngestOrchestrator.cs
│   │   │   ├── IEdgarIngestOrchestrator.cs
│   │   │   ├── ILivePositionCorporateActionAdjuster.cs
│   │   │   ├── ISecurityMasterQueryService.cs
│   │   │   ├── ISecurityMasterService.cs
│   │   │   ├── ISecurityMasterWorkbenchQueryService.cs
│   │   │   ├── ISecurityResolver.cs
│   │   │   ├── IUflProjectionRebuilder.cs
│   │   │   ├── NullSecurityMasterServices.cs
│   │   │   ├── SecurityEconomicDefinitionAdapter.cs
│   │   │   ├── SecurityKindMapping.cs
│   │   │   ├── SecurityMasterAggregateRebuilder.cs
│   │   │   ├── SecurityMasterCanonicalSymbolSeedService.cs
│   │   │   ├── SecurityMasterConflictService.cs
│   │   │   ├── SecurityMasterCsvParser.cs
│   │   │   ├── SecurityMasterImportService.cs
│   │   │   ├── SecurityMasterIngestStatusService.cs
│   │   │   ├── SecurityMasterLedgerBridge.cs
│   │   │   ├── SecurityMasterMapping.cs
│   │   │   ├── SecurityMasterOptionsValidator.cs
│   │   │   ├── SecurityMasterProjectionService.cs
│   │   │   ├── SecurityMasterProjectionWarmupService.cs
│   │   │   ├── SecurityMasterQueryService.cs
│   │   │   ├── SecurityMasterRebuildOrchestrator.cs
│   │   │   ├── SecurityMasterService.cs
│   │   │   ├── SecurityResolver.cs
│   │   │   └── UflProjectionRebuilder.cs
│   │   ├── Services
│   │   │   ├── ApiDocumentationService.cs
│   │   │   ├── AutoConfigurationService.cs
│   │   │   ├── CanonicalSymbolRegistry.cs
│   │   │   ├── CliModeResolver.cs
│   │   │   ├── CoLocationProfileActivator.cs
│   │   │   ├── ConfigEnvironmentOverride.cs
│   │   │   ├── ConfigTemplateGenerator.cs
│   │   │   ├── ConfigurationService.cs
│   │   │   ├── ConfigurationServiceCredentialAdapter.cs
│   │   │   ├── ConfigurationWizard.cs
│   │   │   ├── ConnectivityProbeService.cs
│   │   │   ├── ConnectivityTestService.cs
│   │   │   ├── CredentialValidationService.cs
│   │   │   ├── DailySummaryWebhook.cs
│   │   │   ├── DiagnosticBundleService.cs
│   │   │   ├── DryRunService.cs
│   │   │   ├── ErrorTracker.cs
│   │   │   ├── FriendlyErrorFormatter.cs
│   │   │   ├── GovernanceExceptionService.cs
│   │   │   ├── GracefulShutdownHandler.cs
│   │   │   ├── GracefulShutdownService.cs
│   │   │   ├── HistoricalDataQueryService.cs
│   │   │   ├── NavAttributionService.cs
│   │   │   ├── OptionsChainService.cs
│   │   │   ├── PluginLoaderService.cs
│   │   │   ├── PreflightChecker.cs
│   │   │   ├── ProgressDisplayService.cs
│   │   │   ├── ReconciliationEngineService.cs
│   │   │   ├── ReportGenerationService.cs
│   │   │   ├── SampleDataGenerator.cs
│   │   │   ├── ServiceRegistry.cs
│   │   │   ├── StartupSummary.cs
│   │   │   └── TradingCalendar.cs
│   │   ├── Subscriptions
│   │   │   ├── Services
│   │   │   │   ├── AutoResubscribePolicy.cs
│   │   │   │   ├── BatchOperationsService.cs
│   │   │   │   ├── IndexSubscriptionService.cs
│   │   │   │   ├── MetadataEnrichmentService.cs
│   │   │   │   ├── PortfolioImportService.cs
│   │   │   │   ├── SchedulingService.cs
│   │   │   │   ├── SymbolImportExportService.cs
│   │   │   │   ├── SymbolManagementService.cs
│   │   │   │   ├── SymbolSearchService.cs
│   │   │   │   ├── TemplateService.cs
│   │   │   │   └── WatchlistService.cs
│   │   │   └── SubscriptionOrchestrator.cs
│   │   ├── Testing
│   │   │   └── DepthBufferSelfTests.cs
│   │   ├── Tracing
│   │   │   ├── EventTraceContext.cs
│   │   │   ├── OpenTelemetrySetup.cs
│   │   │   └── TracedEventMetrics.cs
│   │   ├── Treasury
│   │   │   ├── IMmfLiquidityService.cs
│   │   │   ├── IMoneyMarketFundService.cs
│   │   │   └── InMemoryMoneyMarketFundService.cs
│   │   └── Wizard
│   │       ├── Core
│   │       │   ├── IWizardStep.cs
│   │       │   ├── WizardContext.cs
│   │       │   ├── WizardCoordinator.cs
│   │       │   ├── WizardStepId.cs
│   │       │   ├── WizardStepResult.cs
│   │       │   ├── WizardStepStatus.cs
│   │       │   ├── WizardSummary.cs
│   │       │   └── WizardTransition.cs
│   │       ├── Metadata
│   │       │   ├── ProviderDescriptor.cs
│   │       │   └── ProviderRegistry.cs
│   │       ├── Steps
│   │       │   ├── ConfigureBackfillStep.cs
│   │       │   ├── ConfigureDataSourceStep.cs
│   │       │   ├── ConfigureStorageStep.cs
│   │       │   ├── ConfigureSymbolsStep.cs
│   │       │   ├── CredentialGuidanceStep.cs
│   │       │   ├── DetectProvidersStep.cs
│   │       │   ├── ReviewConfigurationStep.cs
│   │       │   ├── SaveConfigurationStep.cs
│   │       │   ├── SelectUseCaseStep.cs
│   │       │   └── ValidateCredentialsStep.cs
│   │       └── WizardWorkflowFactory.cs
│   ├── Meridian.Backtesting
│   │   ├── BacktestStudioRunOrchestrator.cs
│   │   ├── BatchBacktestService.cs
│   │   ├── CorporateActionAdjustmentService.cs
│   │   ├── Engine
│   │   │   ├── BacktestContext.cs
│   │   │   ├── BacktestEngine.cs
│   │   │   ├── ContingentOrderManager.cs
│   │   │   ├── MultiSymbolMergeEnumerator.cs
│   │   │   ├── StageTimer.cs
│   │   │   └── UniverseDiscovery.cs
│   │   ├── FillModels
│   │   │   ├── BarMidpointFillModel.cs
│   │   │   ├── IFillModel.cs
│   │   │   ├── MarketImpactFillModel.cs
│   │   │   ├── OrderBookFillModel.cs
│   │   │   └── OrderFillResult.cs
│   │   ├── GlobalUsings.cs
│   │   ├── ICorporateActionAdjustmentService.cs
│   │   ├── Meridian.Backtesting.csproj
│   │   ├── MeridianNativeBacktestStudioEngine.cs
│   │   ├── Metrics
│   │   │   ├── BacktestMetricsEngine.cs
│   │   │   ├── PostSimulationTcaReporter.cs
│   │   │   └── XirrCalculator.cs
│   │   ├── Plugins
│   │   │   └── StrategyPluginLoader.cs
│   │   └── Portfolio
│   │       ├── ICommissionModel.cs
│   │       ├── LinkedListExtensions.cs
│   │       └── SimulatedPortfolio.cs
│   ├── Meridian.Backtesting.Sdk
│   │   ├── AssetEvent.cs
│   │   ├── BacktestEngineMode.cs
│   │   ├── BacktestProgressEvent.cs
│   │   ├── BacktestRequest.cs
│   │   ├── BacktestResult.cs
│   │   ├── BacktestStage.cs
│   │   ├── BacktestStageTelemetryDto.cs
│   │   ├── CashFlowEntry.cs
│   │   ├── ClosedLot.cs
│   │   ├── FillEvent.cs
│   │   ├── FinancialAccount.cs
│   │   ├── FinancialAccountSnapshot.cs
│   │   ├── GlobalUsings.cs
│   │   ├── IBacktestContext.cs
│   │   ├── IBacktestStrategy.cs
│   │   ├── IntermediateMetrics.cs
│   │   ├── LotSelectionMethod.cs
│   │   ├── Meridian.Backtesting.Sdk.csproj
│   │   ├── OpenLot.cs
│   │   ├── Order.cs
│   │   ├── PortfolioSnapshot.cs
│   │   ├── Position.cs
│   │   ├── Strategies
│   │   │   ├── AdvancedCarry
│   │   │   │   ├── AdvancedCarryDecisionEngine.cs
│   │   │   │   ├── AdvancedCarryModels.cs
│   │   │   │   └── CarryTradeBacktestStrategy.cs
│   │   │   └── OptionsOverwrite
│   │   │       ├── BlackScholesCalculator.cs
│   │   │       ├── CoveredCallOverwriteStrategy.cs
│   │   │       ├── OptionsOverwriteFilters.cs
│   │   │       ├── OptionsOverwriteMetricsCalculator.cs
│   │   │       ├── OptionsOverwriteModels.cs
│   │   │       ├── OptionsOverwriteParams.cs
│   │   │       └── OptionsOverwriteScoring.cs
│   │   ├── StrategyParameterAttribute.cs
│   │   ├── TcaReportModels.cs
│   │   └── TradeTicket.cs
│   ├── Meridian.Contracts
│   │   ├── Api
│   │   │   ├── BackfillApiModels.cs
│   │   │   ├── ClientModels.cs
│   │   │   ├── DataIngestionContracts.cs
│   │   │   ├── ErrorResponse.cs
│   │   │   ├── ExecutionApiModels.cs
│   │   │   ├── LeanApiModels.cs
│   │   │   ├── LiveDataModels.cs
│   │   │   ├── OptionsModels.cs
│   │   │   ├── PositionLotModels.cs
│   │   │   ├── ProviderCatalog.cs
│   │   │   ├── ProviderRoutingApiModels.cs
│   │   │   ├── Quality
│   │   │   │   └── QualityApiModels.cs
│   │   │   ├── SecurityMasterIngestStatusModels.cs
│   │   │   ├── StatusEndpointModels.cs
│   │   │   ├── StatusModels.cs
│   │   │   ├── UiApiClient.cs
│   │   │   ├── UiApiRoutes.cs
│   │   │   └── UiDashboardModels.cs
│   │   ├── Archive
│   │   │   └── ArchiveHealthModels.cs
│   │   ├── Auth
│   │   │   ├── RolePermissions.cs
│   │   │   ├── UserPermission.cs
│   │   │   └── UserRole.cs
│   │   ├── Backfill
│   │   │   └── BackfillProgress.cs
│   │   ├── Backtesting
│   │   │   └── BacktestPreflightDtos.cs
│   │   ├── Banking
│   │   │   └── BankingModels.cs
│   │   ├── Catalog
│   │   │   ├── DirectoryIndex.cs
│   │   │   ├── ICanonicalSymbolRegistry.cs
│   │   │   ├── StorageCatalog.cs
│   │   │   └── SymbolRegistry.cs
│   │   ├── CertificatesOfDeposit
│   │   │   └── CertificateOfDepositReferenceDtos.cs
│   │   ├── Commodities
│   │   │   └── CommodityDtos.cs
│   │   ├── Configuration
│   │   │   ├── AppConfigDto.cs
│   │   │   ├── DerivativesConfigDto.cs
│   │   │   ├── MeridianPathDefaults.cs
│   │   │   ├── ProviderConnectionDtos.cs
│   │   │   ├── ProviderConnectionsConfigDto.cs
│   │   │   └── SymbolConfig.cs
│   │   ├── Credentials
│   │   │   ├── CredentialModels.cs
│   │   │   └── ISecretProvider.cs
│   │   ├── CryptoCurrency
│   │   │   └── CryptoDtos.cs
│   │   ├── Deposits
│   │   │   └── DepositDtos.cs
│   │   ├── Derivatives
│   │   │   └── SwapDtos.cs
│   │   ├── DirectLending
│   │   │   ├── DirectLendingCommandResults.cs
│   │   │   ├── DirectLendingDtos.cs
│   │   │   ├── DirectLendingOptions.cs
│   │   │   └── DirectLendingWorkflowDtos.cs
│   │   ├── Domain
│   │   │   ├── CanonicalSymbol.cs
│   │   │   ├── Enums
│   │   │   │   ├── AggressorSide.cs
│   │   │   │   ├── CanonicalTradeCondition.cs
│   │   │   │   ├── ConnectionStatus.cs
│   │   │   │   ├── DepthIntegrityKind.cs
│   │   │   │   ├── DepthOperation.cs
│   │   │   │   ├── InstrumentType.cs
│   │   │   │   ├── IntegritySeverity.cs
│   │   │   │   ├── LiquidityProfile.cs
│   │   │   │   ├── MarketEventTier.cs
│   │   │   │   ├── MarketEventType.cs
│   │   │   │   ├── MarketState.cs
│   │   │   │   ├── OptionRight.cs
│   │   │   │   ├── OptionStyle.cs
│   │   │   │   ├── OrderBookSide.cs
│   │   │   │   └── OrderSide.cs
│   │   │   ├── Events
│   │   │   │   ├── IMarketEventPayload.cs
│   │   │   │   ├── MarketEvent.cs
│   │   │   │   └── MarketEventPayload.cs
│   │   │   ├── IPositionSnapshotStore.cs
│   │   │   ├── MarketDataModels.cs
│   │   │   ├── Models
│   │   │   │   ├── AdjustedHistoricalBar.cs
│   │   │   │   ├── AggregateBarPayload.cs
│   │   │   │   ├── BboQuotePayload.cs
│   │   │   │   ├── DepthIntegrityEvent.cs
│   │   │   │   ├── GreeksSnapshot.cs
│   │   │   │   ├── HistoricalAuction.cs
│   │   │   │   ├── HistoricalBar.cs
│   │   │   │   ├── HistoricalQuote.cs
│   │   │   │   ├── HistoricalTrade.cs
│   │   │   │   ├── IntegrityEvent.cs
│   │   │   │   ├── L2SnapshotPayload.cs
│   │   │   │   ├── LOBSnapshot.cs
│   │   │   │   ├── MarketQuoteUpdate.cs
│   │   │   │   ├── OpenInterestUpdate.cs
│   │   │   │   ├── OptionChainSnapshot.cs
│   │   │   │   ├── OptionContractSpec.cs
│   │   │   │   ├── OptionQuote.cs
│   │   │   │   ├── OptionTrade.cs
│   │   │   │   ├── OrderAdd.cs
│   │   │   │   ├── OrderBookLevel.cs
│   │   │   │   ├── OrderCancel.cs
│   │   │   │   ├── OrderExecute.cs
│   │   │   │   ├── OrderFlowStatistics.cs
│   │   │   │   ├── OrderModify.cs
│   │   │   │   ├── OrderReplace.cs
│   │   │   │   ├── SessionStats.cs
│   │   │   │   └── Trade.cs
│   │   │   ├── ProviderId.cs
│   │   │   ├── ProviderSymbol.cs
│   │   │   ├── StreamId.cs
│   │   │   ├── SubscriptionId.cs
│   │   │   ├── SymbolId.cs
│   │   │   └── VenueCode.cs
│   │   ├── EnvironmentDesign
│   │   │   └── EnvironmentDesignDtos.cs
│   │   ├── Equity
│   │   │   └── EquityReferenceDtos.cs
│   │   ├── Etl
│   │   │   └── EtlModels.cs
│   │   ├── Export
│   │   │   ├── AnalysisExportModels.cs
│   │   │   ├── ExportPreset.cs
│   │   │   └── StandardPresets.cs
│   │   ├── FixedIncome
│   │   │   └── BondReferenceDtos.cs
│   │   ├── FundStructure
│   │   │   ├── AccountManagementDtos.cs
│   │   │   ├── AccountManagementOptions.cs
│   │   │   ├── FundStructureCommands.cs
│   │   │   ├── FundStructureContractsJsonContext.cs
│   │   │   ├── FundStructureDtos.cs
│   │   │   ├── FundStructureQueries.cs
│   │   │   └── LedgerGroupId.cs
│   │   ├── Futures
│   │   │   └── FutureReferenceDtos.cs
│   │   ├── FxSpot
│   │   │   └── FxSpotReferenceDtos.cs
│   │   ├── Ledger
│   │   │   └── LedgerBookDtos.cs
│   │   ├── Manifest
│   │   │   └── DataManifest.cs
│   │   ├── Meridian.Contracts.csproj
│   │   ├── MoneyMarketFunds
│   │   │   └── MoneyMarketFundReferenceDtos.cs
│   │   ├── Options
│   │   │   └── OptionReferenceDtos.cs
│   │   ├── Pipeline
│   │   │   ├── IngestionJob.cs
│   │   │   ├── PipelinePolicyConstants.cs
│   │   │   └── UflOutboxMessage.cs
│   │   ├── RuleEvaluation
│   │   │   └── DecisionContracts.cs
│   │   ├── Schema
│   │   │   ├── EventSchema.cs
│   │   │   └── ISchemaUpcaster.cs
│   │   ├── SecurityMaster
│   │   │   ├── EdgarReferenceDtos.cs
│   │   │   ├── ISecurityMasterAmender.cs
│   │   │   ├── ISecurityMasterQueryService.cs
│   │   │   ├── ISecurityMasterRuntimeStatus.cs
│   │   │   ├── ISecurityMasterService.cs
│   │   │   ├── OperatorOverrides.cs
│   │   │   ├── SecurityCommands.cs
│   │   │   ├── SecurityDtos.cs
│   │   │   ├── SecurityEvents.cs
│   │   │   ├── SecurityIdentifiers.cs
│   │   │   ├── SecurityMasterOptions.cs
│   │   │   └── SecurityQueries.cs
│   │   ├── Services
│   │   │   ├── IBacktestPreflightService.cs
│   │   │   └── IConnectivityProbeService.cs
│   │   ├── Session
│   │   │   └── CollectionSession.cs
│   │   ├── Store
│   │   │   └── MarketDataQuery.cs
│   │   ├── Treasury
│   │   │   └── MoneyMarketFundDtos.cs
│   │   └── Workstation
│   │       ├── BrokerageSyncDtos.cs
│   │       ├── EvidenceWorkflowDtos.cs
│   │       ├── FundLedgerDtos.cs
│   │       ├── FundOperationsDtos.cs
│   │       ├── FundOperationsWorkspaceDtos.cs
│   │       ├── IOperatorInboxService.cs
│   │       ├── PilotReadinessArtifactDtos.cs
│   │       ├── ReconciliationDtos.cs
│   │       ├── ResearchBriefingDtos.cs
│   │       ├── SecurityMasterTrustWorkbenchDtos.cs
│   │       ├── SecurityMasterWorkstationDtos.cs
│   │       ├── StrategyDesignDtos.cs
│   │       ├── StrategyRunContractCompatibility.cs
│   │       ├── StrategyRunReadModels.cs
│   │       ├── TradingOperatorReadinessDtos.cs
│   │       ├── WorkflowLibraryDtos.cs
│   │       ├── WorkflowSummaryDtos.cs
│   │       └── WorkstationBootstrapDtos.cs
│   ├── Meridian.Core
│   │   ├── Config
│   │   │   ├── AlpacaCredentialEnvironment.cs
│   │   │   ├── AlpacaOptions.cs
│   │   │   ├── AppConfig.cs
│   │   │   ├── BackfillConfig.cs
│   │   │   ├── CanonicalizationConfig.cs
│   │   │   ├── CoordinationConfig.cs
│   │   │   ├── DataSourceConfig.cs
│   │   │   ├── DataSourceKind.cs
│   │   │   ├── DataSourceKindConverter.cs
│   │   │   ├── DerivativesConfig.cs
│   │   │   ├── IConfigurationProvider.cs
│   │   │   ├── ProviderConnectionsConfig.cs
│   │   │   ├── SyntheticMarketDataConfig.cs
│   │   │   └── ValidatedConfig.cs
│   │   ├── Exceptions
│   │   │   ├── ConfigurationException.cs
│   │   │   ├── ConnectionException.cs
│   │   │   ├── DataProviderException.cs
│   │   │   ├── MeridianException.cs
│   │   │   ├── OperationTimeoutException.cs
│   │   │   ├── RateLimitException.cs
│   │   │   ├── SequenceValidationException.cs
│   │   │   ├── StorageException.cs
│   │   │   ├── UnsupportedAssetClassException.cs
│   │   │   └── ValidationException.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Logging
│   │   │   └── LoggingSetup.cs
│   │   ├── Meridian.Core.csproj
│   │   ├── Monitoring
│   │   │   ├── Core
│   │   │   │   ├── IAlertDispatcher.cs
│   │   │   │   └── IHealthCheckProvider.cs
│   │   │   ├── EventSchemaValidator.cs
│   │   │   ├── IConnectionHealthMonitor.cs
│   │   │   ├── IReconnectionMetrics.cs
│   │   │   └── MigrationDiagnostics.cs
│   │   ├── Performance
│   │   │   └── Performance
│   │   │       ├── ConnectionWarmUp.cs
│   │   │       ├── RawQuoteEvent.cs
│   │   │       ├── RawTradeEvent.cs
│   │   │       ├── SpscRingBuffer.cs
│   │   │       ├── SymbolTable.cs
│   │   │       └── ThreadingUtilities.cs
│   │   ├── Pipeline
│   │   │   └── EventPipelinePolicy.cs
│   │   ├── Scheduling
│   │   │   └── CronExpressionParser.cs
│   │   ├── Serialization
│   │   │   ├── MarketDataJsonContext.cs
│   │   │   └── SecurityMasterJsonContext.cs
│   │   ├── Services
│   │   │   └── IFlushable.cs
│   │   └── Subscriptions
│   │       └── Models
│   │           ├── BatchOperations.cs
│   │           ├── BulkImportExport.cs
│   │           ├── IndexComponents.cs
│   │           ├── PortfolioImport.cs
│   │           ├── ResubscriptionMetrics.cs
│   │           ├── SubscriptionSchedule.cs
│   │           ├── SymbolMetadata.cs
│   │           ├── SymbolSearchResult.cs
│   │           ├── SymbolTemplate.cs
│   │           └── Watchlist.cs
│   ├── Meridian.Domain
│   │   ├── BannedReferences.txt
│   │   ├── Collectors
│   │   │   ├── IQuoteStateStore.cs
│   │   │   ├── L3OrderBookCollector.cs
│   │   │   ├── MarketDepthCollector.cs
│   │   │   ├── OptionDataCollector.cs
│   │   │   ├── QuoteCollector.cs
│   │   │   ├── SessionStatsCollector.cs
│   │   │   ├── SymbolSubscriptionTracker.cs
│   │   │   └── TradeDataCollector.cs
│   │   ├── Events
│   │   │   ├── IBackpressureSignal.cs
│   │   │   ├── IMarketEventPublisher.cs
│   │   │   ├── MarketEvent.cs
│   │   │   ├── MarketEventPayload.cs
│   │   │   ├── PublishResult.cs
│   │   │   └── Publishers
│   │   │       └── CompositePublisher.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.Domain.csproj
│   │   ├── Models
│   │   │   ├── AggregateBar.cs
│   │   │   ├── MarketDepthUpdate.cs
│   │   │   └── MarketTradeUpdate.cs
│   │   ├── Reconciliation
│   │   │   ├── BrokerStatementModels.cs
│   │   │   ├── StatementEntities.cs
│   │   │   └── StatementReconciliationAggregate.cs
│   │   └── Telemetry
│   │       └── MarketEventIngressTracing.cs
│   ├── Meridian.Execution
│   │   ├── Adapters
│   │   │   ├── BaseBrokerageGateway.cs
│   │   │   ├── BrokerageGatewayAdapter.cs
│   │   │   └── PaperTradingGateway.cs
│   │   ├── Allocation
│   │   │   ├── AllocationResult.cs
│   │   │   ├── AllocationRule.cs
│   │   │   ├── BlockTradeAllocator.cs
│   │   │   ├── IAllocationEngine.cs
│   │   │   └── ProportionalAllocationEngine.cs
│   │   ├── BrokerageServiceRegistration.cs
│   │   ├── Derivatives
│   │   │   ├── FuturePosition.cs
│   │   │   ├── IDerivativePosition.cs
│   │   │   └── OptionPosition.cs
│   │   ├── Events
│   │   │   ├── ITradeEventPublisher.cs
│   │   │   ├── LedgerPostingConsumer.cs
│   │   │   └── TradeExecutedEvent.cs
│   │   ├── Exceptions
│   │   │   └── UnsupportedOrderRequestException.cs
│   │   ├── GlobalUsings.cs
│   │   ├── IRiskValidator.cs
│   │   ├── ISecurityMasterGate.cs
│   │   ├── Interfaces
│   │   │   ├── IAccountPortfolio.cs
│   │   │   ├── IExecutionContext.cs
│   │   │   ├── ILiveFeedAdapter.cs
│   │   │   └── IOrderGateway.cs
│   │   ├── Margin
│   │   │   ├── IMarginModel.cs
│   │   │   ├── MarginAccountType.cs
│   │   │   ├── MarginCallStatus.cs
│   │   │   ├── MarginRequirement.cs
│   │   │   ├── PortfolioMarginModel.cs
│   │   │   └── RegTMarginModel.cs
│   │   ├── Meridian.Execution.csproj
│   │   ├── Models
│   │   │   ├── AccountKind.cs
│   │   │   ├── ExecutionFill.cs
│   │   │   ├── ExecutionMode.cs
│   │   │   ├── ExecutionPosition.cs
│   │   │   ├── IMultiAccountPortfolioState.cs
│   │   │   ├── IPortfolioState.cs
│   │   │   ├── OrderAcknowledgement.cs
│   │   │   ├── OrderGatewayCapabilities.cs
│   │   │   ├── OrderStatus.cs
│   │   │   └── OrderStatusUpdate.cs
│   │   ├── MultiCurrency
│   │   │   ├── FxRate.cs
│   │   │   ├── IFxRateProvider.cs
│   │   │   └── MultiCurrencyCashBalance.cs
│   │   ├── OrderManagementSystem.cs
│   │   ├── PaperExecutionContext.cs
│   │   ├── PaperTradingGateway.cs
│   │   ├── SecurityMasterGate.cs
│   │   ├── Serialization
│   │   │   └── ExecutionJsonContext.cs
│   │   ├── Services
│   │   │   ├── ExecutionAuditTrailService.cs
│   │   │   ├── ExecutionOperatorControlService.cs
│   │   │   ├── IPaperSessionStore.cs
│   │   │   ├── JsonlFilePaperSessionStore.cs
│   │   │   ├── OrderLifecycleManager.cs
│   │   │   ├── PaperSessionOptions.cs
│   │   │   ├── PaperSessionPersistenceService.cs
│   │   │   ├── PaperTradingPortfolio.cs
│   │   │   ├── PortfolioRegistry.cs
│   │   │   ├── PositionLotSelector.cs
│   │   │   ├── PositionReconciliationService.cs
│   │   │   └── PositionSyncOptions.cs
│   │   └── TaxLotAccounting
│   │       ├── ITaxLotSelector.cs
│   │       ├── TaxLotAccountingMethod.cs
│   │       ├── TaxLotRelief.cs
│   │       └── TaxLotSelectors.cs
│   ├── Meridian.Execution.Sdk
│   │   ├── BrokerageConfiguration.cs
│   │   ├── BrokerageValidationEvaluator.cs
│   │   ├── Derivatives
│   │   │   ├── FutureDetails.cs
│   │   │   ├── OptionDetails.cs
│   │   │   └── OptionGreeks.cs
│   │   ├── IBrokerageAccountSync.cs
│   │   ├── IBrokerageGateway.cs
│   │   ├── IBrokeragePositionSync.cs
│   │   ├── IExecutionGateway.cs
│   │   ├── IOrderManager.cs
│   │   ├── IPosition.cs
│   │   ├── IPositionTracker.cs
│   │   ├── Meridian.Execution.Sdk.csproj
│   │   ├── Models.cs
│   │   ├── PositionExtensions.cs
│   │   └── TaxLot.cs
│   ├── Meridian.FSharp
│   │   ├── Calculations
│   │   │   ├── Aggregations.fs
│   │   │   ├── Imbalance.fs
│   │   │   └── Spread.fs
│   │   ├── Canonicalization
│   │   │   └── MappingRules.fs
│   │   ├── Domain
│   │   │   ├── AccountReconciliation.fs
│   │   │   ├── AccountStatements.fs
│   │   │   ├── CashFlowProjection.fs
│   │   │   ├── CashFlowRules.fs
│   │   │   ├── DirectLending.fs
│   │   │   ├── FundStructure.fs
│   │   │   ├── Integrity.fs
│   │   │   ├── MarketEvents.fs
│   │   │   ├── SecMasterDomain.fs
│   │   │   ├── SecurityClassification.fs
│   │   │   ├── SecurityEconomicDefinition.fs
│   │   │   ├── SecurityIdentifiers.fs
│   │   │   ├── SecurityMaster.fs
│   │   │   ├── SecurityMasterCommands.fs
│   │   │   ├── SecurityMasterEvents.fs
│   │   │   ├── SecurityMasterLegacyUpgrade.fs
│   │   │   ├── SecurityTermModules.fs
│   │   │   ├── SettlementInstructionCommands.fs
│   │   │   └── Sides.fs
│   │   ├── Generated
│   │   │   └── Meridian.FSharp.Interop.g.cs
│   │   ├── Interop.AccountDetails.fs
│   │   ├── Interop.CashFlow.fs
│   │   ├── Interop.DirectLending.fs
│   │   ├── Interop.SecurityMaster.fs
│   │   ├── Interop.fs
│   │   ├── Meridian.FSharp.fsproj
│   │   ├── Pipeline
│   │   │   └── Transforms.fs
│   │   ├── Promotion
│   │   │   ├── PromotionPolicy.fs
│   │   │   └── PromotionTypes.fs
│   │   ├── Risk
│   │   │   ├── RiskEvaluation.fs
│   │   │   ├── RiskRules.fs
│   │   │   └── RiskTypes.fs
│   │   └── Validation
│   │       ├── QuoteValidator.fs
│   │       ├── TradeValidator.fs
│   │       ├── ValidationPipeline.fs
│   │       └── ValidationTypes.fs
│   ├── Meridian.FSharp.DirectLending.Aggregates
│   │   ├── AggregateTypes.fs
│   │   ├── ContractAggregate.fs
│   │   ├── Interop.fs
│   │   ├── Meridian.FSharp.DirectLending.Aggregates.fsproj
│   │   └── ServicingAggregate.fs
│   ├── Meridian.FSharp.Ledger
│   │   ├── AccrualTypes.fs
│   │   ├── Interop.fs
│   │   ├── JournalValidation.fs
│   │   ├── LedgerReadModels.fs
│   │   ├── LedgerTypes.fs
│   │   ├── Meridian.FSharp.Ledger.fsproj
│   │   ├── PeriodManagement.fs
│   │   ├── Posting.fs
│   │   ├── Reconciliation.fs
│   │   ├── ReconciliationClassification.fs
│   │   ├── ReconciliationRules.fs
│   │   └── ReconciliationTypes.fs
│   ├── Meridian.FSharp.Trading
│   │   ├── Interop.fs
│   │   ├── Meridian.FSharp.Trading.fsproj
│   │   ├── PromotionReadiness.fs
│   │   ├── StrategyLifecycleState.fs
│   │   ├── StrategyLifecycleTransitions.fs
│   │   └── StrategyRunTypes.fs
│   ├── Meridian.IbApi.SmokeStub
│   │   ├── IBApiSmokeStub.cs
│   │   └── Meridian.IbApi.SmokeStub.csproj
│   ├── Meridian.Infrastructure
│   │   ├── Adapters
│   │   │   ├── Alpaca
│   │   │   │   ├── AlpacaBrokerageGateway.cs
│   │   │   │   ├── AlpacaConstants.cs
│   │   │   │   ├── AlpacaCorporateActionProvider.cs
│   │   │   │   ├── AlpacaHistoricalDataProvider.cs
│   │   │   │   ├── AlpacaMarketDataClient.cs
│   │   │   │   ├── AlpacaOptionsChainProvider.cs
│   │   │   │   ├── AlpacaProviderModule.cs
│   │   │   │   └── AlpacaSymbolSearchProviderRefactored.cs
│   │   │   ├── AlphaVantage
│   │   │   │   └── AlphaVantageHistoricalDataProvider.cs
│   │   │   ├── Core
│   │   │   │   ├── Backfill
│   │   │   │   │   ├── BackfillJob.cs
│   │   │   │   │   ├── BackfillJobManager.cs
│   │   │   │   │   ├── BackfillRequestQueue.cs
│   │   │   │   │   ├── BackfillWorkerService.cs
│   │   │   │   │   ├── MarketDataTracing.cs
│   │   │   │   │   └── PriorityBackfillQueue.cs
│   │   │   │   ├── BackfillProgressTracker.cs
│   │   │   │   ├── BaseHistoricalDataProvider.cs
│   │   │   │   ├── BaseSymbolSearchProvider.cs
│   │   │   │   ├── CompositeHistoricalDataProvider.cs
│   │   │   │   ├── GapAnalysis
│   │   │   │   │   ├── DataGapAnalyzer.cs
│   │   │   │   │   ├── DataGapRepair.cs
│   │   │   │   │   └── DataQualityMonitor.cs
│   │   │   │   ├── ICorporateActionProvider.cs
│   │   │   │   ├── IHistoricalAggregateBarProvider.cs
│   │   │   │   ├── IHistoricalDataProvider.cs
│   │   │   │   ├── ISymbolSearchProvider.cs
│   │   │   │   ├── ProviderBehaviorBuilder.cs
│   │   │   │   ├── ProviderFactory.cs
│   │   │   │   ├── ProviderRegistry.cs
│   │   │   │   ├── ProviderServiceExtensions.cs
│   │   │   │   ├── ProviderSubscriptionRanges.cs
│   │   │   │   ├── ProviderTemplate.cs
│   │   │   │   ├── RateLimiting
│   │   │   │   │   ├── ProviderRateLimitTracker.cs
│   │   │   │   │   └── RateLimiter.cs
│   │   │   │   ├── ResponseHandler.cs
│   │   │   │   ├── SymbolResolution
│   │   │   │   │   └── ISymbolResolver.cs
│   │   │   │   ├── SymbolSearchUtility.cs
│   │   │   │   └── WebSocketProviderBase.cs
│   │   │   ├── Edgar
│   │   │   │   ├── EdgarReferenceDataProvider.cs
│   │   │   │   ├── EdgarSecurityDocumentParser.cs
│   │   │   │   ├── EdgarSecurityMasterIngestProvider.cs
│   │   │   │   ├── EdgarSymbolSearchProvider.cs
│   │   │   │   └── IEdgarReferenceDataProvider.cs
│   │   │   ├── Failover
│   │   │   │   ├── FailoverAwareMarketDataClient.cs
│   │   │   │   ├── StreamingFailoverRegistry.cs
│   │   │   │   └── StreamingFailoverService.cs
│   │   │   ├── Finnhub
│   │   │   │   ├── FinnhubConstants.cs
│   │   │   │   ├── FinnhubHistoricalDataProvider.cs
│   │   │   │   └── FinnhubSymbolSearchProviderRefactored.cs
│   │   │   ├── Fred
│   │   │   │   └── FredHistoricalDataProvider.cs
│   │   │   ├── InteractiveBrokers
│   │   │   │   ├── ContractFactory.cs
│   │   │   │   ├── EnhancedIBConnectionManager.IBApi.cs
│   │   │   │   ├── EnhancedIBConnectionManager.IBApiVendorStubs.cs
│   │   │   │   ├── EnhancedIBConnectionManager.cs
│   │   │   │   ├── IBApiLimits.cs
│   │   │   │   ├── IBApiVersionValidator.cs
│   │   │   │   ├── IBBrokerageGateway.cs
│   │   │   │   ├── IBBrokerageInterop.cs
│   │   │   │   ├── IBBuildGuidance.cs
│   │   │   │   ├── IBCallbackRouter.cs
│   │   │   │   ├── IBConnectionManager.cs
│   │   │   │   ├── IBHistoricalDataProvider.cs
│   │   │   │   ├── IBMarketDataClient.cs
│   │   │   │   └── IBSimulationClient.cs
│   │   │   ├── NYSE
│   │   │   │   ├── NYSEDataSource.cs
│   │   │   │   ├── NYSEOptions.cs
│   │   │   │   ├── NYSEServiceExtensions.cs
│   │   │   │   ├── NyseMarketDataClient.cs
│   │   │   │   └── NyseNationalTradesCsvParser.cs
│   │   │   ├── NasdaqDataLink
│   │   │   │   └── NasdaqDataLinkHistoricalDataProvider.cs
│   │   │   ├── OpenFigi
│   │   │   │   ├── OpenFigiClient.cs
│   │   │   │   └── OpenFigiSymbolResolver.cs
│   │   │   ├── Polygon
│   │   │   │   ├── ITradingParametersBackfillService.cs
│   │   │   │   ├── PolygonConstants.cs
│   │   │   │   ├── PolygonCorporateActionFetcher.cs
│   │   │   │   ├── PolygonHistoricalDataProvider.cs
│   │   │   │   ├── PolygonMarketDataClient.cs
│   │   │   │   ├── PolygonOptionsChainProvider.cs
│   │   │   │   ├── PolygonSecurityMasterIngestProvider.cs
│   │   │   │   ├── PolygonSymbolSearchProvider.cs
│   │   │   │   └── TradingParametersBackfillService.cs
│   │   │   ├── Robinhood
│   │   │   │   ├── RobinhoodBrokerageGateway.cs
│   │   │   │   ├── RobinhoodHistoricalDataProvider.cs
│   │   │   │   ├── RobinhoodMarketDataClient.cs
│   │   │   │   ├── RobinhoodOptionsChainProvider.cs
│   │   │   │   ├── RobinhoodReadOnlyBrokerageSyncAdapter.cs
│   │   │   │   ├── RobinhoodSymbolSearchModels.cs
│   │   │   │   └── RobinhoodSymbolSearchProvider.cs
│   │   │   ├── Stooq
│   │   │   │   └── StooqHistoricalDataProvider.cs
│   │   │   ├── Synthetic
│   │   │   │   ├── SyntheticHistoricalDataProvider.cs
│   │   │   │   ├── SyntheticMarketDataClient.cs
│   │   │   │   ├── SyntheticOptionsChainProvider.cs
│   │   │   │   └── SyntheticReferenceDataCatalog.cs
│   │   │   ├── Templates
│   │   │   │   └── TemplateBrokerageGateway.cs
│   │   │   ├── Tiingo
│   │   │   │   └── TiingoHistoricalDataProvider.cs
│   │   │   ├── TwelveData
│   │   │   │   └── TwelveDataHistoricalDataProvider.cs
│   │   │   └── YahooFinance
│   │   │       └── YahooFinanceHistoricalDataProvider.cs
│   │   ├── Contracts
│   │   │   ├── ContractVerificationExtensions.cs
│   │   │   └── ContractVerificationService.cs
│   │   ├── DataSources
│   │   │   ├── DataSourceBase.cs
│   │   │   └── DataSourceConfiguration.cs
│   │   ├── Etl
│   │   │   ├── CsvPartnerFileParser.cs
│   │   │   ├── ISftpFilePublisher.cs
│   │   │   ├── LocalFileSourceReader.cs
│   │   │   ├── Sftp
│   │   │   │   └── ISftpClientFactory.cs
│   │   │   ├── SftpFilePublisher.cs
│   │   │   └── SftpFileSourceReader.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Http
│   │   │   ├── HttpClientConfiguration.cs
│   │   │   └── SharedResiliencePolicies.cs
│   │   ├── Meridian.Infrastructure.csproj
│   │   ├── NoOpMarketDataClient.cs
│   │   ├── Reconciliation
│   │   │   ├── BrokerStatementInfrastructure.cs
│   │   │   ├── BrokerStatementNormalizer.cs
│   │   │   └── ReconciliationCaseInfrastructure.cs
│   │   ├── Resilience
│   │   │   ├── HttpResiliencePolicy.cs
│   │   │   ├── WebSocketConnectionConfig.cs
│   │   │   ├── WebSocketConnectionManager.cs
│   │   │   └── WebSocketResiliencePolicy.cs
│   │   ├── Shared
│   │   │   ├── ISymbolStateStore.cs
│   │   │   ├── SubscriptionManager.cs
│   │   │   ├── TaskSafetyExtensions.cs
│   │   │   └── WebSocketReconnectionHelper.cs
│   │   └── Utilities
│   │       ├── HttpResponseHandler.cs
│   │       ├── JsonElementExtensions.cs
│   │       └── SymbolNormalization.cs
│   ├── Meridian.Infrastructure.CppTrader
│   │   ├── CppTraderServiceCollectionExtensions.cs
│   │   ├── Diagnostics
│   │   │   ├── CppTraderSessionDiagnostic.cs
│   │   │   ├── CppTraderSessionDiagnosticsService.cs
│   │   │   ├── CppTraderStatusService.cs
│   │   │   ├── ICppTraderSessionDiagnosticsService.cs
│   │   │   └── ICppTraderStatusService.cs
│   │   ├── Execution
│   │   │   ├── CppTraderLiveFeedAdapter.cs
│   │   │   └── CppTraderOrderGateway.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Host
│   │   │   ├── CppTraderHostManager.cs
│   │   │   ├── ICppTraderHostManager.cs
│   │   │   ├── ICppTraderSessionClient.cs
│   │   │   └── ProcessBackedCppTraderSessionClient.cs
│   │   ├── Meridian.Infrastructure.CppTrader.csproj
│   │   ├── Options
│   │   │   └── CppTraderOptions.cs
│   │   ├── Protocol
│   │   │   ├── CppTraderProtocolModels.cs
│   │   │   └── LengthPrefixedProtocolStream.cs
│   │   ├── Providers
│   │   │   ├── CppTraderItchIngestionService.cs
│   │   │   ├── CppTraderMarketDataClient.cs
│   │   │   └── ICppTraderItchIngestionService.cs
│   │   ├── Replay
│   │   │   ├── CppTraderReplayService.cs
│   │   │   └── ICppTraderReplayService.cs
│   │   ├── Symbols
│   │   │   ├── CppTraderSymbolMapper.cs
│   │   │   └── ICppTraderSymbolMapper.cs
│   │   └── Translation
│   │       ├── CppTraderExecutionTranslator.cs
│   │       ├── CppTraderSnapshotTranslator.cs
│   │       ├── ICppTraderExecutionTranslator.cs
│   │       └── ICppTraderSnapshotTranslator.cs
│   ├── Meridian.Ledger
│   │   ├── FundLedgerBook.cs
│   │   ├── GlobalUsings.cs
│   │   ├── IReadOnlyLedger.cs
│   │   ├── JournalEntry.cs
│   │   ├── JournalEntryMetadata.cs
│   │   ├── Ledger.cs
│   │   ├── LedgerAccount.cs
│   │   ├── LedgerAccountSummary.cs
│   │   ├── LedgerAccountType.cs
│   │   ├── LedgerAccounts.cs
│   │   ├── LedgerBalancePoint.cs
│   │   ├── LedgerBookKey.cs
│   │   ├── LedgerEntry.cs
│   │   ├── LedgerQuery.cs
│   │   ├── LedgerSnapshot.cs
│   │   ├── LedgerValidationException.cs
│   │   ├── LedgerViewKind.cs
│   │   ├── Meridian.Ledger.csproj
│   │   ├── ProjectLedgerBook.cs
│   │   └── ReadOnlyCollectionHelpers.cs
│   ├── Meridian.Mcp
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.Mcp.csproj
│   │   ├── Program.cs
│   │   ├── Prompts
│   │   │   ├── CodeReviewPrompts.cs
│   │   │   ├── ProviderPrompts.cs
│   │   │   └── TestWriterPrompts.cs
│   │   ├── Resources
│   │   │   ├── AdrResources.cs
│   │   │   ├── ConventionResources.cs
│   │   │   └── TemplateResources.cs
│   │   ├── Services
│   │   │   └── RepoPathService.cs
│   │   └── Tools
│   │       ├── AdrTools.cs
│   │       ├── AuditTools.cs
│   │       ├── ConventionTools.cs
│   │       ├── KnownErrorTools.cs
│   │       └── ProviderTools.cs
│   ├── Meridian.McpServer
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.McpServer.csproj
│   │   ├── Navigation
│   │   │   └── RepoNavigationCatalog.cs
│   │   ├── Program.cs
│   │   ├── Prompts
│   │   │   └── MarketDataPrompts.cs
│   │   ├── Resources
│   │   │   ├── MarketDataResources.cs
│   │   │   └── RepoNavigationResources.cs
│   │   └── Tools
│   │       ├── BackfillTools.cs
│   │       ├── ProviderTools.cs
│   │       ├── RepoNavigationTools.cs
│   │       ├── StorageTools.cs
│   │       └── SymbolTools.cs
│   ├── Meridian.ProviderSdk
│   │   ├── AttributeCredentialResolver.cs
│   │   ├── CredentialSchemaRegistry.cs
│   │   ├── CredentialValidator.cs
│   │   ├── DataSourceAttribute.cs
│   │   ├── DataSourceRegistry.cs
│   │   ├── HistoricalDataCapabilities.cs
│   │   ├── ICredentialContext.cs
│   │   ├── IDataSource.cs
│   │   ├── IHistoricalBarWriter.cs
│   │   ├── IHistoricalDataSource.cs
│   │   ├── IMarketDataClient.cs
│   │   ├── IOptionsChainProvider.cs
│   │   ├── IProviderFamilyAdapter.cs
│   │   ├── IProviderMetadata.cs
│   │   ├── IProviderModule.cs
│   │   ├── IRealtimeDataSource.cs
│   │   ├── ImplementsAdrAttribute.cs
│   │   ├── Meridian.ProviderSdk.csproj
│   │   ├── ProviderHttpUtilities.cs
│   │   ├── ProviderModuleLoader.cs
│   │   ├── ProviderRoutingModels.cs
│   │   └── RequiresCredentialAttribute.cs
│   ├── Meridian.QuantScript
│   │   ├── Api
│   │   │   ├── BacktestProxy.cs
│   │   │   ├── DataProxy.cs
│   │   │   ├── EfficientFrontierConstraints.cs
│   │   │   ├── IQuantDataContext.cs
│   │   │   ├── LambdaBacktestStrategy.cs
│   │   │   ├── PortfolioBuilder.cs
│   │   │   ├── PriceBar.cs
│   │   │   ├── PriceSeries.cs
│   │   │   ├── PriceSeriesExtensions.cs
│   │   │   ├── QuantDataContext.cs
│   │   │   ├── ReturnSeries.cs
│   │   │   ├── ScriptModels.cs
│   │   │   ├── ScriptParamAttribute.cs
│   │   │   ├── StatisticsEngine.cs
│   │   │   └── TechnicalSeriesExtensions.cs
│   │   ├── Compilation
│   │   │   ├── IQuantScriptCompiler.cs
│   │   │   ├── IScriptRunner.cs
│   │   │   ├── NotebookExecutionSession.cs
│   │   │   ├── QuantScriptGlobals.cs
│   │   │   ├── RoslynScriptCompiler.cs
│   │   │   ├── ScriptExecutionCheckpoint.cs
│   │   │   ├── ScriptRunResult.cs
│   │   │   └── ScriptRunner.cs
│   │   ├── Documents
│   │   │   ├── IQuantScriptNotebookStore.cs
│   │   │   ├── QuantScriptDocumentModels.cs
│   │   │   └── QuantScriptNotebookStore.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.QuantScript.csproj
│   │   ├── Plotting
│   │   │   ├── PlotQueue.cs
│   │   │   ├── PlotRequest.cs
│   │   │   └── PlotType.cs
│   │   ├── QuantScriptOptions.cs
│   │   ├── QuantScriptServiceCollectionExtensions.cs
│   │   └── ScriptContext.cs
│   ├── Meridian.Risk
│   │   ├── CompositeRiskValidator.cs
│   │   ├── IRiskRule.cs
│   │   ├── Meridian.Risk.csproj
│   │   └── Rules
│   │       ├── DrawdownCircuitBreaker.cs
│   │       ├── OrderRateThrottle.cs
│   │       └── PositionLimitRule.cs
│   ├── Meridian.Storage
│   │   ├── Archival
│   │   │   ├── ArchivalStorageService.cs
│   │   │   ├── AtomicFileWriter.cs
│   │   │   ├── CompressionProfileManager.cs
│   │   │   ├── SchemaVersionManager.cs
│   │   │   └── WriteAheadLog.cs
│   │   ├── DirectLending
│   │   │   ├── DirectLendingMigrationRunner.cs
│   │   │   ├── DirectLendingPersistenceBatch.cs
│   │   │   ├── IDirectLendingOperationsStore.cs
│   │   │   ├── IDirectLendingStateStore.cs
│   │   │   ├── Migrations
│   │   │   │   ├── 001_direct_lending.sql
│   │   │   │   ├── 002_direct_lending_projections.sql
│   │   │   │   ├── 003_direct_lending_accrual_and_event_metadata.sql
│   │   │   │   ├── 004_direct_lending_event_schema_and_snapshots.sql
│   │   │   │   ├── 005_direct_lending_operations.sql
│   │   │   │   └── 005_direct_lending_workflows.sql
│   │   │   ├── PostgresDirectLendingStateStore.Operations.cs
│   │   │   └── PostgresDirectLendingStateStore.cs
│   │   ├── Etl
│   │   │   └── EtlStores.cs
│   │   ├── Export
│   │   │   ├── AnalysisExportService.Features.cs
│   │   │   ├── AnalysisExportService.Formats.Arrow.cs
│   │   │   ├── AnalysisExportService.Formats.Parquet.cs
│   │   │   ├── AnalysisExportService.Formats.Xlsx.cs
│   │   │   ├── AnalysisExportService.Formats.cs
│   │   │   ├── AnalysisExportService.IO.cs
│   │   │   ├── AnalysisExportService.cs
│   │   │   ├── AnalysisQualityReport.cs
│   │   │   ├── ExportPreflightRules.cs
│   │   │   ├── ExportProfile.cs
│   │   │   ├── ExportRequest.cs
│   │   │   ├── ExportResult.cs
│   │   │   ├── ExportValidator.cs
│   │   │   ├── ExportVerificationReport.cs
│   │   │   ├── PreflightRule.cs
│   │   │   └── XlsxWorkbookWriter.cs
│   │   ├── FundAccounts
│   │   │   ├── IFundAccountStore.cs
│   │   │   └── Migrations
│   │   │       └── 001_fund_accounts.sql
│   │   ├── GlobalUsings.cs
│   │   ├── Interfaces
│   │   │   ├── IMarketDataStore.cs
│   │   │   ├── ISourceRegistry.cs
│   │   │   ├── IStorageCatalogService.cs
│   │   │   ├── IStoragePolicy.cs
│   │   │   ├── IStorageSink.cs
│   │   │   └── ISymbolRegistryService.cs
│   │   ├── Ledger
│   │   │   ├── ILedgerJournalStore.cs
│   │   │   ├── LedgerBookServiceException.cs
│   │   │   ├── LedgerJournalStoreOptions.cs
│   │   │   ├── LedgerStoreExtensions.cs
│   │   │   ├── Migrations
│   │   │   │   ├── V_ledger_001__journal_entries.sql
│   │   │   │   ├── V_ledger_002__accounting_periods.sql
│   │   │   │   ├── V_ledger_003__ledger_books.sql
│   │   │   │   ├── V_ledger_004__accounting_basis_policies.sql
│   │   │   │   └── V_ledger_005__journal_basis_lineage.sql
│   │   │   ├── PostgresLedgerBookService.cs
│   │   │   └── PostgresLedgerJournalStore.cs
│   │   ├── Maintenance
│   │   │   ├── ArchiveMaintenanceModels.cs
│   │   │   ├── ArchiveMaintenanceScheduleManager.cs
│   │   │   ├── IArchiveMaintenanceScheduleManager.cs
│   │   │   ├── IArchiveMaintenanceService.cs
│   │   │   ├── IMaintenanceExecutionHistory.cs
│   │   │   └── ScheduledArchiveMaintenanceService.cs
│   │   ├── Meridian.Storage.csproj
│   │   ├── Packaging
│   │   │   ├── PackageManifest.cs
│   │   │   ├── PackageOptions.cs
│   │   │   ├── PackageResult.cs
│   │   │   ├── PortableDataPackager.Creation.cs
│   │   │   ├── PortableDataPackager.Scripts.Import.cs
│   │   │   ├── PortableDataPackager.Scripts.Sql.cs
│   │   │   ├── PortableDataPackager.Scripts.cs
│   │   │   ├── PortableDataPackager.Validation.cs
│   │   │   └── PortableDataPackager.cs
│   │   ├── Policies
│   │   │   └── JsonlStoragePolicy.cs
│   │   ├── Replay
│   │   │   ├── JsonlReplayer.cs
│   │   │   └── MemoryMappedJsonlReader.cs
│   │   ├── SecurityMaster
│   │   │   ├── FileEdgarReferenceDataStore.cs
│   │   │   ├── IBondReferenceProjectionStore.cs
│   │   │   ├── ICertificateOfDepositReferenceProjectionStore.cs
│   │   │   ├── ICommodityReferenceProjectionStore.cs
│   │   │   ├── ICryptoReferenceProjectionStore.cs
│   │   │   ├── IDepositReferenceProjectionStore.cs
│   │   │   ├── IEdgarReferenceDataStore.cs
│   │   │   ├── IEquityReferenceProjectionStore.cs
│   │   │   ├── IFutureReferenceProjectionStore.cs
│   │   │   ├── IFxSpotReferenceProjectionStore.cs
│   │   │   ├── IMoneyMarketFundReferenceProjectionStore.cs
│   │   │   ├── IOperatorOverridesStore.cs
│   │   │   ├── IOptionReferenceProjectionStore.cs
│   │   │   ├── ISecurityMasterEventStore.cs
│   │   │   ├── ISecurityMasterSnapshotStore.cs
│   │   │   ├── ISecurityMasterStore.cs
│   │   │   ├── ISwapReferenceProjectionStore.cs
│   │   │   ├── Migrations
│   │   │   │   ├── 001_security_master.sql
│   │   │   │   ├── 002_security_master_fts.sql
│   │   │   │   ├── 003_security_master_corp_actions.sql
│   │   │   │   ├── 004_security_master_operator_overrides.sql
│   │   │   │   ├── 005_security_master_bond_reference_projection.sql
│   │   │   │   ├── 006_security_master_option_reference_projection.sql
│   │   │   │   ├── 007_security_master_equity_projection.sql
│   │   │   │   ├── 008_security_master_future_projection.sql
│   │   │   │   ├── 009_security_master_fxspot_projection.sql
│   │   │   │   ├── 010_security_master_swap_projection.sql
│   │   │   │   ├── 011_security_master_commodity_projection.sql
│   │   │   │   ├── 012_security_master_crypto_projection.sql
│   │   │   │   ├── 013_security_master_deposit_projection.sql
│   │   │   │   ├── 014_security_master_money_market_fund_projection.sql
│   │   │   │   └── 015_security_master_certificate_of_deposit_projection.sql
│   │   │   ├── PostgresBondReferenceProjectionStore.cs
│   │   │   ├── PostgresCertificateOfDepositReferenceProjectionStore.cs
│   │   │   ├── PostgresCommodityReferenceProjectionStore.cs
│   │   │   ├── PostgresCryptoReferenceProjectionStore.cs
│   │   │   ├── PostgresDepositReferenceProjectionStore.cs
│   │   │   ├── PostgresEquityReferenceProjectionStore.cs
│   │   │   ├── PostgresFutureReferenceProjectionStore.cs
│   │   │   ├── PostgresFxSpotReferenceProjectionStore.cs
│   │   │   ├── PostgresMoneyMarketFundReferenceProjectionStore.cs
│   │   │   ├── PostgresOperatorOverridesStore.cs
│   │   │   ├── PostgresOptionReferenceProjectionStore.cs
│   │   │   ├── PostgresSecurityMasterEventStore.cs
│   │   │   ├── PostgresSecurityMasterSnapshotStore.cs
│   │   │   ├── PostgresSecurityMasterStore.cs
│   │   │   ├── PostgresSwapReferenceProjectionStore.cs
│   │   │   ├── SecurityMasterDbMapper.cs
│   │   │   ├── SecurityMasterMigrationRunner.cs
│   │   │   └── SecurityMasterProjectionCache.cs
│   │   ├── Services
│   │   │   ├── AuditChainService.cs
│   │   │   ├── DataLineageService.cs
│   │   │   ├── DataQualityScoringService.cs
│   │   │   ├── DataQualityService.cs
│   │   │   ├── EventBuffer.cs
│   │   │   ├── FileMaintenanceService.cs
│   │   │   ├── FilePermissionsService.cs
│   │   │   ├── JsonlPositionSnapshotStore.cs
│   │   │   ├── LifecyclePolicyEngine.cs
│   │   │   ├── MaintenanceScheduler.cs
│   │   │   ├── MetadataTagService.cs
│   │   │   ├── ParquetConversionService.cs
│   │   │   ├── QualityTrendStore.cs
│   │   │   ├── QuotaEnforcementService.cs
│   │   │   ├── RetentionComplianceReporter.cs
│   │   │   ├── SourceRegistry.cs
│   │   │   ├── StorageCatalogService.cs
│   │   │   ├── StorageChecksumService.cs
│   │   │   ├── StorageSearchService.cs
│   │   │   ├── SymbolRegistryService.cs
│   │   │   └── TierMigrationService.cs
│   │   ├── Sinks
│   │   │   ├── CatalogSyncSink.cs
│   │   │   ├── CompositeSink.cs
│   │   │   ├── JsonlStorageSink.cs
│   │   │   └── ParquetStorageSink.cs
│   │   ├── StorageOptions.cs
│   │   ├── StorageProfiles.cs
│   │   ├── StorageSinkAttribute.cs
│   │   ├── StorageSinkRegistry.cs
│   │   └── Store
│   │       ├── CompositeMarketDataStore.cs
│   │       └── JsonlMarketDataStore.cs
│   ├── Meridian.Strategies
│   │   ├── GlobalUsings.cs
│   │   ├── Interfaces
│   │   │   ├── ILiveStrategy.cs
│   │   │   ├── IPromotionRecordStore.cs
│   │   │   ├── IStrategyDesignRepository.cs
│   │   │   ├── IStrategyLifecycle.cs
│   │   │   └── IStrategyRepository.cs
│   │   ├── Meridian.Strategies.csproj
│   │   ├── Models
│   │   │   ├── RunType.cs
│   │   │   ├── StrategyRunEntry.cs
│   │   │   ├── StrategyRunRepositoryQuery.cs
│   │   │   └── StrategyStatus.cs
│   │   ├── Promotions
│   │   │   ├── BacktestToLivePromoter.cs
│   │   │   ├── PromotionApprovalChecklist.cs
│   │   │   └── PromotionRecordService.cs
│   │   ├── Serialization
│   │   │   ├── FSharpInteropJsonContext.cs
│   │   │   ├── PromotionRecordJsonContext.cs
│   │   │   └── StrategyDesignJsonContext.cs
│   │   ├── Services
│   │   │   ├── AggregatePortfolioService.cs
│   │   │   ├── CashFlowProjectionService.cs
│   │   │   ├── FileReconciliationBreakQueueRepository.cs
│   │   │   ├── IAggregatePortfolioService.cs
│   │   │   ├── IReconciliationBreakQueueRepository.cs
│   │   │   ├── IReconciliationRunRepository.cs
│   │   │   ├── IReconciliationRunService.cs
│   │   │   ├── ISecurityReferenceLookup.cs
│   │   │   ├── InMemoryReconciliationRunRepository.cs
│   │   │   ├── LedgerReadService.cs
│   │   │   ├── PortfolioReadService.cs
│   │   │   ├── PromotionService.cs
│   │   │   ├── ReconciliationGovernanceService.cs
│   │   │   ├── ReconciliationProjectionService.cs
│   │   │   ├── ReconciliationRunService.cs
│   │   │   ├── ReconciliationSourceAdapters.cs
│   │   │   ├── StrategyDesignService.cs
│   │   │   ├── StrategyLifecycleManager.cs
│   │   │   ├── StrategyRunContinuityService.cs
│   │   │   ├── StrategyRunReadService.cs
│   │   │   └── StrategyRunScopeMetadataResolver.cs
│   │   └── Storage
│   │       ├── JsonlPromotionRecordStore.cs
│   │       ├── JsonlStrategyDesignRepository.cs
│   │       └── StrategyRunStore.cs
│   ├── Meridian.Ui
│   │   └── dashboard
│   │       ├── artifacts
│   │       │   ├── automation
│   │       │   │   ├── covered-call-history-1440.png
│   │       │   │   ├── covered-call-history-390.png
│   │       │   │   ├── covered-call-trade-timeline-smoke.png
│   │       │   │   ├── global-scope-smoke
│   │       │   │   │   ├── global-scope-portfolio.png
│   │       │   │   │   └── preview.pid
│   │       │   │   ├── governance-reconciliation-disabled-reasons-mobile-smoke.png
│   │       │   │   ├── governance-reconciliation-disabled-reasons-smoke.png
│   │       │   │   ├── price-alerts-dev-tables-smoke.png
│   │       │   │   ├── price-alerts-final-smoke.png
│   │       │   │   ├── price-alerts-fixture-tables-smoke.png
│   │       │   │   ├── price-alerts-helper-desktop-smoke.png
│   │       │   │   ├── price-alerts-helper-mobile-smoke.png
│   │       │   │   ├── price-alerts-screen.json
│   │       │   │   ├── price-alerts-seed.json
│   │       │   │   ├── price-alerts-smoke.png
│   │       │   │   ├── progress
│   │       │   │   │   ├── command-palette-alpaca-setup-smoke.png
│   │       │   │   │   ├── command-palette-hash-aware-smoke.png
│   │       │   │   │   ├── covered-call-cancel-confirm-smoke.png
│   │       │   │   │   ├── covered-call-chain-dense-table-smoke.png
│   │       │   │   │   ├── covered-call-command-feedback-smoke.png
│   │       │   │   │   ├── covered-call-history-dense-table-smoke.png
│   │       │   │   │   ├── covered-call-history-loading-smoke.png
│   │       │   │   │   ├── covered-call-history-open-guard-smoke.png
│   │       │   │   │   ├── covered-call-results-handoff-smoke.png
│   │       │   │   │   ├── covered-call-stage-navigation-smoke.png
│   │       │   │   │   ├── covered-call-trade-timeline-smoke.png
│   │       │   │   │   ├── evidence-manifest-action-smoke.png
│   │       │   │   │   ├── linked-context-portfolio-msft.png
│   │       │   │   │   ├── live-quotes-order-readiness-handoff-smoke.png
│   │       │   │   │   ├── operator-focus-dock-smoke-2026-05-15.png
│   │       │   │   │   ├── operator-focus-dock-smoke.png
│   │       │   │   │   ├── operator-focus-full-queue-smoke.png
│   │       │   │   │   ├── operator-focus-route-state.png
│   │       │   │   │   ├── operator-focus-settings-deeplink-2026-05-15.png
│   │       │   │   │   ├── operator-focus-visible-detail-smoke.png
│   │       │   │   │   ├── operator-readiness-utc-smoke.png
│   │       │   │   │   ├── overview-portfolio-empty-actions-smoke.png
│   │       │   │   │   ├── overview-utc-smoke.png
│   │       │   │   │   ├── portfolio-brokerage-account-detail-smoke.png
│   │       │   │   │   ├── portfolio-brokerage-sync-actions-smoke.png
│   │       │   │   │   ├── portfolio-readiness-debug.png
│   │       │   │   │   ├── portfolio-readiness-handoff-smoke.png
│   │       │   │   │   ├── portfolio-sync-trust-smoke.png
│   │       │   │   │   ├── price-alert-delete-confirmation-smoke.png
│   │       │   │   │   ├── price-alert-storage-warning-smoke.png
│   │       │   │   │   ├── price-alerts-handoff-smoke.png
│   │       │   │   │   ├── price-alerts-utc-smoke.png
│   │       │   │   │   ├── readiness-inbox-refresh-smoke.png
│   │       │   │   │   ├── readiness-provider-setup-handoff-smoke.png
│   │       │   │   │   ├── readiness-report-pack-deeplink-smoke.png
│   │       │   │   │   ├── report-pack-actions-smoke.png
│   │       │   │   │   ├── report-pack-reference-smoke.png
│   │       │   │   │   ├── reporting-dense-table-smoke.png
│   │       │   │   │   ├── research-covered-call-smoke.png
│   │       │   │   │   ├── research-promotion-cancel-smoke.png
│   │       │   │   │   ├── security-lots-desktop-smoke.png
│   │       │   │   │   ├── security-lots-mobile-smoke.png
│   │       │   │   │   ├── security-master-conflicts-retry-smoke.png
│   │       │   │   │   ├── security-master-route-smoke.png
│   │       │   │   │   ├── settings-alpaca-abort-smoke.png
│   │       │   │   │   ├── settings-clear-confirm-smoke.png
│   │       │   │   │   ├── settings-live-ack-smoke.png
│   │       │   │   │   ├── settings-utc-smoke.png
│   │       │   │   │   ├── strategy-designer-reorder-smoke.png
│   │       │   │   │   ├── strategy-plottool-statistics-desktop.png
│   │       │   │   │   ├── strategy-plottool-statistics-mobile.png
│   │       │   │   │   ├── strategy-promotion-ack-smoke.png
│   │       │   │   │   ├── strategy-promotion-disabled-reason-smoke.png
│   │       │   │   │   ├── strategy-promotion-smoke.png
│   │       │   │   │   ├── trading-fills-detail-mobile-smoke.png
│   │       │   │   │   ├── trading-fills-detail-smoke-current.png
│   │       │   │   │   ├── trading-fills-detail-smoke.png
│   │       │   │   │   ├── trading-order-ticket-ack-smoke.png
│   │       │   │   │   ├── trading-order-ticket-duplicate-guard-smoke.png
│   │       │   │   │   ├── trading-paper-session-form-smoke.png
│   │       │   │   │   ├── watchlist-empty-value-mobile-smoke.png
│   │       │   │   │   ├── watchlist-empty-value-smoke.png
│   │       │   │   │   ├── watchlist-retry-smoke.png
│   │       │   │   │   └── workstation-preview-smoke.png
│   │       │   │   ├── strategy-designer-field-labels-smoke.png
│   │       │   │   ├── strategy-designer-field-search-desktop.png
│   │       │   │   ├── strategy-designer-field-search-mobile.png
│   │       │   │   ├── user-test-and-implement-smoke
│   │       │   │   │   ├── accounting.png
│   │       │   │   │   ├── changed-routes-smoke.json
│   │       │   │   │   ├── portfolio-readiness-preview.png
│   │       │   │   │   ├── portfolio.png
│   │       │   │   │   ├── reporting-export-gate-preview.png
│   │       │   │   │   ├── smoke.json
│   │       │   │   │   ├── strategy.png
│   │       │   │   │   └── trading.png
│   │       │   │   ├── web-ui
│   │       │   │   │   ├── calibration-profile-detail-mobile-smoke.png
│   │       │   │   │   ├── calibration-profile-detail-smoke.png
│   │       │   │   │   ├── data-backfill-detail-desktop.png
│   │       │   │   │   ├── data-backfill-detail-mobile.png
│   │       │   │   │   ├── data-export-detail-desktop.png
│   │       │   │   │   ├── data-export-detail-mobile.png
│   │       │   │   │   ├── data-provider-cancel-smoke-mobile.png
│   │       │   │   │   ├── data-provider-cancel-smoke.png
│   │       │   │   │   ├── live-quotes-depth-debug.png
│   │       │   │   │   ├── live-quotes-depth-detail-desktop.png
│   │       │   │   │   ├── live-quotes-depth-detail-mobile.png
│   │       │   │   │   ├── live-quotes-seeded-desktop.png
│   │       │   │   │   ├── live-quotes-seeded-mobile.png
│   │       │   │   │   ├── live-quotes-session-desktop.png
│   │       │   │   │   ├── live-quotes-session-mobile.png
│   │       │   │   │   ├── operator-readiness-evidence-desktop.png
│   │       │   │   │   ├── operator-readiness-evidence-mobile.png
│   │       │   │   │   ├── overview-activity-desktop.png
│   │       │   │   │   ├── overview-activity-mobile.png
│   │       │   │   │   ├── portfolio-account-table-desktop.png
│   │       │   │   │   ├── portfolio-account-table-mobile.png
│   │       │   │   │   ├── provider-setup-continuity-desktop.png
│   │       │   │   │   ├── provider-setup-continuity-mobile.png
│   │       │   │   │   ├── quant-lab-trade-ledger-desktop.png
│   │       │   │   │   ├── quant-lab-trade-ledger-mobile.png
│   │       │   │   │   ├── quant-notebook-desktop.png
│   │       │   │   │   ├── quant-notebook-mobile.png
│   │       │   │   │   ├── reporting-report-pack-ids-desktop.png
│   │       │   │   │   ├── reporting-report-pack-ids-mobile.png
│   │       │   │   │   ├── research-tab-focus-desktop.png
│   │       │   │   │   ├── research-tab-focus-mobile.png
│   │       │   │   │   ├── security-master-conflict-actions-desktop.png
│   │       │   │   │   ├── security-master-conflict-actions-mobile.png
│   │       │   │   │   ├── strategy-designer-selected-leg-desktop.png
│   │       │   │   │   ├── strategy-designer-selected-leg-mobile.png
│   │       │   │   │   ├── watchlist-add-field-clean-desktop.png
│   │       │   │   │   ├── watchlist-add-field-clean-mobile.png
│   │       │   │   │   ├── watchlist-add-field-desktop.png
│   │       │   │   │   ├── watchlist-add-field-mobile.png
│   │       │   │   │   ├── watchlist-clean-smoke-runner.mjs
│   │       │   │   │   ├── workspace-header-desktop.png
│   │       │   │   │   └── workspace-header-mobile.png
│   │       │   │   ├── web-ui-data-operations-preview.pid
│   │       │   │   ├── web-ui-overview-today-vite-4223.pid
│   │       │   │   ├── web-ui-overview-today-vite.pid
│   │       │   │   ├── web-ui-quant-notebook-smoke.png
│   │       │   │   ├── web-ui-report-pack-profile-keyboard-mobile-smoke.png
│   │       │   │   ├── web-ui-report-pack-profile-keyboard-smoke.png
│   │       │   │   ├── web-ui-reporting-export-smoke.png
│   │       │   │   ├── web-ui-reporting-mobile-smoke.png
│   │       │   │   ├── web-ui-research-cash-disabled-mobile-smoke.png
│   │       │   │   ├── web-ui-research-cash-disabled-smoke.png
│   │       │   │   ├── web-ui-research-plottool-desktop.png
│   │       │   │   ├── web-ui-research-plottool-mobile.png
│   │       │   │   ├── web-ui-strategy-designer-spot-price.png
│   │       │   │   ├── web-ui-trading-command-state-desktop.png
│   │       │   │   ├── web-ui-trading-command-state-mobile.png
│   │       │   │   ├── web-ui-trading-paper-session-smoke.png
│   │       │   │   ├── web-ui-trading-strategy-lifecycle-smoke.png
│   │       │   │   ├── web-ui-watchlist-sort-smoke.png
│   │       │   │   └── web-ux
│   │       │   │       ├── investment-operations-path-msedge.png
│   │       │   │       └── workflow-continuity-msft.png
│   │       │   └── workflow-continuity-msft.png
│   │       ├── index.html
│   │       ├── package-lock.json
│   │       ├── package.json
│   │       ├── postcss.config.cjs
│   │       ├── src
│   │       │   ├── app-shell.view-model.test.ts
│   │       │   ├── app-shell.view-model.ts
│   │       │   ├── app.test.tsx
│   │       │   ├── app.tsx
│   │       │   ├── assets
│   │       │   │   └── brand
│   │       │   │       └── meridian-mark.svg
│   │       │   ├── components
│   │       │   │   ├── data
│   │       │   │   │   ├── backfill-validation-dashboard.tsx
│   │       │   │   │   └── symbol-universe-manager.tsx
│   │       │   │   ├── meridian
│   │       │   │   │   ├── command-palette.test.tsx
│   │       │   │   │   ├── command-palette.tsx
│   │       │   │   │   ├── command-palette.view-model.test.ts
│   │       │   │   │   ├── command-palette.view-model.ts
│   │       │   │   │   ├── historical-chart.test.tsx
│   │       │   │   │   ├── historical-chart.tsx
│   │       │   │   │   ├── historical-chart.view-model.test.ts
│   │       │   │   │   ├── historical-chart.view-model.ts
│   │       │   │   │   ├── mega-menu.test.tsx
│   │       │   │   │   ├── mega-menu.tsx
│   │       │   │   │   ├── mega-menu.view-model.test.ts
│   │       │   │   │   ├── mega-menu.view-model.ts
│   │       │   │   │   ├── metric-card.test.tsx
│   │       │   │   │   ├── metric-card.tsx
│   │       │   │   │   ├── metric-card.view-model.test.ts
│   │       │   │   │   ├── metric-card.view-model.ts
│   │       │   │   │   ├── quant-notebook.test.tsx
│   │       │   │   │   ├── quant-notebook.tsx
│   │       │   │   │   ├── quant-notebook.view-model.test.ts
│   │       │   │   │   ├── quant-notebook.view-model.ts
│   │       │   │   │   ├── quant-plot.test.tsx
│   │       │   │   │   ├── quant-plot.tsx
│   │       │   │   │   ├── quant-plot.view-model.ts
│   │       │   │   │   ├── security-details-tracker.test.tsx
│   │       │   │   │   ├── security-details-tracker.tsx
│   │       │   │   │   ├── security-details-tracker.view-model.test.ts
│   │       │   │   │   ├── security-details-tracker.view-model.ts
│   │       │   │   │   ├── ui-kit-primitives.test.tsx
│   │       │   │   │   ├── ui-kit-primitives.tsx
│   │       │   │   │   ├── workspace-header.test.tsx
│   │       │   │   │   ├── workspace-header.tsx
│   │       │   │   │   ├── workspace-header.view-model.test.ts
│   │       │   │   │   ├── workspace-header.view-model.ts
│   │       │   │   │   ├── workspace-nav.test.tsx
│   │       │   │   │   ├── workspace-nav.tsx
│   │       │   │   │   ├── workspace-nav.view-model.test.ts
│   │       │   │   │   └── workspace-nav.view-model.ts
│   │       │   │   ├── settings
│   │       │   │   │   └── provider-credential-setup.tsx
│   │       │   │   └── ui
│   │       │   │       ├── badge.tsx
│   │       │   │       ├── button.test.tsx
│   │       │   │       ├── button.tsx
│   │       │   │       ├── button.view-model.test.ts
│   │       │   │       ├── button.view-model.ts
│   │       │   │       ├── card.tsx
│   │       │   │       ├── dialog.test.tsx
│   │       │   │       ├── dialog.tsx
│   │       │   │       ├── dialog.view-model.test.ts
│   │       │   │       ├── dialog.view-model.ts
│   │       │   │       ├── input.tsx
│   │       │   │       ├── label.tsx
│   │       │   │       ├── progress.tsx
│   │       │   │       ├── risk-control-panel.test.tsx
│   │       │   │       ├── risk-control-panel.tsx
│   │       │   │       ├── risk-control-panel.view-model.test.ts
│   │       │   │       ├── risk-control-panel.view-model.ts
│   │       │   │       ├── select.tsx
│   │       │   │       ├── sheet.test.tsx
│   │       │   │       ├── sheet.tsx
│   │       │   │       └── tooltip.tsx
│   │       │   ├── design-system-contract.test.ts
│   │       │   ├── hooks
│   │       │   │   ├── use-workstation-data.test.ts
│   │       │   │   └── use-workstation-data.ts
│   │       │   ├── lib
│   │       │   │   ├── api
│   │       │   │   │   ├── covered-call.test.ts
│   │       │   │   │   └── covered-call.ts
│   │       │   │   ├── api.trading.test.ts
│   │       │   │   ├── api.ts
│   │       │   │   ├── covered-call
│   │       │   │   │   ├── payoff.test.ts
│   │       │   │   │   └── payoff.ts
│   │       │   │   ├── dev-fixtures.ts
│   │       │   │   ├── price-alerts
│   │       │   │   │   ├── evaluator.test.ts
│   │       │   │   │   ├── evaluator.ts
│   │       │   │   │   ├── service.test.tsx
│   │       │   │   │   ├── service.ts
│   │       │   │   │   ├── storage.test.ts
│   │       │   │   │   ├── storage.ts
│   │       │   │   │   └── types.ts
│   │       │   │   ├── utils.ts
│   │       │   │   ├── workspace.test.ts
│   │       │   │   ├── workspace.ts
│   │       │   │   ├── workstation-endpoints.test.ts
│   │       │   │   └── workstation-endpoints.ts
│   │       │   ├── main.tsx
│   │       │   ├── screens
│   │       │   │   ├── covered-call-screen.test.tsx
│   │       │   │   ├── covered-call-screen.tsx
│   │       │   │   ├── covered-call-screen.view-model.test.ts
│   │       │   │   ├── covered-call-screen.view-model.ts
│   │       │   │   ├── data-operations-screen.security-master.ts
│   │       │   │   ├── data-operations-screen.test.tsx
│   │       │   │   ├── data-operations-screen.tsx
│   │       │   │   ├── data-operations-screen.view-model.test.ts
│   │       │   │   ├── data-operations-screen.view-model.ts
│   │       │   │   ├── evidence-workbench-screen.tsx
│   │       │   │   ├── evidence-workbench-screen.view-model.test.tsx
│   │       │   │   ├── evidence-workbench-screen.view-model.ts
│   │       │   │   ├── governance-screen.test.tsx
│   │       │   │   ├── governance-screen.tsx
│   │       │   │   ├── governance-screen.view-model.test.ts
│   │       │   │   ├── governance-screen.view-model.ts
│   │       │   │   ├── live-quotes-screen.test.tsx
│   │       │   │   ├── live-quotes-screen.tsx
│   │       │   │   ├── live-quotes-screen.view-model.ts
│   │       │   │   ├── operator-readiness-console.test.tsx
│   │       │   │   ├── operator-readiness-console.tsx
│   │       │   │   ├── operator-readiness-console.view-model.test.ts
│   │       │   │   ├── operator-readiness-console.view-model.ts
│   │       │   │   ├── overview-screen.test.tsx
│   │       │   │   ├── overview-screen.tsx
│   │       │   │   ├── overview-screen.view-model.test.ts
│   │       │   │   ├── overview-screen.view-model.ts
│   │       │   │   ├── portfolio-screen.test.tsx
│   │       │   │   ├── portfolio-screen.tsx
│   │       │   │   ├── portfolio-screen.view-model.test.ts
│   │       │   │   ├── portfolio-screen.view-model.ts
│   │       │   │   ├── price-alerts-screen.test.tsx
│   │       │   │   ├── price-alerts-screen.tsx
│   │       │   │   ├── price-alerts-screen.view-model.test.ts
│   │       │   │   ├── price-alerts-screen.view-model.ts
│   │       │   │   ├── quant-lab-screen.test.tsx
│   │       │   │   ├── quant-lab-screen.tsx
│   │       │   │   ├── quant-lab-screen.view-model.test.ts
│   │       │   │   ├── quant-lab-screen.view-model.ts
│   │       │   │   ├── reporting-screen.test.tsx
│   │       │   │   ├── reporting-screen.tsx
│   │       │   │   ├── reporting-screen.view-model.test.ts
│   │       │   │   ├── reporting-screen.view-model.ts
│   │       │   │   ├── research-screen.test.tsx
│   │       │   │   ├── research-screen.tsx
│   │       │   │   ├── research-screen.view-model.test.ts
│   │       │   │   ├── research-screen.view-model.ts
│   │       │   │   ├── settings-screen.test.tsx
│   │       │   │   ├── settings-screen.tsx
│   │       │   │   ├── settings-screen.view-model.test.ts
│   │       │   │   ├── settings-screen.view-model.ts
│   │       │   │   ├── strategy-designer-screen.test.tsx
│   │       │   │   ├── strategy-designer-screen.tsx
│   │       │   │   ├── strategy-designer-screen.view-model.test.ts
│   │       │   │   ├── strategy-designer-screen.view-model.ts
│   │       │   │   ├── today-panel.view-model.test.ts
│   │       │   │   ├── today-panel.view-model.ts
│   │       │   │   ├── trading-screen.test.tsx
│   │       │   │   ├── trading-screen.tsx
│   │       │   │   ├── trading-screen.view-model.test.ts
│   │       │   │   ├── trading-screen.view-model.ts
│   │       │   │   ├── watchlist-screen.test.tsx
│   │       │   │   ├── watchlist-screen.tsx
│   │       │   │   ├── watchlist-screen.view-model.test.ts
│   │       │   │   ├── watchlist-screen.view-model.ts
│   │       │   │   ├── workspace-placeholder-screen.test.tsx
│   │       │   │   ├── workspace-placeholder-screen.tsx
│   │       │   │   ├── workspace-placeholder-screen.view-model.test.ts
│   │       │   │   └── workspace-placeholder-screen.view-model.ts
│   │       │   ├── styles
│   │       │   │   └── index.css
│   │       │   ├── test
│   │       │   │   ├── render.tsx
│   │       │   │   └── setup.ts
│   │       │   ├── types
│   │       │   │   └── covered-call.ts
│   │       │   ├── types.ts
│   │       │   ├── vite-config.test.ts
│   │       │   └── vite-env.d.ts
│   │       ├── tailwind.config.ts
│   │       ├── tsconfig.json
│   │       ├── tsconfig.node.json
│   │       └── vite.config.ts
│   ├── Meridian.Ui.Services
│   │   ├── Collections
│   │   │   ├── BoundedObservableCollection.cs
│   │   │   └── CircularBuffer.cs
│   │   ├── Contracts
│   │   │   ├── ConnectionTypes.cs
│   │   │   ├── IAdminMaintenanceService.cs
│   │   │   ├── IArchiveHealthService.cs
│   │   │   ├── IBackgroundTaskSchedulerService.cs
│   │   │   ├── IConfigService.cs
│   │   │   ├── ICredentialService.cs
│   │   │   ├── ILoggingService.cs
│   │   │   ├── IMessagingService.cs
│   │   │   ├── INotificationService.cs
│   │   │   ├── IOfflineTrackingPersistenceService.cs
│   │   │   ├── IPendingOperationsQueueService.cs
│   │   │   ├── IRefreshScheduler.cs
│   │   │   ├── ISchemaService.cs
│   │   │   ├── IStatusService.cs
│   │   │   ├── IThemeService.cs
│   │   │   ├── IWatchlistService.cs
│   │   │   └── NavigationTypes.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.Ui.Services.csproj
│   │   └── Services
│   │       ├── ActivityFeedService.cs
│   │       ├── AdminMaintenanceModels.cs
│   │       ├── AdminMaintenanceServiceBase.cs
│   │       ├── AdvancedAnalyticsModels.cs
│   │       ├── AdvancedAnalyticsServiceBase.cs
│   │       ├── AlertService.cs
│   │       ├── AnalysisExportService.cs
│   │       ├── AnalysisExportWizardService.cs
│   │       ├── ApiClientService.cs
│   │       ├── ArchiveBrowserService.cs
│   │       ├── ArchiveHealthService.cs
│   │       ├── BackendServiceManagerBase.cs
│   │       ├── BackfillApiService.cs
│   │       ├── BackfillCheckpointService.cs
│   │       ├── BackfillProviderConfigService.cs
│   │       ├── BackfillService.cs
│   │       ├── BatchExportSchedulerService.cs
│   │       ├── ChartingService.cs
│   │       ├── CollectionSessionService.cs
│   │       ├── ColorPalette.cs
│   │       ├── CommandPaletteService.cs
│   │       ├── ConfigService.cs
│   │       ├── ConfigServiceBase.cs
│   │       ├── ConnectionServiceBase.cs
│   │       ├── CredentialService.cs
│   │       ├── DataCalendarService.cs
│   │       ├── DataCompletenessService.cs
│   │       ├── DataQuality
│   │       │   ├── DataQualityApiClient.cs
│   │       │   ├── DataQualityModels.cs
│   │       │   ├── DataQualityPresentationService.cs
│   │       │   ├── DataQualityRefreshService.cs
│   │       │   ├── IDataQualityApiClient.cs
│   │       │   ├── IDataQualityPresentationService.cs
│   │       │   └── IDataQualityRefreshService.cs
│   │       ├── DataQualityRefreshCoordinator.cs
│   │       ├── DataQualityServiceBase.cs
│   │       ├── DataSamplingService.cs
│   │       ├── DesktopJsonOptions.cs
│   │       ├── DesktopShellPreferences.cs
│   │       ├── DiagnosticsService.cs
│   │       ├── ErrorHandlingService.cs
│   │       ├── ErrorMessages.cs
│   │       ├── EventReplayService.cs
│   │       ├── ExportPresetServiceBase.cs
│   │       ├── FixtureDataService.cs
│   │       ├── FixtureModeDetector.cs
│   │       ├── FixtureScenario.cs
│   │       ├── FormValidationRules.cs
│   │       ├── FormatHelpers.cs
│   │       ├── HttpClientConfiguration.cs
│   │       ├── InfoBarConstants.cs
│   │       ├── IntegrityEventsService.cs
│   │       ├── LeanIntegrationService.cs
│   │       ├── LiveDataService.cs
│   │       ├── LoggingService.cs
│   │       ├── LoggingServiceBase.cs
│   │       ├── ManifestService.cs
│   │       ├── NavigationServiceBase.cs
│   │       ├── NotificationService.cs
│   │       ├── NotificationServiceBase.cs
│   │       ├── OAuthRefreshService.cs
│   │       ├── OnboardingTourService.cs
│   │       ├── OperationResult.cs
│   │       ├── OrderBookVisualizationService.cs
│   │       ├── PeriodicRefreshScheduler.cs
│   │       ├── PortablePackagerService.cs
│   │       ├── PortfolioImportService.cs
│   │       ├── ProviderHealthService.cs
│   │       ├── ProviderManagementService.cs
│   │       ├── ProviderOperationsResults.cs
│   │       ├── QualityArchiveStore.cs
│   │       ├── Reconciliation
│   │       │   └── ReconciliationApiService.cs
│   │       ├── RetentionAssuranceModels.cs
│   │       ├── ScheduleManagerService.cs
│   │       ├── ScheduledMaintenanceService.cs
│   │       ├── SchemaService.cs
│   │       ├── SchemaServiceBase.cs
│   │       ├── SearchService.cs
│   │       ├── SettingsConfigurationService.cs
│   │       ├── SetupWizardService.cs
│   │       ├── SmartRecommendationsService.cs
│   │       ├── StatusServiceBase.cs
│   │       ├── StorageAnalyticsService.cs
│   │       ├── StorageModels.cs
│   │       ├── StorageOptimizationAdvisorService.cs
│   │       ├── StorageServiceBase.cs
│   │       ├── SymbolGroupService.cs
│   │       ├── SymbolManagementService.cs
│   │       ├── SymbolMappingService.cs
│   │       ├── SystemHealthService.cs
│   │       ├── ThemeServiceBase.cs
│   │       ├── TimeSeriesAlignmentService.cs
│   │       ├── TooltipContent.cs
│   │       ├── WatchlistService.cs
│   │       └── WorkspaceModels.cs
│   ├── Meridian.Ui.Shared
│   │   ├── Contracts
│   │   │   ├── CoveredCallContracts.cs
│   │   │   └── Reconciliation
│   │   │       └── StatementImportContracts.cs
│   │   ├── DtoExtensions.cs
│   │   ├── Endpoints
│   │   │   ├── AdminEndpoints.cs
│   │   │   ├── AnalyticsEndpoints.cs
│   │   │   ├── ApiKeyMiddleware.cs
│   │   │   ├── AuthEndpoints.cs
│   │   │   ├── AuthenticationMode.cs
│   │   │   ├── BackfillEndpoints.cs
│   │   │   ├── BackfillScheduleEndpoints.cs
│   │   │   ├── BackfillValidationEndpoints.cs
│   │   │   ├── BankingEndpoints.cs
│   │   │   ├── BondReferenceEndpoints.cs
│   │   │   ├── BrokerageConnectionEndpoints.cs
│   │   │   ├── CalendarEndpoints.cs
│   │   │   ├── CanonicalizationEndpoints.cs
│   │   │   ├── CatalogEndpoints.cs
│   │   │   ├── CertificateOfDepositReferenceEndpoints.cs
│   │   │   ├── CheckpointEndpoints.cs
│   │   │   ├── CommodityReferenceEndpoints.cs
│   │   │   ├── ConfigEndpoints.cs
│   │   │   ├── CoveredCallEndpoints.cs
│   │   │   ├── CppTraderEndpoints.cs
│   │   │   ├── CredentialEndpoints.cs
│   │   │   ├── CronEndpoints.cs
│   │   │   ├── CryptoReferenceEndpoints.cs
│   │   │   ├── DemoModeEndpoints.cs
│   │   │   ├── DepositReferenceEndpoints.cs
│   │   │   ├── DiagnosticsEndpoints.cs
│   │   │   ├── DirectLendingEndpoints.cs
│   │   │   ├── EdgarReferenceDataEndpoints.cs
│   │   │   ├── EndpointHelpers.cs
│   │   │   ├── EnvironmentDesignerEndpoints.cs
│   │   │   ├── EquityReferenceEndpoints.cs
│   │   │   ├── EvidenceEndpoints.cs
│   │   │   ├── ExecutionEndpoints.cs
│   │   │   ├── ExportEndpoints.cs
│   │   │   ├── FailoverEndpoints.cs
│   │   │   ├── FundAccountEndpoints.cs
│   │   │   ├── FundStructureEndpoints.cs
│   │   │   ├── FutureReferenceEndpoints.cs
│   │   │   ├── FxSpotReferenceEndpoints.cs
│   │   │   ├── HealthEndpoints.cs
│   │   │   ├── HistoricalEndpoints.cs
│   │   │   ├── IBEndpoints.cs
│   │   │   ├── IngestionJobEndpoints.cs
│   │   │   ├── LeanEndpoints.cs
│   │   │   ├── LedgerEndpoints.cs
│   │   │   ├── LiveDataEndpoints.cs
│   │   │   ├── LoginSessionMiddleware.cs
│   │   │   ├── MaintenanceScheduleEndpoints.cs
│   │   │   ├── MessagingEndpoints.cs
│   │   │   ├── MoneyMarketFundEndpoints.cs
│   │   │   ├── MoneyMarketFundReferenceEndpoints.cs
│   │   │   ├── OptionChainEndpoints.cs
│   │   │   ├── OptionReferenceEndpoints.cs
│   │   │   ├── OptionsEndpoints.cs
│   │   │   ├── PathValidation.cs
│   │   │   ├── PromotionEndpoints.cs
│   │   │   ├── ProviderConnectionEndpoints.cs
│   │   │   ├── ProviderCredentialEndpoints.cs
│   │   │   ├── ProviderEndpoints.cs
│   │   │   ├── ProviderExtendedEndpoints.cs
│   │   │   ├── QuantLabEndpoints.cs
│   │   │   ├── ReplayEndpoints.cs
│   │   │   ├── ResilienceEndpoints.cs
│   │   │   ├── RiskEndpoints.cs
│   │   │   ├── SamplingEndpoints.cs
│   │   │   ├── SecurityMasterEndpoints.cs
│   │   │   ├── StatusEndpoints.cs
│   │   │   ├── StorageEndpoints.cs
│   │   │   ├── StorageQualityEndpoints.cs
│   │   │   ├── StrategyLifecycleEndpoints.cs
│   │   │   ├── SubscriptionEndpoints.cs
│   │   │   ├── SwapReferenceEndpoints.cs
│   │   │   ├── SymbolEndpoints.cs
│   │   │   ├── SymbolMappingEndpoints.cs
│   │   │   ├── UiEndpoints.cs
│   │   │   ├── WorkstationEndpoints.cs
│   │   │   └── WorkstationRiskEndpoints.cs
│   │   ├── Evidence
│   │   │   ├── EvidenceContribution.cs
│   │   │   ├── EvidenceContributors.cs
│   │   │   ├── EvidenceGraphService.cs
│   │   │   ├── EvidenceSubjectResolver.cs
│   │   │   ├── EvidenceTemplateRegistry.cs
│   │   │   ├── EvidenceWorkflowServiceCollectionExtensions.cs
│   │   │   └── FileEvidenceArtifactStore.cs
│   │   ├── GlobalUsings.cs
│   │   ├── HtmlTemplateGenerator.Login.cs
│   │   ├── HtmlTemplateGenerator.Scripts.cs
│   │   ├── HtmlTemplateGenerator.Styles.cs
│   │   ├── HtmlTemplateGenerator.cs
│   │   ├── LeanAutoExportService.cs
│   │   ├── LeanSymbolMapper.cs
│   │   ├── LoginSessionService.cs
│   │   ├── Meridian.Ui.Shared.csproj
│   │   ├── ScoreExplanationProjection.cs
│   │   ├── Serialization
│   │   │   ├── CoveredCallJsonContext.cs
│   │   │   └── DirectLendingJsonContext.cs
│   │   ├── Services
│   │   │   ├── AlpacaBrokerageConnectionService.cs
│   │   │   ├── BackfillCoordinator.cs
│   │   │   ├── BrokerageConnectionService.cs
│   │   │   ├── BrokeragePortfolioSyncService.cs
│   │   │   ├── ConfigStore.cs
│   │   │   ├── CoveredCall
│   │   │   │   ├── CoveredCallBacktestOptions.cs
│   │   │   │   ├── CoveredCallBacktestService.cs
│   │   │   │   ├── CoveredCallChainProviderAdapter.cs
│   │   │   │   ├── CoveredCallChainProviderFactory.cs
│   │   │   │   ├── CoveredCallRunProjection.cs
│   │   │   │   ├── ICoveredCallBacktestService.cs
│   │   │   │   └── ICoveredCallChainProviderFactory.cs
│   │   │   ├── Dk1TrustGateReadinessService.cs
│   │   │   ├── FundOperationsWorkspaceReadService.cs
│   │   │   ├── GovernanceReportPackRepository.cs
│   │   │   ├── InMemoryOperatorInboxService.cs
│   │   │   ├── OperatorRiskRuleService.cs
│   │   │   ├── ProviderConnectionLifecycleService.cs
│   │   │   ├── RiskRuleRuntimeService.cs
│   │   │   ├── SecurityMasterSecurityReferenceLookup.cs
│   │   │   ├── SecurityMasterWorkbenchQueryService.cs
│   │   │   ├── StrategyRunReviewPacketService.cs
│   │   │   ├── TradingOperatorReadinessService.cs
│   │   │   └── WorkstationWorkflowSummaryService.cs
│   │   ├── UserProfileRegistry.cs
│   │   └── Workflows
│   │       ├── BuiltInWorkflowDefinitionProvider.cs
│   │       ├── FileWorkflowPresetStore.cs
│   │       ├── IWorkflowActionCatalog.cs
│   │       ├── IWorkflowDefinitionProvider.cs
│   │       ├── IWorkflowPresetStore.cs
│   │       ├── WorkflowActionIds.cs
│   │       ├── WorkflowLibraryService.cs
│   │       ├── WorkflowPresetService.cs
│   │       ├── WorkflowRegistry.cs
│   │       └── WorkflowServiceCollectionExtensions.cs
│   └── Meridian.Wpf
│       ├── App.xaml
│       ├── App.xaml.cs
│       ├── AssemblyInfo.cs
│       ├── Assets
│       │   ├── Brand
│       │   │   ├── meridian-hero.svg
│       │   │   ├── meridian-mark.svg
│       │   │   ├── meridian-tile-256.png
│       │   │   ├── meridian-tile.svg
│       │   │   └── meridian-wordmark.svg
│       │   ├── Icons
│       │   │   ├── README.md
│       │   │   ├── account-portfolio.svg
│       │   │   ├── admin-maintenance.svg
│       │   │   ├── aggregate-portfolio.svg
│       │   │   ├── archive-health.svg
│       │   │   ├── backfill.svg
│       │   │   ├── backtest.svg
│       │   │   ├── charting.svg
│       │   │   ├── collection-sessions.svg
│       │   │   ├── dashboard.svg
│       │   │   ├── data-browser.svg
│       │   │   ├── data-calendar.svg
│       │   │   ├── data-export.svg
│       │   │   ├── data-operations.svg
│       │   │   ├── data-quality.svg
│       │   │   ├── data-sampling.svg
│       │   │   ├── data-sources.svg
│       │   │   ├── diagnostics.svg
│       │   │   ├── event-replay.svg
│       │   │   ├── governance.svg
│       │   │   ├── help.svg
│       │   │   ├── index-subscription.svg
│       │   │   ├── keyboard-shortcuts.svg
│       │   │   ├── lean-integration.svg
│       │   │   ├── live-data.svg
│       │   │   ├── order-book.svg
│       │   │   ├── portfolio-import.svg
│       │   │   ├── provider-health.svg
│       │   │   ├── research.svg
│       │   │   ├── retention-assurance.svg
│       │   │   ├── run-detail.svg
│       │   │   ├── run-ledger.svg
│       │   │   ├── run-mat.svg
│       │   │   ├── run-portfolio.svg
│       │   │   ├── schedule-manager.svg
│       │   │   ├── security-master.svg
│       │   │   ├── service-manager.svg
│       │   │   ├── settings.svg
│       │   │   ├── storage-optimization.svg
│       │   │   ├── storage.svg
│       │   │   ├── strategy-runs.svg
│       │   │   ├── symbol-storage.svg
│       │   │   ├── symbols.svg
│       │   │   ├── system-health.svg
│       │   │   ├── trading-hours.svg
│       │   │   ├── trading.svg
│       │   │   └── watchlist.svg
│       │   └── app.ico
│       ├── Behaviors
│       │   ├── AvalonEditNotebookBehavior.cs
│       │   ├── ParameterTemplateSelector.cs
│       │   └── PlotRenderBehavior.cs
│       ├── Contracts
│       │   ├── IConnectionService.cs
│       │   └── INavigationService.cs
│       ├── Controls
│       │   └── AutomationLeafBorder.cs
│       ├── Converters
│       │   ├── BoolToStringConverter.cs
│       │   ├── BoolToVisibilityConverter.cs
│       │   ├── ConsoleEntryKindToBrushConverter.cs
│       │   ├── CountToVisibilityConverter.cs
│       │   ├── IntToVisibilityConverter.cs
│       │   ├── InvertBoolConverter.cs
│       │   ├── NullToCollapsedConverter.cs
│       │   ├── StringToBoolConverter.cs
│       │   └── StringToVisibilityConverter.cs
│       ├── Copy
│       │   └── WorkspaceCopyCatalog.cs
│       ├── Features
│       │   ├── Data
│       │   │   ├── DataFeatureModule.cs
│       │   │   └── Shell
│       │   │       ├── DataWorkspaceShellPage.xaml
│       │   │       ├── DataWorkspaceShellPage.xaml.cs
│       │   │       ├── DataWorkspaceShellPresentationService.cs
│       │   │       ├── DataWorkspaceShellSnapshotService.cs
│       │   │       └── DataWorkspaceShellViewModel.cs
│       │   ├── DesktopFeatureModuleRegistry.cs
│       │   └── IDesktopFeatureModule.cs
│       ├── GlobalUsings.cs
│       ├── MainWindow.xaml
│       ├── MainWindow.xaml.cs
│       ├── Meridian.Wpf.csproj
│       ├── Models
│       │   ├── ActionEntry.cs
│       │   ├── ActivityLogModels.cs
│       │   ├── AlignmentModels.cs
│       │   ├── AppConfig.cs
│       │   ├── BackfillModels.cs
│       │   ├── BlotterModels.cs
│       │   ├── DashboardModels.cs
│       │   ├── DataQualityModels.cs
│       │   ├── FundLedgerDimensionView.cs
│       │   ├── FundProfileModels.cs
│       │   ├── FundReconciliationWorkbenchModels.cs
│       │   ├── LeanModels.cs
│       │   ├── LiveDataModels.cs
│       │   ├── NotificationModels.cs
│       │   ├── OrderBookModels.cs
│       │   ├── PaneDropAction.cs
│       │   ├── PaneDropEventArgs.cs
│       │   ├── PaneLayout.cs
│       │   ├── ProviderHealthModels.cs
│       │   ├── QuantScriptExecutionHistoryModels.cs
│       │   ├── QuantScriptModels.cs
│       │   ├── ResearchWorkspaceShellPresentationModels.cs
│       │   ├── SecurityMasterPresentationModels.cs
│       │   ├── SettingsModels.cs
│       │   ├── ShellNavigationCatalog.DataOperations.cs
│       │   ├── ShellNavigationCatalog.Governance.cs
│       │   ├── ShellNavigationCatalog.Research.cs
│       │   ├── ShellNavigationCatalog.Trading.cs
│       │   ├── ShellNavigationCatalog.Workspaces.cs
│       │   ├── ShellNavigationCatalog.cs
│       │   ├── ShellNavigationModels.cs
│       │   ├── ShellNavigationTextStyleGuide.cs
│       │   ├── StorageDisplayModels.cs
│       │   ├── SymbolsModels.cs
│       │   ├── TradingWorkspaceShellPresentationModels.cs
│       │   ├── WatchlistModels.cs
│       │   ├── WorkspaceDefinition.cs
│       │   ├── WorkspaceQueueRegionState.cs
│       │   ├── WorkspaceRegistry.cs
│       │   ├── WorkspaceShellChromeModels.cs
│       │   ├── WorkspaceShellModels.cs
│       │   └── WorkstationOperatingContextModels.cs
│       ├── Package.appxmanifest
│       ├── README.md
│       ├── Services
│       │   ├── AgentLoopService.cs
│       │   ├── ApiStatusService.cs
│       │   ├── ArchiveHealthService.cs
│       │   ├── BackendServiceManager.cs
│       │   ├── BackgroundTaskSchedulerService.cs
│       │   ├── BacktestDataAvailabilityService.cs
│       │   ├── BacktestService.cs
│       │   ├── BrushRegistry.cs
│       │   ├── CashFinancingReadService.cs
│       │   ├── ClipboardWatcherService.cs
│       │   ├── ConfigService.cs
│       │   ├── ConnectionService.cs
│       │   ├── ContextMenuService.cs
│       │   ├── CredentialService.cs
│       │   ├── DataOperationsWorkspacePresentationBuilder.cs
│       │   ├── DesktopLaunchArguments.cs
│       │   ├── DropImportService.cs
│       │   ├── ExportFormat.cs
│       │   ├── ExportPresetService.cs
│       │   ├── FirstRunService.cs
│       │   ├── FloatingPageService.cs
│       │   ├── FormValidationService.cs
│       │   ├── FundAccountReadService.cs
│       │   ├── FundContextService.cs
│       │   ├── FundLedgerReadService.cs
│       │   ├── FundProfileKeyTranslator.cs
│       │   ├── FundReconciliationWorkbenchService.cs
│       │   ├── GlobalHotkeyService.cs
│       │   ├── ICommandContextProvider.cs
│       │   ├── IFundProfileCatalog.cs
│       │   ├── IQuantScriptLayoutService.cs
│       │   ├── IWorkspaceShellStateProvider.cs
│       │   ├── InfoBarService.cs
│       │   ├── JumpListService.cs
│       │   ├── KeyboardShortcutService.cs
│       │   ├── LoggingService.cs
│       │   ├── MessagingService.cs
│       │   ├── NavigationService.cs
│       │   ├── NotificationService.cs
│       │   ├── OfflineTrackingPersistenceService.cs
│       │   ├── PendingOperationsQueueService.cs
│       │   ├── QuantScriptExecutionHistoryService.cs
│       │   ├── QuantScriptLayoutService.cs
│       │   ├── QuantScriptStorageJsonContext.cs
│       │   ├── QuantScriptTemplateCatalogService.cs
│       │   ├── ReconciliationReadService.cs
│       │   ├── ResearchWorkspaceShellPresentationService.cs
│       │   ├── RetentionAssuranceService.cs
│       │   ├── RunMatService.cs
│       │   ├── SchemaService.cs
│       │   ├── SecurityMasterOperatorWorkflowClient.cs
│       │   ├── SecurityMasterRuntimeStatusService.cs
│       │   ├── SingleInstanceService.cs
│       │   ├── StatusService.cs
│       │   ├── StorageService.cs
│       │   ├── StrategyRunWorkspaceService.cs
│       │   ├── SystemTrayService.cs
│       │   ├── TaskbarProgressService.cs
│       │   ├── TearOffPanelService.cs
│       │   ├── ThemeService.cs
│       │   ├── TickerStripService.cs
│       │   ├── ToastNotificationService.cs
│       │   ├── TooltipService.cs
│       │   ├── TradingWorkspaceShellPresentationService.cs
│       │   ├── TypeForwards.cs
│       │   ├── WatchlistService.cs
│       │   ├── WindowStartupRecovery.cs
│       │   ├── WorkspaceService.cs
│       │   ├── WorkspaceShellContextService.cs
│       │   ├── WorkspaceShellStateProviders.cs
│       │   ├── WorkstationOperatingContextService.cs
│       │   ├── WorkstationOperatorInboxApiClient.cs
│       │   ├── WorkstationReconciliationApiClient.cs
│       │   ├── WorkstationResearchBriefingService.cs
│       │   ├── WorkstationSecurityMasterApiClient.cs
│       │   └── WpfShellServiceCollectionExtensions.cs
│       ├── Shell
│       │   ├── Models
│       │   │   ├── PaneContentState.cs
│       │   │   ├── PaneDropResult.cs
│       │   │   └── ShellRoute.cs
│       │   ├── Refresh
│       │   │   └── ShellRefreshCoordinator.cs
│       │   ├── Root
│       │   │   ├── DesktopLaunchRouter.cs
│       │   │   ├── DesktopShellCoordinator.cs
│       │   │   └── FileDropRouter.cs
│       │   ├── Services
│       │   │   ├── IPageContentFactory.cs
│       │   │   ├── IShellNavigationCoordinator.cs
│       │   │   ├── IShellRouteRegistry.cs
│       │   │   ├── PageContentFactory.cs
│       │   │   ├── ShellNavigationCoordinator.cs
│       │   │   └── ShellRouteRegistry.cs
│       │   ├── Session
│       │   │   ├── DesktopShellSessionService.cs
│       │   │   ├── DesktopWindowState.cs
│       │   │   ├── IWindowStateStore.cs
│       │   │   └── WindowStateStore.cs
│       │   └── ViewModels
│       │       ├── CommandPaletteViewModel.cs
│       │       ├── OperatorInboxViewModel.cs
│       │       ├── PaneHostViewModel.cs
│       │       └── WorkflowSummaryStripViewModel.cs
│       ├── Styles
│       │   ├── Animations.xaml
│       │   ├── AppStyles.xaml
│       │   ├── BrandResources.xaml
│       │   ├── IconResources.xaml
│       │   ├── ThemeControls.xaml
│       │   ├── ThemeSurfaces.xaml
│       │   ├── ThemeTokens.xaml
│       │   └── ThemeTypography.xaml
│       ├── Templates
│       │   └── QuantScript
│       │       ├── catalog.json
│       │       ├── hello-spy.csx
│       │       ├── indicator-sma.csx
│       │       └── single-symbol-backtest.csx
│       ├── ViewModels
│       │   ├── AccountPortfolioViewModel.cs
│       │   ├── ActivityLogViewModel.cs
│       │   ├── AddProviderWizardViewModel.cs
│       │   ├── AdminMaintenanceViewModel.cs
│       │   ├── AdvancedAnalyticsViewModel.cs
│       │   ├── AgentViewModel.cs
│       │   ├── AggregatePortfolioViewModel.cs
│       │   ├── AnalysisExportViewModel.cs
│       │   ├── AnalysisExportWizardViewModel.cs
│       │   ├── BackfillViewModel.cs
│       │   ├── BacktestViewModel.cs
│       │   ├── BatchBacktestViewModel.cs
│       │   ├── BindableBase.cs
│       │   ├── CarryTradeBacktestViewModel.cs
│       │   ├── CashFlowViewModel.cs
│       │   ├── ChartingPageViewModel.cs
│       │   ├── ClusterStatusViewModel.cs
│       │   ├── CollectionSessionViewModel.cs
│       │   ├── CredentialManagementViewModel.cs
│       │   ├── DashboardViewModel.cs
│       │   ├── DataBrowserViewModel.cs
│       │   ├── DataCalendarViewModel.cs
│       │   ├── DataExportViewModel.cs
│       │   ├── DataQualityViewModel.cs
│       │   ├── DataSamplingViewModel.cs
│       │   ├── DataSourcesViewModel.cs
│       │   ├── DiagnosticsPageViewModel.cs
│       │   ├── DirectLendingViewModel.cs
│       │   ├── EventReplayViewModel.cs
│       │   ├── ExportPresetsViewModel.cs
│       │   ├── FundAccountProviderPanelModels.cs
│       │   ├── FundAccountsViewModel.cs
│       │   ├── FundLedgerViewModel.Reconciliation.cs
│       │   ├── FundLedgerViewModel.cs
│       │   ├── FundProfileSelectionViewModel.cs
│       │   ├── IPageActionBarProvider.cs
│       │   ├── IndexSubscriptionViewModel.cs
│       │   ├── LeanIntegrationViewModel.cs
│       │   ├── LiveDataViewerViewModel.cs
│       │   ├── MainPageViewModel.cs
│       │   ├── MainWindowViewModel.cs
│       │   ├── MessagingHubViewModel.cs
│       │   ├── NotificationCenterViewModel.cs
│       │   ├── OptionsViewModel.cs
│       │   ├── OrderBookHeatmapViewModel.cs
│       │   ├── OrderBookViewModel.cs
│       │   ├── PackageManagerViewModel.cs
│       │   ├── PluginManagementViewModel.cs
│       │   ├── PortfolioImportViewModel.cs
│       │   ├── PositionBlotterViewModel.cs
│       │   ├── ProviderHealthViewModel.cs
│       │   ├── ProviderPageModels.cs
│       │   ├── ProviderViewModel.cs
│       │   ├── QualityArchiveViewModel.cs
│       │   ├── QuantScriptViewModel.cs
│       │   ├── QuoteFloatViewModel.cs
│       │   ├── ResearchWorkspaceShellViewModel.cs
│       │   ├── RetentionAssuranceViewModel.cs
│       │   ├── RunMatViewModel.cs
│       │   ├── RunRiskViewModel.cs
│       │   ├── ScatterAnalysisViewModel.cs
│       │   ├── ScheduleManagerViewModel.cs
│       │   ├── SecurityConflictLaneModels.cs
│       │   ├── SecurityMasterDeactivateViewModel.cs
│       │   ├── SecurityMasterEditViewModel.cs
│       │   ├── SecurityMasterViewModel.cs
│       │   ├── ServiceManagerViewModel.cs
│       │   ├── SettingsViewModel.cs
│       │   ├── SplitPaneViewModel.cs
│       │   ├── StatusBarViewModel.cs
│       │   ├── StorageViewModel.cs
│       │   ├── StrategyRunBrowserViewModel.cs
│       │   ├── StrategyRunDetailViewModel.cs
│       │   ├── StrategyRunLedgerViewModel.cs
│       │   ├── StrategyRunPortfolioViewModel.cs
│       │   ├── SymbolMappingViewModel.cs
│       │   ├── SymbolsPageViewModel.cs
│       │   ├── SystemHealthViewModel.cs
│       │   ├── TickerStripViewModel.cs
│       │   ├── TimeSeriesAlignmentViewModel.cs
│       │   ├── TradingHoursViewModel.cs
│       │   ├── TradingWorkspaceShellViewModel.cs
│       │   ├── WatchlistViewModel.cs
│       │   ├── WelcomePageViewModel.cs
│       │   ├── WorkflowLibraryViewModel.cs
│       │   ├── WorkspacePageViewModel.cs
│       │   └── WorkspaceShellViewModelBase.cs
│       └── Views
│           ├── AccountPortfolioPage.xaml
│           ├── AccountPortfolioPage.xaml.cs
│           ├── ActivityLogPage.xaml
│           ├── ActivityLogPage.xaml.cs
│           ├── AddProviderWizardPage.xaml
│           ├── AddProviderWizardPage.xaml.cs
│           ├── AdminMaintenancePage.xaml
│           ├── AdminMaintenancePage.xaml.cs
│           ├── AdvancedAnalyticsPage.xaml
│           ├── AdvancedAnalyticsPage.xaml.cs
│           ├── AgentPage.xaml
│           ├── AgentPage.xaml.cs
│           ├── AggregatePortfolioPage.xaml
│           ├── AggregatePortfolioPage.xaml.cs
│           ├── AnalysisExportPage.xaml
│           ├── AnalysisExportPage.xaml.cs
│           ├── AnalysisExportWizardPage.xaml
│           ├── AnalysisExportWizardPage.xaml.cs
│           ├── ApiKeyDialog.xaml
│           ├── ApiKeyDialog.xaml.cs
│           ├── ArchiveHealthPage.xaml
│           ├── ArchiveHealthPage.xaml.cs
│           ├── BackfillPage.xaml
│           ├── BackfillPage.xaml.cs
│           ├── BacktestPage.xaml
│           ├── BacktestPage.xaml.cs
│           ├── BatchBacktestPage.xaml
│           ├── BatchBacktestPage.xaml.cs
│           ├── CarryTradeBacktestPage.xaml
│           ├── ChartingPage.xaml
│           ├── ChartingPage.xaml.cs
│           ├── ClusterStatusPage.xaml
│           ├── ClusterStatusPage.xaml.cs
│           ├── CollectionSessionPage.xaml
│           ├── CollectionSessionPage.xaml.cs
│           ├── CommandPaletteWindow.xaml
│           ├── CommandPaletteWindow.xaml.cs
│           ├── CreateWatchlistDialog.cs
│           ├── CredentialManagementPage.xaml
│           ├── CredentialManagementPage.xaml.cs
│           ├── DashboardPage.xaml
│           ├── DashboardPage.xaml.cs
│           ├── DataBrowserPage.xaml
│           ├── DataBrowserPage.xaml.cs
│           ├── DataCalendarPage.xaml
│           ├── DataCalendarPage.xaml.cs
│           ├── DataExportPage.xaml
│           ├── DataExportPage.xaml.cs
│           ├── DataQualityPage.xaml
│           ├── DataQualityPage.xaml.cs
│           ├── DataSamplingPage.xaml
│           ├── DataSamplingPage.xaml.cs
│           ├── DataSourcesPage.xaml
│           ├── DataSourcesPage.xaml.cs
│           ├── DiagnosticsPage.xaml
│           ├── DiagnosticsPage.xaml.cs
│           ├── DirectLendingPage.xaml
│           ├── DirectLendingPage.xaml.cs
│           ├── EditScheduledJobDialog.xaml
│           ├── EditScheduledJobDialog.xaml.cs
│           ├── EditWatchlistDialog.cs
│           ├── EnvironmentDesignerPage.xaml
│           ├── EnvironmentDesignerPage.xaml.cs
│           ├── EventReplayPage.xaml
│           ├── EventReplayPage.xaml.cs
│           ├── ExportPresetsPage.xaml
│           ├── ExportPresetsPage.xaml.cs
│           ├── FloatingPageWindow.xaml
│           ├── FloatingPageWindow.xaml.cs
│           ├── FundAccountsPage.xaml
│           ├── FundAccountsPage.xaml.cs
│           ├── FundLedgerPage.xaml
│           ├── FundLedgerPage.xaml.cs
│           ├── FundProfileSelectionPage.xaml
│           ├── FundProfileSelectionPage.xaml.cs
│           ├── GovernanceWorkspaceShellPage.xaml
│           ├── GovernanceWorkspaceShellPage.xaml.cs
│           ├── HelpPage.xaml
│           ├── HelpPage.xaml.cs
│           ├── IndexSubscriptionPage.xaml
│           ├── IndexSubscriptionPage.xaml.cs
│           ├── KeyboardShortcutsPage.xaml
│           ├── KeyboardShortcutsPage.xaml.cs
│           ├── LeanIntegrationPage.xaml
│           ├── LeanIntegrationPage.xaml.cs
│           ├── LiveDataViewerPage.xaml
│           ├── LiveDataViewerPage.xaml.cs
│           ├── MainPage.SplitPane.cs
│           ├── MainPage.xaml
│           ├── MainPage.xaml.cs
│           ├── MeridianDockingManager.xaml
│           ├── MeridianDockingManager.xaml.cs
│           ├── MessagingHubPage.xaml
│           ├── MessagingHubPage.xaml.cs
│           ├── NotificationCenterPage.xaml
│           ├── NotificationCenterPage.xaml.cs
│           ├── OptionsPage.xaml
│           ├── OptionsPage.xaml.cs
│           ├── OrderBookHeatmapControl.xaml
│           ├── OrderBookHeatmapControl.xaml.cs
│           ├── OrderBookPage.xaml
│           ├── OrderBookPage.xaml.cs
│           ├── PackageManagerPage.xaml
│           ├── PackageManagerPage.xaml.cs
│           ├── PageActionBarControl.xaml
│           ├── PageActionBarControl.xaml.cs
│           ├── Pages.cs
│           ├── PluginManagementPage.xaml
│           ├── PluginManagementPage.xaml.cs
│           ├── PortfolioImportPage.xaml
│           ├── PortfolioImportPage.xaml.cs
│           ├── PositionBlotterPage.xaml
│           ├── PositionBlotterPage.xaml.cs
│           ├── ProviderHealthPage.xaml
│           ├── ProviderHealthPage.xaml.cs
│           ├── ProviderPage.xaml
│           ├── ProviderPage.xaml.cs
│           ├── QualityArchivePage.xaml
│           ├── QualityArchivePage.xaml.cs
│           ├── QuantScriptPage.xaml
│           ├── QuantScriptPage.xaml.cs
│           ├── QuoteFloatWindow.xaml
│           ├── QuoteFloatWindow.xaml.cs
│           ├── ResearchWorkspaceShellPage.xaml
│           ├── ResearchWorkspaceShellPage.xaml.cs
│           ├── RetentionAssurancePage.xaml
│           ├── RetentionAssurancePage.xaml.cs
│           ├── RunCashFlowPage.xaml
│           ├── RunCashFlowPage.xaml.cs
│           ├── RunDetailPage.xaml
│           ├── RunDetailPage.xaml.cs
│           ├── RunLedgerPage.xaml
│           ├── RunLedgerPage.xaml.cs
│           ├── RunMatPage.xaml
│           ├── RunMatPage.xaml.cs
│           ├── RunPortfolioPage.xaml
│           ├── RunPortfolioPage.xaml.cs
│           ├── RunRiskPage.xaml
│           ├── RunRiskPage.xaml.cs
│           ├── SaveWatchlistDialog.xaml
│           ├── SaveWatchlistDialog.xaml.cs
│           ├── ScatterAnalysisPage.xaml
│           ├── ScatterAnalysisPage.xaml.cs
│           ├── ScheduleManagerPage.xaml
│           ├── ScheduleManagerPage.xaml.cs
│           ├── SecurityMasterPage.xaml
│           ├── SecurityMasterPage.xaml.cs
│           ├── ServiceManagerPage.xaml
│           ├── ServiceManagerPage.xaml.cs
│           ├── SettingsPage.xaml
│           ├── SettingsPage.xaml.cs
│           ├── SetupWizardPage.xaml
│           ├── SetupWizardPage.xaml.cs
│           ├── SplitPaneHostControl.xaml
│           ├── SplitPaneHostControl.xaml.cs
│           ├── StatusBarControl.xaml
│           ├── StatusBarControl.xaml.cs
│           ├── StorageOptimizationPage.xaml
│           ├── StorageOptimizationPage.xaml.cs
│           ├── StoragePage.xaml
│           ├── StoragePage.xaml.cs
│           ├── StrategyRunsPage.xaml
│           ├── StrategyRunsPage.xaml.cs
│           ├── SymbolMappingPage.xaml
│           ├── SymbolMappingPage.xaml.cs
│           ├── SymbolStoragePage.xaml
│           ├── SymbolStoragePage.xaml.cs
│           ├── SymbolsPage.xaml
│           ├── SymbolsPage.xaml.cs
│           ├── SystemHealthPage.xaml
│           ├── SystemHealthPage.xaml.cs
│           ├── TickerStripWindow.xaml
│           ├── TickerStripWindow.xaml.cs
│           ├── TimeSeriesAlignmentPage.xaml
│           ├── TimeSeriesAlignmentPage.xaml.cs
│           ├── TradingHoursPage.xaml
│           ├── TradingHoursPage.xaml.cs
│           ├── TradingWorkspaceShellPage.xaml
│           ├── TradingWorkspaceShellPage.xaml.cs
│           ├── WatchlistPage.xaml
│           ├── WatchlistPage.xaml.cs
│           ├── WelcomePage.xaml
│           ├── WelcomePage.xaml.cs
│           ├── WorkflowLibraryPage.xaml
│           ├── WorkflowLibraryPage.xaml.cs
│           ├── WorkspaceCapabilityHomePage.cs
│           ├── WorkspaceCommandBarControl.xaml
│           ├── WorkspaceCommandBarControl.xaml.cs
│           ├── WorkspaceDeepPageHostPage.xaml
│           ├── WorkspaceDeepPageHostPage.xaml.cs
│           ├── WorkspacePage.xaml
│           ├── WorkspacePage.xaml.cs
│           ├── WorkspaceShellChromeState.cs
│           ├── WorkspaceShellContextStripControl.xaml
│           ├── WorkspaceShellContextStripControl.xaml.cs
│           ├── WorkspaceShellFallbackContentFactory.cs
│           └── WorkspaceShellPageBase.cs
├── tests
│   ├── Directory.Build.props
│   ├── Meridian.Backtesting.Tests
│   │   ├── AdvancedCarryDecisionEngineTests.cs
│   │   ├── BacktestEngineIntegrationTests.cs
│   │   ├── BacktestMetricsEngineTests.cs
│   │   ├── BacktestPreflightServiceTests.cs
│   │   ├── BacktestRequestConfigTests.cs
│   │   ├── BatchBacktestServiceTests.cs
│   │   ├── BracketOrderTests.cs
│   │   ├── CorporateActionAdjustmentServiceTests.cs
│   │   ├── FillModelExpansionTests.cs
│   │   ├── FillModelTests.cs
│   │   ├── GlobalUsings.cs
│   │   ├── LedgerQueryTests.cs
│   │   ├── LotLevelTrackingTests.cs
│   │   ├── MarketImpactFillModelTests.cs
│   │   ├── Meridian.Backtesting.Tests.csproj
│   │   ├── MeridianNativeBacktestStudioEngineTests.cs
│   │   ├── OptionsOverwriteStrategyTests.cs
│   │   ├── SimulatedPortfolioTests.cs
│   │   ├── StageTelemetryTests.cs
│   │   ├── TcaReporterTests.cs
│   │   ├── XirrCalculatorTests.cs
│   │   └── YahooFinanceBacktestIntegrationTests.cs
│   ├── Meridian.DirectLending.Tests
│   │   ├── BankTransactionSeedTests.cs
│   │   ├── DirectLendingDatabaseFactAttribute.cs
│   │   ├── DirectLendingPostgresIntegrationTests.cs
│   │   ├── DirectLendingPostgresTestDatabase.cs
│   │   ├── DirectLendingServiceTests.cs
│   │   ├── DirectLendingWorkflowTests.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.DirectLending.Tests.csproj
│   │   └── PaymentApprovalTests.cs
│   ├── Meridian.FSharp.Tests
│   │   ├── AccountDetailsTests.fs
│   │   ├── CalculationTests.fs
│   │   ├── CanonicalizationTests.fs
│   │   ├── CashFlowProjectorTests.fs
│   │   ├── DirectLendingInteropTests.fs
│   │   ├── DomainTests.fs
│   │   ├── LedgerKernelTests.fs
│   │   ├── Meridian.FSharp.Tests.fsproj
│   │   ├── PeriodManagementTests.fs
│   │   ├── PipelineTests.fs
│   │   ├── PromotionPolicyTests.fs
│   │   ├── RiskPolicyTests.fs
│   │   ├── SettlementInstructionCommandsTests.fs
│   │   ├── TradingTransitionTests.fs
│   │   └── ValidationTests.fs
│   ├── Meridian.FundStructure.Tests
│   │   ├── EnvironmentDesignerServiceTests.cs
│   │   ├── GovernanceSharedDataAccessServiceTests.cs
│   │   ├── InMemoryFundStructureServiceTests.cs
│   │   └── Meridian.FundStructure.Tests.csproj
│   ├── Meridian.McpServer.Tests
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.McpServer.Tests.csproj
│   │   └── Tools
│   │       ├── BackfillToolsTests.cs
│   │       ├── RepoNavigationToolsTests.cs
│   │       └── StorageToolsTests.cs
│   ├── Meridian.QuantScript.Tests
│   │   ├── GlobalUsings.cs
│   │   ├── Helpers
│   │   │   ├── FakeQuantDataContext.cs
│   │   │   ├── FakeScriptRunner.cs
│   │   │   └── TestPriceSeriesBuilder.cs
│   │   ├── Meridian.QuantScript.Tests.csproj
│   │   ├── NotebookExecutionSessionTests.cs
│   │   ├── PlotQueueTests.cs
│   │   ├── PortfolioBuilderTests.cs
│   │   ├── PriceSeriesTests.cs
│   │   ├── QuantScriptNotebookStoreTests.cs
│   │   ├── RoslynScriptCompilerTests.cs
│   │   ├── ScriptRunnerTests.cs
│   │   └── StatisticsEngineTests.cs
│   ├── Meridian.Tests
│   │   ├── Application
│   │   │   ├── Backfill
│   │   │   │   ├── AdditionalProviderContractTests.cs
│   │   │   │   ├── AutoGapRemediationServiceTests.cs
│   │   │   │   ├── BackfillCoordinatorPreviewTests.cs
│   │   │   │   ├── BackfillCostEstimatorTests.cs
│   │   │   │   ├── BackfillStatusStoreTests.cs
│   │   │   │   ├── BackfillWorkerServiceTests.cs
│   │   │   │   ├── CompositeHistoricalDataProviderTests.cs
│   │   │   │   ├── GapBackfillServiceTests.cs
│   │   │   │   ├── HistoricalProviderContractTests.cs
│   │   │   │   ├── ParallelBackfillServiceTests.cs
│   │   │   │   ├── PriorityBackfillQueueTests.cs
│   │   │   │   ├── RateLimiterTests.cs
│   │   │   │   ├── ScheduledBackfillTests.cs
│   │   │   │   ├── TwelveDataNasdaqProviderContractTests.cs
│   │   │   │   └── YahooFinanceIntradayContractTests.cs
│   │   │   ├── Backtesting
│   │   │   │   └── BacktestStudioRunOrchestratorTests.cs
│   │   │   ├── Canonicalization
│   │   │   │   ├── CanonicalizationFixtureDriftTests.cs
│   │   │   │   ├── CanonicalizationGoldenFixtureTests.cs
│   │   │   │   └── Fixtures
│   │   │   │       ├── alpaca_trade_extended_hours.json
│   │   │   │       ├── alpaca_trade_odd_lot.json
│   │   │   │       ├── alpaca_trade_regular.json
│   │   │   │       ├── alpaca_xnas_identity.json
│   │   │   │       ├── polygon_trade_extended_hours.json
│   │   │   │       ├── polygon_trade_odd_lot.json
│   │   │   │       ├── polygon_trade_regular.json
│   │   │   │       └── polygon_xnas_identity.json
│   │   │   ├── Commands
│   │   │   │   ├── CliArgumentsTests.cs
│   │   │   │   ├── CommandDispatcherTests.cs
│   │   │   │   ├── ConfigCommandsTests.cs
│   │   │   │   ├── DiagnosticsCommandsTests.cs
│   │   │   │   ├── DryRunCommandTests.cs
│   │   │   │   ├── EtlCommandsTests.cs
│   │   │   │   ├── HelpCommandTests.cs
│   │   │   │   ├── LedgerCliCommandTests.cs
│   │   │   │   ├── PackageCommandsTests.cs
│   │   │   │   ├── SecurityMasterCommandsEdgarTests.cs
│   │   │   │   ├── SelfTestCommandTests.cs
│   │   │   │   ├── SymbolCommandsTests.cs
│   │   │   │   └── ValidateConfigCommandTests.cs
│   │   │   ├── Composition
│   │   │   │   ├── DirectLendingStartupTests.cs
│   │   │   │   ├── ProviderFeatureRegistrationTests.cs
│   │   │   │   ├── SecurityMasterStartupTests.cs
│   │   │   │   ├── Startup
│   │   │   │   │   └── SharedStartupBootstrapperTests.cs
│   │   │   │   └── StorageFeatureRegistrationTests.cs
│   │   │   ├── Config
│   │   │   │   ├── AppSettingsSampleTests.cs
│   │   │   │   ├── ConfigEnvironmentOverrideTests.cs
│   │   │   │   ├── ConfigJsonSchemaGeneratorTests.cs
│   │   │   │   ├── ConfigSchemaIntegrationTests.cs
│   │   │   │   ├── ConfigValidationPipelineTests.cs
│   │   │   │   ├── ConfigValidatorCliTests.cs
│   │   │   │   ├── ConfigValidatorTests.cs
│   │   │   │   ├── ConfigurationUnificationTests.cs
│   │   │   │   ├── ProviderCredentialResolverTests.cs
│   │   │   │   └── ProviderCredentialStoreTests.cs
│   │   │   ├── Coordination
│   │   │   │   ├── ClusterCoordinatorServiceTests.cs
│   │   │   │   ├── LeaseManagerTests.cs
│   │   │   │   ├── SplitBrainDetectorTests.cs
│   │   │   │   └── SubscriptionOrchestratorCoordinationTests.cs
│   │   │   ├── Credentials
│   │   │   │   ├── CredentialStatusTests.cs
│   │   │   │   ├── CredentialTestingServiceTests.cs
│   │   │   │   └── OAuthTokenTests.cs
│   │   │   ├── DirectLending
│   │   │   │   └── DirectLendingOutboxDispatcherTests.cs
│   │   │   ├── DirectLendingServiceTests.cs
│   │   │   ├── Etl
│   │   │   │   ├── EtlJobDefinitionStoreTests.cs
│   │   │   │   ├── EtlJobOrchestratorTests.cs
│   │   │   │   └── EtlNormalizationServiceTests.cs
│   │   │   ├── FundAccounts
│   │   │   │   └── FundAccountServiceTests.cs
│   │   │   ├── FundStructure
│   │   │   │   ├── FundAccountTraversalQueryServiceTests.cs
│   │   │   │   ├── LedgerGroupIdTests.cs
│   │   │   │   └── LedgerGroupingRulesTests.cs
│   │   │   ├── GovernanceExceptionServiceTests.cs
│   │   │   ├── Indicators
│   │   │   │   └── TechnicalIndicatorServiceTests.cs
│   │   │   ├── Ledger
│   │   │   │   └── AccountingPolicyServiceTests.cs
│   │   │   ├── Logging
│   │   │   │   └── LoggingSetupTests.cs
│   │   │   ├── Monitoring
│   │   │   │   ├── AlertDispatcherTests.cs
│   │   │   │   ├── BackpressureAlertServiceTests.cs
│   │   │   │   ├── BadTickFilterTests.cs
│   │   │   │   ├── ClockSkewEstimatorTests.cs
│   │   │   │   ├── DataQuality
│   │   │   │   │   ├── DataFreshnessSlaMonitorTests.cs
│   │   │   │   │   ├── DataQualityTests.cs
│   │   │   │   │   └── LiquidityProfileTests.cs
│   │   │   │   ├── ErrorRingBufferTests.cs
│   │   │   │   ├── PriceContinuityCheckerTests.cs
│   │   │   │   ├── PrometheusMetricsTests.cs
│   │   │   │   ├── ProviderDegradationCalibrationTests.cs
│   │   │   │   ├── ProviderDegradationScorerTests.cs
│   │   │   │   ├── ProviderLatencyServiceTests.cs
│   │   │   │   ├── QualityTrendCalculationTests.cs
│   │   │   │   ├── SchemaValidationServiceTests.cs
│   │   │   │   ├── SloDefinitionRegistryTests.cs
│   │   │   │   ├── SpreadMonitorTests.cs
│   │   │   │   ├── TickSizeValidatorTests.cs
│   │   │   │   └── TracedEventMetricsTests.cs
│   │   │   ├── Pipeline
│   │   │   │   ├── BackfillProgressTrackerTests.cs
│   │   │   │   ├── BackpressureSignalTests.cs
│   │   │   │   ├── CompositePublisherTests.cs
│   │   │   │   ├── DeadLetterSinkTests.cs
│   │   │   │   ├── DroppedEventAuditTrailTests.cs
│   │   │   │   ├── DualPathEventPipelineTests.cs
│   │   │   │   ├── EventPipelineMetricsTests.cs
│   │   │   │   ├── EventPipelineTests.cs
│   │   │   │   ├── EventPipelineTracePropagationTests.cs
│   │   │   │   ├── FSharpEventValidatorTests.cs
│   │   │   │   ├── GoldenMasterPipelineReplayTests.cs
│   │   │   │   ├── HotPathBatchSerializerTests.cs
│   │   │   │   ├── IngestionJobServiceCoordinationTests.cs
│   │   │   │   ├── IngestionJobServiceTests.cs
│   │   │   │   ├── IngestionJobTests.cs
│   │   │   │   ├── MarketDataClientFactoryTests.cs
│   │   │   │   ├── PersistentDedupLedgerTests.cs
│   │   │   │   ├── SpscRingBufferTests.cs
│   │   │   │   └── WalEventPipelineTests.cs
│   │   │   ├── ProviderRouting
│   │   │   │   ├── BestOfBreedProviderSelectorTests.cs
│   │   │   │   ├── KernelObservabilityServiceTests.cs
│   │   │   │   ├── ProviderRoutingServiceTests.cs
│   │   │   │   └── ProviderTrustScoringServiceTests.cs
│   │   │   ├── ReconciliationGovernanceServiceTests.cs
│   │   │   ├── ReconciliationRunServiceTests.cs
│   │   │   ├── SecurityMaster
│   │   │   │   ├── EdgarIngestOrchestratorTests.cs
│   │   │   │   ├── SecurityKindMappingTests.cs
│   │   │   │   ├── SecurityMasterImportServiceTests.cs
│   │   │   │   └── SecurityMasterMappingInteropTests.cs
│   │   │   ├── Services
│   │   │   │   ├── CanonicalizingPublisherTests.cs
│   │   │   │   ├── CliModeResolverTests.cs
│   │   │   │   ├── ConditionCodeMapperTests.cs
│   │   │   │   ├── ConfigurationPresetsTests.cs
│   │   │   │   ├── ConfigurationServiceTests.cs
│   │   │   │   ├── CronExpressionParserTests.cs
│   │   │   │   ├── DataQuality
│   │   │   │   │   ├── AnomalyDetectorTests.cs
│   │   │   │   │   ├── CompletenessScoreCalculatorTests.cs
│   │   │   │   │   ├── GapAnalyzerTests.cs
│   │   │   │   │   └── SequenceErrorTrackerTests.cs
│   │   │   │   ├── ErrorCodeMappingTests.cs
│   │   │   │   ├── EventCanonicalizerTests.cs
│   │   │   │   ├── FundOperationsWorkspaceReadServiceTests.cs
│   │   │   │   ├── GracefulShutdownTests.cs
│   │   │   │   ├── HistoricalDataQueryServiceBarsTests.cs
│   │   │   │   ├── OperationalSchedulerTests.cs
│   │   │   │   ├── OptionsChainServiceTests.cs
│   │   │   │   ├── PreflightCheckerTests.cs
│   │   │   │   ├── ReportGenerationServiceTests.cs
│   │   │   │   ├── TradingCalendarTests.cs
│   │   │   │   └── VenueMicMapperTests.cs
│   │   │   ├── Ui
│   │   │   │   └── ConfigStoreTests.cs
│   │   │   └── Wizard
│   │   │       └── WizardConfigurationStepTests.cs
│   │   ├── Architecture
│   │   │   └── LayerBoundaryTests.cs
│   │   ├── CertificatesOfDeposit
│   │   │   └── CertificateOfDepositProjectionServiceTests.cs
│   │   ├── Commodities
│   │   │   └── CommodityProjectionServiceTests.cs
│   │   ├── Contracts
│   │   │   ├── Api
│   │   │   │   └── UiApiClientTests.cs
│   │   │   └── FundStructureContractsJsonContextTests.cs
│   │   ├── CryptoCurrency
│   │   │   └── CryptoProjectionServiceTests.cs
│   │   ├── Deposits
│   │   │   └── DepositProjectionServiceTests.cs
│   │   ├── Derivatives
│   │   │   └── SwapProjectionServiceTests.cs
│   │   ├── Domain
│   │   │   ├── Collectors
│   │   │   │   ├── L3OrderBookCollectorTests.cs
│   │   │   │   ├── LiveDataAccessTests.cs
│   │   │   │   ├── MarketDepthCollectorTests.cs
│   │   │   │   ├── OptionDataCollectorTests.cs
│   │   │   │   ├── QuoteCollectorTests.cs
│   │   │   │   ├── SessionStatsCollectorTests.cs
│   │   │   │   └── TradeDataCollectorTests.cs
│   │   │   ├── Models
│   │   │   │   ├── AdjustedHistoricalBarTests.cs
│   │   │   │   ├── AggregateBarTests.cs
│   │   │   │   ├── BboQuotePayloadTests.cs
│   │   │   │   ├── EffectiveSymbolTests.cs
│   │   │   │   ├── GreeksSnapshotTests.cs
│   │   │   │   ├── HistoricalBarTests.cs
│   │   │   │   ├── OpenInterestUpdateTests.cs
│   │   │   │   ├── OptionChainSnapshotTests.cs
│   │   │   │   ├── OptionContractSpecTests.cs
│   │   │   │   ├── OptionQuoteTests.cs
│   │   │   │   ├── OptionTradeTests.cs
│   │   │   │   ├── OrderBookLevelTests.cs
│   │   │   │   ├── OrderEventPayloadTests.cs
│   │   │   │   └── TradeModelTests.cs
│   │   │   └── StrongDomainTypeTests.cs
│   │   ├── Equity
│   │   │   └── EquityProjectionServiceTests.cs
│   │   ├── Execution
│   │   │   ├── BrokerageGatewayAdapterTests.cs
│   │   │   ├── Enhancements
│   │   │   │   ├── AllocationEngineTests.cs
│   │   │   │   ├── DerivativePositionTests.cs
│   │   │   │   ├── EventDrivenDecouplingTests.cs
│   │   │   │   ├── MarginModelTests.cs
│   │   │   │   ├── MultiCurrencyTests.cs
│   │   │   │   └── TaxLotAccountingTests.cs
│   │   │   ├── ExecutionAuditTrailServiceTests.cs
│   │   │   ├── MultiAccountPaperTradingPortfolioTests.cs
│   │   │   ├── OrderManagementSystemGovernanceTests.cs
│   │   │   ├── OrderManagementSystemTests.cs
│   │   │   ├── PaperSessionPersistenceServiceTests.cs
│   │   │   ├── PaperTradingGatewayTests.cs
│   │   │   ├── PaperTradingPortfolioLotSelectionTests.cs
│   │   │   ├── PaperTradingPortfolioLotSnapshotTests.cs
│   │   │   ├── PaperTradingPortfolioTests.cs
│   │   │   └── PositionLotSelectorTests.cs
│   │   ├── FixedIncome
│   │   │   └── BondProjectionServiceTests.cs
│   │   ├── Futures
│   │   │   └── FutureProjectionServiceTests.cs
│   │   ├── FxSpot
│   │   │   └── FxSpotProjectionServiceTests.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Infrastructure
│   │   │   ├── CppTrader
│   │   │   │   └── CppTraderOrderGatewayTests.cs
│   │   │   ├── DataSources
│   │   │   │   └── CredentialConfigTests.cs
│   │   │   ├── Etl
│   │   │   │   └── CsvPartnerFileParserTests.cs
│   │   │   ├── Http
│   │   │   │   └── HttpClientConfigurationTests.cs
│   │   │   ├── Providers
│   │   │   │   ├── AlpacaBrokerageGatewayTests.cs
│   │   │   │   ├── AlpacaCorporateActionProviderTests.cs
│   │   │   │   ├── AlpacaCredentialAndReconnectTests.cs
│   │   │   │   ├── AlpacaHistoricalDataProviderTests.cs
│   │   │   │   ├── AlpacaMessageParsingTests.cs
│   │   │   │   ├── AlpacaQuotePipelineGoldenTests.cs
│   │   │   │   ├── AlpacaQuoteRoutingTests.cs
│   │   │   │   ├── BackfillRetryAfterTests.cs
│   │   │   │   ├── EdgarReferenceDataProviderTests.cs
│   │   │   │   ├── EdgarSymbolSearchProviderTests.cs
│   │   │   │   ├── FailoverAwareMarketDataClientTests.cs
│   │   │   │   ├── Fixtures
│   │   │   │   │   ├── InteractiveBrokers
│   │   │   │   │   │   ├── ib_order_limit_buy_day.json
│   │   │   │   │   │   ├── ib_order_limit_buy_govt_gtc.json
│   │   │   │   │   │   ├── ib_order_limit_sell_fok.json
│   │   │   │   │   │   ├── ib_order_loc_sell_day.json
│   │   │   │   │   │   ├── ib_order_market_buy_bond_day.json
│   │   │   │   │   │   ├── ib_order_market_sell_gtc.json
│   │   │   │   │   │   ├── ib_order_moc_sell_day.json
│   │   │   │   │   │   ├── ib_order_stop_buy_ioc.json
│   │   │   │   │   │   ├── ib_order_stop_limit_buy_day.json
│   │   │   │   │   │   └── ib_order_trailing_stop_sell_gtc.json
│   │   │   │   │   └── Polygon
│   │   │   │   │       ├── polygon-recorded-session-aapl.json
│   │   │   │   │       ├── polygon-recorded-session-auth-failure-rate-limit.json
│   │   │   │   │       ├── polygon-recorded-session-gld-cboe-sell.json
│   │   │   │   │       ├── polygon-recorded-session-msft-edge.json
│   │   │   │   │       ├── polygon-recorded-session-nvda-multi-batch.json
│   │   │   │   │       ├── polygon-recorded-session-spy-etf.json
│   │   │   │   │       └── polygon-recorded-session-tsla-opening-cross.json
│   │   │   │   ├── FreeHistoricalProviderParsingTests.cs
│   │   │   │   ├── FreeProviderContractTests.cs
│   │   │   │   ├── HistoricalDataProviderContractTests.cs
│   │   │   │   ├── IBApiVersionValidatorTests.cs
│   │   │   │   ├── IBBrokerageGatewayTests.cs
│   │   │   │   ├── IBHistoricalProviderContractTests.cs
│   │   │   │   ├── IBMarketDataClientContractTests.cs
│   │   │   │   ├── IBOrderSampleTests.cs
│   │   │   │   ├── IBRuntimeGuidanceTests.cs
│   │   │   │   ├── IBSimulationClientContractTests.cs
│   │   │   │   ├── IBSimulationClientTests.cs
│   │   │   │   ├── MarketDataClientContractTests.cs
│   │   │   │   ├── NYSECredentialAndRateLimitTests.cs
│   │   │   │   ├── NYSEMessageParsingTests.cs
│   │   │   │   ├── NyseMarketDataClientContractTests.cs
│   │   │   │   ├── NyseMarketDataClientTests.cs
│   │   │   │   ├── NyseMessagePipelineTests.cs
│   │   │   │   ├── NyseNationalTradesCsvParserTests.cs
│   │   │   │   ├── NyseSharedLifecycleTests.cs
│   │   │   │   ├── NyseTaqCollectorIntegrationTests.cs
│   │   │   │   ├── PolygonCorporateActionFetcherTests.cs
│   │   │   │   ├── PolygonMarketDataClientTests.cs
│   │   │   │   ├── PolygonMessageParsingTests.cs
│   │   │   │   ├── PolygonProviderContractTests.cs
│   │   │   │   ├── PolygonRecordedSessionReplayTests.cs
│   │   │   │   ├── PolygonSubscriptionTests.cs
│   │   │   │   ├── ProviderBehaviorBuilderTests.cs
│   │   │   │   ├── ProviderFactoryCredentialContextTests.cs
│   │   │   │   ├── ProviderResilienceTests.cs
│   │   │   │   ├── ProviderTemplateFactoryCredentialTests.cs
│   │   │   │   ├── RobinhoodBrokerageGatewayTests.cs
│   │   │   │   ├── RobinhoodHistoricalDataProviderTests.cs
│   │   │   │   ├── RobinhoodMarketDataClientTests.cs
│   │   │   │   ├── RobinhoodReadOnlyBrokerageSyncAdapterTests.cs
│   │   │   │   ├── RobinhoodSymbolSearchProviderTests.cs
│   │   │   │   ├── StreamingFailoverServiceTests.cs
│   │   │   │   ├── SyntheticMarketDataProviderTests.cs
│   │   │   │   ├── SyntheticOptionsChainProviderTests.cs
│   │   │   │   ├── WebSocketProviderBaseTests.cs
│   │   │   │   └── YahooFinanceHistoricalDataProviderTests.cs
│   │   │   ├── Resilience
│   │   │   │   ├── WebSocketConnectionManagerTests.cs
│   │   │   │   └── WebSocketResiliencePolicyTests.cs
│   │   │   └── Shared
│   │   │       ├── SymbolNormalizationTests.cs
│   │   │       └── TempDirectoryFixture.cs
│   │   ├── Integration
│   │   │   ├── ConfigurableTickerDataCollectionTests.cs
│   │   │   ├── ConnectionRetryIntegrationTests.cs
│   │   │   ├── EndpointStubDetectionTests.cs
│   │   │   ├── EndpointTests
│   │   │   │   ├── AccountPortfolioEndpointTests.cs
│   │   │   │   ├── AdminEndpointPermissionTests.cs
│   │   │   │   ├── AuthEndpointTests.cs
│   │   │   │   ├── BackfillEndpointTests.cs
│   │   │   │   ├── CatalogEndpointTests.cs
│   │   │   │   ├── CheckpointEndpointTests.cs
│   │   │   │   ├── ConfigEndpointTests.cs
│   │   │   │   ├── EndpointIntegrationTestBase.cs
│   │   │   │   ├── EndpointMetadataTests.cs
│   │   │   │   ├── EndpointTestCollection.cs
│   │   │   │   ├── EndpointTestFixture.cs
│   │   │   │   ├── EnvironmentDesignerEndpointTests.cs
│   │   │   │   ├── FailoverEndpointTests.cs
│   │   │   │   ├── FundStructureEndpointTests.cs
│   │   │   │   ├── HealthEndpointTests.cs
│   │   │   │   ├── HistoricalEndpointTests.cs
│   │   │   │   ├── IBEndpointTests.cs
│   │   │   │   ├── LeanEndpointTests.cs
│   │   │   │   ├── LiveDataEndpointTests.cs
│   │   │   │   ├── MaintenanceEndpointTests.cs
│   │   │   │   ├── NegativePathEndpointTests.cs
│   │   │   │   ├── OptionsEndpointTests.cs
│   │   │   │   ├── PilotAcceptanceHarnessTests.cs
│   │   │   │   ├── ProviderEndpointTests.cs
│   │   │   │   ├── QualityDropsEndpointTests.cs
│   │   │   │   ├── QualityEndpointContractTests.cs
│   │   │   │   ├── ResponseSchemaSnapshotTests.cs
│   │   │   │   ├── ResponseSchemaValidationTests.cs
│   │   │   │   ├── RiskEndpointTests.cs
│   │   │   │   ├── RoleAuthorizationTests.cs
│   │   │   │   ├── StatusEndpointTests.cs
│   │   │   │   ├── StorageEndpointTests.cs
│   │   │   │   ├── SymbolEndpointTests.cs
│   │   │   │   └── UiEndpointsJsonOptionsTests.cs
│   │   │   ├── FixtureProviderTests.cs
│   │   │   ├── GracefulShutdownIntegrationTests.cs
│   │   │   └── YahooFinancePcgPreferredIntegrationTests.cs
│   │   ├── Ledger
│   │   │   └── LedgerIntegrationTests.cs
│   │   ├── Meridian.Tests.csproj
│   │   ├── MoneyMarketFunds
│   │   │   └── MoneyMarketFundProjectionServiceTests.cs
│   │   ├── Options
│   │   │   └── OptionProjectionServiceTests.cs
│   │   ├── Performance
│   │   │   └── AllocationBudgetIntegrationTests.cs
│   │   ├── ProviderSdk
│   │   │   ├── AttributeCredentialResolverTests.cs
│   │   │   ├── CredentialValidatorTests.cs
│   │   │   ├── DataSourceAttributeTests.cs
│   │   │   ├── DataSourceRegistryTests.cs
│   │   │   ├── ExceptionTypeTests.cs
│   │   │   └── ProviderModuleLoaderTests.cs
│   │   ├── Reconciliation
│   │   │   ├── ReconciliationCaseServiceTests.cs
│   │   │   └── StatementImportAndMatchingTests.cs
│   │   ├── Risk
│   │   │   ├── CompositeRiskValidatorTests.cs
│   │   │   ├── DrawdownCircuitBreakerTests.cs
│   │   │   ├── OrderRateThrottleTests.cs
│   │   │   ├── PositionLimitRuleTests.cs
│   │   │   └── RiskIntegrationTests.cs
│   │   ├── SecurityMaster
│   │   │   ├── SecurityEnrichmentTests.cs
│   │   │   ├── SecurityMasterAggregateRebuilderTests.cs
│   │   │   ├── SecurityMasterAssetClassSupportTests.cs
│   │   │   ├── SecurityMasterConflictServiceTests.cs
│   │   │   ├── SecurityMasterConvertibleEquityAmendmentTests.cs
│   │   │   ├── SecurityMasterDatabaseFactAttribute.cs
│   │   │   ├── SecurityMasterDatabaseFixture.cs
│   │   │   ├── SecurityMasterImportServiceTests.cs
│   │   │   ├── SecurityMasterLedgerBridgeTests.cs
│   │   │   ├── SecurityMasterMigrationRunnerTests.cs
│   │   │   ├── SecurityMasterPostgresRoundTripTests.cs
│   │   │   ├── SecurityMasterPreferredEquityAmendmentTests.cs
│   │   │   ├── SecurityMasterProjectionServiceSnapshotTests.cs
│   │   │   ├── SecurityMasterQueryServiceEquityTermsTests.cs
│   │   │   ├── SecurityMasterRebuildOrchestratorTests.cs
│   │   │   ├── SecurityMasterReferenceLookupTests.cs
│   │   │   ├── SecurityMasterServiceSnapshotTests.cs
│   │   │   └── SecurityMasterSnapshotStoreTests.cs
│   │   ├── Serialization
│   │   │   └── HighPerformanceJsonTests.cs
│   │   ├── StatementReconciliationServiceTests.cs
│   │   ├── Storage
│   │   │   ├── AnalysisExportServiceTests.cs
│   │   │   ├── AtomicFileWriterTests.cs
│   │   │   ├── CanonicalSymbolRegistryTests.cs
│   │   │   ├── CompositeSinkTests.cs
│   │   │   ├── DataLineageServiceTests.cs
│   │   │   ├── DataQualityScoringServiceTests.cs
│   │   │   ├── DataValidatorTests.cs
│   │   │   ├── EventBufferTests.cs
│   │   │   ├── ExportValidatorTests.cs
│   │   │   ├── FilePermissionsServiceTests.cs
│   │   │   ├── JsonlBatchWriteTests.cs
│   │   │   ├── JsonlReplayerTests.cs
│   │   │   ├── LedgerBookServiceTests.cs
│   │   │   ├── LedgerJournalStoreTests.cs
│   │   │   ├── LifecyclePolicyEngineTests.cs
│   │   │   ├── MaintenancePersistenceTests.cs
│   │   │   ├── MemoryMappedJsonlReaderTests.cs
│   │   │   ├── MetadataTagServiceTests.cs
│   │   │   ├── ParquetConversionServiceTests.cs
│   │   │   ├── ParquetStorageSinkTests.cs
│   │   │   ├── PortableDataPackagerTests.cs
│   │   │   ├── PositionSnapshotStoreTests.cs
│   │   │   ├── QuotaEnforcementServiceTests.cs
│   │   │   ├── SourceRegistryPersistenceTests.cs
│   │   │   ├── StorageCatalogServiceTests.cs
│   │   │   ├── StorageChecksumServiceTests.cs
│   │   │   ├── StorageOptionsDefaultsTests.cs
│   │   │   ├── StorageSinkRegistryTests.cs
│   │   │   ├── SymbolRegistryServiceTests.cs
│   │   │   ├── WriteAheadLogCorruptionModeTests.cs
│   │   │   ├── WriteAheadLogFuzzTests.cs
│   │   │   └── WriteAheadLogTests.cs
│   │   ├── Strategies
│   │   │   ├── AggregatePortfolioServiceTests.cs
│   │   │   ├── CashFlowProjectionTests.cs
│   │   │   ├── CoveredCall
│   │   │   │   ├── CoveredCallChainProviderAdapterTests.cs
│   │   │   │   ├── CoveredCallChainProviderFactoryConvertCallsTests.cs
│   │   │   │   └── CoveredCallRunProjectionTests.cs
│   │   │   ├── LedgerReadServiceTests.cs
│   │   │   ├── PortfolioReadServiceTests.cs
│   │   │   ├── PromotionServiceLiveGovernanceTests.cs
│   │   │   ├── PromotionServiceTests.cs
│   │   │   ├── ReconciliationProjectionServiceTests.cs
│   │   │   ├── StrategyDesignRepositoryTests.cs
│   │   │   ├── StrategyDesignServiceTests.cs
│   │   │   ├── StrategyLifecycleManagerTests.cs
│   │   │   ├── StrategyRunContinuityServiceTests.cs
│   │   │   ├── StrategyRunDrillInTests.cs
│   │   │   ├── StrategyRunReadServiceTests.cs
│   │   │   └── StrategyRunStoreTests.cs
│   │   ├── SymbolSearch
│   │   │   ├── OpenFigiClientTests.cs
│   │   │   └── SymbolSearchServiceTests.cs
│   │   ├── TestCollections.cs
│   │   ├── TestData
│   │   │   └── Golden
│   │   │       └── alpaca-quote-pipeline.json
│   │   ├── TestHelpers
│   │   │   ├── Builders
│   │   │   │   ├── BacktestRequestBuilder.cs
│   │   │   │   ├── HistoricalBarBuilder.cs
│   │   │   │   ├── MarketEventBuilder.cs
│   │   │   │   ├── SecurityBuilder.cs
│   │   │   │   └── TradeBuilder.cs
│   │   │   ├── MarketScenarioBuilder.cs
│   │   │   ├── PolygonStubClient.cs
│   │   │   ├── StubHttpMessageHandler.cs
│   │   │   └── TestMarketEventPublisher.cs
│   │   ├── Treasury
│   │   │   ├── MmfFamilyNormalizationTests.cs
│   │   │   ├── MmfLiquidityServiceTests.cs
│   │   │   ├── MmfRebuildTests.cs
│   │   │   └── MoneyMarketFundServiceTests.cs
│   │   └── Ui
│   │       ├── AlpacaBrokerageConnectionServiceTests.cs
│   │       ├── BondReferenceEndpointsTests.cs
│   │       ├── BrokerageConnectionEndpointsTests.cs
│   │       ├── BrokeragePortfolioSyncServiceTests.cs
│   │       ├── DirectLendingEndpointsTests.cs
│   │       ├── EdgarReferenceDataEndpointsTests.cs
│   │       ├── EvidenceWorkflowFabricTests.cs
│   │       ├── ExecutionGovernanceEndpointsTests.cs
│   │       ├── ExecutionWriteEndpointsTests.cs
│   │       ├── ExportEndpointsTests.cs
│   │       ├── OptionReferenceEndpointsRoundtripTests.cs
│   │       ├── ProviderConnectionEndpointsTests.cs
│   │       ├── RiskEndpointsTests.cs
│   │       ├── SecurityMasterConvertibleEquityEndpointsTests.cs
│   │       ├── SecurityMasterIngestStatusEndpointsTests.cs
│   │       ├── SecurityMasterPreferredEquityEndpointsTests.cs
│   │       ├── StrategyDesignerWorkstationEndpointsTests.cs
│   │       ├── TradingOperatorReadinessServiceTests.cs
│   │       ├── Wave2OperatorInboxAcceptanceTests.cs
│   │       ├── Wave2PaperTradingCockpitAcceptanceTests.cs
│   │       ├── WorkflowLibraryEndpointTests.cs
│   │       └── WorkstationEndpointsTests.cs
│   ├── Meridian.Ui.Tests
│   │   ├── Collections
│   │   │   ├── BoundedObservableCollectionTests.cs
│   │   │   └── CircularBufferTests.cs
│   │   ├── Meridian.Ui.Tests.csproj
│   │   ├── README.md
│   │   └── Services
│   │       ├── ActivityFeedServiceTests.cs
│   │       ├── AlertServiceTests.cs
│   │       ├── AnalysisExportServiceBaseTests.cs
│   │       ├── AnalysisExportWizardServiceTests.cs
│   │       ├── ApiClientServiceTests.cs
│   │       ├── ArchiveBrowserServiceTests.cs
│   │       ├── AtomicPersistenceServiceTests.cs
│   │       ├── BackendServiceManagerBaseTests.cs
│   │       ├── BackfillApiServiceTests.cs
│   │       ├── BackfillCheckpointServiceTests.cs
│   │       ├── BackfillProviderConfigServiceTests.cs
│   │       ├── BackfillServiceTests.cs
│   │       ├── ChartingServiceTests.cs
│   │       ├── CollectionSessionServiceTests.cs
│   │       ├── CommandPaletteServiceTests.cs
│   │       ├── ConfigServiceBaseTests.cs
│   │       ├── ConfigServiceTests.cs
│   │       ├── ConnectionServiceBaseTests.cs
│   │       ├── CredentialServiceTests.cs
│   │       ├── DataCalendarServiceTests.cs
│   │       ├── DataCompletenessServiceTests.cs
│   │       ├── DataQualityRefreshCoordinatorTests.cs
│   │       ├── DataQualityServiceBaseTests.cs
│   │       ├── DataSamplingServiceTests.cs
│   │       ├── DiagnosticsServiceTests.cs
│   │       ├── ErrorHandlingServiceTests.cs
│   │       ├── EventReplayServiceTests.cs
│   │       ├── FixtureDataServiceTests.cs
│   │       ├── FixtureModeDetectorTests.cs
│   │       ├── FormValidationServiceTests.cs
│   │       ├── IntegrityEventsServiceTests.cs
│   │       ├── LeanIntegrationServiceTests.cs
│   │       ├── LiveDataServiceTests.cs
│   │       ├── LoggingServiceBaseTests.cs
│   │       ├── ManifestServiceTests.cs
│   │       ├── NotificationServiceBaseTests.cs
│   │       ├── NotificationServiceTests.cs
│   │       ├── OrderBookVisualizationServiceTests.cs
│   │       ├── PortfolioImportServiceTests.cs
│   │       ├── ProviderHealthServiceTests.cs
│   │       ├── ProviderManagementServiceTests.cs
│   │       ├── ScheduleManagerServiceTests.cs
│   │       ├── ScheduledMaintenanceServiceTests.cs
│   │       ├── SchemaServiceTests.cs
│   │       ├── SearchServiceTests.cs
│   │       ├── SettingsConfigurationServiceTests.cs
│   │       ├── SmartRecommendationsServiceTests.cs
│   │       ├── StatusServiceBaseTests.cs
│   │       ├── StorageAnalyticsServiceTests.cs
│   │       ├── SymbolGroupServiceTests.cs
│   │       ├── SymbolManagementServiceTests.cs
│   │       ├── SymbolMappingServiceTests.cs
│   │       ├── SystemHealthServiceTests.cs
│   │       ├── TestSupport
│   │       │   └── FixedConfigService.cs
│   │       ├── TimeSeriesAlignmentServiceTests.cs
│   │       ├── WatchlistServiceCollection.cs
│   │       └── WatchlistServiceTests.cs
│   ├── Meridian.Wpf.Tests
│   │   ├── Copy
│   │   │   └── WorkspaceCopyCatalogTests.cs
│   │   ├── Features
│   │   │   └── Data
│   │   │       └── Shell
│   │   │           └── DataWorkspaceShellViewModelTests.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.Wpf.Tests.csproj
│   │   ├── Models
│   │   │   └── ShellNavigationCatalogTests.cs
│   │   ├── Services
│   │   │   ├── AdminMaintenanceServiceTests.cs
│   │   │   ├── AppServiceRegistrationTests.cs
│   │   │   ├── BackendServiceManagerTests.cs
│   │   │   ├── BackgroundTaskSchedulerServiceTests.cs
│   │   │   ├── ConfigServiceTests.cs
│   │   │   ├── ConnectionServiceTests.cs
│   │   │   ├── DataOperationsWorkspacePresentationBuilderTests.cs
│   │   │   ├── ExportPresetServiceTests.cs
│   │   │   ├── FirstRunServiceTests.cs
│   │   │   ├── FundLedgerReadServiceTests.cs
│   │   │   ├── FundReconciliationWorkbenchServiceTests.cs
│   │   │   ├── InfoBarServiceTests.cs
│   │   │   ├── KeyboardShortcutServiceTests.cs
│   │   │   ├── MessagingServiceTests.cs
│   │   │   ├── NavigationServiceTests.cs
│   │   │   ├── NotificationServiceTests.cs
│   │   │   ├── OfflineTrackingPersistenceServiceTests.cs
│   │   │   ├── PendingOperationsQueueServiceTests.cs
│   │   │   ├── QuantScriptExecutionHistoryServiceTests.cs
│   │   │   ├── QuantScriptTemplateCatalogServiceTests.cs
│   │   │   ├── ResearchBriefingWorkspaceServiceTests.cs
│   │   │   ├── RetentionAssuranceServiceTests.cs
│   │   │   ├── RunMatServiceTests.cs
│   │   │   ├── SingleInstanceServiceTests.cs
│   │   │   ├── StatusServiceTests.cs
│   │   │   ├── StorageServiceTests.cs
│   │   │   ├── StrategyRunWorkspaceServiceTests.cs
│   │   │   ├── TooltipServiceTests.cs
│   │   │   ├── WatchlistServiceTests.cs
│   │   │   ├── WorkspaceServiceTests.cs
│   │   │   ├── WorkspaceShellContextServiceTests.cs
│   │   │   ├── WorkstationOperatingContextServiceTests.cs
│   │   │   └── WorkstationWorkflowSummaryServiceTests.cs
│   │   ├── Shell
│   │   │   ├── PageContentFactoryTests.cs
│   │   │   ├── PaneHostViewModelTests.cs
│   │   │   ├── ShellNavigationCoordinatorTests.cs
│   │   │   └── ShellRouteRegistryTests.cs
│   │   ├── Support
│   │   │   ├── FakeQuantScriptCompiler.cs
│   │   │   ├── FakeScriptRunner.cs
│   │   │   ├── FakeWorkstationReconciliationApiClient.cs
│   │   │   ├── FakeWorkstationResearchBriefingApiClient.cs
│   │   │   ├── MainPageUiAutomationFacade.cs
│   │   │   ├── NavigationHostInspector.cs
│   │   │   ├── RunMatUiAutomationFacade.cs
│   │   │   ├── RunMatUiAutomationFacadeTests.cs
│   │   │   ├── StrategyRunWorkspaceTestData.cs
│   │   │   └── WpfTestThread.cs
│   │   ├── TestAssemblyConfiguration.cs
│   │   ├── ViewModels
│   │   │   ├── AccountPortfolioViewModelTests.cs
│   │   │   ├── ActivityLogViewModelTests.cs
│   │   │   ├── AddProviderWizardViewModelTests.cs
│   │   │   ├── AdminMaintenanceViewModelTests.cs
│   │   │   ├── AdvancedAnalyticsViewModelTests.cs
│   │   │   ├── AgentViewModelTests.cs
│   │   │   ├── AggregatePortfolioViewModelTests.cs
│   │   │   ├── AnalysisExportViewModelTests.cs
│   │   │   ├── AnalysisExportWizardViewModelTests.cs
│   │   │   ├── BackfillViewModelTests.cs
│   │   │   ├── BatchBacktestViewModelTests.cs
│   │   │   ├── CashFlowViewModelTests.cs
│   │   │   ├── ChartingPageViewModelTests.cs
│   │   │   ├── CollectionSessionViewModelTests.cs
│   │   │   ├── DataBrowserViewModelTests.cs
│   │   │   ├── DataExportViewModelTests.cs
│   │   │   ├── DataQualityViewModelCharacterizationTests.cs
│   │   │   ├── DataSamplingViewModelTests.cs
│   │   │   ├── DataSourcesViewModelTests.cs
│   │   │   ├── ExportPresetsViewModelTests.cs
│   │   │   ├── FundAccountsViewModelTests.cs
│   │   │   ├── FundLedgerViewModelTests.cs
│   │   │   ├── MainShellViewModelTests.cs
│   │   │   ├── MessagingHubViewModelTests.cs
│   │   │   ├── NotificationCenterViewModelTests.cs
│   │   │   ├── OrderBookViewModelTests.cs
│   │   │   ├── PortfolioImportViewModelTests.cs
│   │   │   ├── PositionBlotterViewModelTests.cs
│   │   │   ├── ProviderHealthViewModelTests.cs
│   │   │   ├── QuantScriptViewModelTests.cs
│   │   │   ├── ResearchWorkspaceShellViewModelTests.cs
│   │   │   ├── RetentionAssuranceViewModelTests.cs
│   │   │   ├── RunMatViewModelTests.cs
│   │   │   ├── ScheduleManagerViewModelTests.cs
│   │   │   ├── SecurityMasterViewModelTests.cs
│   │   │   ├── ShellPresentationViewModelTests.cs
│   │   │   ├── StatusBarViewModelTests.cs
│   │   │   ├── StorageViewModelTests.cs
│   │   │   ├── StrategyRunBrowserViewModelTests.cs
│   │   │   ├── StrategyRunLedgerViewModelTests.cs
│   │   │   ├── StrategyRunPortfolioViewModelTests.cs
│   │   │   ├── SymbolMappingViewModelTests.cs
│   │   │   ├── SymbolsPageViewModelTests.cs
│   │   │   ├── SystemHealthViewModelTests.cs
│   │   │   ├── TimeSeriesAlignmentViewModelTests.cs
│   │   │   ├── TradingHoursViewModelTests.cs
│   │   │   ├── TradingWorkspaceShellViewModelTests.cs
│   │   │   ├── WatchlistViewModelTests.cs
│   │   │   ├── WelcomePageViewModelTests.cs
│   │   │   ├── WorkflowLibraryViewModelTests.cs
│   │   │   └── WorkspacePageViewModelTests.cs
│   │   └── Views
│   │       ├── DashboardPageSmokeTests.cs
│   │       ├── DataOperationsWorkspaceShellSmokeTests.cs
│   │       ├── DataQualityPageSmokeTests.cs
│   │       ├── DesktopWorkflowScriptTests.cs
│   │       ├── FullNavigationSweepTests.cs
│   │       ├── FundProfileSelectionPageSmokeTests.cs
│   │       ├── GovernanceWorkspaceShellPageTests.cs
│   │       ├── GovernanceWorkspaceShellSmokeTests.cs
│   │       ├── MainPageSmokeTests.cs
│   │       ├── MainPageUiWorkflowTests.cs
│   │       ├── NavigationPageSmokeTests.cs
│   │       ├── PageLifecycleCleanupTests.cs
│   │       ├── PlotRenderBehaviorTests.cs
│   │       ├── QuantScriptPageTests.cs
│   │       ├── ResearchWorkspaceShellPageTests.cs
│   │       ├── ResearchWorkspaceShellSmokeTests.cs
│   │       ├── ResearchWorkspaceShellWorkflowTests.cs
│   │       ├── RunMatUiSmokeTests.cs
│   │       ├── RunMatWorkflowSmokeTests.cs
│   │       ├── SplitPaneHostControlTests.cs
│   │       ├── SystemHealthPageSmokeTests.cs
│   │       ├── TradingWorkspaceShellPageTests.cs
│   │       ├── WorkspaceDeepPageChromeTests.cs
│   │       ├── WorkspaceQueueToneStylesTests.cs
│   │       ├── WorkspaceShellContextStripControlTests.cs
│   │       ├── WorkspaceShellPageSmokeTests.cs
│   │       └── WorkstationPageSmokeTests.cs
│   ├── coverlet.runsettings
│   ├── scripts
│   │   ├── setup-verification.sh
│   │   ├── test_artifact_retention_module.py
│   │   ├── test_buildctl_artifact_retention.py
│   │   ├── test_central_package_versions.py
│   │   ├── test_check_contract_compatibility_gate.py
│   │   ├── test_check_program_state_consistency.py
│   │   ├── test_check_workflow_docs_parity.py
│   │   ├── test_cleanup_generated_script.py
│   │   ├── test_code_quality_workflow.py
│   │   ├── test_compare_run_contract.py
│   │   ├── test_dashboard_package_lock.py
│   │   ├── test_documentation_workflow.py
│   │   ├── test_export_project_artifact_workflow.py
│   │   ├── test_generate_contract_review_packet.py
│   │   ├── test_generate_dk1_pilot_parity_packet.py
│   │   ├── test_generate_program_state_summary.py
│   │   ├── test_golden_path_validation_workflow.py
│   │   ├── test_maintenance_full_workflow.py
│   │   ├── test_meridian_code_review_run_eval.py
│   │   ├── test_prepare_dk1_operator_signoff.py
│   │   ├── test_project_target_framework_alignment.py
│   │   ├── test_python_package_conda_dependencies.py
│   │   ├── test_refresh_screenshots_workflow.py
│   │   ├── test_screenshot_diff_report.py
│   │   ├── test_screenshot_workflow_plan.py
│   │   ├── test_setup_dotnet_cache_action.py
│   │   ├── test_shared_build_retention.py
│   │   ├── test_web_workstation_installer.py
│   │   ├── test_wpf_msix_install_guidance.py
│   │   └── test_wpf_msix_manifest.py
│   ├── setup-script-tests.md
│   └── xunit.runner.json
├── tree.bak
└── wwwroot
    └── workstation
        ├── assets
        │   ├── activity-CtHpOVKp.js
        │   ├── activity-DdzC2NHl.js
        │   ├── arrow-right-BQMXU4Qj.js
        │   ├── arrow-right-DVsZxtAC.js
        │   ├── briefcase-business-CD4M7um3.js
        │   ├── briefcase-business-TKfvxiVx.js
        │   ├── circle-alert-D7rSODdk.js
        │   ├── circle-alert-HHMNSH16.js
        │   ├── circle-check-DPF4ogK2.js
        │   ├── circle-check-eX-lGLCP.js
        │   ├── circle-x-CMg0Qc3V.js
        │   ├── circle-x-DY2RQmFF.js
        │   ├── clipboard-list-DAEDYX46.js
        │   ├── clipboard-list-jTN-MPwF.js
        │   ├── data-operations-screen-B3QBwTVq.js
        │   ├── data-operations-screen-D0lJrkiA.js
        │   ├── dialog-CinaMm0v.js
        │   ├── dialog-DSdabj2S.js
        │   ├── evidence-workbench-screen-ClQx7gzd.js
        │   ├── evidence-workbench-screen-eYN2-zbn.js
        │   ├── external-link-BZXdW9W9.js
        │   ├── external-link-Grqr3LkX.js
        │   ├── file-text-BlxlUqHd.js
        │   ├── file-text-N-hievbj.js
        │   ├── governance-screen-DqY6ddBm.js
        │   ├── governance-screen-DrFPMbFb.js
        │   ├── index-C40I5Ro9.js
        │   ├── index-Ch7FNJl9.js
        │   ├── index-DRTDTLoU.css
        │   ├── index-aS_m2ekp.css
        │   ├── input-Cy4wm2Ml.js
        │   ├── input-DH1caSqT.js
        │   ├── live-quotes-screen--RkhoaOE.js
        │   ├── live-quotes-screen-D1hu0dqT.js
        │   ├── metric-card-6FGqr-9F.js
        │   ├── metric-card-BbZrlC_6.js
        │   ├── network-1U1oL-WT.js
        │   ├── network-CwsNwANB.js
        │   ├── operator-readiness-console-BRtzgXL5.js
        │   ├── operator-readiness-console-BrBqInOx.js
        │   ├── overview-screen-BR5AraLj.js
        │   ├── overview-screen-BeJxfrwP.js
        │   ├── plus-Cw2iQHnA.js
        │   ├── plus-DSjajpe-.js
        │   ├── portfolio-screen-CSEy6zr5.js
        │   ├── portfolio-screen-mIlILUNM.js
        │   ├── price-alerts-screen-COXY9m6l.js
        │   ├── price-alerts-screen-njljCYiX.js
        │   ├── quant-lab-screen-BOhDQUug.js
        │   ├── quant-lab-screen-Dpy7KWIY.js
        │   ├── refresh-cw-DbBLAiPQ.js
        │   ├── refresh-cw-Dp5Clkb9.js
        │   ├── reporting-screen-BliDtXaE.js
        │   ├── reporting-screen-nAqRD7ij.js
        │   ├── research-screen-Df8dFRgK.js
        │   ├── research-screen-DwSDHbyl.js
        │   ├── rotate-ccw-9wKbnWAt.js
        │   ├── rotate-ccw-DOv9SmtZ.js
        │   ├── select-CoGQYu8S.js
        │   ├── select-Dydh4qce.js
        │   ├── settings-screen-DpB0U5fm.js
        │   ├── settings-screen-paK4sHDZ.js
        │   ├── sparkles-C72l2Ug0.js
        │   ├── sparkles-CPzvLarX.js
        │   ├── strategy-designer-screen-DDl7I7Ey.js
        │   ├── trading-screen-CEO2TrpK.js
        │   ├── trading-screen-DJVhwdT7.js
        │   ├── trash-2-Br6Kw89A.js
        │   ├── trash-2-DG_-jE3C.js
        │   ├── trending-up-CyiwhFqC.js
        │   ├── trending-up-Dm0UHjUd.js
        │   ├── ui-kit-primitives-1LSkXU-q.js
        │   ├── ui-kit-primitives-HLPUYoNX.js
        │   ├── wallet-SSwWmFz-.js
        │   ├── wallet-gTxB9uZO.js
        │   ├── watchlist-screen-Z-X12prT.js
        │   └── watchlist-screen-lIteaLqr.js
        └── index.html

780 directories, 4541 files
```
<!-- readme-tree end -->
