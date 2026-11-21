#!/usr/bin/env node
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const source = await build({
  absWorkingDir: root, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2020',
  stdin: { contents: `import { GlobalSearch } from './components/shell/GlobalSearch.jsx'; window.SearchUnderTest = GlobalSearch;`, resolveDir: root, loader: 'js' },
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
  await page.setContent(`<!doctype html><html lang="en"><head><title>Global search focus</title><style>
    :root{--interaction-focus:#2D6FB3;--focus-ring:2px solid var(--interaction-focus);--bg-light:#F8F7F3;--text-primary:#20252B}
    :root[data-theme="dark"]{--interaction-focus:#5C9FE5;--bg-light:#171E25;--text-primary:#F1EFEA}
  </style></head><body><button id="before" type="button">Before search</button><div id="fixture"></div></body></html>`);
  await page.addScriptTag({ content: await readFile(resolve(root, '_preview_runtime.js'), 'utf8') });
  await page.addScriptTag({ content: source.outputFiles[0].text });
  await page.evaluate(() => {
    window.actions = 0;
    window.closes = 0;
    ReactDOM.createRoot(document.getElementById('fixture')).render(React.createElement(window.SearchUnderTest, {
      open: true, scopeLabel: 'Synthetic fixtures', onClose: () => { window.closes++; },
      destinations: [{ id: 'synthetic-record', label: 'Synthetic record', run: () => { window.actions++; } }]
    }));
  });
  const input = page.getByRole('textbox', { name: 'Search', exact: true });
  await input.waitFor();
  for (const [theme, expectedColor] of [['light', 'rgb(45, 111, 179)'], ['dark', 'rgb(92, 159, 229)']]) {
    await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
    await page.locator('#before').focus();
    const unfocused = await page.locator('.mds-gsearch__field').evaluate(node => getComputedStyle(node).outlineStyle);
    assert.equal(unfocused, 'none');
    await page.keyboard.press('Tab');
    assert(await input.evaluate(node => node === document.activeElement && node.matches(':focus-visible')));
    const focused = await page.locator('.mds-gsearch__field').evaluate(node => {
      const style = getComputedStyle(node);
      return { outlineStyle: style.outlineStyle, outlineWidth: style.outlineWidth, outlineColor: style.outlineColor, outlineOffset: style.outlineOffset };
    });
    assert.deepEqual(focused, { outlineStyle: 'solid', outlineWidth: '2px', outlineColor: expectedColor, outlineOffset: '-2px' });
    await input.fill('Synthetic');
    assert.equal(await input.inputValue(), 'Synthetic');
    assert.equal(await page.evaluate(() => window.actions), 0, 'Focusing/typing must not execute a result action');
    assert.equal(await page.evaluate(() => window.closes), 0);
  }
  assert.deepEqual(errors, []);
  console.log('PASS GlobalSearch: visible keyboard focus in light/dark without executing search actions');
} finally {
  await browser.close();
}
