import { createRequire } from "node:module";
import path from "node:path";
import { pathToFileURL } from "node:url";

/** Load the same TypeScript registry used by Vite previews and Vitest. */
export async function loadScreenshotScenarioModule(dashboardDir) {
  const dashboardRequire = createRequire(path.join(dashboardDir, "package.json"));
  let server;
  try {
    const { createServer } = await import(pathToFileURL(dashboardRequire.resolve("vite")).href);
    server = await createServer({
      root: dashboardDir,
      configFile: false,
      server: { middlewareMode: true, hmr: false, ws: false },
      optimizeDeps: { noDiscovery: true, include: [] },
      resolve: { alias: { "@": path.join(dashboardDir, "src") } },
      appType: "custom"
    });
    const module = await server.ssrLoadModule("/src/scenarios/index.ts");
    return { module, close: () => server.close() };
  } catch (cause) {
    await server?.close();
    throw new Error(`Could not load screenshot scenarios from ${dashboardDir}/src/scenarios/index.ts: ${cause.message}`, { cause });
  }
}

/**
 * Scenario overrides win over JSON fixtures. Legacy fixture lookups are exact
 * GET paths: a fixture for an index must never hide an unregistered child or write.
 */
export function createScreenshotApiResolver(module, fixtureRoutes, { scenarioId, strict = false } = {}) {
  const scenario = module.getScenario(scenarioId ?? "accounting.normal");
  const runtime = module.createScenarioRuntime(scenario, { strict });
  return {
    scenario,
    resolve(url, method = "GET") {
      const pathname = new URL(url, "http://meridian.local").pathname;
      const hasScenarioOverride = scenario.responses.some((route) =>
        new URL(route.path, "http://meridian.local").pathname === pathname
      );
      if (scenarioId || hasScenarioOverride) {
        const response = module.resolveScenarioResponse(scenario, url, method);
        if (response) return response;
      }
      if (method.toUpperCase() === "GET" && Object.hasOwn(fixtureRoutes, pathname)) {
        return { status: 200, body: fixtureRoutes[pathname] };
      }
      // This also resolves explicitly registered shared supporting fixtures.
      // Strict runtimes retain unexpected requests even when the UI catches them.
      const response = runtime.resolve(url, method);
      if (!response) runtime.assertNoUnexpectedRequests();
      return response ?? { status: 404, body: {} };
    },
    assertNoUnexpectedRequests: () => runtime.assertNoUnexpectedRequests(),
    unexpectedRequests: runtime.unexpectedRequests
  };
}

/** Add the URL selector and readiness contract for the requested scenario. */
export function scenarioCapture(capture, scenario, module) {
  const url = new URL(capture.path, "http://meridian.local");
  url.searchParams.set("scenario", scenario.id);
  const sharedResponse = module.resolveScenarioResponse(scenario, "/api/workstation/accounting");
  const payload = sharedResponse?.body;
  const fallbackTexts = sharedResponse?.status >= 400
    ? [payload?.detail ?? payload?.title]
    : scenario.id.endsWith(".empty")
      ? ["Reconciliation Casework", "No reconciliation runs are available for this accounting scope."]
      : ["Reconciliation Casework", "Open breaks", payload?.reconciliationQueue?.at(-1)?.strategyName];
  return {
    ...capture,
    path: `${url.pathname}${url.search}${url.hash}`,
    name: `${capture.name}-${scenario.id.replaceAll(".", "-")}`,
    scenario: scenario.id,
    previewUrl: module.scenarioPreviewUrl(scenario),
    waitForText: undefined,
    waitForTexts: scenario.waitForTexts ?? fallbackTexts.filter(Boolean),
    waitForAbsentTexts: scenario.waitForAbsentTexts ?? [],
    waitForSelectors: []
  };
}

/** Install the response adapter before navigation, reporting failures to capture. */
export async function setupScenarioApiMocking(page, resolver, onError) {
  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    // Vite source imports such as /workstation/src/lib/api/*.ts are not APIs.
    if (!url.pathname.startsWith("/api/")) return route.continue();
    try {
      const response = resolver.resolve(request.url(), request.method());
      if (response.delayMs) await new Promise((resolve) => setTimeout(resolve, response.delayMs));
      await route.fulfill({
        status: response.status,
        contentType: "application/json",
        headers: response.headers,
        body: JSON.stringify(response.body)
      });
    } catch (error) {
      onError(`API scenario failure for ${request.method()} ${url.pathname}${url.search}: ${error.message}`);
      await route.fulfill({
        status: 500,
        contentType: "application/json",
        headers: { "x-meridian-scenario": resolver.scenario.id },
        body: JSON.stringify({ title: "Screenshot scenario request failed", detail: error.message, status: 500 })
      }).catch(() => undefined);
    }
  });
}
