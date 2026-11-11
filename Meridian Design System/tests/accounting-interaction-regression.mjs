#!/usr/bin/env node
// Source-built contracts for accounting selection, keyboard access and financial absence.
import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';
import { chromium } from 'playwright';
const root=resolve(dirname(fileURLToPath(import.meta.url)),'..');
const names=['AccountTree','AccountingPeriodSelector','AllocationEditor','CloseCalendar','IncomeSchedule','LifecycleRibbon','ScheduleLadder','AgingTable','CertificationMatrix','FxRevaluationTable','LedgerTable','PostingImpactPreview','RollforwardTrace','TaxLotTable','TrialBalance'];
const manifest=JSON.parse(await readFile(resolve(root,'_ds_manifest.json'),'utf8'));const sourcePaths=Object.fromEntries(manifest.components.map(entry=>[entry.name,entry.sourcePath]));
const entry=`import React from 'react'; import {createRoot} from 'react-dom/client'; window.React=React;window.ReactDOM={createRoot};\n`+names.map(name=>`import {${name}} from './${sourcePaths[name]}';`).join('\n')+`\nwindow.contracts={${names.join(',')}};`;
const output=await build({absWorkingDir:root,stdin:{contents:entry,resolveDir:root,loader:'jsx'},write:false,bundle:true,format:'iife',platform:'browser',resolveExtensions:['.js','.jsx','.ts','.tsx','.json'],define:{'process.env.NODE_ENV':'"production"'},logLevel:'error'});
const browser=await chromium.launch({executablePath:process.env.MERIDIAN_CHROMIUM_PATH||process.env.CHROMIUM_EXECUTABLE_PATH||'/usr/bin/chromium',args:['--no-sandbox']});
const page=await browser.newPage({viewport:{width:1366,height:768}}),errors=[],cases=[];
page.on('pageerror',error=>errors.push(error.message));
const eventually=async check=>{let last;for(let n=0;n<30;n++){try{return await check();}catch(error){last=error;await page.waitForTimeout(30);}}throw last;};
const run=async(name,states,fn)=>{try{await fn();cases.push({name,states,status:'passed'});}catch(error){cases.push({name,states,status:'failed',error:error.message});console.error('FAIL '+name+': '+error.message);}};
const selected=(locator,attribute='aria-selected',value='true')=>eventually(async()=>assert.equal(await locator.getAttribute(attribute),value));
const mount=async(name,props={},interactive=true)=>{await page.evaluate(({name,props,interactive})=>window.mountAccounting(name,props,interactive),{name,props,interactive});await page.waitForTimeout(40);};
try{
 await page.setContent('<!doctype html><html lang="en"><head><title>Accounting source contracts</title></head><body><main id="fixture"></main></body></html>');
 await page.addScriptTag({content:output.outputFiles[0].text});
 await page.evaluate(()=>{
  window.accountingRoot=ReactDOM.createRoot(document.getElementById('fixture'));window.sequence=0;
  window.mountAccounting=(name,props,interactive)=>{
   window.accountingEvents=[];window.fixtureProps=props;const id=++window.sequence;
   function Fixture(){
    const [selection,setSelection]=React.useState(props.selectedIndex??props.selectedCode??props.selectedId??props.value);
    const [view,setView]=React.useState(props.view||'calendar');const next={...props};
    const event=value=>window.accountingEvents.push(value);
    if(interactive){
     if(name==='AccountTree'){next.selectedCode=selection;next.onSelect=node=>{event(node);setSelection(node.code);};}
     if(name==='AccountingPeriodSelector'){next.value=selection;next.onChange=(value,period)=>{event({value,period});setSelection(value);};}
     if(name==='CloseCalendar'){next.selectedId=selection;next.onSelect=task=>{event(task);setSelection(task.id);};next.view=view;next.onViewChange=value=>{event({view:value});setView(value);};}
     if(name==='IncomeSchedule'||name==='LifecycleRibbon'){next.selectedId=selection;next.onSelect=value=>{event(value);setSelection(value);};}
     if(name==='ScheduleLadder'){next.selectedId=selection;next.onSelectRow=row=>{event({select:row});setSelection(row.id);};next.onOpenTrace=row=>event({trace:row});}
     if(name==='AllocationEditor')next.onChange=(amounts,weights)=>event({amounts,weights});
     if(['AgingTable','FxRevaluationTable','LedgerTable','TaxLotTable','TrialBalance'].includes(name)){next.selectedIndex=selection;next.onRowClick=(row,index)=>{event({row,index});setSelection(index);};}
     if(name==='CertificationMatrix')next.onOpenAccount=code=>event({code});
     if(name==='PostingImpactPreview'){next.onPost=()=>event({post:true});next.onCancel=()=>event({cancel:true});}
     if(name==='RollforwardTrace')next.movements=(props.movements||[]).map(movement=>({...movement,...(movement.linkLabel?{onOpen:()=>event({movement:movement.id})}:{})}));
    }
    return React.createElement(window.contracts[name],next);
   }
   accountingRoot.render(React.createElement(Fixture,{key:id}));
  };
 });
 const nodes=[{code:'1000',name:'Assets',children:[{code:'1100',name:'Cash',balance:0},{code:'1200',name:'Investments',balance:25.01}]}];
 await run('AccountTree roving focus, expand/collapse and exact controlled identity',['keyboard','focus','controlled-state','selection'],async()=>{
  await mount('AccountTree',{nodes,defaultExpandedDepth:0,selectedCode:'1000'});
  const parent=page.getByRole('treeitem').first();await parent.focus();await parent.press('ArrowRight');await selected(parent,'aria-expanded');
  await parent.press('ArrowRight');assert(await page.getByRole('treeitem').nth(1).evaluate(node=>node===document.activeElement));
  await page.keyboard.press('Enter');await selected(page.getByRole('treeitem').nth(1));assert.equal((await page.evaluate(()=>accountingEvents.at(-1))).code,'1100');
  await page.keyboard.press('End');assert(await page.getByRole('treeitem').last().evaluate(node=>node===document.activeElement));await page.keyboard.press('ArrowLeft');assert(await parent.evaluate(node=>node===document.activeElement));await page.keyboard.press('ArrowLeft');await selected(parent,'aria-expanded','false');
 });
 await run('AccountTree empty recovery and missing versus explicit parent zero',['empty','recovery','missing','zero'],async()=>{
  await mount('AccountTree',{nodes:[]});await page.getByText('No accounts',{exact:true}).waitFor();
  await mount('AccountTree',{nodes:[{code:'A',name:'Uncertain assets',children:[{code:'known',name:'Known',balance:10},{code:'unknown',name:'Unavailable'}]}]});
  assert.match(await page.getByRole('treeitem').first().innerText(),/Missing|—/,'Unknown descendant cannot be a known zero in the roll-up');
  await mount('AccountTree',{nodes:[{code:'A',name:'Explicit zero',balance:0,children:[{code:'unknown',name:'Unavailable'}]}]});assert.match(await page.getByRole('treeitem').first().innerText(),/0\.00/);
 });
 const periods=[{value:'2026-07',label:'July 2026',state:'open',postingPeriod:'2026-07',reportingPeriod:'July 2026'},{value:'2026-06',label:'June 2026',state:'locked',lockedAfter:'July 5, 2026 at 18:00 UTC',postingPeriod:'2026-06',reportingPeriod:'June 2026',permission:'Read-only'}];
 await run('AccountingPeriodSelector locked periods inspectable, controlled selection and distinct dates',['controlled-state','keyboard','focus','locked','permissions'],async()=>{
  await mount('AccountingPeriodSelector',{periods,value:'2026-07',dates:{transaction:'2026-06-29',posting:'2026-07-02',reporting:'July 2026'}});
  const select=page.getByRole('combobox',{name:'Accounting period',exact:true});await select.focus();assert(await select.evaluate(node=>node===document.activeElement));await select.selectOption('2026-06');
  await eventually(async()=>assert.equal(await select.inputValue(),'2026-06'));assert.match(await page.locator('#fixture').innerText(),/Editing and posting are unavailable/);assert.match(await page.locator('#fixture').innerText(),/2026-06-29/);assert.match(await page.locator('#fixture').innerText(),/2026-07-02/);assert.equal((await page.evaluate(()=>accountingEvents.at(-1))).period.state,'locked');
 });
 await run('AccountingPeriodSelector disabled and empty recovery',['disabled','empty','recovery'],async()=>{
  await mount('AccountingPeriodSelector',{periods,value:'2026-07',disabled:true});assert(await page.getByRole('combobox').isDisabled());assert.equal(await page.evaluate(()=>accountingEvents.length),0);
  await mount('AccountingPeriodSelector',{periods:[]});assert.equal(await page.locator('select option').count(),0);
  await mount('AccountingPeriodSelector',{periods,value:'2026-07'});assert.equal(await page.getByRole('combobox').inputValue(),'2026-07');
 });
 await run('AllocationEditor exact residual allocation, numeric editing and zero-weight recovery',['editing','validation','keyboard','focus','recovery','financial-precision'],async()=>{
  await mount('AllocationEditor',{total:'0.05',lines:[{label:'Alpha',weight:1},{label:'Beta',weight:1},{label:'Gamma',weight:1}]});
  const input=page.getByRole('spinbutton',{name:'Alpha weight',exact:true});await input.focus();await input.fill('3');
  await eventually(async()=>assert.equal(await page.evaluate(()=>accountingEvents.at(-1)?.weights[0]),3));const first=await page.evaluate(()=>accountingEvents.at(-1));assert.equal(first.amounts.reduce((sum,value)=>sum+Math.round(value*100),0),5);
  await input.fill('-2');await eventually(async()=>assert.equal(await input.inputValue(),'0'));await input.fill('2');await eventually(async()=>assert.equal(await input.inputValue(),'2'));assert.match(await page.locator('tfoot').innerText(),/0\.05/);
 });
 await run('AllocationEditor empty and replaced-line-count recovery',['empty','recovery'],async()=>{
  await mount('AllocationEditor',{total:5,lines:[]});await page.getByText('No allocation lines',{exact:true}).waitFor();await mount('AllocationEditor',{total:5,lines:[{label:'Recovered',weight:1}]});assert.equal(await page.getByRole('spinbutton',{name:'Recovered weight',exact:true}).inputValue(),'1');
 });
 const tasks=[{id:'close:001',title:'Approve prices',date:'2026-08-01',owner:'Synthetic Reviewer',team:'Accounting',status:'blocked',blocker:'Missing source',due:'Aug 1 18:00 UTC'},{id:'close:002',title:'Reconcile ledger',date:'2026-08-02',owner:'Synthetic Controller',team:'Accounting',status:'waiting',dependsOn:['close:001']}];
 await run('CloseCalendar controlled views, keyboard selection and blocked provenance',['keyboard','focus','controlled-state','selection','blocked'],async()=>{
  await mount('CloseCalendar',{days:[{date:'2026-08-01'},{date:'2026-08-02'}],tasks,currentDate:'2026-08-02'});
  const blocked=page.getByRole('button',{name:'Approve prices',exact:false});await blocked.focus();await blocked.press('Enter');await selected(blocked,'data-selected');assert.equal((await page.evaluate(()=>accountingEvents.at(-1))).id,'close:001');assert.match(await blocked.innerText(),/Missing source/);
  await page.getByRole('button',{name:'Dependency',exact:true}).click();await selected(page.getByRole('button',{name:'Dependency',exact:true}),'aria-pressed');assert.match(await page.locator('#fixture').innerText(),/Reconcile ledger/);await page.getByRole('button',{name:'Owner',exact:true}).click();await selected(page.getByRole('button',{name:'Owner',exact:true}),'aria-pressed');
 });
 await run('CloseCalendar empty task set and source recovery',['empty','recovery'],async()=>{await mount('CloseCalendar',{days:[],tasks:[]});assert.match(await page.locator('#fixture').innerText(),/0 tasks/);await mount('CloseCalendar',{days:[{date:'2026-08-01'}],tasks});await page.getByRole('button',{name:'Approve prices',exact:false}).waitFor();});
 const income=[{id:'income:01',period:'June 2026',coupon:0,accretion:0.01,income:0.01,closingBook:10.01,state:'accrued'},{id:'income:02',period:'July 2026',coupon:0,accretion:0.02,income:0.02,closingBook:10.03,state:'scheduled'}];
 await run('IncomeSchedule keyboard commits exact period identity, controlled selection and EUR values',['keyboard','focus','controlled-state','selection','currency'],async()=>{
  await mount('IncomeSchedule',{rows:income,currency:'EUR',selectedId:'income:01'});const row=page.locator('tbody tr').nth(1);await row.focus();await row.press('Space');await selected(row);assert.equal(await page.evaluate(()=>accountingEvents.at(-1)),'income:02');assert.match(await row.innerText(),/€/);assert.equal(await row.locator('td').nth(1).innerText(),'—');
 });
 await run('IncomeSchedule missing amounts invalidate column totals and recover without treating them as zero',['missing','zero','error','empty','recovery'],async()=>{
  await mount('IncomeSchedule',{rows:[income[0],{...income[1],coupon:null,income:undefined,closingBook:undefined}],currency:'EUR'});assert.equal(await page.locator('tfoot td').nth(1).innerText(),'Missing');assert.equal(await page.locator('tfoot td').nth(3).innerText(),'Missing');assert.equal(await page.locator('tbody tr').nth(1).locator('td').nth(4).innerText(),'Missing');
  await mount('IncomeSchedule',{rows:[]});assert.equal(await page.locator('tbody tr').count(),0);assert.equal(await page.locator('tfoot').count(),0);await mount('IncomeSchedule',{rows:income},false);assert.equal(await page.locator('tbody tr[tabindex="0"]').count(),0);assert.doesNotMatch(await page.locator('tfoot').innerText(),/Missing/);
 });
 const stages=[{id:'trade',label:'Trade captured',state:'done'},{id:'settle',label:'Settlement expected',state:'blocked',meta:'Awaiting confirmation'}];
 await run('LifecycleRibbon keyboard selection announces state and static mode has no fake buttons',['keyboard','focus','controlled-state','blocked','read-only','empty','recovery'],async()=>{
  await mount('LifecycleRibbon',{stages,selectedId:'trade'});const stage=page.getByRole('button',{name:'Settlement expected',exact:false});await stage.focus();await stage.press('Enter');await selected(stage,'aria-pressed');assert.equal(await page.evaluate(()=>accountingEvents.at(-1)),'settle');assert.match(await stage.innerText(),/blocked/);
  await mount('LifecycleRibbon',{stages},false);assert.equal(await page.getByRole('button').count(),0);assert.equal(await page.getByRole('listitem').count(),2);await mount('LifecycleRibbon',{stages:[]});assert.equal(await page.getByRole('button').count(),0);await mount('LifecycleRibbon',{stages});assert.equal(await page.getByRole('button').count(),2);
 });
 const schedule=[{id:'factor:past',period:'June',date:'2026-06-01',expected:0,actual:0},{id:'factor:future',period:'August',date:'2026-08-01',expected:0.9,actual:null,actualDue:'Aug 2'}];
 await run('ScheduleLadder selection/trace preserve row identity and projected absence has no trace action',['keyboard','focus','controlled-state','selection','projected','missing','zero'],async()=>{
  await mount('ScheduleLadder',{rows:schedule,asOf:'2026-07-01',notation:'factor',properties:{amortizing:true},requires:'amortizing'});
  const selection=page.locator('tbody tr[data-state="within"] button.mds-var');await selection.focus();await selection.press('Enter');assert.equal((await page.evaluate(()=>accountingEvents.at(-1))).select.id,'factor:past');
  await page.getByRole('button',{name:'Trace',exact:true}).click();assert.equal((await page.evaluate(()=>accountingEvents.at(-1))).trace.id,'factor:past');assert.equal(await page.getByRole('button',{name:'Trace',exact:true}).count(),1);assert.match(await page.locator('tr[data-projected="true"]').innerText(),/Not yet delivered/);assert.match(await page.locator('#fixture').innerText(),/1 of 2 periods delivered/);
 });
 await run('ScheduleLadder applicability and empty/source recovery',['not-applicable','empty','recovery'],async()=>{
  await mount('ScheduleLadder',{rows:schedule,properties:{amortizing:false},requires:'amortizing'});assert.equal(await page.locator('section').count(),0);await mount('ScheduleLadder',{rows:schedule,properties:{amortizing:false},requires:'amortizing',hideWhenNotApplicable:false});assert.match(await page.locator('section').innerText(),/not applicable/);await mount('ScheduleLadder',{rows:[]});assert.equal(await page.locator('tbody tr').count(),0);await mount('ScheduleLadder',{rows:schedule});assert.equal(await page.getByRole('button',{name:'Trace',exact:true}).count(),1);
 });

 const agingRows=[{name:'Alpha',ref:'aging/001',amounts:[0,10]},{name:'Beta',ref:'aging/002',amounts:[5,0]}];
 await run('AgingTable keyboard inspection and controlled original-row identity',['keyboard','focus','controlled-state','selection','zero'],async()=>{
  await mount('AgingTable',{rows:agingRows,buckets:['Current','Late']});const row=page.locator('tbody tr').nth(1);await row.focus();await row.press('Enter');await eventually(async()=>assert.match(await row.getAttribute('class'),/agt__row--on/));const event=await page.evaluate(()=>accountingEvents.at(-1));assert.equal(event.index,1);assert.equal(event.row.ref,'aging/002');assert.match(await page.locator('tfoot').innerText(),/15\.00/);
 });
 await run('AgingTable missing bucket totals cannot certify partial zero; empty recovery',['missing','empty','recovery','read-only'],async()=>{
  await mount('AgingTable',{rows:[{name:'Unknown',amounts:[null,0]}],buckets:['Current','Late']});assert.match(await page.locator('tfoot').innerText(),/Missing|Indeterminate|unavailable/i);
  await mount('AgingTable',{rows:[]});await page.getByText('No open items',{exact:true}).waitFor();await mount('AgingTable',{rows:agingRows},false);assert.equal(await page.locator('tbody tr[tabindex="0"]').count(),0);
 });
 const certificationAccounts=[{code:'acct/001',name:'Cash',material:true},{code:'acct/002',name:'Investments',material:false}],certificationRecords={'acct/001':{signatures:[{stage:'certified',actor:'Synthetic Controller',at:'2026-07-01T00:00:00Z',balance:100}]}};
 await run('CertificationMatrix keyboard account opening and stale-balance signature evidence',['keyboard','focus','stale','recovery','selection'],async()=>{
  await mount('CertificationMatrix',{accounts:certificationAccounts,records:certificationRecords,balances:{'acct/001':100,'acct/002':0}});const row=page.locator('tbody tr').first();await row.focus();await row.press('Space');assert.equal((await page.evaluate(()=>accountingEvents.at(-1))).code,'acct/001');assert.match(await page.locator('.mds-cert__foot').innerText(),/1 of 2 accounts certified/);
  await mount('CertificationMatrix',{accounts:certificationAccounts,records:certificationRecords,balances:{'acct/001':101,'acct/002':0}});assert.match(await page.locator('.mds-cert__foot').innerText(),/void.*balance moved/);assert.match(await page.locator('.mds-cert__foot').innerText(),/0 of 2 accounts certified/);
 });
 await run('CertificationMatrix empty scope and read-only recovery',['empty','read-only','recovery'],async()=>{await mount('CertificationMatrix',{accounts:[],records:{},balances:{}});assert.match(await page.locator('.mds-cert__foot').innerText(),/0 of 0/);await mount('CertificationMatrix',{accounts:certificationAccounts,records:{},balances:{}},false);assert.equal(await page.locator('tbody tr[tabindex="0"]').count(),0);});
 const fxRows=[{currency:'EUR',account:'fx/001',local:100,bookedRate:1,currentRate:1.2},{currency:'JPY',account:'fx/002',local:0,bookedRate:0.006,currentRate:0.007}];
 await run('FxRevaluationTable keyboard inspection, controlled row and real zero',['keyboard','focus','controlled-state','selection','zero'],async()=>{
  await mount('FxRevaluationTable',{rows:fxRows});const row=page.locator('tbody tr').nth(1);await row.focus();await row.press('Space');await eventually(async()=>assert.match(await row.getAttribute('class'),/fxr__row--on/));const event=await page.evaluate(()=>accountingEvents.at(-1));assert.equal(event.index,1);assert.equal(event.row.account,'fx/002');assert.match(await page.locator('tfoot').innerText(),/20\.00/);
 });
 await run('FxRevaluationTable unavailable market rate invalidates totals, empty/source recovery',['missing','error','empty','recovery'],async()=>{
  await mount('FxRevaluationTable',{rows:[fxRows[0],{...fxRows[1],currentRate:null}]});assert.match(await page.locator('tfoot').innerText(),/Missing|Indeterminate|unavailable/i);await mount('FxRevaluationTable',{rows:[]});await page.getByText('No foreign-currency balances',{exact:true}).waitFor();await mount('FxRevaluationTable',{rows:fxRows},false);assert.equal(await page.locator('tbody tr[tabindex="0"]').count(),0);
 });
 const ledgerRows=[{date:'2026-06-01',ref:'JE/first',memo:'First',debit:10},{date:'2026-06-02',ref:'JE/second',memo:'Second',credit:2},{date:'2026-06-03',ref:'JE/void',memo:'Voided',credit:1000,status:'void'}];
 await run('LedgerTable keyboard sorting preserves original row and chronological balance; void math excluded',['keyboard','focus','controlled-state','selection','sort','void'],async()=>{
  await mount('LedgerTable',{rows:ledgerRows,opening:0});const date=page.getByRole('columnheader',{name:'Date',exact:false});await date.focus();await date.press('Enter');await date.press('Enter');await selected(date,'aria-sort','descending');const second=page.locator('tbody tr').filter({hasText:'JE/second'});await second.focus();await second.press('Enter');assert.equal((await page.evaluate(()=>accountingEvents.at(-1))).index,1);assert.match(await second.getAttribute('class'),/ldg__row--on/);assert.match(await second.innerText(),/8\.00/);assert.match(await page.locator('tfoot').innerText(),/void.*excluded/);assert.doesNotMatch(await page.locator('tfoot').innerText(),/1,000/);
 });
 await run('LedgerTable explicit unavailable figure prevents numeric proof; empty recovery',['missing','error','empty','recovery'],async()=>{
  await mount('LedgerTable',{rows:[{date:'2026-06-01',ref:'Unknown',debit:{value:null,condition:'missing',authority:'source'}}],opening:0});assert.match(await page.locator('tfoot').innerText(),/Missing|Indeterminate|unavailable/i);await mount('LedgerTable',{rows:[]});await page.getByText('No journal entries',{exact:true}).waitFor();await mount('LedgerTable',{rows:ledgerRows},false);assert.equal(await page.locator('tbody tr[tabindex="0"]').count(),0);
 });
 const postingLines=[{id:'line/001',account:'Cash',debit:10},{id:'line/002',account:'Capital',credit:10}];
 await run('PostingImpactPreview warning acknowledgment and authorized balanced-post callback',['keyboard','focus','validation','disabled','acknowledgment','recovery'],async()=>{
  await mount('PostingImpactPreview',{lines:postingLines,period:'June 2026',periodState:'soft-closed'});const post=page.getByRole('button',{name:'Post journal',exact:true});assert(await post.isDisabled());const ack=page.getByRole('checkbox');await ack.focus();await ack.press('Space');await eventually(async()=>assert.equal(await post.isDisabled(),false));await post.click();assert.deepEqual(await page.evaluate(()=>accountingEvents.at(-1)),{post:true});
 });
 await run('PostingImpactPreview empty/zero/unknown/locked/unauthorized entries cannot post',['empty','missing','error','disabled','locked','permissions','recovery'],async()=>{
  for(const props of [{lines:[]},{lines:[{account:'Cash',debit:0},{account:'Capital',credit:0}]},{lines:[{account:'Cash',debit:null},{account:'Capital',credit:null}]},{lines:postingLines,periodState:'locked'},{lines:postingLines,authority:false}]){await mount('PostingImpactPreview',props);assert(await page.getByRole('button',{name:'Post journal',exact:true}).isDisabled(),JSON.stringify(props));assert.equal(await page.evaluate(()=>accountingEvents.length),0);}
  await mount('PostingImpactPreview',{lines:postingLines,periodState:'open'});assert.equal(await page.getByRole('button',{name:'Post journal',exact:true}).isDisabled(),false);await page.getByRole('button',{name:'Cancel',exact:true}).click();assert.deepEqual(await page.evaluate(()=>accountingEvents.at(-1)),{cancel:true});
 });
 const movements=[{id:'movement/001',label:'Accretion',value:0.01,linkLabel:'Open exact journal'},{id:'movement/zero',label:'Principal',value:0}];
 await run('RollforwardTrace keyboard evidence drilldown and independent mismatch proof',['keyboard','focus','evidence','zero','error','recovery'],async()=>{
  await mount('RollforwardTrace',{opening:10,movements,closing:10.01});const link=page.getByRole('button',{name:'Open exact journal',exact:true});await link.focus();await link.press('Enter');assert.equal((await page.evaluate(()=>accountingEvents.at(-1))).movement,'movement/001');assert.match(await page.locator('.mds-rf__proof').innerText(),/Opening \+ movements = closing/);
  await mount('RollforwardTrace',{opening:10,movements,closing:10.03});assert.match(await page.locator('.mds-rf__proof').innerText(),/Out by.*0\.02/);
 });
 await run('RollforwardTrace unavailable movement cannot become a zero tie, empty recovery',['missing','error','empty','read-only','recovery'],async()=>{
  await mount('RollforwardTrace',{opening:10,movements:[{label:'Missing movement',value:null}],closing:10});assert.match(await page.locator('.mds-rf__proof').innerText(),/Missing|Indeterminate|unavailable/i);await mount('RollforwardTrace',{opening:0,movements:[],closing:0},false);assert.equal(await page.getByRole('button').count(),0);assert.match(await page.locator('.mds-rf__proof').innerText(),/0 movements/);
 });
 const lots=[{id:'lot/001',acquired:'2025-01-01',symbol:'SYN',quantity:1,costBasis:10,marketValue:12,proceeds:11},{id:'lot/002',acquired:'2026-06-01',symbol:'SYN',quantity:0,costBasis:0,marketValue:0,proceeds:0}];
 await run('TaxLotTable keyboard selection and as-of holding classification; realized mode uses proceeds',['keyboard','focus','controlled-state','selection','zero','as-of'],async()=>{
  await mount('TaxLotTable',{lots,asOf:'2026-07-01',showSymbol:true});const row=page.locator('tbody tr').nth(1);await row.focus();await row.press('Space');assert.equal((await page.evaluate(()=>accountingEvents.at(-1))).row.id,'lot/002');assert.match(await row.getAttribute('class'),/tlt__row--on/);assert.match(await page.locator('tbody tr').first().innerText(),/Long/);await mount('TaxLotTable',{lots,asOf:'2026-07-01',mode:'realized'});await page.getByRole('columnheader',{name:'Proceeds',exact:true}).waitFor();assert.match(await page.locator('tfoot').innerText(),/11\.00/);
 });
 await run('TaxLotTable missing market value prevents a numeric gain total, empty recovery',['missing','error','empty','recovery'],async()=>{
  await mount('TaxLotTable',{lots:[lots[0],{...lots[1],marketValue:null}],asOf:'2026-07-01'});assert.match(await page.locator('tfoot').innerText(),/Missing|Indeterminate|unavailable/i);await mount('TaxLotTable',{lots:[]});await page.getByText('No lots',{exact:true}).waitFor();await mount('TaxLotTable',{lots,asOf:'2026-07-01'},false);assert.equal(await page.locator('tbody tr[tabindex="0"]').count(),0);
 });
 const balances=[{code:'1000',account:'Cash',type:'asset',balance:10},{code:'3000',account:'Capital',type:'equity',balance:10}];
 await run('TrialBalance keyboard controlled account identity preserves grouped original index and balanced proof',['keyboard','focus','controlled-state','selection','proof'],async()=>{
  await mount('TrialBalance',{rows:balances});const row=page.locator('tbody tr').filter({hasText:'Capital'}).filter({has:page.locator('td')});await row.focus();await row.press('Enter');assert.equal((await page.evaluate(()=>accountingEvents.at(-1))).index,1);assert.equal((await page.evaluate(()=>accountingEvents.at(-1))).row.code,'3000');assert.match(await row.getAttribute('class'),/tbl__row--on/);assert.match(await page.locator('tfoot').innerText(),/10\.00/);
 });
 await run('TrialBalance missing whole balance cannot certify an all-zero book, empty recovery',['missing','error','empty','recovery'],async()=>{
  await mount('TrialBalance',{rows:[{code:'1000',account:'Unknown',type:'asset',balance:null}]});assert.match(await page.locator('tfoot').innerText(),/Missing|Indeterminate|unavailable/i);await mount('TrialBalance',{rows:[]});await page.getByText('No accounts',{exact:true}).waitFor();await mount('TrialBalance',{rows:balances},false);assert.equal(await page.locator('tbody tr[tabindex="0"]').count(),0);
 });
 assert.deepEqual(errors,[],'Accounting contract fixtures have no runtime exceptions');
 const report={schemaVersion:1,source:'Current source bundled in memory; local React and Chromium',cases,notApplicable:{loading:'These seven contracts have no remote loading prop; templates exercise DataStateSwitch loading/error/recovery.',disabled:'Only AccountingPeriodSelector declares disabled. Other components inspect blocked/locked records or omit callbacks for read-only mode; blocked evidence does not disable inspection.',validation:'Allocation weights normalize to non-negative values. Other components display caller-provided data and expose selection, not input validation.'},limitations:['Synthetic behavior only; no provider/ledger transport, native Windows/Edge, or assistive-technology certification.']};
 await writeFile('/tmp/pr3501-full-audit/accounting-interaction-results.json',JSON.stringify(report,null,2)+'\n');
 const failed=cases.filter(test=>test.status==='failed').length;console.log(`${failed?'FAIL':'PASS'} ${cases.length-failed}/${cases.length} accounting interaction contracts across ${names.length} exports`);if(failed)process.exitCode=1;
}finally{await browser.close();}
