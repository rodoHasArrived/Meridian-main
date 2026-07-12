import { useCallback, useEffect, useState } from "react";
import { getQualityDashboard } from "@/lib/api";
import { useRequestLifecycle } from "@/hooks/use-request-lifecycle";
import type {
  QualityAnomalyEntry,
  QualityDashboardResponse,
  QualityGapEntry,
  QualitySymbolScore
} from "@/types";

export type QualityDashboardFetcher = typeof getQualityDashboard;

export type QualityTone = "success" | "warning" | "danger";

export interface DataQualityScoreCard {
  id: string;
  label: string;
  value: string;
  tone: QualityTone;
}

export interface DataQualitySymbolRow {
  symbol: string;
  completenessLabel: string;
  freshnessLabel: string;
  gapCount: number;
  anomalyCount: number;
  health: QualitySymbolScore["health"];
  tone: QualityTone;
}

export interface DataQualityPanelModel {
  overallTone: QualityTone;
  overallLabel: string;
  scoreCards: DataQualityScoreCard[];
  attentionSymbols: DataQualitySymbolRow[];
  healthySymbolCount: number;
  openGaps: QualityGapEntry[];
  unacknowledgedAnomalies: QualityAnomalyEntry[];
  summary: string;
}

/** Score thresholds shared with the quality monitoring service conventions. */
const WARNING_SCORE = 95;
const CRITICAL_SCORE = 85;

export function scoreTone(score: number): QualityTone {
  if (score < CRITICAL_SCORE) return "danger";
  if (score < WARNING_SCORE) return "warning";
  return "success";
}

function formatScore(score: number): string {
  return `${score.toFixed(1)}%`;
}

function healthTone(health: QualitySymbolScore["health"]): QualityTone {
  switch (health) {
    case "Critical":
      return "danger";
    case "Warning":
      return "warning";
    default:
      return "success";
  }
}

/**
 * Pure projection of the unified quality dashboard response into presentation state:
 * tones for the score strip, the symbols that need operator attention (worst first),
 * and the open-gap / unacknowledged-anomaly queues.
 */
export function buildDataQualityPanelModel(response: QualityDashboardResponse): DataQualityPanelModel {
  const attentionSymbols = response.symbols
    .filter((symbol) => symbol.health !== "Healthy")
    .sort(
      (a, b) =>
        Math.min(a.completenessScore, a.freshnessScore) -
        Math.min(b.completenessScore, b.freshnessScore)
    )
    .map((symbol) => ({
      symbol: symbol.symbol,
      completenessLabel: formatScore(symbol.completenessScore),
      freshnessLabel: formatScore(symbol.freshnessScore),
      gapCount: symbol.gapCount,
      anomalyCount: symbol.anomalyCount,
      health: symbol.health,
      tone: healthTone(symbol.health)
    }));

  const openGaps = response.recentGaps.filter((gap) => gap.status === "Open");
  const unacknowledgedAnomalies = response.recentAnomalies.filter((anomaly) => !anomaly.acknowledged);
  const healthySymbolCount = response.symbols.length - attentionSymbols.length;

  return {
    overallTone: scoreTone(response.overallScore),
    overallLabel: formatScore(response.overallScore),
    scoreCards: [
      {
        id: "overall",
        label: "Overall quality",
        value: formatScore(response.overallScore),
        tone: scoreTone(response.overallScore)
      },
      {
        id: "completeness",
        label: "Completeness",
        value: formatScore(response.completenessScore),
        tone: scoreTone(response.completenessScore)
      },
      {
        id: "freshness",
        label: "Freshness",
        value: formatScore(response.freshnessScore),
        tone: scoreTone(response.freshnessScore)
      },
      {
        id: "anomaly-rate",
        label: "Anomaly rate",
        value: `${response.anomalyRate.toFixed(2)}/min`,
        tone: response.anomalyRate > 1 ? "danger" : response.anomalyRate > 0.25 ? "warning" : "success"
      }
    ],
    attentionSymbols,
    healthySymbolCount,
    openGaps,
    unacknowledgedAnomalies,
    summary:
      attentionSymbols.length === 0 && openGaps.length === 0 && unacknowledgedAnomalies.length === 0
        ? `All ${response.symbols.length} tracked symbols healthy.`
        : `${attentionSymbols.length} symbol${attentionSymbols.length === 1 ? "" : "s"} need attention, ` +
          `${openGaps.length} open gap${openGaps.length === 1 ? "" : "s"}, ` +
          `${unacknowledgedAnomalies.length} unacknowledged anomal${unacknowledgedAnomalies.length === 1 ? "y" : "ies"}.`
  };
}

export interface DataQualityPanelViewModel {
  loading: boolean;
  error: string | null;
  model: DataQualityPanelModel | null;
  refresh: () => Promise<void>;
}

/**
 * View-model for the Data workspace quality panel, backed by the unified
 * `/api/quality/dashboard` read. Fetches on mount; consumers refresh on demand.
 */
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
    if (!request) {
      return;
    }
    try {
      const response = await fetchDashboard();
      if (!request.isCurrent()) {
        // A newer refresh superseded this one; drop the stale response.
        return;
      }
      request.safeSetState(setModel, buildDataQualityPanelModel(response));
      succeed(request);
    } catch (fetchError) {
      if (!request.isCurrent()) {
        // A newer refresh superseded this one; discard the stale failure so it
        // cannot clobber the newer request's state (symmetric with the success path).
        return;
      }
      request.safeSetState(setModel, null);
      fail(request, fetchError);
    } finally {
      finish(request);
    }
  }, [fail, fetchDashboard, finish, start, succeed]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  // Preserve the previous contract: the panel reads as loading until the first
  // request settles, then tracks in-flight refreshes. The lifecycle version guard
  // adds the stale-response protection the hand-rolled fetch lacked.
  const loading = status.phase === "idle" || status.inFlight;
  return { loading, error: status.error, model, refresh };
}
