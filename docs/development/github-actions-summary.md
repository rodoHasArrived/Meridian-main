# GitHub Actions Workflows - Summary

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

The Meridian Actions inventory is generated from `.github/workflows/*.yml` files instead of hand-maintained counts or tables. Use [`docs/generated/workflows-overview.md`](../generated/workflows-overview.md) as the workflow list source of truth.

Related generated command and validation artifacts:

- `docs/status/workflow-manifest.json` (canonical command manifest)
- `docs/generated/workflow-command-reference.md` (generated workflow commands)
- `docs/status/workflow-validation-summary.json` (machine-readable command validation summary)

## Choose a validation path

| Task | Authoritative guidance |
| --- | --- |
| Prepare a pull request | [Engineering build/test/run](../engineering/README.md#buildtestrun); run `bash scripts/ci.sh` from the repository root |
| Understand required hosted checks | [CI/CD ownership](../engineering/ci-cd-optimization.md); `Meridian CI` owns the four local gate lanes and the hosted integration companion |
| Validate a WPF change | [Desktop testing guide](desktop-testing-guide.md), on Windows |
| Run a focused hosted check | [Workflow guide](../../.github/workflows/README.md), including `Targeted Test` modes and inputs |
| Check packaging | [Deployment and packaging](../operators/deployment-packaging.md) and the `Publish Smoke` entry in the workflow guide |
| Review workflow edits | [GitHub Actions testing checklist](github-actions-testing.md) |

The legacy workflow named `CI` retains Secret Scan and nightly/manual coverage. It is not the
owner of the current four-lane quality gate. Use the workflow guide for exact triggers, artifacts,
retention, and publishing behavior instead of copying workflow steps into this supporting index.
