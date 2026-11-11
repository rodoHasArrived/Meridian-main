#!/usr/bin/env node
// Source-level keyboard and accessibility regression for ChartAnnotations (overlay + List) and
// PeerComparisonStrip. Does not write generated package files.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
const source = await build({
  absWorkingDir: root, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2020',
  stdin: {
    contents: `
      import { ChartFrame } from './components/charts/ChartFrame.jsx';
      import { ChartAnnotations } from './components/charts/ChartAnnotations.jsx';
      import { PeerComparisonStrip } from './components/charts/PeerComparisonStrip.jsx';
      window.Charts = { ChartFrame, ChartAnnotations, PeerComparisonStrip };`,
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
const errors = [];

// styles.css is @import lines only; inline the token files it names so the fixtures render with
// the real palette and focus roles instead of fallbacks.
const entry = await readFile(resolve(root, 'styles.css'), 'utf8');
const tokenCss = (await Promise.all([...entry.matchAll(/@import\s+"([^"]+)"/g)]
  .map(([, path]) => readFile(resolve(root, path), 'utf8')))).join('\n').replace(/@import[^;]+;/g, '').replace(/@font-face\s*\{[^}]*\}/g, '');
assert.match(tokenCss, /--interaction-focus\s*:/, 'token styles are loaded');

async function open(theme, forcedColors) {
  const context = await browser.newContext({ viewport: { width: 1100, height: 760 }, forcedColors: forcedColors ? 'active' : 'none' });
  const page = await context.newPage();
  page.on('pageerror', (error) => errors.push(error.message));
  await page.setContent(`<!doctype html><html lang="en" data-theme="${theme}"><head><title>Chart accessibility regression</title>
    <style>${tokenCss}</style></head>
    <body><main><h1>Synthetic chart fixtures</h1><button id="before" type="button">Before</button>
    <section id="annotations"></section><section id="peers"></section><button id="after" type="button">After</button></main></body></html>`);
  await page.addScriptTag({ content: await readFile(resolve(root, '_preview_runtime.js'), 'utf8') });
  await page.addScriptTag({ content: source.outputFiles[0].text });
  await page.addScriptTag({ path: axePath });
  await page.evaluate(() => {
    const { ChartFrame, ChartAnnotations, PeerComparisonStrip } = window.Charts;
    const h = React.createElement;
    const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug'];
    const bands = [{ id: 'window', from: 2, to: 5, label: 'Scenario window', kind: 'scenario', detail: 'Rates held 50 bp above the base path.' }];
    const events = [
      { id: 'policy', index: 4, badge: 'A', label: 'Policy change', kind: 'warning', detail: 'Synthetic policy change; fixture only.' },
      { id: 'rebalance', index: 1, badge: 'B', label: 'Rebalance' },
    ];
    const callouts = [{ id: 'peak', index: 6, value: 104, text: 'Observed peak', detail: 'Highest synthetic close in the window.' }];
    function Annotated() {
      const [active, setActive] = React.useState(null);
      window.activeAnnotation = active;
      return h('div', { style: { width: 720 } },
        h(ChartFrame, { n: months.length, height: 220, ariaLabel: 'Synthetic price with annotations', panes: [{ key: 'price', domain: [90, 110] }] },
          (g) => h(ChartAnnotations, { g, pane: 'price', bands, events, callouts, activeId: active })),
        h(ChartAnnotations.List, {
          bands, events, callouts, ariaLabel: 'Annotations on synthetic price',
          formatIndex: (i) => months[i], formatValue: (v) => `$${v.toFixed(2)}`, activeId: active, onActiveChange: setActive,
        }));
    }
    ReactDOM.createRoot(document.getElementById('annotations')).render(h(Annotated));
    ReactDOM.createRoot(document.getElementById('peers')).render(h('div', { style: { width: 720 } }, h(PeerComparisonStrip, {
      subjectLabel: 'Synthetic fund', ariaLabel: 'Synthetic fund relative profile',
      caption: 'Synthetic comparable funds; one reporting period.',
      metrics: [
        { id: 'fee', label: 'Fee (bp)', value: 42, peers: [60, 30, 50, 40], peerLabels: ['Fund D', 'Fund A', 'Fund C', 'Fund B'], higherIsBetter: false, fmt: (v) => `${v.toFixed(0)} bp` },
        { id: 'ret', label: 'Return (%)', value: 6.1, peers: [4.2, 7.5, 5.0], higherIsBetter: true },
        { id: 'none', label: 'No peers', value: 1.5, peers: [] },
      ],
    })));
  });
  await page.waitForSelector('#annotations ol button');
  await page.waitForSelector('#peers button');
  return { page, context };
}

async function axe(page, label) {
  const result = await page.evaluate(() => window.axe.run(document.querySelector('main'), { runOnly: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] }));
  assert.deepEqual(result.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(' | ')}`), [], `axe violations (${label})`);
}

try {
  // ── ChartAnnotations.List: every annotation reachable and readable by keyboard ───────────
  const { page, context } = await open('light', false);
  const list = page.locator('#annotations ol');
  assert.equal(await list.getAttribute('aria-label'), 'Annotations on synthetic price');
  const names = await list.locator('button').evaluateAll((buttons) => buttons.map((b) => b.textContent.replace(/\s+/g, ' ').trim()));
  assert.equal(names.length, 4, 'one control per band, event and callout');
  assert.match(names[0], /Feb.*Rebalance.*Event/, 'time order: the February event is first');
  assert.match(names[1], /Mar to Jun.*Scenario window.*Regime · Scenario/, 'a band names its range and its kind in words');
  assert.match(names[2], /May.*Policy change.*Event · Warning/, 'semantic kind is stated in text, not colour alone');
  assert.match(names[3], /Jul, \$104\.00.*Observed peak.*Callout/, 'a callout carries its formatted value');
  assert.equal(await page.locator('#annotations svg[role="img"] [tabindex], #annotations svg[role="img"] button').count(), 0,
    'nothing focusable is placed inside the image-role chart');

  await page.locator('#before').focus();
  await page.keyboard.press('Tab');
  const focused = () => page.evaluate(() => document.activeElement.getAttribute('data-annotation-id'));
  assert.equal(await focused(), 'rebalance', 'Tab enters the list at the first annotation');
  assert.equal(await page.evaluate(() => window.activeAnnotation), 'rebalance', 'focus reports the active annotation to the overlay');
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('ArrowDown');
  assert.equal(await focused(), 'policy', 'arrow keys move in time order');
  const strokeOf = () => page.evaluate(() => [...document.querySelectorAll('#annotations svg rect')].filter((r) => r.getAttribute('rx') === '2').map((r) => r.getAttribute('stroke')));
  assert.equal((await strokeOf()).filter((s) => s && s !== 'none').length, 1, 'exactly one overlay badge is emphasised for the focused annotation');
  await page.keyboard.press('End');
  assert.equal(await focused(), 'peak');
  await page.keyboard.press('Home');
  assert.equal(await focused(), 'rebalance');
  assert.equal(await page.evaluate(() => document.activeElement.getAttribute('aria-expanded')), null, 'an annotation without detail is not a disclosure');

  await page.keyboard.press('ArrowDown');
  assert.equal(await page.evaluate(() => document.activeElement.getAttribute('aria-expanded')), 'false');
  await page.keyboard.press('Enter');
  assert.equal(await page.evaluate(() => document.activeElement.getAttribute('aria-expanded')), 'true', 'Enter opens the detail');
  const controlled = await page.evaluate(() => document.getElementById(document.activeElement.getAttribute('aria-controls'))?.textContent);
  assert.equal(controlled, 'Rates held 50 bp above the base path.', 'aria-controls points at the rendered detail');
  await axe(page, 'annotations detail open, light');
  await page.keyboard.press('Escape');
  assert.equal(await page.evaluate(() => document.activeElement.getAttribute('aria-expanded')), 'false', 'Escape closes the detail');
  assert.equal(await focused(), 'window', 'focus stays on the annotation that opened it');
  await page.keyboard.press('Space');
  assert.equal(await page.evaluate(() => document.activeElement.getAttribute('aria-expanded')), 'true', 'Space opens the detail');
  await page.keyboard.press('Space');

  const ring = await page.evaluate(() => { const s = getComputedStyle(document.activeElement); return { style: s.outlineStyle, width: s.outlineWidth }; });
  assert.deepEqual(ring, { style: 'solid', width: '2px' }, 'keyboard focus is visibly ringed');

  // ── PeerComparisonStrip: peer values reachable by keyboard, not title-only ───────────────
  assert.equal(await page.locator('#peers [title]').count(), 0, 'no value is disclosed through a title attribute alone');
  const rails = page.locator('#peers button');
  assert.equal(await rails.count(), 2, 'a row without peers has no empty disclosure');
  const railName = await rails.first().getAttribute('aria-label');
  assert.equal(railName, 'Fee (bp): Synthetic fund 42 bp, peer median 45 bp, rank 3 of 5. 4 peer values');
  await rails.first().focus();
  assert.equal(await rails.first().getAttribute('aria-expanded'), 'false');
  await page.keyboard.press('Enter');
  assert.equal(await rails.first().getAttribute('aria-expanded'), 'true');
  const detail = await page.evaluate(() => document.getElementById(document.activeElement.getAttribute('aria-controls')).innerText.replace(/\s+/g, ' ').trim());
  assert.equal(detail, 'Peer median 45 bp Fund A 30 bp Fund B 40 bp Fund C 50 bp Fund D 60 bp', 'each peer is named with its value, in ascending order');
  await axe(page, 'peer detail open, light');
  await page.keyboard.press('Escape');
  assert.equal(await rails.first().getAttribute('aria-expanded'), 'false', 'Escape closes the peer detail');
  assert.equal(await page.evaluate(() => document.activeElement === document.querySelector('#peers button')), true, 'focus returns to the rail');
  await rails.first().click();
  assert.equal(await rails.first().getAttribute('aria-expanded'), 'true', 'pointer activation opens the same detail');
  await rails.first().click();

  const cells = await page.locator('#peers table tbody tr').evaluateAll((rows) => rows.map((row) => [...row.children].map((cell) => cell.textContent)));
  assert.deepEqual(cells[0], ['Fee (bp)', '42 bp', '45 bp', '3 of 5', 'Fund A 30 bp; Fund B 40 bp; Fund C 50 bp; Fund D 60 bp']);
  assert.equal(cells[1][4], 'Peer 1 4.20; Peer 3 5.00; Peer 2 7.50', 'unnamed peers are numbered by their declared position');
  assert.equal(cells[2][4], 'no peers');
  await page.keyboard.press('Tab');
  await page.keyboard.press('Tab');
  assert.equal(await page.evaluate(() => document.activeElement.id), 'after', 'focus leaves the strip in document order');
  await axe(page, 'collapsed, light');
  await context.close();

  // ── Dark theme and forced colours ────────────────────────────────────────────────────────
  const dark = await open('dark', false);
  await dark.page.locator('#annotations ol button').nth(1).click();
  await dark.page.locator('#peers button').first().click();
  await axe(dark.page, 'details open, dark');
  await dark.context.close();

  const forced = await open('light', true);
  const paint = await forced.page.evaluate(() => {
    const read = (selector) => getComputedStyle(document.querySelector(selector)).backgroundColor;
    return { canvas: getComputedStyle(document.body).backgroundColor, peer: read('.mds-pcs__peer'), subject: read('.mds-pcs__subject'), track: read('.mds-pcs__track') };
  });
  for (const mark of ['peer', 'subject', 'track']) {
    assert.notEqual(paint[mark], paint.canvas, `forced colours: the ${mark} mark is still painted against the canvas`);
    assert.notEqual(paint[mark], 'rgba(0, 0, 0, 0)', `forced colours: the ${mark} mark is not transparent`);
  }
  await forced.page.locator('#peers button').first().focus();
  assert.equal(await forced.page.evaluate(() => getComputedStyle(document.activeElement).outlineStyle), 'solid', 'forced colours: focus ring is kept');
  await forced.context.close();

  assert.deepEqual(errors, [], 'No runtime errors');
  console.log('PASS Chart accessibility: annotation list keyboard order/detail/overlay link, peer value disclosure and table, dark theme, forced colours, axe WCAG A/AA');
} finally {
  await browser.close();
}
