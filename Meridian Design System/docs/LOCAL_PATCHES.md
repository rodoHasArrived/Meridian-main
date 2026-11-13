# Local patches to the vendored export

This package is vendored from the Claude Design export. The fixes below were made
in this repository after the 4.3.0 import. A fresh export from the design source
will not contain them unless they are carried back upstream or re-applied here.

**After any re-export, before accepting the new tree:**

```bash
npm run build
npm run test:patches
```

`test:patches` builds from source and needs a Chromium; set
`MERIDIAN_CHROMIUM_PATH` to use a system one. Every row in the first table has a
regression that was run against the pre-patch file and failed on it.

## Behaviour fixes with a regression

| Id | File(s) | Behaviour fixed | Patch commit / PR | Regression |
| --- | --- | --- | --- | --- |
| LP-01 | `templates/_scaffold/action-bridge.jsx` | The bridge notified its parent on every render. Basket Builder, Settings Admin and Report Pack re-rendered without end. It now hands the parent one stable API object and notifies only when the instruction list changes. | `7e580e17` | `tests/local-patches-regression.mjs` |
| LP-02 | `components/operations/ActionEnvelope.jsx` | Repeat submits made before React re-rendered each reached the transport. The idempotency key is now reserved synchronously. `retry()` rebuilt the instruction from the static declaration and dropped payload and endpoint overrides; it now resends the declaration that was sent. | `7e580e17` | `tests/local-patches-regression.mjs` |
| LP-03 | `templates/settings-admin/SettingsAdmin.dc.html` | The save key did not change when an edit kept the payload length, so the second save was deduplicated and never sent. The key is now a hash of the full serialized payload. | `7e580e17` | `tests/local-patches-regression.mjs` |
| LP-04 | `components/core/ContinuityFrame.jsx` | A stale, offline or error frame blocked the pointer only. Retained controls could still be focused and activated by keyboard. The locked body is now `inert`; the retry control stays outside it. | `7e580e17` | `tests/local-patches-regression.mjs` |
| LP-05 | `components/core/FormatProvider.jsx` | The surface's default decimals overrode an instrument tick, so `price(1.123456, { tick: 0.000001 })` printed four decimals. Surface decimals are now only a fallback. | `7e580e17` | `tests/local-patches-regression.mjs` |
| LP-06 | `components/core/localDate.js` | Impossible typed dates were normalized to another day or year (`2026-02-30` became 2 March). They are now rejected. | `7e580e17` | `tests/local-patches-regression.mjs` |
| LP-07 | `templates/_scaffold/books.js` | Staging checked only the selected period's state, so a journal dated in a hard-closed period could be staged from an open one. The posting date must now be valid and inside the selected period. | `7e580e17` | `tests/local-patches-regression.mjs` |
| LP-08 | `templates/interaction-depth/screen.jsx`, `components/data/DataGrid.jsx` | The work plane was redefined on every render, so the filter lost focus per keystroke and the grid scrolled to the top. Context-menu actions targeted the previously selected row instead of the right-clicked one. | `b2717394` | `tests/interaction-depth-regression.mjs` |
| LP-09 | `docs/eligibility.card.html`, `tests/action-contract.html` | CodeQL findings: an escape helper that left quotes unescaped inside attributes, and result rows built with `innerHTML` from assertion and exception text. | `daa3f512` | `tests/local-patches-regression.mjs` |
| LP-10 | `components/charts/ScenarioSelector.jsx` | Keyboard operation (arrow/Home/End, Space/Enter), no silent model change for a missing saved selection, and no accidental form submission. | `20f24a1d` | `tests/scenario-selector-regression.mjs` |
| LP-11 | `components/data/OptimisticCell.jsx` | A throwing settle observer or ledger subscriber could reclassify a confirmed or rejected transport outcome. Observer faults are now isolated and logged. | `20f24a1d` | `tests/optimistic-observer-regression.mjs` |
| LP-12 | `components/core/Modal.jsx` | Every dialog labelled itself with the shared id `mdl-title`. Each dialog now owns its title id, and the close control is an explicit `type="button"`. | `db7902a8` | `tests/modal-label-regression.mjs` |
| LP-13 | `components/charts/ChartAnnotations.jsx` | Annotations had no accessible text. The overlay group now carries an image name listing band positions, event explanations and callout values, and each event exposes its `detail` in an SVG title. | `590735c5` | `tests/chart-accessibility-regression.mjs` |
| LP-14 | `components/charts/PeerComparisonStrip.jsx` | Individual peer values were available only through hover titles. The table emitted with `ariaLabel` now lists them. | `590735c5` | `tests/chart-accessibility-regression.mjs` |
| LP-15 | `components/core/useOverlayFocus.js` | Initial focus and the Tab cycle exclude hidden, inert, disabled and collapsed content. Intentional heading autofocus, empty-panel focus and trigger restoration preserve the existing scroll locks and Escape stack. | PR #3542 | `tests/core-integration-regression.mjs` |
| LP-16 | `components/core/DatePicker.jsx`, `DateRangePicker.jsx`, `localDate.js` | Field labels and validation ARIA reach the actual date controls; feedback descriptions compose with caller descriptions. Civil years 0000–0099 round-trip without a century shift while retaining local-calendar semantics. | PR #3542 | `tests/core-integration-regression.mjs` |
| LP-17 | `components/accounting/LedgerTable.jsx`, `components/trading/OptionChainTable.jsx`, `components/charts/CorrelationHeatmap.jsx`, `CoverageMatrix.jsx`, `components/data/VirtualizedList.jsx`, `WorksheetGrid.jsx` | Roving rows preserve original callback objects and exclude child-control activation. Read-only tables, active strike announcements, accessible values, list positions and worksheet editability reflect their actual interaction contracts. | PR #3542 | `tests/integration-data-accessibility-regression.mjs` |
| LP-18 | `templates/field-formula/screen.jsx` | Retained formula suggestions use native buttons with announced selection, roving focus, arrow/Home/End navigation and native Enter/Space activation. The newer published DC workflow retains its existing implementation. | PR #3542 | `tests/retained-reference-accessibility-regression.mjs` |
| LP-19 | `templates/books-loop/BooksLoop.dc.html` | A cold start could remain on its loading view; duplicate dependency initialization could leave the view subscribed to an obsolete instruction store. Dependency readiness refreshes the real model and maintains one active subscription to the current store, with listener and subscription cleanup on unmount. | PR #3542 | `tests/books-loop-readiness-regression.mjs` |
| LP-20 | `components/operations/ActionQueue.jsx`, `ActionEnvelope.jsx` | Retrying an older queue row could resend a newer instruction of the same action. Retry now accepts the row identity and preserves its key, payload and attempt; concurrent retries share one dispatch. | Follow-up to PR #3520 | `tests/action-queue-retry-regression.mjs` |
| LP-21 | `components/core/DatePicker.jsx`, `DateRangePicker.jsx` | Tab from a calendar day closed the popup before operators could reach its footer or return to its month controls. Day navigation and dialog dismissal now have separate handlers. Tab traversal and a day-grid entry remain available after month changes, including date bounds. | Follow-up to PR #3520 | `tests/calendar-keyboard-regression.mjs` |
| LP-22 | `components/data/DataGrid.jsx` | Valid decimal strings displayed as amounts but could not establish totals or proofs, including strings emitted by inline editing. The shared aggregate helper normalizes decimal strings while withholding missing, invalid and overflowing evidence. | Follow-up to PR #3520 | `tests/grid-amount-regression.mjs` |
| LP-23 | `components/data/EditableCell.jsx` | An edit begun while enabled could commit on blur after the cell became disabled. Disabling now cancels the pending edit and commit checks the current disabled state. | Follow-up to PR #3520 | `tests/core-table-regression.mjs` |

## Other local changes

These are local too and would also be lost on re-export. They are verified by the
checks named here, not by `test:patches`.

| Change | Commit | Verified by |
| --- | --- | --- |
| Template contract repairs (`books-loop`, `position-detail` and the retained compatibility templates), shell comment tokens, retired-export declarations | `590735c5`, `db7902a8`, `bd3354f5`, `9ea0b762` | `scripts/check_design_system_governance.py` (zero findings), `tests/retained-template-states-regression.mjs`, `tests/template-preview-regression.mjs` |
| Token and palette reconciliation with the dashboard and WPF, and white-label brand identities in dark and forced-light, listed in [the audit](AUDIT_4_3_0.md) | `c77ced17`, `9ecec3da`, `2b3822d2`, `bc980e1b`, `004c94b8` | `scripts/check_contrast.py`, `scripts/check_token_parity.py`, `tests/brand-contrast-regression.mjs` |
| Release tooling: case-correct `README.md` lookups, family classification of 17 exports, build provenance, deterministic export | `de886adb` | `scripts/validate_release.py`, `npm run test:release`, `npm run release:check` |
| Strict asset-failure and loaded-style auditing; iframe accessibility remains independently checked | PR #3542 | `tests/browser-audit-regression.mjs`, `tests/full-library-browser.mjs` |
| Reference form names, associated labels, image alternatives, per-job progress names and keyboard-scrollable read-only examples | PR #3542 | `tests/full-library-browser.mjs` |

## To carry upstream

LP-01 to LP-23 are defects in the design source and should be fixed there, so the
next export arrives with them. Until then this file is the re-apply list. When a
patch lands upstream, keep its regression and remove its row.

## Adding a patch

1. Fix the source file and rebuild with `npm run build`. Never edit `_ds_bundle.js` by hand.
2. Add a regression that fails on the unpatched file, and wire it into `test:patches`.
3. Add a row here with the commit.
