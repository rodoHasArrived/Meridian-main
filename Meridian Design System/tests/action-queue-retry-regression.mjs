#!/usr/bin/env node
// Synthetic source-level regression: queue retries keep the selected instruction's identity.
import assert from "node:assert/strict";
import { build } from "esbuild";
import { chromium } from "playwright";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const built = await build({
  absWorkingDir: root,
  bundle: true,
  write: false,
  format: "iife",
  platform: "browser",
  define: { "process.env.NODE_ENV": '"production"' },
  stdin: {
    resolveDir: root,
    loader: "jsx",
    contents: `import React from "react";
      import { createRoot } from "react-dom/client";
      import { ActionEnvelopeProvider } from "./components/operations/ActionEnvelope.jsx";
      import { ActionQueue } from "./components/operations/ActionQueue.jsx";
      window.sent = [];
      window.replyMode = "unknown";
      const transport = instruction => {
        window.sent.push(instruction);
        if (window.replyMode === "defer") return new Promise(resolve => { window.answer = resolve; });
        return window.replyMode === "landed" ? { ok: true, id: "SYNTHETIC-RECEIPT" } : {};
      };
      createRoot(document.getElementById("fixture")).render(
        <ActionEnvelopeProvider actions={{ post: { endpoint: "POST /synthetic/default", payload: { amount: 0 } } }} transport={transport}>
          {api => { window.action = api; return <ActionQueue docked="inline" />; }}
        </ActionEnvelopeProvider>
      );`,
  },
});
const executablePath = process.env.MERIDIAN_CHROMIUM_PATH || process.env.CHROMIUM_EXECUTABLE_PATH;
const browser = await chromium.launch({ ...(executablePath ? { executablePath } : {}), args: ["--no-sandbox"] });
const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
const errors = [];
page.on("pageerror", error => errors.push(error.message));

try {
  await page.setContent('<!doctype html><html lang="en"><title>Selected instruction retry</title><div id="fixture"></div></html>');
  await page.addScriptTag({ content: built.outputFiles[0].text });
  await page.waitForFunction(() => !!window.action);
  const older = await page.evaluate(() => window.action.submit("post", {
    label: "Older instruction", idempotencyKey: "older", endpoint: "POST /synthetic/older", payload: { amount: 10 },
  }));
  await page.waitForFunction(id => window.action.instructions.find(i => i.id === id)?.state === "unlanded", older.id);
  const newer = await page.evaluate(() => window.action.submit("post", {
    label: "Newer instruction", idempotencyKey: "newer", endpoint: "POST /synthetic/newer", payload: { amount: 20 },
  }));
  await page.waitForFunction(id => window.action.instructions.find(i => i.id === id)?.state === "unlanded", newer.id);

  // The queue must pass the older row id, rather than retrying the newest matching action.
  const olderRow = page.locator(".mds-aq__row").filter({ hasText: "Older instruction" });
  await olderRow.getByRole("button", { name: "Retry · same key" }).focus();
  await page.keyboard.press("Enter");
  await page.waitForFunction(() => window.sent.length === 3);
  assert.deepEqual(await page.evaluate(() => window.sent.map(i => i.idempotencyKey)), ["older", "newer", "older"]);
  assert.deepEqual(await page.evaluate(() => window.sent[2]), {
    action: "post", endpoint: "POST /synthetic/older", idempotencyKey: "older", attempt: 2,
    posture: "confirmed", permission: undefined, payload: { amount: 10 },
  });
  assert.equal(await page.evaluate(id => window.action.instructions.find(i => i.id === id).receipt.attempt, newer.id), 1);

  // Existing action-only consumers still select the latest unresolved instruction.
  const compatible = await page.evaluate(() => window.action.retry("post"));
  assert.equal(compatible.id, newer.id);
  assert.equal(await page.evaluate(() => window.sent.at(-1).idempotencyKey), "newer");
  assert.equal(await page.evaluate(() => window.sent.at(-1).attempt), 2);
  await page.waitForFunction(id => window.action.instructions.find(i => i.id === id)?.state === "unlanded", newer.id);

  const count = await page.evaluate(() => window.sent.length);
  assert.equal(await page.evaluate(() => window.action.retry("post", "missing")), null);
  assert.equal(await page.evaluate(id => window.action.retry("different-action", id), older.id), null);
  assert.equal(await page.evaluate(() => window.sent.length), count);

  // Two immediate retries share an attempt, and a rendered in-flight row cannot retry.
  await page.evaluate(id => {
    window.replyMode = "defer";
    window.retryA = window.action.retry("post", id);
    window.retryB = window.action.retry("post", id);
  }, older.id);
  await page.waitForFunction(id => window.action.instructions.find(i => i.id === id)?.state === "inflight", older.id);
  assert.equal(await page.evaluate(() => window.sent.length), count + 1);
  assert.equal(await page.evaluate(id => window.action.retry("post", id), older.id), null);
  assert.equal(await olderRow.getByRole("button", { name: "Retry · same key" }).count(), 0);
  assert.deepEqual(await page.evaluate(() => window.sent.at(-1)), {
    action: "post", endpoint: "POST /synthetic/older", idempotencyKey: "older", attempt: 3,
    posture: "confirmed", permission: undefined, payload: { amount: 10 },
  });
  await page.evaluate(() => window.answer({ ok: true, id: "OLDER-RECEIPT" }));
  assert.equal((await page.evaluate(() => window.retryA)).id, older.id);
  assert.equal((await page.evaluate(() => window.retryB)).deduped, true);
  await page.waitForFunction(id => window.action.instructions.find(i => i.id === id)?.state === "landed", older.id);
  assert.equal(await page.evaluate(id => window.action.retry("post", id), older.id), null);
  assert.equal(await page.evaluate(() => window.sent.length), count + 1);

  // A landed target never redirects the request to another unresolved instruction.
  assert.equal(await page.evaluate(() => window.action.instructions.filter(i => i.state === "unlanded").length), 1);
  assert.deepEqual(errors, []);
  console.log("Selected instruction retry: passed (older row, exact key/payload/attempt, compatibility, unknown id, in-flight/landed guards, concurrent dedupe).");
} finally {
  await browser.close();
}
