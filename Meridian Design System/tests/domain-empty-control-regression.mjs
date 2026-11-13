#!/usr/bin/env node
// Declared empty/read-only/control contracts, using package source and one local React runtime.
import assert from 'node:assert/strict';
import { build } from 'esbuild';
import { chromium } from 'playwright';
import { createHash } from 'node:crypto';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const imports = {
  AnalyticalField: 'components/charts/AnalyticalField.jsx',
  CoverageMatrix: 'components/charts/CoverageMatrix.jsx',
  ReconciliationPanel: 'components/accounting/ReconciliationPanel.jsx',
  TimeframeSwitcher: 'components/charts/TimeframeSwitcher.jsx',
  WorkspaceSection: 'components/operations/WorkspaceSection.jsx',
  SourceCoverageSummary: 'components/operations/SourceCoverageSummary.jsx',
};
const sourceHashes = Object.fromEntries(await Promise.all(Object.entries(imports).map(async ([name, path]) => [name, createHash('sha256').update(await readFile(resolve(root, path))).digest('hex')])));
const entry = `import React from 'react';import {createRoot} from 'react-dom/client';${Object.entries(imports).map(([name, path]) => `import {${name}} from './${path}';`).join('')}window.fixture={React,createRoot,${Object.keys(imports).join(',')}};`;
const built = await build({ absWorkingDir: root, stdin: { contents: entry, resolveDir: root, loader: 'jsx' }, bundle: true, write: false, format: 'iife', platform: 'browser', resolveExtensions: ['.js', '.jsx', '.ts', '.tsx', '.json'], define: { 'process.env.NODE_ENV': '"production"' }, logLevel: 'error' });
const browser = await chromium.launch({ executablePath: process.env.MERIDIAN_CHROMIUM_PATH || '/usr/bin/chromium', args: ['--no-sandbox'] });
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
page.setDefaultTimeout(5000);
const errors = [], requests = [], cases = [];
page.on('pageerror', error => errors.push(error.message));
page.on('request', request => { if (/^https?:/.test(request.url())) requests.push(request.url()); });
async function check(names, name, states, run) {
  try { await run(); cases.push({ exports: names, name, states, status: 'passed' }); }
  catch (error) { cases.push({ exports: names, name, states, status: 'failed', detail: error.stack || error.message }); console.error(`FAIL ${name}: ${error.message}`); }
}
try {
  await page.setContent('<!doctype html><html lang="en"><title>Domain empty and controlled states</title><body><main><h1>Synthetic empty and read-only contracts</h1><button id="outside">Outside</button><div id="fixture"></div></main></body></html>');
  await page.addScriptTag({ content: built.outputFiles[0].text });
  await page.evaluate(() => {
    const f = window.fixture, h = f.React.createElement, root = f.createRoot(document.getElementById('fixture'));
    window.events = []; window.revision = 0;
    const items = [{ id: 0, label: 'Zero source', value: 0, condition: 'confirmed' }, { id: 'stale', label: 'Stale source', value: 20, condition: 'stale' }, { id: 'missing', label: 'Missing source', condition: 'missing' }];
    const sources = [...items, { id: 'optional', label: 'Optional source', condition: 'na' }];
    function Demo({ kind, c, revision }) {
      f.React.useLayoutEffect(() => { document.getElementById('fixture').dataset.revision = String(revision); }, [revision]);
      if (kind === 'field') return h(f.AnalyticalField, { items: c.empty ? [] : items, selectedId: c.selectedId, columns: 2, showLegend: true, ...(c.readOnly ? {} : { onSelect: (item, index) => window.events.push({ type: 'select', item, index }) }) });
      if (kind === 'matrix') return h(f.CoverageMatrix, { rows: c.empty ? [] : [{ id: 'SYN', label: 'Synthetic instrument' }], cols: c.empty ? [] : [{ id: '2026-07-01', label: 'July 1' }], data: { SYN: { '2026-07-01': { status: 'partial', detail: '2/4 observed bars' } } }, ...(c.readOnly ? {} : { onCellClick: (row, col, cell) => window.events.push({ row, col, cell }) }) });
      if (kind === 'reconciliation') return h(f.ReconciliationPanel, { left: { title: 'Statement', items: c.loaded ? [{ id: 0, ref: 'EXACT/0', amount: 0 }] : [] }, right: { title: 'Ledger', items: c.loaded ? [{ id: 'L/0', ref: 'LEDGER/0', amount: 0 }] : [] }, onToggleItem: (id, matched) => window.events.push({ id, matched }), onToggleAll: (side, matched) => window.events.push({ side: side.title, ids: side.items.map(item => item.id), matched }) });
      if (kind === 'timeframe') return h(f.TimeframeSwitcher, { options: c.options || ['1D', '1W', '1M'], defaultValue: c.defaultValue, ...(c.value !== undefined ? { value: c.value } : {}), onChange: value => window.events.push(value) });
      if (kind === 'workspace') return h(f.WorkspaceSection, { id: 'empty-optional-region', title: c.filled ? 'Retained section' : undefined, summary: c.filled ? 'Retained context' : undefined, jump: c.filled ? 'Open retained section' : undefined, onJump: () => window.events.push('jump'), jumpHref: c.href }, c.filled ? h('p', null, 'Retained body') : undefined);
      if (kind === 'coverage') return h(f.SourceCoverageSummary, { compact: false, sources: c.empty ? [] : sources, selectedCategory: c.selectedCategory, freshnessThreshold: '24h for synthetic prices', asOf: '2026-07-01T12:00Z', weighting: 'record', showField: c.showField !== false, ...(c.readOnly ? {} : { onSelect: (source, index) => window.events.push({ type: 'source', source, index }), onFilter: (category, info) => window.events.push({ type: 'filter', category, info }) }) });
      throw new Error('Unknown fixture ' + kind);
    }
    window.mount = (kind, c = {}, fresh = true) => { const revision = ++window.revision; if (fresh) { window.fixtureKey = revision; window.events = []; } root.render(h(Demo, { kind, c, revision, key: window.fixtureKey })); return revision; };
  });
  async function mount(kind, config = {}, fresh = true) {
    const revision = await page.evaluate(([kind, config, fresh]) => window.mount(kind, config, fresh), [kind, config, fresh]);
    await page.waitForFunction(revision => document.getElementById('fixture').dataset.revision === String(revision), revision);
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
  }
  const events = () => page.evaluate(() => window.events);
  const text = () => page.locator('#fixture').textContent();

  await check(['AnalyticalField'], 'Unselected analytical population has one tab stop and exact zero identity keyboard callbacks', ['unselected', 'keyboard', 'focus', 'exact-callback', 'zero'], async () => {
    await mount('field');
    assert.equal(await page.locator('[role="gridcell"][aria-selected="true"]').count(), 0);
    assert.equal(await page.locator('.mds-af__mark[tabindex="0"]').count(), 1);
    const zero = page.getByRole('button', { name: 'Zero source, confirmed, 0', exact: true });
    await zero.focus(); await zero.press('Enter'); assert.deepEqual(await events(), [{ type: 'select', item: { id: 0, label: 'Zero source', value: 0, condition: 'confirmed' }, index: 0 }]);
    assert.equal(await page.locator('[role="gridcell"][aria-selected="true"]').count(), 0, 'A controlled selection proposal does not set selectedId');
    await zero.press('ArrowDown'); const last = page.getByRole('button', { name: 'Missing source, missing', exact: true });
    assert(await last.evaluate(node => node === document.activeElement)); await last.press('Space'); assert.equal((await events()).at(-1).index, 2);
  });
  await check(['AnalyticalField'], 'Read-only analytical marks expose population status without mutation controls and recover from empty', ['read-only', 'empty', 'recovery', 'population-state'], async () => {
    await mount('field', { readOnly: true }); assert.equal(await page.getByRole('button').count(), 1, 'Only outside control remains');
    assert.equal(await page.locator('#fixture [tabindex]').count(), 0); assert.match(await page.getByRole('img').getAttribute('aria-label'), /1 confirmed.*1 stale.*1 missing/);
    await page.getByRole('img').click(); await page.keyboard.press('Enter'); assert.deepEqual(await events(), []);
    await mount('field', { empty: true }); assert.equal(await page.getByRole('gridcell').count(), 0); assert.equal(await page.locator('#fixture button').count(), 0); assert(!/NaN|Infinity|confirmed/.test(await text()));
    await mount('field', { selectedId: 'stale' }, false); assert.equal(await page.locator('[role="gridcell"][aria-selected="true"]').count(), 1); assert.equal(await page.getByRole('button', { name: 'Stale source, stale, 20' }).getAttribute('tabindex'), '0');
  });
  await check(['CoverageMatrix'], 'Read-only coverage retains exact hover evidence, scroll focus and no cell activation', ['read-only', 'focus', 'hover', 'evidence'], async () => {
    await mount('matrix', { readOnly: true }); assert.equal(await page.locator('#fixture button').count(), 0);
    const scroll = page.getByRole('region', { name: 'Coverage matrix scroll area' }); await scroll.focus(); assert(await scroll.evaluate(node => node === document.activeElement));
    const cell = page.getByRole('cell', { name: 'Synthetic instrument · July 1 · Partial (2/4 observed bars)', exact: true });
    assert.equal(await cell.getAttribute('tabindex'), null); await cell.hover(); assert.equal(await page.locator('.mds-covm__readout').textContent(), 'Synthetic instrument · July 1 · Partial (2/4 observed bars)');
    await cell.click(); assert.deepEqual(await events(), []); await page.locator('#outside').hover(); assert.match(await page.locator('.mds-covm__readout').textContent(), /Hover a cell/);
  });
  await check(['CoverageMatrix'], 'Empty coverage has no cells or callbacks and populated recovery keeps exact normalized payload', ['empty', 'recovery', 'keyboard', 'focus', 'exact-callback'], async () => {
    await mount('matrix', { empty: true }); assert.equal(await page.getByRole('gridcell').count(), 0); assert.equal(await page.locator('#fixture button').count(), 0); await page.getByRole('grid').press('Enter'); assert.deepEqual(await events(), []);
    await mount('matrix', {}, false); const cell = page.getByRole('gridcell'); await cell.press('Space');
    assert.deepEqual(await events(), [{ row: { id: 'SYN', label: 'Synthetic instrument' }, col: { id: '2026-07-01', label: 'July 1' }, cell: { status: 'partial', detail: '2/4 observed bars' } }]);
  });
  await check(['ReconciliationPanel'], 'Loaded empty reconciliation sides disable all matching and retain filter and sort keyboard behavior', ['empty', 'disabled', 'keyboard', 'focus', 'filter', 'sort', 'zero-loaded'], async () => {
    await mount('reconciliation'); assert.equal(await page.getByText('No matching items', { exact: true }).count(), 2);
    const all = page.locator('#fixture input[type="checkbox"]'); assert.equal(await all.count(), 2);
    for (const checkbox of await all.all()) { assert(await checkbox.isDisabled()); assert(!(await checkbox.isChecked())); await checkbox.evaluate(node => node.click()); }
    await page.getByRole('button', { name: 'Open', exact: true }).press('Space'); assert.equal(await page.getByRole('button', { name: 'Open', exact: true }).getAttribute('aria-pressed'), 'true');
    const sort = page.getByRole('region', { name: 'Statement items' }).getByRole('columnheader', { name: 'Reference', exact: true }); await sort.press('Enter'); assert.equal(await sort.getAttribute('aria-sort'), 'ascending');
    await page.getByRole('textbox', { name: 'Filter reconciliation items by memo or reference' }).fill('absent'); assert.equal(await page.getByText('No matching items', { exact: true }).count(), 2); assert.deepEqual(await events(), []); assert(!/NaN|Infinity/.test(await text()));
  });
  await check(['ReconciliationPanel'], 'Population recovery enables exact zero-identifier match callbacks without treating empty filter as missing data', ['recovery', 'zero', 'exact-callback', 'keyboard', 'focus'], async () => {
    await mount('reconciliation', { loaded: true }); const zero = page.getByRole('checkbox', { name: 'Match Statement item EXACT/0', exact: true }); await zero.press('Space'); assert.deepEqual(await events(), [{ id: 0, matched: true }]);
    await page.getByRole('checkbox', { name: 'Mark all Ledger items matched', exact: true }).press('Space'); assert.deepEqual((await events()).at(-1), { side: 'Ledger', ids: ['L/0'], matched: true });
    await page.getByRole('textbox', { name: 'Filter reconciliation items by memo or reference' }).fill('absent'); assert.equal(await page.getByText('No matching items', { exact: true }).count(), 2); assert(!(await page.getByRole('checkbox', { name: 'Mark all Statement items matched', exact: true }).isDisabled()), 'Filter emptiness does not erase the loaded population');
  });
  await check(['TimeframeSwitcher'], 'Uncontrolled timeframe owns keyboard selection and preserves roving focus', ['uncontrolled', 'keyboard', 'focus', 'selection', 'callback'], async () => {
    await mount('timeframe', { defaultValue: '1W' }); const week = page.getByRole('radio', { name: '1W', exact: true }); assert.equal(await week.getAttribute('aria-checked'), 'true'); await week.press('End');
    const month = page.getByRole('radio', { name: '1M', exact: true }); assert.equal(await month.getAttribute('aria-checked'), 'true'); assert.equal(await month.getAttribute('tabindex'), '0'); assert(await month.evaluate(node => node === document.activeElement)); assert.deepEqual(await events(), ['1M']); await month.press('Home'); assert.deepEqual(await events(), ['1M', '1D']);
  });
  await check(['TimeframeSwitcher'], 'Removed active timeframe falls back to a reachable current option; empty options produce no radios or callback', ['removed-option', 'fallback', 'empty', 'recovery', 'uncontrolled', 'keyboard'], async () => {
    await mount('timeframe', { defaultValue: '1M' }); await mount('timeframe', { options: ['1D', '1W'] }, false); const day = page.getByRole('radio', { name: '1D', exact: true }); assert.equal(await day.getAttribute('aria-checked'), 'true'); assert.equal(await day.getAttribute('tabindex'), '0'); assert.deepEqual(await events(), []);
    await day.press('ArrowRight'); assert.deepEqual(await events(), ['1W']); await mount('timeframe', { options: [] }, false); assert.equal(await page.getByRole('radio').count(), 0); assert.deepEqual(await events(), ['1W']); await mount('timeframe', { options: ['1D', '1W'] }, false); assert.equal(await page.getByRole('radio', { name: '1W' }).getAttribute('aria-checked'), 'true');
  });
  await check(['WorkspaceSection'], 'Absent optional slots keep region identity without phantom jump actions and recover actual content', ['empty', 'optional-slots', 'read-only', 'recovery', 'keyboard', 'focus'], async () => {
    await mount('workspace', { href: '#retained' }); assert.equal(await page.locator('#empty-optional-region').count(), 1); assert.equal(await page.locator('#fixture h3,#fixture p,#fixture button,#fixture a').count(), 0); assert.deepEqual(await events(), []);
    await mount('workspace', { filled: true }, false); assert.equal(await page.getByRole('heading', { name: 'Retained section' }).count(), 1); assert.match(await text(), /Retained context.*Retained body/s); await page.getByRole('button', { name: 'Open retained section' }).press('Enter'); assert.deepEqual(await events(), ['jump']); await mount('workspace', {}, false); assert.equal(await page.locator('#fixture button,#fixture a').count(), 0);
  });
  await check(['SourceCoverageSummary'], 'Expanded population retains denominator, exact source callback and controlled category filter', ['expanded', 'population', 'denominator', 'zero', 'keyboard', 'focus', 'exact-callback', 'controlled'], async () => {
    await mount('coverage'); assert.match(await text(), /Denominator: 3 required of 4 in population/); assert.match(await text(), /Basis: record-weighted/); assert.match(await text(), /Freshness threshold: 24h for synthetic prices/); assert.match(await text(), /As of 2026-07-01T12:00Z/);
    assert.equal(await page.locator('.mds-af__mark').count(), 4); assert.equal(await page.locator('.mds-scs__row').count(), 5);
    await page.getByRole('button', { name: 'Zero source, confirmed, 0', exact: true }).press('Space'); assert.deepEqual(await events(), [{ type: 'source', source: { id: 0, label: 'Zero source', value: 0, condition: 'confirmed' }, index: 0 }]);
    const stale = page.locator('.mds-scs__row[data-cat="stale"]'); await stale.press('Enter'); assert.deepEqual((await events()).at(-1), { type: 'filter', category: 'stale', info: { count: 1, required: 3, population: 4 } }); assert.equal(await stale.getAttribute('aria-pressed'), 'false'); await mount('coverage', { selectedCategory: 'stale' }, false); assert.equal(await stale.getAttribute('aria-pressed'), 'true');
  });
  await check(['SourceCoverageSummary'], 'Expanded read-only and empty populations retain absence and category structure without mutation', ['expanded', 'read-only', 'empty', 'zero', 'recovery'], async () => {
    await mount('coverage', { readOnly: true }); assert.equal(await page.locator('#fixture button').count(), 0); assert.equal(await page.getByRole('img').count(), 1); assert.match(await text(), /Denominator: 3 required of 4 in population/);
    await mount('coverage', { empty: true, readOnly: true }, false); assert.equal(await page.locator('.mds-af').count(), 0); assert.equal(await page.locator('.mds-scs__row').count(), 5); assert.match(await text(), /Denominator: 0 required of 0 in population/); assert(!/NaN|Infinity/.test(await text())); assert.deepEqual(await events(), []);
    await mount('coverage', { showField: false }, false); assert.equal(await page.locator('.mds-af').count(), 0); assert.match(await text(), /Denominator: 3 required of 4 in population/); assert.equal(await page.locator('.mds-scs__row').count(), 5);
  });
} catch (error) {
  errors.push(error.stack || error.message);
} finally {
  const testSource = await readFile(fileURLToPath(import.meta.url), 'utf8');
  const exports = {};
  for (const [name, source] of Object.entries(imports)) {
    const currentHash = createHash('sha256').update(await readFile(resolve(root, source))).digest('hex');
    if (currentHash !== sourceHashes[name]) errors.push(`${name} source changed while its fixture was executing`);
    exports[name] = { source, sha256: sourceHashes[name], cases: cases.filter(item => item.exports.includes(name)).map(({ exports, ...item }) => ({ ...item, path: 'tests/domain-empty-control-regression.mjs', line: testSource.split('\n').findIndex(line => line.includes(JSON.stringify(item.name).slice(1, -1))) + 1 })) };
  }
  const report = { schemaVersion: 1, testPath: 'tests/domain-empty-control-regression.mjs', reviewedTestHash: createHash('sha256').update(testSource).digest('hex'), exports, cases, summary: { passed: cases.filter(item => item.status === 'passed').length, failed: cases.filter(item => item.status !== 'passed').length }, errors, unexpectedRequests: requests };
  const out = resolve(process.env.MERIDIAN_FULL_AUDIT_OUTPUT || '/tmp/pr3501-full-audit'); await mkdir(out, { recursive: true }); await writeFile(resolve(out, 'domain-empty-control-results.json'), JSON.stringify(report, null, 2) + '\n');
  await browser.close(); console.log(JSON.stringify(report.summary)); if (report.summary.failed || errors.length || requests.length) process.exitCode = 1;
}
