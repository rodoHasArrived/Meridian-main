import type { ProviderIntegrationCapabilityKind, ProviderIntegrationProcessingStatus, ProviderIntegrationValidationIssue } from "./workstation-7";

export interface ProviderIntegrationManifestReference {
  manifestId: string;
  manifestVersion: number;
  contentDigest: string;
}

export type ProviderIntegrationReplayMode = "Original" | "Remediation";

export interface ProviderIntegrationManifestProvenance {
  /** Absent for legacy evidence whose exact mapping cannot be verified. */
  manifestReference?: ProviderIntegrationManifestReference | null;
  originalManifestReference?: ProviderIntegrationManifestReference | null;
  sourceSyncRunId?: string | null;
  replayMode?: ProviderIntegrationReplayMode | null;
}

export interface RawIngestionPayload extends ProviderIntegrationManifestProvenance {
  payloadId: string;
  providerId: string;
  connectionId: string;
  capability: ProviderIntegrationCapabilityKind;
  endpointKey: string;
  syncRunId: string;
  receivedAt: string;
  requestMetadata: Record<string, string>;
  rawPayload: unknown;
  mappingVersion: string;
  processingStatus: ProviderIntegrationProcessingStatus;
}

export interface ProviderIntegrationSyncRun extends ProviderIntegrationManifestProvenance {
  syncRunId: string;
  manifestId: string;
  connectionId: string;
  providerId: string;
  capability: ProviderIntegrationCapabilityKind;
  endpointKey: string;
  startedAt: string;
  completedAt?: string | null;
  status: ProviderIntegrationProcessingStatus;
  recordsReceived: number;
  recordsAccepted: number;
  recordsQuarantined: number;
  rawPayloadId?: string | null;
  issues: ProviderIntegrationValidationIssue[];
}
