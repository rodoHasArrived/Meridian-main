import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { existsSync } from 'node:fs';
import { mkdir, readFile } from 'node:fs/promises';
import { dirname, extname, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { cardGroups, normalizeCards } from './gallery.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const specimen = '<!doctype html><html lang="en"><head><title>Local specimen fixture</title></head><body><main><h1>Local specimen fixture</h1><p>This synthetic document exercises gallery loading, not component conformance.</p></main></body></html>';

export async function verifyGallery({ browser, base, evidence }) {
  const manifest = JSON.parse(await readFile(resolve(root, '_ds_manifest.json'), 'utf8'));
  const cards = normalizeCards(manifest);
  const [first, second] = cards;
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce' });
  const runtimeErrors = [];
  page.on('pageerror', error => runtimeErrors.push(error.message));
  try {
    await page.goto(`${base}/tests/gallery.html`);
    await page.getByText(`${cards.length} of ${cards.length} cards`, { exact: true }).waitFor();
    assert.equal(await page.locator('.gallery-card').count(), cards.length, 'All supported manifest cards appear');
    assert.equal(await page.locator('iframe').count(), 0, 'Initial browsing creates no previews');
    const search = page.getByRole('searchbox', { name: 'Search cards', exact: true });
    const group = page.getByRole('combobox', { name: 'Group', exact: true });
    const reset = page.getByRole('button', { name: 'Reset filters', exact: true });
    await search.fill(first.path);
    assert.equal(await page.locator('.gallery-card').count(), 1, 'Search includes the card path');
    assert(await search.evaluate(node => node === document.activeElement), 'Filtering preserves input focus');
    const otherGroup = cardGroups(cards).find(([name]) => name !== first.group)[0];
    await group.selectOption(otherGroup);
    assert(await page.getByText('No matching cards.', { exact: false }).isVisible(), 'Combined filters expose a no-results state');
    await reset.click();
    assert.equal(await page.locator('.gallery-card').count(), cards.length);
    assert(await reset.evaluate(node => node === document.activeElement), 'Reset keeps focus on its control');
    await group.selectOption(first.group);
    assert.equal(await page.locator('.gallery-card').count(), cardGroups(cards).find(([name]) => name === first.group)[1]);
    await reset.click();
    const firstCard = page.locator(`.gallery-card[data-path="${first.path}"]`);
    const secondCard = page.locator(`.gallery-card[data-path="${second.path}"]`);
    await page.route(`${base}/${first.path}`, route => route.fulfill({ contentType: 'text/html', body: specimen }));
    await firstCard.getByRole('button', { name: `Show preview: ${first.name}`, exact: true }).click();
    await firstCard.getByText('Document loaded.', { exact: false }).waitFor();
    assert.equal(await page.locator('iframe').count(), 1);
    assert.equal(await page.locator('iframe').getAttribute('width'), String(first.width));
    assert.equal(await page.locator('iframe').getAttribute('height'), String(first.height));
    await page.frameLocator('iframe').getByRole('heading', { name: 'Local specimen fixture' }).waitFor();
    await page.route(`${base}/${second.path}`, route => route.fulfill({ status: 503, contentType: 'text/plain', body: 'Synthetic unavailable specimen' }));
    await secondCard.getByRole('button', { name: `Show preview: ${second.name}`, exact: true }).click();
    await secondCard.getByText('Preview unavailable: HTTP 503.', { exact: false }).waitFor();
    assert.equal(await page.locator('iframe').count(), 0, 'Switching preview removes the previous frame, including when the next request fails');
    await page.unroute(`${base}/${second.path}`);
    await page.route(`${base}/${second.path}`, route => route.fulfill({ contentType: 'text/html', body: specimen }));
    await secondCard.getByRole('button', { name: 'Retry preview', exact: true }).click();
    await secondCard.getByText('Document loaded.', { exact: false }).waitFor();
    assert.equal(await page.locator('iframe').count(), 1);
    assert(await secondCard.getByRole('button', { name: `Hide preview: ${second.name}`, exact: true }).evaluate(node => node === document.activeElement), 'Retry restores focus to the preview control');
    await page.clock.install();
    await page.evaluate(path => {
      window.galleryOriginalFetch = window.fetch;
      window.fetch = (url, options) => String(url).endsWith(path) ? new Promise(() => {}) : window.galleryOriginalFetch(url, options);
    }, second.path);
    await secondCard.getByRole('button', { name: `Hide preview: ${second.name}`, exact: true }).click();
    await secondCard.getByRole('button', { name: `Show preview: ${second.name}`, exact: true }).click();
    await page.clock.fastForward(16000);
    await secondCard.getByText('did not load within 15 seconds.', { exact: false }).waitFor();
    assert.equal(await page.locator('iframe').count(), 0, 'A timed-out document has a visible retry state');
    await page.evaluate(() => { window.fetch = window.galleryOriginalFetch; });
    await search.fill(first.path);
    assert.equal(await page.locator('iframe').count(), 0, 'Changing filters releases the active document');
    await reset.click();
    await search.focus();
    await page.keyboard.press('Tab');
    assert(await group.evaluate(node => node === document.activeElement), 'Filter controls have a predictable keyboard order');
    const focus = await group.evaluate(node => ({ style: getComputedStyle(node).outlineStyle, width: getComputedStyle(node).outlineWidth }));
    assert.notEqual(focus.style, 'none');
    assert.equal(focus.width, '2px');
    await page.addScriptTag({ content: await readFile(resolve(root, 'node_modules/axe-core/axe.min.js'), 'utf8') });
    const accessibility = await page.evaluate(async () => await axe.run(document, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] }
    }));
    assert.deepEqual(accessibility.violations.map(item => ({ id: item.id, nodes: item.nodes.map(node => node.target) })), [], 'Gallery accessibility violations');
    await mkdir(evidence, { recursive: true });
    for (const [width, height] of [[1440, 900], [1366, 768]]) {
      await page.setViewportSize({ width, height });
      assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${width}: no page-level overflow`);
      assert.match(await page.locator('body').evaluate(node => getComputedStyle(node).fontFamily), /IBM Plex Sans/);
      await page.evaluate(() => document.fonts.ready);
      assert(await page.evaluate(() => document.fonts.check('14px "IBM Plex Sans"')), 'Self-hosted interface font is available');
      await page.screenshot({ path: `${evidence}/gallery-${width}x${height}.png`, fullPage: true });
      await page.screenshot({ path: `${evidence}/gallery-${width}x${height}-viewport.png` });
    }
    await page.route(`${base}/_ds_manifest.json`, route => route.fulfill({ status: 503, body: 'Synthetic manifest failure' }));
    await page.reload();
    await page.getByRole('alert').getByText('HTTP 503.', { exact: false }).waitFor();
    assert.equal(await page.locator('.gallery-card').count(), 0, 'A failed manifest never falls back to stale inventory');
    await page.unroute(`${base}/_ds_manifest.json`);
    await page.getByRole('button', { name: 'Retry catalogue', exact: true }).click();
    await page.getByText(`${cards.length} of ${cards.length} cards`, { exact: true }).waitFor();
    assert(await search.evaluate(node => node === document.activeElement), 'Catalogue recovery places focus on the search control');
    const literal = '<img src=x onerror="window.galleryInjected=true">';
    await page.route(`${base}/_ds_manifest.json`, route => route.fulfill({ json: { cards: [{ ...manifest.cards[0], name: literal, subtitle: '<script>not executable</script>' }] } }));
    await page.reload();
    await page.getByRole('heading', { name: literal, exact: true }).waitFor();
    assert.equal(await page.locator('img[src="x"]').count(), 0);
    assert.equal(await page.evaluate(() => window.galleryInjected), undefined, 'Manifest strings remain literal text');
    assert.equal(await page.locator('iframe').count(), 0);
    await page.unroute(`${base}/_ds_manifest.json`);
    await page.route(`${base}/_ds_manifest.json`, route => route.fulfill({ json: { cards: [{ ...manifest.cards[0], path: '../outside.card.html' }] } }));
    await page.reload();
    await page.getByRole('alert').getByText('Unsafe card path:', { exact: false }).waitFor();
    assert.equal(await page.locator('iframe').count(), 0);
    assert.deepEqual(runtimeErrors, [], 'Gallery journeys have no uncaught runtime errors');
    console.log(JSON.stringify({ result: 'PASS', galleryCards: cards.length, groups: cardGroups(cards).length, lazyPreviewMaximum: 1, manifestRecovery: true, previewRecovery: true, safeMetadata: true, desktopViewports: 2 }));
  } finally { await page.close(); }
}

async function servePackage() {
  const types = { '.html': 'text/html', '.mjs': 'text/javascript', '.js': 'text/javascript', '.json': 'application/json', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.ttf': 'font/ttf', '.txt': 'text/plain' };
  const server = createServer(async (request, response) => {
    try {
      const path = resolve(root, `.${decodeURIComponent(new URL(request.url, 'http://localhost').pathname)}`);
      if (!path.startsWith(root + sep)) throw new Error('Outside package');
      response.setHeader('Content-Type', types[extname(path)] || 'application/octet-stream');
      response.end(await readFile(path));
    } catch { response.statusCode = 404; response.end('Not found'); }
  });
  await new Promise(resolveReady => server.listen(0, '127.0.0.1', resolveReady));
  return { base: `http://127.0.0.1:${server.address().port}`, close: () => new Promise(resolveClosed => server.close(resolveClosed)) };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const server = process.env.MERIDIAN_PREVIEW_URL ? null : await servePackage();
  const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH || (existsSync('/usr/bin/chromium') ? '/usr/bin/chromium' : undefined);
  const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), headless: true, args: ['--no-sandbox'] });
  try {
    await verifyGallery({ browser, base: server?.base || process.env.MERIDIAN_PREVIEW_URL, evidence: process.env.MERIDIAN_QA_OUTPUT || '/tmp/meridian-gallery-qa' });
  } finally { await browser.close(); await server?.close(); }
}
