// W10-SEAM-001 response simulation only. No live acceptance or human verdict is produced.
import { createRequire } from 'node:module';
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
const repo='/workspace/Meridian-main';
const out=process.env.W10_QA_OUTPUT ?? '/workspace/scratch/w10-seam-source-s/browser/rendered';
const require=createRequire(path.join(repo,'src/Meridian.Ui/dashboard/package.json'));
const {chromium}=require('playwright');
const base='http://127.0.0.1:4173/workstation';
const clone=x=>structuredClone(x);
const sourceHead=execFileSync('git',['-C',repo,'rev-parse','HEAD'],{encoding:'utf8'}).trim();
const expectedSourceHead=process.env.W10_EXPECTED_SOURCE_HEAD;
if(!expectedSourceHead||sourceHead!==expectedSourceHead)throw new Error(`Expected committed candidate ${expectedSourceHead}, found ${sourceHead}`);
const sessionStartUtc=new Date().toISOString();
await fs.mkdir(path.join(out,'screenshots'),{recursive:true});
const browser=await chromium.launch({executablePath:'/usr/bin/chromium',headless:true,args:['--no-sandbox']});
const page=await browser.newPage({viewport:{width:1440,height:900}});
page.setDefaultTimeout(12000);
page.setDefaultNavigationTimeout(45000);
const consoleMessages=[],requests=[],cases=[];let phase='preflight';
page.on('console',m=>{if(['warning','error'].includes(m.type()))consoleMessages.push({phase,type:m.type(),text:m.text()});});
page.on('pageerror',e=>consoleMessages.push({phase,type:'pageerror',text:e.message}));
page.on('response',r=>{if(r.status()>=400)consoleMessages.push({phase,type:'http-error',status:r.status(),url:r.url()});});
await page.addInitScript(()=>localStorage.setItem('meridian.workstation.onboarding.dismissed','true'));
await page.goto(`${base}/`);
const fixtures=await page.evaluate(async()=>{
  const {resolveDevFixture}=await import('/workstation/src/lib/dev-fixtures.ts');
  const {sharedCloseDecision}=await import('/workstation/src/screens/operations-continuity-screen.close-test-fixtures.ts');
  const {WORKSTATION_API_ENDPOINTS}=await import('/workstation/src/lib/workstation-endpoints.ts');
  const routes=Object.fromEntries(Object.values(WORKSTATION_API_ENDPOINTS).filter(x=>typeof x==='string').map(x=>[x,resolveDevFixture(x)]).filter(([,v])=>v!==undefined));
  const workflow=resolveDevFixture(`${WORKSTATION_API_ENDPOINTS.operationsContinuity}/${routes[WORKSTATION_API_ENDPOINTS.operationsContinuity][0].workflowId}`);
  return {routes,workflow,decision:sharedCloseDecision(workflow)};
});
const screenshotFixtures=JSON.parse(await fs.readFile(path.join(repo,'scripts/dev/web-screenshot-fixtures.json'),'utf8')).routes;
const continuity='/api/workstation/operations/continuity';
const commandCenter='/api/workstation/operations/financial-operations-command-center';
const scope=fixtures.decision.closeReadiness.scope;
const ready=clone(fixtures.workflow);
Object.assign(ready,{status:'ReadyForClose',brokerIntakeState:'Complete',securityMasterState:'Complete',ledgerPostingState:'Complete',reconciliationState:'Complete',approvalState:'Approved',closePackage:null,nextActions:[],blockers:[],closeReadiness:{isReadyToClose:true,severity:'Info',score:100,components:[],blockers:[],nextActions:[]}});
ready.gates=ready.gates.map(g=>({...g,status:'Passed',blockers:[],nextActions:[],completedAtUtc:'2026-10-06T12:00:00Z',completedBy:'simulated-controller'}));
ready.reconciliationLanes=ready.reconciliationLanes?.map(l=>({...l,status:'Ready',isReady:true,breakCount:0,requiredActions:[]}));
ready.breakCases=ready.breakCases.map(b=>({...b,status:'Resolved'}));
ready.closeChecklist=ready.closeChecklist.map(t=>({...t,status:'Done',blockingReason:null,requiredApprovalCount:1,canAcknowledge:false,acknowledgedAtUtc:'2026-10-06T12:00:00Z',acknowledgedBy:'simulated-controller'}));
if(ready.dashboardSummary)Object.assign(ready.dashboardSummary,{status:'Ready',isReady:true,stage:'Produce Evidence',readyMetricCount:6,totalMetricCount:6,requiredActions:[],metrics:ready.dashboardSummary.metrics.map(m=>({...m,status:'Ready',value:'Ready',detail:'Simulated current records are ready for review.',requiredActions:[]}))});
function revision(v){
  const evidence={evidenceId:`response-simulation-report-evidence-v${v}`,label:`Simulated report manifest v${v}`,route:`/workstation/reporting/evidence/response-simulation-report-evidence-v${v}`,source:'response-simulation',capturedAtUtc:`2026-10-06T12:${String(v).padStart(2,'0')}:00Z`};
  const workflow=clone(ready);workflow.version=v;workflow.reportPackReadiness={isReady:true,reportPackId:`response-simulation-report-pack-v${v}`,blockingReason:null,evidenceLinks:[evidence]};
  workflow.closeChecklist=workflow.closeChecklist.map(t=>({...t,evidencePointer:evidence.evidenceId}));
  workflow.approvals=[{approvalId:`response-simulation-approval-v${v}`,status:'Approved',operator:'simulated-maker',reviewer:'simulated-checker',submittedAtUtc:'2026-10-06T11:00:00Z',decidedAtUtc:evidence.capturedAtUtc,evidenceLinks:[evidence]}];
  if(workflow.dashboardSummary){workflow.dashboardSummary.evidenceLinks=[evidence];workflow.dashboardSummary.metrics=workflow.dashboardSummary.metrics.map(m=>({...m,evidenceLinks:[evidence]}));}
  const decision={...clone(fixtures.decision),activeWorkflow:clone(workflow)};
  decision.closeReadiness.contributors=[{contributorId:'close-plan',owner:'Controller',status:'Current',evaluatedAtUtc:evidence.capturedAtUtc,recordIds:[`response-simulation-close-plan-v${v}`]},{contributorId:'report-pack',owner:'Reporting',status:'Current',evaluatedAtUtc:evidence.capturedAtUtc,recordIds:[evidence.evidenceId]}];
  return {workflow,decision,evidence};
}
let current=revision(8),state={label:'initial-v8',...current,closeMode:'refuse',delayDetailMs:0,delayDecisionMs:0,detailFailure:false,detailOverride:null};
function change(v,extra={}){current=revision(v);state={...state,...current,...extra};}
await page.route('**/api/**',async route=>{
  const request=route.request(),u=new URL(request.url()),p=u.pathname,captured=clone(state);let body,status=200,source='maintained-development-fixture';
  if(p.startsWith('/api/workstation/first-run/')){body=clone(screenshotFixtures['/api/workstation/first-run/']);body.workspace.safetyMessage='RESPONSE SIMULATION — no provisioned accounting host or human approval';}
  else if(p===commandCenter){body=captured.decision;source='simulated-shared-projection';if(captured.delayDecisionMs)await new Promise(r=>setTimeout(r,captured.delayDecisionMs));}
  else if(p===continuity){body=fixtures.routes[continuity].map((w,i)=>i?w:{...w,status:captured.workflow.status,version:captured.workflow.version,gates:captured.workflow.gates,nextActions:[]});}
  else if(p===`${continuity}/${ready.workflowId}/close`){
    source='simulated-command';
    if(captured.closeMode==='refuse'){status=409;body={message:'Simulated changed workflow, close plan and evidence refusal. Refresh supporting records and review again.'};}
    else {
      assert.equal(request.postDataJSON().expectedVersion,captured.workflow.version);
      assert.equal(request.postDataJSON().reportPackId,captured.workflow.reportPackReadiness.reportPackId);
      assert.ok(request.postDataJSON().evidenceLinks.some(e=>e.evidenceId===captured.evidence.evidenceId));
      const closed=clone(captured.workflow);closed.version++;closed.status='Closed';closed.closePackage={closePackageId:`response-simulation-close-package-v${closed.version}`,reportPackId:captured.workflow.reportPackReadiness.reportPackId,retainedManifestId:`response-simulation-manifest-v${closed.version}`,retainedManifestRoute:captured.evidence.route,evidenceHash:'simulation-evidence-hash-not-real-retention',publishedAtUtc:'2026-10-06T12:40:00Z',publishedBy:'simulated-controller',signOffRationale:'Automated response simulation only',evidenceLinks:[captured.evidence],checklistControlApprovals:[]};
      state={...state,workflow:closed,decision:{...state.decision,activeWorkflow:clone(closed)}};
      body={success:true,workflow:closed,blockers:[],message:'Response simulation: close package published; live operator verdict pending.'};
    }
  }
  else if(p===`${continuity}/${ready.workflowId}`){source='simulated-workflow-detail';body=captured.detailOverride??captured.workflow;if(captured.delayDetailMs)await new Promise(r=>setTimeout(r,captured.delayDetailMs));if(captured.detailFailure){status=503;body={message:'Simulated selected workflow detail unavailable.'};}}
  else if(fixtures.routes[p]!==undefined)body=fixtures.routes[p];
  else if(screenshotFixtures[p]!==undefined)body=screenshotFixtures[p];
  else {const prefix=Object.keys(screenshotFixtures).find(k=>p.startsWith(k.endsWith('/')?k:`${k}/`));if(prefix)body=screenshotFixtures[prefix];else return route.continue();}
  const record={atUtc:new Date().toISOString(),scenario:captured.label,method:request.method(),url:request.url(),requestBody:request.postDataJSON?.()??null,status,responseBody:body,source,responseOrigin:'Playwright route.fulfill; no service persistence',operatorDecision:'pending'};requests.push(record);
  try{await route.fulfill({status,contentType:'application/json',headers:{'cache-control':'no-store','x-meridian-dev-fixture':'true'},body:JSON.stringify(body)});}catch(e){record.deliveryError=e.message;}
});
const close=()=>page.getByRole('button',{name:`Publish close package for ${ready.periodId}`,exact:true});
const refresh=()=>page.getByRole('button',{name:'Refresh operations continuity workflows'});
async function waitReady(){await close().waitFor();await page.waitForFunction(e=>!e.disabled,await close().elementHandle());}
async function navigate(sc=scope){await page.goto(`${base}/accounting/operations-continuity?${new URLSearchParams(sc)}`);await page.getByRole('region',{name:'Operations continuity control strip'}).waitFor();}
async function showSystemDetails(){const details=page.locator('details').filter({has:page.locator('summary').filter({hasText:'Workflow system details'})});if(await details.count()&&(await details.getAttribute('open'))===null)await details.locator('summary').click();}
async function screenshot(id,focus){
 if(focus)await focus.scrollIntoViewIfNeeded();
 await page.evaluate(()=>{let b=document.getElementById('qa-provenance');if(!b){b=document.createElement('aside');b.id='qa-provenance';document.body.append(b);}b.style.cssText='position:fixed;bottom:6px;left:236px;right:12px;background:#111827;color:#fff;border:2px solid #f59e0b;padding:6px 10px;font:12px monospace;z-index:2147483647;pointer-events:none';b.textContent='W10-SEAM-001 · RESPONSE SIMULATION · no live server publication · human operator verdict PENDING';});
 const file=path.join(out,'screenshots',`${id}.png`);await page.screenshot({path:file,fullPage:false});return file;
}
async function record(id,expected,focus){
 const bodyText=await page.locator('body').innerText();
 const record={id,expected,automationDecision:'pass',operatorDecision:'pending',operatorIdentity:null,atUtc:new Date().toISOString(),url:page.url(),title:await page.title(),viewport:page.viewportSize(),workflowVersion:state.workflow.version,projectionWorkflowVersion:state.decision.activeWorkflow.version,selectedReportPackId:(state.detailOverride??state.workflow).reportPackReadiness.reportPackId,projectionReportPackId:state.decision.activeWorkflow.reportPackReadiness.reportPackId,selectedReportEvidence:(state.detailOverride??state.workflow).reportPackReadiness.evidenceLinks,projectionReportEvidence:state.decision.activeWorkflow.reportPackReadiness.evidenceLinks,projectionContributorRecords:state.decision.closeReadiness.contributors.map(c=>({contributorId:c.contributorId,recordIds:c.recordIds})),closeButtonCount:await close().count(),closeButtonDisabled:await close().count()?await close().isDisabled():null,frameworkOverlay:await page.locator('vite-error-overlay').count(),bodyText,screenshot:await screenshot(id,focus)};
 assert.equal(record.frameworkOverlay,0);assert.ok(bodyText.includes('Operations Continuity'));assert.ok(record.url.includes('/accounting/operations-continuity'));assert.ok(record.title.toLowerCase().includes('meridian'));
 cases.push(record);console.log(`PASS ${id}`);
}
try{
 phase='rendered-response-simulation';
 await navigate();await waitReady();await page.evaluate(()=>window.scrollTo(0,0));await record('S0-first-viewport','Page identity, meaningful app content and first viewport without framework overlay');await showSystemDetails();
 await record('S1-initial-v8','Matching workflow v8 + plan-v8 + evidence-v8 allow command',page.getByText('Workflow system details',{exact:true}));
 await close().click();await page.getByText(/Simulated changed workflow, close plan and evidence refusal\./).first().waitFor();
 await record('S2-refusal-v8','409 refusal remains visible without a package',page.getByText(/Simulated changed workflow, close plan and evidence refusal\./).first());
 change(9,{label:'repair-v9',delayDetailMs:1000,closeMode:'publish'});await refresh().click();
 await page.waitForTimeout(150);assert.ok(!(await close().count())||await close().isDisabled(),'publication stays unavailable during selected-detail refresh');
 await record('S3-refresh-pending','Old v8 detail cannot supply enabled publication while v9 detail loads',refresh());
 await waitReady();await showSystemDetails();await page.getByText('Ready: response-simulation-report-pack-v9',{exact:true}).first().waitFor();
 assert.ok(!(await page.locator('body').innerText()).includes('response-simulation-report-pack-v8'),'old report pack must be replaced');
 await record('S4-repaired-v9','Selected detail/report pack v9 matches current shared workflow and contributor records',page.getByText('Workflow system details',{exact:true}));
 await close().focus();assert.equal(await close().evaluate(e=>e===document.activeElement),true);await page.keyboard.press('Enter');
 await page.getByText('Response simulation: close package published; live operator verdict pending.',{exact:true}).waitFor();
 await page.getByLabel('Close package publication summary').getByText('response-simulation-close-package-v10',{exact:true}).waitFor();
 const lastClose=requests.filter(r=>r.method==='POST'&&new URL(r.url).pathname.endsWith('/close')).at(-1);
 assert.equal(lastClose.requestBody.expectedVersion,9);assert.equal(lastClose.requestBody.reportPackId,'response-simulation-report-pack-v9');assert.ok(lastClose.requestBody.evidenceLinks.some(e=>e.evidenceId==='response-simulation-report-evidence-v9'));
 await record('S5-published-v10','Enter submits repaired v9 command and refreshed selected detail retains simulated package v10',page.getByLabel('Close package publication summary'));
 change(11,{label:'ready-v11',delayDetailMs:0,closeMode:'refuse'});await refresh().click();await waitReady();
 change(12,{label:'mismatched-detail-v11',detailOverride:revision(11).workflow});await refresh().click();await page.waitForTimeout(700);
 assert.ok(!(await close().count())||await close().isDisabled(),'mismatched v11 selected detail vs v12 summary/projection must block');
 await record('S6-version-mismatch','Older returned selected detail cannot enable command for newer summary/projection',refresh());
 state={...state,label:'repair-mismatch-v12',detailOverride:null};await refresh().click();await waitReady();await record('S7-version-match-recovered','Matching current v12 details restore publication',close());
 change(13,{label:'late-detail-v13',delayDetailMs:1500});await refresh().click();await page.waitForTimeout(120);
 const foreignScope={...scope,entityId:'response-simulation-foreign-entity'};
 await page.evaluate(url=>{history.pushState({},'',url);dispatchEvent(new PopStateEvent('popstate'));},`${base}/accounting/operations-continuity?${new URLSearchParams(foreignScope)}`);
 state={...state,label:'foreign-scope-after-late-detail',delayDetailMs:0};await page.waitForTimeout(1800);
 assert.ok(!(await close().count())||await close().isDisabled(),'late detail cannot transfer ready posture across scope');
 await record('S8-late-detail-wrong-scope','Delayed old-scope detail stays unavailable after entity changes',refresh());
 change(14,{label:'scope-repair-v14',delayDetailMs:0});await navigate();await waitReady();await record('S9-scope-repaired','Original complete scope recovers with current v14 detail',close());
 state={...state,label:'detail-unavailable',detailFailure:true};await refresh().click();await page.waitForTimeout(700);assert.ok(!(await close().count())||await close().isDisabled(),'failed detail refresh cannot leave publish enabled');
 await record('S10-detail-failure','Selected detail failure blocks publication',refresh());
 state={...state,label:'detail-failure-repaired',detailFailure:false};await refresh().click();await waitReady();
 await page.setViewportSize({width:1366,height:768});await showSystemDetails();await record('S11-1366x768-current','Controls/details remain usable at second desktop size',page.getByText('Workflow system details',{exact:true}));
 await refresh().focus();assert.equal(await refresh().evaluate(e=>e===document.activeElement),true);await page.keyboard.press('Enter');await waitReady();await page.keyboard.press('Tab');
 cases.push({id:'S12-keyboard-basics',automationDecision:'pass',operatorDecision:'pending',observed:{refreshActivatedByEnter:true,publishActivatedByEnter:true,focusAfterTab:await page.evaluate(()=>({tag:document.activeElement?.tagName,label:document.activeElement?.getAttribute('aria-label')??document.activeElement?.textContent}))}});
 console.log('PASS S12-keyboard-basics');
 const revised=revision(15);revised.workflow.version=14;revised.decision.activeWorkflow=clone(revised.workflow);state={...state,...revised,label:'same-workflow-version-new-plan-and-evidence',closeMode:'publish'};
 await refresh().click();await waitReady();await showSystemDetails();await page.getByText('Ready: response-simulation-report-pack-v15',{exact:true}).waitFor();
 assert.ok(!(await page.locator('body').innerText()).includes('response-simulation-report-pack-v14'));
 await record('S13-same-workflow-version-repaired','Unchanged workflow version 14 refresh replaces plan/report/evidence records with v15',page.getByText('Workflow system details',{exact:true}));
 await close().click();await page.getByLabel('Close package publication summary').getByText('response-simulation-close-package-v15',{exact:true}).waitFor();
 const sameVersionCommand=requests.filter(r=>r.method==='POST'&&new URL(r.url).pathname.endsWith('/close')).at(-1);assert.equal(sameVersionCommand.requestBody.expectedVersion,14);assert.equal(sameVersionCommand.requestBody.reportPackId,'response-simulation-report-pack-v15');assert.ok(sameVersionCommand.requestBody.evidenceLinks.some(e=>e.evidenceId==='response-simulation-report-evidence-v15'));
 await record('S14-same-version-current-command','Second simulated publication submits workflow v14 with refreshed report/evidence v15',page.getByLabel('Close package publication summary'));
 change(16,{label:'delayed-same-scope-v16',delayDetailMs:1500});const delayedRequest=page.waitForRequest(r=>new URL(r.url()).pathname===`${continuity}/${ready.workflowId}`);await refresh().click();await delayedRequest;
 change(17,{label:'newer-refresh-v17',delayDetailMs:0});await refresh().click();await waitReady();await showSystemDetails();await page.getByText('Ready: response-simulation-report-pack-v17',{exact:true}).waitFor();await page.waitForTimeout(1700);
 assert.ok(!(await page.locator('body').innerText()).includes('response-simulation-report-pack-v16'));await page.getByText('Ready: response-simulation-report-pack-v17',{exact:true}).waitFor();
 await record('S15-late-detail-after-newer-refresh','Late v16 detail cannot overwrite completed v17 refresh for same workflow/scope',page.getByText('Workflow system details',{exact:true}));
 await page.setViewportSize({width:1440,height:900});
 const oldEvidence=revision(18),newEvidence=revision(19);newEvidence.workflow.version=18;newEvidence.decision.activeWorkflow=clone(newEvidence.workflow);
 const publicationCalls=()=>requests.filter(r=>r.method==='POST'&&new URL(r.url).pathname.endsWith('/close'));
 const callsBeforeMismatch=publicationCalls().length;
 const mismatchText='Close evidence no longer matches this workflow. Refresh workflows before publishing a close package.';
 state={...state,...oldEvidence,decision:newEvidence.decision,label:'same-version-detail-old-projection-new',closeMode:'refuse',delayDetailMs:0,detailOverride:null};
 await refresh().click();await page.getByText(mismatchText,{exact:true}).first().waitFor();assert.equal(await close().isDisabled(),true);
 await record('S16-same-version-detail-old-projection-new','Workflow v18 in both responses; older selected report/evidence v18 cannot publish against projection report/evidence v19',close());
 state={...state,...newEvidence,decision:oldEvidence.decision,label:'same-version-detail-new-projection-old'};
 await refresh().click();await page.getByText(mismatchText,{exact:true}).first().waitFor();assert.equal(await close().isDisabled(),true);assert.equal(publicationCalls().length,callsBeforeMismatch);
 await record('S17-same-version-detail-new-projection-old','Workflow v18 in both responses; newer selected report/evidence v19 cannot publish against projection report/evidence v18; neither mismatch sends a command',close());
 state={...state,...newEvidence,label:'same-version-matched-command-refusal',closeMode:'refuse'};await refresh().click();await waitReady();await close().click();await page.getByText(/Simulated changed workflow, close plan and evidence refusal\./).first().waitFor();
 assert.equal(publicationCalls().at(-1).requestBody.expectedVersion,18);assert.equal(publicationCalls().at(-1).requestBody.reportPackId,'response-simulation-report-pack-v19');assert.equal(publicationCalls().at(-1).status,409);
 await record('S18-same-version-refusal','Matching workflow v18/report v19 permits an attempted command; simulated server refusal remains visible',page.getByText(/Simulated changed workflow, close plan and evidence refusal\./).first());
 const matchingRepair=revision(20);matchingRepair.workflow.version=18;matchingRepair.decision.activeWorkflow=clone(matchingRepair.workflow);state={...state,...matchingRepair,label:'same-version-matched-repair-current-evidence',closeMode:'publish'};
 await refresh().click();await waitReady();await showSystemDetails();await page.getByText('Ready: response-simulation-report-pack-v20',{exact:true}).waitFor();
 await record('S19-same-version-matching-repair','Workflow version remains18; selected/projection report, evidence and contributor record identities all refresh to v20',page.getByText('Workflow system details',{exact:true}));
 await close().click();await page.getByLabel('Close package publication summary').getByText('response-simulation-close-package-v19',{exact:true}).waitFor();const repairedCommand=publicationCalls().at(-1);assert.equal(repairedCommand.requestBody.expectedVersion,18);assert.equal(repairedCommand.requestBody.reportPackId,'response-simulation-report-pack-v20');assert.ok(repairedCommand.requestBody.evidenceLinks.some(e=>e.evidenceId==='response-simulation-report-evidence-v20'));assert.equal(repairedCommand.status,200);
 await record('S20-same-version-repaired-publication','Simulated publication submits repaired report/evidence v20 with expected workflow version18 and retains closed workflow19',page.getByLabel('Close package publication summary'));
 change(21,{label:'same-evidence-id-different-retained-route',closeMode:'refuse'});state.decision.activeWorkflow.reportPackReadiness.evidenceLinks[0].route='/workstation/reporting/evidence/response-simulation-replaced-location';await refresh().click();await page.getByText(mismatchText,{exact:true}).first().waitFor();assert.equal(await close().isDisabled(),true);
 await record('S21-same-id-retained-location-mismatch','Identical workflow/report/evidence ID cannot hide a changed retained route in the shared projection',close());
 change(22,{label:'same-report-different-publication-evidence-union'});state.decision.activeWorkflow.evidenceLinks=[...state.decision.activeWorkflow.evidenceLinks,{evidenceId:'response-simulation-projection-only-retained-source',label:'Simulated additional retained evidence',route:'/workstation/reporting/evidence/response-simulation-projection-only-retained-source',source:'response-simulation',capturedAtUtc:'2026-10-06T12:22:00Z'}];await refresh().click();await page.getByText(mismatchText,{exact:true}).first().waitFor();assert.equal(await close().isDisabled(),true);
 await record('S22-publication-evidence-union-mismatch','Matching report-pack evidence cannot bypass a mismatch in the other evidence the command would publish',close());
 state={...state,label:'all-publication-evidence-matched',decision:{...state.decision,activeWorkflow:clone(state.workflow)}};await refresh().click();await waitReady();await record('S23-all-publication-evidence-repaired','Matching all retained publication evidence restores availability',close());
}catch(error){console.error(error.stack);await fs.writeFile(path.join(out,'harness-blocker.txt'),await page.locator('body').innerText());cases.push({id:'harness-blocker',automationDecision:'fail',operatorDecision:'pending',error:error.stack,screenshot:await screenshot('harness-blocker')});process.exitCode=1;}
finally{
 const sourcePaths=['src/Meridian.Ui/dashboard/src/screens/operations-continuity-screen.view-model.ts','src/Meridian.Ui/dashboard/src/screens/operations-continuity-screen.workflow-selection.ts','src/Meridian.Ui/dashboard/src/screens/operations-continuity-screen.tsx','src/Meridian.Ui/dashboard/src/screens/operations-continuity-screen.close-test-fixtures.ts','src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts','src/Meridian.Ui/dashboard/src/lib/workstation-endpoints.ts','scripts/dev/web-screenshot-fixtures.json','scripts/dev/web-screenshot-routes.json'];
 const sourceHashes=await Promise.all(sourcePaths.map(async file=>({file,sha256:crypto.createHash('sha256').update(await fs.readFile(path.join(repo,file))).digest('hex')})));
 const relevantConsole=consoleMessages.filter(m=>m.phase==='rendered-response-simulation');
 await fs.writeFile(path.join(out,'requests-responses.json'),JSON.stringify(requests.filter(r=>new URL(r.url).pathname.startsWith(continuity)||new URL(r.url).pathname===commandCenter),null,2));
 await fs.writeFile(path.join(out,'cases.json'),JSON.stringify(cases,null,2));
 await fs.writeFile(path.join(out,'provenance.json'),JSON.stringify({sourceHead,sourceHashes,workingTree:true,sessionStartUtc,sessionEndUtc:new Date().toISOString(),browserVersion:browser.version(),playwrightVersion:require('playwright/package.json').version,url:base,browserPlugin:'not available; installed Playwright fallback',tier:'Rendered response simulation; all governed API scenarios intercepted by Playwright',client:'browser',actualAccountingPopulation:'not provisioned',operatorIdentity:null,operatorDecision:'pending',implementationOrder:'LOT → MARK → SEAM',exerciseOrder:'SEAM only; MARK not exercised and live acceptance ordering remains SEAM → MARK',consoleMessages,relevantConsole,limitations:['No backend persistence, real authorization, PostgreSQL or installed WPF is exercised','No human operator verdict is fabricated','Plan/evidence revisions are simulated contributor record identities; no separate version fields exist on this projection DTO']},null,2));
 await browser.close();
}
