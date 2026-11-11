#!/usr/bin/env node
// Exercise current chart sources without writing the generated distribution.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const source = await build({
  absWorkingDir: root, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2020',
  stdin: { contents: `
    import { ScenarioSelector } from './components/charts/ScenarioSelector.jsx';
    import { ChartAnnotations } from './components/charts/ChartAnnotations.jsx';
    import { ChartFrame } from './components/charts/ChartFrame.jsx';
    import { PeerComparisonStrip } from './components/charts/PeerComparisonStrip.jsx';
    window.ChartA11y = { ScenarioSelector, ChartAnnotations, ChartFrame, PeerComparisonStrip };
  `, resolveDir: root, loader: 'js' },
  jsxFactory: 'window.React.createElement', jsxFragment: 'window.React.Fragment',
  plugins: [{ name: 'react-peer', setup(builder) {
    builder.onResolve({ filter: /^react$/ }, () => ({ path: 'react', namespace: 'peer' }));
    builder.onLoad({ filter: /.*/, namespace: 'peer' }, () => ({ contents: 'export default window.React;', loader: 'js' }));
  } }]
});
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
const errors = [];
page.on('pageerror', error => errors.push(error.message));

try {
  await page.route('http://meridian.test/**', route => route.fulfill({ contentType: 'text/html', body: `
    <!doctype html><html lang="en"><head><title>Chart accessibility regression</title></head>
    <body style="margin:16px;min-height:1800px"><div id="fixture" style="width:1000px"></div></body></html>
  ` }));
  await page.goto('http://meridian.test/chart-accessibility');
  await page.addScriptTag({ content: await readFile(resolve(root, '_preview_runtime.js'), 'utf8') });
  await page.addScriptTag({ content: source.outputFiles[0].text });
  await page.evaluate(() => {
    const h = React.createElement;
    const root = ReactDOM.createRoot(document.getElementById('fixture'));
    const scenarios = [
      { id: 'base', label: 'Base', headline: '4.2%', spark: [100, 101, 102] },
      { id: 'stress', label: 'Stress', headline: '-3.1%', tone: 'negative' },
      { id: 'recovery', label: 'Recovery', headline: '1.2%' }
    ];
    let revision = 0;
    function ScenarioFixture({ initial, interactive, frozen, layout, empty, version }) {
      const [value, setValue] = React.useState(initial);
      window.fixtureValue = value;
      window.fixtureVersion = version;
      return h(React.Fragment, null,
        h('button', null, 'Before selector'),
        h(ChartA11y.ScenarioSelector, {
          scenarios: empty ? [] : scenarios, value, layout, ariaLabel: 'Synthetic rate scenarios',
          onChange: interactive ? next => { window.changes.push(next); if (!frozen) setValue(next); } : null
        }),
        h('button', null, 'After selector')
      );
    }
    window.mountScenario = options => {
      window.changes = [];
      const version = ++revision;
      root.render(h(ScenarioFixture, { key: version, initial: 'base', interactive: true, ...options, version }));
      return version;
    };
    window.mountChartData = () => root.render(h(React.Fragment, null,
      h(ChartA11y.ChartFrame, {
        height: 180, n: 10, panes: [{ key: 'price', domain: [90, 110], ticks: 0 }],
        ariaLabel: 'Modeled price chart', ariaDescription: 'Synthetic prices in USD; not an execution instruction.'
      }, g => h(ChartA11y.ChartAnnotations, {
        g, pane: 'price', bands: [{ from: 2, to: 5, label: 'Scenario window', kind: 'scenario' }],
        events: [{ index: 4, badge: 'A', label: 'Policy input', kind: 'information', detail: 'Evidence scenario-inputs-42 <reviewed>' }],
        callouts: [{ index: 6, value: 101, text: 'Observed threshold', kind: 'warning' }]
      })),
      h(ChartA11y.PeerComparisonStrip, {
        subjectLabel: 'Synthetic fund', ariaLabel: 'Peer profile',
        metrics: [
          { label: 'Fee', value: 42, peers: [30, 40, 50, 60], median: 45, higherIsBetter: false, fmt: value => `${value} bp` },
          { label: 'Tied peers', value: 10, peers: [10, 10, 5], fmt: String },
          { label: 'No observed peers', value: 7, peers: [], fmt: String }
        ]
      })
    ));
  });

  const group = page.getByRole('radiogroup', { name: 'Synthetic rate scenarios' });
  const radio = label => group.getByRole('radio', { name: new RegExp(`^${label}`) });
  const mount = async (options = {}) => {
    const version = await page.evaluate(options => window.mountScenario(options), options);
    await page.waitForFunction(version => window.fixtureVersion === version, version);
  };
  const selected = async (label) => assert.equal(await radio(label).getAttribute('aria-checked'), 'true');
  await mount();
  assert.equal(await group.getAttribute('aria-orientation'), 'vertical');
  await page.getByRole('button', { name: 'Before selector' }).focus();
  await page.keyboard.press('Tab');
  assert(await radio('Base').evaluate(node => node === document.activeElement));
  await selected('Base');
  for (const [key, label] of [['ArrowDown', 'Stress'], ['ArrowRight', 'Recovery'], ['ArrowDown', 'Base'], ['ArrowUp', 'Recovery'], ['ArrowLeft', 'Stress']]) {
    await page.keyboard.press(key);
    await selected(label);
    assert(await radio(label).evaluate(node => node === document.activeElement), `${key} moves focus with selection`);
    assert.equal(await group.locator('[tabindex="0"]').count(), 1);
  }
  await radio('Recovery').focus();
  await page.keyboard.press('Space');
  await selected('Recovery');
  assert.equal(await page.evaluate(() => window.scrollY), 0, 'Space selects without scrolling');
  await radio('Base').focus();
  await page.keyboard.press('Enter');
  await selected('Base');
  const beforeIgnored = await page.evaluate(() => window.changes.length);
  await page.keyboard.press('a');
  await page.keyboard.press('Control+ArrowRight');
  assert.equal(await page.evaluate(() => window.changes.length), beforeIgnored, 'Unrelated and modified keys do not select');
  await page.keyboard.press('Tab');
  assert(await page.getByRole('button', { name: 'After selector' }).evaluate(node => node === document.activeElement), 'Tab exits the group');
  await radio('Stress').click();
  await selected('Stress');
  assert(await radio('Stress').evaluate(node => node === document.activeElement), 'Pointer selection also focuses its option');
  console.log('PASS ScenarioSelector: focus, wrapping arrows, Space/Enter, pointer and Tab behavior');

  for (const initial of [null, 'removed-scenario']) {
    await mount({ initial });
    assert.equal(await radio('Base').getAttribute('tabindex'), '0');
    assert.equal(await group.locator('[aria-checked="true"]').count(), 0);
    await radio('Base').focus();
    await page.keyboard.press('ArrowRight');
    await selected('Stress');
  }
  await mount({ frozen: true, layout: 'columns' });
  assert.equal(await group.getAttribute('aria-orientation'), 'horizontal');
  await radio('Base').focus();
  await page.keyboard.press('ArrowRight');
  assert(await radio('Stress').evaluate(node => node === document.activeElement));
  await page.keyboard.press('ArrowRight');
  assert(await radio('Recovery').evaluate(node => node === document.activeElement));
  await selected('Base');
  assert.deepEqual(await page.evaluate(() => window.changes), ['stress', 'recovery'], 'Navigation follows focus while the caller remains selection authority');
  console.log('PASS ScenarioSelector: missing IDs remain reachable and controlled selection stays authoritative');

  await mount({ interactive: false });
  assert.equal(await group.locator('[aria-disabled="true"]').count(), 3);
  assert.equal(await group.locator('[tabindex="0"]').count(), 0);
  await page.getByRole('button', { name: 'Before selector' }).focus();
  await page.keyboard.press('Tab');
  assert(await page.getByRole('button', { name: 'After selector' }).evaluate(node => node === document.activeElement));
  await radio('Stress').dispatchEvent('click');
  await radio('Stress').focus();
  for (const key of ['ArrowRight', 'Enter', 'Space']) await page.keyboard.press(key);
  assert.deepEqual(await page.evaluate(() => window.changes), []);
  await selected('Base');
  await mount({ empty: true });
  assert.equal(await group.getByRole('radio').count(), 0);
  assert.deepEqual(await page.evaluate(() => window.changes), []);
  console.log('PASS ScenarioSelector: printed and empty records do not produce selection requests');

  await page.evaluate(() => window.mountChartData());
  const annotations = page.getByRole('img', { name: /^Chart annotations\./ });
  await annotations.waitFor();
  const annotationName = await annotations.getAttribute('aria-label');
  for (const content of ['Scenario window', 'positions 2 to 5', 'Event A', 'Policy input', 'information', 'position 4', 'Evidence scenario-inputs-42 <reviewed>', 'Observed threshold', 'value 101']) {
    assert(annotationName.includes(content), `Annotation text exposes ${content}`);
  }
  assert.equal(await annotations.locator('title').textContent(), 'Policy input: Evidence scenario-inputs-42 <reviewed>');
  const snapshot = await page.locator('body').ariaSnapshot();
  assert(snapshot.includes('Evidence scenario-inputs-42 <reviewed>'), 'Event detail appears in the actual chart accessibility tree');
  assert.equal(await page.locator('svg reviewed').count(), 0, 'Annotation detail remains text');
  const table = page.getByRole('table', { name: 'Peer profile' });
  assert.equal(await table.getByRole('columnheader', { name: 'Peer values', exact: true }).count(), 1);
  assert.equal(await table.getByRole('row', { name: /^Fee / }).getByRole('cell', { name: '30 bp, 40 bp, 50 bp, 60 bp', exact: true }).count(), 1);
  assert.equal(await table.getByRole('row', { name: /^Tied peers / }).getByRole('cell', { name: '10, 10, 5', exact: true }).count(), 1);
  assert.equal(await table.getByRole('row', { name: /^No observed peers / }).getByRole('cell', { name: 'no peers', exact: true }).count(), 2);
  assert.equal(await page.locator('button,a,input,[tabindex]').count(), 0, 'Static chart data adds no artificial keyboard stops');
  assert.deepEqual(errors, []);
  console.log('PASS ChartAnnotations/PeerComparisonStrip: full event explanations and individual peer values are accessible');
} finally {
  await browser.close();
}
