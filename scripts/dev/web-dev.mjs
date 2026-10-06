#!/usr/bin/env node

import { access, mkdir, writeFile } from "node:fs/promises";
import { randomUUID } from "node:crypto";
import http from "node:http";
import net from "node:net";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { setTimeout as delay } from "node:timers/promises";
import { startOwnedProcess } from "./owned-process.mjs";

const defaultRepoRoot = fileURLToPath(new URL("../../", import.meta.url));
const help = `Meridian web development launcher (Node.js 22.12+ and .NET 10 for backend mode)

  npm run dev                         Seeded backend + dotnet watch + Vite
  npm run dev:fixtures                Vite with fixture-only API responses
  node scripts/dev/web-dev.mjs --mode backend-connected|fixture-only [options]

Options:
  --port <port>                      Vite loopback port (default 5173)
  --backend-port <port>              Seeded host loopback port (default 8080)
  --data-dir <directory>             Retained demo parent (default artifacts/dev-web)
  --startup-timeout <milliseconds>   Per-stage readiness/build deadline (default 600000)
  --help                            Show this help

Occupied ports cause an error; existing servers are never reused or stopped.
Ctrl+C stops this launcher's processes. Seeded data survives shutdown and restarts.
`;

export function parseOptions(argv) {
  const options = { port: 5173, backendPort: 8080, dataDir: "artifacts/dev-web", timeoutMs: 600_000 };
  const names = new Map([["--mode", "mode"], ["--port", "port"], ["--backend-port", "backendPort"],
    ["--data-dir", "dataDir"], ["--startup-timeout", "timeoutMs"]]);
  const seen = new Set();
  for (let index = 0; index < argv.length; index++) {
    const [flag, inline] = argv[index].split(/=(.*)/s);
    if (flag === "--help" || flag === "-h") return { help: true };
    const name = names.get(flag);
    if (!name || seen.has(name)) throw new Error(`Unknown or repeated option: ${flag}`);
    seen.add(name);
    const value = inline ?? argv[++index];
    if (!value || value.startsWith("--")) throw new Error(`${flag} requires a value.`);
    options[name] = value;
  }
  if (!["fixture-only", "backend-connected"].includes(options.mode)) {
    throw new Error("Choose --mode fixture-only or --mode backend-connected (or use npm run dev).");
  }
  for (const name of ["port", "backendPort", "timeoutMs"]) {
    const value = Number(options[name]);
    if (!/^\d+$/.test(String(options[name])) || !Number.isSafeInteger(value) || value < 1 ||
        value > (name === "timeoutMs" ? 2_147_483_647 : 65535)) {
      throw new Error(`Invalid ${name}: ${options[name]}`);
    }
    options[name] = value;
  }
  if (options.mode === "backend-connected" && options.port === options.backendPort) {
    throw new Error("Vite and the backend must use different ports.");
  }
  return options;
}

export async function assertPortAvailable(port, { label = "port" } = {}) {
  for (const host of ["127.0.0.1", "::1"]) {
    await new Promise((resolve, reject) => {
      const server = net.createServer();
      server.once("error", (error) => {
        if (host === "::1" && ["EAFNOSUPPORT", "EADDRNOTAVAIL"].includes(error.code)) return resolve();
        reject(new Error(`${label} ${host}:${port} is unavailable (${error.code}). Choose another port; no existing process was stopped.`));
      });
      server.listen({ host, port, exclusive: true }, () => server.close(resolve));
    });
  }
}

export async function waitForHttp(url, { timeoutMs, signal, expectedHeader }) {
  const deadline = Date.now() + timeoutMs;
  let lastError = "no response";
  while (Date.now() < deadline) {
    signal?.throwIfAborted();
    try {
      await new Promise((resolve, reject) => {
        const request = http.get(url, { signal }, (response) => {
          response.resume();
          if (response.statusCode === 200 && expectedHeader &&
              response.headers[expectedHeader.name.toLowerCase()] !== expectedHeader.value) {
            reject(new Error("Readiness response belongs to a different process (session marker mismatch)"));
          } else if (response.statusCode === 200) resolve();
          else reject(new Error(`HTTP ${response.statusCode}`));
        });
        const timer = setTimeout(() => request.destroy(new Error("request timed out")), Math.min(1000, deadline - Date.now()));
        request.once("close", () => clearTimeout(timer));
        request.once("error", reject);
      });
      signal?.throwIfAborted();
      return;
    } catch (error) {
      signal?.throwIfAborted();
      lastError = error.message;
    }
    try {
      await delay(Math.min(200, Math.max(0, deadline - Date.now())), undefined, { signal });
    } catch (error) {
      signal?.throwIfAborted();
      throw error;
    }
  }
  throw new Error(`Timed out waiting for ${url}: ${lastError}`);
}

export async function runDev(options, { repoRoot = defaultRepoRoot, spawnProcess = startOwnedProcess } = {}) {
  const controller = new AbortController();
  const owned = new Set();
  let interrupted = false;
  const interrupt = () => { interrupted = true; controller.abort(new Error("Shutdown requested")); };
  process.on("SIGINT", interrupt);
  process.on("SIGTERM", interrupt);
  const dashboard = path.join(repoRoot, "src/Meridian.Ui/dashboard");
  const vitePath = path.join(dashboard, "node_modules/vite/bin/vite.js");
  const backendUrl = `http://127.0.0.1:${options.backendPort}`;
  const frontendUrl = `http://127.0.0.1:${options.port}/workstation/`;
  const session = randomUUID();
  const expectedHeader = { name: "x-meridian-dev-session", value: session };
  const env = {
    ...process.env,
    MERIDIAN_DEV_MODE: options.mode,
    MERIDIAN_DEV_SESSION: session,
    VITE_MERIDIAN_DEV_MODE: options.mode,
    MERIDIAN_API_BASE_URL: backendUrl,
    VITE_MERIDIAN_API_BASE_URL: backendUrl,
    MERIDIAN_SCREENSHOT_CAPTURE: "false",
    DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER: "1",
    DOTNET_WATCH_SUPPRESS_BROWSER_REFRESH: "1",
    // .NET 10 defaults this to zero in --no-hot-reload mode. Immediate SIGKILL
    // prevents `dotnet run` from forwarding termination to its actual host child.
    DOTNET_WATCH_PROCESS_CLEANUP_TIMEOUT_MS: "10000",
    // Compiler/MSBuild servers outlive their callers and must not escape this session.
    MSBUILDDISABLENODEREUSE: "1",
    DOTNET_CLI_USE_MSBUILD_SERVER: "0"
  };
  const start = (label, command, args, cwd = repoRoot) => {
    controller.signal.throwIfAborted();
    console.log(`[dev] Starting ${label}`);
    const child = spawnProcess({ label, command, args, cwd, env });
    owned.add(child);
    return child;
  };
  const trackService = (child, label) => {
    void child.result.then((result) => {
      if (!controller.signal.aborted) controller.abort(new Error(`${label} exited unexpectedly (${describeExit(result)}).`));
    });
  };
  try {
    await access(vitePath).catch(() => { throw new Error("Install dashboard dependencies first: npm --prefix src/Meridian.Ui/dashboard ci"); });
    await assertPortAvailable(options.port, { label: "Vite port" });
    if (options.mode === "backend-connected") {
      await assertPortAvailable(options.backendPort, { label: "Backend port" });
      const dataDir = path.resolve(repoRoot, options.dataDir);
      await mkdir(dataDir, { recursive: true });
      const configPath = path.join(dataDir, "appsettings.dev.json");
      await writeFile(configPath, JSON.stringify({ dataRoot: path.join(dataDir, "data") }, null, 2) + "\n");
      const project = path.join(repoRoot, "src/Meridian/Meridian.csproj");
      const common = ["--config", configPath, "--http-port", String(options.backendPort)];
      console.log(`[dev] Seeded data: ${path.join(dataDir, "data/demo-workspace")}`);
      const seed = start("seed", "dotnet", ["run", "--project", project, "--no-launch-profile", "--disable-build-servers",
        "-p:UseSharedCompilation=false", "--", "--seed-demo", "--seed-only", ...common]);
      const deadline = setTimeout(() => controller.abort(new Error("Timed out building/seeding the development host.")), options.timeoutMs);
      let result;
      try { result = await abortable(seed.result, controller.signal); } finally { clearTimeout(deadline); }
      if (result.code !== 0) throw new Error(`Demo seed failed (${describeExit(result)}). Check the .NET SDK and seed output above.`);
      await seed.stop();
      owned.delete(seed);
      // Seeding can take time. Check again before starting either listener.
      await assertPortAvailable(options.backendPort, { label: "Backend port" });
      await assertPortAvailable(options.port, { label: "Vite port" });
      const backend = start("backend", "dotnet", ["watch", "--project", project, "--no-hot-reload", "--disable-build-servers",
        "run", "--no-launch-profile",
        "--property:UseSharedCompilation=false", "--", "--demo", ...common]);
      trackService(backend, "Backend watch");
      await waitForHttp(`${backendUrl}/readyz`, { timeoutMs: options.timeoutMs, signal: controller.signal, expectedHeader });
      console.log(`[dev] Backend ready: ${backendUrl} (source edits restart the host)`);
    }
    const vite = start("vite", process.execPath, [vitePath, "--host", "127.0.0.1", "--port", String(options.port), "--strictPort"], dashboard);
    trackService(vite, "Vite");
    await waitForHttp(frontendUrl, { timeoutMs: options.timeoutMs, signal: controller.signal, expectedHeader });
    console.log(`[dev] Ready (${options.mode}): ${frontendUrl}`);
    console.log("[dev] Browser hot reload is enabled. Press Ctrl+C to stop this session.");
    await abortable(new Promise(() => {}), controller.signal);
  } catch (error) {
    if (!interrupted) throw error;
  } finally {
    console.log("[dev] Stopping owned processes...");
    const results = await Promise.allSettled([...owned].reverse().map((child) => child.stop()));
    process.off("SIGINT", interrupt);
    process.off("SIGTERM", interrupt);
    const failures = results.filter((result) => result.status === "rejected");
    if (failures.length) throw new AggregateError(failures.map((result) => result.reason), "Could not stop all owned processes");
    console.log("[dev] Shutdown complete.");
  }
}

function describeExit({ code, signal, error }) {
  return error ?? (signal ? `signal ${signal}` : `exit code ${code}`);
}

function abortable(promise, signal) {
  signal.throwIfAborted();
  return new Promise((resolve, reject) => {
    const abort = () => reject(signal.reason);
    signal.addEventListener("abort", abort, { once: true });
    promise.then(resolve, reject).finally(() => signal.removeEventListener("abort", abort));
  });
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const options = parseOptions(process.argv.slice(2));
    if (options.help) console.log(help);
    else await runDev(options);
  } catch (error) {
    console.error(`[dev] ${error.message}`);
    process.exitCode = 1;
  }
}
