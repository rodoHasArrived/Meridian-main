#!/usr/bin/env node

import { spawn } from "node:child_process";
import { createRequire } from "node:module";
import fs from "node:fs/promises";
import path from "node:path";
import process from "node:process";

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
  --url <url>              Existing workstation URL to test.
  --skip-server            Do not start Vite; requires --url or default local server.
  --host <host>            Vite host when starting the dev server. Default: 127.0.0.1.
  --port <port>            Vite port when starting the dev server. Default: 5173.
  --timeout-ms <ms>        Navigation and startup timeout. Default: 30000.
  --manifest <path>        JSON result path. Default: artifacts/browser-workstation-smoke/manifest.json.
  --screenshot <path>      Screenshot path. Default: artifacts/browser-workstation-smoke/workstation-smoke.png.
  --min-text-length <n>    Minimum rendered body text length. Default: 200.
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

async function setupApiMocking(page, fixtureRoutes) {
  await page.route("**/api/**", (route) => {
    const url = new URL(route.request().url());
    const pathname = url.pathname;

    if (Object.prototype.hasOwnProperty.call(fixtureRoutes, pathname)) {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(fixtureRoutes[pathname])
      });
    }

    const prefixEntry = Object.entries(fixtureRoutes).find(([key]) => pathname.startsWith(`${key}/`));
    if (prefixEntry) {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(prefixEntry[1])
      });
    }

    return route.fulfill({ status: 404, contentType: "application/json", body: "{}" });
  });
}

async function main() {
  const { values, flags } = parseArgs(process.argv.slice(2));
  if (flags.has("help")) {
    showHelp();
    return;
  }

  const repoRoot = await findRepoRoot(process.cwd());
  const dashboardDir = path.join(repoRoot, "src/Meridian.Ui/dashboard");
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
    browser = await chromium.launch();
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

    await setupApiMocking(page, await loadFixtureRoutes(repoRoot));
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
    throw error;
  } finally {
    if (browser) {
      await browser.close();
    }
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

main().catch((error) => {
  console.error(error instanceof Error ? error.stack ?? error.message : error);
  process.exitCode = 1;
});
