import type { ApiRequestOptions } from "@/lib/api";
import { FUND_STRUCTURE_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import type { ReportingRunReadiness, ReportingRunRequest, ReportingRunResult } from "@/types";

type PostJson = <T>(path: string, body?: unknown, options?: ApiRequestOptions) => Promise<T>;

export function createReportingRunsApi(postJson: PostJson) {
  return {
    runReportingNow(request: ReportingRunRequest, options: ApiRequestOptions = {}) {
      return postJson<ReportingRunResult>(FUND_STRUCTURE_API_ENDPOINTS.reportingRuns, request, options);
    },
    assessReportingRunReadiness(request: ReportingRunRequest, options: ApiRequestOptions = {}) {
      return postJson<ReportingRunReadiness>(FUND_STRUCTURE_API_ENDPOINTS.reportingRunReadiness, request, options);
    }
  };
}
