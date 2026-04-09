# Development Guides

**Owner:** Core Team
**Scope:** Engineering — Developer-Facing
**Review Cadence:** As features and tooling evolve

---

## Purpose

This directory contains guides for developers contributing to or extending Meridian. It covers project conventions, tooling, CI/CD, testing, documentation automation, and how to add new capabilities.

---

## What Belongs Here

- Developer onboarding and project organization guides
- Provider implementation patterns and how-to guides
- Build tooling, CI/CD pipeline documentation
- Desktop (WPF) development guides
- Refactoring maps and cleanup plans
- Documentation standards and contribution guidelines
- Script development and extension guides
- Feature-specific implementation notes (e.g., backfill, offline mode)

## What Does NOT Belong Here

- Architecture rationale → use `architecture/`
- Operational deployment procedures → use `operations/`
- High-level evaluations or brainstorms → use `evaluations/`
- Active tracking of project status → use `status/`

---

## Contents

### Getting Started

| Document | Description |
|----------|-------------|
| [Repository Organization Guide](repository-organization-guide.md) | **START HERE** — Project layout and conventions |
| [Repository Rule Set](repository-rule-set.md) | Repository-wide contribution and quality rules |
| [Documentation Contribution Guide](documentation-contribution-guide.md) | Writing and maintaining docs |

### Building & Extending

| Document | Description |
|----------|-------------|
| [Provider Implementation Guide](provider-implementation.md) | How to add new data providers |
| [Adding Custom Rules](adding-custom-rules.md) | Build system rule customization |
| [Expanding Scripts](expanding-scripts.md) | Script development patterns |
| [Central Package Management](central-package-management.md) | NuGet version management (CPM) |

### Testing

| Document | Description |
|----------|-------------|
| [Desktop Testing Guide](desktop-testing-guide.md) | Testing desktop (WPF) services |
| [GitHub Actions Testing](github-actions-testing.md) | Testing CI/CD workflows locally |

### CI/CD & Build

| Document | Description |
|----------|-------------|
| [GitHub Actions Summary](github-actions-summary.md) | CI/CD pipeline overview |
| [Build Observability](build-observability.md) | Build metrics and diagnostics |
| [OTLP Trace Visualization](otlp-trace-visualization.md) | Inspect local trace output and observability wiring |
| [Git Hooks](git-hooks.md) | Local pre-commit quality checks (`dotnet format`) |
| [Tooling & Workflow Backlog](tooling-workflow-backlog.md) | Prioritized cleanup plan for repo automation |

### Desktop Development

| Document | Description |
|----------|-------------|
| [WPF Implementation Notes](wpf-implementation-notes.md) | WPF desktop app development |
| [UI Fixture Mode Guide](ui-fixture-mode-guide.md) | Offline development with mock data |
| [Desktop Workflow Automation](desktop-workflow-automation.md) | Scripted desktop debug flows, screenshot capture, and manual generation |

> **Desktop improvement evaluations** (executive summary, quick reference, implementation guide) have been moved to [`evaluations/`](../evaluations/README.md) where strategic assessments are maintained.

### Documentation Automation

| Document | Description |
|----------|-------------|
| [Documentation Automation](documentation-automation.md) | Auto-generation scripts and pipelines |

### Policies

| Document | Description |
|----------|-------------|
| [Desktop Support Policy](policies/desktop-support-policy.md) | Desktop support and maintenance policy |

### Planning & Cleanup

| Document | Description |
|----------|-------------|
| [Refactor Map](refactor-map.md) | Code areas earmarked for refactoring |

---

## Related

- [Architecture Documentation](../architecture/README.md) — System design and ADRs
- [Operations Documentation](../operations/README.md) — Deployment and maintenance guides
- [Plans Overview](../plans/README.md) — Active implementation blueprints and roadmaps
- [Evaluations](../evaluations/README.md) — Technology and architecture evaluations

---

*Development guides are maintained by the core team and updated as tooling and conventions evolve.*
