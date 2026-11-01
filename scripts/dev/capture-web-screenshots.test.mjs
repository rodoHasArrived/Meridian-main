import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import fs from "node:fs/promises";
import http from "node:http";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import {
  captureRouteWithRetries,
  finalizeCaptureRun,
  waitForServer,
  writeCaptureManifest
} from "./capture-web-screenshots.mjs";

async function temporaryDirectory(t) {
  const directory = await fs.mkdtemp(path.join(os.tmpdir(), "meridian-capture-"));
  t.after(() => fs.rm(directory, { recursive: true, force: true }));
  return directory;
}

function manifest(status = "running") {
  return {
    status,
    selectedCaptureCount: 4,
    captures: [{ name: "first", status: "passed" }, { name: "second", status: "failed" }]
  };
}

test("incremental manifest retains completed routes, unattempted count, and the log tail", async (t) => {
  const directory = await temporaryDirectory(t);
  const file = path.join(directory, "artifacts", "manifest.json");
  const current = manifest();
  await writeCaptureManifest(current, file, Array.from({ length: 250 }, (_, index) => `log ${index}`));
  const saved = JSON.parse(await fs.readFile(file, "utf8"));
  assert.equal(saved.status, "running");
  assert.equal(saved.capturedCount, 1);
  assert.equal(saved.failedCaptureCount, 1);
  assert.equal(saved.unattemptedCaptureCount, 2);
  assert.equal(saved.logs.length, 200);
  assert.equal(saved.logs[0], "log 50");
  assert.deepEqual(saved.captures, current.captures);
  assert.equal((await fs.readdir(path.dirname(file))).includes("manifest.json.tmp"), false);
});

test("cleanup failures preserve the primary capture error and still write diagnostics", async (t) => {
  const directory = await temporaryDirectory(t);
  const file = path.join(directory, "manifest.json");
  const current = { ...manifest("failed"), error: "app shell failed" };
  const cleaned = [];
  await finalizeCaptureRun(current, file, new Date(), ["server log"], [
    { name: "browser", run() { cleaned.push("browser"); throw new Error("browser already disconnected"); } },
    { name: "server", run() { cleaned.push("server"); } },
    { name: "scenarios", async run() { cleaned.push("scenarios"); throw new Error("close failed"); } }
  ]);
  assert.deepEqual(cleaned, ["browser", "server", "scenarios"]);
  const saved = JSON.parse(await fs.readFile(file, "utf8"));
  assert.equal(saved.status, "failed");
  assert.equal(saved.error, "app shell failed");
  assert.deepEqual(saved.cleanupErrors, ["browser: browser already disconnected", "scenarios: close failed"]);
  assert.equal(saved.unattemptedCaptureCount, 2);
  assert.ok(saved.finishedAtUtc);
  assert.ok(saved.durationSeconds >= 0);
});

test("a cleanup failure after capture success fails the run after retaining its manifest", async (t) => {
  const directory = await temporaryDirectory(t);
  const file = path.join(directory, "manifest.json");
  await assert.rejects(
    finalizeCaptureRun(manifest("passed"), file, new Date(), [], [
      { name: "browser", run() { throw new Error("close failed"); } }
    ]),
    /Capture cleanup failed: browser: close failed/
  );
  assert.equal(JSON.parse(await fs.readFile(file, "utf8")).status, "failed");
});

async function captureWithStub(outputDir, { bytes = 100, strictError = false, closeError = false } = {}) {
  let closed = false;
  const page = new EventEmitter();
  Object.assign(page, {
    async route() {},
    async setViewportSize() {},
    async goto() {},
    async waitForSelector() {},
    async waitForLoadState() {},
    async evaluate() { return 100; },
    url() { return "http://localhost/workstation/accounting"; },
    async screenshot({ path: file }) {
      const image = Buffer.alloc(bytes);
      await fs.writeFile(file, image);
      return image;
    }
  });
  const browser = {
    async newContext() {
      return {
        async newPage() { return page; },
        async close() { closed = true; if (closeError) throw new Error("Context close failed"); }
      };
    }
  };
  const createResolver = () => ({
    assertNoUnexpectedRequests() { if (strictError) throw new Error("Unexpected API request"); }
  });
  const promise = captureRouteWithRetries(
    browser,
    { id: "W-test", name: "accounting", path: "/accounting" },
    createResolver,
    outputDir,
    "http://localhost/workstation",
    {}, 50, 1, 100, 100, 1, () => {}
  );
  return { promise, isClosed: () => closed };
}

for (const [name, options, expectedError] of [
  ["an undersized image", { bytes: 2 }, /below minimum 50/],
  ["an unexpected strict API request", { strictError: true }, /Unexpected API request/]
]) {
  test(`a failed capture removes its PNG after ${name}`, async (t) => {
    const directory = await temporaryDirectory(t);
    const run = await captureWithStub(directory, options);
    await assert.rejects(run.promise, expectedError);
    await assert.rejects(fs.access(path.join(directory, "accounting.png")), { code: "ENOENT" });
    assert.equal(run.isClosed(), true);
  });
}

test("a passed capture retains its PNG", async (t) => {
  const directory = await temporaryDirectory(t);
  const run = await captureWithStub(directory);
  assert.equal((await run.promise).status, "passed");
  assert.equal((await fs.stat(path.join(directory, "accounting.png"))).size, 100);
  assert.equal(run.isClosed(), true);
});

test("a context close failure preserves the original render error", async (t) => {
  const directory = await temporaryDirectory(t);
  const run = await captureWithStub(directory, { strictError: true, closeError: true });
  await assert.rejects(run.promise, /Unexpected API request/);
  await assert.rejects(fs.access(path.join(directory, "accounting.png")), { code: "ENOENT" });
});

test("a context close failure after capture removes the rejected PNG", async (t) => {
  const directory = await temporaryDirectory(t);
  const run = await captureWithStub(directory, { closeError: true });
  await assert.rejects(run.promise, /Context close failed/);
  await assert.rejects(fs.access(path.join(directory, "accounting.png")), { code: "ENOENT" });
});

async function serverUrl(t, handler) {
  const server = http.createServer(handler);
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  t.after(async () => {
    server.closeAllConnections();
    await new Promise((resolve) => server.close(resolve));
  });
  return `http://127.0.0.1:${server.address().port}/workstation/`;
}

test("server readiness is bounded when a server accepts connections without responding", async (t) => {
  const url = await serverUrl(t, () => {});
  const started = Date.now();
  await assert.rejects(waitForServer(url, 100, null, []), /Timed out waiting/);
  assert.ok(Date.now() - started < 2000, "A hung fetch must respect the readiness budget");
});

test("an HTTP404 is not a ready workstation", async (t) => {
  // Classify a received response independently of cold HTTP-client startup. The hung-socket
  // case above exercises the real connection deadline.
  const probe = t.mock.method(globalThis, "fetch", async () => new Response(null, { status: 404 }));
  await assert.rejects(waitForServer("http://workstation.invalid/workstation/", 100, null, []), /HTTP 404/);
  assert.ok(probe.mock.callCount() > 0, "Readiness must inspect the unsuccessful HTTP response");
});

test("readiness reports an exited child with the dev-server log tail", async () => {
  const child = Object.assign(new EventEmitter(), { exitCode: 1, signalCode: null });
  await assert.rejects(waitForServer("http://localhost", 100, child, ["Vite configuration failed"]), /exit code 1.*\nVite configuration failed/);
  assert.equal(child.listenerCount("error"), 0);
});

test("readiness reports spawn errors and removes its child listeners", async (t) => {
  const child = Object.assign(new EventEmitter(), { exitCode: null, signalCode: null });
  const url = await serverUrl(t, (_request, response) => {
    child.emit("error", new Error("spawn npm ENOENT"));
    response.writeHead(503).end();
  });
  await assert.rejects(waitForServer(url, 100, child, []), /Dev server could not start: spawn npm ENOENT/);
  assert.equal(child.listenerCount("exit"), 0);
  assert.equal(child.listenerCount("error"), 0);
});
