import type { PrivateCapitalRecordCommitment, PrivateCapitalSnapshot } from "@/types/private-capital.types";

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
