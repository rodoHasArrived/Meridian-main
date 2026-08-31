#!/usr/bin/env node

import { spawn } from "node:child_process";
import { createRequire } from "node:module";
import fs from "node:fs/promises";
import path from "node:path";
import process from "node:process";

const repoMarkers = ["Meridian.sln", ".git"];

function parseArgs(argv) {
  const values = new Map();
  const valueLists = new Map();
  const flags = new Set();
  const recordValue = (name, value) => {
    values.set(name, value);
    valueLists.set(name, [...(valueLists.get(name) ?? []), value]);
  };

  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (!arg.startsWith("--")) {
      throw new Error(`Unexpected argument: ${arg}`);
    }

    const eqIndex = arg.indexOf("=");
    if (eqIndex > 0) {
      recordValue(arg.slice(2, eqIndex), arg.slice(eqIndex + 1));
      continue;
    }

    const name = arg.slice(2);
    const next = argv[index + 1];
    if (next && !next.startsWith("--")) {
      recordValue(name, next);
      index += 1;
    } else {
      flags.add(name);
    }
  }

  return { values, valueLists, flags };
}

async function pathExists(candidate) {
  try {
    await fs.access(candidate);
    return true;
  } catch {
    return false;
  }
}

async function findRepoRoot(startDir) {
  let current = path.resolve(startDir);
  while (true) {
    for (const marker of repoMarkers) {
      if (await pathExists(path.join(current, marker))) {
        return current;
      }
    }

    const parent = path.dirname(current);
    if (parent === current) {
      throw new Error(`Could not locate Meridian repo root from ${startDir}`);
    }

    current = parent;
  }
}

function readJson(filePath) {
  return fs.readFile(filePath, "utf8").then((content) => JSON.parse(content));
}

function resolveRepoPath(repoRoot, inputPath) {
  return path.isAbsolute(inputPath) ? inputPath : path.join(repoRoot, inputPath);
}

function normalizeBaseUrl(url) {
  return url.replace(/\/+$/, "");
}

function toRouteUrl(baseUrl, routePath) {
  const normalizedPath = routePath.startsWith("/") ? routePath : `/${routePath}`;
  return `${normalizeBaseUrl(baseUrl)}${normalizedPath}`;
}

function collectCaptureSelectors(valueLists) {
  const selectors = [
    ...(valueLists.get("capture") ?? []),
    ...(valueLists.get("captures") ?? []),
    ...(valueLists.get("capture-id") ?? []),
    ...(valueLists.get("capture-name") ?? [])
  ];

  return selectors
    .flatMap((value) => String(value).split(","))
    .map((value) => value.trim().toLowerCase())
    .filter(Boolean);
}

function captureMatchesSelector(capture, selector) {
  const candidates = [
    capture.id,
    capture.name,
    capture.path,
    screenshotCoveragePath(capture.path ?? ""),
    capture.docLabel
  ]
    .filter((value) => typeof value === "string")
    .map((value) => value.trim().toLowerCase());

  return candidates.includes(selector);
}

function selectCaptures(captures, selectors) {
  if (selectors.length === 0) {
    return captures;
  }

  const selected = captures.filter((capture) =>
    selectors.some((selector) => captureMatchesSelector(capture, selector))
  );

  if (selected.length === 0) {
    throw new Error(`No web screenshot captures matched selector(s): ${selectors.join(", ")}`);
  }

  return selected;
}

function npmInvocation(args) {
  if (process.platform === "win32") {
    return {
      command: "cmd.exe",
      args: ["/d", "/s", "/c", "npm.cmd", ...args]
    };
  }

  return {
    command: "npm",
    args
  };
}

function appendLog(logs, prefix, chunk) {
  const text = chunk.toString();
  logs.push(...text.split(/\r?\n/).filter(Boolean).map((line) => `${prefix}${line}`));
}

async function waitForServer(url, timeoutMs, child, logs) {
  const started = Date.now();
  let lastError = "";
  let serverExit = null;

  // Fail fast if the dev server process dies before it ever answers (port
  // conflict, Vite config error, OOM) instead of polling for the full timeout
  // and then reporting a generic "timed out" message that hides the real cause.
  const noteExit = (code, signal) => {
    serverExit = `exit code ${code ?? "null"}${signal ? `, signal ${signal}` : ""}`;
  };
  if (child) {
    if (child.exitCode !== null || child.signalCode !== null) {
      noteExit(child.exitCode, child.signalCode);
    } else {
      child.once("exit", noteExit);
    }
  }

  const describeServerExit = () => {
    const recentLogs = Array.isArray(logs) ? logs.slice(-20).join("\n") : "";
    const detail = recentLogs ? `\n${recentLogs}` : "";
    return `Dev server exited before becoming ready (${serverExit}).${detail}`;
  };

  while (Date.now() - started < timeoutMs) {
    if (serverExit) {
      throw new Error(describeServerExit());
    }

    try {
      const response = await fetch(url, { redirect: "manual" });
      if (response.status >= 200 && response.status < 500) {
        return;
      }

      lastError = `HTTP ${response.status}`;
    } catch (error) {
      lastError = error instanceof Error ? error.message : String(error);
    }

    await new Promise((resolve) => setTimeout(resolve, 500));
  }

  if (serverExit) {
    throw new Error(describeServerExit());
  }
  throw new Error(`Timed out waiting for ${url}: ${lastError}`);
}

function startViteServer(dashboardDir, host, port, logs) {
  const invocation = npmInvocation(["run", "dev", "--", "--host", host, "--port", String(port), "--strictPort"]);
  const child = spawn(
    invocation.command,
    invocation.args,
    {
      cwd: dashboardDir,
      env: {
        ...process.env,
        BROWSER: "none",
        MERIDIAN_SCREENSHOT_CAPTURE: "true",
        MERIDIAN_API_BASE_URL: process.env.MERIDIAN_API_BASE_URL ?? "http://127.0.0.1:8080"
      },
      stdio: ["ignore", "pipe", "pipe"],
      detached: process.platform !== "win32"
    }
  );

  child.stdout.on("data", (chunk) => appendLog(logs, "", chunk));
  child.stderr.on("data", (chunk) => appendLog(logs, "stderr: ", chunk));

  return child;
}

async function stopProcess(child) {
  if (!child || child.exitCode !== null) {
    return;
  }

  const waitForExit = (timeoutMs) =>
    new Promise((resolve) => {
      const timer = setTimeout(() => resolve(false), timeoutMs);
      child.once("exit", () => {
        clearTimeout(timer);
        resolve(true);
      });
    });

  const killUnixGroup = (signal) => {
    try {
      process.kill(-child.pid, signal);
      return true;
    } catch {
      return false;
    }
  };

  if (process.platform === "win32") {
    const taskkill = spawn("taskkill", ["/pid", String(child.pid), "/t", "/f"], { stdio: "ignore" });
    const taskkillExited = await new Promise((resolve) => {
      const timer = setTimeout(() => resolve(false), 5000);
      taskkill.once("exit", () => {
        clearTimeout(timer);
        resolve(true);
      });
    });
    if (!taskkillExited) {
      taskkill.kill("SIGKILL");
    }
    await waitForExit(5000);
    return;
  }

  if (!killUnixGroup("SIGTERM")) {
    child.kill("SIGTERM");
  }

  const exited = await waitForExit(5000);
  if (exited) {
    return;
  }

  if (!killUnixGroup("SIGKILL")) {
    child.kill("SIGKILL");
  }
  await waitForExit(2000);
}

/**
 * Register Playwright API route mocks for all entries in the fixtures map.
 * Intercepts /api/** requests in the browser so screenshots never depend on a
 * running Meridian backend. Must be called before the first page.goto().
 *
 * @param {import('playwright').Page} page
 * @param {Record<string, unknown>} fixtureRoutes  Path → JSON body map from web-screenshot-fixtures.json
 */
async function setupApiMocking(page, fixtureRoutes) {
  await page.route("**/api/**", (route) => {
    const url = new URL(route.request().url());
    const pathname = url.pathname;

    // Vite source modules can live under paths such as
    // /workstation/src/lib/api/*.ts. Let those module requests pass through;
    // only root API calls should be answered by screenshot fixtures.
    if (!pathname.startsWith("/api/")) {
      return route.continue();
    }

    // Exact-path match first (strips query string for lookup).
    if (Object.prototype.hasOwnProperty.call(fixtureRoutes, pathname)) {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(fixtureRoutes[pathname])
      });
    }

    // Prefix match for parameterised routes (e.g. /api/portfolio/household?provider=alpaca).
    const prefixEntry = Object.entries(fixtureRoutes).find(([key]) => pathname.startsWith(key));
    if (prefixEntry) {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(prefixEntry[1])
      });
    }

    // Unregistered endpoints return empty 404 so the dev-fixture fallback can
    // still handle them inside the browser if needed.
    return route.fulfill({ status: 404, contentType: "application/json", body: "{}" });
  });
}

function collectRequiredFixtureRoutes(captures) {
  const required = new Set();
  for (const capture of captures) {
    const configured = Array.isArray(capture.requiredApiRoutes) ? capture.requiredApiRoutes : [];
    for (const route of configured) {
      if (typeof route === "string" && route.trim().length > 0) {
        required.add(route.trim());
      }
    }
  }
  return [...required];
}

function findMissingFixtureRoutes(requiredRoutes, fixtureRoutes) {
  const availableRoutes = Object.keys(fixtureRoutes);
  return requiredRoutes.filter(
    (requiredRoute) => !availableRoutes.some((candidate) => candidate === requiredRoute || requiredRoute.startsWith(`${candidate}/`))
  );
}

function extractWorkstationRouteCatalog(source, filePath) {
  const match = source.match(/export const WORKSTATION_ROUTE_CATALOG = \{(?<body>[\s\S]*?)\} as const;/);
  if (!match?.groups?.body) {
    throw new Error(`WORKSTATION_ROUTE_CATALOG block was not found in ${filePath}`);
  }

  const catalog = new Map();
  const routePattern = /\n\s*([A-Za-z0-9_]+):\s*"([^"]+)"/g;
  let routeMatch = routePattern.exec(match.groups.body);
  while (routeMatch) {
    catalog.set(routeMatch[1], routeMatch[2]);
    routeMatch = routePattern.exec(match.groups.body);
  }

  return catalog;
}

function extractExplicitAppRoutes(source) {
  const paths = new Set();
  const routePattern = /<Route\s+path="([^"]+)"/g;
  let routeMatch = routePattern.exec(source);
  while (routeMatch) {
    paths.add(routeMatch[1]);
    routeMatch = routePattern.exec(source);
  }

  return paths;
}

function collectExpectedCapturePaths(routeCatalog, appRoutes) {
  // Compatibility redirect routes forward to a canonical screen that already has
  // its own capture, so they do not need a dedicated screenshot entry. This list
  // must stay in sync with the compatibility routes excluded by the coverage
  // tests in tests/scripts/test_refresh_screenshots_workflow.py.
  const compatibilityRouteKeys = new Set([
    "accountingTrialBalanceLegacy",
    "dataAlertsLegacy",
    "dataSecurityMasterLegacy",
    "dataWatchlistLegacy",
    "settingsIntegrations",
    "settingsFeatureCoverage",
    "settingsAlpacaProviderSetup",
    "settingsBackendCapabilityCoverage",
    "settingsDiagnosticEndpoints",
    "strategyFormulaWorkbenchLegacy"
  ]);
  const compatibilityAppRoutes = new Set([
    "/accounting/trial-balance",
    "/data/alerts",
    "/data/security-master",
    "/data/security-master/*",
    "/data/watchlist",
    "/overview/*",
    "/research/*",
    "/data-operations/*",
    "/governance/*",
    "/strategy/formula-workbench"
  ]);
  const expected = new Set(["/"]);

  for (const [key, routePath] of routeCatalog.entries()) {
    if (!compatibilityRouteKeys.has(key)) {
      expected.add(routePath);
    }
  }

  for (const routePath of appRoutes) {
    if (!routePath.includes("*") && !compatibilityAppRoutes.has(routePath)) {
      expected.add(routePath);
    }
  }

  return expected;
}

function screenshotCoveragePath(routePath) {
  if (typeof routePath !== "string" || routePath.trim().length === 0) {
    return "";
  }

  try {
    const routeUrl = new URL(routePath.trim(), "http://meridian.local");
    return `${routeUrl.pathname || "/"}${routeUrl.hash}`;
  } catch {
    return "";
  }
}

function assertCaptureRouteStateIdentity(captures) {
  const capturesByPath = new Map();

  for (const capture of captures) {
    const routePath = typeof capture.path === "string" ? capture.path.trim() : "";
    if (routePath.length === 0) {
      continue;
    }

    const matches = capturesByPath.get(routePath) ?? [];
    matches.push(capture);
    capturesByPath.set(routePath, matches);
  }

  const ambiguousPaths = [];
  for (const [routePath, matches] of capturesByPath.entries()) {
    if (matches.length < 2) {
      continue;
    }

    const variants = matches.map((capture) =>
      typeof capture.variant === "string" ? capture.variant.trim() : ""
    );
    const hasExplicitUniqueVariants = variants.every((variant) => variant.length > 0)
      && new Set(variants).size === variants.length;
    if (!hasExplicitUniqueVariants) {
      ambiguousPaths.push(routePath);
    }
  }

  if (ambiguousPaths.length > 0) {
    throw new Error(
      "Web screenshot captures must use a unique route state or declare distinct non-empty "
      + `variant values. Ambiguous path(s): ${ambiguousPaths.sort().join(", ")}`
    );
  }
}

async function findMissingCaptureRoutePaths(captures, routeCatalogPath, appShellPath) {
  const capturedPaths = new Set(
    captures
      .map((capture) => screenshotCoveragePath(capture.path))
      .filter((routePath) => routePath.length > 0)
  );
  const routeCatalog = extractWorkstationRouteCatalog(
    await fs.readFile(routeCatalogPath, "utf8"),
    routeCatalogPath
  );
  const appRoutes = extractExplicitAppRoutes(await fs.readFile(appShellPath, "utf8"));
  const expectedPaths = new Set(
    [...collectExpectedCapturePaths(routeCatalog, appRoutes)]
      .map((routePath) => screenshotCoveragePath(routePath))
      .filter((routePath) => routePath.length > 0)
  );
  return [...expectedPaths]
    .filter((routePath) => !capturedPaths.has(routePath))
    .sort();
}

function collectCaptureWaitForTexts(capture) {
  const waitForTexts = [];
  if (typeof capture.waitForText === "string" && capture.waitForText.trim().length > 0) {
    waitForTexts.push(capture.waitForText.trim());
  }

  if (Array.isArray(capture.waitForTexts)) {
    for (const value of capture.waitForTexts) {
      if (typeof value === "string" && value.trim().length > 0) {
        waitForTexts.push(value.trim());
      }
    }
  }

  return [...new Set(waitForTexts)];
}

function collectCaptureWaitForAbsentTexts(capture) {
  if (!Array.isArray(capture.waitForAbsentTexts)) {
    return [];
  }

  return [...new Set(
    capture.waitForAbsentTexts
      .filter((value) => typeof value === "string" && value.trim().length > 0)
      .map((value) => value.trim())
  )];
}

function isActionableBrowserError(text) {
  return /Maximum update depth exceeded|Meridian workstation route failed to render|Unhandled error/i.test(text);
}

function createPageErrorTracker(page) {
  const errors = [];
  const waiters = new Set();

  const record = (message) => {
    errors.push(message);
    for (const waiter of waiters) {
      waiter.resolve(message);
    }
    waiters.clear();
  };

  const onPageError = (error) => {
    record(`pageerror: ${error.stack ?? error.message}`);
  };
  const onConsole = (message) => {
    if (message.type() !== "error") {
      return;
    }

    const text = message.text();
    if (isActionableBrowserError(text)) {
      record(`console.error: ${text}`);
    }
  };

  page.on("pageerror", onPageError);
  page.on("console", onConsole);

  return {
    reset() {
      errors.length = 0;
      waiters.clear();
    },
    firstError() {
      return errors[0] ?? null;
    },
    waitForErrorSignal() {
      const existing = errors[0];
      if (existing) {
        return {
          promise: Promise.resolve(existing),
          dispose() {}
        };
      }

      const waiter = {};
      waiter.promise = new Promise((resolve) => {
        waiter.resolve = resolve;
      });
      waiter.dispose = () => waiters.delete(waiter);
      waiters.add(waiter);
      return waiter;
    },
    dispose() {
      page.off("pageerror", onPageError);
      page.off("console", onConsole);
      waiters.clear();
    }
  };
}

async function waitForCaptureStep(stepPromise, pageErrors, captureName, description) {
  const waiter = pageErrors.waitForErrorSignal();
  try {
    await Promise.race([
      stepPromise,
      waiter.promise.then((message) => {
        throw new Error(`Capture ${captureName} hit a browser render error while ${description}: ${message}`);
      })
    ]);
  } finally {
    waiter.dispose();
  }
}

function assertNoCaptureBrowserError(pageErrors, captureName) {
  const message = pageErrors.firstError();
  if (message) {
    throw new Error(`Capture ${captureName} hit a browser render error: ${message}`);
  }
}

async function captureRoute(page, pageErrors, capture, outputDir, baseUrl, defaults, minBytes, minTextLength, timeoutMs, readinessTimeoutMs) {
  pageErrors.reset();
  const viewport = {
    width: Number(capture.viewport?.width ?? defaults.width ?? 1440),
    height: Number(capture.viewport?.height ?? defaults.height ?? 1100)
  };
  await page.setViewportSize(viewport);

  const url = toRouteUrl(baseUrl, capture.path);
  const fileName = `${capture.name}.png`;
  const outputPath = path.join(outputDir, fileName);
  const started = Date.now();

  await waitForCaptureStep(
    page.goto(url, { waitUntil: "domcontentloaded", timeout: timeoutMs }),
    pageErrors,
    capture.name,
    "loading the route"
  );
  // Label the frame timeout explicitly: the raw Playwright message ("page.waitForSelector:
  // Timeout ... exceeded.") does not say which wait failed, and this is the one wait that
  // fails when the app shell itself never mounts (e.g. the first-run activation gate has no
  // fixture and blocks every route).
  await waitForCaptureStep(
    page.waitForSelector(".workstation-frame", { timeout: timeoutMs }).catch((cause) => {
      if (cause?.name === "TimeoutError") {
        throw new Error(
          `App shell failed to mount: .workstation-frame did not render within ${timeoutMs}ms. `
          + "Check shell-level screenshot fixtures (e.g. /api/workstation/first-run/) and the dev server logs."
        );
      }
      throw cause;
    }),
    pageErrors,
    capture.name,
    "waiting for the workstation frame"
  );
  for (const waitForText of collectCaptureWaitForTexts(capture)) {
    await waitForCaptureStep(
      page.getByText(waitForText, { exact: false }).filter({ visible: true }).first().waitFor({ timeout: readinessTimeoutMs }),
      pageErrors,
      capture.name,
      `waiting for visible text '${waitForText}'`
    );
  }
  if (Array.isArray(capture.waitForSelectors)) {
    for (const selector of capture.waitForSelectors) {
      if (typeof selector === "string" && selector.trim().length > 0) {
        await waitForCaptureStep(
          page.waitForSelector(selector, { timeout: readinessTimeoutMs }),
          pageErrors,
          capture.name,
          `waiting for selector '${selector}'`
        );
      }
    }
  }
  await page.waitForLoadState("networkidle", { timeout: 15000 }).catch(() => undefined);
  for (const waitForAbsentText of collectCaptureWaitForAbsentTexts(capture)) {
    await waitForCaptureStep(
      page.getByText(waitForAbsentText, { exact: false })
        .filter({ visible: true })
        .first()
        .waitFor({ state: "hidden", timeout: readinessTimeoutMs }),
      pageErrors,
      capture.name,
      `waiting for transitional text '${waitForAbsentText}' to clear`
    );
  }
  assertNoCaptureBrowserError(pageErrors, capture.name);

  const textLength = await page.evaluate(() => document.body.innerText.trim().length);
  if (textLength < minTextLength) {
    throw new Error(`Rendered body text length ${textLength} is below minimum ${minTextLength}`);
  }

  const actualUrl = page.url();
  const requestedPath = new URL(url).pathname;
  // Some captures deliberately target a compatibility route that redirects to
  // the canonical screen (e.g. the accounting/data evidence entry points land
  // on the reporting evidence workbench). When a capture declares that redirect
  // via `expectedRedirectPath`, treat the redirect destination as the expected
  // final path so the intentional navigation is not mistaken for a route that
  // bounced to a fallback. The strict check still catches undeclared redirects.
  const expectedPath = typeof capture.expectedRedirectPath === "string" && capture.expectedRedirectPath.trim().length > 0
    ? new URL(toRouteUrl(baseUrl, capture.expectedRedirectPath.trim())).pathname
    : requestedPath;
  const actualPath = new URL(actualUrl).pathname;
  if (actualPath !== expectedPath) {
    throw new Error(`Capture ${capture.name} ended on '${actualPath}', expected '${expectedPath}'.`);
  }

  await fs.mkdir(outputDir, { recursive: true });
  const buffer = await page.screenshot({ path: outputPath, fullPage: true });
  if (buffer.length < minBytes) {
    throw new Error(`Screenshot ${fileName} is ${buffer.length} bytes, below minimum ${minBytes}`);
  }

  return {
    id: capture.id,
    name: capture.name,
    docLabel: capture.docLabel,
    route: capture.path,
    url,
    actualUrl,
    requestedPath,
    expectedPath,
    actualPath,
    file: fileName,
    path: outputPath,
    viewport,
    bytes: buffer.length,
    textLength,
    durationSeconds: Number(((Date.now() - started) / 1000).toFixed(2)),
    status: "passed"
  };
}

async function captureRouteWithRetries(
  browser,
  capture,
  fixtureRoutes,
  outputDir,
  baseUrl,
  defaults,
  minBytes,
  minTextLength,
  timeoutMs,
  readinessTimeoutMs,
  maxAttempts,
  onTrackerChange
) {
  let lastError = null;

  for (let attempt = 1; attempt <= maxAttempts; attempt += 1) {
    // Each attempt renders in a fresh browser context so a retry starts from the
    // route's default first-load state instead of inheriting a half-rendered
    // page from the attempt that just failed.
    const context = await browser.newContext();
    const page = await context.newPage();
    await setupApiMocking(page, fixtureRoutes);
    const tracker = createPageErrorTracker(page);
    onTrackerChange(tracker);

    try {
      const result = await captureRoute(
        page,
        tracker,
        capture,
        outputDir,
        baseUrl,
        defaults,
        minBytes,
        minTextLength,
        timeoutMs,
        readinessTimeoutMs
      );
      result.attempts = attempt;
      return result;
    } catch (error) {
      lastError = error;
      if (attempt < maxAttempts) {
        const message = error instanceof Error ? error.message : String(error);
        console.warn(
          `::warning::Capture ${capture.name} (${capture.path}) attempt ${attempt}/${maxAttempts} failed: `
          + `${message.split(/\r?\n/)[0]}; retrying.`
        );
      }
    } finally {
      tracker.dispose();
      onTrackerChange(null);
      await context.close();
    }
  }

  throw lastError ?? new Error(`Capture ${capture.name} failed after ${maxAttempts} attempt(s).`);
}

async function main() {
  const { values, valueLists, flags } = parseArgs(process.argv.slice(2));
  const repoRoot = values.has("repo-root")
    ? path.resolve(values.get("repo-root"))
    : await findRepoRoot(process.cwd());
  const dashboardDir = resolveRepoPath(repoRoot, values.get("dashboard-dir") ?? "src/Meridian.Ui/dashboard");
  const configPath = resolveRepoPath(repoRoot, values.get("config") ?? "scripts/dev/web-screenshot-routes.json");
  const routeConfig = await readJson(configPath);

  if (Number(routeConfig.version) !== 1) {
    throw new Error(`Unsupported web screenshot route config version: ${routeConfig.version}`);
  }

  const allCaptures = Array.isArray(routeConfig.captures) ? routeConfig.captures : [];
  if (allCaptures.length === 0) {
    throw new Error(`No web screenshot captures found in ${configPath}`);
  }
  assertCaptureRouteStateIdentity(allCaptures);
  const captureSelectors = collectCaptureSelectors(valueLists);
  const captures = selectCaptures(allCaptures, captureSelectors);

  if (flags.has("list")) {
    for (const capture of captures) {
      console.log(`${capture.id ?? ""}\t${capture.name}\t${capture.path}`);
    }
    return;
  }

  const outputDir = resolveRepoPath(repoRoot, values.get("output-dir") ?? routeConfig.outputRoot ?? "docs/screenshots/web");
  const manifestPath = resolveRepoPath(repoRoot, values.get("manifest") ?? "artifacts/web-screenshots/manifest.json");
  const fixturesPath = resolveRepoPath(
    repoRoot,
    values.get("fixtures") ?? routeConfig.fixturesPath ?? "scripts/dev/web-screenshot-fixtures.json"
  );
  const routeCatalogPath = resolveRepoPath(
    repoRoot,
    values.get("route-catalog") ?? "src/Meridian.Ui/dashboard/src/lib/workspace.ts"
  );
  const appShellPath = resolveRepoPath(
    repoRoot,
    values.get("app-shell") ?? "src/Meridian.Ui/dashboard/src/app.tsx"
  );
  const host = values.get("host") ?? "127.0.0.1";
  const port = Number(values.get("port") ?? "5173");
  const timeoutMs = Number(values.get("timeout-ms") ?? "120000");
  // Readiness assertions (waitForText/selector) use a shorter budget than the
  // navigation/frame timeout so a single screen that never renders its expected
  // content fails fast and the run moves on to the next route instead of
  // blocking the whole catalog for the full navigation timeout.
  const readinessTimeoutMs = Number(values.get("readiness-timeout-ms") ?? "30000");
  const minBytes = Number(values.get("min-bytes") ?? "12000");
  const minTextLength = Number(values.get("min-text-length") ?? "80");
  // A transient render or network-idle hiccup on a single route should not fail
  // the whole catalog. Retry each capture a bounded number of times (fresh
  // context each attempt) before recording it as failed.
  const parsedCaptureRetries = Number(values.get("capture-retries") ?? "1");
  const captureRetries = Number.isFinite(parsedCaptureRetries) ? Math.max(0, parsedCaptureRetries) : 1;
  const captureAttempts = captureRetries + 1;
  const basePath = routeConfig.basePath ?? "/workstation";
  const baseUrl = values.get("base-url") ?? `http://${host}:${port}${basePath}`;
  const logs = [];
  const startedUtc = new Date();
  let server = null;
  let browser = null;
  let pageErrors = null;
  const results = [];

  const manifest = {
    version: 1,
    status: "running",
    generatedAtUtc: startedUtc.toISOString(),
    repoRoot,
    dashboardDir,
    configPath,
    baseUrl: normalizeBaseUrl(baseUrl),
    outputDir,
    selectedCaptureCount: captures.length,
    totalCaptureCount: allCaptures.length,
    maxAttemptsPerCapture: captureAttempts,
    captures: results,
    logs: []
  };

  try {
    // Coverage gaps (a live app route with no capture entry) are reported at the
    // end as a run failure rather than aborting before any screenshot is taken,
    // so the catalog stays self-adjusting: every configured screen is still
    // captured and the run tells you exactly which new route needs an entry.
    const missingCoverage = await findMissingCaptureRoutePaths(allCaptures, routeCatalogPath, appShellPath);

    if (!flags.has("skip-server")) {
      server = startViteServer(dashboardDir, host, port, logs);
      await waitForServer(`${normalizeBaseUrl(baseUrl)}/`, timeoutMs, server, logs);
    }

    // Load fixture API responses and mock all /api/** requests so screenshots
    // never depend on a running Meridian backend.
    const fixtureConfig = await readJson(fixturesPath);
    const fixtureRoutes = (fixtureConfig && typeof fixtureConfig.routes === "object")
      ? fixtureConfig.routes
      : {};
    const requiredFixtureRoutes = collectRequiredFixtureRoutes(captures);
    const missingFixtureRoutes = findMissingFixtureRoutes(requiredFixtureRoutes, fixtureRoutes);
    if (missingFixtureRoutes.length > 0) {
      // A missing fixture no longer aborts the run; the affected route may render
      // a degraded state and, if it fails its readiness checks, is reported as a
      // per-route capture failure while every other route still captures.
      console.warn(
        `::warning::Screenshot fixture route coverage is incomplete. Missing route fixtures: ${missingFixtureRoutes.join(", ")}. `
        + "Affected routes may render degraded and be reported as capture failures."
      );
    }

    const dashboardRequire = createRequire(path.join(dashboardDir, "package.json"));
    const { chromium } = dashboardRequire("playwright");
    // Sandboxes and CI images often provide a system Chromium instead of the exact
    // browser build the pinned Playwright version would download.
    const chromiumExecutablePath = process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH;
    // --disable-dev-shm-usage avoids Chromium crashing when the CI container's
    // /dev/shm is too small for full-page screenshots of large workstation routes.
    const launchOptions = { args: ["--disable-dev-shm-usage"] };
    if (chromiumExecutablePath) {
      launchOptions.executablePath = chromiumExecutablePath;
    }
    browser = await chromium.launch(launchOptions);

    for (const capture of captures) {
      // Each capture renders in a fresh browser context so it shows the
      // route's default first-load state. The app shell persists
      // workflow-continuity, activity, and focus state across navigations,
      // so a shared context makes capture results depend on visit order.
      try {
        const result = await captureRouteWithRetries(
          browser,
          capture,
          fixtureRoutes,
          outputDir,
          baseUrl,
          routeConfig.defaultViewport ?? {},
          minBytes,
          minTextLength,
          timeoutMs,
          readinessTimeoutMs,
          captureAttempts,
          (tracker) => {
            pageErrors = tracker;
          }
        );
        results.push(result);
        console.log(`Captured ${capture.name} -> ${result.path}`);
      } catch (error) {
        // Fault isolation: a screen that still fails after its retries is
        // recorded with its error and skipped so the run continues through the
        // remaining screens. The failure is surfaced in the end-of-run summary
        // and turns the overall run non-zero, but never blocks the rest of the
        // catalog.
        const message = error instanceof Error ? error.message : String(error);
        const failed = {
          id: capture.id,
          name: capture.name,
          docLabel: capture.docLabel,
          route: capture.path,
          url: toRouteUrl(baseUrl, capture.path),
          status: "failed",
          attempts: captureAttempts,
          error: message
        };
        results.push(failed);
        console.error(`::warning::Skipped ${capture.name} (${capture.path}): ${message.split(/\r?\n/)[0]}`);

        // Systemic-failure circuit breaker: per-route fault isolation exists so one broken
        // screen never blocks the rest of the catalog, but when the shared app shell itself
        // never mounts, every remaining route is guaranteed to fail the same slow way
        // (2 x timeoutMs each) until the CI job ceiling cancels the run. If the first few
        // routes ALL failed to mount the shell, abort now with a pointed error instead.
        const shellMountFailureLimit = 3;
        if (
          results.length >= shellMountFailureLimit
          && results.every((result) => result.status === "failed"
            && /App shell failed to mount/.test(String(result.error)))
        ) {
          throw new Error(
            `Aborting capture run: the app shell failed to mount on the first ${results.length} route(s). `
            + "This is a shell-level failure (activation gate, missing shell fixtures, or a dev server "
            + "compile error), so every remaining route would fail the same way. See the manifest logs."
          );
        }
      }
    }

    const proxyErrors = logs.filter((line) => /http proxy error:\s*\/api\//i.test(line));
    const failedCaptures = results.filter((result) => result.status === "failed");
    const passedCaptures = results.filter((result) => result.status === "passed");
    manifest.capturedCount = passedCaptures.length;
    manifest.failedCaptureCount = failedCaptures.length;

    console.log(
      `\nScreenshot capture summary: ${passedCaptures.length}/${captures.length} route(s) captured successfully.`
    );
    if (failedCaptures.length > 0) {
      console.log(`${failedCaptures.length} route(s) did not render correctly and were skipped:`);
      for (const failure of failedCaptures) {
        const firstLine = String(failure.error ?? "unknown error").split(/\r?\n/)[0];
        console.log(`  - ${failure.name} (${failure.route}): ${firstLine}`);
      }
    }

    const problems = [];
    if (missingCoverage.length > 0) {
      problems.push(`Live route(s) without a screenshot capture entry: ${missingCoverage.join(", ")}`);
    }
    if (proxyErrors.length > 0) {
      problems.push(`Detected ${proxyErrors.length} Vite proxy API error log(s) during screenshot capture.`);
    }
    for (const problem of problems) {
      console.error(`::error::${problem}`);
    }

    // The run has already moved through every screen and written a screenshot
    // for each one that rendered. If any screen failed or coverage is
    // incomplete, exit non-zero with a consolidated summary so CI flags it while
    // the successful screenshots remain available.
    if (failedCaptures.length > 0 || problems.length > 0) {
      manifest.status = "failed";
      const summary = [
        failedCaptures.length > 0
          ? `${failedCaptures.length} route(s) failed to render: ${failedCaptures.map((failure) => failure.name).join(", ")}`
          : null,
        ...problems
      ].filter(Boolean).join(" | ");
      throw new Error(summary);
    }

    manifest.status = "passed";
  } catch (error) {
    manifest.status = "failed";
    manifest.error = error instanceof Error ? error.message : String(error);
    throw error;
  } finally {
    if (pageErrors) {
      pageErrors.dispose();
    }
    if (browser) {
      await browser.close();
    }
    await stopProcess(server);

    manifest.finishedAtUtc = new Date().toISOString();
    manifest.durationSeconds = Number(((Date.now() - startedUtc.getTime()) / 1000).toFixed(2));
    manifest.logs = logs.slice(-200);
    await fs.mkdir(path.dirname(manifestPath), { recursive: true });
    await fs.writeFile(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
  }
}

main().catch((error) => {
  console.error(error instanceof Error ? error.stack ?? error.message : error);
  process.exitCode = 1;
});
