import type { CloseReadinessProjection } from "./workstation-2";

/** Mirrors the shared OnboardingDtos; policy, lineage and readiness remain server-owned. */
export interface OnboardingScope {
  tenantId: string; companyId: string; entityId: string; fundProfileId: string;
  ledgerBookId: string; accountIds: string[]; startDate: string; endDate: string;
}
export interface OnboardingCriteria {
  requiredDates: string[]; requiredKinds: string[];
  balanceTolerance: number; positionTolerance: number; navTolerance: number;
  minimumCoveragePercent: number; requiredReviewerIds: string[]; minimumReviewers: number;
  requireCloseReadiness: boolean; reviewInstructions: string;
}
export interface CreateOnboardingWorkspaceRequest {
  name: string; scope: Omit<OnboardingScope, "tenantId" | "companyId">; criteria: OnboardingCriteria;
}
export interface CaptureOnboardingComparisonRequest {
  expectedVersion: number; asOfDate: string; providerId: string; importId: string;
  mappingProfileId: string; mappingVersion: string; notes?: string;
}
export interface OnboardingSourceSelection {
  providerId: string; importId: string; asOfDate: string; mappingProfileId: string; mappingVersion: string;
}
export interface OnboardingSourceSnapshot {
  snapshotId: string; sourceKind: string; sourceId: string; version: string; contentHash: string;
  capturedAtUtc: string; asOfDate: string; mappingVersion: string; payloadJson: string; evidenceIds: string[];
}
export interface OnboardingObservation {
  kind: string; accountId: string; currency: string; instrumentId: string | null;
  meridianAmount: number | null; externalAmount: number | null;
  snapshotIds: string[]; evidenceIds: string[]; missingReason?: string | null;
}
export interface OnboardingMissingSource {
  code: string; sourceKind: string; accountId: string | null; message: string;
}
export interface OnboardingDifference {
  differenceKey: string; kind: string; accountId: string; currency: string; instrumentId: string | null;
  meridianAmount: number | null; externalAmount: number | null; difference: number | null; tolerance: number;
  status: string; ownerId: string; firstSeenComparisonId: string; lastSeenComparisonId: string;
  evidenceIds: string[]; missingReason: string | null;
}
export interface OnboardingComparison {
  comparisonId: string; sequence: number; asOfDate: string; dataRevision: number;
  actorId: string; capturedAtUtc: string; notes: string | null; providerId: string; importId: string;
  mappingProfileId: string; mappingVersion: string; criteria: OnboardingCriteria;
  inputs: {
    snapshots: OnboardingSourceSnapshot[]; observations: OnboardingObservation[];
    missingSources: OnboardingMissingSource[]; closeReadiness: CloseReadinessProjection | null;
  };
  differences: OnboardingDifference[]; coveragePercent: number; contentHash: string;
  previousContentHash: string | null; algorithmVersion: string;
  /** Shadow-mode opening-balance comparison; omitted (false) for period comparisons. */
  isOpening?: boolean; openingSourceKind?: string | null;
}
export interface OnboardingReview {
  reviewId: string; dataRevision: number; reviewerId: string; decision: string;
  notes: string; evidenceIds: string[]; recordedAtUtc: string;
}
export interface OnboardingReadiness {
  status: string; isReady: boolean; unresolvedDifferenceCount: number; missingDates: string[];
  missingSources: OnboardingMissingSource[]; blockers: string[]; approvedReviewers: number; requiredReviewers: number;
  unresolvedDifferences: { comparisonId: string; asOfDate: string; difference: OnboardingDifference }[];
  periods?: OnboardingPeriodSummary[] | null; opening?: OnboardingOpeningBalanceReport | null;
  explainedDifferenceCount?: number;
}
export interface OnboardingPacketContent {
  workspaceId: string; name: string; ownerId: string; scope: OnboardingScope;
  workspaceVersion: number; dataRevision: number; criteria: OnboardingCriteria;
  criteriaHistory: { dataRevision: number; criteria: OnboardingCriteria; actorId: string; recordedAtUtc: string }[];
  comparisons: OnboardingComparison[]; currentDifferences: OnboardingDifference[];
  assignments: { differenceKey: string; ownerId: string; notes: string; evidenceIds: string[];
    actorId: string; recordedAtUtc: string; dataRevision: number }[];
  reviews: OnboardingReview[]; readiness: OnboardingReadiness; authorityPosture: string;
  explanations?: OnboardingDifferenceExplanation[] | null; shadowDesignation?: OnboardingShadowDesignation | null;
}
export interface OnboardingReadinessPacket {
  packetId: string; frozenAtUtc: string; frozenBy: string; content: OnboardingPacketContent;
  contentHash: string; hashAlgorithm: string;
}
export interface OnboardingWorkspace extends Omit<OnboardingPacketContent, "workspaceVersion"> {
  version: number; createdAtUtc: string; updatedAtUtc: string; packets: OnboardingReadinessPacket[];
  certificates?: OnboardingParallelRunCertificate[] | null; promotion?: ShadowBookPromotionDecision | null;
  shadowAudit?: OnboardingShadowAuditEntry[] | null;
}

/** Shadow-mode onboarding (W11-SHADOW-001); mirrors OnboardingShadowDtos. */
export type OnboardingShadowState = "Shadow" | "Promoted" | "Withdrawn";
export const ONBOARDING_DIFFERENCE_CLASSIFICATIONS = ["Timing", "Pricing", "Treatment", "SourceData", "MeridianError"] as const;
export type OnboardingDifferenceClassification = typeof ONBOARDING_DIFFERENCE_CLASSIFICATIONS[number];
export const ONBOARDING_OPENING_SOURCE_KINDS = ["StatementOpening", "AdministratorPack", "GlTrialBalance"] as const;
export type OnboardingOpeningSourceKind = typeof ONBOARDING_OPENING_SOURCE_KINDS[number];
export interface OnboardingShadowDesignation {
  state: OnboardingShadowState; ledgerBookId: string; designatedBy: string; designatedAtUtc: string; rationale: string;
  resolvedBy?: string | null; resolvedAtUtc?: string | null; promotionDecisionId?: string | null; withdrawalReason?: string | null;
}
export interface OnboardingOpeningAccountMapping { externalAccount: string; accountId: string; }
export interface ImportOnboardingOpeningBalancesRequest {
  expectedVersion: number; sourceKind: OnboardingOpeningSourceKind; mappingProfileId: string; mappingVersion: string;
  accountMappings: OnboardingOpeningAccountMapping[]; fileName?: string; contentBase64?: string;
  providerId?: string; importId?: string; notes?: string;
}
export interface OnboardingOpeningBalanceLine {
  differenceKey: string; kind: string; accountId: string; currency: string; instrumentId: string | null;
  meridianAmount: number | null; incumbentAmount: number | null; difference: number | null; tolerance: number;
  status: "Matched" | "Explained" | "Break" | "Missing"; classification: string | null; missingReason: string | null;
}
export interface OnboardingOpeningBalanceReport {
  comparisonId: string; sourceKind: string; asOfDate: string; sourceContentHash: string;
  matchedCount: number; explainedCount: number; breakCount: number; missingCount: number; isReconciled: boolean;
  lines: OnboardingOpeningBalanceLine[]; evidenceIds: string[];
}
export interface OnboardingDifferenceExplanation {
  explanationId: string; comparisonId: string; differenceKey: string; classification: string; notes: string;
  evidenceIds: string[]; expectedReversalDate: string | null; meridianAmount: number | null; externalAmount: number | null;
  actorId: string; recordedAtUtc: string; dataRevision: number;
}
export interface OnboardingPeriodSummary {
  asOfDate: string; comparisonId: string; navCurrency: string | null;
  meridianNav: number | null; externalNav: number | null; navVariance: number | null;
  observationCount: number; matchedCount: number; explainedCount: number; unexplainedCount: number; missingSourceCount: number;
  explainedByClassification: { classification: string; count: number }[]; isClean: boolean;
}
export interface OnboardingParallelRunCertificate {
  certificateId: string; issuedAtUtc: string; issuedBy: string; contentHash: string; hashAlgorithm: string;
  content: {
    workspaceId: string; name: string; scope: OnboardingScope; dataRevision: number;
    opening: OnboardingOpeningBalanceReport; periods: OnboardingPeriodSummary[]; cleanPeriods: string[];
    explanations: OnboardingDifferenceExplanation[]; approvingReviews: OnboardingReview[];
    packetId: string; packetContentHash: string; statement: string;
  };
}
export interface ShadowBookPromotionDecision {
  decisionId: string; certificateId: string; certificateContentHash: string; decidedBy: string; decidedAtUtc: string;
  rationale: string; evidenceIds: string[]; dataRevision: number;
}
export interface OnboardingShadowAuditEntry {
  sequence: number; action: string; actorId: string; occurredAtUtc: string; subjectId: string; detail: string;
  previousHash: string | null; entryHash: string;
}
export interface BookOfRecordStatus {
  ledgerBookId: string; isBookOfRecord: boolean; shadowState: string | null; shadowWorkspaceId: string | null; reason: string;
  cleanPeriodCount?: number | null; requiredPeriodCount?: number | null; unexplainedDifferenceCount?: number | null;
}
