# `scripts/` — restored offline tooling (3.5)

Self-contained, dependency-free (Python 3.9+ stdlib), no network. These
restore the tooling `CONTRIBUTING.md` documents; third-party notices: `../THIRD_PARTY_NOTICES.md`.

| Script | What it does |
| --- | --- |
| `generate_inventory.py` | Regenerates the readme census between `inventory:generated` markers from `_ds_manifest.json`. `--write` / `--check`. Idempotent. |
| `check_design_system_governance.py` | Static gates: component trio pairing, raw-hex, `!important`, manifest/source parity, template contract, broken routes, hard-coded ARIA references, generated files present. |
| `sync_catalog.py` | Generates `guidelines/CARD_INDEX.md` from every `@dsCard` marker. `--write` / `--check`. |
| `sync_template_scaffold.py` | Enforces byte-identical `ds-base.js`/`support.js` from `templates/_scaffold/`. `--write` / `--check`. |
| `compile_bundle_extensions.py` | Validates `bundle_extensions.json` (sources exist, `.d.ts` pairs, exports real, manifest carries them). The design-system compiler performs the actual fold into `_ds_bundle.js` / `_ds_manifest.json` / `_adherence.oxlintrc.json` idempotently — those three are generated, never hand-edited. |
| `validate_release.py` | Orchestrates every gate above; one PASS/FAIL verdict. Browser/NVDA checks are deliberately deferred to `tests/BROWSER_TEST_PLAN.md`, never claimed here. |
| `codemod_4_0.py` | Consumer migration to 4.0: renames the deleted table renderers to `DataGrid`, remaps merged template paths to survivor + `view` param, flags what needs a human (Glyph*, `CONDITIONS`, `aggregateRows`, expansion/filter props). Dry-run by default; `--write` applies. Prose: `docs/UPGRADING.md` § → 4.0. |

Runtime unit tests (schemas, lifecycle, hashing, invalidation, metrics, adapters, persistence,
migrations, URL identity) live in `tests/unit.html` — open it in any browser, offline.
