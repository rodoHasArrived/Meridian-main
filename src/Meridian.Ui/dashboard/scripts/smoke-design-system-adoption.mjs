#!/usr/bin/env node
/* global console, process, URL, fetch, document, window, requestAnimationFrame, innerWidth, localStorage, DataTransfer, getComputedStyle */
/** Production screens with synthetic local API fixtures. Start fixture-only Vite first.
 * No provider connection, maintenance run, report publication, or backend write is made.
 * Browser plugin is unavailable in this execution environment; use Playwright Chromium.
 */
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";

const args = process.argv.slice(2);
if (args.includes("--help")) {
  console.log("Start MERIDIAN_DEV_MODE=fixture-only npm run dev -- --port 5178, then:\nnode scripts/smoke-design-system-adoption.mjs [--url http://127.0.0.1:5178/workstation] [--output <directory>]\nOptional focused reruns: --only institutions,maintenance,report-tokens --sizes 1920,1440,1366,desktop-200-percent\nUse CHROMIUM_EXECUTABLE_PATH or MERIDIAN_CHROMIUM_PATH for a system browser.\nDefaults audit all three regions in both appearances at 1920/1440/1366 and the 1366x768 desktop at 200% equivalent (683x384 CSS pixels, DPR 2). Axe scope is each migrated region.");
  process.exit(0);
}
const options = new Map();
for (let i = 0; i < args.length; i += 2) {
  if (!["--url", "--output", "--only", "--sizes"].includes(args[i]) || !args[i + 1]) throw new Error(`Invalid option: ${args[i]}`);
  options.set(args[i], args[i + 1]);
}
const address = new URL(options.get("--url") || "http://127.0.0.1:5178/workstation");
assert(["localhost", "127.0.0.1", "[::1]"].includes(address.hostname), "Use a local fixture-only Vite server");
const base = `${address.origin}${address.pathname === "/" ? "/workstation" : address.pathname}`.replace(/\/+$/, "");
const output = path.resolve(options.get("--output") || path.join(tmpdir(), "meridian-design-system-adoption"));
const dashboard = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const require = createRequire(import.meta.url);
const axeSource = await readFile(require.resolve("axe-core/axe.min.js"), "utf8");
const report = {
  schemaVersion: 1, auditedAt: new Date().toISOString(), url: base,
  evidenceScope: "synthetic-verified: actual production screens and styles; local fixture-only Vite; no live provider, maintenance service or publication claim",
  accessibilityScope: "WCAG 2 A/AA, 2.1 A/AA and 2.2 AA in the three migrated regions; not a whole-workstation conformance claim",
  zoomScope: "200% desktop equivalent: 1366x768 device pixels / 2 = 683x384 CSS pixels, deviceScaleFactor 2; no mobile product flow",
  reviewedSources: [], cases: [], accessibility: [], layoutFailures: [], exceptions: [], networkFailures: [], fixtureLimitations: [], expectedFailures: [],
};
for (const file of ["src/screens/data-screen.tsx", "src/screens/data-operations-maintenance.tsx", "src/screens/reporting-screen.report-writer.tsx"]) {
  report.reviewedSources.push({ path: `src/Meridian.Ui/dashboard/${file}`, reviewedSourceHash: createHash("sha256").update(await readFile(path.join(dashboard, file))).digest("hex") });
}
await mkdir(output, { recursive: true });
// Unknown paths return this declaration only in explicit fixture-only mode. Probe
// before installing any mocks or exercising a control capable of sending a command.
const probe = await fetch(`${address.origin}/api/__design_system_adoption_fixture_probe__`);
assert.equal(probe.status, 501, "Fixture-only server must refuse unsupported fixture routes");
assert.equal((await probe.json()).mode, "fixture-only", "Refuse backend-connected or automatic fallback servers");

const institutions = [
  { institutionId: "ins:synthetic/business", name: "North Bank Business", products: ["Transactions"], countryCodes: ["US"], url: null, primaryColor: null, logo: null },
  { institutionId: "ins:synthetic/exact-003", name: "North Bank", products: ["Transactions", "Auth", "Identity"], countryCodes: ["US"], url: null, primaryColor: null, logo: null },
];
const executions = Array.from({ length: 25 }, (_, index) => ({
  executionId: `execution:synthetic/${(index * 7 + 13) % 25}`, scheduleId: null, scheduleName: null,
  taskType: index % 2 ? 0 : 1, status: index === 0 ? 1 : index === 1 ? 2 : 4,
  manualTrigger: index === 0, startedAt: `2026-09-${String(25 - index).padStart(2, "0")}T12:00:00Z`, completedAt: null,
  duration: null, filesProcessed: 1000 + index, issuesFound: 0, issuesResolved: 0, bytesProcessed: 0, bytesSaved: 0,
  errorMessage: index > 1 ? "Synthetic checksum review" : null, logMessages: [],
}));
const allSizes = [
  { label: "1920", width: 1920, height: 1080, scale: 1 },
  { label: "1440", width: 1440, height: 900, scale: 1 },
  { label: "1366", width: 1366, height: 768, scale: 1 },
  { label: "desktop-200-percent", width: 683, height: 384, scale: 2 },
];
const sceneNames = ["institutions", "maintenance", "report-tokens"];
const requestedScenes = options.get("--only")?.split(",") || sceneNames;
const requestedSizes = options.get("--sizes")?.split(",") || allSizes.map(s => s.label);
assert(requestedScenes.length && requestedScenes.every(name => sceneNames.includes(name)), "Unknown --only scene");
assert(requestedSizes.length && requestedSizes.every(name => allSizes.some(s => s.label === name)), "Unknown --sizes viewport");
const sizes = allSizes.filter(size => requestedSizes.includes(size.label));
report.matrix = { scenes: sceneNames.filter(name => requestedScenes.includes(name)), sizes, themes: ["light", "dark"] };
const expectedCases = report.matrix.scenes.length * sizes.length * 2;
const browserPath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(browserPath ? { executablePath: browserPath } : {}), args: ["--no-sandbox"] });
const pendingResponses = [];
async function settled(page) {
  await page.evaluate(async () => {
    await document.fonts.ready;
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    await Promise.all(document.getAnimations().filter(a => a.effect?.getComputedTiming().iterations !== Infinity).map(a => a.finished.catch(() => {})));
  });
  assert.equal(await page.locator("vite-error-overlay").count(), 0, "No Vite error overlay");
}
async function visual(page, region, scene, theme, size) {
  await region.scrollIntoViewIfNeeded(); await settled(page);
  const overflow = await page.evaluate(() => ({ width: innerWidth, scrollWidth: document.documentElement.scrollWidth }));
  if(overflow.scrollWidth > overflow.width + 1) {
    const offenders = await page.evaluate(() => [...document.body.querySelectorAll("*")].map(n => ({n,b:n.getBoundingClientRect()})).filter(({n,b}) => b.width > 0 && b.height > 0 && b.right > innerWidth + 1 && getComputedStyle(n).position !== "fixed").slice(0,12).map(({n,b}) => ({tag:n.tagName,role:n.getAttribute("role"),className:n.className,text:n.textContent?.slice(0,70),left:b.left,right:b.right,width:b.width})));
    report.layoutFailures.push({scene,theme,viewport:size,overflow,offenders});
    assert.fail(`${scene}: page-level horizontal overflow: ${JSON.stringify({overflow,offenders})}`);
  }
  if (scene === "report-tokens") {
    const targets = await region.evaluate(zone => {
      const bounds = zone.getBoundingClientRect();
      return [...zone.querySelectorAll("button")].map(button => {
        const b = button.getBoundingClientRect();
        return { name: button.getAttribute("aria-label"), width: b.width, height: b.height, contained: b.left >= bounds.left - 1 && b.right <= bounds.right + 1 };
      });
    });
    assert(targets.length > 0 && targets.every(t => t.width >= 24 && t.height >= 24 && t.contained), "Token controls are contained and at least 24x24 CSS pixels");
  }
  await page.addScriptTag({ content: axeSource });
  const audit = await region.evaluate(async node => {
    const result = await window.axe.run(node, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] } });
    return { passes: result.passes.length, incomplete: result.incomplete.map(v => v.id), violations: result.violations.map(v => ({ id: v.id, impact: v.impact, nodes: v.nodes.map(n => ({ target: n.target, summary: n.failureSummary })) })) };
  });
  const screenshot = `${scene}-${theme}-${size.label}.png`;
  await page.screenshot({ path: path.join(output, screenshot) });
  report.accessibility.push({ scene, theme, viewport: size, overflow, screenshot, ...audit });
  assert.equal(audit.violations.length, 0, `${scene}/${theme}/${size.label}: axe violations: ${JSON.stringify(audit.violations)}`);
}
async function runCase(page, scene, theme, size, run) {
  if(!report.matrix.scenes.includes(scene)) return;
  const record = { scene, theme, viewport: size, status: "passed" };
  try { await run(record); } catch (error) {
    record.status = "failed"; record.error = error.stack || error.message;
    console.error(record.error);
    await page.screenshot({ path: path.join(output, `failed-${scene}-${theme}-${size.label}.png`) }).catch(() => {});
  }
  report.cases.push(record);
  await writeFile(path.join(output, "results.in-progress.json"), JSON.stringify(report, null, 2) + "\n");
  console.log(`${record.status}: ${scene}/${theme}/${size.label}`);
}
try {
  for (const theme of ["light", "dark"]) for (const size of sizes) {
    const context = await browser.newContext({ viewport: { width: size.width, height: size.height }, deviceScaleFactor: size.scale, colorScheme: theme, reducedMotion: "reduce", timezoneId: "UTC" });
    await context.addInitScript(theme => localStorage.setItem("meridian.workstation.appearance.v1", theme), theme);
    const page = await context.newPage(); page.setDefaultTimeout(15000); page.setDefaultNavigationTimeout(20000);
    const state = { institutionMode: "populated", maintenanceMode: "populated", cancelled: false, searches: [], historyReads: [], cancellations: [] };
    page.on("pageerror", error => report.exceptions.push({ theme, size: size.label, message: error.message }));
    page.on("requestfailed", request => {
      if (request.failure()?.errorText !== "net::ERR_ABORTED") report.networkFailures.push({ url: request.url(), error: request.failure()?.errorText });
    });
    page.on("response", response => {
      if (response.status() < 400) return;
      pendingResponses.push((async () => {
        const body = await response.json().catch(() => null);
        if (response.status() === 501 && body?.mode === "fixture-only") report.fixtureLimitations.push({ path: new URL(response.url()).pathname });
        else if (response.status() === 503 && body?.syntheticAdoptionFailure === true) report.expectedFailures.push({ path: new URL(response.url()).pathname });
        else report.networkFailures.push({ url: response.url(), status: response.status() });
      })());
    });
    await page.route("**/*", async route => {
      const request = route.request(), url = new URL(request.url());
      if (url.origin !== address.origin && !["data:", "blob:"].includes(url.protocol)) {
        report.networkFailures.push({ url: request.url(), error: "Unexpected external request" }); return route.abort();
      }
      const json = (body, status = 200) => route.fulfill({ status, contentType: "application/json", headers: { "x-meridian-dev-fixture": "true" }, body: JSON.stringify(body) });
      if (url.pathname === "/api/plaid/institutions/search") {
        state.searches.push({ query: url.searchParams.get("query"), method: request.method() });
        if (state.institutionMode === "error") return json({ title: "Synthetic institution lookup unavailable", status: 503, syntheticAdoptionFailure: true }, 503);
        return json({ query: url.searchParams.get("query"), requestId: "synthetic-adoption", institutions: state.institutionMode === "empty" ? [] : institutions });
      }
      if (url.pathname.startsWith("/api/maintenance/")) {
        const cancel = url.pathname.match(/^\/api\/maintenance\/executions\/([^/]+)\/cancel$/);
        if (cancel && request.method() === "POST") {
          state.cancellations.push(decodeURIComponent(cancel[1])); state.cancelled = true;
          return json({ message: "Synthetic execution cancellation accepted." });
        }
        if (request.method() !== "GET") {
          report.networkFailures.push({ url: request.url(), error: "Unexpected synthetic maintenance command" }); return json({ title: "Command outside test scope" }, 405);
        }
        if (url.pathname.endsWith("/status")) return json({ isRunning: true, queuedExecutions: 1, currentExecution: null, nextScheduledExecution: "2026-09-26T02:00:00Z", activeSchedules: 3, totalExecutionsToday: 25, uptime: "1.02:00:00" });
        if (url.pathname.endsWith("/statistics")) return json({ generatedAt: "2026-09-25T12:00:00Z", totalSchedules: 4, enabledSchedules: 3, disabledSchedules: 1, totalExecutions: 25, successfulExecutions: 1, failedExecutions: 23, executionsLast24Hours: 25, executionsLast7Days: 25, totalBytesProcessed: 1048576, totalBytesSaved: 524288, totalIssuesFound: 4, totalIssuesResolved: 4, averageExecutionDuration: "00:02:00", lastExecutionAt: "2026-09-25T02:00:00Z", nextScheduledExecution: "2026-09-26T02:00:00Z" });
        if (url.pathname.endsWith("/schedules/summary")) return json({ totalSchedules: 4, enabledSchedules: 3, disabledSchedules: 1, byTaskType: { "0": 2, "1": 2 }, nextDueSchedule: "2026-09-26T02:00:00Z", nextDueScheduleName: "Synthetic health check" });
        if (url.pathname.endsWith("/task-types")) return json([{ value: "HealthCheck", name: "Health check", description: "Synthetic check" }, { value: "Cleanup", name: "Cleanup", description: "Synthetic cleanup" }]);
        if (/\/executions(?:\/failed)?$/.test(url.pathname)) {
          state.historyReads.push({ path: url.pathname, limit: url.searchParams.get("limit") });
          if (state.maintenanceMode === "error") return json({ title: "Synthetic maintenance history unavailable", status: 503, syntheticAdoptionFailure: true }, 503);
          const rows = executions.map((row, index) => index === 0 && state.cancelled ? { ...row, status: 3 } : row);
          return json(state.maintenanceMode === "empty" ? [] : url.pathname.endsWith("/failed") ? rows.filter(r => r.status === 4) : rows);
        }
      }
      return route.continue();
    });
    await runCase(page, "institutions", theme, size, async record => {
      await page.goto(`${base}/data/providers`);
      await page.getByRole("button", { name: /configure a new data provider/i }).click();
      await page.getByLabel("Select provider type").selectOption("plaid");
      const search = page.getByRole("searchbox", { name: "Search supported financial institutions", exact: true });
      await search.fill("North Bank"); await settled(page); assert.equal(state.searches.length, 0, "Typing never starts the explicit server search");
      await search.press("Enter");
      const table = page.getByRole("table", { name: "Supported financial institutions", exact: true }); await table.waitFor();
      const first = table.getByRole("row", { name: "Select North Bank Business", exact: true });
      const exact = table.getByRole("row", { name: "Select North Bank", exact: true });
      assert.equal(await exact.getAttribute("data-dense-row-id"), institutions[1].institutionId);
      await first.focus(); await first.press("End"); assert.equal(await exact.evaluate(n => n === document.activeElement), true);
      await exact.press("Home"); assert.equal(await first.evaluate(n => n === document.activeElement), true);
      await first.press("ArrowDown"); await exact.press("Enter"); await exact.press("Space");
      assert.equal(await exact.getAttribute("aria-selected"), "true"); assert.equal(await first.getAttribute("aria-selected"), null);
      assert.equal(await search.inputValue(), "North Bank", "Exact row selection preserves search text");
      assert.deepEqual(state.searches, [{ query: "North Bank", method: "GET" }]);
      await page.getByText("North Bank is selected for the next bank connection step.", { exact: true }).waitFor();
      await visual(page, page.getByRole("region", { name: "Bank connection institution search", exact: true }), "institutions", theme, size);
      state.institutionMode = "error"; await search.fill("Unavailable"); await search.press("Enter");
      await page.getByRole("status").filter({ hasText: /Synthetic institution lookup unavailable/ }).waitFor(); assert.equal(await table.count(), 0);
      assert.equal(await page.getByRole("button", { name: /Open secure bank connection unavailable/ }).isDisabled(), true);
      state.institutionMode = "empty"; await search.fill("None"); await search.press("Enter"); await page.getByText('No supported institutions matched "None".', { exact: true }).waitFor();
      state.institutionMode = "populated"; await search.fill("North Bank"); await search.press("Enter"); await exact.waitFor(); await exact.click();
      assert.equal(await exact.getAttribute("aria-selected"), "true");
      assert.deepEqual(state.searches.map(r => r.query), ["North Bank", "Unavailable", "None", "North Bank"]);
      record.exactSelectedInstitutionId = await exact.getAttribute("data-dense-row-id"); record.serverSearches = state.searches;
      record.states = ["explicit-submit", "exact-id", "keyboard", "error", "empty", "recovery"];
    });
    await runCase(page, "maintenance", theme, size, async record => {
      await page.goto(`${base}/data/assurance`);
      const region = page.locator('section[aria-labelledby="archive-maintenance-title"]');
      const table = region.getByRole("table", { name: "Maintenance execution history", exact: true }); await table.waitFor();
      const files = () => table.locator("tbody tr td:nth-child(6)").allTextContents();
      const expectedFiles = executions.map(r => String(r.filesProcessed));
      await page.waitForFunction(() => document.querySelectorAll('[aria-label="Maintenance execution history"] tbody tr').length === 25);
      assert.deepEqual(await files(), expectedFiles, "All 25 returned executions retain exact server order");
      await region.getByLabel("Maintenance task type", { exact: true }).selectOption("Cleanup");
      assert.deepEqual(await files(), expectedFiles, "Task choice does not sort or filter financial history");
      assert.equal(await table.locator("thead button").count(), 0, "History headers do not add sorting");
      const scroll = region.getByRole("region", { name: "Maintenance execution history scroll area", exact: true });
      await scroll.focus(); await scroll.press("Tab"); assert.equal(await table.getByRole("button", { name: "Cancel", exact: true }).evaluate(n => n === document.activeElement), true);
      await visual(page, region, "maintenance", theme, size);
      await table.getByRole("button", { name: "Cancel", exact: true }).press("Enter");
      await region.getByText("Synthetic execution cancellation accepted.", { exact: true }).waitFor();
      assert.deepEqual(state.cancellations, [executions[0].executionId], "Cancellation submits the exact encoded server identity");
      await region.getByRole("button", { name: "Show failed only", exact: true }).click();
      await page.waitForFunction(() => document.querySelectorAll('[aria-label="Maintenance execution history"] tbody tr').length === 23);
      assert.deepEqual(await files(), executions.filter(r => r.status === 4).map(r => String(r.filesProcessed)));
      assert.equal(await table.getByRole("button", { name: "Cancel", exact: true }).count(), 0);
      await region.getByRole("button", { name: "Show all executions", exact: true }).click();
      state.maintenanceMode = "error"; await region.getByRole("button", { name: "Refresh", exact: true }).click();
      await region.getByText(/1 of 5 maintenance reads failed/).waitFor(); assert.equal(await table.getByRole("button", { name: "Cancel", exact: true }).count(), 0);
      state.maintenanceMode = "empty"; await region.getByRole("button", { name: "Refresh", exact: true }).click();
      await table.getByText("No maintenance executions recorded.", { exact: true }).waitFor();
      state.maintenanceMode = "populated"; await region.getByRole("button", { name: "Refresh", exact: true }).click();
      await page.waitForFunction(() => document.querySelectorAll('[aria-label="Maintenance execution history"] tbody tr').length === 25);
      assert.deepEqual(await files(), expectedFiles); assert(state.historyReads.every(r => r.limit === "25"), "Every history request preserves the 25-row server limit");
      record.historyReads = state.historyReads; record.serverExecutionIds = executions.map(r => r.executionId); record.cancelledId = state.cancellations[0];
      record.states = ["25-row-limit", "server-order", "native-select", "keyboard-scroll", "exact-cancellation-id", "failed-only", "error", "empty", "recovery"];
    });
    await runCase(page, "report-tokens", theme, size, async record => {
      await page.goto(`${base}/reporting/report-builder`);
      const metrics = page.getByRole("list", { name: "Sector Pivot Metrics", exact: true }); await metrics.waitFor();
      const left = metrics.getByRole("button", { name: "Move P&L left", exact: true }); await left.focus(); await left.press("Enter");
      assert.equal(await left.isDisabled(), true); assert.equal(await left.getAttribute("title"), "This token is already first in the list.");
      const right = metrics.getByRole("button", { name: "Move P&L right", exact: true }); await right.focus(); await right.press("Space");
      assert.equal(await right.isDisabled(), true); assert.equal(await right.getAttribute("title"), "This token is already last in the list.");
      await visual(page, metrics, "report-tokens", theme, size);
      const labels = list => list.getByRole("listitem").evaluateAll(nodes => nodes.map(n => n.querySelector(".break-all.font-mono")?.textContent));
      const before = await labels(metrics); assert.equal(before.at(-1), "P&L");
      const remove = metrics.getByRole("button", { name: "Remove P&L", exact: true }); await remove.focus(); await remove.press("Enter");
      assert.deepEqual(await labels(metrics), before.filter(label => label !== "P&L"));
      const sources = page.getByRole("list", { name: "Sector Pivot source fields", exact: true });
      const source = sources.getByRole("listitem").filter({ has: page.getByText("pnl", { exact: true }) });
      const formulas = page.getByRole("list", { name: "Sector Pivot Formulas", exact: true });
      async function drag(from, to) {
        // Browser-native DataTransfer and real React drag handlers; no direct state access.
        const dataTransfer = await page.evaluateHandle(() => new DataTransfer());
        const sourceHandle = await from.elementHandle();
        try {
          await sourceHandle.dispatchEvent("dragstart", { dataTransfer });
          const payload = await dataTransfer.evaluate(d => JSON.parse(d.getData("application/x-meridian-report-writer-token")));
          await to.dispatchEvent("dragover", { dataTransfer }); await to.dispatchEvent("drop", { dataTransfer }); await sourceHandle.dispatchEvent("dragend", { dataTransfer });
          return payload;
        } finally { await sourceHandle.dispose(); await dataTransfer.dispose(); }
      }
      const copied = await drag(source, formulas); assert.equal(copied.origin, null); assert.equal(copied.token.label, "pnl");
      assert.equal(await formulas.getByText("pnl", { exact: true }).count(), 1); assert.equal(await source.count(), 1, "Source field is copied");
      const movedToken = formulas.getByRole("listitem").filter({ has: page.getByText("pnl", { exact: true }) });
      const moved = await drag(movedToken, metrics); assert.equal(moved.token.id, copied.token.id); assert.equal(moved.origin.zone, "formulas");
      assert.equal(await formulas.getByText("pnl", { exact: true }).count(), 0); assert.equal(await metrics.getByText("pnl", { exact: true }).count(), 1);
      await drag(source, metrics); assert.equal(await metrics.getByText("pnl", { exact: true }).count(), 1, "Duplicate token drop remains a no-op");
      await metrics.getByRole("button", { name: "Remove pnl", exact: true }).press("Space"); assert.equal(await metrics.getByText("pnl", { exact: true }).count(), 0);
      record.copiedToken = copied.token; record.moveOrigin = moved.origin; record.states = ["native-keyboard", "reorder", "boundary-disabled-reason", "removal", "drag-copy", "drag-move", "duplicate-no-op", "24px-targets"];
    });
    await context.close();
  }
} finally {
  await Promise.all(pendingResponses); await browser.close();
  for (const source of report.reviewedSources) {
    const hash = createHash("sha256").update(await readFile(path.join(dashboard, source.path.replace("src/Meridian.Ui/dashboard/", "")))).digest("hex");
    if (hash !== source.reviewedSourceHash) report.exceptions.push({ message: `${source.path} changed during execution; rerun final proof` });
  }
  report.summary = { cases: report.cases.length, failed: report.cases.filter(c => c.status === "failed").length, axeScans: report.accessibility.length, axeViolations: report.accessibility.reduce((n, r) => n + r.violations.length, 0), exceptions: report.exceptions.length, networkFailures: report.networkFailures.length };
  await writeFile(path.join(output, "results.json"), JSON.stringify(report, null, 2) + "\n");
  console.log(JSON.stringify({ output, ...report.summary }));
}
if (report.summary.failed || report.summary.exceptions || report.summary.networkFailures || report.summary.axeViolations || report.summary.cases !== expectedCases || report.summary.axeScans !== expectedCases) process.exitCode = 1;
