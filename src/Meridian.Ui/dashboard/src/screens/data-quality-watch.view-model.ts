/**
 * Presentation logic for the data quality watch panel.
 *
 * The Data screen already shows the quality dashboard, gaps, and the anomaly
 * feed. What it never showed is the operational question underneath them:
 * which symbols are unhealthy, slow, incomplete, or silent right now. Nine
 * rollup routes answer exactly that and had no caller.
 *
 * The endpoint group serializes enums as ordinals, so health states, anomaly
 * types and severities resolve through the transcribed maps; an ordinal outside
 * a map is named with its number rather than collapsed into a neighbour.
 */

import {
  QUALITY_ANOMALY_SEVERITY_LABELS,
  QUALITY_ANOMALY_TYPE_LABELS,
  QUALITY_HEALTH_STATE_LABELS,
  type QualityAnomaly,
  type QualityCompletenessScore,
  type QualityCompletenessSummary,
  type QualityHealthSnapshot,
  type QualityHighLatencySymbol,
  type QualityLatencyStatistics,
  type QualitySymbolHealth,
  type QualityTopErrorSymbol
} from "@/types/data-quality-watch.types";

export type QualityTone = "default" | "success" | "warning" | "danger";

export interface QualityHeadlineViewModel {
  loaded: boolean;
  statusLabel: string;
  statusTone: QualityTone;
  scoreLabel: string;
  activeSymbolsLabel: string;
  symbolsWithIssuesLabel: string;
  recentActivityLabel: string;
  asOfLabel: string;
}

export interface UnhealthySymbolRowViewModel {
  symbol: string;
  stateLabel: string;
  stateTone: QualityTone;
  scoreLabel: string;
  silenceLabel: string;
  issues: string;
  ariaLabel: string;
}

export interface LatencyOverviewViewModel {
  loaded: boolean;
  headlineLabel: string;
  spreadLabel: string;
  extremesLabel: string;
  sampleLabel: string;
}

export interface SymbolCountRowViewModel {
  symbol: string;
  valueLabel: string;
  tone: QualityTone;
}

export interface CompletenessOverviewViewModel {
  loaded: boolean;
  coverageLabel: string;
  averageScoreLabel: string;
  spreadLabel: string;
  gradeLabel: string;
  trackedLabel: string;
}

export interface LowCompletenessRowViewModel {
  symbol: string;
  date: string;
  scoreLabel: string;
  grade: string;
  tone: QualityTone;
  missingLabel: string;
  coverageLabel: string;
  ariaLabel: string;
}

export interface UnacknowledgedAnomalyRowViewModel {
  id: string;
  symbol: string;
  typeLabel: string;
  severityLabel: string;
  severityTone: QualityTone;
  description: string;
  deviationLabel: string;
  detectedAt: string;
  ariaLabel: string;
}

export interface StaleSymbolsViewModel {
  loaded: boolean;
  symbols: string[];
  label: string;
  /** Null when nothing is stale, so the panel does not manufacture an alarm. */
  notice: string | null;
}

export function buildQualityHeadline(snapshot: QualityHealthSnapshot | null): QualityHeadlineViewModel {
  if (!snapshot) {
    return {
      loaded: false,
      statusLabel: "Not loaded",
      statusTone: "default",
      scoreLabel: "—",
      activeSymbolsLabel: "—",
      symbolsWithIssuesLabel: "—",
      recentActivityLabel: "—",
      asOfLabel: "Not loaded"
    };
  }

  return {
    loaded: true,
    // The server derives status from score; it is restated, not recomputed.
    statusLabel: snapshot.status,
    statusTone: healthStatusTone(snapshot.status),
    scoreLabel: formatPercent(snapshot.score),
    activeSymbolsLabel: String(snapshot.activeSymbols),
    symbolsWithIssuesLabel: `${snapshot.symbolsWithIssues} of ${snapshot.activeSymbols}`,
    recentActivityLabel: `${snapshot.gapsLast5Min} gaps, ${snapshot.errorsLast5Min} errors, `
      + `${snapshot.anomaliesLast5Min} anomalies in the last 5 min`,
    asOfLabel: snapshot.timestamp
  };
}

export function buildUnhealthySymbolRow(health: QualitySymbolHealth): UnhealthySymbolRowViewModel {
  const stateLabel = resolveOrdinal(health.state, QUALITY_HEALTH_STATE_LABELS, "Health state");
  const issues = health.activeIssues.length > 0
    ? health.activeIssues.join("; ")
    : "No issue detail reported";

  return {
    symbol: health.symbol,
    stateLabel,
    stateTone: healthStateTone(health.state),
    scoreLabel: formatPercent(health.score),
    silenceLabel: describeSilence(health.timeSinceLastEvent, health.lastEvent),
    issues,
    ariaLabel: `${health.symbol}: ${stateLabel}, score ${formatPercent(health.score)}. ${issues}`
  };
}

export function buildLatencyOverview(statistics: QualityLatencyStatistics | null): LatencyOverviewViewModel {
  if (!statistics) {
    return {
      loaded: false,
      headlineLabel: "—",
      spreadLabel: "—",
      extremesLabel: "Not loaded",
      sampleLabel: "—"
    };
  }

  return {
    loaded: true,
    headlineLabel: `p99 ${formatMs(statistics.globalP99Ms)}`,
    spreadLabel: `p50 ${formatMs(statistics.globalP50Ms)} · p90 ${formatMs(statistics.globalP90Ms)} `
      + `· mean ${formatMs(statistics.globalMeanMs)}`,
    // Either extreme can be absent when no symbol has enough samples to rank.
    extremesLabel: `Fastest ${statistics.fastestSymbol ?? "not ranked"} · `
      + `slowest ${statistics.slowestSymbol ?? "not ranked"}`,
    sampleLabel: `${statistics.totalSamples.toLocaleString("en-US")} samples across `
      + `${statistics.symbolsTracked} symbol${statistics.symbolsTracked === 1 ? "" : "s"}`
  };
}

export function buildHighLatencyRow(
  entry: QualityHighLatencySymbol,
  thresholdMs: number
): SymbolCountRowViewModel {
  return {
    symbol: entry.symbol,
    valueLabel: formatMs(entry.p99Ms),
    // Every row is already over the threshold the query asked for; the tone
    // separates the ones an order of magnitude past it.
    tone: entry.p99Ms >= thresholdMs * 10 ? "danger" : "warning"
  };
}

export function buildTopErrorRow(entry: QualityTopErrorSymbol): SymbolCountRowViewModel {
  return {
    symbol: entry.symbol,
    valueLabel: `${entry.errorCount.toLocaleString("en-US")} error${entry.errorCount === 1 ? "" : "s"}`,
    tone: entry.errorCount > 0 ? "warning" : "default"
  };
}

export function buildCompletenessOverview(
  summary: QualityCompletenessSummary | null
): CompletenessOverviewViewModel {
  if (!summary) {
    return {
      loaded: false,
      coverageLabel: "—",
      averageScoreLabel: "—",
      spreadLabel: "—",
      gradeLabel: "Not loaded",
      trackedLabel: "—"
    };
  }

  const grades = Object.entries(summary.gradeDistribution)
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([grade, count]) => `${grade}:${count}`)
    .join(" · ");

  return {
    loaded: true,
    coverageLabel: formatPercent(summary.overallCoverage),
    averageScoreLabel: formatPercent(summary.averageScore),
    spreadLabel: `${formatPercent(summary.minScore)} – ${formatPercent(summary.maxScore)}`,
    gradeLabel: grades || "No grades recorded",
    trackedLabel: `${summary.symbolsTracked} symbol${summary.symbolsTracked === 1 ? "" : "s"} over `
      + `${summary.datesTracked} date${summary.datesTracked === 1 ? "" : "s"}`
  };
}

export function buildLowCompletenessRow(score: QualityCompletenessScore): LowCompletenessRowViewModel {
  const scoreLabel = formatPercent(score.score);
  return {
    symbol: score.symbol,
    date: score.date,
    scoreLabel,
    // Grade is computed server-side from the same score; it is shown as sent.
    grade: score.grade,
    tone: score.score < 0.5 ? "danger" : "warning",
    missingLabel: `${score.missingEvents.toLocaleString("en-US")} of `
      + `${score.expectedEvents.toLocaleString("en-US")} missing`,
    coverageLabel: formatPercent(score.coveragePercent / 100),
    ariaLabel: `${score.symbol} on ${score.date}: completeness ${scoreLabel}, grade ${score.grade}, `
      + `${score.missingEvents} of ${score.expectedEvents} events missing.`
  };
}

export function buildUnacknowledgedAnomalyRow(anomaly: QualityAnomaly): UnacknowledgedAnomalyRowViewModel {
  const typeLabel = resolveOrdinal(anomaly.type, QUALITY_ANOMALY_TYPE_LABELS, "Anomaly type");
  const severityLabel = resolveOrdinal(anomaly.severity, QUALITY_ANOMALY_SEVERITY_LABELS, "Severity");

  return {
    id: anomaly.id,
    symbol: anomaly.symbol,
    typeLabel,
    severityLabel,
    severityTone: anomalySeverityTone(anomaly.severity),
    description: anomaly.description?.trim() || "No description recorded.",
    deviationLabel: `${formatSignedPercent(anomaly.deviationPercent)} (z ${anomaly.zScore.toFixed(2)})`,
    detectedAt: anomaly.detectedAt,
    ariaLabel: `${severityLabel} ${typeLabel} on ${anomaly.symbol}, detected ${anomaly.detectedAt}.`
  };
}

export function buildStaleSymbolsViewModel(symbols: string[] | null): StaleSymbolsViewModel {
  if (!symbols) {
    return { loaded: false, symbols: [], label: "Not loaded", notice: null };
  }

  if (symbols.length === 0) {
    return { loaded: true, symbols: [], label: "None", notice: null };
  }

  return {
    loaded: true,
    symbols,
    label: String(symbols.length),
    notice: `${symbols.length} symbol${symbols.length === 1 ? " has" : "s have"} stopped reporting: `
      + `${symbols.join(", ")}.`
  };
}

function healthStatusTone(status: string): QualityTone {
  const normalized = status.trim().toLowerCase();
  if (normalized === "healthy") {
    return "success";
  }

  if (normalized === "degraded") {
    return "warning";
  }

  // An unrecognized status is not assumed benign.
  return normalized === "unhealthy" ? "danger" : "default";
}

function healthStateTone(state: number): QualityTone {
  if (state === 0) {
    return "success";
  }

  if (state === 1) {
    return "warning";
  }

  return state === 2 || state === 3 ? "danger" : "default";
}

function anomalySeverityTone(severity: number): QualityTone {
  if (severity === 0) {
    return "default";
  }

  if (severity === 1) {
    return "warning";
  }

  return severity === 2 || severity === 3 ? "danger" : "default";
}

/**
 * `TimeSpan` crosses the wire as `"[d.]hh:mm:ss[.fffffff]"`. Rather than parse
 * that into a duration the panel would then re-render, the raw span is shown
 * beside the timestamp it was measured from.
 */
function describeSilence(timeSinceLastEvent: string, lastEvent: string): string {
  const span = timeSinceLastEvent?.trim();
  return span ? `${span} since ${lastEvent}` : `Last event ${lastEvent}`;
}

function resolveOrdinal(
  ordinal: number | null | undefined,
  labels: Readonly<Record<number, string>>,
  subject: string
): string {
  if (typeof ordinal !== "number") {
    return `${subject} not reported`;
  }

  return labels[ordinal] ?? `Unrecognized ${subject.toLowerCase()} ${ordinal}`;
}

function formatPercent(fraction: number): string {
  return `${(fraction * 100).toFixed(1)}%`;
}

function formatSignedPercent(percent: number): string {
  const formatted = `${Math.abs(percent).toFixed(1)}%`;
  if (percent > 0) {
    return `+${formatted}`;
  }

  return percent < 0 ? `-${formatted}` : formatted;
}

function formatMs(value: number): string {
  return value >= 1000 ? `${(value / 1000).toFixed(2)}s` : `${value.toFixed(1)}ms`;
}
