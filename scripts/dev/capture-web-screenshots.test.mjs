import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import fs from "node:fs/promises";
import http from "node:http";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import {
  captureRouteWithRetries,
  collectAllowedContentIssueKinds,
  createCaptureApiTracker,
  describeCaptureContentIssues,
  finalizeCaptureRun,
  findCaptureContentIssues,
  isSlugIdentifier,
  parseCaptureWorkers,
  partitionCaptureContentIssues,
  runCaptureWorkers,
  waitForCaptureReadiness,
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

function deferred() {
  let resolve;
  let reject;
  const promise = new Promise((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

test("capture workers default to one and reject unbounded or fractional counts", () => {
  assert.equal(parseCaptureWorkers(), 1);
  assert.equal(parseCaptureWorkers("2"), 2);
  assert.equal(parseCaptureWorkers(8), 8);
  for (const value of [0, -1, 1.5, 9, Infinity, "not-a-number"]) {
    assert.throws(() => parseCaptureWorkers(value), /workers.*1.*8/i);
  }
});

test("capture workers overlap within their limit and retain catalog order", { timeout: 5000 }, async () => {
  const captures = Array.from({ length: 5 }, (_, index) => ({ name: `route-${index}` }));
  const started = captures.map(() => deferred());
  const finish = captures.map(() => deferred());
  const checkpoints = [];
  let active = 0;
  let peak = 0;
  const run = runCaptureWorkers(captures, 2, async (capture, index) => {
    active += 1;
    peak = Math.max(peak, active);
    started[index].resolve();
    await finish[index].promise;
    active -= 1;
    return { name: capture.name, status: "passed" };
  }, async (results) => {
    checkpoints.push(results.map((result) => result.name));
  });

  await Promise.all([started[0].promise, started[1].promise]);
  assert.equal(active, 2);
  finish[1].resolve();
  await started[2].promise;
  finish[2].resolve();
  await started[3].promise;
  finish[0].resolve();
  await started[4].promise;
  finish[3].resolve();
  finish[4].resolve();

  assert.deepEqual((await run).map((result) => result.name), captures.map((capture) => capture.name));
  assert.equal(peak, 2);
  assert.equal(active, 0);
  assert.equal(checkpoints.length, captures.length);
  assert.deepEqual(checkpoints[0], ["route-1"]);
  for (const checkpoint of checkpoints) {
    assert.deepEqual(checkpoint, [...checkpoint].sort());
  }
});

test("concurrent completions serialize manifest checkpoints without losing results", async (t) => {
  const directory = await temporaryDirectory(t);
  const file = path.join(directory, "manifest.json");
  const captures = Array.from({ length: 12 }, (_, index) => ({ name: `route-${index}` }));
  const current = { status: "running", selectedCaptureCount: captures.length, captures: [] };
  let activeWrites = 0;
  let peakWrites = 0;
  await runCaptureWorkers(captures, 4, async (capture) => ({ ...capture, status: "passed" }), async (results) => {
    activeWrites += 1;
    peakWrites = Math.max(peakWrites, activeWrites);
    current.captures = results;
    await writeCaptureManifest(current, file, []);
    activeWrites -= 1;
  });
  const saved = JSON.parse(await fs.readFile(file, "utf8"));
  assert.equal(peakWrites, 1);
  assert.equal(saved.capturedCount, captures.length);
  assert.equal(saved.unattemptedCaptureCount, 0);
  assert.deepEqual(saved.captures.map((result) => result.name), captures.map((capture) => capture.name));
});

test("shell circuit breaker stops scheduling and drains active captures before rejecting", { timeout: 5000 }, async () => {
  const captures = Array.from({ length: 10 }, (_, index) => ({ name: `route-${index}` }));
  const started = captures.map(() => deferred());
  const finish = captures.map(() => deferred());
  const attempted = [];
  const thirdFailureSaved = deferred();
  let active = 0;
  let settled = false;
  let checkpoint;
  const run = runCaptureWorkers(captures, 2, async (capture, index) => {
    attempted.push(index);
    active += 1;
    started[index].resolve();
    await finish[index].promise;
    active -= 1;
    return { ...capture, status: "failed", error: "App shell failed to mount: missing fixtures" };
  }, async (results) => {
    checkpoint = [...results];
    if (results.length === 3) thirdFailureSaved.resolve();
  });
  const rejection = assert.rejects(run, /Aborting capture run: the app shell failed to mount/);
  run.then(() => { settled = true; }, () => { settled = true; });

  await Promise.all([started[0].promise, started[1].promise]);
  finish[0].resolve();
  await started[2].promise;
  finish[1].resolve();
  await started[3].promise;
  finish[2].resolve();
  await thirdFailureSaved.promise;
  assert.equal(settled, false, "The active route must finish before the run rejects");
  finish[3].resolve();
  await rejection;

  assert.deepEqual(attempted, [0, 1, 2, 3]);
  assert.equal(active, 0);
  assert.equal(checkpoint.length, 4, "The final active capture must remain in partial evidence");
});

test("a checkpoint failure stops pending captures and waits for active captures", { timeout: 5000 }, async () => {
  const secondCapture = deferred();
  const secondStarted = deferred();
  const checkpointFailed = deferred();
  const attempted = [];
  let settled = false;
  const run = runCaptureWorkers([0, 1, 2, 3], 2, async (_capture, index) => {
    attempted.push(index);
    if (index === 1) {
      secondStarted.resolve();
      await secondCapture.promise;
    }
    return { name: `route-${index}`, status: "passed" };
  }, async () => {
    checkpointFailed.resolve();
    throw new Error("manifest disk full");
  });
  const rejection = assert.rejects(run, /manifest disk full/);
  run.then(() => { settled = true; }, () => { settled = true; });
  await Promise.all([secondStarted.promise, checkpointFailed.promise]);
  assert.equal(settled, false);
  secondCapture.resolve();
  await rejection;
  assert.deepEqual(attempted, [0, 1]);
});

test("an unexpected worker failure drains other active work before rejecting", { timeout: 5000 }, async () => {
  const failure = deferred();
  const finishOther = deferred();
  const otherStarted = deferred();
  const attempted = [];
  let otherFinished = false;
  const run = runCaptureWorkers([0, 1, 2], 2, async (_capture, index) => {
    attempted.push(index);
    if (index === 0) await failure.promise;
    else {
      otherStarted.resolve();
      await finishOther.promise;
      otherFinished = true;
    }
    return { name: `route-${index}`, status: "passed" };
  }, async () => {});
  const rejection = assert.rejects(run, /browser disconnected/);
  await otherStarted.promise;
  failure.reject(new Error("browser disconnected"));
  // Let the rejected worker settle while its peer remains deliberately active.
  await new Promise((resolve) => setImmediate(resolve));
  assert.equal(otherFinished, false);
  finishOther.resolve();
  await rejection;
  assert.equal(otherFinished, true);
  assert.deepEqual(attempted, [0, 1]);
});

test("render readiness uses its timeout and never swallows an unfinished render", async () => {
  const page = {
    async waitForFunction(_predicate, _argument, options) {
      assert.equal(options.timeout, 1234);
      throw new Error("Fonts or content did not finish rendering");
    }
  };
  await assert.rejects(waitForCaptureReadiness(page, 1234), /did not finish rendering/);
});

function captureRequest(path) {
  return { url: () => new URL(path, "http://localhost").href };
}

test("render readiness waits for pending API data even without a busy element", async (t) => {
  const page = new EventEmitter();
  let renderChecks = 0;
  page.waitForFunction = async () => { renderChecks += 1; };
  const tracker = createCaptureApiTracker(page);
  t.after(() => tracker.dispose());
  const request = captureRequest("/api/portfolio/positions");
  page.emit("request", request);
  const run = waitForCaptureReadiness(page, 1000, tracker);
  await Promise.resolve();
  assert.equal(renderChecks, 0, "A stable loading panel is not completed API content");
  page.emit("requestfinished", request);
  await run;
  assert.equal(renderChecks, 1);
});

test("render readiness rechecks after API work starts during a painted-frame check", async (t) => {
  const page = new EventEmitter();
  const request = captureRequest("/api/ledger/balances");
  const requestStarted = deferred();
  let renderChecks = 0;
  let finished = false;
  page.waitForFunction = async () => {
    renderChecks += 1;
    if (renderChecks === 1) {
      page.emit("request", request);
      requestStarted.resolve();
    }
  };
  const tracker = createCaptureApiTracker(page);
  t.after(() => tracker.dispose());
  const run = waitForCaptureReadiness(page, 1000, tracker).then(() => { finished = true; });
  await requestStarted.promise;
  await Promise.resolve();
  assert.equal(finished, false);
  assert.equal(renderChecks, 1);
  page.emit("requestfinished", request);
  await run;
  assert.equal(renderChecks, 2, "New API data must receive another stable render check");
});

test("API work completed during a render check still invalidates the checked content", async (t) => {
  const page = new EventEmitter();
  let renderChecks = 0;
  page.waitForFunction = async () => {
    renderChecks += 1;
    if (renderChecks === 1) {
      const request = captureRequest("/api/reporting/runs");
      page.emit("request", request);
      page.emit("requestfinished", request);
    }
  };
  const tracker = createCaptureApiTracker(page);
  t.after(() => tracker.dispose());
  await waitForCaptureReadiness(page, 1000, tracker);
  assert.equal(tracker.pendingCount, 0);
  assert.equal(renderChecks, 2, "A changed request revision needs another render check even when already drained");
});

test("API request tracking releases failed requests and ignores similarly named Vite modules", async (t) => {
  const page = new EventEmitter();
  const tracker = createCaptureApiTracker(page);
  t.after(() => tracker.dispose());
  for (const path of ["/workstation/src/lib/api.ts", "/workstation/src/api/client.ts", "/apiary/status", "/workstation/?next=/api/ledger"]) {
    page.emit("request", captureRequest(path));
  }
  assert.equal(tracker.pendingCount, 0);
  assert.equal(tracker.revision, 0);
  const request = captureRequest("/api/ledger/balances");
  page.emit("request", request);
  assert.equal(tracker.pendingCount, 1);
  const idle = tracker.waitForIdle(1000);
  page.emit("requestfailed", request);
  await idle;
  assert.equal(tracker.pendingCount, 0);
  assert.ok(tracker.revision > 0);
  tracker.dispose();
  for (const event of ["request", "requestfinished", "requestfailed"]) {
    assert.equal(page.listenerCount(event), 0);
  }
});

test("an API request that never completes fails readiness within its budget", async (t) => {
  const page = new EventEmitter();
  let renderChecks = 0;
  page.waitForFunction = async () => { renderChecks += 1; };
  const tracker = createCaptureApiTracker(page);
  t.after(() => tracker.dispose());
  page.emit("request", captureRequest("/api/portfolio/positions"));
  await assert.rejects(waitForCaptureReadiness(page, 25, tracker), /API|timed out/i);
  assert.equal(renderChecks, 0);
});

test("API changes during rendering share one total readiness deadline", async (t) => {
  let now = 1000;
  t.mock.method(Date, "now", () => now);
  const apiTracker = { pendingCount: 0, revision: 0, async waitForIdle() {} };
  const budgets = [];
  const page = {
    async waitForFunction(_predicate, _argument, { timeout }) {
      budgets.push(timeout);
      now += 60;
      apiTracker.revision += 1;
    }
  };
  await assert.rejects(waitForCaptureReadiness(page, 100, apiTracker), /did not settle within 100ms/);
  assert.deepEqual(budgets, [100, 40], "API changes must not restart the full readiness timeout");
});

test("render readiness rejects loading fonts, busy content, images and shifting layouts", async (t) => {
  const bounds = { x: 0, y: 0, width: 100, height: 80 };
  const root = {
    innerText: "Account records loaded",
    scrollWidth: 100,
    scrollHeight: 80,
    busy: false,
    matches() { return this.busy; },
    getClientRects() { return [bounds]; },
    getBoundingClientRect() { return bounds; },
    querySelectorAll(selector) { return selector === "*" ? [child] : busyChildren; }
  };
  const child = {
    hidden: false,
    getClientRects() { return this.hidden ? [] : [bounds]; },
    getBoundingClientRect() { return bounds; }
  };
  let busyChildren = [];
  let onFrame = () => {};
  let frames = 0;
  const document = { fonts: { status: "loaded" }, images: [], querySelector: () => root };
  for (const [name, value] of Object.entries({
    document,
    getComputedStyle: (element) => ({ visibility: element.hidden ? "hidden" : "visible" }),
    requestAnimationFrame(callback) { frames += 1; onFrame(frames); callback(); }
  })) {
    const previous = Object.getOwnPropertyDescriptor(globalThis, name);
    Object.defineProperty(globalThis, name, { configurable: true, writable: true, value });
    t.after(() => {
      if (previous) Object.defineProperty(globalThis, name, previous);
      else delete globalThis[name];
    });
  }
  async function ready() {
    let result;
    frames = 0;
    await waitForCaptureReadiness({ async waitForFunction(predicate) { result = await predicate(); } }, 100);
    return result;
  }

  assert.equal(await ready(), true);
  assert.equal(frames, 2, "Readiness must compare painted frames");
  document.fonts.status = "loading";
  assert.equal(await ready(), false);
  document.fonts.status = "loaded";
  root.busy = true;
  assert.equal(await ready(), false);
  root.busy = false;
  busyChildren = [child];
  assert.equal(await ready(), false);
  child.hidden = true;
  assert.equal(await ready(), true, "Hidden busy regions must not block visible content");
  child.hidden = false;
  busyChildren = [];
  document.images = [{ ...child, complete: false }];
  assert.equal(await ready(), false);
  document.images[0].complete = true;
  assert.equal(await ready(), true);
  onFrame = (frame) => { if (frame === 2) bounds.width += 10; };
  assert.equal(await ready(), false, "A layout change during paint must delay capture");
  onFrame = (frame) => { if (frame === 2) root.innerText = "New account records loaded"; };
  assert.equal(await ready(), false, "A content change during paint must delay capture");
  onFrame = () => {};
  assert.equal(await ready(), true);
});

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
    async waitForFunction() {},
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

test("a ready shell cannot capture before its configured loaded-data text appears", { timeout: 5000 }, async (t) => {
  const directory = await temporaryDirectory(t);
  const waitingForData = deferred();
  const dataVisible = deferred();
  t.after(() => dataVisible.resolve());
  let captured = false;
  let closed = false;
  const page = Object.assign(new EventEmitter(), {
    async route() {},
    async setViewportSize() {},
    async goto() {},
    async waitForSelector() {},
    async waitForFunction() {},
    getByText(text) {
      return {
        filter() { return this; },
        first() { return this; },
        async waitFor() {
          if (text === "Loaded ledger postings") {
            waitingForData.resolve();
            await dataVisible.promise;
          }
        }
      };
    },
    async evaluate() { return 100; },
    url() { return "http://localhost/workstation/accounting"; },
    async screenshot({ path: file }) {
      captured = true;
      const image = Buffer.alloc(100);
      await fs.writeFile(file, image);
      return image;
    }
  });
  const browser = {
    async newContext() {
      return {
        async newPage() { return page; },
        async close() { closed = true; }
      };
    }
  };
  const run = captureRouteWithRetries(
    browser,
    { name: "accounting", path: "/accounting", waitForText: "Ledger", waitForTexts: ["Loaded ledger postings"] },
    () => ({ assertNoUnexpectedRequests() {} }),
    directory,
    "http://localhost/workstation",
    {}, 50, 1, 100, 1000, 1, () => {}
  );
  await waitingForData.promise;
  assert.equal(captured, false, "Heading and layout readiness must not accept an unfinished data region");
  assert.equal(closed, false, "The page must stay open for its loaded-data assertion");
  dataVisible.resolve();
  assert.equal((await run).status, "passed");
  assert.equal(captured, true);
  assert.equal(closed, true);
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

test("rendered identifiers are reported once each, whatever their case", () => {
  const guid = "53bf0251-17f6-4fb7-8dbe-6fb4966e2749";
  const issues = findCaptureContentIssues(
    `Selected 2026-05 / ${guid}\nFund account ${guid.toUpperCase()}\nBook 00000000-0000-0000-0000-0000000000aa`
  );
  assert.deepEqual(issues, [
    { kind: "opaque-identifier", excerpt: guid },
    { kind: "opaque-identifier", excerpt: "00000000-0000-0000-0000-0000000000aa" }
  ]);
  // Readable codes, dates and partial hex are not identifiers.
  assert.deepEqual(findCaptureContentIssues("Period 2026-05 · fund-alpha · deadbeef-cafe"), []);
});

test("request paths and words about the system are reported", () => {
  assert.deepEqual(
    findCaptureContentIssues(
      "Recurring queue unavailable: Request failed for /api/ledger/journal-automation/recurring/occurrences?fundProfileId=x (500)\n"
      + "Strategy Lane · PlotTool\nNo payload is available.\nLegacy pack mutations are retired.\nCaller-visible Reporting records"
    ),
    [
      { kind: "api-path", excerpt: "/api/ledger/journal-automation/recurring/occurrences?fundProfileId=x" },
      { kind: "internal-term", excerpt: "Lane" },
      { kind: "internal-term", excerpt: "payload" },
      { kind: "internal-term", excerpt: "mutations" },
      { kind: "internal-term", excerpt: "Caller-visible" }
    ]
  );
  // Words that merely contain a banned term, and a path that is not a request path, are fine.
  assert.deepEqual(findCaptureContentIssues("Planet · explanation · permutation · Rapid API usage · api/keys"), []);
});

test("slug-shaped record identifiers are reported and ordinary compounds are not", () => {
  for (const slug of ["run-dev-1", "default-fund", "entity-fund-alpha", "sec-dev-001", "investor-anchor-lp", "cfo-1"]) {
    assert.equal(isSlugIdentifier(slug), true, slug);
  }
  for (const compound of [
    "as-of", "read-only", "point-in-time", "end-of-day", "fund-level", "account-period", "tier-1", "phase-2",
    "rule-144a", "mark-to-market", "strat-mean-reversion", "fund"
  ]) {
    assert.equal(isSlugIdentifier(compound), false, compound);
  }
  assert.deepEqual(
    findCaptureContentIssues("Fund: default-fund, Entity: entity-fund-alpha as-of 2026-06-30 (read-only), run run-dev-1"),
    [
      { kind: "slug-identifier", excerpt: "default-fund" },
      { kind: "slug-identifier", excerpt: "entity-fund-alpha" },
      { kind: "slug-identifier", excerpt: "run-dev-1" }
    ]
  );
  // Dates, file names, addresses and upper-case codes are not slugs.
  assert.deepEqual(findCaptureContentIssues("2026-06-30 · notes-2026.pdf · ops-1@example.com · PO-1 · FL-1"), []);
});

test("a disabled primary action without a reason is reported from the DOM probe", () => {
  assert.deepEqual(
    findCaptureContentIssues("Submit approval", { unexplainedDisabledActions: ["Submit approval", "Submit approval", ""] }),
    [
      { kind: "disabled-primary-without-reason", excerpt: "Submit approval" },
      { kind: "disabled-primary-without-reason", excerpt: "(unlabelled button)" }
    ]
  );
  const excused = partitionCaptureContentIssues("", {
    allowedContentIssues: [{ kind: "disabled-primary-without-reason", reason: "Reason is shown inline beside the button." }]
  }, { unexplainedDisabledActions: ["Add symbol set"] });
  assert.equal(excused.blocking.length, 0);
  assert.equal(excused.allowed.length, 1);
});

test("a status bar that contradicts the page's freshness is reported", () => {
  const contradiction = 'status bar "Sync up to date Workspace Portfolio" vs page "Freshness Needs review Stale · 187d ago"';
  assert.deepEqual(
    findCaptureContentIssues("Portfolio", { statusContradictions: [contradiction] }),
    [{ kind: "status-contradiction", excerpt: contradiction }]
  );
  assert.deepEqual(findCaptureContentIssues("Portfolio", { statusContradictions: [] }), []);
});

test("a count of one before a plural noun is reported", () => {
  assert.deepEqual(
    findCaptureContentIssues("1 ROWS\n1 templates · 2 jobs\nShowing 1 packages.\n(1 checks)").map((issue) => issue.excerpt),
    ["1 ROWS", "1 templates", "1 packages", "1 checks"]
  );
});

test("correct singulars, larger counts and verbs are not reported as plural mistakes", () => {
  const clean = [
    "1 row", "11 rows", "21 rows", "0.1 rows", "$1 fees", "2026-01-1 runs", "1 of 4 gates passed",
    "Slice 1 uses the configured provider", "Step 1 needs review", "1 business day", "1 status",
    "1 basis", "1 series", "1 provider blocks dependent workflows", "Tier 1 requires approval",
    // A plural-form modifier inside a singular phrase is correct.
    "1 operations continuity workflow loaded.", "1 securities account", "1 sales order",
    // A ratio counts the whole set.
    "1 of 1 loaded breaks", "Showing 1 of 1 breaks", "Step 1 fails."
  ].join("\n");
  assert.deepEqual(findCaptureContentIssues(clean), []);
  assert.deepEqual(findCaptureContentIssues(""), []);
  assert.deepEqual(findCaptureContentIssues(null), []);
});

test("a capture is only excused from a content rule it names with a reason", () => {
  const text = "1 rows · 53bf0251-17f6-4fb7-8dbe-6fb4966e2749";
  assert.equal(partitionCaptureContentIssues(text, { name: "plain" }).blocking.length, 2);

  const excused = partitionCaptureContentIssues(text, {
    name: "excused",
    allowedContentIssues: [{ kind: "opaque-identifier", reason: "Contract exposes no display name yet." }]
  });
  assert.deepEqual(excused.allowed.map((issue) => issue.kind), ["opaque-identifier"]);
  assert.deepEqual(excused.blocking.map((issue) => issue.kind), ["singular-count-plural-noun"]);

  // An exception with no reason, or for an unknown rule, excuses nothing.
  assert.equal(collectAllowedContentIssueKinds({ allowedContentIssues: [{ kind: "opaque-identifier" }] }).size, 0);
  assert.equal(collectAllowedContentIssueKinds({ allowedContentIssues: [{ kind: "typo", reason: "x" }] }).size, 0);
  assert.equal(collectAllowedContentIssueKinds({ allowedContentIssues: "opaque-identifier" }).size, 0);
});

test("the content-issue failure names the route, the excerpts and the remedy", () => {
  const issues = Array.from({ length: 7 }, (_, index) => ({ kind: "singular-count-plural-noun", excerpt: `1 thing${index}s` }));
  const message = describeCaptureContentIssues("web-example", issues);
  assert.match(message, /^Capture web-example renders 7 operator-copy issue\(s\)/);
  assert.match(message, /singular-count-plural-noun "1 thing0s"/);
  assert.match(message, /and 2 more/);
  assert.match(message, /allowedContentIssues/);
});
