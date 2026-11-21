#!/usr/bin/env node
// Existing optional CSS APIs keep their fallbacks, inheritance and local overrides.
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
    import { TreeView } from './components/core/TreeView.jsx';
    import { Stepper } from './components/core/Stepper.jsx';
    import { Datum } from './components/core/Datum.jsx';
    import { DepthLadder } from './components/trading/DepthLadder.jsx';
    import { OptionChainTable } from './components/trading/OptionChainTable.jsx';
    window.Hooks = { TreeView, Stepper, Datum, DepthLadder, OptionChainTable };
  `, resolveDir: root, loader: 'js' },
  jsxFactory: 'window.React.createElement', jsxFragment: 'window.React.Fragment',
  plugins: [{ name: 'react-peer', setup(builder) {
    builder.onResolve({ filter: /^react$/ }, () => ({ path: 'react', namespace: 'peer' }));
    builder.onLoad({ filter: /.*/, namespace: 'peer' }, () => ({ contents: 'export default window.React;', loader: 'js' }));
  } }]
});
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });
const page = await browser.newPage();
const errors = [];
page.on('pageerror', error => errors.push(error.message));

try {
  await page.setContent('<!doctype html><html lang="en"><head><style>:root{--theme-row-height:36px;--font-data:monospace;--font-body:sans-serif}</style></head><body><div id="fixture"></div></body></html>');
  await page.addScriptTag({ content: await readFile(resolve(root, '_preview_runtime.js'), 'utf8') });
  await page.addScriptTag({ content: source.outputFiles[0].text });
  await page.evaluate(() => {
    const h = React.createElement;
    const hooks = { '--tree-row-height': '48px', '--stepper-height': '49px', '--ladder-row': '30px', '--ochain-row': '31px', '--datum-lane': 2, '--datum-color': 'rgb(10, 20, 30)' };
    const direct = { '--tree-row-height': '52px', '--stepper-height': '53px', '--ladder-row': '34px', '--ochain-row': '35px', '--datum-lane': 3, '--datum-color': 'rgb(40, 50, 60)' };
    const components = style => [
      h(Hooks.TreeView, { key: 'tree', items: [{ id: 'one', label: 'One' }], style }),
      h(Hooks.Stepper, { key: 'stepper', steps: [{ label: 'Review' }], style }),
      h(Hooks.DepthLadder, { key: 'ladder', bids: [{ price: 99, size: 10 }], asks: [{ price: 101, size: 12 }], style }),
      h(Hooks.OptionChainTable, { key: 'chain', rows: [{ strike: 100, call: { bid: 1 }, put: { bid: 2 } }], columns: ['bid'], style }),
      h('div', { key: 'datum', style: { position: 'relative', height: 100 } },
        h(Hooks.Datum, { label: 'Baseline', marker: true, style })),
      h('svg', { key: 'svg', width: 100, height: 100 }, h(Hooks.Datum, { svg: true, marker: true, style }))
    ];
    ReactDOM.createRoot(document.getElementById('fixture')).render(h(React.Fragment, null,
      h('section', { id: 'default' }, ...components()),
      h('section', { id: 'density', style: { '--theme-row-height': '26px' } }, ...components()),
      h('section', { id: 'inherited', style: hooks }, ...components()),
      h('section', { id: 'direct', style: hooks }, ...components(direct))
    ));
  });
  await page.locator('#direct .mds-tree-row').waitFor();
  const snapshot = id => page.locator(`#${id}`).evaluate(section => {
    const style = selector => getComputedStyle(section.querySelector(selector));
    return {
      tree: style('.mds-tree-row').height, stepper: style('.mds-stepper-step').minHeight,
      ladder: style('.mds-ladder__row').height, chain: style('.mds-ochain__cell').height,
      lane: style('.mds-datum__label').transform,
      line: style('.mds-datum').borderLeftColor, marker: style('.mds-datum__marker').borderTopColor,
      svgLine: style('.mds-datum-svg__line').stroke
    };
  });
  const initial = await snapshot('default');
  assert.deepEqual([initial.tree, initial.stepper, initial.ladder, initial.chain], ['36px', '36px', '22px', '26px']);
  assert.equal(initial.lane, 'matrix(1, 0, 0, 1, 0, 0)');
  assert.equal(initial.line, 'rgb(170, 164, 154)');
  assert.equal(initial.marker, 'rgb(73, 108, 143)', 'Baseline marker retains its distinct selected-color fallback');
  assert.equal(initial.svgLine, 'rgb(170, 164, 154)');
  assert.deepEqual([(await snapshot('density')).tree, (await snapshot('density')).stepper], ['26px', '26px']);
  await page.locator('#density').evaluate(node => node.style.setProperty('--theme-row-height', '44px'));
  assert.deepEqual([(await snapshot('density')).tree, (await snapshot('density')).stepper], ['44px', '44px']);
  console.log('PASS component hooks: original dimensions, live nested density and distinct Datum fallbacks');

  const inherited = await snapshot('inherited');
  assert.deepEqual([inherited.tree, inherited.stepper, inherited.ladder, inherited.chain], ['48px', '49px', '30px', '31px']);
  assert.equal(inherited.lane, 'matrix(1, 0, 0, 1, 0, 42)');
  assert.equal(inherited.line, 'rgb(10, 20, 30)');
  assert.equal(inherited.marker, 'rgb(10, 20, 30)');
  assert.equal(inherited.svgLine, 'rgb(170, 164, 154)', 'Existing SVG-local role color remains the default');
  const direct = await snapshot('direct');
  assert.deepEqual([direct.tree, direct.stepper, direct.ladder, direct.chain], ['52px', '53px', '34px', '35px']);
  assert.equal(direct.lane, 'matrix(1, 0, 0, 1, 0, 63)');
  assert.equal(direct.line, 'rgb(40, 50, 60)');
  assert.equal(direct.marker, 'rgb(40, 50, 60)');
  assert.equal(direct.svgLine, 'rgb(40, 50, 60)', 'SVG branch forwards consumer style to its existing role');
  assert.deepEqual(errors, []);
  console.log('PASS component hooks: inherited and direct overrides remain effective, including HTML and SVG Datum');
} finally {
  await browser.close();
}
