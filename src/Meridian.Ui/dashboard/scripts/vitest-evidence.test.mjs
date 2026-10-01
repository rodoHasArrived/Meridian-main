import assert from "node:assert/strict";
import test from "node:test";
import { readEvidence, identityDigest } from "./vitest-evidence.mjs";

const report = { success: true, numTotalTests: 2, testResults: [{ name: "/repo/src/a.test.ts", assertionResults: [
  { fullName: "suite passes", status: "passed" }, { fullName: "suite skipped", status: "pending" }
] }] };
test("counts and identities are stable across batch ordering", () => {
  const result = readEvidence(report, "/repo", ["src/a.test.ts"]);
  assert.deepEqual(result.counts, { passed: 1, failed: 0, skipped: 1, other: 0 });
  assert.equal(identityDigest(result.identities), identityDigest(result.identities.toReversed()));
});
test("missing files, missing discovery, inconsistent totals and reported failures fail closed", () => {
  for (const invalid of [{}, { ...report, numTotalTests: 0 }, { ...report, success: false }, { ...report, testResults: [] }]) {
    assert.throws(() => readEvidence(invalid, "/repo"));
  }
  assert.throws(() => readEvidence(report, "/repo", ["src/missing.test.ts"]));
});
