#!/usr/bin/env node
// Verify semantic roles against the actual CSS import closure and compiled component sources.
// Uses synthetic records and loopback-free Chromium; writes no generated package assets.
import assert from 'node:assert/strict';
import { readFile, readdir } from 'node:fs/promises';
import { dirname, resolve, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';
import { chromium } from 'playwright';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const sheets = new Map();
async function closure(path) {
  if (sheets.has(path)) return;
  const text = await readFile(path, 'utf8'); sheets.set(path, text);
  for (const match of text.matchAll(/@import\s+["']([^"']+)["']/g)) await closure(resolve(dirname(path), match[1]));
}
await closure(resolve(root, 'styles.css'));
const declarations = new Set([...sheets.values()].flatMap(text => [...text.matchAll(/(--[\w-]+)\s*:/g)].map(match => match[1])));
const retired = new Set(['surface-selected', 'bg-selected', 'pending-dim', 'info-dim', 'info', 'info-a10', 'red-ghost', 'red-wash', 'amber-wash', 'bg-well', 'bg-inset', 'border-heavy', 'bg-dark', 'graphite', 'action-primary-bg', 'positive-fg', 'negative-fg', 'severity-complete-fg', 'severity-complete-bd', 'severity-complete-bg', 'blue', 'accent-a10'].map(name => `--${name}`));
function validateBindings(source, label) {
  for (const match of source.matchAll(/var\(\s*(--[\w-]+)/g)) assert(!retired.has(match[1]) || declarations.has(match[1]), `${label}: undeclared role ${match[1]}`);
}
assert.throws(() => validateBindings('color:var(--info,#2F648A)', 'negative info fixture'), /undeclared role/);
assert.throws(() => validateBindings('background:var(--surface-selected,var(--bg-active))', 'negative selection fixture'), /undeclared role/);
validateBindings('color:var(--color-info);background:var(--interaction-selected-surface)', 'positive fixture');
assert(declarations.has('--accent-a10'), 'The published compatibility wash is declared in the actual CSS closure');
validateBindings('background:var(--accent-a10)', 'declared compatibility wash fixture');
async function files(path) {
  const out = [];
  for (const entry of await readdir(path, { withFileTypes: true })) {
    const full = resolve(path, entry.name);
    if (entry.isDirectory()) out.push(...await files(full));
    else if (/\.(?:jsx|html)$/.test(entry.name)) out.push(full);
  }
  return out;
}
for (const folder of ['components', 'templates', 'guidelines', 'docs']) {
  for (const path of await files(resolve(root, folder))) validateBindings(await readFile(path, 'utf8'), relative(root, path));
}
validateBindings(await readFile(resolve(root, 'scripts/create-workstation.sh'), 'utf8'), 'workstation scaffold');
function validateGeometryBindings(source, label) {
  for (const token of ['shadow-raised', 'radius-input', 'radius-control', 'z-popover', 'row-standard']) {
    assert(!new RegExp(`var\\(\\s*--${token}\\s*[,)]`).test(source), `${label}: undeclared geometry role --${token}`);
  }
}
assert.throws(() => validateGeometryBindings('border-radius:var(--radius-input,2px)', 'negative control'), /undeclared geometry role/);
assert.throws(() => validateGeometryBindings('min-height:var(--row-standard,36px)', 'negative density control'), /undeclared geometry role/);
validateGeometryBindings('border-radius:var(--radius-button,4px);min-height:var(--theme-row-height,36px)', 'positive geometry fixture');
for (const file of ['components/charts/OwnershipGraph.jsx', 'components/data/FilterBuilder.jsx', 'components/operations/ActionEnvelope.jsx', 'components/operations/ActionQueue.jsx']) {
  validateGeometryBindings(await readFile(resolve(root, file), 'utf8'), file);
}
for (const name of ['interaction-selected-surface', 'state-pending-fg', 'purple-dim', 'color-info', 'purple-a10', 'red-a10', 'orange-a10', 'surface-well', 'surface-recessed', 'border-strong', 'topbar-bg', 'brand-axis', 'ink-inverse', 'green-dim', 'red-dim', 'severity-ready-fg', 'severity-ready-bd', 'severity-ready-bg', 'brand-signal-a10', 'interaction-focus', 'shadow-card', 'radius-button', 'z-dropdown', 'theme-row-height']) assert(declarations.has(`--${name}`), `Replacement --${name} missing from styles.css dependency closure`);
console.log('PASS source bindings: positive/negative controls and every replacement declared in the CSS closure');
const compiled = await build({
  absWorkingDir: root, bundle: true, write: false, format: 'iife', globalName: 'MeridianRoleFixtures', platform: 'browser', target: 'es2020', logLevel: 'error',
  stdin: { contents: [
    "export {AssuranceChip} from './components/accounting/Assurance.jsx';",
    "export {BasisComparisonMatrix} from './components/accounting/BasisComparisonMatrix.jsx';",
    "export {ActionEnvelope,InstructionLedger} from './components/operations/ActionEnvelope.jsx';",
    "export {ActionQueue} from './components/operations/ActionQueue.jsx';",
    "export {FilterBuilder} from './components/data/FilterBuilder.jsx';",
    "export {OwnershipGraph} from './components/charts/OwnershipGraph.jsx';",
  ].join('\n'), resolveDir: root, loader: 'js' },
  jsxFactory: 'window.React.createElement', jsxFragment: 'window.React.Fragment',
  plugins: [{ name: 'consumer-react', setup(builder) {
    builder.onResolve({ filter: /^react$/ }, () => ({ path: 'react', namespace: 'consumer-react' }));
    builder.onLoad({ filter: /.*/, namespace: 'consumer-react' }, () => ({ contents: 'export default window.React; export const {useRef,useLayoutEffect,useState}=window.React;', loader: 'js' }));
  } }],
});
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });
try {
  const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  await page.setContent('<!doctype html><html lang="en"><head><title>Semantic role contracts</title></head><body class="meridian"><main><h1>Synthetic role fixtures</h1><div id="root"></div></main></body></html>');
  for (const [path, source] of sheets) {
    if (path.endsWith('/styles.css') || path.endsWith('/fonts.css')) continue;
    await page.addStyleTag({ content: source });
  }
  await page.addScriptTag({ content: await readFile(resolve(root, '_preview_runtime.js'), 'utf8') });
  await page.addScriptTag({ content: compiled.outputFiles[0].text });
  await page.evaluate(() => {
    const E = React.createElement, DS = window.MeridianRoleFixtures;
    window.testRoot = ReactDOM.createRoot(document.getElementById('root'));
    const noop = () => {};
    window.testRoot.render(E(React.Fragment, null,
      E(DS.AssuranceChip, { stage: 'prepared' }), E(DS.AssuranceChip, { stage: 'reviewed' }),
      E(DS.BasisComparisonMatrix, { bases: [{ id: 'stat', label: 'Statutory' }], rows: [{ id: 'value', label: 'Value', values: { stat: 10 } }], selectedCell: 'value::stat', onSelectCell: noop }),
      E(DS.ActionEnvelope, { label: 'Synthetic save', state: 'idle', onSubmit: noop }),
      E(DS.ActionQueue, { instructions: [{ id: 'fixture', action: 'save', label: 'Synthetic unresolved save', state: 'unlanded', idempotencyKey: 'fixture-key', receipt: { attempt: 1 } }], docked: 'inline', onRetry: noop }),
      E(DS.InstructionLedger, { instructions: [{ id: 'audit', action: 'save', label: 'Synthetic audit instruction', state: 'inflight' }] }),
      E(DS.FilterBuilder, { fields: [{ key: 'symbol', label: 'Symbol', type: 'text' }], defaultValue: [{ field: 'symbol', op: 'contains', value: 'FIXTURE' }] }),
      E(DS.OwnershipGraph, { nodes: [{ id: 'fund', label: 'Synthetic fund', type: 'Fund' }], edges: [] }),
    ));
    window.readRole = (token, property = 'color') => {
      const probe = document.createElement('span'); probe.style[property] = `var(${token})`; document.body.appendChild(probe);
      const value = getComputedStyle(probe)[property]; probe.remove(); return value;
    };
    window.ratio = (a, b) => {
      const ctx = document.createElement('canvas').getContext('2d');
      const lum = color => { ctx.clearRect(0, 0, 1, 1); ctx.fillStyle = color; ctx.fillRect(0, 0, 1, 1); return [...ctx.getImageData(0, 0, 1, 1).data].slice(0, 3).map(v => { const s = v / 255; return s <= .04045 ? s / 12.92 : ((s + .055) / 1.055) ** 2.4; }).reduce((sum, value, i) => sum + value * [.2126, .7152, .0722][i], 0); };
      const x = lum(a), y = lum(b); return (Math.max(x, y) + .05) / (Math.min(x, y) + .05);
    };
  });
  await page.waitForSelector('.mds-aq__btn[data-primary="true"]');
  await page.waitForSelector('.mds-own__node');
  const geometry = await page.evaluate(() => ({
    radii: ['.mds-fb__ctl', '.mds-act__btn', '.mds-aq__btn'].map(selector => getComputedStyle(document.querySelector(selector)).borderRadius),
    nodeShadow: getComputedStyle(document.querySelector('.mds-own__node')).boxShadow,
    queueLayer: getComputedStyle(document.querySelector('.mds-aq')).zIndex,
  }));
  assert.deepEqual(geometry.radii, ['4px', '4px', '4px']);
  assert.equal(geometry.nodeShadow, 'none');
  assert.equal(geometry.queueLayer, '100');
  for (const [density, height] of [['terminal', '26px'], ['compact', '28px'], ['default', '36px'], ['spacious', '44px']]) {
    await page.evaluate(density => document.body.dataset.themeDensity = density, density);
    assert.equal(await page.locator('.mds-led__row').evaluate(el => getComputedStyle(el).minHeight), height, `Instruction ledger follows ${density} density`);
  }
  await page.evaluate(() => delete document.body.dataset.themeDensity);
  console.log('PASS geometry: flat ownership nodes, 4px controls, dropdown layer, all four ledger densities');
  for (const theme of ['light', 'dark']) {
    await page.evaluate(theme => document.documentElement.dataset.theme = theme, theme);
    const proof = await page.evaluate(() => {
      const css = selector => getComputedStyle(document.querySelector(selector));
      const prepared = css('[data-stage="prepared"]'), reviewed = css('[data-stage="reviewed"]');
      const selected = css('.mds-bcm__cell[data-selected="true"]');
      const buttons = ['.mds-act__btn[data-primary="true"]', '.mds-aq__btn[data-primary="true"]'].map(selector => { const style = css(selector); return { fill: style.backgroundColor, ink: style.color, contrast: window.ratio(style.backgroundColor, style.color) }; });
      return { prepared: prepared.color, reviewed: reviewed.color, pendingRole: readRole('--state-pending-fg'), infoRole: readRole('--purple-dim'), preparedContrast: ratio(prepared.color, prepared.backgroundColor), reviewedContrast: ratio(reviewed.color, reviewed.backgroundColor), selected: selected.backgroundColor, selectionRole: readRole('--interaction-selected-surface', 'backgroundColor'), actionRole: readRole('--brand-axis', 'backgroundColor'), inverseRole: readRole('--ink-inverse'), buttons };
    });
    assert.equal(proof.prepared, proof.pendingRole); assert.equal(proof.reviewed, proof.infoRole); assert.notEqual(proof.prepared, proof.reviewed, 'Preparation and reviewed assurance retain different meanings');
    assert(proof.preparedContrast >= 4.5); assert(proof.reviewedContrast >= 4.5);
    assert.equal(proof.selected, proof.selectionRole);
    for (const button of proof.buttons) { assert.equal(button.fill, proof.actionRole); assert.equal(button.ink, proof.inverseRole); assert(button.contrast >= 4.5); }
    console.log(`PASS ${theme}: pending/info remain distinct, selected cell follows blue selection, primary/retry labels retain AA contrast`);
  }
  await page.evaluate(() => { document.documentElement.style.setProperty('--selected-surface', '#D8E7F5'); document.documentElement.style.setProperty('--brand-axis', '#35485C'); document.documentElement.style.setProperty('--ink-inverse', '#FFFFFF'); document.documentElement.style.setProperty('--radius-button', '6px'); document.documentElement.style.setProperty('--z-dropdown', '130'); });
  assert.equal(await page.locator('.mds-bcm__cell[data-selected="true"]').evaluate(el => getComputedStyle(el).backgroundColor), 'rgb(216, 231, 245)');
  for (const selector of ['.mds-act__btn[data-primary="true"]', '.mds-aq__btn[data-primary="true"]']) {
    assert.equal(await page.locator(selector).evaluate(el => getComputedStyle(el).backgroundColor), 'rgb(53, 72, 92)');
    assert.equal(await page.locator(selector).evaluate(el => getComputedStyle(el).color), 'rgb(255, 255, 255)');
    assert.equal(await page.locator(selector).evaluate(el => getComputedStyle(el).borderRadius), '6px');
  }
  assert.equal(await page.locator('.mds-fb__ctl').first().evaluate(el => getComputedStyle(el).borderRadius), '6px');
  assert.equal(await page.locator('.mds-aq').evaluate(el => getComputedStyle(el).zIndex), '130');
  assert.deepEqual(errors, []);
  console.log('PASS consumer role overrides propagate without literal fallback locks; no runtime errors');
} finally { await browser.close(); }
