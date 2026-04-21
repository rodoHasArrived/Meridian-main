# Development Guides

**Owner:** Core Team
**Scope:** Engineering and contributor-facing guidance
**Last Reviewed:** 2026-04-16
**Review Cadence:** As tooling, workflows, or implementation conventions evolve

This directory is the working index for Meridian developer guidance. Use it when you need to understand how the repository is organized, how to extend or test the platform, how desktop development works, or which automation and CI documents are authoritative.

## Start Here

If you are new to the repository, read these in order:

1. [Repository Organization Guide](repository-organization-guide.md) for folder placement, naming, and doc location rules.
2. [Repository Rule Set](repository-rule-set.md) for repository-wide contribution and quality expectations.
3. [Documentation Contribution Guide](documentation-contribution-guide.md) if your change adds, moves, or retires docs.

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
- Operational runbooks and deployment steps belong in [operations/](../operations/README.md)
- Evaluations, proposals, and option analysis belong in [evaluations/](../evaluations/README.md)
- Active roadmap and delivery tracking belong in [status/](../status/README.md)

## Guide Map

### Repository And Contribution Foundations

| Document | Use it when you need to... |
| --- | --- |
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
| [Desktop Support Policy](policies/desktop-support-policy.md) | confirm expected validation and support obligations for desktop-facing changes |

### CI, Build, And Observability

| Document | Use it when you need to... |
| --- | --- |
| [GitHub Actions Summary](github-actions-summary.md) | get the short reference for the repo's core workflows |
| [GitHub Actions Testing Checklist](github-actions-testing.md) | validate workflow changes before or after editing GitHub Actions |
| [Build Observability System](build-observability.md) | capture structured build telemetry, metrics, and diagnostics |
| [OTLP Trace Visualization](otlp-trace-visualization.md) | inspect Meridian traces and metrics in a local telemetry UI |
| [Git Hooks](git-hooks.md) | install the repo-managed local quality gate before committing |

### Documentation Tooling And Automation

| Document | Use it when you need to... |
| --- | --- |
| [Documentation Automation Guide](documentation-automation.md) | run or understand the documentation workflow and generated outputs |
| [Expanding Documentation Scripts](expanding-scripts.md) | add new scripts under `build/scripts/docs/` using existing conventions |
| [Adding Custom Documentation Rules](adding-custom-rules.md) | extend `build/rules/doc-rules.yaml` and the rules engine safely |

## Authoritative Neighbors

Some topics in this folder have deeper source material outside `docs/development/`:

- [`.github/workflows/README.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.github/workflows/README.md) is the authoritative workflow inventory referenced by the GitHub Actions summary.
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
- [Evaluations](../evaluations/README.md)
- [Status Docs](../status/README.md)
