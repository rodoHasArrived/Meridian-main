#!/usr/bin/env node
// Exercise real adapters and production CSS without writing build output.
import assert from 'node:assert/strict';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createServer } from 'vite';
import { chromium } from 'playwright';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const fixturePath = '/native-adapter-colors.tsx';
const fixture = `
import React from 'react';
import { createRoot } from 'react-dom/client';
import '/src/styles/index.css';
import '/src/styles/app-shell.css';
import '/src/styles/workflow-continuity-dock.css';
import { ChartCard } from '/src/components/charts/ChartCard';
import { CorrelationHeatmap } from '/src/components/charts/CorrelationHeatmap';
import { TrialBalanceTable } from '/src/components/accounting/TrialBalanceTable';
import { JournalEntryForm } from '/src/components/accounting/JournalEntryForm';
import { MetricCard } from '/src/components/data/metric-card';
import { Pagination } from '/src/components/data/pagination';
import { EvidenceLink } from '/src/components/operations/evidence-link';
import { GateRail } from '/src/components/operations/gate-rail';
createRoot(document.getElementById('root')).render(<main>
  <h1>Native adapter color contract</h1>
  <section data-probe="chart"><ChartCard title="Synthetic chart" height={40}><p>Plot</p></ChartCard></section>
  <TrialBalanceTable rows={[]} />
  <JournalEntryForm accounts={[]} onPost={() => {}} />
  <MetricCard label="Synthetic balance" value="100" />
  <Pagination currentPage={1} totalPages={2} totalItems={100} onPageChange={() => {}} />
  <EvidenceLink label="Synthetic evidence" href="#source" />
  <GateRail gates={[{ key: 'source', label: 'Source', status: 'NotStarted' }, { key: 'review', label: 'Review', status: 'NotStarted' }]} />
  <CorrelationHeatmap labels={['A', 'B']} matrix={[[1, 0.2], [0.2, 1]]} />
  <button className="workflow-continuity-edit">Edit workflow</button>
  <button className="operating-scope-chip">Operating scope</button>
  <span data-probe="danger" className="border border-danger text-danger">Validation error</span>
  <span data-probe="warning" className="border border-warning text-warning">Validation warning</span>
</main>);
`;
const server = await createServer({
  root, configFile: resolve(root, 'vite.config.ts'), server: { host: '127.0.0.1', port: 0 },
  plugins: [{ name: 'native-adapter-color-fixture',
    resolveId(id) { if (id === fixturePath) return resolve(root, 'native-adapter-colors.tsx'); },
    load(id) { if (id === resolve(root, 'native-adapter-colors.tsx')) return fixture; },
    configureServer(server) {
      server.middlewares.use(async (req, res, next) => {
        if (req.url?.split('?')[0] !== '/native-adapter-colors') return next();
        const html = await server.transformIndexHtml(req.url, '<!doctype html><html lang="en"><head><title>Native adapter colors</title><link rel="icon" href="data:,"></head><body><div id="root"></div><script type="module" src="' + fixturePath + '"></script></body></html>');
        res.setHeader('Content-Type', 'text/html'); res.end(html);
      });
    },
  }],
});
let browser;
try {
  await server.listen();
  const address = server.httpServer.address();
  const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
  browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => { if (message.type() === 'error') errors.push(message.text() + ' ' + message.location().url); });
  await page.goto('http://127.0.0.1:' + address.port + '/native-adapter-colors');
  await page.getByRole('heading', { name: 'Native adapter color contract' }).waitFor();
  for (const theme of ['light', 'dark']) {
    await page.evaluate(async theme => {
      document.documentElement.setAttribute('data-theme', theme);
      // Read the settled role colors, after any component border transitions.
      await Promise.all(document.getAnimations().filter(animation => animation.effect.getComputedTiming().iterations !== Infinity).map(animation => animation.finished.catch(() => {})));
    }, theme);
    const colors = await page.evaluate(() => {
      const probe = document.createElement('span'); document.body.appendChild(probe);
      const color = value => { probe.style.color = value; return getComputedStyle(probe).color; };
      const colors = { border: color('var(--ws-border)'), link: color('var(--interaction-link)'), ring: color('var(--interaction-focus)'), danger: color('hsl(var(--danger))'), warning: color('hsl(var(--warning))') }; probe.remove(); return colors;
    });
    for (const selector of ['[data-probe="chart"] > div', '.tbl-wrap', '.jnl', '.mds-metric', '.pgn-btn', '.mds-evidence']) {
      const border = await page.locator(selector).first().evaluate(element => {
        const s = getComputedStyle(element); return { color: s.borderTopColor, width: s.borderTopWidth, style: s.borderTopStyle };
      });
      assert.deepEqual(border, { color: colors.border, width: '1px', style: 'solid' }, theme + ' ' + selector + ': visible border');
    }
    assert.equal(await page.locator('.mds-corr').evaluate(el => getComputedStyle(el).backgroundColor), colors.border, theme + ': matrix separators');
    assert.equal(await page.locator('.mds-gate').nth(1).evaluate(el => getComputedStyle(el, '::before').backgroundColor), colors.border, theme + ': gate connector');
    assert.equal(await page.locator('.workflow-continuity-edit').evaluate(el => getComputedStyle(el).color), colors.link, theme + ': interaction link text');
    for (const role of ['danger', 'warning']) {
      const c = await page.locator('[data-probe="' + role + '"]').evaluate(el => ({ color: getComputedStyle(el).color, border: getComputedStyle(el).borderTopColor }));
      assert.deepEqual(c, { color: colors[role], border: colors[role] }, theme + ': validation colors');
    }
    await page.keyboard.press('Tab');
    for (const selector of ['.workflow-continuity-edit', '.operating-scope-chip']) {
      await page.locator(selector).focus();
      const outline = await page.locator(selector).evaluate(el => ({ color: getComputedStyle(el).outlineColor, width: getComputedStyle(el).outlineWidth, style: getComputedStyle(el).outlineStyle }));
      assert.deepEqual(outline, { color: colors.ring, width: '2px', style: 'solid' }, theme + ': interaction-blue focus');
    }
    console.log('PASS native borders, separators, semantic colors and focus: ' + theme);
  }
  assert.deepEqual(errors, [], 'No adapter runtime errors');
} finally { await browser?.close(); await server.close(); }
