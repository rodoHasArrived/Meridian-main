import type { AuditTrailExplorerResult, AuditTrailTimelineEntry } from "@/types/execution-audit.types";
import { readFixtureSearchParams } from "./fixture-resolver";

const common = { objectKind: "Order", objectId: "PO-1", orderId: "PO-1", symbol: "AAPL",
  actor: "synthetic-operator", correlationId: "synthetic-order-correlation", runId: "synthetic-paper-run",
  scope: "Synthetic paper evidence", actionLedgerSource: "SyntheticOrderAudit", actionLedgerStatus: "NotVerified" };
const events: AuditTrailTimelineEntry[] = [
  { ...common, auditId: "synthetic-check-1", occurredAt: "2026-05-29T11:00:00Z", category: "Control",
    action: "ControlEvaluated", outcome: "Passed", reason: "Synthetic retained control decision.",
    metadata: { timestampBasis: "audit-recording-time", policyVersion: "synthetic-policy-v1" } },
  { ...common, auditId: "synthetic-submit-1", occurredAt: "2026-05-29T11:00:02Z", category: "Order",
    action: "OrderSubmitted", outcome: "Accepted", metadata: { timestampBasis: "audit-recording-time" } },
  { ...common, auditId: "synthetic-fill-1", occurredAt: "2026-05-29T11:00:05Z", category: "Fill",
    action: "PartialFill", outcome: "PartiallyFilled", actor: "synthetic-paper-broker",
    metadata: { timestampBasis: "execution-report-event", filledQuantity: "40", fillPrice: "188.07",
      quantityBasis: "PerExecution", fillId: "synthetic-fill-report-1" } },
  { ...common, auditId: "synthetic-operator-1", occurredAt: "2026-05-29T11:02:00Z", category: "Operator",
    action: "OperatorReview", outcome: "Recorded", reason: "Synthetic operator reviewed the remaining order.",
    metadata: { timestampBasis: "audit-recording-time" } }
];

export function resolveAnalyticalExecutionAudit(path: string): AuditTrailExplorerResult {
  const params = readFixtureSearchParams(path);
  const orderId = params.get("orderId");
  const search = params.get("searchText")?.toLowerCase();
  const from = params.get("fromUtc");
  const to = params.get("toUtc");
  const matching = events.filter((entry) => (!orderId || entry.orderId === orderId)
    && (!search || JSON.stringify(entry).toLowerCase().includes(search))
    && (!from || Date.parse(entry.occurredAt) >= Date.parse(from))
    && (!to || Date.parse(entry.occurredAt) <= Date.parse(to))
    && ["category", "action", "outcome", "actor", "symbol", "correlationId", "objectKind", "objectId", "runId"]
      .every((key) => !params.has(key) || entry[key as keyof AuditTrailTimelineEntry] === params.get(key)));
  const requestedLimit = Number(params.get("limit") ?? 50);
  const limit = Number.isFinite(requestedLimit) ? Math.max(1, Math.min(1000, requestedLimit)) : 50;
  return { asOf: "2026-05-29T12:00:00Z", totalMatched: matching.length, returned: Math.min(limit, matching.length),
    entries: [...matching].reverse().slice(0, limit),
    sources: [{ source: "SyntheticOrderAudit", state: "Available" }, { source: "SyntheticFillReports", state: "Available" }],
    coverageNote: "Explicit synthetic evidence. The fixture demonstrates retained events and does not establish complete control or broker history." };
}
