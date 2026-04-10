# Meridian - Copilot Instructions

**Last Updated:** 2026-03-16

> **Note:** For comprehensive project context, see [CLAUDE.md](https://github.com/rodoHasArrived/Meridian/blob/main/CLAUDE.md) in the repository root. For the master AI resource index, see [docs/ai/README.md](https://github.com/rodoHasArrived/Meridian/blob/main/docs/ai/README.md).

## Coding Agent Optimization (GitHub Best Practices)

This repository uses native Copilot instruction files to improve agent output quality:

- Repository-wide guidance: `.github/copilot-instructions.md`
- Path-specific guidance: `.github/instructions/*.instructions.md`
- Environment bootstrap workflow: `.github/workflows/copilot-setup-steps.yml`

When assigning work to AI coding agents, prefer issues/prompts that include:

1. Clear problem statement.
2. Explicit acceptance criteria (including required tests).
3. Expected files/areas to change.
4. Any risk boundaries (security, prod critical paths, sensitive logic).

Use PR review comments to iterate in batches so the agent can address full feedback in one pass.

## Quick Start Checklist for Copilot Sessions

Before producing code, Copilot should:

1. Read repository-level instructions in `.github/copilot-instructions.md`.
2. Read any path-specific instruction file under `.github/instructions/` that matches touched files.
3. Review `docs/ai/ai-known-errors.md` and apply relevant prevention checks.
4. Confirm acceptance criteria include required validation commands.
5. Document assumptions and constraints directly in the PR description.

## Repository Overview

**Meridian** is a high-performance, cross-platform market data collection system for real-time and historical market microstructure data. It's a production-ready .NET 9.0 solution with F# domain libraries, supporting multiple data providers (Interactive Brokers, Alpaca, NYSE, Polygon, StockSharp) and offering flexible storage options.

| Attribute | Value |
|-----------|-------|
| **Project Type** | .NET Solution (C# and F#) |
| **Target Framework** | .NET 9.0 |
| **Languages** | C# 13, F# 8.0 |
| **Source Files** | 704 (692 C#, 12 F#) |
| **Test Files** | 241 across 4 test projects |
| **Main Projects** | 13 + 4 test + 1 benchmark |
| **Architecture** | Event-driven, monolithic core with optional UI projects |
| **Desktop App** | WPF (Windows) |

## AI Error Registry Workflow

Before implementing changes, review `docs/ai/ai-known-errors.md` and apply relevant prevention checks.
When an AI-caused regression is identified in GitHub, add label `ai-known-error` so the `AI Known Errors Intake` job in `.github/workflows/documentation.yml` can open a PR that records it.

## Build & Test Commands

**IMPORTANT:** Always use `/p:EnableWindowsTargeting=true` flag on non-Windows systems to avoid NETSDK1100 errors.

```bash
# Restore dependencies (ALWAYS run first)
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true

# Build
dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true

# Run core tests
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release /p:EnableWindowsTargeting=true

# Run F# tests
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj -c Release /p:EnableWindowsTargeting=true

# Run WPF tests (Windows only)
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -c Release /p:EnableWindowsTargeting=true

# Run UI service tests (Windows only)
dotnet test tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj -c Release /p:EnableWindowsTargeting=true

# Run all tests
dotnet test -c Release /p:EnableWindowsTargeting=true
```

### Test Framework

- **Framework:** xUnit
- **Mocking:** Moq, NSubstitute
- **Assertions:** FluentAssertions
- **Coverage:** coverlet

### Make Commands (Alternative)

```bash
make help           # Show all available commands
make build          # Build the project
make test           # Run tests
make run-ui         # Run with web dashboard
make docker         # Build and start Docker container
make doctor         # Run environment diagnostics
```

### Running the Application

```bash
# Basic run (smoke test with no provider)
dotnet run --project src/Meridian/Meridian.csproj

# Run with web dashboard
dotnet run --project src/Meridian/Meridian.csproj -- --ui --http-port 8080

# Historical backfill
dotnet run --project src/Meridian/Meridian.csproj -- --backfill --backfill-provider stooq --backfill-symbols SPY,AAPL
```

## Project Structure

### Solution Layout

```text
Meridian-main
├── .claude
│   ├── agents
│   │   ├── meridian-blueprint.md
│   │   ├── meridian-cleanup.md
│   │   ├── meridian-docs.md
│   │   └── meridian-navigation.md
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
│   │   ├── meridian-implementation-assurance
│   │   │   ├── references
│   │   │   │   ├── documentation-routing.md
│   │   │   │   └── evaluation-harness.md
│   │   │   ├── scripts
│   │   │   │   ├── doc_route.py
│   │   │   │   └── score_eval.py
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
│   │   ├── meridian-archive-organizer
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   └── evals.json
│   │   │   ├── references
│   │   │   │   ├── archive-placement-guide.md
│   │   │   │   └── evaluation-harness.md
│   │   │   ├── scripts
│   │   │   │   ├── run_evals.py
│   │   │   │   ├── score_eval.py
│   │   │   │   └── trace_archive_candidates.py
│   │   │   └── SKILL.md
│   │   ├── meridian-blueprint
│   │   │   ├── references
│   │   │   │   └── blueprint-patterns.md
│   │   │   └── SKILL.md
│   │   ├── meridian-brainstorm
│   │   │   ├── references
│   │   │   │   └── competitive-landscape.md
│   │   │   └── SKILL.md
│   │   ├── meridian-cleanup
│   │   │   ├── scripts
│   │   │   │   └── repo-updater.ps1
│   │   │   └── SKILL.md
│   │   ├── meridian-code-review
│   │   │   └── SKILL.md
│   │   ├── meridian-implementation-assurance
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   ├── evals.json
│   │   │   │   ├── meridian-implementation-assurance.prompts.csv
│   │   │   │   └── style-rubric.schema.json
│   │   │   ├── references
│   │   │   │   ├── documentation-routing.md
│   │   │   │   └── evaluation-harness.md
│   │   │   ├── scripts
│   │   │   │   ├── doc_route.py
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── meridian-provider-builder
│   │   │   ├── references
│   │   │   │   └── provider-patterns.md
│   │   │   └── SKILL.md
│   │   ├── meridian-repo-navigation
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   └── SKILL.md
│   │   ├── meridian-roadmap-strategist
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
│   ├── devcontainer.json
│   ├── docker-compose.yml
│   └── Dockerfile
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
│   │   ├── code-review-agent.md
│   │   ├── documentation-agent.md
│   │   ├── implementation-assurance-agent.md
│   │   ├── performance-agent.md
│   │   ├── provider-builder-agent.md
│   │   ├── repo-navigation-agent.md
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
│   │   ├── codeql.yml
│   │   ├── copilot-pull-request-reviewer.yml
│   │   ├── copilot-setup-steps.yml
│   │   ├── copilot-swe-agent-copilot.yml
│   │   ├── desktop-builds.yml
│   │   ├── docker.yml
│   │   ├── documentation.yml
│   │   ├── export-project-artifact.yml
│   │   ├── export-standalone-exe.yml
│   │   ├── generate-build-artifact.yml
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
│   │   ├── refresh-screenshots.yml
│   │   ├── release.yml
│   │   ├── repo-health.yml
│   │   ├── reusable-ai-analysis.yml
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
│       │   ├── generate-ai-navigation.py
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
│       ├── run
│       │   ├── start-collector.ps1
│       │   ├── start-collector.sh
│       │   ├── stop-collector.ps1
│       │   └── stop-collector.sh
│       ├── tests
│       │   ├── test_generate_ai_navigation.py
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
│   │   ├── generated
│   │   │   ├── repo-navigation.json
│   │   │   └── repo-navigation.md
│   │   ├── instructions
│   │   │   └── README.md
│   │   ├── navigation
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
│   │   ├── ledger-architecture.md
│   │   ├── overview.md
│   │   ├── provider-management.md
│   │   ├── README.md
│   │   ├── storage-design.md
│   │   ├── ui-redesign.md
│   │   ├── why-this-architecture.md
│   │   ├── wpf-shell-mvvm.md
│   │   └── wpf-workstation-shell-ux.md
│   ├── audits
│   │   ├── audit-architecture-results.txt
│   │   ├── audit-code-results.json
│   │   ├── audit-results-full.json
│   │   ├── AUDIT_REPORT.md
│   │   ├── BACKTEST_ENGINE_CODE_REVIEW_2026_03_25.md
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
│   │   ├── desktop-workflow-automation.md
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
│   │   │   ├── Activity Diagram - Data Collection Process Flow.png
│   │   │   ├── Activity Diagram - Data Collection Process Flow.svg
│   │   │   ├── Activity Diagram - Historical Backfill Process.png
│   │   │   ├── Activity Diagram - Historical Backfill Process.svg
│   │   │   ├── activity-diagram-backfill.png
│   │   │   ├── activity-diagram-backfill.puml
│   │   │   ├── activity-diagram.png
│   │   │   ├── activity-diagram.puml
│   │   │   ├── Class Diagram - WPF MVVM Architecture.png
│   │   │   ├── Class Diagram - WPF MVVM Architecture.svg
│   │   │   ├── class-diagram-wpf-mvvm.puml
│   │   │   ├── Communication Diagram - Component Message Exchange.png
│   │   │   ├── Communication Diagram - Component Message Exchange.svg
│   │   │   ├── communication-diagram.png
│   │   │   ├── communication-diagram.puml
│   │   │   ├── Interaction Overview Diagram - System Workflow.png
│   │   │   ├── Interaction Overview Diagram - System Workflow.svg
│   │   │   ├── interaction-overview-diagram.png
│   │   │   ├── interaction-overview-diagram.puml
│   │   │   ├── README.md
│   │   │   ├── Sequence Diagram - Backtesting Engine.png
│   │   │   ├── Sequence Diagram - Backtesting Engine.svg
│   │   │   ├── Sequence Diagram - Historical Backfill Flow.png
│   │   │   ├── Sequence Diagram - Historical Backfill Flow.svg
│   │   │   ├── Sequence Diagram - Paper Trading Order Execution.png
│   │   │   ├── Sequence Diagram - Paper Trading Order Execution.svg
│   │   │   ├── Sequence Diagram - Real-Time Data Collection Flow.png
│   │   │   ├── Sequence Diagram - Real-Time Data Collection Flow.svg
│   │   │   ├── Sequence Diagram - Strategy Promotion Lifecycle.png
│   │   │   ├── Sequence Diagram - Strategy Promotion Lifecycle.svg
│   │   │   ├── Sequence Diagram - WAL Durability and Crash-Safe Writes.png
│   │   │   ├── Sequence Diagram - WAL Durability and Crash-Safe Writes.svg
│   │   │   ├── sequence-diagram-backfill.png
│   │   │   ├── sequence-diagram-backfill.puml
│   │   │   ├── sequence-diagram-backtesting.puml
│   │   │   ├── sequence-diagram-paper-trading.puml
│   │   │   ├── sequence-diagram-strategy-promotion.puml
│   │   │   ├── sequence-diagram-wal-durability.puml
│   │   │   ├── sequence-diagram.png
│   │   │   ├── sequence-diagram.puml
│   │   │   ├── State Diagram - Backfill Request States.png
│   │   │   ├── State Diagram - Backfill Request States.svg
│   │   │   ├── State Diagram - Order Book Stream States.png
│   │   │   ├── State Diagram - Order Book Stream States.svg
│   │   │   ├── State Diagram - Provider Connection States.png
│   │   │   ├── State Diagram - Provider Connection States.svg
│   │   │   ├── State Diagram - Trade Sequence Validation States.png
│   │   │   ├── State Diagram - Trade Sequence Validation States.svg
│   │   │   ├── state-diagram-backfill.png
│   │   │   ├── state-diagram-backfill.puml
│   │   │   ├── state-diagram-orderbook.png
│   │   │   ├── state-diagram-orderbook.puml
│   │   │   ├── state-diagram-trade-sequence.png
│   │   │   ├── state-diagram-trade-sequence.puml
│   │   │   ├── state-diagram.png
│   │   │   ├── state-diagram.puml
│   │   │   ├── Timing Diagram - Backfill Operation Timeline.png
│   │   │   ├── Timing Diagram - Backfill Operation Timeline.svg
│   │   │   ├── Timing Diagram - Event Processing Timeline.png
│   │   │   ├── Timing Diagram - Event Processing Timeline.svg
│   │   │   ├── timing-diagram-backfill.png
│   │   │   ├── timing-diagram-backfill.puml
│   │   │   ├── timing-diagram.png
│   │   │   ├── timing-diagram.puml
│   │   │   ├── Use Case Diagram - Meridian.png
│   │   │   ├── Use Case Diagram - Meridian.svg
│   │   │   ├── use-case-diagram.png
│   │   │   └── use-case-diagram.puml
│   │   ├── backfill-workflow.dot
│   │   ├── backfill-workflow.png
│   │   ├── backfill-workflow.svg
│   │   ├── backtesting-engine.dot
│   │   ├── backtesting-engine.png
│   │   ├── backtesting-engine.svg
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
│   │   ├── configuration-management.png
│   │   ├── configuration-management.svg
│   │   ├── data-flow.dot
│   │   ├── data-flow.png
│   │   ├── data-flow.svg
│   │   ├── data-quality-monitoring.dot
│   │   ├── data-quality-monitoring.png
│   │   ├── data-quality-monitoring.svg
│   │   ├── deployment-options.dot
│   │   ├── deployment-options.png
│   │   ├── deployment-options.svg
│   │   ├── domain-event-model.dot
│   │   ├── domain-event-model.png
│   │   ├── domain-event-model.svg
│   │   ├── event-pipeline-sequence.dot
│   │   ├── event-pipeline-sequence.png
│   │   ├── event-pipeline-sequence.svg
│   │   ├── execution-layer.dot
│   │   ├── execution-layer.png
│   │   ├── execution-layer.svg
│   │   ├── fsharp-domain.dot
│   │   ├── fsharp-domain.png
│   │   ├── fsharp-domain.svg
│   │   ├── mcp-server.dot
│   │   ├── mcp-server.png
│   │   ├── mcp-server.svg
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
│   │   ├── strategy-lifecycle.png
│   │   ├── strategy-lifecycle.svg
│   │   ├── symbol-search-resolution.dot
│   │   ├── symbol-search-resolution.png
│   │   ├── symbol-search-resolution.svg
│   │   ├── ui-implementation-flow.dot
│   │   ├── ui-implementation-flow.png
│   │   ├── ui-implementation-flow.svg
│   │   ├── ui-navigation-map.dot
│   │   ├── ui-navigation-map.png
│   │   └── ui-navigation-map.svg
│   ├── docfx
│   │   ├── api
│   │   │   ├── .manifest
│   │   │   ├── index.md
│   │   │   ├── Meridian.Application.Backfill.BackfillCostEstimate.yml
│   │   │   ├── Meridian.Application.Backfill.BackfillCostEstimator.yml
│   │   │   ├── Meridian.Application.Backfill.BackfillCostRequest.yml
│   │   │   ├── Meridian.Application.Backfill.BackfillRequest.yml
│   │   │   ├── Meridian.Application.Backfill.BackfillResult.yml
│   │   │   ├── Meridian.Application.Backfill.BackfillStatusStore.yml
│   │   │   ├── Meridian.Application.Backfill.GapBackfillService.yml
│   │   │   ├── Meridian.Application.Backfill.HistoricalBackfillService.yml
│   │   │   ├── Meridian.Application.Backfill.ProviderCostEstimate.yml
│   │   │   ├── Meridian.Application.Backfill.SymbolValidationSignal.yml
│   │   │   ├── Meridian.Application.Backfill.yml
│   │   │   ├── Meridian.Application.Backtesting.BacktestStudioRunHandle.yml
│   │   │   ├── Meridian.Application.Backtesting.BacktestStudioRunRequest.yml
│   │   │   ├── Meridian.Application.Backtesting.BacktestStudioRunStatus.yml
│   │   │   ├── Meridian.Application.Backtesting.IBacktestStudioEngine.yml
│   │   │   ├── Meridian.Application.Backtesting.yml
│   │   │   ├── Meridian.Application.Banking.BankingException.yml
│   │   │   ├── Meridian.Application.Banking.IBankingService.yml
│   │   │   ├── Meridian.Application.Banking.InMemoryBankingService.yml
│   │   │   ├── Meridian.Application.Banking.yml
│   │   │   ├── Meridian.Application.Canonicalization.CanonicalizationMetrics.yml
│   │   │   ├── Meridian.Application.Canonicalization.CanonicalizationMetricsSnapshot.yml
│   │   │   ├── Meridian.Application.Canonicalization.CanonicalizationSnapshot.yml
│   │   │   ├── Meridian.Application.Canonicalization.CanonicalizingPublisher.yml
│   │   │   ├── Meridian.Application.Canonicalization.ConditionCodeMapper.yml
│   │   │   ├── Meridian.Application.Canonicalization.DefaultCanonicalizationMetrics.yml
│   │   │   ├── Meridian.Application.Canonicalization.EventCanonicalizer.yml
│   │   │   ├── Meridian.Application.Canonicalization.ICanonicalizationMetrics.yml
│   │   │   ├── Meridian.Application.Canonicalization.IEventCanonicalizer.yml
│   │   │   ├── Meridian.Application.Canonicalization.ProviderParitySnapshot.yml
│   │   │   ├── Meridian.Application.Canonicalization.VenueMicMapper.yml
│   │   │   ├── Meridian.Application.Canonicalization.yml
│   │   │   ├── Meridian.Application.Commands.CliArguments.yml
│   │   │   ├── Meridian.Application.Commands.CliResult.yml
│   │   │   ├── Meridian.Application.Commands.ICliCommand.yml
│   │   │   ├── Meridian.Application.Commands.yml
│   │   │   ├── Meridian.Application.Composition.BackfillHostAdapter.yml
│   │   │   ├── Meridian.Application.Composition.CompositionOptions.yml
│   │   │   ├── Meridian.Application.Composition.ConsoleHostAdapter.yml
│   │   │   ├── Meridian.Application.Composition.DesktopHostAdapter.yml
│   │   │   ├── Meridian.Application.Composition.Features.IServiceFeatureRegistration.yml
│   │   │   ├── Meridian.Application.Composition.Features.yml
│   │   │   ├── Meridian.Application.Composition.HostBuilder.yml
│   │   │   ├── Meridian.Application.Composition.HostStartup.yml
│   │   │   ├── Meridian.Application.Composition.HostStartupFactory.yml
│   │   │   ├── Meridian.Application.Composition.IHostAdapter.yml
│   │   │   ├── Meridian.Application.Composition.PipelinePublisher.yml
│   │   │   ├── Meridian.Application.Composition.ServiceCompositionRoot.yml
│   │   │   ├── Meridian.Application.Composition.Startup.DashboardServerFactory.yml
│   │   │   ├── Meridian.Application.Composition.Startup.HostModeOrchestrator.yml
│   │   │   ├── Meridian.Application.Composition.Startup.IHostDashboardServer.yml
│   │   │   ├── Meridian.Application.Composition.Startup.ModeRunners.BackfillModeRunner.yml
│   │   │   ├── Meridian.Application.Composition.Startup.ModeRunners.CollectorModeRunner.yml
│   │   │   ├── Meridian.Application.Composition.Startup.ModeRunners.CommandModeRunner.yml
│   │   │   ├── Meridian.Application.Composition.Startup.ModeRunners.DesktopModeRunner.yml
│   │   │   ├── Meridian.Application.Composition.Startup.ModeRunners.WebModeRunner.yml
│   │   │   ├── Meridian.Application.Composition.Startup.ModeRunners.yml
│   │   │   ├── Meridian.Application.Composition.Startup.SharedStartupBootstrapper.yml
│   │   │   ├── Meridian.Application.Composition.Startup.SharedStartupHelpers.yml
│   │   │   ├── Meridian.Application.Composition.Startup.StartupModels.HostMode.yml
│   │   │   ├── Meridian.Application.Composition.Startup.StartupModels.StartupContext.yml
│   │   │   ├── Meridian.Application.Composition.Startup.StartupModels.StartupPlan.yml
│   │   │   ├── Meridian.Application.Composition.Startup.StartupModels.StartupRequest.yml
│   │   │   ├── Meridian.Application.Composition.Startup.StartupModels.StartupValidationResult.yml
│   │   │   ├── Meridian.Application.Composition.Startup.StartupModels.yml
│   │   │   ├── Meridian.Application.Composition.Startup.StartupOrchestrator.yml
│   │   │   ├── Meridian.Application.Composition.Startup.yml
│   │   │   ├── Meridian.Application.Composition.StreamingHostAdapter.yml
│   │   │   ├── Meridian.Application.Composition.WebHostAdapter.yml
│   │   │   ├── Meridian.Application.Composition.yml
│   │   │   ├── Meridian.Application.Config.AlpacaBackfillConfig.yml
│   │   │   ├── Meridian.Application.Config.AlpacaOptions.yml
│   │   │   ├── Meridian.Application.Config.AlpacaOptionsValidator.yml
│   │   │   ├── Meridian.Application.Config.AlphaVantageConfig.yml
│   │   │   ├── Meridian.Application.Config.AppConfig.yml
│   │   │   ├── Meridian.Application.Config.AppConfigJsonOptions.yml
│   │   │   ├── Meridian.Application.Config.AppConfigValidator.yml
│   │   │   ├── Meridian.Application.Config.BackfillConfig.yml
│   │   │   ├── Meridian.Application.Config.BackfillJobsConfig.yml
│   │   │   ├── Meridian.Application.Config.BackfillProvidersConfig.yml
│   │   │   ├── Meridian.Application.Config.BinanceConfig.yml
│   │   │   ├── Meridian.Application.Config.CanonicalizationConfig.yml
│   │   │   ├── Meridian.Application.Config.CoinbaseConfig.yml
│   │   │   ├── Meridian.Application.Config.ConfigDtoMapper.yml
│   │   │   ├── Meridian.Application.Config.ConfigJsonSchemaGenerator.yml
│   │   │   ├── Meridian.Application.Config.ConfigurationChangedEventArgs.yml
│   │   │   ├── Meridian.Application.Config.ConfigurationMetadata.yml
│   │   │   ├── Meridian.Application.Config.ConfigurationOrigin.yml
│   │   │   ├── Meridian.Application.Config.ConfigurationPipeline.yml
│   │   │   ├── Meridian.Application.Config.ConfigurationProviderExtensions.yml
│   │   │   ├── Meridian.Application.Config.ConfigurationSection.yml
│   │   │   ├── Meridian.Application.Config.ConfigurationSource.yml
│   │   │   ├── Meridian.Application.Config.ConfigurationValidationError.yml
│   │   │   ├── Meridian.Application.Config.ConfigurationValidationResult.yml
│   │   │   ├── Meridian.Application.Config.ConfigurationValidationWarning.yml
│   │   │   ├── Meridian.Application.Config.ConfigValidationPipeline.yml
│   │   │   ├── Meridian.Application.Config.ConfigValidationResult.yml
│   │   │   ├── Meridian.Application.Config.ConfigValidationSeverity.yml
│   │   │   ├── Meridian.Application.Config.ConfigValidatorCli.yml
│   │   │   ├── Meridian.Application.Config.ConfigWatcher.yml
│   │   │   ├── Meridian.Application.Config.CoordinationConfig.yml
│   │   │   ├── Meridian.Application.Config.CoordinationMode.yml
│   │   │   ├── Meridian.Application.Config.CQGConfig.yml
│   │   │   ├── Meridian.Application.Config.CQGConfigValidator.yml
│   │   │   ├── Meridian.Application.Config.Credentials.CredentialAuthStatus.yml
│   │   │   ├── Meridian.Application.Config.Credentials.CredentialExpirationConfig.yml
│   │   │   ├── Meridian.Application.Config.Credentials.CredentialStatusSummary.yml
│   │   │   ├── Meridian.Application.Config.Credentials.CredentialTestingService.yml
│   │   │   ├── Meridian.Application.Config.Credentials.CredentialTestResult.yml
│   │   │   ├── Meridian.Application.Config.Credentials.OAuthProviderConfig.yml
│   │   │   ├── Meridian.Application.Config.Credentials.OAuthRefreshResult.yml
│   │   │   ├── Meridian.Application.Config.Credentials.OAuthToken.yml
│   │   │   ├── Meridian.Application.Config.Credentials.OAuthTokenRefreshService.TokenStatus.yml
│   │   │   ├── Meridian.Application.Config.Credentials.OAuthTokenRefreshService.yml
│   │   │   ├── Meridian.Application.Config.Credentials.ProviderCredentialResolver.yml
│   │   │   ├── Meridian.Application.Config.Credentials.StoredCredentialStatus.yml
│   │   │   ├── Meridian.Application.Config.Credentials.yml
│   │   │   ├── Meridian.Application.Config.CredentialSecurityStage.yml
│   │   │   ├── Meridian.Application.Config.DataSourceConfig.yml
│   │   │   ├── Meridian.Application.Config.DataSourceKind.yml
│   │   │   ├── Meridian.Application.Config.DataSourceKindConverter.yml
│   │   │   ├── Meridian.Application.Config.DataSourcesConfig.yml
│   │   │   ├── Meridian.Application.Config.DataSourceType.yml
│   │   │   ├── Meridian.Application.Config.DefaultScheduleConfig.yml
│   │   │   ├── Meridian.Application.Config.DeploymentContext.yml
│   │   │   ├── Meridian.Application.Config.DeploymentMode.yml
│   │   │   ├── Meridian.Application.Config.DerivativesConfig.yml
│   │   │   ├── Meridian.Application.Config.FailoverRuleConfig.yml
│   │   │   ├── Meridian.Application.Config.FieldValidationStage.yml
│   │   │   ├── Meridian.Application.Config.FinnhubConfig.yml
│   │   │   ├── Meridian.Application.Config.FredConfig.yml
│   │   │   ├── Meridian.Application.Config.IBOptions.yml
│   │   │   ├── Meridian.Application.Config.IConfigurationProvider.yml
│   │   │   ├── Meridian.Application.Config.IConfigValidationStage.yml
│   │   │   ├── Meridian.Application.Config.IConfigValidator.yml
│   │   │   ├── Meridian.Application.Config.IndexOptionsConfig.yml
│   │   │   ├── Meridian.Application.Config.IQFeedConfig.yml
│   │   │   ├── Meridian.Application.Config.IQFeedConfigValidator.yml
│   │   │   ├── Meridian.Application.Config.KrakenConfig.yml
│   │   │   ├── Meridian.Application.Config.NasdaqDataLinkConfig.yml
│   │   │   ├── Meridian.Application.Config.OpenFigiConfig.yml
│   │   │   ├── Meridian.Application.Config.PipelineOptions.yml
│   │   │   ├── Meridian.Application.Config.PolygonConfig.yml
│   │   │   ├── Meridian.Application.Config.PolygonOptions.yml
│   │   │   ├── Meridian.Application.Config.ProviderBindingConfig.yml
│   │   │   ├── Meridian.Application.Config.ProviderCertificationConfig.yml
│   │   │   ├── Meridian.Application.Config.ProviderConnectionConfig.yml
│   │   │   ├── Meridian.Application.Config.ProviderConnectionsConfig.yml
│   │   │   ├── Meridian.Application.Config.ProviderOptionsBase.yml
│   │   │   ├── Meridian.Application.Config.ProviderPolicyConfig.yml
│   │   │   ├── Meridian.Application.Config.ProviderPresetConfig.yml
│   │   │   ├── Meridian.Application.Config.ProviderRegistryConfig.yml
│   │   │   ├── Meridian.Application.Config.RithmicConfig.yml
│   │   │   ├── Meridian.Application.Config.RithmicConfigValidator.yml
│   │   │   ├── Meridian.Application.Config.RobinhoodConfig.yml
│   │   │   ├── Meridian.Application.Config.ScheduledBackfillConfig.yml
│   │   │   ├── Meridian.Application.Config.SelfHealingFix.yml
│   │   │   ├── Meridian.Application.Config.SelfHealingSeverity.yml
│   │   │   ├── Meridian.Application.Config.SelfHealingStrictness.yml
│   │   │   ├── Meridian.Application.Config.SemanticValidationStage.yml
│   │   │   ├── Meridian.Application.Config.SensitiveValueMasker.yml
│   │   │   ├── Meridian.Application.Config.SourceRegistryConfig.yml
│   │   │   ├── Meridian.Application.Config.StockSharpConfig.yml
│   │   │   ├── Meridian.Application.Config.StockSharpConfigValidator.yml
│   │   │   ├── Meridian.Application.Config.StockSharpIBConfig.yml
│   │   │   ├── Meridian.Application.Config.StockSharpIBConfigValidator.yml
│   │   │   ├── Meridian.Application.Config.StooqConfig.yml
│   │   │   ├── Meridian.Application.Config.StorageConfig.yml
│   │   │   ├── Meridian.Application.Config.StorageConfigExtensions.yml
│   │   │   ├── Meridian.Application.Config.StorageConfigValidator.yml
│   │   │   ├── Meridian.Application.Config.SymbolConfigValidator.yml
│   │   │   ├── Meridian.Application.Config.SymbolMappingConfig.yml
│   │   │   ├── Meridian.Application.Config.SymbolMappingsConfig.yml
│   │   │   ├── Meridian.Application.Config.SyntheticMarketDataConfig.yml
│   │   │   ├── Meridian.Application.Config.TiingoConfig.yml
│   │   │   ├── Meridian.Application.Config.ValidatedConfig.yml
│   │   │   ├── Meridian.Application.Config.ValidationPipelineConfig.yml
│   │   │   ├── Meridian.Application.Config.YahooFinanceConfig.yml
│   │   │   ├── Meridian.Application.Config.yml
│   │   │   ├── Meridian.Application.Coordination.ClusterCoordinatorService.yml
│   │   │   ├── Meridian.Application.Coordination.CoordinationSnapshot.yml
│   │   │   ├── Meridian.Application.Coordination.IClusterCoordinator.yml
│   │   │   ├── Meridian.Application.Coordination.ICoordinationStore.yml
│   │   │   ├── Meridian.Application.Coordination.ILeaseManager.yml
│   │   │   ├── Meridian.Application.Coordination.IScheduledWorkOwnershipService.yml
│   │   │   ├── Meridian.Application.Coordination.ISubscriptionOwnershipService.yml
│   │   │   ├── Meridian.Application.Coordination.LeadershipChangedEventArgs.yml
│   │   │   ├── Meridian.Application.Coordination.LeaseAcquireResult.yml
│   │   │   ├── Meridian.Application.Coordination.LeaseManager.yml
│   │   │   ├── Meridian.Application.Coordination.LeaseRecord.yml
│   │   │   ├── Meridian.Application.Coordination.ScheduledWorkOwnershipService.yml
│   │   │   ├── Meridian.Application.Coordination.SharedStorageCoordinationStore.yml
│   │   │   ├── Meridian.Application.Coordination.SplitBrainDetector.yml
│   │   │   ├── Meridian.Application.Coordination.SubscriptionOwnershipService.yml
│   │   │   ├── Meridian.Application.Coordination.yml
│   │   │   ├── Meridian.Application.Credentials.CredentialMetadata.yml
│   │   │   ├── Meridian.Application.Credentials.CredentialResult.yml
│   │   │   ├── Meridian.Application.Credentials.CredentialSource.yml
│   │   │   ├── Meridian.Application.Credentials.CredentialStoreExtensions.yml
│   │   │   ├── Meridian.Application.Credentials.CredentialType.yml
│   │   │   ├── Meridian.Application.Credentials.CredentialValidationResult.yml
│   │   │   ├── Meridian.Application.Credentials.ICredentialStore.yml
│   │   │   ├── Meridian.Application.Credentials.yml
│   │   │   ├── Meridian.Application.DirectLending.DailyAccrualWorker.yml
│   │   │   ├── Meridian.Application.DirectLending.DirectLendingEventRebuilder.yml
│   │   │   ├── Meridian.Application.DirectLending.DirectLendingOutboxDispatcher.yml
│   │   │   ├── Meridian.Application.DirectLending.IDirectLendingCommandService.yml
│   │   │   ├── Meridian.Application.DirectLending.IDirectLendingQueryService.yml
│   │   │   ├── Meridian.Application.DirectLending.IDirectLendingService.yml
│   │   │   ├── Meridian.Application.DirectLending.InMemoryDirectLendingService.yml
│   │   │   ├── Meridian.Application.DirectLending.PostgresDirectLendingCommandService.yml
│   │   │   ├── Meridian.Application.DirectLending.PostgresDirectLendingQueryService.yml
│   │   │   ├── Meridian.Application.DirectLending.PostgresDirectLendingService.yml
│   │   │   ├── Meridian.Application.DirectLending.yml
│   │   │   ├── Meridian.Application.Etl.EtlExportResult.yml
│   │   │   ├── Meridian.Application.Etl.EtlExportService.yml
│   │   │   ├── Meridian.Application.Etl.EtlJobDefinitionStore.yml
│   │   │   ├── Meridian.Application.Etl.EtlJobOrchestrator.yml
│   │   │   ├── Meridian.Application.Etl.EtlJobService.yml
│   │   │   ├── Meridian.Application.Etl.EtlNormalizationService.yml
│   │   │   ├── Meridian.Application.Etl.EtlRunResult.yml
│   │   │   ├── Meridian.Application.Etl.IEtlExportService.yml
│   │   │   ├── Meridian.Application.Etl.IEtlJobDefinitionStore.yml
│   │   │   ├── Meridian.Application.Etl.IEtlJobService.yml
│   │   │   ├── Meridian.Application.Etl.NormalizationOutcome.yml
│   │   │   ├── Meridian.Application.Etl.PartnerSchemaRegistry.yml
│   │   │   ├── Meridian.Application.Etl.yml
│   │   │   ├── Meridian.Application.Exceptions.ConfigurationException.yml
│   │   │   ├── Meridian.Application.Exceptions.ConnectionException.yml
│   │   │   ├── Meridian.Application.Exceptions.DataProviderException.yml
│   │   │   ├── Meridian.Application.Exceptions.MeridianException.yml
│   │   │   ├── Meridian.Application.Exceptions.OperationTimeoutException.yml
│   │   │   ├── Meridian.Application.Exceptions.RateLimitException.yml
│   │   │   ├── Meridian.Application.Exceptions.SequenceValidationException.yml
│   │   │   ├── Meridian.Application.Exceptions.SequenceValidationType.yml
│   │   │   ├── Meridian.Application.Exceptions.StorageException.yml
│   │   │   ├── Meridian.Application.Exceptions.ValidationError.yml
│   │   │   ├── Meridian.Application.Exceptions.ValidationException.yml
│   │   │   ├── Meridian.Application.Exceptions.yml
│   │   │   ├── Meridian.Application.Filters.MarketEventFilter.yml
│   │   │   ├── Meridian.Application.Filters.yml
│   │   │   ├── Meridian.Application.FundAccounts.IFundAccountService.yml
│   │   │   ├── Meridian.Application.FundAccounts.InMemoryFundAccountService.yml
│   │   │   ├── Meridian.Application.FundAccounts.yml
│   │   │   ├── Meridian.Application.Indicators.HistoricalIndicatorResult.yml
│   │   │   ├── Meridian.Application.Indicators.IndicatorConfiguration.yml
│   │   │   ├── Meridian.Application.Indicators.IndicatorDataPoint.yml
│   │   │   ├── Meridian.Application.Indicators.IndicatorSnapshot.yml
│   │   │   ├── Meridian.Application.Indicators.IndicatorType.yml
│   │   │   ├── Meridian.Application.Indicators.TechnicalIndicatorService.yml
│   │   │   ├── Meridian.Application.Indicators.yml
│   │   │   ├── Meridian.Application.Logging.LoggingSetup.yml
│   │   │   ├── Meridian.Application.Logging.yml
│   │   │   ├── Meridian.Application.Monitoring.BackpressureAlert.yml
│   │   │   ├── Meridian.Application.Monitoring.BackpressureAlertConfig.yml
│   │   │   ├── Meridian.Application.Monitoring.BackpressureAlertService.yml
│   │   │   ├── Meridian.Application.Monitoring.BackpressureLevel.yml
│   │   │   ├── Meridian.Application.Monitoring.BackpressureResolvedEvent.yml
│   │   │   ├── Meridian.Application.Monitoring.BackpressureStatus.yml
│   │   │   ├── Meridian.Application.Monitoring.BadTickAlert.yml
│   │   │   ├── Meridian.Application.Monitoring.BadTickFilter.yml
│   │   │   ├── Meridian.Application.Monitoring.BadTickFilterConfig.yml
│   │   │   ├── Meridian.Application.Monitoring.BadTickFilterStats.yml
│   │   │   ├── Meridian.Application.Monitoring.BadTickReason.yml
│   │   │   ├── Meridian.Application.Monitoring.CircuitBreakerDashboard.yml
│   │   │   ├── Meridian.Application.Monitoring.CircuitBreakerState.yml
│   │   │   ├── Meridian.Application.Monitoring.CircuitBreakerStateChange.yml
│   │   │   ├── Meridian.Application.Monitoring.CircuitBreakerStatus.yml
│   │   │   ├── Meridian.Application.Monitoring.CircuitBreakerStatusService.yml
│   │   │   ├── Meridian.Application.Monitoring.ClockSkewEstimator.yml
│   │   │   ├── Meridian.Application.Monitoring.ClockSkewSnapshot.yml
│   │   │   ├── Meridian.Application.Monitoring.CombinedMetricsSnapshot.yml
│   │   │   ├── Meridian.Application.Monitoring.ConnectionHealthConfig.yml
│   │   │   ├── Meridian.Application.Monitoring.ConnectionHealthMonitor.yml
│   │   │   ├── Meridian.Application.Monitoring.ConnectionHealthSnapshot.yml
│   │   │   ├── Meridian.Application.Monitoring.ConnectionLostEvent.yml
│   │   │   ├── Meridian.Application.Monitoring.ConnectionRecoveredEvent.yml
│   │   │   ├── Meridian.Application.Monitoring.ConnectionStatus.yml
│   │   │   ├── Meridian.Application.Monitoring.ConnectionStatusWebhook.yml
│   │   │   ├── Meridian.Application.Monitoring.ConnectionStatusWebhookConfig.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.AggregatedHealthReport.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.AlertCategory.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.AlertDispatcher.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.AlertFilter.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.AlertRunbookEntry.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.AlertRunbookRegistry.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.AlertSeverity.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.AlertStatistics.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.HealthCheckAggregator.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.HealthCheckResult.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.HealthSeverity.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.IAlertDispatcher.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.IHealthCheckAggregator.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.IHealthCheckProvider.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.MonitoringAlert.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.SloComplianceDashboard.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.SloComplianceResult.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.SloComplianceState.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.SloDefinition.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.SloDefinitionRegistry.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.SloDefinitionSummary.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.SloSubsystem.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.SloSubsystemSummary.yml
│   │   │   ├── Meridian.Application.Monitoring.Core.yml
│   │   │   ├── Meridian.Application.Monitoring.DataLossAccounting.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.AggregatedQualityReport.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.AnomalyDetectionConfig.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.AnomalyDetector.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.AnomalySeverity.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.AnomalyStatistics.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.AnomalyType.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.CompletenessConfig.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.CompletenessScore.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.CompletenessScoreCalculator.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.CompletenessSummary.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.CrossProviderComparison.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.CrossProviderComparisonService.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.CrossProviderConfig.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.DailyQualityReport.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.DataAnomaly.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.DataFreshnessSlaMonitor.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.DataGap.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.DataQualityDashboard.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.DataQualityEndpoints.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.DataQualityMonitoringConfig.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.DataQualityMonitoringService.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.DataQualityReportGenerator.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.DiscontinuityType.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.DiscrepancySeverity.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.GapAnalysisResult.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.GapAnalyzer.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.GapAnalyzerConfig.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.GapSeverity.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.GapStatistics.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.HealthState.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.HistogramBucket.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.IQualityAnalysisEngine.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.IQualityAnalyzer-1.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.IQualityAnalyzerMetadata.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.IQualityAnalyzerRegistry.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.LatencyDistribution.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.LatencyHistogram.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.LatencyHistogramConfig.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.LatencyStatistics.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.LiquidityProfileProvider.LiquidityThresholds.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.LiquidityProfileProvider.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.PriceContinuityChecker.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.PriceContinuityConfig.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.PriceContinuityResult.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.PriceContinuityStatistics.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.PriceDiscontinuityEvent.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.ProviderComparisonStatistics.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.ProviderDataSummary.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.ProviderDiscrepancy.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.QualityAnalysisResult.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.QualityAnalyzerConfig.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.QualityIssue.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.QualityIssueCategory.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.QualityIssueSeverity.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.RealTimeQualityMetrics.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.ReportExportFormat.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.ReportExportRequest.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.ReportGenerationOptions.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.ReportStatistics.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.SequenceError.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.SequenceErrorConfig.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.SequenceErrorStatistics.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.SequenceErrorSummary.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.SequenceErrorTracker.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.SequenceErrorType.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.SlaConfig.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.SlaRecoveryEvent.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.SlaState.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.SlaStatusSnapshot.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.SlaViolationEvent.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.SymbolHealthStatus.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.SymbolPriceStatistics.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.SymbolQualitySummary.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.SymbolSlaStatus.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.TimelineEntry.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.TimelineEntryType.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.WeeklyQualityReport.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.WeeklyStatistics.yml
│   │   │   ├── Meridian.Application.Monitoring.DataQuality.yml
│   │   │   ├── Meridian.Application.Monitoring.DefaultEventMetrics.yml
│   │   │   ├── Meridian.Application.Monitoring.DependencyRecoveredEvent.yml
│   │   │   ├── Meridian.Application.Monitoring.DependencyUnhealthyEvent.yml
│   │   │   ├── Meridian.Application.Monitoring.DetailedHealthCheck.yml
│   │   │   ├── Meridian.Application.Monitoring.DetailedHealthCheckConfig.yml
│   │   │   ├── Meridian.Application.Monitoring.DetailedHealthReport.yml
│   │   │   ├── Meridian.Application.Monitoring.DetailedHealthStatus.yml
│   │   │   ├── Meridian.Application.Monitoring.DiskSpaceInfo.yml
│   │   │   ├── Meridian.Application.Monitoring.ErrorEntry.yml
│   │   │   ├── Meridian.Application.Monitoring.ErrorLevel.yml
│   │   │   ├── Meridian.Application.Monitoring.ErrorRingBuffer.yml
│   │   │   ├── Meridian.Application.Monitoring.ErrorStats.yml
│   │   │   ├── Meridian.Application.Monitoring.EventSchemaValidator.yml
│   │   │   ├── Meridian.Application.Monitoring.HealthCheckItem.yml
│   │   │   ├── Meridian.Application.Monitoring.HealthSummary.yml
│   │   │   ├── Meridian.Application.Monitoring.HealthWarningCategory.yml
│   │   │   ├── Meridian.Application.Monitoring.HealthWarningSeverity.yml
│   │   │   ├── Meridian.Application.Monitoring.HeartbeatMissedEvent.yml
│   │   │   ├── Meridian.Application.Monitoring.HighLatencyEvent.yml
│   │   │   ├── Meridian.Application.Monitoring.IConnectionHealthMonitor.yml
│   │   │   ├── Meridian.Application.Monitoring.IEventMetrics.yml
│   │   │   ├── Meridian.Application.Monitoring.IReconnectionMetrics.yml
│   │   │   ├── Meridian.Application.Monitoring.LatencyBucket.yml
│   │   │   ├── Meridian.Application.Monitoring.LuldBand.yml
│   │   │   ├── Meridian.Application.Monitoring.MemoryInfo.yml
│   │   │   ├── Meridian.Application.Monitoring.Metrics.yml
│   │   │   ├── Meridian.Application.Monitoring.MetricsSnapshot.yml
│   │   │   ├── Meridian.Application.Monitoring.MigrationDiagnostics.yml
│   │   │   ├── Meridian.Application.Monitoring.MigrationDiagnosticsSnapshot.yml
│   │   │   ├── Meridian.Application.Monitoring.MonotonicityStats.yml
│   │   │   ├── Meridian.Application.Monitoring.MonotonicityViolation.yml
│   │   │   ├── Meridian.Application.Monitoring.NullReconnectionMetrics.yml
│   │   │   ├── Meridian.Application.Monitoring.PrometheusMetrics.yml
│   │   │   ├── Meridian.Application.Monitoring.PrometheusMetricsUpdater.yml
│   │   │   ├── Meridian.Application.Monitoring.PrometheusReconnectionMetrics.yml
│   │   │   ├── Meridian.Application.Monitoring.ProviderDegradationConfig.yml
│   │   │   ├── Meridian.Application.Monitoring.ProviderDegradationScore.yml
│   │   │   ├── Meridian.Application.Monitoring.ProviderDegradationScorer.yml
│   │   │   ├── Meridian.Application.Monitoring.ProviderDegradedEvent.yml
│   │   │   ├── Meridian.Application.Monitoring.ProviderLatencyConfig.yml
│   │   │   ├── Meridian.Application.Monitoring.ProviderLatencyHistogram.yml
│   │   │   ├── Meridian.Application.Monitoring.ProviderLatencyService.yml
│   │   │   ├── Meridian.Application.Monitoring.ProviderLatencyStats.yml
│   │   │   ├── Meridian.Application.Monitoring.ProviderLatencySummary.yml
│   │   │   ├── Meridian.Application.Monitoring.ProviderMetrics.yml
│   │   │   ├── Meridian.Application.Monitoring.ProviderMetricsStatus.yml
│   │   │   ├── Meridian.Application.Monitoring.ProviderReconciliation.yml
│   │   │   ├── Meridian.Application.Monitoring.ProviderRecoveredEvent.yml
│   │   │   ├── Meridian.Application.Monitoring.ReconciliationReport.yml
│   │   │   ├── Meridian.Application.Monitoring.SchemaCheckResult.yml
│   │   │   ├── Meridian.Application.Monitoring.SchemaIncompatibility.yml
│   │   │   ├── Meridian.Application.Monitoring.SchemaValidationOptions.yml
│   │   │   ├── Meridian.Application.Monitoring.SchemaValidationService.yml
│   │   │   ├── Meridian.Application.Monitoring.SpreadMonitor.yml
│   │   │   ├── Meridian.Application.Monitoring.SpreadMonitorConfig.yml
│   │   │   ├── Meridian.Application.Monitoring.SpreadMonitorStats.yml
│   │   │   ├── Meridian.Application.Monitoring.SpreadSnapshot.yml
│   │   │   ├── Meridian.Application.Monitoring.StartupSchemaCheckResult.yml
│   │   │   ├── Meridian.Application.Monitoring.StatusHttpServer.yml
│   │   │   ├── Meridian.Application.Monitoring.StatusSnapshot.yml
│   │   │   ├── Meridian.Application.Monitoring.StatusWriter.yml
│   │   │   ├── Meridian.Application.Monitoring.SymbolBadTickStats.yml
│   │   │   ├── Meridian.Application.Monitoring.SymbolMonotonicityStats.yml
│   │   │   ├── Meridian.Application.Monitoring.SymbolSpreadStats.yml
│   │   │   ├── Meridian.Application.Monitoring.SymbolTickSizeStats.yml
│   │   │   ├── Meridian.Application.Monitoring.SystemHealthChecker.yml
│   │   │   ├── Meridian.Application.Monitoring.SystemHealthConfig.yml
│   │   │   ├── Meridian.Application.Monitoring.SystemHealthSnapshot.yml
│   │   │   ├── Meridian.Application.Monitoring.SystemHealthStatus.yml
│   │   │   ├── Meridian.Application.Monitoring.SystemHealthWarning.yml
│   │   │   ├── Meridian.Application.Monitoring.TickSizePriceType.yml
│   │   │   ├── Meridian.Application.Monitoring.TickSizeValidator.yml
│   │   │   ├── Meridian.Application.Monitoring.TickSizeValidatorConfig.yml
│   │   │   ├── Meridian.Application.Monitoring.TickSizeValidatorStats.yml
│   │   │   ├── Meridian.Application.Monitoring.TickSizeViolationAlert.yml
│   │   │   ├── Meridian.Application.Monitoring.TimestampGapAlert.yml
│   │   │   ├── Meridian.Application.Monitoring.TimestampMonotonicityChecker.yml
│   │   │   ├── Meridian.Application.Monitoring.TimestampMonotonicityConfig.yml
│   │   │   ├── Meridian.Application.Monitoring.ValidationMetrics.yml
│   │   │   ├── Meridian.Application.Monitoring.ValidationMetricsSnapshot.yml
│   │   │   ├── Meridian.Application.Monitoring.WideSpreadAlert.yml
│   │   │   ├── Meridian.Application.Monitoring.yml
│   │   │   ├── Meridian.Application.Pipeline.DeadLetterSink.yml
│   │   │   ├── Meridian.Application.Pipeline.DeadLetterStatistics.yml
│   │   │   ├── Meridian.Application.Pipeline.DroppedEventAuditTrail.yml
│   │   │   ├── Meridian.Application.Pipeline.DroppedEventStatistics.yml
│   │   │   ├── Meridian.Application.Pipeline.DualPathEventPipeline.yml
│   │   │   ├── Meridian.Application.Pipeline.EventPipeline.yml
│   │   │   ├── Meridian.Application.Pipeline.EventPipelinePolicy.yml
│   │   │   ├── Meridian.Application.Pipeline.FSharpEventValidator.yml
│   │   │   ├── Meridian.Application.Pipeline.HotPathBatchSerializer.yml
│   │   │   ├── Meridian.Application.Pipeline.IDedupStore.yml
│   │   │   ├── Meridian.Application.Pipeline.IEventValidator.yml
│   │   │   ├── Meridian.Application.Pipeline.IngestionJobService.yml
│   │   │   ├── Meridian.Application.Pipeline.IngestionJobSummary.yml
│   │   │   ├── Meridian.Application.Pipeline.PersistentDedupLedger.yml
│   │   │   ├── Meridian.Application.Pipeline.PipelineStatistics.yml
│   │   │   ├── Meridian.Application.Pipeline.SchemaUpcasterRegistry.yml
│   │   │   ├── Meridian.Application.Pipeline.SchemaUpcasterStatistics.yml
│   │   │   ├── Meridian.Application.Pipeline.ValidationResult.yml
│   │   │   ├── Meridian.Application.Pipeline.yml
│   │   │   ├── Meridian.Application.ProviderRouting.IProviderFamilyCatalogService.yml
│   │   │   ├── Meridian.Application.ProviderRouting.ProviderBindingService.yml
│   │   │   ├── Meridian.Application.ProviderRouting.ProviderCertificationService.yml
│   │   │   ├── Meridian.Application.ProviderRouting.ProviderConnectionService.yml
│   │   │   ├── Meridian.Application.ProviderRouting.ProviderPresetService.yml
│   │   │   ├── Meridian.Application.ProviderRouting.ProviderRouteExplainabilityService.yml
│   │   │   ├── Meridian.Application.ProviderRouting.ProviderRoutingService.yml
│   │   │   ├── Meridian.Application.ProviderRouting.ProviderTrustScoringService.yml
│   │   │   ├── Meridian.Application.ProviderRouting.yml
│   │   │   ├── Meridian.Application.ResultTypes.ErrorCode.yml
│   │   │   ├── Meridian.Application.ResultTypes.ErrorCodeExtensions.yml
│   │   │   ├── Meridian.Application.ResultTypes.OperationError.yml
│   │   │   ├── Meridian.Application.ResultTypes.Result-1.yml
│   │   │   ├── Meridian.Application.ResultTypes.Result-2.yml
│   │   │   ├── Meridian.Application.ResultTypes.Result.yml
│   │   │   ├── Meridian.Application.ResultTypes.yml
│   │   │   ├── Meridian.Application.Scheduling.BackfillExecutionHistory.yml
│   │   │   ├── Meridian.Application.Scheduling.BackfillExecutionLog.yml
│   │   │   ├── Meridian.Application.Scheduling.BackfillSchedule.yml
│   │   │   ├── Meridian.Application.Scheduling.BackfillScheduleManager.yml
│   │   │   ├── Meridian.Application.Scheduling.BackfillSchedulePresets.yml
│   │   │   ├── Meridian.Application.Scheduling.ExecutionStatistics.yml
│   │   │   ├── Meridian.Application.Scheduling.ExecutionStatus.yml
│   │   │   ├── Meridian.Application.Scheduling.ExecutionTrigger.yml
│   │   │   ├── Meridian.Application.Scheduling.IOperationalScheduler.yml
│   │   │   ├── Meridian.Application.Scheduling.ITradingCalendarProvider.yml
│   │   │   ├── Meridian.Application.Scheduling.MaintenanceWindow.yml
│   │   │   ├── Meridian.Application.Scheduling.OperationalScheduler.yml
│   │   │   ├── Meridian.Application.Scheduling.OperationType.yml
│   │   │   ├── Meridian.Application.Scheduling.ProviderUsageStats.yml
│   │   │   ├── Meridian.Application.Scheduling.ResourceRequirements.yml
│   │   │   ├── Meridian.Application.Scheduling.ScheduledBackfillOptions.yml
│   │   │   ├── Meridian.Application.Scheduling.ScheduledBackfillService.yml
│   │   │   ├── Meridian.Application.Scheduling.ScheduledBackfillType.yml
│   │   │   ├── Meridian.Application.Scheduling.ScheduleDecision.yml
│   │   │   ├── Meridian.Application.Scheduling.ScheduleExecutionSummary.yml
│   │   │   ├── Meridian.Application.Scheduling.ScheduleSlot.yml
│   │   │   ├── Meridian.Application.Scheduling.ScheduleStatusSummary.yml
│   │   │   ├── Meridian.Application.Scheduling.SymbolExecutionResult.yml
│   │   │   ├── Meridian.Application.Scheduling.SystemExecutionSummary.yml
│   │   │   ├── Meridian.Application.Scheduling.TradingSession.yml
│   │   │   ├── Meridian.Application.Scheduling.yml
│   │   │   ├── Meridian.Application.SecurityMaster.ILivePositionCorporateActionAdjuster.yml
│   │   │   ├── Meridian.Application.SecurityMaster.ISecurityMasterConflictService.yml
│   │   │   ├── Meridian.Application.SecurityMaster.ISecurityMasterImportService.yml
│   │   │   ├── Meridian.Application.SecurityMaster.ISecurityMasterLedgerBridge.yml
│   │   │   ├── Meridian.Application.SecurityMaster.ISecurityMasterQueryService.yml
│   │   │   ├── Meridian.Application.SecurityMaster.ISecurityResolver.yml
│   │   │   ├── Meridian.Application.SecurityMaster.PositionCorporateActionAdjustment.yml
│   │   │   ├── Meridian.Application.SecurityMaster.SecurityMasterAggregateRebuilder.yml
│   │   │   ├── Meridian.Application.SecurityMaster.SecurityMasterCanonicalSymbolSeedService.yml
│   │   │   ├── Meridian.Application.SecurityMaster.SecurityMasterConflictService.yml
│   │   │   ├── Meridian.Application.SecurityMaster.SecurityMasterCsvParser.yml
│   │   │   ├── Meridian.Application.SecurityMaster.SecurityMasterImportProgress.yml
│   │   │   ├── Meridian.Application.SecurityMaster.SecurityMasterImportResult.yml
│   │   │   ├── Meridian.Application.SecurityMaster.SecurityMasterImportService.yml
│   │   │   ├── Meridian.Application.SecurityMaster.SecurityMasterLedgerBridge.yml
│   │   │   ├── Meridian.Application.SecurityMaster.SecurityMasterOptionsValidator.yml
│   │   │   ├── Meridian.Application.SecurityMaster.SecurityMasterProjectionService.yml
│   │   │   ├── Meridian.Application.SecurityMaster.SecurityMasterProjectionWarmupService.yml
│   │   │   ├── Meridian.Application.SecurityMaster.SecurityMasterQueryService.yml
│   │   │   ├── Meridian.Application.SecurityMaster.SecurityMasterRebuildOrchestrator.yml
│   │   │   ├── Meridian.Application.SecurityMaster.SecurityMasterService.yml
│   │   │   ├── Meridian.Application.SecurityMaster.SecurityResolver.yml
│   │   │   ├── Meridian.Application.SecurityMaster.yml
│   │   │   ├── Meridian.Application.Serialization.AlpacaJsonContext.yml
│   │   │   ├── Meridian.Application.Serialization.AlpacaMessage.yml
│   │   │   ├── Meridian.Application.Serialization.AlpacaQuoteMessage.yml
│   │   │   ├── Meridian.Application.Serialization.AlpacaTradeMessage.yml
│   │   │   ├── Meridian.Application.Serialization.HighPerformanceJson.yml
│   │   │   ├── Meridian.Application.Serialization.JsonBenchmarkUtilities.yml
│   │   │   ├── Meridian.Application.Serialization.MarketDataJsonContext.yml
│   │   │   ├── Meridian.Application.Serialization.yml
│   │   │   ├── Meridian.Application.Services.ApiDocumentationService.yml
│   │   │   ├── Meridian.Application.Services.AssetClassSection.yml
│   │   │   ├── Meridian.Application.Services.AutoConfigurationService.AutoConfigResult.yml
│   │   │   ├── Meridian.Application.Services.AutoConfigurationService.DetectedProvider.yml
│   │   │   ├── Meridian.Application.Services.AutoConfigurationService.yml
│   │   │   ├── Meridian.Application.Services.CanonicalSymbolRegistry.yml
│   │   │   ├── Meridian.Application.Services.ChecklistDisplay.yml
│   │   │   ├── Meridian.Application.Services.CliModeResolver.RunMode.yml
│   │   │   ├── Meridian.Application.Services.CliModeResolver.yml
│   │   │   ├── Meridian.Application.Services.CoLocationProfileActivator.yml
│   │   │   ├── Meridian.Application.Services.ConfigEnvironmentOverride.yml
│   │   │   ├── Meridian.Application.Services.ConfigPreset.yml
│   │   │   ├── Meridian.Application.Services.ConfigPresetInfo.yml
│   │   │   ├── Meridian.Application.Services.ConfigTemplate.yml
│   │   │   ├── Meridian.Application.Services.ConfigTemplateCategory.yml
│   │   │   ├── Meridian.Application.Services.ConfigTemplateGenerator.yml
│   │   │   ├── Meridian.Application.Services.ConfigTemplateValidationResult.yml
│   │   │   ├── Meridian.Application.Services.ConfigurationPresets.yml
│   │   │   ├── Meridian.Application.Services.ConfigurationService.yml
│   │   │   ├── Meridian.Application.Services.ConfigurationServiceCredentialAdapter.yml
│   │   │   ├── Meridian.Application.Services.ConfigurationWizard.yml
│   │   │   ├── Meridian.Application.Services.ConnectivityProbeService.yml
│   │   │   ├── Meridian.Application.Services.ConnectivityTestService.ConnectivitySummary.yml
│   │   │   ├── Meridian.Application.Services.ConnectivityTestService.ConnectivityTestResult.yml
│   │   │   ├── Meridian.Application.Services.ConnectivityTestService.yml
│   │   │   ├── Meridian.Application.Services.CredentialValidationService.ValidationResult.yml
│   │   │   ├── Meridian.Application.Services.CredentialValidationService.ValidationSummary.yml
│   │   │   ├── Meridian.Application.Services.CredentialValidationService.yml
│   │   │   ├── Meridian.Application.Services.DailySummary.yml
│   │   │   ├── Meridian.Application.Services.DailySummaryResult.yml
│   │   │   ├── Meridian.Application.Services.DailySummaryWebhook.yml
│   │   │   ├── Meridian.Application.Services.DailySummaryWebhookConfig.yml
│   │   │   ├── Meridian.Application.Services.DataSourceSelection.yml
│   │   │   ├── Meridian.Application.Services.DiagnosticBundleOptions.yml
│   │   │   ├── Meridian.Application.Services.DiagnosticBundleResult.yml
│   │   │   ├── Meridian.Application.Services.DiagnosticBundleService.yml
│   │   │   ├── Meridian.Application.Services.DryRunOptions.yml
│   │   │   ├── Meridian.Application.Services.DryRunResult.yml
│   │   │   ├── Meridian.Application.Services.DryRunService.yml
│   │   │   ├── Meridian.Application.Services.EngineReconciliationRequest.yml
│   │   │   ├── Meridian.Application.Services.EngineReconciliationResult.yml
│   │   │   ├── Meridian.Application.Services.EnrichedLedgerRow.yml
│   │   │   ├── Meridian.Application.Services.EnvironmentOverrideInfo.yml
│   │   │   ├── Meridian.Application.Services.ErrorQueryResult.yml
│   │   │   ├── Meridian.Application.Services.ErrorStatistics.yml
│   │   │   ├── Meridian.Application.Services.ErrorTracker.yml
│   │   │   ├── Meridian.Application.Services.FirstTimeConfigOptions.yml
│   │   │   ├── Meridian.Application.Services.FormattedError.yml
│   │   │   ├── Meridian.Application.Services.FriendlyErrorFormatter.yml
│   │   │   ├── Meridian.Application.Services.GcStats.yml
│   │   │   ├── Meridian.Application.Services.GovernanceException.yml
│   │   │   ├── Meridian.Application.Services.GovernanceExceptionDashboard.yml
│   │   │   ├── Meridian.Application.Services.GovernanceExceptionService.yml
│   │   │   ├── Meridian.Application.Services.GovernanceExceptionSeverity.yml
│   │   │   ├── Meridian.Application.Services.GovernanceExceptionStatus.yml
│   │   │   ├── Meridian.Application.Services.GracefulShutdownConfig.yml
│   │   │   ├── Meridian.Application.Services.GracefulShutdownHandler.yml
│   │   │   ├── Meridian.Application.Services.GracefulShutdownService.yml
│   │   │   ├── Meridian.Application.Services.HistoricalDataDateRange.yml
│   │   │   ├── Meridian.Application.Services.HistoricalDataQuery.yml
│   │   │   ├── Meridian.Application.Services.HistoricalDataQueryResult.yml
│   │   │   ├── Meridian.Application.Services.HistoricalDataQueryService.yml
│   │   │   ├── Meridian.Application.Services.HistoricalDataRecord.yml
│   │   │   ├── Meridian.Application.Services.ICoLocationProfileActivator.yml
│   │   │   ├── Meridian.Application.Services.IFlushable.yml
│   │   │   ├── Meridian.Application.Services.IPluginLoaderService.yml
│   │   │   ├── Meridian.Application.Services.IssueSeverity.yml
│   │   │   ├── Meridian.Application.Services.MarketHoliday.yml
│   │   │   ├── Meridian.Application.Services.MarketState.yml
│   │   │   ├── Meridian.Application.Services.MarketStatus.yml
│   │   │   ├── Meridian.Application.Services.NavAttributionRequest.yml
│   │   │   ├── Meridian.Application.Services.NavAttributionResult.yml
│   │   │   ├── Meridian.Application.Services.NavAttributionService.yml
│   │   │   ├── Meridian.Application.Services.NavBreakdown.yml
│   │   │   ├── Meridian.Application.Services.NavComponent.yml
│   │   │   ├── Meridian.Application.Services.OpenApiComponents.yml
│   │   │   ├── Meridian.Application.Services.OpenApiContact.yml
│   │   │   ├── Meridian.Application.Services.OpenApiInfo.yml
│   │   │   ├── Meridian.Application.Services.OpenApiLicense.yml
│   │   │   ├── Meridian.Application.Services.OpenApiMediaType.yml
│   │   │   ├── Meridian.Application.Services.OpenApiOperation.yml
│   │   │   ├── Meridian.Application.Services.OpenApiParameter.yml
│   │   │   ├── Meridian.Application.Services.OpenApiPathItem.yml
│   │   │   ├── Meridian.Application.Services.OpenApiRequestBody.yml
│   │   │   ├── Meridian.Application.Services.OpenApiResponse.yml
│   │   │   ├── Meridian.Application.Services.OpenApiSchema.yml
│   │   │   ├── Meridian.Application.Services.OpenApiServer.yml
│   │   │   ├── Meridian.Application.Services.OpenApiSpec.yml
│   │   │   ├── Meridian.Application.Services.OpenApiTag.yml
│   │   │   ├── Meridian.Application.Services.OperationProgress.yml
│   │   │   ├── Meridian.Application.Services.OptionsChainService.yml
│   │   │   ├── Meridian.Application.Services.PluginLoaderService.yml
│   │   │   ├── Meridian.Application.Services.PluginLoadResult.yml
│   │   │   ├── Meridian.Application.Services.PortfolioPositionInput.yml
│   │   │   ├── Meridian.Application.Services.PreflightChecker.yml
│   │   │   ├── Meridian.Application.Services.PreflightCheckResult.yml
│   │   │   ├── Meridian.Application.Services.PreflightCheckStatus.yml
│   │   │   ├── Meridian.Application.Services.PreflightConfig.yml
│   │   │   ├── Meridian.Application.Services.PreflightException.yml
│   │   │   ├── Meridian.Application.Services.PreflightResult.yml
│   │   │   ├── Meridian.Application.Services.ProgressDisplayService.yml
│   │   │   ├── Meridian.Application.Services.QuickCheckIssue.yml
│   │   │   ├── Meridian.Application.Services.QuickCheckResult.yml
│   │   │   ├── Meridian.Application.Services.ReconciliationEngineService.yml
│   │   │   ├── Meridian.Application.Services.ReportGenerationService.yml
│   │   │   ├── Meridian.Application.Services.ReportKind.yml
│   │   │   ├── Meridian.Application.Services.ReportPack.yml
│   │   │   ├── Meridian.Application.Services.ReportRequest.yml
│   │   │   ├── Meridian.Application.Services.SampleDataGenerator.yml
│   │   │   ├── Meridian.Application.Services.SampleDataOptions.yml
│   │   │   ├── Meridian.Application.Services.SampleDataPreview.yml
│   │   │   ├── Meridian.Application.Services.SampleDataResult.yml
│   │   │   ├── Meridian.Application.Services.SecurityLookupKey.yml
│   │   │   ├── Meridian.Application.Services.ServiceCategory.yml
│   │   │   ├── Meridian.Application.Services.ServiceInfo.yml
│   │   │   ├── Meridian.Application.Services.ServiceRegistry.yml
│   │   │   ├── Meridian.Application.Services.ServiceRegistryExtensions.yml
│   │   │   ├── Meridian.Application.Services.ShutdownContext.yml
│   │   │   ├── Meridian.Application.Services.ShutdownProgress.yml
│   │   │   ├── Meridian.Application.Services.ShutdownReason.yml
│   │   │   ├── Meridian.Application.Services.ShutdownResult.yml
│   │   │   ├── Meridian.Application.Services.StartupSummary.yml
│   │   │   ├── Meridian.Application.Services.SymbolPreset.yml
│   │   │   ├── Meridian.Application.Services.TrackedError.yml
│   │   │   ├── Meridian.Application.Services.TradingCalendar.yml
│   │   │   ├── Meridian.Application.Services.UseCase.yml
│   │   │   ├── Meridian.Application.Services.ValidationCheck.yml
│   │   │   ├── Meridian.Application.Services.ValidationSection.yml
│   │   │   ├── Meridian.Application.Services.WebhookConfig.yml
│   │   │   ├── Meridian.Application.Services.WebhookDeliveryResult.yml
│   │   │   ├── Meridian.Application.Services.WebhookType.yml
│   │   │   ├── Meridian.Application.Services.WizardResult.yml
│   │   │   ├── Meridian.Application.Services.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.AddSymbolsToWatchlistRequest.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.ApplyTemplateRequest.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.BatchAddDefaults.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.BatchAddRequest.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.BatchCopySettingsRequest.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.BatchDeleteRequest.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.BatchFilter.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.BatchFilteredOperationRequest.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.BatchMoveToWatchlistRequest.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.BatchOperationResult.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.BatchToggleRequest.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.BatchUpdateRequest.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.BrokerType.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.BulkExportOptions.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.BulkImportOptions.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.BulkImportResult.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.CircuitState.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.CreateScheduleRequest.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.CreateWatchlistRequest.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.CsvColumns.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.FigiLookupRequest.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.FigiMapping.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.ImportDefaults.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.ImportError.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.IndexComponent.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.IndexComponents.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.IndexDefinition.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.IndexSubscribeRequest.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.IndexSubscribeResult.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.KnownIndices.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.ManualPortfolioEntry.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.MarketCapCategory.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.MetadataFilterResult.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.PortfolioImportOptions.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.PortfolioImportRequest.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.PortfolioImportResult.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.PortfolioPosition.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.PortfolioSummary.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.RemoveSymbolsFromWatchlistRequest.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.ResubscriptionMetrics.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.ResubscriptionMetricsSnapshot.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.ScheduleAction.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.ScheduleExecutionStatus.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.ScheduleTiming.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.ScheduleType.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.SubscriptionSchedule.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.SymbolDetails.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.SymbolMetadata.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.SymbolMetadataFilter.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.SymbolSearchRequest.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.SymbolSearchResponse.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.SymbolSearchResult.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.SymbolTemplate.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.TemplateCategory.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.TemplateSubscriptionDefaults.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.UpdateWatchlistRequest.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.Watchlist.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.WatchlistDefaults.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.WatchlistOperationResult.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.WatchlistSubscriptionRequest.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.WatchlistSummary.yml
│   │   │   ├── Meridian.Application.Subscriptions.Models.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.ApplyTemplateResult.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.ArchivedSymbolInfo.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.ArchivedSymbolsOptions.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.ArchivedSymbolsResult.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.AutoResubscribeOptions.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.AutoResubscribePolicy.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.BatchOperationsService.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.IndexSubscriptionService.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.IndexSubscriptionStatus.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.MetadataEnrichmentService.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.MonitoredSymbolInfo.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.MonitoredSymbolsResult.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.PortfolioImportService.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.ProviderStatus.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.SchedulingService.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.SymbolAddOptions.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.SymbolImportExportService.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.SymbolManagementService.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.SymbolOperationResult.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.SymbolSearchService.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.SymbolStatusReport.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.TemplateService.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.WatchlistService.yml
│   │   │   ├── Meridian.Application.Subscriptions.Services.yml
│   │   │   ├── Meridian.Application.Subscriptions.SubscriptionOrchestrator.yml
│   │   │   ├── Meridian.Application.Subscriptions.yml
│   │   │   ├── Meridian.Application.Testing.DepthBufferSelfTests.yml
│   │   │   ├── Meridian.Application.Testing.yml
│   │   │   ├── Meridian.Application.Tracing.EventTraceContext.yml
│   │   │   ├── Meridian.Application.Tracing.MarketDataTracing.yml
│   │   │   ├── Meridian.Application.Tracing.OpenTelemetryConfiguration.yml
│   │   │   ├── Meridian.Application.Tracing.OpenTelemetrySetup.yml
│   │   │   ├── Meridian.Application.Tracing.TracedEventMetrics.yml
│   │   │   ├── Meridian.Application.Tracing.yml
│   │   │   ├── Meridian.Application.Treasury.IMmfLiquidityService.yml
│   │   │   ├── Meridian.Application.Treasury.IMoneyMarketFundService.yml
│   │   │   ├── Meridian.Application.Treasury.InMemoryMoneyMarketFundService.yml
│   │   │   ├── Meridian.Application.Treasury.yml
│   │   │   ├── Meridian.Application.UI.ArchiveMaintenanceEndpoints.yml
│   │   │   ├── Meridian.Application.UI.BackfillCoordinator.yml
│   │   │   ├── Meridian.Application.UI.CleanupHistoryRequest.yml
│   │   │   ├── Meridian.Application.UI.ConfigStore.yml
│   │   │   ├── Meridian.Application.UI.CreateMaintenanceScheduleRequest.yml
│   │   │   ├── Meridian.Application.UI.ExecuteMaintenanceRequest.yml
│   │   │   ├── Meridian.Application.UI.HtmlTemplateLoader.yml
│   │   │   ├── Meridian.Application.UI.HtmlTemplateLoaderExtensions.yml
│   │   │   ├── Meridian.Application.UI.HtmlTemplateManager.yml
│   │   │   ├── Meridian.Application.UI.ImportRequest.yml
│   │   │   ├── Meridian.Application.UI.MaintenanceOptionsDto.yml
│   │   │   ├── Meridian.Application.UI.PackageRequest.yml
│   │   │   ├── Meridian.Application.UI.PackagingEndpoints.yml
│   │   │   ├── Meridian.Application.UI.StatusEndpointHandlers.yml
│   │   │   ├── Meridian.Application.UI.UpdateMaintenanceScheduleRequest.yml
│   │   │   ├── Meridian.Application.UI.ValidateMaintenanceCronRequest.yml
│   │   │   ├── Meridian.Application.UI.ValidateRequest.yml
│   │   │   ├── Meridian.Application.UI.yml
│   │   │   ├── Meridian.Application.Wizard.Core.IWizardStep.yml
│   │   │   ├── Meridian.Application.Wizard.Core.WizardContext.yml
│   │   │   ├── Meridian.Application.Wizard.Core.WizardCoordinator.yml
│   │   │   ├── Meridian.Application.Wizard.Core.WizardStepId.yml
│   │   │   ├── Meridian.Application.Wizard.Core.WizardStepResult.yml
│   │   │   ├── Meridian.Application.Wizard.Core.WizardStepStatus.yml
│   │   │   ├── Meridian.Application.Wizard.Core.WizardSummary.yml
│   │   │   ├── Meridian.Application.Wizard.Core.WizardTransition.yml
│   │   │   ├── Meridian.Application.Wizard.Core.yml
│   │   │   ├── Meridian.Application.Wizard.Metadata.ProviderDescriptor.yml
│   │   │   ├── Meridian.Application.Wizard.Metadata.ProviderRegistry.yml
│   │   │   ├── Meridian.Application.Wizard.Metadata.yml
│   │   │   ├── Meridian.Application.Wizard.Steps.ConfigureBackfillStep.yml
│   │   │   ├── Meridian.Application.Wizard.Steps.ConfigureDataSourceStep.yml
│   │   │   ├── Meridian.Application.Wizard.Steps.ConfigureStorageStep.yml
│   │   │   ├── Meridian.Application.Wizard.Steps.ConfigureSymbolsStep.yml
│   │   │   ├── Meridian.Application.Wizard.Steps.CredentialGuidanceStep.yml
│   │   │   ├── Meridian.Application.Wizard.Steps.DetectProvidersStep.yml
│   │   │   ├── Meridian.Application.Wizard.Steps.ReviewConfigurationStep.yml
│   │   │   ├── Meridian.Application.Wizard.Steps.SaveConfigurationStep.yml
│   │   │   ├── Meridian.Application.Wizard.Steps.SelectUseCaseStep.yml
│   │   │   ├── Meridian.Application.Wizard.Steps.ValidateCredentialsStep.yml
│   │   │   ├── Meridian.Application.Wizard.Steps.yml
│   │   │   ├── Meridian.Application.Wizard.WizardWorkflowFactory.yml
│   │   │   ├── Meridian.Application.Wizard.yml
│   │   │   ├── Meridian.Backtesting.BacktestStudioRunOrchestrator.yml
│   │   │   ├── Meridian.Backtesting.BatchBacktestProgress.yml
│   │   │   ├── Meridian.Backtesting.BatchBacktestRequest.yml
│   │   │   ├── Meridian.Backtesting.BatchBacktestRun.yml
│   │   │   ├── Meridian.Backtesting.BatchBacktestService.yml
│   │   │   ├── Meridian.Backtesting.BatchBacktestSummary.yml
│   │   │   ├── Meridian.Backtesting.CorporateActionAdjustmentService.yml
│   │   │   ├── Meridian.Backtesting.Engine.BacktestEngine.yml
│   │   │   ├── Meridian.Backtesting.Engine.yml
│   │   │   ├── Meridian.Backtesting.IBatchBacktestService.yml
│   │   │   ├── Meridian.Backtesting.ICorporateActionAdjustmentService.yml
│   │   │   ├── Meridian.Backtesting.MeridianNativeBacktestStudioEngine.yml
│   │   │   ├── Meridian.Backtesting.Plugins.StrategyParameterInfo.yml
│   │   │   ├── Meridian.Backtesting.Plugins.StrategyPluginLoader.yml
│   │   │   ├── Meridian.Backtesting.Plugins.yml
│   │   │   ├── Meridian.Backtesting.Portfolio.FixedCommissionModel.yml
│   │   │   ├── Meridian.Backtesting.Portfolio.ICommissionModel.yml
│   │   │   ├── Meridian.Backtesting.Portfolio.PercentageCommissionModel.yml
│   │   │   ├── Meridian.Backtesting.Portfolio.PerShareCommissionModel.yml
│   │   │   ├── Meridian.Backtesting.Portfolio.yml
│   │   │   ├── Meridian.Backtesting.Sdk.AssetEvent.yml
│   │   │   ├── Meridian.Backtesting.Sdk.AssetEventCashFlow.yml
│   │   │   ├── Meridian.Backtesting.Sdk.AssetEventType.yml
│   │   │   ├── Meridian.Backtesting.Sdk.BacktestArtifactCoverage.yml
│   │   │   ├── Meridian.Backtesting.Sdk.BacktestArtifactStatus.yml
│   │   │   ├── Meridian.Backtesting.Sdk.BacktestCommissionKind.yml
│   │   │   ├── Meridian.Backtesting.Sdk.BacktestDefaults.yml
│   │   │   ├── Meridian.Backtesting.Sdk.BacktestEngineMetadata.yml
│   │   │   ├── Meridian.Backtesting.Sdk.BacktestEngineMode.yml
│   │   │   ├── Meridian.Backtesting.Sdk.BacktestMetrics.yml
│   │   │   ├── Meridian.Backtesting.Sdk.BacktestProgressEvent.yml
│   │   │   ├── Meridian.Backtesting.Sdk.BacktestRequest.yml
│   │   │   ├── Meridian.Backtesting.Sdk.BacktestResult.yml
│   │   │   ├── Meridian.Backtesting.Sdk.BracketOrderRequest.yml
│   │   │   ├── Meridian.Backtesting.Sdk.CashFlowEntry.yml
│   │   │   ├── Meridian.Backtesting.Sdk.CashInterestCashFlow.yml
│   │   │   ├── Meridian.Backtesting.Sdk.ClosedLot.yml
│   │   │   ├── Meridian.Backtesting.Sdk.CommissionCashFlow.yml
│   │   │   ├── Meridian.Backtesting.Sdk.DividendCashFlow.yml
│   │   │   ├── Meridian.Backtesting.Sdk.ExecutionModel.yml
│   │   │   ├── Meridian.Backtesting.Sdk.FillEvent.yml
│   │   │   ├── Meridian.Backtesting.Sdk.FinancialAccount.yml
│   │   │   ├── Meridian.Backtesting.Sdk.FinancialAccountKind.yml
│   │   │   ├── Meridian.Backtesting.Sdk.FinancialAccountRules.yml
│   │   │   ├── Meridian.Backtesting.Sdk.FinancialAccountSnapshot.yml
│   │   │   ├── Meridian.Backtesting.Sdk.IBacktestContext.yml
│   │   │   ├── Meridian.Backtesting.Sdk.IBacktestStrategy.yml
│   │   │   ├── Meridian.Backtesting.Sdk.LotSelectionMethod.yml
│   │   │   ├── Meridian.Backtesting.Sdk.MarginInterestCashFlow.yml
│   │   │   ├── Meridian.Backtesting.Sdk.OpenLot.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Order.yml
│   │   │   ├── Meridian.Backtesting.Sdk.OrderRequest.yml
│   │   │   ├── Meridian.Backtesting.Sdk.OrderStatus.yml
│   │   │   ├── Meridian.Backtesting.Sdk.OrderType.yml
│   │   │   ├── Meridian.Backtesting.Sdk.PortfolioSnapshot.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Position.yml
│   │   │   ├── Meridian.Backtesting.Sdk.ShortRebateCashFlow.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.AdvancedCarry.AdvancedCarryConfiguration.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.AdvancedCarry.AdvancedCarryDecision.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.AdvancedCarry.AdvancedCarryDecisionEngine.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.AdvancedCarry.AdvancedCarryExecutionOptions.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.AdvancedCarry.AdvancedCarryInput.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.AdvancedCarry.AdvancedCarryRiskOptions.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.AdvancedCarry.AssetCorrelation.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.AdvancedCarry.CarryAssetSnapshot.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.AdvancedCarry.CarryExecutionAlgorithm.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.AdvancedCarry.CarryOptimizationMethod.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.AdvancedCarry.CarryPortfolioState.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.AdvancedCarry.CarryRiskReport.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.AdvancedCarry.CarryScenarioType.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.AdvancedCarry.CarryTailRiskEstimate.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.AdvancedCarry.ExecutionPlan.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.AdvancedCarry.ExecutionSlice.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.AdvancedCarry.ICarryForecastOverlay.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.AdvancedCarry.OptimizedTargetWeight.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.AdvancedCarry.RebalanceInstruction.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.AdvancedCarry.ScenarioImpact.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.AdvancedCarry.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite.BlackScholesCalculator.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite.CoveredCallOverwriteStrategy.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite.IOptionChainProvider.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite.OptionCandidateInfo.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite.OptionsOverwriteFilters.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite.OptionsOverwriteMetrics.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite.OptionsOverwriteMetricsCalculator.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite.OptionsOverwriteParams.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite.OptionsOverwriteScoring.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite.OptionsOverwriteTradeRecord.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite.OverwriteScoringMode.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite.ShortCallExitReason.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite.ShortCallPosition.yml
│   │   │   ├── Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite.yml
│   │   │   ├── Meridian.Backtesting.Sdk.StrategyParameterAttribute.yml
│   │   │   ├── Meridian.Backtesting.Sdk.SymbolAttribution.yml
│   │   │   ├── Meridian.Backtesting.Sdk.SymbolTcaSummary.yml
│   │   │   ├── Meridian.Backtesting.Sdk.TcaCostSummary.yml
│   │   │   ├── Meridian.Backtesting.Sdk.TcaFillOutlier.yml
│   │   │   ├── Meridian.Backtesting.Sdk.TcaReport.yml
│   │   │   ├── Meridian.Backtesting.Sdk.TimeInForce.yml
│   │   │   ├── Meridian.Backtesting.Sdk.TradeCashFlow.yml
│   │   │   ├── Meridian.Backtesting.Sdk.TradeTicket.yml
│   │   │   ├── Meridian.Backtesting.Sdk.yml
│   │   │   ├── Meridian.Backtesting.yml
│   │   │   ├── Meridian.Contracts.Api.ApiResponse-1.yml
│   │   │   ├── Meridian.Contracts.Api.ApplyProviderPresetRequest.yml
│   │   │   ├── Meridian.Contracts.Api.BackfillExecution.yml
│   │   │   ├── Meridian.Contracts.Api.BackfillExecutionResponse.yml
│   │   │   ├── Meridian.Contracts.Api.BackfillHealthResponse.yml
│   │   │   ├── Meridian.Contracts.Api.BackfillPreset.yml
│   │   │   ├── Meridian.Contracts.Api.BackfillProviderHealth.yml
│   │   │   ├── Meridian.Contracts.Api.BackfillProviderInfo.yml
│   │   │   ├── Meridian.Contracts.Api.BackfillRequest.yml
│   │   │   ├── Meridian.Contracts.Api.BackfillRequestDto.yml
│   │   │   ├── Meridian.Contracts.Api.BackfillResultDto.yml
│   │   │   ├── Meridian.Contracts.Api.BackfillStatistics.yml
│   │   │   ├── Meridian.Contracts.Api.BackpressureStatusDto.yml
│   │   │   ├── Meridian.Contracts.Api.BboResponse.yml
│   │   │   ├── Meridian.Contracts.Api.CapabilityInfo.yml
│   │   │   ├── Meridian.Contracts.Api.ConnectionHealthDto.yml
│   │   │   ├── Meridian.Contracts.Api.ConnectionHealthSnapshotDto.yml
│   │   │   ├── Meridian.Contracts.Api.CreateProviderConnectionRequest.yml
│   │   │   ├── Meridian.Contracts.Api.CredentialFieldInfo.yml
│   │   │   ├── Meridian.Contracts.Api.CredentialFieldOutput.yml
│   │   │   ├── Meridian.Contracts.Api.DataSourceConfigRequest.yml
│   │   │   ├── Meridian.Contracts.Api.DataSourceRequest.yml
│   │   │   ├── Meridian.Contracts.Api.DefaultSourcesRequest.yml
│   │   │   ├── Meridian.Contracts.Api.DryRunPlanRequest.yml
│   │   │   ├── Meridian.Contracts.Api.ErrorEntryDto.yml
│   │   │   ├── Meridian.Contracts.Api.ErrorResponse.yml
│   │   │   ├── Meridian.Contracts.Api.ErrorsResponseDto.yml
│   │   │   ├── Meridian.Contracts.Api.ErrorStatsDto.yml
│   │   │   ├── Meridian.Contracts.Api.FailoverConfigRequest.yml
│   │   │   ├── Meridian.Contracts.Api.FailoverConfigResponse.yml
│   │   │   ├── Meridian.Contracts.Api.FailoverRuleRequest.yml
│   │   │   ├── Meridian.Contracts.Api.FailoverRuleResponse.yml
│   │   │   ├── Meridian.Contracts.Api.FailoverSettingsRequest.yml
│   │   │   ├── Meridian.Contracts.Api.FieldError.yml
│   │   │   ├── Meridian.Contracts.Api.ForceFailoverRequest.yml
│   │   │   ├── Meridian.Contracts.Api.FreshnessStates.yml
│   │   │   ├── Meridian.Contracts.Api.GapFillRequest.yml
│   │   │   ├── Meridian.Contracts.Api.GreeksSnapshotDto.yml
│   │   │   ├── Meridian.Contracts.Api.HealthCheckItem.yml
│   │   │   ├── Meridian.Contracts.Api.HealthCheckResponse.yml
│   │   │   ├── Meridian.Contracts.Api.HealthIssueResponse.yml
│   │   │   ├── Meridian.Contracts.Api.HealthSummaryProviders.yml
│   │   │   ├── Meridian.Contracts.Api.HealthSummaryResponse.yml
│   │   │   ├── Meridian.Contracts.Api.LeanBacktestResultsResponseDto.yml
│   │   │   ├── Meridian.Contracts.Api.LeanBacktestResultsSummaryDto.yml
│   │   │   ├── Meridian.Contracts.Api.LeanRawArtifactFileDto.yml
│   │   │   ├── Meridian.Contracts.Api.LeanResultsArtifactSectionsDto.yml
│   │   │   ├── Meridian.Contracts.Api.LeanResultsArtifactSummaryDto.yml
│   │   │   ├── Meridian.Contracts.Api.LeanResultsImportRequestDto.yml
│   │   │   ├── Meridian.Contracts.Api.LeanResultsIngestResponseDto.yml
│   │   │   ├── Meridian.Contracts.Api.LiveDataHealthResponse.yml
│   │   │   ├── Meridian.Contracts.Api.MetricsData.yml
│   │   │   ├── Meridian.Contracts.Api.MetricsFreshness.yml
│   │   │   ├── Meridian.Contracts.Api.OpenInterestDto.yml
│   │   │   ├── Meridian.Contracts.Api.OptionQuoteDto.yml
│   │   │   ├── Meridian.Contracts.Api.OptionQuoteRequest.yml
│   │   │   ├── Meridian.Contracts.Api.OptionsChainResponse.yml
│   │   │   ├── Meridian.Contracts.Api.OptionsExpirationsResponse.yml
│   │   │   ├── Meridian.Contracts.Api.OptionsRefreshRequest.yml
│   │   │   ├── Meridian.Contracts.Api.OptionsStrikesResponse.yml
│   │   │   ├── Meridian.Contracts.Api.OptionsSummaryResponse.yml
│   │   │   ├── Meridian.Contracts.Api.OptionTradeDto.yml
│   │   │   ├── Meridian.Contracts.Api.OrderBookLevelDto.yml
│   │   │   ├── Meridian.Contracts.Api.OrderBookResponse.yml
│   │   │   ├── Meridian.Contracts.Api.OrderFlowResponse.yml
│   │   │   ├── Meridian.Contracts.Api.PipelineData.yml
│   │   │   ├── Meridian.Contracts.Api.PrometheusMetricsDto.yml
│   │   │   ├── Meridian.Contracts.Api.ProviderBindingDto.yml
│   │   │   ├── Meridian.Contracts.Api.ProviderCapabilityOutput.yml
│   │   │   ├── Meridian.Contracts.Api.ProviderCatalog.yml
│   │   │   ├── Meridian.Contracts.Api.ProviderCatalogEntry.yml
│   │   │   ├── Meridian.Contracts.Api.ProviderCertificationDto.yml
│   │   │   ├── Meridian.Contracts.Api.ProviderComparisonResponse.yml
│   │   │   ├── Meridian.Contracts.Api.ProviderConnectionDto.yml
│   │   │   ├── Meridian.Contracts.Api.ProviderHealthResponse.yml
│   │   │   ├── Meridian.Contracts.Api.ProviderLatencyStatsDto.yml
│   │   │   ├── Meridian.Contracts.Api.ProviderLatencySummaryDto.yml
│   │   │   ├── Meridian.Contracts.Api.ProviderMetricsResponse.yml
│   │   │   ├── Meridian.Contracts.Api.ProviderPolicyDto.yml
│   │   │   ├── Meridian.Contracts.Api.ProviderPresetDto.yml
│   │   │   ├── Meridian.Contracts.Api.ProviderRateLimitOutput.yml
│   │   │   ├── Meridian.Contracts.Api.ProviderRouteScopeDto.yml
│   │   │   ├── Meridian.Contracts.Api.ProviderStatusResponse.yml
│   │   │   ├── Meridian.Contracts.Api.ProviderTemplateOutput.yml
│   │   │   ├── Meridian.Contracts.Api.ProviderTrustSnapshotDto.yml
│   │   │   ├── Meridian.Contracts.Api.ProviderTypeKind.yml
│   │   │   ├── Meridian.Contracts.Api.Quality.QualityAnomalyAcknowledgementResponse.yml
│   │   │   ├── Meridian.Contracts.Api.Quality.QualityAnomalyAcknowledgeRequest.yml
│   │   │   ├── Meridian.Contracts.Api.Quality.QualityAnomalyResponse.yml
│   │   │   ├── Meridian.Contracts.Api.Quality.QualityAnomalyStatisticsResponse.yml
│   │   │   ├── Meridian.Contracts.Api.Quality.QualityComparisonRequest.yml
│   │   │   ├── Meridian.Contracts.Api.Quality.QualityComparisonResponse.yml
│   │   │   ├── Meridian.Contracts.Api.Quality.QualityCompletenessSummaryResponse.yml
│   │   │   ├── Meridian.Contracts.Api.Quality.QualityCountBySymbolResponse.yml
│   │   │   ├── Meridian.Contracts.Api.Quality.QualityDashboardResponse.yml
│   │   │   ├── Meridian.Contracts.Api.Quality.QualityGapResponse.yml
│   │   │   ├── Meridian.Contracts.Api.Quality.QualityGapStatisticsResponse.yml
│   │   │   ├── Meridian.Contracts.Api.Quality.QualityLatencyStatisticsResponse.yml
│   │   │   ├── Meridian.Contracts.Api.Quality.QualityProviderDataSummaryResponse.yml
│   │   │   ├── Meridian.Contracts.Api.Quality.QualityProviderDiscrepancyResponse.yml
│   │   │   ├── Meridian.Contracts.Api.Quality.QualityRealTimeMetricsResponse.yml
│   │   │   ├── Meridian.Contracts.Api.Quality.QualitySequenceErrorResponse.yml
│   │   │   ├── Meridian.Contracts.Api.Quality.QualitySequenceErrorStatisticsResponse.yml
│   │   │   ├── Meridian.Contracts.Api.Quality.QualitySymbolHealthResponse.yml
│   │   │   ├── Meridian.Contracts.Api.Quality.yml
│   │   │   ├── Meridian.Contracts.Api.QuoteDataResponse.yml
│   │   │   ├── Meridian.Contracts.Api.QuotesResponse.yml
│   │   │   ├── Meridian.Contracts.Api.RateLimitInfo.yml
│   │   │   ├── Meridian.Contracts.Api.RoutePreviewCandidateDto.yml
│   │   │   ├── Meridian.Contracts.Api.RoutePreviewRequest.yml
│   │   │   ├── Meridian.Contracts.Api.RoutePreviewResponse.yml
│   │   │   ├── Meridian.Contracts.Api.RunCertificationRequest.yml
│   │   │   ├── Meridian.Contracts.Api.ServiceHealthResult.yml
│   │   │   ├── Meridian.Contracts.Api.StatusResponse.yml
│   │   │   ├── Meridian.Contracts.Api.StorageAnalytics.yml
│   │   │   ├── Meridian.Contracts.Api.StorageProfileResponse.yml
│   │   │   ├── Meridian.Contracts.Api.StorageSettingsRequest.yml
│   │   │   ├── Meridian.Contracts.Api.StorageSymbolBreakdown.yml
│   │   │   ├── Meridian.Contracts.Api.SymbolBackfillResult.yml
│   │   │   ├── Meridian.Contracts.Api.SymbolDataHealthDto.yml
│   │   │   ├── Meridian.Contracts.Api.SymbolMappingRequest.yml
│   │   │   ├── Meridian.Contracts.Api.SymbolMappingResponse.yml
│   │   │   ├── Meridian.Contracts.Api.SymbolResolutionResponse.yml
│   │   │   ├── Meridian.Contracts.Api.ToggleRequest.yml
│   │   │   ├── Meridian.Contracts.Api.TradeDataResponse.yml
│   │   │   ├── Meridian.Contracts.Api.TradesResponse.yml
│   │   │   ├── Meridian.Contracts.Api.UiApiClient.yml
│   │   │   ├── Meridian.Contracts.Api.UiApiRoutes.yml
│   │   │   ├── Meridian.Contracts.Api.UpdateProviderBindingRequest.yml
│   │   │   ├── Meridian.Contracts.Api.yml
│   │   │   ├── Meridian.Contracts.Archive.ArchiveHealthSeverity.yml
│   │   │   ├── Meridian.Contracts.Archive.ArchiveHealthStatus.yml
│   │   │   ├── Meridian.Contracts.Archive.ArchiveHealthStatusValues.yml
│   │   │   ├── Meridian.Contracts.Archive.ArchiveIssue.yml
│   │   │   ├── Meridian.Contracts.Archive.ArchiveIssueCategory.yml
│   │   │   ├── Meridian.Contracts.Archive.StorageHealthInfo.yml
│   │   │   ├── Meridian.Contracts.Archive.VerificationJob.yml
│   │   │   ├── Meridian.Contracts.Archive.VerificationJobStatus.yml
│   │   │   ├── Meridian.Contracts.Archive.VerificationJobType.yml
│   │   │   ├── Meridian.Contracts.Archive.VerificationScheduleConfig.yml
│   │   │   ├── Meridian.Contracts.Archive.yml
│   │   │   ├── Meridian.Contracts.Auth.RolePermissions.yml
│   │   │   ├── Meridian.Contracts.Auth.UserPermission.yml
│   │   │   ├── Meridian.Contracts.Auth.UserRole.yml
│   │   │   ├── Meridian.Contracts.Auth.yml
│   │   │   ├── Meridian.Contracts.Backfill.BackfillJobStatus.yml
│   │   │   ├── Meridian.Contracts.Backfill.BackfillProgress.yml
│   │   │   ├── Meridian.Contracts.Backfill.SymbolBackfillProgress.yml
│   │   │   ├── Meridian.Contracts.Backfill.SymbolBackfillStatus.yml
│   │   │   ├── Meridian.Contracts.Backfill.yml
│   │   │   ├── Meridian.Contracts.Banking.ApprovePaymentRequest.yml
│   │   │   ├── Meridian.Contracts.Banking.BankTransactionDto.yml
│   │   │   ├── Meridian.Contracts.Banking.BankTransactionSeedRequest.yml
│   │   │   ├── Meridian.Contracts.Banking.BankTransactionSeedResultDto.yml
│   │   │   ├── Meridian.Contracts.Banking.IBankTransactionSource.yml
│   │   │   ├── Meridian.Contracts.Banking.InitiatePaymentRequest.yml
│   │   │   ├── Meridian.Contracts.Banking.PaymentApprovalStatus.yml
│   │   │   ├── Meridian.Contracts.Banking.PendingPaymentDto.yml
│   │   │   ├── Meridian.Contracts.Banking.RejectPaymentRequest.yml
│   │   │   ├── Meridian.Contracts.Banking.yml
│   │   │   ├── Meridian.Contracts.Catalog.CanonicalSymbolDefinition.yml
│   │   │   ├── Meridian.Contracts.Catalog.CatalogConfiguration.yml
│   │   │   ├── Meridian.Contracts.Catalog.CatalogDateRange.yml
│   │   │   ├── Meridian.Contracts.Catalog.CatalogIntegrity.yml
│   │   │   ├── Meridian.Contracts.Catalog.CatalogIntegrityIssue.yml
│   │   │   ├── Meridian.Contracts.Catalog.CatalogStatistics.yml
│   │   │   ├── Meridian.Contracts.Catalog.CorporateActionRef.yml
│   │   │   ├── Meridian.Contracts.Catalog.DirectoryDateRange.yml
│   │   │   ├── Meridian.Contracts.Catalog.DirectoryIndex.yml
│   │   │   ├── Meridian.Contracts.Catalog.DirectoryScanResult.yml
│   │   │   ├── Meridian.Contracts.Catalog.DirectoryStatistics.yml
│   │   │   ├── Meridian.Contracts.Catalog.ICanonicalSymbolRegistry.yml
│   │   │   ├── Meridian.Contracts.Catalog.IdentifierIndex.yml
│   │   │   ├── Meridian.Contracts.Catalog.IndexedFileEntry.yml
│   │   │   ├── Meridian.Contracts.Catalog.SchemaReference.yml
│   │   │   ├── Meridian.Contracts.Catalog.SequenceRange.yml
│   │   │   ├── Meridian.Contracts.Catalog.StorageCatalog.yml
│   │   │   ├── Meridian.Contracts.Catalog.SymbolAlias.yml
│   │   │   ├── Meridian.Contracts.Catalog.SymbolCatalogEntry.yml
│   │   │   ├── Meridian.Contracts.Catalog.SymbolClassification.yml
│   │   │   ├── Meridian.Contracts.Catalog.SymbolIdentifiers.yml
│   │   │   ├── Meridian.Contracts.Catalog.SymbolLookupResult.yml
│   │   │   ├── Meridian.Contracts.Catalog.SymbolRegistry.yml
│   │   │   ├── Meridian.Contracts.Catalog.SymbolRegistryEntry.yml
│   │   │   ├── Meridian.Contracts.Catalog.SymbolRegistryStatistics.yml
│   │   │   ├── Meridian.Contracts.Catalog.yml
│   │   │   ├── Meridian.Contracts.Configuration.AlpacaOptionsDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.AppConfigDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.AppSettingsDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.BackfillConfigDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.BackfillDryRunPlanDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.BackfillProviderMetadataDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.BackfillProviderOptionsDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.BackfillProvidersConfigDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.BackfillProviderStatusDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.BackfillSymbolPlanDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.CQGOptionsDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.DataSourceConfigDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.DataSourcesConfigDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.DerivativesConfigDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.ExtendedSymbolConfigDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.IBOptionsDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.IndexOptionsConfigDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.IQFeedOptionsDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.PolygonOptionsDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.ProviderBindingConfigDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.ProviderCertificationConfigDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.ProviderConfigAuditEntryDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.ProviderConnectionConfigDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.ProviderConnectionsConfigDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.ProviderPolicyConfigDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.ProviderPresetConfigDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.ProviderScopeDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.RithmicOptionsDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.SmartGroupCriteriaDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.StockSharpIBOptionsDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.StockSharpOptionsDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.StorageConfigDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.SymbolConfig.yml
│   │   │   ├── Meridian.Contracts.Configuration.SymbolConfigDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.SymbolGroupDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.SymbolGroupsConfigDto.yml
│   │   │   ├── Meridian.Contracts.Configuration.yml
│   │   │   ├── Meridian.Contracts.Credentials.CredentialInfo.yml
│   │   │   ├── Meridian.Contracts.Credentials.CredentialMetadata.yml
│   │   │   ├── Meridian.Contracts.Credentials.CredentialTestResult.yml
│   │   │   ├── Meridian.Contracts.Credentials.CredentialTestStatus.yml
│   │   │   ├── Meridian.Contracts.Credentials.CredentialType.yml
│   │   │   ├── Meridian.Contracts.Credentials.EnvironmentSecretProvider.yml
│   │   │   ├── Meridian.Contracts.Credentials.ISecretProvider.yml
│   │   │   ├── Meridian.Contracts.Credentials.OAuthProviderConfig.yml
│   │   │   ├── Meridian.Contracts.Credentials.OAuthTokenResponse.yml
│   │   │   ├── Meridian.Contracts.Credentials.yml
│   │   │   ├── Meridian.Contracts.DirectLending.AccountingPeriodLockDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ActivateLoanRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.AddCollateralRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.AmendLoanTermsRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.AmortizationType.yml
│   │   │   ├── Meridian.Contracts.DirectLending.AmortizeDiscountPremiumRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ApplyMixedPaymentRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ApplyPrincipalPaymentRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ApplyRateResetRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ApplyWriteOffRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.AssessFeeRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.BookDrawdownRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.BorrowerInfoDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.CashTransactionDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ChargePrepaymentPenaltyRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.CollateralDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.CollateralType.yml
│   │   │   ├── Meridian.Contracts.DirectLending.CreateLoanRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.CreateServicerReportBatchRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.CurrencyCode.yml
│   │   │   ├── Meridian.Contracts.DirectLending.DailyAccrualEntryDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.DayCountBasis.yml
│   │   │   ├── Meridian.Contracts.DirectLending.DirectLendingCommandEnvelope-1.yml
│   │   │   ├── Meridian.Contracts.DirectLending.DirectLendingCommandError.yml
│   │   │   ├── Meridian.Contracts.DirectLending.DirectLendingCommandException.yml
│   │   │   ├── Meridian.Contracts.DirectLending.DirectLendingCommandMetadataDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.DirectLendingCommandResult-1.yml
│   │   │   ├── Meridian.Contracts.DirectLending.DirectLendingErrorCode.yml
│   │   │   ├── Meridian.Contracts.DirectLending.DirectLendingOptions.yml
│   │   │   ├── Meridian.Contracts.DirectLending.DirectLendingOutboxMessageDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.DirectLendingReplayCheckpointDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.DirectLendingTermsDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.DrawdownLotDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.FeeBalanceDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.JournalEntryDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.JournalEntryStatus.yml
│   │   │   ├── Meridian.Contracts.DirectLending.JournalLineDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.LoanAggregateSnapshotDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.LoanContractDetailDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.LoanEventLineageDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.LoanPortfolioSummaryDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.LoanServicingStateDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.LoanStatus.yml
│   │   │   ├── Meridian.Contracts.DirectLending.LoanSummaryDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.LoanTermsVersionDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.MixedPaymentResolutionDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.OutstandingBalancesDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.PaymentAllocationDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.PaymentBreakdownDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.PaymentFrequency.yml
│   │   │   ├── Meridian.Contracts.DirectLending.PostDailyAccrualRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ProjectedCashFlowDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ProjectionRunDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ProjectionRunStatus.yml
│   │   │   ├── Meridian.Contracts.DirectLending.RateResetDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.RateTypeKind.yml
│   │   │   ├── Meridian.Contracts.DirectLending.RebuildCheckpointDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ReconcileLoanRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ReconciliationExceptionDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ReconciliationResultDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ReconciliationRunDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.RemoveCollateralRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ReplayDirectLendingRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ReplayDirectLendingResultDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.RequestProjectionRunRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ResolveReconciliationExceptionRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.RestructureLoanRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.RestructuringType.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ServicerPositionReportLineDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ServicerPositionReportLineImportDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ServicerReportBatchDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ServicerTransactionReportLineDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ServicerTransactionReportLineImportDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.ServicingRevisionDto.yml
│   │   │   ├── Meridian.Contracts.DirectLending.TogglePikRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.TransitionLoanStatusRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.UpdateCollateralValueRequest.yml
│   │   │   ├── Meridian.Contracts.DirectLending.yml
│   │   │   ├── Meridian.Contracts.Domain.AccountSnapshotRecord.yml
│   │   │   ├── Meridian.Contracts.Domain.AggressorSideValues.yml
│   │   │   ├── Meridian.Contracts.Domain.BarIntervalValues.yml
│   │   │   ├── Meridian.Contracts.Domain.CanonicalSymbol.yml
│   │   │   ├── Meridian.Contracts.Domain.Enums.AggressorSide.yml
│   │   │   ├── Meridian.Contracts.Domain.Enums.CanonicalTradeCondition.yml
│   │   │   ├── Meridian.Contracts.Domain.Enums.ConnectionStatus.yml
│   │   │   ├── Meridian.Contracts.Domain.Enums.DepthIntegrityKind.yml
│   │   │   ├── Meridian.Contracts.Domain.Enums.DepthOperation.yml
│   │   │   ├── Meridian.Contracts.Domain.Enums.InstrumentType.yml
│   │   │   ├── Meridian.Contracts.Domain.Enums.IntegritySeverity.yml
│   │   │   ├── Meridian.Contracts.Domain.Enums.LiquidityProfile.yml
│   │   │   ├── Meridian.Contracts.Domain.Enums.MarketEventTier.yml
│   │   │   ├── Meridian.Contracts.Domain.Enums.MarketEventType.yml
│   │   │   ├── Meridian.Contracts.Domain.Enums.MarketState.yml
│   │   │   ├── Meridian.Contracts.Domain.Enums.OptionRight.yml
│   │   │   ├── Meridian.Contracts.Domain.Enums.OptionStyle.yml
│   │   │   ├── Meridian.Contracts.Domain.Enums.OrderBookSide.yml
│   │   │   ├── Meridian.Contracts.Domain.Enums.OrderSide.yml
│   │   │   ├── Meridian.Contracts.Domain.Enums.yml
│   │   │   ├── Meridian.Contracts.Domain.Events.IMarketEventPayload.yml
│   │   │   ├── Meridian.Contracts.Domain.Events.MarketEventDto.yml
│   │   │   ├── Meridian.Contracts.Domain.Events.MarketEventPayload.HeartbeatPayload.yml
│   │   │   ├── Meridian.Contracts.Domain.Events.MarketEventPayload.yml
│   │   │   ├── Meridian.Contracts.Domain.Events.yml
│   │   │   ├── Meridian.Contracts.Domain.HistoricalBarDto.yml
│   │   │   ├── Meridian.Contracts.Domain.IntegrityEventDto.yml
│   │   │   ├── Meridian.Contracts.Domain.IPositionSnapshotStore.yml
│   │   │   ├── Meridian.Contracts.Domain.MarketStateValues.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.AdjustedHistoricalBar.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.AggregateBarPayload.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.AggregateTimeframe.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.AuctionPrice.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.BboQuotePayload.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.DepthIntegrityEvent.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.GreeksSnapshot.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.HistoricalAuction.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.HistoricalBar.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.HistoricalQuote.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.HistoricalTrade.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.IntegrityEvent.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.L2SnapshotPayload.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.LOBSnapshot.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.MarketQuoteUpdate.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.OpenInterestUpdate.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.OptionChainSnapshot.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.OptionContractSpec.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.OptionQuote.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.OptionTrade.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.OrderAdd.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.OrderBookLevel.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.OrderCancel.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.OrderExecute.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.OrderFlowStatistics.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.OrderModify.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.OrderReplace.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.Trade.yml
│   │   │   ├── Meridian.Contracts.Domain.Models.yml
│   │   │   ├── Meridian.Contracts.Domain.OrderBookLevelDto.yml
│   │   │   ├── Meridian.Contracts.Domain.OrderBookSideValues.yml
│   │   │   ├── Meridian.Contracts.Domain.OrderBookSnapshotDto.yml
│   │   │   ├── Meridian.Contracts.Domain.PositionRecord.yml
│   │   │   ├── Meridian.Contracts.Domain.ProviderId.yml
│   │   │   ├── Meridian.Contracts.Domain.ProviderSymbol.yml
│   │   │   ├── Meridian.Contracts.Domain.QuoteDto.yml
│   │   │   ├── Meridian.Contracts.Domain.StreamId.yml
│   │   │   ├── Meridian.Contracts.Domain.SubscriptionId.yml
│   │   │   ├── Meridian.Contracts.Domain.SymbolId.yml
│   │   │   ├── Meridian.Contracts.Domain.TradeDto.yml
│   │   │   ├── Meridian.Contracts.Domain.VenueCode.yml
│   │   │   ├── Meridian.Contracts.Domain.yml
│   │   │   ├── Meridian.Contracts.Etl.CsvSchemaDefinition.yml
│   │   │   ├── Meridian.Contracts.Etl.EtlAuditEvent.yml
│   │   │   ├── Meridian.Contracts.Etl.EtlCheckpointToken.yml
│   │   │   ├── Meridian.Contracts.Etl.EtlDestinationDefinition.yml
│   │   │   ├── Meridian.Contracts.Etl.EtlDestinationKind.yml
│   │   │   ├── Meridian.Contracts.Etl.EtlFileManifest.yml
│   │   │   ├── Meridian.Contracts.Etl.EtlFlowDirection.yml
│   │   │   ├── Meridian.Contracts.Etl.EtlJobDefinition.yml
│   │   │   ├── Meridian.Contracts.Etl.EtlPackageFormat.yml
│   │   │   ├── Meridian.Contracts.Etl.EtlRecordDisposition.yml
│   │   │   ├── Meridian.Contracts.Etl.EtlRejectRecord.yml
│   │   │   ├── Meridian.Contracts.Etl.EtlRemoteFile.yml
│   │   │   ├── Meridian.Contracts.Etl.EtlSourceDefinition.yml
│   │   │   ├── Meridian.Contracts.Etl.EtlSourceKind.yml
│   │   │   ├── Meridian.Contracts.Etl.EtlStagedFile.yml
│   │   │   ├── Meridian.Contracts.Etl.EtlTransferMode.yml
│   │   │   ├── Meridian.Contracts.Etl.IEtlSourceReader.yml
│   │   │   ├── Meridian.Contracts.Etl.IPartnerFileParser.yml
│   │   │   ├── Meridian.Contracts.Etl.IPartnerSchemaRegistry.yml
│   │   │   ├── Meridian.Contracts.Etl.PartnerRecordEnvelope.yml
│   │   │   ├── Meridian.Contracts.Etl.yml
│   │   │   ├── Meridian.Contracts.Export.AggregationOption.yml
│   │   │   ├── Meridian.Contracts.Export.AnalysisExportFormat.yml
│   │   │   ├── Meridian.Contracts.Export.AnalysisExportOptions.yml
│   │   │   ├── Meridian.Contracts.Export.AnalysisExportResponse.yml
│   │   │   ├── Meridian.Contracts.Export.AnalysisExportResult.yml
│   │   │   ├── Meridian.Contracts.Export.CompressionType.yml
│   │   │   ├── Meridian.Contracts.Export.DataAggregation.yml
│   │   │   ├── Meridian.Contracts.Export.DataTypeInclusion.yml
│   │   │   ├── Meridian.Contracts.Export.DateRangeType.yml
│   │   │   ├── Meridian.Contracts.Export.ExportFormatInfo.yml
│   │   │   ├── Meridian.Contracts.Export.ExportFormatsResponse.yml
│   │   │   ├── Meridian.Contracts.Export.ExportFormatsResult.yml
│   │   │   ├── Meridian.Contracts.Export.ExportPreset.yml
│   │   │   ├── Meridian.Contracts.Export.ExportPresetCompression.yml
│   │   │   ├── Meridian.Contracts.Export.ExportPresetFilters.yml
│   │   │   ├── Meridian.Contracts.Export.ExportPresetFormat.yml
│   │   │   ├── Meridian.Contracts.Export.ExportProgressEventArgs.yml
│   │   │   ├── Meridian.Contracts.Export.ExportTemplate.yml
│   │   │   ├── Meridian.Contracts.Export.ExportValidationRules.yml
│   │   │   ├── Meridian.Contracts.Export.IntegrityExportOptions.yml
│   │   │   ├── Meridian.Contracts.Export.OrderFlowExportOptions.yml
│   │   │   ├── Meridian.Contracts.Export.QualityReportOptions.yml
│   │   │   ├── Meridian.Contracts.Export.QualityReportResponse.yml
│   │   │   ├── Meridian.Contracts.Export.QualityReportResult.yml
│   │   │   ├── Meridian.Contracts.Export.QualityReportSummary.yml
│   │   │   ├── Meridian.Contracts.Export.ResearchPackageOptions.yml
│   │   │   ├── Meridian.Contracts.Export.ResearchPackageResponse.yml
│   │   │   ├── Meridian.Contracts.Export.ResearchPackageResult.yml
│   │   │   ├── Meridian.Contracts.Export.StandardPresets.yml
│   │   │   ├── Meridian.Contracts.Export.yml
│   │   │   ├── Meridian.Contracts.FundStructure.AccountBalanceSnapshotDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.AccountManagementOptions.yml
│   │   │   ├── Meridian.Contracts.FundStructure.AccountReconciliationResultDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.AccountReconciliationRunDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.AccountStructureQuery.yml
│   │   │   ├── Meridian.Contracts.FundStructure.AccountSummaryDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.AccountTypeDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.AssignFundStructureNodeRequest.yml
│   │   │   ├── Meridian.Contracts.FundStructure.BankAccountDetailsDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.BankStatementBatchDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.BankStatementLineDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.CreateAccountRequest.yml
│   │   │   ├── Meridian.Contracts.FundStructure.CreateFundRequest.yml
│   │   │   ├── Meridian.Contracts.FundStructure.CreateLegalEntityRequest.yml
│   │   │   ├── Meridian.Contracts.FundStructure.CreateSleeveRequest.yml
│   │   │   ├── Meridian.Contracts.FundStructure.CreateVehicleRequest.yml
│   │   │   ├── Meridian.Contracts.FundStructure.CustodianAccountDetailsDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.CustodianPositionLineDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.CustodianStatementBatchDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.FundAccountsDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.FundStructureAssignmentDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.FundStructureAssignmentQuery.yml
│   │   │   ├── Meridian.Contracts.FundStructure.FundStructureGraphDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.FundStructureNodeDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.FundStructureNodeKindDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.FundStructureQuery.yml
│   │   │   ├── Meridian.Contracts.FundStructure.FundSummaryDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.IngestBankStatementRequest.yml
│   │   │   ├── Meridian.Contracts.FundStructure.IngestCustodianStatementRequest.yml
│   │   │   ├── Meridian.Contracts.FundStructure.LegalEntitySummaryDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.LegalEntityTypeDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.LinkFundStructureNodesRequest.yml
│   │   │   ├── Meridian.Contracts.FundStructure.OwnershipLinkDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.OwnershipRelationshipTypeDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.ReconcileAccountRequest.yml
│   │   │   ├── Meridian.Contracts.FundStructure.RecordAccountBalanceSnapshotRequest.yml
│   │   │   ├── Meridian.Contracts.FundStructure.SleeveSummaryDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.UpdateBankAccountDetailsRequest.yml
│   │   │   ├── Meridian.Contracts.FundStructure.UpdateCustodianAccountDetailsRequest.yml
│   │   │   ├── Meridian.Contracts.FundStructure.VehicleSummaryDto.yml
│   │   │   ├── Meridian.Contracts.FundStructure.yml
│   │   │   ├── Meridian.Contracts.Manifest.DataManifest.yml
│   │   │   ├── Meridian.Contracts.Manifest.DataQualityMetrics.yml
│   │   │   ├── Meridian.Contracts.Manifest.DateRangeInfo.yml
│   │   │   ├── Meridian.Contracts.Manifest.ManifestFileEntry.yml
│   │   │   ├── Meridian.Contracts.Manifest.VerificationStatusValues.yml
│   │   │   ├── Meridian.Contracts.Manifest.yml
│   │   │   ├── Meridian.Contracts.Pipeline.IngestionCheckpointToken.yml
│   │   │   ├── Meridian.Contracts.Pipeline.IngestionJob.yml
│   │   │   ├── Meridian.Contracts.Pipeline.IngestionJobState.yml
│   │   │   ├── Meridian.Contracts.Pipeline.IngestionSla.yml
│   │   │   ├── Meridian.Contracts.Pipeline.IngestionSymbolProgress.yml
│   │   │   ├── Meridian.Contracts.Pipeline.IngestionWorkloadType.yml
│   │   │   ├── Meridian.Contracts.Pipeline.PipelinePolicyConstants.yml
│   │   │   ├── Meridian.Contracts.Pipeline.RetryEnvelope.yml
│   │   │   ├── Meridian.Contracts.Pipeline.yml
│   │   │   ├── Meridian.Contracts.Schema.DataDictionary.yml
│   │   │   ├── Meridian.Contracts.Schema.EventSchema.yml
│   │   │   ├── Meridian.Contracts.Schema.FieldValidRange.yml
│   │   │   ├── Meridian.Contracts.Schema.ISchemaUpcaster-1.yml
│   │   │   ├── Meridian.Contracts.Schema.SchemaField.yml
│   │   │   ├── Meridian.Contracts.Schema.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.AmendSecurityTermsRequest.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.CorporateActionDto.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.CreateSecurityRequest.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.DeactivateSecurityRequest.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.ISecurityMasterAmender.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.ISecurityMasterService.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.ResolveConflictRequest.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.ResolveSecurityRequest.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.SecurityAliasDto.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.SecurityAliasScope.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.SecurityDetailDto.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.SecurityEconomicDefinitionRecord.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.SecurityHistoryRequest.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.SecurityIdentifierDto.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.SecurityIdentifierKind.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.SecurityMasterConflict.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.SecurityMasterEventEnvelope.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.SecurityMasterImportRequest.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.SecurityMasterOptions.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.SecurityProjectionRecord.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.SecuritySearchRequest.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.SecuritySnapshotRecord.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.SecurityStatusDto.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.SecuritySummaryDto.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.TradingParametersDto.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.UpsertSecurityAliasRequest.yml
│   │   │   ├── Meridian.Contracts.SecurityMaster.yml
│   │   │   ├── Meridian.Contracts.Services.IConnectivityProbeService.yml
│   │   │   ├── Meridian.Contracts.Services.yml
│   │   │   ├── Meridian.Contracts.Session.CollectionSession.yml
│   │   │   ├── Meridian.Contracts.Session.CollectionSessionsConfig.yml
│   │   │   ├── Meridian.Contracts.Session.CollectionSessionStatistics.yml
│   │   │   ├── Meridian.Contracts.Session.SessionStatus.yml
│   │   │   ├── Meridian.Contracts.Session.yml
│   │   │   ├── Meridian.Contracts.Store.MarketDataQuery.yml
│   │   │   ├── Meridian.Contracts.Store.yml
│   │   │   ├── Meridian.Contracts.Treasury.MmfDetailDto.yml
│   │   │   ├── Meridian.Contracts.Treasury.MmfFundFamilyDto.yml
│   │   │   ├── Meridian.Contracts.Treasury.MmfLiquidityDto.yml
│   │   │   ├── Meridian.Contracts.Treasury.MmfLiquidityState.yml
│   │   │   ├── Meridian.Contracts.Treasury.MmfRebuildCheckpointDto.yml
│   │   │   ├── Meridian.Contracts.Treasury.MmfSearchQuery.yml
│   │   │   ├── Meridian.Contracts.Treasury.MmfSweepProfileDto.yml
│   │   │   ├── Meridian.Contracts.Treasury.yml
│   │   │   ├── Meridian.Contracts.Workstation.BankAccountSnapshot.yml
│   │   │   ├── Meridian.Contracts.Workstation.CashFinancingSummary.yml
│   │   │   ├── Meridian.Contracts.Workstation.CashFlowEntryDto.yml
│   │   │   ├── Meridian.Contracts.Workstation.CashLadderBucketDto.yml
│   │   │   ├── Meridian.Contracts.Workstation.EquityCurvePoint.yml
│   │   │   ├── Meridian.Contracts.Workstation.EquityCurveSummary.yml
│   │   │   ├── Meridian.Contracts.Workstation.FundAccountSummary.yml
│   │   │   ├── Meridian.Contracts.Workstation.FundAuditEntry.yml
│   │   │   ├── Meridian.Contracts.Workstation.FundJournalLine.yml
│   │   │   ├── Meridian.Contracts.Workstation.FundLedgerQuery.yml
│   │   │   ├── Meridian.Contracts.Workstation.FundLedgerScope.yml
│   │   │   ├── Meridian.Contracts.Workstation.FundLedgerSummary.yml
│   │   │   ├── Meridian.Contracts.Workstation.FundOperationsNavigationContext.yml
│   │   │   ├── Meridian.Contracts.Workstation.FundOperationsTab.yml
│   │   │   ├── Meridian.Contracts.Workstation.FundPortfolioPosition.yml
│   │   │   ├── Meridian.Contracts.Workstation.FundReconciliationItem.yml
│   │   │   ├── Meridian.Contracts.Workstation.FundTrialBalanceLine.yml
│   │   │   ├── Meridian.Contracts.Workstation.FundWorkspaceSummary.yml
│   │   │   ├── Meridian.Contracts.Workstation.LedgerJournalLine.yml
│   │   │   ├── Meridian.Contracts.Workstation.LedgerSummary.yml
│   │   │   ├── Meridian.Contracts.Workstation.LedgerTrialBalanceLine.yml
│   │   │   ├── Meridian.Contracts.Workstation.PortfolioPositionSummary.yml
│   │   │   ├── Meridian.Contracts.Workstation.PortfolioSummary.yml
│   │   │   ├── Meridian.Contracts.Workstation.ReconciliationBreakCategory.yml
│   │   │   ├── Meridian.Contracts.Workstation.ReconciliationBreakDto.yml
│   │   │   ├── Meridian.Contracts.Workstation.ReconciliationBreakQueueItem.yml
│   │   │   ├── Meridian.Contracts.Workstation.ReconciliationBreakQueueStatus.yml
│   │   │   ├── Meridian.Contracts.Workstation.ReconciliationBreakStatus.yml
│   │   │   ├── Meridian.Contracts.Workstation.ReconciliationMatchDto.yml
│   │   │   ├── Meridian.Contracts.Workstation.ReconciliationRunDetail.yml
│   │   │   ├── Meridian.Contracts.Workstation.ReconciliationRunRequest.yml
│   │   │   ├── Meridian.Contracts.Workstation.ReconciliationRunSummary.yml
│   │   │   ├── Meridian.Contracts.Workstation.ReconciliationSecurityCoverageIssueDto.yml
│   │   │   ├── Meridian.Contracts.Workstation.ReconciliationSourceKind.yml
│   │   │   ├── Meridian.Contracts.Workstation.ReconciliationSummary.yml
│   │   │   ├── Meridian.Contracts.Workstation.ResolveReconciliationBreakRequest.yml
│   │   │   ├── Meridian.Contracts.Workstation.ReviewReconciliationBreakRequest.yml
│   │   │   ├── Meridian.Contracts.Workstation.RunAttributionSummary.yml
│   │   │   ├── Meridian.Contracts.Workstation.RunCashFlowSummary.yml
│   │   │   ├── Meridian.Contracts.Workstation.RunCashLadder.yml
│   │   │   ├── Meridian.Contracts.Workstation.RunComparisonDto.yml
│   │   │   ├── Meridian.Contracts.Workstation.RunFillEntry.yml
│   │   │   ├── Meridian.Contracts.Workstation.RunFillSummary.yml
│   │   │   ├── Meridian.Contracts.Workstation.SecurityClassificationSummaryDto.yml
│   │   │   ├── Meridian.Contracts.Workstation.SecurityEconomicDefinitionSummaryDto.yml
│   │   │   ├── Meridian.Contracts.Workstation.SecurityIdentityDrillInDto.yml
│   │   │   ├── Meridian.Contracts.Workstation.SecurityMasterWorkstationDto.yml
│   │   │   ├── Meridian.Contracts.Workstation.StrategyRunComparison.yml
│   │   │   ├── Meridian.Contracts.Workstation.StrategyRunDetail.yml
│   │   │   ├── Meridian.Contracts.Workstation.StrategyRunEngine.yml
│   │   │   ├── Meridian.Contracts.Workstation.StrategyRunExecutionSummary.yml
│   │   │   ├── Meridian.Contracts.Workstation.StrategyRunGovernanceSummary.yml
│   │   │   ├── Meridian.Contracts.Workstation.StrategyRunHistoryQuery.yml
│   │   │   ├── Meridian.Contracts.Workstation.StrategyRunMode.yml
│   │   │   ├── Meridian.Contracts.Workstation.StrategyRunPromotionState.yml
│   │   │   ├── Meridian.Contracts.Workstation.StrategyRunPromotionSummary.yml
│   │   │   ├── Meridian.Contracts.Workstation.StrategyRunStatus.yml
│   │   │   ├── Meridian.Contracts.Workstation.StrategyRunSummary.yml
│   │   │   ├── Meridian.Contracts.Workstation.StrategyRunTimelineEntry.yml
│   │   │   ├── Meridian.Contracts.Workstation.SymbolAttributionEntry.yml
│   │   │   ├── Meridian.Contracts.Workstation.WorkstationSecurityReference.yml
│   │   │   ├── Meridian.Contracts.Workstation.yml
│   │   │   ├── Meridian.Core.Performance.ConnectionWarmUp.yml
│   │   │   ├── Meridian.Core.Performance.ExponentialBackoffRetry.yml
│   │   │   ├── Meridian.Core.Performance.HeartbeatMonitor.yml
│   │   │   ├── Meridian.Core.Performance.HeartbeatResult.yml
│   │   │   ├── Meridian.Core.Performance.HighResolutionTimestamp.yml
│   │   │   ├── Meridian.Core.Performance.RawQuoteEvent.yml
│   │   │   ├── Meridian.Core.Performance.RawTradeEvent.yml
│   │   │   ├── Meridian.Core.Performance.SpscRingBuffer-1.yml
│   │   │   ├── Meridian.Core.Performance.SymbolTable.yml
│   │   │   ├── Meridian.Core.Performance.ThreadingUtilities.yml
│   │   │   ├── Meridian.Core.Performance.ThreadLocalSequenceGenerator.yml
│   │   │   ├── Meridian.Core.Performance.WarmUpStatistics.yml
│   │   │   ├── Meridian.Core.Performance.yml
│   │   │   ├── Meridian.Core.Scheduling.CronExpressionParser.yml
│   │   │   ├── Meridian.Core.Scheduling.CronSchedule.yml
│   │   │   ├── Meridian.Core.Scheduling.yml
│   │   │   ├── Meridian.Core.Serialization.SecurityMasterJsonContext.yml
│   │   │   ├── Meridian.Core.Serialization.yml
│   │   │   ├── Meridian.Domain.Collectors.IQuoteStateStore.yml
│   │   │   ├── Meridian.Domain.Collectors.L3OrderBookCollector.yml
│   │   │   ├── Meridian.Domain.Collectors.MarketDepthCollector.yml
│   │   │   ├── Meridian.Domain.Collectors.OptionDataCollector.yml
│   │   │   ├── Meridian.Domain.Collectors.OptionDataSummary.yml
│   │   │   ├── Meridian.Domain.Collectors.QuoteCollector.yml
│   │   │   ├── Meridian.Domain.Collectors.SymbolSubscriptionTracker.yml
│   │   │   ├── Meridian.Domain.Collectors.TradeDataCollector.yml
│   │   │   ├── Meridian.Domain.Collectors.yml
│   │   │   ├── Meridian.Domain.Events.IBackpressureSignal.yml
│   │   │   ├── Meridian.Domain.Events.IMarketEventPublisher.yml
│   │   │   ├── Meridian.Domain.Events.MarketEvent.yml
│   │   │   ├── Meridian.Domain.Events.MarketEventPayload.yml
│   │   │   ├── Meridian.Domain.Events.Publishers.CompositePublisher.yml
│   │   │   ├── Meridian.Domain.Events.Publishers.yml
│   │   │   ├── Meridian.Domain.Events.PublishResult.yml
│   │   │   ├── Meridian.Domain.Events.yml
│   │   │   ├── Meridian.Domain.Models.AggregateBar.yml
│   │   │   ├── Meridian.Domain.Models.AggregateTimeframe.yml
│   │   │   ├── Meridian.Domain.Models.MarketDepthUpdate.yml
│   │   │   ├── Meridian.Domain.Models.MarketTradeUpdate.yml
│   │   │   ├── Meridian.Domain.Models.yml
│   │   │   ├── Meridian.Execution.Adapters.BaseBrokerageGateway.yml
│   │   │   ├── Meridian.Execution.Adapters.BrokerageGatewayAdapter.yml
│   │   │   ├── Meridian.Execution.Adapters.PaperTradingGateway.yml
│   │   │   ├── Meridian.Execution.Adapters.yml
│   │   │   ├── Meridian.Execution.Allocation.AllocationResult.yml
│   │   │   ├── Meridian.Execution.Allocation.AllocationRule.yml
│   │   │   ├── Meridian.Execution.Allocation.AllocationSlice.yml
│   │   │   ├── Meridian.Execution.Allocation.BlockTradeAllocator.yml
│   │   │   ├── Meridian.Execution.Allocation.IAllocationEngine.yml
│   │   │   ├── Meridian.Execution.Allocation.ProportionalAllocationEngine.yml
│   │   │   ├── Meridian.Execution.Allocation.yml
│   │   │   ├── Meridian.Execution.BrokerageServiceRegistration.yml
│   │   │   ├── Meridian.Execution.Derivatives.DerivativeKind.yml
│   │   │   ├── Meridian.Execution.Derivatives.FuturePosition.yml
│   │   │   ├── Meridian.Execution.Derivatives.IDerivativePosition.yml
│   │   │   ├── Meridian.Execution.Derivatives.OptionPosition.yml
│   │   │   ├── Meridian.Execution.Derivatives.yml
│   │   │   ├── Meridian.Execution.Events.ITradeEventPublisher.yml
│   │   │   ├── Meridian.Execution.Events.LedgerPostingConsumer.yml
│   │   │   ├── Meridian.Execution.Events.TradeExecutedEvent.yml
│   │   │   ├── Meridian.Execution.Events.yml
│   │   │   ├── Meridian.Execution.Exceptions.UnsupportedOrderRequestException.yml
│   │   │   ├── Meridian.Execution.Exceptions.yml
│   │   │   ├── Meridian.Execution.Interfaces.ExecutionAccountDetailSnapshot.yml
│   │   │   ├── Meridian.Execution.Interfaces.IAccountPortfolio.yml
│   │   │   ├── Meridian.Execution.Interfaces.IExecutionContext.yml
│   │   │   ├── Meridian.Execution.Interfaces.ILiveFeedAdapter.yml
│   │   │   ├── Meridian.Execution.Interfaces.IOrderGateway.yml
│   │   │   ├── Meridian.Execution.Interfaces.yml
│   │   │   ├── Meridian.Execution.IRiskValidator.yml
│   │   │   ├── Meridian.Execution.ISecurityMasterGate.yml
│   │   │   ├── Meridian.Execution.Margin.IMarginModel.yml
│   │   │   ├── Meridian.Execution.Margin.MarginRequirement.yml
│   │   │   ├── Meridian.Execution.Margin.PortfolioMarginModel.yml
│   │   │   ├── Meridian.Execution.Margin.RegTMarginModel.yml
│   │   │   ├── Meridian.Execution.Margin.yml
│   │   │   ├── Meridian.Execution.Models.AccountKind.yml
│   │   │   ├── Meridian.Execution.Models.ExecutionMode.yml
│   │   │   ├── Meridian.Execution.Models.ExecutionPosition.yml
│   │   │   ├── Meridian.Execution.Models.IMultiAccountPortfolioState.yml
│   │   │   ├── Meridian.Execution.Models.IPortfolioState.yml
│   │   │   ├── Meridian.Execution.Models.MultiAccountPortfolioSnapshot.yml
│   │   │   ├── Meridian.Execution.Models.OrderAcknowledgement.yml
│   │   │   ├── Meridian.Execution.Models.OrderGatewayCapabilities.yml
│   │   │   ├── Meridian.Execution.Models.OrderStatus.yml
│   │   │   ├── Meridian.Execution.Models.OrderStatusUpdate.yml
│   │   │   ├── Meridian.Execution.Models.OrderValidationResult.yml
│   │   │   ├── Meridian.Execution.Models.yml
│   │   │   ├── Meridian.Execution.MultiCurrency.FxRate.yml
│   │   │   ├── Meridian.Execution.MultiCurrency.IFxRateProvider.yml
│   │   │   ├── Meridian.Execution.MultiCurrency.MultiCurrencyCashBalance.yml
│   │   │   ├── Meridian.Execution.MultiCurrency.yml
│   │   │   ├── Meridian.Execution.OrderManagementSystem.yml
│   │   │   ├── Meridian.Execution.PaperExecutionContext.yml
│   │   │   ├── Meridian.Execution.PaperTradingGateway.yml
│   │   │   ├── Meridian.Execution.RiskValidationResult.yml
│   │   │   ├── Meridian.Execution.Sdk.AccountInfo.yml
│   │   │   ├── Meridian.Execution.Sdk.BrokerageAccountSummaryDto.yml
│   │   │   ├── Meridian.Execution.Sdk.BrokerageCapabilities.yml
│   │   │   ├── Meridian.Execution.Sdk.BrokerageConfiguration.yml
│   │   │   ├── Meridian.Execution.Sdk.BrokeragePositionDto.yml
│   │   │   ├── Meridian.Execution.Sdk.BrokerHealthStatus.yml
│   │   │   ├── Meridian.Execution.Sdk.BrokerOrder.yml
│   │   │   ├── Meridian.Execution.Sdk.BrokerPosition.yml
│   │   │   ├── Meridian.Execution.Sdk.Derivatives.FutureDetails.yml
│   │   │   ├── Meridian.Execution.Sdk.Derivatives.OptionDetails.yml
│   │   │   ├── Meridian.Execution.Sdk.Derivatives.OptionGreeks.yml
│   │   │   ├── Meridian.Execution.Sdk.Derivatives.OptionRight.yml
│   │   │   ├── Meridian.Execution.Sdk.Derivatives.OptionStyle.yml
│   │   │   ├── Meridian.Execution.Sdk.Derivatives.yml
│   │   │   ├── Meridian.Execution.Sdk.ExecutionMode.yml
│   │   │   ├── Meridian.Execution.Sdk.ExecutionReport.yml
│   │   │   ├── Meridian.Execution.Sdk.ExecutionReportType.yml
│   │   │   ├── Meridian.Execution.Sdk.IBrokerageGateway.yml
│   │   │   ├── Meridian.Execution.Sdk.IBrokeragePositionSync.yml
│   │   │   ├── Meridian.Execution.Sdk.IExecutionGateway.yml
│   │   │   ├── Meridian.Execution.Sdk.IOrderManager.yml
│   │   │   ├── Meridian.Execution.Sdk.IPositionTracker.yml
│   │   │   ├── Meridian.Execution.Sdk.OrderModification.yml
│   │   │   ├── Meridian.Execution.Sdk.OrderRequest.yml
│   │   │   ├── Meridian.Execution.Sdk.OrderResult.yml
│   │   │   ├── Meridian.Execution.Sdk.OrderSide.yml
│   │   │   ├── Meridian.Execution.Sdk.OrderState.yml
│   │   │   ├── Meridian.Execution.Sdk.OrderStatus.yml
│   │   │   ├── Meridian.Execution.Sdk.OrderType.yml
│   │   │   ├── Meridian.Execution.Sdk.PositionState.yml
│   │   │   ├── Meridian.Execution.Sdk.TaxLot.yml
│   │   │   ├── Meridian.Execution.Sdk.TimeInForce.yml
│   │   │   ├── Meridian.Execution.Sdk.yml
│   │   │   ├── Meridian.Execution.SecurityMasterGate.yml
│   │   │   ├── Meridian.Execution.SecurityMasterGateResult.yml
│   │   │   ├── Meridian.Execution.Services.AccountDefinition.yml
│   │   │   ├── Meridian.Execution.Services.CreatePaperSessionDto.yml
│   │   │   ├── Meridian.Execution.Services.ExecutionAuditEntry.yml
│   │   │   ├── Meridian.Execution.Services.ExecutionAuditTrailOptions.yml
│   │   │   ├── Meridian.Execution.Services.ExecutionAuditTrailService.yml
│   │   │   ├── Meridian.Execution.Services.ExecutionCircuitBreakerState.yml
│   │   │   ├── Meridian.Execution.Services.ExecutionControlDecision.yml
│   │   │   ├── Meridian.Execution.Services.ExecutionControlSnapshot.yml
│   │   │   ├── Meridian.Execution.Services.ExecutionManualOverride.yml
│   │   │   ├── Meridian.Execution.Services.ExecutionManualOverrideKinds.yml
│   │   │   ├── Meridian.Execution.Services.ExecutionOperatorControlOptions.yml
│   │   │   ├── Meridian.Execution.Services.ExecutionOperatorControlService.yml
│   │   │   ├── Meridian.Execution.Services.ExecutionPortfolioSnapshotDto.yml
│   │   │   ├── Meridian.Execution.Services.IPaperSessionStore.yml
│   │   │   ├── Meridian.Execution.Services.JsonlFilePaperSessionStore.yml
│   │   │   ├── Meridian.Execution.Services.LivePromotionControlDecision.yml
│   │   │   ├── Meridian.Execution.Services.ManualOverrideRequest.yml
│   │   │   ├── Meridian.Execution.Services.OrderLifecycleManager.yml
│   │   │   ├── Meridian.Execution.Services.PaperSessionDetailDto.yml
│   │   │   ├── Meridian.Execution.Services.PaperSessionOptions.yml
│   │   │   ├── Meridian.Execution.Services.PaperSessionPersistenceService.yml
│   │   │   ├── Meridian.Execution.Services.PaperSessionSummaryDto.yml
│   │   │   ├── Meridian.Execution.Services.PaperTradingPortfolio.yml
│   │   │   ├── Meridian.Execution.Services.PersistedJournalEntryDto.yml
│   │   │   ├── Meridian.Execution.Services.PersistedLedgerAccountDto.yml
│   │   │   ├── Meridian.Execution.Services.PersistedLedgerLineDto.yml
│   │   │   ├── Meridian.Execution.Services.PersistedSessionRecord.yml
│   │   │   ├── Meridian.Execution.Services.PortfolioRegistry.yml
│   │   │   ├── Meridian.Execution.Services.PositionReconciliationService.yml
│   │   │   ├── Meridian.Execution.Services.PositionSyncOptions.yml
│   │   │   ├── Meridian.Execution.Services.ReconciliationReport.yml
│   │   │   ├── Meridian.Execution.Services.yml
│   │   │   ├── Meridian.Execution.TaxLotAccounting.ITaxLotSelector.yml
│   │   │   ├── Meridian.Execution.TaxLotAccounting.RelievedLot.yml
│   │   │   ├── Meridian.Execution.TaxLotAccounting.TaxLotAccountingMethod.yml
│   │   │   ├── Meridian.Execution.TaxLotAccounting.TaxLotReliefResult.yml
│   │   │   ├── Meridian.Execution.TaxLotAccounting.TaxLotSelectors.yml
│   │   │   ├── Meridian.Execution.TaxLotAccounting.yml
│   │   │   ├── Meridian.Execution.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Alpaca.AlpacaBrokerageGateway.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Alpaca.AlpacaCorporateActionProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Alpaca.AlpacaHistoricalDataProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Alpaca.AlpacaMarketDataClient.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Alpaca.AlpacaProviderModule.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Alpaca.AlpacaSymbolSearchProviderRefactored.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Alpaca.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.AlphaVantage.AlphaVantageHistoricalDataProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.AlphaVantage.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BackfillError.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BackfillJob.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BackfillJobManager.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BackfillJobOptions.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BackfillJobRequest.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BackfillJobStatistics.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BackfillJobStatus.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BackfillPriority.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BackfillProgressSnapshot.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BackfillProgressTracker.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BackfillQueueStatistics.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BackfillRequest.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BackfillRequestQueue.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BackfillRequestStatus.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BackfillServiceFactory.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BackfillServices.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BackfillSymbolProgress.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BackfillWorkerService.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BaseHistoricalDataProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BaseSymbolSearchProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BatchEnqueueError.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BatchEnqueueOptions.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.BatchEnqueueResult.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.CompositeHistoricalDataProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.CompositeProviderOptions.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.CorporateActionCommand.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.CoverageReport.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.DataFileInfo.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.DataGap.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.DataGapAnalyzer.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.DataGapRepairService.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.DataGranularity.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.DataGranularityExtensions.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.DataQualityMonitor.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.EnvironmentCredentialResolver.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.FailedModuleInfo.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.GapAnalysisResult.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.GapRepairItemResult.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.GapRepairOptions.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.GapRepairResult.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.GapReport.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.GapSeverity.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.GapType.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.HistoricalAuctionsResult.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.HistoricalDataCapabilities.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.HistoricalQuotesResult.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.HistoricalTradesResult.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ICorporateActionProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.IFilterableSymbolSearchProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.IHistoricalDataProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.IOptionsChainProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.IProviderCredentialResolver.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.IProviderMetadata.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.IProviderModule.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.IRateLimitAwareProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ISymbolSearchProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.JobStatusChangedEventArgs.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.LoadedModuleInfo.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ModuleLoadReport.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ModuleValidationResult.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.OptionsChainCapabilities.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.PriorityBackfillQueue.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ProviderAvailabilityExtensions.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ProviderAvailabilitySummary.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ProviderBackfillProgress.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ProviderBehaviorBuilder.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ProviderCapabilities.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ProviderCreationResult.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ProviderCredentialField.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ProviderFactory.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ProviderHealthStatus.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ProviderInfo.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ProviderModuleLoader.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ProviderRateLimitProfile.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ProviderRateLimitTracker.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ProviderRegistry.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ProviderRegistrySummary.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ProviderServiceExtensions.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ProviderSubscriptionRanges.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ProviderTemplate.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ProviderTemplateFactory.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ProviderType.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.QualityAlert.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.QualityDimension.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.QualityIssue.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.QualityIssueType.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.QualityMonitorOptions.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.QualityScore.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.QualitySeverity.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.QueueStateChangedEventArgs.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.QueueStatistics.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.RateLimiter.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.RateLimiterRegistry.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.RateLimitInfo.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.RateLimitStatus.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ResponseHandler.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.ResponseResult.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.SymbolBackfillProgress.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.SymbolBackfillStatus.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.SymbolDataInventory.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.SymbolGapInfo.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.SymbolResolution.ISymbolResolver.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.SymbolResolution.SymbolResolution.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.SymbolResolution.SymbolSearchResult.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.SymbolResolution.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.SymbolSearchUtility.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.WebSocketProviderBase.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Core.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Edgar.EdgarSecurityMasterIngestProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Edgar.EdgarSymbolSearchProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Edgar.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Failover.FailoverAwareMarketDataClient.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Failover.FailoverRecoveredEvent.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Failover.FailoverRuleSnapshot.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Failover.FailoverTriggeredEvent.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Failover.ProviderHealthSnapshot.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Failover.StreamingFailoverRegistry.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Failover.StreamingFailoverService.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Failover.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Finnhub.FinnhubCompanyProfile.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Finnhub.FinnhubEarning.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Finnhub.FinnhubHistoricalDataProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Finnhub.FinnhubSymbolSearchProviderRefactored.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Finnhub.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Fred.FredHistoricalDataProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Fred.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.ContractFactory.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.EnhancedIBConnectionManager.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBApiError.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBApiException.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBApiLimits.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBApiVersionMismatchException.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBApiVersionValidator.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBBarSizes.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBBrokerageGateway.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBCallbackRouter.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBConnectionManager.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBDurationStrings.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBErrorCodeMap.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBErrorInfo.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBErrorSeverity.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBGenericTickTypes.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBHistoricalDataProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBMarketDataClient.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBMarketDataNotSubscribedException.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBPacingViolationException.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBSecurityNotFoundException.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBSimulationClient.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBTickByTickTypes.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBTickTypes.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.IBWhatToShow.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.InteractiveBrokers.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.NasdaqDataLink.NasdaqDataLinkHistoricalDataProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.NasdaqDataLink.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.NYSE.NYSEDataSource.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.NYSE.NYSEFeedTier.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.NYSE.NyseMarketDataClient.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.NYSE.NyseNationalTradesCsvParser.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.NYSE.NYSEOptions.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.NYSE.NYSEServiceExtensions.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.NYSE.NyseTaqTradeRecord.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.NYSE.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.OpenFigi.OpenFigiClient.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.OpenFigi.OpenFigiSymbolResolver.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.OpenFigi.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Polygon.IPolygonCorporateActionFetcher.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Polygon.ITradingParametersBackfillService.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Polygon.PolygonCorporateActionFetcher.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Polygon.PolygonHistoricalDataProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Polygon.PolygonMarketDataClient.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Polygon.PolygonSecurityMasterIngestProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Polygon.PolygonSymbolSearchProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Polygon.TradingParametersBackfillService.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Polygon.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Robinhood.RobinhoodBrokerageGateway.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Robinhood.RobinhoodHistoricalDataProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Robinhood.RobinhoodMarketDataClient.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Robinhood.RobinhoodSymbolSearchProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Robinhood.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.StockSharp.ConnectionState.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.StockSharp.ConnectorCapabilities.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.StockSharp.Converters.MessageConverter.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.StockSharp.Converters.SecurityConverter.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.StockSharp.Converters.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.StockSharp.ProviderHealthMetrics.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.StockSharp.StockSharpConnectorCapabilities.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.StockSharp.StockSharpConnectorFactory.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.StockSharp.StockSharpHistoricalDataProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.StockSharp.StockSharpMarketDataClient.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.StockSharp.StockSharpSymbolSearchProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.StockSharp.SubscriptionType.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.StockSharp.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Stooq.StooqHistoricalDataProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Stooq.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Synthetic.SyntheticHistoricalDataProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Synthetic.SyntheticMarketDataClient.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Synthetic.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Templates.TemplateBrokerageGateway.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Templates.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Tiingo.TiingoHistoricalDataProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.Tiingo.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.TwelveData.TwelveDataHistoricalDataProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.TwelveData.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.YahooFinance.YahooFinanceHistoricalDataProvider.yml
│   │   │   ├── Meridian.Infrastructure.Adapters.YahooFinance.yml
│   │   │   ├── Meridian.Infrastructure.Contracts.AdrImplementation.yml
│   │   │   ├── Meridian.Infrastructure.Contracts.AdrVerificationExtensions.yml
│   │   │   ├── Meridian.Infrastructure.Contracts.AttributeCredentialResolver.yml
│   │   │   ├── Meridian.Infrastructure.Contracts.ContractVerificationExtensions.yml
│   │   │   ├── Meridian.Infrastructure.Contracts.ContractVerificationHostedService.yml
│   │   │   ├── Meridian.Infrastructure.Contracts.ContractVerificationService.yml
│   │   │   ├── Meridian.Infrastructure.Contracts.ContractViolation.yml
│   │   │   ├── Meridian.Infrastructure.Contracts.CredentialSchema.yml
│   │   │   ├── Meridian.Infrastructure.Contracts.CredentialSchemaRegistry.yml
│   │   │   ├── Meridian.Infrastructure.Contracts.DocumentedContractAttribute.yml
│   │   │   ├── Meridian.Infrastructure.Contracts.ICredentialContext.yml
│   │   │   ├── Meridian.Infrastructure.Contracts.ImplementsAdrAttribute.yml
│   │   │   ├── Meridian.Infrastructure.Contracts.RequiresCredentialAttribute.yml
│   │   │   ├── Meridian.Infrastructure.Contracts.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.CppTraderServiceCollectionExtensions.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Diagnostics.CppTraderSessionDiagnostic.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Diagnostics.CppTraderSessionDiagnosticsService.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Diagnostics.CppTraderStatusService.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Diagnostics.ICppTraderSessionDiagnosticsService.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Diagnostics.ICppTraderStatusService.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Diagnostics.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Execution.CppTraderLiveFeedAdapter.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Execution.CppTraderOrderGateway.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Execution.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Host.CppTraderHostManager.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Host.ICppTraderHostManager.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Host.ICppTraderSessionClient.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Host.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Options.CppTraderFeatureOptions.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Options.CppTraderOptions.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Options.CppTraderSymbolSpecification.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Options.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.AcceptedEvent.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.BookSnapshotEvent.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.CancelledEvent.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.CancelOrderRequest.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.CancelOrderResponse.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.CppTraderBookLevel.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.CppTraderBookSnapshot.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.CppTraderEnvelope.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.CppTraderProtocolNames.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.CppTraderSessionKind.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.CreateSessionRequest.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.CreateSessionResponse.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.ExecutionEvent.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.FaultEvent.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.GetSnapshotRequest.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.GetSnapshotResponse.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.HeartbeatRequest.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.HeartbeatResponse.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.HostHealthSnapshot.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.RegisterSymbolRequest.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.RegisterSymbolResponse.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.RejectedEvent.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.SessionClosedEvent.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.SubmitOrderRequest.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.SubmitOrderResponse.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.TradePrintEvent.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Protocol.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Providers.CppTraderItchIngestionService.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Providers.CppTraderMarketDataClient.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Providers.ICppTraderItchIngestionService.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Providers.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Replay.CppTraderReplayService.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Replay.ICppTraderReplayService.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Replay.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Symbols.CppTraderSymbolMapper.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Symbols.ICppTraderSymbolMapper.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Symbols.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Translation.CppTraderExecutionTranslator.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Translation.CppTraderSnapshotTranslator.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Translation.ICppTraderExecutionTranslator.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Translation.ICppTraderSnapshotTranslator.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.Translation.yml
│   │   │   ├── Meridian.Infrastructure.CppTrader.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.AssetClass.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.CapabilityConfig.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.ConnectionConfig.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.CredentialConfig.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.CredentialValidationResult.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.DataSourceAttribute.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.DataSourceAttributeExtensions.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.DataSourceBase.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.DataSourceCapabilities.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.DataSourceCapabilityInfo.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.DataSourceCategory.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.DataSourceConfigurationExtensions.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.DataSourceError.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.DataSourceHealth.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.DataSourceHealthChanged.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.DataSourceMetadata.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.DataSourceOptions.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.DataSourceRegistry.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.DataSourceStatus.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.DataSourceType.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.DefaultsConfig.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.DividendInfo.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.DividendType.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.FailoverConfig.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.FallbackOptions.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.FallbackStrategy.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.HealthCheckConfig.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.HealthCheckOptions.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.ICorporateActionSource.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.IDailyBarSource.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.IDataSource.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.IDepthSource.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.IHistoricalDataSource.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.IIntradayBarSource.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.IntradayBar.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.IQuoteSource.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.IRealtimeDataSource.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.ITradeSource.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.PluginInstanceConfig.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.PluginSystemConfig.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.RateLimitConfig.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.RateLimitOptions.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.RateLimitState.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.RealtimeDepthUpdate.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.RealtimeQuote.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.RealtimeTrade.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.RetryPolicyConfig.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.RetryPolicyOptions.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.SourceConfig.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.SplitInfo.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.UnifiedDataSourcesConfig.yml
│   │   │   ├── Meridian.Infrastructure.DataSources.yml
│   │   │   ├── Meridian.Infrastructure.Etl.CsvPartnerFileParser.yml
│   │   │   ├── Meridian.Infrastructure.Etl.ISftpFilePublisher.yml
│   │   │   ├── Meridian.Infrastructure.Etl.LocalFileSourceReader.yml
│   │   │   ├── Meridian.Infrastructure.Etl.Sftp.ISftpClient.yml
│   │   │   ├── Meridian.Infrastructure.Etl.Sftp.ISftpClientFactory.yml
│   │   │   ├── Meridian.Infrastructure.Etl.Sftp.ISftpFileEntry.yml
│   │   │   ├── Meridian.Infrastructure.Etl.Sftp.SftpClientFactory.yml
│   │   │   ├── Meridian.Infrastructure.Etl.Sftp.yml
│   │   │   ├── Meridian.Infrastructure.Etl.SftpFilePublisher.yml
│   │   │   ├── Meridian.Infrastructure.Etl.SftpFileSourceReader.yml
│   │   │   ├── Meridian.Infrastructure.Etl.yml
│   │   │   ├── Meridian.Infrastructure.Http.HttpClientConfiguration.yml
│   │   │   ├── Meridian.Infrastructure.Http.HttpClientFactoryProvider.yml
│   │   │   ├── Meridian.Infrastructure.Http.HttpClientNames.yml
│   │   │   ├── Meridian.Infrastructure.Http.ProviderHttpUtilities.yml
│   │   │   ├── Meridian.Infrastructure.Http.SharedResiliencePolicies.yml
│   │   │   ├── Meridian.Infrastructure.Http.yml
│   │   │   ├── Meridian.Infrastructure.IMarketDataClient.yml
│   │   │   ├── Meridian.Infrastructure.NoOpMarketDataClient.yml
│   │   │   ├── Meridian.Infrastructure.Resilience.HttpHandleResult.yml
│   │   │   ├── Meridian.Infrastructure.Resilience.HttpResiliencePolicy.yml
│   │   │   ├── Meridian.Infrastructure.Resilience.RateLimitEventArgs.yml
│   │   │   ├── Meridian.Infrastructure.Resilience.ReconnectionGap.yml
│   │   │   ├── Meridian.Infrastructure.Resilience.WebSocketConnectionConfig.yml
│   │   │   ├── Meridian.Infrastructure.Resilience.WebSocketConnectionManager.yml
│   │   │   ├── Meridian.Infrastructure.Resilience.WebSocketHeartbeat.yml
│   │   │   ├── Meridian.Infrastructure.Resilience.WebSocketResiliencePolicy.yml
│   │   │   ├── Meridian.Infrastructure.Resilience.yml
│   │   │   ├── Meridian.Infrastructure.Shared.ExpiringSymbolStateStore-1.yml
│   │   │   ├── Meridian.Infrastructure.Shared.ISymbolStateStore-1.yml
│   │   │   ├── Meridian.Infrastructure.Shared.ReconnectionEvent.yml
│   │   │   ├── Meridian.Infrastructure.Shared.Subscription.yml
│   │   │   ├── Meridian.Infrastructure.Shared.SubscriptionManager.yml
│   │   │   ├── Meridian.Infrastructure.Shared.SubscriptionSnapshot.yml
│   │   │   ├── Meridian.Infrastructure.Shared.SymbolStateStore-1.yml
│   │   │   ├── Meridian.Infrastructure.Shared.TaskSafetyExtensions.yml
│   │   │   ├── Meridian.Infrastructure.Shared.WebSocketReconnectionHelper.yml
│   │   │   ├── Meridian.Infrastructure.Shared.yml
│   │   │   ├── Meridian.Infrastructure.Utilities.CredentialValidator.yml
│   │   │   ├── Meridian.Infrastructure.Utilities.HttpResponseHandler.yml
│   │   │   ├── Meridian.Infrastructure.Utilities.HttpResponseResult.yml
│   │   │   ├── Meridian.Infrastructure.Utilities.JsonElementExtensions.yml
│   │   │   ├── Meridian.Infrastructure.Utilities.RateLimitEventArgs.yml
│   │   │   ├── Meridian.Infrastructure.Utilities.SymbolNormalization.yml
│   │   │   ├── Meridian.Infrastructure.Utilities.yml
│   │   │   ├── Meridian.Infrastructure.yml
│   │   │   ├── Meridian.Integrations.Lean.MeridianDataProvider.yml
│   │   │   ├── Meridian.Integrations.Lean.MeridianQuoteData.yml
│   │   │   ├── Meridian.Integrations.Lean.MeridianTradeData.yml
│   │   │   ├── Meridian.Integrations.Lean.SampleLeanAlgorithm.yml
│   │   │   ├── Meridian.Integrations.Lean.yml
│   │   │   ├── Meridian.Ledger.FundLedgerBook.yml
│   │   │   ├── Meridian.Ledger.FundLedgerSnapshot.yml
│   │   │   ├── Meridian.Ledger.IReadOnlyLedger.yml
│   │   │   ├── Meridian.Ledger.JournalEntry.yml
│   │   │   ├── Meridian.Ledger.JournalEntryMetadata.yml
│   │   │   ├── Meridian.Ledger.Ledger.yml
│   │   │   ├── Meridian.Ledger.LedgerAccount.yml
│   │   │   ├── Meridian.Ledger.LedgerAccounts.yml
│   │   │   ├── Meridian.Ledger.LedgerAccountSummary.yml
│   │   │   ├── Meridian.Ledger.LedgerAccountType.yml
│   │   │   ├── Meridian.Ledger.LedgerBalancePoint.yml
│   │   │   ├── Meridian.Ledger.LedgerBookKey.yml
│   │   │   ├── Meridian.Ledger.LedgerEntry.yml
│   │   │   ├── Meridian.Ledger.LedgerQuery.yml
│   │   │   ├── Meridian.Ledger.LedgerSnapshot.yml
│   │   │   ├── Meridian.Ledger.LedgerValidationException.yml
│   │   │   ├── Meridian.Ledger.LedgerViewKind.yml
│   │   │   ├── Meridian.Ledger.ProjectLedgerBook.yml
│   │   │   ├── Meridian.Ledger.yml
│   │   │   ├── Meridian.Mcp.Prompts.CodeReviewPrompts.yml
│   │   │   ├── Meridian.Mcp.Prompts.ProviderPrompts.yml
│   │   │   ├── Meridian.Mcp.Prompts.TestWriterPrompts.yml
│   │   │   ├── Meridian.Mcp.Prompts.yml
│   │   │   ├── Meridian.Mcp.Resources.AdrResources.yml
│   │   │   ├── Meridian.Mcp.Resources.ConventionResources.yml
│   │   │   ├── Meridian.Mcp.Resources.TemplateResources.yml
│   │   │   ├── Meridian.Mcp.Resources.yml
│   │   │   ├── Meridian.Mcp.Services.RepoPathService.yml
│   │   │   ├── Meridian.Mcp.Services.yml
│   │   │   ├── Meridian.Mcp.Tools.AdrTools.yml
│   │   │   ├── Meridian.Mcp.Tools.AuditTools.yml
│   │   │   ├── Meridian.Mcp.Tools.ConventionTools.yml
│   │   │   ├── Meridian.Mcp.Tools.KnownErrorTools.yml
│   │   │   ├── Meridian.Mcp.Tools.ProviderTools.yml
│   │   │   ├── Meridian.Mcp.Tools.yml
│   │   │   ├── Meridian.McpServer.Navigation.RepoNavigationCatalog.yml
│   │   │   ├── Meridian.McpServer.Navigation.RepoNavigationData.yml
│   │   │   ├── Meridian.McpServer.Navigation.RepoNavigationDependency.yml
│   │   │   ├── Meridian.McpServer.Navigation.RepoNavigationDocument.yml
│   │   │   ├── Meridian.McpServer.Navigation.RepoNavigationProject.yml
│   │   │   ├── Meridian.McpServer.Navigation.RepoNavigationRoute.yml
│   │   │   ├── Meridian.McpServer.Navigation.RepoNavigationRouteSymbol.yml
│   │   │   ├── Meridian.McpServer.Navigation.RepoNavigationSubsystem.yml
│   │   │   ├── Meridian.McpServer.Navigation.RepoNavigationSymbol.yml
│   │   │   ├── Meridian.McpServer.Navigation.yml
│   │   │   ├── Meridian.McpServer.Prompts.MarketDataPrompts.yml
│   │   │   ├── Meridian.McpServer.Prompts.yml
│   │   │   ├── Meridian.McpServer.Resources.MarketDataResources.yml
│   │   │   ├── Meridian.McpServer.Resources.RepoNavigationResources.yml
│   │   │   ├── Meridian.McpServer.Resources.yml
│   │   │   ├── Meridian.McpServer.Tools.BackfillTools.yml
│   │   │   ├── Meridian.McpServer.Tools.ProviderTools.yml
│   │   │   ├── Meridian.McpServer.Tools.RepoNavigationTools.yml
│   │   │   ├── Meridian.McpServer.Tools.StorageTools.yml
│   │   │   ├── Meridian.McpServer.Tools.SymbolTools.yml
│   │   │   ├── Meridian.McpServer.Tools.yml
│   │   │   ├── Meridian.Program.yml
│   │   │   ├── Meridian.ProviderSdk.ICapabilityRouter.yml
│   │   │   ├── Meridian.ProviderSdk.IHistoricalBarWriter.yml
│   │   │   ├── Meridian.ProviderSdk.IProviderCertificationRunner.yml
│   │   │   ├── Meridian.ProviderSdk.IProviderConnectionHealthSource.yml
│   │   │   ├── Meridian.ProviderSdk.IProviderFamilyAdapter.yml
│   │   │   ├── Meridian.ProviderSdk.ProviderBindingTarget.yml
│   │   │   ├── Meridian.ProviderSdk.ProviderCapabilityDescriptor.yml
│   │   │   ├── Meridian.ProviderSdk.ProviderCapabilityKind.yml
│   │   │   ├── Meridian.ProviderSdk.ProviderCertificationRunResult.yml
│   │   │   ├── Meridian.ProviderSdk.ProviderConnectionHealthSnapshot.yml
│   │   │   ├── Meridian.ProviderSdk.ProviderConnectionId.yml
│   │   │   ├── Meridian.ProviderSdk.ProviderConnectionMode.yml
│   │   │   ├── Meridian.ProviderSdk.ProviderConnectionScope.yml
│   │   │   ├── Meridian.ProviderSdk.ProviderConnectionTestResult.yml
│   │   │   ├── Meridian.ProviderSdk.ProviderConnectionType.yml
│   │   │   ├── Meridian.ProviderSdk.ProviderFamilyAdapterExtensions.yml
│   │   │   ├── Meridian.ProviderSdk.ProviderRouteContext.yml
│   │   │   ├── Meridian.ProviderSdk.ProviderRouteDecision.yml
│   │   │   ├── Meridian.ProviderSdk.ProviderRouteResult.yml
│   │   │   ├── Meridian.ProviderSdk.ProviderSafetyMode.yml
│   │   │   ├── Meridian.ProviderSdk.ProviderSafetyPolicy.yml
│   │   │   ├── Meridian.ProviderSdk.ProviderTrustSnapshot.yml
│   │   │   ├── Meridian.ProviderSdk.yml
│   │   │   ├── Meridian.Risk.CompositeRiskValidator.yml
│   │   │   ├── Meridian.Risk.IRiskRule.yml
│   │   │   ├── Meridian.Risk.Rules.DrawdownCircuitBreaker.yml
│   │   │   ├── Meridian.Risk.Rules.OrderRateThrottle.yml
│   │   │   ├── Meridian.Risk.Rules.PositionLimitRule.yml
│   │   │   ├── Meridian.Risk.Rules.yml
│   │   │   ├── Meridian.Risk.yml
│   │   │   ├── Meridian.Storage.Archival.ArchivalStorageOptions.yml
│   │   │   ├── Meridian.Storage.Archival.ArchivalStorageService.yml
│   │   │   ├── Meridian.Storage.Archival.ArchivalStorageStats.yml
│   │   │   ├── Meridian.Storage.Archival.AtomicFileWriter.yml
│   │   │   ├── Meridian.Storage.Archival.CompressionBenchmarkResult.yml
│   │   │   ├── Meridian.Storage.Archival.CompressionCodec.yml
│   │   │   ├── Meridian.Storage.Archival.CompressionContext.yml
│   │   │   ├── Meridian.Storage.Archival.CompressionPriority.yml
│   │   │   ├── Meridian.Storage.Archival.CompressionProfile.yml
│   │   │   ├── Meridian.Storage.Archival.CompressionProfileManager.yml
│   │   │   ├── Meridian.Storage.Archival.CompressionResult.yml
│   │   │   ├── Meridian.Storage.Archival.FieldConstraints.yml
│   │   │   ├── Meridian.Storage.Archival.MigrationResult.yml
│   │   │   ├── Meridian.Storage.Archival.SchemaDefinition.yml
│   │   │   ├── Meridian.Storage.Archival.SchemaField.yml
│   │   │   ├── Meridian.Storage.Archival.SchemaFieldType.yml
│   │   │   ├── Meridian.Storage.Archival.SchemaMigration.yml
│   │   │   ├── Meridian.Storage.Archival.SchemaRegistry.yml
│   │   │   ├── Meridian.Storage.Archival.SchemaRegistryEntry.yml
│   │   │   ├── Meridian.Storage.Archival.SchemaValidationResult.yml
│   │   │   ├── Meridian.Storage.Archival.SchemaVersionManager.yml
│   │   │   ├── Meridian.Storage.Archival.StorageTier.yml
│   │   │   ├── Meridian.Storage.Archival.WalCorruptionMode.yml
│   │   │   ├── Meridian.Storage.Archival.WalOptions.yml
│   │   │   ├── Meridian.Storage.Archival.WalRecord.yml
│   │   │   ├── Meridian.Storage.Archival.WalRepairResult.yml
│   │   │   ├── Meridian.Storage.Archival.WalSyncMode.yml
│   │   │   ├── Meridian.Storage.Archival.WriteAheadLog.yml
│   │   │   ├── Meridian.Storage.Archival.yml
│   │   │   ├── Meridian.Storage.ArchivePolicyConfig.yml
│   │   │   ├── Meridian.Storage.ArchiveReason.yml
│   │   │   ├── Meridian.Storage.CompressionCodec.yml
│   │   │   ├── Meridian.Storage.ConflictStrategy.yml
│   │   │   ├── Meridian.Storage.DataClassification.yml
│   │   │   ├── Meridian.Storage.DatePartition.yml
│   │   │   ├── Meridian.Storage.DirectLending.DirectLendingCashTransactionWrite.yml
│   │   │   ├── Meridian.Storage.DirectLending.DirectLendingEventWriteMetadata.yml
│   │   │   ├── Meridian.Storage.DirectLending.DirectLendingFeeBalanceWrite.yml
│   │   │   ├── Meridian.Storage.DirectLending.DirectLendingMigrationRunner.yml
│   │   │   ├── Meridian.Storage.DirectLending.DirectLendingOutboxMessage.yml
│   │   │   ├── Meridian.Storage.DirectLending.DirectLendingOutboxMessageWrite.yml
│   │   │   ├── Meridian.Storage.DirectLending.DirectLendingPaymentAllocationWrite.yml
│   │   │   ├── Meridian.Storage.DirectLending.DirectLendingPersistenceBatch.yml
│   │   │   ├── Meridian.Storage.DirectLending.IDirectLendingOperationsStore.yml
│   │   │   ├── Meridian.Storage.DirectLending.IDirectLendingStateStore.yml
│   │   │   ├── Meridian.Storage.DirectLending.PersistedDirectLendingState.yml
│   │   │   ├── Meridian.Storage.DirectLending.PostgresDirectLendingStateStore.yml
│   │   │   ├── Meridian.Storage.DirectLending.yml
│   │   │   ├── Meridian.Storage.DynamicQuotaConfig.yml
│   │   │   ├── Meridian.Storage.Etl.EtlAuditStore.yml
│   │   │   ├── Meridian.Storage.Etl.EtlRejectSink.yml
│   │   │   ├── Meridian.Storage.Etl.EtlStagingStore.yml
│   │   │   ├── Meridian.Storage.Etl.yml
│   │   │   ├── Meridian.Storage.Export.AggregationSettings.yml
│   │   │   ├── Meridian.Storage.Export.AnalysisExportService.yml
│   │   │   ├── Meridian.Storage.Export.AnalysisQualityReport.yml
│   │   │   ├── Meridian.Storage.Export.AnalysisQualityReportGenerator.yml
│   │   │   ├── Meridian.Storage.Export.AnalysisRecommendation.yml
│   │   │   ├── Meridian.Storage.Export.CompressionSettings.yml
│   │   │   ├── Meridian.Storage.Export.CompressionType.yml
│   │   │   ├── Meridian.Storage.Export.DataGap.yml
│   │   │   ├── Meridian.Storage.Export.DataOutlier.yml
│   │   │   ├── Meridian.Storage.Export.DescriptiveStats.yml
│   │   │   ├── Meridian.Storage.Export.ExportDateRange.yml
│   │   │   ├── Meridian.Storage.Export.ExportedFile.yml
│   │   │   ├── Meridian.Storage.Export.ExportFileVerificationResult.yml
│   │   │   ├── Meridian.Storage.Export.ExportFormat.yml
│   │   │   ├── Meridian.Storage.Export.ExportPreviewResult.yml
│   │   │   ├── Meridian.Storage.Export.ExportProfile.yml
│   │   │   ├── Meridian.Storage.Export.ExportQualitySummary.yml
│   │   │   ├── Meridian.Storage.Export.ExportRequest.yml
│   │   │   ├── Meridian.Storage.Export.ExportResult.yml
│   │   │   ├── Meridian.Storage.Export.ExportValidationIssue.yml
│   │   │   ├── Meridian.Storage.Export.ExportValidationResult.yml
│   │   │   ├── Meridian.Storage.Export.ExportValidationRulesRequest.yml
│   │   │   ├── Meridian.Storage.Export.ExportValidationSeverity.yml
│   │   │   ├── Meridian.Storage.Export.ExportValidator.yml
│   │   │   ├── Meridian.Storage.Export.ExportVerificationResult.yml
│   │   │   ├── Meridian.Storage.Export.ExportVerifier.yml
│   │   │   ├── Meridian.Storage.Export.FeatureSettings.yml
│   │   │   ├── Meridian.Storage.Export.FileQualityAnalysis.yml
│   │   │   ├── Meridian.Storage.Export.GapHandling.yml
│   │   │   ├── Meridian.Storage.Export.GapType.yml
│   │   │   ├── Meridian.Storage.Export.IssueSeverity.yml
│   │   │   ├── Meridian.Storage.Export.NormalizationType.yml
│   │   │   ├── Meridian.Storage.Export.PriceAggregation.yml
│   │   │   ├── Meridian.Storage.Export.QualityIssue.yml
│   │   │   ├── Meridian.Storage.Export.ReportFormat.yml
│   │   │   ├── Meridian.Storage.Export.SessionFilter.yml
│   │   │   ├── Meridian.Storage.Export.TimestampFormat.yml
│   │   │   ├── Meridian.Storage.Export.TimestampSettings.yml
│   │   │   ├── Meridian.Storage.Export.TimeStats.yml
│   │   │   ├── Meridian.Storage.Export.VolumeAggregation.yml
│   │   │   ├── Meridian.Storage.Export.yml
│   │   │   ├── Meridian.Storage.FileNamingConvention.yml
│   │   │   ├── Meridian.Storage.FundAccounts.IFundAccountStore.yml
│   │   │   ├── Meridian.Storage.FundAccounts.yml
│   │   │   ├── Meridian.Storage.Interfaces.CatalogExportFormat.yml
│   │   │   ├── Meridian.Storage.Interfaces.CatalogRebuildOptions.yml
│   │   │   ├── Meridian.Storage.Interfaces.CatalogRebuildProgress.yml
│   │   │   ├── Meridian.Storage.Interfaces.CatalogRebuildResult.yml
│   │   │   ├── Meridian.Storage.Interfaces.CatalogSearchCriteria.yml
│   │   │   ├── Meridian.Storage.Interfaces.CatalogVerificationOptions.yml
│   │   │   ├── Meridian.Storage.Interfaces.CatalogVerificationProgress.yml
│   │   │   ├── Meridian.Storage.Interfaces.CatalogVerificationResult.yml
│   │   │   ├── Meridian.Storage.Interfaces.IMarketDataStore.yml
│   │   │   ├── Meridian.Storage.Interfaces.ISourceRegistry.yml
│   │   │   ├── Meridian.Storage.Interfaces.IStorageCatalogService.yml
│   │   │   ├── Meridian.Storage.Interfaces.IStoragePolicy.yml
│   │   │   ├── Meridian.Storage.Interfaces.IStorageSink.yml
│   │   │   ├── Meridian.Storage.Interfaces.ISymbolRegistryService.yml
│   │   │   ├── Meridian.Storage.Interfaces.SourceInfo.yml
│   │   │   ├── Meridian.Storage.Interfaces.SourceType.yml
│   │   │   ├── Meridian.Storage.Interfaces.SymbolInfo.yml
│   │   │   ├── Meridian.Storage.Interfaces.yml
│   │   │   ├── Meridian.Storage.Maintenance.ArchiveMaintenanceSchedule.yml
│   │   │   ├── Meridian.Storage.Maintenance.ArchiveMaintenanceScheduleManager.yml
│   │   │   ├── Meridian.Storage.Maintenance.IArchiveMaintenanceScheduleManager.yml
│   │   │   ├── Meridian.Storage.Maintenance.IArchiveMaintenanceService.yml
│   │   │   ├── Meridian.Storage.Maintenance.IMaintenanceExecutionHistory.yml
│   │   │   ├── Meridian.Storage.Maintenance.MaintenanceExecution.yml
│   │   │   ├── Meridian.Storage.Maintenance.MaintenanceExecutionHistory.yml
│   │   │   ├── Meridian.Storage.Maintenance.MaintenanceExecutionStatus.yml
│   │   │   ├── Meridian.Storage.Maintenance.MaintenanceIssue.yml
│   │   │   ├── Meridian.Storage.Maintenance.MaintenancePriority.yml
│   │   │   ├── Meridian.Storage.Maintenance.MaintenanceResult.yml
│   │   │   ├── Meridian.Storage.Maintenance.MaintenanceSchedulePresets.yml
│   │   │   ├── Meridian.Storage.Maintenance.MaintenanceScheduleSummary.yml
│   │   │   ├── Meridian.Storage.Maintenance.MaintenanceServiceStatus.yml
│   │   │   ├── Meridian.Storage.Maintenance.MaintenanceStatistics.yml
│   │   │   ├── Meridian.Storage.Maintenance.MaintenanceTaskOptions.yml
│   │   │   ├── Meridian.Storage.Maintenance.MaintenanceTaskType.yml
│   │   │   ├── Meridian.Storage.Maintenance.ScheduledArchiveMaintenanceService.yml
│   │   │   ├── Meridian.Storage.Maintenance.ScheduleExecutionSummary.yml
│   │   │   ├── Meridian.Storage.Maintenance.yml
│   │   │   ├── Meridian.Storage.Packaging.ImportResult.yml
│   │   │   ├── Meridian.Storage.Packaging.ImportScriptTarget.yml
│   │   │   ├── Meridian.Storage.Packaging.PackageCompressionLevel.yml
│   │   │   ├── Meridian.Storage.Packaging.PackageContents.yml
│   │   │   ├── Meridian.Storage.Packaging.PackageDataFormat.yml
│   │   │   ├── Meridian.Storage.Packaging.PackageDateRange.yml
│   │   │   ├── Meridian.Storage.Packaging.PackageFileEntry.yml
│   │   │   ├── Meridian.Storage.Packaging.PackageFormat.yml
│   │   │   ├── Meridian.Storage.Packaging.PackageLayout.yml
│   │   │   ├── Meridian.Storage.Packaging.PackageManifest.yml
│   │   │   ├── Meridian.Storage.Packaging.PackageOptions.yml
│   │   │   ├── Meridian.Storage.Packaging.PackageProgress.yml
│   │   │   ├── Meridian.Storage.Packaging.PackageQualityMetrics.yml
│   │   │   ├── Meridian.Storage.Packaging.PackageResult.yml
│   │   │   ├── Meridian.Storage.Packaging.PackageSchema.yml
│   │   │   ├── Meridian.Storage.Packaging.PackageSchemaField.yml
│   │   │   ├── Meridian.Storage.Packaging.PackageStage.yml
│   │   │   ├── Meridian.Storage.Packaging.PackageValidationResult.yml
│   │   │   ├── Meridian.Storage.Packaging.PortableDataPackager.yml
│   │   │   ├── Meridian.Storage.Packaging.SupplementaryFileInfo.yml
│   │   │   ├── Meridian.Storage.Packaging.ValidationError.yml
│   │   │   ├── Meridian.Storage.Packaging.yml
│   │   │   ├── Meridian.Storage.PartitionDimension.yml
│   │   │   ├── Meridian.Storage.PartitionStrategy.yml
│   │   │   ├── Meridian.Storage.Policies.JsonlStoragePolicy.yml
│   │   │   ├── Meridian.Storage.Policies.ParsedPathMetadata.yml
│   │   │   ├── Meridian.Storage.Policies.yml
│   │   │   ├── Meridian.Storage.QuotaEnforcementPolicy.yml
│   │   │   ├── Meridian.Storage.QuotaOptions.yml
│   │   │   ├── Meridian.Storage.Replay.FileStatistics.yml
│   │   │   ├── Meridian.Storage.Replay.JsonlReplayer.yml
│   │   │   ├── Meridian.Storage.Replay.MemoryMappedJsonlReader.yml
│   │   │   ├── Meridian.Storage.Replay.MemoryMappedReaderOptions.yml
│   │   │   ├── Meridian.Storage.Replay.yml
│   │   │   ├── Meridian.Storage.SecurityMaster.ISecurityMasterEventStore.yml
│   │   │   ├── Meridian.Storage.SecurityMaster.ISecurityMasterSnapshotStore.yml
│   │   │   ├── Meridian.Storage.SecurityMaster.ISecurityMasterStore.yml
│   │   │   ├── Meridian.Storage.SecurityMaster.PostgresSecurityMasterEventStore.yml
│   │   │   ├── Meridian.Storage.SecurityMaster.PostgresSecurityMasterSnapshotStore.yml
│   │   │   ├── Meridian.Storage.SecurityMaster.PostgresSecurityMasterStore.yml
│   │   │   ├── Meridian.Storage.SecurityMaster.SecurityMasterDbMapper.yml
│   │   │   ├── Meridian.Storage.SecurityMaster.SecurityMasterMigrationRunner.yml
│   │   │   ├── Meridian.Storage.SecurityMaster.SecurityMasterProjectionCache.yml
│   │   │   ├── Meridian.Storage.SecurityMaster.yml
│   │   │   ├── Meridian.Storage.Services.AuditChainService.yml
│   │   │   ├── Meridian.Storage.Services.AuditChainVerifyResult.yml
│   │   │   ├── Meridian.Storage.Services.BestOfBreedResult.yml
│   │   │   ├── Meridian.Storage.Services.ConsolidatedDataset.yml
│   │   │   ├── Meridian.Storage.Services.ConsolidationOptions.yml
│   │   │   ├── Meridian.Storage.Services.ConversionSummary.yml
│   │   │   ├── Meridian.Storage.Services.DataCatalog.yml
│   │   │   ├── Meridian.Storage.Services.DataInsight.yml
│   │   │   ├── Meridian.Storage.Services.DataLineageService.yml
│   │   │   ├── Meridian.Storage.Services.DataQualityReport.yml
│   │   │   ├── Meridian.Storage.Services.DataQualityScore.yml
│   │   │   ├── Meridian.Storage.Services.DataQualityScoringReport.yml
│   │   │   ├── Meridian.Storage.Services.DataQualityScoringService.yml
│   │   │   ├── Meridian.Storage.Services.DataQualityService.yml
│   │   │   ├── Meridian.Storage.Services.DateIndex.yml
│   │   │   ├── Meridian.Storage.Services.DateRange.yml
│   │   │   ├── Meridian.Storage.Services.DefragOptions.yml
│   │   │   ├── Meridian.Storage.Services.DefragResult.yml
│   │   │   ├── Meridian.Storage.Services.DiscoveryQuery.yml
│   │   │   ├── Meridian.Storage.Services.EventBuffer-1.yml
│   │   │   ├── Meridian.Storage.Services.EventSearchQuery.yml
│   │   │   ├── Meridian.Storage.Services.EventSearchResult.yml
│   │   │   ├── Meridian.Storage.Services.FacetedSearchQuery.yml
│   │   │   ├── Meridian.Storage.Services.FacetedSearchResult.yml
│   │   │   ├── Meridian.Storage.Services.FileMaintenanceService.yml
│   │   │   ├── Meridian.Storage.Services.FileMetadata.yml
│   │   │   ├── Meridian.Storage.Services.FileMetadataRecord.yml
│   │   │   ├── Meridian.Storage.Services.FileMigrationResult.yml
│   │   │   ├── Meridian.Storage.Services.FilePermissionsDiagnostic.yml
│   │   │   ├── Meridian.Storage.Services.FilePermissionsOptions.yml
│   │   │   ├── Meridian.Storage.Services.FilePermissionsResult.yml
│   │   │   ├── Meridian.Storage.Services.FilePermissionsService.yml
│   │   │   ├── Meridian.Storage.Services.FileSearchQuery.yml
│   │   │   ├── Meridian.Storage.Services.FileSearchResult.yml
│   │   │   ├── Meridian.Storage.Services.HealthCheckOptions.yml
│   │   │   ├── Meridian.Storage.Services.HealthIssue.yml
│   │   │   ├── Meridian.Storage.Services.HealthReport.yml
│   │   │   ├── Meridian.Storage.Services.HealthStatistics.yml
│   │   │   ├── Meridian.Storage.Services.HealthSummary.yml
│   │   │   ├── Meridian.Storage.Services.IAuditChainService.yml
│   │   │   ├── Meridian.Storage.Services.IDataLineageService.yml
│   │   │   ├── Meridian.Storage.Services.IDataQualityScoringService.yml
│   │   │   ├── Meridian.Storage.Services.IDataQualityService.yml
│   │   │   ├── Meridian.Storage.Services.IFileMaintenanceService.yml
│   │   │   ├── Meridian.Storage.Services.ILifecyclePolicyEngine.yml
│   │   │   ├── Meridian.Storage.Services.IMaintenanceScheduler.yml
│   │   │   ├── Meridian.Storage.Services.IMetadataTagService.yml
│   │   │   ├── Meridian.Storage.Services.IndexUpdateType.yml
│   │   │   ├── Meridian.Storage.Services.IngestionRecord.yml
│   │   │   ├── Meridian.Storage.Services.InsightSeverity.yml
│   │   │   ├── Meridian.Storage.Services.IQuotaEnforcementService.yml
│   │   │   ├── Meridian.Storage.Services.IssueSeverity.yml
│   │   │   ├── Meridian.Storage.Services.IssueType.yml
│   │   │   ├── Meridian.Storage.Services.IStorageSearchService.yml
│   │   │   ├── Meridian.Storage.Services.ITierMigrationService.yml
│   │   │   ├── Meridian.Storage.Services.JobExecutionStatus.yml
│   │   │   ├── Meridian.Storage.Services.JobPriority.yml
│   │   │   ├── Meridian.Storage.Services.JobStatus.yml
│   │   │   ├── Meridian.Storage.Services.JsonlPositionSnapshotStore.yml
│   │   │   ├── Meridian.Storage.Services.LifecycleAction.yml
│   │   │   ├── Meridian.Storage.Services.LifecycleActionType.yml
│   │   │   ├── Meridian.Storage.Services.LifecycleEvaluationResult.yml
│   │   │   ├── Meridian.Storage.Services.LifecycleExecutionResult.yml
│   │   │   ├── Meridian.Storage.Services.LifecyclePolicyEngine.yml
│   │   │   ├── Meridian.Storage.Services.LifecycleState.yml
│   │   │   ├── Meridian.Storage.Services.LifecycleTierInfo.yml
│   │   │   ├── Meridian.Storage.Services.LineageEntry.yml
│   │   │   ├── Meridian.Storage.Services.LineageGraph.yml
│   │   │   ├── Meridian.Storage.Services.LineageReport.yml
│   │   │   ├── Meridian.Storage.Services.MaintenanceJob.yml
│   │   │   ├── Meridian.Storage.Services.MaintenanceScheduler.yml
│   │   │   ├── Meridian.Storage.Services.MaintenanceType.yml
│   │   │   ├── Meridian.Storage.Services.MaintenanceWindow.yml
│   │   │   ├── Meridian.Storage.Services.MarketEventBuffer.yml
│   │   │   ├── Meridian.Storage.Services.MetadataTagService.yml
│   │   │   ├── Meridian.Storage.Services.MigrationOptions.yml
│   │   │   ├── Meridian.Storage.Services.MigrationPlan.yml
│   │   │   ├── Meridian.Storage.Services.MigrationProgress.yml
│   │   │   ├── Meridian.Storage.Services.MigrationRecord.yml
│   │   │   ├── Meridian.Storage.Services.MigrationResult.yml
│   │   │   ├── Meridian.Storage.Services.OperationalScheduleConfig.yml
│   │   │   ├── Meridian.Storage.Services.OperationalState.yml
│   │   │   ├── Meridian.Storage.Services.OrphanedFile.yml
│   │   │   ├── Meridian.Storage.Services.OrphanReport.yml
│   │   │   ├── Meridian.Storage.Services.ParquetConversionService.yml
│   │   │   ├── Meridian.Storage.Services.PlannedMigrationAction.yml
│   │   │   ├── Meridian.Storage.Services.QualityAlert.yml
│   │   │   ├── Meridian.Storage.Services.QualityAssessment.yml
│   │   │   ├── Meridian.Storage.Services.QualityDimension.yml
│   │   │   ├── Meridian.Storage.Services.QualityIssue.yml
│   │   │   ├── Meridian.Storage.Services.QualityIssueSeverity.yml
│   │   │   ├── Meridian.Storage.Services.QualityReportOptions.yml
│   │   │   ├── Meridian.Storage.Services.QualityReportSummary.yml
│   │   │   ├── Meridian.Storage.Services.QualityTrend.yml
│   │   │   ├── Meridian.Storage.Services.QuotaCheckResult.yml
│   │   │   ├── Meridian.Storage.Services.QuotaEnforcementService.yml
│   │   │   ├── Meridian.Storage.Services.QuotaScanResult.yml
│   │   │   ├── Meridian.Storage.Services.QuotaStatusEntry.yml
│   │   │   ├── Meridian.Storage.Services.QuotaStatusReport.yml
│   │   │   ├── Meridian.Storage.Services.QuotaUsage.yml
│   │   │   ├── Meridian.Storage.Services.QuotaViolation.yml
│   │   │   ├── Meridian.Storage.Services.RebuildOptions.yml
│   │   │   ├── Meridian.Storage.Services.RepairOptions.yml
│   │   │   ├── Meridian.Storage.Services.RepairResult.yml
│   │   │   ├── Meridian.Storage.Services.RepairScope.yml
│   │   │   ├── Meridian.Storage.Services.RepairStrategy.yml
│   │   │   ├── Meridian.Storage.Services.ResourceLimits.yml
│   │   │   ├── Meridian.Storage.Services.ResourceRequirements.yml
│   │   │   ├── Meridian.Storage.Services.RetentionComplianceReport.yml
│   │   │   ├── Meridian.Storage.Services.RetentionComplianceReporter.yml
│   │   │   ├── Meridian.Storage.Services.RetentionEntry.yml
│   │   │   ├── Meridian.Storage.Services.RetentionStatus.yml
│   │   │   ├── Meridian.Storage.Services.RetentionViolation.yml
│   │   │   ├── Meridian.Storage.Services.ScheduleDecision.yml
│   │   │   ├── Meridian.Storage.Services.ScheduledJob.yml
│   │   │   ├── Meridian.Storage.Services.ScheduleOptions.yml
│   │   │   ├── Meridian.Storage.Services.ScheduleSlot.yml
│   │   │   ├── Meridian.Storage.Services.SearchResult-1.yml
│   │   │   ├── Meridian.Storage.Services.SortField.yml
│   │   │   ├── Meridian.Storage.Services.SourceCandidate.yml
│   │   │   ├── Meridian.Storage.Services.SourceRanking.yml
│   │   │   ├── Meridian.Storage.Services.SourceRegistry.yml
│   │   │   ├── Meridian.Storage.Services.SourceSelectionStrategy.yml
│   │   │   ├── Meridian.Storage.Services.StorageCatalogService.yml
│   │   │   ├── Meridian.Storage.Services.StorageChecksumService.yml
│   │   │   ├── Meridian.Storage.Services.StorageQuery.yml
│   │   │   ├── Meridian.Storage.Services.StorageQueryBuilder.yml
│   │   │   ├── Meridian.Storage.Services.StorageSearchService.yml
│   │   │   ├── Meridian.Storage.Services.StorageSymbolCatalogEntry.yml
│   │   │   ├── Meridian.Storage.Services.SymbolIndex.yml
│   │   │   ├── Meridian.Storage.Services.SymbolRegistryService.yml
│   │   │   ├── Meridian.Storage.Services.TierInfo.yml
│   │   │   ├── Meridian.Storage.Services.TierMigrationService.yml
│   │   │   ├── Meridian.Storage.Services.TierStatistics.yml
│   │   │   ├── Meridian.Storage.Services.TradingSession.yml
│   │   │   ├── Meridian.Storage.Services.TransformationRecord.yml
│   │   │   ├── Meridian.Storage.Services.yml
│   │   │   ├── Meridian.Storage.Sinks.CatalogSyncSink.yml
│   │   │   ├── Meridian.Storage.Sinks.CompositeSink.yml
│   │   │   ├── Meridian.Storage.Sinks.FailurePolicy.yml
│   │   │   ├── Meridian.Storage.Sinks.JsonlBatchOptions.yml
│   │   │   ├── Meridian.Storage.Sinks.JsonlStorageSink.yml
│   │   │   ├── Meridian.Storage.Sinks.ParquetStorageOptions.yml
│   │   │   ├── Meridian.Storage.Sinks.ParquetStorageSink.yml
│   │   │   ├── Meridian.Storage.Sinks.SinkHealth.yml
│   │   │   ├── Meridian.Storage.Sinks.SinkHealthState.yml
│   │   │   ├── Meridian.Storage.Sinks.yml
│   │   │   ├── Meridian.Storage.StorageOptions.yml
│   │   │   ├── Meridian.Storage.StoragePolicyConfig.yml
│   │   │   ├── Meridian.Storage.StorageProfile.yml
│   │   │   ├── Meridian.Storage.StorageProfilePreset.yml
│   │   │   ├── Meridian.Storage.StorageProfilePresets.yml
│   │   │   ├── Meridian.Storage.StorageQuota.yml
│   │   │   ├── Meridian.Storage.StorageSinkAttribute.yml
│   │   │   ├── Meridian.Storage.StorageSinkAttributeExtensions.yml
│   │   │   ├── Meridian.Storage.StorageSinkMetadata.yml
│   │   │   ├── Meridian.Storage.StorageSinkRegistry.yml
│   │   │   ├── Meridian.Storage.StorageTier.yml
│   │   │   ├── Meridian.Storage.Store.CompositeMarketDataStore.yml
│   │   │   ├── Meridian.Storage.Store.JsonlMarketDataStore.yml
│   │   │   ├── Meridian.Storage.Store.yml
│   │   │   ├── Meridian.Storage.TierConfig.yml
│   │   │   ├── Meridian.Storage.TieringOptions.yml
│   │   │   ├── Meridian.Storage.yml
│   │   │   ├── Meridian.Strategies.Interfaces.ILiveStrategy.yml
│   │   │   ├── Meridian.Strategies.Interfaces.IStrategyLifecycle.yml
│   │   │   ├── Meridian.Strategies.Interfaces.IStrategyRepository.yml
│   │   │   ├── Meridian.Strategies.Interfaces.yml
│   │   │   ├── Meridian.Strategies.Models.RunType.yml
│   │   │   ├── Meridian.Strategies.Models.StrategyRunEntry.yml
│   │   │   ├── Meridian.Strategies.Models.StrategyStatus.yml
│   │   │   ├── Meridian.Strategies.Models.yml
│   │   │   ├── Meridian.Strategies.Promotions.BacktestToLivePromoter.yml
│   │   │   ├── Meridian.Strategies.Promotions.PromotionCriteria.yml
│   │   │   ├── Meridian.Strategies.Promotions.StrategyPromotionRecord.yml
│   │   │   ├── Meridian.Strategies.Promotions.yml
│   │   │   ├── Meridian.Strategies.Services.AggregatedPosition.yml
│   │   │   ├── Meridian.Strategies.Services.AggregatePortfolioService.yml
│   │   │   ├── Meridian.Strategies.Services.CashFlowProjectionService.yml
│   │   │   ├── Meridian.Strategies.Services.CrossStrategyExposureReport.yml
│   │   │   ├── Meridian.Strategies.Services.IAggregatePortfolioService.yml
│   │   │   ├── Meridian.Strategies.Services.InMemoryReconciliationRunRepository.yml
│   │   │   ├── Meridian.Strategies.Services.IReconciliationRunRepository.yml
│   │   │   ├── Meridian.Strategies.Services.IReconciliationRunService.yml
│   │   │   ├── Meridian.Strategies.Services.ISecurityReferenceLookup.yml
│   │   │   ├── Meridian.Strategies.Services.LedgerReadService.yml
│   │   │   ├── Meridian.Strategies.Services.NetSymbolPosition.yml
│   │   │   ├── Meridian.Strategies.Services.PortfolioReadService.yml
│   │   │   ├── Meridian.Strategies.Services.PromotionApprovalRequest.yml
│   │   │   ├── Meridian.Strategies.Services.PromotionDecisionResult.yml
│   │   │   ├── Meridian.Strategies.Services.PromotionEvaluationResult.yml
│   │   │   ├── Meridian.Strategies.Services.PromotionRejectionRequest.yml
│   │   │   ├── Meridian.Strategies.Services.PromotionService.yml
│   │   │   ├── Meridian.Strategies.Services.ReconciliationProjectionService.yml
│   │   │   ├── Meridian.Strategies.Services.ReconciliationRunService.yml
│   │   │   ├── Meridian.Strategies.Services.RunPositionContribution.yml
│   │   │   ├── Meridian.Strategies.Services.StrategyLifecycleManager.yml
│   │   │   ├── Meridian.Strategies.Services.StrategyRunReadService.yml
│   │   │   ├── Meridian.Strategies.Services.yml
│   │   │   ├── Meridian.Strategies.Storage.StrategyRunStore.yml
│   │   │   ├── Meridian.Strategies.Storage.yml
│   │   │   ├── Meridian.Tools.DataValidator.GapInfo.yml
│   │   │   ├── Meridian.Tools.DataValidator.ValidationResult.yml
│   │   │   ├── Meridian.Tools.DataValidator.ValidationSummary.yml
│   │   │   ├── Meridian.Tools.DataValidator.yml
│   │   │   ├── Meridian.Tools.yml
│   │   │   ├── Meridian.Ui.Services.ActivityFeedService.yml
│   │   │   ├── Meridian.Ui.Services.ActivityItem.yml
│   │   │   ├── Meridian.Ui.Services.ActivityType.yml
│   │   │   ├── Meridian.Ui.Services.AddSymbolRequest.yml
│   │   │   ├── Meridian.Ui.Services.AdminMaintenanceServiceBase.yml
│   │   │   ├── Meridian.Ui.Services.AdvancedAnalyticsServiceBase.yml
│   │   │   ├── Meridian.Ui.Services.AggregationMethod.yml
│   │   │   ├── Meridian.Ui.Services.AggregationType.yml
│   │   │   ├── Meridian.Ui.Services.Alert.yml
│   │   │   ├── Meridian.Ui.Services.AlertEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.AlertGroup.yml
│   │   │   ├── Meridian.Ui.Services.AlertPlaybook.yml
│   │   │   ├── Meridian.Ui.Services.AlertService.yml
│   │   │   ├── Meridian.Ui.Services.AlertSeverity.yml
│   │   │   ├── Meridian.Ui.Services.AlertSummary.yml
│   │   │   ├── Meridian.Ui.Services.AlertSuppressionRule.yml
│   │   │   ├── Meridian.Ui.Services.AlgorithmInfo.yml
│   │   │   ├── Meridian.Ui.Services.AlgorithmListResponse.yml
│   │   │   ├── Meridian.Ui.Services.AlgorithmListResult.yml
│   │   │   ├── Meridian.Ui.Services.AlignmentInterval.yml
│   │   │   ├── Meridian.Ui.Services.AlignmentMetadata.yml
│   │   │   ├── Meridian.Ui.Services.AlignmentOptions.yml
│   │   │   ├── Meridian.Ui.Services.AlignmentPreset.yml
│   │   │   ├── Meridian.Ui.Services.AlignmentPreview.yml
│   │   │   ├── Meridian.Ui.Services.AlignmentPreviewResponse.yml
│   │   │   ├── Meridian.Ui.Services.AlignmentProgressEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.AlignmentResponse.yml
│   │   │   ├── Meridian.Ui.Services.AlignmentResult.yml
│   │   │   ├── Meridian.Ui.Services.AlignmentValidationResult.yml
│   │   │   ├── Meridian.Ui.Services.AllProvidersStatusResponse.yml
│   │   │   ├── Meridian.Ui.Services.AllProvidersStatusResult.yml
│   │   │   ├── Meridian.Ui.Services.AllProvidersTestResponse.yml
│   │   │   ├── Meridian.Ui.Services.AllProvidersTestResult.yml
│   │   │   ├── Meridian.Ui.Services.AnalysisExportWizardService.yml
│   │   │   ├── Meridian.Ui.Services.AnalyticsDataGap.yml
│   │   │   ├── Meridian.Ui.Services.AnalyticsGapRepairResult.yml
│   │   │   ├── Meridian.Ui.Services.AnalyticsQualityMetrics.yml
│   │   │   ├── Meridian.Ui.Services.AnalyticsSymbolInfo.yml
│   │   │   ├── Meridian.Ui.Services.AnalyticsSymbolQualityReport.yml
│   │   │   ├── Meridian.Ui.Services.AnalyticsSymbolsResponse.yml
│   │   │   ├── Meridian.Ui.Services.AnalyticsSymbolsResult.yml
│   │   │   ├── Meridian.Ui.Services.AnalyzedFile.yml
│   │   │   ├── Meridian.Ui.Services.AnomalyDetectionOptions.yml
│   │   │   ├── Meridian.Ui.Services.AnomalyDetectionResponse.yml
│   │   │   ├── Meridian.Ui.Services.AnomalyDetectionResult.yml
│   │   │   ├── Meridian.Ui.Services.AnomalySummary.yml
│   │   │   ├── Meridian.Ui.Services.ApiClientService.yml
│   │   │   ├── Meridian.Ui.Services.AppSettings.yml
│   │   │   ├── Meridian.Ui.Services.ArchiveBrowserService.yml
│   │   │   ├── Meridian.Ui.Services.ArchiveExportProgress.yml
│   │   │   ├── Meridian.Ui.Services.ArchiveExportResult.yml
│   │   │   ├── Meridian.Ui.Services.ArchiveFileInfo.yml
│   │   │   ├── Meridian.Ui.Services.ArchiveHealthService.yml
│   │   │   ├── Meridian.Ui.Services.ArchiveStats.yml
│   │   │   ├── Meridian.Ui.Services.ArchiveTree.yml
│   │   │   ├── Meridian.Ui.Services.BackfillableGap.yml
│   │   │   ├── Meridian.Ui.Services.BackfillApiService.yml
│   │   │   ├── Meridian.Ui.Services.BackfillCheckpoint.yml
│   │   │   ├── Meridian.Ui.Services.BackfillCheckpointService.yml
│   │   │   ├── Meridian.Ui.Services.BackfillCompletedEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.BackfillProgressEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.BackfillProviderConfigService.yml
│   │   │   ├── Meridian.Ui.Services.BackfillRecommendations.yml
│   │   │   ├── Meridian.Ui.Services.BackfillSchedule.yml
│   │   │   ├── Meridian.Ui.Services.BackfillScheduleCreateRequest.yml
│   │   │   ├── Meridian.Ui.Services.BackfillScheduleSummary.yml
│   │   │   ├── Meridian.Ui.Services.BackfillService.yml
│   │   │   ├── Meridian.Ui.Services.BacktestHistoryResponse.yml
│   │   │   ├── Meridian.Ui.Services.BacktestHistoryResult.yml
│   │   │   ├── Meridian.Ui.Services.BacktestOptions.yml
│   │   │   ├── Meridian.Ui.Services.BacktestStartResponse.yml
│   │   │   ├── Meridian.Ui.Services.BacktestStartResult.yml
│   │   │   ├── Meridian.Ui.Services.BacktestState.yml
│   │   │   ├── Meridian.Ui.Services.BacktestStatus.yml
│   │   │   ├── Meridian.Ui.Services.BacktestStatusChangedEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.BacktestSummary.yml
│   │   │   ├── Meridian.Ui.Services.BatchExportSchedulerService.yml
│   │   │   ├── Meridian.Ui.Services.BatchOperationResponse.yml
│   │   │   ├── Meridian.Ui.Services.BboQuote.yml
│   │   │   ├── Meridian.Ui.Services.BollingerBandsData.yml
│   │   │   ├── Meridian.Ui.Services.BrowserArchiveFileInfo.yml
│   │   │   ├── Meridian.Ui.Services.BulkSymbolOperationResponse.yml
│   │   │   ├── Meridian.Ui.Services.BulkSymbolOperationResult.yml
│   │   │   ├── Meridian.Ui.Services.BusinessImpact.yml
│   │   │   ├── Meridian.Ui.Services.CalendarDay.yml
│   │   │   ├── Meridian.Ui.Services.CalendarDayData.yml
│   │   │   ├── Meridian.Ui.Services.CalendarMonthData.yml
│   │   │   ├── Meridian.Ui.Services.CalendarYearData.yml
│   │   │   ├── Meridian.Ui.Services.Candlestick.yml
│   │   │   ├── Meridian.Ui.Services.CandlestickData.yml
│   │   │   ├── Meridian.Ui.Services.CatalogEntry.yml
│   │   │   ├── Meridian.Ui.Services.ChartingService.yml
│   │   │   ├── Meridian.Ui.Services.ChartTimeframe.yml
│   │   │   ├── Meridian.Ui.Services.CheckpointStatus.yml
│   │   │   ├── Meridian.Ui.Services.CheckResult.yml
│   │   │   ├── Meridian.Ui.Services.CheckSeverity.yml
│   │   │   ├── Meridian.Ui.Services.ChecksumMismatch.yml
│   │   │   ├── Meridian.Ui.Services.ChecksumVerificationResult.yml
│   │   │   ├── Meridian.Ui.Services.CleanupCandidate.yml
│   │   │   ├── Meridian.Ui.Services.CleanupFileInfo.yml
│   │   │   ├── Meridian.Ui.Services.CleanupOptions.yml
│   │   │   ├── Meridian.Ui.Services.CleanupPreviewResponse.yml
│   │   │   ├── Meridian.Ui.Services.CleanupPreviewResult.yml
│   │   │   ├── Meridian.Ui.Services.CleanupResult.yml
│   │   │   ├── Meridian.Ui.Services.CleanupResultResponse.yml
│   │   │   ├── Meridian.Ui.Services.CleanupStatus.yml
│   │   │   ├── Meridian.Ui.Services.Collections.BoundedObservableCollection-1.yml
│   │   │   ├── Meridian.Ui.Services.Collections.CircularBuffer-1.yml
│   │   │   ├── Meridian.Ui.Services.Collections.CircularBufferExtensions.yml
│   │   │   ├── Meridian.Ui.Services.Collections.yml
│   │   │   ├── Meridian.Ui.Services.CollectionSessionEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.CollectionSessionService.yml
│   │   │   ├── Meridian.Ui.Services.CommandEntry.yml
│   │   │   ├── Meridian.Ui.Services.CommandPaletteService.yml
│   │   │   ├── Meridian.Ui.Services.CompletenessAnalysisOptions.yml
│   │   │   ├── Meridian.Ui.Services.CompletenessAnalysisResponse.yml
│   │   │   ├── Meridian.Ui.Services.CompletenessAnalysisResult.yml
│   │   │   ├── Meridian.Ui.Services.CompletenessLevel.yml
│   │   │   ├── Meridian.Ui.Services.CompletenessReport.yml
│   │   │   ├── Meridian.Ui.Services.CompletenessStatus.yml
│   │   │   ├── Meridian.Ui.Services.CompletenessTrendData.yml
│   │   │   ├── Meridian.Ui.Services.CompletenessTrendPoint.yml
│   │   │   ├── Meridian.Ui.Services.ConfigIssue.yml
│   │   │   ├── Meridian.Ui.Services.ConfigItem.yml
│   │   │   ├── Meridian.Ui.Services.ConfigSection.yml
│   │   │   ├── Meridian.Ui.Services.ConfigService.yml
│   │   │   ├── Meridian.Ui.Services.ConfigStatusResponse.yml
│   │   │   ├── Meridian.Ui.Services.ConfigValidationResponse.yml
│   │   │   ├── Meridian.Ui.Services.ConnectionTestResult.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.AppTheme.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.ConnectionErrorCategory.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.ConnectionHealthEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.ConnectionSettings.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.ConnectionState.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.ConnectionStateChangedEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.ConnectionStateEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.DiagnosticValidationResult.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.IArchiveHealthService.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.IBackgroundTaskSchedulerService.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.IConfigService.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.ICredentialService.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.ILoggingService.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.IMessagingService.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.INotificationService.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.IOfflineTrackingPersistenceService.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.IPendingOperationsQueueService.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.IRefreshScheduler.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.ISchemaService.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.IStatusService.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.IThemeService.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.IWatchlistService.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.NavigationEntry.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.NavigationEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.ReconnectEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.ReconnectFailedEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.Contracts.yml
│   │   │   ├── Meridian.Ui.Services.CoverageMatrixData.yml
│   │   │   ├── Meridian.Ui.Services.CreateBackfillScheduleRequest.yml
│   │   │   ├── Meridian.Ui.Services.CreateMaintenanceScheduleRequest.yml
│   │   │   ├── Meridian.Ui.Services.CredentialExpirationEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.CredentialMetadataInfo.yml
│   │   │   ├── Meridian.Ui.Services.CredentialMetadataUpdate.yml
│   │   │   ├── Meridian.Ui.Services.CredentialService.yml
│   │   │   ├── Meridian.Ui.Services.CredentialValidationResponse.yml
│   │   │   ├── Meridian.Ui.Services.CredentialValidationResult.yml
│   │   │   ├── Meridian.Ui.Services.CredentialWithMetadata.yml
│   │   │   ├── Meridian.Ui.Services.CronValidationResult.yml
│   │   │   ├── Meridian.Ui.Services.CrossProviderComparisonOptions.yml
│   │   │   ├── Meridian.Ui.Services.CrossProviderComparisonResponse.yml
│   │   │   ├── Meridian.Ui.Services.CrossProviderComparisonResult.yml
│   │   │   ├── Meridian.Ui.Services.DailyCompleteness.yml
│   │   │   ├── Meridian.Ui.Services.DailySymbolDetail.yml
│   │   │   ├── Meridian.Ui.Services.DataAnomaly.yml
│   │   │   ├── Meridian.Ui.Services.DataCalendarService.yml
│   │   │   ├── Meridian.Ui.Services.DataCompletenessService.yml
│   │   │   ├── Meridian.Ui.Services.DataDictionaryEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.DataDiscrepancy.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.DataQualityAlertPresentation.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.DataQualityAnomalyPresentation.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.DataQualityApiClient.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.DataQualityDrilldownIssuePresentation.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.DataQualityGapPresentation.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.DataQualityHeatmapCellPresentation.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.DataQualityPresentationService.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.DataQualityPresentationSnapshot.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.DataQualityProviderComparisonItem.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.DataQualityProviderComparisonPresentation.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.DataQualityRefreshService.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.DataQualitySymbolDrilldownPresentation.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.DataQualitySymbolPresentation.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.DataQualityVisualTones.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.IDataQualityApiClient.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.IDataQualityPresentationService.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.IDataQualityRefreshService.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.QualityActionResponse.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.QualityAnomalyResponse.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.QualityAnomalyStatsResponse.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.QualityCompletenessStatsResponse.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.QualityDashboardMetricsResponse.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.QualityDashboardResponse.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.QualityGapResponse.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.QualityGapStatsResponse.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.QualityLatencyStatisticsResponse.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.QualityProviderComparisonEntryResponse.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.QualityProviderComparisonResponse.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.QualitySequenceStatsResponse.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.QualitySymbolHealthResponse.yml
│   │   │   ├── Meridian.Ui.Services.DataQuality.yml
│   │   │   ├── Meridian.Ui.Services.DataQualityIssue.yml
│   │   │   ├── Meridian.Ui.Services.DataQualityReportOptions.yml
│   │   │   ├── Meridian.Ui.Services.DataQualityReportResponse.yml
│   │   │   ├── Meridian.Ui.Services.DataQualityReportResult.yml
│   │   │   ├── Meridian.Ui.Services.DataSamplingService.yml
│   │   │   ├── Meridian.Ui.Services.DataStreamHealth.yml
│   │   │   ├── Meridian.Ui.Services.DataSyncOptions.yml
│   │   │   ├── Meridian.Ui.Services.DataSyncResponse.yml
│   │   │   ├── Meridian.Ui.Services.DataSyncResult.yml
│   │   │   ├── Meridian.Ui.Services.DataSyncStatus.yml
│   │   │   ├── Meridian.Ui.Services.DateRange.yml
│   │   │   ├── Meridian.Ui.Services.DayCoverageInfo.yml
│   │   │   ├── Meridian.Ui.Services.DayEventCount.yml
│   │   │   ├── Meridian.Ui.Services.DayNode.yml
│   │   │   ├── Meridian.Ui.Services.DefragmentationApiResult.yml
│   │   │   ├── Meridian.Ui.Services.DeletedFileInfo.yml
│   │   │   ├── Meridian.Ui.Services.DeleteResponse.yml
│   │   │   ├── Meridian.Ui.Services.DepthChartData.yml
│   │   │   ├── Meridian.Ui.Services.DepthPoint.yml
│   │   │   ├── Meridian.Ui.Services.DesktopJsonOptions.yml
│   │   │   ├── Meridian.Ui.Services.DiagnosticBundle.yml
│   │   │   ├── Meridian.Ui.Services.DiagnosticBundleOptions.yml
│   │   │   ├── Meridian.Ui.Services.DiagnosticBundleResponse.yml
│   │   │   ├── Meridian.Ui.Services.DiagnosticBundleResult.yml
│   │   │   ├── Meridian.Ui.Services.DiagnosticConfigValidationResult.yml
│   │   │   ├── Meridian.Ui.Services.DiagnosticIssue.yml
│   │   │   ├── Meridian.Ui.Services.DiagnosticProviderTestResult.yml
│   │   │   ├── Meridian.Ui.Services.DiagnosticsService.yml
│   │   │   ├── Meridian.Ui.Services.DiagnosticSystemMetrics.yml
│   │   │   ├── Meridian.Ui.Services.DriveStorageInfo.yml
│   │   │   ├── Meridian.Ui.Services.DryRunResponse.yml
│   │   │   ├── Meridian.Ui.Services.DryRunResult.yml
│   │   │   ├── Meridian.Ui.Services.EnableResponse.yml
│   │   │   ├── Meridian.Ui.Services.ErrorCodeInfo.yml
│   │   │   ├── Meridian.Ui.Services.ErrorCodesResponse.yml
│   │   │   ├── Meridian.Ui.Services.ErrorCodesResult.yml
│   │   │   ├── Meridian.Ui.Services.ErrorHandledEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.ErrorHandlingOptions.yml
│   │   │   ├── Meridian.Ui.Services.ErrorHandlingService.yml
│   │   │   ├── Meridian.Ui.Services.ErrorMessages.yml
│   │   │   ├── Meridian.Ui.Services.ErrorRecord.yml
│   │   │   ├── Meridian.Ui.Services.ErrorSeverity.yml
│   │   │   ├── Meridian.Ui.Services.EventPreviewResponse.yml
│   │   │   ├── Meridian.Ui.Services.EventPreviewResult.yml
│   │   │   ├── Meridian.Ui.Services.EventReplayService.yml
│   │   │   ├── Meridian.Ui.Services.ExportConfiguration.yml
│   │   │   ├── Meridian.Ui.Services.ExportDataType.yml
│   │   │   ├── Meridian.Ui.Services.ExportDateRange.yml
│   │   │   ├── Meridian.Ui.Services.ExportEstimate.yml
│   │   │   ├── Meridian.Ui.Services.ExportFormat.yml
│   │   │   ├── Meridian.Ui.Services.ExportJob.yml
│   │   │   ├── Meridian.Ui.Services.ExportJobEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.ExportJobProgressEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.ExportJobRequest.yml
│   │   │   ├── Meridian.Ui.Services.ExportJobRun.yml
│   │   │   ├── Meridian.Ui.Services.ExportJobStatus.yml
│   │   │   ├── Meridian.Ui.Services.ExportOptions.yml
│   │   │   ├── Meridian.Ui.Services.ExportPriority.yml
│   │   │   ├── Meridian.Ui.Services.ExportProfile.yml
│   │   │   ├── Meridian.Ui.Services.ExportProgress.yml
│   │   │   ├── Meridian.Ui.Services.ExportResult.yml
│   │   │   ├── Meridian.Ui.Services.ExportSchedule.yml
│   │   │   ├── Meridian.Ui.Services.FailoverConfigResponse.yml
│   │   │   ├── Meridian.Ui.Services.FailoverConfigResult.yml
│   │   │   ├── Meridian.Ui.Services.FailoverEvent.yml
│   │   │   ├── Meridian.Ui.Services.FailoverResponse.yml
│   │   │   ├── Meridian.Ui.Services.FailoverResult.yml
│   │   │   ├── Meridian.Ui.Services.FailoverThresholds.yml
│   │   │   ├── Meridian.Ui.Services.FailoverThresholdsResponse.yml
│   │   │   ├── Meridian.Ui.Services.FileComparisonResult.yml
│   │   │   ├── Meridian.Ui.Services.FileMetadata.yml
│   │   │   ├── Meridian.Ui.Services.FilePreview.yml
│   │   │   ├── Meridian.Ui.Services.FileSearchApiResponse.yml
│   │   │   ├── Meridian.Ui.Services.FileSearchQuery.yml
│   │   │   ├── Meridian.Ui.Services.FileSearchResult.yml
│   │   │   ├── Meridian.Ui.Services.FileToDelete.yml
│   │   │   ├── Meridian.Ui.Services.FileVerificationResult.yml
│   │   │   ├── Meridian.Ui.Services.FloatingWorkspaceWindowState.yml
│   │   │   ├── Meridian.Ui.Services.FormatHelpers.yml
│   │   │   ├── Meridian.Ui.Services.GapAnalysisOptions.yml
│   │   │   ├── Meridian.Ui.Services.GapAnalysisResponse.yml
│   │   │   ├── Meridian.Ui.Services.GapAnalysisResult.yml
│   │   │   ├── Meridian.Ui.Services.GapHandlingStrategy.yml
│   │   │   ├── Meridian.Ui.Services.GapInfo.yml
│   │   │   ├── Meridian.Ui.Services.GapRepairDetail.yml
│   │   │   ├── Meridian.Ui.Services.GapRepairOptions.yml
│   │   │   ├── Meridian.Ui.Services.GapRepairProgress.yml
│   │   │   ├── Meridian.Ui.Services.GapRepairResponse.yml
│   │   │   ├── Meridian.Ui.Services.GapRepairResult.yml
│   │   │   ├── Meridian.Ui.Services.GapStrategy.yml
│   │   │   ├── Meridian.Ui.Services.GapSummaryData.yml
│   │   │   ├── Meridian.Ui.Services.GapType.yml
│   │   │   ├── Meridian.Ui.Services.GuardrailViolation.yml
│   │   │   ├── Meridian.Ui.Services.HealthAlertEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.HealthAlertType.yml
│   │   │   ├── Meridian.Ui.Services.HealthHistoryPoint.yml
│   │   │   ├── Meridian.Ui.Services.HealthIssue.yml
│   │   │   ├── Meridian.Ui.Services.HealthMetrics.yml
│   │   │   ├── Meridian.Ui.Services.HealthScoreBreakdown.yml
│   │   │   ├── Meridian.Ui.Services.HealthSummary.yml
│   │   │   ├── Meridian.Ui.Services.HealthUpdateEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.HeatmapLevel.yml
│   │   │   ├── Meridian.Ui.Services.HttpClientConfiguration.yml
│   │   │   ├── Meridian.Ui.Services.HttpClientFactoryProvider.yml
│   │   │   ├── Meridian.Ui.Services.HttpClientNames.yml
│   │   │   ├── Meridian.Ui.Services.IAdminMaintenanceService.yml
│   │   │   ├── Meridian.Ui.Services.IndexConstituentsResponse.yml
│   │   │   ├── Meridian.Ui.Services.IndexConstituentsResult.yml
│   │   │   ├── Meridian.Ui.Services.IndicatorData.yml
│   │   │   ├── Meridian.Ui.Services.IndicatorType.yml
│   │   │   ├── Meridian.Ui.Services.IndicatorValue.yml
│   │   │   ├── Meridian.Ui.Services.InsightMessage.yml
│   │   │   ├── Meridian.Ui.Services.InsightType.yml
│   │   │   ├── Meridian.Ui.Services.IntegrityEvent.yml
│   │   │   ├── Meridian.Ui.Services.IntegrityEventsService.yml
│   │   │   ├── Meridian.Ui.Services.IntegrityEventType.yml
│   │   │   ├── Meridian.Ui.Services.IntegritySeverity.yml
│   │   │   ├── Meridian.Ui.Services.IntegritySummary.yml
│   │   │   ├── Meridian.Ui.Services.IssueSeverity.yml
│   │   │   ├── Meridian.Ui.Services.LatencyBucket.yml
│   │   │   ├── Meridian.Ui.Services.LatencyHistogramOptions.yml
│   │   │   ├── Meridian.Ui.Services.LatencyHistogramResponse.yml
│   │   │   ├── Meridian.Ui.Services.LatencyHistogramResult.yml
│   │   │   ├── Meridian.Ui.Services.LatencyStatisticsResponse.yml
│   │   │   ├── Meridian.Ui.Services.LatencyStatisticsResult.yml
│   │   │   ├── Meridian.Ui.Services.LeanAutoExportConfigureOptions.yml
│   │   │   ├── Meridian.Ui.Services.LeanAutoExportStatus.yml
│   │   │   ├── Meridian.Ui.Services.LeanConfiguration.yml
│   │   │   ├── Meridian.Ui.Services.LeanConfigurationUpdate.yml
│   │   │   ├── Meridian.Ui.Services.LeanIntegrationService.yml
│   │   │   ├── Meridian.Ui.Services.LeanStatus.yml
│   │   │   ├── Meridian.Ui.Services.LeanSymbolMapping.yml
│   │   │   ├── Meridian.Ui.Services.LeanSymbolMappingResult.yml
│   │   │   ├── Meridian.Ui.Services.LeanVerificationResponse.yml
│   │   │   ├── Meridian.Ui.Services.LeanVerificationResult.yml
│   │   │   ├── Meridian.Ui.Services.LegalHold.yml
│   │   │   ├── Meridian.Ui.Services.LegalHoldEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.LiveDataService.yml
│   │   │   ├── Meridian.Ui.Services.LoggingService.yml
│   │   │   ├── Meridian.Ui.Services.MacdData.yml
│   │   │   ├── Meridian.Ui.Services.MaintenanceCleanupResult.yml
│   │   │   ├── Meridian.Ui.Services.MaintenanceExecutionLog.yml
│   │   │   ├── Meridian.Ui.Services.MaintenanceHistoryResponse.yml
│   │   │   ├── Meridian.Ui.Services.MaintenanceHistoryResult.yml
│   │   │   ├── Meridian.Ui.Services.MaintenanceOperation.yml
│   │   │   ├── Meridian.Ui.Services.MaintenanceResult.yml
│   │   │   ├── Meridian.Ui.Services.MaintenanceRunOptions.yml
│   │   │   ├── Meridian.Ui.Services.MaintenanceRunResponse.yml
│   │   │   ├── Meridian.Ui.Services.MaintenanceRunResult.yml
│   │   │   ├── Meridian.Ui.Services.MaintenanceRunSummary.yml
│   │   │   ├── Meridian.Ui.Services.MaintenanceSchedule.yml
│   │   │   ├── Meridian.Ui.Services.MaintenanceScheduleConfig.yml
│   │   │   ├── Meridian.Ui.Services.MaintenanceScheduleResponse.yml
│   │   │   ├── Meridian.Ui.Services.MaintenanceScheduleResult.yml
│   │   │   ├── Meridian.Ui.Services.MaintenanceScope.yml
│   │   │   ├── Meridian.Ui.Services.MaintenanceTask.yml
│   │   │   ├── Meridian.Ui.Services.MaintenanceTaskType.yml
│   │   │   ├── Meridian.Ui.Services.MaintenanceTimingConfig.yml
│   │   │   ├── Meridian.Ui.Services.ManifestEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.ManifestService.yml
│   │   │   ├── Meridian.Ui.Services.ManifestVerificationEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.ManifestVerificationResult.yml
│   │   │   ├── Meridian.Ui.Services.MappingProviderInfo.yml
│   │   │   ├── Meridian.Ui.Services.MigrationPlanApiResult.yml
│   │   │   ├── Meridian.Ui.Services.MigrationPlanItem.yml
│   │   │   ├── Meridian.Ui.Services.MonthNode.yml
│   │   │   ├── Meridian.Ui.Services.NavigationPage.yml
│   │   │   ├── Meridian.Ui.Services.NotificationService.yml
│   │   │   ├── Meridian.Ui.Services.NotificationType.yml
│   │   │   ├── Meridian.Ui.Services.OAuthRefreshResult.yml
│   │   │   ├── Meridian.Ui.Services.OAuthRefreshService.yml
│   │   │   ├── Meridian.Ui.Services.OAuthTokenStatus.yml
│   │   │   ├── Meridian.Ui.Services.OnboardingTourService.yml
│   │   │   ├── Meridian.Ui.Services.OperationResponse.yml
│   │   │   ├── Meridian.Ui.Services.OperationResult.yml
│   │   │   ├── Meridian.Ui.Services.OptimizationExecutionResult.yml
│   │   │   ├── Meridian.Ui.Services.OptimizationProgress.yml
│   │   │   ├── Meridian.Ui.Services.OptimizationRecommendation.yml
│   │   │   ├── Meridian.Ui.Services.OptimizationType.yml
│   │   │   ├── Meridian.Ui.Services.OrderBookFlowStats.yml
│   │   │   ├── Meridian.Ui.Services.OrderBookHeatmapData.yml
│   │   │   ├── Meridian.Ui.Services.OrderBookHistorySnapshot.yml
│   │   │   ├── Meridian.Ui.Services.OrderBookLevel.yml
│   │   │   ├── Meridian.Ui.Services.OrderBookSnapshot.yml
│   │   │   ├── Meridian.Ui.Services.OrderBookState.yml
│   │   │   ├── Meridian.Ui.Services.OrderBookUpdateEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.OrderBookVisualizationService.yml
│   │   │   ├── Meridian.Ui.Services.OrderFlowStats.yml
│   │   │   ├── Meridian.Ui.Services.OrphanedFileInfo.yml
│   │   │   ├── Meridian.Ui.Services.OrphanFilesResult.yml
│   │   │   ├── Meridian.Ui.Services.PackageCreationOptions.yml
│   │   │   ├── Meridian.Ui.Services.PackageCreationResult.yml
│   │   │   ├── Meridian.Ui.Services.PackagedFile.yml
│   │   │   ├── Meridian.Ui.Services.PackageFormat.yml
│   │   │   ├── Meridian.Ui.Services.PackageImportOptions.yml
│   │   │   ├── Meridian.Ui.Services.PackageImportResult.yml
│   │   │   ├── Meridian.Ui.Services.PackageInfo.yml
│   │   │   ├── Meridian.Ui.Services.PackageManifest.yml
│   │   │   ├── Meridian.Ui.Services.PackageProgress.yml
│   │   │   ├── Meridian.Ui.Services.PackageRequest.yml
│   │   │   ├── Meridian.Ui.Services.PackageResult.yml
│   │   │   ├── Meridian.Ui.Services.PackageValidationResult.yml
│   │   │   ├── Meridian.Ui.Services.PackageVerificationResult.yml
│   │   │   ├── Meridian.Ui.Services.PaletteCommand.yml
│   │   │   ├── Meridian.Ui.Services.PaletteCommandCategory.yml
│   │   │   ├── Meridian.Ui.Services.PaletteCommandEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.PermissionValidationResponse.yml
│   │   │   ├── Meridian.Ui.Services.PermissionValidationResult.yml
│   │   │   ├── Meridian.Ui.Services.PortablePackagerService.yml
│   │   │   ├── Meridian.Ui.Services.PortfolioEntry.yml
│   │   │   ├── Meridian.Ui.Services.PortfolioImportResult.yml
│   │   │   ├── Meridian.Ui.Services.PortfolioImportService.yml
│   │   │   ├── Meridian.Ui.Services.PortfolioParseResult.yml
│   │   │   ├── Meridian.Ui.Services.PreExportQualityReport.yml
│   │   │   ├── Meridian.Ui.Services.PreflightCheck.yml
│   │   │   ├── Meridian.Ui.Services.PreflightCheckResult.yml
│   │   │   ├── Meridian.Ui.Services.PreflightResult.yml
│   │   │   ├── Meridian.Ui.Services.ProviderBindingMutationResult.yml
│   │   │   ├── Meridian.Ui.Services.ProviderBindingsResult.yml
│   │   │   ├── Meridian.Ui.Services.ProviderCapabilities.yml
│   │   │   ├── Meridian.Ui.Services.ProviderCapabilitiesResponse.yml
│   │   │   ├── Meridian.Ui.Services.ProviderCapabilitiesResult.yml
│   │   │   ├── Meridian.Ui.Services.ProviderCertificationMutationResult.yml
│   │   │   ├── Meridian.Ui.Services.ProviderCertificationsResult.yml
│   │   │   ├── Meridian.Ui.Services.ProviderComparison.yml
│   │   │   ├── Meridian.Ui.Services.ProviderConnectionMutationResult.yml
│   │   │   ├── Meridian.Ui.Services.ProviderConnectionsResult.yml
│   │   │   ├── Meridian.Ui.Services.ProviderConnectivityResult.yml
│   │   │   ├── Meridian.Ui.Services.ProviderCredentialStatus.yml
│   │   │   ├── Meridian.Ui.Services.ProviderDetailResponse.yml
│   │   │   ├── Meridian.Ui.Services.ProviderDetailResult.yml
│   │   │   ├── Meridian.Ui.Services.ProviderDiagnostics.yml
│   │   │   ├── Meridian.Ui.Services.ProviderHealth.yml
│   │   │   ├── Meridian.Ui.Services.ProviderHealthComparison.yml
│   │   │   ├── Meridian.Ui.Services.ProviderHealthData.yml
│   │   │   ├── Meridian.Ui.Services.ProviderHealthInfo.yml
│   │   │   ├── Meridian.Ui.Services.ProviderHealthResponse.yml
│   │   │   ├── Meridian.Ui.Services.ProviderHealthService.yml
│   │   │   ├── Meridian.Ui.Services.ProviderInfo.yml
│   │   │   ├── Meridian.Ui.Services.ProviderLatencyData.yml
│   │   │   ├── Meridian.Ui.Services.ProviderLatencyStatistics.yml
│   │   │   ├── Meridian.Ui.Services.ProviderManagementService.yml
│   │   │   ├── Meridian.Ui.Services.ProviderManagementTestResult.yml
│   │   │   ├── Meridian.Ui.Services.ProviderPoliciesResult.yml
│   │   │   ├── Meridian.Ui.Services.ProviderPresetApplyResult.yml
│   │   │   ├── Meridian.Ui.Services.ProviderPresetsResult.yml
│   │   │   ├── Meridian.Ui.Services.ProviderRateLimit.yml
│   │   │   ├── Meridian.Ui.Services.ProviderRateLimitStatus.yml
│   │   │   ├── Meridian.Ui.Services.ProviderRouteHistoryResult.yml
│   │   │   ├── Meridian.Ui.Services.ProviderRoutePreviewQueryResult.yml
│   │   │   ├── Meridian.Ui.Services.ProviderStatistics.yml
│   │   │   ├── Meridian.Ui.Services.ProviderStatusInfo.yml
│   │   │   ├── Meridian.Ui.Services.ProviderStatusResponse.yml
│   │   │   ├── Meridian.Ui.Services.ProviderTestResponse.yml
│   │   │   ├── Meridian.Ui.Services.ProviderTestResult.yml
│   │   │   ├── Meridian.Ui.Services.ProviderTrustSnapshotsResult.yml
│   │   │   ├── Meridian.Ui.Services.QuickAction.yml
│   │   │   ├── Meridian.Ui.Services.QuickActionType.yml
│   │   │   ├── Meridian.Ui.Services.QuickCheckItem.yml
│   │   │   ├── Meridian.Ui.Services.QuickCheckResponse.yml
│   │   │   ├── Meridian.Ui.Services.QuickCheckResult.yml
│   │   │   ├── Meridian.Ui.Services.QuoteEvent.yml
│   │   │   ├── Meridian.Ui.Services.RateLimitDataPoint.yml
│   │   │   ├── Meridian.Ui.Services.RateLimitHistoryResponse.yml
│   │   │   ├── Meridian.Ui.Services.RateLimitHistoryResult.yml
│   │   │   ├── Meridian.Ui.Services.RateLimitsResponse.yml
│   │   │   ├── Meridian.Ui.Services.RateLimitsResult.yml
│   │   │   ├── Meridian.Ui.Services.RateLimitStatusResponse.yml
│   │   │   ├── Meridian.Ui.Services.RateLimitStatusResult.yml
│   │   │   ├── Meridian.Ui.Services.RecentPackageInfo.yml
│   │   │   ├── Meridian.Ui.Services.RecommendationPriority.yml
│   │   │   ├── Meridian.Ui.Services.RemediationStep.yml
│   │   │   ├── Meridian.Ui.Services.ReplayEvent.yml
│   │   │   ├── Meridian.Ui.Services.ReplayEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.ReplayFileInfo.yml
│   │   │   ├── Meridian.Ui.Services.ReplayFilesResponse.yml
│   │   │   ├── Meridian.Ui.Services.ReplayFilesResult.yml
│   │   │   ├── Meridian.Ui.Services.ReplayFileStats.yml
│   │   │   ├── Meridian.Ui.Services.ReplayOptions.yml
│   │   │   ├── Meridian.Ui.Services.ReplayProgressEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.ReplayStartResponse.yml
│   │   │   ├── Meridian.Ui.Services.ReplayStartResult.yml
│   │   │   ├── Meridian.Ui.Services.ReplayState.yml
│   │   │   ├── Meridian.Ui.Services.ReplayStateChangedEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.ReplayStatus.yml
│   │   │   ├── Meridian.Ui.Services.RetentionApplyResponse.yml
│   │   │   ├── Meridian.Ui.Services.RetentionApplyResult.yml
│   │   │   ├── Meridian.Ui.Services.RetentionAuditReport.yml
│   │   │   ├── Meridian.Ui.Services.RetentionConfiguration.yml
│   │   │   ├── Meridian.Ui.Services.RetentionDryRunResult.yml
│   │   │   ├── Meridian.Ui.Services.RetentionGuardrails.yml
│   │   │   ├── Meridian.Ui.Services.RetentionPoliciesResponse.yml
│   │   │   ├── Meridian.Ui.Services.RetentionPoliciesResult.yml
│   │   │   ├── Meridian.Ui.Services.RetentionPolicy.yml
│   │   │   ├── Meridian.Ui.Services.RetentionValidationResult.yml
│   │   │   ├── Meridian.Ui.Services.SampleEstimate.yml
│   │   │   ├── Meridian.Ui.Services.SampleEstimateResponse.yml
│   │   │   ├── Meridian.Ui.Services.SampleStatistics.yml
│   │   │   ├── Meridian.Ui.Services.SamplingDeleteResponse.yml
│   │   │   ├── Meridian.Ui.Services.SamplingOptions.yml
│   │   │   ├── Meridian.Ui.Services.SamplingPreset.yml
│   │   │   ├── Meridian.Ui.Services.SamplingProgressEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.SamplingResponse.yml
│   │   │   ├── Meridian.Ui.Services.SamplingResult.yml
│   │   │   ├── Meridian.Ui.Services.SamplingStrategy.yml
│   │   │   ├── Meridian.Ui.Services.SamplingStrategyType.yml
│   │   │   ├── Meridian.Ui.Services.SamplingValidationResult.yml
│   │   │   ├── Meridian.Ui.Services.SavedSample.yml
│   │   │   ├── Meridian.Ui.Services.SavedSamplesResponse.yml
│   │   │   ├── Meridian.Ui.Services.ScheduledMaintenanceService.yml
│   │   │   ├── Meridian.Ui.Services.ScheduleExecutionLog.yml
│   │   │   ├── Meridian.Ui.Services.ScheduleExecutionResult.yml
│   │   │   ├── Meridian.Ui.Services.ScheduleFrequency.yml
│   │   │   ├── Meridian.Ui.Services.ScheduleManagerService.yml
│   │   │   ├── Meridian.Ui.Services.ScheduleTemplate.yml
│   │   │   ├── Meridian.Ui.Services.ScheduleType.yml
│   │   │   ├── Meridian.Ui.Services.SchemaService.yml
│   │   │   ├── Meridian.Ui.Services.SchemaServiceBase.yml
│   │   │   ├── Meridian.Ui.Services.ScoreComponent.yml
│   │   │   ├── Meridian.Ui.Services.SearchOptions.yml
│   │   │   ├── Meridian.Ui.Services.SearchResult.yml
│   │   │   ├── Meridian.Ui.Services.SearchResultItem.yml
│   │   │   ├── Meridian.Ui.Services.SearchResults.yml
│   │   │   ├── Meridian.Ui.Services.SearchService.yml
│   │   │   ├── Meridian.Ui.Services.SearchSuggestion.yml
│   │   │   ├── Meridian.Ui.Services.SelfTestItem.yml
│   │   │   ├── Meridian.Ui.Services.SelfTestOptions.yml
│   │   │   ├── Meridian.Ui.Services.SelfTestResponse.yml
│   │   │   ├── Meridian.Ui.Services.SelfTestResult.yml
│   │   │   ├── Meridian.Ui.Services.Services.AcknowledgeResponse.yml
│   │   │   ├── Meridian.Ui.Services.Services.AnalysisExportService.yml
│   │   │   ├── Meridian.Ui.Services.Services.AnomalyEvent.yml
│   │   │   ├── Meridian.Ui.Services.Services.BackendInstallationInfo.yml
│   │   │   ├── Meridian.Ui.Services.Services.BackendRuntimeInfo.yml
│   │   │   ├── Meridian.Ui.Services.Services.BackendServiceManagerBase.yml
│   │   │   ├── Meridian.Ui.Services.Services.BackendServiceOperationResult.yml
│   │   │   ├── Meridian.Ui.Services.Services.BackendServiceStatus.yml
│   │   │   ├── Meridian.Ui.Services.Services.ColorPalette.ArgbColor.yml
│   │   │   ├── Meridian.Ui.Services.Services.ColorPalette.yml
│   │   │   ├── Meridian.Ui.Services.Services.ConfigProfile.yml
│   │   │   ├── Meridian.Ui.Services.Services.ConfigServiceBase.yml
│   │   │   ├── Meridian.Ui.Services.Services.ConfigValidationResultDetail.yml
│   │   │   ├── Meridian.Ui.Services.Services.ConnectionServiceBase.yml
│   │   │   ├── Meridian.Ui.Services.Services.CredentialState.yml
│   │   │   ├── Meridian.Ui.Services.Services.DailyQualityRecord.yml
│   │   │   ├── Meridian.Ui.Services.Services.DataGapInfo.yml
│   │   │   ├── Meridian.Ui.Services.Services.DataQualityRefreshCoordinator.yml
│   │   │   ├── Meridian.Ui.Services.Services.DataQualityServiceBase.yml
│   │   │   ├── Meridian.Ui.Services.Services.DataQualitySummary.yml
│   │   │   ├── Meridian.Ui.Services.Services.ErrorDetailsModel.yml
│   │   │   ├── Meridian.Ui.Services.Services.ExportPresetServiceBase.yml
│   │   │   ├── Meridian.Ui.Services.Services.FeatureHelp.yml
│   │   │   ├── Meridian.Ui.Services.Services.FixtureDataService.yml
│   │   │   ├── Meridian.Ui.Services.Services.FixtureModeDetector.yml
│   │   │   ├── Meridian.Ui.Services.Services.FixtureScenario.yml
│   │   │   ├── Meridian.Ui.Services.Services.FormValidationRules.yml
│   │   │   ├── Meridian.Ui.Services.Services.InfoBarConstants.yml
│   │   │   ├── Meridian.Ui.Services.Services.InfoBarSeverityLevel.yml
│   │   │   ├── Meridian.Ui.Services.Services.IntegrityVerificationResult.yml
│   │   │   ├── Meridian.Ui.Services.Services.IQualityArchiveStore.yml
│   │   │   ├── Meridian.Ui.Services.Services.LiveStatusEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.Services.LogEntryEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.Services.LoggingServiceBase.yml
│   │   │   ├── Meridian.Ui.Services.Services.LogLevel.yml
│   │   │   ├── Meridian.Ui.Services.Services.NavigationServiceBase.yml
│   │   │   ├── Meridian.Ui.Services.Services.NotificationEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.Services.NotificationHistoryItem.yml
│   │   │   ├── Meridian.Ui.Services.Services.NotificationServiceBase.yml
│   │   │   ├── Meridian.Ui.Services.Services.NotificationSettings.yml
│   │   │   ├── Meridian.Ui.Services.Services.OnboardingTip.yml
│   │   │   ├── Meridian.Ui.Services.Services.PeriodicRefreshScheduler.yml
│   │   │   ├── Meridian.Ui.Services.Services.ProviderCatalogEntry.yml
│   │   │   ├── Meridian.Ui.Services.Services.ProviderCredentialStatus.yml
│   │   │   ├── Meridian.Ui.Services.Services.ProviderInfo.yml
│   │   │   ├── Meridian.Ui.Services.Services.ProviderTier.yml
│   │   │   ├── Meridian.Ui.Services.Services.QualityAlert.yml
│   │   │   ├── Meridian.Ui.Services.Services.QualityArchiveStore.yml
│   │   │   ├── Meridian.Ui.Services.Services.QualityCheckResult.yml
│   │   │   ├── Meridian.Ui.Services.Services.QualityCompletenessReport.yml
│   │   │   ├── Meridian.Ui.Services.Services.QualityDataGap.yml
│   │   │   ├── Meridian.Ui.Services.Services.QualityIssue.yml
│   │   │   ├── Meridian.Ui.Services.Services.QualityScoreEntry.yml
│   │   │   ├── Meridian.Ui.Services.Services.QualityTrendData.yml
│   │   │   ├── Meridian.Ui.Services.Services.SettingsConfigurationService.yml
│   │   │   ├── Meridian.Ui.Services.Services.SimpleStatus.yml
│   │   │   ├── Meridian.Ui.Services.Services.SourceRanking.yml
│   │   │   ├── Meridian.Ui.Services.Services.StatusChangedEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.Services.StatusProviderInfo.yml
│   │   │   ├── Meridian.Ui.Services.Services.StatusServiceBase.yml
│   │   │   ├── Meridian.Ui.Services.Services.SymbolQualityReport.yml
│   │   │   ├── Meridian.Ui.Services.Services.SymbolQualitySummary.yml
│   │   │   ├── Meridian.Ui.Services.Services.ThemeServiceBase.yml
│   │   │   ├── Meridian.Ui.Services.Services.TooltipContent.yml
│   │   │   ├── Meridian.Ui.Services.Services.TrendDataPoint.yml
│   │   │   ├── Meridian.Ui.Services.Services.ValidationExtensions.yml
│   │   │   ├── Meridian.Ui.Services.Services.ValidationResult.yml
│   │   │   ├── Meridian.Ui.Services.Services.yml
│   │   │   ├── Meridian.Ui.Services.ServiceUrlChangedEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.SessionState.yml
│   │   │   ├── Meridian.Ui.Services.SetupPreset.yml
│   │   │   ├── Meridian.Ui.Services.SetupWizardService.yml
│   │   │   ├── Meridian.Ui.Services.ShowConfigResponse.yml
│   │   │   ├── Meridian.Ui.Services.ShowConfigResult.yml
│   │   │   ├── Meridian.Ui.Services.SkippedFileInfo.yml
│   │   │   ├── Meridian.Ui.Services.SmartRecommendationsService.yml
│   │   │   ├── Meridian.Ui.Services.StaleIndicatorResult.yml
│   │   │   ├── Meridian.Ui.Services.StorageAnalysisOptions.yml
│   │   │   ├── Meridian.Ui.Services.StorageAnalysisProgress.yml
│   │   │   ├── Meridian.Ui.Services.StorageAnalytics.yml
│   │   │   ├── Meridian.Ui.Services.StorageAnalyticsEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.StorageAnalyticsService.yml
│   │   │   ├── Meridian.Ui.Services.StorageCatalogApiResult.yml
│   │   │   ├── Meridian.Ui.Services.StorageCategory.yml
│   │   │   ├── Meridian.Ui.Services.StorageHealth.yml
│   │   │   ├── Meridian.Ui.Services.StorageHealthCheckResult.yml
│   │   │   ├── Meridian.Ui.Services.StorageHealthReport.yml
│   │   │   ├── Meridian.Ui.Services.StorageIssue.yml
│   │   │   ├── Meridian.Ui.Services.StorageOptimizationAdvisorService.yml
│   │   │   ├── Meridian.Ui.Services.StorageOptimizationReport.yml
│   │   │   ├── Meridian.Ui.Services.StorageRetentionPolicy.yml
│   │   │   ├── Meridian.Ui.Services.StorageServiceBase.yml
│   │   │   ├── Meridian.Ui.Services.StorageStatsSummary.yml
│   │   │   ├── Meridian.Ui.Services.StorageStatusResponse.yml
│   │   │   ├── Meridian.Ui.Services.StorageTierConfig.yml
│   │   │   ├── Meridian.Ui.Services.StreamHealthInfo.yml
│   │   │   ├── Meridian.Ui.Services.SubscribeRequest.yml
│   │   │   ├── Meridian.Ui.Services.SubscriptionInfo.yml
│   │   │   ├── Meridian.Ui.Services.SubscriptionResult.yml
│   │   │   ├── Meridian.Ui.Services.SuggestedBackfill.yml
│   │   │   ├── Meridian.Ui.Services.SwitchProviderResponse.yml
│   │   │   ├── Meridian.Ui.Services.SwitchProviderResult.yml
│   │   │   ├── Meridian.Ui.Services.SymbolAnalyticsInfo.yml
│   │   │   ├── Meridian.Ui.Services.SymbolArchiveInfo.yml
│   │   │   ├── Meridian.Ui.Services.SymbolCheckpoint.yml
│   │   │   ├── Meridian.Ui.Services.SymbolCheckpointStatus.yml
│   │   │   ├── Meridian.Ui.Services.SymbolCompleteness.yml
│   │   │   ├── Meridian.Ui.Services.SymbolCoverageData.yml
│   │   │   ├── Meridian.Ui.Services.SymbolDayData.yml
│   │   │   ├── Meridian.Ui.Services.SymbolDeletionSummary.yml
│   │   │   ├── Meridian.Ui.Services.SymbolDetailedStatus.yml
│   │   │   ├── Meridian.Ui.Services.SymbolFileDto.yml
│   │   │   ├── Meridian.Ui.Services.SymbolGapAnalysisDto.yml
│   │   │   ├── Meridian.Ui.Services.SymbolGapSummary.yml
│   │   │   ├── Meridian.Ui.Services.SymbolGroupEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.SymbolGroupService.yml
│   │   │   ├── Meridian.Ui.Services.SymbolInfo.yml
│   │   │   ├── Meridian.Ui.Services.SymbolListResponse.yml
│   │   │   ├── Meridian.Ui.Services.SymbolListResult.yml
│   │   │   ├── Meridian.Ui.Services.SymbolManagementService.yml
│   │   │   ├── Meridian.Ui.Services.SymbolMapping.yml
│   │   │   ├── Meridian.Ui.Services.SymbolMappingsConfig.yml
│   │   │   ├── Meridian.Ui.Services.SymbolMappingService.yml
│   │   │   ├── Meridian.Ui.Services.SymbolNode.yml
│   │   │   ├── Meridian.Ui.Services.SymbolOperationResponse.yml
│   │   │   ├── Meridian.Ui.Services.SymbolOperationResult.yml
│   │   │   ├── Meridian.Ui.Services.SymbolPathResponse.yml
│   │   │   ├── Meridian.Ui.Services.SymbolQualityInfo.yml
│   │   │   ├── Meridian.Ui.Services.SymbolSearchApiResponse.yml
│   │   │   ├── Meridian.Ui.Services.SymbolSearchApiResult.yml
│   │   │   ├── Meridian.Ui.Services.SymbolSearchResponse.yml
│   │   │   ├── Meridian.Ui.Services.SymbolSearchResultItem.yml
│   │   │   ├── Meridian.Ui.Services.SymbolStatistics.yml
│   │   │   ├── Meridian.Ui.Services.SymbolStorageInfo.yml
│   │   │   ├── Meridian.Ui.Services.SymbolStorageStats.yml
│   │   │   ├── Meridian.Ui.Services.SymbolTransform.yml
│   │   │   ├── Meridian.Ui.Services.SymbolValidationResult.yml
│   │   │   ├── Meridian.Ui.Services.SystemEvent.yml
│   │   │   ├── Meridian.Ui.Services.SystemHealthService.yml
│   │   │   ├── Meridian.Ui.Services.SystemHealthSummary.yml
│   │   │   ├── Meridian.Ui.Services.SystemMetrics.yml
│   │   │   ├── Meridian.Ui.Services.TechnicalIndicatorInfo.yml
│   │   │   ├── Meridian.Ui.Services.ThroughputAnalysisOptions.yml
│   │   │   ├── Meridian.Ui.Services.ThroughputAnalysisResponse.yml
│   │   │   ├── Meridian.Ui.Services.ThroughputAnalysisResult.yml
│   │   │   ├── Meridian.Ui.Services.ThroughputDataPoint.yml
│   │   │   ├── Meridian.Ui.Services.TierConfigResponse.yml
│   │   │   ├── Meridian.Ui.Services.TierConfigResult.yml
│   │   │   ├── Meridian.Ui.Services.TierMigrationApiResult.yml
│   │   │   ├── Meridian.Ui.Services.TierMigrationOptions.yml
│   │   │   ├── Meridian.Ui.Services.TierMigrationResponse.yml
│   │   │   ├── Meridian.Ui.Services.TierMigrationResult.yml
│   │   │   ├── Meridian.Ui.Services.TierStatisticsApiResult.yml
│   │   │   ├── Meridian.Ui.Services.TierStats.yml
│   │   │   ├── Meridian.Ui.Services.TierUsage.yml
│   │   │   ├── Meridian.Ui.Services.TierUsageResponse.yml
│   │   │   ├── Meridian.Ui.Services.TierUsageResult.yml
│   │   │   ├── Meridian.Ui.Services.TimeAndSalesData.yml
│   │   │   ├── Meridian.Ui.Services.TimeSeriesAlignmentService.yml
│   │   │   ├── Meridian.Ui.Services.TimeSeriesInterval.yml
│   │   │   ├── Meridian.Ui.Services.TokenExpirationWarningEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.TokenRefreshEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.TokenRefreshFailedEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.TooltipPlacement.yml
│   │   │   ├── Meridian.Ui.Services.TourCategory.yml
│   │   │   ├── Meridian.Ui.Services.TourCompletedEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.TourDefinition.yml
│   │   │   ├── Meridian.Ui.Services.TourInfo.yml
│   │   │   ├── Meridian.Ui.Services.TourSession.yml
│   │   │   ├── Meridian.Ui.Services.TourStep.yml
│   │   │   ├── Meridian.Ui.Services.TourStepEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.TradeEvent.yml
│   │   │   ├── Meridian.Ui.Services.TradeEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.TradeRecord.yml
│   │   │   ├── Meridian.Ui.Services.TradeSide.yml
│   │   │   ├── Meridian.Ui.Services.TradingCalendarService.yml
│   │   │   ├── Meridian.Ui.Services.UnsubscribeResponse.yml
│   │   │   ├── Meridian.Ui.Services.UpdateBackfillScheduleRequest.yml
│   │   │   ├── Meridian.Ui.Services.UpdateMaintenanceScheduleRequest.yml
│   │   │   ├── Meridian.Ui.Services.ValidationDetail.yml
│   │   │   ├── Meridian.Ui.Services.ValidationIssue.yml
│   │   │   ├── Meridian.Ui.Services.ValidationResult.yml
│   │   │   ├── Meridian.Ui.Services.VerifiedFile.yml
│   │   │   ├── Meridian.Ui.Services.ViolationSeverity.yml
│   │   │   ├── Meridian.Ui.Services.VolumePriceLevel.yml
│   │   │   ├── Meridian.Ui.Services.VolumeProfileData.yml
│   │   │   ├── Meridian.Ui.Services.WatchlistData.yml
│   │   │   ├── Meridian.Ui.Services.WatchlistGroup.yml
│   │   │   ├── Meridian.Ui.Services.WatchlistItem.yml
│   │   │   ├── Meridian.Ui.Services.WatchlistService.yml
│   │   │   ├── Meridian.Ui.Services.WidgetPosition.yml
│   │   │   ├── Meridian.Ui.Services.WindowBounds.yml
│   │   │   ├── Meridian.Ui.Services.WorkspaceCategory.yml
│   │   │   ├── Meridian.Ui.Services.WorkspaceCategoryExtensions.yml
│   │   │   ├── Meridian.Ui.Services.WorkspaceEventArgs.yml
│   │   │   ├── Meridian.Ui.Services.WorkspaceLayoutPreset.yml
│   │   │   ├── Meridian.Ui.Services.WorkspacePage.yml
│   │   │   ├── Meridian.Ui.Services.WorkspaceTemplate.yml
│   │   │   ├── Meridian.Ui.Services.WorkstationLayoutState.yml
│   │   │   ├── Meridian.Ui.Services.WorkstationPaneState.yml
│   │   │   ├── Meridian.Ui.Services.YearNode.yml
│   │   │   ├── Meridian.Ui.Services.yml
│   │   │   ├── Meridian.Ui.Shared.DtoExtensions.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.AdminEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.AnalyticsEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.ApiKeyMiddleware.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.ApiKeyMiddlewareExtensions.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.ApiKeyRateLimitMiddleware.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.AuthEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.BackfillEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.BackfillScheduleEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.BankingEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.CalendarEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.CanonicalizationEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.CatalogEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.CheckpointEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.CircuitBreakerCommandRequest.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.ClearManualOverrideCommandRequest.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.ConfigEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.CppTraderEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.CreatePaperSessionRequest.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.CredentialEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.CronEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.DiagnosticsEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.DirectLendingEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.ExecutionAccountSnapshot.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.ExecutionEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.ExecutionGatewayHealth.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.ExecutionPortfolioSnapshot.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.ExportEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.FailoverEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.FundAccountEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.HealthEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.HistoricalEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.IBEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.IngestionJobEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.LeanEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.LiveDataEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.LoginSessionMiddleware.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.LoginSessionMiddlewareExtensions.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.MaintenanceScheduleEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.ManualOverrideCommandRequest.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.MessagingEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.MetricsDiff.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.MoneyMarketFundEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.OptionsEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.ParameterDiff.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.PositionDiffEntry.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.PositionLimitCommandRequest.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.PromotionEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.ProviderEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.ProviderExtendedEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.ReplayEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.ResilienceEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.RunComparisonRequest.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.RunDiffRequest.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.SamplingEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.SecurityMasterEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.StatusEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.StorageEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.StorageQualityEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.StrategyActionResult.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.StrategyLifecycleEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.StrategyRunDiff.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.StrategyStatusDto.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.SubscriptionEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.SymbolEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.SymbolMappingEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.TradingActionResult.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.UiEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.WorkstationEndpoints.yml
│   │   │   ├── Meridian.Ui.Shared.Endpoints.yml
│   │   │   ├── Meridian.Ui.Shared.HtmlTemplateGenerator.yml
│   │   │   ├── Meridian.Ui.Shared.LeanAutoExportService.yml
│   │   │   ├── Meridian.Ui.Shared.LeanSymbolMapper.yml
│   │   │   ├── Meridian.Ui.Shared.LoginSessionService.yml
│   │   │   ├── Meridian.Ui.Shared.Services.BackfillCoordinator.yml
│   │   │   ├── Meridian.Ui.Shared.Services.BackfillPreviewResult.yml
│   │   │   ├── Meridian.Ui.Shared.Services.ConfigStore.yml
│   │   │   ├── Meridian.Ui.Shared.Services.ConfigStoreExtensions.yml
│   │   │   ├── Meridian.Ui.Shared.Services.ExistingDataInfo.yml
│   │   │   ├── Meridian.Ui.Shared.Services.SecurityMasterSecurityReferenceLookup.yml
│   │   │   ├── Meridian.Ui.Shared.Services.SymbolPreview.yml
│   │   │   ├── Meridian.Ui.Shared.Services.yml
│   │   │   ├── Meridian.Ui.Shared.UserAccountConfig.yml
│   │   │   ├── Meridian.Ui.Shared.UserProfile.yml
│   │   │   ├── Meridian.Ui.Shared.UserProfileRegistry.yml
│   │   │   ├── Meridian.Ui.Shared.yml
│   │   │   ├── Meridian.UiServer.yml
│   │   │   ├── Meridian.yml
│   │   │   └── toc.yml
│   │   ├── docfx-log.json
│   │   ├── filterConfig.yml
│   │   ├── README.md
│   │   └── temp-metadata-only.json
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
│   │   ├── project-context.md
│   │   ├── project-dependencies.md
│   │   ├── provider-registry.md
│   │   ├── README.md
│   │   ├── repository-structure.md
│   │   └── workflows-overview.md
│   ├── getting-started
│   │   ├── pilot-operator-quickstart.md
│   │   └── README.md
│   ├── integrations
│   │   ├── fsharp-integration.md
│   │   ├── language-strategy.md
│   │   ├── lean-integration.md
│   │   └── README.md
│   ├── operations
│   │   ├── deployment.md
│   │   ├── governance-operator-workflow.md
│   │   ├── high-availability.md
│   │   ├── live-execution-controls.md
│   │   ├── msix-packaging.md
│   │   ├── operator-runbook.md
│   │   ├── performance-tuning.md
│   │   ├── portable-data-packager.md
│   │   ├── preflight-checklist.md
│   │   ├── README.md
│   │   └── service-level-objectives.md
│   ├── plans
│   │   ├── assembly-performance-roadmap.md
│   │   ├── backtest-studio-unification-blueprint.md
│   │   ├── backtest-studio-unification-pr-sequenced-roadmap.md
│   │   ├── codebase-audit-cleanup-roadmap.md
│   │   ├── fund-management-module-implementation-backlog.md
│   │   ├── fund-management-pr-sequenced-roadmap.md
│   │   ├── fund-management-product-vision-and-capability-matrix.md
│   │   ├── governance-fund-ops-blueprint.md
│   │   ├── l3-inference-implementation-plan.md
│   │   ├── meridian-6-week-roadmap.md
│   │   ├── meridian-database-blueprint.md
│   │   ├── options-roadmap.md
│   │   ├── provider-reliability-data-confidence-wave-1-blueprint.md
│   │   ├── quant-script-environment-blueprint.md
│   │   ├── quant-script-page-implementation-guide.md
│   │   ├── quantscript-l3-multiinstance-round2-roadmap.md
│   │   ├── readability-refactor-baseline.md
│   │   ├── readability-refactor-roadmap.md
│   │   ├── readability-refactor-technical-design-pack.md
│   │   ├── README.md
│   │   ├── security-master-productization-roadmap.md
│   │   ├── trading-workstation-migration-blueprint.md
│   │   ├── ufl-bond-target-state-v2.md
│   │   ├── ufl-cash-sweep-target-state-v2.md
│   │   ├── ufl-certificate-of-deposit-target-state-v2.md
│   │   ├── ufl-cfd-target-state-v2.md
│   │   ├── ufl-commercial-paper-target-state-v2.md
│   │   ├── ufl-commodity-target-state-v2.md
│   │   ├── ufl-crypto-target-state-v2.md
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
│   │   ├── ufl-warrant-target-state-v2.md
│   │   ├── workstation-release-readiness-blueprint.md
│   │   └── workstation-sprint-1-implementation-backlog.md
│   ├── providers
│   │   ├── alpaca-setup.md
│   │   ├── backfill-guide.md
│   │   ├── data-sources.md
│   │   ├── interactive-brokers-free-equity-reference.md
│   │   ├── interactive-brokers-setup.md
│   │   ├── provider-comparison.md
│   │   ├── provider-confidence-baseline.md
│   │   ├── README.md
│   │   ├── security-master-guide.md
│   │   └── stocksharp-connectors.md
│   ├── reference
│   │   ├── api-reference.md
│   │   ├── brand-assets.md
│   │   ├── data-dictionary.md
│   │   ├── data-uniformity.md
│   │   ├── design-review-memo.md
│   │   ├── environment-variables.md
│   │   ├── open-source-references.md
│   │   └── README.md
│   ├── screenshots
│   │   ├── desktop
│   │   │   ├── wpf-backfill.png
│   │   │   ├── wpf-backtest.png
│   │   │   ├── wpf-dashboard.png
│   │   │   ├── wpf-data-browser.png
│   │   │   ├── wpf-data-quality.png
│   │   │   ├── wpf-diagnostics.png
│   │   │   ├── wpf-live-data.png
│   │   │   ├── wpf-provider-health.png
│   │   │   ├── wpf-providers.png
│   │   │   ├── wpf-quant-script.png
│   │   │   ├── wpf-security-master.png
│   │   │   ├── wpf-settings.png
│   │   │   ├── wpf-storage.png
│   │   │   ├── wpf-strategy-runs.png
│   │   │   └── wpf-symbols.png
│   │   ├── 01-dashboard.png
│   │   ├── 02-workstation.png
│   │   ├── 03-swagger.png
│   │   ├── 04-status-overview.png
│   │   ├── 05-data-source.png
│   │   ├── 06-data-sources.png
│   │   ├── 07-backfill.png
│   │   ├── 08-derivatives.png
│   │   ├── 09-symbols.png
│   │   ├── 10-status-section.png
│   │   ├── 10-status.png
│   │   ├── 11-login.png
│   │   ├── 12-workstation-research.png
│   │   ├── 12-workstation-trading.png
│   │   ├── 13-workstation-data-operations.png
│   │   ├── 13-workstation-trading.png
│   │   ├── 14-workstation-data-operations.png
│   │   ├── 14-workstation-governance.png
│   │   ├── 14-workstation-trading-orders.png
│   │   ├── 15-workstation-governance.png
│   │   ├── 15-workstation-trading-orders.png
│   │   ├── 15-workstation-trading-positions.png
│   │   ├── 16-workstation-trading-positions.png
│   │   ├── 16-workstation-trading-risk.png
│   │   ├── 17-workstation-data-operations-providers.png
│   │   ├── 17-workstation-trading-risk.png
│   │   ├── 18-workstation-data-operations-backfills.png
│   │   ├── 18-workstation-data-operations-providers.png
│   │   ├── 19-workstation-data-operations-backfills.png
│   │   ├── 19-workstation-data-operations-exports.png
│   │   ├── 20-workstation-data-operations-exports.png
│   │   ├── 20-workstation-governance-ledger.png
│   │   ├── 21-workstation-governance-ledger.png
│   │   ├── 21-workstation-governance-reconciliation.png
│   │   ├── 22-workstation-governance-reconciliation.png
│   │   ├── 22-workstation-governance-security-master.png
│   │   ├── 23-workstation-governance-security-master.png
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
│   │   ├── EVALUATIONS_AND_AUDITS.md
│   │   ├── example-validation.md
│   │   ├── FEATURE_INVENTORY.md
│   │   ├── FULL_IMPLEMENTATION_TODO_2026_03_20.md
│   │   ├── health-dashboard.md
│   │   ├── IMPROVEMENTS.md
│   │   ├── link-repair-report.md
│   │   ├── metrics-dashboard.md
│   │   ├── OPPORTUNITY_SCAN.md
│   │   ├── production-status.md
│   │   ├── provider-validation-matrix.md
│   │   ├── README.md
│   │   ├── ROADMAP.md
│   │   ├── ROADMAP_COMBINED.md
│   │   ├── rules-report.md
│   │   ├── TARGET_END_PRODUCT.md
│   │   └── TODO.md
│   ├── DEPENDENCIES.md
│   ├── HELP.md
│   ├── README.md
│   ├── toc.yml
│   └── WORKFLOW_GUIDE.md
├── issues
│   ├── phase-1-5-add-equityclassification-discriminator-and-preferredterms-domain-model.md
│   └── phase_1_5_1_add_equityclassification_discriminator_and_preferredterms_domain_model.md
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
├── plugins
│   └── csharp-dotnet-development
│       ├── .github
│       │   └── plugin
│       │       └── plugin.json
│       ├── agents
│       │   └── expert-dotnet-software-engineer.md
│       ├── skills
│       │   ├── aspnet-minimal-api-openapi
│       │   │   └── SKILL.md
│       │   ├── csharp-async
│       │   │   └── SKILL.md
│       │   ├── csharp-mstest
│       │   │   └── SKILL.md
│       │   ├── csharp-nunit
│       │   │   └── SKILL.md
│       │   ├── csharp-tunit
│       │   │   └── SKILL.md
│       │   ├── csharp-xunit
│       │   │   └── SKILL.md
│       │   ├── dotnet-best-practices
│       │   │   └── SKILL.md
│       │   └── dotnet-upgrade
│       │       └── SKILL.md
│       └── README.md
├── PROJECTS
│   └── Phase_1.5_Preferred_and_Convertible_Equity_Support.md
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
│   │   ├── capture-desktop-screenshots.ps1
│   │   ├── cleanup-generated.ps1
│   │   ├── desktop-dev.ps1
│   │   ├── desktop-workflows.json
│   │   ├── diagnose-uwp-xaml.ps1
│   │   ├── generate-desktop-user-manual.ps1
│   │   ├── install-git-hooks.sh
│   │   ├── run-desktop-workflow.ps1
│   │   └── run-desktop.ps1
│   ├── lib
│   │   ├── ui-diagram-generator.mjs
│   │   └── ui-diagram-generator.test.mjs
│   ├── compare_benchmarks.py
│   ├── example-sharpe.csx
│   ├── generate-diagrams.mjs
│   ├── report_canonicalization_drift.py
│   └── wpf_finance_ux_checks.py
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
│   │   ├── app.ico
│   │   ├── app.manifest
│   │   ├── DashboardServerBridge.cs
│   │   ├── GlobalUsings.cs
│   │   ├── HostedBrokerageGatewayServiceCollectionExtensions.cs
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
│   │   │   ├── HistoricalBackfillService.cs
│   │   │   └── SymbolValidationSignal.cs
│   │   ├── Backtesting
│   │   │   └── BacktestStudioContracts.cs
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
│   │   │   ├── SecurityMasterCommands.cs
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
│   │   │   │   ├── LedgerFeatureRegistration.cs
│   │   │   │   ├── MaintenanceFeatureRegistration.cs
│   │   │   │   ├── PipelineFeatureRegistration.cs
│   │   │   │   ├── ProviderFeatureRegistration.cs
│   │   │   │   ├── ProviderRoutingFeatureRegistration.cs
│   │   │   │   ├── StorageFeatureRegistration.cs
│   │   │   │   └── SymbolManagementFeatureRegistration.cs
│   │   │   ├── Startup
│   │   │   │   ├── ModeRunners
│   │   │   │   │   ├── BackfillModeRunner.cs
│   │   │   │   │   ├── CollectorModeRunner.cs
│   │   │   │   │   ├── CommandModeRunner.cs
│   │   │   │   │   ├── DesktopModeRunner.cs
│   │   │   │   │   └── WebModeRunner.cs
│   │   │   │   ├── StartupModels
│   │   │   │   │   ├── HostMode.cs
│   │   │   │   │   ├── StartupContext.cs
│   │   │   │   │   ├── StartupPlan.cs
│   │   │   │   │   ├── StartupRequest.cs
│   │   │   │   │   └── StartupValidationResult.cs
│   │   │   │   ├── SharedStartupBootstrapper.cs
│   │   │   │   └── StartupOrchestrator.cs
│   │   │   ├── CircuitBreakerCallbackRouter.cs
│   │   │   ├── DirectLendingStartup.cs
│   │   │   ├── FundAccountsStartup.cs
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
│   │   │   ├── CredentialPlaceholderDetector.cs
│   │   │   ├── DefaultConfigPathResolver.cs
│   │   │   ├── DeploymentContext.cs
│   │   │   ├── IConfigValidator.cs
│   │   │   ├── SensitiveValueMasker.cs
│   │   │   ├── StorageConfigExtensions.cs
│   │   │   └── StorageConfigRules.cs
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
│   │   ├── FundAccounts
│   │   │   ├── IFundAccountService.cs
│   │   │   └── InMemoryFundAccountService.cs
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
│   │   ├── ProviderRouting
│   │   │   ├── ProviderBindingService.cs
│   │   │   ├── ProviderConnectionService.cs
│   │   │   ├── ProviderOperationsSupportServices.cs
│   │   │   ├── ProviderRoutingEngine.cs
│   │   │   └── ProviderRoutingMapper.cs
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
│   │   │   ├── ILivePositionCorporateActionAdjuster.cs
│   │   │   ├── ISecurityMasterQueryService.cs
│   │   │   ├── ISecurityMasterService.cs
│   │   │   ├── ISecurityResolver.cs
│   │   │   ├── NullSecurityMasterServices.cs
│   │   │   ├── SecurityEconomicDefinitionAdapter.cs
│   │   │   ├── SecurityMasterAggregateRebuilder.cs
│   │   │   ├── SecurityMasterCanonicalSymbolSeedService.cs
│   │   │   ├── SecurityMasterConflictService.cs
│   │   │   ├── SecurityMasterCsvParser.cs
│   │   │   ├── SecurityMasterImportService.cs
│   │   │   ├── SecurityMasterLedgerBridge.cs
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
│   │   ├── Treasury
│   │   │   ├── IMmfLiquidityService.cs
│   │   │   ├── IMoneyMarketFundService.cs
│   │   │   └── InMemoryMoneyMarketFundService.cs
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
│   │   │   ├── LinkedListExtensions.cs
│   │   │   └── SimulatedPortfolio.cs
│   │   ├── BacktestStudioRunOrchestrator.cs
│   │   ├── BatchBacktestService.cs
│   │   ├── CorporateActionAdjustmentService.cs
│   │   ├── GlobalUsings.cs
│   │   ├── ICorporateActionAdjustmentService.cs
│   │   ├── Meridian.Backtesting.csproj
│   │   └── MeridianNativeBacktestStudioEngine.cs
│   ├── Meridian.Backtesting.Sdk
│   │   ├── Ledger
│   │   │   ├── BacktestLedger.cs
│   │   │   ├── JournalEntry.cs
│   │   │   ├── LedgerAccount.cs
│   │   │   ├── LedgerAccounts.cs
│   │   │   ├── LedgerAccountType.cs
│   │   │   └── LedgerEntry.cs
│   │   ├── Strategies
│   │   │   ├── AdvancedCarry
│   │   │   │   ├── AdvancedCarryDecisionEngine.cs
│   │   │   │   └── AdvancedCarryModels.cs
│   │   │   └── OptionsOverwrite
│   │   │       ├── BlackScholesCalculator.cs
│   │   │       ├── CoveredCallOverwriteStrategy.cs
│   │   │       ├── OptionsOverwriteFilters.cs
│   │   │       ├── OptionsOverwriteMetricsCalculator.cs
│   │   │       ├── OptionsOverwriteModels.cs
│   │   │       ├── OptionsOverwriteParams.cs
│   │   │       └── OptionsOverwriteScoring.cs
│   │   ├── AssetEvent.cs
│   │   ├── BacktestEngineMode.cs
│   │   ├── BacktestProgressEvent.cs
│   │   ├── BacktestRequest.cs
│   │   ├── BacktestResult.cs
│   │   ├── CashFlowEntry.cs
│   │   ├── ClosedLot.cs
│   │   ├── FillEvent.cs
│   │   ├── FinancialAccount.cs
│   │   ├── FinancialAccountSnapshot.cs
│   │   ├── GlobalUsings.cs
│   │   ├── IBacktestContext.cs
│   │   ├── IBacktestStrategy.cs
│   │   ├── LotSelectionMethod.cs
│   │   ├── Meridian.Backtesting.Sdk.csproj
│   │   ├── OpenLot.cs
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
│   │   │   ├── LeanApiModels.cs
│   │   │   ├── LiveDataModels.cs
│   │   │   ├── OptionsModels.cs
│   │   │   ├── ProviderCatalog.cs
│   │   │   ├── ProviderRoutingApiModels.cs
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
│   │   │   ├── ProviderConnectionsConfigDto.cs
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
│   │   │   ├── IPositionSnapshotStore.cs
│   │   │   ├── MarketDataModels.cs
│   │   │   ├── ProviderId.cs
│   │   │   ├── ProviderSymbol.cs
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
│   │   │   ├── AccountManagementDtos.cs
│   │   │   ├── AccountManagementOptions.cs
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
│   │   │   ├── ISecurityMasterRuntimeStatus.cs
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
│   │   ├── Treasury
│   │   │   └── MoneyMarketFundDtos.cs
│   │   ├── Workstation
│   │   │   ├── FundLedgerDtos.cs
│   │   │   ├── FundOperationsDtos.cs
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
│   │   │   ├── ProviderConnectionsConfig.cs
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
│   │   ├── Allocation
│   │   │   ├── AllocationResult.cs
│   │   │   ├── AllocationRule.cs
│   │   │   ├── BlockTradeAllocator.cs
│   │   │   ├── IAllocationEngine.cs
│   │   │   └── ProportionalAllocationEngine.cs
│   │   ├── Derivatives
│   │   │   ├── FuturePosition.cs
│   │   │   ├── IDerivativePosition.cs
│   │   │   └── OptionPosition.cs
│   │   ├── Events
│   │   │   ├── ITradeEventPublisher.cs
│   │   │   ├── LedgerPostingConsumer.cs
│   │   │   └── TradeExecutedEvent.cs
│   │   ├── Exceptions
│   │   │   └── UnsupportedOrderRequestException.cs
│   │   ├── Interfaces
│   │   │   ├── IAccountPortfolio.cs
│   │   │   ├── IExecutionContext.cs
│   │   │   ├── ILiveFeedAdapter.cs
│   │   │   └── IOrderGateway.cs
│   │   ├── Margin
│   │   │   ├── IMarginModel.cs
│   │   │   ├── MarginAccountType.cs
│   │   │   ├── MarginCallStatus.cs
│   │   │   ├── MarginRequirement.cs
│   │   │   ├── PortfolioMarginModel.cs
│   │   │   └── RegTMarginModel.cs
│   │   ├── Models
│   │   │   ├── AccountKind.cs
│   │   │   ├── ExecutionMode.cs
│   │   │   ├── ExecutionPosition.cs
│   │   │   ├── IMultiAccountPortfolioState.cs
│   │   │   ├── IPortfolioState.cs
│   │   │   ├── OrderAcknowledgement.cs
│   │   │   ├── OrderGatewayCapabilities.cs
│   │   │   ├── OrderStatus.cs
│   │   │   └── OrderStatusUpdate.cs
│   │   ├── MultiCurrency
│   │   │   ├── FxRate.cs
│   │   │   ├── IFxRateProvider.cs
│   │   │   └── MultiCurrencyCashBalance.cs
│   │   ├── Serialization
│   │   │   └── ExecutionJsonContext.cs
│   │   ├── Services
│   │   │   ├── ExecutionAuditTrailService.cs
│   │   │   ├── ExecutionOperatorControlService.cs
│   │   │   ├── IPaperSessionStore.cs
│   │   │   ├── JsonlFilePaperSessionStore.cs
│   │   │   ├── OrderLifecycleManager.cs
│   │   │   ├── PaperSessionOptions.cs
│   │   │   ├── PaperSessionPersistenceService.cs
│   │   │   ├── PaperTradingPortfolio.cs
│   │   │   ├── PortfolioRegistry.cs
│   │   │   ├── PositionReconciliationService.cs
│   │   │   └── PositionSyncOptions.cs
│   │   ├── TaxLotAccounting
│   │   │   ├── ITaxLotSelector.cs
│   │   │   ├── TaxLotAccountingMethod.cs
│   │   │   ├── TaxLotRelief.cs
│   │   │   └── TaxLotSelectors.cs
│   │   ├── BrokerageServiceRegistration.cs
│   │   ├── GlobalUsings.cs
│   │   ├── IRiskValidator.cs
│   │   ├── ISecurityMasterGate.cs
│   │   ├── Meridian.Execution.csproj
│   │   ├── OrderManagementSystem.cs
│   │   ├── PaperExecutionContext.cs
│   │   ├── PaperTradingGateway.cs
│   │   └── SecurityMasterGate.cs
│   ├── Meridian.Execution.Sdk
│   │   ├── Derivatives
│   │   │   ├── FutureDetails.cs
│   │   │   ├── OptionDetails.cs
│   │   │   └── OptionGreeks.cs
│   │   ├── BrokerageConfiguration.cs
│   │   ├── IBrokerageGateway.cs
│   │   ├── IBrokeragePositionSync.cs
│   │   ├── IExecutionGateway.cs
│   │   ├── IOrderManager.cs
│   │   ├── IPosition.cs
│   │   ├── IPositionTracker.cs
│   │   ├── Meridian.Execution.Sdk.csproj
│   │   ├── Models.cs
│   │   ├── PositionExtensions.cs
│   │   └── TaxLot.cs
│   ├── Meridian.FSharp
│   │   ├── Calculations
│   │   │   ├── Aggregations.fs
│   │   │   ├── Imbalance.fs
│   │   │   └── Spread.fs
│   │   ├── Canonicalization
│   │   │   └── MappingRules.fs
│   │   ├── Domain
│   │   │   ├── AccountStatements.fs
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
│   │   ├── Interop.AccountDetails.fs
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
│   │   │   │   ├── AlpacaCorporateActionProvider.cs
│   │   │   │   ├── AlpacaHistoricalDataProvider.cs
│   │   │   │   ├── AlpacaMarketDataClient.cs
│   │   │   │   ├── AlpacaOptionsChainProvider.cs
│   │   │   │   ├── AlpacaProviderModule.cs
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
│   │   │   │   ├── ICorporateActionProvider.cs
│   │   │   │   ├── IHistoricalDataProvider.cs
│   │   │   │   ├── ISymbolSearchProvider.cs
│   │   │   │   ├── ProviderBehaviorBuilder.cs
│   │   │   │   ├── ProviderFactory.cs
│   │   │   │   ├── ProviderRegistry.cs
│   │   │   │   ├── ProviderServiceExtensions.cs
│   │   │   │   ├── ProviderSubscriptionRanges.cs
│   │   │   │   ├── ProviderTemplate.cs
│   │   │   │   ├── ResponseHandler.cs
│   │   │   │   ├── SymbolSearchUtility.cs
│   │   │   │   └── WebSocketProviderBase.cs
│   │   │   ├── Edgar
│   │   │   │   ├── EdgarSecurityMasterIngestProvider.cs
│   │   │   │   └── EdgarSymbolSearchProvider.cs
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
│   │   │   │   ├── PolygonOptionsChainProvider.cs
│   │   │   │   ├── PolygonSecurityMasterIngestProvider.cs
│   │   │   │   ├── PolygonSymbolSearchProvider.cs
│   │   │   │   └── TradingParametersBackfillService.cs
│   │   │   ├── Robinhood
│   │   │   │   ├── RobinhoodBrokerageGateway.cs
│   │   │   │   ├── RobinhoodHistoricalDataProvider.cs
│   │   │   │   ├── RobinhoodMarketDataClient.cs
│   │   │   │   ├── RobinhoodOptionsChainProvider.cs
│   │   │   │   ├── RobinhoodSymbolSearchModels.cs
│   │   │   │   └── RobinhoodSymbolSearchProvider.cs
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
│   │   │   │   ├── SyntheticOptionsChainProvider.cs
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
│   │   ├── ProjectLedgerBook.cs
│   │   └── ReadOnlyCollectionHelpers.cs
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
│   │   ├── Navigation
│   │   │   └── RepoNavigationCatalog.cs
│   │   ├── Prompts
│   │   │   └── MarketDataPrompts.cs
│   │   ├── Resources
│   │   │   ├── MarketDataResources.cs
│   │   │   └── RepoNavigationResources.cs
│   │   ├── Tools
│   │   │   ├── BackfillTools.cs
│   │   │   ├── ProviderTools.cs
│   │   │   ├── RepoNavigationTools.cs
│   │   │   ├── StorageTools.cs
│   │   │   └── SymbolTools.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.McpServer.csproj
│   │   └── Program.cs
│   ├── Meridian.ProviderSdk
│   │   ├── AttributeCredentialResolver.cs
│   │   ├── CredentialSchemaRegistry.cs
│   │   ├── CredentialValidator.cs
│   │   ├── DataSourceAttribute.cs
│   │   ├── DataSourceRegistry.cs
│   │   ├── HistoricalDataCapabilities.cs
│   │   ├── ICredentialContext.cs
│   │   ├── IDataSource.cs
│   │   ├── IHistoricalBarWriter.cs
│   │   ├── IHistoricalDataSource.cs
│   │   ├── IMarketDataClient.cs
│   │   ├── ImplementsAdrAttribute.cs
│   │   ├── IOptionsChainProvider.cs
│   │   ├── IProviderFamilyAdapter.cs
│   │   ├── IProviderMetadata.cs
│   │   ├── IProviderModule.cs
│   │   ├── IRealtimeDataSource.cs
│   │   ├── Meridian.ProviderSdk.csproj
│   │   ├── ProviderHttpUtilities.cs
│   │   ├── ProviderModuleLoader.cs
│   │   ├── ProviderRoutingModels.cs
│   │   └── RequiresCredentialAttribute.cs
│   ├── Meridian.QuantScript
│   │   ├── Api
│   │   │   ├── BacktestProxy.cs
│   │   │   ├── DataProxy.cs
│   │   │   ├── EfficientFrontierConstraints.cs
│   │   │   ├── IQuantDataContext.cs
│   │   │   ├── LambdaBacktestStrategy.cs
│   │   │   ├── PortfolioBuilder.cs
│   │   │   ├── PriceBar.cs
│   │   │   ├── PriceSeries.cs
│   │   │   ├── PriceSeriesExtensions.cs
│   │   │   ├── QuantDataContext.cs
│   │   │   ├── ReturnSeries.cs
│   │   │   ├── ScriptModels.cs
│   │   │   ├── ScriptParamAttribute.cs
│   │   │   ├── StatisticsEngine.cs
│   │   │   └── TechnicalSeriesExtensions.cs
│   │   ├── Compilation
│   │   │   ├── Contracts.cs
│   │   │   ├── IQuantScriptCompiler.cs
│   │   │   ├── IScriptRunner.cs
│   │   │   ├── NotebookExecutionSession.cs
│   │   │   ├── QuantScriptGlobals.cs
│   │   │   ├── RoslynScriptCompiler.cs
│   │   │   ├── ScriptExecutionCheckpoint.cs
│   │   │   ├── ScriptRunner.cs
│   │   │   └── ScriptRunResult.cs
│   │   ├── Documents
│   │   │   ├── IQuantScriptNotebookStore.cs
│   │   │   ├── QuantScriptDocumentModels.cs
│   │   │   └── QuantScriptNotebookStore.cs
│   │   ├── Plotting
│   │   │   ├── PlotQueue.cs
│   │   │   ├── PlotRequest.cs
│   │   │   └── PlotType.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.QuantScript.csproj
│   │   ├── QuantScriptOptions.cs
│   │   └── ScriptContext.cs
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
│   │   ├── FundAccounts
│   │   │   ├── Migrations
│   │   │   │   └── 001_fund_accounts.sql
│   │   │   └── IFundAccountStore.cs
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
│   │   │   ├── JsonlPositionSnapshotStore.cs
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
│   │   │   ├── AggregatePortfolioService.cs
│   │   │   ├── CashFlowProjectionService.cs
│   │   │   ├── IAggregatePortfolioService.cs
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
│   │   │   │   │   │   ├── equity-curve-chart.test.tsx
│   │   │   │   │   │   ├── equity-curve-chart.tsx
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
│   │   │   │   │   ├── api.trading.test.ts
│   │   │   │   │   ├── api.ts
│   │   │   │   │   ├── utils.ts
│   │   │   │   │   └── workspace.ts
│   │   │   │   ├── screens
│   │   │   │   │   ├── data-operations-screen.test.tsx
│   │   │   │   │   ├── data-operations-screen.tsx
│   │   │   │   │   ├── governance-screen.test.tsx
│   │   │   │   │   ├── governance-screen.tsx
│   │   │   │   │   ├── overview-screen.tsx
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
│   │   │       │   ├── index-BUqJ19Wh.css
│   │   │       │   └── index-VxIQhfQW.js
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
│   │   │   ├── FixtureScenario.cs
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
│   │   │   ├── ProviderOperationsResults.cs
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
│   │   │   ├── CredentialEndpoints.cs
│   │   │   ├── CronEndpoints.cs
│   │   │   ├── DiagnosticsEndpoints.cs
│   │   │   ├── DirectLendingEndpoints.cs
│   │   │   ├── EndpointHelpers.cs
│   │   │   ├── ExecutionEndpoints.cs
│   │   │   ├── ExportEndpoints.cs
│   │   │   ├── FailoverEndpoints.cs
│   │   │   ├── FundAccountEndpoints.cs
│   │   │   ├── HealthEndpoints.cs
│   │   │   ├── HistoricalEndpoints.cs
│   │   │   ├── IBEndpoints.cs
│   │   │   ├── IngestionJobEndpoints.cs
│   │   │   ├── LeanEndpoints.cs
│   │   │   ├── LiveDataEndpoints.cs
│   │   │   ├── LoginSessionMiddleware.cs
│   │   │   ├── MaintenanceScheduleEndpoints.cs
│   │   │   ├── MessagingEndpoints.cs
│   │   │   ├── MoneyMarketFundEndpoints.cs
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
│   │   ├── Serialization
│   │   │   └── DirectLendingJsonContext.cs
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
│   │   ├── StaticAssetPathResolver.cs
│   │   └── UserProfileRegistry.cs
│   └── Meridian.Wpf
│       ├── Assets
│       │   ├── Brand
│       │   │   ├── meridian-hero.svg
│       │   │   ├── meridian-mark.svg
│       │   │   ├── meridian-tile-256.png
│       │   │   ├── meridian-tile.svg
│       │   │   └── meridian-wordmark.svg
│       │   ├── Icons
│       │   │   ├── account-portfolio.svg
│       │   │   ├── admin-maintenance.svg
│       │   │   ├── aggregate-portfolio.svg
│       │   │   ├── archive-health.svg
│       │   │   ├── backfill.svg
│       │   │   ├── backtest.svg
│       │   │   ├── charting.svg
│       │   │   ├── collection-sessions.svg
│       │   │   ├── dashboard.svg
│       │   │   ├── data-browser.svg
│       │   │   ├── data-calendar.svg
│       │   │   ├── data-export.svg
│       │   │   ├── data-operations.svg
│       │   │   ├── data-quality.svg
│       │   │   ├── data-sampling.svg
│       │   │   ├── data-sources.svg
│       │   │   ├── diagnostics.svg
│       │   │   ├── event-replay.svg
│       │   │   ├── governance.svg
│       │   │   ├── help.svg
│       │   │   ├── index-subscription.svg
│       │   │   ├── keyboard-shortcuts.svg
│       │   │   ├── lean-integration.svg
│       │   │   ├── live-data.svg
│       │   │   ├── order-book.svg
│       │   │   ├── portfolio-import.svg
│       │   │   ├── provider-health.svg
│       │   │   ├── README.md
│       │   │   ├── research.svg
│       │   │   ├── retention-assurance.svg
│       │   │   ├── run-detail.svg
│       │   │   ├── run-ledger.svg
│       │   │   ├── run-mat.svg
│       │   │   ├── run-portfolio.svg
│       │   │   ├── schedule-manager.svg
│       │   │   ├── security-master.svg
│       │   │   ├── service-manager.svg
│       │   │   ├── settings.svg
│       │   │   ├── storage-optimization.svg
│       │   │   ├── storage.svg
│       │   │   ├── strategy-runs.svg
│       │   │   ├── symbol-storage.svg
│       │   │   ├── symbols.svg
│       │   │   ├── system-health.svg
│       │   │   ├── trading-hours.svg
│       │   │   ├── trading.svg
│       │   │   └── watchlist.svg
│       │   └── app.ico
│       ├── Behaviors
│       │   ├── AvalonEditNotebookBehavior.cs
│       │   ├── ParameterTemplateSelector.cs
│       │   └── PlotRenderBehavior.cs
│       ├── Contracts
│       │   ├── IConnectionService.cs
│       │   └── INavigationService.cs
│       ├── Converters
│       │   ├── BoolToStringConverter.cs
│       │   ├── BoolToVisibilityConverter.cs
│       │   ├── ConsoleEntryKindToBrushConverter.cs
│       │   ├── CountToVisibilityConverter.cs
│       │   ├── IntToVisibilityConverter.cs
│       │   ├── InvertBoolConverter.cs
│       │   ├── NullToCollapsedConverter.cs
│       │   ├── StringToBoolConverter.cs
│       │   └── StringToVisibilityConverter.cs
│       ├── Models
│       │   ├── ActionEntry.cs
│       │   ├── ActivityLogModels.cs
│       │   ├── AlignmentModels.cs
│       │   ├── AppConfig.cs
│       │   ├── BackfillModels.cs
│       │   ├── BlotterModels.cs
│       │   ├── DashboardModels.cs
│       │   ├── DataQualityModels.cs
│       │   ├── FundProfileModels.cs
│       │   ├── FundReconciliationWorkbenchModels.cs
│       │   ├── LeanModels.cs
│       │   ├── LiveDataModels.cs
│       │   ├── NotificationModels.cs
│       │   ├── OrderBookModels.cs
│       │   ├── PaneDropEventArgs.cs
│       │   ├── PaneLayout.cs
│       │   ├── ProviderHealthModels.cs
│       │   ├── QuantScriptModels.cs
│       │   ├── SettingsModels.cs
│       │   ├── StorageDisplayModels.cs
│       │   ├── SymbolsModels.cs
│       │   ├── WatchlistModels.cs
│       │   ├── WorkspaceDefinition.cs
│       │   ├── WorkspaceRegistry.cs
│       │   ├── WorkspaceShellChromeModels.cs
│       │   └── WorkspaceShellModels.cs
│       ├── Services
│       │   ├── AgentLoopService.cs
│       │   ├── ArchiveHealthService.cs
│       │   ├── BackendServiceManager.cs
│       │   ├── BackgroundTaskSchedulerService.cs
│       │   ├── BacktestService.cs
│       │   ├── BrushRegistry.cs
│       │   ├── CashFinancingReadService.cs
│       │   ├── ClipboardWatcherService.cs
│       │   ├── ConfigService.cs
│       │   ├── ConnectionService.cs
│       │   ├── ContextMenuService.cs
│       │   ├── CredentialService.cs
│       │   ├── DropImportService.cs
│       │   ├── ExportFormat.cs
│       │   ├── ExportPresetService.cs
│       │   ├── FirstRunService.cs
│       │   ├── FloatingPageService.cs
│       │   ├── FormValidationService.cs
│       │   ├── FundAccountReadService.cs
│       │   ├── FundContextService.cs
│       │   ├── FundLedgerReadService.cs
│       │   ├── FundProfileKeyTranslator.cs
│       │   ├── FundReconciliationWorkbenchService.cs
│       │   ├── GlobalHotkeyService.cs
│       │   ├── ICommandContextProvider.cs
│       │   ├── IFundProfileCatalog.cs
│       │   ├── InfoBarService.cs
│       │   ├── IQuantScriptLayoutService.cs
│       │   ├── JumpListService.cs
│       │   ├── KeyboardShortcutService.cs
│       │   ├── LoggingService.cs
│       │   ├── MessagingService.cs
│       │   ├── NavigationService.cs
│       │   ├── NotificationService.cs
│       │   ├── OfflineTrackingPersistenceService.cs
│       │   ├── PendingOperationsQueueService.cs
│       │   ├── QuantScriptLayoutService.cs
│       │   ├── ReconciliationReadService.cs
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
│       │   ├── WorkspaceService.cs
│       │   ├── WorkspaceShellContextService.cs
│       │   └── WorkstationReconciliationApiClient.cs
│       ├── Styles
│       │   ├── Animations.xaml
│       │   ├── AppStyles.xaml
│       │   ├── BrandResources.xaml
│       │   ├── IconResources.xaml
│       │   ├── ThemeControls.xaml
│       │   ├── ThemeSurfaces.xaml
│       │   ├── ThemeTokens.xaml
│       │   └── ThemeTypography.xaml
│       ├── ViewModels
│       │   ├── AccountPortfolioViewModel.cs
│       │   ├── ActivityLogViewModel.cs
│       │   ├── AddProviderWizardViewModel.cs
│       │   ├── AdminMaintenanceViewModel.cs
│       │   ├── AdvancedAnalyticsViewModel.cs
│       │   ├── AgentViewModel.cs
│       │   ├── AggregatePortfolioViewModel.cs
│       │   ├── AnalysisExportViewModel.cs
│       │   ├── AnalysisExportWizardViewModel.cs
│       │   ├── BackfillViewModel.cs
│       │   ├── BacktestViewModel.cs
│       │   ├── BatchBacktestViewModel.cs
│       │   ├── BindableBase.cs
│       │   ├── CashFlowViewModel.cs
│       │   ├── ChartingPageViewModel.cs
│       │   ├── ClusterStatusViewModel.cs
│       │   ├── CollectionSessionViewModel.cs
│       │   ├── CredentialManagementViewModel.cs
│       │   ├── DashboardViewModel.cs
│       │   ├── DataBrowserViewModel.cs
│       │   ├── DataCalendarViewModel.cs
│       │   ├── DataExportViewModel.cs
│       │   ├── DataQualityViewModel.cs
│       │   ├── DataSamplingViewModel.cs
│       │   ├── DataSourcesViewModel.cs
│       │   ├── DiagnosticsPageViewModel.cs
│       │   ├── DirectLendingViewModel.cs
│       │   ├── EventReplayViewModel.cs
│       │   ├── ExportPresetsViewModel.cs
│       │   ├── FundAccountProviderPanelModels.cs
│       │   ├── FundAccountsViewModel.cs
│       │   ├── FundLedgerViewModel.cs
│       │   ├── FundLedgerViewModel.Reconciliation.cs
│       │   ├── FundProfileSelectionViewModel.cs
│       │   ├── IndexSubscriptionViewModel.cs
│       │   ├── IPageActionBarProvider.cs
│       │   ├── LeanIntegrationViewModel.cs
│       │   ├── LiveDataViewerViewModel.cs
│       │   ├── MainPageViewModel.cs
│       │   ├── MainWindowViewModel.cs
│       │   ├── MessagingHubViewModel.cs
│       │   ├── NotificationCenterViewModel.cs
│       │   ├── OptionsViewModel.cs
│       │   ├── OrderBookHeatmapViewModel.cs
│       │   ├── OrderBookViewModel.cs
│       │   ├── PackageManagerViewModel.cs
│       │   ├── PluginManagementViewModel.cs
│       │   ├── PortfolioImportViewModel.cs
│       │   ├── PositionBlotterViewModel.cs
│       │   ├── ProviderHealthViewModel.cs
│       │   ├── ProviderPageModels.cs
│       │   ├── ProviderViewModel.cs
│       │   ├── QualityArchiveViewModel.cs
│       │   ├── QuantScriptViewModel.cs
│       │   ├── QuoteFloatViewModel.cs
│       │   ├── RunMatViewModel.cs
│       │   ├── RunRiskViewModel.cs
│       │   ├── ScatterAnalysisViewModel.cs
│       │   ├── SecurityMasterDeactivateViewModel.cs
│       │   ├── SecurityMasterEditViewModel.cs
│       │   ├── SecurityMasterViewModel.cs
│       │   ├── ServiceManagerViewModel.cs
│       │   ├── SettingsViewModel.cs
│       │   ├── SplitPaneViewModel.cs
│       │   ├── StatusBarViewModel.cs
│       │   ├── StorageViewModel.cs
│       │   ├── StrategyRunBrowserViewModel.cs
│       │   ├── StrategyRunDetailViewModel.cs
│       │   ├── StrategyRunLedgerViewModel.cs
│       │   ├── StrategyRunPortfolioViewModel.cs
│       │   ├── SymbolsPageViewModel.cs
│       │   ├── SystemHealthViewModel.cs
│       │   ├── TickerStripViewModel.cs
│       │   ├── TradingHoursViewModel.cs
│       │   ├── WatchlistViewModel.cs
│       │   └── WelcomePageViewModel.cs
│       ├── Views
│       │   ├── AccountPortfolioPage.xaml
│       │   ├── AccountPortfolioPage.xaml.cs
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
│       │   ├── AggregatePortfolioPage.xaml
│       │   ├── AggregatePortfolioPage.xaml.cs
│       │   ├── AnalysisExportPage.xaml
│       │   ├── AnalysisExportPage.xaml.cs
│       │   ├── AnalysisExportWizardPage.xaml
│       │   ├── AnalysisExportWizardPage.xaml.cs
│       │   ├── ApiKeyDialog.xaml
│       │   ├── ApiKeyDialog.xaml.cs
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
│       │   ├── CreateWatchlistDialog.cs
│       │   ├── CredentialManagementPage.xaml
│       │   ├── CredentialManagementPage.xaml.cs
│       │   ├── DashboardPage.xaml
│       │   ├── DashboardPage.xaml.cs
│       │   ├── DashboardWebPage.xaml
│       │   ├── DashboardWebPage.xaml.cs
│       │   ├── DataBrowserPage.xaml
│       │   ├── DataBrowserPage.xaml.cs
│       │   ├── DataCalendarPage.xaml
│       │   ├── DataCalendarPage.xaml.cs
│       │   ├── DataExportPage.xaml
│       │   ├── DataExportPage.xaml.cs
│       │   ├── DataOperationsWorkspaceShellPage.xaml
│       │   ├── DataOperationsWorkspaceShellPage.xaml.cs
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
│       │   ├── EditScheduledJobDialog.xaml
│       │   ├── EditScheduledJobDialog.xaml.cs
│       │   ├── EditWatchlistDialog.cs
│       │   ├── EventReplayPage.xaml
│       │   ├── EventReplayPage.xaml.cs
│       │   ├── ExportPresetsPage.xaml
│       │   ├── ExportPresetsPage.xaml.cs
│       │   ├── FloatingPageWindow.xaml
│       │   ├── FloatingPageWindow.xaml.cs
│       │   ├── FundAccountsPage.xaml
│       │   ├── FundAccountsPage.xaml.cs
│       │   ├── FundLedgerPage.xaml
│       │   ├── FundLedgerPage.xaml.cs
│       │   ├── FundProfileSelectionPage.xaml
│       │   ├── FundProfileSelectionPage.xaml.cs
│       │   ├── GovernanceWorkspaceShellPage.xaml
│       │   ├── GovernanceWorkspaceShellPage.xaml.cs
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
│       │   ├── MeridianDockingManager.xaml
│       │   ├── MeridianDockingManager.xaml.cs
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
│       │   ├── PositionBlotterPage.xaml
│       │   ├── PositionBlotterPage.xaml.cs
│       │   ├── ProviderHealthPage.xaml
│       │   ├── ProviderHealthPage.xaml.cs
│       │   ├── ProviderPage.xaml
│       │   ├── ProviderPage.xaml.cs
│       │   ├── QualityArchivePage.xaml
│       │   ├── QualityArchivePage.xaml.cs
│       │   ├── QuantScriptPage.xaml
│       │   ├── QuantScriptPage.xaml.cs
│       │   ├── QuoteFloatWindow.xaml
│       │   ├── QuoteFloatWindow.xaml.cs
│       │   ├── ResearchWorkspaceShellPage.xaml
│       │   ├── ResearchWorkspaceShellPage.xaml.cs
│       │   ├── RetentionAssurancePage.xaml
│       │   ├── RetentionAssurancePage.xaml.cs
│       │   ├── RunCashFlowPage.xaml
│       │   ├── RunCashFlowPage.xaml.cs
│       │   ├── RunDetailPage.xaml
│       │   ├── RunDetailPage.xaml.cs
│       │   ├── RunLedgerPage.xaml
│       │   ├── RunLedgerPage.xaml.cs
│       │   ├── RunMatPage.xaml
│       │   ├── RunMatPage.xaml.cs
│       │   ├── RunPortfolioPage.xaml
│       │   ├── RunPortfolioPage.xaml.cs
│       │   ├── RunRiskPage.xaml
│       │   ├── RunRiskPage.xaml.cs
│       │   ├── SaveWatchlistDialog.xaml
│       │   ├── SaveWatchlistDialog.xaml.cs
│       │   ├── ScatterAnalysisPage.xaml
│       │   ├── ScatterAnalysisPage.xaml.cs
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
│       │   ├── WorkspaceCommandBarControl.xaml
│       │   ├── WorkspaceCommandBarControl.xaml.cs
│       │   ├── WorkspacePage.xaml
│       │   ├── WorkspacePage.xaml.cs
│       │   ├── WorkspaceShellContextStripControl.xaml
│       │   └── WorkspaceShellContextStripControl.xaml.cs
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
│   │   ├── AdvancedCarryDecisionEngineTests.cs
│   │   ├── BacktestEngineIntegrationTests.cs
│   │   ├── BacktestMetricsEngineTests.cs
│   │   ├── BacktestRequestConfigTests.cs
│   │   ├── BracketOrderTests.cs
│   │   ├── CorporateActionAdjustmentServiceTests.cs
│   │   ├── FillModelExpansionTests.cs
│   │   ├── FillModelTests.cs
│   │   ├── GlobalUsings.cs
│   │   ├── LedgerQueryTests.cs
│   │   ├── LotLevelTrackingTests.cs
│   │   ├── MarketImpactFillModelTests.cs
│   │   ├── Meridian.Backtesting.Tests.csproj
│   │   ├── MeridianNativeBacktestStudioEngineTests.cs
│   │   ├── OptionsOverwriteStrategyTests.cs
│   │   ├── SimulatedPortfolioTests.cs
│   │   ├── TcaReporterTests.cs
│   │   ├── XirrCalculatorTests.cs
│   │   └── YahooFinanceBacktestIntegrationTests.cs
│   ├── Meridian.DirectLending.Tests
│   │   ├── BankTransactionSeedTests.cs
│   │   ├── DirectLendingDatabaseFactAttribute.cs
│   │   ├── DirectLendingPostgresIntegrationTests.cs
│   │   ├── DirectLendingPostgresTestDatabase.cs
│   │   ├── DirectLendingServiceTests.cs
│   │   ├── DirectLendingWorkflowTests.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.DirectLending.Tests.csproj
│   │   └── PaymentApprovalTests.cs
│   ├── Meridian.FSharp.Tests
│   │   ├── AccountDetailsTests.fs
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
│   │   │   ├── RepoNavigationToolsTests.cs
│   │   │   └── StorageToolsTests.cs
│   │   ├── GlobalUsings.cs
│   │   └── Meridian.McpServer.Tests.csproj
│   ├── Meridian.QuantScript.Tests
│   │   ├── Helpers
│   │   │   ├── FakeQuantDataContext.cs
│   │   │   ├── FakeScriptRunner.cs
│   │   │   └── TestPriceSeriesBuilder.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.QuantScript.Tests.csproj
│   │   ├── PlotQueueTests.cs
│   │   ├── PortfolioBuilderTests.cs
│   │   ├── PriceSeriesTests.cs
│   │   ├── QuantScriptNotebookStoreTests.cs
│   │   ├── RoslynScriptCompilerTests.cs
│   │   ├── ScriptRunnerTests.cs
│   │   └── StatisticsEngineTests.cs
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
│   │   │   ├── Backtesting
│   │   │   │   └── BacktestStudioRunOrchestratorTests.cs
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
│   │   │   │   ├── DirectLendingStartupTests.cs
│   │   │   │   ├── SecurityMasterStartupTests.cs
│   │   │   │   └── StorageFeatureRegistrationTests.cs
│   │   │   ├── Config
│   │   │   │   ├── ConfigJsonSchemaGeneratorTests.cs
│   │   │   │   ├── ConfigSchemaIntegrationTests.cs
│   │   │   │   ├── ConfigurationUnificationTests.cs
│   │   │   │   ├── ConfigValidationPipelineTests.cs
│   │   │   │   ├── ConfigValidatorTests.cs
│   │   │   │   └── ProviderCredentialResolverTests.cs
│   │   │   ├── Coordination
│   │   │   │   ├── ClusterCoordinatorServiceTests.cs
│   │   │   │   ├── LeaseManagerTests.cs
│   │   │   │   ├── SplitBrainDetectorTests.cs
│   │   │   │   └── SubscriptionOrchestratorCoordinationTests.cs
│   │   │   ├── Credentials
│   │   │   │   ├── CredentialStatusTests.cs
│   │   │   │   ├── CredentialTestingServiceTests.cs
│   │   │   │   └── OAuthTokenTests.cs
│   │   │   ├── DirectLending
│   │   │   │   └── DirectLendingOutboxDispatcherTests.cs
│   │   │   ├── Etl
│   │   │   │   ├── EtlJobDefinitionStoreTests.cs
│   │   │   │   ├── EtlJobOrchestratorTests.cs
│   │   │   │   └── EtlNormalizationServiceTests.cs
│   │   │   ├── FundAccounts
│   │   │   │   └── FundAccountServiceTests.cs
│   │   │   ├── Indicators
│   │   │   │   └── TechnicalIndicatorServiceTests.cs
│   │   │   ├── Monitoring
│   │   │   │   ├── DataQuality
│   │   │   │   │   ├── DataFreshnessSlaMonitorTests.cs
│   │   │   │   │   ├── DataQualityTests.cs
│   │   │   │   │   └── LiquidityProfileTests.cs
│   │   │   │   ├── AlertDispatcherTests.cs
│   │   │   │   ├── BackpressureAlertServiceTests.cs
│   │   │   │   ├── BadTickFilterTests.cs
│   │   │   │   ├── ClockSkewEstimatorTests.cs
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
│   │   │   ├── ProviderRouting
│   │   │   │   └── ProviderRoutingServiceTests.cs
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
│   │   │   ├── Ui
│   │   │   │   └── ConfigStoreTests.cs
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
│   │   │   ├── Enhancements
│   │   │   │   ├── AllocationEngineTests.cs
│   │   │   │   ├── DerivativePositionTests.cs
│   │   │   │   ├── EventDrivenDecouplingTests.cs
│   │   │   │   ├── MarginModelTests.cs
│   │   │   │   ├── MultiCurrencyTests.cs
│   │   │   │   └── TaxLotAccountingTests.cs
│   │   │   ├── BrokerageGatewayAdapterTests.cs
│   │   │   ├── MultiAccountPaperTradingPortfolioTests.cs
│   │   │   ├── OrderManagementSystemGovernanceTests.cs
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
│   │   │   ├── Http
│   │   │   │   └── HttpClientConfigurationTests.cs
│   │   │   ├── Providers
│   │   │   │   ├── Fixtures
│   │   │   │   │   ├── InteractiveBrokers
│   │   │   │   │   │   ├── ib_order_limit_buy_day.json
│   │   │   │   │   │   ├── ib_order_limit_buy_govt_gtc.json
│   │   │   │   │   │   ├── ib_order_limit_sell_fok.json
│   │   │   │   │   │   ├── ib_order_loc_sell_day.json
│   │   │   │   │   │   ├── ib_order_market_buy_bond_day.json
│   │   │   │   │   │   ├── ib_order_market_sell_gtc.json
│   │   │   │   │   │   ├── ib_order_moc_sell_day.json
│   │   │   │   │   │   ├── ib_order_stop_buy_ioc.json
│   │   │   │   │   │   ├── ib_order_stop_limit_buy_day.json
│   │   │   │   │   │   └── ib_order_trailing_stop_sell_gtc.json
│   │   │   │   │   └── Polygon
│   │   │   │   │       ├── polygon-recorded-session-aapl.json
│   │   │   │   │       ├── polygon-recorded-session-auth-failure-rate-limit.json
│   │   │   │   │       ├── polygon-recorded-session-gld-cboe-sell.json
│   │   │   │   │       ├── polygon-recorded-session-msft-edge.json
│   │   │   │   │       ├── polygon-recorded-session-nvda-multi-batch.json
│   │   │   │   │       ├── polygon-recorded-session-spy-etf.json
│   │   │   │   │       └── polygon-recorded-session-tsla-opening-cross.json
│   │   │   │   ├── AlpacaBrokerageGatewayTests.cs
│   │   │   │   ├── AlpacaCorporateActionProviderTests.cs
│   │   │   │   ├── AlpacaCredentialAndReconnectTests.cs
│   │   │   │   ├── AlpacaMessageParsingTests.cs
│   │   │   │   ├── AlpacaQuotePipelineGoldenTests.cs
│   │   │   │   ├── AlpacaQuoteRoutingTests.cs
│   │   │   │   ├── BackfillRetryAfterTests.cs
│   │   │   │   ├── EdgarSymbolSearchProviderTests.cs
│   │   │   │   ├── FailoverAwareMarketDataClientTests.cs
│   │   │   │   ├── FreeHistoricalProviderParsingTests.cs
│   │   │   │   ├── FreeProviderContractTests.cs
│   │   │   │   ├── HistoricalDataProviderContractTests.cs
│   │   │   │   ├── IBBrokerageGatewayTests.cs
│   │   │   │   ├── IBHistoricalProviderContractTests.cs
│   │   │   │   ├── IBMarketDataClientContractTests.cs
│   │   │   │   ├── IBOrderSampleTests.cs
│   │   │   │   ├── IBRuntimeGuidanceTests.cs
│   │   │   │   ├── IBSimulationClientContractTests.cs
│   │   │   │   ├── IBSimulationClientTests.cs
│   │   │   │   ├── MarketDataClientContractTests.cs
│   │   │   │   ├── NyseMarketDataClientContractTests.cs
│   │   │   │   ├── NyseMarketDataClientTests.cs
│   │   │   │   ├── NYSEMessageParsingTests.cs
│   │   │   │   ├── NyseMessagePipelineTests.cs
│   │   │   │   ├── NyseNationalTradesCsvParserTests.cs
│   │   │   │   ├── NyseSharedLifecycleTests.cs
│   │   │   │   ├── NyseTaqCollectorIntegrationTests.cs
│   │   │   │   ├── PolygonCorporateActionFetcherTests.cs
│   │   │   │   ├── PolygonMarketDataClientTests.cs
│   │   │   │   ├── PolygonMessageParsingTests.cs
│   │   │   │   ├── PolygonProviderContractTests.cs
│   │   │   │   ├── PolygonRecordedSessionReplayTests.cs
│   │   │   │   ├── PolygonSubscriptionTests.cs
│   │   │   │   ├── ProviderBehaviorBuilderTests.cs
│   │   │   │   ├── ProviderFactoryCredentialContextTests.cs
│   │   │   │   ├── ProviderResilienceTests.cs
│   │   │   │   ├── ProviderTemplateFactoryCredentialTests.cs
│   │   │   │   ├── RobinhoodBrokerageGatewayTests.cs
│   │   │   │   ├── RobinhoodHistoricalDataProviderTests.cs
│   │   │   │   ├── RobinhoodMarketDataClientTests.cs
│   │   │   │   ├── RobinhoodSymbolSearchProviderTests.cs
│   │   │   │   ├── StockSharpConnectorFactoryTests.cs
│   │   │   │   ├── StockSharpConverterEdgeCaseTests.cs
│   │   │   │   ├── StockSharpMessageConversionTests.cs
│   │   │   │   ├── StockSharpProviderContractTests.cs
│   │   │   │   ├── StockSharpSubscriptionTests.cs
│   │   │   │   ├── StreamingFailoverServiceTests.cs
│   │   │   │   ├── SyntheticMarketDataProviderTests.cs
│   │   │   │   ├── SyntheticOptionsChainProviderTests.cs
│   │   │   │   └── WebSocketProviderBaseTests.cs
│   │   │   ├── Resilience
│   │   │   │   ├── WebSocketConnectionManagerTests.cs
│   │   │   │   └── WebSocketResiliencePolicyTests.cs
│   │   │   └── Shared
│   │   │       ├── SymbolNormalizationTests.cs
│   │   │       └── TempDirectoryFixture.cs
│   │   ├── Integration
│   │   │   ├── EndpointTests
│   │   │   │   ├── AccountPortfolioEndpointTests.cs
│   │   │   │   ├── AuthEndpointTests.cs
│   │   │   │   ├── BackfillEndpointTests.cs
│   │   │   │   ├── CatalogEndpointTests.cs
│   │   │   │   ├── CheckpointEndpointTests.cs
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
│   │   │   │   ├── SymbolEndpointTests.cs
│   │   │   │   └── UiEndpointsJsonOptionsTests.cs
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
│   │   │   ├── AttributeCredentialResolverTests.cs
│   │   │   ├── CredentialValidatorTests.cs
│   │   │   ├── DataSourceAttributeTests.cs
│   │   │   ├── DataSourceRegistryTests.cs
│   │   │   ├── ExceptionTypeTests.cs
│   │   │   └── ProviderModuleLoaderTests.cs
│   │   ├── Risk
│   │   │   ├── CompositeRiskValidatorTests.cs
│   │   │   ├── DrawdownCircuitBreakerTests.cs
│   │   │   ├── OrderRateThrottleTests.cs
│   │   │   └── PositionLimitRuleTests.cs
│   │   ├── SecurityMaster
│   │   │   ├── SecurityEnrichmentTests.cs
│   │   │   ├── SecurityMasterAggregateRebuilderTests.cs
│   │   │   ├── SecurityMasterAssetClassSupportTests.cs
│   │   │   ├── SecurityMasterConflictServiceTests.cs
│   │   │   ├── SecurityMasterConvertibleEquityAmendmentTests.cs
│   │   │   ├── SecurityMasterDatabaseFactAttribute.cs
│   │   │   ├── SecurityMasterDatabaseFixture.cs
│   │   │   ├── SecurityMasterLedgerBridgeTests.cs
│   │   │   ├── SecurityMasterMigrationRunnerTests.cs
│   │   │   ├── SecurityMasterPostgresRoundTripTests.cs
│   │   │   ├── SecurityMasterPreferredEquityAmendmentTests.cs
│   │   │   ├── SecurityMasterProjectionServiceSnapshotTests.cs
│   │   │   ├── SecurityMasterQueryServiceEquityTermsTests.cs
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
│   │   │   ├── ParquetStorageSinkTests.cs
│   │   │   ├── PortableDataPackagerTests.cs
│   │   │   ├── PositionSnapshotStoreTests.cs
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
│   │   │   ├── AggregatePortfolioServiceTests.cs
│   │   │   ├── CashFlowProjectionTests.cs
│   │   │   ├── LedgerReadServiceTests.cs
│   │   │   ├── PortfolioReadServiceTests.cs
│   │   │   ├── PromotionServiceLiveGovernanceTests.cs
│   │   │   ├── PromotionServiceTests.cs
│   │   │   ├── ReconciliationProjectionServiceTests.cs
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
│   │   │   ├── Builders
│   │   │   │   ├── BacktestRequestBuilder.cs
│   │   │   │   ├── HistoricalBarBuilder.cs
│   │   │   │   ├── MarketEventBuilder.cs
│   │   │   │   ├── SecurityBuilder.cs
│   │   │   │   └── TradeBuilder.cs
│   │   │   ├── PolygonStubClient.cs
│   │   │   ├── StubHttpMessageHandler.cs
│   │   │   └── TestMarketEventPublisher.cs
│   │   ├── Treasury
│   │   │   ├── MmfFamilyNormalizationTests.cs
│   │   │   ├── MmfLiquidityServiceTests.cs
│   │   │   ├── MmfRebuildTests.cs
│   │   │   └── MoneyMarketFundServiceTests.cs
│   │   ├── Ui
│   │   │   ├── DirectLendingEndpointsTests.cs
│   │   │   ├── ExecutionGovernanceEndpointsTests.cs
│   │   │   ├── ExecutionWriteEndpointsTests.cs
│   │   │   ├── SecurityMasterPreferredEquityEndpointsTests.cs
│   │   │   ├── StaticAssetPathResolverTests.cs
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
│   │   │   ├── FixtureModeDetectorTests.cs
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
│   │   │   ├── SettingsConfigurationServiceTests.cs
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
│   │   │   ├── AppServiceRegistrationTests.cs
│   │   │   ├── BackgroundTaskSchedulerServiceTests.cs
│   │   │   ├── ConfigServiceTests.cs
│   │   │   ├── ConnectionServiceTests.cs
│   │   │   ├── ExportPresetServiceTests.cs
│   │   │   ├── FirstRunServiceTests.cs
│   │   │   ├── FundReconciliationWorkbenchServiceTests.cs
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
│   │   │   ├── WorkspaceServiceTests.cs
│   │   │   └── WorkspaceShellContextServiceTests.cs
│   │   ├── Support
│   │   │   ├── FakeQuantScriptCompiler.cs
│   │   │   ├── FakeScriptRunner.cs
│   │   │   ├── FakeWorkstationReconciliationApiClient.cs
│   │   │   ├── RunMatUiAutomationFacade.cs
│   │   │   └── WpfTestThread.cs
│   │   ├── ViewModels
│   │   │   ├── AddProviderWizardViewModelTests.cs
│   │   │   ├── CashFlowViewModelTests.cs
│   │   │   ├── DataQualityViewModelCharacterizationTests.cs
│   │   │   ├── FundAccountsViewModelTests.cs
│   │   │   ├── FundLedgerViewModelTests.cs
│   │   │   ├── MainShellViewModelTests.cs
│   │   │   ├── QuantScriptViewModelTests.cs
│   │   │   ├── RunMatViewModelTests.cs
│   │   │   └── StrategyRunBrowserViewModelTests.cs
│   │   ├── Views
│   │   │   ├── DashboardPageSmokeTests.cs
│   │   │   ├── DataQualityPageSmokeTests.cs
│   │   │   ├── MainPageSmokeTests.cs
│   │   │   ├── NavigationPageSmokeTests.cs
│   │   │   ├── QuantScriptPageTests.cs
│   │   │   ├── RunMatUiSmokeTests.cs
│   │   │   ├── RunMatWorkflowSmokeTests.cs
│   │   │   ├── SplitPaneHostControlTests.cs
│   │   │   └── SystemHealthPageSmokeTests.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.Wpf.Tests.csproj
│   │   └── TestAssemblyConfiguration.cs
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
├── .gitleaks.toml
├── .globalconfig
├── .markdownlint.json
├── .vsconfig
├── CLAUDE.md
├── Directory.Build.props
├── Directory.Packages.props
├── docfx.json
├── environment.yml
├── global.json
├── LICENSE
├── Makefile
├── Meridian.sln
├── package-lock.json
├── package.json
└── README.md
```

For the complete repository tree with all 700+ files, see [`CLAUDE.md`](https://github.com/rodoHasArrived/Meridian/blob/main/CLAUDE.md) § Repository Structure.

## CI/CD Workflow

**GitHub Actions:** 26 workflows in `.github/workflows/`. For the full inventory, see [`CLAUDE.actions.md`](../claude/CLAUDE.actions.md).

Key workflows:
- `pr-checks.yml` — PR validation (format, build, test, coverage, AI review)
- `test-matrix.yml` — Multi-platform test matrix (Windows, Linux, macOS)
- `code-quality.yml` — Formatting, analyzers, AI quality suggestions
- `security.yml` — CodeQL, dependency review, secret detection
- `desktop-builds.yml` — WPF builds and MSIX packaging
- `documentation.yml` — Doc generation, TODO scanning, AI error intake
- `nightly.yml` — Full build + test + AI failure diagnosis

## Development Practices

### Configuration Management

- **NEVER commit credentials:** `appsettings.json` is gitignored
- **Use environment variables for secrets:** `ALPACA_KEY_ID`, `ALPACA_SECRET_KEY`, etc.
- **Copy sample config:** Always start with `cp config/appsettings.sample.json config/appsettings.json`

### Code Style

- C# 13 with nullable reference types enabled
- Implicit usings enabled
- Structured logging with semantic parameters (never string interpolation)
- `CancellationToken` on all async methods
- `sealed` classes by default
- Central Package Management — never add `Version=` to `<PackageReference>`

For the full conventions reference, see [`CLAUDE.md`](https://github.com/rodoHasArrived/Meridian/blob/main/CLAUDE.md) § Critical Rules and § Coding Conventions.

## Common Issues & Workarounds

| Issue | Solution |
|-------|----------|
| Build fails with NETSDK1100 on Linux/macOS | Use `/p:EnableWindowsTargeting=true` (set in `Directory.Build.props`) |
| `appsettings.json` not found | `cp config/appsettings.sample.json config/appsettings.json` |
| Data or logs directories missing | `mkdir -p data logs` or `make setup-config` |
| Docker build fails | Ensure `appsettings.json` exists before building |
| Tests fail due to missing config | Tests should mock configuration; check test setup |
| NU1008 error on restore | Remove `Version=` from `<PackageReference>` (CPM is active) |

## Quick Decision Tree

| Task | Where to Look |
|------|---------------|
| Adding new functionality | Appropriate layer in `src/`, follow existing patterns |
| Fixing a bug | Add test first in `tests/`, then fix |
| Working with providers | `src/Meridian.Infrastructure/Adapters/` |
| Storage changes | `src/Meridian.Storage/` |
| WPF desktop | `src/Meridian.Wpf/` |
| Run tests | `dotnet test tests/Meridian.Tests/` |
| Build | `dotnet restore && dotnet build -c Release` (both with `/p:EnableWindowsTargeting=true`) |
| Start app | `dotnet run --project src/Meridian/Meridian.csproj -- --ui` |

## Related Resources

- **Master AI index:** [`docs/ai/README.md`](https://github.com/rodoHasArrived/Meridian/blob/main/docs/ai/README.md)
- **Root context:** [`CLAUDE.md`](https://github.com/rodoHasArrived/Meridian/blob/main/CLAUDE.md)
- **Error prevention:** [`docs/ai/ai-known-errors.md`](../ai-known-errors.md)
- **Code review:** [`.github/agents/code-review-agent.md`](https://github.com/rodoHasArrived/Meridian/blob/main/.github/agents/code-review-agent.md)
- **Prompt templates:** [`.github/prompts/README.md`](https://github.com/rodoHasArrived/Meridian/blob/main/.github/prompts/README.md)
- **CI/CD details:** [`docs/ai/claude/CLAUDE.actions.md`](../claude/CLAUDE.actions.md)
