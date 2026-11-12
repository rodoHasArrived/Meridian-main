#!/usr/bin/env node
// Source-level callback, controlled selection and data-replacement contracts.
// The generated-bundle/library matrix is an independent consumer check.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build, transform } from 'esbuild';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const files = ['charts/ComparisonPlate', 'charts/ControlDependencyGraph', 'charts/LineageGraph', 'charts/OwnershipGraph', 'charts/TraceChart', 'charts/WatchlistStrip', 'trading/DepthLadder', 'trading/OptionChainTable', 'trading/Blotter'];
const contents = await Promise.all(files.map(file => readFile(resolve(root, 'components', `${file}.jsx`))));
const report = { schemaVersion: 1, source: { auditedAt: new Date().toISOString(), sha256: createHash('sha256').update(Buffer.concat(contents)).digest('hex'), runtime: 'Source bundled with project React 19; isolated synthetic fixtures' }, exports: {}, errors: [] };
const statesByCase={
  'native Enter and Space activate once; selected remains controlled':['populated','keyboard','focus','selected','controlled','zero','missing'],
  'callback preserves exact selected ID and shared domain geometry':['populated','selected','controlled','shared-domain'],
  'Enter and Space preserve exact node object; blocked meaning and selected rail persist':['populated','keyboard','focus','selected','controlled','blocked'],
  'uncontrolled view changes; controlled view emits without mutating':['populated','controlled','uncontrolled'],
  'native keyboard returns exact ID and waits for controlled selection':['populated','keyboard','focus','selected','controlled'],
  'native keyboard returns normalized exact ID and node fields; selection controlled':['populated','keyboard','focus','selected','controlled'],
  'arrow, Home and End commit exact series and index without mutating controlled selection':['populated','keyboard','focus','selected','controlled','missing','zero'],
  'pointer category selection defaults to actual series and preserves zero/missing table values':['populated','selected','missing','zero'],
  'native keyboard returns item ID or exact symbol fallback; selection controlled':['populated','keyboard','focus','selected','controlled'],
  'roving focus follows visual asks/bids; exact price and side callback':['populated','keyboard','focus'],
  'sorted keyboard and pointer return original row object; controlled strike does not mutate':['populated','keyboard','focus','selected','controlled'],
  'uncontrolled selection and zero quote distinguish missing values':['populated','selected','uncontrolled','zero'],
  'data shrink clamps focused strike before activation':['populated','keyboard','recovery'],
  'native row activation returns original order and source index; selected index stays controlled':['populated','keyboard','focus','selected','controlled'],
  'sort emits exact key without changing controlled row order; zero limit differs from missing':['populated','controlled','sort','zero','missing'],
  'controlled refresh preserves focused row DOM and updates active index':['populated','focus','controlled','selected'],
  'empty input has no selectable data targets and recovers with data':['empty','recovery','populated'],
  'omitted callback exposes read-only data without active selection targets':['read-only']
};
const source = await build({ absWorkingDir: root, bundle: true, write: false, resolveExtensions: ['.js', '.jsx', '.ts', '.tsx', '.json'], format: 'iife', platform: 'browser', define: { 'process.env.NODE_ENV': '"production"' }, stdin: { resolveDir: root, loader: 'jsx', contents: `
import React from 'react'; import { createRoot } from 'react-dom/client';
import { ComparisonPlate, PlateGrid } from './components/charts/ComparisonPlate.jsx';
${files.slice(1).map(file => `import { ${file.split('/').at(-1)} } from './components/${file}.jsx';`).join('\n')}
window.Fixture={React,createRoot,ComparisonPlate,PlateGrid,${files.slice(1).map(file => file.split('/').at(-1)).join(',')}};` } });
const browser = await chromium.launch({ executablePath: process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH || '/usr/bin/chromium', args: ['--no-sandbox'] });
const context = await browser.newContext({ viewport: { width: 1366, height: 768 }, timezoneId: 'UTC', locale: 'en-US', reducedMotion: 'reduce' });
const page = await context.newPage();
page.setDefaultTimeout(3000);
page.on('pageerror', error => report.errors.push(error.message));
try {
  await page.setContent('<!doctype html><html lang="en"><head><title>Synthetic chart and trading contracts</title></head><body><main><h1>Chart and trading contracts</h1><div id="fixture"></div></main></body></html>');
  await page.addScriptTag({ content: source.outputFiles[0].text });
  await page.addScriptTag({ content: (await transform(String.raw`(() => {
    const { React, createRoot, ...DS } = window.Fixture;
    const root = createRoot(document.getElementById('fixture'));
    const nodes = [{ id: 'source:exact/001', label: 'Source Alpha', kind: 'source', status: 'blocked', type: 'input' }, { id: 'output:exact/002', label: 'Output Beta', kind: 'output', status: 'ready', type: 'report' }];
    const edges = [{ from: nodes[0].id, to: nodes[1].id, type: 'requires', relationship: 'Owner', percent: 75 }];
    const rows = [{ strike: 110, call: { bid: 0, ask: 1 }, put: { bid: 2, ask: 3 } }, { strike: 90, call: { bid: 4, ask: 5 }, put: { bid: 0, ask: 1 } }, { strike: 100, call: { bid: 2, ask: 3 }, put: { bid: 3, ask: 4 } }];
    const orders = [{id:'ORDER:exact/002',time:'2026-09-10T14:30:00Z',symbol:'ZZZ',side:'Buy',qty:'0',limit:0,filled:'0',status:'Working'},{id:'ORDER:exact/001',time:'2026-09-10T14:29:00Z',symbol:'AAA',side:'Sell',qty:'100.125',filled:'0',status:'Cancelled'}];
    window.events = []; window.revision = 0;
    function Fixture({ kind, config, revision }) {
      React.useLayoutEffect(() => { document.getElementById('fixture').dataset.revision = String(revision); }, [revision]);
      const event = value => window.events.push(value);
      const list = config.empty ? [] : config.shrunk ? nodes.slice(0, 1) : nodes;
      const callback = config.readOnly ? undefined : id => event({ id });
      if (kind === 'ComparisonPlate') return <DS.ComparisonPlate title="Synthetic plate" categories={config.empty ? [] : ['First','Second']} values={config.empty ? [] : [0,null]} domain={[0,10]} selected={!!config.selected} onSelect={config.readOnly ? undefined : () => event('select')}/>;
      if (kind === 'PlateGrid') return <DS.PlateGrid categories={['First','Second']} plates={config.empty ? [] : nodes.map((n,index) => ({ id:n.id,title:n.label,values:index?[5,10]:[0,5] }))} selectedId={config.selected} onSelect={callback}/>;
      if (kind === 'ControlDependencyGraph') return <DS.ControlDependencyGraph nodes={list} edges={config.empty || config.shrunk ? [] : edges} selectedId={config.selected} onSelect={config.readOnly ? undefined : node => event({id:node.id,exact:node===nodes.find(n=>n.id===node.id)})} view={config.view} onViewChange={value=>event({view:value})}/>;
      if (kind === 'LineageGraph') return <DS.LineageGraph nodes={list} edges={config.empty || config.shrunk ? [] : edges} selectedId={config.selected} onSelect={callback}/>;
      if (kind === 'OwnershipGraph') return <DS.OwnershipGraph nodes={list} edges={config.empty || config.shrunk ? [] : edges} selectedId={config.selected} onSelectNode={config.readOnly ? undefined : (id,node)=>event({id,nodeId:node.id,label:node.label})}/>;
      if (kind === 'TraceChart') return <DS.TraceChart description="Synthetic curve" categories={config.empty ? [] : ['First','Second','Third']} series={config.empty ? [] : [{id:'observed:exact',label:'Observed',role:'actual',values:[0,null,5]},{id:'comparison:exact',label:'Prior',role:'prior',values:[1,2,3]}]} selected={config.selected} onSelect={config.readOnly ? undefined : event}/>;
      if (kind === 'WatchlistStrip') return <DS.WatchlistStrip items={config.empty ? [] : [{id:nodes[0].id,symbol:'AAA',last:0,change:0},{symbol:'BBB',last:null,changePct:null}]} selectedId={config.selected} onSelect={callback} showSpark={false}/>;
      if (kind === 'DepthLadder') return <DS.DepthLadder bids={config.empty ? [] : [{price:99.125,size:0},{price:98.875,size:1200}]} asks={config.empty ? [] : [{price:99.375,size:100},{price:99.625,size:200}]} priceDecimals={3} onPriceClick={config.readOnly ? undefined : (price,side)=>event({price,side})}/>;
      if (kind === 'OptionChainTable') return <DS.OptionChainTable rows={config.empty ? [] : config.shrunk ? [rows[1]] : rows} columns={['bid','ask']} underlyingPrice={100} selectedStrike={config.selected} defaultSelectedStrike={config.defaultSelected} onSelect={config.readOnly ? undefined : row=>event({strike:row?.strike,exact:rows.includes(row)})}/>;
      if (kind === 'Blotter') return <DS.Blotter orders={config.empty?[]:orders} selectedIndex={config.selected} sortKey={config.sortKey} sortDir={config.sortDir} onSort={config.readOnly?undefined:key=>event({sort:key})} onRowClick={config.readOnly?undefined:(order,index)=>event({id:order.id,index,exact:order===orders[index]})}/>;
      throw new Error('Unknown contract fixture '+kind);
    }
    window.mount=(kind,config={},reset=true)=>{const revision=++window.revision;window.events=[];root.render(<Fixture key={reset?revision:kind} kind={kind} config={config} revision={revision}/>);return revision;};
  })();`, { loader: 'jsx' })).code });
  async function mount(kind, config = {}, reset = true) {
    const revision = await page.evaluate(({kind,config,reset})=>window.mount(kind,config,reset), {kind,config,reset});
    await page.waitForFunction(revision=>document.getElementById('fixture').dataset.revision===String(revision),revision);
  }
  const events = () => page.evaluate(()=>window.events);
  async function test(names, name, kind, run) {
    let status='passed', detail;
    try { await run(); } catch(error) { status='failed'; detail=error.message; }
    for(const exportName of [].concat(names)) (report.exports[exportName] ||= {cases:[]}).cases.push({name,kind,status,detail,states:[...(statesByCase[name]||[]),...(exportName==='OptionChainTable'&&name.startsWith('omitted callback')?['keyboard','focus']:[])]});
    console.log(`${status}: ${[].concat(names).join('/')} — ${name}`);
  }
  await test('ComparisonPlate','native Enter and Space activate once; selected remains controlled','keyboard',async()=>{
    await mount('ComparisonPlate'); const plate=page.locator('button.mds-plate'); await plate.focus(); await plate.press('Enter'); await plate.press('Space'); assert.deepEqual(await events(),['select','select']); assert.equal(await plate.getAttribute('aria-pressed'),'false');
    assert(await plate.evaluate(node=>node===document.activeElement));
    assert.equal(await plate.locator('circle').count(),1,'Null is omitted while the zero observation retains its mark');
    assert.equal(await plate.locator('.mds-plate__end').textContent(),'0','Zero is a real endpoint, not a missing value');
    await mount('ComparisonPlate',{selected:true}); assert.equal(await plate.getAttribute('aria-pressed'),'true');
  });
  await test(['PlateGrid','ComparisonPlate'],'callback preserves exact selected ID and shared domain geometry','state',async()=>{
    await mount('PlateGrid',{selected:'source:exact/001'}); const plates=page.locator('button.mds-plate'); await plates.nth(1).click(); assert.deepEqual(await events(),[{id:'output:exact/002'}]); assert.equal(await plates.first().getAttribute('aria-pressed'),'true'); assert.equal(await plates.nth(1).getAttribute('aria-pressed'),'false'); assert.equal(await plates.first().locator('svg').getAttribute('viewBox'),await plates.nth(1).locator('svg').getAttribute('viewBox'));
    assert.equal(await plates.first().locator('circle').nth(1).getAttribute('cy'),await plates.nth(1).locator('circle').first().getAttribute('cy'),'Shared value5 occupies the same y-coordinate across distinct plate ranges');
    assert.notEqual(await plates.first().locator('circle').first().getAttribute('cy'),await plates.nth(1).locator('circle').first().getAttribute('cy'),'Different values do not become the same independently normalized minimum');
  });
  await test('ControlDependencyGraph','Enter and Space preserve exact node object; blocked meaning and selected rail persist','keyboard',async()=>{
    await mount('ControlDependencyGraph',{selected:'source:exact/001'}); const node=page.locator('.mds-cdg__hit').nth(1); await node.focus(); await node.press('Enter'); await node.press('Space'); assert.deepEqual(await events(),[{id:'output:exact/002',exact:true},{id:'output:exact/002',exact:true}]); assert.equal(await page.locator('.mds-cdg__datum').count(),1); assert.match(await page.locator('.mds-cdg__head').textContent(),/1 blocked.*1 downstream impeded/);
    assert.equal(await page.locator('.mds-cdg__hit').first().getAttribute('aria-pressed'),'true');
    assert.equal(await node.getAttribute('aria-pressed'),'false','Callback cannot mutate controlled selectedId');
    assert(await node.evaluate(node=>node===document.activeElement));
    await mount('ControlDependencyGraph',{selected:'output:exact/002'});
    assert.equal(await node.getAttribute('aria-pressed'),'true','Controlled selectedId updates programmatic selection');
    assert.equal(await page.locator('.mds-cdg__hit').first().getAttribute('aria-pressed'),'false');
  });
  await test('ControlDependencyGraph','uncontrolled view changes; controlled view emits without mutating','state',async()=>{
    await mount('ControlDependencyGraph'); await page.getByRole('button',{name:'Outline',exact:true}).click(); assert.equal(await page.getByRole('button',{name:'Outline',exact:true}).getAttribute('aria-pressed'),'true'); assert.deepEqual(await events(),[{view:'outline'}]);
    await mount('ControlDependencyGraph',{view:'graph'}); await page.getByRole('button',{name:'Outline',exact:true}).click(); assert.equal(await page.getByRole('button',{name:'Graph',exact:true}).getAttribute('aria-pressed'),'true'); assert.deepEqual(await events(),[{view:'outline'}]);
  });
  await test('LineageGraph','native keyboard returns exact ID and waits for controlled selection','keyboard',async()=>{
    await mount('LineageGraph',{selected:'source:exact/001'}); const node=page.locator('.mds-lin__node').nth(1); await node.focus(); await node.press('Space'); assert.deepEqual(await events(),[{id:'output:exact/002'}]); assert.equal(await node.getAttribute('aria-pressed'),'false');
    assert(await node.evaluate(node=>node===document.activeElement));
    await mount('LineageGraph',{selected:'output:exact/002'}); assert.equal(await node.getAttribute('aria-pressed'),'true'); assert.match(await page.locator('.mds-lin__out').textContent(),/Source Alpha.*Output Beta/);
  });
  await test('OwnershipGraph','native keyboard returns normalized exact ID and node fields; selection controlled','keyboard',async()=>{
    await mount('OwnershipGraph',{selected:'source:exact/001'}); const node=page.locator('.mds-own__node').nth(1); await node.focus(); await node.press('Enter'); assert.deepEqual(await events(),[{id:'output:exact/002',nodeId:'output:exact/002',label:'Output Beta'}]); assert.equal(await node.evaluate(n=>n.classList.contains('mds-own__node--sel')),false); assert.equal(await page.locator('.mds-own__node--sel').count(),1);
    assert.equal(await page.locator('.mds-own__node').first().getAttribute('aria-pressed'),'true');
    assert.equal(await node.getAttribute('aria-pressed'),'false','Callback cannot mutate controlled selectedId');
    assert(await node.evaluate(node=>node===document.activeElement));
    await mount('OwnershipGraph',{selected:'output:exact/002'});
    assert.equal(await node.getAttribute('aria-pressed'),'true','Controlled selectedId updates programmatic selection');
    assert.equal(await page.locator('.mds-own__node').first().getAttribute('aria-pressed'),'false');
  });
  await test('TraceChart','arrow, Home and End commit exact series and index without mutating controlled selection','keyboard',async()=>{
    await mount('TraceChart',{selected:{seriesId:'comparison:exact',index:1}}); const chart=page.locator('.mds-trc__svg'); await chart.focus(); for(const key of ['ArrowRight','ArrowLeft','Home','End']) await chart.press(key); assert.deepEqual(await events(),[{seriesId:'comparison:exact',index:2},{seriesId:'comparison:exact',index:0},{seriesId:'comparison:exact',index:0},{seriesId:'comparison:exact',index:2}]); assert.equal(await page.locator('.mds-trc__sr').getByText('Missing',{exact:true}).count(),1); assert.match(await page.locator('.mds-trc__sr').textContent(),/First0/);
    assert(await chart.evaluate(node=>node===document.activeElement));
  });
  await test('TraceChart','pointer category selection defaults to actual series and preserves zero/missing table values','state',async()=>{
    await mount('TraceChart'); await page.locator('.mds-trc__hit').nth(0).click(); assert.deepEqual(await events(),[{seriesId:'observed:exact',index:0}]); assert.equal(await page.locator('.mds-trc__sr tbody tr').first().locator('td').first().textContent(),'0'); assert.equal(await page.locator('.mds-trc__sr tbody tr').nth(1).locator('td').first().textContent(),'Missing');
  });
  await test('WatchlistStrip','native keyboard returns item ID or exact symbol fallback; selection controlled','keyboard',async()=>{
    await mount('WatchlistStrip',{selected:'source:exact/001'}); const options=page.getByRole('option'); await options.nth(1).focus(); await options.nth(1).press('Space'); await options.first().press('Enter'); assert.deepEqual(await events(),[{id:'BBB'},{id:'source:exact/001'}]); assert.equal(await options.first().getAttribute('aria-selected'),'true'); assert.equal(await options.nth(1).getAttribute('aria-selected'),'false');
    assert(await options.first().evaluate(node=>node===document.activeElement));
  });
  await test('DepthLadder','roving focus follows visual asks/bids; exact price and side callback','keyboard',async()=>{
    await mount('DepthLadder'); const prices=page.locator('button.mds-ladder__price'); assert.equal(await prices.count(),4); assert.equal(await page.locator('button.mds-ladder__price[tabindex="0"]').getAttribute('aria-label'),'bid 99.125'); await prices.nth(2).focus(); await prices.nth(2).press('Home'); assert.equal(await prices.first().evaluate(n=>n===document.activeElement),true); await prices.first().press('Enter'); await prices.first().press('End'); await prices.last().press('Space'); assert.deepEqual(await events(),[{price:99.625,side:'ask'},{price:98.875,side:'bid'}]); assert.equal(await prices.last().evaluate(n=>n===document.activeElement),true);
  });
  await test('OptionChainTable','sorted keyboard and pointer return original row object; controlled strike does not mutate','keyboard',async()=>{
    await mount('OptionChainTable',{selected:90}); const grid=page.getByRole('grid'); await grid.focus(); await grid.press('End'); await grid.press('Enter'); await grid.press('Home'); await grid.press('Space'); assert.deepEqual(await events(),[{strike:110,exact:true},{strike:90,exact:true}]); assert(await grid.evaluate(node=>node===document.activeElement)); assert.equal(await page.locator('.mds-ochain__row--selected .mds-ochain__cell--strike').textContent(),'90.00'); await page.locator('.mds-ochain__cell--strike').filter({hasText:'100.00'}).click(); assert.deepEqual((await events()).at(-1),{strike:100,exact:true}); assert.equal(await page.locator('.mds-ochain__row--selected .mds-ochain__cell--strike').textContent(),'90.00');
  });
  await test('OptionChainTable','uncontrolled selection and zero quote distinguish missing values','state',async()=>{
    await mount('OptionChainTable',{defaultSelected:90}); await page.getByRole('grid').press('End'); await page.getByRole('grid').press('Enter'); assert.equal(await page.locator('.mds-ochain__row--selected .mds-ochain__cell--strike').textContent(),'110.00'); assert.equal(await page.locator('.mds-ochain__row').last().locator('[role="gridcell"]').first().textContent(),'0.00');
  });
  await test('OptionChainTable','data shrink clamps focused strike before activation','state',async()=>{
    await mount('OptionChainTable',{},false); await page.getByRole('grid').press('End'); await mount('OptionChainTable',{shrunk:true},false); await page.getByRole('grid').press('Enter'); assert.deepEqual(await events(),[{strike:90,exact:true}]);
  });
  await test('Blotter','native row activation returns original order and source index; selected index stays controlled','keyboard',async()=>{
    await mount('Blotter',{selected:0}); const rows=page.locator('tr[data-row-key]'); await rows.nth(1).focus(); await rows.nth(1).press('Enter'); await rows.nth(1).press('Space'); assert.deepEqual(await events(),[{id:'ORDER:exact/001',index:1,exact:true},{id:'ORDER:exact/001',index:1,exact:true}]); assert.equal(await rows.first().getAttribute('data-selected'),'true'); assert.equal(await rows.nth(1).getAttribute('data-selected'),null); assert.equal(await rows.first().locator('td').first().textContent(),'ORDER:exact/002');
    assert(await rows.nth(1).evaluate(node=>node===document.activeElement));
  });
  await test('Blotter','sort emits exact key without changing controlled row order; zero limit differs from missing','state',async()=>{
    await mount('Blotter',{sortKey:'symbol',sortDir:'asc'}); const header=page.getByRole('columnheader',{name:/^Symbol/}); await header.focus(); await header.press('Space'); assert.deepEqual(await events(),[{sort:'symbol'}]); assert.equal(await header.getAttribute('aria-sort'),'ascending'); const rows=page.locator('tr[data-row-key]'); assert.equal(await rows.first().locator('td').first().textContent(),'ORDER:exact/002'); assert.equal(await rows.first().locator('td').nth(6).textContent(),'0'); assert.equal(await rows.nth(1).locator('td').nth(6).textContent(),'—'); assert.equal(await rows.nth(1).locator('.mds-sev').textContent(),'Cancelled');
  });
  await test('Blotter','controlled refresh preserves focused row DOM and updates active index','state',async()=>{
    await mount('Blotter',{selected:0},false); const row=page.locator('tr[data-row-key]').nth(1); await row.focus(); await row.evaluate(node=>{window.focusedOrderRow=node;}); await mount('Blotter',{selected:1},false); assert(await row.evaluate(node=>node===window.focusedOrderRow&&node===document.activeElement)); assert.equal(await row.getAttribute('data-selected'),'true'); assert.deepEqual(await events(),[]);
  });
  for(const name of ['ComparisonPlate','PlateGrid','ControlDependencyGraph','LineageGraph','OwnershipGraph','TraceChart','WatchlistStrip','DepthLadder','OptionChainTable','Blotter']) {
    await test(name,'empty input has no selectable data targets and recovers with data','state',async()=>{
      await mount(name,{empty:true},false); assert.equal(await page.locator('.mds-cdg__hit,.mds-lin__node,.mds-own__node,.mds-trc__hit,[role="option"],button.mds-ladder__price,.mds-ochain__row,tr[data-row-key],button.mds-plate:not(:only-child)').count(),0); assert(!/NaN|Infinity/.test(await page.locator('#fixture').textContent())); await mount(name,{},false);
      const target={ComparisonPlate:'.mds-plate circle',PlateGrid:'.mds-plate circle',ControlDependencyGraph:'.mds-cdg__hit',LineageGraph:'.mds-lin__node',OwnershipGraph:'.mds-own__node',TraceChart:'.mds-trc__hit',WatchlistStrip:'[role="option"]',DepthLadder:'button.mds-ladder__price',OptionChainTable:'.mds-ochain__row',Blotter:'tr[data-row-key]'}[name];
      assert(await page.locator(target).count()>0,'Real data targets recover in the same component instance');
    });
    await test(name,'omitted callback exposes read-only data without active selection targets','state',async()=>{
      await mount(name,{readOnly:true}); assert.equal(await page.locator('.mds-cdg__hit[tabindex="0"],button.mds-lin__node:not(:disabled),button.mds-own__node,.mds-trc__svg[tabindex="0"],[role="option"],button.mds-ladder__price,.mds-ochain__row[aria-selected],tr[data-row-key][tabindex="0"],button.mds-plate').count(),0);
      if(name==='OptionChainTable') {
        const scroll=page.getByRole('grid',{name:'Option chain',exact:true});
        assert.equal(await scroll.getAttribute('tabindex'),'0','Read-only horizontal data remains reachable by keyboard');
        await scroll.focus(); assert(await scroll.evaluate(node=>node===document.activeElement));
        for(const key of ['ArrowDown','End','Enter','Space']) await scroll.press(key);
        await page.locator('.mds-ochain__row').first().click();
        assert.equal(await page.locator('.mds-ochain__row[aria-selected],.mds-ochain__row--selected,.mds-ochain__row--focus').count(),0,'Read-only keyboard/pointer interaction cannot activate or select a strike');
      }
      assert.deepEqual(await events(),[]);
    });
  }
} finally {
  await browser.close();
  const output=resolve(process.env.MERIDIAN_CHART_TRADING_REPORT || '/tmp/pr3501-full-audit/chart-trading-contract-results.json'); await mkdir(dirname(output),{recursive:true}); await writeFile(output,JSON.stringify(report,null,2)+'\n');
}
const cases=Object.values(report.exports).flatMap(entry=>entry.cases);
console.log(JSON.stringify({exports:Object.keys(report.exports).length,passed:cases.filter(c=>c.status==='passed').length,failed:cases.filter(c=>c.status==='failed').length,runtimeErrors:report.errors.length}));
if(cases.some(c=>c.status==='failed')||report.errors.length) process.exitCode=1;
