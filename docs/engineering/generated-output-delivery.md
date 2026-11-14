# Generated output delivery

**Status:** active; CI governance changes require human review before merge
**Owner:** core-team
**Reviewed:** 2026-11-14

Commit authored inputs and the generated documentation that needs review. CI generates mechanical
inventories and compiler output at the tested revision; contributors do not commit those files.
The `delivery` section of `build/scripts/maintained-generation-outputs.json` is the exact path
inventory. Unlisted files remain reviewable; adding a delivery rule requires whole-output write
authority and cannot encompass handwritten blocks or reviewed baselines.

| Output | Delivery and reason |
| --- | --- |
| `docs/status/wpf-screen-development-tracker.{md,json}` | CI artifact; source/evidence projection, including changes from C# alone. |
| Other declared operational status reports, handoff packets, TODO scans and workflow validation reports | CI artifacts; mechanical inventories. Exact files are listed in `delivery.ci_files`, not a directory wildcard. |
| `database/manifest/contracts.json` | CI artifact; source catalog with file fingerprints. Schema-control still compares reviewed database docs, dependencies, physical manifests and policy findings. Structural diffs reconstruct the contract baseline from pinned base source. |
| `docs/{source,roadmap}/generated/MANIFEST.json` | CI artifacts; mechanical registry/output digests. |
| Dashboard `ui-api-routes.generated.ts` and `workspace-catalog.generated.ts` | Generated on npm installation and before development, build and validation commands; included in CI evidence. |
| `src/Meridian.Ui/wwwroot/workstation/` | Build artifact; browser lane validates emitted entrypoints/assets and supplies the same-run bundle to .NET before project evaluation. Packaging builds from source. The canonical served path is unchanged. |
| Actual source fingerprints | `artifacts/docs/source-hashes.json`; informational evidence, never acceptance of a review baseline. |
| `docs/source/generated/source-hash-manifest.json` | Committed reviewed source/README baseline; regeneration must never mint review evidence. Module-specific review and freshness checks remain. |
| `docs/status/doc-health-dashboard.{md,json}` | Committed reviewed health-score baseline; keeps the severe-regression comparison available. |
| Roadmap/source narrative, database docs, public references, diagrams and generated blocks in authored docs | Committed for review, with deterministic freshness checks. Generated content does not authorize changes to surrounding prose. |
| `docs/status/coverage-report.md` | Committed hybrid document; handwritten suffix remains protected. |

## One local command

```sh
python -m pip install --requirement build/scripts/docs/requirements.txt
npm ci --no-fund --no-audit
npm --prefix src/Meridian.Ui/dashboard ci --include=optional
python build/python/cli/buildctl.py regen --fix
```

Use `--docs-only` to omit browser builds. Without `--fix`, `regen` regenerates and then checks
committed freshness. Run generation with exclusive access to the checkout: its write guard cannot
distinguish concurrent user edits from unauthorized generator writes. Review the resulting diff;
CI outputs stay ignored. A second unchanged run should converge in one pass.

Install local hooks with `bash scripts/dev/install-git-hooks.sh` to run the same fix after merges.
The hook neither stages nor commits. Built-in binary merge attributes force conflicts on retained
reviewed snapshots instead of silently combining generated text. Resolve authored conflicts, use
the [explicit recovery procedure](generated-merge-recovery.md), then regenerate. GitHub does not
run local hooks; its canonical lanes regenerate the actual PR or merge-group revision.

## CI evidence and safeguards

The freshness checker exports current CI outputs under `artifacts/docs/generated/`, preserving
repository-relative paths. `outputs.json` records the checked-out revision and SHA-256 for each
exported file. Docs-only runs export only available outputs; the browser artifact separately
contains the built workstation. Required generators, tests, schema policy checks and convergence
must still succeed. Machine drift alone needs no follow-up commit; reviewed drift produces
`generated-repair.patch` and fails freshness. Force-adding a declared CI output also fails.

The existing Generated Repair Publisher remains a PR-only, isolated, provenance-checked service
for reviewable generated patches. It gains no default-branch write authority. CI-only outputs are
excluded from its publishable changes. Forks retain artifact-only repair. No job approves or
merges this governance change or changes repository rules.

Human governance review is required for the CI changes. Hosted required checks remain authoritative;
local tests establish behavior, not successful hosted activation or release certification.
