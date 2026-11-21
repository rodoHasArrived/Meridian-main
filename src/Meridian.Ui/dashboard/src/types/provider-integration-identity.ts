/** A reviewed security choice bound to the complete identifier matching evidence. */
export interface ProviderIntegrationIdentityResolutionRequest {
  syncRunId: string;
  stagingRecordId: string;
  selectedSecurityId: string;
  evidenceHash: string;
  reason: string;
}

/** The server retains the authenticated reviewer and time with the immutable decision. */
export interface ProviderIntegrationIdentityDecision extends ProviderIntegrationIdentityResolutionRequest {
  decisionId: string;
  connectionId: string;
  reviewedBy: string;
  reviewedAt: string;
}
