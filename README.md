# Meridian

Meridian is a .NET 10 operational-finance and trading-platform codebase in active delivery. It is built for financial operations teams that need to import data, validate it, reconcile it, approve decisions, retain evidence, and publish governed reports without losing the chain of proof.

The core product promise is simple:

> Meridian does not just show the number. Meridian proves the number.

For an end user, Meridian is intended to reduce disconnected spreadsheets, inbox handoffs, opaque reconciliations, and hard-to-audit report preparation. The system keeps source evidence, workflow state, approvals, ledger impact, reconciliation history, report provenance, and audit trails connected so an operator can answer:

- What happened?
- Can it be trusted?
- What still needs review, approval, reconciliation, evidence, or reporting?
- Which report, ledger entry, close blocker, or operational decision depends on it?

## Get Meridian Running

**There is no downloadable installer yet.** No release has been published, so the only way to run
Meridian today is from source. The desktop installer lane exists and builds signed MSIX and
one-click setup artifacts, but publishing a release needs a code-signing certificate that is not in
the repository; until then, use the path below.

### See it working in one command

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --seed-demo
```

This provisions a durable, clearly-labelled **Seeded** demo workspace and opens the browser
workstation on it, so the first screen is populated — reconciliation casework in the control tower
and a completed paper strategy run on the Strategy desk — rather than an empty shell. Demo data
lives in its own root (`{dataRoot}/demo-workspace`), never mixes with real data, carries `Seeded`
provenance on every record, and is removed by `--reset-demo`. Re-open it later with `--demo`.

You need the .NET SDK pinned by [global.json](global.json) and Git. Node.js is **not** required to
see the workstation: the browser bundle is tracked in the repository.

### Running against your own data

`--seed-demo` and `--demo` configure a database-less local profile for you. Any other launch —
including `--mode workstation` — requires you to choose a persistence profile first, and fails
closed with a diagnostic naming the variable if you have not:

```bash
# Real local persistence (recommended): point Meridian at a PostgreSQL instance
export MERIDIAN_DATABASE_URL=postgres://user:password@localhost:5432/meridian
dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080

# Or, for local/dev fixture scenarios only, file-backed governance stores
export MERIDIAN_USE_INMEMORY_GOVERNANCE=true
```

Authentication defaults to required outside Development, so plan credentials before a
non-demo launch. See [Start Here](docs/start/README.md) for the full setup path and
[environment variables](docs/reference/environment-variables.md) for the complete list.

### What is supported today

The v1 envelope in [ADR-019](docs/adr/019-production-support-matrix-and-deployment-posture.md) is a
**single-operator, single-company, single-node local workstation** on Windows 11 x64. Container
(`deploy/docker/`, `deploy/k8s/`), systemd, remote-hosted `ProductionApi`, and multi-node
topologies are **experimental and fail closed** — they are not a supported way to run Meridian, and
the installer offers Docker only on that basis. Meridian is not production-certified: the release
gate in the
[Implementation and Readiness Tracker](docs/product/implementation-todo-list.md) requires every
`P0` row to be complete on one release commit, and that has not happened yet.

## Current Product Status

Meridian's current baseline is the closed W1-W5 operational record plus completed W5X shared
explorer, Financial Operations, statement connector, and bounded W7 live-readiness governance
milestones. Evidence Vault productization, statement reconciliation onboarding, browser workstation
screen consolidation, WPF workstation parity, and the Asset Accounting Event Spine are active
delivery work, and the ranked W9 first-order improvement slate (`W9-TRUTH-001` through
`W9-INGEST-009`) is the accepted planned priority order for the next delivery decisions.

These are bounded roadmap acceptance claims, not blanket production certification. A completed row
means its named scope closed with linked evidence; release and support decisions still require fresh
operator preflight, packaging, deployment, and required GitHub Actions evidence. Production
readiness is tracked in the canonical
[Implementation and Readiness Tracker](docs/product/implementation-todo-list.md), whose release gate
requires every P0 row to be complete on the same release commit.

That means the repository contains accepted evidence for:

| Capability | Current status |
| --- | --- |
| Data confidence and provider validation | Complete baseline. Provider trust gates, source evidence, validation packets, setup orchestration, and data-quality checks are part of the W1 evidence record. |
| Paper trading and research continuity | Complete baseline. Research-to-paper handoff, paper-session readiness, promotion evidence, and operator acceptance are supported before live promotion is allowed. |
| Portfolio, accounting, and reconciliation workflows | Complete baseline. Portfolio ledger reconciliation, accounting record summaries, retained source evidence, reconciliation cases, ledger evidence, approvals, and report-pack lineage are connected through shared contracts and read models. |
| Governed reporting | Complete baseline. Report-pack lifecycle, approval evidence, export provenance, restatement posture, report-line provenance, and evidence retention are represented in shared services and workstation surfaces. |
| Multi-asset operational coverage | Complete bounded proof lane. Security Master and asset operations cover the accepted public and private/structured asset-class baseline through provider evidence, identifier confidence, terms, obligations, ledger classification, reconciliation signals, and close blockers. Broader provider and lifecycle depth remains separate work. |
| Shared Financial Record Explorers | Complete W5X milestone. Ledger, Portfolio, Security & Instrument, and Report-Line Provenance explorers share contracts, saved views, dense grids, proof ribbons, record drawers, evidence links, approval state, reconciliation state, report usage, and audit timelines. |
| Financial Operations control center | Complete W5X milestone. Operations Continuity and Fund Ledger surfaces expose reconciliation queues, exception casework, close readiness, workflow controls, approval policy, audit evidence, checklist state, and governed reopen posture. |
| Statement connector library | Complete W5X milestone. Declarative CSV and OFX mapping profiles, IB Flex XML, OFX bank/investment files, and Alpaca activity/portfolio statements normalize into canonical statement records with preview, confidence, drift warning, and reconciliation handoff support. |
| Evidence Vault and statement onboarding | In progress. Evidence Vault primitives exist for retained source documents, manifests, review state, object links, and audit trails; the first acceptance path is browser-first statement reconciliation onboarding. |
| Browser and WPF workstation delivery | In progress. Both operator clients are active, co-equal product lanes over shared contracts and read models. `W8-UX-CONSOL-001` folds closely related browser screens into deeper host screens behind the seven root workspaces, and `W8-WPF-PARITY-001` tracks browser-first screens that still need a WPF equivalent or an explicit parity decision. |
| Live-readiness governance | Complete bounded W7 milestone. Paper-to-live promotion requires trusted-data review, paper-validation evidence, reconciliation evidence, accounting-record evidence, governed-reporting evidence, governance sign-off, exception-handling evidence, rollback/kill-switch evidence, audit retention, live brokerage enablement, and an active manual override. This is governance, not full live execution productization. |
| Asset accounting event spine | Complete W9 milestone. `W9-ASSET-010` established one evidence-backed Asset Accounting Event Spine across acquisition, capitalization, valuation, income, corporate action, impairment, depreciation/amortization, and disposal. All eight kinds resolve Security Master, position, ledger, period, basis, rule-pack, lineage, and retained-evidence authority before drafting; lifecycle states never collapse; and acquisition lot creation plus versioned selected-lot disposal join the governed journal append in one serializable transaction. |

Active operator UI work spans both:

- Windows desktop workstation: `src/Meridian.Wpf/`
- Browser workstation: `src/Meridian.Ui/dashboard/`, built into `src/Meridian.Ui/wwwroot/workstation/`

Both clients should stay thin over shared contracts, shared API endpoints, and shared read models in `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/`.

Visible root operator navigation remains limited to:

```text
Trading, Portfolio, Accounting, Reporting, Strategy, Data, Settings
```

## End-User Value

Meridian is valuable when a financial operations user needs to move from fragmented records to a governed operational answer.

It helps the user:

- import provider, broker, custodian, statement, or file evidence while retaining the original source;
- validate mappings, freshness, provider confidence, and data quality before downstream use;
- reconcile positions, transactions, balances, capital activity, and accounting records;
- investigate exceptions with owner, SLA, materiality, blocked-output, evidence, and approval context;
- keep ledger, capital-account, close, and report impacts tied to the same proof trail;
- approve, reopen, restate, publish, or block workflows based on explicit policy and retained evidence;
- produce report packs and exports that can be traced back to source records, approvals, and audit history;
- use browser or WPF workstations without duplicating business rules in each client.

The near-term product wedge is a Close, Data, and Evidence Control Tower: a finance operations layer that sits above spreadsheets, custodians, brokers, administrators, portfolio systems, general ledgers, banks, and document stores so users can close faster and verify every reported number.

## Future Plans

The roadmap remains evidence-led. A future capability is treated as complete only when the roadmap registry, generated roadmap evidence, source/test artifacts, and operator documentation agree.

Near-term work:

| Plan | Direction |
| --- | --- |
| Evidence Vault productization | Turn retained documents, source manifests, request lists, extracted-field review, object links, and audit primitives into a reusable evidence layer for Accounting, Reporting, and Data workflows. |
| Statement reconciliation onboarding | Make statement import the first browser-first onboarding path: import, preview, commit, retain Evidence Vault proof, route reconciliation breaks, and guide the operator to next actions. |
| WPF workstation parity | Close tracked browser-first screen gaps through shared DTOs, endpoints, and read models while preserving desktop MVVM, validation, and release workflows. |
| Browser workstation screen consolidation | Fold closely related browser workstation screens into deeper host screens behind the seven root workspaces (`W8-UX-CONSOL-001`), keeping retired routes as scope-preserving redirects and refreshing the WPF parity matrix with each fold. |
| Ranked W9 improvement slate | Execute the accepted `W9-TRUTH-001` through `W9-INGEST-009` priority order: truthful simulation posture, seeded demo evaluation, paper-trading realism, live fill streaming, client-grade reporting, fund economics, execution safety, governance hardening, and institutional statement ingestion. The completed `W9-ASSET-010` spine (retained immutable journals as the only Posted authority) is the accounting baseline these lanes build on. |

Longer-term or explicitly deferred lanes:

- broader live execution productization and live portfolio operations beyond bounded W7 governance;
- Backtesting Studio (`W6-BTSTUDIO-001`) remains planned, with broader research-workbench expansion deferred;
- full treasury payment execution and bank-release automation;
- full alternative asset operations beyond the current multi-asset proof lane;
- forecasting and scenario engines;
- enterprise risk platform;
- client portal and stakeholder self-service workflows;
- no-code workflow designer and policy compiler;
- broad reporting/analytics platform expansion beyond the governed report-pack baseline;
- mobile applications, which remain out of scope unless the roadmap explicitly reopens that lane.

## Start Here

Use the documentation front door and audience paths for current guidance:

- [Documentation Index](docs/README.md)
- [Start Here](docs/start/README.md)
- [Product Direction](docs/product/README.md)
- [Design Charter](docs/product/meridian-design-document.md)
- [Implementation and Readiness Tracker](docs/product/implementation-todo-list.md)
- [Engineering Guide](docs/engineering/README.md)
- [Operator Guide](docs/operators/README.md)
- [Reference Lookup](docs/reference/README.md)
- [Roadmap Registry](docs/roadmap/README.md)
- [Generated Roadmap Summary](docs/roadmap/generated/ROADMAP_SUMMARY.md)
- [Source Documentation Mesh](docs/source/README.md)
- [AI Guide](docs/ai/README.md)
- [Documentation Ownership Contract](docs/documentation-ownership.md)

## Repository Map

Major solution areas include:

| Area | Paths |
| --- | --- |
| Host, application, domain, contracts, infrastructure, and storage | `src/Meridian`, `src/Meridian.Application`, `src/Meridian.Domain`, `src/Meridian.Core`, `src/Meridian.Contracts`, `src/Meridian.Infrastructure`, `src/Meridian.Storage` |
| Bounded-context domain modules | `src/Meridian.Entities`, `src/Meridian.Identity`, `src/Meridian.Instruments`, `src/Meridian.PortfolioRecords`, `src/Meridian.ReferenceData`, `src/Meridian.DataIntegration`, `src/Meridian.Documents`, `src/Meridian.Workflow`, `src/Meridian.Platform` |
| Providers, execution, risk, and strategies | `src/Meridian.ProviderSdk`, `src/Meridian.Execution`, `src/Meridian.Execution.Sdk`, `src/Meridian.Risk`, `src/Meridian.Strategies` |
| Backtesting and replay | `src/Meridian.Backtesting`, `src/Meridian.Backtesting.Sdk` |
| Accounting, financial operations, audit, and F# domain work | `src/Meridian.Ledger`, `src/Meridian.FinancialOperations`, `src/Meridian.Audit`, `src/Meridian.FSharp`, `src/Meridian.FSharp.Ledger`, `src/Meridian.FSharp.Trading`, `src/Meridian.FSharp.DirectLending.Aggregates` |
| Reporting and analytics support | `src/Meridian.Reporting`, `src/Meridian.QuantScript` |
| MCP tooling | `src/Meridian.Mcp` |
| Browser workstation and shared UI/API read models | `src/Meridian.Ui/dashboard`, `src/Meridian.Ui/wwwroot/workstation`, `src/Meridian.Ui.Services`, `src/Meridian.Ui.Shared` |
| Windows desktop workstation | `src/Meridian.Wpf` |
| Installed workstation launch, lifecycle, and setup | `src/Meridian.Launcher`, `src/Meridian.LifecycleSupervisor`, `src/Meridian.Setup` |
| Tests and benchmarks | `tests/`, `benchmarks/` |

For maintained ownership and dependency boundaries, use [Project Structure](docs/architecture/project-structure.md), [Module Map](docs/architecture/module-map.md), and the generated [repository structure](docs/generated/repository-structure.md).

## Quick Commands

Start with the maintained command references when in doubt:

- [Start Here](docs/start/README.md)
- [Engineering Guide](docs/engineering/README.md)
- [Operator Guide](docs/operators/README.md)
- [docs/HELP.md](docs/HELP.md)

Direct commands:

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --help
dotnet run --project src/Meridian/Meridian.csproj -- --seed-demo
dotnet run --project src/Meridian/Meridian.csproj -- --setup
python build/python/cli/buildctl.py --help
```

`--mode workstation --http-port 8080` serves the host and `http://localhost:8080/workstation/`, but
only once a persistence profile is configured — see [Running against your own
data](#running-against-your-own-data). Unlike `--seed-demo`, it does not choose one for you.

GNU Make targets are optional convenience wrappers when `make` is installed:

```bash
make help
make bootstrap
make verify-fast
make verify-full
make verify-docs
make verify-desktop
make verify-release
```

### Main CLI host - `src/Meridian`

The primary runnable project supports collector, desktop-local API host, setup, self-test, backfill, provider recommendation, symbol-management, and ledger-reporting modes.

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --help
dotnet run --project src/Meridian/Meridian.csproj -- --backfill --backfill-symbols AAPL,MSFT --backfill-from 2024-01-01 --backfill-to 2024-12-31
dotnet run --project src/Meridian/Meridian.csproj -- ledger -f ledger.dat balance
```

When launched from the repository root, the local workstation/API host binds to `http://localhost:8080` by default. Config path resolution is `--config <path>` -> `MDC_CONFIG_PATH` -> `config/appsettings.json`.

### Browser workstation - `src/Meridian.Ui/dashboard`

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

### MCP server

Meridian includes a lightweight Model Context Protocol host for repo-navigation and code-review AI tooling. Diagnostic output goes to stderr; stdout is reserved for the MCP protocol.

```bash
dotnet run --project src/Meridian.Mcp/Meridian.Mcp.csproj
```

### Windows WPF desktop app - `src/Meridian.Wpf`

The WPF desktop shell is an active, co-equal product/UI lane. Its immediate focus is closing
browser-first parity gaps under `W8-WPF-PARITY-001`. It shares contracts, read models, and API seams
with the browser workstation. On non-Windows, the project builds as a stub for CI compatibility
unless the full WPF build flag is enabled on Windows.

```bash
pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Development
pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Development -Fixture
pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Production -BuildOnly
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
- Use `python build/python/cli/buildctl.py build --project Meridian.sln --configuration Release` for a restore-once, single-node solution build.
- For automation or concurrent local runs, pass `--isolation-key <name>` to `buildctl.py` so outputs write under isolated `artifacts/bin/<name>/` and `artifacts/obj/<name>/` roots.
- Keep the F# test project's transitive `xunit.v3` runtime pin aligned with `xunit.runner.visualstudio`; Linux/macOS VSTest discovery depends on the v3 JSON handshake.

## Planning Source Of Truth

Use these documents together when planning or implementing new work:

- [Roadmap Registry](docs/roadmap/README.md) and `docs/roadmap/data/*.yml` for active wave and gate records.
- [Generated Roadmap Summary](docs/roadmap/generated/ROADMAP_SUMMARY.md) for rendered roadmap state.
- [Design Charter](docs/product/meridian-design-document.md) for the canonical product framing and evidence-backed operations model.
- [Implementation and Readiness Tracker](docs/product/implementation-todo-list.md) for current implementation, evidence, and readiness follow-up.
- [Provider Capability Matrix](docs/reference/provider-capability-matrix.md) and [Provider Validation Matrix](docs/reference/provider-validation-matrix.md) for provider-confidence scope.
- [Engineering Guide](docs/engineering/README.md) for execution architecture and shared-model guidance.
- [Operator Guide](docs/operators/README.md) for governance and support posture.
- [Source Documentation Mesh](docs/source/README.md) and `docs/source/data/source-modules.yml` for source README ownership and module traceability.

<!-- readme-tree start -->

The full repository tree is generated in [docs/generated/repository-structure.md](docs/generated/repository-structure.md). For maintained ownership and dependency boundaries, use [docs/architecture/project-structure.md](docs/architecture/project-structure.md) and [docs/architecture/module-map.md](docs/architecture/module-map.md).

<!-- readme-tree end -->
