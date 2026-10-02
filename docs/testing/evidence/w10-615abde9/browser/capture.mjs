// Reproducible rendered-response simulation. This does not provision accounting data,
// execute governed server commands, or record human operator acceptance.
import { createRequire } from 'node:module';
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import os from 'node:os';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
const out = path.dirname(fileURLToPath(import.meta.url));
const rootArgument = process.argv.includes('--root') ? process.argv[process.argv.indexOf('--root') + 1] : null;
let repo = path.resolve(rootArgument ?? process.env.W10_REPO_ROOT ?? out);
if (!rootArgument && !process.env.W10_REPO_ROOT) {
  while (true) {
    try { await fs.access(path.join(repo,'src/Meridian.Ui/dashboard/package.json')); break; }
    catch { const parent=path.dirname(repo);if(parent===repo)throw new Error('Set W10_REPO_ROOT or pass --root with the candidate checkout path.');repo=parent; }
  }
}
const require = createRequire(path.join(repo,'src/Meridian.Ui/dashboard/package.json'));
const { chromium } = require('playwright');
const origin = process.env.W10_BROWSER_URL ?? 'http://127.0.0.1:4173';
const base = `${origin}/workstation`;
const candidate = '615abde90001ab33bd6e58e545edc7fce635e254';
const sessionStartUtc = new Date().toISOString();
const sourceHead=execFileSync('git',['-C',repo,'rev-parse','HEAD'],{encoding:'utf8'}).trim();
if(sourceHead!==candidate)throw new Error(`Candidate mismatch: expected ${candidate}, found ${sourceHead}. Run against an isolated candidate checkout using --root or W10_REPO_ROOT.`);
const screenshotFixtures = JSON.parse(await fs.readFile(path.join(repo,'scripts/dev/web-screenshot-fixtures.json'),'utf8')).routes;
const browser = await chromium.launch({executablePath:process.env.W10_CHROMIUM ?? '/usr/bin/chromium',headless:true,args:['--no-sandbox']});
const page = await browser.newPage({viewport:{width:1440,height:900}});
const consoleMessages=[]; const requests=[]; const cases=[];let phase='fixture-helper-preflight';
page.on('console',m=>{if(['warning','error'].includes(m.type()))consoleMessages.push({atUtc:new Date().toISOString(),phase,type:m.type(),message:m.text()});});
page.on('pageerror',e=>consoleMessages.push({atUtc:new Date().toISOString(),phase,type:'pageerror',message:e.message}));
page.on('response',r=>{if(r.status()>=400)consoleMessages.push({atUtc:new Date().toISOString(),phase,type:'http-error',status:r.status(),url:r.url()});});
await fs.mkdir(path.join(out,'evidence/screenshots'),{recursive:true});
// Load the maintained TypeScript helpers through Vite; no copied whole application.
await page.goto(`${base}/`);
const fixtureData=await page.evaluate(async()=>{
  const {resolveDevFixture}=await import('/workstation/src/lib/dev-fixtures.ts');
  const {sharedCloseDecision}=await import('/workstation/src/screens/operations-continuity-screen.close-test-fixtures.ts');
  const {WORKSTATION_API_ENDPOINTS}=await import('/workstation/src/lib/workstation-endpoints.ts');
  const paths=Object.values(WORKSTATION_API_ENDPOINTS).filter(x=>typeof x==='string');
  const routes=Object.fromEntries(paths.map(x=>[x,resolveDevFixture(x)]).filter(([,v])=>v!==undefined));
  const workflows=routes[WORKSTATION_API_ENDPOINTS.operationsContinuity];
  const workflow=resolveDevFixture(`${WORKSTATION_API_ENDPOINTS.operationsContinuity}/${workflows[0].workflowId}`);
  return {routes,workflow,decision:sharedCloseDecision(workflow)};
});
const continuity='/api/workstation/operations/continuity';
const commandCenter='/api/workstation/operations/financial-operations-command-center';
const markPreview='/api/ledger/journal-automation/daily-mark-to-market-preview';
const scheduleEndpoint='/api/ledger/journal-automation/daily-mark-to-market-schedules';
const scope=fixtureData.decision.closeReadiness.scope;
const query=()=>new URLSearchParams(scope).toString();
const clone=x=>structuredClone(x);
const reportEvidence={evidenceId:'simulation-report-pack-evidence',label:'Simulated report manifest',route:'/workstation/reporting/evidence',source:'response-simulation',capturedAtUtc:'2026-09-04T12:00:00Z'};
const readyWorkflow=clone(fixtureData.workflow);
Object.assign(readyWorkflow,{status:'ReadyForClose',brokerIntakeState:'Complete',securityMasterState:'Complete',ledgerPostingState:'Complete',reconciliationState:'Complete',approvalState:'Approved',closePackage:null,
  closeReadiness:{isReadyToClose:true,severity:'Info',score:100,components:[],blockers:[],nextActions:[]},
  reportPackReadiness:{isReady:true,reportPackId:'simulation-report-pack',blockingReason:null,evidenceLinks:[reportEvidence]}});
readyWorkflow.gates=readyWorkflow.gates.map(g=>({...g,status:'Passed',blockers:[],nextActions:[],completedAtUtc:'2026-09-04T12:00:00Z',completedBy:'simulated-controller'}));
readyWorkflow.breakCases=readyWorkflow.breakCases.map(b=>({...b,status:'Resolved'}));
readyWorkflow.closeChecklist=readyWorkflow.closeChecklist.map(t=>({...t,status:'Done',blockingReason:null,evidencePointer:reportEvidence.evidenceId,requiredApprovalCount:1,canAcknowledge:false,acknowledgedAtUtc:'2026-09-04T12:00:00Z',acknowledgedBy:'simulated-controller'}));
readyWorkflow.approvals=[{approvalId:'simulation-approval',status:'Approved',operator:'simulated-maker',reviewer:'simulated-checker',submittedAtUtc:'2026-09-04T11:00:00Z',decidedAtUtc:'2026-09-04T12:00:00Z',evidenceLinks:[reportEvidence]}];
if(readyWorkflow.dashboardSummary) Object.assign(readyWorkflow.dashboardSummary,{status:'Ready',isReady:true,stage:'Produce Evidence',readyMetricCount:6,totalMetricCount:6,requiredActions:[],metrics:readyWorkflow.dashboardSummary.metrics.map(m=>({...m,status:'Ready',requiredActions:[],evidenceLinks:[reportEvidence]}))});
const readyDecision={...clone(fixtureData.decision),activeWorkflow:readyWorkflow};
readyDecision.closeReadiness.contributors=[{contributorId:'report-pack',owner:'Reporting',status:'Current',evaluatedAtUtc:'2026-09-04T12:00:00Z',recordIds:[reportEvidence.evidenceId]}];
const schedule={scheduleId:'daily-1',fundProfileId:scope.fundProfileId,ledgerBookId:scope.ledgerBookId,entityId:scope.entityId,periodId:scope.periodId,currency:'USD',actor:'simulated-operator',policyId:'policy-1',policyName:'Simulated mark policy',nextRunAtUtc:'2026-09-04T00:00:00Z',positions:[],valuationMethod:'MarkToMarket',policyApprovedBy:'simulated-controller',policyApprovedAtUtc:'2026-09-03T12:00:00Z',reason:'Response simulation',maximumMarkAgeDays:3,minimumConfidence:'Medium',requireCompleteCoverage:true};
const current={symbol:'AAPL',securityId:'apple-security',financialAccountId:'account-1',valuationDate:'2026-09-04',observedOn:'2026-09-04',ageDays:0,policyVersion:'policy-1',status:'Current',blockReason:null};
let state={label:'setup',decision:clone(readyDecision),workflow:clone(readyWorkflow),schedules:[clone(schedule)],mark:{policyVersion:'policy-1',assessedPositionCount:1,blockedPositionCount:0,affectedValuationCount:0,evaluatedAtUtc:'2026-09-04T00:00:00Z',positions:[clone(current)]},markFailure:false,contributorFailure:false,closeRefusal:false,delayCloseMs:0,delayMarkMs:0};
await page.route('**/api/**',async route=>{
 const request=route.request();const u=new URL(request.url());const pathname=u.pathname;let body,status=200,source='maintained-development-fixture';
 const captured=clone(state);
 if(pathname.startsWith('/api/workstation/first-run/')){body=clone(screenshotFixtures['/api/workstation/first-run/']);body.workspace.safetyMessage='RESPONSE SIMULATION — no provisioned accounting host or human approval';source='maintained-screenshot-fixture';}
 else if(pathname===commandCenter){body=captured.decision;source='derived-sharedCloseDecision';if(captured.contributorFailure){status=503;body={message:'Simulated contributor service unavailable.'};}if(captured.delayCloseMs)await new Promise(r=>setTimeout(r,captured.delayCloseMs));}
 else if(pathname===continuity){body=fixtureData.routes[continuity].map((w,i)=>i? w:{...w,status:captured.workflow.status,version:captured.workflow.version});}
 else if(pathname===`${continuity}/${readyWorkflow.workflowId}/close`){source='simulated-command-refusal';status=409;body={message:captured.closeRefusal?'Simulated changed evidence/version refusal. Rebuild report support and review again.':'No human close command is authorized by this simulation.'};}
 else if(pathname===`${continuity}/${readyWorkflow.workflowId}`){body=captured.workflow;source='derived-maintained-workflow';}
 else if(pathname===scheduleEndpoint){body=captured.schedules;source='unit-contract-derived-schedule';}
 else if(pathname===markPreview){body=captured.mark;source='unit-contract-derived-mark-preview';if(captured.markFailure){status=503;body={message:'Simulated historical mark source unavailable.'};}if(captured.delayMarkMs)await new Promise(r=>setTimeout(r,captured.delayMarkMs));}
 else if(fixtureData.routes[pathname]!==undefined)body=fixtureData.routes[pathname];
 else if(screenshotFixtures[pathname]!==undefined)body=screenshotFixtures[pathname];
 else{const prefix=Object.keys(screenshotFixtures).find(k=>pathname.startsWith(k.endsWith('/')?k:`${k}/`));if(prefix)body=screenshotFixtures[prefix];else return route.continue();}
 const record={atUtc:new Date().toISOString(),scenario:captured.label,actor:'automation; not designated human operator',method:request.method(),url:request.url(),requestBody:request.postDataJSON?.()??null,status,responseBody:body,source,responseOrigin:'Playwright route.fulfill; no application service persistence'};
 requests.push(record);
 try{await route.fulfill({status,contentType:'application/json',headers:{'x-meridian-dev-fixture':'true','cache-control':'no-store'},body:JSON.stringify(body)});}catch(e){record.deliveryError=e.message;}
});
await page.addInitScript(()=>{localStorage.setItem('meridian.workstation.onboarding.dismissed','true');});
async function navigate(route,sc=scope){
 await page.goto(`${base}${route}?${new URLSearchParams(sc)}`);
 if(route.endsWith('/operations-continuity'))await page.getByRole('region',{name:'Operations continuity control strip'}).waitFor();
 else await page.getByRole('heading',{name:'Close Cockpit',exact:true}).waitFor();
 await page.waitForTimeout(700);
}
async function screenshot(name,target=null){
 if(target)await target.scrollIntoViewIfNeeded();
 await page.evaluate(({candidate})=>{
  let badge=document.getElementById('acceptance-simulation-provenance');
  if(!badge){badge=document.createElement('aside');badge.id='acceptance-simulation-provenance';document.body.append(badge);}
  const q=new URLSearchParams(location.search);
  badge.style.cssText='position:fixed;bottom:8px;right:12px;left:236px;z-index:2147483647;background:#111827;color:#fff;border:2px solid #f59e0b;padding:8px 12px;font:12px/1.4 monospace;pointer-events:none;white-space:pre-wrap';
  badge.textContent=`SIMULATED RESPONSE | candidate ${candidate.slice(0,8)} | operator decision PENDING\nSelected query scope (fixture identities; not provisioned): ${['fundProfileId','ledgerBookId','fundAccountId','entityId','periodId'].map(k=>`${k}=${q.get(k)??'MISSING'}`).join(' | ')}`;
 },{candidate});
 await page.screenshot({path:path.join(out,`evidence/screenshots/${name}.png`),fullPage:false});return `evidence/screenshots/${name}.png`;
}
async function record(id,expected,action,focus=null){
 const text=await page.locator('body').innerText();const close=page.getByRole('button',{name:`Publish close package for ${readyWorkflow.periodId}`,exact:true});
 const closeButtonDisabled=await close.count()?await close.isDisabled():null;
 const expectsDisabled=/^S2-|^S3-|^S8-delayed|^S4-missing$|^S5-stale$|^S6-unavailable$/.test(id);
 const expectsEnabled=/^S1-|^S4-missing-repair$|^S5-stale-repair$|^S6-repair$|^S7-repair$|^S8-repair$/.test(id);
 const decision=expectsDisabled?closeButtonDisabled===true? 'pass':'fail':expectsEnabled?closeButtonDisabled===false?'pass':'fail':'pass';
 let contextScreenshot=null;
 if(/^S4-|^S5-|^S7-/.test(id)&&page.url().includes('/operations-continuity')){await page.getByRole('region',{name:'Operations continuity control strip'}).scrollIntoViewIfNeeded();contextScreenshot=await screenshot(`${id}-context`);}
 cases.push({id,atUtc:new Date().toISOString(),candidate,url:page.url(),viewport:page.viewportSize(),title:await page.title(),provenance:'Rendered production components with simulated API responses; no live accounting host',expected,action,observed:{bodyText:text,closeButtonCount:await close.count(),closeButtonDisabled,frameworkOverlay:await page.locator('vite-error-overlay').count()},contextScreenshot,screenshot:await screenshot(id,focus),automationDecision:decision,operatorDecision:'pending',operatorIdentity:null});
 console.log(id);
}
try{
 phase='rendered-response-simulation';
 await navigate('/accounting/operations-continuity');
 await page.getByRole('button',{name:`Publish close package for ${readyWorkflow.periodId}`,exact:true}).waitFor();
 await record('S1-current-scope','Matching shared scope/workflow ready posture','Open maintained Operations Continuity screen with complete scope',page.getByRole('button',{name:`Publish close package for ${readyWorkflow.periodId}`,exact:true}));
 for(const [id,type,message] of [['S4-missing','Missing','Missing report-pack contributor; Reporting must rebuild support.'],['S5-stale','Stale','Stale report-pack contributor; Reporting must refresh support.']]){
  state.label=id;state.decision=clone(readyDecision);Object.assign(state.decision.closeReadiness,{status:'Blocked',isComplete:false,isReadyToClose:false,blockers:[{code:`report-pack-${type.toLowerCase()}`,contributorId:'report-pack',type,count:1,severity:'Critical',owner:'Reporting',message,recordIds:[reportEvidence.evidenceId]}]});
  await page.getByRole('button',{name:'Refresh operations continuity workflows'}).click();await page.getByText(message,{exact:false}).first().waitFor();
  await record(id,'Close blocked with contributor repair reason','Refresh after intercepting shared missing/stale response',page.getByText(message,{exact:false}).first());
  state.label=`${id}-repair`;state.decision=clone(readyDecision);await page.getByRole('button',{name:'Refresh operations continuity workflows'}).click();await page.waitForTimeout(600);await record(`${id}-repair`,'Matching repaired shared readiness restored','Restore current response and refresh');
 }
 state.label='S6-unavailable';state.contributorFailure=true;await page.getByRole('button',{name:'Refresh operations continuity workflows'}).click();await page.getByText(/Simulated contributor service unavailable/).first().waitFor();await record('S6-unavailable','Unavailable projection blocks close','Respond 503 to shared readiness request');
 state.label='S6-repair';state.contributorFailure=false;await page.getByRole('button',{name:'Refresh operations continuity workflows'}).click();await page.waitForTimeout(600);await record('S6-repair','Ready response recovers after refresh','Restore response and refresh');
 state.label='S7-refusal';state.closeRefusal=true;await page.getByRole('button',{name:`Publish close package for ${readyWorkflow.periodId}`,exact:true}).click();await page.getByText(/Simulated changed evidence\/version refusal/).first().waitFor();await record('S7-refusal','409 command refusal remains visible','Automation attempts intercepted close; no real server command or human decision',page.getByText(/Simulated changed evidence\/version refusal/).first());
 state.label='S7-repair';state.workflow.version++;state.decision.activeWorkflow=clone(state.workflow);await page.getByRole('button',{name:'Refresh operations continuity workflows'}).click();await page.waitForTimeout(600);await record('S7-repair','Updated matching workflow response reloads; human repeat approval still pending','Restore simulated supporting response and refreshed version',page.getByRole('button',{name:`Publish close package for ${readyWorkflow.periodId}`,exact:true}));
 state.workflow=clone(readyWorkflow);state.decision=clone(readyDecision);
 for(const dimension of Object.keys(scope)){
  state.label=`S2-missing-${dimension}`;const incomplete={...scope};delete incomplete[dimension];await navigate('/accounting/operations-continuity',incomplete);await record(`S2-missing-${dimension}`,'Incomplete requested scope cannot use ready response',`Omit ${dimension}; same ready response cannot transfer`);
 }
 state.label='S3-foreign-scope';await navigate('/accounting/operations-continuity',{...scope,entityId:'foreign-entity'});await record('S3-foreign-scope','Mismatched requested ownership cannot use ready response','Change entity selection while shared response retains original ownership');
 state.label='S8-reviewed-current-scope';state.delayCloseMs=0;await navigate('/accounting/operations-continuity');
 state.label='S8-delay-old-scope';state.delayCloseMs=2000;await page.getByRole('button',{name:'Refresh operations continuity workflows'}).click();await page.waitForTimeout(250);
 await page.evaluate(url=>{history.pushState({},'',url);dispatchEvent(new PopStateEvent('popstate'));},`${base}/accounting/operations-continuity?${new URLSearchParams({...scope,entityId:'foreign-entity'})}`);
 state.label='S8-new-scope';state.delayCloseMs=0;await page.waitForTimeout(2400);await record('S8-delayed-old-response','No ready state transfers when old response completes','Delay shared request 2 s; push changed entity selection; complete old response');
 state.label='S8-repair';await navigate('/accounting/operations-continuity');await record('S8-repair','Complete original scope refresh recovers','Restore original complete scope');
 for(const [id,type,message] of [['S4-accounting-details','Missing','Missing report-pack contributor; Reporting must rebuild support.'],['S5-accounting-details','Stale','Stale report-pack contributor; Reporting must refresh support.']]){
  state.label=id;state.decision=clone(readyDecision);Object.assign(state.decision.closeReadiness,{status:'Blocked',isComplete:false,isReadyToClose:false,blockers:[{code:`report-pack-${type.toLowerCase()}`,contributorId:'report-pack',type,count:1,severity:'Critical',owner:'Reporting',message,recordIds:[reportEvidence.evidenceId]}]});
  await navigate('/accounting');await page.getByText('System details',{exact:true}).click();await page.getByText(/Count: 1\. Severity: Critical\./).first().waitFor();
  await record(id,'Named contributor, type, count, severity, owner, source ID and repair route visible','Open Accounting → System details with shared blocker response',page.getByText(/Count: 1\. Severity: Critical\./).first());
 }
 state.decision=clone(readyDecision);
 // Preserve operator-session order: all SEAM exercises above precede MARK below.
 state.label='M1-fresh';await navigate('/accounting');const panel=page.getByRole('region',{name:'Valuation mark impact preview'});const markPanel=page.locator('section[aria-label="Valuation mark impact preview"]');
 await markPanel.getByRole('button',{name:'Preview mark impact'}).waitFor();await markPanel.getByRole('button',{name:'Preview mark impact'}).click();await markPanel.getByText(/0 of 1 positions/).waitFor();await record('M1-fresh','Current observation/date/age and policy visible','Preview fixture-derived schedule; POST intercepted',markPanel);
 const issues=[['M2-stale',{observedOn:'2026-08-01',ageDays:34,blockReason:'AAPL observation is stale.'}],['M3-future',{observedOn:'2026-09-05',ageDays:-1,blockReason:'AAPL observation follows the valuation date.'}],['M4-missing-date',{observedOn:null,ageDays:null,blockReason:'AAPL mark observation is missing.'}],['M5-coverage',{blockReason:'AAPL completeness evidence is missing.'}],['M6-confidence',{blockReason:'AAPL confidence is below policy minimum Medium.'}],['M10-unsupported-override-simulation',{observedOn:'2026-08-01',ageDays:34,blockReason:'AAPL stale observation. Unsupported override wording is simulated; real override expiry is untested.'}]];
 for(const [id,issue] of issues){
  state.label=id;state.mark={...state.mark,blockedPositionCount:1,affectedValuationCount:1,positions:[{...current,...issue,status:'ReviewRequired'}]};await markPanel.getByRole('button',{name:'Preview mark impact'}).click();await markPanel.getByText(issue.blockReason,{exact:true}).waitFor();await record(id,'Shared review-required reason stays visible','Intercept blocked shared assessment and invoke read-only preview',markPanel);
  state.label=`${id}-repair`;state.mark={...state.mark,blockedPositionCount:0,affectedValuationCount:0,positions:[clone(current)]};await markPanel.getByRole('button',{name:'Preview mark impact'}).click();await markPanel.getByText(/0 of 1 positions/).waitFor();await record(`${id}-repair`,'Current assessment restored after fresh preview','Restore dated current response and preview again',markPanel);
 }
 state.label='M-refusal';state.markFailure=true;await markPanel.getByRole('button',{name:'Preview mark impact'}).click();await markPanel.getByText(/Simulated historical mark source unavailable/).waitFor();await record('M-refusal','Failed assessment provides retry and no current preview','Return simulated 503 read-only preview refusal',markPanel);
 state.label='M-refusal-repair';state.markFailure=false;await markPanel.getByRole('button',{name:'Preview mark impact'}).click();await markPanel.getByText(/0 of 1 positions/).waitFor();await record('M-refusal-repair','Read-only reassessment recovers','Restore current response and preview again',markPanel);
 state.label='M8-delayed-old-preview';state.delayMarkMs=2000;await markPanel.getByRole('button',{name:'Preview mark impact'}).click();await page.waitForTimeout(200);
 await page.evaluate(url=>{history.pushState({},'',url);dispatchEvent(new PopStateEvent('popstate'));},`${base}/accounting?${new URLSearchParams({...scope,fundProfileId:'foreign-fund'})}`);
 state.label='M8-new-scope';state.delayMarkMs=0;await page.waitForTimeout(2300);await record('M8-delayed-old-preview','Changed selection must discard old preview','Delay preview 2 s then change fund profile query');
 state.label='desktop-size';await page.setViewportSize({width:1366,height:768});await navigate('/accounting');await markPanel.getByRole('button',{name:'Preview mark impact'}).click();await markPanel.getByText(/0 of 1 positions/).waitFor();await record('M1-fresh-1366x768','Preview remains usable at second desktop size','Reload and reassess at 1366x768',markPanel);
 state.label='S1-second-desktop-size';await navigate('/accounting/operations-continuity');const finalClose=page.getByRole('button',{name:`Publish close package for ${readyWorkflow.periodId}`,exact:true});await finalClose.waitFor();await page.waitForFunction(element=>!element.disabled,await finalClose.elementHandle());await record('S1-current-scope-1366x768','Complete scope remains usable at second desktop size','Reload complete scope at 1366x768',finalClose);
}catch(error){console.log(error.stack);await fs.writeFile(path.join(out,'harness-blocker.txt'),await page.locator('body').innerText());await screenshot('harness-blocker');cases.push({id:'harness-blocker',error:error.message,operatorDecision:'pending'});process.exitCode=1;}
finally{
 const sources=['src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts','src/Meridian.Ui/dashboard/src/screens/operations-continuity-screen.close-test-fixtures.ts','src/Meridian.Ui/dashboard/src/screens/operations-continuity-screen.test.tsx','src/Meridian.Ui/dashboard/src/screens/accounting-screen.mark-preview.test.tsx','src/Meridian.Ui/dashboard/src/screens/portfolio-screen.mark-freshness.test.ts','scripts/dev/web-screenshot-fixtures.json'];
 const hashes=await Promise.all(sources.map(async file=>({file,sha256:crypto.createHash('sha256').update(await fs.readFile(path.join(repo,file))).digest('hex')})));
 const retainedPaths=new Set([commandCenter,continuity,markPreview,scheduleEndpoint,'/api/workstation/operations/private-capital-close-cockpit','/api/workstation/operations/continuity/close-calendar']);
 const targetRequests=requests.filter(r=>{const p=new URL(r.url).pathname;return retainedPaths.has(p)||p.startsWith(`${continuity}/`);});
 await fs.writeFile(path.join(out,'evidence/requests-responses.json'),JSON.stringify(targetRequests,null,2));
 await fs.writeFile(path.join(out,'evidence/network-observations.json'),JSON.stringify(requests.map(({atUtc,scenario,method,url,status,source,responseOrigin,deliveryError})=>({atUtc,scenario,method,url,status,source,responseOrigin,...(deliveryError?{deliveryError}:{})})),null,2));
 await fs.writeFile(path.join(out,'evidence/cases.json'),JSON.stringify(cases,null,2));
 const sourceCandidateMatch=hashes.every(({file,sha256})=>crypto.createHash('sha256').update(execFileSync('git',['-C',repo,'show',`${candidate}:${file}`])).digest('hex')===sha256);
 await fs.writeFile(path.join(out,'evidence/provenance.json'),JSON.stringify({candidate,sourceHead,sourceCandidateMatch,sessionStartUtc,sessionEndUtc:new Date().toISOString(),browserVersion:browser.version(),playwrightVersion:require('playwright/package.json').version,nodeVersion:process.version,platform:process.platform,osRelease:os.release(),browserPlugin:'not available; regular Playwright',url:base,sourceHashes:hashes,client:'browser only',actualAccountingPopulation:'not provisioned',testPopulation:'one unit-contract-derived AAPL assessment; no population-wide claim',operatorIdentity:null,operatorDecision:'pending',implementationOrder:'LOT → MARK → SEAM → RECON',exerciseOrder:'SEAM → MARK; final SEAM screenshot-only resize check repeats completed scope',limitations:['All scenario responses supplied by Playwright route.fulfill','No backend persistence, authorization, PostgreSQL, governed approval, or installed WPF tested','Expired-override wording is response simulation only; actual expiry/recheck must be tested live','M7 representative accounting population and M9 retained lineage remain pending'],consoleMessages},null,2));
 await browser.close();
}
