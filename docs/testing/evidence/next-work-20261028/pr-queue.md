# Live PR queue and generated recovery — 2026-10-28

**Observed:** 2026-10-28 22:36:15 UTC. This is engineering evidence, not acceptance or a merge decision.
**Mode:** Standard. Integration owner: primary credential-ownership task. This lane owns queue evidence only.

The live GitHub connector reports **one open PR**, [#3319](https://github.com/rodoHasArrived/Meridian-main/pull/3319),
Security Master schedule writes and OTC lifecycle events. The October 27 queue of 46 PRs and its
452 conflict messages are historical. No currently open PR has tracked workstation-bundle conflicts.

The observed base is `a5b2589f0395d054bec0b59672a990419ef237ed`; #3319's head is
`fb3de843e460260130fb3dfae7780d5000323822`. Both refs were rechecked with `git ls-remote`
after recovery and were unchanged. The merge base is `3a624fc3b3b56181af2601cd82224287300b238c`.

`git merge-tree --write-tree --name-only` reports **22 conflict messages and 22 conflicted paths**.
The existing resolver's maintained policy classifies them as follows:

| Strategy | Paths | Result |
| --- | ---: | --- |
| regenerate | 3 | Resolved in the disposable merge: repository structure and both doc-health reports. |
| protected-suffix | 1 | Coverage report refused: its protected marker is missing or duplicated in a merge parent. |
| reviewed-hashes | 1 | Reviewed source manifest retained for review while other conflicts remain. |
| manual | 17 | Fourteen database manifest/catalog/diagram outputs and three Security Master source files. |

The isolated repair used the existing trusted resolver and policy from local revision
`3a624fc3b3b56181af2601cd82224287300b238c`:

```bash
git fetch origin main
git fetch origin refs/pull/3319/head:refs/remotes/pr/3319
git merge-tree --write-tree --name-only a5b2589f0395d054bec0b59672a990419ef237ed refs/remotes/pr/3319
python build/scripts/repair-generated.py --base-ref a5b2589f0395d054bec0b59672a990419ef237ed --head-ref refs/remotes/pr/3319 --output-dir artifacts/next-work-20261028/pr-3319-generated-repair --install-dependencies
```

The repair exited **1**, `review_required`, after resolving the three authorized generated conflicts.
Nineteen paths still require review. Dependencies and generation were not run because the merged
source tree was unresolved. No successful repair patch, commit, branch update, or publication was
produced. The disposable worktree was removed by the existing repair command. The caller's checkout
was unchanged by recovery.

The remaining source conflicts are `SecurityMasterService.cs`, `AssetClassValidatorRegistry.cs`,
and `SecurityAssetClassCatalog.cs`. A contributor must integrate their financial semantics, resolve
the coverage prose/marker issue, use the owning schema generation lane for database outputs, and
reconcile reviewed source hashes. Then rerun the isolated repair and required validation. Replacing
these paths with one parent's content is outside the resolver's authority.

Evidence retained:

- [Path classifications and measured refs](pr-queue-conflicts.json).
- [Existing repair command report](pr-3319-generated-repair-report.json).
- [Resolver and repair output](pr-3319-generated-repair.txt).

The GitHub CLI's queue query was denied by the environment's network policy (`api.github.com`);
the connected GitHub tool supplied the live queue, while permitted Git transport supplied exact refs.
No CLI authentication change or proxy bypass was attempted. Requery the queue and repin refs if
either base/head changes before repair.
