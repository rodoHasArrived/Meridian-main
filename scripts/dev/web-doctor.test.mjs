import test from "node:test";
import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import { createServer } from "node:http";
import { copyFile, mkdir, mkdtemp, readFile, readdir, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { promisify } from "node:util";
import { diagnoseWeb, formatDiagnosis, supportsNode } from "./web-doctor.mjs";
import { parseOptions } from "./web-dev.mjs";

const execute = promisify(execFile);
const fixtureMode = { mode: "fixture-only", port: 5173, backendPort: 8080 };
const backendMode = { ...fixtureMode, mode: "backend-connected" };
const available = async () => {};

async function fixture(t, { dependencies = true } = {}) {
  const repoRoot = await mkdtemp(path.join(os.tmpdir(), "meridian-web-doctor-"));
  t.after(() => rm(repoRoot, { recursive: true, force: true }));
  const dashboard = path.join(repoRoot, "src/Meridian.Ui/dashboard");
  await mkdir(dashboard, { recursive: true });
  await writeFile(path.join(dashboard, "package-lock.json"), JSON.stringify({
    packages: { "node_modules/vite": { engines: { node: "^20.19.0 || >=22.12.0" } } },
  }));
  await writeFile(path.join(repoRoot, "global.json"), JSON.stringify({
    sdk: { version: "10.0.100", rollForward: "latestMinor", allowPrerelease: true },
  }));
  if (dependencies) {
    await mkdir(path.join(dashboard, "node_modules/vite/bin"), { recursive: true });
    await writeFile(path.join(dashboard, "node_modules/vite/bin/vite.js"),
      "if (process.argv[2] !== '--version') throw Error('Doctor must not launch Vite'); console.log('vite/8.3.5 test-platform node-v24.0.0');");
  }
  return repoRoot;
}

test("doctor flags keep mode selection explicit and JSON limited to diagnostics", () => {
  assert.throws(() => parseOptions(["--doctor"]), /mode/i);
  assert.throws(() => parseOptions(["--mode", "fixture-only", "--json"]), /requires --doctor/);
  assert.throws(() => parseOptions(["--mode", "fixture-only", "--doctor=true"]), /option/);
  assert.throws(() => parseOptions(["--mode", "fixture-only", "--doctor", "--doctor"]), /repeated/);
  assert.equal(parseOptions(["--mode", "fixture-only", "--json", "--doctor"]).doctor, true);
});

test("Node compatibility follows the locked Vite engine boundaries", () => {
  const range = "^20.19.0 || >=22.12.0";
  for (const version of ["20.19.0", "20.20.1", "22.12.0", "24.0.0"]) assert.ok(supportsNode(version, range), version);
  for (const version of ["18.20.0", "20.18.9", "21.99.0", "22.11.9", "22.12.0-rc.1", "24.0.0-nightly"]) {
    assert.equal(supportsNode(version, range), false, version);
  }
  assert.throws(() => supportsNode("24.0.0", ">=22"), /Unsupported/);
});

test("fixture diagnosis never probes .NET or the backend port", async (t) => {
  const repoRoot = await fixture(t);
  const ports = [];
  const report = await diagnoseWeb(fixtureMode, {
    repoRoot, nodeVersion: "24.0.0",
    runCommand: async () => { throw Error("fixture mode must not call .NET"); },
    checkPort: async (port) => ports.push(port),
  });
  assert.equal(report.ok, true);
  assert.deepEqual(report.checks.map(({ id }) => id), ["node", "dashboard-dependencies", "vite-port"]);
  assert.deepEqual(ports, [5173]);
  assert.match(formatDiagnosis(report), /prerequisites available/);
});

test("doctor reports independent failures together with actionable fixes", async (t) => {
  const repoRoot = await fixture(t, { dependencies: false });
  const report = await diagnoseWeb(backendMode, {
    repoRoot, nodeVersion: "22.11.0",
    runCommand: async () => { throw Object.assign(Error("missing"), { code: "ENOENT" }); },
    checkPort: async () => { throw Error("occupied"); },
  });
  assert.equal(report.ok, false);
  assert.equal(report.checks.filter(({ status }) => status === "fail").length, 5);
  const output = formatDiagnosis(report);
  assert.match(output, /Install Node.js 24/);
  assert.match(output, /ci --include=optional/);
  assert.match(output, /global.json/);
  assert.match(output, /--backend-port/);
  assert.match(output, /--port/);
});

test("doctor catches a Vite install whose native dependencies cannot load", async (t) => {
  const repoRoot = await fixture(t);
  await writeFile(path.join(repoRoot, "src/Meridian.Ui/dashboard/node_modules/vite/bin/vite.js"),
    "throw Error('Missing native binding');");
  const report = await diagnoseWeb(fixtureMode, { repoRoot, nodeVersion: "24.0.0", checkPort: available });
  const dependencies = report.checks.find(({ id }) => id === "dashboard-dependencies");
  assert.equal(dependencies.status, "fail");
  assert.match(dependencies.detail, /native\/optional/);
  assert.match(dependencies.remedy, /include=optional/);
});

test("Vite probe is bounded, disables compile-cache writes, and requires valid output", async (t) => {
  const repoRoot = await fixture(t);
  let call;
  const report = await diagnoseWeb(fixtureMode, {
    repoRoot, nodeVersion: "24.0.0", checkPort: available,
    runVite: async (...args) => { call = args; return { stdout: "unrecognized CLI" }; },
  });
  assert.equal(report.ok, false);
  assert.equal(call[0], process.execPath);
  assert.equal(call[1][1], "--version");
  assert.equal(call[2].timeout, 10_000);
  assert.equal(call[2].env.NODE_DISABLE_COMPILE_CACHE, "1");
});

test("SDK probe runs in the repo so dotnet applies global.json selection", async (t) => {
  const repoRoot = await fixture(t);
  let call;
  const report = await diagnoseWeb(backendMode, {
    repoRoot, nodeVersion: "24.0.0", checkPort: available,
    runCommand: async (...args) => { call = args; return { stdout: "10.0.201\n" }; },
  });
  assert.equal(report.ok, true);
  assert.deepEqual(call.slice(0, 2), ["dotnet", ["--version"]]);
  assert.equal(call[2].cwd, repoRoot);
  assert.equal(call[2].timeout, 10_000);
  assert.match(report.checks.find(({ id }) => id === "dotnet-sdk").detail, /10.0.201/);
});

test("an incompatible SDK, malformed version, or timed out probe blocks connected mode", async (t) => {
  const repoRoot = await fixture(t);
  for (const stdout of ["9.0.200", "not-a-version", ""]) {
    const report = await diagnoseWeb(backendMode, {
      repoRoot, nodeVersion: "24.0.0", checkPort: available,
      runCommand: async () => ({ stdout }),
    });
    assert.equal(report.checks.find(({ id }) => id === "dotnet-sdk").status, "fail");
  }
  const report = await diagnoseWeb(backendMode, {
    repoRoot, nodeVersion: "24.0.0", checkPort: available,
    runCommand: async () => { throw Object.assign(Error("timeout"), { killed: true }); },
  });
  assert.equal(report.ok, false);
});

test("a malformed manifest blocks its check without hiding port diagnostics", async (t) => {
  const repoRoot = await fixture(t);
  await writeFile(path.join(repoRoot, "src/Meridian.Ui/dashboard/package-lock.json"), "not JSON");
  const report = await diagnoseWeb(fixtureMode, { repoRoot, nodeVersion: "24.0.0", checkPort: available });
  assert.equal(report.ok, false);
  assert.equal(report.checks.find(({ id }) => id === "vite-port").status, "pass");
});

async function installLauncher(repoRoot) {
  const destination = path.join(repoRoot, "scripts/dev");
  await mkdir(destination, { recursive: true });
  for (const file of ["web-dev.mjs", "web-doctor.mjs", "owned-process.mjs"]) {
    await copyFile(new URL(file, import.meta.url), path.join(destination, file));
  }
  return path.join(destination, "web-dev.mjs");
}

async function openServer(t) {
  const server = createServer((_request, response) => response.end("unrelated developer server"));
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  t.after(() => new Promise((resolve) => server.close(resolve)));
  return server;
}

test("real fixture doctor CLI succeeds without .NET, global.json, or launching Vite", async (t) => {
  const repoRoot = await fixture(t);
  const script = await installLauncher(repoRoot);
  await rm(path.join(repoRoot, "global.json"));
  const server = await openServer(t);
  const port = server.address().port;
  await new Promise((resolve) => server.close(resolve));
  const before = await readdir(repoRoot, { recursive: true });
  const { stdout } = await execute(process.execPath, [
    script, "--mode", "fixture-only", "--doctor", "--json", "--port", String(port),
  ], { cwd: repoRoot });
  const report = JSON.parse(stdout);
  assert.equal(report.ok, true);
  assert.equal(report.mode, "fixture-only");
  assert.equal(report.checks.some(({ id }) => id === "dotnet-sdk"), false);
  assert.deepEqual(await readdir(repoRoot, { recursive: true }), before);
});

test("real doctor CLI diagnoses an occupied port without changing files or stopping its listener", async (t) => {
  const repoRoot = await fixture(t);
  const script = await installLauncher(repoRoot);
  const server = await openServer(t);
  const port = server.address().port;
  const before = await readdir(repoRoot, { recursive: true });
  let result;
  try {
    await execute(process.execPath, [script, "--mode", "fixture-only", "--doctor", "--json", "--port", String(port)], { cwd: repoRoot });
    assert.fail("occupied port must return nonzero");
  } catch (error) {
    assert.equal(error.code, 1);
    result = JSON.parse(error.stdout);
  }
  assert.equal(result.ok, false);
  assert.equal(result.checks.find(({ id }) => id === "vite-port").status, "fail");
  assert.equal(await (await fetch(`http://127.0.0.1:${port}`)).text(), "unrelated developer server");
  assert.deepEqual(await readdir(repoRoot, { recursive: true }), before);
  assert.match(await readFile(path.join(repoRoot, "src/Meridian.Ui/dashboard/node_modules/vite/bin/vite.js"), "utf8"), /must not launch/);
});

test("real connected doctor CLI reports a missing SDK with nonzero status and JSON output", async (t) => {
  const repoRoot = await fixture(t);
  const script = await installLauncher(repoRoot);
  // Absolute Node executable still runs with an empty command search path.
  const env = { ...process.env, PATH: path.join(repoRoot, "no-tools") };
  for (const key of Object.keys(env)) if (key.toLowerCase() === "path" && key !== "PATH") delete env[key];
  try {
    await execute(process.execPath, [script, "--mode", "backend-connected", "--doctor", "--json"], { cwd: repoRoot, env });
    assert.fail("missing SDK must return nonzero");
  } catch (error) {
    assert.equal(error.code, 1);
    const report = JSON.parse(error.stdout);
    assert.equal(report.checks.find(({ id }) => id === "dotnet-sdk").status, "fail");
    assert.match(report.checks.find(({ id }) => id === "dotnet-sdk").remedy, /global.json/);
  }
});
