#!/usr/bin/env node
// Combine hosted browser shard summaries and prove every dashboard test file ran exactly once.
//
// Meridian CI runs the dashboard tests as several runner shards. This is the lane's single
// verdict: it fails unless every expected shard reported for the same commit, the shards
// together ran every test file present in this checkout once and only once, and nothing
// failed. A missing, duplicated or foreign summary can therefore never read as green.
import { readdir, readFile, mkdir, writeFile, appendFile } from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";
import { identityDigest } from "./vitest-evidence.mjs";
import { findTestFiles, toPosixPath } from "./vitest-files.mjs";

const SUMMARY_SUFFIX = "test-results/browser/summary.json";

async function findSummaries(directory) {
  let entries;
  try {
    entries = await readdir(directory, { withFileTypes: true });
  } catch {
    return [];
  }
  const found = [];
  for (const entry of entries.sort((left, right) => left.name.localeCompare(right.name))) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) found.push(...await findSummaries(fullPath));
    else if (toPosixPath(fullPath).endsWith(SUMMARY_SUFFIX)) found.push(fullPath);
  }
  return found;
}

export function merge(summaries, expectedShards, expectedFiles) {
  const errors = [];
  const byIndex = new Map();
  for (const { source, payload } of summaries) {
    const shard = payload?.shard;
    if (!shard || !Number.isInteger(shard.index) || shard.total !== expectedShards
        || shard.index < 1 || shard.index > expectedShards) {
      errors.push(`${source}: summary is not one of the ${expectedShards} expected shards`);
    } else if (byIndex.has(shard.index)) {
      errors.push(`${source}: duplicate summary for shard ${shard.index}`);
    } else {
      byIndex.set(shard.index, payload);
    }
  }
  for (let index = 1; index <= expectedShards; index += 1) {
    if (!byIndex.has(index)) errors.push(`shard ${index}/${expectedShards}: no summary was produced`);
  }

  const shards = [...byIndex.keys()].sort((left, right) => left - right).map((index) => byIndex.get(index));
  const commits = new Set(shards.map((payload) => payload.commitSha ?? null));
  if (commits.size > 1) errors.push(`shards report different commits: ${[...commits].sort().join(", ")}`);

  const counts = { passed: 0, failed: 0, skipped: 0, other: 0 };
  const identities = [];
  const owners = new Map();
  for (const payload of shards) {
    const index = payload.shard.index;
    if (payload.exitCode !== 0) errors.push(`shard ${index}: test run exited ${payload.exitCode}`);
    for (const result of payload.results ?? []) {
      if (result.exitCode !== 0 || result.error || !result.evidence) {
        errors.push(`shard ${index}: a batch failed or lacks evidence (${result.error ?? `exit ${result.exitCode}`})`);
        continue;
      }
      for (const file of result.files ?? []) {
        if (owners.has(file)) errors.push(`test file '${file}' ran more than once`);
        owners.set(file, index);
      }
      for (const key of Object.keys(counts)) counts[key] += result.evidence.counts[key] ?? 0;
      identities.push(...result.evidence.identities);
    }
  }
  const expected = new Set(expectedFiles);
  for (const file of expectedFiles) {
    if (!owners.has(file)) errors.push(`test file '${file}' did not run in any shard`);
  }
  for (const file of owners.keys()) {
    if (!expected.has(file)) errors.push(`'${file}' is not a dashboard test file in this checkout`);
  }
  if (counts.failed || counts.other) errors.push(`shards record ${counts.failed} failed and ${counts.other} unclassified tests`);

  const seconds = shards.map((payload) => payload.testSeconds ?? 0);
  return {
    counts,
    testIdentityDigest: identities.length ? identityDigest(identities) : null,
    testFiles: owners.size,
    expectedTestFiles: expectedFiles.length,
    // Shards overlap, so their sum is runner time and the longest is the lane's test time.
    testSeconds: seconds.length ? Math.max(...seconds) : null,
    testRunnerSeconds: seconds.reduce((total, value) => total + value, 0),
    commitSha: commits.size === 1 ? [...commits][0] : null,
    shards: shards.map((payload) => ({
      index: payload.shard.index, runId: payload.runId ?? null, runAttempt: payload.runAttempt ?? null,
      cacheHit: payload.cacheHit ?? null, testSeconds: payload.testSeconds ?? null,
      batches: (payload.results ?? []).length,
      testFiles: (payload.results ?? []).reduce((total, result) => total + (result.files ?? []).length, 0),
    })),
    accepted: errors.length === 0,
    errors,
  };
}

export function render(combined, expectedShards) {
  const lines = [
    "### Browser combined shard summary", "",
    `- Shards reporting: ${combined.shards.length} of ${expectedShards}`,
    `- Test files: ${combined.testFiles} of ${combined.expectedTestFiles}`,
    `- Tests: ${JSON.stringify(combined.counts)}`,
    `- Verdict: ${combined.accepted ? "accepted" : "rejected"}`, "",
  ];
  if (combined.errors.length) {
    // A wholly missing shard would otherwise list every one of its files.
    const shown = combined.errors.slice(0, 25);
    lines.push("#### Rejected because", "", ...shown.map((error) => `- ${error}`));
    if (combined.errors.length > shown.length) lines.push(`- ...and ${combined.errors.length - shown.length} more`);
    lines.push("");
  }
  lines.push("| Shard | Run attempt | Test seconds | Batches | Test files |", "| ---: | --- | ---: | ---: | ---: |",
    ...combined.shards.map((shard) =>
      `| ${shard.index} | ${shard.runAttempt ?? "local"} | ${(shard.testSeconds ?? 0).toFixed(1)} | ${shard.batches} | ${shard.testFiles} |`),
    "", "Reproduce: `bash scripts/ci.sh --lane verify-browser`", "");
  return lines.join("\n");
}

function parseArgs(argv) {
  const options = {};
  for (let index = 0; index < argv.length; index += 2) {
    const name = argv[index];
    const value = argv[index + 1];
    if (!["--shards-dir", "--expected-shards", "--output-dir"].includes(name) || value === undefined) {
      throw new Error("Usage: merge-vitest-shards.mjs --shards-dir <dir> --expected-shards <n> --output-dir <dir>");
    }
    options[name.slice(2)] = value;
  }
  const expectedShards = Number.parseInt(options["expected-shards"] ?? "", 10);
  if (!options["shards-dir"] || !options["output-dir"] || !(expectedShards >= 1)) {
    throw new Error("Usage: merge-vitest-shards.mjs --shards-dir <dir> --expected-shards <n> --output-dir <dir>");
  }
  return { shardsDir: options["shards-dir"], outputDir: options["output-dir"], expectedShards };
}

async function main() {
  const { shardsDir, outputDir, expectedShards } = parseArgs(process.argv.slice(2));
  const dashboardRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
  const expectedFiles = (await findTestFiles(path.join(dashboardRoot, "src")))
    .map((file) => toPosixPath(path.relative(dashboardRoot, file)));

  const summaries = [];
  const unreadable = [];
  for (const source of await findSummaries(path.resolve(shardsDir))) {
    try {
      summaries.push({ source, payload: JSON.parse(await readFile(source, "utf8")) });
    } catch (error) {
      unreadable.push(`${source}: unreadable summary (${error.message})`);
    }
  }
  const combined = merge(summaries, expectedShards, expectedFiles);
  combined.errors.unshift(...unreadable);
  combined.accepted = combined.errors.length === 0;

  const markdown = render(combined, expectedShards);
  await mkdir(outputDir, { recursive: true });
  await writeFile(path.join(outputDir, "summary.json"), JSON.stringify(combined, null, 2) + "\n");
  await writeFile(path.join(outputDir, "summary.md"), markdown);
  if (process.env.GITHUB_STEP_SUMMARY) await appendFile(process.env.GITHUB_STEP_SUMMARY, markdown);

  if (!combined.accepted) {
    console.error("Combined browser shard evidence was rejected:");
    for (const error of combined.errors.slice(0, 50)) console.error(`- ${error}`);
    return 1;
  }
  console.log(`All ${combined.testFiles} dashboard test files passed across ${expectedShards} shards.`);
  return 0;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(await main());
}
