import { FUND_STRUCTURE_API_ENDPOINTS, routeWithParam } from "@/lib/workstation-endpoints";

// Per-schedule reporting endpoints. Kept apart from workstation-endpoints.ts, which is at its
// file-size cap.
export function reportingSchedulePauseEndpoint(scheduleId: string): string {
  return routeWithParam(FUND_STRUCTURE_API_ENDPOINTS.reportingSchedulePause, "scheduleId", scheduleId);
}

export function reportingScheduleResumeEndpoint(scheduleId: string): string {
  return routeWithParam(FUND_STRUCTURE_API_ENDPOINTS.reportingScheduleResume, "scheduleId", scheduleId);
}

export function reportingScheduleDisableEndpoint(scheduleId: string): string {
  return routeWithParam(FUND_STRUCTURE_API_ENDPOINTS.reportingScheduleDisable, "scheduleId", scheduleId);
}

export function reportingScheduleDeleteEndpoint(scheduleId: string): string {
  return routeWithParam(FUND_STRUCTURE_API_ENDPOINTS.reportingScheduleDelete, "scheduleId", scheduleId);
}

export function reportingScheduleRunNowEndpoint(scheduleId: string): string {
  return routeWithParam(FUND_STRUCTURE_API_ENDPOINTS.reportingScheduleRunNow, "scheduleId", scheduleId);
}
