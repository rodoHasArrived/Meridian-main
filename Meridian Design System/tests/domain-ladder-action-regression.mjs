#!/usr/bin/env node
// Public JSX source, synthetic inputs, and real Chromium keyboard/pointer activation.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build, transform } from 'esbuild';
import { chromium } from 'playwright';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const sources = {
  MaturityLadder: 'components/charts/MaturityLadder.jsx',
  CashFlowLadder: 'components/charts/CashFlowLadder.jsx',
  DurationContribution: 'components/charts/DurationContribution.jsx',
  CorrelationHeatmap: 'components/charts/CorrelationHeatmap.jsx',
  Treemap: 'components/charts/Treemap.jsx',
  PlateGrid: 'components/charts/ComparisonPlate.jsx',
  ComparisonPlate: 'components/charts/ComparisonPlate.jsx',
  LineageGraph: 'components/charts/LineageGraph.jsx',
  OwnershipGraph: 'components/charts/OwnershipGraph.jsx',
  TraceChart: 'components/charts/TraceChart.jsx',
  WatchlistStrip: 'components/charts/WatchlistStrip.jsx',
};
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const report = { schemaVersion: 1, exports: {}, cases: [], summary: {}, errors: [] };
for (const [name, source] of Object.entries(sources)) {
  report.exports[name] = { source, sha256: hash(await readFile(resolve(root, source))), test: 'tests/domain-ladder-action-regression.mjs', cases: [] };
}
report.exports.MaturityLadder.scope = 'Local and controlled amount/percent modes; declared, zero and undeclared normalized bucket selection. No asynchronous lifecycle API.';
report.exports.CashFlowLadder.scope = 'Native row activation returns normalized bucket/index; selectedId remains controlled. No local selection API.';
report.exports.DurationContribution.scope = 'Local and controlled ranks; roving keyboard focus and exact normalized selection/index; double-click invokes the documented trace callback.';
report.exports.CorrelationHeatmap.scope = 'Pointer hover is the public interaction, with exact numeric row/col/value. No keyboard-selection API.';
report.exports.Treemap.scope = 'Native keyboard tile activation returns retained item fields plus computed area. Layout uses ResizeObserver; nonpositive weights are omitted.';
report.exports.PlateGrid.scope = 'Native keyboard plate activation returns exact ID and respects controlled selection; shared plot geometry is retained.';
report.exports.ComparisonPlate.scope = 'Standalone native button selection callback has no payload; selected is controlled.';
report.exports.LineageGraph.scope = 'Cycle rendering is bounded; natural canvas geometry remains stable while host width changes. Native selection and acyclic recovery preserve exact IDs.';
report.exports.OwnershipGraph.scope = 'Cycle-safe DTO normalization, measured edge reflow on resize, controlled selection, acyclic recovery, exact SVG titles, and a semantic ownership relationship list with unchanged keyboard order. Native assistive technology is unrun.';
report.exports.TraceChart.scope = 'Native category title is the hover surface; no onHover API or numeric preview readout. Model bands and resized hit targets preserve controlled series/index and exact fallback data.';
report.exports.WatchlistStrip.scope = 'Missing change remains an em dash with neutral tone and differs from observed zero through source replacement and keyboard selection.';

const bundle = await build({
  absWorkingDir: root, bundle: true, write: false,
  resolveExtensions: ['.js', '.jsx', '.ts', '.tsx', '.json'],
  format: 'iife', platform: 'browser', define: { 'process.env.NODE_ENV': '"production"' },
  stdin: { resolveDir: root, loader: 'jsx', contents: `
    import React from 'react'; import {createRoot} from 'react-dom/client';
    import {MaturityLadder} from './components/charts/MaturityLadder.jsx';
    import {CashFlowLadder} from './components/charts/CashFlowLadder.jsx';
    import {DurationContribution} from './components/charts/DurationContribution.jsx';
    import {CorrelationHeatmap} from './components/charts/CorrelationHeatmap.jsx';
    import {Treemap} from './components/charts/Treemap.jsx';
    import {PlateGrid,ComparisonPlate} from './components/charts/ComparisonPlate.jsx';
    import {LineageGraph} from './components/charts/LineageGraph.jsx';
    import {OwnershipGraph} from './components/charts/OwnershipGraph.jsx';
    import {TraceChart} from './components/charts/TraceChart.jsx';
    import {WatchlistStrip} from './components/charts/WatchlistStrip.jsx';
    window.Fixture={React,createRoot,MaturityLadder,CashFlowLadder,DurationContribution,CorrelationHeatmap,Treemap,PlateGrid,ComparisonPlate,LineageGraph,OwnershipGraph,TraceChart,WatchlistStrip};
  ` },
});
const browser = await chromium.launch({ executablePath: process.env.MERIDIAN_CHROMIUM_PATH || '/usr/bin/chromium', args: ['--no-sandbox'] });
const context = await browser.newContext({ viewport: { width: 1366, height: 900 }, reducedMotion: 'reduce' });
const page = await context.newPage();
page.setDefaultTimeout(3500);
page.on('pageerror', error => report.errors.push(error.message));
try {
  await page.setContent(`<!doctype html><html lang="en"><head><title>Synthetic ladder action contracts</title><style>
    :root{--font-body:Arial,sans-serif;--font-data:monospace;--text-primary:#20252b;--text-secondary:#535c65;--text-muted:#606870;--border:#d2cdc3;--chart-plot:#f8f7f3;--card-surface-raised:#f8f7f3;--red:#b54b42;--green:#2e7655;--accent:#2d6fb3;--focus-ring:2px solid #2d6fb3}
    body{font:14px Arial,sans-serif}#fixture{width:1100px}button:focus-visible{outline:2px solid #2d6fb3}
  </style></head><body><main><h1>Synthetic ladder action contracts</h1><div id="fixture"></div></main></body></html>`);
  await page.addScriptTag({ content: bundle.outputFiles[0].text });
  await page.addScriptTag({ content: (await transform(String.raw`(() => {
    const {React,createRoot,...DS}=window.Fixture;
    const root=createRoot(document.getElementById('fixture'));
    const maturity=[
      {id:'bucket:exact/002',label:'Second bucket',primary:30,comparison:10,detail:'Retained source detail',sourceKey:'source:002'},
      {id:'bucket:exact/001',label:'First bucket',primary:10,comparison:30,sourceKey:'source:001'},
      {id:'bucket:exact/003',label:'Perpetual source',primary:10,comparison:0,sourceKey:'source:003'},
    ];
    const buckets=['First bucket','Empty bucket','Second bucket'];
    const cash=[
      {id:'flow:exact/002',label:'Second source period',assets:30,liabilities:10,coverage:'2 of 2',detail:'Retained flow detail'},
      {id:'flow:exact/001',label:'First source period',assets:5,liabilities:15,net:-7,scenarioAssets:9,scenarioLiabilities:14,unscheduled:3},
    ];
    const duration=[
      {id:'duration:exact/002',label:'Beta source',weight:20,duration:10,comparisonContribution:1.5,sourceKey:'source:002'},
      {id:'duration:exact/003',label:'Gamma source',weight:10,duration:5,contribution:-3,comparisonContribution:-2.75,sourceKey:'source:003'},
      {id:'duration:exact/001',label:'Alpha source',weight:70,duration:1,comparisonContribution:0.1,sourceKey:'source:001'},
    ];
    const tree=[{label:'Small source',value:10,delta:-0.25,group:'Group A'},{label:'Largest source',value:30,delta:0.5,group:'Group B'},{label:'Zero omitted',value:0}];
    const plates=[{id:'plate:exact/002',title:'Second source',tag:'S2',values:[2,null,6],footerLabel:'Exact value',footerValue:'6'},{id:'plate:exact/001',title:'First source',tag:'S1',values:[-2,0,3],footerLabel:'Exact value',footerValue:'3'}];
    const graphNodes=[{id:'graph:exact/A',label:'Graph Alpha',kind:'source',meta:'Source A'},{id:'graph:exact/B',label:'Graph Beta',kind:'model',meta:'Model B'},{id:'graph:exact/C',label:'Graph Gamma',kind:'output',meta:'Output C'}];
    const graphEdges=[{from:graphNodes[0].id,to:graphNodes[1].id,label:'feeds'},{from:graphNodes[1].id,to:graphNodes[2].id,label:'derives'},{from:graphNodes[2].id,to:graphNodes[0].id,label:'returns'}];
    const ownerNodes=[{nodeId:'owner:exact/A',displayName:'Owner Alpha',nodeType:'Trust',jurisdiction:'DE',currency:'USD'},{nodeId:'owner:exact/B',displayName:'Owner Beta',nodeType:'Fund',jurisdiction:'SD',currency:'USD'},{nodeId:'owner:exact/C',displayName:'Owner Gamma',nodeType:'Account'}];
    const ownerEdges=[{edgeId:'ownership:A-B',sourceNodeId:'owner:exact/A',targetNodeId:'owner:exact/B',relationshipType:'Member',ownershipPercent:75},{edgeId:'ownership:B-C',sourceNodeId:'owner:exact/B',targetNodeId:'owner:exact/C',relationshipType:'Owner',ownershipPercent:100},{edgeId:'ownership:C-A',sourceNodeId:'owner:exact/C',targetNodeId:'owner:exact/A',relationshipType:'Returns',ownershipPercent:25}];
    window.events=[];window.revision=0;let activeKind,activeKey=0;
    function Fixture({kind,config,revision}) {
      React.useLayoutEffect(()=>{document.getElementById('fixture').dataset.revision=String(revision)},[revision]);
      const event=value=>window.events.push(value);
      const select=(item,index)=>event({type:'select',item,index});
      if(kind==='MaturityLadder') return <DS.MaturityLadder items={config.empty?[]:maturity} buckets={buckets} defaultMode={config.defaultMode} mode={config.mode} onModeChange={config.readOnly?undefined:mode=>event({type:'mode',mode})} selectedId={config.selectedId} onSelect={config.readOnly?undefined:select} valueFormat={v=>'USD '+v} description="Synthetic maturity profile"/>;
      if(kind==='CashFlowLadder') return <DS.CashFlowLadder items={config.empty?[]:cash} selectedId={config.selectedId} onSelect={config.readOnly?undefined:select} cumulative={config.cumulative} showScenario={config.showScenario} valueFormat={v=>'USD '+v} description="Synthetic scheduled cash flows"/>;
      if(kind==='DurationContribution') return <DS.DurationContribution items={config.empty?[]:config.incomplete?[...duration,{id:'duration:exact/incomplete',label:'Incomplete source',contribution:1.25,comparisonContribution:0.5}]:duration} total={config.total} rank={config.rank} defaultRank={config.defaultRank} selectedId={config.selectedId} showComparison={config.showComparison} onRankChange={rank=>event({type:'rank',rank})} onSelect={config.readOnly?undefined:select} onOpenTrace={config.readOnly?undefined:item=>event({type:'trace',item})} description="Synthetic duration attribution"/>;
      if(kind==='CorrelationHeatmap') return <DS.CorrelationHeatmap labels={config.empty?[]:['First series','Second series','Third series']} matrix={config.empty?[]:[[1,-0.625,0],[-0.625,1,0.125],[0,0.125,1]]} showValues={config.showValues} valueFmt={v=>v.toFixed(3)} onCellHover={config.readOnly?undefined:cell=>event({type:'hover',...cell})}/>;
      if(kind==='Treemap') return <div data-testid="treemap-host" style={{width:config.width||600,height:240}}><DS.Treemap items={config.empty?[]:tree} onSelect={config.readOnly?undefined:item=>event({type:'select',item})} colorBy={config.colorBy||'auto'} valueFmt={v=>'USD '+v}/></div>;
      if(kind==='PlateGrid') return <DS.PlateGrid plates={config.empty?[]:config.reversed?[...plates].reverse():plates} categories={['Start','Gap','End']} selectedId={config.selectedId} onSelect={config.readOnly?undefined:id=>event({type:'select',id})}/>;
      if(kind==='ComparisonPlate') return <DS.ComparisonPlate title="Standalone source" categories={['Start','Gap','End']} values={[0,null,4]} domain={[0,6]} selected={config.selected} onSelect={config.readOnly?undefined:(...args)=>event({type:'select',argumentCount:args.length})}/>;
      if(kind==='LineageGraph') return <div data-testid="graph-host" style={{width:config.width||820}}><DS.LineageGraph nodes={config.empty?[]:graphNodes} edges={config.empty?[]:config.cycle?graphEdges:graphEdges.slice(0,2)} selectedId={config.selectedId} onSelect={id=>event({type:'select',id})}/></div>;
      if(kind==='OwnershipGraph') {
        const nodes=config.relationshipUpdated?ownerNodes.map(n=>({...n,displayName:n.displayName+' revised'})):ownerNodes;
        const edges=config.relationshipEvidence?ownerEdges.map((e,i)=>({...e,relationshipType:config.relationshipUpdated?['Participant','Custodian','Guarantor'][i]:e.relationshipType,ownershipPercent:i===0?(config.relationshipUpdated?62.5:75):i===1?0:undefined})):config.cycle?ownerEdges:ownerEdges.slice(0,2);
        return <div data-testid="graph-host" style={{width:config.width||820}}><DS.OwnershipGraph nodes={config.empty?[]:nodes} edges={config.empty?[]:edges} selectedId={config.selectedId} onSelectNode={(id,node)=>event({type:'select',id,node})}/></div>;
      }
      if(kind==='TraceChart') return <div data-testid="trace-host" style={{width:config.width||820}}><DS.TraceChart categories={config.empty?[]:['Start','Missing','End']} series={config.empty?[]:[{id:'trace:exact/actual',label:'Actual source',role:'actual',values:[0,null,5]},{id:'trace:exact/prior',label:'Prior source',role:'prior',values:[1,2,3]},{id:'trace:exact/hidden',label:'Hidden source',role:'estimated',values:[99,99,99],hidden:true}]} band={config.band?{upper:[3,5,7],lower:[-1,0,2],innerUpper:[1,4,6],innerLower:[0,1,3]}:undefined} selected={config.selected} onSelect={selection=>event({type:'select',...selection})} yFormat={v=>v.toFixed(2)} description="Synthetic observed values and model uncertainty"/></div>;
      if(kind==='WatchlistStrip') return <DS.WatchlistStrip items={config.empty?[]:[{id:'watch:exact/missing',symbol:'MISSING',last:10,change:config.recovered?0:null,changePct:null},{id:'watch:exact/zero',symbol:'ZERO',last:0,change:0},{id:'watch:exact/percent',symbol:'PCT',last:12,changePct:0},{symbol:'UNKNOWN',last:null}]} priceFmt={v=>v==null?'Missing':v.toFixed(2)} selectedId={config.selectedId} onSelect={id=>event({type:'select',id})} showSpark={false} showRange={false}/>;
      throw Error('Unknown fixture '+kind);
    }
    window.mount=(kind,config={},reset=true)=>{if(reset||activeKind!==kind)activeKey++;activeKind=kind;const revision=++window.revision;window.events=[];root.render(<Fixture key={activeKey} kind={kind} config={config} revision={revision}/>);return revision};
    window.clearEvents=()=>{window.events=[]};
  })();`, { loader: 'jsx' })).code });
  async function mount(kind, config = {}, reset = true) {
    const revision = await page.evaluate(({kind,config,reset})=>window.mount(kind,config,reset), {kind,config,reset});
    await page.waitForFunction(revision=>document.getElementById('fixture').dataset.revision===String(revision), revision);
  }
  const events = () => page.evaluate(()=>window.events);
  const focused = locator => locator.evaluate(node=>node===document.activeElement);
  const labels = selector => page.locator(selector).allTextContents();
  const maturityValues = () => page.locator('.mds-ml__row .mds-ml__value').evaluateAll(nodes=>nodes.map(node=>{
    const clone=node.cloneNode(true);clone.querySelectorAll('[aria-hidden="true"]').forEach(mark=>mark.remove());return clone.textContent;
  }));
  async function test(exportName, name, states, run) {
    const result = { name, states, status: 'passed' };
    try { await run(); } catch(error) { result.status='failed';result.detail=error.message; }
    report.exports[exportName].cases.push(result);report.cases.push({export:exportName,...result});
    console.log(`${result.status}: ${exportName} — ${name}`);
  }

  await test('MaturityLadder','local native modes change numeric units, preserve the amount table, and return exact requested modes', ['uncontrolled','amount','percent','keyboard'], async()=>{
    await mount('MaturityLadder');
    const amount=page.getByRole('button',{name:'Amount',exact:true}),percent=page.getByRole('button',{name:'Percent',exact:true});
    assert.equal(await amount.getAttribute('aria-pressed'),'true');
    assert.deepEqual(await maturityValues(),['USD 10','USD 30','USD 0','USD 0','USD 30','USD 10','USD 10','USD 0']);
    const exactTable=await labels('.mds-ml__table tbody tr');
    await percent.focus();await percent.press('Enter');
    assert.deepEqual(await events(),[{type:'mode',mode:'percent'}]);assert.equal(await percent.getAttribute('aria-pressed'),'true');
    assert.deepEqual(await maturityValues(),['20.0%','75.0%','0.0%','0.0%','60.0%','25.0%','20.0%','0.0%']);
    assert.match(await page.locator('.mds-ml__unit').textContent(),/percent of total/);assert.match(await page.locator('.mds-ml__note').textContent(),/Difference stated in pp/);
    assert.deepEqual(await labels('.mds-ml__table tbody tr'),exactTable);
    await amount.focus();await amount.press('Space');assert.equal(await amount.getAttribute('aria-pressed'),'true');
    assert.deepEqual(await events(),[{type:'mode',mode:'percent'},{type:'mode',mode:'amount'}]);assert.equal(await focused(amount),true);
    await mount('MaturityLadder',{defaultMode:'percent'});assert.equal(await percent.getAttribute('aria-pressed'),'true');
  });
  await test('MaturityLadder','controlled mode emits proposals and waits for parent acceptance', ['controlled','amount','percent','keyboard'], async()=>{
    await mount('MaturityLadder',{mode:'amount'},false);
    const percent=page.getByRole('button',{name:'Percent',exact:true});await percent.focus();await percent.press('Space');
    assert.deepEqual(await events(),[{type:'mode',mode:'percent'}]);assert.equal(await percent.getAttribute('aria-pressed'),'false');assert.match(await page.locator('.mds-ml__unit').textContent(),/USD/);
    await mount('MaturityLadder',{mode:'percent'},false);assert.equal(await percent.getAttribute('aria-pressed'),'true');
    await page.getByRole('button',{name:'Amount',exact:true}).press('Enter');assert.deepEqual(await events(),[{type:'mode',mode:'amount'}]);assert.equal(await percent.getAttribute('aria-pressed'),'true');
    await mount('MaturityLadder',{mode:'amount'},false);assert.equal(await percent.getAttribute('aria-pressed'),'false');
  });
  await test('MaturityLadder','native row selection returns exact normalized bucket/index and retains controlled selection', ['keyboard','controlled','selection','declared-order','unknown'], async()=>{
    await mount('MaturityLadder',{selectedId:'bucket:exact/001'},false);
    const rows=page.locator('button.mds-ml__row');assert.equal(await rows.count(),4);
    assert.equal(await rows.first().getAttribute('data-selected'),'true');
    await rows.nth(2).focus();await rows.nth(2).press('Enter');await rows.nth(3).focus();await rows.nth(3).press('Space');
    assert.deepEqual(await events(),[
      {type:'select',item:{id:'bucket:exact/002',label:'Second bucket',primary:30,comparison:10,detail:'Retained source detail',sourceKey:'source:002',p:30,c:10,unknown:false},index:2},
      {type:'select',item:{id:'bucket:exact/003',label:'Perpetual source',primary:10,comparison:0,sourceKey:'source:003',p:10,c:0,unknown:true,undeclared:true},index:3},
    ]);
    assert.equal(await rows.first().getAttribute('data-selected'),'true');assert.equal(await rows.nth(3).getAttribute('data-selected'),null);
    await mount('MaturityLadder',{selectedId:'bucket:exact/003'},false);assert.equal(await rows.nth(3).getAttribute('data-selected'),'true');assert.equal(await rows.first().getAttribute('data-selected'),null);
    await rows.nth(1).press('Enter');assert.deepEqual(await events(),[{type:'select',item:{label:'Empty bucket',primary:0,comparison:0,condition:'na',empty:true,p:0,c:0,unknown:false},index:1}]);
  });
  await test('MaturityLadder','empty inputs keep declared zero buckets and recover without stale unknown rows; readonly removes row actions', ['empty','recovery','zero','readonly'], async()=>{
    await mount('MaturityLadder');await mount('MaturityLadder',{empty:true},false);
    assert.equal(await page.locator('.mds-ml__row').count(),3);assert.deepEqual(await maturityValues(),Array(6).fill('USD 0'));
    assert.equal(await page.locator('.mds-ml__row[data-unknown="true"]').count(),0);assert.doesNotMatch(await page.locator('#fixture').innerHTML(),/NaN|Infinity/);
    await mount('MaturityLadder',{readOnly:true},false);assert.equal(await page.locator('button.mds-ml__row').count(),0);assert.equal(await page.locator('.mds-ml__table tbody tr').count(),5);
  });

  await test('CashFlowLadder','native keyboard selection returns exact gross/net/cumulative bucket and caller index', ['keyboard','controlled','selection','exact-payload'], async()=>{
    await mount('CashFlowLadder',{selectedId:'flow:exact/002'},false);
    const rows=page.locator('button.mds-cfl__row');await rows.nth(1).focus();await rows.nth(1).press('Enter');await rows.nth(1).press('Space');
    const expected={type:'select',item:{id:'flow:exact/001',label:'First source period',assets:5,liabilities:15,net:-7,scenarioAssets:9,scenarioLiabilities:14,unscheduled:3,a:5,l:15,n:-7,cum:13},index:1};
    assert.deepEqual(await events(),[expected,expected]);assert.equal(await focused(rows.nth(1)),true);
    assert.equal(await rows.first().getAttribute('data-selected'),'true');assert.equal(await rows.nth(1).getAttribute('data-selected'),null);
    await mount('CashFlowLadder',{selectedId:'flow:exact/001'},false);assert.equal(await rows.first().getAttribute('data-selected'),null);assert.equal(await rows.nth(1).getAttribute('data-selected'),'true');
    await rows.first().press('Enter');assert.deepEqual(await events(),[{type:'select',item:{id:'flow:exact/002',label:'Second source period',assets:30,liabilities:10,coverage:'2 of 2',detail:'Retained flow detail',a:30,l:10,n:20,cum:20},index:0}]);
  });
  await test('CashFlowLadder','coverage/scenario toggles retain the same exact row contract', ['scenario','coverage','keyboard','selection'], async()=>{
    await mount('CashFlowLadder',{cumulative:false,showScenario:true});
    assert.equal(await page.locator('.mds-cfl__scen').count(),2);assert.match(await page.locator('.mds-cfl__row').first().textContent(),/2 of 2/);assert.equal(await page.locator('thead th').allTextContents().then(x=>x.includes('Cumulative')),false);
    await page.locator('button.mds-cfl__row').nth(1).press('Space');assert.equal((await events())[0].item.cum,13);assert.equal((await events())[0].index,1);
  });
  await test('CashFlowLadder','empty/recovery removes stale action rows and readonly preserves exact table', ['empty','recovery','readonly'], async()=>{
    await mount('CashFlowLadder',{},false);await mount('CashFlowLadder',{empty:true},false);
    assert.equal(await page.locator('.mds-cfl__row').count(),0);assert.equal(await page.locator('tbody tr').count(),1);assert.deepEqual(await labels('tbody td'),['USD 0','USD 0','+USD 0','+USD 0','—']);
    await mount('CashFlowLadder',{readOnly:true},false);assert.equal(await page.locator('button').count(),0);assert.equal(await page.locator('.mds-cfl__row').count(),2);assert.equal(await page.locator('tbody tr').count(),3);
  });

  const rankLabels={contribution:['Gamma source','Beta source','Alpha source'],difference:['Alpha source','Beta source','Gamma source'],weight:['Alpha source','Beta source','Gamma source'],label:['Alpha source','Beta source','Gamma source']};
  await test('DurationContribution','local native rank controls reorder plot and exact table and emit requested IDs', ['uncontrolled','ranking','keyboard'], async()=>{
    await mount('DurationContribution');assert.deepEqual(await labels('.mds-dc__label'),rankLabels.contribution);
    for(const [rank,label,key] of [['weight','Weight','Enter'],['difference','Difference','Space'],['label','Name','Enter'],['contribution','Contribution','Space']]) {
      const button=page.getByRole('button',{name:label,exact:true});await button.focus();await button.press(key);
      assert.equal(await button.getAttribute('aria-pressed'),'true');assert.deepEqual(await labels('.mds-dc__label'),rankLabels[rank]);assert.deepEqual(await labels('tbody tr:not([data-total]) th'),rankLabels[rank]);assert.equal((await events()).at(-1).rank,rank);
    }
    assert.deepEqual(await events(),['weight','difference','label','contribution'].map(rank=>({type:'rank',rank})));
    await mount('DurationContribution',{defaultRank:'label',showComparison:false});assert.equal(await page.getByRole('button',{name:'Difference',exact:true}).count(),0);assert.deepEqual(await labels('.mds-dc__label'),rankLabels.label);
  });
  await test('DurationContribution','controlled rank waits for parent acceptance and preserves authoritative order', ['controlled','ranking','keyboard'], async()=>{
    await mount('DurationContribution',{rank:'contribution'},false);
    const weight=page.getByRole('button',{name:'Weight',exact:true});await weight.press('Space');assert.deepEqual(await events(),[{type:'rank',rank:'weight'}]);assert.equal(await weight.getAttribute('aria-pressed'),'false');assert.deepEqual(await labels('.mds-dc__label'),rankLabels.contribution);
    await mount('DurationContribution',{rank:'weight'},false);assert.equal(await weight.getAttribute('aria-pressed'),'true');assert.deepEqual(await labels('.mds-dc__label'),rankLabels.weight);
    await page.getByRole('button',{name:'Contribution',exact:true}).press('Enter');assert.deepEqual(await events(),[{type:'rank',rank:'contribution'}]);assert.deepEqual(await labels('.mds-dc__label'),rankLabels.weight);
    await mount('DurationContribution',{rank:'contribution'},false);assert.deepEqual(await labels('.mds-dc__label'),rankLabels.contribution);
  });
  await test('DurationContribution','Arrow/Home/End move focus without selecting; Enter/Space return exact ranked normalized row once', ['keyboard','selection','controlled','roving-focus','exact-payload'], async()=>{
    await mount('DurationContribution',{selectedId:'duration:exact/003'});const rows=page.getByRole('option');
    assert.equal(await rows.first().getAttribute('tabindex'),'0');assert.equal(await rows.nth(1).getAttribute('tabindex'),'-1');await rows.first().focus();
    await page.keyboard.press('ArrowDown');assert.equal(await focused(rows.nth(1)),true);await page.keyboard.press('End');assert.equal(await focused(rows.last()),true);await page.keyboard.press('ArrowDown');assert.equal(await focused(rows.last()),true);
    await page.keyboard.press('Home');assert.equal(await focused(rows.first()),true);await page.keyboard.press('ArrowUp');assert.equal(await focused(rows.first()),true);assert.deepEqual(await events(),[]);
    await page.keyboard.press('ArrowDown');await page.keyboard.press('Enter');await page.keyboard.press('Space');
    const expected={type:'select',item:{id:'duration:exact/002',label:'Beta source',weight:20,duration:10,comparisonContribution:1.5,sourceKey:'source:002',contribution:2,cmp:1.5,diff:0.5,incomplete:false},index:1};assert.deepEqual(await events(),[expected,expected]);
    assert.equal(await rows.first().getAttribute('aria-selected'),'true');assert.equal(await rows.nth(1).getAttribute('aria-selected'),'false');
    await mount('DurationContribution',{rank:'label',selectedId:'duration:exact/002'},false);assert.equal(await rows.nth(1).getAttribute('aria-selected'),'true');assert.equal(await rows.nth(1).getAttribute('tabindex'),'0');
  });
  await test('DurationContribution','double-click invokes exact trace row after ranking and readonly removes callbacks', ['trace','pointer','ranking','readonly','exact-payload'], async()=>{
    await mount('DurationContribution',{rank:'label'});await page.getByRole('option').first().dblclick();
    const trace=(await events()).filter(e=>e.type==='trace');assert.equal(trace.length,1);assert.equal(trace[0].item.id,'duration:exact/001');assert.equal(trace[0].item.sourceKey,'source:001');assert.equal(trace[0].item.contribution,0.7);assert.equal(trace[0].item.cmp,0.1);assert.ok(Math.abs(trace[0].item.diff-0.6)<1e-12);assert.equal(trace[0].item.incomplete,false);
    await mount('DurationContribution',{readOnly:true});await page.getByRole('option').first().press('Enter');await page.getByRole('option').first().press('Space');await page.getByRole('option').first().dblclick();assert.deepEqual(await events(),[]);
  });
  await test('DurationContribution','empty/recovery clears ranked options and restores first-row tab entry', ['empty','recovery','keyboard'], async()=>{
    await mount('DurationContribution',{},false);await mount('DurationContribution',{empty:true},false);assert.equal(await page.getByRole('option').count(),0);assert.equal(await page.locator('tbody tr').count(),1);assert.doesNotMatch(await page.locator('#fixture').innerHTML(),/NaN|Infinity/);
    await mount('DurationContribution',{},false);assert.equal(await page.getByRole('option').count(),3);assert.equal(await page.getByRole('option').first().getAttribute('tabindex'),'0');assert.deepEqual(await labels('.mds-dc__label'),rankLabels.contribution);
  });

  await test('CorrelationHeatmap','real pointer hover returns exact numeric row/col/value including negative and zero', ['hover','negative','zero','exact-payload'], async()=>{
    await mount('CorrelationHeatmap');const cells=page.getByRole('cell');assert.equal(await cells.count(),9);
    await cells.nth(1).hover();await cells.nth(6).hover();await cells.nth(8).hover();
    assert.deepEqual(await events(),[{type:'hover',row:0,col:1,value:-0.625},{type:'hover',row:2,col:0,value:0},{type:'hover',row:2,col:2,value:1}]);
    assert.equal(await cells.nth(1).getAttribute('title'),'First series · Second series = -0.625');assert.equal(await cells.nth(6).textContent(),'0.000');
  });
  await test('CorrelationHeatmap','hidden values retain exact hover payload and tooltips; absent handler remains inert', ['hover','hidden-values','readonly'], async()=>{
    await mount('CorrelationHeatmap',{showValues:false});const cells=page.getByRole('cell');assert.deepEqual(await cells.allTextContents(),Array(9).fill(''));
    await cells.nth(5).hover();assert.deepEqual(await events(),[{type:'hover',row:1,col:2,value:0.125}]);assert.equal(await cells.nth(5).getAttribute('title'),'Second series · Third series = 0.125');
    await mount('CorrelationHeatmap',{readOnly:true});await page.getByRole('cell').first().hover();assert.deepEqual(await events(),[]);
  });
  await test('CorrelationHeatmap','empty/recovery clears stale matrix cells and axis headers', ['empty','recovery'], async()=>{
    await mount('CorrelationHeatmap',{},false);await mount('CorrelationHeatmap',{empty:true},false);assert.equal(await page.getByRole('cell').count(),0);assert.equal(await page.getByRole('rowheader').count(),0);assert.equal(await page.getByRole('columnheader').count(),1);assert.deepEqual(await events(),[]);
    await mount('CorrelationHeatmap',{},false);assert.equal(await page.getByRole('cell').count(),9);assert.deepEqual(await page.getByRole('rowheader').allTextContents(),['First series','Second series','Third series']);
  });

  await test('Treemap','native Enter/Space activate sorted positive tiles with exact retained item fields and measured area', ['keyboard','exact-payload','positive-weights','resize'], async()=>{
    await mount('Treemap');const tiles=page.locator('[data-testid="treemap-host"] button');await tiles.first().waitFor();assert.equal(await tiles.count(),2);
    assert.equal(await tiles.first().getAttribute('title'),'Largest source · USD 30 · +0.50%');await tiles.first().focus();await tiles.first().press('Enter');await tiles.nth(1).focus();await tiles.nth(1).press('Space');
    const actual=await events();assert.equal(actual.length,2);
    assert.deepEqual(Object.fromEntries(Object.entries(actual[0].item).filter(([key])=>key!=='area')),{label:'Largest source',value:30,delta:0.5,group:'Group B'});
    assert.deepEqual(Object.fromEntries(Object.entries(actual[1].item).filter(([key])=>key!=='area')),{label:'Small source',value:10,delta:-0.25,group:'Group A'});
    const bounds=await tiles.first().evaluate(n=>({w:n.parentElement.clientWidth,h:n.parentElement.clientHeight}));assert.equal(actual[0].item.area,bounds.w*bounds.h*0.75);assert.equal(actual[1].item.area,bounds.w*bounds.h*0.25);assert.equal(await focused(tiles.nth(1)),true);
    await mount('Treemap',{width:420},false);await page.waitForFunction(()=>{
      const tile=document.querySelector('[data-testid="treemap-host"] button');return tile&&Math.abs(parseFloat(tile.style.width)*parseFloat(tile.style.height)-418*238*0.75)<0.001;
    });await tiles.first().press('Enter');assert.equal((await events())[0].item.value,30);assert.equal((await events())[0].item.area,418*238*0.75);
  });
  await test('Treemap','empty/recovery and removing selection callback remove stale keyboard targets', ['empty','recovery','readonly'], async()=>{
    await mount('Treemap',{},false);await page.locator('[data-testid="treemap-host"] button').first().waitFor();await mount('Treemap',{empty:true},false);assert.equal(await page.locator('[data-testid="treemap-host"] [title]').count(),0);
    await mount('Treemap',{readOnly:true},false);await page.locator('[data-testid="treemap-host"] div[title]').first().waitFor();assert.equal(await page.locator('[data-testid="treemap-host"] button').count(),0);assert.equal(await page.locator('[data-testid="treemap-host"] div[title]').count(),2);assert.deepEqual(await events(),[]);
  });

  await test('PlateGrid','native keyboard callbacks preserve exact ID and controlled selected plate', ['keyboard','controlled','selection','exact-payload'], async()=>{
    await mount('PlateGrid',{selectedId:'plate:exact/002'},false);const plates=page.locator('button.mds-plate');const before=await plates.evaluateAll(nodes=>nodes.map(n=>({w:n.clientWidth,h:n.clientHeight})));
    await plates.nth(1).focus();await plates.nth(1).press('Enter');await plates.nth(1).press('Space');assert.deepEqual(await events(),[{type:'select',id:'plate:exact/001'},{type:'select',id:'plate:exact/001'}]);assert.equal(await plates.first().getAttribute('aria-pressed'),'true');assert.equal(await plates.nth(1).getAttribute('aria-pressed'),'false');
    await mount('PlateGrid',{selectedId:'plate:exact/001'},false);assert.equal(await plates.first().getAttribute('aria-pressed'),'false');assert.equal(await plates.nth(1).getAttribute('aria-pressed'),'true');assert.deepEqual(await plates.evaluateAll(nodes=>nodes.map(n=>({w:n.clientWidth,h:n.clientHeight}))),before);
  });
  await test('PlateGrid','reordering keeps shared axis geometry and exact ID mapping; empty/readonly recover', ['reorder','keyboard','shared-scale','empty','recovery','readonly'], async()=>{
    await mount('PlateGrid',{},false);const axes=await page.locator('.mds-plate svg').evaluateAll(nodes=>nodes.map(n=>[...n.querySelectorAll('line')].map(l=>l.getAttribute('y1'))));assert.deepEqual(axes[0],axes[1]);
    await mount('PlateGrid',{reversed:true},false);assert.deepEqual(await labels('.mds-plate__t'),['First source','Second source']);await page.locator('button.mds-plate').first().press('Enter');assert.deepEqual(await events(),[{type:'select',id:'plate:exact/001'}]);
    await mount('PlateGrid',{empty:true},false);assert.equal(await page.locator('.mds-plate').count(),0);await mount('PlateGrid',{readOnly:true},false);assert.equal(await page.locator('button.mds-plate').count(),0);assert.equal(await page.locator('.mds-plate').count(),2);
  });
  await test('ComparisonPlate','native standalone activation fires callback and parent controls pressed state', ['keyboard','controlled','selection'], async()=>{
    await mount('ComparisonPlate',{selected:false},false);const plate=page.locator('button.mds-plate');await plate.focus();await plate.press('Enter');await plate.press('Space');
    // The native React click handler passes its event; the public callback ignores it.
    assert.deepEqual((await events()).map(({type})=>({type})),[{type:'select'},{type:'select'}]);assert.equal(await plate.getAttribute('aria-pressed'),'false');assert.equal(await focused(plate),true);
    await mount('ComparisonPlate',{selected:true},false);assert.equal(await plate.getAttribute('aria-pressed'),'true');assert.equal(await page.locator('svg circle').count(),2);
    await mount('ComparisonPlate',{readOnly:true},false);assert.equal(await page.locator('button').count(),0);assert.equal(await page.locator('.mds-plate').count(),1);
  });
  await test('DurationContribution','incomplete inputs keep explicit contribution and exact callback while residual is named; complete inputs recover', ['incomplete','residual','missing','keyboard','recovery'], async()=>{
    await mount('DurationContribution',{incomplete:true,total:2,rank:'label'});
    const incomplete=page.getByRole('option').filter({hasText:'Incomplete source'});
    assert.equal(await incomplete.count(),1);assert.equal(await incomplete.locator('[data-missing="true"]').count(),2);assert.match(await incomplete.textContent(),/Incomplete inputs.*Missing.*Missing.*1\.25/);
    assert.equal(await page.locator('.mds-dc__tie').getAttribute('data-ok'),'false');assert.match(await page.locator('.mds-dc__tie').textContent(),/Contributions sum to 0\.95 against a stated total of 2\.00 — residual \+1\.05 years is unexplained/);
    assert.match(await page.locator('.mds-dc__tie').textContent(),/1 row missing weight or duration; only explicitly supplied contributions remain available/);
    await incomplete.press('Space');assert.deepEqual(await events(),[{type:'select',item:{id:'duration:exact/incomplete',label:'Incomplete source',contribution:1.25,comparisonContribution:0.5,cmp:0.5,diff:0.75,incomplete:true},index:3}]);
    const row=page.locator('tbody tr').filter({hasText:'Incomplete source'});assert.deepEqual(await row.locator('td').allTextContents(),['Missing','Missing','1.25','0.50','+0.75','confirmed']);
    await mount('DurationContribution',{total:-0.3,rank:'label'},false);assert.equal(await page.locator('.mds-dc__tie').getAttribute('data-ok'),'true');assert.match(await page.locator('.mds-dc__tie').textContent(),/Contributions tie to the stated portfolio duration of -0\.30/);assert.equal(await page.locator('[data-missing="true"]').count(),0);assert.equal(await page.getByText('Incomplete source',{exact:true}).count(),0);
  });
  await test('LineageGraph','cyclic lineage renders bounded exact topology and native selection; acyclic and empty inputs recover', ['cycle','keyboard','controlled','selection','recovery','empty'], async()=>{
    await mount('LineageGraph',{cycle:true,selectedId:'graph:exact/A'});const nodes=page.locator('.mds-lin__node');
    assert.equal(await nodes.count(),3);assert.equal(await page.locator('.mds-lin__edges path').count(),3);assert.deepEqual(await labels('.mds-lin__nl'),['Graph Alpha','Graph Beta','Graph Gamma']);
    assert.deepEqual(await labels('.mds-lin__out li'),['Graph Alpha — Source A → Graph Beta','Graph Beta — Model B → Graph Gamma','Graph Gamma — Output C → Graph Alpha']);
    const bounds=await page.locator('.mds-lin__canvas').evaluate(node=>({width:node.clientWidth,height:node.clientHeight,children:[...node.querySelectorAll('.mds-lin__node')].map(n=>({x:n.offsetLeft,y:n.offsetTop,w:n.offsetWidth,h:n.offsetHeight}))}));
    assert.ok(bounds.width>0&&bounds.width<10000);assert.ok(bounds.height>0&&bounds.height<10000);assert.ok(bounds.children.every(n=>n.x>=0&&n.y>=0&&n.x+n.w<=bounds.width&&n.y+n.h<=bounds.height));assert.doesNotMatch(await page.locator('.mds-lin__edges').innerHTML(),/NaN|Infinity/);
    await nodes.nth(1).focus();await nodes.nth(1).press('Enter');assert.deepEqual(await events(),[{type:'select',id:'graph:exact/B'}]);assert.equal(await nodes.first().getAttribute('aria-pressed'),'true');assert.equal(await nodes.nth(1).getAttribute('aria-pressed'),'false');
    await mount('LineageGraph',{selectedId:'graph:exact/B'},false);assert.equal(await page.locator('.mds-lin__edges path').count(),2);assert.ok(await page.locator('.mds-lin__canvas').evaluate(n=>n.clientWidth)<bounds.width);assert.equal(await nodes.nth(1).getAttribute('aria-pressed'),'true');assert.equal(await page.locator('.mds-lin__out li').last().textContent(),'Graph Gamma — Output C');
    await mount('LineageGraph',{empty:true},false);assert.equal(await nodes.count(),0);assert.equal(await page.locator('.mds-lin__edges path').count(),0);await mount('LineageGraph',{cycle:true},false);assert.equal(await nodes.count(),3);await nodes.last().press('Space');assert.deepEqual(await events(),[{type:'select',id:'graph:exact/C'}]);
  });
  await test('LineageGraph','host resize retains natural cycle canvas, exact focused node and selection ID', ['cycle','resize','keyboard','focus','controlled'], async()=>{
    await mount('LineageGraph',{cycle:true,selectedId:'graph:exact/A'});const node=page.locator('.mds-lin__node').nth(1);await node.focus();await node.evaluate(n=>{window.retainedGraphNode=n});
    const before=await page.locator('.mds-lin__canvas').evaluate(n=>({width:n.clientWidth,height:n.clientHeight,positions:[...n.querySelectorAll('.mds-lin__node')].map(x=>[x.style.left,x.style.top])}));const wide=await page.locator('.mds-lin__vp').evaluate(n=>n.clientWidth);
    await mount('LineageGraph',{cycle:true,width:420,selectedId:'graph:exact/A'},false);assert.ok(await page.locator('.mds-lin__vp').evaluate(n=>n.clientWidth)<wide);assert.deepEqual(await page.locator('.mds-lin__canvas').evaluate(n=>({width:n.clientWidth,height:n.clientHeight,positions:[...n.querySelectorAll('.mds-lin__node')].map(x=>[x.style.left,x.style.top])})),before);
    assert.equal(await node.evaluate(n=>n===window.retainedGraphNode&&n===document.activeElement),true);await node.press('Space');assert.deepEqual(await events(),[{type:'select',id:'graph:exact/B'}]);assert.equal(await page.locator('.mds-lin__node').first().getAttribute('aria-pressed'),'true');
  });
  async function ownershipEdgeMatchesNodes() {
    const coordinates=await page.locator('.mds-own').evaluate(wrap=>{
      const box=wrap.getBoundingClientRect();const byLabel=label=>[...wrap.querySelectorAll('.mds-own__node')].find(n=>n.querySelector('.mds-own__name').textContent===label).getBoundingClientRect();const a=byLabel('Owner Alpha'),b=byLabel('Owner Beta');
      const path=wrap.querySelector('.mds-own__edge').getAttribute('d').match(/-?\d+(?:\.\d+)?/g).map(Number);
      return {start:path.slice(0,2),end:path.slice(-2),expectedStart:[a.left-box.left+a.width/2+wrap.scrollLeft,a.bottom-box.top+wrap.scrollTop],expectedEnd:[b.left-box.left+b.width/2+wrap.scrollLeft,b.top-box.top+wrap.scrollTop]};
    });
    for(const [actual,expected] of [[coordinates.start,coordinates.expectedStart],[coordinates.end,coordinates.expectedEnd]])for(let i=0;i<2;i++)assert.ok(Math.abs(actual[i]-expected[i])<0.1,`Measured edge coordinate ${actual[i]} must match node coordinate ${expected[i]}`);
  }
  await test('OwnershipGraph','cyclic DTO topology keeps exact normalized selection and relationship titles and recovers', ['cycle','keyboard','controlled','exact-payload','relationship','recovery','empty'], async()=>{
    await mount('OwnershipGraph',{cycle:true,selectedId:'owner:exact/A'});await page.locator('.mds-own__edge').last().waitFor({state:'attached'});assert.equal(await page.locator('.mds-own__edge').count(),3);assert.equal(await page.locator('.mds-own__node').count(),3);assert.deepEqual(await labels('.mds-own__pct'),['75%','100%','25%']);assert.deepEqual(await labels('.mds-own__svg > g > title'),['Member · 75%','Owner · 100%','Returns · 25%']);assert.doesNotMatch(await page.locator('.mds-own__svg').innerHTML(),/NaN|Infinity/);
    assert.ok(await page.locator('.mds-own').evaluate(n=>n.scrollHeight)<10000);const beta=page.locator('.mds-own__node').filter({hasText:'Owner Beta'});await beta.press('Enter');assert.deepEqual(await events(),[{type:'select',id:'owner:exact/B',node:{id:'owner:exact/B',label:'Owner Beta',type:'Fund',jurisdiction:'SD',currency:'USD'}}]);assert.equal(await beta.getAttribute('aria-pressed'),'false');
    await mount('OwnershipGraph',{selectedId:'owner:exact/B'},false);await page.waitForFunction(()=>document.querySelectorAll('.mds-own__edge').length===2);assert.equal(await beta.getAttribute('aria-pressed'),'true');assert.deepEqual(await labels('.mds-own__svg > g > title'),['Member · 75%','Owner · 100%']);
    await mount('OwnershipGraph',{empty:true},false);assert.equal(await page.locator('.mds-own__node').count(),0);await page.waitForFunction(()=>document.querySelectorAll('.mds-own__edge').length===0);await mount('OwnershipGraph',{cycle:true},false);await page.waitForFunction(()=>document.querySelectorAll('.mds-own__edge').length===3);assert.equal(await page.locator('.mds-own__node').count(),3);
  });
  await test('OwnershipGraph','cycle edge measurements follow resize while keyboard callback preserves exact normalized node', ['cycle','resize','keyboard','controlled','exact-payload'], async()=>{
    await mount('OwnershipGraph',{cycle:true,selectedId:'owner:exact/A'});await page.locator('.mds-own__edge').last().waitFor({state:'attached'});await ownershipEdgeMatchesNodes();const oldPath=await page.locator('.mds-own__edge').first().getAttribute('d');
    const gamma=page.locator('.mds-own__node').filter({hasText:'Owner Gamma'});await gamma.focus();await gamma.evaluate(n=>{window.retainedOwnerNode=n});
    await mount('OwnershipGraph',{cycle:true,width:420,selectedId:'owner:exact/A'},false);await page.waitForFunction(oldPath=>document.querySelector('.mds-own__edge')?.getAttribute('d')!==oldPath,oldPath);await ownershipEdgeMatchesNodes();assert.equal(await gamma.evaluate(n=>n===window.retainedOwnerNode&&n===document.activeElement),true);
    await gamma.press('Space');assert.deepEqual(await events(),[{type:'select',id:'owner:exact/C',node:{id:'owner:exact/C',label:'Owner Gamma',type:'Account',jurisdiction:null,currency:null}}]);assert.equal(await gamma.getAttribute('aria-pressed'),'false');assert.equal(await page.locator('.mds-own__node[aria-pressed="true"] .mds-own__name').textContent(),'Owner Alpha');
  });
  await test('OwnershipGraph','semantic relationship list preserves exact labels/type and positive/zero/missing ownership through updates without changing Tab order or callbacks', ['accessible-data','relationship','positive','zero','missing','controlled','keyboard','focus','exact-payload','replacement'], async()=>{
    await mount('OwnershipGraph',{relationshipEvidence:true,selectedId:'owner:exact/A'});
    const list=page.getByRole('list',{name:'Ownership relationships',exact:true}),nodes=page.locator('button.mds-own__node');
    assert.deepEqual(await list.locator('li').allTextContents(),['Owner Alpha → Owner Beta · Member · 75% ownership','Owner Beta → Owner Gamma · Owner · 0% ownership','Owner Gamma → Owner Alpha · Returns · Ownership percentage unavailable']);
    assert.equal(await page.locator('.mds-own__svg').getAttribute('aria-hidden'),'true');assert.equal(await list.evaluate(n=>n.closest('[aria-hidden="true"],[inert]')===null),true);
    assert.equal(await list.locator('button,a,input,select,textarea,[tabindex],[contenteditable="true"]').count(),0);assert.equal(await page.locator('button').count(),3);
    const order=await labels('.mds-own__name');assert.deepEqual([...order].sort(),['Owner Alpha','Owner Beta','Owner Gamma']);
    await nodes.evaluateAll(n=>{window.ownerTabOrder=n});await nodes.first().focus();await page.keyboard.press('Tab');assert.equal(await focused(nodes.nth(1)),true);await page.keyboard.press('Tab');assert.equal(await focused(nodes.nth(2)),true);assert.deepEqual(await events(),[]);
    await nodes.filter({hasText:'Owner Beta'}).press('Enter');assert.deepEqual(await events(),[{type:'select',id:'owner:exact/B',node:{id:'owner:exact/B',label:'Owner Beta',type:'Fund',jurisdiction:'SD',currency:'USD'}}]);assert.equal(await nodes.filter({hasText:'Owner Beta'}).getAttribute('aria-pressed'),'false');
    await mount('OwnershipGraph',{relationshipEvidence:true,relationshipUpdated:true,selectedId:'owner:exact/A'},false);
    assert.deepEqual(await list.locator('li').allTextContents(),['Owner Alpha revised → Owner Beta revised · Participant · 62.5% ownership','Owner Beta revised → Owner Gamma revised · Custodian · 0% ownership','Owner Gamma revised → Owner Alpha revised · Guarantor · Ownership percentage unavailable']);
    assert.doesNotMatch(await list.textContent(),/ · Member| · Owner ·| · Returns|75% ownership|Owner Alpha →|Owner Beta →|Owner Gamma →/);
    assert.equal(await nodes.evaluateAll(n=>n.length===window.ownerTabOrder.length&&n.every((node,i)=>node===window.ownerTabOrder[i])),true);assert.deepEqual(await labels('.mds-own__name'),order.map(label=>label+' revised'));assert.equal(await list.locator('button,a,input,select,textarea,[tabindex],[contenteditable="true"]').count(),0);
    await nodes.first().focus();await page.keyboard.press('Tab');assert.equal(await focused(nodes.nth(1)),true);await page.keyboard.press('Tab');assert.equal(await focused(nodes.nth(2)),true);assert.deepEqual(await events(),[]);
    await nodes.filter({hasText:'Owner Beta revised'}).press('Space');assert.deepEqual(await events(),[{type:'select',id:'owner:exact/B',node:{id:'owner:exact/B',label:'Owner Beta revised',type:'Fund',jurisdiction:'SD',currency:'USD'}}]);assert.equal(await nodes.filter({hasText:'Owner Beta revised'}).getAttribute('aria-pressed'),'false');assert.equal(await nodes.filter({hasText:'Owner Alpha revised'}).getAttribute('aria-pressed'),'true');
  });
  await test('TraceChart','real hover exposes exact native category title without committing controlled selection', ['hover','preview','controlled','missing','zero'], async()=>{
    await mount('TraceChart',{selected:{seriesId:'trace:exact/prior',index:0}});await page.waitForFunction(()=>document.querySelector('.mds-trc__svg')?.getAttribute('viewBox')==='0 0 820 320');
    const exactTable=await labels('.mds-trc__sr tbody tr');assert.deepEqual(exactTable,['Start0.001.00','MissingMissing2.00','End5.003.00']);const selectedBefore=await page.locator('.mds-trc__svg circle[r="5"]').evaluateAll(nodes=>nodes.map(n=>[n.getAttribute('cx'),n.getAttribute('cy')]));assert.equal(selectedBefore.length,2);
    const hit=page.locator('.mds-trc__hit').nth(1);await hit.hover();assert.equal(await hit.locator('title').textContent(),'Missing — select');assert.deepEqual(await events(),[]);assert.deepEqual(await page.locator('.mds-trc__svg circle[r="5"]').evaluateAll(nodes=>nodes.map(n=>[n.getAttribute('cx'),n.getAttribute('cy')])),selectedBefore);assert.deepEqual(await labels('.mds-trc__sr tbody tr'),exactTable);
    await hit.click();assert.deepEqual(await events(),[{type:'select',seriesId:'trace:exact/prior',index:1}]);assert.deepEqual(await page.locator('.mds-trc__svg circle[r="5"]').evaluateAll(nodes=>nodes.map(n=>[n.getAttribute('cx'),n.getAttribute('cy')])),selectedBefore);
  });
  await test('TraceChart','distinct closed model bands resize with the plot while hover and selection retain exact category mapping', ['model-band','resize','hover','controlled','selection','missing','zero'], async()=>{
    await mount('TraceChart',{band:true,selected:{seriesId:'trace:exact/actual',index:2}});await page.waitForFunction(()=>document.querySelector('.mds-trc__svg')?.getAttribute('viewBox')==='0 0 820 320');const bands=page.locator('.mds-trc__svg > g[aria-hidden="true"] > path');assert.equal(await bands.count(),2);
    assert.deepEqual(await bands.evaluateAll(nodes=>nodes.map(n=>n.style.fill)),['var(--viz-band-outer)','var(--viz-band-inner)']);for(const d of await bands.evaluateAll(nodes=>nodes.map(n=>n.getAttribute('d')))){assert.match(d,/Z$/);assert.doesNotMatch(d,/NaN|Infinity/)}
    const before=await bands.first().getAttribute('d'),table=await labels('.mds-trc__sr tbody tr');const originalLabel=await labels('.mds-trc__label');assert.equal(originalLabel.some(x=>x.includes('Hidden source')),false);
    await mount('TraceChart',{band:true,width:420,selected:{seriesId:'trace:exact/actual',index:2}},false);await page.waitForFunction(()=>document.querySelector('.mds-trc__svg')?.getAttribute('viewBox')==='0 0 420 320');assert.notEqual(await bands.first().getAttribute('d'),before);assert.deepEqual(await labels('.mds-trc__sr tbody tr'),table);assert.deepEqual(await labels('.mds-trc__label'),originalLabel);
    const end=page.locator('.mds-trc__hit').last();await end.hover();assert.equal(await end.locator('title').textContent(),'End — select');assert.deepEqual(await events(),[]);await end.click();assert.deepEqual(await events(),[{type:'select',seriesId:'trace:exact/actual',index:2}]);assert.equal(await page.locator('.mds-trc__svg circle[r="5"]').count(),2);
    await mount('TraceChart',{width:420},false);assert.equal(await bands.count(),0);assert.deepEqual(await labels('.mds-trc__sr tbody tr'),table);
  });
  await test('WatchlistStrip','missing change is neutral unavailable evidence and differs from explicit amount/percent zero', ['missing-change','missing','zero','keyboard','controlled','exact-payload'], async()=>{
    await mount('WatchlistStrip',{selectedId:'watch:exact/zero'});const rows=page.getByRole('option');
    const cells=await rows.evaluateAll(nodes=>nodes.map(n=>({price:n.children[1].textContent,change:n.children[2].textContent,tone:n.children[2].style.color})));assert.deepEqual(cells,[{price:'10.00',change:'—',tone:'var(--text-muted)'},{price:'0.00',change:'+0.00',tone:'var(--series-positive)'},{price:'12.00',change:'+0.00%',tone:'var(--series-positive)'},{price:'Missing',change:'—',tone:'var(--text-muted)'}]);
    await rows.first().press('Space');await rows.last().press('Enter');assert.deepEqual(await events(),[{type:'select',id:'watch:exact/missing'},{type:'select',id:'UNKNOWN'}]);assert.equal(await rows.nth(1).getAttribute('aria-selected'),'true');assert.equal(await rows.first().getAttribute('aria-selected'),'false');
  });
  await test('WatchlistStrip','missing to observed zero and back retains exact item selection and never invents a change', ['missing-change','recovery','zero','keyboard','controlled'], async()=>{
    await mount('WatchlistStrip',{selectedId:'watch:exact/missing'});const row=page.getByRole('option').first();await row.focus();await row.evaluate(n=>{window.retainedWatchRow=n});
    await mount('WatchlistStrip',{selectedId:'watch:exact/missing',recovered:true},false);assert.equal(await row.evaluate(n=>n.children[2].textContent),'+0.00');assert.equal(await row.evaluate(n=>n.children[2].style.color),'var(--series-positive)');assert.equal(await row.evaluate(n=>n===window.retainedWatchRow&&n===document.activeElement),true);await row.press('Enter');assert.deepEqual(await events(),[{type:'select',id:'watch:exact/missing'}]);
    await mount('WatchlistStrip',{selectedId:'watch:exact/missing'},false);assert.equal(await row.evaluate(n=>n.children[2].textContent),'—');assert.equal(await row.evaluate(n=>n.children[2].style.color),'var(--text-muted)');assert.equal(await row.getAttribute('aria-selected'),'true');assert.deepEqual(await events(),[]);
  });
} finally {
  await browser.close();
  for(const [name,source] of Object.entries(sources)) {
    if(hash(await readFile(resolve(root,source)))!==report.exports[name].sha256) report.errors.push(`${name} source changed during execution; rerun final source`);
  }
  report.summary={cases:report.cases.length,passed:report.cases.filter(c=>c.status==='passed').length,failed:report.cases.filter(c=>c.status==='failed').length,browserErrors:report.errors.length};
  const output=process.env.DOMAIN_LADDER_ACTION_RESULTS || '/tmp/pr3501-full-audit/domain-ladder-action-results.json';
  await mkdir(dirname(output),{recursive:true});await writeFile(output,JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify({output,...report.summary}));
}
if(report.summary.failed||report.errors.length) process.exitCode=1;
