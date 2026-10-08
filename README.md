# Meridian

Meridian is a .NET 10 operational-finance and trading platform in active delivery. It connects source data, reconciliation, accounting, approvals, and governed reporting so financial operations teams can trace a reported number back to its evidence. Fund management is a first-class specialization within the common financial core.

Start with the [seeded demo](#see-it-working-in-one-command), the [source setup guide](docs/start/README.md), or the [documentation index](docs/README.md).

The core product promise is simple:

> Meridian does not just show the number. Meridian proves the number.

For an end user, Meridian is intended to reduce disconnected spreadsheets, inbox handoffs, opaque reconciliations, and hard-to-audit report preparation. The system keeps source evidence, workflow state, approvals, ledger impact, reconciliation history, report provenance, and audit trails connected so an operator can answer:

- What happened?
- Can it be trusted?
- What still needs review, approval, reconciliation, evidence, or reporting?
- Which report, ledger entry, close blocker, or operational decision depends on it?

## Get Meridian Running

Run source commands from the repository root. Choose prerequisites for your task:

| Task | Prerequisites |
| --- | --- |
| Run the tracked browser workstation or seeded demo | Git and the .NET SDK pinned by [global.json](global.json) |
| Develop the browser workstation | Node.js 24 and npm, plus .NET for the connected backend |
| Use Windows development helpers | PowerShell 7 |
| Run build and documentation automation | Python 3.11 or newer; GNU Make is optional |

See [Start Here](docs/start/README.md#prerequisites) for the complete setup path.

Choose the [source setup path](docs/start/README.md) below, or use the
[Windows installer guide](docs/operators/browser-workstation-installer.md) to check release
artifacts and installation requirements. The [release list](https://github.com/rodoHasArrived/Meridian-main/releases)
is the authority for downloadable builds.

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

`--seed-demo` and `--demo` select a database-less local profile. Normal workstation launches
require configured governance persistence and an operator credential path. Follow
[operator preflight](docs/operators/preflight-checklist.md) for prerequisites, separate host and
request terminals, authenticated checks, and recovery steps.

Use the [environment reference](docs/reference/environment-variables.md) for PostgreSQL connection
precedence and the explicitly non-production, file-backed governance opt-in. That opt-in does not
make every money-path store durable. The environment defaults to Production when none is named.

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

Use the owning records for status instead of a second capability checklist here:

| Question | Authoritative record |
| --- | --- |
| What does the product intend to do? | [Design charter](docs/product/meridian-design-document.md) |
| What is implemented, and what blocks release? | [Implementation and Readiness Tracker](docs/product/implementation-todo-list.md) |
| Which milestones have accepted evidence? | [Generated roadmap summary](docs/roadmap/generated/ROADMAP_SUMMARY.md), rendered from the [roadmap registry](docs/roadmap/README.md) |
| What should be worked on next? | [Product Plans](docs/product/plans/README.md) |

A completed roadmap row applies to its named scope and linked evidence. Release decisions still
require current preflight, packaging, deployment, and required GitHub Actions evidence for the
release commit.

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

[Product Plans](docs/product/plans/README.md) identifies the current priority determination and
links supporting engineering plans. The [roadmap registry](docs/roadmap/README.md) owns milestone
state; the [design charter](docs/product/meridian-design-document.md) owns product scope and
deferred boundaries. Consult these records before treating a proposal or dated review as an
active commitment.

## Start Here

| Task | Start with |
| --- | --- |
| Run a demo or set up a checkout | [Start Here](docs/start/README.md) |
| Understand scope, readiness, and priorities | [Product](docs/product/README.md) |
| Build, test, or contribute | [Engineering](docs/engineering/README.md) |
| Install, configure, or recover a workstation | [Operators](docs/operators/README.md) |
| Look up APIs, configuration, or providers | [Reference](docs/reference/README.md) |
| Find documentation or its owner | [Documentation index](docs/README.md) and [ownership contract](docs/documentation-ownership.md) |

## Repository Map

Use [Project Structure](docs/architecture/project-structure.md) and [Module Map](docs/architecture/module-map.md) for maintained ownership and dependency boundaries.

| Area | Entry point |
| --- | --- |
| Host and application orchestration | `src/Meridian/`, `src/Meridian.Application/` |
| Domain, contracts, providers, and storage | [Source Documentation Mesh](docs/source/README.md) |
| Accounting, financial operations, and audit | `src/Meridian.Ledger/`, `src/Meridian.FinancialOperations/`, `src/Meridian.Audit/` |
| Trading, execution, risk, and backtesting | `src/Meridian.Execution/`, `src/Meridian.Risk/`, `src/Meridian.Strategies/`, `src/Meridian.Backtesting/` |
| Browser workstation and shared services | `src/Meridian.Ui/dashboard/`, `src/Meridian.Ui.Services/`, `src/Meridian.Ui.Shared/` |
| Windows workstation and installed lifecycle | `src/Meridian.Wpf/`, `src/Meridian.Launcher/`, `src/Meridian.LifecycleSupervisor/`, `src/Meridian.Setup/` |
| Tests and benchmarks | `tests/`, `benchmarks/` |

## Quick Commands

Use [Start Here](docs/start/README.md) for setup and launch profiles,
[Engineering](docs/engineering/README.md) for build and test commands, and
[Operator preflight](docs/operators/preflight-checklist.md) before using your own data.
The [command help](docs/HELP.md) and [reference index](docs/reference/README.md) own detailed lookup material.

### Main CLI host - `src/Meridian`

Discover host commands directly:

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --help
python build/python/cli/buildctl.py --help
```

For a configured local browser host, use `--mode workstation --http-port 8080`;
it serves `http://localhost:8080/workstation/`. Complete
[operator preflight](docs/operators/preflight-checklist.md) first.
Config path resolution is `--config <path>` → `MDC_CONFIG_PATH` → `config/appsettings.json`.

### Browser workstation - `src/Meridian.Ui/dashboard`

Install root and dashboard dependencies, then launch the coordinated development environment:

```bash
npm ci
npm --prefix src/Meridian.Ui/dashboard ci
npm run dev
```

The root launcher seeds the demo backend, starts the host in watch mode, and starts Vite at
`http://localhost:5173/workstation/`. Use `npm run dev:fixtures` for fixture-only UI work
without a backend. See [browser development](docs/engineering/web-development.md) for
prerequisites, modes, readiness checks, and shutdown behavior.

For Vite alone, use `npm --prefix src/Meridian.Ui/dashboard run dev` and start a configured
API host separately. Vite proxies `/api` to `MERIDIAN_API_BASE_URL`, or
`http://localhost:8080` by default.

When dashboard source changes, regenerate and commit the tracked bundle:

```bash
npm --prefix src/Meridian.Ui/dashboard run build
```

CI checks that `src/Meridian.Ui/wwwroot/workstation/` matches the dashboard source.
See [Start Here](docs/start/README.md#the-workstation-bundle-the-demo-serves) for the bundle contract.

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

Use the smallest lane that covers your change during development:

| Change | Focused command |
| --- | --- |
| Documentation and generated-doc contracts | `bash scripts/ci.sh --lane verify-docs` |
| Browser source and tracked bundle | `bash scripts/ci.sh --lane verify-browser` |
| .NET source and tests | `bash scripts/ci.sh --lane verify-dotnet` |
| Workflow and lane contracts | `bash scripts/ci.sh --lane verify-workflows` |

Before completing PR work, run the canonical repository gate:

```bash
bash scripts/ci.sh
```

Contribute through a `codex/<short-task-name>` branch and a pull request targeting `main`.
Required GitHub Actions checks remain the merge authority.
See [Engineering](docs/engineering/README.md#buildtestrun) for desktop and release validation,
build isolation, and hosted validation when local tooling is unavailable.

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
