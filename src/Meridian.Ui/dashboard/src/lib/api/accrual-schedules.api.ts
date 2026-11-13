import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { RecurringJournalScope } from "@/lib/api/recurring-journals.api";
export type AccrualCadence = "Daily" | "Weekly" | "Monthly" | "Quarterly" | "SemiAnnually" | "Annually";
export type AccrualAccountType = "Asset" | "Liability" | "Equity" | "Revenue" | "Expense";
export interface AccrualScheduleInput extends RecurringJournalScope {
  scheduleId: string; currency: string; cadence: AccrualCadence; anchorDate: string; endsOn: string;
  amount: string; debitAccount: string; debitAccountType: AccrualAccountType;
  creditAccount: string; creditAccountType: AccrualAccountType; description: string;
  evidence: Array<Record<string, unknown>>; expectedScheduleVersion: number; expectedTemplateVersion: number;
}
export interface AccrualScheduleRegistry {
  canConfigure: boolean;
  schedules: Array<{ scheduleId: string; version: number; templateVersion: number; anchorDate: string;
    endsOn: string | null; cadence: AccrualCadence; description: string | null; currency: string }>;
}
export interface AccrualSchedulePreview {
  fingerprint: string; currency: string;
  occurrences: Array<{ effectiveDate: string; periodId: string; version: number; isOpen: boolean;
    blocker: string | null; lockOwner: string | null; reopenPath: string | null;
    amount: string; debitAccount: string; creditAccount: string }>;
}
export function getAccrualSchedules(scope: RecurringJournalScope, options: ApiRequestOptions = {}) {
  return apiGetJson<AccrualScheduleRegistry>(`${UI_API_ROUTES.LedgerAccrualSchedules}?${new URLSearchParams({ ...scope })}`,
    { ...options, allowDevelopmentFallback: false });
}
export async function previewAccrualSchedule(input: AccrualScheduleInput, options: ApiRequestOptions = {}) {
  const result = await apiPostJson<AccrualSchedulePreview>(UI_API_ROUTES.LedgerAccrualSchedulePreview, input,
    { ...options, allowDevelopmentFallback: false });
  if (!/^[a-f0-9]{64}$/i.test(result.fingerprint) || result.currency !== input.currency || !Array.isArray(result.occurrences)
    || result.occurrences.some(row => typeof row.amount !== "string" || row.amount !== input.amount
      || row.debitAccount !== input.debitAccount || row.creditAccount !== input.creditAccount)) {
    throw new Error("The accrual preview does not match the reviewed inputs or exact decimal contract.");
  }
  return result;
}
export function configureAccrualSchedule(input: AccrualScheduleInput, previewFingerprint: string, options: ApiRequestOptions = {}) {
  return apiPostJson<{ scheduleId: string; version: number; contentHash: string }>(UI_API_ROUTES.LedgerAccrualScheduleConfigure,
    { input, previewFingerprint }, { ...options, allowDevelopmentFallback: false });
}
export function runAccrualSchedule(scheduleId: string, scope: RecurringJournalScope, expectedVersion: number, options: ApiRequestOptions = {}) {
  return apiPostJson<unknown>(UI_API_ROUTES.LedgerAccrualScheduleRun.replace("{scheduleId}", encodeURIComponent(scheduleId)),
    { ...scope, expectedVersion }, { ...options, allowDevelopmentFallback: false });
}
