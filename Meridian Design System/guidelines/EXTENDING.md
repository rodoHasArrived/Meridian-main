# Extending Meridian

How to add to the system without eroding it. Every recipe below is additive — the compiler
picks new files up automatically; nothing is registered by hand.

## Stable vs. internal surface

**Stable (semver-governed):** every `PascalCase` export on `window.MeridianDesignSystem_205d17`,
every prop documented in a `.d.ts`, every Tier-1/Tier-2 token name, template folder names.
**Internal (may change in a MINOR):** lowercase bundle helpers (`useToast`, `money.js` utils),
the `--ws-*` compatibility alias layer, injected CSS class names (`.op-rail`, `.tlt__*` — never
target these from consumer CSS), card/test files. If you need an internal, promote it: give it a
`.d.ts` and a doc line, then treat it as stable.

## Add a component

1. Pick the domain folder (`core/ data/ charts/ accounting/ trading/ operations/ shell/`).
2. Create the trio: `Name.d.ts` (the contract — doc comment + typed props with `@default`s),
   `Name.jsx` (implementation), `Name.prompt.md` (one-paragraph usage + a minimal JSX example).
3. Follow the implementation contract:
   - **Self-injected scoped CSS** — one `inject()` guard writing a `<style data-mds="name">`
     block; class names prefixed uniquely. No external stylesheet, no global selectors.
   - **Tokens only** — every color/space/radius/font via `var(--token, fallback)` with the
     literal fallback so the component survives outside `styles.css`. Never invent colors.
   - **Graceful degradation** — every prop optional where possible; missing data renders an
     honest empty state, never `undefined` text or a crash. Cover the five data states
     (see `STATES_AND_ANTIPATTERNS.md`).
   - **Signal discipline** — graphite owns primary action, blue owns focus/selection, and terracotta
     is reserved for brand/scenario signal; status uses semantic trios as alpha-10 washes.
4. Give the folder's `@dsCard` gallery an example, or add a dedicated
   `name.card.html` (copy an existing card for the React scaffold).
5. Run the design-system check; the component compiles onto the namespace automatically.

**A children-shaped API must also have a markup-shaped path.** If a component reads its children's
props to do its work — a grid that measures `LayoutRegion` siblings, a rail that inspects step
elements — it is unusable from a design-component template, because a template passes *mounts*, not
your element type. Every such component ships a positional declaration prop alongside the
children form (`WorkspaceLayoutGrid`'s `slots`: one entry per child, in order, wrapped internally).
Without it the component compiles, cards fine, and cannot be adopted by any of the 27 templates —
which is how `WorkspaceLayout` shipped and had to be revised inside one release.

## Add a token

Author in the right tier (full model: `TOKEN_REFERENCE.md`):

- **Tier 2 (semantic)** — new capability tokens (`--chart-*`, `--severity-*`) go in the matching
  `tokens/*.css` file, derived from Tier-1/Tier-2 values via `color-mix` where possible so brand
  remaps and dark mode flow through for free.
- **Dark value** — add the override in `tokens/colors-dark.css` (it loads last and wins).
- **Never Tier 3** — components read Tier 2; hardcoded hex in a component is a defect.

## Add a brand preset

Add one block to `tokens/theme.css`:

```css
html[data-brand="oxblood"] {
  --theme-accent: #7A2E2E;
  --theme-accent-dim: #5E2323;
  --theme-accent-hover: #8F4040;
}
```

Brand/scenario signal treatments and active identity markers re-point automatically; focus and selection remain interaction blue. Keep the
accent desaturated and AA-contrasting on `#F8F7F3`; verify with `tests/test_contrast.py`.

## Add a density

Density scopes live in `tokens/base.css` under `body[data-theme-density="…"]` and remap only
`--theme-row-height` and the `--theme-spacing-*` scale. A new density is one block; components
inherit it — never write per-component density CSS.

## Add a template

1. Create `templates/<slug>/` with `<Slug>.dc.html`; first line of the template body:
   `<!-- @template name="<Label>" description="<one line>" -->`.
2. Load the system via the sibling `ds-base.js` (`<helmet><script src="./ds-base.js"></script></helmet>`);
   it is scaffolded automatically with `base = '../..'`.
3. **Compose, don't scaffold** — frame the screen with `WorkstationShell` (topbar / nav /
   status-bar slots) instead of hand-rolled flex chrome; build content from existing components.
   A template that needs a new primitive means the primitive comes first, as a component.
4. Add a row to the readme template index and a one-liner in `CHANGELOG.md`.

## Versioning & robustness gates

- MAJOR = removing/renaming a stable export, prop, or token. MINOR = additive. PATCH = fixes.
- Before shipping: `check` clean · `tests/unit-tests.html` green · contrast + governance scripts
  in `tests/` pass · new components documented (`.d.ts` + `.prompt.md`) · consumer migration
  notes in `docs/UPGRADING.md` when behavior shifts.
