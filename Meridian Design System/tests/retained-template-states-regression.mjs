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
  ['backtest-compare', 'BacktestCompare', 'Compare runs'],
  ['amx-governance', 'AmxGovernance', 'Governance & AMX catalog'],
  ['backtest-builder', 'BacktestBuilder', 'Backtest — Bond Carry'],
  ['covered-call-lab', 'CoveredCallLab', 'Covered-Call Lab'],
  ['family-office', 'FamilyOffice', 'Whitfield Family Office'],
  ['journaling-workstation', 'JournalingWorkstation', 'Accounting'],
  ['reconciliation-workstation', 'ReconciliationWorkstation', 'Statement Reconciliation'],
  ['split-pane-reconciliation', 'Split Pane Reconciliation', 'Reconciliation'],
  ['strategy-builder', 'StrategyBuilder', 'Bond Carry'],
  ['strategy-onboarding', 'StrategyOnboarding', 'Strategy formation'],
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
    const entryHtml = await readFile(resolve(root, 'templates', slug, entry + '.dc.html'), 'utf8');
    const compatibilityTarget = entryHtml.match(/component-from-global-scope="MeridianTemplateCompatibility"[^>]*target="([^"]+)"/)?.[1]?.replaceAll('&amp;', '&');
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
    async function readyPlane() {
      let plane = page;
      if (compatibilityTarget) {
        const iframe = page.locator('iframe[data-meridian-compatibility-target]');
        await iframe.waitFor();
        const expected = new URL(compatibilityTarget, page.url());
        await page.waitForFunction(({ pathname, search }) => {
          const frame = document.querySelector('iframe[data-meridian-compatibility-target]');
          return frame?.contentDocument?.readyState === 'complete'
            && frame.contentWindow.location.pathname === pathname
            && [...new URLSearchParams(search)].every(([key, value]) => new URL(frame.src).searchParams.get(key) === value)
            && !!frame.contentWindow.__dcRootName?.();
        }, { pathname: expected.pathname, search: expected.search });
        plane = await (await iframe.elementHandle()).contentFrame();
        assert(plane, `${slug}: retained entry mounts its actual canonical frame`);
      }
      await plane.getByText(readyText, { exact: false }).first().waitFor();
      assert((await plane.locator('body').innerText()).length > 80, `${slug}: substantive canonical work plane`);
      return plane;
    }
    await readyPlane();
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
      if (state === 'ready') {
        const plane = await readyPlane();
        if (compatibilityTarget) {
          await plane.waitForFunction(() => document.documentElement.dataset.theme === 'dark'
            && document.body.dataset.themeDensity === 'terminal'
            && document.documentElement.dataset.environment === 'PAPER');
        }
      }
      else await page.locator('.mds-dss--' + state).waitFor();
      assert.equal(await page.getAttribute('html', 'data-theme'), 'dark', `${slug}: theme`);
      assert.equal(await page.getAttribute('body', 'data-theme-density'), 'terminal', `${slug}: density`);
      assert.equal(await page.getAttribute('html', 'data-environment'), 'PAPER', `${slug}: environment`);
    }

    if (slug === 'backtest-compare') {
      // The retained URL forwards to StrategyRuns?view=compare. Its canonical
      // comparison owns run identity in the actual selects and URL, rather
      // than the retired wrapper's runA/runB preview props.
      const plane = await readyPlane();
      const baseline = plane.getByLabel('Run A · baseline', { exact: true });
      const candidate = plane.getByLabel('Run B · candidate', { exact: true });
      const aLabel = (await baseline.innerText()).trim(), bLabel = (await candidate.innerText()).trim();
      const a = aLabel.match(/^(.+) · v[^ ]+ · (?:FIXTURE|PAPER|LIVE)(?: · legacy)?$/i)?.[1];
      const b = bLabel.match(/^(.+) · v[^ ]+ · (?:FIXTURE|PAPER|LIVE)(?: · legacy)?$/i)?.[1];
      assert(a && b && a !== b, `Comparison retains two distinct immutable run identities: ${JSON.stringify({ aLabel, bLabel })}`);
      const returns = plane.getByRole('row').filter({ hasText: 'Total return' });
      const before = await returns.getByRole('cell').allTextContents();
      assert.equal(before.length, 5, 'KPI rows retain metric, baseline, candidate, delta and declared method');
      await plane.getByRole('button', { name: 'Swap', exact: true }).click();
      await plane.waitForFunction(({ a, b }) => {
        return new URL(location.href).searchParams.get('runA') === b
          && new URL(location.href).searchParams.get('runB') === a;
      }, { a, b });
      assert.equal((await baseline.innerText()).trim(), bLabel, 'Swap preserves exact candidate identity as the new baseline');
      assert.equal((await candidate.innerText()).trim(), aLabel, 'Swap preserves exact baseline identity as the new candidate');
      const after = await returns.getByRole('cell').allTextContents();
      assert.deepEqual([after[1], after[2], after[4]], [before[2], before[1], before[4]], 'Swap exchanges financial values while retaining the declared method');
      const number = value => Number(value.replaceAll('−', '-').replaceAll('%', '').replaceAll(',', '').trim());
      assert(Number.isFinite(number(before[3])) && Number.isFinite(number(after[3])), 'Comparison exposes finite derived deltas');
      assert.equal(number(after[3]), -number(before[3]), 'Delta remains candidate minus baseline after swap');
    }
    assert.deepEqual(errors, [], `${slug}: no runtime or module-load failures`);
    console.log(`PASS ${slug}: actual DC ready/loading/empty/error and theme/density/environment`);
    await context.close();
  }
} finally {
  await browser.close();
}
