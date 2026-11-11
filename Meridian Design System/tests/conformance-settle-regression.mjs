#!/usr/bin/env node
// Exercise the real iframe lifecycle with delayed, broken and never-settled fonts.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const html = await readFile(resolve(root, 'tests/conformance.html'), 'utf8');
const start = html.indexOf('function assertCard(card) {');
const end = html.indexOf('$("#runCards")', start);
assert(start >= 0 && end > start, 'Use the shipped iframe assertion helper');
const source = html.slice(start, end);
const font = await readFile(resolve(root, 'assets/fonts/IBMPlexSans-Regular.ttf'));
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });
const page = await browser.newPage();
const card = (family, url) => `<!doctype html><html lang="en"><head><style>
  @font-face{font-family:${family};src:url("${url}") format("truetype")}
  body{font-family:${family}}
</style></head><body><p>Meaningful specimen text waits for its real local font metrics.</p>
<script>document.fonts.load('16px ${family}').catch(() => undefined);</script></body></html>`;
let releaseFont;
let fontRequested;

try {
  await page.route('http://meridian.test/**', route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><html><body><div id="stage"></div></body></html>' }));
  await page.goto('http://meridian.test/tests/settle-fixture.html');
  await page.addScriptTag({ content: `
    window.measurements = [];
    window.CARD_ASSERTIONS = [{ id: 'font-measure', run(doc) {
      window.measurements.push({ status: doc.fonts.status, faces: [...doc.fonts].map(face => ({ family: face.family, status: face.status })) });
      return null;
    } }];
    ${source}
  ` });
  await page.route('**/delayed-card.html', route => route.fulfill({ contentType: 'text/html', body: card('DelayedFace', '/delayed.ttf') }));
  const requested = new Promise(resolve => { fontRequested = resolve; });
  const released = new Promise(resolve => { releaseFont = resolve; });
  await page.route('**/delayed.ttf', async route => {
    fontRequested();
    await released;
    await route.fulfill({ contentType: 'font/ttf', body: font }).catch(() => undefined);
  });
  const pending = page.evaluate(async () => {
    window.cardResult = await assertCard({ path: 'delayed-card.html', name: 'Delayed face' });
    return window.cardResult;
  });
  await requested;
  await page.waitForTimeout(1500);
  assert.equal(await page.evaluate(() => window.cardResult), undefined, 'No result or cleanup before the used font settles');
  assert.equal(await page.locator('#stage iframe').count(), 1);
  assert.equal(await page.evaluate(() => window.measurements.length), 0, 'No geometry assertion before font readiness');
  releaseFont();
  const loaded = await pending;
  assert.equal(loaded.ok, true);
  assert.equal(await page.locator('#stage iframe').count(), 0);
  const measured = await page.evaluate(() => window.measurements[0]);
  assert.equal(measured.status, 'loaded');
  assert(measured.faces.some(face => face.family === 'DelayedFace' && face.status === 'loaded'));
  console.log('PASS specimen settle: used font loads before assertions and iframe removal');

  await page.route('**/broken-card.html', route => route.fulfill({ contentType: 'text/html', body: card('BrokenFace', '/broken.ttf') }));
  await page.route('**/broken.ttf', route => route.fulfill({ contentType: 'font/ttf', body: Buffer.from('not a TrueType font') }));
  const broken = await page.evaluate(() => assertCard({ path: 'broken-card.html', name: 'Broken face' }));
  assert.equal(broken.ok, false);
  assert(broken.findings.some(finding => finding.id === 'fonts' && finding.note.includes('BrokenFace')), 'Failed faces remain explicit failures');
  console.log('PASS specimen settle: failed font faces are reported');

  let releaseNever;
  const neverReleased = new Promise(resolve => { releaseNever = resolve; });
  await page.route('**/never-card.html', route => route.fulfill({ contentType: 'text/html', body: card('NeverFace', '/never.ttf') }));
  await page.route('**/never.ttf', async route => {
    await neverReleased;
    await route.fulfill({ contentType: 'font/ttf', body: font }).catch(() => undefined);
  });
  await page.evaluate(() => {
    const native = window.setTimeout;
    window.scheduledTimeouts = [];
    window.setTimeout = (callback, delay, ...args) => {
      window.scheduledTimeouts.push(delay);
      return native(callback, delay === 14000 ? 500 : delay === 1200 ? 0 : delay, ...args);
    };
    window.measurements = [];
  });
  const bounded = await Promise.race([
    page.evaluate(() => assertCard({ path: 'never-card.html', name: 'Never-settled face' })),
    new Promise((_, reject) => setTimeout(() => reject(new Error('The iframe timeout was disabled while waiting for fonts')), 2000))
  ]);
  assert.equal(bounded.ok, false);
  assert(bounded.findings.some(finding => finding.id === 'load' && finding.note.includes('14 s')));
  assert((await page.evaluate(() => window.scheduledTimeouts)).includes(14000), 'The shipped 14-second bound remains scheduled');
  assert.equal(await page.evaluate(() => window.measurements.length), 0);
  assert.equal(await page.locator('#stage iframe').count(), 0);
  releaseNever();
  console.log('PASS specimen settle: timeout remains active throughout font loading');
} finally {
  releaseFont?.();
  await browser.close();
}
