#!/usr/bin/env node
// Exercise the real DC host, prop forwarding and rendered state boundaries.
// Transform JSX in memory so this test needs no CDN or generated source edits.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { dirname, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { transform } from 'esbuild';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const templates = [
  ['alm-review', 'AlmReview', 'Asset-liability review'],
  ['close-management', 'CloseManagement', 'Close management'],
  ['books-loop', 'BooksLoop', 'Actual residual'],
  ['position-detail', 'PositionDetail', 'GA-FI-07'],
  ['backtest-compare', 'BacktestCompare', 'KPI deltas'],
  ['amx-governance', 'AmxGovernance', 'AMX Data Catalog'],
  ['backtest-builder', 'BacktestBuilder', 'Backtest — Bond Carry'],
  ['covered-call-lab', 'CoveredCallLab', 'Covered-Call Lab'],
  ['family-office', 'FamilyOffice', 'Whitfield Family Office'],
  ['journaling-workstation', 'JournalingWorkstation', 'Journaling'],
  ['reconciliation-workstation', 'ReconciliationWorkstation', 'Statement Reconciliation'],
  ['split-pane-reconciliation', 'Split Pane Reconciliation', 'Reconciliation'],
  ['strategy-builder', 'StrategyBuilder', 'Bond Carry'],
  ['strategy-onboarding', 'StrategyOnboarding', 'Set up your strategy'],
];
const runtime = await readFile(resolve(root, '_preview_runtime.js'), 'utf8');
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });
const mimeTypes = {
  '.html': 'text/html', '.js': 'application/javascript', '.jsx': 'application/javascript',
  '.css': 'text/css', '.svg': 'image/svg+xml', '.ttf': 'font/ttf', '.png': 'image/png',
};

try {
  for (const [slug, entry, readyText] of templates) {
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
    // support.js finds the local React peer; JSX responses are already transformed.
    await page.addInitScript({ content: runtime + ';window.Babel={transform:source=>({code:source})};' });
    await page.route('**/*', async route => {
      try {
        const url = new URL(route.request().url());
        if (url.origin !== 'http://meridian.test') return route.abort();
        const path = resolve(root, '.' + decodeURIComponent(url.pathname));
        if (!path.startsWith(root + '/')) return route.abort();
        let body = await readFile(path);
        const extension = extname(path);
        if (extension === '.jsx') {
          body = (await transform(body.toString(), {
            loader: 'jsx', format: 'iife', jsxFactory: 'React.createElement', jsxFragment: 'React.Fragment',
          })).code;
        }
        return route.fulfill({ body, contentType: mimeTypes[extension] || 'application/octet-stream' });
      } catch (error) {
        errors.push(`${route.request().url()}: ${error.message}`);
        return route.fulfill({ status: 404, body: 'Missing package resource' });
      }
    });

    await page.goto(`http://meridian.test/templates/${slug}/${entry}.dc.html`);
    await page.waitForFunction(() => !!window.__dcRootName?.());
    await page.getByText(readyText, { exact: false }).first().waitFor();
    assert.equal(await page.getByRole('main').count(), 1, `${slug}: one main landmark`);
    assert.equal(await page.getByRole('heading', { level: 1 }).count(), 1, `${slug}: actual page heading`);
    if (slug === 'alm-review') {
      await page.waitForFunction(() => /\$[0-9,.]+M/.test(document.body.innerText));
    }
    const name = await page.evaluate(() => window.__dcRootName());
    const schema = await page.evaluate(() => JSON.parse(document.querySelector('script[data-dc-script]').getAttribute('data-props')));
    assert(schema.$preview.width >= 1280 && schema.$preview.height >= 720, `${slug}: desktop preview`);
    for (const control of ['density', 'theme', 'environment', 'dataState']) assert(schema[control], `${slug}: ${control} control`);

    for (const state of ['loading', 'empty', 'error', 'ready']) {
      await page.evaluate(({ name, state }) => window.__dcSetProps(name, {
        dataState: state, theme: 'dark', density: 'terminal', environment: 'PAPER',
      }), { name, state });
      if (state === 'ready') await page.getByText(readyText, { exact: false }).first().waitFor();
      else await page.locator('.mds-dss--' + state).waitFor();
      assert.equal(await page.getAttribute('html', 'data-theme'), 'dark', `${slug}: theme`);
      assert.equal(await page.getAttribute('body', 'data-theme-density'), 'terminal', `${slug}: density`);
      assert.equal(await page.getAttribute('html', 'data-environment'), 'PAPER', `${slug}: environment`);
    }

    if (slug === 'backtest-compare') {
      await page.evaluate(name => window.__dcSetProps(name, {
        runA: '1852', runB: '1841', highlightDeltas: false,
      }), name);
      const returns = page.getByRole('row').filter({ hasText: 'Total return' });
      await returns.getByText('+28.91%', { exact: true }).waitFor();
      await returns.getByText('+24.61%', { exact: true }).waitFor();
      assert.equal(await returns.getByRole('cell').count(), 4, 'KPI rows retain baseline, candidate and delta');
    }
    assert.deepEqual(errors, [], `${slug}: no runtime or module-load failures`);
    console.log(`PASS ${slug}: actual DC ready/loading/empty/error and theme/density/environment`);
    await context.close();
  }
} finally {
  await browser.close();
}
