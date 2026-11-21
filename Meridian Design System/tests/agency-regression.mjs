#!/usr/bin/env node
// Focused synthetic regression checks for the actual 4.3.0 financial/agency contracts.
// Run npm run build first. Uses exported runtime + namespace; no network/providers.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });
const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
const errors = [];
page.on('pageerror', error => errors.push(error.message));
try {
  await page.setContent('<!doctype html><html lang="en"><head><title>Meridian 4.3 agency contracts</title></head><body><main><h1>Financial and agency contracts</h1><div id="fixture-root"></div></main></body></html>');
  for (const filename of ['_preview_runtime.js', '_ds_bundle.js']) await page.addScriptTag({ content: await readFile(resolve(root, filename), 'utf8') });
  await page.evaluate(() => {
    const DS = window.MeridianDesignSystem_205d17;
    if (!DS?.OptimisticHooks || !DS?.AnnotationHooks || !DS?.OrderTicket) throw new Error('Actual 4.3 namespace is incomplete');
    window.contractEvents = [];
    window.contractModel = 10;
    window.contractRevision = 0;
    window.contractRoot = ReactDOM.createRoot(document.getElementById('fixture-root'));
    function LedgerProbe() {
      const ledger = DS.OptimisticHooks.useOptimistic();
      window.contractLedger = ledger;
      return React.createElement('output', { id: 'ledger-snapshot' }, JSON.stringify(ledger.entries));
    }
    function AnnotationProbe() {
      const api = DS.AnnotationHooks.useAnnotations();
      window.contractAnnotations = api;
      return React.createElement('output', { id: 'annotation-snapshot' }, JSON.stringify(api.notes));
    }
    function ContractFixture({ kind, props, revision }) {
      React.useLayoutEffect(() => { document.getElementById('fixture-root').dataset.revision = String(revision); }, [revision]);
      if (kind === 'OrderTicket') {
        const { hasSubmit, ...ticket } = props;
        return React.createElement(DS.OrderTicket, { ...ticket, ...(hasSubmit === false ? {} : { onSubmit(order) { window.contractEvents.push({ type: 'order', order }); } }) });
      }
      if (kind === 'Optimistic') return React.createElement(DS.OptimisticProvider, {
        key: props.reset,
        commit() { return window.deferCommit ? new Promise(resolve => { window.releaseCommit = resolve; }) : Promise.resolve(); },
        revert(edits, reason) { window.contractEvents.push({ type: 'batch', values: edits.map(edit => edit.value), reason }); return Promise.resolve(); },
        onSettle(edit) { window.contractEvents.push({ type: 'settle', edit }); if (edit.status === 'confirmed') window.contractModel = edit.value; if (edit.status === 'reverted') window.contractModel = edit.prior; }
      }, React.createElement(LedgerProbe));
      if (kind === 'Annotation') return React.createElement(DS.AnnotationProvider, { ...props }, React.createElement(AnnotationProbe));
      throw new Error(`Unknown fixture ${kind}`);
    }
    window.contractMount = (kind, props) => {
      const revision = ++window.contractRevision;
      window.contractRoot.render(React.createElement(ContractFixture, { kind, props, revision }));
      return revision;
    };
  });
  async function mount(kind, props) {
    const revision = await page.evaluate(({ kind, props }) => window.contractMount(kind, props), { kind, props });
    await page.waitForFunction(revision => document.getElementById('fixture-root').dataset.revision === String(revision), revision);
  }

  const order = { symbol: 'FIXTURE', lastPrice: 12.5, environment: 'live' };
  await mount('OrderTicket', order);
  const quantity = page.getByRole('textbox', { name: 'Quantity' });
  const submit = page.getByRole('button', { name: 'Submit buy order' });
  const confirm = page.getByRole('checkbox', { name: /Live environment/ });
  for (const value of ['10abc', '1e3', 'Infinity', '0', '-1']) {
    await quantity.fill(value);
    assert(await submit.isDisabled(), `Reject malformed or non-positive quantity ${value}`);
  }
  await quantity.fill('10.5');
  await confirm.check();
  assert.equal(await submit.isDisabled(), false, 'Valid confirmed draft enables submit');
  await quantity.fill('11');
  assert.equal(await confirm.isChecked(), false, 'Quantity edit revokes acknowledgement');
  await confirm.check();
  await mount('OrderTicket', { ...order, lastPrice: 13 });
  assert(await confirm.isChecked(), 'Market quote tick preserves acknowledgement');
  await mount('OrderTicket', { ...order, symbol: 'OTHER' });
  assert.equal(await confirm.isChecked(), false, 'Symbol change revokes acknowledgement');
  await confirm.check();
  await page.getByRole('button', { name: 'Sell', exact: true }).click();
  assert.equal(await confirm.isChecked(), false, 'Side change revokes acknowledgement');
  await page.getByRole('button', { name: 'Buy', exact: true }).click();
  await confirm.check();
  await page.getByRole('combobox', { name: /^Type / }).click();
  await page.getByRole('option', { name: 'Limit', exact: true }).click();
  assert.equal(await confirm.isChecked(), false, 'Order type change revokes acknowledgement');
  const limit = page.getByRole('textbox', { name: 'Limit price' });
  await limit.fill('12abc');
  await limit.blur();
  assert.equal(await limit.inputValue(), '12abc', 'Blur cannot launder a malformed price into a valid number');
  await confirm.check();
  assert(await submit.isDisabled(), 'Malformed limit remains blocked after acknowledgement');
  await limit.fill('12.25');
  assert.equal(await confirm.isChecked(), false, 'Limit edit revokes acknowledgement');
  await confirm.check();
  await page.getByRole('combobox', { name: /^Time in force / }).click();
  await page.getByRole('option', { name: 'GTC', exact: true }).click();
  assert.equal(await confirm.isChecked(), false, 'Time-in-force edit revokes acknowledgement');
  await confirm.check();
  await submit.click();
  assert.equal(await page.evaluate(() => window.contractEvents.filter(event => event.type === 'order').length), 1);
  assert.equal(await confirm.isChecked(), false, 'Submit revokes acknowledgement');
  assert.equal(await quantity.inputValue(), '', 'Actual 4.3 clear-after-submit behavior is preserved');
  await mount('OrderTicket', { ...order, environment: 'unrecognized' });
  await quantity.fill('2');
  assert(await submit.isDisabled(), 'Unrecognized environment fails closed');
  await mount('OrderTicket', { ...order, environment: 'paper', symbol: '' });
  assert(await submit.isDisabled(), 'Missing symbol fails closed');
  await mount('OrderTicket', { ...order, environment: 'paper', symbol: null });
  assert(await submit.isDisabled(), 'Malformed nullable symbol fails closed at runtime');
  await mount('OrderTicket', { ...order, environment: 'paper', hasSubmit: false });
  assert(await submit.isDisabled(), 'Absent handler cannot produce an inert enabled submit');
  await mount('OrderTicket', { ...order, prefill: { token: 'p1', qty: 3, type: 'limit', limitPrice: 12.75, tif: 'DAY' } });
  await confirm.check();
  await mount('OrderTicket', { ...order, prefill: { token: 'p2', qty: 4, type: 'limit', limitPrice: 12.75, tif: 'DAY' } });
  assert.equal(await confirm.isChecked(), false, 'Changed external prefill token revokes acknowledgement');
  await page.waitForFunction(() => document.querySelector('input[inputmode=decimal]').value === '4');
  await mount('OrderTicket', { ...order, guards: [{ id: 'fixture-block', state: 'blocking', label: 'Synthetic risk limit' }] });
  await confirm.check();
  assert(await submit.isDisabled(), 'Actual 4.3 blocking guard still gates submission');
  console.log('PASS OrderTicket: strict decimals, invalid blur, draft identity, prefill, guards, clearing');

  await mount('Optimistic', { reset: 'batch' });
  await page.evaluate(async () => {
    await window.contractLedger.commit({ rowId: 'r1', field: 'limit', prior: 10, value: 20 });
    await window.contractLedger.commit({ rowId: 'r1', field: 'limit', prior: 20, value: 30 });
    await window.contractLedger.revert(window.contractLedger.entries.map(edit => edit.id), 'Synthetic rollback');
  });
  assert.equal(await page.evaluate(() => window.contractModel), 10, 'Batch rollback restores original field value');
  assert.deepEqual(await page.evaluate(() => window.contractEvents.filter(event => event.type === 'batch').at(-1).values), [30, 20], 'Handler receives newest first');
  assert(await page.evaluate(() => window.contractLedger.entries.every(edit => edit.status === 'reverted')), 'Both receipts record reversion');
  await mount('Optimistic', { reset: 'dependency' });
  await page.evaluate(async () => {
    await window.contractLedger.commit({ rowId: 'r1', field: 'limit', prior: 10, value: 20 });
    await window.contractLedger.commit({ rowId: 'r1', field: 'limit', prior: 20, value: 30 });
    await window.contractLedger.revert(window.contractLedger.entries[0].id, 'Must not overwrite later write');
  });
  assert.equal(await page.evaluate(() => window.contractModel), 30, 'Older undo cannot overwrite unselected newer confirmed write');
  assert(await page.evaluate(() => window.contractLedger.entries.every(edit => edit.status === 'confirmed')));
  await page.evaluate(() => {
    window.deferCommit = true;
    window.contractLedger.commit({ rowId: 'r1', field: 'limit', prior: 30, value: 40 });
  });
  await page.evaluate(async () => { await window.contractLedger.revert(window.contractLedger.entries.filter(edit => edit.status === 'confirmed').map(edit => edit.id), 'Wait for pending write'); });
  assert.equal(await page.evaluate(() => window.contractModel), 30, 'Rollback cannot overwrite newer provisional write');
  assert.equal(await page.evaluate(() => window.contractLedger.entries.filter(edit => edit.status === 'reverted').length), 0);
  await page.evaluate(() => { window.deferCommit = false; window.releaseCommit(); });
  await page.waitForFunction(() => window.contractLedger.entries.every(edit => edit.status === 'confirmed'));
  console.log('PASS Optimistic ledger: reverse-order compensation and newer-write protection');

  const anchor = { kind: 'header', ref: 'balance', label: 'Balance' };
  const seeds = [
    { id: 'ann-1', objectId: 'object-A', anchor, text: 'Existing A', author: 'fixture', at: 1, version: 3, lifecycle: 'follow' },
    { id: 'ann-2', objectId: 'object-B', anchor, text: 'Existing B', author: 'fixture', at: 1, version: 1, lifecycle: 'follow' }
  ];
  await mount('Annotation', { objectId: 'object-A', objectVersion: 3, annotations: seeds });
  assert.deepEqual(await page.evaluate(() => window.contractAnnotations.notes.map(note => note.text)), ['Existing A']);
  const added = await page.evaluate(anchor => window.contractAnnotations.add({ anchor, text: 'New A' }), anchor);
  assert(!['ann-1', 'ann-2'].includes(added.id), 'New annotation ID cannot collide with persisted seed IDs');
  assert.equal(added.objectId, 'object-A');
  assert.equal(added.version, 3, 'Hook defaults to current object version');
  await mount('Annotation', { objectId: 'object-B', objectVersion: 1, annotations: seeds });
  assert.deepEqual(await page.evaluate(() => window.contractAnnotations.notes.map(note => note.text)), ['Existing B'], 'Switching objects scopes visible annotations');
  assert.equal(await page.evaluate(() => window.contractAnnotations.notes[0].stale), false, 'Object A version does not stale object B note');
  await mount('Annotation', { objectId: 'object-A', objectVersion: 4, annotations: [] });
  assert.deepEqual(await page.evaluate(() => window.contractAnnotations.notes.map(note => note.text)), ['Existing A', 'New A'], 'Returning restores unsaved per-object notes');
  assert(await page.evaluate(() => window.contractAnnotations.notes.every(note => note.stale)), 'Notes use their own object version');
  await page.evaluate(id => window.contractAnnotations.remove(id), added.id);
  assert.deepEqual(await page.evaluate(() => window.contractAnnotations.notes.map(note => note.id)), ['ann-1'], 'Removing new note preserves persisted note');
  console.log('PASS Annotations: persisted identity, object isolation, navigation retention, staleness');
  assert.deepEqual(errors, [], 'No browser runtime errors');
} finally { await browser.close(); }
