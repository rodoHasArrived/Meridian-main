export interface AgreementWorkQueueScope {
  ledgerBookId: string;
  fundProfileId?: string | null;
  asOf: string;
  expiryHorizonDays: number;
  entityId?: string | null;
}
export type AgreementWorkItemState = "Open" | "AwaitingReview" | "Resolved" | "Reopened";
export type AgreementWorkItemAction = "AssignOwner" | "AddNote" | "AwaitReview" | "Resolve" | "Reopen";
export interface AgreementWorkItemHistory {
  commandId: string; version: number; recordHash: string; action: string;
  state: AgreementWorkItemState; actor: string; recordedAtUtc: string; reason: string;
  owner: string | null; note: string | null; evidenceReferences: string[];
  observedAsOf: string; sourceFingerprint: string;
  sourceVersions?: { sourceKind: string; sourceId: string; version: string; contentHash: string }[];
}
export interface AgreementWorkItem {
  caseId: string; ledgerBookId: string; agreementId: string; agreementVersion: number | null;
  findingCode: string; severity: string; message: string; suggestedAction: string;
  dueDate: string | null; observedAsOf: string; isCurrentlyObserved: boolean; isTracked: boolean;
  state: AgreementWorkItemState; owner: string | null; firstSeenAtUtc: string | null;
  lastObservedAtUtc: string | null; ageDays: number | null; daysOverdue: number | null;
  lastObservedSourceFingerprint: string; version: number; recordHash: string | null;
  history: AgreementWorkItemHistory[];
}
export interface GuaranteeDisclosure {
  agreementId: string; code: string; name: string; agreementVersion: number; payloadHash: string;
  partyOneId: string; partyOneRole: string; partyTwoId: string; partyTwoRole: string;
  currency: string; beneficiaryReference: string | null; limitAmount: string | null;
  effectiveFrom: string; effectiveTo: string | null; terminationEffectiveDate: string | null;
  effectiveStatus: string; currentExposure: string | null; exposureStatus: string; exposureUnavailableReason: string;
}
export interface AgreementWorkQueue {
  ledgerBookId: string; fundProfileId: string | null;
  asOf: string; expiryHorizonDays: number; evaluatedAtUtc: string;
  status: string; isComplete: boolean; sourceFingerprint: string; canManage: boolean;
  cases: AgreementWorkItem[]; guarantees: GuaranteeDisclosure[]; diagnostics: string[];
}
export interface ObserveAgreementWorkItem {
  commandId: string; caseId: string; expectedSourceFingerprint: string; reason: string;
  expectedVersion: number; expectedRecordHash: string | null;
}
export interface UpdateAgreementWorkItem {
  commandId: string; caseId: string; action: AgreementWorkItemAction;
  expectedVersion: number; expectedRecordHash: string; expectedSourceFingerprint: string;
  reason: string; owner?: string | null; note?: string | null; evidenceReferences?: string[] | null;
}
