import type { ReconciliationBreakQueueItem } from "@/types";

export function areReconciliationBreakQueuesEquivalent(
  current: ReconciliationBreakQueueItem[],
  next: ReconciliationBreakQueueItem[]
): boolean {
  if (current === next) {
    return true;
  }

  if (current.length !== next.length) {
    return false;
  }

  for (let index = 0; index < current.length; index += 1) {
    const left = current[index];
    const right = next[index];

    if (
      left.breakId !== right.breakId ||
      left.runId !== right.runId ||
      left.strategyName !== right.strategyName ||
      left.category !== right.category ||
      left.status !== right.status ||
      left.variance !== right.variance ||
      left.reason !== right.reason ||
      left.assignedTo !== right.assignedTo ||
      left.detectedAt !== right.detectedAt ||
      left.lastUpdatedAt !== right.lastUpdatedAt ||
      left.reviewedBy !== right.reviewedBy ||
      left.reviewedAt !== right.reviewedAt ||
      left.resolvedBy !== right.resolvedBy ||
      left.resolvedAt !== right.resolvedAt ||
      left.resolutionNote !== right.resolutionNote ||
      left.routingTarget !== right.routingTarget ||
      left.routingDetail !== right.routingDetail ||
      left.recommendedAction !== right.recommendedAction
    ) {
      return false;
    }
  }

  return true;
}

export function replaceBreakQueueItem(
  current: ReconciliationBreakQueueItem[],
  updated: ReconciliationBreakQueueItem
): ReconciliationBreakQueueItem[] {
  if (!current.some((item) => item.breakId === updated.breakId)) {
    return [updated, ...current];
  }

  return current.map((item) => (item.breakId === updated.breakId ? updated : item));
}
