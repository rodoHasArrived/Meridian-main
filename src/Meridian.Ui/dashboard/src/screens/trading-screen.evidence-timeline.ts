import { pushEvidenceTimelineCandidate, type EvidenceTimelineCandidate } from "@/app-shell.evidence-timeline";
import type { OperatorFocusCandidateTone } from "@/app-shell.operator-focus";
import { routeForOperatorWorkItem, workspaceLabelForRoute } from "@/app-shell.workflow-routing";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type { OperatorWorkItem, TradingAcceptanceGate, TradingWorkspaceResponse } from "@/types";

export function buildTradingEvidenceTimelineItems(trading: TradingWorkspaceResponse | null): EvidenceTimelineCandidate[] {
  if (!trading) {
    return [];
  }

  const items: EvidenceTimelineCandidate[] = [];
  const readiness = trading.readiness ?? null;
  if (readiness) {
    pushEvidenceTimelineCandidate(items, {
      id: "trading-readiness:snapshot",
      label: `Paper readiness ${readiness.overallStatus}`,
      detail: readiness.readyForPaperOperation
        ? "Ready for paper operation with the latest readiness snapshot."
        : `${formatTradingEvidenceCount((readiness.acceptanceGates ?? []).filter((gate) => gate.status !== "Ready").length, "gate")} and ${formatTradingEvidenceCount((readiness.workItems ?? []).length, "work item")} require operator review.`,
      route: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
      workspaceLabel: "Trading",
      timestamp: readiness.asOf,
      tone: toneFromReadinessStatus(readiness.overallStatus),
      sourcePriority: 0,
      sourceIndex: 0
    });

    (readiness.workItems ?? []).forEach((item, index) => {
      const route = routeForOperatorWorkItem(item);
      pushEvidenceTimelineCandidate(items, {
        id: `work-item:${item.workItemId}`,
        label: item.label,
        detail: item.auditReference ? `${item.detail} Audit: ${item.auditReference}.` : item.detail,
        route,
        workspaceLabel: workspaceLabelForRoute(route),
        timestamp: item.createdAt,
        tone: toneFromTimelineWorkItem(item.tone),
        sourcePriority: 1,
        sourceIndex: index
      });
    });

    if (readiness.replay) {
      pushEvidenceTimelineCandidate(items, {
        id: `trading-replay:${readiness.replay.sessionId}`,
        label: readiness.replay.isConsistent ? "Replay verification passed" : "Replay verification mismatch",
        detail: `${formatTradingEvidenceCount(readiness.replay.comparedFillCount, "fill")}, ${formatTradingEvidenceCount(readiness.replay.comparedOrderCount, "order")}, and ${formatTradingEvidenceCount(readiness.replay.comparedLedgerEntryCount, "ledger entry", "ledger entries")} compared.${readiness.replay.verificationAuditId ? ` Audit: ${readiness.replay.verificationAuditId}.` : ""}`,
        route: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
        workspaceLabel: "Trading",
        timestamp: readiness.replay.verifiedAt,
        tone: readiness.replay.isConsistent ? "ready" : "blocked",
        sourcePriority: 2,
        sourceIndex: 0
      });
    }

    if (readiness.controls?.circuitBreakerChangedAt) {
      pushEvidenceTimelineCandidate(items, {
        id: "trading-control:circuit-breaker",
        label: readiness.controls.circuitBreakerOpen ? "Execution circuit breaker open" : "Execution circuit breaker changed",
        detail: readiness.controls.circuitBreakerReason ?? "Execution controls changed for the paper cockpit.",
        route: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
        workspaceLabel: "Trading",
        timestamp: readiness.controls.circuitBreakerChangedAt,
        tone: readiness.controls.circuitBreakerOpen ? "blocked" : "ready",
        sourcePriority: 3,
        sourceIndex: 0
      });
    }

    if (readiness.brokerageSync) {
      pushEvidenceTimelineCandidate(items, {
        id: `brokerage-sync:${readiness.brokerageSync.fundAccountId}`,
        label: `Brokerage sync ${readiness.brokerageSync.health.toLowerCase()}`,
        detail: readiness.brokerageSync.lastError
          ?? ((readiness.brokerageSync.warnings ?? []).join("; ")
            || `${formatTradingEvidenceCount(readiness.brokerageSync.positionCount, "position")}, ${formatTradingEvidenceCount(readiness.brokerageSync.openOrderCount, "open order")}, and ${formatTradingEvidenceCount(readiness.brokerageSync.securityMissingCount, "missing security", "missing securities")} in sync evidence.`),
        route: readiness.brokerageSync.health === "Unlinked" ? WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup : WORKSTATION_ROUTE_CATALOG.portfolioBrokerageSync,
        workspaceLabel: readiness.brokerageSync.health === "Unlinked" ? "Settings" : "Portfolio",
        timestamp: readiness.brokerageSync.lastAttemptedSyncAt ?? readiness.brokerageSync.lastSuccessfulSyncAt,
        tone: readiness.brokerageSync.health === "Healthy" ? "ready" : readiness.brokerageSync.health === "Stale" ? "review" : "blocked",
        sourcePriority: 4,
        sourceIndex: 0
      });
    }
  }

  (trading.fills ?? []).forEach((fill, index) => {
    pushEvidenceTimelineCandidate(items, {
      id: `trading-fill:${fill.fillId}`,
      label: `${fill.side} fill ${fill.symbol}`,
      detail: `${fill.quantity} @ ${fill.price} on ${fill.venue}. Order: ${fill.orderId}.`,
      route: WORKSTATION_ROUTE_CATALOG.trading,
      workspaceLabel: "Trading",
      timestamp: fill.timestamp,
      tone: "ready",
      sourcePriority: 5,
      sourceIndex: index
    });
  });

  return items;
}

function toneFromReadinessStatus(status: TradingAcceptanceGate["status"]): OperatorFocusCandidateTone {
  switch (status) {
    case "Blocked":
      return "blocked";
    case "ReviewRequired":
      return "review";
    case "Ready":
      return "ready";
  }
}

function toneFromTimelineWorkItem(tone: OperatorWorkItem["tone"]): OperatorFocusCandidateTone {
  switch (tone) {
    case "Critical":
      return "blocked";
    case "Warning":
    case "Info":
      return "review";
    case "Success":
      return "ready";
  }
}

function formatTradingEvidenceCount(count: number, singular: string, plural = `${singular}s`): string {
  return `${count} ${count === 1 ? singular : plural}`;
}
