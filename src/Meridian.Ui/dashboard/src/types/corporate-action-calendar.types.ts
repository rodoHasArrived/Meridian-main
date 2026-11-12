import type { CorporateActionCaseScope, CorporateActionProcessingCaseDto, CorporateActionCaseSourceSnapshotDto } from "./workstation-7";

export interface CorporateActionElectionDeadline {
  seriesId: string;
  kind: "IssuerElection" | "BrokerElection" | "CustodianElection";
  sourceParty: string;
  date?: string | null;
  at?: string | null;
  timeZoneId?: string | null;
  financialAccountId?: string | null;
  custodyAccountId?: string | null;
  supersedesEvidenceId?: string | null;
  withdrawn: boolean;
  sourceText?: string | null;
}
export interface CorporateActionDeadlineHistory {
  evidenceId: string; deadline: CorporateActionElectionDeadline; evidenceReference: string;
  evidenceHash?: string | null; rationale?: string | null; caseVersion: number;
  recordedBy: string; recordedAtUtc: string; isCurrent: boolean;
  evidenceAssurance?: string;
}
export interface CorporateActionCaseDeadlines {
  case: CorporateActionProcessingCaseDto; history: CorporateActionDeadlineHistory[]; canRecord: boolean; blocker?: string | null;
}
export interface CorporateActionCalendarEvent {
  eventId: string; securityId: string; ticker: string; actionType: string; proposalId: string;
  dateKind: string; date: string; at?: string | null;
  cases: Array<{ caseId: string; version: number; state: string; scope: CorporateActionCaseScope }>;
  source: CorporateActionCaseSourceSnapshotDto["providerIdentity"]; proposalState: string;
  deadline?: CorporateActionDeadlineHistory | null;
}
export interface CorporateActionCalendarPage {
  from: string; to: string; generatedAtUtc: string; revision: string; items: CorporateActionCalendarEvent[];
  nextCursor?: string | null; totalCount: number; coverage: string;
}
export interface RecordCorporateActionDeadlineRequest {
  caseId: string; expectedVersion: number; idempotencyKey: string; tenantId: string; companyId: string;
  deadline: CorporateActionElectionDeadline; evidenceReference: string; evidenceHash: string; rationale: string;
  scopeAssertion: CorporateActionCaseScope;
}
export interface CorporateActionDeadlineImportRequest {
  caseId: string; expectedVersion: number; idempotencyKey: string; tenantId: string; companyId: string;
  csv: string; previewHash?: string | null; scopeAssertion: CorporateActionCaseScope;
}
export interface CorporateActionDeadlineImportPreview {
  previewHash: string; expectedVersion: number;
  rows: Array<{ row: number; deadline: CorporateActionElectionDeadline; evidenceReference: string; evidenceHash: string; rationale: string }>;
  errors: string[];
}
export interface CorporateActionDeadlineMutationResult {
  case: CorporateActionProcessingCaseDto;
  evidence: { evidenceId: string; caseId: string; evidenceKind: string; evidenceReference: string; evidenceHash?: string | null; description?: string | null; metadata: unknown; caseVersion: number; recordedBy: string; recordedAtUtc: string };
  replayed: boolean;
}
export interface CorporateActionDeadlineImportResult {
  committedCount: number; complete: boolean; results: CorporateActionDeadlineMutationResult[]; failedRow?: number | null; error?: string | null;
}
