# tokens/ — the L0 foundation

`../styles.css` @imports everything here; **order is load-bearing** (base colors → theme remap → dark overrides last, so dark's attribute/media rules win cascade ties).

| File | Owns |
| --- | --- |
| `fonts.css` | IBM Plex Sans/Mono loading + `--font-*` stacks |
| `colors.css` | the base palette — surfaces, text ladder, semantic trios, chart inks |
| `typography.css` | type ramp (`--type-*`, `--lh-*`), numeric variants |
| `elevation.css` | spacing, grid, dimensions, borders, radii (2/4/6), motion, the one popover shadow |
| `base.css` | minimal application reset and data/label utility classes |
| `theme.css` | white-label remaps — `data-brand` presets |
| `colors-dark.css` | dark mode (media query + `data-theme="dark"`) — every color's counterpart |
| `visualization.css` | the analytical layer — series-role ledger, ten data-condition triads, model-interval bands, plate/inspector geometry, connection states, material-hierarchy aliases (see `../guidelines/VISUALIZATION_GRAMMAR.md`) |
| `contrast-modes.css` | high-contrast adjustments |
| `print.css` | print layer — chrome hidden, `.mds-sheet` page model for report packs |

Release 3.1 adds `--workspace-context-height` and `--calendar-cell-min-height` for the coordinated context and close surfaces.

Rules: author a color in `colors.css` **and** `colors-dark.css` · tier it per `../guidelines/TOKEN_REFERENCE.md` (Tier 1 white-label · Tier 2 semantic · Tier 3 internal) · check `../tests/test_contrast.py` pairs · never silently repoint an existing token's meaning. Specimen cards in this folder (`*.card.html`) render the token sheets live.
