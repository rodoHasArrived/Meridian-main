import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";

export interface SecurityRateObservation {
  provider: string;
  seriesId: string;
  value: string;
  originalValue: string;
  unit: "Fraction" | "Percent" | "BasisPoints" | "IndexLevel";
  sourceUnits: string;
  observationDate: string;
  publishedAt: string | null;
  vintageDate: string | null;
  vintageEndDate: string | null;
  retrievedAt: string;
  transformation: string;
  kind: "ObservedFixing" | "ForecastRate" | "Assumption" | "TreasuryConstantMaturityYield" | "EconomicObservation";
  currency: string | null;
}

export interface RateCurveDefinition { curveId: string; curveKey: string; kind: string; currency: string; indexName: string | null }
export type RateConstructionMethod = "DirectForwardRates" | "ObservedFixingFlatForecast" | "ReviewedDiscountFactors";
export interface RateIngestionRequest {
  curveId: string;
  asOf: string;
  constructionMethod: RateConstructionMethod;
  methodDescription: string;
  points: Array<{ pointDate: string; observation: SecurityRateObservation; reviewedDiscountFactor?: string }>;
}
export interface RateCurveVersion {
  curveVersionId: string; curveKey: string; version: number; contentSha256: string;
  points: Array<{ date: string; value: string; rateEvidence?: { observation: SecurityRateObservation; constructionMethod: RateConstructionMethod; methodDescription: string; reviewedBy: string } }>;
}

export function getRateObservations(seriesId: string, from: string, to: string, options: ApiRequestOptions = {}) {
  const query = new URLSearchParams({ provider: "fred", seriesId, from, to });
  return apiGetJson<SecurityRateObservation[]>(`${UI_API_ROUTES.SecurityMasterRateObservations}?${query}`, { ...options, allowDevelopmentFallback: false });
}
export function getRateCurveDefinitions(options: ApiRequestOptions = {}) {
  return apiGetJson<RateCurveDefinition[]>("/api/security-master/curves", { ...options, allowDevelopmentFallback: false });
}
export function ingestRates(request: RateIngestionRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<RateCurveVersion>(UI_API_ROUTES.SecurityMasterRateIngest, request, { ...options, allowDevelopmentFallback: false });
}
