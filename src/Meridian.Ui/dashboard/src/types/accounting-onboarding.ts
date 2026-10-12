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
}
export interface OnboardingReview {
  reviewId: string; dataRevision: number; reviewerId: string; decision: string;
  notes: string; evidenceIds: string[]; recordedAtUtc: string;
}
export interface OnboardingReadiness {
  status: string; isReady: boolean; unresolvedDifferenceCount: number; missingDates: string[];
  missingSources: OnboardingMissingSource[]; blockers: string[]; approvedReviewers: number; requiredReviewers: number;
  unresolvedDifferences: { comparisonId: string; asOfDate: string; difference: OnboardingDifference }[];
}
export interface OnboardingPacketContent {
  workspaceId: string; name: string; ownerId: string; scope: OnboardingScope;
  workspaceVersion: number; dataRevision: number; criteria: OnboardingCriteria;
  criteriaHistory: { dataRevision: number; criteria: OnboardingCriteria; actorId: string; recordedAtUtc: string }[];
  comparisons: OnboardingComparison[]; currentDifferences: OnboardingDifference[];
  assignments: { differenceKey: string; ownerId: string; notes: string; evidenceIds: string[];
    actorId: string; recordedAtUtc: string; dataRevision: number }[];
  reviews: OnboardingReview[]; readiness: OnboardingReadiness; authorityPosture: string;
}
export interface OnboardingReadinessPacket {
  packetId: string; frozenAtUtc: string; frozenBy: string; content: OnboardingPacketContent;
  contentHash: string; hashAlgorithm: string;
}
export interface OnboardingWorkspace extends Omit<OnboardingPacketContent, "workspaceVersion"> {
  version: number; createdAtUtc: string; updatedAtUtc: string; packets: OnboardingReadinessPacket[];
}
