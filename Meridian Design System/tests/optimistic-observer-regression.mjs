#!/usr/bin/env node
// Synthetic observer-fault regression against the production component source.
// Compiles in memory, so it can run without mutating the package export.
import assert from 'node:assert/strict';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const { outputFiles } = await build({
  absWorkingDir: root,
  stdin: {
    contents: `import React from 'react'; import { createRoot } from 'react-dom/client';
      import { OptimisticProvider, useOptimistic } from './components/data/OptimisticCell.jsx';
      window.fixture = { React, createRoot, OptimisticProvider, useOptimistic };`,
    resolveDir: root, loader: 'jsx'
  },
  bundle: true, write: false, format: 'iife', platform: 'browser',
  define: { 'process.env.NODE_ENV': '"production"' }, logLevel: 'error'
});
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });
const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
const pageErrors = [];
const diagnostics = [];
page.on('pageerror', error => pageErrors.push(error.message));
page.on('console', message => { if (message.type() === 'error') diagnostics.push(message.text()); });
try {
  await page.setContent('<!doctype html><html lang="en"><head><title>Optimistic observer contracts</title></head><body><main><h1>Observer fault isolation</h1><div id="fixture-root"></div></main></body></html>');
  await page.addScriptTag({ content: outputFiles[0].text });
  // Exercise console fallback; the settlement regression covers reportError and async faults.
  await page.evaluate(() => { window.reportError = undefined; });
  await page.evaluate(() => {
    const { React, createRoot, OptimisticProvider, useOptimistic } = window.fixture;
    window.fixtureRoot = createRoot(document.getElementById('fixture-root'));
    window.fixtureRevision = 0;
    function Probe() {
      const ledger = useOptimistic();
      window.ledger = ledger;
      return React.createElement('output', { id: 'ledger-state' }, JSON.stringify(ledger.entries));
    }
    function Fixture({ config, revision }) {
      React.useEffect(() => { document.getElementById('fixture-root').dataset.revision = String(revision); }, [revision]);
      return React.createElement(OptimisticProvider, {
        commit(edit) {
          window.transportEvents.push({ type: 'commit', value: edit.value });
          if (config.rejectCommit) return Promise.reject(new Error('transport denied'));
          return Promise.resolve({ receipt: 'synthetic-receipt' });
        },
        revert(edits, reason) {
          window.transportEvents.push({ type: 'revert', values: edits.map(edit => edit.value), reason });
          if (config.rejectRevert) return Promise.reject(new Error('compensation denied'));
          return Promise.resolve();
        },
        onSettle(edit) {
          window.settlements.push({ ...edit });
          if (config.throwOnSettle) throw new Error('consumer projection failed');
        },
        onChange(entries) {
          window.changeEvents.push(entries.map(edit => edit.status));
          if (config.throwOnChange) throw new Error('consumer change observer failed');
        }
      }, React.createElement(Probe));
    }
    window.mountFixture = config => {
      window.transportEvents = [];
      window.settlements = [];
      window.changeEvents = [];
      const revision = ++window.fixtureRevision;
      window.fixtureRoot.render(React.createElement(Fixture, { key: revision, config, revision }));
      return revision;
    };
  });
  async function mount(config = {}) {
    const revision = await page.evaluate(config => window.mountFixture(config), config);
    await page.waitForFunction(revision => document.getElementById('fixture-root').dataset.revision === String(revision), revision);
  }
  const edit = { rowId: 'synthetic-row', field: 'limit', prior: 10, value: 20 };

  await mount({ throwOnSettle: true, throwOnChange: true });
  const confirmed = await page.evaluate(async edit => {
    window.subscriberEvents = [];
    window.ledger.subscribe(() => { throw new Error('subscriber failed'); });
    window.ledger.subscribe(entries => window.subscriberEvents.push(entries.map(entry => entry.status)));
    await window.ledger.commit(edit);
    return { entries: window.ledger.entries, transport: window.transportEvents, settlements: window.settlements, subscribers: window.subscriberEvents };
  }, edit);
  assert.equal(confirmed.transport.length, 1, 'A pending-state observer failure cannot prevent the write');
  assert.equal(confirmed.entries[0].status, 'confirmed', 'A settlement callback cannot reject a confirmed write');
  assert.equal(confirmed.entries[0].receipt, 'synthetic-receipt');
  assert.deepEqual(confirmed.settlements.map(entry => entry.status), ['confirmed'], 'Only one actual outcome is reported');
  assert.deepEqual(confirmed.subscribers, [['pending'], ['confirmed']], 'A failed subscriber cannot starve later subscribers');
  await page.waitForFunction(() => JSON.parse(document.getElementById('ledger-state').textContent)[0]?.status === 'confirmed');

  await mount({ rejectCommit: true, throwOnSettle: true });
  const rejected = await page.evaluate(async edit => {
    await window.ledger.commit(edit);
    return { entries: window.ledger.entries, settlements: window.settlements };
  }, edit);
  assert.equal(rejected.entries[0].status, 'rejected');
  assert.equal(rejected.entries[0].reason, 'transport denied', 'The transport rejection remains the displayed reason');
  assert.deepEqual(rejected.settlements.map(entry => entry.status), ['rejected']);

  await mount({ throwOnSettle: true, throwOnChange: true });
  const reverted = await page.evaluate(async edit => {
    await window.ledger.commit(edit);
    await window.ledger.commit({ ...edit, prior: 20, value: 30 });
    await window.ledger.revert(window.ledger.entries.map(entry => entry.id), 'Synthetic correction');
    return { entries: window.ledger.entries, settlements: window.settlements, transport: window.transportEvents };
  }, edit);
  assert.deepEqual(reverted.entries.map(entry => entry.status), ['reverted', 'reverted'], 'Every successful batch compensation remains reverted');
  assert.deepEqual(reverted.settlements.map(entry => [entry.status, entry.value]), [['confirmed', 20], ['confirmed', 30], ['reverted', 30], ['reverted', 20]], 'Callback faults cannot stop newest-first batch notifications');
  assert.deepEqual(reverted.transport[2], { type: 'revert', values: [30, 20], reason: 'Synthetic correction' });
  assert(reverted.entries.every(entry => entry.reason === 'Synthetic correction' && entry.revertedAt && !entry.revertError));

  await mount({ rejectRevert: true, throwOnSettle: true });
  const failedRevert = await page.evaluate(async edit => {
    await window.ledger.commit(edit);
    await window.ledger.revert(window.ledger.entries[0].id, 'Synthetic correction');
    return { entries: window.ledger.entries, settlements: window.settlements };
  }, edit);
  assert.equal(failedRevert.entries[0].status, 'confirmed', 'A rejected compensating write preserves its confirmed edit');
  assert.equal(failedRevert.entries[0].revertError, 'compensation denied');
  assert.deepEqual(failedRevert.settlements.map(entry => entry.status), ['confirmed'], 'A failed compensation never announces a revert');
  assert.deepEqual(pageErrors, [], 'Observer faults do not escape as page exceptions or unhandled promise rejections');
  assert.equal(diagnostics.length, 19, 'Every injected synchronous observer fault is reported once');
  assert(diagnostics.every(message => /^OptimisticProvider: (ledger|onSettle) observer failed/.test(message)), 'Only expected observer diagnostics were emitted');
  console.log('Optimistic observer regression passed: confirmed/rejected writes, newest-first batch compensation, failed revert, subscriber delivery, explicit diagnostics.');
} finally {
  await browser.close();
}
