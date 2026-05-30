# Repository Structure

> Auto-generated on 2026-05-30 04:11:10 UTC. Do not edit manually.

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
│       ├── meridian-browser-workstation
│       │   ├── agents
│       │   │   └── openai.yaml
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
│   │   ├── meridian-browser-workstation
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
│   ├── checklists
│   │   ├── desktop-workspace-definition-of-done.md
│   │   ├── modularity-checklist.md
│   │   ├── mvvm-checklist.md
│   │   ├── resource-management-checklist.md
│   │   └── safe-refactor-checklist.md
│   ├── environments
│   │   ├── environment.toml
│   │   └── README.md
│   ├── prompts
│   │   ├── add-shared-workstation-control.md
│   │   ├── audit-timeline.md
│   │   ├── diagnostics-panel.md
│   │   ├── generate-viewmodel-tests.md
│   │   ├── implement-desktop-workspace.md
│   │   ├── mvvm-compliance-review.md
│   │   ├── optimize-resource-usage.md
│   │   ├── provider-workflow-feature.md
│   │   ├── refactor-screen-shared-components.md
│   │   └── research-data-acquisition-feature.md
│   ├── skills
│   │   ├── _shared
│   │   │   ├── codex-execution-contract.md
│   │   │   └── project-context.md
│   │   ├── dense-data-grid-inspector-panel
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   └── SKILL.md
│   │   ├── desktop-test-generation
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   └── SKILL.md
│   │   ├── diagnostics-audit-timeline
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   └── SKILL.md
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
│   │   ├── meridian-browser-workstation
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
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
│   │   ├── meridian-docs
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
│   │   ├── modular-desktop-mvvm
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   └── SKILL.md
│   │   ├── performance-resource-review
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   └── SKILL.md
│   │   ├── provider-management-workflow
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   └── SKILL.md
│   │   ├── research-data-acquisition
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   └── SKILL.md
│   │   ├── safe-refactoring
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   └── SKILL.md
│   │   ├── shared-component-extraction
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   └── SKILL.md
│   │   ├── workstation-screen-composition
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   └── SKILL.md
│   │   └── README.md
│   ├── validation
│   │   └── w4-close-gate
│   ├── AGENTS.md
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
│   │   ├── source-documentation.instructions.md
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
│   │   ├── operations-continuity-core.prompt.yml
│   │   ├── optimize-performance.prompt.yml
│   │   ├── project-context.prompt.yml
│   │   ├── provider-implementation-guide.prompt.yml
│   │   ├── README.md
│   │   ├── runtime-observability-diagnostics.prompt.yml
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
│   │   ├── codeql.yml
│   │   ├── desktop-installer-packaging.yml
│   │   ├── desktop-screenshot-capture.yml
│   │   ├── desktop-standalone-publish.yml
│   │   ├── desktop-user-manual.yml
│   │   ├── desktop-workflow-runner.yml
│   │   ├── golden-path-validation.yml
│   │   ├── ibapi-smoke.yml
│   │   ├── maintenance.yml
│   │   ├── provider-validation.yml
│   │   ├── publish-smoke.yml
│   │   ├── README.md
│   │   ├── roadmap-source-docs.yml
│   │   ├── roadmap-tools-manual.yml
│   │   ├── robinhood-options-smoke.yml
│   │   ├── web-screenshot-capture.yml
│   │   ├── windows-desktop-build.yml
│   │   ├── wpf-dev-validation.yml
│   │   └── wpf-route-validation.yml
│   ├── copilot-instructions.md
│   ├── dependabot.yml
│   ├── labeler.yml
│   ├── labels.yml
│   ├── markdown-link-check-config.json
│   ├── PULL_REQUEST_TEMPLATE.md
│   ├── pull_request_template_desktop.md
│   └── spellcheck-config.yml
├── .nuget
│   └── packages
│       ├── apache.arrow
│       │   └── 22.1.0
│       │       ├── lib
│       │       │   ├── net462
│       │       │   │   └── Apache.Arrow.dll
│       │       │   ├── net6.0
│       │       │   │   └── Apache.Arrow.dll
│       │       │   ├── net8.0
│       │       │   │   └── Apache.Arrow.dll
│       │       │   └── netstandard2.0
│       │       │       └── Apache.Arrow.dll
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── apache.arrow.22.1.0.nupkg
│       │       ├── apache.arrow.22.1.0.nupkg.sha512
│       │       ├── apache.arrow.nuspec
│       │       ├── LICENSE.txt
│       │       └── logo_asf.png
│       ├── azure.core
│       │   ├── 1.38.0
│       │   │   ├── lib
│       │   │   │   ├── net461
│       │   │   │   │   ├── Azure.Core.dll
│       │   │   │   │   └── Azure.Core.xml
│       │   │   │   ├── net472
│       │   │   │   │   ├── Azure.Core.dll
│       │   │   │   │   └── Azure.Core.xml
│       │   │   │   ├── net6.0
│       │   │   │   │   ├── Azure.Core.dll
│       │   │   │   │   └── Azure.Core.xml
│       │   │   │   └── netstandard2.0
│       │   │   │       ├── Azure.Core.dll
│       │   │   │       └── Azure.Core.xml
│       │   │   ├── .nupkg.metadata
│       │   │   ├── .signature.p7s
│       │   │   ├── azure.core.1.38.0.nupkg
│       │   │   ├── azure.core.1.38.0.nupkg.sha512
│       │   │   ├── azure.core.nuspec
│       │   │   ├── azureicon.png
│       │   │   ├── CHANGELOG.md
│       │   │   └── README.md
│       │   └── 1.46.2
│       │       ├── lib
│       │       │   ├── net462
│       │       │   │   ├── Azure.Core.dll
│       │       │   │   └── Azure.Core.xml
│       │       │   ├── net472
│       │       │   │   ├── Azure.Core.dll
│       │       │   │   └── Azure.Core.xml
│       │       │   ├── net6.0
│       │       │   │   ├── Azure.Core.dll
│       │       │   │   └── Azure.Core.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Azure.Core.dll
│       │       │   │   └── Azure.Core.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Azure.Core.dll
│       │       │       └── Azure.Core.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── azure.core.1.46.2.nupkg
│       │       ├── azure.core.1.46.2.nupkg.sha512
│       │       ├── azure.core.nuspec
│       │       ├── azureicon.png
│       │       ├── CHANGELOG.md
│       │       └── README.md
│       ├── azure.core.amqp
│       │   └── 1.3.1
│       │       ├── lib
│       │       │   └── netstandard2.0
│       │       │       ├── Azure.Core.Amqp.dll
│       │       │       └── Azure.Core.Amqp.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── azure.core.amqp.1.3.1.nupkg
│       │       ├── azure.core.amqp.1.3.1.nupkg.sha512
│       │       ├── azure.core.amqp.nuspec
│       │       ├── azureicon.png
│       │       ├── CHANGELOG.md
│       │       └── README.md
│       ├── azure.identity
│       │   └── 1.11.4
│       │       ├── lib
│       │       │   └── netstandard2.0
│       │       │       ├── Azure.Identity.dll
│       │       │       └── Azure.Identity.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── azure.identity.1.11.4.nupkg
│       │       ├── azure.identity.1.11.4.nupkg.sha512
│       │       ├── azure.identity.nuspec
│       │       ├── azureicon.png
│       │       ├── CHANGELOG.md
│       │       └── README.md
│       ├── azure.messaging.servicebus
│       │   └── 7.20.1
│       │       ├── lib
│       │       │   ├── net8.0
│       │       │   │   ├── Azure.Messaging.ServiceBus.dll
│       │       │   │   └── Azure.Messaging.ServiceBus.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Azure.Messaging.ServiceBus.dll
│       │       │       └── Azure.Messaging.ServiceBus.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── azure.messaging.servicebus.7.20.1.nupkg
│       │       ├── azure.messaging.servicebus.7.20.1.nupkg.sha512
│       │       ├── azure.messaging.servicebus.nuspec
│       │       ├── azureicon.png
│       │       ├── CHANGELOG.md
│       │       └── README.md
│       ├── communitytoolkit.highperformance
│       │   └── 8.4.0
│       │       ├── lib
│       │       │   ├── net7.0
│       │       │   │   ├── CommunityToolkit.HighPerformance.dll
│       │       │   │   ├── CommunityToolkit.HighPerformance.pdb
│       │       │   │   └── CommunityToolkit.HighPerformance.xml
│       │       │   ├── net8.0
│       │       │   │   ├── CommunityToolkit.HighPerformance.dll
│       │       │   │   ├── CommunityToolkit.HighPerformance.pdb
│       │       │   │   └── CommunityToolkit.HighPerformance.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── CommunityToolkit.HighPerformance.dll
│       │       │   │   ├── CommunityToolkit.HighPerformance.pdb
│       │       │   │   └── CommunityToolkit.HighPerformance.xml
│       │       │   └── netstandard2.1
│       │       │       ├── CommunityToolkit.HighPerformance.dll
│       │       │       ├── CommunityToolkit.HighPerformance.pdb
│       │       │       └── CommunityToolkit.HighPerformance.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── communitytoolkit.highperformance.8.4.0.nupkg
│       │       ├── communitytoolkit.highperformance.8.4.0.nupkg.sha512
│       │       ├── communitytoolkit.highperformance.nuspec
│       │       ├── Icon.png
│       │       ├── License.md
│       │       └── ThirdPartyNotices.txt
│       ├── expecto
│       │   ├── 10.2.2
│       │   │   ├── lib
│       │   │   │   └── net6.0
│       │   │   │       ├── Expecto.dll
│       │   │   │       ├── Expecto.pdb
│       │   │   │       └── Expecto.xml
│       │   │   ├── .nupkg.metadata
│       │   │   ├── .signature.p7s
│       │   │   ├── expecto-logo-small.png
│       │   │   ├── expecto.10.2.2.nupkg
│       │   │   ├── expecto.10.2.2.nupkg.sha512
│       │   │   └── expecto.nuspec
│       │   └── 10.2.3
│       │       ├── lib
│       │       │   └── net6.0
│       │       │       ├── Expecto.dll
│       │       │       ├── Expecto.pdb
│       │       │       └── Expecto.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── expecto-logo-small.png
│       │       ├── expecto.10.2.3.nupkg
│       │       ├── expecto.10.2.3.nupkg.sha512
│       │       └── expecto.nuspec
│       ├── expecto.fscheck
│       │   └── 10.2.3
│       │       ├── lib
│       │       │   └── net6.0
│       │       │       ├── Expecto.FsCheck.dll
│       │       │       ├── Expecto.FsCheck.pdb
│       │       │       └── Expecto.FsCheck.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── expecto-logo-small.png
│       │       ├── expecto.fscheck.10.2.3.nupkg
│       │       ├── expecto.fscheck.10.2.3.nupkg.sha512
│       │       └── expecto.fscheck.nuspec
│       ├── farmer
│       │   └── 1.9.26
│       │       ├── lib
│       │       │   └── netstandard2.0
│       │       │       ├── Farmer.dll
│       │       │       └── Farmer.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── farmer.1.9.26.nupkg
│       │       ├── farmer.1.9.26.nupkg.sha512
│       │       ├── farmer.nuspec
│       │       ├── Icon.jpg
│       │       ├── LICENSE
│       │       └── readme.md
│       ├── fluentvalidation
│       │   └── 12.1.1
│       │       ├── lib
│       │       │   └── net8.0
│       │       │       ├── FluentValidation.dll
│       │       │       └── FluentValidation.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── fluent-validation-icon.png
│       │       ├── fluentvalidation.12.1.1.nupkg
│       │       ├── fluentvalidation.12.1.1.nupkg.sha512
│       │       ├── fluentvalidation.nuspec
│       │       └── README.md
│       ├── fscheck
│       │   ├── 2.16.5
│       │   │   ├── lib
│       │   │   │   ├── net452
│       │   │   │   │   ├── FsCheck.dll
│       │   │   │   │   └── FsCheck.xml
│       │   │   │   ├── netstandard1.0
│       │   │   │   │   ├── FsCheck.dll
│       │   │   │   │   └── FsCheck.xml
│       │   │   │   ├── netstandard1.6
│       │   │   │   │   ├── FsCheck.dll
│       │   │   │   │   └── FsCheck.xml
│       │   │   │   └── netstandard2.0
│       │   │   │       ├── FsCheck.dll
│       │   │   │       └── FsCheck.xml
│       │   │   ├── .nupkg.metadata
│       │   │   ├── .signature.p7s
│       │   │   ├── fscheck.2.16.5.nupkg
│       │   │   ├── fscheck.2.16.5.nupkg.sha512
│       │   │   └── fscheck.nuspec
│       │   └── 3.3.2
│       │       ├── lib
│       │       │   └── netstandard2.0
│       │       │       ├── FsCheck.dll
│       │       │       └── FsCheck.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── fscheck.3.3.2.nupkg
│       │       ├── fscheck.3.3.2.nupkg.sha512
│       │       ├── fscheck.nuspec
│       │       └── logo.png
│       ├── fsharp.control.asyncseq
│       │   └── 4.10.0
│       │       ├── fable
│       │       │   ├── AsyncSeq.fs
│       │       │   ├── AsyncSeq.fsi
│       │       │   └── FSharp.Control.AsyncSeq.fsproj
│       │       ├── lib
│       │       │   ├── netstandard2.0
│       │       │   │   ├── FSharp.Control.AsyncSeq.dll
│       │       │   │   └── FSharp.Control.AsyncSeq.xml
│       │       │   └── netstandard2.1
│       │       │       ├── FSharp.Control.AsyncSeq.dll
│       │       │       └── FSharp.Control.AsyncSeq.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── fsharp.control.asyncseq.4.10.0.nupkg
│       │       ├── fsharp.control.asyncseq.4.10.0.nupkg.sha512
│       │       └── fsharp.control.asyncseq.nuspec
│       ├── fsharp.core
│       │   └── 11.0.100
│       │       ├── lib
│       │       │   ├── netstandard2.0
│       │       │   │   ├── cs
│       │       │   │   │   └── FSharp.Core.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── FSharp.Core.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── FSharp.Core.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── FSharp.Core.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── FSharp.Core.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── FSharp.Core.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── FSharp.Core.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── FSharp.Core.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── FSharp.Core.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── FSharp.Core.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── FSharp.Core.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── FSharp.Core.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── FSharp.Core.resources.dll
│       │       │   │   ├── FSharp.Core.dll
│       │       │   │   └── FSharp.Core.xml
│       │       │   └── netstandard2.1
│       │       │       ├── cs
│       │       │       │   └── FSharp.Core.resources.dll
│       │       │       ├── de
│       │       │       │   └── FSharp.Core.resources.dll
│       │       │       ├── es
│       │       │       │   └── FSharp.Core.resources.dll
│       │       │       ├── fr
│       │       │       │   └── FSharp.Core.resources.dll
│       │       │       ├── it
│       │       │       │   └── FSharp.Core.resources.dll
│       │       │       ├── ja
│       │       │       │   └── FSharp.Core.resources.dll
│       │       │       ├── ko
│       │       │       │   └── FSharp.Core.resources.dll
│       │       │       ├── pl
│       │       │       │   └── FSharp.Core.resources.dll
│       │       │       ├── pt-BR
│       │       │       │   └── FSharp.Core.resources.dll
│       │       │       ├── ru
│       │       │       │   └── FSharp.Core.resources.dll
│       │       │       ├── tr
│       │       │       │   └── FSharp.Core.resources.dll
│       │       │       ├── zh-Hans
│       │       │       │   └── FSharp.Core.resources.dll
│       │       │       ├── zh-Hant
│       │       │       │   └── FSharp.Core.resources.dll
│       │       │       ├── FSharp.Core.dll
│       │       │       └── FSharp.Core.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── fsharp.core.11.0.100.nupkg
│       │       ├── fsharp.core.11.0.100.nupkg.sha512
│       │       ├── fsharp.core.nuspec
│       │       └── Icon.png
│       ├── fsharp.data
│       │   └── 8.1.2
│       │       ├── lib
│       │       │   ├── net8.0
│       │       │   │   ├── FSharp.Data.dll
│       │       │   │   └── FSharp.Data.xml
│       │       │   └── netstandard2.0
│       │       │       ├── FSharp.Data.dll
│       │       │       └── FSharp.Data.xml
│       │       ├── typeproviders
│       │       │   └── fsharp41
│       │       │       ├── net8.0
│       │       │       │   ├── FSharp.Core.xml
│       │       │       │   ├── FSharp.Data.Csv.Core.dll
│       │       │       │   ├── FSharp.Data.Csv.Core.xml
│       │       │       │   ├── FSharp.Data.DesignTime.deps.json
│       │       │       │   ├── FSharp.Data.DesignTime.dll
│       │       │       │   ├── FSharp.Data.Html.Core.dll
│       │       │       │   ├── FSharp.Data.Html.Core.xml
│       │       │       │   ├── FSharp.Data.Http.dll
│       │       │       │   ├── FSharp.Data.Http.xml
│       │       │       │   ├── FSharp.Data.Json.Core.dll
│       │       │       │   ├── FSharp.Data.Json.Core.xml
│       │       │       │   ├── FSharp.Data.Runtime.Utilities.dll
│       │       │       │   ├── FSharp.Data.Runtime.Utilities.xml
│       │       │       │   ├── FSharp.Data.WorldBank.Core.dll
│       │       │       │   ├── FSharp.Data.WorldBank.Core.xml
│       │       │       │   ├── FSharp.Data.Xml.Core.dll
│       │       │       │   └── FSharp.Data.Xml.Core.xml
│       │       │       └── netstandard2.0
│       │       │           ├── FSharp.Core.xml
│       │       │           ├── FSharp.Data.Csv.Core.dll
│       │       │           ├── FSharp.Data.Csv.Core.xml
│       │       │           ├── FSharp.Data.DesignTime.deps.json
│       │       │           ├── FSharp.Data.DesignTime.dll
│       │       │           ├── FSharp.Data.Html.Core.dll
│       │       │           ├── FSharp.Data.Html.Core.xml
│       │       │           ├── FSharp.Data.Http.dll
│       │       │           ├── FSharp.Data.Http.xml
│       │       │           ├── FSharp.Data.Json.Core.dll
│       │       │           ├── FSharp.Data.Json.Core.xml
│       │       │           ├── FSharp.Data.Runtime.Utilities.dll
│       │       │           ├── FSharp.Data.Runtime.Utilities.xml
│       │       │           ├── FSharp.Data.WorldBank.Core.dll
│       │       │           ├── FSharp.Data.WorldBank.Core.xml
│       │       │           ├── FSharp.Data.Xml.Core.dll
│       │       │           └── FSharp.Data.Xml.Core.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── fsharp.data.8.1.2.nupkg
│       │       ├── fsharp.data.8.1.2.nupkg.sha512
│       │       ├── fsharp.data.nuspec
│       │       └── logo.png
│       ├── fsharp.data.csv.core
│       │   └── 8.1.2
│       │       ├── lib
│       │       │   ├── net8.0
│       │       │   │   ├── FSharp.Data.Csv.Core.dll
│       │       │   │   └── FSharp.Data.Csv.Core.xml
│       │       │   └── netstandard2.0
│       │       │       ├── FSharp.Data.Csv.Core.dll
│       │       │       └── FSharp.Data.Csv.Core.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── fsharp.data.csv.core.8.1.2.nupkg
│       │       ├── fsharp.data.csv.core.8.1.2.nupkg.sha512
│       │       ├── fsharp.data.csv.core.nuspec
│       │       └── logo.png
│       ├── fsharp.data.html.core
│       │   └── 8.1.2
│       │       ├── lib
│       │       │   └── netstandard2.0
│       │       │       ├── FSharp.Data.Html.Core.dll
│       │       │       └── FSharp.Data.Html.Core.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── fsharp.data.html.core.8.1.2.nupkg
│       │       ├── fsharp.data.html.core.8.1.2.nupkg.sha512
│       │       ├── fsharp.data.html.core.nuspec
│       │       └── logo.png
│       ├── fsharp.data.http
│       │   └── 8.1.2
│       │       ├── lib
│       │       │   └── netstandard2.0
│       │       │       ├── FSharp.Data.Http.dll
│       │       │       └── FSharp.Data.Http.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── fsharp.data.http.8.1.2.nupkg
│       │       ├── fsharp.data.http.8.1.2.nupkg.sha512
│       │       ├── fsharp.data.http.nuspec
│       │       └── logo.png
│       ├── fsharp.data.json.core
│       │   └── 8.1.2
│       │       ├── lib
│       │       │   ├── net8.0
│       │       │   │   ├── FSharp.Data.Json.Core.dll
│       │       │   │   └── FSharp.Data.Json.Core.xml
│       │       │   └── netstandard2.0
│       │       │       ├── FSharp.Data.Json.Core.dll
│       │       │       └── FSharp.Data.Json.Core.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── fsharp.data.json.core.8.1.2.nupkg
│       │       ├── fsharp.data.json.core.8.1.2.nupkg.sha512
│       │       ├── fsharp.data.json.core.nuspec
│       │       └── logo.png
│       ├── fsharp.data.runtime.utilities
│       │   └── 8.1.2
│       │       ├── lib
│       │       │   ├── net8.0
│       │       │   │   ├── FSharp.Data.Runtime.Utilities.dll
│       │       │   │   └── FSharp.Data.Runtime.Utilities.xml
│       │       │   └── netstandard2.0
│       │       │       ├── FSharp.Data.Runtime.Utilities.dll
│       │       │       └── FSharp.Data.Runtime.Utilities.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── fsharp.data.runtime.utilities.8.1.2.nupkg
│       │       ├── fsharp.data.runtime.utilities.8.1.2.nupkg.sha512
│       │       ├── fsharp.data.runtime.utilities.nuspec
│       │       └── logo.png
│       ├── fsharp.data.worldbank.core
│       │   └── 8.1.2
│       │       ├── lib
│       │       │   └── netstandard2.0
│       │       │       ├── FSharp.Data.WorldBank.Core.dll
│       │       │       └── FSharp.Data.WorldBank.Core.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── fsharp.data.worldbank.core.8.1.2.nupkg
│       │       ├── fsharp.data.worldbank.core.8.1.2.nupkg.sha512
│       │       ├── fsharp.data.worldbank.core.nuspec
│       │       └── logo.png
│       ├── fsharp.data.xml.core
│       │   └── 8.1.2
│       │       ├── lib
│       │       │   ├── net8.0
│       │       │   │   ├── FSharp.Data.Xml.Core.dll
│       │       │   │   └── FSharp.Data.Xml.Core.xml
│       │       │   └── netstandard2.0
│       │       │       ├── FSharp.Data.Xml.Core.dll
│       │       │       └── FSharp.Data.Xml.Core.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── fsharp.data.xml.core.8.1.2.nupkg
│       │       ├── fsharp.data.xml.core.8.1.2.nupkg.sha512
│       │       ├── fsharp.data.xml.core.nuspec
│       │       └── logo.png
│       ├── fsharp.quotations.evaluator
│       │   └── 2.1.0
│       │       ├── lib
│       │       │   └── netstandard2.0
│       │       │       ├── FSharp.Quotations.Evaluator.dll
│       │       │       ├── FSharp.Quotations.Evaluator.pdb
│       │       │       └── FSharp.Quotations.Evaluator.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── fsharp.quotations.evaluator.2.1.0.nupkg
│       │       ├── fsharp.quotations.evaluator.2.1.0.nupkg.sha512
│       │       ├── fsharp.quotations.evaluator.nuspec
│       │       └── logo.png
│       ├── fsharp.systemtextjson
│       │   └── 1.4.36
│       │       ├── lib
│       │       │   └── netstandard2.0
│       │       │       ├── FSharp.SystemTextJson.dll
│       │       │       ├── FSharp.SystemTextJson.pdb
│       │       │       └── FSharp.SystemTextJson.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── fsharp.systemtextjson.1.4.36.nupkg
│       │       ├── fsharp.systemtextjson.1.4.36.nupkg.sha512
│       │       └── fsharp.systemtextjson.nuspec
│       ├── fsharpplus
│       │   └── 1.9.1
│       │       ├── fable
│       │       │   ├── Control
│       │       │   │   ├── Alternative.fs
│       │       │   │   ├── Applicative.fs
│       │       │   │   ├── Arrow.fs
│       │       │   │   ├── ArrowApply.fs
│       │       │   │   ├── ArrowChoice.fs
│       │       │   │   ├── Bifoldable.fs
│       │       │   │   ├── Bitraversable.fs
│       │       │   │   ├── Category.fs
│       │       │   │   ├── Collection.fs
│       │       │   │   ├── Comonad.fs
│       │       │   │   ├── Converter.fs
│       │       │   │   ├── Foldable.fs
│       │       │   │   ├── Functor.fs
│       │       │   │   ├── Indexable.fs
│       │       │   │   ├── Invokable.fs
│       │       │   │   ├── Monad.fs
│       │       │   │   ├── MonadOps.fs
│       │       │   │   ├── MonadTrans.fs
│       │       │   │   ├── Monoid.fs
│       │       │   │   ├── Numeric.fs
│       │       │   │   ├── Traversable.fs
│       │       │   │   ├── Tuple.fs
│       │       │   │   └── ZipApplicative.fs
│       │       │   ├── Data
│       │       │   │   ├── Compose.fs
│       │       │   │   ├── Const.fs
│       │       │   │   ├── Cont.fs
│       │       │   │   ├── Coproduct.fs
│       │       │   │   ├── DList.fs
│       │       │   │   ├── Error.fs
│       │       │   │   ├── Free.fs
│       │       │   │   ├── Identity.fs
│       │       │   │   ├── Kleisli.fs
│       │       │   │   ├── List.fs
│       │       │   │   ├── Monoids.fs
│       │       │   │   ├── MultiMap.fs
│       │       │   │   ├── NonEmptyList.fs
│       │       │   │   ├── NonEmptyMap.fs
│       │       │   │   ├── NonEmptySeq.fs
│       │       │   │   ├── NonEmptySet.fs
│       │       │   │   ├── Option.fs
│       │       │   │   ├── ParallelArray.fs
│       │       │   │   ├── Reader.fs
│       │       │   │   ├── Seq.fs
│       │       │   │   ├── State.fs
│       │       │   │   ├── Validation.fs
│       │       │   │   ├── ValueOption.fs
│       │       │   │   ├── Writer.fs
│       │       │   │   └── ZipList.fs
│       │       │   ├── Extensions
│       │       │   │   ├── Array.fs
│       │       │   │   ├── Async.fs
│       │       │   │   ├── AsyncEnumerable.fs
│       │       │   │   ├── Choice.fs
│       │       │   │   ├── Dict.fs
│       │       │   │   ├── Dictionary.fs
│       │       │   │   ├── Enumerator.fs
│       │       │   │   ├── Exception.fs
│       │       │   │   ├── Extensions.fs
│       │       │   │   ├── HashSet.fs
│       │       │   │   ├── IList.fs
│       │       │   │   ├── IReadOnlyCollection.fs
│       │       │   │   ├── IReadOnlyDictionary.fs
│       │       │   │   ├── IReadOnlyList.fs
│       │       │   │   ├── Lazy.fs
│       │       │   │   ├── List.fs
│       │       │   │   ├── Map.fs
│       │       │   │   ├── Nullable.fs
│       │       │   │   ├── Obj.fs
│       │       │   │   ├── Observable.fs
│       │       │   │   ├── Option.fs
│       │       │   │   ├── ResizeArray.fs
│       │       │   │   ├── Result.fs
│       │       │   │   ├── Seq.fs
│       │       │   │   ├── String.fs
│       │       │   │   ├── Task.fs
│       │       │   │   ├── Tuple.fs
│       │       │   │   ├── ValueOption.fs
│       │       │   │   ├── ValueTask.fs
│       │       │   │   └── ValueTuple.fs
│       │       │   ├── Math
│       │       │   │   ├── Applicative.fs
│       │       │   │   └── Generic.fs
│       │       │   ├── Builders.fs
│       │       │   ├── FSharpPlus.fsproj
│       │       │   ├── Internals.fs
│       │       │   ├── Lens.fs
│       │       │   ├── Memoization.fs
│       │       │   ├── Operators.fs
│       │       │   └── Parsing.fs
│       │       ├── lib
│       │       │   ├── net6.0
│       │       │   │   ├── FSharpPlus.dll
│       │       │   │   └── FSharpPlus.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── FSharpPlus.dll
│       │       │   │   └── FSharpPlus.xml
│       │       │   └── netstandard2.1
│       │       │       ├── FSharpPlus.dll
│       │       │       └── FSharpPlus.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── fsharpplus.1.9.1.nupkg
│       │       ├── fsharpplus.1.9.1.nupkg.sha512
│       │       ├── fsharpplus.nuspec
│       │       ├── logo.png
│       │       └── README.md
│       ├── fspickler
│       │   └── 5.3.2
│       │       ├── lib
│       │       │   ├── net45
│       │       │   │   ├── FsPickler.dll
│       │       │   │   ├── FsPickler.pdb
│       │       │   │   └── FsPickler.xml
│       │       │   └── netstandard2.0
│       │       │       ├── FsPickler.dll
│       │       │       ├── FsPickler.pdb
│       │       │       └── FsPickler.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── fspickler.5.3.2.nupkg
│       │       ├── fspickler.5.3.2.nupkg.sha512
│       │       └── fspickler.nuspec
│       ├── fspickler.json
│       │   └── 5.3.2
│       │       ├── lib
│       │       │   ├── net45
│       │       │   │   ├── FsPickler.Json.dll
│       │       │   │   ├── FsPickler.Json.pdb
│       │       │   │   └── FsPickler.Json.xml
│       │       │   └── netstandard2.0
│       │       │       ├── FsPickler.Json.dll
│       │       │       ├── FsPickler.Json.pdb
│       │       │       └── FsPickler.Json.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── fspickler.json.5.3.2.nupkg
│       │       ├── fspickler.json.5.3.2.nupkg.sha512
│       │       └── fspickler.json.nuspec
│       ├── fstoolkit.errorhandling
│       │   └── 5.2.0
│       │       ├── fable
│       │       │   ├── Array.fs
│       │       │   ├── AssemblyInfo.fs
│       │       │   ├── Async.fs
│       │       │   ├── AsyncOption.fs
│       │       │   ├── AsyncOptionCE.fs
│       │       │   ├── AsyncOptionOp.fs
│       │       │   ├── AsyncResult.fs
│       │       │   ├── AsyncResultCE.fs
│       │       │   ├── AsyncResultOp.fs
│       │       │   ├── AsyncResultOption.fs
│       │       │   ├── AsyncResultOptionCE.fs
│       │       │   ├── AsyncResultOptionOp.fs
│       │       │   ├── AsyncValidation.fs
│       │       │   ├── AsyncValidationCE.fs
│       │       │   ├── AsyncValidationOp.fs
│       │       │   ├── FsToolkit.ErrorHandling.fsproj
│       │       │   ├── List.fs
│       │       │   ├── Nullness.fs
│       │       │   ├── Option.fs
│       │       │   ├── OptionCE.fs
│       │       │   ├── OptionOp.fs
│       │       │   ├── ParallelAsyncResult.fs
│       │       │   ├── ParallelAsyncResultCE.fs
│       │       │   ├── ParallelAsyncValidation.fs
│       │       │   ├── ParallelAsyncValidationCE.fs
│       │       │   ├── Result.fs
│       │       │   ├── ResultCE.fs
│       │       │   ├── ResultOp.fs
│       │       │   ├── ResultOption.fs
│       │       │   ├── ResultOptionCE.fs
│       │       │   ├── ResultOptionOp.fs
│       │       │   ├── Seq.fs
│       │       │   ├── Validation.fs
│       │       │   ├── ValidationCE.fs
│       │       │   ├── ValidationOp.fs
│       │       │   ├── ValueOption.fs
│       │       │   ├── ValueTaskValueOption.fs
│       │       │   ├── ValueTaskValueOptionCE.fs
│       │       │   └── ValueTaskValueOptionOp.fs
│       │       ├── lib
│       │       │   ├── net9.0
│       │       │   │   ├── FsToolkit.ErrorHandling.dll
│       │       │   │   ├── FsToolkit.ErrorHandling.pdb
│       │       │   │   └── FsToolkit.ErrorHandling.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── FsToolkit.ErrorHandling.dll
│       │       │   │   ├── FsToolkit.ErrorHandling.pdb
│       │       │   │   └── FsToolkit.ErrorHandling.xml
│       │       │   └── netstandard2.1
│       │       │       ├── FsToolkit.ErrorHandling.dll
│       │       │       ├── FsToolkit.ErrorHandling.pdb
│       │       │       └── FsToolkit.ErrorHandling.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── fstoolkit.errorhandling.5.2.0.nupkg
│       │       ├── fstoolkit.errorhandling.5.2.0.nupkg.sha512
│       │       ├── fstoolkit.errorhandling.nuspec
│       │       └── README.md
│       ├── k4os.compression.lz4
│       │   └── 1.3.8
│       │       ├── lib
│       │       │   ├── net462
│       │       │   │   ├── K4os.Compression.LZ4.dll
│       │       │   │   └── K4os.Compression.LZ4.xml
│       │       │   ├── net5.0
│       │       │   │   ├── K4os.Compression.LZ4.dll
│       │       │   │   └── K4os.Compression.LZ4.xml
│       │       │   ├── net6.0
│       │       │   │   ├── K4os.Compression.LZ4.dll
│       │       │   │   └── K4os.Compression.LZ4.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── K4os.Compression.LZ4.dll
│       │       │   │   └── K4os.Compression.LZ4.xml
│       │       │   └── netstandard2.1
│       │       │       ├── K4os.Compression.LZ4.dll
│       │       │       └── K4os.Compression.LZ4.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── k4os.compression.lz4.1.3.8.nupkg
│       │       ├── k4os.compression.lz4.1.3.8.nupkg.sha512
│       │       └── k4os.compression.lz4.nuspec
│       ├── k4os.compression.lz4.streams
│       │   └── 1.3.8
│       │       ├── lib
│       │       │   ├── net462
│       │       │   │   ├── K4os.Compression.LZ4.Streams.dll
│       │       │   │   └── K4os.Compression.LZ4.Streams.xml
│       │       │   ├── net5.0
│       │       │   │   ├── K4os.Compression.LZ4.Streams.dll
│       │       │   │   └── K4os.Compression.LZ4.Streams.xml
│       │       │   ├── net6.0
│       │       │   │   ├── K4os.Compression.LZ4.Streams.dll
│       │       │   │   └── K4os.Compression.LZ4.Streams.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── K4os.Compression.LZ4.Streams.dll
│       │       │   │   └── K4os.Compression.LZ4.Streams.xml
│       │       │   └── netstandard2.1
│       │       │       ├── K4os.Compression.LZ4.Streams.dll
│       │       │       └── K4os.Compression.LZ4.Streams.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── k4os.compression.lz4.streams.1.3.8.nupkg
│       │       ├── k4os.compression.lz4.streams.1.3.8.nupkg.sha512
│       │       └── k4os.compression.lz4.streams.nuspec
│       ├── k4os.hash.xxhash
│       │   └── 1.0.8
│       │       ├── lib
│       │       │   ├── net462
│       │       │   │   ├── K4os.Hash.xxHash.dll
│       │       │   │   └── K4os.Hash.xxHash.xml
│       │       │   ├── net5.0
│       │       │   │   ├── K4os.Hash.xxHash.dll
│       │       │   │   └── K4os.Hash.xxHash.xml
│       │       │   ├── net6.0
│       │       │   │   ├── K4os.Hash.xxHash.dll
│       │       │   │   └── K4os.Hash.xxHash.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── K4os.Hash.xxHash.dll
│       │       │   │   └── K4os.Hash.xxHash.xml
│       │       │   └── netstandard2.1
│       │       │       ├── K4os.Hash.xxHash.dll
│       │       │       └── K4os.Hash.xxHash.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── k4os.hash.xxhash.1.0.8.nupkg
│       │       ├── k4os.hash.xxhash.1.0.8.nupkg.sha512
│       │       └── k4os.hash.xxhash.nuspec
│       ├── microsoft.applicationinsights
│       │   └── 2.23.0
│       │       ├── lib
│       │       │   ├── net452
│       │       │   │   ├── Microsoft.ApplicationInsights.dll
│       │       │   │   ├── Microsoft.ApplicationInsights.pdb
│       │       │   │   └── Microsoft.ApplicationInsights.xml
│       │       │   ├── net46
│       │       │   │   ├── Microsoft.ApplicationInsights.dll
│       │       │   │   ├── Microsoft.ApplicationInsights.pdb
│       │       │   │   └── Microsoft.ApplicationInsights.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.ApplicationInsights.dll
│       │       │       ├── Microsoft.ApplicationInsights.pdb
│       │       │       └── Microsoft.ApplicationInsights.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── icon.png
│       │       ├── microsoft.applicationinsights.2.23.0.nupkg
│       │       ├── microsoft.applicationinsights.2.23.0.nupkg.sha512
│       │       └── microsoft.applicationinsights.nuspec
│       ├── microsoft.aspnetcore.app.runtime.win-x64
│       │   └── 10.0.8
│       │       ├── data
│       │       │   └── RuntimeList.xml
│       │       ├── runtimes
│       │       │   └── win-x64
│       │       │       ├── lib
│       │       │       │   └── net10.0
│       │       │       │       ├── Microsoft.AspNetCore.Antiforgery.dll
│       │       │       │       ├── Microsoft.AspNetCore.App.deps.json
│       │       │       │       ├── Microsoft.AspNetCore.App.runtimeconfig.json
│       │       │       │       ├── Microsoft.AspNetCore.Authentication.Abstractions.dll
│       │       │       │       ├── Microsoft.AspNetCore.Authentication.BearerToken.dll
│       │       │       │       ├── Microsoft.AspNetCore.Authentication.Cookies.dll
│       │       │       │       ├── Microsoft.AspNetCore.Authentication.Core.dll
│       │       │       │       ├── Microsoft.AspNetCore.Authentication.dll
│       │       │       │       ├── Microsoft.AspNetCore.Authentication.OAuth.dll
│       │       │       │       ├── Microsoft.AspNetCore.Authorization.dll
│       │       │       │       ├── Microsoft.AspNetCore.Authorization.Policy.dll
│       │       │       │       ├── Microsoft.AspNetCore.Components.Authorization.dll
│       │       │       │       ├── Microsoft.AspNetCore.Components.dll
│       │       │       │       ├── Microsoft.AspNetCore.Components.Endpoints.dll
│       │       │       │       ├── Microsoft.AspNetCore.Components.Forms.dll
│       │       │       │       ├── Microsoft.AspNetCore.Components.Server.dll
│       │       │       │       ├── Microsoft.AspNetCore.Components.Web.dll
│       │       │       │       ├── Microsoft.AspNetCore.Connections.Abstractions.dll
│       │       │       │       ├── Microsoft.AspNetCore.CookiePolicy.dll
│       │       │       │       ├── Microsoft.AspNetCore.Cors.dll
│       │       │       │       ├── Microsoft.AspNetCore.Cryptography.Internal.dll
│       │       │       │       ├── Microsoft.AspNetCore.Cryptography.KeyDerivation.dll
│       │       │       │       ├── Microsoft.AspNetCore.DataProtection.Abstractions.dll
│       │       │       │       ├── Microsoft.AspNetCore.DataProtection.dll
│       │       │       │       ├── Microsoft.AspNetCore.DataProtection.Extensions.dll
│       │       │       │       ├── Microsoft.AspNetCore.Diagnostics.Abstractions.dll
│       │       │       │       ├── Microsoft.AspNetCore.Diagnostics.dll
│       │       │       │       ├── Microsoft.AspNetCore.Diagnostics.HealthChecks.dll
│       │       │       │       ├── Microsoft.AspNetCore.dll
│       │       │       │       ├── Microsoft.AspNetCore.HostFiltering.dll
│       │       │       │       ├── Microsoft.AspNetCore.Hosting.Abstractions.dll
│       │       │       │       ├── Microsoft.AspNetCore.Hosting.dll
│       │       │       │       ├── Microsoft.AspNetCore.Hosting.Server.Abstractions.dll
│       │       │       │       ├── Microsoft.AspNetCore.Html.Abstractions.dll
│       │       │       │       ├── Microsoft.AspNetCore.Http.Abstractions.dll
│       │       │       │       ├── Microsoft.AspNetCore.Http.Connections.Common.dll
│       │       │       │       ├── Microsoft.AspNetCore.Http.Connections.dll
│       │       │       │       ├── Microsoft.AspNetCore.Http.dll
│       │       │       │       ├── Microsoft.AspNetCore.Http.Extensions.dll
│       │       │       │       ├── Microsoft.AspNetCore.Http.Features.dll
│       │       │       │       ├── Microsoft.AspNetCore.Http.Results.dll
│       │       │       │       ├── Microsoft.AspNetCore.HttpLogging.dll
│       │       │       │       ├── Microsoft.AspNetCore.HttpOverrides.dll
│       │       │       │       ├── Microsoft.AspNetCore.HttpsPolicy.dll
│       │       │       │       ├── Microsoft.AspNetCore.Identity.dll
│       │       │       │       ├── Microsoft.AspNetCore.Localization.dll
│       │       │       │       ├── Microsoft.AspNetCore.Localization.Routing.dll
│       │       │       │       ├── Microsoft.AspNetCore.Metadata.dll
│       │       │       │       ├── Microsoft.AspNetCore.Mvc.Abstractions.dll
│       │       │       │       ├── Microsoft.AspNetCore.Mvc.ApiExplorer.dll
│       │       │       │       ├── Microsoft.AspNetCore.Mvc.Core.dll
│       │       │       │       ├── Microsoft.AspNetCore.Mvc.Cors.dll
│       │       │       │       ├── Microsoft.AspNetCore.Mvc.DataAnnotations.dll
│       │       │       │       ├── Microsoft.AspNetCore.Mvc.dll
│       │       │       │       ├── Microsoft.AspNetCore.Mvc.Formatters.Json.dll
│       │       │       │       ├── Microsoft.AspNetCore.Mvc.Formatters.Xml.dll
│       │       │       │       ├── Microsoft.AspNetCore.Mvc.Localization.dll
│       │       │       │       ├── Microsoft.AspNetCore.Mvc.Razor.dll
│       │       │       │       ├── Microsoft.AspNetCore.Mvc.RazorPages.dll
│       │       │       │       ├── Microsoft.AspNetCore.Mvc.TagHelpers.dll
│       │       │       │       ├── Microsoft.AspNetCore.Mvc.ViewFeatures.dll
│       │       │       │       ├── Microsoft.AspNetCore.OutputCaching.dll
│       │       │       │       ├── Microsoft.AspNetCore.RateLimiting.dll
│       │       │       │       ├── Microsoft.AspNetCore.Razor.dll
│       │       │       │       ├── Microsoft.AspNetCore.Razor.Runtime.dll
│       │       │       │       ├── Microsoft.AspNetCore.RequestDecompression.dll
│       │       │       │       ├── Microsoft.AspNetCore.ResponseCaching.Abstractions.dll
│       │       │       │       ├── Microsoft.AspNetCore.ResponseCaching.dll
│       │       │       │       ├── Microsoft.AspNetCore.ResponseCompression.dll
│       │       │       │       ├── Microsoft.AspNetCore.Rewrite.dll
│       │       │       │       ├── Microsoft.AspNetCore.Routing.Abstractions.dll
│       │       │       │       ├── Microsoft.AspNetCore.Routing.dll
│       │       │       │       ├── Microsoft.AspNetCore.Server.HttpSys.dll
│       │       │       │       ├── Microsoft.AspNetCore.Server.IIS.dll
│       │       │       │       ├── Microsoft.AspNetCore.Server.IISIntegration.dll
│       │       │       │       ├── Microsoft.AspNetCore.Server.Kestrel.Core.dll
│       │       │       │       ├── Microsoft.AspNetCore.Server.Kestrel.dll
│       │       │       │       ├── Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes.dll
│       │       │       │       ├── Microsoft.AspNetCore.Server.Kestrel.Transport.Quic.dll
│       │       │       │       ├── Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.dll
│       │       │       │       ├── Microsoft.AspNetCore.Session.dll
│       │       │       │       ├── Microsoft.AspNetCore.SignalR.Common.dll
│       │       │       │       ├── Microsoft.AspNetCore.SignalR.Core.dll
│       │       │       │       ├── Microsoft.AspNetCore.SignalR.dll
│       │       │       │       ├── Microsoft.AspNetCore.SignalR.Protocols.Json.dll
│       │       │       │       ├── Microsoft.AspNetCore.StaticAssets.dll
│       │       │       │       ├── Microsoft.AspNetCore.StaticFiles.dll
│       │       │       │       ├── Microsoft.AspNetCore.WebSockets.dll
│       │       │       │       ├── Microsoft.AspNetCore.WebUtilities.dll
│       │       │       │       ├── Microsoft.Extensions.Caching.Abstractions.dll
│       │       │       │       ├── Microsoft.Extensions.Caching.Memory.dll
│       │       │       │       ├── Microsoft.Extensions.Configuration.Abstractions.dll
│       │       │       │       ├── Microsoft.Extensions.Configuration.Binder.dll
│       │       │       │       ├── Microsoft.Extensions.Configuration.CommandLine.dll
│       │       │       │       ├── Microsoft.Extensions.Configuration.dll
│       │       │       │       ├── Microsoft.Extensions.Configuration.EnvironmentVariables.dll
│       │       │       │       ├── Microsoft.Extensions.Configuration.FileExtensions.dll
│       │       │       │       ├── Microsoft.Extensions.Configuration.Ini.dll
│       │       │       │       ├── Microsoft.Extensions.Configuration.Json.dll
│       │       │       │       ├── Microsoft.Extensions.Configuration.KeyPerFile.dll
│       │       │       │       ├── Microsoft.Extensions.Configuration.UserSecrets.dll
│       │       │       │       ├── Microsoft.Extensions.Configuration.Xml.dll
│       │       │       │       ├── Microsoft.Extensions.DependencyInjection.Abstractions.dll
│       │       │       │       ├── Microsoft.Extensions.DependencyInjection.dll
│       │       │       │       ├── Microsoft.Extensions.Diagnostics.Abstractions.dll
│       │       │       │       ├── Microsoft.Extensions.Diagnostics.dll
│       │       │       │       ├── Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions.dll
│       │       │       │       ├── Microsoft.Extensions.Diagnostics.HealthChecks.dll
│       │       │       │       ├── Microsoft.Extensions.Features.dll
│       │       │       │       ├── Microsoft.Extensions.FileProviders.Abstractions.dll
│       │       │       │       ├── Microsoft.Extensions.FileProviders.Composite.dll
│       │       │       │       ├── Microsoft.Extensions.FileProviders.Embedded.dll
│       │       │       │       ├── Microsoft.Extensions.FileProviders.Physical.dll
│       │       │       │       ├── Microsoft.Extensions.FileSystemGlobbing.dll
│       │       │       │       ├── Microsoft.Extensions.Hosting.Abstractions.dll
│       │       │       │       ├── Microsoft.Extensions.Hosting.dll
│       │       │       │       ├── Microsoft.Extensions.Http.dll
│       │       │       │       ├── Microsoft.Extensions.Identity.Core.dll
│       │       │       │       ├── Microsoft.Extensions.Identity.Stores.dll
│       │       │       │       ├── Microsoft.Extensions.Localization.Abstractions.dll
│       │       │       │       ├── Microsoft.Extensions.Localization.dll
│       │       │       │       ├── Microsoft.Extensions.Logging.Abstractions.dll
│       │       │       │       ├── Microsoft.Extensions.Logging.Configuration.dll
│       │       │       │       ├── Microsoft.Extensions.Logging.Console.dll
│       │       │       │       ├── Microsoft.Extensions.Logging.Debug.dll
│       │       │       │       ├── Microsoft.Extensions.Logging.dll
│       │       │       │       ├── Microsoft.Extensions.Logging.EventLog.dll
│       │       │       │       ├── Microsoft.Extensions.Logging.EventSource.dll
│       │       │       │       ├── Microsoft.Extensions.Logging.TraceSource.dll
│       │       │       │       ├── Microsoft.Extensions.ObjectPool.dll
│       │       │       │       ├── Microsoft.Extensions.Options.ConfigurationExtensions.dll
│       │       │       │       ├── Microsoft.Extensions.Options.DataAnnotations.dll
│       │       │       │       ├── Microsoft.Extensions.Options.dll
│       │       │       │       ├── Microsoft.Extensions.Primitives.dll
│       │       │       │       ├── Microsoft.Extensions.Validation.dll
│       │       │       │       ├── Microsoft.Extensions.WebEncoders.dll
│       │       │       │       ├── Microsoft.JSInterop.dll
│       │       │       │       ├── Microsoft.Net.Http.Headers.dll
│       │       │       │       ├── System.Diagnostics.EventLog.dll
│       │       │       │       ├── System.Diagnostics.EventLog.Messages.dll
│       │       │       │       ├── System.Formats.Cbor.dll
│       │       │       │       ├── System.Security.Cryptography.Pkcs.dll
│       │       │       │       ├── System.Security.Cryptography.Xml.dll
│       │       │       │       └── System.Threading.RateLimiting.dll
│       │       │       └── native
│       │       │           └── aspnetcorev2_inprocess.dll
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── LICENSE.txt
│       │       ├── microsoft.aspnetcore.app.runtime.win-x64.10.0.8.nupkg
│       │       ├── microsoft.aspnetcore.app.runtime.win-x64.10.0.8.nupkg.sha512
│       │       ├── microsoft.aspnetcore.app.runtime.win-x64.nuspec
│       │       ├── Microsoft.AspNetCore.App.versions.txt
│       │       └── THIRD-PARTY-NOTICES.TXT
│       ├── microsoft.aspnetcore.openapi
│       │   └── 9.0.15
│       │       ├── lib
│       │       │   └── net9.0
│       │       │       ├── Microsoft.AspNetCore.OpenApi.dll
│       │       │       └── Microsoft.AspNetCore.OpenApi.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.aspnetcore.openapi.9.0.15.nupkg
│       │       ├── microsoft.aspnetcore.openapi.9.0.15.nupkg.sha512
│       │       ├── microsoft.aspnetcore.openapi.nuspec
│       │       ├── PACKAGE.md
│       │       └── THIRD-PARTY-NOTICES.TXT
│       ├── microsoft.azure.amqp
│       │   ├── 2.6.7
│       │   │   ├── images
│       │   │   │   └── icon.png
│       │   │   ├── lib
│       │   │   │   ├── monoandroid
│       │   │   │   │   └── Microsoft.Azure.Amqp.dll
│       │   │   │   ├── net45
│       │   │   │   │   ├── Microsoft.Azure.Amqp.dll
│       │   │   │   │   └── Microsoft.Azure.Amqp.xml
│       │   │   │   ├── netstandard1.3
│       │   │   │   │   ├── Microsoft.Azure.Amqp.dll
│       │   │   │   │   └── Microsoft.Azure.Amqp.xml
│       │   │   │   ├── netstandard2.0
│       │   │   │   │   ├── Microsoft.Azure.Amqp.dll
│       │   │   │   │   └── Microsoft.Azure.Amqp.xml
│       │   │   │   ├── portable-net45+wp8+wpa81+win8+MonoAndroid10+MonoTouch10+Xamarin.iOS10+UAP10
│       │   │   │   │   └── Microsoft.Azure.Amqp.dll
│       │   │   │   └── uap10.0
│       │   │   │       ├── Microsoft.Azure.Amqp.dll
│       │   │   │       └── Microsoft.Azure.Amqp.pri
│       │   │   ├── .nupkg.metadata
│       │   │   ├── .signature.p7s
│       │   │   ├── microsoft.azure.amqp.2.6.7.nupkg
│       │   │   ├── microsoft.azure.amqp.2.6.7.nupkg.sha512
│       │   │   └── microsoft.azure.amqp.nuspec
│       │   └── 2.7.0
│       │       ├── images
│       │       │   └── icon.png
│       │       ├── lib
│       │       │   ├── monoandroid
│       │       │   │   └── Microsoft.Azure.Amqp.dll
│       │       │   ├── net45
│       │       │   │   ├── Microsoft.Azure.Amqp.dll
│       │       │   │   └── Microsoft.Azure.Amqp.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Azure.Amqp.dll
│       │       │   │   └── Microsoft.Azure.Amqp.xml
│       │       │   ├── netstandard1.3
│       │       │   │   ├── Microsoft.Azure.Amqp.dll
│       │       │   │   └── Microsoft.Azure.Amqp.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── Microsoft.Azure.Amqp.dll
│       │       │   │   └── Microsoft.Azure.Amqp.xml
│       │       │   ├── portable-net45+wp8+wpa81+win8+MonoAndroid10+MonoTouch10+Xamarin.iOS10+UAP10
│       │       │   │   └── Microsoft.Azure.Amqp.dll
│       │       │   └── uap10.0
│       │       │       ├── Microsoft.Azure.Amqp.dll
│       │       │       └── Microsoft.Azure.Amqp.pri
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── microsoft.azure.amqp.2.7.0.nupkg
│       │       ├── microsoft.azure.amqp.2.7.0.nupkg.sha512
│       │       └── microsoft.azure.amqp.nuspec
│       ├── microsoft.bcl.asyncinterfaces
│       │   ├── 10.0.3
│       │   │   ├── buildTransitive
│       │   │   │   ├── net461
│       │   │   │   │   └── Microsoft.Bcl.AsyncInterfaces.targets
│       │   │   │   └── net462
│       │   │   │       └── _._
│       │   │   ├── lib
│       │   │   │   ├── net462
│       │   │   │   │   ├── Microsoft.Bcl.AsyncInterfaces.dll
│       │   │   │   │   └── Microsoft.Bcl.AsyncInterfaces.xml
│       │   │   │   ├── netstandard2.0
│       │   │   │   │   ├── Microsoft.Bcl.AsyncInterfaces.dll
│       │   │   │   │   └── Microsoft.Bcl.AsyncInterfaces.xml
│       │   │   │   └── netstandard2.1
│       │   │   │       ├── Microsoft.Bcl.AsyncInterfaces.dll
│       │   │   │       └── Microsoft.Bcl.AsyncInterfaces.xml
│       │   │   ├── .nupkg.metadata
│       │   │   ├── .signature.p7s
│       │   │   ├── Icon.png
│       │   │   ├── microsoft.bcl.asyncinterfaces.10.0.3.nupkg
│       │   │   ├── microsoft.bcl.asyncinterfaces.10.0.3.nupkg.sha512
│       │   │   ├── microsoft.bcl.asyncinterfaces.nuspec
│       │   │   ├── PACKAGE.md
│       │   │   ├── THIRD-PARTY-NOTICES.TXT
│       │   │   └── useSharedDesignerContext.txt
│       │   └── 8.0.0
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Bcl.AsyncInterfaces.targets
│       │       │   └── net462
│       │       │       └── _._
│       │       ├── lib
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Bcl.AsyncInterfaces.dll
│       │       │   │   └── Microsoft.Bcl.AsyncInterfaces.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── Microsoft.Bcl.AsyncInterfaces.dll
│       │       │   │   └── Microsoft.Bcl.AsyncInterfaces.xml
│       │       │   └── netstandard2.1
│       │       │       ├── Microsoft.Bcl.AsyncInterfaces.dll
│       │       │       └── Microsoft.Bcl.AsyncInterfaces.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── LICENSE.TXT
│       │       ├── microsoft.bcl.asyncinterfaces.8.0.0.nupkg
│       │       ├── microsoft.bcl.asyncinterfaces.8.0.0.nupkg.sha512
│       │       ├── microsoft.bcl.asyncinterfaces.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.codeanalysis.analyzers
│       │   └── 5.3.0-2.25625.1
│       │       ├── analyzers
│       │       │   └── dotnet
│       │       │       ├── cs
│       │       │       │   ├── cs
│       │       │       │   │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │       │   ├── de
│       │       │       │   │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │       │   ├── es
│       │       │       │   │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │       │   ├── fr
│       │       │       │   │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │       │   ├── it
│       │       │       │   │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │       │   ├── ja
│       │       │       │   │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │       │   ├── ko
│       │       │       │   │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │       │   ├── pl
│       │       │       │   │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │       │   ├── pt-BR
│       │       │       │   │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │       │   ├── ru
│       │       │       │   │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │       │   ├── tr
│       │       │       │   │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │       │   ├── zh-Hans
│       │       │       │   │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │       │   ├── zh-Hant
│       │       │       │   │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │       │   ├── Microsoft.CodeAnalysis.Analyzers.dll
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.Analyzers.dll
│       │       │       └── vb
│       │       │           ├── cs
│       │       │           │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │           ├── de
│       │       │           │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │           ├── es
│       │       │           │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │           ├── fr
│       │       │           │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │           ├── it
│       │       │           │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │           ├── ja
│       │       │           │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │           ├── ko
│       │       │           │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │           ├── pl
│       │       │           │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │           ├── pt-BR
│       │       │           │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │           ├── ru
│       │       │           │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │           ├── tr
│       │       │           │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │           ├── zh-Hans
│       │       │           │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │           ├── zh-Hant
│       │       │           │   └── Microsoft.CodeAnalysis.Analyzers.resources.dll
│       │       │           ├── Microsoft.CodeAnalysis.Analyzers.dll
│       │       │           └── Microsoft.CodeAnalysis.VisualBasic.Analyzers.dll
│       │       ├── buildTransitive
│       │       │   ├── Microsoft.CodeAnalysis.Analyzers.props
│       │       │   └── Microsoft.CodeAnalysis.Analyzers.targets
│       │       ├── documentation
│       │       │   ├── Microsoft.CodeAnalysis.Analyzers.md
│       │       │   ├── Microsoft.CodeAnalysis.Analyzers.sarif
│       │       │   └── readme.md
│       │       ├── editorconfig
│       │       │   ├── AllRulesDefault
│       │       │   │   └── .editorconfig
│       │       │   ├── AllRulesDisabled
│       │       │   │   └── .editorconfig
│       │       │   ├── AllRulesEnabled
│       │       │   │   └── .editorconfig
│       │       │   ├── CorrectnessRulesDefault
│       │       │   │   └── .editorconfig
│       │       │   ├── CorrectnessRulesEnabled
│       │       │   │   └── .editorconfig
│       │       │   ├── DataflowRulesDefault
│       │       │   │   └── .editorconfig
│       │       │   ├── DataflowRulesEnabled
│       │       │   │   └── .editorconfig
│       │       │   ├── LibraryRulesDefault
│       │       │   │   └── .editorconfig
│       │       │   ├── LibraryRulesEnabled
│       │       │   │   └── .editorconfig
│       │       │   ├── MicrosoftCodeAnalysisCompatibilityRulesDefault
│       │       │   │   └── .editorconfig
│       │       │   ├── MicrosoftCodeAnalysisCompatibilityRulesEnabled
│       │       │   │   └── .editorconfig
│       │       │   ├── MicrosoftCodeAnalysisCorrectnessRulesDefault
│       │       │   │   └── .editorconfig
│       │       │   ├── MicrosoftCodeAnalysisCorrectnessRulesEnabled
│       │       │   │   └── .editorconfig
│       │       │   ├── MicrosoftCodeAnalysisDesignRulesDefault
│       │       │   │   └── .editorconfig
│       │       │   ├── MicrosoftCodeAnalysisDesignRulesEnabled
│       │       │   │   └── .editorconfig
│       │       │   ├── MicrosoftCodeAnalysisDocumentationRulesDefault
│       │       │   │   └── .editorconfig
│       │       │   ├── MicrosoftCodeAnalysisDocumentationRulesEnabled
│       │       │   │   └── .editorconfig
│       │       │   ├── MicrosoftCodeAnalysisLocalizationRulesDefault
│       │       │   │   └── .editorconfig
│       │       │   ├── MicrosoftCodeAnalysisLocalizationRulesEnabled
│       │       │   │   └── .editorconfig
│       │       │   ├── MicrosoftCodeAnalysisPerformanceRulesDefault
│       │       │   │   └── .editorconfig
│       │       │   ├── MicrosoftCodeAnalysisPerformanceRulesEnabled
│       │       │   │   └── .editorconfig
│       │       │   ├── MicrosoftCodeAnalysisReleaseTrackingRulesDefault
│       │       │   │   └── .editorconfig
│       │       │   ├── MicrosoftCodeAnalysisReleaseTrackingRulesEnabled
│       │       │   │   └── .editorconfig
│       │       │   ├── PortedFromFxCopRulesDefault
│       │       │   │   └── .editorconfig
│       │       │   └── PortedFromFxCopRulesEnabled
│       │       │       └── .editorconfig
│       │       ├── rulesets
│       │       │   ├── AllRulesDefault.ruleset
│       │       │   ├── AllRulesDisabled.ruleset
│       │       │   ├── AllRulesEnabled.ruleset
│       │       │   ├── CorrectnessRulesDefault.ruleset
│       │       │   ├── CorrectnessRulesEnabled.ruleset
│       │       │   ├── DataflowRulesDefault.ruleset
│       │       │   ├── DataflowRulesEnabled.ruleset
│       │       │   ├── LibraryRulesDefault.ruleset
│       │       │   ├── LibraryRulesEnabled.ruleset
│       │       │   ├── MicrosoftCodeAnalysisCompatibilityRulesDefault.ruleset
│       │       │   ├── MicrosoftCodeAnalysisCompatibilityRulesEnabled.ruleset
│       │       │   ├── MicrosoftCodeAnalysisCorrectnessRulesDefault.ruleset
│       │       │   ├── MicrosoftCodeAnalysisCorrectnessRulesEnabled.ruleset
│       │       │   ├── MicrosoftCodeAnalysisDesignRulesDefault.ruleset
│       │       │   ├── MicrosoftCodeAnalysisDesignRulesEnabled.ruleset
│       │       │   ├── MicrosoftCodeAnalysisDocumentationRulesDefault.ruleset
│       │       │   ├── MicrosoftCodeAnalysisDocumentationRulesEnabled.ruleset
│       │       │   ├── MicrosoftCodeAnalysisLocalizationRulesDefault.ruleset
│       │       │   ├── MicrosoftCodeAnalysisLocalizationRulesEnabled.ruleset
│       │       │   ├── MicrosoftCodeAnalysisPerformanceRulesDefault.ruleset
│       │       │   ├── MicrosoftCodeAnalysisPerformanceRulesEnabled.ruleset
│       │       │   ├── MicrosoftCodeAnalysisReleaseTrackingRulesDefault.ruleset
│       │       │   ├── MicrosoftCodeAnalysisReleaseTrackingRulesEnabled.ruleset
│       │       │   ├── PortedFromFxCopRulesDefault.ruleset
│       │       │   └── PortedFromFxCopRulesEnabled.ruleset
│       │       ├── tools
│       │       │   ├── install.ps1
│       │       │   └── uninstall.ps1
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.codeanalysis.analyzers.5.3.0-2.25625.1.nupkg
│       │       ├── microsoft.codeanalysis.analyzers.5.3.0-2.25625.1.nupkg.sha512
│       │       ├── microsoft.codeanalysis.analyzers.nuspec
│       │       └── ThirdPartyNotices.txt
│       ├── microsoft.codeanalysis.common
│       │   └── 5.3.0
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.pdb
│       │       │   │   └── Microsoft.CodeAnalysis.xml
│       │       │   ├── net8.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.pdb
│       │       │   │   └── Microsoft.CodeAnalysis.xml
│       │       │   ├── net9.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.pdb
│       │       │   │   └── Microsoft.CodeAnalysis.xml
│       │       │   └── netstandard2.0
│       │       │       ├── cs
│       │       │       │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │       ├── de
│       │       │       │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │       ├── es
│       │       │       │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │       ├── fr
│       │       │       │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │       ├── it
│       │       │       │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │       ├── ja
│       │       │       │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │       ├── ko
│       │       │       │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │       ├── pl
│       │       │       │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │       ├── pt-BR
│       │       │       │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │       ├── ru
│       │       │       │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │       ├── tr
│       │       │       │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │       ├── zh-Hans
│       │       │       │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │       ├── zh-Hant
│       │       │       │   └── Microsoft.CodeAnalysis.resources.dll
│       │       │       ├── Microsoft.CodeAnalysis.dll
│       │       │       ├── Microsoft.CodeAnalysis.pdb
│       │       │       └── Microsoft.CodeAnalysis.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.codeanalysis.common.5.3.0.nupkg
│       │       ├── microsoft.codeanalysis.common.5.3.0.nupkg.sha512
│       │       ├── microsoft.codeanalysis.common.nuspec
│       │       └── ThirdPartyNotices.rtf
│       ├── microsoft.codeanalysis.csharp
│       │   └── 5.3.0
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.CSharp.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.CSharp.pdb
│       │       │   │   └── Microsoft.CodeAnalysis.CSharp.xml
│       │       │   ├── net8.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.CSharp.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.CSharp.pdb
│       │       │   │   └── Microsoft.CodeAnalysis.CSharp.xml
│       │       │   ├── net9.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.CSharp.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.CSharp.pdb
│       │       │   │   └── Microsoft.CodeAnalysis.CSharp.xml
│       │       │   └── netstandard2.0
│       │       │       ├── cs
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │       ├── de
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │       ├── es
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │       ├── fr
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │       ├── it
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │       ├── ja
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │       ├── ko
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │       ├── pl
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │       ├── pt-BR
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │       ├── ru
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │       ├── tr
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │       ├── zh-Hans
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │       ├── zh-Hant
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.resources.dll
│       │       │       ├── Microsoft.CodeAnalysis.CSharp.dll
│       │       │       ├── Microsoft.CodeAnalysis.CSharp.pdb
│       │       │       └── Microsoft.CodeAnalysis.CSharp.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.codeanalysis.csharp.5.3.0.nupkg
│       │       ├── microsoft.codeanalysis.csharp.5.3.0.nupkg.sha512
│       │       ├── microsoft.codeanalysis.csharp.nuspec
│       │       └── ThirdPartyNotices.rtf
│       ├── microsoft.codeanalysis.csharp.scripting
│       │   └── 5.3.0
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.CSharp.Scripting.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.CSharp.Scripting.pdb
│       │       │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.xml
│       │       │   ├── net8.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.CSharp.Scripting.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.CSharp.Scripting.pdb
│       │       │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.xml
│       │       │   ├── net9.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.CSharp.Scripting.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.CSharp.Scripting.pdb
│       │       │   │   └── Microsoft.CodeAnalysis.CSharp.Scripting.xml
│       │       │   └── netstandard2.0
│       │       │       ├── cs
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │       ├── de
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │       ├── es
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │       ├── fr
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │       ├── it
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │       ├── ja
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │       ├── ko
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │       ├── pl
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │       ├── pt-BR
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │       ├── ru
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │       ├── tr
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │       ├── zh-Hans
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │       ├── zh-Hant
│       │       │       │   └── Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll
│       │       │       ├── Microsoft.CodeAnalysis.CSharp.Scripting.dll
│       │       │       ├── Microsoft.CodeAnalysis.CSharp.Scripting.pdb
│       │       │       └── Microsoft.CodeAnalysis.CSharp.Scripting.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.codeanalysis.csharp.scripting.5.3.0.nupkg
│       │       ├── microsoft.codeanalysis.csharp.scripting.5.3.0.nupkg.sha512
│       │       ├── microsoft.codeanalysis.csharp.scripting.nuspec
│       │       └── ThirdPartyNotices.rtf
│       ├── microsoft.codeanalysis.scripting.common
│       │   └── 5.3.0
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.Scripting.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.Scripting.pdb
│       │       │   │   └── Microsoft.CodeAnalysis.Scripting.xml
│       │       │   ├── net8.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.Scripting.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.Scripting.pdb
│       │       │   │   └── Microsoft.CodeAnalysis.Scripting.xml
│       │       │   ├── net9.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.Scripting.dll
│       │       │   │   ├── Microsoft.CodeAnalysis.Scripting.pdb
│       │       │   │   └── Microsoft.CodeAnalysis.Scripting.xml
│       │       │   └── netstandard2.0
│       │       │       ├── cs
│       │       │       │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │       ├── de
│       │       │       │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │       ├── es
│       │       │       │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │       ├── fr
│       │       │       │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │       ├── it
│       │       │       │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │       ├── ja
│       │       │       │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │       ├── ko
│       │       │       │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │       ├── pl
│       │       │       │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │       ├── pt-BR
│       │       │       │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │       ├── ru
│       │       │       │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │       ├── tr
│       │       │       │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │       ├── zh-Hans
│       │       │       │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │       ├── zh-Hant
│       │       │       │   └── Microsoft.CodeAnalysis.Scripting.resources.dll
│       │       │       ├── Microsoft.CodeAnalysis.Scripting.dll
│       │       │       ├── Microsoft.CodeAnalysis.Scripting.pdb
│       │       │       └── Microsoft.CodeAnalysis.Scripting.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.codeanalysis.scripting.common.5.3.0.nupkg
│       │       ├── microsoft.codeanalysis.scripting.common.5.3.0.nupkg.sha512
│       │       ├── microsoft.codeanalysis.scripting.common.nuspec
│       │       └── ThirdPartyNotices.rtf
│       ├── microsoft.codecoverage
│       │   └── 18.5.1
│       │       ├── build
│       │       │   └── netstandard2.0
│       │       │       ├── alpine
│       │       │       │   └── x64
│       │       │       │       ├── Cov_x64.config
│       │       │       │       ├── libCoverageInstrumentationMethod.so
│       │       │       │       └── libInstrumentationEngine.so
│       │       │       ├── arm64
│       │       │       │   └── MicrosoftInstrumentationEngine_arm64.dll
│       │       │       ├── CodeCoverage
│       │       │       │   ├── amd64
│       │       │       │   │   ├── CodeCoverage.exe
│       │       │       │   │   ├── Cov_x64.config
│       │       │       │   │   ├── covrun64.dll
│       │       │       │   │   └── msdia140.dll
│       │       │       │   ├── arm64
│       │       │       │   │   ├── Cov_arm64.config
│       │       │       │   │   ├── covrunarm64.dll
│       │       │       │   │   └── msdia140.dll
│       │       │       │   ├── coreclr
│       │       │       │   │   └── Microsoft.VisualStudio.CodeCoverage.Shim.dll
│       │       │       │   ├── CodeCoverage.config
│       │       │       │   ├── CodeCoverage.exe
│       │       │       │   ├── CodeCoverageMessages.dll
│       │       │       │   ├── Cov_x86.config
│       │       │       │   ├── covrun32.dll
│       │       │       │   └── msdia140.dll
│       │       │       ├── cs
│       │       │       │   ├── Microsoft.CodeCoverage.Core.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TraceDataCollector.resources.dll
│       │       │       ├── de
│       │       │       │   ├── Microsoft.CodeCoverage.Core.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TraceDataCollector.resources.dll
│       │       │       ├── es
│       │       │       │   ├── Microsoft.CodeCoverage.Core.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TraceDataCollector.resources.dll
│       │       │       ├── fr
│       │       │       │   ├── Microsoft.CodeCoverage.Core.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TraceDataCollector.resources.dll
│       │       │       ├── it
│       │       │       │   ├── Microsoft.CodeCoverage.Core.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TraceDataCollector.resources.dll
│       │       │       ├── ja
│       │       │       │   ├── Microsoft.CodeCoverage.Core.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TraceDataCollector.resources.dll
│       │       │       ├── ko
│       │       │       │   ├── Microsoft.CodeCoverage.Core.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TraceDataCollector.resources.dll
│       │       │       ├── macos
│       │       │       │   └── x64
│       │       │       │       ├── Cov_x64.config
│       │       │       │       ├── libCoverageInstrumentationMethod.dylib
│       │       │       │       └── libInstrumentationEngine.dylib
│       │       │       ├── pl
│       │       │       │   ├── Microsoft.CodeCoverage.Core.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TraceDataCollector.resources.dll
│       │       │       ├── pt-BR
│       │       │       │   ├── Microsoft.CodeCoverage.Core.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TraceDataCollector.resources.dll
│       │       │       ├── ru
│       │       │       │   ├── Microsoft.CodeCoverage.Core.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TraceDataCollector.resources.dll
│       │       │       ├── tr
│       │       │       │   ├── Microsoft.CodeCoverage.Core.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TraceDataCollector.resources.dll
│       │       │       ├── ubuntu
│       │       │       │   └── x64
│       │       │       │       ├── Cov_x64.config
│       │       │       │       ├── libCoverageInstrumentationMethod.so
│       │       │       │       └── libInstrumentationEngine.so
│       │       │       ├── x64
│       │       │       │   └── MicrosoftInstrumentationEngine_x64.dll
│       │       │       ├── x86
│       │       │       │   └── MicrosoftInstrumentationEngine_x86.dll
│       │       │       ├── zh-Hans
│       │       │       │   ├── Microsoft.CodeCoverage.Core.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TraceDataCollector.resources.dll
│       │       │       ├── zh-Hant
│       │       │       │   ├── Microsoft.CodeCoverage.Core.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TraceDataCollector.resources.dll
│       │       │       ├── Microsoft.CodeCoverage.Core.dll
│       │       │       ├── Microsoft.CodeCoverage.Instrumentation.Core.dll
│       │       │       ├── Microsoft.CodeCoverage.Instrumentation.dll
│       │       │       ├── Microsoft.CodeCoverage.Interprocess.dll
│       │       │       ├── Microsoft.CodeCoverage.props
│       │       │       ├── Microsoft.CodeCoverage.targets
│       │       │       ├── Microsoft.DiaSymReader.dll
│       │       │       ├── Microsoft.VisualStudio.TraceDataCollector.dll
│       │       │       ├── Mono.Cecil.dll
│       │       │       ├── Mono.Cecil.Pdb.dll
│       │       │       ├── Mono.Cecil.Rocks.dll
│       │       │       ├── System.Memory.dll
│       │       │       ├── System.Text.Json.dll
│       │       │       └── ThirdPartyNotices.txt
│       │       ├── lib
│       │       │   ├── net462
│       │       │   │   └── Microsoft.VisualStudio.CodeCoverage.Shim.dll
│       │       │   └── net8.0
│       │       │       └── Microsoft.VisualStudio.CodeCoverage.Shim.dll
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.codecoverage.18.5.1.nupkg
│       │       ├── microsoft.codecoverage.18.5.1.nupkg.sha512
│       │       ├── microsoft.codecoverage.nuspec
│       │       ├── PACKAGE.md
│       │       └── ThirdPartyNotices.txt
│       ├── microsoft.data.sqlclient
│       │   └── 5.2.2
│       │       ├── lib
│       │       │   ├── net462
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── Microsoft.Data.SqlClient.dll
│       │       │   │   └── Microsoft.Data.SqlClient.xml
│       │       │   ├── net6.0
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── Microsoft.Data.SqlClient.dll
│       │       │   │   └── Microsoft.Data.SqlClient.xml
│       │       │   ├── net8.0
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── Microsoft.Data.SqlClient.dll
│       │       │   │   └── Microsoft.Data.SqlClient.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │   │   ├── Microsoft.Data.SqlClient.dll
│       │       │   │   └── Microsoft.Data.SqlClient.xml
│       │       │   └── netstandard2.1
│       │       │       ├── de
│       │       │       │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │       ├── es
│       │       │       │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │       ├── fr
│       │       │       │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │       ├── it
│       │       │       │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │       ├── ja
│       │       │       │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │       ├── ko
│       │       │       │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │       ├── pt-BR
│       │       │       │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │       ├── ru
│       │       │       │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │       ├── zh-Hans
│       │       │       │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │       ├── zh-Hant
│       │       │       │   └── Microsoft.Data.SqlClient.resources.dll
│       │       │       ├── Microsoft.Data.SqlClient.dll
│       │       │       └── Microsoft.Data.SqlClient.xml
│       │       ├── ref
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Data.SqlClient.dll
│       │       │   │   └── Microsoft.Data.SqlClient.xml
│       │       │   ├── net6.0
│       │       │   │   ├── Microsoft.Data.SqlClient.dll
│       │       │   │   └── Microsoft.Data.SqlClient.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Data.SqlClient.dll
│       │       │   │   └── Microsoft.Data.SqlClient.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── Microsoft.Data.SqlClient.dll
│       │       │   │   └── Microsoft.Data.SqlClient.xml
│       │       │   └── netstandard2.1
│       │       │       ├── Microsoft.Data.SqlClient.dll
│       │       │       └── Microsoft.Data.SqlClient.xml
│       │       ├── runtimes
│       │       │   ├── unix
│       │       │   │   └── lib
│       │       │   │       ├── net6.0
│       │       │   │       │   └── Microsoft.Data.SqlClient.dll
│       │       │   │       ├── net8.0
│       │       │   │       │   └── Microsoft.Data.SqlClient.dll
│       │       │   │       ├── netstandard2.0
│       │       │   │       │   └── Microsoft.Data.SqlClient.dll
│       │       │   │       └── netstandard2.1
│       │       │   │           └── Microsoft.Data.SqlClient.dll
│       │       │   └── win
│       │       │       └── lib
│       │       │           ├── net462
│       │       │           │   └── Microsoft.Data.SqlClient.dll
│       │       │           ├── net6.0
│       │       │           │   └── Microsoft.Data.SqlClient.dll
│       │       │           ├── net8.0
│       │       │           │   └── Microsoft.Data.SqlClient.dll
│       │       │           ├── netstandard2.0
│       │       │           │   └── Microsoft.Data.SqlClient.dll
│       │       │           └── netstandard2.1
│       │       │               └── Microsoft.Data.SqlClient.dll
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── dotnet.png
│       │       ├── microsoft.data.sqlclient.5.2.2.nupkg
│       │       ├── microsoft.data.sqlclient.5.2.2.nupkg.sha512
│       │       └── microsoft.data.sqlclient.nuspec
│       ├── microsoft.data.sqlclient.sni.runtime
│       │   └── 5.2.0
│       │       ├── runtimes
│       │       │   ├── win-arm
│       │       │   │   └── native
│       │       │   │       └── Microsoft.Data.SqlClient.SNI.dll
│       │       │   ├── win-arm64
│       │       │   │   └── native
│       │       │   │       └── Microsoft.Data.SqlClient.SNI.dll
│       │       │   ├── win-x64
│       │       │   │   └── native
│       │       │   │       └── Microsoft.Data.SqlClient.SNI.dll
│       │       │   └── win-x86
│       │       │       └── native
│       │       │           └── Microsoft.Data.SqlClient.SNI.dll
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── dotnet.png
│       │       ├── LICENSE.txt
│       │       ├── microsoft.data.sqlclient.sni.runtime.5.2.0.nupkg
│       │       ├── microsoft.data.sqlclient.sni.runtime.5.2.0.nupkg.sha512
│       │       └── microsoft.data.sqlclient.sni.runtime.nuspec
│       ├── microsoft.extensions.apidescription.server
│       │   └── 10.0.0
│       │       ├── build
│       │       │   ├── Microsoft.Extensions.ApiDescription.Server.props
│       │       │   └── Microsoft.Extensions.ApiDescription.Server.targets
│       │       ├── buildMultiTargeting
│       │       │   ├── Microsoft.Extensions.ApiDescription.Server.props
│       │       │   └── Microsoft.Extensions.ApiDescription.Server.targets
│       │       ├── tools
│       │       │   ├── net10.0
│       │       │   │   ├── GetDocument.Insider.deps.json
│       │       │   │   ├── GetDocument.Insider.dll
│       │       │   │   ├── GetDocument.Insider.exe
│       │       │   │   ├── GetDocument.Insider.runtimeconfig.json
│       │       │   │   ├── Microsoft.AspNetCore.Connections.Abstractions.dll
│       │       │   │   ├── Microsoft.AspNetCore.Connections.Abstractions.xml
│       │       │   │   ├── Microsoft.AspNetCore.Hosting.Server.Abstractions.dll
│       │       │   │   ├── Microsoft.AspNetCore.Hosting.Server.Abstractions.xml
│       │       │   │   ├── Microsoft.AspNetCore.Http.Features.dll
│       │       │   │   ├── Microsoft.AspNetCore.Http.Features.xml
│       │       │   │   ├── Microsoft.Extensions.Configuration.Abstractions.dll
│       │       │   │   ├── Microsoft.Extensions.DependencyInjection.Abstractions.dll
│       │       │   │   ├── Microsoft.Extensions.Diagnostics.Abstractions.dll
│       │       │   │   ├── Microsoft.Extensions.Features.dll
│       │       │   │   ├── Microsoft.Extensions.Features.xml
│       │       │   │   ├── Microsoft.Extensions.FileProviders.Abstractions.dll
│       │       │   │   ├── Microsoft.Extensions.Hosting.Abstractions.dll
│       │       │   │   ├── Microsoft.Extensions.Logging.Abstractions.dll
│       │       │   │   ├── Microsoft.Extensions.Options.dll
│       │       │   │   ├── Microsoft.Extensions.Primitives.dll
│       │       │   │   ├── Microsoft.Net.Http.Headers.dll
│       │       │   │   ├── Microsoft.Net.Http.Headers.xml
│       │       │   │   └── Microsoft.OpenApi.dll
│       │       │   ├── net462
│       │       │   │   ├── GetDocument.Insider.exe
│       │       │   │   ├── GetDocument.Insider.exe.config
│       │       │   │   ├── Microsoft.Bcl.AsyncInterfaces.dll
│       │       │   │   ├── Microsoft.OpenApi.dll
│       │       │   │   ├── Microsoft.Win32.Primitives.dll
│       │       │   │   ├── netstandard.dll
│       │       │   │   ├── System.AppContext.dll
│       │       │   │   ├── System.Buffers.dll
│       │       │   │   ├── System.Collections.Concurrent.dll
│       │       │   │   ├── System.Collections.dll
│       │       │   │   ├── System.Collections.NonGeneric.dll
│       │       │   │   ├── System.Collections.Specialized.dll
│       │       │   │   ├── System.ComponentModel.dll
│       │       │   │   ├── System.ComponentModel.EventBasedAsync.dll
│       │       │   │   ├── System.ComponentModel.Primitives.dll
│       │       │   │   ├── System.ComponentModel.TypeConverter.dll
│       │       │   │   ├── System.Console.dll
│       │       │   │   ├── System.Data.Common.dll
│       │       │   │   ├── System.Diagnostics.Contracts.dll
│       │       │   │   ├── System.Diagnostics.Debug.dll
│       │       │   │   ├── System.Diagnostics.DiagnosticSource.dll
│       │       │   │   ├── System.Diagnostics.FileVersionInfo.dll
│       │       │   │   ├── System.Diagnostics.Process.dll
│       │       │   │   ├── System.Diagnostics.StackTrace.dll
│       │       │   │   ├── System.Diagnostics.TextWriterTraceListener.dll
│       │       │   │   ├── System.Diagnostics.Tools.dll
│       │       │   │   ├── System.Diagnostics.TraceSource.dll
│       │       │   │   ├── System.Diagnostics.Tracing.dll
│       │       │   │   ├── System.Drawing.Primitives.dll
│       │       │   │   ├── System.Dynamic.Runtime.dll
│       │       │   │   ├── System.Globalization.Calendars.dll
│       │       │   │   ├── System.Globalization.dll
│       │       │   │   ├── System.Globalization.Extensions.dll
│       │       │   │   ├── System.IO.Compression.dll
│       │       │   │   ├── System.IO.Compression.ZipFile.dll
│       │       │   │   ├── System.IO.dll
│       │       │   │   ├── System.IO.FileSystem.dll
│       │       │   │   ├── System.IO.FileSystem.DriveInfo.dll
│       │       │   │   ├── System.IO.FileSystem.Primitives.dll
│       │       │   │   ├── System.IO.FileSystem.Watcher.dll
│       │       │   │   ├── System.IO.IsolatedStorage.dll
│       │       │   │   ├── System.IO.MemoryMappedFiles.dll
│       │       │   │   ├── System.IO.Pipes.dll
│       │       │   │   ├── System.IO.UnmanagedMemoryStream.dll
│       │       │   │   ├── System.Linq.dll
│       │       │   │   ├── System.Linq.Expressions.dll
│       │       │   │   ├── System.Linq.Parallel.dll
│       │       │   │   ├── System.Linq.Queryable.dll
│       │       │   │   ├── System.Memory.dll
│       │       │   │   ├── System.Net.Http.dll
│       │       │   │   ├── System.Net.NameResolution.dll
│       │       │   │   ├── System.Net.NetworkInformation.dll
│       │       │   │   ├── System.Net.Ping.dll
│       │       │   │   ├── System.Net.Primitives.dll
│       │       │   │   ├── System.Net.Requests.dll
│       │       │   │   ├── System.Net.Security.dll
│       │       │   │   ├── System.Net.Sockets.dll
│       │       │   │   ├── System.Net.WebHeaderCollection.dll
│       │       │   │   ├── System.Net.WebSockets.Client.dll
│       │       │   │   ├── System.Net.WebSockets.dll
│       │       │   │   ├── System.Numerics.Vectors.dll
│       │       │   │   ├── System.ObjectModel.dll
│       │       │   │   ├── System.Reflection.dll
│       │       │   │   ├── System.Reflection.Extensions.dll
│       │       │   │   ├── System.Reflection.Primitives.dll
│       │       │   │   ├── System.Resources.Reader.dll
│       │       │   │   ├── System.Resources.ResourceManager.dll
│       │       │   │   ├── System.Resources.Writer.dll
│       │       │   │   ├── System.Runtime.CompilerServices.Unsafe.dll
│       │       │   │   ├── System.Runtime.CompilerServices.VisualC.dll
│       │       │   │   ├── System.Runtime.dll
│       │       │   │   ├── System.Runtime.Extensions.dll
│       │       │   │   ├── System.Runtime.Handles.dll
│       │       │   │   ├── System.Runtime.InteropServices.dll
│       │       │   │   ├── System.Runtime.InteropServices.RuntimeInformation.dll
│       │       │   │   ├── System.Runtime.Numerics.dll
│       │       │   │   ├── System.Runtime.Serialization.Formatters.dll
│       │       │   │   ├── System.Runtime.Serialization.Json.dll
│       │       │   │   ├── System.Runtime.Serialization.Primitives.dll
│       │       │   │   ├── System.Runtime.Serialization.Xml.dll
│       │       │   │   ├── System.Security.Claims.dll
│       │       │   │   ├── System.Security.Cryptography.Algorithms.dll
│       │       │   │   ├── System.Security.Cryptography.Csp.dll
│       │       │   │   ├── System.Security.Cryptography.Encoding.dll
│       │       │   │   ├── System.Security.Cryptography.Primitives.dll
│       │       │   │   ├── System.Security.Cryptography.X509Certificates.dll
│       │       │   │   ├── System.Security.Principal.dll
│       │       │   │   ├── System.Security.SecureString.dll
│       │       │   │   ├── System.Text.Encoding.dll
│       │       │   │   ├── System.Text.Encoding.Extensions.dll
│       │       │   │   ├── System.Text.Encodings.Web.dll
│       │       │   │   ├── System.Text.Json.dll
│       │       │   │   ├── System.Text.RegularExpressions.dll
│       │       │   │   ├── System.Threading.dll
│       │       │   │   ├── System.Threading.Overlapped.dll
│       │       │   │   ├── System.Threading.Tasks.dll
│       │       │   │   ├── System.Threading.Tasks.Extensions.dll
│       │       │   │   ├── System.Threading.Tasks.Parallel.dll
│       │       │   │   ├── System.Threading.Thread.dll
│       │       │   │   ├── System.Threading.ThreadPool.dll
│       │       │   │   ├── System.Threading.Timer.dll
│       │       │   │   ├── System.ValueTuple.dll
│       │       │   │   ├── System.Xml.ReaderWriter.dll
│       │       │   │   ├── System.Xml.XDocument.dll
│       │       │   │   ├── System.Xml.XmlDocument.dll
│       │       │   │   ├── System.Xml.XmlSerializer.dll
│       │       │   │   ├── System.Xml.XPath.dll
│       │       │   │   └── System.Xml.XPath.XDocument.dll
│       │       │   ├── net462-x86
│       │       │   │   ├── GetDocument.Insider.exe
│       │       │   │   ├── GetDocument.Insider.exe.config
│       │       │   │   ├── Microsoft.Bcl.AsyncInterfaces.dll
│       │       │   │   ├── Microsoft.OpenApi.dll
│       │       │   │   ├── Microsoft.Win32.Primitives.dll
│       │       │   │   ├── netstandard.dll
│       │       │   │   ├── System.AppContext.dll
│       │       │   │   ├── System.Buffers.dll
│       │       │   │   ├── System.Collections.Concurrent.dll
│       │       │   │   ├── System.Collections.dll
│       │       │   │   ├── System.Collections.NonGeneric.dll
│       │       │   │   ├── System.Collections.Specialized.dll
│       │       │   │   ├── System.ComponentModel.dll
│       │       │   │   ├── System.ComponentModel.EventBasedAsync.dll
│       │       │   │   ├── System.ComponentModel.Primitives.dll
│       │       │   │   ├── System.ComponentModel.TypeConverter.dll
│       │       │   │   ├── System.Console.dll
│       │       │   │   ├── System.Data.Common.dll
│       │       │   │   ├── System.Diagnostics.Contracts.dll
│       │       │   │   ├── System.Diagnostics.Debug.dll
│       │       │   │   ├── System.Diagnostics.DiagnosticSource.dll
│       │       │   │   ├── System.Diagnostics.FileVersionInfo.dll
│       │       │   │   ├── System.Diagnostics.Process.dll
│       │       │   │   ├── System.Diagnostics.StackTrace.dll
│       │       │   │   ├── System.Diagnostics.TextWriterTraceListener.dll
│       │       │   │   ├── System.Diagnostics.Tools.dll
│       │       │   │   ├── System.Diagnostics.TraceSource.dll
│       │       │   │   ├── System.Diagnostics.Tracing.dll
│       │       │   │   ├── System.Drawing.Primitives.dll
│       │       │   │   ├── System.Dynamic.Runtime.dll
│       │       │   │   ├── System.Globalization.Calendars.dll
│       │       │   │   ├── System.Globalization.dll
│       │       │   │   ├── System.Globalization.Extensions.dll
│       │       │   │   ├── System.IO.Compression.dll
│       │       │   │   ├── System.IO.Compression.ZipFile.dll
│       │       │   │   ├── System.IO.dll
│       │       │   │   ├── System.IO.FileSystem.dll
│       │       │   │   ├── System.IO.FileSystem.DriveInfo.dll
│       │       │   │   ├── System.IO.FileSystem.Primitives.dll
│       │       │   │   ├── System.IO.FileSystem.Watcher.dll
│       │       │   │   ├── System.IO.IsolatedStorage.dll
│       │       │   │   ├── System.IO.MemoryMappedFiles.dll
│       │       │   │   ├── System.IO.Pipes.dll
│       │       │   │   ├── System.IO.UnmanagedMemoryStream.dll
│       │       │   │   ├── System.Linq.dll
│       │       │   │   ├── System.Linq.Expressions.dll
│       │       │   │   ├── System.Linq.Parallel.dll
│       │       │   │   ├── System.Linq.Queryable.dll
│       │       │   │   ├── System.Memory.dll
│       │       │   │   ├── System.Net.Http.dll
│       │       │   │   ├── System.Net.NameResolution.dll
│       │       │   │   ├── System.Net.NetworkInformation.dll
│       │       │   │   ├── System.Net.Ping.dll
│       │       │   │   ├── System.Net.Primitives.dll
│       │       │   │   ├── System.Net.Requests.dll
│       │       │   │   ├── System.Net.Security.dll
│       │       │   │   ├── System.Net.Sockets.dll
│       │       │   │   ├── System.Net.WebHeaderCollection.dll
│       │       │   │   ├── System.Net.WebSockets.Client.dll
│       │       │   │   ├── System.Net.WebSockets.dll
│       │       │   │   ├── System.Numerics.Vectors.dll
│       │       │   │   ├── System.ObjectModel.dll
│       │       │   │   ├── System.Reflection.dll
│       │       │   │   ├── System.Reflection.Extensions.dll
│       │       │   │   ├── System.Reflection.Primitives.dll
│       │       │   │   ├── System.Resources.Reader.dll
│       │       │   │   ├── System.Resources.ResourceManager.dll
│       │       │   │   ├── System.Resources.Writer.dll
│       │       │   │   ├── System.Runtime.CompilerServices.Unsafe.dll
│       │       │   │   ├── System.Runtime.CompilerServices.VisualC.dll
│       │       │   │   ├── System.Runtime.dll
│       │       │   │   ├── System.Runtime.Extensions.dll
│       │       │   │   ├── System.Runtime.Handles.dll
│       │       │   │   ├── System.Runtime.InteropServices.dll
│       │       │   │   ├── System.Runtime.InteropServices.RuntimeInformation.dll
│       │       │   │   ├── System.Runtime.Numerics.dll
│       │       │   │   ├── System.Runtime.Serialization.Formatters.dll
│       │       │   │   ├── System.Runtime.Serialization.Json.dll
│       │       │   │   ├── System.Runtime.Serialization.Primitives.dll
│       │       │   │   ├── System.Runtime.Serialization.Xml.dll
│       │       │   │   ├── System.Security.Claims.dll
│       │       │   │   ├── System.Security.Cryptography.Algorithms.dll
│       │       │   │   ├── System.Security.Cryptography.Csp.dll
│       │       │   │   ├── System.Security.Cryptography.Encoding.dll
│       │       │   │   ├── System.Security.Cryptography.Primitives.dll
│       │       │   │   ├── System.Security.Cryptography.X509Certificates.dll
│       │       │   │   ├── System.Security.Principal.dll
│       │       │   │   ├── System.Security.SecureString.dll
│       │       │   │   ├── System.Text.Encoding.dll
│       │       │   │   ├── System.Text.Encoding.Extensions.dll
│       │       │   │   ├── System.Text.Encodings.Web.dll
│       │       │   │   ├── System.Text.Json.dll
│       │       │   │   ├── System.Text.RegularExpressions.dll
│       │       │   │   ├── System.Threading.dll
│       │       │   │   ├── System.Threading.Overlapped.dll
│       │       │   │   ├── System.Threading.Tasks.dll
│       │       │   │   ├── System.Threading.Tasks.Extensions.dll
│       │       │   │   ├── System.Threading.Tasks.Parallel.dll
│       │       │   │   ├── System.Threading.Thread.dll
│       │       │   │   ├── System.Threading.ThreadPool.dll
│       │       │   │   ├── System.Threading.Timer.dll
│       │       │   │   ├── System.ValueTuple.dll
│       │       │   │   ├── System.Xml.ReaderWriter.dll
│       │       │   │   ├── System.Xml.XDocument.dll
│       │       │   │   ├── System.Xml.XmlDocument.dll
│       │       │   │   ├── System.Xml.XmlSerializer.dll
│       │       │   │   ├── System.Xml.XPath.dll
│       │       │   │   └── System.Xml.XPath.XDocument.dll
│       │       │   ├── dotnet-getdocument.deps.json
│       │       │   ├── dotnet-getdocument.dll
│       │       │   ├── dotnet-getdocument.runtimeconfig.json
│       │       │   └── Newtonsoft.Json.dll
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.apidescription.server.10.0.0.nupkg
│       │       ├── microsoft.extensions.apidescription.server.10.0.0.nupkg.sha512
│       │       └── microsoft.extensions.apidescription.server.nuspec
│       ├── microsoft.extensions.caching.abstractions
│       │   └── 10.0.5
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Caching.Abstractions.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.Caching.Abstractions.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Caching.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.Caching.Abstractions.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Caching.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.Caching.Abstractions.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Caching.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.Caching.Abstractions.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Caching.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.Caching.Abstractions.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Caching.Abstractions.dll
│       │       │       └── Microsoft.Extensions.Caching.Abstractions.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.caching.abstractions.10.0.5.nupkg
│       │       ├── microsoft.extensions.caching.abstractions.10.0.5.nupkg.sha512
│       │       ├── microsoft.extensions.caching.abstractions.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.caching.memory
│       │   ├── 10.0.1
│       │   │   ├── buildTransitive
│       │   │   │   ├── net461
│       │   │   │   │   └── Microsoft.Extensions.Caching.Memory.targets
│       │   │   │   ├── net462
│       │   │   │   │   └── _._
│       │   │   │   ├── net8.0
│       │   │   │   │   └── _._
│       │   │   │   └── netcoreapp2.0
│       │   │   │       └── Microsoft.Extensions.Caching.Memory.targets
│       │   │   ├── lib
│       │   │   │   ├── net10.0
│       │   │   │   │   ├── Microsoft.Extensions.Caching.Memory.dll
│       │   │   │   │   └── Microsoft.Extensions.Caching.Memory.xml
│       │   │   │   ├── net462
│       │   │   │   │   ├── Microsoft.Extensions.Caching.Memory.dll
│       │   │   │   │   └── Microsoft.Extensions.Caching.Memory.xml
│       │   │   │   ├── net8.0
│       │   │   │   │   ├── Microsoft.Extensions.Caching.Memory.dll
│       │   │   │   │   └── Microsoft.Extensions.Caching.Memory.xml
│       │   │   │   ├── net9.0
│       │   │   │   │   ├── Microsoft.Extensions.Caching.Memory.dll
│       │   │   │   │   └── Microsoft.Extensions.Caching.Memory.xml
│       │   │   │   └── netstandard2.0
│       │   │   │       ├── Microsoft.Extensions.Caching.Memory.dll
│       │   │   │       └── Microsoft.Extensions.Caching.Memory.xml
│       │   │   ├── .nupkg.metadata
│       │   │   ├── .signature.p7s
│       │   │   ├── Icon.png
│       │   │   ├── microsoft.extensions.caching.memory.10.0.1.nupkg
│       │   │   ├── microsoft.extensions.caching.memory.10.0.1.nupkg.sha512
│       │   │   ├── microsoft.extensions.caching.memory.nuspec
│       │   │   ├── PACKAGE.md
│       │   │   ├── THIRD-PARTY-NOTICES.TXT
│       │   │   └── useSharedDesignerContext.txt
│       │   └── 10.0.5
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Caching.Memory.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.Caching.Memory.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Caching.Memory.dll
│       │       │   │   └── Microsoft.Extensions.Caching.Memory.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Caching.Memory.dll
│       │       │   │   └── Microsoft.Extensions.Caching.Memory.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Caching.Memory.dll
│       │       │   │   └── Microsoft.Extensions.Caching.Memory.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Caching.Memory.dll
│       │       │   │   └── Microsoft.Extensions.Caching.Memory.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Caching.Memory.dll
│       │       │       └── Microsoft.Extensions.Caching.Memory.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.caching.memory.10.0.5.nupkg
│       │       ├── microsoft.extensions.caching.memory.10.0.5.nupkg.sha512
│       │       ├── microsoft.extensions.caching.memory.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.caching.sqlserver
│       │   └── 10.0.5
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Caching.SqlServer.dll
│       │       │   │   └── Microsoft.Extensions.Caching.SqlServer.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Caching.SqlServer.dll
│       │       │   │   └── Microsoft.Extensions.Caching.SqlServer.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Caching.SqlServer.dll
│       │       │       └── Microsoft.Extensions.Caching.SqlServer.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.caching.sqlserver.10.0.5.nupkg
│       │       ├── microsoft.extensions.caching.sqlserver.10.0.5.nupkg.sha512
│       │       ├── microsoft.extensions.caching.sqlserver.nuspec
│       │       └── THIRD-PARTY-NOTICES.TXT
│       ├── microsoft.extensions.configuration
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Configuration.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.Configuration.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Configuration.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Configuration.dll
│       │       │       └── Microsoft.Extensions.Configuration.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.configuration.10.0.7.nupkg
│       │       ├── microsoft.extensions.configuration.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.configuration.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.configuration.abstractions
│       │   ├── 10.0.0
│       │   │   ├── buildTransitive
│       │   │   │   ├── net461
│       │   │   │   │   └── Microsoft.Extensions.Configuration.Abstractions.targets
│       │   │   │   ├── net462
│       │   │   │   │   └── _._
│       │   │   │   ├── net8.0
│       │   │   │   │   └── _._
│       │   │   │   └── netcoreapp2.0
│       │   │   │       └── Microsoft.Extensions.Configuration.Abstractions.targets
│       │   │   ├── lib
│       │   │   │   ├── net10.0
│       │   │   │   │   ├── Microsoft.Extensions.Configuration.Abstractions.dll
│       │   │   │   │   └── Microsoft.Extensions.Configuration.Abstractions.xml
│       │   │   │   ├── net462
│       │   │   │   │   ├── Microsoft.Extensions.Configuration.Abstractions.dll
│       │   │   │   │   └── Microsoft.Extensions.Configuration.Abstractions.xml
│       │   │   │   ├── net8.0
│       │   │   │   │   ├── Microsoft.Extensions.Configuration.Abstractions.dll
│       │   │   │   │   └── Microsoft.Extensions.Configuration.Abstractions.xml
│       │   │   │   ├── net9.0
│       │   │   │   │   ├── Microsoft.Extensions.Configuration.Abstractions.dll
│       │   │   │   │   └── Microsoft.Extensions.Configuration.Abstractions.xml
│       │   │   │   └── netstandard2.0
│       │   │   │       ├── Microsoft.Extensions.Configuration.Abstractions.dll
│       │   │   │       └── Microsoft.Extensions.Configuration.Abstractions.xml
│       │   │   ├── .nupkg.metadata
│       │   │   ├── .signature.p7s
│       │   │   ├── Icon.png
│       │   │   ├── microsoft.extensions.configuration.abstractions.10.0.0.nupkg
│       │   │   ├── microsoft.extensions.configuration.abstractions.10.0.0.nupkg.sha512
│       │   │   ├── microsoft.extensions.configuration.abstractions.nuspec
│       │   │   ├── PACKAGE.md
│       │   │   ├── THIRD-PARTY-NOTICES.TXT
│       │   │   └── useSharedDesignerContext.txt
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Configuration.Abstractions.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.Configuration.Abstractions.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.Abstractions.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Configuration.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.Abstractions.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.Abstractions.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.Abstractions.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Configuration.Abstractions.dll
│       │       │       └── Microsoft.Extensions.Configuration.Abstractions.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.configuration.abstractions.10.0.7.nupkg
│       │       ├── microsoft.extensions.configuration.abstractions.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.configuration.abstractions.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.configuration.binder
│       │   └── 10.0.7
│       │       ├── analyzers
│       │       │   └── dotnet
│       │       │       └── cs
│       │       │           ├── cs
│       │       │           │   └── Microsoft.Extensions.Configuration.Binder.SourceGeneration.resources.dll
│       │       │           ├── de
│       │       │           │   └── Microsoft.Extensions.Configuration.Binder.SourceGeneration.resources.dll
│       │       │           ├── es
│       │       │           │   └── Microsoft.Extensions.Configuration.Binder.SourceGeneration.resources.dll
│       │       │           ├── fr
│       │       │           │   └── Microsoft.Extensions.Configuration.Binder.SourceGeneration.resources.dll
│       │       │           ├── it
│       │       │           │   └── Microsoft.Extensions.Configuration.Binder.SourceGeneration.resources.dll
│       │       │           ├── ja
│       │       │           │   └── Microsoft.Extensions.Configuration.Binder.SourceGeneration.resources.dll
│       │       │           ├── ko
│       │       │           │   └── Microsoft.Extensions.Configuration.Binder.SourceGeneration.resources.dll
│       │       │           ├── pl
│       │       │           │   └── Microsoft.Extensions.Configuration.Binder.SourceGeneration.resources.dll
│       │       │           ├── pt-BR
│       │       │           │   └── Microsoft.Extensions.Configuration.Binder.SourceGeneration.resources.dll
│       │       │           ├── ru
│       │       │           │   └── Microsoft.Extensions.Configuration.Binder.SourceGeneration.resources.dll
│       │       │           ├── tr
│       │       │           │   └── Microsoft.Extensions.Configuration.Binder.SourceGeneration.resources.dll
│       │       │           ├── zh-Hans
│       │       │           │   └── Microsoft.Extensions.Configuration.Binder.SourceGeneration.resources.dll
│       │       │           ├── zh-Hant
│       │       │           │   └── Microsoft.Extensions.Configuration.Binder.SourceGeneration.resources.dll
│       │       │           └── Microsoft.Extensions.Configuration.Binder.SourceGeneration.dll
│       │       ├── buildTransitive
│       │       │   └── netstandard2.0
│       │       │       └── Microsoft.Extensions.Configuration.Binder.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.Binder.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.Binder.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Configuration.Binder.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.Binder.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.Binder.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.Binder.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.Binder.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.Binder.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Configuration.Binder.dll
│       │       │       └── Microsoft.Extensions.Configuration.Binder.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.configuration.binder.10.0.7.nupkg
│       │       ├── microsoft.extensions.configuration.binder.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.configuration.binder.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.configuration.commandline
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Configuration.CommandLine.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.Configuration.CommandLine.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.CommandLine.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.CommandLine.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Configuration.CommandLine.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.CommandLine.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.CommandLine.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.CommandLine.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.CommandLine.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.CommandLine.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Configuration.CommandLine.dll
│       │       │       └── Microsoft.Extensions.Configuration.CommandLine.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.configuration.commandline.10.0.7.nupkg
│       │       ├── microsoft.extensions.configuration.commandline.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.configuration.commandline.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.configuration.environmentvariables
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Configuration.EnvironmentVariables.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.Configuration.EnvironmentVariables.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.EnvironmentVariables.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.EnvironmentVariables.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Configuration.EnvironmentVariables.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.EnvironmentVariables.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.EnvironmentVariables.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.EnvironmentVariables.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.EnvironmentVariables.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.EnvironmentVariables.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Configuration.EnvironmentVariables.dll
│       │       │       └── Microsoft.Extensions.Configuration.EnvironmentVariables.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.configuration.environmentvariables.10.0.7.nupkg
│       │       ├── microsoft.extensions.configuration.environmentvariables.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.configuration.environmentvariables.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.configuration.fileextensions
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Configuration.FileExtensions.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.Configuration.FileExtensions.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.FileExtensions.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.FileExtensions.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Configuration.FileExtensions.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.FileExtensions.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.FileExtensions.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.FileExtensions.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.FileExtensions.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.FileExtensions.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Configuration.FileExtensions.dll
│       │       │       └── Microsoft.Extensions.Configuration.FileExtensions.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.configuration.fileextensions.10.0.7.nupkg
│       │       ├── microsoft.extensions.configuration.fileextensions.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.configuration.fileextensions.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.configuration.json
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Configuration.Json.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.Configuration.Json.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.Json.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.Json.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Configuration.Json.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.Json.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.Json.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.Json.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.Json.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.Json.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.Json.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.Json.xml
│       │       │   └── netstandard2.1
│       │       │       ├── Microsoft.Extensions.Configuration.Json.dll
│       │       │       └── Microsoft.Extensions.Configuration.Json.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.configuration.json.10.0.7.nupkg
│       │       ├── microsoft.extensions.configuration.json.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.configuration.json.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.configuration.usersecrets
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Configuration.UserSecrets.targets
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Configuration.UserSecrets.props
│       │       │   │   └── Microsoft.Extensions.Configuration.UserSecrets.targets
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.UserSecrets.props
│       │       │   │   └── Microsoft.Extensions.Configuration.UserSecrets.targets
│       │       │   ├── netcoreapp2.0
│       │       │   │   └── Microsoft.Extensions.Configuration.UserSecrets.targets
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Configuration.UserSecrets.props
│       │       │       └── Microsoft.Extensions.Configuration.UserSecrets.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.UserSecrets.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.UserSecrets.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Configuration.UserSecrets.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.UserSecrets.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.UserSecrets.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.UserSecrets.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Configuration.UserSecrets.dll
│       │       │   │   └── Microsoft.Extensions.Configuration.UserSecrets.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Configuration.UserSecrets.dll
│       │       │       └── Microsoft.Extensions.Configuration.UserSecrets.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.configuration.usersecrets.10.0.7.nupkg
│       │       ├── microsoft.extensions.configuration.usersecrets.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.configuration.usersecrets.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.dependencyinjection
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.DependencyInjection.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.DependencyInjection.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.DependencyInjection.dll
│       │       │   │   └── Microsoft.Extensions.DependencyInjection.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.DependencyInjection.dll
│       │       │   │   └── Microsoft.Extensions.DependencyInjection.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.DependencyInjection.dll
│       │       │   │   └── Microsoft.Extensions.DependencyInjection.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.DependencyInjection.dll
│       │       │   │   └── Microsoft.Extensions.DependencyInjection.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── Microsoft.Extensions.DependencyInjection.dll
│       │       │   │   └── Microsoft.Extensions.DependencyInjection.xml
│       │       │   └── netstandard2.1
│       │       │       ├── Microsoft.Extensions.DependencyInjection.dll
│       │       │       └── Microsoft.Extensions.DependencyInjection.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.dependencyinjection.10.0.7.nupkg
│       │       ├── microsoft.extensions.dependencyinjection.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.dependencyinjection.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.dependencyinjection.abstractions
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.DependencyInjection.Abstractions.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.DependencyInjection.Abstractions.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.DependencyInjection.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.DependencyInjection.Abstractions.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.DependencyInjection.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.DependencyInjection.Abstractions.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.DependencyInjection.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.DependencyInjection.Abstractions.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.DependencyInjection.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.DependencyInjection.Abstractions.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── Microsoft.Extensions.DependencyInjection.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.DependencyInjection.Abstractions.xml
│       │       │   └── netstandard2.1
│       │       │       ├── Microsoft.Extensions.DependencyInjection.Abstractions.dll
│       │       │       └── Microsoft.Extensions.DependencyInjection.Abstractions.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.dependencyinjection.abstractions.10.0.7.nupkg
│       │       ├── microsoft.extensions.dependencyinjection.abstractions.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.dependencyinjection.abstractions.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.dependencymodel
│       │   └── 10.0.0
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.DependencyModel.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.DependencyModel.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.DependencyModel.dll
│       │       │   │   └── Microsoft.Extensions.DependencyModel.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.DependencyModel.dll
│       │       │   │   └── Microsoft.Extensions.DependencyModel.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.DependencyModel.dll
│       │       │   │   └── Microsoft.Extensions.DependencyModel.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.DependencyModel.dll
│       │       │   │   └── Microsoft.Extensions.DependencyModel.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.DependencyModel.dll
│       │       │       └── Microsoft.Extensions.DependencyModel.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.dependencymodel.10.0.0.nupkg
│       │       ├── microsoft.extensions.dependencymodel.10.0.0.nupkg.sha512
│       │       ├── microsoft.extensions.dependencymodel.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.diagnostics
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Diagnostics.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.Diagnostics.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Diagnostics.dll
│       │       │   │   └── Microsoft.Extensions.Diagnostics.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Diagnostics.dll
│       │       │   │   └── Microsoft.Extensions.Diagnostics.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Diagnostics.dll
│       │       │   │   └── Microsoft.Extensions.Diagnostics.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Diagnostics.dll
│       │       │   │   └── Microsoft.Extensions.Diagnostics.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Diagnostics.dll
│       │       │       └── Microsoft.Extensions.Diagnostics.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.diagnostics.10.0.7.nupkg
│       │       ├── microsoft.extensions.diagnostics.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.diagnostics.nuspec
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.diagnostics.abstractions
│       │   ├── 10.0.0
│       │   │   ├── buildTransitive
│       │   │   │   ├── net461
│       │   │   │   │   └── Microsoft.Extensions.Diagnostics.Abstractions.targets
│       │   │   │   ├── net462
│       │   │   │   │   └── _._
│       │   │   │   ├── net8.0
│       │   │   │   │   └── _._
│       │   │   │   └── netcoreapp2.0
│       │   │   │       └── Microsoft.Extensions.Diagnostics.Abstractions.targets
│       │   │   ├── lib
│       │   │   │   ├── net10.0
│       │   │   │   │   ├── Microsoft.Extensions.Diagnostics.Abstractions.dll
│       │   │   │   │   └── Microsoft.Extensions.Diagnostics.Abstractions.xml
│       │   │   │   ├── net462
│       │   │   │   │   ├── Microsoft.Extensions.Diagnostics.Abstractions.dll
│       │   │   │   │   └── Microsoft.Extensions.Diagnostics.Abstractions.xml
│       │   │   │   ├── net8.0
│       │   │   │   │   ├── Microsoft.Extensions.Diagnostics.Abstractions.dll
│       │   │   │   │   └── Microsoft.Extensions.Diagnostics.Abstractions.xml
│       │   │   │   ├── net9.0
│       │   │   │   │   ├── Microsoft.Extensions.Diagnostics.Abstractions.dll
│       │   │   │   │   └── Microsoft.Extensions.Diagnostics.Abstractions.xml
│       │   │   │   └── netstandard2.0
│       │   │   │       ├── Microsoft.Extensions.Diagnostics.Abstractions.dll
│       │   │   │       └── Microsoft.Extensions.Diagnostics.Abstractions.xml
│       │   │   ├── .nupkg.metadata
│       │   │   ├── .signature.p7s
│       │   │   ├── Icon.png
│       │   │   ├── microsoft.extensions.diagnostics.abstractions.10.0.0.nupkg
│       │   │   ├── microsoft.extensions.diagnostics.abstractions.10.0.0.nupkg.sha512
│       │   │   ├── microsoft.extensions.diagnostics.abstractions.nuspec
│       │   │   ├── THIRD-PARTY-NOTICES.TXT
│       │   │   └── useSharedDesignerContext.txt
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Diagnostics.Abstractions.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.Diagnostics.Abstractions.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Diagnostics.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.Diagnostics.Abstractions.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Diagnostics.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.Diagnostics.Abstractions.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Diagnostics.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.Diagnostics.Abstractions.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Diagnostics.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.Diagnostics.Abstractions.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Diagnostics.Abstractions.dll
│       │       │       └── Microsoft.Extensions.Diagnostics.Abstractions.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.diagnostics.abstractions.10.0.7.nupkg
│       │       ├── microsoft.extensions.diagnostics.abstractions.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.diagnostics.abstractions.nuspec
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.fileproviders.abstractions
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.FileProviders.Abstractions.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.FileProviders.Abstractions.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.FileProviders.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.FileProviders.Abstractions.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.FileProviders.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.FileProviders.Abstractions.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.FileProviders.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.FileProviders.Abstractions.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.FileProviders.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.FileProviders.Abstractions.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.FileProviders.Abstractions.dll
│       │       │       └── Microsoft.Extensions.FileProviders.Abstractions.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.fileproviders.abstractions.10.0.7.nupkg
│       │       ├── microsoft.extensions.fileproviders.abstractions.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.fileproviders.abstractions.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.fileproviders.physical
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.FileProviders.Physical.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.FileProviders.Physical.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.FileProviders.Physical.dll
│       │       │   │   └── Microsoft.Extensions.FileProviders.Physical.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.FileProviders.Physical.dll
│       │       │   │   └── Microsoft.Extensions.FileProviders.Physical.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.FileProviders.Physical.dll
│       │       │   │   └── Microsoft.Extensions.FileProviders.Physical.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.FileProviders.Physical.dll
│       │       │   │   └── Microsoft.Extensions.FileProviders.Physical.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.FileProviders.Physical.dll
│       │       │       └── Microsoft.Extensions.FileProviders.Physical.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.fileproviders.physical.10.0.7.nupkg
│       │       ├── microsoft.extensions.fileproviders.physical.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.fileproviders.physical.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.filesystemglobbing
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.FileSystemGlobbing.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.FileSystemGlobbing.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.FileSystemGlobbing.dll
│       │       │   │   └── Microsoft.Extensions.FileSystemGlobbing.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.FileSystemGlobbing.dll
│       │       │   │   └── Microsoft.Extensions.FileSystemGlobbing.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.FileSystemGlobbing.dll
│       │       │   │   └── Microsoft.Extensions.FileSystemGlobbing.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.FileSystemGlobbing.dll
│       │       │   │   └── Microsoft.Extensions.FileSystemGlobbing.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.FileSystemGlobbing.dll
│       │       │       └── Microsoft.Extensions.FileSystemGlobbing.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.filesystemglobbing.10.0.7.nupkg
│       │       ├── microsoft.extensions.filesystemglobbing.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.filesystemglobbing.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.hosting
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Hosting.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.Hosting.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Hosting.dll
│       │       │   │   └── Microsoft.Extensions.Hosting.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Hosting.dll
│       │       │   │   └── Microsoft.Extensions.Hosting.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Hosting.dll
│       │       │   │   └── Microsoft.Extensions.Hosting.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Hosting.dll
│       │       │   │   └── Microsoft.Extensions.Hosting.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── Microsoft.Extensions.Hosting.dll
│       │       │   │   └── Microsoft.Extensions.Hosting.xml
│       │       │   └── netstandard2.1
│       │       │       ├── Microsoft.Extensions.Hosting.dll
│       │       │       └── Microsoft.Extensions.Hosting.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.hosting.10.0.7.nupkg
│       │       ├── microsoft.extensions.hosting.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.hosting.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.hosting.abstractions
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Hosting.Abstractions.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.Hosting.Abstractions.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Hosting.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.Hosting.Abstractions.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Hosting.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.Hosting.Abstractions.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Hosting.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.Hosting.Abstractions.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Hosting.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.Hosting.Abstractions.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── Microsoft.Extensions.Hosting.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.Hosting.Abstractions.xml
│       │       │   └── netstandard2.1
│       │       │       ├── Microsoft.Extensions.Hosting.Abstractions.dll
│       │       │       └── Microsoft.Extensions.Hosting.Abstractions.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.hosting.abstractions.10.0.7.nupkg
│       │       ├── microsoft.extensions.hosting.abstractions.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.hosting.abstractions.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.http
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Http.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.Http.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Http.dll
│       │       │   │   └── Microsoft.Extensions.Http.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Http.dll
│       │       │   │   └── Microsoft.Extensions.Http.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Http.dll
│       │       │   │   └── Microsoft.Extensions.Http.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Http.dll
│       │       │   │   └── Microsoft.Extensions.Http.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Http.dll
│       │       │       └── Microsoft.Extensions.Http.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.http.10.0.7.nupkg
│       │       ├── microsoft.extensions.http.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.http.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.http.polly
│       │   └── 10.0.7
│       │       ├── lib
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Http.Polly.dll
│       │       │       └── Microsoft.Extensions.Http.Polly.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.http.polly.10.0.7.nupkg
│       │       ├── microsoft.extensions.http.polly.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.http.polly.nuspec
│       │       ├── PACKAGE.md
│       │       └── THIRD-PARTY-NOTICES.TXT
│       ├── microsoft.extensions.logging
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Logging.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.Logging.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Logging.dll
│       │       │   │   └── Microsoft.Extensions.Logging.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Logging.dll
│       │       │   │   └── Microsoft.Extensions.Logging.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Logging.dll
│       │       │   │   └── Microsoft.Extensions.Logging.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Logging.dll
│       │       │   │   └── Microsoft.Extensions.Logging.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── Microsoft.Extensions.Logging.dll
│       │       │   │   └── Microsoft.Extensions.Logging.xml
│       │       │   └── netstandard2.1
│       │       │       ├── Microsoft.Extensions.Logging.dll
│       │       │       └── Microsoft.Extensions.Logging.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.logging.10.0.7.nupkg
│       │       ├── microsoft.extensions.logging.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.logging.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.logging.abstractions
│       │   └── 10.0.7
│       │       ├── analyzers
│       │       │   └── dotnet
│       │       │       ├── roslyn3.11
│       │       │       │   └── cs
│       │       │       │       ├── cs
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── de
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── es
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── fr
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── it
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── ja
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── ko
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── pl
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── pt-BR
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── ru
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── tr
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── zh-Hans
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── zh-Hant
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       └── Microsoft.Extensions.Logging.Generators.dll
│       │       │       ├── roslyn4.0
│       │       │       │   └── cs
│       │       │       │       ├── cs
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── de
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── es
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── fr
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── it
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── ja
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── ko
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── pl
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── pt-BR
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── ru
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── tr
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── zh-Hans
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       ├── zh-Hant
│       │       │       │       │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │       │       └── Microsoft.Extensions.Logging.Generators.dll
│       │       │       └── roslyn4.4
│       │       │           └── cs
│       │       │               ├── cs
│       │       │               │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │               ├── de
│       │       │               │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │               ├── es
│       │       │               │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │               ├── fr
│       │       │               │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │               ├── it
│       │       │               │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │               ├── ja
│       │       │               │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │               ├── ko
│       │       │               │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │               ├── pl
│       │       │               │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │               ├── pt-BR
│       │       │               │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │               ├── ru
│       │       │               │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │               ├── tr
│       │       │               │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │               ├── zh-Hans
│       │       │               │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │               ├── zh-Hant
│       │       │               │   └── Microsoft.Extensions.Logging.Generators.resources.dll
│       │       │               └── Microsoft.Extensions.Logging.Generators.dll
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Logging.Abstractions.targets
│       │       │   ├── net462
│       │       │   │   └── Microsoft.Extensions.Logging.Abstractions.targets
│       │       │   ├── net8.0
│       │       │   │   └── Microsoft.Extensions.Logging.Abstractions.targets
│       │       │   ├── netcoreapp2.0
│       │       │   │   └── Microsoft.Extensions.Logging.Abstractions.targets
│       │       │   └── netstandard2.0
│       │       │       └── Microsoft.Extensions.Logging.Abstractions.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Logging.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.Logging.Abstractions.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Logging.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.Logging.Abstractions.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Logging.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.Logging.Abstractions.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Logging.Abstractions.dll
│       │       │   │   └── Microsoft.Extensions.Logging.Abstractions.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Logging.Abstractions.dll
│       │       │       └── Microsoft.Extensions.Logging.Abstractions.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.logging.abstractions.10.0.7.nupkg
│       │       ├── microsoft.extensions.logging.abstractions.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.logging.abstractions.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.logging.configuration
│       │   ├── 10.0.0
│       │   │   ├── buildTransitive
│       │   │   │   ├── net461
│       │   │   │   │   └── Microsoft.Extensions.Logging.Configuration.targets
│       │   │   │   ├── net462
│       │   │   │   │   └── _._
│       │   │   │   ├── net8.0
│       │   │   │   │   └── _._
│       │   │   │   └── netcoreapp2.0
│       │   │   │       └── Microsoft.Extensions.Logging.Configuration.targets
│       │   │   ├── lib
│       │   │   │   ├── net10.0
│       │   │   │   │   ├── Microsoft.Extensions.Logging.Configuration.dll
│       │   │   │   │   └── Microsoft.Extensions.Logging.Configuration.xml
│       │   │   │   ├── net462
│       │   │   │   │   ├── Microsoft.Extensions.Logging.Configuration.dll
│       │   │   │   │   └── Microsoft.Extensions.Logging.Configuration.xml
│       │   │   │   ├── net8.0
│       │   │   │   │   ├── Microsoft.Extensions.Logging.Configuration.dll
│       │   │   │   │   └── Microsoft.Extensions.Logging.Configuration.xml
│       │   │   │   ├── net9.0
│       │   │   │   │   ├── Microsoft.Extensions.Logging.Configuration.dll
│       │   │   │   │   └── Microsoft.Extensions.Logging.Configuration.xml
│       │   │   │   └── netstandard2.0
│       │   │   │       ├── Microsoft.Extensions.Logging.Configuration.dll
│       │   │   │       └── Microsoft.Extensions.Logging.Configuration.xml
│       │   │   ├── .nupkg.metadata
│       │   │   ├── .signature.p7s
│       │   │   ├── Icon.png
│       │   │   ├── microsoft.extensions.logging.configuration.10.0.0.nupkg
│       │   │   ├── microsoft.extensions.logging.configuration.10.0.0.nupkg.sha512
│       │   │   ├── microsoft.extensions.logging.configuration.nuspec
│       │   │   ├── THIRD-PARTY-NOTICES.TXT
│       │   │   └── useSharedDesignerContext.txt
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Logging.Configuration.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.Logging.Configuration.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Logging.Configuration.dll
│       │       │   │   └── Microsoft.Extensions.Logging.Configuration.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Logging.Configuration.dll
│       │       │   │   └── Microsoft.Extensions.Logging.Configuration.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Logging.Configuration.dll
│       │       │   │   └── Microsoft.Extensions.Logging.Configuration.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Logging.Configuration.dll
│       │       │   │   └── Microsoft.Extensions.Logging.Configuration.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Logging.Configuration.dll
│       │       │       └── Microsoft.Extensions.Logging.Configuration.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.logging.configuration.10.0.7.nupkg
│       │       ├── microsoft.extensions.logging.configuration.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.logging.configuration.nuspec
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.logging.console
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Logging.Console.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.Logging.Console.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Logging.Console.dll
│       │       │   │   └── Microsoft.Extensions.Logging.Console.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Logging.Console.dll
│       │       │   │   └── Microsoft.Extensions.Logging.Console.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Logging.Console.dll
│       │       │   │   └── Microsoft.Extensions.Logging.Console.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Logging.Console.dll
│       │       │   │   └── Microsoft.Extensions.Logging.Console.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Logging.Console.dll
│       │       │       └── Microsoft.Extensions.Logging.Console.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.logging.console.10.0.7.nupkg
│       │       ├── microsoft.extensions.logging.console.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.logging.console.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.logging.debug
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Logging.Debug.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.Logging.Debug.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Logging.Debug.dll
│       │       │   │   └── Microsoft.Extensions.Logging.Debug.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Logging.Debug.dll
│       │       │   │   └── Microsoft.Extensions.Logging.Debug.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Logging.Debug.dll
│       │       │   │   └── Microsoft.Extensions.Logging.Debug.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Logging.Debug.dll
│       │       │   │   └── Microsoft.Extensions.Logging.Debug.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Logging.Debug.dll
│       │       │       └── Microsoft.Extensions.Logging.Debug.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.logging.debug.10.0.7.nupkg
│       │       ├── microsoft.extensions.logging.debug.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.logging.debug.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.logging.eventlog
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Logging.EventLog.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.Logging.EventLog.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Logging.EventLog.dll
│       │       │   │   └── Microsoft.Extensions.Logging.EventLog.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Logging.EventLog.dll
│       │       │   │   └── Microsoft.Extensions.Logging.EventLog.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Logging.EventLog.dll
│       │       │   │   └── Microsoft.Extensions.Logging.EventLog.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Logging.EventLog.dll
│       │       │   │   └── Microsoft.Extensions.Logging.EventLog.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Logging.EventLog.dll
│       │       │       └── Microsoft.Extensions.Logging.EventLog.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.logging.eventlog.10.0.7.nupkg
│       │       ├── microsoft.extensions.logging.eventlog.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.logging.eventlog.nuspec
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.logging.eventsource
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Logging.EventSource.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.Logging.EventSource.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Logging.EventSource.dll
│       │       │   │   └── Microsoft.Extensions.Logging.EventSource.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Logging.EventSource.dll
│       │       │   │   └── Microsoft.Extensions.Logging.EventSource.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Logging.EventSource.dll
│       │       │   │   └── Microsoft.Extensions.Logging.EventSource.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Logging.EventSource.dll
│       │       │   │   └── Microsoft.Extensions.Logging.EventSource.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Logging.EventSource.dll
│       │       │       └── Microsoft.Extensions.Logging.EventSource.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.logging.eventsource.10.0.7.nupkg
│       │       ├── microsoft.extensions.logging.eventsource.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.logging.eventsource.nuspec
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.objectpool
│       │   └── 10.0.7
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.ObjectPool.dll
│       │       │   │   └── Microsoft.Extensions.ObjectPool.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.ObjectPool.dll
│       │       │   │   └── Microsoft.Extensions.ObjectPool.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.ObjectPool.dll
│       │       │       └── Microsoft.Extensions.ObjectPool.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.objectpool.10.0.7.nupkg
│       │       ├── microsoft.extensions.objectpool.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.objectpool.nuspec
│       │       └── THIRD-PARTY-NOTICES.TXT
│       ├── microsoft.extensions.options
│       │   └── 10.0.7
│       │       ├── analyzers
│       │       │   └── dotnet
│       │       │       └── roslyn4.4
│       │       │           └── cs
│       │       │               ├── cs
│       │       │               │   └── Microsoft.Extensions.Options.SourceGeneration.resources.dll
│       │       │               ├── de
│       │       │               │   └── Microsoft.Extensions.Options.SourceGeneration.resources.dll
│       │       │               ├── es
│       │       │               │   └── Microsoft.Extensions.Options.SourceGeneration.resources.dll
│       │       │               ├── fr
│       │       │               │   └── Microsoft.Extensions.Options.SourceGeneration.resources.dll
│       │       │               ├── it
│       │       │               │   └── Microsoft.Extensions.Options.SourceGeneration.resources.dll
│       │       │               ├── ja
│       │       │               │   └── Microsoft.Extensions.Options.SourceGeneration.resources.dll
│       │       │               ├── ko
│       │       │               │   └── Microsoft.Extensions.Options.SourceGeneration.resources.dll
│       │       │               ├── pl
│       │       │               │   └── Microsoft.Extensions.Options.SourceGeneration.resources.dll
│       │       │               ├── pt-BR
│       │       │               │   └── Microsoft.Extensions.Options.SourceGeneration.resources.dll
│       │       │               ├── ru
│       │       │               │   └── Microsoft.Extensions.Options.SourceGeneration.resources.dll
│       │       │               ├── tr
│       │       │               │   └── Microsoft.Extensions.Options.SourceGeneration.resources.dll
│       │       │               ├── zh-Hans
│       │       │               │   └── Microsoft.Extensions.Options.SourceGeneration.resources.dll
│       │       │               ├── zh-Hant
│       │       │               │   └── Microsoft.Extensions.Options.SourceGeneration.resources.dll
│       │       │               └── Microsoft.Extensions.Options.SourceGeneration.dll
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Options.targets
│       │       │   ├── net462
│       │       │   │   └── Microsoft.Extensions.Options.targets
│       │       │   ├── net8.0
│       │       │   │   └── Microsoft.Extensions.Options.targets
│       │       │   ├── netcoreapp2.0
│       │       │   │   └── Microsoft.Extensions.Options.targets
│       │       │   └── netstandard2.0
│       │       │       └── Microsoft.Extensions.Options.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Options.dll
│       │       │   │   └── Microsoft.Extensions.Options.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Options.dll
│       │       │   │   └── Microsoft.Extensions.Options.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Options.dll
│       │       │   │   └── Microsoft.Extensions.Options.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Options.dll
│       │       │   │   └── Microsoft.Extensions.Options.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── Microsoft.Extensions.Options.dll
│       │       │   │   └── Microsoft.Extensions.Options.xml
│       │       │   └── netstandard2.1
│       │       │       ├── Microsoft.Extensions.Options.dll
│       │       │       └── Microsoft.Extensions.Options.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.options.10.0.7.nupkg
│       │       ├── microsoft.extensions.options.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.options.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.options.configurationextensions
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Options.ConfigurationExtensions.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.Options.ConfigurationExtensions.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Options.ConfigurationExtensions.dll
│       │       │   │   └── Microsoft.Extensions.Options.ConfigurationExtensions.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Options.ConfigurationExtensions.dll
│       │       │   │   └── Microsoft.Extensions.Options.ConfigurationExtensions.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Options.ConfigurationExtensions.dll
│       │       │   │   └── Microsoft.Extensions.Options.ConfigurationExtensions.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Options.ConfigurationExtensions.dll
│       │       │   │   └── Microsoft.Extensions.Options.ConfigurationExtensions.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Options.ConfigurationExtensions.dll
│       │       │       └── Microsoft.Extensions.Options.ConfigurationExtensions.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.options.configurationextensions.10.0.7.nupkg
│       │       ├── microsoft.extensions.options.configurationextensions.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.options.configurationextensions.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.extensions.primitives
│       │   ├── 10.0.5
│       │   │   ├── buildTransitive
│       │   │   │   ├── net461
│       │   │   │   │   └── Microsoft.Extensions.Primitives.targets
│       │   │   │   ├── net462
│       │   │   │   │   └── _._
│       │   │   │   ├── net8.0
│       │   │   │   │   └── _._
│       │   │   │   └── netcoreapp2.0
│       │   │   │       └── Microsoft.Extensions.Primitives.targets
│       │   │   ├── lib
│       │   │   │   ├── net10.0
│       │   │   │   │   ├── Microsoft.Extensions.Primitives.dll
│       │   │   │   │   └── Microsoft.Extensions.Primitives.xml
│       │   │   │   ├── net462
│       │   │   │   │   ├── Microsoft.Extensions.Primitives.dll
│       │   │   │   │   └── Microsoft.Extensions.Primitives.xml
│       │   │   │   ├── net8.0
│       │   │   │   │   ├── Microsoft.Extensions.Primitives.dll
│       │   │   │   │   └── Microsoft.Extensions.Primitives.xml
│       │   │   │   ├── net9.0
│       │   │   │   │   ├── Microsoft.Extensions.Primitives.dll
│       │   │   │   │   └── Microsoft.Extensions.Primitives.xml
│       │   │   │   └── netstandard2.0
│       │   │   │       ├── Microsoft.Extensions.Primitives.dll
│       │   │   │       └── Microsoft.Extensions.Primitives.xml
│       │   │   ├── .nupkg.metadata
│       │   │   ├── .signature.p7s
│       │   │   ├── Icon.png
│       │   │   ├── microsoft.extensions.primitives.10.0.5.nupkg
│       │   │   ├── microsoft.extensions.primitives.10.0.5.nupkg.sha512
│       │   │   ├── microsoft.extensions.primitives.nuspec
│       │   │   ├── PACKAGE.md
│       │   │   ├── THIRD-PARTY-NOTICES.TXT
│       │   │   └── useSharedDesignerContext.txt
│       │   └── 10.0.7
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── Microsoft.Extensions.Primitives.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── Microsoft.Extensions.Primitives.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Microsoft.Extensions.Primitives.dll
│       │       │   │   └── Microsoft.Extensions.Primitives.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Extensions.Primitives.dll
│       │       │   │   └── Microsoft.Extensions.Primitives.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Extensions.Primitives.dll
│       │       │   │   └── Microsoft.Extensions.Primitives.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Extensions.Primitives.dll
│       │       │   │   └── Microsoft.Extensions.Primitives.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Extensions.Primitives.dll
│       │       │       └── Microsoft.Extensions.Primitives.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.extensions.primitives.10.0.7.nupkg
│       │       ├── microsoft.extensions.primitives.10.0.7.nupkg.sha512
│       │       ├── microsoft.extensions.primitives.nuspec
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.identity.client
│       │   └── 4.61.3
│       │       ├── lib
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.Identity.Client.dll
│       │       │   │   └── Microsoft.Identity.Client.xml
│       │       │   ├── net6.0
│       │       │   │   ├── Microsoft.Identity.Client.dll
│       │       │   │   └── Microsoft.Identity.Client.xml
│       │       │   ├── net6.0-android31.0
│       │       │   │   ├── Microsoft.Identity.Client.dll
│       │       │   │   └── Microsoft.Identity.Client.xml
│       │       │   ├── net6.0-ios15.4
│       │       │   │   ├── Microsoft.Identity.Client.dll
│       │       │   │   └── Microsoft.Identity.Client.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Identity.Client.dll
│       │       │       └── Microsoft.Identity.Client.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── microsoft.identity.client.4.61.3.nupkg
│       │       ├── microsoft.identity.client.4.61.3.nupkg.sha512
│       │       ├── microsoft.identity.client.nuspec
│       │       └── README.md
│       ├── microsoft.identity.client.extensions.msal
│       │   └── 4.61.3
│       │       ├── lib
│       │       │   ├── net6.0
│       │       │   │   ├── Microsoft.Identity.Client.Extensions.Msal.dll
│       │       │   │   └── Microsoft.Identity.Client.Extensions.Msal.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Identity.Client.Extensions.Msal.dll
│       │       │       └── Microsoft.Identity.Client.Extensions.Msal.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── microsoft.identity.client.extensions.msal.4.61.3.nupkg
│       │       ├── microsoft.identity.client.extensions.msal.4.61.3.nupkg.sha512
│       │       └── microsoft.identity.client.extensions.msal.nuspec
│       ├── microsoft.identitymodel.abstractions
│       │   └── 6.35.0
│       │       ├── lib
│       │       │   ├── net45
│       │       │   │   ├── Microsoft.IdentityModel.Abstractions.dll
│       │       │   │   └── Microsoft.IdentityModel.Abstractions.xml
│       │       │   ├── net461
│       │       │   │   ├── Microsoft.IdentityModel.Abstractions.dll
│       │       │   │   └── Microsoft.IdentityModel.Abstractions.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.IdentityModel.Abstractions.dll
│       │       │   │   └── Microsoft.IdentityModel.Abstractions.xml
│       │       │   ├── net472
│       │       │   │   ├── Microsoft.IdentityModel.Abstractions.dll
│       │       │   │   └── Microsoft.IdentityModel.Abstractions.xml
│       │       │   ├── net6.0
│       │       │   │   ├── Microsoft.IdentityModel.Abstractions.dll
│       │       │   │   └── Microsoft.IdentityModel.Abstractions.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.IdentityModel.Abstractions.dll
│       │       │       └── Microsoft.IdentityModel.Abstractions.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── microsoft.identitymodel.abstractions.6.35.0.nupkg
│       │       ├── microsoft.identitymodel.abstractions.6.35.0.nupkg.sha512
│       │       └── microsoft.identitymodel.abstractions.nuspec
│       ├── microsoft.identitymodel.jsonwebtokens
│       │   └── 6.35.0
│       │       ├── lib
│       │       │   ├── net45
│       │       │   │   ├── Microsoft.IdentityModel.JsonWebTokens.dll
│       │       │   │   └── Microsoft.IdentityModel.JsonWebTokens.xml
│       │       │   ├── net461
│       │       │   │   ├── Microsoft.IdentityModel.JsonWebTokens.dll
│       │       │   │   └── Microsoft.IdentityModel.JsonWebTokens.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.IdentityModel.JsonWebTokens.dll
│       │       │   │   └── Microsoft.IdentityModel.JsonWebTokens.xml
│       │       │   ├── net472
│       │       │   │   ├── Microsoft.IdentityModel.JsonWebTokens.dll
│       │       │   │   └── Microsoft.IdentityModel.JsonWebTokens.xml
│       │       │   ├── net6.0
│       │       │   │   ├── Microsoft.IdentityModel.JsonWebTokens.dll
│       │       │   │   └── Microsoft.IdentityModel.JsonWebTokens.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.IdentityModel.JsonWebTokens.dll
│       │       │       └── Microsoft.IdentityModel.JsonWebTokens.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── microsoft.identitymodel.jsonwebtokens.6.35.0.nupkg
│       │       ├── microsoft.identitymodel.jsonwebtokens.6.35.0.nupkg.sha512
│       │       └── microsoft.identitymodel.jsonwebtokens.nuspec
│       ├── microsoft.identitymodel.logging
│       │   └── 6.35.0
│       │       ├── lib
│       │       │   ├── net45
│       │       │   │   ├── Microsoft.IdentityModel.Logging.dll
│       │       │   │   └── Microsoft.IdentityModel.Logging.xml
│       │       │   ├── net461
│       │       │   │   ├── Microsoft.IdentityModel.Logging.dll
│       │       │   │   └── Microsoft.IdentityModel.Logging.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.IdentityModel.Logging.dll
│       │       │   │   └── Microsoft.IdentityModel.Logging.xml
│       │       │   ├── net472
│       │       │   │   ├── Microsoft.IdentityModel.Logging.dll
│       │       │   │   └── Microsoft.IdentityModel.Logging.xml
│       │       │   ├── net6.0
│       │       │   │   ├── Microsoft.IdentityModel.Logging.dll
│       │       │   │   └── Microsoft.IdentityModel.Logging.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.IdentityModel.Logging.dll
│       │       │       └── Microsoft.IdentityModel.Logging.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── microsoft.identitymodel.logging.6.35.0.nupkg
│       │       ├── microsoft.identitymodel.logging.6.35.0.nupkg.sha512
│       │       └── microsoft.identitymodel.logging.nuspec
│       ├── microsoft.identitymodel.protocols
│       │   └── 6.35.0
│       │       ├── lib
│       │       │   ├── net45
│       │       │   │   ├── Microsoft.IdentityModel.Protocols.dll
│       │       │   │   └── Microsoft.IdentityModel.Protocols.xml
│       │       │   ├── net461
│       │       │   │   ├── Microsoft.IdentityModel.Protocols.dll
│       │       │   │   └── Microsoft.IdentityModel.Protocols.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.IdentityModel.Protocols.dll
│       │       │   │   └── Microsoft.IdentityModel.Protocols.xml
│       │       │   ├── net472
│       │       │   │   ├── Microsoft.IdentityModel.Protocols.dll
│       │       │   │   └── Microsoft.IdentityModel.Protocols.xml
│       │       │   ├── net6.0
│       │       │   │   ├── Microsoft.IdentityModel.Protocols.dll
│       │       │   │   └── Microsoft.IdentityModel.Protocols.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.IdentityModel.Protocols.dll
│       │       │       └── Microsoft.IdentityModel.Protocols.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── microsoft.identitymodel.protocols.6.35.0.nupkg
│       │       ├── microsoft.identitymodel.protocols.6.35.0.nupkg.sha512
│       │       └── microsoft.identitymodel.protocols.nuspec
│       ├── microsoft.identitymodel.protocols.openidconnect
│       │   └── 6.35.0
│       │       ├── lib
│       │       │   ├── net45
│       │       │   │   ├── Microsoft.IdentityModel.Protocols.OpenIdConnect.dll
│       │       │   │   └── Microsoft.IdentityModel.Protocols.OpenIdConnect.xml
│       │       │   ├── net461
│       │       │   │   ├── Microsoft.IdentityModel.Protocols.OpenIdConnect.dll
│       │       │   │   └── Microsoft.IdentityModel.Protocols.OpenIdConnect.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.IdentityModel.Protocols.OpenIdConnect.dll
│       │       │   │   └── Microsoft.IdentityModel.Protocols.OpenIdConnect.xml
│       │       │   ├── net472
│       │       │   │   ├── Microsoft.IdentityModel.Protocols.OpenIdConnect.dll
│       │       │   │   └── Microsoft.IdentityModel.Protocols.OpenIdConnect.xml
│       │       │   ├── net6.0
│       │       │   │   ├── Microsoft.IdentityModel.Protocols.OpenIdConnect.dll
│       │       │   │   └── Microsoft.IdentityModel.Protocols.OpenIdConnect.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.IdentityModel.Protocols.OpenIdConnect.dll
│       │       │       └── Microsoft.IdentityModel.Protocols.OpenIdConnect.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── microsoft.identitymodel.protocols.openidconnect.6.35.0.nupkg
│       │       ├── microsoft.identitymodel.protocols.openidconnect.6.35.0.nupkg.sha512
│       │       └── microsoft.identitymodel.protocols.openidconnect.nuspec
│       ├── microsoft.identitymodel.tokens
│       │   └── 6.35.0
│       │       ├── lib
│       │       │   ├── net45
│       │       │   │   ├── Microsoft.IdentityModel.Tokens.dll
│       │       │   │   └── Microsoft.IdentityModel.Tokens.xml
│       │       │   ├── net461
│       │       │   │   ├── Microsoft.IdentityModel.Tokens.dll
│       │       │   │   └── Microsoft.IdentityModel.Tokens.xml
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.IdentityModel.Tokens.dll
│       │       │   │   └── Microsoft.IdentityModel.Tokens.xml
│       │       │   ├── net472
│       │       │   │   ├── Microsoft.IdentityModel.Tokens.dll
│       │       │   │   └── Microsoft.IdentityModel.Tokens.xml
│       │       │   ├── net6.0
│       │       │   │   ├── Microsoft.IdentityModel.Tokens.dll
│       │       │   │   └── Microsoft.IdentityModel.Tokens.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.IdentityModel.Tokens.dll
│       │       │       └── Microsoft.IdentityModel.Tokens.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── microsoft.identitymodel.tokens.6.35.0.nupkg
│       │       ├── microsoft.identitymodel.tokens.6.35.0.nupkg.sha512
│       │       └── microsoft.identitymodel.tokens.nuspec
│       ├── microsoft.io.recyclablememorystream
│       │   ├── 3.0.0
│       │   │   ├── _rels
│       │   │   ├── lib
│       │   │   │   ├── net6.0
│       │   │   │   │   ├── Microsoft.IO.RecyclableMemoryStream.dll
│       │   │   │   │   └── Microsoft.IO.RecyclableMemoryStream.xml
│       │   │   │   ├── netstandard2.0
│       │   │   │   │   ├── Microsoft.IO.RecyclableMemoryStream.dll
│       │   │   │   │   └── Microsoft.IO.RecyclableMemoryStream.xml
│       │   │   │   └── netstandard2.1
│       │   │   │       ├── Microsoft.IO.RecyclableMemoryStream.dll
│       │   │   │       └── Microsoft.IO.RecyclableMemoryStream.xml
│       │   │   ├── package
│       │   │   │   └── services
│       │   │   │       └── metadata
│       │   │   │           └── core-properties
│       │   │   ├── .nupkg.metadata
│       │   │   ├── .signature.p7s
│       │   │   ├── microsoft.io.recyclablememorystream.3.0.0.nupkg
│       │   │   ├── microsoft.io.recyclablememorystream.3.0.0.nupkg.sha512
│       │   │   ├── microsoft.io.recyclablememorystream.nuspec
│       │   │   └── README.md
│       │   └── 3.0.1
│       │       ├── _rels
│       │       ├── lib
│       │       │   ├── net6.0
│       │       │   │   ├── Microsoft.IO.RecyclableMemoryStream.dll
│       │       │   │   └── Microsoft.IO.RecyclableMemoryStream.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── Microsoft.IO.RecyclableMemoryStream.dll
│       │       │   │   └── Microsoft.IO.RecyclableMemoryStream.xml
│       │       │   └── netstandard2.1
│       │       │       ├── Microsoft.IO.RecyclableMemoryStream.dll
│       │       │       └── Microsoft.IO.RecyclableMemoryStream.xml
│       │       ├── package
│       │       │   └── services
│       │       │       └── metadata
│       │       │           └── core-properties
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── microsoft.io.recyclablememorystream.3.0.1.nupkg
│       │       ├── microsoft.io.recyclablememorystream.3.0.1.nupkg.sha512
│       │       ├── microsoft.io.recyclablememorystream.nuspec
│       │       └── README.md
│       ├── microsoft.net.illink.tasks
│       │   └── 10.0.8
│       │       ├── analyzers
│       │       │   └── dotnet
│       │       │       └── cs
│       │       │           ├── ILLink.CodeFixProvider.dll
│       │       │           └── ILLink.RoslynAnalyzer.dll
│       │       ├── build
│       │       │   ├── Microsoft.NET.ILLink.Analyzers.props
│       │       │   ├── Microsoft.NET.ILLink.targets
│       │       │   └── Microsoft.NET.ILLink.Tasks.props
│       │       ├── Sdk
│       │       │   └── Sdk.props
│       │       ├── tools
│       │       │   ├── net
│       │       │   │   ├── build
│       │       │   │   │   ├── Microsoft.NET.ILLink.Analyzers.props
│       │       │   │   │   ├── Microsoft.NET.ILLink.targets
│       │       │   │   │   └── Microsoft.NET.ILLink.Tasks.props
│       │       │   │   ├── Sdk
│       │       │   │   │   └── Sdk.props
│       │       │   │   ├── illink.deps.json
│       │       │   │   ├── illink.dll
│       │       │   │   ├── illink.runtimeconfig.json
│       │       │   │   ├── ILLink.Tasks.deps.json
│       │       │   │   ├── ILLink.Tasks.dll
│       │       │   │   ├── Mono.Cecil.dll
│       │       │   │   ├── Mono.Cecil.Mdb.dll
│       │       │   │   ├── Mono.Cecil.Pdb.dll
│       │       │   │   └── Mono.Cecil.Rocks.dll
│       │       │   └── netframework
│       │       │       ├── build
│       │       │       │   ├── Microsoft.NET.ILLink.Analyzers.props
│       │       │       │   ├── Microsoft.NET.ILLink.targets
│       │       │       │   └── Microsoft.NET.ILLink.Tasks.props
│       │       │       ├── Sdk
│       │       │       │   └── Sdk.props
│       │       │       ├── ILLink.Tasks.dll
│       │       │       ├── ILLink.Tasks.dll.config
│       │       │       ├── Mono.Cecil.dll
│       │       │       ├── Mono.Cecil.Mdb.dll
│       │       │       ├── Mono.Cecil.Pdb.dll
│       │       │       ├── Mono.Cecil.Rocks.dll
│       │       │       ├── System.Buffers.dll
│       │       │       ├── System.Collections.Immutable.dll
│       │       │       ├── System.Memory.dll
│       │       │       ├── System.Numerics.Vectors.dll
│       │       │       ├── System.Reflection.Metadata.dll
│       │       │       └── System.Runtime.CompilerServices.Unsafe.dll
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.net.illink.tasks.10.0.8.nupkg
│       │       ├── microsoft.net.illink.tasks.10.0.8.nupkg.sha512
│       │       ├── microsoft.net.illink.tasks.nuspec
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.net.test.sdk
│       │   └── 18.5.1
│       │       ├── build
│       │       │   ├── net462
│       │       │   │   ├── Microsoft.NET.Test.Sdk.props
│       │       │   │   └── Microsoft.NET.Test.Sdk.targets
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.NET.Test.Sdk.Program.cs
│       │       │   │   ├── Microsoft.NET.Test.Sdk.Program.fs
│       │       │   │   ├── Microsoft.NET.Test.Sdk.Program.vb
│       │       │   │   ├── Microsoft.NET.Test.Sdk.props
│       │       │   │   └── Microsoft.NET.Test.Sdk.targets
│       │       │   ├── netcoreapp2.0
│       │       │   │   ├── Microsoft.NET.Test.Sdk.props
│       │       │   │   └── Microsoft.NET.Test.Sdk.targets
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.NET.Test.Sdk.props
│       │       │       └── Microsoft.NET.Test.Sdk.targets
│       │       ├── buildMultiTargeting
│       │       │   ├── net462
│       │       │   │   └── Microsoft.NET.Test.Sdk.props
│       │       │   ├── net8.0
│       │       │   │   └── Microsoft.NET.Test.Sdk.props
│       │       │   ├── netcoreapp2.0
│       │       │   │   ├── Microsoft.NET.Test.Sdk.props
│       │       │   │   └── Microsoft.NET.Test.Sdk.targets
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.NET.Test.Sdk.props
│       │       │       └── Microsoft.NET.Test.Sdk.targets
│       │       ├── lib
│       │       │   ├── native
│       │       │   │   └── _._
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   └── net8.0
│       │       │       └── _._
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.net.test.sdk.18.5.1.nupkg
│       │       ├── microsoft.net.test.sdk.18.5.1.nupkg.sha512
│       │       └── microsoft.net.test.sdk.nuspec
│       ├── microsoft.netcore.app.runtime.win-x64
│       │   └── 10.0.8
│       │       ├── data
│       │       │   └── RuntimeList.xml
│       │       ├── runtimes
│       │       │   └── win-x64
│       │       │       ├── lib
│       │       │       │   └── net10.0
│       │       │       │       ├── Microsoft.CSharp.dll
│       │       │       │       ├── Microsoft.NETCore.App.deps.json
│       │       │       │       ├── Microsoft.NETCore.App.runtimeconfig.json
│       │       │       │       ├── Microsoft.VisualBasic.Core.dll
│       │       │       │       ├── Microsoft.VisualBasic.dll
│       │       │       │       ├── Microsoft.Win32.Primitives.dll
│       │       │       │       ├── Microsoft.Win32.Registry.dll
│       │       │       │       ├── mscorlib.dll
│       │       │       │       ├── netstandard.dll
│       │       │       │       ├── System.AppContext.dll
│       │       │       │       ├── System.Buffers.dll
│       │       │       │       ├── System.Collections.Concurrent.dll
│       │       │       │       ├── System.Collections.dll
│       │       │       │       ├── System.Collections.Immutable.dll
│       │       │       │       ├── System.Collections.NonGeneric.dll
│       │       │       │       ├── System.Collections.Specialized.dll
│       │       │       │       ├── System.ComponentModel.Annotations.dll
│       │       │       │       ├── System.ComponentModel.DataAnnotations.dll
│       │       │       │       ├── System.ComponentModel.dll
│       │       │       │       ├── System.ComponentModel.EventBasedAsync.dll
│       │       │       │       ├── System.ComponentModel.Primitives.dll
│       │       │       │       ├── System.ComponentModel.TypeConverter.dll
│       │       │       │       ├── System.Configuration.dll
│       │       │       │       ├── System.Console.dll
│       │       │       │       ├── System.Core.dll
│       │       │       │       ├── System.Data.Common.dll
│       │       │       │       ├── System.Data.DataSetExtensions.dll
│       │       │       │       ├── System.Data.dll
│       │       │       │       ├── System.Diagnostics.Contracts.dll
│       │       │       │       ├── System.Diagnostics.Debug.dll
│       │       │       │       ├── System.Diagnostics.DiagnosticSource.dll
│       │       │       │       ├── System.Diagnostics.FileVersionInfo.dll
│       │       │       │       ├── System.Diagnostics.Process.dll
│       │       │       │       ├── System.Diagnostics.StackTrace.dll
│       │       │       │       ├── System.Diagnostics.TextWriterTraceListener.dll
│       │       │       │       ├── System.Diagnostics.Tools.dll
│       │       │       │       ├── System.Diagnostics.TraceSource.dll
│       │       │       │       ├── System.Diagnostics.Tracing.dll
│       │       │       │       ├── System.dll
│       │       │       │       ├── System.Drawing.dll
│       │       │       │       ├── System.Drawing.Primitives.dll
│       │       │       │       ├── System.Dynamic.Runtime.dll
│       │       │       │       ├── System.Formats.Asn1.dll
│       │       │       │       ├── System.Formats.Tar.dll
│       │       │       │       ├── System.Globalization.Calendars.dll
│       │       │       │       ├── System.Globalization.dll
│       │       │       │       ├── System.Globalization.Extensions.dll
│       │       │       │       ├── System.IO.Compression.Brotli.dll
│       │       │       │       ├── System.IO.Compression.dll
│       │       │       │       ├── System.IO.Compression.FileSystem.dll
│       │       │       │       ├── System.IO.Compression.ZipFile.dll
│       │       │       │       ├── System.IO.dll
│       │       │       │       ├── System.IO.FileSystem.AccessControl.dll
│       │       │       │       ├── System.IO.FileSystem.dll
│       │       │       │       ├── System.IO.FileSystem.DriveInfo.dll
│       │       │       │       ├── System.IO.FileSystem.Primitives.dll
│       │       │       │       ├── System.IO.FileSystem.Watcher.dll
│       │       │       │       ├── System.IO.IsolatedStorage.dll
│       │       │       │       ├── System.IO.MemoryMappedFiles.dll
│       │       │       │       ├── System.IO.Pipelines.dll
│       │       │       │       ├── System.IO.Pipes.AccessControl.dll
│       │       │       │       ├── System.IO.Pipes.dll
│       │       │       │       ├── System.IO.UnmanagedMemoryStream.dll
│       │       │       │       ├── System.Linq.AsyncEnumerable.dll
│       │       │       │       ├── System.Linq.dll
│       │       │       │       ├── System.Linq.Expressions.dll
│       │       │       │       ├── System.Linq.Parallel.dll
│       │       │       │       ├── System.Linq.Queryable.dll
│       │       │       │       ├── System.Memory.dll
│       │       │       │       ├── System.Net.dll
│       │       │       │       ├── System.Net.Http.dll
│       │       │       │       ├── System.Net.Http.Json.dll
│       │       │       │       ├── System.Net.HttpListener.dll
│       │       │       │       ├── System.Net.Mail.dll
│       │       │       │       ├── System.Net.NameResolution.dll
│       │       │       │       ├── System.Net.NetworkInformation.dll
│       │       │       │       ├── System.Net.Ping.dll
│       │       │       │       ├── System.Net.Primitives.dll
│       │       │       │       ├── System.Net.Quic.dll
│       │       │       │       ├── System.Net.Requests.dll
│       │       │       │       ├── System.Net.Security.dll
│       │       │       │       ├── System.Net.ServerSentEvents.dll
│       │       │       │       ├── System.Net.ServicePoint.dll
│       │       │       │       ├── System.Net.Sockets.dll
│       │       │       │       ├── System.Net.WebClient.dll
│       │       │       │       ├── System.Net.WebHeaderCollection.dll
│       │       │       │       ├── System.Net.WebProxy.dll
│       │       │       │       ├── System.Net.WebSockets.Client.dll
│       │       │       │       ├── System.Net.WebSockets.dll
│       │       │       │       ├── System.Numerics.dll
│       │       │       │       ├── System.Numerics.Vectors.dll
│       │       │       │       ├── System.ObjectModel.dll
│       │       │       │       ├── System.Private.CoreLib.dll
│       │       │       │       ├── System.Private.DataContractSerialization.dll
│       │       │       │       ├── System.Private.Uri.dll
│       │       │       │       ├── System.Private.Xml.dll
│       │       │       │       ├── System.Private.Xml.Linq.dll
│       │       │       │       ├── System.Reflection.DispatchProxy.dll
│       │       │       │       ├── System.Reflection.dll
│       │       │       │       ├── System.Reflection.Emit.dll
│       │       │       │       ├── System.Reflection.Emit.ILGeneration.dll
│       │       │       │       ├── System.Reflection.Emit.Lightweight.dll
│       │       │       │       ├── System.Reflection.Extensions.dll
│       │       │       │       ├── System.Reflection.Metadata.dll
│       │       │       │       ├── System.Reflection.Primitives.dll
│       │       │       │       ├── System.Reflection.TypeExtensions.dll
│       │       │       │       ├── System.Resources.Reader.dll
│       │       │       │       ├── System.Resources.ResourceManager.dll
│       │       │       │       ├── System.Resources.Writer.dll
│       │       │       │       ├── System.Runtime.CompilerServices.Unsafe.dll
│       │       │       │       ├── System.Runtime.CompilerServices.VisualC.dll
│       │       │       │       ├── System.Runtime.dll
│       │       │       │       ├── System.Runtime.Extensions.dll
│       │       │       │       ├── System.Runtime.Handles.dll
│       │       │       │       ├── System.Runtime.InteropServices.dll
│       │       │       │       ├── System.Runtime.InteropServices.JavaScript.dll
│       │       │       │       ├── System.Runtime.InteropServices.RuntimeInformation.dll
│       │       │       │       ├── System.Runtime.Intrinsics.dll
│       │       │       │       ├── System.Runtime.Loader.dll
│       │       │       │       ├── System.Runtime.Numerics.dll
│       │       │       │       ├── System.Runtime.Serialization.dll
│       │       │       │       ├── System.Runtime.Serialization.Formatters.dll
│       │       │       │       ├── System.Runtime.Serialization.Json.dll
│       │       │       │       ├── System.Runtime.Serialization.Primitives.dll
│       │       │       │       ├── System.Runtime.Serialization.Xml.dll
│       │       │       │       ├── System.Security.AccessControl.dll
│       │       │       │       ├── System.Security.Claims.dll
│       │       │       │       ├── System.Security.Cryptography.Algorithms.dll
│       │       │       │       ├── System.Security.Cryptography.Cng.dll
│       │       │       │       ├── System.Security.Cryptography.Csp.dll
│       │       │       │       ├── System.Security.Cryptography.dll
│       │       │       │       ├── System.Security.Cryptography.Encoding.dll
│       │       │       │       ├── System.Security.Cryptography.OpenSsl.dll
│       │       │       │       ├── System.Security.Cryptography.Primitives.dll
│       │       │       │       ├── System.Security.Cryptography.X509Certificates.dll
│       │       │       │       ├── System.Security.dll
│       │       │       │       ├── System.Security.Principal.dll
│       │       │       │       ├── System.Security.Principal.Windows.dll
│       │       │       │       ├── System.Security.SecureString.dll
│       │       │       │       ├── System.ServiceModel.Web.dll
│       │       │       │       ├── System.ServiceProcess.dll
│       │       │       │       ├── System.Text.Encoding.CodePages.dll
│       │       │       │       ├── System.Text.Encoding.dll
│       │       │       │       ├── System.Text.Encoding.Extensions.dll
│       │       │       │       ├── System.Text.Encodings.Web.dll
│       │       │       │       ├── System.Text.Json.dll
│       │       │       │       ├── System.Text.RegularExpressions.dll
│       │       │       │       ├── System.Threading.AccessControl.dll
│       │       │       │       ├── System.Threading.Channels.dll
│       │       │       │       ├── System.Threading.dll
│       │       │       │       ├── System.Threading.Overlapped.dll
│       │       │       │       ├── System.Threading.Tasks.Dataflow.dll
│       │       │       │       ├── System.Threading.Tasks.dll
│       │       │       │       ├── System.Threading.Tasks.Extensions.dll
│       │       │       │       ├── System.Threading.Tasks.Parallel.dll
│       │       │       │       ├── System.Threading.Thread.dll
│       │       │       │       ├── System.Threading.ThreadPool.dll
│       │       │       │       ├── System.Threading.Timer.dll
│       │       │       │       ├── System.Transactions.dll
│       │       │       │       ├── System.Transactions.Local.dll
│       │       │       │       ├── System.ValueTuple.dll
│       │       │       │       ├── System.Web.dll
│       │       │       │       ├── System.Web.HttpUtility.dll
│       │       │       │       ├── System.Windows.dll
│       │       │       │       ├── System.Xml.dll
│       │       │       │       ├── System.Xml.Linq.dll
│       │       │       │       ├── System.Xml.ReaderWriter.dll
│       │       │       │       ├── System.Xml.Serialization.dll
│       │       │       │       ├── System.Xml.XDocument.dll
│       │       │       │       ├── System.Xml.XmlDocument.dll
│       │       │       │       ├── System.Xml.XmlSerializer.dll
│       │       │       │       ├── System.Xml.XPath.dll
│       │       │       │       ├── System.Xml.XPath.XDocument.dll
│       │       │       │       └── WindowsBase.dll
│       │       │       └── native
│       │       │           ├── clretwrc.dll
│       │       │           ├── clrgc.dll
│       │       │           ├── clrgcexp.dll
│       │       │           ├── clrjit.dll
│       │       │           ├── coreclr.dll
│       │       │           ├── createdump.exe
│       │       │           ├── hostfxr.dll
│       │       │           ├── hostpolicy.dll
│       │       │           ├── Microsoft.DiaSymReader.Native.amd64.dll
│       │       │           ├── mscordaccore.dll
│       │       │           ├── mscordaccore_amd64_amd64_10.0.826.23019.dll
│       │       │           ├── mscordbi.dll
│       │       │           ├── mscorrc.dll
│       │       │           ├── msquic.dll
│       │       │           └── System.IO.Compression.Native.dll
│       │       ├── tools
│       │       │   └── StandardOptimizationData.mibc
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── LICENSE.TXT
│       │       ├── microsoft.netcore.app.runtime.win-x64.10.0.8.nupkg
│       │       ├── microsoft.netcore.app.runtime.win-x64.10.0.8.nupkg.sha512
│       │       ├── microsoft.netcore.app.runtime.win-x64.nuspec
│       │       ├── Microsoft.NETCore.App.versions.txt
│       │       ├── PACKAGE.md
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── microsoft.openapi
│       │   ├── 1.6.17
│       │   │   ├── lib
│       │   │   │   └── netstandard2.0
│       │   │   │       ├── Microsoft.OpenApi.dll
│       │   │   │       ├── Microsoft.OpenApi.pdb
│       │   │   │       └── Microsoft.OpenApi.xml
│       │   │   ├── .nupkg.metadata
│       │   │   ├── .signature.p7s
│       │   │   ├── microsoft.openapi.1.6.17.nupkg
│       │   │   ├── microsoft.openapi.1.6.17.nupkg.sha512
│       │   │   ├── microsoft.openapi.nuspec
│       │   │   └── README.md
│       │   └── 2.4.1
│       │       ├── lib
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.OpenApi.dll
│       │       │   │   ├── Microsoft.OpenApi.pdb
│       │       │   │   └── Microsoft.OpenApi.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.OpenApi.dll
│       │       │       ├── Microsoft.OpenApi.pdb
│       │       │       └── Microsoft.OpenApi.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── microsoft.openapi.2.4.1.nupkg
│       │       ├── microsoft.openapi.2.4.1.nupkg.sha512
│       │       ├── microsoft.openapi.nuspec
│       │       └── README.md
│       ├── microsoft.sqlserver.server
│       │   └── 1.0.0
│       │       ├── lib
│       │       │   ├── net46
│       │       │   │   ├── Microsoft.SqlServer.Server.dll
│       │       │   │   ├── Microsoft.SqlServer.Server.pdb
│       │       │   │   └── Microsoft.SqlServer.Server.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.SqlServer.Server.dll
│       │       │       ├── Microsoft.SqlServer.Server.pdb
│       │       │       └── Microsoft.SqlServer.Server.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── dotnet.png
│       │       ├── microsoft.sqlserver.server.1.0.0.nupkg
│       │       ├── microsoft.sqlserver.server.1.0.0.nupkg.sha512
│       │       └── microsoft.sqlserver.server.nuspec
│       ├── microsoft.testing.extensions.telemetry
│       │   └── 1.9.0
│       │       ├── build
│       │       │   ├── net6.0
│       │       │   │   └── Microsoft.Testing.Extensions.Telemetry.props
│       │       │   ├── net7.0
│       │       │   │   └── Microsoft.Testing.Extensions.Telemetry.props
│       │       │   ├── net8.0
│       │       │   │   └── Microsoft.Testing.Extensions.Telemetry.props
│       │       │   ├── net9.0
│       │       │   │   └── Microsoft.Testing.Extensions.Telemetry.props
│       │       │   └── netstandard2.0
│       │       │       └── Microsoft.Testing.Extensions.Telemetry.props
│       │       ├── buildMultiTargeting
│       │       │   └── Microsoft.Testing.Extensions.Telemetry.props
│       │       ├── buildTransitive
│       │       │   ├── net6.0
│       │       │   │   └── Microsoft.Testing.Extensions.Telemetry.props
│       │       │   ├── net7.0
│       │       │   │   └── Microsoft.Testing.Extensions.Telemetry.props
│       │       │   ├── net8.0
│       │       │   │   └── Microsoft.Testing.Extensions.Telemetry.props
│       │       │   ├── net9.0
│       │       │   │   └── Microsoft.Testing.Extensions.Telemetry.props
│       │       │   └── netstandard2.0
│       │       │       └── Microsoft.Testing.Extensions.Telemetry.props
│       │       ├── lib
│       │       │   ├── net6.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── Microsoft.Testing.Extensions.Telemetry.dll
│       │       │   │   └── Microsoft.Testing.Extensions.Telemetry.xml
│       │       │   ├── net7.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── Microsoft.Testing.Extensions.Telemetry.dll
│       │       │   │   └── Microsoft.Testing.Extensions.Telemetry.xml
│       │       │   ├── net8.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── Microsoft.Testing.Extensions.Telemetry.dll
│       │       │   │   └── Microsoft.Testing.Extensions.Telemetry.xml
│       │       │   ├── net9.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │   │   ├── Microsoft.Testing.Extensions.Telemetry.dll
│       │       │   │   └── Microsoft.Testing.Extensions.Telemetry.xml
│       │       │   └── netstandard2.0
│       │       │       ├── cs
│       │       │       │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │       ├── de
│       │       │       │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │       ├── es
│       │       │       │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │       ├── fr
│       │       │       │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │       ├── it
│       │       │       │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │       ├── ja
│       │       │       │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │       ├── ko
│       │       │       │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │       ├── pl
│       │       │       │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │       ├── pt-BR
│       │       │       │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │       ├── ru
│       │       │       │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │       ├── tr
│       │       │       │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │       ├── zh-Hans
│       │       │       │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │       ├── zh-Hant
│       │       │       │   └── Microsoft.Testing.Extensions.Telemetry.resources.dll
│       │       │       ├── Microsoft.Testing.Extensions.Telemetry.dll
│       │       │       └── Microsoft.Testing.Extensions.Telemetry.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.testing.extensions.telemetry.1.9.0.nupkg
│       │       ├── microsoft.testing.extensions.telemetry.1.9.0.nupkg.sha512
│       │       ├── microsoft.testing.extensions.telemetry.nuspec
│       │       └── PACKAGE.md
│       ├── microsoft.testing.extensions.trxreport.abstractions
│       │   └── 1.9.0
│       │       ├── lib
│       │       │   ├── net6.0
│       │       │   │   ├── Microsoft.Testing.Extensions.TrxReport.Abstractions.dll
│       │       │   │   └── Microsoft.Testing.Extensions.TrxReport.Abstractions.xml
│       │       │   ├── net7.0
│       │       │   │   ├── Microsoft.Testing.Extensions.TrxReport.Abstractions.dll
│       │       │   │   └── Microsoft.Testing.Extensions.TrxReport.Abstractions.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Testing.Extensions.TrxReport.Abstractions.dll
│       │       │   │   └── Microsoft.Testing.Extensions.TrxReport.Abstractions.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Testing.Extensions.TrxReport.Abstractions.dll
│       │       │   │   └── Microsoft.Testing.Extensions.TrxReport.Abstractions.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Testing.Extensions.TrxReport.Abstractions.dll
│       │       │       └── Microsoft.Testing.Extensions.TrxReport.Abstractions.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.testing.extensions.trxreport.abstractions.1.9.0.nupkg
│       │       ├── microsoft.testing.extensions.trxreport.abstractions.1.9.0.nupkg.sha512
│       │       ├── microsoft.testing.extensions.trxreport.abstractions.nuspec
│       │       └── PACKAGE.md
│       ├── microsoft.testing.extensions.vstestbridge
│       │   └── 1.9.0
│       │       ├── lib
│       │       │   ├── net6.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── Microsoft.Testing.Extensions.VSTestBridge.dll
│       │       │   │   └── Microsoft.Testing.Extensions.VSTestBridge.xml
│       │       │   ├── net7.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── Microsoft.Testing.Extensions.VSTestBridge.dll
│       │       │   │   └── Microsoft.Testing.Extensions.VSTestBridge.xml
│       │       │   ├── net8.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── Microsoft.Testing.Extensions.VSTestBridge.dll
│       │       │   │   └── Microsoft.Testing.Extensions.VSTestBridge.xml
│       │       │   ├── net9.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │   │   ├── Microsoft.Testing.Extensions.VSTestBridge.dll
│       │       │   │   └── Microsoft.Testing.Extensions.VSTestBridge.xml
│       │       │   └── netstandard2.0
│       │       │       ├── cs
│       │       │       │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │       ├── de
│       │       │       │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │       ├── es
│       │       │       │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │       ├── fr
│       │       │       │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │       ├── it
│       │       │       │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │       ├── ja
│       │       │       │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │       ├── ko
│       │       │       │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │       ├── pl
│       │       │       │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │       ├── pt-BR
│       │       │       │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │       ├── ru
│       │       │       │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │       ├── tr
│       │       │       │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │       ├── zh-Hans
│       │       │       │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │       ├── zh-Hant
│       │       │       │   └── Microsoft.Testing.Extensions.VSTestBridge.resources.dll
│       │       │       ├── Microsoft.Testing.Extensions.VSTestBridge.dll
│       │       │       └── Microsoft.Testing.Extensions.VSTestBridge.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.testing.extensions.vstestbridge.1.9.0.nupkg
│       │       ├── microsoft.testing.extensions.vstestbridge.1.9.0.nupkg.sha512
│       │       ├── microsoft.testing.extensions.vstestbridge.nuspec
│       │       └── PACKAGE.md
│       ├── microsoft.testing.platform
│       │   └── 1.9.0
│       │       ├── build
│       │       │   ├── net6.0
│       │       │   │   ├── Microsoft.Testing.Platform.props
│       │       │   │   └── Microsoft.Testing.Platform.targets
│       │       │   ├── net7.0
│       │       │   │   ├── Microsoft.Testing.Platform.props
│       │       │   │   └── Microsoft.Testing.Platform.targets
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Testing.Platform.props
│       │       │   │   └── Microsoft.Testing.Platform.targets
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Testing.Platform.props
│       │       │   │   └── Microsoft.Testing.Platform.targets
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Testing.Platform.props
│       │       │       └── Microsoft.Testing.Platform.targets
│       │       ├── buildMultiTargeting
│       │       │   ├── Microsoft.Testing.Platform.props
│       │       │   └── Microsoft.Testing.Platform.targets
│       │       ├── buildTransitive
│       │       │   ├── net6.0
│       │       │   │   ├── Microsoft.Testing.Platform.props
│       │       │   │   └── Microsoft.Testing.Platform.targets
│       │       │   ├── net7.0
│       │       │   │   ├── Microsoft.Testing.Platform.props
│       │       │   │   └── Microsoft.Testing.Platform.targets
│       │       │   ├── net8.0
│       │       │   │   ├── Microsoft.Testing.Platform.props
│       │       │   │   └── Microsoft.Testing.Platform.targets
│       │       │   ├── net9.0
│       │       │   │   ├── Microsoft.Testing.Platform.props
│       │       │   │   └── Microsoft.Testing.Platform.targets
│       │       │   └── netstandard2.0
│       │       │       ├── Microsoft.Testing.Platform.props
│       │       │       └── Microsoft.Testing.Platform.targets
│       │       ├── lib
│       │       │   ├── net6.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── Microsoft.Testing.Platform.dll
│       │       │   │   └── Microsoft.Testing.Platform.xml
│       │       │   ├── net7.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── Microsoft.Testing.Platform.dll
│       │       │   │   └── Microsoft.Testing.Platform.xml
│       │       │   ├── net8.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── Microsoft.Testing.Platform.dll
│       │       │   │   └── Microsoft.Testing.Platform.xml
│       │       │   ├── net9.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.Testing.Platform.resources.dll
│       │       │   │   ├── Microsoft.Testing.Platform.dll
│       │       │   │   └── Microsoft.Testing.Platform.xml
│       │       │   └── netstandard2.0
│       │       │       ├── cs
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── de
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── es
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── fr
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── it
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── ja
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── ko
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── pl
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── pt-BR
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── ru
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── tr
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── zh-Hans
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── zh-Hant
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── Microsoft.Testing.Platform.dll
│       │       │       └── Microsoft.Testing.Platform.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.testing.platform.1.9.0.nupkg
│       │       ├── microsoft.testing.platform.1.9.0.nupkg.sha512
│       │       ├── microsoft.testing.platform.nuspec
│       │       └── PACKAGE.md
│       ├── microsoft.testing.platform.msbuild
│       │   └── 1.9.0
│       │       ├── _MSBuildTasks
│       │       │   └── netstandard2.0
│       │       │       ├── cs
│       │       │       │   ├── Microsoft.Testing.Platform.MSBuild.resources.dll
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── de
│       │       │       │   ├── Microsoft.Testing.Platform.MSBuild.resources.dll
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── es
│       │       │       │   ├── Microsoft.Testing.Platform.MSBuild.resources.dll
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── fr
│       │       │       │   ├── Microsoft.Testing.Platform.MSBuild.resources.dll
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── it
│       │       │       │   ├── Microsoft.Testing.Platform.MSBuild.resources.dll
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── ja
│       │       │       │   ├── Microsoft.Testing.Platform.MSBuild.resources.dll
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── ko
│       │       │       │   ├── Microsoft.Testing.Platform.MSBuild.resources.dll
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── pl
│       │       │       │   ├── Microsoft.Testing.Platform.MSBuild.resources.dll
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── pt-BR
│       │       │       │   ├── Microsoft.Testing.Platform.MSBuild.resources.dll
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── ru
│       │       │       │   ├── Microsoft.Testing.Platform.MSBuild.resources.dll
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── tr
│       │       │       │   ├── Microsoft.Testing.Platform.MSBuild.resources.dll
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── zh-Hans
│       │       │       │   ├── Microsoft.Testing.Platform.MSBuild.resources.dll
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── zh-Hant
│       │       │       │   ├── Microsoft.Testing.Platform.MSBuild.resources.dll
│       │       │       │   └── Microsoft.Testing.Platform.resources.dll
│       │       │       ├── Microsoft.Testing.Platform.dll
│       │       │       ├── Microsoft.Testing.Platform.MSBuild.dll
│       │       │       ├── Microsoft.Testing.Platform.MSBuild.xml
│       │       │       ├── Microsoft.Testing.Platform.xml
│       │       │       ├── Microsoft.Win32.Registry.dll
│       │       │       ├── System.Security.AccessControl.dll
│       │       │       └── System.Security.Principal.Windows.dll
│       │       ├── build
│       │       │   ├── Microsoft.Testing.Platform.MSBuild.props
│       │       │   └── Microsoft.Testing.Platform.MSBuild.targets
│       │       ├── buildMultiTargeting
│       │       │   ├── Microsoft.Testing.Platform.MSBuild.CustomTestTarget.targets
│       │       │   ├── Microsoft.Testing.Platform.MSBuild.props
│       │       │   ├── Microsoft.Testing.Platform.MSBuild.targets
│       │       │   └── Microsoft.Testing.Platform.MSBuild.VSTest.targets
│       │       ├── buildTransitive
│       │       │   ├── Microsoft.Testing.Platform.MSBuild.props
│       │       │   └── Microsoft.Testing.Platform.MSBuild.targets
│       │       ├── lib
│       │       │   ├── net6.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── Microsoft.Testing.Extensions.MSBuild.dll
│       │       │   │   └── Microsoft.Testing.Extensions.MSBuild.xml
│       │       │   ├── net7.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── Microsoft.Testing.Extensions.MSBuild.dll
│       │       │   │   └── Microsoft.Testing.Extensions.MSBuild.xml
│       │       │   ├── net8.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── Microsoft.Testing.Extensions.MSBuild.dll
│       │       │   │   └── Microsoft.Testing.Extensions.MSBuild.xml
│       │       │   ├── net9.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │   │   ├── Microsoft.Testing.Extensions.MSBuild.dll
│       │       │   │   └── Microsoft.Testing.Extensions.MSBuild.xml
│       │       │   └── netstandard2.0
│       │       │       ├── cs
│       │       │       │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │       ├── de
│       │       │       │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │       ├── es
│       │       │       │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │       ├── fr
│       │       │       │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │       ├── it
│       │       │       │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │       ├── ja
│       │       │       │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │       ├── ko
│       │       │       │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │       ├── pl
│       │       │       │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │       ├── pt-BR
│       │       │       │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │       ├── ru
│       │       │       │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │       ├── tr
│       │       │       │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │       ├── zh-Hans
│       │       │       │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │       ├── zh-Hant
│       │       │       │   └── Microsoft.Testing.Extensions.MSBuild.resources.dll
│       │       │       ├── Microsoft.Testing.Extensions.MSBuild.dll
│       │       │       └── Microsoft.Testing.Extensions.MSBuild.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.testing.platform.msbuild.1.9.0.nupkg
│       │       ├── microsoft.testing.platform.msbuild.1.9.0.nupkg.sha512
│       │       ├── microsoft.testing.platform.msbuild.nuspec
│       │       └── PACKAGE.md
│       ├── microsoft.testplatform.adapterutilities
│       │   └── 17.13.0
│       │       ├── lib
│       │       │   ├── net462
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   └── Microsoft.TestPlatform.AdapterUtilities.dll
│       │       │   ├── net6.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   └── Microsoft.TestPlatform.AdapterUtilities.dll
│       │       │   ├── net8.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   └── Microsoft.TestPlatform.AdapterUtilities.dll
│       │       │   ├── net9.0
│       │       │   │   ├── cs
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │   │   └── Microsoft.TestPlatform.AdapterUtilities.dll
│       │       │   └── netstandard2.0
│       │       │       ├── cs
│       │       │       │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │       ├── de
│       │       │       │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │       ├── es
│       │       │       │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │       ├── fr
│       │       │       │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │       ├── it
│       │       │       │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │       ├── ja
│       │       │       │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │       ├── ko
│       │       │       │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │       ├── pl
│       │       │       │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │       ├── pt-BR
│       │       │       │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │       ├── ru
│       │       │       │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │       ├── tr
│       │       │       │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │       ├── zh-Hans
│       │       │       │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │       ├── zh-Hant
│       │       │       │   └── Microsoft.TestPlatform.AdapterUtilities.resources.dll
│       │       │       └── Microsoft.TestPlatform.AdapterUtilities.dll
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.testplatform.adapterutilities.17.13.0.nupkg
│       │       ├── microsoft.testplatform.adapterutilities.17.13.0.nupkg.sha512
│       │       └── microsoft.testplatform.adapterutilities.nuspec
│       ├── microsoft.testplatform.objectmodel
│       │   ├── 17.13.0
│       │   │   ├── lib
│       │   │   │   ├── net462
│       │   │   │   │   ├── cs
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── de
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── es
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── fr
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── it
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── ja
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── ko
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── pl
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── pt-BR
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── ru
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── tr
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── zh-Hans
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── zh-Hant
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.dll
│       │   │   │   │   ├── Microsoft.TestPlatform.PlatformAbstractions.dll
│       │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.dll
│       │   │   │   ├── netcoreapp3.1
│       │   │   │   │   ├── cs
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── de
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── es
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── fr
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── it
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── ja
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── ko
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── pl
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── pt-BR
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── ru
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── tr
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── zh-Hans
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── zh-Hant
│       │   │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.dll
│       │   │   │   │   ├── Microsoft.TestPlatform.PlatformAbstractions.dll
│       │   │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.dll
│       │   │   │   └── netstandard2.0
│       │   │   │       ├── cs
│       │   │   │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │       ├── de
│       │   │   │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │       ├── es
│       │   │   │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │       ├── fr
│       │   │   │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │       ├── it
│       │   │   │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │       ├── ja
│       │   │   │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │       ├── ko
│       │   │   │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │       ├── pl
│       │   │   │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │       ├── pt-BR
│       │   │   │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │       ├── ru
│       │   │   │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │       ├── tr
│       │   │   │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │       ├── zh-Hans
│       │   │   │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │       ├── zh-Hant
│       │   │   │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │   │   │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │   │   │       ├── Microsoft.TestPlatform.CoreUtilities.dll
│       │   │   │       ├── Microsoft.TestPlatform.PlatformAbstractions.dll
│       │   │   │       └── Microsoft.VisualStudio.TestPlatform.ObjectModel.dll
│       │   │   ├── .nupkg.metadata
│       │   │   ├── .signature.p7s
│       │   │   ├── Icon.png
│       │   │   ├── microsoft.testplatform.objectmodel.17.13.0.nupkg
│       │   │   ├── microsoft.testplatform.objectmodel.17.13.0.nupkg.sha512
│       │   │   └── microsoft.testplatform.objectmodel.nuspec
│       │   └── 18.5.1
│       │       ├── lib
│       │       │   ├── net462
│       │       │   │   ├── cs
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── Microsoft.TestPlatform.CoreUtilities.dll
│       │       │   │   ├── Microsoft.TestPlatform.PlatformAbstractions.dll
│       │       │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.dll
│       │       │   ├── net8.0
│       │       │   │   ├── cs
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── de
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── es
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── fr
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── it
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── ja
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── ko
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── pl
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── pt-BR
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── ru
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── tr
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── zh-Hans
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── zh-Hant
│       │       │   │   │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │   │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │   │   ├── Microsoft.TestPlatform.CoreUtilities.dll
│       │       │   │   ├── Microsoft.TestPlatform.PlatformAbstractions.dll
│       │       │   │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.dll
│       │       │   └── netstandard2.0
│       │       │       ├── cs
│       │       │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │       ├── de
│       │       │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │       ├── es
│       │       │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │       ├── fr
│       │       │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │       ├── it
│       │       │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │       ├── ja
│       │       │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │       ├── ko
│       │       │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │       ├── pl
│       │       │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │       ├── pt-BR
│       │       │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │       ├── ru
│       │       │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │       ├── tr
│       │       │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │       ├── zh-Hans
│       │       │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │       ├── zh-Hant
│       │       │       │   ├── Microsoft.TestPlatform.CoreUtilities.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.ObjectModel.resources.dll
│       │       │       ├── Microsoft.TestPlatform.CoreUtilities.dll
│       │       │       ├── Microsoft.TestPlatform.PlatformAbstractions.dll
│       │       │       └── Microsoft.VisualStudio.TestPlatform.ObjectModel.dll
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.testplatform.objectmodel.18.5.1.nupkg
│       │       ├── microsoft.testplatform.objectmodel.18.5.1.nupkg.sha512
│       │       └── microsoft.testplatform.objectmodel.nuspec
│       ├── microsoft.testplatform.testhost
│       │   └── 18.5.1
│       │       ├── build
│       │       │   └── net8.0
│       │       │       ├── x64
│       │       │       │   ├── testhost.dll
│       │       │       │   └── testhost.exe
│       │       │       ├── x86
│       │       │       │   ├── testhost.x86.dll
│       │       │       │   └── testhost.x86.exe
│       │       │       ├── Microsoft.TestPlatform.TestHost.props
│       │       │       └── Microsoft.TestPlatform.TestHost.targets
│       │       ├── lib
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   └── net8.0
│       │       │       ├── cs
│       │       │       │   ├── Microsoft.TestPlatform.CommunicationUtilities.resources.dll
│       │       │       │   ├── Microsoft.TestPlatform.CrossPlatEngine.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.Common.resources.dll
│       │       │       ├── de
│       │       │       │   ├── Microsoft.TestPlatform.CommunicationUtilities.resources.dll
│       │       │       │   ├── Microsoft.TestPlatform.CrossPlatEngine.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.Common.resources.dll
│       │       │       ├── es
│       │       │       │   ├── Microsoft.TestPlatform.CommunicationUtilities.resources.dll
│       │       │       │   ├── Microsoft.TestPlatform.CrossPlatEngine.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.Common.resources.dll
│       │       │       ├── fr
│       │       │       │   ├── Microsoft.TestPlatform.CommunicationUtilities.resources.dll
│       │       │       │   ├── Microsoft.TestPlatform.CrossPlatEngine.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.Common.resources.dll
│       │       │       ├── it
│       │       │       │   ├── Microsoft.TestPlatform.CommunicationUtilities.resources.dll
│       │       │       │   ├── Microsoft.TestPlatform.CrossPlatEngine.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.Common.resources.dll
│       │       │       ├── ja
│       │       │       │   ├── Microsoft.TestPlatform.CommunicationUtilities.resources.dll
│       │       │       │   ├── Microsoft.TestPlatform.CrossPlatEngine.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.Common.resources.dll
│       │       │       ├── ko
│       │       │       │   ├── Microsoft.TestPlatform.CommunicationUtilities.resources.dll
│       │       │       │   ├── Microsoft.TestPlatform.CrossPlatEngine.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.Common.resources.dll
│       │       │       ├── pl
│       │       │       │   ├── Microsoft.TestPlatform.CommunicationUtilities.resources.dll
│       │       │       │   ├── Microsoft.TestPlatform.CrossPlatEngine.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.Common.resources.dll
│       │       │       ├── pt-BR
│       │       │       │   ├── Microsoft.TestPlatform.CommunicationUtilities.resources.dll
│       │       │       │   ├── Microsoft.TestPlatform.CrossPlatEngine.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.Common.resources.dll
│       │       │       ├── ru
│       │       │       │   ├── Microsoft.TestPlatform.CommunicationUtilities.resources.dll
│       │       │       │   ├── Microsoft.TestPlatform.CrossPlatEngine.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.Common.resources.dll
│       │       │       ├── tr
│       │       │       │   ├── Microsoft.TestPlatform.CommunicationUtilities.resources.dll
│       │       │       │   ├── Microsoft.TestPlatform.CrossPlatEngine.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.Common.resources.dll
│       │       │       ├── x64
│       │       │       │   └── msdia140.dll
│       │       │       ├── x86
│       │       │       │   └── msdia140.dll
│       │       │       ├── zh-Hans
│       │       │       │   ├── Microsoft.TestPlatform.CommunicationUtilities.resources.dll
│       │       │       │   ├── Microsoft.TestPlatform.CrossPlatEngine.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.Common.resources.dll
│       │       │       ├── zh-Hant
│       │       │       │   ├── Microsoft.TestPlatform.CommunicationUtilities.resources.dll
│       │       │       │   ├── Microsoft.TestPlatform.CrossPlatEngine.resources.dll
│       │       │       │   └── Microsoft.VisualStudio.TestPlatform.Common.resources.dll
│       │       │       ├── Microsoft.TestPlatform.CommunicationUtilities.dll
│       │       │       ├── Microsoft.TestPlatform.CoreUtilities.dll
│       │       │       ├── Microsoft.TestPlatform.CrossPlatEngine.dll
│       │       │       ├── Microsoft.TestPlatform.PlatformAbstractions.dll
│       │       │       ├── Microsoft.TestPlatform.Utilities.dll
│       │       │       ├── Microsoft.VisualStudio.TestPlatform.Common.dll
│       │       │       ├── Microsoft.VisualStudio.TestPlatform.ObjectModel.dll
│       │       │       ├── testhost.deps.json
│       │       │       └── testhost.dll
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── microsoft.testplatform.testhost.18.5.1.nupkg
│       │       ├── microsoft.testplatform.testhost.18.5.1.nupkg.sha512
│       │       ├── microsoft.testplatform.testhost.nuspec
│       │       └── ThirdPartyNotices.txt
│       ├── microsoft.windowsdesktop.app.runtime.win-x64
│       │   └── 10.0.8
│       │       ├── data
│       │       │   └── RuntimeList.xml
│       │       ├── runtimes
│       │       │   └── win-x64
│       │       │       ├── lib
│       │       │       │   └── net10.0
│       │       │       │       ├── cs
│       │       │       │       │   ├── Microsoft.VisualBasic.Forms.resources.dll
│       │       │       │       │   ├── PresentationCore.resources.dll
│       │       │       │       │   ├── PresentationFramework.resources.dll
│       │       │       │       │   ├── PresentationUI.resources.dll
│       │       │       │       │   ├── ReachFramework.resources.dll
│       │       │       │       │   ├── System.Windows.Controls.Ribbon.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Design.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Primitives.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.resources.dll
│       │       │       │       │   ├── System.Windows.Input.Manipulations.resources.dll
│       │       │       │       │   ├── System.Xaml.resources.dll
│       │       │       │       │   ├── UIAutomationClient.resources.dll
│       │       │       │       │   ├── UIAutomationClientSideProviders.resources.dll
│       │       │       │       │   ├── UIAutomationProvider.resources.dll
│       │       │       │       │   ├── UIAutomationTypes.resources.dll
│       │       │       │       │   ├── WindowsBase.resources.dll
│       │       │       │       │   └── WindowsFormsIntegration.resources.dll
│       │       │       │       ├── de
│       │       │       │       │   ├── Microsoft.VisualBasic.Forms.resources.dll
│       │       │       │       │   ├── PresentationCore.resources.dll
│       │       │       │       │   ├── PresentationFramework.resources.dll
│       │       │       │       │   ├── PresentationUI.resources.dll
│       │       │       │       │   ├── ReachFramework.resources.dll
│       │       │       │       │   ├── System.Windows.Controls.Ribbon.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Design.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Primitives.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.resources.dll
│       │       │       │       │   ├── System.Windows.Input.Manipulations.resources.dll
│       │       │       │       │   ├── System.Xaml.resources.dll
│       │       │       │       │   ├── UIAutomationClient.resources.dll
│       │       │       │       │   ├── UIAutomationClientSideProviders.resources.dll
│       │       │       │       │   ├── UIAutomationProvider.resources.dll
│       │       │       │       │   ├── UIAutomationTypes.resources.dll
│       │       │       │       │   ├── WindowsBase.resources.dll
│       │       │       │       │   └── WindowsFormsIntegration.resources.dll
│       │       │       │       ├── es
│       │       │       │       │   ├── Microsoft.VisualBasic.Forms.resources.dll
│       │       │       │       │   ├── PresentationCore.resources.dll
│       │       │       │       │   ├── PresentationFramework.resources.dll
│       │       │       │       │   ├── PresentationUI.resources.dll
│       │       │       │       │   ├── ReachFramework.resources.dll
│       │       │       │       │   ├── System.Windows.Controls.Ribbon.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Design.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Primitives.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.resources.dll
│       │       │       │       │   ├── System.Windows.Input.Manipulations.resources.dll
│       │       │       │       │   ├── System.Xaml.resources.dll
│       │       │       │       │   ├── UIAutomationClient.resources.dll
│       │       │       │       │   ├── UIAutomationClientSideProviders.resources.dll
│       │       │       │       │   ├── UIAutomationProvider.resources.dll
│       │       │       │       │   ├── UIAutomationTypes.resources.dll
│       │       │       │       │   ├── WindowsBase.resources.dll
│       │       │       │       │   └── WindowsFormsIntegration.resources.dll
│       │       │       │       ├── fr
│       │       │       │       │   ├── Microsoft.VisualBasic.Forms.resources.dll
│       │       │       │       │   ├── PresentationCore.resources.dll
│       │       │       │       │   ├── PresentationFramework.resources.dll
│       │       │       │       │   ├── PresentationUI.resources.dll
│       │       │       │       │   ├── ReachFramework.resources.dll
│       │       │       │       │   ├── System.Windows.Controls.Ribbon.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Design.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Primitives.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.resources.dll
│       │       │       │       │   ├── System.Windows.Input.Manipulations.resources.dll
│       │       │       │       │   ├── System.Xaml.resources.dll
│       │       │       │       │   ├── UIAutomationClient.resources.dll
│       │       │       │       │   ├── UIAutomationClientSideProviders.resources.dll
│       │       │       │       │   ├── UIAutomationProvider.resources.dll
│       │       │       │       │   ├── UIAutomationTypes.resources.dll
│       │       │       │       │   ├── WindowsBase.resources.dll
│       │       │       │       │   └── WindowsFormsIntegration.resources.dll
│       │       │       │       ├── it
│       │       │       │       │   ├── Microsoft.VisualBasic.Forms.resources.dll
│       │       │       │       │   ├── PresentationCore.resources.dll
│       │       │       │       │   ├── PresentationFramework.resources.dll
│       │       │       │       │   ├── PresentationUI.resources.dll
│       │       │       │       │   ├── ReachFramework.resources.dll
│       │       │       │       │   ├── System.Windows.Controls.Ribbon.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Design.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Primitives.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.resources.dll
│       │       │       │       │   ├── System.Windows.Input.Manipulations.resources.dll
│       │       │       │       │   ├── System.Xaml.resources.dll
│       │       │       │       │   ├── UIAutomationClient.resources.dll
│       │       │       │       │   ├── UIAutomationClientSideProviders.resources.dll
│       │       │       │       │   ├── UIAutomationProvider.resources.dll
│       │       │       │       │   ├── UIAutomationTypes.resources.dll
│       │       │       │       │   ├── WindowsBase.resources.dll
│       │       │       │       │   └── WindowsFormsIntegration.resources.dll
│       │       │       │       ├── ja
│       │       │       │       │   ├── Microsoft.VisualBasic.Forms.resources.dll
│       │       │       │       │   ├── PresentationCore.resources.dll
│       │       │       │       │   ├── PresentationFramework.resources.dll
│       │       │       │       │   ├── PresentationUI.resources.dll
│       │       │       │       │   ├── ReachFramework.resources.dll
│       │       │       │       │   ├── System.Windows.Controls.Ribbon.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Design.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Primitives.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.resources.dll
│       │       │       │       │   ├── System.Windows.Input.Manipulations.resources.dll
│       │       │       │       │   ├── System.Xaml.resources.dll
│       │       │       │       │   ├── UIAutomationClient.resources.dll
│       │       │       │       │   ├── UIAutomationClientSideProviders.resources.dll
│       │       │       │       │   ├── UIAutomationProvider.resources.dll
│       │       │       │       │   ├── UIAutomationTypes.resources.dll
│       │       │       │       │   ├── WindowsBase.resources.dll
│       │       │       │       │   └── WindowsFormsIntegration.resources.dll
│       │       │       │       ├── ko
│       │       │       │       │   ├── Microsoft.VisualBasic.Forms.resources.dll
│       │       │       │       │   ├── PresentationCore.resources.dll
│       │       │       │       │   ├── PresentationFramework.resources.dll
│       │       │       │       │   ├── PresentationUI.resources.dll
│       │       │       │       │   ├── ReachFramework.resources.dll
│       │       │       │       │   ├── System.Windows.Controls.Ribbon.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Design.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Primitives.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.resources.dll
│       │       │       │       │   ├── System.Windows.Input.Manipulations.resources.dll
│       │       │       │       │   ├── System.Xaml.resources.dll
│       │       │       │       │   ├── UIAutomationClient.resources.dll
│       │       │       │       │   ├── UIAutomationClientSideProviders.resources.dll
│       │       │       │       │   ├── UIAutomationProvider.resources.dll
│       │       │       │       │   ├── UIAutomationTypes.resources.dll
│       │       │       │       │   ├── WindowsBase.resources.dll
│       │       │       │       │   └── WindowsFormsIntegration.resources.dll
│       │       │       │       ├── pl
│       │       │       │       │   ├── Microsoft.VisualBasic.Forms.resources.dll
│       │       │       │       │   ├── PresentationCore.resources.dll
│       │       │       │       │   ├── PresentationFramework.resources.dll
│       │       │       │       │   ├── PresentationUI.resources.dll
│       │       │       │       │   ├── ReachFramework.resources.dll
│       │       │       │       │   ├── System.Windows.Controls.Ribbon.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Design.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Primitives.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.resources.dll
│       │       │       │       │   ├── System.Windows.Input.Manipulations.resources.dll
│       │       │       │       │   ├── System.Xaml.resources.dll
│       │       │       │       │   ├── UIAutomationClient.resources.dll
│       │       │       │       │   ├── UIAutomationClientSideProviders.resources.dll
│       │       │       │       │   ├── UIAutomationProvider.resources.dll
│       │       │       │       │   ├── UIAutomationTypes.resources.dll
│       │       │       │       │   ├── WindowsBase.resources.dll
│       │       │       │       │   └── WindowsFormsIntegration.resources.dll
│       │       │       │       ├── pt-BR
│       │       │       │       │   ├── Microsoft.VisualBasic.Forms.resources.dll
│       │       │       │       │   ├── PresentationCore.resources.dll
│       │       │       │       │   ├── PresentationFramework.resources.dll
│       │       │       │       │   ├── PresentationUI.resources.dll
│       │       │       │       │   ├── ReachFramework.resources.dll
│       │       │       │       │   ├── System.Windows.Controls.Ribbon.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Design.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Primitives.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.resources.dll
│       │       │       │       │   ├── System.Windows.Input.Manipulations.resources.dll
│       │       │       │       │   ├── System.Xaml.resources.dll
│       │       │       │       │   ├── UIAutomationClient.resources.dll
│       │       │       │       │   ├── UIAutomationClientSideProviders.resources.dll
│       │       │       │       │   ├── UIAutomationProvider.resources.dll
│       │       │       │       │   ├── UIAutomationTypes.resources.dll
│       │       │       │       │   ├── WindowsBase.resources.dll
│       │       │       │       │   └── WindowsFormsIntegration.resources.dll
│       │       │       │       ├── ru
│       │       │       │       │   ├── Microsoft.VisualBasic.Forms.resources.dll
│       │       │       │       │   ├── PresentationCore.resources.dll
│       │       │       │       │   ├── PresentationFramework.resources.dll
│       │       │       │       │   ├── PresentationUI.resources.dll
│       │       │       │       │   ├── ReachFramework.resources.dll
│       │       │       │       │   ├── System.Windows.Controls.Ribbon.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Design.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Primitives.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.resources.dll
│       │       │       │       │   ├── System.Windows.Input.Manipulations.resources.dll
│       │       │       │       │   ├── System.Xaml.resources.dll
│       │       │       │       │   ├── UIAutomationClient.resources.dll
│       │       │       │       │   ├── UIAutomationClientSideProviders.resources.dll
│       │       │       │       │   ├── UIAutomationProvider.resources.dll
│       │       │       │       │   ├── UIAutomationTypes.resources.dll
│       │       │       │       │   ├── WindowsBase.resources.dll
│       │       │       │       │   └── WindowsFormsIntegration.resources.dll
│       │       │       │       ├── tr
│       │       │       │       │   ├── Microsoft.VisualBasic.Forms.resources.dll
│       │       │       │       │   ├── PresentationCore.resources.dll
│       │       │       │       │   ├── PresentationFramework.resources.dll
│       │       │       │       │   ├── PresentationUI.resources.dll
│       │       │       │       │   ├── ReachFramework.resources.dll
│       │       │       │       │   ├── System.Windows.Controls.Ribbon.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Design.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Primitives.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.resources.dll
│       │       │       │       │   ├── System.Windows.Input.Manipulations.resources.dll
│       │       │       │       │   ├── System.Xaml.resources.dll
│       │       │       │       │   ├── UIAutomationClient.resources.dll
│       │       │       │       │   ├── UIAutomationClientSideProviders.resources.dll
│       │       │       │       │   ├── UIAutomationProvider.resources.dll
│       │       │       │       │   ├── UIAutomationTypes.resources.dll
│       │       │       │       │   ├── WindowsBase.resources.dll
│       │       │       │       │   └── WindowsFormsIntegration.resources.dll
│       │       │       │       ├── zh-Hans
│       │       │       │       │   ├── Microsoft.VisualBasic.Forms.resources.dll
│       │       │       │       │   ├── PresentationCore.resources.dll
│       │       │       │       │   ├── PresentationFramework.resources.dll
│       │       │       │       │   ├── PresentationUI.resources.dll
│       │       │       │       │   ├── ReachFramework.resources.dll
│       │       │       │       │   ├── System.Windows.Controls.Ribbon.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Design.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Primitives.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.resources.dll
│       │       │       │       │   ├── System.Windows.Input.Manipulations.resources.dll
│       │       │       │       │   ├── System.Xaml.resources.dll
│       │       │       │       │   ├── UIAutomationClient.resources.dll
│       │       │       │       │   ├── UIAutomationClientSideProviders.resources.dll
│       │       │       │       │   ├── UIAutomationProvider.resources.dll
│       │       │       │       │   ├── UIAutomationTypes.resources.dll
│       │       │       │       │   ├── WindowsBase.resources.dll
│       │       │       │       │   └── WindowsFormsIntegration.resources.dll
│       │       │       │       ├── zh-Hant
│       │       │       │       │   ├── Microsoft.VisualBasic.Forms.resources.dll
│       │       │       │       │   ├── PresentationCore.resources.dll
│       │       │       │       │   ├── PresentationFramework.resources.dll
│       │       │       │       │   ├── PresentationUI.resources.dll
│       │       │       │       │   ├── ReachFramework.resources.dll
│       │       │       │       │   ├── System.Windows.Controls.Ribbon.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Design.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.Primitives.resources.dll
│       │       │       │       │   ├── System.Windows.Forms.resources.dll
│       │       │       │       │   ├── System.Windows.Input.Manipulations.resources.dll
│       │       │       │       │   ├── System.Xaml.resources.dll
│       │       │       │       │   ├── UIAutomationClient.resources.dll
│       │       │       │       │   ├── UIAutomationClientSideProviders.resources.dll
│       │       │       │       │   ├── UIAutomationProvider.resources.dll
│       │       │       │       │   ├── UIAutomationTypes.resources.dll
│       │       │       │       │   ├── WindowsBase.resources.dll
│       │       │       │       │   └── WindowsFormsIntegration.resources.dll
│       │       │       │       ├── Accessibility.dll
│       │       │       │       ├── DirectWriteForwarder.dll
│       │       │       │       ├── Microsoft.VisualBasic.dll
│       │       │       │       ├── Microsoft.VisualBasic.Forms.dll
│       │       │       │       ├── Microsoft.Win32.Registry.AccessControl.dll
│       │       │       │       ├── Microsoft.Win32.SystemEvents.dll
│       │       │       │       ├── Microsoft.WindowsDesktop.App.deps.json
│       │       │       │       ├── Microsoft.WindowsDesktop.App.runtimeconfig.json
│       │       │       │       ├── PresentationCore.dll
│       │       │       │       ├── PresentationFramework-SystemCore.dll
│       │       │       │       ├── PresentationFramework-SystemData.dll
│       │       │       │       ├── PresentationFramework-SystemDrawing.dll
│       │       │       │       ├── PresentationFramework-SystemXml.dll
│       │       │       │       ├── PresentationFramework-SystemXmlLinq.dll
│       │       │       │       ├── PresentationFramework.Aero.dll
│       │       │       │       ├── PresentationFramework.Aero2.dll
│       │       │       │       ├── PresentationFramework.AeroLite.dll
│       │       │       │       ├── PresentationFramework.Classic.dll
│       │       │       │       ├── PresentationFramework.dll
│       │       │       │       ├── PresentationFramework.Fluent.dll
│       │       │       │       ├── PresentationFramework.Luna.dll
│       │       │       │       ├── PresentationFramework.Royale.dll
│       │       │       │       ├── PresentationUI.dll
│       │       │       │       ├── ReachFramework.dll
│       │       │       │       ├── System.CodeDom.dll
│       │       │       │       ├── System.Configuration.ConfigurationManager.dll
│       │       │       │       ├── System.Design.dll
│       │       │       │       ├── System.Diagnostics.EventLog.dll
│       │       │       │       ├── System.Diagnostics.EventLog.Messages.dll
│       │       │       │       ├── System.Diagnostics.PerformanceCounter.dll
│       │       │       │       ├── System.DirectoryServices.dll
│       │       │       │       ├── System.Drawing.Common.dll
│       │       │       │       ├── System.Drawing.Design.dll
│       │       │       │       ├── System.Drawing.dll
│       │       │       │       ├── System.Formats.Nrbf.dll
│       │       │       │       ├── System.IO.Packaging.dll
│       │       │       │       ├── System.Printing.dll
│       │       │       │       ├── System.Private.Windows.Core.dll
│       │       │       │       ├── System.Private.Windows.GdiPlus.dll
│       │       │       │       ├── System.Resources.Extensions.dll
│       │       │       │       ├── System.Security.Cryptography.Pkcs.dll
│       │       │       │       ├── System.Security.Cryptography.ProtectedData.dll
│       │       │       │       ├── System.Security.Cryptography.Xml.dll
│       │       │       │       ├── System.Security.Permissions.dll
│       │       │       │       ├── System.Windows.Controls.Ribbon.dll
│       │       │       │       ├── System.Windows.Extensions.dll
│       │       │       │       ├── System.Windows.Forms.Design.dll
│       │       │       │       ├── System.Windows.Forms.Design.Editors.dll
│       │       │       │       ├── System.Windows.Forms.dll
│       │       │       │       ├── System.Windows.Forms.Primitives.dll
│       │       │       │       ├── System.Windows.Input.Manipulations.dll
│       │       │       │       ├── System.Windows.Presentation.dll
│       │       │       │       ├── System.Windows.Primitives.dll
│       │       │       │       ├── System.Xaml.dll
│       │       │       │       ├── UIAutomationClient.dll
│       │       │       │       ├── UIAutomationClientSideProviders.dll
│       │       │       │       ├── UIAutomationProvider.dll
│       │       │       │       ├── UIAutomationTypes.dll
│       │       │       │       ├── WindowsBase.dll
│       │       │       │       └── WindowsFormsIntegration.dll
│       │       │       └── native
│       │       │           ├── D3DCompiler_47_cor3.dll
│       │       │           ├── PenImc_cor3.dll
│       │       │           ├── PresentationNative_cor3.dll
│       │       │           ├── vcruntime140_cor3.dll
│       │       │           └── wpfgfx_cor3.dll
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── LICENSE
│       │       ├── microsoft.windowsdesktop.app.runtime.win-x64.10.0.8.nupkg
│       │       ├── microsoft.windowsdesktop.app.runtime.win-x64.10.0.8.nupkg.sha512
│       │       ├── microsoft.windowsdesktop.app.runtime.win-x64.nuspec
│       │       └── Microsoft.WindowsDesktop.App.versions.txt
│       ├── mono.cecil
│       │   └── 0.11.4
│       │       ├── lib
│       │       │   ├── net40
│       │       │   │   ├── Mono.Cecil.dll
│       │       │   │   ├── Mono.Cecil.Mdb.dll
│       │       │   │   ├── Mono.Cecil.Mdb.pdb
│       │       │   │   ├── Mono.Cecil.pdb
│       │       │   │   ├── Mono.Cecil.Pdb.dll
│       │       │   │   ├── Mono.Cecil.Pdb.pdb
│       │       │   │   ├── Mono.Cecil.Rocks.dll
│       │       │   │   └── Mono.Cecil.Rocks.pdb
│       │       │   └── netstandard2.0
│       │       │       ├── Mono.Cecil.dll
│       │       │       ├── Mono.Cecil.Mdb.dll
│       │       │       ├── Mono.Cecil.Mdb.pdb
│       │       │       ├── Mono.Cecil.pdb
│       │       │       ├── Mono.Cecil.Pdb.dll
│       │       │       ├── Mono.Cecil.Pdb.pdb
│       │       │       ├── Mono.Cecil.Rocks.dll
│       │       │       └── Mono.Cecil.Rocks.pdb
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── mono.cecil.0.11.4.nupkg
│       │       ├── mono.cecil.0.11.4.nupkg.sha512
│       │       └── mono.cecil.nuspec
│       ├── mqttnet
│       │   └── 5.1.0.1559
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── MQTTnet.dll
│       │       │   │   └── MQTTnet.xml
│       │       │   └── net8.0
│       │       │       ├── MQTTnet.dll
│       │       │       └── MQTTnet.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── LICENSE
│       │       ├── mqttnet.5.1.0.1559.nupkg
│       │       ├── mqttnet.5.1.0.1559.nupkg.sha512
│       │       ├── mqttnet.nuspec
│       │       └── nuget.png
│       ├── newtonsoft.json
│       │   └── 13.0.4
│       │       ├── lib
│       │       │   ├── net20
│       │       │   │   ├── Newtonsoft.Json.dll
│       │       │   │   └── Newtonsoft.Json.xml
│       │       │   ├── net35
│       │       │   │   ├── Newtonsoft.Json.dll
│       │       │   │   └── Newtonsoft.Json.xml
│       │       │   ├── net40
│       │       │   │   ├── Newtonsoft.Json.dll
│       │       │   │   └── Newtonsoft.Json.xml
│       │       │   ├── net45
│       │       │   │   ├── Newtonsoft.Json.dll
│       │       │   │   └── Newtonsoft.Json.xml
│       │       │   ├── net6.0
│       │       │   │   ├── Newtonsoft.Json.dll
│       │       │   │   └── Newtonsoft.Json.xml
│       │       │   ├── netstandard1.0
│       │       │   │   ├── Newtonsoft.Json.dll
│       │       │   │   └── Newtonsoft.Json.xml
│       │       │   ├── netstandard1.3
│       │       │   │   ├── Newtonsoft.Json.dll
│       │       │   │   └── Newtonsoft.Json.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Newtonsoft.Json.dll
│       │       │       └── Newtonsoft.Json.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── LICENSE.md
│       │       ├── newtonsoft.json.13.0.4.nupkg
│       │       ├── newtonsoft.json.13.0.4.nupkg.sha512
│       │       ├── newtonsoft.json.nuspec
│       │       ├── packageIcon.png
│       │       └── README.md
│       ├── npgsql
│       │   └── 10.0.2
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Npgsql.dll
│       │       │   │   └── Npgsql.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Npgsql.dll
│       │       │   │   └── Npgsql.xml
│       │       │   └── net9.0
│       │       │       ├── Npgsql.dll
│       │       │       └── Npgsql.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── npgsql.10.0.2.nupkg
│       │       ├── npgsql.10.0.2.nupkg.sha512
│       │       ├── npgsql.nuspec
│       │       ├── postgresql.png
│       │       └── README.md
│       ├── npgsql.fsharp
│       │   └── 8.0.0
│       │       ├── lib
│       │       │   └── net8.0
│       │       │       ├── Npgsql.FSharp.dll
│       │       │       └── Npgsql.FSharp.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── npgsql.fsharp.8.0.0.nupkg
│       │       ├── npgsql.fsharp.8.0.0.nupkg.sha512
│       │       └── npgsql.fsharp.nuspec
│       ├── opentelemetry
│       │   └── 1.15.3
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── OpenTelemetry.dll
│       │       │   │   ├── OpenTelemetry.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.xml
│       │       │   ├── net462
│       │       │   │   ├── OpenTelemetry.dll
│       │       │   │   ├── OpenTelemetry.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.xml
│       │       │   ├── net8.0
│       │       │   │   ├── OpenTelemetry.dll
│       │       │   │   ├── OpenTelemetry.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.xml
│       │       │   ├── net9.0
│       │       │   │   ├── OpenTelemetry.dll
│       │       │   │   ├── OpenTelemetry.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── OpenTelemetry.dll
│       │       │   │   ├── OpenTelemetry.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.xml
│       │       │   └── netstandard2.1
│       │       │       ├── OpenTelemetry.dll
│       │       │       ├── OpenTelemetry.dll.sigstore.json
│       │       │       └── OpenTelemetry.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── LICENSE.TXT
│       │       ├── opentelemetry-icon-color.png
│       │       ├── opentelemetry.1.15.3.nupkg
│       │       ├── opentelemetry.1.15.3.nupkg.sha512
│       │       ├── opentelemetry.nuspec
│       │       ├── README.md
│       │       └── THIRD-PARTY-NOTICES.TXT
│       ├── opentelemetry.api
│       │   └── 1.15.3
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── OpenTelemetry.Api.dll
│       │       │   │   ├── OpenTelemetry.Api.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Api.xml
│       │       │   ├── net462
│       │       │   │   ├── OpenTelemetry.Api.dll
│       │       │   │   ├── OpenTelemetry.Api.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Api.xml
│       │       │   ├── net8.0
│       │       │   │   ├── OpenTelemetry.Api.dll
│       │       │   │   ├── OpenTelemetry.Api.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Api.xml
│       │       │   ├── net9.0
│       │       │   │   ├── OpenTelemetry.Api.dll
│       │       │   │   ├── OpenTelemetry.Api.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Api.xml
│       │       │   └── netstandard2.0
│       │       │       ├── OpenTelemetry.Api.dll
│       │       │       ├── OpenTelemetry.Api.dll.sigstore.json
│       │       │       └── OpenTelemetry.Api.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── LICENSE.TXT
│       │       ├── opentelemetry-icon-color.png
│       │       ├── opentelemetry.api.1.15.3.nupkg
│       │       ├── opentelemetry.api.1.15.3.nupkg.sha512
│       │       ├── opentelemetry.api.nuspec
│       │       ├── README.md
│       │       └── THIRD-PARTY-NOTICES.TXT
│       ├── opentelemetry.api.providerbuilderextensions
│       │   ├── 1.15.0
│       │   │   ├── lib
│       │   │   │   ├── net10.0
│       │   │   │   │   ├── OpenTelemetry.Api.ProviderBuilderExtensions.dll
│       │   │   │   │   ├── OpenTelemetry.Api.ProviderBuilderExtensions.dll.sigstore.json
│       │   │   │   │   └── OpenTelemetry.Api.ProviderBuilderExtensions.xml
│       │   │   │   ├── net462
│       │   │   │   │   ├── OpenTelemetry.Api.ProviderBuilderExtensions.dll
│       │   │   │   │   ├── OpenTelemetry.Api.ProviderBuilderExtensions.dll.sigstore.json
│       │   │   │   │   └── OpenTelemetry.Api.ProviderBuilderExtensions.xml
│       │   │   │   ├── net8.0
│       │   │   │   │   ├── OpenTelemetry.Api.ProviderBuilderExtensions.dll
│       │   │   │   │   ├── OpenTelemetry.Api.ProviderBuilderExtensions.dll.sigstore.json
│       │   │   │   │   └── OpenTelemetry.Api.ProviderBuilderExtensions.xml
│       │   │   │   ├── net9.0
│       │   │   │   │   ├── OpenTelemetry.Api.ProviderBuilderExtensions.dll
│       │   │   │   │   ├── OpenTelemetry.Api.ProviderBuilderExtensions.dll.sigstore.json
│       │   │   │   │   └── OpenTelemetry.Api.ProviderBuilderExtensions.xml
│       │   │   │   └── netstandard2.0
│       │   │   │       ├── OpenTelemetry.Api.ProviderBuilderExtensions.dll
│       │   │   │       ├── OpenTelemetry.Api.ProviderBuilderExtensions.dll.sigstore.json
│       │   │   │       └── OpenTelemetry.Api.ProviderBuilderExtensions.xml
│       │   │   ├── .nupkg.metadata
│       │   │   ├── .signature.p7s
│       │   │   ├── LICENSE.TXT
│       │   │   ├── opentelemetry-icon-color.png
│       │   │   ├── opentelemetry.api.providerbuilderextensions.1.15.0.nupkg
│       │   │   ├── opentelemetry.api.providerbuilderextensions.1.15.0.nupkg.sha512
│       │   │   ├── opentelemetry.api.providerbuilderextensions.nuspec
│       │   │   ├── README.md
│       │   │   └── THIRD-PARTY-NOTICES.TXT
│       │   └── 1.15.3
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── OpenTelemetry.Api.ProviderBuilderExtensions.dll
│       │       │   │   ├── OpenTelemetry.Api.ProviderBuilderExtensions.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Api.ProviderBuilderExtensions.xml
│       │       │   ├── net462
│       │       │   │   ├── OpenTelemetry.Api.ProviderBuilderExtensions.dll
│       │       │   │   ├── OpenTelemetry.Api.ProviderBuilderExtensions.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Api.ProviderBuilderExtensions.xml
│       │       │   ├── net8.0
│       │       │   │   ├── OpenTelemetry.Api.ProviderBuilderExtensions.dll
│       │       │   │   ├── OpenTelemetry.Api.ProviderBuilderExtensions.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Api.ProviderBuilderExtensions.xml
│       │       │   ├── net9.0
│       │       │   │   ├── OpenTelemetry.Api.ProviderBuilderExtensions.dll
│       │       │   │   ├── OpenTelemetry.Api.ProviderBuilderExtensions.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Api.ProviderBuilderExtensions.xml
│       │       │   └── netstandard2.0
│       │       │       ├── OpenTelemetry.Api.ProviderBuilderExtensions.dll
│       │       │       ├── OpenTelemetry.Api.ProviderBuilderExtensions.dll.sigstore.json
│       │       │       └── OpenTelemetry.Api.ProviderBuilderExtensions.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── LICENSE.TXT
│       │       ├── opentelemetry-icon-color.png
│       │       ├── opentelemetry.api.providerbuilderextensions.1.15.3.nupkg
│       │       ├── opentelemetry.api.providerbuilderextensions.1.15.3.nupkg.sha512
│       │       ├── opentelemetry.api.providerbuilderextensions.nuspec
│       │       ├── README.md
│       │       └── THIRD-PARTY-NOTICES.TXT
│       ├── opentelemetry.exporter.console
│       │   └── 1.15.1
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── OpenTelemetry.Exporter.Console.dll
│       │       │   │   ├── OpenTelemetry.Exporter.Console.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Exporter.Console.xml
│       │       │   ├── net462
│       │       │   │   ├── OpenTelemetry.Exporter.Console.dll
│       │       │   │   ├── OpenTelemetry.Exporter.Console.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Exporter.Console.xml
│       │       │   ├── net8.0
│       │       │   │   ├── OpenTelemetry.Exporter.Console.dll
│       │       │   │   ├── OpenTelemetry.Exporter.Console.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Exporter.Console.xml
│       │       │   ├── net9.0
│       │       │   │   ├── OpenTelemetry.Exporter.Console.dll
│       │       │   │   ├── OpenTelemetry.Exporter.Console.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Exporter.Console.xml
│       │       │   └── netstandard2.0
│       │       │       ├── OpenTelemetry.Exporter.Console.dll
│       │       │       ├── OpenTelemetry.Exporter.Console.dll.sigstore.json
│       │       │       └── OpenTelemetry.Exporter.Console.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── LICENSE.TXT
│       │       ├── opentelemetry-icon-color.png
│       │       ├── opentelemetry.exporter.console.1.15.1.nupkg
│       │       ├── opentelemetry.exporter.console.1.15.1.nupkg.sha512
│       │       ├── opentelemetry.exporter.console.nuspec
│       │       ├── README.md
│       │       └── THIRD-PARTY-NOTICES.TXT
│       ├── opentelemetry.exporter.opentelemetryprotocol
│       │   └── 1.15.3
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── OpenTelemetry.Exporter.OpenTelemetryProtocol.dll
│       │       │   │   ├── OpenTelemetry.Exporter.OpenTelemetryProtocol.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Exporter.OpenTelemetryProtocol.xml
│       │       │   ├── net462
│       │       │   │   ├── OpenTelemetry.Exporter.OpenTelemetryProtocol.dll
│       │       │   │   ├── OpenTelemetry.Exporter.OpenTelemetryProtocol.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Exporter.OpenTelemetryProtocol.xml
│       │       │   ├── net8.0
│       │       │   │   ├── OpenTelemetry.Exporter.OpenTelemetryProtocol.dll
│       │       │   │   ├── OpenTelemetry.Exporter.OpenTelemetryProtocol.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Exporter.OpenTelemetryProtocol.xml
│       │       │   ├── net9.0
│       │       │   │   ├── OpenTelemetry.Exporter.OpenTelemetryProtocol.dll
│       │       │   │   ├── OpenTelemetry.Exporter.OpenTelemetryProtocol.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Exporter.OpenTelemetryProtocol.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── OpenTelemetry.Exporter.OpenTelemetryProtocol.dll
│       │       │   │   ├── OpenTelemetry.Exporter.OpenTelemetryProtocol.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Exporter.OpenTelemetryProtocol.xml
│       │       │   └── netstandard2.1
│       │       │       ├── OpenTelemetry.Exporter.OpenTelemetryProtocol.dll
│       │       │       ├── OpenTelemetry.Exporter.OpenTelemetryProtocol.dll.sigstore.json
│       │       │       └── OpenTelemetry.Exporter.OpenTelemetryProtocol.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── LICENSE.TXT
│       │       ├── opentelemetry-icon-color.png
│       │       ├── opentelemetry.exporter.opentelemetryprotocol.1.15.3.nupkg
│       │       ├── opentelemetry.exporter.opentelemetryprotocol.1.15.3.nupkg.sha512
│       │       ├── opentelemetry.exporter.opentelemetryprotocol.nuspec
│       │       ├── README.md
│       │       └── THIRD-PARTY-NOTICES.TXT
│       ├── opentelemetry.exporter.prometheus.aspnetcore
│       │   └── 1.14.0-beta.1
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── OpenTelemetry.Exporter.Prometheus.AspNetCore.dll
│       │       │   │   ├── OpenTelemetry.Exporter.Prometheus.AspNetCore.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Exporter.Prometheus.AspNetCore.xml
│       │       │   ├── net8.0
│       │       │   │   ├── OpenTelemetry.Exporter.Prometheus.AspNetCore.dll
│       │       │   │   ├── OpenTelemetry.Exporter.Prometheus.AspNetCore.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Exporter.Prometheus.AspNetCore.xml
│       │       │   └── net9.0
│       │       │       ├── OpenTelemetry.Exporter.Prometheus.AspNetCore.dll
│       │       │       ├── OpenTelemetry.Exporter.Prometheus.AspNetCore.dll.sigstore.json
│       │       │       └── OpenTelemetry.Exporter.Prometheus.AspNetCore.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── LICENSE.TXT
│       │       ├── opentelemetry-icon-color.png
│       │       ├── opentelemetry.exporter.prometheus.aspnetcore.1.14.0-beta.1.nupkg
│       │       ├── opentelemetry.exporter.prometheus.aspnetcore.1.14.0-beta.1.nupkg.sha512
│       │       ├── opentelemetry.exporter.prometheus.aspnetcore.nuspec
│       │       ├── README.md
│       │       └── THIRD-PARTY-NOTICES.TXT
│       ├── opentelemetry.extensions.hosting
│       │   └── 1.15.3
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── OpenTelemetry.Extensions.Hosting.dll
│       │       │   │   ├── OpenTelemetry.Extensions.Hosting.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Extensions.Hosting.xml
│       │       │   ├── net462
│       │       │   │   ├── OpenTelemetry.Extensions.Hosting.dll
│       │       │   │   ├── OpenTelemetry.Extensions.Hosting.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Extensions.Hosting.xml
│       │       │   ├── net8.0
│       │       │   │   ├── OpenTelemetry.Extensions.Hosting.dll
│       │       │   │   ├── OpenTelemetry.Extensions.Hosting.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Extensions.Hosting.xml
│       │       │   ├── net9.0
│       │       │   │   ├── OpenTelemetry.Extensions.Hosting.dll
│       │       │   │   ├── OpenTelemetry.Extensions.Hosting.dll.sigstore.json
│       │       │   │   └── OpenTelemetry.Extensions.Hosting.xml
│       │       │   └── netstandard2.0
│       │       │       ├── OpenTelemetry.Extensions.Hosting.dll
│       │       │       ├── OpenTelemetry.Extensions.Hosting.dll.sigstore.json
│       │       │       └── OpenTelemetry.Extensions.Hosting.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── LICENSE.TXT
│       │       ├── opentelemetry-icon-color.png
│       │       ├── opentelemetry.extensions.hosting.1.15.3.nupkg
│       │       ├── opentelemetry.extensions.hosting.1.15.3.nupkg.sha512
│       │       ├── opentelemetry.extensions.hosting.nuspec
│       │       ├── README.md
│       │       └── THIRD-PARTY-NOTICES.TXT
│       ├── opentelemetry.instrumentation.aspnetcore
│       │   └── 1.15.1
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── OpenTelemetry.Instrumentation.AspNetCore.dll
│       │       │   │   └── OpenTelemetry.Instrumentation.AspNetCore.xml
│       │       │   ├── net8.0
│       │       │   │   ├── OpenTelemetry.Instrumentation.AspNetCore.dll
│       │       │   │   └── OpenTelemetry.Instrumentation.AspNetCore.xml
│       │       │   └── netstandard2.0
│       │       │       ├── OpenTelemetry.Instrumentation.AspNetCore.dll
│       │       │       └── OpenTelemetry.Instrumentation.AspNetCore.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── LICENSE.TXT
│       │       ├── opentelemetry-icon-color.png
│       │       ├── opentelemetry.instrumentation.aspnetcore.1.15.1.nupkg
│       │       ├── opentelemetry.instrumentation.aspnetcore.1.15.1.nupkg.sha512
│       │       ├── opentelemetry.instrumentation.aspnetcore.nuspec
│       │       └── README.md
│       ├── opentelemetry.instrumentation.http
│       │   └── 1.15.0
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── OpenTelemetry.Instrumentation.Http.dll
│       │       │   │   └── OpenTelemetry.Instrumentation.Http.xml
│       │       │   ├── net462
│       │       │   │   ├── OpenTelemetry.Instrumentation.Http.dll
│       │       │   │   └── OpenTelemetry.Instrumentation.Http.xml
│       │       │   ├── net8.0
│       │       │   │   ├── OpenTelemetry.Instrumentation.Http.dll
│       │       │   │   └── OpenTelemetry.Instrumentation.Http.xml
│       │       │   └── netstandard2.0
│       │       │       ├── OpenTelemetry.Instrumentation.Http.dll
│       │       │       └── OpenTelemetry.Instrumentation.Http.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── LICENSE.TXT
│       │       ├── opentelemetry-icon-color.png
│       │       ├── opentelemetry.instrumentation.http.1.15.0.nupkg
│       │       ├── opentelemetry.instrumentation.http.1.15.0.nupkg.sha512
│       │       ├── opentelemetry.instrumentation.http.nuspec
│       │       └── README.md
│       ├── parquet.net
│       │   └── 5.5.0
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Parquet.dll
│       │       │   │   ├── Parquet.pdb
│       │       │   │   └── Parquet.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Parquet.dll
│       │       │   │   ├── Parquet.pdb
│       │       │   │   └── Parquet.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── Parquet.dll
│       │       │   │   ├── Parquet.pdb
│       │       │   │   └── Parquet.xml
│       │       │   └── netstandard2.1
│       │       │       ├── Parquet.dll
│       │       │       ├── Parquet.pdb
│       │       │       └── Parquet.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── icon.png
│       │       ├── parquet.net.5.5.0.nupkg
│       │       ├── parquet.net.5.5.0.nupkg.sha512
│       │       ├── parquet.net.nuspec
│       │       └── README.md
│       ├── polly
│       │   └── 8.6.6
│       │       ├── lib
│       │       │   ├── net462
│       │       │   │   ├── Polly.dll
│       │       │   │   ├── Polly.pdb
│       │       │   │   └── Polly.xml
│       │       │   ├── net472
│       │       │   │   ├── Polly.dll
│       │       │   │   ├── Polly.pdb
│       │       │   │   └── Polly.xml
│       │       │   ├── net6.0
│       │       │   │   ├── Polly.dll
│       │       │   │   ├── Polly.pdb
│       │       │   │   └── Polly.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Polly.dll
│       │       │       ├── Polly.pdb
│       │       │       └── Polly.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── LICENSE
│       │       ├── package-icon.png
│       │       ├── package-readme.md
│       │       ├── polly.8.6.6.nupkg
│       │       ├── polly.8.6.6.nupkg.sha512
│       │       └── polly.nuspec
│       ├── polly.core
│       │   └── 8.6.6
│       │       ├── lib
│       │       │   ├── net462
│       │       │   │   ├── Polly.Core.dll
│       │       │   │   ├── Polly.Core.pdb
│       │       │   │   └── Polly.Core.xml
│       │       │   ├── net472
│       │       │   │   ├── Polly.Core.dll
│       │       │   │   ├── Polly.Core.pdb
│       │       │   │   └── Polly.Core.xml
│       │       │   ├── net6.0
│       │       │   │   ├── Polly.Core.dll
│       │       │   │   ├── Polly.Core.pdb
│       │       │   │   └── Polly.Core.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Polly.Core.dll
│       │       │   │   ├── Polly.Core.pdb
│       │       │   │   └── Polly.Core.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Polly.Core.dll
│       │       │       ├── Polly.Core.pdb
│       │       │       └── Polly.Core.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── LICENSE
│       │       ├── package-icon.png
│       │       ├── package-readme.md
│       │       ├── polly.core.8.6.6.nupkg
│       │       ├── polly.core.8.6.6.nupkg.sha512
│       │       └── polly.core.nuspec
│       ├── polly.extensions
│       │   └── 8.6.6
│       │       ├── lib
│       │       │   ├── net462
│       │       │   │   ├── Polly.Extensions.dll
│       │       │   │   ├── Polly.Extensions.pdb
│       │       │   │   └── Polly.Extensions.xml
│       │       │   ├── net472
│       │       │   │   ├── Polly.Extensions.dll
│       │       │   │   ├── Polly.Extensions.pdb
│       │       │   │   └── Polly.Extensions.xml
│       │       │   ├── net6.0
│       │       │   │   ├── Polly.Extensions.dll
│       │       │   │   ├── Polly.Extensions.pdb
│       │       │   │   └── Polly.Extensions.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Polly.Extensions.dll
│       │       │   │   ├── Polly.Extensions.pdb
│       │       │   │   └── Polly.Extensions.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Polly.Extensions.dll
│       │       │       ├── Polly.Extensions.pdb
│       │       │       └── Polly.Extensions.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── LICENSE
│       │       ├── package-icon.png
│       │       ├── package-readme.md
│       │       ├── polly.extensions.8.6.6.nupkg
│       │       ├── polly.extensions.8.6.6.nupkg.sha512
│       │       └── polly.extensions.nuspec
│       ├── polly.extensions.http
│       │   └── 3.0.0
│       │       ├── lib
│       │       │   ├── netstandard1.1
│       │       │   │   ├── Polly.Extensions.Http.dll
│       │       │   │   └── Polly.Extensions.Http.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Polly.Extensions.Http.dll
│       │       │       └── Polly.Extensions.Http.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── polly.extensions.http.3.0.0.nupkg
│       │       ├── polly.extensions.http.3.0.0.nupkg.sha512
│       │       └── polly.extensions.http.nuspec
│       ├── prometheus-net
│       │   └── 8.2.1
│       │       ├── lib
│       │       │   ├── net462
│       │       │   │   ├── Prometheus.NetStandard.dll
│       │       │   │   └── Prometheus.NetStandard.xml
│       │       │   ├── net6.0
│       │       │   │   ├── Prometheus.NetStandard.dll
│       │       │   │   └── Prometheus.NetStandard.xml
│       │       │   ├── net7.0
│       │       │   │   ├── Prometheus.NetStandard.dll
│       │       │   │   └── Prometheus.NetStandard.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Prometheus.NetStandard.dll
│       │       │       └── Prometheus.NetStandard.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── prometheus-net-logo.png
│       │       ├── prometheus-net.8.2.1.nupkg
│       │       ├── prometheus-net.8.2.1.nupkg.sha512
│       │       ├── prometheus-net.nuspec
│       │       └── README.md
│       ├── prometheus-net.aspnetcore
│       │   └── 8.2.1
│       │       ├── lib
│       │       │   └── net6.0
│       │       │       ├── Prometheus.AspNetCore.dll
│       │       │       └── Prometheus.AspNetCore.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── prometheus-net-logo.png
│       │       ├── prometheus-net.aspnetcore.8.2.1.nupkg
│       │       ├── prometheus-net.aspnetcore.8.2.1.nupkg.sha512
│       │       ├── prometheus-net.aspnetcore.nuspec
│       │       └── README.md
│       ├── rabbitmq.client
│       │   └── 7.2.1
│       │       ├── lib
│       │       │   ├── net8.0
│       │       │   │   ├── RabbitMQ.Client.dll
│       │       │   │   └── RabbitMQ.Client.xml
│       │       │   └── netstandard2.0
│       │       │       ├── RabbitMQ.Client.dll
│       │       │       └── RabbitMQ.Client.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── icon.png
│       │       ├── rabbitmq.client.7.2.1.nupkg
│       │       ├── rabbitmq.client.7.2.1.nupkg.sha512
│       │       ├── rabbitmq.client.nuspec
│       │       └── README.md
│       ├── serilog
│       │   └── 4.3.1
│       │       ├── build
│       │       │   └── Serilog.targets
│       │       ├── buildTransitive
│       │       │   └── Serilog.targets
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Serilog.dll
│       │       │   │   └── Serilog.xml
│       │       │   ├── net462
│       │       │   │   ├── Serilog.dll
│       │       │   │   └── Serilog.xml
│       │       │   ├── net471
│       │       │   │   ├── Serilog.dll
│       │       │   │   └── Serilog.xml
│       │       │   ├── net6.0
│       │       │   │   ├── Serilog.dll
│       │       │   │   └── Serilog.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Serilog.dll
│       │       │   │   └── Serilog.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Serilog.dll
│       │       │   │   └── Serilog.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Serilog.dll
│       │       │       └── Serilog.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── icon.png
│       │       ├── README.md
│       │       ├── serilog.4.3.1.nupkg
│       │       ├── serilog.4.3.1.nupkg.sha512
│       │       └── serilog.nuspec
│       ├── serilog.extensions.logging
│       │   └── 10.0.0
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Serilog.Extensions.Logging.dll
│       │       │   │   └── Serilog.Extensions.Logging.xml
│       │       │   ├── net462
│       │       │   │   ├── Serilog.Extensions.Logging.dll
│       │       │   │   └── Serilog.Extensions.Logging.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Serilog.Extensions.Logging.dll
│       │       │   │   └── Serilog.Extensions.Logging.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Serilog.Extensions.Logging.dll
│       │       │   │   └── Serilog.Extensions.Logging.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── Serilog.Extensions.Logging.dll
│       │       │   │   └── Serilog.Extensions.Logging.xml
│       │       │   └── netstandard2.1
│       │       │       ├── Serilog.Extensions.Logging.dll
│       │       │       └── Serilog.Extensions.Logging.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── README.md
│       │       ├── serilog-extension-nuget.png
│       │       ├── serilog.extensions.logging.10.0.0.nupkg
│       │       ├── serilog.extensions.logging.10.0.0.nupkg.sha512
│       │       └── serilog.extensions.logging.nuspec
│       ├── serilog.settings.configuration
│       │   └── 10.0.0
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Serilog.Settings.Configuration.dll
│       │       │   │   └── Serilog.Settings.Configuration.xml
│       │       │   ├── net462
│       │       │   │   ├── Serilog.Settings.Configuration.dll
│       │       │   │   └── Serilog.Settings.Configuration.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Serilog.Settings.Configuration.dll
│       │       │   │   └── Serilog.Settings.Configuration.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Serilog.Settings.Configuration.dll
│       │       │   │   └── Serilog.Settings.Configuration.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Serilog.Settings.Configuration.dll
│       │       │       └── Serilog.Settings.Configuration.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── icon.png
│       │       ├── README.md
│       │       ├── serilog.settings.configuration.10.0.0.nupkg
│       │       ├── serilog.settings.configuration.10.0.0.nupkg.sha512
│       │       └── serilog.settings.configuration.nuspec
│       ├── serilog.sinks.console
│       │   └── 6.1.1
│       │       ├── lib
│       │       │   ├── net462
│       │       │   │   ├── Serilog.Sinks.Console.dll
│       │       │   │   └── Serilog.Sinks.Console.xml
│       │       │   ├── net471
│       │       │   │   ├── Serilog.Sinks.Console.dll
│       │       │   │   └── Serilog.Sinks.Console.xml
│       │       │   ├── net6.0
│       │       │   │   ├── Serilog.Sinks.Console.dll
│       │       │   │   └── Serilog.Sinks.Console.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Serilog.Sinks.Console.dll
│       │       │   │   └── Serilog.Sinks.Console.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Serilog.Sinks.Console.dll
│       │       │       └── Serilog.Sinks.Console.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── icon.png
│       │       ├── README.md
│       │       ├── serilog.sinks.console.6.1.1.nupkg
│       │       ├── serilog.sinks.console.6.1.1.nupkg.sha512
│       │       └── serilog.sinks.console.nuspec
│       ├── serilog.sinks.file
│       │   └── 7.0.0
│       │       ├── lib
│       │       │   ├── net462
│       │       │   │   ├── Serilog.Sinks.File.dll
│       │       │   │   └── Serilog.Sinks.File.xml
│       │       │   ├── net471
│       │       │   │   ├── Serilog.Sinks.File.dll
│       │       │   │   └── Serilog.Sinks.File.xml
│       │       │   ├── net6.0
│       │       │   │   ├── Serilog.Sinks.File.dll
│       │       │   │   └── Serilog.Sinks.File.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Serilog.Sinks.File.dll
│       │       │   │   └── Serilog.Sinks.File.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Serilog.Sinks.File.dll
│       │       │   │   └── Serilog.Sinks.File.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Serilog.Sinks.File.dll
│       │       │       └── Serilog.Sinks.File.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── README.md
│       │       ├── serilog-sink-nuget.png
│       │       ├── serilog.sinks.file.7.0.0.nupkg
│       │       ├── serilog.sinks.file.7.0.0.nupkg.sha512
│       │       └── serilog.sinks.file.nuspec
│       ├── sharpino
│       │   └── 4.9.4
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Sharpino.Lib.dll
│       │       │   │   └── Sharpino.Lib.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Sharpino.Lib.dll
│       │       │   │   └── Sharpino.Lib.xml
│       │       │   └── net9.0
│       │       │       ├── Sharpino.Lib.dll
│       │       │       └── Sharpino.Lib.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── README.md
│       │       ├── sharpino.4.9.4.nupkg
│       │       ├── sharpino.4.9.4.nupkg.sha512
│       │       └── sharpino.nuspec
│       ├── sharpino.core
│       │   └── 4.8.8
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Sharpino.Lib.Core.dll
│       │       │   │   └── Sharpino.Lib.Core.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Sharpino.Lib.Core.dll
│       │       │   │   └── Sharpino.Lib.Core.xml
│       │       │   └── net9.0
│       │       │       ├── Sharpino.Lib.Core.dll
│       │       │       └── Sharpino.Lib.Core.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── README.md
│       │       ├── sharpino.core.4.8.8.nupkg
│       │       ├── sharpino.core.4.8.8.nupkg.sha512
│       │       └── sharpino.core.nuspec
│       ├── skender.stock.indicators
│       │   └── 2.7.1
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Skender.Stock.Indicators.dll
│       │       │   │   └── Skender.Stock.Indicators.xml
│       │       │   ├── net6.0
│       │       │   │   ├── Skender.Stock.Indicators.dll
│       │       │   │   └── Skender.Stock.Indicators.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Skender.Stock.Indicators.dll
│       │       │   │   └── Skender.Stock.Indicators.xml
│       │       │   ├── net9.0
│       │       │   │   ├── Skender.Stock.Indicators.dll
│       │       │   │   └── Skender.Stock.Indicators.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── Skender.Stock.Indicators.dll
│       │       │   │   └── Skender.Stock.Indicators.xml
│       │       │   └── netstandard2.1
│       │       │       ├── Skender.Stock.Indicators.dll
│       │       │       └── Skender.Stock.Indicators.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── icon.png
│       │       ├── LICENSE
│       │       ├── NOTICE
│       │       ├── README.md
│       │       ├── skender.stock.indicators.2.7.1.nupkg
│       │       ├── skender.stock.indicators.2.7.1.nupkg.sha512
│       │       └── skender.stock.indicators.nuspec
│       ├── snappier
│       │   └── 1.3.1
│       │       ├── lib
│       │       │   ├── net472
│       │       │   │   ├── Snappier.dll
│       │       │   │   └── Snappier.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Snappier.dll
│       │       │   │   └── Snappier.xml
│       │       │   └── netstandard2.0
│       │       │       ├── Snappier.dll
│       │       │       └── Snappier.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── icon.png
│       │       ├── README.md
│       │       ├── snappier.1.3.1.nupkg
│       │       ├── snappier.1.3.1.nupkg.sha512
│       │       └── snappier.nuspec
│       ├── swashbuckle.aspnetcore
│       │   └── 10.1.7
│       │       ├── build
│       │       │   └── Swashbuckle.AspNetCore.props
│       │       ├── buildMultiTargeting
│       │       │   └── Swashbuckle.AspNetCore.props
│       │       ├── docs
│       │       │   └── package-readme.md
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── swashbuckle.aspnetcore.10.1.7.nupkg
│       │       ├── swashbuckle.aspnetcore.10.1.7.nupkg.sha512
│       │       └── swashbuckle.aspnetcore.nuspec
│       ├── swashbuckle.aspnetcore.swagger
│       │   └── 10.1.7
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Swashbuckle.AspNetCore.Swagger.dll
│       │       │   │   ├── Swashbuckle.AspNetCore.Swagger.pdb
│       │       │   │   └── Swashbuckle.AspNetCore.Swagger.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Swashbuckle.AspNetCore.Swagger.dll
│       │       │   │   ├── Swashbuckle.AspNetCore.Swagger.pdb
│       │       │   │   └── Swashbuckle.AspNetCore.Swagger.xml
│       │       │   └── net9.0
│       │       │       ├── Swashbuckle.AspNetCore.Swagger.dll
│       │       │       ├── Swashbuckle.AspNetCore.Swagger.pdb
│       │       │       └── Swashbuckle.AspNetCore.Swagger.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── package-readme.md
│       │       ├── swashbuckle.aspnetcore.swagger.10.1.7.nupkg
│       │       ├── swashbuckle.aspnetcore.swagger.10.1.7.nupkg.sha512
│       │       └── swashbuckle.aspnetcore.swagger.nuspec
│       ├── swashbuckle.aspnetcore.swaggergen
│       │   └── 10.1.7
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Swashbuckle.AspNetCore.SwaggerGen.dll
│       │       │   │   ├── Swashbuckle.AspNetCore.SwaggerGen.pdb
│       │       │   │   └── Swashbuckle.AspNetCore.SwaggerGen.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Swashbuckle.AspNetCore.SwaggerGen.dll
│       │       │   │   ├── Swashbuckle.AspNetCore.SwaggerGen.pdb
│       │       │   │   └── Swashbuckle.AspNetCore.SwaggerGen.xml
│       │       │   └── net9.0
│       │       │       ├── Swashbuckle.AspNetCore.SwaggerGen.dll
│       │       │       ├── Swashbuckle.AspNetCore.SwaggerGen.pdb
│       │       │       └── Swashbuckle.AspNetCore.SwaggerGen.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── package-readme.md
│       │       ├── swashbuckle.aspnetcore.swaggergen.10.1.7.nupkg
│       │       ├── swashbuckle.aspnetcore.swaggergen.10.1.7.nupkg.sha512
│       │       └── swashbuckle.aspnetcore.swaggergen.nuspec
│       ├── swashbuckle.aspnetcore.swaggerui
│       │   └── 10.1.7
│       │       ├── lib
│       │       │   ├── net10.0
│       │       │   │   ├── Swashbuckle.AspNetCore.SwaggerUI.dll
│       │       │   │   ├── Swashbuckle.AspNetCore.SwaggerUI.pdb
│       │       │   │   └── Swashbuckle.AspNetCore.SwaggerUI.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Swashbuckle.AspNetCore.SwaggerUI.dll
│       │       │   │   ├── Swashbuckle.AspNetCore.SwaggerUI.pdb
│       │       │   │   └── Swashbuckle.AspNetCore.SwaggerUI.xml
│       │       │   └── net9.0
│       │       │       ├── Swashbuckle.AspNetCore.SwaggerUI.dll
│       │       │       ├── Swashbuckle.AspNetCore.SwaggerUI.pdb
│       │       │       └── Swashbuckle.AspNetCore.SwaggerUI.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── package-readme.md
│       │       ├── swashbuckle.aspnetcore.swaggerui.10.1.7.nupkg
│       │       ├── swashbuckle.aspnetcore.swaggerui.10.1.7.nupkg.sha512
│       │       └── swashbuckle.aspnetcore.swaggerui.nuspec
│       ├── system.clientmodel
│       │   └── 1.4.2
│       │       ├── analyzers
│       │       │   └── dotnet
│       │       │       └── cs
│       │       │           └── System.ClientModel.SourceGeneration.dll
│       │       ├── lib
│       │       │   ├── net6.0
│       │       │   │   ├── System.ClientModel.dll
│       │       │   │   └── System.ClientModel.xml
│       │       │   ├── net8.0
│       │       │   │   ├── System.ClientModel.dll
│       │       │   │   └── System.ClientModel.xml
│       │       │   └── netstandard2.0
│       │       │       ├── System.ClientModel.dll
│       │       │       └── System.ClientModel.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── CHANGELOG.md
│       │       ├── DotNetPackageIcon.png
│       │       ├── README.md
│       │       ├── system.clientmodel.1.4.2.nupkg
│       │       ├── system.clientmodel.1.4.2.nupkg.sha512
│       │       └── system.clientmodel.nuspec
│       ├── system.configuration.configurationmanager
│       │   └── 8.0.0
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── System.Configuration.ConfigurationManager.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net6.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── System.Configuration.ConfigurationManager.targets
│       │       ├── lib
│       │       │   ├── net462
│       │       │   │   ├── System.Configuration.ConfigurationManager.dll
│       │       │   │   └── System.Configuration.ConfigurationManager.xml
│       │       │   ├── net6.0
│       │       │   │   ├── System.Configuration.ConfigurationManager.dll
│       │       │   │   └── System.Configuration.ConfigurationManager.xml
│       │       │   ├── net7.0
│       │       │   │   ├── System.Configuration.ConfigurationManager.dll
│       │       │   │   └── System.Configuration.ConfigurationManager.xml
│       │       │   ├── net8.0
│       │       │   │   ├── System.Configuration.ConfigurationManager.dll
│       │       │   │   └── System.Configuration.ConfigurationManager.xml
│       │       │   └── netstandard2.0
│       │       │       ├── System.Configuration.ConfigurationManager.dll
│       │       │       └── System.Configuration.ConfigurationManager.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── LICENSE.TXT
│       │       ├── PACKAGE.md
│       │       ├── system.configuration.configurationmanager.8.0.0.nupkg
│       │       ├── system.configuration.configurationmanager.8.0.0.nupkg.sha512
│       │       ├── system.configuration.configurationmanager.nuspec
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── system.diagnostics.eventlog
│       │   ├── 10.0.7
│       │   │   ├── buildTransitive
│       │   │   │   ├── net461
│       │   │   │   │   └── System.Diagnostics.EventLog.targets
│       │   │   │   ├── net462
│       │   │   │   │   └── _._
│       │   │   │   ├── net8.0
│       │   │   │   │   └── _._
│       │   │   │   └── netcoreapp2.0
│       │   │   │       └── System.Diagnostics.EventLog.targets
│       │   │   ├── lib
│       │   │   │   ├── net10.0
│       │   │   │   │   ├── System.Diagnostics.EventLog.dll
│       │   │   │   │   └── System.Diagnostics.EventLog.xml
│       │   │   │   ├── net462
│       │   │   │   │   ├── System.Diagnostics.EventLog.dll
│       │   │   │   │   └── System.Diagnostics.EventLog.xml
│       │   │   │   ├── net8.0
│       │   │   │   │   ├── System.Diagnostics.EventLog.dll
│       │   │   │   │   └── System.Diagnostics.EventLog.xml
│       │   │   │   ├── net9.0
│       │   │   │   │   ├── System.Diagnostics.EventLog.dll
│       │   │   │   │   └── System.Diagnostics.EventLog.xml
│       │   │   │   └── netstandard2.0
│       │   │   │       ├── System.Diagnostics.EventLog.dll
│       │   │   │       └── System.Diagnostics.EventLog.xml
│       │   │   ├── runtimes
│       │   │   │   └── win
│       │   │   │       └── lib
│       │   │   │           ├── net10.0
│       │   │   │           │   ├── System.Diagnostics.EventLog.dll
│       │   │   │           │   ├── System.Diagnostics.EventLog.Messages.dll
│       │   │   │           │   └── System.Diagnostics.EventLog.xml
│       │   │   │           ├── net8.0
│       │   │   │           │   ├── System.Diagnostics.EventLog.dll
│       │   │   │           │   ├── System.Diagnostics.EventLog.Messages.dll
│       │   │   │           │   └── System.Diagnostics.EventLog.xml
│       │   │   │           └── net9.0
│       │   │   │               ├── System.Diagnostics.EventLog.dll
│       │   │   │               ├── System.Diagnostics.EventLog.Messages.dll
│       │   │   │               └── System.Diagnostics.EventLog.xml
│       │   │   ├── .nupkg.metadata
│       │   │   ├── .signature.p7s
│       │   │   ├── Icon.png
│       │   │   ├── PACKAGE.md
│       │   │   ├── system.diagnostics.eventlog.10.0.7.nupkg
│       │   │   ├── system.diagnostics.eventlog.10.0.7.nupkg.sha512
│       │   │   ├── system.diagnostics.eventlog.nuspec
│       │   │   ├── THIRD-PARTY-NOTICES.TXT
│       │   │   └── useSharedDesignerContext.txt
│       │   └── 8.0.0
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── System.Diagnostics.EventLog.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net6.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── System.Diagnostics.EventLog.targets
│       │       ├── lib
│       │       │   ├── net462
│       │       │   │   ├── System.Diagnostics.EventLog.dll
│       │       │   │   └── System.Diagnostics.EventLog.xml
│       │       │   ├── net6.0
│       │       │   │   ├── System.Diagnostics.EventLog.dll
│       │       │   │   └── System.Diagnostics.EventLog.xml
│       │       │   ├── net7.0
│       │       │   │   ├── System.Diagnostics.EventLog.dll
│       │       │   │   └── System.Diagnostics.EventLog.xml
│       │       │   ├── net8.0
│       │       │   │   ├── System.Diagnostics.EventLog.dll
│       │       │   │   └── System.Diagnostics.EventLog.xml
│       │       │   └── netstandard2.0
│       │       │       ├── System.Diagnostics.EventLog.dll
│       │       │       └── System.Diagnostics.EventLog.xml
│       │       ├── runtimes
│       │       │   └── win
│       │       │       └── lib
│       │       │           ├── net6.0
│       │       │           │   ├── System.Diagnostics.EventLog.dll
│       │       │           │   ├── System.Diagnostics.EventLog.Messages.dll
│       │       │           │   └── System.Diagnostics.EventLog.xml
│       │       │           ├── net7.0
│       │       │           │   ├── System.Diagnostics.EventLog.dll
│       │       │           │   ├── System.Diagnostics.EventLog.Messages.dll
│       │       │           │   └── System.Diagnostics.EventLog.xml
│       │       │           └── net8.0
│       │       │               ├── System.Diagnostics.EventLog.dll
│       │       │               ├── System.Diagnostics.EventLog.Messages.dll
│       │       │               └── System.Diagnostics.EventLog.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── LICENSE.TXT
│       │       ├── PACKAGE.md
│       │       ├── system.diagnostics.eventlog.8.0.0.nupkg
│       │       ├── system.diagnostics.eventlog.8.0.0.nupkg.sha512
│       │       ├── system.diagnostics.eventlog.nuspec
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── system.identitymodel.tokens.jwt
│       │   └── 6.35.0
│       │       ├── lib
│       │       │   ├── net45
│       │       │   │   ├── System.IdentityModel.Tokens.Jwt.dll
│       │       │   │   └── System.IdentityModel.Tokens.Jwt.xml
│       │       │   ├── net461
│       │       │   │   ├── System.IdentityModel.Tokens.Jwt.dll
│       │       │   │   └── System.IdentityModel.Tokens.Jwt.xml
│       │       │   ├── net462
│       │       │   │   ├── System.IdentityModel.Tokens.Jwt.dll
│       │       │   │   └── System.IdentityModel.Tokens.Jwt.xml
│       │       │   ├── net472
│       │       │   │   ├── System.IdentityModel.Tokens.Jwt.dll
│       │       │   │   └── System.IdentityModel.Tokens.Jwt.xml
│       │       │   ├── net6.0
│       │       │   │   ├── System.IdentityModel.Tokens.Jwt.dll
│       │       │   │   └── System.IdentityModel.Tokens.Jwt.xml
│       │       │   └── netstandard2.0
│       │       │       ├── System.IdentityModel.Tokens.Jwt.dll
│       │       │       └── System.IdentityModel.Tokens.Jwt.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── system.identitymodel.tokens.jwt.6.35.0.nupkg
│       │       ├── system.identitymodel.tokens.jwt.6.35.0.nupkg.sha512
│       │       └── system.identitymodel.tokens.jwt.nuspec
│       ├── system.memory.data
│       │   ├── 1.0.2
│       │   │   ├── lib
│       │   │   │   ├── net461
│       │   │   │   │   ├── System.Memory.Data.dll
│       │   │   │   │   └── System.Memory.Data.xml
│       │   │   │   └── netstandard2.0
│       │   │   │       ├── System.Memory.Data.dll
│       │   │   │       └── System.Memory.Data.xml
│       │   │   ├── .nupkg.metadata
│       │   │   ├── .signature.p7s
│       │   │   ├── CHANGELOG.md
│       │   │   ├── DotNetPackageIcon.png
│       │   │   ├── README.md
│       │   │   ├── system.memory.data.1.0.2.nupkg
│       │   │   ├── system.memory.data.1.0.2.nupkg.sha512
│       │   │   └── system.memory.data.nuspec
│       │   └── 6.0.1
│       │       ├── buildTransitive
│       │       │   ├── netcoreapp2.0
│       │       │   │   └── System.Memory.Data.targets
│       │       │   └── netcoreapp3.1
│       │       │       └── _._
│       │       ├── lib
│       │       │   ├── net461
│       │       │   │   ├── System.Memory.Data.dll
│       │       │   │   └── System.Memory.Data.xml
│       │       │   ├── net6.0
│       │       │   │   ├── System.Memory.Data.dll
│       │       │   │   └── System.Memory.Data.xml
│       │       │   └── netstandard2.0
│       │       │       ├── System.Memory.Data.dll
│       │       │       └── System.Memory.Data.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── LICENSE.TXT
│       │       ├── system.memory.data.6.0.1.nupkg
│       │       ├── system.memory.data.6.0.1.nupkg.sha512
│       │       ├── system.memory.data.nuspec
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── system.reactive
│       │   └── 6.1.0
│       │       ├── build
│       │       │   ├── net6.0
│       │       │   │   └── _._
│       │       │   └── net6.0-windows10.0.19041
│       │       │       └── _._
│       │       ├── buildTransitive
│       │       │   ├── net6.0
│       │       │   │   └── _._
│       │       │   └── net6.0-windows10.0.19041
│       │       │       └── _._
│       │       ├── lib
│       │       │   ├── net472
│       │       │   │   ├── System.Reactive.dll
│       │       │   │   └── System.Reactive.xml
│       │       │   ├── net6.0
│       │       │   │   ├── System.Reactive.dll
│       │       │   │   └── System.Reactive.xml
│       │       │   ├── net6.0-windows10.0.19041
│       │       │   │   ├── System.Reactive.dll
│       │       │   │   └── System.Reactive.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── System.Reactive.dll
│       │       │   │   └── System.Reactive.xml
│       │       │   └── uap10.0.18362
│       │       │       ├── System.Reactive.dll
│       │       │       └── System.Reactive.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── icon.png
│       │       ├── readme.md
│       │       ├── system.reactive.6.1.0.nupkg
│       │       ├── system.reactive.6.1.0.nupkg.sha512
│       │       └── system.reactive.nuspec
│       ├── system.runtime.caching
│       │   └── 8.0.0
│       │       ├── buildTransitive
│       │       │   ├── net6.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── System.Runtime.Caching.targets
│       │       ├── lib
│       │       │   ├── MonoAndroid10
│       │       │   │   └── _._
│       │       │   ├── MonoTouch10
│       │       │   │   └── _._
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net6.0
│       │       │   │   ├── System.Runtime.Caching.dll
│       │       │   │   └── System.Runtime.Caching.xml
│       │       │   ├── net7.0
│       │       │   │   ├── System.Runtime.Caching.dll
│       │       │   │   └── System.Runtime.Caching.xml
│       │       │   ├── net8.0
│       │       │   │   ├── System.Runtime.Caching.dll
│       │       │   │   └── System.Runtime.Caching.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── System.Runtime.Caching.dll
│       │       │   │   └── System.Runtime.Caching.xml
│       │       │   ├── xamarinios10
│       │       │   │   └── _._
│       │       │   ├── xamarinmac20
│       │       │   │   └── _._
│       │       │   ├── xamarintvos10
│       │       │   │   └── _._
│       │       │   └── xamarinwatchos10
│       │       │       └── _._
│       │       ├── runtimes
│       │       │   └── win
│       │       │       └── lib
│       │       │           ├── net6.0
│       │       │           │   ├── System.Runtime.Caching.dll
│       │       │           │   └── System.Runtime.Caching.xml
│       │       │           ├── net7.0
│       │       │           │   ├── System.Runtime.Caching.dll
│       │       │           │   └── System.Runtime.Caching.xml
│       │       │           └── net8.0
│       │       │               ├── System.Runtime.Caching.dll
│       │       │               └── System.Runtime.Caching.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── LICENSE.TXT
│       │       ├── PACKAGE.md
│       │       ├── system.runtime.caching.8.0.0.nupkg
│       │       ├── system.runtime.caching.8.0.0.nupkg.sha512
│       │       ├── system.runtime.caching.nuspec
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── system.security.cryptography.protecteddata
│       │   └── 9.0.2
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── System.Security.Cryptography.ProtectedData.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net8.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── System.Security.Cryptography.ProtectedData.targets
│       │       ├── lib
│       │       │   ├── MonoAndroid10
│       │       │   │   └── _._
│       │       │   ├── MonoTouch10
│       │       │   │   └── _._
│       │       │   ├── net462
│       │       │   │   ├── System.Security.Cryptography.ProtectedData.dll
│       │       │   │   └── System.Security.Cryptography.ProtectedData.xml
│       │       │   ├── net8.0
│       │       │   │   ├── System.Security.Cryptography.ProtectedData.dll
│       │       │   │   └── System.Security.Cryptography.ProtectedData.xml
│       │       │   ├── net9.0
│       │       │   │   ├── System.Security.Cryptography.ProtectedData.dll
│       │       │   │   └── System.Security.Cryptography.ProtectedData.xml
│       │       │   ├── netstandard2.0
│       │       │   │   ├── System.Security.Cryptography.ProtectedData.dll
│       │       │   │   └── System.Security.Cryptography.ProtectedData.xml
│       │       │   ├── xamarinios10
│       │       │   │   └── _._
│       │       │   ├── xamarinmac20
│       │       │   │   └── _._
│       │       │   ├── xamarintvos10
│       │       │   │   └── _._
│       │       │   └── xamarinwatchos10
│       │       │       └── _._
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── LICENSE.TXT
│       │       ├── PACKAGE.md
│       │       ├── system.security.cryptography.protecteddata.9.0.2.nupkg
│       │       ├── system.security.cryptography.protecteddata.9.0.2.nupkg.sha512
│       │       ├── system.security.cryptography.protecteddata.nuspec
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── system.threading.ratelimiting
│       │   └── 8.0.0
│       │       ├── buildTransitive
│       │       │   ├── net461
│       │       │   │   └── System.Threading.RateLimiting.targets
│       │       │   ├── net462
│       │       │   │   └── _._
│       │       │   ├── net6.0
│       │       │   │   └── _._
│       │       │   └── netcoreapp2.0
│       │       │       └── System.Threading.RateLimiting.targets
│       │       ├── lib
│       │       │   ├── net462
│       │       │   │   ├── System.Threading.RateLimiting.dll
│       │       │   │   └── System.Threading.RateLimiting.xml
│       │       │   ├── net6.0
│       │       │   │   ├── System.Threading.RateLimiting.dll
│       │       │   │   └── System.Threading.RateLimiting.xml
│       │       │   ├── net7.0
│       │       │   │   ├── System.Threading.RateLimiting.dll
│       │       │   │   └── System.Threading.RateLimiting.xml
│       │       │   ├── net8.0
│       │       │   │   ├── System.Threading.RateLimiting.dll
│       │       │   │   └── System.Threading.RateLimiting.xml
│       │       │   └── netstandard2.0
│       │       │       ├── System.Threading.RateLimiting.dll
│       │       │       └── System.Threading.RateLimiting.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── Icon.png
│       │       ├── LICENSE.TXT
│       │       ├── system.threading.ratelimiting.8.0.0.nupkg
│       │       ├── system.threading.ratelimiting.8.0.0.nupkg.sha512
│       │       ├── system.threading.ratelimiting.nuspec
│       │       ├── THIRD-PARTY-NOTICES.TXT
│       │       └── useSharedDesignerContext.txt
│       ├── websocket.client
│       │   └── 5.3.0
│       │       ├── lib
│       │       │   ├── net6.0
│       │       │   │   ├── Websocket.Client.dll
│       │       │   │   └── Websocket.Client.xml
│       │       │   ├── net7.0
│       │       │   │   ├── Websocket.Client.dll
│       │       │   │   └── Websocket.Client.xml
│       │       │   ├── net8.0
│       │       │   │   ├── Websocket.Client.dll
│       │       │   │   └── Websocket.Client.xml
│       │       │   └── netstandard2.1
│       │       │       ├── Websocket.Client.dll
│       │       │       └── Websocket.Client.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── icon-modern.png
│       │       ├── icon.png
│       │       ├── README.md
│       │       ├── websocket.client.5.3.0.nupkg
│       │       ├── websocket.client.5.3.0.nupkg.sha512
│       │       └── websocket.client.nuspec
│       ├── yolodev.expecto.testsdk
│       │   └── 0.15.5
│       │       ├── build
│       │       │   └── net8.0
│       │       │       ├── expecto.visualstudio.dotnetcore.testadapter.dll
│       │       │       ├── expecto.visualstudio.dotnetcore.testadapter.runtimeconfig.json
│       │       │       ├── YoloDev.Expecto.TestSdk.props
│       │       │       └── YoloDev.Expecto.TestSdk.targets
│       │       ├── lib
│       │       │   └── net8.0
│       │       │       └── _._
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── yolodev.expecto.testsdk.0.15.5.nupkg
│       │       ├── yolodev.expecto.testsdk.0.15.5.nupkg.sha512
│       │       └── yolodev.expecto.testsdk.nuspec
│       ├── ziggycreatures.fusioncache
│       │   └── 2.6.0
│       │       ├── lib
│       │       │   ├── net8.0
│       │       │   │   ├── ZiggyCreatures.FusionCache.dll
│       │       │   │   └── ZiggyCreatures.FusionCache.xml
│       │       │   ├── net9.0
│       │       │   │   ├── ZiggyCreatures.FusionCache.dll
│       │       │   │   └── ZiggyCreatures.FusionCache.xml
│       │       │   └── netstandard2.0
│       │       │       ├── ZiggyCreatures.FusionCache.dll
│       │       │       └── ZiggyCreatures.FusionCache.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── logo-128x128.png
│       │       ├── README.md
│       │       ├── ziggycreatures.fusioncache.2.6.0.nupkg
│       │       ├── ziggycreatures.fusioncache.2.6.0.nupkg.sha512
│       │       └── ziggycreatures.fusioncache.nuspec
│       ├── ziggycreatures.fusioncache.serialization.systemtextjson
│       │   └── 2.6.0
│       │       ├── lib
│       │       │   └── netstandard2.0
│       │       │       ├── ZiggyCreatures.FusionCache.Serialization.SystemTextJson.dll
│       │       │       └── ZiggyCreatures.FusionCache.Serialization.SystemTextJson.xml
│       │       ├── .nupkg.metadata
│       │       ├── .signature.p7s
│       │       ├── logo-128x128.png
│       │       ├── README.md
│       │       ├── ziggycreatures.fusioncache.serialization.systemtextjson.2.6.0.nupkg
│       │       ├── ziggycreatures.fusioncache.serialization.systemtextjson.2.6.0.nupkg.sha512
│       │       └── ziggycreatures.fusioncache.serialization.systemtextjson.nuspec
│       └── zstdsharp.port
│           └── 0.8.7
│               ├── lib
│               │   ├── net462
│               │   │   └── ZstdSharp.dll
│               │   ├── net5.0
│               │   │   └── ZstdSharp.dll
│               │   ├── net6.0
│               │   │   └── ZstdSharp.dll
│               │   ├── net7.0
│               │   │   └── ZstdSharp.dll
│               │   ├── net8.0
│               │   │   └── ZstdSharp.dll
│               │   ├── net9.0
│               │   │   └── ZstdSharp.dll
│               │   ├── netcoreapp3.1
│               │   │   └── ZstdSharp.dll
│               │   ├── netstandard2.0
│               │   │   └── ZstdSharp.dll
│               │   └── netstandard2.1
│               │       └── ZstdSharp.dll
│               ├── .nupkg.metadata
│               ├── .signature.p7s
│               ├── zstdsharp.port.0.8.7.nupkg
│               ├── zstdsharp.port.0.8.7.nupkg.sha512
│               └── zstdsharp.port.nuspec
├── artifacts
│   ├── browser-smoke
│   │   ├── settings-fund-operations-control-center.png
│   │   ├── vite-settings-control.err.log
│   │   └── vite-settings-control.out.log
│   ├── codex
│   │   └── meridian-tests-build.log
│   ├── codex-close-readiness-uishared-build
│   ├── codex-close-readiness-validation
│   ├── codex-validation
│   │   └── statement-intake
│   ├── desktop-workflows
│   │   ├── debug-startup
│   │   │   ├── 20260528-055452-debug-startup
│   │   │   │   ├── bundle
│   │   │   │   │   ├── fixture-state-backup
│   │   │   │   │   │   ├── fund-profiles.json.bak
│   │   │   │   │   │   └── workstation-operating-context.json.bak
│   │   │   │   │   ├── screenshots
│   │   │   │   │   │   ├── 01-research-workspace.png
│   │   │   │   │   │   ├── 02-provider-health.png
│   │   │   │   │   │   ├── 03-diagnostics.png
│   │   │   │   │   │   ├── 04-system-health.png
│   │   │   │   │   │   ├── 05-settings.png
│   │   │   │   │   │   └── 06-settings-failed-attempt.png
│   │   │   │   │   ├── bundle-summary.md
│   │   │   │   │   ├── environment.json
│   │   │   │   │   ├── last-successful-step.json
│   │   │   │   │   ├── stage-status.json
│   │   │   │   │   └── workflow-log.txt
│   │   │   │   ├── logs
│   │   │   │   │   ├── .preflight-write-0be195fa1db6436ea10700ea721a23e9.tmp
│   │   │   │   │   ├── .preflight-write-116b85b4e9df4152b84abca2c3992fba.tmp
│   │   │   │   │   ├── .preflight-write-1d06ad3da11d4f61ad990840a83705b6.tmp
│   │   │   │   │   ├── .preflight-write-320c707623ea4a3b91aa05cdc69e298f.tmp
│   │   │   │   │   ├── .preflight-write-3a66b9fae4c449a1bee077f13e491400.tmp
│   │   │   │   │   ├── .preflight-write-3bfa6211c70942a29e09f64c3d00a832.tmp
│   │   │   │   │   ├── .preflight-write-3e41a53b13ae46808e6e825b7b95432d.tmp
│   │   │   │   │   ├── .preflight-write-4911350d7ee2412e991cb1fbd0ffaa2a.tmp
│   │   │   │   │   ├── .preflight-write-4df1bd845ada481189a3c10a48e5b5f9.tmp
│   │   │   │   │   ├── .preflight-write-4e619d9e86c5476892b6c8ad73cd4730.tmp
│   │   │   │   │   ├── .preflight-write-595e30a303f04ab49bdd4e85a77dd834.tmp
│   │   │   │   │   ├── .preflight-write-5b1a6804ad7e4ff69379ee451d089995.tmp
│   │   │   │   │   ├── .preflight-write-6777829ebc554585966e020ef5978cbf.tmp
│   │   │   │   │   ├── .preflight-write-692f594746094fdb9958ea071a2f487d.tmp
│   │   │   │   │   ├── .preflight-write-74c3acb8c4e24d7da1f175076f1daa84.tmp
│   │   │   │   │   ├── .preflight-write-891dd819971f48d0b2a8b5c1ce724e90.tmp
│   │   │   │   │   ├── .preflight-write-8fea44a43cb54b06b23782548880c203.tmp
│   │   │   │   │   ├── .preflight-write-9a54d4fe6c214742b36cf9699dc0c619.tmp
│   │   │   │   │   ├── .preflight-write-9cc4c836ae964767992c1d581aa60d2c.tmp
│   │   │   │   │   ├── .preflight-write-9e736fc7d3f4407799a37c15496e8a8f.tmp
│   │   │   │   │   ├── .preflight-write-b0bfebe3eb08405685621351263b2848.tmp
│   │   │   │   │   ├── .preflight-write-b7e695b47ad04b5d81805e9a08f59471.tmp
│   │   │   │   │   ├── .preflight-write-c15b81bc9f3e40a7ac8ab61aa60628c6.tmp
│   │   │   │   │   ├── .preflight-write-f1c46e0123aa4ff697addb17b101e70a.tmp
│   │   │   │   │   ├── .preflight-write-f5cea1115dc04b4c84c1c90322f0198c.tmp
│   │   │   │   │   ├── .preflight-write-f77449ea93fc4356a2adf9511ec48493.tmp
│   │   │   │   │   ├── .preflight-write-fdfcce3422b5422983c0059428b3c18a.tmp
│   │   │   │   │   ├── stderr.log
│   │   │   │   │   └── stdout.log
│   │   │   │   ├── screenshots
│   │   │   │   │   ├── .preflight-write-0abb373940b54a7489f511e4bed4bece.tmp
│   │   │   │   │   ├── .preflight-write-0f04ab4f95e54d39b10ac2352ddf06f6.tmp
│   │   │   │   │   ├── .preflight-write-19a54f2e10744dfcaa9e27f015717175.tmp
│   │   │   │   │   ├── .preflight-write-20d2b8057f0f4559810bcc6a32abd9a6.tmp
│   │   │   │   │   ├── .preflight-write-3ea2fc736b2b4229b9ef43d29f385db3.tmp
│   │   │   │   │   ├── .preflight-write-4084404df17443089b9b890b7a1671c6.tmp
│   │   │   │   │   ├── .preflight-write-4575b4b7f08b4924bb31f19ad2e5bc96.tmp
│   │   │   │   │   ├── .preflight-write-4d1752f83f66487c841c6af8c05238d8.tmp
│   │   │   │   │   ├── .preflight-write-6571e1cf2f0744c5bd1d2834c877c979.tmp
│   │   │   │   │   ├── .preflight-write-661b211d2bd248398f84103a616f652f.tmp
│   │   │   │   │   ├── .preflight-write-6cf578b26e354471bc60189022caa251.tmp
│   │   │   │   │   ├── .preflight-write-78381534503340f691f2799076db0f21.tmp
│   │   │   │   │   ├── .preflight-write-79e0658267e04e4d809f274dc355a557.tmp
│   │   │   │   │   ├── .preflight-write-7b15c3cafdce45b6a1139f801705e127.tmp
│   │   │   │   │   ├── .preflight-write-7bfb05df294b4133865c8a30aae6eef3.tmp
│   │   │   │   │   ├── .preflight-write-82f72a5dc8414af188d78d13f26f4e45.tmp
│   │   │   │   │   ├── .preflight-write-84df9b86dc5a4baba6fec4d9b9fe5422.tmp
│   │   │   │   │   ├── .preflight-write-8d83c279358742f1bf9e13e57195fcfa.tmp
│   │   │   │   │   ├── .preflight-write-91ae02ecda9f4d179a083b266a4af582.tmp
│   │   │   │   │   ├── .preflight-write-c1f2df2f6ccf45aea32b3d29e16b9571.tmp
│   │   │   │   │   ├── .preflight-write-c456ff181cb5425596b47bc56b52dd28.tmp
│   │   │   │   │   ├── .preflight-write-c4929478ac424c4fa18ce631d0935899.tmp
│   │   │   │   │   ├── .preflight-write-c747ba9f041b4d9d942a45f16e724422.tmp
│   │   │   │   │   ├── .preflight-write-c95f7d2d4325467b9eaa637ca67f9efa.tmp
│   │   │   │   │   ├── .preflight-write-cc837ffd51b9499a86f131a8a4a67c61.tmp
│   │   │   │   │   ├── .preflight-write-d67b95f4714742f1989f353f9cbb4b22.tmp
│   │   │   │   │   ├── .preflight-write-f8570fc9771044e99735b916ebb44059.tmp
│   │   │   │   │   ├── 01-research-workspace.png
│   │   │   │   │   ├── 02-provider-health.png
│   │   │   │   │   ├── 03-diagnostics.png
│   │   │   │   │   ├── 04-system-health.png
│   │   │   │   │   └── 05-settings.png
│   │   │   │   ├── .preflight-write-09f44205bdf8467d859fdac8aa2ba285.tmp
│   │   │   │   ├── .preflight-write-1781c878b34447efb314be3aad891d55.tmp
│   │   │   │   ├── .preflight-write-1986c78d0a844a448d2833c76d290a6a.tmp
│   │   │   │   ├── .preflight-write-289dab920c44403a9c292ad4701f4e90.tmp
│   │   │   │   ├── .preflight-write-28fe660b63574287ab29e75c2d6fff2f.tmp
│   │   │   │   ├── .preflight-write-3b5527b0c1b5487fa531f475f3f37007.tmp
│   │   │   │   ├── .preflight-write-4329d65081514c82be4cd85d439c1496.tmp
│   │   │   │   ├── .preflight-write-472208738bd943b4bbfe95b233b12470.tmp
│   │   │   │   ├── .preflight-write-54a875a06af64faebda954a6b2b275d0.tmp
│   │   │   │   ├── .preflight-write-6d10f0c32e264b2f9569e20c6079fc82.tmp
│   │   │   │   ├── .preflight-write-7b86c54bc5f34f3e88497b36099d1d9c.tmp
│   │   │   │   ├── .preflight-write-80f469733ef94a71934c32c4b696d374.tmp
│   │   │   │   ├── .preflight-write-8b2195faf0b84401a3af87497b9f323d.tmp
│   │   │   │   ├── .preflight-write-91643f6321334f1d9da86e62b8736d91.tmp
│   │   │   │   ├── .preflight-write-93e77c78fc7b4a44a27223ed7ef0da79.tmp
│   │   │   │   ├── .preflight-write-a702517428764922b99b74793365bcdc.tmp
│   │   │   │   ├── .preflight-write-a7fbb1ab6d7f4ab39ba228da53615d06.tmp
│   │   │   │   ├── .preflight-write-a962291eee6d4e3a8823c3cc9d5fe717.tmp
│   │   │   │   ├── .preflight-write-abd9466d1e4c43f7b7baa96b40c15f6e.tmp
│   │   │   │   ├── .preflight-write-b8666550177e4640b133915ca5b1be28.tmp
│   │   │   │   ├── .preflight-write-c8ee7840fef34cc8bdcc85db279125bb.tmp
│   │   │   │   ├── .preflight-write-ce26e17e183a432095ef61b108caefe1.tmp
│   │   │   │   ├── .preflight-write-cee9104052f44be9a2c4e772e7da2783.tmp
│   │   │   │   ├── .preflight-write-d3c7be3a1c024ac5889f7bfa6c634478.tmp
│   │   │   │   ├── .preflight-write-e54c649f57f741dcbc10d7061379d8b7.tmp
│   │   │   │   ├── .preflight-write-f36b523ea59e416488f8487e569672e7.tmp
│   │   │   │   ├── .preflight-write-fbc570921a064d9289fe442e58b6458c.tmp
│   │   │   │   ├── build.stage.json
│   │   │   │   ├── capture.stage.json
│   │   │   │   ├── launch.stage.json
│   │   │   │   ├── manifest.json
│   │   │   │   ├── post-process.stage.json
│   │   │   │   ├── preflight.json
│   │   │   │   ├── preflight.stage.json
│   │   │   │   ├── publish.stage.json
│   │   │   │   └── run-summary.json
│   │   │   ├── checkpoints
│   │   │   │   └── debug-startup.checkpoint.json
│   │   │   ├── .preflight-write-004746a4336c410ba9da5029eaa846fe.tmp
│   │   │   ├── .preflight-write-0223247d32214addb357b5f272989f88.tmp
│   │   │   ├── .preflight-write-05273397f820486a8d113397d1e8da79.tmp
│   │   │   ├── .preflight-write-05dcbb2f3b014e07bb1df6ac54411fd3.tmp
│   │   │   ├── .preflight-write-083da9bd68854362ad179a7039c3039a.tmp
│   │   │   ├── .preflight-write-0bf8345a42cf42179bdb36847d5d605b.tmp
│   │   │   ├── .preflight-write-24c62fb95acf4a20a7f3de3cb2e39c6c.tmp
│   │   │   ├── .preflight-write-26b252c6532f4b6e93fc78c9559540d3.tmp
│   │   │   ├── .preflight-write-29434ea3d5c84611acdd72457fab3354.tmp
│   │   │   ├── .preflight-write-2d9dc625c46748ffa3b30f554c3c1d56.tmp
│   │   │   ├── .preflight-write-2e1c7cd90a4c4d29a7520e1e9bd1324c.tmp
│   │   │   ├── .preflight-write-2f2534208c43438cb1b7451fd3be233b.tmp
│   │   │   ├── .preflight-write-31e86b2282ca4b319d8c80607fca4f39.tmp
│   │   │   ├── .preflight-write-3337127204164369af0650ec648c20e2.tmp
│   │   │   ├── .preflight-write-3b985537f4ae42bc8812e4ee00bf424d.tmp
│   │   │   ├── .preflight-write-4308e35af0b64bda9eb4e2fa2e53fd0e.tmp
│   │   │   ├── .preflight-write-4ffd9316622d4cbf91043bc4166827a1.tmp
│   │   │   ├── .preflight-write-6c2b7fcef80148d88e1adfe86b4fddec.tmp
│   │   │   ├── .preflight-write-70e1f1541e464fb08b8851a5a80c6160.tmp
│   │   │   ├── .preflight-write-90775538fa714cf39aa5f9b3f673cd1f.tmp
│   │   │   ├── .preflight-write-a3dbc4a0bef94344a564b8866bca8c8b.tmp
│   │   │   ├── .preflight-write-bb254dc89f614e19b5ab3b33eb04b8f5.tmp
│   │   │   ├── .preflight-write-c1f12c023b754f6d9af2d3a796a27442.tmp
│   │   │   ├── .preflight-write-c65a2bfb9c324828811a1d7853a97978.tmp
│   │   │   ├── .preflight-write-e12479d8d726424181c577adb35389cd.tmp
│   │   │   ├── .preflight-write-fb5e155f753f4c529fdb37bc38b66e26.tmp
│   │   │   └── .preflight-write-fbe93209dd77436e808b9f37d654c844.tmp
│   │   ├── .preflight-write-0f7d91c991794effbe73d1045809f78f.tmp
│   │   ├── .preflight-write-2f658248b9a04993950c6cf5bf7cb7fa.tmp
│   │   ├── .preflight-write-4a62b7c471bc47e5bd5bb6d85a4adc92.tmp
│   │   ├── .preflight-write-4b992817898941b89dcdc29d077bbef7.tmp
│   │   ├── .preflight-write-5f77abc409e64168bc1435185f998697.tmp
│   │   ├── .preflight-write-714e60340f64439a8e7323dcc80d6731.tmp
│   │   ├── .preflight-write-730e31ae350f4ba98e212a1ebc515187.tmp
│   │   ├── .preflight-write-77de8884c5654653b04543ea724c9dca.tmp
│   │   ├── .preflight-write-7df64d5c84504bd387b67761e1ead0b4.tmp
│   │   ├── .preflight-write-a400cfc1aeba49488919654623f00f43.tmp
│   │   ├── .preflight-write-bb11202597d4449397fabbdbb375cf16.tmp
│   │   ├── .preflight-write-bd412b4025b342b083fd824cd8b7719c.tmp
│   │   ├── .preflight-write-eb6f355e26ee44cb9e428112d8eb388c.tmp
│   │   ├── .preflight-write-ec717f209f0f4fffb3dedcb2a4eb2de6.tmp
│   │   ├── .preflight-write-f50d114f3eba429eb5693a0d3150d8dc.tmp
│   │   └── .preflight-write-f722003487ba4727834d5c60e06e6180.tmp
│   ├── dotnet
│   │   ├── codex-asset-profiles-app
│   │   ├── codex-ledger-posting-guard
│   │   ├── codex-ledger-posting-guard-storage
│   │   ├── codex-ledger-provenance-ui-shared
│   │   ├── codex-security-casework-tests
│   │   ├── codex-wpf-shell
│   │   ├── codex-wpf-shell-home-verify
│   │   ├── codex-wpf-shell-verify
│   │   ├── codex-wpf-shell-build.log
│   │   ├── codex-wpf-shell-home-build-restore.log
│   │   └── codex-wpf-shell-home-build.log
│   ├── logs
│   │   ├── ledger-provenance-ui-shared-build.log
│   │   ├── ui-shared-build.log
│   │   └── wpf-release-build.log
│   ├── pilot-acceptance
│   │   └── latest
│   │       ├── pilot-readiness.json
│   │       └── pilot-readiness.md
│   ├── publish
│   │   └── web-workstation
│   │       └── win-x64
│   │           ├── Banking
│   │           │   └── Migrations
│   │           │       └── 001_banking.sql
│   │           ├── DirectLending
│   │           │   └── Migrations
│   │           │       ├── 001_direct_lending.sql
│   │           │       ├── 002_direct_lending_projections.sql
│   │           │       ├── 003_direct_lending_accrual_and_event_metadata.sql
│   │           │       ├── 004_direct_lending_event_schema_and_snapshots.sql
│   │           │       ├── 005_direct_lending_operations.sql
│   │           │       ├── 005_direct_lending_workflows.sql
│   │           │       ├── 006_direct_lending_operations_workflow_audit.sql
│   │           │       ├── 006_direct_lending_terms_projection_extended_fields.sql
│   │           │       └── 007_direct_lending_command_idempotency.sql
│   │           ├── FundAccounts
│   │           │   └── Migrations
│   │           │       └── 001_fund_accounts.sql
│   │           ├── FundStructure
│   │           │   └── Migrations
│   │           │       └── 001_fund_structure.sql
│   │           ├── Ledger
│   │           │   └── Migrations
│   │           │       ├── V_ledger_001__journal_entries.sql
│   │           │       ├── V_ledger_002__accounting_periods.sql
│   │           │       ├── V_ledger_003__ledger_books.sql
│   │           │       ├── V_ledger_004__accounting_basis_policies.sql
│   │           │       ├── V_ledger_005__journal_basis_lineage.sql
│   │           │       ├── V_ledger_006__journal_posting_kind.sql
│   │           │       ├── V_ledger_007__journal_adjustment_approval_metadata.sql
│   │           │       └── V_ledger_008__operations_continuity.sql
│   │           ├── MoneyMarket
│   │           │   └── Migrations
│   │           │       └── 001_money_market.sql
│   │           ├── SecurityMaster
│   │           │   └── Migrations
│   │           │       ├── 001_security_master.sql
│   │           │       ├── 002_security_master_fts.sql
│   │           │       ├── 003_security_master_corp_actions.sql
│   │           │       ├── 004_security_master_operator_overrides.sql
│   │           │       ├── 005_security_master_bond_reference_projection.sql
│   │           │       ├── 006_security_master_option_reference_projection.sql
│   │           │       ├── 007_security_master_equity_projection.sql
│   │           │       ├── 008_security_master_future_projection.sql
│   │           │       ├── 009_security_master_fxspot_projection.sql
│   │           │       ├── 010_security_master_swap_projection.sql
│   │           │       ├── 011_security_master_commodity_projection.sql
│   │           │       ├── 012_security_master_crypto_projection.sql
│   │           │       ├── 013_security_master_deposit_projection.sql
│   │           │       ├── 014_security_master_money_market_fund_projection.sql
│   │           │       ├── 015_security_master_certificate_of_deposit_projection.sql
│   │           │       └── 016_security_master_normalized_identifier_lookup.sql
│   │           ├── aspnetcorev2_inprocess.dll
│   │           ├── Meridian.Application.pdb
│   │           ├── Meridian.Application.xml
│   │           ├── Meridian.Backtesting.pdb
│   │           ├── Meridian.Backtesting.Sdk.pdb
│   │           ├── Meridian.Contracts.pdb
│   │           ├── Meridian.Contracts.xml
│   │           ├── Meridian.Core.pdb
│   │           ├── Meridian.Core.xml
│   │           ├── Meridian.Domain.pdb
│   │           ├── Meridian.Domain.xml
│   │           ├── Meridian.exe
│   │           ├── Meridian.Execution.pdb
│   │           ├── Meridian.Execution.Sdk.pdb
│   │           ├── Meridian.FSharp.DirectLending.Aggregates.pdb
│   │           ├── Meridian.FSharp.DirectLending.Aggregates.xml
│   │           ├── Meridian.FSharp.Ledger.pdb
│   │           ├── Meridian.FSharp.Ledger.xml
│   │           ├── Meridian.FSharp.pdb
│   │           ├── Meridian.FSharp.Trading.pdb
│   │           ├── Meridian.FSharp.Trading.xml
│   │           ├── Meridian.FSharp.xml
│   │           ├── Meridian.Infrastructure.pdb
│   │           ├── Meridian.Infrastructure.xml
│   │           ├── Meridian.Ledger.pdb
│   │           ├── Meridian.ProviderSdk.pdb
│   │           ├── Meridian.ProviderSdk.xml
│   │           ├── Meridian.QuantScript.pdb
│   │           ├── Meridian.Storage.pdb
│   │           ├── Meridian.Storage.xml
│   │           ├── Meridian.Strategies.pdb
│   │           ├── Meridian.Ui.Shared.pdb
│   │           ├── Meridian.Ui.Shared.xml
│   │           ├── Meridian.xml
│   │           └── Microsoft.Data.SqlClient.SNI.dll
│   ├── test-results
│   │   └── desktop-ui
│   │       ├── restore-stderr.txt
│   │       ├── restore-stdout.txt
│   │       ├── symbols-page-build-stderr.txt
│   │       ├── symbols-page-build-stdout.txt
│   │       ├── symbols-page-test-nofull-stderr.txt
│   │       ├── symbols-page-test-nofull-stdout.txt
│   │       ├── symbols-page-test-stderr.txt
│   │       └── symbols-page-test-stdout.txt
│   ├── tmp
│   │   └── meridian-preprocessed.xml
│   ├── validation
│   │   └── desktop-workflow-script-test.log
│   ├── wpf-validation
│   │   ├── codex-strangler-shell
│   │   │   ├── 20260529-010659
│   │   │   │   ├── build-wpf.log
│   │   │   │   ├── restore-wpf.log
│   │   │   │   ├── wpf-dev-test-validation.json
│   │   │   │   └── wpf-dev-test-validation.md
│   │   │   ├── 20260529-013825
│   │   │   │   ├── build-wpf.log
│   │   │   │   ├── restore-wpf.log
│   │   │   │   ├── wpf-dev-test-validation.json
│   │   │   │   └── wpf-dev-test-validation.md
│   │   │   ├── 20260529-021047
│   │   │   │   ├── build-wpf.log
│   │   │   │   ├── restore-wpf.log
│   │   │   │   ├── wpf-dev-test-validation.json
│   │   │   │   └── wpf-dev-test-validation.md
│   │   │   ├── 20260529-023326
│   │   │   │   ├── active-dotnet-processes.log
│   │   │   │   ├── wpf-dev-test-validation.json
│   │   │   │   └── wpf-dev-test-validation.md
│   │   │   └── 20260529-025306
│   │   │       ├── active-dotnet-processes.log
│   │   │       ├── wpf-dev-test-validation.json
│   │   │       └── wpf-dev-test-validation.md
│   │   ├── dev-loop
│   │   │   ├── 20260528-034858
│   │   │   │   ├── build-wpf.log
│   │   │   │   ├── restore-tests.log
│   │   │   │   ├── restore-wpf.log
│   │   │   │   ├── wpf-dev-test-validation.json
│   │   │   │   └── wpf-dev-test-validation.md
│   │   │   ├── 20260528-035732
│   │   │   │   ├── build-wpf.log
│   │   │   │   ├── restore-tests.log
│   │   │   │   ├── restore-wpf.log
│   │   │   │   ├── wpf-dev-test-validation.json
│   │   │   │   └── wpf-dev-test-validation.md
│   │   │   ├── 20260528-040133
│   │   │   │   ├── build-wpf.log
│   │   │   │   ├── restore-wpf.log
│   │   │   │   ├── wpf-dev-test-validation.json
│   │   │   │   └── wpf-dev-test-validation.md
│   │   │   ├── 20260528-043227
│   │   │   │   ├── build-wpf.log
│   │   │   │   ├── restore-tests.log
│   │   │   │   ├── restore-wpf.log
│   │   │   │   ├── wpf-dev-test-validation.json
│   │   │   │   └── wpf-dev-test-validation.md
│   │   │   ├── 20260528-045325
│   │   │   │   ├── build-wpf.log
│   │   │   │   ├── wpf-dev-test-validation.json
│   │   │   │   └── wpf-dev-test-validation.md
│   │   │   ├── 20260528-045609
│   │   │   │   ├── build-tests.log
│   │   │   │   ├── build-wpf.log
│   │   │   │   ├── test.log
│   │   │   │   ├── wpf-dev-test-validation.json
│   │   │   │   └── wpf-dev-test-validation.md
│   │   │   ├── 20260528-173425
│   │   │   │   ├── active-dotnet-processes.log
│   │   │   │   ├── wpf-dev-test-validation.json
│   │   │   │   └── wpf-dev-test-validation.md
│   │   │   ├── 20260528-214842
│   │   │   │   ├── active-dotnet-processes.log
│   │   │   │   ├── wpf-dev-test-validation.json
│   │   │   │   └── wpf-dev-test-validation.md
│   │   │   ├── 20260528-220809
│   │   │   │   ├── active-dotnet-processes.log
│   │   │   │   ├── wpf-dev-test-validation.json
│   │   │   │   └── wpf-dev-test-validation.md
│   │   │   ├── 20260528-225001
│   │   │   │   ├── active-dotnet-processes.log
│   │   │   │   ├── wpf-dev-test-validation.json
│   │   │   │   └── wpf-dev-test-validation.md
│   │   │   └── 20260529-001013
│   │   │       ├── active-dotnet-processes.log
│   │   │       ├── wpf-dev-test-validation.json
│   │   │       └── wpf-dev-test-validation.md
│   │   ├── operator-inbox-route
│   │   │   ├── 20260528-170914
│   │   │   │   ├── build.log
│   │   │   │   ├── operator-inbox-route-validation.json
│   │   │   │   └── operator-inbox-route-validation.md
│   │   │   ├── local-context-strip
│   │   │   ├── local-mainpage-blame
│   │   │   │   └── 926090c7-374c-4c78-aba7-323c58ea47be
│   │   │   │       ├── Sequence_3af7ce0da0b441c1949890c400b9311a.xml
│   │   │   │       └── testhost_30620_20260528T183816_hangdump.dmp
│   │   │   ├── local-mainpage-blame-after-fix
│   │   │   │   └── fd4ef8d8-af40-4f7a-a947-12aab153492c
│   │   │   │       ├── Sequence_8b5935af80884fdfbc2b932040e420d3.xml
│   │   │   │       └── testhost_33432_20260528T192714_hangdump.dmp
│   │   │   ├── local-operator-filter-after-fix
│   │   │   ├── local-recent-empty-after-fix
│   │   │   ├── local-recent-empty-blame
│   │   │   │   └── 61d6fc09-9548-4061-9f5e-a68cd4160d35
│   │   │   │       ├── Sequence_70311482c0a4468a8485dc4cbc7f7b6e.xml
│   │   │   │       └── testhost_12192_20260528T184113_hangdump.dmp
│   │   │   ├── local-recent-empty-build-test.log
│   │   │   ├── local-wpf-nodeps-build.log
│   │   │   ├── local-wpf-tests-full-nodeps-build.log
│   │   │   └── local-wpf-tests-nodeps-build.log
│   │   ├── position-blotter-route
│   │   │   ├── 20260528-170915
│   │   │   │   ├── build.log
│   │   │   │   ├── position-blotter-route-validation.json
│   │   │   │   └── position-blotter-route-validation.md
│   │   │   ├── 20260528-171317
│   │   │   │   ├── build.log
│   │   │   │   ├── position-blotter-route-validation.json
│   │   │   │   └── position-blotter-route-validation.md
│   │   │   └── 20260528-173218
│   │   │       └── build.log
│   │   └── catalog-text-test.log
│   ├── appservice-registration-build.log
│   ├── cashflow-build.err.log
│   ├── cashflow-build.log
│   ├── cashflow-test-retry.err.log
│   ├── cashflow-test-retry.log
│   ├── codex-meridian-tests-build.log
│   ├── codex-order-governance-build.log
│   ├── contracts-build.stderr.log
│   ├── contracts-build.stdout.log
│   ├── desktop-launcher-host.stderr.log
│   ├── desktop-launcher-host.stdout.log
│   ├── desktop-launcher.stderr.log
│   ├── desktop-launcher.stdout.log
│   ├── desktop-window-check-final.png
│   ├── desktop-window-check-foreground.png
│   ├── desktop-window-check.png
│   ├── symbols-page-build.stderr.log
│   ├── symbols-page-build.stdout.log
│   ├── symbols-test-build.log
│   ├── symbols-wpf-build.log
│   ├── symbols-wpf-nodeps-build.log
│   ├── symbols-wpf-project-build.log
│   ├── ui-shared-nodeps-build.log
│   ├── wpf-nodeps-fast-build.log
│   └── wpf-tests-source-regressions-build.log
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
│   ├── results
│   │   ├── 20260521_110732
│   │   ├── 20260521_111257
│   │   │   ├── batchserialization
│   │   │   │   ├── results
│   │   │   │   │   ├── Meridian.Benchmarks.BatchSerializationBenchmarks-report-github.md
│   │   │   │   │   ├── Meridian.Benchmarks.BatchSerializationBenchmarks-report.csv
│   │   │   │   │   └── Meridian.Benchmarks.BatchSerializationBenchmarks-report.html
│   │   │   │   └── Meridian.Benchmarks.BatchSerializationBenchmarks-20260521-114851.log
│   │   │   ├── canonicalizing
│   │   │   │   ├── results
│   │   │   │   │   ├── Meridian.Benchmarks.CanonicalizingPublisherBenchmarks-report-github.md
│   │   │   │   │   ├── Meridian.Benchmarks.CanonicalizingPublisherBenchmarks-report.csv
│   │   │   │   │   └── Meridian.Benchmarks.CanonicalizingPublisherBenchmarks-report.html
│   │   │   │   ├── BenchmarkRun-20260521-115950.log
│   │   │   │   └── Meridian.Benchmarks.CanonicalizingPublisherBenchmarks-20260521-120958.log
│   │   │   ├── collectors
│   │   │   │   ├── results
│   │   │   │   └── BenchmarkRun-20260521-113045.log
│   │   │   ├── composite
│   │   │   │   ├── results
│   │   │   │   │   ├── Meridian.Benchmarks.CompositeSinkBenchmarks-report-github.md
│   │   │   │   │   ├── Meridian.Benchmarks.CompositeSinkBenchmarks-report.csv
│   │   │   │   │   └── Meridian.Benchmarks.CompositeSinkBenchmarks-report.html
│   │   │   │   └── Meridian.Benchmarks.CompositeSinkBenchmarks-20260521-115950.log
│   │   │   ├── dedup
│   │   │   │   ├── results
│   │   │   │   └── BenchmarkRun-20260521-114411.log
│   │   │   ├── end-to-end
│   │   │   │   ├── results
│   │   │   │   └── Meridian.Benchmarks.EndToEndPipelineBenchmarks-20260521-111315.log
│   │   │   └── wal
│   │   │       ├── results
│   │   │       │   ├── Meridian.Benchmarks.WalChecksumBenchmarks-report-github.md
│   │   │       │   ├── Meridian.Benchmarks.WalChecksumBenchmarks-report.csv
│   │   │       │   └── Meridian.Benchmarks.WalChecksumBenchmarks-report.html
│   │   │       └── Meridian.Benchmarks.WalChecksumBenchmarks-20260521-114851.log
│   │   ├── 20260521_180245
│   │   │   └── results
│   │   │       ├── Meridian.Benchmarks.CanonicalizingPublisherBenchmarks-report-github.md
│   │   │       ├── Meridian.Benchmarks.CanonicalizingPublisherBenchmarks-report.csv
│   │   │       ├── Meridian.Benchmarks.CanonicalizingPublisherBenchmarks-report.html
│   │   │       ├── Meridian.Benchmarks.CanonicalizingPublisherThroughputBenchmarks-report-github.md
│   │   │       ├── Meridian.Benchmarks.CanonicalizingPublisherThroughputBenchmarks-report.csv
│   │   │       ├── Meridian.Benchmarks.CanonicalizingPublisherThroughputBenchmarks-report.html
│   │   │       ├── Meridian.Benchmarks.CompositeSinkBenchmarks-report-github.md
│   │   │       ├── Meridian.Benchmarks.CompositeSinkBenchmarks-report.csv
│   │   │       ├── Meridian.Benchmarks.CompositeSinkBenchmarks-report.html
│   │   │       ├── Meridian.Benchmarks.DepthCollectorBenchmarks-report-github.md
│   │   │       ├── Meridian.Benchmarks.DepthCollectorBenchmarks-report.csv
│   │   │       ├── Meridian.Benchmarks.DepthCollectorBenchmarks-report.html
│   │   │       ├── Meridian.Benchmarks.EndToEndPipelineBenchmarks-report-github.md
│   │   │       ├── Meridian.Benchmarks.EndToEndPipelineBenchmarks-report.csv
│   │   │       ├── Meridian.Benchmarks.EndToEndPipelineBenchmarks-report.html
│   │   │       ├── Meridian.Benchmarks.TradeCollectorBenchmarks-report-github.md
│   │   │       ├── Meridian.Benchmarks.TradeCollectorBenchmarks-report.csv
│   │   │       ├── Meridian.Benchmarks.TradeCollectorBenchmarks-report.html
│   │   │       ├── Meridian.Benchmarks.WalChecksumBenchmarks-report-github.md
│   │   │       ├── Meridian.Benchmarks.WalChecksumBenchmarks-report.csv
│   │   │       └── Meridian.Benchmarks.WalChecksumBenchmarks-report.html
│   │   ├── 20260521_181635
│   │   │   └── results
│   │   │       ├── Meridian.Benchmarks.EventPipelineBenchmarks-report-github.md
│   │   │       ├── Meridian.Benchmarks.EventPipelineBenchmarks-report.csv
│   │   │       └── Meridian.Benchmarks.EventPipelineBenchmarks-report.html
│   │   ├── 20260521_181812
│   │   │   └── results
│   │   │       ├── Meridian.Benchmarks.EventBufferBenchmarks-report-github.md
│   │   │       ├── Meridian.Benchmarks.EventBufferBenchmarks-report.csv
│   │   │       └── Meridian.Benchmarks.EventBufferBenchmarks-report.html
│   │   ├── 20260521_182021
│   │   │   └── results
│   │   │       ├── Meridian.Benchmarks.BatchSerializationBenchmarks-report-github.md
│   │   │       ├── Meridian.Benchmarks.BatchSerializationBenchmarks-report.csv
│   │   │       └── Meridian.Benchmarks.BatchSerializationBenchmarks-report.html
│   │   ├── 20260521_182313
│   │   │   └── results
│   │   │       ├── Meridian.Benchmarks.JsonSerializationBenchmarks-report-github.md
│   │   │       ├── Meridian.Benchmarks.JsonSerializationBenchmarks-report.csv
│   │   │       └── Meridian.Benchmarks.JsonSerializationBenchmarks-report.html
│   │   ├── 20260521_182521
│   │   │   └── results
│   │   │       ├── Meridian.Benchmarks.JsonParsingBenchmarks-report-github.md
│   │   │       ├── Meridian.Benchmarks.JsonParsingBenchmarks-report.csv
│   │   │       └── Meridian.Benchmarks.JsonParsingBenchmarks-report.html
│   │   ├── latest-manual
│   │   │   ├── results
│   │   │   │   ├── Meridian.Benchmarks.WalChecksumBenchmarks-report-github.md
│   │   │   │   ├── Meridian.Benchmarks.WalChecksumBenchmarks-report.csv
│   │   │   │   └── Meridian.Benchmarks.WalChecksumBenchmarks-report.html
│   │   │   ├── Meridian.Benchmarks.WalChecksumBenchmarks-20260521-123937.log
│   │   │   └── Meridian.Benchmarks.WalChecksumBenchmarks-20260521-124144.log
│   │   ├── short-run-short
│   │   │   ├── results
│   │   │   │   ├── Meridian.Benchmarks.WalChecksumBenchmarks-report-github.md
│   │   │   │   ├── Meridian.Benchmarks.WalChecksumBenchmarks-report.csv
│   │   │   │   └── Meridian.Benchmarks.WalChecksumBenchmarks-report.html
│   │   │   └── Meridian.Benchmarks.WalChecksumBenchmarks-20260521-122521.log
│   │   └── short-run-short2
│   │       ├── results
│   │       │   ├── Meridian.Benchmarks.WalChecksumBenchmarks-report-github.md
│   │       │   ├── Meridian.Benchmarks.WalChecksumBenchmarks-report.csv
│   │       │   └── Meridian.Benchmarks.WalChecksumBenchmarks-report.html
│   │       └── Meridian.Benchmarks.WalChecksumBenchmarks-20260521-123257.log
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
│   │   │   ├── __pycache__
│   │   │   │   └── buildctl.cpython-311.pyc
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
│       │   ├── check-warning-suppressions.py
│       │   └── check-workflow-hygiene.py
│       ├── docs
│       │   ├── __pycache__
│       │   │   ├── check-ai-inventory.cpython-311.pyc
│       │   │   ├── check-ai-inventory.cpython-311.pyc.1253840980848
│       │   │   ├── check-ai-inventory.cpython-311.pyc.1421052990432
│       │   │   ├── check-ai-inventory.cpython-311.pyc.1639392349040
│       │   │   ├── check-ai-inventory.cpython-311.pyc.1852411809488
│       │   │   ├── check-ai-inventory.cpython-311.pyc.1964138696400
│       │   │   ├── check-ai-inventory.cpython-311.pyc.2038736686944
│       │   │   ├── check-ai-inventory.cpython-311.pyc.2146906803920
│       │   │   ├── check-ai-inventory.cpython-311.pyc.2149375413248
│       │   │   ├── check-ai-inventory.cpython-311.pyc.2219289284448
│       │   │   ├── check-ai-inventory.cpython-311.pyc.2361990581248
│       │   │   ├── check-ai-inventory.cpython-311.pyc.2393609801568
│       │   │   ├── check-ai-inventory.cpython-311.pyc.2433520504528
│       │   │   ├── check-ai-inventory.cpython-311.pyc.2839093367776
│       │   │   ├── check-ai-inventory.cpython-311.pyc.2884838756048
│       │   │   ├── check-codex-skills.cpython-39.pyc
│       │   │   ├── common.cpython-311.pyc
│       │   │   ├── common.cpython-39.pyc
│       │   │   ├── dashboard_rendering.cpython-311.pyc
│       │   │   ├── dashboard_rendering.cpython-39.pyc
│       │   │   ├── generate-pilot-readiness-dashboard.cpython-311.pyc
│       │   │   ├── mark-stale-docs.cpython-39.pyc
│       │   │   ├── render-source-docs.cpython-39.pyc
│       │   │   ├── scan-source-todos.cpython-39.pyc
│       │   │   ├── scan-todos.cpython-311.pyc
│       │   │   ├── sync-source-readmes.cpython-311.pyc
│       │   │   ├── sync-source-readmes.cpython-39.pyc
│       │   │   ├── validate-doc-hashes.cpython-311.pyc
│       │   │   ├── validate-doc-hashes.cpython-311.pyc.1191214049888
│       │   │   ├── validate-doc-hashes.cpython-311.pyc.1269572037216
│       │   │   ├── validate-doc-hashes.cpython-311.pyc.1469965003360
│       │   │   ├── validate-doc-hashes.cpython-311.pyc.1889778133600
│       │   │   ├── validate-doc-hashes.cpython-311.pyc.1929116183136
│       │   │   ├── validate-doc-hashes.cpython-311.pyc.2177795091040
│       │   │   ├── validate-doc-hashes.cpython-311.pyc.2859794033248
│       │   │   ├── validate-doc-hashes.cpython-39.pyc
│       │   │   ├── validate-roadmap-registry.cpython-39.pyc
│       │   │   └── validate-source-readmes.cpython-39.pyc
│       │   ├── tests
│       │   │   ├── __pycache__
│       │   │   │   ├── test_check_ai_inventory.cpython-311.pyc
│       │   │   │   ├── test_check_ai_inventory.cpython-311.pyc.1253840555344
│       │   │   │   ├── test_check_ai_inventory.cpython-311.pyc.1421052157440
│       │   │   │   ├── test_check_ai_inventory.cpython-311.pyc.1639391923536
│       │   │   │   ├── test_check_ai_inventory.cpython-311.pyc.1852410895872
│       │   │   │   ├── test_check_ai_inventory.cpython-311.pyc.1964137520640
│       │   │   │   ├── test_check_ai_inventory.cpython-311.pyc.2038735773184
│       │   │   │   ├── test_check_ai_inventory.cpython-311.pyc.2146905890304
│       │   │   │   ├── test_check_ai_inventory.cpython-311.pyc.2149374790992
│       │   │   │   ├── test_check_ai_inventory.cpython-311.pyc.2219288370688
│       │   │   │   ├── test_check_ai_inventory.cpython-311.pyc.2361989893456
│       │   │   │   ├── test_check_ai_inventory.cpython-311.pyc.2393608887808
│       │   │   │   ├── test_check_ai_inventory.cpython-311.pyc.2433519590912
│       │   │   │   ├── test_check_ai_inventory.cpython-311.pyc.2839092338176
│       │   │   │   ├── test_check_ai_inventory.cpython-311.pyc.2884837711360
│       │   │   │   ├── test_pilot_readiness_dashboard.cpython-311.pyc
│       │   │   │   └── test_scan_todos.cpython-311.pyc
│       │   │   ├── test_check_ai_inventory.py
│       │   │   ├── test_markdown_generation_lint.py
│       │   │   ├── test_pilot_readiness_dashboard.py
│       │   │   └── test_scan_todos.py
│       │   ├── add-todos.py
│       │   ├── ai-docs-maintenance.py
│       │   ├── check-ai-contract-drift.py
│       │   ├── check-ai-inventory.py
│       │   ├── check-ai-navigation-freshness.py
│       │   ├── check-codex-skills.py
│       │   ├── check-known-lanes.py
│       │   ├── check-plan-checklists.py
│       │   ├── common.py
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
│       │   ├── mark-stale-docs.py
│       │   ├── README.md
│       │   ├── render-roadmap-diagrams.py
│       │   ├── render-roadmap-docs.py
│       │   ├── render-source-diagrams.py
│       │   ├── render-source-docs.py
│       │   ├── repair-links.py
│       │   ├── rules-engine.py
│       │   ├── run-docs-automation.py
│       │   ├── scan-source-todos.py
│       │   ├── scan-todos.py
│       │   ├── sync-readme-badges.py
│       │   ├── sync-source-readmes.py
│       │   ├── test-scripts.py
│       │   ├── update-claude-md.py
│       │   ├── validate-api-docs.py
│       │   ├── validate-doc-hashes.py
│       │   ├── validate-docs-structure.py
│       │   ├── validate-examples.py
│       │   ├── validate-golden-path.sh
│       │   ├── validate-roadmap-registry.py
│       │   ├── validate-skill-packages.py
│       │   ├── validate-source-readmes.py
│       │   └── validate-todo-registry.py
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
│       │   ├── measure-size.ps1
│       │   ├── publish.ps1
│       │   └── publish.sh
│       ├── run
│       │   ├── start-collector.ps1
│       │   ├── start-collector.sh
│       │   ├── stop-collector.ps1
│       │   └── stop-collector.sh
│       ├── tests
│       │   ├── test_check_ai_navigation_freshness.py
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
├── data
│   ├── _catalog
│   │   └── symbols.json
│   ├── _coordination
│   ├── _dedup
│   ├── _logs
│   │   ├── meridian-20260520.log
│   │   ├── meridian-20260521.log
│   │   ├── meridian-20260526.log
│   │   └── meridian-20260528.log
│   ├── _wal
│   │   ├── wal_20260521_044032_000000000000.wal
│   │   ├── wal_20260521_073712_000000000000.wal
│   │   ├── wal_20260529_014540_000000000000.wal
│   │   ├── wal_20260529_015551_000000000000.wal
│   │   ├── wal_20260529_020620_000000000000.wal
│   │   └── wal_20260529_022347_000000000000.wal
│   └── reports
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
│   │   │   ├── CLAUDE.testing.md
│   │   │   └── contract-policy.mirror.json
│   │   ├── codex
│   │   │   ├── quickstart.md
│   │   │   ├── README.md
│   │   │   └── route-cards.md
│   │   ├── copilot
│   │   │   ├── ai-sync-workflow.md
│   │   │   ├── contract-policy.mirror.json
│   │   │   └── instructions.md
│   │   ├── generated
│   │   │   ├── recent-changes.md
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
│   │   ├── contract-policy.json
│   │   └── README.md
│   ├── api
│   │   └── oms-ems-integration.md
│   ├── architecture
│   │   ├── diagrams
│   │   │   ├── meridian-assurance-loop.mmd
│   │   │   ├── meridian-browser-workstation-flow.mmd
│   │   │   ├── meridian-development-roadmap.mmd
│   │   │   ├── meridian-paper-session-replay-flow.mmd
│   │   │   └── meridian-source-layer-map.mmd
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
│   │   ├── operator-observability-dashboard.md
│   │   ├── overview.md
│   │   ├── project-structure.md
│   │   ├── provider-management.md
│   │   ├── README.md
│   │   ├── runtime-component-state-boundaries.md
│   │   ├── storage-design.md
│   │   ├── strategy-builder-integration.md
│   │   ├── strategy-engine-foundation.md
│   │   ├── why-this-architecture.md
│   │   ├── workflow-library.md
│   │   ├── workstation-continuity-payload-profile.md
│   │   ├── wpf-shell-mvvm.md
│   │   ├── wpf-workstation-shell-ux.md
│   │   └── write-path-invariants.md
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
│   │   ├── codex-workflow.md
│   │   ├── desktop-command-surface-migration.md
│   │   ├── desktop-resource-management.md
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
│   │   ├── modular-desktop-architecture.md
│   │   ├── otlp-trace-visualization.md
│   │   ├── process-lifecycle-diagnostics.md
│   │   ├── provider-implementation.md
│   │   ├── README.md
│   │   ├── refactor-map.md
│   │   ├── repository-organization-guide.md
│   │   ├── repository-rule-set.md
│   │   ├── rule-evaluation-contracts.md
│   │   ├── runtime-observability.md
│   │   ├── score-reason-taxonomy.md
│   │   ├── shared-workstation-components.md
│   │   ├── synthetic-provider-test-harness.md
│   │   ├── tooling-architecture.md
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
│   │   ├── source
│   │   │   ├── diagrams
│   │   │   │   ├── source-modules.mmd
│   │   │   │   └── source-readme-coverage.mmd
│   │   │   ├── render-manifest.json
│   │   │   ├── source-modules.json
│   │   │   ├── source-modules.normalized.yml
│   │   │   ├── source-readme-coverage.json
│   │   │   └── source-readme-coverage.normalized.yml
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
│   │   ├── README.md
│   │   └── tastytrade-endpoint-coverage.md
│   ├── operations
│   │   ├── broker-order-routing-phased-runbook.md
│   │   ├── canonical-buyer-workflow.md
│   │   ├── cleanup-and-maintenance.md
│   │   ├── deployment.md
│   │   ├── disk-space-hygiene.md
│   │   ├── environment-and-deployment-standard.md
│   │   ├── error-budget-policy-runbook.md
│   │   ├── failover-and-recovery-runbook.md
│   │   ├── fund-ops-persistence-cutover-runbook.md
│   │   ├── governance-operator-workflow.md
│   │   ├── high-availability.md
│   │   ├── ibkr-promotion-checklist.md
│   │   ├── live-execution-controls.md
│   │   ├── msix-packaging.md
│   │   ├── operator-runbook.md
│   │   ├── orphaned-doc-triage-index.md
│   │   ├── performance-tuning.md
│   │   ├── portable-data-packager.md
│   │   ├── preflight-checklist.md
│   │   ├── provider-credential-management.md
│   │   ├── provider-degradation-calibration.md
│   │   ├── provider-degradation-policy.md
│   │   ├── README.md
│   │   ├── reconciliation-operations.md
│   │   ├── reconciliation-policy-operations.md
│   │   ├── reconciliation-resilience-runbook.md
│   │   ├── reconciliation-runbook.md
│   │   ├── service-level-objectives.md
│   │   ├── slo-review-template.md
│   │   ├── tradier-provider-endpoint-catalog.md
│   │   ├── web-workstation-installer.md
│   │   └── workstation-governance-approval-runbook.md
│   ├── plans
│   │   ├── adapters-completion-plan.md
│   │   ├── assembly-performance-roadmap.md
│   │   ├── backtest-studio-unification-blueprint.md
│   │   ├── backtest-studio-unification-pr-sequenced-roadmap.md
│   │   ├── brokerage-portfolio-sync-blueprint.md
│   │   ├── codebase-audit-cleanup-roadmap.md
│   │   ├── current-direction-and-status.md
│   │   ├── desktop-shell-modularity-roadmap.md
│   │   ├── desktop-ui-workflow-acceptance-matrix.md
│   │   ├── desktop-workstation-screen-blueprint.checklist.json
│   │   ├── desktop-workstation-screen-blueprint.md
│   │   ├── entity-aware-workstation-capability-blueprint.md
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
│   │   ├── performance-todo-2026-05-21.md
│   │   ├── portfolio-level-backtesting-composer-blueprint.md
│   │   ├── quantscript-l3-multiinstance-round2-roadmap.md
│   │   ├── README.md
│   │   ├── research-backtest-trust-and-velocity-blueprint.md
│   │   ├── runbook-template-registry-modernization-plan.md
│   │   ├── sfo-mvp-implementation-design.md
│   │   ├── trading-workstation-migration-blueprint.md
│   │   ├── ufl-accounting-impact-model.md
│   │   ├── ufl-asset-profile-template.md
│   │   ├── ufl-bond-target-state-v2.md
│   │   ├── ufl-capability-model.md
│   │   ├── ufl-cash-sweep-target-state-v2.md
│   │   ├── ufl-certificate-of-deposit-target-state-v2.md
│   │   ├── ufl-cfd-target-state-v2.md
│   │   ├── ufl-commercial-paper-target-state-v2.md
│   │   ├── ufl-commodity-target-state-v2.md
│   │   ├── ufl-conformance-matrix.md
│   │   ├── ufl-crypto-target-state-v2.md
│   │   ├── ufl-custom-asset-composability.md
│   │   ├── ufl-deposit-target-state-v2.md
│   │   ├── ufl-direct-lending-implementation-roadmap.md
│   │   ├── ufl-direct-lending-target-state-v2.md
│   │   ├── ufl-equity-target-state-v2.md
│   │   ├── ufl-future-target-state-v2.md
│   │   ├── ufl-fx-spot-target-state-v2.md
│   │   ├── ufl-money-market-fund-target-state-v2.md
│   │   ├── ufl-option-target-state-v2.md
│   │   ├── ufl-other-security-target-state-v2.md
│   │   ├── ufl-projection-and-evidence-kernel.md
│   │   ├── ufl-repo-target-state-v2.md
│   │   ├── ufl-supported-assets-index.md
│   │   ├── ufl-swap-target-state-v2.md
│   │   ├── ufl-treasury-bill-target-state-v2.md
│   │   ├── ufl-warrant-target-state-v2.md
│   │   ├── wave-implementation-checklists.md
│   │   ├── waves-2-4-operator-readiness-addendum.md
│   │   └── web-ui-development-pivot.md
│   ├── prompts
│   │   ├── automation-prompts.md
│   │   ├── README.md
│   │   ├── repo-maintenance-prompts.md
│   │   └── roadmap-source-docs-implementation-prompt.md
│   ├── providers
│   │   ├── alpaca-setup.md
│   │   ├── backfill-guide.md
│   │   ├── broker-adapter-template-guide.md
│   │   ├── data-sources.md
│   │   ├── interactive-brokers-free-equity-reference.md
│   │   ├── interactive-brokers-setup.md
│   │   ├── provider-comparison.md
│   │   ├── provider-confidence-baseline.md
│   │   ├── README.md
│   │   ├── security-master-guide.md
│   │   ├── stocksharp-connectors.md
│   │   └── tradestation-endpoint-inventory.md
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
│   │   ├── oms-ems-integration.md
│   │   ├── open-source-references.md
│   │   ├── README.md
│   │   ├── reconciliation-break-taxonomy.md
│   │   ├── research-briefing-workflow.md
│   │   └── strategy-promotion-history.md
│   ├── roadmap
│   │   ├── data
│   │   │   ├── decision-log.yml
│   │   │   ├── document-index.yml
│   │   │   ├── program-state.yml
│   │   │   ├── risk-register.yml
│   │   │   ├── roadmap-items.yml
│   │   │   └── stage-gates.yml
│   │   ├── generated
│   │   │   ├── MANIFEST.json
│   │   │   ├── roadmap-register.md
│   │   │   └── ROADMAP_SUMMARY.md
│   │   ├── schemas
│   │   │   ├── decision-log.v1.schema.json
│   │   │   ├── document-index.v1.schema.json
│   │   │   ├── evidence-record.v1.schema.json
│   │   │   ├── program-state.v1.schema.json
│   │   │   ├── risk-register.v1.schema.json
│   │   │   ├── roadmap-item.v1.schema.json
│   │   │   ├── roadmap-items.v1.schema.json
│   │   │   ├── shared-enums.v1.schema.json
│   │   │   └── stage-gates.v1.schema.json
│   │   ├── generated-doc-policy.md
│   │   ├── README.md
│   │   ├── roadmap-governance.md
│   │   ├── roadmap-item-template.md
│   │   ├── schema-versioning.md
│   │   └── status-taxonomy.md
│   ├── screenshots
│   │   ├── desktop
│   │   │   ├── manuals
│   │   │   │   ├── manual-data-operations
│   │   │   │   │   ├── 01-data-operations-shell.png
│   │   │   │   │   ├── 02-providers.png
│   │   │   │   │   ├── 03-provider-health.png
│   │   │   │   │   ├── 04-backfill.png
│   │   │   │   │   ├── 05-data-sources.png
│   │   │   │   │   ├── 06-storage.png
│   │   │   │   │   └── 07-data-quality.png
│   │   │   │   ├── manual-governance
│   │   │   │   │   ├── 01-governance-shell.png
│   │   │   │   │   ├── 02-fund-ledger.png
│   │   │   │   │   ├── 03-fund-reconciliation.png
│   │   │   │   │   ├── 04-fund-report-pack.png
│   │   │   │   │   ├── 05-security-master.png
│   │   │   │   │   └── 06-settings.png
│   │   │   │   ├── manual-overview
│   │   │   │   │   ├── 01-research-workspace.png
│   │   │   │   │   ├── 02-workspace-layouts.png
│   │   │   │   │   ├── 03-research-workspace.png
│   │   │   │   │   ├── 04-trading-workspace.png
│   │   │   │   │   ├── 05-data-operations-workspace.png
│   │   │   │   │   ├── 06-governance-workspace.png
│   │   │   │   │   └── 07-help.png
│   │   │   │   └── manual-research-and-trading
│   │   │   │       ├── 01-research-shell.png
│   │   │   │       ├── 02-backtest.png
│   │   │   │       ├── 03-strategy-runs.png
│   │   │   │       ├── 04-quant-script.png
│   │   │   │       ├── 05-trading-shell.png
│   │   │   │       ├── 06-position-blotter.png
│   │   │   │       └── 07-run-risk.png
│   │   │   ├── README.md
│   │   │   ├── retry-telemetry.json
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
│   │   │   ├── wpf-research-workspace.png
│   │   │   ├── wpf-security-master.png
│   │   │   ├── wpf-settings.png
│   │   │   ├── wpf-storage.png
│   │   │   └── wpf-strategy-runs.png
│   │   ├── web
│   │   │   ├── web-accounting-approvals.png
│   │   │   ├── web-accounting-reconciliation.png
│   │   │   ├── web-accounting-security-master.png
│   │   │   ├── web-accounting-workspace.png
│   │   │   ├── web-data-backfills.png
│   │   │   ├── web-data-live-quotes.png
│   │   │   ├── web-data-watchlist.png
│   │   │   ├── web-data-workspace.png
│   │   │   ├── web-operator-readiness-console.png
│   │   │   ├── web-overview-workspace.png
│   │   │   ├── web-portfolio-attribution.png
│   │   │   ├── web-portfolio-brokerage-sync.png
│   │   │   ├── web-portfolio-workspace.png
│   │   │   ├── web-reporting-evidence-workbench.png
│   │   │   ├── web-reporting-exports.png
│   │   │   ├── web-reporting-report-packs.png
│   │   │   ├── web-reporting-workspace.png
│   │   │   ├── web-settings-integrations.png
│   │   │   ├── web-settings-preferences.png
│   │   │   ├── web-settings-workspace.png
│   │   │   ├── web-strategy-designer.png
│   │   │   ├── web-strategy-promotions.png
│   │   │   ├── web-strategy-quant-lab.png
│   │   │   ├── web-strategy-research.png
│   │   │   ├── web-strategy-workspace.png
│   │   │   ├── web-trading-orders.png
│   │   │   ├── web-trading-positions.png
│   │   │   ├── web-trading-risk.png
│   │   │   └── web-trading-workspace.png
│   │   └── README.md
│   ├── security
│   │   ├── buyer-packet
│   │   │   ├── architecture-summary.md
│   │   │   ├── control-mapping.md
│   │   │   ├── document-index.md
│   │   │   ├── operational-controls.md
│   │   │   ├── threat-model-summary.md
│   │   │   └── vulnerability-management.md
│   │   ├── compliance
│   │   │   ├── soc2-control-matrix.md
│   │   │   ├── soc2-evidence-calendar.md
│   │   │   ├── soc2-roadmap.md
│   │   │   └── soc2-scope.md
│   │   ├── codex-security-remediation-2026-05-20.md
│   │   ├── known-vulnerabilities.md
│   │   ├── README.md
│   │   ├── security-remediation-backlog.md
│   │   └── threat-model-current-state.md
│   ├── source
│   │   ├── data
│   │   │   ├── diagram-index.yml
│   │   │   ├── source-modules.yml
│   │   │   ├── source-readme-coverage.yml
│   │   │   ├── source-readme-ignore.yml
│   │   │   └── source-todos.yml
│   │   ├── generated
│   │   │   ├── MANIFEST.json
│   │   │   ├── source-hash-manifest.json
│   │   │   ├── source-module-index.md
│   │   │   ├── source-roadmap-traceability.md
│   │   │   ├── source-todo-checklist.md
│   │   │   ├── stale-docs.json
│   │   │   └── stale-docs.md
│   │   ├── schemas
│   │   │   ├── diagram-index.v1.schema.json
│   │   │   ├── readme-coverage.v1.schema.json
│   │   │   ├── shared-enums.v1.schema.json
│   │   │   ├── source-doc.v1.schema.json
│   │   │   ├── source-modules.v1.schema.json
│   │   │   └── source-todos.v1.schema.json
│   │   ├── README.md
│   │   ├── source-diagram-standard.md
│   │   ├── source-documentation-standard.md
│   │   ├── source-readme-template.md
│   │   ├── source-todo-standard.md
│   │   └── todo-registry.json
│   ├── status
│   │   ├── evidence
│   │   │   ├── dk1-baseline-trust-thresholds.md
│   │   │   ├── dk1-pilot-parity-runbook.md
│   │   │   ├── dk1-trust-rationale-mapping.md
│   │   │   ├── wave2-cockpit-evidence-packet.md
│   │   │   └── wave4-evidence-template.md
│   │   ├── slo-reports
│   │   │   └── README.md
│   │   ├── api-docs-report.md
│   │   ├── badge-sync-report.md
│   │   ├── CHANGELOG.md
│   │   ├── contract-compatibility-matrix.md
│   │   ├── coverage-report.md
│   │   ├── desktop-application-screens.md
│   │   ├── doc-health-dashboard.json
│   │   ├── doc-health-dashboard.md
│   │   ├── docs-automation-summary.json
│   │   ├── docs-automation-summary.md
│   │   ├── EVALUATIONS_AND_AUDITS.md
│   │   ├── example-validation.md
│   │   ├── FEATURE_INVENTORY.md
│   │   ├── FULL_IMPLEMENTATION_TODO.md
│   │   ├── fund-ops-persistence-cutover-status.md
│   │   ├── IMPROVEMENTS.md
│   │   ├── kernel-readiness-dashboard.md
│   │   ├── link-repair-report.md
│   │   ├── metrics-dashboard.md
│   │   ├── OPPORTUNITY_SCAN.md
│   │   ├── production-status.md
│   │   ├── program-state-summary.json
│   │   ├── program-state-summary.md
│   │   ├── PROGRAM_STATE.md
│   │   ├── provider-capability-matrix.md
│   │   ├── provider-integration-status.md
│   │   ├── provider-validation-evidence-schema.md
│   │   ├── provider-validation-matrix.md
│   │   ├── readiness-claim-language-policy.md
│   │   ├── README.md
│   │   ├── ROADMAP.md
│   │   ├── ROADMAP_COMBINED.md
│   │   ├── ROADMAP_SUMMARY.md
│   │   ├── rules-report.md
│   │   ├── run-contract.schema.json
│   │   ├── TARGET_END_PRODUCT.md
│   │   ├── TODO.md
│   │   ├── workflow-drift-report.md
│   │   ├── workflow-manifest.json
│   │   ├── workflow-validation-summary.json
│   │   ├── workstation-cockpit-acceptance-matrix.json
│   │   ├── workstation-cockpit-acceptance-matrix.md
│   │   └── workstation-governance-state-model.md
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
├── manifests
│   └── meridian-runtime-observability.yaml
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
│   │   ├── __pycache__
│   │   │   └── check_design_system_governance.cpython-311.pyc
│   │   └── check_design_system_governance.py
│   ├── tests
│   │   ├── __pycache__
│   │   │   └── test_design_system_governance.cpython-311.pyc
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
│   ├── README.md.bak
│   ├── SKILL.md
│   └── VISUAL_FOUNDATIONS.md
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
│   ├── __pycache__
│   │   ├── check_contract_compatibility_gate.cpython-311.pyc
│   │   └── compare_run_contract.cpython-311.pyc
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
│   │   ├── __pycache__
│   │   │   ├── desktop_screen_blueprint_checklist.cpython-311.pyc
│   │   │   ├── screenshot_workflow_plan.cpython-311.pyc
│   │   │   └── validate-screenshot-captures.cpython-311.pyc
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
│   │   ├── desktop_screen_blueprint_checklist.py
│   │   ├── diagnose-uwp-xaml.ps1
│   │   ├── generate-desktop-user-manual.ps1
│   │   ├── generate-dk1-pilot-parity-packet.ps1
│   │   ├── install-git-hooks.sh
│   │   ├── preflight_runner.py
│   │   ├── prepare-dk1-operator-signoff.ps1
│   │   ├── robinhood-options-smoke.ps1
│   │   ├── run-desktop-workflow.ps1
│   │   ├── run-desktop.ps1
│   │   ├── run-provider-validation-evidence-bundle.ps1
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
│   │   ├── validate-screenshot-captures.py
│   │   ├── validate-screenshot-contract.py
│   │   ├── validate-workflow-profile.ps1
│   │   ├── validate-wpf-dev.ps1
│   │   ├── validate_workstation_cockpit_acceptance_matrix.py
│   │   ├── web-screenshot-fixtures.json
│   │   └── web-screenshot-routes.json
│   ├── lib
│   │   ├── ui-diagram-generator.mjs
│   │   └── ui-diagram-generator.test.mjs
│   ├── load
│   │   └── synthetic_load_harness.py
│   ├── check_contract_compatibility_gate.py
│   ├── check_program_state_consistency.py
│   ├── check_status_delivery_claims.py
│   ├── check_status_doc_staleness.py
│   ├── check_workflow_docs_parity.py
│   ├── compare_benchmarks.py
│   ├── compare_run_contract.py
│   ├── example-sharpe.csx
│   ├── generate-diagrams.mjs
│   ├── generate_contract_review_packet.py
│   ├── generate_program_state_summary.py
│   ├── report_canonicalization_drift.py
│   ├── update_coverage_report.py
│   └── wpf_finance_ux_checks.py
├── src
│   ├── Meridian
│   │   ├── artifacts
│   │   │   └── codex-provider-ledger-20260528164336
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
│   │   ├── HostedBrokerageGatewayRuntimeSurfaceCatalog.cs
│   │   ├── HostedBrokerageGatewayServiceCollectionExtensions.cs
│   │   ├── Meridian.csproj
│   │   ├── Program.cs
│   │   ├── README.md
│   │   ├── runtimeconfig.template.json
│   │   └── UiServer.cs
│   ├── Meridian.Application
│   │   ├── AccountingClose
│   │   │   ├── AccountingCloseModels.cs
│   │   │   └── AccountingCloseServices.cs
│   │   ├── Accounts
│   │   │   ├── IAccountManagementService.cs
│   │   │   └── IAccountQueryService.cs
│   │   ├── artifacts
│   │   │   └── codex-provider-ledger-20260528164336
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
│   │   │   ├── InMemoryBankingService.cs
│   │   │   └── PostgresBankingService.cs
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
│   │   │   ├── SimulationCommands.cs
│   │   │   ├── StatementCommands.cs
│   │   │   ├── StatementImportCommands.cs
│   │   │   ├── SymbolCommands.cs
│   │   │   ├── ValidateConfigCommand.cs
│   │   │   └── WalRepairCommand.cs
│   │   ├── Commodities
│   │   │   ├── CommodityProjectionService.cs
│   │   │   └── ICommodityReferenceService.cs
│   │   ├── Compliance
│   │   │   ├── ComplianceModels.cs
│   │   │   └── ComplianceServices.cs
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
│   │   │   │   ├── ProviderFeatureRegistration.OptionsChain.cs
│   │   │   │   ├── ProviderFeatureRegistration.Registry.cs
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
│   │   │   │   ├── CommandDispatchPlanner.cs
│   │   │   │   ├── HostModeOrchestrator.cs
│   │   │   │   ├── SharedStartupBootstrapper.cs
│   │   │   │   ├── SharedStartupHelpers.cs
│   │   │   │   ├── StartupOrchestrator.cs
│   │   │   │   └── StartupValidationRunner.cs
│   │   │   ├── BankingStartup.cs
│   │   │   ├── CircuitBreakerCallbackRouter.cs
│   │   │   ├── DirectLendingStartup.cs
│   │   │   ├── FundAccountsStartup.cs
│   │   │   ├── FundStructureStartup.cs
│   │   │   ├── HostAdapters.cs
│   │   │   ├── HostStartup.cs
│   │   │   ├── LedgerStartup.cs
│   │   │   ├── MoneyMarketStartup.cs
│   │   │   ├── ProductionServiceRegistrationPolicy.cs
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
│   │   │   ├── InMemoryFundAccountService.cs
│   │   │   └── PostgresFundAccountService.cs
│   │   ├── FundOperationsPersistence
│   │   │   ├── CanonicalProjectionSchemas.cs
│   │   │   ├── DomainReadSwitch.cs
│   │   │   ├── FileShadowProjectionWriter.cs
│   │   │   ├── FundOperationsPersistenceContracts.cs
│   │   │   └── ProjectionReconciliationHostedService.cs
│   │   ├── FundStructure
│   │   │   ├── FundAccountTraversalQueryService.cs
│   │   │   ├── FundStructurePolicyService.cs
│   │   │   ├── GovernanceSharedDataAccessService.cs
│   │   │   ├── IFundAccountTraversalQueryService.cs
│   │   │   ├── IFundStructurePolicyService.cs
│   │   │   ├── IFundStructureService.cs
│   │   │   ├── IFundStructureStateStore.cs
│   │   │   ├── IGovernanceSharedDataAccessService.cs
│   │   │   ├── InMemoryFundStructureService.cs
│   │   │   ├── InMemoryFundStructureStateStore.cs
│   │   │   ├── JsonFileFundStructureStateStore.cs
│   │   │   ├── LedgerGroupingRules.cs
│   │   │   ├── LedgerMappingWorkbenchService.cs
│   │   │   └── PostgresFundStructureService.cs
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
│   │   ├── OperationsContinuity
│   │   │   ├── OperationsApprovalPolicyMatrixService.cs
│   │   │   ├── OperationsCloseCalendarService.cs
│   │   │   ├── OperationsContinuityRepositories.cs
│   │   │   ├── OperationsContinuityWorkflow.cs
│   │   │   ├── OperationsContinuityWorkflowService.cs
│   │   │   ├── OperationsStatusDerivationService.cs
│   │   │   ├── OperationsWorkflowAuditHashing.cs
│   │   │   └── PostgresOperationsContinuityStore.cs
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
│   │   │   ├── ProviderRoutingMapper.cs
│   │   │   └── ProviderSetupService.cs
│   │   ├── Reconciliation
│   │   │   ├── BrokerCustodianReconciliationModels.cs
│   │   │   ├── BrokerReconciliationFeedModels.cs
│   │   │   ├── CanonicalReconciliationEngine.cs
│   │   │   ├── FileReconciliationDecisionJournal.cs
│   │   │   ├── ReconciliationContractCatalog.cs
│   │   │   ├── ReconciliationContracts.cs
│   │   │   ├── ReconciliationEngine.cs
│   │   │   ├── ReconciliationOrchestrationResilience.cs
│   │   │   ├── StatementBreakClassifier.cs
│   │   │   ├── StatementMappingProfiles.cs
│   │   │   ├── StatementMatchingEngine.cs
│   │   │   ├── StatementReconciliationOrchestrator.cs
│   │   │   ├── StatementReconciliationService.cs
│   │   │   ├── StatementRepositories.cs
│   │   │   ├── StatementRunCreateRequest.cs
│   │   │   ├── StatementRunEvidenceLinks.cs
│   │   │   ├── StatementToleranceProfiles.cs
│   │   │   └── StatementValidationService.cs
│   │   ├── Reporting
│   │   │   ├── DefaultReportingTemplateCatalog.cs
│   │   │   ├── ReportingContracts.cs
│   │   │   └── ReportingOrchestrationService.cs
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
│   │   │   ├── AssetClassValidatorRegistry.cs
│   │   │   ├── EdgarIngestOrchestrator.cs
│   │   │   ├── FileSecurityValidationSnapshotStore.cs
│   │   │   ├── IEdgarIngestOrchestrator.cs
│   │   │   ├── ILivePositionCorporateActionAdjuster.cs
│   │   │   ├── ISecurityMasterQueryService.cs
│   │   │   ├── ISecurityMasterService.cs
│   │   │   ├── ISecurityMasterWorkbenchQueryService.cs
│   │   │   ├── ISecurityResolver.cs
│   │   │   ├── IUflProjectionRebuilder.cs
│   │   │   ├── NullSecurityMasterServices.cs
│   │   │   ├── SecurityAssetProfileCatalog.cs
│   │   │   ├── SecurityAssetProfileGovernanceService.cs
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
│   │   │   ├── SecurityValidationGateService.cs
│   │   │   ├── SecurityValidationService.cs
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
│   │   │   ├── ExecutionSimulationOrchestrator.cs
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
│   │   │   ├── RuntimeDiagnosticRedactor.cs
│   │   │   ├── SampleDataGenerator.cs
│   │   │   ├── ServiceRegistry.cs
│   │   │   ├── ShutdownDiagnosticsService.cs
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
│   │   │   ├── InMemoryMoneyMarketFundService.cs
│   │   │   └── PostgresMoneyMarketFundService.cs
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
│   │   ├── Workflows
│   │   │   └── FundWorkflowCommandHandler.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.Application.csproj
│   │   └── README.md
│   ├── Meridian.Backtesting
│   │   ├── artifacts
│   │   │   └── codex-provider-ledger-20260528164336
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
│   │   ├── MeridianNativeBacktestStudioEngine.cs
│   │   └── README.md
│   ├── Meridian.Backtesting.Sdk
│   │   ├── artifacts
│   │   │   └── codex-provider-ledger-20260528164336
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
│   │   ├── CanonicalBacktestResultNormalizer.cs
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
│   │   ├── README.md
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
│   │   │   ├── ProviderSetupApiModels.cs
│   │   │   ├── SecurityMasterIngestStatusModels.cs
│   │   │   ├── StatusEndpointModels.cs
│   │   │   ├── StatusModels.cs
│   │   │   ├── UiApiClient.cs
│   │   │   ├── UiApiRoutes.cs
│   │   │   └── UiDashboardModels.cs
│   │   ├── Archive
│   │   │   └── ArchiveHealthModels.cs
│   │   ├── artifacts
│   │   │   └── codex-provider-ledger-20260528164336
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
│   │   │   ├── Reconciliation
│   │   │   │   └── ReconciliationDomainModels.cs
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
│   │   │   ├── FundStructureSetupWorkflowDtos.cs
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
│   │   │   ├── SecurityAssetClassCatalog.cs
│   │   │   ├── SecurityAssetProfiles.cs
│   │   │   ├── SecurityCommands.cs
│   │   │   ├── SecurityDtos.cs
│   │   │   ├── SecurityEvents.cs
│   │   │   ├── SecurityIdentifierNormalizer.cs
│   │   │   ├── SecurityIdentifiers.cs
│   │   │   ├── SecurityMasterOptions.cs
│   │   │   ├── SecurityMasterSchemaVersions.cs
│   │   │   ├── SecurityQueries.cs
│   │   │   └── SecurityValidationDtos.cs
│   │   ├── Services
│   │   │   ├── IBacktestPreflightService.cs
│   │   │   └── IConnectivityProbeService.cs
│   │   ├── Session
│   │   │   └── CollectionSession.cs
│   │   ├── Store
│   │   │   └── MarketDataQuery.cs
│   │   ├── StrategyEngine
│   │   │   └── StrategyEngineContracts.cs
│   │   ├── Treasury
│   │   │   └── MoneyMarketFundDtos.cs
│   │   ├── Workstation
│   │   │   ├── AuditTrailExplorerDtos.cs
│   │   │   ├── BrokerageSyncDtos.cs
│   │   │   ├── CashOperationsDtos.cs
│   │   │   ├── CollateralExposureDtos.cs
│   │   │   ├── EvidenceWorkflowDtos.cs
│   │   │   ├── FeatureCapabilityDtos.cs
│   │   │   ├── FundLedgerDtos.cs
│   │   │   ├── FundOperationsDtos.cs
│   │   │   ├── FundOperationsWorkspaceDtos.cs
│   │   │   ├── FundWorkflowCommands.cs
│   │   │   ├── InvestmentAccountingTransactionLabDtos.cs
│   │   │   ├── IOperatorInboxService.cs
│   │   │   ├── LedgerReconciliationContractCompatibility.cs
│   │   │   ├── OperationsContinuityDtos.cs
│   │   │   ├── PilotReadinessArtifactDtos.cs
│   │   │   ├── ReconciliationDtos.cs
│   │   │   ├── ResearchBriefingDtos.cs
│   │   │   ├── SecurityMasterTrustWorkbenchDtos.cs
│   │   │   ├── SecurityMasterWorkstationDtos.cs
│   │   │   ├── StatementReconciliationDtos.cs
│   │   │   ├── StrategyDesignDtos.cs
│   │   │   ├── StrategyRunContractCompatibility.cs
│   │   │   ├── StrategyRunReadModels.cs
│   │   │   ├── TradingOperatorReadinessDtos.cs
│   │   │   ├── WorkflowLibraryDtos.cs
│   │   │   ├── WorkflowSummaryDtos.cs
│   │   │   └── WorkstationBootstrapDtos.cs
│   │   ├── Meridian.Contracts.csproj
│   │   └── README.md
│   ├── Meridian.Core
│   │   ├── artifacts
│   │   │   └── codex-provider-ledger-20260528164336
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
│   │   │   ├── FeatureCapabilityCatalog.cs
│   │   │   ├── FeatureCapabilityDescriptor.cs
│   │   │   ├── FeatureCapabilityOptions.cs
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
│   │   ├── Meridian.Core.csproj
│   │   └── README.md
│   ├── Meridian.Domain
│   │   ├── artifacts
│   │   │   └── codex-provider-ledger-20260528164336
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
│   │   │   ├── CanonicalReconciliation.cs
│   │   │   ├── StatementEntities.cs
│   │   │   └── StatementReconciliationAggregate.cs
│   │   ├── Telemetry
│   │   │   └── MarketEventIngressTracing.cs
│   │   ├── BannedReferences.txt
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.Domain.csproj
│   │   └── README.md
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
│   │   │   └── codex-provider-ledger-20260528164336
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
│   │   │   ├── PositionSyncOptions.cs
│   │   │   └── ReplayDriftDetector.cs
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
│   │   ├── README.md
│   │   └── SecurityMasterGate.cs
│   ├── Meridian.Execution.Sdk
│   │   ├── artifacts
│   │   │   └── codex-provider-ledger-20260528164336
│   │   ├── Derivatives
│   │   │   ├── FutureDetails.cs
│   │   │   ├── OptionDetails.cs
│   │   │   └── OptionGreeks.cs
│   │   ├── BrokerageConfiguration.cs
│   │   ├── BrokerageOrderPlacementGate.cs
│   │   ├── BrokerageValidationEvaluator.cs
│   │   ├── ExecutionOrderMetadataPolicy.cs
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
│   │   ├── README.md
│   │   └── TaxLot.cs
│   ├── Meridian.FSharp
│   │   ├── artifacts
│   │   │   └── codex-provider-ledger-20260528164336
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
│   │   ├── Operations
│   │   │   ├── OperationsContinuityRules.fs
│   │   │   ├── ReportPackValidationRules.fs
│   │   │   ├── SensitiveActionPolicy.fs
│   │   │   └── TradingReadinessRules.fs
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
│   │   ├── Meridian.FSharp.fsproj
│   │   └── README.md
│   ├── Meridian.FSharp.DirectLending.Aggregates
│   │   ├── artifacts
│   │   │   └── codex-provider-ledger-20260528164336
│   │   ├── AggregateTypes.fs
│   │   ├── ContractAggregate.fs
│   │   ├── Interop.fs
│   │   ├── Meridian.FSharp.DirectLending.Aggregates.fsproj
│   │   ├── README.md
│   │   └── ServicingAggregate.fs
│   ├── Meridian.FSharp.Ledger
│   │   ├── artifacts
│   │   │   └── codex-provider-ledger-20260528164336
│   │   ├── AccrualTypes.fs
│   │   ├── Interop.fs
│   │   ├── JournalValidation.fs
│   │   ├── LedgerReadModels.fs
│   │   ├── LedgerTypes.fs
│   │   ├── Meridian.FSharp.Ledger.fsproj
│   │   ├── PeriodManagement.fs
│   │   ├── Posting.fs
│   │   ├── README.md
│   │   ├── Reconciliation.fs
│   │   ├── ReconciliationCaseWorkflow.fs
│   │   ├── ReconciliationClassification.fs
│   │   ├── ReconciliationRules.fs
│   │   └── ReconciliationTypes.fs
│   ├── Meridian.FSharp.Trading
│   │   ├── artifacts
│   │   │   └── codex-provider-ledger-20260528164336
│   │   ├── Interop.fs
│   │   ├── Meridian.FSharp.Trading.fsproj
│   │   ├── PromotionReadiness.fs
│   │   ├── README.md
│   │   ├── StrategyLifecycleState.fs
│   │   ├── StrategyLifecycleTransitions.fs
│   │   └── StrategyRunTypes.fs
│   ├── Meridian.IbApi.SmokeStub
│   │   ├── IBApiSmokeStub.cs
│   │   ├── Meridian.IbApi.SmokeStub.csproj
│   │   └── README.md
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
│   │   │   │   ├── IProviderConnectionDiagnosticsSource.cs
│   │   │   │   ├── ISymbolSearchProvider.cs
│   │   │   │   ├── ProviderBehaviorBuilder.cs
│   │   │   │   ├── ProviderCapabilityDescriptorCatalog.cs
│   │   │   │   ├── ProviderDataQualityValidator.cs
│   │   │   │   ├── ProviderFactory.cs
│   │   │   │   ├── ProviderIdentity.cs
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
│   │   │   │   ├── IBCanonicalPayloadMapper.cs
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
│   │   │   │   ├── BrokerAdapterTemplate.cs
│   │   │   │   └── TemplateBrokerageGateway.cs
│   │   │   ├── Tiingo
│   │   │   │   └── TiingoHistoricalDataProvider.cs
│   │   │   ├── TradeStation
│   │   │   │   └── TradeStationPayloadMappers.cs
│   │   │   ├── Tradier
│   │   │   │   └── TradierCanonicalMappers.cs
│   │   │   ├── TwelveData
│   │   │   │   └── TwelveDataHistoricalDataProvider.cs
│   │   │   └── YahooFinance
│   │   │       └── YahooFinanceHistoricalDataProvider.cs
│   │   ├── artifacts
│   │   │   └── codex-provider-ledger-20260528164336
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
│   │   ├── NoOpMarketDataClient.cs
│   │   └── README.md
│   ├── Meridian.Infrastructure.CppTrader
│   ├── Meridian.Ledger
│   │   ├── artifacts
│   │   │   └── codex-provider-ledger-20260528164336
│   │   ├── AutomatedJournalApproval.cs
│   │   ├── AutomatedJournalApprovalEvent.cs
│   │   ├── AutomatedJournalApprovalStatus.cs
│   │   ├── AutomatedJournalDraft.cs
│   │   ├── AutomatedJournalDraftProjector.cs
│   │   ├── AutomatedJournalEvent.cs
│   │   ├── AutomatedJournalEventKind.cs
│   │   ├── ChartOfAccounts.cs
│   │   ├── ChartOfAccountsNode.cs
│   │   ├── DailyPortfolioPriceMark.cs
│   │   ├── DailyPortfolioPricingInput.cs
│   │   ├── DailyPortfolioPricingLine.cs
│   │   ├── DailyPortfolioPricingPolicy.cs
│   │   ├── DailyPortfolioPricingProjection.cs
│   │   ├── DailyPortfolioPricingProjector.cs
│   │   ├── FixedIncomeAmortizationInput.cs
│   │   ├── FixedIncomeAmortizationProjection.cs
│   │   ├── FixedIncomeAmortizationProjector.cs
│   │   ├── FundLedgerBook.cs
│   │   ├── GlobalUsings.cs
│   │   ├── IReadOnlyLedger.cs
│   │   ├── JournalEntry.cs
│   │   ├── JournalEntryMetadata.cs
│   │   ├── Ledger.cs
│   │   ├── LedgerAccount.cs
│   │   ├── LedgerAccounts.cs
│   │   ├── LedgerAccountSummary.cs
│   │   ├── LedgerAccountTaxLotPolicy.cs
│   │   ├── LedgerAccountTaxLotPolicyBook.cs
│   │   ├── LedgerAccountType.cs
│   │   ├── LedgerBalancePoint.cs
│   │   ├── LedgerBookKey.cs
│   │   ├── LedgerChartBalance.cs
│   │   ├── LedgerCurrencyExposure.cs
│   │   ├── LedgerCurrencyTranslation.cs
│   │   ├── LedgerEntry.cs
│   │   ├── LedgerFinancialReportPack.cs
│   │   ├── LedgerFinancialStatementBuilder.cs
│   │   ├── LedgerFinancialStatements.cs
│   │   ├── LedgerQuery.cs
│   │   ├── LedgerReportExportFormat.cs
│   │   ├── LedgerReportPackArtifact.cs
│   │   ├── LedgerReportPackBuilder.cs
│   │   ├── LedgerReportPackLifecycle.cs
│   │   ├── LedgerReportPackRequest.cs
│   │   ├── LedgerReportPackSignature.cs
│   │   ├── LedgerReportSchedule.cs
│   │   ├── LedgerReportScheduledExport.cs
│   │   ├── LedgerReportScheduleFrequency.cs
│   │   ├── LedgerReportSchedulePlanner.cs
│   │   ├── LedgerScheduledReportExportPackageBuilder.cs
│   │   ├── LedgerSnapshot.cs
│   │   ├── LedgerTaxLot.cs
│   │   ├── LedgerTaxLotReliefInput.cs
│   │   ├── LedgerTaxLotReliefMethod.cs
│   │   ├── LedgerTaxLotReliefProjection.cs
│   │   ├── LedgerTaxLotReliefProjector.cs
│   │   ├── LedgerTaxLotReliefSelection.cs
│   │   ├── LedgerValidationException.cs
│   │   ├── LedgerViewKind.cs
│   │   ├── LockedAccountingPeriod.cs
│   │   ├── LockedAccountingPeriodBook.cs
│   │   ├── Meridian.Ledger.csproj
│   │   ├── MultiCurrencyJournalInput.cs
│   │   ├── MultiCurrencyJournalLineInput.cs
│   │   ├── MultiCurrencyJournalLineProjection.cs
│   │   ├── MultiCurrencyJournalProjection.cs
│   │   ├── MultiCurrencyJournalProjector.cs
│   │   ├── MultiCurrencyLedgerTranslator.cs
│   │   ├── PartnershipInvestor.cs
│   │   ├── PartnershipInvestorAccountingProjector.cs
│   │   ├── PartnershipInvestorAllocation.cs
│   │   ├── PartnershipInvestorAllocationInput.cs
│   │   ├── PartnershipInvestorAllocationProjection.cs
│   │   ├── PartnershipWaterfallAllocationInput.cs
│   │   ├── PartnershipWaterfallAllocationProjection.cs
│   │   ├── PartnershipWaterfallAllocationRule.cs
│   │   ├── PartnershipWaterfallProjector.cs
│   │   ├── PartnershipWaterfallTier.cs
│   │   ├── PartnershipWaterfallTierAllocation.cs
│   │   ├── ProjectLedgerBook.cs
│   │   ├── README.md
│   │   ├── ReadOnlyCollectionHelpers.cs
│   │   ├── ShadowNavOverrideDraft.cs
│   │   ├── ShadowNavValidationFinding.cs
│   │   ├── ShadowNavValidationPolicy.cs
│   │   ├── ShadowNavValidationReport.cs
│   │   └── ShadowNavValidator.cs
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
│   │   ├── Program.cs
│   │   └── README.md
│   ├── Meridian.ProviderSdk
│   │   ├── artifacts
│   │   │   └── codex-provider-ledger-20260528164336
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
│   │   ├── README.md
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
│   │   │   └── codex-provider-ledger-20260528164336
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
│   │   ├── README.md
│   │   └── ScriptContext.cs
│   ├── Meridian.Risk
│   │   ├── artifacts
│   │   │   └── codex-provider-ledger-20260528164336
│   │   ├── Rules
│   │   │   ├── DrawdownCircuitBreaker.cs
│   │   │   ├── OrderRateThrottle.cs
│   │   │   └── PositionLimitRule.cs
│   │   ├── CompositeRiskValidator.cs
│   │   ├── IRiskRule.cs
│   │   ├── Meridian.Risk.csproj
│   │   └── README.md
│   ├── Meridian.Storage
│   │   ├── Archival
│   │   │   ├── ArchivalStorageService.cs
│   │   │   ├── AtomicFileWriter.cs
│   │   │   ├── CompressionProfileManager.cs
│   │   │   ├── SchemaVersionManager.cs
│   │   │   └── WriteAheadLog.cs
│   │   ├── artifacts
│   │   │   └── codex-provider-ledger-20260528164336
│   │   ├── Banking
│   │   │   ├── Migrations
│   │   │   │   └── 001_banking.sql
│   │   │   ├── BankingMigrationRunner.cs
│   │   │   ├── BankingStoreOptions.cs
│   │   │   ├── IBankingStore.cs
│   │   │   └── PostgresBankingStore.cs
│   │   ├── DirectLending
│   │   │   ├── Migrations
│   │   │   │   ├── 001_direct_lending.sql
│   │   │   │   ├── 002_direct_lending_projections.sql
│   │   │   │   ├── 003_direct_lending_accrual_and_event_metadata.sql
│   │   │   │   ├── 004_direct_lending_event_schema_and_snapshots.sql
│   │   │   │   ├── 005_direct_lending_operations.sql
│   │   │   │   ├── 005_direct_lending_workflows.sql
│   │   │   │   ├── 006_direct_lending_operations_workflow_audit.sql
│   │   │   │   ├── 006_direct_lending_terms_projection_extended_fields.sql
│   │   │   │   └── 007_direct_lending_command_idempotency.sql
│   │   │   ├── DirectLendingMigrationRunner.cs
│   │   │   ├── DirectLendingPersistenceBatch.cs
│   │   │   ├── IDirectLendingOperationsStore.cs
│   │   │   ├── IDirectLendingStateStore.cs
│   │   │   ├── PostgresDirectLendingStateStore.cs
│   │   │   ├── PostgresDirectLendingStateStore.Operations.cs
│   │   │   └── PostgresDirectLendingStateStore.WorkflowAudit.cs
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
│   │   │   │   ├── 001_fund_accounts.sql
│   │   │   │   └── 002_add_operational_status.sql
│   │   │   ├── FundAccountMigrationRunner.cs
│   │   │   ├── FundAccountStoreOptions.cs
│   │   │   ├── IFundAccountStore.cs
│   │   │   └── PostgresFundAccountStore.cs
│   │   ├── FundStructure
│   │   │   ├── Migrations
│   │   │   │   └── 001_fund_structure.sql
│   │   │   ├── FundStructureMigrationRunner.cs
│   │   │   ├── FundStructureStoreOptions.cs
│   │   │   ├── IFundStructureStore.cs
│   │   │   └── PostgresFundStructureStore.cs
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
│   │   │   │   ├── V_ledger_005__journal_basis_lineage.sql
│   │   │   │   ├── V_ledger_006__journal_posting_kind.sql
│   │   │   │   ├── V_ledger_007__journal_adjustment_approval_metadata.sql
│   │   │   │   ├── V_ledger_008__operations_continuity.sql
│   │   │   │   └── V_ledger_009__tax_lot_persistence.sql
│   │   │   ├── ILedgerJournalStore.cs
│   │   │   ├── LedgerBookServiceException.cs
│   │   │   ├── LedgerJournalStoreOptions.cs
│   │   │   ├── LedgerMigrationRunner.cs
│   │   │   ├── LedgerPeriodPostingGuard.cs
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
│   │   ├── MoneyMarket
│   │   │   ├── Migrations
│   │   │   │   └── 001_money_market.sql
│   │   │   ├── IMoneyMarketFundAuxStore.cs
│   │   │   ├── MoneyMarketMigrationRunner.cs
│   │   │   ├── MoneyMarketStoreOptions.cs
│   │   │   └── PostgresMoneyMarketFundStore.cs
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
│   │   │   │   ├── 015_security_master_certificate_of_deposit_projection.sql
│   │   │   │   └── 016_security_master_normalized_identifier_lookup.sql
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
│   │   ├── README.md
│   │   ├── StorageOptions.cs
│   │   ├── StorageProfiles.cs
│   │   ├── StorageSinkAttribute.cs
│   │   └── StorageSinkRegistry.cs
│   ├── Meridian.Strategies
│   │   ├── artifacts
│   │   │   └── codex-provider-ledger-20260528164336
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
│   │   │   ├── ReconciliationCaseWorkflowService.cs
│   │   │   ├── ReconciliationGovernanceService.cs
│   │   │   ├── ReconciliationProjectionService.cs
│   │   │   ├── ReconciliationRunService.cs
│   │   │   ├── ReconciliationSlaCalculator.cs
│   │   │   ├── ReconciliationSourceAdapters.cs
│   │   │   ├── SecurityMasterAccountingEventService.cs
│   │   │   ├── SecurityMasterAccountingEventSourceAdapter.cs
│   │   │   ├── ShadowBookValuationService.cs
│   │   │   ├── StrategyDesignService.cs
│   │   │   ├── StrategyEngineRegistry.cs
│   │   │   ├── StrategyEngineValidationService.cs
│   │   │   ├── StrategyLifecycleManager.cs
│   │   │   ├── StrategyRunContinuityService.cs
│   │   │   ├── StrategyRunReadService.cs
│   │   │   └── StrategyRunScopeMetadataResolver.cs
│   │   ├── Storage
│   │   │   ├── JsonlPromotionRecordStore.cs
│   │   │   ├── JsonlStrategyDesignRepository.cs
│   │   │   └── StrategyRunStore.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.Strategies.csproj
│   │   └── README.md
│   ├── Meridian.Ui
│   │   ├── dashboard
│   │   │   ├── artifacts
│   │   │   │   ├── automation
│   │   │   │   │   ├── risk-control-browser-smoke.err.log
│   │   │   │   │   ├── risk-control-browser-smoke.out.log
│   │   │   │   │   ├── risk-control-preview.err.log
│   │   │   │   │   ├── risk-control-preview.out.log
│   │   │   │   │   ├── static-server.err.log
│   │   │   │   │   ├── static-server.out.log
│   │   │   │   │   └── web-ui-trading-risk-panel-smoke.png
│   │   │   │   ├── web-ux
│   │   │   │   │   ├── workflow-continuity-dock-after-bootstrap.png
│   │   │   │   │   ├── workflow-continuity-dock-mobile.png
│   │   │   │   │   └── workflow-continuity-dock.png
│   │   │   │   ├── preview-uii-err.log
│   │   │   │   ├── preview-uii-out.log
│   │   │   │   └── preview-uii-routes.txt
│   │   │   ├── scripts
│   │   │   │   └── run-vitest-stable.mjs
│   │   │   ├── src
│   │   │   │   ├── assets
│   │   │   │   │   └── brand
│   │   │   │   │       └── meridian-mark.svg
│   │   │   │   ├── components
│   │   │   │   │   ├── data
│   │   │   │   │   │   ├── backfill-validation-dashboard.tsx
│   │   │   │   │   │   ├── symbol-universe-manager.test.tsx
│   │   │   │   │   │   └── symbol-universe-manager.tsx
│   │   │   │   │   ├── meridian
│   │   │   │   │   │   ├── command-palette.test.tsx
│   │   │   │   │   │   ├── command-palette.tsx
│   │   │   │   │   │   ├── command-palette.view-model.test.ts
│   │   │   │   │   │   ├── command-palette.view-model.ts
│   │   │   │   │   │   ├── dense-row-detail-accessibility.test.tsx
│   │   │   │   │   │   ├── dense-row-detail-accessibility.tsx
│   │   │   │   │   │   ├── historical-chart.test.tsx
│   │   │   │   │   │   ├── historical-chart.tsx
│   │   │   │   │   │   ├── historical-chart.view-model.test.ts
│   │   │   │   │   │   ├── historical-chart.view-model.ts
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
│   │   │   │   │   │   ├── strategy-formula-workbench.test.tsx
│   │   │   │   │   │   ├── strategy-formula-workbench.tsx
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
│   │   │   │   │   │   ├── provider-credential-setup.test.tsx
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
│   │   │   │   │       ├── field-support.test.tsx
│   │   │   │   │       ├── field-support.tsx
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
│   │   │   │   ├── features
│   │   │   │   │   ├── accounting
│   │   │   │   │   │   └── accountingCloseModels.ts
│   │   │   │   │   └── fund-structure
│   │   │   │   │       ├── entity-setup-wizard.test.tsx
│   │   │   │   │       └── entity-setup-wizard.tsx
│   │   │   │   ├── hooks
│   │   │   │   │   ├── use-request-lifecycle.test.ts
│   │   │   │   │   ├── use-request-lifecycle.ts
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
│   │   │   │   │   ├── api-errors.test.ts
│   │   │   │   │   ├── api-errors.ts
│   │   │   │   │   ├── api.reconciliation.test.ts
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
│   │   │   │   │   ├── family-office-screen.test.tsx
│   │   │   │   │   ├── family-office-screen.tsx
│   │   │   │   │   ├── family-office-screen.view-model.test.ts
│   │   │   │   │   ├── family-office-screen.view-model.ts
│   │   │   │   │   ├── governance-screen.test.tsx
│   │   │   │   │   ├── governance-screen.tsx
│   │   │   │   │   ├── governance-screen.view-model.test.ts
│   │   │   │   │   ├── governance-screen.view-model.ts
│   │   │   │   │   ├── live-quotes-screen.test.tsx
│   │   │   │   │   ├── live-quotes-screen.tsx
│   │   │   │   │   ├── live-quotes-screen.view-model.ts
│   │   │   │   │   ├── operations-continuity-screen.test.tsx
│   │   │   │   │   ├── operations-continuity-screen.tsx
│   │   │   │   │   ├── operations-continuity-screen.view-model.test.ts
│   │   │   │   │   ├── operations-continuity-screen.view-model.ts
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
│   │   │   │   │   ├── strategy-formula-workbench-screen.test.tsx
│   │   │   │   │   ├── strategy-formula-workbench-screen.tsx
│   │   │   │   │   ├── today-panel.view-model.test.ts
│   │   │   │   │   ├── today-panel.view-model.ts
│   │   │   │   │   ├── trading-screen.test.tsx
│   │   │   │   │   ├── trading-screen.tsx
│   │   │   │   │   ├── trading-screen.view-model.test.ts
│   │   │   │   │   ├── trading-screen.view-model.ts
│   │   │   │   │   ├── w4-acceptance-parity.test.ts
│   │   │   │   │   ├── watchlist-screen.test.tsx
│   │   │   │   │   ├── watchlist-screen.tsx
│   │   │   │   │   ├── watchlist-screen.view-model.test.ts
│   │   │   │   │   └── watchlist-screen.view-model.ts
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
│   │   │   ├── README.md
│   │   │   ├── tailwind.config.ts
│   │   │   ├── tsconfig.json
│   │   │   ├── tsconfig.node.json
│   │   │   └── vite.config.ts
│   │   ├── wwwroot
│   │   │   └── workstation
│   │   │       ├── assets
│   │   │       │   ├── activity-BDfZsj1Y.js
│   │   │       │   ├── card-Chsu2nws.js
│   │   │       │   ├── chart-column-BNiK3Z3N.js
│   │   │       │   ├── chart-line-Dvt0_u0K.js
│   │   │       │   ├── circle-alert-UMY-Egak.js
│   │   │       │   ├── circle-check-LQmB3BoW.js
│   │   │       │   ├── circle-play-ey3gyN7z.js
│   │   │       │   ├── circle-x-CrwqILZ8.js
│   │   │       │   ├── clipboard-list-D0VIkVsi.js
│   │   │       │   ├── covered-call-screen-D0U693lx.js
│   │   │       │   ├── data-operations-screen-CCdQFbhf.js
│   │   │       │   ├── data-operations-screen.view-model-BCQQIj-c.js
│   │   │       │   ├── database-BGIgNEA-.js
│   │   │       │   ├── dialog-BtTsjlOI.js
│   │   │       │   ├── evidence-workbench-screen-BsgvzBKp.js
│   │   │       │   ├── external-link-DrRBJq9F.js
│   │   │       │   ├── eye-XSrI3joe.js
│   │   │       │   ├── field-support-C_i1yi_a.js
│   │   │       │   ├── file-text-CokGdpZY.js
│   │   │       │   ├── governance-screen-Ckc07hTE.js
│   │   │       │   ├── index-C1LMoKts.js
│   │   │       │   ├── index-DWGwR4ED.css
│   │   │       │   ├── input-D4wQWQTM.js
│   │   │       │   ├── layers-B40ab27F.js
│   │   │       │   ├── list-checks-hKvpwTpY.js
│   │   │       │   ├── live-quotes-screen-b56uCC9_.js
│   │   │       │   ├── metric-card-BaXfuIdW.js
│   │   │       │   ├── network-DrDfGfwA.js
│   │   │       │   ├── operations-continuity-screen-QG7EGb-P.js
│   │   │       │   ├── operator-readiness-console-KnOLH2Ww.js
│   │   │       │   ├── play-C3mWwAkc.js
│   │   │       │   ├── plus-CsYfFM5b.js
│   │   │       │   ├── portfolio-screen-CYCWG3hx.js
│   │   │       │   ├── price-alerts-screen-Dr7ADSM-.js
│   │   │       │   ├── quant-lab-screen-D9_DXt62.js
│   │   │       │   ├── refresh-cw-CNPAoQZP.js
│   │   │       │   ├── reporting-screen-D4e0P9rt.js
│   │   │       │   ├── research-screen-Bw88HEpC.js
│   │   │       │   ├── rotate-ccw-BOUb4UQR.js
│   │   │       │   ├── save-aLeHHlg1.js
│   │   │       │   ├── select-5tY1tM8H.js
│   │   │       │   ├── settings-screen-DH-MdpAw.js
│   │   │       │   ├── shield-check-DTD6CwVH.js
│   │   │       │   ├── sigma-C5dzR_M8.js
│   │   │       │   ├── sparkles-BYGiXGQA.js
│   │   │       │   ├── strategy-designer-screen-BHD9oB5p.js
│   │   │       │   ├── strategy-formula-workbench-screen-CEy9m85x.js
│   │   │       │   ├── trading-screen-Chxq9Euv.js
│   │   │       │   ├── trash-2-2dolA973.js
│   │   │       │   ├── trending-up-rwgg01j-.js
│   │   │       │   ├── ui-kit-primitives-BjIrvzwU.js
│   │   │       │   ├── wallet-DZZxAb45.js
│   │   │       │   ├── watchlist-screen-JUIczORU.js
│   │   │       │   └── workflow-DQjlWIDT.js
│   │   │       └── index.html
│   │   └── README.md
│   ├── Meridian.Ui.Services
│   │   ├── artifacts
│   │   │   └── codex-provider-ledger-20260528164336
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
│   │   │   ├── Accounting
│   │   │   │   └── AccountingProjectionQueryService.cs
│   │   │   ├── DataQuality
│   │   │   │   ├── DataQualityApiClient.cs
│   │   │   │   ├── DataQualityModels.cs
│   │   │   │   ├── DataQualityPresentationService.cs
│   │   │   │   ├── DataQualityRefreshService.cs
│   │   │   │   ├── IDataQualityApiClient.cs
│   │   │   │   ├── IDataQualityPresentationService.cs
│   │   │   │   └── IDataQualityRefreshService.cs
│   │   │   ├── Integrations
│   │   │   │   ├── OmsIntegrationApiHandler.cs
│   │   │   │   └── OmsIntegrationServiceCollectionExtensions.cs
│   │   │   ├── Reconciliation
│   │   │   │   └── ReconciliationApiService.cs
│   │   │   ├── Reporting
│   │   │   │   └── ReportingStatusProjectionService.cs
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
│   │   ├── Meridian.Ui.Services.csproj
│   │   └── README.md
│   ├── Meridian.Ui.Shared
│   │   ├── artifacts
│   │   │   └── codex-provider-ledger-20260528164336
│   │   ├── Contracts
│   │   │   ├── Integrations
│   │   │   │   └── OmsIntegrationContracts.cs
│   │   │   ├── Reconciliation
│   │   │   │   ├── IReconciliationApiService.cs
│   │   │   │   └── StatementImportContracts.cs
│   │   │   ├── Simulation
│   │   │   │   └── ExecutionSimulationContracts.cs
│   │   │   ├── CoveredCallContracts.cs
│   │   │   ├── FamilyOfficeContracts.cs
│   │   │   └── WorkstationOperationsContracts.cs
│   │   ├── Endpoints
│   │   │   ├── Compliance
│   │   │   │   └── ComplianceEndpoints.cs
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
│   │   │   ├── CredentialEndpoints.cs
│   │   │   ├── CronEndpoints.cs
│   │   │   ├── CryptoReferenceEndpoints.cs
│   │   │   ├── DemoModeEndpoints.cs
│   │   │   ├── DepositReferenceEndpoints.cs
│   │   │   ├── DiagnosticsEndpoints.cs
│   │   │   ├── DirectLendingEndpoints.cs
│   │   │   ├── EdgarReferenceDataEndpoints.cs
│   │   │   ├── EndpointAuthorization.cs
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
│   │   │   ├── OmsIntegrationEndpoints.cs
│   │   │   ├── OptionChainEndpoints.cs
│   │   │   ├── OptionReferenceEndpoints.cs
│   │   │   ├── OptionsEndpoints.cs
│   │   │   ├── PathValidation.cs
│   │   │   ├── PromotionEndpoints.cs
│   │   │   ├── ProviderConnectionDiagnosticsProjection.cs
│   │   │   ├── ProviderConnectionEndpoints.cs
│   │   │   ├── ProviderCredentialEndpoints.cs
│   │   │   ├── ProviderEndpoints.cs
│   │   │   ├── ProviderExtendedEndpoints.cs
│   │   │   ├── ProviderRoutingEndpoints.cs
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
│   │   │   ├── WorkstationEndpoints.FamilyOffice.cs
│   │   │   ├── WorkstationEndpoints.FeatureCapabilities.cs
│   │   │   ├── WorkstationEndpoints.OperationsContinuity.cs
│   │   │   ├── WorkstationEndpoints.Reconciliation.cs
│   │   │   └── WorkstationRiskEndpoints.cs
│   │   ├── Evidence
│   │   │   ├── EvidenceContribution.cs
│   │   │   ├── EvidenceContributors.cs
│   │   │   ├── EvidenceGraphService.cs
│   │   │   ├── EvidencePacketValidationService.cs
│   │   │   ├── EvidenceSubjectResolver.cs
│   │   │   ├── EvidenceTemplateRegistry.cs
│   │   │   ├── EvidenceWorkflowServiceCollectionExtensions.cs
│   │   │   └── FileEvidenceArtifactStore.cs
│   │   ├── Serialization
│   │   │   ├── CoveredCallJsonContext.cs
│   │   │   ├── DirectLendingJsonContext.cs
│   │   │   ├── FamilyOfficeJsonContext.cs
│   │   │   └── WorkstationOperationsJsonContext.cs
│   │   ├── Services
│   │   │   ├── Acceptance
│   │   │   │   └── W4AcceptanceFilter.cs
│   │   │   ├── CoveredCall
│   │   │   │   ├── CoveredCallBacktestOptions.cs
│   │   │   │   ├── CoveredCallBacktestService.cs
│   │   │   │   ├── CoveredCallChainProviderAdapter.cs
│   │   │   │   ├── CoveredCallChainProviderFactory.cs
│   │   │   │   ├── CoveredCallRunProjection.cs
│   │   │   │   ├── ICoveredCallBacktestService.cs
│   │   │   │   └── ICoveredCallChainProviderFactory.cs
│   │   │   ├── AlpacaBrokerageConnectionService.cs
│   │   │   ├── AuditTrailExplorerService.cs
│   │   │   ├── BackfillCoordinator.cs
│   │   │   ├── BrokerageConnectionService.cs
│   │   │   ├── BrokeragePortfolioSyncService.cs
│   │   │   ├── CashOperationsOrchestratorService.cs
│   │   │   ├── CashSyncOrchestrationService.cs
│   │   │   ├── CollateralExposureService.cs
│   │   │   ├── ConfigStore.cs
│   │   │   ├── Dk1TrustGateReadinessService.cs
│   │   │   ├── FamilyOfficeReadService.cs
│   │   │   ├── FeatureCapabilitySettingsService.cs
│   │   │   ├── FundAccountCloseReadinessService.cs
│   │   │   ├── FundOperationsWorkspaceReadService.cs
│   │   │   ├── FundStructureSetupWorkflowService.cs
│   │   │   ├── GovernanceReportPackRepository.cs
│   │   │   ├── InMemoryOperatorInboxService.cs
│   │   │   ├── InvestmentAccountingTransactionLabService.cs
│   │   │   ├── LedgerAmountProvenanceService.cs
│   │   │   ├── OmsIntegrationService.cs
│   │   │   ├── OperationsContinuityReconciliationBridge.cs
│   │   │   ├── OperatorInboxPriorityScoringService.cs
│   │   │   ├── OperatorRiskRuleService.cs
│   │   │   ├── PortfolioLedgerWorkflowStatusService.cs
│   │   │   ├── ProviderConnectionLifecycleService.cs
│   │   │   ├── ProviderLedgerReconciliationService.cs
│   │   │   ├── ProviderNavigationRouteMapper.cs
│   │   │   ├── ReportingWorkflowService.cs
│   │   │   ├── ReportPackValidationService.cs
│   │   │   ├── RiskRuleRuntimeService.cs
│   │   │   ├── SecurityMasterExceptionCaseworkService.cs
│   │   │   ├── SecurityMasterSecurityReferenceLookup.cs
│   │   │   ├── SecurityMasterWorkbenchQueryService.cs
│   │   │   ├── SensitiveActionGovernance.cs
│   │   │   ├── StrategyRunComparisonService.cs
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
│   │   ├── README.md
│   │   ├── RolePermissionProfileStore.cs
│   │   ├── ScoreExplanationProjection.cs
│   │   └── UserProfileRegistry.cs
│   ├── Meridian.Wpf
│   │   ├── Assets
│   │   │   ├── Brand
│   │   │   │   ├── meridian-hero.svg
│   │   │   │   ├── meridian-mark.svg
│   │   │   │   ├── meridian-tile-256.png
│   │   │   │   ├── meridian-tile.svg
│   │   │   │   └── meridian-wordmark.svg
│   │   │   ├── Icons
│   │   │   │   ├── account-portfolio.svg
│   │   │   │   ├── admin-maintenance.svg
│   │   │   │   ├── aggregate-portfolio.svg
│   │   │   │   ├── archive-health.svg
│   │   │   │   ├── backfill.svg
│   │   │   │   ├── backtest.svg
│   │   │   │   ├── charting.svg
│   │   │   │   ├── collection-sessions.svg
│   │   │   │   ├── dashboard.svg
│   │   │   │   ├── data-browser.svg
│   │   │   │   ├── data-calendar.svg
│   │   │   │   ├── data-export.svg
│   │   │   │   ├── data-operations.svg
│   │   │   │   ├── data-quality.svg
│   │   │   │   ├── data-sampling.svg
│   │   │   │   ├── data-sources.svg
│   │   │   │   ├── diagnostics.svg
│   │   │   │   ├── event-replay.svg
│   │   │   │   ├── governance.svg
│   │   │   │   ├── help.svg
│   │   │   │   ├── index-subscription.svg
│   │   │   │   ├── keyboard-shortcuts.svg
│   │   │   │   ├── lean-integration.svg
│   │   │   │   ├── live-data.svg
│   │   │   │   ├── order-book.svg
│   │   │   │   ├── portfolio-import.svg
│   │   │   │   ├── provider-health.svg
│   │   │   │   ├── README.md
│   │   │   │   ├── research.svg
│   │   │   │   ├── retention-assurance.svg
│   │   │   │   ├── run-detail.svg
│   │   │   │   ├── run-ledger.svg
│   │   │   │   ├── run-mat.svg
│   │   │   │   ├── run-portfolio.svg
│   │   │   │   ├── schedule-manager.svg
│   │   │   │   ├── security-master.svg
│   │   │   │   ├── service-manager.svg
│   │   │   │   ├── settings.svg
│   │   │   │   ├── storage-optimization.svg
│   │   │   │   ├── storage.svg
│   │   │   │   ├── strategy-runs.svg
│   │   │   │   ├── symbol-storage.svg
│   │   │   │   ├── symbols.svg
│   │   │   │   ├── system-health.svg
│   │   │   │   ├── trading-hours.svg
│   │   │   │   ├── trading.svg
│   │   │   │   └── watchlist.svg
│   │   │   └── app.ico
│   │   ├── Behaviors
│   │   │   ├── AvalonEditNotebookBehavior.cs
│   │   │   ├── ParameterTemplateSelector.cs
│   │   │   └── PlotRenderBehavior.cs
│   │   ├── Contracts
│   │   │   ├── IConnectionService.cs
│   │   │   ├── INavigationService.cs
│   │   │   └── IPageActivationLifetime.cs
│   │   ├── Controls
│   │   │   └── AutomationLeafBorder.cs
│   │   ├── Converters
│   │   │   ├── BoolToStringConverter.cs
│   │   │   ├── BoolToVisibilityConverter.cs
│   │   │   ├── ConsoleEntryKindToBrushConverter.cs
│   │   │   ├── CountToVisibilityConverter.cs
│   │   │   ├── IntToVisibilityConverter.cs
│   │   │   ├── InvertBoolConverter.cs
│   │   │   ├── NullToCollapsedConverter.cs
│   │   │   ├── StringToBoolConverter.cs
│   │   │   └── StringToVisibilityConverter.cs
│   │   ├── Copy
│   │   │   └── WorkspaceCopyCatalog.cs
│   │   ├── Features
│   │   │   ├── Accounting
│   │   │   │   └── AccountingFeatureModule.cs
│   │   │   ├── Data
│   │   │   │   ├── Shell
│   │   │   │   │   ├── DataWorkspaceShellPage.xaml
│   │   │   │   │   ├── DataWorkspaceShellPage.xaml.cs
│   │   │   │   │   ├── DataWorkspaceShellPresentationService.cs
│   │   │   │   │   ├── DataWorkspaceShellSnapshotService.cs
│   │   │   │   │   └── DataWorkspaceShellViewModel.cs
│   │   │   │   └── DataFeatureModule.cs
│   │   │   ├── Portfolio
│   │   │   │   ├── Shell
│   │   │   │   │   ├── PortfolioWorkspaceShellPage.xaml
│   │   │   │   │   └── PortfolioWorkspaceShellPage.xaml.cs
│   │   │   │   └── PortfolioFeatureModule.cs
│   │   │   ├── Reporting
│   │   │   │   ├── Shell
│   │   │   │   │   ├── ReportingWorkspaceShellPage.xaml
│   │   │   │   │   └── ReportingWorkspaceShellPage.xaml.cs
│   │   │   │   └── ReportingFeatureModule.cs
│   │   │   ├── Settings
│   │   │   │   ├── Shell
│   │   │   │   │   ├── SettingsWorkspaceShellModels.cs
│   │   │   │   │   ├── SettingsWorkspaceShellPage.xaml
│   │   │   │   │   ├── SettingsWorkspaceShellPage.xaml.cs
│   │   │   │   │   ├── SettingsWorkspaceShellPresentationService.cs
│   │   │   │   │   ├── SettingsWorkspaceShellSnapshotService.cs
│   │   │   │   │   └── SettingsWorkspaceShellViewModel.cs
│   │   │   │   └── SettingsFeatureModule.cs
│   │   │   ├── Strategy
│   │   │   │   └── StrategyFeatureModule.cs
│   │   │   ├── Trading
│   │   │   │   ├── Shell
│   │   │   │   │   ├── TradingWorkspaceShellPage.xaml
│   │   │   │   │   └── TradingWorkspaceShellPage.xaml.cs
│   │   │   │   └── TradingFeatureModule.cs
│   │   │   ├── DesktopFeatureModuleRegistry.cs
│   │   │   ├── FeatureCapabilityGateService.cs
│   │   │   ├── IDesktopFeatureModule.cs
│   │   │   └── IFeatureCapabilityGate.cs
│   │   ├── Models
│   │   │   ├── ActionEntry.cs
│   │   │   ├── ActivityLogModels.cs
│   │   │   ├── AlignmentModels.cs
│   │   │   ├── AppConfig.cs
│   │   │   ├── BackfillModels.cs
│   │   │   ├── BlotterModels.cs
│   │   │   ├── DashboardModels.cs
│   │   │   ├── DataQualityModels.cs
│   │   │   ├── FundLedgerDimensionView.cs
│   │   │   ├── FundProfileModels.cs
│   │   │   ├── FundReconciliationWorkbenchModels.cs
│   │   │   ├── LeanModels.cs
│   │   │   ├── LiveDataModels.cs
│   │   │   ├── NotificationModels.cs
│   │   │   ├── OrderBookModels.cs
│   │   │   ├── PaneDropAction.cs
│   │   │   ├── PaneDropEventArgs.cs
│   │   │   ├── PaneLayout.cs
│   │   │   ├── ProviderHealthModels.cs
│   │   │   ├── QuantScriptExecutionHistoryModels.cs
│   │   │   ├── QuantScriptModels.cs
│   │   │   ├── ResearchWorkspaceShellPresentationModels.cs
│   │   │   ├── SecurityMasterPresentationModels.cs
│   │   │   ├── SettingsModels.cs
│   │   │   ├── ShellNavigationCatalog.cs
│   │   │   ├── ShellNavigationCatalog.Governance.cs
│   │   │   ├── ShellNavigationCatalog.Layouts.cs
│   │   │   ├── ShellNavigationCatalog.Research.cs
│   │   │   ├── ShellNavigationCatalog.Workspaces.cs
│   │   │   ├── ShellNavigationModels.cs
│   │   │   ├── ShellNavigationTextStyleGuide.cs
│   │   │   ├── StatementReconciliationWorkbenchModels.cs
│   │   │   ├── StorageDisplayModels.cs
│   │   │   ├── SymbolsModels.cs
│   │   │   ├── TradingWorkspaceShellPresentationModels.cs
│   │   │   ├── WatchlistModels.cs
│   │   │   ├── WorkspaceDefinition.cs
│   │   │   ├── WorkspaceQueueRegionState.cs
│   │   │   ├── WorkspaceRegistry.cs
│   │   │   ├── WorkspaceShellChromeModels.cs
│   │   │   ├── WorkspaceShellLayoutModels.cs
│   │   │   ├── WorkspaceShellModels.cs
│   │   │   ├── WorkspaceStateTokenModels.cs
│   │   │   └── WorkstationOperatingContextModels.cs
│   │   ├── Services
│   │   │   ├── AgentLoopService.cs
│   │   │   ├── ApiStatusService.cs
│   │   │   ├── AppConfigDefaults.cs
│   │   │   ├── ArchiveHealthService.cs
│   │   │   ├── BackendServiceManager.cs
│   │   │   ├── BackgroundTaskSchedulerService.cs
│   │   │   ├── BacktestDataAvailabilityService.cs
│   │   │   ├── BacktestService.cs
│   │   │   ├── BrushRegistry.cs
│   │   │   ├── CashFinancingReadService.cs
│   │   │   ├── ClipboardWatcherService.cs
│   │   │   ├── ConfigService.cs
│   │   │   ├── ConnectionService.cs
│   │   │   ├── ContextMenuService.cs
│   │   │   ├── CredentialService.cs
│   │   │   ├── DataOperationsWorkspacePresentationBuilder.cs
│   │   │   ├── DesktopLaunchArguments.cs
│   │   │   ├── DropImportService.cs
│   │   │   ├── ExportFormat.cs
│   │   │   ├── ExportPresetService.cs
│   │   │   ├── FirstRunService.cs
│   │   │   ├── FloatingPageService.cs
│   │   │   ├── FormValidationService.cs
│   │   │   ├── FundAccountReadService.cs
│   │   │   ├── FundContextService.cs
│   │   │   ├── FundLedgerReadService.cs
│   │   │   ├── FundProfileKeyTranslator.cs
│   │   │   ├── FundReconciliationWorkbenchService.cs
│   │   │   ├── GlobalHotkeyService.cs
│   │   │   ├── GovernanceWorkspacePresentationService.cs
│   │   │   ├── ICommandContextProvider.cs
│   │   │   ├── IFundProfileCatalog.cs
│   │   │   ├── InfoBarService.cs
│   │   │   ├── IQuantScriptLayoutService.cs
│   │   │   ├── IViewModelViewResolver.cs
│   │   │   ├── IWorkspaceScopedService.cs
│   │   │   ├── IWorkspaceShellStateProvider.cs
│   │   │   ├── JumpListService.cs
│   │   │   ├── KeyboardShortcutService.cs
│   │   │   ├── LoggingService.cs
│   │   │   ├── MessagingService.cs
│   │   │   ├── NavigationService.cs
│   │   │   ├── NotificationService.cs
│   │   │   ├── OfflineTrackingPersistenceService.cs
│   │   │   ├── PendingOperationsQueueService.cs
│   │   │   ├── QuantScriptExecutionHistoryService.cs
│   │   │   ├── QuantScriptLayoutService.cs
│   │   │   ├── QuantScriptTemplateCatalogService.cs
│   │   │   ├── ReconciliationReadService.cs
│   │   │   ├── ResearchWorkspaceShellPresentationService.cs
│   │   │   ├── RetentionAssuranceService.cs
│   │   │   ├── RunMatService.cs
│   │   │   ├── SchemaService.cs
│   │   │   ├── SecurityMasterOperatorWorkflowClient.cs
│   │   │   ├── SecurityMasterRuntimeStatusService.cs
│   │   │   ├── SetupWizardStateService.cs
│   │   │   ├── SingleInstanceService.cs
│   │   │   ├── StatementReconciliationWorkbenchService.cs
│   │   │   ├── StatusService.cs
│   │   │   ├── StorageService.cs
│   │   │   ├── StrategyRunWorkspaceService.cs
│   │   │   ├── SystemTrayService.cs
│   │   │   ├── TaskbarProgressService.cs
│   │   │   ├── TearOffPanelService.cs
│   │   │   ├── ThemeService.cs
│   │   │   ├── TickerStripService.cs
│   │   │   ├── ToastNotificationService.cs
│   │   │   ├── TooltipService.cs
│   │   │   ├── TradingWorkspaceShellPresentationService.cs
│   │   │   ├── TypeForwards.cs
│   │   │   ├── ViewModelViewResolver.cs
│   │   │   ├── WatchlistService.cs
│   │   │   ├── WindowStartupRecovery.cs
│   │   │   ├── WorkspaceService.cs
│   │   │   ├── WorkspaceShellContextService.cs
│   │   │   ├── WorkspaceShellSlotContributionService.cs
│   │   │   ├── WorkspaceShellStateProviders.cs
│   │   │   ├── WorkspaceStateTokenStore.cs
│   │   │   ├── WorkstationOperatingContextService.cs
│   │   │   ├── WorkstationOperatorInboxApiClient.cs
│   │   │   ├── WorkstationReconciliationApiClient.cs
│   │   │   ├── WorkstationResearchBriefingService.cs
│   │   │   ├── WorkstationSecurityMasterApiClient.cs
│   │   │   └── WpfShellServiceCollectionExtensions.cs
│   │   ├── Shell
│   │   │   ├── Models
│   │   │   │   ├── PaneContentState.cs
│   │   │   │   ├── PaneDropResult.cs
│   │   │   │   └── ShellRoute.cs
│   │   │   ├── Refresh
│   │   │   │   └── ShellRefreshCoordinator.cs
│   │   │   ├── Root
│   │   │   │   ├── DesktopLaunchRouter.cs
│   │   │   │   ├── DesktopShellCoordinator.cs
│   │   │   │   └── FileDropRouter.cs
│   │   │   ├── Services
│   │   │   │   ├── IPageContentFactory.cs
│   │   │   │   ├── IShellNavigationCoordinator.cs
│   │   │   │   ├── IShellPageRegistry.cs
│   │   │   │   ├── IShellRouteRegistry.cs
│   │   │   │   ├── PageContentFactory.cs
│   │   │   │   ├── ShellNavigationCoordinator.cs
│   │   │   │   ├── ShellPageRegistryBuilder.cs
│   │   │   │   └── ShellRouteRegistry.cs
│   │   │   ├── Session
│   │   │   │   ├── DesktopShellSessionService.cs
│   │   │   │   ├── DesktopWindowState.cs
│   │   │   │   ├── IWindowStateStore.cs
│   │   │   │   └── WindowStateStore.cs
│   │   │   └── ViewModels
│   │   │       ├── CommandPaletteViewModel.cs
│   │   │       ├── OperatorInboxViewModel.cs
│   │   │       ├── PaneHostViewModel.cs
│   │   │       └── WorkflowSummaryStripViewModel.cs
│   │   ├── Styles
│   │   │   ├── Animations.xaml
│   │   │   ├── AppStyles.xaml
│   │   │   ├── BrandResources.xaml
│   │   │   ├── IconResources.xaml
│   │   │   ├── ThemeControls.xaml
│   │   │   ├── ThemeSurfaces.xaml
│   │   │   ├── ThemeTokens.xaml
│   │   │   └── ThemeTypography.xaml
│   │   ├── Templates
│   │   │   └── QuantScript
│   │   │       ├── catalog.json
│   │   │       ├── hello-spy.csx
│   │   │       ├── indicator-sma.csx
│   │   │       └── single-symbol-backtest.csx
│   │   ├── ViewModels
│   │   │   ├── Accounting
│   │   │   │   └── AccountingCloseViewModel.cs
│   │   │   ├── AccountPortfolioViewModel.cs
│   │   │   ├── ActivityLogViewModel.cs
│   │   │   ├── AddProviderWizardViewModel.cs
│   │   │   ├── AdminMaintenanceViewModel.cs
│   │   │   ├── AdvancedAnalyticsViewModel.cs
│   │   │   ├── AgentViewModel.cs
│   │   │   ├── AggregatePortfolioViewModel.cs
│   │   │   ├── AnalysisExportViewModel.cs
│   │   │   ├── AnalysisExportWizardViewModel.cs
│   │   │   ├── BackfillViewModel.cs
│   │   │   ├── BackfillViewModel.Sections.cs
│   │   │   ├── BacktestViewModel.cs
│   │   │   ├── BatchBacktestViewModel.cs
│   │   │   ├── BindableBase.cs
│   │   │   ├── CarryTradeBacktestViewModel.cs
│   │   │   ├── CashFlowViewModel.cs
│   │   │   ├── ChartingPageViewModel.cs
│   │   │   ├── ClusterStatusViewModel.cs
│   │   │   ├── CollectionSessionViewModel.cs
│   │   │   ├── CredentialManagementViewModel.cs
│   │   │   ├── DashboardViewModel.cs
│   │   │   ├── DataBrowserViewModel.cs
│   │   │   ├── DataCalendarViewModel.cs
│   │   │   ├── DataExportViewModel.cs
│   │   │   ├── DataQualityViewModel.cs
│   │   │   ├── DataSamplingViewModel.cs
│   │   │   ├── DataSourcesViewModel.cs
│   │   │   ├── DiagnosticsPageViewModel.cs
│   │   │   ├── DirectLendingViewModel.cs
│   │   │   ├── EventReplayViewModel.cs
│   │   │   ├── ExportPresetsViewModel.cs
│   │   │   ├── FundAccountProviderPanelModels.cs
│   │   │   ├── FundAccountsViewModel.cs
│   │   │   ├── FundLedgerViewModel.cs
│   │   │   ├── FundLedgerViewModel.Reconciliation.cs
│   │   │   ├── FundLedgerViewModel.Sections.cs
│   │   │   ├── FundLedgerViewModel.StatementReconciliation.cs
│   │   │   ├── FundProfileSelectionViewModel.cs
│   │   │   ├── FundStructureSetupViewModel.cs
│   │   │   ├── IndexSubscriptionViewModel.cs
│   │   │   ├── IPageActionBarProvider.cs
│   │   │   ├── LeanIntegrationViewModel.cs
│   │   │   ├── LiveDataViewerViewModel.cs
│   │   │   ├── MainPageViewModel.cs
│   │   │   ├── MainPageViewModel.Sections.cs
│   │   │   ├── MainWindowViewModel.cs
│   │   │   ├── MessagingHubViewModel.cs
│   │   │   ├── NotificationCenterViewModel.cs
│   │   │   ├── OptionsViewModel.cs
│   │   │   ├── OrderBookHeatmapViewModel.cs
│   │   │   ├── OrderBookViewModel.cs
│   │   │   ├── PackageManagerViewModel.cs
│   │   │   ├── PluginManagementViewModel.cs
│   │   │   ├── PortfolioImportViewModel.cs
│   │   │   ├── PositionBlotterViewModel.cs
│   │   │   ├── ProviderHealthViewModel.cs
│   │   │   ├── ProviderHealthViewModel.Sections.cs
│   │   │   ├── ProviderPageModels.cs
│   │   │   ├── ProviderViewModel.cs
│   │   │   ├── QualityArchiveViewModel.cs
│   │   │   ├── QuantScriptViewModel.cs
│   │   │   ├── QuantScriptViewModel.Sections.cs
│   │   │   ├── QuoteFloatViewModel.cs
│   │   │   ├── ResearchWorkspaceShellViewModel.cs
│   │   │   ├── RetentionAssuranceViewModel.cs
│   │   │   ├── RunMatViewModel.cs
│   │   │   ├── RunRiskViewModel.cs
│   │   │   ├── ScatterAnalysisViewModel.cs
│   │   │   ├── ScheduleManagerViewModel.cs
│   │   │   ├── SecurityConflictLaneModels.cs
│   │   │   ├── SecurityMasterDeactivateViewModel.cs
│   │   │   ├── SecurityMasterEditViewModel.cs
│   │   │   ├── SecurityMasterViewModel.cs
│   │   │   ├── SecurityMasterViewModel.Sections.cs
│   │   │   ├── SecurityMasterViewModel.Services.cs
│   │   │   ├── SecurityMasterViewModel.TextHelpers.cs
│   │   │   ├── ServiceManagerViewModel.cs
│   │   │   ├── SettingsViewModel.cs
│   │   │   ├── SetupWizardViewModel.cs
│   │   │   ├── SplitPaneViewModel.cs
│   │   │   ├── StatusBarViewModel.cs
│   │   │   ├── StorageOptimizationViewModel.cs
│   │   │   ├── StorageViewModel.cs
│   │   │   ├── StrategyRunBrowserViewModel.cs
│   │   │   ├── StrategyRunDetailViewModel.cs
│   │   │   ├── StrategyRunLedgerViewModel.cs
│   │   │   ├── StrategyRunPortfolioViewModel.cs
│   │   │   ├── SymbolMappingViewModel.cs
│   │   │   ├── SymbolsPageViewModel.cs
│   │   │   ├── SymbolsPageViewModel.Sections.cs
│   │   │   ├── SymbolStorageViewModel.cs
│   │   │   ├── SystemHealthViewModel.cs
│   │   │   ├── TickerStripViewModel.cs
│   │   │   ├── TimeSeriesAlignmentViewModel.cs
│   │   │   ├── TradingHoursViewModel.cs
│   │   │   ├── TradingWorkspaceShellViewModel.cs
│   │   │   ├── WatchlistViewModel.cs
│   │   │   ├── WelcomePageViewModel.cs
│   │   │   ├── WorkflowLibraryViewModel.cs
│   │   │   ├── WorkspacePageViewModel.cs
│   │   │   └── WorkspaceShellViewModelBase.cs
│   │   ├── Views
│   │   │   ├── AccountPortfolioPage.xaml
│   │   │   ├── AccountPortfolioPage.xaml.cs
│   │   │   ├── ActivityLogPage.xaml
│   │   │   ├── ActivityLogPage.xaml.cs
│   │   │   ├── AddProviderWizardPage.xaml
│   │   │   ├── AddProviderWizardPage.xaml.cs
│   │   │   ├── AdminMaintenancePage.xaml
│   │   │   ├── AdminMaintenancePage.xaml.cs
│   │   │   ├── AdvancedAnalyticsPage.xaml
│   │   │   ├── AdvancedAnalyticsPage.xaml.cs
│   │   │   ├── AgentPage.xaml
│   │   │   ├── AgentPage.xaml.cs
│   │   │   ├── AggregatePortfolioPage.xaml
│   │   │   ├── AggregatePortfolioPage.xaml.cs
│   │   │   ├── AnalysisExportPage.xaml
│   │   │   ├── AnalysisExportPage.xaml.cs
│   │   │   ├── AnalysisExportWizardPage.xaml
│   │   │   ├── AnalysisExportWizardPage.xaml.cs
│   │   │   ├── ApiKeyDialog.xaml
│   │   │   ├── ApiKeyDialog.xaml.cs
│   │   │   ├── ArchiveHealthPage.xaml
│   │   │   ├── ArchiveHealthPage.xaml.cs
│   │   │   ├── BackfillPage.xaml
│   │   │   ├── BackfillPage.xaml.cs
│   │   │   ├── BacktestPage.xaml
│   │   │   ├── BacktestPage.xaml.cs
│   │   │   ├── BatchBacktestPage.xaml
│   │   │   ├── BatchBacktestPage.xaml.cs
│   │   │   ├── CarryTradeBacktestPage.xaml
│   │   │   ├── ChartingPage.xaml
│   │   │   ├── ChartingPage.xaml.cs
│   │   │   ├── ClusterStatusPage.xaml
│   │   │   ├── ClusterStatusPage.xaml.cs
│   │   │   ├── CollectionSessionPage.xaml
│   │   │   ├── CollectionSessionPage.xaml.cs
│   │   │   ├── CommandPaletteWindow.xaml
│   │   │   ├── CommandPaletteWindow.xaml.cs
│   │   │   ├── CreateWatchlistDialog.cs
│   │   │   ├── CredentialManagementPage.xaml
│   │   │   ├── CredentialManagementPage.xaml.cs
│   │   │   ├── DashboardPage.xaml
│   │   │   ├── DashboardPage.xaml.cs
│   │   │   ├── DataBrowserPage.xaml
│   │   │   ├── DataBrowserPage.xaml.cs
│   │   │   ├── DataCalendarPage.xaml
│   │   │   ├── DataCalendarPage.xaml.cs
│   │   │   ├── DataExportPage.xaml
│   │   │   ├── DataExportPage.xaml.cs
│   │   │   ├── DataQualityPage.xaml
│   │   │   ├── DataQualityPage.xaml.cs
│   │   │   ├── DataSamplingPage.xaml
│   │   │   ├── DataSamplingPage.xaml.cs
│   │   │   ├── DataSourcesPage.xaml
│   │   │   ├── DataSourcesPage.xaml.cs
│   │   │   ├── DiagnosticsPage.xaml
│   │   │   ├── DiagnosticsPage.xaml.cs
│   │   │   ├── DirectLendingPage.xaml
│   │   │   ├── DirectLendingPage.xaml.cs
│   │   │   ├── EditScheduledJobDialog.xaml
│   │   │   ├── EditScheduledJobDialog.xaml.cs
│   │   │   ├── EditWatchlistDialog.cs
│   │   │   ├── EnvironmentDesignerPage.xaml
│   │   │   ├── EnvironmentDesignerPage.xaml.cs
│   │   │   ├── EventReplayPage.xaml
│   │   │   ├── EventReplayPage.xaml.cs
│   │   │   ├── ExportPresetsPage.xaml
│   │   │   ├── ExportPresetsPage.xaml.cs
│   │   │   ├── FloatingPageWindow.xaml
│   │   │   ├── FloatingPageWindow.xaml.cs
│   │   │   ├── FundAccountsPage.xaml
│   │   │   ├── FundAccountsPage.xaml.cs
│   │   │   ├── FundLedgerPage.xaml
│   │   │   ├── FundLedgerPage.xaml.cs
│   │   │   ├── FundProfileSelectionPage.xaml
│   │   │   ├── FundProfileSelectionPage.xaml.cs
│   │   │   ├── FundStructureSetupPage.xaml
│   │   │   ├── FundStructureSetupPage.xaml.cs
│   │   │   ├── GovernanceWorkspaceShellPage.xaml
│   │   │   ├── GovernanceWorkspaceShellPage.xaml.cs
│   │   │   ├── HelpPage.xaml
│   │   │   ├── HelpPage.xaml.cs
│   │   │   ├── IndexSubscriptionPage.xaml
│   │   │   ├── IndexSubscriptionPage.xaml.cs
│   │   │   ├── InstitutionalCommandPaletteControl.xaml
│   │   │   ├── InstitutionalCommandPaletteControl.xaml.cs
│   │   │   ├── InstitutionalShellFrameControl.cs
│   │   │   ├── KeyboardShortcutsPage.xaml
│   │   │   ├── KeyboardShortcutsPage.xaml.cs
│   │   │   ├── LeanIntegrationPage.xaml
│   │   │   ├── LeanIntegrationPage.xaml.cs
│   │   │   ├── LiveDataViewerPage.xaml
│   │   │   ├── LiveDataViewerPage.xaml.cs
│   │   │   ├── MainPage.SplitPane.cs
│   │   │   ├── MainPage.xaml
│   │   │   ├── MainPage.xaml.cs
│   │   │   ├── MeridianDockingManager.xaml
│   │   │   ├── MeridianDockingManager.xaml.cs
│   │   │   ├── MessagingHubPage.xaml
│   │   │   ├── MessagingHubPage.xaml.cs
│   │   │   ├── NotificationCenterPage.xaml
│   │   │   ├── NotificationCenterPage.xaml.cs
│   │   │   ├── OptionsPage.xaml
│   │   │   ├── OptionsPage.xaml.cs
│   │   │   ├── OrderBookHeatmapControl.xaml
│   │   │   ├── OrderBookHeatmapControl.xaml.cs
│   │   │   ├── OrderBookPage.xaml
│   │   │   ├── OrderBookPage.xaml.cs
│   │   │   ├── PackageManagerPage.xaml
│   │   │   ├── PackageManagerPage.xaml.cs
│   │   │   ├── PageActionBarControl.xaml
│   │   │   ├── PageActionBarControl.xaml.cs
│   │   │   ├── Pages.cs
│   │   │   ├── PluginManagementPage.xaml
│   │   │   ├── PluginManagementPage.xaml.cs
│   │   │   ├── PortfolioImportPage.xaml
│   │   │   ├── PortfolioImportPage.xaml.cs
│   │   │   ├── PositionBlotterPage.xaml
│   │   │   ├── PositionBlotterPage.xaml.cs
│   │   │   ├── ProviderHealthPage.xaml
│   │   │   ├── ProviderHealthPage.xaml.cs
│   │   │   ├── ProviderPage.xaml
│   │   │   ├── ProviderPage.xaml.cs
│   │   │   ├── QualityArchivePage.xaml
│   │   │   ├── QualityArchivePage.xaml.cs
│   │   │   ├── QuantScriptPage.xaml
│   │   │   ├── QuantScriptPage.xaml.cs
│   │   │   ├── QuoteFloatWindow.xaml
│   │   │   ├── QuoteFloatWindow.xaml.cs
│   │   │   ├── ResearchWorkspaceShellPage.xaml
│   │   │   ├── ResearchWorkspaceShellPage.xaml.cs
│   │   │   ├── RetentionAssurancePage.xaml
│   │   │   ├── RetentionAssurancePage.xaml.cs
│   │   │   ├── RunCashFlowPage.xaml
│   │   │   ├── RunCashFlowPage.xaml.cs
│   │   │   ├── RunDetailPage.xaml
│   │   │   ├── RunDetailPage.xaml.cs
│   │   │   ├── RunLedgerPage.xaml
│   │   │   ├── RunLedgerPage.xaml.cs
│   │   │   ├── RunMatPage.xaml
│   │   │   ├── RunMatPage.xaml.cs
│   │   │   ├── RunPortfolioPage.xaml
│   │   │   ├── RunPortfolioPage.xaml.cs
│   │   │   ├── RunRiskPage.xaml
│   │   │   ├── RunRiskPage.xaml.cs
│   │   │   ├── SaveWatchlistDialog.xaml
│   │   │   ├── SaveWatchlistDialog.xaml.cs
│   │   │   ├── ScatterAnalysisPage.xaml
│   │   │   ├── ScatterAnalysisPage.xaml.cs
│   │   │   ├── ScheduleManagerPage.xaml
│   │   │   ├── ScheduleManagerPage.xaml.cs
│   │   │   ├── SecurityMasterPage.xaml
│   │   │   ├── SecurityMasterPage.xaml.cs
│   │   │   ├── ServiceManagerPage.xaml
│   │   │   ├── ServiceManagerPage.xaml.cs
│   │   │   ├── SettingsPage.xaml
│   │   │   ├── SettingsPage.xaml.cs
│   │   │   ├── SetupWizardPage.xaml
│   │   │   ├── SetupWizardPage.xaml.cs
│   │   │   ├── ShellMastheadControl.xaml
│   │   │   ├── ShellMastheadControl.xaml.cs
│   │   │   ├── ShellRailControl.xaml
│   │   │   ├── ShellRailControl.xaml.cs
│   │   │   ├── SplitPaneHostControl.xaml
│   │   │   ├── SplitPaneHostControl.xaml.cs
│   │   │   ├── StatusBarControl.xaml
│   │   │   ├── StatusBarControl.xaml.cs
│   │   │   ├── StorageOptimizationPage.xaml
│   │   │   ├── StorageOptimizationPage.xaml.cs
│   │   │   ├── StoragePage.xaml
│   │   │   ├── StoragePage.xaml.cs
│   │   │   ├── StrategyRunsPage.xaml
│   │   │   ├── StrategyRunsPage.xaml.cs
│   │   │   ├── SymbolMappingPage.xaml
│   │   │   ├── SymbolMappingPage.xaml.cs
│   │   │   ├── SymbolsPage.xaml
│   │   │   ├── SymbolsPage.xaml.cs
│   │   │   ├── SymbolStoragePage.xaml
│   │   │   ├── SymbolStoragePage.xaml.cs
│   │   │   ├── SystemHealthPage.xaml
│   │   │   ├── SystemHealthPage.xaml.cs
│   │   │   ├── TickerStripWindow.xaml
│   │   │   ├── TickerStripWindow.xaml.cs
│   │   │   ├── TimeSeriesAlignmentPage.xaml
│   │   │   ├── TimeSeriesAlignmentPage.xaml.cs
│   │   │   ├── TradingHoursPage.xaml
│   │   │   ├── TradingHoursPage.xaml.cs
│   │   │   ├── WatchlistPage.xaml
│   │   │   ├── WatchlistPage.xaml.cs
│   │   │   ├── WelcomePage.xaml
│   │   │   ├── WelcomePage.xaml.cs
│   │   │   ├── WorkflowLibraryPage.xaml
│   │   │   ├── WorkflowLibraryPage.xaml.cs
│   │   │   ├── WorkspaceCapabilityHomePage.cs
│   │   │   ├── WorkspaceCommandBarControl.xaml
│   │   │   ├── WorkspaceCommandBarControl.xaml.cs
│   │   │   ├── WorkspaceCommandSurfaceControl.xaml
│   │   │   ├── WorkspaceCommandSurfaceControl.xaml.cs
│   │   │   ├── WorkspaceDecisionQueueControl.xaml
│   │   │   ├── WorkspaceDecisionQueueControl.xaml.cs
│   │   │   ├── WorkspaceDeepPageHostPage.xaml
│   │   │   ├── WorkspaceDeepPageHostPage.xaml.cs
│   │   │   ├── WorkspaceDialogChromeControl.xaml
│   │   │   ├── WorkspaceDialogChromeControl.xaml.cs
│   │   │   ├── WorkspaceEvidenceStripControl.xaml
│   │   │   ├── WorkspaceEvidenceStripControl.xaml.cs
│   │   │   ├── WorkspaceInspectorHostControl.xaml
│   │   │   ├── WorkspaceInspectorHostControl.xaml.cs
│   │   │   ├── WorkspacePage.xaml
│   │   │   ├── WorkspacePage.xaml.cs
│   │   │   ├── WorkspaceShellChromeState.cs
│   │   │   ├── WorkspaceShellContextStripControl.xaml
│   │   │   ├── WorkspaceShellContextStripControl.xaml.cs
│   │   │   ├── WorkspaceShellFallbackContentFactory.cs
│   │   │   └── WorkspaceShellPageBase.cs
│   │   ├── Workstation
│   │   │   ├── Commands
│   │   │   │   ├── AsyncCommandViewModel.cs
│   │   │   │   ├── CommandViewModel.cs
│   │   │   │   └── WorkspaceCommandAdapters.cs
│   │   │   ├── Composition
│   │   │   │   └── WorkspaceCompositionDefinition.cs
│   │   │   ├── Controls
│   │   │   │   ├── ActivityLogGridControl.xaml
│   │   │   │   ├── ActivityLogGridControl.xaml.cs
│   │   │   │   ├── DenseDataGridControl.xaml
│   │   │   │   ├── DenseDataGridControl.xaml.cs
│   │   │   │   ├── DiagnosticsChecklistControl.xaml
│   │   │   │   ├── DiagnosticsChecklistControl.xaml.cs
│   │   │   │   ├── HealthBadgeControl.xaml
│   │   │   │   ├── HealthBadgeControl.xaml.cs
│   │   │   │   ├── InspectorPanelControl.xaml
│   │   │   │   ├── InspectorPanelControl.xaml.cs
│   │   │   │   ├── MetricTileControl.xaml
│   │   │   │   ├── MetricTileControl.xaml.cs
│   │   │   │   ├── RoutingMatrixControl.xaml
│   │   │   │   ├── RoutingMatrixControl.xaml.cs
│   │   │   │   ├── WorkstationCommandBarControl.xaml
│   │   │   │   ├── WorkstationCommandBarControl.xaml.cs
│   │   │   │   ├── WorkstationStatePanelControl.xaml
│   │   │   │   └── WorkstationStatePanelControl.xaml.cs
│   │   │   ├── Diagnostics
│   │   │   │   └── WorkstationDiagnosticsControls.cs
│   │   │   ├── Layout
│   │   │   │   └── WorkstationLayoutControls.cs
│   │   │   ├── Models
│   │   │   │   └── WorkstationPresentationModels.cs
│   │   │   ├── Primitives
│   │   │   │   └── WorkstationPrimitiveControls.cs
│   │   │   ├── State
│   │   │   │   └── WorkstationRegionState.cs
│   │   │   ├── Tables
│   │   │   │   └── WorkstationTableControls.cs
│   │   │   └── ViewModels
│   │   │       ├── Base
│   │   │       │   ├── AuditTimelineViewModel.cs
│   │   │       │   ├── DataQualityViewModelBase.cs
│   │   │       │   ├── DetailWorkspaceViewModelBase.cs
│   │   │       │   ├── DiagnosticsViewModel.cs
│   │   │       │   ├── ErrorStateViewModel.cs
│   │   │       │   ├── FilterableTableViewModel.cs
│   │   │       │   ├── InspectorViewModel.cs
│   │   │       │   ├── LoadingStateViewModel.cs
│   │   │       │   ├── RoutingMatrixViewModel.cs
│   │   │       │   ├── TableViewModel.cs
│   │   │       │   ├── ValidationStateViewModel.cs
│   │   │       │   └── WorkspaceViewModelBase.cs
│   │   │       └── WorkspaceViewModelBase.cs
│   │   ├── App.xaml
│   │   ├── App.xaml.cs
│   │   ├── AssemblyInfo.cs
│   │   ├── GlobalUsings.cs
│   │   ├── MainWindow.xaml
│   │   ├── MainWindow.xaml.cs
│   │   ├── Meridian.Wpf.csproj
│   │   ├── Meridian.Wpf_1fdztfjd_wpftmp.csproj
│   │   ├── Meridian.Wpf_1iibccgd_wpftmp.csproj
│   │   ├── Meridian.Wpf_4qqooq4f_wpftmp.csproj
│   │   ├── Meridian.Wpf_bqftum4u_wpftmp.csproj
│   │   ├── Meridian.Wpf_dgkhv3uj_wpftmp.csproj
│   │   ├── Meridian.Wpf_loq1dgav_wpftmp.csproj
│   │   ├── Meridian.Wpf_orjnur5i_wpftmp.csproj
│   │   ├── Meridian.Wpf_tsxd4nsk_wpftmp.csproj
│   │   ├── Meridian.Wpf_uelpy5ah_wpftmp.csproj
│   │   ├── Meridian.Wpf_xx5ueail_wpftmp.csproj
│   │   ├── Meridian.Wpf_zrcduyo2_wpftmp.csproj
│   │   ├── Package.appxmanifest
│   │   └── README.md
│   └── README.md
├── tests
│   ├── Meridian.Backtesting.Tests
│   │   ├── AdvancedCarryDecisionEngineTests.cs
│   │   ├── BacktestEngineIntegrationTests.cs
│   │   ├── BacktestMetricsEngineTests.cs
│   │   ├── BacktestPreflightServiceTests.cs
│   │   ├── BacktestRequestConfigTests.cs
│   │   ├── BatchBacktestServiceTests.cs
│   │   ├── BracketOrderTests.cs
│   │   ├── CanonicalBacktestResultNormalizerTests.cs
│   │   ├── CorporateActionAdjustmentServiceTests.cs
│   │   ├── FillModelExpansionTests.cs
│   │   ├── FillModelTests.cs
│   │   ├── GlobalUsings.cs
│   │   ├── LedgerQueryTests.cs
│   │   ├── LotLevelTrackingTests.cs
│   │   ├── MarketImpactFillModelTests.cs
│   │   ├── Meridian.Backtesting.Tests.csproj
│   │   ├── MeridianNativeBacktestStudioEngineTests.cs
│   │   ├── MultiSymbolMergeEnumeratorTests.cs
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
│   │   ├── OperationsContinuityRulesTests.fs
│   │   ├── PeriodManagementTests.fs
│   │   ├── PipelineTests.fs
│   │   ├── PromotionPolicyTests.fs
│   │   ├── ReconciliationCaseWorkflowTests.fs
│   │   ├── ReportPackValidationRulesTests.fs
│   │   ├── RiskPolicyTests.fs
│   │   ├── SensitiveActionPolicyTests.fs
│   │   ├── SettlementInstructionCommandsTests.fs
│   │   ├── TradingReadinessRulesTests.fs
│   │   ├── TradingTransitionTests.fs
│   │   └── ValidationTests.fs
│   ├── Meridian.FundStructure.Tests
│   │   ├── EnvironmentDesignerServiceTests.cs
│   │   ├── FundStructurePolicyServiceTests.cs
│   │   ├── FundStructureSetupWorkflowServiceTests.cs
│   │   ├── GovernanceSharedDataAccessServiceTests.cs
│   │   ├── InMemoryFundStructureServiceTests.cs
│   │   ├── LedgerGroupingRulesTests.cs
│   │   └── Meridian.FundStructure.Tests.csproj
│   ├── Meridian.McpServer.Tests
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
│   │   │   │   ├── SimulationCommandsTests.cs
│   │   │   │   ├── StatementImportCommandsTests.cs
│   │   │   │   ├── SymbolCommandsTests.cs
│   │   │   │   └── ValidateConfigCommandTests.cs
│   │   │   ├── Composition
│   │   │   │   ├── Startup
│   │   │   │   │   └── SharedStartupBootstrapperTests.cs
│   │   │   │   ├── DiagnosticsFeatureRegistrationTests.cs
│   │   │   │   ├── DirectLendingStartupTests.cs
│   │   │   │   ├── PipelineFeatureRegistrationTests.cs
│   │   │   │   ├── ProductionServiceRegistrationPolicyTests.cs
│   │   │   │   ├── ProviderCapabilityContractRegistrationTests.cs
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
│   │   │   │   ├── AccrualLedgerServiceTests.cs
│   │   │   │   ├── DailyAccrualWorkerTests.cs
│   │   │   │   ├── DirectLendingOutboxDispatcherTests.cs
│   │   │   │   └── PostgresDirectLendingCommandServiceTests.cs
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
│   │   │   │   ├── ProviderRegistryDeterministicSelectionTests.cs
│   │   │   │   ├── SpscRingBufferTests.cs
│   │   │   │   └── WalEventPipelineTests.cs
│   │   │   ├── ProviderRouting
│   │   │   │   ├── BestOfBreedProviderSelectorTests.cs
│   │   │   │   ├── KernelObservabilityServiceTests.cs
│   │   │   │   ├── ProviderRoutingServiceTests.cs
│   │   │   │   └── ProviderTrustScoringServiceTests.cs
│   │   │   ├── Reconciliation
│   │   │   │   ├── CanonicalReconciliationMatchingEngineTests.cs
│   │   │   │   ├── StatementMatchingEngineTests.cs
│   │   │   │   ├── StatementReconciliationOrchestratorTests.cs
│   │   │   │   ├── StatementRepositoryTests.cs
│   │   │   │   └── StatementValidationServiceTests.cs
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
│   │   │   │   ├── DiagnosticBundleServiceTests.cs
│   │   │   │   ├── ErrorCodeMappingTests.cs
│   │   │   │   ├── EventCanonicalizerTests.cs
│   │   │   │   ├── ExecutionSimulationOrchestratorTests.cs
│   │   │   │   ├── FundOperationsWorkspaceReadServiceTests.cs
│   │   │   │   ├── GracefulShutdownTests.cs
│   │   │   │   ├── HistoricalDataQueryServiceBarsTests.cs
│   │   │   │   ├── HistoricalDataQueryServiceTests.cs
│   │   │   │   ├── OperationalSchedulerTests.cs
│   │   │   │   ├── OptionsChainServiceTests.cs
│   │   │   │   ├── PreflightCheckerTests.cs
│   │   │   │   ├── ReportGenerationServiceTests.cs
│   │   │   │   ├── RuntimeDiagnosticRedactorTests.cs
│   │   │   │   ├── TradingCalendarTests.cs
│   │   │   │   └── VenueMicMapperTests.cs
│   │   │   ├── Ui
│   │   │   │   └── ConfigStoreTests.cs
│   │   │   ├── Wizard
│   │   │   │   └── WizardConfigurationStepTests.cs
│   │   │   ├── DirectLendingServiceTests.cs
│   │   │   ├── GovernanceExceptionServiceTests.cs
│   │   │   ├── OperationsContinuityPostgresRoundTripTests.cs
│   │   │   ├── OperationsContinuityWorkflowServiceTests.cs
│   │   │   ├── ReconciliationGovernanceServiceTests.cs
│   │   │   └── ReconciliationRunServiceTests.cs
│   │   ├── Architecture
│   │   │   └── LayerBoundaryTests.cs
│   │   ├── artifacts
│   │   │   └── codex-provider-ledger-20260528164336
│   │   ├── CertificatesOfDeposit
│   │   │   └── CertificateOfDepositProjectionServiceTests.cs
│   │   ├── Commodities
│   │   │   └── CommodityProjectionServiceTests.cs
│   │   ├── Compliance
│   │   │   └── CompliancePolicyEngineTests.cs
│   │   ├── Contracts
│   │   │   ├── Api
│   │   │   │   └── UiApiClientTests.cs
│   │   │   ├── FundStructureContractsJsonContextTests.cs
│   │   │   └── LedgerReconciliationContractCompatibilityTests.cs
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
│   │   │   ├── HostedBrokerageGatewayRegistrationTests.cs
│   │   │   ├── MultiAccountPaperTradingPortfolioTests.cs
│   │   │   ├── OrderManagementSystemGovernanceTests.cs
│   │   │   ├── OrderManagementSystemTests.cs
│   │   │   ├── PaperExecutionGatewayLotSizeTests.cs
│   │   │   ├── PaperSessionPersistenceServiceTests.cs
│   │   │   ├── PaperTradingGatewayTests.cs
│   │   │   ├── PaperTradingPortfolioLotSelectionTests.cs
│   │   │   ├── PaperTradingPortfolioLotSnapshotTests.cs
│   │   │   ├── PaperTradingPortfolioTests.cs
│   │   │   ├── PositionLotSelectorTests.cs
│   │   │   └── TradierExecutionReconciliationTests.cs
│   │   ├── FixedIncome
│   │   │   └── BondProjectionServiceTests.cs
│   │   ├── Futures
│   │   │   └── FutureProjectionServiceTests.cs
│   │   ├── FxSpot
│   │   │   └── FxSpotProjectionServiceTests.cs
│   │   ├── Infrastructure
│   │   │   ├── Adapters
│   │   │   │   └── TradierCanonicalMappersTests.cs
│   │   │   ├── DataSources
│   │   │   │   └── CredentialConfigTests.cs
│   │   │   ├── Etl
│   │   │   │   └── CsvPartnerFileParserTests.cs
│   │   │   ├── Http
│   │   │   │   └── HttpClientConfigurationTests.cs
│   │   │   ├── Providers
│   │   │   │   ├── Fixtures
│   │   │   │   │   ├── Alpaca
│   │   │   │   │   │   └── alpaca-aapl-partial-fill-ledger-break.json
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
│   │   │   │   │   │   ├── ib_order_trailing_stop_sell_gtc.json
│   │   │   │   │   │   └── ibkr-aapl-partial-fill-ledger-break.json
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
│   │   │   │   ├── PolygonCoherenceTests.cs
│   │   │   │   ├── PolygonCorporateActionFetcherTests.cs
│   │   │   │   ├── PolygonMarketDataClientTests.cs
│   │   │   │   ├── PolygonMessageParsingTests.cs
│   │   │   │   ├── PolygonProviderContractTests.cs
│   │   │   │   ├── PolygonRecordedSessionReplayTests.cs
│   │   │   │   ├── PolygonSubscriptionTests.cs
│   │   │   │   ├── ProviderBehaviorBuilderTests.cs
│   │   │   │   ├── ProviderDataQualityValidatorTests.cs
│   │   │   │   ├── ProviderFactoryCredentialContextTests.cs
│   │   │   │   ├── ProviderResilienceTests.cs
│   │   │   │   ├── ProviderTemplateFactoryCredentialTests.cs
│   │   │   │   ├── RobinhoodBrokerageGatewayTests.cs
│   │   │   │   ├── RobinhoodHistoricalDataProviderTests.cs
│   │   │   │   ├── RobinhoodMarketDataClientTests.cs
│   │   │   │   ├── RobinhoodReadOnlyBrokerageSyncAdapterTests.cs
│   │   │   │   ├── RobinhoodSymbolSearchProviderTests.cs
│   │   │   │   ├── StreamingFailoverServiceTests.cs
│   │   │   │   ├── SyntheticHistoricalProviderContractTests.cs
│   │   │   │   ├── SyntheticMarketDataProviderTests.cs
│   │   │   │   ├── SyntheticOptionsChainProviderTests.cs
│   │   │   │   ├── SyntheticProviderTestHarness.cs
│   │   │   │   ├── TradeStationPayloadMappersTests.cs
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
│   │   │   │   ├── SensitiveActionGovernanceTests.cs
│   │   │   │   ├── StatusEndpointTests.cs
│   │   │   │   ├── StorageEndpointTests.cs
│   │   │   │   ├── SymbolEndpointTests.cs
│   │   │   │   └── UiEndpointsJsonOptionsTests.cs
│   │   │   ├── ConfigurableTickerDataCollectionTests.cs
│   │   │   ├── ConnectionRetryIntegrationTests.cs
│   │   │   ├── EndpointStubDetectionTests.cs
│   │   │   ├── FixtureProviderTests.cs
│   │   │   ├── GracefulShutdownIntegrationTests.cs
│   │   │   ├── ProviderGoldenPathScenarioGenerator.cs
│   │   │   ├── ProviderGoldenPathTransactionLedgerReconciliationTests.cs
│   │   │   └── YahooFinancePcgPreferredIntegrationTests.cs
│   │   ├── Ledger
│   │   │   └── LedgerIntegrationTests.cs
│   │   ├── MoneyMarketFunds
│   │   │   └── MoneyMarketFundProjectionServiceTests.cs
│   │   ├── Options
│   │   │   └── OptionProjectionServiceTests.cs
│   │   ├── Performance
│   │   │   └── AllocationBudgetIntegrationTests.cs
│   │   ├── Providers
│   │   │   └── ProviderCapabilityDescriptorCatalogTests.cs
│   │   ├── ProviderSdk
│   │   │   ├── AttributeCredentialResolverTests.cs
│   │   │   ├── CredentialValidatorTests.cs
│   │   │   ├── DataSourceAttributeTests.cs
│   │   │   ├── DataSourceRegistryTests.cs
│   │   │   ├── ExceptionTypeTests.cs
│   │   │   └── ProviderModuleLoaderTests.cs
│   │   ├── Reconciliation
│   │   │   ├── Fixtures
│   │   │   │   ├── statement-clean-reconciles.csv
│   │   │   │   ├── statement-invalid-blockers.csv
│   │   │   │   └── statement-unresolved-breaks.csv
│   │   │   ├── BrokerCustodianMatchingPipelineTests.cs
│   │   │   ├── ReconciliationCaseServiceTests.cs
│   │   │   ├── ReconciliationContractsTests.cs
│   │   │   ├── StatementBreakClassifierTests.cs
│   │   │   ├── StatementFixtureScenarioTests.cs
│   │   │   ├── StatementImportAndMatchingTests.cs
│   │   │   └── StatementMatchingToleranceTests.cs
│   │   ├── Reporting
│   │   │   └── ReportingOrchestrationServiceTests.cs
│   │   ├── Risk
│   │   │   ├── CompositeRiskValidatorTests.cs
│   │   │   ├── DrawdownCircuitBreakerTests.cs
│   │   │   ├── OrderRateThrottleTests.cs
│   │   │   ├── PositionLimitRuleTests.cs
│   │   │   └── RiskIntegrationTests.cs
│   │   ├── SecurityMaster
│   │   │   ├── SecurityAssetClassCatalogTests.cs
│   │   │   ├── SecurityAssetProfileGovernanceServiceTests.cs
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
│   │   │   ├── SecurityMasterQueryServiceProfileSearchTests.cs
│   │   │   ├── SecurityMasterRebuildOrchestratorTests.cs
│   │   │   ├── SecurityMasterReferenceLookupTests.cs
│   │   │   ├── SecurityMasterServiceSnapshotTests.cs
│   │   │   ├── SecurityMasterSnapshotStoreTests.cs
│   │   │   └── SecurityValidationServiceTests.cs
│   │   ├── Serialization
│   │   │   └── HighPerformanceJsonTests.cs
│   │   ├── Services
│   │   │   └── CashSyncOrchestrationServiceTests.cs
│   │   ├── Storage
│   │   │   ├── AnalysisExportServiceTests.cs
│   │   │   ├── AtomicFileWriterTests.cs
│   │   │   ├── AuditChainServiceTests.cs
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
│   │   │   ├── LedgerDatabaseFactAttribute.cs
│   │   │   ├── LedgerJournalStoreTests.cs
│   │   │   ├── LedgerPostgresTestDatabase.cs
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
│   │   │   ├── ReconciliationBreakQueueRepositoryTests.cs
│   │   │   ├── ReconciliationCaseWorkflowServiceTests.cs
│   │   │   ├── ReconciliationProjectionServiceTests.cs
│   │   │   ├── SecurityMasterAccountingEventServiceTests.cs
│   │   │   ├── ShadowBookValuationServiceTests.cs
│   │   │   ├── StrategyDesignRepositoryTests.cs
│   │   │   ├── StrategyDesignServiceTests.cs
│   │   │   ├── StrategyEngineValidationServiceTests.cs
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
│   │   │   ├── ActivityCapturingPublisher.cs
│   │   │   ├── ActivityTestListenerFactory.cs
│   │   │   ├── MarketScenarioBuilder.cs
│   │   │   ├── PolygonStubClient.cs
│   │   │   ├── StubHttpMessageHandler.cs
│   │   │   └── TestMarketEventPublisher.cs
│   │   ├── Testing
│   │   │   └── TestArtifactDirectory.cs
│   │   ├── Treasury
│   │   │   ├── MmfFamilyNormalizationTests.cs
│   │   │   ├── MmfLiquidityServiceTests.cs
│   │   │   ├── MmfRebuildTests.cs
│   │   │   └── MoneyMarketFundServiceTests.cs
│   │   ├── Ui
│   │   │   ├── AlpacaBrokerageConnectionServiceTests.cs
│   │   │   ├── AlpacaCredentialEnvironmentCollection.cs
│   │   │   ├── AuditTrailExplorerServiceTests.cs
│   │   │   ├── BondReferenceEndpointsTests.cs
│   │   │   ├── BrokerageConnectionEndpointsTests.cs
│   │   │   ├── BrokeragePortfolioSyncServiceTests.cs
│   │   │   ├── CashOperationsOrchestratorServiceTests.cs
│   │   │   ├── CollateralExposureServiceTests.cs
│   │   │   ├── CredentialCompatibilityEndpointsTests.cs
│   │   │   ├── DiagnosticsEndpointsTests.cs
│   │   │   ├── DirectLendingEndpointsTests.cs
│   │   │   ├── EdgarReferenceDataEndpointsTests.cs
│   │   │   ├── EvidenceWorkflowFabricTests.cs
│   │   │   ├── ExecutionGovernanceEndpointsTests.cs
│   │   │   ├── ExecutionRouteContractParityTests.cs
│   │   │   ├── ExecutionWriteEndpointsTests.cs
│   │   │   ├── ExportEndpointsTests.cs
│   │   │   ├── FamilyOfficeContractTests.cs
│   │   │   ├── FamilyOfficeReadServiceTests.cs
│   │   │   ├── FundOpsCloseLaneScenarioTests.cs
│   │   │   ├── InvestmentAccountingTransactionLabServiceTests.cs
│   │   │   ├── LedgerAmountProvenanceServiceTests.cs
│   │   │   ├── OmsIntegrationServiceTests.cs
│   │   │   ├── OperationsContinuityReconciliationBridgeTests.cs
│   │   │   ├── OperatorApprovalFlowScenarioTests.cs
│   │   │   ├── OptionReferenceEndpointsRoundtripTests.cs
│   │   │   ├── PortfolioLedgerWorkflowStatusServiceTests.cs
│   │   │   ├── ProductionStartupPolicySmokeTests.cs
│   │   │   ├── PromotionDecisionChainScenarioTests.cs
│   │   │   ├── ProviderConnectionDiagnosticsProjectionTests.cs
│   │   │   ├── ProviderConnectionEndpointsTests.cs
│   │   │   ├── ProviderLedgerReconciliationServiceTests.cs
│   │   │   ├── ProviderRoutingEndpointsTests.cs
│   │   │   ├── ReconciliationApiServiceTests.cs
│   │   │   ├── ReferenceDataEndpointAuthorizationTests.cs
│   │   │   ├── ReportPackValidationServiceTests.cs
│   │   │   ├── ReportPackWorkflowServiceTests.cs
│   │   │   ├── RiskEndpointsTests.cs
│   │   │   ├── SecurityMasterConvertibleEquityEndpointsTests.cs
│   │   │   ├── SecurityMasterExceptionCaseworkServiceTests.cs
│   │   │   ├── SecurityMasterIngestStatusEndpointsTests.cs
│   │   │   ├── SecurityMasterInstrumentPassportTests.cs
│   │   │   ├── SecurityMasterPreferredEquityEndpointsTests.cs
│   │   │   ├── SecurityMasterValidationEndpointsTests.cs
│   │   │   ├── StrategyDesignerWorkstationEndpointsTests.cs
│   │   │   ├── TradingOperatorReadinessServiceTests.cs
│   │   │   ├── Wave2OperatorInboxAcceptanceTests.cs
│   │   │   ├── Wave2PaperTradingCockpitAcceptanceTests.cs
│   │   │   ├── WorkflowLibraryEndpointTests.cs
│   │   │   ├── WorkstationCollateralExposureEndpointsTests.cs
│   │   │   ├── WorkstationContractSnapshotTests.cs
│   │   │   ├── WorkstationEndpointContractCompatibilityTests.cs
│   │   │   ├── WorkstationEndpointsTests.cs
│   │   │   ├── WorkstationEndpointsTests.Infrastructure.cs
│   │   │   ├── WorkstationEndpointsTests.Wave4.cs
│   │   │   ├── WorkstationFamilyOfficeEndpointsTests.cs
│   │   │   ├── WorkstationServiceCollectionExtensionsTests.cs
│   │   │   └── WorkstationStatementReconciliationEndpointTests.cs
│   │   ├── AccountingCloseServicesTests.cs
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
│   │   │   ├── OAuthRefreshServiceTests.cs
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
│   │   │   ├── Data
│   │   │   │   └── Shell
│   │   │   │       └── DataWorkspaceShellViewModelTests.cs
│   │   │   ├── Settings
│   │   │   │   └── Shell
│   │   │   │       └── SettingsWorkspaceShellViewModelTests.cs
│   │   │   └── FeatureCapabilityGateTests.cs
│   │   ├── Models
│   │   │   ├── ShellNavigationCatalogTests.cs
│   │   │   └── WorkspaceShellChromeContributionTests.cs
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
│   │   │   ├── OperationsContinuityDtoContractTests.cs
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
│   │   │   ├── ViewModelViewResolverTests.cs
│   │   │   ├── WatchlistServiceTests.cs
│   │   │   ├── WorkspaceServiceTests.cs
│   │   │   ├── WorkspaceShellContextServiceTests.cs
│   │   │   ├── WorkspaceStateTokenTests.cs
│   │   │   ├── WorkstationOperatingContextServiceTests.cs
│   │   │   └── WorkstationWorkflowSummaryServiceTests.cs
│   │   ├── Shell
│   │   │   ├── PageContentFactoryTests.cs
│   │   │   ├── PaneHostViewModelTests.cs
│   │   │   ├── ShellNavigationCoordinatorTests.cs
│   │   │   └── ShellRouteRegistryTests.cs
│   │   ├── Support
│   │   │   ├── AppServiceTestHost.cs
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
│   │   │   ├── BacktestViewModelTests.cs
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
│   │   │   ├── FundStructureSetupViewModelTests.cs
│   │   │   ├── LiveDataViewerViewModelTests.cs
│   │   │   ├── MainShellViewModelTests.cs
│   │   │   ├── MessagingHubViewModelTests.cs
│   │   │   ├── NotificationCenterViewModelTests.cs
│   │   │   ├── OrderBookViewModelTests.cs
│   │   │   ├── PageActivationLifetimeContractTests.cs
│   │   │   ├── PortfolioImportViewModelTests.cs
│   │   │   ├── PositionBlotterViewModelTests.cs
│   │   │   ├── ProviderHealthViewModelTests.cs
│   │   │   ├── QuantScriptViewModelTests.cs
│   │   │   ├── ResearchWorkspaceShellViewModelTests.cs
│   │   │   ├── RetentionAssuranceViewModelTests.cs
│   │   │   ├── RunMatViewModelTests.cs
│   │   │   ├── RunRiskViewModelTests.cs
│   │   │   ├── ScheduleManagerViewModelTests.cs
│   │   │   ├── SecurityMasterEditViewModelTests.cs
│   │   │   ├── SecurityMasterViewModelTests.cs
│   │   │   ├── ServiceManagerViewModelTests.cs
│   │   │   ├── SetupWizardViewModelTests.cs
│   │   │   ├── ShellPresentationViewModelTests.cs
│   │   │   ├── StatusBarViewModelTests.cs
│   │   │   ├── StorageOptimizationViewModelTests.cs
│   │   │   ├── StorageViewModelTests.cs
│   │   │   ├── StrategyRunBrowserViewModelTests.cs
│   │   │   ├── StrategyRunLedgerViewModelTests.cs
│   │   │   ├── StrategyRunPortfolioViewModelTests.cs
│   │   │   ├── SymbolMappingViewModelTests.cs
│   │   │   ├── SymbolsPageViewModelTests.cs
│   │   │   ├── SymbolStorageViewModelTests.cs
│   │   │   ├── SystemHealthViewModelTests.cs
│   │   │   ├── TimeSeriesAlignmentViewModelTests.cs
│   │   │   ├── TradingHoursViewModelTests.cs
│   │   │   ├── TradingWorkspaceShellViewModelTests.cs
│   │   │   ├── WatchlistViewModelTests.cs
│   │   │   ├── Wave2OperatorInboxAcceptanceTests.cs
│   │   │   ├── WelcomePageViewModelTests.cs
│   │   │   ├── WorkflowLibraryViewModelTests.cs
│   │   │   ├── WorkspaceCockpitShellViewModelTests.cs
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
│   │   │   ├── WorkspaceCommandBarControlTests.cs
│   │   │   ├── WorkspaceDecisionQueueControlTests.cs
│   │   │   ├── WorkspaceDeepPageChromeTests.cs
│   │   │   ├── WorkspaceDialogChromeControlTests.cs
│   │   │   ├── WorkspaceInspectorHostControlTests.cs
│   │   │   ├── WorkspaceQueueToneStylesTests.cs
│   │   │   ├── WorkspaceShellContextStripControlTests.cs
│   │   │   ├── WorkspaceShellHomeTemplateTests.cs
│   │   │   ├── WorkspaceShellPageSmokeTests.cs
│   │   │   ├── WorkspaceShellPrimitiveControlsTests.cs
│   │   │   ├── WorkstationPageSmokeTests.cs
│   │   │   └── WorkstationPrimitiveControlsTests.cs
│   │   ├── Workstation
│   │   │   └── WorkstationPresentationModelsTests.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.Wpf.Tests.csproj
│   │   └── TestAssemblyConfiguration.cs
│   ├── scripts
│   │   ├── __pycache__
│   │   │   ├── test_buildctl_artifact_retention.cpython-311.pyc
│   │   │   ├── test_central_package_versions.cpython-311.pyc
│   │   │   ├── test_check_codex_skills.cpython-39.pyc
│   │   │   ├── test_compare_run_contract.cpython-311.pyc
│   │   │   ├── test_desktop_screen_blueprint_checklist.cpython-311.pyc
│   │   │   ├── test_generate_dk1_pilot_parity_packet.cpython-311.pyc
│   │   │   ├── test_golden_path_validation_workflow.cpython-311.pyc
│   │   │   ├── test_golden_path_validation_workflow.cpython-39.pyc
│   │   │   ├── test_prepare_dk1_operator_signoff.cpython-311.pyc
│   │   │   ├── test_refresh_screenshots_workflow.cpython-311.pyc
│   │   │   ├── test_roadmap_source_docs.cpython-39.pyc
│   │   │   ├── test_screenshot_workflow_plan.cpython-311.pyc
│   │   │   ├── test_shared_build_retention.cpython-311.pyc
│   │   │   ├── test_shared_checkpoint.cpython-311.pyc
│   │   │   ├── test_validate_screenshot_captures.cpython-311.pyc
│   │   │   ├── test_validate_screenshot_captures.cpython-311.pyc.1299866721792
│   │   │   ├── test_web_workstation_installer.cpython-311.pyc
│   │   │   ├── test_web_workstation_installer.cpython-311.pyc.1394999696896
│   │   │   ├── test_web_workstation_installer.cpython-311.pyc.1947816238592
│   │   │   ├── test_web_workstation_installer.cpython-311.pyc.2117375106560
│   │   │   ├── test_web_workstation_installer.cpython-311.pyc.2159732858368
│   │   │   ├── test_web_workstation_installer.cpython-311.pyc.2213497937408
│   │   │   ├── test_web_workstation_installer.cpython-311.pyc.2263618297344
│   │   │   ├── test_web_workstation_installer.cpython-311.pyc.2292943429120
│   │   │   └── test_web_workstation_installer.cpython-311.pyc.2556824395264
│   │   ├── fixtures
│   │   │   └── roadmap
│   │   │       ├── ambiguous-input.yaml
│   │   │       └── unordered-mixed-unicode.yaml
│   │   ├── setup-verification.sh
│   │   ├── test_archive_code_tombstones.py
│   │   ├── test_artifact_retention_module.py
│   │   ├── test_buildctl_artifact_retention.py
│   │   ├── test_central_package_versions.py
│   │   ├── test_check_codex_skills.py
│   │   ├── test_check_contract_compatibility_gate.py
│   │   ├── test_check_program_state_consistency.py
│   │   ├── test_check_status_delivery_claims.py
│   │   ├── test_check_workflow_docs_parity.py
│   │   ├── test_cleanup_generated_script.py
│   │   ├── test_code_quality_workflow.py
│   │   ├── test_compare_run_contract.py
│   │   ├── test_dashboard_package_lock.py
│   │   ├── test_desktop_screen_blueprint_checklist.py
│   │   ├── test_doc_render_determinism.py
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
│   │   ├── test_roadmap_source_docs.py
│   │   ├── test_run_provider_validation_evidence_bundle.py
│   │   ├── test_screenshot_diff_report.py
│   │   ├── test_screenshot_workflow_plan.py
│   │   ├── test_setup_dotnet_cache_action.py
│   │   ├── test_shared_build_retention.py
│   │   ├── test_shared_checkpoint.py
│   │   ├── test_summarize_desktop_workflow_bundle.py
│   │   ├── test_validate_screenshot_captures.py
│   │   ├── test_validate_source_readmes.py
│   │   ├── test_validate_tooling_metadata.py
│   │   ├── test_validate_workstation_cockpit_acceptance_matrix.py
│   │   ├── test_web_workstation_installer.py
│   │   ├── test_wpf_msix_install_guidance.py
│   │   └── test_wpf_msix_manifest.py
│   ├── coverlet.runsettings
│   ├── Directory.Build.props
│   ├── setup-script-tests.md
│   └── xunit.runner.json
├── tools
│   ├── codex
│   │   ├── _codex-scan-lib.ps1
│   │   ├── architecture-scan.ps1
│   │   ├── component-inventory.ps1
│   │   ├── desktop-workspace-generator.ps1
│   │   ├── mvvm-compliance-check.ps1
│   │   ├── refactor-plan-generator.ps1
│   │   ├── resource-review.ps1
│   │   ├── run-codex-quality-suite.ps1
│   │   ├── shared-pattern-suggest.ps1
│   │   └── test-gap-scan.ps1
│   ├── roadmap
│   │   ├── fixtures
│   │   │   ├── invalid-enums.json
│   │   │   ├── invalid-missing-required.json
│   │   │   ├── invalid-unexpected-field.json
│   │   │   └── valid-enums.json
│   │   ├── __init__.py
│   │   ├── enforce_phase_scope.py
│   │   ├── README.md
│   │   ├── render_roadmap_docs.py
│   │   └── validate_roadmap.py
│   ├── source_docs
│   │   ├── fixtures
│   │   │   ├── readme_contract
│   │   │   │   ├── missing_front_matter
│   │   │   │   │   └── README.md
│   │   │   │   ├── missing_heading
│   │   │   │   │   └── README.md
│   │   │   │   ├── missing_markers
│   │   │   │   │   └── README.md
│   │   │   │   ├── valid
│   │   │   │   │   └── README.md
│   │   │   │   ├── coverage-invalid.yml
│   │   │   │   ├── coverage-valid.yml
│   │   │   │   └── modules.yml
│   │   │   ├── invalid-enums.json
│   │   │   ├── invalid-missing-required.json
│   │   │   ├── invalid-unexpected-field.json
│   │   │   └── valid-enums.json
│   │   ├── __init__.py
│   │   ├── check_source_determinism.py
│   │   ├── README.md
│   │   ├── render_source_docs.py
│   │   ├── requirements-render.txt
│   │   └── validate_source_readmes.py
│   ├── __init__.py
│   └── README.md
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
├── .rgignore
├── .vsconfig
├── AGENTS.md
├── artifacts-w4-isolated.log
├── artifacts-w4-test.log
├── artifacts-w4-wpf.log
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
├── README.md
└── README.md.bak
```
