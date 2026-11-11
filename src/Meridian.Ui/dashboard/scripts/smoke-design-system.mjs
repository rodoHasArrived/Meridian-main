#!/usr/bin/env node
/** Real workstation regression check. Run Vite in fixture-only mode, then this script.
 * Evidence stays in the OS temp directory unless --output is supplied.
 * Browser plugin unavailable in Cloud: use the installed Playwright Chromium or
 * CHROMIUM_EXECUTABLE_PATH to select the platform browser.
 */
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { chromiumLaunchOptions, parseDesignSystemOptions } from "./design-system-smoke-helpers.mjs";
import { runExpandedDesignSystemSmoke } from "./design-system-smoke-profile.mjs";

const args = process.argv.slice(2);
if (args.includes("--help")) {
  console.log("Start MERIDIAN_DEV_MODE=fixture-only npm run dev -- --port 5178, then:\nnode scripts/smoke-design-system.mjs [--url http://127.0.0.1:5178/workstation] [--output <directory>]\nSet MERIDIAN_CHROMIUM_PATH or CHROMIUM_EXECUTABLE_PATH when using a system Chromium. For the complete maintained matrix, use smoke-workstation.mjs --design-system (starts isolated fixture, backend and production-preview servers).");
  process.exit(0);
}
const profileOptions = parseDesignSystemOptions(args);
const { base, output } = profileOptions;
const origin = new URL(base).origin;
const require = createRequire(import.meta.url);
const axeSource = await readFile(require.resolve("axe-core/axe.min.js"), "utf8");
const sourceRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const appearanceKey = "meridian.workstation.appearance.v1";
const workspaces = ["Trading", "Portfolio", "Accounting", "Reporting", "Strategy", "Data", "Settings"];
const themes = {
  light: { canvas: "#F2F0EB", panel: "#F8F7F3", border: "#D2CDC3", signal: "#C56343", axis: "#29323A", focus: "#2D6FB3" },
  dark: { canvas: "#11171D", panel: "#171E25", border: "#2C3640", signal: "#D88466", axis: "#F1EFEA", focus: "#5C9FE5" }
};
const report = { sourceRoot, url: base, browser: "Chromium / Playwright", evidenceScope: "Synthetic fixture-only workstation; no backend or live-account validation", checks: [], accessibility: [], fixtureLimitations: [], consoleErrors: [], exceptions: [], assetFailures: [] };
await mkdir(output, { recursive: true });
const browser = await chromium.launch(chromiumLaunchOptions());
const pendingResponses = [];
let fixtureServer = false;
// Known gaps observed on the unchanged fixture-only baseline. A live server is
// never allowed this exception; successful fixture-provenance headers prove scope.
const knownFixtureGaps = new Set([
  "/api/workstation/operator/scope-preference", "/api/risk/escalations",
  "/api/workstation/operations/financial-operations-command-center",
  "/api/ledger/journal-automation/daily-mark-to-market-schedules",
  "/api/workstation/operator/visits/reconciliation-breaks", "/api/quality/dashboard", "/api/ledger/books",
  "/api/providers/capability-matrix", "/api/security-master/corporate-actions/inbox",
  "/api/security-master/quality-report/latest"
]);
function monitor(page) {
  page.setDefaultTimeout(20000);
  page.setDefaultNavigationTimeout(20000);
  page.on("pageerror", error => report.exceptions.push(error.message));
  page.on("console", message => {
    if (message.type() === "error") report.consoleErrors.push({ text: message.text(), url: message.location().url });
  });
  page.on("requestfailed", request => {
    const canceledApi = new URL(request.url()).pathname.startsWith("/api/") && request.failure()?.errorText === "net::ERR_ABORTED";
    if (!canceledApi) report.assetFailures.push(`${request.url()}: ${request.failure()?.errorText}`);
  });
  page.on("response", response => {
    if (response.headers()["x-meridian-dev-fixture"] === "true") fixtureServer = true;
    if (response.status() < 400) return;
    pendingResponses.push((async () => {
      const url = new URL(response.url());
      const body = await response.json().catch(() => null);
      const declaredFixtureGap = body?.mode === "fixture-only" && body?.title === "Development fixture unavailable";
      const baselineFixtureGap = fixtureServer && (knownFixtureGaps.has(url.pathname) || /^\/api\/ledger\/close-management\/period-plan\/[^/]+$/.test(url.pathname));
      if (url.pathname.startsWith("/api/") && response.status() === 501 && (declaredFixtureGap || baselineFixtureGap)) {
        report.fixtureLimitations.push({ method: response.request().method(), path: url.pathname });
      } else report.assetFailures.push(`HTTP ${response.status()}: ${response.url()}`);
    })());
  });
  page.on("request", request => {
    if (new URL(request.url()).origin !== origin) report.assetFailures.push(`Unexpected remote request: ${request.url()}`);
  });
}
async function ready(page, workspace) {
  const heading = workspace === "Settings" && page.url().includes("/settings/preferences") ? "Preferences" : workspace === "Accounting" && page.url().includes("/accounting/reconciliation") ? "Reconciliation" : workspace;
  await page.getByRole("heading", { name: heading, exact: true }).first().waitFor();
  await page.waitForLoadState("networkidle");
  await Promise.all(pendingResponses);
  await page.waitForFunction(() => ![...document.querySelectorAll("main h1, main h2")].some(element => /^Loading /.test(element.textContent || "")));
  assert.equal(await page.locator(".workstation-frame").count(), 1, "The real workstation shell is rendered");
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, `${workspace}: page-level horizontal overflow`);
  assert.equal(await page.locator("vite-error-overlay").count(), 0, "No Vite overlay");
}
async function tokens(page, expectedTheme) {
  await page.waitForFunction(() => {
    const icons = [...document.querySelectorAll(".operator-nav-item__asset")];
    return icons.length === 7 && icons.every(element => {
      const style = getComputedStyle(element);
      const link = element.closest("a");
      return style.maskImage !== "none" && style.maskImage !== "" && link && style.backgroundColor === getComputedStyle(link).color;
    });
  });
  const actual = await page.evaluate(() => {
    const style = getComputedStyle(document.documentElement);
    const read = name => style.getPropertyValue(name).trim().toUpperCase();
    return { canvas: read("--ws-page-bg"), panel: read("--ws-surface"), border: read("--ws-border"), signal: read("--brand-signal"), axis: read("--brand-axis"), focus: read("--interaction-focus") };
  });
  assert.deepEqual(actual, themes[expectedTheme], `${expectedTheme}: computed 4.3 roles`);
  const icons = await page.locator(".operator-nav-item__asset").evaluateAll(elements => elements.map(element => {
    const style = getComputedStyle(element);
    return { mask: style.maskImage, fill: style.backgroundColor, rowColor: getComputedStyle(element.closest("a")).color };
  }));
  assert.equal(icons.length, 7, "Seven workspace icon masks are rendered");
  assert(icons.every(icon => icon.mask !== "none" && icon.mask !== ""), "Canonical SVG masks are declared; network monitoring rejects missing assets");
  assert(icons.every(icon => icon.fill === icon.rowColor), "Workspace masks inherit each navigation row's currentColor in manual and automatic appearances");
  return actual;
}
async function fonts(page) {
  const faces = await page.evaluate(async () => {
    const loadedFaces = [];
    for (const family of ["IBM Plex Sans", "IBM Plex Mono"]) {
      for (const face of ["400", "italic 400", "500", "600", "700"]) {
        const loaded = await document.fonts.load(face + ' 14px "' + family + '"', "Meridian 0123456789");
        loadedFaces.push({ family, face, count: loaded.length, ready: loaded.every(item => item.status === "loaded") });
      }
    }
    await document.fonts.ready;
    return {
      body: getComputedStyle(document.body).fontFamily,
      loaded: [...document.fonts].filter(face => face.status === "loaded").map(face => face.family.replace(/["']/g, "")),
      faces: loadedFaces
    };
  });
  assert.match(faces.body, /IBM Plex Sans/);
  assert(faces.faces.every(face => face.count > 0 && face.ready), "All ten bundled Plex faces actually load");
  return faces;
}
async function screenshot(page, name) {
  const filename = `${name}.png`;
  await page.screenshot({ path: path.join(output, filename), fullPage: false });
  return filename;
}
async function scan(page, label) {
  // Each appearance scene gets a fresh document and axe engine. Reproduce the
  // same public interaction so no prior scan or transition can supply stale styles.
  const audit = await browser.newPage({ storageState: await page.context().storageState(), viewport: page.viewportSize(), reducedMotion: "reduce" });
  monitor(audit);
  try {
    await audit.emulateMedia({ colorScheme: await page.evaluate(() => matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light"), reducedMotion: "reduce" });
    await audit.goto(page.url());
    if (new URL(page.url()).pathname.endsWith("/portfolio")) {
      await audit.getByRole("heading", { name: "Portfolio Explorer", exact: true }).waitFor();
      await ready(audit, "Portfolio");
      const record = audit.getByRole("row", { name: /MSFT/ }).first();
      await record.focus(); await audit.keyboard.press("Enter");
      await audit.getByRole("dialog", { name: "MSFT proof detail", exact: true }).waitFor();
      await audit.waitForFunction(() => document.querySelector("[role=dialog]")?.textContent.includes("$6,747.20"));
    } else {
      await audit.getByRole("region", { name: "Appearance preferences", exact: true }).waitFor();
      await ready(audit, "Settings");
    }
    await fonts(audit);
    await audit.addScriptTag({ content: axeSource });
    const result = await audit.evaluate(async () => await window.axe.run(document, {
      runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] }
    }));
    report.accessibility.push({ label, passes: result.passes.length, incomplete: result.incomplete.map(item => item.id), violations: result.violations.map(item => ({ id: item.id, impact: item.impact, nodes: item.nodes.map(node => ({ target: node.target, summary: node.failureSummary })) })) });
    await screenshot(audit, `axe-${label.toLowerCase().replace(/[^a-z0-9]+/g, "-")}`);
  } finally {
    await Promise.all(pendingResponses);
    await audit.close();
  }
}

async function nativeAccounting(page) {
  await page.goto(`${base}/accounting/reconciliation`);
  await ready(page, "Accounting");
  await page.evaluate(() => document.documentElement.setAttribute("data-theme", "dark"));
  const reference = page.locator(".accounting-reference-panel[data-appearance='light']").first();
  await reference.waitFor({ state: "attached" });
  const details = reference.locator("xpath=ancestor::details[1]");
  if (await details.count() && await details.getAttribute("open") === null) await details.locator("summary").first().click();
  await reference.waitFor();
  const nested = await reference.evaluate(element => {
    const style = getComputedStyle(element);
    return Object.fromEntries(["--green", "--purple", "--ws-disabled-bg", "--ws-disabled-fg", "--ws-disabled-border"].map(name => [name, style.getPropertyValue(name).trim().toUpperCase()]));
  });
  assert.deepEqual(nested, { "--green": "#2E7655", "--purple": "#2F648A", "--ws-disabled-bg": "#EBE8E1", "--ws-disabled-fg": "#606870", "--ws-disabled-border": "#6D757D" }, "Nested light reference resets semantic and disabled colors under dark appearance");
  report.nestedLightReference = nested;
  // Posted-book fixtures require a backend. Render the actual native source modules
  // with declared synthetic rows under this workstation's real CSS instead; this
  // verifies visual integration without claiming posted-book workflow validation.
  await page.evaluate(async () => {
    const resources = performance.getEntriesByType("resource").map(entry => entry.name);
    const reactUrl = resources.find(url => /\/react\.js(?:\?|$)/.test(url));
    const clientUrl = resources.find(url => /\/react-dom_client\.js(?:\?|$)/.test(url));
    if (!reactUrl || !clientUrl) throw new Error("Vite React module URLs unavailable; use the fixture-only dev server for native component checks");
    const [reactModule, clientModule, { TrialBalanceTable }, { LedgerTable }] = await Promise.all([
      import(reactUrl), import(clientUrl), import("/workstation/src/components/accounting/TrialBalanceTable.tsx"), import("/workstation/src/components/accounting/LedgerTable.tsx")
    ]);
    const React = reactModule.default || reactModule;
    const createRoot = clientModule.createRoot || clientModule.default?.createRoot;
    if (typeof createRoot !== "function") throw new Error("React DOM client does not expose createRoot");
    const host = document.createElement("section"); host.id = "design-system-accounting-fixture";
    host.setAttribute("aria-label", "Synthetic native accounting component test");
    Object.assign(host.style, { position: "fixed", inset: "110px 24px 36px 248px", zIndex: "2147483640", padding: "16px", overflow: "auto", background: "var(--bg)", color: "var(--text-primary)" });
    document.body.appendChild(host);
    const row = (id, accountType, balance) => ({ rowId: id, accountLabel: `Synthetic ${id}`, accountTypeLabel: accountType, accountType, basisLabel: "Accrual", basisTone: "default", policyLabel: "GAAP", dimensionLabel: "Fixture", balance, balanceLabel: String(balance), balanceTone: "default", entryCount: 1, entryCountLabel: "1", ariaLabel: `Synthetic ${id}`, selectAriaLabel: `Select synthetic ${id}`, detailPanelId: `synthetic-${id}-detail`, isExpanded: false, financialAccountId: `synthetic-${id}`, symbol: null, security: null });
    const root = createRoot(host);
    root.render(React.createElement(React.Fragment, null,
      React.createElement("h2", null, "Synthetic native accounting components"),
      React.createElement("p", null, "Production modules and styles; synthetic balanced rows; no posting or persistence."),
      React.createElement(TrialBalanceTable, { rows: [row("cash", "Asset", 100), row("payable", "Liability", 100)], selectedRowId: "cash", currency: "USD" }),
      React.createElement(LedgerTable, { rows: [{ date: "2026-11-11", ref: "SYN-01", memo: "Synthetic debit", debit: 100, credit: 0 }, { date: "2026-11-11", ref: "SYN-02", memo: "Synthetic credit", debit: 0, credit: 100 }], currency: "USD", opening: 0 })
    ));
    window.__dsAccountingFixture = { root, host };
  });
  await page.locator("#design-system-accounting-fixture .tbl-wrap").waitFor();
  await page.locator("#design-system-accounting-fixture .ldg-wrap").waitFor();
  report.nativeAccounting = { scope: "Production source components under actual dashboard CSS; synthetic rows, no backend/posting claim", themes: [] };
  for (const theme of ["light", "dark"]) {
    await page.evaluate(theme => document.documentElement.setAttribute("data-theme", theme), theme);
    const expected = theme === "light" ? "rgb(210, 205, 195)" : "rgb(44, 54, 64)";
    await page.waitForFunction(expected => [...document.querySelectorAll("#design-system-accounting-fixture .tbl-wrap, #design-system-accounting-fixture .ldg-wrap")].every(element => getComputedStyle(element).borderTopColor === expected), expected);
    const borders = await page.locator("#design-system-accounting-fixture .tbl-wrap, #design-system-accounting-fixture .ldg-wrap").evaluateAll(elements => elements.map(element => {
      const style = getComputedStyle(element); return { width: style.borderTopWidth, style: style.borderTopStyle, color: style.borderTopColor };
    }));
    assert.deepEqual(borders, [{ width: "1px", style: "solid", color: expected }, { width: "1px", style: "solid", color: expected }], `${theme}: native accounting hairlines resolve raw color tokens`);
    await page.waitForFunction(expected => getComputedStyle(document.querySelector("#design-system-accounting-fixture .tbl-wrap tr[aria-selected='true'] td")).backgroundColor === expected, theme === "light" ? "rgb(231, 237, 242)" : "rgb(44, 64, 84)");
    const selected = await page.locator("#design-system-accounting-fixture .tbl-wrap tr[aria-selected='true'] td").first().evaluate(element => getComputedStyle(element).backgroundColor);
    assert.equal(selected, theme === "light" ? "rgb(231, 237, 242)" : "rgb(44, 64, 84)", `${theme}: selection uses blue, independently of signal`);
    report.nativeAccounting.themes.push({ theme, borders, selected, screenshot: await screenshot(page, `after-native-accounting-${theme}`) });
  }
  await page.evaluate(() => { window.__dsAccountingFixture.root.unmount(); window.__dsAccountingFixture.host.remove(); delete window.__dsAccountingFixture; });
}

try {
  // Six representative desktop render cases exercise the actual production token bridge.
  for (const theme of ["light", "dark"]) {
    for (const [width, height] of [[1920, 1080], [1440, 900], [1366, 768]]) {
      const page = await browser.newPage({ viewport: { width, height }, colorScheme: theme, reducedMotion: "reduce" });
      monitor(page);
      await page.addInitScript(({ key, theme }) => localStorage.setItem(key, theme), { key: appearanceKey, theme });
      await page.goto(`${base}/trading`);
      await page.getByRole("button", { name: "Open strategy controls panel", exact: true }).waitFor();
      await ready(page, "Trading");
      const computed = await tokens(page, theme);
      const loadedFonts = await fonts(page);
      const emptyAction = page.getByRole("button", { name: "Choose account", exact: true });
      const emptyInk = await emptyAction.evaluate(element => {
        const probe = document.createElement("span"); probe.style.color = "var(--brand-axis)"; document.body.appendChild(probe);
        const expected = getComputedStyle(probe).color; probe.remove();
        return { actual: getComputedStyle(element).backgroundColor, expected };
      });
      assert.equal(emptyInk.actual, emptyInk.expected, "Empty-state actions use structural ink rather than brand signal");
      const tradingScreenshot = await screenshot(page, `after-${theme}-${width}`);
      await page.goto(`${base}/accounting`);
      await ready(page, "Accounting");
      const primary = page.locator("main .mds-btn--primary:visible:not(:disabled)").first();
      await primary.waitFor();
      const normal = await primary.evaluate(element => ({ background: getComputedStyle(element).backgroundColor, radius: getComputedStyle(element).borderRadius }));
      const expectedInk = await page.evaluate(() => {
        const element = document.createElement("span"); element.style.color = "var(--brand-axis)"; document.body.appendChild(element);
        const value = getComputedStyle(element).color; element.remove(); return value;
      });
      assert.equal(normal.background, expectedInk, "Primary button uses structural ink");
      assert.equal(normal.radius, "4px", "4.3 control radius");
      await primary.hover();
      await page.waitForFunction(element => {
        const probe = document.createElement("span"); probe.style.color = "color-mix(in srgb, var(--brand-axis) 88%, var(--bg-light))"; document.body.appendChild(probe);
        const expected = getComputedStyle(probe).color; probe.remove(); return getComputedStyle(element).backgroundColor === expected;
      }, await primary.elementHandle());
      await primary.focus(); await page.keyboard.press("Tab"); await page.keyboard.press("Shift+Tab");
      const focus = await primary.evaluate(element => {
        const style = getComputedStyle(element);
        const probe = document.createElement("span"); probe.style.color = "var(--interaction-focus)"; document.body.appendChild(probe);
        const color = getComputedStyle(probe).color; probe.remove();
        return { visible: element.matches(":focus-visible"), blue: style.boxShadow.includes(color) || style.outlineColor === color, shadow: style.boxShadow };
      });
      assert(focus.visible && focus.blue, "Keyboard focus uses independent interaction blue");
      await page.mouse.move(width - 1, height - 1);
      report.checks.push({ case: `render-${theme}-${width}`, computed, loadedFonts, screenshot: tradingScreenshot, controlsScreenshot: await screenshot(page, `after-accounting-${theme}-${width}`) });
      await Promise.all(pendingResponses);
      await page.close();
    }
  }
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 }, reducedMotion: "reduce" });
  monitor(page);
  await page.goto(`${base}/trading`);
  await page.getByRole("button", { name: "Open strategy controls panel", exact: true }).waitFor();
  for (const workspace of workspaces) {
    if (workspace !== "Trading") await page.locator(`nav a[href="/workstation/${workspace.toLowerCase()}"]`).first().click();
    await page.waitForURL(new RegExp(`/workstation/${workspace.toLowerCase()}(?:[/?#]|$)`));
    await ready(page, workspace);
    report.checks.push({ case: `navigation-${workspace}`, title: await page.title(), tableRows: await page.locator("main tbody tr").count(), screenshot: await screenshot(page, `after-${workspace.toLowerCase()}-1440`) });
  }
  await page.goto(`${base}/portfolio`);
  await page.getByRole("heading", { name: "Portfolio Explorer", exact: true }).waitFor(); await ready(page, "Portfolio");
  const record = page.getByRole("row", { name: /MSFT/ }).first();
  await record.focus(); await page.keyboard.press("Enter");
  const proof = page.getByRole("dialog", { name: "MSFT proof detail", exact: true });
  await proof.waitFor();
  await page.waitForFunction(() => document.querySelector("[role=dialog]")?.textContent.includes("$6,747.20"));
  assert.match(await proof.innerText(), /Review required/);
  const proofScreenshot = await screenshot(page, "after-portfolio-proof-1440");
  await scan(page, "Portfolio proof detail, light");
  await page.keyboard.press("Escape"); await proof.waitFor({ state: "hidden" });
  assert(await record.evaluate(element => element === document.activeElement), "Escape restores the selected record's focus");
  report.checks.push({ case: "portfolio-proof-keyboard", screenshot: proofScreenshot });
  await page.goto(`${base}/settings/preferences`);
  await page.getByRole("region", { name: "Appearance preferences", exact: true }).waitFor(); await ready(page, "Settings");
  const appearance = page.getByRole("radiogroup", { name: "Appearance", exact: true });
  for (const [os, manual, expected] of [["light", "System", "light"], ["dark", "System", "dark"], ["light", "Dark", "dark"], ["dark", "Light", "light"]]) {
    await page.emulateMedia({ colorScheme: os });
    await appearance.getByRole("radio", { name: manual, exact: true }).focus(); await page.keyboard.press("Space");
    await page.waitForFunction(({ key, manual }) => localStorage.getItem(key) === manual.toLowerCase(), { key: appearanceKey, manual });
    const computed = await tokens(page, expected);
    report.checks.push({ case: `appearance-os-${os}-manual-${manual}`, computed });
  }
  await page.reload(); await page.getByRole("region", { name: "Appearance preferences", exact: true }).waitFor();
  assert.equal(await page.locator("html").getAttribute("data-theme"), "light", "Manual light survives reload under a dark OS");
  const density = page.getByRole("radiogroup", { name: "Display density", exact: true });
  for (const [label, row, font] of [["Compact", "28px", "13px"], ["Default", "36px", "14px"], ["Spacious", "44px", "15px"]]) {
    await density.getByRole("radio", { name: label, exact: true }).focus(); await page.keyboard.press("Space");
    await page.waitForFunction(({ row, font }) => getComputedStyle(document.body).getPropertyValue("--theme-row-height").trim() === row && getComputedStyle(document.body).fontSize === font, { row, font });
    report.checks.push({ case: `density-${label}`, row, font });
  }
  await density.getByRole("radio", { name: "Default", exact: true }).click();
  await scan(page, "Settings appearance, explicit light under dark OS");
  await appearance.getByRole("radio", { name: "Dark", exact: true }).click();
  await scan(page, "Settings appearance, explicit dark");
  await appearance.getByRole("radio", { name: "Light", exact: true }).click();
  await page.keyboard.press("Control+k");
  const search = page.getByRole("searchbox", { name: "Search command palette", exact: true });
  await search.waitFor(); assert(await search.evaluate(element => element === document.activeElement));
  await search.fill("portfolio"); await page.keyboard.press("ArrowDown");
  assert.equal(await search.evaluate(element => element === document.activeElement), false, "ArrowDown moves into commands");
  await page.keyboard.press("Escape"); await search.waitFor({ state: "hidden" });
  report.checks.push({ case: "command-palette-keyboard", screenshot: await screenshot(page, "after-preferences-1440") });
  await nativeAccounting(page);
  await page.close();
  await Promise.all(pendingResponses);
  report.fixtureLimitations = [...new Map(report.fixtureLimitations.map(item => [`${item.method} ${item.path}`, item])).values()];
  const expected501s = report.consoleErrors.filter(item => item.text.includes("501 (Not Implemented)"));
  report.unexpectedConsoleErrors = report.consoleErrors.filter(item => !expected501s.includes(item));
  report.accessibilityViolations = report.accessibility.reduce((sum, result) => sum + result.violations.length, 0);
  assert.equal(report.checks.length, 22, "All twenty-two workstation cases ran");
  assert.deepEqual(report.exceptions, [], "No application exceptions");
  assert.deepEqual(report.assetFailures, [], "No missing/failed assets or unexpected API errors");
  assert.deepEqual(report.unexpectedConsoleErrors, [], "No unexpected console errors");
  // Full-page axe findings are reported for audit. They are not silently relabeled
  // as a conformance pass, and do not replace scoped keyboard/contrast checks.
  if (profileOptions.expanded) {
    await runExpandedDesignSystemSmoke({
      browser, base, output, sourceRoot, report, axeSource,
      backendBase: profileOptions.backendBase, productionBase: profileOptions.productionBase,
      timeoutMs: profileOptions.timeoutMs
    });
  }
  report.accessibilityViolations = report.accessibility.reduce((sum, result) => sum + result.violations.length, 0);
  report.result = report.accessibilityViolations ? "FUNCTIONAL PASS; ACCESSIBILITY FINDINGS" : "PASS";
} catch (error) {
  report.result = "FAIL"; report.failure = error.stack || String(error); process.exitCode = 1;
} finally {
  await browser.close();
  report.accessibilityViolations = report.accessibility.reduce((sum, result) => sum + result.violations.length, 0);
  await writeFile(path.join(output, "report.json"), JSON.stringify(report, null, 2) + "\n");
}
console.log(JSON.stringify({ result: report.result, cases: report.checks.length, expandedCases: report.expanded?.length ?? 0, accessibilityViolations: report.accessibilityViolations, fixtureLimitations: report.fixtureLimitations.length, report: path.join(output, "report.json"), failure: report.failure }));
