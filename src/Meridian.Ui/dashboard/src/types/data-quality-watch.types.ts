/**
 * Data quality watch types.
 *
 * Mirrors the responses of the `/api/quality/*` rollup routes served by
 * `Meridian.Ui.Shared.Endpoints.DataQualityEndpoints`:
 *
 * - `GET /api/quality/health` and `/health/unhealthy`
 * - `GET /api/quality/latency/statistics` and `/latency/high`
 * - `GET /api/quality/errors/top-symbols`
 * - `GET /api/quality/completeness/summary` and `/completeness/low`
 * - `GET /api/quality/anomalies/unacknowledged` and `/anomalies/stale`
 *
 * This endpoint group builds its own `JsonSerializerOptions` with camelCase
 * naming and no `JsonStringEnumConverter`, so every enum arrives as its
 * **ordinal**. The maps below are transcribed from the `byte` enums in
 * `DataQualityModels.cs`; an ordinal outside a map is reported as unrecognized
 * rather than guessed. `TimeSpan` values arrive as `"hh:mm:ss"` strings.
 */

import type { ConfigurationChangeStatus } from "@/screens/settings-change-impact";

/** `HealthState` ordinals. */
export const QUALITY_HEALTH_STATE_LABELS: Readonly<Record<number, string>> = Object.freeze({
  0: "Healthy",
  1: "Degraded",
  2: "Unhealthy",
  3: "Stale",
  4: "Unknown"
});

/** `AnomalySeverity` ordinals. */
export const QUALITY_ANOMALY_SEVERITY_LABELS: Readonly<Record<number, string>> = Object.freeze({
  0: "Info",
  1: "Warning",
  2: "Error",
  3: "Critical"
});

/** `AnomalyType` ordinals. */
export const QUALITY_ANOMALY_TYPE_LABELS: Readonly<Record<number, string>> = Object.freeze({
  0: "Price spike",
  1: "Price drop",
  2: "Volume spike",
  3: "Volume drop",
  4: "Wide spread",
  5: "Stale data",
  6: "Rapid price change",
  7: "Abnormal volatility",
  8: "Missing data",
  9: "Duplicate data",
  10: "Crossed market",
  11: "Invalid price",
  12: "Invalid volume"
});

/**
 * `GET /api/quality/health`. The handler composes this object inline rather than
 * returning a shared contract, so the shape is transcribed from the handler.
 * `status` is derived server-side from `score` and is reported as sent.
 */
export interface QualityHealthSnapshot {
  status: string;
  score: number;
  activeSymbols: number;
  symbolsWithIssues: number;
  gapsLast5Min: number;
  errorsLast5Min: number;
  anomaliesLast5Min: number;
  timestamp: string;
}

/** `SymbolHealthStatus`, returned unprojected by `/health/unhealthy`. */
export interface QualitySymbolHealth {
  symbol: string;
  /** `HealthState` ordinal. */
  state: number;
  score: number;
  lastEvent: string;
  /** `TimeSpan` as `"hh:mm:ss"`. */
  timeSinceLastEvent: string;
  activeIssues: string[];
}

/** `QualityLatencyStatisticsResponse`. */
export interface QualityLatencyStatistics {
  symbolsTracked: number;
  totalSamples: number;
  globalMeanMs: number;
  globalP50Ms: number;
  globalP90Ms: number;
  globalP99Ms: number;
  fastestSymbol?: string | null;
  slowestSymbol?: string | null;
  distributionsBySymbol: Record<string, number>;
  calculatedAt: string;
}

/** `QualityHighLatencySymbolResponse`. */
export interface QualityHighLatencySymbol {
  symbol: string;
  p99Ms: number;
}

/** `QualityTopErrorSymbolResponse`. */
export interface QualityTopErrorSymbol {
  symbol: string;
  errorCount: number;
}

/** `CompletenessSummary`, returned unprojected by `/completeness/summary`. */
export interface QualityCompletenessSummary {
  totalSymbolDates: number;
  averageScore: number;
  minScore: number;
  maxScore: number;
  symbolsTracked: number;
  datesTracked: number;
  totalEvents: number;
  totalExpectedEvents: number;
  overallCoverage: number;
  gradeDistribution: Record<string, number>;
  calculatedAt: string;
}

/** `CompletenessScore`, returned unprojected by `/completeness/low`. */
export interface QualityCompletenessScore {
  symbol: string;
  date: string;
  score: number;
  expectedEvents: number;
  actualEvents: number;
  missingEvents: number;
  /** `TimeSpan` as `"hh:mm:ss"`. */
  tradingDuration: string;
  coveredDuration: string;
  coveragePercent: number;
  calculatedAt: string;
  /** Computed server-side from `score`. */
  grade: string;
}

/** `QualityAnomalyResponse`. */
export interface QualityAnomaly {
  id: string;
  timestamp: string;
  symbol: string;
  /** `AnomalyType` ordinal. */
  type: number;
  /** `AnomalySeverity` ordinal. */
  severity: number;
  description: string;
  expectedValue: number;
  actualValue: number;
  deviationPercent: number;
  zScore: number;
  provider?: string | null;
  isAcknowledged: boolean;
  detectedAt: string;
}

// --- Cross-provider tolerance policy (/api/quality/cross-provider/*) ---

export type CrossProviderVolumeMode = "Alert" | "Informational" | "Off";

/** Unset fields inherit the policy defaults. */
export interface CrossProviderToleranceOverride {
  priceTolerancePercent?: number | null;
  quoteTolerancePercent?: number | null;
  volumeTolerancePercent?: number | null;
  volumeMode?: CrossProviderVolumeMode | null;
  backfillCloseTolerancePercent?: number | null;
}

export interface CrossProviderTolerancePolicy {
  priceTolerancePercent: number;
  quoteTolerancePercent: number;
  volumeTolerancePercent: number;
  volumeMode: CrossProviderVolumeMode;
  /** null compares every bar. */
  backfillBarsToCompare: number | null;
  backfillCloseTolerancePercent: number;
  assetClassOverrides?: Record<string, CrossProviderToleranceOverride> | null;
}

export interface CrossProviderToleranceBounds {
  maxPriceTolerancePercent: number;
  maxVolumeTolerancePercent: number;
  maxBackfillBarsToCompare: number;
  maxAssetClassOverrides: number;
  maxPreviewDays: number;
}

export interface CrossProviderTolerancePolicyAudit {
  id: string;
  changedAt: string;
  actor: string;
  reason: string | null;
  loosened: boolean;
  before: CrossProviderTolerancePolicy;
  after: CrossProviderTolerancePolicy;
}

export interface CrossProviderTolerancePolicyResponse {
  policy: CrossProviderTolerancePolicy;
  defaults: CrossProviderTolerancePolicy;
  bounds: CrossProviderToleranceBounds;
  audit: CrossProviderTolerancePolicyAudit[];
  previewProviders: string[];
  providerCallsPerPreview: number;
}

export interface CrossProviderTolerancePolicySaveResponse {
  policy: CrossProviderTolerancePolicy;
  audit: CrossProviderTolerancePolicyAudit;
  changeImpact: ConfigurationChangeStatus;
}

export interface CrossProviderTolerancePreviewRequest {
  symbol: string;
  baselineProvider: string;
  comparisonProvider: string;
  days: number;
  policy: CrossProviderTolerancePolicy;
  /** Optional assertion about the registry result. */
  assetClass?: string | null;
}

export interface CrossProviderTolerancePreviewDay {
  sessionDate: string;
  baselineClose: number | null;
  comparisonClose: number | null;
  closeDifferencePercent: number | null;
  volumeDifferencePercent: number | null;
  flaggedBy: "Price" | "Volume";
}

export interface CrossProviderTolerancePreviewResponse {
  symbol: string;
  from: string;
  to: string;
  baselineProvider: string;
  comparisonProvider: string;
  resolvedAssetClass: string | null;
  tolerances: Omit<CrossProviderTolerancePolicy, "backfillBarsToCompare" | "assetClassOverrides">;
  flaggedDays: number;
  flagged: CrossProviderTolerancePreviewDay[];
  informationalDays: number;
  missingSessions: number;
  providerErrors: string[];
  providerCallsUsed: number;
}
