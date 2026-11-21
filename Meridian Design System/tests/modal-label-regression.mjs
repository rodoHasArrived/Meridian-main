#!/usr/bin/env node
// Exercise the retained compound Modal API without changing the public bundle.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const compiled = await build({
  absWorkingDir: root, bundle: true, write: false, format: 'iife', platform: 'browser',
  resolveExtensions: ['.js', '.jsx'], jsxFactory: 'React.createElement', jsxFragment: 'React.Fragment',
  stdin: { resolveDir: root, loader: 'js', contents:
    "import * as modal from './components/core/Modal.jsx'; window.modalFixture = modal;" },
  plugins: [{ name: 'react-peer', setup(builder) {
    builder.onResolve({ filter: /^react$/ }, () => ({ path: 'react', namespace: 'peer' }));
    builder.onLoad({ filter: /.*/, namespace: 'peer' }, () => ({ contents: 'export default window.React;', loader: 'js' }));
  } }]
});
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });
const page = await browser.newPage();
const errors = [];
page.on('pageerror', error => errors.push(error.message));
try {
  await page.setContent('<!doctype html><html lang="en"><head><title>Modal label regression</title></head><body><div id="fixture"></div></body></html>');
  await page.addScriptTag({ content: await readFile(resolve(root, '_preview_runtime.js'), 'utf8') });
  await page.addScriptTag({ content: compiled.outputFiles[0].text });
  await page.evaluate(() => {
    const { Modal, ModalHeader, ModalBody } = window.modalFixture;
    const h = React.createElement;
    window.fixtureRoot = ReactDOM.createRoot(document.getElementById('fixture'));
    window.renderModals = (firstOpen = true, firstTitle = 'Review ledger A') => window.fixtureRoot.render(h(React.Fragment, null,
      h(Modal, { key: 'first', open: firstOpen }, h(ModalHeader, { title: firstTitle }), h(ModalBody, null, 'First ledger evidence')),
      h(Modal, { key: 'second', open: true }, h(ModalHeader, { title: 'Review ledger B' }), h(ModalBody, null, 'Second ledger evidence'))
    ));
    window.renderModals();
  });
  await page.getByRole('dialog', { name: 'Review ledger A', exact: true }).waitFor();
  await page.getByRole('dialog', { name: 'Review ledger B', exact: true }).waitFor();
  const ids = await page.getByRole('dialog').evaluateAll(dialogs => dialogs.map(dialog => dialog.getAttribute('aria-labelledby')));
  assert.equal(new Set(ids).size, 2, 'Simultaneously mounted dialogs have distinct title references');
  assert(await page.getByRole('dialog').evaluateAll(dialogs => dialogs.every(dialog =>
    dialog.contains(document.getElementById(dialog.getAttribute('aria-labelledby')))
  )), 'Every dialog names its own header');
  await page.evaluate(() => window.renderModals(true, 'Confirm ledger A'));
  await page.getByRole('dialog', { name: 'Confirm ledger A', exact: true }).waitFor();
  assert.equal(await page.getByRole('dialog', { name: 'Confirm ledger A' }).getAttribute('aria-labelledby'), ids[0]);
  await page.evaluate(() => window.renderModals(false));
  await page.getByRole('dialog', { name: 'Confirm ledger A' }).waitFor({ state: 'detached' });
  assert.equal(await page.getByRole('dialog', { name: 'Review ledger B' }).getAttribute('aria-labelledby'), ids[1]);
  assert.deepEqual(errors, []);
  console.log('PASS Modal: unique per-instance names, updated title and retained sibling reference');
} finally {
  await browser.close();
}
