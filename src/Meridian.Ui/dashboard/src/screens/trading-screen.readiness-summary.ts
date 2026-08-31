import type {
  TradingAcceptanceGateStatus,
  TradingOperatorReadiness,
  WorkstationBrokerageSyncStatus
} from "@/types";

export type AcceptanceLevel = "ready" | "review" | "atRisk";

export interface TradingReadinessSummaryRow {
  id: string;
  label: string;
  value: string;
  level: AcceptanceLevel;
  ariaLabel: string;
}

export function formatReadinessStatusValue(status: TradingAcceptanceGateStatus | string): string {
  if (status === "ReviewRequired") {
    return "Review required";
  }

  return status;
}

export function mapReadinessStatusLevel(status: TradingAcceptanceGateStatus | string): AcceptanceLevel {
  if (status === "Ready") {
    return "ready";
  }

  if (status === "Blocked") {
    return "atRisk";
  }

  return "review";
}

export function mapBrokerageSyncLevel(status: WorkstationBrokerageSyncStatus): AcceptanceLevel {
  if (status.health === "Healthy" && !status.isStale) {
    return "ready";
  }

  if (status.health === "Failed" || status.health === "Degraded") {
    return "atRisk";
  }

  return "review";
}

export function buildTradingReadinessSummaryRows(
  readiness: TradingOperatorReadiness
): TradingReadinessSummaryRow[] {
  const overallValue = formatReadinessStatusValue(readiness.overallStatus);
  const paperValue = readiness.readyForPaperOperation ? "Ready for paper" : "Not paper ready";
  const liveBlockers = readiness.liveOperationBlockers ?? [];
  const liveOperationRequirements = readiness.liveOperationRequirements ?? [];
  const liveValue = readiness.readyForLiveOperation
    ? "Ready for live"
    : liveBlockers.length > 0
      ? `${liveBlockers.length} blocker${liveBlockers.length === 1 ? "" : "s"}`
      : "Not live ready";
  const brokerageValue = readiness.brokerageSync
    ? formatBrokerageSyncValue(readiness.brokerageSync)
    : "No account sync";
  const executionReconciliation = readiness.executionReconciliation ?? null;
  const asOfLabel = formatTradingUtcDateTime(readiness.asOf);
  const rows: TradingReadinessSummaryRow[] = [
    {
      id: "overall",
      label: "Overall",
      value: overallValue,
      level: mapReadinessStatusLevel(readiness.overallStatus),
      ariaLabel: `Overall readiness: ${overallValue}`
    },
    {
      id: "paper",
      label: "Paper",
      value: paperValue,
      level: readiness.readyForPaperOperation ? "ready" : "review",
      ariaLabel: `Paper operation readiness: ${paperValue}`
    },
    {
      id: "live",
      label: "Live",
      value: liveValue,
      level: readiness.readyForLiveOperation ? "ready" : liveBlockers.length > 0 ? "atRisk" : "review",
      ariaLabel: `Live operation readiness: ${liveValue}${liveBlockers.length > 0 ? `. Blockers: ${liveBlockers.join(", ")}` : ""}`
    },
    {
      id: "brokerage",
      label: "Brokerage",
      value: brokerageValue,
      level: readiness.brokerageSync ? mapBrokerageSyncLevel(readiness.brokerageSync) : "review",
      ariaLabel: `Brokerage sync: ${brokerageValue}`
    }
  ];

  for (const requirement of liveOperationRequirements) {
    const requirementValue = formatReadinessStatusValue(requirement.status);
    rows.push({
      id: `live-requirement-${requirement.requirementId}`,
      label: requirement.label,
      value: requirementValue,
      level: mapReadinessStatusLevel(requirement.status),
      ariaLabel: formatLiveOperationRequirementAriaLabel(requirement, requirementValue)
    });
  }

  if (executionReconciliation) {
    const executionValue = formatExecutionReconciliationValue(executionReconciliation);
    rows.push({
      id: "broker-execution",
      label: "Broker orders",
      value: executionValue,
      level: mapReadinessStatusLevel(executionReconciliation.status),
      ariaLabel: `Broker execution reconciliation: ${executionValue}`
    });
  }

  rows.push({
    id: "as-of",
    label: "As of",
    value: asOfLabel,
    level: "review",
    ariaLabel: `Readiness snapshot timestamp: ${asOfLabel}`
  });

  return rows;
}

export function formatTradingUtcDateTime(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return `${UTC_MONTH_LABELS[date.getUTCMonth()]} ${date.getUTCDate()}, ${padUtc(date.getUTCHours())}:${padUtc(date.getUTCMinutes())} UTC`;
}

function formatLiveOperationRequirementAriaLabel(
  requirement: NonNullable<TradingOperatorReadiness["liveOperationRequirements"]>[number],
  requirementValue: string
): string {
  const blocker = requirement.blockerCode ? ` Blocker: ${requirement.blockerCode}.` : "";
  return `${requirement.label}: ${requirementValue}. ${requirement.detail}${blocker}`;
}

function formatBrokerageSyncValue(status: WorkstationBrokerageSyncStatus): string {
  const staleSuffix = status.isStale && status.health !== "Stale" ? " stale" : "";
  return `${status.health}${staleSuffix}`;
}

function formatExecutionReconciliationValue(
  reconciliation: NonNullable<TradingOperatorReadiness["executionReconciliation"]>
): string {
  if (reconciliation.status === "Ready") {
    return `${reconciliation.matchedOpenOrderCount} matched`;
  }

  if (reconciliation.breakCount > 0) {
    return `${reconciliation.breakCount} break${reconciliation.breakCount === 1 ? "" : "s"}`;
  }

  return formatReadinessStatusValue(reconciliation.status);
}

const UTC_MONTH_LABELS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

function padUtc(value: number): string {
  return value.toString().padStart(2, "0");
}
