#!/usr/bin/env node
// Real rendered negative fixtures for the actual audit helpers. No component
// runtime is injected: these pages exercise styles, resources and child frames.
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { auditVisibleFrames, monitorResourceFailures } from './browser-audit-helpers.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const document = (head, content) => `<!doctype html><html lang="en"><head><title>Audit regression</title><link rel="icon" href="data:,">${head}</head><body><main><h1>Audit regression</h1>${content}</main></body></html>`;
const styles = '<link rel="stylesheet" href="../styles.css">';
const files = new Map([
  ['/styles.css', ['text/css', '@import "./tokens/palette.css";']],
  ['/tokens/palette.css', ['text/css', 'body{background:white;color:black}.low-contrast{color:#ddd}']],
  ['/preview/import.html', ['text/html', document(styles, '<p class="low-contrast">Imported low contrast must fail.</p>')]],
  ['/preview/nested.html', ['text/html', document(styles, '<iframe title="Nested explanation" src="../child.html"></iframe>')]],
  ['/child.html', ['text/html', document('<link rel="stylesheet" href="/styles.css">', '<p class="low-contrast">Child-frame low contrast must fail.</p>')]],
  ['/preview/missing.html', ['text/html', document('<link rel="stylesheet" href="../missing.css">', '<p>A real missing stylesheet must fail.</p>')]],
  ['/preview/stylesheet.html', ['text/html', document('<link rel="stylesheet" href="../broken.css">', '<p>An interrupted stylesheet must fail.</p>')]],
  ['/preview/script.html', ['text/html', document('<script src="../broken.js"></script>', '<p>An interrupted script must fail.</p>')]],
]);
const server = createServer((request, response) => {
  const item = files.get(new URL(request.url, 'http://fixture.test').pathname);
  response.writeHead(item ? 200 : 404, { 'content-type': item?.[0] ?? 'text/plain' });
  response.end(item?.[1] ?? 'Missing fixture');
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const base = `http://127.0.0.1:${server.address().port}`;
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });
const axeSource = await readFile(resolve(root, 'node_modules/axe-core/axe.min.js'), 'utf8');
try {
  for (const fixture of ['import', 'nested']) {
    const page = await browser.newPage();
    const failures = [];
    monitorResourceFailures(page, failures, base);
    await page.goto(`${base}/preview/${fixture}.html`);
    const audit = await auditVisibleFrames(page, axeSource);
    assert.deepEqual(failures, [], 'Loaded CSS imports must not invent document-relative requests');
    assert(audit.violations.some(item => item.id === 'color-contrast'), 'Loaded colors remain subject to the contrast rule');
    if (fixture === 'nested') {
      assert.equal(audit.frames.length, 2, 'The parent and actual rendered child are each audited');
      assert(audit.violations.some(item => item.id === 'color-contrast' && item.url.endsWith('/child.html')), 'Child-only contrast failures must fail the parent audit');
    }
    await page.close();
  }
  const missing = await browser.newPage();
  const missingFailures = [];
  monitorResourceFailures(missing, missingFailures, base);
  await missing.goto(`${base}/preview/missing.html`);
  assert(missingFailures.some(item => item.url === '/missing.css' && item.status === 404), 'A genuine missing stylesheet must fail');
  await missing.close();
  for (const [kind, filename] of [['stylesheet', 'broken.css'], ['script', 'broken.js']]) {
    for (const errorCode of ['aborted', 'connectionreset']) {
      const page = await browser.newPage();
      const failures = [];
      monitorResourceFailures(page, failures, base);
      await page.route(`${base}/${filename}`, route => route.abort(errorCode));
      await page.goto(`${base}/preview/${kind}.html`);
      assert(failures.some(item => item.url === `/${filename}` && item.error && item.resourceType === kind), `${kind} ${errorCode} without an HTTP response must fail`);
      if (errorCode === 'aborted') assert(failures.some(item => item.error === 'net::ERR_ABORTED'), 'The previously ignored abort must be observed as an actual asset failure');
      await page.close();
    }
  }
  console.log('Browser audit negative fixtures: imported contrast, nested-frame contrast, missing CSS, and interrupted CSS/scripts passed.');
} finally {
  await browser.close();
  await new Promise(resolve => server.close(resolve));
}
