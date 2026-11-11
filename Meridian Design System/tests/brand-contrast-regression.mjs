#!/usr/bin/env node
// Exercise the browser's real CSS cascade, including brand/appearance specificity.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';
import { chromium } from 'playwright';
import './conformance-rules.js';

// Scrubbing a sanctioned fallback must not manufacture a runtime var() reference
// or hide an unsanctioned literal later in the same source.
const rawHexRule = globalThis.MeridianConformance.RULES.find(rule => rule.id === 'C-07');
assert(rawHexRule);
assert.deepEqual(rawHexRule.scan('const style = { color: "var(--brand-signal, #C56343)" };'), []);
const mixedColourSource = 'const first = { color: `var(\n--brand-signal,\n#C56343\n)` };\nconst second = { color: "#123456" };';
const rawFindings = rawHexRule.scan(mixedColourSource);
assert.equal(rawFindings.length, 1, 'Raw hex outside a fallback remains detected');
assert.equal(rawFindings[0].line, 5, 'Scrubbing preserves diagnostic line offsets');

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const theme = await readFile(resolve(root, 'tokens/theme.css'), 'utf8');
const brands = [...new Set([...theme.matchAll(/html\[data-brand="([^"]+)"\]/g)].map(match => match[1]))];
assert(brands.length > 0, 'The declared brand variants must be exercised');
const styles = await Promise.all(['colors.css', 'theme.css', 'colors-dark.css'].map(name => readFile(resolve(root, 'tokens', name), 'utf8')));
const button = await readFile(resolve(root, 'components/core/Button.jsx'), 'utf8');
const buttonCss = button.match(/const css\s*=\s*`([\s\S]+?)`;/);
assert(buttonCss, 'The public Button source must expose its measured CSS');
const adapterCss = await Promise.all(['components/data/DenseDataTable.jsx', 'components/data/ColumnChooser.jsx'].map(async relative => {
  const source = await readFile(resolve(root, relative), 'utf8');
  const css = source.match(/const css\s*=\s*`([\s\S]+?)`;/);
  assert(css, `${relative} source CSS must be measured`);
  return css[1];
}));
const breadcrumb = await build({
  absWorkingDir: root, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2020',
  stdin: { contents: `import { Breadcrumb } from './components/core/Breadcrumb.jsx'; window.SourceBreadcrumb = Breadcrumb;`, resolveDir: root, loader: 'js' },
  jsxFactory: 'window.React.createElement', jsxFragment: 'window.React.Fragment',
  plugins: [{ name: 'react-peer', setup(builder) {
    builder.onResolve({ filter: /^react$/ }, () => ({ path: 'react', namespace: 'peer' }));
    builder.onLoad({ filter: /.*/, namespace: 'peer' }, () => ({ contents: 'export default window.React;', loader: 'js' }));
  } }]
});
const roles = ['severity-blocked', 'severity-action', 'severity-review', 'severity-ready', 'severity-info',
  'state-healthy', 'state-warn', 'state-danger', 'state-paper', 'state-strategy', 'state-live', 'state-muted', 'state-pending'];
const surfaces = ['--bg', '--bg-light', '--bg-medium', '--card-surface-raised'];
const inkSurfaces = [...surfaces, '--bg-active', '--accent-ghost', '--blue-a10'];
const browser = await chromium.launch({ headless: true, ...(process.env.MERIDIAN_CHROMIUM_PATH ? { executablePath: process.env.MERIDIAN_CHROMIUM_PATH } : {}) });
const errors = [];
try {
  const page = await browser.newPage();
  page.on('pageerror', error => errors.push(error.message));
  await page.setContent('<main><button class="mds-btn mds-btn--primary">Primary action</button><div id="breadcrumb"></div><div style="background:var(--bg-medium)"><span class="dds__rank" data-source-text>1</span><button class="mds-colchz__reset" data-source-text>Reset columns</button></div><div id="chips"></div></main>');
  await page.addStyleTag({ content: styles.join('\n') + '\n' + buttonCss[1] + '\n' + adapterCss.join('\n') + '\nbody{background:var(--bg-light)}' });
  await page.addScriptTag({ path: resolve(root, '_preview_runtime.js') });
  await page.addScriptTag({ content: breadcrumb.outputFiles[0].text });
  await page.evaluate(() => {
    window.contrastActions = 0;
    ReactDOM.createRoot(document.getElementById('breadcrumb')).render(React.createElement(window.SourceBreadcrumb, {
      items: [{ label: 'Earlier record', onClick() { window.contrastActions += 1; } }, { label: 'Current record' }]
    }));
  });
  await page.getByRole('button', { name: 'Earlier record', exact: true }).waitFor();
  await page.getByRole('button', { name: 'Earlier record', exact: true }).evaluate(node => { node.dataset.sourceText = ''; });
  await page.evaluate(({ roles, surfaces, inkSurfaces }) => {
    for (const role of roles) for (const surface of surfaces) {
      const backdrop = document.createElement('div');
      backdrop.style.background = `var(${surface})`;
      const chip = document.createElement('span');
      chip.textContent = role;
      chip.dataset.role = role;
      chip.dataset.surface = surface;
      chip.style.cssText = `color:var(--${role}-fg);background:var(--${role}-bg)`;
      backdrop.append(chip);
      document.getElementById('chips').append(backdrop);
    }
    for (const surface of inkSurfaces) for (const underlay of surface === '--blue-a10' ? surfaces : ['--bg-light']) {
      const backdrop = document.createElement('div'); backdrop.style.background = `var(${underlay})`;
      const text = document.createElement('span'); text.textContent = 'Signal reference'; text.dataset.ink = `${surface} over ${underlay}`;
      text.style.cssText = `color:var(--brand-signal-ink);background:var(${surface})`;
      backdrop.append(text); document.getElementById('chips').append(backdrop);
    }
  }, { roles, surfaces, inkSurfaces });

  const records = [];
  for (const brand of [null, ...brands]) {
    const brandRecords = [];
    for (const [mode, colorScheme, appearance] of [['light', 'light', null], ['auto dark', 'dark', null],
      ['manual dark', 'light', 'dark'], ['forced light', 'dark', 'light']]) {
      await page.emulateMedia({ colorScheme });
      await page.evaluate(({ brand, appearance }) => {
        const html = document.documentElement;
        if (brand) html.dataset.brand = brand; else delete html.dataset.brand;
        if (appearance) html.dataset.theme = appearance; else delete html.dataset.theme;
      }, { brand, appearance });
      const measured = await page.evaluate(() => {
        const canvas = document.createElement('canvas'); canvas.width = canvas.height = 1;
        const context = canvas.getContext('2d', { willReadFrequently: true });
        function rgb(color, backdrop) {
          context.clearRect(0, 0, 1, 1);
          if (backdrop) { context.fillStyle = backdrop; context.fillRect(0, 0, 1, 1); }
          context.fillStyle = color; context.fillRect(0, 0, 1, 1);
          const measured = [...context.getImageData(0, 0, 1, 1).data];
          if (measured[3] !== 255) throw new Error(`Text/fill did not resolve over an opaque surface: ${color}`);
          return measured.slice(0, 3);
        }
        function luminance(values) {
          const linear = values.map(value => { const channel = value / 255; return channel <= .04045 ? channel / 12.92 : ((channel + .055) / 1.055) ** 2.4; });
          return .2126 * linear[0] + .7152 * linear[1] + .0722 * linear[2];
        }
        function ratio(foreground, background) {
          const values = [luminance(foreground), luminance(background)].sort((a, b) => b - a);
          return (values[0] + .05) / (values[1] + .05);
        }
        function backgroundOf(element) {
          const layers = []; for (let node = element; node; node = node.parentElement) layers.unshift(getComputedStyle(node).backgroundColor);
          context.clearRect(0, 0, 1, 1);
          for (const layer of layers) { context.fillStyle = layer; context.fillRect(0, 0, 1, 1); }
          const measured = [...context.getImageData(0, 0, 1, 1).data];
          if (measured[3] !== 255) throw new Error('The source text backdrop must resolve to an opaque surface');
          return measured.slice(0, 3);
        }
        const signals = ['--accent', '--accent-hover', '--accent-dim'].map(token => {
          const sample = document.createElement('span'); sample.style.cssText = `color:var(--text-on-accent);background:var(${token})`;
          document.body.append(sample); const style = getComputedStyle(sample);
          const fill = rgb(style.backgroundColor), contrast = ratio(rgb(style.color), fill); sample.remove();
          return { token, fill, contrast };
        });
        const chips = [...document.querySelectorAll('[data-role]')].map(chip => {
          const style = getComputedStyle(chip), backdrop = getComputedStyle(chip.parentElement).backgroundColor;
          return { role: chip.dataset.role, surface: chip.dataset.surface, contrast: ratio(rgb(style.color), rgb(style.backgroundColor, backdrop)) };
        });
        const primary = getComputedStyle(document.querySelector('.mds-btn--primary'));
        const ink = [...document.querySelectorAll('[data-ink]')].map(text => {
          const style = getComputedStyle(text), backdrop = getComputedStyle(text.parentElement).backgroundColor;
          return { surface: text.dataset.ink, contrast: ratio(rgb(style.color), rgb(style.backgroundColor, backdrop)) };
        });
        const sourceText = [...document.querySelectorAll('[data-source-text]')].map(text => {
          const style = getComputedStyle(text), backdrop = backgroundOf(text), opacity = Number(style.opacity);
          const foreground = rgb(style.color).map((channel, index) => channel * opacity + backdrop[index] * (1 - opacity));
          return { label: text.textContent, contrast: ratio(foreground, backdrop) };
        });
        return { signals, chips, ink, sourceText, primaryContrast: ratio(rgb(primary.color), rgb(primary.backgroundColor)) };
      });
      for (const signal of measured.signals) assert(signal.contrast >= 4.5, `${brand ?? 'default'} ${mode} ${signal.token}: ${signal.contrast.toFixed(2)}:1`);
      for (const chip of measured.chips) assert(chip.contrast >= 4.5, `${brand ?? 'default'} ${mode} ${chip.role} on ${chip.surface}: ${chip.contrast.toFixed(2)}:1`);
      for (const ink of measured.ink) assert(ink.contrast >= 4.5, `${brand ?? 'default'} ${mode} signal ink on ${ink.surface}: ${ink.contrast.toFixed(2)}:1`);
      for (const text of measured.sourceText) assert(text.contrast >= 4.5, `${brand ?? 'default'} ${mode} source ${text.label}: ${text.contrast.toFixed(2)}:1`);
      assert(measured.primaryContrast >= 4.5, `${brand ?? 'default'} ${mode} primary action contrast`);
      brandRecords.push({ brand: brand ?? 'default', mode, ...measured });
    }
    assert.deepEqual(brandRecords[3].signals.map(signal => signal.fill), brandRecords[0].signals.map(signal => signal.fill), `${brand ?? 'default'} forced light preserves its declared light identity`);
    assert.deepEqual(brandRecords[1].signals.map(signal => signal.fill), brandRecords[2].signals.map(signal => signal.fill), `${brand ?? 'default'} OS/manual dark signal palettes agree`);
    records.push(...brandRecords);
  }
  for (const appearance of ['light', 'dark']) {
    await page.evaluate(appearance => { delete document.documentElement.dataset.brand; document.documentElement.dataset.theme = appearance; }, appearance);
    for (const label of ['Earlier record', 'Reset columns']) {
      const control = page.getByRole('button', { name: label, exact: true }); await control.hover();
      const hovered = await control.evaluate(node => ({ opacity: getComputedStyle(node).opacity, decoration: getComputedStyle(node).textDecorationLine }));
      assert.equal(hovered.opacity, '1', `${appearance} ${label} must not fade readable text on hover`);
      assert(hovered.decoration.includes('underline'), `${appearance} ${label} retains visible hover feedback`);
      await page.mouse.move(0, 0);
    }
  }
  assert.equal(await page.evaluate(() => window.contrastActions), 0, 'Hover verification must not dispatch an action');
  assert.deepEqual(errors, []);
  const pairs = records.reduce((count, record) => count + record.chips.length + record.ink.length + record.sourceText.length + record.signals.length + 1, 0);
  console.log(`PASS brand/appearance contrast: ${records.length} browser states, ${pairs} text pairs, ${brands.length} brand identities preserved in forced light; retained table and source control hover remain readable`);
} finally {
  await browser.close();
}
