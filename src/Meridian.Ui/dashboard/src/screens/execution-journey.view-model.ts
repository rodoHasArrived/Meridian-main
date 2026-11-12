import type { AuditTrailExplorerResult, AuditTrailTimelineEntry } from "@/types/execution-audit.types";
import type { TradingFill } from "@/types";

export interface ExecutionJourneyEvent {
  id: string;
  timestamp: string;
  timestampKind: "Recorded" | "Occurred";
  kind: string;
  label: string;
  audit?: AuditTrailTimelineEntry;
  fill?: TradingFill;
}

/** Only explicit primary/related identities join an event to an order. */
export function buildExecutionJourney(orderId: string, results: AuditTrailExplorerResult[], fills: TradingFill[]) {
  const audits = new Map<string, AuditTrailTimelineEntry>();
  const matchesOrder = (id: string) => id.toLowerCase() === orderId.toLowerCase();
  for (const result of results) {
    for (const entry of result.entries) {
      if (matchesOrder(entry.objectId) || entry.relatedObjectIds?.some(matchesOrder)) audits.set(entry.auditId, entry);
    }
  }
  const events: ExecutionJourneyEvent[] = [...audits.values()].map((audit) => ({
    id: `audit:${audit.auditId}`,
    timestamp: audit.occurredAt,
    timestampKind: "Recorded",
    kind: /control|risk/i.test(audit.category) ? "Control evidence" : /submit/i.test(audit.action) ? "Submission" : "Recorded outcome / action",
    label: `${audit.action} · ${audit.outcome}`,
    audit
  }));
  const retainedFills = new Map(fills.filter((fill) => fill.orderId === orderId).map((fill) => [fill.fillId, fill]));
  for (const fill of retainedFills.values()) events.push({
    id: `fill:${fill.fillId}`, timestamp: fill.timestamp, timestampKind: "Occurred", kind: "Fill",
    label: `${fill.side} ${fill.quantity} ${fill.symbol} @ ${fill.price}`, fill
  });
  events.sort((a, b) => {
    const aTime = Date.parse(a.timestamp);
    const bTime = Date.parse(b.timestamp);
    return (Number.isFinite(aTime) && Number.isFinite(bTime) ? aTime - bTime : a.timestamp.localeCompare(b.timestamp)) || a.id.localeCompare(b.id);
  });
  return {
    events,
    truncated: results.some((result) => result.totalMatched > result.returned),
    sourceUnavailable: results.some((result) => result.sourceAvailability?.executionAudit === false),
    sourceUnknown: results.some((result) => result.sourceAvailability?.executionAudit == null),
    hasControlEvidence: events.some((event) => event.kind === "Control evidence" || event.audit?.metadata?.controlDecision),
    asOf: results.map((result) => result.asOf).sort()[0] ?? null
  };
}
