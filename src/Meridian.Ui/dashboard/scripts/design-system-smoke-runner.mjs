import { spawn } from "node:child_process";
import { access, copyFile, mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { workstationBase } from "./design-system-smoke-helpers.mjs";

export function managedSmokeOptions({ values, flags, repoRoot }) {
  const allowed = new Set(["host", "port", "url", "output", "artifacts-dir", "manifest", "backend-url", "production-url", "timeout-ms"]);
  for (const key of values.keys()) if (!allowed.has(key)) throw new Error("Unsupported --design-system option: --" + key);
  for (const flag of flags) if (!["design-system", "skip-server"].includes(flag)) throw new Error("Unsupported --design-system flag: --" + flag);
  if (flags.has("skip-server") && !values.has("url")) throw new Error("--skip-server requires --url in design-system mode");
  const port = Number(values.get("port") || 5173);
  if (!Number.isInteger(port) || port < 1024 || port > 65533) throw new Error("--port must be an integer between 1024 and 65533");
  const timeoutMs = Number(values.get("timeout-ms") || 30000);
  if (!Number.isInteger(timeoutMs) || timeoutMs < 1000 || timeoutMs > 120000) throw new Error("--timeout-ms must be an integer between 1000 and 120000");
  const host = values.get("host") || "127.0.0.1";
  if (!/^[a-zA-Z0-9.:[\]-]+$/.test(host)) throw new Error("Invalid --host");
  const defaultBase = "http://" + host + ":" + port + "/workstation";
  const base = workstationBase(values.get("url") || defaultBase);
  const backendBase = workstationBase(values.get("backend-url") || ("http://" + host + ":" + (port + 1) + "/workstation"));
  const productionBase = workstationBase(values.get("production-url") || ("http://" + host + ":" + (port + 2) + "/workstation"));
  if (new Set([base, backendBase, productionBase]).size !== 3) throw new Error("Fixture, backend and production URLs must be distinct");
  const selectedOutput = values.get("artifacts-dir") || values.get("output") || "artifacts/design-system-smoke";
  return {
    host, port, timeoutMs, base, backendBase, productionBase,
    output: path.resolve(repoRoot, selectedOutput),
    manifest: values.has("manifest") ? path.resolve(repoRoot, values.get("manifest")) : null,
    startFixture: !flags.has("skip-server") && !values.has("url"),
    startBackend: !values.has("backend-url"),
    startProduction: !values.has("production-url")
  };
}

function serverArguments(mode, host, port) {
  return [...(mode === "production" ? ["preview", "--outDir", "../wwwroot/workstation"] : []),
    "--host", host, "--port", String(port), "--strictPort"];
}

function startServer(dashboardDir, mode, host, port, logs) {
  // Launch Node directly: npm.cmd is not directly executable on every Windows host.
  const child = spawn(process.execPath, [path.join(dashboardDir, "node_modules/vite/bin/vite.js"), ...serverArguments(mode, host, port)], {
    cwd: dashboardDir,
    env: {
      ...process.env, BROWSER: "none", MERIDIAN_SCREENSHOT_CAPTURE: "true",
      MERIDIAN_DEV_MODE: mode === "fixture" ? "fixture-only" : "backend-connected",
      VITE_MERIDIAN_DEV_MODE: mode === "fixture" ? "fixture-only" : "backend-connected"
    },
    stdio: ["ignore", "pipe", "pipe"], detached: process.platform !== "win32"
  });
  const record = chunk => logs.push(...chunk.toString().split(/\r?\n/).filter(Boolean).map(line => mode + ": " + line));
  child.stdout.on("data", record); child.stderr.on("data", record);
  child.on("error", error => logs.push(mode + ": " + error.message));
  return child;
}

async function waitForServer(base, timeoutMs, processHandle) {
  const start = Date.now();
  let detail = "no response";
  while (Date.now() - start < timeoutMs) {
    if (processHandle && processHandle.exitCode !== null) throw new Error("Server exited before startup: " + base);
    try {
      const result = await fetch(base + "/", { signal: AbortSignal.timeout(2000) });
      if (result.ok) return;
      detail = "HTTP " + result.status;
    } catch (error) { detail = error.message; }
    await new Promise(resolve => setTimeout(resolve, 200));
  }
  throw new Error("Timed out starting " + base + ": " + detail);
}

async function stopServer(child) {
  if (!child || child.exitCode !== null) return;
  if (process.platform === "win32") {
    const kill = spawn("taskkill", ["/pid", String(child.pid), "/t", "/f"], { stdio: "ignore" });
    await new Promise(resolve => { kill.once("exit", resolve); kill.once("error", resolve); });
  } else {
    try { process.kill(-child.pid, "SIGTERM"); } catch { child.kill("SIGTERM"); }
    await Promise.race([
      new Promise(resolve => child.once("exit", resolve)),
      new Promise(resolve => setTimeout(resolve, 1000))
    ]);
    if (child.exitCode === null) { try { process.kill(-child.pid, "SIGKILL"); } catch { child.kill("SIGKILL"); } }
  }
}

export async function runManagedDesignSystemSmoke({ values, flags, repoRoot, dashboardDir }) {
  const options = managedSmokeOptions({ values, flags, repoRoot });
  // Gating is verified against the actual published application, never an implicit rebuild.
  await access(path.resolve(dashboardDir, "../wwwroot/workstation/index.html")).catch(() => {
    throw new Error("Production workstation assets are absent; run npm run build before design-system browser verification");
  });
  const logs = [], servers = [];
  await mkdir(options.output, { recursive: true });
  let resultCode = 1;
  const reportPath = path.join(options.output, "report.json");
  await writeFile(reportPath, JSON.stringify({ result: "RUNNING", url: options.base }) + "\n");
  try {
    for (const [mode, shouldStart, base, port] of [
      ["fixture", options.startFixture, options.base, options.port],
      ["backend", options.startBackend, options.backendBase, options.port + 1],
      ["production", options.startProduction, options.productionBase, options.port + 2]
    ]) {
      const child = shouldStart ? startServer(dashboardDir, mode, options.host, port, logs) : null;
      if (child) servers.push(child);
      await waitForServer(base, options.timeoutMs, child);
    }
    const child = spawn(process.execPath, [
      path.join(dashboardDir, "scripts/smoke-design-system.mjs"),
      "--expanded", "--url", options.base, "--backend-url", options.backendBase,
      "--production-url", options.productionBase, "--output", options.output,
      "--timeout-ms", String(options.timeoutMs)
    ], { cwd: dashboardDir, env: process.env, stdio: "inherit" });
    resultCode = await new Promise((resolve, reject) => { child.once("exit", code => resolve(code ?? 1)); child.once("error", reject); });
    if (resultCode !== 0) {
      const report = JSON.parse(await readFile(reportPath, "utf8"));
      throw new Error("Design-system browser verification failed: " + (report.failure || "browser exited before completing report.json") + "\nEvidence: " + options.output);
    }
  } catch (error) {
    resultCode = 1;
    const report = JSON.parse(await readFile(reportPath, "utf8").catch(() => "{}"));
    if (!report.failure) Object.assign(report, { result: "FAIL", failure: error.stack || String(error) });
    await writeFile(reportPath, JSON.stringify(report, null, 2) + "\n");
    throw error;
  } finally {
    await Promise.all(servers.map(stopServer));
    await writeFile(path.join(options.output, "servers.json"), JSON.stringify({ resultCode, logs }, null, 2) + "\n");
    if (options.manifest) {
      await mkdir(path.dirname(options.manifest), { recursive: true });
      await copyFile(reportPath, options.manifest);
    }
  }
}
