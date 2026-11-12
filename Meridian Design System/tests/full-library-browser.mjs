#!/usr/bin/env node
// Manifest-driven local browser audit. Rendering, accessibility and exercised
// behavior are separate evidence; loading an export never certifies its contract.
import { createServer } from 'node:http';
import { readFile, readdir, mkdir, writeFile } from 'node:fs/promises';
import { resolve, dirname, extname, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import assert from 'node:assert/strict';
import { chromium } from 'playwright';

const require = createRequire(import.meta.url);
const babel = require('@babel/standalone');
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const args = new Map(process.argv.slice(2).map(arg => { const [key, ...value] = arg.replace(/^--/, '').split('='); return [key, value.join('=') || true]; }));
const out = resolve(String(args.get('out') || process.env.MERIDIAN_FULL_AUDIT_OUTPUT || '/tmp/pr3501-full-audit'));
const selection = String(args.get('kind') || 'all');
const matrix = String(args.get('matrix') || 'full');
const only = args.get('only') ? String(args.get('only')).split(',').filter(Boolean) : null;
const limit = Number(args.get('limit') || Infinity);
const concurrency = Math.max(1, Math.min(4, Number(args.get('workers') || 3)));
for (const key of args.keys()) if (!['out', 'kind', 'matrix', 'only', 'limit', 'workers'].includes(key)) throw new Error(`Unknown audit argument --${key}`);
if (!['all', 'cards', 'templates', 'contracts'].includes(selection)) throw new Error(`Unknown audit kind ${selection}`);
if (!['full', 'smoke'].includes(matrix)) throw new Error(`Unknown audit matrix ${matrix}`);
if (limit !== Infinity && (!Number.isInteger(limit) || limit < 1)) throw new Error('--limit must be a positive integer');
if (!Number.isInteger(concurrency)) throw new Error('--workers must be an integer');
const manifestBytes = await readFile(resolve(root, '_ds_manifest.json'));
const manifest = JSON.parse(manifestBytes);
const eligibility = JSON.parse(await readFile(resolve(root, 'api/template-eligibility.json'), 'utf8'));
const journeyBytes = await readFile(resolve(root, 'tests/template-journeys.json')).catch(error => error.code === 'ENOENT' ? Buffer.from('{"schemaVersion":1,"entries":{}}') : Promise.reject(error));
const journeys = JSON.parse(journeyBytes);
const runtime = await readFile(resolve(root, '_preview_runtime.js'), 'utf8');
const babelBrowser = await readFile(resolve(root, '_preview_jsx.js'), 'utf8');
const axe = await readFile(resolve(root, 'node_modules/axe-core/axe.min.js'), 'utf8');
const bundle = await readFile(resolve(root, '_ds_bundle.js'), 'utf8');
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
async function snapshotRuntimeSources() {
  const filenames = ['styles.css','_preview_runtime.js','_preview_jsx.js'];
  const extensions = new Set(['.js','.jsx','.html','.css','.svg','.woff2','.woff','.ttf','.png','.jpg','.jpeg','.webp','.avif','.gif','.ico','.thumbnail']);
  async function walk(relative) {
    const entries=await readdir(resolve(root,relative),{withFileTypes:true});
    for(const entry of entries) {
      const child=`${relative}/${entry.name}`;
      if(entry.isDirectory()) await walk(child);
      else if(entry.isFile()&&(extensions.has(extname(entry.name).toLowerCase())||entry.name==='.thumbnail')) filenames.push(child);
    }
  }
  for(const directory of ['components','tokens','templates','docs','guidelines','assets']) await walk(directory);
  filenames.sort(); const hashes={}; let cursor=0;
  await Promise.all(Array.from({length:16},async()=>{while(cursor<filenames.length){const filename=filenames[cursor++];hashes[filename]=hash(await readFile(resolve(root,filename)));}}));
  return Object.fromEntries(filenames.map(filename=>[filename,hashes[filename]]));
}
const runtimeSourceHashes=await snapshotRuntimeSources();
const zoomControlSha256=hash(await readFile(resolve(root,'tests/desktop-zoom-control.html')));
await mkdir(out, { recursive: true });
await mkdir(resolve(out, 'screenshots'), { recursive: true });

const instrumentation = `(() => {
  const RealDate = Date, instant = RealDate.parse('2026-09-10T14:32:08Z');
  window.Date = class extends RealDate { constructor(...args) { super(...(args.length ? args : [instant])); } static now() { return instant; } };
  window.__audit = { referenced: new Set(), rendered: new Set(), events: [], roots: [] };
  const createElement = React.createElement;
  React.createElement = function(type, ...rest) {
    const names = window.__auditExportTypes?.get(type);
    if (names) for (const name of names) window.__audit.rendered.add(name);
    return createElement.call(this, type, ...rest);
  };
  const createRoot = ReactDOM.createRoot;
  ReactDOM.createRoot = function(container, ...rest) {
    const result = createRoot.call(this, container, ...rest);
    window.__audit.roots.push({ id: container.id, node: container });
    return result;
  };
})();`;
const instrumentBundle = `\n;(() => {
  const target = window.${manifest.namespace};
  const types = window.__auditExportTypes || new Map();
  for (const [name,value] of Object.entries(target)) { const names = types.get(value)||[]; if(!names.includes(name)) names.push(name); types.set(value,names); }
  window.__auditExportTypes = types;
  window.${manifest.namespace} = new Proxy(target,{ get(object,key,receiver) { if(typeof key==='string') window.__audit?.referenced.add(key); return Reflect.get(object,key,receiver); } });
})();`;
const contentTypes = { '.html': 'text/html', '.js': 'text/javascript', '.jsx': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.svg': 'image/svg+xml', '.png': 'image/png', '.ttf': 'font/ttf' };
const themeFixture = `<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Theme contract fixture</title><link rel="stylesheet" href="/styles.css"></head><body style="margin:0;padding:24px;background:var(--bg);color:var(--text-primary)"><main><h1>Theme contracts</h1><div id="root"></div></main><script src="/_ds_bundle.js"></script><script type="text/babel">
const DS=window.${manifest.namespace};
function ThemeFixture(){ const [checked,setChecked]=React.useState(false);return <div style={{display:'grid',gap:12,maxWidth:720}}>
<p>Primary and secondary text remain readable on the current surface.</p>
<DS.Button>Primary action</DS.Button><DS.Button variant="ghost">Secondary action</DS.Button>
<DS.Button variant="danger">Destructive action</DS.Button><DS.Button disabled>Unavailable action</DS.Button>
<DS.Input label="Named input" defaultValue="Synthetic input"/><DS.Input label="Invalid input" defaultValue="Invalid" error="Correct the synthetic input"/>
<DS.Checkbox label="Controlled selection" checked={checked} onChange={setChecked}/>
<DS.StatusBanner tone="warning" title="Review required" detail="Synthetic evidence only"/>
<div aria-label="Semantic token contrast" style={{display:'flex',flexWrap:'wrap',gap:8}}>{['severity-blocked','severity-action','severity-review','severity-ready','severity-info','state-healthy','state-warn','state-danger','state-paper','state-strategy','state-live','state-muted','state-pending'].map(role=><span key={role} data-token-pair={role} style={{fontSize:12,padding:'4px 8px',color:'var(--'+role+'-fg)',background:'var(--'+role+'-bg)',border:'1px solid var(--'+role+'-bd)'}}>{role}</span>)}</div>
</div>};ReactDOM.createRoot(document.getElementById('root')).render(<ThemeFixture/>);
</script></body></html>`;
function prepareHtml(source, filename) {
  const dcTemplate = /<x-dc(?:\s|>)/i.test(source);
  // Specimens historically embed these exact CDN development dependencies.
  // Replace their host-provided runtime with the pinned local audit runtime;
  // unknown remote dependencies remain network failures below.
  source = source.replace(/<script\b[^>]*src=["']https:\/\/unpkg\.com\/(?:react(?:-dom)?@18\.3\.1\/umd\/react(?:-dom)?\.(?:development|production\.min)\.js|@babel\/standalone@7\.29\.0\/babel\.min\.js)["'][^>]*>\s*<\/script>/gi, '');
  // Published previews now load local assets. The project-compatibility host
  // supplies its React19 peer and the published pinned compiler once, before
  // instrumentation. Only these exact known assets are deduplicated; unknown
  // local or remote dependencies retain their normal loading/failure checks.
  const peerAssets=new Set(['/_preview_runtime.js','/_preview_jsx.js',
    '/templates/_scaffold/vendor/react-18.3.1.production.min.js',
    '/templates/_scaffold/vendor/react-dom-18.3.1.production.min.js']);
  const documentPath=filename.startsWith(root+sep)?filename.slice(root.length):'/__audit/theme.html';
  source=source.replace(/<script\b([^>]*)>\s*<\/script>/gi,(whole,attributes)=>{
    const src=attributes.match(/\bsrc\s*=\s*["']([^"']+)["']/i)?.[1];
    if(!src)return whole;
    const url=new URL(src,new URL(documentPath,'http://audit.local'));
    if(url.origin!=='http://audit.local')return whole;
    return peerAssets.has(url.pathname)||(dcTemplate&&url.pathname==='/_ds_bundle.js')?'':whole;
  });
  // The DC host ordinarily transpiles authored JSX. Do that same transformation
  // in the local test server; never edit the authored specimen/template bytes.
  source = source.replace(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi, (whole, attributes, contents) => {
    if (!/type\s*=\s*["']text\/(?:babel|x-dc)["']/i.test(attributes)) return whole;
    const code = babel.transform(contents, { presets: ['react'], filename, sourceType: 'script' }).code;
    return `<script${attributes.replace(/type\s*=\s*["']text\/babel["']/i, 'type="text/javascript"')}>${code}</script>`;
  });
  // A DC host supplies the compiled namespace before evaluating template logic.
  // ds-base's asynchronous link/script setup is not that host precondition.
  const boot = '<script src="/__audit/react.js"></script><script src="/__audit/babel.js"></script><script src="/__audit/instrument.js"></script>' + (dcTemplate ? '<script src="/_ds_bundle.js"></script>' : '');
  return source.replace(/<head([^>]*)>/i, `<head$1>${boot}`);
}
const server = createServer(async (request, response) => {
  try {
    const pathname = decodeURIComponent(new URL(request.url, 'http://local').pathname);
    const internal = { '/__audit/react.js': runtime, '/__audit/babel.js': babelBrowser, '/__audit/instrument.js': instrumentation };
    if (Object.hasOwn(internal, pathname)) { response.writeHead(200, { 'content-type': 'text/javascript' }); return response.end(internal[pathname]); }
    if (pathname === '/__audit/theme.html') { response.writeHead(200, { 'content-type': 'text/html' }); return response.end(prepareHtml(themeFixture, 'theme-fixture.jsx')); }
    const filename = resolve(root, `.${pathname}`);
    if (!filename.startsWith(root + sep)) { response.writeHead(403); return response.end(); }
    let source = await readFile(filename);
    if (extname(filename) === '.html') source = prepareHtml(source.toString('utf8'), filename);
    // One immutable compiled snapshot per run, even during a coordinator rebuild.
    if (filename === resolve(root, '_ds_bundle.js')) source = bundle + instrumentBundle;
    if (filename === resolve(root, '_preview_jsx.js')) source = babelBrowser;
    response.writeHead(200, { 'content-type': `${contentTypes[extname(filename)] || 'application/octet-stream'}; charset=utf-8`, 'cache-control': 'no-store' });
    response.end(source);
  } catch (error) { response.writeHead(error.code === 'ENOENT' ? 404 : 500, { 'content-type': 'text/plain' }); response.end(error.message); }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const base = `http://127.0.0.1:${server.address().port}`;
const browser = await chromium.launch({ executablePath: process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH || '/usr/bin/chromium', headless: true, args: ['--no-sandbox'] });
const results = { schemaVersion: 1, source: { manifestSha256: hash(manifestBytes), bundleSha256: hash(bundle), recipeFileSha256: hash(journeyBytes), zoomControlSha256, runtimeSourceHashes, auditedAt: new Date().toISOString(), syntheticTime: '2026-09-10T14:32:08Z', runtime: 'Local React 19 and Babel 7.29.0; authored JSX transpiled by local test server', run: {kind:selection,matrix,only,limit:Number.isFinite(limit)?limit:null,workers:concurrency} }, cards: {}, templates: {}, exports: {}, harnesses: {}, themes: [], integrity: {cases:[]}, limitations: ['Namespace access and element creation are coverage observations, not proof of every applicable state.', 'The 200% desktop zoom layout equivalent uses 683×384 CSS pixels at DPR2 for a 1366×768 physical viewport; native Windows browser-toolbar zoom is not run.', 'Native Windows/Edge and assistive-technology announcement journeys are not run by this harness.'] };
const jobs = [
  ...(selection === 'all' || selection === 'cards' ? manifest.cards.map(card => ({ kind: 'cards', path: card.path, published: true })) : []),
  ...(selection === 'all' || selection === 'templates' ? Object.entries(eligibility.templates).flatMap(([folder, record]) => record.entries.map(entry => ({ kind: 'templates', path: `templates/${folder}/${entry}`, published: manifest.templates.some(template => template.entryPath === `templates/${folder}/${entry}`) }))) : [])
].filter(job => !only || only.some(fragment => job.path.includes(fragment))).slice(0, limit);
if (selection !== 'contracts' && jobs.length === 0) {
  await browser.close(); await new Promise(resolve => server.close(resolve));
  throw new Error('No declared specimen or template matched the audit selection');
}
const recordCase = (record, name, kind, status, detail) => record.cases.push({ name, kind, status, detail });
const metricsSessions=new WeakMap();
async function setDesktopZoomEquivalent(page,enabled) {
  await page.setViewportSize({width:enabled?683:1366,height:enabled?384:768});
  let session=metricsSessions.get(page);
  if(!session){session=await page.context().newCDPSession(page);metricsSessions.set(page,session);}
  await session.send('Emulation.setDeviceMetricsOverride',{width:enabled?683:1366,height:enabled?384:768,deviceScaleFactor:enabled?2:1,mobile:false,screenWidth:1366,screenHeight:768});
}
async function captureDesktopZoom(page,path) {
  // Playwright's screenshot helper restores its context's DPR1 viewport. Direct
  // CDP capture preserves the emulated DPR2 metrics and physical image size.
  const captured=await metricsSessions.get(page).send('Page.captureScreenshot',{format:'png',fromSurface:true,captureBeyondViewport:false});
  const bytes=Buffer.from(captured.data,'base64'); await writeFile(path,bytes); return bytes;
}
async function auditZoomControls() {
  const context=await browser.newContext({viewport:{width:1366,height:768}}), page=await context.newPage();
  try {
    await page.goto(`${base}/tests/desktop-zoom-control.html`);
    const columns=()=>page.locator('#responsive').evaluate(node=>getComputedStyle(node).gridTemplateColumns.split(/\s+/).length);
    assert.equal(await columns(),2,'The normal desktop fixture starts with two columns');
    await setDesktopZoomEquivalent(page,true); await settleAppearance(page);
    const layout=await page.evaluate(()=>({width:innerWidth,height:innerHeight,dpr:devicePixelRatio,narrow:matchMedia('(max-width:800px)').matches,scrollWidth:document.documentElement.scrollWidth}));
    const screenshot=resolve(out,'screenshots','desktop-zoom-negative-control.png');
    const png=await captureDesktopZoom(page,screenshot);
    recordCase(results.integrity,'200% desktop zoom layout equivalent activates authored narrow media queries','layout',layout.width===683&&layout.height===384&&layout.dpr===2&&layout.narrow&&await columns()===1&&layout.scrollWidth<=683?'passed':'failed',layout);
    recordCase(results.integrity,'desktop zoom screenshot preserves 1366×768 physical pixels','layout',png.readUInt32BE(16)===1366&&png.readUInt32BE(20)===768?'passed':'failed',{width:png.readUInt32BE(16),height:png.readUInt32BE(20),screenshot});
    await page.locator('#forced-overflow').evaluate(node=>{node.style.display='block';});
    const overflow=await page.evaluate(()=>({width:innerWidth,scrollWidth:document.documentElement.scrollWidth}));
    recordCase(results.integrity,'desktop zoom negative control detects genuine overflow beyond 683 CSS pixels','layout',overflow.width===683&&overflow.scrollWidth>683?'passed':'failed',overflow);
  }catch(error){recordCase(results.integrity,'desktop zoom controls execute','layout','failed',error.message);}
  finally{await context.close();}
}

async function contentFrame(page) {
  const iframe = page.locator('iframe[data-meridian-compatibility-target]').first();
  if (await iframe.count()) {
    const frame = await iframe.elementHandle().then(handle => handle.contentFrame());
    if (frame) {
      await frame.waitForURL(url=>url.origin===base,{timeout:8000,waitUntil:'domcontentloaded'});
      await frame.waitForLoadState('domcontentloaded', { timeout: 8000 });
      if (new URL(frame.url()).origin === base) { await frame.waitForFunction(inspectContent, null, { timeout: 8000 }); return frame; }
    }
  }
  return page.mainFrame();
}
function inspectContent() {
  const scope = document.querySelector('#dc-root,#root,#app') || document.body;
  const text = scope?.innerText?.trim() || '';
  const media = scope ? [...scope.querySelectorAll('img[alt],svg[aria-label],svg[aria-labelledby],svg:has(title),iframe[data-meridian-compatibility-target]')].filter(node => {
    const rect = node.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0 && (node instanceof HTMLImageElement ? node.complete && node.naturalWidth > 0 && node.alt.trim() : node instanceof HTMLIFrameElement ? !!node.contentDocument?.body?.children.length : node.children.length > 0);
  }).length : 0;
  return text.length > 0 || media > 0;
}
async function waitForContent(page) {
  await page.waitForFunction(inspectContent, null, { timeout: 8000 });
  // Imported JSX DC modules resolve independently of the document load event.
  await page.waitForTimeout(180);
}
async function setAppearance(frame, theme, density = 'default', template = false) {
  await frame.evaluate(({ theme, density }) => {
    if (window.__dcSetProps && window.__dcRootName) window.__dcSetProps(window.__dcRootName(), { theme, density });
    document.documentElement.dataset.theme = theme;
    document.body.dataset.themeDensity = density;
  }, { theme, density });
  await settleAppearance(frame);
  if(template) {
    const expectsReady=await frame.evaluate(()=>window.__dcRegistry?.[window.__dcRootName?.()]?.propsMeta?.dataState?.default==='ready');
    if(expectsReady) {
      // Some DC entries asynchronously re-bootstrap after host prop updates.
      // A ready fixture must finish that loading phase; permanent loading fails.
      await frame.waitForFunction(()=>document.readyState==='complete'&&![...document.querySelectorAll('.mds-dss--loading')].some(node=>{
        const bounds=node.getBoundingClientRect(); return bounds.width>0&&bounds.height>0&&getComputedStyle(node).visibility!=='hidden';
      }),null,{timeout:8000});
      await settleAppearance(frame);
    }
  }
}
async function settleAppearance(frame) {
  await frame.evaluate(async () => {
    await document.fonts.ready;
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    // Theme changes can start genuine CSS color transitions. Audit their settled
    // values, without disabling motion or waiting forever on live spinners.
    const finite = document.getAnimations().filter(animation => {
      const timing = animation.effect?.getComputedTiming();
      return animation.playState === 'running' && timing && Number.isFinite(timing.endTime) && timing.iterations !== Infinity;
    });
    await Promise.race([Promise.all(finite.map(animation => animation.finished.catch(() => {}))), new Promise(resolve => setTimeout(resolve, 1200))]);
    await new Promise(resolve => requestAnimationFrame(resolve));
  });
}
async function probeKeyboard(frame, record) {
  // Each probe checks a concrete changed state. Static or read-only examples
  // without callbacks are reported blocked rather than treated as successful.
  const checks = frame.locator('input[type="checkbox"]:enabled,[role="switch"][tabindex="0"],[role="checkbox"][tabindex="0"]');
  for (let index = 0; index < Math.min(await checks.count(), 2); index++) {
    const control = checks.nth(index);
    if (!await control.isVisible()) continue;
    const before = await control.evaluate(node => node instanceof HTMLInputElement ? node.checked : node.getAttribute('aria-checked'));
    await control.focus(); await control.press('Space'); await frame.waitForTimeout(50);
    const after = await control.evaluate(node => node instanceof HTMLInputElement ? node.checked : node.getAttribute('aria-checked'));
    recordCase(record, `checkbox/switch ${index + 1} Space changes controlled state`, 'keyboard', before !== after ? 'passed' : 'blocked', { before, after, reason: before === after ? 'Specimen does not demonstrate a changing controlled value' : undefined });
    if (before !== after) { await control.press('Space'); await frame.waitForTimeout(20); }
  }
  const tablists = frame.locator('[role="tablist"]');
  for (let index = 0; index < Math.min(await tablists.count(), 2); index++) {
    const tabs = tablists.nth(index).locator('[role="tab"]:not([disabled]):not([aria-disabled="true"])');
    if (await tabs.count() < 2 || !await tabs.first().isVisible()) continue;
    await tabs.first().focus(); await tabs.first().press('ArrowRight'); await frame.waitForTimeout(50);
    const moved = await tabs.nth(1).evaluate(node => document.activeElement === node);
    recordCase(record, `tab group ${index + 1} ArrowRight moves focus`, 'keyboard', moved ? 'passed' : 'failed', { selected: await tabs.nth(1).getAttribute('aria-selected') });
    await frame.locator('body').press('Escape');
  }
  const combobox = frame.locator('button[role="combobox"]:not([disabled])').first();
  if (await combobox.count() && await combobox.isVisible()) {
    await combobox.focus(); await combobox.press('ArrowDown'); await frame.waitForTimeout(30);
    const expanded = await combobox.getAttribute('aria-expanded') === 'true';
    await frame.locator('body').press('Escape'); await frame.waitForTimeout(30);
    recordCase(record, 'combobox ArrowDown opens and Escape closes', 'keyboard', expanded && await combobox.getAttribute('aria-expanded') !== 'true' ? 'passed' : 'failed', { expanded });
  }
  const sortable = frame.locator('[aria-sort] button').first();
  if (await sortable.count() && await sortable.isVisible()) {
    const owner = sortable.locator('xpath=ancestor::*[@aria-sort][1]');
    const before = await owner.getAttribute('aria-sort'); await sortable.press('Enter'); await frame.waitForTimeout(30);
    const after = await owner.getAttribute('aria-sort');
    recordCase(record, 'sort header Enter changes declared direction', 'keyboard', before !== after ? 'passed' : 'blocked', { before, after });
  }
}
async function probeTemplateStates(frame, record) {
  const stateOptions = await frame.evaluate(() => {
    if (!window.__dcRegistry || !window.__dcRootName) return [];
    return window.__dcRegistry[window.__dcRootName()]?.propsMeta?.dataState?.options || [];
  });
  if (!stateOptions.length) { recordCase(record, 'declared data-state transitions', 'state', 'blocked', 'Entry declares no dataState host contract; no synthetic pass is inferred.'); return; }
  for (const state of stateOptions) {
    await frame.evaluate(state => window.__dcSetProps(window.__dcRootName(), { dataState: state }), state);
    await frame.waitForTimeout(50);
    const snapshot = await frame.locator('body').innerText();
    const content = snapshot.trim().length > 0;
    const appropriate = state === 'ready' ? content : new RegExp(state === 'error' ? 'error|failed|unavailable|retry|wrong|attention' : state === 'loading' ? 'loading|preparing|fetching' : 'empty|no |nothing|zero', 'i').test(snapshot);
    recordCase(record, `declared dataState=${state} renders explicit state`, 'state', appropriate ? 'passed' : 'failed', { excerpt: snapshot.slice(0, 400) });
  }
  await frame.evaluate(() => window.__dcSetProps(window.__dcRootName(), { dataState: 'ready' }));
  await frame.waitForTimeout(50);
}
function locate(frame, target) {
  if (!target) throw new Error('Journey action requires a target locator');
  const scope = target.within ? locate(frame, target.within) : frame;
  let locator;
  if (target.css) locator = scope.locator(target.css);
  else if (target.role) locator = scope.getByRole(target.role, { ...(target.name !== undefined ? { name: target.name } : {}), exact: target.exact !== false });
  else if (target.label) locator = scope.getByLabel(target.label, { exact: target.exact !== false });
  else if (target.text) locator = scope.getByText(target.text, { exact: target.exact !== false });
  else throw new Error(`Unknown journey locator ${JSON.stringify(target)}`);
  return target.index !== undefined ? locator.nth(target.index) : locator;
}
async function eventually(assertion) {
  const deadline = Date.now() + 4000; let last;
  do { try { await assertion(); return; } catch (error) { last = error; await new Promise(resolve => setTimeout(resolve, 40)); } } while (Date.now() < deadline);
  throw last;
}
async function withDeadline(operation, milliseconds, message) {
  let timer;
  try { return await Promise.race([operation, new Promise((_, reject) => { timer = setTimeout(() => reject(new Error(message)), milliseconds); })]); }
  finally { clearTimeout(timer); }
}
async function probePrimaryJourney(frame, record, path) {
  let recipePath = path, recipe = journeys.entries?.[path];
  const seen = new Set();
  while (recipe?.use) {
    if (seen.has(recipePath)) throw new Error(`Circular journey recipe alias ${recipePath}`);
    seen.add(recipePath); recipePath = recipe.use; recipe = journeys.entries?.[recipePath];
  }
  record.primaryJourney = { recipe: recipePath, recipeSha256: recipe ? hash(JSON.stringify(recipe)) : null, recipeFileSha256: hash(journeyBytes), description: recipe?.description, totalSteps: recipe?.steps?.length || 0, completedSteps: 0, status: 'blocked' };
  if (!recipe?.steps?.length) { recordCase(record, 'primary operator journey', 'state', 'blocked', 'No authored template-specific primary journey recipe; generic controls and host-state probes do not certify this flow.'); return; }
  for (const [index, step] of recipe.steps.entries()) {
    try {
      const target = step.target ? locate(frame, step.target) : null;
      switch (step.action) {
        case 'click': await target.click(); break;
        case 'fill': await target.fill(String(step.value)); break;
        case 'select': await target.selectOption(String(step.value)); break;
        case 'press': await target.press(step.key || step.value); break;
        case 'assertVisible': await target.waitFor({ state: 'visible', timeout: 5000 }); break;
        case 'assertText': await eventually(async () => { const text = await target.innerText(); if (step.contains !== undefined) assert(text.includes(step.contains), `Expected ${JSON.stringify(text)} to include ${JSON.stringify(step.contains)}`); else assert.equal(text, String(step.value)); }); break;
        case 'assertValue': await eventually(async () => assert.equal(await target.inputValue(), String(step.value))); break;
        case 'assertCount': await eventually(async () => assert.equal(await target.count(), step.count)); break;
        case 'assertAttribute': await eventually(async () => assert.equal(await target.getAttribute(step.attribute), String(step.value))); break;
        case 'assertURL': await eventually(async () => assert(frame.url().includes(step.contains || step.value), `Expected URL ${frame.url()} to include ${step.contains || step.value}`)); break;
        case 'assertDisabled': {
          assert(await target.isDisabled() || await target.getAttribute('aria-disabled') === 'true', 'Consequential action must be disabled');
          if (step.reasonContains) {
            const reason = await target.evaluate(node => [node.getAttribute('title') || '', ...(node.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean).map(id => document.getElementById(id)?.textContent || '')].join(' '));
            assert(reason.includes(step.reasonContains), `Expected disabled reason ${JSON.stringify(reason)} to include ${JSON.stringify(step.reasonContains)}`);
          }
          break;
        }
        default: throw new Error(`Unsupported journey action ${step.action}`);
      }
      record.primaryJourney.completedSteps += 1;
      recordCase(record, `primary journey ${index + 1}/${recipe.steps.length}: ${step.action}`, 'state', 'passed', { recipe: recipePath, ...step });
    } catch (error) {
      record.primaryJourney.status = 'failed';
      recordCase(record, `primary journey ${index + 1}/${recipe.steps.length}: ${step.action}`, 'state', 'failed', { recipe: recipePath, ...step, error: error.message });
      return;
    }
  }
  record.primaryJourney.status = 'passed';
}
async function auditJob(job) {
  const record = { path: job.path, published: job.published, sourceSha256: hash(await readFile(resolve(root, job.path))), cases: [], referencedExports: [], renderedExports: [], runtimeErrors: [], remoteRequests: [], failedRequests: [], accessibility: [], layouts: [] };
  results[job.kind][job.path] = record;
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce', locale: 'en-US', timezoneId: 'UTC' });
  const page = await context.newPage();
  page.setDefaultTimeout(5000);
  page.on('pageerror', error => record.runtimeErrors.push(error.message));
  page.on('console', message => { if (message.type() === 'error' && !message.text().startsWith('Failed to load resource:')) record.runtimeErrors.push(message.text()); });
  page.on('requestfailed', request => { if (!request.failure()?.errorText.includes('ERR_ABORTED')) record.failedRequests.push({ url: request.url().replace(base, ''), error: request.failure()?.errorText }); });
  page.on('response', response => { if (response.status() >= 400) record.failedRequests.push({ url: response.url().replace(base, ''), status: response.status() }); });
  await page.route('**/*', route => {
    const url = new URL(route.request().url());
    if (url.protocol === 'data:' || url.protocol === 'blob:' || url.origin === base) return route.continue();
    record.remoteRequests.push(route.request().url()); return route.abort();
  });
  try {
    await page.goto(`${base}/${job.path}`, { waitUntil: 'load', timeout: 15000 });
    await waitForContent(page);
    const compatibility = await page.locator('iframe[data-meridian-compatibility-target]').count() > 0;
    if (compatibility) await probeTemplateStates(page.mainFrame(), record);
    const frame = await contentFrame(page);
    record.contentUrl = frame.url().replace(base, '');
    const text = await frame.locator('body').innerText();
    const overlay = await frame.locator('.sc-logic-error,.sc-placeholder-error').allTextContents();
    const meaningful = await frame.evaluate(inspectContent);
    recordCase(record, 'authored content renders with local runtime', 'render', record.runtimeErrors.length === 0 && overlay.length === 0 && meaningful ? 'passed' : 'failed', { excerpt: text.slice(0, 450), meaningfulMediaOrText: meaningful, overlays: overlay });
    if (!record.runtimeErrors.length && !overlay.length) {
      if (job.kind === 'templates') await probePrimaryJourney(frame, record, job.path);
      if(job.path==='components/charts/viz-workbench.card.html') {
        try {
          const trigger=frame.getByRole('button',{name:'Open lineage',exact:true});
          await trigger.click();
          await frame.getByRole('dialog',{name:'Synthetic curve lineage',exact:true}).waitFor({state:'visible'});
          await page.keyboard.press('Escape');
          await frame.getByRole('dialog',{name:'Synthetic curve lineage',exact:true}).waitFor({state:'hidden'});
          assert(await trigger.evaluate(node=>node===document.activeElement),'Closing the lineage drawer restores its actual trigger focus');
          recordCase(record,'Open lineage opens its named shared Drawer; Escape closes and restores trigger focus','keyboard','passed');
        }catch(error){recordCase(record,'Open lineage opens its named shared Drawer; Escape closes and restores trigger focus','keyboard','failed',error.message);}
      }
      await probeKeyboard(frame, record);
      if (job.kind === 'templates' && !compatibility) await probeTemplateStates(frame, record);
      const themes = ['light', 'dark'];
      for (const theme of themes) {
        await setAppearance(frame, theme,'default',job.kind==='templates');
        await frame.addScriptTag({ content: axe });
        const a11y = await frame.evaluate(async () => {
          const result = await axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] } });
          return { passes: result.passes.length, incomplete: result.incomplete.map(item => item.id), violations: result.violations.map(item => ({ id: item.id, impact: item.impact, nodes: item.nodes.map(node => ({ target: node.target, summary: node.failureSummary })) })) };
        });
        record.accessibility.push({ theme, ...a11y });
        recordCase(record, `${theme} automated WCAG A/AA`, 'accessibility', a11y.violations.length ? 'failed' : 'passed', { violations: a11y.violations.map(item => ({ id: item.id, nodes: item.nodes.length })) });
        for (const [width, height] of matrix === 'smoke' ? [[1440, 900]] : [[1920, 1080], [1440, 900], [1366, 768]]) {
          await page.setViewportSize({ width, height });
          await settleAppearance(frame);
          const layout = await frame.evaluate(() => ({ width: innerWidth, scrollWidth: document.documentElement.scrollWidth, bodyWidth: document.body.scrollWidth, focusable: document.querySelectorAll('button,input,select,textarea,[tabindex="0"]').length }));
          const screenshot = resolve(out, 'screenshots', `${job.path.replace(/[^a-z0-9.-]/gi, '_')}.${theme}.${width}x${height}.png`);
          await page.screenshot({path:screenshot,fullPage:false});
          record.layouts.push({ theme, width, height, ...layout, screenshot });
          recordCase(record, `${theme} ${width}×${height} root overflow`, 'layout', layout.scrollWidth <= layout.width ? 'passed' : 'failed', layout);
        }
        if (matrix !== 'smoke') {
          await setDesktopZoomEquivalent(page,true);
          await settleAppearance(frame);
          const zoom = await frame.evaluate(() => ({ width: innerWidth, scrollWidth: document.documentElement.scrollWidth, overflow: [...document.querySelectorAll('body *')].filter(node=>node.getBoundingClientRect().right>innerWidth+1).sort((a,b)=>b.getBoundingClientRect().width-a.getBoundingClientRect().width).slice(0,12).map(node=>({tag:node.tagName,role:node.getAttribute('role'),className:typeof node.className==='string'?node.className:'svg',text:node.textContent.slice(0,70),right:node.getBoundingClientRect().right,width:node.getBoundingClientRect().width,scrollWidth:node.scrollWidth,clientWidth:node.clientWidth,html:node.outerHTML.slice(0,300)})) }));
          zoom.topViewport=await page.evaluate(()=>({width:innerWidth,scrollWidth:document.documentElement.scrollWidth,dpr:devicePixelRatio}));
          const screenshot = resolve(out, 'screenshots', `${job.path.replace(/[^a-z0-9.-]/gi, '_')}.${theme}.200percent.png`);
          await captureDesktopZoom(page,screenshot);
          record.layouts.push({theme,zoom:2,model:'desktop zoom layout equivalent',physicalWidth:1366,physicalHeight:768,...zoom,screenshot});
          recordCase(record, `${theme} 200% desktop zoom layout equivalent root overflow`, 'layout', zoom.topViewport.width===683&&zoom.topViewport.scrollWidth<=683&&zoom.scrollWidth<=zoom.width ? 'passed' : 'failed', zoom);
          await setDesktopZoomEquivalent(page,false); await settleAppearance(frame);
        }
      }
      for (const density of ['terminal', 'compact', 'default', 'spacious']) {
        await setAppearance(frame, 'light', density,job.kind==='templates');
        const geometry = await frame.evaluate(() => {
          const firstControl = [...document.querySelectorAll('button,input,select,textarea,[role="row"]')].find(node => node.getBoundingClientRect().height > 0);
          return { density: document.body.dataset.themeDensity, hasContent: document.body.innerText.trim().length > 0, width: innerWidth, scrollWidth: document.documentElement.scrollWidth, firstControlHeight: firstControl?.getBoundingClientRect().height ?? null };
        });
        geometry.hasContent = await frame.evaluate(inspectContent);
        recordCase(record, `density=${density} remains rendered and contained`, 'layout', geometry.hasContent && geometry.scrollWidth <= geometry.width ? 'passed' : 'failed', geometry);
      }
      await page.screenshot({ path: resolve(out, 'screenshots', job.path.replace(/[^a-z0-9.-]/gi, '_') + '.png'), fullPage: false });
    }
    const coverage = await frame.evaluate(() => ({ referenced: [...(window.__audit?.referenced || [])], rendered: [...(window.__audit?.rendered || [])] }));
    record.referencedExports = coverage.referenced.filter(name => manifest.components.some(component => component.name === name));
    record.renderedExports = coverage.rendered.filter(name => manifest.components.some(component => component.name === name));
    for (const name of record.referencedExports) {
      const evidence = results.exports[name] ||= { referencedBy: [], renderedBy: [], cases: [] };
      evidence.referencedBy.push(job.path);
      if (record.renderedExports.includes(name)) evidence.renderedBy.push(job.path);
    }
  } catch (error) { recordCase(record, 'browser audit completes', 'render', 'failed', error.message); }
  finally {
    recordCase(record, 'no runtime exceptions', 'render', record.runtimeErrors.length ? 'failed' : 'passed', record.runtimeErrors);
    recordCase(record, 'no external requests', 'contract', record.remoteRequests.length ? 'failed' : 'passed', record.remoteRequests);
    recordCase(record, 'no failed local assets', 'contract', record.failedRequests.length ? 'failed' : 'passed', record.failedRequests);
    await context.close();
    await writeFile(resolve(out, 'browser-results.json'), JSON.stringify(results, null, 2) + '\n');
    console.log(`${job.kind} ${job.path}: ${record.cases.filter(test => test.status === 'passed').length} passed, ${record.cases.filter(test => test.status === 'failed').length} failed, ${record.cases.filter(test => test.status === 'blocked').length} blocked`);
  }
}

async function auditThemes() {
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce' });
  const page = await context.newPage();
  try {
    await page.goto(`${base}/__audit/theme.html`); await waitForContent(page);
    const publicExports = await page.evaluate(({ namespace, names }) => names.map(name => {
      const value = window[namespace]?.[name];
      return { name, present: Object.hasOwn(window[namespace] || {}, name), runtimeKind: Array.isArray(value) ? 'array' : typeof value, members: value && typeof value === 'object' && !Array.isArray(value) ? Object.keys(value).slice(0, 30) : undefined };
    }), { namespace: manifest.namespace, names: manifest.components.map(component => component.name) });
    for (const observed of publicExports) {
      const evidence = results.exports[observed.name] ||= { referencedBy: [], renderedBy: [], cases: [] };
      evidence.runtimeKind = observed.runtimeKind;
      evidence.members = observed.members;
      evidence.cases.push({ name: 'declared compiled export exists', kind: 'contract', status: observed.present ? 'passed' : 'failed', detail: { runtimeKind: observed.runtimeKind, members: observed.members } });
    }
    await page.addScriptTag({ content: axe });
    for (const theme of manifest.themes) {
      const configured = await page.evaluate(({ selector, label }) => {
        delete document.documentElement.dataset.brand;
        document.documentElement.dataset.theme = 'light';
        document.body.dataset.themeDensity = 'default';
        const match = selector.match(/\[([^=]+)="([^"]+)"\]/);
        if (!match) return { selector, label, error: 'Unsupported manifest theme selector' };
        const element = selector.startsWith('body') ? document.body : document.documentElement;
        element.setAttribute(match[1], match[2]);
        return {selector,label};
      }, theme);
      await settleAppearance(page);
      const observed = configured.error ? configured : await page.evaluate(async ({selector,label}) => {
        const element = selector.startsWith('body') ? document.body : document.documentElement;
        const roleNames = ['--bg', '--bg-light', '--text-primary', '--text-secondary', '--brand-axis', '--interaction-focus', '--theme-row-height'];
        const roles = Object.fromEntries(roleNames.map(name => [name, getComputedStyle(element).getPropertyValue(name).trim()]));
        const canvas = document.createElement('canvas'); canvas.width=canvas.height=1; const context=canvas.getContext('2d',{willReadFrequently:true});
        const rgba = color => {context.clearRect(0,0,1,1);context.fillStyle=color;context.fillRect(0,0,1,1);return [...context.getImageData(0,0,1,1).data].map((value,index)=>index===3?value/255:value/255);};
        const over=(top,bottom)=>[0,1,2].map(index=>top[index]*top[3]+bottom[index]*(1-top[3]));
        const luminance=rgb=>rgb.reduce((sum,value,index)=>sum+[.2126,.7152,.0722][index]*(value<=.04045?value/12.92:((value+.055)/1.055)**2.4),0);
        const body=rgba(getComputedStyle(document.body).backgroundColor), root=rgba(getComputedStyle(document.documentElement).backgroundColor);
        const surface=over(body,over(root,[1,1,1]));
        const semanticChips=[...document.querySelectorAll('[data-token-pair]')].map(node=>{
          const style=getComputedStyle(node), bg=over(rgba(style.backgroundColor),surface), fg=over(rgba(style.color),bg), a=luminance(fg), b=luminance(bg), contrast=(Math.max(a,b)+.05)/(Math.min(a,b)+.05);
          return {role:node.dataset.tokenPair,color:style.color,background:style.backgroundColor,contrast,status:contrast>=4.5?'passed':'failed'};
        });
        const result = await axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'] } });
        return { selector, label, roles, semanticChips, violations: result.violations.map(item => ({ id: item.id, nodes: item.nodes.map(node => ({ target: node.target, summary: node.failureSummary })) })) };
      }, theme);
      results.themes.push({ ...observed, status: observed.error || observed.violations?.length || observed.semanticChips?.some(chip=>chip.status==='failed') ? 'failed' : 'passed' });
    }
  } catch (error) { results.themes.push({ status: 'failed', error: error.message }); }
  finally { await context.close(); }
}

async function auditExistingHarnesses() {
  for (const path of ['tests/unit.html', 'tests/semantic-safety.html', 'tests/books-loop.html', 'tests/action-contract.html', 'tests/conformance.html', 'tests/a11y-run.html']) {
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce' });
    const page = await context.newPage();
    const record = { path, sourceSha256: hash(await readFile(resolve(root,path))), cases: [], runtimeErrors: [] }; results.harnesses[path] = record;
    page.on('pageerror', error => record.runtimeErrors.push(error.message));
    try {
      await page.goto(`${base}/${path}`, { waitUntil: 'load' });
      if (path.endsWith('a11y-run.html')) {
        await page.waitForFunction(() => !!window.__a11yRun, null, { timeout: 10000 });
        await withDeadline(page.evaluate(async () => { if (!window.__a11yRun.started) await window.__a11yRun.runAll(); }), 480000, 'Authored accessibility journey did not finish within eight minutes');
      }
      await page.waitForFunction(() => window.__testResults || window.__conformance || window.__a11y || /^(?:PASS|FAIL)/.test(document.title), null, { timeout: path.endsWith('a11y-run.html') ? 480000 : 45000 });
      const result = await page.evaluate(() => ({ title: document.title, testResults: window.__testResults || null, conformance: window.__conformance || null, a11y: window.__a11y || null, text: document.body.innerText.slice(-2500) }));
      record.result = result;
      const failed = result.testResults?.fail || result.testResults?.fails || result.testResults?.failed || result.a11y?.counts.FAIL || result.conformance?.blocked?.length || /^FAIL/.test(result.title) || record.runtimeErrors.length;
      recordCase(record, 'existing authored contract assertions', 'contract', failed ? 'failed' : result.a11y?.counts.OPEN ? 'blocked' : result.testResults || result.a11y || /^PASS/.test(result.title) ? 'passed' : 'blocked', result);
    } catch (error) { recordCase(record, 'existing harness completes', 'contract', 'failed', error.message); }
    finally { await context.close(); await writeFile(resolve(out, 'browser-results.json'), JSON.stringify(results, null, 2) + '\n'); console.log(`harness ${path}: ${record.cases[0]?.status}`); }
  }
}

try {
  if(matrix==='full') await auditZoomControls();
  let cursor = 0;
  await Promise.all(Array.from({ length: Math.min(concurrency, jobs.length) }, async () => { while (cursor < jobs.length) await auditJob(jobs[cursor++]); }));
  if (selection === 'all' || selection === 'contracts') { await auditThemes(); await auditExistingHarnesses(); }
  const finalRuntimeSourceHashes=await snapshotRuntimeSources();
  const changedRuntimeSources=[...new Set([...Object.keys(runtimeSourceHashes),...Object.keys(finalRuntimeSourceHashes)])].filter(path=>runtimeSourceHashes[path]!==finalRuntimeSourceHashes[path]);
  results.source.runtimeSourceHashesStable=changedRuntimeSources.length===0;
  results.source.changedRuntimeSources=changedRuntimeSources;
  results.source.finishedAt=new Date().toISOString();
  recordCase(results.integrity,'runtime sources remain unchanged throughout the audit','contract',changedRuntimeSources.length?'failed':'passed',changedRuntimeSources);
  for (const component of manifest.components) results.exports[component.name] ||= { referencedBy: [], renderedBy: [], cases: [{ name: 'behavior fixture coverage', kind: 'contract', status: 'blocked', detail: 'No browser fixture referenced this export; source/logic review or an explicit fixture is required.' }] };
  results.summary = { cards: Object.keys(results.cards).length, templates: Object.keys(results.templates).length, exports: Object.keys(results.exports).length, cases: {} };
  for (const result of [...Object.values(results.cards), ...Object.values(results.templates)]) for (const test of result.cases) results.summary.cases[test.status] = (results.summary.cases[test.status] || 0) + 1;
  for (const result of Object.values(results.harnesses)) for (const test of result.cases) results.summary.cases[test.status] = (results.summary.cases[test.status] || 0) + 1;
  for (const test of results.integrity.cases) results.summary.cases[test.status] = (results.summary.cases[test.status] || 0) + 1;
  for (const theme of results.themes) results.summary.cases[theme.status] = (results.summary.cases[theme.status] || 0) + 1;
  results.summary.requiredJourneyGaps = Object.values(results.templates).filter(result => result.primaryJourney?.status !== 'passed').map(result => result.path);
  await writeFile(resolve(out, 'browser-results.json'), JSON.stringify(results, null, 2) + '\n');
  console.log(JSON.stringify(results.summary));
  if (results.summary.cases.failed || results.summary.requiredJourneyGaps.length) process.exitCode = 1;
} finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
