import { formatTimestamp } from "@/lib/format";
import type { ExecutionAuditEntry } from "@/types";

export type ExecutionEvidenceTone = "success" | "warning" | "danger";

export interface ExecutionAuditRow {
  id: string;
  action: string;
  outcome: string;
  outcomeTone: ExecutionEvidenceTone;
  message: string;
  metadataText: string;
  technicalMetadataText: string | null;
  ariaLabel: string;
}

export function buildExecutionAuditRow(entry: ExecutionAuditEntry): ExecutionAuditRow {
  const metadataText = formatExecutionAuditMetadata(entry);
  const technicalMetadataText = formatExecutionAuditTechnicalMetadata(entry);
  const message = entry.message?.trim() || "No operator message recorded.";
  const action = formatExecutionAuditAction(entry.action);

  return {
    id: entry.auditId,
    action,
    outcome: entry.outcome,
    outcomeTone: mapExecutionOutcomeTone(entry.outcome),
    message,
    metadataText,
    technicalMetadataText,
    ariaLabel: `${action} ${entry.outcome}. ${message} ${metadataText}`.trim()
  };
}

function formatExecutionAuditMetadata(entry: ExecutionAuditEntry): string {
  const occurredAt = new Date(entry.occurredAt);
  if (Number.isNaN(occurredAt.getTime())) {
    return "Recorded time unavailable";
  }

  return `Recorded ${formatTimestamp(occurredAt)}`;
}

function formatExecutionAuditTechnicalMetadata(entry: ExecutionAuditEntry): string | null {
  const parts = [
    entry.occurredAt,
    entry.metadata?.sessionId ? `session ${entry.metadata.sessionId}` : null,
    entry.orderId ? `order ${entry.orderId}` : null,
    entry.symbol ? `symbol ${entry.symbol}` : null,
    entry.runId ? `run ${entry.runId}` : null
  ].filter(Boolean);

  return parts.length > 0 ? parts.join(" · ") : null;
}

function formatExecutionAuditAction(action: string): string {
  const normalized = action.trim();
  if (normalized === "ReplayPaperSession") {
    return "Paper session replay";
  }

  const words = normalized
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[._-]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();
  return words ? `${words.charAt(0).toUpperCase()}${words.slice(1)}` : "Execution activity";
}

function mapExecutionOutcomeTone(outcome: string): ExecutionEvidenceTone {
  const normalized = outcome.toLowerCase();
  if (normalized.includes("fail") || normalized.includes("reject") || normalized.includes("error")) {
    return "danger";
  }

  if (normalized.includes("complete") || normalized.includes("accept") || normalized.includes("success")) {
    return "success";
  }

  return "warning";
}
