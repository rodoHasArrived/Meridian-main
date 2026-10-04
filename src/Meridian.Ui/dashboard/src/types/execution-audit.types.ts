/**
 * Cross-object audit trail types.
 *
 * Mirrors `Meridian.Contracts.Workstation.AuditTrailExplorerDtos`, the contract
 * behind `GET /api/execution/audit/search`. Object kinds are declared with a
 * `JsonStringEnumConverter`, so they arrive as names rather than ordinals.
 */

export interface AuditTrailTimelineEntry {
  auditId: string;
  occurredAt: string;
  objectKind: string;
  objectId: string;
  category: string;
  action: string;
  outcome: string;
  actor?: string | null;
  runId?: string | null;
  symbol?: string | null;
  correlationId?: string | null;
  reason?: string | null;
  scope?: string | null;
  message?: string | null;
  metadata?: Record<string, string> | null;
  relatedObjectIds?: string[] | null;
  evidenceRoute?: string | null;
  /** Hash-chain fields; a gap or mismatch is what makes the trail auditable. */
  actionLedgerSource?: string | null;
  actionLedgerSequence?: number | null;
  previousActionHash?: string | null;
  currentActionHash?: string | null;
  actionLedgerStatus?: string | null;
}

export interface AuditTrailExplorerResult {
  asOf: string;
  /** Matches before the limit is applied, so truncation is visible to the operator. */
  totalMatched: number;
  returned: number;
  entries: AuditTrailTimelineEntry[];
}

/** Query parameters accepted by `GET /api/execution/audit/search`. */
export interface AuditTrailSearchQuery {
  searchText?: string;
  runId?: string;
  category?: string;
  action?: string;
  outcome?: string;
  actor?: string;
  symbol?: string;
  correlationId?: string;
  objectKind?: string;
  objectId?: string;
  relatedObjectId?: string;
  fromUtc?: string;
  toUtc?: string;
  limit?: number;
}
