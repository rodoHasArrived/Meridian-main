#!/usr/bin/env node
// Current source, local React, isolated synthetic records; no live services or CDN.
// Browser plugin not available: use the repository's Playwright/Chromium harness.
import assert from 'node:assert/strict';
import { build } from 'esbuild';
import { chromium } from 'playwright';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const imports = {
  EvidencePreview: 'operations/EvidencePreview',
  VersionConflictNotice: 'operations/VersionConflictNotice',
  ChangeSetProvider: 'operations/ChangeSetReview',
  ChangeSetReview: 'operations/ChangeSetReview',
  ChangeSetHooks: 'operations/ChangeSetReview',
  ResolutionDrawer: 'operations/ResolutionDrawer',
};
const built = await build({
  absWorkingDir: root, bundle: true, write: false, format: 'iife', platform: 'browser',
  resolveExtensions: ['.js', '.jsx', '.ts', '.tsx', '.json'],
  define: { 'process.env.NODE_ENV': '"production"' },
  stdin: {
    resolveDir: root, loader: 'jsx',
    contents: `import React from 'react';import {createRoot} from 'react-dom/client';${Object.entries(imports).map(([name, path]) => `import {${name}} from './components/${path}.jsx';`).join('')}window.Fixture={React,createRoot,${Object.keys(imports).join(',')}};`,
  },
});
const browser = await chromium.launch({
  executablePath: process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH,
  args: ['--no-sandbox'],
});
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
page.setDefaultTimeout(5000);
const errors = [], cases = [], requests = [];
page.on('pageerror', error => errors.push(error.message));
async function check(names, name, states, fn) {
  try {
    await fn();
    cases.push({ exports: names, name, states, status: 'passed' });
  } catch (error) {
    cases.push({ exports: names, name, states, status: 'failed', detail: error.message });
    console.error(`FAIL ${name}: ${error.message}`);
  }
}

try {
  await page.route('**/*', route => {
    const url = route.request().url();
    if (url === 'http://meridian.test/operations-states') {
      return route.fulfill({ contentType: 'text/html', body: '<!doctype html><html lang="en"><head><title>Operation state contracts</title></head><body><main><h1>Synthetic operation state contracts</h1><div id="fixture"></div></main></body></html>' });
    }
    requests.push(url);
    return route.abort();
  });
  await page.goto('http://meridian.test/operations-states');
  assert.equal(await page.title(), 'Operation state contracts');
  await page.addScriptTag({ content: built.outputFiles[0].text });
  await page.evaluate(() => {
    const { React, createRoot, ...DS } = window.Fixture;
    const host = document.getElementById('fixture'), root = createRoot(host);
    let revision = 0;
    window.events = [];
    const record = (name, payload) => window.events.push(payload === undefined ? { name } : { name, payload });
    const pixel = 'data:image/svg+xml,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="120" height="40"><rect width="120" height="40" fill="white"/><text x="5" y="25">Synthetic 25</text></svg>');
    const original = { amount: 10, code: 'A' }, draft = { amount: 25, code: 'B' };
    const fields = { amount: { label: 'Amount' }, code: { label: 'Control code' } };
    const conflictFields = [
      { key: 'amount', label: 'Amount', base: 10, proposed: 20, latest: 30 },
      { key: 'currency', label: 'Currency', base: 'USD', proposed: 'EUR', latest: 'GBP' },
      { key: 'memo', label: 'Memo', base: 'Opening', proposed: 'Checked', latest: 'Opening' },
    ];
    function ChangeProbe() { window.changeSet = DS.ChangeSetHooks.useChangeSet(); return null; }
    function Fixture({ kind, config, revision }) {
      React.useLayoutEffect(() => { host.dataset.revision = String(revision); }, [revision]);
      if (kind === 'evidence') return React.createElement(DS.EvidencePreview, {
        title: 'Synthetic statement', source: 'Synthetic Custodian', sourceSystem: 'Fixture archive', documentId: 'DOC-17',
        type: config.type || 'statement', page: 4, section: 'Closing balance',
        before: 'Opening ', highlight: '25.00 USD', after: ' closing.',
        image: config.image ? pixel : undefined,
        imageAlt: config.noAlt ? undefined : config.image ? 'Synthetic balance crop' : undefined,
        textEquivalent: config.equivalent ? 'Explicit textual balance: 25.00 USD.' : undefined,
        asOf: '2026-10-30', statementDate: config.statementDate ? '2026-10-31' : undefined,
        receivedAt: '2026-11-01', extraction: config.statusAlias ? undefined : 'Parsed exact fixture',
        extractionStatus: config.statusAlias ? 'Manual exact fixture' : undefined,
        completeness: 0, confidence: 0, metadata: [{ label: 'Synthetic metadata', value: 'META-17' }],
        citation: 'DOC-17 · page 4', condition: config.restricted ? 'restricted' : 'confirmed',
        superseded: config.superseded, supersededBy: config.supersededBy ? 'DOC-18' : undefined,
        redacted: config.redacted, restriction: config.restriction ? 'Internal retention only' : undefined,
        permissions: { canOpen: !config.denyOpen, canExport: !config.denyExport }, index: config.index ?? 1, total: 3,
        ...(config.noCallbacks ? {} : {
          onPrevious: () => record('previous'), onNext: () => record('next'),
          onCopyCitation: () => record('citation'), onCompare: () => record('compare'),
          onLink: () => record('link'), onMarkSuperseded: () => record('supersede'),
          onOpenOriginal: () => record('original'),
        }),
      });
      if (kind === 'conflict') return React.createElement(DS.VersionConflictNotice, {
        record: config.record || 'REC-17',
        baseVersion: config.originalVersion ? undefined : 4, originalVersion: config.originalVersion ? 3 : undefined,
        latestVersion: config.version || 5, latestAuthor: 'Synthetic controller', latestAt: '2026-11-01T12:00:00Z',
        fields: config.cleanOnly ? [
          { key: 'amount', label: 'Amount', base: 10, proposed: 20, latest: 10 },
          { key: 'posted', label: 'Posted', base: false, proposed: false, latest: true },
        ] : config.single ? [{ ...conflictFields[0], latest: config.latest ?? 30 }] : conflictFields,
        choices: config.controlled ? (config.choices || {}) : undefined,
        onFieldChoice: (key, side) => record('choice', { key, side }), requireReason: config.noReason ? false : true,
        onReload: () => record('reload'), onCompare: () => record('compare'),
        onMergeNonConflicting: () => record('merge'), onReapply: detail => record('reapply', detail),
        onSaveDraft: reason => record('draft', reason), onDiscard: () => record('discard'), autoFocus: true,
      });
      if (kind === 'change') return React.createElement(DS.ChangeSetProvider, {
        original: config.original || original, value: config.value || draft, id: config.id || 'CS-17', version: config.version ?? 4, fields,
        onChange: value => record('draft', value),
      }, React.createElement(ChangeProbe), React.createElement(DS.ChangeSetReview, {
        status: config.status, requireReason: config.requireReason, fields,
        issues: config.error ? [{ key: 'amount', severity: 'error', message: 'Exact field is invalid' }] : [],
        impact: { records: 2, recalculations: 0, lockedPeriod: true, auditEvent: true },
        onReviewImpact: () => record('impact'),
        ...(config.externalDiscard ? { onDiscard: () => config.discardBinding
          ? record('discard', { id: config.id || 'CS-17', version: config.version ?? 4, draft: config.value || draft })
          : record('discard') } : {}),
        onSave: (draft, changes) => record('save', { draft, changes }),
        onSubmit: (draft, changes) => record('submit', { draft, changes }),
      }));
      if (kind === 'resolution') return React.createElement(DS.ResolutionDrawer, {
        inline: true, title: 'Resolve synthetic exception', caseId: config.caseId || 'CASE-17', version: config.version ?? 4,
        severity: config.severity || 'exception', condition: 'missing', object: 'Journal J-17',
        statement: 'A synthetic source confirmation is missing.',
        fields: [{ id: 'balance', label: 'Observed balance', value: 0, flag: true }],
        evidenceItems: config.attachedOnly ? [{ id: 'attached', label: 'Attached source', href: '#source', detail: 'Fixture source' }] : [
          { id: 'missing', label: 'Required confirmation', missing: true },
          { id: 'attached', label: 'Attached source', href: '#source', detail: 'Fixture source' },
        ],
        rootCauses: ['Timing', { value: 'mapping', label: 'Mapping issue' }], rootCause: config.cause || '',
        onRootCauseChange: value => record('cause', value), rootCauseNote: config.note || '',
        onRootCauseNoteChange: value => record('note', value),
        correctionFacts: [{ label: 'Proposed balance', value: 25 }], impactItems: [{ label: 'Reports', value: 1 }],
        ownership: { assignee: 'Synthetic operator', team: 'Control', due: '2026-11-12', age: '1d', escalation: 'Controller', handoffs: '2' },
        audit: 'Fixture audit event AUD-17', requireReason: config.noReason ? false : true,
        reason: config.controlled ? config.reason || '' : undefined,
        onReasonChange: value => record('reason', value), primaryAction: config.primary || 'resolve',
        onBack: () => record('back'), onRequestEvidence: () => record('request'), onAssign: () => record('assign'),
        onResolve: config.noResolve ? undefined : reason => record('resolve', reason),
        onEscalate: () => record('escalate'), onDefer: () => record('defer'), onReopen: () => record('reopen'),
      });
      throw new Error(kind);
    }
    window.mount = (kind, config = {}) => {
      window.events = [];
      const next = ++revision;
      root.render(React.createElement(Fixture, { kind, config, revision: next, key: config.reset || kind }));
      return next;
    };
  });
  async function mount(kind, config = {}) {
    const revision = await page.evaluate(({ kind, config }) => window.mount(kind, config), { kind, config });
    await page.waitForFunction(revision => document.querySelector('#fixture').dataset.revision === String(revision), revision);
  }
  const text = () => page.locator('#fixture').textContent();
  const events = () => page.evaluate(() => window.events);
  const button = name => page.getByRole('button', { name, exact: true });
  async function keyActivate(locator, key = 'Space') {
    await locator.focus();
    assert(await locator.evaluate(node => document.activeElement === node), 'Native control receives keyboard focus');
    await page.keyboard.press(key);
  }
  const csNames = ['ChangeSetProvider', 'ChangeSetHooks', 'ChangeSetReview'];

  await check(['EvidencePreview'], 'text excerpt preserves highlight, zero metadata and exact native evidence actions', ['text', 'highlight', 'confirmed', 'zero', 'keyboard', 'focus', 'previous', 'next', 'citation', 'compare', 'link', 'supersede', 'original'], async () => {
    await mount('evidence');
    assert.equal(await page.locator('.mds-evp__doc').textContent(), 'Opening 25.00 USD closing.');
    assert.equal(await page.locator('mark').textContent(), '25.00 USD');
    assert.match(await text(), /Synthetic Custodian · Fixture archive · DOC-17/);
    assert.match(await text(), /Page 4Closing balance/);
    assert.match(await text(), /Completeness0Match confidence0%/);
    assert.match(await text(), /Synthetic metadataMETA-17/);
    assert.match(await text(), /Evidence 2 of 3/);
    const labels = ['Previous', 'Next', 'Copy citation', 'Compare', 'Link to exception', 'Mark superseded', 'Open original'];
    for (const [index, label] of labels.entries()) await keyActivate(button(label), index % 2 ? 'Enter' : 'Space');
    assert.deepEqual(await events(), ['previous', 'next', 'citation', 'compare', 'link', 'supersede', 'original'].map(name => ({ name })));
  });
  await check(['EvidencePreview'], 'image alt provides text equivalent and explicit equivalent remains separate from image accessible name', ['image', 'image-alt', 'text-equivalent', 'fallback', 'extraction-status', 'statement-date'], async () => {
    await mount('evidence', { image: true, statusAlias: true, statementDate: true });
    assert.equal(await page.getByRole('img', { name: 'Synthetic balance crop', exact: true }).count(), 1);
    assert.equal(await page.locator('.mds-evp__equiv p').textContent(), 'Synthetic balance crop');
    assert.equal(await page.locator('mark').count(), 0);
    assert.match(await text(), /Statement date2026-10-31/);
    assert.match(await text(), /ExtractionManual exact fixture/);
    await mount('evidence', { image: true, equivalent: true, noAlt: true, type: 'Synthetic custom type' });
    assert.equal(await page.getByRole('img', { name: 'Evidence excerpt from Synthetic statement', exact: true }).count(), 1);
    assert.equal(await page.locator('.mds-evp__equiv p').textContent(), 'Explicit textual balance: 25.00 USD.');
    assert.match(await text(), /Synthetic custom type/);
  });
  await check(['EvidencePreview'], 'redaction and supersession remain explicit while export restriction preserves permitted evidence actions', ['redacted', 'superseded', 'export-restricted', 'restriction', 'keyboard', 'recovery'], async () => {
    await mount('evidence', { redacted: true, superseded: true, supersededBy: true, denyExport: true, restriction: true });
    assert.match(await text(), /Superseded by DOC-18/);
    assert.match(await text(), /Contains redactions/);
    assert.match(await text(), /Export restricted/);
    assert.match(await text(), /Internal retention only/);
    assert.equal(await page.locator('.mds-evp--superseded').count(), 1);
    assert.equal(await button('Mark superseded').count(), 0);
    assert(!(await button('Open original').isDisabled()));
    assert(!(await button('Compare').isDisabled()));
    await keyActivate(button('Copy citation'), 'Enter');
    await keyActivate(button('Open original'));
    assert.deepEqual(await events(), [{ name: 'citation' }, { name: 'original' }]);
    await mount('evidence', { redacted: 'Account identifier withheld' });
    assert.match(await text(), /Redacted · Account identifier withheld/);
    assert.equal(await button('Mark superseded').count(), 1);
  });
  await check(['EvidencePreview'], 'restricted evidence withholds content and metadata and states precise original and compare disabled reasons', ['restricted', 'withheld', 'disabled', 'permission-denied', 'keyboard', 'recovery'], async () => {
    await mount('evidence', { image: true, equivalent: true, restricted: true });
    assert.equal(await page.locator('.mds-evp__doc').textContent(), 'Restricted');
    assert.equal(await page.getByRole('img').count(), 0);
    assert.equal(await page.locator('.mds-evp__equiv').count(), 0);
    for (const secret of ['Explicit textual balance', 'Parsed exact fixture', 'META-17', 'Completeness', 'Match confidence']) assert(!(await text()).includes(secret), `${secret} is withheld`);
    assert(await button('Open original').isDisabled());
    assert.equal(await button('Open original').getAttribute('title'), 'Evidence is restricted');
    assert(await button('Compare').isDisabled());
    assert.equal(await button('Compare').getAttribute('title'), 'Restricted evidence cannot be compared');
    await keyActivate(button('Copy citation'));
    await keyActivate(button('Next'), 'Enter');
    assert.deepEqual(await events(), [{ name: 'citation' }, { name: 'next' }]);
    await mount('evidence', { denyOpen: true });
    assert(await button('Open original').isDisabled());
    assert.equal(await button('Open original').getAttribute('title'), 'Document permissions do not allow opening the original');
    assert(!(await button('Compare').isDisabled()));
    await mount('evidence');
    assert(!(await button('Open original').isDisabled()));
    assert.equal(await button('Open original').getAttribute('title'), null);
  });
  await check(['EvidencePreview'], 'optional callbacks determine available evidence actions and index does not infer navigation permission', ['callbacks-absent', 'navigation-boundary', 'keyboard'], async () => {
    await mount('evidence', { noCallbacks: true });
    assert.equal(await page.getByRole('button').count(), 0);
    await mount('evidence', { index: 0 });
    assert(!(await button('Previous').isDisabled()));
    await keyActivate(button('Previous'));
    assert.deepEqual(await events(), [{ name: 'previous' }]);
  });
  await check(['VersionConflictNotice'], 'controlled field proposals wait for parent choices and every conflict plus rationale gates exact reapply', ['controlled', 'conflict', 'multiple-fields', 'reason-required', 'disabled', 'keyboard', 'focus', 'reapply'], async () => {
    await mount('conflict', { controlled: true, reset: 'controlled-conflict' });
    assert.match(await text(), /2 fields conflict · 1 field merge cleanly/);
    const mine = page.getByRole('radio', { name: 'Keep mine', exact: true }).first();
    await keyActivate(mine);
    assert.deepEqual(await events(), [{ name: 'choice', payload: { key: 'amount', side: 'mine' } }]);
    assert(!(await mine.isChecked()));
    await mount('conflict', { controlled: true, choices: { amount: 'mine' }, reset: 'controlled-conflict' });
    assert(await mine.isChecked());
    assert.match(await text(), /Keeping your value replaces a newer saved value/);
    const reason = page.getByRole('textbox', { name: 'Reason for replacing the newer record', exact: true });
    await reason.fill('  Approved by AUTH-17  ');
    assert(await button('Reapply with rationale').isDisabled());
    await mount('conflict', { controlled: true, choices: { amount: 'mine', currency: 'theirs' }, reset: 'controlled-conflict' });
    assert.equal(await reason.inputValue(), '  Approved by AUTH-17  ');
    assert(!(await button('Reapply with rationale').isDisabled()));
    await keyActivate(button('Reapply with rationale'), 'Enter');
    assert.deepEqual(await events(), [{ name: 'reapply', payload: { reason: 'Approved by AUTH-17', choices: { amount: 'mine', currency: 'theirs' } } }]);
    await reason.fill('   ');
    assert(await button('Reapply with rationale').isDisabled());
  });
  await check(['VersionConflictNotice'], 'clean fields merge without picks and original-version alias preserves lineage and exact callback payloads', ['nonconflicting', 'original-version', 'no-reason', 'merge', 'draft', 'reload', 'discard', 'compare', 'keyboard'], async () => {
    await mount('conflict', { cleanOnly: true, originalVersion: true, noReason: true, reset: 'clean-conflict' });
    assert.match(await text(), /No field conflicts · 2 fields merge cleanly/);
    assert.match(await text(), /Your edit began from version 3 · version 5 was saved by Synthetic controller at 2026-11-01T12:00:00Z/);
    assert.equal(await page.getByRole('radio').count(), 0);
    assert.match(await page.locator('.mds-vcn__r--clean').nth(1).textContent(), /PostedMergesNoNoYes/);
    for (const label of ['Merge 2 nonconflicting', 'Reapply with rationale', 'Compare all changes', 'Save as new draft', 'Discard my draft', 'Reload latest record']) await keyActivate(button(label), 'Enter');
    assert.deepEqual(await events(), [{ name: 'merge' }, { name: 'reapply', payload: { reason: '', choices: {} } }, { name: 'compare' }, { name: 'draft', payload: '' }, { name: 'discard' }, { name: 'reload' }]);
    await mount('conflict', { single: true, reset: 'conflict-draft' });
    assert(await button('Merge 0 nonconflicting').isDisabled());
    await page.getByRole('textbox').fill('  Preserve raw draft rationale  ');
    await keyActivate(button('Save as new draft'));
    assert.deepEqual(await events(), [{ name: 'draft', payload: '  Preserve raw draft rationale  ' }]);
  });
  await check(['VersionConflictNotice'], 'uncontrolled choices and rationale reset with record, version and changed field scope', ['uncontrolled', 'selected', 'version-change', 'record-change', 'field-change', 'reset', 'auto-focus'], async () => {
    for (const [index, next] of [{ version: 6 }, { record: 'REC-18' }, { latest: 31 }].entries()) {
      const reset = `scope-conflict-${index}`;
      await mount('conflict', { single: true, reset });
      await page.waitForFunction(() => document.activeElement?.tagName === 'H3');
      await keyActivate(page.getByRole('radio', { name: 'Keep mine', exact: true }));
      await page.getByRole('textbox').fill('Scope-specific AUTH-17');
      assert(!(await button('Reapply with rationale').isDisabled()));
      await mount('conflict', { single: true, ...next, reset });
      assert.equal(await page.getByRole('textbox').inputValue(), '');
      assert(!(await page.getByRole('radio', { name: 'Keep mine', exact: true }).isChecked()));
      assert(await button('Reapply with rationale').isDisabled());
    }
  });
  await check(csNames, 'committed superseded conflicted and validating gate save and submit while preserving inspectable staged changes', ['committed', 'superseded', 'conflicted', 'validating', 'disabled', 'impact', 'discard', 'keyboard'], async () => {
    const labels = { committed: 'Committed', superseded: 'Superseded', conflicted: 'Conflicted', validating: 'Validating' };
    for (const [status, label] of Object.entries(labels)) {
      await mount('change', { status, externalDiscard: true, reset: 'final-change' });
      assert.equal(await page.locator('.mds-cs__status').textContent(), label);
      assert.equal(await page.locator('.mds-cs__row').count(), 2);
      assert(await button('Save changes').isDisabled());
      assert(await button('Submit for review').isDisabled());
      await keyActivate(button('Review impact'));
      await keyActivate(button('Discard'), 'Enter');
      assert.match(await text(), /Discard 2 staged changes\?/);
      await keyActivate(button('Keep editing'));
      assert.equal(await page.locator('.mds-cs__confirm').count(), 0);
      assert.deepEqual(await events(), [{ name: 'impact' }]);
    }
  });
  await check(csNames, 'partial recovery commits only the parent supplied remaining draft without replaying a committed field', ['partial', 'remaining-draft', 'recovery', 'controlled', 'save', 'submit', 'keyboard', 'committed', 'clean'], async () => {
    const original = { amount: 25, code: 'A' }, value = { amount: 25, code: 'B' };
    await mount('change', { original, value, status: 'partial', reset: 'partial-change' });
    assert.equal(await page.locator('.mds-cs__status').textContent(), 'Partially committed');
    assert.equal(await page.locator('.mds-cs__status').getAttribute('data-tone'), 'review');
    assert.equal(await page.locator('.mds-cs__row').count(), 1);
    assert.equal(await page.locator('.mds-cs__label').textContent(), 'Control code');
    await keyActivate(button('Save changes'));
    await keyActivate(button('Submit for review'), 'Enter');
    const expected = { draft: value, changes: [{ key: 'code', before: 'A', after: 'B', group: undefined, label: 'Control code', impact: undefined, reason: undefined }] };
    assert.deepEqual(await events(), [{ name: 'save', payload: expected }, { name: 'submit', payload: expected }]);
    assert.equal(await page.locator('.mds-cs__row').count(), 1, 'Callback proposals do not commit the parent draft');
    await mount('change', { original, value, status: 'committed', reset: 'partial-change' });
    assert(await button('Save changes').isDisabled());
    await mount('change', { original: value, value, reset: 'partial-change' });
    assert.equal(await page.locator('.mds-cs__row').count(), 0);
    assert.match(await text(), /No staged changes/);
    assert(await button('Save changes').isDisabled());
    assert(await button('Discard').isDisabled());
  });
  await check(csNames, 'field rationale and validation independently gate exact draft callbacks and recover when parent clears errors', ['reason-required', 'validation', 'invalid', 'ready', 'disabled', 'recovery', 'keyboard'], async () => {
    await mount('change', { status: 'ready', requireReason: true, reset: 'reason-change' });
    const reasons = page.getByRole('textbox', { name: 'Reason for change', exact: true });
    await reasons.nth(0).fill('  Authority A  ');
    assert(await button('Save changes').isDisabled());
    await reasons.nth(1).fill('   ');
    assert(await button('Submit for review').isDisabled());
    await reasons.nth(1).fill('Authority B');
    assert(!(await button('Save changes').isDisabled()));
    await mount('change', { error: true, requireReason: true, reset: 'reason-change' });
    assert.match(await text(), /Exact field is invalid/);
    assert(await button('Save changes').isDisabled());
    await mount('change', { requireReason: true, reset: 'reason-change' });
    await keyActivate(button('Save changes'), 'Enter');
    assert.deepEqual(await events(), [{ name: 'save', payload: { draft: { amount: 25, code: 'B' }, changes: [
      { key: 'amount', before: 10, after: 25, group: undefined, label: 'Amount', impact: undefined, reason: '  Authority A  ' },
      { key: 'code', before: 'A', after: 'B', group: undefined, label: 'Control code', impact: undefined, reason: 'Authority B' },
    ] } }]);
  });
  await check(csNames, 'confirmed controlled discard proposes original draft and waits for parent acceptance', ['discard-confirmation', 'controlled', 'keyboard', 'empty', 'recovery'], async () => {
    await mount('change', { reset: 'discard-change' });
    await keyActivate(button('Discard'));
    assert.deepEqual(await events(), []);
    await keyActivate(button('Discard staged changes'), 'Enter');
    assert.deepEqual(await events(), [{ name: 'draft', payload: { amount: 10, code: 'A' } }]);
    assert.equal(await page.locator('.mds-cs__row').count(), 2);
    await mount('change', { value: { amount: 10, code: 'A' }, reset: 'discard-change' });
    assert.match(await text(), /No staged changes/);
    assert(await button('Review impact').isDisabled());
  });
  await check(csNames, 'discard confirmation resets across change set identity and version and fresh confirmation uses the current controlled binding', ['discard-confirmation', 'controlled', 'identity-change', 'version-change', 'reset', 'current-callback', 'keyboard'], async () => {
    const reset = 'scoped-discard-change';
    const originalA = { amount: 10, code: 'A' }, valueA = { amount: 25, code: 'B' };
    const originalB = { amount: 100, code: 'C' }, valueB = { amount: 125, code: 'D' };
    const valueB2 = { amount: 130, code: 'E' };
    const callbacks = { externalDiscard: true, discardBinding: true, reset };
    await mount('change', { ...callbacks, id: 'CS-A', version: 1, original: originalA, value: valueA });
    await keyActivate(button('Discard'));
    assert.equal(await page.locator('.mds-cs__confirm').count(), 1);
    await mount('change', { ...callbacks, id: 'CS-B', version: 1, original: originalB, value: valueB });
    assert.equal(await page.locator('.mds-cs__confirm').count(), 0);
    assert.deepEqual(await page.evaluate(() => window.changeSet.draft), valueB);
    assert.deepEqual(await events(), []);
    assert.equal(await page.locator('.mds-cs__before').first().textContent(), '100');
    assert.equal(await page.locator('.mds-cs__after').first().textContent(), '125');
    await keyActivate(button('Discard'), 'Enter');
    assert.equal(await page.locator('.mds-cs__confirm').count(), 1);
    assert.match(await page.locator('.mds-cs__confirm').textContent(), /Amount: 100 → 125/);
    await mount('change', { ...callbacks, id: 'CS-B', version: 2, original: originalB, value: valueB2 });
    assert.equal(await page.locator('.mds-cs__confirm').count(), 0);
    assert.deepEqual(await page.evaluate(() => window.changeSet.draft), valueB2);
    assert.deepEqual(await events(), []);
    await keyActivate(button('Discard'));
    assert.match(await page.locator('.mds-cs__confirm').textContent(), /Amount: 100 → 130/);
    await keyActivate(button('Discard staged changes'), 'Enter');
    assert.deepEqual(await events(), [{ name: 'discard', payload: { id: 'CS-B', version: 2, draft: valueB2 } }]);
    assert.equal(await page.locator('.mds-cs__confirm').count(), 0);
    assert.deepEqual(await page.evaluate(() => window.changeSet.draft), valueB2, 'External callback waits for the parent to accept new controlled values');
    assert.equal(await page.locator('.mds-cs__row').count(), 2);
    await mount('change', { ...callbacks, id: 'CS-B', version: 2, original: originalB, value: originalB });
    assert.equal(await page.locator('.mds-cs__row').count(), 0);
    assert(await button('Discard').isDisabled());
  });
  await check(['ResolutionDrawer'], 'missing evidence is explicit and exact request assignment and back callbacks work from native keyboard', ['evidence-missing', 'evidence-attached', 'request-evidence', 'assign', 'back', 'keyboard', 'focus'], async () => {
    await mount('resolution', { reset: 'missing-resolution' });
    assert.match(await text(), /Required confirmationMissing/);
    assert.equal(await page.getByRole('link', { name: 'Attached source', exact: true }).getAttribute('href'), '#source');
    assert.equal(await page.locator('.mds-res__field[data-flag="true"]').textContent(), 'Observed balance0');
    assert.match(await text(), /CASE-17 · Version 4 · Journal J-17 · Missing/);
    for (const [index, label] of ['Request evidence', 'Assign', 'Back to queue'].entries()) await keyActivate(button(label), index % 2 ? 'Enter' : 'Space');
    assert.deepEqual(await events(), [{ name: 'request' }, { name: 'assign' }, { name: 'back' }]);
    await mount('resolution', { attachedOnly: true, reset: 'missing-resolution' });
    assert(!/Required confirmationMissing/.test(await text()));
    assert.match(await text(), /Attached sourceFixture source/);
  });
  await check(['ResolutionDrawer'], 'defer escalate and reopen are exact public keyboard actions with independent primary placement and rationale gates', ['defer', 'escalate', 'reopen', 'primary', 'secondary', 'reason-required', 'disabled', 'keyboard'], async () => {
    for (const primary of ['defer', 'escalate', 'reopen']) {
      await mount('resolution', { primary, reset: `action-resolution-${primary}` });
      const label = primary[0].toUpperCase() + primary.slice(1);
      assert.equal(await page.locator('.mds-res__foot button').last().textContent(), label);
      assert(await button('Resolve exception').isDisabled());
      await keyActivate(button(label));
      assert.deepEqual(await events(), [{ name: primary }]);
      for (const other of ['defer', 'escalate', 'reopen'].filter(action => action !== primary)) await keyActivate(button(other[0].toUpperCase() + other.slice(1)), 'Enter');
      assert.deepEqual(await events(), [primary, ...['defer', 'escalate', 'reopen'].filter(action => action !== primary)].map(name => ({ name })));
    }
  });
  await check(['ResolutionDrawer'], 'controlled rationale classification and detail proposals wait for parent acceptance then resolve exact reason', ['controlled', 'reason-required', 'classification', 'detail', 'resolve', 'keyboard', 'recovery'], async () => {
    await mount('resolution', { controlled: true, reason: '', reset: 'controlled-resolution' });
    const reason = page.getByRole('textbox', { name: 'Resolution rationale', exact: true });
    await reason.fill('  AUTH-17 resolves the gap  ');
    assert.equal(await reason.inputValue(), '');
    assert(await button('Resolve exception').isDisabled());
    assert.deepEqual((await events()).at(-1), { name: 'reason', payload: '  AUTH-17 resolves the gap  ' });
    const cause = page.getByRole('combobox', { name: 'Classification', exact: true });
    await cause.selectOption('mapping');
    assert.equal(await cause.inputValue(), '');
    assert.deepEqual((await events()).at(-1), { name: 'cause', payload: 'mapping' });
    const note = page.getByRole('textbox', { name: 'Additional detail', exact: true });
    await note.fill('Exact mapping explanation');
    assert.equal(await note.inputValue(), '');
    assert.deepEqual((await events()).at(-1), { name: 'note', payload: 'Exact mapping explanation' });
    await mount('resolution', { controlled: true, reason: '  AUTH-17 resolves the gap  ', cause: 'mapping', note: 'Exact mapping explanation', reset: 'controlled-resolution' });
    assert.equal(await cause.inputValue(), 'mapping');
    assert.equal(await note.inputValue(), 'Exact mapping explanation');
    await keyActivate(button('Resolve exception'), 'Enter');
    assert.deepEqual(await events(), [{ name: 'resolve', payload: '  AUTH-17 resolves the gap  ' }]);
  });
  await check(['ResolutionDrawer'], 'uncontrolled rationale gates resolve without disabling missing-evidence recovery and fallback chooses an available primary', ['uncontrolled', 'reason-required', 'whitespace', 'resolve', 'primary-fallback', 'evidence-missing', 'keyboard'], async () => {
    await mount('resolution', { primary: 'defer', reset: 'local-resolution' });
    const reason = page.getByRole('textbox', { name: 'Resolution rationale', exact: true });
    await reason.fill('   ');
    assert(await button('Resolve exception').isDisabled());
    await reason.fill('  Reconciled by AUTH-17  ');
    assert(!(await button('Resolve exception').isDisabled()));
    await keyActivate(button('Resolve exception'));
    assert.deepEqual((await events()).at(-1), { name: 'resolve', payload: '  Reconciled by AUTH-17  ' });
    assert.match(await text(), /Required confirmationMissing/);
    await mount('resolution', { primary: 'resolve', noResolve: true, reset: 'fallback-resolution' });
    assert.equal(await button('Resolve exception').count(), 0);
    assert.equal(await page.locator('.mds-res__foot button').last().textContent(), 'Escalate');
    await keyActivate(button('Escalate'), 'Enter');
    assert.deepEqual(await events(), [{ name: 'escalate' }]);
    await mount('resolution', { noReason: true, reset: 'optional-reason-resolution' });
    assert.equal(await page.getByRole('textbox', { name: 'Resolution rationale', exact: true }).count(), 0);
    await keyActivate(button('Resolve exception'));
    assert.deepEqual(await events(), [{ name: 'resolve', payload: '' }]);
  });
  await check(['ResolutionDrawer'], 'severity labels and tones retain real declared operation semantics', ['blocked', 'action', 'exception', 'review', 'ready', 'info'], async () => {
    const tones = { blocked: 'exception', action: 'exception', exception: 'exception', review: 'review', ready: 'pass', info: 'info' };
    for (const [severity, tone] of Object.entries(tones)) {
      await mount('resolution', { severity, reset: 'severity-resolution' });
      assert.equal(await page.locator('.mds-res__sev').getAttribute('data-tone'), tone);
      assert((await page.locator('.mds-res__sev').textContent()).includes(severity[0].toUpperCase() + severity.slice(1)));
    }
  });
  await check(['ResolutionDrawer'], 'uncontrolled rationale cannot authorize another case or record version and fresh rationale recovers', ['uncontrolled', 'case-change', 'version-change', 'reset', 'disabled', 'recovery', 'keyboard'], async () => {
    for (const [index, next] of [{ caseId: 'CASE-18' }, { version: 5 }].entries()) {
      const reset = `scoped-resolution-${index}`;
      await mount('resolution', { reset });
      const reason = page.getByRole('textbox', { name: 'Resolution rationale', exact: true });
      await reason.fill('Authority only for CASE-17 version 4');
      assert(!(await button('Resolve exception').isDisabled()));
      await mount('resolution', { ...next, reset });
      assert.equal(await reason.inputValue(), '');
      assert(await button('Resolve exception').isDisabled());
      assert.deepEqual(await events(), []);
      await reason.fill('Fresh case and version authority');
      await keyActivate(button('Resolve exception'), 'Enter');
      assert.deepEqual((await events()).at(-1), { name: 'resolve', payload: 'Fresh case and version authority' });
    }
  });
  assert.deepEqual(errors, [], 'No rendering/callback exceptions');
  assert.deepEqual(requests, [], 'All evidence is caller supplied; no external network requests');
} finally {
  const exports = {};
  for (const [name, path] of Object.entries(imports)) {
    const source = `components/${path}.jsx`;
    const sha256 = createHash('sha256').update(await readFile(resolve(root, source))).digest('hex');
    exports[name] = { source, sha256, cases: cases.filter(item => item.exports.includes(name)).map(({ exports, ...item }) => item) };
  }
  const summary = { passed: cases.filter(item => item.status === 'passed').length, failed: cases.filter(item => item.status !== 'passed').length };
  const report = { schemaVersion: 1, source: 'Current source bundled in memory; local production React; synthetic caller data; no live/CDN', exports, cases, summary, errors };
  const output = resolve(process.env.MERIDIAN_FULL_AUDIT_OUTPUT || '/tmp/pr3501-full-audit');
  await mkdir(output, { recursive: true });
  await writeFile(resolve(output, 'domain-operations-state-results.json'), JSON.stringify(report, null, 2) + '\n');
  await browser.close();
  console.log(JSON.stringify(summary));
  if (summary.failed || errors.length) process.exitCode = 1;
}
