#!/usr/bin/env node
// Public provider contracts: committed selection and screen/region reading depth.
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { mkdir, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';
import { build } from 'esbuild';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const contents = `
import React from 'react';
import {createRoot} from 'react-dom/client';
import {LinkedSelectionProvider,LinkedSelectionTarget,SelectionHooks} from './components/data/LinkedSelectionProvider.jsx';
import {Disclosure,DisclosureProvider,DisclosureHooks,DetailDial} from './components/core/Disclosure.jsx';
const h=React.createElement, root=createRoot(document.getElementById('fixture'));
window.events=[];window.apis={};window.revision=0;window.storageBlocked=false;
window.storageEntries=new Map();
window.fixtureStorage={
  getItem:key=>{if(window.storageBlocked)throw new Error('Synthetic blocked storage');return window.storageEntries.get(key)??null},
  setItem:(key,value)=>{if(window.storageBlocked)throw new Error('Synthetic blocked storage');window.storageEntries.set(key,String(value))},
  removeItem:key=>{if(window.storageBlocked)throw new Error('Synthetic blocked storage');window.storageEntries.delete(key)},
  clear:()=>window.storageEntries.clear(),
};
Object.defineProperty(window,'localStorage',{configurable:true,value:window.fixtureStorage});
function SelectionProbe({name='nearest',scope}){
  const api=SelectionHooks.useLinkedSelection(scope);
  React.useLayoutEffect(()=>{window.apis[name]=api},[name,api]);
  return h('output',{'data-probe':name},JSON.stringify({ids:api.selectedIds,scope:api.scope,count:api.count,history:api.history.map(xs=>xs.map(x=>x.id)),canGoBack:api.canGoBack}));
}
function DisclosureProbe(){
  const api=DisclosureHooks.useDisclosure();
  React.useLayoutEffect(()=>{window.apis.disclosure=api},[api]);
  return h('output',{'data-probe':'disclosure'},JSON.stringify({detail:api.detail,regions:api.regions,evidenceDetail:api.detailFor('evidence'),overridden:api.overridden('evidence')}));
}
const population=[{id:'a',value:0},{id:'b',value:20},{id:'c',value:30},{id:'d',value:40}];
function Demo({kind,c,revision}){
  React.useLayoutEffect(()=>{document.getElementById('fixture').dataset.revision=String(revision)},[revision]);
  if(kind==='empty')return h('p',null,'Unmounted fixture');
  if(kind==='nested')return h(LinkedSelectionProvider,{scope:'portfolio',defaultValue:'outer'},h(SelectionProbe,{name:'outer'}),
    h(LinkedSelectionProvider,{scope:'lot',defaultValue:'inner'},h(SelectionProbe,{name:'inner'}),h(SelectionProbe,{name:'namedOuter',scope:'portfolio'}),h(SelectionProbe,{name:'namedInner',scope:'lot'})));
  if(kind==='selection')return h(LinkedSelectionProvider,{multiple:c.multiple!==false,defaultValue:c.defaultValue??null,items:c.noItems?undefined:population,
    ...(c.controlled?{value:c.value}:{}),max:c.max,scope:c.scope,historyLimit:c.historyLimit??12,clearOnEscape:c.clearOnEscape!==false,
    urlParam:c.urlParam,storageKey:c.storageKey,onChange:(value,meta)=>window.events.push({value,meta})},
    h(SelectionProbe),population.map((item,index)=>h(LinkedSelectionTarget,{key:item.id,selection:item,index},item.id.toUpperCase())));
  if(kind==='disclosure')return h(c.carrier?Disclosure.Provider:DisclosureProvider,{screenId:c.screenId||'depth-test',defaultDetail:c.defaultDetail||'full',
    regions:c.regions,persist:c.persist!==false,urlParam:c.urlParam===undefined?'depth':c.urlParam,storage:window.fixtureStorage,
    ...(c.controlled?{detail:c.detail}:{}),onChange:event=>window.events.push(event)},h(DisclosureProbe),
    h(c.carrier?Disclosure.Dial:DetailDial),h(DetailDial,{region:'evidence'}),h(Disclosure.DetailOnly,{region:'evidence',label:'Evidence detail withheld'},'Exact evidence detail'));
}
window.mount=(kind,c={},fresh=true)=>{const revision=++window.revision;if(fresh){window.fixtureKey=revision;window.apis={};window.events=[]}root.render(h(Demo,{kind,c,revision,key:window.fixtureKey}));return revision};
window.prepare=search=>{window.storageBlocked=false;window.storageEntries.clear();window.events=[];history.replaceState(null,'','/'+search)};
`;
const { outputFiles } = await build({
  absWorkingDir: root, stdin: { contents, resolveDir: root, loader: 'jsx' },
  bundle: true, write: false, format: 'iife', platform: 'browser',
  define: { 'process.env.NODE_ENV': '"production"' }, logLevel: 'error',
});
const html = '<!doctype html><html lang="en"><title>Selection and disclosure contracts</title><body><main><h1>Synthetic provider contracts</h1><div id="fixture"></div></main><script src="/fixture.js"></script></body></html>';
const server = createServer((request, response) => {
  response.setHeader('content-type', request.url === '/fixture.js' ? 'application/javascript' : 'text/html');
  response.end(request.url === '/fixture.js' ? outputFiles[0].text : html);
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const origin = `http://127.0.0.1:${server.address().port}`;
const browser = await chromium.launch({ executablePath: process.env.MERIDIAN_CHROMIUM_PATH || '/usr/bin/chromium', args: ['--no-sandbox'] });
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
page.setDefaultTimeout(5000);
const errors = [], unexpectedRequests = [], results = [];
page.on('pageerror', error => errors.push(error.message));
page.on('request', request => { if (!request.url().startsWith(origin + '/')) unexpectedRequests.push(request.url()); });
const artifactDir = process.env.MERIDIAN_SELECTION_DISCLOSURE_ARTIFACTS || resolve(tmpdir(), 'meridian-core-selection-disclosure-regression');
await mkdir(artifactDir, { recursive: true });
const settle = () => page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
async function mount(kind, config = {}, fresh = true) {
  const revision = await page.evaluate(([kind, config, fresh]) => window.mount(kind, config, fresh), [kind, config, fresh]);
  await page.waitForFunction(revision => document.getElementById('fixture').dataset.revision === String(revision), revision);
  await settle();
}
async function start(kind, config = {}, search = '') {
  await mount('empty');
  await page.evaluate(search => window.prepare(search), search);
  await mount(kind, config);
}
const state = (name = 'nearest') => page.locator(`[data-probe="${name}"]`).textContent().then(JSON.parse);
const events = () => page.evaluate(() => window.events);
async function invoke(method, args = [], name = 'nearest') {
  await page.evaluate(([method, args, name]) => window.apis[name][method](...args), [method, args, name]);
  await settle();
}
async function check(exports, name, states, run) {
  const call = new Error().stack.split('\n')[2];
  const line = Number(call.match(/:(\d+):\d+\)?$/)?.[1]);
  try { await run(); results.push({ exports, name, states, line, status: 'passed' }); console.log(`PASS ${name}`); }
  catch (error) { results.push({ exports, name, states, line, status: 'failed', error: error.message }); console.error(`FAIL ${name}: ${error.message}`); }
}

try {
  await page.goto(origin);
  await page.waitForFunction(() => typeof window.mount === 'function');

  await check(['LinkedSelectionProvider', 'SelectionHooks'], 'Controlled selection delegates exact proposed objects and waits for external value', ['controlled-value', 'callback', 'zero', 'external-update'], async () => {
    await start('selection', { controlled: true, value: [{ id: 'a', value: 0 }], scope: 'positions' });
    assert.equal(await page.evaluate(() => window.apis.nearest.selection.value), 0);
    await invoke('select', [[{ id: 'b', value: 20 }], { source: 'synthetic-control' }]);
    assert.deepEqual((await state()).ids, ['a']);
    assert.deepEqual(await events(), [{ value: [{ id: 'b', value: 20, source: 'synthetic-control' }], meta: { source: 'synthetic-control', selections: [{ id: 'b', value: 20, source: 'synthetic-control' }] } }]);
    await mount('selection', { controlled: true, value: [{ id: 'b', value: 20 }], scope: 'positions' }, false);
    assert.deepEqual((await state()).ids, ['b']); assert.equal((await state()).scope, 'positions');
    await invoke('clear', [{ source: 'clear-request' }]); assert.deepEqual((await state()).ids, ['b']); assert.deepEqual((await events()).at(-1).value, []);
  });

  await check(['LinkedSelectionProvider', 'SelectionHooks'], 'Controlled selection persists only accepted owner state through rejected proposals and clear requests', ['controlled-value', 'url-persistence', 'storage-persistence', 'rejected-proposal', 'external-update'], async () => {
    const config = { controlled: true, value: [{ id: 'a', value: 0 }], urlParam: 'selected', storageKey: 'accepted-selection' };
    const persistedIds = () => page.evaluate(() => JSON.parse(window.storageEntries.get('accepted-selection')).map(item => item.id));
    await start('selection', config, '?unrelated=retained'); assert.equal(new URL(page.url()).searchParams.get('selected'), 'a'); assert.deepEqual(await persistedIds(), ['a']);
    await invoke('select', [{ id: 'b', value: 7.125 }]);
    assert.deepEqual((await state()).ids, ['a']); assert.equal(new URL(page.url()).searchParams.get('selected'), 'a'); assert.deepEqual(await persistedIds(), ['a']);
    assert.equal((await events()).at(-1).value[0].id, 'b');
    await mount('selection', { ...config, value: [{ id: 'b', value: 7.125 }] }, false);
    assert.deepEqual((await state()).ids, ['b']); assert.equal(new URL(page.url()).searchParams.get('selected'), 'b'); assert.deepEqual(await persistedIds(), ['b']);
    await invoke('clear'); assert.deepEqual((await events()).at(-1).value, []); assert.deepEqual((await state()).ids, ['b']);
    assert.equal(new URL(page.url()).searchParams.get('selected'), 'b'); assert.deepEqual(await persistedIds(), ['b']); assert.equal(new URL(page.url()).searchParams.get('unrelated'), 'retained');
  });

  await check(['LinkedSelectionProvider', 'SelectionHooks'], 'Selection limits preserve population order and max zero rejects new selections', ['selection-limit', 'zero-limit', 'source-order', 'deduplication'], async () => {
    await start('selection', { max: 2 }); await invoke('selectAll'); assert.deepEqual((await state()).ids, ['a', 'b']);
    await invoke('add', [{ id: 'a' }]); assert.deepEqual((await state()).ids, ['a', 'b']); assert.equal((await events()).length, 1);
    await invoke('clear'); await invoke('selectRange', ['d', 'b']); assert.deepEqual((await state()).ids, ['b', 'c']);
    await start('selection', { max: 0 }); await invoke('selectAll'); await invoke('toggle', [{ id: 0, value: 0 }]);
    assert.deepEqual((await state()).ids, []); assert.deepEqual(await events(), []);
  });

  await check(['LinkedSelectionProvider', 'SelectionHooks'], 'Initial defaults and restored selections obey declared single and multi selection limits', ['initial-selection-limit', 'zero-limit', 'controlled-value', 'storage-restoration', 'url-restoration'], async () => {
    await start('selection', { max: 0, defaultValue: ['a', 'b'] }); assert.deepEqual((await state()).ids, []);
    await start('selection', { max: 2, defaultValue: ['a', 'b', 'c'] }); assert.deepEqual((await state()).ids, ['a', 'b']);
    await start('selection', { multiple: false, defaultValue: ['a', 'b'] }); assert.deepEqual((await state()).ids, ['a']);
    await start('selection', { controlled: true, value: ['b', 'a'], max: 1 }); assert.deepEqual((await state()).ids, ['b']);
    await start('selection', { controlled: true, value: ['b'], max: 0 }); assert.deepEqual((await state()).ids, []);
    await start('empty', {}, '?selected=d,c,b,a'); await page.evaluate(() => window.storageEntries.set('initial-limit', JSON.stringify([{ id: 'a' }, { id: 'b' }, { id: 'c' }])));
    await mount('selection', { max: 2, storageKey: 'initial-limit', urlParam: 'selected' }); assert.deepEqual((await state()).ids, ['d', 'c']);
    await start('empty'); await page.evaluate(() => window.storageEntries.set('initial-limit', JSON.stringify([{ id: 'a' }, { id: 'b' }, { id: 'c' }])));
    await mount('selection', { max: 2, storageKey: 'initial-limit' }); assert.deepEqual((await state()).ids, ['a', 'b']);
  });

  await check(['LinkedSelectionProvider', 'SelectionHooks'], 'Named nested selection scopes expose outer and nearest providers independently', ['nested-context', 'named-scope', 'scope-isolation'], async () => {
    await start('nested'); assert.deepEqual((await state('inner')).ids, ['inner']); assert.deepEqual((await state('namedOuter')).ids, ['outer']);
    await invoke('select', [{ id: 'outer-next' }], 'namedOuter');
    assert.deepEqual((await state('outer')).ids, ['outer-next']); assert.deepEqual((await state('namedOuter')).ids, ['outer-next']);
    assert.deepEqual((await state('inner')).ids, ['inner']); assert.deepEqual((await state('namedInner')).ids, ['inner']);
    await invoke('select', [{ id: 'inner-next' }], 'namedInner'); assert.deepEqual((await state('inner')).ids, ['inner-next']); assert.deepEqual((await state('outer')).ids, ['outer-next']);
  });

  await check(['LinkedSelectionProvider', 'SelectionHooks', 'LinkedSelectionTarget'], 'Range and all selection use exact source order, with shift and additive target activation', ['range', 'select-all', 'source-order', 'keyboard', 'additive-selection'], async () => {
    await start('selection'); await page.getByRole('button', { name: 'B', exact: true }).press('Space');
    await page.getByRole('button', { name: 'D', exact: true }).press('Shift+Enter'); assert.deepEqual((await state()).ids, ['b', 'c', 'd']);
    await page.getByRole('button', { name: 'C', exact: true }).click({ modifiers: ['Control'] }); assert.deepEqual((await state()).ids, ['b', 'd']);
    await invoke('remove', ['b']); assert.deepEqual((await state()).ids, ['d']);
    await invoke('selectRange', ['unknown', 'a']); assert.deepEqual((await state()).ids, ['a']);
    await invoke('selectAll'); assert.deepEqual((await state()).ids, ['a', 'b', 'c', 'd']);
    await start('selection', { multiple: false }); await invoke('selectRange', ['b', 'd']); assert.deepEqual((await state()).ids, ['d']);
    assert.equal(Array.isArray((await events()).at(-1).value), false);
  });

  await check(['LinkedSelectionProvider', 'SelectionHooks'], 'Bounded history walks backward once without readding entries, and zero disables history', ['history', 'history-limit', 'back', 'deduplication', 'zero-limit'], async () => {
    await start('selection', { historyLimit: 2 });
    for (const id of ['a', 'b', 'c']) await invoke('select', [{ id }]);
    assert.deepEqual((await state()).history, [['b'], ['a']]);
    await invoke('back'); assert.deepEqual((await state()).ids, ['b']); assert.deepEqual((await state()).history, [['a']]);
    assert.equal((await events()).at(-1).meta.replaceHistory, true); assert.equal((await events()).at(-1).meta.source, 'history');
    await invoke('back'); assert.deepEqual((await state()).ids, ['a']); assert.equal((await state()).canGoBack, false);
    const count = (await events()).length; await invoke('back'); assert.equal((await events()).length, count);
    await start('selection', { historyLimit: 0 }); await invoke('select', ['b']); assert.deepEqual((await state()).history, []); assert.equal((await state()).canGoBack, false);
  });

  await check(['LinkedSelectionProvider', 'SelectionHooks'], 'Forced same-identity updates preserve exact values and explicit history replacement', ['callback', 'force', 'replace-history', 'exact-values'], async () => {
    await start('selection', { defaultValue: { id: 'a', value: 0 } });
    await invoke('select', [{ id: 'a', value: 7.125 }]); assert.deepEqual(await events(), []);
    await invoke('select', [{ id: 'a', value: 7.125 }, { force: true, replaceHistory: true, source: 'evidence-refresh' }]);
    assert.equal(await page.evaluate(() => window.apis.nearest.selection.value), 7.125);
    assert.equal(await page.evaluate(() => window.apis.nearest.selectedId), 'a'); assert.deepEqual((await state()).history, []);
    assert.deepEqual(await events(), [{ value: [{ id: 'a', value: 7.125, source: 'evidence-refresh' }], meta: { force: true, replaceHistory: true, source: 'evidence-refresh', selections: [{ id: 'a', value: 7.125, source: 'evidence-refresh' }] } }]);
  });

  await check(['LinkedSelectionProvider', 'SelectionHooks'], 'URL restoration takes precedence and persistence preserves unrelated query state and exact selected identities', ['url-restoration', 'url-persistence', 'storage-persistence', 'clear', 'source-order'], async () => {
    await start('empty', {}, '?selected=b,a&unrelated=retained');
    await page.evaluate(() => window.storageEntries.set('synthetic-selection', JSON.stringify([{ id: 'stored' }])));
    await mount('selection', { urlParam: 'selected', storageKey: 'synthetic-selection', defaultValue: 'default' });
    assert.deepEqual((await state()).ids, ['b', 'a']);
    await invoke('select', [[{ id: 0, value: 0 }, { id: 'exact-id' }], { source: 'manual' }]);
    assert.equal(new URL(page.url()).searchParams.get('selected'), '0,exact-id'); assert.equal(new URL(page.url()).searchParams.get('unrelated'), 'retained');
    assert.deepEqual(await page.evaluate(() => JSON.parse(window.storageEntries.get('synthetic-selection')).map(x => x.id)), ['0', 'exact-id']);
    await invoke('clear'); assert.equal(new URL(page.url()).searchParams.has('selected'), false); assert.equal(new URL(page.url()).searchParams.get('unrelated'), 'retained');
    assert.equal(await page.evaluate(() => window.storageEntries.has('synthetic-selection')), false);
  });

  await check(['LinkedSelectionProvider', 'SelectionHooks'], 'Valid stored selection restores; malformed and inaccessible storage recover to defaults and keep actions usable', ['storage-restoration', 'malformed-storage', 'storage-unavailable', 'recovery'], async () => {
    await start('empty'); await page.evaluate(() => window.storageEntries.set('selection-store', JSON.stringify([{ id: 'stored', value: 0 }])));
    await mount('selection', { storageKey: 'selection-store', defaultValue: 'default' }); assert.deepEqual((await state()).ids, ['stored']);
    await mount('empty'); await page.evaluate(() => window.storageEntries.set('selection-store', '{bad'));
    await mount('selection', { storageKey: 'selection-store', defaultValue: 'default' }); assert.deepEqual((await state()).ids, ['default']);
    await mount('empty'); await page.evaluate(() => window.storageBlocked = true);
    try { await mount('selection', { storageKey: 'selection-store', defaultValue: 'fallback' }); assert.deepEqual((await state()).ids, ['fallback']); await invoke('select', ['usable']); assert.deepEqual((await state()).ids, ['usable']); }
    finally { await page.evaluate(() => window.storageBlocked = false); }
  });

  await check(['LinkedSelectionProvider', 'SelectionHooks'], 'Escape clear can be disabled and its document listener is removed on unmount', ['keyboard', 'clear', 'disabled-clear', 'unmount-cleanup'], async () => {
    await start('selection', { defaultValue: 'a' }); await page.keyboard.press('Escape'); await settle(); assert.deepEqual((await state()).ids, []); assert.equal((await events()).at(-1).meta.source, 'keyboard');
    await start('selection', { defaultValue: 'b', clearOnEscape: false }); await page.keyboard.press('Escape'); await settle(); assert.deepEqual((await state()).ids, ['b']); assert.deepEqual(await events(), []);
    await start('selection', { defaultValue: 'c' }); await mount('empty', {}, false); const before = (await events()).length;
    await page.keyboard.press('Escape'); await settle(); assert.equal((await events()).length, before);
  });

  await check(['DisclosureProvider', 'DisclosureHooks', 'DetailDial', 'Disclosure'], 'Screen detail dial supports roving keyboard selection and URL mirroring without losing other query state', ['keyboard', 'focus', 'url-restoration', 'url-persistence', 'callback'], async () => {
    await start('disclosure', { carrier: true, defaultDetail: 'full', persist: false }, '?depth=summary&unrelated=retained');
    const group = page.getByRole('radiogroup', { name: 'Detail level', exact: true });
    const summary = group.getByRole('radio', { name: 'Summary', exact: true }); const full = group.getByRole('radio', { name: 'Full', exact: true });
    assert.equal((await state('disclosure')).detail, 'summary'); assert.equal(await summary.getAttribute('tabindex'), '0'); assert.equal(await full.getAttribute('tabindex'), '-1');
    await summary.focus(); await summary.press('ArrowRight'); await settle();
    assert.equal((await state('disclosure')).detail, 'full'); assert.equal(await full.getAttribute('aria-checked'), 'true'); assert(await full.evaluate(node => node === document.activeElement));
    assert.equal(new URL(page.url()).searchParams.get('depth'), 'full'); assert.equal(new URL(page.url()).searchParams.get('unrelated'), 'retained');
    assert.deepEqual((await events()).at(-1), { screenId: 'depth-test', detail: 'full', scope: 'screen' });
    await full.press('Home'); await settle(); assert.equal((await state('disclosure')).detail, 'summary'); await summary.press('End'); await settle(); assert.equal((await state('disclosure')).detail, 'full');
  });

  await check(['DisclosureProvider', 'DisclosureHooks', 'DetailDial'], 'Region detail keyboard override stays independent until keyboard reset restores screen inheritance', ['keyboard', 'focus', 'region-override', 'region-reset', 'inheritance', 'callback'], async () => {
    await start('disclosure', { defaultDetail: 'summary', persist: false, urlParam: null });
    const screen = page.getByRole('radiogroup', { name: 'Detail level', exact: true }); const region = page.getByRole('radiogroup', { name: 'Detail level — evidence', exact: true });
    await region.getByRole('radio', { name: 'Summary', exact: true }).press('ArrowRight'); await settle();
    assert.equal((await state('disclosure')).detail, 'summary'); assert.equal((await state('disclosure')).evidenceDetail, 'full');
    assert.deepEqual((await events()).at(-1), { screenId: 'depth-test', detail: 'full', scope: 'region', region: 'evidence' });
    await screen.getByRole('radio', { name: 'Full', exact: true }).press('Enter'); await settle(); await screen.getByRole('radio', { name: 'Summary', exact: true }).press('Space'); await settle();
    assert.equal((await state('disclosure')).evidenceDetail, 'full');
    const reset = page.getByRole('button', { name: '↺ screen', exact: true }); await reset.focus(); await reset.press('Enter'); await settle();
    assert.equal((await state('disclosure')).overridden, false); assert.equal((await state('disclosure')).evidenceDetail, 'summary'); assert.equal(await reset.count(), 0);
    assert(await region.getByRole('radio', { name: 'Summary', exact: true }).evaluate(node => node === document.activeElement), 'Removing the region reset button must return focus to its inherited selected radio');
    assert.deepEqual((await events()).at(-1), { screenId: 'depth-test', detail: null, scope: 'region', region: 'evidence' });
    assert(await page.getByText('follows screen', { exact: true }).isVisible());
  });

  await check(['DisclosureProvider', 'DisclosureHooks', 'DetailDial'], 'Disclosure URL depth wins over stored/default depth and invalid URL falls back safely', ['url-restoration', 'storage-restoration', 'invalid-url', 'fallback', 'persistence'], async () => {
    await start('empty', {}, '?depth=summary&unrelated=retained');
    await page.evaluate(() => window.storageEntries.set('mds.disclosure.depth-test', JSON.stringify({ detail: 'full', regions: { evidence: 'full' }, open: {} })));
    await mount('disclosure', { defaultDetail: 'full' }); assert.equal((await state('disclosure')).detail, 'summary'); assert.equal((await state('disclosure')).evidenceDetail, 'full');
    await page.getByRole('radiogroup', { name: 'Detail level', exact: true }).getByRole('radio', { name: 'Full', exact: true }).press('Space'); await settle();
    assert.equal(await page.evaluate(() => JSON.parse(window.storageEntries.get('mds.disclosure.depth-test')).detail), 'full');
    await mount('empty'); await page.evaluate(() => history.replaceState(null, '', '/?depth=invalid&unrelated=retained'));
    await mount('disclosure', { defaultDetail: 'summary' }); assert.equal((await state('disclosure')).detail, 'full'); assert.equal(new URL(page.url()).searchParams.get('depth'), 'full');
    await start('disclosure', { defaultDetail: 'summary', persist: false }, '?depth=invalid'); assert.equal((await state('disclosure')).detail, 'summary'); assert.equal(new URL(page.url()).searchParams.get('depth'), 'summary');
  });

  await check(['DisclosureProvider', 'DisclosureHooks', 'DetailDial'], 'Controlled disclosure keyboard requests preserve external depth and URL until owner updates', ['controlled-value', 'keyboard', 'callback', 'external-update', 'url-persistence'], async () => {
    await start('disclosure', { controlled: true, detail: 'full', persist: false });
    const group = page.getByRole('radiogroup', { name: 'Detail level', exact: true }); await group.getByRole('radio', { name: 'Full', exact: true }).press('ArrowLeft'); await settle();
    assert.equal((await state('disclosure')).detail, 'full'); assert.equal(new URL(page.url()).searchParams.get('depth'), 'full'); assert.equal((await events()).at(-1).detail, 'summary');
    await mount('disclosure', { controlled: true, detail: 'summary', persist: false }, false); assert.equal((await state('disclosure')).detail, 'summary'); assert.equal(new URL(page.url()).searchParams.get('depth'), 'summary');
  });

  assert.deepEqual(errors, []); assert.deepEqual(unexpectedRequests, []);
} finally {
  await writeFile(resolve(artifactDir, 'results.json'), JSON.stringify({ results, errors, unexpectedRequests }, null, 2) + '\n');
  await browser.close(); await new Promise(resolve => server.close(resolve));
}
if (results.some(result => result.status !== 'passed')) process.exitCode = 1;
console.log(JSON.stringify({ cases: results.length, passed: results.filter(result => result.status === 'passed').length, artifacts: artifactDir }));
