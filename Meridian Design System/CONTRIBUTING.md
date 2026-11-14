# Contributing to Meridian

The 60-second orientation, then a checklist per change type. Architecture and rationale: `docs/ARCHITECTURE.md`. Extension recipes with full code: `guidelines/EXTENDING.md`.

## Where things live

```
styles.css          global CSS entry — @imports everything in tokens/
tokens/             color/type/spacing/elevation/theme/dark/print (L0)
components/         core · data · charts · accounting · trading · operations · shell
templates/          26 copy-and-edit screens — 7 archetypes (one reference per group)
                    + Meridian's 19 production screens (+ _strategy-executor/ shared store)
guidelines/         design references — visual, content, tokens, a11y, patterns, brand
docs/               developer docs — architecture, getting started, cookbook, perf, upgrading
assets/             brand marks + the 24px line-icon set
scripts/            governance checker + baseline
tests/              governance/contrast tests, gallery, smoke, unit probes
_ds_*.{js,json}     GENERATED — never hand-edit
```

## Add a component

1. **Right domain?** `core` (generic) · `data` (tables) · `charts` · `accounting` · `trading` · `operations` (readiness/audit) · `shell` (chrome). Imports may point **down** the layers only (`docs/ARCHITECTURE.md`).
2. **Check it doesn't exist** — `readme.md` inventory, then `guidelines/PATTERNS.md` (often the answer is composition, not a new component).
3. Create the trio in `components/<domain>/`:

```jsx
// Name.jsx — tokens only, honest empty states, className/style/rest passthrough
import React from "react";
export function Name({ value, onChange, className = "", style = {}, ...rest }) {
  return (
    <div className={className} style={{ fontFamily: "var(--font-body)", color: "var(--text-primary, #20252B)", ...style }} {...rest}>
      {/* … */}
    </div>
  );
}
```

- `Name.d.ts` — typed props with doc comments (this is the public contract; write it like `Money.d.ts`).
- `Name.prompt.md` — a working snippet + the non-obvious behavior. Required (`prompt-coverage` gate).

4. **Specimen** — add it to the domain's `<domain>.card.html` or create a focused `*.card.html` with an `@dsCard` first-line comment. Run `python scripts/sync_catalog.py --write`; never hand-maintain the gallery list.
5. **Rules:** tokens via `var(--token, #hex-fallback)` — no bare hex (`raw-hex` gate) · radii ≤ 8px · graphite = primary action, terracotta = brand/scenario signal, blue = focus/selection, semantics use redundant labels and alpha washes · data conditions (`guidelines/STATES_AND_ANTIPATTERNS.md`) · keyboard + ARIA (`guidelines/ACCESSIBILITY.md`).
6. **Compile and document:** add the module path to `scripts/bundle_extensions.json`, run `node scripts/compile_bundle_extensions.mjs`, then `python scripts/generate_inventory.py --write`. Add the behavior to `CHANGELOG.md` and `docs/UPGRADING.md` only when consumers must act.

## Add or change a token

1. Author the value in the right file (`tokens/README.md` has the map) — usually `colors.css` **and its dark counterpart** in `colors-dark.css`.
2. Tier it correctly (`guidelines/TOKEN_REFERENCE.md`): Tier 1 = white-label surface, Tier 2 = semantic roles, Tier 3 = component-internal.
3. Check contrast (`tests/test_contrast.py` pairs) and both modes on `tokens/dark-mode.card.html`.
4. Never repoint an existing token's *meaning* in a MINOR release — add a new token instead.

## Add a template

First: is it an **archetype** — the reference composition for a *kind* of screen (a picker group) — or a **product screen**? Product screens belong in `templates/` (same anatomy, same scaffold, no picker entry), not here; `templates/` holds one reference per group.

1. Folder per screen: `templates/<slug>/` with entry `PascalCase(<slug>).dc.html`, first line inside the markup: `<!-- @template name="<Group> · <Name>" description="…" -->`. Group is one of **Start · Monitor · Investigate · Compose · Report · Admin** — it is how a consumer chooses from the picker. Keep the description under ~110 characters; it truncates.
2. Load the system via the sibling `ds-base.js`. Never author `ds-base.js` or `support.js` per folder — they are generated: `python scripts/sync_template_scaffold.py` writes the canonical copies from `templates/_scaffold/`, and `--check` fails CI on drift.
3. Content lives in the `.dc.html` as editable markup; `screen.jsx` / `views/` hold only what needs React (interactive tables, charts, dialogs, live state). A template whose screen is one opaque `<x-import>` cannot be edited by the people it exists for — `dashboard-workstation/` is the reference for the split.
4. Declare the standard tweaks in `data-props`: `$preview` (true working size), `density`, `theme`, `environment`, `dataState` — plus anything specific to the screen. The logic class mirrors `theme`/`density`/`environment` onto `<html data-theme>`, `<body data-theme-density>`, `<html data-environment>`.
5. Wrap the dominant work plane in `DataStateSwitch state="{{ dataState }}"` so a copy ships loading / empty / error, not just the happy path.
6. Contract (enforced by `scripts/check_design_system_governance.py`): viewport meta · `<main>` landmark · `<h1>` (may live in an `<x-import>`-ed sibling) · visible Live/Paper/Fixture badge (`WorkstationTopbar`, an explicit `[data-environment-badge]`, or the `ds-base.js` fallback the `environment` tweak drives) · `$preview` · the four tweaks · `DataStateSwitch` · PascalCase entry name · scaffold in sync · no bare legacy workspace titles.
7. `WorkstationShell` is recommended, not required: it owns the frame and the `<main>` landmark, and `blank-workstation/` + `dashboard-workstation/` show the composition. Hand-rolled chrome must still satisfy the contract.
8. Register: one row in `readme.md`'s template index + `CHANGELOG.md`.

## Release protocol

1. `CHANGELOG.md` — semver entry (MAJOR breaking · MINOR additive · PATCH fix).
2. `docs/UPGRADING.md` — migration note only when consumer action is needed.
3. Generate runtime and catalogs:

```bash
node scripts/compile_bundle_extensions.mjs
python scripts/sync_catalog.py --write
python scripts/generate_inventory.py --write
```

4. Run `python scripts/validate_release.py`. It checks syntax, token parity, contrast, governance, catalogs, inventory, scaffold drift, and Python tests.
5. Serve the root over HTTP and exercise affected specimens at desktop and mobile widths. Record the results in the release QA report.
6. Counts come from `_ds_manifest.json`; avoid hand-maintained census prose outside generated markers.

## Don'ts

- Don't edit `_ds_bundle.js`, `_ds_manifest.json`, `_adherence.oxlintrc.json`, or any screen folder's `support.js`/`ds-base.js` (`templates/*`, `templates/*`) — generated.
- Don't invent colors, fonts, radii, shadows, or emoji — `guidelines/VISUAL_FOUNDATIONS.md` is law.
- Don't add a root-level doc — guidelines go in `guidelines/`, developer docs in `docs/`. The root owns only `readme.md`, `CHANGELOG.md`, `CONTRIBUTING.md`, `SKILL.md`.
- Don't reference `uploads/` from shipped files (`local-upload-reference` gate).
