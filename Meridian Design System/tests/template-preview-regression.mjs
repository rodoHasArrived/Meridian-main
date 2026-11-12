#!/usr/bin/env node
// Exercise retained template preview contracts against the actual package runtime.
// Templates are transformed in memory; no generated package files are written.
import assert from 'node:assert/strict';
import { readFile, readdir } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { transform } from 'esbuild';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const templates = [
  ['amx-governance', 'AmxGovernanceScreen'],
  ['backtest-builder', 'BacktestBuilderScreen'],
  ['covered-call-lab', 'CoveredCallLabScreen'],
  ['family-office', 'FamilyOfficeScreen'],
  ['journaling-workstation', 'JournalingWorkstationScreen'],
  ['reconciliation-workstation', 'ReconciliationWorkstationScreen'],
  ['split-pane-reconciliation', 'SplitPaneReconciliationScreen'],
  ['strategy-builder', 'StrategyBuilderScreen'],
  ['strategy-onboarding', 'StrategyOnboardingScreen'],
];
const [runtime, bundle, prefs] = await Promise.all(['_preview_runtime.js', '_ds_bundle.js', 'templates/_scaffold/prefs.js'].map(name => readFile(resolve(root, name), 'utf8')));
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });
try {
  for (const [slug, component] of templates) {
    const dir = resolve(root, 'templates', slug);
    const entry = (await readdir(dir)).find(name => name.endsWith('.dc.html'));
    const html = await readFile(resolve(dir, entry), 'utf8');
    const schema = JSON.parse(html.match(/data-props="([^"]+)"/)[1].replaceAll('&quot;', '"'));
    assert(schema.$preview.width >= 1280 && schema.$preview.height >= 720, `${slug}: desktop thumbnail dimensions`);
    assert.deepEqual(schema.density.options, ['default', 'compact', 'terminal', 'spacious']);
    assert.deepEqual(schema.theme.options, ['light', 'dark']);
    assert.deepEqual(schema.environment.options, ['FIXTURE', 'PAPER', 'LIVE']);
    assert.deepEqual(schema.dataState.options, ['ready', 'loading', 'empty', 'error']);
    assert(html.includes('environment="{{ environment }}"') && html.includes('state="{{ dataState }}"'), `${slug}: entry forwards the effective environment and data state`);
    const source = await readFile(resolve(dir, 'screen.jsx'), 'utf8');
    assert(!source.includes('MeridianDesignSystem_4f61be'), `${slug}: current public namespace`);
    const [screen, logic] = await Promise.all([
      transform(source, { loader: 'jsx', format: 'iife', target: 'es2020' }),
      transform(html.match(/<script[^>]*data-dc-script[^>]*>([\s\S]*?)<\/script>/)[1] + '\nwindow.TemplateLogic = Component;', { loader: 'jsx', format: 'iife', target: 'es2020' }),
    ]);
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.route('http://meridian.test/**', route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><html lang="en"><head><title>Template preview regression</title></head><body><div id="fixture" style="height:860px;display:flex;flex-direction:column"></div></body></html>' }));
    await page.goto(`http://meridian.test/${slug}`);
    for (const code of [runtime, bundle, prefs, 'window.DCLogic = class { constructor(props) { this.props = props; } };', logic.code, screen.code]) await page.addScriptTag({ content: code });
    await page.evaluate(component => {
      const fixture = document.getElementById('fixture');
      const root = ReactDOM.createRoot(fixture);
      const logic = new window.TemplateLogic({});
      window.previewRevision = 0;
      function Fixture({ values, revision }) {
        React.useLayoutEffect(() => { fixture.dataset.revision = String(revision); }, [revision]);
        return React.createElement(window[component], { environment: values.environment, state: values.dataState });
      }
      window.renderPreview = props => {
        logic.props = props;
        const values = logic.renderVals();
        const revision = ++window.previewRevision;
        root.render(React.createElement(Fixture, { values, revision }));
        return revision;
      };
    }, component);
    async function render(props) {
      const revision = await page.evaluate(props => window.renderPreview(props), props);
      await page.waitForFunction(revision => document.getElementById('fixture').dataset.revision === String(revision), revision, { timeout: 5000 }).catch(error => { throw new Error(`${slug}: ${error.message}; runtime errors ${JSON.stringify(errors)}`); });
    }
    const defaults = Object.fromEntries(Object.entries(schema).filter(([key]) => key !== '$preview').map(([key, spec]) => [key, spec.default]));
    await render(defaults);
    assert.equal(await page.getByRole('main').count(), 1, `${slug}: one main landmark`);
    assert.equal(await page.getByRole('heading', { level: 1 }).count(), 1, `${slug}: one page title`);
    assert.equal(await page.locator('main .mds-dss').count(), 0, `${slug}: ready state renders the work plane`);
    assert((await page.locator('main').innerText()).length > 80, `${slug}: meaningful ready content`);
    if (slug === 'journaling-workstation') {
      await page.getByRole('button', { name: 'New entry', exact: true }).click();
      await page.getByRole('dialog', { name: 'New journal entry', exact: true }).waitFor();
      await page.getByRole('button', { name: 'Close dialog', exact: true }).click();
      assert.equal(await page.getByRole('dialog').count(), 0, 'Current Dialog API preserves journal compose open/close behavior');
    }
    for (const dataState of ['loading', 'empty', 'error']) {
      await render({ ...defaults, dataState, theme: 'dark', density: 'compact', environment: 'LIVE' });
      assert.equal(await page.locator('html').getAttribute('data-theme'), 'dark');
      assert.equal(await page.locator('body').getAttribute('data-theme-density'), 'compact');
      assert.equal(await page.locator('html').getAttribute('data-environment'), 'LIVE');
      assert.equal((await page.locator('[data-environment-badge]').first().innerText()).trim(), 'LIVE', `${slug}: visible chrome follows environment tweak`);
      const state = page.locator(`main .mds-dss--${dataState}`);
      assert.equal(await state.count(), 1, `${slug}: ${dataState} replaces the dominant plane`);
      assert.equal(await state.getAttribute('role'), dataState === 'error' ? 'alert' : 'status');
      assert.equal(await page.getByRole('heading', { level: 1 }).count(), 1, `${slug}: page identity survives ${dataState}`);
    }
    for (const density of ['default', 'terminal', 'spacious']) {
      await render({ ...defaults, density });
      assert.equal(await page.locator('body').getAttribute('data-theme-density'), density, `${slug}: ${density} density is effective`);
    }
    await page.evaluate(() => {
      MeridianPrefs.set('theme', 'dark'); MeridianPrefs.set('density', 'terminal'); MeridianPrefs.set('environment', 'FIXTURE');
    });
    await render({ ...defaults, theme: 'light', density: 'spacious', environment: 'LIVE' });
    assert.equal(await page.locator('html').getAttribute('data-theme'), 'dark', `${slug}: operator theme wins over preview seed`);
    assert.equal(await page.locator('body').getAttribute('data-theme-density'), 'terminal', `${slug}: operator density wins over preview seed`);
    assert.equal(await page.locator('html').getAttribute('data-environment'), 'FIXTURE', `${slug}: operator environment wins over preview seed`);
    assert.equal((await page.locator('[data-environment-badge]').first().innerText()).trim(), 'FIXTURE');
    assert.deepEqual(errors, [], `${slug}: no runtime errors`);
    console.log(`PASS ${slug}: ready/loading/empty/error, headings, environment, density and operator preference precedence`);
    await context.close();
  }
} finally {
  await browser.close();
}
