#!/usr/bin/env node
// Source-level regression: each open Modal labels itself with its own title id.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const source = await build({
  absWorkingDir: root, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2020',
  stdin: { contents: `import * as M from './components/core/Modal.jsx'; window.MeridianModal = M;`, resolveDir: root, loader: 'js' },
  jsxFactory: 'window.React.createElement', jsxFragment: 'window.React.Fragment',
  plugins: [{ name: 'react-peer', setup(builder) {
    builder.onResolve({ filter: /^react$/ }, () => ({ path: 'react', namespace: 'peer' }));
    builder.onLoad({ filter: /.*/, namespace: 'peer' }, () => ({ contents: 'export default window.React;', loader: 'js' }));
  } }]
});
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });
const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
const errors = [];
page.on('pageerror', error => errors.push(error.message));
try {
  await page.setContent('<!doctype html><html lang="en"><head><title>Modal title id regression</title></head><body><main><h1>Synthetic dialogs</h1><div id="fixture"></div></main></body></html>');
  await page.addScriptTag({ content: await readFile(resolve(root, '_preview_runtime.js'), 'utf8') });
  await page.addScriptTag({ content: source.outputFiles[0].text });
  const dialogs = await page.evaluate(async () => {
    const { Modal, ModalHeader, ModalBody, ModalForm } = window.MeridianModal;
    const h = React.createElement;
    ReactDOM.createRoot(document.getElementById('fixture')).render(h(React.Fragment, null,
      h(Modal, { open: true }, h(ModalHeader, { title: 'Close period' }), h(ModalBody, null, 'First synthetic dialog')),
      h(ModalForm, { open: true, title: 'Rename book' }, 'Second synthetic dialog')));
    await new Promise(resolve => setTimeout(resolve, 100));
    return [...document.querySelectorAll('[role="dialog"]')].map(dialog => {
      const id = dialog.getAttribute('aria-labelledby');
      const targets = id ? document.querySelectorAll(`[id="${id}"]`) : [];
      return { id, matches: targets.length, inside: targets.length === 1 && dialog.contains(targets[0]), name: targets[0]?.textContent ?? null };
    });
  });
  assert.equal(dialogs.length, 2, 'both dialogs render');
  for (const dialog of dialogs) {
    assert.ok(dialog.id, 'dialog declares aria-labelledby');
    assert.equal(dialog.matches, 1, `title id ${dialog.id} is unique in the document`);
    assert.ok(dialog.inside, 'the labelling title belongs to its own dialog');
  }
  assert.notEqual(dialogs[0].id, dialogs[1].id, 'two dialogs never share a title id');
  assert.deepEqual(dialogs.map(dialog => dialog.name), ['Close period', 'Rename book']);
  assert.deepEqual(errors, []);
  console.log('PASS Modal: each dialog is labelled by its own unique title id');
} finally {
  await browser.close();
}
