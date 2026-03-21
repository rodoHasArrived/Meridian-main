> **AUTO-GENERATED — DO NOT EDIT**
> This file is generated automatically. Manual edits will be overwritten.
> See `docs/generated/README.md` for details on how generation works.

# GitHub Workflows Overview

> Auto-generated on 2026-03-20 18:36:19 UTC

This document provides an overview of all GitHub Actions workflows in the repository.

## Available Workflows

| Workflow | File | Triggers |
|----------|------|----------|
| Benchmark Performance | `benchmark.yml` | push, PR, manual |
| Bottleneck Detection | `bottleneck-detection.yml` | PR, manual |
| Build Observability | `build-observability.yml` | push, PR, manual |
| Close Duplicate and Stale Auto-Generated Issues | `close-duplicate-issues.yml` | manual, scheduled |
| Code Quality | `code-quality.yml` | push, PR, manual |
| Copilot Pull Request Reviewer | `copilot-pull-request-reviewer.yml` | PR, manual |
| Copilot SWE Agent / Copilot | `copilot-swe-agent-copilot.yml` | manual |
| Copilot Setup Steps | `copilot-setup-steps.yml` | push, manual |
| Deploy static content to Pages | `static.yml` | push, manual |
| Desktop Builds | `desktop-builds.yml` | push, PR, manual |
| Docker | `docker.yml` | push, PR, manual |
| Documentation Automation | `documentation.yml` | push, PR, manual, scheduled |
| Export Project Artifact | `export-project-artifact.yml` | manual |
| Golden Path Validation | `golden-path-validation.yml` | push, PR, manual |
| Labeling | `labeling.yml` | PR, manual |
| Maintenance Checks | `maintenance.yml` | push, PR, manual, scheduled |
| Maintenance Self-Test | `maintenance-self-test.yml` | PR, manual |
| Makefile CI | `makefile.yml` | push, PR, manual |
| Mark Stale Issues and PRs | `stale.yml` | manual, scheduled |
| Nightly Testing | `nightly.yml` | manual, scheduled |
| Prompt Generation | `prompt-generation.yml` | manual |
| Pull Request Checks | `pr-checks.yml` | PR, manual |
| Python Package using Conda | `python-package-conda.yml` | unknown |
| Release Management | `release.yml` | manual |
| Repo Health | `repo-health.yml` | PR, manual, scheduled |
| Reusable .NET Build | `reusable-dotnet-build.yml` | unknown |
| Scheduled Maintenance | `scheduled-maintenance.yml` | manual, scheduled |
| Security | `security.yml` | PR, manual, scheduled |
| Skill Eval Regression Check | `skill-evals.yml` | PR, manual |
| Test Matrix | `test-matrix.yml` | push, PR, manual |
| Ticker Data Collection | `ticker-data-collection.yml` | manual |
| Update Diagram Artifacts | `update-diagrams.yml` | push, manual |
| Validate Workflows | `validate-workflows.yml` | PR, manual |

## Workflow Categories

### CI/CD Workflows
- **Build & Test**: Main build pipeline, test matrix
- **Code Quality**: Linting, static analysis
- **Security**: Dependency scanning, vulnerability checks

### Documentation Workflows
- **Documentation**: Validation, generation, deployment
- **Docs Structure Sync**: Auto-update structure documentation

### Release Workflows
- **Docker Build**: Container image builds
- **Publishing**: Release artifacts

### Maintenance Workflows
- **Scheduled Maintenance**: Cleanup, dependency updates
- **Stale Management**: Issue/PR lifecycle

## Workflow Count

- **Total workflows:** 33

---

*This file is auto-generated. Do not edit manually.*
