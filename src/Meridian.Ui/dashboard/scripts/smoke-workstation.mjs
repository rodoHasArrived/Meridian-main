#!/usr/bin/env node

import { spawn } from "node:child_process";
import { createRequire } from "node:module";
import fs from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { chromiumLaunchOptions } from "./design-system-smoke-helpers.mjs";
import { A11Y_ROUTES, checkInspectorFocusNotObscured, compareAxeFindings, nextAxeBaseline, readAxeBaseline, runAxe, writeAxeBaseline } from "./smoke-workstation-a11y.mjs";
import { createScreenshotApiResolver, loadScreenshotScenarioModule, setupScenarioApiMocking } from "../../../../scripts/dev/web-screenshot-scenarios.mjs";

const navLabels = ["Trading", "Portfolio", "Accounting", "Reporting", "Strategy", "Data", "Settings"];

function parseArgs(argv) {
  const values = new Map();
  const flags = new Set();

  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (arg === "--help" || arg === "-h") {
      flags.add("help");
      continue;
    }

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

function showHelp() {
  console.log(`Meridian browser workstation smoke check

Usage:
  node scripts/smoke-workstation.mjs [options]

Options:
  --design-system          Run maintained theme/font/accessibility and fixture-workbench checks.
  --artifacts-dir <path>   Design-system JSON/screenshots directory (ignored artifacts/ by default).
  --backend-url <url>      Existing backend-connected dev server for workbench exclusion checks.
  --production-url <url>   Existing production preview for workbench exclusion checks.
  --url <url>              Existing workstation URL to test (fixture-only dev for --design-system).
  --skip-server            Do not start Vite; requires --url or default local server.
  --host <host>            Vite host when starting the dev server. Default: 127.0.0.1.
  --port <port>            Vite port when starting the dev server. Default: 5173.
  --timeout-ms <ms>        Navigation and startup timeout. Default: 30000.
  --manifest <path>        JSON result path. Default: artifacts/browser-workstation-smoke/manifest.json.
  --screenshot <path>      Screenshot path. Default: artifacts/browser-workstation-smoke/workstation-smoke.png.
  --min-text-length <n>    Minimum rendered body text length. Default: 200.
  --skip-a11y              Skip the axe route audit and the inspector focus check.
  --write-axe-baseline     Seed scripts/smoke-workstation-axe-baseline.json, or lock in removed findings.
  --help                   Show this help text.
`);
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
    if (await pathExists(path.join(current, "Meridian.sln"))) {
      return current;
    }

    const parent = path.dirname(current);
    if (parent === current) {
      throw new Error(`Could not locate Meridian repo root from ${startDir}`);
    }

    current = parent;
  }
}

function resolveRepoPath(repoRoot, inputPath) {
  return path.isAbsolute(inputPath) ? inputPath : path.join(repoRoot, inputPath);
}

function npmCommand() {
  return process.platform === "win32" ? "npm.cmd" : "npm";
}

function normalizeBaseUrl(url) {
  return url.replace(/\/+$/, "");
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
  const child = spawn(
    npmCommand(),
    ["run", "dev", "--", "--host", host, "--port", String(port), "--strictPort"],
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

  if (process.platform === "win32") {
    const taskkill = spawn("taskkill", ["/pid", String(child.pid), "/t", "/f"], { stdio: "ignore" });
    await new Promise((resolve) => taskkill.once("exit", resolve));
    return;
  }

  try {
    process.kill(-child.pid, "SIGTERM");
  } catch {
    child.kill("SIGTERM");
  }

  await new Promise((resolve) => setTimeout(resolve, 1000));
  if (child.exitCode === null) {
    child.kill("SIGKILL");
  }
}

async function loadFixtureRoutes(repoRoot) {
  const fixturePath = path.join(repoRoot, "scripts/dev/web-screenshot-fixtures.json");
  const content = await fs.readFile(fixturePath, "utf8");
  const fixtureConfig = JSON.parse(content);
  return fixtureConfig && typeof fixtureConfig.routes === "object" ? fixtureConfig.routes : {};
}

async function main() {
  const { values, flags } = parseArgs(process.argv.slice(2));
  if (flags.has("help")) {
    showHelp();
    return;
  }

  const repoRoot = await findRepoRoot(process.cwd());
  const dashboardDir = path.join(repoRoot, "src/Meridian.Ui/dashboard");
  if (flags.has("design-system")) {
    const { runManagedDesignSystemSmoke } = await import("./design-system-smoke-runner.mjs");
    await runManagedDesignSystemSmoke({ values, flags, repoRoot, dashboardDir });
    return;
  }
  const host = values.get("host") ?? "127.0.0.1";
  const port = Number(values.get("port") ?? "5173");
  const timeoutMs = Number(values.get("timeout-ms") ?? "30000");
  const minTextLength = Number(values.get("min-text-length") ?? "200");
  const targetUrl = values.get("url") ?? `http://${host}:${port}/workstation/`;
  const manifestPath = resolveRepoPath(repoRoot, values.get("manifest") ?? "artifacts/browser-workstation-smoke/manifest.json");
  const screenshotPath = resolveRepoPath(repoRoot, values.get("screenshot") ?? "artifacts/browser-workstation-smoke/workstation-smoke.png");
  const startedAt = new Date();
  const logs = [];
  const errors = [];
  const consoleMessages = [];
  const nonApiHttpErrors = [];
  const failedRequests = [];
  let server = null;
  let browser = null;
  let scenarioSource = null;

  const manifest = {
    version: 1,
    status: "running",
    generatedAtUtc: startedAt.toISOString(),
    targetUrl,
    screenshotPath,
    navLabels,
    logs
  };

  try {
    if (!flags.has("skip-server")) {
      server = startViteServer(dashboardDir, host, port, logs);
      await waitForServer(targetUrl, timeoutMs);
    }

    const dashboardRequire = createRequire(path.join(dashboardDir, "package.json"));
    const { chromium } = dashboardRequire("playwright");
    browser = await chromium.launch(chromiumLaunchOptions());
    const page = await browser.newPage({ viewport: { width: 1440, height: 1100 }, deviceScaleFactor: 1 });

    page.on("console", (message) => {
      if (message.type() === "error" && !message.text().includes("Failed to load resource: the server responded with a status of 404")) {
        errors.push(message.text());
      }
      if (message.type() === "error") {
        consoleMessages.push(message.text());
      }
    });
    page.on("pageerror", (error) => errors.push(error.message));
    page.on("response", (response) => {
      if (response.status() >= 400 && !response.url().includes("/api/")) {
        nonApiHttpErrors.push(`${response.status()} ${response.url()}`);
      }
    });
    page.on("requestfailed", (request) => {
      if (!request.url().includes("/api/")) {
        failedRequests.push(`${request.method()} ${request.url()} ${request.failure()?.errorText ?? ""}`.trim());
      }
    });

    // Use the same typed Accounting scenario and exact fixture reads as screenshot
    // capture. Accounting is intentionally absent from the legacy JSON fixtures.
    scenarioSource = await loadScreenshotScenarioModule(dashboardDir);
    const apiResolver = createScreenshotApiResolver(scenarioSource.module, await loadFixtureRoutes(repoRoot));
    await setupScenarioApiMocking(page, apiResolver, (message) => errors.push(message));
    await page.goto(targetUrl, { waitUntil: "domcontentloaded", timeout: timeoutMs });
    await page.waitForSelector(".workstation-frame", { timeout: timeoutMs });
    await page.waitForLoadState("networkidle", { timeout: 15000 }).catch(() => undefined);

    const bodyText = await page.locator("body").innerText();
    const missingLabels = navLabels.filter((label) => !bodyText.includes(label));
    if (missingLabels.length > 0) {
      throw new Error(`Missing workstation navigation labels: ${missingLabels.join(", ")}`);
    }

    if (bodyText.trim().length < minTextLength) {
      throw new Error(`Rendered body text length ${bodyText.trim().length} is below minimum ${minTextLength}`);
    }

    if (!new URL(page.url()).pathname.startsWith("/workstation")) {
      throw new Error(`Expected workstation route, got ${page.url()}`);
    }

    await fs.mkdir(path.dirname(screenshotPath), { recursive: true });
    const screenshot = await page.screenshot({ path: screenshotPath, fullPage: true });
    if (screenshot.length < 12000) {
      throw new Error(`Smoke screenshot is unexpectedly small: ${screenshot.length} bytes`);
    }

    if (!flags.has("skip-a11y")) {
      manifest.accessibility = await runAccessibilityChecks({
        page,
        baseUrl: new URL("/workstation", targetUrl).toString(),
        dashboardDir,
        timeoutMs,
        writeBaseline: flags.has("write-axe-baseline")
      });
    }

    if (errors.length > 0) {
      throw new Error(`Browser console/page errors detected: ${errors.join(" | ")}`);
    }

    if (nonApiHttpErrors.length > 0) {
      throw new Error(`Non-API HTTP errors detected: ${nonApiHttpErrors.join(" | ")}`);
    }

    if (failedRequests.length > 0) {
      throw new Error(`Non-API request failures detected: ${failedRequests.join(" | ")}`);
    }

    manifest.status = "passed";
    manifest.actualUrl = page.url();
    manifest.textLength = bodyText.trim().length;
    manifest.screenshotBytes = screenshot.length;
  } catch (error) {
    manifest.status = "failed";
    manifest.error = error instanceof Error ? error.message : String(error);
    if (error && typeof error === "object" && "accessibility" in error) manifest.accessibility = error.accessibility;
    throw error;
  } finally {
    if (browser) {
      await browser.close();
    }
    await scenarioSource?.close();
    await stopProcess(server);

    manifest.finishedAtUtc = new Date().toISOString();
    manifest.durationSeconds = Number(((Date.now() - startedAt.getTime()) / 1000).toFixed(2));
    manifest.consoleErrors = errors;
    manifest.consoleMessages = consoleMessages.slice(-50);
    manifest.nonApiHttpErrors = nonApiHttpErrors;
    manifest.failedRequests = failedRequests;
    manifest.logs = logs.slice(-200);
    await fs.mkdir(path.dirname(manifestPath), { recursive: true });
    await fs.writeFile(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
  }
}

async function runAccessibilityChecks({ page, baseUrl, dashboardDir, timeoutMs, writeBaseline }) {
  const dashboardRequire = createRequire(path.join(dashboardDir, "package.json"));
  const axeSource = await fs.readFile(dashboardRequire.resolve("axe-core/axe.min.js"), "utf8");
  const baselinePath = path.join(dashboardDir, "scripts/smoke-workstation-axe-baseline.json");
  const findings = [];
  const focus = { checked: 0, obscured: [] };
  for (const route of A11Y_ROUTES) {
    await page.goto(`${baseUrl}${route.path === "/" ? "/" : route.path}`, { waitUntil: "domcontentloaded", timeout: timeoutMs });
    await page.waitForSelector(".workstation-frame", { timeout: timeoutMs });
    await page.waitForLoadState("networkidle", { timeout: 15000 }).catch(() => undefined);
    findings.push(...await runAxe(page, axeSource, route.id));
    if (route.id === "accounting-reconciliation") {
      Object.assign(focus, await checkInspectorFocusNotObscured(page, timeoutMs));
    }
  }

  const baseline = await readAxeBaseline(baselinePath);
  if (writeBaseline) {
    await writeAxeBaseline(baselinePath, nextAxeBaseline(findings, baseline));
  }
  const comparison = compareAxeFindings(findings, writeBaseline ? await readAxeBaseline(baselinePath) : baseline ?? { allowed: [] });
  const result = { routes: A11Y_ROUTES.map((route) => route.id), findings, focus, ...comparison };
  if (comparison.regressions.length > 0) {
    throw Object.assign(new Error(`New serious or critical axe findings: ${comparison.regressions.join(", ")}`), { accessibility: result });
  }
  if (comparison.improvements.length > 0) {
    console.log(`Axe findings no longer present; lock them in with --write-axe-baseline: ${comparison.improvements.join(", ")}`);
  }
  if (focus.obscured.length > 0) {
    throw Object.assign(new Error(`Focused controls are obscured in the reconciliation inspector: ${focus.obscured.join(" | ")}`), { accessibility: result });
  }
  return result;
}

main().catch((error) => {
  console.error(error instanceof Error ? error.stack ?? error.message : error);
  process.exitCode = 1;
});
