/** Shared contracts for provider credential ownership administration (tenant and actor are server-resolved). */

export interface CreateOwnedProviderConnectionRequest {
  connection: {
    connectionId: string;
    providerFamilyId: string;
    displayName: string;
    externalAccountId: string;
    connectionType?: string;
    connectionMode?: string;
    enabled?: boolean;
    description?: string;
  };
  environment: string;
}

export interface BindProviderRuntimeCredentialRequest {
  providerId: string;
}

/** expectedGeneration comes from the non-secret migration preview, never from a secret value. */
export interface ProviderCredentialMigrationRequest {
  providerId: string;
  migrationId: string;
  expectedGeneration?: string | null;
}

/** Non-secret vault status; auditMetadata.credentialGeneration is the migration's optimistic token. */
export interface ProviderCredentialStoreStatus {
  providerId: string;
  displayName: string;
  credentialState: string;
  credentialSource: string;
  verificationState: string;
  savedAt?: string | null;
  updatedAt?: string | null;
  lastVerifiedAt?: string | null;
  lastSuccessfulAt?: string | null;
  lastFailureAt?: string | null;
  lastError?: string | null;
  maskedKeyPreview?: string | null;
  environment?: string | null;
  externalAccountId?: string | null;
  missingFields: string[];
  presentFields: string[];
  auditMetadata: Record<string, string>;
}

export interface ProviderCredentialMigrationReceipt {
  migrationId: string;
  providerId: string;
  scope: { tenantId: string; connectionId: string; externalAccountId: string; environment: string };
  credentialGeneration: string;
  actor: string;
  migratedAt: string;
  rolledBack: boolean;
  rolledBackBy?: string | null;
  rolledBackAt?: string | null;
  /** A separate provider-wide OAuth token moved under the scope with the record and moves back on rollback. */
  oAuthTokenTransferred?: boolean;
  /** Set once the verified migration was finalized: rollback ends and the provider-wide record is released. */
  finalizedBy?: string | null;
  finalizedAt?: string | null;
}

export interface ProviderRuntimeCredentialBinding {
  providerId: string;
  tenantId: string;
  connectionId: string;
}

/** Stable response codes returned by the ownership endpoints alongside an operator-facing error text. */
export type ProviderOwnershipErrorCode =
  | "OwnershipRequestInvalid"
  | "OwnershipScopeMismatch"
  | "VaultMigrationUnsupported"
  | "CredentialGenerationConflict"
  | "OwnershipTransferBlocked";

export interface ApplyProviderRuntimeCredentialRequest {
  providerId: string;
}

/**
 * Whether the retained host binding has reached running provider instances in this host process. A
 * binding changed after host start stays pending until an in-process recycle applies it or the host is
 * deliberately restarted; nothing activates hot.
 */
export interface ProviderRuntimeCredentialApplicationStatus {
  providerId: string;
  tenantId: string;
  connectionId: string;
  isBound: boolean;
  pendingRestart: boolean;
  boundAt: string | null;
  appliedAt: string | null;
  detail: string;
}
