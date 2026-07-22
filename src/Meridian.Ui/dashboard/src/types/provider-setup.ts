// Provider module setup types — mirrors Meridian.Ui.Services.Services.ProviderSetup

export interface CredentialKeyStatus {
  key: string;
  hasValue: boolean;
}

export interface ProviderModuleStatus {
  moduleId: string;
  displayName: string;
  enabled: boolean;
  priority: number;
  isConfigured: boolean;
  isRunning: boolean;
  credentialStatus: CredentialKeyStatus[];
  capabilityNames: string[];
  lastTestResult?: string | null;
  lastTestedAt?: string | null;
}

export interface ProviderModuleCatalogueEntry {
  moduleId: string;
  displayName: string;
  capabilityNames: string[];
  credentialKeyHints: string[];
  requiresExternalConfig: boolean;
  alreadyConfigured: boolean;
}

export interface UpsertProviderModuleRequest {
  moduleId: string;
  enabled?: boolean;
  priority?: number;
  credentialValues?: Record<string, string | null>;
  settings?: Record<string, string | null>;
}

export interface ProviderModuleSetupResult {
  success: boolean;
  error?: string | null;
}

export interface ProviderModuleTestResult {
  success: boolean;
  credentialConfigValid: boolean;
  connectionVerified: boolean;
  failureReason?: string | null;
  latencyMs?: number | null;
  probeDetail?: string | null;
}

export interface SetEnabledRequest {
  enabled: boolean;
}
