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
npm ci --no-fund --no-audit
python -m pip install --requirement build/scripts/docs/requirements.txt
python build/scripts/run-maintained-generation.py
python build/scripts/run-maintained-generation.py --report artifacts/maintained-generation/second-run.json
git diff --check
git status --short
```

The runner generates the browser contract mirrors before building the workstation, then renders
adapter readiness and roadmap/source docs. It discovers the remaining generation commands from
the documentation workflow's `regenerate-docs` job, including Mermaid, WPF, UML, workflow overview,
and workflow manifest outputs. Dependency installation and CI comparison steps remain separate.
Use `--dry-run` to inspect the discovered commands without changing files.

The complete sequence repeats until file contents stop changing, with a default limit of three
passes (`--max-passes 1..10`). This accounts for later generators changing inputs to earlier
reports. A required step failure, protected-file change, or failure to converge exits nonzero.
The second invocation should converge in one pass with empty `changed_outputs`. JSON reports and
per-step logs live under ignored `artifacts/maintained-generation/`; reports list added, modified,
and deleted outputs, failed commands, and each pass's changes. UML rendering remains advisory,
as in the workflow: its failures are reported and it runs once per invocation. Docker and Bash
are needed for that optional step.

The output policy in `build/scripts/maintained-generation-outputs.json` authorizes exact generated
files and the workstation output tree. Registered source READMEs and the two workflow-reference
docs permit only their named generated blocks; malformed markers fail before generation.
Handwritten content and reviewed baselines are compared with the invocation-start bytes, including
any existing local edits. Unauthorized changes are restored and reported. Run this command with
exclusive access to the checkout: concurrent edits cannot be distinguished from generator writes.
This is a local guard for reviewed generators, not a sandbox for untrusted code or an automatic
commit/push service. Ignored caches and operational artifacts are outside the comparison.

The Vite build empties `src/Meridian.Ui/wwwroot/workstation/` before emitting the new bundle, so
removed hashes appear as deleted outputs. The runner does not refresh the reviewed source hash
baseline, rewrite unmarked prose, enable the operational pilot-readiness report, or create issues.
If a new generator emits an unlisted path, review its ownership and update the output policy.

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
