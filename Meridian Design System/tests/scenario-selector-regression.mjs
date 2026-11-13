#!/usr/bin/env node
// Source-level keyboard regression; does not write generated package files.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const source = await build({
  absWorkingDir: root, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2020',
  stdin: { contents: `import { ScenarioSelector } from './components/charts/ScenarioSelector.jsx'; window.ScenarioSelector = ScenarioSelector;`, resolveDir: root, loader: 'js' },
  jsxFactory: 'window.React.createElement', jsxFragment: 'window.React.Fragment',
  plugins: [{ name: 'react-peer', setup(builder) {
    builder.onResolve({ filter: /^react$/ }, () => ({ path: 'react', namespace: 'peer' }));
    builder.onLoad({ filter: /.*/, namespace: 'peer' }, () => ({ contents: 'export default window.React;', loader: 'js' }));
  } }]
});
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });
const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
const errors = [];
page.on('pageerror', error => errors.push(error.message));
try {
  await page.setContent('<!doctype html><html lang="en"><head><title>Scenario selector keyboard regression</title></head><body><main><h1>Synthetic scenario choices</h1><div id="fixture"></div></main></body></html>');
  await page.addScriptTag({ content: await readFile(resolve(root, '_preview_runtime.js'), 'utf8') });
  await page.addScriptTag({ content: source.outputFiles[0].text });
  await page.evaluate(() => {
    const root = ReactDOM.createRoot(document.getElementById('fixture'));
    window.fixtureRevision = 0;
    const options = [
      { id: 'base', label: 'Base', headline: '4.2%', spark: [100, 101, 102] },
      { id: 'stress', label: 'Stress', headline: '−3.1%', tone: 'negative' },
      { id: 'recovery', label: 'Recovery', headline: '+1.0%', tone: 'positive' }
    ];
    function Fixture({ config, revision }) {
      const [value, setValue] = React.useState(config.value);
      React.useLayoutEffect(() => { document.getElementById('fixture').dataset.revision = String(revision); }, [revision]);
      return React.createElement('form', { onSubmit(event) { event.preventDefault(); window.fixtureSubmits += 1; } },
        React.createElement('button', { type: 'button' }, 'Before scenarios'),
        React.createElement(window.ScenarioSelector, {
          scenarios: config.empty ? [] : options,
          value,
          ariaLabel: 'Synthetic rate scenarios',
          onChange: config.readOnly ? null : id => { window.fixtureEvents.push(id); setValue(id); }
        }),
        React.createElement('button', { type: 'button' }, 'After scenarios'));
    }
    window.mountScenarioFixture = config => {
      window.fixtureEvents = [];
      window.fixtureSubmits = 0;
      const revision = ++window.fixtureRevision;
      root.render(React.createElement(Fixture, { key: revision, config, revision }));
      return revision;
    };
  });
  async function mount(config) {
    const revision = await page.evaluate(config => window.mountScenarioFixture(config), config);
    await page.waitForFunction(revision => document.getElementById('fixture').dataset.revision === String(revision), revision);
  }
  const group = page.getByRole('radiogroup', { name: 'Synthetic rate scenarios' });
  const base = group.getByRole('radio', { name: /^Base/ });
  const stress = group.getByRole('radio', { name: /^Stress/ });
  const recovery = group.getByRole('radio', { name: /^Recovery/ });
  async function assertFocusedAndSelected(option) {
    assert(await option.evaluate(node => node === document.activeElement), 'Focus follows the scenario option');
    assert.equal(await option.getAttribute('aria-checked'), 'true', 'Focused scenario reflects controlled selection');
    assert.equal(await group.locator('[tabindex="0"]').count(), 1, 'There is one group Tab stop');
  }

  await mount({ value: 'stress' });
  await page.getByRole('button', { name: 'Before scenarios' }).focus();
  await page.keyboard.press('Tab');
  await assertFocusedAndSelected(stress);
  await page.keyboard.press('ArrowRight');
  await assertFocusedAndSelected(recovery);
  await page.keyboard.press('ArrowDown');
  await assertFocusedAndSelected(base);
  await page.keyboard.press('ArrowLeft');
  await assertFocusedAndSelected(recovery);
  await page.keyboard.press('ArrowUp');
  await assertFocusedAndSelected(stress);
  await page.keyboard.press('Home');
  await assertFocusedAndSelected(base);
  await page.keyboard.press('End');
  await assertFocusedAndSelected(recovery);
  await page.keyboard.press('Tab');
  assert(await page.getByRole('button', { name: 'After scenarios' }).evaluate(node => node === document.activeElement), 'Tab exits the radio group');
  assert.deepEqual(await page.evaluate(() => window.fixtureEvents), ['recovery', 'base', 'recovery', 'stress', 'base', 'recovery']);

  for (const key of ['Space', 'Enter']) {
    await mount({ value: 'missing-restored-scenario' });
    assert.equal(await group.locator('[aria-checked="true"]').count(), 0, 'Invalid saved selection is not silently replaced');
    assert.deepEqual(await page.evaluate(() => window.fixtureEvents), []);
    await page.getByRole('button', { name: 'Before scenarios' }).focus();
    await page.keyboard.press('Tab');
    assert(await base.evaluate(node => node === document.activeElement), 'Invalid saved selection keeps a reachable fallback');
    await page.keyboard.press(key);
    await assertFocusedAndSelected(base);
    assert.deepEqual(await page.evaluate(() => window.fixtureEvents), ['base'], `${key} activates exactly once`);
    assert.equal(await page.evaluate(() => window.fixtureSubmits), 0, `${key} cannot submit a surrounding form`);
  }

  await mount({ value: null });
  await page.getByRole('button', { name: 'Before scenarios' }).focus();
  await page.keyboard.press('Tab');
  await page.keyboard.press('ArrowLeft');
  await assertFocusedAndSelected(recovery);

  await mount({ value: 'stress', readOnly: true });
  assert.equal(await group.locator('[tabindex="0"]').count(), 0, 'Printed records are outside the Tab order');
  assert(await base.isDisabled());
  assert(await stress.isDisabled());
  assert.equal(await stress.getAttribute('aria-checked'), 'true');
  await page.getByRole('button', { name: 'Before scenarios' }).focus();
  await page.keyboard.press('Tab');
  assert(await page.getByRole('button', { name: 'After scenarios' }).evaluate(node => node === document.activeElement));
  assert.deepEqual(await page.evaluate(() => window.fixtureEvents), []);

  await mount({ value: null, empty: true });
  assert.equal(await group.getByRole('radio').count(), 0);
  assert.deepEqual(errors, [], 'No browser runtime errors');
  console.log('PASS ScenarioSelector: controlled focus/selection, wrapping, Home/End, Space/Enter, stale selection, read-only, empty, form safety');
} finally { await browser.close(); }
