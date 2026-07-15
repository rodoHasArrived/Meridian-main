import { useCallback, useEffect, useState } from "react";
import { getQualityDashboard } from "@/lib/api";
import { useRequestLifecycle } from "@/hooks/use-request-lifecycle";
import type {
  QualityAnomalyEntry,
  QualityComponentResponse,
  QualityCompositeGapResponse,
  QualityCompositeSymbolResponse,
  QualityDashboardResponse,
  QualityProviderFreshnessResponse
} from "@/types";

export type QualityDashboardFetcher = typeof getQualityDashboard;

export type QualityTone = "success" | "warning" | "danger";

export interface DataQualityScoreCard {
  id: string;
  label: string;
  value: string;
  tone: QualityTone;
  detail: string;
}

export interface DataQualitySymbolRow {
  symbol: string;
  scoreLabel: string;
  status: QualityCompositeSymbolResponse["status"];
  tone: QualityTone;
  isPartial: boolean;
  coverageLabel: string;
  completenessLabel: string;
  freshnessLabel: string;
  adapterLabel: string;
  expectedEventsLabel: string;
  gapCount: number;
  anomalyCount: number;
  components: QualityComponentResponse[];
  openGaps: QualityCompositeGapResponse[];
  providerFreshness: QualityProviderFreshnessResponse[];
  issues: string[];
}

export interface DataQualityPanelModel {
  dashboardVersion: string | null;
  overallTone: QualityTone;
  overallLabel: string;
  overallStatus: "Green" | "Amber" | "Red" | "Unavailable";
  isPartial: boolean;
  scoreCards: DataQualityScoreCard[];
  symbols: DataQualitySymbolRow[];
  healthySymbolCount: number;
  openGaps: QualityCompositeGapResponse[];
  unacknowledgedAnomalies: QualityAnomalyEntry[];
  summary: string;
}

const WARNING_SCORE = 80;
const CRITICAL_SCORE = 60;

export function scoreTone(score: number): QualityTone {
  if (score < CRITICAL_SCORE) return "danger";
  if (score < WARNING_SCORE) return "warning";
  return "success";
}

function formatScore(score: number | null): string {
  return score === null ? "Unavailable" : `${score.toFixed(1)}`;
}

function statusTone(status: QualityCompositeSymbolResponse["status"]): QualityTone {
  switch (status) {
    case "Red":
      return "danger";
    case "Green":
      return "success";
    default:
      return "warning";
  }
}

function componentFor(
  components: QualityComponentResponse[],
  kind: QualityComponentResponse["kind"]
): QualityComponentResponse | undefined {
  return components.find((component) => component.kind === kind);
}

function componentCard(
  components: QualityComponentResponse[],
  kind: QualityComponentResponse["kind"],
  label: string
): DataQualityScoreCard {
  const component = componentFor(components, kind);
  return {
    id: kind,
    label,
    value: formatScore(component?.score ?? null),
    tone: component?.score === null || component?.score === undefined
      ? "warning"
      : scoreTone(component.score),
    detail: component?.detail ?? "This source has not produced a measured score."
  };
}

function buildSymbolRow(symbol: QualityCompositeSymbolResponse): DataQualitySymbolRow {
  const stored = componentFor(symbol.components, "StoredCompleteness");
  const streaming = componentFor(symbol.components, "StreamingFreshness");
  const adapter = componentFor(symbol.components, "AdapterGapIntegrity");
  const expectedEventsLabel = symbol.expectedEvents === null || symbol.observedEvents === null
    ? "Expected-session counts unavailable"
    : `${symbol.observedEvents.toLocaleString()} / ${symbol.expectedEvents.toLocaleString()} expected events`;

  return {
    symbol: symbol.symbol,
    scoreLabel: formatScore(symbol.compositeScore),
    status: symbol.status,
    tone: statusTone(symbol.status),
    isPartial: symbol.isPartial,
    coverageLabel: `${Math.round(symbol.coverageWeight * 100)}% evidence coverage`,
    completenessLabel: formatScore(stored?.score ?? null),
    freshnessLabel: formatScore(streaming?.score ?? null),
    adapterLabel: formatScore(adapter?.score ?? null),
    expectedEventsLabel,
    gapCount: symbol.openGaps.length,
    anomalyCount: symbol.anomalyCount,
    components: symbol.components,
    openGaps: symbol.openGaps,
    providerFreshness: symbol.providerFreshness,
    issues: symbol.issues
  };
}

/** Project the canonical server-owned composite model into browser presentation state. */
export function buildDataQualityPanelModel(response: QualityDashboardResponse): DataQualityPanelModel {
  const unacknowledgedAnomalies = response.recentAnomalies.filter((anomaly) => !anomaly.isAcknowledged);
  if (!response.composite) {
    return {
      dashboardVersion: null,
      overallTone: "warning",
      overallLabel: "Unavailable",
      overallStatus: "Unavailable",
      isPartial: true,
      scoreCards: [],
      symbols: [],
      healthySymbolCount: 0,
      openGaps: [],
      unacknowledgedAnomalies,
      summary: "Composite quality evidence is unavailable. Legacy streaming telemetry remains available."
    };
  }

  const composite = response.composite;
  const symbols = composite.symbols
    .map(buildSymbolRow)
    .sort((left, right) => {
      const leftScore = Number(left.scoreLabel);
      const rightScore = Number(right.scoreLabel);
      return (Number.isFinite(leftScore) ? leftScore : -1) -
        (Number.isFinite(rightScore) ? rightScore : -1) || left.symbol.localeCompare(right.symbol);
    });
  const healthySymbolCount = symbols.filter((symbol) => symbol.status === "Green").length;

  return {
    dashboardVersion: composite.version,
    overallTone: statusTone(composite.status),
    overallLabel: composite.status === "Unavailable" ? "Unavailable" : formatScore(composite.compositeScore),
    overallStatus: composite.status,
    isPartial: composite.isPartial,
    scoreCards: [
      {
        id: "Composite",
        label: "Composite quality",
        value: composite.status === "Unavailable" ? "Unavailable" : formatScore(composite.compositeScore),
        tone: statusTone(composite.status),
        detail: composite.isPartial
          ? `${Math.round(composite.coverageWeight * 100)}% of required evidence is measured.`
          : "All required quality sources are measured."
      },
      componentCard(composite.components, "StoredCompleteness", "Stored completeness"),
      componentCard(composite.components, "StreamingFreshness", "Streaming freshness"),
      componentCard(composite.components, "AdapterGapIntegrity", "Adapter gap integrity")
    ],
    symbols,
    healthySymbolCount,
    openGaps: composite.openGaps.filter((gap) => gap.status === "Open"),
    unacknowledgedAnomalies,
    summary: symbols.length === 0
      ? "No collected symbols have produced quality evidence."
      : `${symbols.length} collected symbol${symbols.length === 1 ? "" : "s"}; ` +
        `${symbols.length - healthySymbolCount} require review, ${composite.openGaps.length} open gap` +
        `${composite.openGaps.length === 1 ? "" : "s"}, ${composite.anomalyCount} anomal` +
        `${composite.anomalyCount === 1 ? "y" : "ies"}.`
  };
}

export interface DataQualityPanelViewModel {
  loading: boolean;
  error: string | null;
  model: DataQualityPanelModel | null;
  refresh: () => Promise<void>;
}

/** Fetches the canonical `/api/quality/dashboard` projection and rejects stale refresh results. */
export function useDataQualityPanel(
  fetchDashboard: QualityDashboardFetcher = getQualityDashboard
): DataQualityPanelViewModel {
  const { status, start, succeed, fail, finish } = useRequestLifecycle({
    operation: "data-quality-dashboard",
    failureMessage: "Data quality dashboard is unavailable."
  });
  const [model, setModel] = useState<DataQualityPanelModel | null>(null);

  const refresh = useCallback(async () => {
    const request = start({ busyMode: "supersede" });
    if (!request) return;
    try {
      const response = await fetchDashboard();
      if (!request.isCurrent()) return;
      request.safeSetState(setModel, buildDataQualityPanelModel(response));
      succeed(request);
    } catch (fetchError) {
      if (!request.isCurrent()) return;
      request.safeSetState(setModel, null);
      fail(request, fetchError);
    } finally {
      finish(request);
    }
  }, [fail, fetchDashboard, finish, start, succeed]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  return {
    loading: status.phase === "idle" || status.inFlight,
    error: status.error,
    model,
    refresh
  };
}
