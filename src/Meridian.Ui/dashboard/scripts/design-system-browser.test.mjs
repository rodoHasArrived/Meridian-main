import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import { childFrameExclusion, clearSelectedEvidence, hasMeaningfulContent, launchBrowser, manualEvidenceStatus, packageCases, parseOptions, PINNED_ASSETS, prepareAssets, runCase, startStaticServer, staticTarget, verifyAsset, workstationCases } from './design-system-browser.mjs';

test('selection is explicit and malformed command lines fail', () => {
  assert.equal(parseOptions(['--scope', 'package', '--browser=msedge', '--workers', '3']).workers, 3);
  for (const args of [['--scope', 'unknown'], ['--browser', 'firefox'], ['--workers', '0'], ['--workers', '7'], ['--unknown'], ['--url']]) {
    assert.throws(() => parseOptions(args));
  }
});

test('workstation acceptance includes every root, supported width, theme and registered failure state', () => {
  const cases = workstationCases();
  assert.equal(new Set(cases.map((item) => item.id)).size, cases.length);
  for (const workspace of ['trading', 'portfolio', 'accounting', 'reporting', 'strategy', 'data', 'settings']) {
    for (const width of [1920, 1440, 1366, 1280]) for (const theme of ['light', 'dark']) {
      assert.ok(cases.some((item) => item.workspace === workspace && item.width === width && item.theme === theme));
    }
    for (const suffix of ['system-dark', 'forced-light', 'reduced-motion', 'forced-colors', 'print', 'density-terminal', 'density-compact', 'density-spacious']) {
      assert.ok(cases.some((item) => item.id === `workstation.${workspace}.${suffix}`));
    }
  }
  assert.deepEqual(cases.filter((item) => item.kind === 'scenario').map((item) => item.state), ['normal', 'empty', 'delayed', 'forbidden', 'failed']);
});

test('package inventory derives real cards and template entries instead of a hardcoded count', async (t) => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'meridian-package-cases-'));
  t.after(() => fs.rm(root, { recursive: true, force: true }));
  await fs.mkdir(path.join(root, 'templates/new'), { recursive: true });
  await fs.writeFile(path.join(root, 'new.card.html'), 'new demo');
  await fs.writeFile(path.join(root, 'templates/new/New.dc.html'), 'new template');
  await fs.writeFile(path.join(root, 'templates/new/support.js'), 'support');
  const cases = await packageCases(root);
  assert.equal(cases.filter((item) => item.kind === 'card').length, 1);
  assert.equal(cases.filter((item) => item.kind === 'template').length, 1);
  assert.ok(cases.some((item) => item.kind === 'exports'));
});

test('runtime cache verifies content instead of trusting a present file', async (t) => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'meridian-browser-runtime-'));
  t.after(() => fs.rm(root, { recursive: true, force: true }));
  assert.equal(verifyAsset(Buffer.from('test'), createHash('sha256').update('test').digest('hex'), 'fixture').toString(), 'test');
  assert.throws(() => verifyAsset(Buffer.from('tampered'), PINNED_ASSETS[0].sha256, 'fixture'), /digest mismatch/);
  await fs.writeFile(path.join(root, `${PINNED_ASSETS[0].sha256}.js`), 'tampered');
  await assert.rejects(prepareAssets(root, { offline: true }), /digest mismatch/);
  await fs.rm(path.join(root, `${PINNED_ASSETS[0].sha256}.js`));
  await assert.rejects(prepareAssets(root, { offline: true }), /not cached/);
});

test('static server serves real bytes and cannot expose parent files', async (t) => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'meridian-browser-static-'));
  t.after(() => fs.rm(root, { recursive: true, force: true }));
  await fs.writeFile(path.join(root, 'index.html'), '<main>Reference</main>');
  const server = await startStaticServer(root);
  t.after(server.close);
  assert.equal(await (await fetch(server.url)).text(), '<main>Reference</main>');
  assert.equal((await fetch(`${server.url}/missing.js`)).status, 404);
  assert.throws(() => staticTarget(root, '/%2e%2e%2fsecret'), /escapes/);
});

test('Linux automation or stale manual observations cannot satisfy Windows acceptance', () => {
  const sample = { sourceCommit: 'head', observedAt: '2026-11-12T00:00:00Z', observer: 'Operator', windowsVersion: 'Windows 11 24H2', browsers: ['chrome', 'msedge'].map((browser) => ({ browser, platform: 'win32', version: '143.0.0.0', nvdaVersion: '2026.1', keyboard: 'passed', zoom200: 'passed', nvda: 'passed' })) };
  assert.equal(manualEvidenceStatus(undefined, 'head').status, 'pending');
  assert.equal(manualEvidenceStatus(sample, 'different-head').status, 'invalid');
  assert.equal(manualEvidenceStatus({ ...sample, browsers: sample.browsers.map((browser) => ({ ...browser, platform: 'linux' })) }, 'head').status, 'invalid');
  assert.equal(manualEvidenceStatus({ ...sample, browsers: sample.browsers.map((browser) => ({ ...browser, version: 'current', nvdaVersion: 'pending' })) }, 'head').status, 'invalid');
  assert.equal(manualEvidenceStatus(sample, 'head').status, 'passed');
});

test('image-only brand references need a loaded accessible graphic, not arbitrary markup', () => {
  assert.equal(hasMeaningfulContent({ text: '', unfinished: false, labelledGraphic: true }), true);
  assert.equal(hasMeaningfulContent({ text: '', unfinished: false, labelledGraphic: false }), false);
  assert.equal(hasMeaningfulContent({ text: 'Loading…', unfinished: true, labelledGraphic: false }), false);
});

test('a new run cannot reuse stale success receipts or screenshots', async (t) => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'meridian-browser-evidence-'));
  t.after(() => fs.rm(root, { recursive: true, force: true }));
  await fs.mkdir(path.join(root, 'runtime-assets'));
  for (const file of ['results.json', 'card.first.json', 'card.first.png', 'unselected.json']) await fs.writeFile(path.join(root, file), 'old evidence');
  await fs.writeFile(path.join(root, 'runtime-assets/pinned.js'), 'cached runtime');
  await clearSelectedEvidence(root, [{ id: 'card.first' }]);
  assert.deepEqual((await fs.readdir(root)).sort(), ['runtime-assets', 'unselected.json']);
  assert.equal(await fs.readFile(path.join(root, 'runtime-assets/pinned.js'), 'utf8'), 'cached runtime');
  await assert.rejects(clearSelectedEvidence(root, [{ id: 'a/b' }, { id: 'a-b' }]), /collide/);
});

test('only gallery duplicates may omit child-frame axe because cards run independently', () => {
  assert.match(childFrameExclusion('tests/gallery.html'), /individual acceptance/);
  assert.equal(childFrameExclusion('guidelines/colors-states.card.html'), null);
  assert.equal(childFrameExclusion('guidelines/dark-mode-validation.card.html'), null);
  assert.equal(hasMeaningfulContent({ text: '', unfinished: false, readableFrame: true }), true);
});

test('loaded imported styles retain contrast auditing and real missing styles still fail', async (t) => {
  let browser;
  try { browser = await launchBrowser({ browser: 'chromium' }); }
  catch { t.skip('Install Chromium or set MERIDIAN_BROWSER_EXECUTABLE to run this rendered harness regression.'); return; }
  t.after(() => browser.close());
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'meridian-browser-audit-'));
  t.after(() => fs.rm(root, { recursive: true, force: true }));
  await fs.mkdir(path.join(root, 'preview'));
  await fs.mkdir(path.join(root, 'tokens'));
  await fs.writeFile(path.join(root, 'styles.css'), '@import "./tokens/colors.css";');
  await fs.writeFile(path.join(root, 'tokens/colors.css'), 'body{background:white;color:black}.low-contrast{color:#ddd}');
  const document = (link, content) => `<!doctype html><html lang="en"><head><title>Audit fixture</title><link rel="icon" href="data:,"><link rel="stylesheet" href="${link}"></head><body><main><h1>Audit fixture</h1>${content}</main></body></html>`;
  await fs.writeFile(path.join(root, 'preview/contrast.html'), document('../styles.css', '<p class="low-contrast">This imported color must fail contrast.</p>'));
  await fs.writeFile(path.join(root, 'preview/missing.html'), document('../missing.css', '<p>This document requests a missing stylesheet.</p>'));
  const server = await startStaticServer(root);
  t.after(server.close);
  const settings = { packageUrl: server.url, assets: new Map(), manifest: {}, outputDir: root };
  const contrast = await runCase(browser, { id: 'contrast-fixture', kind: 'reference', file: 'preview/contrast.html' }, settings);
  assert.equal(contrast.status, 'failed');
  assert.deepEqual(contrast.assetFailures, [], 'axe must not invent stylesheet requests relative to the preview URL');
  assert.ok(contrast.checks.violations.some((violation) => violation.id === 'color-contrast' && violation.nodes.some((node) => node.html.includes('class="low-contrast"'))), 'The loaded imported color must remain subject to WCAG contrast checks');
  const missing = await runCase(browser, { id: 'missing-css-fixture', kind: 'reference', file: 'preview/missing.html' }, settings);
  assert.equal(missing.status, 'failed');
  assert.ok(missing.assetFailures.some((failure) => failure.status === 404 && failure.url.endsWith('/missing.css')), 'A real missing stylesheet must fail acceptance');
  await fs.writeFile(path.join(root, 'preview/reset.html'), document('../reset.css', '<p>This document requests assets whose connections reset.</p><script src="../reset.js"></script>'));
  const resetBrowser = { newContext: async (options) => {
    const context = await browser.newContext(options);
    await context.route('**/reset.{css,js}', (route) => route.abort('connectionreset'));
    return context;
  } };
  const reset = await runCase(resetBrowser, { id: 'reset-assets-fixture', kind: 'reference', file: 'preview/reset.html' }, settings);
  assert.equal(reset.status, 'failed');
  for (const extension of ['css', 'js']) {
    assert.ok(reset.assetFailures.some((failure) => failure.url.endsWith(`/reset.${extension}`) && /ERR_CONNECTION_RESET/.test(failure.error)), `A ${extension} connection reset without an HTTP response must fail acceptance`);
  }
});
