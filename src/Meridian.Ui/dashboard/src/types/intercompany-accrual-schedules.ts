// Mirrors src/Meridian.Contracts/Ledger/IntercompanyAccrualScheduleDtos.cs (camelCase JSON, string enums).

export type IntercompanyAccrualScheduleKind = "LoanInterest";
export type IntercompanyAccrualScheduleStatus = "Proposed" | "Active" | "Rejected" | "Disabled";
export type IntercompanyAccrualScheduleAction = "Propose" | "Approve" | "Reject" | "Disable";
export type IntercompanyAccrualScheduleDecision = Exclude<IntercompanyAccrualScheduleAction, "Propose">;
export type IntercompanyAccrualScheduleOutcome = "Due" | "Drafted" | "AlreadyDrafted" | "Refused";

export interface IntercompanyAccrualScheduleEvent {
  eventId: string; requestId: string; action: IntercompanyAccrualScheduleAction; actor: string; recordedAt: string; reason?: string | null;
}

/** An opt-in, reviewed instruction to draft one book's governed accruals. It drafts only; nothing posts. */
export interface IntercompanyAccrualSchedule {
  scheduleId: string; tenantId: string; companyId?: string | null; ledgerBookId: string; kind: IntercompanyAccrualScheduleKind;
  profileArrangementId: string; principalArrangementId?: string | null; version: number; status: IntercompanyAccrualScheduleStatus;
  activeFrom?: string | null; events: IntercompanyAccrualScheduleEvent[];
}

export interface ProposeIntercompanyAccrualScheduleRequest {
  requestId: string; ledgerBookId: string; kind: IntercompanyAccrualScheduleKind; profileArrangementId: string;
  principalArrangementId: string | null; reason: string;
}

export interface DecideIntercompanyAccrualScheduleRequest {
  requestId: string; action: IntercompanyAccrualScheduleDecision; expectedVersion: number; reason: string | null;
}

export interface IntercompanyAccrualScheduleWindow {
  accrualFrom: string; accrualTo: string; outcome: IntercompanyAccrualScheduleOutcome;
  draftId?: string | null; draftStatus?: string | null; reason?: string | null;
}

/** Due periods as of a date (dry run) or what an executed run drafted and why anything was refused. */
export interface IntercompanyAccrualScheduleRun {
  scheduleId: string; asOf: string; executed: boolean; windows: IntercompanyAccrualScheduleWindow[];
}
