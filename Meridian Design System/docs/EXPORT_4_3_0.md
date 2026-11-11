# Build, verify, and export

This handoff packages the existing PR #3501 design system under the release
identity declared in `release.json`. It does not replace that identity, invent a
new release history, or certify later working-tree additions using upstream QA.
Read [Current export audit](AUDIT_4_3_0.md) for the source census and baseline.

## Source and generated assets

`release.json` remains authoritative for version/channel. `_ds_manifest.json`
defines the public namespace, component names, cards, templates, and token
inventory. The package build preserves that contract rather than discovering a
different API from a historical checkout.

`scripts/build_package.mjs` rebuilds `_ds_bundle.js` from the manifest's current
sources. `release-build.json` records source hashes, generated asset hashes,
counts, and tool versions. `_preview_jsx.js` is the pinned in-browser JSX compiler that
standalone specimen pages load so their readable JSX demo source runs offline.
The separate `_preview_runtime.js` supplies a pinned
local React/ReactDOM runtime for the ordinary HTML smoke page. Application
consumers should use their own React instance and omit that preview runtime.

Install build dependencies from the package's lockfile, then run from the package
directory:

```bash
npm ci
npm run build
npm run build:check
npm run test:release
python scripts/validate_release.py
python scripts/export_release.py --check
python scripts/export_release.py
```

The build requires the Node version declared in `package.json`; Python gates use
the standard library. Build-time dependencies and their exact versions are pinned
in `package-lock.json`. The source freshness check and export integrity check
must pass after component changes. Documentation-only edits still change the
archive's per-file hashes and require a new export.

With Chromium available, `npm run test:components` runs the ScenarioSelector and
optimistic-observer source regressions. `npm run test:agency` exercises agency
contracts and `npm run test:browser` exercises the standalone reference. Set
`MERIDIAN_CHROMIUM_PATH` when selecting a system Chromium. These browser checks
have their own synthetic-data scope; they do not replace workstation integration
or live-backend validation.

The additional `tests/optimistic-settlement-regression.mjs` and
`tests/chart-accessibility-regression.mjs` cover observer reporting and the chart
keyboard/data-alternative contracts. Run the full specimen matrix after the
central build using [`tests/SPECIMEN_MATRIX.md`](../tests/SPECIMEN_MATRIX.md).
It requires a separately prepared, pinned peer cache and preserves the original
conformance assertions and raw findings. The inspected cards contain 84 peer
script references without declared SRI; local byte-pin verification does not
change that source-integrity limitation or prove live CDN access.

The initial expanded export recorded 67 governance findings and 25 warnings.
The final merged source through `7952be63ba` passed the complete 135-test Python
suite and full contrast check. All seven release gates passed after the runtime
alias update; parity passed 12 tests and the full 502-declared/227-shared-name
check with zero findings. The 743-token metadata refresh adds 39 brand-scoped/
default records, with other catalogue counts unchanged. The brand browser matrix
passed 28 states, 1,932 AA pairs and 60 actual dark-role mapping checks. Native
browser coverage passed 64 cases and 15 axe scans with zero reported violations.
The byte-identical component bundle and public runtime retain the strict
135/135-card proof. Source-hash and README checks reported zero errors.

The 13-test metadata suite includes unconditional-default precedence.
Build/source freshness and release integrity passed for 1,347 curated files.
The retired-export declaration is enforced by 29 governance tests; the actual
package reports zero governance failures and warnings.
Regenerate the artifact after final documentation edits and associate its
checksum with the delivered source. Use the audit's continuation status for the
precise scope, historical dashboard evidence and documentation gate that did not
converge.
Build/export integrity validates the curated artifact; it cannot turn a failed
or unrun gate into release certification. Keep NVDA/VoiceOver, Firefox/WebKit,
native Windows/Edge and full-monorepo checks explicit when they were not run.

## Archive and verification

Default output:

```text
dist/meridian-design-system-4.3.0.zip
dist/meridian-design-system-4.3.0.zip.sha256
```

The archive has one `meridian-design-system-4.3.0/` root. It includes
`export-inventory.json` with per-file byte sizes and SHA-256 values, plus
`SHA256SUMS` for the payload and inventory. The `.zip.sha256` sidecar verifies
the complete archive. Preserve both the archive and sidecar when handing it off.

Verify the sidecar before extraction. On Linux/macOS, from the output directory:

```bash
sha256sum -c meridian-design-system-4.3.0.zip.sha256
```

On Windows, compare the sidecar's hash with:

```powershell
Get-FileHash .\meridian-design-system-4.3.0.zip -Algorithm SHA256
```

After extraction, verify the payload against its internal inventory/SHA256SUMS.
Checksums detect changed bytes; they do not independently establish authorship or
publisher trust. Re-exporting unchanged inputs produces the same archive bytes.

## Accepting a re-export

A fresh export from the design source replaces the vendored files and drops every
local fix that was not carried upstream. Before accepting a new tree, rebuild and
run the local-patch regressions, then re-apply whatever fails from
[LOCAL_PATCHES.md](LOCAL_PATCHES.md):

```bash
npm run build
MERIDIAN_CHROMIUM_PATH=/path/to/chromium npm run test:patches
```

## Hosted checks

`.github/workflows/design-system-package.yml` runs on pull requests and `main` pushes that
change this package or the dashboard asset synchronization script. `package-integrity` runs
`build:check`, `release:check`, `test:release`, the dashboard asset projection check,
`test:patches`, `test:agency` and `test:browser`. `release-governance` runs
`scripts/validate_release.py` and the Python suite with no allowance for failure. Neither
is a required check.

## Consumer dependencies

- **Component bundle:** React is a consumer-supplied peer/global. Load React
  before `_ds_bundle.js`, then consumer code. Do not load a second React runtime
  into an existing application.
- **Styles:** preserve `styles.css` and every imported token stylesheet. Tokens
  are a dependency closure, not optional individual samples.
- **Fonts:** `tokens/fonts.css` references IBM Plex Sans/Mono TTFs in
  `assets/fonts/`; the OFL licenses ship beside them. No Google Fonts request is
  required for this package's type system.
- **Templates:** `.dc.html`/`x-dc`/`x-import` content retains its Design Component
  host and JSX runtime requirement. The standard HTML smoke entry proves a
  narrower browser-bundle path; it does not convert the full template catalogue.
- **Platform integration:** map the package's token roles and domain contracts
  deliberately. `tokens/parity-manifest.json` records the names and, since the
  4.3.0 migration, the values the package shares with the Meridian browser
  workstation; `scripts/check_token_parity.py` checks both when the package
  sits in that repository and skips the comparison when it does not. No
  divergence is recorded. The production browser integration in this PR is
  delivered through the monorepo's native source and built workstation assets;
  this standalone archive contains the design-system package. Concurrent WPF
  integration uses its platform resources and requires Windows validation
  separately.

The archive excludes uploads, scraps, screenshots, embedded application-source
snapshots, local dependency directories, and previous export output. It retains
the required source, docs, styles, fonts, notices, generated assets, and template
helpers. Local export does not deploy a website, publish to a package registry,
or activate trading/provider connections.

## Record the delivered evidence

Associate the final archive checksum with its source commit and the actual
verification commands/results. At minimum, record source/build freshness,
release gates, archive/inventory checks, and the browser interaction scope that
was exercised. Keep failures and unrun assistive-technology/browser matrix
portions explicit. The initial PR failed six release gates; that baseline is
preserved in the audit rather than silently relabeled as passing.
