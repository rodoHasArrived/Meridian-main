# Historical AI Instructions Sync Workflow

This page preserves the behavior of the retired AI instructions sync workflow for traceability.
It is not an active operating runbook.

The workflow file that used to own this job was archived with the legacy GitHub Actions inventory
on 2026-05-18. Current AI guidance maintenance should use the local docs and inventory commands
listed below, then update the shared AI contract and nearest indexes in the same change.

## Current Maintenance Path

Use these local commands when changing AI instructions, prompt catalogs, agent definitions, skills,
or generated navigation:

```bash
python build/scripts/docs/check-ai-inventory.py --summary
python build/scripts/docs/run-docs-automation.py --profile quick --dry-run
python build/scripts/docs/generate-ai-navigation.py --json-output docs/ai/generated/repo-navigation.json --markdown-output docs/ai/generated/repo-navigation.md --recent-changes-output docs/ai/generated/recent-changes.md --summary
git diff --check
```

Use the narrowest subset that covers the touched files. For docs-only Copilot guidance changes,
`check-ai-inventory.py --summary` plus `git diff --check` is usually enough.

## AI Contract Coverage

- Repo navigation: [`../navigation/README.md`](../navigation/README.md), [`../generated/repo-navigation.md`](../generated/repo-navigation.md)
- Agent edit rules: keep rules in [`../assistant-workflow-contract.md`](../assistant-workflow-contract.md), avoid duplicating policy in retired workflow files
- Generated-file handling: this file is historical only; it should not be used to drive generated artifact edits. For generated outputs, run `generate-ai-navigation.py` and related generators in-band.
- Agent orchestration: when resurfacing this process, align with `../agent-handoff-checklist.md` and `../parallel-task-manifest-template.md` to prevent lane overlap
- Parallel development workflows: this historical index was single-lane; use modern manifest-based routing for multi-lane AI updates
- Token/context management: avoid loading full historical notes for current work; read one source of truth and the migration index only
- Validation procedures: `python build/scripts/docs/check-ai-inventory.py --summary`, `python build/scripts/docs/generate-ai-navigation.py ...`, `python build/scripts/docs/check-ai-contract-drift.py ...`
- Documentation ownership: [`../../documentation-ownership.md`](../../documentation-ownership.md)

## Historical Summary

The retired sync workflow kept assistant files such as `CLAUDE.md`, `docs/ai/`, and
`.github/agents/` synchronized with repository structure and generated navigation. It also had a
PR-creation fallback path for repositories where GitHub Actions could not create pull requests.

Historical behavior:

- Direct-commit mode updated AI files without requiring PR creation permissions.
- PR mode used an `automation/ai-instructions-sync` branch when repository settings allowed it.
- Dry-run mode generated a diff without committing.
- A separate README tree marker sync maintained broad tree snapshots.

Do not re-enable that workflow just to refresh AI docs. Prefer the local scripts above and the
provider-agnostic rules in [`../assistant-workflow-contract.md`](../assistant-workflow-contract.md).

## Related Files

| File | Purpose |
|------|---------|
| [`../../../archive/docs/workflows/legacy-github-actions-2026-05-18.md`](../../../archive/docs/workflows/legacy-github-actions-2026-05-18.md) | Archive inventory for retired GitHub Actions workflows |
| [`../README.md`](../README.md) | Master AI resource index |
| [`../../../CLAUDE.md`](../../../CLAUDE.md) | Root AI context document |
| [`instructions.md`](instructions.md) | Compact Copilot guide and routing links |
| [`../assistant-workflow-contract.md`](../assistant-workflow-contract.md) | Shared provider-agnostic AI workflow contract |

---

*Last Updated: 2026-05-20*
