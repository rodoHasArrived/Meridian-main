#!/usr/bin/env node
// Bounded async DecisionFlow contracts. Deferred work is synthetic and local.
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const sourcePath = 'components/operations/DecisionFlow.jsx';
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const sourceHash = sha256(await readFile(resolve(root, sourcePath)));
const names = ['DecisionFlowProvider', 'DecisionFlow', 'DecisionFlowRail', 'DecisionFlowStage', 'DecisionConsequenceBar'];
const report = {
  schemaVersion: 1,
  source: { path: sourcePath, sha256: sourceHash, testSha256: sha256(await readFile(fileURLToPath(import.meta.url))), auditedAt: new Date().toISOString() },
  exports: Object.fromEntries(names.map(name => [name, { source: sourcePath, sha256: sourceHash, cases: [] }])),
  pageErrors: [], consoleErrors: [], externalRequests: [],
};
const built = await build({
  absWorkingDir: root, bundle: true, write: false, format: 'iife', platform: 'browser',
  resolveExtensions: ['.js', '.jsx', '.ts', '.tsx', '.json'],
  define: { 'process.env.NODE_ENV': '"production"' },
  stdin: { resolveDir: root, loader: 'jsx', contents: `
    import React from 'react'; import { createRoot } from 'react-dom/client';
    import * as Flow from './components/operations/DecisionFlow.jsx';
    window.Fixture={React,createRoot,...Flow};
  ` },
});
const browser = await chromium.launch({ executablePath: process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH || '/usr/bin/chromium', args: ['--no-sandbox'] });
const context = await browser.newContext({ viewport: { width: 1366, height: 768 }, locale: 'en-US', timezoneId: 'UTC', reducedMotion: 'reduce' });
const page = await context.newPage();
page.setDefaultTimeout(4000);
page.on('pageerror', error => report.pageErrors.push(error.message));
page.on('console', message => { if (message.type() === 'error') report.consoleErrors.push(message.text()); });
await page.route('**/*', route => { report.externalRequests.push(route.request().url()); return route.abort(); });
await page.clock.setFixedTime(new Date('2026-09-10T14:32:08Z'));
try {
  await page.setContent('<!doctype html><html lang="en"><head><title>DecisionFlow asynchronous source contracts</title></head><body><main><h1>Synthetic DecisionFlow contracts</h1><div id="fixture"></div></main></body></html>');
  await page.addScriptTag({ content: built.outputFiles[0].text });
  await page.evaluate(() => {
    const { React, createRoot, ...DS } = window.Fixture;
    const host = document.getElementById('fixture');
    let reactRoot = createRoot(host), revision = 0;
    window.behavior = {};
    window.validationRequests = []; window.entryRequests = []; window.events = []; window.results = {};
    const deferred = (kind, stepId) => {
      let resolve, reject;
      const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
      const request = { stepId, object: { ...window.behavior.object }, resolve, reject };
      window[kind].push(request);
      return promise;
    };
    const onValidate = stepId => window.behavior.validation === 'deferred' ? deferred('validationRequests', stepId) : [];
    const onEnter = () => {
      if (window.behavior.entry === 'deferred') return deferred('entryRequests', 'review');
      if (window.behavior.entry === 'rejected') return Promise.reject(new Error('Synthetic entry failure REVIEW-ERR'));
    };
    const onTransition = transition => window.events.push({ name: 'transition', transition });
    const policy = { id: 'policy:exact', state: 'satisfied', label: 'Scope verified' };
    function Probe() {
      const flow = DS.useDecisionFlow();
      window.flow = flow;
      window.aliasMatches = DS.DecisionFlow.use() === flow && DS.DecisionFlow.useDecisionFlow() === flow;
      return null;
    }
    function Fixture({ config, revision }) {
      React.useLayoutEffect(() => { host.dataset.revision = String(revision); }, [revision]);
      const steps = React.useMemo(() => [
        { id: 'prepare', label: 'Prepare' },
        { id: 'review', label: 'Review', requires: config.independent ? [] : ['prepare'], onEnter, guards: config.policy ? [policy] : [] },
      ], [config.independent, config.policy]);
      const Provider = config.compound ? DS.DecisionFlow.Provider : DS.DecisionFlowProvider;
      const Rail = config.compound ? DS.DecisionFlow.Rail : DS.DecisionFlowRail;
      const Stage = config.compound ? DS.DecisionFlow.Stage : DS.DecisionFlowStage;
      const Bar = config.compound ? DS.DecisionFlow.ConsequenceBar : DS.DecisionConsequenceBar;
      return React.createElement(Provider, {
        object: config.object, snapshotId: 'SNAPSHOT:' + config.object.id + ':v' + config.object.version,
        initialStep: 'prepare', actorId: 'operator:async', steps,
        onValidate: config.validation === 'none' ? undefined : onValidate, onTransition,
      }, React.createElement(Probe), React.createElement(Rail, { label: 'Synthetic decision progression' }),
      React.createElement(Stage, null, React.createElement('p', null, 'Retained synthetic scope ' + config.object.id)),
      React.createElement(Bar, { primaryLabel: 'Commit synthetic decision', onPrimary: detail => window.events.push({ name: 'primary', detail }) }));
    }
    window.mount = (config, reset = true) => {
      if (!reactRoot) reactRoot = createRoot(host);
      window.behavior = { validation: 'success', entry: 'success', object: { type: 'journal', id: 'OBJECT:A', version: 1 }, ...config };
      const next = ++revision;
      reactRoot.render(React.createElement(Fixture, { key: reset ? config.caseId : window.caseId, config: window.behavior, revision: next }));
      if (reset) window.caseId = config.caseId;
      return next;
    };
    window.newCase = config => {
      window.validationRequests = []; window.entryRequests = []; window.events = []; window.results = {};
      return window.mount(config, true);
    };
    window.unmount = () => { reactRoot.unmount(); reactRoot = null; };
    window.start = (key, method, ...args) => {
      window.results[key] = { state: 'pending' };
      Promise.resolve().then(() => window.flow[method](...args)).then(
        value => { window.results[key] = { state: 'resolved', value }; },
        error => { window.results[key] = { state: 'rejected', message: String(error.message || error) }; },
      );
    };
    window.resolveValidation = (index, guards) => window.validationRequests[index].resolve(guards);
    window.resolveEntry = index => window.entryRequests[index].resolve();
  });
  async function mount(config, fresh = true) {
    const revision = await page.evaluate(({ config, fresh }) => fresh ? window.newCase(config) : window.mount(config, false), { config, fresh });
    await page.waitForFunction(revision => document.getElementById('fixture').dataset.revision === String(revision), revision);
    assert.equal(await page.evaluate(() => window.aliasMatches), true, 'Both public namespace hooks return the same scoped provider value');
  }
  async function settled(key) {
    await page.waitForFunction(key => window.results[key]?.state !== 'pending', key);
    return page.evaluate(key => window.results[key], key);
  }
  const primary = () => page.getByRole('button', { name: 'Commit synthetic decision', exact: true });
  async function check(name, states, run) {
    let status = 'passed', detail;
    try { await run(); } catch (error) { status = 'failed'; detail = error.message; }
    for (const exportName of names) report.exports[exportName].cases.push({ name, states, status, detail, kind: 'state' });
    console.log(`${status}: ${name}`);
  }

  await check('pending validation blocks the actual primary action and recovers with exact scope', ['async-validation', 'pending', 'loading', 'disabled', 'keyboard', 'recovery', 'callback', 'scope'], async () => {
    await mount({ caseId: 'pending', validation: 'deferred' });
    await page.waitForFunction(() => window.validationRequests.length === 1);
    assert.equal(await page.evaluate(() => window.aliasMatches), true);
    assert.equal(await page.evaluate(() => window.flow.isBlocked), true);
    assert.equal(await page.evaluate(() => window.flow.summary.blocked), false, 'Pending input is not a policy blocking guard');
    assert.match(await page.locator('.mds-dfbar').innerText(), /Validation in progress/i);
    assert.equal(await primary().getAttribute('aria-disabled'), 'true');
    await primary().focus(); await primary().press('Enter');
    assert.deepEqual(await page.evaluate(() => window.events), []);
    assert.equal(await page.locator('.mds-dfstep[data-current="true"] .mds-dfstep__lbl').textContent(), 'Prepare');
    await page.getByRole('region', { name: 'Prepare — decision step 1 of 2', exact: true }).waitFor();
    await page.evaluate(() => window.resolveValidation(0, []));
    await page.waitForFunction(() => !window.flow.isBlocked);
    assert.equal(await primary().getAttribute('aria-disabled'), null);
    await primary().press('Space');
    assert.deepEqual(await page.evaluate(() => window.events), [{ name: 'primary', detail: {
      object: { type: 'journal', id: 'OBJECT:A', version: 1 }, snapshotId: 'SNAPSHOT:OBJECT:A:v1', stepId: 'prepare', guards: [],
    } }]);
  });

  await check('newer same-step blocking validation defeats old automatic and complete responses', ['async-validation', 'stale', 'blocked', 'complete', 'recovery', 'compound-forwarding'], async () => {
    await mount({ caseId: 'same-step', compound: true, validation: 'deferred' });
    await page.waitForFunction(() => window.validationRequests.length === 1);
    await page.evaluate(() => window.start('old-complete', 'complete', 'prepare'));
    await page.waitForFunction(() => window.validationRequests.length === 2);
    await page.evaluate(() => window.start('new-validation', 'validate', 'prepare'));
    await page.waitForFunction(() => window.validationRequests.length === 3);
    const guard = { id: 'latest:policy', state: 'blocking', label: 'Latest policy hold', reason: 'Newer exact result' };
    await page.evaluate(guard => window.resolveValidation(2, [guard]), guard);
    assert.deepEqual(await settled('new-validation'), { state: 'resolved', value: [guard] });
    await page.waitForFunction(() => window.flow.summary.blocked);
    await page.evaluate(() => window.resolveValidation(1, []));
    assert.deepEqual(await settled('old-complete'), { state: 'resolved', value: false });
    await page.evaluate(() => window.resolveValidation(0, []));
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    assert.deepEqual(await page.evaluate(() => window.flow.stepGuards('prepare')), [guard]);
    assert.deepEqual(await page.evaluate(() => window.flow.completed), {});
    assert.equal(await page.evaluate(() => window.flow.stepId), 'prepare');
    assert.deepEqual(await page.evaluate(() => window.flow.transitions), []);
    assert.deepEqual(await page.evaluate(() => window.events), []);
    assert.equal(await primary().getAttribute('aria-disabled'), 'true');
    assert.match(await page.locator('.mds-dfbar').innerText(), /Latest policy hold/);
    assert.equal(await page.locator('.mds-dfstep[data-current="true"]').getAttribute('data-state'), 'blocked');
    assert.equal(await page.locator('.mds-dfstep[data-current="true"] .mds-dfstep__lbl').textContent(), 'Prepare');
    await page.getByRole('region', { name: 'Prepare — decision step 1 of 2', exact: true }).waitFor();
    await page.evaluate(() => window.start('recovery-validation', 'validate', 'prepare'));
    await page.waitForFunction(() => window.validationRequests.length === 4);
    await page.evaluate(() => window.resolveValidation(3, []));
    assert.deepEqual(await settled('recovery-validation'), { state: 'resolved', value: [] });
    await page.waitForFunction(() => !window.flow.isBlocked);
    assert.equal(await primary().getAttribute('aria-disabled'), null);
  });

  await check('completion awaiting next-step entry cannot land after identity version or unmount invalidation', ['async-entry', 'identity-change', 'version-change', 'scope-invalidation', 'unmount', 'stale', 'callback'], async () => {
    for (const invalidation of ['identity', 'version', 'unmount']) {
      const config = { caseId: 'entry-' + invalidation, entry: 'deferred' };
      await mount(config);
      await page.waitForFunction(() => !window.flow.isBlocked);
      await page.evaluate(() => window.start('complete', 'complete', 'prepare'));
      await page.waitForFunction(() => window.entryRequests.length === 1);
      assert.deepEqual(await page.evaluate(() => window.results.complete), { state: 'pending' });
      assert.deepEqual(await page.evaluate(() => window.events), []);
      if (invalidation === 'unmount') {
        await page.evaluate(() => window.unmount());
        assert.equal(await page.locator('#fixture > *').count(), 0);
      } else {
        const object = { type: 'journal', id: invalidation === 'identity' ? 'OBJECT:B' : 'OBJECT:A', version: invalidation === 'version' ? 2 : 1 };
        await mount({ ...config, object }, false);
        await page.waitForFunction(object => window.flow.object.id === object.id && window.flow.object.version === object.version && Object.keys(window.flow.completed).length === 0, object);
        assert.notEqual(await page.evaluate(() => window.flow.versionChanged), null);
        assert.match(await page.locator('.mds-dfrail__hd').textContent(), new RegExp(object.id + ' v' + object.version));
        assert.equal(await page.locator('.mds-dfstep[data-state="done"]').count(), 0);
        await page.getByText('The object changed underneath this review', { exact: true }).waitFor();
        assert.match(await page.locator('.mds-dfbar__ob').innerText(), new RegExp(object.id + ' · version ' + object.version));
      }
      await page.evaluate(() => window.resolveEntry(0));
      assert.deepEqual(await settled('complete'), { state: 'resolved', value: false }, invalidation);
      assert.deepEqual(await page.evaluate(() => window.events), [], 'Invalidated work cannot call onTransition');
      if (invalidation !== 'unmount') assert.deepEqual(await page.evaluate(() => window.flow.transitions), []);
    }
  });

  await check('rejected step entry reports blocking evidence and existing goTo retries recover exactly', ['async-entry', 'error', 'blocked', 'disabled', 'recovery', 'callback', 'compound-forwarding'], async () => {
    await mount({ caseId: 'entry-retry', compound: true, validation: 'none', entry: 'rejected', independent: true, policy: true });
    await page.evaluate(() => window.start('failed-entry', 'goTo', 'review', { reason: 'Inspect exact failure' }));
    assert.deepEqual(await settled('failed-entry'), { state: 'resolved', value: false });
    await page.waitForFunction(() => window.flow.summary.blocked);
    assert.match(await page.locator('.mds-dfbar').innerText(), /Step entry unavailable/);
    assert(await page.evaluate(() => window.flow.stepGuards('review').some(guard => /REVIEW-ERR/.test(guard.reason || ''))));
    assert.equal(await primary().getAttribute('aria-disabled'), 'true');
    assert.equal(await page.locator('.mds-dfstep[data-current="true"]').getAttribute('data-state'), 'blocked');
    assert.deepEqual(await page.evaluate(() => window.events), []);
    assert.deepEqual(await page.evaluate(() => window.flow.transitions), []);
    await page.evaluate(() => { window.behavior.entry = 'success'; window.start('retry-entry', 'goTo', 'review', { reason: 'Retry exact entry' }); });
    assert.deepEqual(await settled('retry-entry'), { state: 'resolved', value: true });
    await page.waitForFunction(() => !window.flow.isBlocked);
    assert.deepEqual(await page.evaluate(() => window.flow.stepGuards('review')), [{ id: 'policy:exact', state: 'satisfied', label: 'Scope verified' }]);
    assert.equal(await primary().getAttribute('aria-disabled'), null);
    assert.equal(await page.locator('.mds-dfstep[data-current="true"]').getAttribute('data-state'), 'open');
    await page.getByRole('region', { name: 'Review — decision step 2 of 2', exact: true }).waitFor();
    const transitions = await page.evaluate(() => window.events.filter(event => event.name === 'transition'));
    assert.deepEqual(transitions, [{ name: 'transition', transition: { from: 'review', to: 'review', at: '2026-09-10T14:32:08.000Z', actorId: 'operator:async', reason: 'Retry exact entry' } }]);
    await primary().click();
    const detail = await page.evaluate(() => window.events.find(event => event.name === 'primary')?.detail);
    assert.deepEqual(detail, { object: { type: 'journal', id: 'OBJECT:A', version: 1 }, snapshotId: 'SNAPSHOT:OBJECT:A:v1', stepId: 'review', guards: [{ id: 'policy:exact', state: 'satisfied', label: 'Scope verified' }] });
  });
} finally {
  await context.close(); await browser.close();
  report.source.currentSha256 = sha256(await readFile(resolve(root, sourcePath)));
  report.source.stable = report.source.currentSha256 === sourceHash;
  const output = resolve(process.env.MERIDIAN_DECISIONFLOW_ASYNC_REPORT || '/tmp/pr3501-full-audit/domain-flow-async-results.json');
  await mkdir(dirname(output), { recursive: true }); await writeFile(output, JSON.stringify(report, null, 2) + '\n');
}
const cases = Object.values(report.exports).flatMap(record => record.cases);
const failed = cases.filter(record => record.status === 'failed').length;
console.log(JSON.stringify({ groups: cases.length / names.length, exports: names.length, passed: cases.length - failed, failed, pageErrors: report.pageErrors.length, consoleErrors: report.consoleErrors.length, externalRequests: report.externalRequests.length, sourceStable: report.source.stable }));
if (failed || report.pageErrors.length || report.consoleErrors.length || report.externalRequests.length || !report.source.stable) process.exitCode = 1;
