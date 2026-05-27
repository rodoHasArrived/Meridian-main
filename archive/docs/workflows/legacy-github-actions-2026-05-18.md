# Legacy GitHub Actions Inventory - 2026-05-18

This note records the workflow modernization pass that reduced `.github/workflows/`
to the current four-workflow surface:

- `ci.yml`
- `windows-desktop-build.yml`
- `publish-smoke.yml`
- `maintenance.yml`

The files below were removed from the active workflow directory after inspection. Useful
build, test, publish, and workflow-hygiene behavior was merged into the current workflows.

## Rewritten Or Merged

| Legacy workflow | Disposition | Preserved behavior |
| --- | --- | --- |
| `pr-checks.yml` | Merged | Restore, build, tests, targeted contract checks folded into `ci.yml` where still generally useful |
| `test-matrix.yml` | Merged | Non-integration solution tests folded into `ci.yml`; Windows WPF validation moved to `windows-desktop-build.yml` |
| `code-quality.yml` | Merged | `dotnet format --verify-no-changes` folded into `ci.yml` |
| `desktop-builds.yml` | Rewritten | Windows WPF build, WPF tests, and smoke publish moved to `windows-desktop-build.yml` |
| `export-standalone-exe.yml` | Rewritten | Safe manual publish artifact flow moved to `publish-smoke.yml` |
| `validate-workflows.yml` | Merged | Workflow syntax and hygiene checks moved to `maintenance.yml` plus `build/scripts/ci/check-workflow-hygiene.py` |
| `maintenance.yml` | Rewritten | Broad maintenance split down to workflow hygiene only |
| `repo-health.yml` | Merged | Relevant artifact/path/workflow-name checks moved to the workflow hygiene script |
| `workflow-docs-parity.yml` | Merged | Active workflow docs are now maintained directly and checked for stale removed workflow references |

## Deleted As Obsolete Or Out Of Scope

| Legacy workflow | Reason |
| --- | --- |
| `benchmark.yml` | Manual benchmark automation was not part of the normal build/test/publish gate; local benchmark commands remain available |
| `bottleneck-detection.yml` | Duplicated benchmark/performance automation with nonblocking advisory output |
| `build-observability.yml` | Build observability remains available through local `buildctl.py`; the workflow was not required for the shippable gate |
| `canonicalization-fixture-maintenance.yml` | Specialized fixture regeneration, not part of current CI/CD gate |
| `close-duplicate-issues.yml` | Issue administration, outside build/test/publish modernization scope |
| `codeql.yml` | Duplicated security automation and older action refs; restore-time NuGet audit remains in build lanes |
| `copilot-pull-request-reviewer.yml` | AI review automation outside current CI/CD scope |
| `copilot-setup-steps.yml` | Copilot environment automation outside current CI/CD scope |
| `copilot-swe-agent-copilot.yml` | Copilot visibility automation outside current CI/CD scope |
| `docker.yml` | Docker build/publish path is not the current safe release surface |
| `documentation.yml` | Regeneration, GitHub Pages deployment, issue creation, and AI jobs were too broad for current CI/CD |
| `export-project-artifact.yml` | Generic repository export did not validate shippability |
| `generate-build-artifact.yml` | Reusable artifact workflow was no longer called after CI simplification |
| `golden-path-validation.yml` | Duplicated current restore/build/test/dashboard validation |
| `labeling.yml` | Issue/PR labeling, outside build/test/publish scope |
| `maintenance-self-test.yml` | Superseded by the new maintenance hygiene script |
| `makefile.yml` | Duplicated direct CI commands and depended on Make availability |
| `nightly.yml` | Overlapped CI with broad scheduled work and advisory AI jobs |
| `program-state-validation.yml` | Specialized status check not part of normal build/test/publish gate |
| `prompt-generation.yml` | AI prompt mutation workflow outside current CI/CD scope |
| `python-package-conda.yml` | Conda package workflow no longer represents the current project build reality |
| `readme-tree.yml` | Automatic doc mutation removed from active workflows |
| `refresh-screenshots.yml` | Heavy screenshot regeneration removed from normal workflow surface; screenshot scripts remain local/manual |
| `release.yml` | Public release creation and tag mutation removed; publish smoke is artifact-only |
| `reusable-ai-analysis.yml` | No active workflow calls shared AI analysis |
| `reusable-dotnet-build.yml` | Removed after replacing reusable indirection with direct, readable jobs |
| `scheduled-maintenance.yml` | Broad scheduled maintenance overlapped CI and introduced nonessential mutation/advisory work |
| `security.yml` | Broad security/AI/Docker scanning workflow removed from the smaller shippability gate |
| `skill-evals.yml` | Skill evaluation automation outside build/test/publish scope |
| `stale.yml` | Issue/PR stale management outside build/test/publish scope |
| `static.yml` | GitHub Pages deployment outside safe workflow scope |
| `ticker-data-collection.yml` | Data collection artifact workflow outside normal CI/CD |
| `update-diagrams.yml` | Automatic generated-doc mutation removed; local diagram generation remains documented |

## Related Current Docs

- `.github/workflows/README.md`
- `docs/development/github-actions-summary.md`
- `docs/development/github-actions-testing.md`
- `docs/developer/build-test-run.md`
- `docs/developer/publish-standalone-exe.md`
