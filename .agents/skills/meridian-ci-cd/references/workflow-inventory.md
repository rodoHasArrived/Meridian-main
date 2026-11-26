# Meridian workflow map (orientation only)

The authoritative table is `.github/workflows/README.md` in the repository, with generated
copies in `docs/status/workflow-manifest.json` and `docs/generated/workflows-overview.md`.
This map groups the workflows so you know which file to open. Check the README before relying
on a trigger or purpose stated here.

## Merge gate and security

| Workflow | File | Runs on |
|---|---|---|
| Meridian CI | `meridian-ci.yml` | PRs, `main` pushes, merge groups, manual. Four lanes in three jobs plus `quality-gate` and `integration-gate` |
| Service-backed Integrations | `service-backed-integrations.yml` | Reusable; called by Production Certification. Its suites live in the `.github/actions/service-backed-integrations` action, which Meridian CI's `integration-gate` also runs |
| CI | `ci.yml` | Secret Scan on PRs/pushes/merge groups; nightly and manual coverage |
| CodeQL | `codeql.yml` | PRs, `main` pushes, weekly, manual, release gate |
| Maintenance | `maintenance.yml` | Workflow/docs/tooling changes, weekly, manual |

## Path-filtered specialist checks

| Workflow | File |
|---|---|
| Windows Desktop Build | `windows-desktop-build.yml` |
| Golden Path Validation | `golden-path-validation.yml` |
| Demo Smoke | `demo-smoke.yml` |
| PostgreSQL Schema Control | `schema-control.yml` |
| Roadmap Source Docs | `roadmap-source-docs.yml` |
| Design System Package | `design-system-package.yml` |
| Pipeline Benchmark | `pipeline-benchmark.yml` |
| Dependency Lock Candidates | `dependency-locks.yml` |
| IB API Smoke Build | `ibapi-smoke.yml` |
| Copilot Setup Steps | `copilot-setup-steps.yml` |

## Manual tools

| Workflow | File |
|---|---|
| Targeted Test | `targeted-test.yml` |
| Documentation Automation | `documentation.yml` |
| Roadmap Tools (Manual) | `roadmap-tools-manual.yml` |
| CI Concurrency Benchmark | `ci-concurrency-benchmark.yml` |
| Desktop Workflow Runner | `desktop-workflow-runner.yml` |
| Robinhood Options Smoke | `robinhood-options-smoke.yml` |
| Branch Cleanup | `branch-cleanup.yml` |

## Scheduled operations

| Workflow | File |
|---|---|
| CI Operational Metrics | `ci-metrics.yml` |
| AI Navigation Refresh | `ai-navigation-refresh.yml` |
| Provider Validation | `provider-validation.yml` |
| IB API Official Runtime | `ibapi-runtime.yml` (self-hosted, protected environment) |
| Release Readiness | `release-readiness.yml` |
| Web / Desktop Screenshot Capture | `web-screenshot-capture.yml`, `desktop-screenshot-capture.yml` |

## Generated-file repair

| Workflow | File |
|---|---|
| Generated Repair | `generated-repair.yml` (read-only `pull_request_target`) |
| Generated Repair Publisher | `generated-repair-publish.yml` (App token, validated provenance) |

## Release and certification

| Workflow | File |
|---|---|
| Publish Smoke | `publish-smoke.yml` |
| Production Certification | `production-certification.yml` |
| Desktop Installer Packaging | `desktop-installer-packaging.yml` |
| Desktop Evaluation Prerelease | `desktop-evaluation-prerelease.yml` |

## Local composite actions

| Action | Purpose |
|---|---|
| `.github/actions/setup-dotnet-nuget` | SDK from `global.json` plus the shared NuGet cache |
| `.github/actions/install-playwright-chromium` | Bounded Chromium install for screenshot workflows |
| `.github/actions/close-superseded-refresh-prs` | Closes older automation refresh PRs |
