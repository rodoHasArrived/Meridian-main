# `scripts/` — package build and validation

The Python gates use the standard library and run offline. The reproducible
bundle build uses the Node dependencies pinned in `../package-lock.json`; install
them with `npm ci` from the package directory. See
[`docs/EXPORT_4_3_0.md`](../docs/EXPORT_4_3_0.md) for the complete handoff and
[`THIRD_PARTY_NOTICES.md`](../THIRD_PARTY_NOTICES.md) for dependency notices.

| Script | What it does |
| --- | --- |
| `build_package.mjs` | Rebuilds the exact manifest API into `_ds_bundle.js`, builds the separate local preview React runtime, and records source/asset hashes in `release-build.json`. `npm run build` / `npm run build:check`. |
| `export_release.py` | Verifies the build and curated payload, then emits a deterministic ZIP, archive checksum, internal per-file inventory, and `SHA256SUMS`. `--check` validates without exporting. |
| `generate_inventory.py` | Regenerates the readme census between `inventory:generated` markers from `_ds_manifest.json`. `--write` / `--check`. Idempotent. |
| `generate_families.py` | Generates `api/families.json` and the marked counts/membership section of `api/README.md` from the manifest and family barrels. `--check` rejects unclassified, phantom, duplicate, or drifted assignments/docs. |
| `release_identity.py` | Renders canonical identity/census blocks in `README.md`, `docs/METRICS.md`, and the package landing page from release/manifest/family records. `--write` / `--check`. |
| `check_design_system_governance.py` | Static gates: component trio pairing, raw-hex, `!important`, manifest/source parity, template contract, broken routes, hard-coded ARIA references, generated files present. |
| `sync_catalog.py` | Generates `guidelines/CARD_INDEX.md` from every `@dsCard` marker. `--write` / `--check`. |
| `sync_template_scaffold.py` | Enforces byte-identical `ds-base.js`/`support.js` from `templates/_scaffold/`. `--write` / `--check`. |
| `compile_bundle_extensions.py` | Validates `bundle_extensions.json` (sources exist, `.d.ts` pairs, exports real, manifest carries them). This is a compile-plan check; it does not rebuild the bundle. Use the package build for current source output. |
| `validate_release.py` | Orchestrates every gate above; one PASS/FAIL verdict. Browser/NVDA checks are deliberately deferred to `tests/BROWSER_TEST_PLAN.md`, never claimed here. |
| `codemod_4_0.py` | Consumer migration to 4.0: renames the deleted table renderers to `DataGrid`, remaps merged template paths to survivor + `view` param, flags what needs a human (Glyph*, `CONDITIONS`, `aggregateRows`, expansion/filter props). Dry-run by default; `--write` applies. Prose: `docs/UPGRADING.md` § → 4.0. |

Runtime unit tests (schemas, lifecycle, hashing, invalidation, metrics, adapters, persistence,
migrations, URL identity) live in `tests/unit.html` — open it in any browser, offline.

Current agency regression checks run through `npm run test:agency`. Browser test
results must state the exercised pages, interactions, viewport sizes, and any
unrun assistive-technology/browser portions. A Python static gate is not a
browser execution record.
