import assert from "node:assert/strict";
import test from "node:test";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";
import {
  assertHealthyScene, chromiumLaunchOptions, compositeColor, contrastRatio, designSystemMatrix,
  parseCssColor, parseDesignSystemOptions, readOnlySmokeResponse, supportingSmokeResponse, workstationBase
} from "./design-system-smoke-helpers.mjs";
import { managedSmokeOptions } from "./design-system-smoke-runner.mjs";

const dashboardDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const options = (values = [], flags = ["design-system"]) =>
  managedSmokeOptions({ values: new Map(values), flags: new Set(flags), repoRoot: "/repo with spaces" });

test("matrix covers three actual routes at all desktop sizes and appearance configurations", () => {
  const matrix = designSystemMatrix();
  assert.equal(matrix.length, 36);
  assert.equal(new Set(matrix.map(scene => scene.name)).size, 36);
  for (const width of [1920, 1440, 1366]) {
    assert.equal(matrix.filter(scene => scene.width === width).length, 12);
  }
  for (const name of ["light", "manual-dark", "system-dark", "forced-light"]) {
    assert.equal(matrix.filter(scene => scene.appearance.name === name).length, 9);
  }
});

test("URLs must target the HTTP workstation without credentials or query state", () => {
  assert.equal(workstationBase("https://example.test/workstation/"), "https://example.test/workstation");
  for (const value of ["file:///workstation", "javascript:alert(1)", "https://user:secret@example.test/workstation", "https://example.test/", "https://example.test/workstation?token=x"]) {
    assert.throws(() => workstationBase(value));
  }
});

test("expanded profile requires explicit backend and production origins", () => {
  assert.throws(() => parseDesignSystemOptions(["--expanded"]), /backend-url/);
  const parsed = parseDesignSystemOptions(["--expanded", "--backend-url", "http://localhost:9001/workstation", "--production-url=http://localhost:9002/workstation", "--output", "/tmp/evidence with spaces"]);
  assert.equal(parsed.expanded, true);
  assert(parsed.output.endsWith("evidence with spaces"));
  assert.throws(() => parseDesignSystemOptions(["--url"]), /Missing value/);
  assert.throws(() => parseDesignSystemOptions(["--unknown", "value"]), /Invalid option/);
  assert.throws(() => parseDesignSystemOptions(["--timeout-ms", "NaN"]), /timeout/);
});

test("managed defaults are bounded and evidence paths preserve spaces", () => {
  const parsed = options([["artifacts-dir", "evidence with spaces"], ["port", "5190"]]);
  assert.equal(parsed.port, 5190);
  assert(parsed.backendBase.includes(":5191/"));
  assert(parsed.productionBase.includes(":5192/"));
  assert(parsed.output.endsWith(path.join("repo with spaces", "evidence with spaces")));
  assert.equal(parsed.startFixture, true);
  assert.equal(parsed.startBackend, true);
});

test("managed CLI rejects unsafe ports, duplicate targets and missing existing-server URL", () => {
  for (const port of ["0", "65534", "oops", "5.2"]) assert.throws(() => options([["port", port]]), /port/);
  assert.throws(() => options([], ["design-system", "skip-server"]), /requires --url/);
  assert.throws(() => options([["wat", "value"]]), /Unsupported/);
  assert.throws(() => options([["url", "http://127.0.0.1:5174/workstation"]]), /distinct/);
  const parsed = options([["url", "http://127.0.0.1:9000/workstation"]], ["design-system", "skip-server"]);
  assert.equal(parsed.startFixture, false);
});

test("supporting fixtures are exact-method/path matches rather than catch-all writes", () => {
  assert.deepEqual(supportingSmokeResponse("/api/risk/escalations", "GET").body, []);
  assert.equal(supportingSmokeResponse("/api/risk/escalations", "POST"), null);
  assert.equal(supportingSmokeResponse("/api/ledger/post", "POST"), null);
  assert.equal(supportingSmokeResponse("/api/risk/escalations/unknown", "GET"), null);
  assert.equal(supportingSmokeResponse("/api/workstation/operator/visits/reconciliation-breaks", "POST").body.surface, "reconciliation-breaks");
  assert.equal(supportingSmokeResponse("/api/workstation/operator/visits/unknown", "POST"), null);
});

test("new smoke-only support stays scoped to five GET identities without inventing history", () => {
  const paths = [
    "/api/workstation/tables/close-checklist-progress-data/views",
    "/api/workstation/tables/portfolio-positions/views",
    "/api/workstation/tables/provider-evidence-age-data/views",
    "/api/workstation/operations/continuity/79f1f386-0bb1-4aef-9a85-fb9d6de8e1f6/checklist/history",
    "/api/workstation/provider-verification-history/provider-alpaca-paper"
  ];
  for (const path of paths) {
    const response = readOnlySmokeResponse(path, "GET");
    assert.equal(response.status, 200);
    assert.equal(response.smokeFixture.path, path);
    assert.equal(response.headers["x-meridian-dev-fixture"], "true");
    assert.equal(readOnlySmokeResponse(path, "POST"), null);
    assert.equal(readOnlySmokeResponse(path + "/unknown", "GET"), null);
  }
  for (const path of paths.slice(0, 3)) assert.deepEqual(readOnlySmokeResponse(path).body, []);
  const checklist = readOnlySmokeResponse(paths[3]).body;
  assert.equal(checklist.status, "Unavailable");
  assert.equal(checklist.workflowId, "79f1f386-0bb1-4aef-9a85-fb9d6de8e1f6");
  assert.deepEqual(checklist.observations, []);
  assert.equal(checklist.coverageStart, null);
  assert.equal(checklist.coverageEnd, null);
  const verification = readOnlySmokeResponse(paths[4]).body;
  assert.equal(verification.connectionId, "provider-alpaca-paper");
  assert.deepEqual(verification.observations, []);
  assert.equal(readOnlySmokeResponse(paths[4].replace("provider-alpaca-paper", "other-connection")), null);
  assert.equal(readOnlySmokeResponse(paths[3].replace("79f1f386-0bb1-4aef-9a85-fb9d6de8e1f6", "other-workflow")), null);
  assert.equal(readOnlySmokeResponse("/api/workstation/tables/other-table/views"), null);
});

test("contrast math covers alpha washes and exposes the original selected-row failure", () => {
  assert.equal(contrastRatio(parseCssColor("#FFFFFF"), parseCssColor("#000000")), 21);
  assert.deepEqual(parseCssColor("color(srgb 1 0.5 0 / 0.25)"), [255, 127.5, 0, 0.25]);
  assert.deepEqual(compositeColor(parseCssColor("rgba(0, 0, 0, 0.5)"), parseCssColor("#FFFFFF")), [127.5, 127.5, 127.5, 1]);
  assert(parseCssColor("oklab(1 0 0)").slice(0, 3).every(channel => Math.abs(channel - 255) < 0.001));
  const translucent = parseCssColor("oklab(0.931382 0.000479549 0.00992298 / 0.15)");
  assert.equal(translucent[3], 0.15);
  assert(translucent[0] > 230 && translucent[2] < 233);
  assert(contrastRatio(parseCssColor("#929CA6"), parseCssColor("#2C4054")) < 4.5);
  assert(contrastRatio(parseCssColor("#BEC4CA"), parseCssColor("#2C4054")) > 4.5);
  assert.throws(() => parseCssColor("unresolved-var(--missing)"), /Unsupported/);
});

test("health failures identify the actual runtime/network problem", () => {
  assert.doesNotThrow(() => assertHealthyScene({ exceptions: [], errors: [], assetFailures: [], unexpectedRequests: [] }));
  for (const key of ["exceptions", "errors", "assetFailures", "unexpectedRequests"]) {
    assert.throws(() => assertHealthyScene({ [key]: ["specific defect"] }), new RegExp(key + ": specific defect"));
  }
});

test("browser selection is portable and explicit override wins", () => {
  assert.deepEqual(chromiumLaunchOptions({}), { args: ["--no-sandbox"] });
  assert.equal(chromiumLaunchOptions({ CHROMIUM_EXECUTABLE_PATH: "C:/Program Files/Chrome/chrome.exe" }).executablePath, "C:/Program Files/Chrome/chrome.exe");
  assert.equal(chromiumLaunchOptions({ MERIDIAN_CHROMIUM_PATH: "/preferred", CHROMIUM_EXECUTABLE_PATH: "/other" }).executablePath, "/preferred");
});

test("CLI help and invalid managed requests finish before launching browsers", () => {
  const script = path.join(dashboardDir, "scripts/smoke-workstation.mjs");
  const help = spawnSync(process.execPath, [script, "--help"], { cwd: dashboardDir, encoding: "utf8" });
  assert.equal(help.status, 0);
  assert.match(help.stdout, /--design-system/);
  const invalid = spawnSync(process.execPath, [script, "--design-system", "--skip-server"], { cwd: dashboardDir, encoding: "utf8" });
  assert.equal(invalid.status, 1);
  assert.match(invalid.stderr, /--skip-server requires --url/);
});
