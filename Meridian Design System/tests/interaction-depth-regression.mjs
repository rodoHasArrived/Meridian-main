#!/usr/bin/env node
// Source-level browser regression: no generated package files are written.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build, transform } from 'esbuild';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const grid = await build({
  absWorkingDir: root, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2020',
  resolveExtensions: ['.js', '.jsx', '.ts', '.tsx', '.json'],
  stdin: { contents: `import { DataGrid } from './components/data/DataGrid.jsx'; window.MeridianDesignSystem_205d17.DataGrid = DataGrid;`, resolveDir: root, loader: 'js' },
  jsxFactory: 'window.React.createElement', jsxFragment: 'window.React.Fragment',
  plugins: [{ name: 'react-peer', setup(builder) {
    builder.onResolve({ filter: /^react$/ }, () => ({ path: 'react', namespace: 'peer' }));
    builder.onLoad({ filter: /.*/, namespace: 'peer' }, () => ({ contents: 'export default window.React;', loader: 'js' }));
  } }]
});
const screen = await transform(await readFile(resolve(root, 'templates/interaction-depth/screen.jsx'), 'utf8'), {
  loader: 'jsx', format: 'iife', target: 'es2020', jsxFactory: 'React.createElement', jsxFragment: 'React.Fragment'
});
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
const errors = [];
page.on('pageerror', error => errors.push(error.message));
try {
  await page.route('http://meridian.test/**', route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><html lang="en"><head><title>Interaction Depth regression</title></head><body><div id="fixture" style="height:760px;width:1380px"></div></body></html>' }));
  await page.goto('http://meridian.test/interaction-depth');
  for (const name of ['_preview_runtime.js', '_ds_bundle.js']) {
    await page.addScriptTag({ content: await readFile(resolve(root, name), 'utf8') });
  }
  await page.addScriptTag({ content: grid.outputFiles[0].text });
  await page.addScriptTag({ content: screen.code });
  await page.evaluate(() => {
    const DS = window.MeridianDesignSystem_205d17;
    const Provider = DS.OptimisticProvider;
    window.fixtureEdits = [];
    function LedgerProbe() {
      window.fixtureLedger = DS.OptimisticHooks.useOptimistic();
      return null;
    }
    DS.OptimisticProvider = function RecordingProvider(props) {
      return React.createElement(Provider, { ...props, onSettle(edit) {
        window.fixtureEdits.push(edit);
        props.onSettle?.(edit);
      } }, React.createElement(React.Fragment, null, React.createElement(LedgerProbe), props.children));
    };
    ReactDOM.createRoot(document.getElementById('fixture')).render(React.createElement(window.InteractionDepthPlane, {
      rowCount: 2000,
      onSummary(summary) { window.fixtureSummary = summary; },
      onApi(api) { window.fixtureApi = api; }
    }));
  });
  const filter = page.getByRole('textbox', { name: 'Filter by symbol or name' });
  await filter.waitFor({ timeout: 5000 }).catch(error => { throw new Error(`${error.message}\nRuntime errors: ${JSON.stringify(errors)}`); });
  await filter.evaluate(node => { window.filterNode = node; });
  await filter.focus();
  await filter.pressSequentially('AB');
  assert.equal(await filter.inputValue(), 'AB', 'All typed characters survive controlled query updates');
  assert(await page.evaluate(() => document.activeElement === window.filterNode && window.filterNode.isConnected), 'Filtering retains the original focused input');
  console.log('PASS Interaction Depth: query input identity and focus');
  await filter.fill('');
  await page.waitForFunction(() => window.fixtureSummary?.shown === 2000);

  const plane = page.getByLabel('Instrument grid · j/k move, x select, c compare, right-click for verbs', { exact: true });
  const gridRegion = page.getByRole('region', { name: 'Instruments', exact: true });
  await plane.evaluate(node => { window.planeNode = node; });
  await plane.focus();
  await plane.press('j');
  await plane.press('j');
  await plane.press('x');
  await page.waitForFunction(() => window.fixtureSummary?.selected === 1);
  assert(await page.evaluate(() => document.activeElement === window.planeNode), 'Keyboard navigation and selection retain plane focus');
  const active = await page.evaluate(() => window.fixtureSummary.active);
  assert(await page.locator(`tr[data-row-key="${active}"] input[aria-label="Select row"]`).isChecked(), 'Keyboard selection targets the active row');

  console.log('PASS Interaction Depth: keyboard movement and selection focus');
  await page.evaluate(() => window.fixtureApi.clear());
  await page.waitForFunction(() => window.fixtureSummary?.selected === 0);
  await gridRegion.evaluate(node => {
    window.gridNode = node;
    node.scrollTop = 720;
    node.dispatchEvent(new Event('scroll', { bubbles: true }));
  });
  await page.waitForFunction(() => document.querySelector('tr[data-row-key]')?.getAttribute('data-row-key') !== 'AAA-0');
  // Skip the eight overscan rows above the viewport so Playwright need not scroll.
  const firstVisible = page.locator('tr[data-row-key]').nth(8);
  const firstKey = await firstVisible.getAttribute('data-row-key');
  await firstVisible.locator('input[aria-label="Select row"]').check();
  assert(await page.evaluate(() => document.querySelector('.mds-grid') === window.gridNode), 'Selection retains the mounted DataGrid');
  assert.equal(await gridRegion.evaluate(node => node.scrollTop), 720, 'Selection retains virtualized scroll position');
  assert(await page.locator(`tr[data-row-key="${firstKey}"] input[aria-label="Select row"]`).isChecked());

  console.log('PASS Interaction Depth: virtualized selection keeps grid and scroll');
  const secondVisible = page.locator('tr[data-row-key]').nth(9);
  const targetKey = await secondVisible.getAttribute('data-row-key');
  const targetSymbol = targetKey.split('-')[0];
  await firstVisible.click();
  await secondVisible.click({ button: 'right' });
  await page.getByRole('menuitem', { name: `Open ${targetSymbol}`, exact: true }).waitFor();
  assert.equal(await page.evaluate(() => window.fixtureSummary.active), targetKey, 'Right-click activates the row under the pointer');
  await page.getByRole('menuitem', { name: 'Set ack budget → 250', exact: true }).click();
  await page.waitForFunction(() => window.fixtureEdits.some(edit => edit.status === 'confirmed'));
  const edit = await page.evaluate(() => window.fixtureEdits.findLast(entry => entry.status === 'confirmed'));
  assert.equal(edit.rowId, targetKey, 'Context action edits the right-clicked row rather than a prior active row');
  assert.equal(edit.field, 'budget');
  assert.equal(edit.value, '250');

  await page.getByRole('columnheader', { name: /^Symbol/ }).click({ button: 'right' });
  const emptyMenu = page.getByRole('menuitem', { name: 'No row under the pointer', exact: true });
  await emptyMenu.waitFor();
  assert(await emptyMenu.isDisabled(), 'Header context cannot fall back to an unrelated active row');
  assert.equal(await page.getByRole('menuitem', { name: 'Set ack budget → 250', exact: true }).count(), 0);
  // Keep selection/query unchanged between writes: the host's retained API must
  // use the newly confirmed model rather than a pre-settlement valueOf closure.
  await page.keyboard.press('Escape');
  await page.evaluate(() => window.fixtureApi.clear());
  await page.waitForFunction(() => window.fixtureSummary?.selected === 0);
  await filter.fill('AAA');
  await page.waitForFunction(() => window.fixtureSummary?.shown === 1);
  await page.getByRole('button', { name: /^Columns/ }).click();
  await page.locator('.mds-colchz__item').filter({ hasText: 'Ack budget' }).click();
  await page.getByRole('button', { name: /^Columns/ }).click();
  const apiRow = page.locator('tr[data-row-key="AAA-0"]');
  await apiRow.locator('input[aria-label="Select row"]').check();
  await page.waitForFunction(() => window.fixtureSummary?.selected === 1);
  await page.evaluate(() => window.fixtureApi.bulkBudget('1234'));
  await page.waitForFunction(() => window.fixtureEdits.some(edit => edit.rowId === 'AAA-0' && edit.value === '1234' && edit.status === 'confirmed'));
  await apiRow.getByRole('button', { name: 'Edit 1234', exact: true }).waitFor();
  const firstBudget = await page.evaluate(() => window.fixtureEdits.findLast(edit => edit.rowId === 'AAA-0' && edit.status === 'confirmed'));
  assert.equal(firstBudget.prior, 180, 'Fixture baseline differs from both tested budgets');
  await page.evaluate(() => window.fixtureApi.bulkBudget('5678'));
  await page.waitForFunction(() => window.fixtureEdits.some(edit => edit.rowId === 'AAA-0' && edit.value === '5678' && edit.status === 'confirmed'));
  await apiRow.getByRole('button', { name: 'Edit 5678', exact: true }).waitFor();
  const secondBudget = await page.evaluate(() => window.fixtureEdits.findLast(edit => edit.rowId === 'AAA-0' && edit.status === 'confirmed'));
  assert.equal(secondBudget.prior, '1234', 'Repeated host API command records the last confirmed value as its prior');
  await page.evaluate(id => window.fixtureLedger.revert(id, 'Synthetic repeated-command regression'), secondBudget.id);
  await page.waitForFunction(id => window.fixtureEdits.some(edit => edit.id === id && edit.status === 'reverted'), secondBudget.id);
  await apiRow.getByRole('button', { name: 'Edit 1234', exact: true }).waitFor();
  const rollback = await page.evaluate(id => window.fixtureEdits.findLast(edit => edit.id === id), secondBudget.id);
  assert.equal(rollback.prior, '1234', 'Rollback restores the previous confirmed budget');
  console.log('PASS Interaction Depth: retained host API tracks confirmed edits and restores the correct prior');
  assert.deepEqual(errors, [], 'No runtime errors');
  console.log('PASS Interaction Depth: query focus, keyboard selection, virtualized continuity, exact row context actions, header fail-closed');
} finally {
  await browser.close();
}
