# GitHub Actions Workflows - Summary

This document provides a quick reference for all GitHub Actions workflows in the Meridian repository.

**Last Updated:** 2026-03-20
**Scope:** Core workflow summary (see `.github/workflows/README.md` for the full current inventory)
**Authoritative Reference:** [`.github/workflows/README.md`](https://github.com/rodoHasArrived/Meridian/blob/main/.github/workflows/README.md)

---

## Core Workflow Inventory

### Build & Release (5 workflows)

| Workflow | File | Trigger | Purpose |
| --- | --- | --- | --- |
| Pull Request Checks | `pr-checks.yml` | PRs to main/develop | Format, build, test, coverage, AI review |
| Docker | `docker.yml` | Manual dispatch | Multi-arch Docker images, optional GHCR push |
| Release Management | `release.yml` | Manual dispatch | Semver validation, changelog, tag, GitHub release |
| Desktop Builds | `desktop-builds.yml` | Push/PRs (desktop paths), manual | WPF desktop builds with selective targeting |
| Reusable .NET Build | `reusable-dotnet-build.yml` | Called by other workflows | Shared build/test steps |

### Code Quality & Security (3 workflows)

| Workflow | File | Trigger | Purpose |
| --- | --- | --- | --- |
| Code Quality | `code-quality.yml` | Push/PRs (source changes) | Formatting, analyzers, AI quality suggestions |
| Security | `security.yml` | PRs to main, weekly (Mon 5 AM UTC), manual | CodeQL, dependency review, secret detection, SAST, AI assessment |
| Validate Workflows | `validate-workflows.yml` | PRs (workflow changes), manual | YAML validation, action ref checks, permission audit |

### Testing (4 workflows)

| Workflow | File | Trigger | Purpose |
| --- | --- | --- | --- |
| Test Matrix | `test-matrix.yml` | Push/PRs (source changes) | Cross-platform tests (Linux on PRs, Win/Mac on main) |
| Python Package using Conda | `python-package-conda.yml` | Push | Flake8 and pytest coverage for Python automation scripts |
| Nightly Testing | `nightly.yml` | Daily (1 AM UTC), manual | Full cross-platform tests, benchmarks, AI failure diagnosis |
| Benchmark | `benchmark.yml` | Manual dispatch | BenchmarkDotNet performance tracking |

### Documentation & Maintenance (5 workflows)

| Workflow | File | Trigger | Purpose |
| --- | --- | --- | --- |
| Documentation | `documentation.yml` | Push/PRs (docs/source), weekly (Mon 3 AM UTC), `ai-known-error` issues, manual | Doc generation, linting, link checks, AI instruction sync, task marker scan |
| Labeling | `labeling.yml` | PR opened/edited/reopened, manual | Auto-label by file paths and PR size |
| Stale Management | `stale.yml` | Daily (midnight UTC), manual | Mark/close stale issues (60d) and PRs (30d) |
| Scheduled Maintenance | `scheduled-maintenance.yml` | Weekly (Sun 8 AM UTC), manual | Tests, dependency health, cache cleanup, AI recommendations |
| Build Observability | `build-observability.yml` | Manual dispatch | Build diagnostics, metrics, fingerprint collection |

---

## Consolidation History

The workflow surface has continued to evolve after the 2026-02-05 consolidation pass, so this document intentionally summarizes the core lanes rather than maintaining an exact file count.

| Consolidated Workflow | Replaced |
| --- | --- |
| `documentation.yml` | `docs-comprehensive.yml`, `docs-auto-update.yml`, `docs-structure-sync.yml`, `ai-instructions-sync.yml`, `task-automation.yml` |
| `desktop-builds.yml` | `desktop-app.yml`, `wpf-desktop.yml`, `wpf-commands.yml` |
| `security.yml` | absorbed `dependency-review.yml` |
| `scheduled-maintenance.yml` | absorbed `cache-management.yml` |

---

## AI-Powered Features

All AI features use `actions/ai-inference@v1` with `continue-on-error: true` (never blocks workflows):

| Workflow | AI Feature |
| --- | --- |
| `pr-checks.yml` | PR review with risk assessment |
| `code-quality.yml` | Quality suggestions with priority fixes |
| `security.yml` | Vulnerability assessment and remediation |
| `nightly.yml` | Failure diagnosis with root cause analysis |
| `desktop-builds.yml` | Build failure diagnosis |
| `documentation.yml` | Doc quality review, task marker triage |
| `scheduled-maintenance.yml` | Dependency upgrade recommendations |

---

## Custom Action

### Setup .NET with Cache (`.github/actions/setup-dotnet-cache/`)

- Composite action for .NET SDK setup with NuGet caching
- Inputs: `dotnet-version` (default: 10.0.x), `cache-suffix`
- Cache key based on project file hashes
- On macOS, exports `DOTNET_ROOT`, `DOTNET_ROOT_ARM64`, and `DOTNET_ROOT_X64` from the resolved
  real `dotnet` command path so generated test apphosts, including the F# xUnit v3 discovery
  executable, can find `hostfxr` when the SDK comes from the hosted runner image or a symlinked
  `dotnet` shim instead of `actions/setup-dotnet`; the action now verifies that the resolved root
  contains `host/fxr` and falls back one directory when the executable lives under an architecture
  subfolder.

---

## Configuration Files

| File | Purpose |
| --- | --- |
| `.github/dependabot.yml` | Weekly dependency updates (npm, NuGet, Actions, Docker) |
| `.github/labeler.yml` | Auto-label path patterns |
| `.github/labels.yml` | Label definitions |
| `.github/markdown-link-check-config.json` | Link checker settings |
| `.github/spellcheck-config.yml` | Spell-check configuration |
| `environment.yml` | Conda dependencies for Python workflow tests, including Pillow-backed screenshot diff fixtures |

---

## Quick Commands

```bash
# View all workflows
ls .github/workflows/*.yml

# Validate YAML syntax
for f in .github/workflows/*.yml; do
  python3 -c "import yaml; yaml.safe_load(open('$f'))"
done

# Check recent workflow runs
gh run list --limit 10

# Trigger a workflow manually
gh workflow run benchmark.yml
```

---

## Related Documentation

- [`.github/workflows/README.md`](https://github.com/rodoHasArrived/Meridian/blob/main/.github/workflows/README.md) - Full workflow details and dependency diagram
- [`docs/ai/claude/CLAUDE.actions.md`](../ai/claude/CLAUDE.actions.md) - AI assistant CI/CD guide
- [`docs/development/build-observability.md`](build-observability.md) - Build metrics system
- [`docs/development/github-actions-testing.md`](github-actions-testing.md) - CI testing tips
