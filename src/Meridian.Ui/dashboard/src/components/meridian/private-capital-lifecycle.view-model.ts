import type { PrivateCapitalRecordCommitment, PrivateCapitalSnapshot } from "@/types/private-capital.types";
import type { CapitalCommitment } from "@/types/family-office.types";

export const capitalMeasures = [
  ["commitmentAmount", "Commitment"], ["calledAmount", "Called capital"],
  ["unfundedAmount", "Unfunded commitment"], ["cumulativeDistributions", "Distributions"], ["navAmount", "NAV"]
] as const;

export function buildCapitalRows(commitments: PrivateCapitalRecordCommitment[], snapshots: PrivateCapitalSnapshot[],
  group: "vintage" | "strategy", asOfDate?: string) {
  const superseded = new Set(snapshots.flatMap(row => row.supersedesSnapshotId ? [row.supersedesSnapshotId] : []));
  const eligible = snapshots.filter(row => !superseded.has(row.snapshotId) && (!asOfDate || row.asOfDate <= asOfDate));
  return commitments.map(commitment => {
    const current = eligible.filter(row => row.commitmentId === commitment.commitmentId && !superseded.has(row.snapshotId))
      .sort((a, b) => b.asOfDate.localeCompare(a.asOfDate) || b.recordedAt.localeCompare(a.recordedAt));
    const sameDate = current.filter(row => row.asOfDate === current[0]?.asOfDate);
    return { commitment, snapshot: sameDate.length === 1 ? sameDate[0] : null,
      conflicting: sameDate.length > 1,
      group: group === "vintage" ? String(commitment.vintageYear ?? "Vintage unavailable") : commitment.strategy || "Strategy unavailable" };
  }).sort((a, b) => a.commitment.currency.localeCompare(b.commitment.currency) || a.group.localeCompare(b.group) ||
    a.commitment.vehicleName.localeCompare(b.commitment.vehicleName) || a.commitment.commitmentId.localeCompare(b.commitment.commitmentId));
}

/** Approximate geometry only. Exact source text remains the accessible and displayed value. */
export function capitalBarWidth(value: string | null | undefined, values: (string | null | undefined)[]): number {
  const number = Number(value);
  const max = Math.max(1, ...values.map(item => item == null ? 0 : Math.abs(Number(item))).filter(Number.isFinite));
  return value == null || !Number.isFinite(number) ? 0 : Math.min(100, Math.abs(number) / max * 100);
}


export type PrivateCapitalMetric = "commitmentAmount" | "calledAmount" | "unfundedAmount" | "distributedAmount" | "currentNav";
export const PRIVATE_CAPITAL_METRICS: Array<{ key: PrivateCapitalMetric; label: string }> = [
  { key: "commitmentAmount", label: "Commitment" },
  { key: "calledAmount", label: "Called capital" },
  { key: "unfundedAmount", label: "Unfunded" },
  { key: "distributedAmount", label: "Distributions" },
  { key: "currentNav", label: "NAV" }
];

export function hasPrivateCapitalBalances(row: CapitalCommitment): boolean {
  return row.balancesAvailable !== false && row.evidenceCompleteness !== "Missing"
    && PRIVATE_CAPITAL_METRICS.every(({ key }) => typeof row[key] === "number" && Number.isFinite(row[key]) && (key === "currentNav" || row[key] >= 0));
}

export function privateCapitalRowKey(row: CapitalCommitment): string {
  return JSON.stringify([row.commitmentId, row.entityId, row.currency]);
}

/** Independent balances: no additive stack or derived unfunded balance. */
export function groupPrivateCapital(rows: CapitalCommitment[], grouping: "vintage" | "strategy", currency: string) {
  const filtered = rows.filter((row) => row.currency === currency);
  const groups = new Map<string, CapitalCommitment[]>();
  let maximum = 0;
  let hasNegative = false;
  for (const row of filtered) {
    const label = grouping === "vintage" ? (row.vintageYear == null ? "Vintage unavailable" : `Vintage ${row.vintageYear}`)
      : row.strategy || "Strategy unavailable";
    const items = groups.get(label) ?? [];
    items.push(row);
    groups.set(label, items);
    if (hasPrivateCapitalBalances(row)) {
      for (const { key } of PRIVATE_CAPITAL_METRICS) {
        maximum = Math.max(maximum, Math.abs(row[key]));
        hasNegative ||= row[key] < 0;
      }
    }
  }
  return {
    maximum,
    hasNegative,
    groups: [...groups].sort(([left], [right]) => left.localeCompare(right)).map(([label, items]) => ({
      label,
      rows: [...items].sort((left, right) => left.vehicleName.localeCompare(right.vehicleName) || privateCapitalRowKey(left).localeCompare(privateCapitalRowKey(right)))
    }))
  };
}
