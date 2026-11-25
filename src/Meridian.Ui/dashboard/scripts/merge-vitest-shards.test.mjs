import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { merge, render } from "./merge-vitest-shards.mjs";
import { applyShard, findTestFiles, toPosixPath } from "./vitest-files.mjs";

const scripts = path.dirname(fileURLToPath(import.meta.url));
const dashboard = path.resolve(scripts, "..");

function shard(index, files, overrides = {}) {
  return { source: `shard-${index}`, payload: {
    shard: { index, total: 2 }, exitCode: 0, commitSha: "abc", runAttempt: "1", testSeconds: 10 * index,
    results: [{ files, exitCode: 0, evidence: {
      counts: { passed: files.length, failed: 0, skipped: 0, other: 0 },
      identities: files.map((file) => `${file}|passes`) } }],
    ...overrides } };
}

test("every shard count deals each file to exactly one shard", () => {
  const files = Array.from({ length: 23 }, (_, index) => `f${index}`);
  for (let total = 1; total <= 6; total += 1) {
    const dealt = [];
    for (let index = 1; index <= total; index += 1) dealt.push(...applyShard(files, index, total));
    assert.deepEqual(dealt.toSorted(), files.toSorted());
  }
});

test("complete disjoint shards are accepted and the digest ignores how files were dealt", () => {
  const expected = ["src/a.test.ts", "src/b.test.tsx", "src/c.test.ts"];
  const one = merge([shard(2, [expected[1]]), shard(1, [expected[0], expected[2]])], 2, expected);
  assert.deepEqual(one.errors, []);
  assert.equal(one.accepted, true);
  assert.deepEqual(one.counts, { passed: 3, failed: 0, skipped: 0, other: 0 });
  assert.equal(one.testFiles, 3);
  assert.equal(one.testSeconds, 20);
  assert.equal(one.testRunnerSeconds, 30);
  assert.deepEqual(one.shards.map((entry) => entry.index), [1, 2]);
  const other = merge([shard(1, [expected[1]]), shard(2, [expected[0], expected[2]])], 2, expected);
  assert.equal(one.testIdentityDigest, other.testIdentityDigest);
  assert.match(render(one, 2), /Verdict: accepted/);
});

test("missing, duplicated, failed, foreign and mixed evidence is rejected", () => {
  const expected = ["src/a.test.ts", "src/b.test.ts"];
  const good = [shard(1, [expected[0]]), shard(2, [expected[1]])];
  const failedBatch = shard(2, [expected[1]]);
  failedBatch.payload.results[0].exitCode = 1;
  const noEvidence = shard(2, [expected[1]]);
  delete noEvidence.payload.results[0].evidence;
  const unsharded = shard(2, [expected[1]], { shard: null });
  const cases = {
    "no summary was produced": [good[0]],
    "did not run in any shard": [good[0], shard(2, [])],
    "ran more than once": [good[0], shard(2, [expected[1], expected[0]])],
    "is not a dashboard test file": [good[0], shard(2, [expected[1], "src/stray.test.ts"])],
    "different commits": [good[0], shard(2, [expected[1]], { commitSha: "other" })],
    "duplicate summary for shard 1": [good[0], shard(1, [expected[1]])],
    "test run exited 1": [good[0], shard(2, [expected[1]], { exitCode: 1 })],
    "a batch failed or lacks evidence": [good[0], failedBatch],
    "lacks evidence": [good[0], noEvidence],
    "not one of the 2 expected shards": [good[0], unsharded],
  };
  for (const [reason, summaries] of Object.entries(cases)) {
    const combined = merge(summaries, 2, expected);
    assert.equal(combined.accepted, false, reason);
    assert.ok(combined.errors.some((error) => error.includes(reason)), `${reason}: ${combined.errors}`);
    assert.match(render(combined, 2), /Verdict: rejected/);
  }
});

test("the command fails closed without evidence and accepts the real file roster", async () => {
  const root = mkdtempSync(path.join(os.tmpdir(), "merge-vitest-shards-"));
  try {
    const script = path.join(scripts, "merge-vitest-shards.mjs");
    const empty = spawnSync(process.execPath, [script, "--shards-dir", path.join(root, "absent"),
      "--expected-shards", "2", "--output-dir", path.join(root, "rejected")], { encoding: "utf8", env: { ...process.env, GITHUB_STEP_SUMMARY: "" } });
    assert.equal(empty.status, 1);
    assert.match(empty.stderr, /no summary was produced/);
    assert.equal(JSON.parse(readFileSync(path.join(root, "rejected", "summary.json"), "utf8")).accepted, false);

    const files = (await findTestFiles(path.join(dashboard, "src")))
      .map((file) => toPosixPath(path.relative(dashboard, file)));
    assert.ok(files.length > 0);
    for (const index of [1, 2]) {
      const directory = path.join(root, "shards", `artifact-${index}`, "artifacts", "test-results", "browser");
      mkdirSync(directory, { recursive: true });
      writeFileSync(path.join(directory, "summary.json"),
        JSON.stringify(shard(index, applyShard(files, index, 2)).payload));
    }
    const output = execFileSync(process.execPath, [script, "--shards-dir", path.join(root, "shards"),
      "--expected-shards", "2", "--output-dir", path.join(root, "accepted")], { encoding: "utf8", env: { ...process.env, GITHUB_STEP_SUMMARY: "" } });
    assert.match(output, new RegExp(`All ${files.length} dashboard test files passed across 2 shards`));
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
