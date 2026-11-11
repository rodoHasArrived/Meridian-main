# Third-party notices

The Meridian Design System's own source (tokens, components, templates, scripts, docs) is
first-party. The following third-party materials are referenced or loaded by consumers:

## React and ReactDOM — MIT License
Loaded by consuming pages (and the design-component runtime) from a CDN; not redistributed in
this repository. Copyright (c) Meta Platforms, Inc. and affiliates. Licensed under the MIT
License: https://github.com/facebook/react/blob/main/LICENSE

## IBM Plex Sans and IBM Plex Mono — SIL Open Font License 1.1
Both families are SELF-HOSTED: TTF binaries (400/400i/500/600/700 each) ship in `assets/fonts/`
with the licenses alongside (`assets/fonts/OFL-IBMPlexSans.txt`, `assets/fonts/OFL-IBMPlexMono.txt`),
declared with a `local()` short-circuit. Copyright IBM Corp. Licensed under the SIL OFL 1.1:
https://github.com/IBM/plex/blob/master/LICENSE.txt

## oxlint rule-file format
`_adherence.oxlintrc.json` is a GENERATED configuration in the oxlint (MIT) file format; no
oxlint code is vendored here. https://github.com/oxc-project/oxc/blob/main/LICENSE

## Tooling
`scripts/` uses only the Python 3 standard library (PSF License); nothing third-party is
vendored, fetched, or bundled. The
deterministic fixture engine's PRNG (mulberry32) and FNV-1a hash in
`templates/_strategy-executor/executor-store.js` are public-domain algorithms implemented
first-party; the SHA-256 in `components/core/StrategyModel.jsx` is a first-party implementation
of FIPS 180-4.
