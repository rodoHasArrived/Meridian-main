#!/usr/bin/env node
// Execute source-based contracts in fresh processes and retain their provenance.
import { readdir, readFile, writeFile, mkdir } from 'node:fs/promises';
import { resolve, dirname, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { spawn } from 'node:child_process';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const output = process.argv.find(arg => arg.startsWith('--out='))?.slice(6);
if (process.argv.slice(2).some(arg => !arg.startsWith('--out='))) throw new Error('Usage: node tests/source-contracts.mjs [--out=FILE]');
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const proofOutputs = new Set(['api/source-contract-results.json', 'api/project-compatibility.json', 'api/dashboard-validation.json']);
async function sources(directory) {
  const paths = [];
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const path = resolve(directory, entry.name);
    if (entry.isDirectory()) paths.push(...await sources(path));
    else if (/\.(?:js|jsx|ts|mjs|html|css|json|md|py|sh)$/.test(entry.name)
      && !proofOutputs.has(relative(root, path).split('\\').join('/'))) paths.push(path);
  }
  return paths;
}
async function snapshot() {
  const paths = (await Promise.all(['components', 'tests', 'scripts', 'templates', 'api'].map(directory => sources(resolve(root, directory))))).flat();
  for (const name of ['package.json', 'package-lock.json', '_ds_manifest.json', '_ds_bundle.js', '_preview_runtime.js', '_preview_jsx.js', 'release.json', 'release-build.json', 'styles.css']) paths.push(resolve(root, name));
  paths.sort();
  return Object.fromEntries(await Promise.all(paths.map(async path => [relative(root, path).split('\\').join('/'), hash(await readFile(path))])));
}
const before = await snapshot();
const suites = (await readdir(resolve(root, 'tests'))).filter(name => name.endsWith('-regression.mjs'))
  .concat(['public-contract-types.mjs']).sort();
const results = { schemaVersion: 1, startedAt: new Date().toISOString(), sourceHashes: before, suites: [] };
let cursor = 0;
await Promise.all(Array.from({ length: 2 }, async () => {
  while (cursor < suites.length) {
    const name = suites[cursor++], started = performance.now();
    const result = await new Promise((done, reject) => {
      const child = spawn(process.execPath, [resolve(root, 'tests', name)], { cwd: root, env: process.env });
      let stdout = '', stderr = '';
      child.stdout.on('data', chunk => { stdout += chunk; });
      child.stderr.on('data', chunk => { stderr += chunk; });
      child.on('error', reject);
      child.on('close', code => done({ path: `tests/${name}`, exitCode: code, status: code === 0 ? 'passed' : 'failed',
        seconds: Math.round((performance.now() - started) / 10) / 100,
        stdout: stdout.slice(-60000), stderr: stderr.slice(-12000) }));
    });
    results.suites.push(result);
    console.log(`${result.status}: ${result.path}`);
  }
}));
const after = await snapshot();
results.changedSources = [...new Set([...Object.keys(before), ...Object.keys(after)])].filter(path => before[path] !== after[path]);
results.sourceStable = results.changedSources.length === 0;
results.suites.sort((a, b) => a.path.localeCompare(b.path));
results.status = results.sourceStable && results.suites.every(suite => suite.status === 'passed') ? 'passed' : 'failed';
if (output) {
  await mkdir(dirname(resolve(output)), { recursive: true });
  await writeFile(resolve(output), JSON.stringify(results, null, 2) + '\n');
}
console.log(JSON.stringify({ suites: suites.length, status: results.status, changedSources: results.changedSources }));
if (results.status !== 'passed') process.exitCode = 1;
