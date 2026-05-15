import { chromium } from "playwright";
import path from "node:path";
try {
  const port = process.env.MERIDIAN_WATCHLIST_SMOKE_PORT;
  const artifactDir = process.env.MERIDIAN_WATCHLIST_ARTIFACT_DIR;
  const baseUrl = `http://127.0.0.1:${port}`;
  const consoleErrors = [];
  const pageErrors = [];
  const symbols = [
    { symbol: "MSFT", status: "Monitored", provider: null, lastEventAt: null, eventCount: 0, hasHistoricalData: false },
    { symbol: "AAPL", status: "Active", provider: "Polygon", lastEventAt: "2026-05-09T01:00:00.000Z", eventCount: 1200, hasHistoricalData: true }
  ];
  const stats = { totalSymbols: 2, monitoredSymbols: 1, archivedSymbols: 0, symbolsWithErrors: 0, totalEventsLast24h: 1200 };
  const snapshot = { timestamp: "2026-05-09T01:00:01.000Z", count: 2, quotes: [] };
  async function installRoutes(page) {
    await page.route("**/api/symbols/statistics", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(stats) }));
    await page.route("**/api/symbols", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(symbols) }));
    await page.route("**/api/data/quotes-snapshot**", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(snapshot) }));
  }
  async function smokeViewport(browser, width, height, name) {
    const page = await browser.newPage({ viewport: { width, height } });
    await installRoutes(page);
    page.on("console", msg => { if (["error", "warning"].includes(msg.type())) consoleErrors.push(`${name}:${msg.type()}:${msg.text()}`); });
    page.on("pageerror", error => pageErrors.push(`${name}:${error.message}`));
    await page.goto(`${baseUrl}/workstation/data/watchlist`, { waitUntil: "domcontentloaded" });
    await page.getByRole("table", { name: /subscribed symbol watchlist/i }).waitFor({ timeout: 20000 });
    const input = page.getByRole("textbox", { name: "Add symbol" });
    const attrs = await input.evaluate(el => ({ invalid: el.getAttribute("aria-invalid"), errorMessage: el.getAttribute("aria-errormessage"), describedBy: el.getAttribute("aria-describedby") }));
    if (attrs.invalid !== null || attrs.errorMessage !== null || attrs.describedBy !== "add-symbol-help") throw new Error(`Unexpected idle input attributes for ${name}: ${JSON.stringify(attrs)}`);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth);
    if (overflow) throw new Error(`Horizontal overflow detected for ${name}`);
    await page.screenshot({ path: path.join(artifactDir, `watchlist-add-field-clean-${name}.png`), fullPage: true });
    await page.close();
  }
  const browser = await chromium.launch({ channel: "msedge" });
  try {
    await smokeViewport(browser, 1440, 1000, "desktop");
    await smokeViewport(browser, 390, 844, "mobile");
  } finally {
    await browser.close();
  }
  if (consoleErrors.length || pageErrors.length) throw new Error(`Console/page errors: ${[...consoleErrors, ...pageErrors].join("\n")}`);
  console.log("watchlist clean smoke passed");
} catch (error) {
  console.error("SMOKE_ERROR", error && error.stack ? error.stack : error);
  process.exit(1);
}
