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
python build/scripts/docs/generate-ai-navigation.py --json-output docs/ai/generated/repo-navigation.json --markdown-output docs/ai/generated/repo-navigation.md --summary
git diff --check
```

Use the narrowest subset that covers the touched files. For docs-only Copilot guidance changes,
`check-ai-inventory.py --summary` plus `git diff --check` is usually enough.

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
| [`../../../docs/archive/workflows/legacy-github-actions-2026-05-18.md`](../../../docs/archive/workflows/legacy-github-actions-2026-05-18.md) | Archive inventory for retired GitHub Actions workflows |
| [`../README.md`](../README.md) | Master AI resource index |
| [`../../../CLAUDE.md`](../../../CLAUDE.md) | Root AI context document |
| [`instructions.md`](instructions.md) | Compact Copilot guide and routing links |
| [`../assistant-workflow-contract.md`](../assistant-workflow-contract.md) | Shared provider-agnostic AI workflow contract |

---

*Last Updated: 2026-05-20*
