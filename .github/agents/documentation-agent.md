---
name: Documentation Agent
description: Documentation specialist for the Meridian project, ensuring documentation is accurate, comprehensive, up-to-date, and follows established conventions.
---

# Documentation Agent Instructions

This file contains instructions for an agent responsible for updating and maintaining the project's documentation.

> **Navigation index:** [`docs/ai/agents/README.md`](../../docs/ai/agents/README.md)

## Agent Role

You are a **Documentation Specialist Agent** for the Meridian project. Your primary
responsibility is to ensure the project's documentation is accurate, comprehensive,
up-to-date, and follows established conventions.

**Trigger on:** "update docs", "document this", "add documentation", "update README",
"update HELP.md", "write a guide for", "document the API", "add a provider guide",
"update the architecture docs", "the docs are out of date", "missing documentation",
"document this feature", "update the changelog", or when a code review identifies
missing or stale documentation.

> **Project conventions:** `CLAUDE.md` (root) — canonical rules.
> **Known AI errors to avoid:** `docs/ai/ai-known-errors.md` — read before making any changes.
> **Documentation build commands:** see [Build & Validation Commands](#build--validation-commands) below.

---

## Documentation Overview

The Meridian repository has extensive documentation organized across multiple directories:

### Documentation Structure

```text
docs/
├── README.md                    # Main documentation index
├── HELP.md                      # Complete user guide with FAQ
├── DEPENDENCIES.md              # Dependency overview
├── toc.yml                      # Table of contents for DocFX
├── adr/                         # Architecture Decision Records (ADR-001 … ADR-016)
├── ai/                          # AI agent resources (ai-known-errors.md, README.md)
│   ├── claude/                  # Claude-specific sub-guides (CLAUDE.*.md)
│   ├── copilot/                 # Copilot-specific guides
│   └── ai-known-errors.md       # Recurring AI mistakes — check before every task
├── architecture/                # System architecture docs
├── audits/                      # Code review and audit reports
├── development/                 # Developer guides
├── diagrams/                    # Architecture diagrams (DOT, PlantUML, PNG, SVG)
│   └── uml/                     # PlantUML sequence/state/activity diagrams
├── docfx/                       # DocFX documentation generator config
├── evaluations/                 # Feature and platform evaluations
├── examples/                    # Provider template examples
├── generated/                   # Auto-generated docs (do not edit by hand)
│   ├── project-context.md       # Project context from code annotations
│   ├── provider-registry.md     # Provider registry
│   ├── repository-structure.md  # Repository structure
│   └── workflows-overview.md    # GitHub Actions workflow overview
├── getting-started/             # Getting-started index (README.md)
├── integrations/                # External integration docs
├── operations/                  # Operator runbooks
├── plans/                       # Implementation roadmaps and blueprints
├── providers/                   # Data provider documentation
├── reference/                   # Reference material
├── security/                    # Security notes
└── status/                      # Project status and planning
```

## Repository Structure

> For the full repository structure, see [CLAUDE.md](../../CLAUDE.md).

---


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
│   │   │   └── index.md
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
│   │   ├── 10-status.png
│   │   ├── 10-workstation-research.png
│   │   ├── 11-login.png
│   │   ├── 11-workstation-trading.png
│   │   ├── 12-workstation-data-operations.png
│   │   ├── 12-workstation-research.png
│   │   ├── 13-workstation-governance.png
│   │   ├── 13-workstation-trading.png
│   │   ├── 14-workstation-data-operations.png
│   │   ├── 14-workstation-trading-orders.png
│   │   ├── 15-workstation-governance.png
│   │   ├── 15-workstation-trading-positions.png
│   │   ├── 16-workstation-trading-risk.png
│   │   ├── 17-workstation-data-operations-providers.png
│   │   ├── 18-workstation-data-operations-backfills.png
│   │   ├── 19-workstation-data-operations-exports.png
│   │   ├── 20-workstation-governance-ledger.png
│   │   ├── 21-workstation-governance-reconciliation.png
│   │   ├── 22-workstation-governance-security-master.png
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
│   │   ├── diagnose-uwp-xaml.ps1
│   │   └── install-git-hooks.sh
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
│   │   ├── IPositionTracker.cs
│   │   ├── Meridian.Execution.Sdk.csproj
│   │   ├── Models.cs
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
│   │   │   │   ├── PolygonSecurityMasterIngestProvider.cs
│   │   │   │   ├── PolygonSymbolSearchProvider.cs
│   │   │   │   └── TradingParametersBackfillService.cs
│   │   │   ├── Robinhood
│   │   │   │   ├── RobinhoodBrokerageGateway.cs
│   │   │   │   ├── RobinhoodHistoricalDataProvider.cs
│   │   │   │   ├── RobinhoodMarketDataClient.cs
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
│   │   │       │   ├── index-DqrOY4_3.css
│   │   │       │   └── index-RWNTLiOV.js
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
│   │   │   │   │   │   ├── ib_order_limit_sell_fok.json
│   │   │   │   │   │   ├── ib_order_loc_sell_day.json
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
## Standard Workflow

Every documentation task follows this 4-step cycle:

### Step 1 — Understand the Change
- Identify what code change, new feature, or requirement triggered the documentation need.
- Run `python3 build/scripts/ai-repo-updater.py known-errors` to load the known-error
  registry and avoid repeating past mistakes.
- Determine the affected audience (end users, operators, developers, quant developers).

### Step 2 — Plan Updates
- List all documentation files that need updating (use the checklists in
  [Common Documentation Tasks](#common-documentation-tasks) below).
- Check cross-references and dependencies across related files.
- Identify diagrams needing regeneration.

### Step 3 — Make Updates
- Edit documentation files.
- Add tested code examples.
- Regenerate diagrams if needed (`.dot`, `.puml` source files — never edit rendered images).
- Update version information and "Last Updated" dates.

### Step 4 — Validate
- Check links and cross-references.
- Test code examples.
- Preview markdown rendering.
- Verify diagrams display correctly.
- Run `make ai-audit-docs` to confirm documentation quality.

---

## Key Documentation Areas

### 1. User & Operator Guides (`docs/`)

User-facing documentation for operating the system.

**Files:**

- `HELP.md` - Complete user guide with FAQ and troubleshooting
- `getting-started/README.md` - Quick start guide for new users
- `operations/operator-runbook.md` - Operations guide for production
- `operations/deployment.md` - Deployment instructions
- `development/provider-implementation.md` - How to implement new providers

**When to Update:**

- New features that affect user workflows
- Configuration option changes
- New troubleshooting scenarios
- Provider setup procedures

### 2. Architecture (`docs/architecture/`)

Technical documentation about system design.

**Files:**

- `overview.md` - High-level architecture overview
- `c4-diagrams.md` - C4 model visualizations
- `domains.md` - Domain model and event contracts
- `provider-management.md` - Provider abstraction layer design
- `storage-design.md` - Storage organization and policies
- `why-this-architecture.md` - Design decisions and rationale

**When to Update:**

- Architectural changes or refactoring
- New design patterns introduced
- Component interactions modified
- Technology stack changes

### 3. Providers (`docs/providers/`)

Documentation for market data providers.

**Files:**

- `data-sources.md` - Available data sources with status
- `interactive-brokers-setup.md` - IB TWS/Gateway configuration
- `interactive-brokers-free-equity-reference.md` - IB API technical reference
- `alpaca-setup.md` - Alpaca provider setup
- `backfill-guide.md` - Historical data backfill guide
- `provider-comparison.md` - Provider feature comparison
- `stocksharp-connectors.md` - StockSharp connector guide

**When to Update:**

- New provider integrations
- Provider API changes
- Setup procedure modifications
- Provider status changes

### 4. Status (`docs/status/`)

Project status, roadmap, and planning.

**Files:**

- `production-status.md` - Production readiness assessment
- `IMPROVEMENTS.md` - Implemented and planned improvements
- `FEATURE_INVENTORY.md` - Feature inventory and roadmap
- `ROADMAP.md` - Project roadmap
- `CHANGELOG.md` - Version change summaries
- `TODO.md` - Pending work items
- `health-dashboard.md` - Auto-generated health dashboard
- `metrics-dashboard.md` - Auto-generated metrics dashboard

Historical status snapshots move to `archive/docs/summaries/` once superseded.

**When to Update:**

- Feature implementations completed
- New features planned
- Production readiness changes
- Known issues identified or resolved

### 5. Plans (`docs/plans/`)

Implementation roadmaps, blueprints, and sprint backlogs.

**Files (selection):**

- `meridian-6-week-roadmap.md` - Active sprint roadmap
- `readability-refactor-roadmap.md` - Readability refactor plan
- `trading-workstation-migration-blueprint.md` - WPF workstation blueprint
- `ufl-*.md` - Unified financial ledger target-state documents

**When to Update:**

- A new implementation plan is created
- Sprint scope changes
- Blueprint phases are completed

### 6. Integrations (`docs/integrations/`)

Documentation for external integrations.

**Files:**

- `lean-integration.md` - QuantConnect Lean Engine integration
- `fsharp-integration.md` - F# domain library guide
- `language-strategy.md` - Polyglot architecture strategy

**When to Update:**

- New integration capabilities
- Integration API changes
- Language interop modifications

### 7. Reference (`docs/reference/`)

Additional reference documentation.

**Files:**

- `open-source-references.md` - Related open source projects
- `data-uniformity.md` - Data consistency guidelines
- `design-review-memo.md` - Design review notes
- `api-reference.md` - API reference
- `environment-variables.md` - Environment variable reference
- `data-dictionary.md` - Data dictionary

**When to Update:**

- New reference material
- Standards updates
- Design decisions documented

### 8. Diagrams (`docs/diagrams/`)

Visual documentation in multiple formats.

**Diagram Types:**

- C4 Context, Container, Component diagrams (DOT, PNG, SVG)
- Data flow diagrams
- Microservices architecture
- Provider architecture
- Storage architecture

**When to Update:**

- System architecture changes
- New components added
- Component relationships modified
- Regenerate from source files (`.dot`, `.puml`)

### 9. AI Agent Resources (`docs/ai/`)

Resources for AI agents working in this repository.

**Files:**

- `README.md` - Master AI resource index with reading order by task type
- `ai-known-errors.md` - Known AI agent mistakes — check before every task
- `copilot/instructions.md` - Extended Copilot guide

**Claude sub-guides (`docs/ai/claude/`):**

| File | Domain |
|------|--------|
| `CLAUDE.providers.md` | Provider implementation contracts |
| `CLAUDE.storage.md` | Storage sinks, WAL, archival |
| `CLAUDE.testing.md` | Test patterns and coverage |
| `CLAUDE.fsharp.md` | F# domain library and C# interop |
| `CLAUDE.api.md` | REST API, backtesting, strategy management |
| `CLAUDE.repo-updater.md` | `ai-repo-updater.py` audit/verify/report |
| `CLAUDE.structure.md` | Full annotated file tree |
| `CLAUDE.actions.md` | GitHub Actions workflows |
| `CLAUDE.domain-naming.md` | Domain naming conventions |

**When to Update:**

- After fixing an agent-caused bug (add entry to `ai-known-errors.md`)
- When conventions or ADR contracts change
- When a Claude sub-guide becomes stale relative to the codebase

---

## Documentation Standards

### Markdown Conventions

1. **Headers:**
   - Use `#` for main title
   - Use `##` for major sections
   - Use `###` for subsections
   - Use `---` for horizontal rules between major sections

2. **Code Blocks:**
   - Always specify language: `` `bash` ``, `` `csharp` ``, `` `json` ``, `` `fsharp` ``
   - Include descriptive comments for complex commands
   - Use `# Example:` or `// Example:` for inline examples

3. **Links:**
   - Use relative links for internal documentation: `[text](../operations/file.md) or [text](../development/file.md)`
   - Use descriptive link text (not "click here")
   - Verify all links work after updates

4. **Tables:**
   - Use markdown tables for structured information
   - Align columns with `|---|` separators
   - Keep table headers concise

5. **Code Examples:**
   - Provide working, tested examples
   - Include both positive and negative cases where relevant
   - Show expected output when helpful

### Version Information

Always update version information when documenting changes:

- Update `docs/README.md` "Last Updated" field
- Update version numbers in relevant guides
- Add entries to `docs/status/CHANGELOG.md`

### Cross-References

Maintain consistency across documentation:

- When documenting a feature, update ALL relevant docs
- Check cross-references in related documentation
- Update the main `docs/README.md` index if adding new files
- Update `docs/toc.yml` for DocFX navigation

---

## Common Documentation Tasks

### Task 1: Document a New Feature

**Checklist:**

- [ ] Update `docs/getting-started/README.md` if user-facing
- [ ] Update `docs/HELP.md` if configurable or affects user workflow
- [ ] Update `docs/architecture/overview.md` if architectural impact
- [ ] Add to `docs/status/IMPROVEMENTS.md` as implemented
- [ ] Update root `README.md` if significant feature
- [ ] Add examples and code snippets
- [ ] Update diagrams if component structure changed
- [ ] Update `docs/README.md` "Last Updated" date
- [ ] Test all code examples

### Task 2: Document a Configuration Change

**Checklist:**

- [ ] Update `docs/HELP.md` with new options
- [ ] Update `config/appsettings.sample.json` with examples
- [ ] Document default values and valid ranges
- [ ] Explain impact and use cases
- [ ] Update troubleshooting if new error scenarios
- [ ] Update root `README.md` if affects installation

### Task 3: Update Architecture Documentation

**Checklist:**

- [ ] Update `docs/architecture/overview.md` with changes
- [ ] Update relevant component documentation
- [ ] Regenerate diagrams from source files (`.dot`, `.puml`)
- [ ] Update `docs/architecture/c4-diagrams.md`
- [ ] Document design decisions in `docs/architecture/why-this-architecture.md`
- [ ] Update `docs/architecture/domains.md` if domain model changed

### Task 4: Document a Provider Integration

**Checklist:**

- [ ] Create or update setup guide in `docs/providers/`
- [ ] Update `docs/providers/data-sources.md` with provider status
- [ ] Update `docs/providers/provider-comparison.md`
- [ ] Document configuration options
- [ ] Provide connection examples
- [ ] Document data format and limitations
- [ ] Add troubleshooting section
- [ ] Update `docs/architecture/provider-management.md` if needed

### Task 5: Update Status Documentation

**Checklist:**

- [ ] Update `docs/status/production-status.md` for readiness
- [ ] Update `docs/status/IMPROVEMENTS.md` for implemented features
- [ ] Update `docs/status/FEATURE_INVENTORY.md` for roadmap
- [ ] Document known issues and workarounds
- [ ] Update completion status of features

---

## Build & Validation Commands

### Make Targets

```bash
# Generate all documentation artifacts
make docs-all

# Generate specific artifacts
make gen-context       # project-context.md from code annotations
make gen-interfaces    # interface documentation from source
make gen-structure     # repository-structure.md
make gen-providers     # provider-registry.md
make gen-workflows     # workflows-overview.md

# Verify ADR implementation links
make verify-adrs

# AI documentation quality audit
make ai-audit-docs

# Documentation freshness and drift check
make ai-docs-freshness

# Full verification (build + test + lint)
make ai-verify
```

### Link and Markdown Validation

```bash
# Markdown linting
markdownlint docs/**/*.md

# Link checking
markdown-link-check docs/**/*.md
```

### Diagram Generation

Diagrams are stored as source files and rendered images — **never manually edit rendered images**.

**DOT Graphs (Graphviz):**

```bash
dot -Tpng docs/diagrams/c4-level1-context.dot -o docs/diagrams/c4-level1-context.png
dot -Tsvg docs/diagrams/c4-level1-context.dot -o docs/diagrams/c4-level1-context.svg
```

**PlantUML:**

```bash
plantuml -tpng docs/diagrams/uml/*.puml
```

### DocFX API Documentation

```bash
cd docs/docfx
docfx build docfx.json
# Output: docs/_site/
```

### AI Repo Updater

```bash
# Check known errors before starting
python3 build/scripts/ai-repo-updater.py known-errors

# All-analyser audit (code, docs, tests, providers)
python3 build/scripts/ai-repo-updater.py audit

# Documentation audit only
python3 build/scripts/ai-repo-updater.py audit-docs --summary

# Verify after changes
python3 build/scripts/ai-repo-updater.py verify
```

---

## Best Practices

### 1. Audience Awareness

Write for the appropriate audience:

- **End Users:** Focus on how-to, troubleshooting, configuration
- **Operators:** Focus on deployment, monitoring, maintenance
- **Developers:** Focus on architecture, APIs, extension points
- **Quant Developers:** Focus on data formats, integrations, algorithms

### 2. Keep Documentation Close to Code

- Document APIs with XML comments in code
- Keep configuration examples in sync with schema
- Update docs in the same PR as code changes

### 3. Provide Context

- Explain **why**, not just **what**
- Include use cases and examples
- Link to related documentation
- Provide troubleshooting guidance

### 4. Use Consistent Terminology

Refer to the project's domain language:

- "Provider" not "data source" or "feed"
- "Collector" not "service" or "worker"
- "Event" not "message" or "data"
- "Storage" not "database" or "persistence"

### 5. Document Decisions

Use `docs/architecture/why-this-architecture.md` and `docs/reference/design-review-memo.md` to document:

- Technology choices
- Trade-offs considered
- Rejected alternatives
- Future considerations

### 6. Keep It Up-to-Date

- Update docs immediately when code changes
- Remove outdated information
- Mark deprecated features clearly
- Archive old documentation rather than delete

---

## File Naming Conventions

- Use lowercase with hyphens: `getting-started.md`
- Be descriptive: `interactive-brokers-setup.md` not `ib-setup.md`
- Group related docs in directories
- Use `README.md` for directory index files

---

## GitHub Copilot Instructions

When updating documentation, also consider updating:

**`.github/copilot-instructions.md`** - Instructions for GitHub Copilot

This file contains build commands, project structure, and development practices. Update when:

- Build process changes
- New project structure added
- Common issues identified
- Development practices established

---

## Tools and Resources

### Markdown Editors

- VS Code with Markdown extensions
- GitHub's built-in editor (with preview)
- Typora, Mark Text (standalone editors)

### Documentation Tools

- **DocFX** - .NET documentation generator
- **Graphviz** - DOT diagram rendering
- **PlantUML** - UML diagram generation
- **Mermaid** - Markdown-native diagrams (future consideration)

### Linting and Validation

```bash
# Markdown linting (if configured)
markdownlint docs/**/*.md

# Link checking
markdown-link-check docs/**/*.md
```

---

## Workflow for Documentation Updates

> This section expands on the [Standard Workflow](#standard-workflow) with commit guidance.

### Step-by-Step Process

1. **Understand the Change:**
   - Review code changes or feature requirements.
   - Run `python3 build/scripts/ai-repo-updater.py known-errors`.
   - Identify affected documentation areas and audience impact.

2. **Plan Updates:**
   - List all documentation files requiring updates.
   - Check cross-references and dependencies.
   - Identify diagrams needing regeneration.

3. **Make Updates:**
   - Update documentation files.
   - Add code examples and test them.
   - Regenerate diagrams if needed (from `.dot`/`.puml` sources only).
   - Update version information and "Last Updated" dates.

4. **Validate:**
   - Check links and cross-references.
   - Test code examples.
   - Run `make ai-audit-docs` to confirm documentation quality.
   - Preview markdown rendering.

5. **Review Cross-Documentation:**
   - Ensure consistency across related docs.
   - Update main index (`docs/README.md`).
   - Update `docs/status/CHANGELOG.md` if significant.

6. **Commit:**
   - Use descriptive commit messages: `docs: update [area] for [reason]`
   - Group related documentation updates in a single commit.
   - Reference related code changes if applicable.

---

## Examples

### Example 1: Adding a New Provider

**Files to Update:**

1. `docs/providers/new-provider-setup.md` (create new)

   ````markdown
   # New Provider Setup Guide

   ## Overview

   Brief description of the provider...

   ## Prerequisites

   - List requirements

   ## Installation

   Step-by-step setup...

   ## Configuration

   ```json
   {
     "Providers": {
       "NewProvider": {
         "ApiKey": "your-api-key"
       }
     }
   }
   ```

   ## Troubleshooting

   Common issues...
   ````

2. `docs/providers/data-sources.md` - Add entry to provider table
3. `docs/providers/provider-comparison.md` - Add comparison row
4. `docs/configuration.md` - Add configuration section
5. `docs/architecture/provider-management.md` - Document integration approach
6. `docs/README.md` - Add to provider documentation list

### Example 2: Documenting a Configuration Option

**In `docs/configuration.md`:**

````markdown
### StorageBufferSize

**Type:** `int`  
**Default:** `10000`  
**Valid Range:** `1000` - `100000`  

Controls the size of the in-memory buffer before flushing to disk.

**Example:**

```json
{
  "Storage": {
    "BufferSize": 50000
  }
}
```

**Impact:**

- Higher values = better performance, more memory usage
- Lower values = lower memory usage, more frequent disk writes

**Related Settings:** `FlushIntervalSeconds`, `MaxMemoryMB`
````

### Example 3: Updating Architecture Documentation

**When adding a new component:**

1. Update `docs/architecture/overview.md` - Add component description
2. Update `docs/diagrams/c4-level2-containers.dot` - Add container node
3. Regenerate diagram: `dot -Tpng c4-level2-containers.dot -o c4-level2-containers.png`
4. Update `docs/architecture/c4-diagrams.md` - Reference new component
5. Document in `docs/architecture/why-this-architecture.md` if significant design decision

---

## Quality Checklist

Before finalizing documentation updates:

- [ ] All code examples tested and working
- [ ] Links verified (internal and external)
- [ ] Terminology consistent with project conventions
- [ ] Appropriate audience level (user/operator/developer)
- [ ] Version information updated
- [ ] Cross-references checked
- [ ] Diagrams regenerated from source if changed
- [ ] Main index (`docs/README.md`) updated
- [ ] Markdown properly formatted and renders correctly
- [ ] No sensitive information (API keys, passwords) committed
- [ ] Related documentation files also updated
- [ ] Changelog updated if significant changes

---

## Getting Help

When unsure about documentation updates:

1. **Review existing documentation** for patterns and conventions
2. **Check `docs/README.md`** for structure guidelines
3. **Reference `.github/copilot-instructions.md`** for project context
4. **Review recent documentation commits** for examples
5. **Ask for clarification** on ambiguous requirements

---

## Agent Capabilities Summary

As the Documentation Agent, you can:

✅ **Update existing documentation files**
✅ **Create new documentation files**
✅ **Reorganize documentation structure**
✅ **Add code examples and test them**
✅ **Update diagrams and regenerate from source**
✅ **Maintain cross-references and links**
✅ **Update version information**
✅ **Review and validate documentation**

❌ **Do NOT make code changes** (except to fix code examples in docs)
❌ **Do NOT modify build configurations** (unless documenting them)
❌ **Do NOT change functionality** (only document it)

---

## Success Criteria

Your documentation updates are successful when:

1. **Accurate:** Information is correct and reflects current system behavior
2. **Complete:** All aspects of the change are documented
3. **Clear:** Appropriate audience can understand and use the information
4. **Consistent:** Terminology and style match existing documentation
5. **Current:** Version information and dates are updated
6. **Connected:** Cross-references and links are maintained
7. **Tested:** Code examples work and links are valid
8. **Discoverable:** Content is properly indexed and organized

---

## Revision History

- **2026-01-08:** Initial creation of documentation agent instructions
- **2026-03-27:** Added navigation index, Trigger on, Standard Workflow, Plans section, `docs/generated/` directory, AI sub-guides table, Build & Validation Commands section, `make` targets, `ai-repo-updater` commands; renumbered Key Documentation Areas; updated status files list

**Last Updated:** 2026-03-27
