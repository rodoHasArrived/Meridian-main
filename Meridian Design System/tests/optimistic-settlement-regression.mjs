#!/usr/bin/env node
// Source-level synthetic regression: observer failures cannot rewrite transport outcomes.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const source = await build({
  absWorkingDir: root, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2020',
  resolveExtensions: ['.js', '.jsx', '.ts', '.tsx', '.json'],
  stdin: { contents: `import { OptimisticProvider, OptimisticHooks } from './components/data/OptimisticCell.jsx'; window.OptimisticUnderTest = { OptimisticProvider, OptimisticHooks };`, resolveDir: root, loader: 'js' },
  jsxFactory: 'window.React.createElement', jsxFragment: 'window.React.Fragment',
  plugins: [{ name: 'react-peer', setup(builder) {
    builder.onResolve({ filter: /^react$/ }, () => ({ path: 'react', namespace: 'peer' }));
    builder.onLoad({ filter: /.*/, namespace: 'peer' }, () => ({ contents: 'export default window.React;', loader: 'js' }));
  } }]
});
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });
const page = await browser.newPage();
const errors = [];
page.on('pageerror', error => errors.push(error.message));
try {
  await page.setContent('<!doctype html><html lang="en"><head><title>Optimistic settlement boundary</title></head><body><main><div id="fixture"></div></main></body></html>');
  await page.addScriptTag({ content: await readFile(resolve(root, '_preview_runtime.js'), 'utf8') });
  await page.addScriptTag({ content: source.outputFiles[0].text });
  await page.evaluate(() => {
    const { OptimisticProvider, OptimisticHooks } = window.OptimisticUnderTest;
    let revision = 0;
    window.fixtureRoot = ReactDOM.createRoot(document.getElementById('fixture'));
    console.error = (...args) => { window.fixtureConsoleReports.push(args.map(String).join(' ')); };
    function Probe({ ready }) {
      window.fixtureLedger = OptimisticHooks.useOptimistic();
      React.useEffect(() => { window.fixtureReady = ready; }, [ready]);
      return null;
    }
    window.mountFixture = options => {
      window.fixtureEvents = [];
      window.fixtureReports = [];
      window.fixtureConsoleReports = [];
      window.reportError = options.consoleFallback ? undefined : error => window.fixtureReports.push(error.message);
      const ready = ++revision;
      window.fixtureRoot.render(React.createElement(OptimisticProvider, {
        key: ready,
        async commit(edit) {
          window.fixtureEvents.push({ kind: 'write', id: edit.id });
          if (options.rejectCommit) throw new Error('Write refused by transport');
          return { receipt: `receipt-${edit.id}` };
        },
        async revert(edits) {
          window.fixtureEvents.push({ kind: 'compensate', ids: edits.map(edit => edit.id) });
          if (options.rejectRevert) throw new Error('Compensation refused by transport');
        },
        onSettle(edit) {
          window.fixtureEvents.push({ kind: 'settle', id: edit.id, status: edit.status });
          if (edit.status === options.throwOnStatus) {
            const error = new Error(`Observer failed for ${edit.status}`);
            if (options.asyncObserver) return Promise.reject(error);
            throw error;
          }
        }
      }, React.createElement(Probe, { ready })));
      return ready;
    };
    window.captureFailure = async action => {
      try { await action(); return null; } catch (error) { return error.message; }
    };
  });
  async function mount(options) {
    const ready = await page.evaluate(options => window.mountFixture(options), options);
    await page.waitForFunction(ready => window.fixtureReady === ready, ready);
  }
  const edit = { rowId: 'synthetic-row', field: 'limit', prior: 10, value: 20 };

  await mount({ throwOnStatus: 'confirmed' });
  const confirmed = await page.evaluate(async edit => {
    const failure = await window.captureFailure(() => window.fixtureLedger.commit(edit));
    return { failure, entries: window.fixtureLedger.entries, events: window.fixtureEvents, reports: window.fixtureReports };
  }, edit);
  assert.equal(confirmed.entries[0].status, 'confirmed', 'Observer failure cannot reject a transport-confirmed write');
  assert.match(confirmed.entries[0].receipt, /^receipt-/);
  assert.equal(confirmed.failure, null, 'An observer failure cannot encourage retrying a completed write');
  assert.deepEqual(confirmed.reports, ['Observer failed for confirmed'], 'The callback fault is reported independently');
  assert.deepEqual(confirmed.events.filter(event => event.kind === 'settle').map(event => event.status), ['confirmed']);
  assert.equal(confirmed.events.filter(event => event.kind === 'write').length, 1);
  console.log('PASS Optimistic settlement: confirmed receipt survives a throwing observer');

  await mount({ rejectCommit: true, throwOnStatus: 'rejected' });
  const rejected = await page.evaluate(async edit => {
    const failure = await window.captureFailure(() => window.fixtureLedger.commit(edit));
    return { failure, entries: window.fixtureLedger.entries, events: window.fixtureEvents, reports: window.fixtureReports };
  }, edit);
  assert.equal(rejected.entries[0].status, 'rejected');
  assert.equal(rejected.entries[0].reason, 'Write refused by transport', 'The recorded reason comes from the transport');
  assert.equal(rejected.failure, null);
  assert.deepEqual(rejected.reports, ['Observer failed for rejected']);
  assert.deepEqual(rejected.events.filter(event => event.kind === 'settle').map(event => event.status), ['rejected']);
  console.log('PASS Optimistic settlement: rejected write retains its transport reason');

  await mount({ throwOnStatus: 'reverted' });
  const reverted = await page.evaluate(async edit => {
    await window.fixtureLedger.commit(edit);
    await window.fixtureLedger.commit({ ...edit, prior: 20, value: 30 });
    const failure = await window.captureFailure(() => window.fixtureLedger.revert(window.fixtureLedger.entries.map(entry => entry.id), 'Synthetic correction'));
    return { failure, entries: window.fixtureLedger.entries, events: window.fixtureEvents, reports: window.fixtureReports };
  }, edit);
  assert(reverted.entries.every(entry => entry.status === 'reverted'), 'Every target retains the completed compensation outcome');
  assert(reverted.entries.every(entry => entry.reason === 'Synthetic correction' && !entry.revertError));
  assert.equal(reverted.failure, null, 'Observer failures cannot encourage retrying a completed compensation');
  assert.deepEqual(reverted.reports, ['Observer failed for reverted', 'Observer failed for reverted']);
  assert.equal(reverted.events.filter(event => event.kind === 'settle' && event.status === 'reverted').length, 2, 'One observer failure does not skip the remaining target notifications');
  assert.equal(reverted.events.filter(event => event.kind === 'compensate').length, 1);
  console.log('PASS Optimistic settlement: batch compensation survives all observer failures');

  await mount({ rejectRevert: true });
  const revertRejected = await page.evaluate(async edit => {
    await window.fixtureLedger.commit(edit);
    const failure = await window.captureFailure(() => window.fixtureLedger.revert(window.fixtureLedger.entries[0].id, 'Synthetic correction'));
    return { failure, entries: window.fixtureLedger.entries, events: window.fixtureEvents };
  }, edit);
  assert.equal(revertRejected.failure, null);
  assert.equal(revertRejected.entries[0].status, 'confirmed');
  assert.equal(revertRejected.entries[0].revertError, 'Compensation refused by transport');
  assert.equal(revertRejected.events.filter(event => event.kind === 'settle' && event.status === 'reverted').length, 0);
  console.log('PASS Optimistic settlement: transport compensation failure remains distinct');

  await mount({ throwOnStatus: 'confirmed', consoleFallback: true });
  const fallback = await page.evaluate(async edit => {
    const failure = await window.captureFailure(() => window.fixtureLedger.commit(edit));
    return { failure, entries: window.fixtureLedger.entries, reports: window.fixtureConsoleReports };
  }, edit);
  assert.equal(fallback.failure, null);
  assert.equal(fallback.entries[0].status, 'confirmed');
  assert.equal(fallback.reports.length, 1);
  assert.match(fallback.reports[0], /onSettle observer failed.*Observer failed for confirmed/);
  console.log('PASS Optimistic settlement: console fallback reports callback faults');

  await mount({ throwOnStatus: 'confirmed', asyncObserver: true });
  const asynchronous = await page.evaluate(async edit => {
    const failure = await window.captureFailure(() => window.fixtureLedger.commit(edit));
    return { failure, entries: window.fixtureLedger.entries };
  }, edit);
  await page.waitForFunction(() => window.fixtureReports.length === 1);
  assert.equal(asynchronous.failure, null);
  assert.equal(asynchronous.entries[0].status, 'confirmed');
  assert.deepEqual(await page.evaluate(() => window.fixtureReports), ['Observer failed for confirmed']);
  console.log('PASS Optimistic settlement: rejected async observer is reported without rejecting the write');
  assert.deepEqual(errors, [], 'Deliberate callback faults are reported without unhandled runtime errors');
} finally {
  await browser.close();
}
