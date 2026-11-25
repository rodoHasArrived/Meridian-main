#!/usr/bin/env node
/** Deterministic browser acceptance for existing fixed-income access. Never contacts a financial API. */
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { createScreenshotApiResolver, loadScreenshotScenarioModule } from "./web-screenshot-scenarios.mjs";

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const dashboard = path.join(repo, "src/Meridian.Ui/dashboard");
const require = createRequire(path.join(dashboard, "package.json"));
const { chromium } = require("playwright");
const base = (process.argv[2] ?? "http://127.0.0.1:5173/workstation").replace(/\/$/, "");
const output = process.argv[3] ?? "/tmp/meridian-fixed-income-access";
const securityId = "11111111-1111-1111-1111-111111111111";
const projectionRunId = "22222222-2222-2222-2222-222222222222";
const termsVersionId = "33333333-3333-3333-3333-333333333333";
const name = "Synthetic Meridian 5% 2031";
const entry = {
  securityId, displayName: name, status: "Active",
  classification: { assetClass: "Bond", subType: "FixedCoupon", primaryIdentifierKind: "Isin", primaryIdentifierValue: "XS0000000001" },
  economicDefinition: { currency: "USD", version: 7, effectiveFrom: "2026-01-01", effectiveTo: null, subType: "FixedCoupon", assetFamily: "FixedIncome", issuerType: "Corporate" }
};
const identity = { securityId, displayName: name, assetClass: "Bond", status: "Active", version: 7, effectiveFrom: "2026-01-01", effectiveTo: null,
  identifiers: [{ kind: "Isin", value: "XS0000000001", isPrimary: true, validFrom: "2026-01-01", validTo: null, provider: "Synthetic browser acceptance" }], aliases: [] };
const operations = {
  subject: { securityId, assetClass: "Bond", displayName: name, primaryIdentifier: "XS0000000001", operationalProfile: [] },
  termsHistory: [{ securityId, termsVersionId, termsHash: "synthetic-terms-v7", versionNumber: 7, effectiveDate: "2026-01-01", recordedAt: "2026-01-01T12:00:00Z", sourceDomain: "SecurityMaster", sourceEntityId: securityId, summary: "Synthetic retained bond terms v7" }],
  cashFlowProjectionRuns: [{ securityId, projectionRunId, projectionAsOf: "2026-07-05", engineVersion: "asset-obligation-projection-v1", status: "Completed", generatedAt: "2026-07-05T12:00:00Z", sourceDomain: "SecurityMaster", sourceEntityId: securityId }],
  projectedCashFlows: [], lifecycleEvents: [], actualActivity: [], reconciliationRuns: [], reconciliationResults: [], ledgerProjections: [], workflowAudit: [],
  readiness: { securityId, status: "ReviewRequired", capabilities: [], readyCapabilities: [], missingCapabilities: [], blockers: ["Synthetic evidence only"], evaluatedAt: "2026-07-05T12:00:00Z", sourceDomain: "SecurityMaster", sourceEntityId: securityId }
};
const analytics = {
  securityId, currency: "USD", modelVersion: "bond-analytics-v2", context: { asOf: "2026-11-12T00:00:00Z", settlementDate: "2026-11-12" }, yieldConvention: "NominalPeriodic",
  cleanPrice: "99.125", dirtyPrice: "100.125", accruedInterest: "1", yieldToMaturity: "0.0512345", currentYield: "0.05044", macaulayDuration: "3.2", modifiedDuration: "3.1", convexity: "12.5", dv01: "0.031", yieldToWorst: "0.04",
  cashFlows: [{ flowId: "synthetic-coupon", paymentDate: "2027-01-31", accrualStart: "2026-07-31", accrualEnd: "2027-01-31", interest: "2.5", principal: "100", timeInYears: "0.25" }],
  exerciseYields: [], inputs: [{ kind: "SecurityTerms", key: securityId, version: 7, contentSha256: "synthetic-sha256" }], blockers: [], capabilities: [{ operation: "Analytics", supported: true }], diagnostics: ["Synthetic browser acceptance result"], isBlocked: false
};
const scenarioResult = {
  events: [{ eventId: "synthetic-event", streamId: "coupon", periodId: "p1", date: "2027-01-31", accrualStart: "2026-07-31", accrualEnd: "2027-01-31", openingBalance: "1000000", cashInterest: "25000.123456789012", capitalizedInterest: "0", scheduledPrincipal: "0", forecastPrepayment: "0", redeemedPrincipal: "0", redemptionPremium: "0", prepaymentPenalty: "0", closingBalance: "1000000", direction: "Receive" }],
  exerciseEligibility: [{ side: "Call", entryStartDate: "2027-01-31", streamId: "coupon", windowEnd: "2028-01-31", style: "Bermudan", dates: ["2027-01-31", "2027-07-31"], businessDayCalendarId: "NYB" }],
  exerciseCalculations: [], blockers: [], evidence: { engineVersion: "synthetic-contractual-cashflow", mappingVersion: "m1", prepaymentVersion: "p1", kernelVersion: "k1", inputHash: "synthetic-input", outputHash: "synthetic-output", sourceTermsHash: "synthetic-terms-v7", sourceVersion: "7", hasContractOverride: false, calendars: [], curves: [], warnings: [] },
  resolvedContract: { streams: [{ streamId: "coupon", prepaymentAllowed: true }] }, isCalculated: true, isPostable: false
};

const fixtures = JSON.parse(await fs.readFile(path.join(repo, "scripts/dev/web-screenshot-fixtures.json"), "utf8")).routes;
const ladder = structuredClone(fixtures["/api/portfolio/cash-ladder"]);
Object.assign(ladder.contributions[0], { securityId, displayName: name, projectionRunId, termsVersionId, termsHash: "synthetic-terms-v7", sourceEntityId: securityId });
const loaded = await loadScreenshotScenarioModule(dashboard);
const resolver = createScreenshotApiResolver(loaded.module, fixtures);
const explorerPath = "/api/workstation/financial-record-explorers/security-instrument";
const emptyExplorer = structuredClone(resolver.resolve(explorerPath).body);
Object.assign(emptyExplorer, { rows: [], selectedRecord: null, proofActions: [], recordGraph: { nodes: [], edges: [] } });
const populatedExplorer = structuredClone(resolver.resolve(explorerPath).body);
for (const filter of [...populatedExplorer.filters, ...populatedExplorer.savedViews.flatMap(view => view.filters)]) {
  if (filter.filterId === "asset-class") filter.value = "Bond";
}
const selectedExplorerRow = populatedExplorer.rows[0];
selectedExplorerRow.recordId = `security-instrument:${securityId}`;
selectedExplorerRow.label = name;
for (const cell of selectedExplorerRow.cells) {
  if (cell.columnId === "security") cell.displayValue = cell.rawValue = name;
  if (cell.columnId === "assetClass") cell.displayValue = cell.rawValue = "Bond";
}
selectedExplorerRow.detail = {
  recordId: selectedExplorerRow.recordId, recordType: "Security instrument", title: name,
  subtitle: "ISIN XS0000000001", description: "Synthetic retained instrument for browser history acceptance.", tone: "Success",
  fields: [{ label: "Currency", value: "USD", detail: "Retained economic currency.", tone: "Default" }],
  proofActions: [], usedIn: [], impacts: [], fullRecordHref: `/accounting/security-master?securityId=${securityId}`
};
Object.assign(populatedExplorer, { rows: [selectedExplorerRow], selectedRecord: selectedExplorerRow.detail, proofActions: [], recordGraph: { nodes: [], edges: [] } });
const reads = new Map([
  [explorerPath, emptyExplorer],
  ["/api/workstation/security-master/securities", [entry]],
  [`/api/workstation/security-master/securities/${securityId}`, entry],
  [`/api/workstation/security-master/securities/${securityId}/identity`, identity],
  [`/api/security-master/${securityId}/corporate-actions`, []],
  [`/api/security-master/${securityId}/trading-parameters`, { securityId, asOf: "2026-11-12", lotSize: 1000, tickSize: "0.001", contractMultiplier: 1, marginRequirementPct: null, tradingHoursUtc: null, circuitBreakerThresholdPct: null }],
  [`/api/workstation/assets/${securityId}/operations`, operations],
  ["/api/reference-data/bonds/issuer-ladder/page", { items: [{ securityId, displayName: name, currency: "USD", issuerName: "Synthetic Meridian", seniority: "Senior", primaryIdentifier: "XS0000000001", lifecycle: { securityId, issueDate: "2026-01-01", callDate: "2028-01-31", maturityDate: "2031-01-31", isCallable: true, version: 7 }, version: 7 }], nextCursor: null }],
  [`/api/reference-data/bonds/${securityId}/analytics`, analytics],
  ["/api/portfolio/cash-ladder", ladder],
  ["/api/ledger/books", []],
  ["/api/workstation/tables/accounting-reconciliation-breaks/views", []],
  ["/api/ledger/journal-automation/daily-mark-to-market-schedules", []]
]);
const expectedBlockers = new Set([
  `/api/workstation/security-master/securities/${securityId}/trust-snapshot`,
  `/api/workstation/security-master/securities/${securityId}/passport`,
  "/api/ledger/close-management/period-plan/79f1f386-0bb1-4aef-9a85-fb9d6de8e1f6"
]);
// The existing reference catalog probes other asset families during selection. These are
// deliberately unavailable supporting reads, not synthetic claims about those workflows.
const isReferenceCatalogProbe = pathname => /^\/api\/reference-data\/(bonds|options|equities|futures|fxspot|swaps|commodities|crypto|deposits|money-market-funds|certificates-of-deposit|edgar)\//.test(pathname);
const report = { evidenceScope: "synthetic-verified; live providers untested", browserPath: "Browser plugin not available; Playwright with installed Chromium", base, viewports: [], checks: [], screenshots: [], pageErrors: [], consoleErrors: [], failedRequests: [], unhandledApiRequests: [], expectedApiBlockers: [], scenarioRequests: [], analyticsRequests: [] };
report.pageIdentitySnapshots = [];
let browser;
let page;
try {
  await fs.mkdir(output, { recursive: true });
  browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH ?? "/usr/bin/chromium", args: ["--no-sandbox"] });
  page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
  page.setDefaultTimeout(10000);
  page.on("pageerror", error => report.pageErrors.push(error.message));
  page.on("console", message => { if (message.type() === "error") report.consoleErrors.push(message.text()); });
  page.on("requestfailed", request => { if (request.failure()?.errorText !== "net::ERR_ABORTED") report.failedRequests.push({ url: request.url(), error: request.failure()?.errorText }); });
  await page.route("**/api/**", async route => {
    const request = route.request();
    const url = new URL(request.url());
    if (!url.pathname.startsWith("/api/")) return route.continue();
    const send = (status, body) => route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body), headers: { "x-meridian-dev-fixture": "true" } });
    if (request.method() === "GET" && url.pathname === "/api/symbols/search" && url.searchParams.get("query") === "Security Master") {
      return send(200, []); // This command query has no synthetic asset matches.
    }
    if (request.method() === "GET" && reads.has(url.pathname)) {
      if (url.pathname === `/api/reference-data/bonds/${securityId}/analytics`) report.analyticsRequests.push(Object.fromEntries(url.searchParams));
      return send(200, reads.get(url.pathname));
    }
    if (request.method() === "POST" && url.pathname === `/api/security-master/${securityId}/cashflow-projections/preview`) {
      report.scenarioRequests.push(request.postDataJSON());
      return send(200, scenarioResult);
    }
    if ((request.method() === "GET" && (expectedBlockers.has(url.pathname) || isReferenceCatalogProbe(url.pathname))) ||
      (request.method() === "POST" && url.pathname === "/api/workstation/operator/visits/reconciliation-breaks")) {
      report.expectedApiBlockers.push(url.pathname);
      return send(503, { title: "Synthetic supporting evidence unavailable", detail: "No retained supporting evidence is configured in this synthetic browser acceptance scope.", status: 503 });
    }
    const response = resolver.resolve(request.url(), request.method());
    if (response.status !== 404) return send(response.status, response.body);
    report.unhandledApiRequests.push(`${request.method()} ${url.pathname}${url.search}`);
    return send(501, { title: "Acceptance fixture unavailable", detail: `No synthetic response registered for ${request.method()} ${url.pathname}.`, status: 501 });
  });
  const navigate = route => page.goto(`${base}${route}`, { waitUntil: "domcontentloaded" });
  const assertFocusVisible = async id => {
    await page.waitForFunction(targetId => {
      const target = document.getElementById(targetId);
      const heading = target?.querySelector("h2,h3,summary");
      const header = target?.closest(".workspace-section-band")?.querySelector(".workspace-section-subheader");
      return document.activeElement === target && heading && header && heading.getBoundingClientRect().top >= header.getBoundingClientRect().bottom - 1;
    }, id);
  };
  const capture = async (label, width = 1440, height = 900) => {
    await page.setViewportSize({ width, height });
    const title = await page.title();
    const body = await page.locator("body").innerText();
    assert.match(title, /meridian/i);
    assert.ok(page.url().startsWith(base), `Unexpected page identity: ${page.url()}`);
    assert.ok(body.trim().length > 200, "Page must render meaningful app content");
    assert.equal(await page.locator("vite-error-overlay").count(), 0);
    const snapshot = path.join(output, `${label}-${width}x${height}.txt`);
    await fs.writeFile(snapshot, body);
    report.pageIdentitySnapshots.push({ url: page.url(), title, bodyLength: body.length, headings: await page.locator("h1").allTextContents(), snapshot });
    if (!report.viewports.some(size => size.width === width && size.height === height)) report.viewports.push({ width, height });
    const file = path.join(output, `${label}-${width}x${height}.png`);
    await page.screenshot({ path: file, fullPage: false });
    report.screenshots.push(file);
  };

  await navigate("/accounting/security-master");
  await page.getByRole("region", { name: "Bond reference ladders" }).waitFor();
  assert.equal(new URL(page.url()).searchParams.get("securityId"), null);
  await page.getByLabel("Issuer name", { exact: true }).fill("Synthetic Meridian");
  await page.getByRole("button", { name: "Load ladder", exact: true }).click();
  const bondRow = page.getByRole("table", { name: "Bond reference ladder rows" }).getByRole("button", { name, exact: true });
  await bondRow.focus();
  await capture("issuer-ladder-before-selection");
  await bondRow.press("Enter");
  await page.waitForURL(url => url.searchParams.get("securityId") === securityId);
  assert.equal(new URL(page.url()).searchParams.get("tab"), "evidence");
  await page.getByLabel("Clean price / 100", { exact: true }).waitFor();
  await page.getByRole("button", { name: "Return to bond ladder", exact: true }).click();
  assert.equal(await page.getByLabel("Issuer name", { exact: true }).inputValue(), "Synthetic Meridian");
  await page.waitForFunction(displayName => [...document.querySelectorAll("section[aria-label='Bond reference ladders'] button")]
    .some(button => button.textContent === displayName && document.activeElement === button), name);
  report.checks.push("Issuer ladder browsable before selection; keyboard activation opens the exact instrument Evidence tab; Return to bond ladder retains the query and restores row focus.");
  await page.getByLabel("Ladder kind", { exact: true }).selectOption("maturity");
  await page.getByLabel("Maturity window start", { exact: true }).fill("2030-01-01");
  await page.getByLabel("Maturity window end", { exact: true }).fill("2032-12-31");
  reads.set("/api/reference-data/bonds/maturity-ladder/page", reads.get("/api/reference-data/bonds/issuer-ladder/page"));
  await page.getByRole("button", { name: "Load ladder", exact: true }).click();
  await page.getByRole("table", { name: "Bond reference ladder rows" }).getByRole("button", { name, exact: true }).waitFor();
  await capture("maturity-window-ladder", 1366, 768);
  report.checks.push("Maturity date-window query renders a bounded single page; multi-page continuation is not claimed by this browser check.");

  await navigate(`/portfolio/asset-detail?securityId=${securityId}`);
  await page.getByRole("link", { name: "Bond analytics", exact: true }).click();
  await page.waitForURL(url => url.searchParams.get("securityId") === securityId && url.searchParams.get("bondAction") === "analytics" && url.searchParams.get("tab") === "evidence");
  await page.getByLabel("Input basis", { exact: true }).selectOption("cleanPrice");
  await page.getByLabel("Clean price / 100", { exact: true }).fill("99.125");
  await page.getByRole("button", { name: "Calculate bond analytics", exact: true }).click();
  await page.getByText("Calculation ready.", { exact: true }).waitFor();
  assert.equal(report.analyticsRequests.at(-1).cleanPrice, "99.125");
  assert.equal(report.analyticsRequests.at(-1).calculationVersion, "bond-analytics-v2");
  await page.getByText("Projected contractual payments (1)", { exact: true }).click();
  await page.getByRole("table", { name: "Projected bond payments" }).waitFor();
  await page.locator("#bond-analytics-action").evaluate(element => element.scrollIntoView({ block: "start" }));
  await capture("bond-analytics-calculated");
  report.checks.push("Asset detail action opens exact bond Evidence tab; analytics form submits and displays synthetic calculation/provenance.");
  // Validate focus with the laptop layout already applied, before the navigation
  // measures its wrapped sticky header. Resizing only for a screenshot would test
  // a different layout from the one used by the focus-visibility assertion.
  await page.setViewportSize({ width: 1366, height: 768 });
  await page.reload({ waitUntil: "domcontentloaded" });
  await page.getByLabel("Clean price / 100", { exact: true }).waitFor();
  const restored = new URL(page.url());
  assert.equal(restored.searchParams.get("securityId"), securityId);
  assert.equal(restored.searchParams.get("tab"), "evidence");
  assert.equal(restored.searchParams.get("bondAction"), "analytics");
  assert.equal(await page.getByLabel("Clean price / 100", { exact: true }).inputValue(), "");
  await page.getByText("Enter inputs to calculate. No market price is assumed.", { exact: true }).waitFor();
  await assertFocusVisible("bond-analytics-action");
  await page.locator("#bond-analytics-action").evaluate(element => element.scrollIntoView({ block: "start" }));
  await capture("bond-analytics-deep-link-reloaded", 1366, 768);
  report.checks.push("Reload preserves exact bond/action/Evidence deep link and resets calculation inputs; no price is silently assumed.");

  await navigate(`/portfolio/asset-detail?securityId=${securityId}`);
  await page.getByRole("link", { name: "Convention migration", exact: true }).click();
  await page.waitForURL(url => url.searchParams.get("securityId") === securityId && url.searchParams.get("bondAction") === "migration");
  await page.waitForFunction(() => document.activeElement?.id === "bond-migration-action");
  await assertFocusVisible("bond-migration-action");
  const migrationDisclosure = page.getByText("Review bond convention migration", { exact: true });
  if (!(await page.getByRole("form", { name: "Bond migration preview inputs" }).isVisible())) await migrationDisclosure.click();
  await page.getByRole("form", { name: "Bond migration preview inputs" }).waitFor();
  await capture("bond-migration-focus", 1366, 768);
  await migrationDisclosure.click();
  assert.equal(await page.getByRole("form", { name: "Bond migration preview inputs" }).isVisible(), false);
  await migrationDisclosure.click();
  await page.getByRole("form", { name: "Bond migration preview inputs" }).waitFor();
  report.checks.push("Asset detail convention action opens exact record and focuses migration preview. No draft or terms mutation performed.");

  await navigate("/portfolio/cash-ladder");
  const bucket = page.getByRole("button", { name: /Jul 5 - Jul 11: net/ });
  await bucket.focus();
  await bucket.press("Enter");
  const flowTable = page.getByRole("table", { name: "Cash ladder contributing flows" });
  assert.match(await flowTable.getByRole("link", { name, exact: true }).getAttribute("href"), new RegExp(`securityId=${securityId}`));
  await flowTable.getByRole("button", { name: `Inspect projection run for ${name}`, exact: true }).click();
  const evidence = page.getByRole("region", { name: `Projection evidence for ${name}` });
  await evidence.getByText("Matched terms evidence", { exact: true }).waitFor();
  assert.ok((await evidence.innerText()).includes(projectionRunId));
  assert.ok((await evidence.innerText()).includes("Synthetic retained bond terms v7"));
  await evidence.scrollIntoViewIfNeeded();
  await capture("cash-ladder-projection-evidence");
  await capture("cash-ladder-projection-evidence", 1366, 768);
  report.checks.push("Keyboard bucket drill exposes instrument/source links and exact Asset Operations run plus matching retained terms.");

  await page.keyboard.press("Control+k");
  await page.getByLabel("Search command palette", { exact: true }).fill("Account cash forecasts");
  const forecastCommand = page.getByRole("link", { name: "Open Account cash forecasts route", exact: true });
  await forecastCommand.focus();
  await forecastCommand.press("Enter");
  await page.waitForURL(url => url.pathname.endsWith("/accounting/reconciliation") && url.searchParams.get("oversight") === "forecast");
  const oversight = page.getByRole("region", { name: "Account oversight", exact: true });
  await oversight.getByText("No account-owned books available", { exact: true }).waitFor();
  await oversight.getByLabel("Account-owned ledger book", { exact: true }).waitFor();
  await oversight.scrollIntoViewIfNeeded();
  await capture("account-forecast-configuration-blocker");
  report.checks.push("Ctrl+K command search and keyboard forecast-command activation exposes account book selector and actionable no-account-book configuration blocker.");

  await navigate(`/portfolio/asset-detail?securityId=${securityId}`);
  await page.getByRole("tab", { name: "Cash-flow scenarios", exact: true }).click();
  await page.getByLabel("Scenario valuation date", { exact: true }).fill("2026-10-31");
  await page.getByRole("button", { name: "Preview scenario", exact: true }).click();
  await page.getByText("25,000.123456789012", { exact: true }).waitFor();
  await page.getByRole("button", { name: "Add prepayment assumption", exact: true }).click();
  await page.getByLabel("Prepayment 1 stream", { exact: true }).selectOption("coupon");
  await page.getByLabel("Prepayment 1 end date", { exact: true }).fill("2027-01-01");
  await page.getByLabel("Prepayment 1 value", { exact: true }).fill("0.0612345678901234567");
  await page.getByRole("button", { name: "Add prepayment assumption", exact: true }).click();
  await page.getByLabel("Prepayment 2 stream", { exact: true }).selectOption("coupon");
  await page.getByLabel("Prepayment 2 start date", { exact: true }).fill("2027-01-01");
  await page.getByLabel("Prepayment 2 end date", { exact: true }).fill("2027-07-01");
  await page.getByLabel("Prepayment 2 method", { exact: true }).selectOption("Psa");
  await page.getByLabel("Prepayment 2 value", { exact: true }).fill("1.5");
  await page.getByLabel("Prepayment 2 seasoning origin", { exact: true }).fill("2026-01-01");
  const exercise = page.getByLabel("Include call exercise on coupon", { exact: true });
  await exercise.focus();
  await exercise.press("Space");
  assert.equal(await exercise.isChecked(), true);
  await page.getByLabel("Exercise amount for coupon", { exact: true }).fill("1000000");
  await page.getByLabel("Notice date for coupon", { exact: true }).fill("2027-01-05");
  await page.getByRole("button", { name: "Preview scenario", exact: true }).click();
  await page.getByText("25,000.123456789012", { exact: true }).waitFor();
  const submitted = report.scenarioRequests.at(-1);
  assert.equal(submitted.prepayments.length, 2);
  assert.equal(submitted.prepayments[0].value, "0.0612345678901234567");
  assert.equal(submitted.prepayments[1].method, "Psa");
  assert.equal(submitted.exercises[0].noticeDate, "2027-01-05");
  assert.equal(submitted.exercises[0].hypotheticalNotice, true);
  await page.getByLabel("Prepayment 2 method", { exact: true }).scrollIntoViewIfNeeded();
  await capture("scenario-prepayments-and-notice", 1366, 768);
  report.checks.push("Scenario preview submits two exact prepayment intervals and an explicit hypothetical notice date, separate from exercise date.");

  reads.set(explorerPath, populatedExplorer);
  for (const { width, height } of [{ width: 1440, height: 900 }, { width: 1366, height: 768 }]) {
    await page.setViewportSize({ width, height });
    await navigate("/portfolio/cash-ladder");
    await page.getByRole("button", { name: /Jul 5 - Jul 11: net/ }).waitFor();
    await page.keyboard.press("Control+k");
    await page.getByLabel("Search command palette", { exact: true }).fill("Security Master");
    await page.getByRole("link", { name: "Open Security Master route", exact: true }).click();
    await page.waitForURL(url => url.pathname.endsWith("/accounting/security-master") && url.searchParams.get("securityId") === securityId);
    await page.getByRole("row").filter({ hasText: name }).waitFor();
    await page.goBack();
    await page.waitForURL(url => url.pathname.endsWith("/portfolio/cash-ladder"));
    await capture("security-master-browser-back", width, height);
    await page.goForward();
    await page.waitForURL(url => url.pathname.endsWith("/accounting/security-master") && url.searchParams.get("securityId") === securityId);
    await page.getByRole("heading", { name: "Security Master", exact: true }).waitFor();
    await capture("security-master-browser-forward", width, height);
  }
  report.checks.push("With a populated explorer, default Security Master selection preserves one navigation entry: Back returns to Cash ladder and Forward restores the exact instrument at both viewport widths.");
  assert.equal(await page.locator("vite-error-overlay").count(), 0);
  assert.equal(report.pageErrors.length, 0, JSON.stringify(report.pageErrors));
  assert.equal(report.failedRequests.length, 0, JSON.stringify(report.failedRequests));
  assert.equal(report.unhandledApiRequests.length, 0, JSON.stringify(report.unhandledApiRequests));
  const unexpectedConsole = report.consoleErrors.filter(message => !message.includes("503 (Service Unavailable)"));
  assert.equal(unexpectedConsole.length, 0, JSON.stringify(unexpectedConsole));
  report.status = "passed";
} catch (failure) {
  report.status = "failed";
  report.error = failure.stack ?? String(failure);
  if (page) {
    report.failureUrl = page.url();
    report.failureFocus = await page.evaluate(() => ({ tag: document.activeElement?.tagName, id: document.activeElement?.id, text: document.activeElement?.textContent?.slice(0,120) }));
    report.failureText = await page.locator("body").innerText().catch(() => "Body unavailable");
    await page.screenshot({ path: path.join(output, "failure.png"), fullPage: true }).catch(() => {});
  }
  process.exitCode = 1;
} finally {
  await browser?.close();
  await loaded.close();
  await fs.mkdir(output, { recursive: true });
  report.consoleErrors = [...new Set(report.consoleErrors)];
  report.expectedApiBlockers = [...new Set(report.expectedApiBlockers)];
  report.unhandledApiRequests = [...new Set(report.unhandledApiRequests)];
  await fs.writeFile(path.join(output, "report.json"), `${JSON.stringify(report, null, 2)}\n`);
  console.log(JSON.stringify(report, null, 2));
}
