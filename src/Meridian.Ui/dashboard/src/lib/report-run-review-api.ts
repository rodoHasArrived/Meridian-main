import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import { governedReportingRunPath } from "@/lib/reporting-governance-routes";

export interface ReportReviewChange { key: string; previous: string | null; current: string | null; }
export interface ReportReviewLine {
  gridId: string; rowKey: string; column: string; state: string;
  previous: string | null; current: string | null; delta: string | null; percent: string | null;
  evidenceIds: string[];
  materialityOutcome?: "NotAssessed" | "WithinTolerance" | "MaterialException";
}
export interface ReportRunReview {
  reviewId: string; runId: string; version: number; snapshotHash: string;
  periodId: string; currency: string | null; accountingBasis: string | null;
  baselineRunId: string | null; baselineVersion: number | null;
  parameters: ReportReviewChange[]; sourceChanges: ReportReviewChange[]; lines: ReportReviewLine[];
  materialityOutcome: "NotAssessed" | "WithinTolerance" | "MaterialException";
  materialChangeCount: number | null; unavailableReasons: string[];
  materialityPolicy?: { policyId: string; version: string; currency: string;
    metrics: { gridId: string; column: string; absoluteVariance: string | null; relativeVariancePercent: string | null }[] } | null;
}

export async function getReportRunReview(runId: string, options: ApiRequestOptions = {}): Promise<ReportRunReview> {
  const result = await apiGetJson<ReportRunReview>(`${governedReportingRunPath(runId)}/review`,
    { ...options, allowDevelopmentFallback: false });
  if (!result || result.runId !== runId || !/^[a-f0-9]{64}$/.test(result.reviewId)
      || !Number.isSafeInteger(result.version) || result.version <= 0
      || typeof result.snapshotHash !== "string"
      || !Array.isArray(result.parameters) || !Array.isArray(result.sourceChanges)
      || !Array.isArray(result.lines) || !Array.isArray(result.unavailableReasons)
      || !["NotAssessed", "WithinTolerance", "MaterialException"].includes(result.materialityOutcome)
      || !(result.materialChangeCount === null || Number.isSafeInteger(result.materialChangeCount))) {
    throw new Error("The server returned invalid report review evidence.");
  }
  return result;
}

export function getRetainedReportRunReview(runId: string, reviewId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<ReportRunReview>(`${governedReportingRunPath(runId)}/review/${encodeURIComponent(reviewId)}`,
    { ...options, allowDevelopmentFallback: false });
}
