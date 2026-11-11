#!/usr/bin/env node
// Public source contracts, isolated synthetic data, and actual native browser activation.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build, transform } from 'esbuild';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const sources = {
  AnalyticalFigure: 'components/charts/AnalyticalFigure.jsx',
  AsOfControl: 'components/charts/TimeframeSwitcher.jsx',
  DonutChart: 'components/charts/DonutChart.jsx',
  DotMatrix: 'components/charts/DotMatrix.jsx',
};
const report = {
  schemaVersion: 1,
  source: { auditedAt: new Date().toISOString(), runtime: 'Public JSX source bundled with project React; isolated synthetic fixtures; Chromium native keyboard; UTC timestamps in America/New_York' },
  exports: {}, errors: [],
};
for (const [name, file] of Object.entries(sources)) {
  report.exports[name] = { source: file, reviewedSourceHash: createHash('sha256').update(await readFile(resolve(root, file))).digest('hex'), cases: [] };
}
report.exports.AnalyticalFigure.scope = 'Owns the native details disclosure and continuity composition. Child actions, fetch/loading/retry and empty explanations are caller-owned; composed state tests do not claim fetch behavior.';
report.exports.AsOfControl.scope = 'Mode and supplied asOf are authoritative controlled values. Freeze/edit emit proposed Date values while parent values remain displayed; absent asOf permits local timestamp edits. Empty edits must not emit an invalid Date. No asynchronous loading/error API.';
report.exports.DonutChart.scope = 'Caller-controlled views use native Tab/Enter/Space buttons; no roving-arrow contract. Inputs are finite nonnegative parts of a real total. Empty/loading/error explanations are caller-owned.';
report.exports.DotMatrix.scope = 'Native cell drilldown is optional. Missing values remain null and differ from numeric zero. Shape and sign encoding are caller-controlled; no loading/error/selection API.';
const bundle = await build({
  absWorkingDir: root, bundle: true, write: false,
  resolveExtensions: ['.js', '.jsx', '.ts', '.tsx', '.json'],
  format: 'iife', platform: 'browser', define: { 'process.env.NODE_ENV': '"production"' },
  stdin: { resolveDir: root, loader: 'jsx', contents: `
    import React from 'react'; import {createRoot} from 'react-dom/client';
    import {AnalyticalFigure} from './components/charts/AnalyticalFigure.jsx';
    import {AsOfControl} from './components/charts/TimeframeSwitcher.jsx';
    import {DonutChart} from './components/charts/DonutChart.jsx';
    import {DotMatrix} from './components/charts/DotMatrix.jsx';
    window.Fixture={React,createRoot,AnalyticalFigure,AsOfControl,DonutChart,DotMatrix};
  ` },
});
const browser = await chromium.launch({ executablePath: process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH || '/usr/bin/chromium', args: ['--no-sandbox'] });
const context = await browser.newContext({ viewport: { width: 1366, height: 768 }, timezoneId: 'America/New_York', locale: 'en-US', reducedMotion: 'reduce' });
const page = await context.newPage();
page.setDefaultTimeout(3000);
page.on('pageerror', error => report.errors.push(error.message));
try {
  await page.clock.install({ time: new Date('2026-09-10T14:29:59.000Z') });
  await page.clock.pauseAt(new Date('2026-09-10T14:30:00.000Z'));
  await page.setContent(`<!doctype html><html lang="en"><head><title>Synthetic domain chart state contracts</title><style>
    :root{--chart-primary:#2d6fb3;--series-positive:#28754e;--series-negative:#ba4035;--series-actual:#2d6fb3;--series-prior:#96723f;--text-primary:#20252b;--text-secondary:#535c65;--text-muted:#606870;--border:#d2cdc3;--focus-ring:2px solid #2d6fb3;--focus-ring-offset:2px}
    body{font:14px Arial,sans-serif}button:focus-visible,summary:focus-visible{outline:var(--focus-ring)}
  </style></head><body><main><h1>Domain chart state contracts</h1><div id="fixture"></div></main></body></html>`);
  await page.addScriptTag({ content: bundle.outputFiles[0].text });
  await page.addScriptTag({ content: (await transform(String.raw`(() => {
    const {React,createRoot,...DS}=window.Fixture;
    const root=createRoot(document.getElementById('fixture'));
    const columns=[{id:'column:exact/001',label:'Observed'},{id:'column:exact/002',label:'Prior'},{id:'column:exact/003',label:'Gap'}];
    const rows=[{id:'row:exact/002',label:'Second source',sub:'Synthetic retained values',cells:[0,{value:-4,note:'Exact negative observation'},null]},{id:'row:exact/001',label:'First source',cells:[9,4,{value:null,note:'Unavailable'}]}];
    const segments=[{id:'part:exact/002',label:'Second source',value:30},{id:'part:exact/001',label:'First source',value:10}];
    const views=[{id:'allocation:exact/001',label:'Allocation',segments,centerLabel:'Allocation total',centerValue:'40'},{id:'risk:exact/002',label:'Risk',segments:[{label:'Risk total',value:5}],centerLabel:'Risk total',centerValue:'5'}];
    window.events=[];window.revision=0;
    function Fixture({kind,config,revision}) {
      React.useLayoutEffect(()=>{document.getElementById('fixture').dataset.revision=String(revision)},[revision]);
      const event=value=>window.events.push(value);
      if(kind==='AnalyticalFigure') return <DS.AnalyticalFigure title="Synthetic source coverage" question="What is known?" subtitle="Exact source evidence" unit="USD" asOf="2026-09-10T14:30Z" source={config.noSource?undefined:'Synthetic fixture'} condition={config.condition||'confirmed'} continuityState={config.continuityState} coverage="2 of 3 sources" age="5m" variant={config.variant} selectedLabel={config.zeroOnly?undefined:'Selected amount'} selectedValue={0} annotation="Zero is observed" method="Method fallback" note="Synthetic caveat" fallback={config.noFallback?null:<table><caption>Exact synthetic values</caption><tbody><tr><th>Observed</th><td>0</td></tr><tr><th>Unavailable</th><td>Missing</td></tr></tbody></table>}>
        <div data-testid="plot-evidence">{config.plotState==='loading'?<p role="status">Loading synthetic evidence</p>:config.plotState==='empty'?<p>No observations supplied</p>:config.plotState==='error'?<p role="alert">Synthetic source unavailable</p>:<p>Observed 0; unavailable Missing</p>}</div>
      </DS.AnalyticalFigure>;
      if(kind==='AsOfControl') return <DS.AsOfControl mode={config.mode} defaultMode={config.defaultMode} asOf={config.asOf} withDate={config.withDate} onChange={({mode,asOf})=>event({mode,asOf:asOf?asOf.toISOString():null,isDate:asOf instanceof Date})}/>;
      if(kind==='DonutChart') return <DS.DonutChart segments={config.empty?[]:config.zero?[{label:'Zero source',value:0}]:segments} views={config.emptyViews?[]:config.views?views:undefined} view={config.view} onViewChange={config.readOnly?undefined:id=>event({id})} total={config.total} centerLabel="Outer total" centerValue="40" fmt={v=>'USD '+v} pctFmt={v=>(v*100).toFixed(1)+'%'} ariaLabel={config.unnamed?undefined:'Synthetic allocation'} caption="Synthetic allocation exact values" legend={config.legend}/>;
      if(kind==='DotMatrix') {
        const cols=config.stringColumns?columns.map(c=>c.label):columns;
        return <DS.DotMatrix rows={config.empty?[]:rows} columns={config.empty?[]:cols} shape={config.shape} diverging={config.diverging} domainMax={config.domainMax} legend={config.legend} colLabels={config.colLabels} emptyMark="Missing" fmt={v=>String(v)} ariaLabel={config.unnamed?undefined:'Synthetic exposure'} caption="Synthetic exposure exact values" onCellClick={config.readOnly?undefined:(row,col,value)=>event({rowId:row.id,columnId:col.id,value,exactRow:rows.includes(row),exactColumn:columns.includes(col),columnLabel:col.label})}/>;
      }
      throw new Error('Unknown fixture '+kind);
    }
    window.mount=(kind,config={},reset=true)=>{const revision=++window.revision;window.events=[];root.render(<Fixture key={reset?revision:kind} kind={kind} config={config} revision={revision}/>);return revision;};
  })();`, { loader: 'jsx' })).code });
  async function mount(kind, config = {}, reset = true) {
    const revision = await page.evaluate(({kind,config,reset})=>window.mount(kind,config,reset), {kind,config,reset});
    await page.waitForFunction(revision=>document.getElementById('fixture').dataset.revision===String(revision), revision);
  }
  const events=()=>page.evaluate(()=>window.events);
  const focused=locator=>locator.evaluate(node=>node===document.activeElement);
  async function test(exportName, name, states, run) {
    let status='passed', detail;
    try { await run(); } catch(error) { status='failed';detail=error.message; }
    report.exports[exportName].cases.push({name,kind:states.includes('keyboard')?'keyboard':'state',states,status,...(detail?{detail}:{})});
    console.log(`${status}: ${exportName} — ${name}`);
  }
  await test('AnalyticalFigure','native disclosure toggles with Enter/Space and preserves exact fallback, provenance and zero', ['populated','keyboard','accessible-data'],async()=>{
    await mount('AnalyticalFigure');
    const details=page.locator('details'), summary=details.locator('summary');
    assert.equal(await details.getAttribute('open'),null);
    await summary.focus();await summary.press('Enter');
    assert.equal(await details.evaluate(n=>n.open),true);assert.equal(await focused(summary),true);
    assert.deepEqual(await details.locator('td').allTextContents(),['0','Missing']);
    assert.equal(await page.locator('.mds-figure__readout strong').textContent(),'0');
    assert.match(await page.locator('figcaption').textContent(),/What is known\?.*USD.*As of 2026-09-10T14:30Z/);
    assert.equal(await page.locator('.mds-figure__foot').textContent(),'Synthetic caveatSource: Synthetic fixture');
    await summary.press('Space');assert.equal(await details.evaluate(n=>n.open),false);assert.equal(await focused(summary),true);
    assert.deepEqual(await events(),[]);
  });
  await test('AnalyticalFigure','numeric zero alone is a selected readout', ['populated','zero'],async()=>{
    await mount('AnalyticalFigure',{zeroOnly:true});
    assert.equal(await page.locator('.mds-figure__readout strong').count(),1);
    assert.equal(await page.locator('.mds-figure__readout strong').textContent(),'0');
  });
  await test('AnalyticalFigure','condition changes retain exact plot evidence and expose stale/partial/offline/delayed status', ['retained','partial','stale','offline','delayed'],async()=>{
    await mount('AnalyticalFigure',{},false);
    await page.locator('[data-testid="plot-evidence"]').evaluate(n=>{window.retainedPlot=n});
    for(const condition of ['partial','stale','offline','delayed']) {
      await mount('AnalyticalFigure',{condition},false);
      assert.equal(await page.locator('[data-continuity-state]').getAttribute('data-continuity-state'),condition);
      assert.match(await page.locator('.mds-cont__status').textContent(),new RegExp(condition,'i'));
      assert.match(await page.locator('[data-testid="plot-evidence"]').textContent(),/Observed 0; unavailable Missing/);
      assert.equal(await page.locator('.mds-cont__body').evaluate(n=>n.inert),['stale','offline'].includes(condition));
      assert.equal(await page.locator('.mds-cont__retained').count(),condition==='delayed'?0:1);
      await page.locator('summary').focus();assert.equal(await focused(page.locator('summary')),true);
    }
  });
  await test('AnalyticalFigure','explicit error/recovering/live transitions preserve evidence and release inert busy state', ['error','loading','recovery','retained'],async()=>{
    await mount('AnalyticalFigure',{continuityState:'error'},false);
    await page.locator('[data-testid="plot-evidence"]').evaluate(n=>{window.retainedPlot=n});
    assert.equal(await page.locator('.mds-cont__body').evaluate(n=>n.inert),true);
    await mount('AnalyticalFigure',{continuityState:'recovering'},false);
    assert.equal(await page.locator('.mds-cont__body').getAttribute('aria-busy'),'true');
    assert.equal(await page.locator('.mds-cont__body').evaluate(n=>n.inert),false);
    assert.equal(await page.locator('[data-testid="plot-evidence"]').evaluate(n=>n===window.retainedPlot),true);
    await mount('AnalyticalFigure',{continuityState:'live'},false);
    assert.equal(await page.locator('.mds-cont__body').getAttribute('aria-busy'),null);
    assert.equal(await page.locator('.mds-cont__retained').count(),0);
    assert.equal(await page.locator('[data-testid="plot-evidence"]').evaluate(n=>n===window.retainedPlot),true);
  });
  await test('AnalyticalFigure','caller empty/loading/error composition remains visible and recovers with provenance', ['empty','loading','error','recovery'],async()=>{
    for(const [plotState,text] of [['empty','No observations supplied'],['loading','Loading synthetic evidence'],['error','Synthetic source unavailable'],['populated','Observed 0; unavailable Missing']]) {
      await mount('AnalyticalFigure',{plotState,noSource:true},false);
      assert.equal(await page.locator('[data-testid="plot-evidence"]').textContent(),text);
      assert.match(await page.locator('.mds-figure__foot').textContent(),/Source not supplied/);
      assert.equal(await page.locator('details').count(),1);
    }
  });
  await test('AnalyticalFigure','print expands exact fallback; small multiples and absent fallback follow documented variants', ['print','small-multiple','empty'],async()=>{
    await mount('AnalyticalFigure',{variant:'print'});assert.equal(await page.locator('details').evaluate(n=>n.open),true);assert.equal(await page.locator('details table').isVisible(),true);
    await mount('AnalyticalFigure',{variant:'small-multiple'});assert.equal(await page.locator('details').isVisible(),false);assert.equal(await page.locator('.mds-figure__question').isVisible(),false);
    await mount('AnalyticalFigure',{noFallback:true});assert.equal(await page.locator('summary').count(),0);
  });
  await test('AsOfControl','uncontrolled live ticks in UTC, native Freeze is exact, frozen time stops, and Return to live resumes', ['uncontrolled','live','freeze','return-live','keyboard'],async()=>{
    await mount('AsOfControl',{},false);
    const first=await page.locator('.mds-asof__time').textContent();
    assert.match(first,/^2026-09-10 14:30:00Z$/);
    await page.clock.runFor(1000);assert.equal(await page.locator('.mds-asof__time').textContent(),'2026-09-10 14:30:01Z');
    const freeze=page.getByRole('button',{name:'Freeze',exact:true});await freeze.focus();await freeze.press('Enter');
    assert.deepEqual(await events(),[{mode:'asof',asOf:'2026-09-10T14:30:01.000Z',isDate:true}]);
    const input=page.locator('input[type="datetime-local"]');
    assert.equal(await input.inputValue(),'2026-09-10T14:30');
    await page.clock.runFor(5000);assert.equal(await input.inputValue(),'2026-09-10T14:30');
    assert.equal(await page.locator('.mds-asof__time').getAttribute('title'),'2026-09-10 14:30:01Z');
    await input.focus();const back=page.getByRole('button',{name:'Return to live',exact:true});
    // Chromium exposes the native datetime-local editor as several keyboard segments.
    for(let index=0;index<8&&!await focused(back);index++) await page.keyboard.press('Tab');
    assert.equal(await focused(back),true);await back.press('Space');
    assert.deepEqual((await events()).at(-1),{mode:'live',asOf:null,isDate:false});
    assert.equal(await page.locator('.mds-asof__time').textContent(),'2026-09-10 14:30:06Z');
    await page.clock.runFor(1000);assert.equal(await page.locator('.mds-asof__time').textContent(),'2026-09-10 14:30:07Z');
  });
  await test('AsOfControl','controlled live/asof mode waits for parent and external timestamps replace the UTC input', ['controlled','live','asof','return-live'],async()=>{
    await mount('AsOfControl',{mode:'live',asOf:'2020-01-02T03:04:00Z'},false);
    await page.getByRole('button',{name:'Freeze',exact:true}).click();
    assert.equal((await events())[0].mode,'asof');assert.equal(await page.getByRole('button',{name:'Freeze',exact:true}).count(),1);
    await mount('AsOfControl',{mode:'asof',asOf:'2020-01-02T03:04:00Z'},false);
    const input=page.locator('input[type="datetime-local"]');
    assert.equal(await input.inputValue(),'2020-01-02T03:04','Parent-supplied asOf must be respected when entering controlled asof mode after an unaccepted Freeze request');
    await page.getByRole('button',{name:'Return to live',exact:true}).click();assert.equal(await input.count(),1);
    assert.deepEqual(await events(),[{mode:'live',asOf:null,isDate:false}]);
    await mount('AsOfControl',{mode:'asof',asOf:'2021-06-07T08:09:00Z'},false);await page.waitForFunction(()=>document.querySelector('input')?.value==='2021-06-07T08:09');
    assert.equal(await input.inputValue(),'2021-06-07T08:09');
    await mount('AsOfControl',{mode:'live',withDate:false},false);assert.match(await page.locator('.mds-asof__time').textContent(),/^14:30:\d{2}Z$/);
  });
  await test('AsOfControl','UTC edit emits an exact Date across non-UTC browser timezone; empty edit is ignored and recovers', ['asof','keyboard','empty','recovery'],async()=>{
    await mount('AsOfControl',{defaultMode:'asof'});
    const input=page.locator('input[type="datetime-local"]');assert.equal(await input.getAttribute('aria-label'),'As-of time (UTC)');
    assert.equal(await input.inputValue(),'2026-09-10T14:30');
    await input.fill('');assert.deepEqual(await events(),[]);
    await input.fill('2025-07-08T09:10');assert.deepEqual(await events(),[{mode:'asof',asOf:'2025-07-08T09:10:00.000Z',isDate:true}]);
    assert.equal(await input.inputValue(),'2025-07-08T09:10');assert.equal(await page.locator('.mds-asof__time').getAttribute('title'),'2025-07-08 09:10:00Z');
  });
  await test('AsOfControl','rejected edit emits proposed UTC instant while authoritative supplied timestamp stays displayed', ['controlled','asof','keyboard','recovery'],async()=>{
    await mount('AsOfControl',{mode:'asof',asOf:'2024-01-02T03:04:00Z'},false);
    const input=page.locator('input[type="datetime-local"]');assert.equal(await input.inputValue(),'2024-01-02T03:04');
    await input.fill('2025-07-08T09:10');
    assert.deepEqual(await events(),[{mode:'asof',asOf:'2025-07-08T09:10:00.000Z',isDate:true}]);
    assert.equal(await input.inputValue(),'2024-01-02T03:04');
    assert.equal(await page.locator('.mds-asof__time').getAttribute('title'),'2024-01-02 03:04:00Z');
    await mount('AsOfControl',{mode:'asof',asOf:'2025-07-08T09:10:00Z'},false);
    assert.equal(await input.inputValue(),'2025-07-08T09:10');assert.deepEqual(await events(),[]);
  });
  await test('DonutChart','controlled native view activation returns exact IDs without mutating active selection', ['views','controlled','keyboard'],async()=>{
    await mount('DonutChart',{views:true,view:'allocation:exact/001'},false);
    const risk=page.getByRole('tab',{name:'Risk',exact:true});await risk.focus();await risk.press('Enter');await risk.press('Space');
    assert.deepEqual(await events(),[{id:'risk:exact/002'},{id:'risk:exact/002'}]);assert.equal(await risk.getAttribute('aria-selected'),'false');assert.equal(await focused(risk),true);
    assert.deepEqual(await page.locator('svg text').allTextContents(),['40','Allocation total']);
    await mount('DonutChart',{views:true,view:'risk:exact/002'},false);assert.equal(await risk.getAttribute('aria-selected'),'true');assert.deepEqual(await page.locator('svg text').allTextContents(),['5','Risk total']);
    assert.deepEqual(await page.locator('table tbody tr').allTextContents(),['Risk totalUSD 5100.0%']);
  });
  await test('DonutChart','unknown view falls back to first; optional callback preserves controlled readonly view', ['views','controlled'],async()=>{
    await mount('DonutChart',{views:true,view:'unknown',readOnly:true});assert.equal(await page.getByRole('tab',{name:'Allocation',exact:true}).getAttribute('aria-selected'),'true');
    await page.getByRole('tab',{name:'Risk',exact:true}).click();assert.deepEqual(await events(),[]);assert.equal(await page.getByRole('tab',{name:'Allocation',exact:true}).getAttribute('aria-selected'),'true');
  });
  await test('DonutChart','legend, accessible table and explicit total preserve source order and fraction units', ['legend','accessible-data','populated'],async()=>{
    await mount('DonutChart',{total:80});
    assert.equal(await page.getByRole('img',{name:'Synthetic allocation'}).count(),1);
    assert.deepEqual(await page.locator('table thead th').allTextContents(),['Part','Value','Share']);
    assert.deepEqual(await page.locator('table tbody tr').allTextContents(),['Second sourceUSD 3037.5%','First sourceUSD 1012.5%']);
    assert.deepEqual(await page.locator('ul li').allTextContents(),['Second sourceUSD 3037.5%','First sourceUSD 1012.5%']);
    assert.equal(await page.locator('svg path').count(),2);assert.doesNotMatch(await page.locator('svg').innerHTML(),/NaN|Infinity/);
    await mount('DonutChart',{legend:false});assert.equal(await page.locator('ul').count(),0);assert.equal(await page.locator('table tbody tr').count(),2);
    await mount('DonutChart',{unnamed:true});assert.equal(await page.locator('svg').getAttribute('aria-hidden'),'true');assert.equal(await page.locator('table').count(),0);
  });
  await test('DonutChart','empty, zero-total and empty-views remain finite and recover without stale segments', ['empty','zero-total','recovery'],async()=>{
    for(const config of [{empty:true},{emptyViews:true,empty:true},{zero:true},{total:0}]) {
      await mount('DonutChart',config,false);assert.equal(await page.locator('svg path').count(),0);assert.doesNotMatch(await page.locator('svg').innerHTML(),/NaN|Infinity/);
      for(const value of await page.locator('table tbody td:last-child').allTextContents()) assert.equal(value,'0.0%');
    }
    await mount('DonutChart',{},false);assert.equal(await page.locator('svg path').count(),2);assert.deepEqual(await page.locator('table tbody th').allTextContents(),['Second source','First source']);
  });
  await test('DotMatrix','native Enter/Space drilldown returns original row/column objects and distinct zero/null values', ['keyboard','zero','missing'],async()=>{
    await mount('DotMatrix');const cells=page.locator('button[title]');
    await cells.first().focus();await cells.first().press('Enter');await cells.nth(2).focus();await cells.nth(2).press('Space');
    assert.deepEqual(await events(),[
      {rowId:'row:exact/002',columnId:'column:exact/001',value:0,exactRow:true,exactColumn:true,columnLabel:'Observed'},
      {rowId:'row:exact/002',columnId:'column:exact/003',value:null,exactRow:true,exactColumn:true,columnLabel:'Gap'},
    ]);assert.equal(await focused(cells.nth(2)),true);
    assert.equal(await cells.nth(1).getAttribute('title'),'Second source · Prior: -4 — Exact negative observation');
    assert.deepEqual(await page.locator('table tbody tr').first().locator('td').allTextContents(),['0','-4','no data']);
    assert.equal(await cells.first().locator('span').evaluate(n=>n.style.width),'4px');assert.equal(await cells.nth(2).textContent(),'Missing');
  });
  await test('DotMatrix','normalized string-column IDs and pointer callback retain exact source row and note', ['populated','dot'],async()=>{
    await mount('DotMatrix',{stringColumns:true});await page.locator('button[title]').nth(1).click();
    assert.deepEqual(await events(),[{rowId:'row:exact/002',columnId:'1',value:-4,exactRow:true,exactColumn:false,columnLabel:'Prior'}]);
  });
  await test('DotMatrix','controlled square/diverging encoding retains source values, uses sign hues and keeps zero visible', ['square','diverging','zero','missing'],async()=>{
    await mount('DotMatrix',{shape:'square',diverging:true,domainMax:9},false);const cells=page.locator('button[title]');
    const backgrounds=await cells.evaluateAll(nodes=>nodes.map(n=>getComputedStyle(n).backgroundColor));
    assert.notEqual(backgrounds[0],backgrounds[2]);assert.notEqual(backgrounds[1],backgrounds[4]);assert.equal(backgrounds[2],'rgba(0, 0, 0, 0)');
    assert.deepEqual(await page.locator('table tbody tr').first().locator('td').allTextContents(),['0','-4','no data']);
    await mount('DotMatrix',{shape:'dot',diverging:true,domainMax:9},false);
    assert.equal(await cells.nth(1).locator('span').evaluate(n=>getComputedStyle(n).backgroundColor),'rgb(186, 64, 53)');
    assert.equal(await cells.nth(4).locator('span').evaluate(n=>getComputedStyle(n).backgroundColor),'rgb(40, 117, 78)');
    assert.doesNotMatch(await page.locator('#fixture').innerHTML(),/NaN|Infinity/);
  });
  await test('DotMatrix','removing drilldown removes interactive targets while preserving exact accessible data', ['readonly','accessible-data'],async()=>{
    await mount('DotMatrix',{},false);assert.equal(await page.locator('button').count(),6);
    await mount('DotMatrix',{readOnly:true,legend:false,colLabels:false},false);assert.equal(await page.locator('button').count(),0);assert.equal(await page.locator('[tabindex]').count(),0);
    assert.equal(await page.getByRole('group',{name:'Synthetic exposure'}).count(),1);assert.deepEqual(await page.locator('table thead th').allTextContents(),['Row','Observed','Prior','Gap']);
    assert.deepEqual(await page.locator('table tbody th').allTextContents(),['Second source','First source']);assert.deepEqual(await events(),[]);
  });
  await test('DotMatrix','empty rows/columns and recovery have no stale controls, stale values or invalid geometry', ['empty','recovery'],async()=>{
    await mount('DotMatrix',{},false);await mount('DotMatrix',{empty:true},false);
    assert.equal(await page.locator('button').count(),0);assert.equal(await page.locator('table tbody tr').count(),0);assert.doesNotMatch(await page.locator('#fixture').innerHTML(),/NaN|Infinity/);
    await mount('DotMatrix',{},false);assert.equal(await page.locator('button').count(),6);assert.deepEqual(await page.locator('table tbody th').allTextContents(),['Second source','First source']);
    await page.locator('button[title]').last().click();assert.equal((await events())[0].value,null);
  });
} finally {
  await browser.close();
  for(const [name,file] of Object.entries(sources)) {
    const finalHash=createHash('sha256').update(await readFile(resolve(root,file))).digest('hex');
    if(finalHash!==report.exports[name].reviewedSourceHash) report.errors.push(`${name} source changed during execution; rerun against the final source`);
  }
  report.summary={cases:Object.values(report.exports).reduce((n,e)=>n+e.cases.length,0),failed:Object.values(report.exports).flatMap(e=>e.cases).filter(c=>c.status==='failed').length,browserErrors:report.errors.length};
  const output=process.env.DOMAIN_CHART_STATE_RESULTS||'/tmp/pr3501-full-audit/domain-chart-state-results.json';
  await mkdir(dirname(output),{recursive:true});await writeFile(output,JSON.stringify(report,null,2)+'\n');
  console.log(JSON.stringify({output,...report.summary}));
}
if(report.summary.failed||report.errors.length) process.exitCode=1;
