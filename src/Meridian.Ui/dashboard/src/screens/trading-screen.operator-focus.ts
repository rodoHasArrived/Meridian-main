import { buildOperatorFocusCandidate, type OperatorFocusCandidate } from "@/app-shell.operator-focus";
import {
  actionLabelForOperatorWorkItem,
  routeForOperatorWorkItem,
  workspaceLabelForRoute
} from "@/app-shell.workflow-routing";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type { OperatorWorkItem, TradingAcceptanceGate, TradingWorkspaceResponse } from "@/types";

export function buildTradingOperatorFocusItems(trading: TradingWorkspaceResponse | null): OperatorFocusCandidate[] {
  const readiness = trading?.readiness ?? null;
  if (!readiness) {
    return [];
  }

  const workItems = (readiness.workItems ?? [])
    .map((item, index) => buildOperatorFocusCandidateFromWorkItem(item, index))
    .filter((item): item is OperatorFocusCandidate => Boolean(item));

  const gateItems = (readiness.acceptanceGates ?? [])
    .map((gate, index) => buildOperatorFocusCandidateFromGate(gate, index))
    .filter((item): item is OperatorFocusCandidate => Boolean(item));

  const replayItems = readiness.replay && !readiness.replay.isConsistent
    ? [buildOperatorFocusCandidate({
        id: `trading-replay:${readiness.replay.sessionId}`,
        label: "Replay evidence inconsistent",
        detail: readiness.replay.mismatchReasons.length > 0
          ? readiness.replay.mismatchReasons.join("; ")
          : "Latest replay verification does not match the active paper session.",
        route: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
        workspaceLabel: "Trading",
        actionLabel: "Open replay evidence",
        tone: "blocked",
        sourcePriority: 4,
        sourceIndex: 0
      })]
    : [];

  const controlItems = readiness.controls?.circuitBreakerOpen
    ? [buildOperatorFocusCandidate({
        id: "trading-control:circuit-breaker",
        label: "Execution circuit breaker open",
        detail: readiness.controls.circuitBreakerReason ?? "Execution controls require operator review before paper operation.",
        route: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
        workspaceLabel: "Trading",
        actionLabel: "Open execution controls",
        tone: "blocked",
        sourcePriority: 4,
        sourceIndex: 1
      })]
    : [];

  const brokerage = readiness.brokerageSync;
  const brokerageItems = brokerage && brokerage.health !== "Healthy"
    ? [buildOperatorFocusCandidate({
        id: `brokerage-sync:${brokerage.fundAccountId}`,
        label: `Brokerage sync ${brokerage.health.toLowerCase()}`,
        detail: brokerage.lastError
          ?? ((brokerage.warnings ?? []).join("; ")
            || `${brokerage.positionCount} positions, ${brokerage.openOrderCount} open orders, and ${brokerage.securityMissingCount} missing securities in sync evidence.`),
        route: brokerage.health === "Unlinked" ? WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup : WORKSTATION_ROUTE_CATALOG.portfolioBrokerageSync,
        workspaceLabel: brokerage.health === "Unlinked" ? "Settings" : "Portfolio",
        actionLabel: brokerage.health === "Unlinked" ? "Fix provider setup" : "Open brokerage sync",
        tone: brokerage.health === "Stale" ? "review" : "blocked",
        sourcePriority: 5,
        sourceIndex: 0
      })]
    : [];

  return [...workItems, ...gateItems, ...replayItems, ...controlItems, ...brokerageItems];
}

function buildOperatorFocusCandidateFromWorkItem(
  item: OperatorWorkItem,
  index: number
): OperatorFocusCandidate | null {
  const tone = toneFromOperatorWorkItem(item);
  if (!tone) {
    return null;
  }

  const route = routeForOperatorWorkItem(item);
  const actionLabel = actionLabelForOperatorWorkItem(item);
  return buildOperatorFocusCandidate({
    id: `work-item:${item.workItemId}`,
    label: item.label,
    detail: item.detail,
    route,
    workspaceLabel: workspaceLabelForRoute(route),
    actionLabel,
    tone,
    sourcePriority: 0,
    sourceIndex: index
  });
}

function buildOperatorFocusCandidateFromGate(
  gate: TradingAcceptanceGate,
  index: number
): OperatorFocusCandidate | null {
  if (gate.status === "Ready") {
    return null;
  }

  return buildOperatorFocusCandidate({
    id: `trading-gate:${gate.gateId}`,
    label: gate.label,
    detail: gate.detail,
    route: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
    workspaceLabel: "Trading",
    actionLabel: "Open readiness",
    tone: gate.status === "Blocked" ? "blocked" : "review",
    sourcePriority: 3,
    sourceIndex: index
  });
}

function toneFromOperatorWorkItem(item: OperatorWorkItem): OperatorFocusCandidate["tone"] | null {
  switch (item.tone) {
    case "Critical":
      return "blocked";
    case "Warning":
    case "Info":
      return "review";
    case "Success":
      return null;
  }
}
