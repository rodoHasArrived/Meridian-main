# Repair generated output and merge conflicts

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-15

Use this procedure when merging current `origin/main` into a feature branch leaves conflicts in
the tracked browser bundle or generated documentation. The canonical workstation tree remains
tracked, as required by PRD-013 and PRD-018. Rebuild it from the merged dashboard source.

## Prepare a repair without changing your branch

Fetch the base and run the complete repair command from the repository root:

```powershell
git fetch origin main
python build/scripts/repair-generated.py --base-ref origin/main --head-ref HEAD --install-dependencies
```

The command pins both refs once, creates a disposable detached worktree, merges the pinned base,
resolves supported generated conflicts, reconciles reviewed module hashes, installs dependencies,
and runs maintained generation until stable. Your branch, index and local edits remain unchanged;
uncommitted edits are not inputs. It creates no commits and pushes nothing. Use reviewed tooling
locally: isolation prevents accidental edits to your checkout but is not a sandbox for generators.

`--repo` selects another checkout, `--output-dir` overrides `artifacts/generated-repair`, and
`--max-passes 1..10` changes the default three-pass convergence limit. Omit
`--install-dependencies` only when dependencies are already available to the disposable checkout;
`node_modules` in your original worktree are not copied there.

| Evidence | Meaning |
| --- | --- |
| `report.json` | Pinned head/base/merge-base, outcome, conflicts, module review decisions, changed paths and patch/tree digests. |
| `repair.patch` | On success, a binary-capable diff from the pinned head to the final repaired tree, including legitimate source changes merged from the base. |
| `generated.patch` | On success, a diagnostic delta from the deterministic merged tree to the regenerated result. The hosted writer independently reconstructs and validates this scope from `repair.patch`. |
| `dependencies.log`, `generation.log`, `generation-report.json`, `logs/` | Dependency and generator evidence when those stages ran. |

Exit 0 means a candidate is ready for review, not that required CI passed. Exit 1 means source or
hash review, dependency installation, or generation needs attention. Exit 2 means a precondition
or tool failure. Inspect `report.json` for the specific reason; refusal does not produce a
successful repair patch.

Review `repair.patch` against its recorded head. A patch represents file contents, not Git merge
parents: applying the full patch alone does not record that the branch merged its base. Use the
hosted writer for an eligible PR, or complete the explicit merge below and regenerate. Do not apply
stale evidence after either revision changes. Fork contributors use this same local procedure.

## Hosted PR repair

`Generated Repair` prepares read-only evidence on PR open, synchronize and reopen. A separate
`Generated Repair Publisher` validates it with trusted base tooling and can push a repair to an
eligible same-repository branch. The push uses a repository-scoped GitHub App so fresh required
checks run on the resulting revision. See [activation and configuration](ci-cd-optimization.md#generated-repair-and-maintenance-activation).

Hosted dependencies and generators execute inside a restricted Docker container containing a
disposable clone and raw output directory. Host runner credentials, caches, actions and the Docker
socket are excluded. Trusted host tooling exports only bounded regular files after the container
exits. The local repair command above does not provide this container boundary.

Main pushes, the daily 05:23 UTC scan and manual publisher dispatch discover same-repository PRs
that need refreshing against a new base. Each scan requests at most ten new head/base pairs,
skips previously requested pairs and reports the backlog. Daily/manual scans drain remaining work;
API pagination stops with an explicit error at its 1,000-item bound.

Forks and missing App configuration retain artifact-only behavior. Source/handwritten conflicts,
module review decisions, and generator/dependency/policy changes use contributor review. A failed
run's report explains the next step; a patch is available only when generation produced a valid
candidate. A moving head or base invalidates the candidate and allows at most two automatic
requeues. A repair commit suppresses its own synchronize loop only while it already includes the
current base; a new base can trigger another repair. No repair job approves or merges the PR,
bypasses freshness checks, or changes protected `main`.

## Complete an explicit merge, then regenerate

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
  When no source/prose conflict remains, the resolver can combine independent module entries and
  reuse parent entries that match the merged module. A module that matches neither reviewed parent
  requires the [source documentation workflow](../source/README.md); never create new review hashes
  merely to clear a merge.
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
`--docs-only` runs the documentation prerequisites and discovered documentation steps without
browser contract/build work; Documentation Automation uses it to converge its own outputs.
Use the default full mode for a browser/source merge repair.

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

The isolated repair command checks reviewed module hashes again after generation, because generated
README blocks participate in the existing fingerprint rules. For an explicit merge, use
`merge-reviewed-doc-hashes.py --ancestor <merge-base-sha> --head <pre-merge-head-sha>
--base <incoming-base-sha>` to inspect the same three-way decisions; add `--apply` only after a
successful result to stage the reconciled manifest. It never invents a new reviewed entry. A novel
combined module or conflicting manifest metadata requires review. Refresh lockfiles with their
package manager after resolving dependency inputs; they are outside generic generated recovery.

Review and stage the complete regenerated output, including removed hashed assets, then inspect
`git diff --cached`. Run the applicable validation lanes and `bash scripts/ci.sh`, commit the merge,
and push the feature branch. The hosted `quality-gate` and documentation drift checks must pass
on the final head. Source README/hash changes still need their existing review and validation.
The full diagram sequence is documented in [the documentation workflow](../../.github/workflows/documentation.yml).

## Merge queues and custom merge drivers

Both Meridian CI and Documentation Automation run freshness checks for merge groups. Their
synthetic trees cannot be patched in place. If a combination is stale, keep the check failing,
refresh affected PR branches and rebuild the group. Repeated combined-output conflicts may warrant
an administrator's review of one-PR groups. Per-PR repair does not eliminate this limitation.

A custom `.gitattributes` merge driver depends on local Git configuration and does not configure
GitHub's merge environment. It also cannot prove that a parent's assets match the merged source.
The explicit command makes local conflict resolution repeatable and leaves regeneration and the
existing freshness checks mandatory. See the
[automation design constraints](docs-regeneration-automation-design.md) for the trust boundary and
the remaining contributor cases.
