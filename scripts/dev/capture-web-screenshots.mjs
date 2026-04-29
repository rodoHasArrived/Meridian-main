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

function npmCommand() {
  return process.platform === "win32" ? "npm.cmd" : "npm";
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
      stdio: ["ignore", "pipe", "pipe"]
    }
  );

  child.stdout.on("data", (chunk) => appendLog(logs, "", chunk));
  child.stderr.on("data", (chunk) => appendLog(logs, "stderr: ", chunk));

  return child;
}

async function stopProcess(child) {
  if (!child || child.killed || child.exitCode !== null) {
    return;
  }

  child.kill("SIGTERM");

  await new Promise((resolve) => {
    const timer = setTimeout(() => {
      if (!child.killed) {
        child.kill("SIGKILL");
      }
      resolve();
    }, 5000);

    child.once("exit", () => {
      clearTimeout(timer);
      resolve();
    });
  });
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
  if (capture.waitForText) {
    await page.getByText(capture.waitForText, { exact: false }).first().waitFor({ timeout: timeoutMs });
  }
  await page.waitForLoadState("networkidle", { timeout: 5000 }).catch(() => undefined);

  const textLength = await page.evaluate(() => document.body.innerText.trim().length);
  if (textLength < minTextLength) {
    throw new Error(`Rendered body text length ${textLength} is below minimum ${minTextLength}`);
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
  const host = values.get("host") ?? "127.0.0.1";
  const port = Number(values.get("port") ?? "5173");
  const timeoutMs = Number(values.get("timeout-ms") ?? "30000");
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

    const dashboardRequire = createRequire(path.join(dashboardDir, "package.json"));
    const { chromium } = dashboardRequire("playwright");
    browser = await chromium.launch();
    const page = await browser.newPage();

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
