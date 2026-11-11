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

| Id | File(s) | Behaviour fixed | Patch commit | Regression |
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
| LP-12 | `components/core/Modal.jsx` | Every dialog labelled itself with the shared id `mdl-title`. Each dialog now owns its title id. | `c40a3b00` | `tests/modal-title-id-regression.mjs` |
| LP-13 | `components/charts/ChartAnnotations.jsx` | Annotations could not be reached by keyboard or read item by item. Adds `ChartAnnotations.List`, an HTML list of the same annotations in time order with arrow/Home/End movement and an Enter/Space detail, and `activeId` to emphasise the matching mark. | this change | `tests/chart-a11y-regression.mjs` |
| LP-14 | `components/charts/PeerComparisonStrip.jsx` | Peer values were disclosed only through hover titles. Each rail is now a button that opens the named peer values; the table lists them; marks survive forced colours. | this change | `tests/chart-a11y-regression.mjs` |

## Other local changes

These are local too and would also be lost on re-export. They are verified by the
checks named here, not by `test:patches`.

| Change | Commit | Verified by |
| --- | --- | --- |
| `templates/books-loop` and `templates/position-detail` declare and honour the standard tweaks and data-state switch; three shell component comments name tokens | `c40a3b00` | `scripts/check_design_system_governance.py` (the count rises from 56 if lost) |
| Token and palette reconciliation with the dashboard and WPF, listed under "Package changed to match the application" in [the audit](AUDIT_4_3_0.md) | `c77ced17`, `9ecec3da`, `2b3822d2` | `scripts/check_contrast.py`, `scripts/check_token_parity.py` |
| Release tooling: case-correct `README.md` lookups, family classification of 17 exports, build provenance, deterministic export | `de886adb` | `scripts/validate_release.py`, `npm run test:release`, `npm run release:check` |

## To carry upstream

LP-01 to LP-12 are defects in the design source and should be fixed there, so the
next export arrives with them. Until then this file is the re-apply list. When a
patch lands upstream, keep its regression and remove its row.

## Adding a patch

1. Fix the source file and rebuild with `npm run build`. Never edit `_ds_bundle.js` by hand.
2. Add a regression that fails on the unpatched file, and wire it into `test:patches`.
3. Add a row here with the commit.
