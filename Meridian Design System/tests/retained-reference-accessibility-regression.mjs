#!/usr/bin/env node
// Exercise the retained JSX reference against the current package runtime.
// The published DC workflow has its own template journeys; this protects the
// still-shipped screen module's keyboard behavior when it is copied or imported.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { dirname, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { transform } from 'esbuild';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const sourcePath = process.env.MERIDIAN_REFERENCE_SOURCE || resolve(root, 'templates/field-formula/screen.jsx');
const source = (await transform(await readFile(sourcePath, 'utf8'), {
  loader: 'jsx', format: 'iife', jsxFactory: 'React.createElement', jsxFragment: 'React.Fragment',
})).code;
const runtime = await readFile(resolve(root, '_preview_runtime.js'), 'utf8');
const bundle = await readFile(resolve(root, '_ds_bundle.js'), 'utf8');
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.ttf': 'font/ttf', '.woff2': 'font/woff2' };

try {
  for (const theme of ['light', 'dark']) {
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, colorScheme: theme });
    try {
      const page = await context.newPage();
      const errors = [];
      page.on('pageerror', error => errors.push(error.message));
      page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
      await page.route('http://meridian.test/**', async route => {
        const url = new URL(route.request().url());
        if (url.pathname.endsWith('/retained-reference.html')) return route.fulfill({ contentType: 'text/html', body: `<!doctype html><html lang="en" data-theme="${theme}"><head><title>Retained formula reference</title><link rel="icon" href="data:,"><link rel="stylesheet" href="/styles.css"></head><body><div id="fixture"></div></body></html>` });
        try {
          const path = resolve(root, '.' + decodeURIComponent(url.pathname));
          if (!path.startsWith(root + '/')) return route.abort();
          return route.fulfill({ body: await readFile(path), contentType: mime[extname(path)] || 'application/octet-stream' });
        } catch (error) {
          errors.push(`Missing package resource: ${url.pathname}: ${error.message}`);
          return route.fulfill({ status: 404, body: 'Missing package resource' });
        }
      });
      await page.goto('http://meridian.test/templates/field-formula/retained-reference.html');
      for (const content of [runtime, bundle, source]) await page.addScriptTag({ content });
      await page.evaluate(() => ReactDOM.createRoot(document.getElementById('fixture')).render(React.createElement(window.FieldFormulaScreen)));
      const group = page.getByRole('group', { name: 'Formula suggestions' });
      await group.waitFor();
      const buttons = group.getByRole('button');
      assert.equal(await buttons.count(), 5, 'Every suggestion is a native button');
      assert.equal(await group.locator('[tabindex="0"]').count(), 1, 'The suggestions have one Tab stop');
      const selected = async index => {
        assert.equal(await buttons.nth(index).getAttribute('aria-pressed'), 'true');
        assert(await buttons.nth(index).evaluate(node => node === document.activeElement), 'Focus moves with selection');
        assert.equal(await group.locator('[tabindex="0"]').count(), 1);
      };
      await buttons.nth(2).focus();
      for (const [key, index] of [['ArrowDown', 3], ['End', 4], ['ArrowDown', 0], ['ArrowUp', 4], ['Home', 0], ['ArrowDown', 1]]) {
        await page.keyboard.press(key);
        await selected(index);
      }
      assert(await page.getByText('Use clean_price to avoid double-counting accrued interest.', { exact: true }).isVisible(), 'Keyboard selection updates the related explanation');
      const outline = await buttons.nth(1).evaluate(node => ({ style: getComputedStyle(node).outlineStyle, width: getComputedStyle(node).outlineWidth }));
      assert.deepEqual(outline, { style: 'solid', width: '2px' }, 'Keyboard focus is visible');
      await buttons.nth(3).focus();
      await page.keyboard.press('Space');
      await selected(3);
      assert(await page.getByText('Newton-Raphson solver over all coupons + principal.', { exact: true }).isVisible(), 'Space activates a suggestion');
      await buttons.nth(0).focus();
      await page.keyboard.press('Enter');
      await selected(0);
      await page.keyboard.press('Tab');
      assert.equal(await group.evaluate(node => node.contains(document.activeElement)), false, 'Tab leaves the suggestion group');
      await page.keyboard.press('Shift+Tab');
      await selected(0);
      assert.deepEqual(errors, [], 'Retained source renders without browser errors');
      console.log(`PASS retained formula suggestions: ${theme}`);
      for (const card of ['depth-surfaces', 'dark-mode-validation']) {
        await page.goto(`http://meridian.test/guidelines/${card}.card.html`);
        await page.waitForFunction(() => document.querySelectorAll('iframe').length === 2 && [...document.querySelectorAll('iframe')].every(frame => frame.contentDocument?.readyState === 'complete'));
        const scopes = await page.locator('iframe').evaluateAll(frames => frames.map(frame => ({
          theme: frame.contentDocument.documentElement.dataset.theme,
          background: getComputedStyle(frame.contentDocument.body).backgroundColor,
          nested: frame.contentDocument.querySelectorAll('iframe').length,
        })));
        assert.deepEqual(scopes.map(scope => scope.theme), ['light', 'dark']);
        assert.notEqual(scopes[0].background, scopes[1].background, 'The two columns render distinct canonical themes');
        assert(scopes.every(scope => scope.nested === 0), 'Theme specimens do not recursively create frames');
        await page.waitForTimeout(80);
        const heights = await page.locator('iframe').evaluateAll(frames => frames.map(frame => frame.offsetHeight));
        await page.waitForTimeout(120);
        assert.deepEqual(await page.locator('iframe').evaluateAll(frames => frames.map(frame => frame.offsetHeight)), heights, 'Theme frame sizing stabilizes');
        assert.deepEqual(errors, [], 'Theme specimens load their actual local assets');
        console.log(`PASS independent theme specimens: ${card} (${theme})`);
      }
    } finally { await context.close(); }
  }
} finally { await browser.close(); }
