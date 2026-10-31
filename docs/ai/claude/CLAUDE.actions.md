# CLAUDE.actions.md - GitHub Actions & CI/CD Guide

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-30

Use this guide to select and interpret validation, not to maintain a second workflow inventory.
The [workflow guide](../../../.github/workflows/README.md) owns hosted lane behavior;
[Engineering](../../engineering/README.md) owns local prerequisites and reproduction commands.
Follow [AGENTS.md](../../../AGENTS.md) for the current contribution and review requirements.

## Workflow Inventory

| Surface | Current responsibility |
| --- | --- |
| `Meridian CI` (`meridian-ci.yml`) | Runs `verify-dotnet`, `verify-browser`, `verify-docs`, and `verify-workflows`; their stable aggregate is `quality-gate`. |
| `Meridian CI / integration-gate` | Separately reports service-backed PostgreSQL integration evidence from the shared `service-backed-integrations.yml` workflow. It is not a fifth local `scripts/ci.sh` lane. |
| Legacy `CI` (`ci.yml`) | Owns Secret Scan and nightly/manual coverage/scenario execution, not the canonical .NET/browser/docs lanes. |
| Documentation Automation | Validates documentation and runs maintained generation/freshness checks. Generated changes belong to their generators, not manual edits. |
| Windows Desktop Build / Targeted Test | Provide Windows desktop validation and whitelisted manually selected hosted checks. A targeted pass establishes only its selected scope. |
| Production Certification / release workflows | Produce separately scoped integration, recovery, packaging, and release evidence; ordinary development verification is not production certification. |

This is an ownership map, not the complete workflow or trigger list. Consult the workflow guide
and the actual YAML for event filters, inputs, required jobs, artifacts, and retention. Do not infer
that an untriggered or skipped job passed, or that a proposal to consolidate CI has changed the YAML.

## Common Tasks

### Validate CI Locally

Run from the repository root. Select the narrowest meaningful check for the change, and expand
according to the current repository policy and risk. The maintained entrypoint is
[`scripts/ci.sh`](../../../scripts/ci.sh), not a copied sequence of restore/build/npm commands:

```bash
bash scripts/ci.sh --lane verify-docs
# Other focused lanes: verify-dotnet, verify-browser, verify-workflows
# Full local aggregate, when applicable or required:
bash scripts/ci.sh
```

`verify-fast` combines .NET and browser checks only; it is not the full gate. The default
`quality-gate` covers all four local lanes. Shared prerequisite checks run before the selected
work. Use the SDK/tool versions and pinned requirements documented in Engineering, rather than
installing a different runtime merely to satisfy an old example.

The local entrypoint defaults `MERIDIAN_DISABLE_DOCKER_TESTS` to `true` unless already set.
A local aggregate pass therefore does not replace hosted PostgreSQL integration evidence.
Record the actual command, tested commit, result, and missing prerequisites. An unavailable or
unattempted full gate must be reported as such, not as a pass.

### Validate Desktop Locally

Use the maintained Windows commands in Engineering or the hosted Windows/Targeted Test workflow.
The browser and WPF workstations are both active product surfaces. A portable .NET build or a
browser test does not prove native WPF behavior, packaging, or installer operation.

### Validate Workflow Hygiene

```bash
bash scripts/ci.sh --lane verify-workflows
```

This is broader than invoking the standalone hygiene script: the maintained lane includes its
prerequisites and associated validation. Editing this guide does not authorize workflow, required
check, permission, branch-protection, or release-policy changes.

## Standards

Keep code, configuration, tests, and generated outputs under their existing owners. Do not alter
checks or add skip directives to make a documentation PR green. Use synthetic fixtures for routine
development; live account credentials are not a prerequisite to documenting or testing synthetic
behavior. Never copy secrets or account data into logs, documentation, or public evidence.

## Troubleshooting

Read the failed step and retained logs before rerunning. Local lane summaries live under
`artifacts/ci-summary/`; the workflow guide describes hosted evidence. Use Engineering's
validation-status and build-lock recovery guidance before starting overlapping builds.

For hosted results, bind conclusions to the PR's latest head and the actual run attempt. A pass
on `main`, an earlier head, or another attempt is not proof for the current change. Distinguish
successful checks, failed checks, pending work, and checks that did not run. Retain any material
failure instead of assuming it is flaky. Keep owner review and merge decisions separate from CI.

## Related Documentation

- [Workflow guide](../../../.github/workflows/README.md)
- [Engineering entrypoint](../../engineering/README.md)
- [Documentation ownership](../../documentation-ownership.md)
- [Repository updater limitations](CLAUDE.repo-updater.md)
