# AI Instructions Sync Workflow

This document covers the AI instruction sync mechanism — the GitHub Actions workflow that keeps AI
assistant files (`CLAUDE.md`, `docs/ai/`, `.github/agents/`) up to date as the codebase evolves.
The compact Copilot guide intentionally links to generated navigation instead of embedding a full
repository tree.

---

## Fix Summary (2026-02-05)

**Workflow:** `documentation.yml` (AI Instructions Sync job)
**Issue resolved:** `GitHub Actions is not permitted to create or approve pull requests`
**Status:** ✅ Fixed with graceful fallback

### What Was Changed

1. **Added Graceful Fallback Mechanism**
   - `continue-on-error: true` on the PR creation step
   - Fallback step commits directly if PR creation fails
   - Workflow completes successfully in both scenarios

2. **Enhanced User Communication**
   - Header comments explaining repository settings requirement
   - Workflow summary shows PR creation status
   - Warning and notice messages guide users to enable PR creation

### How It Works Now

#### Default Behavior (Direct Commit)
When `create_pr` is **not** checked:
- Changes are committed directly to the main branch
- No special repository settings required
- ✅ Works out of the box

#### PR Creation Path
When `create_pr` **is** checked:

| Repository setting | Outcome |
|--------------------|---------|
| PR creation **disabled** | Fallback commits directly; warning logged |
| PR creation **enabled** | PR created on `automation/ai-instructions-sync` |

### How to Enable PR Creation (Optional)

1. Go to repository **Settings → Actions → General**
2. Scroll to **Workflow permissions**
3. Check ☑️ **"Allow GitHub Actions to create and approve pull requests"**
4. Click **Save**

For organization repositories, the setting must be enabled at **both** the organization and repository level.

---

## Testing the Sync Workflow

### Scenario 1: Direct Commit (Default)

```
Actions > AI Instructions Sync > Run workflow  (leave create_pr unchecked)
```

**Expected:** Workflow completes, changes committed to main, summary shows `Create PR mode: no`.

### Scenario 2: PR Creation (Fallback Path)

Ensure **"Allow GitHub Actions to create and approve pull requests"** is **disabled**, then:

```
Actions > AI Instructions Sync > Run workflow  (check create_pr)
```

**Expected:** PR creation step shows `continued on error`; fallback commits directly; summary shows
`PR Creation: ⚠️ Failed (fell back to direct commit)`.

### Scenario 3: PR Creation (Happy Path)

Enable PR creation in repository settings (see above), then:

```
Actions > AI Instructions Sync > Run workflow  (check create_pr)
```

**Expected:** PR created on `automation/ai-instructions-sync`; summary shows `PR Creation: ✅ Success`.

### Scenario 4: Dry Run

```
Actions > AI Instructions Sync > Run workflow  (check dry_run)
```

**Expected:** No commits or PRs created; diff shown in logs; summary shows `Dry run: yes`.

### Verification Checklist

```bash
# View recent commits
git log --oneline -5

# Check if AI files were updated
git log --oneline -1 -- CLAUDE.md docs/ai/copilot/instructions.md .github/agents/documentation-agent.md

# View repository structure section in CLAUDE.md
grep -A 50 "## Repository Structure" CLAUDE.md
```

---

## Troubleshooting

| Issue | Fix |
|-------|-----|
| Workflow still fails | Verify "Read and write permissions" are set under Workflow permissions |
| Files not updated | Check `build/scripts/docs/` scripts are executable; look for errors in "Generate repository structure" step |
| PR creation fails with setting enabled | Wait a few minutes (cache refresh); check organization-level settings |

---

## Schedule

The sync workflow runs automatically:
- **Trigger:** Every Monday at 03:00 UTC
- **Default behavior:** Direct commit (no PR)
- **Purpose:** Keeps AI instruction files synchronized with repository structure and generated
  navigation without duplicating tree snapshots in the compact Copilot guide

Manual runs can be triggered at any time via `workflow_dispatch` with custom options.

---

## Related Files

| File | Purpose |
|------|---------|
| `.github/workflows/documentation.yml` | Workflow definition (AI Instructions Sync job) |
| `.github/workflows/readme-tree.yml` | Keeps README/AI markdown tree markers synchronized with the live repository layout |
| `docs/ai/README.md` | Master AI resource index |
| `CLAUDE.md` | Root AI context document |
| `docs/ai/copilot/instructions.md` | Compact Copilot guide and routing links; no embedded repository tree |

## README Tree Marker Sync

Repository tree snapshots embedded in markdown are now maintained by a separate workflow:

- **Workflow:** `readme-tree.yml`
- **Trigger:** Every push to `main` and manual dispatch
- **Action:** `RavelloH/readme-tree`
- **Managed files:** `README.md`, `docs/ai/README.md`, `docs/ai/claude/CLAUDE.structure.md`

To opt a markdown document into automatic tree updates, add:

```md
<!-- readme-tree start -->
<!-- readme-tree end -->
```

Do not apply repository-structure block sync to `docs/ai/copilot/instructions.md`; use
`docs/ai/generated/repo-navigation.md`, `docs/ai/generated/repo-navigation.json`, or
`docs/generated/repository-structure.md` when Copilot needs layout context.

---

*Last Updated: 2026-03-20*
