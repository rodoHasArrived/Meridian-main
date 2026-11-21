#!/usr/bin/env node
// Regressions for the local patches recorded in docs/LOCAL_PATCHES.md. Each case fails if its
// patch is reverted, so a fresh export that drops a fix cannot be accepted silently.
// Source-level: builds from components/ and templates/, never writes generated package files.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';
import { build } from 'esbuild';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const read = (path) => readFile(resolve(root, path), 'utf8');
const passed = [];
const pass = (id, what) => { passed.push(id); console.log(`PASS ${id}: ${what}`); };

// ── LP-07 · books.js — posting date must be valid and inside the selected open period ────────
{
  const sandbox = {};
  vm.runInNewContext(await read('templates/_scaffold/books.js'), sandbox);
  const scope = { entity: 'MER-FUND-I', basis: 'STAT', period: '2026-06' };
  const labels = (date) => Array.from(sandbox.MeridianBooks.stage({ date, lines: [] }, scope).issues, (issue) => issue.label);
  const dated = (date) => labels(date).filter((label) => label.startsWith('Posting date'));
  assert.deepEqual(dated('2026-06-10'), [], 'a date inside the open period raises no date issue');
  assert.equal(dated('2026-05-20').length, 1, 'a date in another period is refused');
  assert.match(dated('2026-05-20')[0], /^Posting date is outside /);
  assert.deepEqual(dated('2026-06-31'), ['Posting date is not a valid date']);
  assert.deepEqual(dated(''), ['Posting date is not a valid date']);
  pass('LP-07', 'books staging refuses invalid and out-of-period posting dates');
}

// ── LP-03 · SettingsAdmin — the save key identifies the whole settings revision ───────────────
{
  const source = await read('templates/settings-admin/SettingsAdmin.dc.html');
  const match = /idempotencyKey:\s*("CFG-"[^\n]*?),\s*payload\s*\}\)/.exec(source);
  assert.ok(match, 'the settings.save idempotency key expression is present');
  const keyFor = (payload) => vm.runInNewContext(match[1], { payload, JSON, Math });
  const base = { orgName: 'Meridian Capital Research', tz: 'UTC', rateLimit: 120, recipients: 2 };
  const sameLength = { ...base, orgName: 'Meridian Capital Resaerch' };
  assert.equal(JSON.stringify(base).length, JSON.stringify(sameLength).length, 'fixture keeps the serialized length');
  assert.equal(keyFor(base), keyFor({ ...base }), 'a retry of the same revision keeps its key');
  assert.notEqual(keyFor(base), keyFor(sameLength), 'an edit that preserves payload length gets a new key');
  assert.notEqual(keyFor(base), keyFor({ ...base, rateLimit: 121 }));
  pass('LP-03', 'settings save key changes with any payload change and is stable across retries');
}

// ── LP-09 · escaping in the eligibility card and the action-contract result table ─────────────
{
  const card = await read('docs/eligibility.card.html');
  const match = /const esc = (\(s\) => [^\n]+);/.exec(card);
  assert.ok(match, 'the eligibility card escape helper is present');
  const esc = vm.runInNewContext(match[1]);
  assert.equal(esc(`"><img src=x onerror='1'>&`), '&quot;&gt;&lt;img src=x onerror=&#39;1&#39;&gt;&amp;');
  assert.ok(!(await read('tests/action-contract.html')).includes('innerHTML'),
    'action-contract result rows are built without innerHTML');
  pass('LP-09', 'attribute-safe escaping; assertion and exception text never parsed as HTML');
}

// ── Browser cases ─────────────────────────────────────────────────────────────────────────────
const bundle = await build({
  absWorkingDir: root, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2020',
  stdin: {
    contents: `
      import { ActionEnvelopeProvider, ActionHooks } from './components/operations/ActionEnvelope.jsx';
      import { ContinuityFrame } from './components/core/ContinuityFrame.jsx';
      import { FormatProvider, useFormat } from './components/core/FormatProvider.jsx';
      import { parseLoose, toISO } from './components/core/localDate.js';
      window.Patched = { ActionEnvelopeProvider, ActionHooks, ContinuityFrame, FormatProvider, useFormat, parseLoose, toISO };
      window.MeridianDesignSystem_205d17 = { ActionHooks };`,
    resolveDir: root, loader: 'js',
  },
  jsxFactory: 'window.React.createElement', jsxFragment: 'window.React.Fragment',
  plugins: [{ name: 'react-peer', setup(builder) {
    builder.onResolve({ filter: /^react$/ }, () => ({ path: 'react', namespace: 'peer' }));
    builder.onLoad({ filter: /.*/, namespace: 'peer' }, () => ({ contents: 'export default window.React;', loader: 'js' }));
  } }],
});
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });
const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
const errors = [];
page.on('pageerror', (error) => errors.push(error.message));
try {
  await page.setContent('<!doctype html><html lang="en"><head><title>Local patch regressions</title></head><body><main><h1>Synthetic fixtures</h1><div id="fixture"></div></main></body></html>');
  await page.addScriptTag({ content: await read('_preview_runtime.js') });
  await page.addScriptTag({ content: bundle.outputFiles[0].text });
  await page.addScriptTag({ content: await read('templates/_scaffold/action-bridge.jsx') });
  await page.evaluate(() => {
    window.mount = (element) => {
      const host = document.createElement('div');
      document.getElementById('fixture').replaceChildren(host);
      const reactRoot = ReactDOM.createRoot(host);
      reactRoot.render(element);
      return { host, unmount: () => reactRoot.unmount() };
    };
    window.settle = (ms = 60) => new Promise((done) => setTimeout(done, ms));
  });

  // LP-06 · localDate — impossible typed dates are rejected, not normalized to another day
  const dates = await page.evaluate(() => {
    const { parseLoose, toISO } = window.Patched;
    const show = (raw) => { const value = parseLoose(raw); return value ? toISO(value) : null; };
    return Object.fromEntries(['2026-02-30', '2/30/2026', '13/01/2026', '30 Feb 2026', '2026-06-15', '8/26/2026', '26 Aug 2026'].map((raw) => [raw, show(raw)]));
  });
  assert.deepEqual(dates, {
    '2026-02-30': null, '2/30/2026': null, '13/01/2026': null, '30 Feb 2026': null,
    '2026-06-15': '2026-06-15', '8/26/2026': '2026-08-26', '26 Aug 2026': '2026-08-26',
  });
  pass('LP-06', 'impossible dates return null; valid ISO, slash and long dates still parse');

  // LP-05 · FormatProvider — an instrument tick overrides the surface default decimals
  const prices = await page.evaluate(async () => {
    const { FormatProvider, useFormat } = window.Patched;
    const out = {};
    function Probe() {
      const format = useFormat();
      out.tick = format.price(1.123456, { tick: 0.000001 });
      out.surface = format.price(1.123456);
      out.explicit = format.price(1.123456, { tick: 0.000001, decimals: 2 });
      return null;
    }
    const mounted = window.mount(React.createElement(FormatProvider, { priceDecimals: 4 }, React.createElement(Probe)));
    await window.settle();
    mounted.unmount();
    return out;
  });
  assert.match(prices.tick, /1\.123456$/, `tick precision is kept (got ${prices.tick})`);
  assert.match(prices.surface, /1\.1235$/, `surface decimals remain the fallback (got ${prices.surface})`);
  assert.match(prices.explicit, /1\.12$/, `a per-call decimals still wins (got ${prices.explicit})`);
  pass('LP-05', 'tick precision beats the surface default; per-call decimals beat both');

  // LP-04 · ContinuityFrame — a locked body is inert, the retry control stays usable
  const frame = await page.evaluate(async () => {
    const { ContinuityFrame } = window.Patched;
    const read = async (state) => {
      const mounted = window.mount(React.createElement(ContinuityFrame,
        { state, subject: 'Positions', onRetry() {}, retryLabel: 'Retry now' },
        React.createElement('button', { type: 'button', id: 'post' }, 'Post journal')));
      await window.settle();
      const post = mounted.host.querySelector('#post');
      const retry = [...mounted.host.querySelectorAll('button')].find((button) => button !== post);
      post.focus();
      const result = {
        bodyInert: Boolean(post.closest('[inert]')),
        bodyFocusable: document.activeElement === post,
        retryPresent: Boolean(retry),
        retryInert: retry ? Boolean(retry.closest('[inert]')) : null,
      };
      mounted.unmount();
      return result;
    };
    return { live: await read('live'), stale: await read('stale'), offline: await read('offline'), error: await read('error') };
  });
  assert.deepEqual({ inert: frame.live.bodyInert, focusable: frame.live.bodyFocusable }, { inert: false, focusable: true });
  for (const state of ['stale', 'offline', 'error']) {
    assert.equal(frame[state].bodyInert, true, `${state}: retained body is inert`);
    assert.equal(frame[state].bodyFocusable, false, `${state}: a retained control cannot take keyboard focus`);
  }
  assert.equal(frame.stale.retryPresent, true, 'stale: a retry control is rendered');
  assert.equal(frame.stale.retryInert, false, 'stale: the retry control sits outside the inert body');
  pass('LP-04', 'stale/offline/error bodies are inert to keyboard; retry stays reachable');

  // LP-02 · ActionEnvelope — synchronous repeat submits are one instruction; retry resends the
  // declaration that was actually sent (payload, key and endpoint overrides included)
  const envelope = await page.evaluate(async () => {
    const { ActionEnvelopeProvider, ActionHooks } = window.Patched;
    const calls = [];
    let answer = () => new Promise(() => {});
    const transport = (instruction) => { calls.push(instruction); return answer(instruction); };
    let api;
    function Grab() { api = ActionHooks.useAction(); return null; }
    const mounted = window.mount(React.createElement(ActionEnvelopeProvider,
      { actions: { 'order.place': { label: 'Place order' }, 'settings.save': { label: 'Save' } }, transport, timeoutMs: 150 },
      React.createElement(Grab)));
    await window.settle();

    answer = () => new Promise((done) => setTimeout(() => done({ ok: true, id: 'RCPT-1' }), 40));
    const triple = [1, 2, 3].map(() => api.submit('order.place', { idempotencyKey: 'ORD-1', payload: { qty: 5 } }));
    const tripleResults = await Promise.all(triple);
    const tripleCalls = calls.length;
    await window.settle();
    const tripleRecords = api.instructions.filter((item) => item.idempotencyKey === 'ORD-1').length;

    calls.length = 0;
    answer = () => new Promise(() => {});
    const first = await api.submit('settings.save', { idempotencyKey: 'CFG-9', endpoint: '/cfg/next', payload: { orgName: 'Meridian' } });
    await window.settle();
    answer = () => Promise.resolve({ ok: true, id: 'CFG-RCPT' });
    const second = await api.retry('settings.save');
    await window.settle();
    mounted.unmount();
    const pick = ({ idempotencyKey, endpoint, payload, attempt }) => ({ idempotencyKey, endpoint, payload, attempt });
    return {
      tripleCalls, tripleRecords, tripleIds: [...new Set(tripleResults.map((result) => result.id))].length,
      firstState: first.state, secondState: second && second.state, sent: calls.map(pick),
    };
  });
  assert.equal(envelope.tripleCalls, 1, 'three synchronous submits make one transport call');
  assert.equal(envelope.tripleRecords, 1, 'three synchronous submits create one instruction');
  assert.equal(envelope.tripleIds, 1, 'every caller is handed the same instruction');
  assert.equal(envelope.firstState, 'unlanded', 'a silent transport leaves the instruction unlanded');
  assert.equal(envelope.secondState, 'landed');
  assert.deepEqual(envelope.sent, [
    { idempotencyKey: 'CFG-9', endpoint: '/cfg/next', payload: { orgName: 'Meridian' }, attempt: 1 },
    { idempotencyKey: 'CFG-9', endpoint: '/cfg/next', payload: { orgName: 'Meridian' }, attempt: 2 },
  ], 'the retry carries the original key, endpoint and payload');
  pass('LP-02', 'idempotency key reserved synchronously; retry resends the effective declaration');

  // LP-01 · action-bridge — the parent is notified on instruction changes, not on every render
  const bridge = await page.evaluate(async () => {
    const { ActionEnvelopeProvider } = window.Patched;
    let notified = 0;
    let renders = 0;
    const proxies = new Set();
    // Mirrors the DC logic classes: every onApi forces a parent re-render that hands the provider
    // fresh action declarations and a fresh settle callback.
    function Parent() {
      const [, force] = React.useReducer((count) => count + 1, 0);
      renders += 1;
      const onApi = (api) => { notified += 1; proxies.add(api); if (notified < 500) force(); };
      return React.createElement(ActionEnvelopeProvider,
        { actions: { 'report.build': { label: 'Build' } }, transport: () => Promise.resolve({ ok: true, id: 'R-1' }), onSettled() {} },
        React.createElement(window.ActionBridge, { onApi }));
    }
    const mounted = window.mount(React.createElement(Parent));
    await window.settle(400);
    const idle = { notified, renders };
    const [api] = [...proxies];
    await api.submit('report.build', { idempotencyKey: 'RPT-1' });
    await window.settle(200);
    const after = { notified, renders, proxies: proxies.size, landed: api.instructions.map((item) => item.state) };
    mounted.unmount();
    return { idle, after };
  });
  assert.ok(bridge.idle.notified <= 2, `idle bridge notifies once, not in a loop (got ${bridge.idle.notified})`);
  assert.ok(bridge.idle.renders <= 4, `idle parent stops re-rendering (got ${bridge.idle.renders})`);
  assert.ok(bridge.after.notified > bridge.idle.notified, 'an instruction change still notifies the parent');
  assert.ok(bridge.after.notified <= bridge.idle.notified + 4, `one submit causes a bounded number of notifications (got ${bridge.after.notified})`);
  assert.equal(bridge.after.proxies, 1, 'the parent always receives the same stable API object');
  assert.deepEqual(bridge.after.landed, ['landed'], 'the stable API reads the latest instruction state');
  pass('LP-01', 'no parent-render feedback loop; one stable API that tracks instruction changes');

  assert.deepEqual(errors, [], 'No runtime errors');
} finally {
  await browser.close();
}
console.log(`Local patch regressions passed: ${passed.join(', ')}`);
