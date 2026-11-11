#!/usr/bin/env node
// Behavioral proof for governance repairs; uses production source without regenerating the export.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';
import { build } from 'esbuild';
import { chromium } from 'playwright';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const manifest = JSON.parse(await readFile(resolve(root, '_ds_manifest.json'), 'utf8'));
const entry = `import React from 'react'; import { createRoot } from 'react-dom/client';
window.React = React; window.ReactDOM = { createRoot };\n` + manifest.components.map((e,i)=>`import { ${e.name} as symbol${i} } from './${e.sourcePath}';`).join('\n') + `\nwindow.${manifest.namespace} = {` + manifest.components.map((e,i)=>`${e.name}: symbol${i}`).join(',') + `};\nimport * as LegacyModal from './components/core/Modal.jsx'; window.LegacyModal=LegacyModal;`;
const source = await build({absWorkingDir:root,stdin:{contents:entry,resolveDir:root,loader:'jsx'},bundle:true,write:false,format:'iife',platform:'browser',resolveExtensions:['.js','.jsx','.ts','.tsx','.json'],define:{'process.env.NODE_ENV':'"production"'},logLevel:'error'});
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({...(executablePath?{executablePath}:{}),args:['--no-sandbox']});
const page = await browser.newPage({viewport:{width:1366,height:768}});
const exceptions=[]; page.on('pageerror',error=>exceptions.push(error.message));
try {
  await page.setContent('<!doctype html><html lang="en"><head><title>Governance source fixtures</title></head><body><div id="fixture"></div></body></html>');
  await page.addScriptTag({content:source.outputFiles[0].text});
  await page.evaluate(()=>{window.fixtureRoot=ReactDOM.createRoot(document.getElementById('fixture'));window.renderFixture=node=>fixtureRoot.render(node);});
  await page.evaluate(()=>{const {Modal,ModalHeader,ModalBody}=LegacyModal;renderFixture(React.createElement(React.Fragment,null,...['First','Second'].map(title=>React.createElement(Modal,{key:title,open:true},React.createElement(ModalHeader,{title}),React.createElement(ModalBody,null,title+' body')))))});
  await page.getByRole('dialog',{name:'Second'}).waitFor();
  const labelled = await page.getByRole('dialog').evaluateAll(nodes=>nodes.map(node=>node.getAttribute('aria-labelledby')));
  assert.equal(new Set(labelled).size,2,'Concurrent legacy Modal instances have distinct names');
  assert.equal(await page.getByRole('button',{name:'Close',exact:true}).first().getAttribute('type'),'button');

  // The compatibility helper must preserve the requested object and explicit query/hash.
  const compatibility = await readFile(resolve(root,'templates/_scaffold/compatibility-frame.jsx'),'utf8');
  const compiledCompatibility = await build({stdin:{contents:compatibility,loader:'jsx'},write:false,format:'iife',jsxFactory:'window.React.createElement',logLevel:'error'});
  await page.addScriptTag({content:compiledCompatibility.outputFiles[0].text});
  const target = await page.evaluate(()=>{
    const fakeLocation={href:'https://synthetic.invalid/templates/backtest-builder/BacktestBuilder.dc.html?runId=retained&theme=dark#evidence',origin:'https://synthetic.invalid'};
    // Hooks are exercised by rendering below; URL behavior is tested by the same helper source under a separate VM as well.
    return fakeLocation;
  });
  const helperContext={window:{location:target},React:{useMemo:fn=>fn(),createElement:(type,props)=>({type,props})},URL,Object,Error};helperContext.window.React=helperContext.React;
  vm.runInNewContext(compiledCompatibility.outputFiles[0].text,helperContext);
  const frame = helperContext.window.MeridianTemplateCompatibility({target:'../strategy-runs/StrategyRuns.dc.html?view=backtest',title:'Backtest canonical workstation',theme:'light',density:'compact',environment:'FIXTURE'});
  const url=new URL(frame.props.src);assert.equal(url.searchParams.get('runId'),'retained');assert.equal(url.searchParams.get('view'),'backtest');assert.equal(url.searchParams.get('theme'),'dark');assert.equal(url.hash,'#evidence');assert.equal(frame.props.title,'Backtest canonical workstation');

  await page.addScriptTag({content:await readFile(resolve(root,'templates/family-office/screen.jsx'),'utf8').then(async text=>(await build({stdin:{contents:text,loader:'jsx'},write:false,format:'iife',jsxFactory:'window.React.createElement',logLevel:'error'})).outputFiles[0].text)});
  await page.evaluate(()=>renderFixture(React.createElement(window.FamilyOfficeScreen,{environment:'FIXTURE'})));
  await page.getByRole('heading',{name:'Whitfield Family Office',exact:true}).waitFor();
  await page.getByRole('button',{name:'Evidence binder',exact:true}).click();
  await page.getByRole('region',{name:'Example evidence binder',exact:true}).waitFor();
  await page.getByRole('button',{name:'Record example review',exact:true}).click();
  assert.match(await page.getByRole('status').filter({hasText:'Synthetic review noted'}).innerText(),/no review was submitted/);
  assert.equal(await page.getByRole('region',{name:'Example family accounts',exact:true}).count(),1);
  assert.equal(await page.getByRole('region',{name:'Example capital activity',exact:true}).count(),1);

  await page.addScriptTag({content:await readFile(resolve(root,'templates/split-pane-reconciliation/screen.jsx'),'utf8').then(async text=>(await build({stdin:{contents:text,loader:'jsx'},write:false,format:'iife',jsxFactory:'window.React.createElement',logLevel:'error'})).outputFiles[0].text)});
  await page.evaluate(()=>renderFixture(React.createElement(window.SplitPaneReconciliationScreen,{environment:'FIXTURE'})));
  await page.getByRole('heading',{name:'Reconciliation',exact:true}).waitFor();
  await page.getByRole('combobox',{name:'Reconciliation period',exact:true}).waitFor();
  const row=page.locator('.spr-table tbody tr[tabindex="0"]').first();await row.focus();await row.press('ArrowDown');
  assert.equal(await page.locator('.spr-table tbody tr').nth(1).evaluate(n=>n===document.activeElement),true,'Arrow keys move statement-row focus');
  const breakRow=page.locator('.spr-table tbody tr.break').first();await breakRow.focus();await breakRow.press('Enter');
  const dialog=page.getByRole('dialog',{name:'Break details',exact:true});await dialog.waitFor();
  assert(await dialog.getByRole('button',{name:'Close',exact:true}).evaluate(n=>n===document.activeElement));
  await page.keyboard.press('Escape');await dialog.waitFor({state:'hidden'});
  assert(await breakRow.evaluate(n=>n===document.activeElement),'Closing the drawer restores selected-row focus');

  const ledgerView=await readFile(resolve(root,'templates/accounting-workstation/views/LedgerView.jsx'),'utf8');
  const ledgerCompiled=await build({stdin:{contents:ledgerView,loader:'jsx'},write:false,format:'iife',jsxFactory:'window.React.createElement',logLevel:'error'});
  await page.addScriptTag({content:ledgerCompiled.outputFiles[0].text});
  await page.evaluate(()=>{
    window.ledgerExportOriginal={create:URL.createObjectURL,revoke:URL.revokeObjectURL,click:HTMLAnchorElement.prototype.click};
    URL.createObjectURL=blob=>{window.ledgerExportBlob=blob;return 'blob:synthetic-ledger';};
    URL.revokeObjectURL=()=>{};HTMLAnchorElement.prototype.click=function(){window.ledgerExportName=this.download;};
    renderFixture(React.createElement(window.AcctLedgerView,{rows:[{date:'2026-06-01',ref:'JE:exact/0001',account:'1100 · Cash',memo:'wire, "quoted"\nsecond line',debit:0,credit:null}],lastRef:'JE:exact/0001'}));
  });
  await page.getByRole('button',{name:'Export CSV',exact:true}).click();
  const csv=await page.evaluate(()=>ledgerExportBlob.text());
  assert.match(csv,/"JE:exact\/0001"/);assert.match(csv,/"wire, ""quoted""\nsecond line"/);assert.match(csv,/,"0","","posted"/,'CSV keeps real zero distinct from missing credit');
  assert.equal(await page.evaluate(()=>ledgerExportName),'general-ledger-2026-06.csv');
  await page.evaluate(()=>{URL.createObjectURL=ledgerExportOriginal.create;URL.revokeObjectURL=ledgerExportOriginal.revoke;HTMLAnchorElement.prototype.click=ledgerExportOriginal.click;});
  assert.deepEqual(exceptions,[],'No template source runtime errors');

  const prefsCode=await readFile(resolve(root,'templates/_scaffold/prefs.js'),'utf8');
  const saved = {}, attributes = {};
  const preferenceContext = { window: {
    location: { href: 'https://synthetic.invalid/example?theme=dark&density=compact&environment=FIXTURE' },
    localStorage: { getItem: key => saved[key] || null, setItem: (key,value) => { saved[key] = value; } },
    document: {
      documentElement: { setAttribute: (key,value) => { attributes[key] = value; } },
      body: { setAttribute: (key,value) => { attributes[key] = value; } }
    }
  }, URL };
  vm.runInNewContext(prefsCode,preferenceContext);
  assert.equal(preferenceContext.window.MeridianPrefs.theme('light'),'dark');
  preferenceContext.window.MeridianPrefs.set('theme','light');
  assert.equal(preferenceContext.window.MeridianPrefs.theme('dark'),'light','Stored operator preference outranks the compatibility URL seed');

  const downloadCode=await readFile(resolve(root,'templates/_scaffold/template-actions.js'),'utf8');
  const downloads=[], releases=[], scheduled=[];
  const exportContext={Blob,JSON,setTimeout:fn=>scheduled.push(fn),URL:{createObjectURL:blob=>{downloads.push({blob});return 'blob:synthetic';},revokeObjectURL:url=>releases.push(url)},document:{body:{appendChild:node=>{downloads.at(-1).node=node;}},createElement:()=>({click(){this.clicked=true;},remove(){this.removed=true;}})},window:{}};
  vm.runInNewContext(downloadCode,exportContext);
  const exportValues={missing:null,zero:0,exactAmount:'9007199254740993.01',cancellationId:'job/retained:0001'};
  exportContext.window.MeridianTemplateActions.downloadJson('retained-example.json',exportValues);
  assert.deepEqual(JSON.parse(await downloads[0].blob.text()),{synthetic:true,data:exportValues},'Read-only export preserves missing, zero, financial precision and exact identifiers');
  assert.equal(downloads[0].node.download,'retained-example.json');
  assert(downloads[0].node.clicked && downloads[0].node.removed);
  assert.equal(releases.length,0);scheduled.forEach(fn=>fn());assert.deepEqual(releases,['blob:synthetic'],'Download object URL is eventually released');

  const tieOutHtml=await readFile(resolve(root,'templates/close-management/CloseTieOut.dc.html'),'utf8');
  const tieOutScript=tieOutHtml.match(/<script type="text\/x-dc"[^>]*>([\s\S]*?)<\/script>/)[1];
  const tieOutContext={window:{MeridianDesignSystem_205d17:{ValueEnvelope:{check:()=>[]}}},document:{documentElement:{setAttribute(){}},body:{setAttribute(){}}},DCLogic:class{constructor(){this.props={};}setState(update){this.state={...this.state,...update};}},Number,Math,NaN};
  vm.runInNewContext(tieOutScript+'\nglobalThis.TieOutExample=Component;',tieOutContext);
  const tieOut=new tieOutContext.TieOutExample(),sourceRows=tieOut.rows.bind(tieOut);
  assert.equal(tieOut.renderVals().signOffBlocked,false,'A complete balanced synthetic book with coherent envelopes can demonstrate the local disposition');
  tieOut.props.forceOutOfBalance=true;
  let disposition=tieOut.renderVals();assert.equal(disposition.signOffBlocked,true);disposition.onSignOff();assert.equal(tieOut.state.disposition,null,'Out-of-balance input cannot sign off even when envelopes are coherent');
  tieOut.props.forceOutOfBalance=false;tieOut.rows=()=>[{debit:null,credit:0}];
  assert.equal(tieOut.renderVals().signOffBlocked,true,'Missing amount is not a zero balance');
  tieOut.rows=sourceRows;
  delete tieOutContext.window.MeridianDesignSystem_205d17.ValueEnvelope;
  assert.equal(tieOut.renderVals().signOffBlocked,true,'Unavailable coherence checker cannot authorize sign-off');
  tieOut.renderVals().onReturn();assert.match(tieOut.state.disposition,/preview only/);
  console.log('PASS governance sources: unique Modal names, compatibility query/hash, family evidence/review/tables, reconciliation keyboard/dialog, preference precedence');
} finally {await browser.close();}
