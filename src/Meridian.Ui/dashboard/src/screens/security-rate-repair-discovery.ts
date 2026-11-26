import type { RateRepairCandidate, RateRepairDiscovery, RateRepairDiscoveryIssue } from "@/lib/api/security-rate-repair.api";

const authority = (row: RateRepairCandidate) => JSON.stringify([row.version, row.assetClass, row.sourceSystem,
  row.sourceField, row.sourceSchemaVersion, row.rateRole, row.rawValue, row.currentUnit, [...row.allowedUnits].sort()]);
const issueKey = (issue: RateRepairDiscoveryIssue) => JSON.stringify([issue.securityId, issue.code, issue.message]);

/** Overlapping live pages never silently replace an already observed interpretation. */
export function mergeRateRepairPage(previous: RateRepairDiscovery, page: RateRepairDiscovery): {
  discovery: RateRepairDiscovery; changedRecordIds: string[];
} {
  if (previous.nextSkip !== page.skip || previous.pageSize !== page.pageSize || previous.scopeLabel !== page.scopeLabel
    || !Number.isSafeInteger(page.scannedCount) || page.scannedCount < 0 || page.scannedCount > page.pageSize
    || (page.nextSkip !== null && (!Number.isSafeInteger(page.nextSkip) || page.nextSkip <= page.skip)))
    throw new Error("The discovery page no longer matches this source scan. Refresh candidates before continuing.");
  const rows = new Map(previous.candidates.map(row => [row.securityId.toLowerCase(), row]));
  const issues = new Map([...previous.issues, ...page.issues].map(issue => [issueKey(issue), issue]));
  const changed = new Set<string>();
  for (const row of page.candidates) {
    const key = row.securityId.toLowerCase();
    const observed = rows.get(key);
    if (!observed) rows.set(key, row);
    else if (authority(observed) !== authority(row)) {
      changed.add(row.securityId);
      const message = "This security changed between discovery pages. Refresh the source scan and reselect its current version.";
      rows.set(key, { ...observed, eligible: false, blockerCode: "DiscoveryChanged", blockerReason: message });
      const issue = { securityId: row.securityId, code: "DiscoveryChanged", message };
      issues.set(issueKey(issue), issue);
    }
  }
  return { discovery: { ...previous, candidates: [...rows.values()], actions: page.actions,
    scannedCount: previous.scannedCount + page.scannedCount, nextSkip: page.nextSkip,
    isComplete: page.isComplete && issues.size === 0, issues: [...issues.values()] },
  changedRecordIds: [...changed] };
}
