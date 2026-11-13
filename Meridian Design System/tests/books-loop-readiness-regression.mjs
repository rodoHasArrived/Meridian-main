#!/usr/bin/env node
// Real DC boot with the shared books script delayed: mounting before the model
// arrives must recover automatically, and unmounting must release its observers.
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { dirname, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const entry = '/templates/books-loop/BooksLoop.dc.html';
const entryBytes = await readFile(process.env.MERIDIAN_BOOKS_LOOP_SOURCE || resolve(root, '.' + entry));
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.ttf': 'font/ttf', '.woff2': 'font/woff2' };
let releaseBooks;
let booksGate;
const armDelay = () => { booksGate = new Promise(resolve => { releaseBooks = resolve; }); };
const server = createServer(async (request, response) => {
  try {
    const pathname = decodeURIComponent(new URL(request.url, 'http://meridian.test').pathname);
    // Chromium requests this convention URL although the DC entry declares no
    // icon. The fixture host has no favicon; authored resources retain 404s.
    if (pathname === '/favicon.ico') { response.writeHead(204); return response.end(); }
    const path = resolve(root, '.' + pathname);
    if (!path.startsWith(root + '/')) { response.writeHead(403); return response.end(); }
    if (pathname.endsWith('/_scaffold/books.js')) {
      await booksGate;
      await new Promise(resolve => setTimeout(resolve, 600));
    }
    let bytes = pathname === entry ? entryBytes : await readFile(path);
    // Observe the real runtime roots and store subscriptions; retain their full
    // behavior and the authored model, transport, and DC entry unchanged.
    if (pathname.endsWith('/react-dom-18.3.1.production.min.js')) bytes = bytes.toString() + `\n;(() => {const original=ReactDOM.createRoot;window.booksReadinessRoots=[];ReactDOM.createRoot=function(node,...args){const root=original.call(this,node,...args);window.booksReadinessRoots.push({node,root});return root;};})();`;
    if (pathname.endsWith('/_scaffold/instructions.js')) bytes = `;(() => {
      window.booksReadinessMetrics = window.booksReadinessMetrics || {subscriptions:0,unsubscriptions:0};
      window.booksReadinessStoreIds = window.booksReadinessStoreIds || new WeakMap();
      window.booksReadinessActiveStores = window.booksReadinessActiveStores || new Set();
      let current;
      Object.defineProperty(window,'MeridianInstructions',{configurable:true,get(){return current;},set(value){
        current=value;
        const id=window.booksReadinessLoads=(window.booksReadinessLoads||0)+1;
        window.booksReadinessStoreIds.set(value,id);
        const original=value.subscribe;
        value.subscribe=function(listener){
          window.booksReadinessMetrics.subscriptions++;
          window.booksReadinessActiveStores.add(id);
          const off=original.call(this,listener);
          return()=>{window.booksReadinessMetrics.unsubscriptions++;window.booksReadinessActiveStores.delete(id);off();};
        };
      }});
    })();\n` + bytes.toString();
    response.setHeader('content-type', mime[extname(path)] || 'application/octet-stream');
    response.end(bytes);
  } catch (error) { response.writeHead(404); response.end(error.message); }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });

try {
  for (const earlyUnmount of [false, true]) {
    armDelay();
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    try {
      const page = await context.newPage();
      const errors = [];
      page.on('pageerror', error => errors.push(error.message));
      page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
      await page.goto(`http://127.0.0.1:${server.address().port}${entry}`, { waitUntil: 'domcontentloaded' });
      await page.locator('.mds-dss--loading').waitFor();
      const trial = page.getByRole('radio', { name: 'Trial balance', exact: true });
      assert.equal(await trial.count(), 0, 'Financial controls remain unavailable before the real model arrives');
      const unmount = () => page.evaluate(() => window.booksReadinessRoots.find(item => item.node.id === 'dc-root').root.unmount());
      if (earlyUnmount) await unmount();
      releaseBooks();
      await page.waitForFunction(() => !!window.MeridianBooks && !!window.MeridianInstructions);
      await page.waitForLoadState('networkidle');
      if (earlyUnmount) {
        await page.waitForTimeout(50);
        assert.deepEqual(await page.evaluate(() => window.booksReadinessMetrics), { subscriptions: 0, unsubscriptions: 0 }, 'An unmounted screen removes its pending readiness listener');
      } else {
        await trial.waitFor({ timeout: 5000 });
        await trial.click();
        await page.locator('[data-books-view="trial"]:visible').waitFor();
        assert.equal(await page.locator('[data-books-view]:visible').count(), 1);
        await page.getByRole('radio', { name: 'Ledger', exact: false }).first().click();
        await page.locator('[data-books-view="ledger"]:visible').waitFor();
        assert.equal(await page.locator('[data-books-view]:visible').count(), 1);
        const active = await page.evaluate(() => ({
          ...window.booksReadinessMetrics,
          stores: [...window.booksReadinessActiveStores],
          current: window.booksReadinessStoreIds.get(window.MeridianInstructions),
        }));
        assert.equal(active.subscriptions - active.unsubscriptions, 1, 'Exactly one instruction subscription is active');
        assert.deepEqual(active.stores, [active.current], 'The subscription follows the latest real store publication');
        // Subsequent unrelated resource load events must not bind the store again.
        await page.evaluate(() => document.dispatchEvent(new Event('load')));
        assert.deepEqual(await page.evaluate(() => window.booksReadinessMetrics), { subscriptions: active.subscriptions, unsubscriptions: active.unsubscriptions });
        // A republished read-model identity also refreshes the view while keeping
        // the same current instruction subscription and real selector results.
        await page.evaluate(() => {
          const original = window.MeridianBooks;
          window.booksReadinessReads = 0;
          window.MeridianBooks = { ...original, select(...args) { window.booksReadinessReads++; return original.select(...args); } };
          document.dispatchEvent(new Event('load'));
        });
        await page.waitForFunction(() => window.booksReadinessReads > 0);
        assert.deepEqual(await page.evaluate(() => window.booksReadinessMetrics), { subscriptions: active.subscriptions, unsubscriptions: active.unsubscriptions });
        await unmount();
        assert.deepEqual(await page.evaluate(() => window.booksReadinessMetrics), { subscriptions: active.subscriptions, unsubscriptions: active.subscriptions }, 'All instruction subscriptions are released on unmount');
        assert.deepEqual(await page.evaluate(() => [...window.booksReadinessActiveStores]), []);
      }
      assert.deepEqual(errors, [], 'The real delayed DC entry has no runtime errors');
      console.log(`PASS Books Loop delayed model: ${earlyUnmount ? 'unmount before readiness' : 'automatic readiness and cleanup'}`);
    } finally { releaseBooks?.(); await context.close(); }
  }
} finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
