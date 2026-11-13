#!/usr/bin/env node
// Decimal wire/edit values must agree with displayed ledger totals and proofs.
import assert from 'node:assert/strict';
import { build } from 'esbuild';
import { chromium } from 'playwright';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const { outputFiles } = await build({
  absWorkingDir: root,
  stdin: {
    contents: `import React from 'react';
      import {createRoot} from 'react-dom/client';
      import {DataGrid, gridProof} from './components/data/DataGrid.jsx';
      window.fixture={React,createRoot,DataGrid,gridProof};`,
    resolveDir: root, loader: 'jsx',
  },
  bundle: true, write: false, format: 'iife', platform: 'browser',
  define: { 'process.env.NODE_ENV': '"production"' }, logLevel: 'error',
});
const browser = await chromium.launch({
  executablePath: process.env.MERIDIAN_CHROMIUM_PATH || '/usr/bin/chromium',
  args: ['--no-sandbox'],
});
const page = await browser.newPage();
const errors = [];
page.on('pageerror', error => errors.push(error.message));
try {
  await page.setContent('<!doctype html><html lang="en"><title>Grid amount contracts</title><main><h1>Synthetic ledger amounts</h1><div id="fixture"></div></main></html>');
  await page.addScriptTag({ content: outputFiles[0].text });
  await page.evaluate(() => {
    const { React, createRoot, DataGrid } = window.fixture;
    const root = createRoot(document.getElementById('fixture'));
    window.events = [];
    window.revision = 0;
    const columns = [
      { key: 'account', label: 'Account' },
      { key: 'debit', label: 'Debit', format: 'money', total: 'sum', editable: true },
      { key: 'credit', label: 'Credit', format: 'money', total: 'sum', editable: true },
    ];
    function Ledger({ initialRows, revision }) {
      const [rows, setRows] = React.useState(initialRows);
      React.useLayoutEffect(() => {
        document.getElementById('fixture').dataset.revision = String(revision);
      }, [revision]);
      return React.createElement(DataGrid, {
        label: 'Synthetic ledger', columns, rows, groupBy: 'book', edit: true,
        proof: { type: 'balance', debit: 'debit', credit: 'credit' },
        onEdit: change => {
          window.events.push({ column: change.column, value: change.value });
          setRows(previous => previous.map(row => row.id === change.row.id
            ? { ...row, [change.column]: change.value } : row));
        },
      });
    }
    window.mount = rows => {
      const revision = ++window.revision;
      root.render(React.createElement(Ledger, { initialRows: rows, revision, key: revision }));
      return revision;
    };
  });
  async function mount(rows) {
    const revision = await page.evaluate(rows => window.mount(rows), rows);
    await page.waitForFunction(revision => document.getElementById('fixture').dataset.revision === String(revision), revision);
  }
  const proof = page.locator('.mds-grid__proof');
  const footer = page.locator('tfoot td');
  const subtotal = page.locator('.mds-grid__grp td');
  await mount([
    { id: 'cash', account: 'Cash', book: 'Books A', debit: '100.00', credit: 0 },
    { id: 'income', account: 'Income', book: 'Books A', debit: 0, credit: '100.00' },
  ]);
  assert.match(await proof.textContent(), /Balanced · 100\.00/);
  assert.deepEqual(await footer.allTextContents(), ['Total', '100.00', '100.00']);
  assert.deepEqual((await subtotal.allTextContents()).slice(1), ['100.00', '100.00']);

  // Apply the grid's own string edit payload without a consumer-side conversion.
  await page.locator('[data-row-key="cash"] td').nth(1).press('F2');
  await page.getByRole('textbox', { name: 'Edit Debit' }).fill('125.25');
  await page.getByRole('textbox', { name: 'Edit Debit' }).press('Enter');
  await page.waitForFunction(() => document.querySelector('.mds-grid__proof').textContent.includes('Out by 25.25'));
  assert.deepEqual(await page.evaluate(() => window.events), [{ column: 'debit', value: '125.25' }]);
  assert.deepEqual(await footer.allTextContents(), ['Total', '125.25', '100.00']);
  assert.deepEqual((await subtotal.allTextContents()).slice(1), ['125.25', '100.00']);
  await page.locator('[data-row-key="income"] td').nth(2).press('F2');
  await page.getByRole('textbox', { name: 'Edit Credit' }).fill('125.25');
  await page.getByRole('textbox', { name: 'Edit Credit' }).press('Enter');
  await page.waitForFunction(() => document.querySelector('.mds-grid__proof').textContent.includes('Balanced · 125.25'));

  await mount([{ id: 'imbalance', account: 'Cash', book: 'Books A', debit: '100.00', credit: '0.00' }]);
  assert.match(await proof.textContent(), /Out by 100\.00/);
  assert.deepEqual(await footer.allTextContents(), ['Total', '100.00', '0.00']);
  await mount([{ id: 'missing', account: 'Cash', book: 'Books A', debit: null, credit: '0.00' }]);
  assert.match(await proof.textContent(), /Incomplete evidence/);
  assert.deepEqual(await footer.allTextContents(), ['Total', '—', '0.00']);
  assert.deepEqual((await subtotal.allTextContents()).slice(1), ['—', '0.00']);

  const boundaries = await page.evaluate(() => {
    const { gridProof } = window.fixture;
    const balance = debit => gridProof({ type: 'balance', debit: 'debit', credit: 'credit' }, [{ debit, credit: '0.00' }]);
    return {
      zero: balance('0.00'),
      signed: gridProof({ type: 'sum', column: 'amount', expected: -12.5 }, [{ amount: ' -1.25e1 ' }]),
      invalid: [null, undefined, '', ' ', 'not-an-amount', '12bad', '0x10', true, false, [], {}, NaN, Infinity, -Infinity, 'Infinity', '1e309'].map(balance),
      overflow: gridProof({ type: 'sum', column: 'amount' }, [{ amount: '1e308' }, { amount: '1e308' }]),
      balanceDifferenceOverflow: gridProof({ type: 'balance', debit: 'debit', credit: 'credit' }, [{ debit: 1e308, credit: -1e308 }]),
      roundedDifferenceOverflow: gridProof({ type: 'balance', debit: 'debit', credit: 'credit' }, [{ debit: '2e306', credit: '0' }]),
      sumDifferenceOverflow: gridProof({ type: 'sum', column: 'amount', expected: -1e308 }, [{ amount: 1e308 }]),
      empty: gridProof({ type: 'balance', debit: 'debit', credit: 'credit' }, []),
    };
  });
  assert.equal(boundaries.zero.ok, true);
  assert.equal(boundaries.signed.ok, true);
  for (const result of [...boundaries.invalid, boundaries.overflow, boundaries.balanceDifferenceOverflow, boundaries.roundedDifferenceOverflow, boundaries.sumDifferenceOverflow, boundaries.empty]) {
    assert.equal(result.ok, false);
    assert.equal(result.text, 'Incomplete evidence');
  }
  assert.deepEqual(errors, []);
  console.log('PASS grid amounts: decimal-string balance/imbalance, totals/subtotals, edit round trip, signed decimals, invalid/missing/nonfinite/overflow evidence');
} finally {
  await browser.close();
}
