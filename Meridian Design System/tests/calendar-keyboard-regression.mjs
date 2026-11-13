#!/usr/bin/env node
// Nonmodal calendars retain native button/Tab behavior. CALENDAR_SOURCE_REF=HEAD proves pre-fix failure.
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { dirname, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';
import { chromium } from 'playwright';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const sourceRef = process.env.CALENDAR_SOURCE_REF;
const compiled = await build({ absWorkingDir: root,
  stdin: { contents: `import React from 'react';import {createRoot} from 'react-dom/client';import {DatePicker} from './components/core/DatePicker.jsx';import {DateRangePicker} from './components/core/DateRangePicker.jsx';window.fixture={React,createRoot,DatePicker,DateRangePicker};`, resolveDir: root, loader: 'jsx' },
  bundle: true, write: false, format: 'iife', platform: 'browser', define: { 'process.env.NODE_ENV': '"production"' }, logLevel: 'error',
  plugins: sourceRef ? [{ name: 'pre-fix-calendars', setup(builder) {
    builder.onLoad({ filter: /components\/core\/Date(?:Range)?Picker\.jsx$/ }, ({ path }) => ({ contents: execFileSync('git', ['show', `${sourceRef}:Meridian Design System/${relative(root, path).replaceAll('\\', '/')}`], { cwd: root, encoding: 'utf8' }), loader: 'jsx' }));
  } }] : [],
});
const browser = await chromium.launch({ executablePath: process.env.MERIDIAN_CHROMIUM_PATH || '/usr/bin/chromium', args: ['--no-sandbox'] });
const context = await browser.newContext({ timezoneId: 'UTC', viewport: { width: 1366, height: 768 } });
const page = await context.newPage(); page.setDefaultTimeout(5000);
const errors = []; page.on('pageerror', error => errors.push(error.message));
try {
  await page.setContent('<!doctype html><html lang="en"><title>Calendar keyboard contracts</title><main><h1>Synthetic calendars</h1><div id="fixture"></div></main><button id="after">After calendars</button></html>');
  await page.addScriptTag({ content: compiled.outputFiles[0].text });
  await page.evaluate(() => {
    const { React, createRoot, DatePicker, DateRangePicker } = window.fixture, h = React.createElement, root = createRoot(document.getElementById('fixture'));
    window.revision = 0; window.events = [];
    function Demo({ kind, revision, months, limits }) {
      const [date, setDate] = React.useState(limits.initial || '2026-07-15'), [range, setRange] = React.useState({ start: limits.initial || '2026-07-15', end: limits.initial || '2026-07-20' });
      React.useLayoutEffect(() => { document.getElementById('fixture').dataset.revision = String(revision); }, [revision]);
      return kind === 'date' ? h(DatePicker, { label: 'Settlement', value: date, min: limits.min, max: limits.max, onChange: value => { setDate(value); window.events.push(value); } })
        : h(DateRangePicker, { label: 'Reporting period', ...range, months, min: limits.min, max: limits.max, presets: [{ id: 'prior', label: 'Prior reference period', start: '2026-05-01', end: '2026-05-31' }], onChange: value => { setRange(value); window.events.push(value); } });
    }
    window.mountCalendar = (kind, months, limits) => { const revision = ++window.revision; window.events = []; root.render(h(Demo, { kind, revision, months, limits, key: revision })); return revision; };
  });
  async function mount(kind, months = 1, limits = {}) {
    const revision = await page.evaluate(({ kind, months, limits }) => window.mountCalendar(kind, months, limits), { kind, months, limits });
    await page.waitForFunction(revision => document.getElementById('fixture').dataset.revision === String(revision), revision);
    await page.getByRole('button', { name: kind === 'date' ? 'Open calendar' : /Reporting period/ }).click();
    await page.waitForFunction(kind => document.activeElement?.classList.contains(kind === 'date' ? 'mds-date-day' : 'mds-dr-day'), kind);
    await page.evaluate(() => { window.events = []; });
  }
  const dialog = () => page.getByRole('dialog'), focused = locator => locator.evaluate(node => node === document.activeElement), events = () => page.evaluate(() => window.events);
  await mount('date'); await page.keyboard.press('Tab');
  assert.equal(await dialog().count(), 1, 'Tab from date grid must keep footer reachable');
  assert(await focused(page.getByRole('button', { name: 'Today', exact: true })));
  await page.keyboard.press('Shift+Tab'); assert(await focused(page.locator('.mds-date-day[tabindex="0"]')));
  await page.keyboard.press('Shift+Tab'); const dateNext = page.getByRole('button', { name: 'Next month' }); assert(await focused(dateNext));
  await page.keyboard.press('Enter'); assert.equal(await page.getByRole('grid').getAttribute('aria-label'), 'August 2026'); assert(await focused(dateNext), 'Month control retains focus');
  await page.keyboard.press('Space'); assert.equal(await page.getByRole('grid').getAttribute('aria-label'), 'September 2026'); assert.deepEqual(await events(), []);
  await page.keyboard.press('Escape'); assert.equal(await dialog().count(), 0); assert(await focused(page.getByRole('textbox', { name: 'Settlement' })));
  await mount('date'); await page.keyboard.press('ArrowRight'); assert.match(await page.evaluate(() => document.activeElement.getAttribute('aria-label')), /July 16, 2026/);
  await page.keyboard.press('PageDown'); assert.match(await page.evaluate(() => document.activeElement.getAttribute('aria-label')), /August 16, 2026/);
  await page.keyboard.press('Enter'); assert.deepEqual(await events(), ['2026-08-16']); assert.equal(await dialog().count(), 0);
  await mount('date'); await page.keyboard.press('Tab'); await page.keyboard.press('Space');
  assert.deepEqual(await events(), [await page.evaluate(() => new Date().toISOString().slice(0, 10))], 'Today keeps native Space activation');
  await mount('date'); await page.keyboard.press('Tab'); await page.keyboard.press('Tab'); assert(await focused(dialog().getByRole('button', { name: 'Clear', exact: true })));
  await page.keyboard.press('Enter'); assert.deepEqual(await events(), ['']); assert.equal(await dialog().count(), 0);
  await mount('range'); await page.keyboard.press('Tab'); assert.equal(await dialog().count(), 1, 'Tab from range grid must keep footer reachable');
  assert(await focused(dialog().getByRole('button', { name: 'Clear', exact: true })));
  await page.keyboard.press('Shift+Tab'); assert(await focused(page.locator('.mds-dr-day[tabindex="0"]')));
  await page.keyboard.press('Shift+Tab'); const rangeNext = page.getByRole('button', { name: 'Next month' }); assert(await focused(rangeNext));
  await page.keyboard.press('Enter'); assert.equal(await page.getByRole('grid').getAttribute('aria-label'), 'August 2026'); assert(await focused(rangeNext));
  await page.keyboard.press('Space'); assert.equal(await page.getByRole('grid').getAttribute('aria-label'), 'September 2026'); assert.deepEqual(await events(), []);
  await page.keyboard.press('Tab');
  assert(await focused(page.locator('.mds-dr-day[tabindex="0"]')), 'Tab after month navigation enters a visible day');
  assert.match(await page.evaluate(() => document.activeElement.getAttribute('aria-label')), /September 15, 2026/);
  await page.keyboard.press('Shift+Tab'); assert(await focused(rangeNext));
  await page.keyboard.press('Shift+Tab'); await page.keyboard.press('Shift+Tab'); assert(await focused(page.getByRole('button', { name: 'Prior reference period' })));
  await page.keyboard.press('Enter'); assert.deepEqual(await events(), [{ start: '2026-05-01', end: '2026-05-31' }]); assert.equal(await dialog().count(), 0);
  await mount('range'); await page.keyboard.press('Shift+Tab'); await page.keyboard.press('Shift+Tab'); await page.keyboard.press('Shift+Tab'); await page.keyboard.press('Space');
  assert.deepEqual(await events(), [{ start: '2026-05-01', end: '2026-05-31' }], 'Preset keeps native Space activation');
  await mount('range'); await page.keyboard.press('Tab'); await page.keyboard.press('Space'); assert.deepEqual(await events(), [{ start: null, end: null }]); assert.equal(await dialog().count(), 0);
  await mount('range'); await page.keyboard.press('ArrowRight'); await page.keyboard.press('Enter'); assert.deepEqual(await events(), [{ start: '2026-07-16', end: null }]);
  await page.keyboard.press('ArrowRight'); await page.keyboard.press('Space'); assert.deepEqual(await events(), [{ start: '2026-07-16', end: null }, { start: '2026-07-16', end: '2026-07-17' }]); assert.equal(await dialog().count(), 0);
  await mount('range'); await page.keyboard.press('Tab'); await page.keyboard.press('Escape'); assert.equal(await dialog().count(), 0); assert(await focused(page.getByRole('button', { name: /Reporting period/ }))); assert.deepEqual(await events(), []);
  for (const months of [1, 2]) {
    await mount('range', months);
    const next = page.getByRole('button', { name: 'Next month' }), previous = page.getByRole('button', { name: 'Previous month' });
    await next.focus(); await page.keyboard.press('Enter'); await page.keyboard.press('Space'); assert(await focused(next));
    await previous.focus(); await page.keyboard.press('Enter'); assert(await focused(previous));
    await page.keyboard.press('Tab');
    if (months === 1) { assert(await focused(next)); await page.keyboard.press('Tab'); }
    assert(await focused(page.locator('.mds-dr-day[tabindex="0"]')), `${months}-month view retains keyboard day entry after previous-month navigation`);
    assert.match(await page.evaluate(() => document.activeElement.getAttribute('aria-label')), /August 15, 2026/);
    assert.deepEqual(await events(), []);
  }
  for (const kind of ['date', 'range']) {
    for (const direction of ['next', 'previous']) {
      const limits = direction === 'next' ? { initial: '2026-01-31', max: '2026-02-15' } : { initial: '2026-03-01', min: '2026-02-15' };
      await mount(kind, 1, limits);
      const navigation = page.getByRole('button', { name: direction === 'next' ? 'Next month' : 'Previous month' });
      await navigation.focus(); await page.keyboard.press('Enter');
      // At a declared bound the button just used becomes disabled; focus then
      // moves to the available day rather than remaining on a disabled control.
      if (await navigation.isEnabled()) {
        assert(await focused(navigation)); await page.keyboard.press('Tab');
        if (direction === 'previous') await page.keyboard.press('Tab');
      }
      const day = page.locator(kind === 'date' ? '.mds-date-day[tabindex="0"]' : '.mds-dr-day[tabindex="0"]');
      assert(await focused(day), `${kind} ${direction}-month entry remains inside declared bounds`);
      assert.equal(await day.isEnabled(), true);
      assert.match(await day.getAttribute('aria-label'), /February 15, 2026/);
      assert.deepEqual(await events(), []);
    }
  }
  assert.deepEqual(errors, []);
  console.log('PASS calendar keyboard: Tab/Shift+Tab traversal; native month/preset/footer Enter/Space; day navigation/selection; Escape focus return; no runtime errors (Chromium 1366×768).');
} finally { await browser.close(); }
