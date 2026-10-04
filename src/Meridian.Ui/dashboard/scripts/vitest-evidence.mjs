import { createHash } from "node:crypto";
import path from "node:path";

export function readEvidence(report, root, expectedFiles = []) {
  const counts = { passed: 0, failed: 0, skipped: 0, other: 0 };
  const identities = [];
  const files = new Set();
  for (const suite of report.testResults ?? []) {
    const file = path.relative(root, suite.name).replaceAll(path.sep, "/");
    files.add(file);
    for (const test of suite.assertionResults ?? []) {
      const outcome = { passed: "passed", failed: "failed", pending: "skipped", skipped: "skipped", todo: "skipped" }[test.status] ?? "other";
      counts[outcome] += 1;
      identities.push(`${file}|${test.fullName}`);
    }
  }
  if (!counts.passed || identities.length !== report.numTotalTests) {
    throw new Error("Required browser batch has zero passing tests or incomplete result evidence.");
  }
  for (const file of expectedFiles) {
    if (!files.has(file)) throw new Error(`Missing expected browser result: ${file}`);
  }
  if (report.success !== true || counts.failed || counts.other) throw new Error("Browser report records a failure.");
  return { counts, identities: identities.sort() };
}

export function identityDigest(identities) {
  return createHash("sha256").update(JSON.stringify([...identities].sort())).digest("hex");
}
