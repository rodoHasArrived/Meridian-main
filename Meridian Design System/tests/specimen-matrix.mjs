#!/usr/bin/env node
// Render every manifest specimen through the existing conformance harness.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createServer } from 'node:http';
import { mkdir, mkdtemp, readFile, stat, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, extname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const options = {};
for (let i = 2; i < process.argv.length; i++) {
  const arg = process.argv[i];
  if (arg === '--help' || arg === '--validate-peers-only') options[arg.slice(2)] = true;
  else if (['--peer-cache', '--base-url', '--output', '--timeout-ms'].includes(arg)) {
    assert(process.argv[i + 1] && !process.argv[i + 1].startsWith('--'), `${arg} requires a value`);
    options[arg.slice(2)] = process.argv[++i];
  } else throw new Error(`Unknown argument: ${arg}`);
}
if (options.help) {
  console.log(`Usage: node tests/specimen-matrix.mjs --peer-cache PATH [options]

--peer-cache PATH       Prepared pinned runtime cache (or MERIDIAN_SPECIMEN_PEER_CACHE).
--base-url URL          Existing package server (or MERIDIAN_SPECIMEN_URL).
                       Omit to start an ephemeral loopback static server.
--output PATH          Artifact directory (or MERIDIAN_SPECIMEN_OUTPUT); defaults to OS temp.
--timeout-ms MS        Full matrix timeout, default 300000.
--validate-peers-only  Check cache bytes, versions and source references without rendering.

Browser: MERIDIAN_CHROMIUM_PATH / CHROMIUM_EXECUTABLE_PATH, otherwise Playwright Chromium.
See tests/SPECIMEN_MATRIX.md for cache preparation and evidence scope.`);
  process.exit(0);
}

const peerCache = options['peer-cache'] || process.env.MERIDIAN_SPECIMEN_PEER_CACHE;
assert(peerCache, 'An explicit --peer-cache PATH or MERIDIAN_SPECIMEN_PEER_CACHE is required; see tests/SPECIMEN_MATRIX.md');
const timeout = Number(options['timeout-ms'] || 300000);
assert(Number.isFinite(timeout) && timeout > 0, '--timeout-ms must be a positive number');
const output = options.output || process.env.MERIDIAN_SPECIMEN_OUTPUT;
const outputPath = output ? resolve(output) : await mkdtemp(join(tmpdir(), 'meridian-specimen-'));
await mkdir(outputPath, { recursive: true });

const pins = [
  { name: 'react', version: '18.3.1', file: 'umd/react.development.js',
    url: 'https://unpkg.com/react@18.3.1/umd/react.development.js',
    integrity: 'sha384-hD6/rw4ppMLGNu3tX5cjIb+uRZ7UkRJ6BPkLpg4hAu/6onKUg4lLsHAs9EBPT82L' },
  { name: 'react-dom', version: '18.3.1', file: 'umd/react-dom.development.js',
    url: 'https://unpkg.com/react-dom@18.3.1/umd/react-dom.development.js',
    integrity: 'sha384-u6aeetuaXnQ38mYT8rp6sbXaQe3NL9t+IBXmnYxwkUI2Hw4bsp2Wvmx4yRQF1uAm' },
  { name: '@babel/standalone', version: '7.29.0', file: 'babel.min.js',
    url: 'https://unpkg.com/@babel/standalone@7.29.0/babel.min.js',
    integrity: 'sha384-m08KidiNqLdpJqLq95G/LEi8Qvjl/xUYll3QILypMoQ65QorJ9Lvtp2RXYGBFj1y' }
];
const runtimes = new Map();
const report = {
  startedAt: new Date().toISOString(), status: 'FAIL', evidenceScope: 'Synthetic specimen conformance; pinned local peer substitution; not live CDN or application certification',
  motion: 'no-preference', viewport: { width: 1440, height: 900 }, peerCache: resolve(peerCache),
  output: outputPath, peerPins: [], sourcePeerReferences: [], missingIntegrityReferences: [],
  cacheSubstitutions: [], remoteDenied: [], errors: [], consoleErrors: [], resourceFailures: [], httpFailures: [], diagnostics: []
};
let browser, server, page, progress, startHash;
const digest = async () => createHash('sha256').update(await readFile(resolve(root, '_ds_bundle.js'))).digest('hex');

async function startStaticServer() {
  const mime = { '.html': 'text/html', '.js': 'application/javascript', '.css': 'text/css', '.json': 'application/json',
    '.svg': 'image/svg+xml', '.png': 'image/png', '.jpg': 'image/jpeg', '.ico': 'image/x-icon',
    '.ttf': 'font/ttf', '.woff': 'font/woff', '.woff2': 'font/woff2', '.txt': 'text/plain' };
  server = createServer(async (request, response) => {
    try {
      const pathname = decodeURIComponent(new URL(request.url, 'http://127.0.0.1').pathname);
      const target = resolve(root, `.${pathname.endsWith('/') ? pathname + 'index.html' : pathname}`);
      const rel = relative(root, target);
      if (rel.startsWith('..') || rel === '') { response.writeHead(403); response.end(); return; }
      if (!(await stat(target)).isFile()) throw new Error('Not a file');
      response.writeHead(200, { 'Content-Type': mime[extname(target)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
      response.end(await readFile(target));
    } catch (error) {
      response.writeHead(error.code === 'ENOENT' ? 404 : 400);
      response.end();
    }
  });
  await new Promise((ready, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', ready); });
  return `http://127.0.0.1:${server.address().port}`;
}

try {
  const manifest = JSON.parse(await readFile(resolve(root, '_ds_manifest.json'), 'utf8'));
  assert(Array.isArray(manifest.cards) && manifest.cards.length > 0, 'The manifest must declare specimen cards');
  report.expectedCards = manifest.cards.length;
  for (const pin of pins) {
    const directory = resolve(peerCache, 'node_modules', pin.name);
    const pkg = JSON.parse(await readFile(resolve(directory, 'package.json'), 'utf8'));
    assert.equal(pkg.version, pin.version, `Pinned ${pin.name} version`);
    const bytes = await readFile(resolve(directory, pin.file));
    const integrity = `sha384-${createHash('sha384').update(bytes).digest('base64')}`;
    report.peerPins.push({ ...pin, actualVersion: pkg.version, computedIntegrity: integrity, bytes: bytes.length });
    assert.equal(integrity, pin.integrity, `Pinned ${pin.name} bytes must match the existing specimen SRI`);
    runtimes.set(pin.url, { bytes, integrity });
  }
  for (const card of manifest.cards) {
    const html = await readFile(resolve(root, card.path), 'utf8');
    for (const match of html.matchAll(/<script\b([^>]+)>/gi)) {
      const url = match[1].match(/\bsrc\s*=\s*["']([^"']+)/i)?.[1];
      if (!url || !/^https?:/.test(url)) continue;
      const declared = match[1].match(/\bintegrity\s*=\s*["']([^"']+)/i)?.[1] || null;
      const reference = { card: card.path, url, declaredIntegrity: declared, cacheIntegrity: runtimes.get(url)?.integrity || null };
      report.sourcePeerReferences.push(reference);
      if (!declared) report.missingIntegrityReferences.push(reference);
      if (!runtimes.has(url)) report.errors.push(`Unmapped external script in ${card.path}: ${url}`);
      else if (declared && declared !== runtimes.get(url).integrity) report.errors.push(`Declared SRI does not match pinned bytes in ${card.path}: ${url}`);
    }
  }
  if (options['validate-peers-only']) {
    report.status = report.errors.length ? 'FAIL' : 'PEERS_VERIFIED';
  } else {
    const base = (options['base-url'] || process.env.MERIDIAN_SPECIMEN_URL || await startStaticServer()).replace(/\/+$/, '');
    const origin = new URL(base).origin;
    report.url = `${base}/tests/conformance.html`;
    startHash = await digest();
    report.bundleStartSha256 = startHash;
    const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
    browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });
    report.browser = await browser.version();
    const context = await browser.newContext({ viewport: report.viewport, reducedMotion: report.motion });
    await context.exposeBinding('recordSpecimenDiagnostic', ({ frame }, data) => report.diagnostics.push({ frame: frame.url(), ...data }));
    await context.addInitScript(() => {
      const record = data => window.recordSpecimenDiagnostic?.({ kind: 'runtime', href: location.href, ...data });
      window.addEventListener('error', event => {
        if (event.message) record({ message: event.message, filename: event.filename, line: event.lineno });
        else if (event.target?.src || event.target?.href) record({ message: `Resource failed: ${event.target.src || event.target.href}` });
      }, true);
      window.addEventListener('unhandledrejection', event => record({ message: String(event.reason?.stack || event.reason) }));
    });
    await context.route('**/*', async route => {
      const url = route.request().url();
      if (runtimes.has(url)) {
        report.cacheSubstitutions.push({ url, frame: route.request().frame().url() });
        return route.fulfill({ status: 200, contentType: 'application/javascript', headers: { 'access-control-allow-origin': '*' }, body: runtimes.get(url).bytes });
      }
      const parsed = new URL(url);
      if (parsed.origin === origin || ['data:', 'blob:'].includes(parsed.protocol)) return route.continue();
      report.remoteDenied.push({ url, frame: route.request().frame().url() });
      return route.abort('blockedbyclient');
    });
    page = await context.newPage();
    page.on('pageerror', error => report.errors.push(error.message));
    page.on('console', message => { if (message.type() === 'error') report.consoleErrors.push({ message: message.text(), location: message.location() }); });
    page.on('requestfailed', request => report.resourceFailures.push({ url: request.url(), frame: request.frame().url(), error: request.failure()?.errorText }));
    page.on('response', response => { if (response.status() >= 400) report.httpFailures.push({ status: response.status(), url: response.url() }); });
    await page.goto(report.url, { waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => !!window.__conformance);
    await page.evaluate(() => {
      const mounted = window.MeridianConformance.CARD_ASSERTIONS.find(rule => rule.id === 'mounted');
      const original = mounted.run;
      mounted.run = (doc, win) => {
        try {
          const elements = [...doc.querySelectorAll('*')];
          const activeAll = elements.filter(element => {
            const style = win.getComputedStyle(element);
            const props = style.transitionProperty.split(',').map(value => value.trim());
            const durations = style.transitionDuration.split(',').map(value => parseFloat(value));
            return props.some((property, index) => property === 'all' && durations[index % durations.length] > 0);
          });
          window.recordSpecimenDiagnostic({ kind: 'assertSnapshot', href: win.location.href,
            visibleTextLength: doc.body?.innerText.trim().length || 0, bodyElementCount: doc.body?.querySelectorAll('*').length || 0,
            roots: ['root', 'app', 'demo', 'demo1', 'demo2'].map(id => doc.getElementById(id)).filter(Boolean).map(node => ({ id: node.id, children: node.childElementCount, text: node.innerText.slice(0, 120) })),
            images: [...doc.images].map(image => ({ src: image.src, complete: image.complete, width: image.naturalWidth, alt: image.alt })),
            activeTransitionAll: activeAll.length,
            activeTransitionExamples: activeAll.slice(0, 4).map(element => element.outerHTML.slice(0, 250)),
            untypedButtons: [...doc.querySelectorAll('button:not([type])')].slice(0, 5).map(node => node.outerHTML.slice(0, 300))
          });
        } catch (error) {
          window.recordSpecimenDiagnostic({ kind: 'collectorDiagnosticFailure', message: String(error.stack || error) });
        }
        return original(doc, win);
      };
    });
    report.initiatedBy = 'Run card assertions button';
    await page.getByRole('button', { name: 'Run card assertions' }).click();
    progress = setInterval(async () => {
      try { console.log(`PROGRESS ${await page.locator('#cards-idx').textContent()}`); } catch { /* The final report records collector failures. */ }
    }, 20000);
    await page.waitForFunction(count => window.__conformance?.cards?.total === count && !document.getElementById('runCards').disabled, manifest.cards.length, { timeout });
    report.conformance = await page.evaluate(() => window.__conformance);
    await page.screenshot({ path: join(outputPath, 'scoreboard.png'), fullPage: false });
    report.bundleEndSha256 = await digest();
    report.bundleStable = startHash === report.bundleEndSha256;
    const runtimeDiagnostics = report.diagnostics.filter(item => item.kind === 'runtime' || item.kind === 'collectorDiagnosticFailure');
    const snapshots = report.diagnostics.filter(item => item.kind === 'assertSnapshot');
    if (snapshots.length !== manifest.cards.length) report.errors.push(`Expected ${manifest.cards.length} assertion snapshots; recorded ${snapshots.length}`);
    report.status = (report.conformance.verdict === 'PASS' && report.conformance.cards.total === manifest.cards.length
      && report.conformance.cards.pass === manifest.cards.length && report.conformance.cards.findings.length === 0
      && report.bundleStable && !report.errors.length && !report.consoleErrors.length && !report.resourceFailures.length
      && !report.httpFailures.length && !report.remoteDenied.length && !runtimeDiagnostics.length) ? 'PASS' : 'FAIL';
  }
} catch (error) {
  report.status = 'FAIL';
  report.errors.push(`Collector: ${error.stack || error}`);
  if (page) report.conformance = await page.evaluate(() => window.__conformance || null).catch(() => null);
} finally {
  clearInterval(progress);
  if (startHash && !report.bundleEndSha256) {
    report.bundleEndSha256 = await digest().catch(() => null);
    report.bundleStable = startHash === report.bundleEndSha256;
  }
  try { await browser?.close(); } catch (error) { report.status = 'FAIL'; report.errors.push(`Browser cleanup: ${error.message}`); }
  if (server) await new Promise(resolveClose => server.close(error => {
    if (error) { report.status = 'FAIL'; report.errors.push(`Server cleanup: ${error.message}`); }
    resolveClose();
  }));
  // Cleanup may expose late asset failures. Keep them fatal instead of writing a
  // PASS report with requestfailed events that arrived after the final assertion.
  if (report.status === 'PASS' && (report.errors.length || report.consoleErrors.length || report.resourceFailures.length
      || report.httpFailures.length || report.remoteDenied.length
      || report.diagnostics.some(item => item.kind === 'runtime' || item.kind === 'collectorDiagnosticFailure'))) report.status = 'FAIL';
  report.finishedAt = new Date().toISOString();
  await writeFile(join(outputPath, 'result.json'), JSON.stringify(report, null, 2));
  const byRule = {};
  for (const finding of report.conformance?.cards?.findings || []) byRule[finding.id] = (byRule[finding.id] || 0) + 1;
  console.log(JSON.stringify({ status: report.status, output: outputPath, cards: report.conformance?.cards?.total,
    pass: report.conformance?.cards?.pass, byRule, runtimeErrors: report.errors.length, consoleErrors: report.consoleErrors.length,
    resourceFailures: report.resourceFailures.length, httpFailures: report.httpFailures.length, remoteDenied: report.remoteDenied.length,
    sourcePeerReferences: report.sourcePeerReferences.length, missingSourceIntegrity: report.missingIntegrityReferences.length,
    cacheSubstitutions: report.cacheSubstitutions.length, bundleStable: report.bundleStable }));
}
if (!['PASS', 'PEERS_VERIFIED'].includes(report.status)) process.exitCode = 1;
