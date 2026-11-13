import type { CapitalCommitment } from "@/types/family-office.types";

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
