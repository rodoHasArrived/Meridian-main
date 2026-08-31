/**
 * Statement reconciliation run detail types.
 *
 * Mirrors `Meridian.Contracts.Workstation.StatementReconciliationDtos`, the
 * contracts behind the three per-run routes:
 *
 * - `GET  /api/workstation/reconciliation/statement-runs/{runId}/validation`
 * - `GET  /api/workstation/reconciliation/statement-runs/{runId}/breaks`
 * - `POST /api/workstation/reconciliation/statement-runs/{runId}/reconcile`
 *
 * The workstation endpoint group builds its `JsonSerializerOptions` in
 * `UiEndpoints.CreateEndpointJsonOptions` with no `JsonStringEnumConverter`, so
 * every enum on these contracts crosses the wire as its **ordinal**, not its
 * name. The ordinal maps below are transcribed from the `byte` enums in
 * `StatementReconciliationDtos.cs`; an ordinal outside a map is reported as
 * unrecognized rather than guessed.
 */

/** `StatementValidationSeverity` ordinals. */
export const STATEMENT_VALIDATION_SEVERITY_LABELS: Readonly<Record<number, string>> = Object.freeze({
  0: "Info",
  1: "Warning",
  2: "Error",
  3: "Critical"
});

/** `StatementBreakType` ordinals. */
export const STATEMENT_BREAK_TYPE_LABELS: Readonly<Record<number, string>> = Object.freeze({
  0: "Unknown",
  1: "Missing statement position",
  2: "Missing book position",
  3: "Position quantity mismatch",
  4: "Position market value mismatch",
  5: "Missing statement cash",
  6: "Missing book cash",
  7: "Cash balance mismatch",
  8: "Missing statement transaction",
  9: "Missing book transaction",
  10: "Transaction amount mismatch",
  11: "Security identifier mismatch",
  12: "Timing mismatch",
  13: "Classification mismatch",
  14: "Duplicate statement item",
  15: "Validation failure"
});

/**
 * Severity ordinals that block a run from progressing, mirroring the server's
 * own `IsBlocked` computation rather than re-deriving it in the browser.
 */
export const BLOCKING_STATEMENT_VALIDATION_SEVERITIES: readonly number[] = Object.freeze([2, 3]);

/** `StatementValidationIssueDto`. */
export interface StatementValidationIssue {
  issueId?: string | null;
  /** `StatementValidationSeverity` ordinal. */
  severity?: number | null;
  code?: string | null;
  message?: string | null;
  sourceRowNumber?: number | null;
  sourceColumn?: string | null;
  rawValue?: string | null;
  recommendedAction?: string | null;
  evidenceLink?: string | null;
}

/** `StatementRunValidationDto`. */
export interface StatementRunValidation {
  runId: string;
  issues: StatementValidationIssue[];
  /** Server-owned decision; the browser reports it, it does not recompute it. */
  isBlocked: boolean;
}

/** `StatementRunBreakDto`. */
export interface StatementRunBreak {
  breakId: string;
  runId: string;
  importId: string;
  sourceReference: string;
  /** `StatementBreakType` ordinal. */
  breakType: number;
  category: string;
  delta: number;
  tolerance: number;
  toleranceBreached: boolean;
  createdAtUtc: string;
  status: string;
}

/**
 * `StatementRunReconcileRequestDto`. `Actor` is overwritten server-side with the
 * authenticated user, so the browser never sends it.
 */
export interface StatementRunReconcileRequest {
  reason?: string | null;
  force?: boolean;
}

/** `StatementRunStatus` ordinals. */
export const STATEMENT_RUN_STATUS_LABELS: Readonly<Record<number, string>> = Object.freeze({
  0: "Unknown",
  1: "Pending validation",
  2: "Validating",
  3: "Validation failed",
  4: "Importing",
  5: "Reconciling",
  6: "Review required",
  7: "Completed",
  8: "Failed",
  9: "Canceled"
});

/**
 * The reconcile route returns the whole `StatementRunDto` — every normalized
 * position, cash balance, and transaction on the run. The panel reads only the
 * acknowledgement fields below and refetches validation and breaks afterwards,
 * so the rest is deliberately left untyped rather than mirrored and unused.
 */
export interface StatementRunReconcileAcknowledgement {
  runId?: string | null;
  /** `StatementRunStatus` ordinal. */
  status?: number | null;
  completedAtUtc?: string | null;
}
