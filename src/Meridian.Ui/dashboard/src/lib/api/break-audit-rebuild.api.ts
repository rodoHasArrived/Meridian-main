/**
 * Client function for the break audit rebuild route.
 *
 * Thin wrapper over the shared workstation API client so errors, aborts, and
 * no-host fixture semantics stay aligned with the rest of the dashboard.
 */

import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import { reconciliationBreakRebuiltSnapshotEndpoint } from "@/lib/workstation-endpoints";
import type { ReconciliationBreakQueueItem } from "@/types";

/** Replays the break's audit trail and returns the state that trail implies. */
export function getReconciliationBreakRebuiltSnapshot(
  breakId: string,
  options: ApiRequestOptions = {}
): Promise<ReconciliationBreakQueueItem> {
  return apiGetJson<ReconciliationBreakQueueItem>(
    reconciliationBreakRebuiltSnapshotEndpoint(breakId),
    options
  );
}
