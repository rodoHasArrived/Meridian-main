#!/usr/bin/env node

import { spawn } from "node:child_process";
import { createRequire } from "node:module";
import fs from "node:fs/promises";
import path from "node:path";
import process from "node:process";

const repoMarkers = ["Meridian.sln", ".git"];

function parseArgs(argv) {
  const values = new Map();
  const flags = new Set();

  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (!arg.startsWith("--")) {
      throw new Error(`Unexpected argument: ${arg}`);
    }

    const eqIndex = arg.indexOf("=");
    if (eqIndex > 0) {
      values.set(arg.slice(2, eqIndex), arg.slice(eqIndex + 1));
      continue;
    }

    const name = arg.slice(2);
    const next = argv[index + 1];
    if (next && !next.startsWith("--")) {
      values.set(name, next);
      index += 1;
    } else {
      flags.add(name);
    }
  }

  return { values, flags };
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

async function waitForServer(url, timeoutMs) {
  const started = Date.now();
  let lastError = "";

  while (Date.now() - started < timeoutMs) {
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
    await new Promise((resolve) => taskkill.once("exit", resolve));
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

function assertFixtureRouteCoverage(requiredRoutes, fixtureRoutes) {
  const availableRoutes = Object.keys(fixtureRoutes);
  const missing = requiredRoutes.filter(
    (requiredRoute) => !availableRoutes.some((candidate) => candidate === requiredRoute || requiredRoute.startsWith(`${candidate}/`))
  );

  if (missing.length > 0) {
    throw new Error(
      `Screenshot fixture route coverage is incomplete. Missing route fixtures: ${missing.join(", ")}`
    );
  }
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

async function captureRoute(page, capture, outputDir, baseUrl, defaults, minBytes, minTextLength, timeoutMs) {
  const viewport = {
    width: Number(capture.viewport?.width ?? defaults.width ?? 1440),
    height: Number(capture.viewport?.height ?? defaults.height ?? 1100)
  };
  await page.setViewportSize(viewport);

  const url = toRouteUrl(baseUrl, capture.path);
  const fileName = `${capture.name}.png`;
  const outputPath = path.join(outputDir, fileName);
  const started = Date.now();

  await page.goto(url, { waitUntil: "domcontentloaded", timeout: timeoutMs });
  await page.waitForSelector(".workstation-frame", { timeout: timeoutMs });
  for (const waitForText of collectCaptureWaitForTexts(capture)) {
    await page.getByText(waitForText, { exact: false }).filter({ visible: true }).first().waitFor({ timeout: timeoutMs });
  }
  if (Array.isArray(capture.waitForSelectors)) {
    for (const selector of capture.waitForSelectors) {
      if (typeof selector === "string" && selector.trim().length > 0) {
        await page.waitForSelector(selector, { timeout: timeoutMs });
      }
    }
  }
  await page.waitForLoadState("networkidle", { timeout: 15000 }).catch(() => undefined);

  const textLength = await page.evaluate(() => document.body.innerText.trim().length);
  if (textLength < minTextLength) {
    throw new Error(`Rendered body text length ${textLength} is below minimum ${minTextLength}`);
  }

  const actualUrl = page.url();
  const expectedPath = new URL(url).pathname;
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

async function main() {
  const { values, flags } = parseArgs(process.argv.slice(2));
  const repoRoot = values.has("repo-root")
    ? path.resolve(values.get("repo-root"))
    : await findRepoRoot(process.cwd());
  const dashboardDir = resolveRepoPath(repoRoot, values.get("dashboard-dir") ?? "src/Meridian.Ui/dashboard");
  const configPath = resolveRepoPath(repoRoot, values.get("config") ?? "scripts/dev/web-screenshot-routes.json");
  const routeConfig = await readJson(configPath);

  if (Number(routeConfig.version) !== 1) {
    throw new Error(`Unsupported web screenshot route config version: ${routeConfig.version}`);
  }

  const captures = Array.isArray(routeConfig.captures) ? routeConfig.captures : [];
  if (captures.length === 0) {
    throw new Error(`No web screenshot captures found in ${configPath}`);
  }

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
  const host = values.get("host") ?? "127.0.0.1";
  const port = Number(values.get("port") ?? "5173");
  const timeoutMs = Number(values.get("timeout-ms") ?? "120000");
  const minBytes = Number(values.get("min-bytes") ?? "12000");
  const minTextLength = Number(values.get("min-text-length") ?? "80");
  const basePath = routeConfig.basePath ?? "/workstation";
  const baseUrl = values.get("base-url") ?? `http://${host}:${port}${basePath}`;
  const logs = [];
  const startedUtc = new Date();
  let server = null;
  let browser = null;
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
    captures: results,
    logs: []
  };

  try {
    if (!flags.has("skip-server")) {
      server = startViteServer(dashboardDir, host, port, logs);
      await waitForServer(`${normalizeBaseUrl(baseUrl)}/`, timeoutMs);
    }

    // Load fixture API responses and mock all /api/** requests so screenshots
    // never depend on a running Meridian backend.
    const fixtureConfig = await readJson(fixturesPath);
    const fixtureRoutes = (fixtureConfig && typeof fixtureConfig.routes === "object")
      ? fixtureConfig.routes
      : {};
    const requiredFixtureRoutes = collectRequiredFixtureRoutes(captures);
    assertFixtureRouteCoverage(requiredFixtureRoutes, fixtureRoutes);

    const dashboardRequire = createRequire(path.join(dashboardDir, "package.json"));
    const { chromium } = dashboardRequire("playwright");
    browser = await chromium.launch();
    const page = await browser.newPage();
    await setupApiMocking(page, fixtureRoutes);

    for (const capture of captures) {
      try {
        const result = await captureRoute(
          page,
          capture,
          outputDir,
          baseUrl,
          routeConfig.defaultViewport ?? {},
          minBytes,
          minTextLength,
          timeoutMs
        );
        results.push(result);
        console.log(`Captured ${capture.name} -> ${result.path}`);
      } catch (error) {
        const failed = {
          id: capture.id,
          name: capture.name,
          docLabel: capture.docLabel,
          route: capture.path,
          url: toRouteUrl(baseUrl, capture.path),
          status: "failed",
          error: error instanceof Error ? error.message : String(error)
        };
        results.push(failed);
        throw error;
      }
    }

    const proxyErrors = logs.filter((line) => /http proxy error:\s*\/api\//i.test(line));
    if (proxyErrors.length > 0) {
      throw new Error(
        `Detected ${proxyErrors.length} Vite proxy API error log(s) during screenshot capture.`
      );
    }

    manifest.status = "passed";
  } catch (error) {
    manifest.status = "failed";
    manifest.error = error instanceof Error ? error.message : String(error);
    throw error;
  } finally {
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
