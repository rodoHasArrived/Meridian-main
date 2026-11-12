# Meridian — What a consumer loads

**Status:** active
**Measured:** 2026-11-12

Measurements below cover the deterministic package with 160 public references. They are source
byte counts and Node 24 gzip level-9 estimates, not a claim about a particular web server's wire encoding.
The [July measurements](../../archive/docs/design-system/2026-11-12/performance-july-reference.md)
remain historical evidence; they do not describe the current bundle.

## Package payload

| Asset | Source bytes | Gzip estimate | Scope |
| --- | ---: | ---: | --- |
| `_ds_bundle.js` | 787,344 (768.9 KiB) | 160,725 (157.0 KiB) | All public references and internal helpers; React is a peer |
| `styles.css` and its token import closure | 65,990 (64.4 KiB) | 18,887 (18.4 KiB) | Ten local CSS files; gzip estimate sums independently compressed files |
| React/React DOM peers | Not included above | Depends on selected peer build | Reference demos retain pinned React 18 peers; the browser application has its own version |

Fonts resolve from installed Windows and system stacks. No Google Fonts or other external font
request is made. Local Segoe UI, Cascadia, JetBrains Mono and system fallbacks require no font
payload. React/Babel dependencies used by reference demos are separate from these package totals;
the acceptance harness resolves declared versions through its verified local cache.

## Runtime behavior

Components inject their scoped CSS at first render, guarded once per component family. Theme,
brand, density and appearance changes restyle existing token-backed rules without replacing
component CSS. The JavaScript parser still processes the whole package; CSS rules reach the
browser's stylesheet parser when the corresponding component mounts.

The namespace bundle does not tree-shake by consumer usage. A page loading it for Button also
loads the accounting and chart implementations. Load the React peer before `_ds_bundle.js`, then
consumer code. Templates do this through their existing bootstrap; do not add a second bundle
script. The production browser workstation retains its own code-split native adapters.

Reference tables can automatically window large datasets. Native workstation tables deliberately
retain their own row cap and explicit virtualization. These are separate behavior contracts;
printing a current view does not fetch virtualized offscreen records.

## Re-measuring

Run this JavaScript with Node from the repository root after `npm run design:build`:

```js
const { readFileSync } = await import('node:fs');
const { resolve, dirname } = await import('node:path');
const { gzipSync } = await import('node:zlib');
const base = resolve('Meridian Design System');
const visited = new Set();
function closure(file) {
  file = resolve(file);
  if (visited.has(file)) return;
  visited.add(file);
  const text = readFileSync(file, 'utf8');
  for (const match of text.matchAll(/@import\s+["']([^"']+)["']/g)) {
    closure(resolve(dirname(file), match[1]));
  }
}
closure(resolve(base, 'styles.css'));
for (const [name, files] of [
  ['bundle', [resolve(base, '_ds_bundle.js')]],
  ['CSS import closure', [...visited]]
]) {
  const bytes = files.map(file => readFileSync(file));
  console.log(name, {
    files: files.length,
    raw: bytes.reduce((sum, value) => sum + value.length, 0),
    gzip: bytes.reduce((sum, value) => sum + gzipSync(value, { level: 9 }).length, 0)
  });
}
```

`npm run design:check` verifies generated freshness, source policy, contrast and component contracts.
Payload size is measured evidence rather than an arbitrary pass/fail ceiling; rerun the snippet
and update these figures when changing package code or token layers.
