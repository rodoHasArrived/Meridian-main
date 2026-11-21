# QA report — 3.3.1

**11 August 2026 · scope: `templates/reconciliation-exception/` recomposition and documentation counts**

## What runs in this project

This project is the design-system source tree. The upstream package's `scripts/` governance tools
and `tests/` suites are **not** ported here (`readme.md › Repository map`), so this report records
what was verified in this environment, not a re-run of the upstream gate. `docs/QA_REPORT_3.1.0.md`
remains the last full validation evidence.

## Checks performed

| Check | Method | Result |
| --- | --- | --- |
| Compiler conformance | `check_design_system` | Namespace, 173 modules / 230 exports, 102 cards, 31 templates, 644 tokens, fonts resolved |
| Template scaffold parity | `templates/reconciliation-exception/ds-base.js` vs `templates/_scaffold/ds-base.js` | Byte-identical; loads `styles.css` + `_ds_bundle.js` only (no duplicate token stylesheets) |
| Standard template controls | `data-props` on the template's script tag | `density`, `theme`, `environment`, `dataState` present with `$preview` 1440×900 |
| Data-state coverage | `dataState` tweak through `DataStateSwitch` | `ready` / `loading` / `empty` / `error` all render |
| Degraded-data behaviour | `ContinuityFrame` around the work plane | Retains the snapshot, names its age, offers retry |
| Selection ownership | Source review | No committed selection held in local template state; `LinkedSelectionProvider` scope `exception`, URL param `exception` |
| Workflow resumability | `?step=` / `?done=` round-trip | Step and proof set restore on reload |
| Interaction colour | Source review of the template stylesheet | No `var(--accent)` in selection, focus, or hover rules |
| Console | Preview load | Clean |

## Not covered

- Contrast measurement, keyboard/ARIA matrix, and the 22-question visualization gate — upstream suites.
- The other 30 templates were not re-run against the template contract in this pass; the
  operating-language adoption sweep is release 3.4.
