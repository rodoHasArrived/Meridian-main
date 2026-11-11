# Meridian — What a consumer pays

Measured payload and load-behavior reference for consuming projects. Figures measured
**July 2026** against the then-current bundle; the system now compiles **268 exports across
181 modules**, so the byte figures below are a floor, not a current reading. Re-run the
snippet in §5 before quoting any number here.

---

## 1. The three things a page loads

| Asset | Raw | Gzip (wire) | Notes |
| --- | --- | --- | --- |
| `styles.css` + token closure (`tokens/*.css`) | 38 KB | **13 KB** | 10 files via `@import`; tokens, fonts, elevation, dark, print, forced-colors |
| `_ds_bundle.js` | 1,141 KB | **~247 KB** | All components, compiled; loads once, cached |
| React 18 UMD (peer, not bundled) | — | ~45 KB | The bundle expects `React`/`ReactDOM` globals; it ships **no** React copy |

Total first-visit wire cost ≈ **305 KB** gzip (13 + 247 + 45), **plus both webfont families**.
`tokens/fonts.css` `@import`s IBM Plex Sans *and* IBM Plex Mono from Google Fonts — the
`local()` `@font-face` entries only avoid the download where the faces are already installed.
An install with no egress to `fonts.googleapis.com` gets system substitutes for the entire
type ramp. Self-hosting the subset is an open item.

## 2. How component CSS works (and what it costs)

- Components **inject their own scoped CSS at first render** — a guarded, run-once
  `<style data-mds="…">` per component family. Render two `DepthLadder`s or a hundred:
  one style tag.
- Injected CSS is roughly a quarter of the bundle's **1,141 KB raw**. It compresses well
  (repetitive token references) and is inert until a component actually renders — pages
  pay parse cost only for what they mount.
- **Verified: zero exact-duplicate CSS blocks** across the 95 injection sites (checked
  July 2026). Shared visual patterns (small-caps labels, hairline rules) intentionally
  repeat *token references*, not rule blocks — deduping them into a shared stylesheet
  would couple component releases and break copy-one-component consumption.
- All injected rules resolve through tokens, so theme/brand/density/dark flips restyle
  already-injected CSS with **zero JS work**.

## 3. The monolith trade-off (deliberate)

The bundle is one file by design: workstations mount 20–40 components per screen, so
per-component files would mean dozens of requests and no meaningful savings after gzip.
The costs to know about:

- **No tree-shaking.** A page using only `Button` still downloads the accounting tables.
  At ~247 KB gzip, cached after first load, this is cheap for an operator terminal that
  users live in all day. Don't use the bundle for a marketing page.
- **Load order matters:** React UMD → `_ds_bundle.js` → your code. `defer` all three or
  put them at the end of `<body>`.
- **Load it once.** Templates load it via `ds-base.js`; don't add a second `<script>` —
  the bundle is idempotent but you'd pay double parse.

## 4. Runtime notes

- Tables self-window past 500 rows (`DataGrid`) and
  `VirtualizedList`/`AsyncCombobox` window unconditionally — payload, not rendering, is
  the thing to budget.
- No entrance animations, no springs: first paint is the final paint.
- `guidelines/PATTERNS.md › Performance` covers render-time guidance (density, chart heights).

## 5. Re-measuring

```js
// in a browser console on any page of this project
const gz = async (u) => { const b = await (await fetch(u)).blob();
  const g = await new Response(b.stream().pipeThrough(new CompressionStream('gzip'))).blob();
  console.log(u, b.size, '→', g.size, 'gzip'); };
['styles.css', '_ds_bundle.js'].forEach(gz);
```

CI-adjacent checks: `scripts/check_contrast.py` (token contrast), `tests/` (unit suite).
A size regression has no automated gate yet — compare against this file when reviewing
large component additions.
