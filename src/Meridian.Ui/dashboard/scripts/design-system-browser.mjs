#!/usr/bin/env node
/** Rendered acceptance for package references and the consuming workstation.
 * Results distinguish automated evidence from the required Windows/NVDA manual pass.
 * All data is synthetic. Browser CDN requests are fulfilled from verified pinned files.
 */
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawn } from 'node:child_process';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import fs from 'node:fs/promises';
import http from 'node:http';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { chromium } from 'playwright';

const exec = promisify(execFile);
export const dashboardDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const repoRoot = path.resolve(dashboardDir, '../../..');
const packageRoot = path.join(repoRoot, 'Meridian Design System');
const NAMESPACE = 'MeridianDesignSystem_4f61be';
export const PINNED_ASSETS = [
  ['https://unpkg.com/react@18.3.1/umd/react.production.min.js', 'd949f1c3687aedadcedac85261865f29b17cd273997e7f6b2bfc53b2f9d4c4dd'],
  ['https://unpkg.com/react-dom@18.3.1/umd/react-dom.production.min.js', '35f4f974f4b2bcd44da73963347f8952e341f83909e4498227d4e26b98f66f0d'],
  ['https://unpkg.com/react@18.3.1/umd/react.development.js', '28348fef6cb0ed8b2ceeb22deaf824428fd13875d84c73d38f77dd216fc24e7f'],
  ['https://unpkg.com/react-dom@18.3.1/umd/react-dom.development.js', 'f9044a5e9c39db8bb1a204dff924e526ec0a621e695bb69de1035811be8709e4'],
  ['https://unpkg.com/@babel/standalone@7.29.0/babel.min.js', '2623a9e22809915ce789b4461154e277ddce520d5a4320c14d44332a5d0dcea0'],
  ['https://unpkg.com/@babel/standalone@7.26.4/babel.min.js', 'a12872ea8da3d29b2a296c51bfac7c482e81419c755f2207a49ad9b77200f4ea']
].map(([url, sha256]) => ({ url, sha256 }));

export function parseOptions(argv) {
  const options = { scope: 'all', browser: 'chromium', workers: 2, outputDir: path.join(repoRoot, 'artifacts/design-system-compliance/browser') };
  const names = new Set(['scope', 'browser', 'url', 'executable-path', 'output-dir', 'filter', 'workers', 'manual-evidence']);
  for (let index = 0; index < argv.length; index++) {
    const argument = argv[index];
    if (['--help', '--list', '--offline', '--require-windows'].includes(argument)) { options[argument.slice(2)] = true; continue; }
    const [key, inline] = argument.replace(/^--/, '').split('=', 2);
    if (!argument.startsWith('--') || !names.has(key)) throw new Error(`Unknown option: ${argument}`);
    const value = inline ?? argv[++index];
    if (!value || value.startsWith('--')) throw new Error(`Missing value for --${key}`);
    options[key] = value;
  }
  if (!['all', 'package', 'workstation'].includes(options.scope)) throw new Error('--scope must be all, package, or workstation');
  if (!['chromium', 'chrome', 'msedge'].includes(options.browser)) throw new Error('--browser must be chromium, chrome, or msedge');
  options.workers = Number(options.workers);
  if (!Number.isInteger(options.workers) || options.workers < 1 || options.workers > 6) throw new Error('--workers must be an integer from 1 to 6');
  options.outputDir = path.resolve(options['output-dir'] ?? options.outputDir);
  return options;
}

async function walk(directory) {
  return (await Promise.all((await fs.readdir(directory, { withFileTypes: true })).map(async (entry) => {
    const candidate = path.join(directory, entry.name);
    return entry.isDirectory() ? walk(candidate) : [candidate];
  }))).flat().sort();
}

export async function packageCases(root = packageRoot) {
  const files = await walk(root);
  const relative = (file) => path.relative(root, file).split(path.sep).join('/');
  const references = [];
  for (const file of files.filter((candidate) => candidate.endsWith('.html') && !candidate.endsWith('.card.html') && !candidate.endsWith('.dc.html') && !relative(candidate).startsWith('templates/') && !relative(candidate).startsWith('tests/') && relative(candidate) !== 'index.html')) {
    const name = relative(file);
    const activeReference = name.startsWith('preview/') || name.startsWith('ui_kits/') || !name.includes('/') || (await fs.readFile(file, 'utf8')).includes('@dsCard');
    if (activeReference) references.push({ id: `reference.${name}`, kind: 'reference', file: name });
  }
  return [
    { id: 'package.exports', kind: 'exports', file: '_ds_manifest.json' },
    ...files.filter((file) => file.endsWith('.card.html')).map((file) => ({ id: `card.${relative(file)}`, kind: 'card', file: relative(file) })),
    ...files.filter((file) => relative(file).startsWith('templates/') && (file.endsWith('.dc.html') || file.endsWith('/index.html'))).map((file) => ({ id: `template.${relative(file)}`, kind: 'template', file: relative(file) })),
    ...references,
    { id: 'package.catalog', kind: 'catalog', file: 'index.html' },
    { id: 'package.gallery', kind: 'catalog', file: 'tests/gallery.html' },
    { id: 'package.behavior-tests', kind: 'behavior', file: 'tests/unit-tests.html' }
  ];
}

export function workstationCases() {
  const routes = [
    ['trading', '/trading'], ['portfolio', '/portfolio'], ['accounting', '/accounting/reconciliation'],
    ['reporting', '/reporting'], ['strategy', '/strategy'], ['data', '/data'], ['settings', '/settings/preferences']
  ];
  const cases = [];
  for (const [workspace, route] of routes) {
    for (const width of [1920, 1440, 1366, 1280]) for (const theme of ['light', 'dark']) {
      cases.push({ id: `workstation.${workspace}.${width}.${theme}`, kind: 'workstation', workspace, route, width, theme });
    }
    for (const [suffix, values] of [
      ['system-dark', { systemDark: true }], ['forced-light', { theme: 'light', systemDark: true }],
      ['reduced-motion', { theme: 'dark', reducedMotion: 'reduce' }], ['forced-colors', { theme: 'light', forcedColors: 'active' }],
      ['print', { theme: 'dark', media: 'print', width: 816 }],
      ...['terminal', 'compact', 'spacious'].map((density) => [`density-${density}`, { theme: 'light', density }])
    ]) cases.push({ id: `workstation.${workspace}.${suffix}`, kind: 'workstation', workspace, route, ...values });
  }
  for (const state of ['normal', 'empty', 'delayed', 'forbidden', 'failed']) {
    cases.push({ id: `scenario.accounting.${state}`, kind: 'scenario', workspace: 'accounting', route: `/accounting/reconciliation?scenario=accounting.${state}`, theme: 'light', state });
  }
  return cases;
}

export function verifyAsset(bytes, expectedSha256, label) {
  const actual = createHash('sha256').update(bytes).digest('hex');
  if (actual !== expectedSha256) throw new Error(`Pinned asset digest mismatch for ${label}: expected ${expectedSha256}, got ${actual}`);
  return bytes;
}

export async function prepareAssets(cacheDir, { offline = false, download } = {}) {
  await fs.mkdir(cacheDir, { recursive: true });
  const assets = new Map();
  for (const entry of PINNED_ASSETS) {
    const file = path.join(cacheDir, `${entry.sha256}.js`);
    let bytes;
    try { bytes = await fs.readFile(file); }
    catch (error) {
      if (error.code !== 'ENOENT') throw error;
      if (offline) throw new Error(`Pinned runtime asset is not cached: ${entry.url}. Run once without --offline.`);
      if (download) bytes = await download(entry.url);
      else {
        // curl inherits the managed proxy/CA configuration; no shell interpolation.
        const result = await exec(process.platform === 'win32' ? 'curl.exe' : 'curl', ['--fail', '--silent', '--show-error', '--location', '--max-time', '45', entry.url], { encoding: 'buffer', maxBuffer: 10 * 1024 * 1024 });
        bytes = result.stdout;
      }
      verifyAsset(bytes, entry.sha256, entry.url);
      await fs.writeFile(file, bytes);
    }
    assets.set(entry.url, verifyAsset(bytes, entry.sha256, entry.url));
  }
  return assets;
}

export function staticTarget(root, requestUrl) {
  const pathname = decodeURIComponent(new URL(requestUrl, 'http://local').pathname);
  const target = path.resolve(root, `.${pathname}`);
  if (target !== root && !target.startsWith(`${root}${path.sep}`)) throw new Error('Path escapes static root');
  return target;
}

export async function startStaticServer(root) {
  const contentTypes = { '.html': 'text/html', '.css': 'text/css', '.js': 'text/javascript', '.jsx': 'text/plain', '.json': 'application/json', '.svg': 'image/svg+xml', '.png': 'image/png', '.woff2': 'font/woff2' };
  const server = http.createServer(async (request, response) => {
    try {
      let target = staticTarget(root, request.url);
      const stat = await fs.stat(target);
      if (stat.isDirectory()) target = path.join(target, 'index.html');
      const body = await fs.readFile(target);
      response.writeHead(200, { 'Content-Type': `${contentTypes[path.extname(target)] ?? 'application/octet-stream'}; charset=utf-8`, 'Cache-Control': 'no-store' });
      response.end(body);
    } catch (error) {
      response.writeHead(error.code === 'ENOENT' ? 404 : 400); response.end('Reference file unavailable');
    }
  });
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  return { url: `http://127.0.0.1:${server.address().port}`, close: () => new Promise((resolve) => server.close(resolve)) };
}

async function availablePort() {
  const server = http.createServer();
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const port = server.address().port;
  await new Promise((resolve) => server.close(resolve));
  return port;
}

async function startWorkstation() {
  const port = await availablePort();
  const vite = path.join(dashboardDir, 'node_modules/vite/bin/vite.js');
  const logs = [];
  const child = spawn(process.execPath, [vite, '--host', '127.0.0.1', '--port', String(port), '--strictPort'], {
    cwd: dashboardDir, env: { ...process.env, MERIDIAN_DEV_MODE: 'fixture-only', VITE_MERIDIAN_DEV_MODE: 'fixture-only', MERIDIAN_SCREENSHOT_CAPTURE: 'true', VITE_CONFIG_NATIVE_IGNORE_WARNING: 'true' }, stdio: ['ignore', 'pipe', 'pipe']
  });
  for (const stream of [child.stdout, child.stderr]) stream.on('data', (chunk) => { logs.push(String(chunk)); if (logs.length > 100) logs.shift(); });
  const url = `http://127.0.0.1:${port}/workstation`;
  const deadline = Date.now() + 30_000;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) throw new Error(`Vite exited with ${child.exitCode}: ${logs.join('')}`);
    try { if ((await fetch(`${url}/`)).ok) return { url, logs, close: () => { child.kill('SIGTERM'); } }; } catch { /* server is starting */ }
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
  child.kill('SIGTERM'); throw new Error(`Vite startup timed out: ${logs.join('')}`);
}

export function manualEvidenceStatus(evidence, sourceCommit) {
  if (!evidence) return { status: 'pending', reason: 'Windows Chrome/Edge keyboard, 200% zoom, and NVDA checks require a human observation.' };
  if (evidence.sourceCommit !== sourceCommit) return { status: 'invalid', reason: 'Manual evidence does not identify the current source commit.' };
  const browsers = ['chrome', 'msedge'];
  const nonempty = (value) => typeof value === 'string' && value.trim().length > 0;
  const concreteVersion = (value) => nonempty(value) && /\d+\.\d+/.test(value);
  const valid = browsers.every((browser) => evidence.browsers?.some((item) => item.browser === browser && item.platform === 'win32' && concreteVersion(item.version) && concreteVersion(item.nvdaVersion) && item.keyboard === 'passed' && item.zoom200 === 'passed' && item.nvda === 'passed'));
  return valid && nonempty(evidence.windowsVersion) && /\d/.test(evidence.windowsVersion) && nonempty(evidence.observer) && /^\d{4}-\d{2}-\d{2}T/.test(evidence.observedAt) && Number.isFinite(Date.parse(evidence.observedAt)) ? { status: 'passed', observedAt: evidence.observedAt } : { status: 'invalid', reason: 'Both Windows browsers need dated keyboard, zoom, and NVDA observations with concrete browser, Windows, and NVDA versions.' };
}

export async function launchBrowser(options) {
  const executablePath = options['executable-path'] ?? process.env.MERIDIAN_BROWSER_EXECUTABLE;
  const base = { headless: true, ...(executablePath ? { executablePath } : options.browser !== 'chromium' ? { channel: options.browser } : {}) };
  if (process.platform !== 'win32' && typeof process.getuid === 'function' && process.getuid() === 0) base.args = ['--no-sandbox'];
  try { return await chromium.launch(base); }
  catch (error) {
    if (executablePath || options.browser !== 'chromium') throw error;
    for (const candidate of ['/usr/bin/chromium', '/usr/bin/chromium-browser']) {
      try { await fs.access(candidate); return await chromium.launch({ ...base, executablePath: candidate }); } catch { /* try the next installed binary */ }
    }
    throw error;
  }
}

export function hasMeaningfulContent(document) {
  return !document.unfinished && (document.text.length >= 20 || document.labelledGraphic === true || document.readableFrame === true);
}

export function childFrameExclusion(file) {
  return file === 'tests/gallery.html' ? 'Gallery embeds the same cards covered by individual acceptance cases.' : null;
}

export async function pageAudit(page, { requireDocument = false, axe = true, excludeFrames = null } = {}) {
  const document = await page.evaluate(() => ({
    title: document.title, text: document.body.innerText.trim(), overlay: !!document.querySelector('vite-error-overlay'),
    headings: Array.from(document.querySelectorAll('h1')).filter((node) => node.getClientRects().length).length,
    mains: Array.from(document.querySelectorAll('main,[role=main]')).filter((node) => node.getClientRects().length).length,
    width: innerWidth, scrollWidth: document.documentElement.scrollWidth,
    labelledGraphic: Array.from(document.querySelectorAll('img')).some((node) => node.complete && node.naturalWidth > 0 && node.alt.trim() && node.getBoundingClientRect().width > 0 && node.getBoundingClientRect().height > 0),
    readableFrame: Array.from(document.querySelectorAll('iframe')).some((node) => node.title.trim() && node.getClientRects().length && (node.contentDocument?.body?.innerText.trim().length ?? 0) >= 20),
    unfinished: /^(?:loading[.…]*|running[.…]*|error|undefined|null)$/i.test(document.body.innerText.trim())
  }));
  assert.ok(document.title.trim(), 'Document title is missing');
  assert.ok(hasMeaningfulContent(document), 'No meaningful rendered reference content');
  assert.equal(document.overlay, false, 'Framework error overlay is visible');
  if (requireDocument) {
    assert.equal(document.headings, 1, `Expected one visible h1, found ${document.headings}`);
    assert.equal(document.mains, 1, `Expected one visible main landmark, found ${document.mains}`);
    assert.ok(document.scrollWidth <= document.width + 2, `Page overflows horizontally: ${document.scrollWidth} > ${document.width}`);
  }
  let violations = [];
  if (axe) {
    await page.addScriptTag({ path: path.join(dashboardDir, 'node_modules/axe-core/axe.min.js') });
    violations = await page.evaluate(async () => {
      // All local styles are already loaded. axe's optional CSSOM preloader clones
      // @import rules against the document URL rather than the stylesheet URL;
      // prevent that extra fetch while retaining every WCAG rule (including contrast).
      const result = await window.axe.run(document, { preload: false, iframes: false, runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'] } });
      return result.violations.map((item) => ({ id: item.id, impact: item.impact, description: item.description, nodes: item.nodes.map((node) => ({ target: node.target, summary: node.failureSummary, html: node.html })) }));
    });
  }
  const frameResults = [];
  if (axe && !excludeFrames) {
    for (const frame of page.frames().filter((item) => item !== page.mainFrame())) {
      await frame.addScriptTag({ path: path.join(dashboardDir, 'node_modules/axe-core/axe.min.js') });
      const frameViolations = await frame.evaluate(async () => {
        const result = await window.axe.run(document, { preload: false, iframes: false, runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'] } });
        return result.violations.map((item) => ({ id: item.id, impact: item.impact, description: item.description, nodes: item.nodes.map((node) => ({ target: node.target, summary: node.failureSummary, html: node.html })) }));
      });
      const title = await frame.evaluate(() => document.title);
      frameResults.push({ url: frame.url(), title, violations: frameViolations });
      violations.push(...frameViolations.map((item) => ({ ...item, frame: { url: frame.url(), title } })));
    }
  }
  return { document: { ...document, text: document.text.slice(0, 500) }, axeRun: axe, frameCoverage: { inspected: frameResults.length, excludedReason: excludeFrames, results: frameResults }, violations };
}

export async function runCase(browser, target, settings) {
  const started = Date.now();
  const context = await browser.newContext({ viewport: { width: target.width ?? 1440, height: target.width === 1920 ? 1080 : target.width === 1366 ? 768 : target.width === 1280 ? 720 : 900 }, colorScheme: target.systemDark ? 'dark' : 'light', reducedMotion: target.reducedMotion ?? 'no-preference', forcedColors: target.forcedColors ?? 'none' });
  // Reference charts generate synthetic data with Math.random; keep those samples
  // stable without altering real browser timers, Date, or cryptographic APIs.
  await context.addInitScript(() => {
    let state = 0x4d455249;
    Math.random = () => { state = (Math.imul(1664525, state) + 1013904223) >>> 0; return state / 4294967296; };
  });
  const page = await context.newPage();
  const errors = [], consoleErrors = [], consoleWarnings = [], assetFailures = [], unsupportedExternals = [];
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('console', (message) => {
    if (message.type() === 'error' && !message.text().startsWith('Failed to load resource:')) consoleErrors.push(message.text());
    if (message.type() === 'warning') consoleWarnings.push(message.text());
  });
  page.on('response', (response) => {
    if (response.status() >= 400 && !new URL(response.url()).pathname.startsWith('/api/')) assetFailures.push({ url: response.url(), status: response.status() });
  });
  await context.route('https://**/*', async (route) => {
    const bytes = settings.assets.get(route.request().url());
    if (bytes) return route.fulfill({ status: 200, contentType: 'text/javascript', headers: { 'Access-Control-Allow-Origin': '*' }, body: bytes });
    unsupportedExternals.push(route.request().url()); return route.abort('blockedbyclient');
  });
  let checks;
  try {
    if (target.kind === 'exports') {
      await page.goto(`${settings.packageUrl}/_ds_manifest.json`);
      // Export contract is separate from real demo rendering. No fabricated generic props.
      await page.addScriptTag({ content: settings.assets.get(PINNED_ASSETS[0].url).toString() });
      await page.addScriptTag({ content: settings.assets.get(PINNED_ASSETS[1].url).toString() });
      await page.addScriptTag({ url: `${settings.packageUrl}/_ds_bundle.js` });
      checks = await page.evaluate(({ namespace, names }) => {
        const value = window[namespace];
        if (!value) throw new Error('Compiled design-system namespace is missing');
        for (const name of names) if (value[name] === undefined) throw new Error(`Missing public export: ${name}`);
        const unexpected = Object.keys(value).filter((name) => name !== '__errors' && !names.includes(name));
        if (unexpected.length) throw new Error(`Undocumented public exports: ${unexpected.join(', ')}`);
        if (value.__errors?.length) throw new Error(`Bundle initialization failed: ${JSON.stringify(value.__errors)}`);
        return { exports: names.length };
      }, { namespace: NAMESPACE, names: settings.manifest.components.map((item) => item.name) });
    } else if (target.kind === 'workstation' || target.kind === 'scenario') {
      const density = target.density ?? 'default';
      await page.addInitScript(({ theme, density }) => {
        localStorage.setItem('meridian.workstation.appearance.v1', theme ?? 'system');
        localStorage.setItem('meridian.workstation.density.v1', density);
      }, { theme: target.theme, density });
      await page.goto(`${settings.workstationUrl}${target.route}`);
      await page.waitForFunction(() => (document.querySelector('main')?.textContent.length ?? 0) > 200);
      await page.waitForLoadState('networkidle', { timeout: 20_000 });
      if (target.kind === 'scenario') {
        const ready = {
          normal: 'Reconciliation break queue', delayed: 'Reconciliation break queue', empty: 'No reconciliation breaks in the current queue.',
          failed: 'Some workspace data is unavailable', forbidden: 'Some workspace data is unavailable'
        }[target.state];
        await page.getByText(ready, { exact: false }).first().waitFor();
      }
      if (target.workspace === 'accounting' && (target.kind === 'workstation' || ['normal', 'delayed'].includes(target.state))) {
        // The populated fixture selects its first run and break asynchronously.
        // Observe that committed selection before testing scope-dialog cancellation.
        await page.waitForFunction(() => {
          const params = new URL(location.href).searchParams;
          return params.has('reconciliationRun') && params.has('reconciliationBreak');
        });
      }
      await page.waitForTimeout(500); // Theme transitions are part of the existing token cascade.
      const scope = page.getByRole('button', { name: /^(?:Set|Change) operating scope$/ }).first();
      if (await scope.isVisible()) {
        const before = page.url(); await scope.click(); const dialog = page.getByRole('dialog'); await dialog.waitFor();
        await page.keyboard.press('Escape'); await dialog.waitFor({ state: 'hidden' });
        assert.equal(page.url(), before, 'Cancelling the scope dialog changed the current view');
        assert.ok(await scope.evaluate((node) => node === document.activeElement), 'Dialog did not restore focus');
      }
      const interactions = [];
      if (target.id === 'workstation.portfolio.1440.light') {
        const search = page.getByLabel('Search positions by symbol', { exact: true });
        await search.fill('NO-MATCH-ACCEPTANCE');
        await page.getByText(/Showing 0 of \d+ loaded positions/).waitFor();
        await search.fill('');
        await page.getByText(/Showing [1-9]\d* of \d+ loaded positions/).waitFor();
        interactions.push('Portfolio filter updates real loaded rows and can be cleared');
      }
      if (target.id === 'workstation.settings.1440.light') {
        const group = page.getByRole('radiogroup', { name: 'Display density', exact: true });
        const terminal = group.getByRole('radio', { name: 'Terminal', exact: true });
        await terminal.focus(); await page.keyboard.press('ArrowRight');
        const compact = group.getByRole('radio', { name: 'Compact', exact: true });
        assert.ok(await compact.evaluate((node) => node === document.activeElement), 'Density arrows did not move actual focus');
        assert.equal(await compact.getAttribute('aria-checked'), 'true', 'Density arrows did not change selection');
        assert.equal(await page.locator('body').getAttribute('data-theme-density'), 'compact');
        await group.getByRole('radio', { name: 'Default', exact: true }).click();
        interactions.push('Density keyboard movement changes selection, focus, and applied scope');
      }
      const beforePrint = target.media === 'print' ? await page.evaluate(() => ({
        session: document.querySelector('.workstation-session-card')?.innerText,
        provenance: document.querySelector('.workstation-provenance')?.innerText,
        scope: document.querySelector('.workflow-continuity-context')?.textContent.trim(),
        selectedRows: Array.from(document.querySelectorAll('table tr[aria-selected=true], [role=row][aria-selected=true]')).map((node) => node.textContent.trim())
      })) : null;
      if (target.media === 'print') await page.emulateMedia({ media: 'print' });
      checks = { ...await pageAudit(page, { requireDocument: true, axe: target.media !== 'print' }), interactions };
      const appliedDensity = await page.locator('body').getAttribute('data-theme-density');
      assert.equal(appliedDensity, density === 'default' ? null : density, 'Stored density was not applied before rendering');
      if (target.media === 'print') {
        checks.print = await page.evaluate(() => ({
          session: document.querySelector('.workstation-session-card')?.innerText,
          provenance: document.querySelector('.workstation-provenance')?.innerText,
          scope: document.querySelector('.workflow-continuity-context')?.textContent.trim(),
          selectedRows: Array.from(document.querySelectorAll('table tr[aria-selected=true], [role=row][aria-selected=true]')).map((node) => node.textContent.trim())
        }));
        assert.ok(checks.print.session?.trim(), 'Print view lost the environment/session context');
        assert.equal(checks.print.session, beforePrint.session, 'Print view changed the environment/session');
        assert.equal(checks.print.provenance, beforePrint.provenance, 'Print view hid or changed the financial provenance notice');
        assert.equal(checks.print.scope, beforePrint.scope, 'Print view changed the operating scope');
        assert.deepEqual(checks.print.selectedRows, beforePrint.selectedRows, 'Print view changed the selected financial records');
      }
    } else {
      await page.goto(`${settings.packageUrl}/${target.file.split('/').map(encodeURIComponent).join('/')}`);
      if (target.kind === 'template') await page.waitForFunction(() => !!document.querySelector('main,[role=main]'));
      await page.waitForLoadState('networkidle', { timeout: 20_000 });
      await page.waitForTimeout(500);
      if (target.theme) {
        await page.evaluate((theme) => document.documentElement.setAttribute('data-theme', theme), target.theme);
        await page.waitForTimeout(500);
      }
      if (target.kind === 'behavior') {
        await page.waitForFunction(() => /\d+\/\d+ passed/.test(document.querySelector('#summary')?.textContent ?? ''));
        const summary = await page.locator('#summary').innerText();
        const [passed, total] = summary.match(/(\d+)\/(\d+)/).slice(1).map(Number);
        checks = { assertions: total, passed, failures: await page.locator('#results .t--fail').allTextContents() };
        assert.ok(total > 0 && passed === total, `Package behavior assertions failed: ${summary}`);
      } else checks = await pageAudit(page, { requireDocument: target.kind === 'template', excludeFrames: childFrameExclusion(target.file) });
      const bundleErrors = await page.evaluate((namespace) => window[namespace]?.__errors ?? [], NAMESPACE);
      assert.deepEqual(bundleErrors, [], 'Compiled package reported component initialization errors');
    }
    assert.deepEqual(errors, [], 'Browser runtime errors');
    assert.deepEqual(consoleErrors, [], 'Browser console errors');
    const unexpectedWarnings = consoleWarnings.filter((message) => !(!['workstation', 'scenario'].includes(target.kind) && message.startsWith('You are using the in-browser Babel transformer.')));
    assert.deepEqual(unexpectedWarnings, [], 'Unexpected browser console warnings');
    assert.deepEqual(assetFailures, [], 'Local or external assets failed');
    assert.deepEqual(unsupportedExternals, [], 'A reference depends on an unpinned external runtime');
    assert.deepEqual(checks.violations ?? [], [], 'Rendered accessibility violations');
    let screenshot;
    if (target.id.startsWith('package.forced-light.') || /^workstation\.[^.]+\.1440\.(light|dark)$/.test(target.id) || target.media === 'print') {
      screenshot = path.join(settings.outputDir, `${target.id.replace(/[^a-zA-Z0-9.-]/g, '-')}.png`);
      await page.screenshot({ path: screenshot, fullPage: target.media === 'print' });
    }
    return { id: target.id, status: 'passed', seconds: (Date.now() - started) / 1000, screenshot, consoleWarnings, checks };
  } catch (error) {
    const screenshot = path.join(settings.outputDir, `${target.id.replace(/[^a-zA-Z0-9.-]/g, '-')}.png`);
    await page.screenshot({ path: screenshot, fullPage: true }).catch(() => undefined);
    return { id: target.id, status: 'failed', seconds: (Date.now() - started) / 1000, error: error.message, errors, consoleErrors, consoleWarnings, assetFailures, unsupportedExternals, checks, screenshot };
  } finally { await context.close(); }
}

async function parallelMap(values, workers, operation) {
  let next = 0; const results = new Array(values.length);
  await Promise.all(Array.from({ length: Math.min(workers, values.length) }, async () => {
    while (next < values.length) { const index = next++; results[index] = await operation(values[index]); }
  }));
  return results;
}

export async function clearSelectedEvidence(outputDir, cases) {
  const stems = cases.map((item) => item.id.replace(/[^a-zA-Z0-9.-]/g, '-'));
  assert.equal(new Set(stems).size, stems.length, 'Acceptance ids collide after artifact filename normalization');
  await Promise.all([
    fs.rm(path.join(outputDir, 'results.json'), { force: true }),
    ...stems.flatMap((stem) => ['json', 'png'].map((extension) => fs.rm(path.join(outputDir, `${stem}.${extension}`), { force: true })))
  ]);
}

export async function main(argv = process.argv.slice(2)) {
  const options = parseOptions(argv);
  if (options.help) {
    console.log('Meridian rendered design-system acceptance\n\nOptions: --scope all|package|workstation --browser chromium|chrome|msedge\n  --executable-path <binary> --url <existing workstation URL> --workers <1..6>\n  --output-dir <directory> --filter <case-id substring> --list --offline\n  --manual-evidence <JSON file> --require-windows\n\nDefault runs package references plus the workstation fixture matrix. CDN scripts are\nfulfilled from SHA-256 verified cache entries. Windows/NVDA observations are never inferred.');
    return;
  }
  let cases = [];
  if (options.scope !== 'workstation') {
    const inventory = await packageCases();
    for (const item of inventory) {
      if (['card', 'template', 'reference', 'catalog'].includes(item.kind)) for (const theme of ['light', 'dark']) cases.push({ ...item, id: `${item.id}.${theme}`, theme });
      else cases.push(item);
    }
    for (const [category, file] of [
      ['colors', 'guidelines/colors-states.card.html'], ['radii', 'guidelines/spacing-radii.card.html'],
      ['chart', 'components/charts/charts.card.html'], ['accounting', 'components/accounting/accounting.card.html'],
      ['shell', 'components/shell/shell.card.html'], ['session', 'templates/session-start/SessionStart.dc.html'],
      ['report-pack', 'templates/report-pack/ReportPack.dc.html']
    ]) {
      const item = inventory.find((candidate) => candidate.file === file);
      assert.ok(item, `Required forced-light reference is missing: ${file}`);
      cases.push({ ...item, id: `package.forced-light.${category}`, theme: 'light', systemDark: true });
    }
  }
  if (options.scope !== 'package') cases.push(...workstationCases());
  if (options.filter) cases = cases.filter((item) => item.id.includes(options.filter));
  assert.ok(cases.length, 'No acceptance cases match the selection');
  if (options.list) { console.log(cases.map((item) => item.id).join('\n')); return; }
  await fs.mkdir(options.outputDir, { recursive: true });
  await clearSelectedEvidence(options.outputDir, cases);
  const sourceCommit = (await exec('git', ['rev-parse', 'HEAD'], { cwd: repoRoot })).stdout.trim();
  const dirty = (await exec('git', ['status', '--porcelain'], { cwd: repoRoot })).stdout.trim().length > 0;
  const assets = options.scope === 'workstation' ? new Map() : await prepareAssets(path.join(options.outputDir, 'runtime-assets'), { offline: options.offline });
  const manifest = JSON.parse(await fs.readFile(path.join(packageRoot, '_ds_manifest.json'), 'utf8'));
  let staticServer, workstation, browser;
  const results = [];
  try {
    if (options.scope !== 'workstation') staticServer = await startStaticServer(packageRoot);
    if (options.scope !== 'package' && !options.url) workstation = await startWorkstation();
    browser = await launchBrowser(options);
    const settings = { assets, manifest, packageUrl: staticServer?.url, workstationUrl: (options.url ?? workstation?.url)?.replace(/\/$/, ''), outputDir: options.outputDir };
    results.push(...await parallelMap(cases, options.workers, async (item) => {
      const result = await runCase(browser, item, settings);
      console.log(`${result.status === 'passed' ? 'PASS' : 'FAIL'} ${result.id}${result.error ? ` — ${result.error.split('\n')[0]}` : ''}`);
      await fs.writeFile(path.join(options.outputDir, `${item.id.replace(/[^a-zA-Z0-9.-]/g, '-')}.json`), `${JSON.stringify(result, null, 2)}\n`);
      return result;
    }));
  } finally { await browser?.close(); await staticServer?.close(); await workstation?.close(); }
  let manual;
  if (options['manual-evidence']) manual = JSON.parse(await fs.readFile(options['manual-evidence'], 'utf8'));
  const manualStatus = dirty ? { status: 'pending', reason: 'Working tree differs from the identified commit; collect final manual evidence after committing.' } : manualEvidenceStatus(manual, sourceCommit);
  const failures = results.filter((item) => item.status === 'failed');
  const report = { schema: 'meridian.design-system.browser.v1', sourceCommit, dirty, observedAt: new Date().toISOString(), platform: process.platform, browser: options.browser, browserVersion: browser?.version(), evidenceScope: 'synthetic-verified', selection: { scope: options.scope, filter: options.filter ?? null, cases: cases.length }, automationStatus: failures.length ? 'failed' : 'passed', manualStatus, complianceStatus: failures.length ? 'failed' : manualStatus.status === 'passed' && !options.filter && options.scope === 'all' ? 'passed' : 'pending-full-matrix-or-manual', results };
  await fs.writeFile(path.join(options.outputDir, 'results.json'), `${JSON.stringify(report, null, 2)}\n`);
  await fs.writeFile(path.join(options.outputDir, 'manual-evidence.template.json'), `${JSON.stringify({ sourceCommit, observedAt: null, observer: null, windowsVersion: null, browsers: ['chrome', 'msedge'].map((name) => ({ browser: name, platform: 'win32', version: null, nvdaVersion: null, keyboard: 'pending', zoom200: 'pending', nvda: 'pending' })) }, null, 2)}\n`);
  await fs.writeFile(path.join(options.outputDir, 'windows-manual-checklist.md'), `# Windows browser acceptance — pending human observation\n\nSource commit: ${sourceCommit}${dirty ? ' (working tree dirty; recollect after commit)' : ''}\n\nRun this command on Windows once for --browser chrome and once for --browser msedge.\nUse the same committed source and retain each results.json with the browser version.\nFor the separate manual pass, run npm run dev:fixtures from the repository root and open\nhttp://127.0.0.1:5173/workstation in each Windows browser. Automation is headless and does\nnot supply screen-reader observations.\n\nFor each of the seven workspaces and supported desktop sizes:\n\n- [ ] Tab/Shift+Tab reaches every command; focus remains visible and returns from overlays.\n- [ ] Arrow keys operate radio/segmented/table controls, including disabled options.\n- [ ] At 200% browser zoom with at least 1280px logical viewport width, no text, warning, financial value or command is clipped; wide tables scroll within their own region.\n- [ ] NVDA announces landmarks, headings, input names, selection, errors and async status.\n- [ ] Print retains environment, scope, current selection and source/financial notices.\n\nRecord browser/version, Windows version, NVDA version, observer and observation date.\nNever mark these observations passed based on Linux Chromium or automated axe alone.\n`);
  console.log(`Rendered acceptance: ${results.length - failures.length}/${results.length} passed; Windows/manual: ${manualStatus.status}. Results: ${path.join(options.outputDir, 'results.json')}`);
  if (failures.length) process.exitCode = 1;
  else if (options['require-windows'] && report.complianceStatus !== 'passed') process.exitCode = 2;
  return report;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  main().catch((error) => { console.error(error.stack ?? error.message); process.exitCode = 1; });
}
