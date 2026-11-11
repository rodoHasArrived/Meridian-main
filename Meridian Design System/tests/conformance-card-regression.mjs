#!/usr/bin/env node
// Prove the specimen assertions accept real static content and reject real defects.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const rules = await readFile(resolve(root, 'tests/conformance-rules.js'), 'utf8');
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });
const page = await browser.newPage({ viewport: { width: 1180, height: 820 }, reducedMotion: 'no-preference' });
const image = `data:image/svg+xml,${encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="80" height="40"><rect width="80" height="40" fill="blue"/></svg>')}`;

async function run(id, markup, css = '') {
  await page.setContent(`<!doctype html><html lang="en"><head><title>Assertion fixture</title><style data-mds="fixture">${css}</style></head><body>${markup}</body></html>`);
  await page.addScriptTag({ content: rules });
  await page.evaluate(async () => { await Promise.all([...document.images].map(image => image.decode().catch(() => undefined))); });
  if (await page.locator('[autofocus]').count()) await page.locator('[autofocus]').focus();
  return page.evaluate(id => window.MeridianConformance.CARD_ASSERTIONS.find(rule => rule.id === id).run(document, window), id);
}

try {
  await page.route('http://meridian.test/**', route => route.fulfill({
    contentType: route.request().url().endsWith('.css') ? 'text/css' : 'text/html',
    body: route.request().url().endsWith('.css') ? '.probe{--imported-geometry:2px}' : '<!doctype html><html><body></body></html>'
  }));
  await page.goto('http://meridian.test/checker');
  assert.equal(await run('mounted', '<p>A small static specimen with meaningful rendered typography.</p>'), null);
  assert.equal(await run('mounted', `<img src="${image}" alt="Meridian brand mark">`), null);
  assert.equal(await run('mounted', '<svg width="80" height="40" role="img" aria-label="Exposure profile"><rect width="80" height="40"/></svg>'), null);
  assert.equal(await run('mounted', '<svg width="80" height="40"><title>Exposure profile</title><path d="M0 0L80 40"/></svg>'), null);
  assert.equal(await run('mounted', '<p id="label">Exposure profile</p><svg width="80" height="40" aria-labelledby="label"><circle cx="20" cy="20" r="10"/></svg>'), null);
  console.log('PASS specimen mount assertion: small text and loaded named media count as rendered content');

  assert(await run('mounted', '<div id="root"></div>'));
  assert(await run('mounted', `${'<div></div>'.repeat(16)}<script>window.fixtureNoise = 'This long script is not rendered specimen content.';</script>`));
  assert(await run('mounted', '<img src="data:image/png;base64,not-a-valid-image" alt="Missing evidence figure">'));
  assert(await run('mounted', `<img src="${image}" alt="">`));
  assert(await run('mounted', `<img src="${image}" alt="Hidden figure" style="display:none">`));
  assert(await run('mounted', '<svg width="80" height="40" aria-label="Empty graphic"></svg>'));
  console.log('PASS specimen mount assertion: blank, script-only, failed, decorative and hidden media remain failures');

  assert.equal(await run('transition-all', '<button type="button">Default transition</button>'), null);
  assert.equal(await run('transition-all', '<div class="probe">Instant change</div>', '.probe{transition:all 0s 200ms}'), null);
  assert.equal(await run('transition-all', '<div class="probe">Bounded change</div>', '.probe{transition:color 150ms,opacity 200ms}'), null);
  assert(await run('transition-all', '<div class="probe">Unbounded change</div>', '.probe{transition:all 150ms}'));
  assert(await run('transition-all', '<div class="probe">Unbounded second property</div>', '.probe{transition:opacity 0s,all 200ms}'));
  assert(await run('transition-all', '<div class="probe">Repeated duration list</div>', '.probe{transition-property:color,all;transition-duration:100ms}'));
  assert.equal(await run('transition-all', '<div class="probe">Instant all, bounded opacity</div>', '.probe{transition:all 0s,opacity 200ms}'), null);
  console.log('PASS specimen transition assertion: only active all-property transitions fail, including repeated CSS lists');

  assert.equal(await run('tokens-resolve', '<div class="probe">Declared on the component</div>', '.probe{--local-geometry:4px;padding:var(--local-geometry)}'), null);
  assert.equal(await run('tokens-resolve', '<div class="probe" style="--column-count:3">Declared inline</div>', '.probe{padding:calc(var(--column-count)*1px)}'), null);
  assert.equal(await run('tokens-resolve', '<div class="probe">Declared in an imported stylesheet</div>', '@import url("/fixture-tokens.css");.probe{padding:var(--imported-geometry)}'), null);
  const missing = await run('tokens-resolve', '<div class="probe">An undeclared role</div>', '.probe{--actual-role:4px;padding:var(--actual-role);color:var(--never-published-role)}');
  assert.match(missing, /--never-published-role/);
  const fabricated = await run('tokens-resolve', '<div class="probe">Text is not a declaration</div>', '.probe{content:"--fabricated-role: red";color:var(--fabricated-role)}');
  assert.match(fabricated, /--fabricated-role/);
  assert(await run('tokens-resolve', '<div class="unrelated"></div><div class="probe"></div>', '.unrelated{--unrelated-role:4px}.probe{padding:var(--unrelated-role)}'));
  assert(await run('tokens-resolve', '<div class="unrelated" style="--unrelated-inline:4px"></div><div class="probe"></div>', '.probe{padding:var(--unrelated-inline)}'));
  assert(await run('tokens-resolve', '<div class="probe"></div>', '.probe{color:var(--active-typo,red)}'));
  assert.equal(await run('tokens-resolve', '<div class="probe"></div>', '.inactive-variant{color:var(--inactive-role)}'), null);
  assert.equal(await run('tokens-resolve', '<div class="probe"></div>', '.probe{content:"var(--quoted-label)"}'), null);
  assert.equal(await run('tokens-resolve', '<div class="probe"></div>', '@media(min-width:2000px){.probe{color:var(--inactive-media-role)}}'), null);
  assert(await run('tokens-resolve', '<div class="probe"></div>', '@media(min-width:1px){.probe{color:var(--active-media-role)}}'));
  assert.equal(await run('tokens-resolve', '<div class="outer"><div class="probe"></div></div>', '.outer{--nested-role:4px;& .probe{padding:var(--nested-role)}}'), null);
  assert(await run('tokens-resolve', '<div class="outer"><div class="probe"></div></div>', '.outer{& .probe{padding:var(--missing-nested-role)}}'));
  assert.equal(await run('tokens-resolve', '<div class="probe"></div>', '.probe{--pseudo-role:blue}.probe::before{content:"x";color:var(--pseudo-role)}'), null);
  assert(await run('tokens-resolve', '<div class="probe"></div>', '.probe::before{content:"x";color:var(--missing-pseudo-role)}'));
  assert(await run('tokens-resolve', '<div class="probe"></div>', '.probe{animation:probe 1s linear infinite}@keyframes probe{from{color:var(--missing-animation-role)}to{color:blue}}'));
  console.log('PASS specimen token assertion: active local/inherited/imported scopes resolve; unrelated declarations and real typos still fail');

  assert.equal(await run('focus-visible', '<button type="button">Native focus</button>'), null);
  assert.equal(await run('focus-visible', '<button type="button">Keyboard modality</button>', 'button{outline:none}button:focus-visible{outline:2px solid blue}'), null);
  assert.equal(await run('focus-visible', '<div class="field"><input aria-label="Compound field"></div>', 'input{outline:none}.field:focus-within{outline:2px solid blue}'), null);
  assert.equal(await run('focus-visible', '<div class="field"><input aria-label="Shadow field"></div>', 'input{outline:none}.field:focus-within{box-shadow:0 0 0 2px blue}'), null);
  assert.equal(await run('focus-visible', '<div class="field"><input autofocus aria-label="Initially focused field"></div>', 'input{outline:none}.field:focus-within{outline:2px solid blue}'), null);
  for (const unavailable of ['<button disabled type="button">Disabled</button>', '<div style="display:none"><button type="button">Hidden ancestor</button></div>', '<button style="visibility:hidden" type="button">Invisible</button>', '<div inert><button type="button">Locked</button></div>']) {
    assert.equal(await run('focus-visible', `${unavailable}<button id="ready" type="button">Ready control</button>`), null);
    assert.equal(await page.evaluate(() => document.activeElement.id), 'ready');
  }
  assert(await run('focus-visible', '<button type="button">No focus indicator</button>', 'button{outline:none;box-shadow:none}'));
  assert(await run('focus-visible', '<div class="field"><input aria-label="Unindicated control"></div>', 'input{outline:none;box-shadow:none}.field{outline:2px solid red;box-shadow:0 2px 5px black}'));
  console.log('PASS specimen focus assertion: eligible controls and focus-dependent wrappers count; absent indicators remain failures');
} finally {
  await browser.close();
}
