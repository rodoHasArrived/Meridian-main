# CLAUDE.md - AI Assistant Guide for Meridian

## Local Codex Skills

Repo-local Codex skills live under `.codex/skills/`. Use them for Meridian-specific blueprinting, brainstorming, code review, provider implementation, and test writing workflows.

**Meridian** is a high-performance .NET 9.0 / C# 13 / F# 8.0 integrated trading platform. It collects real-time and historical market microstructure data from multiple providers, executes trading strategies in real-time, backtests strategies on historical data, and tracks portfolio performance across all runs.

**Version:** 1.7.2 | **Status:** Development / Pilot Ready | **Files:** 1,220 source files (1,169 C# + 51 F#) | **Tests:** ~4,756

### Platform Pillars
- **📡 Data Collection** - Real-time streaming (90+ sources) + historical backfill (10+ providers) with data quality monitoring
- **🔬 Backtesting** - Tick-level strategy replay with fill models, portfolio metrics (Sharpe, drawdown, XIRR), and full audit trail
- **⚡ Real-Time Execution** - Paper trading gateway + brokerage gateway framework (Alpaca, IB, StockSharp) for strategy validation and live integration
- **🗂️ Portfolio Tracking** - Performance metrics, strategy lifecycle management, and multi-run comparison

### Key Capabilities
- Real-time streaming: Interactive Brokers, Alpaca, NYSE, Polygon, StockSharp (90+ sources)
- Historical backfill: 10+ providers with automatic fallback chain
- Symbol search: 5 providers (Alpaca, Finnhub, Polygon, OpenFIGI, StockSharp)
- Brokerage gateway framework: Alpaca, IB, StockSharp adapters for order routing
- Current provider implementation inventory documented below for audit parity (streaming, historical, symbol-search, brokerage, base, and template classes)
- Data quality monitoring with SLA enforcement
- WAL + tiered JSONL/Parquet storage
- Backtesting engine with tick-by-tick replay and fill models
- Paper trading gateway with risk rules (position limits, drawdown stops, order rate throttle)
- Portfolio performance tracking and multi-run analysis
- Direct lending module with PostgreSQL persistence
- Web dashboard (300 API routes, 0 stubs)
- WPF desktop app (Windows) — **code present in `src/Meridian.Wpf/`, included in solution build; builds a stub on non-Windows for CI compatibility**
- QuantConnect Lean Engine integration
- CppTrader native matching engine integration

---

## Quick Commands

```bash
# Build & Test
dotnet build -c Release
dotnet test tests/Meridian.Tests
dotnet test tests/Meridian.FSharp.Tests
make test                    # All tests via Makefile
make build                   # Build via Makefile

# Run
dotnet run --project src/Meridian/Meridian.csproj -- --ui --http-port 8080
make run-ui

# AI Audit Tools (run before/after changes)
make ai-audit                # Full audit (code, docs, tests, providers)
make ai-audit-code           # Convention violations only
make ai-audit-tests          # Test coverage gaps
make ai-verify               # Build + test + lint
make ai-maintenance-light    # Fast maintenance lane + .ai/maintenance-status.json
make ai-maintenance-full     # Full maintenance lane + .ai/maintenance-status.json
python3 build/scripts/ai-repo-updater.py known-errors   # Avoid past AI mistakes
python3 build/scripts/ai-repo-updater.py diff-summary   # Review uncommitted changes

# Diagnostics
make doctor
make diagnose
dotnet restore /p:EnableWindowsTargeting=true -v diag   # Build issue diagnosis

# Backfill
dotnet run --project src/Meridian -- \
  --backfill --backfill-provider stooq \
  --backfill-symbols SPY,AAPL \
  --backfill-from 2024-01-01 --backfill-to 2024-01-05

```

---

## Standard Execution Flow

For every task, follow this sequence to maximize quality and minimize review cycles:

1. **Restate the requested change** in one sentence.
2. **Identify acceptance criteria** before coding (including required tests).
3. **Make the smallest possible set of edits** that satisfy the task.
4. **Run targeted validation commands** (see "Quick Commands" section above).
5. **Summarize what changed, why, and how it was validated.**

If requirements are ambiguous, document assumptions and propose concrete acceptance criteria before proceeding.

---

## Quality Bar Checklist (Before Opening PR or Marking Work Complete)

Always complete this checklist before submitting a PR or marking work as complete:

1. **Review known errors:** Run `python3 build/scripts/ai-repo-updater.py known-errors` and scan `docs/ai/ai-known-errors.md`. Apply all relevant prevention checks. If this task is related to a past AI mistake, verify the prevention pattern is applied.

2. **Restore and build with Windows targeting:**
   ```bash
   dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
   dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true
   ```
   **Note:** Always use `/p:EnableWindowsTargeting=true` on non-Windows systems to avoid NETSDK1100 errors.

3. **Run tests relevant to touched code:**
   - If you modified `src/Meridian.Domain/**`: `dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true`
   - If you modified `src/Meridian.FSharp/**`: also run `dotnet test tests/Meridian.FSharp.Tests -c Release /p:EnableWindowsTargeting=true`
   - Run `make test` to run all tests if unsure

4. **Update docs when behavior changes:**
   - Public API changes → update interface documentation in relevant `CLAUDE.*.md` file or code comments
   - New feature or workflow change → update `docs/HELP.md` FAQ or relevant architecture doc
   - Provider added/modified → update provider inventory in `CLAUDE.md` or `docs/ai/claude/CLAUDE.providers.md`

5. **Keep PR title and body in sync:**
   - Title should match final implemented behavior
   - Body should include: summary, risks/tradeoffs, validation commands run, and follow-up items

---

## AI Error Prevention

**Required workflow:**

1. **Before making changes**: run `python3 build/scripts/ai-repo-updater.py known-errors` and scan `docs/ai/ai-known-errors.md`
2. **After fixing an agent-caused bug**: add a new entry to `docs/ai/ai-known-errors.md` (symptoms, root cause, prevention, verification command)
3. **Before opening PR**: confirm your change does not repeat any known pattern
4. **For automation environments**: prefer the light/full maintenance lanes, which emit `.ai/maintenance-status.json` and `.ai/MAINTENANCE_STATUS.md`

---

## Repository Layout

```text
Meridian-main
├── .claude
│   ├── agents
│   │   ├── meridian-blueprint.md
│   │   ├── meridian-cleanup.md
│   │   └── meridian-docs.md
│   ├── skills
│   │   ├── _shared
│   │   │   └── project-context.md
│   │   ├── meridian-blueprint
│   │   │   ├── references
│   │   │   │   ├── blueprint-patterns.md
│   │   │   │   └── pipeline-position.md
│   │   │   ├── CHANGELOG.md
│   │   │   └── SKILL.md
│   │   ├── meridian-brainstorm
│   │   │   ├── references
│   │   │   │   ├── competitive-landscape.md
│   │   │   │   └── idea-dimensions.md
│   │   │   ├── brainstorm-history.jsonl
│   │   │   ├── CHANGELOG.md
│   │   │   └── SKILL.md
│   │   ├── meridian-code-review
│   │   │   ├── agents
│   │   │   │   └── grader.md
│   │   │   ├── eval-viewer
│   │   │   │   ├── generate_review.py
│   │   │   │   └── viewer.html
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── references
│   │   │   │   ├── architecture.md
│   │   │   │   └── schemas.md
│   │   │   ├── scripts
│   │   │   │   ├── __init__.py
│   │   │   │   ├── aggregate_benchmark.py
│   │   │   │   ├── package_skill.py
│   │   │   │   ├── quick_validate.py
│   │   │   │   ├── run_eval.py
│   │   │   │   └── utils.py
│   │   │   ├── CHANGELOG.md
│   │   │   └── SKILL.md
│   │   ├── meridian-provider-builder
│   │   │   ├── references
│   │   │   │   └── provider-patterns.md
│   │   │   ├── CHANGELOG.md
│   │   │   └── SKILL.md
│   │   ├── meridian-test-writer
│   │   │   ├── references
│   │   │   │   └── test-patterns.md
│   │   │   ├── CHANGELOG.md
│   │   │   └── SKILL.md
│   │   └── skills_provider.py
│   ├── settings.json
│   └── settings.local.json
├── .codex
│   ├── environments
│   │   ├── environment.toml
│   │   └── README.md
│   ├── skills
│   │   ├── _shared
│   │   │   └── project-context.md
│   │   ├── meridian-blueprint
│   │   │   ├── references
│   │   │   │   └── blueprint-patterns.md
│   │   │   └── SKILL.md
│   │   ├── meridian-brainstorm
│   │   │   ├── references
│   │   │   │   └── competitive-landscape.md
│   │   │   └── SKILL.md
│   │   ├── meridian-code-review
│   │   │   └── SKILL.md
│   │   ├── meridian-provider-builder
│   │   │   ├── references
│   │   │   │   └── provider-patterns.md
│   │   │   └── SKILL.md
│   │   ├── meridian-roadmap-strategist
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── references
│   │   │   │   └── roadmap-source-map.md
│   │   │   └── SKILL.md
│   │   ├── meridian-test-writer
│   │   │   ├── references
│   │   │   │   └── test-patterns.md
│   │   │   └── SKILL.md
│   │   └── README.md
│   └── config.toml
├── .devcontainer
│   └── devcontainer.json
├── .githooks
│   └── pre-commit
├── .github
│   ├── actions
│   │   └── setup-dotnet-cache
│   │       └── action.yml
│   ├── agents
│   │   ├── adr-generator.agent.md
│   │   ├── blueprint-agent.md
│   │   ├── brainstorm-agent.md
│   │   ├── bug-fix-agent.md
│   │   ├── cleanup-agent.md
│   │   ├── cleanup-specialist.agent.md
│   │   ├── code-review-agent.md
│   │   ├── documentation-agent.md
│   │   ├── performance-agent.md
│   │   ├── provider-builder-agent.md
│   │   └── test-writer-agent.md
│   ├── instructions
│   │   ├── csharp.instructions.md
│   │   ├── docs.instructions.md
│   │   ├── dotnet-tests.instructions.md
│   │   └── wpf.instructions.md
│   ├── ISSUE_TEMPLATE
│   │   ├── .gitkeep
│   │   ├── bug_report.yml
│   │   ├── config.yml
│   │   └── feature_request.yml
│   ├── prompts
│   │   ├── add-data-provider.prompt.yml
│   │   ├── add-export-format.prompt.yml
│   │   ├── code-review.prompt.yml
│   │   ├── configure-deployment.prompt.yml
│   │   ├── explain-architecture.prompt.yml
│   │   ├── fix-build-errors.prompt.yml
│   │   ├── fix-code-quality.prompt.yml
│   │   ├── fix-test-failures.prompt.yml
│   │   ├── optimize-performance.prompt.yml
│   │   ├── project-context.prompt.yml
│   │   ├── provider-implementation-guide.prompt.yml
│   │   ├── README.md
│   │   ├── troubleshoot-issue.prompt.yml
│   │   ├── workflow-results-code-quality.prompt.yml
│   │   ├── workflow-results-test-matrix.prompt.yml
│   │   ├── wpf-debug-improve.prompt.yml
│   │   └── write-unit-tests.prompt.yml
│   ├── workflows
│   │   ├── benchmark.yml
│   │   ├── bottleneck-detection.yml
│   │   ├── build-observability.yml
│   │   ├── canonicalization-fixture-maintenance.yml
│   │   ├── close-duplicate-issues.yml
│   │   ├── code-quality.yml
│   │   ├── copilot-pull-request-reviewer.yml
│   │   ├── copilot-setup-steps.yml
│   │   ├── copilot-swe-agent-copilot.yml
│   │   ├── desktop-builds.yml
│   │   ├── docker.yml
│   │   ├── documentation.yml
│   │   ├── export-project-artifact.yml
│   │   ├── golden-path-validation.yml
│   │   ├── labeling.yml
│   │   ├── maintenance-self-test.yml
│   │   ├── maintenance.yml
│   │   ├── makefile.yml
│   │   ├── nightly.yml
│   │   ├── pr-checks.yml
│   │   ├── prompt-generation.yml
│   │   ├── python-package-conda.yml
│   │   ├── readme-tree.yml
│   │   ├── README.md
│   │   ├── release.yml
│   │   ├── repo-health.yml
│   │   ├── reusable-dotnet-build.yml
│   │   ├── scheduled-maintenance.yml
│   │   ├── security.yml
│   │   ├── skill-evals.yml
│   │   ├── SKIPPED_JOBS_EXPLAINED.md
│   │   ├── stale.yml
│   │   ├── static.yml
│   │   ├── test-matrix.yml
│   │   ├── ticker-data-collection.yml
│   │   ├── update-diagrams.yml
│   │   └── validate-workflows.yml
│   ├── copilot-instructions.md
│   ├── dependabot.yml
│   ├── labeler.yml
│   ├── labels.yml
│   ├── markdown-link-check-config.json
│   ├── PULL_REQUEST_TEMPLATE.md
│   ├── pull_request_template_desktop.md
│   └── spellcheck-config.yml
├── benchmarks
│   ├── Meridian.Benchmarks
│   │   ├── Budget
│   │   │   ├── BenchmarkResultStore.cs
│   │   │   ├── IPerformanceBudget.cs
│   │   │   ├── PerformanceBudget.cs
│   │   │   └── PerformanceBudgetRegistry.cs
│   │   ├── CanonicalizationBenchmarks.cs
│   │   ├── CollectorBenchmarks.cs
│   │   ├── CompositeSinkBenchmarks.cs
│   │   ├── DeduplicationKeyBenchmarks.cs
│   │   ├── EndToEndPipelineBenchmarks.cs
│   │   ├── EventPipelineBenchmarks.cs
│   │   ├── IndicatorBenchmarks.cs
│   │   ├── JsonSerializationBenchmarks.cs
│   │   ├── Meridian.Benchmarks.csproj
│   │   ├── NewlineScanBenchmarks.cs
│   │   ├── Program.cs
│   │   ├── StorageSinkBenchmarks.cs
│   │   └── WalChecksumBenchmarks.cs
│   ├── BOTTLENECK_REPORT.md
│   └── run-bottleneck-benchmarks.sh
├── build
│   ├── dotnet
│   │   ├── DocGenerator
│   │   │   ├── DocGenerator.csproj
│   │   │   └── Program.cs
│   │   └── FSharpInteropGenerator
│   │       ├── FSharpInteropGenerator.csproj
│   │       └── Program.cs
│   ├── node
│   │   ├── generate-diagrams.mjs
│   │   └── generate-icons.mjs
│   ├── python
│   │   ├── adapters
│   │   │   ├── __init__.py
│   │   │   └── dotnet.py
│   │   ├── analytics
│   │   │   ├── __init__.py
│   │   │   ├── history.py
│   │   │   ├── metrics.py
│   │   │   └── profile.py
│   │   ├── cli
│   │   │   └── buildctl.py
│   │   ├── core
│   │   │   ├── __init__.py
│   │   │   ├── events.py
│   │   │   ├── fingerprint.py
│   │   │   ├── graph.py
│   │   │   └── utils.py
│   │   ├── diagnostics
│   │   │   ├── __init__.py
│   │   │   ├── doctor.py
│   │   │   ├── env_diff.py
│   │   │   ├── error_matcher.py
│   │   │   ├── preflight.py
│   │   │   └── validate_data.py
│   │   ├── knowledge
│   │   │   └── errors
│   │   │       ├── msbuild.json
│   │   │       └── nuget.json
│   │   └── __init__.py
│   ├── rules
│   │   └── doc-rules.yaml
│   └── scripts
│       ├── docs
│       │   ├── add-todos.py
│       │   ├── ai-docs-maintenance.py
│       │   ├── create-todo-issues.py
│       │   ├── generate-changelog.py
│       │   ├── generate-coverage.py
│       │   ├── generate-dependency-graph.py
│       │   ├── generate-health-dashboard.py
│       │   ├── generate-metrics-dashboard.py
│       │   ├── generate-prompts.py
│       │   ├── generate-structure-docs.py
│       │   ├── README.md
│       │   ├── repair-links.py
│       │   ├── rules-engine.py
│       │   ├── run-docs-automation.py
│       │   ├── scan-todos.py
│       │   ├── sync-readme-badges.py
│       │   ├── test-scripts.py
│       │   ├── update-claude-md.py
│       │   ├── validate-api-docs.py
│       │   ├── validate-docs-structure.py
│       │   ├── validate-examples.py
│       │   ├── validate-golden-path.sh
│       │   └── validate-skill-packages.py
│       ├── hooks
│       │   ├── commit-msg
│       │   ├── install-hooks.sh
│       │   └── pre-commit
│       ├── install
│       │   ├── install.ps1
│       │   └── install.sh
│       ├── lib
│       │   └── BuildNotification.psm1
│       ├── publish
│       │   ├── publish.ps1
│       │   └── publish.sh
│       ├── run
│       │   ├── start-collector.ps1
│       │   ├── start-collector.sh
│       │   ├── stop-collector.ps1
│       │   └── stop-collector.sh
│       ├── tests
│       │   └── test_validate_budget.py
│       ├── ai-architecture-check.py
│       ├── ai-repo-updater.py
│       ├── validate-tooling-metadata.py
│       └── validate_budget.py
├── config
│   ├── appsettings.sample.json
│   ├── appsettings.schema.json
│   ├── condition-codes.json
│   └── venue-mapping.json
├── deploy
│   ├── docker
│   │   ├── .dockerignore
│   │   ├── docker-compose.override.yml
│   │   ├── docker-compose.yml
│   │   └── Dockerfile
│   ├── k8s
│   │   ├── configmap.yaml
│   │   ├── deployment.yaml
│   │   ├── kustomization.yaml
│   │   ├── namespace.yaml
│   │   ├── pvc.yaml
│   │   ├── secret.yaml
│   │   ├── service.yaml
│   │   └── serviceaccount.yaml
│   ├── monitoring
│   │   ├── grafana
│   │   │   └── provisioning
│   │   │       ├── dashboards
│   │   │       │   ├── json
│   │   │       │   │   ├── meridian-overview.json
│   │   │       │   │   └── meridian-trades.json
│   │   │       │   └── dashboards.yml
│   │   │       └── datasources
│   │   │           └── datasources.yml
│   │   ├── alert-rules.yml
│   │   └── prometheus.yml
│   └── systemd
│       └── meridian.service
├── docs
│   ├── adr
│   │   ├── 001-provider-abstraction.md
│   │   ├── 002-tiered-storage-architecture.md
│   │   ├── 003-microservices-decomposition.md
│   │   ├── 004-async-streaming-patterns.md
│   │   ├── 005-attribute-based-discovery.md
│   │   ├── 006-domain-events-polymorphic-payload.md
│   │   ├── 007-write-ahead-log-durability.md
│   │   ├── 008-multi-format-composite-storage.md
│   │   ├── 009-fsharp-interop.md
│   │   ├── 010-httpclient-factory.md
│   │   ├── 011-centralized-configuration-and-credentials.md
│   │   ├── 012-monitoring-and-alerting-pipeline.md
│   │   ├── 013-bounded-channel-policy.md
│   │   ├── 014-json-source-generators.md
│   │   ├── 015-strategy-execution-contract.md
│   │   ├── 016-platform-architecture-migration.md
│   │   ├── _template.md
│   │   ├── ADR-015-platform-restructuring.md
│   │   └── README.md
│   ├── ai
│   │   ├── agents
│   │   │   └── README.md
│   │   ├── claude
│   │   │   ├── CLAUDE.actions.md
│   │   │   ├── CLAUDE.api.md
│   │   │   ├── CLAUDE.domain-naming.md
│   │   │   ├── CLAUDE.fsharp.md
│   │   │   ├── CLAUDE.providers.md
│   │   │   ├── CLAUDE.repo-updater.md
│   │   │   ├── CLAUDE.storage.md
│   │   │   ├── CLAUDE.structure.md
│   │   │   └── CLAUDE.testing.md
│   │   ├── copilot
│   │   │   ├── ai-sync-workflow.md
│   │   │   └── instructions.md
│   │   ├── instructions
│   │   │   └── README.md
│   │   ├── prompts
│   │   │   └── README.md
│   │   ├── skills
│   │   │   └── README.md
│   │   ├── ai-known-errors.md
│   │   └── README.md
│   ├── architecture
│   │   ├── c4-diagrams.md
│   │   ├── crystallized-storage-format.md
│   │   ├── desktop-layers.md
│   │   ├── deterministic-canonicalization.md
│   │   ├── domains.md
│   │   ├── layer-boundaries.md
│   │   ├── overview.md
│   │   ├── provider-management.md
│   │   ├── README.md
│   │   ├── storage-design.md
│   │   ├── ui-redesign.md
│   │   └── why-this-architecture.md
│   ├── audits
│   │   ├── audit-architecture-results.txt
│   │   ├── audit-code-results.json
│   │   ├── audit-results-full.json
│   │   ├── AUDIT_REPORT.md
│   │   ├── BACKTEST_ENGINE_CODE_REVIEW_2026_03_25.md
│   │   ├── CODE_REVIEW_2026-03-16.md
│   │   ├── FURTHER_SIMPLIFICATION_OPPORTUNITIES.md
│   │   ├── prompt-generation-results.json
│   │   └── README.md
│   ├── development
│   │   ├── policies
│   │   │   └── desktop-support-policy.md
│   │   ├── adding-custom-rules.md
│   │   ├── build-observability.md
│   │   ├── central-package-management.md
│   │   ├── desktop-testing-guide.md
│   │   ├── documentation-automation.md
│   │   ├── documentation-contribution-guide.md
│   │   ├── expanding-scripts.md
│   │   ├── fsharp-decision-rule.md
│   │   ├── git-hooks.md
│   │   ├── github-actions-summary.md
│   │   ├── github-actions-testing.md
│   │   ├── otlp-trace-visualization.md
│   │   ├── provider-implementation.md
│   │   ├── README.md
│   │   ├── refactor-map.md
│   │   ├── repository-organization-guide.md
│   │   ├── repository-rule-set.md
│   │   ├── tooling-workflow-backlog.md
│   │   ├── ui-fixture-mode-guide.md
│   │   └── wpf-implementation-notes.md
│   ├── diagrams
│   │   ├── uml
│   │   │   ├── activity-diagram-backfill.png
│   │   │   ├── activity-diagram-backfill.puml
│   │   │   ├── activity-diagram.png
│   │   │   ├── activity-diagram.puml
│   │   │   ├── communication-diagram.png
│   │   │   ├── communication-diagram.puml
│   │   │   ├── interaction-overview-diagram.png
│   │   │   ├── interaction-overview-diagram.puml
│   │   │   ├── README.md
│   │   │   ├── sequence-diagram-backfill.png
│   │   │   ├── sequence-diagram-backfill.puml
│   │   │   ├── sequence-diagram.png
│   │   │   ├── sequence-diagram.puml
│   │   │   ├── state-diagram-backfill.png
│   │   │   ├── state-diagram-backfill.puml
│   │   │   ├── state-diagram-orderbook.png
│   │   │   ├── state-diagram-orderbook.puml
│   │   │   ├── state-diagram-trade-sequence.png
│   │   │   ├── state-diagram-trade-sequence.puml
│   │   │   ├── state-diagram.png
│   │   │   ├── state-diagram.puml
│   │   │   ├── timing-diagram-backfill.png
│   │   │   ├── timing-diagram-backfill.puml
│   │   │   ├── timing-diagram.png
│   │   │   ├── timing-diagram.puml
│   │   │   ├── use-case-diagram.png
│   │   │   └── use-case-diagram.puml
│   │   ├── backfill-workflow.dot
│   │   ├── backtesting-engine.dot
│   │   ├── c4-level1-context.dot
│   │   ├── c4-level1-context.png
│   │   ├── c4-level1-context.svg
│   │   ├── c4-level2-containers.dot
│   │   ├── c4-level2-containers.png
│   │   ├── c4-level2-containers.svg
│   │   ├── c4-level3-components.dot
│   │   ├── c4-level3-components.png
│   │   ├── c4-level3-components.svg
│   │   ├── cli-commands.dot
│   │   ├── cli-commands.png
│   │   ├── cli-commands.svg
│   │   ├── configuration-management.dot
│   │   ├── data-flow.dot
│   │   ├── data-flow.png
│   │   ├── data-flow.svg
│   │   ├── data-quality-monitoring.dot
│   │   ├── deployment-options.dot
│   │   ├── deployment-options.png
│   │   ├── deployment-options.svg
│   │   ├── domain-event-model.dot
│   │   ├── event-pipeline-sequence.dot
│   │   ├── event-pipeline-sequence.png
│   │   ├── event-pipeline-sequence.svg
│   │   ├── execution-layer.dot
│   │   ├── fsharp-domain.dot
│   │   ├── mcp-server.dot
│   │   ├── onboarding-flow.dot
│   │   ├── onboarding-flow.png
│   │   ├── onboarding-flow.svg
│   │   ├── project-dependencies.dot
│   │   ├── project-dependencies.png
│   │   ├── project-dependencies.svg
│   │   ├── provider-architecture.dot
│   │   ├── provider-architecture.png
│   │   ├── provider-architecture.svg
│   │   ├── README.md
│   │   ├── resilience-patterns.dot
│   │   ├── resilience-patterns.png
│   │   ├── resilience-patterns.svg
│   │   ├── storage-architecture.dot
│   │   ├── storage-architecture.png
│   │   ├── storage-architecture.svg
│   │   ├── strategy-lifecycle.dot
│   │   ├── symbol-search-resolution.dot
│   │   ├── ui-implementation-flow.dot
│   │   ├── ui-implementation-flow.svg
│   │   ├── ui-navigation-map.dot
│   │   └── ui-navigation-map.svg
│   ├── docfx
│   │   ├── api
│   │   │   └── index.md
│   │   ├── docfx.json
│   │   ├── filterConfig.yml
│   │   └── README.md
│   ├── evaluations
│   │   ├── 2026-03-brainstorm-next-frontier.md
│   │   ├── assembly-performance-opportunities.md
│   │   ├── competitive-analysis-2026-03.md
│   │   ├── data-quality-monitoring-evaluation.md
│   │   ├── desktop-improvements-executive-summary.md
│   │   ├── desktop-platform-improvements-implementation-guide.md
│   │   ├── high-impact-improvement-brainstorm-2026-03.md
│   │   ├── high-value-low-cost-improvements-brainstorm.md
│   │   ├── historical-data-providers-evaluation.md
│   │   ├── ingestion-orchestration-evaluation.md
│   │   ├── nautilus-inspired-restructuring-proposal.md
│   │   ├── operational-readiness-evaluation.md
│   │   ├── quant-script-blueprint-brainstorm.md
│   │   ├── README.md
│   │   ├── realtime-streaming-architecture-evaluation.md
│   │   ├── storage-architecture-evaluation.md
│   │   └── windows-desktop-provider-configurability-assessment.md
│   ├── examples
│   │   ├── provider-template
│   │   │   ├── README.md
│   │   │   ├── TemplateConfig.cs
│   │   │   ├── TemplateConstants.cs
│   │   │   ├── TemplateFactory.cs
│   │   │   ├── TemplateHistoricalDataProvider.cs
│   │   │   ├── TemplateMarketDataClient.cs
│   │   │   └── TemplateSymbolSearchProvider.cs
│   │   └── README.md
│   ├── generated
│   │   ├── adr-index.md
│   │   ├── configuration-schema.md
│   │   ├── documentation-coverage.md
│   │   ├── interfaces.md
│   │   ├── project-context.md
│   │   ├── project-dependencies.md
│   │   ├── provider-registry.md
│   │   ├── README.md
│   │   ├── repository-structure.md
│   │   └── workflows-overview.md
│   ├── getting-started
│   │   └── README.md
│   ├── integrations
│   │   ├── fsharp-integration.md
│   │   ├── language-strategy.md
│   │   ├── lean-integration.md
│   │   └── README.md
│   ├── operations
│   │   ├── deployment.md
│   │   ├── high-availability.md
│   │   ├── msix-packaging.md
│   │   ├── operator-runbook.md
│   │   ├── performance-tuning.md
│   │   ├── portable-data-packager.md
│   │   ├── README.md
│   │   └── service-level-objectives.md
│   ├── plans
│   │   ├── assembly-performance-roadmap.md
│   │   ├── codebase-audit-cleanup-roadmap.md
│   │   ├── fund-management-module-implementation-backlog.md
│   │   ├── fund-management-pr-sequenced-roadmap.md
│   │   ├── fund-management-product-vision-and-capability-matrix.md
│   │   ├── governance-fund-ops-blueprint.md
│   │   ├── l3-inference-implementation-plan.md
│   │   ├── meridian-6-week-roadmap.md
│   │   ├── meridian-database-blueprint.md
│   │   ├── quant-script-environment-blueprint.md
│   │   ├── readability-refactor-baseline.md
│   │   ├── readability-refactor-roadmap.md
│   │   ├── readability-refactor-technical-design-pack.md
│   │   ├── README.md
│   │   ├── security-master-productization-roadmap.md
│   │   ├── trading-workstation-migration-blueprint.md
│   │   ├── ufl-bond-target-state-v2.md
│   │   ├── ufl-cash-sweep-target-state-v2.md
│   │   ├── ufl-certificate-of-deposit-target-state-v2.md
│   │   ├── ufl-commercial-paper-target-state-v2.md
│   │   ├── ufl-deposit-target-state-v2.md
│   │   ├── ufl-direct-lending-implementation-roadmap.md
│   │   ├── ufl-direct-lending-target-state-v2.md
│   │   ├── ufl-equity-target-state-v2.md
│   │   ├── ufl-future-target-state-v2.md
│   │   ├── ufl-fx-spot-target-state-v2.md
│   │   ├── ufl-money-market-fund-target-state-v2.md
│   │   ├── ufl-option-target-state-v2.md
│   │   ├── ufl-other-security-target-state-v2.md
│   │   ├── ufl-repo-target-state-v2.md
│   │   ├── ufl-supported-assets-index.md
│   │   ├── ufl-swap-target-state-v2.md
│   │   ├── ufl-treasury-bill-target-state-v2.md
│   │   ├── workstation-release-readiness-blueprint.md
│   │   └── workstation-sprint-1-implementation-backlog.md
│   ├── providers
│   │   ├── alpaca-setup.md
│   │   ├── backfill-guide.md
│   │   ├── data-sources.md
│   │   ├── interactive-brokers-free-equity-reference.md
│   │   ├── interactive-brokers-setup.md
│   │   ├── provider-comparison.md
│   │   ├── README.md
│   │   ├── security-master-guide.md
│   │   └── stocksharp-connectors.md
│   ├── reference
│   │   ├── api-reference.md
│   │   ├── data-dictionary.md
│   │   ├── data-uniformity.md
│   │   ├── design-review-memo.md
│   │   ├── environment-variables.md
│   │   ├── open-source-references.md
│   │   └── README.md
│   ├── security
│   │   ├── known-vulnerabilities.md
│   │   └── README.md
│   ├── status
│   │   ├── api-docs-report.md
│   │   ├── badge-sync-report.md
│   │   ├── CHANGELOG.md
│   │   ├── coverage-report.md
│   │   ├── docs-automation-summary.json
│   │   ├── docs-automation-summary.md
│   │   ├── DOCUMENTATION_TRIAGE_2026_03_21.md
│   │   ├── EVALUATIONS_AND_AUDITS.md
│   │   ├── example-validation.md
│   │   ├── FEATURE_INVENTORY.md
│   │   ├── FULL_IMPLEMENTATION_TODO_2026_03_20.md
│   │   ├── health-dashboard.md
│   │   ├── IMPROVEMENTS.md
│   │   ├── link-repair-report.md
│   │   ├── metrics-dashboard.md
│   │   ├── production-status.md
│   │   ├── README.md
│   │   ├── ROADMAP.md
│   │   ├── ROADMAP_NOW_NEXT_LATER_2026_03_25.md
│   │   ├── rules-report.md
│   │   └── TODO.md
│   ├── DEPENDENCIES.md
│   ├── HELP.md
│   ├── README.md
│   └── toc.yml
├── make
│   ├── ai.mk
│   ├── build.mk
│   ├── desktop.mk
│   ├── diagnostics.mk
│   ├── docs.mk
│   ├── install.mk
│   └── test.mk
├── native
│   └── cpptrader-host
│       ├── src
│       │   └── main.cpp
│       ├── CMakeLists.txt
│       └── README.md
├── scripts
│   ├── ai
│   │   ├── cleanup.sh
│   │   ├── common.sh
│   │   ├── maintenance-full.sh
│   │   ├── maintenance-light.sh
│   │   ├── maintenance.sh
│   │   ├── route-maintenance.sh
│   │   ├── setup-ai-agent.sh
│   │   └── setup.sh
│   ├── dev
│   │   ├── build-ibapi-smoke.ps1
│   │   ├── desktop-dev.ps1
│   │   ├── diagnose-uwp-xaml.ps1
│   │   └── install-git-hooks.sh
│   ├── lib
│   │   ├── ui-diagram-generator.mjs
│   │   └── ui-diagram-generator.test.mjs
│   ├── compare_benchmarks.py
│   ├── generate-diagrams.mjs
│   └── report_canonicalization_drift.py
├── src
│   ├── Meridian
│   │   ├── Integrations
│   │   │   └── Lean
│   │   │       ├── MeridianDataProvider.cs
│   │   │       ├── MeridianQuoteData.cs
│   │   │       ├── MeridianTradeData.cs
│   │   │       ├── README.md
│   │   │       └── SampleLeanAlgorithm.cs
│   │   ├── Tools
│   │   │   └── DataValidator.cs
│   │   ├── wwwroot
│   │   │   └── templates
│   │   │       ├── credentials.html
│   │   │       ├── index.html
│   │   │       └── index.js
│   │   ├── app.manifest
│   │   ├── DashboardServerBridge.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.csproj
│   │   ├── Program.cs
│   │   ├── runtimeconfig.template.json
│   │   └── UiServer.cs
│   ├── Meridian.Application
│   │   ├── Backfill
│   │   │   ├── BackfillCostEstimator.cs
│   │   │   ├── BackfillRequest.cs
│   │   │   ├── BackfillResult.cs
│   │   │   ├── BackfillStatusStore.cs
│   │   │   ├── GapBackfillService.cs
│   │   │   └── HistoricalBackfillService.cs
│   │   ├── Banking
│   │   │   ├── BankingException.cs
│   │   │   ├── IBankingService.cs
│   │   │   └── InMemoryBankingService.cs
│   │   ├── Canonicalization
│   │   │   ├── CanonicalizationMetrics.cs
│   │   │   ├── CanonicalizingPublisher.cs
│   │   │   ├── ConditionCodeMapper.cs
│   │   │   ├── EventCanonicalizer.cs
│   │   │   ├── IEventCanonicalizer.cs
│   │   │   └── VenueMicMapper.cs
│   │   ├── Commands
│   │   │   ├── CatalogCommand.cs
│   │   │   ├── CliArguments.cs
│   │   │   ├── CommandDispatcher.cs
│   │   │   ├── ConfigCommands.cs
│   │   │   ├── ConfigPresetCommand.cs
│   │   │   ├── DiagnosticsCommands.cs
│   │   │   ├── DryRunCommand.cs
│   │   │   ├── EtlCommands.cs
│   │   │   ├── GenerateLoaderCommand.cs
│   │   │   ├── HelpCommand.cs
│   │   │   ├── ICliCommand.cs
│   │   │   ├── PackageCommands.cs
│   │   │   ├── QueryCommand.cs
│   │   │   ├── SchemaCheckCommand.cs
│   │   │   ├── SelfTestCommand.cs
│   │   │   ├── SymbolCommands.cs
│   │   │   ├── ValidateConfigCommand.cs
│   │   │   └── WalRepairCommand.cs
│   │   ├── Composition
│   │   │   ├── Features
│   │   │   │   ├── BackfillFeatureRegistration.cs
│   │   │   │   ├── CanonicalizationFeatureRegistration.cs
│   │   │   │   ├── CollectorFeatureRegistration.cs
│   │   │   │   ├── ConfigurationFeatureRegistration.cs
│   │   │   │   ├── CoordinationFeatureRegistration.cs
│   │   │   │   ├── CredentialFeatureRegistration.cs
│   │   │   │   ├── DiagnosticsFeatureRegistration.cs
│   │   │   │   ├── EtlFeatureRegistration.cs
│   │   │   │   ├── HttpClientFeatureRegistration.cs
│   │   │   │   ├── IServiceFeatureRegistration.cs
│   │   │   │   ├── MaintenanceFeatureRegistration.cs
│   │   │   │   ├── PipelineFeatureRegistration.cs
│   │   │   │   ├── ProviderFeatureRegistration.cs
│   │   │   │   ├── StorageFeatureRegistration.cs
│   │   │   │   └── SymbolManagementFeatureRegistration.cs
│   │   │   ├── Startup
│   │   │   │   └── SharedStartupBootstrapper.cs
│   │   │   ├── CircuitBreakerCallbackRouter.cs
│   │   │   ├── DirectLendingStartup.cs
│   │   │   ├── HostAdapters.cs
│   │   │   ├── HostStartup.cs
│   │   │   ├── SecurityMasterStartup.cs
│   │   │   └── ServiceCompositionRoot.cs
│   │   ├── Config
│   │   │   ├── Credentials
│   │   │   │   ├── CredentialStatus.cs
│   │   │   │   ├── CredentialTestingService.cs
│   │   │   │   ├── OAuthToken.cs
│   │   │   │   ├── OAuthTokenRefreshService.cs
│   │   │   │   └── ProviderCredentialResolver.cs
│   │   │   ├── AppConfigJsonOptions.cs
│   │   │   ├── ConfigDtoMapper.cs
│   │   │   ├── ConfigJsonSchemaGenerator.cs
│   │   │   ├── ConfigurationPipeline.cs
│   │   │   ├── ConfigValidationHelper.cs
│   │   │   ├── ConfigValidatorCli.cs
│   │   │   ├── ConfigWatcher.cs
│   │   │   ├── DeploymentContext.cs
│   │   │   ├── IConfigValidator.cs
│   │   │   ├── SensitiveValueMasker.cs
│   │   │   └── StorageConfigExtensions.cs
│   │   ├── Coordination
│   │   │   ├── ClusterCoordinatorService.cs
│   │   │   ├── CoordinationSnapshot.cs
│   │   │   ├── IClusterCoordinator.cs
│   │   │   ├── ICoordinationStore.cs
│   │   │   ├── ILeaseManager.cs
│   │   │   ├── IScheduledWorkOwnershipService.cs
│   │   │   ├── ISubscriptionOwnershipService.cs
│   │   │   ├── LeaseAcquireResult.cs
│   │   │   ├── LeaseManager.cs
│   │   │   ├── LeaseRecord.cs
│   │   │   ├── ScheduledWorkOwnershipService.cs
│   │   │   ├── SharedStorageCoordinationStore.cs
│   │   │   ├── SplitBrainDetector.cs
│   │   │   └── SubscriptionOwnershipService.cs
│   │   ├── Credentials
│   │   │   └── ICredentialStore.cs
│   │   ├── DirectLending
│   │   │   ├── DailyAccrualWorker.cs
│   │   │   ├── DirectLendingEventRebuilder.cs
│   │   │   ├── DirectLendingOutboxDispatcher.cs
│   │   │   ├── DirectLendingServiceSupport.cs
│   │   │   ├── DirectLendingWorkflowSupport.cs
│   │   │   ├── DirectLendingWorkflowTopics.cs
│   │   │   ├── IDirectLendingCommandService.cs
│   │   │   ├── IDirectLendingQueryService.cs
│   │   │   ├── IDirectLendingService.cs
│   │   │   ├── InMemoryDirectLendingService.cs
│   │   │   ├── InMemoryDirectLendingService.Workflows.cs
│   │   │   ├── PostgresDirectLendingCommandService.cs
│   │   │   ├── PostgresDirectLendingQueryService.cs
│   │   │   └── PostgresDirectLendingService.cs
│   │   ├── Etl
│   │   │   ├── EtlAbstractions.cs
│   │   │   └── EtlServices.cs
│   │   ├── Filters
│   │   │   └── MarketEventFilter.cs
│   │   ├── Http
│   │   │   ├── Endpoints
│   │   │   │   ├── ArchiveMaintenanceEndpoints.cs
│   │   │   │   ├── DataQualityEndpoints.cs
│   │   │   │   ├── PackagingEndpoints.cs
│   │   │   │   └── StatusEndpointHandlers.cs
│   │   │   ├── BackfillCoordinator.cs
│   │   │   ├── ConfigStore.cs
│   │   │   ├── HtmlTemplateLoader.cs
│   │   │   └── HtmlTemplates.cs
│   │   ├── Indicators
│   │   │   └── TechnicalIndicatorService.cs
│   │   ├── Monitoring
│   │   │   ├── Core
│   │   │   │   ├── AlertDispatcher.cs
│   │   │   │   ├── AlertRunbookRegistry.cs
│   │   │   │   ├── HealthCheckAggregator.cs
│   │   │   │   └── SloDefinitionRegistry.cs
│   │   │   ├── DataQuality
│   │   │   │   ├── AnomalyDetector.cs
│   │   │   │   ├── CompletenessScoreCalculator.cs
│   │   │   │   ├── CrossProviderComparisonService.cs
│   │   │   │   ├── DataFreshnessSlaMonitor.cs
│   │   │   │   ├── DataQualityModels.cs
│   │   │   │   ├── DataQualityMonitoringService.cs
│   │   │   │   ├── DataQualityReportGenerator.cs
│   │   │   │   ├── GapAnalyzer.cs
│   │   │   │   ├── IQualityAnalyzer.cs
│   │   │   │   ├── LatencyHistogram.cs
│   │   │   │   ├── LiquidityProfileProvider.cs
│   │   │   │   ├── PriceContinuityChecker.cs
│   │   │   │   └── SequenceErrorTracker.cs
│   │   │   ├── BackpressureAlertService.cs
│   │   │   ├── BadTickFilter.cs
│   │   │   ├── CircuitBreakerStatusService.cs
│   │   │   ├── ClockSkewEstimator.cs
│   │   │   ├── ConnectionHealthMonitor.cs
│   │   │   ├── ConnectionStatusWebhook.cs
│   │   │   ├── DataLossAccounting.cs
│   │   │   ├── DetailedHealthCheck.cs
│   │   │   ├── ErrorRingBuffer.cs
│   │   │   ├── IEventMetrics.cs
│   │   │   ├── Metrics.cs
│   │   │   ├── PrometheusMetrics.cs
│   │   │   ├── ProviderDegradationScorer.cs
│   │   │   ├── ProviderLatencyService.cs
│   │   │   ├── ProviderMetricsStatus.cs
│   │   │   ├── SchemaValidationService.cs
│   │   │   ├── SpreadMonitor.cs
│   │   │   ├── StatusHttpServer.cs
│   │   │   ├── StatusSnapshot.cs
│   │   │   ├── StatusWriter.cs
│   │   │   ├── SystemHealthChecker.cs
│   │   │   ├── TickSizeValidator.cs
│   │   │   ├── TimestampMonotonicityChecker.cs
│   │   │   └── ValidationMetrics.cs
│   │   ├── Pipeline
│   │   │   ├── DeadLetterSink.cs
│   │   │   ├── DroppedEventAuditTrail.cs
│   │   │   ├── DualPathEventPipeline.cs
│   │   │   ├── EventPipeline.cs
│   │   │   ├── FSharpEventValidator.cs
│   │   │   ├── HotPathBatchSerializer.cs
│   │   │   ├── IDedupStore.cs
│   │   │   ├── IEventValidator.cs
│   │   │   ├── IngestionJobService.cs
│   │   │   ├── PersistentDedupLedger.cs
│   │   │   └── SchemaUpcasterRegistry.cs
│   │   ├── Results
│   │   │   ├── ErrorCode.cs
│   │   │   ├── OperationError.cs
│   │   │   └── Result.cs
│   │   ├── Scheduling
│   │   │   ├── BackfillExecutionLog.cs
│   │   │   ├── BackfillSchedule.cs
│   │   │   ├── BackfillScheduleManager.cs
│   │   │   ├── IOperationalScheduler.cs
│   │   │   ├── OperationalScheduler.cs
│   │   │   └── ScheduledBackfillService.cs
│   │   ├── SecurityMaster
│   │   │   ├── ISecurityMasterQueryService.cs
│   │   │   ├── ISecurityMasterService.cs
│   │   │   ├── ISecurityResolver.cs
│   │   │   ├── SecurityEconomicDefinitionAdapter.cs
│   │   │   ├── SecurityMasterAggregateRebuilder.cs
│   │   │   ├── SecurityMasterCsvParser.cs
│   │   │   ├── SecurityMasterImportService.cs
│   │   │   ├── SecurityMasterMapping.cs
│   │   │   ├── SecurityMasterOptionsValidator.cs
│   │   │   ├── SecurityMasterProjectionService.cs
│   │   │   ├── SecurityMasterProjectionWarmupService.cs
│   │   │   ├── SecurityMasterQueryService.cs
│   │   │   ├── SecurityMasterRebuildOrchestrator.cs
│   │   │   ├── SecurityMasterService.cs
│   │   │   └── SecurityResolver.cs
│   │   ├── Services
│   │   │   ├── ApiDocumentationService.cs
│   │   │   ├── AutoConfigurationService.cs
│   │   │   ├── CanonicalSymbolRegistry.cs
│   │   │   ├── CliModeResolver.cs
│   │   │   ├── CoLocationProfileActivator.cs
│   │   │   ├── ConfigEnvironmentOverride.cs
│   │   │   ├── ConfigTemplateGenerator.cs
│   │   │   ├── ConfigurationService.cs
│   │   │   ├── ConfigurationServiceCredentialAdapter.cs
│   │   │   ├── ConfigurationWizard.cs
│   │   │   ├── ConnectivityProbeService.cs
│   │   │   ├── ConnectivityTestService.cs
│   │   │   ├── CredentialValidationService.cs
│   │   │   ├── DailySummaryWebhook.cs
│   │   │   ├── DiagnosticBundleService.cs
│   │   │   ├── DryRunService.cs
│   │   │   ├── ErrorTracker.cs
│   │   │   ├── FriendlyErrorFormatter.cs
│   │   │   ├── GovernanceExceptionService.cs
│   │   │   ├── GracefulShutdownHandler.cs
│   │   │   ├── GracefulShutdownService.cs
│   │   │   ├── HistoricalDataQueryService.cs
│   │   │   ├── NavAttributionService.cs
│   │   │   ├── OptionsChainService.cs
│   │   │   ├── PluginLoaderService.cs
│   │   │   ├── PreflightChecker.cs
│   │   │   ├── ProgressDisplayService.cs
│   │   │   ├── ReconciliationEngineService.cs
│   │   │   ├── ReportGenerationService.cs
│   │   │   ├── SampleDataGenerator.cs
│   │   │   ├── ServiceRegistry.cs
│   │   │   ├── StartupSummary.cs
│   │   │   └── TradingCalendar.cs
│   │   ├── Subscriptions
│   │   │   ├── Services
│   │   │   │   ├── AutoResubscribePolicy.cs
│   │   │   │   ├── BatchOperationsService.cs
│   │   │   │   ├── IndexSubscriptionService.cs
│   │   │   │   ├── MetadataEnrichmentService.cs
│   │   │   │   ├── PortfolioImportService.cs
│   │   │   │   ├── SchedulingService.cs
│   │   │   │   ├── SymbolImportExportService.cs
│   │   │   │   ├── SymbolManagementService.cs
│   │   │   │   ├── SymbolSearchService.cs
│   │   │   │   ├── TemplateService.cs
│   │   │   │   └── WatchlistService.cs
│   │   │   └── SubscriptionOrchestrator.cs
│   │   ├── Testing
│   │   │   └── DepthBufferSelfTests.cs
│   │   ├── Tracing
│   │   │   ├── EventTraceContext.cs
│   │   │   ├── OpenTelemetrySetup.cs
│   │   │   └── TracedEventMetrics.cs
│   │   ├── Wizard
│   │   │   ├── Core
│   │   │   │   ├── IWizardStep.cs
│   │   │   │   ├── WizardContext.cs
│   │   │   │   ├── WizardCoordinator.cs
│   │   │   │   ├── WizardStepId.cs
│   │   │   │   ├── WizardStepResult.cs
│   │   │   │   ├── WizardStepStatus.cs
│   │   │   │   ├── WizardSummary.cs
│   │   │   │   └── WizardTransition.cs
│   │   │   ├── Metadata
│   │   │   │   ├── ProviderDescriptor.cs
│   │   │   │   └── ProviderRegistry.cs
│   │   │   ├── Steps
│   │   │   │   ├── ConfigureBackfillStep.cs
│   │   │   │   ├── ConfigureDataSourceStep.cs
│   │   │   │   ├── ConfigureStorageStep.cs
│   │   │   │   ├── ConfigureSymbolsStep.cs
│   │   │   │   ├── CredentialGuidanceStep.cs
│   │   │   │   ├── DetectProvidersStep.cs
│   │   │   │   ├── ReviewConfigurationStep.cs
│   │   │   │   ├── SaveConfigurationStep.cs
│   │   │   │   ├── SelectUseCaseStep.cs
│   │   │   │   └── ValidateCredentialsStep.cs
│   │   │   └── WizardWorkflowFactory.cs
│   │   ├── GlobalUsings.cs
│   │   └── Meridian.Application.csproj
│   ├── Meridian.Backtesting
│   │   ├── Engine
│   │   │   ├── BacktestContext.cs
│   │   │   ├── BacktestEngine.cs
│   │   │   ├── ContingentOrderManager.cs
│   │   │   ├── MultiSymbolMergeEnumerator.cs
│   │   │   └── UniverseDiscovery.cs
│   │   ├── FillModels
│   │   │   ├── BarMidpointFillModel.cs
│   │   │   ├── IFillModel.cs
│   │   │   ├── MarketImpactFillModel.cs
│   │   │   ├── OrderBookFillModel.cs
│   │   │   └── OrderFillResult.cs
│   │   ├── Metrics
│   │   │   ├── BacktestMetricsEngine.cs
│   │   │   ├── PostSimulationTcaReporter.cs
│   │   │   └── XirrCalculator.cs
│   │   ├── Plugins
│   │   │   └── StrategyPluginLoader.cs
│   │   ├── Portfolio
│   │   │   ├── ICommissionModel.cs
│   │   │   └── SimulatedPortfolio.cs
│   │   ├── BatchBacktestService.cs
│   │   ├── CorporateActionAdjustmentService.cs
│   │   ├── GlobalUsings.cs
│   │   ├── ICorporateActionAdjustmentService.cs
│   │   └── Meridian.Backtesting.csproj
│   ├── Meridian.Backtesting.Sdk
│   │   ├── Ledger
│   │   │   ├── BacktestLedger.cs
│   │   │   ├── JournalEntry.cs
│   │   │   ├── LedgerAccount.cs
│   │   │   ├── LedgerAccounts.cs
│   │   │   ├── LedgerAccountType.cs
│   │   │   └── LedgerEntry.cs
│   │   ├── AssetEvent.cs
│   │   ├── BacktestEngineMode.cs
│   │   ├── BacktestProgressEvent.cs
│   │   ├── BacktestRequest.cs
│   │   ├── BacktestResult.cs
│   │   ├── CashFlowEntry.cs
│   │   ├── FillEvent.cs
│   │   ├── FinancialAccount.cs
│   │   ├── FinancialAccountSnapshot.cs
│   │   ├── GlobalUsings.cs
│   │   ├── IBacktestContext.cs
│   │   ├── IBacktestStrategy.cs
│   │   ├── Meridian.Backtesting.Sdk.csproj
│   │   ├── Order.cs
│   │   ├── PortfolioSnapshot.cs
│   │   ├── Position.cs
│   │   ├── StrategyParameterAttribute.cs
│   │   ├── TcaReportModels.cs
│   │   └── TradeTicket.cs
│   ├── Meridian.Contracts
│   │   ├── Api
│   │   │   ├── Quality
│   │   │   │   └── QualityApiModels.cs
│   │   │   ├── BackfillApiModels.cs
│   │   │   ├── ClientModels.cs
│   │   │   ├── ErrorResponse.cs
│   │   │   ├── LiveDataModels.cs
│   │   │   ├── OptionsModels.cs
│   │   │   ├── ProviderCatalog.cs
│   │   │   ├── StatusEndpointModels.cs
│   │   │   ├── StatusModels.cs
│   │   │   ├── UiApiClient.cs
│   │   │   ├── UiApiRoutes.cs
│   │   │   └── UiDashboardModels.cs
│   │   ├── Archive
│   │   │   └── ArchiveHealthModels.cs
│   │   ├── Auth
│   │   │   ├── RolePermissions.cs
│   │   │   ├── UserPermission.cs
│   │   │   └── UserRole.cs
│   │   ├── Backfill
│   │   │   └── BackfillProgress.cs
│   │   ├── Banking
│   │   │   └── BankingModels.cs
│   │   ├── Catalog
│   │   │   ├── DirectoryIndex.cs
│   │   │   ├── ICanonicalSymbolRegistry.cs
│   │   │   ├── StorageCatalog.cs
│   │   │   └── SymbolRegistry.cs
│   │   ├── Configuration
│   │   │   ├── AppConfigDto.cs
│   │   │   ├── DerivativesConfigDto.cs
│   │   │   └── SymbolConfig.cs
│   │   ├── Credentials
│   │   │   ├── CredentialModels.cs
│   │   │   └── ISecretProvider.cs
│   │   ├── DirectLending
│   │   │   ├── DirectLendingCommandResults.cs
│   │   │   ├── DirectLendingDtos.cs
│   │   │   ├── DirectLendingOptions.cs
│   │   │   └── DirectLendingWorkflowDtos.cs
│   │   ├── Domain
│   │   │   ├── Enums
│   │   │   │   ├── AggressorSide.cs
│   │   │   │   ├── CanonicalTradeCondition.cs
│   │   │   │   ├── ConnectionStatus.cs
│   │   │   │   ├── DepthIntegrityKind.cs
│   │   │   │   ├── DepthOperation.cs
│   │   │   │   ├── InstrumentType.cs
│   │   │   │   ├── IntegritySeverity.cs
│   │   │   │   ├── LiquidityProfile.cs
│   │   │   │   ├── MarketEventTier.cs
│   │   │   │   ├── MarketEventType.cs
│   │   │   │   ├── MarketState.cs
│   │   │   │   ├── OptionRight.cs
│   │   │   │   ├── OptionStyle.cs
│   │   │   │   ├── OrderBookSide.cs
│   │   │   │   └── OrderSide.cs
│   │   │   ├── Events
│   │   │   │   ├── IMarketEventPayload.cs
│   │   │   │   ├── MarketEvent.cs
│   │   │   │   └── MarketEventPayload.cs
│   │   │   ├── Models
│   │   │   │   ├── AdjustedHistoricalBar.cs
│   │   │   │   ├── AggregateBarPayload.cs
│   │   │   │   ├── BboQuotePayload.cs
│   │   │   │   ├── DepthIntegrityEvent.cs
│   │   │   │   ├── GreeksSnapshot.cs
│   │   │   │   ├── HistoricalAuction.cs
│   │   │   │   ├── HistoricalBar.cs
│   │   │   │   ├── HistoricalQuote.cs
│   │   │   │   ├── HistoricalTrade.cs
│   │   │   │   ├── IntegrityEvent.cs
│   │   │   │   ├── L2SnapshotPayload.cs
│   │   │   │   ├── LOBSnapshot.cs
│   │   │   │   ├── MarketQuoteUpdate.cs
│   │   │   │   ├── OpenInterestUpdate.cs
│   │   │   │   ├── OptionChainSnapshot.cs
│   │   │   │   ├── OptionContractSpec.cs
│   │   │   │   ├── OptionQuote.cs
│   │   │   │   ├── OptionTrade.cs
│   │   │   │   ├── OrderAdd.cs
│   │   │   │   ├── OrderBookLevel.cs
│   │   │   │   ├── OrderCancel.cs
│   │   │   │   ├── OrderExecute.cs
│   │   │   │   ├── OrderFlowStatistics.cs
│   │   │   │   ├── OrderModify.cs
│   │   │   │   ├── OrderReplace.cs
│   │   │   │   └── Trade.cs
│   │   │   ├── CanonicalSymbol.cs
│   │   │   ├── MarketDataModels.cs
│   │   │   ├── ProviderId.cs
│   │   │   ├── StreamId.cs
│   │   │   ├── SubscriptionId.cs
│   │   │   ├── SymbolId.cs
│   │   │   └── VenueCode.cs
│   │   ├── Etl
│   │   │   └── EtlModels.cs
│   │   ├── Export
│   │   │   ├── AnalysisExportModels.cs
│   │   │   ├── ExportPreset.cs
│   │   │   └── StandardPresets.cs
│   │   ├── FundStructure
│   │   │   ├── FundStructureCommands.cs
│   │   │   ├── FundStructureDtos.cs
│   │   │   └── FundStructureQueries.cs
│   │   ├── Manifest
│   │   │   └── DataManifest.cs
│   │   ├── Pipeline
│   │   │   ├── IngestionJob.cs
│   │   │   └── PipelinePolicyConstants.cs
│   │   ├── Schema
│   │   │   ├── EventSchema.cs
│   │   │   └── ISchemaUpcaster.cs
│   │   ├── SecurityMaster
│   │   │   ├── ISecurityMasterAmender.cs
│   │   │   ├── ISecurityMasterQueryService.cs
│   │   │   ├── ISecurityMasterService.cs
│   │   │   ├── SecurityCommands.cs
│   │   │   ├── SecurityDtos.cs
│   │   │   ├── SecurityEvents.cs
│   │   │   ├── SecurityIdentifiers.cs
│   │   │   ├── SecurityMasterOptions.cs
│   │   │   └── SecurityQueries.cs
│   │   ├── Services
│   │   │   └── IConnectivityProbeService.cs
│   │   ├── Session
│   │   │   └── CollectionSession.cs
│   │   ├── Store
│   │   │   └── MarketDataQuery.cs
│   │   ├── Workstation
│   │   │   ├── ReconciliationDtos.cs
│   │   │   ├── SecurityMasterWorkstationDtos.cs
│   │   │   └── StrategyRunReadModels.cs
│   │   └── Meridian.Contracts.csproj
│   ├── Meridian.Core
│   │   ├── Config
│   │   │   ├── AlpacaOptions.cs
│   │   │   ├── AppConfig.cs
│   │   │   ├── BackfillConfig.cs
│   │   │   ├── CanonicalizationConfig.cs
│   │   │   ├── CoordinationConfig.cs
│   │   │   ├── DataSourceConfig.cs
│   │   │   ├── DataSourceKind.cs
│   │   │   ├── DataSourceKindConverter.cs
│   │   │   ├── DerivativesConfig.cs
│   │   │   ├── IConfigurationProvider.cs
│   │   │   ├── StockSharpConfig.cs
│   │   │   ├── SyntheticMarketDataConfig.cs
│   │   │   └── ValidatedConfig.cs
│   │   ├── Exceptions
│   │   │   ├── ConfigurationException.cs
│   │   │   ├── ConnectionException.cs
│   │   │   ├── DataProviderException.cs
│   │   │   ├── MeridianException.cs
│   │   │   ├── OperationTimeoutException.cs
│   │   │   ├── RateLimitException.cs
│   │   │   ├── SequenceValidationException.cs
│   │   │   ├── StorageException.cs
│   │   │   └── ValidationException.cs
│   │   ├── Logging
│   │   │   └── LoggingSetup.cs
│   │   ├── Monitoring
│   │   │   ├── Core
│   │   │   │   ├── IAlertDispatcher.cs
│   │   │   │   └── IHealthCheckProvider.cs
│   │   │   ├── EventSchemaValidator.cs
│   │   │   ├── IConnectionHealthMonitor.cs
│   │   │   ├── IReconnectionMetrics.cs
│   │   │   └── MigrationDiagnostics.cs
│   │   ├── Performance
│   │   │   └── Performance
│   │   │       ├── ConnectionWarmUp.cs
│   │   │       ├── RawQuoteEvent.cs
│   │   │       ├── RawTradeEvent.cs
│   │   │       ├── SpscRingBuffer.cs
│   │   │       ├── SymbolTable.cs
│   │   │       └── ThreadingUtilities.cs
│   │   ├── Pipeline
│   │   │   └── EventPipelinePolicy.cs
│   │   ├── Scheduling
│   │   │   └── CronExpressionParser.cs
│   │   ├── Serialization
│   │   │   ├── MarketDataJsonContext.cs
│   │   │   └── SecurityMasterJsonContext.cs
│   │   ├── Services
│   │   │   └── IFlushable.cs
│   │   ├── Subscriptions
│   │   │   └── Models
│   │   │       ├── BatchOperations.cs
│   │   │       ├── BulkImportExport.cs
│   │   │       ├── IndexComponents.cs
│   │   │       ├── PortfolioImport.cs
│   │   │       ├── ResubscriptionMetrics.cs
│   │   │       ├── SubscriptionSchedule.cs
│   │   │       ├── SymbolMetadata.cs
│   │   │       ├── SymbolSearchResult.cs
│   │   │       ├── SymbolTemplate.cs
│   │   │       └── Watchlist.cs
│   │   ├── GlobalUsings.cs
│   │   └── Meridian.Core.csproj
│   ├── Meridian.Domain
│   │   ├── Collectors
│   │   │   ├── IQuoteStateStore.cs
│   │   │   ├── L3OrderBookCollector.cs
│   │   │   ├── MarketDepthCollector.cs
│   │   │   ├── OptionDataCollector.cs
│   │   │   ├── QuoteCollector.cs
│   │   │   ├── SymbolSubscriptionTracker.cs
│   │   │   └── TradeDataCollector.cs
│   │   ├── Events
│   │   │   ├── Publishers
│   │   │   │   └── CompositePublisher.cs
│   │   │   ├── IBackpressureSignal.cs
│   │   │   ├── IMarketEventPublisher.cs
│   │   │   ├── MarketEvent.cs
│   │   │   ├── MarketEventPayload.cs
│   │   │   └── PublishResult.cs
│   │   ├── Models
│   │   │   ├── AggregateBar.cs
│   │   │   ├── MarketDepthUpdate.cs
│   │   │   └── MarketTradeUpdate.cs
│   │   ├── Telemetry
│   │   │   └── MarketEventIngressTracing.cs
│   │   ├── BannedReferences.txt
│   │   ├── GlobalUsings.cs
│   │   └── Meridian.Domain.csproj
│   ├── Meridian.Execution
│   │   ├── Adapters
│   │   │   ├── BaseBrokerageGateway.cs
│   │   │   ├── BrokerageGatewayAdapter.cs
│   │   │   └── PaperTradingGateway.cs
│   │   ├── Exceptions
│   │   │   └── UnsupportedOrderRequestException.cs
│   │   ├── Interfaces
│   │   │   ├── IExecutionContext.cs
│   │   │   ├── ILiveFeedAdapter.cs
│   │   │   └── IOrderGateway.cs
│   │   ├── Models
│   │   │   ├── ExecutionMode.cs
│   │   │   ├── ExecutionPosition.cs
│   │   │   ├── IPortfolioState.cs
│   │   │   ├── OrderAcknowledgement.cs
│   │   │   ├── OrderGatewayCapabilities.cs
│   │   │   ├── OrderStatus.cs
│   │   │   └── OrderStatusUpdate.cs
│   │   ├── Services
│   │   │   ├── OrderLifecycleManager.cs
│   │   │   ├── PaperSessionPersistenceService.cs
│   │   │   └── PaperTradingPortfolio.cs
│   │   ├── BrokerageServiceRegistration.cs
│   │   ├── GlobalUsings.cs
│   │   ├── IRiskValidator.cs
│   │   ├── Meridian.Execution.csproj
│   │   ├── OrderManagementSystem.cs
│   │   ├── PaperExecutionContext.cs
│   │   └── PaperTradingGateway.cs
│   ├── Meridian.Execution.Sdk
│   │   ├── BrokerageConfiguration.cs
│   │   ├── IBrokerageGateway.cs
│   │   ├── IExecutionGateway.cs
│   │   ├── IOrderManager.cs
│   │   ├── IPositionTracker.cs
│   │   ├── Meridian.Execution.Sdk.csproj
│   │   └── Models.cs
│   ├── Meridian.FSharp
│   │   ├── Calculations
│   │   │   ├── Aggregations.fs
│   │   │   ├── Imbalance.fs
│   │   │   └── Spread.fs
│   │   ├── Canonicalization
│   │   │   └── MappingRules.fs
│   │   ├── Domain
│   │   │   ├── CashFlowProjection.fs
│   │   │   ├── CashFlowRules.fs
│   │   │   ├── DirectLending.fs
│   │   │   ├── FundStructure.fs
│   │   │   ├── Integrity.fs
│   │   │   ├── MarketEvents.fs
│   │   │   ├── SecMasterDomain.fs
│   │   │   ├── SecurityClassification.fs
│   │   │   ├── SecurityEconomicDefinition.fs
│   │   │   ├── SecurityIdentifiers.fs
│   │   │   ├── SecurityMaster.fs
│   │   │   ├── SecurityMasterCommands.fs
│   │   │   ├── SecurityMasterEvents.fs
│   │   │   ├── SecurityMasterLegacyUpgrade.fs
│   │   │   ├── SecurityTermModules.fs
│   │   │   └── Sides.fs
│   │   ├── Generated
│   │   │   └── Meridian.FSharp.Interop.g.cs
│   │   ├── Pipeline
│   │   │   └── Transforms.fs
│   │   ├── Promotion
│   │   │   ├── PromotionPolicy.fs
│   │   │   └── PromotionTypes.fs
│   │   ├── Risk
│   │   │   ├── RiskEvaluation.fs
│   │   │   ├── RiskRules.fs
│   │   │   └── RiskTypes.fs
│   │   ├── Validation
│   │   │   ├── QuoteValidator.fs
│   │   │   ├── TradeValidator.fs
│   │   │   ├── ValidationPipeline.fs
│   │   │   └── ValidationTypes.fs
│   │   ├── Interop.CashFlow.fs
│   │   ├── Interop.DirectLending.fs
│   │   ├── Interop.fs
│   │   ├── Interop.SecurityMaster.fs
│   │   └── Meridian.FSharp.fsproj
│   ├── Meridian.FSharp.DirectLending.Aggregates
│   │   ├── AggregateTypes.fs
│   │   ├── ContractAggregate.fs
│   │   ├── Interop.fs
│   │   ├── Meridian.FSharp.DirectLending.Aggregates.fsproj
│   │   └── ServicingAggregate.fs
│   ├── Meridian.FSharp.Ledger
│   │   ├── Interop.fs
│   │   ├── JournalValidation.fs
│   │   ├── LedgerReadModels.fs
│   │   ├── LedgerTypes.fs
│   │   ├── Meridian.FSharp.Ledger.fsproj
│   │   ├── Posting.fs
│   │   ├── Reconciliation.fs
│   │   ├── ReconciliationRules.fs
│   │   └── ReconciliationTypes.fs
│   ├── Meridian.FSharp.Trading
│   │   ├── Interop.fs
│   │   ├── Meridian.FSharp.Trading.fsproj
│   │   ├── PromotionReadiness.fs
│   │   ├── StrategyLifecycleState.fs
│   │   ├── StrategyLifecycleTransitions.fs
│   │   └── StrategyRunTypes.fs
│   ├── Meridian.IbApi.SmokeStub
│   │   ├── IBApiSmokeStub.cs
│   │   └── Meridian.IbApi.SmokeStub.csproj
│   ├── Meridian.Infrastructure
│   │   ├── Adapters
│   │   │   ├── Alpaca
│   │   │   │   ├── AlpacaBrokerageGateway.cs
│   │   │   │   ├── AlpacaConstants.cs
│   │   │   │   ├── AlpacaHistoricalDataProvider.cs
│   │   │   │   ├── AlpacaMarketDataClient.cs
│   │   │   │   └── AlpacaSymbolSearchProviderRefactored.cs
│   │   │   ├── AlphaVantage
│   │   │   │   └── AlphaVantageHistoricalDataProvider.cs
│   │   │   ├── Core
│   │   │   │   ├── Backfill
│   │   │   │   │   ├── BackfillJob.cs
│   │   │   │   │   ├── BackfillJobManager.cs
│   │   │   │   │   ├── BackfillRequestQueue.cs
│   │   │   │   │   ├── BackfillWorkerService.cs
│   │   │   │   │   └── PriorityBackfillQueue.cs
│   │   │   │   ├── GapAnalysis
│   │   │   │   │   ├── DataGapAnalyzer.cs
│   │   │   │   │   ├── DataGapRepair.cs
│   │   │   │   │   └── DataQualityMonitor.cs
│   │   │   │   ├── RateLimiting
│   │   │   │   │   ├── ProviderRateLimitTracker.cs
│   │   │   │   │   └── RateLimiter.cs
│   │   │   │   ├── SymbolResolution
│   │   │   │   │   └── ISymbolResolver.cs
│   │   │   │   ├── BackfillProgressTracker.cs
│   │   │   │   ├── BaseHistoricalDataProvider.cs
│   │   │   │   ├── BaseSymbolSearchProvider.cs
│   │   │   │   ├── CompositeHistoricalDataProvider.cs
│   │   │   │   ├── IHistoricalDataProvider.cs
│   │   │   │   ├── ISymbolSearchProvider.cs
│   │   │   │   ├── ProviderFactory.cs
│   │   │   │   ├── ProviderRegistry.cs
│   │   │   │   ├── ProviderServiceExtensions.cs
│   │   │   │   ├── ProviderSubscriptionRanges.cs
│   │   │   │   ├── ProviderTemplate.cs
│   │   │   │   ├── ResponseHandler.cs
│   │   │   │   ├── SymbolSearchUtility.cs
│   │   │   │   └── WebSocketProviderBase.cs
│   │   │   ├── Failover
│   │   │   │   ├── FailoverAwareMarketDataClient.cs
│   │   │   │   ├── StreamingFailoverRegistry.cs
│   │   │   │   └── StreamingFailoverService.cs
│   │   │   ├── Finnhub
│   │   │   │   ├── FinnhubConstants.cs
│   │   │   │   ├── FinnhubHistoricalDataProvider.cs
│   │   │   │   └── FinnhubSymbolSearchProviderRefactored.cs
│   │   │   ├── Fred
│   │   │   │   └── FredHistoricalDataProvider.cs
│   │   │   ├── InteractiveBrokers
│   │   │   │   ├── ContractFactory.cs
│   │   │   │   ├── EnhancedIBConnectionManager.cs
│   │   │   │   ├── EnhancedIBConnectionManager.IBApi.cs
│   │   │   │   ├── IBApiLimits.cs
│   │   │   │   ├── IBApiVersionValidator.cs
│   │   │   │   ├── IBBrokerageGateway.cs
│   │   │   │   ├── IBBuildGuidance.cs
│   │   │   │   ├── IBCallbackRouter.cs
│   │   │   │   ├── IBConnectionManager.cs
│   │   │   │   ├── IBHistoricalDataProvider.cs
│   │   │   │   ├── IBMarketDataClient.cs
│   │   │   │   └── IBSimulationClient.cs
│   │   │   ├── NasdaqDataLink
│   │   │   │   └── NasdaqDataLinkHistoricalDataProvider.cs
│   │   │   ├── NYSE
│   │   │   │   ├── NYSEDataSource.cs
│   │   │   │   ├── NyseMarketDataClient.cs
│   │   │   │   ├── NyseNationalTradesCsvParser.cs
│   │   │   │   ├── NYSEOptions.cs
│   │   │   │   └── NYSEServiceExtensions.cs
│   │   │   ├── OpenFigi
│   │   │   │   ├── OpenFigiClient.cs
│   │   │   │   └── OpenFigiSymbolResolver.cs
│   │   │   ├── Polygon
│   │   │   │   ├── ITradingParametersBackfillService.cs
│   │   │   │   ├── PolygonConstants.cs
│   │   │   │   ├── PolygonCorporateActionFetcher.cs
│   │   │   │   ├── PolygonHistoricalDataProvider.cs
│   │   │   │   ├── PolygonMarketDataClient.cs
│   │   │   │   ├── PolygonSymbolSearchProvider.cs
│   │   │   │   └── TradingParametersBackfillService.cs
│   │   │   ├── StockSharp
│   │   │   │   ├── Converters
│   │   │   │   │   ├── MessageConverter.cs
│   │   │   │   │   └── SecurityConverter.cs
│   │   │   │   ├── StockSharpBrokerageGateway.cs
│   │   │   │   ├── StockSharpConnectorCapabilities.cs
│   │   │   │   ├── StockSharpConnectorFactory.cs
│   │   │   │   ├── StockSharpHistoricalDataProvider.cs
│   │   │   │   ├── StockSharpMarketDataClient.cs
│   │   │   │   └── StockSharpSymbolSearchProvider.cs
│   │   │   ├── Stooq
│   │   │   │   └── StooqHistoricalDataProvider.cs
│   │   │   ├── Synthetic
│   │   │   │   ├── SyntheticHistoricalDataProvider.cs
│   │   │   │   ├── SyntheticMarketDataClient.cs
│   │   │   │   └── SyntheticReferenceDataCatalog.cs
│   │   │   ├── Templates
│   │   │   │   └── TemplateBrokerageGateway.cs
│   │   │   ├── Tiingo
│   │   │   │   └── TiingoHistoricalDataProvider.cs
│   │   │   ├── TwelveData
│   │   │   │   └── TwelveDataHistoricalDataProvider.cs
│   │   │   └── YahooFinance
│   │   │       └── YahooFinanceHistoricalDataProvider.cs
│   │   ├── Contracts
│   │   │   ├── ContractVerificationExtensions.cs
│   │   │   └── ContractVerificationService.cs
│   │   ├── DataSources
│   │   │   ├── DataSourceBase.cs
│   │   │   └── DataSourceConfiguration.cs
│   │   ├── Etl
│   │   │   ├── Sftp
│   │   │   │   └── ISftpClientFactory.cs
│   │   │   ├── CsvPartnerFileParser.cs
│   │   │   ├── ISftpFilePublisher.cs
│   │   │   ├── LocalFileSourceReader.cs
│   │   │   ├── SftpFilePublisher.cs
│   │   │   └── SftpFileSourceReader.cs
│   │   ├── Http
│   │   │   ├── HttpClientConfiguration.cs
│   │   │   └── SharedResiliencePolicies.cs
│   │   ├── Resilience
│   │   │   ├── HttpResiliencePolicy.cs
│   │   │   ├── WebSocketConnectionConfig.cs
│   │   │   ├── WebSocketConnectionManager.cs
│   │   │   └── WebSocketResiliencePolicy.cs
│   │   ├── Shared
│   │   │   ├── ISymbolStateStore.cs
│   │   │   ├── SubscriptionManager.cs
│   │   │   ├── TaskSafetyExtensions.cs
│   │   │   └── WebSocketReconnectionHelper.cs
│   │   ├── Utilities
│   │   │   ├── HttpResponseHandler.cs
│   │   │   ├── JsonElementExtensions.cs
│   │   │   └── SymbolNormalization.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.Infrastructure.csproj
│   │   └── NoOpMarketDataClient.cs
│   ├── Meridian.Infrastructure.CppTrader
│   │   ├── Diagnostics
│   │   │   ├── CppTraderSessionDiagnostic.cs
│   │   │   ├── CppTraderSessionDiagnosticsService.cs
│   │   │   ├── CppTraderStatusService.cs
│   │   │   ├── ICppTraderSessionDiagnosticsService.cs
│   │   │   └── ICppTraderStatusService.cs
│   │   ├── Execution
│   │   │   ├── CppTraderLiveFeedAdapter.cs
│   │   │   └── CppTraderOrderGateway.cs
│   │   ├── Host
│   │   │   ├── CppTraderHostManager.cs
│   │   │   ├── ICppTraderHostManager.cs
│   │   │   ├── ICppTraderSessionClient.cs
│   │   │   └── ProcessBackedCppTraderSessionClient.cs
│   │   ├── Options
│   │   │   └── CppTraderOptions.cs
│   │   ├── Protocol
│   │   │   ├── CppTraderProtocolModels.cs
│   │   │   └── LengthPrefixedProtocolStream.cs
│   │   ├── Providers
│   │   │   ├── CppTraderItchIngestionService.cs
│   │   │   ├── CppTraderMarketDataClient.cs
│   │   │   └── ICppTraderItchIngestionService.cs
│   │   ├── Replay
│   │   │   ├── CppTraderReplayService.cs
│   │   │   └── ICppTraderReplayService.cs
│   │   ├── Symbols
│   │   │   ├── CppTraderSymbolMapper.cs
│   │   │   └── ICppTraderSymbolMapper.cs
│   │   ├── Translation
│   │   │   ├── CppTraderExecutionTranslator.cs
│   │   │   ├── CppTraderSnapshotTranslator.cs
│   │   │   ├── ICppTraderExecutionTranslator.cs
│   │   │   └── ICppTraderSnapshotTranslator.cs
│   │   ├── CppTraderServiceCollectionExtensions.cs
│   │   ├── GlobalUsings.cs
│   │   └── Meridian.Infrastructure.CppTrader.csproj
│   ├── Meridian.Ledger
│   │   ├── FundLedgerBook.cs
│   │   ├── GlobalUsings.cs
│   │   ├── IReadOnlyLedger.cs
│   │   ├── JournalEntry.cs
│   │   ├── JournalEntryMetadata.cs
│   │   ├── Ledger.cs
│   │   ├── LedgerAccount.cs
│   │   ├── LedgerAccounts.cs
│   │   ├── LedgerAccountSummary.cs
│   │   ├── LedgerAccountType.cs
│   │   ├── LedgerBalancePoint.cs
│   │   ├── LedgerBookKey.cs
│   │   ├── LedgerEntry.cs
│   │   ├── LedgerQuery.cs
│   │   ├── LedgerSnapshot.cs
│   │   ├── LedgerValidationException.cs
│   │   ├── LedgerViewKind.cs
│   │   ├── Meridian.Ledger.csproj
│   │   └── ProjectLedgerBook.cs
│   ├── Meridian.Mcp
│   │   ├── Prompts
│   │   │   ├── CodeReviewPrompts.cs
│   │   │   ├── ProviderPrompts.cs
│   │   │   └── TestWriterPrompts.cs
│   │   ├── Resources
│   │   │   ├── AdrResources.cs
│   │   │   ├── ConventionResources.cs
│   │   │   └── TemplateResources.cs
│   │   ├── Services
│   │   │   └── RepoPathService.cs
│   │   ├── Tools
│   │   │   ├── AdrTools.cs
│   │   │   ├── AuditTools.cs
│   │   │   ├── ConventionTools.cs
│   │   │   ├── KnownErrorTools.cs
│   │   │   └── ProviderTools.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.Mcp.csproj
│   │   └── Program.cs
│   ├── Meridian.McpServer
│   │   ├── Prompts
│   │   │   └── MarketDataPrompts.cs
│   │   ├── Resources
│   │   │   └── MarketDataResources.cs
│   │   ├── Tools
│   │   │   ├── BackfillTools.cs
│   │   │   ├── ProviderTools.cs
│   │   │   ├── StorageTools.cs
│   │   │   └── SymbolTools.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.McpServer.csproj
│   │   └── Program.cs
│   ├── Meridian.ProviderSdk
│   │   ├── CredentialValidator.cs
│   │   ├── DataSourceAttribute.cs
│   │   ├── DataSourceRegistry.cs
│   │   ├── HistoricalDataCapabilities.cs
│   │   ├── IDataSource.cs
│   │   ├── IHistoricalBarWriter.cs
│   │   ├── IHistoricalDataSource.cs
│   │   ├── IMarketDataClient.cs
│   │   ├── ImplementsAdrAttribute.cs
│   │   ├── IOptionsChainProvider.cs
│   │   ├── IProviderMetadata.cs
│   │   ├── IProviderModule.cs
│   │   ├── IRealtimeDataSource.cs
│   │   ├── Meridian.ProviderSdk.csproj
│   │   └── ProviderHttpUtilities.cs
│   ├── Meridian.Risk
│   │   ├── Rules
│   │   │   ├── DrawdownCircuitBreaker.cs
│   │   │   ├── OrderRateThrottle.cs
│   │   │   └── PositionLimitRule.cs
│   │   ├── CompositeRiskValidator.cs
│   │   ├── IRiskRule.cs
│   │   └── Meridian.Risk.csproj
│   ├── Meridian.Storage
│   │   ├── Archival
│   │   │   ├── ArchivalStorageService.cs
│   │   │   ├── AtomicFileWriter.cs
│   │   │   ├── CompressionProfileManager.cs
│   │   │   ├── SchemaVersionManager.cs
│   │   │   └── WriteAheadLog.cs
│   │   ├── DirectLending
│   │   │   ├── Migrations
│   │   │   │   ├── 001_direct_lending.sql
│   │   │   │   ├── 002_direct_lending_projections.sql
│   │   │   │   ├── 003_direct_lending_accrual_and_event_metadata.sql
│   │   │   │   ├── 004_direct_lending_event_schema_and_snapshots.sql
│   │   │   │   ├── 005_direct_lending_operations.sql
│   │   │   │   └── 005_direct_lending_workflows.sql
│   │   │   ├── DirectLendingMigrationRunner.cs
│   │   │   ├── DirectLendingPersistenceBatch.cs
│   │   │   ├── IDirectLendingOperationsStore.cs
│   │   │   ├── IDirectLendingStateStore.cs
│   │   │   ├── PostgresDirectLendingStateStore.cs
│   │   │   └── PostgresDirectLendingStateStore.Operations.cs
│   │   ├── Etl
│   │   │   └── EtlStores.cs
│   │   ├── Export
│   │   │   ├── AnalysisExportService.cs
│   │   │   ├── AnalysisExportService.Features.cs
│   │   │   ├── AnalysisExportService.Formats.Arrow.cs
│   │   │   ├── AnalysisExportService.Formats.cs
│   │   │   ├── AnalysisExportService.Formats.Parquet.cs
│   │   │   ├── AnalysisExportService.Formats.Xlsx.cs
│   │   │   ├── AnalysisExportService.IO.cs
│   │   │   ├── AnalysisQualityReport.cs
│   │   │   ├── ExportProfile.cs
│   │   │   ├── ExportRequest.cs
│   │   │   ├── ExportResult.cs
│   │   │   ├── ExportValidator.cs
│   │   │   └── ExportVerificationReport.cs
│   │   ├── Interfaces
│   │   │   ├── IMarketDataStore.cs
│   │   │   ├── ISourceRegistry.cs
│   │   │   ├── IStorageCatalogService.cs
│   │   │   ├── IStoragePolicy.cs
│   │   │   ├── IStorageSink.cs
│   │   │   └── ISymbolRegistryService.cs
│   │   ├── Maintenance
│   │   │   ├── ArchiveMaintenanceModels.cs
│   │   │   ├── ArchiveMaintenanceScheduleManager.cs
│   │   │   ├── IArchiveMaintenanceScheduleManager.cs
│   │   │   ├── IArchiveMaintenanceService.cs
│   │   │   ├── IMaintenanceExecutionHistory.cs
│   │   │   └── ScheduledArchiveMaintenanceService.cs
│   │   ├── Packaging
│   │   │   ├── PackageManifest.cs
│   │   │   ├── PackageOptions.cs
│   │   │   ├── PackageResult.cs
│   │   │   ├── PortableDataPackager.Creation.cs
│   │   │   ├── PortableDataPackager.cs
│   │   │   ├── PortableDataPackager.Scripts.cs
│   │   │   ├── PortableDataPackager.Scripts.Import.cs
│   │   │   ├── PortableDataPackager.Scripts.Sql.cs
│   │   │   └── PortableDataPackager.Validation.cs
│   │   ├── Policies
│   │   │   └── JsonlStoragePolicy.cs
│   │   ├── Replay
│   │   │   ├── JsonlReplayer.cs
│   │   │   └── MemoryMappedJsonlReader.cs
│   │   ├── SecurityMaster
│   │   │   ├── Migrations
│   │   │   │   ├── 001_security_master.sql
│   │   │   │   ├── 002_security_master_fts.sql
│   │   │   │   └── 003_security_master_corp_actions.sql
│   │   │   ├── ISecurityMasterEventStore.cs
│   │   │   ├── ISecurityMasterSnapshotStore.cs
│   │   │   ├── ISecurityMasterStore.cs
│   │   │   ├── PostgresSecurityMasterEventStore.cs
│   │   │   ├── PostgresSecurityMasterSnapshotStore.cs
│   │   │   ├── PostgresSecurityMasterStore.cs
│   │   │   ├── SecurityMasterDbMapper.cs
│   │   │   ├── SecurityMasterMigrationRunner.cs
│   │   │   └── SecurityMasterProjectionCache.cs
│   │   ├── Services
│   │   │   ├── AuditChainService.cs
│   │   │   ├── DataLineageService.cs
│   │   │   ├── DataQualityScoringService.cs
│   │   │   ├── DataQualityService.cs
│   │   │   ├── EventBuffer.cs
│   │   │   ├── FileMaintenanceService.cs
│   │   │   ├── FilePermissionsService.cs
│   │   │   ├── LifecyclePolicyEngine.cs
│   │   │   ├── MaintenanceScheduler.cs
│   │   │   ├── MetadataTagService.cs
│   │   │   ├── ParquetConversionService.cs
│   │   │   ├── QuotaEnforcementService.cs
│   │   │   ├── RetentionComplianceReporter.cs
│   │   │   ├── SourceRegistry.cs
│   │   │   ├── StorageCatalogService.cs
│   │   │   ├── StorageChecksumService.cs
│   │   │   ├── StorageSearchService.cs
│   │   │   ├── SymbolRegistryService.cs
│   │   │   └── TierMigrationService.cs
│   │   ├── Sinks
│   │   │   ├── CatalogSyncSink.cs
│   │   │   ├── CompositeSink.cs
│   │   │   ├── JsonlStorageSink.cs
│   │   │   └── ParquetStorageSink.cs
│   │   ├── Store
│   │   │   ├── CompositeMarketDataStore.cs
│   │   │   └── JsonlMarketDataStore.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.Storage.csproj
│   │   ├── StorageOptions.cs
│   │   ├── StorageProfiles.cs
│   │   ├── StorageSinkAttribute.cs
│   │   └── StorageSinkRegistry.cs
│   ├── Meridian.Strategies
│   │   ├── Interfaces
│   │   │   ├── ILiveStrategy.cs
│   │   │   ├── IStrategyLifecycle.cs
│   │   │   └── IStrategyRepository.cs
│   │   ├── Models
│   │   │   ├── RunType.cs
│   │   │   ├── StrategyRunEntry.cs
│   │   │   └── StrategyStatus.cs
│   │   ├── Promotions
│   │   │   └── BacktestToLivePromoter.cs
│   │   ├── Services
│   │   │   ├── CashFlowProjectionService.cs
│   │   │   ├── InMemoryReconciliationRunRepository.cs
│   │   │   ├── IReconciliationRunRepository.cs
│   │   │   ├── IReconciliationRunService.cs
│   │   │   ├── ISecurityReferenceLookup.cs
│   │   │   ├── LedgerReadService.cs
│   │   │   ├── PortfolioReadService.cs
│   │   │   ├── PromotionService.cs
│   │   │   ├── ReconciliationProjectionService.cs
│   │   │   ├── ReconciliationRunService.cs
│   │   │   ├── StrategyLifecycleManager.cs
│   │   │   └── StrategyRunReadService.cs
│   │   ├── Storage
│   │   │   └── StrategyRunStore.cs
│   │   ├── GlobalUsings.cs
│   │   └── Meridian.Strategies.csproj
│   ├── Meridian.Ui
│   │   ├── dashboard
│   │   │   ├── src
│   │   │   │   ├── components
│   │   │   │   │   ├── meridian
│   │   │   │   │   │   ├── command-palette.test.tsx
│   │   │   │   │   │   ├── command-palette.tsx
│   │   │   │   │   │   ├── entity-data-table.test.tsx
│   │   │   │   │   │   ├── entity-data-table.tsx
│   │   │   │   │   │   ├── metric-card.tsx
│   │   │   │   │   │   ├── run-status-badge.tsx
│   │   │   │   │   │   ├── workspace-header.tsx
│   │   │   │   │   │   └── workspace-nav.tsx
│   │   │   │   │   └── ui
│   │   │   │   │       ├── badge.tsx
│   │   │   │   │       ├── button.tsx
│   │   │   │   │       ├── card.tsx
│   │   │   │   │       ├── command.tsx
│   │   │   │   │       ├── dialog.tsx
│   │   │   │   │       └── input.tsx
│   │   │   │   ├── hooks
│   │   │   │   │   └── use-workstation-data.ts
│   │   │   │   ├── lib
│   │   │   │   │   ├── api.ts
│   │   │   │   │   ├── utils.ts
│   │   │   │   │   └── workspace.ts
│   │   │   │   ├── screens
│   │   │   │   │   ├── data-operations-screen.test.tsx
│   │   │   │   │   ├── data-operations-screen.tsx
│   │   │   │   │   ├── governance-screen.test.tsx
│   │   │   │   │   ├── governance-screen.tsx
│   │   │   │   │   ├── research-screen.test.tsx
│   │   │   │   │   ├── research-screen.tsx
│   │   │   │   │   ├── trading-screen.test.tsx
│   │   │   │   │   ├── trading-screen.tsx
│   │   │   │   │   └── workspace-placeholder.tsx
│   │   │   │   ├── styles
│   │   │   │   │   └── index.css
│   │   │   │   ├── test
│   │   │   │   │   └── setup.ts
│   │   │   │   ├── app.tsx
│   │   │   │   ├── main.tsx
│   │   │   │   └── types.ts
│   │   │   ├── index.html
│   │   │   ├── package-lock.json
│   │   │   ├── package.json
│   │   │   ├── postcss.config.cjs
│   │   │   ├── tailwind.config.d.ts
│   │   │   ├── tailwind.config.js
│   │   │   ├── tailwind.config.ts
│   │   │   ├── tsconfig.app.json
│   │   │   ├── tsconfig.app.tsbuildinfo
│   │   │   ├── tsconfig.json
│   │   │   ├── tsconfig.node.json
│   │   │   ├── tsconfig.node.tsbuildinfo
│   │   │   ├── vite.config.d.ts
│   │   │   ├── vite.config.js
│   │   │   └── vite.config.ts
│   │   ├── wwwroot
│   │   │   ├── static
│   │   │   │   └── dashboard.css
│   │   │   └── workstation
│   │   │       ├── assets
│   │   │       │   ├── index-BLxm5sCJ.js
│   │   │       │   └── index-erdiJ_gu.css
│   │   │       └── index.html
│   │   ├── app.manifest
│   │   ├── Meridian.Ui.csproj
│   │   └── Program.cs
│   ├── Meridian.Ui.Services
│   │   ├── Collections
│   │   │   ├── BoundedObservableCollection.cs
│   │   │   └── CircularBuffer.cs
│   │   ├── Contracts
│   │   │   ├── ConnectionTypes.cs
│   │   │   ├── IAdminMaintenanceService.cs
│   │   │   ├── IArchiveHealthService.cs
│   │   │   ├── IBackgroundTaskSchedulerService.cs
│   │   │   ├── IConfigService.cs
│   │   │   ├── ICredentialService.cs
│   │   │   ├── ILoggingService.cs
│   │   │   ├── IMessagingService.cs
│   │   │   ├── INotificationService.cs
│   │   │   ├── IOfflineTrackingPersistenceService.cs
│   │   │   ├── IPendingOperationsQueueService.cs
│   │   │   ├── IRefreshScheduler.cs
│   │   │   ├── ISchemaService.cs
│   │   │   ├── IStatusService.cs
│   │   │   ├── IThemeService.cs
│   │   │   ├── IWatchlistService.cs
│   │   │   └── NavigationTypes.cs
│   │   ├── Services
│   │   │   ├── DataQuality
│   │   │   │   ├── DataQualityApiClient.cs
│   │   │   │   ├── DataQualityModels.cs
│   │   │   │   ├── DataQualityPresentationService.cs
│   │   │   │   ├── DataQualityRefreshService.cs
│   │   │   │   ├── IDataQualityApiClient.cs
│   │   │   │   ├── IDataQualityPresentationService.cs
│   │   │   │   └── IDataQualityRefreshService.cs
│   │   │   ├── ActivityFeedService.cs
│   │   │   ├── AdminMaintenanceModels.cs
│   │   │   ├── AdminMaintenanceServiceBase.cs
│   │   │   ├── AdvancedAnalyticsModels.cs
│   │   │   ├── AdvancedAnalyticsServiceBase.cs
│   │   │   ├── AlertService.cs
│   │   │   ├── AnalysisExportService.cs
│   │   │   ├── AnalysisExportWizardService.cs
│   │   │   ├── ApiClientService.cs
│   │   │   ├── ArchiveBrowserService.cs
│   │   │   ├── ArchiveHealthService.cs
│   │   │   ├── BackendServiceManagerBase.cs
│   │   │   ├── BackfillApiService.cs
│   │   │   ├── BackfillCheckpointService.cs
│   │   │   ├── BackfillProviderConfigService.cs
│   │   │   ├── BackfillService.cs
│   │   │   ├── BatchExportSchedulerService.cs
│   │   │   ├── ChartingService.cs
│   │   │   ├── CollectionSessionService.cs
│   │   │   ├── ColorPalette.cs
│   │   │   ├── CommandPaletteService.cs
│   │   │   ├── ConfigService.cs
│   │   │   ├── ConfigServiceBase.cs
│   │   │   ├── ConnectionServiceBase.cs
│   │   │   ├── CredentialService.cs
│   │   │   ├── DataCalendarService.cs
│   │   │   ├── DataCompletenessService.cs
│   │   │   ├── DataQualityRefreshCoordinator.cs
│   │   │   ├── DataQualityServiceBase.cs
│   │   │   ├── DataSamplingService.cs
│   │   │   ├── DesktopJsonOptions.cs
│   │   │   ├── DiagnosticsService.cs
│   │   │   ├── ErrorHandlingService.cs
│   │   │   ├── ErrorMessages.cs
│   │   │   ├── EventReplayService.cs
│   │   │   ├── ExportPresetServiceBase.cs
│   │   │   ├── FixtureDataService.cs
│   │   │   ├── FixtureModeDetector.cs
│   │   │   ├── FormatHelpers.cs
│   │   │   ├── FormValidationRules.cs
│   │   │   ├── HttpClientConfiguration.cs
│   │   │   ├── InfoBarConstants.cs
│   │   │   ├── IntegrityEventsService.cs
│   │   │   ├── LeanIntegrationService.cs
│   │   │   ├── LiveDataService.cs
│   │   │   ├── LoggingService.cs
│   │   │   ├── LoggingServiceBase.cs
│   │   │   ├── ManifestService.cs
│   │   │   ├── NavigationServiceBase.cs
│   │   │   ├── NotificationService.cs
│   │   │   ├── NotificationServiceBase.cs
│   │   │   ├── OAuthRefreshService.cs
│   │   │   ├── OnboardingTourService.cs
│   │   │   ├── OperationResult.cs
│   │   │   ├── OrderBookVisualizationService.cs
│   │   │   ├── PeriodicRefreshScheduler.cs
│   │   │   ├── PortablePackagerService.cs
│   │   │   ├── PortfolioImportService.cs
│   │   │   ├── ProviderHealthService.cs
│   │   │   ├── ProviderManagementService.cs
│   │   │   ├── QualityArchiveStore.cs
│   │   │   ├── RetentionAssuranceModels.cs
│   │   │   ├── ScheduledMaintenanceService.cs
│   │   │   ├── ScheduleManagerService.cs
│   │   │   ├── SchemaService.cs
│   │   │   ├── SchemaServiceBase.cs
│   │   │   ├── SearchService.cs
│   │   │   ├── SettingsConfigurationService.cs
│   │   │   ├── SetupWizardService.cs
│   │   │   ├── SmartRecommendationsService.cs
│   │   │   ├── StatusServiceBase.cs
│   │   │   ├── StorageAnalyticsService.cs
│   │   │   ├── StorageModels.cs
│   │   │   ├── StorageOptimizationAdvisorService.cs
│   │   │   ├── StorageServiceBase.cs
│   │   │   ├── SymbolGroupService.cs
│   │   │   ├── SymbolManagementService.cs
│   │   │   ├── SymbolMappingService.cs
│   │   │   ├── SystemHealthService.cs
│   │   │   ├── ThemeServiceBase.cs
│   │   │   ├── TimeSeriesAlignmentService.cs
│   │   │   ├── TooltipContent.cs
│   │   │   ├── WatchlistService.cs
│   │   │   └── WorkspaceModels.cs
│   │   ├── GlobalUsings.cs
│   │   └── Meridian.Ui.Services.csproj
│   ├── Meridian.Ui.Shared
│   │   ├── Endpoints
│   │   │   ├── AdminEndpoints.cs
│   │   │   ├── AnalyticsEndpoints.cs
│   │   │   ├── ApiKeyMiddleware.cs
│   │   │   ├── AuthEndpoints.cs
│   │   │   ├── AuthenticationMode.cs
│   │   │   ├── BackfillEndpoints.cs
│   │   │   ├── BackfillScheduleEndpoints.cs
│   │   │   ├── BankingEndpoints.cs
│   │   │   ├── CalendarEndpoints.cs
│   │   │   ├── CanonicalizationEndpoints.cs
│   │   │   ├── CatalogEndpoints.cs
│   │   │   ├── CheckpointEndpoints.cs
│   │   │   ├── ConfigEndpoints.cs
│   │   │   ├── CppTraderEndpoints.cs
│   │   │   ├── CronEndpoints.cs
│   │   │   ├── DiagnosticsEndpoints.cs
│   │   │   ├── DirectLendingEndpoints.cs
│   │   │   ├── EndpointHelpers.cs
│   │   │   ├── ExecutionEndpoints.cs
│   │   │   ├── ExportEndpoints.cs
│   │   │   ├── FailoverEndpoints.cs
│   │   │   ├── HealthEndpoints.cs
│   │   │   ├── HistoricalEndpoints.cs
│   │   │   ├── IBEndpoints.cs
│   │   │   ├── IngestionJobEndpoints.cs
│   │   │   ├── LeanEndpoints.cs
│   │   │   ├── LiveDataEndpoints.cs
│   │   │   ├── LoginSessionMiddleware.cs
│   │   │   ├── MaintenanceScheduleEndpoints.cs
│   │   │   ├── MessagingEndpoints.cs
│   │   │   ├── OptionsEndpoints.cs
│   │   │   ├── PathValidation.cs
│   │   │   ├── PromotionEndpoints.cs
│   │   │   ├── ProviderEndpoints.cs
│   │   │   ├── ProviderExtendedEndpoints.cs
│   │   │   ├── ReplayEndpoints.cs
│   │   │   ├── ResilienceEndpoints.cs
│   │   │   ├── SamplingEndpoints.cs
│   │   │   ├── SecurityMasterEndpoints.cs
│   │   │   ├── StatusEndpoints.cs
│   │   │   ├── StorageEndpoints.cs
│   │   │   ├── StorageQualityEndpoints.cs
│   │   │   ├── StrategyLifecycleEndpoints.cs
│   │   │   ├── SubscriptionEndpoints.cs
│   │   │   ├── SymbolEndpoints.cs
│   │   │   ├── SymbolMappingEndpoints.cs
│   │   │   ├── UiEndpoints.cs
│   │   │   └── WorkstationEndpoints.cs
│   │   ├── Services
│   │   │   ├── BackfillCoordinator.cs
│   │   │   ├── ConfigStore.cs
│   │   │   └── SecurityMasterSecurityReferenceLookup.cs
│   │   ├── DtoExtensions.cs
│   │   ├── GlobalUsings.cs
│   │   ├── HtmlTemplateGenerator.cs
│   │   ├── HtmlTemplateGenerator.Login.cs
│   │   ├── HtmlTemplateGenerator.Scripts.cs
│   │   ├── HtmlTemplateGenerator.Styles.cs
│   │   ├── LeanAutoExportService.cs
│   │   ├── LeanSymbolMapper.cs
│   │   ├── LoginSessionService.cs
│   │   ├── Meridian.Ui.Shared.csproj
│   │   └── UserProfileRegistry.cs
│   └── Meridian.Wpf
│       ├── Contracts
│       │   ├── IConnectionService.cs
│       │   └── INavigationService.cs
│       ├── Converters
│       │   ├── BoolToStringConverter.cs
│       │   ├── BoolToVisibilityConverter.cs
│       │   ├── InvertBoolConverter.cs
│       │   └── NullToCollapsedConverter.cs
│       ├── Models
│       │   ├── ActionEntry.cs
│       │   ├── ActivityLogModels.cs
│       │   ├── AppConfig.cs
│       │   ├── BackfillModels.cs
│       │   ├── DashboardModels.cs
│       │   ├── DataQualityModels.cs
│       │   ├── LeanModels.cs
│       │   ├── LiveDataModels.cs
│       │   ├── NotificationModels.cs
│       │   ├── OrderBookModels.cs
│       │   ├── PaneLayout.cs
│       │   ├── ProviderHealthModels.cs
│       │   ├── StorageDisplayModels.cs
│       │   ├── SymbolsModels.cs
│       │   ├── WorkspaceDefinition.cs
│       │   ├── WorkspaceRegistry.cs
│       │   └── WorkspaceShellModels.cs
│       ├── Services
│       │   ├── AgentLoopService.cs
│       │   ├── ArchiveHealthService.cs
│       │   ├── BackendServiceManager.cs
│       │   ├── BackgroundTaskSchedulerService.cs
│       │   ├── BacktestService.cs
│       │   ├── BrushRegistry.cs
│       │   ├── ClipboardWatcherService.cs
│       │   ├── ConfigService.cs
│       │   ├── ConnectionService.cs
│       │   ├── ContextMenuService.cs
│       │   ├── CredentialService.cs
│       │   ├── DropImportService.cs
│       │   ├── ExportFormat.cs
│       │   ├── ExportPresetService.cs
│       │   ├── FirstRunService.cs
│       │   ├── FormValidationService.cs
│       │   ├── GlobalHotkeyService.cs
│       │   ├── ICommandContextProvider.cs
│       │   ├── InfoBarService.cs
│       │   ├── JumpListService.cs
│       │   ├── KeyboardShortcutService.cs
│       │   ├── LoggingService.cs
│       │   ├── MessagingService.cs
│       │   ├── NavigationService.cs
│       │   ├── NotificationService.cs
│       │   ├── OfflineTrackingPersistenceService.cs
│       │   ├── PendingOperationsQueueService.cs
│       │   ├── RetentionAssuranceService.cs
│       │   ├── RunMatService.cs
│       │   ├── SchemaService.cs
│       │   ├── SingleInstanceService.cs
│       │   ├── StatusService.cs
│       │   ├── StorageService.cs
│       │   ├── StrategyRunWorkspaceService.cs
│       │   ├── SystemTrayService.cs
│       │   ├── TaskbarProgressService.cs
│       │   ├── TearOffPanelService.cs
│       │   ├── ThemeService.cs
│       │   ├── TickerStripService.cs
│       │   ├── ToastNotificationService.cs
│       │   ├── TooltipService.cs
│       │   ├── TypeForwards.cs
│       │   ├── WatchlistService.cs
│       │   └── WorkspaceService.cs
│       ├── Styles
│       │   ├── Animations.xaml
│       │   ├── AppStyles.xaml
│       │   ├── IconResources.xaml
│       │   ├── ThemeControls.xaml
│       │   ├── ThemeSurfaces.xaml
│       │   ├── ThemeTokens.xaml
│       │   └── ThemeTypography.xaml
│       ├── ViewModels
│       │   ├── ActivityLogViewModel.cs
│       │   ├── AgentViewModel.cs
│       │   ├── BackfillViewModel.cs
│       │   ├── BacktestViewModel.cs
│       │   ├── BatchBacktestViewModel.cs
│       │   ├── BindableBase.cs
│       │   ├── ChartingPageViewModel.cs
│       │   ├── ClusterStatusViewModel.cs
│       │   ├── DashboardViewModel.cs
│       │   ├── DataQualityViewModel.cs
│       │   ├── DiagnosticsPageViewModel.cs
│       │   ├── DirectLendingViewModel.cs
│       │   ├── ExportPresetsViewModel.cs
│       │   ├── IPageActionBarProvider.cs
│       │   ├── LeanIntegrationViewModel.cs
│       │   ├── LiveDataViewerViewModel.cs
│       │   ├── MainPageViewModel.cs
│       │   ├── NotificationCenterViewModel.cs
│       │   ├── OrderBookHeatmapViewModel.cs
│       │   ├── OrderBookViewModel.cs
│       │   ├── PluginManagementViewModel.cs
│       │   ├── ProviderHealthViewModel.cs
│       │   ├── ProviderPageModels.cs
│       │   ├── QualityArchiveViewModel.cs
│       │   ├── QuoteFloatViewModel.cs
│       │   ├── RunMatViewModel.cs
│       │   ├── SecurityMasterDeactivateViewModel.cs
│       │   ├── SecurityMasterEditViewModel.cs
│       │   ├── SecurityMasterViewModel.cs
│       │   ├── ServiceManagerViewModel.cs
│       │   ├── SplitPaneViewModel.cs
│       │   ├── StatusBarViewModel.cs
│       │   ├── StrategyRunBrowserViewModel.cs
│       │   ├── StrategyRunDetailViewModel.cs
│       │   ├── StrategyRunLedgerViewModel.cs
│       │   ├── StrategyRunPortfolioViewModel.cs
│       │   ├── SymbolsPageViewModel.cs
│       │   └── TickerStripViewModel.cs
│       ├── Views
│       │   ├── ActivityLogPage.xaml
│       │   ├── ActivityLogPage.xaml.cs
│       │   ├── AddProviderWizardPage.xaml
│       │   ├── AddProviderWizardPage.xaml.cs
│       │   ├── AdminMaintenancePage.xaml
│       │   ├── AdminMaintenancePage.xaml.cs
│       │   ├── AdvancedAnalyticsPage.xaml
│       │   ├── AdvancedAnalyticsPage.xaml.cs
│       │   ├── AgentPage.xaml
│       │   ├── AgentPage.xaml.cs
│       │   ├── AnalysisExportPage.xaml
│       │   ├── AnalysisExportPage.xaml.cs
│       │   ├── AnalysisExportWizardPage.xaml
│       │   ├── AnalysisExportWizardPage.xaml.cs
│       │   ├── ArchiveHealthPage.xaml
│       │   ├── ArchiveHealthPage.xaml.cs
│       │   ├── BackfillPage.xaml
│       │   ├── BackfillPage.xaml.cs
│       │   ├── BacktestPage.xaml
│       │   ├── BacktestPage.xaml.cs
│       │   ├── BatchBacktestPage.xaml
│       │   ├── BatchBacktestPage.xaml.cs
│       │   ├── ChartingPage.xaml
│       │   ├── ChartingPage.xaml.cs
│       │   ├── ClusterStatusPage.xaml
│       │   ├── ClusterStatusPage.xaml.cs
│       │   ├── CollectionSessionPage.xaml
│       │   ├── CollectionSessionPage.xaml.cs
│       │   ├── CommandPaletteWindow.xaml
│       │   ├── CommandPaletteWindow.xaml.cs
│       │   ├── DashboardPage.xaml
│       │   ├── DashboardPage.xaml.cs
│       │   ├── DataBrowserPage.xaml
│       │   ├── DataBrowserPage.xaml.cs
│       │   ├── DataCalendarPage.xaml
│       │   ├── DataCalendarPage.xaml.cs
│       │   ├── DataExportPage.xaml
│       │   ├── DataExportPage.xaml.cs
│       │   ├── DataQualityPage.xaml
│       │   ├── DataQualityPage.xaml.cs
│       │   ├── DataSamplingPage.xaml
│       │   ├── DataSamplingPage.xaml.cs
│       │   ├── DataSourcesPage.xaml
│       │   ├── DataSourcesPage.xaml.cs
│       │   ├── DiagnosticsPage.xaml
│       │   ├── DiagnosticsPage.xaml.cs
│       │   ├── DirectLendingPage.xaml
│       │   ├── DirectLendingPage.xaml.cs
│       │   ├── EventReplayPage.xaml
│       │   ├── EventReplayPage.xaml.cs
│       │   ├── ExportPresetsPage.xaml
│       │   ├── ExportPresetsPage.xaml.cs
│       │   ├── HelpPage.xaml
│       │   ├── HelpPage.xaml.cs
│       │   ├── IndexSubscriptionPage.xaml
│       │   ├── IndexSubscriptionPage.xaml.cs
│       │   ├── KeyboardShortcutsPage.xaml
│       │   ├── KeyboardShortcutsPage.xaml.cs
│       │   ├── LeanIntegrationPage.xaml
│       │   ├── LeanIntegrationPage.xaml.cs
│       │   ├── LiveDataViewerPage.xaml
│       │   ├── LiveDataViewerPage.xaml.cs
│       │   ├── MainPage.SplitPane.cs
│       │   ├── MainPage.xaml
│       │   ├── MainPage.xaml.cs
│       │   ├── MessagingHubPage.xaml
│       │   ├── MessagingHubPage.xaml.cs
│       │   ├── NotificationCenterPage.xaml
│       │   ├── NotificationCenterPage.xaml.cs
│       │   ├── OptionsPage.xaml
│       │   ├── OptionsPage.xaml.cs
│       │   ├── OrderBookHeatmapControl.xaml
│       │   ├── OrderBookHeatmapControl.xaml.cs
│       │   ├── OrderBookPage.xaml
│       │   ├── OrderBookPage.xaml.cs
│       │   ├── PackageManagerPage.xaml
│       │   ├── PackageManagerPage.xaml.cs
│       │   ├── PageActionBarControl.xaml
│       │   ├── PageActionBarControl.xaml.cs
│       │   ├── Pages.cs
│       │   ├── PluginManagementPage.xaml
│       │   ├── PluginManagementPage.xaml.cs
│       │   ├── PortfolioImportPage.xaml
│       │   ├── PortfolioImportPage.xaml.cs
│       │   ├── ProviderHealthPage.xaml
│       │   ├── ProviderHealthPage.xaml.cs
│       │   ├── ProviderPage.xaml
│       │   ├── ProviderPage.xaml.cs
│       │   ├── QualityArchivePage.xaml
│       │   ├── QualityArchivePage.xaml.cs
│       │   ├── QuoteFloatWindow.xaml
│       │   ├── QuoteFloatWindow.xaml.cs
│       │   ├── ResearchWorkspaceShellPage.xaml
│       │   ├── ResearchWorkspaceShellPage.xaml.cs
│       │   ├── RetentionAssurancePage.xaml
│       │   ├── RetentionAssurancePage.xaml.cs
│       │   ├── RunDetailPage.xaml
│       │   ├── RunDetailPage.xaml.cs
│       │   ├── RunLedgerPage.xaml
│       │   ├── RunLedgerPage.xaml.cs
│       │   ├── RunMatPage.xaml
│       │   ├── RunMatPage.xaml.cs
│       │   ├── RunPortfolioPage.xaml
│       │   ├── RunPortfolioPage.xaml.cs
│       │   ├── ScheduleManagerPage.xaml
│       │   ├── ScheduleManagerPage.xaml.cs
│       │   ├── SecurityMasterPage.xaml
│       │   ├── SecurityMasterPage.xaml.cs
│       │   ├── ServiceManagerPage.xaml
│       │   ├── ServiceManagerPage.xaml.cs
│       │   ├── SettingsPage.xaml
│       │   ├── SettingsPage.xaml.cs
│       │   ├── SetupWizardPage.xaml
│       │   ├── SetupWizardPage.xaml.cs
│       │   ├── SplitPaneHostControl.xaml
│       │   ├── SplitPaneHostControl.xaml.cs
│       │   ├── StatusBarControl.xaml
│       │   ├── StatusBarControl.xaml.cs
│       │   ├── StorageOptimizationPage.xaml
│       │   ├── StorageOptimizationPage.xaml.cs
│       │   ├── StoragePage.xaml
│       │   ├── StoragePage.xaml.cs
│       │   ├── StrategyRunsPage.xaml
│       │   ├── StrategyRunsPage.xaml.cs
│       │   ├── SymbolMappingPage.xaml
│       │   ├── SymbolMappingPage.xaml.cs
│       │   ├── SymbolsPage.xaml
│       │   ├── SymbolsPage.xaml.cs
│       │   ├── SymbolStoragePage.xaml
│       │   ├── SymbolStoragePage.xaml.cs
│       │   ├── SystemHealthPage.xaml
│       │   ├── SystemHealthPage.xaml.cs
│       │   ├── TickerStripWindow.xaml
│       │   ├── TickerStripWindow.xaml.cs
│       │   ├── TimeSeriesAlignmentPage.xaml
│       │   ├── TimeSeriesAlignmentPage.xaml.cs
│       │   ├── TradingHoursPage.xaml
│       │   ├── TradingHoursPage.xaml.cs
│       │   ├── TradingWorkspaceShellPage.xaml
│       │   ├── TradingWorkspaceShellPage.xaml.cs
│       │   ├── WatchlistPage.xaml
│       │   ├── WatchlistPage.xaml.cs
│       │   ├── WelcomePage.xaml
│       │   ├── WelcomePage.xaml.cs
│       │   ├── WorkspacePage.xaml
│       │   └── WorkspacePage.xaml.cs
│       ├── App.xaml
│       ├── App.xaml.cs
│       ├── AssemblyInfo.cs
│       ├── GlobalUsings.cs
│       ├── MainWindow.xaml
│       ├── MainWindow.xaml.cs
│       ├── Meridian.Wpf.csproj
│       └── README.md
├── tests
│   ├── Meridian.Backtesting.Tests
│   │   ├── BacktestEngineIntegrationTests.cs
│   │   ├── BacktestRequestConfigTests.cs
│   │   ├── BracketOrderTests.cs
│   │   ├── CorporateActionAdjustmentServiceTests.cs
│   │   ├── FillModelExpansionTests.cs
│   │   ├── FillModelTests.cs
│   │   ├── GlobalUsings.cs
│   │   ├── LedgerQueryTests.cs
│   │   ├── MarketImpactFillModelTests.cs
│   │   ├── Meridian.Backtesting.Tests.csproj
│   │   ├── SimulatedPortfolioTests.cs
│   │   ├── TcaReporterTests.cs
│   │   ├── XirrCalculatorTests.cs
│   │   └── YahooFinanceBacktestIntegrationTests.cs
│   ├── Meridian.DirectLending.Tests
│   │   ├── BankTransactionSeedTests.cs
│   │   ├── DirectLendingPostgresIntegrationTests.cs
│   │   ├── DirectLendingPostgresTestDatabase.cs
│   │   ├── DirectLendingServiceTests.cs
│   │   ├── DirectLendingWorkflowTests.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.DirectLending.Tests.csproj
│   │   └── PaymentApprovalTests.cs
│   ├── Meridian.FSharp.Tests
│   │   ├── CalculationTests.fs
│   │   ├── CanonicalizationTests.fs
│   │   ├── CashFlowProjectorTests.fs
│   │   ├── DirectLendingInteropTests.fs
│   │   ├── DomainTests.fs
│   │   ├── LedgerKernelTests.fs
│   │   ├── Meridian.FSharp.Tests.fsproj
│   │   ├── PipelineTests.fs
│   │   ├── RiskPolicyTests.fs
│   │   ├── TradingTransitionTests.fs
│   │   └── ValidationTests.fs
│   ├── Meridian.McpServer.Tests
│   │   ├── Tools
│   │   │   ├── BackfillToolsTests.cs
│   │   │   └── StorageToolsTests.cs
│   │   ├── GlobalUsings.cs
│   │   └── Meridian.McpServer.Tests.csproj
│   ├── Meridian.Tests
│   │   ├── Application
│   │   │   ├── Backfill
│   │   │   │   ├── AdditionalProviderContractTests.cs
│   │   │   │   ├── BackfillCostEstimatorTests.cs
│   │   │   │   ├── BackfillStatusStoreTests.cs
│   │   │   │   ├── BackfillWorkerServiceTests.cs
│   │   │   │   ├── CompositeHistoricalDataProviderTests.cs
│   │   │   │   ├── GapBackfillServiceTests.cs
│   │   │   │   ├── HistoricalProviderContractTests.cs
│   │   │   │   ├── ParallelBackfillServiceTests.cs
│   │   │   │   ├── PriorityBackfillQueueTests.cs
│   │   │   │   ├── RateLimiterTests.cs
│   │   │   │   ├── ScheduledBackfillTests.cs
│   │   │   │   └── TwelveDataNasdaqProviderContractTests.cs
│   │   │   ├── Canonicalization
│   │   │   │   ├── Fixtures
│   │   │   │   │   ├── alpaca_trade_extended_hours.json
│   │   │   │   │   ├── alpaca_trade_odd_lot.json
│   │   │   │   │   ├── alpaca_trade_regular.json
│   │   │   │   │   ├── alpaca_xnas_identity.json
│   │   │   │   │   ├── polygon_trade_extended_hours.json
│   │   │   │   │   ├── polygon_trade_odd_lot.json
│   │   │   │   │   ├── polygon_trade_regular.json
│   │   │   │   │   └── polygon_xnas_identity.json
│   │   │   │   ├── CanonicalizationFixtureDriftTests.cs
│   │   │   │   └── CanonicalizationGoldenFixtureTests.cs
│   │   │   ├── Commands
│   │   │   │   ├── CliArgumentsTests.cs
│   │   │   │   ├── CommandDispatcherTests.cs
│   │   │   │   ├── DryRunCommandTests.cs
│   │   │   │   ├── HelpCommandTests.cs
│   │   │   │   ├── PackageCommandsTests.cs
│   │   │   │   ├── SelfTestCommandTests.cs
│   │   │   │   ├── SymbolCommandsTests.cs
│   │   │   │   └── ValidateConfigCommandTests.cs
│   │   │   ├── Composition
│   │   │   │   ├── Startup
│   │   │   │   │   └── SharedStartupBootstrapperTests.cs
│   │   │   │   └── SecurityMasterStartupTests.cs
│   │   │   ├── Config
│   │   │   │   ├── ConfigJsonSchemaGeneratorTests.cs
│   │   │   │   ├── ConfigSchemaIntegrationTests.cs
│   │   │   │   ├── ConfigurationUnificationTests.cs
│   │   │   │   ├── ConfigValidationPipelineTests.cs
│   │   │   │   └── ConfigValidatorTests.cs
│   │   │   ├── Coordination
│   │   │   │   ├── ClusterCoordinatorServiceTests.cs
│   │   │   │   ├── LeaseManagerTests.cs
│   │   │   │   ├── SplitBrainDetectorTests.cs
│   │   │   │   └── SubscriptionOrchestratorCoordinationTests.cs
│   │   │   ├── Credentials
│   │   │   │   ├── CredentialStatusTests.cs
│   │   │   │   ├── CredentialTestingServiceTests.cs
│   │   │   │   └── OAuthTokenTests.cs
│   │   │   ├── Etl
│   │   │   │   ├── EtlJobDefinitionStoreTests.cs
│   │   │   │   ├── EtlJobOrchestratorTests.cs
│   │   │   │   └── EtlNormalizationServiceTests.cs
│   │   │   ├── Indicators
│   │   │   │   └── TechnicalIndicatorServiceTests.cs
│   │   │   ├── Monitoring
│   │   │   │   ├── DataQuality
│   │   │   │   │   ├── DataFreshnessSlaMonitorTests.cs
│   │   │   │   │   ├── DataQualityTests.cs
│   │   │   │   │   └── LiquidityProfileTests.cs
│   │   │   │   ├── BackpressureAlertServiceTests.cs
│   │   │   │   ├── BadTickFilterTests.cs
│   │   │   │   ├── ErrorRingBufferTests.cs
│   │   │   │   ├── PriceContinuityCheckerTests.cs
│   │   │   │   ├── PrometheusMetricsTests.cs
│   │   │   │   ├── ProviderDegradationScorerTests.cs
│   │   │   │   ├── ProviderLatencyServiceTests.cs
│   │   │   │   ├── SchemaValidationServiceTests.cs
│   │   │   │   ├── SloDefinitionRegistryTests.cs
│   │   │   │   ├── SpreadMonitorTests.cs
│   │   │   │   ├── TickSizeValidatorTests.cs
│   │   │   │   └── TracedEventMetricsTests.cs
│   │   │   ├── Pipeline
│   │   │   │   ├── BackfillProgressTrackerTests.cs
│   │   │   │   ├── BackpressureSignalTests.cs
│   │   │   │   ├── CompositePublisherTests.cs
│   │   │   │   ├── DroppedEventAuditTrailTests.cs
│   │   │   │   ├── DualPathEventPipelineTests.cs
│   │   │   │   ├── EventPipelineMetricsTests.cs
│   │   │   │   ├── EventPipelineTests.cs
│   │   │   │   ├── EventPipelineTracePropagationTests.cs
│   │   │   │   ├── FSharpEventValidatorTests.cs
│   │   │   │   ├── GoldenMasterPipelineReplayTests.cs
│   │   │   │   ├── HotPathBatchSerializerTests.cs
│   │   │   │   ├── IngestionJobServiceCoordinationTests.cs
│   │   │   │   ├── IngestionJobServiceTests.cs
│   │   │   │   ├── IngestionJobTests.cs
│   │   │   │   ├── MarketDataClientFactoryTests.cs
│   │   │   │   ├── SpscRingBufferTests.cs
│   │   │   │   └── WalEventPipelineTests.cs
│   │   │   ├── Services
│   │   │   │   ├── DataQuality
│   │   │   │   │   ├── AnomalyDetectorTests.cs
│   │   │   │   │   ├── CompletenessScoreCalculatorTests.cs
│   │   │   │   │   ├── GapAnalyzerTests.cs
│   │   │   │   │   └── SequenceErrorTrackerTests.cs
│   │   │   │   ├── CanonicalizingPublisherTests.cs
│   │   │   │   ├── CliModeResolverTests.cs
│   │   │   │   ├── ConditionCodeMapperTests.cs
│   │   │   │   ├── ConfigurationPresetsTests.cs
│   │   │   │   ├── ConfigurationServiceTests.cs
│   │   │   │   ├── CronExpressionParserTests.cs
│   │   │   │   ├── ErrorCodeMappingTests.cs
│   │   │   │   ├── EventCanonicalizerTests.cs
│   │   │   │   ├── GracefulShutdownTests.cs
│   │   │   │   ├── OperationalSchedulerTests.cs
│   │   │   │   ├── OptionsChainServiceTests.cs
│   │   │   │   ├── PreflightCheckerTests.cs
│   │   │   │   ├── TradingCalendarTests.cs
│   │   │   │   └── VenueMicMapperTests.cs
│   │   │   ├── DirectLendingServiceTests.cs
│   │   │   └── ReconciliationRunServiceTests.cs
│   │   ├── Architecture
│   │   │   └── LayerBoundaryTests.cs
│   │   ├── Domain
│   │   │   ├── Collectors
│   │   │   │   ├── L3OrderBookCollectorTests.cs
│   │   │   │   ├── LiveDataAccessTests.cs
│   │   │   │   ├── MarketDepthCollectorTests.cs
│   │   │   │   ├── OptionDataCollectorTests.cs
│   │   │   │   ├── QuoteCollectorTests.cs
│   │   │   │   └── TradeDataCollectorTests.cs
│   │   │   ├── Models
│   │   │   │   ├── AdjustedHistoricalBarTests.cs
│   │   │   │   ├── AggregateBarTests.cs
│   │   │   │   ├── BboQuotePayloadTests.cs
│   │   │   │   ├── EffectiveSymbolTests.cs
│   │   │   │   ├── GreeksSnapshotTests.cs
│   │   │   │   ├── HistoricalBarTests.cs
│   │   │   │   ├── OpenInterestUpdateTests.cs
│   │   │   │   ├── OptionChainSnapshotTests.cs
│   │   │   │   ├── OptionContractSpecTests.cs
│   │   │   │   ├── OptionQuoteTests.cs
│   │   │   │   ├── OptionTradeTests.cs
│   │   │   │   ├── OrderBookLevelTests.cs
│   │   │   │   ├── OrderEventPayloadTests.cs
│   │   │   │   └── TradeModelTests.cs
│   │   │   └── StrongDomainTypeTests.cs
│   │   ├── Execution
│   │   │   ├── BrokerageGatewayAdapterTests.cs
│   │   │   ├── OrderManagementSystemTests.cs
│   │   │   ├── PaperSessionPersistenceServiceTests.cs
│   │   │   ├── PaperTradingGatewayTests.cs
│   │   │   └── PaperTradingPortfolioTests.cs
│   │   ├── Infrastructure
│   │   │   ├── CppTrader
│   │   │   │   └── CppTraderOrderGatewayTests.cs
│   │   │   ├── DataSources
│   │   │   │   └── CredentialConfigTests.cs
│   │   │   ├── Etl
│   │   │   │   └── CsvPartnerFileParserTests.cs
│   │   │   ├── Providers
│   │   │   │   ├── Fixtures
│   │   │   │   │   ├── InteractiveBrokers
│   │   │   │   │   │   ├── ib_order_limit_buy_day.json
│   │   │   │   │   │   ├── ib_order_limit_sell_fok.json
│   │   │   │   │   │   ├── ib_order_loc_sell_day.json
│   │   │   │   │   │   ├── ib_order_market_sell_gtc.json
│   │   │   │   │   │   ├── ib_order_moc_sell_day.json
│   │   │   │   │   │   ├── ib_order_stop_buy_ioc.json
│   │   │   │   │   │   ├── ib_order_stop_limit_buy_day.json
│   │   │   │   │   │   └── ib_order_trailing_stop_sell_gtc.json
│   │   │   │   │   └── Polygon
│   │   │   │   │       ├── polygon-recorded-session-aapl.json
│   │   │   │   │       ├── polygon-recorded-session-gld-cboe-sell.json
│   │   │   │   │       ├── polygon-recorded-session-msft-edge.json
│   │   │   │   │       ├── polygon-recorded-session-nvda-multi-batch.json
│   │   │   │   │       └── polygon-recorded-session-spy-etf.json
│   │   │   │   ├── AlpacaCredentialAndReconnectTests.cs
│   │   │   │   ├── AlpacaMessageParsingTests.cs
│   │   │   │   ├── AlpacaQuotePipelineGoldenTests.cs
│   │   │   │   ├── AlpacaQuoteRoutingTests.cs
│   │   │   │   ├── BackfillRetryAfterTests.cs
│   │   │   │   ├── FailoverAwareMarketDataClientTests.cs
│   │   │   │   ├── FreeProviderContractTests.cs
│   │   │   │   ├── HistoricalDataProviderContractTests.cs
│   │   │   │   ├── IBOrderSampleTests.cs
│   │   │   │   ├── IBRuntimeGuidanceTests.cs
│   │   │   │   ├── IBSimulationClientContractTests.cs
│   │   │   │   ├── IBSimulationClientTests.cs
│   │   │   │   ├── MarketDataClientContractTests.cs
│   │   │   │   ├── NyseMarketDataClientTests.cs
│   │   │   │   ├── NYSEMessageParsingTests.cs
│   │   │   │   ├── NyseNationalTradesCsvParserTests.cs
│   │   │   │   ├── NyseSharedLifecycleTests.cs
│   │   │   │   ├── NyseTaqCollectorIntegrationTests.cs
│   │   │   │   ├── PolygonMarketDataClientTests.cs
│   │   │   │   ├── PolygonMessageParsingTests.cs
│   │   │   │   ├── PolygonRecordedSessionReplayTests.cs
│   │   │   │   ├── PolygonSubscriptionTests.cs
│   │   │   │   ├── ProviderResilienceTests.cs
│   │   │   │   ├── StockSharpConnectorFactoryTests.cs
│   │   │   │   ├── StockSharpMessageConversionTests.cs
│   │   │   │   ├── StockSharpSubscriptionTests.cs
│   │   │   │   ├── StreamingFailoverServiceTests.cs
│   │   │   │   └── SyntheticMarketDataProviderTests.cs
│   │   │   ├── Resilience
│   │   │   │   ├── WebSocketConnectionManagerTests.cs
│   │   │   │   └── WebSocketResiliencePolicyTests.cs
│   │   │   └── Shared
│   │   │       ├── SymbolNormalizationTests.cs
│   │   │       └── TempDirectoryFixture.cs
│   │   ├── Integration
│   │   │   ├── EndpointTests
│   │   │   │   ├── AuthEndpointTests.cs
│   │   │   │   ├── BackfillEndpointTests.cs
│   │   │   │   ├── CatalogEndpointTests.cs
│   │   │   │   ├── ConfigEndpointTests.cs
│   │   │   │   ├── EndpointIntegrationTestBase.cs
│   │   │   │   ├── EndpointTestCollection.cs
│   │   │   │   ├── EndpointTestFixture.cs
│   │   │   │   ├── FailoverEndpointTests.cs
│   │   │   │   ├── HealthEndpointTests.cs
│   │   │   │   ├── HistoricalEndpointTests.cs
│   │   │   │   ├── IBEndpointTests.cs
│   │   │   │   ├── LeanEndpointTests.cs
│   │   │   │   ├── LiveDataEndpointTests.cs
│   │   │   │   ├── MaintenanceEndpointTests.cs
│   │   │   │   ├── NegativePathEndpointTests.cs
│   │   │   │   ├── OptionsEndpointTests.cs
│   │   │   │   ├── ProviderEndpointTests.cs
│   │   │   │   ├── QualityDropsEndpointTests.cs
│   │   │   │   ├── QualityEndpointContractTests.cs
│   │   │   │   ├── ResponseSchemaSnapshotTests.cs
│   │   │   │   ├── ResponseSchemaValidationTests.cs
│   │   │   │   ├── RoleAuthorizationTests.cs
│   │   │   │   ├── StatusEndpointTests.cs
│   │   │   │   ├── StorageEndpointTests.cs
│   │   │   │   └── SymbolEndpointTests.cs
│   │   │   ├── ConfigurableTickerDataCollectionTests.cs
│   │   │   ├── ConnectionRetryIntegrationTests.cs
│   │   │   ├── EndpointStubDetectionTests.cs
│   │   │   ├── FixtureProviderTests.cs
│   │   │   ├── GracefulShutdownIntegrationTests.cs
│   │   │   └── YahooFinancePcgPreferredIntegrationTests.cs
│   │   ├── Ledger
│   │   │   └── LedgerIntegrationTests.cs
│   │   ├── Performance
│   │   │   └── AllocationBudgetIntegrationTests.cs
│   │   ├── ProviderSdk
│   │   │   ├── CredentialValidatorTests.cs
│   │   │   ├── DataSourceAttributeTests.cs
│   │   │   ├── DataSourceRegistryTests.cs
│   │   │   └── ExceptionTypeTests.cs
│   │   ├── Risk
│   │   │   └── CompositeRiskValidatorTests.cs
│   │   ├── SecurityMaster
│   │   │   ├── SecurityEnrichmentTests.cs
│   │   │   ├── SecurityMasterAssetClassSupportTests.cs
│   │   │   ├── SecurityMasterDatabaseFactAttribute.cs
│   │   │   ├── SecurityMasterDatabaseFixture.cs
│   │   │   ├── SecurityMasterMigrationRunnerTests.cs
│   │   │   ├── SecurityMasterPostgresRoundTripTests.cs
│   │   │   ├── SecurityMasterProjectionServiceSnapshotTests.cs
│   │   │   ├── SecurityMasterRebuildOrchestratorTests.cs
│   │   │   ├── SecurityMasterReferenceLookupTests.cs
│   │   │   ├── SecurityMasterServiceSnapshotTests.cs
│   │   │   └── SecurityMasterSnapshotStoreTests.cs
│   │   ├── Serialization
│   │   │   └── HighPerformanceJsonTests.cs
│   │   ├── Storage
│   │   │   ├── AnalysisExportServiceTests.cs
│   │   │   ├── AtomicFileWriterTests.cs
│   │   │   ├── CanonicalSymbolRegistryTests.cs
│   │   │   ├── CompositeSinkTests.cs
│   │   │   ├── DataLineageServiceTests.cs
│   │   │   ├── DataQualityScoringServiceTests.cs
│   │   │   ├── DataValidatorTests.cs
│   │   │   ├── EventBufferTests.cs
│   │   │   ├── ExportValidatorTests.cs
│   │   │   ├── FilePermissionsServiceTests.cs
│   │   │   ├── JsonlBatchWriteTests.cs
│   │   │   ├── LifecyclePolicyEngineTests.cs
│   │   │   ├── MemoryMappedJsonlReaderTests.cs
│   │   │   ├── MetadataTagServiceTests.cs
│   │   │   ├── ParquetConversionServiceTests.cs
│   │   │   ├── PortableDataPackagerTests.cs
│   │   │   ├── QuotaEnforcementServiceTests.cs
│   │   │   ├── StorageCatalogServiceTests.cs
│   │   │   ├── StorageChecksumServiceTests.cs
│   │   │   ├── StorageOptionsDefaultsTests.cs
│   │   │   ├── StorageSinkRegistryTests.cs
│   │   │   ├── SymbolRegistryServiceTests.cs
│   │   │   ├── WriteAheadLogCorruptionModeTests.cs
│   │   │   ├── WriteAheadLogFuzzTests.cs
│   │   │   └── WriteAheadLogTests.cs
│   │   ├── Strategies
│   │   │   ├── CashFlowProjectionTests.cs
│   │   │   ├── PromotionServiceTests.cs
│   │   │   ├── StrategyLifecycleManagerTests.cs
│   │   │   ├── StrategyRunDrillInTests.cs
│   │   │   └── StrategyRunReadServiceTests.cs
│   │   ├── SymbolSearch
│   │   │   ├── OpenFigiClientTests.cs
│   │   │   └── SymbolSearchServiceTests.cs
│   │   ├── TestData
│   │   │   └── Golden
│   │   │       └── alpaca-quote-pipeline.json
│   │   ├── TestHelpers
│   │   │   ├── PolygonStubClient.cs
│   │   │   └── TestMarketEventPublisher.cs
│   │   ├── Ui
│   │   │   ├── DirectLendingEndpointsTests.cs
│   │   │   └── WorkstationEndpointsTests.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.Tests.csproj
│   │   └── TestCollections.cs
│   ├── Meridian.Ui.Tests
│   │   ├── Collections
│   │   │   ├── BoundedObservableCollectionTests.cs
│   │   │   └── CircularBufferTests.cs
│   │   ├── Services
│   │   │   ├── ActivityFeedServiceTests.cs
│   │   │   ├── AlertServiceTests.cs
│   │   │   ├── AnalysisExportServiceBaseTests.cs
│   │   │   ├── ApiClientServiceTests.cs
│   │   │   ├── ArchiveBrowserServiceTests.cs
│   │   │   ├── BackendServiceManagerBaseTests.cs
│   │   │   ├── BackfillApiServiceTests.cs
│   │   │   ├── BackfillCheckpointServiceTests.cs
│   │   │   ├── BackfillProviderConfigServiceTests.cs
│   │   │   ├── BackfillServiceTests.cs
│   │   │   ├── ChartingServiceTests.cs
│   │   │   ├── CollectionSessionServiceTests.cs
│   │   │   ├── CommandPaletteServiceTests.cs
│   │   │   ├── ConfigServiceBaseTests.cs
│   │   │   ├── ConfigServiceTests.cs
│   │   │   ├── ConnectionServiceBaseTests.cs
│   │   │   ├── CredentialServiceTests.cs
│   │   │   ├── DataCalendarServiceTests.cs
│   │   │   ├── DataCompletenessServiceTests.cs
│   │   │   ├── DataQualityRefreshCoordinatorTests.cs
│   │   │   ├── DataQualityServiceBaseTests.cs
│   │   │   ├── DataSamplingServiceTests.cs
│   │   │   ├── DiagnosticsServiceTests.cs
│   │   │   ├── ErrorHandlingServiceTests.cs
│   │   │   ├── EventReplayServiceTests.cs
│   │   │   ├── FixtureDataServiceTests.cs
│   │   │   ├── FormValidationServiceTests.cs
│   │   │   ├── IntegrityEventsServiceTests.cs
│   │   │   ├── LeanIntegrationServiceTests.cs
│   │   │   ├── LiveDataServiceTests.cs
│   │   │   ├── LoggingServiceBaseTests.cs
│   │   │   ├── ManifestServiceTests.cs
│   │   │   ├── NotificationServiceBaseTests.cs
│   │   │   ├── NotificationServiceTests.cs
│   │   │   ├── OrderBookVisualizationServiceTests.cs
│   │   │   ├── PortfolioImportServiceTests.cs
│   │   │   ├── ProviderHealthServiceTests.cs
│   │   │   ├── ProviderManagementServiceTests.cs
│   │   │   ├── ScheduledMaintenanceServiceTests.cs
│   │   │   ├── ScheduleManagerServiceTests.cs
│   │   │   ├── SchemaServiceTests.cs
│   │   │   ├── SearchServiceTests.cs
│   │   │   ├── SmartRecommendationsServiceTests.cs
│   │   │   ├── StatusServiceBaseTests.cs
│   │   │   ├── StorageAnalyticsServiceTests.cs
│   │   │   ├── SymbolGroupServiceTests.cs
│   │   │   ├── SymbolManagementServiceTests.cs
│   │   │   ├── SymbolMappingServiceTests.cs
│   │   │   ├── SystemHealthServiceTests.cs
│   │   │   ├── TimeSeriesAlignmentServiceTests.cs
│   │   │   ├── WatchlistServiceCollection.cs
│   │   │   └── WatchlistServiceTests.cs
│   │   ├── Meridian.Ui.Tests.csproj
│   │   └── README.md
│   ├── Meridian.Wpf.Tests
│   │   ├── Services
│   │   │   ├── AdminMaintenanceServiceTests.cs
│   │   │   ├── BackgroundTaskSchedulerServiceTests.cs
│   │   │   ├── ConfigServiceTests.cs
│   │   │   ├── ConnectionServiceTests.cs
│   │   │   ├── ExportPresetServiceTests.cs
│   │   │   ├── FirstRunServiceTests.cs
│   │   │   ├── InfoBarServiceTests.cs
│   │   │   ├── KeyboardShortcutServiceTests.cs
│   │   │   ├── MessagingServiceTests.cs
│   │   │   ├── NavigationServiceTests.cs
│   │   │   ├── NotificationServiceTests.cs
│   │   │   ├── OfflineTrackingPersistenceServiceTests.cs
│   │   │   ├── PendingOperationsQueueServiceTests.cs
│   │   │   ├── RetentionAssuranceServiceTests.cs
│   │   │   ├── RunMatServiceTests.cs
│   │   │   ├── StatusServiceTests.cs
│   │   │   ├── StorageServiceTests.cs
│   │   │   ├── StrategyRunWorkspaceServiceTests.cs
│   │   │   ├── TooltipServiceTests.cs
│   │   │   ├── WatchlistServiceTests.cs
│   │   │   └── WorkspaceServiceTests.cs
│   │   ├── Support
│   │   │   ├── RunMatUiAutomationFacade.cs
│   │   │   └── WpfTestThread.cs
│   │   ├── ViewModels
│   │   │   ├── DataQualityViewModelCharacterizationTests.cs
│   │   │   ├── RunMatViewModelTests.cs
│   │   │   └── StrategyRunBrowserViewModelTests.cs
│   │   ├── Views
│   │   │   ├── RunMatUiSmokeTests.cs
│   │   │   └── RunMatWorkflowSmokeTests.cs
│   │   ├── GlobalUsings.cs
│   │   └── Meridian.Wpf.Tests.csproj
│   ├── scripts
│   │   └── setup-verification.sh
│   ├── coverlet.runsettings
│   ├── Directory.Build.props
│   ├── setup-script-tests.md
│   └── xunit.runner.json
├── .editorconfig
├── .flake8
├── .gitattributes
├── .gitignore
├── .globalconfig
├── .markdownlint.json
├── .vsconfig
├── AGENT_IMPLEMENTATION_SUMMARY.md
├── AGENT_QUICK_REFERENCE.md
├── CLAUDE.md
├── CONTEXTUAL_COMMANDS_IMPLEMENTATION_SUMMARY.md
├── CORPACTIONS_IMPLEMENTATION.md
├── desktop.ini
├── Directory.Build.props
├── Directory.Packages.props
├── environment.yml
├── global.json
├── IMPLEMENTATION_CHECKLIST.md
├── IMPLEMENTATION_SUMMARY.md
├── LICENSE
├── Makefile
├── Meridian.sln
├── package-lock.json
├── package.json
├── README.md
└── SYSTEM_TRAY_IMPLEMENTATION.md
```

Full annotated file tree: [`docs/ai/claude/CLAUDE.structure.md`](docs/ai/claude/CLAUDE.structure.md)

---

## Critical Rules

**Always follow these — violations will cause build errors, deadlocks, or data loss:**

- **ALWAYS** use `CancellationToken` on async methods
- **NEVER** store secrets in code or config — use environment variables
- **ALWAYS** use structured logging: `_logger.LogInformation("Received {Count} bars for {Symbol}", count, symbol)`
- **PREFER** `IAsyncEnumerable<T>` for streaming data
- **ALWAYS** mark classes `sealed` unless designed for inheritance
- **NEVER** log sensitive data (API keys, credentials)
- **NEVER** use `Task.Run` for I/O-bound operations (wastes thread pool)
- **NEVER** block async with `.Result` or `.Wait()` (causes deadlocks)
- **ALWAYS** add `[ImplementsAdr]` attributes when implementing ADR contracts
- **NEVER** add `Version="..."` to `<PackageReference>` — causes NU1008 (see CPM section)

---

## Coding Conventions

### Logging
```csharp
// Good — structured
_logger.LogInformation("Received {Count} bars for {Symbol}", bars.Count, symbol);

// Bad — string interpolation loses structure
_logger.LogInformation($"Received {bars.Count} bars for {symbol}");
```

### Error Handling
- Log all errors with context (symbol, provider, timestamp)
- Use exponential backoff for retries
- Throw `ArgumentException` for bad inputs, `InvalidOperationException` for state errors
- Custom exceptions in `src/Meridian.Core/Exceptions/`: `ConfigurationException`, `ConnectionException`, `DataProviderException`, `RateLimitException`, `SequenceValidationException`, `StorageException`, `ValidationException`, `OperationTimeoutException`

### Naming
- Async methods: suffix `Async`
- Cancellation token param: `ct` or `cancellationToken`
- Private fields: `_prefixed`
- Interfaces: `IPrefixed`

### Performance (hot paths)
- Avoid allocations; use object pooling
- Prefer `Span<T>` / `Memory<T>` for buffer ops
- Use `System.Threading.Channels` for producer-consumer patterns

### Path-Specific Instruction Rules

When working with files in specific paths or types, additional rules apply. Review these before making changes:

**C# source files** (`src/**/*.cs`):
- Use `IOptionsMonitor<T>` (not `IOptions<T>`) for runtime-mutable config
- All JSON serialization must use ADR-014 source generators — call `JsonSerializer.Serialize(value, MyJsonContext.Default.MyType)`
- Use `EventPipelinePolicy.Default.CreateChannel<T>()` for producer-consumer queues (ADR-013)
- All domain exceptions must derive from `MeridianException` in `src/Meridian.Core/Exceptions/`
- Register all new serializable DTOs in the project's `JsonSerializerContext` partial class

**Test files** (`tests/**/*.cs`):
- Keep tests deterministic (no time/network/external dependency flakiness)
- Prefer clear Arrange-Act-Assert structure
- Use existing test utilities and fixtures before introducing new helpers
- Name tests to communicate behavior: `[MethodName]_[Condition]_[Expectation]`
- Run the nearest test project and report exact command used

Complete rules for each path are in `.github/instructions/` directory.

---

## Anti-Patterns to Avoid

| Anti-Pattern | Why It's Bad |
|--------------|--------------|
| Swallowing exceptions silently | Hides bugs, makes debugging impossible |
| Hardcoding credentials | Security risk, inflexible deployment |
| `Task.Run` for I/O | Wastes thread pool threads |
| Blocking async with `.Result` | Causes deadlocks |
| `new HttpClient()` directly | Socket exhaustion, DNS issues |
| String interpolation in logger calls | Loses structured logging benefits |
| Missing `CancellationToken` | Prevents graceful shutdown |
| Missing `[ImplementsAdr]` attribute | Loses ADR traceability |
| `Version="..."` on `PackageReference` | NU1008 build error (CPM violation) |

---

## Central Package Management (CPM)

All package versions live in `Directory.Packages.props`. Project files must **not** include versions.

```xml
<!-- CORRECT -->
<PackageReference Include="Serilog" />

<!-- WRONG — causes error NU1008 -->
<PackageReference Include="Serilog" Version="4.3.0" />
```

**Adding a new package:**
1. Add to `Directory.Packages.props`: `<PackageVersion Include="Pkg" Version="1.0.0" />`
2. Reference in `.csproj` without version: `<PackageReference Include="Pkg" />`

---

## Configuration

### Environment Variables (credentials)
```bash
export ALPACA_KEY_ID=your-key-id
export ALPACA_SECRET_KEY=your-secret-key
export NYSE_API_KEY=your-api-key
export POLYGON_API_KEY=your-api-key
export TIINGO_API_TOKEN=your-token
export FINNHUB_API_KEY=your-api-key
export ALPHA_VANTAGE_API_KEY=your-api-key
export NASDAQ_API_KEY=your-api-key
```

### appsettings.json
```bash
cp config/appsettings.sample.json config/appsettings.json
```

Key sections: `DataSource`, `Symbols`, `Storage`, `Backfill`, `DataQuality`, `Sla`, `Maintenance`

### Git Hooks Setup (Optional but Recommended)

Pre-commit and commit-msg hooks enforce code formatting and commit message conventions. Install them at repository setup:

```bash
./build/scripts/hooks/install-hooks.sh
```

**What they do:**

| Hook | Behavior |
|------|----------|
| `pre-commit` | Runs `dotnet format` on staged C#/F# files, re-stages any changes, and blocks commit if formatting issues remain |
| `commit-msg` | Validates commit message subject is <= 72 characters, non-empty, and optionally warns about trailing periods |

**Manual installation:**

```bash
cp build/scripts/hooks/pre-commit .git/hooks/pre-commit
cp build/scripts/hooks/commit-msg .git/hooks/commit-msg
chmod +x .git/hooks/pre-commit .git/hooks/commit-msg
```

**To disable hooks temporarily:**

```bash
git commit --no-verify
```

---

## Architecture Decision Records (ADRs)

Located in `docs/adr/`. Use `[ImplementsAdr("ADR-XXX", "reason")]` on implementing classes.

| ADR | Key Points |
|-----|------------|
| ADR-001 | Provider abstraction — `IMarketDataClient`, `IHistoricalDataProvider` contracts |
| ADR-002 | Tiered storage — hot/warm/cold architecture |
| ADR-003 | Monolith-first architecture — reject premature microservice decomposition |
| ADR-004 | Async patterns — `CancellationToken`, `IAsyncEnumerable` |
| ADR-005 | Attribute-based discovery — `[DataSource]`, `[ImplementsAdr]` |
| ADR-006 | Domain events — sealed record wrapper with static factories |
| ADR-007 | WAL + event pipeline durability |
| ADR-008 | Multi-format storage — JSONL + Parquet simultaneous writes |
| ADR-009 | F# type-safe domain with C# interop |
| ADR-010 | `IHttpClientFactory` — never instantiate `HttpClient` directly |
| ADR-011 | Centralized configuration — environment variables for credentials |
| ADR-012 | Unified monitoring — health checks + Prometheus metrics |
| ADR-013 | Bounded channel pipeline policy — consistent backpressure |
| ADR-014 | JSON source generators — no-reflection serialization |
| ADR-015 | Paper trading gateway — risk-free strategy validation for live + backtest parity |
| ADR-015 | Repository rename and platform restructuring guidance retained as a companion ADR-015 document |
| ADR-016 | Platform architecture migration — repository-wide mandate |

---

## Provider Class Inventory

The following provider-related classes are the current canonical inventory used by the AI docs audit.

### Streaming / hybrid implementations
| Provider Class | Role |
|----------------|------|
| `AlpacaMarketDataClient` | Alpaca real-time streaming market data |
| `IBMarketDataClient` | Interactive Brokers live market data |
| `IBSimulationClient` | Interactive Brokers simulation/testing client |
| `NyseMarketDataClient` | NYSE streaming market data via the unified provider registry |
| `NYSEDataSource` | NYSE direct data source |
| `PolygonMarketDataClient` | Polygon live market data |
| `StockSharpMarketDataClient` | StockSharp streaming market data |
| `SyntheticMarketDataClient` | Deterministic synthetic streaming and symbol-search market data for offline development |
| `FailoverAwareMarketDataClient` | Streaming failover wrapper |

### Historical implementations
| Provider Class | Role |
|----------------|------|
| `AlpacaHistoricalDataProvider` | Alpaca historical bars |
| `AlphaVantageHistoricalDataProvider` | Alpha Vantage historical bars |
| `CompositeHistoricalDataProvider` | Multi-provider historical failover |
| `FredHistoricalDataProvider` | FRED economic time series mapped to synthetic daily bars |
| `FinnhubHistoricalDataProvider` | Finnhub historical bars |
| `IBHistoricalDataProvider` | Interactive Brokers historical bars |
| `NasdaqDataLinkHistoricalDataProvider` | Nasdaq Data Link historical bars |
| `PolygonHistoricalDataProvider` | Polygon historical bars |
| `StockSharpHistoricalDataProvider` | StockSharp historical bars |
| `StooqHistoricalDataProvider` | Stooq historical bars |
| `SyntheticHistoricalDataProvider` | Deterministic synthetic historical bars, quotes, trades, auctions, and corporate actions |
| `TiingoHistoricalDataProvider` | Tiingo historical bars |
| `TwelveDataHistoricalDataProvider` | Twelve Data historical bars |
| `YahooFinanceHistoricalDataProvider` | Yahoo Finance historical bars |

### Symbol search implementations
| Provider Class | Role |
|----------------|------|
| `AlpacaSymbolSearchProviderRefactored` | Alpaca symbol search |
| `FinnhubSymbolSearchProviderRefactored` | Finnhub symbol search |
| `OpenFigiClient` | OpenFIGI symbol resolution/search |
| `PolygonSymbolSearchProvider` | Polygon symbol search |
| `StockSharpSymbolSearchProvider` | StockSharp symbol search |

### Brokerage gateway implementations
| Provider Class | Role |
|----------------|------|
| `BaseBrokerageGateway` | Abstract brokerage adapter base class |
| `BrokerageGatewayAdapter` | Order routing wrapper for `IBrokerageGateway` |
| `AlpacaBrokerageGateway` | Alpaca order routing with fractional quantity support |
| `IBBrokerageGateway` | Interactive Brokers order routing (conditional on IBAPI) |
| `StockSharpBrokerageGateway` | StockSharp connector-based order routing |
| `TemplateBrokerageGateway` | Brokerage adapter scaffold |

### Shared base and template provider classes
| Provider Class | Role |
|----------------|------|
| `BaseHistoricalDataProvider` | Shared historical provider base class |
| `BaseSymbolSearchProvider` | Shared symbol-search provider base class |
| `TemplateHistoricalDataProvider` | Historical provider scaffold |
| `TemplateMarketDataClient` | Streaming provider scaffold |
| `TemplateSymbolSearchProvider` | Symbol-search provider scaffold |

---

## Domain Models

### Core Event Types (Data Collection)
- `Trade` — Tick-by-tick trade prints with sequence validation
- `LOBSnapshot` — Full L2 order book state
- `BboQuote` — Best bid/offer with spread and mid-price
- `OrderFlowStatistics` — Rolling VWAP, imbalance, volume splits
- `IntegrityEvent` — Sequence anomalies (gaps, out-of-order)
- `HistoricalBar` — OHLCV bars from backfill providers

### Execution & Strategy Types
- `Order` — Limit/market orders with timestamp and fill tracking
- `Fill` — Executed trade with price, quantity, and commission
- `StrategyState` — Active/paused/stopped strategy with metadata
- `PortfolioSnapshot` — Position, cash, and performance metrics at point-in-time

### Key Classes
| Class | Location | Purpose |
|-------|----------|---------|
| `EventPipeline` | `Application/Pipeline/` | Bounded channel event routing |
| `TradeDataCollector` | `Domain/Collectors/` | Tick-by-tick trade processing |
| `MarketDepthCollector` | `Domain/Collectors/` | L2 order book maintenance |
| `JsonlStorageSink` | `Storage/Sinks/` | JSONL file persistence |
| `ParquetStorageSink` | `Storage/Sinks/` | Parquet file persistence |
| `WriteAheadLog` | `Storage/Archival/` | WAL for data durability |
| `CompositeHistoricalDataProvider` | `Infrastructure/Adapters/Core/` | Multi-provider backfill with fallback |
| `BacktestEngine` | `Backtesting/` | Tick-by-tick strategy replay with fill models |
| `PaperTradingGateway` | `Execution/` | Paper trading for real-time strategy testing |
| `BaseBrokerageGateway` | `Execution/Adapters/` | Abstract brokerage adapter base class |
| `AlpacaBrokerageGateway` | `Infrastructure/Adapters/Alpaca/` | Alpaca order routing |
| `PortfolioTracker` | `Strategies/` | Multi-run performance metrics and lifecycle |

*All locations relative to `src/Meridian/`*

---

## Build Requirements

- .NET 9.0 SDK
- `EnableWindowsTargeting=true` — set in `Directory.Build.props`, enables cross-platform build of Windows-targeting projects
- Python 3 — build tooling in `build/python/`
- Node.js — diagram generation (optional)

---

## Troubleshooting

```bash
make diagnose      # Build diagnostics
make doctor        # Full diagnostic check
```

| Error | Fix |
|-------|-----|
| NETSDK1100 | Ensure `EnableWindowsTargeting=true` in `Directory.Build.props` |
| NU1008 | Remove `Version="..."` from `<PackageReference>` in failing `.csproj` |
| Credential errors | Check environment variables are set |
| High memory | Check channel capacity in `EventPipeline` |
| Provider rate limits | Check `ProviderRateLimitTracker` logs |

See `docs/HELP.md` for detailed solutions.

---

## Detailed Reference Sub-Documents

Load these on-demand when working in the relevant area — do not read all of them on every task.

| Sub-Document | When to Load |
|--------------|-------------|
| [`docs/ai/claude/CLAUDE.providers.md`](docs/ai/claude/CLAUDE.providers.md) | Adding/modifying data providers, `IMarketDataClient`, `IHistoricalDataProvider`, symbol search |
| [`docs/ai/claude/CLAUDE.storage.md`](docs/ai/claude/CLAUDE.storage.md) | Storage sinks, WAL, archival, packaging, tiered storage |
| [`docs/ai/claude/CLAUDE.testing.md`](docs/ai/claude/CLAUDE.testing.md) | Writing or reviewing tests, test patterns, coverage |
| [`docs/ai/claude/CLAUDE.fsharp.md`](docs/ai/claude/CLAUDE.fsharp.md) | F# domain library, validation pipeline, C# interop |
| [`docs/ai/claude/CLAUDE.api.md`](docs/ai/claude/CLAUDE.api.md) | REST API endpoints, backtesting, strategy management, portfolio tracking, CI/CD pipelines |
| [`docs/ai/claude/CLAUDE.repo-updater.md`](docs/ai/claude/CLAUDE.repo-updater.md) | Running `ai-repo-updater.py` audit/verify/report commands |
| [`docs/ai/claude/CLAUDE.structure.md`](docs/ai/claude/CLAUDE.structure.md) | Full annotated file tree with backtesting, execution, and strategy projects |
| [`docs/ai/claude/CLAUDE.actions.md`](docs/ai/claude/CLAUDE.actions.md) | GitHub Actions workflows |
| [`docs/ai/ai-known-errors.md`](docs/ai/ai-known-errors.md) | Known AI agent mistakes — check before starting any task |

### Other Key Docs
| Doc | Purpose |
|-----|---------|
| `docs/adr/` | Architecture Decision Records |
| `docs/development/provider-implementation.md` | Step-by-step data provider guide |
| `docs/development/strategy-implementation.md` | Step-by-step strategy development guide |
| `docs/operations/portable-data-packager.md` | Data packaging and export |
| `docs/operations/strategy-lifecycle.md` | Strategy registration, deployment, and monitoring |
| `docs/architecture/backtesting-design.md` | Backtest engine architecture and fill models |
| `docs/HELP.md` | Complete user guide with FAQ |
| `docs/development/central-package-management.md` | CPM details |
| `docs/status/production-status.md` | Feature implementation status |
| `docs/status/ROADMAP.md` | Project roadmap and future work |

---

*Last Updated: 2026-03-24*
