#!/usr/bin/env node
// Compile public declaration presence with the consuming project's React types.
// Source behavior is verified separately by source fixtures; declaration compilation alone does not prove parity.
import { createRequire } from 'node:module';
import { dirname, resolve, relative, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { mkdtemp, readFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { existsSync } from 'node:fs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
let require = createRequire(resolve(root, 'package.json'));
let ts;
try { ts = require('typescript'); }
catch { require = createRequire(resolve(root, '../src/Meridian.Ui/dashboard/package.json')); ts = require('typescript'); }
const reactTypes = dirname(require.resolve('@types/react/package.json'));
const manifest = JSON.parse(await readFile(resolve(root, '_ds_manifest.json'), 'utf8'));
const directory = await mkdtemp(join(tmpdir(), 'meridian-public-contract-'));
try {
  const probe = join(directory, 'public-contract.tsx');
  const declarations = manifest.components.map((item, index) => {
    const source = resolve(root, item.sourcePath);
    const exactCompanion = `${source}.d.ts`;
    const declaration = existsSync(exactCompanion) ? exactCompanion : source;
    return `declare const publicExport${index}: typeof import(${JSON.stringify(declaration)}).${item.name};`;
  }).join('\n');
  const propertyProbes = `
import type { TimestampProps } from ${JSON.stringify(resolve(root, 'components/core/Timestamp.d.ts'))};
import { describeObject } from ${JSON.stringify(resolve(root, 'components/core/ObjectModel.jsx'))};
const timestampLocale: TimestampProps = { value: 0, locale: 'de-DE' };
const normalized = describeObject({type: 'security', id: 0, coverage: {attached: 0, required: 1}, exceptions: 0});
const context: {label: string; value: import('react').ReactNode}[] = normalized.context;
const complete: boolean | null | undefined = normalized.coverage?.complete;
const exceptionCount: number | undefined = normalized.exceptions?.count;
const missingIdentity: string | number | null = describeObject().id;
void [timestampLocale, context, complete, exceptionCount, missingIdentity];
`;
  await writeFile(probe, declarations + '\n' + propertyProbes);
  const program = ts.createProgram([probe], {
    noEmit: true, strict: true, skipLibCheck: false,
    target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext,
    moduleResolution: ts.ModuleResolutionKind.Bundler,
    jsx: ts.JsxEmit.ReactJSX, esModuleInterop: true,
    lib: ['lib.es2022.d.ts', 'lib.dom.d.ts'],
    types: ['react'], typeRoots: [dirname(reactTypes)],
    baseUrl: root,
    paths: { react: [resolve(reactTypes, 'index.d.ts')], 'react/*': [resolve(reactTypes, '*')] },
  });
  const diagnostics = ts.getPreEmitDiagnostics(program).map(item => {
    const location = item.file && item.start !== undefined ? item.file.getLineAndCharacterOfPosition(item.start) : null;
    return { code: item.code, file: item.file ? relative(root, item.file.fileName) : null,
      line: location ? location.line + 1 : null, message: ts.flattenDiagnosticMessageText(item.messageText, '\n') };
  });
  const result = { typescript: ts.version, exports: manifest.components.length,
    reactTypes: JSON.parse(await readFile(resolve(reactTypes, 'package.json'), 'utf8')).version,
    status: diagnostics.length ? 'failed' : 'passed', diagnostics };
  if (process.env.MERIDIAN_TYPE_RESULTS) await writeFile(process.env.MERIDIAN_TYPE_RESULTS, JSON.stringify(result, null, 2) + '\n');
  for (const diagnostic of diagnostics) console.error(`${diagnostic.file ?? 'contract'}:${diagnostic.line ?? 0} TS${diagnostic.code} ${diagnostic.message}`);
  console.log(JSON.stringify({ exports: result.exports, typescript: result.typescript, reactTypes: result.reactTypes, errors: diagnostics.length }));
  if (diagnostics.length) process.exitCode = 1;
} finally { await rm(directory, { recursive: true, force: true }); }
