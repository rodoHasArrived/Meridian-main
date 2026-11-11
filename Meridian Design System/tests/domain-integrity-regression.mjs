#!/usr/bin/env node
// Synthetic source-level checks for domain presentation, redaction and callbacks.
import assert from 'node:assert/strict';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const source = await build({ absWorkingDir: root, bundle: true, write: false, resolveExtensions: ['.js', '.jsx', '.ts', '.tsx', '.json'],
  format: 'iife', platform: 'browser', define: { 'process.env.NODE_ENV': '"production"' },
  stdin: { resolveDir: root, loader: 'jsx', contents: `
    import React from 'react'; import { createRoot } from 'react-dom/client';
    import { VarianceCell } from './components/accounting/VarianceCell.jsx';
    import { TieOutPanel } from './components/accounting/TieOutPanel.jsx';
    import { BasisComparisonMatrix } from './components/accounting/BasisComparisonMatrix.jsx';
    import { CalculationTrace } from './components/accounting/CalculationTrace.jsx';
    import { ReconciliationPanel } from './components/accounting/ReconciliationPanel.jsx';
    import { JournalEntryForm } from './components/accounting/JournalEntryForm.jsx';
    import { PeerComparisonStrip } from './components/charts/PeerComparisonStrip.jsx';
    import { ChartFrame } from './components/charts/ChartFrame.jsx';
    import { ChartAnnotations } from './components/charts/ChartAnnotations.jsx';
    import { EvidenceInspector } from './components/operations/EvidenceInspector.jsx';
    import { EvidencePreview } from './components/operations/EvidencePreview.jsx';
    import { AnalyticalField } from './components/charts/AnalyticalField.jsx';
    import { CoverageMatrix } from './components/charts/CoverageMatrix.jsx';
    import { Treemap } from './components/charts/Treemap.jsx';
    import { CorrelationHeatmap } from './components/charts/CorrelationHeatmap.jsx';
    import { TimeframeSwitcher } from './components/charts/TimeframeSwitcher.jsx';
    import { ChartCursor } from './components/charts/useChartCrosshair.js';
    import { PnLCalendar } from './components/charts/PnLCalendar.jsx';
    import { AuthorityStack, AuthorityPanel } from './components/core/ValueAuthority.jsx';
    import { DecisionSnapshot, DecisionSnapshotPanel } from './components/decision/DecisionSnapshot.jsx';
    import { MaturityLadder } from './components/charts/MaturityLadder.jsx';
    import { CashFlowLadder } from './components/charts/CashFlowLadder.jsx';
    import { DurationContribution } from './components/charts/DurationContribution.jsx';
    import { knownAmount, displayAmount } from './components/accounting/knownAmounts.js';
    import { FxRevaluationTable } from './components/accounting/FxRevaluationTable.jsx';
    import { TaxLotTable } from './components/accounting/TaxLotTable.jsx';
    import { Figure } from './components/charts/Figure.jsx';
    window.Fixture = { displayAmount, FxRevaluationTable, TaxLotTable, Figure, knownAmount, MaturityLadder, CashFlowLadder, DurationContribution, AuthorityStack, AuthorityPanel, DecisionSnapshot, DecisionSnapshotPanel, EvidenceInspector, EvidencePreview, AnalyticalField, CoverageMatrix, Treemap, CorrelationHeatmap, TimeframeSwitcher, ChartCursor, PnLCalendar, React, createRoot, VarianceCell, TieOutPanel, BasisComparisonMatrix,
      CalculationTrace, ReconciliationPanel, JournalEntryForm, PeerComparisonStrip, ChartFrame, ChartAnnotations };`
  }
});
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ['--no-sandbox'] });
const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
const errors = [];
page.on('pageerror', error => errors.push(error.message));
try {
  await page.setContent('<!doctype html><html lang="en"><head><title>Synthetic domain integrity</title></head><body><main><h1>Domain integrity fixtures</h1><div id="fixture"></div></main></body></html>');
  await page.addScriptTag({ content: source.outputFiles[0].text });
  await page.evaluate(() => {
    const { React, createRoot, ...DS } = window.Fixture;
    const root = createRoot(document.getElementById('fixture'));
    window.revision = 0;
    window.events = [];
    function CursorProbe({ count }) {
      const cursor=DS.ChartCursor.useChartCrosshair(count);
      return React.createElement('div', { ...cursor.bind, id: 'cursor', style: { ...cursor.bind.style, width: 200, height: 40 } }, cursor.index === null ? 'no cursor' : String(cursor.index));
    }
    function SyncProbe({ id, count }) {
      const cursor=DS.ChartCursor.useSyncedCursor(count);
      return React.createElement('div', { ...cursor.bind, id, style: { ...cursor.bind.style, width: 200, height: 40 } }, cursor.index===null?'no cursor':String(cursor.index));
    }
    function Fixture({ kind, config, revision }) {
      React.useLayoutEffect(() => { document.getElementById('fixture').dataset.revision = String(revision); }, [revision]);
      if (kind === 'variance') return React.createElement(DS.VarianceCell, { actual: config.restricted ? 987654 : null,
        expected: config.restricted ? 123456 : 20, state: 'within', showReference: true,
        condition: config.restricted ? 'restricted' : undefined, onSelect: event => window.events.push(event) });
      if (kind === 'tie') return React.createElement(DS.TieOutPanel, {
        sourceA: { label: 'Statement', value: 987654, condition: config.restrictedItem ? 'confirmed' : 'restricted' },
        sourceB: { label: 'Ledger', value: 987654 },
        items: config.restrictedItem ? [{ label: 'Private adjustment 123456', value: 123456, condition: 'restricted', evidence: 'Private evidence 654321' }] : [],
        defaultExpanded: true, status: 'tied', onAccept: () => window.events.push('accept'), onResolve: () => window.events.push('resolve') });
      if (kind === 'strict-fx') return React.createElement(DS.FxRevaluationTable, { rows: [{ currency: 'EUR', local: '12oops', bookedRate: 1, currentRate: 1 }] });
      if (kind === 'strict-lot') return React.createElement(DS.TaxLotTable, { lots: [{ id: 'lot-1', acquired: '2026-01-01', quantity: 1, costBasis: 10, marketValue: '12oops' }], asOf: '2026-06-01' });
      if (kind === 'figure') return React.createElement(DS.Figure, { title: config.unnamed ? undefined : 'Synthetic exact figure', geometry: config.geometry, series: [] });
      if (kind === 'sync') return React.createElement(DS.ChartCursor.ChartCursorSync, null, config.hideLeft ? React.createElement('div', { style: { height: 40 }, key: 'placeholder' }) : React.createElement(SyncProbe, { id: 'sync-left', count: 3, key: 'left' }), React.createElement(SyncProbe, { id: 'sync-right', count: config.invalidCount==='fractional'?1.5:config.invalidCount==='infinite'?Infinity:2, key: 'right' }));
      if (kind === 'tie-whitespace') return React.createElement(DS.TieOutPanel, { sourceA: { label: 'Source A', value: config.item ? 0 : '   ' }, sourceB: { label: 'Source B', value: 0 }, items: config.item ? [{ label: 'Adjustment', value: '   ' }] : [], defaultExpanded: true, status: 'tied', onAccept() {}, onResolve() {} });
      if (kind === 'basis') return React.createElement(DS.BasisComparisonMatrix, { title: 'Basis comparison',
        bases: [{ id: 'a', label: 'Tax' }, { id: 'b', label: 'Book' }],
        rows: [{ id: 'amount', label: 'Value', values: { a: { value: 987654, condition: 'restricted', override: '987654' }, b: { value: 123456 } } }] });
      if (kind === 'calculation') return React.createElement(DS.CalculationTrace, { title: 'Calculation', condition: 'restricted', defaultLevel: 'audit',
        result: { value: '987654' }, steps: [{ label: 'Input', value: '123456', condition: 'restricted' }],
        technicalFormula: '987654 = 123456 + adjustment', overrides: [{ value: '987654' }] });
      if (kind === 'peer') return React.createElement(DS.PeerComparisonStrip, { ariaLabel: 'Peer comparison', showRank: config.showRank !== false,
        metrics: [{ label: 'Missing subject', value: null, peers: [10, 20, NaN, Infinity] },
          { label: 'Finite subject', value: 15, peers: [10, 20, NaN, Infinity] },
          { label: 'Infinite subject', value: Infinity, peers: [10, 20] }] });
      if (kind === 'journal') return React.createElement(React.Fragment, null,
        React.createElement(DS.JournalEntryForm, { accounts: ['Cash'], onPost() {} }),
        React.createElement(DS.JournalEntryForm, { accounts: ['Other cash'], onPost() {} }));
      if (kind === 'reconciliation') return React.createElement(DS.ReconciliationPanel, {
        left: { title: 'Statement', items: [{ id: 's-1', ref: 'REF1', amount: 10 }, { id: 's-2', ref: 'REF2', amount: 20, matched: true }] },
        right: { title: 'Ledger', items: [{ id: 'l-1', ref: 'BOOK1', amount: 30 }] },
        onToggleItem: config.readOnly ? undefined : (id, matched) => window.events.push({ id, matched }),
        onToggleAll: config.readOnly ? undefined : (side, matched) => window.events.push({ side: side.title, ids: side.items.map(item => item.id), matched }) });
      if (kind === 'annotations') return React.createElement(DS.ChartFrame, { n: 3, height: 200,
        ariaLabel: 'Synthetic annotated chart', ariaDescription: config.description || 'Base description',
        panes: [{ key: 'price', domain: [0, 20], ticks: 0 }], children: geometry => React.createElement(React.Fragment, null,
          !config.hideFirst && React.createElement(DS.ChartAnnotations, { g: geometry, pane: 'price', events: [{ index: 0, label: 'First event' }] }),
          !config.hideSecond && React.createElement(DS.ChartAnnotations, { g: geometry, pane: 'price', callouts: [{ index: 1, value: 10, text: 'Second annotation' }] })) });
      if (kind === 'exposure') {
        const common = { condition: config.condition, label: config.condition ? 'Private row 987654' : 'Public row', id: 'a' };
        const format=value=> { if (config.condition === 'restricted') throw new Error('Restricted exposure reached formatter'); return String(value); };
        if(config.component === 'MaturityLadder') return React.createElement(DS.MaturityLadder, { items: config.empty ? [] : [{ ...common, primary: config.missing ? null : 987654, comparison: 123456 }], valueFormat: format, showTable: false, description: 'Synthetic maturity' });
        if(config.component === 'CashFlowLadder') return React.createElement(DS.CashFlowLadder, { items: config.empty ? [] : [{ ...common, assets: config.missing ? null : 987654, liabilities: 123456 }], valueFormat: format, showTable: false, description: 'Synthetic cash flows' });
        return React.createElement(DS.DurationContribution, { items: config.empty ? [] : [{ ...common, weight: config.missing ? null : 20, duration: config.missing ? null : 5, contribution: config.missing ? null : 987654, comparisonContribution: 123456 }], valueFormat: format, showTable: false, description: 'Synthetic duration' });
      }
      if (kind === 'authority') return React.createElement(React.Fragment, null,
        React.createElement(DS.AuthorityStack, { authority: { candidates: [{ stage: 'posted', value: 987654, condition: 'restricted' }] }, format() { throw new Error('Restricted value reached formatter'); } }),
        React.createElement(DS.AuthorityPanel, { authority: { candidates: [{ stage: 'posted', value: 987654, condition: 'restricted' }] }, format() { throw new Error('Restricted value reached formatter'); } }));
      if (kind === 'snapshot') return React.createElement(DS.DecisionSnapshotPanel, { snapshot: DS.DecisionSnapshot.capture({ id: 'synthetic', capturedAt: '2026-06-01T00:00:00Z', object: { type: 'journal', id: '1', version: 0 }, guards: [{ id: 'policy', label: 'Policy', state: 'blocking' }] }), current: { object: { type: 'order', id: '1', version: 0 } } });
      if (kind === 'inspector') return React.createElement(DS.EvidenceInspector, { value: config.restricted ? '987654' : 0, caption: '987654 context', condition: config.restricted ? 'restricted' : 'confirmed' });
      if (kind === 'preview') return React.createElement(DS.EvidencePreview, { title: 'Withheld evidence', condition: 'restricted', before: '987654', highlight: '123456', after: '987654', image: 'data:image/png;base64,AA', textEquivalent: '987654', extraction: '987654', metadata: [{ label: 'Amount', value: '987654' }], onOpenOriginal: () => window.events.push('open'), onCompare: () => window.events.push('compare') });
      if (kind === 'field') return React.createElement(DS.AnalyticalField, { items: [{ id: 'a', label: 'Source A', condition: 'confirmed' }, { id: 'b', label: 'Source B', condition: 'restricted', value: '987654' }, { id: 'c', label: 'Source C', condition: 'missing' }], selectedId: 'removed', columns: 2, onSelect: item => window.events.push(item.id) });
      if (kind === 'coverage') return React.createElement(DS.CoverageMatrix, { rows: ['A', 'B'], cols: ['1', '2'], onCellClick: (row, col) => window.events.push(row.id + col.id) });
      if (kind === 'treemap') return React.createElement(DS.Treemap, { items: [{ label: 'Alpha', value: 5 }, { label: 'Invalid', value: Infinity }, { label: 'Beta', value: 10 }], onSelect: item => window.events.push(item.label) });
      if (kind === 'correlation') return React.createElement(DS.CorrelationHeatmap, { labels: ['A', 'B'], matrix: [[1, null], [Infinity, 1]] });
      if (kind === 'timeframe') return React.createElement(DS.TimeframeSwitcher, { options: ['1D', '1W', '1M'], value: config.value, onChange: value => window.events.push(value) });
      if (kind === 'pnl') return React.createElement(DS.PnLCalendar, { month: '2026-06', values: { '2026-06-01': 10, '2026-06-30': -2, '2026-06-31': 900, '2026-07-01': 1000 }, valueFormat: value => String(value) });
      if (kind === 'cursor') return React.createElement(CursorProbe, { count: config.count });
      throw new Error('Unknown fixture ' + kind);
    }
    window.mount = (kind, config = {}) => {
      const revision = ++window.revision;
      window.events = [];
      root.render(React.createElement(Fixture, { kind, config, revision }));
      return revision;
    };
  });
  async function mount(kind, config = {}) {
    const revision = await page.evaluate(({ kind, config }) => window.mount(kind, config), { kind, config });
    await page.waitForFunction(revision => document.getElementById('fixture').dataset.revision === String(revision), revision);
  }
  const parsedAmounts=await page.evaluate(()=>{
    const known=window.Fixture.knownAmount;
    return {valid:[0,-12.5,'12.50','1,234.50','($1,234.50)','−12.5'].map(known),invalid:['12oops','1e3','12.3.4','1,23',true,'   ',Infinity,{value:100,condition:'restricted'}].map(known)};
  });
  assert.deepEqual(parsedAmounts.valid,[0,-12.5,12.5,1234.5,-1234.5,-12.5]);
  assert.deepEqual(parsedAmounts.invalid,Array(8).fill(null));
  const provenance=await page.evaluate(()=>{const missing={value:null,condition:'missing',authority:'source',evidence:[{label:'Permitted source',ref:'DOC-1'}],clocks:{sourceAsOf:'2026-06-01T00:00:00Z'}};return{missing:window.Fixture.displayAmount(missing),expected:missing,restricted:window.Fixture.displayAmount({value:987654,condition:'restricted',label:'Private identity',evidence:[{label:'Private source'}]})}});
  assert.deepEqual(provenance.missing,provenance.expected,'Unavailable unrestricted amounts retain permitted provenance facets');
  assert.deepEqual(provenance.restricted,{value:null,condition:'restricted'},'Restricted display helper withholds numeric and evidence metadata');
  await mount('strict-fx');
  assert.equal(await page.locator('tfoot .mds-env__v').filter({ hasText: /^Missing$/ }).count(), 3, 'Malformed FX input cannot turn into a known translated total');
  await mount('strict-lot');
  assert.equal(await page.locator('tfoot .mds-env__v').filter({ hasText: /^Missing$/ }).count(), 2, 'Malformed market value cannot produce a partial numeric value/gain total');
  for (const geometry of ['trace','ladder','distribution']) {
    await mount('figure', { geometry });
    assert.equal(await page.getByRole('img', { name: 'Synthetic exact figure', exact: true }).count(), 1);
  }
  await mount('figure', { unnamed: true });
  assert.equal(await page.locator('svg').getAttribute('aria-hidden'), 'true', 'An unnamed plot remains presentational, matching ChartFrame');
  await mount('sync');
  await page.locator('#sync-left').hover({ position: { x: 130, y: 10 } });
  assert.equal(await page.locator('#sync-left').textContent(), '2');
  assert.equal(await page.locator('#sync-right').textContent(), 'no cursor', 'Shorter sibling never receives an invalid cursor');
  await page.locator('#sync-right').hover({ position: { x: 130, y: 10 } });
  assert.equal(await page.locator('#sync-left').textContent(), '1');
  await mount('sync', { hideLeft: true });
  assert.equal(await page.locator('#sync-right').textContent(), '1');
  await page.mouse.move(800,600);
  assert.equal(await page.locator('#sync-right').textContent(), 'no cursor');
  for (const invalidCount of ['fractional','infinite']) {
    await mount('sync', {invalidCount});
    await page.locator('#sync-left').hover({position:{x:80,y:10}});
    assert.equal(await page.locator('#sync-left').textContent(),'1');
    assert.equal(await page.locator('#sync-right').textContent(),'no cursor','Invalid count cannot receive a synchronized index');
  }
  await mount('variance');
  assert.equal(await page.locator('.mds-var').getAttribute('data-state'), 'missing-actual');
  assert.equal(await page.locator('.mds-var__actual').textContent(), '—');
  assert.equal(await page.locator('.mds-var__delta').count(), 0);
  await mount('variance', { restricted: true });
  assert.equal(await page.locator('.mds-var__actual').textContent(), 'Restricted');
  assert.equal(await page.locator('.mds-var__ref').count(), 0);
  await page.locator('.mds-var').focus(); await page.keyboard.press('Enter'); await page.keyboard.press('Space');
  assert.equal(await page.evaluate(()=>window.events.length),2,'Variance keyboard activates exactly once per key');
  assert.deepEqual(await page.evaluate(() => window.events[0]), { actual: null, expected: null, variance: null, relative: null, state: 'not-comparable', tolerance: 0 });
  assert(!/987[,.]?654|123[,.]?456/.test(await page.locator('#fixture').textContent()));

  for (const restrictedItem of [false, true]) {
    await mount('tie', { restrictedItem });
    assert.match(await page.locator('.mds-tie__status').textContent(), /Not tied — input missing/);
    assert(await page.getByRole('button', { name: 'Resolve', exact: true }).isDisabled());
    assert(await page.getByRole('button', { name: 'Accept within tolerance', exact: true }).isDisabled());
    const text = await page.locator('#fixture').textContent();
    assert(!/123[,.]?456|654[,.]?321|Private adjustment|Private evidence/.test(text), 'A restricted reconciling item amount, label and evidence are not exposed');
    if (!restrictedItem) assert(!/987[,.]?654/.test(text.split('Ledger')[0]), 'The restricted source is not exposed');
  }
  for(const item of [false,true]){
    await mount('tie-whitespace',{item});
    assert.match(await page.locator('.mds-tie__status').textContent(),/Not tied — input missing/);
    assert(await page.getByRole('button',{name:'Resolve',exact:true}).isDisabled());
    assert.match(await page.locator('.mds-tie__block').textContent(),item?/has no amount/:/has no value/);
  }
  await mount('basis');
  assert(!/987[,.]?654/.test(await page.locator('#fixture').textContent()));
  assert.match(await page.locator('.mds-bcm__diff').last().textContent(), /Not comparable — input unavailable|Reference/);
  assert.equal(await page.getByRole('region', { name: 'Basis comparison scroll area' }).getAttribute('tabindex'), '0');
  await mount('calculation');
  assert(!/987[,.]?654|123[,.]?456/.test(await page.locator('#fixture').textContent()));
  assert.match(await page.locator('#fixture').textContent(), /Restricted/);

  await mount('peer');
  const rows = page.getByRole('table').getByRole('row');
  const missing = rows.filter({ has: page.getByRole('rowheader', { name: 'Missing subject', exact: true }) });
  assert.equal(await missing.getByRole('cell').nth(0).textContent(), 'no data');
  assert.equal(await missing.getByRole('cell').nth(2).textContent(), '—');
  assert.equal(await missing.getByRole('cell').nth(3).textContent(), '10.00, 20.00');
  const finite = rows.filter({ has: page.getByRole('rowheader', { name: 'Finite subject', exact: true }) });
  assert.equal(await finite.getByRole('cell').nth(1).textContent(), '15.00');
  assert.equal(await finite.getByRole('cell').nth(2).textContent(), '2 of 3');
  assert(!/NaN|Infinity/.test(await page.locator('#fixture').textContent()));
  await mount('peer', { showRank: false });
  assert.equal(await page.getByRole('table').getByText('2 of 3').count(), 0);

  await mount('journal');
  assert.equal(await page.getByLabel('Date', { exact: true }).count(), 2);
  assert.equal(await page.getByLabel('Reference', { exact: true }).count(), 2);
  assert.equal(await page.getByLabel('Memo', { exact: true }).count(), 2);
  const lists = await page.locator('datalist').evaluateAll(nodes => nodes.map(node => node.id));
  assert.equal(new Set(lists).size, 2, 'Independent forms have independent datalists');
  assert.equal(await page.getByRole('spinbutton', { name: 'Debit line 1', exact: true }).count(), 2);

  await mount('reconciliation');
  const rowMatch = page.getByRole('checkbox', { name: 'Match Statement item REF1', exact: true });
  await rowMatch.focus(); await page.keyboard.press('Space');
  assert.deepEqual(await page.evaluate(() => window.events[0]), { id: 's-1', matched: true });
  await page.getByRole('textbox', { name: 'Filter reconciliation items by memo or reference' }).fill('REF2');
  await page.getByRole('checkbox', { name: 'Mark all Statement items matched', exact: true }).click();
  assert.deepEqual(await page.evaluate(() => window.events.at(-1)), { side: 'Statement', ids: ['s-1', 's-2'], matched: true });
  await mount('reconciliation', { readOnly: true });
  assert(await page.getByRole('checkbox', { name: 'Match Statement item REF2', exact: true }).isDisabled());

  await mount('annotations');
  const svg = page.getByRole('img', { name: 'Synthetic annotated chart' });
  await page.waitForFunction(() => document.querySelector('svg[aria-label="Synthetic annotated chart"]')?.getAttribute('aria-describedby')?.split(/\s+/).length === 3);
  const described = async () => svg.evaluate(node => node.getAttribute('aria-describedby').split(/\s+/).map(id => document.getElementById(id)?.textContent).join(' '));
  assert.match(await described(), /Base description.*First event.*Second annotation/);
  await mount('annotations', { description: 'Updated description' });
  await page.waitForFunction(() => document.querySelector('svg[aria-label="Synthetic annotated chart"]')?.getAttribute('aria-describedby')?.split(/\s+/).length === 3);
  assert.match(await described(), /Updated description.*First event.*Second annotation/);
  await mount('annotations', { description: 'Updated description', hideFirst: true });
  await page.waitForFunction(() => document.querySelector('svg[aria-label="Synthetic annotated chart"]')?.getAttribute('aria-describedby')?.split(/\s+/).length === 2);
  assert(!/First event/.test(await described()));
  await mount('annotations', { hideFirst: true, hideSecond: true });
  await page.waitForFunction(() => document.querySelector('svg[aria-label="Synthetic annotated chart"]')?.getAttribute('aria-describedby')?.split(/\s+/).length === 1);
  for (const component of ['MaturityLadder', 'CashFlowLadder', 'DurationContribution']) {
    await mount('exposure', { component });
    assert.equal(await page.locator('.mds-exposure-unavailable').count(), 0, component+' valid control');
    for(const config of [{ condition: 'restricted' }, { condition: 'missing' }, { missing: true }]) {
      await mount('exposure', { component, ...config });
      assert.equal(await page.locator('.mds-exposure-unavailable').count(), 1, component+' states unusable inputs');
      assert(!/987654|123456|Private row/.test(await page.locator('#fixture').innerHTML()), component+' exposes no private row names, titles, bars or hidden table');
      assert.equal(await page.locator('table, svg, button').count(), 0);
    }
    await mount('exposure', { component, empty: true });
    assert.equal(await page.locator('.mds-exposure-unavailable').count(), 0, component+' empty population differs from unavailable input');
    await mount('exposure', { component });
    assert.equal(await page.locator('.mds-exposure-unavailable').count(), 0, component+' recovers with usable inputs');
  }
  await mount('authority');
  assert(!/987654/.test(await page.locator('#fixture').textContent()));
  assert.equal(await page.locator('.mds-auth__val, .mds-auth__tbl td.num').allTextContents().then(values => values.filter(value => value.startsWith('Restricted')).length), 2);
  await mount('snapshot');
  assert.match(await page.locator('.mds-snap__seal').textContent(), /Superseded/);
  assert.match(await page.locator('#fixture').textContent(), /Policy — blocking/);
  assert(!/Policy — satisfied/.test(await page.locator('#fixture').textContent()));
  const snapshotVersion = await page.evaluate(() => {
    const DS = window.Fixture;
    const snapshot = DS.DecisionSnapshot.capture({ object: { type: 'journal', id: '1', version: 0 }, evidence: [{ id: 'source', version: 0 }] });
    return { frozen: Object.isFrozen(snapshot) && Object.isFrozen(snapshot.object), findings: DS.DecisionSnapshot.verify(snapshot, { evidence: [{ id: 'source' }] }) };
  });
  assert(snapshotVersion.frozen);
  assert(snapshotVersion.findings.some(finding => finding.field === 'evidence' && finding.effect === 'void'), 'Evidence version zero differs from absent version');
  await mount('inspector');
  assert.equal(await page.locator('.mds-insp__val').textContent(), '0', 'Zero remains an observed selection');
  await mount('inspector', { restricted: true });
  assert(!/987654/.test(await page.locator('#fixture').textContent()));
  await mount('preview');
  assert.equal(await page.locator('img').count(), 0, 'Restricted content does not fetch its image');
  assert(!/987654|123456/.test(await page.locator('#fixture').textContent()));
  assert(await page.getByRole('button', { name: 'Open original' }).isDisabled());
  assert(await page.getByRole('button', { name: 'Compare', exact: true }).isDisabled());
  await mount('field');
  assert.equal(await page.getByRole('grid').getByRole('row').count(), 2);
  assert.equal(await page.getByRole('gridcell').count(), 3);
  assert.equal(await page.locator('button[tabindex="0"]').count(), 1, 'Removed selected item leaves a reachable first mark');
  const firstMark=page.getByRole('button', { name: 'Source A, confirmed' });
  await firstMark.focus(); await page.keyboard.press('ArrowRight'); await page.keyboard.press('Space');
  assert.deepEqual(await page.evaluate(() => window.events), ['b'], 'Space activates once after arrow navigation');
  assert(!/987654/.test(await page.locator('#fixture').innerHTML()), 'Restricted mark excludes the supplied value from accessible descriptions');
  await mount('coverage');
  assert.equal(await page.getByRole('grid').getByRole('row').count(), 3);
  await page.locator('.mds-covm__cell').first().focus();
  await page.keyboard.press('ArrowDown'); await page.keyboard.press('End'); await page.keyboard.press('Enter');
  assert.deepEqual(await page.evaluate(() => window.events), ['B2']);
  await mount('treemap');
  assert.equal(await page.getByRole('button').count(), 2);
  await page.getByRole('button').first().focus(); await page.keyboard.press('Space');
  assert.equal((await page.evaluate(() => window.events)).length, 1);
  await mount('correlation');
  assert.equal(await page.getByRole('grid').getByRole('row').count(), 3);
  assert(!/Infinity|NaN/.test(await page.locator('#fixture').innerHTML()));
  await mount('timeframe', { value: '1D' });
  assert.equal(await page.getByRole('radio').evaluateAll(nodes => nodes.filter(node => node.tabIndex === 0).length), 1);
  await page.getByRole('radio', { name: '1D', exact: true }).focus(); await page.keyboard.press('ArrowRight');
  assert.deepEqual(await page.evaluate(() => window.events), ['1W']);
  assert.equal(await page.getByRole('radio', { name: '1D', exact: true }).getAttribute('aria-checked'), 'true', 'Controlled selection waits for the caller');
  await mount('timeframe', { value: '1W' });
  assert.equal(await page.getByRole('radio', { name: '1W', exact: true }).getAttribute('aria-checked'), 'true');
  await mount('pnl');
  assert.equal(await page.locator('.mds-pnlcal__foot-val').textContent(), '8', 'Month total excludes other months and nonexistent dates');
  await mount('cursor', { count: 3 });
  await page.locator('#cursor').hover({ position: { x: 130, y: 10 } });
  assert.equal(await page.locator('#cursor').textContent(), '2');
  await mount('cursor', { count: 0 });
  await page.waitForFunction(() => document.querySelector('#cursor').textContent === 'no cursor');
  assert.deepEqual(errors, [], 'No runtime callback or rendering exceptions');
  console.log('PASS Domain integrity (29 source exports): absent/restricted values, nonfinite peers, reconciliation identity/boolean callbacks, labels/ids, annotation descriptions and cleanup');
} finally { await browser.close(); }
