#!/usr/bin/env node
// Source contracts retained while integrating the 4.3 export: keyboard identity and truthful readonly semantics.
import assert from 'node:assert/strict';
import { build } from 'esbuild';
import { chromium } from 'playwright';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const entry = `import React from 'react'; import {createRoot} from 'react-dom/client';
import {LedgerTable} from './components/accounting/LedgerTable.jsx';
import {OptionChainTable} from './components/trading/OptionChainTable.jsx';
import {CorrelationHeatmap} from './components/charts/CorrelationHeatmap.jsx';
import {CoverageMatrix} from './components/charts/CoverageMatrix.jsx';
import {VirtualizedList} from './components/data/VirtualizedList.jsx';
import {WorksheetGrid} from './components/data/WorksheetGrid.jsx';
window.fixture={React,createRoot,LedgerTable,OptionChainTable,CorrelationHeatmap,CoverageMatrix,VirtualizedList,WorksheetGrid};`;
const {outputFiles} = await build({absWorkingDir:root,stdin:{contents:entry,resolveDir:root,loader:'jsx'},bundle:true,write:false,format:'iife',platform:'browser',define:{'process.env.NODE_ENV':'"production"'},logLevel:'error'});
const browser = await chromium.launch({executablePath:process.env.MERIDIAN_CHROMIUM_PATH || '/usr/bin/chromium',args:['--no-sandbox']});
const page = await browser.newPage(); const errors = []; let assertions = 0;
page.on('pageerror', error => errors.push(error.message));
try {
  await page.setContent('<!doctype html><html lang="en"><title>Integrated data accessibility</title><main><h1>Synthetic data contracts</h1><div id="fixture"></div></main></html>');
  await page.addScriptTag({content:outputFiles[0].text});
  await page.evaluate(() => {
    const {React,createRoot,...components} = window.fixture, h = React.createElement;
    const app = createRoot(document.getElementById('fixture')); let revision = 0;
    window.mountFixture = (kind, config = {}) => {
      window.events = []; window.childEvents = 0; const sequence = ++revision; let props;
      if (kind === 'LedgerTable') {
        window.originalRows = [
          {date:'2026-01-01',ref:'A',memo:config.nested ? h('button',{type:'button',onClick:()=>window.childEvents++},'Inspect source') : 'First',debit:2},
          {date:'2026-01-03',ref:'B',memo:'Second',credit:1},
          {date:'2026-01-02',ref:'C',memo:'Third',debit:3}
        ];
        props = {rows:window.originalRows,opening:0,selectedIndex:1,onRowClick:(row,index)=>window.events.push({ref:row.ref,index,exact:row===window.originalRows[index]})};
      } else if (kind === 'OptionChainTable') {
        window.originalRows = [{strike:110,call:{bid:2},put:{bid:3}},{strike:90,call:{bid:4},put:{bid:5}}];
        props = {rows:window.originalRows,columns:['bid'],underlyingPrice:100,...(config.readOnly ? {} : {onSelect:row=>window.events.push({strike:row.strike,exact:window.originalRows.includes(row)})})};
      } else if (kind === 'CorrelationHeatmap') props = {labels:['A','B'],matrix:[[1,-0.5],[null,1]],showValues:false};
      else if (kind === 'CoverageMatrix') props = {rows:['Synthetic'],cols:['Day'],data:{Synthetic:{Day:'partial'}},...(config.readOnly ? {} : {onCellClick:row=>window.events.push(row.id)})};
      else if (kind === 'VirtualizedList') props = {items:Array.from({length:1000},(_,i)=>'Row '+i),rowHeight:20,height:100,overscan:1};
      else props = {columns:['A'],rows:1,cells:{A1:{value:0}},editable:!!config.editable};
      app.render(h('div',{'data-sequence':sequence},h(components[kind],props))); return sequence;
    };
  });
  async function mount(kind,config={}) {const sequence=await page.evaluate(({kind,config})=>window.mountFixture(kind,config),{kind,config});await page.locator(`[data-sequence="${sequence}"]`).waitFor();}

  await mount('LedgerTable');
  const rows=page.locator('tbody tr.ldg__row--click');
  assert.equal(await rows.evaluateAll(nodes=>nodes.filter(node=>node.tabIndex===0).length),1);
  await rows.nth(1).focus(); await page.keyboard.press('ArrowDown');
  assert(await rows.nth(2).evaluate(node=>node===document.activeElement));
  await page.keyboard.press('Home'); assert(await rows.first().evaluate(node=>node===document.activeElement));
  await page.keyboard.press('End'); await page.keyboard.press('Enter');
  assert.deepEqual(await page.evaluate(()=>window.events),[{ref:'C',index:2,exact:true}]);
  assert.match(await page.locator('tfoot').innerText(),/5\.00/);assert.match(await page.locator('tfoot').innerText(),/1\.00/);
  const date=page.getByRole('columnheader',{name:/^Date/});await date.focus();await date.press('Enter');await date.press('Enter');
  assert(await date.evaluate(node=>node===document.activeElement));
  assert.deepEqual(await rows.locator('.ldg__ref').allTextContents(),['B','C','A']);
  await rows.first().focus();await page.keyboard.press('Enter');
  assert.deepEqual((await page.evaluate(()=>window.events)).at(-1),{ref:'B',index:1,exact:true}); assertions++;

  await mount('LedgerTable',{nested:true});
  const child=page.getByRole('button',{name:'Inspect source'});await child.focus();await child.press('Enter');await child.press('Space');await child.click();
  assert.equal(await page.evaluate(()=>window.childEvents),3);assert.deepEqual(await page.evaluate(()=>window.events),[]);assert(await child.evaluate(node=>node===document.activeElement));assertions++;

  await mount('OptionChainTable');
  const grid=page.getByRole('grid',{name:'Option chain'});await grid.focus();await grid.press('End');
  const active=await grid.getAttribute('aria-activedescendant');assert(active);assert.match(await page.locator(`[id="${active}"]`).innerText(),/110\.00/);
  await grid.press('Space');assert.deepEqual(await page.evaluate(()=>window.events),[{strike:110,exact:true}]);
  await mount('OptionChainTable',{readOnly:true});assert.equal(await page.getByRole('grid').count(),0);assert.equal(await page.getByRole('table',{name:'Option chain'}).getByRole('row').count(),5);assert.equal(await page.getByRole('cell').count(),7);assertions++;

  await mount('CorrelationHeatmap');
  const correlation=page.getByRole('table',{name:'Correlation matrix'});assert.equal(await correlation.getByRole('row').count(),3);
  const correlationCells=correlation.getByRole('cell');assert.equal(await correlationCells.count(),4);assert.equal(await correlationCells.nth(1).getAttribute('aria-label'),'A · B = -0.50');assert.equal(await correlationCells.nth(2).getAttribute('aria-label'),'B · A = —');assert.deepEqual(await correlationCells.allTextContents(),['','','','']);assertions++;

  await mount('CoverageMatrix',{readOnly:true});assert.equal(await page.getByRole('grid').count(),0);assert.equal(await page.getByRole('table',{name:'Coverage matrix'}).getByRole('cell').count(),1);assert.equal(await page.getByRole('button').count(),0);
  await mount('CoverageMatrix');const cell=page.getByRole('gridcell');await cell.focus();await cell.press('Space');assert.deepEqual(await page.evaluate(()=>window.events),['Synthetic']);assertions++;

  await mount('VirtualizedList');
  const region=page.getByRole('region',{name:'Virtualized list'});await region.focus();assert(await region.evaluate(node=>node===document.activeElement));
  const items=page.getByRole('list',{name:'Virtualized list'}).getByRole('listitem');assert.equal(await items.first().getAttribute('aria-setsize'),'1000');assert.equal(await items.first().getAttribute('aria-posinset'),'1');
  await region.evaluate(node=>{node.scrollTop=node.scrollHeight;node.dispatchEvent(new Event('scroll',{bubbles:true}));});await page.getByText('Row 999',{exact:true}).waitFor();assert.equal(await items.last().getAttribute('aria-posinset'),'1000');assertions++;

  await mount('WorksheetGrid');assert.equal(await page.getByRole('grid').getAttribute('aria-readonly'),'true');await mount('WorksheetGrid',{editable:true});assert.equal(await page.getByRole('grid').getAttribute('aria-readonly'),'false');assertions++;
  assert.deepEqual(errors,[]);console.log(`PASS ${assertions}/${assertions} integrated data/finance accessibility contracts`);
} finally {await browser.close();}
