# Development Guides

**Status:** supporting
**Owner:** core-team
**Reviewed:** 2026-07-19
**Scope:** Engineering and contributor-facing guidance
**Review Cadence:** As tooling, workflows, or implementation conventions evolve

This supporting directory contains detailed implementation guides reached through the canonical
[Engineering](../engineering/README.md) lane. Use it for repository organization, extension and
test patterns, desktop development, and automation detail; keep the shortest current command path
in Engineering and Start.

## Start Here

If you are new to the repository, read these in order:

1. [Start](../start/README.md) for local prerequisites and restore commands.
2. [Engineering](../engineering/README.md) for the shortest current build, test, and run command path.
3. [Repository Organization Guide](repository-organization-guide.md) for folder placement, naming, and doc location rules.
4. [Repository Rule Set](repository-rule-set.md) for repository-wide contribution and quality expectations.
5. [Documentation Contribution Guide](documentation-contribution-guide.md) if your change adds, moves, or retires docs.

### Migration Note

High-traffic engineering command and setup guides now route to active docs, with historical versions preserved in archive:

- [Developer Setup archive](../../archive/docs/developer/setup.md)
- [Build, Test, Run archive](../../archive/docs/developer/build-test-run.md)
- [Publish Standalone EXE archive](../../archive/docs/developer/publish-standalone-exe.md)
- [docs/development/desktop-testing-guide.md](desktop-testing-guide.md)

For a broader docs map, return to the main [docs index](../README.md).

## What Belongs Here

- Contributor onboarding and repository conventions
- Developer workflow, testing, CI, and build guidance
- Desktop and WPF implementation and fixture-mode guides
- Provider implementation and extension guidance
- Documentation tooling, script expansion, and custom rule authoring
- Engineering policies, refactor maps, and workflow backlog material

## What Does Not Belong Here

- Architecture narratives and rationale belong in [architecture/](../architecture/README.md)
- Operational runbooks and deployment steps belong in [Operators](../operators/README.md)
- Historical evaluations, proposals, and option analysis belong in the [assessment archive](../../archive/docs/assessments/README.md); active findings belong in the owning canonical lane.
- Active roadmap and delivery tracking belong in the [Roadmap Registry](../roadmap/README.md);
  automation-owned reports remain under [Status](../status/README.md)

## Guide Map

### Repository And Contribution Foundations

| Document | Use it when you need to... |
| --- | --- |
| [Start](../start/README.md) | bootstrap a fresh checkout from the repository root |
| [Engineering](../engineering/README.md) | choose a narrow build, test, or local run command |
| [Tooling Architecture](tooling-architecture.md) | understand tooling layers, ownership, and local-to-CI command mapping |
| [Publish Standalone EXE archive](../../archive/docs/developer/publish-standalone-exe.md) | review historical standalone publish guidance |
| [Repository Organization Guide](repository-organization-guide.md) | place code, docs, assets, or new project files in the right location |
| [Repository Rule Set](repository-rule-set.md) | understand non-negotiable contribution, quality, and repo hygiene rules |
| [Documentation Contribution Guide](documentation-contribution-guide.md) | add, review, archive, or reorganize documentation correctly |
| [Central Package Management](central-package-management.md) | update NuGet dependencies through `Directory.Packages.props` |
| [F# Decision Rule](fsharp-decision-rule.md) | decide whether a new subsystem should stay in C# or move to F# |

### Providers, Extension Points, And Refactors

| Document | Use it when you need to... |
| --- | --- |
| [Provider Implementation Guide](provider-implementation.md) | add or extend streaming, historical, or symbol-search providers |
| [Rule Evaluation Contract Layer](rule-evaluation-contracts.md) | align kernel outputs to the shared `Score + Reasons + Trace` envelope |
| [Refactor Map](refactor-map.md) | understand the current dependency-safe refactor opportunities |
| [Tooling & Workflow Backlog](tooling-workflow-backlog.md) | review proposed contributor-workflow and automation cleanup themes |

### Desktop And WPF Development

| Document | Use it when you need to... |
| --- | --- |
| [WPF Implementation Notes](wpf-implementation-notes.md) | understand the current desktop shell, workspace model, and implementation shape |
| [Desktop Development Testing Guide](desktop-testing-guide.md) | bootstrap, build, and validate the WPF desktop surface locally |
| [UI Fixture Mode Guide](ui-fixture-mode-guide.md) | run the desktop UI with deterministic offline data |
| [Desktop Workflow Automation](desktop-workflow-automation.md) | drive scripted desktop flows, screenshots, and manual-generation workflows |
| [Codex Desktop Workstation Workflow](codex-workflow.md) | use Codex skills, prompt templates, and quality scripts for modular desktop work |
| [Modular Desktop Architecture](modular-desktop-architecture.md) | keep WPF workspaces reusable, MVVM-aligned, and refactorable |
| [Desktop Resource Management](desktop-resource-management.md) | apply memory, CPU, I/O, rendering, concurrency, and lifecycle guardrails |
| [Shared Workstation Components](shared-workstation-components.md) | inventory and extract reusable desktop workstation controls and view-model patterns |
| [Process Lifecycle Diagnostics](process-lifecycle-diagnostics.md) | check startup/shutdown ownership and safely report leftover Meridian/dotnet processes |
| [Desktop Support Policy](policies/desktop-support-policy.md) | confirm expected validation and support obligations for desktop-facing changes |
| [WPF ↔ Web-UI Alignment Plan](wpf-web-ui-alignment-plan.md) | close desktop parity gaps against the browser workstation over shared contracts |

### Browser Workstation UI

| Document | Use it when you need to... |
| --- | --- |
| [Web UI Structural Improvement Proposal](web-ui-structural-improvement-proposal.md) | review the browser workstation's screenshot-evidenced UX findings and the proposed structural changes (route-scoped views, master–detail cockpit, shell de-noising) |
| [Web UI Structural Mockups](mockups/web-ui/README.md) | view static HTML mockups of the proposed workspace layouts before implementation |

### CI, Build, And Observability

| Document | Use it when you need to... |
| --- | --- |
| [GitHub Actions Summary](github-actions-summary.md) | get the short reference for the repo's core workflows |
| [GitHub Actions Testing Checklist](github-actions-testing.md) | validate workflow changes before or after editing GitHub Actions |
| [Build Observability System](build-observability.md) | capture structured build telemetry, metrics, and diagnostics |
| [Runtime Observability And Diagnostics](runtime-observability.md) | apply runtime logging, redaction, correlation, provider-health, and diagnostic-bundle standards |
| [OTLP Trace Visualization](otlp-trace-visualization.md) | inspect Meridian traces and metrics in a local telemetry UI |
| [Git Hooks](git-hooks.md) | install the repo-managed local quality gate before committing |
| [God-File Burn-Down Plan](god-file-burn-down-plan.md) | understand the file-size ratchet baseline, its burn-down targets, and decomposition sequencing |

### Documentation Tooling And Automation

| Document | Use it when you need to... |
| --- | --- |
| [Documentation Automation Guide](documentation-automation.md) | run documentation automation scripts and understand generated outputs |
| [Expanding Documentation Scripts](expanding-scripts.md) | add new scripts under `build/scripts/docs/` using existing conventions |
| [Adding Custom Documentation Rules](adding-custom-rules.md) | extend `build/rules/doc-rules.yaml` and the rules engine safely |

## Authoritative Neighbors

Some topics in this folder have deeper source material outside `docs/development/`:

- [`docs/generated/workflows-overview.md`](../generated/workflows-overview.md) is the generated GitHub Actions workflow inventory. [`docs/status/workflow-manifest.json`](../status/workflow-manifest.json), [`docs/generated/workflow-command-reference.md`](../generated/workflow-command-reference.md), and [`docs/status/workflow-validation-summary.json`](../status/workflow-validation-summary.json) cover workflow command validation.
- [`build/scripts/docs/README.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/build/scripts/docs/README.md) is the script-level companion to the documentation automation guides.
- [`scripts/dev/`](https://github.com/rodoHasArrived/Meridian-main/tree/main/scripts/dev) contains the PowerShell runners and workflow catalogs referenced by the desktop workflow guides.

## Maintenance Notes

When you add, remove, or supersede a guide in this folder:

1. Update this index in the same change.
2. Prefer linking to the most authoritative guide instead of duplicating process details here.
3. Move historical or superseded material to `archive/docs/` rather than leaving stale duplicates in place.
4. Check the root [docs index](../README.md) if the change affects repo-wide navigation.

## Related

- [Architecture Documentation](../architecture/README.md)
- [Operations Documentation](../operations/README.md)
- [Assessment archive](../../archive/docs/assessments/README.md)
- [Status Docs](../status/README.md)
