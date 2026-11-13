# Full browser audit

`full-library-browser.mjs` discovers specimens from `_ds_manifest.json` and every
declared template entry from `api/template-eligibility.json`. It serves authored
HTML through a temporary loopback host with the package's local React 19 peer
runtime and the package's pinned Babel 7.29.0 `_preview_jsx.js`. Known historical
React/Babel CDN tags and exact published local peer tags are supplied once by
the audit host. This tests project React19 compatibility, including templates
whose published host ordinarily pins its vendored React18 peer. Unknown external
requests fail, and unrelated local dependencies retain normal loading checks.
The host transpiles authored JSX without modifying source or generated files.
DC entries receive the compiled namespace before their logic executes, as the
documented consumer contract requires.

```bash
npm run test:library
npm run test:cards
npm run test:templates
npm run test:contracts
npm run test:chart-trading
node tests/full-library-browser.mjs --kind=cards --only=docs/,guidelines/,tokens/ --matrix=smoke --out=/tmp/meridian-audit
```

`--only` accepts comma-separated path fragments. `--limit` and `--workers` are
useful for diagnosis; default concurrency is three. `--matrix=smoke` keeps one
desktop viewport but retains light/dark accessibility checks. The full matrix
uses 1920×1080, 1440×900 and 1366×768, a 200% desktop zoom layout equivalent,
and supported density
scopes. `MERIDIAN_CHROMIUM_PATH` selects a browser; otherwise the system
Chromium is used. Outputs default to `/tmp/pr3501-full-audit` and include
`browser-results.json` and screenshots for every measured theme/viewport/zoom.
Each run captures immutable bundle/compiler bytes and reports their SHA-256, so concurrent
rebuilds cannot mix compiled snapshots. Nothing is written into the package
unless a source repair is explicitly made separately.

Each page gets isolated storage and a fixed synthetic date. Pictorial specimens
are considered rendered only when meaningful named SVGs or images with alt text
have successfully loaded and have visible geometry. No fake text is added to
make image-only pages pass.

The zoom equivalent uses CDP metrics of 683×384 CSS pixels at DPR2, retaining a
1366×768 physical screenshot. Authored narrow media queries therefore activate,
as they do at 200% desktop browser zoom. `desktop-zoom-control.html` proves the
800px breakpoint collapses a two-column layout and that a genuine 800px child
still fails the 683px overflow check. The report records both controls and image
dimensions. This emulates layout; native Windows browser-toolbar zoom remains
unrun.

Appearance changes settle fonts and finite CSS transitions. A template whose
authored data-state contract defaults to `ready` also gets an eight-second bound
for any asynchronous loading wrapper to clear after host prop updates. Permanent
loading fails; informative loading copy retains its independent source contrast
and behavior checks.

## Evidence levels

- A compiled-export existence check establishes runtime availability and type,
  not business semantics.
- All 11 theme scopes exercise 13 actual severity/state foreground and background
  pairs. The report records composited contrast ratios against each rendered
  wash, with a 4.5:1 text minimum, alongside the fixture's accessibility results.
- Namespace references and React element creation establish observed coverage,
  not complete behavior.
- Generic probes establish the specific checkbox, tab, combobox and sorting
  state changes they assert. A read-only fixture without changing callbacks is
  recorded as blocked rather than passed.
- Explicit template recipes establish only their listed operator actions and
  expected states. Missing recipes are required coverage gaps and make the
  command fail. Compatibility entry recipes run inside the loaded canonical
  same-origin iframe; a forwarding frame alone does not establish a journey.
- Native Windows/Edge and assistive-technology announcement tests remain outside
  this system-Chromium run. Authored harness `OPEN` results remain blocked.

The command exits nonzero for failed checks or missing/failed primary template
journeys. Always inspect blocked cases as well; a zero exit code does not turn
an unexecuted assistive-technology check into a pass.

## Template recipes

`template-journeys.json` has `schemaVersion: 1` and an `entries` object keyed by
exact entry path. Each entry contains a description and `steps`, or a `use`
reference to another entry's recipe. Circular references fail.

Targets support `role` with `name`, `text`, `label`, or `css`; matching is exact
unless `exact: false`. `index` selects a deliberate match, and `within` scopes a
nested locator. Supported actions are `click`, `fill`, `select`, `press`,
`assertVisible`, `assertText`, `assertValue`, `assertDisabled`, `assertCount`,
`assertAttribute`, and `assertURL`. Text/URL assertions accept `contains`.
Disabled assertions may include `reasonContains` to check the title or accessible
description. Assertions wait for asynchronous UI updates. Recipes should finish
in a stable state and must not depend on live providers or credentials.

`chart-trading-contract-regression.mjs` independently bundles current component
source with the project React instance. It checks exact callback IDs/row objects,
controlled and uncontrolled selection, keyboard commitment, empty/read-only
states, missing versus zero data, and option-chain focus after row replacement.
Its machine-readable report defaults to
`/tmp/pr3501-full-audit/chart-trading-contract-results.json` and can be redirected
with `MERIDIAN_CHART_TRADING_REPORT`.
