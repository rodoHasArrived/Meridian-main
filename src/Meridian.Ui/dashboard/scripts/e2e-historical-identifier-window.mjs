#!/usr/bin/env node
/**
 * End-to-end check of the browser proposal flow for a governed historical identifier window
 * (Security Master review F1): the real workstation app, served by Vite, driven in Chromium through
 * the security detail screen's Passport tab — propose → submit → approve → publish.
 *
 * Only the HTTP edge is mocked. The security record, its trust-snapshot version, the asset-class
 * schema and the four governed workbench writes are fulfilled here, and every write body is captured
 * and asserted; all other requests fall through to the dev server and its development fixtures.
 * The server-side rules (independent reviewer, approved-only publication, ValidFrom preservation)
 * are covered by HistoricalIdentifierWindowGovernanceTests; this script proves the browser sends
 * the right requests in the right order and reflects each lifecycle state.
 *
 * Usage:
 *   node scripts/e2e-historical-identifier-window.mjs [--url <workstation url>] [--port <port>]
 *        [--timeout-ms <ms>] [--screenshot-dir <dir>]
 */

import { spawn } from "node:child_process";
import { createRequire } from "node:module";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";
import { chromiumLaunchOptions } from "./design-system-smoke-helpers.mjs";

const dashboardDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

const SECURITY_ID = "2a1d7c4e-5b8f-4c3a-9e61-0f7b3d2c1a90";
const REVISION_ID = "7b0c2d4e-1111-4a2b-9c3d-5e6f7a8b9c0d";
const WORKFLOW_ID = "0f9e8d7c-2222-4b3a-8c9d-1e2f3a4b5c6d";
const VERSION = 7;

function parseArgs(argv) {
  const values = new Map();
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (arg === "--help" || arg === "-h") {
      values.set("help", "true");
      continue;
    }
    if (!arg.startsWith("--")) throw new Error(`Unexpected argument: ${arg}`);
    const [name, inline] = arg.slice(2).split("=", 2);
    values.set(name, inline ?? argv[++index]);
  }
  return values;
}

function npmCommand() {
  return process.platform === "win32" ? "npm.cmd" : "npm";
}

async function waitForServer(url, timeoutMs) {
  const started = Date.now();
  let lastError = "";
  while (Date.now() - started < timeoutMs) {
    try {
      const response = await fetch(url, { redirect: "manual" });
      if (response.status >= 200 && response.status < 500) return;
      lastError = `HTTP ${response.status}`;
    } catch (error) {
      lastError = error instanceof Error ? error.message : String(error);
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error(`Timed out waiting for ${url}: ${lastError}`);
}

function startViteServer(port, logs) {
  const child = spawn(npmCommand(), ["run", "dev", "--", "--host", "127.0.0.1", "--port", String(port), "--strictPort"], {
    cwd: dashboardDir,
    env: { ...process.env, BROWSER: "none" },
    stdio: ["ignore", "pipe", "pipe"],
    detached: process.platform !== "win32"
  });
  child.stdout.on("data", (chunk) => logs.push(chunk.toString()));
  child.stderr.on("data", (chunk) => logs.push(chunk.toString()));
  return child;
}

async function stopProcess(child) {
  if (!child || child.exitCode !== null) return;
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
  if (child.exitCode === null) child.kill("SIGKILL");
}

function editResult(state, newVersion) {
  return {
    securityId: SECURITY_ID,
    revisionId: REVISION_ID,
    newVersion,
    state,
    changeEntry: {
      changeId: `${newVersion}:${state}`,
      streamVersion: newVersion,
      eventType: "historical-identifier-window-proposed",
      changedAtUtc: "2026-11-25T00:00:00Z",
      effectiveAtUtc: "2012-05-18T00:00:00Z",
      actor: "session.actor",
      origin: "User",
      sourceSystem: "operator-workbench",
      sourceRecordId: null,
      reason: null,
      summary: "Proposed historical Ticker window.",
      changedFields: ["identifiers.historicalWindow"],
      changedFieldsSummary: "identifiers.historicalWindow"
    }
  };
}

/**
 * The mocked backend: a small state machine over the workbench writes, so each step is accepted
 * only in the order the governed lifecycle allows, plus the reads the Passport tab needs.
 */
function createBackend({ refuseProposal = false } = {}) {
  const writes = [];
  let state = null;

  const json = (status, body) => ({ status, body });
  const workbench = (step) => `/api/security-master/${SECURITY_ID}/workbench/${step}`;

  function handle(method, pathname, body) {
    if (method === "GET" && pathname === `/api/workstation/security-master/securities/${SECURITY_ID}`) {
      return json(200, {
        securityId: SECURITY_ID,
        displayName: "Meta Platforms, Inc.",
        status: "Active",
        classification: {
          assetClass: "Equity",
          subType: "Common Stock",
          primaryIdentifierKind: "Ticker",
          primaryIdentifierValue: "META",
          matchedIdentifierKind: "Ticker",
          matchedIdentifierValue: "META",
          matchedProvider: null
        },
        economicDefinition: { currency: "USD", version: VERSION, effectiveFrom: "2026-11-24", effectiveTo: null, subType: "Common Stock", assetFamily: "Equities", issuerType: "Corporate" }
      });
    }
    if (method === "GET" && pathname === `/api/workstation/security-master/securities/${SECURITY_ID}/trust-snapshot`) {
      return json(200, { securityId: SECURITY_ID, economicDefinition: { version: VERSION } });
    }
    if (method === "GET" && pathname.startsWith("/api/security-master/asset-classes/")) {
      // The term picker is optional; without a schema the editor keeps the free-text path.
      return json(404, { title: "Not found" });
    }

    if (method === "POST" && pathname.startsWith(`/api/security-master/${SECURITY_ID}/workbench/`)) {
      writes.push({ step: pathname.split("/").pop(), body });
      if (pathname === workbench("historical-identifier-window")) {
        if (refuseProposal) {
          return json(422, { error: "unprocessable", message: "Historical identifier window refused: duplicate active identifier." });
        }
        assert.equal(state, null, "a proposal is only staged when no revision is in progress");
        state = "Draft";
        return json(200, editResult("Draft", VERSION));
      }
      if (pathname === workbench("submit")) {
        assert.equal(state, "Draft", "only a draft can be submitted");
        state = "Submitted";
        return json(200, editResult("Submitted", 2));
      }
      if (pathname === workbench("approve")) {
        assert.equal(state, "Submitted", "only a submitted revision can be approved");
        state = "Approved";
        return json(200, editResult("Approved", 3));
      }
      if (pathname === workbench("publish")) {
        assert.equal(state, "Approved", "only an approved revision can be published");
        state = "Published";
        return json(200, {
          securityId: SECURITY_ID,
          revisionId: REVISION_ID,
          newVersion: VERSION + 1,
          restatementRequired: false,
          restatementCandidates: [],
          invalidatedProjections: ["ApprovedHistoricalIdentifierWindowHandler"],
          canonicalMerge: { outcome: "Merged", detail: "The approved historical identifier window was applied.", canonicalVersion: VERSION + 1 }
        });
      }
      return json(404, { title: "Unexpected workbench route" });
    }

    return null;
  }

  return { handle, writes };
}

async function openPassport(page, baseUrl, timeoutMs) {
  await page.goto(`${baseUrl}/accounting/security-master/detail?securityId=${SECURITY_ID}`, { waitUntil: "domcontentloaded", timeout: timeoutMs });
  await page.getByRole("tab", { name: "Passport" }).click({ timeout: timeoutMs });
  await page.getByTestId("security-passport-editor").waitFor({ timeout: timeoutMs });
  await page.getByRole("button", { name: /propose historical identifier/i }).click();
}

async function fillWindow(page) {
  await page.getByLabel("Identifier kind").selectOption("Ticker");
  await page.getByLabel("Identifier value").fill("FB");
  await page.getByLabel("Valid from").fill("2012-05-18");
  await page.getByLabel("Valid to").fill("2022-06-09");
  await page.getByLabel("Evidence Vault reference").fill("evidence-vault://security-master/meta-ticker-change");
}

async function installBackend(page, backend, failures) {
  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (!url.pathname.startsWith("/api/")) return route.continue();
    let body = null;
    try {
      body = request.postData() ? JSON.parse(request.postData()) : null;
      const response = backend.handle(request.method(), url.pathname, body);
      if (!response) return route.continue();
      return route.fulfill({ status: response.status, contentType: "application/json", body: JSON.stringify(response.body) });
    } catch (error) {
      failures.push(`${request.method()} ${url.pathname}: ${error.message}`);
      return route.fulfill({ status: 500, contentType: "application/json", body: JSON.stringify({ title: error.message }) });
    }
  });
}

async function runHappyPath(browser, baseUrl, timeoutMs, screenshotDir) {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1100 } });
  const failures = [];
  page.on("pageerror", (error) => failures.push(`page error: ${error.message}`));
  const backend = createBackend();
  await installBackend(page, backend, failures);

  await openPassport(page, baseUrl, timeoutMs);
  const stage = page.getByRole("button", { name: /stage window draft/i });
  await fillWindow(page);
  assert.equal(await stage.isDisabled(), true, "staging stays disabled until the justification is entered");
  await page.getByText("Explain why this identifier history is correct.").waitFor();
  await page.getByLabel("Window justification").fill("Listed as FB from the 2012 IPO until the 2022-06-09 ticker change.");
  assert.equal(await stage.isEnabled(), true, "a complete window can be staged");

  await stage.click();
  await page.getByText("Historical window staged").waitFor({ timeout: timeoutMs });
  const proposal = backend.writes.find((write) => write.step === "historical-identifier-window");
  assert.ok(proposal, "the proposal posts to the historical-identifier-window route");
  assert.deepEqual(proposal.body, {
    expectedVersion: VERSION,
    kind: "Ticker",
    value: "FB",
    provider: null,
    validFrom: "2012-05-18T00:00:00Z",
    validTo: "2022-06-09T00:00:00Z",
    evidenceReference: "evidence-vault://security-master/meta-ticker-change",
    justification: "Listed as FB from the 2012 IPO until the 2022-06-09 ticker change.",
    fundProfileId: null
  });
  assert.ok(!("actor" in proposal.body), "the proposer is server-derived, never sent by the browser");

  // The staged window is the editor's working revision, so a second proposal must wait.
  await page.getByRole("button", { name: /propose historical identifier/i }).click();
  await page.getByText("Publish or discard the current revision before proposing a historical window.").waitFor();
  await page.getByRole("button", { name: "Cancel" }).click();
  if (screenshotDir) await page.screenshot({ path: path.join(screenshotDir, "historical-window-staged.png"), fullPage: true });

  // Maker → checker: submit names an independent reviewer; approval and publication follow.
  await page.getByLabel("Approval workflow id").fill(WORKFLOW_ID);
  await page.getByLabel("Expected workflow version").fill("1");
  await page.getByLabel("Independent reviewer").fill("ops.reviewer");
  await page.getByLabel("Report pack id").fill("rp-security-master");
  await page.getByRole("button", { name: "Submit for approval", exact: true }).click();
  await page.getByText("Submitted", { exact: true }).waitFor({ timeout: timeoutMs });
  await page.getByRole("button", { name: "Approve", exact: true }).click();
  await page.getByText("Approved", { exact: true }).waitFor({ timeout: timeoutMs });
  await page.getByRole("button", { name: "Publish", exact: true }).click();
  await page.getByText("Published", { exact: true }).waitFor({ timeout: timeoutMs });

  assert.deepEqual(backend.writes.map((write) => write.step), ["historical-identifier-window", "submit", "approve", "publish"]);
  const [, submit, approve, publish] = backend.writes;
  assert.equal(submit.body.revisionId, REVISION_ID, "submit governs the staged window revision");
  assert.equal(submit.body.workflowId, WORKFLOW_ID);
  assert.equal(submit.body.reviewer, "ops.reviewer");
  assert.equal(approve.body.revisionId, REVISION_ID);
  assert.equal(publish.body.revisionId, REVISION_ID);
  for (const write of backend.writes) {
    assert.ok(!("actor" in write.body), `${write.step} never sends a client-chosen actor`);
  }
  if (screenshotDir) await page.screenshot({ path: path.join(screenshotDir, "historical-window-published.png"), fullPage: true });

  assert.deepEqual(failures, [], "no page errors or backend assertion failures");
  await page.close();
}

async function runRefusedProposal(browser, baseUrl, timeoutMs) {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1100 } });
  const failures = [];
  page.on("pageerror", (error) => failures.push(`page error: ${error.message}`));
  const backend = createBackend({ refuseProposal: true });
  await installBackend(page, backend, failures);

  await openPassport(page, baseUrl, timeoutMs);
  await fillWindow(page);
  await page.getByLabel("Window justification").fill("Because.");
  await page.getByRole("button", { name: /stage window draft/i }).click();

  await page.getByText("Historical identifier window refused: duplicate active identifier.").waitFor({ timeout: timeoutMs });
  assert.equal(await page.getByLabel("Identifier value").inputValue(), "FB", "a refused proposal keeps the operator's input");
  // A disabled lifecycle button's accessible name carries its reason, so match its visible text.
  assert.equal(await page.locator("button", { hasText: "Submit for approval" }).isDisabled(), true, "nothing was staged");
  assert.deepEqual(backend.writes.map((write) => write.step), ["historical-identifier-window"]);
  assert.deepEqual(failures, []);
  await page.close();
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.has("help")) {
    console.log("Usage: node scripts/e2e-historical-identifier-window.mjs [--url <url>] [--port <port>] [--timeout-ms <ms>] [--screenshot-dir <dir>]");
    return;
  }

  const timeoutMs = Number(args.get("timeout-ms") ?? 30000);
  const port = Number(args.get("port") ?? 5187);
  const screenshotDir = args.get("screenshot-dir") ?? null;
  if (screenshotDir) await fs.mkdir(screenshotDir, { recursive: true });

  const logs = [];
  let server = null;
  let baseUrl = args.get("url")?.replace(/\/+$/, "");
  if (!baseUrl) {
    server = startViteServer(port, logs);
    baseUrl = `http://127.0.0.1:${port}/workstation`;
  }

  const dashboardRequire = createRequire(path.join(dashboardDir, "package.json"));
  const { chromium } = dashboardRequire("playwright");
  let browser = null;
  try {
    await waitForServer(`${baseUrl}/`, timeoutMs);
    browser = await chromium.launch(chromiumLaunchOptions());
    await runHappyPath(browser, baseUrl, timeoutMs, screenshotDir);
    console.log("PASS historical identifier window: propose → submit → approve → publish");
    await runRefusedProposal(browser, baseUrl, timeoutMs);
    console.log("PASS historical identifier window: refused proposal keeps input and stages nothing");
  } catch (error) {
    if (logs.length > 0) console.error(logs.join("").split("\n").slice(-30).join("\n"));
    throw error;
  } finally {
    await browser?.close();
    await stopProcess(server);
  }
}

main().catch((error) => {
  console.error(`FAIL ${error.stack ?? error}`);
  process.exitCode = 1;
});
