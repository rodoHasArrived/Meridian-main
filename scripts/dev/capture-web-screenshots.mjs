#!/usr/bin/env node

import { spawn } from "node:child_process";
import { createRequire } from "node:module";
import fs from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { pathToFileURL } from "node:url";
import {
  createScreenshotApiResolver,
  loadScreenshotScenarioModule,
  scenarioCapture,
  setupScenarioApiMocking
} from "./web-screenshot-scenarios.mjs";

const repoMarkers = ["Meridian.sln", ".git"];

function parseArgs(argv) {
  const values = new Map();
  const valueLists = new Map();
  const flags = new Set();
  const recordValue = (name, value) => {
    values.set(name, value);
    valueLists.set(name, [...(valueLists.get(name) ?? []), value]);
  };

  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (!arg.startsWith("--")) {
      throw new Error(`Unexpected argument: ${arg}`);
    }

    const eqIndex = arg.indexOf("=");
    if (eqIndex > 0) {
      recordValue(arg.slice(2, eqIndex), arg.slice(eqIndex + 1));
      continue;
    }

    const name = arg.slice(2);
    const next = argv[index + 1];
    if (next && !next.startsWith("--")) {
      recordValue(name, next);
      index += 1;
    } else {
      flags.add(name);
    }
  }

  return { values, valueLists, flags };
}

export function parseCaptureWorkers(value = 1) {
  const workers = Number(value);
  if (!Number.isInteger(workers) || workers < 1 || workers > 8) {
    throw new Error("--workers must be an integer between 1 and 8.");
  }
  return workers;
}

/** Run isolated captures with bounded concurrency and one manifest writer. */
export async function runCaptureWorkers(captures, workers, capture, checkpoint) {
  const workerCount = Math.min(parseCaptureWorkers(workers), captures.length);
  const completed = new Array(captures.length);
  let nextIndex = 0;
  let fatalError = null;
  let checkpointTail = Promise.resolve();
  const shellMountFailureLimit = 3;
  const isShellMountFailure = (result) => result?.status === "failed"
    && /App shell failed to mount/.test(String(result.error));

  async function worker() {
    while (!fatalError && nextIndex < captures.length) {
      const index = nextIndex++;
      try {
        completed[index] = await capture(captures[index], index);
      } catch (error) {
        fatalError ??= error;
        return;
      }

      // Captures may finish out of order. Snapshot in catalog order and serialize
      // writes so the manifest's shared temporary file never has competing owners.
      checkpointTail = checkpointTail.then(async () => {
        const results = completed.filter(Boolean);
        await checkpoint(results);
        if (
          completed.slice(0, shellMountFailureLimit).length === shellMountFailureLimit
          && Array.from({ length: shellMountFailureLimit }, (_, index) => completed[index]).every(isShellMountFailure)
          && results.every(isShellMountFailure)
        ) {
          fatalError ??= new Error(
            `Aborting capture run: the app shell failed to mount on the first ${results.length} route(s). `
            + "This is a shell-level failure (activation gate, missing shell fixtures, or a dev server "
            + "compile error), so every remaining route would fail the same way. See the manifest logs."
          );
        }
      }).catch((error) => { fatalError ??= error; });
      await checkpointTail;
    }
  }

  // Drain every started capture before returning or throwing. Browser teardown
  // must not race a worker that is still closing its context or writing evidence.
  await Promise.all(Array.from({ length: workerCount }, () => worker()));
  await checkpointTail;
  if (fatalError) throw fatalError;
  return completed.filter(Boolean);
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

function collectCaptureSelectors(valueLists) {
  const selectors = [
    ...(valueLists.get("capture") ?? []),
    ...(valueLists.get("captures") ?? []),
    ...(valueLists.get("capture-id") ?? []),
    ...(valueLists.get("capture-name") ?? [])
  ];

  return selectors
    .flatMap((value) => String(value).split(","))
    .map((value) => value.trim().toLowerCase())
    .filter(Boolean);
}

function captureMatchesSelector(capture, selector) {
  const candidates = [
    capture.id,
    capture.name,
    capture.path,
    screenshotCoveragePath(capture.path ?? ""),
    capture.docLabel
  ]
    .filter((value) => typeof value === "string")
    .map((value) => value.trim().toLowerCase());

  return candidates.includes(selector);
}

function selectCaptures(captures, selectors) {
  if (selectors.length === 0) {
    return captures;
  }

  const selected = captures.filter((capture) =>
    selectors.some((selector) => captureMatchesSelector(capture, selector))
  );

  if (selected.length === 0) {
    throw new Error(`No web screenshot captures matched selector(s): ${selectors.join(", ")}`);
  }

  return selected;
}

function npmInvocation(args) {
  if (process.platform === "win32") {
    return {
      command: "cmd.exe",
      args: ["/d", "/s", "/c", "npm.cmd", ...args]
    };
  }

  return {
    command: "npm",
    args
  };
}

function appendLog(logs, prefix, chunk) {
  const text = chunk.toString();
  logs.push(...text.split(/\r?\n/).filter(Boolean).map((line) => `${prefix}${line}`));
}

export async function waitForServer(url, timeoutMs, child, logs) {
  const started = Date.now();
  let lastError = "";
  let serverExit = null;
  let serverError = null;

  // Fail fast if the dev server process dies before it ever answers (port
  // conflict, Vite config error, OOM) instead of polling for the full timeout
  // and then reporting a generic "timed out" message that hides the real cause.
  const noteExit = (code, signal) => {
    serverExit = `exit code ${code ?? "null"}${signal ? `, signal ${signal}` : ""}`;
  };
  const noteError = (error) => { serverError = error; };
  if (child) {
    if (child.exitCode !== null || child.signalCode !== null) {
      noteExit(child.exitCode, child.signalCode);
    } else {
      child.once("exit", noteExit);
    }
    child.once("error", noteError);
  }

  const describeServerExit = () => {
    const recentLogs = Array.isArray(logs) ? logs.slice(-20).join("\n") : "";
    const detail = recentLogs ? `\n${recentLogs}` : "";
    return `Dev server exited before becoming ready (${serverExit}).${detail}`;
  };

  try {
    while (Date.now() - started < timeoutMs) {
      if (serverError) {
        throw new Error(`Dev server could not start: ${serverError.message}`, { cause: serverError });
      }
      if (serverExit) {
        throw new Error(describeServerExit());
      }

      try {
        const remainingMs = Math.max(1, timeoutMs - (Date.now() - started));
        const response = await fetch(url, {
          redirect: "manual",
          signal: AbortSignal.timeout(Math.min(5000, remainingMs))
        });
        // Release the response stream even when it is an HTTP failure. Otherwise
        // repeated startup probes can retain connections until the overall timeout.
        await response.body?.cancel();
        if (response.status >= 200 && response.status < 400) {
          return;
        }

        lastError = `HTTP ${response.status}`;
      } catch (error) {
        lastError = error instanceof Error ? error.message : String(error);
      }

      await new Promise((resolve) => setTimeout(resolve, Math.max(0, Math.min(500, timeoutMs - (Date.now() - started)))));
    }

    if (serverError) {
      throw new Error(`Dev server could not start: ${serverError.message}`, { cause: serverError });
    }
    if (serverExit) {
      throw new Error(describeServerExit());
    }
    throw new Error(`Timed out waiting for ${url}: ${lastError}`);
  } finally {
    child?.off("exit", noteExit);
    child?.off("error", noteError);
  }
}

function startViteServer(dashboardDir, host, port, logs) {
  const invocation = npmInvocation(["run", "dev", "--", "--host", host, "--port", String(port), "--strictPort"]);
  const child = spawn(
    invocation.command,
    invocation.args,
    {
      cwd: dashboardDir,
      env: {
        ...process.env,
        BROWSER: "none",
        MERIDIAN_SCREENSHOT_CAPTURE: "true",
        MERIDIAN_API_BASE_URL: process.env.MERIDIAN_API_BASE_URL ?? "http://127.0.0.1:8080"
      },
      stdio: ["ignore", "pipe", "pipe"],
      detached: process.platform !== "win32"
    }
  );

  child.stdout.on("data", (chunk) => appendLog(logs, "", chunk));
  child.stderr.on("data", (chunk) => appendLog(logs, "stderr: ", chunk));

  return child;
}

async function stopProcess(child) {
  if (!child || child.exitCode !== null) {
    return;
  }

  const waitForExit = (timeoutMs) =>
    new Promise((resolve) => {
      const timer = setTimeout(() => resolve(false), timeoutMs);
      child.once("exit", () => {
        clearTimeout(timer);
        resolve(true);
      });
    });

  const killUnixGroup = (signal) => {
    try {
      process.kill(-child.pid, signal);
      return true;
    } catch {
      return false;
    }
  };

  if (process.platform === "win32") {
    const taskkill = spawn("taskkill", ["/pid", String(child.pid), "/t", "/f"], { stdio: "ignore" });
    const taskkillExited = await new Promise((resolve) => {
      const timer = setTimeout(() => resolve(false), 5000);
      taskkill.once("exit", () => {
        clearTimeout(timer);
        resolve(true);
      });
    });
    if (!taskkillExited) {
      taskkill.kill("SIGKILL");
    }
    await waitForExit(5000);
    return;
  }

  if (!killUnixGroup("SIGTERM")) {
    child.kill("SIGTERM");
  }

  const exited = await waitForExit(5000);
  if (exited) {
    return;
  }

  if (!killUnixGroup("SIGKILL")) {
    child.kill("SIGKILL");
  }
  await waitForExit(2000);
}

function collectRequiredFixtureRoutes(captures) {
  const required = new Set();
  for (const capture of captures) {
    const configured = Array.isArray(capture.requiredApiRoutes) ? capture.requiredApiRoutes : [];
    for (const route of configured) {
      if (typeof route === "string" && route.trim().length > 0) {
        required.add(route.trim());
      }
    }
  }
  return [...required];
}

function findMissingFixtureRoutes(requiredRoutes, fixtureRoutes, scenarioModule, scenario) {
  return requiredRoutes.filter(
    (requiredRoute) => !Object.hasOwn(fixtureRoutes, requiredRoute)
      && !scenarioModule.resolveScenarioResponse(scenario, requiredRoute)
  );
}

function extractWorkstationRouteCatalog(source, filePath) {
  const match = source.match(/export const WORKSTATION_ROUTE_CATALOG = \{(?<body>[\s\S]*?)\} as const;/);
  if (!match?.groups?.body) {
    throw new Error(`WORKSTATION_ROUTE_CATALOG block was not found in ${filePath}`);
  }

  const catalog = new Map();
  const routePattern = /\n\s*([A-Za-z0-9_]+):\s*"([^"]+)"/g;
  let routeMatch = routePattern.exec(match.groups.body);
  while (routeMatch) {
    catalog.set(routeMatch[1], routeMatch[2]);
    routeMatch = routePattern.exec(match.groups.body);
  }

  return catalog;
}

function extractExplicitAppRoutes(source) {
  const paths = new Set();
  const routePattern = /<Route\s+path="([^"]+)"/g;
  let routeMatch = routePattern.exec(source);
  while (routeMatch) {
    paths.add(routeMatch[1]);
    routeMatch = routePattern.exec(source);
  }

  return paths;
}

function collectExpectedCapturePaths(routeCatalog, appRoutes) {
  // Compatibility redirect routes forward to a canonical screen that already has
  // its own capture, so they do not need a dedicated screenshot entry. This list
  // must stay in sync with the compatibility routes excluded by the coverage
  // tests in tests/scripts/test_refresh_screenshots_workflow.py.
  const compatibilityRouteKeys = new Set([
    "accountingTrialBalanceLegacy",
    "dataAlertsLegacy",
    "dataSecurityMasterLegacy",
    "dataWatchlistLegacy",
    "settingsIntegrations",
    "settingsFeatureCoverage",
    "settingsAlpacaProviderSetup",
    "settingsBackendCapabilityCoverage",
    "settingsDiagnosticEndpoints",
    "strategyFormulaWorkbenchLegacy"
  ]);
  const compatibilityAppRoutes = new Set([
    "/accounting/trial-balance",
    "/data/alerts",
    "/data/security-master",
    "/data/security-master/*",
    "/data/watchlist",
    "/overview/*",
    "/research/*",
    "/data-operations/*",
    "/governance/*",
    "/strategy/formula-workbench"
  ]);
  const expected = new Set(["/"]);

  for (const [key, routePath] of routeCatalog.entries()) {
    if (!compatibilityRouteKeys.has(key)) {
      expected.add(routePath);
    }
  }

  for (const routePath of appRoutes) {
    if (!routePath.includes("*") && !compatibilityAppRoutes.has(routePath)) {
      expected.add(routePath);
    }
  }

  return expected;
}

function screenshotCoveragePath(routePath) {
  if (typeof routePath !== "string" || routePath.trim().length === 0) {
    return "";
  }

  try {
    const routeUrl = new URL(routePath.trim(), "http://meridian.local");
    return `${routeUrl.pathname || "/"}${routeUrl.hash}`;
  } catch {
    return "";
  }
}

function assertCaptureRouteStateIdentity(captures) {
  const capturesByPath = new Map();

  for (const capture of captures) {
    const routePath = typeof capture.path === "string" ? capture.path.trim() : "";
    if (routePath.length === 0) {
      continue;
    }

    const matches = capturesByPath.get(routePath) ?? [];
    matches.push(capture);
    capturesByPath.set(routePath, matches);
  }

  const ambiguousPaths = [];
  for (const [routePath, matches] of capturesByPath.entries()) {
    if (matches.length < 2) {
      continue;
    }

    const variants = matches.map((capture) =>
      typeof capture.variant === "string" ? capture.variant.trim() : ""
    );
    const hasExplicitUniqueVariants = variants.every((variant) => variant.length > 0)
      && new Set(variants).size === variants.length;
    if (!hasExplicitUniqueVariants) {
      ambiguousPaths.push(routePath);
    }
  }

  if (ambiguousPaths.length > 0) {
    throw new Error(
      "Web screenshot captures must use a unique route state or declare distinct non-empty "
      + `variant values. Ambiguous path(s): ${ambiguousPaths.sort().join(", ")}`
    );
  }
}

async function findMissingCaptureRoutePaths(captures, routeCatalogPath, appShellPath) {
  const capturedPaths = new Set(
    captures
      .map((capture) => screenshotCoveragePath(capture.path))
      .filter((routePath) => routePath.length > 0)
  );
  const routeCatalog = extractWorkstationRouteCatalog(
    await fs.readFile(routeCatalogPath, "utf8"),
    routeCatalogPath
  );
  const appRoutes = extractExplicitAppRoutes(await fs.readFile(appShellPath, "utf8"));
  const expectedPaths = new Set(
    [...collectExpectedCapturePaths(routeCatalog, appRoutes)]
      .map((routePath) => screenshotCoveragePath(routePath))
      .filter((routePath) => routePath.length > 0)
  );
  return [...expectedPaths]
    .filter((routePath) => !capturedPaths.has(routePath))
    .sort();
}

function collectCaptureWaitForTexts(capture) {
  const waitForTexts = [];
  if (typeof capture.waitForText === "string" && capture.waitForText.trim().length > 0) {
    waitForTexts.push(capture.waitForText.trim());
  }

  if (Array.isArray(capture.waitForTexts)) {
    for (const value of capture.waitForTexts) {
      if (typeof value === "string" && value.trim().length > 0) {
        waitForTexts.push(value.trim());
      }
    }
  }

  return [...new Set(waitForTexts)];
}

function collectCaptureWaitForAbsentTexts(capture) {
  if (!Array.isArray(capture.waitForAbsentTexts)) {
    return [];
  }

  return [...new Set(
    capture.waitForAbsentTexts
      .filter((value) => typeof value === "string" && value.trim().length > 0)
      .map((value) => value.trim())
  )];
}

const OPAQUE_IDENTIFIER_PATTERN = /\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b/gi;
// A count of exactly one directly followed by a plural noun that ends its phrase ("1 rows",
// "1 TEMPLATES", "1 jobs · 2 packages"). The lookbehinds keep "11 rows", "0.1 rows", "$1 fees",
// "2026-01-1 ..." and ratios ("1 of 1 breaks") out. The lookahead requires the plural to be the
// last word of its phrase: a plural-form modifier is correct English ("1 operations continuity
// workflow", "1 securities account"), and a verb after a singular subject reads on ("Slice 1 uses
// the provider"). The price is that "1 rows parsed" is not caught; precision matters more for a
// check that fails a capture.
const SINGULAR_COUNT_PLURAL_NOUN_PATTERN = /(?<![\w.,:/$%+#-])(?<!\bof )1 ([a-z]{3,}s)(?![\w-])(?! [a-z(])/gi;
// Words ending in "s" that are not plural nouns: third-person verbs that can end a sentence
// ("Step 1 fails.") and singular nouns. Words ending in "ss", "us" or "is" (business, status,
// basis) are skipped by rule.
const NOT_PLURAL_NOUNS = new Set([
  "always", "appears", "applies", "blocks", "contributes", "does", "exceeds", "exists", "fails",
  "has", "holds", "needs", "passes", "remains", "requires", "series", "shows", "uses", "was"
]);
// Request paths belong in a collapsed technical-details disclosure (ErrorNotice in the dashboard),
// never in the sentence an operator reads.
const API_PATH_PATTERN = /\/api\/[\w\-/{}?=&.%]*/g;
// Words from how the system is built, not how an operator works. "Lane" names an internal work
// stream, "payload" and "mutation" describe requests, "caller-visible" describes authorization.
const INTERNAL_TERM_PATTERN = /\b(lanes?|payloads?|mutations?|caller-visible)\b/gi;
// A lowercase hyphenated token. Most are ordinary compounds ("as-of", "read-only"), so
// isSlugIdentifier decides which ones are identifiers. The lookarounds leave out pieces of file
// names, addresses and paths ("notes-2026.pdf", "ops-1@example.com").
const HYPHENATED_TOKEN_PATTERN = /(?<![\w/.@#-])[a-z][a-z0-9]*(?:-[a-z0-9]+)+(?![\w-]|[.@/]\w)/g;
const SLUG_RECORD_PREFIX = /^(?:entity|fund|account|acct|investor|ledger|portfolio|strategy|template|schedule|recon)-/;
// Two-part compounds that legitimately end in a number ("tier-1 capital", "phase-2").
const NUMBERED_COMPOUND_HEADS = new Set([
  "class", "day", "form", "fy", "level", "mid", "phase", "post", "pre", "rule", "section", "series",
  "step", "tier", "top", "type", "year"
]);
// A "Latency" term followed by a clock time or "Never": latency is a duration. Line-anchored, so
// "Latency / Last Response", "P99 LATENCY" and "Latency statistics: …" pass.
const LATENCY_NOT_DURATION_PATTERN = /^latency\n(?:never|[a-z]{3} \d{1,2}, \d{1,2}:\d{2}(?: [ap]m)?(?: utc)?)$/gim;
// Placeholders standing in for optional context: an undeclared basis printed as a failure, or a
// missing harness fixture behind the saved-views notice.
const UNAVAILABLE_PLACEHOLDER_PATTERN = /\b(?:basis|saved views) unavailable\b/gi;
export const CAPTURE_CONTENT_ISSUE_KINDS = [
  "opaque-identifier",
  "singular-count-plural-noun",
  "api-path",
  "internal-term",
  "slug-identifier",
  "latency-not-duration",
  "unavailable-placeholder",
  "disabled-primary-without-reason",
  "status-contradiction",
  "truncated-badge"
];

/**
 * True for a hyphenated token that is a record identifier rather than an English compound. Tuned
 * for precision, because a hit fails a capture:
 *   - any part carries a digit ("run-dev-1", "sec-dev-001", "report-run-investor-202605");
 *   - a placeholder scope ("default-fund");
 *   - three or more parts led by a record kind ("entity-fund-alpha", "investor-anchor-lp").
 */
export function isSlugIdentifier(token) {
  const parts = String(token ?? "").toLowerCase().split("-");
  if (parts.length < 2) {
    return false;
  }

  if (parts.some((part) => /\d/.test(part))) {
    return !(parts.length === 2 && NUMBERED_COMPOUND_HEADS.has(parts[0]) && /^\d+[a-z]?$/.test(parts[1]));
  }

  return parts[0] === "default" || (parts.length >= 3 && SLUG_RECORD_PREFIX.test(parts.join("-")));
}

/**
 * Operator-facing copy rules checked on each route's rendered main content:
 *   - opaque-identifier: GUID/UUID-shaped text. Records are named by display name; identifiers
 *     belong in a collapsed technical-details disclosure, a title attribute or a form field,
 *     none of which is part of the rendered text.
 *   - singular-count-plural-noun: "1 rows" style counts. Use pluralizeCount from the dashboard's
 *     lib/format.
 *   - api-path: a request path such as "/api/ledger/...". Failed requests go through ErrorNotice,
 *     which keeps the path under "Technical details".
 *   - internal-term: "lane", "payload", "mutation", "caller-visible".
 *   - slug-identifier: a slug-shaped record identifier (see isSlugIdentifier).
 *   - latency-not-duration: a line reading just "Latency" followed by a line holding a clock time
 *     ("Jul 13, 06:55 PM UTC") or "Never". Latency is a duration; a last-response time belongs
 *     under its own label. Recorded as "Latency: <value>".
 *   - unavailable-placeholder: "Basis unavailable" or "Saved views unavailable". An undeclared
 *     basis is left off the figures strip (the figure's passport states it); the saved-views
 *     notice in a capture means the harness is missing a fixture.
 *   - disabled-primary-without-reason: a disabled primary button with no title or description
 *     saying why. Passed in by the caller, which reads it from the DOM.
 *   - status-contradiction: the status bar says "up to date" while the page's posture strip says
 *     its freshness needs review or is blocked. Also read from the DOM by the caller.
 *   - truncated-badge: a visible status badge whose text is clipped ("OPE" for "Open"). Badges
 *     never truncate; a long one wraps. Read from the DOM by the caller.
 * Returns one entry per distinct offending excerpt.
 */
export function findCaptureContentIssues(text, { unexplainedDisabledActions = [], statusContradictions = [], truncatedBadges = [] } = {}) {
  const issues = [];
  const seen = new Set();
  const record = (kind, excerpt) => {
    const key = `${kind}:${excerpt.toLowerCase()}`;
    if (!seen.has(key)) {
      seen.add(key);
      issues.push({ kind, excerpt });
    }
  };

  const content = String(text ?? "");
  for (const match of content.matchAll(OPAQUE_IDENTIFIER_PATTERN)) {
    record("opaque-identifier", match[0]);
  }
  for (const match of content.matchAll(SINGULAR_COUNT_PLURAL_NOUN_PATTERN)) {
    const noun = match[1].toLowerCase();
    if (!NOT_PLURAL_NOUNS.has(noun) && !/(ss|us|is)$/.test(noun)) {
      record("singular-count-plural-noun", match[0]);
    }
  }
  for (const match of content.matchAll(API_PATH_PATTERN)) {
    record("api-path", match[0]);
  }
  for (const match of content.matchAll(INTERNAL_TERM_PATTERN)) {
    record("internal-term", match[0]);
  }
  for (const match of content.matchAll(HYPHENATED_TOKEN_PATTERN)) {
    if (isSlugIdentifier(match[0])) {
      record("slug-identifier", match[0]);
    }
  }
  for (const match of content.matchAll(LATENCY_NOT_DURATION_PATTERN)) {
    record("latency-not-duration", match[0].replace(/\n/g, ": "));
  }
  for (const match of content.matchAll(UNAVAILABLE_PLACEHOLDER_PATTERN)) {
    record("unavailable-placeholder", match[0]);
  }
  for (const label of unexplainedDisabledActions) {
    record("disabled-primary-without-reason", String(label || "(unlabelled button)"));
  }
  for (const contradiction of statusContradictions) {
    record("status-contradiction", String(contradiction));
  }
  for (const badge of truncatedBadges) {
    record("truncated-badge", String(badge || "(unlabelled badge)"));
  }

  return issues;
}

/**
 * The content-issue kinds a capture is allowed to render, from its `allowedContentIssues` list of
 * `{ kind, reason }` entries. An entry without a reason is ignored: an exception has to say why.
 */
export function collectAllowedContentIssueKinds(capture) {
  if (!Array.isArray(capture?.allowedContentIssues)) {
    return new Set();
  }

  return new Set(
    capture.allowedContentIssues
      .filter((entry) => entry
        && CAPTURE_CONTENT_ISSUE_KINDS.includes(entry.kind)
        && typeof entry.reason === "string"
        && entry.reason.trim().length > 0)
      .map((entry) => entry.kind)
  );
}

/** Splits a capture's content issues into the ones that fail it and the ones it is allowed. */
export function partitionCaptureContentIssues(text, capture, domFindings = {}) {
  const allowedKinds = collectAllowedContentIssueKinds(capture);
  const blocking = [];
  const allowed = [];
  for (const issue of findCaptureContentIssues(text, domFindings)) {
    (allowedKinds.has(issue.kind) ? allowed : blocking).push(issue);
  }
  return { blocking, allowed };
}

export function describeCaptureContentIssues(captureName, issues) {
  const shown = issues.slice(0, 5).map((issue) => `${issue.kind} "${issue.excerpt}"`).join("; ");
  const more = issues.length > 5 ? `; and ${issues.length - 5} more` : "";
  return `Capture ${captureName} renders ${issues.length} operator-copy issue(s) in its main content: ${shown}${more}. `
    + "Name records by display name and keep identifiers and request paths in technical details; use pluralizeCount "
    + "for counts; write for operators, not about the system; give a disabled primary button a disabledReason; "
    + "show latency only as a duration; leave an undeclared basis off the strip; never clip a status badge; "
    + "a 'Saved views unavailable' notice in a capture means a missing fixture. "
    + "A route that cannot comply yet lists the kind with a reason under allowedContentIssues in the route catalog.";
}

function isActionableBrowserError(text) {
  return /Maximum update depth exceeded|Meridian workstation route failed to render|Unhandled error/i.test(text);
}

function createPageErrorTracker(page) {
  const errors = [];
  const waiters = new Set();

  const record = (message) => {
    errors.push(message);
    for (const waiter of waiters) {
      waiter.resolve(message);
    }
    waiters.clear();
  };

  const onPageError = (error) => {
    record(`pageerror: ${error.stack ?? error.message}`);
  };
  const onConsole = (message) => {
    if (message.type() !== "error") {
      return;
    }

    const text = message.text();
    if (isActionableBrowserError(text)) {
      record(`console.error: ${text}`);
    }
  };

  page.on("pageerror", onPageError);
  page.on("console", onConsole);

  return {
    record,
    reset() {
      errors.length = 0;
      waiters.clear();
    },
    firstError() {
      return errors[0] ?? null;
    },
    waitForErrorSignal() {
      const existing = errors[0];
      if (existing) {
        return {
          promise: Promise.resolve(existing),
          dispose() {}
        };
      }

      const waiter = {};
      waiter.promise = new Promise((resolve) => {
        waiter.resolve = resolve;
      });
      waiter.dispose = () => waiters.delete(waiter);
      waiters.add(waiter);
      return waiter;
    },
    dispose() {
      page.off("pageerror", onPageError);
      page.off("console", onConsole);
      waiters.clear();
    }
  };
}

async function waitForCaptureStep(stepPromise, pageErrors, captureName, description) {
  const waiter = pageErrors.waitForErrorSignal();
  try {
    await Promise.race([
      stepPromise,
      waiter.promise.then((message) => {
        throw new Error(`Capture ${captureName} hit a browser render error while ${description}: ${message}`);
      })
    ]);
  } finally {
    waiter.dispose();
  }
}

function assertNoCaptureBrowserError(pageErrors, captureName) {
  const message = pageErrors.firstError();
  if (message) {
    throw new Error(`Capture ${captureName} hit a browser render error: ${message}`);
  }
}

/** Track data requests independently of transport quiet periods and UI busy labels. */
export function createCaptureApiTracker(page) {
  const pending = new Set();
  const waiters = new Set();
  let revision = 0;
  const onRequest = (request) => {
    if (new URL(request.url()).pathname.startsWith("/api/")) {
      pending.add(request);
      revision += 1;
    }
  };
  const onFinished = (request) => {
    if (pending.delete(request)) {
      revision += 1;
      if (pending.size === 0) for (const finish of [...waiters]) finish();
    }
  };
  page.on("request", onRequest);
  page.on("requestfinished", onFinished);
  page.on("requestfailed", onFinished);
  return {
    get revision() { return revision; },
    get pendingCount() { return pending.size; },
    waitForIdle(timeoutMs) {
      if (pending.size === 0) return Promise.resolve();
      return new Promise((resolve, reject) => {
        const finish = (error) => {
          clearTimeout(timer);
          waiters.delete(finish);
          if (error) reject(error);
          else resolve();
        };
        const timer = setTimeout(() => finish(new Error(
          `Screenshot API requests did not finish within ${timeoutMs}ms.`
        )), timeoutMs);
        waiters.add(finish);
      });
    },
    dispose() {
      page.off("request", onRequest);
      page.off("requestfinished", onFinished);
      page.off("requestfailed", onFinished);
      for (const finish of [...waiters]) finish(new Error("Screenshot API request tracker closed."));
      pending.clear();
    }
  };
}

/** Wait for rendered content, not for an unrelated interval without network traffic. */
export async function waitForCaptureReadiness(page, timeoutMs, apiTracker = null) {
  const deadline = Date.now() + timeoutMs;
  do {
    await apiTracker?.waitForIdle(Math.max(1, deadline - Date.now()));
    const revision = apiTracker?.revision;
    await page.waitForFunction(async () => {
      const snapshot = () => {
        const root = document.querySelector("main") ?? document.body;
        if (!root || document.fonts?.status === "loading") return null;
        const visible = (element) => element.getClientRects().length > 0
          && getComputedStyle(element).visibility !== "hidden";
        if ((root.matches("[aria-busy='true']") && visible(root))
          || Array.from(root.querySelectorAll("[aria-busy='true']")).some(visible)) return null;
        if (Array.from(document.images).some((image) => visible(image) && !image.complete)) return null;
        const elements = [root, ...root.querySelectorAll("*")].filter(visible);
        return JSON.stringify([
          root.innerText,
          root.scrollWidth,
          root.scrollHeight,
          elements.map((element) => {
            const bounds = element.getBoundingClientRect();
            return [bounds.x, bounds.y, bounds.width, bounds.height];
          })
        ]);
      };
      const before = snapshot();
      if (before === null) return false;
      // Two painted frames expose font, grid and data-region layout changes without
      // imposing networkidle's fixed 500ms quiet period on every route.
      await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
      return snapshot() === before;
    }, undefined, { timeout: Math.max(1, deadline - Date.now()) });
    // Data can start loading while the DOM settles, even if its panel exposes no
    // aria-busy marker. Recheck after those responses have painted their content.
    if (!apiTracker || (apiTracker.pendingCount === 0 && apiTracker.revision === revision)) return;
  } while (Date.now() < deadline);
  throw new Error(`Screenshot content did not settle within ${timeoutMs}ms.`);
}

async function captureRoute(page, pageErrors, capture, outputDir, baseUrl, defaults, minBytes, minTextLength, timeoutMs, readinessTimeoutMs, textDir = null, apiTracker = null) {
  pageErrors.reset();
  const viewport = {
    width: Number(capture.viewport?.width ?? defaults.width ?? 1440),
    height: Number(capture.viewport?.height ?? defaults.height ?? 1100)
  };
  await page.setViewportSize(viewport);

  const url = toRouteUrl(baseUrl, capture.path);
  const fileName = `${capture.name}.png`;
  const outputPath = path.join(outputDir, fileName);
  const started = Date.now();

  await waitForCaptureStep(
    page.goto(url, { waitUntil: "domcontentloaded", timeout: timeoutMs }),
    pageErrors,
    capture.name,
    "loading the route"
  );
  // Label the frame timeout explicitly: the raw Playwright message ("page.waitForSelector:
  // Timeout ... exceeded.") does not say which wait failed, and this is the one wait that
  // fails when the app shell itself never mounts (e.g. the first-run activation gate has no
  // fixture and blocks every route).
  await waitForCaptureStep(
    page.waitForSelector(".workstation-frame", { timeout: timeoutMs }).catch((cause) => {
      if (cause?.name === "TimeoutError") {
        throw new Error(
          `App shell failed to mount: .workstation-frame did not render within ${timeoutMs}ms. `
          + "Check shell-level screenshot fixtures (e.g. /api/workstation/first-run/) and the dev server logs."
        );
      }
      throw cause;
    }),
    pageErrors,
    capture.name,
    "waiting for the workstation frame"
  );
  for (const waitForText of collectCaptureWaitForTexts(capture)) {
    await waitForCaptureStep(
      page.getByText(waitForText, { exact: false }).filter({ visible: true }).first().waitFor({ timeout: readinessTimeoutMs }),
      pageErrors,
      capture.name,
      `waiting for visible text '${waitForText}'`
    );
  }
  if (Array.isArray(capture.waitForSelectors)) {
    for (const selector of capture.waitForSelectors) {
      if (typeof selector === "string" && selector.trim().length > 0) {
        await waitForCaptureStep(
          page.waitForSelector(selector, { timeout: readinessTimeoutMs }),
          pageErrors,
          capture.name,
          `waiting for selector '${selector}'`
        );
      }
    }
  }
  for (const waitForAbsentText of collectCaptureWaitForAbsentTexts(capture)) {
    await waitForCaptureStep(
      page.getByText(waitForAbsentText, { exact: false })
        .filter({ visible: true })
        .first()
        .waitFor({ state: "hidden", timeout: readinessTimeoutMs }),
      pageErrors,
      capture.name,
      `waiting for transitional text '${waitForAbsentText}' to clear`
    );
  }
  await waitForCaptureStep(
    waitForCaptureReadiness(page, readinessTimeoutMs, apiTracker),
    pageErrors,
    capture.name,
    "waiting for fonts, images and settled content layout"
  );
  assertNoCaptureBrowserError(pageErrors, capture.name);

  const textLength = await page.evaluate(() => document.body.innerText.trim().length);
  if (textLength < minTextLength) {
    throw new Error(`Rendered body text length ${textLength} is below minimum ${minTextLength}`);
  }

  // Rendered text only: collapsed technical details, title attributes and form field values are
  // not part of innerText, which is exactly where identifiers are meant to live.
  const mainText = await page.evaluate(() => (document.querySelector("main") ?? document.body).innerText);
  // Disabled primary actions are read from the DOM, not the text: the rule is about the reason a
  // control carries, which is an attribute.
  const disabledActionProbe = await page.evaluate(() =>
    Array.from((document.querySelector("main") ?? document.body).querySelectorAll("button.mds-btn--primary:disabled"))
      .filter((button) => button.getClientRects().length > 0)
      .filter((button) => !["title", "aria-describedby", "aria-description"]
        .some((attribute) => (button.getAttribute(attribute) ?? "").trim().length > 0))
      .map((button) => (button.getAttribute("aria-label") ?? button.textContent ?? "").trim())
  );
  const unexplainedDisabledActions = Array.isArray(disabledActionProbe) ? disabledActionProbe : [];
  // The status bar and the page's posture strip answer the same freshness question; the bar may
  // not say "up to date" while a strip on the page says its freshness needs review or is blocked.
  const statusProbe = await page.evaluate(() => {
    const staleFact = Array.from(document.querySelectorAll(
      "main [data-trust-fact='freshness'][data-trust-tone='review'], main [data-trust-fact='freshness'][data-trust-tone='blocked']"
    )).find((element) => element.getClientRects().length > 0);
    const footer = document.querySelector("footer.workstation-statusbar");
    return {
      pageFreshness: staleFact ? (staleFact.textContent ?? "").replace(/\s+/g, " ").trim() : null,
      statusBar: footer ? (footer.textContent ?? "").replace(/\s+/g, " ").trim() : ""
    };
  });
  const statusContradictions = statusProbe?.pageFreshness && /up to date/i.test(statusProbe.statusBar ?? "")
    ? [`status bar "${statusProbe.statusBar}" vs page "${statusProbe.pageFreshness}"`]
    : [];
  // A clipped status badge ("OPE") reads as a different status. Badges are measured, not read:
  // the text is all there in innerText even when the box cuts it off.
  const truncatedBadgeProbe = await page.evaluate(() =>
    Array.from(document.querySelectorAll(".mds-sev, .mds-badge"))
      .filter((badge) => badge.getClientRects().length > 0)
      .filter((badge) => badge.scrollWidth > badge.clientWidth + 1)
      .map((badge) => (badge.textContent ?? "").replace(/\s+/g, " ").trim())
  );
  const truncatedBadges = Array.isArray(truncatedBadgeProbe) ? truncatedBadgeProbe : [];
  if (textDir) {
    await fs.mkdir(textDir, { recursive: true });
    await fs.writeFile(path.join(textDir, `${capture.name}.txt`), `${mainText}\n`);
  }
  const contentIssues = partitionCaptureContentIssues(mainText, capture, { unexplainedDisabledActions, statusContradictions, truncatedBadges });
  if (contentIssues.blocking.length > 0) {
    throw new Error(describeCaptureContentIssues(capture.name, contentIssues.blocking));
  }

  const actualUrl = page.url();
  const requestedPath = new URL(url).pathname;
  // Some captures deliberately target a compatibility route that redirects to
  // the canonical screen (e.g. the accounting/data evidence entry points land
  // on the reporting evidence workbench). When a capture declares that redirect
  // via `expectedRedirectPath`, treat the redirect destination as the expected
  // final path so the intentional navigation is not mistaken for a route that
  // bounced to a fallback. The strict check still catches undeclared redirects.
  const expectedPath = typeof capture.expectedRedirectPath === "string" && capture.expectedRedirectPath.trim().length > 0
    ? new URL(toRouteUrl(baseUrl, capture.expectedRedirectPath.trim())).pathname
    : requestedPath;
  const actualPath = new URL(actualUrl).pathname;
  if (actualPath !== expectedPath) {
    throw new Error(`Capture ${capture.name} ended on '${actualPath}', expected '${expectedPath}'.`);
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
    scenario: capture.scenario,
    previewUrl: capture.previewUrl,
    route: capture.path,
    url,
    actualUrl,
    requestedPath,
    expectedPath,
    actualPath,
    file: fileName,
    path: outputPath,
    viewport,
    bytes: buffer.length,
    textLength,
    // Exceptions the route catalog grants this capture, kept visible in the manifest.
    ...(contentIssues.allowed.length > 0 ? { allowedContentIssues: contentIssues.allowed } : {}),
    durationSeconds: Number(((Date.now() - started) / 1000).toFixed(2)),
    status: "passed"
  };
}

export async function captureRouteWithRetries(
  browser,
  capture,
  createApiResolver,
  outputDir,
  baseUrl,
  defaults,
  minBytes,
  minTextLength,
  timeoutMs,
  readinessTimeoutMs,
  maxAttempts,
  onTrackerChange,
  { textDir = null } = {}
) {
  let lastError = null;

  for (let attempt = 1; attempt <= maxAttempts; attempt += 1) {
    // Each attempt renders in a fresh browser context so a retry starts from the
    // route's default first-load state instead of inheriting a half-rendered
    // page from the attempt that just failed.
    let context = null;
    let tracker = null;
    let apiTracker = null;
    let attemptError = null;
    try {
      context = await browser.newContext();
      const page = await context.newPage();
      tracker = createPageErrorTracker(page);
      apiTracker = createCaptureApiTracker(page);
      onTrackerChange(tracker);
      const resolver = createApiResolver();
      await setupScenarioApiMocking(page, resolver, tracker.record);
      const result = await captureRoute(
        page,
        tracker,
        capture,
        outputDir,
        baseUrl,
        defaults,
        minBytes,
        minTextLength,
        timeoutMs,
        readinessTimeoutMs,
        textDir,
        apiTracker
      );
      resolver.assertNoUnexpectedRequests();
      result.attempts = attempt;
      return result;
    } catch (error) {
      lastError = error;
      attemptError = error;
      // A screenshot can have been written before image-size or strict API
      // assertions fail. Never leave that rejected frame in the passed catalog.
      await fs.rm(path.join(outputDir, `${capture.name}.png`), { force: true });
      if (attempt < maxAttempts) {
        const message = error instanceof Error ? error.message : String(error);
        console.warn(
          `::warning::Capture ${capture.name} (${capture.path}) attempt ${attempt}/${maxAttempts} failed: `
          + `${message.split(/\r?\n/)[0]}; retrying.`
        );
      }
    } finally {
      tracker?.dispose();
      apiTracker?.dispose();
      onTrackerChange(null);
      try {
        if (context) await context.close();
      } catch (error) {
        if (attemptError) {
          // Closing a crashed page must not replace the render failure that
          // explains why this attempt was rejected.
          console.warn(`::warning::Could not close capture context: ${error instanceof Error ? error.message : String(error)}`);
        } else {
          await fs.rm(path.join(outputDir, `${capture.name}.png`), { force: true });
          throw error;
        }
      }
    }
  }

  throw lastError ?? new Error(`Capture ${capture.name} failed after ${maxAttempts} attempt(s).`);
}

export async function writeCaptureManifest(manifest, manifestPath, logs) {
  manifest.capturedCount = manifest.captures.filter((capture) => capture.status === "passed").length;
  manifest.failedCaptureCount = manifest.captures.filter((capture) => capture.status === "failed").length;
  manifest.unattemptedCaptureCount = Math.max(0, manifest.selectedCaptureCount - manifest.captures.length);
  manifest.logs = logs.slice(-200);
  await fs.mkdir(path.dirname(manifestPath), { recursive: true });
  const temporaryPath = `${manifestPath}.tmp`;
  await fs.writeFile(temporaryPath, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
  await fs.rename(temporaryPath, manifestPath);
}

export async function finalizeCaptureRun(manifest, manifestPath, startedUtc, logs, cleanupTasks) {
  const hadPrimaryError = manifest.status === "failed";
  const cleanupErrors = [];
  for (const task of cleanupTasks) {
    try {
      await task.run();
    } catch (error) {
      cleanupErrors.push(`${task.name}: ${error instanceof Error ? error.message : String(error)}`);
    }
  }
  if (cleanupErrors.length > 0) {
    manifest.cleanupErrors = cleanupErrors;
    manifest.status = "failed";
    manifest.error ??= `Capture cleanup failed: ${cleanupErrors.join(" | ")}`;
  }
  manifest.finishedAtUtc = new Date().toISOString();
  manifest.durationSeconds = Number(((Date.now() - startedUtc.getTime()) / 1000).toFixed(2));
  try {
    await writeCaptureManifest(manifest, manifestPath, logs);
  } catch (error) {
    if (!hadPrimaryError) throw error;
    // Preserve the original capture error if diagnostics cannot be persisted.
    console.error(`Could not write capture manifest: ${error instanceof Error ? error.message : String(error)}`);
  }
  if (!hadPrimaryError && cleanupErrors.length > 0) {
    throw new Error(manifest.error);
  }
}

async function main() {
  const { values, valueLists, flags } = parseArgs(process.argv.slice(2));
  if (flags.has("workers")) throw new Error("--workers requires an integer between 1 and 8.");
  const workers = parseCaptureWorkers(values.get("workers"));
  const repoRoot = values.has("repo-root")
    ? path.resolve(values.get("repo-root"))
    : await findRepoRoot(process.cwd());
  const dashboardDir = resolveRepoPath(repoRoot, values.get("dashboard-dir") ?? "src/Meridian.Ui/dashboard");
  const configPath = resolveRepoPath(repoRoot, values.get("config") ?? "scripts/dev/web-screenshot-routes.json");
  const routeConfig = await readJson(configPath);

  if (Number(routeConfig.version) !== 1) {
    throw new Error(`Unsupported web screenshot route config version: ${routeConfig.version}`);
  }

  const allCaptures = Array.isArray(routeConfig.captures) ? routeConfig.captures : [];
  if (allCaptures.length === 0) {
    throw new Error(`No web screenshot captures found in ${configPath}`);
  }
  assertCaptureRouteStateIdentity(allCaptures);
  const captureSelectors = collectCaptureSelectors(valueLists);
  if (flags.has("scenario")) throw new Error("--scenario requires a registered scenario ID, for example accounting.normal.");
  const scenarioId = values.get("scenario");
  const scenarioSource = !flags.has("list") || scenarioId
    ? await loadScreenshotScenarioModule(dashboardDir)
    : null;
  let selectedScenario;
  let captures;
  try {
    selectedScenario = scenarioId ? scenarioSource.module.getScenario(scenarioId) : null;
    const selected = captureSelectors.length > 0 || !selectedScenario
      ? selectCaptures(allCaptures, captureSelectors)
      : selectCaptures(allCaptures, [selectedScenario.previewPath]);
    captures = selected.map((capture) => selectedScenario ? scenarioCapture(capture, selectedScenario, scenarioSource.module) : capture);
  } catch (error) {
    await scenarioSource?.close();
    throw error;
  }

  if (flags.has("list")) {
    for (const capture of captures) {
      console.log(`${capture.id ?? ""}\t${capture.name}\t${capture.path}`);
    }
    await scenarioSource?.close();
    return;
  }

  const outputDir = resolveRepoPath(repoRoot, values.get("output-dir") ?? routeConfig.outputRoot ?? "docs/screenshots/web");
  const manifestPath = resolveRepoPath(repoRoot, values.get("manifest") ?? "artifacts/web-screenshots/manifest.json");
  // Optional: write each route's rendered main text as <capture>.txt, for reviewing operator copy
  // across the catalog or tuning the content rules below.
  const textDir = values.has("text-dir") ? resolveRepoPath(repoRoot, values.get("text-dir")) : null;
  const fixturesPath = resolveRepoPath(
    repoRoot,
    values.get("fixtures") ?? routeConfig.fixturesPath ?? "scripts/dev/web-screenshot-fixtures.json"
  );
  const routeCatalogPath = resolveRepoPath(
    repoRoot,
    values.get("route-catalog") ?? "src/Meridian.Ui/dashboard/src/lib/workspace.ts"
  );
  const appShellPath = resolveRepoPath(
    repoRoot,
    values.get("app-shell") ?? "src/Meridian.Ui/dashboard/src/app.tsx"
  );
  const host = values.get("host") ?? "127.0.0.1";
  const port = Number(values.get("port") ?? "5173");
  const timeoutMs = Number(values.get("timeout-ms") ?? "120000");
  // Readiness assertions (waitForText/selector) use a shorter budget than the
  // navigation/frame timeout so a single screen that never renders its expected
  // content fails fast and the run moves on to the next route instead of
  // blocking the whole catalog for the full navigation timeout.
  const readinessTimeoutMs = Number(values.get("readiness-timeout-ms") ?? "30000");
  const minBytes = Number(values.get("min-bytes") ?? "12000");
  const minTextLength = Number(values.get("min-text-length") ?? "80");
  // A transient render or readiness hiccup on a single route should not fail
  // the whole catalog. Retry each capture a bounded number of times (fresh
  // context each attempt) before recording it as failed.
  const parsedCaptureRetries = Number(values.get("capture-retries") ?? "1");
  const captureRetries = Number.isFinite(parsedCaptureRetries) ? Math.max(0, parsedCaptureRetries) : 1;
  const captureAttempts = captureRetries + 1;
  const basePath = routeConfig.basePath ?? "/workstation";
  const baseUrl = values.get("base-url") ?? `http://${host}:${port}${basePath}`;
  const logs = [];
  const startedUtc = new Date();
  let server = null;
  let browser = null;
  const activeTrackers = new Set();
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
    selectedCaptureCount: captures.length,
    totalCaptureCount: allCaptures.length,
    maxAttemptsPerCapture: captureAttempts,
    workers: Math.min(workers, captures.length),
    scenario: selectedScenario?.id ?? null,
    accountingScenario: selectedScenario?.id ?? "accounting.normal",
    previewUrl: selectedScenario ? scenarioSource.module.scenarioPreviewUrl(selectedScenario) : null,
    strictApiRequests: flags.has("strict"),
    captures: results,
    logs: []
  };

  try {
    await writeCaptureManifest(manifest, manifestPath, logs);
    // Coverage gaps (a live app route with no capture entry) are reported at the
    // end as a run failure rather than aborting before any screenshot is taken,
    // so the catalog stays self-adjusting: every configured screen is still
    // captured and the run tells you exactly which new route needs an entry.
    const missingCoverage = await findMissingCaptureRoutePaths(allCaptures, routeCatalogPath, appShellPath);

    if (!flags.has("skip-server")) {
      server = startViteServer(dashboardDir, host, port, logs);
      await waitForServer(`${normalizeBaseUrl(baseUrl)}/`, timeoutMs, server, logs);
    }

    // Load fixture API responses and mock all /api/** requests so screenshots
    // never depend on a running Meridian backend.
    const fixtureConfig = await readJson(fixturesPath);
    const fixtureRoutes = (fixtureConfig && typeof fixtureConfig.routes === "object")
      ? fixtureConfig.routes
      : {};
    const requiredFixtureRoutes = collectRequiredFixtureRoutes(captures);
    const scenario = selectedScenario ?? scenarioSource.module.getScenario("accounting.normal");
    const missingFixtureRoutes = findMissingFixtureRoutes(requiredFixtureRoutes, fixtureRoutes, scenarioSource.module, scenario);
    const createApiResolver = () => createScreenshotApiResolver(scenarioSource.module, fixtureRoutes, {
      scenarioId,
      strict: flags.has("strict")
    });
    if (missingFixtureRoutes.length > 0) {
      // A missing fixture no longer aborts the run; the affected route may render
      // a degraded state and, if it fails its readiness checks, is reported as a
      // per-route capture failure while every other route still captures.
      console.warn(
        `::warning::Screenshot fixture route coverage is incomplete. Missing route fixtures: ${missingFixtureRoutes.join(", ")}. `
        + "Affected routes may render degraded and be reported as capture failures."
      );
    }

    const dashboardRequire = createRequire(path.join(dashboardDir, "package.json"));
    const { chromium } = dashboardRequire("playwright");
    // Sandboxes and CI images often provide a system Chromium instead of the exact
    // browser build the pinned Playwright version would download.
    const chromiumExecutablePath = process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH;
    // --disable-dev-shm-usage avoids Chromium crashing when the CI container's
    // /dev/shm is too small for full-page screenshots of large workstation routes.
    const launchOptions = { args: ["--disable-dev-shm-usage"] };
    if (chromiumExecutablePath) {
      launchOptions.executablePath = chromiumExecutablePath;
    }
    browser = await chromium.launch(launchOptions);

    await runCaptureWorkers(captures, workers, async (capture) => {
      // Each capture renders in a fresh browser context so it shows the
      // route's default first-load state. The app shell persists
      // workflow-continuity, activity, and focus state across navigations,
      // so a shared context makes capture results depend on visit order.
      let activeTracker = null;
      try {
        const result = await captureRouteWithRetries(
          browser,
          capture,
          createApiResolver,
          outputDir,
          baseUrl,
          routeConfig.defaultViewport ?? {},
          minBytes,
          minTextLength,
          timeoutMs,
          readinessTimeoutMs,
          captureAttempts,
          (tracker) => {
            if (activeTracker) activeTrackers.delete(activeTracker);
            activeTracker = tracker;
            if (tracker) activeTrackers.add(tracker);
          },
          { textDir }
        );
        console.log(`Captured ${capture.name} -> ${result.path}`);
        return result;
      } catch (error) {
        // Fault isolation: a screen that still fails after its retries is
        // recorded with its error and skipped so the run continues through the
        // remaining screens. The failure is surfaced in the end-of-run summary
        // and turns the overall run non-zero, but never blocks the rest of the
        // catalog.
        const message = error instanceof Error ? error.message : String(error);
        const failed = {
          id: capture.id,
          name: capture.name,
          docLabel: capture.docLabel,
          scenario: capture.scenario,
          previewUrl: capture.previewUrl,
          route: capture.path,
          url: toRouteUrl(baseUrl, capture.path),
          status: "failed",
          attempts: captureAttempts,
          error: message
        };
        console.error(`::warning::Skipped ${capture.name} (${capture.path}): ${message.split(/\r?\n/)[0]}`);
        return failed;
      }
    }, async (completed) => {
      // Persist every completed route so a cancelled job still has a useful
      // partial manifest, including counts when the shell circuit breaker trips.
      results.splice(0, results.length, ...completed);
      await writeCaptureManifest(manifest, manifestPath, logs);
    });

    const proxyErrors = logs.filter((line) => /http proxy error:\s*\/api\//i.test(line));
    const failedCaptures = results.filter((result) => result.status === "failed");
    const passedCaptures = results.filter((result) => result.status === "passed");
    manifest.capturedCount = passedCaptures.length;
    manifest.failedCaptureCount = failedCaptures.length;

    console.log(
      `\nScreenshot capture summary: ${passedCaptures.length}/${captures.length} route(s) captured successfully.`
    );
    if (failedCaptures.length > 0) {
      console.log(`${failedCaptures.length} route(s) did not render correctly and were skipped:`);
      for (const failure of failedCaptures) {
        const firstLine = String(failure.error ?? "unknown error").split(/\r?\n/)[0];
        console.log(`  - ${failure.name} (${failure.route}): ${firstLine}`);
      }
    }

    const problems = [];
    if (missingCoverage.length > 0) {
      problems.push(`Live route(s) without a screenshot capture entry: ${missingCoverage.join(", ")}`);
    }
    if (proxyErrors.length > 0) {
      problems.push(`Detected ${proxyErrors.length} Vite proxy API error log(s) during screenshot capture.`);
    }
    for (const problem of problems) {
      console.error(`::error::${problem}`);
    }

    // The run has already moved through every screen and written a screenshot
    // for each one that rendered. If any screen failed or coverage is
    // incomplete, exit non-zero with a consolidated summary so CI flags it while
    // the successful screenshots remain available.
    if (failedCaptures.length > 0 || problems.length > 0) {
      manifest.status = "failed";
      const summary = [
        failedCaptures.length > 0
          ? `${failedCaptures.length} route(s) failed to render: ${failedCaptures.map((failure) => failure.name).join(", ")}`
          : null,
        ...problems
      ].filter(Boolean).join(" | ");
      throw new Error(summary);
    }

    manifest.status = "passed";
  } catch (error) {
    manifest.status = "failed";
    manifest.error = error instanceof Error ? error.message : String(error);
    throw error;
  } finally {
    await finalizeCaptureRun(manifest, manifestPath, startedUtc, logs, [
      { name: "page error trackers", run: () => { for (const tracker of activeTrackers) tracker.dispose(); } },
      { name: "browser", run: () => browser?.close() },
      { name: "dev server", run: () => stopProcess(server) },
      { name: "scenario module", run: () => scenarioSource.close() }
    ]);
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  main().catch((error) => {
    console.error(error instanceof Error ? error.stack ?? error.message : error);
    process.exitCode = 1;
  });
}
