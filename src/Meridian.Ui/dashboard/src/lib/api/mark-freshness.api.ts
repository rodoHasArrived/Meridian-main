import { apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { DailyValuationScheduleWorkItem, ValuationFreshnessPreviewDto } from "@/types";

export function previewValuationMarks(schedule: DailyValuationScheduleWorkItem, options: ApiRequestOptions = {}) {
  return apiPostJson<ValuationFreshnessPreviewDto>(UI_API_ROUTES.LedgerJournalAutomationDailyMarkToMarketPreview,
    { ...schedule, asOf: schedule.nextRunAtUtc }, options);
}
