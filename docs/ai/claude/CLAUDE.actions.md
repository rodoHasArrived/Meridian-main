# CLAUDE.actions.md - GitHub Actions & CI/CD Guide

This guide covers the CI/CD pipeline for the Meridian, including workflow structure, common tasks, and troubleshooting.

## Workflow Inventory

The project uses 35 GitHub Actions workflows in `.github/workflows/`:

| Workflow | File | Trigger | Purpose |
|----------|------|---------|---------|
| PR Checks | `pr-checks.yml` | PRs to main/develop | Format, build, test, coverage, AI review |
| Test Matrix | `test-matrix.yml` | Push/PRs (source changes) | Multi-platform test matrix (Windows, Linux, macOS) |
| Code Quality | `code-quality.yml` | Push/PRs (source changes) | Formatting, analyzers, AI quality suggestions |
| Security | `security.yml` | PRs to main, weekly (Mon), manual | CodeQL, dependency review, secret detection, SAST |
| Validate Workflows | `validate-workflows.yml` | PRs (workflow changes), manual | YAML validation, action ref checks, permission audit |
| Desktop Builds | `desktop-builds.yml` | Push/PRs (desktop paths), manual | WPF builds, MSIX packaging |
| Docker | `docker.yml` | Manual dispatch | Multi-arch Docker images, GHCR push |
| Release | `release.yml` | Manual dispatch | Semver validation, changelog, GitHub release |
| Benchmark | `benchmark.yml` | Manual dispatch | BenchmarkDotNet performance tracking |
| Bottleneck Detection | `bottleneck-detection.yml` | Manual dispatch | Performance bottleneck analysis |
| Close Duplicates | `close-duplicate-issues.yml` | Issue opened | Auto-close duplicate issues |
| Nightly | `nightly.yml` | Daily (1 AM UTC), manual | Full build + test + AI failure diagnosis |
| Documentation | `documentation.yml` | Push/PRs (docs/source), weekly, issues, manual | Doc generation, structure sync, TODO scan |
| Labeling | `labeling.yml` | PR opened/edited/reopened, manual | Auto-label based on paths and PR size |
| Stale | `stale.yml` | Daily (midnight UTC), manual | Stale issue/PR management |
| Docs Check | `docs-check.yml` | Push/PRs (docs paths) | Documentation link and format validation |
| Export Project Artifact | `export-project-artifact.yml` | Manual dispatch | Project artifact export |
| Golden Path Validation | `golden-path-validation.yml` | Manual dispatch | End-to-end smoke validation for the recommended developer path |
| Build Observability | `build-observability.yml` | Manual dispatch | Build diagnostics, metrics, fingerprints |
| Scheduled Maintenance | `scheduled-maintenance.yml` | Weekly (Sun), manual | Tests, cache cleanup, dependency health, AI recommendations |
| Makefile Validation | `makefile.yml` | Push/PRs (Makefile/build tooling), manual | Ensures documented make targets stay healthy |
| Copilot Setup | `copilot-setup-steps.yml` | Called by Copilot | Copilot environment setup |
| Prompt Generation | `prompt-generation.yml` | Push/PRs (prompt files), manual | AI prompt template generation |
| Python Package (Conda) | `python-package-conda.yml` | Manual dispatch | Builds and validates the conda-based Python package flow |
| Ticker Data Collection | `ticker-data-collection.yml` | Scheduled, manual | Automated ticker data collection |
| Update Diagrams | `update-diagrams.yml` | Push/PRs (source changes), manual | Architecture diagram and UML generation |
| Skill Evaluations | `skill-evals.yml` | Manual dispatch | Runs Codex skill evaluation scenarios and captures artifacts |
| Static Site Checks | `static.yml` | Push/PRs (docs/site changes), manual | Validates static documentation/site generation assets |
| Reusable .NET Build | `reusable-dotnet-build.yml` | Called by other workflows | Shared build/test steps |
| Copilot PR Reviewer | `copilot-pull-request-reviewer.yml` | PR opened/synchronize | AI-powered code review on every PR |
| Copilot SWE Agent | `copilot-swe-agent-copilot.yml` | Manual dispatch | GitHub Copilot coding agent workflow |

## Key Locations

| Item | Path |
|------|------|
| Workflows | `.github/workflows/` |
| Composite actions | `.github/actions/` |
| Shared .NET build | `.github/workflows/reusable-dotnet-build.yml` |
| Workflow docs | `.github/workflows/README.md` |
| Labels config | `.github/labeler.yml`, `.github/labels.yml` |
| Dependabot | `.github/dependabot.yml` |
| PR template | `.github/PULL_REQUEST_TEMPLATE.md` |

## Reusable Build Workflow

`reusable-dotnet-build.yml` is the shared foundation used by multiple workflows. It handles:
- .NET SDK setup with caching (via `.github/actions/setup-dotnet-cache/`)
- Restore, build, and test steps
- Coverage collection and artifact upload

When modifying build logic, change the reusable workflow rather than individual callers.

## Common CI/CD Tasks

### Adding a new workflow
1. Create the YAML file in `.github/workflows/`
2. Use the `setup-dotnet-cache` composite action for .NET setup
3. Add entries to `.github/workflows/README.md`
4. Consider whether it should call `reusable-dotnet-build.yml`

### Updating artifact actions
The project standardizes on `actions/upload-artifact@v4` and `actions/download-artifact@v4` for compatibility. See `../../../archive/docs/assessments/ARTIFACT_ACTIONS_DOWNGRADE.md` for rationale.

### Desktop build targets
The consolidated `desktop-builds.yml` targets `src/Meridian.Wpf/` which is included in the solution build. On Windows it produces the full WPF application; on Linux/macOS a CI-compatible stub is built automatically by the project file.
- `all` - Build all desktop targets
- `wpf` - WPF only (self-contained + framework-dependent)
- `wpf-smoke-test` - WPF startup validation only

### Build observability
The `build-observability.yml` workflow captures structured build events, dependency graphs, and metrics. Artifacts are written to `.build-system/`. See `docs/development/build-observability.md`.

## AI-Powered Features

Several workflows include AI-powered analysis steps:
- **PR Checks** - AI review summary with risk assessment
- **Desktop Builds** - AI build failure diagnosis
- **Nightly** - AI failure diagnosis
- **Security** - AI vulnerability assessment
- **Code Quality** - AI code quality suggestions
- **Documentation** - AI documentation quality review, AI TODO triage
- **Scheduled Maintenance** - AI dependency upgrade recommendations

## Troubleshooting

### Common CI failures
- **NETSDK1100**: Ensure `EnableWindowsTargeting=true` is set in `Directory.Build.props`
- **NU1008**: Version specified on `PackageReference` - remove it (CPM is active)
- **Format check fails**: Run `dotnet format` locally before pushing
- **Coverage upload fails**: Check Codecov token in repository secrets

### Validating workflows locally
```bash
# Syntax-check all workflow YAML files
for f in .github/workflows/*.yml; do
  python3 -c "import yaml; yaml.safe_load(open('$f'))"
done
```

## Related Documentation

- [`.github/workflows/README.md`](https://github.com/rodoHasArrived/Meridian/blob/main/.github/workflows/README.md) - Full workflow details
- [`docs/development/github-actions-summary.md`](../../development/github-actions-summary.md) - Workflow summary
- [`docs/development/github-actions-testing.md`](../../development/github-actions-testing.md) - CI testing tips
- [`docs/development/build-observability.md`](../../development/build-observability.md) - Build metrics system

---

## Related Resources

- **Master AI index:** [`docs/ai/README.md`](https://github.com/rodoHasArrived/Meridian/blob/main/archive/docs/README.md)
- **Error prevention:** [`docs/ai/ai-known-errors.md`](../ai-known-errors.md)
- **Root context:** [`CLAUDE.md`](https://github.com/rodoHasArrived/Meridian/blob/main/CLAUDE.md) § CI/CD Pipelines

---

*Last Updated: 2026-03-20*
