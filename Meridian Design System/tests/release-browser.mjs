import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { chromium } from 'playwright';

const base = process.env.MERIDIAN_PREVIEW_URL || 'http://127.0.0.1:8766';
const evidence = process.env.MERIDIAN_QA_OUTPUT || '/tmp/meridian-pr3501-qa';
await mkdir(evidence, { recursive: true });
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_EXECUTABLE_PATH || '/usr/bin/chromium', headless: true, args: ['--no-sandbox'] });
const errors = [], remote = [];
try {
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce' });
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
  page.on('requestfailed', request => errors.push(`Failed: ${request.url()}`));
  page.on('response', response => { if (response.status() >= 400) errors.push(`HTTP ${response.status()}: ${response.url()}`); });
  await page.route('**/*', route => {
    if (new URL(route.request().url()).origin !== new URL(base).origin) { remote.push(route.request().url()); return route.abort(); }
    return route.continue();
  });
  await page.goto(`${base}/preview/release-readiness.html`);
  await page.getByRole('heading', { name: 'Evidence register', exact: true }).waitFor();
  assert.match(await page.title(), /4\.3\.0/);
  const condition = page.getByLabel('Data condition', { exact: true });
  const options = await condition.locator('option').evaluateAll(items => items.map(item => item.value));
  assert.equal(options.length, 11);
  for (const value of options) {
    await condition.selectOption(value);
    assert.equal(await page.locator('.mds-grid tbody tr').count(), 3);
    if (['restricted', 'missing', 'na'].includes(value)) {
      assert.equal(await page.getByText('125,000.00', { exact: true }).count(), 0, `${value} must not expose an amount`);
    }
  }
  await condition.selectOption('confirmed');
  const compact = page.getByRole('switch', { name: 'Compact rows' });
  await compact.focus();
  await page.keyboard.press('Space');
  assert.equal(await compact.isChecked(), true);
  await page.getByText('Only review required', { exact: true }).click();
  assert.equal(await page.locator('.mds-grid tbody tr').count(), 1);
  await page.getByRole('button', { name: 'Reset fixture', exact: true }).click();
  assert.equal(await page.locator('.mds-grid tbody tr').count(), 3);
  await page.getByRole('button', { name: 'Inspect evidence', exact: true }).click();
  assert.equal(await page.getByRole('heading', { name: 'Source history' }).isVisible(), true);
  await condition.selectOption('restricted');
  assert.equal(await page.getByRole('button', { name: 'Inspect evidence', exact: true }).isDisabled(), true);
  assert.equal(await page.getByRole('heading', { name: 'Source history' }).count(), 0);
  await condition.selectOption('confirmed');
  await page.getByText('Order validation fixture', { exact: true }).click();
  const quantity = page.locator('.mds-ticket__num input').first();
  await quantity.fill('10abc');
  const submit = page.getByRole('button', { name: 'Submit buy order', exact: true });
  assert.equal(await submit.isDisabled(), true);
  await quantity.fill('10');
  const confirm = page.locator('.mds-ticket__gate input');
  assert.equal(await submit.isDisabled(), true);
  await confirm.check();
  assert.equal(await submit.isEnabled(), true);
  await quantity.fill('11');
  assert.equal(await confirm.isChecked(), false);
  assert.equal(await submit.isDisabled(), true);
  await confirm.check();
  await submit.click();
  assert.equal(await page.locator('[data-order-result]').textContent(), 'Simulated buy 11 SYN; no order sent.');
  assert.equal(await confirm.isChecked(), false);
  await page.getByText('Order validation fixture', { exact: true }).click();
  await condition.selectOption('stale');
  await page.addScriptTag({ content: await readFile(new URL('../node_modules/axe-core/axe.min.js', import.meta.url), 'utf8') });
  const accessibility = [];
  for (const theme of ['light', 'dark']) {
    await page.getByLabel('Theme', { exact: true }).selectOption(theme);
    for (const value of options) {
      await condition.selectOption(value);
      const result = await page.evaluate(async () => await axe.run(document, {
        runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] }
      }));
      accessibility.push({ theme, condition: value, passes: result.passes.length,
        incomplete: result.incomplete.map(item => item.id),
        violations: result.violations.map(item => ({ id: item.id, nodes: item.nodes.map(node => node.target) })) });
    }
    await condition.selectOption('stale');
    for (const [width, height] of [[1920, 1080], [1440, 900], [1366, 768], [1280, 720]]) {
      await page.setViewportSize({ width, height });
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, `${theme} ${width}: horizontal overflow`);
      await page.screenshot({ path: `${evidence}/${theme}-${width}x${height}.png`, fullPage: true });
    }
  }
  await page.getByLabel('Theme', { exact: true }).selectOption('light');
  const grid = page.getByRole('region', { name: 'Synthetic evidence records' });
  await grid.focus();
  assert.equal(await grid.evaluate(element => element === document.activeElement), true, 'Scrollable table receives keyboard focus');
  const needsScroll = await grid.evaluate(element => element.scrollWidth > element.clientWidth);
  if (needsScroll) {
    await page.keyboard.press('ArrowRight');
    await page.waitForFunction(() => document.querySelector('.mds-grid').scrollLeft > 0);
  }
  await page.setViewportSize({ width: 1366, height: 768 });
  await page.evaluate(() => { document.body.style.zoom = '2'; });
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, '200% zoom overflow');
  await page.screenshot({ path: `${evidence}/light-1366-zoom200.png`, fullPage: true });
  assert.deepEqual(remote, [], 'Remote dependencies requested');
  assert.deepEqual(errors, [], 'Browser errors');
  await writeFile(`${evidence}/axe.json`, JSON.stringify(accessibility, null, 2));
  assert.deepEqual(accessibility.filter(item => item.violations.length), [], 'Automated accessibility violations');
  console.log(JSON.stringify({ result: 'PASS', namespace: 'MeridianDesignSystem_205d17', conditions: options.length, themes: 2, viewports: 4, zoom: '200%', remoteRequests: remote.length, runtimeErrors: errors.length, accessibilityStates: accessibility.length, screenshots: evidence }));
} finally { await browser.close(); }
