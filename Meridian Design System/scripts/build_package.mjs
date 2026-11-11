#!/usr/bin/env node
// Reproducible browser distribution. React is a peer of the component bundle;
// the separate preview runtime exists only for the ordinary HTML reference page.
import { build } from 'esbuild';
import { readFile, writeFile, readdir } from 'node:fs/promises';
import { dirname, resolve, relative, extname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { rootTokenDefaults } from './token_defaults.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const check = process.argv.includes('--check');
if (process.argv.slice(2).some(arg => arg !== '--check')) throw new Error('Usage: node scripts/build_package.mjs [--check]');
const pkg = JSON.parse(await readFile(resolve(root, 'package.json'), 'utf8'));
const oldManifest = JSON.parse(await readFile(resolve(root, '_ds_manifest.json'), 'utf8'));
// Preserve the published token identities/scopes while refreshing default values
// from their authored stylesheets. A source token repair must not leave the
// browser's manifest and documentation describing the previous value.
const tokenSheets = new Map();
for (const token of oldManifest.tokens) {
  if (token.scope || !token.definedIn?.endsWith('.css')) continue;
  if (!tokenSheets.has(token.definedIn)) tokenSheets.set(token.definedIn,
    rootTokenDefaults(await readFile(resolve(root, token.definedIn), 'utf8'),
      token.definedIn.endsWith('/colors-dark.css') ? ':root[data-theme="dark"]' : ':root'));
  const value = tokenSheets.get(token.definedIn).get(token.name);
  if (!value) throw new Error(`Published token declaration is missing: ${token.name} in ${token.definedIn}`);
  if (value.replace(/\s+/g, ' ') !== token.value.replace(/\s+/g, ' ')) token.value = value;
}
const identity = JSON.parse(await readFile(resolve(root, 'release.json'), 'utf8'));
if (pkg.version !== identity.version) throw new Error('package.json must match authoritative release.json');
const namespace = oldManifest.namespace;
const priorHeader = JSON.parse((await readFile(resolve(root, '_ds_bundle.js'), 'utf8')).split('\n')[0].match(/@ds-bundle: (.+) \*\//)[1]);
const hash = content => createHash('sha256').update(content).digest('hex');
const posix = path => relative(root, path).split('\\').join('/');
async function files(dir) {
  const result = [];
  for (const entry of (await readdir(dir, { withFileTypes: true })).sort((a, b) => a.name < b.name ? -1 : a.name > b.name ? 1 : 0)) {
    if (['node_modules', '.git', 'dist', '__pycache__'].includes(entry.name)) continue;
    if (dir === root && ['uploads', 'scraps', 'screenshots', 'src'].includes(entry.name)) continue;
    const path = resolve(dir, entry.name);
    if (entry.isDirectory()) result.push(...await files(path));
    else if (entry.isFile()) result.push(path);
    else throw new Error(`Symlinks and special files are unsupported: ${path}`);
  }
  return result;
}
// The manifest is the public contract: retained legacy source files are not
// automatically revived as exports. Hash the entire retained source inventory.
const sourcePaths = [...await files(resolve(root, 'components')), ...await files(resolve(root, 'api'))]
  .filter(path => ['.js', '.jsx'].includes(extname(path)));
for (const name of ['tests/conformance-rules.js', 'tests/mock-transport.js']) sourcePaths.push(resolve(root, name));
sourcePaths.sort();
const components = oldManifest.components;
const unexposedExports = priorHeader.unexposedExports || [];
const sourceHashes = {};
for (const path of sourcePaths) sourceHashes[posix(path)] = hash(await readFile(path));
const seen = new Set();
for (const component of components) {
  if (seen.has(component.name)) throw new Error(`Duplicate public export: ${component.name}`);
  seen.add(component.name);
}
const reactNames = ['Children', 'Component', 'Fragment', 'PureComponent', 'StrictMode', 'Suspense', 'cloneElement', 'createContext', 'createElement', 'createRef', 'forwardRef', 'isValidElement', 'memo', 'useCallback', 'useContext', 'useDebugValue', 'useDeferredValue', 'useEffect', 'useId', 'useImperativeHandle', 'useInsertionEffect', 'useLayoutEffect', 'useMemo', 'useReducer', 'useRef', 'useState', 'useSyncExternalStore', 'useTransition'];
const sideEffects = sourcePaths.filter(path => !posix(path).startsWith('components/')).map(path => `import './${posix(path)}';`).join('\n');
const entry = sideEffects + '\n' + components.map((item, i) => `import { ${item.name} as e${i} } from './${item.sourcePath}';`).join('\n') +
  `\nObject.assign(window.${namespace} = window.${namespace} || {}, {\n` +
  components.map((item, i) => `${item.name}:e${i}`).join(',\n') + '\n});\n';
// money.js and the public Money.jsx carrier coexist. Prefer the exact .js helper
// for extensionless './money' imports, including on Windows case-insensitive disks.
const common = { absWorkingDir: root, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2020', charset: 'utf8', legalComments: 'inline', logLevel: 'error', resolveExtensions: ['.js', '.jsx', '.ts', '.tsx', '.json'] };
const header = { format: 4, version: pkg.version, namespace, components, sourceHashAlgorithm: 'sha256', sourceHashes, inlinedExternals: [], unexposedExports };
const bundle = await build({
  ...common,
  treeShaking: false,
  stdin: { contents: entry, resolveDir: root, sourcefile: 'meridian-design-system-entry.js', loader: 'js' },
  jsxFactory: 'window.React.createElement', jsxFragment: 'window.React.Fragment',
  banner: { js: `/* @ds-bundle: ${JSON.stringify(header)} */` },
  plugins: [{ name: 'react-peer', setup(builder) {
    builder.onResolve({ filter: /^react$/ }, () => ({ path: 'react', namespace: 'meridian-peer' }));
    builder.onLoad({ filter: /.*/, namespace: 'meridian-peer' }, () => ({ contents:
      `const React = window.React; if (!React) throw new Error('Load React before the Meridian component bundle'); export default React;\n` +
      reactNames.map(name => `export const ${name} = React.${name};`).join('\n'), loader: 'js' }));
  } }]
});
const runtime = await build({ ...common,
  stdin: { contents: `import React from 'react'; import * as ReactDOM from 'react-dom'; import {createRoot} from 'react-dom/client'; window.React=React; window.ReactDOM={...ReactDOM,createRoot};`, resolveDir: root, sourcefile: 'meridian-preview-runtime.js', loader: 'js' },
  define: { 'process.env.NODE_ENV': '"production"' },
  minify: true,
  banner: { js: `/* Meridian ${pkg.version} offline preview runtime. React / React DOM ${pkg.devDependencies.react}; MIT. See THIRD_PARTY_NOTICES.md. */` }
});
const manifest = oldManifest;
const generated = {
  '_ds_bundle.js': bundle.outputFiles[0].text,
  '_preview_runtime.js': runtime.outputFiles[0].text,
  '_ds_manifest.json': JSON.stringify(manifest) + '\n',
};
const release = { name: pkg.name, version: pkg.version, namespace, sourceHashAlgorithm: 'sha256',
  counts: { exports: components.length, cards: manifest.cards.length, templates: manifest.templates.length, tokenRecords: manifest.tokens.length },
  build: { esbuild: pkg.devDependencies.esbuild, reactPreview: pkg.devDependencies.react, sourceHashes },
  assets: Object.fromEntries(Object.entries({ ...generated,
    'release.json': await readFile(resolve(root, 'release.json'), 'utf8')
  }).map(([path, content]) => [path, { sha256: hash(content), bytes: Buffer.byteLength(content) }])),
  evidenceScope: 'Design-system package export; synthetic examples; not application production certification',
  externalRequirements: { components: 'Consumer supplied React global (React 18 or 19)', templates: 'Design Component host for x-dc/x-import/helmet elements', preview: 'Offline React production runtime and IBM Plex fonts included; see THIRD_PARTY_NOTICES.md' }
};
generated['release-build.json'] = JSON.stringify(release, null, 2) + '\n';
for (const [name, content] of Object.entries(generated)) {
  if (check) {
    if (await readFile(resolve(root, name), 'utf8').catch(() => '') !== content) throw new Error(`${name} is stale; run npm run build`);
  } else await writeFile(resolve(root, name), content);
}
console.log(`Meridian ${pkg.version}: ${components.length} exports, ${manifest.cards.length} cards, ${manifest.templates.length} templates; ${check ? 'generated files current' : 'built'}`);
