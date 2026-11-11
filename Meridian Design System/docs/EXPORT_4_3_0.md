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
counts, and tool versions. The separate `_preview_runtime.js` supplies a pinned
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

The retained whole-package governance gate currently reports 56 legacy/template
findings and 25 warnings, so `validate_release.py` returns a failure while its
other six gates pass. That result remains visible in the audit. The separate
build/export integrity checks validate the curated artifact; they do not turn
the legacy governance result into a passing release certification.

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
`scripts/validate_release.py` and the Python suite and stays red while their findings are
open. Neither is a required check.

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
