#!/usr/bin/env node
// Source regressions for calendar field feedback and blocking-overlay focus.
// CORE_INTEGRATION_SOURCE_REF can verify that each check fails on a pre-patch export.
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import { dirname, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const sourceRef = process.env.CORE_INTEGRATION_SOURCE_REF;
const changed = new Set(['DatePicker.jsx', 'DateRangePicker.jsx', 'localDate.js', 'useOverlayFocus.js']);
const contents = `
import React from 'react';
import {DatePicker} from './components/core/DatePicker.jsx';
import {DateRangePicker} from './components/core/DateRangePicker.jsx';
import {useOverlayFocus, useOverlayEscape} from './components/core/useOverlayFocus.js';
import * as dates from './components/core/localDate.js';
window.coreFixture={React,DatePicker,DateRangePicker,useOverlayFocus,useOverlayEscape,dates};`;
const plugins = [{ name: 'react-peer', setup(builder) {
  builder.onResolve({ filter: /^react$/ }, () => ({ path: 'react', namespace: 'peer' }));
  builder.onLoad({ filter: /.*/, namespace: 'peer' }, () => ({ contents: 'export default window.React;', loader: 'js' }));
  if (sourceRef) builder.onLoad({ filter: /components\/core\/[^/]+\.(jsx|js)$/ }, ({ path }) => {
    if (!changed.has(path.split('/').at(-1))) return;
    const file = `Meridian Design System/${relative(root, path).replaceAll('\\', '/')}`;
    return { contents: execFileSync('git', ['show', `${sourceRef}:${file}`], { cwd: root, encoding: 'utf8' }), loader: path.endsWith('.jsx') ? 'jsx' : 'js' };
  });
} }];
const compiled = await build({ absWorkingDir: root, stdin: { contents, resolveDir: root, loader: 'js' },
  bundle: true, write: false, format: 'iife', platform: 'browser', plugins,
  jsxFactory: 'React.createElement', jsxFragment: 'React.Fragment', logLevel: 'error' });
const runtime = await readFile(resolve(root, '_preview_runtime.js'), 'utf8');
const browser = await chromium.launch({ executablePath: process.env.MERIDIAN_CHROMIUM_PATH || '/usr/bin/chromium', args: ['--no-sandbox'] });
const failures = [];
const results = [];
try {
  for (const timezoneId of ['America/Los_Angeles', 'Pacific/Auckland']) {
    const context = await browser.newContext({ timezoneId, viewport: { width: 1440, height: 900 } });
    const page = await context.newPage();
    const errors = [];
    page.setDefaultTimeout(5000);
    page.on('pageerror', error => errors.push(error.message));
    await page.setContent('<!doctype html><html lang="en"><head><title>Core integration contracts</title></head><body><button id="trigger">Outside trigger</button><main><h1>Core contracts</h1><div id="fixture"></div></main><button id="after">After fields</button></body></html>');
    await page.addScriptTag({ content: runtime });
    await page.addScriptTag({ content: compiled.outputFiles[0].text });
    await page.evaluate(() => {
      const f = window.coreFixture, h = f.React.createElement;
      const root = window.ReactDOM.createRoot(document.getElementById('fixture'));
      window.events = []; window.revision = 0;
      function Overlay({ empty = false, autofocus = false }) {
        const ref = f.React.useRef(null);
        f.useOverlayFocus(ref, true);
        f.useOverlayEscape(true, () => window.events.push('escape'));
        return h('div', { ref, role: 'dialog', 'aria-label': 'Review evidence', 'data-panel': true },
          h('h2', { tabIndex: -1, ...(autofocus ? { 'data-autofocus': true } : {}) }, 'Review heading'),
          h('button', { tabIndex: -2 }, 'Programmatic action'),
          h('div', { hidden: true }, h('button', null, 'Hidden action')),
          h('div', { inert: true }, h('button', null, 'Inert action')),
          h('div', { style: { display: 'none' } }, h('button', null, 'CSS hidden action')),
          h('fieldset', { disabled: true }, h('button', null, 'Disabled fieldset action')),
          !empty && h('button', null, 'First action'),
          !empty && h('details', null, h('summary', null, 'Collapsed evidence'), h('button', null, 'Collapsed action')),
          !empty && h('button', { style: { position: 'fixed', top: 30, right: 30 } }, 'Last fixed action'));
      }
      function Demo({ kind, revision }) {
        const [date, setDate] = f.React.useState('2026-01-31');
        const [range, setRange] = f.React.useState({ start: '2026-01-31', end: '2026-02-02' });
        f.React.useLayoutEffect(() => { document.getElementById('fixture').dataset.revision = String(revision); }, [revision]);
        if (kind === 'empty') return h('p', null, 'No overlay');
        if (kind === 'overlay' || kind === 'autofocus' || kind === 'empty-overlay') return h(Overlay, { autofocus: kind === 'autofocus', empty: kind === 'empty-overlay' });
        return h(f.React.Fragment, null,
          h('span', { id: 'external-name' }, 'Settlement date'), h('span', { id: 'help' }, 'Use the agreed settlement date.'), h('span', { id: 'validation' }, 'Settlement requires review.'),
          h(f.DatePicker, { id: 'settlement', value: date, onChange: next => { setDate(next); window.events.push(next); },
            'aria-labelledby': 'external-name', 'aria-describedby': 'help', 'aria-invalid': true, 'aria-errormessage': 'validation', 'aria-required': true }),
          h('label', { htmlFor: 'period' }, 'Reporting period'),
          h(f.DateRangePicker, { id: 'period', start: range.start, end: range.end, onChange: next => { setRange(next); window.events.push(next); }, months: 1, showPresets: false,
            'aria-describedby': 'help', 'aria-invalid': true, 'aria-errormessage': 'validation', 'aria-required': true, error: 'Range requires review.' }));
      }
      window.mountCore = kind => { const revision = ++window.revision; window.events = []; root.render(h(Demo, { kind, revision, key: revision })); return revision; };
    });
    const settle = () => page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    async function mount(kind) {
      const revision = await page.evaluate(kind => window.mountCore(kind), kind);
      await page.waitForFunction(revision => document.getElementById('fixture').dataset.revision === String(revision), revision);
      await settle();
    }
    async function check(name, work) {
      try { await work(); results.push(`${timezoneId}: ${name}`); }
      catch (error) { failures.push(`${timezoneId}: ${name}: ${error.message}`); }
    }
    await check('calendar civil dates retain exact four-digit years and month arithmetic', async () => {
      const actual = await page.evaluate(() => {
        const d = window.coreFixture.dates;
        return {
          modern: d.toISO(d.fromISO('2026-01-31')),
          clamped: d.toISO(d.addMonths(d.fromISO('2026-01-31'), 1)),
          early: ['0000-02-29', '0099-01-31'].map(value => { const date = d.fromISO(value); return date ? d.toISO(date) : null; }),
          earlyMonth: d.fromISO('0099-01-31') && d.toISO(d.addMonths(d.fromISO('0099-01-31'), 1)),
          impossible: d.fromISO('2026-02-30'),
          firstGridYear: d.fromISO('0099-01-31') && d.weeksFor(d.fromISO('0099-01-31')).flat().find(date => date.getDate() === 15).getFullYear(),
        };
      });
      assert.deepEqual(actual, { modern: '2026-01-31', clamped: '2026-02-28', early: ['0000-02-29', '0099-01-31'], earlyMonth: '0099-02-28', impossible: null, firstGridYear: 99 });
    });
    await check('calendar field names and validation describe the actual controls', async () => {
      await mount('dates');
      const date = page.getByRole('textbox', { name: 'Settlement date' });
      assert.equal(await date.getAttribute('id'), 'settlement');
      assert.equal(await date.getAttribute('aria-describedby'), 'help');
      assert.equal(await date.getAttribute('aria-invalid'), 'true');
      assert.equal(await date.getAttribute('aria-errormessage'), 'validation');
      assert.equal(await date.getAttribute('aria-required'), 'true');
      const range = page.getByRole('button', { name: 'Reporting period', exact: true });
      assert.equal(await range.getAttribute('id'), 'period');
      assert.equal(await range.getAttribute('aria-describedby'), 'help period-error');
      assert.equal(await range.getAttribute('aria-invalid'), 'true');
      assert.equal(await range.getAttribute('aria-errormessage'), 'validation');
      assert.equal(await range.getAttribute('aria-required'), 'true');
      await date.press('ArrowDown');
      const day = page.locator('.mds-date-day[tabindex="0"]');
      await day.press('PageDown');
      assert.match(await page.locator('.mds-date-day[tabindex="0"]').getAttribute('aria-label'), /February 28, 2026/);
      await page.locator('.mds-date-day[tabindex="0"]').press('Enter');
      assert.equal(await date.inputValue(), '2026-02-28');
      assert.equal(await page.evaluate(() => window.events.at(-1)), '2026-02-28');
    });
    await check('overlay starts at an available control and excludes unavailable Tab stops', async () => {
      await mount('empty'); await page.locator('#trigger').focus(); await mount('overlay');
      assert.equal(await page.evaluate(() => document.activeElement.textContent), 'First action');
      await page.keyboard.press('Shift+Tab');
      assert.equal(await page.evaluate(() => document.activeElement.textContent), 'Last fixed action');
      await page.keyboard.press('Tab');
      assert.equal(await page.evaluate(() => document.activeElement.textContent), 'First action');
      await page.keyboard.press('Tab');
      assert.equal(await page.evaluate(() => document.activeElement.textContent), 'Collapsed evidence');
      await page.keyboard.press('Tab');
      assert.equal(await page.evaluate(() => document.activeElement.textContent), 'Last fixed action');
      await page.keyboard.press('Escape');
      assert.deepEqual(await page.evaluate(() => window.events), ['escape']);
      assert.equal(await page.evaluate(() => document.body.style.overflow), 'hidden');
      await mount('empty');
      assert.equal(await page.evaluate(() => document.activeElement.id), 'trigger');
      assert.equal(await page.evaluate(() => document.body.style.overflow), '');
    });
    await check('intentional heading focus is initial only and empty panels remain focusable', async () => {
      await page.locator('#trigger').focus(); await mount('autofocus');
      assert.equal(await page.evaluate(() => document.activeElement.textContent), 'Review heading');
      await page.keyboard.press('Tab');
      assert.equal(await page.evaluate(() => document.activeElement.textContent), 'First action');
      await mount('empty'); await page.locator('#trigger').focus(); await mount('empty-overlay');
      assert.equal(await page.evaluate(() => document.activeElement.hasAttribute('data-panel')), true);
      await page.keyboard.press('Tab');
      assert.equal(await page.evaluate(() => document.activeElement.hasAttribute('data-panel')), true);
      await mount('empty');
      assert.equal(await page.evaluate(() => document.activeElement.id), 'trigger');
    });
    assert.deepEqual(errors, [], 'No browser runtime errors');
    await context.close();
  }
} finally { await browser.close(); }
results.forEach(result => console.log(`PASS ${result}`));
if (failures.length) { failures.forEach(result => console.error(`FAIL ${result}`)); process.exitCode = 1; }
else console.log(`Core integration: ${results.length} source contracts passed.`);
