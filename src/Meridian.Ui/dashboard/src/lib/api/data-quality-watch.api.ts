/**
 * Client functions for the `/api/quality/*` rollup routes.
 *
 * Thin wrappers over the shared workstation API client so errors, aborts, and
 * no-host fixture semantics stay aligned with the rest of the dashboard.
 */

import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import {
  qualityCompletenessSummaryEndpoint,
  qualityHealthEndpoint,
  qualityHighLatencySymbolsEndpoint,
  qualityLatencyStatisticsEndpoint,
  qualityLowCompletenessEndpoint,
  qualityStaleSymbolsEndpoint,
  qualityTopErrorSymbolsEndpoint,
  qualityUnacknowledgedAnomaliesEndpoint,
  qualityUnhealthySymbolsEndpoint
} from "@/lib/workstation-endpoints";
import type {
  QualityAnomaly,
  QualityCompletenessScore,
  QualityCompletenessSummary,
  QualityHealthSnapshot,
  QualityHighLatencySymbol,
  QualityLatencyStatistics,
  QualitySymbolHealth,
  QualityTopErrorSymbol
} from "@/types/data-quality-watch.types";

export function getQualityHealth(options: ApiRequestOptions = {}): Promise<QualityHealthSnapshot> {
  return apiGetJson<QualityHealthSnapshot>(qualityHealthEndpoint(), options);
}

export function getUnhealthyQualitySymbols(options: ApiRequestOptions = {}): Promise<QualitySymbolHealth[]> {
  return apiGetJson<QualitySymbolHealth[]>(qualityUnhealthySymbolsEndpoint(), options);
}

export function getQualityLatencyStatistics(options: ApiRequestOptions = {}): Promise<QualityLatencyStatistics> {
  return apiGetJson<QualityLatencyStatistics>(qualityLatencyStatisticsEndpoint(), options);
}

export function getHighLatencyQualitySymbols(
  thresholdMs?: number,
  options: ApiRequestOptions = {}
): Promise<QualityHighLatencySymbol[]> {
  return apiGetJson<QualityHighLatencySymbol[]>(qualityHighLatencySymbolsEndpoint(thresholdMs), options);
}

export function getTopErrorQualitySymbols(
  count?: number,
  options: ApiRequestOptions = {}
): Promise<QualityTopErrorSymbol[]> {
  return apiGetJson<QualityTopErrorSymbol[]>(qualityTopErrorSymbolsEndpoint(count), options);
}

export function getQualityCompletenessSummary(
  options: ApiRequestOptions = {}
): Promise<QualityCompletenessSummary> {
  return apiGetJson<QualityCompletenessSummary>(qualityCompletenessSummaryEndpoint(), options);
}

export function getLowCompletenessQualitySymbols(
  query: { date?: string; threshold?: number } = {},
  options: ApiRequestOptions = {}
): Promise<QualityCompletenessScore[]> {
  return apiGetJson<QualityCompletenessScore[]>(qualityLowCompletenessEndpoint(query), options);
}

export function getUnacknowledgedQualityAnomalies(
  count?: number,
  options: ApiRequestOptions = {}
): Promise<QualityAnomaly[]> {
  return apiGetJson<QualityAnomaly[]>(qualityUnacknowledgedAnomaliesEndpoint(count), options);
}

/** Symbols the detector has stopped seeing data for; the route returns bare tickers. */
export function getStaleQualitySymbols(options: ApiRequestOptions = {}): Promise<string[]> {
  return apiGetJson<string[]>(qualityStaleSymbolsEndpoint(), options);
}
