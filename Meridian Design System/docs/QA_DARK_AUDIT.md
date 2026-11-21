# Dark-mode token audit — 2026-08-17

**Scope:** every component source (`components/**/*.jsx`), card gallery (`*.card.html`), and
template (`templates/**/*.{dc.html,jsx}`), scanned for color literals that would pin a surface to
the light theme (hardcoded paper/plate/well hexes, `white`/`#fff` backgrounds, light-ink text
colors) outside `var(--token, fallback)` fallback position.

## Result

Token discipline holds. Zero hardcoded surface backgrounds or text colors in component sources or
templates — dark mode (`data-theme="dark"` / `prefers-color-scheme`) retones everything through
`tokens/dark.css`.

Findings fixed:

| File | Was | Now |
| --- | --- | --- |
| `components/core/form-controls.card.html` | section chrome `background:"white"` | `var(--bg-well,#FCFBF8)` |
| `components/data/sparkline-empty-columns.card.html` | section chrome `background:"white"` | `var(--bg-well,#FCFBF8)` |

Reviewed and left as-is (correct in both themes):

- `color:#fff` / `color:"white"` on brand-signal button fills (`EmptyState`, `operations.card.html`
  `.btn--primary`) — text on terracotta, unchanged by theme.
- `__bundler_thumbnail` SVG fills — export splash art, never themed.

## Not covered

This is a source audit, not a rendered-contrast pass. A visual dark sweep of the 118 cards
(semantic trio contrast, chart series roles, environment badges under `tokens/dark.css`) remains
open — tracked with the deferred browser plan in `tests/BROWSER_TEST_PLAN.md`.
