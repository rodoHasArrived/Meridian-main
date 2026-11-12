# Design-system compliance

**Status:** active
**Scope:** the active design package and Meridian browser workstation; WPF is excluded.

Compliance has three independent evidence layers: source policy, rendered acceptance, and a
Windows manual pass. A zero policy count alone is not a WCAG or product certification.

## Sources and compatibility

The canonical token layers are loaded by `styles.css`. Current components, reference cards,
and templates consume semantic roles. `colors_and_type.css` is a compatibility adapter, not
an independently authored palette. Its HSL consumers use `--border-hsl` and `--accent-hsl`;
`--border` and `--accent` remain full CSS colors in the package. The browser retains its own
explicit HSL/hex bridge documented in its source README.

`guidelines/` owns current visual and accessibility guidance. Superseded copies and imported
snapshots are preserved with provenance and replacement links in
[the design-system archive](../../archive/docs/design-system/README.md).

The generated `design-system-consumption.json` classifies all public exports by static
symbol reachability from the application entry. Tests, barrels, metadata and unused wrappers
do not establish integration. These classifications identify consumers; rendered acceptance
is still required. Reference-only components do not imply missing product workflows.

## Rebuild and automated checks

Run these commands from the repository root after installing the dashboard's locked dependencies:

```bash
npm run design:build
npm run design:check
npm run design:test:browser
```

`design:build` regenerates the package bundle/manifest and consumption inventory, then builds
the served browser assets. The namespace and public exports come from
`scripts/public-exports.json`. Generated output is deterministic; edit source inputs and rerun
the generator instead of patching the bundle. Source line endings are normalized for portable
freshness checks.

`design:check` checks output freshness, package policy and contrast, actual browser styling,
and the build/policy/inventory/browser-harness regressions plus native adapter contracts.
It does not silently rebuild stale output or excuse failures.

`design:test:browser` exercises the package references and the seven browser workspaces with
synthetic data. It checks meaningful rendering, actual compiled styles, console/asset health,
keyboard/focus behavior, axe, desktop overflow, themes, density, reduced motion, forced colors,
and current-view printing. Print checks preserve session/provenance and mounted selection;
printing never fetches unmounted virtualized records or replaces governed report generation.

Axe runs against each loaded document and its rendered child frames with stylesheet/media
preloading disabled. The actual same-origin styles are already loaded; this avoids axe's cloned
stylesheets resolving relative imports against the wrong URL. All selected WCAG rules remain
enabled. A rendered regression fixture verifies that low contrast and real missing assets still fail.

The reference demos retain pinned React 18 peers; the application retains its existing React
version. The harness fulfills declared CDN requests from a SHA-256 verified local cache. The
first run may download those exact assets. Use `--offline` on subsequent runs. A missing cache
entry or an unexpected external dependency fails; dependencies are not injected into broken
pages to hide missing bootstrap code.

Results and failure screenshots go under ignored `artifacts/design-system-compliance/`.
A filtered run is diagnostic evidence and cannot establish full conformance. For explicit
browser selection or a local executable:

```bash
npm run design:test:browser -- --browser chrome
npm run design:test:browser -- --browser msedge
npm run design:test:browser -- --executable-path /path/to/chromium
```

The supported desktop sizes are 1920×1080, 1440×900 and 1366×768, with 1280×720 for dense
surfaces. Phone/tablet layouts and a mobile application are outside the product scope.

## Windows manual acceptance

Do not mark this layer passed from Linux Chromium or automated axe results. On the exact clean
source commit, run the full matrix in Windows Chrome and Edge, then record real observations:

- Tab and Shift+Tab through each workspace and representative forms, tables, overlays and
  disclosures. Verify visible focus, arrow/Home/End behavior, disabled controls, Escape, and
  focus restoration; no keyboard traps or hidden targets.
- At 200% browser zoom, verify readable text, reachable commands, scrollable wide tables,
  unclipped dialogs and retained financial/provenance context. Test dense surfaces at the
  documented desktop floor. Do not claim unsupported 400% single-column reflow.
- With NVDA, verify landmarks, one screen heading, labels, status announcements, table/grid
  relationships, selection, validation, and actionable recovery.
- In Windows High Contrast, verify system-color focus/selection and readable states with
  non-color labels. Confirm reduced-motion and light/dark/forced-light behavior.

Create a local evidence JSON containing the actual commit, observer, observation time, Windows
version, and one entry per browser. This intentionally incomplete template cannot pass:

```json
{
  "sourceCommit": "<full tested commit>",
  "observer": "<person who performed the manual checks>",
  "observedAt": "<UTC observation timestamp>",
  "windowsVersion": "<actual Windows version>",
  "browsers": [
    { "browser": "chrome", "platform": "win32", "version": "", "nvdaVersion": "", "keyboard": "pending", "zoom200": "pending", "nvda": "pending" },
    { "browser": "msedge", "platform": "win32", "version": "", "nvdaVersion": "", "keyboard": "pending", "zoom200": "pending", "nvda": "pending" }
  ]
}
```

Change a field to `passed` only after performing its checks. Supply the completed evidence to
an unfiltered full-matrix run:

```bash
npm run design:test:browser -- --scope all --manual-evidence /path/to/windows-evidence.json --require-windows
```

Dirty trees, a different commit, missing observations, a filtered/partial matrix, or any automated
failure keep the full-compliance result pending or failed. `--require-windows` exits nonzero when
the required manual evidence is absent. Keep significant limitations visible in the handoff.
