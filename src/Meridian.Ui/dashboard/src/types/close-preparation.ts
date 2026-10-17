import type { ClosePeriodPlan, CloseTaskConfiguration, CloseTaskDependencyConfiguration, CloseTaskSignOffRequirementConfiguration, MaterialityPolicy } from "./workstation-3";
import type { LedgerBook } from "./workstation-5";
import type { LedgerPeriod } from "./workstation-6";

/** Mirrors Meridian.Contracts.Ledger.AccountingClosePreparationDtos. Dates remain calendar dates. */
export interface CloseDeadlineRule {
  taskId: string;
  anchor: "PeriodStart" | "PeriodEnd";
  offsetDays: number;
  dayCount: "CalendarDays" | "BusinessDays";
  adjustment: "None" | "FollowingBusinessDay" | "PrecedingBusinessDay";
}

export interface ClosePreparationCalendar {
  calendarId: string;
  version: string;
  weekendDays: number[];
  holidays: string[];
}

export interface ClosePreparationHistory {
  eventType: string;
  occurredAtUtc: string;
  actor: string;
  description: string;
}

export interface ClosePlanPreparationLineage {
  templateId: string;
  templateVersion: number;
  sourceWorkflowId: string;
  targetPeriodId: string;
  periodStart: string;
  periodEnd: string;
  createdAtUtc: string;
  createdBy: string;
  history: ClosePreparationHistory[];
}

export interface ClosePlanTemplate {
  templateId: string;
  version: number;
  name: string;
  sourceWorkflowId: string;
  sourceLedgerBookId: string;
  sourcePeriodId: string;
  fundAccountId: string;
  fundProfileId: string;
  sourceAccountingPolicyId: string;
  sourceAccountingPolicyVersion: string;
  materialityPolicy: MaterialityPolicy;
  calendar: ClosePreparationCalendar;
  tasks: { configuration: CloseTaskConfiguration; sourceDueDate: string; deadlineRule: CloseDeadlineRule }[];
  capturedAtUtc: string;
  capturedBy: string;
  history: ClosePreparationHistory[];
}

export interface CaptureClosePlanTemplateRequest {
  sourceWorkflowId: string;
  name: string;
  deadlineRules: CloseDeadlineRule[];
  calendar: ClosePreparationCalendar;
  templateId?: string;
}

export interface PreviewClosePreparationRequest {
  templateId: string;
  templateVersion: number;
  targetLedgerBookId: string;
  targetPeriodId: string;
  ownerMappings: { sourceOwner: string; targetOwner: string }[];
  acknowledgePolicyChange: boolean;
}

export interface ClosePreparationPreview {
  previewId: string;
  templateId: string;
  templateVersion: number;
  sourceWorkflowId: string;
  targetBook: LedgerBook;
  targetPeriod: LedgerPeriod;
  calendar: ClosePreparationCalendar;
  tasks: {
    taskId: string;
    displayName: string;
    sourceOwner: string;
    owner: string;
    sourceDueDate: string;
    dueDate: string;
    deadlineRule: CloseDeadlineRule;
    dependencies: CloseTaskDependencyConfiguration[];
    signOffRequirements: CloseTaskSignOffRequirementConfiguration[];
    ownerChanged: boolean;
  }[];
  issues: { code: string; message: string; isBlocking: boolean; taskId?: string | null }[];
  policyChanged: boolean;
  canCreate: boolean;
  createdAtUtc: string;
  expiresAtUtc: string;
}

export interface PreparedClosePlanResult {
  workflowId: string;
  templateId: string;
  templateVersion: number;
  sourceWorkflowId: string;
  targetLedgerBookId: string;
  targetPeriodId: string;
  createdAtUtc: string;
  createdBy: string;
  wasAlreadyCreated: boolean;
  plan: ClosePeriodPlan;
  history: ClosePreparationHistory[];
}
