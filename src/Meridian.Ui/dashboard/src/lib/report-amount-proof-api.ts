import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import { governedReportingRunAmountsPath } from "@/lib/reporting-governance-routes";
import type { ReportLedgerAmountBinding } from "@/types/report-amount-proof";

export function getReportAmountBindings(runId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<ReportLedgerAmountBinding[]>(governedReportingRunAmountsPath(runId), {
    ...options,
    allowDevelopmentFallback: false
  });
}
