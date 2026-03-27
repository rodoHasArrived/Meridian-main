# Meridian

Meridian is a comprehensive fund management platform in active delivery. The current platform includes market-data ingestion (90+ streaming sources, 10+ backfill providers), tiered storage (WAL + JSONL/Parquet), backtesting (tick-level replay with fill models), a brokerage gateway framework (Alpaca, IB, StockSharp adapters), paper-trading with risk rules, portfolio and ledger read models, Security Master foundations, direct-lending services, and a web dashboard with 300 API routes. The next delivery wave focuses on wiring brokerage gateways into a paper-trading cockpit, provider confidence hardening, Security Master productization, and governance/fund-operations product slices.

> **WPF Desktop App:** Code is present in `src/Meridian.Wpf/` and is included in the solution build. On Windows it builds as the full WPF desktop application; on Linux/macOS it compiles as a minimal stub for CI compatibility. The web dashboard (`make run-ui`) remains the cross-platform UI surface.

## Start Here

- [Documentation Index](docs/README.md)
- [Project Roadmap](docs/status/ROADMAP.md)
- [Feature Inventory](docs/status/FEATURE_INVENTORY.md)
- [Improvements Tracker](docs/status/IMPROVEMENTS.md)
- [Trading Workstation Migration Blueprint](docs/plans/trading-workstation-migration-blueprint.md)
- [Governance and Fund Operations Blueprint](docs/plans/governance-fund-ops-blueprint.md)

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

## Planning Source of Truth

Use these documents together when planning or implementing new work:

- [docs/status/ROADMAP.md](docs/status/ROADMAP.md) for delivery waves and target product direction
- [docs/status/FEATURE_INVENTORY.md](docs/status/FEATURE_INVENTORY.md) for current-vs-target capability status
- [docs/status/IMPROVEMENTS.md](docs/status/IMPROVEMENTS.md) for tracked implementation themes
- [docs/plans/trading-workstation-migration-blueprint.md](docs/plans/trading-workstation-migration-blueprint.md) for the workstation migration shape
<!-- readme-tree start -->
```
.
├── .claude
│   ├── agents
│   │   ├── meridian-blueprint.md
│   │   ├── meridian-cleanup.md
│   │   └── meridian-docs.md
│   ├── settings.json
│   ├── settings.local.json
│   └── skills
│       ├── _shared
│       │   └── project-context.md
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
│       ├── meridian-provider-builder
│       │   ├── CHANGELOG.md
│       │   ├── SKILL.md
│       │   └── references
│       │       └── provider-patterns.md
│       ├── meridian-test-writer
│       │   ├── CHANGELOG.md
│       │   ├── SKILL.md
│       │   └── references
│       │       └── test-patterns.md
│       └── skills_provider.py
├── .codex
│   ├── config.toml
│   ├── environments
│   │   ├── README.md
│   │   └── environment.toml
│   └── skills
│       ├── README.md
│       ├── _shared
│       │   └── project-context.md
│       ├── meridian-blueprint
│       │   ├── SKILL.md
│       │   └── references
│       │       └── blueprint-patterns.md
│       ├── meridian-brainstorm
│       │   ├── SKILL.md
│       │   └── references
│       │       └── competitive-landscape.md
│       ├── meridian-code-review
│       │   └── SKILL.md
│       ├── meridian-provider-builder
│       │   ├── SKILL.md
│       │   └── references
│       │       └── provider-patterns.md
│       ├── meridian-roadmap-strategist
│       │   ├── SKILL.md
│       │   ├── agents
│       │   │   └── openai.yaml
│       │   └── references
│       │       └── roadmap-source-map.md
│       └── meridian-test-writer
│           ├── SKILL.md
│           └── references
│               └── test-patterns.md
├── .devcontainer
│   └── devcontainer.json
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
│   │   ├── cleanup-specialist.agent.md
│   │   ├── code-review-agent.md
│   │   ├── documentation-agent.md
│   │   ├── performance-agent.md
│   │   ├── provider-builder-agent.md
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
│   │   ├── troubleshoot-issue.prompt.yml
│   │   ├── workflow-results-code-quality.prompt.yml
│   │   ├── workflow-results-test-matrix.prompt.yml
│   │   ├── wpf-debug-improve.prompt.yml
│   │   └── write-unit-tests.prompt.yml
│   ├── pull_request_template_desktop.md
│   ├── spellcheck-config.yml
│   └── workflows
│       ├── README.md
│       ├── SKIPPED_JOBS_EXPLAINED.md
│       ├── benchmark.yml
│       ├── bottleneck-detection.yml
│       ├── build-observability.yml
│       ├── canonicalization-fixture-maintenance.yml
│       ├── close-duplicate-issues.yml
│       ├── code-quality.yml
│       ├── copilot-pull-request-reviewer.yml
│       ├── copilot-setup-steps.yml
│       ├── copilot-swe-agent-copilot.yml
│       ├── desktop-builds.yml
│       ├── docker.yml
│       ├── documentation.yml
│       ├── export-project-artifact.yml
│       ├── golden-path-validation.yml
│       ├── labeling.yml
│       ├── maintenance-self-test.yml
│       ├── maintenance.yml
│       ├── makefile.yml
│       ├── nightly.yml
│       ├── pr-checks.yml
│       ├── prompt-generation.yml
│       ├── python-package-conda.yml
│       ├── readme-tree.yml
│       ├── release.yml
│       ├── repo-health.yml
│       ├── reusable-dotnet-build.yml
│       ├── scheduled-maintenance.yml
│       ├── security.yml
│       ├── skill-evals.yml
│       ├── stale.yml
│       ├── static.yml
│       ├── test-matrix.yml
│       ├── ticker-data-collection.yml
│       ├── update-diagrams.yml
│       └── validate-workflows.yml
├── .gitignore
├── .globalconfig
├── .markdownlint.json
├── .vsconfig
├── AGENT_IMPLEMENTATION_SUMMARY.md
├── AGENT_QUICK_REFERENCE.md
├── CLAUDE.md
├── CONTEXTUAL_COMMANDS_IMPLEMENTATION_SUMMARY.md
├── CORPACTIONS_IMPLEMENTATION.md
├── Directory.Build.props
├── Directory.Packages.props
├── IMPLEMENTATION_CHECKLIST.md
├── IMPLEMENTATION_SUMMARY.md
├── LICENSE
├── Makefile
├── Meridian.sln
├── README.md
├── SYSTEM_TRAY_IMPLEMENTATION.md
├── archive
│   ├── README.md
│   ├── code
│   │   ├── README.md
│   │   └── obj-codex
│   │       ├── src
│   │       │   ├── Meridian
│   │       │   │   └── obj-codex
│   │       │   │       ├── Meridian.csproj.nuget.dgspec.json
│   │       │   │       ├── Meridian.csproj.nuget.g.props
│   │       │   │       ├── Meridian.csproj.nuget.g.targets
│   │       │   │       ├── project.assets.json
│   │       │   │       └── project.nuget.cache
│   │       │   ├── Meridian.Application
│   │       │   │   └── obj-codex
│   │       │   │       ├── Meridian.Application.csproj.nuget.dgspec.json
│   │       │   │       ├── Meridian.Application.csproj.nuget.g.props
│   │       │   │       ├── Meridian.Application.csproj.nuget.g.targets
│   │       │   │       ├── project.assets.json
│   │       │   │       └── project.nuget.cache
│   │       │   ├── Meridian.Backtesting.Sdk
│   │       │   │   └── obj-codex
│   │       │   │       ├── Meridian.Backtesting.Sdk.csproj.nuget.dgspec.json
│   │       │   │       ├── Meridian.Backtesting.Sdk.csproj.nuget.g.props
│   │       │   │       ├── Meridian.Backtesting.Sdk.csproj.nuget.g.targets
│   │       │   │       ├── project.assets.json
│   │       │   │       └── project.nuget.cache
│   │       │   ├── Meridian.Contracts
│   │       │   │   └── obj-codex
│   │       │   │       ├── Meridian.Contracts.csproj.nuget.dgspec.json
│   │       │   │       ├── Meridian.Contracts.csproj.nuget.g.props
│   │       │   │       ├── Meridian.Contracts.csproj.nuget.g.targets
│   │       │   │       ├── project.assets.json
│   │       │   │       └── project.nuget.cache
│   │       │   ├── Meridian.Core
│   │       │   │   └── obj-codex
│   │       │   │       ├── Meridian.Core.csproj.nuget.dgspec.json
│   │       │   │       ├── Meridian.Core.csproj.nuget.g.props
│   │       │   │       ├── Meridian.Core.csproj.nuget.g.targets
│   │       │   │       ├── project.assets.json
│   │       │   │       └── project.nuget.cache
│   │       │   ├── Meridian.Domain
│   │       │   │   └── obj-codex
│   │       │   │       ├── Meridian.Domain.csproj.nuget.dgspec.json
│   │       │   │       ├── Meridian.Domain.csproj.nuget.g.props
│   │       │   │       ├── Meridian.Domain.csproj.nuget.g.targets
│   │       │   │       ├── project.assets.json
│   │       │   │       └── project.nuget.cache
│   │       │   ├── Meridian.Execution
│   │       │   │   └── obj-codex
│   │       │   │       ├── Meridian.Execution.csproj.nuget.dgspec.json
│   │       │   │       ├── Meridian.Execution.csproj.nuget.g.props
│   │       │   │       ├── Meridian.Execution.csproj.nuget.g.targets
│   │       │   │       ├── project.assets.json
│   │       │   │       └── project.nuget.cache
│   │       │   ├── Meridian.Execution.Sdk
│   │       │   │   └── obj-codex
│   │       │   │       ├── Meridian.Execution.Sdk.csproj.nuget.dgspec.json
│   │       │   │       ├── Meridian.Execution.Sdk.csproj.nuget.g.props
│   │       │   │       ├── Meridian.Execution.Sdk.csproj.nuget.g.targets
│   │       │   │       ├── project.assets.json
│   │       │   │       └── project.nuget.cache
│   │       │   ├── Meridian.FSharp
│   │       │   │   └── obj-codex
│   │       │   │       ├── Meridian.FSharp.fsproj.nuget.dgspec.json
│   │       │   │       ├── Meridian.FSharp.fsproj.nuget.g.props
│   │       │   │       ├── Meridian.FSharp.fsproj.nuget.g.targets
│   │       │   │       ├── project.assets.json
│   │       │   │       └── project.nuget.cache
│   │       │   ├── Meridian.FSharp.Ledger
│   │       │   │   └── obj-codex
│   │       │   │       ├── Meridian.FSharp.Ledger.fsproj.nuget.dgspec.json
│   │       │   │       ├── Meridian.FSharp.Ledger.fsproj.nuget.g.props
│   │       │   │       ├── Meridian.FSharp.Ledger.fsproj.nuget.g.targets
│   │       │   │       ├── project.assets.json
│   │       │   │       └── project.nuget.cache
│   │       │   ├── Meridian.FSharp.Trading
│   │       │   │   └── obj-codex
│   │       │   │       ├── Meridian.FSharp.Trading.fsproj.nuget.dgspec.json
│   │       │   │       ├── Meridian.FSharp.Trading.fsproj.nuget.g.props
│   │       │   │       ├── Meridian.FSharp.Trading.fsproj.nuget.g.targets
│   │       │   │       ├── project.assets.json
│   │       │   │       └── project.nuget.cache
│   │       │   ├── Meridian.Infrastructure
│   │       │   │   └── obj-codex
│   │       │   │       ├── Meridian.Infrastructure.csproj.nuget.dgspec.json
│   │       │   │       ├── Meridian.Infrastructure.csproj.nuget.g.props
│   │       │   │       ├── Meridian.Infrastructure.csproj.nuget.g.targets
│   │       │   │       ├── project.assets.json
│   │       │   │       └── project.nuget.cache
│   │       │   ├── Meridian.Infrastructure.CppTrader
│   │       │   │   └── obj-codex
│   │       │   │       ├── Meridian.Infrastructure.CppTrader.csproj.nuget.dgspec.json
│   │       │   │       ├── Meridian.Infrastructure.CppTrader.csproj.nuget.g.props
│   │       │   │       ├── Meridian.Infrastructure.CppTrader.csproj.nuget.g.targets
│   │       │   │       ├── project.assets.json
│   │       │   │       └── project.nuget.cache
│   │       │   ├── Meridian.Ledger
│   │       │   │   └── obj-codex
│   │       │   │       ├── Meridian.Ledger.csproj.nuget.dgspec.json
│   │       │   │       ├── Meridian.Ledger.csproj.nuget.g.props
│   │       │   │       ├── Meridian.Ledger.csproj.nuget.g.targets
│   │       │   │       ├── project.assets.json
│   │       │   │       └── project.nuget.cache
│   │       │   ├── Meridian.ProviderSdk
│   │       │   │   └── obj-codex
│   │       │   │       ├── Meridian.ProviderSdk.csproj.nuget.dgspec.json
│   │       │   │       ├── Meridian.ProviderSdk.csproj.nuget.g.props
│   │       │   │       ├── Meridian.ProviderSdk.csproj.nuget.g.targets
│   │       │   │       ├── project.assets.json
│   │       │   │       └── project.nuget.cache
│   │       │   ├── Meridian.Risk
│   │       │   │   └── obj-codex
│   │       │   │       ├── Meridian.Risk.csproj.nuget.dgspec.json
│   │       │   │       ├── Meridian.Risk.csproj.nuget.g.props
│   │       │   │       ├── Meridian.Risk.csproj.nuget.g.targets
│   │       │   │       ├── project.assets.json
│   │       │   │       └── project.nuget.cache
│   │       │   ├── Meridian.Storage
│   │       │   │   └── obj-codex
│   │       │   │       ├── Meridian.Storage.csproj.nuget.dgspec.json
│   │       │   │       ├── Meridian.Storage.csproj.nuget.g.props
│   │       │   │       ├── Meridian.Storage.csproj.nuget.g.targets
│   │       │   │       ├── project.assets.json
│   │       │   │       └── project.nuget.cache
│   │       │   ├── Meridian.Strategies
│   │       │   │   └── obj-codex
│   │       │   │       ├── Meridian.Strategies.csproj.nuget.dgspec.json
│   │       │   │       ├── Meridian.Strategies.csproj.nuget.g.props
│   │       │   │       ├── Meridian.Strategies.csproj.nuget.g.targets
│   │       │   │       ├── project.assets.json
│   │       │   │       └── project.nuget.cache
│   │       │   └── Meridian.Ui.Shared
│   │       │       └── obj-codex
│   │       │           ├── Meridian.Ui.Shared.csproj.nuget.dgspec.json
│   │       │           ├── Meridian.Ui.Shared.csproj.nuget.g.props
│   │       │           ├── Meridian.Ui.Shared.csproj.nuget.g.targets
│   │       │           ├── project.assets.json
│   │       │           └── project.nuget.cache
│   │       └── tests
│   │           └── Meridian.Tests
│   │               └── obj-codex
│   │                   ├── Meridian.Tests.csproj.nuget.dgspec.json
│   │                   ├── Meridian.Tests.csproj.nuget.g.props
│   │                   ├── Meridian.Tests.csproj.nuget.g.targets
│   │                   ├── project.assets.json
│   │                   └── project.nuget.cache
│   └── docs
│       ├── INDEX.md
│       ├── README.md
│       ├── assessments
│       │   ├── ARTIFACT_ACTIONS_DOWNGRADE.md
│       │   ├── AUDIT_REPORT_2026_03_20.md
│       │   ├── CLEANUP_OPPORTUNITIES.md
│       │   ├── CLEANUP_SUMMARY.md
│       │   ├── CONFIG_CONSOLIDATION_REPORT.md
│       │   ├── DUPLICATE_CODE_ANALYSIS.md
│       │   ├── H3_DEBUG_CODE_ANALYSIS.md
│       │   ├── UWP_COMPREHENSIVE_AUDIT.md
│       │   ├── canonicalization-drift-report.local.md
│       │   ├── desktop-devex-high-value-improvements.md
│       │   ├── desktop-end-user-improvements-shortlist.md
│       │   ├── desktop-end-user-improvements.md
│       │   ├── desktop-ui-alternatives-evaluation.md
│       │   └── high-impact-improvements-brainstorm.md
│       ├── c4-context-legacy.png
│       ├── c4-context-legacy.puml
│       ├── migrations
│       │   ├── desktop-app-xaml-compiler-errors.md
│       │   ├── uwp-development-roadmap.md
│       │   ├── uwp-release-checklist.md
│       │   └── uwp-to-wpf-migration.md
│       ├── plans
│       │   ├── QUICKSTART_2026-01-08.md
│       │   ├── REPOSITORY_REORGANIZATION_PLAN.md
│       │   ├── WORKFLOW_IMPROVEMENTS_2026-01-08.md
│       │   ├── consolidation.md
│       │   └── repository-cleanup-action-plan.md
│       └── summaries
│           ├── 2026-02_PR_SUMMARY.md
│           ├── 2026-02_UI_IMPROVEMENTS_SUMMARY.md
│           ├── 2026-02_VISUAL_CODE_EXAMPLES.md
│           ├── CHANGES_SUMMARY.md
│           ├── CS0101_FIX_SUMMARY.md
│           ├── IMPROVEMENTS_2026-02.md
│           ├── REDESIGN_IMPROVEMENTS.md
│           ├── ROADMAP_UPDATE_SUMMARY.md
│           ├── STRUCTURAL_IMPROVEMENTS_2026-02.md
│           └── TEST_MATRIX_FIX_SUMMARY.md
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
│       │   ├── create-todo-issues.py
│       │   ├── generate-changelog.py
│       │   ├── generate-coverage.py
│       │   ├── generate-dependency-graph.py
│       │   ├── generate-health-dashboard.py
│       │   ├── generate-metrics-dashboard.py
│       │   ├── generate-prompts.py
│       │   ├── generate-structure-docs.py
│       │   ├── repair-links.py
│       │   ├── rules-engine.py
│       │   ├── run-docs-automation.py
│       │   ├── scan-todos.py
│       │   ├── sync-readme-badges.py
│       │   ├── test-scripts.py
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
│       │   ├── install.ps1
│       │   └── install.sh
│       ├── lib
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
│       │   └── test_validate_budget.py
│       ├── validate-tooling-metadata.py
│       └── validate_budget.py
├── config
│   ├── appsettings.sample.json
│   ├── appsettings.schema.json
│   ├── condition-codes.json
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
├── desktop.ini
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
│   │   ├── 016-platform-architecture-migration.md
│   │   ├── ADR-015-platform-restructuring.md
│   │   ├── README.md
│   │   └── _template.md
│   ├── ai
│   │   ├── README.md
│   │   ├── agents
│   │   │   └── README.md
│   │   ├── ai-known-errors.md
│   │   ├── claude
│   │   │   ├── CLAUDE.actions.md
│   │   │   ├── CLAUDE.api.md
│   │   │   ├── CLAUDE.domain-naming.md
│   │   │   ├── CLAUDE.fsharp.md
│   │   │   ├── CLAUDE.providers.md
│   │   │   ├── CLAUDE.repo-updater.md
│   │   │   ├── CLAUDE.storage.md
│   │   │   ├── CLAUDE.structure.md
│   │   │   └── CLAUDE.testing.md
│   │   ├── copilot
│   │   │   ├── ai-sync-workflow.md
│   │   │   └── instructions.md
│   │   ├── instructions
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
│   │   ├── layer-boundaries.md
│   │   ├── overview.md
│   │   ├── provider-management.md
│   │   ├── storage-design.md
│   │   ├── ui-redesign.md
│   │   └── why-this-architecture.md
│   ├── audits
│   │   ├── AUDIT_REPORT.md
│   │   ├── BACKTEST_ENGINE_CODE_REVIEW_2026_03_25.md
│   │   ├── CODE_REVIEW_2026-03-16.md
│   │   ├── FURTHER_SIMPLIFICATION_OPPORTUNITIES.md
│   │   ├── README.md
│   │   ├── audit-architecture-results.txt
│   │   ├── audit-code-results.json
│   │   ├── audit-results-full.json
│   │   └── prompt-generation-results.json
│   ├── development
│   │   ├── README.md
│   │   ├── adding-custom-rules.md
│   │   ├── build-observability.md
│   │   ├── central-package-management.md
│   │   ├── desktop-testing-guide.md
│   │   ├── documentation-automation.md
│   │   ├── documentation-contribution-guide.md
│   │   ├── expanding-scripts.md
│   │   ├── fsharp-decision-rule.md
│   │   ├── git-hooks.md
│   │   ├── github-actions-summary.md
│   │   ├── github-actions-testing.md
│   │   ├── otlp-trace-visualization.md
│   │   ├── policies
│   │   │   └── desktop-support-policy.md
│   │   ├── provider-implementation.md
│   │   ├── refactor-map.md
│   │   ├── repository-organization-guide.md
│   │   ├── repository-rule-set.md
│   │   ├── tooling-workflow-backlog.md
│   │   ├── ui-fixture-mode-guide.md
│   │   └── wpf-implementation-notes.md
│   ├── diagrams
│   │   ├── README.md
│   │   ├── backfill-workflow.dot
│   │   ├── backtesting-engine.dot
│   │   ├── c4-level1-context.dot
│   │   ├── c4-level1-context.png
│   │   ├── c4-level1-context.svg
│   │   ├── c4-level2-containers.dot
│   │   ├── c4-level2-containers.png
│   │   ├── c4-level2-containers.svg
│   │   ├── c4-level3-components.dot
│   │   ├── c4-level3-components.png
│   │   ├── c4-level3-components.svg
│   │   ├── cli-commands.dot
│   │   ├── cli-commands.png
│   │   ├── cli-commands.svg
│   │   ├── configuration-management.dot
│   │   ├── data-flow.dot
│   │   ├── data-flow.png
│   │   ├── data-flow.svg
│   │   ├── data-quality-monitoring.dot
│   │   ├── deployment-options.dot
│   │   ├── deployment-options.png
│   │   ├── deployment-options.svg
│   │   ├── domain-event-model.dot
│   │   ├── event-pipeline-sequence.dot
│   │   ├── event-pipeline-sequence.png
│   │   ├── event-pipeline-sequence.svg
│   │   ├── execution-layer.dot
│   │   ├── fsharp-domain.dot
│   │   ├── mcp-server.dot
│   │   ├── onboarding-flow.dot
│   │   ├── onboarding-flow.png
│   │   ├── onboarding-flow.svg
│   │   ├── project-dependencies.dot
│   │   ├── project-dependencies.png
│   │   ├── project-dependencies.svg
│   │   ├── provider-architecture.dot
│   │   ├── provider-architecture.png
│   │   ├── provider-architecture.svg
│   │   ├── resilience-patterns.dot
│   │   ├── resilience-patterns.png
│   │   ├── resilience-patterns.svg
│   │   ├── storage-architecture.dot
│   │   ├── storage-architecture.png
│   │   ├── storage-architecture.svg
│   │   ├── strategy-lifecycle.dot
│   │   ├── symbol-search-resolution.dot
│   │   ├── ui-implementation-flow.dot
│   │   ├── ui-implementation-flow.svg
│   │   ├── ui-navigation-map.dot
│   │   ├── ui-navigation-map.svg
│   │   └── uml
│   │       ├── README.md
│   │       ├── activity-diagram-backfill.png
│   │       ├── activity-diagram-backfill.puml
│   │       ├── activity-diagram.png
│   │       ├── activity-diagram.puml
│   │       ├── communication-diagram.png
│   │       ├── communication-diagram.puml
│   │       ├── interaction-overview-diagram.png
│   │       ├── interaction-overview-diagram.puml
│   │       ├── sequence-diagram-backfill.png
│   │       ├── sequence-diagram-backfill.puml
│   │       ├── sequence-diagram.png
│   │       ├── sequence-diagram.puml
│   │       ├── state-diagram-backfill.png
│   │       ├── state-diagram-backfill.puml
│   │       ├── state-diagram-orderbook.png
│   │       ├── state-diagram-orderbook.puml
│   │       ├── state-diagram-trade-sequence.png
│   │       ├── state-diagram-trade-sequence.puml
│   │       ├── state-diagram.png
│   │       ├── state-diagram.puml
│   │       ├── timing-diagram-backfill.png
│   │       ├── timing-diagram-backfill.puml
│   │       ├── timing-diagram.png
│   │       ├── timing-diagram.puml
│   │       ├── use-case-diagram.png
│   │       └── use-case-diagram.puml
│   ├── docfx
│   │   ├── README.md
│   │   ├── api
│   │   │   └── index.md
│   │   ├── docfx.json
│   │   └── filterConfig.yml
│   ├── evaluations
│   │   ├── 2026-03-brainstorm-next-frontier.md
│   │   ├── README.md
│   │   ├── assembly-performance-opportunities.md
│   │   ├── competitive-analysis-2026-03.md
│   │   ├── data-quality-monitoring-evaluation.md
│   │   ├── desktop-improvements-executive-summary.md
│   │   ├── desktop-platform-improvements-implementation-guide.md
│   │   ├── high-impact-improvement-brainstorm-2026-03.md
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
│   │   └── workflows-overview.md
│   ├── getting-started
│   │   └── README.md
│   ├── integrations
│   │   ├── README.md
│   │   ├── fsharp-integration.md
│   │   ├── language-strategy.md
│   │   └── lean-integration.md
│   ├── operations
│   │   ├── README.md
│   │   ├── deployment.md
│   │   ├── high-availability.md
│   │   ├── msix-packaging.md
│   │   ├── operator-runbook.md
│   │   ├── performance-tuning.md
│   │   ├── portable-data-packager.md
│   │   └── service-level-objectives.md
│   ├── plans
│   │   ├── README.md
│   │   ├── assembly-performance-roadmap.md
│   │   ├── codebase-audit-cleanup-roadmap.md
│   │   ├── fund-management-module-implementation-backlog.md
│   │   ├── fund-management-pr-sequenced-roadmap.md
│   │   ├── fund-management-product-vision-and-capability-matrix.md
│   │   ├── governance-fund-ops-blueprint.md
│   │   ├── l3-inference-implementation-plan.md
│   │   ├── meridian-6-week-roadmap.md
│   │   ├── meridian-database-blueprint.md
│   │   ├── quant-script-environment-blueprint.md
│   │   ├── readability-refactor-baseline.md
│   │   ├── readability-refactor-roadmap.md
│   │   ├── readability-refactor-technical-design-pack.md
│   │   ├── security-master-productization-roadmap.md
│   │   ├── trading-workstation-migration-blueprint.md
│   │   ├── ufl-bond-target-state-v2.md
│   │   ├── ufl-cash-sweep-target-state-v2.md
│   │   ├── ufl-certificate-of-deposit-target-state-v2.md
│   │   ├── ufl-commercial-paper-target-state-v2.md
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
│   │   ├── workstation-release-readiness-blueprint.md
│   │   └── workstation-sprint-1-implementation-backlog.md
│   ├── providers
│   │   ├── README.md
│   │   ├── alpaca-setup.md
│   │   ├── backfill-guide.md
│   │   ├── data-sources.md
│   │   ├── interactive-brokers-free-equity-reference.md
│   │   ├── interactive-brokers-setup.md
│   │   ├── provider-comparison.md
│   │   ├── security-master-guide.md
│   │   └── stocksharp-connectors.md
│   ├── reference
│   │   ├── README.md
│   │   ├── api-reference.md
│   │   ├── data-dictionary.md
│   │   ├── data-uniformity.md
│   │   ├── design-review-memo.md
│   │   ├── environment-variables.md
│   │   └── open-source-references.md
│   ├── security
│   │   ├── README.md
│   │   └── known-vulnerabilities.md
│   ├── status
│   │   ├── CHANGELOG.md
│   │   ├── DOCUMENTATION_TRIAGE_2026_03_21.md
│   │   ├── EVALUATIONS_AND_AUDITS.md
│   │   ├── FEATURE_INVENTORY.md
│   │   ├── FULL_IMPLEMENTATION_TODO_2026_03_20.md
│   │   ├── IMPROVEMENTS.md
│   │   ├── README.md
│   │   ├── ROADMAP.md
│   │   ├── ROADMAP_NOW_NEXT_LATER_2026_03_25.md
│   │   ├── TODO.md
│   │   ├── api-docs-report.md
│   │   ├── badge-sync-report.md
│   │   ├── coverage-report.md
│   │   ├── docs-automation-summary.json
│   │   ├── docs-automation-summary.md
│   │   ├── example-validation.md
│   │   ├── health-dashboard.md
│   │   ├── link-repair-report.md
│   │   ├── metrics-dashboard.md
│   │   ├── production-status.md
│   │   └── rules-report.md
│   └── toc.yml
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
│   ├── compare_benchmarks.py
│   ├── dev
│   │   ├── build-ibapi-smoke.ps1
│   │   ├── desktop-dev.ps1
│   │   ├── diagnose-uwp-xaml.ps1
│   │   └── install-git-hooks.sh
│   ├── generate-diagrams.mjs
│   ├── lib
│   │   ├── ui-diagram-generator.mjs
│   │   └── ui-diagram-generator.test.mjs
│   └── report_canonicalization_drift.py
├── src
│   ├── Meridian
│   │   ├── DashboardServerBridge.cs
│   │   ├── GlobalUsings.cs
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
│   │   ├── app.manifest
│   │   ├── runtimeconfig.template.json
│   │   └── wwwroot
│   │       └── templates
│   │           ├── credentials.html
│   │           ├── index.html
│   │           └── index.js
│   ├── Meridian.Application
│   │   ├── Backfill
│   │   │   ├── BackfillCostEstimator.cs
│   │   │   ├── BackfillRequest.cs
│   │   │   ├── BackfillResult.cs
│   │   │   ├── BackfillStatusStore.cs
│   │   │   ├── GapBackfillService.cs
│   │   │   └── HistoricalBackfillService.cs
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
│   │   │   ├── PackageCommands.cs
│   │   │   ├── QueryCommand.cs
│   │   │   ├── SchemaCheckCommand.cs
│   │   │   ├── SelfTestCommand.cs
│   │   │   ├── SymbolCommands.cs
│   │   │   ├── ValidateConfigCommand.cs
│   │   │   └── WalRepairCommand.cs
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
│   │   │   │   ├── MaintenanceFeatureRegistration.cs
│   │   │   │   ├── PipelineFeatureRegistration.cs
│   │   │   │   ├── ProviderFeatureRegistration.cs
│   │   │   │   ├── StorageFeatureRegistration.cs
│   │   │   │   └── SymbolManagementFeatureRegistration.cs
│   │   │   ├── HostAdapters.cs
│   │   │   ├── HostStartup.cs
│   │   │   ├── SecurityMasterStartup.cs
│   │   │   ├── ServiceCompositionRoot.cs
│   │   │   └── Startup
│   │   │       └── SharedStartupBootstrapper.cs
│   │   ├── Config
│   │   │   ├── AppConfigJsonOptions.cs
│   │   │   ├── ConfigDtoMapper.cs
│   │   │   ├── ConfigJsonSchemaGenerator.cs
│   │   │   ├── ConfigValidationHelper.cs
│   │   │   ├── ConfigValidatorCli.cs
│   │   │   ├── ConfigWatcher.cs
│   │   │   ├── ConfigurationPipeline.cs
│   │   │   ├── Credentials
│   │   │   │   ├── CredentialStatus.cs
│   │   │   │   ├── CredentialTestingService.cs
│   │   │   │   ├── OAuthToken.cs
│   │   │   │   ├── OAuthTokenRefreshService.cs
│   │   │   │   └── ProviderCredentialResolver.cs
│   │   │   ├── DeploymentContext.cs
│   │   │   ├── IConfigValidator.cs
│   │   │   ├── SensitiveValueMasker.cs
│   │   │   └── StorageConfigExtensions.cs
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
│   │   ├── DirectLending
│   │   │   ├── DailyAccrualWorker.cs
│   │   │   ├── DirectLendingEventRebuilder.cs
│   │   │   ├── DirectLendingOutboxDispatcher.cs
│   │   │   ├── DirectLendingServiceSupport.cs
│   │   │   ├── DirectLendingWorkflowSupport.cs
│   │   │   ├── DirectLendingWorkflowTopics.cs
│   │   │   ├── IDirectLendingCommandService.cs
│   │   │   ├── IDirectLendingQueryService.cs
│   │   │   ├── IDirectLendingService.cs
│   │   │   ├── InMemoryDirectLendingService.Workflows.cs
│   │   │   ├── InMemoryDirectLendingService.cs
│   │   │   ├── PostgresDirectLendingCommandService.cs
│   │   │   ├── PostgresDirectLendingQueryService.cs
│   │   │   └── PostgresDirectLendingService.cs
│   │   ├── Etl
│   │   │   ├── EtlAbstractions.cs
│   │   │   └── EtlServices.cs
│   │   ├── Filters
│   │   │   └── MarketEventFilter.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Http
│   │   │   ├── BackfillCoordinator.cs
│   │   │   ├── ConfigStore.cs
│   │   │   ├── Endpoints
│   │   │   │   ├── ArchiveMaintenanceEndpoints.cs
│   │   │   │   ├── DataQualityEndpoints.cs
│   │   │   │   ├── PackagingEndpoints.cs
│   │   │   │   └── StatusEndpointHandlers.cs
│   │   │   ├── HtmlTemplateLoader.cs
│   │   │   └── HtmlTemplates.cs
│   │   ├── Indicators
│   │   │   └── TechnicalIndicatorService.cs
│   │   ├── Meridian.Application.csproj
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
│   │   ├── Results
│   │   │   ├── ErrorCode.cs
│   │   │   ├── OperationError.cs
│   │   │   └── Result.cs
│   │   ├── Scheduling
│   │   │   ├── BackfillExecutionLog.cs
│   │   │   ├── BackfillSchedule.cs
│   │   │   ├── BackfillScheduleManager.cs
│   │   │   ├── IOperationalScheduler.cs
│   │   │   ├── OperationalScheduler.cs
│   │   │   └── ScheduledBackfillService.cs
│   │   ├── SecurityMaster
│   │   │   ├── ISecurityMasterQueryService.cs
│   │   │   ├── ISecurityMasterService.cs
│   │   │   ├── ISecurityResolver.cs
│   │   │   ├── SecurityEconomicDefinitionAdapter.cs
│   │   │   ├── SecurityMasterAggregateRebuilder.cs
│   │   │   ├── SecurityMasterCsvParser.cs
│   │   │   ├── SecurityMasterImportService.cs
│   │   │   ├── SecurityMasterMapping.cs
│   │   │   ├── SecurityMasterOptionsValidator.cs
│   │   │   ├── SecurityMasterProjectionService.cs
│   │   │   ├── SecurityMasterProjectionWarmupService.cs
│   │   │   ├── SecurityMasterQueryService.cs
│   │   │   ├── SecurityMasterRebuildOrchestrator.cs
│   │   │   ├── SecurityMasterService.cs
│   │   │   └── SecurityResolver.cs
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
│   │   ├── BatchBacktestService.cs
│   │   ├── CorporateActionAdjustmentService.cs
│   │   ├── Engine
│   │   │   ├── BacktestContext.cs
│   │   │   ├── BacktestEngine.cs
│   │   │   ├── ContingentOrderManager.cs
│   │   │   ├── MultiSymbolMergeEnumerator.cs
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
│   │   ├── Metrics
│   │   │   ├── BacktestMetricsEngine.cs
│   │   │   ├── PostSimulationTcaReporter.cs
│   │   │   └── XirrCalculator.cs
│   │   ├── Plugins
│   │   │   └── StrategyPluginLoader.cs
│   │   └── Portfolio
│   │       ├── ICommissionModel.cs
│   │       └── SimulatedPortfolio.cs
│   ├── Meridian.Backtesting.Sdk
│   │   ├── AssetEvent.cs
│   │   ├── BacktestEngineMode.cs
│   │   ├── BacktestProgressEvent.cs
│   │   ├── BacktestRequest.cs
│   │   ├── BacktestResult.cs
│   │   ├── CashFlowEntry.cs
│   │   ├── FillEvent.cs
│   │   ├── FinancialAccount.cs
│   │   ├── FinancialAccountSnapshot.cs
│   │   ├── GlobalUsings.cs
│   │   ├── IBacktestContext.cs
│   │   ├── IBacktestStrategy.cs
│   │   ├── Ledger
│   │   │   ├── BacktestLedger.cs
│   │   │   ├── JournalEntry.cs
│   │   │   ├── LedgerAccount.cs
│   │   │   ├── LedgerAccountType.cs
│   │   │   ├── LedgerAccounts.cs
│   │   │   └── LedgerEntry.cs
│   │   ├── Meridian.Backtesting.Sdk.csproj
│   │   ├── Order.cs
│   │   ├── PortfolioSnapshot.cs
│   │   ├── Position.cs
│   │   ├── StrategyParameterAttribute.cs
│   │   ├── TcaReportModels.cs
│   │   └── TradeTicket.cs
│   ├── Meridian.Contracts
│   │   ├── Api
│   │   │   ├── BackfillApiModels.cs
│   │   │   ├── ClientModels.cs
│   │   │   ├── ErrorResponse.cs
│   │   │   ├── LiveDataModels.cs
│   │   │   ├── OptionsModels.cs
│   │   │   ├── ProviderCatalog.cs
│   │   │   ├── Quality
│   │   │   │   └── QualityApiModels.cs
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
│   │   ├── Banking
│   │   │   └── BankingModels.cs
│   │   ├── Catalog
│   │   │   ├── DirectoryIndex.cs
│   │   │   ├── ICanonicalSymbolRegistry.cs
│   │   │   ├── StorageCatalog.cs
│   │   │   └── SymbolRegistry.cs
│   │   ├── Configuration
│   │   │   ├── AppConfigDto.cs
│   │   │   ├── DerivativesConfigDto.cs
│   │   │   └── SymbolConfig.cs
│   │   ├── Credentials
│   │   │   ├── CredentialModels.cs
│   │   │   └── ISecretProvider.cs
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
│   │   │   │   └── Trade.cs
│   │   │   ├── ProviderId.cs
│   │   │   ├── StreamId.cs
│   │   │   ├── SubscriptionId.cs
│   │   │   ├── SymbolId.cs
│   │   │   └── VenueCode.cs
│   │   ├── Etl
│   │   │   └── EtlModels.cs
│   │   ├── Export
│   │   │   ├── AnalysisExportModels.cs
│   │   │   ├── ExportPreset.cs
│   │   │   └── StandardPresets.cs
│   │   ├── FundStructure
│   │   │   ├── FundStructureCommands.cs
│   │   │   ├── FundStructureDtos.cs
│   │   │   └── FundStructureQueries.cs
│   │   ├── Manifest
│   │   │   └── DataManifest.cs
│   │   ├── Meridian.Contracts.csproj
│   │   ├── Pipeline
│   │   │   ├── IngestionJob.cs
│   │   │   └── PipelinePolicyConstants.cs
│   │   ├── Schema
│   │   │   ├── EventSchema.cs
│   │   │   └── ISchemaUpcaster.cs
│   │   ├── SecurityMaster
│   │   │   ├── ISecurityMasterAmender.cs
│   │   │   ├── ISecurityMasterQueryService.cs
│   │   │   ├── ISecurityMasterService.cs
│   │   │   ├── SecurityCommands.cs
│   │   │   ├── SecurityDtos.cs
│   │   │   ├── SecurityEvents.cs
│   │   │   ├── SecurityIdentifiers.cs
│   │   │   ├── SecurityMasterOptions.cs
│   │   │   └── SecurityQueries.cs
│   │   ├── Services
│   │   │   └── IConnectivityProbeService.cs
│   │   ├── Session
│   │   │   └── CollectionSession.cs
│   │   ├── Store
│   │   │   └── MarketDataQuery.cs
│   │   └── Workstation
│   │       ├── ReconciliationDtos.cs
│   │       ├── SecurityMasterWorkstationDtos.cs
│   │       └── StrategyRunReadModels.cs
│   ├── Meridian.Core
│   │   ├── Config
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
│   │   │   ├── StockSharpConfig.cs
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
│   │   └── Telemetry
│   │       └── MarketEventIngressTracing.cs
│   ├── Meridian.Execution
│   │   ├── Adapters
│   │   │   ├── BaseBrokerageGateway.cs
│   │   │   ├── BrokerageGatewayAdapter.cs
│   │   │   └── PaperTradingGateway.cs
│   │   ├── BrokerageServiceRegistration.cs
│   │   ├── Exceptions
│   │   │   └── UnsupportedOrderRequestException.cs
│   │   ├── GlobalUsings.cs
│   │   ├── IRiskValidator.cs
│   │   ├── Interfaces
│   │   │   ├── IExecutionContext.cs
│   │   │   ├── ILiveFeedAdapter.cs
│   │   │   └── IOrderGateway.cs
│   │   ├── Meridian.Execution.csproj
│   │   ├── Models
│   │   │   ├── ExecutionMode.cs
│   │   │   ├── ExecutionPosition.cs
│   │   │   ├── IPortfolioState.cs
│   │   │   ├── OrderAcknowledgement.cs
│   │   │   ├── OrderGatewayCapabilities.cs
│   │   │   ├── OrderStatus.cs
│   │   │   └── OrderStatusUpdate.cs
│   │   ├── OrderManagementSystem.cs
│   │   ├── PaperExecutionContext.cs
│   │   ├── PaperTradingGateway.cs
│   │   └── Services
│   │       ├── OrderLifecycleManager.cs
│   │       ├── PaperSessionPersistenceService.cs
│   │       └── PaperTradingPortfolio.cs
│   ├── Meridian.Execution.Sdk
│   │   ├── BrokerageConfiguration.cs
│   │   ├── IBrokerageGateway.cs
│   │   ├── IExecutionGateway.cs
│   │   ├── IOrderManager.cs
│   │   ├── IPositionTracker.cs
│   │   ├── Meridian.Execution.Sdk.csproj
│   │   └── Models.cs
│   ├── Meridian.FSharp
│   │   ├── Calculations
│   │   │   ├── Aggregations.fs
│   │   │   ├── Imbalance.fs
│   │   │   └── Spread.fs
│   │   ├── Canonicalization
│   │   │   └── MappingRules.fs
│   │   ├── Domain
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
│   │   │   └── Sides.fs
│   │   ├── Generated
│   │   │   └── Meridian.FSharp.Interop.g.cs
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
│   │   ├── Interop.fs
│   │   ├── JournalValidation.fs
│   │   ├── LedgerReadModels.fs
│   │   ├── LedgerTypes.fs
│   │   ├── Meridian.FSharp.Ledger.fsproj
│   │   ├── Posting.fs
│   │   ├── Reconciliation.fs
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
│   │   │   │   ├── AlpacaHistoricalDataProvider.cs
│   │   │   │   ├── AlpacaMarketDataClient.cs
│   │   │   │   └── AlpacaSymbolSearchProviderRefactored.cs
│   │   │   ├── AlphaVantage
│   │   │   │   └── AlphaVantageHistoricalDataProvider.cs
│   │   │   ├── Core
│   │   │   │   ├── Backfill
│   │   │   │   │   ├── BackfillJob.cs
│   │   │   │   │   ├── BackfillJobManager.cs
│   │   │   │   │   ├── BackfillRequestQueue.cs
│   │   │   │   │   ├── BackfillWorkerService.cs
│   │   │   │   │   └── PriorityBackfillQueue.cs
│   │   │   │   ├── BackfillProgressTracker.cs
│   │   │   │   ├── BaseHistoricalDataProvider.cs
│   │   │   │   ├── BaseSymbolSearchProvider.cs
│   │   │   │   ├── CompositeHistoricalDataProvider.cs
│   │   │   │   ├── GapAnalysis
│   │   │   │   │   ├── DataGapAnalyzer.cs
│   │   │   │   │   ├── DataGapRepair.cs
│   │   │   │   │   └── DataQualityMonitor.cs
│   │   │   │   ├── IHistoricalDataProvider.cs
│   │   │   │   ├── ISymbolSearchProvider.cs
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
│   │   │   │   ├── EnhancedIBConnectionManager.cs
│   │   │   │   ├── IBApiLimits.cs
│   │   │   │   ├── IBApiVersionValidator.cs
│   │   │   │   ├── IBBrokerageGateway.cs
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
│   │   │   │   ├── PolygonSymbolSearchProvider.cs
│   │   │   │   └── TradingParametersBackfillService.cs
│   │   │   ├── StockSharp
│   │   │   │   ├── Converters
│   │   │   │   │   ├── MessageConverter.cs
│   │   │   │   │   └── SecurityConverter.cs
│   │   │   │   ├── StockSharpBrokerageGateway.cs
│   │   │   │   ├── StockSharpConnectorCapabilities.cs
│   │   │   │   ├── StockSharpConnectorFactory.cs
│   │   │   │   ├── StockSharpHistoricalDataProvider.cs
│   │   │   │   ├── StockSharpMarketDataClient.cs
│   │   │   │   └── StockSharpSymbolSearchProvider.cs
│   │   │   ├── Stooq
│   │   │   │   └── StooqHistoricalDataProvider.cs
│   │   │   ├── Synthetic
│   │   │   │   ├── SyntheticHistoricalDataProvider.cs
│   │   │   │   ├── SyntheticMarketDataClient.cs
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
│   │   └── ProjectLedgerBook.cs
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
│   │   ├── Program.cs
│   │   ├── Prompts
│   │   │   └── MarketDataPrompts.cs
│   │   ├── Resources
│   │   │   └── MarketDataResources.cs
│   │   └── Tools
│   │       ├── BackfillTools.cs
│   │       ├── ProviderTools.cs
│   │       ├── StorageTools.cs
│   │       └── SymbolTools.cs
│   ├── Meridian.ProviderSdk
│   │   ├── CredentialValidator.cs
│   │   ├── DataSourceAttribute.cs
│   │   ├── DataSourceRegistry.cs
│   │   ├── HistoricalDataCapabilities.cs
│   │   ├── IDataSource.cs
│   │   ├── IHistoricalBarWriter.cs
│   │   ├── IHistoricalDataSource.cs
│   │   ├── IMarketDataClient.cs
│   │   ├── IOptionsChainProvider.cs
│   │   ├── IProviderMetadata.cs
│   │   ├── IProviderModule.cs
│   │   ├── IRealtimeDataSource.cs
│   │   ├── ImplementsAdrAttribute.cs
│   │   ├── Meridian.ProviderSdk.csproj
│   │   └── ProviderHttpUtilities.cs
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
│   │   │   ├── ExportProfile.cs
│   │   │   ├── ExportRequest.cs
│   │   │   ├── ExportResult.cs
│   │   │   ├── ExportValidator.cs
│   │   │   └── ExportVerificationReport.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Interfaces
│   │   │   ├── IMarketDataStore.cs
│   │   │   ├── ISourceRegistry.cs
│   │   │   ├── IStorageCatalogService.cs
│   │   │   ├── IStoragePolicy.cs
│   │   │   ├── IStorageSink.cs
│   │   │   └── ISymbolRegistryService.cs
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
│   │   │   ├── ISecurityMasterEventStore.cs
│   │   │   ├── ISecurityMasterSnapshotStore.cs
│   │   │   ├── ISecurityMasterStore.cs
│   │   │   ├── Migrations
│   │   │   │   ├── 001_security_master.sql
│   │   │   │   ├── 002_security_master_fts.sql
│   │   │   │   └── 003_security_master_corp_actions.sql
│   │   │   ├── PostgresSecurityMasterEventStore.cs
│   │   │   ├── PostgresSecurityMasterSnapshotStore.cs
│   │   │   ├── PostgresSecurityMasterStore.cs
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
│   │   │   ├── LifecyclePolicyEngine.cs
│   │   │   ├── MaintenanceScheduler.cs
│   │   │   ├── MetadataTagService.cs
│   │   │   ├── ParquetConversionService.cs
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
│   │   │   ├── IStrategyLifecycle.cs
│   │   │   └── IStrategyRepository.cs
│   │   ├── Meridian.Strategies.csproj
│   │   ├── Models
│   │   │   ├── RunType.cs
│   │   │   ├── StrategyRunEntry.cs
│   │   │   └── StrategyStatus.cs
│   │   ├── Promotions
│   │   │   └── BacktestToLivePromoter.cs
│   │   ├── Services
│   │   │   ├── CashFlowProjectionService.cs
│   │   │   ├── IReconciliationRunRepository.cs
│   │   │   ├── IReconciliationRunService.cs
│   │   │   ├── ISecurityReferenceLookup.cs
│   │   │   ├── InMemoryReconciliationRunRepository.cs
│   │   │   ├── LedgerReadService.cs
│   │   │   ├── PortfolioReadService.cs
│   │   │   ├── PromotionService.cs
│   │   │   ├── ReconciliationProjectionService.cs
│   │   │   ├── ReconciliationRunService.cs
│   │   │   ├── StrategyLifecycleManager.cs
│   │   │   └── StrategyRunReadService.cs
│   │   └── Storage
│   │       └── StrategyRunStore.cs
│   ├── Meridian.Ui
│   │   ├── Meridian.Ui.csproj
│   │   ├── Program.cs
│   │   ├── app.manifest
│   │   ├── dashboard
│   │   │   ├── index.html
│   │   │   ├── package-lock.json
│   │   │   ├── package.json
│   │   │   ├── postcss.config.cjs
│   │   │   ├── src
│   │   │   │   ├── app.tsx
│   │   │   │   ├── components
│   │   │   │   │   ├── meridian
│   │   │   │   │   │   ├── command-palette.test.tsx
│   │   │   │   │   │   ├── command-palette.tsx
│   │   │   │   │   │   ├── entity-data-table.test.tsx
│   │   │   │   │   │   ├── entity-data-table.tsx
│   │   │   │   │   │   ├── metric-card.tsx
│   │   │   │   │   │   ├── run-status-badge.tsx
│   │   │   │   │   │   ├── workspace-header.tsx
│   │   │   │   │   │   └── workspace-nav.tsx
│   │   │   │   │   └── ui
│   │   │   │   │       ├── badge.tsx
│   │   │   │   │       ├── button.tsx
│   │   │   │   │       ├── card.tsx
│   │   │   │   │       ├── command.tsx
│   │   │   │   │       ├── dialog.tsx
│   │   │   │   │       └── input.tsx
│   │   │   │   ├── hooks
│   │   │   │   │   └── use-workstation-data.ts
│   │   │   │   ├── lib
│   │   │   │   │   ├── api.ts
│   │   │   │   │   ├── utils.ts
│   │   │   │   │   └── workspace.ts
│   │   │   │   ├── main.tsx
│   │   │   │   ├── screens
│   │   │   │   │   ├── data-operations-screen.test.tsx
│   │   │   │   │   ├── data-operations-screen.tsx
│   │   │   │   │   ├── governance-screen.test.tsx
│   │   │   │   │   ├── governance-screen.tsx
│   │   │   │   │   ├── research-screen.test.tsx
│   │   │   │   │   ├── research-screen.tsx
│   │   │   │   │   ├── trading-screen.test.tsx
│   │   │   │   │   ├── trading-screen.tsx
│   │   │   │   │   └── workspace-placeholder.tsx
│   │   │   │   ├── styles
│   │   │   │   │   └── index.css
│   │   │   │   ├── test
│   │   │   │   │   └── setup.ts
│   │   │   │   └── types.ts
│   │   │   ├── tailwind.config.d.ts
│   │   │   ├── tailwind.config.js
│   │   │   ├── tailwind.config.ts
│   │   │   ├── tsconfig.app.json
│   │   │   ├── tsconfig.app.tsbuildinfo
│   │   │   ├── tsconfig.json
│   │   │   ├── tsconfig.node.json
│   │   │   ├── tsconfig.node.tsbuildinfo
│   │   │   ├── vite.config.d.ts
│   │   │   ├── vite.config.js
│   │   │   └── vite.config.ts
│   │   └── wwwroot
│   │       ├── static
│   │       │   └── dashboard.css
│   │       └── workstation
│   │           ├── assets
│   │           │   ├── index-BLxm5sCJ.js
│   │           │   └── index-erdiJ_gu.css
│   │           └── index.html
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
│   │       ├── DiagnosticsService.cs
│   │       ├── ErrorHandlingService.cs
│   │       ├── ErrorMessages.cs
│   │       ├── EventReplayService.cs
│   │       ├── ExportPresetServiceBase.cs
│   │       ├── FixtureDataService.cs
│   │       ├── FixtureModeDetector.cs
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
│   │       ├── QualityArchiveStore.cs
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
│   │   ├── DtoExtensions.cs
│   │   ├── Endpoints
│   │   │   ├── AdminEndpoints.cs
│   │   │   ├── AnalyticsEndpoints.cs
│   │   │   ├── ApiKeyMiddleware.cs
│   │   │   ├── AuthEndpoints.cs
│   │   │   ├── AuthenticationMode.cs
│   │   │   ├── BackfillEndpoints.cs
│   │   │   ├── BackfillScheduleEndpoints.cs
│   │   │   ├── BankingEndpoints.cs
│   │   │   ├── CalendarEndpoints.cs
│   │   │   ├── CanonicalizationEndpoints.cs
│   │   │   ├── CatalogEndpoints.cs
│   │   │   ├── CheckpointEndpoints.cs
│   │   │   ├── ConfigEndpoints.cs
│   │   │   ├── CppTraderEndpoints.cs
│   │   │   ├── CronEndpoints.cs
│   │   │   ├── DiagnosticsEndpoints.cs
│   │   │   ├── DirectLendingEndpoints.cs
│   │   │   ├── EndpointHelpers.cs
│   │   │   ├── ExecutionEndpoints.cs
│   │   │   ├── ExportEndpoints.cs
│   │   │   ├── FailoverEndpoints.cs
│   │   │   ├── HealthEndpoints.cs
│   │   │   ├── HistoricalEndpoints.cs
│   │   │   ├── IBEndpoints.cs
│   │   │   ├── IngestionJobEndpoints.cs
│   │   │   ├── LeanEndpoints.cs
│   │   │   ├── LiveDataEndpoints.cs
│   │   │   ├── LoginSessionMiddleware.cs
│   │   │   ├── MaintenanceScheduleEndpoints.cs
│   │   │   ├── MessagingEndpoints.cs
│   │   │   ├── OptionsEndpoints.cs
│   │   │   ├── PathValidation.cs
│   │   │   ├── PromotionEndpoints.cs
│   │   │   ├── ProviderEndpoints.cs
│   │   │   ├── ProviderExtendedEndpoints.cs
│   │   │   ├── ReplayEndpoints.cs
│   │   │   ├── ResilienceEndpoints.cs
│   │   │   ├── SamplingEndpoints.cs
│   │   │   ├── SecurityMasterEndpoints.cs
│   │   │   ├── StatusEndpoints.cs
│   │   │   ├── StorageEndpoints.cs
│   │   │   ├── StorageQualityEndpoints.cs
│   │   │   ├── StrategyLifecycleEndpoints.cs
│   │   │   ├── SubscriptionEndpoints.cs
│   │   │   ├── SymbolEndpoints.cs
│   │   │   ├── SymbolMappingEndpoints.cs
│   │   │   ├── UiEndpoints.cs
│   │   │   └── WorkstationEndpoints.cs
│   │   ├── GlobalUsings.cs
│   │   ├── HtmlTemplateGenerator.Login.cs
│   │   ├── HtmlTemplateGenerator.Scripts.cs
│   │   ├── HtmlTemplateGenerator.Styles.cs
│   │   ├── HtmlTemplateGenerator.cs
│   │   ├── LeanAutoExportService.cs
│   │   ├── LeanSymbolMapper.cs
│   │   ├── LoginSessionService.cs
│   │   ├── Meridian.Ui.Shared.csproj
│   │   ├── Services
│   │   │   ├── BackfillCoordinator.cs
│   │   │   ├── ConfigStore.cs
│   │   │   └── SecurityMasterSecurityReferenceLookup.cs
│   │   └── UserProfileRegistry.cs
│   └── Meridian.Wpf
│       ├── App.xaml
│       ├── App.xaml.cs
│       ├── AssemblyInfo.cs
│       ├── Contracts
│       │   ├── IConnectionService.cs
│       │   └── INavigationService.cs
│       ├── Converters
│       │   ├── BoolToStringConverter.cs
│       │   ├── BoolToVisibilityConverter.cs
│       │   ├── InvertBoolConverter.cs
│       │   └── NullToCollapsedConverter.cs
│       ├── GlobalUsings.cs
│       ├── MainWindow.xaml
│       ├── MainWindow.xaml.cs
│       ├── Meridian.Wpf.csproj
│       ├── Models
│       │   ├── ActionEntry.cs
│       │   ├── ActivityLogModels.cs
│       │   ├── AppConfig.cs
│       │   ├── BackfillModels.cs
│       │   ├── DashboardModels.cs
│       │   ├── DataQualityModels.cs
│       │   ├── LeanModels.cs
│       │   ├── LiveDataModels.cs
│       │   ├── NotificationModels.cs
│       │   ├── OrderBookModels.cs
│       │   ├── PaneLayout.cs
│       │   ├── ProviderHealthModels.cs
│       │   ├── StorageDisplayModels.cs
│       │   ├── SymbolsModels.cs
│       │   ├── WorkspaceDefinition.cs
│       │   ├── WorkspaceRegistry.cs
│       │   └── WorkspaceShellModels.cs
│       ├── README.md
│       ├── Services
│       │   ├── AgentLoopService.cs
│       │   ├── ArchiveHealthService.cs
│       │   ├── BackendServiceManager.cs
│       │   ├── BackgroundTaskSchedulerService.cs
│       │   ├── BacktestService.cs
│       │   ├── BrushRegistry.cs
│       │   ├── ClipboardWatcherService.cs
│       │   ├── ConfigService.cs
│       │   ├── ConnectionService.cs
│       │   ├── ContextMenuService.cs
│       │   ├── CredentialService.cs
│       │   ├── DropImportService.cs
│       │   ├── ExportFormat.cs
│       │   ├── ExportPresetService.cs
│       │   ├── FirstRunService.cs
│       │   ├── FormValidationService.cs
│       │   ├── GlobalHotkeyService.cs
│       │   ├── ICommandContextProvider.cs
│       │   ├── InfoBarService.cs
│       │   ├── JumpListService.cs
│       │   ├── KeyboardShortcutService.cs
│       │   ├── LoggingService.cs
│       │   ├── MessagingService.cs
│       │   ├── NavigationService.cs
│       │   ├── NotificationService.cs
│       │   ├── OfflineTrackingPersistenceService.cs
│       │   ├── PendingOperationsQueueService.cs
│       │   ├── RetentionAssuranceService.cs
│       │   ├── RunMatService.cs
│       │   ├── SchemaService.cs
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
│       │   ├── TypeForwards.cs
│       │   ├── WatchlistService.cs
│       │   └── WorkspaceService.cs
│       ├── Styles
│       │   ├── Animations.xaml
│       │   ├── AppStyles.xaml
│       │   ├── IconResources.xaml
│       │   ├── ThemeControls.xaml
│       │   ├── ThemeSurfaces.xaml
│       │   ├── ThemeTokens.xaml
│       │   └── ThemeTypography.xaml
│       ├── ViewModels
│       │   ├── ActivityLogViewModel.cs
│       │   ├── AgentViewModel.cs
│       │   ├── BackfillViewModel.cs
│       │   ├── BacktestViewModel.cs
│       │   ├── BatchBacktestViewModel.cs
│       │   ├── BindableBase.cs
│       │   ├── ChartingPageViewModel.cs
│       │   ├── ClusterStatusViewModel.cs
│       │   ├── DashboardViewModel.cs
│       │   ├── DataQualityViewModel.cs
│       │   ├── DiagnosticsPageViewModel.cs
│       │   ├── DirectLendingViewModel.cs
│       │   ├── ExportPresetsViewModel.cs
│       │   ├── IPageActionBarProvider.cs
│       │   ├── LeanIntegrationViewModel.cs
│       │   ├── LiveDataViewerViewModel.cs
│       │   ├── MainPageViewModel.cs
│       │   ├── NotificationCenterViewModel.cs
│       │   ├── OrderBookHeatmapViewModel.cs
│       │   ├── OrderBookViewModel.cs
│       │   ├── PluginManagementViewModel.cs
│       │   ├── ProviderHealthViewModel.cs
│       │   ├── ProviderPageModels.cs
│       │   ├── QualityArchiveViewModel.cs
│       │   ├── QuoteFloatViewModel.cs
│       │   ├── RunMatViewModel.cs
│       │   ├── SecurityMasterDeactivateViewModel.cs
│       │   ├── SecurityMasterEditViewModel.cs
│       │   ├── SecurityMasterViewModel.cs
│       │   ├── ServiceManagerViewModel.cs
│       │   ├── SplitPaneViewModel.cs
│       │   ├── StatusBarViewModel.cs
│       │   ├── StrategyRunBrowserViewModel.cs
│       │   ├── StrategyRunDetailViewModel.cs
│       │   ├── StrategyRunLedgerViewModel.cs
│       │   ├── StrategyRunPortfolioViewModel.cs
│       │   ├── SymbolsPageViewModel.cs
│       │   └── TickerStripViewModel.cs
│       └── Views
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
│           ├── AnalysisExportPage.xaml
│           ├── AnalysisExportPage.xaml.cs
│           ├── AnalysisExportWizardPage.xaml
│           ├── AnalysisExportWizardPage.xaml.cs
│           ├── ArchiveHealthPage.xaml
│           ├── ArchiveHealthPage.xaml.cs
│           ├── BackfillPage.xaml
│           ├── BackfillPage.xaml.cs
│           ├── BacktestPage.xaml
│           ├── BacktestPage.xaml.cs
│           ├── BatchBacktestPage.xaml
│           ├── BatchBacktestPage.xaml.cs
│           ├── ChartingPage.xaml
│           ├── ChartingPage.xaml.cs
│           ├── ClusterStatusPage.xaml
│           ├── ClusterStatusPage.xaml.cs
│           ├── CollectionSessionPage.xaml
│           ├── CollectionSessionPage.xaml.cs
│           ├── CommandPaletteWindow.xaml
│           ├── CommandPaletteWindow.xaml.cs
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
│           ├── EventReplayPage.xaml
│           ├── EventReplayPage.xaml.cs
│           ├── ExportPresetsPage.xaml
│           ├── ExportPresetsPage.xaml.cs
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
│           ├── ProviderHealthPage.xaml
│           ├── ProviderHealthPage.xaml.cs
│           ├── ProviderPage.xaml
│           ├── ProviderPage.xaml.cs
│           ├── QualityArchivePage.xaml
│           ├── QualityArchivePage.xaml.cs
│           ├── QuoteFloatWindow.xaml
│           ├── QuoteFloatWindow.xaml.cs
│           ├── ResearchWorkspaceShellPage.xaml
│           ├── ResearchWorkspaceShellPage.xaml.cs
│           ├── RetentionAssurancePage.xaml
│           ├── RetentionAssurancePage.xaml.cs
│           ├── RunDetailPage.xaml
│           ├── RunDetailPage.xaml.cs
│           ├── RunLedgerPage.xaml
│           ├── RunLedgerPage.xaml.cs
│           ├── RunMatPage.xaml
│           ├── RunMatPage.xaml.cs
│           ├── RunPortfolioPage.xaml
│           ├── RunPortfolioPage.xaml.cs
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
│           ├── WorkspacePage.xaml
│           └── WorkspacePage.xaml.cs
├── tests
│   ├── Directory.Build.props
│   ├── Meridian.Backtesting.Tests
│   │   ├── BacktestEngineIntegrationTests.cs
│   │   ├── BacktestRequestConfigTests.cs
│   │   ├── BracketOrderTests.cs
│   │   ├── CorporateActionAdjustmentServiceTests.cs
│   │   ├── FillModelExpansionTests.cs
│   │   ├── FillModelTests.cs
│   │   ├── GlobalUsings.cs
│   │   ├── LedgerQueryTests.cs
│   │   ├── MarketImpactFillModelTests.cs
│   │   ├── Meridian.Backtesting.Tests.csproj
│   │   ├── SimulatedPortfolioTests.cs
│   │   ├── TcaReporterTests.cs
│   │   ├── XirrCalculatorTests.cs
│   │   └── YahooFinanceBacktestIntegrationTests.cs
│   ├── Meridian.DirectLending.Tests
│   │   ├── BankTransactionSeedTests.cs
│   │   ├── DirectLendingPostgresIntegrationTests.cs
│   │   ├── DirectLendingPostgresTestDatabase.cs
│   │   ├── DirectLendingServiceTests.cs
│   │   ├── DirectLendingWorkflowTests.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.DirectLending.Tests.csproj
│   │   └── PaymentApprovalTests.cs
│   ├── Meridian.FSharp.Tests
│   │   ├── CalculationTests.fs
│   │   ├── CanonicalizationTests.fs
│   │   ├── CashFlowProjectorTests.fs
│   │   ├── DirectLendingInteropTests.fs
│   │   ├── DomainTests.fs
│   │   ├── LedgerKernelTests.fs
│   │   ├── Meridian.FSharp.Tests.fsproj
│   │   ├── PipelineTests.fs
│   │   ├── RiskPolicyTests.fs
│   │   ├── TradingTransitionTests.fs
│   │   └── ValidationTests.fs
│   ├── Meridian.McpServer.Tests
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.McpServer.Tests.csproj
│   │   └── Tools
│   │       ├── BackfillToolsTests.cs
│   │       └── StorageToolsTests.cs
│   ├── Meridian.Tests
│   │   ├── Application
│   │   │   ├── Backfill
│   │   │   │   ├── AdditionalProviderContractTests.cs
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
│   │   │   │   └── TwelveDataNasdaqProviderContractTests.cs
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
│   │   │   │   ├── DryRunCommandTests.cs
│   │   │   │   ├── HelpCommandTests.cs
│   │   │   │   ├── PackageCommandsTests.cs
│   │   │   │   ├── SelfTestCommandTests.cs
│   │   │   │   ├── SymbolCommandsTests.cs
│   │   │   │   └── ValidateConfigCommandTests.cs
│   │   │   ├── Composition
│   │   │   │   ├── SecurityMasterStartupTests.cs
│   │   │   │   └── Startup
│   │   │   │       └── SharedStartupBootstrapperTests.cs
│   │   │   ├── Config
│   │   │   │   ├── ConfigJsonSchemaGeneratorTests.cs
│   │   │   │   ├── ConfigSchemaIntegrationTests.cs
│   │   │   │   ├── ConfigValidationPipelineTests.cs
│   │   │   │   ├── ConfigValidatorTests.cs
│   │   │   │   └── ConfigurationUnificationTests.cs
│   │   │   ├── Coordination
│   │   │   │   ├── ClusterCoordinatorServiceTests.cs
│   │   │   │   ├── LeaseManagerTests.cs
│   │   │   │   ├── SplitBrainDetectorTests.cs
│   │   │   │   └── SubscriptionOrchestratorCoordinationTests.cs
│   │   │   ├── Credentials
│   │   │   │   ├── CredentialStatusTests.cs
│   │   │   │   ├── CredentialTestingServiceTests.cs
│   │   │   │   └── OAuthTokenTests.cs
│   │   │   ├── DirectLendingServiceTests.cs
│   │   │   ├── Etl
│   │   │   │   ├── EtlJobDefinitionStoreTests.cs
│   │   │   │   ├── EtlJobOrchestratorTests.cs
│   │   │   │   └── EtlNormalizationServiceTests.cs
│   │   │   ├── Indicators
│   │   │   │   └── TechnicalIndicatorServiceTests.cs
│   │   │   ├── Monitoring
│   │   │   │   ├── BackpressureAlertServiceTests.cs
│   │   │   │   ├── BadTickFilterTests.cs
│   │   │   │   ├── DataQuality
│   │   │   │   │   ├── DataFreshnessSlaMonitorTests.cs
│   │   │   │   │   ├── DataQualityTests.cs
│   │   │   │   │   └── LiquidityProfileTests.cs
│   │   │   │   ├── ErrorRingBufferTests.cs
│   │   │   │   ├── PriceContinuityCheckerTests.cs
│   │   │   │   ├── PrometheusMetricsTests.cs
│   │   │   │   ├── ProviderDegradationScorerTests.cs
│   │   │   │   ├── ProviderLatencyServiceTests.cs
│   │   │   │   ├── SchemaValidationServiceTests.cs
│   │   │   │   ├── SloDefinitionRegistryTests.cs
│   │   │   │   ├── SpreadMonitorTests.cs
│   │   │   │   ├── TickSizeValidatorTests.cs
│   │   │   │   └── TracedEventMetricsTests.cs
│   │   │   ├── Pipeline
│   │   │   │   ├── BackfillProgressTrackerTests.cs
│   │   │   │   ├── BackpressureSignalTests.cs
│   │   │   │   ├── CompositePublisherTests.cs
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
│   │   │   │   ├── SpscRingBufferTests.cs
│   │   │   │   └── WalEventPipelineTests.cs
│   │   │   ├── ReconciliationRunServiceTests.cs
│   │   │   └── Services
│   │   │       ├── CanonicalizingPublisherTests.cs
│   │   │       ├── CliModeResolverTests.cs
│   │   │       ├── ConditionCodeMapperTests.cs
│   │   │       ├── ConfigurationPresetsTests.cs
│   │   │       ├── ConfigurationServiceTests.cs
│   │   │       ├── CronExpressionParserTests.cs
│   │   │       ├── DataQuality
│   │   │       │   ├── AnomalyDetectorTests.cs
│   │   │       │   ├── CompletenessScoreCalculatorTests.cs
│   │   │       │   ├── GapAnalyzerTests.cs
│   │   │       │   └── SequenceErrorTrackerTests.cs
│   │   │       ├── ErrorCodeMappingTests.cs
│   │   │       ├── EventCanonicalizerTests.cs
│   │   │       ├── GracefulShutdownTests.cs
│   │   │       ├── OperationalSchedulerTests.cs
│   │   │       ├── OptionsChainServiceTests.cs
│   │   │       ├── PreflightCheckerTests.cs
│   │   │       ├── TradingCalendarTests.cs
│   │   │       └── VenueMicMapperTests.cs
│   │   ├── Architecture
│   │   │   └── LayerBoundaryTests.cs
│   │   ├── Domain
│   │   │   ├── Collectors
│   │   │   │   ├── L3OrderBookCollectorTests.cs
│   │   │   │   ├── LiveDataAccessTests.cs
│   │   │   │   ├── MarketDepthCollectorTests.cs
│   │   │   │   ├── OptionDataCollectorTests.cs
│   │   │   │   ├── QuoteCollectorTests.cs
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
│   │   ├── Execution
│   │   │   ├── BrokerageGatewayAdapterTests.cs
│   │   │   ├── OrderManagementSystemTests.cs
│   │   │   ├── PaperSessionPersistenceServiceTests.cs
│   │   │   ├── PaperTradingGatewayTests.cs
│   │   │   └── PaperTradingPortfolioTests.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Infrastructure
│   │   │   ├── CppTrader
│   │   │   │   └── CppTraderOrderGatewayTests.cs
│   │   │   ├── DataSources
│   │   │   │   └── CredentialConfigTests.cs
│   │   │   ├── Etl
│   │   │   │   └── CsvPartnerFileParserTests.cs
│   │   │   ├── Providers
│   │   │   │   ├── AlpacaCredentialAndReconnectTests.cs
│   │   │   │   ├── AlpacaMessageParsingTests.cs
│   │   │   │   ├── AlpacaQuotePipelineGoldenTests.cs
│   │   │   │   ├── AlpacaQuoteRoutingTests.cs
│   │   │   │   ├── BackfillRetryAfterTests.cs
│   │   │   │   ├── FailoverAwareMarketDataClientTests.cs
│   │   │   │   ├── Fixtures
│   │   │   │   │   ├── InteractiveBrokers
│   │   │   │   │   │   ├── ib_order_limit_buy_day.json
│   │   │   │   │   │   ├── ib_order_limit_sell_fok.json
│   │   │   │   │   │   ├── ib_order_loc_sell_day.json
│   │   │   │   │   │   ├── ib_order_market_sell_gtc.json
│   │   │   │   │   │   ├── ib_order_moc_sell_day.json
│   │   │   │   │   │   ├── ib_order_stop_buy_ioc.json
│   │   │   │   │   │   ├── ib_order_stop_limit_buy_day.json
│   │   │   │   │   │   └── ib_order_trailing_stop_sell_gtc.json
│   │   │   │   │   └── Polygon
│   │   │   │   │       ├── polygon-recorded-session-aapl.json
│   │   │   │   │       ├── polygon-recorded-session-gld-cboe-sell.json
│   │   │   │   │       ├── polygon-recorded-session-msft-edge.json
│   │   │   │   │       ├── polygon-recorded-session-nvda-multi-batch.json
│   │   │   │   │       └── polygon-recorded-session-spy-etf.json
│   │   │   │   ├── FreeProviderContractTests.cs
│   │   │   │   ├── HistoricalDataProviderContractTests.cs
│   │   │   │   ├── IBOrderSampleTests.cs
│   │   │   │   ├── IBRuntimeGuidanceTests.cs
│   │   │   │   ├── IBSimulationClientContractTests.cs
│   │   │   │   ├── IBSimulationClientTests.cs
│   │   │   │   ├── MarketDataClientContractTests.cs
│   │   │   │   ├── NYSEMessageParsingTests.cs
│   │   │   │   ├── NyseMarketDataClientTests.cs
│   │   │   │   ├── NyseNationalTradesCsvParserTests.cs
│   │   │   │   ├── NyseSharedLifecycleTests.cs
│   │   │   │   ├── NyseTaqCollectorIntegrationTests.cs
│   │   │   │   ├── PolygonMarketDataClientTests.cs
│   │   │   │   ├── PolygonMessageParsingTests.cs
│   │   │   │   ├── PolygonRecordedSessionReplayTests.cs
│   │   │   │   ├── PolygonSubscriptionTests.cs
│   │   │   │   ├── ProviderResilienceTests.cs
│   │   │   │   ├── StockSharpConnectorFactoryTests.cs
│   │   │   │   ├── StockSharpMessageConversionTests.cs
│   │   │   │   ├── StockSharpSubscriptionTests.cs
│   │   │   │   ├── StreamingFailoverServiceTests.cs
│   │   │   │   └── SyntheticMarketDataProviderTests.cs
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
│   │   │   │   ├── AuthEndpointTests.cs
│   │   │   │   ├── BackfillEndpointTests.cs
│   │   │   │   ├── CatalogEndpointTests.cs
│   │   │   │   ├── ConfigEndpointTests.cs
│   │   │   │   ├── EndpointIntegrationTestBase.cs
│   │   │   │   ├── EndpointTestCollection.cs
│   │   │   │   ├── EndpointTestFixture.cs
│   │   │   │   ├── FailoverEndpointTests.cs
│   │   │   │   ├── HealthEndpointTests.cs
│   │   │   │   ├── HistoricalEndpointTests.cs
│   │   │   │   ├── IBEndpointTests.cs
│   │   │   │   ├── LeanEndpointTests.cs
│   │   │   │   ├── LiveDataEndpointTests.cs
│   │   │   │   ├── MaintenanceEndpointTests.cs
│   │   │   │   ├── NegativePathEndpointTests.cs
│   │   │   │   ├── OptionsEndpointTests.cs
│   │   │   │   ├── ProviderEndpointTests.cs
│   │   │   │   ├── QualityDropsEndpointTests.cs
│   │   │   │   ├── QualityEndpointContractTests.cs
│   │   │   │   ├── ResponseSchemaSnapshotTests.cs
│   │   │   │   ├── ResponseSchemaValidationTests.cs
│   │   │   │   ├── RoleAuthorizationTests.cs
│   │   │   │   ├── StatusEndpointTests.cs
│   │   │   │   ├── StorageEndpointTests.cs
│   │   │   │   └── SymbolEndpointTests.cs
│   │   │   ├── FixtureProviderTests.cs
│   │   │   ├── GracefulShutdownIntegrationTests.cs
│   │   │   └── YahooFinancePcgPreferredIntegrationTests.cs
│   │   ├── Ledger
│   │   │   └── LedgerIntegrationTests.cs
│   │   ├── Meridian.Tests.csproj
│   │   ├── Performance
│   │   │   └── AllocationBudgetIntegrationTests.cs
│   │   ├── ProviderSdk
│   │   │   ├── CredentialValidatorTests.cs
│   │   │   ├── DataSourceAttributeTests.cs
│   │   │   ├── DataSourceRegistryTests.cs
│   │   │   └── ExceptionTypeTests.cs
│   │   ├── Risk
│   │   │   └── CompositeRiskValidatorTests.cs
│   │   ├── SecurityMaster
│   │   │   ├── SecurityEnrichmentTests.cs
│   │   │   ├── SecurityMasterAssetClassSupportTests.cs
│   │   │   ├── SecurityMasterDatabaseFactAttribute.cs
│   │   │   ├── SecurityMasterDatabaseFixture.cs
│   │   │   ├── SecurityMasterMigrationRunnerTests.cs
│   │   │   ├── SecurityMasterPostgresRoundTripTests.cs
│   │   │   ├── SecurityMasterProjectionServiceSnapshotTests.cs
│   │   │   ├── SecurityMasterRebuildOrchestratorTests.cs
│   │   │   ├── SecurityMasterReferenceLookupTests.cs
│   │   │   ├── SecurityMasterServiceSnapshotTests.cs
│   │   │   └── SecurityMasterSnapshotStoreTests.cs
│   │   ├── Serialization
│   │   │   └── HighPerformanceJsonTests.cs
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
│   │   │   ├── LifecyclePolicyEngineTests.cs
│   │   │   ├── MemoryMappedJsonlReaderTests.cs
│   │   │   ├── MetadataTagServiceTests.cs
│   │   │   ├── ParquetConversionServiceTests.cs
│   │   │   ├── PortableDataPackagerTests.cs
│   │   │   ├── QuotaEnforcementServiceTests.cs
│   │   │   ├── StorageCatalogServiceTests.cs
│   │   │   ├── StorageChecksumServiceTests.cs
│   │   │   ├── StorageOptionsDefaultsTests.cs
│   │   │   ├── StorageSinkRegistryTests.cs
│   │   │   ├── SymbolRegistryServiceTests.cs
│   │   │   ├── WriteAheadLogCorruptionModeTests.cs
│   │   │   ├── WriteAheadLogFuzzTests.cs
│   │   │   └── WriteAheadLogTests.cs
│   │   ├── Strategies
│   │   │   ├── CashFlowProjectionTests.cs
│   │   │   ├── PromotionServiceTests.cs
│   │   │   ├── StrategyLifecycleManagerTests.cs
│   │   │   ├── StrategyRunDrillInTests.cs
│   │   │   └── StrategyRunReadServiceTests.cs
│   │   ├── SymbolSearch
│   │   │   ├── OpenFigiClientTests.cs
│   │   │   └── SymbolSearchServiceTests.cs
│   │   ├── TestCollections.cs
│   │   ├── TestData
│   │   │   └── Golden
│   │   │       └── alpaca-quote-pipeline.json
│   │   ├── TestHelpers
│   │   │   ├── PolygonStubClient.cs
│   │   │   └── TestMarketEventPublisher.cs
│   │   └── Ui
│   │       ├── DirectLendingEndpointsTests.cs
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
│   │       ├── ApiClientServiceTests.cs
│   │       ├── ArchiveBrowserServiceTests.cs
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
│   │       ├── SmartRecommendationsServiceTests.cs
│   │       ├── StatusServiceBaseTests.cs
│   │       ├── StorageAnalyticsServiceTests.cs
│   │       ├── SymbolGroupServiceTests.cs
│   │       ├── SymbolManagementServiceTests.cs
│   │       ├── SymbolMappingServiceTests.cs
│   │       ├── SystemHealthServiceTests.cs
│   │       ├── TimeSeriesAlignmentServiceTests.cs
│   │       ├── WatchlistServiceCollection.cs
│   │       └── WatchlistServiceTests.cs
│   ├── Meridian.Wpf.Tests
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.Wpf.Tests.csproj
│   │   ├── Services
│   │   │   ├── AdminMaintenanceServiceTests.cs
│   │   │   ├── BackgroundTaskSchedulerServiceTests.cs
│   │   │   ├── ConfigServiceTests.cs
│   │   │   ├── ConnectionServiceTests.cs
│   │   │   ├── ExportPresetServiceTests.cs
│   │   │   ├── FirstRunServiceTests.cs
│   │   │   ├── InfoBarServiceTests.cs
│   │   │   ├── KeyboardShortcutServiceTests.cs
│   │   │   ├── MessagingServiceTests.cs
│   │   │   ├── NavigationServiceTests.cs
│   │   │   ├── NotificationServiceTests.cs
│   │   │   ├── OfflineTrackingPersistenceServiceTests.cs
│   │   │   ├── PendingOperationsQueueServiceTests.cs
│   │   │   ├── RetentionAssuranceServiceTests.cs
│   │   │   ├── RunMatServiceTests.cs
│   │   │   ├── StatusServiceTests.cs
│   │   │   ├── StorageServiceTests.cs
│   │   │   ├── StrategyRunWorkspaceServiceTests.cs
│   │   │   ├── TooltipServiceTests.cs
│   │   │   ├── WatchlistServiceTests.cs
│   │   │   └── WorkspaceServiceTests.cs
│   │   ├── Support
│   │   │   ├── RunMatUiAutomationFacade.cs
│   │   │   └── WpfTestThread.cs
│   │   ├── ViewModels
│   │   │   ├── DataQualityViewModelCharacterizationTests.cs
│   │   │   ├── RunMatViewModelTests.cs
│   │   │   └── StrategyRunBrowserViewModelTests.cs
│   │   └── Views
│   │       ├── RunMatUiSmokeTests.cs
│   │       └── RunMatWorkflowSmokeTests.cs
│   ├── coverlet.runsettings
│   ├── scripts
│   │   └── setup-verification.sh
│   ├── setup-script-tests.md
│   └── xunit.runner.json
└── tree.bak

455 directories, 2526 files
```
<!-- readme-tree end -->
