#!/usr/bin/env node
// Synthetic state/continuity contracts. Source modules use the same local React instance.
import assert from 'node:assert/strict';
import { build } from 'esbuild';
import { chromium } from 'playwright';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { mkdir, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const imports = {
  WorkspaceLayoutProvider: 'shell/WorkspaceLayout.jsx', WorkspaceLayoutBar: 'shell/WorkspaceLayout.jsx',
  WorkspaceLayoutGrid: 'shell/WorkspaceLayout.jsx', LayoutRegion: 'shell/WorkspaceLayout.jsx', LayoutHooks: 'shell/WorkspaceLayout.jsx',
  WorkspaceContextProvider: 'shell/WorkspaceContextBar.jsx', WorkspaceContextBar: 'shell/WorkspaceContextBar.jsx', WorkspaceHooks: 'shell/WorkspaceContextBar.jsx',
  ObjectTrailProvider: 'core/ObjectTrail.jsx', ObjectTrail: 'core/ObjectTrail.jsx', ObjectTrailList: 'core/ObjectTrail.jsx', TrailHooks: 'core/ObjectTrail.jsx',
  ObjectScopeProvider: 'core/ObjectScope.jsx', ScopedValue: 'core/ObjectScope.jsx', ScopeMismatch: 'core/ObjectScope.jsx', ScopeHooks: 'core/ObjectScope.jsx',
  TimeContextProvider: 'core/TimeContext.jsx', TimeContextPanel: 'core/TimeContext.jsx', TimeRole: 'core/TimeContext.jsx', TimeHooks: 'core/TimeContext.jsx',
  Disclosure: 'core/Disclosure.jsx', SelectionCompare: 'data/SelectionCompare.jsx', UserMenu: 'shell/SessionControls.jsx', ReturnPath: 'shell/ReturnPath.jsx'
};
const contents = `import React from 'react';import {createRoot} from 'react-dom/client';${Object.entries(imports).map(([name, file]) => `import {${name}} from './components/${file}';`).join('')}window.fixture={React,createRoot,${Object.keys(imports).join(',')}};`;
const { outputFiles } = await build({ absWorkingDir: root, stdin: { contents, resolveDir: root, loader: 'jsx' }, bundle: true, write: false, format: 'iife', platform: 'browser', define: { 'process.env.NODE_ENV': '"production"' }, logLevel: 'error' });
const artifactDir = process.env.MERIDIAN_STATE_ARTIFACTS || resolve(tmpdir(), 'meridian-core-state-regression');
await mkdir(artifactDir, { recursive: true });
const browser = await chromium.launch({ executablePath: process.env.MERIDIAN_CHROMIUM_PATH || '/usr/bin/chromium', args: ['--no-sandbox'] });
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
page.setDefaultTimeout(5000);
const errors = [], requests = [], results = [];
page.on('pageerror', error => errors.push(error.message));
page.on('request', request => { if (/^https?:/.test(request.url())) requests.push(request.url()); });
try {
  await page.setContent('<!doctype html><html lang="en"><title>Continuity contracts</title><style>body{font-family:system-ui}#fixture{max-width:1200px}button:focus-visible{outline:2px solid #2d6fb3}</style><button id="outside">Outside trigger</button><main><h1>Synthetic continuity contracts</h1><div id="fixture"></div></main></html>');
  await page.addScriptTag({ content: outputFiles[0].text });
  await page.evaluate(() => {
    const f = window.fixture, h = f.React.createElement, root = f.createRoot(document.getElementById('fixture'));
    const initialNow = Date.parse('2026-07-31T12:00:00Z'); Date.now = () => initialNow;
    window.events = []; window.revision = 0; window.storeData = new Map(); window.storeBlocked = false;
    window.storage = { getItem(key) { if (window.storeBlocked) throw new Error('Synthetic storage blocked'); return window.storeData.get(key) ?? null; }, setItem(key, value) { if (window.storeBlocked) throw new Error('Synthetic storage blocked'); window.storeData.set(key, value); } };
    function Probe({ kind }) {
      const api = kind === 'layout' ? f.LayoutHooks.useWorkspaceLayout() : kind === 'context' ? f.WorkspaceHooks.useWorkspaceContext() : kind === 'trail' ? f.TrailHooks.useObjectTrail() : kind === 'scope' ? f.ScopeHooks.useScope() : kind === 'time' ? f.TimeHooks.useTimeContext() : kind === 'disclosure' ? f.Disclosure.useDisclosure() : f.SelectionCompare.useSelectionCompare();
      window.api = api;
      const state = kind === 'layout' ? { active: api.active, dirty: api.dirty, restored: api.restored, regions: api.regions, names: api.names, screenId: api.screenId } : kind === 'context' ? api.context : kind === 'trail' ? { current: api.current, recents: api.recents, pinned: api.pinned, canBack: api.canBack, canForward: api.canForward, notice: api.notice, screenId: api.screenId } : kind === 'time' ? api.time : kind === 'disclosure' ? { detail: api.detail, regions: api.regions, screenId: api.screenId } : kind === 'compare' ? { items: api.items, max: api.max, open: api.open } : { scope: api.scope, parent: api.parent };
      return h('output', { 'data-testid': 'state' }, JSON.stringify(state));
    }
    const alpha = { id: 'entry-A', type: 'entry', label: 'Journal A', version: 1, currentVersion: 2 };
    const beta = { id: 'entry-B', type: 'entry', label: 'Journal B', version: 2, currentVersion: 2 };
    const plates = [{ id: 'a', title: 'Alpha', values: [0, 1] }, { id: 'b', title: 'Beta', values: [2, 3] }, { id: 'c', title: 'Gamma', values: [4, 5] }];
    window.entries = { alpha, beta }; window.plates = plates;
    function Demo({ kind, config: c, revision }) {
      f.React.useLayoutEffect(() => { document.getElementById('fixture').dataset.revision = String(revision); }, [revision]);
      if (kind === 'layout') return h(f.WorkspaceLayoutProvider, { screenId: c.screenId || 'layout-A', regions: c.regions || [{ id: 'left', size: 1 }, { id: 'right', size: 1 }], layouts: [{ name: 'Review', regions: { left: { size: 2 }, right: { size: 1 } } }], persist: c.persist !== false, storage: window.storage, onChange: event => window.events.push(event) }, h(Probe, { kind }), h(f.WorkspaceLayoutBar), h(f.WorkspaceLayoutGrid, { style: { width: 800, height: 240 } }, h(f.LayoutRegion, { id: 'left', label: 'Evidence', minSize: .25 }, h('button', null, 'Inspect evidence')), h(f.LayoutRegion, { id: 'right', label: 'Review', minSize: .25 }, 'Review content')));
      if (kind === 'context') return h(f.WorkspaceContextProvider, { ...(c.controlled ? { value: c.value || { currency: 'USD', period: '2026-07' } } : { defaultValue: { currency: 'EUR', period: '2026-07' } }), onChange: event => window.events.push(event) }, h(Probe, { kind }), h(f.WorkspaceContextBar, { collapse: c.collapse || 'always', minVisible: 2, style: { width: c.width || 700 }, value: c.barValue, onBookClick: () => window.events.push('book'), onPeriodClick: () => window.events.push('period'), onScenarioClick: () => window.events.push('scenario') }));
      if (kind === 'trail') return h(f.ObjectTrailProvider, { screenId: c.screenId || 'trail-A', defaultEntries: c.empty ? [] : [alpha], persist: c.persist !== false, storage: window.storage, max: 2, onNavigate: (...args) => window.events.push(args) }, h(Probe, { kind }), h(f.ObjectTrail), h(f.ObjectTrailList, { heading: 'Session visits', showPinned: false }));
      if (kind === 'scope') return h(f.ObjectScopeProvider, { scope: c.scope || 'portfolio', object: 'portfolio-A' }, h(Probe, { kind }), h(f.ScopeMismatch), c.foreign !== false && h(f.ScopedValue, { id: 'duration', scope: c.figureScope || 'security', label: 'Duration', value: c.value === undefined ? 0 : c.value, explain: true }), h(f.ScopedValue, { id: 'balance', label: 'Balance', value: null }));
      if (kind === 'time') return h(f.TimeContextProvider, { defaultValue: { trade: '2026-07-02' }, ...(c.controlled ? { value: c.value || { trade: '2026-07-02' } } : {}), onChange: event => window.events.push(event) }, h(Probe, { kind }), h(f.TimeRole, { role: 'trade' }), h(f.TimeContextPanel, { roles: ['trade', 'settlement', 'reportingPeriod'], showMissing: true }));
      if (kind === 'disclosure') return h(f.Disclosure.Provider, { screenId: c.screenId || 'disclosure-A', defaultDetail: c.defaultDetail || 'summary', persist: c.persist !== false, storage: window.storage, urlParam: null, ...(c.controlled ? { detail: c.detail || 'full' } : {}), onChange: event => window.events.push(event) }, h(Probe, { kind }), h(f.Disclosure.Dial), h(f.Disclosure.Region, { id: 'exceptions', title: 'Exceptions', secondary: true, count: 0, countLabel: 'items', severity: 'critical', freshness: '12:00Z' }, h('button', null, 'Inspect exception')), h(f.Disclosure.Region, { id: 'evidence', title: 'Evidence', withheld: 3 }, 'Summary row', h(f.Disclosure.DetailOnly, { region: 'evidence', label: 'Three additional evidence rows withheld' }, 'Retained detailed evidence')));
      if (kind === 'compare') return h(f.SelectionCompare.Provider, { max: c.max ?? 2, categories: ['Q1', 'Q2'], onChange: items => window.events.push({ type: 'change', ids: items.map(item => item.id) }), onCompare: items => window.events.push({ type: 'compare', ids: items.map(item => item.id) }) }, h(Probe, { kind }), plates.map(item => h(f.SelectionCompare.Toggle, { key: item.id, item, compact: c.compact })), h(f.SelectionCompare.Tray, { emptyLabel: 'Select records' }), h(f.SelectionCompare.Sheet));
      if (kind === 'user') return h(f.UserMenu, { name: 'Ada Lane', role: 'operator', detail: 'Synthetic tenant', items: [{ label: 'Preferences', onSelect: () => window.events.push('preferences') }, { label: 'Session details', onSelect: () => window.events.push('details') }], onSignOut: () => window.events.push('signout') });
      if (kind === 'return') return h(f.ReturnPath, { loops: c.empty ? [] : [{ to: 'break', id: 'A/B', verb: 'Source break', href: '#/break?id=A%2FB', open: () => window.events.push('break') }, { to: 'entry', id: 0, verb: 'Original entry', open: () => window.events.push('entry') }], emptyText: c.emptyText, showIds: c.showIds !== false });
      return null;
    }
    window.mount = (kind, config = {}, fresh = true) => { const revision = ++window.revision; if (fresh) { window.lastKey = revision; window.events = []; } root.render(h(Demo, { kind, config, revision, key: window.lastKey })); return revision; };
  });
  async function mount(kind, config = {}, fresh = true) { const n = await page.evaluate(({ kind, config, fresh }) => window.mount(kind, config, fresh), { kind, config, fresh }); await page.waitForFunction(n => document.getElementById('fixture').dataset.revision === String(n), n); }
  async function invoke(method, ...args) { await page.evaluate(({ method, args }) => window.api[method](...args), { method, args }); await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))); }
  const state = () => page.getByTestId('state').textContent().then(JSON.parse);
  async function check(name, run) { const start = Date.now(); try { await run(); results.push({ name, status: 'passed', elapsedMs: Date.now() - start }); } catch (error) { results.push({ name, status: 'failed', detail: error.stack || error.message }); } }
  await check('WorkspaceLayout filters data context, saves the exact Default arrangement, resets and restores', async () => {
    await mount('layout'); await invoke('setRegion', 'left', { size: 2, period: '2020-01', selection: 'secret', collapsed: false });
    assert.deepEqual((await state()).regions.left, { size: 2, collapsed: false, hidden: false }); assert.equal((await state()).dirty, true);
    await page.getByRole('button', { name: 'Save layout Default', exact: true }).click(); assert.equal((await state()).dirty, false);
    await page.getByRole('combobox', { name: 'Active workspace layout' }).selectOption('Review'); await page.getByRole('combobox', { name: 'Active workspace layout' }).selectOption('Default');
    assert.equal((await state()).regions.left.size, 2); assert.equal(await page.getByRole('option', { name: 'Default', exact: true }).count(), 1);
    await invoke('setRegion', 'left', { size: 3 }); await page.getByRole('button', { name: 'Reset layout Default to its saved arrangement' }).click(); assert.equal((await state()).regions.left.size, 2);
    await mount('layout'); assert.equal((await state()).restored, true); assert.equal((await state()).regions.left.size, 2);
    assert(!JSON.stringify(await page.evaluate(() => [...window.storeData.entries()])).includes('secret'));
  });
  await check('WorkspaceLayout separator keyboard, collapse focus and hidden-region recovery', async () => {
    await mount('layout', { persist: false }); const divider = page.getByRole('separator', { name: 'Resize Evidence' }); await divider.focus(); await divider.press('ArrowRight');
    assert.equal((await state()).regions.left.size, 1.08); assert(Math.abs((await state()).regions.right.size - .92) < 1e-10);
    await page.getByRole('button', { name: 'Collapse Evidence' }).focus(); await page.keyboard.press('Enter');
    assert(await page.getByRole('button', { name: 'Expand Evidence' }).evaluate(node => node === document.activeElement)); assert.equal(await page.getByRole('separator').count(), 0);
    await page.keyboard.press('Enter'); assert(await page.getByRole('button', { name: 'Collapse Evidence' }).evaluate(node => node === document.activeElement));
    await invoke('setRegion', 'right', { hidden: true }); await page.getByRole('button', { name: 'Show 1 hidden region: Review' }).press('Enter'); assert.equal((await state()).regions.right.hidden, false);
  });
  await check('WorkspaceLayout isolates changed screen IDs without remounting the consumer', async () => {
    await mount('layout', { screenId: 'layout-first' }); await invoke('setRegion', 'left', { size: 4 }); await invoke('save', 'Mine');
    await mount('layout', { screenId: 'layout-second' }, false); assert.equal((await state()).screenId, 'layout-second'); assert.equal((await state()).regions.left.size, 1); assert.equal((await state()).active, 'Default');
    await mount('layout', { screenId: 'layout-first' }, false); assert.equal((await state()).regions.left.size, 4);
  });
  await check('WorkspaceLayout survives malformed and blocked persistence', async () => {
    await page.evaluate(() => window.storeData.set('mds.workspace-layout.broken', '{bad')); await mount('layout', { screenId: 'broken' }); assert.equal((await state()).regions.left.size, 1);
    await page.evaluate(() => window.storeBlocked = true); try { await mount('layout', { screenId: 'blocked' }); await invoke('setRegion', 'left', { size: 3 }); assert.equal((await state()).regions.left.size, 3); } finally { await page.evaluate(() => window.storeBlocked = false); }
  });
  await check('WorkspaceContext controlled values, functional updates and reset preserve source ownership', async () => {
    await mount('context', { controlled: true }); await invoke('update', { currency: 'JPY' }); assert.equal((await state()).currency, 'USD'); assert.equal(await page.evaluate(() => window.events.at(-1).currency), 'JPY');
    await mount('context', { controlled: true, value: { currency: 'JPY', period: '2026-08' } }, false); assert.equal((await state()).currency, 'JPY');
    await mount('context'); await invoke('update', { currency: 'GBP' }); assert.equal((await state()).currency, 'GBP'); await page.evaluate(() => window.api.update(previous => ({ currency: previous.currency === 'GBP' ? 'CHF' : 'EUR' }))); await page.waitForFunction(() => window.api.context.currency === 'CHF'); assert.equal((await state()).period, '2026-07'); await invoke('reset'); assert.equal((await state()).currency, 'EUR');
  });
  await check('WorkspaceContext overflow preserves priority, current values, Escape and outside dismissal', async () => {
    await mount('context'); const nav = page.getByRole('navigation', { name: 'Workspace context' }); const more = nav.getByRole('button', { name: /^More/ });
    assert(await nav.locator(':scope > .mds-wctx__item').getByText('General Account', { exact: true }).isVisible()); assert.equal(await nav.locator(':scope > .mds-wctx__item').count(), 2);
    await more.press('Enter'); const overflow = page.getByRole('group', { name: 'Additional workspace context' }); assert(await overflow.getByRole('button', { name: /Currency EUR/ }).count() === 0); assert(await overflow.getByText('EUR', { exact: true }).isVisible());
    await overflow.getByRole('button', { name: /Book STAT/ }).focus(); await page.keyboard.press('Escape'); assert.equal(await overflow.count(), 0); assert(await more.evaluate(node => node === document.activeElement));
    await more.press('Enter'); await page.locator('#outside').click(); assert.equal(await overflow.count(), 0);
    await mount('context', { collapse: 'never' }, false); assert.equal(await nav.getByRole('button', { name: /^More/ }).count(), 0);
  });
  await check('WorkspaceContext remeasures changed field lengths at fixed viewport width', async () => {
    await mount('context', { collapse: 'auto', width: 1100, barValue: { entity: { type: 'Fund', label: 'A' }, asOf: 'Now' } });
    const before = await page.locator('.mds-wctx > .mds-wctx__item').count(); await mount('context', { collapse: 'auto', width: 1100, barValue: { entity: { type: 'Fund', label: 'A very long retained institution name requiring substantially more horizontal room' }, asOf: '2026-07-31T23:59:59.999Z — retained observation clock' } }, false);
    await page.waitForFunction(before => document.querySelectorAll('.mds-wctx > .mds-wctx__item').length < before, before); assert(await page.getByRole('button', { name: /^More/ }).isVisible());
  });
  await check('ObjectTrail exact identity, branch history, moved versions, pinning and retained notices', async () => {
    await mount('trail', { persist: false }); await page.getByRole('button', { name: 'Pin this object' }).press('Space'); await page.evaluate(() => window.api.visit(window.entries.beta)); await page.waitForFunction(() => window.api.current?.id === 'entry-B' && window.api.canBack);
    await page.getByRole('button', { name: 'Back to the previous object' }).press('Enter'); assert.equal((await state()).current.id, 'entry-A'); assert.equal((await state()).notice.type, 'version-moved');
    assert.equal(await page.evaluate(() => window.events.at(-1)[1].versionMoved), true); assert(await page.getByRole('status').getByText('Version moved', { exact: true }).isVisible());
    await page.getByRole('button', { name: 'Dismiss', exact: true }).click(); assert.equal((await state()).notice, null); await page.getByRole('button', { name: 'Forward', exact: true }).press('Enter');
    await page.evaluate(() => window.api.visit({ id: 'entry-C', type: 'entry', label: 'Journal C' })); await page.waitForFunction(() => window.api.current.id === 'entry-C');
    assert.deepEqual((await state()).recents.map(entry => entry.id), ['entry-C', 'entry-B']); assert.equal((await state()).pinned[0].id, 'entry-A');
  });
  await check('ObjectTrail recent keyboard dismissal restores the trigger and clears unpinned history', async () => {
    await mount('trail', { persist: false }); await page.evaluate(() => window.api.visit(window.entries.beta)); await page.waitForFunction(() => window.api.recents.length === 2);
    const recent = page.getByRole('button', { name: /^Recent/ }); await recent.press('Enter'); const panel = page.getByRole('group', { name: 'Recent and pinned objects' }); await panel.getByRole('button', { name: /Journal A/ }).focus(); await page.keyboard.press('Escape'); assert.equal(await panel.count(), 0); assert(await recent.evaluate(node => node === document.activeElement));
    await recent.press('Enter'); await panel.getByRole('button', { name: 'Clear', exact: true }).click(); assert.equal((await state()).recents.length, 0); assert(await panel.getByText('No objects opened in this session.').isVisible());
  });
  await check('ObjectTrail per-screen persistence is isolated on same-mounted scope changes', async () => {
    await mount('trail', { screenId: 'trail-first', empty: true }); await page.evaluate(() => window.api.visit(window.entries.beta)); await page.waitForFunction(() => window.api.current?.id === 'entry-B');
    await mount('trail', { screenId: 'trail-second', empty: true }, false); assert.equal((await state()).current, null); assert.equal((await state()).recents.length, 0);
    await mount('trail', { screenId: 'trail-first', empty: true }, false); assert.equal((await state()).current.id, 'entry-B');
  });
  await check('ObjectScope reports foreign figures, removes unmounted entries and preserves zero/missing distinction', async () => {
    await mount('scope'); await page.getByRole('note').waitFor(); assert(await page.getByRole('note').getByText('Duration', { exact: true }).isVisible());
    assert.equal(await page.locator('.mds-scoped[data-scope="security"] .mds-scoped__n').textContent(), '0'); assert.equal(await page.locator('.mds-scoped:not([data-scope="security"]) .mds-scoped__n').textContent(), '—');
    await mount('scope', { foreign: false }, false); await page.waitForFunction(() => !document.querySelector('.mds-scopemis'));
    await mount('scope', { figureScope: 'portfolio' }, false); assert.equal(await page.getByRole('note').count(), 0);
    await page.evaluate(() => { window.scopeEvents = []; window.unsubscribe = window.api.subscribe(items => window.scopeEvents.push(items)); window.api.report('manual', { scope: 'security', label: 'Price', value: 0 }); window.unsubscribe(); window.api.report('manual', null); });
    assert.equal(await page.evaluate(() => window.scopeEvents.length), 2);
  });
  await check('TimeContext controlled updates, missing-role rendering and temporal error recovery', async () => {
    await mount('time', { controlled: true }); await invoke('update', { settlement: '2026-07-01' }); assert.equal((await state()).settlement, undefined); assert.equal(await page.evaluate(() => window.events.at(-1).settlement), '2026-07-01');
    await mount('time'); assert.equal(await page.locator('[data-time-role="settlement"] [data-missing="on"]').textContent(), '—'); await invoke('update', { settlement: '2026-07-01' });
    assert(await page.getByText('Settlement date precedes the trade date.', { exact: true }).isVisible()); assert.equal(await page.locator('[data-time-role="trade"][data-flagged="on"]').count(), 1);
    await invoke('update', { settlement: '2026-07-03' }); assert.equal(await page.getByText('Settlement date precedes the trade date.', { exact: true }).count(), 0); assert(await page.getByText('Dates are coherent — no ordering, period, or staleness conflict.').isVisible());
  });
  await check('TimeContext reset restores the default role set without stale optional dates', async () => {
    await mount('time'); await invoke('update', { settlement: '2026-07-01' }); await invoke('reset'); assert.deepEqual(await state(), { trade: '2026-07-02' });
  });
  await check('Disclosure summary retains severity/zero/freshness, keyboard pins and reveals withheld evidence', async () => {
    await mount('disclosure', { persist: false }); const exceptions = page.getByRole('button', { name: 'Exceptions', exact: true }); assert.equal(await exceptions.getAttribute('aria-expanded'), 'false');
    assert(await page.getByText('0 items', { exact: true }).isVisible()); assert((await page.locator('.mds-disc__sev--critical').textContent()).includes('critical')); assert(await page.getByText('◷ 12:00Z', { exact: true }).isVisible());
    await exceptions.press('Space'); assert.equal(await exceptions.getAttribute('aria-expanded'), 'true'); await invoke('setDetail', 'full'); await invoke('setDetail', 'summary'); assert.equal(await exceptions.getAttribute('aria-expanded'), 'true');
    const evidenceRegion = page.locator('.mds-disc').filter({ has: page.getByRole('button', { name: 'Evidence', exact: true }) }); assert(!(await evidenceRegion.textContent()).includes('Retained detailed evidence')); await page.getByRole('button', { name: 'Show full detail', exact: true }).click(); await page.waitForFunction(() => window.api.regions.evidence === 'full'); assert((await evidenceRegion.textContent()).includes('Retained detailed evidence'));
    await invoke('reset'); assert.equal(await exceptions.getAttribute('aria-expanded'), 'false');
  });
  await check('Disclosure restores persisted depth and per-screen isolation without consumer remount', async () => {
    await page.evaluate(() => window.storeData.set('mds.disclosure.depth-first', JSON.stringify({ detail: 'full', regions: { evidence: 'summary' }, open: { exceptions: false } })));
    await mount('disclosure', { screenId: 'depth-first' }); assert.equal((await state()).detail, 'full'); assert.equal(await page.getByRole('button', { name: 'Exceptions', exact: true }).getAttribute('aria-expanded'), 'false');
    await mount('disclosure', { screenId: 'depth-second' }, false); assert.equal((await state()).detail, 'summary'); assert.deepEqual((await state()).regions, {});
    await mount('disclosure', { screenId: 'depth-first' }, false); assert.equal((await state()).detail, 'full');
  });
  await check('Disclosure controlled depth delegates updates and malformed/blocked storage recovers', async () => {
    await mount('disclosure', { controlled: true, persist: false }); await invoke('setDetail', 'summary'); assert.equal((await state()).detail, 'full'); assert.equal(await page.evaluate(() => window.events.at(-1).detail), 'summary');
    await page.evaluate(() => { window.storeData.set('mds.disclosure.malformed', 'not-json'); }); await mount('disclosure', { screenId: 'malformed' }); assert.equal((await state()).detail, 'summary');
    await page.evaluate(() => window.storeBlocked = true); try { await mount('disclosure'); await invoke('setDetail', 'full'); assert.equal((await state()).detail, 'full'); } finally { await page.evaluate(() => window.storeBlocked = false); }
  });
  await check('SelectionCompare max, keyboard toggle, exact callback, overlay trap and focus recovery', async () => {
    await mount('compare'); const alpha = page.getByRole('button', { name: 'Compare: Alpha; add to comparison' }); await alpha.press('Space'); assert.equal((await state()).items[0].id, 'a');
    assert(await page.getByRole('button', { name: 'Compare', exact: true }).isDisabled()); await page.getByRole('button', { name: 'Compare: Beta; add to comparison' }).press('Enter'); assert(await page.getByRole('button', { name: 'Compare: Gamma; add to comparison' }).isDisabled());
    const compare = page.getByRole('button', { name: 'Compare 2', exact: true }); await compare.press('Enter'); const dialog = page.getByRole('dialog', { name: 'Comparison by selection' }); await dialog.waitFor();
    assert.deepEqual(await page.evaluate(() => window.events.filter(event => event.type === 'compare')), [{ type: 'compare', ids: ['a', 'b'] }]);
    assert(await dialog.evaluate(node => node.contains(document.activeElement))); await page.getByRole('button', { name: 'Close comparison' }).focus(); await page.keyboard.press('Tab'); assert(await dialog.evaluate(node => node.contains(document.activeElement)));
    await page.keyboard.press('Escape'); assert.equal(await dialog.count(), 0); assert(await compare.evaluate(node => node === document.activeElement)); await page.getByRole('button', { name: 'Remove Alpha from comparison' }).click(); assert.equal((await state()).items.length, 1);
    await page.getByRole('region', { name: 'Comparison set', exact: true }).getByRole('button', { name: 'Clear', exact: true }).press('Space'); assert.equal((await state()).items.length, 0); assert(await page.getByText('Select records', { exact: true }).isVisible());
    await alpha.press('Enter'); await page.getByRole('button', { name: 'In compare: Alpha; remove from comparison' }).press('Space'); assert.equal((await state()).items.length, 0);
  });
  await check('SelectionCompare compact toggles retain a minimum 24 by 24 keyboard target', async () => {
    await mount('compare', { compact: true }); const toggle = page.getByRole('button', { name: 'Compare: Alpha; add to comparison' }); const box = await toggle.boundingBox(); assert(box.width >= 24); assert(box.height >= 24); await toggle.press('Space'); assert.equal((await state()).items[0].id, 'a');
  });
  await check('SelectionCompare zero max is enforced by the public hook as well as disabled UI', async () => {
    await mount('compare', { max: 0 }); assert(await page.getByRole('button', { name: 'Compare: Alpha; add to comparison' }).isDisabled()); await page.evaluate(() => window.api.toggle(window.plates[0])); await page.evaluate(() => new Promise(resolve => requestAnimationFrame(resolve))); assert.equal((await state()).items.length, 0);
  });
  await check('SelectionCompare clear closes the active comparison before a new candidate set', async () => {
    await mount('compare'); await page.getByRole('button', { name: 'Compare: Alpha; add to comparison' }).click(); await page.getByRole('button', { name: 'Compare: Beta; add to comparison' }).click(); await page.getByRole('button', { name: 'Compare 2', exact: true }).click(); await page.getByRole('button', { name: 'Clear set', exact: true }).click(); await page.waitForFunction(() => window.api.open === false); assert.equal((await state()).open, false);
    await page.getByRole('button', { name: 'Compare: Alpha; add to comparison' }).click(); await page.getByRole('button', { name: 'Compare: Beta; add to comparison' }).click(); assert.equal(await page.getByRole('dialog').count(), 0);
  });
  await check('UserMenu keyboard entry, directional focus, exactly-once dispatch and Escape return', async () => {
    await mount('user'); const trigger = page.getByRole('button', { name: 'AL — Account: Ada Lane' }); await trigger.focus(); await trigger.press('ArrowDown'); const preferences = page.getByRole('menuitem', { name: 'Preferences' }); await preferences.waitFor(); assert(await preferences.evaluate(node => node === document.activeElement));
    await page.keyboard.press('End'); assert(await page.getByRole('menuitem', { name: 'Sign out' }).evaluate(node => node === document.activeElement)); await page.keyboard.press('Home'); await page.keyboard.press('Enter'); assert.deepEqual(await page.evaluate(() => window.events), ['preferences']); assert(await trigger.evaluate(node => node === document.activeElement));
    await trigger.press('Enter'); await page.getByRole('menuitem', { name: 'Session details' }).focus(); await page.keyboard.press('Escape'); assert.equal(await page.getByRole('menu').count(), 0); assert(await trigger.evaluate(node => node === document.activeElement));
    await trigger.click(); await page.locator('#outside').click(); assert.equal(await page.getByRole('menu').count(), 0);
  });
  await check('ReturnPath exact href/zero identity, ordinary callback and modified-link preservation', async () => {
    await mount('return'); const link = page.getByRole('link', { name: 'Source break A/B' }); assert.equal(await link.getAttribute('href'), '#/break?id=A%2FB');
    const outcomes = await link.evaluate(node => [
      node.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 })),
      node.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 0, ctrlKey: true })),
      node.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 1 }))
    ]); assert.deepEqual(outcomes, [false, true, true]); assert.deepEqual(await page.evaluate(() => window.events), ['break']);
    await page.getByRole('button', { name: 'Original entry 0' }).press('Enter'); assert.deepEqual(await page.evaluate(() => window.events), ['break', 'entry']);
    await mount('return', { empty: true }); assert.equal(await page.locator('#fixture').textContent(), ''); await mount('return', { empty: true, emptyText: 'No retained source link' }); assert(await page.getByText('No retained source link').isVisible());
  });
  await check('No page exceptions or external network are required by state fixtures', async () => { assert.deepEqual(errors, []); assert.deepEqual(requests, []); });
  await writeFile(resolve(artifactDir, 'results.json'), JSON.stringify({ scope: 'Synthetic package source state/keyboard/persistence contracts; no live host/provider certification', results, errors, requests }, null, 2) + '\n');
  for (const result of results) console.log(`${result.status === 'passed' ? 'PASS' : 'FAIL'} ${result.name}${result.status === 'failed' ? '\n' + result.detail : ''}`);
  console.log(`State contracts: ${results.filter(result => result.status === 'passed').length}/${results.length} passed; evidence ${resolve(artifactDir, 'results.json')}`);
  assert.equal(results.filter(result => result.status === 'failed').length, 0, 'See individual state contract failures above');
} finally { await browser.close(); }
