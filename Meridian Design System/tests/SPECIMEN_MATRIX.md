# Full specimen matrix

`specimen-matrix.mjs` runs every card declared by `_ds_manifest.json` through
the existing `tests/conformance.html` **Run card assertions** control. It keeps
the conformance assertions intact: a wrapper around `mounted.run` records the
DOM seen at assertion time, then returns the original assertion result.

The browser uses normal motion, a 1440 × 900 viewport, and the package's own
card runtime declarations. Output is synthetic UI evidence, not certification
of application workflows, providers, live CDN access or assistive technology.

## Runtime

Every card loads the package's own `_preview_runtime.js` (React and React DOM) and
`_preview_jsx.js` (the JSX compiler for its readable `<script type="text/babel">` source), so the
matrix needs no network and no peer cache:

```bash
node tests/specimen-matrix.mjs
```

Any remote request is blocked, recorded and fails the run, and a card that declares a remote
script fails it too. The peer cache below is kept only for diagnosing such a card.

## Prepare the local peer cache (diagnostic only)

Install the package's development dependencies with `npm ci`, then prepare a
separate cache using the three exact versions the cards declare:

```bash
npm install --prefix /tmp/meridian-card-runtime --no-save --package-lock=false \
  react@18.3.1 react-dom@18.3.1 @babel/standalone@7.29.0
```

Use a writable temporary directory suitable for your operating system; the
path is not hardcoded in the runner. Package installation must use the managed
environment's normal network policy, proxy and CA trust. If registry access is
blocked, obtain the permitted cache through environment setup rather than
disabling network controls or fetching unpinned replacements.

The runner validates installed package versions and SHA-384 of every cached
runtime against the known hashes already used in the specimen HTML. It then
substitutes only those three exact CDN URLs with the verified local bytes.
Other remote requests are blocked, recorded and fail the run. The runner never
downloads runtime dependencies, changes script declarations, adds source SRI,
or allows an unknown CDN URL through to the network.

Missing `integrity` attributes remain visible in
`missingIntegrityReferences`; source-declared integrity and cache-byte
verification are separate facts. The local substitution does not make an
unprovided source integrity attribute present. Missing source SRI is reported
for review separately from the existing conformance assertions.

## Run after the central build

Finish source changes and rebuild the generated package before starting the
matrix. Do not rebuild or modify the package while the collector is running.

```bash
npm run build
npm run build:check
MERIDIAN_CHROMIUM_PATH=/usr/bin/chromium node tests/specimen-matrix.mjs \
  --peer-cache /tmp/meridian-card-runtime
```

`MERIDIAN_CHROMIUM_PATH` or `CHROMIUM_EXECUTABLE_PATH` selects an existing
Chromium executable. Omit both to use the browser installed for Playwright.
The runner starts and closes its own ephemeral loopback static server. To use
an existing server instead, pass `--base-url http://127.0.0.1:8770` or set
`MERIDIAN_SPECIMEN_URL`.
An existing server must serve this checkout; the default private server gives
the clearest source provenance.

Optional flags:

- `--output PATH` or `MERIDIAN_SPECIMEN_OUTPUT`: artifact directory; default is
  a new directory under the operating system's temporary directory.
- `--timeout-ms 300000`: overall completion timeout for the card matrix.
- `--validate-peers-only`: validate cache versions, pinned bytes and raw card
  references without launching the browser. Its `PEERS_VERIFIED` result is not
  a rendered matrix pass.
- `--help`: show command usage.

The runner preserves the complete `window.__conformance` result, all card
findings, runtime and console errors, HTTP/asset failures, blocked remote
requests, peer-substitution evidence and DOM diagnostics in `result.json`.
`scoreboard.png` captures only the viewport. Partial results are retained when
the collector fails or times out.

A matrix run exits nonzero for incomplete/failed conformance, runtime or console
errors, failed assets (including aborted font loads), unknown remote URLs, or a
changed component-bundle hash. Static advisory findings remain in the raw
conformance report; the runner does not discard or reclassify them. Read the
report before treating the result as release evidence.
