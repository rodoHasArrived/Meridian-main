# Repository Structure

> Auto-generated on 2026-05-19 09:29:22 UTC. Do not edit manually.

```text
Meridian-main
├── .agents
│   └── skills
│       ├── _shared
│       │   └── project-context.md
│       ├── meridian-archive-organizer
│       │   ├── agents
│       │   │   └── openai.yaml
│       │   ├── evals
│       │   │   └── evals.json
│       │   ├── fixtures
│       │   │   └── superseded-adr
│       │   │       └── docs
│       │   │           ├── adr
│       │   │           │   ├── ADR-015-platform-restructuring.md
│       │   │           │   └── README.md
│       │   │           └── generated
│       │   │               └── repository-structure.md
│       │   ├── references
│       │   │   ├── archive-placement-guide.md
│       │   │   └── evaluation-harness.md
│       │   ├── scripts
│       │   │   ├── run_evals.py
│       │   │   ├── score_eval.py
│       │   │   └── trace_archive_candidates.py
│       │   └── SKILL.md
│       ├── meridian-blueprint
│       │   ├── references
│       │   │   ├── blueprint-patterns.md
│       │   │   └── pipeline-position.md
│       │   ├── CHANGELOG.md
│       │   └── SKILL.md
│       ├── meridian-brainstorm
│       │   ├── references
│       │   │   ├── competitive-landscape.md
│       │   │   └── idea-dimensions.md
│       │   ├── brainstorm-history.jsonl
│       │   ├── CHANGELOG.md
│       │   └── SKILL.md
│       ├── meridian-code-review
│       │   ├── agents
│       │   │   └── grader.md
│       │   ├── eval-viewer
│       │   │   ├── generate_review.py
│       │   │   └── viewer.html
│       │   ├── evals
│       │   │   ├── benchmark_baseline.json
│       │   │   └── evals.json
│       │   ├── references
│       │   │   ├── architecture.md
│       │   │   └── schemas.md
│       │   ├── scripts
│       │   │   ├── __init__.py
│       │   │   ├── aggregate_benchmark.py
│       │   │   ├── package_skill.py
│       │   │   ├── quick_validate.py
│       │   │   ├── run_eval.py
│       │   │   └── utils.py
│       │   ├── CHANGELOG.md
│       │   └── SKILL.md
│       ├── meridian-implementation-assurance
│       │   ├── references
│       │   │   ├── documentation-routing.md
│       │   │   └── evaluation-harness.md
│       │   ├── scripts
│       │   │   ├── doc_route.py
│       │   │   └── score_eval.py
│       │   └── SKILL.md
│       ├── meridian-provider-builder
│       │   ├── references
│       │   │   └── provider-patterns.md
│       │   ├── CHANGELOG.md
│       │   └── SKILL.md
│       ├── meridian-repo-navigation
│       │   ├── agents
│       │   │   └── openai.yaml
│       │   └── SKILL.md
│       ├── meridian-roadmap-strategist
│       │   ├── agents
│       │   │   └── openai.yaml
│       │   ├── references
│       │   │   └── roadmap-source-map.md
│       │   └── SKILL.md
│       ├── meridian-simulated-user-panel
│       │   ├── agents
│       │   │   └── grader.md
│       │   ├── assets
│       │   │   ├── bundles
│       │   │   │   ├── roadmap-review.manifest.json
│       │   │   │   ├── screen-review.manifest.json
│       │   │   │   ├── ship-readiness.manifest.json
│       │   │   │   └── workflow-walkthrough.manifest.json
│       │   │   ├── eval-result.schema.json
│       │   │   └── review-manifest.schema.json
│       │   ├── evals
│       │   │   ├── golden
│       │   │   │   ├── eval-01-welcome-onboarding-design-partner.md
│       │   │   │   ├── eval-02-provider-onboarding-release-gate.md
│       │   │   │   ├── eval-03-fund-ledger-controls-review.md
│       │   │   │   ├── eval-04-analysis-export-power-user-review.md
│       │   │   │   ├── eval-05-research-promotion-roadmap-review.md
│       │   │   │   └── eval-06-provider-health-usability-lab.md
│       │   │   ├── benchmark_baseline.json
│       │   │   └── evals.json
│       │   ├── references
│       │   │   ├── artifact-bundles.md
│       │   │   ├── personas.md
│       │   │   ├── review-contract.md
│       │   │   ├── review-modes.md
│       │   │   └── sample-prompts.md
│       │   ├── scripts
│       │   │   ├── __init__.py
│       │   │   └── run_eval.py
│       │   └── SKILL.md
│       └── meridian-test-writer
│           ├── references
│           │   └── test-patterns.md
│           ├── CHANGELOG.md
│           └── SKILL.md
├── .claude
│   ├── agents
│   │   ├── meridian-archive-organizer.md
│   │   ├── meridian-blueprint.md
│   │   ├── meridian-cleanup.md
│   │   ├── meridian-docs.md
│   │   ├── meridian-navigation.md
│   │   ├── meridian-repo-navigation.md
│   │   ├── meridian-roadmap-strategist.md
│   │   └── meridian-user-panel.md
│   ├── plugins
│   │   ├── csharp-dotnet-development
│   │   │   ├── .github
│   │   │   │   └── plugin
│   │   │   │       └── plugin.json
│   │   │   ├── agents
│   │   │   │   └── expert-dotnet-software-engineer.md
│   │   │   ├── skills
│   │   │   │   ├── aspnet-minimal-api-openapi
│   │   │   │   │   └── SKILL.md
│   │   │   │   ├── csharp-async
│   │   │   │   │   └── SKILL.md
│   │   │   │   ├── csharp-mstest
│   │   │   │   │   └── SKILL.md
│   │   │   │   ├── csharp-nunit
│   │   │   │   │   └── SKILL.md
│   │   │   │   ├── csharp-tunit
│   │   │   │   │   └── SKILL.md
│   │   │   │   ├── csharp-xunit
│   │   │   │   │   └── SKILL.md
│   │   │   │   ├── dotnet-best-practices
│   │   │   │   │   └── SKILL.md
│   │   │   │   └── dotnet-upgrade
│   │   │   │       └── SKILL.md
│   │   │   └── README.md
│   │   └── frontend-web-dev
│   │       ├── .github
│   │       │   └── plugin
│   │       │       └── plugin.json
│   │       ├── agents
│   │       │   ├── electron-angular-native.md
│   │       │   └── expert-react-frontend-engineer.md
│   │       ├── skills
│   │       │   ├── playwright-explore-website
│   │       │   │   └── SKILL.md
│   │       │   └── playwright-generate-test
│   │       │       └── SKILL.md
│   │       └── README.md
│   ├── skills
│   │   ├── _shared
│   │   │   └── project-context.md
│   │   ├── meridian-archive-organizer
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   └── evals.json
│   │   │   ├── fixtures
│   │   │   │   └── superseded-adr
│   │   │   │       └── docs
│   │   │   │           ├── adr
│   │   │   │           │   ├── ADR-015-platform-restructuring.md
│   │   │   │           │   └── README.md
│   │   │   │           └── generated
│   │   │   │               └── repository-structure.md
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
│   │   ├── meridian-repo-navigation
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   └── SKILL.md
│   │   ├── meridian-roadmap-strategist
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── references
│   │   │   │   └── roadmap-source-map.md
│   │   │   └── SKILL.md
│   │   ├── meridian-simulated-user-panel
│   │   │   ├── agents
│   │   │   │   └── grader.md
│   │   │   ├── assets
│   │   │   │   ├── bundles
│   │   │   │   │   ├── roadmap-review.manifest.json
│   │   │   │   │   ├── screen-review.manifest.json
│   │   │   │   │   ├── ship-readiness.manifest.json
│   │   │   │   │   └── workflow-walkthrough.manifest.json
│   │   │   │   ├── eval-result.schema.json
│   │   │   │   └── review-manifest.schema.json
│   │   │   ├── evals
│   │   │   │   ├── golden
│   │   │   │   │   ├── eval-01-welcome-onboarding-design-partner.md
│   │   │   │   │   ├── eval-02-provider-onboarding-release-gate.md
│   │   │   │   │   ├── eval-03-fund-ledger-controls-review.md
│   │   │   │   │   ├── eval-04-analysis-export-power-user-review.md
│   │   │   │   │   ├── eval-05-research-promotion-roadmap-review.md
│   │   │   │   │   └── eval-06-provider-health-usability-lab.md
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── references
│   │   │   │   ├── artifact-bundles.md
│   │   │   │   ├── personas.md
│   │   │   │   ├── review-contract.md
│   │   │   │   ├── review-modes.md
│   │   │   │   └── sample-prompts.md
│   │   │   ├── scripts
│   │   │   │   ├── __init__.py
│   │   │   │   └── run_eval.py
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
│   ├── agents
│   │   ├── meridian-archive-organizer.toml
│   │   ├── meridian-blueprint.toml
│   │   ├── meridian-cleanup.toml
│   │   ├── meridian-docs.toml
│   │   ├── meridian-navigation.toml
│   │   ├── meridian-repo-navigation.toml
│   │   ├── meridian-roadmap-strategist.toml
│   │   └── meridian-user-panel.toml
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
│   │   │   ├── fixtures
│   │   │   │   └── superseded-adr
│   │   │   │       └── docs
│   │   │   │           ├── adr
│   │   │   │           │   ├── ADR-015-platform-restructuring.md
│   │   │   │           │   └── README.md
│   │   │   │           └── generated
│   │   │   │               └── repository-structure.md
│   │   │   ├── references
│   │   │   │   ├── archive-placement-guide.md
│   │   │   │   └── evaluation-harness.md
│   │   │   ├── scripts
│   │   │   │   ├── run_evals.py
│   │   │   │   ├── score_eval.py
│   │   │   │   └── trace_archive_candidates.py
│   │   │   └── SKILL.md
│   │   ├── meridian-blueprint
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── references
│   │   │   │   └── blueprint-patterns.md
│   │   │   └── SKILL.md
│   │   ├── meridian-brainstorm
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── references
│   │   │   │   └── competitive-landscape.md
│   │   │   └── SKILL.md
│   │   ├── meridian-cleanup
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── scripts
│   │   │   │   └── repo-updater.ps1
│   │   │   └── SKILL.md
│   │   ├── meridian-code-review
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   └── SKILL.md
│   │   ├── meridian-implementation-assurance
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
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
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── references
│   │   │   │   └── provider-patterns.md
│   │   │   └── SKILL.md
│   │   ├── meridian-repo-navigation
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   └── SKILL.md
│   │   ├── meridian-roadmap-strategist
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── references
│   │   │   │   └── roadmap-source-map.md
│   │   │   └── SKILL.md
│   │   ├── meridian-simulated-user-panel
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── assets
│   │   │   │   ├── bundles
│   │   │   │   │   ├── roadmap-review.manifest.json
│   │   │   │   │   ├── screen-review.manifest.json
│   │   │   │   │   ├── ship-readiness.manifest.json
│   │   │   │   │   └── workflow-walkthrough.manifest.json
│   │   │   │   ├── eval-result.schema.json
│   │   │   │   └── review-manifest.schema.json
│   │   │   ├── references
│   │   │   │   ├── artifact-bundles.md
│   │   │   │   ├── personas.md
│   │   │   │   ├── review-contract.md
│   │   │   │   └── review-modes.md
│   │   │   └── SKILL.md
│   │   ├── meridian-test-writer
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
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
│   │   ├── simulated-user-panel-agent.md
│   │   ├── software-engineer-agent-v1.agent.md
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
│   │   ├── simulate-user-panel-choose-mode.prompt.yml
│   │   ├── simulate-user-panel-design-partner.prompt.yml
│   │   ├── simulate-user-panel-release-gate.prompt.yml
│   │   ├── simulate-user-panel-usability-lab.prompt.yml
│   │   ├── simulate-user-panel.prompt.yml
│   │   ├── troubleshoot-issue.prompt.yml
│   │   ├── workflow-results-code-quality.prompt.yml
│   │   ├── workflow-results-test-matrix.prompt.yml
│   │   ├── wpf-debug-improve.prompt.yml
│   │   ├── wpf-design-system-screen-impact.prompt.yml
│   │   └── write-unit-tests.prompt.yml
│   ├── workflows
│   │   ├── ci.yml
│   │   ├── golden-path-validation.yml
│   │   ├── maintenance.yml
│   │   ├── publish-smoke.yml
│   │   ├── README.md
│   │   └── windows-desktop-build.yml
│   ├── copilot-instructions.md
│   ├── dependabot.yml
│   ├── labeler.yml
│   ├── labels.yml
│   ├── markdown-link-check-config.json
│   ├── PULL_REQUEST_TEMPLATE.md
│   ├── pull_request_template_desktop.md
│   └── spellcheck-config.yml
├── artifacts
│   ├── install-smoke
│   │   └── web-workstation-20260519-smoke
│   │       ├── app
│   │       │   └── wwwroot
│   │       │       └── workstation
│   │       └── data
│   │           ├── backups
│   │           │   └── install-20260519-010253
│   │           │       ├── data
│   │           │       │   └── workstation
│   │           │       │       ├── evidence
│   │           │       │       └── workflows
│   │           │       └── service
│   │           ├── data
│   │           │   └── workstation
│   │           │       ├── evidence
│   │           │       └── workflows
│   │           ├── service
│   │           └── appsettings.json
│   ├── pilot-acceptance
│   │   └── latest
│   │       ├── pilot-readiness-dashboard.json
│   │       ├── pilot-readiness-dashboard.md
│   │       ├── pilot-readiness.json
│   │       └── pilot-readiness.md
│   ├── provider-validation
│   │   └── _automation
│   │       └── 2026-05-18
│   │           ├── alpaca-core-provider-confidence.log
│   │           ├── checkpoint-reliability-and-gap-handling.log
│   │           ├── dk1-pilot-parity-packet.checkpoint.json
│   │           ├── dk1-pilot-parity-packet.json
│   │           ├── dk1-pilot-parity-packet.md
│   │           ├── meridian-tests-build.log
│   │           ├── parquet-sink-and-conversion.log
│   │           ├── robinhood-supported-surface.log
│   │           ├── run-wave1-provider-validation.checkpoint.json
│   │           ├── wave1-validation-summary.json
│   │           ├── wave1-validation-summary.md
│   │           └── yahoo-historical-only-core-provider.log
│   ├── publish
│   │   └── size-baseline-before
│   ├── dashboard-build-after-fix.log
│   ├── dashboard-build-after-ts-fix.log
│   ├── dashboard-vitest-current.log
│   ├── dashboard-vitest-data-ops-vm.log
│   ├── dashboard-vitest-evidence-workbench-vm.log
│   ├── dashboard-vitest-failed-batch-after-fix.log
│   ├── dashboard-vitest-failed-batch-no-file-parallelism.log
│   ├── dashboard-vitest-full-after-fix.log
│   ├── dashboard-vitest-full-after-ts-fix.log
│   ├── dashboard-vitest-governance-screen.log
│   ├── dashboard-vitest-governance-vm-after-fix.log
│   ├── dashboard-vitest-governance-vm.log
│   ├── dashboard-vitest-touched-after-fix.log
│   ├── dotnet-build-meridian-tests-retry.log
│   ├── dotnet-focused-webws-tests-current.log
│   ├── dotnet-hostmode-tests-current.log
│   ├── dotnet-hostmode-tests-retry-current.log
│   ├── dotnet-pilot-harness-retry.log
│   ├── gov-vm-test-calibration-after-fix.log
│   ├── gov-vm-test-calibration.log
│   ├── gov-vm-test-clear-drillin.log
│   ├── gov-vm-test-conflict-retry.log
│   ├── gov-vm-test-reporting-export.log
│   ├── gov-vm-test-stale-identity.log
│   ├── web-workstation-install-smoke.log
│   ├── workstation-operator-inbox-after-recon-vstest.host.26-05-19_01-43-44_97907_5.log
│   ├── workstation-operator-inbox-after-recon-vstest.log
│   ├── workstation-operator-inbox-vstest.host.26-05-19_01-39-23_74764_5.log
│   └── workstation-operator-inbox-vstest.log
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
│   │   ├── StrategyRunReadBenchmarks.cs
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
│       ├── ci
│       │   └── check-workflow-hygiene.py
│       ├── docs
│       │   ├── __pycache__
│       │   │   ├── dashboard_rendering.cpython-311.pyc
│       │   │   └── generate-pilot-readiness-dashboard.cpython-311.pyc
│       │   ├── tests
│       │   │   ├── __pycache__
│       │   │   │   └── test_pilot_readiness_dashboard.cpython-311.pyc
│       │   │   ├── test_check_ai_inventory.py
│       │   │   ├── test_markdown_generation_lint.py
│       │   │   ├── test_pilot_readiness_dashboard.py
│       │   │   └── test_scan_todos.py
│       │   ├── add-todos.py
│       │   ├── ai-docs-maintenance.py
│       │   ├── check-ai-inventory.py
│       │   ├── create-todo-issues.py
│       │   ├── dashboard_rendering.py
│       │   ├── generate-ai-navigation.py
│       │   ├── generate-api-contract-coverage-dashboard.py
│       │   ├── generate-changelog.py
│       │   ├── generate-coverage.py
│       │   ├── generate-dependency-graph.py
│       │   ├── generate-evidence-continuity-dashboard.py
│       │   ├── generate-governance-readiness-dashboard.py
│       │   ├── generate-health-dashboard.py
│       │   ├── generate-metrics-dashboard.py
│       │   ├── generate-paper-replay-reliability-dashboard.py
│       │   ├── generate-pilot-readiness-dashboard.py
│       │   ├── generate-prompts.py
│       │   ├── generate-structure-docs.py
│       │   ├── generate-workflow-manifest.py
│       │   ├── lint-command-snippets.py
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
│       │   ├── install-web-workstation.ps1
│       │   ├── install.ps1
│       │   ├── install.sh
│       │   └── smoke-web-workstation-install.ps1
│       ├── lib
│       │   ├── ArtifactRetention.psm1
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
│       │   ├── test_generate_ai_navigation.py
│       │   └── test_validate_budget.py
│       ├── ai-architecture-check.py
│       ├── ai-repo-updater.py
│       ├── validate-tooling-metadata.py
│       └── validate_budget.py
├── config
│   ├── appsettings.json
│   ├── appsettings.sample.json
│   ├── appsettings.schema.json
│   ├── condition-codes.json
│   ├── score-reason-registry.json
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
│   │   ├── 016-custody-cash-reconciliation-break-typing.md
│   │   ├── 016-platform-architecture-migration.md
│   │   ├── _template.md
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
│   │   │   ├── CLAUDE.roadmap-learning-log.md
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
│   │   ├── assistant-workflow-contract.md
│   │   └── README.md
│   ├── architecture
│   │   ├── c4-diagrams.md
│   │   ├── crystallized-storage-format.md
│   │   ├── desktop-layers.md
│   │   ├── deterministic-canonicalization.md
│   │   ├── domains.md
│   │   ├── environment-designer-runtime-projection-and-wpf-admin-surface.md
│   │   ├── evidence-workflow-fabric.md
│   │   ├── layer-boundaries.md
│   │   ├── ledger-architecture.md
│   │   ├── module-map.md
│   │   ├── mvvm-guidelines.md
│   │   ├── overview.md
│   │   ├── project-structure.md
│   │   ├── provider-management.md
│   │   ├── README.md
│   │   ├── storage-design.md
│   │   ├── strategy-builder-integration.md
│   │   ├── why-this-architecture.md
│   │   ├── workflow-library.md
│   │   ├── wpf-shell-mvvm.md
│   │   └── wpf-workstation-shell-ux.md
│   ├── audits
│   │   ├── audit-architecture-results.txt
│   │   ├── audit-code-results.json
│   │   ├── audit-results-full.json
│   │   ├── AUDIT_REPORT.md
│   │   ├── BACKTEST_ENGINE_CODE_REVIEW_2026_03_25.md
│   │   ├── CODE_REVIEW_2026-03-16.md
│   │   ├── FURTHER_SIMPLIFICATION_OPPORTUNITIES.md
│   │   ├── prompt-generation-results.json
│   │   ├── README.md
│   │   └── workspace-visual-audit-checklist-2026-04-22.md
│   ├── design
│   │   ├── design-system-usage.md
│   │   └── README.md
│   ├── developer
│   │   ├── build-test-run.md
│   │   ├── publish-standalone-exe.md
│   │   ├── README.md
│   │   └── setup.md
│   ├── development
│   │   ├── policies
│   │   │   ├── desktop-support-policy.md
│   │   │   └── promotion-policy-matrix.md
│   │   ├── adding-custom-rules.md
│   │   ├── build-observability.md
│   │   ├── central-package-management.md
│   │   ├── desktop-command-surface-migration.md
│   │   ├── desktop-testing-guide.md
│   │   ├── desktop-workflow-automation.md
│   │   ├── documentation-automation.md
│   │   ├── documentation-contribution-guide.md
│   │   ├── expanding-scripts.md
│   │   ├── fsharp-decision-rule.md
│   │   ├── fund-account-traversal.md
│   │   ├── git-hooks.md
│   │   ├── github-actions-summary.md
│   │   ├── github-actions-testing.md
│   │   ├── otlp-trace-visualization.md
│   │   ├── process-lifecycle-diagnostics.md
│   │   ├── provider-implementation.md
│   │   ├── README.md
│   │   ├── refactor-map.md
│   │   ├── repository-organization-guide.md
│   │   ├── repository-rule-set.md
│   │   ├── rule-evaluation-contracts.md
│   │   ├── score-reason-taxonomy.md
│   │   ├── tooling-workflow-backlog.md
│   │   ├── ui-fixture-mode-guide.md
│   │   └── wpf-implementation-notes.md
│   ├── diagrams
│   │   ├── analytics
│   │   │   ├── backtesting-engine.dot
│   │   │   ├── backtesting-engine.png
│   │   │   ├── backtesting-engine.svg
│   │   │   └── README.md
│   │   ├── architecture
│   │   │   ├── c4
│   │   │   │   ├── c4-level1-context.dot
│   │   │   │   ├── c4-level1-context.png
│   │   │   │   ├── c4-level1-context.svg
│   │   │   │   ├── c4-level2-containers.dot
│   │   │   │   ├── c4-level2-containers.png
│   │   │   │   ├── c4-level2-containers.svg
│   │   │   │   ├── c4-level3-components.dot
│   │   │   │   ├── c4-level3-components.png
│   │   │   │   └── c4-level3-components.svg
│   │   │   ├── platform
│   │   │   │   ├── domain-event-model.dot
│   │   │   │   ├── domain-event-model.png
│   │   │   │   ├── domain-event-model.svg
│   │   │   │   ├── fsharp-domain.dot
│   │   │   │   ├── fsharp-domain.png
│   │   │   │   ├── fsharp-domain.svg
│   │   │   │   ├── mcp-server.dot
│   │   │   │   ├── mcp-server.png
│   │   │   │   ├── mcp-server.svg
│   │   │   │   ├── project-dependencies.dot
│   │   │   │   ├── project-dependencies.png
│   │   │   │   ├── project-dependencies.svg
│   │   │   │   ├── provider-architecture.dot
│   │   │   │   ├── provider-architecture.png
│   │   │   │   ├── provider-architecture.svg
│   │   │   │   ├── runtime-hosts.dot
│   │   │   │   ├── runtime-hosts.png
│   │   │   │   ├── runtime-hosts.svg
│   │   │   │   ├── storage-architecture.dot
│   │   │   │   ├── storage-architecture.png
│   │   │   │   ├── storage-architecture.svg
│   │   │   │   ├── workstation-delivery.dot
│   │   │   │   ├── workstation-delivery.png
│   │   │   │   └── workstation-delivery.svg
│   │   │   └── README.md
│   │   ├── operations
│   │   │   ├── data-quality-monitoring.dot
│   │   │   ├── data-quality-monitoring.png
│   │   │   ├── data-quality-monitoring.svg
│   │   │   ├── deployment-options.dot
│   │   │   ├── deployment-options.png
│   │   │   ├── deployment-options.svg
│   │   │   ├── README.md
│   │   │   ├── resilience-patterns.dot
│   │   │   ├── resilience-patterns.png
│   │   │   └── resilience-patterns.svg
│   │   ├── reference
│   │   │   ├── cli-commands.dot
│   │   │   ├── cli-commands.png
│   │   │   ├── cli-commands.svg
│   │   │   ├── configuration-management.dot
│   │   │   ├── configuration-management.png
│   │   │   ├── configuration-management.svg
│   │   │   ├── README.md
│   │   │   ├── symbol-search-resolution.dot
│   │   │   ├── symbol-search-resolution.png
│   │   │   └── symbol-search-resolution.svg
│   │   ├── ui
│   │   │   ├── README.md
│   │   │   ├── ui-implementation-flow.dot
│   │   │   ├── ui-implementation-flow.png
│   │   │   ├── ui-implementation-flow.svg
│   │   │   ├── ui-navigation-map.dot
│   │   │   ├── ui-navigation-map.png
│   │   │   └── ui-navigation-map.svg
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
│   │   ├── workflows
│   │   │   ├── operations
│   │   │   │   ├── backfill-workflow.dot
│   │   │   │   ├── backfill-workflow.png
│   │   │   │   ├── backfill-workflow.svg
│   │   │   │   ├── data-flow.dot
│   │   │   │   ├── data-flow.png
│   │   │   │   ├── data-flow.svg
│   │   │   │   ├── event-pipeline-sequence.dot
│   │   │   │   ├── event-pipeline-sequence.png
│   │   │   │   ├── event-pipeline-sequence.svg
│   │   │   │   ├── execution-layer.dot
│   │   │   │   ├── execution-layer.png
│   │   │   │   ├── execution-layer.svg
│   │   │   │   ├── fund-ops-reconciliation.dot
│   │   │   │   ├── fund-ops-reconciliation.png
│   │   │   │   ├── fund-ops-reconciliation.svg
│   │   │   │   ├── onboarding-flow.dot
│   │   │   │   ├── onboarding-flow.png
│   │   │   │   ├── onboarding-flow.svg
│   │   │   │   ├── security-master-lifecycle.dot
│   │   │   │   ├── security-master-lifecycle.png
│   │   │   │   ├── security-master-lifecycle.svg
│   │   │   │   ├── strategy-lifecycle.dot
│   │   │   │   ├── strategy-lifecycle.png
│   │   │   │   └── strategy-lifecycle.svg
│   │   │   └── README.md
│   │   ├── README.md
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
│   │   ├── desktop-platform-improvements-implementation-guide.md
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
│   │   ├── workflow-command-reference.md
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
│   │   ├── cleanup-and-maintenance.md
│   │   ├── deployment.md
│   │   ├── disk-space-hygiene.md
│   │   ├── governance-operator-workflow.md
│   │   ├── high-availability.md
│   │   ├── live-execution-controls.md
│   │   ├── msix-packaging.md
│   │   ├── operator-runbook.md
│   │   ├── performance-tuning.md
│   │   ├── portable-data-packager.md
│   │   ├── preflight-checklist.md
│   │   ├── provider-credential-management.md
│   │   ├── provider-degradation-calibration.md
│   │   ├── README.md
│   │   ├── reconciliation-operations.md
│   │   ├── reconciliation-policy-operations.md
│   │   ├── reconciliation-runbook.md
│   │   ├── service-level-objectives.md
│   │   └── web-workstation-installer.md
│   ├── plans
│   │   ├── assembly-performance-roadmap.md
│   │   ├── backtest-studio-unification-blueprint.md
│   │   ├── backtest-studio-unification-pr-sequenced-roadmap.md
│   │   ├── brokerage-portfolio-sync-blueprint.md
│   │   ├── codebase-audit-cleanup-roadmap.md
│   │   ├── covered-call-writing-slice-1-blueprint.md
│   │   ├── current-direction-and-status.md
│   │   ├── evidence-backed-investment-operations-plan.md
│   │   ├── fund-management-module-implementation-backlog.md
│   │   ├── fund-management-pr-sequenced-roadmap.md
│   │   ├── fund-management-product-vision-and-capability-matrix.md
│   │   ├── governance-fund-ops-blueprint.md
│   │   ├── kernel-parity-migration-blueprint.md
│   │   ├── l3-inference-implementation-plan.md
│   │   ├── ledger.md
│   │   ├── meridian-6-week-roadmap.md
│   │   ├── meridian-database-blueprint.md
│   │   ├── meridian-pilot-workflow.md
│   │   ├── options-roadmap.md
│   │   ├── paper-trading-cockpit-reliability-sprint.md
│   │   ├── portfolio-level-backtesting-composer-blueprint.md
│   │   ├── quantscript-l3-multiinstance-round2-roadmap.md
│   │   ├── README.md
│   │   ├── research-backtest-trust-and-velocity-blueprint.md
│   │   ├── runbook-template-registry-modernization-plan.md
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
│   │   ├── waves-2-4-operator-readiness-addendum.md
│   │   └── web-ui-development-pivot.md
│   ├── prompts
│   │   ├── automation-prompts.md
│   │   ├── README.md
│   │   └── repo-maintenance-prompts.md
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
│   │   ├── backtest-preflight-and-stage-telemetry.md
│   │   ├── brand-assets.md
│   │   ├── data-dictionary.md
│   │   ├── data-uniformity.md
│   │   ├── design-review-memo.md
│   │   ├── edgar-reference-data.md
│   │   ├── environment-variables.md
│   │   ├── export-preflight-rules.md
│   │   ├── governance-report-packs.md
│   │   ├── ledger-journal-store.md
│   │   ├── open-source-references.md
│   │   ├── README.md
│   │   ├── reconciliation-break-taxonomy.md
│   │   ├── research-briefing-workflow.md
│   │   └── strategy-promotion-history.md
│   ├── screenshots
│   │   ├── desktop
│   │   │   └── manuals
│   │   │       ├── manual-data-operations
│   │   │       │   ├── 01-data-operations-shell.png
│   │   │       │   ├── 02-providers.png
│   │   │       │   ├── 03-provider-health.png
│   │   │       │   ├── 04-backfill.png
│   │   │       │   ├── 05-data-sources.png
│   │   │       │   ├── 06-storage.png
│   │   │       │   └── 07-data-quality.png
│   │   │       ├── manual-governance
│   │   │       │   ├── 01-governance-shell.png
│   │   │       │   ├── 02-fund-ledger.png
│   │   │       │   ├── 03-fund-reconciliation.png
│   │   │       │   ├── 04-fund-report-pack.png
│   │   │       │   ├── 05-security-master.png
│   │   │       │   └── 06-settings.png
│   │   │       ├── manual-overview
│   │   │       │   ├── 01-research-workspace.png
│   │   │       │   ├── 02-workspace-layouts.png
│   │   │       │   ├── 03-research-workspace.png
│   │   │       │   ├── 04-trading-workspace.png
│   │   │       │   ├── 05-data-operations-workspace.png
│   │   │       │   ├── 06-governance-workspace.png
│   │   │       │   └── 07-help.png
│   │   │       └── manual-research-and-trading
│   │   │           ├── 01-research-shell.png
│   │   │           ├── 02-backtest.png
│   │   │           ├── 03-strategy-runs.png
│   │   │           ├── 04-quant-script.png
│   │   │           ├── 05-trading-shell.png
│   │   │           ├── 06-position-blotter.png
│   │   │           └── 07-run-risk.png
│   │   └── README.md
│   ├── security
│   │   ├── known-vulnerabilities.md
│   │   └── README.md
│   ├── status
│   │   ├── api-docs-report.md
│   │   ├── badge-sync-report.md
│   │   ├── CHANGELOG.md
│   │   ├── contract-compatibility-matrix.md
│   │   ├── coverage-report.md
│   │   ├── dk1-baseline-trust-thresholds.md
│   │   ├── dk1-pilot-parity-runbook.md
│   │   ├── dk1-trust-rationale-mapping.md
│   │   ├── docs-automation-summary.json
│   │   ├── docs-automation-summary.md
│   │   ├── EVALUATIONS_AND_AUDITS.md
│   │   ├── example-validation.md
│   │   ├── FEATURE_INVENTORY.md
│   │   ├── FULL_IMPLEMENTATION_TODO_2026_03_20.md
│   │   ├── health-dashboard.md
│   │   ├── IMPROVEMENTS.md
│   │   ├── kernel-readiness-dashboard.md
│   │   ├── KERNEL_PARITY_STATUS.md
│   │   ├── link-repair-report.md
│   │   ├── metrics-dashboard.md
│   │   ├── OPPORTUNITY_SCAN.md
│   │   ├── production-status.md
│   │   ├── program-state-summary.json
│   │   ├── program-state-summary.md
│   │   ├── PROGRAM_STATE.md
│   │   ├── provider-validation-matrix.md
│   │   ├── readiness-claim-language-policy.md
│   │   ├── README.md
│   │   ├── ROADMAP.md
│   │   ├── ROADMAP_COMBINED.md
│   │   ├── rules-report.md
│   │   ├── run-contract.schema.json
│   │   ├── TARGET_END_PRODUCT.md
│   │   ├── TODO.md
│   │   ├── wave4-evidence-template.md
│   │   ├── workflow-drift-report.md
│   │   ├── workflow-manifest.json
│   │   └── workflow-validation-summary.json
│   ├── testing
│   │   ├── README.md
│   │   ├── wave2-cockpit-reliability-evidence-runbook.md
│   │   ├── WAVE2_ACCEPTANCE_GATE_CHECKLIST.md
│   │   └── WAVE2_ACCEPTANCE_TESTS.md
│   ├── ui
│   │   ├── components.md
│   │   └── README.md
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
├── Meridian Design System
│   ├── assets
│   │   ├── brand
│   │   │   ├── meridian-hero.svg
│   │   │   ├── meridian-mark-light.svg
│   │   │   ├── meridian-mark-monochrome.svg
│   │   │   ├── meridian-mark.svg
│   │   │   ├── meridian-symbol.svg
│   │   │   ├── meridian-tile-256.png
│   │   │   ├── meridian-tile.svg
│   │   │   ├── meridian-wordmark-stacked.svg
│   │   │   ├── meridian-wordmark.svg
│   │   │   └── README.md
│   │   ├── icons
│   │   │   ├── account-portfolio.svg
│   │   │   ├── admin-maintenance.svg
│   │   │   ├── aggregate-portfolio.svg
│   │   │   ├── archive-health.svg
│   │   │   ├── backfill.svg
│   │   │   ├── backtest.svg
│   │   │   ├── charting.svg
│   │   │   ├── collection-sessions.svg
│   │   │   ├── dashboard.svg
│   │   │   ├── data-browser.svg
│   │   │   ├── data-calendar.svg
│   │   │   ├── data-export.svg
│   │   │   ├── data-operations.svg
│   │   │   ├── data-quality.svg
│   │   │   ├── data-sampling.svg
│   │   │   ├── data-sources.svg
│   │   │   ├── diagnostics.svg
│   │   │   ├── event-replay.svg
│   │   │   ├── governance.svg
│   │   │   ├── help.svg
│   │   │   ├── index-subscription.svg
│   │   │   ├── keyboard-shortcuts.svg
│   │   │   ├── lean-integration.svg
│   │   │   ├── live-data.svg
│   │   │   ├── order-book.svg
│   │   │   ├── portfolio-import.svg
│   │   │   ├── provider-health.svg
│   │   │   ├── README.md
│   │   │   ├── research.svg
│   │   │   ├── retention-assurance.svg
│   │   │   ├── run-detail.svg
│   │   │   ├── run-ledger.svg
│   │   │   ├── run-mat.svg
│   │   │   ├── run-portfolio.svg
│   │   │   ├── schedule-manager.svg
│   │   │   ├── security-master.svg
│   │   │   ├── service-manager.svg
│   │   │   ├── settings.svg
│   │   │   ├── storage-optimization.svg
│   │   │   ├── storage.svg
│   │   │   ├── strategy-runs.svg
│   │   │   ├── symbol-storage.svg
│   │   │   ├── symbols.svg
│   │   │   ├── system-health.svg
│   │   │   ├── trading-hours.svg
│   │   │   ├── trading.svg
│   │   │   └── watchlist.svg
│   │   └── app.ico
│   ├── preview
│   │   ├── brand-icons.html
│   │   ├── brand-marks.html
│   │   ├── chart-table-standards.html
│   │   ├── charts-candlestick.html
│   │   ├── charts-correlation.html
│   │   ├── charts-equity-print.html
│   │   ├── charts-equity.html
│   │   ├── charts-heatmap.html
│   │   ├── charts-histogram.html
│   │   ├── charts-orderbook.html
│   │   ├── charts-scatter.html
│   │   ├── charts-sparklines.html
│   │   ├── charts-volsurface.html
│   │   ├── charts-yieldcurve.html
│   │   ├── colors-ambient.html
│   │   ├── colors-brand.html
│   │   ├── colors-semantic.html
│   │   ├── colors-surfaces.html
│   │   ├── component-state-matrix.html
│   │   ├── components-badges.html
│   │   ├── components-banners.html
│   │   ├── components-buttons.html
│   │   ├── components-inputs.html
│   │   ├── components-metrics.html
│   │   ├── components-nav.html
│   │   ├── components-table.html
│   │   ├── design-standards.html
│   │   ├── index.html
│   │   ├── institutional-workstation.html
│   │   ├── preview-common.css
│   │   ├── reference-workbench.html
│   │   ├── screen-recipes.html
│   │   ├── spacing-radii.html
│   │   ├── spacing-scale.html
│   │   ├── spacing-shadows.html
│   │   ├── state-patterns.html
│   │   ├── type-body.html
│   │   ├── type-display.html
│   │   └── type-mono.html
│   ├── scripts
│   │   └── check_design_system_governance.py
│   ├── tests
│   │   └── test_design_system_governance.py
│   ├── ui_kits
│   │   ├── dashboard
│   │   │   ├── components.jsx
│   │   │   └── README.md
│   │   ├── plottool_workstation.html
│   │   ├── security_master-company.html
│   │   ├── security_master-print.html
│   │   └── security_master.html
│   ├── uploads
│   │   └── ChatGPT Image Apr 24, 2026, 03_58_29 PM.png
│   ├── BRAND_GUIDELINES.md
│   ├── colors_and_type.css
│   ├── CONTENT_FUNDAMENTALS.md
│   ├── governance-baseline.json
│   ├── ICONOGRAPHY.md
│   ├── index.html
│   ├── INSPIRATION_BRIEF.md
│   ├── README.md
│   ├── SKILL.md
│   └── VISUAL_FOUNDATIONS.md
├── native
│   └── cpptrader-host
│       ├── src
│       │   └── main.cpp
│       ├── CMakeLists.txt
│       └── README.md
├── plugins
│   ├── frontend-web-dev
│   │   ├── .github
│   │   │   └── plugin
│   │   │       └── plugin.json
│   │   ├── agents
│   │   │   ├── electron-angular-native.md
│   │   │   └── expert-react-frontend-engineer.md
│   │   ├── skills
│   │   │   ├── playwright-explore-website
│   │   │   │   └── SKILL.md
│   │   │   └── playwright-generate-test
│   │   │       └── SKILL.md
│   │   └── README.md
│   ├── security-best-practices
│   │   ├── .github
│   │   │   └── plugin
│   │   │       └── plugin.json
│   │   ├── skills
│   │   │   └── ai-prompt-engineering-safety-review
│   │   │       └── SKILL.md
│   │   └── README.md
│   ├── testing-automation
│   │   ├── .github
│   │   │   └── plugin
│   │   │       └── plugin.json
│   │   ├── agents
│   │   │   ├── playwright-tester.md
│   │   │   ├── tdd-green.md
│   │   │   ├── tdd-red.md
│   │   │   └── tdd-refactor.md
│   │   ├── skills
│   │   │   ├── ai-prompt-engineering-safety-review
│   │   │   │   └── SKILL.md
│   │   │   ├── csharp-nunit
│   │   │   │   └── SKILL.md
│   │   │   ├── java-junit
│   │   │   │   └── SKILL.md
│   │   │   ├── playwright-explore-website
│   │   │   │   └── SKILL.md
│   │   │   └── playwright-generate-test
│   │   │       └── SKILL.md
│   │   └── README.md
│   └── README.md
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
│   │   ├── fixtures
│   │   │   └── robinhood-options-smoke.seed.json
│   │   ├── shared
│   │   │   └── retry.ps1
│   │   ├── workflow-profiles
│   │   │   ├── debug-startup.json
│   │   │   ├── manual-data-operations.json
│   │   │   ├── manual-governance.json
│   │   │   ├── manual-overview.json
│   │   │   ├── manual-research-and-trading.json
│   │   │   └── screenshot-catalog.json
│   │   ├── build-ibapi-smoke.ps1
│   │   ├── capture-desktop-screenshots.ps1
│   │   ├── capture-web-screenshots.mjs
│   │   ├── check-meridian-process-lifecycle.ps1
│   │   ├── cleanup-generated.ps1
│   │   ├── desktop-dev.ps1
│   │   ├── desktop-workflows.json
│   │   ├── diagnose-uwp-xaml.ps1
│   │   ├── generate-desktop-user-manual.ps1
│   │   ├── generate-dk1-pilot-parity-packet.ps1
│   │   ├── install-git-hooks.sh
│   │   ├── preflight_runner.py
│   │   ├── prepare-dk1-operator-signoff.ps1
│   │   ├── robinhood-options-smoke.ps1
│   │   ├── run-desktop-workflow.ps1
│   │   ├── run-desktop.ps1
│   │   ├── run-wave1-provider-validation.ps1
│   │   ├── screenshot-diff-config.json
│   │   ├── screenshot_diff_report.py
│   │   ├── screenshot_workflow_plan.py
│   │   ├── SharedBuild.ps1
│   │   ├── SharedCheckpoint.ps1
│   │   ├── SharedPreflight.ps1
│   │   ├── SharedWorkflowProfiles.ps1
│   │   ├── summarize-desktop-workflow-bundle.ps1
│   │   ├── validate-operator-inbox-route.ps1
│   │   ├── validate-position-blotter-route.ps1
│   │   ├── validate-screenshot-contract.py
│   │   ├── validate-workflow-profile.ps1
│   │   ├── web-screenshot-fixtures.json
│   │   └── web-screenshot-routes.json
│   ├── lib
│   │   ├── ui-diagram-generator.mjs
│   │   └── ui-diagram-generator.test.mjs
│   ├── check_contract_compatibility_gate.py
│   ├── check_program_state_consistency.py
│   ├── check_status_delivery_claims.py
│   ├── check_workflow_docs_parity.py
│   ├── compare_benchmarks.py
│   ├── compare_run_contract.py
│   ├── example-sharpe.csx
│   ├── generate-diagrams.mjs
│   ├── generate_contract_review_packet.py
│   ├── generate_program_state_summary.py
│   ├── report_canonicalization_drift.py
│   └── wpf_finance_ux_checks.py
├── src
│   ├── Meridian
│   │   ├── artifacts
│   │   ├── Integrations
│   │   │   └── Lean
│   │   │       ├── MeridianDataProvider.cs
│   │   │       ├── MeridianQuoteData.cs
│   │   │       ├── MeridianTradeData.cs
│   │   │       ├── README.md
│   │   │       └── SampleLeanAlgorithm.cs
│   │   ├── Tools
│   │   │   └── DataValidator.cs
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
│   │   ├── Accounts
│   │   │   ├── IAccountManagementService.cs
│   │   │   └── IAccountQueryService.cs
│   │   ├── artifacts
│   │   ├── Backfill
│   │   │   ├── AutoGapRemediationService.cs
│   │   │   ├── BackfillCoordinatorExecutionGateway.cs
│   │   │   ├── BackfillCostEstimator.cs
│   │   │   ├── BackfillRequest.cs
│   │   │   ├── BackfillResult.cs
│   │   │   ├── BackfillStatusStore.cs
│   │   │   ├── BackfillStatusStoreJsonContext.cs
│   │   │   ├── GapBackfillService.cs
│   │   │   ├── HistoricalBackfillService.cs
│   │   │   ├── IBackfillExecutionGateway.cs
│   │   │   └── SymbolValidationSignal.cs
│   │   ├── Backtesting
│   │   │   ├── BacktestPreflightService.cs
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
│   │   ├── CertificatesOfDeposit
│   │   │   ├── CertificateOfDepositProjectionService.cs
│   │   │   └── ICertificateOfDepositReferenceService.cs
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
│   │   │   ├── LedgerCliCommand.cs
│   │   │   ├── PackageCommands.cs
│   │   │   ├── ProviderCalibrationCommand.cs
│   │   │   ├── QueryCommand.cs
│   │   │   ├── RunbookCommands.cs
│   │   │   ├── SchemaCheckCommand.cs
│   │   │   ├── SecurityMasterCommands.cs
│   │   │   ├── SelfTestCommand.cs
│   │   │   ├── StatementCommands.cs
│   │   │   ├── StatementImportCommands.cs
│   │   │   ├── SymbolCommands.cs
│   │   │   ├── ValidateConfigCommand.cs
│   │   │   └── WalRepairCommand.cs
│   │   ├── Commodities
│   │   │   ├── CommodityProjectionService.cs
│   │   │   └── ICommodityReferenceService.cs
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
│   │   │   │   │   └── WorkstationModeRunner.cs
│   │   │   │   ├── StartupModels
│   │   │   │   │   ├── HostMode.cs
│   │   │   │   │   ├── StartupContext.cs
│   │   │   │   │   ├── StartupPlan.cs
│   │   │   │   │   ├── StartupRequest.cs
│   │   │   │   │   └── StartupValidationResult.cs
│   │   │   │   ├── ApplicationLifecycleCoordinator.cs
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
│   │   │   │   ├── FileProviderCredentialStore.cs
│   │   │   │   ├── IProviderCredentialStore.cs
│   │   │   │   ├── OAuthToken.cs
│   │   │   │   ├── OAuthTokenRefreshService.cs
│   │   │   │   ├── ProviderCredentialCatalog.cs
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
│   │   ├── CryptoCurrency
│   │   │   ├── CryptoProjectionService.cs
│   │   │   └── ICryptoReferenceService.cs
│   │   ├── Deposits
│   │   │   ├── DepositProjectionService.cs
│   │   │   └── IDepositReferenceService.cs
│   │   ├── Derivatives
│   │   │   ├── ISwapReferenceService.cs
│   │   │   └── SwapProjectionService.cs
│   │   ├── DirectLending
│   │   │   ├── AccrualLedgerService.cs
│   │   │   ├── DailyAccrualWorker.cs
│   │   │   ├── DirectLendingEventRebuilder.cs
│   │   │   ├── DirectLendingOutboxDispatcher.cs
│   │   │   ├── DirectLendingServiceSupport.cs
│   │   │   ├── DirectLendingWorkflowSupport.cs
│   │   │   ├── DirectLendingWorkflowTopics.cs
│   │   │   ├── IAccrualLedgerService.cs
│   │   │   ├── IDirectLendingCommandService.cs
│   │   │   ├── IDirectLendingQueryService.cs
│   │   │   ├── IDirectLendingService.cs
│   │   │   ├── InMemoryDirectLendingService.cs
│   │   │   ├── InMemoryDirectLendingService.Workflows.cs
│   │   │   ├── LoanAccountingProjector.cs
│   │   │   ├── PostgresDirectLendingCommandService.cs
│   │   │   ├── PostgresDirectLendingQueryService.cs
│   │   │   └── PostgresDirectLendingService.cs
│   │   ├── EnvironmentDesign
│   │   │   ├── EnvironmentDesignerService.cs
│   │   │   ├── IEnvironmentDesignService.cs
│   │   │   ├── IEnvironmentPublishService.cs
│   │   │   ├── IEnvironmentRuntimeProjectionService.cs
│   │   │   └── IEnvironmentValidationService.cs
│   │   ├── Equity
│   │   │   ├── EquityProjectionService.cs
│   │   │   └── IEquityReferenceService.cs
│   │   ├── Etl
│   │   │   ├── EtlAbstractions.cs
│   │   │   └── EtlServices.cs
│   │   ├── Filters
│   │   │   └── MarketEventFilter.cs
│   │   ├── FixedIncome
│   │   │   ├── BondProjectionService.cs
│   │   │   └── IBondReferenceService.cs
│   │   ├── FundAccounts
│   │   │   ├── IFundAccountService.cs
│   │   │   └── InMemoryFundAccountService.cs
│   │   ├── FundStructure
│   │   │   ├── FundAccountTraversalQueryService.cs
│   │   │   ├── GovernanceSharedDataAccessService.cs
│   │   │   ├── IFundAccountTraversalQueryService.cs
│   │   │   ├── IFundStructureService.cs
│   │   │   ├── IGovernanceSharedDataAccessService.cs
│   │   │   ├── InMemoryFundStructureService.cs
│   │   │   └── LedgerGroupingRules.cs
│   │   ├── Futures
│   │   │   ├── FutureProjectionService.cs
│   │   │   └── IFutureReferenceService.cs
│   │   ├── FxSpot
│   │   │   ├── FxSpotProjectionService.cs
│   │   │   └── IFxSpotReferenceService.cs
│   │   ├── Http
│   │   │   ├── Endpoints
│   │   │   │   ├── ArchiveMaintenanceEndpoints.cs
│   │   │   │   ├── DataQualityEndpoints.cs
│   │   │   │   ├── PackagingEndpoints.cs
│   │   │   │   └── StatusEndpointHandlers.cs
│   │   │   ├── BackfillCoordinator.cs
│   │   │   └── ConfigStore.cs
│   │   ├── Indicators
│   │   │   └── TechnicalIndicatorService.cs
│   │   ├── Ledger
│   │   │   ├── TextJournal
│   │   │   │   ├── LedgerTextJournalDocument.cs
│   │   │   │   ├── LedgerTextJournalException.cs
│   │   │   │   ├── LedgerTextJournalParser.cs
│   │   │   │   ├── LedgerTextJournalReportService.cs
│   │   │   │   ├── LedgerTextReportOptions.cs
│   │   │   │   ├── LedgerTextReportRenderer.cs
│   │   │   │   └── LedgerTextTransaction.cs
│   │   │   └── AccountingPolicyService.cs
│   │   ├── MoneyMarketFunds
│   │   │   ├── IMoneyMarketFundReferenceService.cs
│   │   │   └── MoneyMarketFundProjectionService.cs
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
│   │   │   ├── ProviderDegradationCalibration.cs
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
│   │   ├── Options
│   │   │   ├── IOptionChainImportService.cs
│   │   │   ├── IOptionReferenceService.cs
│   │   │   └── OptionProjectionService.cs
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
│   │   │   ├── BestOfBreedProviderSelector.cs
│   │   │   ├── KernelObservabilityService.cs
│   │   │   ├── ProviderBindingService.cs
│   │   │   ├── ProviderConnectionService.cs
│   │   │   ├── ProviderOperationsSupportServices.cs
│   │   │   ├── ProviderRoutingEngine.cs
│   │   │   └── ProviderRoutingMapper.cs
│   │   ├── Reconciliation
│   │   │   └── StatementReconciliationService.cs
│   │   ├── Results
│   │   │   ├── ErrorCode.cs
│   │   │   ├── OperationError.cs
│   │   │   └── Result.cs
│   │   ├── Runbooks
│   │   │   ├── RunbookExecutor.cs
│   │   │   ├── RunbookModels.cs
│   │   │   └── RunbookStore.cs
│   │   ├── Scheduling
│   │   │   ├── BackfillExecutionLog.cs
│   │   │   ├── BackfillSchedule.cs
│   │   │   ├── BackfillScheduleManager.cs
│   │   │   ├── IOperationalScheduler.cs
│   │   │   ├── OperationalScheduler.cs
│   │   │   └── ScheduledBackfillService.cs
│   │   ├── SecurityMaster
│   │   │   ├── EdgarIngestOrchestrator.cs
│   │   │   ├── IEdgarIngestOrchestrator.cs
│   │   │   ├── ILivePositionCorporateActionAdjuster.cs
│   │   │   ├── ISecurityMasterQueryService.cs
│   │   │   ├── ISecurityMasterService.cs
│   │   │   ├── ISecurityMasterWorkbenchQueryService.cs
│   │   │   ├── ISecurityResolver.cs
│   │   │   ├── IUflProjectionRebuilder.cs
│   │   │   ├── NullSecurityMasterServices.cs
│   │   │   ├── SecurityEconomicDefinitionAdapter.cs
│   │   │   ├── SecurityKindMapping.cs
│   │   │   ├── SecurityMasterAggregateRebuilder.cs
│   │   │   ├── SecurityMasterCanonicalSymbolSeedService.cs
│   │   │   ├── SecurityMasterConflictService.cs
│   │   │   ├── SecurityMasterCsvParser.cs
│   │   │   ├── SecurityMasterImportService.cs
│   │   │   ├── SecurityMasterIngestStatusService.cs
│   │   │   ├── SecurityMasterLedgerBridge.cs
│   │   │   ├── SecurityMasterMapping.cs
│   │   │   ├── SecurityMasterOptionsValidator.cs
│   │   │   ├── SecurityMasterProjectionService.cs
│   │   │   ├── SecurityMasterProjectionWarmupService.cs
│   │   │   ├── SecurityMasterQueryService.cs
│   │   │   ├── SecurityMasterRebuildOrchestrator.cs
│   │   │   ├── SecurityMasterService.cs
│   │   │   ├── SecurityResolver.cs
│   │   │   └── UflProjectionRebuilder.cs
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
│   │   │   ├── StoredProviderCredentialResolver.cs
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
│   │   ├── artifacts
│   │   ├── Engine
│   │   │   ├── BacktestContext.cs
│   │   │   ├── BacktestEngine.cs
│   │   │   ├── ContingentOrderManager.cs
│   │   │   ├── MultiSymbolMergeEnumerator.cs
│   │   │   ├── StageTimer.cs
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
│   │   ├── artifacts
│   │   ├── Ledger
│   │   ├── Strategies
│   │   │   ├── AdvancedCarry
│   │   │   │   ├── AdvancedCarryDecisionEngine.cs
│   │   │   │   ├── AdvancedCarryModels.cs
│   │   │   │   └── CarryTradeBacktestStrategy.cs
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
│   │   ├── BacktestStage.cs
│   │   ├── BacktestStageTelemetryDto.cs
│   │   ├── CashFlowEntry.cs
│   │   ├── ClosedLot.cs
│   │   ├── FillEvent.cs
│   │   ├── FinancialAccount.cs
│   │   ├── FinancialAccountSnapshot.cs
│   │   ├── GlobalUsings.cs
│   │   ├── IBacktestContext.cs
│   │   ├── IBacktestStrategy.cs
│   │   ├── IntermediateMetrics.cs
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
│   │   │   ├── DataIngestionContracts.cs
│   │   │   ├── ErrorResponse.cs
│   │   │   ├── ExecutionApiModels.cs
│   │   │   ├── LeanApiModels.cs
│   │   │   ├── LiveDataModels.cs
│   │   │   ├── OptionsModels.cs
│   │   │   ├── PositionLotModels.cs
│   │   │   ├── ProviderCatalog.cs
│   │   │   ├── ProviderRoutingApiModels.cs
│   │   │   ├── SecurityMasterIngestStatusModels.cs
│   │   │   ├── StatusEndpointModels.cs
│   │   │   ├── StatusModels.cs
│   │   │   ├── UiApiClient.cs
│   │   │   ├── UiApiRoutes.cs
│   │   │   └── UiDashboardModels.cs
│   │   ├── Archive
│   │   │   └── ArchiveHealthModels.cs
│   │   ├── artifacts
│   │   ├── Auth
│   │   │   ├── RolePermissions.cs
│   │   │   ├── UserPermission.cs
│   │   │   └── UserRole.cs
│   │   ├── Backfill
│   │   │   └── BackfillProgress.cs
│   │   ├── Backtesting
│   │   │   └── BacktestPreflightDtos.cs
│   │   ├── Banking
│   │   │   └── BankingModels.cs
│   │   ├── Catalog
│   │   │   ├── DirectoryIndex.cs
│   │   │   ├── ICanonicalSymbolRegistry.cs
│   │   │   ├── StorageCatalog.cs
│   │   │   └── SymbolRegistry.cs
│   │   ├── CertificatesOfDeposit
│   │   │   └── CertificateOfDepositReferenceDtos.cs
│   │   ├── Commodities
│   │   │   └── CommodityDtos.cs
│   │   ├── Configuration
│   │   │   ├── AppConfigDto.cs
│   │   │   ├── DerivativesConfigDto.cs
│   │   │   ├── MeridianPathDefaults.cs
│   │   │   ├── ProviderConnectionDtos.cs
│   │   │   ├── ProviderConnectionsConfigDto.cs
│   │   │   └── SymbolConfig.cs
│   │   ├── Credentials
│   │   │   ├── CredentialModels.cs
│   │   │   └── ISecretProvider.cs
│   │   ├── CryptoCurrency
│   │   │   └── CryptoDtos.cs
│   │   ├── Deposits
│   │   │   └── DepositDtos.cs
│   │   ├── Derivatives
│   │   │   └── SwapDtos.cs
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
│   │   │   │   ├── SessionStats.cs
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
│   │   ├── EnvironmentDesign
│   │   │   └── EnvironmentDesignDtos.cs
│   │   ├── Equity
│   │   │   └── EquityReferenceDtos.cs
│   │   ├── Etl
│   │   │   └── EtlModels.cs
│   │   ├── Export
│   │   │   ├── AnalysisExportModels.cs
│   │   │   ├── ExportPreset.cs
│   │   │   └── StandardPresets.cs
│   │   ├── FixedIncome
│   │   │   └── BondReferenceDtos.cs
│   │   ├── FundStructure
│   │   │   ├── AccountManagementDtos.cs
│   │   │   ├── AccountManagementOptions.cs
│   │   │   ├── FundStructureCommands.cs
│   │   │   ├── FundStructureContractsJsonContext.cs
│   │   │   ├── FundStructureDtos.cs
│   │   │   ├── FundStructureQueries.cs
│   │   │   └── LedgerGroupId.cs
│   │   ├── Futures
│   │   │   └── FutureReferenceDtos.cs
│   │   ├── FxSpot
│   │   │   └── FxSpotReferenceDtos.cs
│   │   ├── Ledger
│   │   │   └── LedgerBookDtos.cs
│   │   ├── Manifest
│   │   │   └── DataManifest.cs
│   │   ├── MoneyMarketFunds
│   │   │   └── MoneyMarketFundReferenceDtos.cs
│   │   ├── Options
│   │   │   └── OptionReferenceDtos.cs
│   │   ├── Pipeline
│   │   │   ├── IngestionJob.cs
│   │   │   ├── PipelinePolicyConstants.cs
│   │   │   └── UflOutboxMessage.cs
│   │   ├── RuleEvaluation
│   │   │   └── DecisionContracts.cs
│   │   ├── Schema
│   │   │   ├── EventSchema.cs
│   │   │   └── ISchemaUpcaster.cs
│   │   ├── SecurityMaster
│   │   │   ├── EdgarReferenceDtos.cs
│   │   │   ├── ISecurityMasterAmender.cs
│   │   │   ├── ISecurityMasterQueryService.cs
│   │   │   ├── ISecurityMasterRuntimeStatus.cs
│   │   │   ├── ISecurityMasterService.cs
│   │   │   ├── OperatorOverrides.cs
│   │   │   ├── SecurityCommands.cs
│   │   │   ├── SecurityDtos.cs
│   │   │   ├── SecurityEvents.cs
│   │   │   ├── SecurityIdentifiers.cs
│   │   │   ├── SecurityMasterOptions.cs
│   │   │   └── SecurityQueries.cs
│   │   ├── Services
│   │   │   ├── IBacktestPreflightService.cs
│   │   │   └── IConnectivityProbeService.cs
│   │   ├── Session
│   │   │   └── CollectionSession.cs
│   │   ├── Store
│   │   │   └── MarketDataQuery.cs
│   │   ├── Treasury
│   │   │   └── MoneyMarketFundDtos.cs
│   │   ├── Workstation
│   │   │   ├── BrokerageSyncDtos.cs
│   │   │   ├── EvidenceWorkflowDtos.cs
│   │   │   ├── FundLedgerDtos.cs
│   │   │   ├── FundOperationsDtos.cs
│   │   │   ├── FundOperationsWorkspaceDtos.cs
│   │   │   ├── IOperatorInboxService.cs
│   │   │   ├── PilotReadinessArtifactDtos.cs
│   │   │   ├── ReconciliationDtos.cs
│   │   │   ├── ResearchBriefingDtos.cs
│   │   │   ├── SecurityMasterTrustWorkbenchDtos.cs
│   │   │   ├── SecurityMasterWorkstationDtos.cs
│   │   │   ├── StrategyDesignDtos.cs
│   │   │   ├── StrategyRunContractCompatibility.cs
│   │   │   ├── StrategyRunReadModels.cs
│   │   │   ├── TradingOperatorReadinessDtos.cs
│   │   │   ├── WorkflowLibraryDtos.cs
│   │   │   ├── WorkflowSummaryDtos.cs
│   │   │   └── WorkstationBootstrapDtos.cs
│   │   └── Meridian.Contracts.csproj
│   ├── Meridian.Core
│   │   ├── artifacts
│   │   ├── Config
│   │   │   ├── AlpacaCredentialEnvironment.cs
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
│   │   │   ├── UnsupportedAssetClassException.cs
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
│   │   ├── artifacts
│   │   ├── Collectors
│   │   │   ├── IQuoteStateStore.cs
│   │   │   ├── L3OrderBookCollector.cs
│   │   │   ├── MarketDepthCollector.cs
│   │   │   ├── OptionDataCollector.cs
│   │   │   ├── QuoteCollector.cs
│   │   │   ├── SessionStatsCollector.cs
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
│   │   ├── Reconciliation
│   │   │   ├── BrokerStatementModels.cs
│   │   │   ├── StatementEntities.cs
│   │   │   └── StatementReconciliationAggregate.cs
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
│   │   ├── artifacts
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
│   │   │   ├── ExecutionFill.cs
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
│   │   │   ├── PositionLotSelector.cs
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
│   │   ├── artifacts
│   │   ├── Derivatives
│   │   │   ├── FutureDetails.cs
│   │   │   ├── OptionDetails.cs
│   │   │   └── OptionGreeks.cs
│   │   ├── BrokerageConfiguration.cs
│   │   ├── BrokerageValidationEvaluator.cs
│   │   ├── IBrokerageAccountSync.cs
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
│   │   ├── artifacts
│   │   ├── Calculations
│   │   │   ├── Aggregations.fs
│   │   │   ├── Imbalance.fs
│   │   │   └── Spread.fs
│   │   ├── Canonicalization
│   │   │   └── MappingRules.fs
│   │   ├── Domain
│   │   │   ├── AccountReconciliation.fs
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
│   │   │   ├── SettlementInstructionCommands.fs
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
│   │   ├── artifacts
│   │   ├── AggregateTypes.fs
│   │   ├── ContractAggregate.fs
│   │   ├── Interop.fs
│   │   ├── Meridian.FSharp.DirectLending.Aggregates.fsproj
│   │   └── ServicingAggregate.fs
│   ├── Meridian.FSharp.Ledger
│   │   ├── artifacts
│   │   ├── AccrualTypes.fs
│   │   ├── Interop.fs
│   │   ├── JournalValidation.fs
│   │   ├── LedgerReadModels.fs
│   │   ├── LedgerTypes.fs
│   │   ├── Meridian.FSharp.Ledger.fsproj
│   │   ├── PeriodManagement.fs
│   │   ├── Posting.fs
│   │   ├── Reconciliation.fs
│   │   ├── ReconciliationClassification.fs
│   │   ├── ReconciliationRules.fs
│   │   └── ReconciliationTypes.fs
│   ├── Meridian.FSharp.Trading
│   │   ├── artifacts
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
│   │   │   │   │   ├── MarketDataTracing.cs
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
│   │   │   │   ├── IHistoricalAggregateBarProvider.cs
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
│   │   │   │   ├── EdgarReferenceDataProvider.cs
│   │   │   │   ├── EdgarSecurityDocumentParser.cs
│   │   │   │   ├── EdgarSecurityMasterIngestProvider.cs
│   │   │   │   ├── EdgarSymbolSearchProvider.cs
│   │   │   │   └── IEdgarReferenceDataProvider.cs
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
│   │   │   │   ├── EnhancedIBConnectionManager.IBApiVendorStubs.cs
│   │   │   │   ├── IBApiLimits.cs
│   │   │   │   ├── IBApiVersionValidator.cs
│   │   │   │   ├── IBBrokerageGateway.cs
│   │   │   │   ├── IBBrokerageInterop.cs
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
│   │   │   │   ├── RobinhoodReadOnlyBrokerageSyncAdapter.cs
│   │   │   │   ├── RobinhoodSymbolSearchModels.cs
│   │   │   │   └── RobinhoodSymbolSearchProvider.cs
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
│   │   ├── artifacts
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
│   │   ├── Reconciliation
│   │   │   ├── BrokerStatementInfrastructure.cs
│   │   │   ├── BrokerStatementNormalizer.cs
│   │   │   └── ReconciliationCaseInfrastructure.cs
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
│   │   ├── artifacts
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
│   │   ├── artifacts
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
│   │   ├── artifacts
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
│   │   ├── artifacts
│   │   ├── Compilation
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
│   │   ├── QuantScriptServiceCollectionExtensions.cs
│   │   └── ScriptContext.cs
│   ├── Meridian.Risk
│   │   ├── artifacts
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
│   │   ├── artifacts
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
│   │   │   ├── ExportPreflightRules.cs
│   │   │   ├── ExportProfile.cs
│   │   │   ├── ExportRequest.cs
│   │   │   ├── ExportResult.cs
│   │   │   ├── ExportValidator.cs
│   │   │   ├── ExportVerificationReport.cs
│   │   │   ├── PreflightRule.cs
│   │   │   └── XlsxWorkbookWriter.cs
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
│   │   ├── Ledger
│   │   │   ├── Migrations
│   │   │   │   ├── V_ledger_001__journal_entries.sql
│   │   │   │   ├── V_ledger_002__accounting_periods.sql
│   │   │   │   ├── V_ledger_003__ledger_books.sql
│   │   │   │   ├── V_ledger_004__accounting_basis_policies.sql
│   │   │   │   └── V_ledger_005__journal_basis_lineage.sql
│   │   │   ├── ILedgerJournalStore.cs
│   │   │   ├── LedgerBookServiceException.cs
│   │   │   ├── LedgerJournalStoreOptions.cs
│   │   │   ├── LedgerStoreExtensions.cs
│   │   │   ├── PostgresLedgerBookService.cs
│   │   │   └── PostgresLedgerJournalStore.cs
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
│   │   │   │   ├── 003_security_master_corp_actions.sql
│   │   │   │   ├── 004_security_master_operator_overrides.sql
│   │   │   │   ├── 005_security_master_bond_reference_projection.sql
│   │   │   │   ├── 006_security_master_option_reference_projection.sql
│   │   │   │   ├── 007_security_master_equity_projection.sql
│   │   │   │   ├── 008_security_master_future_projection.sql
│   │   │   │   ├── 009_security_master_fxspot_projection.sql
│   │   │   │   ├── 010_security_master_swap_projection.sql
│   │   │   │   ├── 011_security_master_commodity_projection.sql
│   │   │   │   ├── 012_security_master_crypto_projection.sql
│   │   │   │   ├── 013_security_master_deposit_projection.sql
│   │   │   │   ├── 014_security_master_money_market_fund_projection.sql
│   │   │   │   └── 015_security_master_certificate_of_deposit_projection.sql
│   │   │   ├── FileEdgarReferenceDataStore.cs
│   │   │   ├── IBondReferenceProjectionStore.cs
│   │   │   ├── ICertificateOfDepositReferenceProjectionStore.cs
│   │   │   ├── ICommodityReferenceProjectionStore.cs
│   │   │   ├── ICryptoReferenceProjectionStore.cs
│   │   │   ├── IDepositReferenceProjectionStore.cs
│   │   │   ├── IEdgarReferenceDataStore.cs
│   │   │   ├── IEquityReferenceProjectionStore.cs
│   │   │   ├── IFutureReferenceProjectionStore.cs
│   │   │   ├── IFxSpotReferenceProjectionStore.cs
│   │   │   ├── IMoneyMarketFundReferenceProjectionStore.cs
│   │   │   ├── IOperatorOverridesStore.cs
│   │   │   ├── IOptionReferenceProjectionStore.cs
│   │   │   ├── ISecurityMasterEventStore.cs
│   │   │   ├── ISecurityMasterSnapshotStore.cs
│   │   │   ├── ISecurityMasterStore.cs
│   │   │   ├── ISwapReferenceProjectionStore.cs
│   │   │   ├── PostgresBondReferenceProjectionStore.cs
│   │   │   ├── PostgresCertificateOfDepositReferenceProjectionStore.cs
│   │   │   ├── PostgresCommodityReferenceProjectionStore.cs
│   │   │   ├── PostgresCryptoReferenceProjectionStore.cs
│   │   │   ├── PostgresDepositReferenceProjectionStore.cs
│   │   │   ├── PostgresEquityReferenceProjectionStore.cs
│   │   │   ├── PostgresFutureReferenceProjectionStore.cs
│   │   │   ├── PostgresFxSpotReferenceProjectionStore.cs
│   │   │   ├── PostgresMoneyMarketFundReferenceProjectionStore.cs
│   │   │   ├── PostgresOperatorOverridesStore.cs
│   │   │   ├── PostgresOptionReferenceProjectionStore.cs
│   │   │   ├── PostgresSecurityMasterEventStore.cs
│   │   │   ├── PostgresSecurityMasterSnapshotStore.cs
│   │   │   ├── PostgresSecurityMasterStore.cs
│   │   │   ├── PostgresSwapReferenceProjectionStore.cs
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
│   │   │   ├── QualityTrendStore.cs
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
│   │   ├── artifacts
│   │   ├── Interfaces
│   │   │   ├── ILiveStrategy.cs
│   │   │   ├── IPromotionRecordStore.cs
│   │   │   ├── IStrategyDesignRepository.cs
│   │   │   ├── IStrategyLifecycle.cs
│   │   │   └── IStrategyRepository.cs
│   │   ├── Models
│   │   │   ├── RunType.cs
│   │   │   ├── StrategyRunEntry.cs
│   │   │   ├── StrategyRunRepositoryQuery.cs
│   │   │   └── StrategyStatus.cs
│   │   ├── Promotions
│   │   │   ├── BacktestToLivePromoter.cs
│   │   │   ├── PromotionApprovalChecklist.cs
│   │   │   └── PromotionRecordService.cs
│   │   ├── Serialization
│   │   │   ├── FSharpInteropJsonContext.cs
│   │   │   ├── PromotionRecordJsonContext.cs
│   │   │   └── StrategyDesignJsonContext.cs
│   │   ├── Services
│   │   │   ├── AggregatePortfolioService.cs
│   │   │   ├── CashFlowProjectionService.cs
│   │   │   ├── FileReconciliationBreakQueueRepository.cs
│   │   │   ├── IAggregatePortfolioService.cs
│   │   │   ├── InMemoryReconciliationRunRepository.cs
│   │   │   ├── IReconciliationBreakQueueRepository.cs
│   │   │   ├── IReconciliationRunRepository.cs
│   │   │   ├── IReconciliationRunService.cs
│   │   │   ├── ISecurityReferenceLookup.cs
│   │   │   ├── LedgerReadService.cs
│   │   │   ├── PortfolioReadService.cs
│   │   │   ├── PromotionService.cs
│   │   │   ├── ReconciliationGovernanceService.cs
│   │   │   ├── ReconciliationProjectionService.cs
│   │   │   ├── ReconciliationRunService.cs
│   │   │   ├── ReconciliationSourceAdapters.cs
│   │   │   ├── StrategyDesignService.cs
│   │   │   ├── StrategyLifecycleManager.cs
│   │   │   ├── StrategyRunContinuityService.cs
│   │   │   ├── StrategyRunReadService.cs
│   │   │   └── StrategyRunScopeMetadataResolver.cs
│   │   ├── Storage
│   │   │   ├── JsonlPromotionRecordStore.cs
│   │   │   ├── JsonlStrategyDesignRepository.cs
│   │   │   └── StrategyRunStore.cs
│   │   ├── GlobalUsings.cs
│   │   └── Meridian.Strategies.csproj
│   ├── Meridian.Ui
│   │   ├── dashboard
│   │   │   ├── scripts
│   │   │   │   └── run-vitest-stable.mjs
│   │   │   ├── src
│   │   │   │   ├── assets
│   │   │   │   │   └── brand
│   │   │   │   │       └── meridian-mark.svg
│   │   │   │   ├── components
│   │   │   │   │   ├── data
│   │   │   │   │   │   ├── backfill-validation-dashboard.tsx
│   │   │   │   │   │   └── symbol-universe-manager.tsx
│   │   │   │   │   ├── meridian
│   │   │   │   │   │   ├── command-palette.test.tsx
│   │   │   │   │   │   ├── command-palette.tsx
│   │   │   │   │   │   ├── command-palette.view-model.test.ts
│   │   │   │   │   │   ├── command-palette.view-model.ts
│   │   │   │   │   │   ├── historical-chart.test.tsx
│   │   │   │   │   │   ├── historical-chart.tsx
│   │   │   │   │   │   ├── historical-chart.view-model.test.ts
│   │   │   │   │   │   ├── historical-chart.view-model.ts
│   │   │   │   │   │   ├── mega-menu.test.tsx
│   │   │   │   │   │   ├── mega-menu.tsx
│   │   │   │   │   │   ├── mega-menu.view-model.test.ts
│   │   │   │   │   │   ├── mega-menu.view-model.ts
│   │   │   │   │   │   ├── metric-card.test.tsx
│   │   │   │   │   │   ├── metric-card.tsx
│   │   │   │   │   │   ├── metric-card.view-model.test.ts
│   │   │   │   │   │   ├── metric-card.view-model.ts
│   │   │   │   │   │   ├── quant-notebook.test.tsx
│   │   │   │   │   │   ├── quant-notebook.tsx
│   │   │   │   │   │   ├── quant-notebook.view-model.test.ts
│   │   │   │   │   │   ├── quant-notebook.view-model.ts
│   │   │   │   │   │   ├── quant-plot.test.tsx
│   │   │   │   │   │   ├── quant-plot.tsx
│   │   │   │   │   │   ├── quant-plot.view-model.ts
│   │   │   │   │   │   ├── security-details-tracker.test.tsx
│   │   │   │   │   │   ├── security-details-tracker.tsx
│   │   │   │   │   │   ├── security-details-tracker.view-model.test.ts
│   │   │   │   │   │   ├── security-details-tracker.view-model.ts
│   │   │   │   │   │   ├── ui-kit-primitives.test.tsx
│   │   │   │   │   │   ├── ui-kit-primitives.tsx
│   │   │   │   │   │   ├── workspace-header.test.tsx
│   │   │   │   │   │   ├── workspace-header.tsx
│   │   │   │   │   │   ├── workspace-header.view-model.test.ts
│   │   │   │   │   │   ├── workspace-header.view-model.ts
│   │   │   │   │   │   ├── workspace-nav.test.tsx
│   │   │   │   │   │   ├── workspace-nav.tsx
│   │   │   │   │   │   ├── workspace-nav.view-model.test.ts
│   │   │   │   │   │   └── workspace-nav.view-model.ts
│   │   │   │   │   ├── settings
│   │   │   │   │   │   └── provider-credential-setup.tsx
│   │   │   │   │   └── ui
│   │   │   │   │       ├── badge.tsx
│   │   │   │   │       ├── button.test.tsx
│   │   │   │   │       ├── button.tsx
│   │   │   │   │       ├── button.view-model.test.ts
│   │   │   │   │       ├── button.view-model.ts
│   │   │   │   │       ├── card.tsx
│   │   │   │   │       ├── dialog.test.tsx
│   │   │   │   │       ├── dialog.tsx
│   │   │   │   │       ├── dialog.view-model.test.ts
│   │   │   │   │       ├── dialog.view-model.ts
│   │   │   │   │       ├── input.tsx
│   │   │   │   │       ├── label.tsx
│   │   │   │   │       ├── progress.tsx
│   │   │   │   │       ├── risk-control-panel.test.tsx
│   │   │   │   │       ├── risk-control-panel.tsx
│   │   │   │   │       ├── risk-control-panel.view-model.test.ts
│   │   │   │   │       ├── risk-control-panel.view-model.ts
│   │   │   │   │       ├── select.tsx
│   │   │   │   │       ├── sheet.test.tsx
│   │   │   │   │       ├── sheet.tsx
│   │   │   │   │       └── tooltip.tsx
│   │   │   │   ├── hooks
│   │   │   │   │   ├── use-workstation-data.test.ts
│   │   │   │   │   └── use-workstation-data.ts
│   │   │   │   ├── lib
│   │   │   │   │   ├── api
│   │   │   │   │   │   ├── covered-call.test.ts
│   │   │   │   │   │   └── covered-call.ts
│   │   │   │   │   ├── covered-call
│   │   │   │   │   │   ├── payoff.test.ts
│   │   │   │   │   │   └── payoff.ts
│   │   │   │   │   ├── price-alerts
│   │   │   │   │   │   ├── evaluator.test.ts
│   │   │   │   │   │   ├── evaluator.ts
│   │   │   │   │   │   ├── service.test.tsx
│   │   │   │   │   │   ├── service.ts
│   │   │   │   │   │   ├── storage.test.ts
│   │   │   │   │   │   ├── storage.ts
│   │   │   │   │   │   └── types.ts
│   │   │   │   │   ├── api.trading.test.ts
│   │   │   │   │   ├── api.ts
│   │   │   │   │   ├── dev-fixtures.ts
│   │   │   │   │   ├── utils.ts
│   │   │   │   │   ├── workspace.test.ts
│   │   │   │   │   ├── workspace.ts
│   │   │   │   │   ├── workstation-endpoints.test.ts
│   │   │   │   │   └── workstation-endpoints.ts
│   │   │   │   ├── screens
│   │   │   │   │   ├── covered-call-screen.test.tsx
│   │   │   │   │   ├── covered-call-screen.tsx
│   │   │   │   │   ├── covered-call-screen.view-model.test.ts
│   │   │   │   │   ├── covered-call-screen.view-model.ts
│   │   │   │   │   ├── data-operations-screen.security-master.ts
│   │   │   │   │   ├── data-operations-screen.test.tsx
│   │   │   │   │   ├── data-operations-screen.tsx
│   │   │   │   │   ├── data-operations-screen.view-model.test.ts
│   │   │   │   │   ├── data-operations-screen.view-model.ts
│   │   │   │   │   ├── evidence-workbench-screen.tsx
│   │   │   │   │   ├── evidence-workbench-screen.view-model.test.tsx
│   │   │   │   │   ├── evidence-workbench-screen.view-model.ts
│   │   │   │   │   ├── governance-screen.test.tsx
│   │   │   │   │   ├── governance-screen.tsx
│   │   │   │   │   ├── governance-screen.view-model.test.ts
│   │   │   │   │   ├── governance-screen.view-model.ts
│   │   │   │   │   ├── live-quotes-screen.test.tsx
│   │   │   │   │   ├── live-quotes-screen.tsx
│   │   │   │   │   ├── live-quotes-screen.view-model.ts
│   │   │   │   │   ├── operator-readiness-console.test.tsx
│   │   │   │   │   ├── operator-readiness-console.tsx
│   │   │   │   │   ├── operator-readiness-console.view-model.test.ts
│   │   │   │   │   ├── operator-readiness-console.view-model.ts
│   │   │   │   │   ├── overview-screen.test.tsx
│   │   │   │   │   ├── overview-screen.tsx
│   │   │   │   │   ├── overview-screen.view-model.test.ts
│   │   │   │   │   ├── overview-screen.view-model.ts
│   │   │   │   │   ├── portfolio-screen.test.tsx
│   │   │   │   │   ├── portfolio-screen.tsx
│   │   │   │   │   ├── portfolio-screen.view-model.test.ts
│   │   │   │   │   ├── portfolio-screen.view-model.ts
│   │   │   │   │   ├── price-alerts-screen.test.tsx
│   │   │   │   │   ├── price-alerts-screen.tsx
│   │   │   │   │   ├── price-alerts-screen.view-model.test.ts
│   │   │   │   │   ├── price-alerts-screen.view-model.ts
│   │   │   │   │   ├── quant-lab-screen.test.tsx
│   │   │   │   │   ├── quant-lab-screen.tsx
│   │   │   │   │   ├── quant-lab-screen.view-model.test.ts
│   │   │   │   │   ├── quant-lab-screen.view-model.ts
│   │   │   │   │   ├── reporting-screen.test.tsx
│   │   │   │   │   ├── reporting-screen.tsx
│   │   │   │   │   ├── reporting-screen.view-model.test.ts
│   │   │   │   │   ├── reporting-screen.view-model.ts
│   │   │   │   │   ├── research-screen.test.tsx
│   │   │   │   │   ├── research-screen.tsx
│   │   │   │   │   ├── research-screen.view-model.test.ts
│   │   │   │   │   ├── research-screen.view-model.ts
│   │   │   │   │   ├── settings-screen.test.tsx
│   │   │   │   │   ├── settings-screen.tsx
│   │   │   │   │   ├── settings-screen.view-model.test.ts
│   │   │   │   │   ├── settings-screen.view-model.ts
│   │   │   │   │   ├── strategy-designer-screen.test.tsx
│   │   │   │   │   ├── strategy-designer-screen.tsx
│   │   │   │   │   ├── strategy-designer-screen.view-model.test.ts
│   │   │   │   │   ├── strategy-designer-screen.view-model.ts
│   │   │   │   │   ├── today-panel.view-model.test.ts
│   │   │   │   │   ├── today-panel.view-model.ts
│   │   │   │   │   ├── trading-screen.test.tsx
│   │   │   │   │   ├── trading-screen.tsx
│   │   │   │   │   ├── trading-screen.view-model.test.ts
│   │   │   │   │   ├── trading-screen.view-model.ts
│   │   │   │   │   ├── watchlist-screen.test.tsx
│   │   │   │   │   ├── watchlist-screen.tsx
│   │   │   │   │   ├── watchlist-screen.view-model.test.ts
│   │   │   │   │   ├── watchlist-screen.view-model.ts
│   │   │   │   │   ├── workspace-placeholder-screen.test.tsx
│   │   │   │   │   ├── workspace-placeholder-screen.tsx
│   │   │   │   │   ├── workspace-placeholder-screen.view-model.test.ts
│   │   │   │   │   └── workspace-placeholder-screen.view-model.ts
│   │   │   │   ├── styles
│   │   │   │   │   └── index.css
│   │   │   │   ├── test
│   │   │   │   │   ├── render.tsx
│   │   │   │   │   └── setup.ts
│   │   │   │   ├── types
│   │   │   │   │   └── covered-call.ts
│   │   │   │   ├── app-shell.view-model.test.ts
│   │   │   │   ├── app-shell.view-model.ts
│   │   │   │   ├── app.test.tsx
│   │   │   │   ├── app.tsx
│   │   │   │   ├── design-system-contract.test.ts
│   │   │   │   ├── main.tsx
│   │   │   │   ├── types.ts
│   │   │   │   ├── vite-config.test.ts
│   │   │   │   └── vite-env.d.ts
│   │   │   ├── index.html
│   │   │   ├── package-lock.json
│   │   │   ├── package.json
│   │   │   ├── postcss.config.cjs
│   │   │   ├── tailwind.config.ts
│   │   │   ├── tsconfig.json
│   │   │   ├── tsconfig.node.json
│   │   │   └── vite.config.ts
│   │   └── wwwroot
│   │       └── workstation
│   │           ├── assets
│   │           │   ├── activity-Mq_ywP-L.js
│   │           │   ├── briefcase-business-BBZxe3E4.js
│   │           │   ├── card-DhqoWZ1O.js
│   │           │   ├── chart-column-BZ2It6nL.js
│   │           │   ├── chart-line-oCr5VQf4.js
│   │           │   ├── circle-alert-DbhoCMTQ.js
│   │           │   ├── circle-check-9Z2mdihI.js
│   │           │   ├── circle-play-FODKShUW.js
│   │           │   ├── circle-x-BpQ_wOeU.js
│   │           │   ├── clipboard-list-xk9niTzc.js
│   │           │   ├── covered-call-screen-DtR7Pah7.js
│   │           │   ├── data-operations-screen-XzF_6qSu.js
│   │           │   ├── database-DNpfpB-K.js
│   │           │   ├── dialog-BQREyRFA.js
│   │           │   ├── evidence-workbench-screen-zwfIVZKm.js
│   │           │   ├── external-link-BjuUaRBA.js
│   │           │   ├── eye-hOXLrG9s.js
│   │           │   ├── file-text-br34IWdn.js
│   │           │   ├── governance-screen-7of8SlF-.js
│   │           │   ├── index-B06V3WmN.css
│   │           │   ├── index-DaUTALrS.js
│   │           │   ├── input-BIosQRIo.js
│   │           │   ├── layers-Bgzx9kxR.js
│   │           │   ├── live-quotes-screen-DIaqMxwG.js
│   │           │   ├── metric-card-CXCyv-S0.js
│   │           │   ├── network-BZRuqqLT.js
│   │           │   ├── operator-readiness-console-Cpa_pNe0.js
│   │           │   ├── overview-screen-DvlMePDV.js
│   │           │   ├── play-DpIdR1Uk.js
│   │           │   ├── plus-BGp63xOt.js
│   │           │   ├── portfolio-screen-FNrGQZVY.js
│   │           │   ├── price-alerts-screen-i_yQjBv5.js
│   │           │   ├── quant-lab-screen-B6n3S-q4.js
│   │           │   ├── refresh-cw-BM5_NYgp.js
│   │           │   ├── reporting-screen-CG_dH8Af.js
│   │           │   ├── research-screen-BIYvjQq9.js
│   │           │   ├── rotate-ccw-2m2MRyBH.js
│   │           │   ├── save-D2AluAJ3.js
│   │           │   ├── select-DKCGm6de.js
│   │           │   ├── settings-screen-PTWWJho5.js
│   │           │   ├── shield-check-BcS_g3xY.js
│   │           │   ├── sparkles-B8NgbrtF.js
│   │           │   ├── strategy-designer-screen-DgHhNHuY.js
│   │           │   ├── trading-screen-Cwno5ufg.js
│   │           │   ├── trash-2-CY1Wl5JZ.js
│   │           │   ├── trending-up-SC38jMnH.js
│   │           │   ├── wallet-CPXrq_Cv.js
│   │           │   └── watchlist-screen-B7hhXvwZ.js
│   │           └── index.html
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
│   │   │   ├── Reconciliation
│   │   │   │   └── ReconciliationApiService.cs
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
│   │   │   ├── DesktopShellPreferences.cs
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
│   │   ├── artifacts
│   │   ├── Contracts
│   │   │   ├── Reconciliation
│   │   │   │   └── StatementImportContracts.cs
│   │   │   └── CoveredCallContracts.cs
│   │   ├── Endpoints
│   │   │   ├── AdminEndpoints.cs
│   │   │   ├── AnalyticsEndpoints.cs
│   │   │   ├── ApiKeyMiddleware.cs
│   │   │   ├── AuthEndpoints.cs
│   │   │   ├── AuthenticationMode.cs
│   │   │   ├── BackfillEndpoints.cs
│   │   │   ├── BackfillScheduleEndpoints.cs
│   │   │   ├── BackfillValidationEndpoints.cs
│   │   │   ├── BankingEndpoints.cs
│   │   │   ├── BondReferenceEndpoints.cs
│   │   │   ├── BrokerageConnectionEndpoints.cs
│   │   │   ├── CalendarEndpoints.cs
│   │   │   ├── CanonicalizationEndpoints.cs
│   │   │   ├── CatalogEndpoints.cs
│   │   │   ├── CertificateOfDepositReferenceEndpoints.cs
│   │   │   ├── CheckpointEndpoints.cs
│   │   │   ├── CommodityReferenceEndpoints.cs
│   │   │   ├── ConfigEndpoints.cs
│   │   │   ├── CoveredCallEndpoints.cs
│   │   │   ├── CppTraderEndpoints.cs
│   │   │   ├── CredentialEndpoints.cs
│   │   │   ├── CronEndpoints.cs
│   │   │   ├── CryptoReferenceEndpoints.cs
│   │   │   ├── DemoModeEndpoints.cs
│   │   │   ├── DepositReferenceEndpoints.cs
│   │   │   ├── DiagnosticsEndpoints.cs
│   │   │   ├── DirectLendingEndpoints.cs
│   │   │   ├── EdgarReferenceDataEndpoints.cs
│   │   │   ├── EndpointHelpers.cs
│   │   │   ├── EnvironmentDesignerEndpoints.cs
│   │   │   ├── EquityReferenceEndpoints.cs
│   │   │   ├── EvidenceEndpoints.cs
│   │   │   ├── ExecutionEndpoints.cs
│   │   │   ├── ExportEndpoints.cs
│   │   │   ├── FailoverEndpoints.cs
│   │   │   ├── FundAccountEndpoints.cs
│   │   │   ├── FundStructureEndpoints.cs
│   │   │   ├── FutureReferenceEndpoints.cs
│   │   │   ├── FxSpotReferenceEndpoints.cs
│   │   │   ├── HealthEndpoints.cs
│   │   │   ├── HistoricalEndpoints.cs
│   │   │   ├── IBEndpoints.cs
│   │   │   ├── IngestionJobEndpoints.cs
│   │   │   ├── LeanEndpoints.cs
│   │   │   ├── LedgerEndpoints.cs
│   │   │   ├── LiveDataEndpoints.cs
│   │   │   ├── LoginSessionMiddleware.cs
│   │   │   ├── MaintenanceScheduleEndpoints.cs
│   │   │   ├── MessagingEndpoints.cs
│   │   │   ├── MoneyMarketFundEndpoints.cs
│   │   │   ├── MoneyMarketFundReferenceEndpoints.cs
│   │   │   ├── OptionChainEndpoints.cs
│   │   │   ├── OptionReferenceEndpoints.cs
│   │   │   ├── OptionsEndpoints.cs
│   │   │   ├── PathValidation.cs
│   │   │   ├── PromotionEndpoints.cs
│   │   │   ├── ProviderConnectionEndpoints.cs
│   │   │   ├── ProviderCredentialEndpoints.cs
│   │   │   ├── ProviderEndpoints.cs
│   │   │   ├── ProviderExtendedEndpoints.cs
│   │   │   ├── QuantLabEndpoints.cs
│   │   │   ├── ReplayEndpoints.cs
│   │   │   ├── ResilienceEndpoints.cs
│   │   │   ├── RiskEndpoints.cs
│   │   │   ├── SamplingEndpoints.cs
│   │   │   ├── SecurityMasterEndpoints.cs
│   │   │   ├── StatusEndpoints.cs
│   │   │   ├── StorageEndpoints.cs
│   │   │   ├── StorageQualityEndpoints.cs
│   │   │   ├── StrategyLifecycleEndpoints.cs
│   │   │   ├── SubscriptionEndpoints.cs
│   │   │   ├── SwapReferenceEndpoints.cs
│   │   │   ├── SymbolEndpoints.cs
│   │   │   ├── SymbolMappingEndpoints.cs
│   │   │   ├── UiEndpoints.cs
│   │   │   ├── WorkstationEndpoints.cs
│   │   │   └── WorkstationRiskEndpoints.cs
│   │   ├── Evidence
│   │   │   ├── EvidenceContribution.cs
│   │   │   ├── EvidenceContributors.cs
│   │   │   ├── EvidenceGraphService.cs
│   │   │   ├── EvidenceSubjectResolver.cs
│   │   │   ├── EvidenceTemplateRegistry.cs
│   │   │   ├── EvidenceWorkflowServiceCollectionExtensions.cs
│   │   │   └── FileEvidenceArtifactStore.cs
│   │   ├── Serialization
│   │   │   ├── CoveredCallJsonContext.cs
│   │   │   └── DirectLendingJsonContext.cs
│   │   ├── Services
│   │   │   ├── CoveredCall
│   │   │   │   ├── CoveredCallBacktestOptions.cs
│   │   │   │   ├── CoveredCallBacktestService.cs
│   │   │   │   ├── CoveredCallChainProviderAdapter.cs
│   │   │   │   ├── CoveredCallChainProviderFactory.cs
│   │   │   │   ├── CoveredCallRunProjection.cs
│   │   │   │   ├── ICoveredCallBacktestService.cs
│   │   │   │   └── ICoveredCallChainProviderFactory.cs
│   │   │   ├── AlpacaBrokerageConnectionService.cs
│   │   │   ├── BackfillCoordinator.cs
│   │   │   ├── BrokerageConnectionService.cs
│   │   │   ├── BrokeragePortfolioSyncService.cs
│   │   │   ├── ConfigStore.cs
│   │   │   ├── Dk1TrustGateReadinessService.cs
│   │   │   ├── FundOperationsWorkspaceReadService.cs
│   │   │   ├── GovernanceReportPackRepository.cs
│   │   │   ├── InMemoryOperatorInboxService.cs
│   │   │   ├── OperatorRiskRuleService.cs
│   │   │   ├── ProviderConnectionLifecycleService.cs
│   │   │   ├── RiskRuleRuntimeService.cs
│   │   │   ├── SecurityMasterSecurityReferenceLookup.cs
│   │   │   ├── SecurityMasterWorkbenchQueryService.cs
│   │   │   ├── StrategyRunReviewPacketService.cs
│   │   │   ├── TradingOperatorReadinessService.cs
│   │   │   ├── WorkstationServiceCollectionExtensions.cs
│   │   │   └── WorkstationWorkflowSummaryService.cs
│   │   ├── Workflows
│   │   │   ├── BuiltInWorkflowDefinitionProvider.cs
│   │   │   ├── FileWorkflowPresetStore.cs
│   │   │   ├── IWorkflowActionCatalog.cs
│   │   │   ├── IWorkflowDefinitionProvider.cs
│   │   │   ├── IWorkflowPresetStore.cs
│   │   │   ├── WorkflowActionIds.cs
│   │   │   ├── WorkflowLibraryService.cs
│   │   │   ├── WorkflowPresetService.cs
│   │   │   ├── WorkflowRegistry.cs
│   │   │   └── WorkflowServiceCollectionExtensions.cs
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
│   │   ├── ScoreExplanationProjection.cs
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
│       ├── Controls
│       │   └── AutomationLeafBorder.cs
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
│       ├── Copy
│       │   └── WorkspaceCopyCatalog.cs
│       ├── Features
│       │   ├── Data
│       │   │   ├── Shell
│       │   │   │   ├── DataWorkspaceShellPage.xaml
│       │   │   │   ├── DataWorkspaceShellPage.xaml.cs
│       │   │   │   ├── DataWorkspaceShellPresentationService.cs
│       │   │   │   ├── DataWorkspaceShellSnapshotService.cs
│       │   │   │   └── DataWorkspaceShellViewModel.cs
│       │   │   └── DataFeatureModule.cs
│       │   ├── DesktopFeatureModuleRegistry.cs
│       │   └── IDesktopFeatureModule.cs
│       ├── Models
│       │   ├── ActionEntry.cs
│       │   ├── ActivityLogModels.cs
│       │   ├── AlignmentModels.cs
│       │   ├── AppConfig.cs
│       │   ├── BackfillModels.cs
│       │   ├── BlotterModels.cs
│       │   ├── DashboardModels.cs
│       │   ├── DataQualityModels.cs
│       │   ├── FundLedgerDimensionView.cs
│       │   ├── FundProfileModels.cs
│       │   ├── FundReconciliationWorkbenchModels.cs
│       │   ├── LeanModels.cs
│       │   ├── LiveDataModels.cs
│       │   ├── NotificationModels.cs
│       │   ├── OrderBookModels.cs
│       │   ├── PaneDropAction.cs
│       │   ├── PaneDropEventArgs.cs
│       │   ├── PaneLayout.cs
│       │   ├── ProviderHealthModels.cs
│       │   ├── QuantScriptExecutionHistoryModels.cs
│       │   ├── QuantScriptModels.cs
│       │   ├── ResearchWorkspaceShellPresentationModels.cs
│       │   ├── SecurityMasterPresentationModels.cs
│       │   ├── SettingsModels.cs
│       │   ├── ShellNavigationCatalog.cs
│       │   ├── ShellNavigationCatalog.DataOperations.cs
│       │   ├── ShellNavigationCatalog.Governance.cs
│       │   ├── ShellNavigationCatalog.Research.cs
│       │   ├── ShellNavigationCatalog.Trading.cs
│       │   ├── ShellNavigationCatalog.Workspaces.cs
│       │   ├── ShellNavigationModels.cs
│       │   ├── ShellNavigationTextStyleGuide.cs
│       │   ├── StorageDisplayModels.cs
│       │   ├── SymbolsModels.cs
│       │   ├── TradingWorkspaceShellPresentationModels.cs
│       │   ├── WatchlistModels.cs
│       │   ├── WorkspaceDefinition.cs
│       │   ├── WorkspaceQueueRegionState.cs
│       │   ├── WorkspaceRegistry.cs
│       │   ├── WorkspaceShellChromeModels.cs
│       │   ├── WorkspaceShellModels.cs
│       │   └── WorkstationOperatingContextModels.cs
│       ├── Services
│       │   ├── AgentLoopService.cs
│       │   ├── ApiStatusService.cs
│       │   ├── ArchiveHealthService.cs
│       │   ├── BackendServiceManager.cs
│       │   ├── BackgroundTaskSchedulerService.cs
│       │   ├── BacktestDataAvailabilityService.cs
│       │   ├── BacktestService.cs
│       │   ├── BrushRegistry.cs
│       │   ├── CashFinancingReadService.cs
│       │   ├── ClipboardWatcherService.cs
│       │   ├── ConfigService.cs
│       │   ├── ConnectionService.cs
│       │   ├── ContextMenuService.cs
│       │   ├── CredentialService.cs
│       │   ├── DataOperationsWorkspacePresentationBuilder.cs
│       │   ├── DesktopLaunchArguments.cs
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
│       │   ├── IWorkspaceShellStateProvider.cs
│       │   ├── JumpListService.cs
│       │   ├── KeyboardShortcutService.cs
│       │   ├── LoggingService.cs
│       │   ├── MessagingService.cs
│       │   ├── NavigationService.cs
│       │   ├── NotificationService.cs
│       │   ├── OfflineTrackingPersistenceService.cs
│       │   ├── PendingOperationsQueueService.cs
│       │   ├── QuantScriptExecutionHistoryService.cs
│       │   ├── QuantScriptLayoutService.cs
│       │   ├── QuantScriptTemplateCatalogService.cs
│       │   ├── ReconciliationReadService.cs
│       │   ├── ResearchWorkspaceShellPresentationService.cs
│       │   ├── RetentionAssuranceService.cs
│       │   ├── RunMatService.cs
│       │   ├── SchemaService.cs
│       │   ├── SecurityMasterOperatorWorkflowClient.cs
│       │   ├── SecurityMasterRuntimeStatusService.cs
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
│       │   ├── TradingWorkspaceShellPresentationService.cs
│       │   ├── TypeForwards.cs
│       │   ├── WatchlistService.cs
│       │   ├── WindowStartupRecovery.cs
│       │   ├── WorkspaceService.cs
│       │   ├── WorkspaceShellContextService.cs
│       │   ├── WorkspaceShellStateProviders.cs
│       │   ├── WorkstationOperatingContextService.cs
│       │   ├── WorkstationOperatorInboxApiClient.cs
│       │   ├── WorkstationReconciliationApiClient.cs
│       │   ├── WorkstationResearchBriefingService.cs
│       │   ├── WorkstationSecurityMasterApiClient.cs
│       │   └── WpfShellServiceCollectionExtensions.cs
│       ├── Shell
│       │   ├── Models
│       │   │   ├── PaneContentState.cs
│       │   │   ├── PaneDropResult.cs
│       │   │   └── ShellRoute.cs
│       │   ├── Refresh
│       │   │   └── ShellRefreshCoordinator.cs
│       │   ├── Root
│       │   │   ├── DesktopLaunchRouter.cs
│       │   │   ├── DesktopShellCoordinator.cs
│       │   │   └── FileDropRouter.cs
│       │   ├── Services
│       │   │   ├── IPageContentFactory.cs
│       │   │   ├── IShellNavigationCoordinator.cs
│       │   │   ├── IShellRouteRegistry.cs
│       │   │   ├── PageContentFactory.cs
│       │   │   ├── ShellNavigationCoordinator.cs
│       │   │   └── ShellRouteRegistry.cs
│       │   ├── Session
│       │   │   ├── DesktopShellSessionService.cs
│       │   │   ├── DesktopWindowState.cs
│       │   │   ├── IWindowStateStore.cs
│       │   │   └── WindowStateStore.cs
│       │   └── ViewModels
│       │       ├── CommandPaletteViewModel.cs
│       │       ├── OperatorInboxViewModel.cs
│       │       ├── PaneHostViewModel.cs
│       │       └── WorkflowSummaryStripViewModel.cs
│       ├── Styles
│       │   ├── Animations.xaml
│       │   ├── AppStyles.xaml
│       │   ├── BrandResources.xaml
│       │   ├── IconResources.xaml
│       │   ├── ThemeControls.xaml
│       │   ├── ThemeSurfaces.xaml
│       │   ├── ThemeTokens.xaml
│       │   └── ThemeTypography.xaml
│       ├── Templates
│       │   └── QuantScript
│       │       ├── catalog.json
│       │       ├── hello-spy.csx
│       │       ├── indicator-sma.csx
│       │       └── single-symbol-backtest.csx
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
│       │   ├── CarryTradeBacktestViewModel.cs
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
│       │   ├── ResearchWorkspaceShellViewModel.cs
│       │   ├── RetentionAssuranceViewModel.cs
│       │   ├── RunMatViewModel.cs
│       │   ├── RunRiskViewModel.cs
│       │   ├── ScatterAnalysisViewModel.cs
│       │   ├── ScheduleManagerViewModel.cs
│       │   ├── SecurityConflictLaneModels.cs
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
│       │   ├── SymbolMappingViewModel.cs
│       │   ├── SymbolsPageViewModel.cs
│       │   ├── SystemHealthViewModel.cs
│       │   ├── TickerStripViewModel.cs
│       │   ├── TimeSeriesAlignmentViewModel.cs
│       │   ├── TradingHoursViewModel.cs
│       │   ├── TradingWorkspaceShellViewModel.cs
│       │   ├── WatchlistViewModel.cs
│       │   ├── WelcomePageViewModel.cs
│       │   ├── WorkflowLibraryViewModel.cs
│       │   ├── WorkspacePageViewModel.cs
│       │   └── WorkspaceShellViewModelBase.cs
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
│       │   ├── CarryTradeBacktestPage.xaml
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
│       │   ├── EditScheduledJobDialog.xaml
│       │   ├── EditScheduledJobDialog.xaml.cs
│       │   ├── EditWatchlistDialog.cs
│       │   ├── EnvironmentDesignerPage.xaml
│       │   ├── EnvironmentDesignerPage.xaml.cs
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
│       │   ├── WorkflowLibraryPage.xaml
│       │   ├── WorkflowLibraryPage.xaml.cs
│       │   ├── WorkspaceCapabilityHomePage.cs
│       │   ├── WorkspaceCommandBarControl.xaml
│       │   ├── WorkspaceCommandBarControl.xaml.cs
│       │   ├── WorkspaceDeepPageHostPage.xaml
│       │   ├── WorkspaceDeepPageHostPage.xaml.cs
│       │   ├── WorkspacePage.xaml
│       │   ├── WorkspacePage.xaml.cs
│       │   ├── WorkspaceShellChromeState.cs
│       │   ├── WorkspaceShellContextStripControl.xaml
│       │   ├── WorkspaceShellContextStripControl.xaml.cs
│       │   ├── WorkspaceShellFallbackContentFactory.cs
│       │   └── WorkspaceShellPageBase.cs
│       ├── App.xaml
│       ├── App.xaml.cs
│       ├── AssemblyInfo.cs
│       ├── GlobalUsings.cs
│       ├── MainWindow.xaml
│       ├── MainWindow.xaml.cs
│       ├── Meridian.Wpf.csproj
│       ├── Package.appxmanifest
│       └── README.md
├── tests
│   ├── Meridian.Backtesting.Tests
│   │   ├── AdvancedCarryDecisionEngineTests.cs
│   │   ├── BacktestEngineIntegrationTests.cs
│   │   ├── BacktestMetricsEngineTests.cs
│   │   ├── BacktestPreflightServiceTests.cs
│   │   ├── BacktestRequestConfigTests.cs
│   │   ├── BatchBacktestServiceTests.cs
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
│   │   ├── StageTelemetryTests.cs
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
│   │   ├── PeriodManagementTests.fs
│   │   ├── PipelineTests.fs
│   │   ├── PromotionPolicyTests.fs
│   │   ├── RiskPolicyTests.fs
│   │   ├── SettlementInstructionCommandsTests.fs
│   │   ├── TradingTransitionTests.fs
│   │   └── ValidationTests.fs
│   ├── Meridian.FundStructure.Tests
│   │   ├── EnvironmentDesignerServiceTests.cs
│   │   ├── GovernanceSharedDataAccessServiceTests.cs
│   │   ├── InMemoryFundStructureServiceTests.cs
│   │   └── Meridian.FundStructure.Tests.csproj
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
│   │   ├── NotebookExecutionSessionTests.cs
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
│   │   │   │   ├── AutoGapRemediationServiceTests.cs
│   │   │   │   ├── BackfillCoordinatorPreviewTests.cs
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
│   │   │   │   ├── TwelveDataNasdaqProviderContractTests.cs
│   │   │   │   └── YahooFinanceIntradayContractTests.cs
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
│   │   │   │   ├── ConfigCommandsTests.cs
│   │   │   │   ├── DiagnosticsCommandsTests.cs
│   │   │   │   ├── DryRunCommandTests.cs
│   │   │   │   ├── EtlCommandsTests.cs
│   │   │   │   ├── HelpCommandTests.cs
│   │   │   │   ├── LedgerCliCommandTests.cs
│   │   │   │   ├── PackageCommandsTests.cs
│   │   │   │   ├── SecurityMasterCommandsEdgarTests.cs
│   │   │   │   ├── SelfTestCommandTests.cs
│   │   │   │   ├── SymbolCommandsTests.cs
│   │   │   │   └── ValidateConfigCommandTests.cs
│   │   │   ├── Composition
│   │   │   │   ├── Startup
│   │   │   │   │   └── SharedStartupBootstrapperTests.cs
│   │   │   │   ├── DirectLendingStartupTests.cs
│   │   │   │   ├── PipelineFeatureRegistrationTests.cs
│   │   │   │   ├── ProviderFeatureRegistrationTests.cs
│   │   │   │   ├── SecurityMasterStartupTests.cs
│   │   │   │   └── StorageFeatureRegistrationTests.cs
│   │   │   ├── Config
│   │   │   │   ├── AppSettingsSampleTests.cs
│   │   │   │   ├── ConfigEnvironmentOverrideTests.cs
│   │   │   │   ├── ConfigJsonSchemaGeneratorTests.cs
│   │   │   │   ├── ConfigSchemaIntegrationTests.cs
│   │   │   │   ├── ConfigurationUnificationTests.cs
│   │   │   │   ├── ConfigValidationPipelineTests.cs
│   │   │   │   ├── ConfigValidatorCliTests.cs
│   │   │   │   ├── ConfigValidatorTests.cs
│   │   │   │   ├── ConfigWatcherTests.cs
│   │   │   │   ├── ProviderCredentialResolverTests.cs
│   │   │   │   └── ProviderCredentialStoreTests.cs
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
│   │   │   ├── FundStructure
│   │   │   │   ├── FundAccountTraversalQueryServiceTests.cs
│   │   │   │   ├── LedgerGroupIdTests.cs
│   │   │   │   └── LedgerGroupingRulesTests.cs
│   │   │   ├── Indicators
│   │   │   │   └── TechnicalIndicatorServiceTests.cs
│   │   │   ├── Ledger
│   │   │   │   └── AccountingPolicyServiceTests.cs
│   │   │   ├── Logging
│   │   │   │   └── LoggingSetupTests.cs
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
│   │   │   │   ├── ProviderDegradationCalibrationTests.cs
│   │   │   │   ├── ProviderDegradationScorerTests.cs
│   │   │   │   ├── ProviderLatencyServiceTests.cs
│   │   │   │   ├── QualityTrendCalculationTests.cs
│   │   │   │   ├── SchemaValidationServiceTests.cs
│   │   │   │   ├── SloDefinitionRegistryTests.cs
│   │   │   │   ├── SpreadMonitorTests.cs
│   │   │   │   ├── TickSizeValidatorTests.cs
│   │   │   │   └── TracedEventMetricsTests.cs
│   │   │   ├── Pipeline
│   │   │   │   ├── BackfillProgressTrackerTests.cs
│   │   │   │   ├── BackpressureSignalTests.cs
│   │   │   │   ├── CompositePublisherTests.cs
│   │   │   │   ├── DeadLetterSinkTests.cs
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
│   │   │   │   ├── PersistentDedupLedgerTests.cs
│   │   │   │   ├── SpscRingBufferTests.cs
│   │   │   │   └── WalEventPipelineTests.cs
│   │   │   ├── ProviderRouting
│   │   │   │   ├── BestOfBreedProviderSelectorTests.cs
│   │   │   │   ├── KernelObservabilityServiceTests.cs
│   │   │   │   ├── ProviderRoutingServiceTests.cs
│   │   │   │   └── ProviderTrustScoringServiceTests.cs
│   │   │   ├── SecurityMaster
│   │   │   │   ├── EdgarIngestOrchestratorTests.cs
│   │   │   │   ├── SecurityKindMappingTests.cs
│   │   │   │   ├── SecurityMasterImportServiceTests.cs
│   │   │   │   └── SecurityMasterMappingInteropTests.cs
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
│   │   │   │   ├── FundOperationsWorkspaceReadServiceTests.cs
│   │   │   │   ├── GracefulShutdownTests.cs
│   │   │   │   ├── HistoricalDataQueryServiceBarsTests.cs
│   │   │   │   ├── HistoricalDataQueryServiceTests.cs
│   │   │   │   ├── OperationalSchedulerTests.cs
│   │   │   │   ├── OptionsChainServiceTests.cs
│   │   │   │   ├── PreflightCheckerTests.cs
│   │   │   │   ├── ReportGenerationServiceTests.cs
│   │   │   │   ├── TradingCalendarTests.cs
│   │   │   │   └── VenueMicMapperTests.cs
│   │   │   ├── Ui
│   │   │   │   └── ConfigStoreTests.cs
│   │   │   ├── Wizard
│   │   │   │   └── WizardConfigurationStepTests.cs
│   │   │   ├── DirectLendingServiceTests.cs
│   │   │   ├── GovernanceExceptionServiceTests.cs
│   │   │   ├── ReconciliationGovernanceServiceTests.cs
│   │   │   └── ReconciliationRunServiceTests.cs
│   │   ├── Architecture
│   │   │   └── LayerBoundaryTests.cs
│   │   ├── artifacts
│   │   ├── CertificatesOfDeposit
│   │   │   └── CertificateOfDepositProjectionServiceTests.cs
│   │   ├── Commodities
│   │   │   └── CommodityProjectionServiceTests.cs
│   │   ├── Contracts
│   │   │   ├── Api
│   │   │   │   └── UiApiClientTests.cs
│   │   │   └── FundStructureContractsJsonContextTests.cs
│   │   ├── CryptoCurrency
│   │   │   └── CryptoProjectionServiceTests.cs
│   │   ├── Deposits
│   │   │   └── DepositProjectionServiceTests.cs
│   │   ├── Derivatives
│   │   │   └── SwapProjectionServiceTests.cs
│   │   ├── Domain
│   │   │   ├── Collectors
│   │   │   │   ├── L3OrderBookCollectorTests.cs
│   │   │   │   ├── LiveDataAccessTests.cs
│   │   │   │   ├── MarketDepthCollectorTests.cs
│   │   │   │   ├── OptionDataCollectorTests.cs
│   │   │   │   ├── QuoteCollectorTests.cs
│   │   │   │   ├── SessionStatsCollectorTests.cs
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
│   │   ├── Equity
│   │   │   └── EquityProjectionServiceTests.cs
│   │   ├── Execution
│   │   │   ├── Enhancements
│   │   │   │   ├── AllocationEngineTests.cs
│   │   │   │   ├── DerivativePositionTests.cs
│   │   │   │   ├── EventDrivenDecouplingTests.cs
│   │   │   │   ├── MarginModelTests.cs
│   │   │   │   ├── MultiCurrencyTests.cs
│   │   │   │   └── TaxLotAccountingTests.cs
│   │   │   ├── BrokerageGatewayAdapterTests.cs
│   │   │   ├── ExecutionAuditTrailServiceTests.cs
│   │   │   ├── MultiAccountPaperTradingPortfolioTests.cs
│   │   │   ├── OrderManagementSystemGovernanceTests.cs
│   │   │   ├── OrderManagementSystemTests.cs
│   │   │   ├── PaperSessionPersistenceServiceTests.cs
│   │   │   ├── PaperTradingGatewayTests.cs
│   │   │   ├── PaperTradingPortfolioLotSelectionTests.cs
│   │   │   ├── PaperTradingPortfolioLotSnapshotTests.cs
│   │   │   ├── PaperTradingPortfolioTests.cs
│   │   │   └── PositionLotSelectorTests.cs
│   │   ├── FixedIncome
│   │   │   └── BondProjectionServiceTests.cs
│   │   ├── Futures
│   │   │   └── FutureProjectionServiceTests.cs
│   │   ├── FxSpot
│   │   │   └── FxSpotProjectionServiceTests.cs
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
│   │   │   │   ├── AlpacaHistoricalDataProviderTests.cs
│   │   │   │   ├── AlpacaMessageParsingTests.cs
│   │   │   │   ├── AlpacaQuotePipelineGoldenTests.cs
│   │   │   │   ├── AlpacaQuoteRoutingTests.cs
│   │   │   │   ├── BackfillRetryAfterTests.cs
│   │   │   │   ├── EdgarReferenceDataProviderTests.cs
│   │   │   │   ├── EdgarSymbolSearchProviderTests.cs
│   │   │   │   ├── FailoverAwareMarketDataClientTests.cs
│   │   │   │   ├── FreeHistoricalProviderParsingTests.cs
│   │   │   │   ├── FreeProviderContractTests.cs
│   │   │   │   ├── HistoricalDataProviderContractTests.cs
│   │   │   │   ├── IBApiVersionValidatorTests.cs
│   │   │   │   ├── IBBrokerageGatewayTests.cs
│   │   │   │   ├── IBHistoricalProviderContractTests.cs
│   │   │   │   ├── IBMarketDataClientContractTests.cs
│   │   │   │   ├── IBOrderSampleTests.cs
│   │   │   │   ├── IBRuntimeGuidanceTests.cs
│   │   │   │   ├── IBSimulationClientContractTests.cs
│   │   │   │   ├── IBSimulationClientTests.cs
│   │   │   │   ├── MarketDataClientContractTests.cs
│   │   │   │   ├── NYSECredentialAndRateLimitTests.cs
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
│   │   │   │   ├── RobinhoodReadOnlyBrokerageSyncAdapterTests.cs
│   │   │   │   ├── RobinhoodSymbolSearchProviderTests.cs
│   │   │   │   ├── StreamingFailoverServiceTests.cs
│   │   │   │   ├── SyntheticMarketDataProviderTests.cs
│   │   │   │   ├── SyntheticOptionsChainProviderTests.cs
│   │   │   │   ├── WebSocketProviderBaseTests.cs
│   │   │   │   └── YahooFinanceHistoricalDataProviderTests.cs
│   │   │   ├── Resilience
│   │   │   │   ├── WebSocketConnectionManagerTests.cs
│   │   │   │   └── WebSocketResiliencePolicyTests.cs
│   │   │   └── Shared
│   │   │       ├── SymbolNormalizationTests.cs
│   │   │       └── TempDirectoryFixture.cs
│   │   ├── Integration
│   │   │   ├── EndpointTests
│   │   │   │   ├── AccountPortfolioEndpointTests.cs
│   │   │   │   ├── AdminEndpointPermissionTests.cs
│   │   │   │   ├── AuthEndpointTests.cs
│   │   │   │   ├── BackfillEndpointTests.cs
│   │   │   │   ├── CatalogEndpointTests.cs
│   │   │   │   ├── CheckpointEndpointTests.cs
│   │   │   │   ├── ConfigEndpointTests.cs
│   │   │   │   ├── EndpointIntegrationTestBase.cs
│   │   │   │   ├── EndpointMetadataTests.cs
│   │   │   │   ├── EndpointTestCollection.cs
│   │   │   │   ├── EndpointTestFixture.cs
│   │   │   │   ├── EnvironmentDesignerEndpointTests.cs
│   │   │   │   ├── FailoverEndpointTests.cs
│   │   │   │   ├── FundStructureEndpointTests.cs
│   │   │   │   ├── HealthEndpointTests.cs
│   │   │   │   ├── HistoricalEndpointTests.cs
│   │   │   │   ├── IBEndpointTests.cs
│   │   │   │   ├── LeanEndpointTests.cs
│   │   │   │   ├── LiveDataEndpointTests.cs
│   │   │   │   ├── MaintenanceEndpointTests.cs
│   │   │   │   ├── NegativePathEndpointTests.cs
│   │   │   │   ├── OptionsEndpointTests.cs
│   │   │   │   ├── PilotAcceptanceHarnessTests.cs
│   │   │   │   ├── ProviderEndpointTests.cs
│   │   │   │   ├── QualityDropsEndpointTests.cs
│   │   │   │   ├── QualityEndpointContractTests.cs
│   │   │   │   ├── ResponseSchemaSnapshotTests.cs
│   │   │   │   ├── ResponseSchemaValidationTests.cs
│   │   │   │   ├── RiskEndpointTests.cs
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
│   │   ├── MoneyMarketFunds
│   │   │   └── MoneyMarketFundProjectionServiceTests.cs
│   │   ├── Options
│   │   │   └── OptionProjectionServiceTests.cs
│   │   ├── Performance
│   │   │   └── AllocationBudgetIntegrationTests.cs
│   │   ├── ProviderSdk
│   │   │   ├── AttributeCredentialResolverTests.cs
│   │   │   ├── CredentialValidatorTests.cs
│   │   │   ├── DataSourceAttributeTests.cs
│   │   │   ├── DataSourceRegistryTests.cs
│   │   │   ├── ExceptionTypeTests.cs
│   │   │   └── ProviderModuleLoaderTests.cs
│   │   ├── Reconciliation
│   │   │   ├── ReconciliationCaseServiceTests.cs
│   │   │   └── StatementImportAndMatchingTests.cs
│   │   ├── Risk
│   │   │   ├── CompositeRiskValidatorTests.cs
│   │   │   ├── DrawdownCircuitBreakerTests.cs
│   │   │   ├── OrderRateThrottleTests.cs
│   │   │   ├── PositionLimitRuleTests.cs
│   │   │   └── RiskIntegrationTests.cs
│   │   ├── SecurityMaster
│   │   │   ├── SecurityEnrichmentTests.cs
│   │   │   ├── SecurityMasterAggregateRebuilderTests.cs
│   │   │   ├── SecurityMasterAssetClassSupportTests.cs
│   │   │   ├── SecurityMasterConflictServiceTests.cs
│   │   │   ├── SecurityMasterConvertibleEquityAmendmentTests.cs
│   │   │   ├── SecurityMasterDatabaseFactAttribute.cs
│   │   │   ├── SecurityMasterDatabaseFixture.cs
│   │   │   ├── SecurityMasterImportServiceTests.cs
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
│   │   │   ├── JsonlReplayerTests.cs
│   │   │   ├── LedgerBookServiceTests.cs
│   │   │   ├── LedgerJournalStoreTests.cs
│   │   │   ├── LifecyclePolicyEngineTests.cs
│   │   │   ├── MaintenancePersistenceTests.cs
│   │   │   ├── MemoryMappedJsonlReaderTests.cs
│   │   │   ├── MetadataTagServiceTests.cs
│   │   │   ├── ParquetConversionServiceTests.cs
│   │   │   ├── ParquetStorageSinkTests.cs
│   │   │   ├── PortableDataPackagerTests.cs
│   │   │   ├── PositionSnapshotStoreTests.cs
│   │   │   ├── QuotaEnforcementServiceTests.cs
│   │   │   ├── SourceRegistryPersistenceTests.cs
│   │   │   ├── StorageCatalogServiceTests.cs
│   │   │   ├── StorageChecksumServiceTests.cs
│   │   │   ├── StorageOptionsDefaultsTests.cs
│   │   │   ├── StorageSinkRegistryTests.cs
│   │   │   ├── SymbolRegistryServiceTests.cs
│   │   │   ├── WriteAheadLogCorruptionModeTests.cs
│   │   │   ├── WriteAheadLogFuzzTests.cs
│   │   │   └── WriteAheadLogTests.cs
│   │   ├── Strategies
│   │   │   ├── CoveredCall
│   │   │   │   ├── CoveredCallChainProviderAdapterTests.cs
│   │   │   │   ├── CoveredCallChainProviderFactoryConvertCallsTests.cs
│   │   │   │   └── CoveredCallRunProjectionTests.cs
│   │   │   ├── AggregatePortfolioServiceTests.cs
│   │   │   ├── CashFlowProjectionTests.cs
│   │   │   ├── LedgerReadServiceTests.cs
│   │   │   ├── PortfolioReadServiceTests.cs
│   │   │   ├── PromotionServiceLiveGovernanceTests.cs
│   │   │   ├── PromotionServiceTests.cs
│   │   │   ├── ReconciliationProjectionServiceTests.cs
│   │   │   ├── StrategyDesignRepositoryTests.cs
│   │   │   ├── StrategyDesignServiceTests.cs
│   │   │   ├── StrategyLifecycleManagerTests.cs
│   │   │   ├── StrategyRunContinuityServiceTests.cs
│   │   │   ├── StrategyRunDrillInTests.cs
│   │   │   ├── StrategyRunReadServiceTests.cs
│   │   │   └── StrategyRunStoreTests.cs
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
│   │   │   ├── MarketScenarioBuilder.cs
│   │   │   ├── PolygonStubClient.cs
│   │   │   ├── StubHttpMessageHandler.cs
│   │   │   └── TestMarketEventPublisher.cs
│   │   ├── Treasury
│   │   │   ├── MmfFamilyNormalizationTests.cs
│   │   │   ├── MmfLiquidityServiceTests.cs
│   │   │   ├── MmfRebuildTests.cs
│   │   │   └── MoneyMarketFundServiceTests.cs
│   │   ├── Ui
│   │   │   ├── AlpacaBrokerageConnectionServiceTests.cs
│   │   │   ├── AlpacaCredentialEnvironmentCollection.cs
│   │   │   ├── BondReferenceEndpointsTests.cs
│   │   │   ├── BrokerageConnectionEndpointsTests.cs
│   │   │   ├── BrokeragePortfolioSyncServiceTests.cs
│   │   │   ├── CredentialCompatibilityEndpointsTests.cs
│   │   │   ├── DirectLendingEndpointsTests.cs
│   │   │   ├── EdgarReferenceDataEndpointsTests.cs
│   │   │   ├── EvidenceWorkflowFabricTests.cs
│   │   │   ├── ExecutionGovernanceEndpointsTests.cs
│   │   │   ├── ExecutionRouteContractParityTests.cs
│   │   │   ├── ExecutionWriteEndpointsTests.cs
│   │   │   ├── ExportEndpointsTests.cs
│   │   │   ├── OptionReferenceEndpointsRoundtripTests.cs
│   │   │   ├── ProviderConnectionEndpointsTests.cs
│   │   │   ├── RiskEndpointsTests.cs
│   │   │   ├── SecurityMasterConvertibleEquityEndpointsTests.cs
│   │   │   ├── SecurityMasterIngestStatusEndpointsTests.cs
│   │   │   ├── SecurityMasterPreferredEquityEndpointsTests.cs
│   │   │   ├── StrategyDesignerWorkstationEndpointsTests.cs
│   │   │   ├── TradingOperatorReadinessServiceTests.cs
│   │   │   ├── Wave2OperatorInboxAcceptanceTests.cs
│   │   │   ├── Wave2PaperTradingCockpitAcceptanceTests.cs
│   │   │   ├── WorkflowLibraryEndpointTests.cs
│   │   │   ├── WorkstationEndpointsTests.cs
│   │   │   └── WorkstationServiceCollectionExtensionsTests.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.Tests.csproj
│   │   ├── StatementReconciliationServiceTests.cs
│   │   └── TestCollections.cs
│   ├── Meridian.Ui.Tests
│   │   ├── Collections
│   │   │   ├── BoundedObservableCollectionTests.cs
│   │   │   └── CircularBufferTests.cs
│   │   ├── Services
│   │   │   ├── TestSupport
│   │   │   │   └── FixedConfigService.cs
│   │   │   ├── ActivityFeedServiceTests.cs
│   │   │   ├── AlertServiceTests.cs
│   │   │   ├── AnalysisExportServiceBaseTests.cs
│   │   │   ├── AnalysisExportWizardServiceTests.cs
│   │   │   ├── ApiClientServiceTests.cs
│   │   │   ├── ArchiveBrowserServiceTests.cs
│   │   │   ├── AtomicPersistenceServiceTests.cs
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
│   │   ├── Copy
│   │   │   └── WorkspaceCopyCatalogTests.cs
│   │   ├── Features
│   │   │   └── Data
│   │   │       └── Shell
│   │   │           └── DataWorkspaceShellViewModelTests.cs
│   │   ├── Models
│   │   │   └── ShellNavigationCatalogTests.cs
│   │   ├── Services
│   │   │   ├── AdminMaintenanceServiceTests.cs
│   │   │   ├── AppServiceRegistrationTests.cs
│   │   │   ├── BackendServiceManagerTests.cs
│   │   │   ├── BackgroundTaskSchedulerServiceTests.cs
│   │   │   ├── ConfigServiceTests.cs
│   │   │   ├── ConnectionServiceTests.cs
│   │   │   ├── DataOperationsWorkspacePresentationBuilderTests.cs
│   │   │   ├── ExportPresetServiceTests.cs
│   │   │   ├── FirstRunServiceTests.cs
│   │   │   ├── FundLedgerReadServiceTests.cs
│   │   │   ├── FundReconciliationWorkbenchServiceTests.cs
│   │   │   ├── InfoBarServiceTests.cs
│   │   │   ├── KeyboardShortcutServiceTests.cs
│   │   │   ├── MessagingServiceTests.cs
│   │   │   ├── NavigationServiceTests.cs
│   │   │   ├── NotificationServiceTests.cs
│   │   │   ├── OfflineTrackingPersistenceServiceTests.cs
│   │   │   ├── PendingOperationsQueueServiceTests.cs
│   │   │   ├── QuantScriptExecutionHistoryServiceTests.cs
│   │   │   ├── QuantScriptTemplateCatalogServiceTests.cs
│   │   │   ├── ResearchBriefingWorkspaceServiceTests.cs
│   │   │   ├── RetentionAssuranceServiceTests.cs
│   │   │   ├── RunMatServiceTests.cs
│   │   │   ├── SingleInstanceServiceTests.cs
│   │   │   ├── StatusServiceTests.cs
│   │   │   ├── StorageServiceTests.cs
│   │   │   ├── StrategyRunWorkspaceServiceTests.cs
│   │   │   ├── TooltipServiceTests.cs
│   │   │   ├── WatchlistServiceTests.cs
│   │   │   ├── WorkspaceServiceTests.cs
│   │   │   ├── WorkspaceShellContextServiceTests.cs
│   │   │   ├── WorkstationOperatingContextServiceTests.cs
│   │   │   └── WorkstationWorkflowSummaryServiceTests.cs
│   │   ├── Shell
│   │   │   ├── PageContentFactoryTests.cs
│   │   │   ├── PaneHostViewModelTests.cs
│   │   │   ├── ShellNavigationCoordinatorTests.cs
│   │   │   └── ShellRouteRegistryTests.cs
│   │   ├── Support
│   │   │   ├── FakeQuantScriptCompiler.cs
│   │   │   ├── FakeScriptRunner.cs
│   │   │   ├── FakeWorkstationReconciliationApiClient.cs
│   │   │   ├── FakeWorkstationResearchBriefingApiClient.cs
│   │   │   ├── MainPageUiAutomationFacade.cs
│   │   │   ├── NavigationHostInspector.cs
│   │   │   ├── RunMatUiAutomationFacade.cs
│   │   │   ├── RunMatUiAutomationFacadeTests.cs
│   │   │   ├── StrategyRunWorkspaceTestData.cs
│   │   │   └── WpfTestThread.cs
│   │   ├── ViewModels
│   │   │   ├── AccountPortfolioViewModelTests.cs
│   │   │   ├── ActivityLogViewModelTests.cs
│   │   │   ├── AddProviderWizardViewModelTests.cs
│   │   │   ├── AdminMaintenanceViewModelTests.cs
│   │   │   ├── AdvancedAnalyticsViewModelTests.cs
│   │   │   ├── AgentViewModelTests.cs
│   │   │   ├── AggregatePortfolioViewModelTests.cs
│   │   │   ├── AnalysisExportViewModelTests.cs
│   │   │   ├── AnalysisExportWizardViewModelTests.cs
│   │   │   ├── BackfillViewModelTests.cs
│   │   │   ├── BatchBacktestViewModelTests.cs
│   │   │   ├── CashFlowViewModelTests.cs
│   │   │   ├── ChartingPageViewModelTests.cs
│   │   │   ├── CollectionSessionViewModelTests.cs
│   │   │   ├── DataBrowserViewModelTests.cs
│   │   │   ├── DataExportViewModelTests.cs
│   │   │   ├── DataQualityViewModelCharacterizationTests.cs
│   │   │   ├── DataSamplingViewModelTests.cs
│   │   │   ├── DataSourcesViewModelTests.cs
│   │   │   ├── ExportPresetsViewModelTests.cs
│   │   │   ├── FundAccountsViewModelTests.cs
│   │   │   ├── FundLedgerViewModelTests.cs
│   │   │   ├── MainShellViewModelTests.cs
│   │   │   ├── MessagingHubViewModelTests.cs
│   │   │   ├── NotificationCenterViewModelTests.cs
│   │   │   ├── OrderBookViewModelTests.cs
│   │   │   ├── PortfolioImportViewModelTests.cs
│   │   │   ├── PositionBlotterViewModelTests.cs
│   │   │   ├── ProviderHealthViewModelTests.cs
│   │   │   ├── QuantScriptViewModelTests.cs
│   │   │   ├── ResearchWorkspaceShellViewModelTests.cs
│   │   │   ├── RetentionAssuranceViewModelTests.cs
│   │   │   ├── RunMatViewModelTests.cs
│   │   │   ├── ScheduleManagerViewModelTests.cs
│   │   │   ├── SecurityMasterViewModelTests.cs
│   │   │   ├── ShellPresentationViewModelTests.cs
│   │   │   ├── StatusBarViewModelTests.cs
│   │   │   ├── StorageViewModelTests.cs
│   │   │   ├── StrategyRunBrowserViewModelTests.cs
│   │   │   ├── StrategyRunLedgerViewModelTests.cs
│   │   │   ├── StrategyRunPortfolioViewModelTests.cs
│   │   │   ├── SymbolMappingViewModelTests.cs
│   │   │   ├── SymbolsPageViewModelTests.cs
│   │   │   ├── SystemHealthViewModelTests.cs
│   │   │   ├── TimeSeriesAlignmentViewModelTests.cs
│   │   │   ├── TradingHoursViewModelTests.cs
│   │   │   ├── TradingWorkspaceShellViewModelTests.cs
│   │   │   ├── WatchlistViewModelTests.cs
│   │   │   ├── WelcomePageViewModelTests.cs
│   │   │   ├── WorkflowLibraryViewModelTests.cs
│   │   │   └── WorkspacePageViewModelTests.cs
│   │   ├── Views
│   │   │   ├── DashboardPageSmokeTests.cs
│   │   │   ├── DataOperationsWorkspaceShellSmokeTests.cs
│   │   │   ├── DataQualityPageSmokeTests.cs
│   │   │   ├── DesktopWorkflowScriptTests.cs
│   │   │   ├── FullNavigationSweepTests.cs
│   │   │   ├── FundProfileSelectionPageSmokeTests.cs
│   │   │   ├── GovernanceWorkspaceShellPageTests.cs
│   │   │   ├── GovernanceWorkspaceShellSmokeTests.cs
│   │   │   ├── MainPageSmokeTests.cs
│   │   │   ├── MainPageUiWorkflowTests.cs
│   │   │   ├── NavigationPageSmokeTests.cs
│   │   │   ├── PageLifecycleCleanupTests.cs
│   │   │   ├── PlotRenderBehaviorTests.cs
│   │   │   ├── QuantScriptPageTests.cs
│   │   │   ├── ResearchWorkspaceShellPageTests.cs
│   │   │   ├── ResearchWorkspaceShellSmokeTests.cs
│   │   │   ├── ResearchWorkspaceShellWorkflowTests.cs
│   │   │   ├── RunMatUiSmokeTests.cs
│   │   │   ├── RunMatWorkflowSmokeTests.cs
│   │   │   ├── SplitPaneHostControlTests.cs
│   │   │   ├── SystemHealthPageSmokeTests.cs
│   │   │   ├── TradingWorkspaceShellPageTests.cs
│   │   │   ├── WorkspaceDeepPageChromeTests.cs
│   │   │   ├── WorkspaceQueueToneStylesTests.cs
│   │   │   ├── WorkspaceShellContextStripControlTests.cs
│   │   │   ├── WorkspaceShellPageSmokeTests.cs
│   │   │   └── WorkstationPageSmokeTests.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.Wpf.Tests.csproj
│   │   └── TestAssemblyConfiguration.cs
│   ├── scripts
│   │   ├── __pycache__
│   │   │   ├── test_central_package_versions.cpython-311.pyc
│   │   │   ├── test_golden_path_validation_workflow.cpython-311.pyc
│   │   │   └── test_web_workstation_installer.cpython-311.pyc
│   │   ├── setup-verification.sh
│   │   ├── test_artifact_retention_module.py
│   │   ├── test_buildctl_artifact_retention.py
│   │   ├── test_central_package_versions.py
│   │   ├── test_check_contract_compatibility_gate.py
│   │   ├── test_check_program_state_consistency.py
│   │   ├── test_check_status_delivery_claims.py
│   │   ├── test_check_workflow_docs_parity.py
│   │   ├── test_cleanup_generated_script.py
│   │   ├── test_code_quality_workflow.py
│   │   ├── test_compare_run_contract.py
│   │   ├── test_dashboard_package_lock.py
│   │   ├── test_documentation_workflow.py
│   │   ├── test_export_project_artifact_workflow.py
│   │   ├── test_generate_contract_review_packet.py
│   │   ├── test_generate_dk1_pilot_parity_packet.py
│   │   ├── test_generate_program_state_summary.py
│   │   ├── test_golden_path_validation_workflow.py
│   │   ├── test_live_execution_controls_route_consistency.py
│   │   ├── test_maintenance_full_workflow.py
│   │   ├── test_meridian_code_review_run_eval.py
│   │   ├── test_prepare_dk1_operator_signoff.py
│   │   ├── test_project_target_framework_alignment.py
│   │   ├── test_python_package_conda_dependencies.py
│   │   ├── test_refresh_screenshots_workflow.py
│   │   ├── test_screenshot_diff_report.py
│   │   ├── test_screenshot_workflow_plan.py
│   │   ├── test_setup_dotnet_cache_action.py
│   │   ├── test_shared_build_retention.py
│   │   ├── test_web_workstation_installer.py
│   │   ├── test_wpf_msix_install_guidance.py
│   │   └── test_wpf_msix_manifest.py
│   ├── coverlet.runsettings
│   ├── Directory.Build.props
│   ├── setup-script-tests.md
│   └── xunit.runner.json
├── wwwroot
│   └── workstation
│       ├── assets
│       │   ├── activity-CtHpOVKp.js
│       │   ├── activity-DdzC2NHl.js
│       │   ├── arrow-right-BQMXU4Qj.js
│       │   ├── arrow-right-DVsZxtAC.js
│       │   ├── briefcase-business-CD4M7um3.js
│       │   ├── briefcase-business-TKfvxiVx.js
│       │   ├── circle-alert-D7rSODdk.js
│       │   ├── circle-alert-HHMNSH16.js
│       │   ├── circle-check-DPF4ogK2.js
│       │   ├── circle-check-eX-lGLCP.js
│       │   ├── circle-x-CMg0Qc3V.js
│       │   ├── circle-x-DY2RQmFF.js
│       │   ├── clipboard-list-DAEDYX46.js
│       │   ├── clipboard-list-jTN-MPwF.js
│       │   ├── data-operations-screen-B3QBwTVq.js
│       │   ├── data-operations-screen-D0lJrkiA.js
│       │   ├── dialog-CinaMm0v.js
│       │   ├── dialog-DSdabj2S.js
│       │   ├── evidence-workbench-screen-ClQx7gzd.js
│       │   ├── evidence-workbench-screen-eYN2-zbn.js
│       │   ├── external-link-BZXdW9W9.js
│       │   ├── external-link-Grqr3LkX.js
│       │   ├── file-text-BlxlUqHd.js
│       │   ├── file-text-N-hievbj.js
│       │   ├── governance-screen-DqY6ddBm.js
│       │   ├── governance-screen-DrFPMbFb.js
│       │   ├── index-aS_m2ekp.css
│       │   ├── index-C40I5Ro9.js
│       │   ├── index-Ch7FNJl9.js
│       │   ├── index-DRTDTLoU.css
│       │   ├── input-Cy4wm2Ml.js
│       │   ├── input-DH1caSqT.js
│       │   ├── live-quotes-screen--RkhoaOE.js
│       │   ├── live-quotes-screen-D1hu0dqT.js
│       │   ├── metric-card-6FGqr-9F.js
│       │   ├── metric-card-BbZrlC_6.js
│       │   ├── network-1U1oL-WT.js
│       │   ├── network-CwsNwANB.js
│       │   ├── operator-readiness-console-BrBqInOx.js
│       │   ├── operator-readiness-console-BRtzgXL5.js
│       │   ├── overview-screen-BeJxfrwP.js
│       │   ├── overview-screen-BR5AraLj.js
│       │   ├── plus-Cw2iQHnA.js
│       │   ├── plus-DSjajpe-.js
│       │   ├── portfolio-screen-CSEy6zr5.js
│       │   ├── portfolio-screen-mIlILUNM.js
│       │   ├── price-alerts-screen-COXY9m6l.js
│       │   ├── price-alerts-screen-njljCYiX.js
│       │   ├── quant-lab-screen-BOhDQUug.js
│       │   ├── quant-lab-screen-Dpy7KWIY.js
│       │   ├── refresh-cw-DbBLAiPQ.js
│       │   ├── refresh-cw-Dp5Clkb9.js
│       │   ├── reporting-screen-BliDtXaE.js
│       │   ├── reporting-screen-nAqRD7ij.js
│       │   ├── research-screen-Df8dFRgK.js
│       │   ├── research-screen-DwSDHbyl.js
│       │   ├── rotate-ccw-9wKbnWAt.js
│       │   ├── rotate-ccw-DOv9SmtZ.js
│       │   ├── select-CoGQYu8S.js
│       │   ├── select-Dydh4qce.js
│       │   ├── settings-screen-DpB0U5fm.js
│       │   ├── settings-screen-paK4sHDZ.js
│       │   ├── sparkles-C72l2Ug0.js
│       │   ├── sparkles-CPzvLarX.js
│       │   ├── strategy-designer-screen-DDl7I7Ey.js
│       │   ├── trading-screen-CEO2TrpK.js
│       │   ├── trading-screen-DJVhwdT7.js
│       │   ├── trash-2-Br6Kw89A.js
│       │   ├── trash-2-DG_-jE3C.js
│       │   ├── trending-up-CyiwhFqC.js
│       │   ├── trending-up-Dm0UHjUd.js
│       │   ├── ui-kit-primitives-1LSkXU-q.js
│       │   ├── ui-kit-primitives-HLPUYoNX.js
│       │   ├── wallet-gTxB9uZO.js
│       │   ├── wallet-SSwWmFz-.js
│       │   ├── watchlist-screen-lIteaLqr.js
│       │   └── watchlist-screen-Z-X12prT.js
│       └── index.html
├── .editorconfig
├── .flake8
├── .gitattributes
├── .gitignore
├── .gitleaks.toml
├── .globalconfig
├── .markdownlint.json
├── .vsconfig
├── AGENTS.md
├── CLAUDE.md
├── Directory.Build.props
├── Directory.Packages.props
├── docfx.json
├── environment.yml
├── global.json
├── LICENSE
├── Makefile
├── Meridian.sln
├── Meridian.WebWorkstation.slnf
├── NuGet.Config
├── package-lock.json
├── package.json
└── README.md
```
