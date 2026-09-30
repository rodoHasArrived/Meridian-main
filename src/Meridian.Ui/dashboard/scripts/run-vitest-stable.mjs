#!/usr/bin/env node
import { spawn } from "node:child_process";
import { readdir, mkdir, readFile, writeFile, appendFile, mkdtemp } from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { readEvidence, identityDigest } from "./vitest-evidence.mjs";

const root = process.cwd();
const sourceRoot = path.join(root, "src");
const vitestEntry = path.join(root, "node_modules", "vitest", "vitest.mjs");
const args = process.argv.slice(2);
const defaultBatchSize = 8;
const requestedBatchSize = Number.parseInt(process.env.DASHBOARD_VITEST_BATCH_SIZE ?? "", 10);
const batchSize = Number.isFinite(requestedBatchSize) && requestedBatchSize > 0
  ? requestedBatchSize
  : defaultBatchSize;

const { passthroughArgs, shard } = parseRunnerArgs(args);
const outputRoot = path.resolve(process.env.MERIDIAN_BROWSER_RESULTS_DIR ?? path.join(root, "../../../artifacts/test-results/browser"));
await mkdir(outputRoot, { recursive: true });
// Unique report directories prevent a stale JSON file from satisfying discovery.
const reportDirectory = await mkdtemp(path.join(outputRoot, "run-"));
const results = [];
const started = performance.now();
let exitCode = 0;

if (hasExplicitFileFilter(args)) {
  exitCode = await runBatch(["run", ...args], []);
} else {
  const allFiles = await findTestFiles(sourceRoot);
  const selectedFiles = shard ? applyShard(allFiles, shard.index, shard.total) : allFiles;
  if (selectedFiles.length === 0) {
    console.error("[dashboard-test-runner] No dashboard test files matched.");
    exitCode = 1;
  }
  const batches = chunk(selectedFiles, batchSize);
  console.log(`[dashboard-test-runner] Running ${selectedFiles.length} test files in ${batches.length} batch(es).`);
  for (const batch of batches) {
    const files = batch.map((file) => toPosixPath(path.relative(root, file)));
    exitCode = await runBatch(["run", ...passthroughArgs, ...files], files);
    if (exitCode) break;
  }
}

const counts = { passed: 0, failed: 0, skipped: 0, other: 0 };
for (const result of results) {
  for (const key of Object.keys(counts)) counts[key] += result.evidence?.counts[key] ?? 0;
}
const identities = results.flatMap(result => result.evidence?.identities ?? []);
const summary = { counts, testIdentityDigest: identities.length ? identityDigest(identities) : null,
  testSeconds: (performance.now() - started) / 1000, exitCode, batchSize, workers: 2,
  commitSha: process.env.GITHUB_SHA, runId: process.env.GITHUB_RUN_ID, runAttempt: process.env.GITHUB_RUN_ATTEMPT,
  cacheHit: process.env.MERIDIAN_DEPENDENCY_CACHE_HIT ?? "not reported", queueSeconds: null, results };
await writeFile(path.join(outputRoot, "summary.json"), JSON.stringify(summary, null, 2) + "\n");
const markdown = ["### Browser test evidence", "", `Tests: ${JSON.stringify(counts)}; test execution: ${summary.testSeconds.toFixed(3)}s`,
  `Run attempt: ${summary.runAttempt ?? "local"}; dependency cache hit: ${summary.cacheHit}`,
  "Queue time is reported separately by ci-metrics.py after the run completes.", "",
  "| Batch | Duration (s) | Result |", "| --- | ---: | --- |",
  ...results.map((r, i) => `| ${i + 1} | ${r.seconds.toFixed(3)} | ${r.error ?? r.exitCode} |`), "",
  "Reproduce: `bash scripts/ci.sh --lane verify-browser`", ""].join("\n");
await writeFile(path.join(outputRoot, "summary.md"), markdown);
if (process.env.GITHUB_STEP_SUMMARY) await appendFile(process.env.GITHUB_STEP_SUMMARY, markdown);
process.exit(exitCode);

async function runBatch(vitestArgs, files) {
  const report = path.join(reportDirectory, `batch-${results.length + 1}.json`);
  const batchStart = performance.now();
  const code = await runVitest([...vitestArgs, "--reporter=default", "--reporter=json", `--outputFile=${report}`]);
  const result = { files, report, seconds: (performance.now() - batchStart) / 1000, exitCode: code };
  try {
    result.evidence = readEvidence(JSON.parse(await readFile(report, "utf8")), root, files);
  } catch (error) {
    result.error = error.message;
    result.exitCode ||= 1;
    console.error(`[dashboard-test-runner] Invalid evidence: ${error.message}`);
  }
  results.push(result);
  return result.exitCode;
}

function parseRunnerArgs(rawArgs) {
  const passthroughArgs = [];
  let shard;

  for (let index = 0; index < rawArgs.length; index += 1) {
    const arg = rawArgs[index];

    if (arg === "--shard") {
      const value = rawArgs[index + 1];
      if (!value) {
        throw new Error("--shard requires a value like 1/4.");
      }

      shard = parseShard(value);
      index += 1;
      continue;
    }

    if (arg.startsWith("--shard=")) {
      shard = parseShard(arg.slice("--shard=".length));
      continue;
    }

    passthroughArgs.push(arg);
  }

  return { passthroughArgs, shard };
}

function parseShard(value) {
  const match = /^(\d+)\/(\d+)$/.exec(value);
  if (!match) {
    throw new Error(`Invalid shard value "${value}". Expected a value like 1/4.`);
  }

  const index = Number.parseInt(match[1], 10);
  const total = Number.parseInt(match[2], 10);
  if (index < 1 || total < 1 || index > total) {
    throw new Error(`Invalid shard value "${value}". Shard index must be between 1 and total.`);
  }

  return { index, total };
}

function hasExplicitFileFilter(rawArgs) {
  for (let index = 0; index < rawArgs.length; index += 1) {
    const arg = rawArgs[index];

    if (arg === "--shard") {
      index += 1;
      continue;
    }

    if (!arg.startsWith("-")) {
      return true;
    }
  }

  return false;
}

async function findTestFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      files.push(...await findTestFiles(fullPath));
      continue;
    }

    if (/\.(test|spec)\.(ts|tsx)$/.test(entry.name)) {
      files.push(fullPath);
    }
  }

  return files.sort((left, right) => toPosixPath(left).localeCompare(toPosixPath(right)));
}

function applyShard(files, index, total) {
  return files.filter((_, fileIndex) => fileIndex % total === index - 1);
}

function chunk(files, size) {
  const batches = [];
  for (let index = 0; index < files.length; index += size) {
    batches.push(files.slice(index, index + size));
  }

  return batches;
}

function toPosixPath(value) {
  return value.replaceAll(path.sep, "/");
}

function runVitest(vitestArgs) {
  return new Promise((resolve) => {
    const child = spawn(process.execPath, [vitestEntry, ...vitestArgs], {
      cwd: root,
      env: {
        ...process.env,
        NODE_OPTIONS: appendNodeOption(process.env.NODE_OPTIONS, "--max-old-space-size=4096")
      },
      shell: false,
      stdio: "inherit",
      windowsHide: true
    });

    child.on("exit", (code, signal) => {
      if (signal) {
        console.error(`[dashboard-test-runner] Vitest exited from signal ${signal}.`);
        resolve(1);
        return;
      }

      resolve(code ?? 1);
    });

    child.on("error", (error) => {
      console.error(`[dashboard-test-runner] Failed to start Vitest: ${error.message}`);
      resolve(1);
    });
  });
}

function appendNodeOption(existing, option) {
  if (!existing) {
    return option;
  }

  if (existing.includes(option)) {
    return existing;
  }

  return `${existing} ${option}`;
}
