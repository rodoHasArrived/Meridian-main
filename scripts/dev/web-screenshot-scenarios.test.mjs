import assert from "node:assert/strict";
import { after, before, test } from "node:test";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { readFile } from "node:fs/promises";
import {
  createScreenshotApiResolver,
  loadScreenshotScenarioModule,
  scenarioCapture,
  setupScenarioApiMocking
} from "./web-screenshot-scenarios.mjs";

const repoRoot = fileURLToPath(new URL("../../", import.meta.url));
let source;
before(async () => {
  source = await loadScreenshotScenarioModule(path.join(repoRoot, "src/Meridian.Ui/dashboard"));
});
after(async () => source?.close());

test("default accounting screenshots use the typed payload without a JSON copy", async () => {
  const fixtures = JSON.parse(await readFile(path.join(repoRoot, "scripts/dev/web-screenshot-fixtures.json"), "utf8"));
  assert.equal(Object.hasOwn(fixtures.routes, "/api/workstation/accounting"), false);
  const resolver = createScreenshotApiResolver(source.module, fixtures.routes, { strict: true });
  const response = resolver.resolve("http://localhost/api/workstation/accounting");
  assert.equal(response.status, 200);
  assert.deepEqual(response.body, source.module.accountingPayload);
  assert.equal(response.headers["x-meridian-scenario"], "accounting.normal");
  resolver.assertNoUnexpectedRequests();
});

test("the shared accounting payload retains the approval inbox capture's populated queue", async () => {
  const routeConfig = JSON.parse(await readFile(path.join(repoRoot, "scripts/dev/web-screenshot-routes.json"), "utf8"));
  const capture = routeConfig.captures.find((item) => item.path === "/accounting/approvals/inbox");
  assert.ok(capture, "Approval inbox capture must remain registered");
  const resolver = createScreenshotApiResolver(source.module, {}, { strict: true });
  const response = resolver.resolve("/api/workstation/accounting");
  const approvals = response.body.closePlans?.flatMap((plan) => plan.approvals) ?? [];
  assert.ok(approvals.length > 0, "Approval inbox screenshot requires a populated approval queue");
  for (const approval of approvals) {
    assert.ok(approval.approvalId, "Approval rows must link to a retained decision reference");
    assert.ok(capture.waitForTexts.includes(approval.label), "Approval readiness must match the shared payload");
  }
  resolver.assertNoUnexpectedRequests();
});

test("every screenshot variant preserves shared bodies, explicit status, headers, and delay", () => {
  for (const [id, status] of Object.entries({
    "accounting.normal": 200,
    "accounting.empty": 200,
    "accounting.delayed": 200,
    "accounting.forbidden": 403,
    "accounting.failed": 500
  })) {
    const scenario = source.module.getScenario(id);
    const expected = source.module.resolveScenarioResponse(scenario, "/api/workstation/accounting");
    const resolver = createScreenshotApiResolver(source.module, {
      "/api/workstation/accounting": { obsoleteDuplicate: true }
    }, { scenarioId: id, strict: true });
    const actual = resolver.resolve("http://localhost/api/workstation/accounting", "GET");
    assert.equal(actual.status, status, id);
    assert.deepEqual(actual, expected, id);
    if (id === "accounting.empty") assert.deepEqual(actual.body.reconciliationQueue, []);
    if (id === "accounting.delayed") assert.ok(actual.delayMs > 0);
  }
});

test("strict capture rejects unexpected children and write methods instead of prefix matching", () => {
  for (const [url, method] of [
    ["http://localhost/api/screenshot-only/undeclared?account=missing", "GET"],
    ["http://localhost/api/screenshot-only", "POST"]
  ]) {
    const resolver = createScreenshotApiResolver(source.module, {
      "/api/screenshot-only": { registered: true }
    }, { scenarioId: "accounting.normal", strict: true });
    assert.throws(() => resolver.resolve(url, method), (error) => {
      assert.match(error.message, /Unexpected API request/);
      assert.ok(error.message.includes(`${method} ${url}`));
      assert.ok(error.message.includes("accounting.normal"));
      return true;
    });
    assert.equal(resolver.unexpectedRequests.length, 1);
    assert.throws(() => resolver.assertNoUnexpectedRequests(), /Unexpected API request/);
  }
});

test("legacy fixture reads require an exact path and known supporting fixtures are available", () => {
  const resolver = createScreenshotApiResolver(source.module, {
    "/api/screenshot-only": { registered: true }
  }, { scenarioId: "accounting.normal", strict: true });
  assert.deepEqual(resolver.resolve("/api/screenshot-only?scope=all").body, { registered: true });
  assert.equal(resolver.resolve("/api/workstation/first-run/").status, 200);
  resolver.assertNoUnexpectedRequests();
});

test("the shell's declared first-run endpoint receives the retained screenshot workspace", async () => {
  const endpoints = await readFile(path.join(repoRoot, "src/Meridian.Ui/dashboard/src/lib/workstation-endpoints.ts"), "utf8");
  const statusPath = endpoints.match(/\bfirstRunStatus:\s*"([^"]+)"/)?.[1];
  assert.ok(statusPath, "The shell must declare its first-run status endpoint");
  const fixtures = JSON.parse(await readFile(path.join(repoRoot, "scripts/dev/web-screenshot-fixtures.json"), "utf8"));
  const resolver = createScreenshotApiResolver(source.module, fixtures.routes, { strict: true });
  const response = resolver.resolve(`${statusPath}?capture=activation`);

  assert.equal(response.status, 200);
  assert.equal(response.body.isComplete, true);
  assert.equal(response.body.workspace.isSample, false,
    "Screenshot activation must use the retained primary workspace, not the sample bootstrap fallback");
  assert.deepEqual(response.body, fixtures.routes[statusPath]);
  resolver.assertNoUnexpectedRequests();
});

test("the first-run fixture cannot answer unknown children or activation writes", async () => {
  const fixtures = JSON.parse(await readFile(path.join(repoRoot, "scripts/dev/web-screenshot-fixtures.json"), "utf8"));
  for (const [route, method] of [
    ["/api/workstation/first-run/unknown", "GET"],
    ["/api/workstation/first-run/", "POST"],
    ["/api/workstation/first-run/complete", "POST"]
  ]) {
    const resolver = createScreenshotApiResolver(source.module, fixtures.routes, { strict: true });
    assert.throws(() => resolver.resolve(route, method), /Unexpected API request/);
    assert.equal(resolver.unexpectedRequests.length, 1);
  }
});

test("scenario captures carry the preview selector and derive readiness from the same payload", () => {
  for (const id of Object.keys(source.module.scenarios)) {
    const scenario = source.module.getScenario(id);
    const capture = scenarioCapture({ name: "accounting", path: "/accounting/reconciliation?filter=open" }, scenario, source.module);
    assert.equal(new URL(capture.path, "http://localhost").searchParams.get("scenario"), id);
    assert.equal(capture.previewUrl, source.module.scenarioPreviewUrl(scenario));
    assert.deepEqual(capture.waitForTexts, scenario.waitForTexts);
    assert.equal(capture.scenario, id);
    assert.ok(capture.name.includes(id.replaceAll(".", "-")));
  }
  const normal = source.module.getScenario("accounting.normal");
  assert.ok(normal.waitForTexts.includes(source.module.accountingPayload.reconciliationQueue[0].strategyName));
});

test("populated scenarios preserve each selected route's readiness contract", async () => {
  const routeConfig = JSON.parse(await readFile(path.join(repoRoot, "scripts/dev/web-screenshot-routes.json"), "utf8"));
  const inbox = routeConfig.captures.find((capture) => capture.id === "W03I4");
  const reconciliation = routeConfig.captures.find((capture) => capture.id === "W03A");
  for (const id of ["accounting.normal", "accounting.delayed"]) {
    const scenario = source.module.getScenario(id);
    const selectedInbox = scenarioCapture(inbox, scenario, source.module);
    assert.equal(selectedInbox.waitForText, inbox.waitForText);
    assert.deepEqual(selectedInbox.waitForTexts, inbox.waitForTexts);
    assert.ok(!selectedInbox.waitForTexts.includes(source.module.accountingPayload.reconciliationQueue[0].strategyName));

    const selectedReconciliation = scenarioCapture(reconciliation, scenario, source.module);
    assert.equal(selectedReconciliation.waitForText, reconciliation.waitForText);
    for (const text of [...reconciliation.waitForTexts, ...scenario.waitForTexts]) {
      assert.ok(selectedReconciliation.waitForTexts.includes(text), `Missing readiness text: ${text}`);
    }
    assert.ok(selectedReconciliation.waitForTexts.includes("Statement runs"));
  }
});

test("scenario selection retains selectors and stale-state checks from the selected capture", () => {
  const capture = {
    name: "accounting-inbox",
    path: "/accounting/approvals/inbox",
    waitForText: "Approval Inbox",
    waitForTexts: ["Reviewer checklist"],
    waitForSelectors: ["[data-approval-ready]"],
    waitForAbsentTexts: ["Loading approvals"]
  };
  const selected = scenarioCapture(capture, source.module.getScenario("accounting.normal"), source.module);
  assert.deepEqual(selected.waitForSelectors, capture.waitForSelectors);
  assert.deepEqual(selected.waitForAbsentTexts, capture.waitForAbsentTexts);
});

function pageStub() {
  let handler;
  return {
    page: { async route(_pattern, callback) { handler = callback; } },
    async request(url, method = "GET") {
      let fulfilled;
      let continued = false;
      await handler({
        request: () => ({ url: () => url, method: () => method }),
        async fulfill(response) { fulfilled = response; },
        async continue() { continued = true; }
      });
      return { fulfilled, continued };
    }
  };
}

test("the Playwright adapter preserves error status and signals strict failure outside the UI", async () => {
  const stub = pageStub();
  const errors = [];
  const resolver = createScreenshotApiResolver(source.module, {}, { scenarioId: "accounting.forbidden", strict: true });
  await setupScenarioApiMocking(stub.page, resolver, (message) => errors.push(message));
  const forbidden = await stub.request("http://localhost/api/workstation/accounting");
  assert.equal(forbidden.fulfilled.status, 403);
  assert.equal(JSON.parse(forbidden.fulfilled.body).detail, "Accounting scenario access is forbidden.");
  const unexpected = await stub.request("http://localhost/api/screenshot-unknown");
  assert.equal(unexpected.fulfilled.status, 500);
  assert.match(errors[0], /Unexpected API request/);
  assert.throws(() => resolver.assertNoUnexpectedRequests(), /screenshot-unknown/);
  const moduleImport = await stub.request("http://localhost/workstation/src/lib/api/example.ts");
  assert.equal(moduleImport.continued, true);
});

test("the Playwright adapter waits for the declared response delay", async () => {
  const stub = pageStub();
  await setupScenarioApiMocking(stub.page, {
    scenario: { id: "delay-test" },
    resolve: () => ({ status: 200, body: { ready: true }, delayMs: 40 })
  }, (message) => assert.fail(message));
  const started = Date.now();
  const result = await stub.request("http://localhost/api/delayed");
  assert.ok(Date.now() - started >= 35);
  assert.equal(result.fulfilled.status, 200);
});
