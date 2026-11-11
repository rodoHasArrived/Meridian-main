# Meridian — Architecture

How the package is put together, the dependency rules that keep it modular, and the update playbook. Companion to `../CONTRIBUTING.md` (checklists) and `../guidelines/EXTENDING.md` (extension recipes).

## The compile model

The design system is a **portable source tree with a checked-in browser runtime**. The historical package ships its generated monolith but not the original whole-tree compiler. Release 3.1 therefore keeps legacy runtime output intact and adds a reproducible, idempotent extension compiler for the new source modules:

```bash
node scripts/compile_bundle_extensions.mjs
```

The compiler transpiles the explicit module list in `scripts/bundle_extensions.json`, resolves internal imports through the existing namespace, replaces one marked extension block, updates source hashes and public exports, removes accidental fixture exports, and synchronizes touched token metadata. Running it twice produces one extension block and the same inventory.

| Generated file | What it is |
| --- | --- |
| `_ds_bundle.js` | Legacy runtime plus the marked 3.1 extension block, exposed as `window.MeridianDesignSystem_205d17` |
| `_ds_manifest.json` | The census: namespace, components, cards, templates, tokens, fonts |
| `_adherence.oxlintrc.json` | Component and token registry consuming projects use to enforce adherence |

Card discovery is generated separately from source metadata:

```bash
python scripts/sync_catalog.py --write
python scripts/generate_inventory.py --write
python scripts/generate_families.py --write
python scripts/release_identity.py --write
```

The first makes every `@dsCard` specimen visible in the gallery and manifest. The second makes the README census match the compiled component manifest. The third assigns every public export to one of the nine families and refuses an unclassified export, a phantom barrel name, or a symbol claimed twice. The fourth renders every human-readable version statement and typed count in the package from `release.json` + `_ds_manifest.json` + `api/families.json` — **no version and no count is typed by hand.** Also generated per screen folder are `support.js` and `ds-base.js` from the canonical scaffold.

**Counts live in the manifest; identity lives in `release.json`.** Docs read `_ds_manifest.json` instead of restating numbers (see `index.html`, `docs/system-map.card.html`), and the marked blocks in `readme.md` and `docs/METRICS.md` are rendered, not written. Hardcoded counts drift: a family table summed to 284 under a sentence claiming it covered 303, and the package called itself production-ready 3.5.0 in one file and a 4.2.0 tree in another.

## The layer model

```
L0  tokens/        styles.css closure — the only place colors/type/spacing are defined
L1  core/          primitives & layout (Button, Input, Dialog, Grid, hooks carriers)
L2  data/ charts/  tables & visualizations — compose core
L3  accounting/ trading/ operations/ decision/   domain surfaces — compose core + L2
L4  shell/         workstation chrome (topbar, nav rail, status bar, WorkstationShell)
L5  templates/     27 screens — 7 archetypes + Meridian's 20 production screens.
                   Every one consumes ONLY the compiled bundle + styles.css
```

**Dependency rule: imports point down, never up or sideways.** `charts/` may import from `core/`, never from `data/` or `trading/`. Domain folders (L3) may use L1–L2. Templates and the `templates/` tier sit outside the bundle — they reach components only through `window.MeridianDesignSystem_205d17`, so they exercise exactly what a consumer gets.

Shared *logic* lives in lowercase helper modules next to their domain (`accounting/money.js`, `core/useOverlayFocus.js`) and is surfaced on the namespace through **carrier objects** (`Money`, `TableHooks`, `FormHooks`, `ChartCursor`) — lowercase exports are bundle-internal by design.

## Anatomy of a component

Every component is a **file trio plus a gallery specimen**, colocated in its domain folder:

```
components/<domain>/
  Name.d.ts        public API contract — typed props, doc comments (the source of truth)
  Name.jsx         implementation — tokens only, var(--token, #hex) fallbacks
  Name.prompt.md   usage brief — the example + non-obvious behavior, for devs and AI tools
  <domain>.card.html   @dsCard specimen(s) rendering the domain's components live
```

Conventions the compiler and checks rely on:

- **PascalCase file = exported symbol.** Compound modules export siblings (`Dialog.jsx` → `Dialog` + `DialogHeader` + `DialogBody` + `DialogFooter` + `DialogForm`). A `.d.ts` without a sibling `.jsx` documents a compound export (e.g. `Validators` from `FormValidation.jsx`).
- **Styling comes from tokens.** Inline styles or injected scoped CSS, always `var(--token)` — with a hex fallback `var(--text-muted, #6B737B)` as the sanctioned pattern. No new hex literals (`scripts/check_design_system_governance.py` enforces; hex is legal only inside a `var()` fallback or a code comment — the baseline is empty and should stay that way).
- **Radii 2/4/6** — never above 8px (enforced).
- **`!important` is governed, not forbidden.** Win by order or specificity. Where neither can reach — a declaration that must beat an inline style, or a universal descendant rule under a captured pointer — state why on the declaration: `/* important: <reason> */`. Gate: `no-important` + conformance rule C-14.
- **One declaration per domain concept.** Object identity is `ObjectRef` / `MeridianObjectType` (`core/ObjectModel`); reversibility is `ReversibilityClass` (`operations/DecisionFlow`); time roles are `TIME_ROLES` (`core/TimeContext`). Re-declaring one forks the model — gate: C-15.
- **API vocabulary:** `open`/`onClose` for overlays, `value`/`onChange` for inputs, `variant`/`size` for chrome, `className`/`style`/`...rest` passthrough everywhere. Every prop optional where possible; missing data renders an honest empty state.

## Theming & tokens (L0)

`styles.css` is the single global entry — `@import` order is load-bearing (base colors → theme remap → dark overrides last). The 3-tier token model, white-label entry points (`data-brand`), density scopes (`data-theme-density`), and dark mode are specified in `../guidelines/TOKEN_REFERENCE.md`. Per-file map: `../tokens/README.md`.

## Quality gates

| Gate | Where | Catches |
| --- | --- | --- |
| Governance checker | `scripts/check_design_system_governance.py` | broken links, uploads refs, raw hex, oversize radii, template contract (viewport/main/h1), prompt coverage |
| Governance tests | `tests/test_design_system_governance.py` | the checker's own contract + the real tree staying clean |
| Contrast tests | `scripts/check_contrast.py`, `tests/test_contrast.py` | WCAG role pairs, semantic washes, chrome, action, selection, and white-label states |
| Card catalog | `scripts/sync_catalog.py` + `tests/gallery.html` | every `@dsCard` is discoverable and present in the visual-regression matrix |
| Inventory generator | `scripts/generate_inventory.py` | README census and namespace purity |
| Family record | `scripts/generate_families.py` | unclassified public exports, phantom barrel names, a symbol in two families, `api/families.json` drift |
| Release identity | `scripts/release_identity.py` | a typed version or count anywhere in `readme.md` / `docs/METRICS.md` disagreeing with `release.json` + the manifest |
| Smoke/unit | `tests/smoke.html`, `tests/unit-tests.html` | bundle loads, exports exist, behavior probes |
| Release validator | `scripts/validate_release.py` | complete static release gate in dependency order |

Pre-ship robustness gates for new components: `../guidelines/EXTENDING.md` §gates.

## Versioning

Semver in `../CHANGELOG.md` (MAJOR = breaking API/token removal · MINOR = additive · PATCH = fixes). Every consumer-visible change also lands a migration note in `UPGRADING.md`. Historical process reports live in `changelog/`.
