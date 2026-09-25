# Regenerate tracked output after a merge

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-09-23

Use this procedure when merging current `origin/main` into a feature branch leaves conflicts in
the tracked browser bundle or generated documentation. The canonical workstation tree remains
tracked, as required by PRD-013 and PRD-018. Rebuild it from the merged dashboard source.

## Resolve source, then regenerate output

Start with a clean worktree and index. Commit or separately preserve any unfinished work before
starting; the recovery command replaces conflicted generated files in both the index and worktree.
Run it before manually editing those generated conflicts.

```powershell
git status --short
git fetch origin main
git merge --no-commit --no-ff origin/main
python build/scripts/resolve-generated-merge-conflicts.py
python build/scripts/resolve-generated-merge-conflicts.py --apply
git diff --name-only --diff-filter=U
```

The command previews by default and accepts only a merge whose single incoming parent matches
`origin/main`. It uses that pinned commit, including its deletions, to seed the known generated
conflicts. `--main-ref` supports a differently named main ref. It does not support rebases.
Exit status 1 means conflicts still need review; 2 means a precondition or Git operation failed.
Exit status 0 is not proof of a fresh build or a completed merge.

On an older branch where the script has not landed yet, invoke a reviewed copy from another
checkout and pass `--repo <feature-checkout>`. Avoid running a conflicted copy of the script.

The fixed allowlist covers the workstation output tree and named whole-file documentation
outputs. Every other conflicted path remains unresolved for review. In particular:

- `src/*/README.md` contains hand-written prose as well as generated blocks. Preserve and merge
  the prose, then let the source renderer replace its generated blocks.
- `docs/HELP.md`, documentation automation guidance, diagram sources, roadmap registries, and
  unknown files under generated/status directories require review.
- `docs/source/generated/source-hash-manifest.json` is a reviewed source/documentation baseline.
  Follow the [source documentation workflow](../source/README.md); never accept new hashes merely
  to clear the merge.
- Database manifests and diagram images need their owning schema/diagram generation lanes.

Resolve all remaining source and test conflicts before running generators. Keep new assertions
from main when integrating older fixtures; a branch's old setup is not authority to remove later
regression coverage. Confirm `git diff --name-only --diff-filter=U` is empty.

```powershell
npm --prefix src/Meridian.Ui/dashboard ci --include=optional
npm --prefix src/Meridian.Ui/dashboard run build
python -m pip install --requirement build/scripts/docs/requirements.txt
python build/scripts/docs/render-roadmap-docs.py --summary
python build/scripts/docs/render-source-docs.py --summary
python build/scripts/docs/run-docs-automation.py --profile core --summary-output docs/status/docs-automation-summary.md --json-output docs/status/docs-automation-summary.json
python build/scripts/docs/generate-structure-docs.py --workflows-only
git diff --check
git status --short
```

Review and stage the complete regenerated output, including removed hashed assets, then inspect
`git diff --cached`. Run the applicable validation lanes and `bash scripts/ci.sh`, commit the merge,
and push the feature branch. The hosted `quality-gate` and documentation drift checks must pass
on the final head. Source README/hash changes still need their existing review and validation.
The full diagram sequence is documented in [the documentation workflow](../../.github/workflows/documentation.yml).

## Why an explicit command

A custom `.gitattributes` merge driver depends on local Git configuration and does not configure
GitHub's merge environment. It also cannot prove that a parent's assets match the merged source.
This command makes local conflict resolution repeatable and leaves regeneration and the existing
freshness checks mandatory. It does not automatically drain PRs or repair a synthetic merge-queue
tree; see the [automation design constraints](docs-regeneration-automation-design.md).
