#!/usr/bin/env node
// Source-level FormField contract: stable labels, native props and real references.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';
import { chromium } from 'playwright';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const source = await build({
  absWorkingDir: root, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2020',
  resolveExtensions: ['.js', '.jsx', '.ts', '.tsx', '.json'],
  stdin: { contents: `import { FormField } from './components/core/FormField.jsx'; import { Input } from './components/core/Input.jsx'; import { ReconciliationPanel } from './components/accounting/ReconciliationPanel.jsx'; import { JournalEntryForm } from './components/accounting/JournalEntryForm.jsx'; window.FieldFixture = { FormField, Input, ReconciliationPanel, JournalEntryForm };`, resolveDir: root, loader: 'js' },
  jsxFactory: 'window.React.createElement', jsxFragment: 'window.React.Fragment',
  plugins: [{ name: 'react-peer', setup(builder) {
    builder.onResolve({ filter: /^react$/ }, () => ({ path: 'react', namespace: 'peer' }));
    builder.onLoad({ filter: /.*/, namespace: 'peer' }, () => ({ contents: 'export default window.React;', loader: 'js' }));
  } }],
});
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });
try {
  const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  await page.setContent('<!doctype html><html lang="en"><head><title>FormField regression</title></head><body><div id="fixture"></div></body></html>');
  await page.addScriptTag({ content: await readFile(resolve(root, '_preview_runtime.js'), 'utf8') });
  await page.addScriptTag({ content: source.outputFiles[0].text });
  await page.evaluate(() => {
    const { FormField, Input, ReconciliationPanel, JournalEntryForm } = window.FieldFixture;
    const host = document.getElementById('fixture');
    const root = ReactDOM.createRoot(host);
    let revision = 0; window.matchCalls = [];
    function Fixture({ config, revision }) {
      const [value, setValue] = React.useState('momentum');
      const [statement, setStatement] = React.useState({ title: 'Statement', items: [
        { id: 'a1', ref: 'A-1', memo: 'First item', amount: 10, matched: false },
        { id: 'a2', ref: 'A-2', memo: 'Second item', amount: 20, matched: true },
      ] });
      React.useLayoutEffect(() => { host.dataset.revision = String(revision); }, [revision]);
      if (config.journal) return React.createElement(JournalEntryForm, { accounts: [], onPost() {} });
      if (config.reconciliation) return React.createElement(ReconciliationPanel, {
        left: statement, right: { title: 'Ledger', items: [] },
        onToggleItem(id, checked) {
          window.matchCalls.push({ kind: 'item', id, checked });
          setStatement(side => ({ ...side, items: side.items.map(item => item.id === id ? { ...item, matched: checked } : item) }));
        },
        onToggleAll(side, checked) {
          window.matchCalls.push({ kind: 'all', side: side.title, checked });
          setStatement(current => ({ ...current, items: current.items.map(item => ({ ...item, matched: checked })) }));
        },
      });
      return React.createElement(React.Fragment, null,
        React.createElement('p', { id: 'outside-help' }, 'Consumer help'),
        React.createElement('span', { id: 'consumer-label' }, 'Consumer supplied field'),
        React.createElement(FormField, { label: 'Strategy name', hint: config.hint, error: config.error, required: true },
          React.createElement(Input, {
            id: config.id, value, onChange: event => setValue(event.target.value), name: 'strategy',
            'data-consumer': 'kept', 'aria-describedby': config.describedBy,
            'aria-label': config.ariaLabel, 'aria-labelledby': config.labelledBy,
            'aria-invalid': config.invalid,
          })),
        React.createElement('output', { id: 'consumer-value' }, value));
    }
    window.renderField = config => {
      root.render(React.createElement(Fixture, { config, revision: ++revision }));
      return revision;
    };
  });
  async function render(config) {
    const revision = await page.evaluate(config => window.renderField(config), config);
    await page.waitForFunction(revision => document.getElementById('fixture').dataset.revision === String(revision), revision);
  }
  async function references() {
    return page.locator('input').evaluate(input => {
      const ids = (input.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean);
      return { ids, resolved: ids.map(id => document.getElementById(id)?.textContent ?? null) };
    });
  }
  const initial = { hint: 'Choose a descriptive name', describedBy: 'outside-help outside-help' };
  await render(initial);
  const label = page.locator('label').filter({ hasText: 'Strategy name' }).first();
  const input = page.getByRole('textbox', { name: 'Strategy name', exact: true });
  const generatedId = await input.getAttribute('id');
  assert.equal(await label.getAttribute('for'), generatedId);
  await label.click(); assert(await input.evaluate(element => element === document.activeElement), 'Label click focuses the control');
  await input.fill('updated'); assert.equal(await page.locator('#consumer-value').innerText(), 'updated', 'Consumer change handler is preserved');
  assert.equal(await input.getAttribute('name'), 'strategy');
  assert.equal(await input.getAttribute('data-consumer'), 'kept');
  assert.equal(await input.getAttribute('aria-required'), 'true');
  let refs = await references();
  assert.equal(new Set(refs.ids).size, refs.ids.length, 'Descriptions are deduplicated');
  assert.deepEqual(refs.resolved, ['Consumer help', initial.hint]);
  await render({ ...initial, hint: 'Updated guidance' });
  assert.equal(await input.getAttribute('id'), generatedId, 'Generated ID remains stable on rerender');
  refs = await references(); assert.deepEqual(refs.resolved, ['Consumer help', 'Updated guidance']);
  await render({ ...initial, error: 'Name already exists' });
  assert.equal(await input.getAttribute('aria-invalid'), 'true');
  refs = await references(); assert.deepEqual(refs.resolved, ['Consumer help', 'Name already exists']);
  await render({ id: 'strategy-name-field', describedBy: 'outside-help outside-help', ariaLabel: 'Custom accessible name', labelledBy: 'consumer-label', invalid: 'spelling' });
  assert.equal(await label.getAttribute('for'), 'strategy-name-field', 'Provided child ID drives the visible label');
  const provided = page.locator('#strategy-name-field');
  assert.equal(await provided.getAttribute('aria-label'), 'Custom accessible name');
  assert.equal(await provided.getAttribute('aria-labelledby'), 'consumer-label');
  assert.equal(await provided.getAttribute('aria-invalid'), 'spelling');
  assert.deepEqual(await references(), { ids: ['outside-help'], resolved: ['Consumer help'] }, 'No hint/error reference remains after its message disappears');
  await render({ reconciliation: true });
  const search = page.getByRole('textbox', { name: 'Filter reconciliation items by memo or reference', exact: true });
  await search.fill('A-1');
  await page.getByRole('checkbox', { name: 'Matched: Statement A-1', exact: true }).check();
  assert.deepEqual(await page.evaluate(() => window.matchCalls.at(-1)), { kind: 'item', id: 'a1', checked: true }, 'Checkbox boolean payload reaches the item callback');
  await search.fill('');
  await page.getByRole('checkbox', { name: 'Matched status for all Statement items', exact: true }).uncheck();
  assert.deepEqual(await page.evaluate(() => window.matchCalls.at(-1)), { kind: 'all', side: 'Statement', checked: false }, 'Bulk matching receives a boolean');
  assert.equal(await page.getByRole('checkbox', { name: /^Matched: Statement/ }).evaluateAll(inputs => inputs.filter(input => input.checked).length), 0);
  await render({ journal: true });
  const date = page.getByLabel('Date', { exact: true });
  const dateId = await date.getAttribute('id');
  await page.locator('.jnl__lbl').filter({ hasText: /^Date$/ }).click();
  assert(await date.evaluate(element => element === document.activeElement), 'Journal date label focuses its input');
  await render({ journal: true });
  assert.equal(await date.getAttribute('id'), dateId, 'Journal header IDs remain stable');
  for (const name of ['Reference', 'Memo', 'Account line 1', 'Debit line 1', 'Credit line 1']) {
    assert.equal(await page.getByLabel(name, { exact: true }).count(), 1, name + ' has an explicit accessible name');
  }
  assert.deepEqual(errors, []);
  console.log('PASS FormField: label focus, stable/provided IDs, merged descriptions, errors, consumer/native props and reconciliation checkbox callbacks');
} finally { await browser.close(); }
