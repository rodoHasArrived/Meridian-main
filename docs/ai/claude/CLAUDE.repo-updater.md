# AI Repository Updater Guide

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-30

[`build/scripts/ai-repo-updater.py`](../../../build/scripts/ai-repo-updater.py) is a diagnostic
assistant, not the repository's acceptance gate. Its findings, exit codes, and limited verification
must not be presented as proof that the entire repository is correct or production-ready.

## Overview

The script offers heuristic audits, JSON/Markdown reports, known-error lookup, a working-tree diff
summary, and legacy verification/maintenance commands. Start with the relevant source and
[documentation ownership contract](../../documentation-ownership.md); use an audit to identify
candidates, then establish whether each finding actually applies.

An old review date alone does not make a document wrong. Conversely, a new date or warning banner
does not repair an invalid command, invented API, or contradicted persistence instruction.

## Workflow

### Step 1 — Audit

Run from the repository root, keeping transient results outside maintained documentation:

```bash
python3 build/scripts/ai-repo-updater.py audit-docs --json-output artifacts/documentation-audit/findings.json
python3 build/scripts/ai-repo-updater.py audit-ai-docs --summary
python3 build/scripts/ai-repo-updater.py known-errors
```

Audits return `summary`, `improvement_plan`, and individual `findings`. **A completed audit returns
exit code 0 even when its report contains critical findings.** Inspect the report, including
warnings about nested auditor failures; successful process execution is not a clean audit.

### Step 2 — Review Known Errors

Use `known-errors` to inspect recorded mistakes before selecting a change. Re-read the current
source and tests rather than treating an earlier error entry as proof that the same defect remains.

### Step 3 — Fix

Verify each proposed `fix_hint` against the current implementation and repository policy. The
provider auditor is a heuristic over a limited interface list; an attribute suggestion does not
replace the descriptor/factory registration contract. See the
[provider implementation guide](../../development/provider-implementation.md).

Keep unrelated work intact, change only the affected owner documents, and preserve historical
evidence as historical. Do not modify generated output by hand or change source behavior merely
to silence a heuristic. A class-name-based missing-test finding is not a measured coverage result.

### Step 4 — Verify

Use the maintained lanes and focused checks in [Engineering](../../engineering/README.md):

```bash
bash scripts/ci.sh --lane verify-docs
# Full local aggregate, when applicable or required:
bash scripts/ci.sh
```

The updater's `verify` command remains available, but its implementation runs only:

| Step | Actual command |
| --- | --- |
| Build | `dotnet build -c Release --nologo -v quiet` |
| Test | `dotnet test tests/Meridian.Tests -c Release --nologo -v quiet --no-build` |
| Lint | `dotnet format --verify-no-changes --no-restore` |

Each subprocess has a 300-second timeout. After shared preflight, `verify` returns 0 for its own
successful steps or 1 for failure; blocked preflight returns 2. Its `overall_ok` does **not** cover
the complete test-project roster, browser lane, documentation lane, workflow lane, native desktop
validation, or service-backed integrations. Do not substitute it for a required hosted check.

### Step 5 — Generate Report

```bash
python3 build/scripts/ai-repo-updater.py report --output artifacts/documentation-audit/report.md
```

An explicit output avoids the command's default `docs/generated/improvement-report.md` destination.
Reports are observations, not approvals. Retain the inspected revision and distinguish actual
source findings, suggested work, checks executed, and checks not run.

### Step 6 — Summarise Changes

```bash
python3 build/scripts/ai-repo-updater.py diff-summary
```

This summarizes uncommitted working-tree changes. Review the complete PR diff as well; a clean
working tree says nothing about the correctness of already committed changes.

## Commands Reference

| Commands | Scope |
| --- | --- |
| `audit` | Combines the code, docs, tests, config, provider, and AI-documentation auditors. |
| `audit-code`, `audit-docs`, `audit-tests`, `audit-config`, `audit-providers`, `audit-ai-docs` | Select one diagnostic area; read findings rather than only the exit status. |
| `known-errors`, `diff-summary` | Inspect recorded errors or uncommitted changes. |
| `report` | Runs the combined audit and writes Markdown; explicit output is recommended. |
| `verify` | Limited legacy build/test/format sequence described above. |
| `maintenance-light`, `maintenance-full` | Execute maintenance steps and emit status artifacts; inspect their individual steps and resulting working-tree changes. |

## Common Flags

`--root`/`-r`, `--output`/`-o`, `--json-output`/`-j`, and `--summary`/`-s` are supported.
Maintenance additionally accepts `--status-file` and `--summary-file`; its default outputs are
`.ai/maintenance-status.json` and `.ai/MAINTENANCE_STATUS.md`. Use `--help` on the checked-out
script before automating arguments. Relative output paths are safest when invoked from the repo root.

## What Each Auditor Checks

These are text/pattern-based diagnostics, not a compiler, authorization review, accounting proof,
or live-provider certification. Review false positives and scope omissions before changing code.
In particular, the maintenance helper still contains a missing-SDK recommendation naming .NET 9;
use the repository's `global.json` and Engineering prerequisites instead of that stale hint.
This guide documents the limitation; it does not claim the helper was repaired.

## Example Session

A useful audit closes a concrete discrepancy: identify the incorrect claim, read its source owner,
replace the active guidance, run the applicable focused check, and report the new PR head and
remaining limitations. Repeatedly running audits until every heuristic is silent is not the goal.

## Integration with Makefile

Make targets are optional wrappers. Prefer the direct script and maintained lane entrypoints above;
do not assume a wrapper exists or represents the full gate without checking the current Makefile.

## Related Resources

- [Repository policy](../../../AGENTS.md)
- [CI ownership and validation](CLAUDE.actions.md)
- [Engineering](../../engineering/README.md)
- [Documentation ownership](../../documentation-ownership.md)
