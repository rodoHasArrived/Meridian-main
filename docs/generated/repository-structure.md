# Repository Structure

> Auto-generated on 1970-01-01 00:00:00 UTC. Do not edit manually.

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
│       ├── meridian-cleanup
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
│       ├── meridian-docs
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
│       │   │   │   ├── cross-surface-review.manifest.json
│       │   │   │   ├── roadmap-review.manifest.json
│       │   │   │   ├── screen-review.manifest.json
│       │   │   │   ├── ship-readiness.manifest.json
│       │   │   │   └── workflow-walkthrough.manifest.json
│       │   │   ├── eval-result.schema.json
│       │   │   ├── review-manifest.schema.json
│       │   │   └── review-result.schema.json
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
│       │   │   ├── rubric.md
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
│   │   ├── meridian-brainstorm.md
│   │   ├── meridian-browser-workstation.md
│   │   ├── meridian-cleanup.md
│   │   ├── meridian-code-review.md
│   │   ├── meridian-docs.md
│   │   ├── meridian-implementation-assurance.md
│   │   ├── meridian-provider-builder.md
│   │   ├── meridian-repo-navigation.md
│   │   ├── meridian-roadmap-strategist.md
│   │   ├── meridian-simulated-user-panel.md
│   │   └── meridian-test-writer.md
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
│   │   ├── meridian-cleanup
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
│   │   ├── meridian-docs
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
│   │   │   │   │   ├── cross-surface-review.manifest.json
│   │   │   │   │   ├── roadmap-review.manifest.json
│   │   │   │   │   ├── screen-review.manifest.json
│   │   │   │   │   ├── ship-readiness.manifest.json
│   │   │   │   │   └── workflow-walkthrough.manifest.json
│   │   │   │   ├── eval-result.schema.json
│   │   │   │   ├── review-manifest.schema.json
│   │   │   │   └── review-result.schema.json
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
│   │   │   │   ├── rubric.md
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
│   ├── launch.json
│   ├── settings.json
│   └── settings.local.json
├── .codex
│   ├── agents
│   │   ├── dense-data-grid-inspector-panel.toml
│   │   ├── desktop-test-generation.toml
│   │   ├── diagnostics-audit-timeline.toml
│   │   ├── meridian-accounting-posting-controls.toml
│   │   ├── meridian-archive-organizer.toml
│   │   ├── meridian-blueprint.toml
│   │   ├── meridian-brainstorm.toml
│   │   ├── meridian-browser-workstation.toml
│   │   ├── meridian-cleanup.toml
│   │   ├── meridian-code-architecture.toml
│   │   ├── meridian-code-review.toml
│   │   ├── meridian-codex-skill-builder.toml
│   │   ├── meridian-contract-governance.toml
│   │   ├── meridian-docs.toml
│   │   ├── meridian-event-accounting-architecture.toml
│   │   ├── meridian-feasibility-sketcher.toml
│   │   ├── meridian-idea-critic.toml
│   │   ├── meridian-idea-to-blueprint-router.toml
│   │   ├── meridian-implementation-assurance.toml
│   │   ├── meridian-ledger-projection-replay-review.toml
│   │   ├── meridian-opportunity-scout.toml
│   │   ├── meridian-persona-signal-scout.toml
│   │   ├── meridian-provider-builder.toml
│   │   ├── meridian-repo-navigation.toml
│   │   ├── meridian-roadmap-strategist.toml
│   │   ├── meridian-simulated-user-panel.toml
│   │   ├── meridian-test-writer.toml
│   │   ├── meridian-user-testing-auditor.toml
│   │   ├── meridian-user-testing-cfo.toml
│   │   ├── meridian-user-testing-cio.toml
│   │   ├── meridian-user-testing-compliance-officer.toml
│   │   ├── meridian-user-testing-controller.toml
│   │   ├── meridian-user-testing-data-operations-analyst.toml
│   │   ├── meridian-user-testing-family-beneficiary.toml
│   │   ├── meridian-user-testing-financial-operations-professional.toml
│   │   ├── meridian-user-testing-fund-accountant.toml
│   │   ├── meridian-user-testing-fund-investor-lp.toml
│   │   ├── meridian-user-testing-integration-administrator.toml
│   │   ├── meridian-user-testing-investment-accountant.toml
│   │   ├── meridian-user-testing-investment-analyst.toml
│   │   ├── meridian-user-testing-operations-manager.toml
│   │   ├── meridian-user-testing-portfolio-manager.toml
│   │   ├── meridian-user-testing-quantitative-researcher.toml
│   │   ├── meridian-user-testing-reconciliation-analyst.toml
│   │   ├── meridian-user-testing-reporting-analyst.toml
│   │   ├── meridian-user-testing-ria-client.toml
│   │   ├── meridian-user-testing-risk-manager.toml
│   │   ├── meridian-user-testing-security-administrator.toml
│   │   ├── meridian-user-testing-system-administrator.toml
│   │   ├── meridian-user-testing-trader.toml
│   │   ├── meridian-user-testing-treasury-operations-specialist.toml
│   │   ├── meridian-user-testing-trustee.toml
│   │   ├── modular-desktop-mvvm.toml
│   │   ├── performance-resource-review.toml
│   │   ├── provider-management-workflow.toml
│   │   ├── research-data-acquisition.toml
│   │   ├── safe-refactoring.toml
│   │   ├── shared-component-extraction.toml
│   │   └── workstation-screen-composition.toml
│   ├── checklists
│   │   ├── desktop-workspace-definition-of-done.md
│   │   ├── modularity-checklist.md
│   │   ├── mvvm-checklist.md
│   │   ├── resource-management-checklist.md
│   │   └── safe-refactor-checklist.md
│   ├── environments
│   │   ├── environment.toml
│   │   └── README.md
│   ├── memory
│   │   ├── branches
│   │   │   └── README.md
│   │   ├── goals
│   │   │   ├── example.yml
│   │   │   ├── README.md
│   │   │   └── reporting-p0-completion.yml
│   │   ├── repo
│   │   │   ├── accounting-workflows.md
│   │   │   ├── ai-guidance.md
│   │   │   ├── architecture.md
│   │   │   ├── financial-record-explorers.md
│   │   │   ├── README.md
│   │   │   └── validation.md
│   │   ├── sessions
│   │   │   └── README.md
│   │   ├── tasks
│   │   │   ├── example.yml
│   │   │   ├── README.md
│   │   │   └── reporting-p0-completion.yml
│   │   ├── index.yml
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
│   │   │   ├── scripts
│   │   │   │   ├── simple_skill_eval_runner.py
│   │   │   │   ├── simple_skill_score.py
│   │   │   │   └── text_package_check.py
│   │   │   ├── codex-execution-contract.md
│   │   │   └── project-context.md
│   │   ├── dense-data-grid-inspector-panel
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── scripts
│   │   │   │   ├── dense_data_grid_inspector_panel_check.py
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── desktop-test-generation
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── scripts
│   │   │   │   ├── desktop_test_generation_check.py
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── diagnostics-audit-timeline
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── scripts
│   │   │   │   ├── diagnostics_audit_timeline_check.py
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── meridian-accounting-posting-controls
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── references
│   │   │   │   └── posting-control-checklist.md
│   │   │   ├── scripts
│   │   │   │   ├── posting_controls_check.py
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── meridian-archive-organizer
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
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
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── references
│   │   │   │   └── blueprint-patterns.md
│   │   │   ├── scripts
│   │   │   │   ├── blueprint_output_check.py
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── meridian-brainstorm
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── references
│   │   │   │   └── competitive-landscape.md
│   │   │   ├── scripts
│   │   │   │   ├── brainstorm_output_check.py
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── meridian-browser-workstation
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── scripts
│   │   │   │   ├── browser_workstation_check.py
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── meridian-cleanup
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── scripts
│   │   │   │   ├── cleanup_check.py
│   │   │   │   ├── repo-updater.ps1
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── meridian-code-architecture
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── references
│   │   │   │   └── architecture-checklist.md
│   │   │   ├── scripts
│   │   │   │   ├── architecture_surface.py
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── meridian-code-review
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── scripts
│   │   │   │   ├── code_review_check.py
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── meridian-codex-skill-builder
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── references
│   │   │   │   └── skill-package-checklist.md
│   │   │   ├── scripts
│   │   │   │   ├── run_evals.py
│   │   │   │   ├── score_eval.py
│   │   │   │   └── skill_package_audit.py
│   │   │   └── SKILL.md
│   │   ├── meridian-contract-governance
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── references
│   │   │   │   └── contract-impact-checklist.md
│   │   │   ├── scripts
│   │   │   │   ├── contract_impact.py
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── meridian-docs
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── scripts
│   │   │   │   ├── docs_check.py
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── meridian-event-accounting-architecture
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── references
│   │   │   │   └── event-accounting-patterns.md
│   │   │   ├── scripts
│   │   │   │   ├── accounting_architecture_check.py
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
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
│   │   │   │   ├── score_eval.py
│   │   │   │   └── skill_script_advisor.py
│   │   │   └── SKILL.md
│   │   ├── meridian-ledger-projection-replay-review
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── references
│   │   │   │   └── projection-replay-checklist.md
│   │   │   ├── scripts
│   │   │   │   ├── projection_replay_check.py
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── meridian-provider-builder
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── references
│   │   │   │   └── provider-patterns.md
│   │   │   ├── scripts
│   │   │   │   ├── provider_builder_check.py
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── meridian-repo-navigation
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── scripts
│   │   │   │   ├── repo_navigation_check.py
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── meridian-roadmap-strategist
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── references
│   │   │   │   └── roadmap-source-map.md
│   │   │   ├── scripts
│   │   │   │   ├── roadmap_output_check.py
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── meridian-simulated-user-panel
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── assets
│   │   │   │   ├── bundles
│   │   │   │   │   ├── cross-surface-review.manifest.json
│   │   │   │   │   ├── roadmap-review.manifest.json
│   │   │   │   │   ├── screen-review.manifest.json
│   │   │   │   │   ├── ship-readiness.manifest.json
│   │   │   │   │   └── workflow-walkthrough.manifest.json
│   │   │   │   ├── eval-result.schema.json
│   │   │   │   ├── review-manifest.schema.json
│   │   │   │   └── review-result.schema.json
│   │   │   ├── evals
│   │   │   │   ├── golden
│   │   │   │   │   ├── eval-01-welcome-onboarding-design-partner.md
│   │   │   │   │   ├── eval-02-provider-onboarding-release-gate.md
│   │   │   │   │   ├── eval-03-fund-ledger-controls-review.md
│   │   │   │   │   ├── eval-04-analysis-export-power-user-review.md
│   │   │   │   │   ├── eval-05-research-promotion-roadmap-review.md
│   │   │   │   │   └── eval-06-provider-health-usability-lab.md
│   │   │   │   ├── negative
│   │   │   │   │   ├── release-gate-insufficient-ship.md
│   │   │   │   │   └── shallow-panel.md
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   ├── evals.json
│   │   │   │   └── trigger-prompts.csv
│   │   │   ├── references
│   │   │   │   ├── artifact-bundles.md
│   │   │   │   ├── personas.md
│   │   │   │   ├── review-contract.md
│   │   │   │   ├── review-modes.md
│   │   │   │   └── rubric.md
│   │   │   ├── scripts
│   │   │   │   ├── check_shared_contracts.py
│   │   │   │   ├── check_trigger_prompts.py
│   │   │   │   ├── run_evals.py
│   │   │   │   ├── score_eval.py
│   │   │   │   ├── simulated_user_output_check.py
│   │   │   │   └── validate_review_manifest.py
│   │   │   └── SKILL.md
│   │   ├── meridian-test-writer
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── references
│   │   │   │   └── test-patterns.md
│   │   │   ├── scripts
│   │   │   │   ├── run_evals.py
│   │   │   │   ├── score_eval.py
│   │   │   │   └── test_writer_check.py
│   │   │   └── SKILL.md
│   │   ├── modular-desktop-mvvm
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── scripts
│   │   │   │   ├── modular_desktop_mvvm_check.py
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── performance-resource-review
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── scripts
│   │   │   │   ├── performance_resource_review_check.py
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── provider-management-workflow
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── scripts
│   │   │   │   ├── provider_management_workflow_check.py
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── research-data-acquisition
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── scripts
│   │   │   │   ├── research_data_acquisition_check.py
│   │   │   │   ├── run_evals.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── safe-refactoring
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── scripts
│   │   │   │   ├── run_evals.py
│   │   │   │   ├── safe_refactoring_check.py
│   │   │   │   └── score_eval.py
│   │   │   └── SKILL.md
│   │   ├── shared-component-extraction
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── scripts
│   │   │   │   ├── run_evals.py
│   │   │   │   ├── score_eval.py
│   │   │   │   └── shared_component_extraction_check.py
│   │   │   └── SKILL.md
│   │   ├── workstation-screen-composition
│   │   │   ├── agents
│   │   │   │   └── openai.yaml
│   │   │   ├── evals
│   │   │   │   ├── benchmark_baseline.json
│   │   │   │   └── evals.json
│   │   │   ├── scripts
│   │   │   │   ├── run_evals.py
│   │   │   │   ├── score_eval.py
│   │   │   │   └── workstation_screen_composition_check.py
│   │   │   └── SKILL.md
│   │   └── README.md
│   ├── AGENTS.md
│   └── config.toml
├── .devcontainer
│   ├── devcontainer.json
│   ├── docker-compose.yml
│   └── Dockerfile
├── .githooks
│   └── pre-commit
├── .github
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
│   │   ├── branch-cleanup.yml
│   │   ├── ci.yml
│   │   ├── codeql.yml
│   │   ├── copilot-setup-steps.yml
│   │   ├── demo-smoke.yml
│   │   ├── desktop-installer-packaging.yml
│   │   ├── desktop-screenshot-capture.yml
│   │   ├── desktop-standalone-publish.yml
│   │   ├── desktop-user-manual.yml
│   │   ├── desktop-workflow-runner.yml
│   │   ├── documentation.yml
│   │   ├── golden-path-validation.yml
│   │   ├── ibapi-runtime.yml
│   │   ├── ibapi-smoke.yml
│   │   ├── maintenance.yml
│   │   ├── meridian-ci.yml
│   │   ├── production-certification.yml
│   │   ├── provider-validation.yml
│   │   ├── publish-smoke.yml
│   │   ├── README.md
│   │   ├── roadmap-source-docs.yml
│   │   ├── roadmap-tools-manual.yml
│   │   ├── robinhood-options-smoke.yml
│   │   ├── schema-control.yml
│   │   ├── targeted-test.yml
│   │   ├── web-screenshot-capture.yml
│   │   ├── windows-desktop-build.yml
│   │   ├── wpf-dev-validation.yml
│   │   └── wpf-route-validation.yml
│   ├── CODEOWNERS
│   ├── copilot-instructions.md
│   ├── dependabot.yml
│   ├── labeler.yml
│   ├── labels.yml
│   ├── markdown-link-check-config.json
│   ├── PULL_REQUEST_TEMPLATE.md
│   ├── pull_request_template.md
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
│   │   ├── StrategyRunReadBenchmarks.cs
│   │   └── WalChecksumBenchmarks.cs
│   ├── results
│   │   ├── 20260521_111257
│   │   │   ├── batchserialization
│   │   │   │   └── results
│   │   │   │       ├── Meridian.Benchmarks.BatchSerializationBenchmarks-report-github.md
│   │   │   │       ├── Meridian.Benchmarks.BatchSerializationBenchmarks-report.csv
│   │   │   │       └── Meridian.Benchmarks.BatchSerializationBenchmarks-report.html
│   │   │   ├── canonicalizing
│   │   │   │   └── results
│   │   │   │       ├── Meridian.Benchmarks.CanonicalizingPublisherBenchmarks-report-github.md
│   │   │   │       ├── Meridian.Benchmarks.CanonicalizingPublisherBenchmarks-report.csv
│   │   │   │       └── Meridian.Benchmarks.CanonicalizingPublisherBenchmarks-report.html
│   │   │   ├── composite
│   │   │   │   └── results
│   │   │   │       ├── Meridian.Benchmarks.CompositeSinkBenchmarks-report-github.md
│   │   │   │       ├── Meridian.Benchmarks.CompositeSinkBenchmarks-report.csv
│   │   │   │       └── Meridian.Benchmarks.CompositeSinkBenchmarks-report.html
│   │   │   └── wal
│   │   │       └── results
│   │   │           ├── Meridian.Benchmarks.WalChecksumBenchmarks-report-github.md
│   │   │           ├── Meridian.Benchmarks.WalChecksumBenchmarks-report.csv
│   │   │           └── Meridian.Benchmarks.WalChecksumBenchmarks-report.html
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
│   │   │   └── results
│   │   │       ├── Meridian.Benchmarks.WalChecksumBenchmarks-report-github.md
│   │   │       ├── Meridian.Benchmarks.WalChecksumBenchmarks-report.csv
│   │   │       └── Meridian.Benchmarks.WalChecksumBenchmarks-report.html
│   │   ├── short-run-short
│   │   │   └── results
│   │   │       ├── Meridian.Benchmarks.WalChecksumBenchmarks-report-github.md
│   │   │       ├── Meridian.Benchmarks.WalChecksumBenchmarks-report.csv
│   │   │       └── Meridian.Benchmarks.WalChecksumBenchmarks-report.html
│   │   └── short-run-short2
│   │       └── results
│   │           ├── Meridian.Benchmarks.WalChecksumBenchmarks-report-github.md
│   │           ├── Meridian.Benchmarks.WalChecksumBenchmarks-report.csv
│   │           └── Meridian.Benchmarks.WalChecksumBenchmarks-report.html
│   ├── BOTTLENECK_REPORT.md
│   └── run-bottleneck-benchmarks.sh
├── build
│   ├── ci
│   │   └── lane-manifest.json
│   ├── config
│   │   ├── security
│   │   │   └── npm-audit-accepted-advisories.json
│   │   ├── testing
│   │   │   └── test-skip-register.json
│   │   └── file-size-baseline.json
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
│       ├── ai
│       │   ├── tests
│       │   │   ├── __init__.py
│       │   │   ├── test_ai_edit_tool.py
│       │   │   ├── test_context_budget.py
│       │   │   └── test_promptfoo_adapter.py
│       │   ├── __init__.py
│       │   ├── ai-edit-tool.py
│       │   ├── ai_edit_tool.py
│       │   ├── context-budget.py
│       │   ├── context_budget.py
│       │   ├── meridian_context_exporter.py
│       │   ├── promptfoo-adapter.py
│       │   └── promptfoo_adapter.py
│       ├── ci
│       │   ├── apiclient-caller-baseline.json
│       │   ├── check-apiclient-callers.py
│       │   ├── check-dashboard-type-barrel.py
│       │   ├── check-file-size.py
│       │   ├── check-lane-manifest.py
│       │   ├── check-test-skip-register.py
│       │   ├── check-warning-suppressions.py
│       │   ├── check-workflow-hygiene.py
│       │   ├── dispatch-targeted-test.py
│       │   ├── generate-release-evidence-manifest.py
│       │   ├── run-dotnet-ci-tests.py
│       │   ├── run-script-tests.py
│       │   ├── script-test-quarantine.json
│       │   ├── summarize-ci-artifacts.py
│       │   ├── validate-monitoring-deployment.py
│       │   ├── validate-npm-audit.py
│       │   ├── validate-observability-contract.py
│       │   └── validate-test-results.py
│       ├── docs
│       │   ├── tests
│       │   │   ├── test_check_ai_handoff.py
│       │   │   ├── test_check_ai_inventory.py
│       │   │   ├── test_check_codex_memory.py
│       │   │   ├── test_generate_api_contract_coverage_dashboard.py
│       │   │   ├── test_generate_structure_docs.py
│       │   │   ├── test_markdown_generation_lint.py
│       │   │   ├── test_pilot_readiness_dashboard.py
│       │   │   ├── test_scan_todos.py
│       │   │   └── test_validate_docs_structure.py
│       │   ├── add-todos.py
│       │   ├── ai-docs-maintenance.py
│       │   ├── ai-handoff-host-targets.json
│       │   ├── check-ai-contract-drift.py
│       │   ├── check-ai-handoff.py
│       │   ├── check-ai-inventory.py
│       │   ├── check-ai-navigation-freshness.py
│       │   ├── check-ai-routing-parity.py
│       │   ├── check-codex-memory.py
│       │   ├── check-codex-skills.py
│       │   ├── check-handoff-packet-schema.py
│       │   ├── check-known-lanes.py
│       │   ├── check-mode-escalation.py
│       │   ├── check-plan-checklists.py
│       │   ├── check-validation-floor.py
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
│       │   ├── handoff-packet-generator.py
│       │   ├── lint-command-snippets.py
│       │   ├── mark-stale-docs.py
│       │   ├── prompt-route-linter.py
│       │   ├── README.md
│       │   ├── render-make-help.py
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
│       │   ├── validate-design-document-adaptation.py
│       │   ├── validate-design-module-conformance.py
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
│       │   ├── build-consumer-setup.ps1
│       │   ├── certify-desktop-install-lifecycle.ps1
│       │   ├── install-web-workstation.ps1
│       │   ├── install.ps1
│       │   ├── install.sh
│       │   ├── package-desktop-msix.ps1
│       │   └── smoke-web-workstation-install.ps1
│       ├── lib
│       │   ├── ArtifactRetention.psm1
│       │   └── BuildNotification.psm1
│       ├── publish
│       │   ├── generate-sbom.ps1
│       │   ├── measure-size.ps1
│       │   ├── publish.ps1
│       │   └── publish.sh
│       ├── recovery
│       │   └── invoke-production-recovery.ps1
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
│       ├── check-execution-log-sanitization.py
│       ├── duplication-audit.ps1
│       ├── generate-ui-api-routes-ts.py
│       ├── generate-workspace-catalog-ts.py
│       ├── schema-control.py
│       ├── validate-tooling-metadata.py
│       └── validate_budget.py
├── config
│   ├── appsettings.sample.json
│   ├── appsettings.schema.json
│   ├── condition-codes.json
│   ├── score-reason-registry.json
│   └── venue-mapping.json
├── database
│   ├── manifest
│   │   ├── schemas
│   │   │   ├── asset_operations.json
│   │   │   ├── banking.json
│   │   │   ├── fund_accounts.json
│   │   │   ├── fund_structure.json
│   │   │   ├── identity_access.json
│   │   │   ├── ledger.json
│   │   │   ├── money_market.json
│   │   │   ├── public.json
│   │   │   ├── reporting.json
│   │   │   └── security_master.json
│   │   ├── catalog.json
│   │   ├── contracts.json
│   │   ├── dependencies.json
│   │   ├── migrations.json
│   │   └── policies.json
│   ├── policies
│   │   ├── migration-waivers.json
│   │   └── schema-control.json
│   └── schema-control.json
├── deploy
│   ├── docker
│   │   ├── .dockerignore
│   │   ├── docker-compose.monitoring.yml
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
│   │   ├── alert-rules.test.yml
│   │   ├── alert-rules.yml
│   │   └── prometheus.yml
│   ├── systemd
│   │   └── meridian.service
│   └── README.md
├── docs
│   ├── adr
│   │   ├── 017-modular-operational-monolith.md
│   │   ├── 018-declarative-statement-mapping-profiles.md
│   │   ├── 019-production-support-matrix-and-deployment-posture.md
│   │   ├── 020-lifecycle-control-plane.md
│   │   ├── 021-verified-operation-outcomes-and-case-history.md
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
│   │   │   ├── advanced-configuration.md
│   │   │   ├── agent-workflow-redesign.md
│   │   │   ├── memory-system.md
│   │   │   ├── prompt-execution-trace.md
│   │   │   ├── prompt-route-rules.json
│   │   │   ├── quickstart.md
│   │   │   ├── README.md
│   │   │   ├── route-cards.md
│   │   │   └── self-improving-agents.md
│   │   ├── context
│   │   │   ├── accounting-context.md
│   │   │   ├── operational-evidence-context.md
│   │   │   └── README.md
│   │   ├── copilot
│   │   │   ├── ai-sync-workflow.md
│   │   │   ├── contract-policy.mirror.json
│   │   │   └── instructions.md
│   │   ├── exports
│   │   │   ├── context.json
│   │   │   ├── LLM_CONTEXT.md
│   │   │   └── README.md
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
│   │   ├── tooling
│   │   │   └── README.md
│   │   ├── agent-handoff-checklist.md
│   │   ├── ai-known-errors.md
│   │   ├── ai-systems-inventory.md
│   │   ├── assistant-workflow-contract.md
│   │   ├── contract-policy.json
│   │   ├── model-routing-policy.json
│   │   ├── model-routing-telemetry.md
│   │   ├── parallel-task-manifest-template.md
│   │   ├── README.md
│   │   ├── work-modes.md
│   │   └── working-memory.md
│   ├── architecture
│   │   ├── diagrams
│   │   │   ├── meridian-assurance-loop.mmd
│   │   │   ├── meridian-bounded-context-ownership.mmd
│   │   │   ├── meridian-browser-workstation-flow.mmd
│   │   │   ├── meridian-browser-workstation-route-map.mmd
│   │   │   ├── meridian-development-roadmap.mmd
│   │   │   ├── meridian-operational-record-context.mmd
│   │   │   ├── meridian-operational-record-flow.mmd
│   │   │   ├── meridian-paper-session-replay-flow.mmd
│   │   │   ├── meridian-source-layer-map.mmd
│   │   │   └── meridian-storage-topology.mmd
│   │   ├── c4-diagrams.md
│   │   ├── core-extensibility-model.md
│   │   ├── crystallized-storage-format.md
│   │   ├── design-document-adaptation.md
│   │   ├── design-document-adaptation.yml
│   │   ├── design-module-conformance.md
│   │   ├── design-module-conformance.yml
│   │   ├── desktop-layers.md
│   │   ├── deterministic-canonicalization.md
│   │   ├── domains.md
│   │   ├── environment-designer-runtime-projection-and-wpf-admin-surface.md
│   │   ├── event-accounting-architecture.md
│   │   ├── evidence-workflow-fabric.md
│   │   ├── layer-boundaries.md
│   │   ├── ledger-architecture.md
│   │   ├── meridian-development-intelligence-framework.md
│   │   ├── meridian-domain-model.md
│   │   ├── meridian-vision.md
│   │   ├── module-conventions.md
│   │   ├── module-map.md
│   │   ├── mvvm-guidelines.md
│   │   ├── operator-observability-dashboard.md
│   │   ├── overview.md
│   │   ├── project-structure.md
│   │   ├── provider-integration-manifest-runtime.md
│   │   ├── provider-management.md
│   │   ├── README.md
│   │   ├── runtime-component-state-boundaries.md
│   │   ├── security-master-architecture-review.md
│   │   ├── storage-design.md
│   │   ├── strategy-builder-integration.md
│   │   ├── strategy-engine-foundation.md
│   │   ├── why-this-architecture.md
│   │   ├── workflow-library.md
│   │   ├── workstation-continuity-payload-profile.md
│   │   ├── wpf-shell-mvvm.md
│   │   ├── wpf-workstation-shell-ux.md
│   │   └── write-path-invariants.md
│   ├── development
│   │   ├── accounting-blueprints
│   │   │   ├── commitment-and-capital-call-engine.md
│   │   │   ├── equalization-and-series-accounting.md
│   │   │   ├── incentive-fee-mechanics.md
│   │   │   └── README.md
│   │   ├── mockups
│   │   │   └── web-ui
│   │   │       ├── 01-trading-cockpit.html
│   │   │       ├── 02-workspace-overview.html
│   │   │       ├── 03-settings-task-route.html
│   │   │       ├── 04-degraded-states.html
│   │   │       └── README.md
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
│   │   ├── web-ui-structural-improvement-proposal.md
│   │   ├── wpf-implementation-notes.md
│   │   └── wpf-web-ui-alignment-plan.md
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
│   │   │   ├── ui-navigation-map.svg
│   │   │   ├── ui-wpf-screen-catalog.dot
│   │   │   ├── ui-wpf-screen-catalog.png
│   │   │   ├── ui-wpf-screen-catalog.svg
│   │   │   ├── ui-wpf-screen-summary.dot
│   │   │   ├── ui-wpf-screen-summary.png
│   │   │   ├── ui-wpf-screen-summary.svg
│   │   │   ├── ui-wpf-screens-accounting.dot
│   │   │   ├── ui-wpf-screens-accounting.png
│   │   │   ├── ui-wpf-screens-accounting.svg
│   │   │   ├── ui-wpf-screens-data.dot
│   │   │   ├── ui-wpf-screens-data.png
│   │   │   ├── ui-wpf-screens-data.svg
│   │   │   ├── ui-wpf-screens-portfolio.dot
│   │   │   ├── ui-wpf-screens-portfolio.png
│   │   │   ├── ui-wpf-screens-portfolio.svg
│   │   │   ├── ui-wpf-screens-reporting.dot
│   │   │   ├── ui-wpf-screens-reporting.png
│   │   │   ├── ui-wpf-screens-reporting.svg
│   │   │   ├── ui-wpf-screens-settings.dot
│   │   │   ├── ui-wpf-screens-settings.png
│   │   │   ├── ui-wpf-screens-settings.svg
│   │   │   ├── ui-wpf-screens-strategy.dot
│   │   │   ├── ui-wpf-screens-strategy.png
│   │   │   ├── ui-wpf-screens-strategy.svg
│   │   │   ├── ui-wpf-screens-trading.dot
│   │   │   ├── ui-wpf-screens-trading.png
│   │   │   └── ui-wpf-screens-trading.svg
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
│   │   ├── ui-navigation-map.svg
│   │   ├── ui-wpf-screen-catalog.dot
│   │   ├── ui-wpf-screen-catalog.png
│   │   ├── ui-wpf-screen-catalog.svg
│   │   ├── ui-wpf-screen-summary.dot
│   │   ├── ui-wpf-screen-summary.png
│   │   ├── ui-wpf-screen-summary.svg
│   │   ├── ui-wpf-screens-accounting.dot
│   │   ├── ui-wpf-screens-accounting.png
│   │   ├── ui-wpf-screens-accounting.svg
│   │   ├── ui-wpf-screens-data.dot
│   │   ├── ui-wpf-screens-data.png
│   │   ├── ui-wpf-screens-data.svg
│   │   ├── ui-wpf-screens-portfolio.dot
│   │   ├── ui-wpf-screens-portfolio.png
│   │   ├── ui-wpf-screens-portfolio.svg
│   │   ├── ui-wpf-screens-reporting.dot
│   │   ├── ui-wpf-screens-reporting.png
│   │   ├── ui-wpf-screens-reporting.svg
│   │   ├── ui-wpf-screens-settings.dot
│   │   ├── ui-wpf-screens-settings.png
│   │   ├── ui-wpf-screens-settings.svg
│   │   ├── ui-wpf-screens-strategy.dot
│   │   ├── ui-wpf-screens-strategy.png
│   │   ├── ui-wpf-screens-strategy.svg
│   │   ├── ui-wpf-screens-trading.dot
│   │   ├── ui-wpf-screens-trading.png
│   │   └── ui-wpf-screens-trading.svg
│   ├── docfx
│   │   ├── api
│   │   │   └── index.md
│   │   ├── filterConfig.yml
│   │   └── README.md
│   ├── domain
│   │   ├── fund-event.md
│   │   ├── operational-evidence-graph.md
│   │   ├── README.md
│   │   └── security.md
│   ├── engineering
│   │   ├── blueprints
│   │   │   ├── financing-liabilities-depreciation-blueprint.md
│   │   │   ├── README.md
│   │   │   └── risk-engine-severity-and-decision-journal-blueprint.md
│   │   ├── dead-code-inventory.md
│   │   ├── free-development-tools.md
│   │   ├── live-trading-engine.md
│   │   ├── practical-csharp-wpf-financial-markets.md
│   │   ├── production-certification-evidence-chain.md
│   │   ├── production-readiness-audit-2026-07-27.md
│   │   ├── README.md
│   │   └── wpf-perf-uiux-audit-2026-06-14.md
│   ├── examples
│   │   ├── agent-improvement-loop
│   │   │   ├── agent_improvement_loop.ipynb
│   │   │   └── README.md
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
│   │   ├── database
│   │   │   ├── contracts
│   │   │   │   ├── asset-operations-contracts.md
│   │   │   │   ├── banking-contracts.md
│   │   │   │   ├── direct-lending-contracts-page-01.md
│   │   │   │   ├── direct-lending-contracts-page-02.md
│   │   │   │   ├── direct-lending-contracts.md
│   │   │   │   ├── fund-governance-contracts-page-01.md
│   │   │   │   ├── fund-governance-contracts-page-02.md
│   │   │   │   ├── fund-governance-contracts.md
│   │   │   │   ├── identity-access-contracts.md
│   │   │   │   ├── ledger-contracts-page-01.md
│   │   │   │   ├── ledger-contracts-page-02.md
│   │   │   │   ├── ledger-contracts-page-03.md
│   │   │   │   ├── ledger-contracts-page-04.md
│   │   │   │   ├── ledger-contracts.md
│   │   │   │   ├── money-market-contracts.md
│   │   │   │   ├── reporting-contracts.md
│   │   │   │   ├── security-master-contracts-page-01.md
│   │   │   │   ├── security-master-contracts-page-02.md
│   │   │   │   ├── security-master-contracts-page-03.md
│   │   │   │   └── security-master-contracts.md
│   │   │   ├── diagrams
│   │   │   │   ├── asset_operations.mmd
│   │   │   │   ├── banking.mmd
│   │   │   │   ├── contracts-asset-operations-contracts.mmd
│   │   │   │   ├── contracts-banking-contracts.mmd
│   │   │   │   ├── contracts-direct-lending-contracts-page-01.mmd
│   │   │   │   ├── contracts-direct-lending-contracts-page-02.mmd
│   │   │   │   ├── contracts-fund-governance-contracts-page-01.mmd
│   │   │   │   ├── contracts-fund-governance-contracts-page-02.mmd
│   │   │   │   ├── contracts-identity-access-contracts.mmd
│   │   │   │   ├── contracts-ledger-contracts-page-01.mmd
│   │   │   │   ├── contracts-ledger-contracts-page-02.mmd
│   │   │   │   ├── contracts-ledger-contracts-page-03.mmd
│   │   │   │   ├── contracts-ledger-contracts-page-04.mmd
│   │   │   │   ├── contracts-money-market-contracts.mmd
│   │   │   │   ├── contracts-reporting-contracts.mmd
│   │   │   │   ├── contracts-security-master-contracts-page-01.mmd
│   │   │   │   ├── contracts-security-master-contracts-page-02.mmd
│   │   │   │   ├── contracts-security-master-contracts-page-03.mmd
│   │   │   │   ├── fund_accounts.mmd
│   │   │   │   ├── fund_structure.mmd
│   │   │   │   ├── identity_access.mmd
│   │   │   │   ├── ledger.mmd
│   │   │   │   ├── money_market.mmd
│   │   │   │   ├── public.mmd
│   │   │   │   ├── reporting.mmd
│   │   │   │   └── security_master.mmd
│   │   │   ├── modules
│   │   │   │   ├── asset_operations.md
│   │   │   │   ├── banking.md
│   │   │   │   ├── fund_accounts.md
│   │   │   │   ├── fund_structure.md
│   │   │   │   ├── identity_access.md
│   │   │   │   ├── ledger.md
│   │   │   │   ├── money_market.md
│   │   │   │   ├── public.md
│   │   │   │   ├── reporting.md
│   │   │   │   └── security_master.md
│   │   │   ├── data-object-catalog.md
│   │   │   ├── README.md
│   │   │   └── schema-catalog.md
│   │   ├── source
│   │   │   ├── diagrams
│   │   │   │   ├── source-modules.mmd
│   │   │   │   └── source-readme-coverage.mmd
│   │   │   ├── render-manifest.json
│   │   │   ├── source-modules.json
│   │   │   ├── source-modules.normalized.yml
│   │   │   ├── source-readme-coverage.json
│   │   │   └── source-readme-coverage.normalized.yml
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
│   ├── integrations
│   │   ├── fsharp-integration.md
│   │   ├── language-strategy.md
│   │   ├── lean-integration.md
│   │   ├── README.md
│   │   └── tastytrade-endpoint-coverage.md
│   ├── operations
│   │   ├── cleanup-and-maintenance.md
│   │   ├── live-execution-controls.md
│   │   ├── operator-runbook.md
│   │   ├── README.md
│   │   └── service-level-objectives.md
│   ├── operators
│   │   ├── browser-workstation-installer.md
│   │   ├── deployment-packaging.md
│   │   ├── failover-and-recovery.md
│   │   ├── fund-ops-persistence-cutover.md
│   │   ├── governed-reporting-operations.md
│   │   ├── operator-runbook.md
│   │   ├── plaid-provider-operations.md
│   │   ├── preflight-checklist.md
│   │   ├── provider-backfill-operations.md
│   │   ├── provider-credentials.md
│   │   ├── provider-onboarding-alpaca.md
│   │   ├── provider-onboarding-interactive-brokers.md
│   │   ├── README.md
│   │   ├── reconciliation-operations.md
│   │   ├── service-level-objectives.md
│   │   ├── statement-reconciliation-report-operations.md
│   │   └── verified-outcome-recovery.md
│   ├── plans
│   │   ├── codebase-audit-cleanup-roadmap.md
│   │   ├── desktop-workstation-screen-blueprint.checklist.json
│   │   ├── desktop-workstation-screen-blueprint.md
│   │   ├── paper-trading-cockpit-reliability-sprint.md
│   │   ├── README.md
│   │   ├── report-writer-auto-preview-blueprint.md
│   │   ├── research-backtest-trust-and-velocity-blueprint.md
│   │   └── security-master-passport-workbench.md
│   ├── product
│   │   ├── adversarial-program-review-2026-07.md
│   │   ├── data-provider-accounting-brainstorm-2026-07.md
│   │   ├── deferred-expansion-boundaries.md
│   │   ├── excel-onboarding-workbook-brainstorm-2026-07.md
│   │   ├── high-value-code-brainstorm-2026-07.md
│   │   ├── implementation-todo-list.md
│   │   ├── meridian-design-document.md
│   │   ├── portfolio-cash-ladder-blueprint-2026-07.md
│   │   ├── product-roadmap-priorities-2026-07.md
│   │   ├── README.md
│   │   ├── w10-depth-slate-2026-07.md
│   │   ├── web-ui-improvements-brainstorm-2026-07.md
│   │   ├── web-ui-improvements-implementation-plan-2026-07.md
│   │   ├── web-ui-report-run-stream-blueprint-2026-07.md
│   │   └── web-ui-stream-fan-out-blueprint-2026-07.md
│   ├── prompts
│   │   ├── automation-prompts.md
│   │   ├── README.md
│   │   ├── repo-maintenance-prompts.md
│   │   └── roadmap-source-docs-implementation-prompt.md
│   ├── reference
│   │   ├── accounting-configuration.md
│   │   ├── accounting-report-packs.md
│   │   ├── api-reference.md
│   │   ├── appsettings-schema.md
│   │   ├── backtest-preflight-and-stage-telemetry.md
│   │   ├── brand-assets.md
│   │   ├── broker-provider-capability-expansion-review.md
│   │   ├── contract-compatibility-matrix.md
│   │   ├── data-dictionary.md
│   │   ├── data-uniformity.md
│   │   ├── database-schema.md
│   │   ├── design-review-memo.md
│   │   ├── edgar-reference-data.md
│   │   ├── environment-variables.md
│   │   ├── export-preflight-rules.md
│   │   ├── governance-report-packs.md
│   │   ├── interactive-brokers-api-compatibility.md
│   │   ├── ledger-journal-store.md
│   │   ├── lifecycle-control-plane.md
│   │   ├── oms-ems-integration.md
│   │   ├── open-source-references.md
│   │   ├── provider-capability-matrix.md
│   │   ├── provider-integration-status.md
│   │   ├── provider-validation-evidence-schema.md
│   │   ├── provider-validation-matrix.md
│   │   ├── README.md
│   │   ├── reconciliation-break-taxonomy.md
│   │   ├── research-briefing-workflow.md
│   │   ├── strategy-briefing-workflow.md
│   │   ├── strategy-promotion-history.md
│   │   ├── ufl-capability-model.md
│   │   ├── ufl-conformance-matrix.md
│   │   ├── ufl-supported-assets-index.md
│   │   └── verified-operation-outcomes.md
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
│   │   │   │   ├── manual-accounting
│   │   │   │   │   ├── 01-accounting-shell.png
│   │   │   │   │   ├── 02-fund-ledger.png
│   │   │   │   │   ├── 03-fund-reconciliation.png
│   │   │   │   │   ├── 04-fund-report-pack.png
│   │   │   │   │   ├── 05-security-master.png
│   │   │   │   │   └── 06-settings.png
│   │   │   │   ├── manual-data
│   │   │   │   │   ├── 01-data-shell.png
│   │   │   │   │   ├── 02-providers.png
│   │   │   │   │   ├── 03-provider-health.png
│   │   │   │   │   ├── 04-backfill.png
│   │   │   │   │   ├── 05-data-sources.png
│   │   │   │   │   ├── 06-storage.png
│   │   │   │   │   └── 07-data-quality.png
│   │   │   │   ├── manual-overview
│   │   │   │   │   ├── 01-strategy-workspace.png
│   │   │   │   │   ├── 02-workspace-layouts.png
│   │   │   │   │   ├── 03-strategy-workspace.png
│   │   │   │   │   ├── 04-trading-workspace.png
│   │   │   │   │   ├── 05-data-workspace.png
│   │   │   │   │   ├── 06-accounting-workspace.png
│   │   │   │   │   └── 07-help.png
│   │   │   │   └── manual-strategy-and-trading
│   │   │   │       ├── 01-strategy-shell.png
│   │   │   │       ├── 02-backtest.png
│   │   │   │       ├── 03-strategy-runs.png
│   │   │   │       ├── 04-quant-script.png
│   │   │   │       ├── 05-trading-shell.png
│   │   │   │       ├── 06-position-blotter.png
│   │   │   │       └── 07-run-risk.png
│   │   │   ├── README.md
│   │   │   ├── retry-telemetry.json
│   │   │   ├── wpf-account-portfolio.png
│   │   │   ├── wpf-accounting-shell.png
│   │   │   ├── wpf-activity-log.png
│   │   │   ├── wpf-add-provider-wizard.png
│   │   │   ├── wpf-admin-maintenance.png
│   │   │   ├── wpf-advanced-analytics.png
│   │   │   ├── wpf-aggregate-portfolio.png
│   │   │   ├── wpf-analysis-export-wizard.png
│   │   │   ├── wpf-analysis-export.png
│   │   │   ├── wpf-archive-health.png
│   │   │   ├── wpf-backfill.png
│   │   │   ├── wpf-backtest.png
│   │   │   ├── wpf-batch-backtest.png
│   │   │   ├── wpf-charts.png
│   │   │   ├── wpf-collection-sessions.png
│   │   │   ├── wpf-credential-management.png
│   │   │   ├── wpf-dashboard.png
│   │   │   ├── wpf-data-browser.png
│   │   │   ├── wpf-data-calendar.png
│   │   │   ├── wpf-data-export.png
│   │   │   ├── wpf-data-quality.png
│   │   │   ├── wpf-data-sampling.png
│   │   │   ├── wpf-data-shell.png
│   │   │   ├── wpf-data-sources.png
│   │   │   ├── wpf-diagnostics.png
│   │   │   ├── wpf-direct-lending.png
│   │   │   ├── wpf-environment-designer.png
│   │   │   ├── wpf-event-replay.png
│   │   │   ├── wpf-export-presets.png
│   │   │   ├── wpf-fund-accounting-close.png
│   │   │   ├── wpf-fund-accounting-configure.png
│   │   │   ├── wpf-fund-accounts.png
│   │   │   ├── wpf-fund-audit-trail.png
│   │   │   ├── wpf-fund-banking.png
│   │   │   ├── wpf-fund-cash-financing.png
│   │   │   ├── wpf-fund-ledger.png
│   │   │   ├── wpf-fund-portfolio.png
│   │   │   ├── wpf-fund-reconciliation.png
│   │   │   ├── wpf-fund-report-pack.png
│   │   │   ├── wpf-fund-structure-setup.png
│   │   │   ├── wpf-fund-trial-balance.png
│   │   │   ├── wpf-help.png
│   │   │   ├── wpf-home-workspace.png
│   │   │   ├── wpf-index-subscription.png
│   │   │   ├── wpf-keyboard-shortcuts.png
│   │   │   ├── wpf-lean-integration.png
│   │   │   ├── wpf-ledger-explorer.png
│   │   │   ├── wpf-lifecycle-control.png
│   │   │   ├── wpf-live-data.png
│   │   │   ├── wpf-messaging-hub.png
│   │   │   ├── wpf-notification-center.png
│   │   │   ├── wpf-options.png
│   │   │   ├── wpf-order-book.png
│   │   │   ├── wpf-package-manager.png
│   │   │   ├── wpf-portfolio-explorer.png
│   │   │   ├── wpf-portfolio-import.png
│   │   │   ├── wpf-portfolio-shell.png
│   │   │   ├── wpf-position-blotter.png
│   │   │   ├── wpf-provider-health.png
│   │   │   ├── wpf-provider.png
│   │   │   ├── wpf-quant-script.png
│   │   │   ├── wpf-report-line-provenance-explorer.png
│   │   │   ├── wpf-report-run-status.png
│   │   │   ├── wpf-reporting-shell.png
│   │   │   ├── wpf-retention-assurance.png
│   │   │   ├── wpf-run-cash-flow.png
│   │   │   ├── wpf-run-detail.png
│   │   │   ├── wpf-run-ledger.png
│   │   │   ├── wpf-run-mat.png
│   │   │   ├── wpf-run-portfolio.png
│   │   │   ├── wpf-run-risk.png
│   │   │   ├── wpf-schedules.png
│   │   │   ├── wpf-security-instrument-explorer.png
│   │   │   ├── wpf-security-master.png
│   │   │   ├── wpf-service-manager.png
│   │   │   ├── wpf-settings-shell.png
│   │   │   ├── wpf-settings.png
│   │   │   ├── wpf-setup-wizard.png
│   │   │   ├── wpf-storage-optimization.png
│   │   │   ├── wpf-storage.png
│   │   │   ├── wpf-strategy-runs.png
│   │   │   ├── wpf-strategy-shell.png
│   │   │   ├── wpf-symbol-mapping.png
│   │   │   ├── wpf-symbol-storage.png
│   │   │   ├── wpf-symbols.png
│   │   │   ├── wpf-system-health.png
│   │   │   ├── wpf-time-series-alignment.png
│   │   │   ├── wpf-trading-hours.png
│   │   │   ├── wpf-trading-shell.png
│   │   │   ├── wpf-watchlist.png
│   │   │   ├── wpf-welcome.png
│   │   │   ├── wpf-workflow-library.png
│   │   │   └── wpf-workspaces.png
│   │   ├── web
│   │   │   ├── web-accounting-account-detail.png
│   │   │   ├── web-accounting-approval-inbox.png
│   │   │   ├── web-accounting-approvals.png
│   │   │   ├── web-accounting-asset-detail.png
│   │   │   ├── web-accounting-capital-accounts.png
│   │   │   ├── web-accounting-close-calendar.png
│   │   │   ├── web-accounting-configure.png
│   │   │   ├── web-accounting-entity-setup.png
│   │   │   ├── web-accounting-evidence-detail.png
│   │   │   ├── web-accounting-evidence-workbench.png
│   │   │   ├── web-accounting-exceptions.png
│   │   │   ├── web-accounting-external-gl-reconciliation.png
│   │   │   ├── web-accounting-journal-entries.png
│   │   │   ├── web-accounting-journal-entry-detail.png
│   │   │   ├── web-accounting-ledger.png
│   │   │   ├── web-accounting-operations-continuity.png
│   │   │   ├── web-accounting-reconciliation-match.png
│   │   │   ├── web-accounting-reconciliation.png
│   │   │   ├── web-accounting-security-master.png
│   │   │   ├── web-accounting-statement-import.png
│   │   │   ├── web-accounting-trial-balance-detail.png
│   │   │   ├── web-accounting-workspace.png
│   │   │   ├── web-daily-control-tower.png
│   │   │   ├── web-data-alerts.png
│   │   │   ├── web-data-backfills.png
│   │   │   ├── web-data-evidence-workbench.png
│   │   │   ├── web-data-exports.png
│   │   │   ├── web-data-import.png
│   │   │   ├── web-data-ingestion-operations.png
│   │   │   ├── web-data-live-quotes.png
│   │   │   ├── web-data-providers.png
│   │   │   ├── web-data-query.png
│   │   │   ├── web-data-storage-assurance.png
│   │   │   ├── web-data-watchlist.png
│   │   │   ├── web-data-workspace.png
│   │   │   ├── web-operator-readiness-console.png
│   │   │   ├── web-portfolio-asset-detail.png
│   │   │   ├── web-portfolio-attribution.png
│   │   │   ├── web-portfolio-brokerage-sync.png
│   │   │   ├── web-portfolio-cash-ladder.png
│   │   │   ├── web-portfolio-family-office.png
│   │   │   ├── web-portfolio-workspace.png
│   │   │   ├── web-reporting-evidence-workbench.png
│   │   │   ├── web-reporting-exports.png
│   │   │   ├── web-reporting-governance.png
│   │   │   ├── web-reporting-library.png
│   │   │   ├── web-reporting-operations-record.png
│   │   │   ├── web-reporting-preview-validation.png
│   │   │   ├── web-reporting-report-builder.png
│   │   │   ├── web-reporting-report-packs.png
│   │   │   ├── web-reporting-run-detail.png
│   │   │   ├── web-reporting-run-parameters.png
│   │   │   ├── web-reporting-run-status.png
│   │   │   ├── web-reporting-scheduled.png
│   │   │   ├── web-reporting-workspace.png
│   │   │   ├── web-settings-access.png
│   │   │   ├── web-settings-accounting-systems.png
│   │   │   ├── web-settings-alpaca-provider-advanced.png
│   │   │   ├── web-settings-alpaca-provider-setup.png
│   │   │   ├── web-settings-backend-capability-coverage.png
│   │   │   ├── web-settings-diagnostic-endpoints.png
│   │   │   ├── web-settings-diagnostics.png
│   │   │   ├── web-settings-feature-coverage.png
│   │   │   ├── web-settings-preferences.png
│   │   │   ├── web-settings-providers.png
│   │   │   ├── web-settings-workspace.png
│   │   │   ├── web-strategy-covered-call.png
│   │   │   ├── web-strategy-designer.png
│   │   │   ├── web-strategy-formula-workbench.png
│   │   │   ├── web-strategy-lab.png
│   │   │   ├── web-strategy-promotions.png
│   │   │   ├── web-strategy-quant-lab.png
│   │   │   ├── web-strategy-workspace.png
│   │   │   ├── web-trading-orders.png
│   │   │   ├── web-trading-positions.png
│   │   │   ├── web-trading-risk.png
│   │   │   └── web-trading-workspace.png
│   │   ├── README.md
│   │   └── web-audit.md
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
│   ├── start
│   │   └── README.md
│   ├── status
│   │   ├── evidence
│   │   │   ├── dk1-baseline-trust-thresholds.md
│   │   │   ├── dk1-pilot-parity-runbook.md
│   │   │   ├── dk1-trust-rationale-mapping.md
│   │   │   ├── wave2-cockpit-evidence-packet.md
│   │   │   └── wave4-evidence-template.md
│   │   ├── slo-reports
│   │   │   └── README.md
│   │   ├── accounting-productization-checklist.md
│   │   ├── ai-handoff-checklist-report.json
│   │   ├── ai-handoff-checklist-report.md
│   │   ├── ai-handoff-packet.json
│   │   ├── ai-handoff-packet.md
│   │   ├── ai-inventory-report.json
│   │   ├── ai-inventory-report.md
│   │   ├── api-contract-coverage-dashboard.json
│   │   ├── api-contract-coverage-dashboard.md
│   │   ├── api-docs-report.md
│   │   ├── badge-sync-report.md
│   │   ├── CHANGELOG.md
│   │   ├── contract-compatibility-matrix.md
│   │   ├── coverage-report.md
│   │   ├── doc-health-dashboard.json
│   │   ├── doc-health-dashboard.md
│   │   ├── docs-automation-summary.json
│   │   ├── docs-automation-summary.md
│   │   ├── evidence-continuity-dashboard.json
│   │   ├── evidence-continuity-dashboard.md
│   │   ├── example-validation.md
│   │   ├── FEATURE_INVENTORY.md
│   │   ├── governance-readiness-dashboard.json
│   │   ├── governance-readiness-dashboard.md
│   │   ├── kernel-readiness-dashboard.md
│   │   ├── link-repair-report.md
│   │   ├── metrics-dashboard.md
│   │   ├── paper-replay-reliability-dashboard.json
│   │   ├── paper-replay-reliability-dashboard.md
│   │   ├── pilot-readiness-dashboard.json
│   │   ├── pilot-readiness-dashboard.md
│   │   ├── program-state-summary.json
│   │   ├── program-state-summary.md
│   │   ├── prompt-route-lint-report.json
│   │   ├── provider-validation-matrix.md
│   │   ├── README.md
│   │   ├── ROADMAP.md
│   │   ├── ROADMAP_SUMMARY.md
│   │   ├── rules-report.md
│   │   ├── run-contract.schema.json
│   │   ├── todo-scan-results.json
│   │   ├── TODO.md
│   │   ├── workflow-drift-report.md
│   │   ├── workflow-manifest.json
│   │   ├── workflow-validation-summary.json
│   │   ├── workstation-cockpit-acceptance-matrix.json
│   │   ├── wpf-screen-development-tracker.json
│   │   └── wpf-screen-development-tracker.md
│   ├── testing
│   │   ├── README.md
│   │   ├── wave2-cockpit-reliability-evidence-runbook.md
│   │   ├── WAVE2_ACCEPTANCE_GATE_CHECKLIST.md
│   │   └── WAVE2_ACCEPTANCE_TESTS.md
│   ├── DEPENDENCIES.md
│   ├── documentation-inventory.md
│   ├── documentation-ownership.md
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
│   │   │   ├── formula.svg
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
│   │   │   ├── strategy-builder.svg
│   │   │   ├── strategy-runs.svg
│   │   │   ├── symbol-storage.svg
│   │   │   ├── symbols.svg
│   │   │   ├── system-health.svg
│   │   │   ├── trading-hours.svg
│   │   │   ├── trading.svg
│   │   │   └── watchlist.svg
│   │   └── app.ico
│   ├── components
│   │   ├── accounting
│   │   │   ├── accounting-fx-alloc.card.html
│   │   │   ├── accounting-ledger.card.html
│   │   │   ├── accounting-reconcile.card.html
│   │   │   ├── accounting-trial-aging.card.html
│   │   │   ├── accounting.card.html
│   │   │   ├── AccountTree.d.ts
│   │   │   ├── AccountTree.jsx
│   │   │   ├── AccountTree.prompt.md
│   │   │   ├── AgingTable.d.ts
│   │   │   ├── AgingTable.jsx
│   │   │   ├── AgingTable.prompt.md
│   │   │   ├── AllocationEditor.d.ts
│   │   │   ├── AllocationEditor.jsx
│   │   │   ├── AllocationEditor.prompt.md
│   │   │   ├── AmountCell.d.ts
│   │   │   ├── AmountCell.jsx
│   │   │   ├── AmountCell.prompt.md
│   │   │   ├── capital-commitments.card.html
│   │   │   ├── CommitmentBar.d.ts
│   │   │   ├── CommitmentBar.jsx
│   │   │   ├── CommitmentBar.prompt.md
│   │   │   ├── FxRevaluationTable.d.ts
│   │   │   ├── FxRevaluationTable.jsx
│   │   │   ├── FxRevaluationTable.prompt.md
│   │   │   ├── JournalEntryForm.d.ts
│   │   │   ├── JournalEntryForm.jsx
│   │   │   ├── JournalEntryForm.prompt.md
│   │   │   ├── LedgerTable.d.ts
│   │   │   ├── LedgerTable.jsx
│   │   │   ├── LedgerTable.prompt.md
│   │   │   ├── Money.d.ts
│   │   │   ├── money.js
│   │   │   ├── Money.jsx
│   │   │   ├── Money.prompt.md
│   │   │   ├── ReconciliationPanel.d.ts
│   │   │   ├── ReconciliationPanel.jsx
│   │   │   ├── ReconciliationPanel.prompt.md
│   │   │   ├── StatementTable.d.ts
│   │   │   ├── StatementTable.jsx
│   │   │   ├── StatementTable.prompt.md
│   │   │   ├── TaxLotTable.d.ts
│   │   │   ├── TaxLotTable.jsx
│   │   │   ├── TaxLotTable.prompt.md
│   │   │   ├── TrialBalance.d.ts
│   │   │   ├── TrialBalance.jsx
│   │   │   └── TrialBalance.prompt.md
│   │   ├── charts
│   │   │   ├── analytics-charts.card.html
│   │   │   ├── attribution-charts.card.html
│   │   │   ├── BarChart.d.ts
│   │   │   ├── BarChart.jsx
│   │   │   ├── BarChart.prompt.md
│   │   │   ├── CandleChart.d.ts
│   │   │   ├── CandleChart.jsx
│   │   │   ├── CandleChart.prompt.md
│   │   │   ├── chart-interaction.card.html
│   │   │   ├── ChartCard.d.ts
│   │   │   ├── ChartCard.jsx
│   │   │   ├── ChartCard.prompt.md
│   │   │   ├── charts.card.html
│   │   │   ├── ChartTooltip.d.ts
│   │   │   ├── ChartTooltip.jsx
│   │   │   ├── ChartTooltip.prompt.md
│   │   │   ├── CorrelationHeatmap.d.ts
│   │   │   ├── CorrelationHeatmap.jsx
│   │   │   ├── CorrelationHeatmap.prompt.md
│   │   │   ├── CoverageMatrix.d.ts
│   │   │   ├── CoverageMatrix.jsx
│   │   │   ├── CoverageMatrix.prompt.md
│   │   │   ├── DepthChart.d.ts
│   │   │   ├── DepthChart.jsx
│   │   │   ├── DepthChart.prompt.md
│   │   │   ├── DrawdownChart.d.ts
│   │   │   ├── DrawdownChart.jsx
│   │   │   ├── DrawdownChart.prompt.md
│   │   │   ├── EquityCurve.d.ts
│   │   │   ├── EquityCurve.jsx
│   │   │   ├── EquityCurve.prompt.md
│   │   │   ├── Histogram.d.ts
│   │   │   ├── Histogram.jsx
│   │   │   ├── Histogram.prompt.md
│   │   │   ├── market-coverage.card.html
│   │   │   ├── ownership-graph.card.html
│   │   │   ├── OwnershipGraph.d.ts
│   │   │   ├── OwnershipGraph.jsx
│   │   │   ├── OwnershipGraph.prompt.md
│   │   │   ├── pnl-calendar.card.html
│   │   │   ├── PnLCalendar.d.ts
│   │   │   ├── PnLCalendar.jsx
│   │   │   ├── PnLCalendar.prompt.md
│   │   │   ├── ScatterChart.d.ts
│   │   │   ├── ScatterChart.jsx
│   │   │   ├── ScatterChart.prompt.md
│   │   │   ├── Sparkline.d.ts
│   │   │   ├── Sparkline.jsx
│   │   │   ├── Sparkline.prompt.md
│   │   │   ├── TimeframeSwitcher.d.ts
│   │   │   ├── TimeframeSwitcher.jsx
│   │   │   ├── TimeframeSwitcher.prompt.md
│   │   │   ├── Treemap.d.ts
│   │   │   ├── Treemap.jsx
│   │   │   ├── Treemap.prompt.md
│   │   │   ├── useChartCrosshair.js
│   │   │   ├── WaterfallChart.d.ts
│   │   │   ├── WaterfallChart.jsx
│   │   │   ├── WaterfallChart.prompt.md
│   │   │   ├── yield-curve.card.html
│   │   │   ├── YieldCurve.d.ts
│   │   │   ├── YieldCurve.jsx
│   │   │   └── YieldCurve.prompt.md
│   │   ├── core
│   │   │   ├── Accordion.d.ts
│   │   │   ├── Accordion.jsx
│   │   │   ├── Accordion.prompt.md
│   │   │   ├── AsyncCombobox.d.ts
│   │   │   ├── AsyncCombobox.jsx
│   │   │   ├── AsyncCombobox.prompt.md
│   │   │   ├── Badge.d.ts
│   │   │   ├── Badge.jsx
│   │   │   ├── Badge.prompt.md
│   │   │   ├── Breadcrumb.d.ts
│   │   │   ├── Breadcrumb.jsx
│   │   │   ├── Breadcrumb.prompt.md
│   │   │   ├── Button.d.ts
│   │   │   ├── Button.jsx
│   │   │   ├── Button.prompt.md
│   │   │   ├── Button.stories.html
│   │   │   ├── Callout.d.ts
│   │   │   ├── Callout.jsx
│   │   │   ├── Callout.prompt.md
│   │   │   ├── Checkbox.d.ts
│   │   │   ├── Checkbox.jsx
│   │   │   ├── Checkbox.prompt.md
│   │   │   ├── Combobox.d.ts
│   │   │   ├── Combobox.jsx
│   │   │   ├── Combobox.prompt.md
│   │   │   ├── command-palette.card.html
│   │   │   ├── CommandPalette.d.ts
│   │   │   ├── CommandPalette.jsx
│   │   │   ├── CommandPalette.prompt.md
│   │   │   ├── context-validators.card.html
│   │   │   ├── ContextMenu.d.ts
│   │   │   ├── ContextMenu.jsx
│   │   │   ├── ContextMenu.prompt.md
│   │   │   ├── core.card.html
│   │   │   ├── DatePicker.d.ts
│   │   │   ├── DatePicker.jsx
│   │   │   ├── DatePicker.prompt.md
│   │   │   ├── DateRangePicker.d.ts
│   │   │   ├── DateRangePicker.jsx
│   │   │   ├── DateRangePicker.prompt.md
│   │   │   ├── Delta.d.ts
│   │   │   ├── Delta.jsx
│   │   │   ├── Delta.prompt.md
│   │   │   ├── DensityToggle.d.ts
│   │   │   ├── DensityToggle.jsx
│   │   │   ├── DensityToggle.prompt.md
│   │   │   ├── Dialog.d.ts
│   │   │   ├── Dialog.jsx
│   │   │   ├── Dialog.prompt.md
│   │   │   ├── Drawer.d.ts
│   │   │   ├── Drawer.jsx
│   │   │   ├── Drawer.prompt.md
│   │   │   ├── ErrorBoundary.d.ts
│   │   │   ├── ErrorBoundary.jsx
│   │   │   ├── ErrorBoundary.prompt.md
│   │   │   ├── Eyebrow.d.ts
│   │   │   ├── Eyebrow.jsx
│   │   │   ├── Eyebrow.prompt.md
│   │   │   ├── feedback-controls.card.html
│   │   │   ├── FileUpload.d.ts
│   │   │   ├── FileUpload.jsx
│   │   │   ├── FileUpload.prompt.md
│   │   │   ├── Flex.d.ts
│   │   │   ├── Flex.jsx
│   │   │   ├── Flex.prompt.md
│   │   │   ├── form-controls.card.html
│   │   │   ├── Form.d.ts
│   │   │   ├── Form.jsx
│   │   │   ├── Form.prompt.md
│   │   │   ├── FormField.d.ts
│   │   │   ├── FormField.jsx
│   │   │   ├── FormField.prompt.md
│   │   │   ├── forms-depth.card.html
│   │   │   ├── FormValidation.d.ts
│   │   │   ├── FormValidation.jsx
│   │   │   ├── FormValidation.prompt.md
│   │   │   ├── FreshnessIndicator.d.ts
│   │   │   ├── FreshnessIndicator.jsx
│   │   │   ├── FreshnessIndicator.prompt.md
│   │   │   ├── Gauge.d.ts
│   │   │   ├── Gauge.jsx
│   │   │   ├── Gauge.prompt.md
│   │   │   ├── Grid.d.ts
│   │   │   ├── Grid.jsx
│   │   │   ├── Grid.prompt.md
│   │   │   ├── HotkeysProvider.d.ts
│   │   │   ├── HotkeysProvider.jsx
│   │   │   ├── HotkeysProvider.prompt.md
│   │   │   ├── Input.d.ts
│   │   │   ├── Input.jsx
│   │   │   ├── Input.prompt.md
│   │   │   ├── Input.stories.html
│   │   │   ├── InstrumentChip.d.ts
│   │   │   ├── InstrumentChip.jsx
│   │   │   ├── InstrumentChip.prompt.md
│   │   │   ├── Kbd.d.ts
│   │   │   ├── Kbd.jsx
│   │   │   ├── Kbd.prompt.md
│   │   │   ├── layout-helpers.card.html
│   │   │   ├── LinearGauge.d.ts
│   │   │   ├── LinearGauge.jsx
│   │   │   ├── LinearGauge.prompt.md
│   │   │   ├── live-status.card.html
│   │   │   ├── modal.card.html
│   │   │   ├── Modal.d.ts
│   │   │   ├── Modal.jsx
│   │   │   ├── Modal.prompt.md
│   │   │   ├── MultiSelect.d.ts
│   │   │   ├── MultiSelect.jsx
│   │   │   ├── MultiSelect.prompt.md
│   │   │   ├── new-components.card.html
│   │   │   ├── new-primitives.card.html
│   │   │   ├── NotificationCenter.d.ts
│   │   │   ├── NotificationCenter.jsx
│   │   │   ├── NotificationCenter.prompt.md
│   │   │   ├── NumberInput.d.ts
│   │   │   ├── NumberInput.jsx
│   │   │   ├── NumberInput.prompt.md
│   │   │   ├── PanelSurface.d.ts
│   │   │   ├── PanelSurface.jsx
│   │   │   ├── PanelSurface.prompt.md
│   │   │   ├── Popover.d.ts
│   │   │   ├── Popover.jsx
│   │   │   ├── Popover.prompt.md
│   │   │   ├── ProgressBar.d.ts
│   │   │   ├── ProgressBar.jsx
│   │   │   ├── ProgressBar.prompt.md
│   │   │   ├── RadioGroup.d.ts
│   │   │   ├── RadioGroup.jsx
│   │   │   ├── RadioGroup.prompt.md
│   │   │   ├── SegmentedControl.d.ts
│   │   │   ├── SegmentedControl.jsx
│   │   │   ├── SegmentedControl.prompt.md
│   │   │   ├── Select.d.ts
│   │   │   ├── Select.jsx
│   │   │   ├── Select.prompt.md
│   │   │   ├── Slider.d.ts
│   │   │   ├── Slider.jsx
│   │   │   ├── Slider.prompt.md
│   │   │   ├── Spinner.d.ts
│   │   │   ├── Spinner.jsx
│   │   │   ├── Spinner.prompt.md
│   │   │   ├── SplitPane.d.ts
│   │   │   ├── SplitPane.jsx
│   │   │   ├── SplitPane.prompt.md
│   │   │   ├── Stack.d.ts
│   │   │   ├── Stack.jsx
│   │   │   ├── Stack.prompt.md
│   │   │   ├── StatusBanner.d.ts
│   │   │   ├── StatusBanner.jsx
│   │   │   ├── StatusBanner.prompt.md
│   │   │   ├── Stepper.d.ts
│   │   │   ├── Stepper.jsx
│   │   │   ├── Stepper.prompt.md
│   │   │   ├── Tabs.d.ts
│   │   │   ├── Tabs.jsx
│   │   │   ├── Tabs.prompt.md
│   │   │   ├── TagInput.d.ts
│   │   │   ├── TagInput.jsx
│   │   │   ├── TagInput.prompt.md
│   │   │   ├── TextArea.d.ts
│   │   │   ├── TextArea.jsx
│   │   │   ├── TextArea.prompt.md
│   │   │   ├── theming.card.html
│   │   │   ├── Timestamp.d.ts
│   │   │   ├── Timestamp.jsx
│   │   │   ├── Timestamp.prompt.md
│   │   │   ├── Toast.d.ts
│   │   │   ├── Toast.jsx
│   │   │   ├── Toast.prompt.md
│   │   │   ├── Tooltip.d.ts
│   │   │   ├── Tooltip.jsx
│   │   │   ├── Tooltip.prompt.md
│   │   │   ├── TreeView.d.ts
│   │   │   ├── TreeView.jsx
│   │   │   ├── TreeView.prompt.md
│   │   │   ├── useFormState.js
│   │   │   ├── useOverlayFocus.js
│   │   │   ├── Validators.d.ts
│   │   │   ├── Validators.js
│   │   │   └── workstation-services.card.html
│   │   ├── data
│   │   │   ├── BulkActionBar.d.ts
│   │   │   ├── BulkActionBar.jsx
│   │   │   ├── BulkActionBar.prompt.md
│   │   │   ├── ColumnChooser.d.ts
│   │   │   ├── ColumnChooser.jsx
│   │   │   ├── ColumnChooser.prompt.md
│   │   │   ├── ColumnManager.d.ts
│   │   │   ├── ColumnManager.jsx
│   │   │   ├── ColumnManager.prompt.md
│   │   │   ├── data-editing.card.html
│   │   │   ├── data-extras.card.html
│   │   │   ├── data.card.html
│   │   │   ├── DenseDataTable.d.ts
│   │   │   ├── DenseDataTable.jsx
│   │   │   ├── DenseDataTable.prompt.md
│   │   │   ├── EditableCell.d.ts
│   │   │   ├── EditableCell.jsx
│   │   │   ├── EditableCell.prompt.md
│   │   │   ├── EmptyState.d.ts
│   │   │   ├── EmptyState.jsx
│   │   │   ├── EmptyState.prompt.md
│   │   │   ├── EntitySummary.d.ts
│   │   │   ├── EntitySummary.jsx
│   │   │   ├── EntitySummary.prompt.md
│   │   │   ├── expandable-table.card.html
│   │   │   ├── ExpandableDataTable.d.ts
│   │   │   ├── ExpandableDataTable.jsx
│   │   │   ├── ExpandableDataTable.prompt.md
│   │   │   ├── filter-builder.card.html
│   │   │   ├── FilterBuilder.d.ts
│   │   │   ├── FilterBuilder.jsx
│   │   │   ├── FilterBuilder.prompt.md
│   │   │   ├── filtered-data-table.card.html
│   │   │   ├── FilteredDataTable.d.ts
│   │   │   ├── FilteredDataTable.jsx
│   │   │   ├── FilteredDataTable.prompt.md
│   │   │   ├── KeyValueGrid.d.ts
│   │   │   ├── KeyValueGrid.jsx
│   │   │   ├── KeyValueGrid.prompt.md
│   │   │   ├── MetricCard.d.ts
│   │   │   ├── MetricCard.jsx
│   │   │   ├── MetricCard.prompt.md
│   │   │   ├── pagination.card.html
│   │   │   ├── Pagination.d.ts
│   │   │   ├── Pagination.jsx
│   │   │   ├── Pagination.prompt.md
│   │   │   ├── saved-views.card.html
│   │   │   ├── SavedViews.d.ts
│   │   │   ├── SavedViews.jsx
│   │   │   ├── SavedViews.prompt.md
│   │   │   ├── SelectionToolbar.d.ts
│   │   │   ├── SelectionToolbar.jsx
│   │   │   ├── SelectionToolbar.prompt.md
│   │   │   ├── Skeleton.d.ts
│   │   │   ├── Skeleton.jsx
│   │   │   ├── Skeleton.prompt.md
│   │   │   ├── table-depth.card.html
│   │   │   ├── TableHooks.js
│   │   │   ├── Toolbar.d.ts
│   │   │   ├── Toolbar.jsx
│   │   │   ├── Toolbar.prompt.md
│   │   │   ├── useAsyncTableData.js
│   │   │   ├── useRowSelection.js
│   │   │   ├── useTableColumns.js
│   │   │   ├── useTableState.js
│   │   │   ├── useThemeRowHeight.js
│   │   │   ├── VirtualizedList.d.ts
│   │   │   ├── VirtualizedList.jsx
│   │   │   ├── VirtualizedList.prompt.md
│   │   │   ├── worksheet-grid.card.html
│   │   │   ├── WorksheetGrid.d.ts
│   │   │   ├── WorksheetGrid.jsx
│   │   │   └── WorksheetGrid.prompt.md
│   │   ├── operations
│   │   │   ├── case-triage.card.html
│   │   │   ├── CaseQueue.d.ts
│   │   │   ├── CaseQueue.jsx
│   │   │   ├── CaseQueue.prompt.md
│   │   │   ├── diff-view.card.html
│   │   │   ├── DiffView.d.ts
│   │   │   ├── DiffView.jsx
│   │   │   ├── DiffView.prompt.md
│   │   │   ├── EventTimeline.d.ts
│   │   │   ├── EventTimeline.jsx
│   │   │   ├── EventTimeline.prompt.md
│   │   │   ├── evidence-surfaces.card.html
│   │   │   ├── EvidenceLink.d.ts
│   │   │   ├── EvidenceLink.jsx
│   │   │   ├── EvidenceLink.prompt.md
│   │   │   ├── GateRail.d.ts
│   │   │   ├── GateRail.jsx
│   │   │   ├── GateRail.prompt.md
│   │   │   ├── LogTail.d.ts
│   │   │   ├── LogTail.jsx
│   │   │   ├── LogTail.prompt.md
│   │   │   ├── operations.card.html
│   │   │   ├── ProvenanceChip.d.ts
│   │   │   ├── ProvenanceChip.jsx
│   │   │   ├── ProvenanceChip.prompt.md
│   │   │   ├── ReadinessPanel.d.ts
│   │   │   ├── ReadinessPanel.jsx
│   │   │   ├── ReadinessPanel.prompt.md
│   │   │   ├── SeverityBadge.d.ts
│   │   │   ├── SeverityBadge.jsx
│   │   │   ├── SeverityBadge.prompt.md
│   │   │   ├── SlaChip.d.ts
│   │   │   ├── SlaChip.jsx
│   │   │   ├── SlaChip.prompt.md
│   │   │   ├── status.js
│   │   │   ├── TrustStrip.d.ts
│   │   │   ├── TrustStrip.jsx
│   │   │   ├── TrustStrip.prompt.md
│   │   │   ├── ValidationIssueList.d.ts
│   │   │   ├── ValidationIssueList.jsx
│   │   │   ├── ValidationIssueList.prompt.md
│   │   │   ├── WorkspaceSection.d.ts
│   │   │   ├── WorkspaceSection.jsx
│   │   │   └── WorkspaceSection.prompt.md
│   │   ├── shell
│   │   │   ├── NavRail.d.ts
│   │   │   ├── NavRail.jsx
│   │   │   ├── NavRail.prompt.md
│   │   │   ├── SessionControls.d.ts
│   │   │   ├── SessionControls.jsx
│   │   │   ├── SessionControls.prompt.md
│   │   │   ├── shell.card.html
│   │   │   ├── StatusBar.d.ts
│   │   │   ├── StatusBar.jsx
│   │   │   ├── StatusBar.prompt.md
│   │   │   ├── WorkstationTopbar.d.ts
│   │   │   ├── WorkstationTopbar.jsx
│   │   │   └── WorkstationTopbar.prompt.md
│   │   └── trading
│   │       ├── Blotter.d.ts
│   │       ├── Blotter.jsx
│   │       ├── Blotter.prompt.md
│   │       ├── depth-ladder.card.html
│   │       ├── DepthLadder.d.ts
│   │       ├── DepthLadder.jsx
│   │       ├── DepthLadder.prompt.md
│   │       ├── FillsFeed.d.ts
│   │       ├── FillsFeed.jsx
│   │       ├── FillsFeed.prompt.md
│   │       ├── option-chain.card.html
│   │       ├── OptionChainTable.d.ts
│   │       ├── OptionChainTable.jsx
│   │       ├── OptionChainTable.prompt.md
│   │       ├── OrderTicket.d.ts
│   │       ├── OrderTicket.jsx
│   │       ├── OrderTicket.prompt.md
│   │       └── trading.card.html
│   ├── docs
│   │   ├── changelog
│   │   │   ├── README.md
│   │   │   └── SYSTEM_STATE_2026-06.md
│   │   ├── COMPONENT_API_REFERENCE.card.html
│   │   ├── contract-audit.card.html
│   │   ├── CONTRACT_AUDIT.md
│   │   ├── cookbook.card.html
│   │   ├── COOKBOOK.md
│   │   ├── GETTING_STARTED.md
│   │   ├── PERFORMANCE.md
│   │   ├── system-map.card.html
│   │   └── UPGRADING.md
│   ├── guidelines
│   │   ├── accessibility.card.html
│   │   ├── ACCESSIBILITY.md
│   │   ├── brand-hero.card.html
│   │   ├── brand-icons.card.html
│   │   ├── brand-marks.card.html
│   │   ├── colors-accent.card.html
│   │   ├── colors-charts.card.html
│   │   ├── colors-modes.card.html
│   │   ├── colors-states.card.html
│   │   ├── colors-surfaces.card.html
│   │   ├── CONTENT_FUNDAMENTALS.md
│   │   ├── dark-mode-validation.card.html
│   │   ├── depth-stacking.card.html
│   │   ├── depth-surfaces.card.html
│   │   ├── entity-schemas.card.html
│   │   ├── ENTITY_SCHEMAS.md
│   │   ├── ICONOGRAPHY.md
│   │   ├── keyboard-map.card.html
│   │   ├── motion.card.html
│   │   ├── spacing-density.card.html
│   │   ├── spacing-radii.card.html
│   │   ├── states-antipatterns.card.html
│   │   ├── STATES_AND_ANTIPATTERNS.md
│   │   ├── token-reference.card.html
│   │   ├── TOKEN_REFERENCE.md
│   │   ├── type-numerals.card.html
│   │   ├── type-scale.card.html
│   │   ├── type-stack.card.html
│   │   ├── VISUAL_FOUNDATIONS.md
│   │   └── WORKSTATION_BLUEPRINT.md
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
│   ├── scraps
│   │   ├── acct-ledger.png
│   │   └── acct-template-check.png
│   ├── screenshots
│   │   ├── core-top.png
│   │   └── gallery-01.png
│   ├── scripts
│   │   ├── check_contrast.py
│   │   ├── check_design_system_governance.py
│   │   ├── create-workstation.sh
│   │   ├── governance-baseline.json
│   │   └── visual_diff.py
│   ├── src
│   │   └── Meridian.Ui
│   │       └── dashboard
│   │           └── src
│   │               └── styles
│   │                   └── index.css
│   ├── templates
│   │   ├── accounting-workstation
│   │   │   ├── views
│   │   │   │   ├── AccountsView.jsx
│   │   │   │   ├── FxRevaluationView.jsx
│   │   │   │   ├── JournalDrawer.jsx
│   │   │   │   ├── LedgerView.jsx
│   │   │   │   ├── PeriodCloseView.jsx
│   │   │   │   ├── ReconciliationView.jsx
│   │   │   │   ├── StatementsView.jsx
│   │   │   │   ├── TaxLotsView.jsx
│   │   │   │   └── TrialBalanceView.jsx
│   │   │   ├── .thumbnail
│   │   │   ├── AccountingWorkstation.dc.html
│   │   │   ├── data.js
│   │   │   ├── ds-base.js
│   │   │   ├── screen.jsx
│   │   │   └── support.js
│   │   ├── alerting-workstation
│   │   │   ├── .thumbnail
│   │   │   ├── AlertingWorkstation.dc.html
│   │   │   ├── ds-base.js
│   │   │   ├── screen.jsx
│   │   │   └── support.js
│   │   ├── amx-governance
│   │   │   ├── .thumbnail
│   │   │   ├── AmxGovernance.dc.html
│   │   │   ├── ds-base.js
│   │   │   ├── screen.jsx
│   │   │   └── support.js
│   │   ├── backtest-builder
│   │   │   ├── .thumbnail
│   │   │   ├── BacktestBuilder.dc.html
│   │   │   ├── ds-base.js
│   │   │   ├── screen.jsx
│   │   │   └── support.js
│   │   ├── backtest-compare
│   │   │   ├── .thumbnail
│   │   │   ├── BacktestCompare.dc.html
│   │   │   ├── ds-base.js
│   │   │   ├── screen.jsx
│   │   │   └── support.js
│   │   ├── basket-builder
│   │   │   ├── .thumbnail
│   │   │   ├── basket-data.js
│   │   │   ├── BasketBuilder.dc.html
│   │   │   ├── ds-base.js
│   │   │   ├── screen.jsx
│   │   │   └── support.js
│   │   ├── blank-workstation
│   │   │   ├── .thumbnail
│   │   │   ├── BlankWorkstation.dc.html
│   │   │   ├── ds-base.js
│   │   │   └── support.js
│   │   ├── charting-workstation
│   │   │   ├── .thumbnail
│   │   │   ├── ChartingWorkstation.dc.html
│   │   │   ├── ds-base.js
│   │   │   ├── index.html
│   │   │   ├── screen.jsx
│   │   │   └── support.js
│   │   ├── covered-call-lab
│   │   │   ├── .thumbnail
│   │   │   ├── CoveredCallLab.dc.html
│   │   │   ├── ds-base.js
│   │   │   ├── screen.jsx
│   │   │   └── support.js
│   │   ├── dashboard-workstation
│   │   │   ├── .thumbnail
│   │   │   ├── DashboardWorkstation.dc.html
│   │   │   ├── ds-base.js
│   │   │   ├── index.html
│   │   │   ├── screen.jsx
│   │   │   └── support.js
│   │   ├── family-office
│   │   │   ├── .thumbnail
│   │   │   ├── ds-base.js
│   │   │   ├── FamilyOffice.dc.html
│   │   │   ├── screen.jsx
│   │   │   └── support.js
│   │   ├── field-formula
│   │   │   ├── .thumbnail
│   │   │   ├── ds-base.js
│   │   │   ├── FieldFormula.dc.html
│   │   │   ├── screen.jsx
│   │   │   └── support.js
│   │   ├── ingestion-operations
│   │   │   ├── .thumbnail
│   │   │   ├── ds-base.js
│   │   │   ├── IngestionOperations.dc.html
│   │   │   ├── screen.jsx
│   │   │   └── support.js
│   │   ├── journaling-workstation
│   │   │   ├── .thumbnail
│   │   │   ├── ds-base.js
│   │   │   ├── JournalingWorkstation.dc.html
│   │   │   ├── screen.jsx
│   │   │   └── support.js
│   │   ├── reconciliation-workstation
│   │   │   ├── .thumbnail
│   │   │   ├── ds-base.js
│   │   │   ├── ReconciliationWorkstation.dc.html
│   │   │   ├── screen.jsx
│   │   │   └── support.js
│   │   ├── report-library
│   │   │   ├── .thumbnail
│   │   │   ├── ds-base.js
│   │   │   ├── ReportLibrary.dc.html
│   │   │   ├── screen.jsx
│   │   │   └── support.js
│   │   ├── report-pack
│   │   │   ├── .thumbnail
│   │   │   ├── ds-base.js
│   │   │   ├── ReportPack.dc.html
│   │   │   └── support.js
│   │   ├── report-scheduler
│   │   │   ├── .thumbnail
│   │   │   ├── ds-base.js
│   │   │   ├── ReportScheduler.dc.html
│   │   │   ├── screen.jsx
│   │   │   └── support.js
│   │   ├── security-master-registry
│   │   │   ├── .thumbnail
│   │   │   ├── ds-base.js
│   │   │   ├── index.html
│   │   │   ├── screen.jsx
│   │   │   ├── securities-data.js
│   │   │   ├── SecurityMasterRegistry.dc.html
│   │   │   └── support.js
│   │   ├── session-start
│   │   │   ├── .thumbnail
│   │   │   ├── ds-base.js
│   │   │   ├── screen.jsx
│   │   │   ├── SessionStart.dc.html
│   │   │   └── support.js
│   │   ├── settings-admin
│   │   │   ├── .thumbnail
│   │   │   ├── ds-base.js
│   │   │   ├── screen.jsx
│   │   │   ├── SettingsAdmin.dc.html
│   │   │   └── support.js
│   │   ├── split-pane-reconciliation
│   │   │   ├── ds-base.js
│   │   │   ├── screen.jsx
│   │   │   ├── Split Pane Reconciliation.dc.html
│   │   │   └── support.js
│   │   ├── strategy-builder
│   │   │   ├── .thumbnail
│   │   │   ├── ds-base.js
│   │   │   ├── screen.jsx
│   │   │   ├── StrategyBuilder.dc.html
│   │   │   └── support.js
│   │   ├── strategy-onboarding
│   │   │   ├── .thumbnail
│   │   │   ├── ds-base.js
│   │   │   ├── screen.jsx
│   │   │   ├── StrategyOnboarding.dc.html
│   │   │   └── support.js
│   │   ├── strategy-runs
│   │   │   ├── .thumbnail
│   │   │   ├── ds-base.js
│   │   │   ├── screen.jsx
│   │   │   ├── StrategyRuns.dc.html
│   │   │   └── support.js
│   │   └── trading-desk
│   │       ├── .thumbnail
│   │       ├── ds-base.js
│   │       ├── screen.jsx
│   │       ├── support.js
│   │       └── TradingDesk.dc.html
│   ├── tests
│   │   ├── gallery.html
│   │   ├── qa-hifi-pass.html
│   │   ├── smoke.html
│   │   ├── test_contrast.py
│   │   ├── test_design_system_governance.py
│   │   ├── unit-tests.html
│   │   └── verify-new-components.html
│   ├── tokens
│   │   ├── base.css
│   │   ├── colors-dark.css
│   │   ├── colors.css
│   │   ├── contrast-modes.css
│   │   ├── dark-mode.card.html
│   │   ├── elevation.css
│   │   ├── fonts.css
│   │   ├── playground.card.html
│   │   ├── print.css
│   │   ├── theme.css
│   │   ├── theming.card.html
│   │   ├── token-browser.card.html
│   │   ├── typography.css
│   │   └── white-label.card.html
│   ├── ui_kits
│   │   ├── accounting
│   │   │   └── index.html
│   │   ├── dashboard
│   │   │   ├── components.jsx
│   │   │   ├── index.html
│   │   │   └── README.md
│   │   ├── plottool
│   │   │   └── index.html
│   │   ├── security-master
│   │   │   └── index.html
│   │   ├── plottool_workstation.html
│   │   ├── security_master-company.html
│   │   ├── security_master-print.html
│   │   └── security_master.html
│   ├── uploads
│   │   └── ChatGPT Image Apr 24, 2026, 03_58_29 PM.png
│   ├── .designsystem-cleaned
│   ├── .thumbnail
│   ├── _adherence.oxlintrc.json
│   ├── _ds_bundle.js
│   ├── _ds_manifest.json
│   ├── Accounting Reconciliation (standalone).html
│   ├── BRAND_GUIDELINES.md
│   ├── Canvas.dc.html
│   ├── CHANGELOG.md
│   ├── colors_and_type.css
│   ├── CONTENT_FUNDAMENTALS.md
│   ├── governance-baseline.json
│   ├── ICONOGRAPHY.md
│   ├── index.html
│   ├── INSPIRATION_BRIEF.md
│   ├── PATTERNS.md
│   ├── README.md
│   ├── SKILL.md
│   ├── styles.css
│   ├── support.js
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
│   │   │   ├── desktop-development.json
│   │   │   ├── desktop-production.json
│   │   │   ├── manual-accounting.json
│   │   │   ├── manual-data.json
│   │   │   ├── manual-overview.json
│   │   │   ├── manual-strategy-and-trading.json
│   │   │   └── screenshot-catalog.json
│   │   ├── build-ibapi-smoke.ps1
│   │   ├── build-ibapi-vendor.ps1
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
│   │   ├── run-local-quality.ps1
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
│   ├── ci.sh
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
│   │   ├── Integrations
│   │   │   └── Lean
│   │   │       ├── MeridianDataProvider.cs
│   │   │       ├── MeridianQuoteData.cs
│   │   │       ├── MeridianTradeData.cs
│   │   │       ├── README.md
│   │   │       └── SampleLeanAlgorithm.cs
│   │   ├── Tools
│   │   │   └── DataValidator.cs
│   │   ├── ApiHostOptions.cs
│   │   ├── app.ico
│   │   ├── app.manifest
│   │   ├── DashboardServerBridge.cs
│   │   ├── DemoWorkspaceCli.cs
│   │   ├── GlobalUsings.cs
│   │   ├── HostedBrokerageGatewayRuntimeSurfaceCatalog.cs
│   │   ├── HostedBrokerageGatewayServiceCollectionExtensions.cs
│   │   ├── LiveTradingEngineHostServiceCollectionExtensions.cs
│   │   ├── Meridian.csproj
│   │   ├── Program.cs
│   │   ├── README.md
│   │   ├── runtimeconfig.template.json
│   │   └── UiServer.cs
│   ├── Meridian.Application
│   │   ├── Accounting
│   │   │   ├── DailyMarkToMarketService.cs
│   │   │   ├── HistoricalCloseMarkPriceSource.cs
│   │   │   ├── RegisteredHistoricalCloseMarkPriceSource.cs
│   │   │   └── WaterfallMarkPriceSource.cs
│   │   ├── Backfill
│   │   │   ├── AutoGapRemediationService.cs
│   │   │   ├── BackfillCoordinator.cs
│   │   │   ├── BackfillCoordinatorExecutionGateway.cs
│   │   │   ├── BackfillCostEstimator.cs
│   │   │   ├── BackfillPartitionPlanner.cs
│   │   │   ├── BackfillPreview.cs
│   │   │   ├── BackfillRequest.cs
│   │   │   ├── BackfillSymbolNormalizer.cs
│   │   │   ├── CrossSourceBackfillReconciliationService.cs
│   │   │   ├── GapBackfillService.cs
│   │   │   ├── HistoricalBackfillService.cs
│   │   │   └── IBackfillExecutionGateway.cs
│   │   ├── Commands
│   │   │   ├── CatalogCommand.cs
│   │   │   ├── CliArguments.cs
│   │   │   ├── CliCommandRouteTable.cs
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
│   │   │   │   ├── CommandServiceRegistration.cs
│   │   │   │   ├── EventPipelineShutdownParticipant.cs
│   │   │   │   ├── HostModeOrchestrator.cs
│   │   │   │   ├── LifecycleSupervisorBridgeHostedService.cs
│   │   │   │   ├── RuntimeReadinessService.cs
│   │   │   │   ├── RuntimeShutdownSequence.cs
│   │   │   │   ├── SharedStartupBootstrapper.cs
│   │   │   │   ├── SharedStartupHelpers.cs
│   │   │   │   ├── StartupOrchestrator.cs
│   │   │   │   └── StartupValidationRunner.cs
│   │   │   ├── AssetOperationsStartup.cs
│   │   │   ├── BankingStartup.cs
│   │   │   ├── CircuitBreakerCallbackRouter.cs
│   │   │   ├── DatabaseMigrationReadinessReceipt.cs
│   │   │   ├── DirectLendingStartup.cs
│   │   │   ├── FundAccountsStartup.cs
│   │   │   ├── FundStructureStartup.cs
│   │   │   ├── HostAdapters.cs
│   │   │   ├── HostStartup.cs
│   │   │   ├── LedgerStartup.cs
│   │   │   ├── LegacySnapshotArchiver.cs
│   │   │   ├── MeridianDeploymentPosture.cs
│   │   │   ├── MoneyMarketStartup.cs
│   │   │   ├── PersistenceConfigurationStatus.cs
│   │   │   ├── ProductionRegistrationGuardService.cs
│   │   │   ├── ProductionServiceRegistrationPolicy.cs
│   │   │   ├── SecurityMasterStartup.cs
│   │   │   └── ServiceCompositionRoot.cs
│   │   ├── Config
│   │   │   ├── Credentials
│   │   │   │   ├── CredentialTestingService.cs
│   │   │   │   ├── OAuthTokenRefreshService.cs
│   │   │   │   └── ProviderCredentialResolver.cs
│   │   │   ├── ConfigurationPipeline.cs
│   │   │   ├── ConfigurationResolutionRules.cs
│   │   │   ├── ConfigValidatorCli.cs
│   │   │   └── IBGatewayProbe.cs
│   │   ├── DataQuality
│   │   │   ├── CompositeDataQualityReadService.cs
│   │   │   └── QualityMonitoringPublisher.cs
│   │   ├── DirectLending
│   │   │   ├── AccrualLedgerService.cs
│   │   │   ├── DailyAccrualWorker.cs
│   │   │   ├── DirectLendingEventRebuilder.cs
│   │   │   ├── DirectLendingOutboxDispatcher.cs
│   │   │   ├── DirectLendingServicerStatementService.cs
│   │   │   ├── DirectLendingServiceSupport.cs
│   │   │   ├── DirectLendingWorkflowSupport.cs
│   │   │   ├── DirectLendingWorkflowTopics.cs
│   │   │   ├── IAccrualLedgerService.cs
│   │   │   ├── IDirectLendingCommandService.cs
│   │   │   ├── IDirectLendingQueryService.cs
│   │   │   ├── IDirectLendingService.cs
│   │   │   ├── IDirectLendingServicerStatementService.cs
│   │   │   ├── InMemoryDirectLendingService.cs
│   │   │   ├── InMemoryDirectLendingService.Workflows.cs
│   │   │   ├── LoanAccountingProjector.cs
│   │   │   ├── PostgresDirectLendingCommandService.cs
│   │   │   ├── PostgresDirectLendingQueryService.cs
│   │   │   └── PostgresDirectLendingService.cs
│   │   ├── FundStructure
│   │   │   ├── GovernanceSharedDataAccessService.cs
│   │   │   ├── InMemoryFundStructureService.cs
│   │   │   ├── InMemoryFundStructureService.Persistence.cs
│   │   │   ├── OwnershipGraphValidation.cs
│   │   │   └── PostgresFundStructureService.cs
│   │   ├── Http
│   │   │   ├── Endpoints
│   │   │   │   └── StatusEndpointHandlers.cs
│   │   │   └── ConfigStore.cs
│   │   ├── Integrations
│   │   │   ├── ProviderIntegrationActivationReadinessService.cs
│   │   │   ├── ProviderIntegrationActivationService.cs
│   │   │   ├── ProviderIntegrationDryRunService.cs
│   │   │   ├── ProviderIntegrationHttpClientTransport.cs
│   │   │   ├── ProviderIntegrationIdentityResolutionPreviewService.cs
│   │   │   ├── ProviderIntegrationMappedRecordIdentity.cs
│   │   │   ├── ProviderIntegrationMappedRecordValidation.cs
│   │   │   ├── ProviderIntegrationMonitoringService.cs
│   │   │   ├── ProviderIntegrationOpenApiImportService.cs
│   │   │   ├── ProviderIntegrationPromotionReadinessService.cs
│   │   │   ├── ProviderIntegrationQuarantineReplayService.cs
│   │   │   ├── ProviderIntegrationQuarantineReviewService.cs
│   │   │   ├── ProviderIntegrationReconciliationHandoffService.cs
│   │   │   ├── ProviderIntegrationRestDryRunService.cs
│   │   │   ├── ProviderIntegrationSchemaDriftService.cs
│   │   │   ├── ProviderIntegrationServiceBoundary.cs
│   │   │   ├── ProviderIntegrationSetupService.cs
│   │   │   ├── ProviderIntegrationSetupValidation.cs
│   │   │   ├── ProviderIntegrationStagingDedupeValidator.cs
│   │   │   ├── ProviderIntegrationStagingReviewService.cs
│   │   │   ├── ProviderIntegrationSyncOrchestrationService.cs
│   │   │   ├── ProviderIntegrationSyncPlanningService.cs
│   │   │   └── ProviderIntegrationTemplateCatalog.cs
│   │   ├── Monitoring
│   │   │   ├── DetailedHealthCheck.cs
│   │   │   ├── PrometheusMetrics.cs
│   │   │   ├── StatusHttpServer.cs
│   │   │   ├── StatusSnapshot.cs
│   │   │   └── StatusWriter.cs
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
│   │   │   └── PersistentDedupLedger.cs
│   │   ├── ProviderRouting
│   │   │   ├── BestOfBreedProviderSelector.cs
│   │   │   ├── KernelObservabilityService.cs
│   │   │   ├── ProviderBindingService.cs
│   │   │   ├── ProviderConnectionService.cs
│   │   │   ├── ProviderInstrumentCapabilityMatrixService.cs
│   │   │   ├── ProviderOperationsSupportServices.cs
│   │   │   ├── ProviderRoutingEngine.cs
│   │   │   ├── ProviderRoutingMapper.cs
│   │   │   └── ProviderSetupService.cs
│   │   ├── Reconciliation
│   │   │   └── RetainedInternalReconciliationPopulationProvider.cs
│   │   ├── Scheduling
│   │   │   ├── BackfillExecutionLog.cs
│   │   │   ├── BackfillSchedule.cs
│   │   │   ├── BackfillScheduleManager.cs
│   │   │   └── ScheduledBackfillService.cs
│   │   ├── SecurityMaster
│   │   │   ├── CashFlow
│   │   │   │   ├── StructuredCashFlowLedgerBridge.cs
│   │   │   │   └── StructuredCashFlowLedgerGate.cs
│   │   │   ├── CorporateActions
│   │   │   │   ├── CorporateActionCommandService.cs
│   │   │   │   ├── CorporateActionInboxState.cs
│   │   │   │   ├── CorporateActionIngestOrchestrator.cs
│   │   │   │   ├── CorporateActionRestatementTrigger.cs
│   │   │   │   ├── CorporateActionValidation.cs
│   │   │   │   ├── ILivePositionCorporateActionAdjuster.cs
│   │   │   │   ├── SecurityMasterCorporateActionCommandService.cs
│   │   │   │   └── SecurityMasterTickerChangeService.cs
│   │   │   ├── Rebuild
│   │   │   │   ├── IUflProjectionRebuilder.cs
│   │   │   │   ├── SecurityMasterAggregateRebuilder.cs
│   │   │   │   ├── SecurityMasterRebuildOrchestrator.cs
│   │   │   │   ├── SecurityProjectionRebuildHandler.cs
│   │   │   │   └── UflProjectionRebuilder.cs
│   │   │   ├── Validation
│   │   │   │   ├── AssetClassValidatorRegistry.cs
│   │   │   │   ├── FileSecurityValidationSnapshotStore.cs
│   │   │   │   ├── SecurityMasterOptionsValidator.cs
│   │   │   │   ├── SecurityValidationGateService.cs
│   │   │   │   └── SecurityValidationService.cs
│   │   │   ├── CanonicalRegistryCoverageSource.cs
│   │   │   ├── CanonicalSymbolRegistryMigrationService.cs
│   │   │   ├── ConfiguredSymbolCoverageSource.cs
│   │   │   ├── CoverageInvalidationHandler.cs
│   │   │   ├── DataVendorEntitlementService.cs
│   │   │   ├── EdgarIngestOrchestrator.cs
│   │   │   ├── FaceValueLotExtensions.cs
│   │   │   ├── IAffectedLedgerBookResolver.cs
│   │   │   ├── IEdgarIngestOrchestrator.cs
│   │   │   ├── ILedgerPeriodLockReader.cs
│   │   │   ├── IMultiAssetCoverageInvalidator.cs
│   │   │   ├── IPeriodAwareRestatementResolver.cs
│   │   │   ├── ISecurityMasterConflictAuthorityPolicy.cs
│   │   │   ├── ISecurityMasterQueryService.cs
│   │   │   ├── ISecurityMasterRevisionPublishedHandler.cs
│   │   │   ├── ISecurityMasterRevisionStore.cs
│   │   │   ├── ISecurityMasterWorkbenchCommandService.cs
│   │   │   ├── ISecurityMasterWorkbenchQueryService.cs
│   │   │   ├── ISecurityResolver.cs
│   │   │   ├── NullSecurityMasterClearwaterServices.cs
│   │   │   ├── NullSecurityMasterServices.cs
│   │   │   ├── PostgresSecurityMasterConflictService.cs
│   │   │   ├── PostgresSecurityMasterRevisionStore.cs
│   │   │   ├── SecurityAssetProfileGovernanceService.cs
│   │   │   ├── SecurityEconomicDefinitionAdapter.cs
│   │   │   ├── SecurityMasterAmortizationLedgerBridge.cs
│   │   │   ├── SecurityMasterCanonicalSymbolSeedService.cs
│   │   │   ├── SecurityMasterCashFlowService.cs
│   │   │   ├── SecurityMasterConcurrencyException.cs
│   │   │   ├── SecurityMasterConflictAuthorityPolicy.cs
│   │   │   ├── SecurityMasterConflictDetection.cs
│   │   │   ├── SecurityMasterConflictService.cs
│   │   │   ├── SecurityMasterContractAliases.cs
│   │   │   ├── SecurityMasterCostBasisAdjustmentService.cs
│   │   │   ├── SecurityMasterCsvParser.cs
│   │   │   ├── SecurityMasterDataQualityService.cs
│   │   │   ├── SecurityMasterDraftProposalService.cs
│   │   │   ├── SecurityMasterHistoricalSymbolTimelineResolver.cs
│   │   │   ├── SecurityMasterImportService.cs
│   │   │   ├── SecurityMasterIngestStatusService.cs
│   │   │   ├── SecurityMasterLedgerBridge.cs
│   │   │   ├── SecurityMasterMapping.cs
│   │   │   ├── SecurityMasterOperationalReadinessService.cs
│   │   │   ├── SecurityMasterPricingService.cs
│   │   │   ├── SecurityMasterProjectionService.cs
│   │   │   ├── SecurityMasterProjectionWarmupService.cs
│   │   │   ├── SecurityMasterPublishFailedException.cs
│   │   │   ├── SecurityMasterQueryService.cs
│   │   │   ├── SecurityMasterService.cs
│   │   │   ├── SecurityMasterWorkbenchCommandService.cs
│   │   │   ├── SecurityMasterWorkbenchOptions.cs
│   │   │   ├── SecurityResolver.cs
│   │   │   └── SymbolResolutionMismatchTracker.cs
│   │   ├── Services
│   │   │   ├── AutoConfigurationService.cs
│   │   │   ├── ConfigurationService.cs
│   │   │   ├── ConfigurationServiceCredentialAdapter.cs
│   │   │   ├── ConfigurationWizard.cs
│   │   │   ├── ConnectivityProbeService.cs
│   │   │   ├── ConnectivityTestService.cs
│   │   │   ├── CredentialValidationService.cs
│   │   │   ├── DailySummaryWebhook.cs
│   │   │   ├── DryRunService.cs
│   │   │   ├── ExecutionSimulationOrchestrator.cs
│   │   │   ├── PreflightChecker.cs
│   │   │   ├── ServiceRegistry.cs
│   │   │   └── StoredProviderCredentialResolver.cs
│   │   ├── Subscriptions
│   │   │   ├── Services
│   │   │   │   ├── AutoResubscribePolicy.cs
│   │   │   │   ├── BatchOperationsService.cs
│   │   │   │   ├── CorruptStoreQuarantine.cs
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
│   │   ├── GlobalUsings.SecurityMasterConcerns.cs
│   │   ├── Meridian.Application.csproj
│   │   └── README.md
│   ├── Meridian.Audit
│   │   ├── Compliance
│   │   │   ├── ComplianceModels.cs
│   │   │   └── ComplianceServices.cs
│   │   ├── DesignModule.cs
│   │   ├── Meridian.Audit.csproj
│   │   └── README.md
│   ├── Meridian.Backtesting
│   │   ├── Engine
│   │   │   ├── BacktestContext.cs
│   │   │   ├── BacktestEngine.cs
│   │   │   ├── ContingentOrderManager.cs
│   │   │   ├── DelistingMonitor.cs
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
│   │   │   ├── PluginBacktestStrategyLiveSource.cs
│   │   │   └── StrategyPluginLoader.cs
│   │   ├── Portfolio
│   │   │   ├── ICommissionModel.cs
│   │   │   ├── LinkedListExtensions.cs
│   │   │   └── SimulatedPortfolio.cs
│   │   ├── WalkForward
│   │   │   ├── WalkForwardContracts.cs
│   │   │   └── WalkForwardService.cs
│   │   ├── BacktestPreflightService.cs
│   │   ├── BacktestStudioContracts.cs
│   │   ├── BacktestStudioRunOrchestrator.cs
│   │   ├── BatchBacktestService.cs
│   │   ├── CorporateActionAdjustmentService.cs
│   │   ├── GlobalUsings.cs
│   │   ├── GlobalUsings.SecurityMasterConcerns.cs
│   │   ├── ICorporateActionAdjustmentService.cs
│   │   ├── Meridian.Backtesting.csproj
│   │   ├── MeridianNativeBacktestStudioEngine.cs
│   │   └── README.md
│   ├── Meridian.Backtesting.Sdk
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
│   │   ├── BacktestStrategyBase.cs
│   │   ├── BiasDisclosure.cs
│   │   ├── CanonicalBacktestResultNormalizer.cs
│   │   ├── CashFlowEntry.cs
│   │   ├── ClosedLot.cs
│   │   ├── ExecutionRealism.cs
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
│   │   ├── AccountingSystem
│   │   │   └── AccountingSystemDtos.cs
│   │   ├── Api
│   │   │   ├── Quality
│   │   │   │   └── QualityApiModels.cs
│   │   │   ├── ApiEndpointDefaults.cs
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
│   │   │   ├── ProviderInstrumentCapabilityMatrix.cs
│   │   │   ├── ProviderRateLimits.cs
│   │   │   ├── ProviderReadinessModels.cs
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
│   │   ├── AssetOperations
│   │   │   ├── AssetAccountingEventDtos.cs
│   │   │   ├── AssetOperationsDtos.cs
│   │   │   ├── InstrumentPositionDtos.cs
│   │   │   ├── PortfolioCashLadderDtos.cs
│   │   │   └── RetainedEvidenceIdentityDto.cs
│   │   ├── Backfill
│   │   │   ├── BackfillProgress.cs
│   │   │   ├── BackfillResult.cs
│   │   │   └── SymbolValidationSignal.cs
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
│   │   │   ├── ConnectivityProbeOptions.cs
│   │   │   ├── DemoWorkspaceLayout.cs
│   │   │   ├── DerivativesConfigDto.cs
│   │   │   ├── MeridianPathDefaults.cs
│   │   │   ├── ProviderConnectionDtos.cs
│   │   │   ├── ProviderConnectionsConfigDto.cs
│   │   │   └── SymbolConfig.cs
│   │   ├── Coordination
│   │   │   ├── CoordinationSnapshot.cs
│   │   │   ├── IClusterCoordinator.cs
│   │   │   ├── ICoordinationStore.cs
│   │   │   ├── ILeaseManager.cs
│   │   │   ├── IScheduledWorkOwnershipService.cs
│   │   │   ├── ISubscriptionOwnershipService.cs
│   │   │   ├── LeaseAcquireResult.cs
│   │   │   └── LeaseRecord.cs
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
│   │   │   ├── EtlModels.cs
│   │   │   ├── IEtlJobDefinitionStore.cs
│   │   │   └── ISftpFilePublisher.cs
│   │   ├── Export
│   │   │   ├── AnalysisExportModels.cs
│   │   │   ├── ExportPreset.cs
│   │   │   └── StandardPresets.cs
│   │   ├── Extensibility
│   │   │   ├── CoreExtensibilityContracts.cs
│   │   │   └── CoreExtensibilityContractsJsonContext.cs
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
│   │   ├── Integrations
│   │   │   ├── ProviderIntegrationContracts.cs
│   │   │   └── ProviderIntegrationContractsJsonContext.cs
│   │   ├── Ledger
│   │   │   ├── AccountingBookContextDtos.cs
│   │   │   ├── AccountingConfigurationCloseReportingDtos.cs
│   │   │   ├── AccountingConfigurationDtos.cs
│   │   │   ├── AccountingConfigurationPrivateCapitalDtos.cs
│   │   │   ├── AccountingPostingCommandDtos.cs
│   │   │   ├── AutomatedJournalEvidenceDtos.cs
│   │   │   ├── LedgerBookDtos.cs
│   │   │   ├── LedgerCurrencyRounding.cs
│   │   │   ├── LedgerToleranceConstants.cs
│   │   │   └── PrivateCapitalActivityRoutes.cs
│   │   ├── Lifecycle
│   │   │   ├── ILifecycleReceiptStore.cs
│   │   │   ├── LifecycleContractsJsonContext.cs
│   │   │   ├── LifecycleDtos.cs
│   │   │   ├── LifecycleEnums.cs
│   │   │   └── LifecycleSupervisorDtos.cs
│   │   ├── Manifest
│   │   │   └── DataManifest.cs
│   │   ├── MoneyMarketFunds
│   │   │   └── MoneyMarketFundReferenceDtos.cs
│   │   ├── Monitoring
│   │   │   ├── IEventMetrics.cs
│   │   │   ├── IMonitoringWebhookSink.cs
│   │   │   └── MetricsSnapshot.cs
│   │   ├── Operations
│   │   │   ├── DataProvenance.cs
│   │   │   ├── IOperationalCaseHistoryStore.cs
│   │   │   ├── OperationalCaseHistoryContracts.cs
│   │   │   ├── OperationalCaseHistoryHashing.cs
│   │   │   ├── OperationsContractsJsonContext.cs
│   │   │   └── VerifiedOperationOutcome.cs
│   │   ├── Options
│   │   │   └── OptionReferenceDtos.cs
│   │   ├── Pipeline
│   │   │   ├── IngestionJob.cs
│   │   │   ├── PipelinePolicyConstants.cs
│   │   │   ├── PipelineStatistics.cs
│   │   │   └── UflOutboxMessage.cs
│   │   ├── Plaid
│   │   │   └── PlaidModels.cs
│   │   ├── Reporting
│   │   │   └── ReportingGovernanceApiDtos.cs
│   │   ├── RuleEvaluation
│   │   │   └── DecisionContracts.cs
│   │   ├── Schema
│   │   │   ├── EventSchema.cs
│   │   │   └── ISchemaUpcaster.cs
│   │   ├── SecurityMaster
│   │   │   ├── CorporateActionEffectiveStateProjector.cs
│   │   │   ├── CorporateActionEventTypeNormalization.cs
│   │   │   ├── CorporateActionEventTypes.cs
│   │   │   ├── CorporateActionLifecycleStates.cs
│   │   │   ├── CorporateActionTypeDescriptorCatalog.cs
│   │   │   ├── DataVendorEntitlement.cs
│   │   │   ├── DayCountConventions.cs
│   │   │   ├── EdgarReferenceDtos.cs
│   │   │   ├── FaceValueLot.cs
│   │   │   ├── IDataVendorEntitlementService.cs
│   │   │   ├── IHistoricalSymbolTimelineResolver.cs
│   │   │   ├── InstrumentTypeDescriptorCatalog.cs
│   │   │   ├── ISecurityCoverageSymbolSource.cs
│   │   │   ├── ISecurityMasterAmender.cs
│   │   │   ├── ISecurityMasterCashFlowService.cs
│   │   │   ├── ISecurityMasterDataQualityService.cs
│   │   │   ├── ISecurityMasterPricingService.cs
│   │   │   ├── ISecurityMasterQueryService.cs
│   │   │   ├── ISecurityMasterReportingQueryService.cs
│   │   │   ├── ISecurityMasterRuntimeStatus.cs
│   │   │   ├── ISecurityMasterService.cs
│   │   │   ├── IStructuredCashFlowProvider.cs
│   │   │   ├── OperatorOverrides.cs
│   │   │   ├── SecurityAssetClassCatalog.cs
│   │   │   ├── SecurityAssetPackRegistry.cs
│   │   │   ├── SecurityAssetProfiles.cs
│   │   │   ├── SecurityAssetSpecificTermsUpcaster.cs
│   │   │   ├── SecurityAssetSpecificTermsUpcasterChain.cs
│   │   │   ├── SecurityAssetTermsSchema.cs
│   │   │   ├── SecurityCommands.cs
│   │   │   ├── SecurityDtos.cs
│   │   │   ├── SecurityEvents.cs
│   │   │   ├── SecurityIdentifierNormalizer.cs
│   │   │   ├── SecurityIdentifiers.cs
│   │   │   ├── SecurityMasterCashFlow.cs
│   │   │   ├── SecurityMasterCorporateActions.cs
│   │   │   ├── SecurityMasterDataQuality.cs
│   │   │   ├── SecurityMasterEnumReads.cs
│   │   │   ├── SecurityMasterOptions.cs
│   │   │   ├── SecurityMasterPricing.cs
│   │   │   ├── SecurityMasterProvenance.cs
│   │   │   ├── SecurityMasterSchemaVersions.cs
│   │   │   ├── SecurityQueries.cs
│   │   │   ├── SecurityTermReader.cs
│   │   │   ├── SecurityValidationDtos.cs
│   │   │   ├── StructuredCashFlowTerms.cs
│   │   │   └── StructuredCashFlowTermsResolver.cs
│   │   ├── Services
│   │   │   ├── IBacktestPreflightService.cs
│   │   │   ├── IConnectivityProbeService.cs
│   │   │   ├── IEnvironmentDesignService.cs
│   │   │   ├── IEnvironmentPublishService.cs
│   │   │   ├── IEnvironmentRuntimeProjectionService.cs
│   │   │   ├── IEnvironmentValidationService.cs
│   │   │   ├── IFundAccountTraversalQueryService.cs
│   │   │   ├── IFundStructureService.cs
│   │   │   ├── IGovernanceSharedDataAccessService.cs
│   │   │   ├── IOperationalScheduler.cs
│   │   │   └── ISecurityValidationGateService.cs
│   │   ├── Session
│   │   │   └── CollectionSession.cs
│   │   ├── Store
│   │   │   └── MarketDataQuery.cs
│   │   ├── StrategyEngine
│   │   │   └── StrategyEngineContracts.cs
│   │   ├── Tenancy
│   │   │   ├── FundProfileTenancy.cs
│   │   │   ├── FundScopedWriteTenantGate.cs
│   │   │   └── TenantReadPredicate.cs
│   │   ├── Treasury
│   │   │   └── MoneyMarketFundDtos.cs
│   │   ├── Workstation
│   │   │   ├── AuditTrailExplorerDtos.cs
│   │   │   ├── AutomatedJournalScheduleDtos.cs
│   │   │   ├── BrokerageSyncDtos.cs
│   │   │   ├── CashOperationsDtos.cs
│   │   │   ├── CollateralExposureDtos.cs
│   │   │   ├── DailyValuationScheduleDtos.cs
│   │   │   ├── DataOperationsAssuranceDtos.cs
│   │   │   ├── DataUploadDtos.cs
│   │   │   ├── EvidenceWorkflowDtos.cs
│   │   │   ├── FeatureCapabilityDtos.cs
│   │   │   ├── FinancialOperationsCommandCenterDtos.cs
│   │   │   ├── FinancialRecordExplorerDtos.cs
│   │   │   ├── FirstRunDtos.cs
│   │   │   ├── FundLedgerDtos.cs
│   │   │   ├── FundOperationsDtos.cs
│   │   │   ├── FundOperationsWorkspaceDtos.cs
│   │   │   ├── FundWorkflowCommands.cs
│   │   │   ├── InvestmentAccountingTransactionLabDtos.cs
│   │   │   ├── IOperatorInboxService.cs
│   │   │   ├── IReportingRunNotifier.cs
│   │   │   ├── LedgerReconciliationContractCompatibility.cs
│   │   │   ├── OperationsContinuityDtos.cs
│   │   │   ├── PilotReadinessArtifactDtos.cs
│   │   │   ├── ReconciliationDtos.cs
│   │   │   ├── ReportingDeploymentDtos.cs
│   │   │   ├── ResearchBriefingDtos.cs
│   │   │   ├── SecurityMasterTrustWorkbenchDtos.cs
│   │   │   ├── SecurityMasterWorkbenchCommandDtos.cs
│   │   │   ├── SecurityMasterWorkstationDtos.cs
│   │   │   ├── StatementConnectorDtos.cs
│   │   │   ├── StatementReconciliationDtos.cs
│   │   │   ├── StrategyBriefingDtos.cs
│   │   │   ├── StrategyDesignDtos.cs
│   │   │   ├── StrategyRunContractCompatibility.cs
│   │   │   ├── StrategyRunReadModels.cs
│   │   │   ├── TradingOperatorReadinessDtos.cs
│   │   │   ├── WorkflowLibraryDtos.cs
│   │   │   ├── WorkflowSummaryDtos.cs
│   │   │   ├── WorkstationBootstrapDtos.cs
│   │   │   └── WorkstationWorkspaceCatalog.cs
│   │   ├── Meridian.Contracts.csproj
│   │   └── README.md
│   ├── Meridian.Core
│   │   ├── Config
│   │   │   ├── AlpacaCredentialEnvironment.cs
│   │   │   ├── AlpacaOptions.cs
│   │   │   ├── AppConfig.cs
│   │   │   ├── AppConfigJsonOptions.cs
│   │   │   ├── BackfillConfig.cs
│   │   │   ├── CanonicalizationConfig.cs
│   │   │   ├── ConfigEnvironmentOverride.cs
│   │   │   ├── ConfigJsonSchemaGenerator.cs
│   │   │   ├── ConfigTemplateGenerator.cs
│   │   │   ├── ConfigValidationHelper.cs
│   │   │   ├── ConfigWatcher.cs
│   │   │   ├── CoordinationConfig.cs
│   │   │   ├── CredentialPlaceholderDetector.cs
│   │   │   ├── DataSourceConfig.cs
│   │   │   ├── DataSourceKind.cs
│   │   │   ├── DataSourceKindConverter.cs
│   │   │   ├── DefaultConfigPathResolver.cs
│   │   │   ├── DerivativesConfig.cs
│   │   │   ├── FeatureCapabilityCatalog.cs
│   │   │   ├── FeatureCapabilityDescriptor.cs
│   │   │   ├── FeatureCapabilityOptions.cs
│   │   │   ├── IConfigurationProvider.cs
│   │   │   ├── IConfigValidator.cs
│   │   │   ├── ProviderConnectionsConfig.cs
│   │   │   ├── ProviderModulesConfig.cs
│   │   │   ├── SensitiveKeyRegistry.cs
│   │   │   ├── SensitiveValueMasker.cs
│   │   │   ├── SyntheticMarketDataConfig.cs
│   │   │   └── ValidatedConfig.cs
│   │   ├── Contracts
│   │   │   └── IProviderCredentialStore.cs
│   │   ├── Diagnostics
│   │   │   └── RuntimeDiagnosticRedactor.cs
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
│   │   ├── IO
│   │   │   └── RootedPathGuard.cs
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
│   │   ├── Resilience
│   │   │   ├── Backoff.cs
│   │   │   └── CircuitBreaker.cs
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
│   ├── Meridian.DataIntegration
│   │   ├── AccountingSystem
│   │   │   ├── Fixtures
│   │   │   │   └── FixtureAccountingProviders.cs
│   │   │   └── QuickBooks
│   │   │       ├── QuickBooksFixtureAccountingProvider.cs
│   │   │       ├── QuickBooksOnlineAccountingProvider.cs
│   │   │       └── QuickBooksOnlineProviderCredentialConnectionStore.cs
│   │   ├── Canonicalization
│   │   │   ├── CanonicalizationMetrics.cs
│   │   │   ├── CanonicalizingPublisher.cs
│   │   │   ├── ConditionCodeMapper.cs
│   │   │   ├── EventCanonicalizer.cs
│   │   │   ├── ICanonicalSecurityIdLookup.cs
│   │   │   ├── IEventCanonicalizer.cs
│   │   │   └── VenueMicMapper.cs
│   │   ├── Credentials
│   │   │   ├── CredentialStatus.cs
│   │   │   ├── FileProviderCredentialStore.cs
│   │   │   ├── ICredentialStore.cs
│   │   │   ├── IProviderCredentialStore.cs
│   │   │   ├── OAuthToken.cs
│   │   │   ├── ProviderCredentialCatalog.cs
│   │   │   └── ProviderSetupHandlers.cs
│   │   ├── Etl
│   │   │   ├── EtlAbstractions.cs
│   │   │   ├── EtlExportService.cs
│   │   │   ├── EtlNormalizationService.cs
│   │   │   ├── EtlOperationJsonContext.cs
│   │   │   ├── EtlPreviewService.cs
│   │   │   ├── EtlServices.cs
│   │   │   └── EtlServices.Verification.cs
│   │   ├── Filters
│   │   │   └── MarketEventFilter.cs
│   │   ├── Historical
│   │   │   └── HistoricalDataQueryService.cs
│   │   ├── Monitoring
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
│   │   │   ├── BadTickFilter.cs
│   │   │   ├── ClockSkewEstimator.cs
│   │   │   ├── ConnectionHealthMonitor.cs
│   │   │   ├── ConnectionStatusWebhook.cs
│   │   │   ├── DataLossAccounting.cs
│   │   │   ├── ProviderDegradationCalibration.cs
│   │   │   ├── ProviderDegradationConfig.cs
│   │   │   ├── ProviderDegradationScorer.cs
│   │   │   ├── ProviderLatencyService.cs
│   │   │   ├── ProviderMetricsStatus.cs
│   │   │   ├── ProviderMonitoringIdentity.cs
│   │   │   ├── SchemaValidationService.cs
│   │   │   ├── SpreadMonitor.cs
│   │   │   ├── TickSizeValidator.cs
│   │   │   ├── TimestampMonotonicityChecker.cs
│   │   │   └── ValidationMetrics.cs
│   │   ├── Testing
│   │   │   ├── DepthBufferSelfTests.cs
│   │   │   └── SampleDataGenerator.cs
│   │   ├── DesignModule.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.DataIntegration.csproj
│   │   └── README.md
│   ├── Meridian.Documents
│   │   ├── ClientGradeReportRenderer.cs
│   │   ├── DesignModule.cs
│   │   ├── DeterministicDocumentPackaging.cs
│   │   ├── DocumentsServiceCollectionExtensions.cs
│   │   ├── FinancialReportDocumentRenderer.cs
│   │   ├── Meridian.Documents.csproj
│   │   ├── README.md
│   │   └── ReportDocumentModel.cs
│   ├── Meridian.Domain
│   │   ├── Collectors
│   │   │   ├── IQuoteStateStore.cs
│   │   │   ├── IQuoteUpdateNotifier.cs
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
│   │   │   ├── IEventQuarantineSink.cs
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
│   ├── Meridian.Entities
│   │   ├── FundStructure
│   │   │   ├── FundStructurePolicyService.cs
│   │   │   ├── IFundStructurePolicyService.cs
│   │   │   ├── LedgerGroupingRules.cs
│   │   │   └── LedgerMappingWorkbenchService.cs
│   │   ├── DesignModule.cs
│   │   ├── Meridian.Entities.csproj
│   │   └── README.md
│   ├── Meridian.Execution
│   │   ├── Adapters
│   │   │   ├── BaseBrokerageGateway.cs
│   │   │   ├── BrokerageGatewayAdapter.cs
│   │   │   ├── LiveMarketDataCache.cs
│   │   │   ├── OmsGovernedBrokerageOrderGateway.cs
│   │   │   ├── OmsGovernedExecutionOrderGateway.cs
│   │   │   ├── PaperTradingGateway.cs
│   │   │   ├── PaperTradingGatewayOptions.cs
│   │   │   └── PaperTradingGatewaySupport.cs
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
│   │   │   ├── TradeExecutedEvent.cs
│   │   │   ├── TradeFillHandoffFailureStore.cs
│   │   │   ├── TradeFillLedgerPostingTarget.cs
│   │   │   └── TradeFillPostingStore.cs
│   │   ├── Exceptions
│   │   │   └── UnsupportedOrderRequestException.cs
│   │   ├── Interfaces
│   │   │   ├── IAccountPortfolio.cs
│   │   │   ├── IExecutionContext.cs
│   │   │   ├── ILiveFeedAdapter.cs
│   │   │   └── IOrderGateway.cs
│   │   ├── Live
│   │   │   ├── ILiveMarketEventFeed.cs
│   │   │   └── LiveMarketEventHub.cs
│   │   ├── Logging
│   │   │   └── LogSanitizer.cs
│   │   ├── Margin
│   │   │   ├── IMarginModel.cs
│   │   │   ├── MarginAccountType.cs
│   │   │   ├── MarginCallStatus.cs
│   │   │   ├── MarginRequirement.cs
│   │   │   ├── PortfolioMarginModel.cs
│   │   │   ├── RegTMarginModel.cs
│   │   │   └── RegTMarginOptions.cs
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
│   │   │   ├── InMemoryFxRateProvider.cs
│   │   │   └── MultiCurrencyCashBalance.cs
│   │   ├── Serialization
│   │   │   └── ExecutionJsonContext.cs
│   │   ├── Services
│   │   │   ├── BrokerageExecutionReconciliationService.cs
│   │   │   ├── ExecutionAuditTrailService.cs
│   │   │   ├── ExecutionOperatorControlService.cs
│   │   │   ├── ILiveOrderReadinessGate.cs
│   │   │   ├── IPaperSessionStore.cs
│   │   │   ├── JsonlFilePaperSessionStore.cs
│   │   │   ├── OrderLifecycleManager.cs
│   │   │   ├── PaperSessionOptions.cs
│   │   │   ├── PaperSessionPersistenceService.cs
│   │   │   ├── PaperTradingPortfolio.cs
│   │   │   ├── PortfolioRegistry.cs
│   │   │   ├── PortfolioStatePositionTracker.cs
│   │   │   ├── PositionLotSelector.cs
│   │   │   ├── PositionReconciliationService.cs
│   │   │   ├── PositionSyncOptions.cs
│   │   │   ├── ReconciliationSetComparer.cs
│   │   │   ├── ReplayDriftDetector.cs
│   │   │   ├── RiskEscalationQueueService.cs
│   │   │   └── SessionTcaReporter.cs
│   │   ├── TaxLotAccounting
│   │   │   ├── ITaxLotSelector.cs
│   │   │   ├── TaxLotAccountingMethod.cs
│   │   │   ├── TaxLotRelief.cs
│   │   │   └── TaxLotSelectors.cs
│   │   ├── BrokerageServiceRegistration.cs
│   │   ├── GlobalUsings.cs
│   │   ├── GlobalUsings.SecurityMasterConcerns.cs
│   │   ├── IRiskValidator.cs
│   │   ├── ISecurityMasterGate.cs
│   │   ├── Meridian.Execution.csproj
│   │   ├── OrderManagementSystem.Audit.cs
│   │   ├── OrderManagementSystem.cs
│   │   ├── OrderManagementSystem.FillIdentity.cs
│   │   ├── OrderManagementSystem.RiskOutcomes.cs
│   │   ├── OrderManagementSystemOptions.cs
│   │   ├── PaperExecutionContext.cs
│   │   ├── PaperTradingGateway.cs
│   │   ├── README.md
│   │   └── SecurityMasterGate.cs
│   ├── Meridian.Execution.Sdk
│   │   ├── Derivatives
│   │   │   ├── FutureDetails.cs
│   │   │   ├── OptionDetails.cs
│   │   │   └── OptionGreeks.cs
│   │   ├── BrokerageConfiguration.cs
│   │   ├── BrokerageOrderPlacementGate.cs
│   │   ├── BrokerageValidationEvaluator.cs
│   │   ├── BrokerNotionalMetadata.cs
│   │   ├── ExecutionOrderMetadataPolicy.cs
│   │   ├── IBrokerageAccountSync.cs
│   │   ├── IBrokerageGateway.cs
│   │   ├── IBrokeragePositionSync.cs
│   │   ├── IExecutionGateway.cs
│   │   ├── IExecutionGatewayModeProvider.cs
│   │   ├── INotionalOrderSizingGateway.cs
│   │   ├── IOrderManager.cs
│   │   ├── IPosition.cs
│   │   ├── IPositionTracker.cs
│   │   ├── Meridian.Execution.Sdk.csproj
│   │   ├── Models.cs
│   │   ├── PositionExtensions.cs
│   │   ├── README.md
│   │   ├── RiskContracts.cs
│   │   └── TaxLot.cs
│   ├── Meridian.FinancialOperations
│   │   ├── AccountingClose
│   │   │   ├── AccountingCloseManagementService.cs
│   │   │   ├── AccountingCloseManagementService.PlanProjection.cs
│   │   │   ├── AccountingCloseManagementService.ValidationAndEvidence.cs
│   │   │   ├── AccountingCloseModels.cs
│   │   │   ├── AccountingClosePostingWorkbench.cs
│   │   │   ├── AccountingCloseServices.cs
│   │   │   └── AccountingReportPackageService.cs
│   │   ├── AccountingSystem
│   │   │   └── AccountingSystemIntegrationService.cs
│   │   ├── Banking
│   │   │   ├── BankingException.cs
│   │   │   ├── IBankingService.cs
│   │   │   ├── InMemoryBankingService.cs
│   │   │   └── PostgresBankingService.cs
│   │   ├── FundAdministration
│   │   │   ├── FundAdministrationControlService.cs
│   │   │   └── FundAdministrationModels.cs
│   │   ├── Ledger
│   │   │   ├── TextJournal
│   │   │   │   ├── LedgerTextJournalDocument.cs
│   │   │   │   ├── LedgerTextJournalException.cs
│   │   │   │   ├── LedgerTextJournalParser.cs
│   │   │   │   ├── LedgerTextJournalReportService.cs
│   │   │   │   ├── LedgerTextReportOptions.cs
│   │   │   │   ├── LedgerTextReportRenderer.cs
│   │   │   │   └── LedgerTextTransaction.cs
│   │   │   ├── AccountingBasisProjectionSetService.cs
│   │   │   ├── AccountingJournalDraftService.cs
│   │   │   ├── AccountingPolicyService.cs
│   │   │   ├── AccountingPostingCandidatePostService.cs
│   │   │   ├── AccountingPostingCandidateService.cs
│   │   │   ├── AssetAccountingCandidateCanonicalizer.cs
│   │   │   ├── AssetAccountingEventSpineService.cs
│   │   │   └── LedgerJournalConstruction.cs
│   │   ├── MiddleOffice
│   │   │   ├── MiddleOfficeModels.cs
│   │   │   └── MiddleOfficeOperationsService.cs
│   │   ├── OperationsContinuity
│   │   │   ├── FinancialOperationsCommandCenterReadService.cs
│   │   │   ├── OperationsApprovalPolicyMatrixService.cs
│   │   │   ├── OperationsCloseCalendarService.cs
│   │   │   ├── OperationsContinuityRepositories.cs
│   │   │   ├── OperationsContinuityWorkflow.cs
│   │   │   ├── OperationsContinuityWorkflow.Reconciliation.cs
│   │   │   ├── OperationsContinuityWorkflowService.cs
│   │   │   ├── OperationsContinuityWorkflowService.Projection.cs
│   │   │   ├── OperationsContinuityWorkflowService.Transitions.cs
│   │   │   ├── OperationsContinuityWorkflowText.cs
│   │   │   ├── OperationsLedgerPostingService.cs
│   │   │   ├── OperationsStatusDerivationService.cs
│   │   │   ├── OperationsWorkflowAuditHashing.cs
│   │   │   ├── OperationsWorkflowAuditHashJsonContext.cs
│   │   │   └── PostgresOperationsContinuityStore.cs
│   │   ├── PrivateCapital
│   │   │   ├── CommitmentRollForwardCalculator.cs
│   │   │   ├── DefaultInterestCalculator.cs
│   │   │   ├── LedgerCapitalAccountReconciliationResolver.cs
│   │   │   ├── PrivateCapitalActivityProjectionBuilder.cs
│   │   │   ├── PrivateCapitalCapitalAccountSubledgerBuilder.cs
│   │   │   ├── PrivateCapitalCloseCockpitService.ApprovalHistory.cs
│   │   │   ├── PrivateCapitalCloseCockpitService.cs
│   │   │   ├── PrivateCapitalCloseCockpitService.Routes.cs
│   │   │   ├── PrivateCapitalEvidenceCategoryBuilder.cs
│   │   │   ├── PrivateCapitalFundEventLedgerReadinessBuilder.cs
│   │   │   ├── PrivateCapitalFundEventLedgerRecordBuilder.cs
│   │   │   └── PrivateCapitalPaymentIntentEvidenceBuilder.cs
│   │   ├── Reconciliation
│   │   │   ├── Connectors
│   │   │   │   ├── Alpaca
│   │   │   │   │   ├── AlpacaActivityStatementConnector.cs
│   │   │   │   │   └── AlpacaStatementSnapshot.cs
│   │   │   │   ├── Bai2
│   │   │   │   │   └── Bai2StatementConnector.cs
│   │   │   │   ├── Camt
│   │   │   │   │   └── Camt053StatementConnector.cs
│   │   │   │   ├── IbFlex
│   │   │   │   │   └── IbFlexStatementConnector.cs
│   │   │   │   ├── Ofx
│   │   │   │   │   ├── OfxDocumentParser.cs
│   │   │   │   │   └── OfxStatementConnector.cs
│   │   │   │   ├── CsvLineSplitter.cs
│   │   │   │   ├── CsvStatementConnector.cs
│   │   │   │   ├── FileStatementMappingProfileStore.cs
│   │   │   │   ├── StatementBuiltInProfiles.cs
│   │   │   │   ├── StatementColumnConfidenceScorer.cs
│   │   │   │   ├── StatementConnectorContracts.cs
│   │   │   │   ├── StatementConnectorRegistry.cs
│   │   │   │   ├── StatementFetchScheduleRunner.cs
│   │   │   │   ├── StatementFetchScheduleStore.cs
│   │   │   │   ├── StatementImportService.cs
│   │   │   │   ├── StatementMappingProfileCatalog.cs
│   │   │   │   ├── StatementMappingProfileDocument.cs
│   │   │   │   ├── StatementMappingProfileJsonContext.cs
│   │   │   │   ├── StatementMappingProfileLoader.cs
│   │   │   │   ├── StatementRecordMapper.cs
│   │   │   │   └── StatementValueParser.cs
│   │   │   ├── BrokerCustodianReconciliationModels.cs
│   │   │   ├── BrokerReconciliationFeedModels.cs
│   │   │   ├── BusinessDayAccountingCalendar.cs
│   │   │   ├── DefaultReconciliationIngestionScheduler.cs
│   │   │   ├── FileAccountingCalendar.cs
│   │   │   ├── FileReconciliationDecisionJournal.cs
│   │   │   ├── FileReconciliationFxRateProvider.cs
│   │   │   ├── FileStatementReconciliationCheckpointStore.cs
│   │   │   ├── FileStatementToleranceProfileProvider.cs
│   │   │   ├── InternalReconciliationBook.cs
│   │   │   ├── InternalReconciliationPopulations.cs
│   │   │   ├── MatchingTolerances.cs
│   │   │   ├── ReconciliationContextContracts.cs
│   │   │   ├── ReconciliationContractCatalog.cs
│   │   │   ├── ReconciliationEngineService.cs
│   │   │   ├── ReconciliationFxRateProvider.cs
│   │   │   ├── ReconciliationIngestionContracts.cs
│   │   │   ├── ReconciliationIngestionOptions.cs
│   │   │   ├── ReconciliationMatchingEngine.cs
│   │   │   ├── ReconciliationMatchKernel.cs
│   │   │   ├── ReconciliationNormalizationService.cs
│   │   │   ├── ReconciliationOrchestrationResilience.cs
│   │   │   ├── ReconciliationRunOrchestrator.cs
│   │   │   ├── ReconciliationServiceRegistration.cs
│   │   │   ├── StatementBreakClassifier.cs
│   │   │   ├── StatementMappingProfiles.cs
│   │   │   ├── StatementMatchingEngine.cs
│   │   │   ├── StatementReconciliationContextAdapter.cs
│   │   │   ├── StatementReconciliationOrchestrator.cs
│   │   │   ├── StatementReconciliationService.cs
│   │   │   ├── StatementRepositories.cs
│   │   │   ├── StatementRunCreateRequest.cs
│   │   │   ├── StatementRunEvidenceLinks.cs
│   │   │   ├── StatementRunMatcher.cs
│   │   │   ├── StatementRunMatchingService.cs
│   │   │   ├── StatementRunWorkflowService.cs
│   │   │   ├── StatementToleranceProfiles.cs
│   │   │   └── StatementValidationService.cs
│   │   ├── DesignModule.cs
│   │   ├── Meridian.FinancialOperations.csproj
│   │   └── README.md
│   ├── Meridian.FSharp
│   │   ├── Calculations
│   │   │   ├── Aggregations.fs
│   │   │   ├── Imbalance.fs
│   │   │   ├── SecurityCalculations.fs
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
│   │   │   ├── ApprovalWorkflowRules.fs
│   │   │   ├── OperationsContinuityRules.fs
│   │   │   ├── ProviderDegradationScoring.fs
│   │   │   ├── ReportPackValidationRules.fs
│   │   │   ├── SecurityTermValidation.fs
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
│   │   ├── AggregateTypes.fs
│   │   ├── ContractAggregate.fs
│   │   ├── Interop.fs
│   │   ├── Meridian.FSharp.DirectLending.Aggregates.fsproj
│   │   ├── README.md
│   │   └── ServicingAggregate.fs
│   ├── Meridian.FSharp.Ledger
│   │   ├── AccrualTypes.fs
│   │   ├── FundEconomics.fs
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
│   ├── Meridian.Identity
│   │   ├── Application
│   │   │   ├── AuthenticationMode.cs
│   │   │   ├── FundStructureAccessScopeLineageProvider.cs
│   │   │   ├── IdentityGovernanceNormalization.cs
│   │   │   ├── LoginSessionService.cs
│   │   │   ├── PasswordHashing.cs
│   │   │   ├── ScopedAccessServices.cs
│   │   │   └── UserProfileRegistry.cs
│   │   ├── Contracts
│   │   │   └── Auth
│   │   │       ├── RolePermissions.cs
│   │   │       ├── ScopedAccessDtos.cs
│   │   │       ├── UserAccountDtos.cs
│   │   │       ├── UserPermission.cs
│   │   │       └── UserRole.cs
│   │   ├── FundStructure
│   │   │   └── FundAccountTraversalQueryService.cs
│   │   ├── Infrastructure
│   │   │   ├── RolePermissionProfileStore.cs
│   │   │   ├── ScopedAccessAssignmentStore.cs
│   │   │   └── UserAccountStore.cs
│   │   ├── Migrations
│   │   │   └── 001_user_access_assignment.sql
│   │   ├── DesignModule.cs
│   │   ├── Meridian.Identity.csproj
│   │   └── README.md
│   ├── Meridian.Infrastructure
│   │   ├── Adapters
│   │   │   ├── Alpaca
│   │   │   │   ├── AlpacaAssetStreamAdapters.cs
│   │   │   │   ├── AlpacaBrokerageGateway.cs
│   │   │   │   ├── AlpacaConstants.cs
│   │   │   │   ├── AlpacaCorporateActionProvider.cs
│   │   │   │   ├── AlpacaHistoricalDataProvider.cs
│   │   │   │   ├── AlpacaMarketDataClient.cs
│   │   │   │   ├── AlpacaOptionsChainProvider.cs
│   │   │   │   ├── AlpacaProviderModule.cs
│   │   │   │   ├── AlpacaStreamProfiles.cs
│   │   │   │   ├── AlpacaSymbolSearchProvider.cs
│   │   │   │   └── AlpacaTradeUpdatesClient.cs
│   │   │   ├── AlphaVantage
│   │   │   │   ├── AlphaVantageCorporateActionProvider.cs
│   │   │   │   ├── AlphaVantageHistoricalDataProvider.cs
│   │   │   │   └── AlphaVantageSymbolSearchProvider.cs
│   │   │   ├── Core
│   │   │   │   ├── Backfill
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
│   │   │   │   │   ├── CanonicalRegistrySymbolResolver.cs
│   │   │   │   │   └── ISymbolResolver.cs
│   │   │   │   ├── BackfillBarValidation.cs
│   │   │   │   ├── BackfillProgressTracker.cs
│   │   │   │   ├── BaseHistoricalDataProvider.cs
│   │   │   │   ├── BaseSymbolSearchProvider.cs
│   │   │   │   ├── CompositeHistoricalDataProvider.cs
│   │   │   │   ├── CrossProviderValidator.cs
│   │   │   │   ├── ICorporateActionProvider.cs
│   │   │   │   ├── IHistoricalAggregateBarProvider.cs
│   │   │   │   ├── IHistoricalDataProvider.cs
│   │   │   │   ├── IReconnectionGapSource.cs
│   │   │   │   ├── ISymbolSearchProvider.cs
│   │   │   │   ├── PollingProviderBase.cs
│   │   │   │   ├── ProviderCapabilityDescriptorCatalog.cs
│   │   │   │   ├── ProviderDataQualityValidator.cs
│   │   │   │   ├── ProviderFactory.cs
│   │   │   │   ├── ProviderHealthTracker.cs
│   │   │   │   ├── ProviderIdentity.cs
│   │   │   │   ├── ProviderRegistry.cs
│   │   │   │   ├── ProviderRotationStrategy.cs
│   │   │   │   ├── ProviderServiceExtensions.cs
│   │   │   │   ├── ProviderSubscriptionRanges.cs
│   │   │   │   ├── ProviderTemplate.cs
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
│   │   │   │   ├── FinnhubCorporateActionProvider.cs
│   │   │   │   ├── FinnhubHistoricalDataProvider.cs
│   │   │   │   └── FinnhubSymbolSearchProvider.cs
│   │   │   ├── Fred
│   │   │   │   ├── FredHistoricalDataProvider.cs
│   │   │   │   └── FredSymbolSearchProvider.cs
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
│   │   │   │   ├── IBDataResultMaterializer.cs
│   │   │   │   ├── IBDataServices.cs
│   │   │   │   ├── IBDurableResultStore.cs
│   │   │   │   ├── IBHistoricalDataProvider.cs
│   │   │   │   ├── IBMarketDataClient.cs
│   │   │   │   ├── IBSimulationClient.cs
│   │   │   │   └── TenantScopedProviderDataUpdateHub.cs
│   │   │   ├── NasdaqDataLink
│   │   │   │   ├── NasdaqDataLinkCorporateActionProvider.cs
│   │   │   │   ├── NasdaqDataLinkHistoricalDataProvider.cs
│   │   │   │   └── NasdaqDataLinkSymbolSearchProvider.cs
│   │   │   ├── NYSE
│   │   │   │   ├── NyseAccessTokenProvider.cs
│   │   │   │   ├── NYSEDataSource.cs
│   │   │   │   ├── NyseHistoricalDataProvider.cs
│   │   │   │   ├── NyseHttpResponseGuard.cs
│   │   │   │   ├── NyseMarketDataClient.cs
│   │   │   │   ├── NyseNationalTradesCsvParser.cs
│   │   │   │   ├── NYSEOptions.cs
│   │   │   │   └── NYSEServiceExtensions.cs
│   │   │   ├── OpenFigi
│   │   │   │   ├── OpenFigiClient.cs
│   │   │   │   └── OpenFigiSymbolResolver.cs
│   │   │   ├── Plaid
│   │   │   │   ├── FilePlaidConnectionRepository.cs
│   │   │   │   └── PlaidHttpClient.cs
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
│   │   │   │   ├── TiingoCorporateActionProvider.cs
│   │   │   │   ├── TiingoHistoricalDataProvider.cs
│   │   │   │   └── TiingoSymbolSearchProvider.cs
│   │   │   ├── TradeStation
│   │   │   │   └── TradeStationPayloadMappers.cs
│   │   │   ├── Tradier
│   │   │   │   └── TradierCanonicalMappers.cs
│   │   │   ├── TwelveData
│   │   │   │   ├── TwelveDataCorporateActionProvider.cs
│   │   │   │   ├── TwelveDataHistoricalDataProvider.cs
│   │   │   │   └── TwelveDataSymbolSearchProvider.cs
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
│   │   │   │   ├── ISftpClientFactory.cs
│   │   │   │   ├── SftpConnectionOptions.cs
│   │   │   │   ├── SftpCredentialResolver.cs
│   │   │   │   └── SftpRemoteLocation.cs
│   │   │   ├── CsvPartnerFileParser.cs
│   │   │   ├── LocalFileSourceReader.cs
│   │   │   ├── SftpFilePublisher.cs
│   │   │   └── SftpFileSourceReader.cs
│   │   ├── Http
│   │   │   ├── HttpClientConfiguration.cs
│   │   │   └── SharedResiliencePolicies.cs
│   │   ├── Reconciliation
│   │   │   ├── BrokerStatementInfrastructure.cs
│   │   │   ├── BrokerStatementNormalizer.cs
│   │   │   ├── IbFlexStatementService.cs
│   │   │   └── ReconciliationCaseInfrastructure.cs
│   │   ├── Resilience
│   │   │   ├── HttpResiliencePolicy.cs
│   │   │   ├── ProviderConnectionSupervisor.cs
│   │   │   ├── WebSocketConnectionConfig.cs
│   │   │   ├── WebSocketConnectionManager.cs
│   │   │   └── WebSocketResiliencePolicy.cs
│   │   ├── Shared
│   │   │   ├── ISymbolStateStore.cs
│   │   │   ├── SubscriptionManager.cs
│   │   │   └── TaskSafetyExtensions.cs
│   │   ├── Utilities
│   │   │   ├── HttpResponseHandler.cs
│   │   │   ├── JsonElementExtensions.cs
│   │   │   ├── ProviderDateParsing.cs
│   │   │   └── SymbolNormalization.cs
│   │   ├── ConnectionDiagnosticsTypeForwarders.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.Infrastructure.csproj
│   │   ├── NoOpMarketDataClient.cs
│   │   └── README.md
│   ├── Meridian.Instruments
│   │   ├── AssetOperations
│   │   │   ├── AssetObligationProjectionService.cs
│   │   │   ├── AssetOperationsReadService.cs
│   │   │   ├── FactorPaydownProjectionService.cs
│   │   │   └── PortfolioCashLadderEngine.cs
│   │   ├── CertificatesOfDeposit
│   │   │   ├── CertificateOfDepositProjectionService.cs
│   │   │   └── ICertificateOfDepositReferenceService.cs
│   │   ├── Commodities
│   │   │   ├── CommodityProjectionService.cs
│   │   │   └── ICommodityReferenceService.cs
│   │   ├── CryptoCurrency
│   │   │   ├── CryptoProjectionService.cs
│   │   │   └── ICryptoReferenceService.cs
│   │   ├── Deposits
│   │   │   ├── DepositProjectionService.cs
│   │   │   └── IDepositReferenceService.cs
│   │   ├── Derivatives
│   │   │   ├── ISwapReferenceService.cs
│   │   │   └── SwapProjectionService.cs
│   │   ├── Equity
│   │   │   ├── EquityProjectionService.cs
│   │   │   └── IEquityReferenceService.cs
│   │   ├── FixedIncome
│   │   │   ├── BondProjectionService.cs
│   │   │   └── IBondReferenceService.cs
│   │   ├── Futures
│   │   │   ├── FutureProjectionService.cs
│   │   │   └── IFutureReferenceService.cs
│   │   ├── FxSpot
│   │   │   ├── FxSpotProjectionService.cs
│   │   │   └── IFxSpotReferenceService.cs
│   │   ├── Indicators
│   │   │   └── TechnicalIndicatorService.cs
│   │   ├── MoneyMarketFunds
│   │   │   ├── IMmfLiquidityService.cs
│   │   │   ├── IMoneyMarketFundReferenceService.cs
│   │   │   ├── IMoneyMarketFundService.cs
│   │   │   ├── InMemoryMoneyMarketFundService.cs
│   │   │   ├── MoneyMarketFundProjectionService.cs
│   │   │   └── PostgresMoneyMarketFundService.cs
│   │   ├── Options
│   │   │   ├── IOptionChainImportService.cs
│   │   │   ├── IOptionReferenceService.cs
│   │   │   ├── OptionProjectionService.cs
│   │   │   └── OptionsChainService.cs
│   │   ├── DesignModule.cs
│   │   ├── InstrumentProjectionServiceBase.cs
│   │   ├── Meridian.Instruments.csproj
│   │   └── README.md
│   ├── Meridian.Launcher
│   │   ├── Meridian.Launcher.csproj
│   │   ├── Program.cs
│   │   ├── README.md
│   │   └── StartupOutcomeReceiptMonitor.cs
│   ├── Meridian.Ledger
│   │   ├── AutomatedJournalApproval.cs
│   │   ├── AutomatedJournalApprovalEvent.cs
│   │   ├── AutomatedJournalApprovalStatus.cs
│   │   ├── AutomatedJournalDraft.cs
│   │   ├── AutomatedJournalDraftProjector.cs
│   │   ├── AutomatedJournalEvent.cs
│   │   ├── AutomatedJournalEventKind.cs
│   │   ├── AutomatedJournalPostingTarget.cs
│   │   ├── BuiltInLedgerReportBinaryRenderer.cs
│   │   ├── CapitalCallDraftFactory.cs
│   │   ├── CapitalCallPlanBuilder.cs
│   │   ├── CapitalCallScheduleDraftBuilder.cs
│   │   ├── CarriedInterestClawbackCalculator.cs
│   │   ├── ChartOfAccounts.cs
│   │   ├── ChartOfAccountsNode.cs
│   │   ├── DailyPortfolioPriceMark.cs
│   │   ├── DailyPortfolioPricingDraftBuilder.cs
│   │   ├── DailyPortfolioPricingInput.cs
│   │   ├── DailyPortfolioPricingLine.cs
│   │   ├── DailyPortfolioPricingPolicy.cs
│   │   ├── DailyPortfolioPricingProjection.cs
│   │   ├── DailyPortfolioPricingProjector.cs
│   │   ├── DepreciationInput.cs
│   │   ├── DepreciationMethod.cs
│   │   ├── DepreciationPeriod.cs
│   │   ├── DepreciationProjection.cs
│   │   ├── DepreciationScheduleCalculator.cs
│   │   ├── EqualizationCalculator.cs
│   │   ├── EuropeanDistributionWaterfall.cs
│   │   ├── FairValueLevel.cs
│   │   ├── FixedAssetDepreciationDraftBuilder.cs
│   │   ├── FixedAssetDepreciationProjector.cs
│   │   ├── FixedAssetRecord.cs
│   │   ├── FixedIncomeAmortizationInput.cs
│   │   ├── FixedIncomeAmortizationProjection.cs
│   │   ├── FixedIncomeAmortizationProjector.cs
│   │   ├── FundAdministrationEvent.cs
│   │   ├── FundAdministrationEventLog.cs
│   │   ├── FundEconomicsJournalFactory.cs
│   │   ├── FundLedgerBook.cs
│   │   ├── GlobalUsings.cs
│   │   ├── IDepreciationScheduleCalculator.cs
│   │   ├── ILedgerReportBinaryRenderer.cs
│   │   ├── IReadOnlyLedger.cs
│   │   ├── IWashSaleReplacementResolver.cs
│   │   ├── JournalEntry.cs
│   │   ├── JournalEntryMetadata.cs
│   │   ├── JournalEvidenceReference.cs
│   │   ├── JournalTemplate.cs
│   │   ├── JournalTemplateBook.cs
│   │   ├── Ledger.cs
│   │   ├── LedgerAccount.cs
│   │   ├── LedgerAccounts.cs
│   │   ├── LedgerAccountSummary.cs
│   │   ├── LedgerAccountTaxLotPolicy.cs
│   │   ├── LedgerAccountTaxLotPolicyBook.cs
│   │   ├── LedgerAccountType.cs
│   │   ├── LedgerBalancePoint.cs
│   │   ├── LedgerBookKey.cs
│   │   ├── LedgerCashFlowStatement.cs
│   │   ├── LedgerChartBalance.cs
│   │   ├── LedgerCurrencyExposure.cs
│   │   ├── LedgerCurrencyTranslation.cs
│   │   ├── LedgerEntry.cs
│   │   ├── LedgerEntryCurrency.cs
│   │   ├── LedgerFinancialReportPack.cs
│   │   ├── LedgerFinancialStatementBuilder.cs
│   │   ├── LedgerFinancialStatements.cs
│   │   ├── LedgerGovernedLifecycle.cs
│   │   ├── LedgerJournalReversal.cs
│   │   ├── LedgerLineDimensionSet.cs
│   │   ├── LedgerLineDimensionSetFields.cs
│   │   ├── LedgerLineDimensionSetNormalizer.cs
│   │   ├── LedgerPartnersCapitalStatement.cs
│   │   ├── LedgerQuery.cs
│   │   ├── LedgerReportExportFormat.cs
│   │   ├── LedgerReportPackArtifact.cs
│   │   ├── LedgerReportPackBuilder.cs
│   │   ├── LedgerReportPackLifecycle.cs
│   │   ├── LedgerReportPackRequest.cs
│   │   ├── LedgerReportPackSignature.cs
│   │   ├── LedgerReportPresentation.cs
│   │   ├── LedgerReportSchedule.cs
│   │   ├── LedgerReportScheduledExport.cs
│   │   ├── LedgerReportScheduleFrequency.cs
│   │   ├── LedgerReportSchedulePlanner.cs
│   │   ├── LedgerScheduledReportExportPackageBuilder.cs
│   │   ├── LedgerSnapshot.cs
│   │   ├── LedgerTaxLot.cs
│   │   ├── LedgerTaxLotBasisAdjuster.cs
│   │   ├── LedgerTaxLotBasisAdjustment.cs
│   │   ├── LedgerTaxLotBasisAdjustmentKind.cs
│   │   ├── LedgerTaxLotReliefHistoryProjector.cs
│   │   ├── LedgerTaxLotReliefInput.cs
│   │   ├── LedgerTaxLotReliefMethod.cs
│   │   ├── LedgerTaxLotReliefProjection.cs
│   │   ├── LedgerTaxLotReliefProjector.cs
│   │   ├── LedgerTaxLotReliefSelection.cs
│   │   ├── LedgerValidationException.cs
│   │   ├── LedgerViewKind.cs
│   │   ├── LockedAccountingPeriod.cs
│   │   ├── LockedAccountingPeriodBook.cs
│   │   ├── LotConsumption.cs
│   │   ├── Meridian.Ledger.csproj
│   │   ├── MultiCurrencyJournalInput.cs
│   │   ├── MultiCurrencyJournalLineInput.cs
│   │   ├── MultiCurrencyJournalLineProjection.cs
│   │   ├── MultiCurrencyJournalProjection.cs
│   │   ├── MultiCurrencyJournalProjector.cs
│   │   ├── MultiCurrencyLedgerTranslator.cs
│   │   ├── NavPerUnitCalculator.cs
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
│   │   ├── PeriodCloseDraftBuilder.cs
│   │   ├── PeriodCloseInput.cs
│   │   ├── PeriodCloseLine.cs
│   │   ├── PeriodCloseProjection.cs
│   │   ├── PeriodCloseProjector.cs
│   │   ├── PeriodReopenEvidence.cs
│   │   ├── PortfolioPricingRule.cs
│   │   ├── PreferredReturnCalculator.cs
│   │   ├── PrivateCapitalCommitments.cs
│   │   ├── PrivateCapitalFundEventLedgerProjector.cs
│   │   ├── ProjectLedgerBook.cs
│   │   ├── README.md
│   │   ├── ReadOnlyCollectionHelpers.cs
│   │   ├── RecurringJournalSchedule.cs
│   │   ├── ShadowNavOverrideDraft.cs
│   │   ├── ShadowNavValidationFinding.cs
│   │   ├── ShadowNavValidationPolicy.cs
│   │   ├── ShadowNavValidationReport.cs
│   │   ├── ShadowNavValidator.cs
│   │   ├── ShareClass.cs
│   │   ├── ShareClassUnitRegisterProjector.cs
│   │   ├── StalePricePolicy.cs
│   │   ├── TaxCharacter.cs
│   │   ├── WashSale.cs
│   │   └── YearEndClose.cs
│   ├── Meridian.LifecycleSupervisor
│   │   ├── LifecycleStartupOutcome.cs
│   │   ├── LifecycleSupervisorConfiguration.cs
│   │   ├── LifecycleSupervisorDatabase.cs
│   │   ├── LifecycleSupervisorPipe.cs
│   │   ├── LifecycleSupervisorRuntime.cs
│   │   ├── Meridian.LifecycleSupervisor.csproj
│   │   ├── Program.cs
│   │   └── README.md
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
│   │   │   ├── ProviderTools.cs
│   │   │   └── RepoEditTools.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.Mcp.csproj
│   │   ├── Program.cs
│   │   └── README.md
│   ├── Meridian.Platform
│   │   ├── ApiDocumentation
│   │   │   └── ApiDocumentationService.cs
│   │   ├── Coordination
│   │   │   ├── ClusterCoordinatorService.cs
│   │   │   ├── LeaseManager.cs
│   │   │   ├── ScheduledWorkOwnershipService.cs
│   │   │   ├── SplitBrainDetector.cs
│   │   │   └── SubscriptionOwnershipService.cs
│   │   ├── Diagnostics
│   │   │   ├── DiagnosticBundleService.cs
│   │   │   ├── ErrorRingBuffer.cs
│   │   │   ├── ErrorTracker.cs
│   │   │   ├── PipelineDiagnosticsProjection.cs
│   │   │   ├── ShutdownDiagnosticsService.cs
│   │   │   ├── ShutdownLifecycleTypes.cs
│   │   │   └── SystemHealthChecker.cs
│   │   ├── FundOperationsPersistence
│   │   │   ├── CanonicalProjectionSchemas.cs
│   │   │   ├── DomainReadSwitch.cs
│   │   │   ├── FileShadowProjectionWriter.cs
│   │   │   ├── FundOperationsPersistenceContracts.cs
│   │   │   └── ProjectionReconciliationHostedService.cs
│   │   ├── Monitoring
│   │   │   ├── Core
│   │   │   │   ├── AlertDispatcher.cs
│   │   │   │   ├── AlertRunbookRegistry.cs
│   │   │   │   ├── HealthCheckAggregator.cs
│   │   │   │   └── SloDefinitionRegistry.cs
│   │   │   ├── BackpressureAlertService.cs
│   │   │   └── CircuitBreakerStatusService.cs
│   │   ├── Performance
│   │   │   └── CoLocationProfileActivator.cs
│   │   ├── Results
│   │   │   ├── ErrorCode.cs
│   │   │   ├── FriendlyErrorFormatter.cs
│   │   │   ├── OperationError.cs
│   │   │   └── Result.cs
│   │   ├── Runtime
│   │   │   ├── CliModeResolver.cs
│   │   │   ├── DeploymentContext.cs
│   │   │   ├── GracefulShutdownHandler.cs
│   │   │   ├── GracefulShutdownService.cs
│   │   │   ├── ProgressDisplayService.cs
│   │   │   └── StartupSummary.cs
│   │   ├── Scheduling
│   │   │   ├── OperationalScheduler.cs
│   │   │   └── TradingCalendar.cs
│   │   ├── Tracing
│   │   │   ├── DefaultEventMetrics.cs
│   │   │   ├── EventTraceContext.cs
│   │   │   ├── Metrics.cs
│   │   │   ├── OpenTelemetrySetup.cs
│   │   │   └── TracedEventMetrics.cs
│   │   ├── DesignModule.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.Platform.csproj
│   │   └── README.md
│   ├── Meridian.PortfolioRecords
│   │   ├── Accounts
│   │   │   ├── IAccountManagementService.cs
│   │   │   └── IAccountQueryService.cs
│   │   ├── FundAccounts
│   │   │   ├── AccountReconciliationChecks.cs
│   │   │   ├── IFundAccountService.cs
│   │   │   ├── InMemoryFundAccountService.cs
│   │   │   └── PostgresFundAccountService.cs
│   │   ├── DesignModule.cs
│   │   ├── Meridian.PortfolioRecords.csproj
│   │   └── README.md
│   ├── Meridian.ProviderSdk
│   │   ├── AccountingSystem
│   │   │   └── IAccountingSystemProvider.cs
│   │   ├── Backfill
│   │   │   └── BackfillJob.cs
│   │   ├── AttributeCredentialResolver.cs
│   │   ├── ConfigurableProviderModuleBase.cs
│   │   ├── ConnectionDiagnosticsContracts.cs
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
│   │   ├── IMarketRuleProvider.cs
│   │   ├── ImplementsAdrAttribute.cs
│   │   ├── IOptionsChainProvider.cs
│   │   ├── IProviderConnectionDiagnosticsSource.cs
│   │   ├── IProviderDataReadService.cs
│   │   ├── IProviderFamilyAdapter.cs
│   │   ├── IProviderInstrumentDiscoveryService.cs
│   │   ├── IProviderMetadata.cs
│   │   ├── IProviderModule.cs
│   │   ├── IProviderModuleConnectionProbe.cs
│   │   ├── IProviderModuleCredentialHints.cs
│   │   ├── IProviderModuleSettingsSchema.cs
│   │   ├── IProviderNewsService.cs
│   │   ├── IProviderPnlStream.cs
│   │   ├── IProviderRateLimitDiagnosticsSource.cs
│   │   ├── IProviderScannerService.cs
│   │   ├── IRealtimeDataSource.cs
│   │   ├── ITradingCalendarProvider.cs
│   │   ├── Meridian.ProviderSdk.csproj
│   │   ├── PluginLoaderService.cs
│   │   ├── ProviderHttpUtilities.cs
│   │   ├── ProviderModuleContext.cs
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
│   │   ├── GlobalUsings.SecurityMasterConcerns.cs
│   │   ├── Meridian.QuantScript.csproj
│   │   ├── QuantScriptOptions.cs
│   │   ├── QuantScriptServiceCollectionExtensions.cs
│   │   ├── README.md
│   │   └── ScriptContext.cs
│   ├── Meridian.ReferenceData
│   │   ├── SecurityMaster
│   │   │   ├── Data
│   │   │   │   └── security-reference-taxonomies.json
│   │   │   ├── SecurityAssetProfileCatalog.cs
│   │   │   ├── SecurityKindMapping.cs
│   │   │   └── SecurityReferenceTaxonomyCatalog.cs
│   │   ├── DesignModule.cs
│   │   ├── Meridian.ReferenceData.csproj
│   │   └── README.md
│   ├── Meridian.Reporting
│   │   ├── CertifiedReportingSnapshot.cs
│   │   ├── CertifiedReportingSnapshotBuilder.cs
│   │   ├── DefaultReportingTemplateCatalog.cs
│   │   ├── DesignModule.cs
│   │   ├── Meridian.Reporting.csproj
│   │   ├── NavAttributionService.cs
│   │   ├── PartnersCapitalProjection.cs
│   │   ├── README.md
│   │   ├── ReportGenerationService.cs
│   │   ├── ReportingArtifactContracts.cs
│   │   ├── ReportingArtifactDeclaration.cs
│   │   ├── ReportingCanonicalParameterSerializer.cs
│   │   ├── ReportingCertifiedManifestValidation.cs
│   │   ├── ReportingContracts.cs
│   │   ├── ReportingDistributionContracts.cs
│   │   ├── ReportingGovernanceCanonicalValidation.cs
│   │   ├── ReportingGovernanceContracts.cs
│   │   ├── ReportingGovernanceService.cs
│   │   ├── ReportingNumberFormat.cs
│   │   ├── ReportingOperationalStoreContracts.cs
│   │   ├── ReportingOrchestrationService.cs
│   │   ├── ReportingReconciliationEvidenceContracts.cs
│   │   ├── ReportingReleaseConsistencyGate.cs
│   │   ├── ReportingStarterKitCatalog.cs
│   │   ├── ReportSnapshotDiffEngine.cs
│   │   ├── ReportWriterGridEngine.cs
│   │   ├── SecurityMasterReportingLookup.cs
│   │   └── StatementReconciliationReportAuthorityContracts.cs
│   ├── Meridian.Risk
│   │   ├── Rules
│   │   │   ├── DrawdownCircuitBreaker.cs
│   │   │   ├── GrossExposureRule.cs
│   │   │   ├── OrderNotionalResolver.cs
│   │   │   ├── OrderNotionalRule.cs
│   │   │   ├── OrderRateThrottle.cs
│   │   │   ├── PositionLimitRule.cs
│   │   │   └── SymbolConcentrationRule.cs
│   │   ├── CompositeRiskValidator.cs
│   │   ├── IRiskRule.cs
│   │   ├── Meridian.Risk.csproj
│   │   ├── PortfolioExposure.cs
│   │   └── README.md
│   ├── Meridian.Setup
│   │   ├── Meridian.Setup.csproj
│   │   ├── Program.cs
│   │   └── README.md
│   ├── Meridian.Storage
│   │   ├── Archival
│   │   │   ├── ArchivalStorageService.cs
│   │   │   ├── AtomicFileWriter.cs
│   │   │   ├── CompressionProfileManager.cs
│   │   │   └── WriteAheadLog.cs
│   │   ├── AssetOperations
│   │   │   ├── Migrations
│   │   │   │   ├── 001_asset_operations.sql
│   │   │   │   ├── 002_instrument_position_projections.sql
│   │   │   │   ├── 003_instrument_position_projection_guards.sql
│   │   │   │   └── 004_asset_accounting_event_spine.sql
│   │   │   ├── AssetOperationsMigrationRunner.cs
│   │   │   ├── IAssetAccountingEventProjectionStore.cs
│   │   │   ├── IAssetOperationsProjectionStore.cs
│   │   │   ├── IInstrumentPositionProjectionStore.cs
│   │   │   ├── InMemoryAssetOperationsProjectionStore.AssetAccountingEvents.cs
│   │   │   ├── InMemoryAssetOperationsProjectionStore.cs
│   │   │   ├── InMemoryAssetOperationsProjectionStore.InstrumentPositions.cs
│   │   │   ├── PostgresAssetOperationsProjectionStore.AssetAccountingEvents.cs
│   │   │   ├── PostgresAssetOperationsProjectionStore.cs
│   │   │   ├── PostgresAssetOperationsProjectionStore.InstrumentPositions.cs
│   │   │   └── PostgresAssetOperationsProjectionStore.Locks.cs
│   │   ├── Backfill
│   │   │   ├── BackfillStatusStore.cs
│   │   │   └── BackfillStatusStoreJsonContext.cs
│   │   ├── Banking
│   │   │   ├── Migrations
│   │   │   │   ├── 001_banking.sql
│   │   │   │   └── 002_bank_transaction_recorded_by.sql
│   │   │   ├── BankingMigrationRunner.cs
│   │   │   ├── BankingStoreOptions.cs
│   │   │   ├── IBankingStore.cs
│   │   │   └── PostgresBankingStore.cs
│   │   ├── Config
│   │   │   └── StorageConfigExtensions.cs
│   │   ├── Coordination
│   │   │   └── SharedStorageCoordinationStore.cs
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
│   │   │   │   ├── 006_servicer_statement_intake.sql
│   │   │   │   ├── 007_direct_lending_command_idempotency.sql
│   │   │   │   └── 008_direct_lending_pik_accrual.sql
│   │   │   ├── DirectLendingMigrationRunner.cs
│   │   │   ├── DirectLendingPersistenceBatch.cs
│   │   │   ├── IDirectLendingOperationsStore.cs
│   │   │   ├── IDirectLendingStateStore.cs
│   │   │   ├── PostgresDirectLendingStateStore.cs
│   │   │   ├── PostgresDirectLendingStateStore.Operations.cs
│   │   │   └── PostgresDirectLendingStateStore.WorkflowAudit.cs
│   │   ├── Etl
│   │   │   ├── EtlJobDefinitionStore.cs
│   │   │   └── EtlStores.cs
│   │   ├── Export
│   │   │   ├── AnalysisExportService.cs
│   │   │   ├── AnalysisExportService.Execution.cs
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
│   │   │   ├── SpreadsheetFormulaGuard.cs
│   │   │   └── XlsxWorkbookWriter.cs
│   │   ├── FundAccounts
│   │   │   ├── Migrations
│   │   │   │   ├── 001_fund_accounts.sql
│   │   │   │   ├── 002_add_operational_status.sql
│   │   │   │   ├── 003_fund_account_tenant_column.sql
│   │   │   │   └── 004_legacy_import_receipts.sql
│   │   │   ├── FundAccountMigrationRunner.cs
│   │   │   ├── FundAccountStoreOptions.cs
│   │   │   ├── IFundAccountStore.cs
│   │   │   └── PostgresFundAccountStore.cs
│   │   ├── FundStructure
│   │   │   ├── Migrations
│   │   │   │   ├── 001_fund_structure.sql
│   │   │   │   ├── 002_legacy_import_receipts.sql
│   │   │   │   └── 003_linked_accounts.sql
│   │   │   ├── FundStructureMigrationRunner.cs
│   │   │   ├── FundStructureStoreOptions.cs
│   │   │   ├── IFundStructureStateStore.cs
│   │   │   ├── IFundStructureStore.cs
│   │   │   ├── InMemoryFundStructureStateStore.cs
│   │   │   ├── JsonFileFundStructureStateStore.cs
│   │   │   └── PostgresFundStructureStore.cs
│   │   ├── Integrations
│   │   │   └── FileProviderIntegrationManifestStore.cs
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
│   │   │   │   ├── V_ledger_008__closing_entry_posting_kind.sql
│   │   │   │   ├── V_ledger_008__operations_continuity.sql
│   │   │   │   ├── V_ledger_009__tax_lot_persistence.sql
│   │   │   │   ├── V_ledger_010__accounting_configuration.sql
│   │   │   │   ├── V_ledger_011__accounting_rule_payload.sql
│   │   │   │   ├── V_ledger_012__accounting_rule_test_cases.sql
│   │   │   │   ├── V_ledger_013__journal_idempotency_guards.sql
│   │   │   │   ├── V_ledger_014__journal_leg_dimensions.sql
│   │   │   │   ├── V_ledger_015__accounting_configuration_ledger_book_scope.sql
│   │   │   │   ├── V_ledger_016__accounting_configuration_tenant_company_scope.sql
│   │   │   │   ├── V_ledger_017__accounting_configuration_audit_tenant_scope.sql
│   │   │   │   ├── V_ledger_018__accounting_audit_fund_lower_index.sql
│   │   │   │   ├── V_ledger_019__fund_profile_tenancy.sql
│   │   │   │   ├── V_ledger_020__fund_scope_tenant_columns.sql
│   │   │   │   ├── V_ledger_021__operations_continuity_tenant_column.sql
│   │   │   │   ├── V_ledger_022__tenant_lower_indexes.sql
│   │   │   │   ├── V_ledger_023__journal_as_of_indexes.sql
│   │   │   │   ├── V_ledger_024__tax_lot_average_cost_method.sql
│   │   │   │   ├── V_ledger_025__global_posting_command_identity.sql
│   │   │   │   ├── V_ledger_026__journal_leg_currency.sql
│   │   │   │   ├── V_ledger_027__atomic_tax_lot_posting.sql
│   │   │   │   └── V_ledger_028__wash_sale_activation.sql
│   │   │   ├── AccountingPostingCommandFingerprintJsonContext.cs
│   │   │   ├── AccountingPostingCommandValidator.cs
│   │   │   ├── AtomicTaxLotJournalFingerprint.cs
│   │   │   ├── DurableAutomatedJournalPoster.cs
│   │   │   ├── GovernedLedgerPostingTarget.cs
│   │   │   ├── ILedgerJournalStore.cs
│   │   │   ├── LedgerBookServiceException.cs
│   │   │   ├── LedgerJournalStoreHydrationExtensions.cs
│   │   │   ├── LedgerJournalStoreOptions.cs
│   │   │   ├── LedgerMigrationRunner.cs
│   │   │   ├── LedgerPeriodPostingGuard.cs
│   │   │   ├── LedgerStoreExtensions.cs
│   │   │   ├── PostgresAccountingConfigurationStore.cs
│   │   │   ├── PostgresFundProfileTenancyRegistry.cs
│   │   │   ├── PostgresLedgerBookService.cs
│   │   │   ├── PostgresLedgerJournalStore.AtomicTaxLots.cs
│   │   │   ├── PostgresLedgerJournalStore.cs
│   │   │   ├── PostgresLedgerJournalStore.Serialization.cs
│   │   │   ├── PostgresLedgerJournalStore.TaxLotDisposalHistory.cs
│   │   │   ├── PostgresLedgerJournalStore.Validation.cs
│   │   │   ├── PostgresLedgerJournalStore.WashSale.cs
│   │   │   └── WashSaleDeferralRecord.cs
│   │   ├── Maintenance
│   │   │   ├── ArchiveMaintenanceModels.cs
│   │   │   ├── ArchiveMaintenanceScheduleManager.cs
│   │   │   ├── IArchiveMaintenanceScheduleManager.cs
│   │   │   ├── IArchiveMaintenanceService.cs
│   │   │   ├── IMaintenanceExecutionHistory.cs
│   │   │   └── ScheduledArchiveMaintenanceService.cs
│   │   ├── Migrations
│   │   │   ├── PostgresMigrationRunner.cs
│   │   │   └── PostgresMigrationRunnerOptions.cs
│   │   ├── MoneyMarket
│   │   │   ├── Migrations
│   │   │   │   └── 001_money_market.sql
│   │   │   ├── IMoneyMarketFundAuxStore.cs
│   │   │   ├── MoneyMarketMigrationRunner.cs
│   │   │   ├── MoneyMarketStoreOptions.cs
│   │   │   └── PostgresMoneyMarketFundStore.cs
│   │   ├── Operations
│   │   │   └── FileOperationalCaseHistoryStore.cs
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
│   │   ├── Query
│   │   │   └── DuckDbQueryService.cs
│   │   ├── Replay
│   │   │   ├── CompressedJsonlStream.cs
│   │   │   ├── JsonlReplayer.cs
│   │   │   └── MemoryMappedJsonlReader.cs
│   │   ├── Reporting
│   │   │   ├── Migrations
│   │   │   │   ├── 001_reporting_artifact_blobs.sql
│   │   │   │   ├── 002_reporting_governance.sql
│   │   │   │   ├── 003_reporting_distribution.sql
│   │   │   │   ├── 004_reporting_artifact_catalog_audit.sql
│   │   │   │   ├── 005_reporting_distribution_hardening.sql
│   │   │   │   ├── 006_reporting_reconciliation_evidence.sql
│   │   │   │   ├── 007_reporting_governance_scope_hardening.sql
│   │   │   │   ├── 008_reporting_governance_format_versions.sql
│   │   │   │   ├── 009_reporting_reconciliation_evidence_v2.sql
│   │   │   │   ├── 010_reporting_operational_state.sql
│   │   │   │   ├── 011_reporting_delivery_run_read_index.sql
│   │   │   │   ├── 012_reporting_access_grant_artifact_consumption.sql
│   │   │   │   └── 013_reporting_statement_reconciliation_authority.sql
│   │   │   ├── PostgresReportingAccessGrantStore.cs
│   │   │   ├── PostgresReportingArtifactAuditStore.cs
│   │   │   ├── PostgresReportingArtifactCatalog.cs
│   │   │   ├── PostgresReportingArtifactStore.cs
│   │   │   ├── PostgresReportingDeliveryStore.cs
│   │   │   ├── PostgresReportingDeploymentProbe.cs
│   │   │   ├── PostgresReportingGovernanceRepository.cs
│   │   │   ├── PostgresReportingReconciliationEvidenceStore.cs
│   │   │   ├── PostgresReportingReleaseConsistencyGate.cs
│   │   │   ├── PostgresReportingRunStore.cs
│   │   │   ├── PostgresReportingScheduleStore.cs
│   │   │   ├── PostgresStatementReconciliationReportAuthorityStore.cs
│   │   │   ├── ReportingArtifactCatalogJsonContext.cs
│   │   │   ├── ReportingArtifactStoreOptions.cs
│   │   │   ├── ReportingDistributionStateCorruptionException.cs
│   │   │   ├── ReportingDistributionStoreGuard.cs
│   │   │   ├── ReportingGovernanceJsonContext.cs
│   │   │   ├── ReportingGovernanceLegacyContracts.cs
│   │   │   ├── ReportingMigrationRunner.cs
│   │   │   └── ReportingReconciliationEvidenceJsonContext.cs
│   │   ├── Runtime
│   │   │   └── JsonLifecycleReceiptStore.cs
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
│   │   │   │   ├── 016_security_master_normalized_identifier_lookup.sql
│   │   │   │   ├── 017_security_master_bond_clearwater_lifecycle_fields.sql
│   │   │   │   ├── 018_security_master_cashflow_sources.sql
│   │   │   │   ├── 019_data_vendor_entitlements.sql
│   │   │   │   ├── 020_data_vendor_entitlement_scope_metadata.sql
│   │   │   │   ├── 021_security_master_corp_action_lifecycle.sql
│   │   │   │   ├── 022_security_master_pricing_hierarchy.sql
│   │   │   │   ├── 023_security_master_quality_reports.sql
│   │   │   │   ├── 024_security_master_schema_version_column.sql
│   │   │   │   ├── 025_security_master_audit_stores.sql
│   │   │   │   └── 026_security_master_operator_override_approvals.sql
│   │   │   ├── FileEdgarReferenceDataStore.cs
│   │   │   ├── IBondReferenceProjectionStore.cs
│   │   │   ├── ICertificateOfDepositReferenceProjectionStore.cs
│   │   │   ├── ICommodityReferenceProjectionStore.cs
│   │   │   ├── ICryptoReferenceProjectionStore.cs
│   │   │   ├── IDataVendorEntitlementStore.cs
│   │   │   ├── IDepositReferenceProjectionStore.cs
│   │   │   ├── IEdgarReferenceDataStore.cs
│   │   │   ├── IEquityReferenceProjectionStore.cs
│   │   │   ├── IFutureReferenceProjectionStore.cs
│   │   │   ├── IFxSpotReferenceProjectionStore.cs
│   │   │   ├── IMoneyMarketFundReferenceProjectionStore.cs
│   │   │   ├── IOperatorOverridesStore.cs
│   │   │   ├── IOptionReferenceProjectionStore.cs
│   │   │   ├── ISecurityMasterCashFlowStore.cs
│   │   │   ├── ISecurityMasterEventStore.cs
│   │   │   ├── ISecurityMasterPricingStore.cs
│   │   │   ├── ISecurityMasterQualityReportStore.cs
│   │   │   ├── ISecurityMasterSnapshotStore.cs
│   │   │   ├── ISecurityMasterStore.cs
│   │   │   ├── ISwapReferenceProjectionStore.cs
│   │   │   ├── PostgresBondReferenceProjectionStore.cs
│   │   │   ├── PostgresCertificateOfDepositReferenceProjectionStore.cs
│   │   │   ├── PostgresCommodityReferenceProjectionStore.cs
│   │   │   ├── PostgresCryptoReferenceProjectionStore.cs
│   │   │   ├── PostgresDataVendorEntitlementStore.cs
│   │   │   ├── PostgresDepositReferenceProjectionStore.cs
│   │   │   ├── PostgresEquityReferenceProjectionStore.cs
│   │   │   ├── PostgresFutureReferenceProjectionStore.cs
│   │   │   ├── PostgresFxSpotReferenceProjectionStore.cs
│   │   │   ├── PostgresMoneyMarketFundReferenceProjectionStore.cs
│   │   │   ├── PostgresOperatorOverridesStore.cs
│   │   │   ├── PostgresOptionReferenceProjectionStore.cs
│   │   │   ├── PostgresSecurityMasterCashFlowStore.cs
│   │   │   ├── PostgresSecurityMasterEventStore.cs
│   │   │   ├── PostgresSecurityMasterPricingStore.cs
│   │   │   ├── PostgresSecurityMasterQualityReportStore.cs
│   │   │   ├── PostgresSecurityMasterSnapshotStore.cs
│   │   │   ├── PostgresSecurityMasterStore.cs
│   │   │   ├── PostgresSwapReferenceProjectionStore.cs
│   │   │   ├── SecurityMasterDbMapper.cs
│   │   │   ├── SecurityMasterMigrationRunner.cs
│   │   │   └── SecurityMasterProjectionCache.cs
│   │   ├── Services
│   │   │   ├── AdaptivePartitionPlacementPlanner.cs
│   │   │   ├── AuditChainService.cs
│   │   │   ├── CanonicalSymbolRegistry.cs
│   │   │   ├── DataLineageService.cs
│   │   │   ├── DataQualityScoringService.cs
│   │   │   ├── DataQualityService.cs
│   │   │   ├── DataReplacementCostEstimator.cs
│   │   │   ├── EventBuffer.cs
│   │   │   ├── FileMaintenanceService.cs
│   │   │   ├── FilePermissionsService.cs
│   │   │   ├── JsonlPositionSnapshotStore.cs
│   │   │   ├── LifecyclePolicyEngine.cs
│   │   │   ├── MaintenanceScheduler.cs
│   │   │   ├── MetadataTagService.cs
│   │   │   ├── ParquetConversionService.cs
│   │   │   ├── QualityTrendStore.cs
│   │   │   ├── QualityTrendStore.Persistence.cs
│   │   │   ├── QualityTrendStoreJsonContext.cs
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
│   │   │   ├── JsonFileIBDataResultStore.cs
│   │   │   ├── JsonFileSnapshotStore.cs
│   │   │   └── JsonlMarketDataStore.cs
│   │   ├── GlobalUsings.cs
│   │   ├── Meridian.Storage.csproj
│   │   ├── MeridianDatabaseEnvironment.cs
│   │   ├── README.md
│   │   ├── StorageOptions.cs
│   │   ├── StorageProfiles.cs
│   │   ├── StorageSinkAttribute.cs
│   │   └── StorageSinkRegistry.cs
│   ├── Meridian.Strategies
│   │   ├── Interfaces
│   │   │   ├── ILiveStrategy.cs
│   │   │   ├── IPromotedRunLauncher.cs
│   │   │   ├── IPromotionRecordStore.cs
│   │   │   ├── IStrategyDesignRepository.cs
│   │   │   ├── IStrategyLifecycle.cs
│   │   │   └── IStrategyRepository.cs
│   │   ├── Live
│   │   │   ├── Strategies
│   │   │   │   ├── BuyAndHoldLiveStrategy.cs
│   │   │   │   └── MovingAverageCrossoverLiveStrategy.cs
│   │   │   ├── BacktestStrategyLiveAdapter.cs
│   │   │   ├── IBacktestStrategyLiveSource.cs
│   │   │   ├── LiveRunMetricsTracker.cs
│   │   │   ├── LiveStrategyBase.cs
│   │   │   ├── LiveStrategyCatalog.cs
│   │   │   ├── LiveStrategyExecutionContext.cs
│   │   │   ├── LiveStrategyRunSession.cs
│   │   │   ├── LiveTradingEngine.cs
│   │   │   └── LiveTradingEngineOptions.cs
│   │   ├── Models
│   │   │   ├── RunType.cs
│   │   │   ├── StrategyRunEntry.cs
│   │   │   ├── StrategyRunLifecycleEventType.cs
│   │   │   ├── StrategyRunRepositoryQuery.cs
│   │   │   └── StrategyStatus.cs
│   │   ├── Promotions
│   │   │   ├── BacktestToLivePromoter.cs
│   │   │   ├── PromotionApprovalChecklist.cs
│   │   │   └── PromotionRecordService.cs
│   │   ├── Serialization
│   │   │   ├── FSharpInteropJsonContext.cs
│   │   │   ├── PromotionRecordJsonContext.cs
│   │   │   ├── StrategyDesignJsonContext.cs
│   │   │   └── StrategyRunPersistenceJsonContext.cs
│   │   ├── Services
│   │   │   ├── AggregatePortfolioService.cs
│   │   │   ├── CashFlowProjectionService.cs
│   │   │   ├── FileReconciliationBreakQueueRepository.Casework.cs
│   │   │   ├── FileReconciliationBreakQueueRepository.CloseScope.cs
│   │   │   ├── FileReconciliationBreakQueueRepository.cs
│   │   │   ├── FileReconciliationBreakQueueRepository.Persistence.cs
│   │   │   ├── FileReconciliationRunRepository.cs
│   │   │   ├── GovernanceExceptionService.cs
│   │   │   ├── IAggregatePortfolioService.cs
│   │   │   ├── InMemoryReconciliationRunRepository.cs
│   │   │   ├── IReconciliationBreakQueueRepository.cs
│   │   │   ├── IReconciliationRunRepository.cs
│   │   │   ├── IReconciliationRunService.cs
│   │   │   ├── IReconciliationSlaPolicyProvider.cs
│   │   │   ├── ISecurityReferenceLookup.cs
│   │   │   ├── LedgerReadService.cs
│   │   │   ├── PortfolioReadService.cs
│   │   │   ├── PromotionService.cs
│   │   │   ├── ReconciliationCaseWorkflowService.cs
│   │   │   ├── ReconciliationCaseWorkflowVocabulary.cs
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
│   │   ├── GlobalUsings.SecurityMasterConcerns.cs
│   │   ├── Meridian.Strategies.csproj
│   │   └── README.md
│   ├── Meridian.Ui
│   │   ├── dashboard
│   │   │   ├── scripts
│   │   │   │   ├── eslint-rules
│   │   │   │   │   └── kebab-filename.mjs
│   │   │   │   ├── run-vitest-stable.mjs
│   │   │   │   └── smoke-workstation.mjs
│   │   │   ├── src
│   │   │   │   ├── assets
│   │   │   │   │   ├── brand
│   │   │   │   │   │   ├── meridian-hero.svg
│   │   │   │   │   │   ├── meridian-mark-light.svg
│   │   │   │   │   │   ├── meridian-mark-monochrome.svg
│   │   │   │   │   │   ├── meridian-mark.svg
│   │   │   │   │   │   ├── meridian-symbol.svg
│   │   │   │   │   │   ├── meridian-tile-256.png
│   │   │   │   │   │   ├── meridian-tile.svg
│   │   │   │   │   │   ├── meridian-wordmark-stacked.svg
│   │   │   │   │   │   ├── meridian-wordmark.svg
│   │   │   │   │   │   └── README.md
│   │   │   │   │   ├── icons
│   │   │   │   │   │   ├── account-portfolio.svg
│   │   │   │   │   │   ├── admin-maintenance.svg
│   │   │   │   │   │   ├── aggregate-portfolio.svg
│   │   │   │   │   │   ├── archive-health.svg
│   │   │   │   │   │   ├── backfill.svg
│   │   │   │   │   │   ├── backtest.svg
│   │   │   │   │   │   ├── charting.svg
│   │   │   │   │   │   ├── collection-sessions.svg
│   │   │   │   │   │   ├── dashboard.svg
│   │   │   │   │   │   ├── data-browser.svg
│   │   │   │   │   │   ├── data-calendar.svg
│   │   │   │   │   │   ├── data-export.svg
│   │   │   │   │   │   ├── data-operations.svg
│   │   │   │   │   │   ├── data-quality.svg
│   │   │   │   │   │   ├── data-sampling.svg
│   │   │   │   │   │   ├── data-sources.svg
│   │   │   │   │   │   ├── diagnostics.svg
│   │   │   │   │   │   ├── event-replay.svg
│   │   │   │   │   │   ├── formula.svg
│   │   │   │   │   │   ├── governance.svg
│   │   │   │   │   │   ├── help.svg
│   │   │   │   │   │   ├── index-subscription.svg
│   │   │   │   │   │   ├── keyboard-shortcuts.svg
│   │   │   │   │   │   ├── lean-integration.svg
│   │   │   │   │   │   ├── live-data.svg
│   │   │   │   │   │   ├── order-book.svg
│   │   │   │   │   │   ├── portfolio-import.svg
│   │   │   │   │   │   ├── provider-health.svg
│   │   │   │   │   │   ├── README.md
│   │   │   │   │   │   ├── research.svg
│   │   │   │   │   │   ├── retention-assurance.svg
│   │   │   │   │   │   ├── run-detail.svg
│   │   │   │   │   │   ├── run-ledger.svg
│   │   │   │   │   │   ├── run-mat.svg
│   │   │   │   │   │   ├── run-portfolio.svg
│   │   │   │   │   │   ├── schedule-manager.svg
│   │   │   │   │   │   ├── security-master.svg
│   │   │   │   │   │   ├── service-manager.svg
│   │   │   │   │   │   ├── settings.svg
│   │   │   │   │   │   ├── storage-optimization.svg
│   │   │   │   │   │   ├── storage.svg
│   │   │   │   │   │   ├── strategy-builder.svg
│   │   │   │   │   │   ├── strategy-runs.svg
│   │   │   │   │   │   ├── symbol-storage.svg
│   │   │   │   │   │   ├── symbols.svg
│   │   │   │   │   │   ├── system-health.svg
│   │   │   │   │   │   ├── trading-hours.svg
│   │   │   │   │   │   ├── trading.svg
│   │   │   │   │   │   └── watchlist.svg
│   │   │   │   │   └── app.ico
│   │   │   │   ├── components
│   │   │   │   │   ├── accounting
│   │   │   │   │   │   ├── AccountTree.test.tsx
│   │   │   │   │   │   ├── AccountTree.tsx
│   │   │   │   │   │   ├── AgingTable.test.tsx
│   │   │   │   │   │   ├── AgingTable.tsx
│   │   │   │   │   │   ├── AmountCell.test.tsx
│   │   │   │   │   │   ├── AmountCell.tsx
│   │   │   │   │   │   ├── index.ts
│   │   │   │   │   │   ├── JournalEntryForm.test.tsx
│   │   │   │   │   │   ├── JournalEntryForm.tsx
│   │   │   │   │   │   ├── LedgerTable.test.tsx
│   │   │   │   │   │   ├── LedgerTable.tsx
│   │   │   │   │   │   ├── money.test.ts
│   │   │   │   │   │   ├── money.ts
│   │   │   │   │   │   ├── ReconciliationComparisonPanel.test.tsx
│   │   │   │   │   │   ├── ReconciliationComparisonPanel.tsx
│   │   │   │   │   │   ├── ReconciliationPanel.test.tsx
│   │   │   │   │   │   ├── ReconciliationPanel.tsx
│   │   │   │   │   │   ├── StatementTable.test.tsx
│   │   │   │   │   │   ├── StatementTable.tsx
│   │   │   │   │   │   ├── TaxLotTable.test.tsx
│   │   │   │   │   │   ├── TaxLotTable.tsx
│   │   │   │   │   │   ├── TrialBalanceRowDetail.test.tsx
│   │   │   │   │   │   ├── TrialBalanceRowDetail.tsx
│   │   │   │   │   │   ├── TrialBalanceTable.test.tsx
│   │   │   │   │   │   └── TrialBalanceTable.tsx
│   │   │   │   │   ├── charts
│   │   │   │   │   │   ├── CandleChart.tsx
│   │   │   │   │   │   ├── chart-interaction.test.ts
│   │   │   │   │   │   ├── chart-interaction.tsx
│   │   │   │   │   │   ├── chart-sync.test.tsx
│   │   │   │   │   │   ├── chart-sync.tsx
│   │   │   │   │   │   ├── ChartCard.tsx
│   │   │   │   │   │   ├── charts.test.tsx
│   │   │   │   │   │   ├── CorrelationHeatmap.tsx
│   │   │   │   │   │   ├── DepthChart.tsx
│   │   │   │   │   │   ├── DrawdownChart.tsx
│   │   │   │   │   │   ├── EquityCurve.tsx
│   │   │   │   │   │   ├── Histogram.tsx
│   │   │   │   │   │   ├── index.ts
│   │   │   │   │   │   ├── Sparkline.tsx
│   │   │   │   │   │   └── ticks.ts
│   │   │   │   │   ├── data
│   │   │   │   │   │   ├── add-provider-drawer.tsx
│   │   │   │   │   │   ├── backfill-validation-dashboard.tsx
│   │   │   │   │   │   ├── concrete.ts
│   │   │   │   │   │   ├── edit-provider-drawer.tsx
│   │   │   │   │   │   ├── empty-state.concrete.test.tsx
│   │   │   │   │   │   ├── empty-state.tsx
│   │   │   │   │   │   ├── entity-summary.tsx
│   │   │   │   │   │   ├── key-value-grid.concrete.test.tsx
│   │   │   │   │   │   ├── key-value-grid.tsx
│   │   │   │   │   │   ├── metric-card.concrete.test.tsx
│   │   │   │   │   │   ├── metric-card.tsx
│   │   │   │   │   │   ├── pagination.concrete.test.tsx
│   │   │   │   │   │   ├── pagination.tsx
│   │   │   │   │   │   ├── provider-capability-badges.tsx
│   │   │   │   │   │   ├── provider-setup-panel.test.tsx
│   │   │   │   │   │   ├── provider-setup-panel.tsx
│   │   │   │   │   │   ├── skeleton.concrete.test.tsx
│   │   │   │   │   │   ├── skeleton.tsx
│   │   │   │   │   │   ├── symbol-universe-manager.test.tsx
│   │   │   │   │   │   └── symbol-universe-manager.tsx
│   │   │   │   │   ├── meridian
│   │   │   │   │   │   ├── activity-center.tsx
│   │   │   │   │   │   ├── bias-disclosure-panel.tsx
│   │   │   │   │   │   ├── command-palette.actions.test.ts
│   │   │   │   │   │   ├── command-palette.actions.ts
│   │   │   │   │   │   ├── command-palette.entity-search.test.ts
│   │   │   │   │   │   ├── command-palette.entity-search.ts
│   │   │   │   │   │   ├── command-palette.test.tsx
│   │   │   │   │   │   ├── command-palette.tsx
│   │   │   │   │   │   ├── command-palette.view-model.test.ts
│   │   │   │   │   │   ├── command-palette.view-model.ts
│   │   │   │   │   │   ├── companion-pane-window.test.tsx
│   │   │   │   │   │   ├── companion-pane-window.tsx
│   │   │   │   │   │   ├── copy-link-button.test.tsx
│   │   │   │   │   │   ├── copy-link-button.tsx
│   │   │   │   │   │   ├── coverage-passport-drill-in.test.tsx
│   │   │   │   │   │   ├── coverage-passport-drill-in.tsx
│   │   │   │   │   │   ├── coverage-passport-drill-in.view-model.test.ts
│   │   │   │   │   │   ├── coverage-passport-drill-in.view-model.ts
│   │   │   │   │   │   ├── data-provenance-banner.test.tsx
│   │   │   │   │   │   ├── data-provenance-banner.tsx
│   │   │   │   │   │   ├── decision-brief-pill.tsx
│   │   │   │   │   │   ├── degraded-mode-banner.test.tsx
│   │   │   │   │   │   ├── degraded-mode-banner.tsx
│   │   │   │   │   │   ├── dense-row-detail-accessibility.test.tsx
│   │   │   │   │   │   ├── dense-row-detail-accessibility.tsx
│   │   │   │   │   │   ├── financial-record-explorer.test.tsx
│   │   │   │   │   │   ├── financial-record-explorer.tsx
│   │   │   │   │   │   ├── financial-record-explorer.view-state.test.ts
│   │   │   │   │   │   ├── financial-record-explorer.view-state.ts
│   │   │   │   │   │   ├── historical-chart.test.tsx
│   │   │   │   │   │   ├── historical-chart.tsx
│   │   │   │   │   │   ├── historical-chart.view-model.test.ts
│   │   │   │   │   │   ├── historical-chart.view-model.ts
│   │   │   │   │   │   ├── layout-switcher.test.tsx
│   │   │   │   │   │   ├── layout-switcher.tsx
│   │   │   │   │   │   ├── lifecycle-control-panel.test.tsx
│   │   │   │   │   │   ├── lifecycle-control-panel.tsx
│   │   │   │   │   │   ├── metric-card.test.tsx
│   │   │   │   │   │   ├── metric-card.tsx
│   │   │   │   │   │   ├── metric-card.view-model.test.ts
│   │   │   │   │   │   ├── metric-card.view-model.ts
│   │   │   │   │   │   ├── notification-center.test.tsx
│   │   │   │   │   │   ├── notification-center.tsx
│   │   │   │   │   │   ├── number-passport.test.ts
│   │   │   │   │   │   ├── number-passport.tsx
│   │   │   │   │   │   ├── onboarding-tour.tsx
│   │   │   │   │   │   ├── operational-trust-summary.test.tsx
│   │   │   │   │   │   ├── operational-trust-summary.tsx
│   │   │   │   │   │   ├── pop-out-pane-button.test.tsx
│   │   │   │   │   │   ├── pop-out-pane-button.tsx
│   │   │   │   │   │   ├── quant-notebook.test.tsx
│   │   │   │   │   │   ├── quant-notebook.tsx
│   │   │   │   │   │   ├── quant-notebook.view-model.test.ts
│   │   │   │   │   │   ├── quant-notebook.view-model.ts
│   │   │   │   │   │   ├── quant-plot.test.tsx
│   │   │   │   │   │   ├── quant-plot.tsx
│   │   │   │   │   │   ├── quant-plot.view-model.ts
│   │   │   │   │   │   ├── report-writer-chart-preview.tsx
│   │   │   │   │   │   ├── report-writer-grid-diff-view.tsx
│   │   │   │   │   │   ├── reporting-hub.test.tsx
│   │   │   │   │   │   ├── reporting-hub.tsx
│   │   │   │   │   │   ├── reporting-period-switcher.tsx
│   │   │   │   │   │   ├── save-view-dialog.test.tsx
│   │   │   │   │   │   ├── save-view-dialog.tsx
│   │   │   │   │   │   ├── scope-picker.test.tsx
│   │   │   │   │   │   ├── scope-picker.tsx
│   │   │   │   │   │   ├── security-details-tracker.test.tsx
│   │   │   │   │   │   ├── security-details-tracker.tsx
│   │   │   │   │   │   ├── security-details-tracker.view-model.test.ts
│   │   │   │   │   │   ├── security-details-tracker.view-model.ts
│   │   │   │   │   │   ├── security-passport-editor-launcher.test.tsx
│   │   │   │   │   │   ├── security-passport-editor-launcher.tsx
│   │   │   │   │   │   ├── security-passport-editor.test.tsx
│   │   │   │   │   │   ├── security-passport-editor.tsx
│   │   │   │   │   │   ├── security-passport-editor.view-model.test.ts
│   │   │   │   │   │   ├── security-passport-editor.view-model.ts
│   │   │   │   │   │   ├── stat-strip.tsx
│   │   │   │   │   │   ├── strategy-formula-workbench.test.tsx
│   │   │   │   │   │   ├── strategy-formula-workbench.tsx
│   │   │   │   │   │   ├── ui-kit-primitives.test.tsx
│   │   │   │   │   │   ├── ui-kit-primitives.tsx
│   │   │   │   │   │   ├── ui-kit-primitives.virtualization.test.tsx
│   │   │   │   │   │   ├── use-workspace-expansion.ts
│   │   │   │   │   │   ├── workflow-continuity-dock.route-mode.test.tsx
│   │   │   │   │   │   ├── workflow-continuity-dock.test.tsx
│   │   │   │   │   │   ├── workflow-continuity-dock.tsx
│   │   │   │   │   │   ├── workspace-header.test.tsx
│   │   │   │   │   │   ├── workspace-header.tsx
│   │   │   │   │   │   ├── workspace-header.view-model.test.ts
│   │   │   │   │   │   ├── workspace-header.view-model.ts
│   │   │   │   │   │   ├── workspace-nav.test.tsx
│   │   │   │   │   │   ├── workspace-nav.tsx
│   │   │   │   │   │   ├── workspace-nav.view-model.test.ts
│   │   │   │   │   │   ├── workspace-nav.view-model.ts
│   │   │   │   │   │   ├── workspace-primitives.test.tsx
│   │   │   │   │   │   ├── workspace-primitives.tsx
│   │   │   │   │   │   ├── workspace-workbench-shell.test.tsx
│   │   │   │   │   │   ├── workspace-workbench-shell.tsx
│   │   │   │   │   │   ├── workstation-status-bar.test.tsx
│   │   │   │   │   │   ├── workstation-status-bar.tsx
│   │   │   │   │   │   ├── workstation-topbar.test.tsx
│   │   │   │   │   │   └── workstation-topbar.tsx
│   │   │   │   │   ├── operations
│   │   │   │   │   │   ├── evidence-link.test.tsx
│   │   │   │   │   │   ├── evidence-link.tsx
│   │   │   │   │   │   ├── gate-rail.test.tsx
│   │   │   │   │   │   ├── gate-rail.tsx
│   │   │   │   │   │   ├── index.ts
│   │   │   │   │   │   ├── inject-style.ts
│   │   │   │   │   │   ├── readiness-panel.test.tsx
│   │   │   │   │   │   ├── readiness-panel.tsx
│   │   │   │   │   │   ├── severity-badge.test.tsx
│   │   │   │   │   │   ├── severity-badge.tsx
│   │   │   │   │   │   ├── status.test.ts
│   │   │   │   │   │   ├── status.ts
│   │   │   │   │   │   ├── trust-strip.test.tsx
│   │   │   │   │   │   ├── trust-strip.tsx
│   │   │   │   │   │   ├── validation-issue-list.test.tsx
│   │   │   │   │   │   ├── validation-issue-list.tsx
│   │   │   │   │   │   ├── workspace-section.test.tsx
│   │   │   │   │   │   └── workspace-section.tsx
│   │   │   │   │   └── ui
│   │   │   │   │       ├── accordion.test.tsx
│   │   │   │   │       ├── accordion.tsx
│   │   │   │   │       ├── async-region.test.tsx
│   │   │   │   │       ├── async-region.tsx
│   │   │   │   │       ├── badge.tsx
│   │   │   │   │       ├── breadcrumb.tsx
│   │   │   │   │       ├── button.test.tsx
│   │   │   │   │       ├── button.tsx
│   │   │   │   │       ├── button.view-model.test.ts
│   │   │   │   │       ├── button.view-model.ts
│   │   │   │   │       ├── callout.tsx
│   │   │   │   │       ├── card.tsx
│   │   │   │   │       ├── checkbox.tsx
│   │   │   │   │       ├── combobox.test.tsx
│   │   │   │   │       ├── combobox.tsx
│   │   │   │   │       ├── context-menu.tsx
│   │   │   │   │       ├── date-picker.test.tsx
│   │   │   │   │       ├── date-picker.tsx
│   │   │   │   │       ├── date-range-picker.tsx
│   │   │   │   │       ├── density-toggle.test.tsx
│   │   │   │   │       ├── density-toggle.tsx
│   │   │   │   │       ├── dialog.test.tsx
│   │   │   │   │       ├── dialog.tsx
│   │   │   │   │       ├── dialog.view-model.test.ts
│   │   │   │   │       ├── dialog.view-model.ts
│   │   │   │   │       ├── drawer.test.tsx
│   │   │   │   │       ├── drawer.tsx
│   │   │   │   │       ├── error-boundary.test.tsx
│   │   │   │   │       ├── error-boundary.tsx
│   │   │   │   │       ├── eyebrow.tsx
│   │   │   │   │       ├── feedback-primitives.test.tsx
│   │   │   │   │       ├── field-support.test.tsx
│   │   │   │   │       ├── field-support.tsx
│   │   │   │   │       ├── file-upload.test.tsx
│   │   │   │   │       ├── file-upload.tsx
│   │   │   │   │       ├── form.tsx
│   │   │   │   │       ├── freshness-chip.test.tsx
│   │   │   │   │       ├── freshness-chip.tsx
│   │   │   │   │       ├── freshness-chip.view-model.test.ts
│   │   │   │   │       ├── freshness-chip.view-model.ts
│   │   │   │   │       ├── gauge.test.tsx
│   │   │   │   │       ├── gauge.tsx
│   │   │   │   │       ├── governed-approvals-panel.tsx
│   │   │   │   │       ├── guardrail-utilization.tsx
│   │   │   │   │       ├── input.tsx
│   │   │   │   │       ├── kbd.tsx
│   │   │   │   │       ├── label.tsx
│   │   │   │   │       ├── layout.test.tsx
│   │   │   │   │       ├── layout.tsx
│   │   │   │   │       ├── modal.tsx
│   │   │   │   │       ├── multi-select.tsx
│   │   │   │   │       ├── number-input.test.tsx
│   │   │   │   │       ├── number-input.tsx
│   │   │   │   │       ├── order-status-banner.tsx
│   │   │   │   │       ├── panel-surface.tsx
│   │   │   │   │       ├── popover.test.tsx
│   │   │   │   │       ├── popover.tsx
│   │   │   │   │       ├── primitives.test.tsx
│   │   │   │   │       ├── progress.tsx
│   │   │   │   │       ├── radio-group.test.tsx
│   │   │   │   │       ├── radio-group.tsx
│   │   │   │   │       ├── risk-control-panel.test.tsx
│   │   │   │   │       ├── risk-control-panel.tsx
│   │   │   │   │       ├── risk-control-panel.view-model.test.ts
│   │   │   │   │       ├── risk-control-panel.view-model.ts
│   │   │   │   │       ├── screen-layout.test.tsx
│   │   │   │   │       ├── screen-layout.tsx
│   │   │   │   │       ├── segmented-control.test.tsx
│   │   │   │   │       ├── segmented-control.tsx
│   │   │   │   │       ├── select.tsx
│   │   │   │   │       ├── sheet.test.tsx
│   │   │   │   │       ├── sheet.tsx
│   │   │   │   │       ├── skeleton.test.tsx
│   │   │   │   │       ├── skeleton.tsx
│   │   │   │   │       ├── spinner.tsx
│   │   │   │   │       ├── status-banner.tsx
│   │   │   │   │       ├── stepper.test.tsx
│   │   │   │   │       ├── stepper.tsx
│   │   │   │   │       ├── tabs.tsx
│   │   │   │   │       ├── technical-details.test.tsx
│   │   │   │   │       ├── technical-details.tsx
│   │   │   │   │       ├── text-area.tsx
│   │   │   │   │       ├── theme-toggle.test.tsx
│   │   │   │   │       ├── theme-toggle.tsx
│   │   │   │   │       ├── toast.tsx
│   │   │   │   │       ├── tooltip.tsx
│   │   │   │   │       └── trading-risk-controls.tsx
│   │   │   │   ├── design-system
│   │   │   │   │   ├── assets.ts
│   │   │   │   │   ├── badge.tsx
│   │   │   │   │   ├── button.tsx
│   │   │   │   │   ├── masthead.tsx
│   │   │   │   │   ├── nav-rail.tsx
│   │   │   │   │   ├── primitives.tsx
│   │   │   │   │   ├── status.test.ts
│   │   │   │   │   ├── status.tsx
│   │   │   │   │   ├── tokens.ts
│   │   │   │   │   └── trust-strip.tsx
│   │   │   │   ├── features
│   │   │   │   │   ├── accounting
│   │   │   │   │   │   └── accountingCloseModels.ts
│   │   │   │   │   ├── first-run
│   │   │   │   │   │   ├── activation-progress.tsx
│   │   │   │   │   │   ├── first-run-screen.tsx
│   │   │   │   │   │   └── types.ts
│   │   │   │   │   └── fund-structure
│   │   │   │   │       ├── entity-setup-wizard.test.tsx
│   │   │   │   │       └── entity-setup-wizard.tsx
│   │   │   │   ├── hooks
│   │   │   │   │   ├── use-notification-center.ts
│   │   │   │   │   ├── use-quotes-stream.test.ts
│   │   │   │   │   ├── use-quotes-stream.ts
│   │   │   │   │   ├── use-report-run-stream.test.ts
│   │   │   │   │   ├── use-report-run-stream.ts
│   │   │   │   │   ├── use-request-lifecycle.test.ts
│   │   │   │   │   ├── use-request-lifecycle.ts
│   │   │   │   │   ├── use-workstation-data.test.ts
│   │   │   │   │   └── use-workstation-data.ts
│   │   │   │   ├── lib
│   │   │   │   │   ├── activity-log
│   │   │   │   │   │   ├── activity-log.test.tsx
│   │   │   │   │   │   ├── storage.ts
│   │   │   │   │   │   ├── store.tsx
│   │   │   │   │   │   └── types.ts
│   │   │   │   │   ├── api
│   │   │   │   │   │   ├── covered-call.api.test.ts
│   │   │   │   │   │   ├── covered-call.api.ts
│   │   │   │   │   │   ├── data-operations-assurance.api.ts
│   │   │   │   │   │   ├── portfolio-cash-ladder.api.ts
│   │   │   │   │   │   ├── provider-modules.api.test.ts
│   │   │   │   │   │   ├── provider-modules.api.ts
│   │   │   │   │   │   ├── reporting-runs.api.test.ts
│   │   │   │   │   │   ├── reporting-runs.api.ts
│   │   │   │   │   │   ├── security-master-workbench.api.test.ts
│   │   │   │   │   │   └── security-master-workbench.api.ts
│   │   │   │   │   ├── companion-pane
│   │   │   │   │   │   ├── chrome-bridge.test.ts
│   │   │   │   │   │   ├── chrome-bridge.ts
│   │   │   │   │   │   ├── open-registry.test.ts
│   │   │   │   │   │   ├── open-registry.ts
│   │   │   │   │   │   ├── opener-broadcast.ts
│   │   │   │   │   │   ├── pane-window.test.ts
│   │   │   │   │   │   └── pane-window.ts
│   │   │   │   │   ├── covered-call
│   │   │   │   │   │   ├── index.ts
│   │   │   │   │   │   ├── payoff.test.ts
│   │   │   │   │   │   └── payoff.ts
│   │   │   │   │   ├── dev-fixtures
│   │   │   │   │   │   ├── fixture-resolver.ts
│   │   │   │   │   │   └── market-data-fixtures.ts
│   │   │   │   │   ├── historical-chart
│   │   │   │   │   │   ├── indicators-worker-client.ts
│   │   │   │   │   │   ├── indicators.test.ts
│   │   │   │   │   │   ├── indicators.ts
│   │   │   │   │   │   └── indicators.worker.ts
│   │   │   │   │   ├── notification-center
│   │   │   │   │   │   ├── merge.test.ts
│   │   │   │   │   │   ├── merge.ts
│   │   │   │   │   │   ├── read-state.test.ts
│   │   │   │   │   │   ├── read-state.ts
│   │   │   │   │   │   └── types.ts
│   │   │   │   │   ├── operating-scope
│   │   │   │   │   │   ├── fund-accounts.test.ts
│   │   │   │   │   │   ├── fund-accounts.ts
│   │   │   │   │   │   ├── persistence.test.ts
│   │   │   │   │   │   └── persistence.ts
│   │   │   │   │   ├── perf
│   │   │   │   │   │   ├── off-thread-compute.test.tsx
│   │   │   │   │   │   └── off-thread-compute.ts
│   │   │   │   │   ├── price-alerts
│   │   │   │   │   │   ├── evaluator.test.ts
│   │   │   │   │   │   ├── evaluator.ts
│   │   │   │   │   │   ├── index.ts
│   │   │   │   │   │   ├── service.test.tsx
│   │   │   │   │   │   ├── service.ts
│   │   │   │   │   │   ├── storage.test.ts
│   │   │   │   │   │   ├── storage.ts
│   │   │   │   │   │   └── types.ts
│   │   │   │   │   ├── provider-setup
│   │   │   │   │   │   └── use-provider-setup.ts
│   │   │   │   │   ├── accounting-navigation.test.ts
│   │   │   │   │   ├── accounting-navigation.ts
│   │   │   │   │   ├── api-errors.test.ts
│   │   │   │   │   ├── api-errors.ts
│   │   │   │   │   ├── api.extensibility.test.ts
│   │   │   │   │   ├── api.operations-continuity.test.ts
│   │   │   │   │   ├── api.private-capital.test.ts
│   │   │   │   │   ├── api.reconciliation.test.ts
│   │   │   │   │   ├── api.risk-escalations.ts
│   │   │   │   │   ├── api.trading.test.ts
│   │   │   │   │   ├── api.ts
│   │   │   │   │   ├── csv.test.ts
│   │   │   │   │   ├── csv.ts
│   │   │   │   │   ├── daily-control-tower.test.ts
│   │   │   │   │   ├── daily-control-tower.ts
│   │   │   │   │   ├── dense-table-virtualization.test.ts
│   │   │   │   │   ├── dense-table-virtualization.ts
│   │   │   │   │   ├── dense-virtualization.test.ts
│   │   │   │   │   ├── dense-virtualization.ts
│   │   │   │   │   ├── density.ts
│   │   │   │   │   ├── dev-fixtures.test.ts
│   │   │   │   │   ├── dev-fixtures.trading-risk.ts
│   │   │   │   │   ├── dev-fixtures.ts
│   │   │   │   │   ├── focus-classes.ts
│   │   │   │   │   ├── format.test.ts
│   │   │   │   │   ├── format.ts
│   │   │   │   │   ├── fund-account-scope.ts
│   │   │   │   │   ├── onboarding.ts
│   │   │   │   │   ├── plaid-link.ts
│   │   │   │   │   ├── provider-integration-setup-validation.test.ts
│   │   │   │   │   ├── provider-integration-setup-validation.ts
│   │   │   │   │   ├── provider-integration-workbench.ts
│   │   │   │   │   ├── quant-api-mappers.ts
│   │   │   │   │   ├── quotes-stream.test.ts
│   │   │   │   │   ├── quotes-stream.ts
│   │   │   │   │   ├── report-run-stream.test.ts
│   │   │   │   │   ├── report-run-stream.ts
│   │   │   │   │   ├── report-writer-grid-diff.test.ts
│   │   │   │   │   ├── report-writer-grid-diff.ts
│   │   │   │   │   ├── report-writer-grid-format.test.ts
│   │   │   │   │   ├── report-writer-grid-format.ts
│   │   │   │   │   ├── reporting-distributions.ts
│   │   │   │   │   ├── reporting-governance-api.test.ts
│   │   │   │   │   ├── reporting-governance-api.ts
│   │   │   │   │   ├── reporting-governance-routes.test.ts
│   │   │   │   │   ├── reporting-governance-routes.ts
│   │   │   │   │   ├── reporting-hub.test.ts
│   │   │   │   │   ├── reporting-hub.ts
│   │   │   │   │   ├── reporting-link-safety.test.ts
│   │   │   │   │   ├── reporting-link-safety.ts
│   │   │   │   │   ├── reporting-periods.test.ts
│   │   │   │   │   ├── reporting-periods.ts
│   │   │   │   │   ├── reporting-workspace.test.ts
│   │   │   │   │   ├── reporting-workspace.ts
│   │   │   │   │   ├── route-error-telemetry.test.ts
│   │   │   │   │   ├── route-error-telemetry.ts
│   │   │   │   │   ├── saved-layouts.test.ts
│   │   │   │   │   ├── saved-layouts.ts
│   │   │   │   │   ├── security-schedule-dev-fixtures.ts
│   │   │   │   │   ├── shared-tone-mappings.test.ts
│   │   │   │   │   ├── shared-tone-mappings.ts
│   │   │   │   │   ├── sql-workbench-storage.test.ts
│   │   │   │   │   ├── sql-workbench-storage.ts
│   │   │   │   │   ├── system-status.ts
│   │   │   │   │   ├── theme.test.ts
│   │   │   │   │   ├── theme.ts
│   │   │   │   │   ├── time.test.ts
│   │   │   │   │   ├── time.ts
│   │   │   │   │   ├── ui-api-routes.generated.ts
│   │   │   │   │   ├── utils.ts
│   │   │   │   │   ├── view-state-envelope.test.ts
│   │   │   │   │   ├── view-state-envelope.ts
│   │   │   │   │   ├── workflow-route-match.test.ts
│   │   │   │   │   ├── workflow-route-match.ts
│   │   │   │   │   ├── workspace-catalog.generated.ts
│   │   │   │   │   ├── workspace.test.ts
│   │   │   │   │   ├── workspace.ts
│   │   │   │   │   ├── workstation-endpoints.test.ts
│   │   │   │   │   ├── workstation-endpoints.ts
│   │   │   │   │   ├── workstation-screen-view-states.test.ts
│   │   │   │   │   └── workstation-screen-view-states.ts
│   │   │   │   ├── screens
│   │   │   │   │   ├── accounting-calibration-summary.view-model.ts
│   │   │   │   │   ├── accounting-screen.approvals.ts
│   │   │   │   │   ├── accounting-screen.calibration-panel.tsx
│   │   │   │   │   ├── accounting-screen.capital-account-workbench-panel.tsx
│   │   │   │   │   ├── accounting-screen.capital-accounts.view-model.test.ts
│   │   │   │   │   ├── accounting-screen.capital-accounts.view-model.ts
│   │   │   │   │   ├── accounting-screen.close-cockpit-drafts.ts
│   │   │   │   │   ├── accounting-screen.close-cockpit-panels.tsx
│   │   │   │   │   ├── accounting-screen.close-cockpit-presenters.ts
│   │   │   │   │   ├── accounting-screen.close-cockpit.view-model.test.ts
│   │   │   │   │   ├── accounting-screen.close-cockpit.view-model.ts
│   │   │   │   │   ├── accounting-screen.close-command-center.view-model.ts
│   │   │   │   │   ├── accounting-screen.configure-panel.test.tsx
│   │   │   │   │   ├── accounting-screen.configure-panel.tsx
│   │   │   │   │   ├── accounting-screen.configure-panel.view-model.test.ts
│   │   │   │   │   ├── accounting-screen.configure-panel.view-model.ts
│   │   │   │   │   ├── accounting-screen.corporate-actions-panel.tsx
│   │   │   │   │   ├── accounting-screen.evidence-timeline.ts
│   │   │   │   │   ├── accounting-screen.formatting.ts
│   │   │   │   │   ├── accounting-screen.governance-presenters.ts
│   │   │   │   │   ├── accounting-screen.governance.view-model.test.ts
│   │   │   │   │   ├── accounting-screen.governance.view-model.ts
│   │   │   │   │   ├── accounting-screen.journal-entries.view-model.test.ts
│   │   │   │   │   ├── accounting-screen.journal-entries.view-model.ts
│   │   │   │   │   ├── accounting-screen.linked-context.ts
│   │   │   │   │   ├── accounting-screen.operations-panels.tsx
│   │   │   │   │   ├── accounting-screen.operator-focus.test.ts
│   │   │   │   │   ├── accounting-screen.operator-focus.ts
│   │   │   │   │   ├── accounting-screen.reconciliation-panels.tsx
│   │   │   │   │   ├── accounting-screen.reconciliation-queue-utils.ts
│   │   │   │   │   ├── accounting-screen.reconciliation.view-model.test.ts
│   │   │   │   │   ├── accounting-screen.reconciliation.view-model.ts
│   │   │   │   │   ├── accounting-screen.security-master-detail-panels.tsx
│   │   │   │   │   ├── accounting-screen.security-master-panels.tsx
│   │   │   │   │   ├── accounting-screen.security-master-presenters.ts
│   │   │   │   │   ├── accounting-screen.styles.ts
│   │   │   │   │   ├── accounting-screen.task-mode-view-model.test.ts
│   │   │   │   │   ├── accounting-screen.task-mode-view-model.ts
│   │   │   │   │   ├── accounting-screen.test.tsx
│   │   │   │   │   ├── accounting-screen.tsx
│   │   │   │   │   ├── accounting-screen.view-model.shared.test.ts
│   │   │   │   │   ├── accounting-screen.view-model.shared.ts
│   │   │   │   │   ├── accounting-screen.view-model.test.ts
│   │   │   │   │   ├── accounting-screen.view-model.ts
│   │   │   │   │   ├── accounting-screen.workbench-context.tsx
│   │   │   │   │   ├── accounting-screen.workflow-continuity.ts
│   │   │   │   │   ├── asset-detail-screen.test.tsx
│   │   │   │   │   ├── asset-detail-screen.tsx
│   │   │   │   │   ├── asset-detail-screen.view-model.test.ts
│   │   │   │   │   ├── asset-detail-screen.view-model.ts
│   │   │   │   │   ├── cash-ladder-screen.tsx
│   │   │   │   │   ├── cash-ladder-screen.view-model.test.ts
│   │   │   │   │   ├── cash-ladder-screen.view-model.ts
│   │   │   │   │   ├── covered-call-screen.test.tsx
│   │   │   │   │   ├── covered-call-screen.tsx
│   │   │   │   │   ├── covered-call-screen.view-model.test.ts
│   │   │   │   │   ├── covered-call-screen.view-model.ts
│   │   │   │   │   ├── daily-control-tower-screen.test.tsx
│   │   │   │   │   ├── daily-control-tower-screen.tsx
│   │   │   │   │   ├── data-operations-assurance-workstreams.test.tsx
│   │   │   │   │   ├── data-operations-assurance-workstreams.tsx
│   │   │   │   │   ├── data-provider-display-view-model.ts
│   │   │   │   │   ├── data-screen-navigation-panels.tsx
│   │   │   │   │   ├── data-screen.analytics-status.test.tsx
│   │   │   │   │   ├── data-screen.analytics-status.tsx
│   │   │   │   │   ├── data-screen.canonical-symbols.test.tsx
│   │   │   │   │   ├── data-screen.canonical-symbols.tsx
│   │   │   │   │   ├── data-screen.canonical-symbols.view-model.test.ts
│   │   │   │   │   ├── data-screen.canonical-symbols.view-model.ts
│   │   │   │   │   ├── data-screen.capability-matrix.view-model.test.ts
│   │   │   │   │   ├── data-screen.capability-matrix.view-model.ts
│   │   │   │   │   ├── data-screen.cell-actions.test.tsx
│   │   │   │   │   ├── data-screen.cell-actions.tsx
│   │   │   │   │   ├── data-screen.corporate-action-inbox.view-model.test.ts
│   │   │   │   │   ├── data-screen.corporate-action-inbox.view-model.ts
│   │   │   │   │   ├── data-screen.coverage-gaps.view-model.test.ts
│   │   │   │   │   ├── data-screen.coverage-gaps.view-model.ts
│   │   │   │   │   ├── data-screen.data-quality.view-model.test.ts
│   │   │   │   │   ├── data-screen.data-quality.view-model.ts
│   │   │   │   │   ├── data-screen.data-regions.tsx
│   │   │   │   │   ├── data-screen.evidence-timeline.ts
│   │   │   │   │   ├── data-screen.linked-context.ts
│   │   │   │   │   ├── data-screen.operator-focus.ts
│   │   │   │   │   ├── data-screen.plaid-institution-search.ts
│   │   │   │   │   ├── data-screen.provider-accounting.test-fixtures.ts
│   │   │   │   │   ├── data-screen.provider-accounting.test.tsx
│   │   │   │   │   ├── data-screen.provider-accounting.tsx
│   │   │   │   │   ├── data-screen.provider-accounting.view-model.test.ts
│   │   │   │   │   ├── data-screen.provider-accounting.view-model.ts
│   │   │   │   │   ├── data-screen.provider-setup.types.ts
│   │   │   │   │   ├── data-screen.query-panel.view-model.test.ts
│   │   │   │   │   ├── data-screen.query-panel.view-model.ts
│   │   │   │   │   ├── data-screen.route-state.ts
│   │   │   │   │   ├── data-screen.security-master.ts
│   │   │   │   │   ├── data-screen.test.tsx
│   │   │   │   │   ├── data-screen.tone-styles.ts
│   │   │   │   │   ├── data-screen.tsx
│   │   │   │   │   ├── data-screen.view-model.test.ts
│   │   │   │   │   ├── data-screen.view-model.ts
│   │   │   │   │   ├── data-screen.workbook-review.test.ts
│   │   │   │   │   ├── data-screen.workbook-review.ts
│   │   │   │   │   ├── data-screen.workflow-continuity.ts
│   │   │   │   │   ├── data-screen.workstreams.tsx
│   │   │   │   │   ├── evidence-workbench-assurance-lists.tsx
│   │   │   │   │   ├── evidence-workbench-formatters.ts
│   │   │   │   │   ├── evidence-workbench-screen.tsx
│   │   │   │   │   ├── evidence-workbench-screen.view-model.test.tsx
│   │   │   │   │   ├── evidence-workbench-screen.view-model.ts
│   │   │   │   │   ├── family-office-screen.test.tsx
│   │   │   │   │   ├── family-office-screen.tsx
│   │   │   │   │   ├── family-office-screen.view-model.test.ts
│   │   │   │   │   ├── family-office-screen.view-model.ts
│   │   │   │   │   ├── finance-standard-pages-screen.test.tsx
│   │   │   │   │   ├── finance-standard-pages-screen.tsx
│   │   │   │   │   ├── journal-entry-detail-screen.test.tsx
│   │   │   │   │   ├── journal-entry-detail-screen.tsx
│   │   │   │   │   ├── journal-entry-detail-screen.view-model.ts
│   │   │   │   │   ├── live-quotes-screen.quick-trade.ts
│   │   │   │   │   ├── live-quotes-screen.test.tsx
│   │   │   │   │   ├── live-quotes-screen.tsx
│   │   │   │   │   ├── live-quotes-screen.view-model.test.ts
│   │   │   │   │   ├── live-quotes-screen.view-model.ts
│   │   │   │   │   ├── market-data-screen.test.tsx
│   │   │   │   │   ├── market-data-screen.tsx
│   │   │   │   │   ├── operations-continuity-reviewed-automation.view-model.ts
│   │   │   │   │   ├── operations-continuity-screen.command-state.ts
│   │   │   │   │   ├── operations-continuity-screen.test.tsx
│   │   │   │   │   ├── operations-continuity-screen.tsx
│   │   │   │   │   ├── operations-continuity-screen.view-model.test.ts
│   │   │   │   │   ├── operations-continuity-screen.view-model.ts
│   │   │   │   │   ├── operations-record-release-screen.test.tsx
│   │   │   │   │   ├── operations-record-release-screen.tsx
│   │   │   │   │   ├── operations-record-release-screen.view-model.test.ts
│   │   │   │   │   ├── operations-record-release-screen.view-model.ts
│   │   │   │   │   ├── operator-readiness-console.presentation.ts
│   │   │   │   │   ├── operator-readiness-console.test.tsx
│   │   │   │   │   ├── operator-readiness-console.tsx
│   │   │   │   │   ├── operator-readiness-console.view-model.test.ts
│   │   │   │   │   ├── operator-readiness-console.view-model.ts
│   │   │   │   │   ├── portfolio-screen.a11y.test.tsx
│   │   │   │   │   ├── portfolio-screen.evidence-timeline.test.ts
│   │   │   │   │   ├── portfolio-screen.evidence-timeline.ts
│   │   │   │   │   ├── portfolio-screen.linked-context.ts
│   │   │   │   │   ├── portfolio-screen.operator-focus.ts
│   │   │   │   │   ├── portfolio-screen.presentation.ts
│   │   │   │   │   ├── portfolio-screen.test.tsx
│   │   │   │   │   ├── portfolio-screen.tsx
│   │   │   │   │   ├── portfolio-screen.view-model.test.ts
│   │   │   │   │   ├── portfolio-screen.view-model.ts
│   │   │   │   │   ├── portfolio-screen.workflow-continuity.ts
│   │   │   │   │   ├── price-alerts-screen.test.tsx
│   │   │   │   │   ├── price-alerts-screen.tsx
│   │   │   │   │   ├── price-alerts-screen.view-model.test.ts
│   │   │   │   │   ├── price-alerts-screen.view-model.ts
│   │   │   │   │   ├── quant-lab-screen.formulas-tab.test.tsx
│   │   │   │   │   ├── quant-lab-screen.test.tsx
│   │   │   │   │   ├── quant-lab-screen.tsx
│   │   │   │   │   ├── quant-lab-screen.view-model.test.ts
│   │   │   │   │   ├── quant-lab-screen.view-model.ts
│   │   │   │   │   ├── reconciliation-casework-outcome.ts
│   │   │   │   │   ├── report-library-screen.test.tsx
│   │   │   │   │   ├── report-library-screen.tsx
│   │   │   │   │   ├── report-run-governance-client-package.ts
│   │   │   │   │   ├── report-run-governance-screen.test.tsx
│   │   │   │   │   ├── report-run-governance-screen.tsx
│   │   │   │   │   ├── report-run-parameters-screen.test.tsx
│   │   │   │   │   ├── report-run-parameters-screen.tsx
│   │   │   │   │   ├── report-run-parameters-screen.view-model.test.ts
│   │   │   │   │   ├── report-run-parameters-screen.view-model.ts
│   │   │   │   │   ├── reporting-screen.a11y.test.tsx
│   │   │   │   │   ├── reporting-screen.branding-access.tsx
│   │   │   │   │   ├── reporting-screen.client-package.ts
│   │   │   │   │   ├── reporting-screen.delivery-history.tsx
│   │   │   │   │   ├── reporting-screen.exports-runner.request.test.ts
│   │   │   │   │   ├── reporting-screen.exports-runner.tsx
│   │   │   │   │   ├── reporting-screen.linked-context.ts
│   │   │   │   │   ├── reporting-screen.operator-focus.ts
│   │   │   │   │   ├── reporting-screen.private-capital-readiness.tsx
│   │   │   │   │   ├── reporting-screen.report-writer-helpers.ts
│   │   │   │   │   ├── reporting-screen.report-writer.tsx
│   │   │   │   │   ├── reporting-screen.run-status-modules.tsx
│   │   │   │   │   ├── reporting-screen.schedule-management.tsx
│   │   │   │   │   ├── reporting-screen.schedule-view-model.ts
│   │   │   │   │   ├── reporting-screen.shared-components.tsx
│   │   │   │   │   ├── reporting-screen.starter-kit.tsx
│   │   │   │   │   ├── reporting-screen.task-mode-view-model.test.ts
│   │   │   │   │   ├── reporting-screen.task-mode-view-model.ts
│   │   │   │   │   ├── reporting-screen.template-lifecycle.tsx
│   │   │   │   │   ├── reporting-screen.test.tsx
│   │   │   │   │   ├── reporting-screen.tsx
│   │   │   │   │   ├── reporting-screen.view-model.test.ts
│   │   │   │   │   ├── reporting-screen.view-model.ts
│   │   │   │   │   ├── reporting-screen.workbench-context.tsx
│   │   │   │   │   ├── reporting-screen.workflow-continuity.ts
│   │   │   │   │   ├── settings-mutation-confirm-dialog.tsx
│   │   │   │   │   ├── settings-route-state.test.ts
│   │   │   │   │   ├── settings-route-state.ts
│   │   │   │   │   ├── settings-screen.a11y.test.tsx
│   │   │   │   │   ├── settings-screen.date-format.ts
│   │   │   │   │   ├── settings-screen.operations-control.ts
│   │   │   │   │   ├── settings-screen.operations-form-options.ts
│   │   │   │   │   ├── settings-screen.test.tsx
│   │   │   │   │   ├── settings-screen.tsx
│   │   │   │   │   ├── settings-screen.view-model.test.ts
│   │   │   │   │   ├── settings-screen.view-model.ts
│   │   │   │   │   ├── settings-screen.workflow-continuity.ts
│   │   │   │   │   ├── settings-task-chooser.tsx
│   │   │   │   │   ├── statement-fetch-panel.test.tsx
│   │   │   │   │   ├── statement-fetch-panel.tsx
│   │   │   │   │   ├── statement-fetch-panel.view-model.ts
│   │   │   │   │   ├── statement-import-panel.test.tsx
│   │   │   │   │   ├── statement-import-panel.tsx
│   │   │   │   │   ├── statement-import-panel.view-model.test.ts
│   │   │   │   │   ├── statement-import-panel.view-model.ts
│   │   │   │   │   ├── statement-import-preview.tsx
│   │   │   │   │   ├── statement-import-screen.tsx
│   │   │   │   │   ├── strategy-designer-screen.test.tsx
│   │   │   │   │   ├── strategy-designer-screen.tsx
│   │   │   │   │   ├── strategy-designer-screen.view-model.test.ts
│   │   │   │   │   ├── strategy-designer-screen.view-model.ts
│   │   │   │   │   ├── strategy-screen.a11y.test.tsx
│   │   │   │   │   ├── strategy-screen.evidence-timeline.ts
│   │   │   │   │   ├── strategy-screen.operator-focus.ts
│   │   │   │   │   ├── strategy-screen.status-announcement.ts
│   │   │   │   │   ├── strategy-screen.test.tsx
│   │   │   │   │   ├── strategy-screen.tsx
│   │   │   │   │   ├── strategy-screen.view-model.test.ts
│   │   │   │   │   ├── strategy-screen.view-model.ts
│   │   │   │   │   ├── strategy-screen.workflow-continuity.ts
│   │   │   │   │   ├── trading-screen.a11y.test.tsx
│   │   │   │   │   ├── trading-screen.acceptance-panel.tsx
│   │   │   │   │   ├── trading-screen.evidence-timeline.ts
│   │   │   │   │   ├── trading-screen.governed-approvals.test.ts
│   │   │   │   │   ├── trading-screen.governed-approvals.ts
│   │   │   │   │   ├── trading-screen.linked-context.ts
│   │   │   │   │   ├── trading-screen.operator-focus.ts
│   │   │   │   │   ├── trading-screen.order-ticket-text.ts
│   │   │   │   │   ├── trading-screen.readiness-summary.ts
│   │   │   │   │   ├── trading-screen.test.tsx
│   │   │   │   │   ├── trading-screen.tones.ts
│   │   │   │   │   ├── trading-screen.tsx
│   │   │   │   │   ├── trading-screen.view-model.test.ts
│   │   │   │   │   ├── trading-screen.view-model.ts
│   │   │   │   │   ├── trading-screen.workflow-continuity.ts
│   │   │   │   │   ├── trial-balance-screen.test.tsx
│   │   │   │   │   ├── trial-balance-screen.tsx
│   │   │   │   │   ├── trial-balance-screen.view-model.test.ts
│   │   │   │   │   ├── trial-balance-screen.view-model.ts
│   │   │   │   │   ├── w4-acceptance-parity.test.ts
│   │   │   │   │   ├── watchlist-screen.a11y.test.tsx
│   │   │   │   │   ├── watchlist-screen.row-actions.test.tsx
│   │   │   │   │   ├── watchlist-screen.row-actions.tsx
│   │   │   │   │   ├── watchlist-screen.test.tsx
│   │   │   │   │   ├── watchlist-screen.tsx
│   │   │   │   │   ├── watchlist-screen.view-model.test.ts
│   │   │   │   │   └── watchlist-screen.view-model.ts
│   │   │   │   ├── styles
│   │   │   │   │   ├── accounting-screen.css
│   │   │   │   │   ├── app-shell.css
│   │   │   │   │   ├── command-palette.css
│   │   │   │   │   ├── companion-pane.css
│   │   │   │   │   ├── dense-row-detail-accessibility.css
│   │   │   │   │   ├── index.css
│   │   │   │   │   ├── ui-kit-primitives.css
│   │   │   │   │   ├── workflow-continuity-dock.css
│   │   │   │   │   ├── workspace-nav.css
│   │   │   │   │   ├── workspace-primitives.css
│   │   │   │   │   ├── workspace-surface.css
│   │   │   │   │   └── workspace-workbench-shell.css
│   │   │   │   ├── test
│   │   │   │   │   ├── render.tsx
│   │   │   │   │   ├── setup.ts
│   │   │   │   │   └── verified-operation-outcome.ts
│   │   │   │   ├── types
│   │   │   │   │   ├── canonical-symbol.ts
│   │   │   │   │   ├── covered-call.types.ts
│   │   │   │   │   ├── data-operations-assurance.ts
│   │   │   │   │   ├── instrument-accounting.test.ts
│   │   │   │   │   ├── instrument-accounting.ts
│   │   │   │   │   ├── lifecycle.ts
│   │   │   │   │   ├── market-data.ts
│   │   │   │   │   ├── portfolio-cash-ladder.types.ts
│   │   │   │   │   ├── provider-accounting.ts
│   │   │   │   │   ├── provider-setup.ts
│   │   │   │   │   ├── reporting-governance.ts
│   │   │   │   │   ├── workstation-1.ts
│   │   │   │   │   ├── workstation-2.ts
│   │   │   │   │   ├── workstation-3.ts
│   │   │   │   │   ├── workstation-4.ts
│   │   │   │   │   ├── workstation-5.ts
│   │   │   │   │   ├── workstation-6.ts
│   │   │   │   │   ├── workstation-7.ts
│   │   │   │   │   └── workstation-8.ts
│   │   │   │   ├── app-shell.command-palette.ts
│   │   │   │   ├── app-shell.data-provenance-badge.test.ts
│   │   │   │   ├── app-shell.data-provenance-badge.ts
│   │   │   │   ├── app-shell.development-fixture-notice.ts
│   │   │   │   ├── app-shell.evidence-timeline.ts
│   │   │   │   ├── app-shell.linked-context.ts
│   │   │   │   ├── app-shell.onboarding.test.tsx
│   │   │   │   ├── app-shell.onboarding.ts
│   │   │   │   ├── app-shell.operating-scope.test.ts
│   │   │   │   ├── app-shell.operating-scope.ts
│   │   │   │   ├── app-shell.operator-focus.ts
│   │   │   │   ├── app-shell.route-focus.ts
│   │   │   │   ├── app-shell.status-panel.ts
│   │   │   │   ├── app-shell.trust-strip.ts
│   │   │   │   ├── app-shell.view-model.test.ts
│   │   │   │   ├── app-shell.view-model.ts
│   │   │   │   ├── app-shell.workflow-continuity-types.ts
│   │   │   │   ├── app-shell.workflow-continuity-view-model.test.ts
│   │   │   │   ├── app-shell.workflow-continuity-view-model.ts
│   │   │   │   ├── app-shell.workflow-continuity.test.ts
│   │   │   │   ├── app-shell.workflow-continuity.ts
│   │   │   │   ├── app-shell.workflow-routing.ts
│   │   │   │   ├── app.test.tsx
│   │   │   │   ├── app.tsx
│   │   │   │   ├── design-system-contract.test.ts
│   │   │   │   ├── main.tsx
│   │   │   │   ├── types.ts
│   │   │   │   ├── vite-config.test.ts
│   │   │   │   └── vite-env.d.ts
│   │   │   ├── eslint.config.mjs
│   │   │   ├── index.html
│   │   │   ├── package-lock.json
│   │   │   ├── package.json
│   │   │   ├── postcss.config.cjs
│   │   │   ├── README.md
│   │   │   ├── tailwind.config.ts
│   │   │   ├── tsconfig.json
│   │   │   ├── tsconfig.node.json
│   │   │   ├── tsconfig.reporting-p0.json
│   │   │   ├── tsconfig.strict-null.json
│   │   │   └── vite.config.ts
│   │   └── README.md
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
│   │   │   ├── ProviderDiagnostics
│   │   │   │   └── ProviderDiagnosticsApiClient.cs
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
│   │   │   ├── ApiClientSession.cs
│   │   │   ├── ApiResponseExtensions.cs
│   │   │   ├── ArchiveBrowserService.cs
│   │   │   ├── ArchiveHealthService.cs
│   │   │   ├── BackendServiceManagerBase.cs
│   │   │   ├── BackfillApiService.cs
│   │   │   ├── BackfillCheckpointService.cs
│   │   │   ├── BackfillPresentationService.cs
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
│   │   │   ├── IndexConstituentCatalog.cs
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
│   │   │   ├── ProblemDetails
│   │   │   │   ├── ApiProblemDetails.cs
│   │   │   │   ├── MeridianApiExceptionHandler.cs
│   │   │   │   └── MeridianApiProblemDetailsServiceCollectionExtensions.cs
│   │   │   ├── AccountingSystemEndpoints.cs
│   │   │   ├── AdminEndpoints.cs
│   │   │   ├── AnalyticsEndpoints.cs
│   │   │   ├── ApiKeyMiddleware.cs
│   │   │   ├── ArchiveMaintenanceEndpoints.cs
│   │   │   ├── AuthEndpoints.cs
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
│   │   │   ├── CookieCsrfMiddleware.cs
│   │   │   ├── CoveredCallEndpoints.cs
│   │   │   ├── CredentialEndpoints.cs
│   │   │   ├── CronEndpoints.cs
│   │   │   ├── CryptoReferenceEndpoints.cs
│   │   │   ├── DataQualityEndpoints.cs
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
│   │   │   ├── FirstRunEndpoints.cs
│   │   │   ├── FundAccountEndpoints.cs
│   │   │   ├── FundProfileScopeEndpointFilters.cs
│   │   │   ├── FundStructureEndpoints.cs
│   │   │   ├── FundStructureEndpoints.ReportingCompatibility.cs
│   │   │   ├── FundStructureEndpoints.ReportingGovernance.cs
│   │   │   ├── FundStructureEndpoints.ReportingRunStream.cs
│   │   │   ├── FundStructureEndpoints.ReportingScheduleAuthority.cs
│   │   │   ├── FundStructureEndpoints.StructuredReportingExport.cs
│   │   │   ├── FutureReferenceEndpoints.cs
│   │   │   ├── FxSpotReferenceEndpoints.cs
│   │   │   ├── HealthEndpoints.cs
│   │   │   ├── HistoricalEndpoints.cs
│   │   │   ├── IBEndpoints.cs
│   │   │   ├── IFundProfileTenantGuard.cs
│   │   │   ├── IngestionJobEndpoints.cs
│   │   │   ├── InitialAccountBootstrapEndpoints.cs
│   │   │   ├── LeanEndpoints.cs
│   │   │   ├── LedgerEndpoints.AccountingConfiguration.cs
│   │   │   ├── LedgerEndpoints.cs
│   │   │   ├── LedgerEndpoints.Dimensions.cs
│   │   │   ├── LedgerEndpoints.JournalAutomation.cs
│   │   │   ├── LedgerEndpoints.Reporting.cs
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
│   │   │   ├── PackagingEndpoints.cs
│   │   │   ├── PathValidation.cs
│   │   │   ├── PlaidEndpoints.cs
│   │   │   ├── PortfolioCashLadderEndpoints.cs
│   │   │   ├── PromotionEndpoints.cs
│   │   │   ├── ProviderConnectionDiagnosticsProjection.cs
│   │   │   ├── ProviderConnectionEndpoints.cs
│   │   │   ├── ProviderCredentialEndpoints.cs
│   │   │   ├── ProviderDataProjectionEndpoints.cs
│   │   │   ├── ProviderEndpoints.cs
│   │   │   ├── ProviderExtendedEndpoints.cs
│   │   │   ├── ProviderModuleEndpoints.cs
│   │   │   ├── ProviderRoutingEndpoints.cs
│   │   │   ├── QuantLabEndpoints.cs
│   │   │   ├── RegistryFundProfileTenantGuard.cs
│   │   │   ├── ReplayEndpoints.cs
│   │   │   ├── ResilienceEndpoints.cs
│   │   │   ├── RiskEndpoints.cs
│   │   │   ├── SamplingEndpoints.cs
│   │   │   ├── SecureReportingDistributionEndpoints.cs
│   │   │   ├── SecurityMasterEndpoints.cs
│   │   │   ├── StatusEndpoints.cs
│   │   │   ├── StorageEndpoints.cs
│   │   │   ├── StorageQualityEndpoints.cs
│   │   │   ├── StrategyLifecycleEndpoints.cs
│   │   │   ├── StreamEndpointHelpers.cs
│   │   │   ├── SubscriptionEndpoints.cs
│   │   │   ├── SwapReferenceEndpoints.cs
│   │   │   ├── SymbolEndpoints.cs
│   │   │   ├── SymbolMappingEndpoints.cs
│   │   │   ├── UiEndpoints.cs
│   │   │   ├── WorkstationEndpoints.AccountingCashFlow.cs
│   │   │   ├── WorkstationEndpoints.CollateralExposure.cs
│   │   │   ├── WorkstationEndpoints.cs
│   │   │   ├── WorkstationEndpoints.DataOperationsAssurance.cs
│   │   │   ├── WorkstationEndpoints.DataProviders.cs
│   │   │   ├── WorkstationEndpoints.DataUploads.cs
│   │   │   ├── WorkstationEndpoints.DataUploadWorkbook.cs
│   │   │   ├── WorkstationEndpoints.Extensibility.cs
│   │   │   ├── WorkstationEndpoints.FamilyOffice.cs
│   │   │   ├── WorkstationEndpoints.FeatureCapabilities.cs
│   │   │   ├── WorkstationEndpoints.FinancialRecordExplorers.cs
│   │   │   ├── WorkstationEndpoints.IBResults.cs
│   │   │   ├── WorkstationEndpoints.OperatorInbox.cs
│   │   │   ├── WorkstationEndpoints.PlotTool.cs
│   │   │   ├── WorkstationEndpoints.PortfolioAggregation.cs
│   │   │   ├── WorkstationEndpoints.ProviderIntegrations.cs
│   │   │   ├── WorkstationEndpoints.Reconciliation.cs
│   │   │   ├── WorkstationEndpoints.ReconciliationBreaks.cs
│   │   │   ├── WorkstationEndpoints.ReconciliationCalibration.cs
│   │   │   ├── WorkstationEndpoints.ReconciliationCasework.cs
│   │   │   ├── WorkstationEndpoints.ReportingAuthority.cs
│   │   │   ├── WorkstationEndpoints.Routing.cs
│   │   │   ├── WorkstationEndpoints.SecurityCoverage.cs
│   │   │   ├── WorkstationEndpoints.SecurityMasterMapping.cs
│   │   │   ├── WorkstationEndpoints.SecurityMasterWorkbench.cs
│   │   │   ├── WorkstationEndpoints.Session.cs
│   │   │   ├── WorkstationEndpoints.StatementCaseworkAuthority.cs
│   │   │   ├── WorkstationEndpoints.StatementConnectors.cs
│   │   │   ├── WorkstationEndpoints.StatementReconciliationReport.cs
│   │   │   ├── WorkstationEndpoints.Strategy.cs
│   │   │   ├── WorkstationEndpoints.StrategyBriefing.cs
│   │   │   ├── WorkstationEndpoints.Stream.cs
│   │   │   ├── WorkstationEndpoints.StructuredReportingExport.cs
│   │   │   ├── WorkstationEndpoints.Trading.cs
│   │   │   ├── WorkstationRiskEndpoints.cs
│   │   │   └── WorkstationTenantContext.cs
│   │   ├── Evidence
│   │   │   ├── EvidenceContribution.cs
│   │   │   ├── EvidenceContributors.cs
│   │   │   ├── EvidenceDocumentExtraction.cs
│   │   │   ├── EvidenceGraphService.cs
│   │   │   ├── EvidencePacketValidationService.cs
│   │   │   ├── EvidenceProofChainBuilder.cs
│   │   │   ├── EvidenceSubjectResolver.cs
│   │   │   ├── EvidenceTemplateRegistry.cs
│   │   │   ├── EvidenceWorkflowServiceCollectionExtensions.cs
│   │   │   ├── FileEvidenceArtifactStore.cs
│   │   │   ├── FileEvidenceArtifactStore.DocumentReview.cs
│   │   │   ├── FileEvidenceArtifactStore.Models.cs
│   │   │   ├── FileStatementReconciliationReportAuthorityStore.cs
│   │   │   ├── ReconciliationEvidenceContributor.StatementRuns.cs
│   │   │   ├── ReportingStatementImportEvidenceRetainer.cs
│   │   │   ├── StatementImportEvidenceBridge.cs
│   │   │   ├── StatementReconciliationReportFetchIngestionAuthority.cs
│   │   │   ├── StatementReconciliationReportWorkflowService.ArtifactHistory.cs
│   │   │   ├── StatementReconciliationReportWorkflowService.Authority.cs
│   │   │   ├── StatementReconciliationReportWorkflowService.cs
│   │   │   ├── StatementReconciliationReportWorkflowService.Reconciliation.cs
│   │   │   └── StatementToReportWorkflowService.Compatibility.cs
│   │   ├── Extensibility
│   │   │   ├── ExtensibilityCatalogService.cs
│   │   │   ├── ExtensibilityConfigurationService.cs
│   │   │   ├── ExtensibilityServiceCollectionExtensions.cs
│   │   │   ├── IExtensibilityCatalogProvider.cs
│   │   │   ├── OperationalConfigurationExtensibilityCatalogProvider.cs
│   │   │   ├── PermissionExtensibilityCatalogProvider.cs
│   │   │   ├── ReportingTemplateExtensibilityCatalogProvider.cs
│   │   │   └── WorkflowExtensibilityCatalogProvider.cs
│   │   ├── Serialization
│   │   │   ├── CoveredCallJsonContext.cs
│   │   │   ├── DirectLendingJsonContext.cs
│   │   │   ├── FamilyOfficeJsonContext.cs
│   │   │   ├── QualityApiJsonContext.cs
│   │   │   ├── ReportingCertifiedArtifactJsonContext.cs
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
│   │   │   ├── AccountingClosePostingWorkbenchBridge.cs
│   │   │   ├── AccountingConfigurationService.ActivationValidation.cs
│   │   │   ├── AccountingConfigurationService.cs
│   │   │   ├── AccountingConfigurationService.RuleValidation.cs
│   │   │   ├── AccountingConfigurationStores.cs
│   │   │   ├── AccountingMigrationRunArtifactStore.cs
│   │   │   ├── AccountingMigrationRunExecutionService.cs
│   │   │   ├── AccountingMigrationRunWorkerPlanStore.cs
│   │   │   ├── AccountingPositionSnapshotCaptureService.cs
│   │   │   ├── AccountingProductionCertificationProfileStore.cs
│   │   │   ├── AccountingProductionReadinessService.cs
│   │   │   ├── AccountingTenantAdministrationProfileStore.cs
│   │   │   ├── AggregatePortfolioExposureProvider.cs
│   │   │   ├── AlpacaBrokerageConnectionService.cs
│   │   │   ├── AuditTrailExplorerService.cs
│   │   │   ├── AutomatedJournalDividendPositionResolver.cs
│   │   │   ├── AutomatedJournalDraftIntakeService.cs
│   │   │   ├── AutomatedJournalEventProducers.cs
│   │   │   ├── AutomatedJournalEvidencePolicy.cs
│   │   │   ├── AutomatedJournalIntakeRunner.cs
│   │   │   ├── AutomatedJournalScheduledWorker.cs
│   │   │   ├── AutomatedJournalScheduleStore.cs
│   │   │   ├── BackfillCoordinator.cs
│   │   │   ├── BackfillExecutionContractProjection.cs
│   │   │   ├── BankFeedTransportService.cs
│   │   │   ├── BrokerageConnectionService.cs
│   │   │   ├── BrokeragePortfolioSyncService.cs
│   │   │   ├── CapitalAccountWorkbenchService.cs
│   │   │   ├── CashOperationsOrchestratorService.cs
│   │   │   ├── CashSyncOrchestrationService.cs
│   │   │   ├── CollateralExposureService.cs
│   │   │   ├── ConfigStore.cs
│   │   │   ├── DailyValuationBatchLifecycleService.cs
│   │   │   ├── DailyValuationPositionService.cs
│   │   │   ├── DailyValuationScheduler.cs
│   │   │   ├── DemoTenantBlueprint.cs
│   │   │   ├── DemoTenantProvisioner.cs
│   │   │   ├── DemoWorkspaceSeeder.cs
│   │   │   ├── DesktopLaunchTicketService.cs
│   │   │   ├── DesktopWorkstationLaunchService.cs
│   │   │   ├── DirectLendingOperationsReadService.cs
│   │   │   ├── Dk1TrustGateReadinessService.cs
│   │   │   ├── DrawdownGuardrailRule.cs
│   │   │   ├── FamilyOfficeReadService.cs
│   │   │   ├── FeatureCapabilitySettingsService.cs
│   │   │   ├── FileFundProfileTenancyRegistry.cs
│   │   │   ├── FinancialRecordExplorerReadService.cs
│   │   │   ├── FinancialRecordExplorerReadService.InstrumentJournalProof.cs
│   │   │   ├── FinancialRecordExplorerSavedViewStore.cs
│   │   │   ├── FirstRunExperienceService.cs
│   │   │   ├── FundAccountCloseReadinessService.cs
│   │   │   ├── FundOperationsWorkspaceReadService.cs
│   │   │   ├── FundOperationsWorkspaceReadService.Models.cs
│   │   │   ├── FundStructureSetupWorkflowService.cs
│   │   │   ├── GovernanceReportPackRepository.cs
│   │   │   ├── GovernedReportingTemplateCatalog.cs
│   │   │   ├── IBackfillProviderConfigAuditReader.cs
│   │   │   ├── IBResultQueryService.cs
│   │   │   ├── IngestionOperationsService.cs
│   │   │   ├── InitialAccountBootstrapService.cs
│   │   │   ├── InMemoryOperatorInboxService.cs
│   │   │   ├── InvestmentAccountingTransactionLabService.cs
│   │   │   ├── IProviderModuleSetupService.cs
│   │   │   ├── LedgerAmountProvenanceService.cs
│   │   │   ├── LedgerClientReportExportService.cs
│   │   │   ├── LedgerDimensionMapper.cs
│   │   │   ├── LedgerMarkToMarketCarryingValueSource.cs
│   │   │   ├── LedgerReportingAuthoritativeSource.cs
│   │   │   ├── ManualJournalEntryDraftStores.cs
│   │   │   ├── ManualJournalEntryWorkbenchService.AccountingCloseReceipts.cs
│   │   │   ├── ManualJournalEntryWorkbenchService.cs
│   │   │   ├── ManualJournalEntryWorkbenchService.Lifecycle.cs
│   │   │   ├── MultiAssetCoverageReadService.cs
│   │   │   ├── OmsIntegrationService.cs
│   │   │   ├── OperationsContinuityReconciliationBridge.cs
│   │   │   ├── OperatorInboxPriorityScoringService.cs
│   │   │   ├── PlaidWorkstationService.cs
│   │   │   ├── PortfolioCashLadderReadService.cs
│   │   │   ├── PortfolioLedgerCashBalanceProvider.cs
│   │   │   ├── PortfolioLedgerWorkflowStatusService.cs
│   │   │   ├── PrivateCapitalFundEventCommandCenterService.cs
│   │   │   ├── ProviderConnectionLifecycleService.cs
│   │   │   ├── ProviderCredentialStore.cs
│   │   │   ├── ProviderDataReadModelService.cs
│   │   │   ├── ProviderLedgerReconciliationService.CorporateActions.cs
│   │   │   ├── ProviderLedgerReconciliationService.cs
│   │   │   ├── ProviderLedgerReconciliationService.Outcomes.cs
│   │   │   ├── ProviderLedgerReconciliationService.SecurityCoverage.cs
│   │   │   ├── ProviderModuleSetupModels.cs
│   │   │   ├── ProviderModuleSetupService.cs
│   │   │   ├── ProviderNavigationRouteMapper.cs
│   │   │   ├── ProviderReadinessService.cs
│   │   │   ├── ReconciliationApiService.cs
│   │   │   ├── ReconciliationBreakQueueProjection.cs
│   │   │   ├── ReportAccessPolicyEvaluator.cs
│   │   │   ├── ReportingAccessGrantService.cs
│   │   │   ├── ReportingArtifactVaultService.cs
│   │   │   ├── ReportingCertifiedArtifactProducer.cs
│   │   │   ├── ReportingDeliveryDispatcher.cs
│   │   │   ├── ReportingDeliveryReadModelSecurity.cs
│   │   │   ├── ReportingDeploymentReadinessService.cs
│   │   │   ├── ReportingGovernanceApiProjector.cs
│   │   │   ├── ReportingGovernanceCoordinatorService.ArtifactAccess.cs
│   │   │   ├── ReportingGovernanceCoordinatorService.ArtifactValidation.cs
│   │   │   ├── ReportingGovernanceCoordinatorService.cs
│   │   │   ├── ReportingGovernanceReleaseAuthorizationVerifier.cs
│   │   │   ├── ReportingPartnersCapitalSource.cs
│   │   │   ├── ReportingPrimaryDocumentRenderer.cs
│   │   │   ├── ReportingReconciliationEvidenceSource.cs
│   │   │   ├── ReportingReconciliationEvidenceStore.cs
│   │   │   ├── ReportingRunCertificationService.cs
│   │   │   ├── ReportingRunCommandService.cs
│   │   │   ├── ReportingRunReadinessService.cs
│   │   │   ├── ReportingRunStore.cs
│   │   │   ├── ReportingScheduleHostedService.cs
│   │   │   ├── ReportingScheduleService.cs
│   │   │   ├── ReportingScheduleService.DeliveryAuthority.cs
│   │   │   ├── ReportingScheduleService.OperationalAuthority.cs
│   │   │   ├── ReportingScheduleStore.OperationalAuthority.cs
│   │   │   ├── ReportingSecureDistributionApplicationService.cs
│   │   │   ├── ReportingSecureDistributionHostedService.cs
│   │   │   ├── ReportingSecureDistributionHttpRelayClient.cs
│   │   │   ├── ReportingSecureDistributionProviderReceipts.cs
│   │   │   ├── ReportingSecureDistributionServiceCollectionExtensions.cs
│   │   │   ├── ReportingStarterKitService.cs
│   │   │   ├── ReportingStateCorruptionException.cs
│   │   │   ├── ReportingWorkflowService.cs
│   │   │   ├── ReportPackDeliveryService.cs
│   │   │   ├── ReportPackDeliveryService.Evidence.cs
│   │   │   ├── ReportPackDeliveryService.Guards.cs
│   │   │   ├── ReportPackRestatementCandidateResolver.cs
│   │   │   ├── ReportPackRunReadService.Access.cs
│   │   │   ├── ReportPackRunReadService.CanonicalDeliveries.cs
│   │   │   ├── ReportPackRunReadService.cs
│   │   │   ├── ReportPackRunReadService.Models.cs
│   │   │   ├── ReportPackRunReadService.RunSnapshots.cs
│   │   │   ├── ReportPackSecurityLineIndex.cs
│   │   │   ├── ReportPackSecurityLineMatcher.cs
│   │   │   ├── ReportPackValidationService.cs
│   │   │   ├── ReportPackWorkflowRecordStore.cs
│   │   │   ├── ReportPeriodRange.cs
│   │   │   ├── ReportWriterDatasetSourceService.cs
│   │   │   ├── ReportWriterGridArtifactService.cs
│   │   │   ├── RiskRuleRuntimeService.cs
│   │   │   ├── SecurityMasterExceptionCaseworkService.cs
│   │   │   ├── SecurityMasterReconciliationSlaPolicyProvider.cs
│   │   │   ├── SecurityMasterSecurityReferenceLookup.cs
│   │   │   ├── SecurityMasterWorkbenchQueryService.cs
│   │   │   ├── SensitiveActionGovernance.cs
│   │   │   ├── SpreadsheetFormulaGuard.cs
│   │   │   ├── StatementFetchSchedulerService.cs
│   │   │   ├── StatementReconciliationCaseworkHandoffService.cs
│   │   │   ├── StatementReconciliationIntakeAuthority.cs
│   │   │   ├── StorageAssuranceService.cs
│   │   │   ├── StrategyRunComparisonService.cs
│   │   │   ├── StrategyRunReviewPacketService.cs
│   │   │   ├── TradingOperatorLiveOrderReadinessGate.cs
│   │   │   ├── TradingOperatorReadinessService.cs
│   │   │   ├── WorkstationServiceCollectionExtensions.cs
│   │   │   └── WorkstationWorkflowSummaryService.cs
│   │   ├── Streaming
│   │   │   ├── IQuoteStreamBroadcaster.cs
│   │   │   ├── QuoteStreamBroadcaster.cs
│   │   │   ├── QuoteStreamOptions.cs
│   │   │   ├── QuoteStreamSubscription.cs
│   │   │   ├── ReportRunStreamBroadcaster.cs
│   │   │   ├── StreamBroadcaster.cs
│   │   │   ├── StreamConnectionRegistry.cs
│   │   │   ├── StreamSubscription.cs
│   │   │   └── StreamTopic.cs
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
│   │   ├── GlobalUsings.SecurityMasterConcerns.cs
│   │   ├── HtmlTemplateGenerator.cs
│   │   ├── HtmlTemplateGenerator.Login.cs
│   │   ├── HtmlTemplateGenerator.Scripts.cs
│   │   ├── HtmlTemplateGenerator.Startup.cs
│   │   ├── HtmlTemplateGenerator.Styles.cs
│   │   ├── LeanAutoExportService.cs
│   │   ├── LeanSymbolMapper.cs
│   │   ├── Meridian.Ui.Shared.csproj
│   │   ├── README.md
│   │   └── ScoreExplanationProjection.cs
│   ├── Meridian.Workflow
│   │   ├── EnvironmentDesign
│   │   │   └── EnvironmentDesignerService.cs
│   │   ├── Runbooks
│   │   │   ├── RunbookExecutor.cs
│   │   │   ├── RunbookModels.cs
│   │   │   └── RunbookStore.cs
│   │   ├── Workflows
│   │   │   └── FundWorkflowCommandHandler.cs
│   │   ├── DesignModule.cs
│   │   ├── Meridian.Workflow.csproj
│   │   └── README.md
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
│   │   │   ├── IPageActivationLifetime.cs
│   │   │   └── IRemoteWorkstationClient.cs
│   │   ├── Controls
│   │   │   ├── AutomationLeafBorder.cs
│   │   │   ├── EmptyStatePanel.xaml
│   │   │   ├── EmptyStatePanel.xaml.cs
│   │   │   ├── EvidenceLinkChip.xaml
│   │   │   ├── EvidenceLinkChip.xaml.cs
│   │   │   ├── FilterChipBar.xaml
│   │   │   ├── FilterChipBar.xaml.cs
│   │   │   ├── FreshnessIndicator.xaml
│   │   │   ├── FreshnessIndicator.xaml.cs
│   │   │   ├── IconTextButton.xaml
│   │   │   ├── IconTextButton.xaml.cs
│   │   │   ├── InlineAlertPanel.xaml
│   │   │   ├── InlineAlertPanel.xaml.cs
│   │   │   ├── MetricCard.xaml
│   │   │   ├── MetricCard.xaml.cs
│   │   │   ├── SectionHeaderBar.xaml
│   │   │   ├── SectionHeaderBar.xaml.cs
│   │   │   ├── ToneBadge.xaml
│   │   │   ├── ToneBadge.xaml.cs
│   │   │   ├── WorkspaceAttentionRibbon.xaml
│   │   │   ├── WorkspaceAttentionRibbon.xaml.cs
│   │   │   ├── WorkspaceQueueCard.xaml
│   │   │   └── WorkspaceQueueCard.xaml.cs
│   │   ├── Converters
│   │   │   ├── BoolToStringConverter.cs
│   │   │   ├── BoolToVisibilityConverter.cs
│   │   │   ├── ConsoleEntryKindToBrushConverter.cs
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
│   │   │   ├── Home
│   │   │   │   └── HomeFeatureModule.cs
│   │   │   ├── Portfolio
│   │   │   │   ├── Shell
│   │   │   │   │   ├── PortfolioWorkspaceShellPage.xaml
│   │   │   │   │   └── PortfolioWorkspaceShellPage.xaml.cs
│   │   │   │   └── PortfolioFeatureModule.cs
│   │   │   ├── Reporting
│   │   │   │   ├── Shell
│   │   │   │   │   ├── ReportingWorkspaceShellPage.xaml
│   │   │   │   │   └── ReportingWorkspaceShellPage.xaml.cs
│   │   │   │   ├── ReportingFeatureModule.cs
│   │   │   │   ├── ReportingGovernanceApiClient.cs
│   │   │   │   ├── ReportingGovernanceWorkbenchViewModel.cs
│   │   │   │   └── ReportingRunRequestBuilder.cs
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
│   │   │   ├── FundAccountingRecordModels.cs
│   │   │   ├── FundLedgerDimensionView.cs
│   │   │   ├── FundPrivateCapitalCloseModels.cs
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
│   │   │   ├── SecurityMasterPresentationModels.cs
│   │   │   ├── SettingsModels.cs
│   │   │   ├── ShellNavigationCatalog.Accounting.cs
│   │   │   ├── ShellNavigationCatalog.cs
│   │   │   ├── ShellNavigationCatalog.Layouts.cs
│   │   │   ├── ShellNavigationCatalog.Strategy.cs
│   │   │   ├── ShellNavigationCatalog.Workspaces.cs
│   │   │   ├── ShellNavigationModels.cs
│   │   │   ├── ShellNavigationTextStyleGuide.cs
│   │   │   ├── StatementReconciliationWorkbenchModels.cs
│   │   │   ├── StorageDisplayModels.cs
│   │   │   ├── StrategyWorkspaceShellPresentationModels.cs
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
│   │   │   ├── WorkstationNavigationDefaults.cs
│   │   │   └── WorkstationOperatingContextModels.cs
│   │   ├── Services
│   │   │   ├── AccountingWorkspacePresentationService.cs
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
│   │   │   ├── DataWorkspacePresentationBuilder.cs
│   │   │   ├── DemoTourService.cs
│   │   │   ├── DesktopAuthenticationSession.cs
│   │   │   ├── DesktopConfigurationRecoveryService.cs
│   │   │   ├── DesktopLaunchArguments.cs
│   │   │   ├── DesktopLaunchTicketClient.cs
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
│   │   │   ├── ICommandContextProvider.cs
│   │   │   ├── IFundProfileCatalog.cs
│   │   │   ├── InfoBarService.cs
│   │   │   ├── IQuantScriptLayoutService.cs
│   │   │   ├── IViewModelViewResolver.cs
│   │   │   ├── IWorkspaceScopedService.cs
│   │   │   ├── IWorkspaceShellStateProvider.cs
│   │   │   ├── JumpListService.cs
│   │   │   ├── KeyboardShortcutService.cs
│   │   │   ├── LifecycleControlClient.cs
│   │   │   ├── LoggingService.cs
│   │   │   ├── MessagingService.cs
│   │   │   ├── ModelRoutingPolicyValidator.cs
│   │   │   ├── NavigationService.cs
│   │   │   ├── NotificationService.cs
│   │   │   ├── OfflineTrackingPersistenceService.cs
│   │   │   ├── OperationsControlCenterClient.cs
│   │   │   ├── PendingOperationsQueueService.cs
│   │   │   ├── QuantScriptExecutionHistoryService.cs
│   │   │   ├── QuantScriptLayoutService.cs
│   │   │   ├── QuantScriptTemplateCatalogService.cs
│   │   │   ├── ReconciliationReadService.cs
│   │   │   ├── ReportingWorkspaceShellPresentationService.cs
│   │   │   ├── RetentionAssuranceService.cs
│   │   │   ├── RunMatService.cs
│   │   │   ├── SchemaService.cs
│   │   │   ├── SecurityAssetProfileWorkflowClient.cs
│   │   │   ├── SecurityMasterOperatorWorkflowClient.cs
│   │   │   ├── SecurityMasterRuntimeStatusService.cs
│   │   │   ├── SetupWizardStateService.cs
│   │   │   ├── SingleInstanceService.cs
│   │   │   ├── StatementReconciliationWorkbenchService.cs
│   │   │   ├── StatusService.cs
│   │   │   ├── StorageService.cs
│   │   │   ├── StrategyRunWorkspaceService.cs
│   │   │   ├── StrategyWorkspaceShellPresentationService.cs
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
│   │   │   ├── WorkspaceLayoutManager.cs
│   │   │   ├── WorkspaceService.cs
│   │   │   ├── WorkspaceShellContextService.cs
│   │   │   ├── WorkspaceShellSlotContributionService.cs
│   │   │   ├── WorkspaceShellStateProviders.cs
│   │   │   ├── WorkspaceStateTokenStore.cs
│   │   │   ├── WorkstationAccountingApiClient.cs
│   │   │   ├── WorkstationOperatingContextScopeResolver.cs
│   │   │   ├── WorkstationOperatingContextService.cs
│   │   │   ├── WorkstationOperatorInboxApiClient.cs
│   │   │   ├── WorkstationReconciliationApiClient.cs
│   │   │   ├── WorkstationSecurityMasterApiClient.cs
│   │   │   ├── WorkstationStrategyBriefingService.cs
│   │   │   ├── WpfRemoteWorkstationClient.cs
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
│   │   │   ├── DataProvenanceBadge.xaml
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
│   │   │   │   ├── AccountingCloseViewModel.cs
│   │   │   │   ├── AccountingCloseViewModel.DraftParsing.cs
│   │   │   │   ├── AccountingCloseViewModel.Drafts.cs
│   │   │   │   ├── AccountingCloseViewModel.EvidenceRequests.cs
│   │   │   │   ├── AccountingCloseViewModel.MutationAuthority.cs
│   │   │   │   └── AccountingConfigureViewModel.cs
│   │   │   ├── AccountPortfolioViewModel.cs
│   │   │   ├── ActivityLogViewModel.cs
│   │   │   ├── AddProviderWizardViewModel.cs
│   │   │   ├── AdminMaintenanceViewModel.cs
│   │   │   ├── AdvancedAnalyticsViewModel.cs
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
│   │   │   ├── FinancialRecordExplorerViewModel.cs
│   │   │   ├── FundAccountProviderPanelModels.cs
│   │   │   ├── FundAccountsViewModel.cs
│   │   │   ├── FundLedgerViewModel.cs
│   │   │   ├── FundLedgerViewModel.PrivateCapitalCloseScope.cs
│   │   │   ├── FundLedgerViewModel.Reconciliation.cs
│   │   │   ├── FundLedgerViewModel.ReviewedAutomation.cs
│   │   │   ├── FundLedgerViewModel.Sections.cs
│   │   │   ├── FundLedgerViewModel.StatementReconciliation.cs
│   │   │   ├── FundProfileSelectionViewModel.cs
│   │   │   ├── FundStructureSetupViewModel.cs
│   │   │   ├── HomeWorkspaceViewModel.cs
│   │   │   ├── IndexSubscriptionViewModel.cs
│   │   │   ├── IPageActionBarProvider.cs
│   │   │   ├── LeanIntegrationViewModel.cs
│   │   │   ├── LifecycleControlViewModel.cs
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
│   │   │   ├── ProviderAccountingViewModel.cs
│   │   │   ├── ProviderDataProjectionViewModel.cs
│   │   │   ├── ProviderHealthViewModel.cs
│   │   │   ├── ProviderHealthViewModel.Sections.cs
│   │   │   ├── ProviderPageModels.cs
│   │   │   ├── ProviderViewModel.cs
│   │   │   ├── QualityArchiveViewModel.cs
│   │   │   ├── QuantScriptViewModel.cs
│   │   │   ├── QuantScriptViewModel.Sections.cs
│   │   │   ├── QuoteFloatViewModel.cs
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
│   │   │   ├── SecurityPassportEditorViewModel.cs
│   │   │   ├── ServiceManagerViewModel.cs
│   │   │   ├── SettingsViewModel.AssetProfiles.cs
│   │   │   ├── SettingsViewModel.cs
│   │   │   ├── SettingsViewModel.OperationsControl.cs
│   │   │   ├── SetupWizardViewModel.cs
│   │   │   ├── SplitPaneViewModel.cs
│   │   │   ├── StartupWindowViewModel.cs
│   │   │   ├── StatusBarViewModel.cs
│   │   │   ├── StorageOptimizationViewModel.cs
│   │   │   ├── StorageViewModel.cs
│   │   │   ├── StrategyRunBrowserViewModel.cs
│   │   │   ├── StrategyRunDetailViewModel.cs
│   │   │   ├── StrategyRunLedgerViewModel.cs
│   │   │   ├── StrategyRunPortfolioViewModel.cs
│   │   │   ├── StrategyWorkspaceShellViewModel.cs
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
│   │   │   ├── AccountingClosePage.xaml
│   │   │   ├── AccountingClosePage.xaml.cs
│   │   │   ├── AccountingConfigurePage.xaml
│   │   │   ├── AccountingConfigurePage.xaml.cs
│   │   │   ├── AccountingWorkspaceShellPage.xaml
│   │   │   ├── AccountingWorkspaceShellPage.xaml.cs
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
│   │   │   ├── FinancialRecordExplorerPage.xaml
│   │   │   ├── FinancialRecordExplorerPage.xaml.cs
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
│   │   │   ├── HelpPage.xaml
│   │   │   ├── HelpPage.xaml.cs
│   │   │   ├── HomeWorkspacePage.xaml
│   │   │   ├── HomeWorkspacePage.xaml.cs
│   │   │   ├── IndexSubscriptionPage.xaml
│   │   │   ├── IndexSubscriptionPage.xaml.cs
│   │   │   ├── InstitutionalCommandPaletteControl.xaml
│   │   │   ├── InstitutionalCommandPaletteControl.xaml.cs
│   │   │   ├── InstitutionalShellFrameControl.cs
│   │   │   ├── KeyboardShortcutsPage.xaml
│   │   │   ├── KeyboardShortcutsPage.xaml.cs
│   │   │   ├── LeanIntegrationPage.xaml
│   │   │   ├── LeanIntegrationPage.xaml.cs
│   │   │   ├── LifecycleControlPage.xaml
│   │   │   ├── LifecycleControlPage.xaml.cs
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
│   │   │   ├── SecretInputControl.xaml
│   │   │   ├── SecretInputControl.xaml.cs
│   │   │   ├── SecurityMasterPage.xaml
│   │   │   ├── SecurityMasterPage.xaml.cs
│   │   │   ├── SecurityPassportEditorPage.xaml
│   │   │   ├── SecurityPassportEditorPage.xaml.cs
│   │   │   ├── SecurityPassportEditorView.xaml
│   │   │   ├── SecurityPassportEditorView.xaml.cs
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
│   │   │   ├── StartupWindow.xaml
│   │   │   ├── StartupWindow.xaml.cs
│   │   │   ├── StatusBarControl.xaml
│   │   │   ├── StatusBarControl.xaml.cs
│   │   │   ├── StorageOptimizationPage.xaml
│   │   │   ├── StorageOptimizationPage.xaml.cs
│   │   │   ├── StoragePage.xaml
│   │   │   ├── StoragePage.xaml.cs
│   │   │   ├── StrategyRunsPage.xaml
│   │   │   ├── StrategyRunsPage.xaml.cs
│   │   │   ├── StrategyWorkspaceShellPage.xaml
│   │   │   ├── StrategyWorkspaceShellPage.xaml.cs
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
│   │   │   │   ├── WorkstationStatePanelControl.xaml.cs
│   │   │   │   ├── WorkstationTableInspectorControl.xaml
│   │   │   │   └── WorkstationTableInspectorControl.xaml.cs
│   │   │   ├── Diagnostics
│   │   │   │   └── WorkstationDiagnosticsControls.cs
│   │   │   ├── Layout
│   │   │   │   └── WorkstationLayoutControls.cs
│   │   │   ├── Models
│   │   │   │   ├── EvidenceVaultPresentationModels.cs
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
│   │   │       │   ├── BatchedObservableCollection.cs
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
│   │   │       └── WorkspaceViewModelAdapters.cs
│   │   ├── App.xaml
│   │   ├── App.xaml.cs
│   │   ├── AssemblyInfo.cs
│   │   ├── GlobalUsings.cs
│   │   ├── GlobalUsings.SecurityMasterConcerns.cs
│   │   ├── MainWindow.xaml
│   │   ├── MainWindow.xaml.cs
│   │   ├── Meridian.Wpf.csproj
│   │   ├── Package.appxmanifest
│   │   └── README.md
│   └── README.md
├── tests
│   ├── fixtures
│   │   ├── corporate-actions
│   │   │   └── golden
│   │   │       ├── aapl-split-4to1-2020.json
│   │   │       ├── alias-normalization-mixed.json
│   │   │       ├── bond-call-101-5.json
│   │   │       ├── cash-stock-merger-2018.json
│   │   │       ├── dividend-amended-supersede.json
│   │   │       ├── dividend-cancelled-after-announce.json
│   │   │       ├── dividend-missing-prior-bar.json
│   │   │       ├── dividend-no-position-context.json
│   │   │       ├── ge-reverse-split-1for8-2021.json
│   │   │       ├── KNOWN-DEFECTS.md
│   │   │       ├── mbs-factor-paydown.json
│   │   │       ├── special-dividend-25pct-synthetic.json
│   │   │       └── t-wbd-spinoff-2022.json
│   │   ├── portfolio
│   │   │   ├── source-payloads
│   │   │   │   ├── bank-statement-2026-06-04.csv
│   │   │   │   ├── broker-positions-2026-06-04.csv
│   │   │   │   ├── corporate-actions-and-loan-events-2026-06-04.json
│   │   │   │   ├── custodian-holdings-2026-06-04.csv
│   │   │   │   ├── pricing-snapshot-2026-06-04.csv
│   │   │   │   └── servicer-personal-loans-2026-06-04.csv
│   │   │   ├── generate_mixed_credit_status_set.py
│   │   │   └── mixed-credit-status-set.json
│   │   └── security-instrument-explorer-parity.json
│   ├── Meridian.Backtesting.Tests
│   │   ├── AdvancedCarryDecisionEngineTests.cs
│   │   ├── BacktestEngineIntegrationTests.cs
│   │   ├── BacktestMetricsEngineTests.cs
│   │   ├── BacktestPreflightServiceTests.cs
│   │   ├── BacktestRequestConfigTests.cs
│   │   ├── BacktestTrustworthinessTests.cs
│   │   ├── BatchBacktestServiceTests.cs
│   │   ├── BracketOrderTests.cs
│   │   ├── CanonicalBacktestResultNormalizerTests.cs
│   │   ├── ConservativeFillModelTests.cs
│   │   ├── CorporateActionAdjustmentPropertyTests.cs
│   │   ├── CorporateActionAdjustmentServiceTests.cs
│   │   ├── CorporateActionGoldenAdjustmentTests.cs
│   │   ├── CorporateActionKnownDefectTests.cs
│   │   ├── CorporateActionSeriesInvariants.cs
│   │   ├── FillModelExpansionTests.cs
│   │   ├── FillModelTests.cs
│   │   ├── GlobalUsings.cs
│   │   ├── GlobalUsings.SecurityMasterConcerns.cs
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
│   │   ├── WalkForwardServiceTests.cs
│   │   ├── XirrCalculatorTests.cs
│   │   └── YahooFinanceBacktestIntegrationTests.cs
│   ├── Meridian.DesignModules.Tests
│   │   ├── DesignModulePhysicalConformanceTests.cs
│   │   └── Meridian.DesignModules.Tests.csproj
│   ├── Meridian.DirectLending.Tests
│   │   ├── DirectLendingDatabaseFactAttribute.cs
│   │   ├── DirectLendingPostgresIntegrationTests.cs
│   │   ├── DirectLendingPostgresTestDatabase.cs
│   │   ├── DirectLendingServiceTests.cs
│   │   ├── DirectLendingWorkflowTests.cs
│   │   ├── GlobalUsings.cs
│   │   └── Meridian.DirectLending.Tests.csproj
│   ├── Meridian.FSharp.Tests
│   │   ├── AccountDetailsTests.fs
│   │   ├── CalculationTests.fs
│   │   ├── CanonicalizationTests.fs
│   │   ├── CashFlowProjectorTests.fs
│   │   ├── DeterministicInvariantTests.fs
│   │   ├── DirectLendingInteropTests.fs
│   │   ├── DomainTests.fs
│   │   ├── FundEconomicsTests.fs
│   │   ├── LedgerKernelTests.fs
│   │   ├── Meridian.FSharp.Tests.fsproj
│   │   ├── OperationsContinuityRulesTests.fs
│   │   ├── PeriodManagementTests.fs
│   │   ├── PipelineTests.fs
│   │   ├── PromotionPolicyTests.fs
│   │   ├── ReconciliationCaseWorkflowTests.fs
│   │   ├── ReportPackValidationRulesTests.fs
│   │   ├── RiskPolicyTests.fs
│   │   ├── SecurityCalculationsTests.fs
│   │   ├── SensitiveActionPolicyTests.fs
│   │   ├── SettlementInstructionCommandsTests.fs
│   │   ├── TradingReadinessRulesTests.fs
│   │   ├── TradingTransitionTests.fs
│   │   └── ValidationTests.fs
│   ├── Meridian.FundStructure.Tests
│   │   ├── EnvironmentDesignerServiceTests.cs
│   │   ├── FundStructurePolicyServiceTests.cs
│   │   ├── FundStructureSetupWorkflowServiceTests.cs
│   │   ├── GlobalUsings.SecurityMasterConcerns.cs
│   │   ├── GovernanceSharedDataAccessServiceTests.cs
│   │   ├── InMemoryFundStructureServiceTests.cs
│   │   ├── LedgerGroupingRulesTests.cs
│   │   └── Meridian.FundStructure.Tests.csproj
│   ├── Meridian.Lifecycle.Tests
│   │   ├── ApplicationLifecycleCoordinatorTests.cs
│   │   ├── JsonLifecycleReceiptStoreTests.cs
│   │   ├── Meridian.Lifecycle.Tests.csproj
│   │   ├── RuntimeReadinessServiceTests.cs
│   │   ├── RuntimeShutdownSequenceTests.cs
│   │   └── WorkstationModeRunnerTests.cs
│   ├── Meridian.LifecycleSupervisor.Tests
│   │   ├── LifecycleDatabaseAclTests.cs
│   │   ├── LifecycleDatabaseToolTests.cs
│   │   ├── LifecycleStartupOutcomeTests.cs
│   │   ├── LifecycleSupervisorConfigurationTests.cs
│   │   ├── LifecycleSupervisorPipeTests.cs
│   │   ├── LifecycleSupervisorRuntimeTests.cs
│   │   └── Meridian.LifecycleSupervisor.Tests.csproj
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
│   │   │   ├── Accounting
│   │   │   │   ├── DailyMarkToMarketServiceTests.cs
│   │   │   │   └── DailyValuationPolicyTests.cs
│   │   │   ├── Auth
│   │   │   │   ├── RolePermissionsTests.cs
│   │   │   │   └── ScopedAccessServiceTests.cs
│   │   │   ├── Backfill
│   │   │   │   ├── AdditionalProviderContractTests.cs
│   │   │   │   ├── AutoGapRemediationServiceTests.cs
│   │   │   │   ├── BackfillCoordinatorPreviewTests.cs
│   │   │   │   ├── BackfillCoordinatorStorageOptionsTests.cs
│   │   │   │   ├── BackfillCostEstimatorTests.cs
│   │   │   │   ├── BackfillExecutionHistoryTests.cs
│   │   │   │   ├── BackfillScheduleManagerDurabilityTests.cs
│   │   │   │   ├── BackfillWorkerServiceLifecycleTests.cs
│   │   │   │   ├── BackfillWorkerServiceTests.cs
│   │   │   │   ├── CompositeHistoricalDataProviderTests.cs
│   │   │   │   ├── CrossSourceBackfillReconciliationServiceTests.cs
│   │   │   │   ├── GapBackfillServiceTests.cs
│   │   │   │   ├── HistoricalBackfillEraSymbolTests.cs
│   │   │   │   ├── HistoricalProviderContractTests.cs
│   │   │   │   ├── ParallelBackfillServiceTests.cs
│   │   │   │   ├── PriorityBackfillQueueTests.cs
│   │   │   │   ├── RateLimiterTests.cs
│   │   │   │   ├── ScheduledBackfillTests.cs
│   │   │   │   ├── TwelveDataNasdaqProviderContractTests.cs
│   │   │   │   └── YahooFinanceIntradayContractTests.cs
│   │   │   ├── Backtesting
│   │   │   │   └── BacktestStudioRunOrchestratorTests.cs
│   │   │   ├── Commands
│   │   │   │   ├── CliArgumentsTests.cs
│   │   │   │   ├── CommandDispatcherTests.cs
│   │   │   │   ├── CommandTestConsole.cs
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
│   │   │   │   │   ├── ModeRunners
│   │   │   │   │   │   └── CommandModeRunnerTests.cs
│   │   │   │   │   └── SharedStartupBootstrapperTests.cs
│   │   │   │   ├── BackfillFeatureRegistrationTests.cs
│   │   │   │   ├── DiagnosticsFeatureRegistrationTests.cs
│   │   │   │   ├── DirectLendingStartupTests.cs
│   │   │   │   ├── HostStartupLifecycleTests.cs
│   │   │   │   ├── LegacySnapshotStartupTests.cs
│   │   │   │   ├── PipelineFeatureRegistrationTests.cs
│   │   │   │   ├── ProcessWideHostedServiceRegistrationTests.cs
│   │   │   │   ├── ProductionRegistrationGuardServiceTests.cs
│   │   │   │   ├── ProductionServiceRegistrationPolicyTests.cs
│   │   │   │   ├── ProviderCapabilityContractRegistrationTests.cs
│   │   │   │   ├── ProviderFeatureRegistrationTests.cs
│   │   │   │   ├── SecurityMasterStartupTests.cs
│   │   │   │   └── StorageFeatureRegistrationTests.cs
│   │   │   ├── Config
│   │   │   │   ├── AppSettingsSampleTests.cs
│   │   │   │   ├── ConfigSchemaIntegrationTests.cs
│   │   │   │   ├── ConfigurationUnificationTests.cs
│   │   │   │   ├── ConfigValidatorCliTests.cs
│   │   │   │   ├── ProviderCredentialResolverTests.cs
│   │   │   │   └── ProviderCredentialStoreTests.cs
│   │   │   ├── Coordination
│   │   │   │   └── SubscriptionOrchestratorCoordinationTests.cs
│   │   │   ├── Credentials
│   │   │   │   └── CredentialTestingServiceTests.cs
│   │   │   ├── DataQuality
│   │   │   │   ├── CompositeDataQualityReadServiceTests.cs
│   │   │   │   └── QualityMonitoringPublisherTests.cs
│   │   │   ├── DirectLending
│   │   │   │   ├── AccrualLedgerServiceTests.cs
│   │   │   │   ├── DailyAccrualWorkerTests.cs
│   │   │   │   ├── DirectLendingEventRebuilderTests.cs
│   │   │   │   ├── DirectLendingOutboxDispatcherTests.cs
│   │   │   │   ├── DirectLendingServicerStatementServiceTests.cs
│   │   │   │   └── PostgresDirectLendingCommandServiceTests.cs
│   │   │   ├── FundStructure
│   │   │   │   └── LedgerGroupIdTests.cs
│   │   │   ├── Indicators
│   │   │   │   └── TechnicalIndicatorServiceTests.cs
│   │   │   ├── Integrations
│   │   │   │   ├── ProviderIntegrationActivationReadinessServiceTests.cs
│   │   │   │   ├── ProviderIntegrationActivationServiceTests.cs
│   │   │   │   ├── ProviderIntegrationDryRunServiceTests.cs
│   │   │   │   ├── ProviderIntegrationHttpClientTransportTests.cs
│   │   │   │   ├── ProviderIntegrationIdentityResolutionPreviewServiceTests.cs
│   │   │   │   ├── ProviderIntegrationMonitoringServiceTests.cs
│   │   │   │   ├── ProviderIntegrationOpenApiImportServiceTests.cs
│   │   │   │   ├── ProviderIntegrationPromotionReadinessServiceTests.cs
│   │   │   │   ├── ProviderIntegrationQuarantineReplayServiceTests.cs
│   │   │   │   ├── ProviderIntegrationQuarantineReviewServiceTests.cs
│   │   │   │   ├── ProviderIntegrationReconciliationHandoffServiceTests.cs
│   │   │   │   ├── ProviderIntegrationRestDryRunServiceTests.cs
│   │   │   │   ├── ProviderIntegrationSchemaDriftServiceTests.cs
│   │   │   │   ├── ProviderIntegrationSetupServiceTests.cs
│   │   │   │   ├── ProviderIntegrationStagingReviewServiceTests.cs
│   │   │   │   ├── ProviderIntegrationSyncOrchestrationServiceTests.cs
│   │   │   │   ├── ProviderIntegrationSyncPlanningServiceTests.cs
│   │   │   │   └── ProviderIntegrationTemplateCatalogTests.cs
│   │   │   ├── Logging
│   │   │   │   └── LoggingSetupTests.cs
│   │   │   ├── Monitoring
│   │   │   │   ├── PrometheusExporterTests.cs
│   │   │   │   ├── PrometheusMetricsTests.cs
│   │   │   │   ├── PrometheusResettableTotalCollection.cs
│   │   │   │   ├── PrometheusResettableTotalTests.cs
│   │   │   │   ├── QualityTrendCalculationTests.cs
│   │   │   │   └── StatusWriterTests.cs
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
│   │   │   │   ├── BusinessDayAccountingCalendarTests.cs
│   │   │   │   ├── CanonicalReconciliationMatchingEngineTests.cs
│   │   │   │   ├── DefaultReconciliationIngestionSchedulerTests.cs
│   │   │   │   ├── FileStatementReconciliationCheckpointStoreTests.cs
│   │   │   │   ├── ReconciliationMatchingFloorTests.cs
│   │   │   │   ├── ReconciliationMatchKernelTests.cs
│   │   │   │   ├── ReconciliationNormalizationServiceTests.cs
│   │   │   │   ├── ReconciliationRunOrchestratorTests.cs
│   │   │   │   ├── StatementMatchingEngineTests.cs
│   │   │   │   ├── StatementReconciliationContextAdapterTests.cs
│   │   │   │   ├── StatementReconciliationOrchestratorTests.cs
│   │   │   │   ├── StatementRepositoryTests.cs
│   │   │   │   └── StatementValidationServiceTests.cs
│   │   │   ├── SecurityMaster
│   │   │   │   ├── CanonicalSymbolRegistryMigrationServiceTests.cs
│   │   │   │   ├── EdgarIngestOrchestratorTests.cs
│   │   │   │   ├── SecurityMasterFieldConflictDetectionTests.cs
│   │   │   │   ├── SecurityMasterImportServiceTests.cs
│   │   │   │   └── SecurityMasterMappingInteropTests.cs
│   │   │   ├── Services
│   │   │   │   ├── CliModeResolverTests.cs
│   │   │   │   ├── ConfigurationPresetsTests.cs
│   │   │   │   ├── ConfigurationServiceTests.cs
│   │   │   │   ├── CronExpressionParserTests.cs
│   │   │   │   ├── ErrorCodeMappingTests.cs
│   │   │   │   ├── ExecutionSimulationOrchestratorTests.cs
│   │   │   │   ├── FundOperationsWorkspaceReadServiceTests.cs
│   │   │   │   ├── GracefulShutdownTests.cs
│   │   │   │   ├── OperationalSchedulerTests.cs
│   │   │   │   ├── PreflightCheckerTests.cs
│   │   │   │   ├── ProviderTradingCalendarContractsTests.cs
│   │   │   │   └── RuntimeDiagnosticRedactorTests.cs
│   │   │   ├── Subscriptions
│   │   │   │   └── SubscriptionStoreQuarantineTests.cs
│   │   │   ├── Ui
│   │   │   │   ├── ConfigStoreTests.cs
│   │   │   │   ├── ProviderCredentialStoreTests.cs
│   │   │   │   └── ProviderModuleSetupServiceTests.cs
│   │   │   ├── Wizard
│   │   │   │   └── WizardConfigurationStepTests.cs
│   │   │   ├── DirectLendingServiceTests.cs
│   │   │   ├── FileReconciliationRunRepositoryTests.cs
│   │   │   ├── OperationsContinuityPostgresRoundTripTests.cs
│   │   │   ├── OperationsContinuityWorkflowServiceTests.cs
│   │   │   ├── ReconciliationGovernanceServiceTests.cs
│   │   │   ├── ReconciliationRunServiceTests.cs
│   │   │   ├── SecurityMasterCashFlowServiceTests.cs
│   │   │   ├── StructuredCashFlowLedgerBridgeTests.cs
│   │   │   └── StructuredCashFlowTermsResolverTests.cs
│   │   ├── Architecture
│   │   │   ├── AccountingSemanticsBoundaryTests.cs
│   │   │   ├── LayerBoundaryTests.cs
│   │   │   └── LedgerNetBalanceCentralizationTests.cs
│   │   ├── AssetOperations
│   │   │   ├── AssetAccountingEventSpineContractTests.cs
│   │   │   ├── AssetAccountingEvidenceSubjectContractTests.cs
│   │   │   ├── AssetOperationsMigrationRunnerTests.cs
│   │   │   ├── AssetOperationsReadServiceTests.cs
│   │   │   ├── FactorPaydownProjectionServiceTests.cs
│   │   │   ├── InMemoryAssetAccountingEventProjectionStoreTests.cs
│   │   │   ├── InMemoryInstrumentPositionProjectionStoreSlice3Tests.cs
│   │   │   ├── InstrumentPositionProjectionStoreTests.cs
│   │   │   ├── PortfolioCashLadderEngineTests.cs
│   │   │   ├── PortfolioCashLadderReadServiceTests.cs
│   │   │   └── RetainedEvidenceIdentityValidatorTests.cs
│   │   ├── Backtesting
│   │   │   └── PluginBacktestStrategyLiveSourceTests.cs
│   │   ├── CertificatesOfDeposit
│   │   │   └── CertificateOfDepositProjectionServiceTests.cs
│   │   ├── Commodities
│   │   │   └── CommodityProjectionServiceTests.cs
│   │   ├── Compliance
│   │   │   ├── AccessReviewServiceTests.cs
│   │   │   ├── CompliancePolicyEngineTests.cs
│   │   │   └── ImmutableAuditLogServiceTests.cs
│   │   ├── Contracts
│   │   │   ├── Api
│   │   │   │   └── UiApiClientTests.cs
│   │   │   ├── AppConfigDtoRoundTripTests.cs
│   │   │   ├── CoreExtensibilityContractsTests.cs
│   │   │   ├── DataProvenanceTests.cs
│   │   │   ├── FundStructureContractsJsonContextTests.cs
│   │   │   ├── InstrumentJournalContractCompatibilityTests.cs
│   │   │   ├── LedgerReconciliationContractCompatibilityTests.cs
│   │   │   ├── OperationalFinanceContractTests.cs
│   │   │   ├── ProviderIntegrationContractsTests.cs
│   │   │   └── VerifiedOperationOutcomeTests.cs
│   │   ├── Core
│   │   │   ├── Config
│   │   │   │   ├── AppConfigJsonOptionsTests.cs
│   │   │   │   ├── ConfigEnvironmentOverrideTests.cs
│   │   │   │   ├── ConfigJsonSchemaGeneratorTests.cs
│   │   │   │   ├── ConfigValidationPipelineTests.cs
│   │   │   │   ├── ConfigValidatorTests.cs
│   │   │   │   ├── ConfigWatcherTests.cs
│   │   │   │   ├── CredentialPlaceholderDetectorTests.cs
│   │   │   │   ├── DataSourceKindConverterTests.cs
│   │   │   │   └── DefaultConfigPathResolverTests.cs
│   │   │   ├── BackoffTests.cs
│   │   │   └── CircuitBreakerTests.cs
│   │   ├── CryptoCurrency
│   │   │   └── CryptoProjectionServiceTests.cs
│   │   ├── DataIntegration
│   │   │   ├── AccountingSystem
│   │   │   │   └── QuickBooks
│   │   │   │       └── QuickBooksOnlineProviderCredentialConnectionStoreTests.cs
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
│   │   │   │   ├── CanonicalizationGoldenFixtureTests.cs
│   │   │   │   ├── CanonicalizingPublisherTests.cs
│   │   │   │   ├── ConditionCodeMapperTests.cs
│   │   │   │   ├── EventCanonicalizerTests.cs
│   │   │   │   └── VenueMicMapperTests.cs
│   │   │   ├── Credentials
│   │   │   │   ├── CredentialStatusTests.cs
│   │   │   │   └── OAuthTokenTests.cs
│   │   │   ├── Etl
│   │   │   │   ├── EtlExportServiceTests.cs
│   │   │   │   ├── EtlJobOrchestratorTests.cs
│   │   │   │   ├── EtlNormalizationServiceTests.cs
│   │   │   │   └── EtlPreviewServiceTests.cs
│   │   │   ├── Historical
│   │   │   │   ├── HistoricalDataQueryServiceBarsTests.cs
│   │   │   │   └── HistoricalDataQueryServiceTests.cs
│   │   │   ├── Monitoring
│   │   │   │   ├── DataQuality
│   │   │   │   │   ├── DataFreshnessSlaMonitorTests.cs
│   │   │   │   │   ├── DataQualityTests.cs
│   │   │   │   │   └── LiquidityProfileTests.cs
│   │   │   │   ├── BadTickFilterTests.cs
│   │   │   │   ├── ClockSkewEstimatorTests.cs
│   │   │   │   ├── ConnectionStatusWebhookTests.cs
│   │   │   │   ├── DataLossAccountingTests.cs
│   │   │   │   ├── PriceContinuityCheckerTests.cs
│   │   │   │   ├── ProviderDegradationCalibrationTests.cs
│   │   │   │   ├── ProviderDegradationScorerTests.cs
│   │   │   │   ├── ProviderLatencyServiceTests.cs
│   │   │   │   ├── SchemaValidationServiceTests.cs
│   │   │   │   ├── SpreadMonitorTests.cs
│   │   │   │   └── TickSizeValidatorTests.cs
│   │   │   ├── Services
│   │   │   │   └── DataQuality
│   │   │   │       ├── AnomalyDetectorTests.cs
│   │   │   │       ├── CompletenessScoreCalculatorTests.cs
│   │   │   │       ├── GapAnalyzerTests.cs
│   │   │   │       └── SequenceErrorTrackerTests.cs
│   │   │   ├── CredentialStoreExtensionsTests.cs
│   │   │   └── MarketEventFilterTests.cs
│   │   ├── Demo
│   │   │   ├── DemoWorkspaceGuardTests.cs
│   │   │   ├── DemoWorkspaceSeederTests.cs
│   │   │   └── DemoWorkspaceSmokeTests.cs
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
│   │   ├── Entities
│   │   │   └── FundStructure
│   │   │       └── LedgerGroupingRulesTests.cs
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
│   │   │   ├── MultiCurrency
│   │   │   │   └── InMemoryFxRateProviderTests.cs
│   │   │   ├── BrokerageExecutionReconciliationServiceTests.cs
│   │   │   ├── BrokerageGatewayAdapterTests.cs
│   │   │   ├── BrokerageOrderPlacementGateTests.cs
│   │   │   ├── BrokerageValidationEvaluatorTests.cs
│   │   │   ├── ExecutionAuditTrailServiceTests.cs
│   │   │   ├── ExecutionOrderMetadataPolicyTests.cs
│   │   │   ├── HostedBrokerageGatewayRegistrationTests.cs
│   │   │   ├── LogSanitizerTests.cs
│   │   │   ├── MultiAccountPaperTradingPortfolioTests.cs
│   │   │   ├── OmsGovernedBrokerageOrderGatewayTests.cs
│   │   │   ├── OrderManagementSystemDurableHandoffOrderingTests.cs
│   │   │   ├── OrderManagementSystemGovernanceTests.cs
│   │   │   ├── OrderManagementSystemReportStreamTests.cs
│   │   │   ├── OrderManagementSystemTests.cs
│   │   │   ├── PaperExecutionGatewayLotSizeTests.cs
│   │   │   ├── PaperGatewayLiveFeedPricingTests.cs
│   │   │   ├── PaperSessionPersistenceServiceTests.cs
│   │   │   ├── PaperTradingGatewayTests.cs
│   │   │   ├── PaperTradingPortfolioLotSelectionTests.cs
│   │   │   ├── PaperTradingPortfolioLotSnapshotTests.cs
│   │   │   ├── PaperTradingPortfolioTests.cs
│   │   │   ├── PortfolioStatePositionTrackerTests.cs
│   │   │   ├── PositionLotSelectorTests.cs
│   │   │   ├── RiskValidationResultTests.cs
│   │   │   ├── SessionTcaReporterTests.cs
│   │   │   ├── TradeFillHandoffFailureStoreConcurrencyTests.cs
│   │   │   └── TradierExecutionReconciliationTests.cs
│   │   ├── FinancialOperations
│   │   │   ├── AccountingClose
│   │   │   │   └── AccountingCloseServicesTests.cs
│   │   │   ├── Banking
│   │   │   │   ├── BankTransactionSeedTests.cs
│   │   │   │   └── PaymentApprovalTests.cs
│   │   │   ├── Ledger
│   │   │   │   ├── AccountingBasisProjectionSetServiceTests.cs
│   │   │   │   ├── AccountingJournalDraftServiceTests.cs
│   │   │   │   ├── AccountingPolicyServiceTests.cs
│   │   │   │   ├── AccountingPostingCandidateServiceTests.cs
│   │   │   │   ├── AssetAccountingEventSpineServiceTests.cs
│   │   │   │   └── AssetAccountingLifecycleSeparationTests.cs
│   │   │   ├── OperationsContinuity
│   │   │   │   └── FinancialOperationsCommandCenterReadServiceTests.cs
│   │   │   ├── PrivateCapital
│   │   │   │   ├── CapitalCallDraftFactoryTests.cs
│   │   │   │   ├── CapitalCallPlanBuilderTests.cs
│   │   │   │   ├── CommitmentRollForwardCalculatorTests.cs
│   │   │   │   ├── DefaultInterestCalculatorTests.cs
│   │   │   │   ├── PrivateCapitalCloseCockpitServiceTests.cs
│   │   │   │   └── PrivateCapitalFundEventLedgerReadinessBuilderTests.cs
│   │   │   ├── Reconciliation
│   │   │   │   └── ReconciliationEngineServiceTests.cs
│   │   │   ├── FundAdministrationControlServiceTests.cs
│   │   │   └── MiddleOfficeOperationsServiceTests.cs
│   │   ├── FixedIncome
│   │   │   └── BondProjectionServiceTests.cs
│   │   ├── FundStructure
│   │   │   └── OwnershipGraphValidationTests.cs
│   │   ├── Futures
│   │   │   └── FutureProjectionServiceTests.cs
│   │   ├── FxSpot
│   │   │   └── FxSpotProjectionServiceTests.cs
│   │   ├── Identity
│   │   │   ├── FundStructure
│   │   │   │   └── FundAccountTraversalQueryServiceTests.cs
│   │   │   ├── FileUserAccountStoreTests.cs
│   │   │   ├── FundStructureAccessScopeLineageProviderTests.cs
│   │   │   ├── GovernanceStoreDataIntegrityTests.cs
│   │   │   ├── IdentityTestSupport.cs
│   │   │   ├── InitialAccountBootstrapServiceTests.cs
│   │   │   ├── LoginSessionServiceTests.cs
│   │   │   ├── PasswordHashingTests.cs
│   │   │   └── UserProfileRegistryTests.cs
│   │   ├── Infrastructure
│   │   │   ├── Adapters
│   │   │   │   └── TradierCanonicalMappersTests.cs
│   │   │   ├── DataSources
│   │   │   │   └── CredentialConfigTests.cs
│   │   │   ├── Etl
│   │   │   │   ├── CsvPartnerFileParserTests.cs
│   │   │   │   ├── SftpCapabilityServiceTests.cs
│   │   │   │   └── SftpInfrastructureTests.cs
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
│   │   │   │   ├── AlpacaAssetStreamRoutingTests.cs
│   │   │   │   ├── AlpacaBrokerageGatewayTests.cs
│   │   │   │   ├── AlpacaCorporateActionProviderTests.cs
│   │   │   │   ├── AlpacaCredentialAndReconnectTests.cs
│   │   │   │   ├── AlpacaHistoricalDataProviderTests.cs
│   │   │   │   ├── AlpacaMessageParsingTests.cs
│   │   │   │   ├── AlpacaQuotePipelineGoldenTests.cs
│   │   │   │   ├── AlpacaQuoteRoutingTests.cs
│   │   │   │   ├── AlpacaStreamDiagnosticsTests.cs
│   │   │   │   ├── AlpacaSymbolSearchProviderTests.cs
│   │   │   │   ├── AlphaVantageCorporateActionProviderTests.cs
│   │   │   │   ├── AlphaVantageHistoricalDataProviderTests.cs
│   │   │   │   ├── AlphaVantageSymbolSearchProviderTests.cs
│   │   │   │   ├── BackfillBarValidationTests.cs
│   │   │   │   ├── BackfillRetryAfterTests.cs
│   │   │   │   ├── BaseSymbolSearchProviderTests.cs
│   │   │   │   ├── CanonicalRegistrySymbolResolverTests.cs
│   │   │   │   ├── CompositeProviderStaleDataTests.cs
│   │   │   │   ├── EdgarReferenceDataProviderTests.cs
│   │   │   │   ├── EdgarSymbolSearchProviderTests.cs
│   │   │   │   ├── FailoverAwareMarketDataClientTests.cs
│   │   │   │   ├── FinnhubCorporateActionProviderTests.cs
│   │   │   │   ├── FinnhubHistoricalDataProviderTests.cs
│   │   │   │   ├── FinnhubSymbolSearchProviderTests.cs
│   │   │   │   ├── FredHistoricalDataProviderTests.cs
│   │   │   │   ├── FredSymbolSearchProviderTests.cs
│   │   │   │   ├── FreeHistoricalProviderParsingTests.cs
│   │   │   │   ├── FreeProviderContractTests.cs
│   │   │   │   ├── HistoricalDataProviderContractTests.cs
│   │   │   │   ├── IBApiVersionValidatorTests.cs
│   │   │   │   ├── IBBrokerageGatewayTests.cs
│   │   │   │   ├── IBDataServicesTests.cs
│   │   │   │   ├── IBHistoricalProviderContractTests.cs
│   │   │   │   ├── IBMarketDataClientContractTests.cs
│   │   │   │   ├── IBOrderSampleTests.cs
│   │   │   │   ├── IBRuntimeGuidanceTests.cs
│   │   │   │   ├── IBSimulationClientContractTests.cs
│   │   │   │   ├── IBSimulationClientTests.cs
│   │   │   │   ├── MarketDataClientContractTests.cs
│   │   │   │   ├── NasdaqDataLinkCorporateActionProviderTests.cs
│   │   │   │   ├── NasdaqDataLinkHistoricalDataProviderTests.cs
│   │   │   │   ├── NasdaqDataLinkSymbolSearchProviderTests.cs
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
│   │   │   │   ├── ProviderDataQualityValidatorTests.cs
│   │   │   │   ├── ProviderFactoryCredentialContextTests.cs
│   │   │   │   ├── ProviderMarketDataCapabilityTests.cs
│   │   │   │   ├── ProviderRateLimitTrackerTests.cs
│   │   │   │   ├── ProviderResilienceTests.cs
│   │   │   │   ├── ProviderTemplateFactoryCredentialTests.cs
│   │   │   │   ├── RobinhoodBrokerageGatewayTests.cs
│   │   │   │   ├── RobinhoodHistoricalDataProviderTests.cs
│   │   │   │   ├── RobinhoodMarketDataClientTests.cs
│   │   │   │   ├── RobinhoodReadOnlyBrokerageSyncAdapterTests.cs
│   │   │   │   ├── RobinhoodSymbolSearchProviderTests.cs
│   │   │   │   ├── StooqHistoricalDataProviderTests.cs
│   │   │   │   ├── StreamingFailoverServiceTests.cs
│   │   │   │   ├── SyntheticHistoricalProviderContractTests.cs
│   │   │   │   ├── SyntheticMarketDataProviderTests.cs
│   │   │   │   ├── SyntheticOptionsChainProviderTests.cs
│   │   │   │   ├── SyntheticProviderTestHarness.cs
│   │   │   │   ├── TemplateBrokerageGatewayTests.cs
│   │   │   │   ├── TiingoCorporateActionProviderTests.cs
│   │   │   │   ├── TiingoHistoricalDataProviderTests.cs
│   │   │   │   ├── TiingoSymbolSearchProviderTests.cs
│   │   │   │   ├── TradeStationPayloadMappersTests.cs
│   │   │   │   ├── TwelveDataCorporateActionProviderTests.cs
│   │   │   │   ├── TwelveDataHistoricalDataProviderTests.cs
│   │   │   │   ├── TwelveDataSymbolSearchProviderTests.cs
│   │   │   │   ├── WebSocketProviderBaseTests.cs
│   │   │   │   └── YahooFinanceHistoricalDataProviderTests.cs
│   │   │   ├── Resilience
│   │   │   │   ├── PollingProviderBaseTests.cs
│   │   │   │   ├── ProviderConnectionSupervisorTests.cs
│   │   │   │   ├── WebSocketConnectionManagerTests.cs
│   │   │   │   └── WebSocketResiliencePolicyTests.cs
│   │   │   └── Shared
│   │   │       ├── SymbolNormalizationTests.cs
│   │   │       └── TempDirectoryFixture.cs
│   │   ├── Instruments
│   │   │   └── Options
│   │   │       └── OptionsChainServiceTests.cs
│   │   ├── Integration
│   │   │   ├── EndpointTests
│   │   │   │   ├── AccountPortfolioEndpointTests.cs
│   │   │   │   ├── AdminEndpointPermissionTests.cs
│   │   │   │   ├── AuthEndpointTests.cs
│   │   │   │   ├── BackfillEndpointTests.cs
│   │   │   │   ├── CatalogEndpointTests.cs
│   │   │   │   ├── CheckpointEndpointTests.cs
│   │   │   │   ├── ConfigDirectLendingAuthorizationTests.cs
│   │   │   │   ├── ConfigEndpointTests.cs
│   │   │   │   ├── DirectLendingEndpointMutationTests.cs
│   │   │   │   ├── EndpointGuardTests.cs
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
│   │   │   │   ├── OperationalProblemDetailsEndpointTests.cs
│   │   │   │   ├── OptionsEndpointTests.cs
│   │   │   │   ├── PilotAcceptanceHarnessTests.cs
│   │   │   │   ├── ProviderConnectionHonestyEndpointTests.cs
│   │   │   │   ├── ProviderDataProjectionAuthorizationTests.cs
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
│   │   │   ├── AutomatedJournalPostingTargetTests.cs
│   │   │   ├── CapitalCallScheduleDraftBuilderTests.cs
│   │   │   ├── CarriedInterestClawbackCalculatorTests.cs
│   │   │   ├── DailyPortfolioPricingDeltaTests.cs
│   │   │   ├── DepreciationScheduleCalculatorTests.cs
│   │   │   ├── DimensionSignatureBackwardCompatibilityTests.cs
│   │   │   ├── EuropeanDistributionWaterfallTests.cs
│   │   │   ├── FinancialReportDocumentRendererTests.cs
│   │   │   ├── FixedAssetDepreciationDraftBuilderTests.cs
│   │   │   ├── FixedAssetDepreciationProjectorTests.cs
│   │   │   ├── FundAdministrationEventLogTests.cs
│   │   │   ├── FundEconomicsJournalFactoryTests.cs
│   │   │   ├── JournalTemplateTests.cs
│   │   │   ├── LedgerAccountIdentityTests.cs
│   │   │   ├── LedgerAccountTypeOrdinalContractTests.cs
│   │   │   ├── LedgerEntryCurrencyTests.cs
│   │   │   ├── LedgerIntegrationTests.cs
│   │   │   ├── LedgerJournalReversalTests.cs
│   │   │   ├── LedgerPartnersCapitalReconciliationTests.cs
│   │   │   ├── LedgerPeriodStatementsTests.cs
│   │   │   ├── LedgerReportDeterministicExportTests.cs
│   │   │   ├── LedgerReportPackTestData.cs
│   │   │   ├── LedgerReportRendererCompositionTests.cs
│   │   │   ├── LedgerScheduledExportFormatTests.cs
│   │   │   ├── LedgerTaxCharacterTests.cs
│   │   │   ├── LedgerTaxLotBasisAdjusterTests.cs
│   │   │   ├── LedgerTaxLotReliefWashSaleTests.cs
│   │   │   ├── LedgerWashSaleActivationTests.cs
│   │   │   ├── LotConsumptionTests.cs
│   │   │   ├── NavPerUnitAndEqualizationTests.cs
│   │   │   ├── PartnersCapitalAllocationBreakoutTests.cs
│   │   │   ├── PeriodCloseProjectorTests.cs
│   │   │   ├── PeriodReopenTests.cs
│   │   │   ├── PortfolioPricingRuleTests.cs
│   │   │   ├── PreferredReturnCalculatorTests.cs
│   │   │   ├── RecurringJournalScheduleTests.cs
│   │   │   ├── ShareClassUnitRegisterTests.cs
│   │   │   └── YearEndCloseTests.cs
│   │   ├── MoneyMarketFunds
│   │   │   └── MoneyMarketFundProjectionServiceTests.cs
│   │   ├── Options
│   │   │   └── OptionProjectionServiceTests.cs
│   │   ├── Performance
│   │   │   └── AllocationBudgetIntegrationTests.cs
│   │   ├── Platform
│   │   │   ├── ApiDocumentation
│   │   │   │   └── ApiDocumentationServiceTests.cs
│   │   │   ├── Coordination
│   │   │   │   ├── ClusterCoordinatorServiceTests.cs
│   │   │   │   ├── LeaseManagerTests.cs
│   │   │   │   └── SplitBrainDetectorTests.cs
│   │   │   ├── Diagnostics
│   │   │   │   ├── DiagnosticBundleServiceTests.cs
│   │   │   │   ├── ErrorRingBufferTests.cs
│   │   │   │   ├── PipelineDiagnosticsProjectionTests.cs
│   │   │   │   └── SystemHealthCheckerTests.cs
│   │   │   ├── Monitoring
│   │   │   │   ├── AlertDispatcherTests.cs
│   │   │   │   ├── BackpressureAlertServiceTests.cs
│   │   │   │   ├── CircuitBreakerStatusServiceTests.cs
│   │   │   │   ├── HealthCheckAggregatorTests.cs
│   │   │   │   └── SloDefinitionRegistryTests.cs
│   │   │   ├── Performance
│   │   │   │   └── CoLocationProfileActivatorTests.cs
│   │   │   ├── Runtime
│   │   │   │   └── StartupSummaryTests.cs
│   │   │   ├── Scheduling
│   │   │   │   └── TradingCalendarTests.cs
│   │   │   ├── Tracing
│   │   │   │   ├── DefaultEventMetricsTests.cs
│   │   │   │   └── TracedEventMetricsTests.cs
│   │   │   └── EventTraceContextTests.cs
│   │   ├── PortfolioRecords
│   │   │   └── FundAccounts
│   │   │       ├── FundAccountServiceContractTests.cs
│   │   │       ├── FundAccountServiceTests.cs
│   │   │       ├── InMemoryFundAccountServiceContractTests.cs
│   │   │       └── PostgresFundAccountServiceContractTests.cs
│   │   ├── Providers
│   │   │   ├── ProviderCapabilityDescriptorCatalogTests.cs
│   │   │   └── ProviderInstrumentCapabilityMatrixServiceTests.cs
│   │   ├── ProviderSdk
│   │   │   ├── AttributeCredentialResolverTests.cs
│   │   │   ├── CredentialValidatorTests.cs
│   │   │   ├── DataSourceAttributeTests.cs
│   │   │   ├── DataSourceRegistryTests.cs
│   │   │   ├── ExceptionTypeTests.cs
│   │   │   ├── OptionalProviderCapabilityContractsTests.cs
│   │   │   ├── PluginLoaderServiceTests.cs
│   │   │   └── ProviderModuleLoaderTests.cs
│   │   ├── Reconciliation
│   │   │   ├── Connectors
│   │   │   │   ├── AlpacaActivityStatementConnectorTests.cs
│   │   │   │   ├── Bai2StatementConnectorTests.cs
│   │   │   │   ├── Camt053StatementConnectorTests.cs
│   │   │   │   ├── CsvLineSplitterTests.cs
│   │   │   │   ├── CsvStatementConnectorTests.cs
│   │   │   │   ├── IbFlexStatementConnectorTests.cs
│   │   │   │   ├── OfxStatementConnectorTests.cs
│   │   │   │   ├── StatementColumnConfidenceScorerTests.cs
│   │   │   │   ├── StatementConnectorTestData.cs
│   │   │   │   ├── StatementImportServiceTests.cs
│   │   │   │   ├── StatementMappingProfileLoaderTests.cs
│   │   │   │   └── StatementMappingProfileStoreTests.cs
│   │   │   ├── Fixtures
│   │   │   │   ├── statement-clean-reconciles.csv
│   │   │   │   ├── statement-invalid-blockers.csv
│   │   │   │   └── statement-unresolved-breaks.csv
│   │   │   ├── BrokerCustodianMatchingPipelineTests.cs
│   │   │   ├── FileReconciliationFxRateProviderTests.cs
│   │   │   ├── FileStatementToleranceProfileProviderTests.cs
│   │   │   ├── IbFlexStatementServiceTests.cs
│   │   │   ├── ReconciliationCaseServiceTests.cs
│   │   │   ├── ReconciliationContractsTests.cs
│   │   │   ├── RetainedInternalReconciliationPopulationProviderTests.cs
│   │   │   ├── StatementBreakClassifierTests.cs
│   │   │   ├── StatementFixtureScenarioTests.cs
│   │   │   ├── StatementImportAndMatchingTests.cs
│   │   │   ├── StatementRunMatchingServiceTests.cs
│   │   │   └── StatementRunWorkflowServiceTests.cs
│   │   ├── ReferenceData
│   │   │   └── SecurityMaster
│   │   │       └── SecurityKindMappingTests.cs
│   │   ├── Reporting
│   │   │   ├── NavAttributionServiceTests.cs
│   │   │   ├── ReportGenerationServiceTests.cs
│   │   │   ├── ReportingGovernanceCanonicalValidationTests.cs
│   │   │   ├── ReportingOrchestrationServiceTests.cs
│   │   │   ├── ReportingSecureDistributionAuthorizationTests.cs
│   │   │   ├── ReportingSecureDistributionTests.cs
│   │   │   ├── ReportSnapshotDiffEngineTests.cs
│   │   │   └── ReportWriterGridEngineTests.cs
│   │   ├── Risk
│   │   │   ├── CompositeRiskValidatorTests.cs
│   │   │   ├── DrawdownCircuitBreakerTests.cs
│   │   │   ├── EnforcedRiskValidatorCompositionTests.cs
│   │   │   ├── OrderRateThrottleTests.cs
│   │   │   ├── PortfolioRiskRulesTests.cs
│   │   │   ├── PositionLimitRuleTests.cs
│   │   │   ├── RiskEscalationQueueServiceTests.cs
│   │   │   └── RiskIntegrationTests.cs
│   │   ├── Scripts
│   │   │   └── ProductionRecoveryScriptTests.cs
│   │   ├── SecurityMaster
│   │   │   ├── Workbench
│   │   │   │   ├── InMemorySecurityMasterRevisionStoreTests.cs
│   │   │   │   ├── LedgerBookAffectedResolverTests.cs
│   │   │   │   ├── LedgerPeriodLockReaderTests.cs
│   │   │   │   ├── PeriodAwareRestatementResolverTests.cs
│   │   │   │   ├── PublishedRevisionHandlerTests.cs
│   │   │   │   ├── ReportPackRestatementCandidateResolverTests.cs
│   │   │   │   ├── ReportPackSecurityLineIndexTests.cs
│   │   │   │   ├── ReportPeriodRangeTests.cs
│   │   │   │   ├── SecurityMasterConflictAuthorityPolicyTests.cs
│   │   │   │   └── SecurityMasterWorkbenchCommandServiceTests.cs
│   │   │   ├── CanonicalRegistryCoverageSourceTests.cs
│   │   │   ├── CorporateActionCommandServiceTests.cs
│   │   │   ├── CorporateActionEffectiveStateProjectorTests.cs
│   │   │   ├── CorporateActionGoldenLedgerTests.cs
│   │   │   ├── CorporateActionInboxStateTests.cs
│   │   │   ├── CorporateActionIngestOrchestratorTests.cs
│   │   │   ├── CorporateActionLedgerInvariants.cs
│   │   │   ├── CorporateActionLedgerKnownDefectTests.cs
│   │   │   ├── CorporateActionTaxonomyPropertyTests.cs
│   │   │   ├── CorporateActionTypeDescriptorCatalogTests.cs
│   │   │   ├── DataVendorEntitlementServiceTests.cs
│   │   │   ├── DayCountConventionsTests.cs
│   │   │   ├── FaceValueLotTests.cs
│   │   │   ├── NullOperatorOverridesStoreTests.cs
│   │   │   ├── PostgresOperatorOverridesStoreTests.cs
│   │   │   ├── PostgresSecurityMasterConflictServiceTests.cs
│   │   │   ├── PostgresSecurityMasterRevisionStoreTests.cs
│   │   │   ├── SecurityAssetClassCatalogTests.cs
│   │   │   ├── SecurityAssetProfileGovernanceServiceTests.cs
│   │   │   ├── SecurityAssetSpecificTermsUpcasterChainTests.cs
│   │   │   ├── SecurityAssetSpecificTermsUpcasterPipelineTests.cs
│   │   │   ├── SecurityAssetSpecificTermsUpcasterTests.cs
│   │   │   ├── SecurityAssetTermsSchemaTests.cs
│   │   │   ├── SecurityEnrichmentTests.cs
│   │   │   ├── SecurityIdentifierNormalizerTests.cs
│   │   │   ├── SecurityMasterAggregateRebuilderTests.cs
│   │   │   ├── SecurityMasterAmortizationLedgerBridgeTests.cs
│   │   │   ├── SecurityMasterAssetClassSupportTests.cs
│   │   │   ├── SecurityMasterConflictServiceTests.cs
│   │   │   ├── SecurityMasterConvertibleEquityAmendmentTests.cs
│   │   │   ├── SecurityMasterCorporateActionCommandServiceTests.cs
│   │   │   ├── SecurityMasterCostBasisAdjustmentServiceTests.cs
│   │   │   ├── SecurityMasterDatabaseFactAttribute.cs
│   │   │   ├── SecurityMasterDatabaseFixture.cs
│   │   │   ├── SecurityMasterDataQualityServiceCoverageTests.cs
│   │   │   ├── SecurityMasterDataQualityServiceTests.cs
│   │   │   ├── SecurityMasterDraftProposalServiceTests.cs
│   │   │   ├── SecurityMasterHistoricalSymbolTimelineResolverTests.cs
│   │   │   ├── SecurityMasterImportServiceTests.cs
│   │   │   ├── SecurityMasterLedgerBridgeAutoReverseTests.cs
│   │   │   ├── SecurityMasterLedgerBridgeTests.cs
│   │   │   ├── SecurityMasterMigrationRunnerTests.cs
│   │   │   ├── SecurityMasterOperationalReadinessServiceTests.cs
│   │   │   ├── SecurityMasterPostgresRoundTripTests.cs
│   │   │   ├── SecurityMasterPreferredEquityAmendmentTests.cs
│   │   │   ├── SecurityMasterPricingServiceTests.cs
│   │   │   ├── SecurityMasterProjectionCodecTests.cs
│   │   │   ├── SecurityMasterProjectionServiceSnapshotTests.cs
│   │   │   ├── SecurityMasterQueryServiceAsOfTests.cs
│   │   │   ├── SecurityMasterQueryServiceEquityTermsTests.cs
│   │   │   ├── SecurityMasterQueryServiceProfileSearchTests.cs
│   │   │   ├── SecurityMasterRebuildOrchestratorTests.cs
│   │   │   ├── SecurityMasterReferenceLookupTests.cs
│   │   │   ├── SecurityMasterSchemaVersionsTests.cs
│   │   │   ├── SecurityMasterServiceSnapshotTests.cs
│   │   │   ├── SecurityMasterSnapshotStoreTests.cs
│   │   │   ├── SecurityMasterTermScheduleCodecTests.cs
│   │   │   ├── SecurityMasterTickerChangeServiceTests.cs
│   │   │   ├── SecurityReferenceTaxonomyCatalogTests.cs
│   │   │   └── SecurityValidationServiceTests.cs
│   │   ├── Serialization
│   │   │   └── HighPerformanceJsonTests.cs
│   │   ├── Services
│   │   │   └── CashSyncOrchestrationServiceTests.cs
│   │   ├── Storage
│   │   │   ├── Backfill
│   │   │   │   └── BackfillStatusStoreTests.cs
│   │   │   ├── Etl
│   │   │   │   └── EtlJobDefinitionStoreTests.cs
│   │   │   ├── FundAccounts
│   │   │   │   ├── FundAccountDatabaseFactAttribute.cs
│   │   │   │   ├── FundAccountDatabaseFixture.cs
│   │   │   │   └── PostgresFundAccountStoreTests.cs
│   │   │   ├── FundStructure
│   │   │   │   └── PostgresFundStructureStoreTests.cs
│   │   │   ├── Integrations
│   │   │   │   └── FileProviderIntegrationManifestStoreTests.cs
│   │   │   ├── Maintenance
│   │   │   │   └── ScheduledArchiveMaintenanceServiceTests.cs
│   │   │   ├── Operations
│   │   │   │   └── FileOperationalCaseHistoryStoreTests.cs
│   │   │   ├── Reporting
│   │   │   │   ├── FileReportingReconciliationEvidenceStoreMigrationTests.cs
│   │   │   │   ├── ReportingArtifactCatalogAuditStoreTests.cs
│   │   │   │   ├── ReportingArtifactStoreTests.cs
│   │   │   │   ├── ReportingDistributionStoreTests.cs
│   │   │   │   ├── ReportingGovernanceRepositoryTests.cs
│   │   │   │   ├── ReportingReconciliationEvidenceStoreTests.cs
│   │   │   │   └── StatementReconciliationReportAuthorityStoreTests.cs
│   │   │   ├── AccountingConfigurationPostgresStoreTests.cs
│   │   │   ├── AdaptivePartitionPlacementPlannerTests.cs
│   │   │   ├── AnalysisExportServiceTests.cs
│   │   │   ├── AnalysisQualityReportCsvTests.cs
│   │   │   ├── AssetAccountingPostingEvidenceValidatorTests.cs
│   │   │   ├── AtomicFileWriterTests.cs
│   │   │   ├── AtomicTaxLotJournalStoreTests.cs
│   │   │   ├── AuditChainServiceTests.cs
│   │   │   ├── CanonicalSymbolRegistryTests.cs
│   │   │   ├── CompositeSinkTests.cs
│   │   │   ├── DataLineageServiceTests.cs
│   │   │   ├── DataQualityScoringServiceTests.cs
│   │   │   ├── DataReplacementCostEstimatorTests.cs
│   │   │   ├── DataValidatorTests.cs
│   │   │   ├── DirectLendingMigrationTests.cs
│   │   │   ├── DuckDbQueryServiceTests.cs
│   │   │   ├── DurableAutomatedJournalPosterTests.cs
│   │   │   ├── EventBufferTests.cs
│   │   │   ├── ExportValidatorTests.cs
│   │   │   ├── FileMaintenanceServiceTests.cs
│   │   │   ├── FilePermissionsServiceTests.cs
│   │   │   ├── FundAccountTenantColumnMigrationTests.cs
│   │   │   ├── FundScopedWriteTenantGateTests.cs
│   │   │   ├── FundScopeTenantColumnMigrationTests.cs
│   │   │   ├── GovernedLedgerPostingTargetTests.cs
│   │   │   ├── JsonFileIBDataResultStoreTests.cs
│   │   │   ├── JsonFileSnapshotStoreTests.cs
│   │   │   ├── JsonlAppendStreamTests.cs
│   │   │   ├── JsonlBatchWriteTests.cs
│   │   │   ├── JsonlReplayerTests.cs
│   │   │   ├── LedgerBookServiceTests.cs
│   │   │   ├── LedgerDatabaseFactAttribute.cs
│   │   │   ├── LedgerJournalStoreHydrationTests.cs
│   │   │   ├── LedgerJournalStoreTests.cs
│   │   │   ├── LedgerPostgresTestDatabase.cs
│   │   │   ├── LifecyclePolicyEngineTests.cs
│   │   │   ├── MaintenancePersistenceTests.cs
│   │   │   ├── MaintenanceSchedulerTests.cs
│   │   │   ├── MemoryMappedJsonlReaderTests.cs
│   │   │   ├── MeridianDatabaseEnvironmentTests.cs
│   │   │   ├── MetadataTagServiceTests.cs
│   │   │   ├── OperationsContinuityTenantColumnMigrationTests.cs
│   │   │   ├── ParquetConversionServiceTests.cs
│   │   │   ├── ParquetStorageSinkTests.cs
│   │   │   ├── PortableDataPackagerTests.cs
│   │   │   ├── PositionSnapshotStoreTests.cs
│   │   │   ├── PostgresMigrationRunnerValidationTests.cs
│   │   │   ├── PostgresReportingDeploymentProbeTests.cs
│   │   │   ├── QualityTrendStoreTests.cs
│   │   │   ├── QuotaEnforcementServiceTests.cs
│   │   │   ├── ReportingOperationalStoreTests.cs
│   │   │   ├── SourceRegistryPersistenceTests.cs
│   │   │   ├── StorageCatalogServiceTests.cs
│   │   │   ├── StorageChecksumServiceTests.cs
│   │   │   ├── StorageOptionsDefaultsTests.cs
│   │   │   ├── StorageProfilePresetsTests.cs
│   │   │   ├── StorageSearchServiceTests.cs
│   │   │   ├── StorageSinkRegistryTests.cs
│   │   │   ├── SymbolRegistryServiceTests.cs
│   │   │   ├── TenantLowerIndexMigrationTests.cs
│   │   │   ├── TenantReadPredicateTests.cs
│   │   │   ├── TierMigrationServiceTests.cs
│   │   │   ├── WriteAheadLogCorruptionModeTests.cs
│   │   │   ├── WriteAheadLogFuzzTests.cs
│   │   │   ├── WriteAheadLogTests.cs
│   │   │   └── XlsxWorkbookWriterTests.cs
│   │   ├── Strategies
│   │   │   ├── CoveredCall
│   │   │   │   ├── CoveredCallChainProviderAdapterTests.cs
│   │   │   │   ├── CoveredCallChainProviderFactoryConvertCallsTests.cs
│   │   │   │   └── CoveredCallRunProjectionTests.cs
│   │   │   ├── AggregatePortfolioServiceTests.cs
│   │   │   ├── CashFlowProjectionTests.cs
│   │   │   ├── GovernanceExceptionServiceTests.cs
│   │   │   ├── LedgerReadServiceTests.cs
│   │   │   ├── LiveStrategyCatalogFallbackTests.cs
│   │   │   ├── LiveTradingEngineTests.cs
│   │   │   ├── PortfolioReadServiceTests.cs
│   │   │   ├── PromotionServiceLiveGovernanceTests.cs
│   │   │   ├── PromotionServiceTests.cs
│   │   │   ├── PromotionWalkForwardGateTests.cs
│   │   │   ├── ReconciliationBreakQueueRepositoryTests.cs
│   │   │   ├── ReconciliationCaseWorkflowServiceTests.cs
│   │   │   ├── ReconciliationCaseWorkflowVocabularyTests.cs
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
│   │   │   ├── OpenFigiClientAmbiguityTests.cs
│   │   │   ├── OpenFigiClientTests.cs
│   │   │   └── SymbolSearchServiceTests.cs
│   │   ├── TestData
│   │   │   └── Golden
│   │   │       ├── statement-connectors
│   │   │       │   ├── alpaca-combined-snapshot.json
│   │   │       │   ├── bai2-sample.bai
│   │   │       │   ├── camt053-sample.xml
│   │   │       │   ├── csv-drifted-headers.csv
│   │   │       │   ├── csv-mixed-kinds.csv
│   │   │       │   ├── csv-quoted-bom.csv
│   │   │       │   ├── csv-semicolon.csv
│   │   │       │   ├── ib-flex-sample.xml
│   │   │       │   ├── ofx-102-bank.ofx
│   │   │       │   └── ofx-211-investment.ofx
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
│   │   │   ├── RecordingLogger.cs
│   │   │   ├── SignalingMarketEventPublisher.cs
│   │   │   ├── StubHttpMessageHandler.cs
│   │   │   └── TestMarketEventPublisher.cs
│   │   ├── Testing
│   │   │   └── TestArtifactDirectory.cs
│   │   ├── TestSupport
│   │   │   ├── ControllableReportingReleaseConsistencyGate.cs
│   │   │   └── ImmediateReportingReleaseConsistencyGate.cs
│   │   ├── Treasury
│   │   │   ├── MmfFamilyNormalizationTests.cs
│   │   │   ├── MmfLiquidityServiceTests.cs
│   │   │   ├── MmfRebuildTests.cs
│   │   │   └── MoneyMarketFundServiceTests.cs
│   │   ├── Ui
│   │   │   ├── Evidence
│   │   │   │   ├── EvidenceDocumentExtractionTests.cs
│   │   │   │   ├── EvidenceProofChainBuilderTests.cs
│   │   │   │   ├── ReconciliationEvidenceContributorTests.cs
│   │   │   │   └── SecurityMasterAndVaultEvidenceContributorTests.cs
│   │   │   ├── Streaming
│   │   │   │   ├── QuoteStreamBroadcasterTests.cs
│   │   │   │   ├── ReportRunStreamBroadcasterTests.cs
│   │   │   │   ├── StreamBroadcasterTests.cs
│   │   │   │   ├── StreamConnectionRegistryTests.cs
│   │   │   │   └── StreamTopicTests.cs
│   │   │   ├── AccountingConfigurationServiceTests.cs
│   │   │   ├── AccountingMigrationRunExecutionServiceTests.cs
│   │   │   ├── AccountingPositionSnapshotCaptureServiceTests.cs
│   │   │   ├── AccountingProductionReadinessOperationalHardeningTests.cs
│   │   │   ├── AccountingProjectionQueryServiceTests.cs
│   │   │   ├── AccountingReportPackageServiceTests.cs
│   │   │   ├── AccountingSystemIntegrationServiceTests.cs
│   │   │   ├── AggregatePortfolioExposureProviderTests.cs
│   │   │   ├── AlpacaBrokerageConnectionServiceTests.cs
│   │   │   ├── AlpacaCredentialEnvironmentCollection.cs
│   │   │   ├── ApiHostOptionsDeploymentModeTests.cs
│   │   │   ├── AuditTrailExplorerServiceTests.cs
│   │   │   ├── AutomatedJournalCapitalAccountReconciliationResolverTests.cs
│   │   │   ├── AutomatedJournalDraftIntakeServiceTests.cs
│   │   │   ├── AutomatedJournalEventProducerTests.cs
│   │   │   ├── AutomatedJournalScheduleTests.cs
│   │   │   ├── BackfillAuditEndpointsTests.cs
│   │   │   ├── BackfillExecutionContractProjectionTests.cs
│   │   │   ├── BankFeedTransportServiceTests.cs
│   │   │   ├── BondReferenceEndpointsTests.cs
│   │   │   ├── BrokerageConnectionEndpointsTests.cs
│   │   │   ├── BrokeragePortfolioSyncServiceTests.cs
│   │   │   ├── CapitalAccountWorkbenchServiceTests.cs
│   │   │   ├── CashOperationsOrchestratorServiceTests.cs
│   │   │   ├── CollateralExposureServiceTests.cs
│   │   │   ├── CookieCsrfProtectionTests.cs
│   │   │   ├── CredentialCompatibilityEndpointsTests.cs
│   │   │   ├── DailyValuationBatchLifecycleServiceTests.cs
│   │   │   ├── DailyValuationPositionServiceTests.cs
│   │   │   ├── DailyValuationScheduleIdentityTests.cs
│   │   │   ├── DataOperationsAssuranceServiceTests.cs
│   │   │   ├── DegradedModeEvaluationTests.cs
│   │   │   ├── DemoTenantProvisionerTests.cs
│   │   │   ├── DesktopLaunchTicketServiceTests.cs
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
│   │   │   ├── FileFundProfileTenancyRegistryTests.cs
│   │   │   ├── FirstRunEndpointsTests.cs
│   │   │   ├── FirstRunExperienceServiceTests.cs
│   │   │   ├── FundAccountEndpointAuthorizationTests.cs
│   │   │   ├── FundOpsCloseLaneScenarioTests.cs
│   │   │   ├── FundProfileScopeEndpointFilterTests.cs
│   │   │   ├── FundStructureEndpointAuthorizationTests.cs
│   │   │   ├── InvestmentAccountingTransactionLabServiceTests.cs
│   │   │   ├── LedgerAmountProvenanceServiceTests.cs
│   │   │   ├── LedgerReportingAuthoritativeSourceTests.cs
│   │   │   ├── LegacyReportingRouteRetirementEndpointTests.cs
│   │   │   ├── LiveTradingEngineHostRegistrationTests.cs
│   │   │   ├── OmsIntegrationServiceTests.cs
│   │   │   ├── OperationsContinuityReconciliationBridgeTests.cs
│   │   │   ├── OperatorApprovalFlowScenarioTests.cs
│   │   │   ├── OptionReferenceEndpointsRoundtripTests.cs
│   │   │   ├── PlaidWorkstationServiceTests.cs
│   │   │   ├── PortfolioLedgerWorkflowStatusServiceTests.cs
│   │   │   ├── PrivateCapitalFundEventCommandCenterServiceTests.cs
│   │   │   ├── ProductionStartupPolicySmokeTests.cs
│   │   │   ├── PromotionDecisionChainScenarioTests.cs
│   │   │   ├── ProviderConnectionDiagnosticsProjectionTests.cs
│   │   │   ├── ProviderConnectionEndpointsTests.cs
│   │   │   ├── ProviderDataProjectionEndpointsTests.cs
│   │   │   ├── ProviderEndpointProjectionTests.cs
│   │   │   ├── ProviderLedgerReconciliationServiceTests.cs
│   │   │   ├── ProviderReadinessEndpointTests.cs
│   │   │   ├── ProviderRoutingEndpointsTests.cs
│   │   │   ├── ReconciliationApiServiceTests.cs
│   │   │   ├── ReconciliationBreakQueueProjectionTests.cs
│   │   │   ├── ReferenceDataEndpointAuthorizationTests.cs
│   │   │   ├── RegistryFundProfileTenantGuardTests.cs
│   │   │   ├── ReportingArtifactVaultServiceTests.cs
│   │   │   ├── ReportingDeliveryReleaseGateTests.cs
│   │   │   ├── ReportingDeploymentReadinessPostgresIntegrationTests.cs
│   │   │   ├── ReportingDeploymentReadinessServiceTests.cs
│   │   │   ├── ReportingFileStoreLegacyCompatibilityTests.cs
│   │   │   ├── ReportingGovernanceEndpointTests.cs
│   │   │   ├── ReportingOperationalConcurrencyTests.cs
│   │   │   ├── ReportingPersistenceFailClosedTests.cs
│   │   │   ├── ReportingProductionCompositionReadinessTests.cs
│   │   │   ├── ReportingRunCertificationServiceTests.cs
│   │   │   ├── ReportingRunReadinessEndpointTests.cs
│   │   │   ├── ReportingRunReadinessServiceTests.cs
│   │   │   ├── ReportingRunStoreManifestHashTests.cs
│   │   │   ├── ReportingRunStreamEndpointTests.cs
│   │   │   ├── ReportingTenantIsolationTests.cs
│   │   │   ├── ReportPackValidationServiceTests.cs
│   │   │   ├── ReportPackWorkflowServiceTests.cs
│   │   │   ├── RiskEndpointsTests.cs
│   │   │   ├── RiskRuleRuntimeOrderRateStatusTests.cs
│   │   │   ├── SecureReportingDistributionEndpointTests.cs
│   │   │   ├── SecurityMasterConvertibleEquityEndpointsTests.cs
│   │   │   ├── SecurityMasterExceptionCaseworkServiceTests.cs
│   │   │   ├── SecurityMasterIngestStatusEndpointsTests.cs
│   │   │   ├── SecurityMasterInstrumentPassportTests.cs
│   │   │   ├── SecurityMasterOperatorOverrideDecisionEndpointsTests.cs
│   │   │   ├── SecurityMasterPreferredEquityEndpointsTests.cs
│   │   │   ├── SecurityMasterReconciliationSlaPolicyProviderTests.cs
│   │   │   ├── SecurityMasterValidationEndpointsTests.cs
│   │   │   ├── SecurityMasterWorkbenchEndpointsTests.cs
│   │   │   ├── SecurityMasterWorkbenchOptionsBindingTests.cs
│   │   │   ├── SpreadsheetFormulaGuardTests.cs
│   │   │   ├── StatementImportEvidenceBridgeTests.cs
│   │   │   ├── StatementReconciliationAuthorityCompositionTests.cs
│   │   │   ├── StatementReconciliationCaseworkHandoffTests.cs
│   │   │   ├── StatementReconciliationIntakeAuthorityTests.cs
│   │   │   ├── StatementReconciliationProductionAuthorityTests.cs
│   │   │   ├── StatementReconciliationReportFetchIngestionAuthorityTests.cs
│   │   │   ├── StatementReconciliationReportWorkflowServiceTests.cs
│   │   │   ├── StatementToDeliveryAuthorityTests.cs
│   │   │   ├── StrategyDesignerWorkstationEndpointsTests.cs
│   │   │   ├── StrategyLifecycleEndpointsTests.cs
│   │   │   ├── SupportedPostureStartupIntegrationTests.cs
│   │   │   ├── TradeFillLedgerPostingHostCompositionTests.cs
│   │   │   ├── TradingOperatorLiveOrderReadinessGateTests.cs
│   │   │   ├── TradingOperatorReadinessServiceTests.cs
│   │   │   ├── Wave2OperatorInboxAcceptanceTests.cs
│   │   │   ├── Wave2PaperTradingCockpitAcceptanceTests.cs
│   │   │   ├── WorkflowLibraryEndpointTests.cs
│   │   │   ├── WorkstationCollateralExposureEndpointsTests.cs
│   │   │   ├── WorkstationContractSnapshotTests.cs
│   │   │   ├── WorkstationDataUploadEndpointTests.cs
│   │   │   ├── WorkstationDataUploadWorkbookEndpointTests.cs
│   │   │   ├── WorkstationEndpointContractCompatibilityTests.cs
│   │   │   ├── WorkstationEndpoints.StatementAuthorityTests.cs
│   │   │   ├── WorkstationEndpointsTests.AccountingConfiguration.cs
│   │   │   ├── WorkstationEndpointsTests.cs
│   │   │   ├── WorkstationEndpointsTests.Extensibility.cs
│   │   │   ├── WorkstationEndpointsTests.IBResults.cs
│   │   │   ├── WorkstationEndpointsTests.Infrastructure.cs
│   │   │   ├── WorkstationEndpointsTests.JournalAutomation.cs
│   │   │   ├── WorkstationEndpointsTests.ProviderIntegrations.cs
│   │   │   ├── WorkstationEndpointsTests.Wave4.cs
│   │   │   ├── WorkstationFamilyOfficeEndpointsTests.cs
│   │   │   ├── WorkstationFinancialRecordExplorerEndpointTests.cs
│   │   │   ├── WorkstationMultiAssetCoverageEndpointsTests.cs
│   │   │   ├── WorkstationPortfolioAggregationScopeTests.cs
│   │   │   ├── WorkstationServiceCollectionExtensionsTests.cs
│   │   │   ├── WorkstationStatementCaseworkAuthorityEndpointTests.cs
│   │   │   ├── WorkstationStatementReconciliationEndpointTests.cs
│   │   │   ├── WorkstationStreamEndpointTests.cs
│   │   │   ├── WorkstationTenantContextTests.cs
│   │   │   └── WorkstationWorkflowSummaryFinancialOperationsTests.cs
│   │   ├── UiServices
│   │   │   └── ApiClientSessionTests.cs
│   │   ├── Workflow
│   │   │   ├── FundWorkflowCommandHandlerTests.cs
│   │   │   └── RunbookServicesTests.cs
│   │   ├── Wpf
│   │   │   ├── WpfAccountingFeatureModuleTests.cs
│   │   │   ├── WpfAssetOperationsReadModelTests.cs
│   │   │   └── WpfReportingWorkspaceShellTests.cs
│   │   ├── GlobalUsings.cs
│   │   ├── GlobalUsings.SecurityMasterConcerns.cs
│   │   ├── Meridian.Tests.csproj
│   │   ├── StatementReconciliationServiceTests.cs
│   │   └── TestCollections.cs
│   ├── Meridian.TestSupport
│   │   ├── Meridian.TestSupport.csproj
│   │   ├── PostgresTestContainerOptions.cs
│   │   ├── PostgresTestSchema.cs
│   │   └── PostgresTestServer.cs
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
│   │   │   ├── ApiClientSingletonCollection.cs
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
│   │   │   ├── SetupWizardServiceTests.cs
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
│   │   │   ├── Accounting
│   │   │   │   ├── AccountingFeatureModuleTests.cs
│   │   │   │   └── AccountingFeatureServiceRegistrationTests.cs
│   │   │   ├── Data
│   │   │   │   ├── Shell
│   │   │   │   │   └── DataWorkspaceShellViewModelTests.cs
│   │   │   │   ├── DataFeatureModuleTests.cs
│   │   │   │   └── DataFeatureServiceRegistrationTests.cs
│   │   │   ├── Home
│   │   │   │   └── HomeFeatureModuleTests.cs
│   │   │   ├── Portfolio
│   │   │   │   └── PortfolioFeatureModuleTests.cs
│   │   │   ├── Reporting
│   │   │   │   ├── ReportingFeatureModuleTests.cs
│   │   │   │   ├── ReportingGovernanceWorkbenchViewModelTests.cs
│   │   │   │   └── ReportingWorkspaceGovernanceSurfaceTests.cs
│   │   │   ├── Settings
│   │   │   │   ├── Shell
│   │   │   │   │   └── SettingsWorkspaceShellViewModelTests.cs
│   │   │   │   ├── SettingsFeatureModuleTests.cs
│   │   │   │   └── SettingsFeatureServiceRegistrationTests.cs
│   │   │   ├── Strategy
│   │   │   │   ├── StrategyFeatureModuleTests.cs
│   │   │   │   └── StrategyFeatureServiceRegistrationTests.cs
│   │   │   ├── Trading
│   │   │   │   ├── TradingFeatureModuleTests.cs
│   │   │   │   └── TradingFeatureServiceRegistrationTests.cs
│   │   │   ├── DesktopFeatureModuleTestAssertions.cs
│   │   │   ├── FeatureCapabilityGateTests.cs
│   │   │   └── ServiceCollectionRegistrationAssertions.cs
│   │   ├── Models
│   │   │   ├── PaneLayoutTests.cs
│   │   │   ├── ShellNavigationCatalogTests.cs
│   │   │   └── WorkspaceShellChromeContributionTests.cs
│   │   ├── Services
│   │   │   ├── AdminMaintenanceServiceTests.cs
│   │   │   ├── AppLifecycleDataRootTests.cs
│   │   │   ├── AppServiceRegistrationTests.cs
│   │   │   ├── BackendServiceManagerTests.cs
│   │   │   ├── BackfillPresentationServiceTests.cs
│   │   │   ├── BackgroundTaskSchedulerServiceTests.cs
│   │   │   ├── ConfigServiceTests.cs
│   │   │   ├── ConnectionServiceTests.cs
│   │   │   ├── CredentialServiceTests.cs
│   │   │   ├── DataWorkspacePresentationBuilderTests.cs
│   │   │   ├── DesktopAuthenticationSessionTests.cs
│   │   │   ├── DesktopConfigurationRecoveryServiceTests.cs
│   │   │   ├── ExportPresetServiceTests.cs
│   │   │   ├── FirstRunServiceTests.cs
│   │   │   ├── FundLedgerReadServiceTests.cs
│   │   │   ├── FundReconciliationWorkbenchServiceTests.cs
│   │   │   ├── InfoBarServiceTests.cs
│   │   │   ├── KeyboardShortcutServiceTests.cs
│   │   │   ├── MessagingServiceTests.cs
│   │   │   ├── ModelRoutingPolicyValidatorTests.cs
│   │   │   ├── NavigationServiceTests.cs
│   │   │   ├── NotificationServiceTests.cs
│   │   │   ├── OfflineTrackingPersistenceServiceTests.cs
│   │   │   ├── OperationsContinuityDtoContractTests.cs
│   │   │   ├── PendingOperationsQueuePersistenceTests.cs
│   │   │   ├── PendingOperationsQueueServiceTests.cs
│   │   │   ├── QuantScriptExecutionHistoryServiceTests.cs
│   │   │   ├── QuantScriptTemplateCatalogServiceTests.cs
│   │   │   ├── RetentionAssuranceServiceTests.cs
│   │   │   ├── RunMatServiceTests.cs
│   │   │   ├── SetupWizardStateServiceTests.cs
│   │   │   ├── SingleInstanceServiceTests.cs
│   │   │   ├── StatusServiceTests.cs
│   │   │   ├── StorageServiceTests.cs
│   │   │   ├── StrategyBriefingWorkspaceServiceTests.cs
│   │   │   ├── StrategyRunWorkspaceServiceTests.cs
│   │   │   ├── TooltipServiceTests.cs
│   │   │   ├── ViewModelViewResolverTests.cs
│   │   │   ├── WatchlistServiceTests.cs
│   │   │   ├── WorkspaceLayoutManagerTests.cs
│   │   │   ├── WorkspaceServiceTests.cs
│   │   │   ├── WorkspaceShellContextServiceTests.cs
│   │   │   ├── WorkspaceStateTokenTests.cs
│   │   │   ├── WorkstationOperatingContextServiceTests.cs
│   │   │   ├── WorkstationReconciliationApiClientTests.cs
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
│   │   │   ├── FakeWorkstationStrategyBriefingApiClient.cs
│   │   │   ├── MainPageUiAutomationFacade.cs
│   │   │   ├── NavigationHostInspector.cs
│   │   │   ├── RunMatUiAutomationFacade.cs
│   │   │   ├── RunMatUiAutomationFacadeTests.cs
│   │   │   ├── StrategyRunWorkspaceTestData.cs
│   │   │   └── WpfTestThread.cs
│   │   ├── ViewModels
│   │   │   ├── AccountingCloseViewModelTests.cs
│   │   │   ├── AccountingConfigureViewModelTests.cs
│   │   │   ├── AccountPortfolioViewModelTests.cs
│   │   │   ├── ActivityLogViewModelTests.cs
│   │   │   ├── AddProviderWizardViewModelTests.cs
│   │   │   ├── AdminMaintenanceViewModelTests.cs
│   │   │   ├── AdvancedAnalyticsViewModelTests.cs
│   │   │   ├── AggregatePortfolioViewModelTests.cs
│   │   │   ├── AnalysisExportViewModelTests.cs
│   │   │   ├── AnalysisExportWizardViewModelTests.cs
│   │   │   ├── BackfillViewModelTests.cs
│   │   │   ├── BacktestViewModelTests.cs
│   │   │   ├── BatchBacktestViewModelTests.cs
│   │   │   ├── CashFlowViewModelTests.cs
│   │   │   ├── ChartingPageViewModelTests.cs
│   │   │   ├── ClusterStatusViewModelTests.cs
│   │   │   ├── CollectionSessionViewModelTests.cs
│   │   │   ├── DataBrowserViewModelTests.cs
│   │   │   ├── DataExportViewModelTests.cs
│   │   │   ├── DataQualityViewModelCharacterizationTests.cs
│   │   │   ├── DataSamplingViewModelTests.cs
│   │   │   ├── DataSourcesViewModelTests.cs
│   │   │   ├── DirectLendingViewModelTests.cs
│   │   │   ├── ExportPresetsViewModelTests.cs
│   │   │   ├── FinancialRecordExplorerViewModelTests.cs
│   │   │   ├── FundAccountsViewModelTests.cs
│   │   │   ├── FundLedgerViewModelTests.cs
│   │   │   ├── FundStructureSetupViewModelTests.cs
│   │   │   ├── HomeWorkspaceViewModelTests.cs
│   │   │   ├── LifecycleControlViewModelTests.cs
│   │   │   ├── LiveDataViewerViewModelTests.cs
│   │   │   ├── MainShellViewModelTests.cs
│   │   │   ├── MessagingHubViewModelTests.cs
│   │   │   ├── NotificationCenterViewModelTests.cs
│   │   │   ├── OrderBookHeatmapViewModelTests.cs
│   │   │   ├── OrderBookViewModelTests.cs
│   │   │   ├── PageActivationLifetimeContractTests.cs
│   │   │   ├── PortfolioImportViewModelTests.cs
│   │   │   ├── PositionBlotterViewModelTests.cs
│   │   │   ├── ProviderAccountingViewModelTests.cs
│   │   │   ├── ProviderDataProjectionViewModelTests.cs
│   │   │   ├── ProviderHealthViewModelTests.cs
│   │   │   ├── ProviderViewModelTests.cs
│   │   │   ├── QuantScriptViewModelTests.cs
│   │   │   ├── RetentionAssuranceViewModelTests.cs
│   │   │   ├── RunMatViewModelTests.cs
│   │   │   ├── RunRiskViewModelTests.cs
│   │   │   ├── ScheduleManagerViewModelTests.cs
│   │   │   ├── SecurityMasterEditViewModelTests.cs
│   │   │   ├── SecurityMasterViewModelTests.cs
│   │   │   ├── SecurityPassportEditorViewModelTests.cs
│   │   │   ├── ServiceManagerViewModelTests.cs
│   │   │   ├── SettingsViewModelAssetProfileTests.cs
│   │   │   ├── SettingsViewModelOperationsControlTests.cs
│   │   │   ├── SetupWizardViewModelTests.cs
│   │   │   ├── ShellPresentationViewModelTests.cs
│   │   │   ├── StartupWindowViewModelTests.cs
│   │   │   ├── StatusBarViewModelTests.cs
│   │   │   ├── StorageOptimizationViewModelTests.cs
│   │   │   ├── StorageViewModelTests.cs
│   │   │   ├── StrategyRunBrowserViewModelTests.cs
│   │   │   ├── StrategyRunDetailViewModelTests.cs
│   │   │   ├── StrategyRunLedgerViewModelTests.cs
│   │   │   ├── StrategyRunPortfolioViewModelTests.cs
│   │   │   ├── StrategyWorkspaceShellViewModelTests.cs
│   │   │   ├── SymbolMappingViewModelTests.cs
│   │   │   ├── SymbolsPageViewModelTests.cs
│   │   │   ├── SymbolStorageViewModelTests.cs
│   │   │   ├── SystemHealthViewModelTests.cs
│   │   │   ├── TickerStripViewModelTests.cs
│   │   │   ├── TimeSeriesAlignmentViewModelTests.cs
│   │   │   ├── TradingHoursViewModelTests.cs
│   │   │   ├── TradingWorkspaceShellViewModelTests.cs
│   │   │   ├── WatchlistViewModelTests.cs
│   │   │   ├── Wave2OperatorInboxAcceptanceTests.cs
│   │   │   ├── WelcomePageViewModelTests.cs
│   │   │   ├── WorkflowLibraryViewModelTests.cs
│   │   │   ├── WorkspaceAttentionRibbonViewModelTests.cs
│   │   │   ├── WorkspaceCockpitShellViewModelTests.cs
│   │   │   └── WorkspacePageViewModelTests.cs
│   │   ├── Views
│   │   │   ├── AccountingWorkspaceShellPageTests.cs
│   │   │   ├── AccountingWorkspaceShellSmokeTests.cs
│   │   │   ├── ApplicationPrimitiveControlsTests.cs
│   │   │   ├── DashboardPageSmokeTests.cs
│   │   │   ├── DataQualityPageSmokeTests.cs
│   │   │   ├── DataWorkspaceShellSmokeTests.cs
│   │   │   ├── DesktopWorkflowScriptTests.cs
│   │   │   ├── FullNavigationSweepTests.cs
│   │   │   ├── FundProfileSelectionPageSmokeTests.cs
│   │   │   ├── HelpPageSmokeTests.cs
│   │   │   ├── LifecycleControlPageSmokeTests.cs
│   │   │   ├── MainPageSmokeTests.cs
│   │   │   ├── MainPageUiWorkflowTests.cs
│   │   │   ├── NavigationPageSmokeTests.cs
│   │   │   ├── PageLifecycleCleanupTests.cs
│   │   │   ├── PlotRenderBehaviorTests.cs
│   │   │   ├── ProviderPageSimplificationTests.cs
│   │   │   ├── QuantScriptPageTests.cs
│   │   │   ├── RunMatUiSmokeTests.cs
│   │   │   ├── RunMatWorkflowSmokeTests.cs
│   │   │   ├── SecretInputControlTests.cs
│   │   │   ├── SplitPaneHostControlTests.cs
│   │   │   ├── StartupWindowSmokeTests.cs
│   │   │   ├── StrategyWorkspaceShellPageTests.cs
│   │   │   ├── StrategyWorkspaceShellSmokeTests.cs
│   │   │   ├── StrategyWorkspaceShellWorkflowTests.cs
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
│   │   │   ├── EvidenceVaultPresentationModelsTests.cs
│   │   │   ├── TableViewModelTests.cs
│   │   │   └── WorkstationPresentationModelsTests.cs
│   │   ├── GlobalUsings.cs
│   │   ├── GlobalUsings.SecurityMasterConcerns.cs
│   │   ├── Meridian.Wpf.Tests.csproj
│   │   └── TestAssemblyConfiguration.cs
│   ├── scripts
│   │   ├── fixtures
│   │   │   └── roadmap
│   │   │       ├── ambiguous-input.yaml
│   │   │       └── unordered-mixed-unicode.yaml
│   │   ├── README.md
│   │   ├── setup-verification.sh
│   │   ├── test_ai_setup_dotnet_channel.py
│   │   ├── test_archive_code_tombstones.py
│   │   ├── test_artifact_retention_module.py
│   │   ├── test_buildctl_artifact_retention.py
│   │   ├── test_buildctl_validation_runner.py
│   │   ├── test_central_package_versions.py
│   │   ├── test_check_apiclient_callers.py
│   │   ├── test_check_codex_memory.py
│   │   ├── test_check_codex_skills.py
│   │   ├── test_check_contract_compatibility_gate.py
│   │   ├── test_check_dashboard_type_barrel.py
│   │   ├── test_check_program_state_consistency.py
│   │   ├── test_check_status_delivery_claims.py
│   │   ├── test_check_test_skip_register.py
│   │   ├── test_check_workflow_docs_parity.py
│   │   ├── test_ci_summary.py
│   │   ├── test_ci_workflow_contract.py
│   │   ├── test_cleanup_generated_script.py
│   │   ├── test_compare_run_contract.py
│   │   ├── test_dashboard_package_lock.py
│   │   ├── test_desktop_msix_packaging.py
│   │   ├── test_desktop_screen_blueprint_checklist.py
│   │   ├── test_direct_lending_outbox_claim_sql.py
│   │   ├── test_doc_render_determinism.py
│   │   ├── test_documentation_workflow.py
│   │   ├── test_export_project_artifact_workflow.py
│   │   ├── test_generate_contract_review_packet.py
│   │   ├── test_generate_dependency_graph.py
│   │   ├── test_generate_desktop_user_manual.py
│   │   ├── test_generate_dk1_pilot_parity_packet.py
│   │   ├── test_generate_program_state_summary.py
│   │   ├── test_generate_ui_api_routes_ts.py
│   │   ├── test_generate_workspace_catalog_ts.py
│   │   ├── test_golden_path_validation_workflow.py
│   │   ├── test_lane_manifest.py
│   │   ├── test_live_execution_controls_route_consistency.py
│   │   ├── test_maintenance_full_workflow.py
│   │   ├── test_meridian_ci_workflow.py
│   │   ├── test_meridian_code_review_run_eval.py
│   │   ├── test_mixed_credit_status_set.py
│   │   ├── test_prepare_dk1_operator_signoff.py
│   │   ├── test_production_certification_workflow.py
│   │   ├── test_project_target_framework_alignment.py
│   │   ├── test_python_package_conda_dependencies.py
│   │   ├── test_refresh_screenshots_workflow.py
│   │   ├── test_release_evidence_manifest.py
│   │   ├── test_release_evidence_workflows.py
│   │   ├── test_render_roadmap_diagrams.py
│   │   ├── test_roadmap_source_docs.py
│   │   ├── test_roadmap_validator_compatibility.py
│   │   ├── test_robinhood_options_smoke.py
│   │   ├── test_route_maintenance_classification.py
│   │   ├── test_run_dotnet_ci_tests.py
│   │   ├── test_run_provider_validation_evidence_bundle.py
│   │   ├── test_run_script_tests.py
│   │   ├── test_schema_control_catalog.py
│   │   ├── test_schema_control_cli.py
│   │   ├── test_schema_control_contracts.py
│   │   ├── test_schema_control_dependencies.py
│   │   ├── test_schema_control_diffing.py
│   │   ├── test_schema_control_migrations.py
│   │   ├── test_schema_control_policies.py
│   │   ├── test_schema_control_render.py
│   │   ├── test_schema_control_workflow.py
│   │   ├── test_screenshot_diff_report.py
│   │   ├── test_screenshot_workflow_plan.py
│   │   ├── test_shared_build_retention.py
│   │   ├── test_shared_checkpoint.py
│   │   ├── test_summarize_desktop_workflow_bundle.py
│   │   ├── test_targeted_test_dispatcher.py
│   │   ├── test_targeted_test_workflow.py
│   │   ├── test_validate_npm_audit.py
│   │   ├── test_validate_observability_contract.py
│   │   ├── test_validate_screenshot_captures.py
│   │   ├── test_validate_source_readmes.py
│   │   ├── test_validate_test_results.py
│   │   ├── test_validate_tooling_metadata.py
│   │   ├── test_validate_workstation_cockpit_acceptance_matrix.py
│   │   ├── test_web_workstation_installer.py
│   │   ├── test_windows_desktop_build_workflow.py
│   │   ├── test_wpf_msix_install_guidance.py
│   │   └── test_wpf_msix_manifest.py
│   ├── Shared
│   │   └── CorporateActions
│   │       ├── GoldenCorporateActionScenario.cs
│   │       └── GoldenCorporateActionScenarioLoader.cs
│   ├── coverlet.runsettings
│   ├── Directory.Build.props
│   ├── setup-script-tests.md
│   └── xunit.runner.json
├── tools
│   ├── actionlint
│   │   ├── docs
│   │   │   ├── api.md
│   │   │   ├── checks.md
│   │   │   ├── config.md
│   │   │   ├── install.md
│   │   │   ├── README.md
│   │   │   ├── reference.md
│   │   │   └── usage.md
│   │   ├── man
│   │   │   └── actionlint.1
│   │   ├── actionlint.exe
│   │   ├── LICENSE.txt
│   │   └── README.md
│   ├── codex
│   │   ├── _codex-scan-lib.ps1
│   │   ├── architecture-scan.ps1
│   │   ├── component-inventory.ps1
│   │   ├── desktop-workspace-generator.ps1
│   │   ├── mvvm-compliance-check.ps1
│   │   ├── refactor-plan-generator.ps1
│   │   ├── resource-review.ps1
│   │   ├── run-codex-quality-suite.ps1
│   │   └── shared-pattern-suggest.ps1
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
│   ├── schema_control
│   │   ├── __init__.py
│   │   ├── __main__.py
│   │   ├── catalog.py
│   │   ├── cli.py
│   │   ├── common.py
│   │   ├── contracts.py
│   │   ├── dependencies.py
│   │   ├── diffing.py
│   │   ├── migrations.py
│   │   ├── policies.py
│   │   ├── README.md
│   │   ├── render.py
│   │   └── requirements.txt
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
├── .editorconfig
├── .flake8
├── .gitattributes
├── .gitignore
├── .gitleaks.toml
├── .gitleaksignore
├── .globalconfig
├── .markdownlint.json
├── .rgignore
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
├── package.json
└── README.md
```
