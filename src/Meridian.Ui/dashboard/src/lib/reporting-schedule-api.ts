import { deleteJson, postJson, type ApiRequestOptions } from "@/lib/api";
import { reportingScheduleDeleteEndpoint, reportingScheduleDisableEndpoint } from "@/lib/reporting-schedule-endpoints";
import type { ReportingScheduleRecord } from "@/types";

// Schedule lifecycle calls added after lib/api.ts reached its file-size cap.

export function disableReportingSchedule(scheduleId: string, options: ApiRequestOptions = {}) {
  return postJson<ReportingScheduleRecord>(reportingScheduleDisableEndpoint(scheduleId), undefined, options);
}

export function deleteReportingSchedule(scheduleId: string, options: ApiRequestOptions = {}) {
  return deleteJson<ReportingScheduleRecord>(reportingScheduleDeleteEndpoint(scheduleId), options);
}
