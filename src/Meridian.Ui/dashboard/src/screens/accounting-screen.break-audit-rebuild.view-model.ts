/**
 * Comparison logic for the break audit rebuild check.
 *
 * `GET /break-queue/{breakId}/rebuilt-snapshot` replays a break's audit trail
 * and returns the state that trail implies. On its own that is just a second
 * copy of the break; its value is in the comparison. If the stored break and
 * the audit-derived one disagree, either the trail is incomplete or the stored
 * record was changed outside it — and both are findings a reconciliation
 * operator needs before signing anything off.
 *
 * The comparison is mechanical: every scalar field the two records share is
 * compared, so a contract that gains a field is covered without another edit.
 * A field the rebuilt snapshot does not carry is reported as not reconstructible
 * from the audit trail, which is a different statement from "they disagree".
 */

import type { ReconciliationBreakQueueItem } from "@/types";

export interface BreakFieldDifference {
  field: string;
  storedValue: string;
  rebuiltValue: string;
}

export interface BreakAuditRebuildViewModel {
  compared: boolean;
  matches: boolean;
  verdict: string;
  differences: BreakFieldDifference[];
  /** Fields present on the stored break that the replayed trail never sets. */
  notReconstructed: string[];
  notReconstructedNotice: string | null;
  comparedFieldCount: number;
}

/**
 * Collections are excluded: comments and evidence links are append-only side
 * records with their own routes, and comparing them here would report ordering
 * noise as a discrepancy.
 */
type ComparableScalar = string | number | boolean | null;

function isComparableScalar(value: unknown): value is ComparableScalar {
  return value === null
    || typeof value === "string"
    || typeof value === "number"
    || typeof value === "boolean";
}

/** Reads the record as a bag of fields so the comparison stays contract-agnostic. */
function fields(item: ReconciliationBreakQueueItem): Record<string, unknown> {
  return item as unknown as Record<string, unknown>;
}

export function buildBreakAuditRebuildViewModel(
  stored: ReconciliationBreakQueueItem | null,
  rebuilt: ReconciliationBreakQueueItem | null
): BreakAuditRebuildViewModel {
  if (!stored || !rebuilt) {
    return {
      compared: false,
      matches: false,
      verdict: "Not compared. Rebuild the break from its audit trail to check it.",
      differences: [],
      notReconstructed: [],
      notReconstructedNotice: null,
      comparedFieldCount: 0
    };
  }

  const differences: BreakFieldDifference[] = [];
  const notReconstructed: string[] = [];
  let comparedFieldCount = 0;

  for (const field of Object.keys(stored).sort()) {
    const storedValue = fields(stored)[field];
    if (!isComparableScalar(storedValue)) {
      continue;
    }

    if (!(field in rebuilt)) {
      notReconstructed.push(field);
      continue;
    }

    const rebuiltValue = fields(rebuilt)[field];
    if (!isComparableScalar(rebuiltValue)) {
      notReconstructed.push(field);
      continue;
    }

    comparedFieldCount += 1;
    if (storedValue !== rebuiltValue) {
      differences.push({
        field,
        storedValue: describeValue(storedValue),
        rebuiltValue: describeValue(rebuiltValue)
      });
    }
  }

  const matches = differences.length === 0;
  return {
    compared: true,
    matches,
    verdict: matches
      ? `Stored break agrees with its audit trail across ${comparedFieldCount} compared field${comparedFieldCount === 1 ? "" : "s"}.`
      : `Stored break differs from its audit trail in ${differences.length} of ${comparedFieldCount} compared field${comparedFieldCount === 1 ? "" : "s"}.`,
    differences,
    notReconstructed,
    notReconstructedNotice: notReconstructed.length > 0
      ? `${notReconstructed.length} field${notReconstructed.length === 1 ? "" : "s"} could not be reconstructed from the audit trail `
        + `and ${notReconstructed.length === 1 ? "was" : "were"} not compared: ${notReconstructed.join(", ")}.`
      : null,
    comparedFieldCount
  };
}

function describeValue(value: ComparableScalar): string {
  if (value === null) {
    return "null";
  }

  return typeof value === "string" && value.trim() === "" ? '""' : String(value);
}
