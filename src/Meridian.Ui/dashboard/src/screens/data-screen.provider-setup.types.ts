import type { ProviderKind } from "@/types";

export interface DataOperationsDetailField {
  id: string;
  label: string;
  value: string;
}

export interface ProviderSetupFormState {
  kind: ProviderKind | string;
  displayName: string;
  environment?: "paper" | "live" | "sandbox" | "custom";
  liveAcknowledged?: boolean;
  apiKey: string;
  apiSecret: string;
  endpoint: string;
  capabilities: string[];
}

export type ProviderSetupPhase = "idle" | "submitting" | "success" | "error";

export interface ProviderSetupDialogState {
  titleId: string;
  descriptionId: string;
  formLabel: string;
  workflowSteps: ProviderSetupWorkflowStepState[];
  providerKindField: ProviderSetupSelectFieldState;
  selectedProviderSummary: ProviderSetupSummaryState;
  displayNameField: ProviderSetupTextFieldState;
  environmentField: ProviderSetupEnvironmentFieldState;
  institutionSearch: ProviderSetupInstitutionSearchState | null;
  liveAcknowledgement: ProviderSetupLiveAcknowledgementState;
  credentialFields: ProviderSetupCredentialFieldState[];
  capabilityOptions: ProviderSetupCapabilityOptionState[];
  closeButtonLabel: string;
  closeButtonDisabledReason: string | null;
  cancelAction: {
    label: string;
    ariaLabel: string;
    disabled: boolean;
    disabledReason: string | null;
  };
  submitAction: {
    label: string;
    ariaLabel: string;
    disabled: boolean;
    disabledReason: string | null;
    busy: boolean;
    busyLabel: string;
  };
  statusLabel: string;
  successPanel: {
    title: string;
    ariaLabel: string;
  };
  successMetadata: ProviderSetupSuccessMetadataState;
  successActions: ProviderSetupNextActionState[];
}

export interface ProviderSetupWorkflowStepState {
  id: "connect-source" | "acquire-data" | "validate-data" | "normalize-data" | "store-data" | "publish-data";
  label: string;
  description: string;
  status: "complete" | "current" | "pending";
  statusLabel: string;
}

export interface ProviderSetupSummaryState {
  providerLabel: string;
  description: string;
  rows: DataOperationsDetailField[];
  noCredentialMessage: string | null;
}

export interface ProviderSetupNextActionState {
  id: "live-quotes" | "backfill" | "readiness" | "security-master" | "plaid-link" | "plaid-transfers";
  label: string;
  href: string;
  ariaLabel: string;
  variant: "default" | "outline";
}

export interface ProviderSetupSuccessMetadataState {
  rows: DataOperationsDetailField[];
  warnings: string[];
  metadataAriaLabel: string;
  warningsAriaLabel: string;
}

export interface ProviderSetupKindOptionState {
  value: string;
  label: string;
}

export interface ProviderSetupSelectFieldState {
  id: string;
  label: string;
  ariaLabel: string;
  description: string;
  options: ProviderSetupKindOptionState[];
  disabled: boolean;
  disabledReason: string | null;
}

export interface ProviderSetupTextFieldState {
  id: string;
  label: string;
  ariaLabel: string;
  field: "displayName";
  value: string;
  disabled: boolean;
  disabledReason: string | null;
}

export interface ProviderSetupEnvironmentOptionState {
  value: NonNullable<ProviderSetupFormState["environment"]>;
  label: string;
}

export interface ProviderSetupEnvironmentFieldState {
  id: string;
  label: string;
  ariaLabel: string;
  description: string;
  value: NonNullable<ProviderSetupFormState["environment"]>;
  options: ProviderSetupEnvironmentOptionState[];
  disabled: boolean;
  disabledReason: string | null;
}

export interface ProviderSetupLiveAcknowledgementState {
  id: string;
  label: string;
  detail: string;
  visible: boolean;
  checked: boolean;
  disabled: boolean;
  disabledReason: string | null;
  ariaLabel: string;
}

export type ProviderSetupInstitutionSearchPhase = "idle" | "searching" | "success" | "error";
export type ProviderSetupPlaidLinkTokenPhase = "idle" | "creating" | "opening" | "exchanging" | "linked" | "ready" | "error";

export interface ProviderSetupInstitutionSearchResultState {
  institutionId: string;
  name: string;
  detail: string;
  selected: boolean;
}

export interface ProviderSetupInstitutionSearchState {
  id: string;
  label: string;
  ariaLabel: string;
  placeholder: string;
  description: string;
  value: string;
  disabled: boolean;
  disabledReason: string | null;
  phase: ProviderSetupInstitutionSearchPhase;
  statusLabel: string;
  searchAction: {
    label: string;
    ariaLabel: string;
    disabled: boolean;
    disabledReason: string | null;
    busy: boolean;
  };
  results: ProviderSetupInstitutionSearchResultState[];
  selectedInstitutionLabel: string | null;
  linkTokenPhase: ProviderSetupPlaidLinkTokenPhase;
  linkTokenStatusLabel: string;
  linkTokenAction: {
    label: string;
    ariaLabel: string;
    disabled: boolean;
    disabledReason: string | null;
    busy: boolean;
  };
  linkTokenResult: {
    linkTokenPreview: string;
    requestId: string | null;
    expirationLabel: string | null;
    institutionLabel: string | null;
    environmentLabel: string | null;
  } | null;
  linkedEvidence: {
    itemId: string;
    institutionName: string;
    status: string;
    accountCountLabel: string;
    accounts: Array<{
      id: string;
      name: string;
      detail: string;
    }>;
    requestId: string | null;
  } | null;
  sandboxGuide: {
    title: string;
    detail: string;
    username: string;
    password: string;
  } | null;
  errorText: string | null;
}

export interface ProviderSetupCredentialFieldState {
  id: string;
  label: string;
  ariaLabel: string;
  field: "apiKey" | "apiSecret" | "endpoint";
  type: "password" | "url";
  value: string;
  autoComplete: "new-password" | "off";
  placeholder: string | null;
  disabled: boolean;
  disabledReason: string | null;
}

export interface ProviderSetupCapabilityOptionState {
  id: string;
  label: string;
  description: string;
  selected: boolean;
  disabled: boolean;
  disabledReason: string | null;
}
