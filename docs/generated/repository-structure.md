Warning: truncated output (original token count: 147851)
Total output lines: 10680

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
│   │   ├── ai-navigation-refresh.yml
│   │   ├── branch-cleanup.yml
│   │   ├── ci.yml
│   │   ├── codeql.yml
│   │   ├── copilot-setup-steps.yml
│   │   ├── demo-smoke.yml
│   │   ├── desktop-evaluation-prerelease.yml
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
│   │   ├── contracts
│   │   │   └── type-parity-registry.json
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
│       │   ├── check-action-origin-derivation.py
│       │   ├── check-apiclient-callers.py
│       │   ├── check-contract-type-parity.py
│       │   ├── check-dashboard-type-barrel.py
│       │   ├── check-duplicate-helpers.py
│       │   ├── check-endpoint-cancellation.py
│       │   ├── check-file-size.py
│       │   ├── check-inline-sha256.py
│       │   ├── check-lane-manifest.py
│       │   ├── check-ledger-book-scope.py
│       │   ├── check-ledger-dimension-coverage.py
│       │   ├── check-posture-env-serialization.py
│       │   ├── check-sample-config-datasources.py
│       │   ├── check-store-concurrency-posture.py
│       │   ├── check-test-skip-register.py
│       │   ├── check-warning-suppressions.py
│       │   ├── check-workflow-hygiene.py
│       │   ├── dispatch-targeted-test.py
│       │   ├── duplicate-helper-baseline.json
│       │   ├── generate-release-evidence-manifest.py
│       │   ├── inline-sha256-baseline.json
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
│       │   │   ├── test_run_docs_automation_pilot_optin.py
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
│       │   ├── generate-ui-route-wiring-report.py
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
│       │   ├── requirements.txt
│       │   ├── rules-engine.py
│       │   ├── run-docs-automation.py
│       │   ├── scan-source-todos.py
│       │   ├── scan-todos.py
│       │   ├── sync-readme-badges.py
│       │   ├── sync-source-readmes.py
│       │   ├── test-scripts.py
│       │   ├── update-claude-md.py
│       │   ├── validate-agent-definitions.py
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
│       │   ├── smoke-web-workstation-install.ps1
│       │   └── windows-sdk-tools.ps1
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
│       │   ├── test_ai_repo_updater_blocking_async.py
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
│   │   ├── 022-canonical-asset-class-homes.md
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
│   │   ├── security-master-extensibility-review.md
│   │   ├── security-master-identifier-conflict-detection.md
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
│   │   ├── god-file-burn-down-plan.md
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
│   │   ├── brokerage-account-snapshot.md
│   │   ├── fund-event.md
│   │   ├── operational-evidence-graph.md
│   │   ├── README.md
│   │   └── security.md
│   ├── engineering
│   │   ├── blueprints
│   │   │   ├── financing-liabilities-depreciation-blueprint.md
│   │   │   ├── README.md
│   │   │   ├── risk-engine-severity-and-decision-journal-blueprint.md
│   │   │   └── w10-mark-001-fail-closed-marks.md
│   │   ├── dead-code-inventory.md
│   │   ├── docs-regeneration-automation-design.md
│   │   ├── free-development-tools.md
│   │   ├── live-trading-engine.md
│   │   ├── practical-csharp-wpf-financial-markets.md
│   │   ├── production-certification-evidence-chain.md
│   │   ├── production-readiness-audit-2026-07-27.md
│   │   ├── README.md
│   │   ├── security-master-architecture-audit-2026-08-13.md
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
│   │   ├── ledger-currency-backfill.md
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
│   │   ├── adversarial-program-review-2026-08-18.md
│   │   ├── adversarial-program-review-2026-08-24.md
│   │   ├── adversarial-program-review-2026-08-25.md
│   │   ├── adversarial-program-review-2026-08.md
│   │   ├── adversarial-review-2026-08-remediation-plan.md
│   │   ├── data-provider-accounting-brainstorm-2026-07.md
│   │   ├── deferred-expansion-boundaries.md
│   │   ├── excel-onboarding-workbook-brainstorm-2026-07.md
│   │   ├── functionality-deepening-brainstorm-2026-07.md
│   │   ├── high-value-code-brainstorm-2026-07.md
│   │   ├── implementation-todo-list.md
│   │   ├── meridian-design-document.md
│   │   ├── portfolio-cash-ladder-blueprint-2026-07.md
│   │   ├── product-roadmap-priorities-2026-07.md
│   │   ├── production-readiness-backlog-2026-08.md
│   │   ├── README.md
│   │   ├── w10-depth-slate-2026-07.md
│   │   ├── w9-close-out-delivery-plan-2026-08.md
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
│   │   ├── api-conflict-contract.md
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
│   │   │   ├── wpf-backtest.p…97851 tokens truncated…   │   │   │   ├── ProviderIntegrationSyncOrchestrationServiceTests.cs
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
│   │   │   │   ├── DedupWalOrderingTests.cs
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
│   │   │   │   ├── DailySummaryWebhookSchedulingTests.cs
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
│   │   │   ├── AssetObligationProjectionServiceTests.cs
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
│   │   │   ├── Integrity
│   │   │   │   └── Sha256DigestTests.cs
│   │   │   ├── Ledger
│   │   │   │   └── LedgerDimensionTagsTests.cs
│   │   │   ├── Operations
│   │   │   │   └── OperationsOriginGuardTests.cs
│   │   │   ├── Text
│   │   │   │   └── TextPrimitivesTests.cs
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
│   │   │   │   ├── ConfigTemplateGeneratorTests.cs
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
│   │   │   │   ├── ConnectionHealthMonitorLifecycleTests.cs
│   │   │   │   ├── ConnectionStatusWebhookTests.cs
│   │   │   │   ├── DataLossAccountingTests.cs
│   │   │   │   ├── PriceContinuityCheckerTests.cs
│   │   │   │   ├── ProviderDegradationCalibrationTests.cs
│   │   │   │   ├── ProviderDegradationScorerTests.cs
│   │   │   │   ├── ProviderLatencyServiceTests.cs
│   │   │   │   ├── SchemaValidationServiceTests.cs
│   │   │   │   ├── SpreadMonitorTests.cs
│   │   │   │   ├── TickSizeValidatorTests.cs
│   │   │   │   └── TimestampMonotonicityCheckerTests.cs
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
│   │   │   ├── DemoWorkspaceSmokeTests.cs
│   │   │   └── DemoWorkstationAssetTreeTests.cs
│   │   ├── Deposits
│   │   │   └── DepositProjectionServiceTests.cs
│   │   ├── Derivatives
│   │   │   └── SwapProjectionServiceTests.cs
│   │   ├── Domain
│   │   │   ├── Collectors
│   │   │   │   ├── CollectorSourceProvenanceTests.cs
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
│   │   │   ├── AlpacaStreamedFillLoopTests.cs
│   │   │   ├── BrokerageExecutionReconciliationServiceTests.cs
│   │   │   ├── BrokerageGatewayAdapterTests.cs
│   │   │   ├── BrokerageOrderPlacementGateTests.cs
│   │   │   ├── BrokerageValidationEvaluatorTests.cs
│   │   │   ├── DurableFillDeliveryBoundaryTests.cs
│   │   │   ├── ExecutionAuditTrailServiceTests.cs
│   │   │   ├── ExecutionOrderMetadataPolicyTests.cs
│   │   │   ├── FixedIncomeFillBookingTests.cs
│   │   │   ├── HostedBrokerageGatewayRegistrationTests.cs
│   │   │   ├── KillSwitchBrokerTruthTests.cs
│   │   │   ├── KillSwitchCloseOnlyTests.cs
│   │   │   ├── LiveMarketDataCacheSnapshotTests.cs
│   │   │   ├── LogSanitizerTests.cs
│   │   │   ├── MultiAccountPaperTradingPortfolioTests.cs
│   │   │   ├── OmsGovernedBrokerageOrderGatewayTests.cs
│   │   │   ├── OrderManagementSystemDurableHandoffOrderingTests.cs
│   │   │   ├── OrderManagementSystemGovernanceTests.cs
│   │   │   ├── OrderManagementSystemReportStreamTests.cs
│   │   │   ├── OrderManagementSystemTests.cs
│   │   │   ├── PaperExecutionGatewayLotSizeTests.cs
│   │   │   ├── PaperFillEnvelopeRegressionTests.cs
│   │   │   ├── PaperGatewayLiveFeedPricingTests.cs
│   │   │   ├── PaperOrderMatchingPolicyTests.cs
│   │   │   ├── PaperSessionPersistenceServiceTests.cs
│   │   │   ├── PaperSessionRecoveryConcurrencyTests.cs
│   │   │   ├── PaperTradingCostModelTests.cs
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
│   │   │   │   ├── AccountingPolicyClockTests.cs
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
│   │   │   │   ├── LocalFileSourceReaderPostProcessingTests.cs
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
│   │   │   │   ├── AlpacaTradeUpdatesClientTests.cs
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
│   │   │   │   ├── PolygonSequenceIntegrityTests.cs
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
│   │   │   │   ├── CoveredCallEndpointAuthorizationTests.cs
│   │   │   │   ├── DirectLendingEndpointMutationTests.cs
│   │   │   │   ├── EndpointAuthorizationCoverageTests.cs
│   │   │   │   ├── EndpointAuthorizationDeclarationTests.cs
│   │   │   │   ├── EndpointGuardTests.cs
│   │   │   │   ├── EndpointIntegrationTestBase.cs
│   │   │   │   ├── EndpointMetadataTests.cs
│   │   │   │   ├── EndpointReadDeclarationTests.cs
│   │   │   │   ├── EndpointTestCollection.cs
│   │   │   │   ├── EndpointTestFixture.cs
│   │   │   │   ├── EndpointTestFixtureProviderCatalogLifetimeTests.cs
│   │   │   │   ├── EnvironmentDesignerEndpointTests.cs
│   │   │   │   ├── FailoverEndpointTests.cs
│   │   │   │   ├── FundStructureEndpointTestFixture.cs
│   │   │   │   ├── FundStructureEndpointTests.cs
│   │   │   │   ├── HealthEndpointTests.cs
│   │   │   │   ├── HistoricalEndpointTests.cs
│   │   │   │   ├── IBEndpointTests.cs
│   │   │   │   ├── InitialAccountBootstrapEndpointTests.cs
│   │   │   │   ├── LeanEndpointTests.cs
│   │   │   │   ├── LiveDataEndpointTests.cs
│   │   │   │   ├── LoginSessionMiddlewarePrincipalTests.cs
│   │   │   │   ├── MaintenanceEndpointTests.cs
│   │   │   │   ├── MutationAuthorizationGuardMiddlewareTests.cs
│   │   │   │   ├── NegativePathEndpointTests.cs
│   │   │   │   ├── NonSessionPrincipalAuthorizationTests.cs
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
│   │   │   │   ├── TrustedActionOriginTests.cs
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
│   │   │   ├── FundEconomicsGoldenWorkedExampleTests.cs
│   │   │   ├── FundEconomicsJournalFactoryTests.cs
│   │   │   ├── JournalTemplateTests.cs
│   │   │   ├── LedgerAccountIdentityTests.cs
│   │   │   ├── LedgerAccountTypeOrdinalContractTests.cs
│   │   │   ├── LedgerEntryCurrencyTests.cs
│   │   │   ├── LedgerImmutabilityTests.cs
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
│   │   │   ├── PartnersCapitalBespokeRenderTests.cs
│   │   │   ├── PartnersCapitalStatementLayoutTests.cs
│   │   │   ├── PeriodCloseProjectorTests.cs
│   │   │   ├── PeriodReopenTests.cs
│   │   │   ├── PortfolioPricingRuleTests.cs
│   │   │   ├── PreferredReturnCalculatorTests.cs
│   │   │   ├── RecurringJournalScheduleTests.cs
│   │   │   ├── ShareClassUnitRegisterTests.cs
│   │   │   └── YearEndCloseTests.cs
│   │   ├── Mcp
│   │   │   └── ToolProcessRunnerTests.cs
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
│   │   │   │   ├── IbFlexWebServiceClientTests.cs
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
│   │   │   ├── LedgerJournalInternalTransactionSourceTests.cs
│   │   │   ├── ReconciliationCaseServiceTests.cs
│   │   │   ├── ReconciliationContractsTests.cs
│   │   │   ├── RetainedInternalReconciliationPopulationProviderTests.cs
│   │   │   ├── StatementBreakClassifierTests.cs
│   │   │   ├── StatementCaseworkCommitStoreTests.cs
│   │   │   ├── StatementFixtureScenarioTests.cs
│   │   │   ├── StatementImportAndMatchingTests.cs
│   │   │   ├── StatementRunMatchingServiceTests.cs
│   │   │   ├── StatementRunRecoveryTests.cs
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
│   │   │   ├── BracketChildLimbRuleTests.cs
│   │   │   ├── CompositeRiskValidatorTests.cs
│   │   │   ├── DrawdownCircuitBreakerTests.cs
│   │   │   ├── EnforcedRiskValidatorCompositionTests.cs
│   │   │   ├── FatFingerRuleTests.cs
│   │   │   ├── KillSwitchSweepTripHandlerTests.cs
│   │   │   ├── OrderRateThrottleTests.cs
│   │   │   ├── PortfolioRiskRulesTests.cs
│   │   │   ├── PositionLimitRuleTests.cs
│   │   │   ├── PriceCollarRuleTests.cs
│   │   │   ├── RiskEscalationQueueServiceTests.cs
│   │   │   └── RiskIntegrationTests.cs
│   │   ├── Scripts
│   │   │   └── ProductionRecoveryScriptTests.cs
│   │   ├── SecurityMaster
│   │   │   ├── Workbench
│   │   │   │   ├── ApprovedFieldEditCanonicalMergeHandlerTests.cs
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
│   │   │   ├── CorporateActionPayloadsTests.cs
│   │   │   ├── CorporateActionTaxonomyPropertyTests.cs
│   │   │   ├── CorporateActionTypeDescriptorCatalogTests.cs
│   │   │   ├── DataVendorEntitlementServiceTests.cs
│   │   │   ├── DayCountConventionsTests.cs
│   │   │   ├── FaceValueLotTests.cs
│   │   │   ├── KeyedGatePoolTests.cs
│   │   │   ├── NullOperatorOverridesStoreTests.cs
│   │   │   ├── PostgresOperatorOverridesStoreTests.cs
│   │   │   ├── PostgresSecurityMasterConflictServiceTests.cs
│   │   │   ├── PostgresSecurityMasterRevisionStoreTests.cs
│   │   │   ├── SecurityAccountingInstrumentClassTests.cs
│   │   │   ├── SecurityAssetClassCatalogTests.cs
│   │   │   ├── SecurityAssetClassParityGuardTests.cs
│   │   │   ├── SecurityAssetProfileGovernanceServiceTests.cs
│   │   │   ├── SecurityAssetSpecificTermsUpcasterChainTests.cs
│   │   │   ├── SecurityAssetSpecificTermsUpcasterPipelineTests.cs
│   │   │   ├── SecurityAssetSpecificTermsUpcasterTests.cs
│   │   │   ├── SecurityAssetTermsFieldEditValidatorTests.cs
│   │   │   ├── SecurityAssetTermsSchemaRoundTripTests.cs
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
│   │   │   ├── SecurityMasterCsvParserTests.cs
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
│   │   │   ├── SecurityMasterProjectionCacheTests.cs
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
│   │   │   ├── Banking
│   │   │   │   └── PostgresBankingIntegrityTests.cs
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
│   │   │   ├── Ledger
│   │   │   │   └── RetainedPostingEquivalenceTests.cs
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
│   │   │   ├── AccountingPostingCommandValidatorCurrencyTests.cs
│   │   │   ├── AdaptivePartitionPlacementPlannerTests.cs
│   │   │   ├── AnalysisExportServiceTests.cs
│   │   │   ├── AnalysisQualityReportCsvTests.cs
│   │   │   ├── ArchiveMaintenanceScheduleManagerDurabilityTests.cs
│   │   │   ├── AssetAccountingPostingEvidenceValidatorTests.cs
│   │   │   ├── AtomicFileWriterTests.cs
│   │   │   ├── AtomicSnapshotTestWriter.cs
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
│   │   │   ├── JsonlMarketDataStoreCorruptionTests.cs
│   │   │   ├── JsonlMarketDataStoreSymbolPathTests.cs
│   │   │   ├── JsonlReplayerTests.cs
│   │   │   ├── LedgerBookServiceTests.cs
│   │   │   ├── LedgerCurrencyBackfillTests.cs
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
│   │   │   │   ├── CoveredCallBacktestServiceTests.cs
│   │   │   │   ├── CoveredCallChainProviderAdapterTests.cs
│   │   │   │   ├── CoveredCallChainProviderFactoryConvertCallsTests.cs
│   │   │   │   └── CoveredCallRunProjectionTests.cs
│   │   │   ├── AggregatePortfolioServiceTests.cs
│   │   │   ├── CashFlowProjectionTests.cs
│   │   │   ├── GovernanceExceptionServiceTests.cs
│   │   │   ├── LedgerReadServiceTests.cs
│   │   │   ├── LiveRunMetricsTrackerTests.cs
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
│   │   │   ├── StrategyRunRealismHashTests.cs
│   │   │   ├── StrategyRunResearchRecorderTests.cs
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
│   │   │   ├── TestMarketEventPublisher.cs
│   │   │   └── ThrowingMarketEventPublisher.cs
│   │   ├── Testing
│   │   │   └── TestArtifactDirectory.cs
│   │   ├── TestSupport
│   │   │   ├── ControllableReportingReleaseConsistencyGate.cs
│   │   │   ├── ImmediateReportingReleaseConsistencyGate.cs
│   │   │   ├── PostgresTestSchemaTests.cs
│   │   │   └── ProviderCatalogTestLease.cs
│   │   ├── Treasury
│   │   │   ├── MmfFamilyNormalizationTests.cs
│   │   │   ├── MmfLiquidityServiceTests.cs
│   │   │   ├── MmfRebuildTests.cs
│   │   │   └── MoneyMarketFundServiceTests.cs
│   │   ├── Ui
│   │   │   ├── Evidence
│   │   │   │   ├── EvidenceDocumentExtractionTests.cs
│   │   │   │   ├── EvidenceProofChainBuilderTests.cs
│   │   │   │   ├── JournalEntryEvidenceTests.cs
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
│   │   │   ├── ArchiveMaintenanceEndpointsTests.cs
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
│   │   │   ├── CapitalCallFundingIntakeTests.cs
│   │   │   ├── CapitalCallIssuanceIntakeTests.cs
│   │   │   ├── CashOperationsOrchestratorServiceTests.cs
│   │   │   ├── CollateralExposureServiceTests.cs
│   │   │   ├── CookieCsrfProtectionTests.cs
│   │   │   ├── CredentialCompatibilityEndpointsTests.cs
│   │   │   ├── CronEndpointsTests.cs
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
│   │   │   ├── LedgerAndCompliancePermissionSplitTests.cs
│   │   │   ├── LedgerReportingAuthoritativeSourceTests.cs
│   │   │   ├── LegacyReportingRouteRetirementEndpointTests.cs
│   │   │   ├── LiveTradingEngineHostRegistrationTests.cs
│   │   │   ├── MarginControlCenterReadServiceTests.cs
│   │   │   ├── OmsIntegrationServiceTests.cs
│   │   │   ├── OperationsContinuityReconciliationBridgeTests.cs
│   │   │   ├── OperatorApprovalFlowScenarioTests.cs
│   │   │   ├── OptionReferenceEndpointsRoundtripTests.cs
│   │   │   ├── PlaidWebhookVerifierTests.cs
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
│   │   │   ├── QuantLabParameterSetTests.cs
│   │   │   ├── ReconciliationApiServiceTests.cs
│   │   │   ├── ReconciliationBreakQueueProjectionTests.cs
│   │   │   ├── ReconciliationLegacyBulkActionTests.cs
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
│   │   │   ├── ReportPackProvenanceResolverTests.cs
│   │   │   ├── ReportPackValidationServiceTests.cs
│   │   │   ├── ReportPackWorkflowServiceTests.cs
│   │   │   ├── RiskEndpointsTests.cs
│   │   │   ├── RiskRuleRuntimeFatFingerStatusTests.cs
│   │   │   ├── RiskRuleRuntimeFirstRunDefaultsTests.cs
│   │   │   ├── RiskRuleRuntimeOrderRateStatusTests.cs
│   │   │   ├── RiskRuleRuntimePriceCollarConfigTests.cs
│   │   │   ├── SecureReportingDistributionEndpointTests.cs
│   │   │   ├── SecurityMasterConvertibleEquityEndpointsTests.cs
│   │   │   ├── SecurityMasterExceptionCaseworkServiceTests.cs
│   │   │   ├── SecurityMasterIngestStatusEndpointsTests.cs
│   │   │   ├── SecurityMasterInstrumentPassportTests.cs
│   │   │   ├── SecurityMasterLegacyConflictActionTests.cs
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
│   │   │   ├── WorkstationEndpointsTests.DataReadAuthorization.cs
│   │   │   ├── WorkstationEndpointsTests.Extensibility.cs
│   │   │   ├── WorkstationEndpointsTests.IBResults.cs
│   │   │   ├── WorkstationEndpointsTests.Infrastructure.cs
│   │   │   ├── WorkstationEndpointsTests.JournalAutomation.cs
│   │   │   ├── WorkstationEndpointsTests.LedgerRoleReachability.cs
│   │   │   ├── WorkstationEndpointsTests.ProviderIntegrations.cs
│   │   │   ├── WorkstationEndpointsTests.StrategyTenantScope.cs
│   │   │   ├── WorkstationEndpointsTests.TradingTenantScope.cs
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
│   │   │   ├── ApiClientSessionTests.cs
│   │   │   └── HttpClientConfigurationTests.cs
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
│   │   ├── PostgresTestSchemaEnvironmentScope.cs
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
│   │   │   ├── ApiClientEndpointGenerationTests.cs
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
│   │   │   ├── PostedLedgerProjectionTests.cs
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
│   │   │   ├── DesktopWorkflowReadScopeResolverTests.cs
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
│   │   │   ├── TradingSafetyCommandTests.cs
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
│   │   │   ├── EvidenceWorkbenchViewModelTests.cs
│   │   │   ├── ExportPresetsViewModelTests.cs
│   │   │   ├── FinancialRecordExplorerViewModelTests.cs
│   │   │   ├── FundAccountsViewModelTests.cs
│   │   │   ├── FundLedgerReconciliationReadStateTests.cs
│   │   │   ├── FundLedgerViewModelTests.cs
│   │   │   ├── FundStructureSetupViewModelTests.cs
│   │   │   ├── HomeWorkspaceViewModelTests.cs
│   │   │   ├── LifecycleControlViewModelTests.cs
│   │   │   ├── LiveDataViewerViewModelTests.cs
│   │   │   ├── MainPageOperatingContextSelectionTests.cs
│   │   │   ├── MainShellViewModelTests.cs
│   │   │   ├── MessagingHubViewModelTests.cs
│   │   │   ├── NotificationCenterViewModelTests.cs
│   │   │   ├── OperationsContinuityViewModelTests.cs
│   │   │   ├── OperationsRecordReleaseViewModelTests.cs
│   │   │   ├── OperatorReadinessConsoleViewModelTests.cs
│   │   │   ├── OptionsViewModelConcurrencyTests.cs
│   │   │   ├── OrderBookHeatmapViewModelTests.cs
│   │   │   ├── OrderBookViewModelTests.cs
│   │   │   ├── PageActivationLifetimeContractTests.cs
│   │   │   ├── PortfolioImportViewModelTests.cs
│   │   │   ├── PositionBlotterViewModelTests.cs
│   │   │   ├── PostedLedgerViewModelTests.cs
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
│   │   │   ├── SettingsViewModelTests.cs
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
│   │   │   ├── WorkspacePageViewModelTests.cs
│   │   │   └── WorkspaceShellProvenanceBadgeTests.cs
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
│   │   ├── test_api_contract_coverage_dashboard.py
│   │   ├── test_archive_code_tombstones.py
│   │   ├── test_artifact_retention_module.py
│   │   ├── test_buildctl_artifact_retention.py
│   │   ├── test_buildctl_validation_runner.py
│   │   ├── test_central_package_versions.py
│   │   ├── test_check_action_origin_derivation.py
│   │   ├── test_check_apiclient_callers.py
│   │   ├── test_check_codex_memory.py
│   │   ├── test_check_codex_skills.py
│   │   ├── test_check_contract_compatibility_gate.py
│   │   ├── test_check_contract_type_parity.py
│   │   ├── test_check_dashboard_type_barrel.py
│   │   ├── test_check_duplicate_helpers.py
│   │   ├── test_check_endpoint_cancellation.py
│   │   ├── test_check_file_size_ratchet.py
│   │   ├── test_check_inline_sha256.py
│   │   ├── test_check_ledger_book_scope.py
│   │   ├── test_check_ledger_dimension_coverage.py
│   │   ├── test_check_posture_env_serialization.py
│   │   ├── test_check_program_state_consistency.py
│   │   ├── test_check_status_delivery_claims.py
│   │   ├── test_check_store_concurrency_posture.py
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
│   │   ├── test_generate_ui_route_wiring_report.py
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
│   │   ├── test_validate_agent_definitions.py
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
