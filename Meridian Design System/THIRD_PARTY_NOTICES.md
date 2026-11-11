# Third-party notices

The Meridian Design System's own source (tokens, components, templates, scripts, docs) is
first-party. The following third-party materials are referenced or loaded by consumers:

## React and ReactDOM — MIT License
The component bundle uses a consumer supplied React global. The offline release reference
loads `_preview_runtime.js`, which redistributes React and React DOM 19.3.1 plus Scheduler.
Other consuming pages and the Design Component host may load their own runtime. These packages
use the MIT License below. Copyright (c) Meta Platforms, Inc. and affiliates.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## IBM Plex Sans and IBM Plex Mono — SIL Open Font License 1.1
Both families are SELF-HOSTED: TTF binaries (400/400i/500/600/700 each) ship in `assets/fonts/`
with the licenses alongside (`assets/fonts/OFL-IBMPlexSans.txt`, `assets/fonts/OFL-IBMPlexMono.txt`),
declared with a `local()` short-circuit. Copyright IBM Corp. Licensed under the SIL OFL 1.1:
https://github.com/IBM/plex/blob/master/LICENSE.txt

## React and ReactDOM 18.3.1 for the template host — MIT License
`templates/_scaffold/vendor/react-18.3.1.production.min.js` and
`react-dom-18.3.1.production.min.js` are the unmodified production builds the Design Component
host (`support.js`) is pinned to by integrity hash. Template entries load them from static tags so
a template opens with no network; `scripts/build_package.mjs` fails if their bytes stop matching
the host's hashes. Copyright (c) Facebook, Inc. and its affiliates. MIT License, text above.

## Babel standalone — MIT License
`_preview_jsx.js` redistributes `@babel/standalone` 7.29.0 unmodified, behind a one-line banner.
Standalone specimen pages (component cards, UI kits, test pages) load it after
`_preview_runtime.js` so their hand-readable `<script type="text/babel">` demo source compiles in
the browser with no network request. It is preview tooling only: the component bundle does not
depend on it and consumers do not need it. Copyright (c) 2014-present Sebastian McKenzie and
other contributors. Distributed under the MIT License, whose text is reproduced above for React.

## oxlint rule-file format
`_adherence.oxlintrc.json` is a GENERATED configuration in the oxlint (MIT) file format; no
oxlint code is vendored here. https://github.com/oxc-project/oxc/blob/main/LICENSE

## Tooling
The static gates and ZIP exporter use the Python 3 standard library (PSF License).
The executable source rebuild uses pinned esbuild 0.28.2 (MIT, copyright 2020 Evan Wallace)
and the pinned React packages above; Playwright is browser test tooling. Dependencies are
recorded in `package-lock.json` and installed with `npm ci`. `node_modules` is excluded from
the ZIP. The
deterministic fixture engine's PRNG (mulberry32) and FNV-1a hash in
`templates/_strategy-executor/executor-store.js` are public-domain algorithms implemented
first-party; the SHA-256 in `components/core/StrategyModel.jsx` is a first-party implementation
of FIPS 180-4.
