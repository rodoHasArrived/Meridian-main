import { useEffect, useRef, useState, type FormEvent } from "react";
import { connectAlpacaConnection, revokeAlpacaConnection } from "@/lib/api";
import type { ApiRequestOptions } from "@/lib/api";
import {
  BACKFILL_API_ENDPOINTS,
  CONFIG_API_ENDPOINTS,
  EXECUTION_API_ENDPOINTS,
  EXPORT_API_ENDPOINTS,
  PORTFOLIO_API_ENDPOINTS,
  PROVIDER_API_ENDPOINTS,
  PROMOTION_API_ENDPOINTS,
  QUALITY_API_ENDPOINTS,
  RECONCILIATION_API_ENDPOINTS,
  REPLAY_API_ENDPOINTS,
  SECURITY_MASTER_API_ENDPOINTS,
  SYMBOL_API_ENDPOINTS,
  WORKSTATION_API_ENDPOINTS,
  WORKSTATION_API_ENDPOINT_TEMPLATES,
  workstationRunCompareEndpoint
} from "@/lib/workstation-endpoints";
import type {
  AlpacaBrokerageConnectionRequest,
  BrokerageConnectionStatus,
  DataOperationsWorkspaceResponse,
  GovernanceWorkspaceResponse,
  PortfolioWorkspaceResponse,
  ProviderConnectionRow,
  ResearchWorkspaceResponse,
  SessionInfo,
  SystemOverviewResponse,
  TradingWorkspaceResponse,
  WorkspaceKey
} from "@/types";

type AlpacaEnvironment = AlpacaBrokerageConnectionRequest["environment"];

export interface SettingsAlpacaConnectionFormState {
  keyId: string;
  secretKey: string;
  environment: AlpacaEnvironment;
  liveAcknowledged: boolean;
  busyAction: "connect" | "clear" | null;
  submitted: boolean;
  actionMessage: string | null;
  actionTone: "default" | "success" | "danger";
}

export interface SettingsAlpacaConnectionCommandState {
  keyIdError: boolean;
  secretKeyError: boolean;
  formPanelId: string;
  formPanelTitle: string;
  formPanelDetail: string;
  formPanelTone: "default" | "success" | "warning" | "danger";
  formPanelRole: "status" | "alert";
  formPanelAriaLive: "polite" | "assertive";
  fieldHelpIds: {
    keyId: string;
    secretKey: string;
    environment: string;
  };
  keyIdField: SettingsAlpacaCredentialFieldState;
  secretKeyField: SettingsAlpacaCredentialFieldState;
  submitLabel: string;
  clearLabel: string;
  canSubmit: boolean;
  canEdit: boolean;
  submitBusy: boolean;
  clearBusy: boolean;
  submitDisabledReason: string | null;
  clearDisabledReason: string | null;
  statusRole: "status" | "alert";
  statusClassName: string;
  keyIdHelpText: string;
  secretKeyHelpText: string;
  environmentHelpText: string;
  environmentLegend: string;
  environmentOptions: SettingsAlpacaEnvironmentOption[];
  liveAcknowledgement: SettingsAlpacaLiveAcknowledgementState;
  requirements: SettingsAlpacaRequirementRow[];
}

export interface SettingsAlpacaCredentialFieldState {
  id: string;
  label: string;
  type: "text" | "password";
  autoComplete: string;
  placeholder: string;
  helpId: string;
  helpText: string;
  describedBy: string;
  error: boolean;
  disabled: boolean;
  disabledReason: string | null;
}

export interface SettingsAlpacaEnvironmentOption {
  id: string;
  value: AlpacaEnvironment;
  label: string;
  badgeLabel: string;
  description: string;
  descriptionId: string;
  isSelected: boolean;
  disabled: boolean;
  disabledReason: string | null;
  ariaLabel: string;
  tone: "paper" | "live";
}

export interface SettingsAlpacaRequirementRow {
  id: string;
  label: string;
  value: string;
  met: boolean;
  tone: "success" | "warning" | "muted";
}

export interface SettingsAlpacaLiveAcknowledgementState {
  id: string;
  descriptionId: string;
  label: string;
  detail: string;
  checked: boolean;
  visible: boolean;
  disabled: boolean;
  disabledReason: string | null;
  required: boolean;
  ariaLabel: string;
}

export interface SettingsAlpacaSetupStep {
  id: string;
  label: string;
  statusLabel: string;
  detail: string;
  tone: "success" | "warning" | "danger" | "muted";
  badgeVariant: "success" | "warning" | "danger" | "outline";
  actionLabel: string | null;
  actionHref: string | null;
  actionAriaLabel: string | null;
}

export interface SettingsAlpacaConnectionFormViewModel extends SettingsAlpacaConnectionFormState, SettingsAlpacaConnectionCommandState {
  setKeyId: (value: string) => void;
  setSecretKey: (value: string) => void;
  setEnvironment: (value: AlpacaEnvironment) => void;
  setLiveAcknowledged: (value: boolean) => void;
  connect: (event: FormEvent<HTMLFormElement>) => Promise<void>;
  clear: () => Promise<void>;
}

interface SettingsAlpacaConnectionDependencies {
  connectConnection?: (request: AlpacaBrokerageConnectionRequest, options?: ApiRequestOptions) => Promise<BrokerageConnectionStatus>;
  revokeConnection?: (options?: ApiRequestOptions) => Promise<BrokerageConnectionStatus>;
}

const emptyAlpacaConnectionForm: SettingsAlpacaConnectionFormState = {
  keyId: "",
  secretKey: "",
  environment: "paper",
  liveAcknowledged: false,
  busyAction: null,
  submitted: false,
  actionMessage: null,
  actionTone: "default"
};

export function buildAlpacaConnectionCommandState({
  form,
  canClear,
  clearConfirmationPending = false
}: {
  form: SettingsAlpacaConnectionFormState;
  canClear: boolean;
  clearConfirmationPending?: boolean;
}): SettingsAlpacaConnectionCommandState {
  const keyIdMissing = form.keyId.trim().length === 0;
  const secretKeyMissing = form.secretKey.trim().length === 0;
  const hasValidationErrors = keyIdMissing || secretKeyMissing;
  const busy = form.busyAction !== null;
  const liveSelected = form.environment === "live";
  const liveAcknowledgementMissing = liveSelected && !form.liveAcknowledged;
  const liveReviewValue = liveSelected ? form.liveAcknowledged ? "Accepted" : "Required" : "Not required";
  const liveReviewTone: SettingsAlpacaRequirementRow["tone"] = liveAcknowledgementMissing
    ? "warning"
    : liveSelected
      ? "success"
      : "muted";
  const clearConfirmationReady = !busy && canClear && clearConfirmationPending;
  const validationVisible = form.submitted || form.actionTone === "danger";
  const keyIdError = validationVisible && keyIdMissing;
  const secretKeyError = validationVisible && secretKeyMissing;
  const missingCredentialValue = validationVisible ? "Required" : "Needed";
  const missingCredentialTone = validationVisible ? "warning" : "muted";
  const formPanelId = "alpaca-credential-readiness";
  const fieldHelpIds = {
    keyId: "alpaca-key-id-help",
    secretKey: "alpaca-secret-key-help",
    environment: "alpaca-environment-help"
  };
  const editDisabledReason = busy ? "Alpaca credential request is already running." : null;
  const keyIdHelpText = keyIdError
    ? "Key ID is required before Meridian can test the Alpaca account."
    : "Stored values remain masked after refresh.";
  const secretKeyHelpText = secretKeyError
    ? "Secret key is required and is cleared after a connection test."
    : "Secret key is never displayed after submit.";
  const environmentOptions: SettingsAlpacaEnvironmentOption[] = [
    {
      id: "alpaca-environment-paper",
      value: "paper",
      label: "Paper",
      badgeLabel: "Default",
      description: "Paper endpoint for workstation validation and readiness rehearsal.",
      descriptionId: "alpaca-environment-paper-description",
      isSelected: form.environment === "paper",
      disabled: busy,
      disabledReason: editDisabledReason,
      ariaLabel: "Use Alpaca paper endpoint for workstation validation",
      tone: "paper"
    },
    {
      id: "alpaca-environment-live",
      value: "live",
      label: "Live",
      badgeLabel: "Real money",
      description: "Live endpoint for production brokerage verification.",
      descriptionId: "alpaca-environment-live-description",
      isSelected: form.environment === "live",
      disabled: busy,
      disabledReason: editDisabledReason,
      ariaLabel: "Use Alpaca live endpoint for production brokerage verification",
      tone: "live"
    }
  ];
  const requirements: SettingsAlpacaRequirementRow[] = [
    {
      id: "alpaca-key-id-requirement",
      label: "Key ID",
      value: keyIdMissing ? missingCredentialValue : "Ready",
      met: !keyIdMissing,
      tone: keyIdMissing ? missingCredentialTone : "success"
    },
    {
      id: "alpaca-secret-key-requirement",
      label: "Secret key",
      value: secretKeyMissing ? missingCredentialValue : "Ready",
      met: !secretKeyMissing,
      tone: secretKeyMissing ? missingCredentialTone : "success"
    },
    {
      id: "alpaca-environment-requirement",
      label: "Environment",
      value: form.environment === "live" ? "LIVE" : "PAPER",
      met: true,
      tone: form.environment === "live" ? "warning" : "success"
    },
    {
      id: "alpaca-live-acknowledgement-requirement",
      label: "Live review",
      value: liveReviewValue,
      met: !liveAcknowledgementMissing,
      tone: liveReviewTone
    }
  ];
  const formPanelTone: SettingsAlpacaConnectionCommandState["formPanelTone"] = busy
    ? "warning"
    : clearConfirmationReady
      ? "warning"
    : form.actionTone === "danger"
      ? "danger"
      : form.actionTone === "success"
        ? "success"
        : hasValidationErrors && validationVisible
          ? "warning"
          : liveAcknowledgementMissing
            ? "warning"
            : "default";
  const formPanelTitle = busy
    ? form.busyAction === "clear"
      ? "Clearing Alpaca credentials"
      : "Testing Alpaca credentials"
    : clearConfirmationReady
      ? "Confirm Alpaca credential clear"
    : form.actionMessage
      ? form.actionMessage
      : liveAcknowledgementMissing && !hasValidationErrors
        ? "Live endpoint review required"
        : hasValidationErrors && validationVisible
          ? "Credentials incomplete"
          : hasValidationErrors
            ? "Enter Alpaca credentials"
            : "Credentials ready for test";
  const formPanelDetail = busy
    ? "Meridian is waiting on the brokerage connection request."
    : clearConfirmationReady
      ? "Confirming will remove the stored Alpaca key reference and block provider-backed workflows until a new connection test succeeds."
    : form.actionMessage
      ? hasValidationErrors
        ? "Review the required fields before the next connection test."
        : "Credential readiness has been recalculated from the current form state."
      : liveAcknowledgementMissing && !hasValidationErrors
        ? "Acknowledge that Meridian will verify live Alpaca brokerage credentials before submitting."
        : hasValidationErrors && validationVisible
          ? "Enter the required Alpaca API values before Meridian can call /v2/account."
          : hasValidationErrors
            ? "Paste the paper key ID and secret key to enable account verification."
            : "Submitting will test the account and clear the secret key from the form after the response.";

  return {
    keyIdError,
    secretKeyError,
    formPanelId,
    formPanelTitle,
    formPanelDetail,
    formPanelTone,
    formPanelRole: formPanelTone === "danger" ? "alert" : "status",
    formPanelAriaLive: formPanelTone === "danger" ? "assertive" : "polite",
    fieldHelpIds,
    keyIdField: {
      id: "alpaca-key-id",
      label: "Key ID",
      type: "text",
      autoComplete: "off",
      placeholder: "ALPACA_KEY_ID",
      helpId: fieldHelpIds.keyId,
      helpText: keyIdHelpText,
      describedBy: `${fieldHelpIds.keyId} ${formPanelId}`,
      error: keyIdError,
      disabled: busy,
      disabledReason: editDisabledReason
    },
    secretKeyField: {
      id: "alpaca-secret-key",
      label: "Secret key",
      type: "password",
      autoComplete: "off",
      placeholder: "ALPACA_SECRET_KEY",
      helpId: fieldHelpIds.secretKey,
      helpText: secretKeyHelpText,
      describedBy: `${fieldHelpIds.secretKey} ${formPanelId}`,
      error: secretKeyError,
      disabled: busy,
      disabledReason: editDisabledReason
    },
    submitLabel: "Connect and test",
    clearLabel: clearConfirmationReady ? "Confirm clear" : "Clear",
    canSubmit: !busy && !hasValidationErrors && !liveAcknowledgementMissing,
    canEdit: !busy,
    submitBusy: form.busyAction === "connect",
    clearBusy: form.busyAction === "clear",
    submitDisabledReason: busy
      ? "Alpaca credential request is already running."
      : keyIdMissing
        ? "Enter an Alpaca key ID before testing the connection."
        : secretKeyMissing
          ? "Enter an Alpaca secret key before testing the connection."
          : liveAcknowledgementMissing
            ? "Acknowledge the live Alpaca endpoint before testing live credentials."
            : null,
    clearDisabledReason: busy
      ? "Alpaca credential request is already running."
      : canClear
        ? null
        : "No stored Alpaca credentials are available to clear.",
    statusRole: form.actionTone === "danger" ? "alert" : "status",
    statusClassName: form.actionTone === "danger" ? "text-sm text-danger" : "text-sm text-muted-foreground",
    keyIdHelpText,
    secretKeyHelpText,
    environmentHelpText: form.environment === "live"
      ? "Live endpoint selected. Acknowledgement is required before Meridian can test these credentials."
      : "Paper endpoint selected for workstation validation.",
    environmentLegend: "Alpaca trading environment",
    environmentOptions,
    liveAcknowledgement: {
      id: "alpaca-live-acknowledgement",
      descriptionId: "alpaca-live-acknowledgement-detail",
      label: "I understand this test uses the live Alpaca endpoint.",
      detail: "Use this only for production brokerage verification; paper remains the default onboarding and demo path.",
      checked: form.liveAcknowledged,
      visible: liveSelected,
      disabled: busy,
      disabledReason: editDisabledReason,
      required: liveSelected,
      ariaLabel: "Acknowledge live Alpaca endpoint before testing credentials"
    },
    requirements
  };
}

export function useAlpacaConnectionFormViewModel({
  onRefresh,
  canClear,
  connectConnection = connectAlpacaConnection,
  revokeConnection = revokeAlpacaConnection
}: {
  onRefresh?: () => Promise<void> | void;
  canClear: boolean;
} & SettingsAlpacaConnectionDependencies): SettingsAlpacaConnectionFormViewModel {
  const [form, setForm] = useState<SettingsAlpacaConnectionFormState>(emptyAlpacaConnectionForm);
  const [clearConfirmationPending, setClearConfirmationPending] = useState(false);
  const mountedRef = useRef(true);
  const actionRevisionRef = useRef(0);
  const actionAbortRef = useRef<AbortController | null>(null);
  const command = buildAlpacaConnectionCommandState({ form, canClear, clearConfirmationPending });

  useEffect(() => () => {
    mountedRef.current = false;
    actionRevisionRef.current += 1;
    actionAbortRef.current?.abort();
  }, []);

  const setKeyId = (keyId: string) => {
    setClearConfirmationPending(false);
    setForm((current) => ({ ...current, keyId, actionMessage: null, actionTone: "default" }));
  };

  const setSecretKey = (secretKey: string) => {
    setClearConfirmationPending(false);
    setForm((current) => ({ ...current, secretKey, actionMessage: null, actionTone: "default" }));
  };

  const setEnvironment = (environment: AlpacaEnvironment) => {
    setClearConfirmationPending(false);
    setForm((current) => ({
      ...current,
      environment,
      liveAcknowledged: environment === "live" ? current.liveAcknowledged : false,
      actionMessage: null,
      actionTone: "default"
    }));
  };

  const setLiveAcknowledged = (liveAcknowledged: boolean) => {
    setClearConfirmationPending(false);
    setForm((current) => ({ ...current, liveAcknowledged, actionMessage: null, actionTone: "default" }));
  };

  const connect = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    const submittedForm = { ...form, submitted: true, actionMessage: null, actionTone: "default" as const };
    const submittedCommand = buildAlpacaConnectionCommandState({ form: submittedForm, canClear });
    if (!submittedCommand.canSubmit) {
      setForm(submittedForm);
      return;
    }

    const revision = actionRevisionRef.current + 1;
    actionRevisionRef.current = revision;
    actionAbortRef.current?.abort();
    const controller = new AbortController();
    actionAbortRef.current = controller;
    setClearConfirmationPending(false);
    setForm({ ...submittedForm, busyAction: "connect" });

    try {
      const status = await connectConnection({
        keyId: form.keyId.trim(),
        secretKey: form.secretKey.trim(),
        environment: form.environment
      }, { signal: controller.signal });
      if (!isActiveAction(mountedRef, actionRevisionRef, revision)) {
        return;
      }

      await onRefresh?.();
      if (!isActiveAction(mountedRef, actionRevisionRef, revision)) {
        return;
      }

      setForm((current) => ({
        ...current,
        secretKey: "",
        busyAction: null,
        submitted: false,
        actionMessage: status.isConnected
          ? "Alpaca account verified."
          : status.lastError ?? status.warnings[0] ?? "Alpaca connection updated.",
        actionTone: status.isConnected ? "success" : "danger"
      }));
    } catch (err) {
      if (!isActiveAction(mountedRef, actionRevisionRef, revision)) {
        return;
      }

      setForm((current) => ({
        ...current,
        busyAction: null,
        actionMessage: err instanceof Error ? err.message : "Alpaca connection request failed.",
        actionTone: "danger"
      }));
    } finally {
      if (actionAbortRef.current === controller) {
        actionAbortRef.current = null;
      }
    }
  };

  const clear = async () => {
    const currentCommand = buildAlpacaConnectionCommandState({ form, canClear, clearConfirmationPending });
    if (currentCommand.clearDisabledReason) {
      return;
    }
    if (!clearConfirmationPending) {
      setClearConfirmationPending(true);
      setForm((current) => ({ ...current, actionMessage: null, actionTone: "default" }));
      return;
    }

    const revision = actionRevisionRef.current + 1;
    actionRevisionRef.current = revision;
    actionAbortRef.current?.abort();
    const controller = new AbortController();
    actionAbortRef.current = controller;
    setClearConfirmationPending(false);
    setForm((current) => ({ ...current, busyAction: "clear", actionMessage: null, actionTone: "default" }));

    try {
      await revokeConnection({ signal: controller.signal });
      if (!isActiveAction(mountedRef, actionRevisionRef, revision)) {
        return;
      }

      await onRefresh?.();
      if (!isActiveAction(mountedRef, actionRevisionRef, revision)) {
        return;
      }

      setForm({
        ...emptyAlpacaConnectionForm,
        actionMessage: "Alpaca credentials cleared.",
        actionTone: "success"
      });
    } catch (err) {
      if (!isActiveAction(mountedRef, actionRevisionRef, revision)) {
        return;
      }

      setForm((current) => ({
        ...current,
        busyAction: null,
        actionMessage: err instanceof Error ? err.message : "Alpaca clear request failed.",
        actionTone: "danger"
      }));
    } finally {
      if (actionAbortRef.current === controller) {
        actionAbortRef.current = null;
      }
    }
  };

  return {
    ...form,
    ...command,
    setKeyId,
    setSecretKey,
    setEnvironment,
    setLiveAcknowledged,
    connect,
    clear
  };
}

function isActiveAction(
  mountedRef: { readonly current: boolean },
  actionRevisionRef: { readonly current: number },
  revision: number
): boolean {
  return mountedRef.current && actionRevisionRef.current === revision;
}

export interface SettingsSessionItem {
  label: string;
  value: string;
  tone: "default" | "success" | "warning" | "muted";
}

export interface SettingsSystemItem {
  label: string;
  value: string;
  tone: "default" | "success" | "warning" | "danger" | "muted";
}

export interface SettingsProfileAuthenticationFact {
  id: string;
  label: string;
  value: string;
  tone: "default" | "success" | "warning" | "danger" | "muted";
}

export interface SettingsProfileAuthenticationStep {
  id: string;
  label: string;
  statusLabel: string;
  detail: string;
  tone: "success" | "warning" | "danger" | "muted";
  badgeVariant: "success" | "warning" | "danger" | "outline";
  actionLabel: string | null;
  actionHref: string | null;
  actionAriaLabel: string | null;
}

export interface SettingsProfileAuthenticationNotice {
  title: string;
  detail: string;
  tone: "warning" | "danger";
  role: "status" | "alert";
}

export interface SettingsProfileAuthenticationPanel {
  regionLabel: string;
  title: string;
  summary: string;
  statusLabel: string;
  statusTone: "default" | "success" | "warning" | "danger";
  badgeVariant: "outline" | "success" | "warning" | "danger";
  avatarInitials: string;
  operatorName: string;
  roleLabel: string;
  environmentLabel: string;
  workspaceLabel: string;
  commandCountLabel: string;
  authorityLabel: string;
  authorityDetail: string;
  notice: SettingsProfileAuthenticationNotice | null;
  facts: SettingsProfileAuthenticationFact[];
  stepsTitle: string;
  stepsAriaLabel: string;
  steps: SettingsProfileAuthenticationStep[];
}

export interface SettingsDiagnosticLink {
  label: string;
  href: string;
  description: string;
  ariaLabel: string;
  statusLabel: string;
  statusDetail: string;
  tone: "default" | "success" | "warning" | "danger";
  badgeVariant: "default" | "success" | "warning" | "danger" | "outline";
  isLoading: boolean;
}

export interface SettingsBackendCapabilityEndpoint {
  id: string;
  method: "GET" | "POST" | "PUT" | "DELETE";
  label: string;
  href: string;
  ariaLabel: string;
  isBrowserNavigable: boolean;
  interactionLabel: string;
}

export interface SettingsBackendCapabilityGroup {
  id: string;
  workspaceLabel: string;
  route: string;
  title: string;
  description: string;
  endpointCountLabel: string;
  loadedCountLabel: string;
  statusLabel: string;
  statusDetail: string;
  statusVariant: "success" | "warning" | "danger" | "outline";
  endpoints: SettingsBackendCapabilityEndpoint[];
}

export interface SettingsEventRow {
  id: string;
  type: "info" | "warning" | "error";
  statusCode: string;
  badgeVariant: "default" | "warning" | "danger";
  tone: "default" | "warning" | "danger";
  message: string;
  source: string;
  timestamp: string;
  ariaLabel: string;
}

export interface SettingsRecentEventTableRow extends SettingsEventRow {
  detailPanelId: string;
  expanded: boolean;
  selectAriaLabel: string;
}

export interface SettingsRecentEventDetailField {
  label: string;
  value: string;
  tone: "default" | "warning" | "danger" | "muted";
}

export interface SettingsRecentEventDetail {
  id: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  statusLabel: string;
  statusVariant: "default" | "warning" | "danger";
  ariaLabel: string;
  fields: SettingsRecentEventDetailField[];
}

export interface SettingsRecentEventsSection {
  title: string;
  description: string;
  listLabel: string;
  countLabel: string;
  statusLabel: string;
  statusDetail: string;
  state: "ready" | "empty" | "unavailable";
  rows: SettingsEventRow[];
}

export interface SettingsRecentEventsSelectionViewModel {
  tableLabel: string;
  tableCaption: string;
  rows: SettingsRecentEventTableRow[];
  selectedRowId: string | null;
  detailPanelId: string;
  detailPanelTitle: string;
  detailPanelDescription: string;
  detailPanelEmptyText: string;
  detailPanelAriaLabel: string;
  selectedDetail: SettingsRecentEventDetail | null;
  selectRow: (rowId: string) => void;
}

export const SETTINGS_RECENT_EVENT_DETAIL_PANEL_ID = "settings-recent-event-detail";

export interface SettingsAlpacaConnectionPanel {
  providerLabel: string;
  stateLabel: string;
  statusDetail: string;
  statusTone: "default" | "success" | "warning" | "danger";
  badgeVariant: "outline" | "success" | "warning" | "danger";
  environmentLabel: string;
  accountLabel: string;
  maskedKeyIdLabel: string;
  verifiedAtLabel: string;
  warnings: string[];
  canClear: boolean;
  setupChecklistTitle: string;
  setupChecklistDetail: string;
  setupChecklistAriaLabel: string;
  setupChecklist: SettingsAlpacaSetupStep[];
}

export interface SettingsProviderConnectionRow {
  providerId: string;
  rowAnchorId: string;
  displayName: string;
  capabilityLabel: string;
  credentialLabel: string;
  credentialTone: "default" | "success" | "warning" | "danger" | "muted";
  verificationLabel: string;
  healthLabel: string;
  healthTone: "default" | "success" | "warning" | "danger" | "muted";
  sourceLabel: string;
  environmentLabel: string;
  maskedKeyPreviewLabel: string;
  lastHeartbeatLabel: string;
  fallbackLabel: string;
  affectedWorkflowsLabel: string;
  affectedWorkflows: string[];
  recommendedAction: string;
  actionHref: string;
  actionLabel: string;
  actionAriaLabel: string;
}

export interface SettingsProviderConnectionGroup {
  id: "brokerage" | "data";
  label: string;
  summary: string;
  rows: SettingsProviderConnectionRow[];
  emptyLabel: string;
}

export interface SettingsProviderConnectionCenter {
  title: string;
  description: string;
  statusLabel: string;
  statusVariant: "default" | "success" | "warning" | "danger" | "outline";
  groups: SettingsProviderConnectionGroup[];
}

export interface SettingsDiagnosticCounts {
  loadedLabel: string;
  failedLabel: string;
  checkingLabel: string;
  loaded: number;
  failed: number;
  checking: number;
}

export interface SettingsHeaderChip {
  label: string;
  value: string;
}

export interface SettingsScreenViewModel {
  headerChips: SettingsHeaderChip[];
  sessionTitle: string;
  sessionItems: SettingsSessionItem[];
  hasSession: boolean;
  profileAuthenticationPanel: SettingsProfileAuthenticationPanel;
  systemTitle: string;
  systemSummary: string;
  systemTone: "default" | "success" | "warning" | "danger";
  systemItems: SettingsSystemItem[];
  hasOverview: boolean;
  recentEventsSection: SettingsRecentEventsSection;
  providerConnectionCenter: SettingsProviderConnectionCenter;
  alpacaConnectionPanel: SettingsAlpacaConnectionPanel;
  diagnosticLinks: SettingsDiagnosticLink[];
  diagnosticCounts: SettingsDiagnosticCounts;
  diagnosticSummary: string;
  diagnosticListLabel: string;
  diagnosticStatusLabel: string;
  diagnosticStatusVariant: "default" | "success" | "warning" | "danger" | "outline";
  backendCapabilityGroups: SettingsBackendCapabilityGroup[];
  backendCapabilitySummary: string;
  backendCapabilityListLabel: string;
  backendCapabilityStatusLabel: string;
  backendCapabilityStatusVariant: "default" | "success" | "warning" | "danger" | "outline";
}

export interface SettingsScreenPayload {
  session: SessionInfo | null;
  overview: SystemOverviewResponse | null;
  research?: ResearchWorkspaceResponse | null;
  trading?: TradingWorkspaceResponse | null;
  portfolio?: PortfolioWorkspaceResponse | null;
  dataOperations?: DataOperationsWorkspaceResponse | null;
  governance?: GovernanceWorkspaceResponse | null;
  reporting?: GovernanceWorkspaceResponse | null;
  brokerageConnection?: BrokerageConnectionStatus | null;
  providerConnections?: ProviderConnectionRow[] | null;
  loading?: boolean;
  error?: string | null;
  workspaceErrors?: Partial<Record<WorkspaceKey, string>>;
}

const noopSelectRecentEvent = () => {};

interface DiagnosticEndpointDefinition {
  id: string;
  label: string;
  href: string;
  description: string;
  ariaLabel: string;
  workspaceKey?: WorkspaceKey;
  isAvailable: (payload: SettingsScreenPayload) => boolean;
  unavailableDetail: string;
}

interface CapabilityEndpointDefinition {
  id: string;
  method: SettingsBackendCapabilityEndpoint["method"];
  label: string;
  href: string;
}

interface BackendCapabilityDefinition {
  id: string;
  workspaceKey: WorkspaceKey;
  workspaceLabel: string;
  route: string;
  title: string;
  description: string;
  endpoints: CapabilityEndpointDefinition[];
  isAvailable: (payload: SettingsScreenPayload) => boolean;
  unavailableDetail: string;
}

const DIAGNOSTIC_ENDPOINTS: DiagnosticEndpointDefinition[] = [
  {
    id: "system-overview",
    label: "System overview",
    href: WORKSTATION_API_ENDPOINTS.systemStatus,
    description: "System health, provider counts, and active run summary.",
    ariaLabel: "Open System overview diagnostic endpoint",
    isAvailable: (payload) => payload.overview !== null,
    unavailableDetail: "System overview has not loaded in this workstation session."
  },
  {
    id: "session-info",
    label: "Session info",
    href: WORKSTATION_API_ENDPOINTS.session,
    description: "Current operator session context and environment.",
    ariaLabel: "Open Session info diagnostic endpoint",
    isAvailable: (payload) => payload.session !== null,
    unavailableDetail: "Operator session context has not loaded."
  },
  {
    id: "data-workspace",
    label: "Data workspace",
    href: WORKSTATION_API_ENDPOINTS.data,
    description: "Provider posture, backfill queues, and export readiness.",
    ariaLabel: "Open Data workspace diagnostic endpoint",
    workspaceKey: "data",
    isAvailable: (payload) => payload.dataOperations !== null && payload.dataOperations !== undefined,
    unavailableDetail: "Data workspace provider posture has not loaded."
  },
  {
    id: "strategy-workspace",
    label: "Strategy workspace",
    href: WORKSTATION_API_ENDPOINTS.strategy,
    description: "Strategy run metrics and active run rows.",
    ariaLabel: "Open Strategy workspace diagnostic endpoint",
    workspaceKey: "strategy",
    isAvailable: (payload) => payload.research !== null && payload.research !== undefined,
    unavailableDetail: "Strategy run payload has not loaded."
  },
  {
    id: "trading-workspace",
    label: "Trading workspace",
    href: WORKSTATION_API_ENDPOINTS.trading,
    description: "Live trading positions, orders, fills, and risk.",
    ariaLabel: "Open Trading workspace diagnostic endpoint",
    workspaceKey: "trading",
    isAvailable: (payload) => payload.trading !== null && payload.trading !== undefined,
    unavailableDetail: "Trading workspace payload has not loaded."
  },
  {
    id: "accounting-workspace",
    label: "Accounting workspace",
    href: WORKSTATION_API_ENDPOINTS.accounting,
    description: "Reconciliation queue, cash flow, and accounting evidence.",
    ariaLabel: "Open Accounting workspace diagnostic endpoint",
    workspaceKey: "accounting",
    isAvailable: (payload) => payload.governance !== null && payload.governance !== undefined,
    unavailableDetail: "Accounting workspace payload has not loaded."
  },
  {
    id: "reporting-workspace",
    label: "Reporting workspace",
    href: WORKSTATION_API_ENDPOINTS.reporting,
    description: "Reporting profiles and governed report-pack targets.",
    ariaLabel: "Open Reporting workspace diagnostic endpoint",
    workspaceKey: "reporting",
    isAvailable: (payload) => payload.reporting !== null && payload.reporting !== undefined,
    unavailableDetail: "Reporting workspace payload has not loaded."
  }
];

const BACKEND_CAPABILITY_GROUPS: BackendCapabilityDefinition[] = [
  {
    id: "trading",
    workspaceKey: "trading",
    workspaceLabel: "Trading",
    route: "/trading",
    title: "Paper trading cockpit",
    description: "Trading positions, orders, sessions, replay, promotion, controls, and operator inbox readiness.",
    isAvailable: (payload) => payload.trading !== null && payload.trading !== undefined,
    unavailableDetail: "Trading cockpit payload has not loaded.",
    endpoints: [
      { id: "trading-workspace", method: "GET", label: "Workspace", href: WORKSTATION_API_ENDPOINTS.trading },
      { id: "trading-readiness", method: "GET", label: "Readiness", href: WORKSTATION_API_ENDPOINTS.tradingReadiness },
      { id: "operator-inbox", method: "GET", label: "Operator inbox", href: WORKSTATION_API_ENDPOINTS.operatorInbox },
      { id: "orders-submit", method: "POST", label: "Submit order", href: EXECUTION_API_ENDPOINTS.ordersSubmit },
      { id: "sessions", method: "GET", label: "Paper sessions", href: EXECUTION_API_ENDPOINTS.sessions },
      { id: "replay-files", method: "GET", label: "Replay files", href: REPLAY_API_ENDPOINTS.files }
    ]
  },
  {
    id: "portfolio",
    workspaceKey: "portfolio",
    workspaceLabel: "Portfolio",
    route: "/portfolio",
    title: "Portfolio and run continuity",
    description: "Aggregate exposure, symbol exposure, run fills, ledger, attribution, continuity, and review packets.",
    isAvailable: (payload) => payload.portfolio !== null && payload.portfolio !== undefined,
    unavailableDetail: "Portfolio workspace payload has not loaded.",
    endpoints: [
      { id: "portfolio-workspace", method: "GET", label: "Workspace", href: WORKSTATION_API_ENDPOINTS.portfolio },
      { id: "portfolio-aggregate", method: "GET", label: "Portfolio aggregate", href: PORTFOLIO_API_ENDPOINTS.aggregate },
      { id: "portfolio-exposure", method: "GET", label: "Portfolio exposure", href: PORTFOLIO_API_ENDPOINTS.exposure },
      { id: "run-ledger", method: "GET", label: "Run ledger", href: WORKSTATION_API_ENDPOINT_TEMPLATES.runLedger },
      { id: "run-continuity", method: "GET", label: "Run continuity", href: WORKSTATION_API_ENDPOINT_TEMPLATES.runContinuity },
      { id: "run-review-packet", method: "GET", label: "Review packet", href: WORKSTATION_API_ENDPOINT_TEMPLATES.runReviewPacket }
    ]
  },
  {
    id: "accounting",
    workspaceKey: "accounting",
    workspaceLabel: "Accounting",
    route: "/accounting",
    title: "Accounting and reconciliation",
    description: "Reconciliation run creation, break queues, audit history, calibration summary, cash flow, ledger drill-ins, and Security Master coverage.",
    isAvailable: (payload) => payload.governance !== null && payload.governance !== undefined,
    unavailableDetail: "Accounting workspace payload has not loaded.",
    endpoints: [
      { id: "accounting-workspace", method: "GET", label: "Workspace", href: WORKSTATION_API_ENDPOINTS.accounting },
      { id: "recon-runs", method: "POST", label: "Run reconciliation", href: RECONCILIATION_API_ENDPOINTS.runs },
      { id: "break-queue", method: "GET", label: "Break queue", href: RECONCILIATION_API_ENDPOINTS.breakQueue },
      { id: "calibration", method: "GET", label: "Calibration", href: RECONCILIATION_API_ENDPOINTS.calibrationSummary },
      { id: "break-audit", method: "GET", label: "Break audit", href: `${RECONCILIATION_API_ENDPOINTS.breakQueue}/{breakId}/audit` },
      { id: "security-master", method: "GET", label: "Security Master", href: SECURITY_MASTER_API_ENDPOINTS.workstationSecurities }
    ]
  },
  {
    id: "reporting",
    workspaceKey: "reporting",
    workspaceLabel: "Reporting",
    route: "/reporting",
    title: "Governed reports and exports",
    description: "Reporting workspace posture, analysis exports, report-pack targets, data dictionaries, and approval lanes.",
    isAvailable: (payload) => payload.reporting !== null && payload.reporting !== undefined,
    unavailableDetail: "Reporting workspace payload has not loaded.",
    endpoints: [
      { id: "reporting-workspace", method: "GET", label: "Workspace", href: WORKSTATION_API_ENDPOINTS.reporting },
      { id: "analysis-export", method: "POST", label: "Analysis export", href: EXPORT_API_ENDPOINTS.analysis },
      { id: "fund-report-packs", method: "GET", label: "Report packs", href: EXPORT_API_ENDPOINTS.reportPacks },
      { id: "export-formats", method: "GET", label: "Export formats", href: EXPORT_API_ENDPOINTS.formats }
    ]
  },
  {
    id: "strategy",
    workspaceKey: "strategy",
    workspaceLabel: "Strategy",
    route: "/strategy",
    title: "Strategy run library",
    description: "Strategy workspace payloads, run history, timeline, sweeps, comparisons, diffs, and promotion actions.",
    isAvailable: (payload) => payload.research !== null && payload.research !== undefined,
    unavailableDetail: "Strategy workspace payload has not loaded.",
    endpoints: [
      { id: "strategy-workspace", method: "GET", label: "Workspace", href: WORKSTATION_API_ENDPOINTS.strategy },
      { id: "run-history", method: "GET", label: "Run history", href: WORKSTATION_API_ENDPOINTS.runHistory },
      { id: "run-timeline", method: "GET", label: "Run timeline", href: WORKSTATION_API_ENDPOINTS.runTimeline },
      { id: "run-sweeps", method: "GET", label: "Run sweeps", href: WORKSTATION_API_ENDPOINTS.runSweeps },
      { id: "run-compare", method: "POST", label: "Compare runs", href: workstationRunCompareEndpoint() },
      { id: "promotion", method: "GET", label: "Promotion check", href: `${PROMOTION_API_ENDPOINTS.evaluate}/{runId}` }
    ]
  },
  {
    id: "data",
    workspaceKey: "data",
    workspaceLabel: "Data",
    route: "/data",
    title: "Data trust and provider operations",
    description: "Provider status, backfill trigger and preview, symbols, storage quality, and data-quality queues.",
    isAvailable: (payload) => payload.dataOperations !== null && payload.dataOperations !== undefined,
    unavailableDetail: "Data workspace payload has not loaded.",
    endpoints: [
      { id: "data-workspace", method: "GET", label: "Workspace", href: WORKSTATION_API_ENDPOINTS.data },
      { id: "provider-status", method: "GET", label: "Provider status", href: PROVIDER_API_ENDPOINTS.status },
      { id: "backfill-run", method: "POST", label: "Backfill run", href: BACKFILL_API_ENDPOINTS.run },
      { id: "backfill-checkpoints", method: "GET", label: "Checkpoints", href: BACKFILL_API_ENDPOINTS.checkpoints },
      { id: "backfill-resumable", method: "GET", label: "Resumable jobs", href: BACKFILL_API_ENDPOINTS.checkpointsResumable },
      { id: "backfill-validation", method: "GET", label: "Checkpoint validation", href: BACKFILL_API_ENDPOINTS.checkpointsValidation },
      { id: "backfill-pending", method: "GET", label: "Pending symbols", href: `${BACKFILL_API_ENDPOINTS.checkpoints}/{jobId}/pending` },
      { id: "backfill-resume", method: "POST", label: "Resume checkpoint", href: `${BACKFILL_API_ENDPOINTS.checkpoints}/{jobId}/resume` },
      { id: "symbols", method: "GET", label: "Symbols", href: SYMBOL_API_ENDPOINTS.symbols },
      { id: "quality-dashboard", method: "GET", label: "Quality", href: QUALITY_API_ENDPOINTS.dashboard }
    ]
  },
  {
    id: "settings",
    workspaceKey: "settings",
    workspaceLabel: "Settings",
    route: "/settings",
    title: "Configuration and diagnostics",
    description: "Session context, health, configuration, workflow library, workflow presets, credentials, and diagnostics.",
    isAvailable: (payload) => payload.session !== null && payload.overview !== null,
    unavailableDetail: "Session or system overview has not loaded.",
    endpoints: [
      { id: "session", method: "GET", label: "Session", href: WORKSTATION_API_ENDPOINTS.session },
      { id: "status", method: "GET", label: "System status", href: WORKSTATION_API_ENDPOINTS.systemStatus },
      { id: "workflow-summary", method: "GET", label: "Workflow summary", href: WORKSTATION_API_ENDPOINTS.workflowSummary },
      { id: "workflow-library", method: "GET", label: "Workflow library", href: WORKSTATION_API_ENDPOINTS.workflowLibrary },
      { id: "workflow-presets", method: "GET", label: "Workflow presets", href: WORKSTATION_API_ENDPOINTS.workflowPresets },
      { id: "config", method: "GET", label: "Config", href: CONFIG_API_ENDPOINTS.config }
    ]
  }
];

function systemTone(status: SystemOverviewResponse["systemStatus"]): SettingsScreenViewModel["systemTone"] {
  if (status === "Healthy") return "success";
  if (status === "Degraded") return "warning";
  if (status === "Offline") return "danger";
  return "default";
}

function storageTone(health: SystemOverviewResponse["storageHealth"]): SettingsSystemItem["tone"] {
  if (health === "Healthy") return "success";
  if (health === "Warning") return "warning";
  if (health === "Critical") return "danger";
  return "default";
}

function eventBadgeVariant(type: SettingsEventRow["type"]): SettingsEventRow["badgeVariant"] {
  if (type === "error") return "danger";
  if (type === "warning") return "warning";
  return "default";
}

function eventStatusCode(type: SettingsEventRow["type"]): string {
  if (type === "error") return "CRIT";
  if (type === "warning") return "OBS";
  return "INFO";
}

function eventTone(type: SettingsEventRow["type"]): SettingsEventRow["tone"] {
  if (type === "error") return "danger";
  if (type === "warning") return "warning";
  return "default";
}

function buildRecentEventsSection(overview: SystemOverviewResponse | null): SettingsRecentEventsSection {
  if (!overview) {
    return {
      title: "Recent events",
      description: "System events from the active session. Check source subsystems for detail.",
      listLabel: "Recent system events unavailable",
      countLabel: "0",
      statusLabel: "Event stream unavailable",
      statusDetail: "System overview is unavailable. Reconnect to the Meridian API before reviewing event posture.",
      state: "unavailable",
      rows: []
    };
  }

  const events = overview.recentEvents ?? [];
  const rows = events.map((event) => {
    const source = event.source.trim() || "Unknown source";
    const timestamp = formatSettingsUtcMinute(event.timestamp, "Timestamp unavailable");
    const statusCode = eventStatusCode(event.type);

    return {
      id: event.id,
      type: event.type,
      statusCode,
      badgeVariant: eventBadgeVariant(event.type),
      tone: eventTone(event.type),
      message: event.message.trim() || "Event detail unavailable.",
      source,
      timestamp,
      ariaLabel: `${statusCode} event from ${source} at ${timestamp}. ${event.message.trim() || "Event detail unavailable."}`
    };
  });

  if (rows.length === 0) {
    return {
      title: "Recent events",
      description: "System events from the active session. Check source subsystems for detail.",
      listLabel: "No recent system events",
      countLabel: "0",
      statusLabel: "No recent events",
      statusDetail: "No system events reported for the active session. Diagnostic endpoints remain available below.",
      state: "empty",
      rows
    };
  }

  return {
    title: "Recent events",
    description: "System events from the active session. Check source subsystems for detail.",
    listLabel: rows.length === 1 ? "1 recent system event" : `${rows.length} recent system events`,
    countLabel: String(rows.length),
    statusLabel: rows.length === 1 ? "1 event reported" : `${rows.length} events reported`,
    statusDetail: "Latest workstation events remain visible with source and timestamp evidence.",
    state: "ready",
    rows
  };
}

export function useSettingsRecentEventsSelectionViewModel(
  section: SettingsRecentEventsSection
): SettingsRecentEventsSelectionViewModel {
  const [selectedRowId, setSelectedRowId] = useState<string | null>(section.rows[0]?.id ?? null);

  useEffect(() => {
    if (section.rows.length === 0) {
      if (selectedRowId !== null) {
        setSelectedRowId(null);
      }
      return;
    }

    if (!selectedRowId || !section.rows.some((row) => row.id === selectedRowId)) {
      setSelectedRowId(section.rows[0].id);
    }
  }, [section.rows, selectedRowId]);

  return buildSettingsRecentEventsSelectionViewModel(section, selectedRowId, setSelectedRowId);
}

export function buildSettingsRecentEventsSelectionViewModel(
  section: SettingsRecentEventsSection,
  selectedRowId: string | null,
  selectRow: (rowId: string) => void = noopSelectRecentEvent
): SettingsRecentEventsSelectionViewModel {
  const selectedStableRow =
    section.rows.find((row) => row.id === selectedRowId) ??
    section.rows[0] ??
    null;
  const selectedStableRowId = selectedStableRow?.id ?? null;
  const rows = section.rows.map((row) => ({
    ...row,
    detailPanelId: SETTINGS_RECENT_EVENT_DETAIL_PANEL_ID,
    expanded: row.id === selectedStableRowId,
    selectAriaLabel: `Select event ${row.id}. ${row.ariaLabel}`
  }));

  return {
    tableLabel: section.listLabel,
    tableCaption: "Select a recent event row to update the event detail panel.",
    rows,
    selectedRowId: selectedStableRowId,
    detailPanelId: SETTINGS_RECENT_EVENT_DETAIL_PANEL_ID,
    detailPanelTitle: "Selected event detail",
    detailPanelDescription: "Inspect event source, timestamp, and severity without leaving Settings.",
    detailPanelEmptyText: section.statusDetail,
    detailPanelAriaLabel: "Selected recent event detail",
    selectedDetail: selectedStableRow ? buildSettingsRecentEventDetail(selectedStableRow) : null,
    selectRow
  };
}

function buildSettingsRecentEventDetail(row: SettingsEventRow): SettingsRecentEventDetail {
  return {
    id: row.id,
    eyebrow: `${row.statusCode} event`,
    title: row.message,
    subtitle: `${row.source} / ${row.id}`,
    description: `${row.source} reported this event at ${row.timestamp}.`,
    statusLabel: eventStatusLabel(row.type),
    statusVariant: row.badgeVariant,
    ariaLabel: `${row.statusCode} event detail for ${row.id}`,
    fields: [
      { label: "Event ID", value: row.id, tone: "muted" },
      { label: "Source", value: row.source, tone: "default" },
      { label: "Timestamp", value: row.timestamp, tone: row.timestamp === "Timestamp unavailable" ? "warning" : "muted" },
      { label: "Type", value: eventTypeLabel(row.type), tone: eventDetailTone(row.type) },
      { label: "Status code", value: row.statusCode, tone: eventDetailTone(row.type) }
    ]
  };
}

function eventTypeLabel(type: SettingsEventRow["type"]): string {
  if (type === "error") return "Error";
  if (type === "warning") return "Warning";
  return "Info";
}

function eventStatusLabel(type: SettingsEventRow["type"]): string {
  if (type === "error") return "Critical";
  if (type === "warning") return "Observe";
  return "Info";
}

function eventDetailTone(type: SettingsEventRow["type"]): SettingsRecentEventDetailField["tone"] {
  if (type === "error") return "danger";
  if (type === "warning") return "warning";
  return "default";
}

function formatSettingsUtcMinute(
  value: string | Date | null | undefined,
  unavailableLabel = "Unavailable"
): string {
  if (!value) {
    return unavailableLabel;
  }

  const date = typeof value === "string" ? new Date(value) : value;
  if (Number.isNaN(date.getTime())) {
    return unavailableLabel;
  }

  return `${UTC_MONTH_LABELS[date.getUTCMonth()]} ${date.getUTCDate()}, ${padUtc(date.getUTCHours())}:${padUtc(date.getUTCMinutes())} UTC`;
}

const UTC_MONTH_LABELS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

function padUtc(value: number): string {
  return String(value).padStart(2, "0");
}

function buildAlpacaConnectionPanel(connection: BrokerageConnectionStatus | null): SettingsAlpacaConnectionPanel {
  const environment = connection?.environment?.trim() || "paper";
  const isLive = environment.toLowerCase() === "live";
  const warnings = [
    ...(connection?.warnings ?? []),
    ...(isLive ? ["Live Alpaca endpoint is selected. Paper remains the default workstation path."] : [])
  ];
  const state = connection?.state ?? "NotConfigured";
  const tone: SettingsAlpacaConnectionPanel["statusTone"] = state === "Connected"
    ? "success"
    : state === "Degraded" || state === "ReauthorizationRequired"
      ? "danger"
      : state === "Disconnected" || state === "AuthorizationPending"
        ? "warning"
        : "default";

  return {
    providerLabel: connection?.displayName ?? "Alpaca paper",
    stateLabel: connectionStateLabel(state),
    statusDetail: connectionStatusDetail(connection),
    statusTone: tone,
    badgeVariant: tone === "default" ? "outline" : tone,
    environmentLabel: environment.toUpperCase(),
    accountLabel: connection?.externalAccountId?.trim() || "Not verified",
    maskedKeyIdLabel: connection?.maskedKeyId?.trim() || "Not stored",
    verifiedAtLabel: connection?.verifiedAt?.trim() || "Not verified",
    warnings,
    canClear: connection?.isConfigured === true,
    setupChecklistTitle: "Provider setup checklist",
    setupChecklistDetail: "Move from demo data to a verified paper connection before relying on readiness evidence.",
    setupChecklistAriaLabel: "Alpaca provider setup checklist",
    setupChecklist: buildAlpacaSetupChecklist(connection, isLive)
  };
}

function buildAlpacaSetupChecklist(
  connection: BrokerageConnectionStatus | null,
  isLive: boolean
): SettingsAlpacaSetupStep[] {
  const isConfigured = connection?.isConfigured === true;
  const isConnected = connection?.isConnected === true;
  const isFailed = connection?.state === "Degraded" || connection?.state === "ReauthorizationRequired";
  const account = connection?.externalAccountId?.trim();
  const lastError = connection?.lastError?.trim();

  return [
    {
      id: "alpaca-paper-environment",
      label: "Use paper endpoint",
      statusLabel: isLive ? "Review" : "Ready",
      detail: isLive
        ? "Switch back to paper before rehearsing first-run readiness."
        : "Paper mode keeps provider setup safe for onboarding and demos.",
      tone: isLive ? "warning" : "success",
      badgeVariant: isLive ? "warning" : "success",
      actionLabel: null,
      actionHref: null,
      actionAriaLabel: null
    },
    {
      id: "alpaca-api-keys",
      label: "Store API keys",
      statusLabel: isConfigured ? "Stored" : "Needed",
      detail: isConfigured
        ? "Key ID is masked and the secret is not displayed after submit."
        : "Paste the paper key ID and secret, then test the account.",
      tone: isConfigured ? "success" : "warning",
      badgeVariant: isConfigured ? "success" : "warning",
      actionLabel: null,
      actionHref: null,
      actionAriaLabel: null
    },
    {
      id: "alpaca-account-verification",
      label: "Verify account",
      statusLabel: isConnected ? "Verified" : isFailed ? "Failed" : isConfigured ? "Test needed" : "Blocked",
      detail: isConnected
        ? account
          ? `Alpaca /v2/account returned account ${account}.`
          : "Alpaca /v2/account returned an account response."
        : isFailed
          ? lastError || "The last Alpaca verification attempt failed."
          : isConfigured
            ? "Run Connect and test to verify the stored paper account."
            : "Store paper credentials before account verification can run.",
      tone: isConnected ? "success" : isFailed ? "danger" : isConfigured ? "warning" : "muted",
      badgeVariant: isConnected ? "success" : isFailed ? "danger" : isConfigured ? "warning" : "outline",
      actionLabel: null,
      actionHref: null,
      actionAriaLabel: null
    },
    {
      id: "alpaca-readiness-handoff",
      label: "Check readiness",
      statusLabel: isConnected ? "Ready" : "Blocked",
      detail: isConnected
        ? "Open Trading readiness to confirm brokerage-sync and execution-control evidence."
        : "Readiness handoff unlocks after account verification succeeds.",
      tone: isConnected ? "success" : "muted",
      badgeVariant: isConnected ? "success" : "outline",
      actionLabel: isConnected ? "Open readiness" : null,
      actionHref: isConnected ? "/trading/readiness" : null,
      actionAriaLabel: isConnected ? "Open Trading readiness after Alpaca account verification" : null
    }
  ];
}

function connectionStateLabel(state: BrokerageConnectionStatus["state"]): string {
  switch (state) {
    case "Connected":
      return "Connected";
    case "AuthorizationPending":
      return "Verification pending";
    case "ReauthorizationRequired":
      return "Review required";
    case "Degraded":
      return "Verification failed";
    case "Disconnected":
      return "Stored";
    default:
      return "Not configured";
  }
}

function connectionStatusDetail(connection: BrokerageConnectionStatus | null): string {
  if (connection?.isConnected) {
    const account = connection.externalAccountId?.trim();
    return account
      ? `Verified Alpaca account ${account} through /v2/account.`
      : "Verified Alpaca account through /v2/account.";
  }

  if (connection?.lastError) {
    return connection.lastError;
  }

  if (connection?.isConfigured) {
    return "Alpaca API keys are stored but the account has not been verified.";
  }

  return "No Alpaca API-key connection is stored.";
}

function buildProfileAuthenticationPanel(
  session: SessionInfo | null,
  connection: BrokerageConnectionStatus | null,
  diagnosticStatusVariant: SettingsScreenViewModel["diagnosticStatusVariant"]
): SettingsProfileAuthenticationPanel {
  const isLiveSession = session?.environment === "live";
  const isConnected = connection?.isConnected === true;
  const isConfigured = connection?.isConfigured === true;
  const connectionFailed = connection?.state === "Degraded" || connection?.state === "ReauthorizationRequired";
  const account = connection?.externalAccountId?.trim();
  const workspaceLabel = session ? labelizeWorkspaceKey(session.activeWorkspace) : "Workspace unavailable";
  const environmentLabel = session ? session.environment.toUpperCase() : "UNKNOWN";
  const diagnosticBlocked = diagnosticStatusVariant === "danger";
  const statusTone: SettingsProfileAuthenticationPanel["statusTone"] = !session || connectionFailed
    ? "danger"
    : isLiveSession || (isConfigured && !isConnected)
      ? "warning"
      : isConnected
        ? "success"
        : "default";
  const statusLabel = !session
    ? "Session unavailable"
    : connectionFailed
      ? "Authorization review"
      : isLiveSession
        ? "Live authority active"
        : isConnected
          ? "Access ready"
          : isConfigured
            ? "Verification needed"
            : "Profile loaded";
  const authorityLabel = isConnected
    ? "Brokerage verified"
    : isConfigured
      ? "Brokerage test needed"
      : "Brokerage not linked";
  const authorityDetail = isConnected
    ? account
      ? `Alpaca account ${account} is verified for readiness handoffs.`
      : "Alpaca account verification succeeded."
    : connectionFailed
      ? connection?.lastError?.trim() || "Brokerage authorization needs operator review."
      : isConfigured
        ? "Stored Alpaca keys still need account verification before readiness handoff."
        : "Connect paper Alpaca credentials before relying on brokerage-backed workflows.";
  const summary = !session
    ? "Operator identity has not loaded, so authorization-sensitive workflows should stay blocked until the session payload returns."
    : isLiveSession
      ? `${session.displayName} is operating in LIVE mode as ${session.role}. Confirm account authority and diagnostics before live workflows.`
      : isConnected
        ? `${session.displayName} has a ${session.role} session with verified brokerage authority for ${workspaceLabel}.`
        : `${session.displayName} has a ${session.role} session in ${environmentLabel}; brokerage authority still needs verification.`;
  const notice = !session
    ? {
        title: "Authentication context unavailable",
        detail: "Reconnect to the Meridian API before changing credentials or acting on sensitive workflows.",
        tone: "danger" as const,
        role: "alert" as const
      }
    : connectionFailed
      ? {
          title: "Brokerage authorization needs review",
          detail: authorityDetail,
          tone: "danger" as const,
          role: "alert" as const
        }
      : isLiveSession
        ? {
            title: "Live environment controls active",
            detail: "Live mode can affect real brokerage state. Keep the Alpaca provider panel and readiness evidence in view before continuing.",
            tone: "warning" as const,
            role: "status" as const
          }
        : null;

  return {
    regionLabel: "Profile and authentication posture",
    title: "Profile and access posture",
    summary,
    statusLabel,
    statusTone,
    badgeVariant: statusTone === "default" ? "outline" : statusTone,
    avatarInitials: buildOperatorInitials(session?.displayName),
    operatorName: session?.displayName ?? "Session unavailable",
    roleLabel: session?.role ?? "Role unavailable",
    environmentLabel,
    workspaceLabel,
    commandCountLabel: session ? `${session.commandCount} command${session.commandCount === 1 ? "" : "s"} issued` : "Commands unavailable",
    authorityLabel,
    authorityDetail,
    notice,
    facts: [
      {
        id: "operator",
        label: "Operator",
        value: session?.displayName ?? "Unavailable",
        tone: session ? "default" : "danger"
      },
      {
        id: "role",
        label: "Role",
        value: session?.role ?? "Unavailable",
        tone: session ? "default" : "danger"
      },
      {
        id: "environment",
        label: "Environment",
        value: environmentLabel,
        tone: isLiveSession ? "warning" : session ? "success" : "danger"
      },
      {
        id: "workspace",
        label: "Workspace",
        value: workspaceLabel,
        tone: session ? "muted" : "danger"
      },
      {
        id: "commands",
        label: "Command trail",
        value: session ? String(session.commandCount) : "Unavailable",
        tone: session ? "muted" : "danger"
      },
      {
        id: "brokerage",
        label: "Brokerage authority",
        value: authorityLabel,
        tone: isConnected ? "success" : connectionFailed ? "danger" : isConfigured ? "warning" : "muted"
      }
    ],
    stepsTitle: "Access readiness",
    stepsAriaLabel: "Profile authentication and authorization readiness steps",
    steps: [
      {
        id: "operator-session",
        label: "Operator session",
        statusLabel: session ? "Loaded" : "Missing",
        detail: session ? `${session.displayName} is recognized as ${session.role}.` : "Session payload has not loaded from the workstation host.",
        tone: session ? "success" : "danger",
        badgeVariant: session ? "success" : "danger",
        actionLabel: null,
        actionHref: null,
        actionAriaLabel: null
      },
      {
        id: "environment-authority",
        label: "Operating mode",
        statusLabel: environmentLabel,
        detail: isLiveSession
          ? "Live mode requires explicit brokerage and readiness evidence before sensitive actions."
          : session
            ? `${environmentLabel} mode is active for this workstation session.`
            : "Operating mode is unknown until the session payload returns.",
        tone: !session ? "danger" : isLiveSession ? "warning" : "success",
        badgeVariant: !session ? "danger" : isLiveSession ? "warning" : "success",
        actionLabel: null,
        actionHref: null,
        actionAriaLabel: null
      },
      {
        id: "brokerage-authority",
        label: "Brokerage authority",
        statusLabel: isConnected ? "Verified" : connectionFailed ? "Review" : isConfigured ? "Test needed" : "Not linked",
        detail: authorityDetail,
        tone: isConnected ? "success" : connectionFailed ? "danger" : isConfigured ? "warning" : "muted",
        badgeVariant: isConnected ? "success" : connectionFailed ? "danger" : isConfigured ? "warning" : "outline",
        actionLabel: isConnected ? "Open readiness" : "Review provider setup",
        actionHref: isConnected ? "/trading/readiness" : "/settings#alpaca-provider-setup",
        actionAriaLabel: isConnected
          ? "Open Trading readiness from verified profile authentication posture"
          : "Review Alpaca provider setup from profile authentication posture"
      },
      {
        id: "audit-diagnostics",
        label: "Audit and diagnostics",
        statusLabel: diagnosticBlocked ? "Review" : "Reachable",
        detail: diagnosticBlocked
          ? "At least one diagnostic payload failed; inspect API reachability before relying on profile state."
          : "Session, diagnostics, and provider evidence can be inspected without leaving Settings.",
        tone: diagnosticBlocked ? "warning" : "success",
        badgeVariant: diagnosticBlocked ? "warning" : "success",
        actionLabel: "Open diagnostics",
        actionHref: "/settings#diagnostic-endpoints",
        actionAriaLabel: "Open Settings diagnostic endpoints from profile authentication posture"
      }
    ]
  };
}

function buildOperatorInitials(displayName: string | null | undefined): string {
  const tokens = (displayName ?? "")
    .replace(/[^A-Za-z0-9 ]/g, " ")
    .trim()
    .split(/\s+/)
    .filter(Boolean);

  if (tokens.length === 0) {
    return "--";
  }

  return tokens.slice(0, 2).map((token) => token[0]?.toUpperCase() ?? "").join("");
}

function labelizeWorkspaceKey(workspace: WorkspaceKey): string {
  return workspace.charAt(0).toUpperCase() + workspace.slice(1);
}

function buildProviderConnectionCenter(
  connections: ProviderConnectionRow[] | null | undefined
): SettingsProviderConnectionCenter {
  const rows = (connections ?? []).map(buildProviderConnectionRow);
  const brokerageRows = rows.filter((row) => row.capabilityLabel.includes("Brokerage"));
  const dataRows = rows.filter((row) => !row.capabilityLabel.includes("Brokerage"));
  const blockedCount = rows.filter((row) => row.healthTone === "danger").length;
  const warningCount = rows.filter((row) => row.healthTone === "warning").length;
  const verifiedCount = rows.filter((row) => row.credentialLabel === "Verified" || row.credentialLabel === "Not required").length;

  const statusLabel = rows.length === 0
    ? "Unavailable"
    : blockedCount > 0
      ? `${blockedCount} blocked`
      : warningCount > 0
        ? `${warningCount} need review`
        : "Continuity ready";

  return {
    title: "Provider Connection Center",
    description: rows.length === 0
      ? "Provider connection evidence has not loaded for this Settings session."
      : `${verifiedCount}/${rows.length} providers are verified or credential-free; repair actions route to the affected provider row.`,
    statusLabel,
    statusVariant: rows.length === 0 ? "warning" : blockedCount > 0 ? "danger" : warningCount > 0 ? "warning" : "success",
    groups: [
      {
        id: "brokerage",
        label: "Brokerage capable",
        summary: "Trading and account-sync providers with credential or gateway posture.",
        rows: brokerageRows,
        emptyLabel: "No brokerage-capable provider rows loaded."
      },
      {
        id: "data",
        label: "Data providers",
        summary: "Market-data and reference-data providers used by backfill and repair workflows.",
        rows: dataRows,
        emptyLabel: "No data-provider rows loaded."
      }
    ]
  };
}

function buildProviderConnectionRow(row: ProviderConnectionRow): SettingsProviderConnectionRow {
  const healthTone = providerHealthTone(row.health);
  const credentialTone = providerCredentialTone(row.credentialState);
  const workflows = row.affectedWorkflows.length > 0 ? row.affectedWorkflows : ["Workflow impact not declared"];
  return {
    providerId: row.providerId,
    rowAnchorId: row.providerId === "alpaca" ? "alpaca-provider-setup" : `provider-${row.providerId}-connection`,
    displayName: row.displayName,
    capabilityLabel: providerCapabilityLabel(row.capability),
    credentialLabel: providerCredentialLabel(row.credentialState),
    credentialTone,
    verificationLabel: providerVerificationLabel(row.verificationState),
    healthLabel: providerHealthLabel(row.health),
    healthTone,
    sourceLabel: providerCredentialSourceLabel(row.credentialSource),
    environmentLabel: row.environment ? row.environment.toUpperCase() : "Not set",
    maskedKeyPreviewLabel: row.maskedKeyPreview ?? "Masked after save",
    lastHeartbeatLabel: formatSettingsUtcMinute(row.lastSuccessfulAt ?? row.lastVerifiedAt),
    fallbackLabel: row.fallbackActive ? "Fallback active" : "Primary route",
    affectedWorkflowsLabel: workflows.join(", "),
    affectedWorkflows: workflows,
    recommendedAction: row.recommendedAction,
    actionHref: row.actionHref || `/settings#provider-${row.providerId}-connection`,
    actionLabel: row.providerId === "alpaca" ? "Manage Alpaca" : "Open provider row",
    actionAriaLabel: `Open ${row.displayName} provider connection row`
  };
}

function providerCapabilityLabel(value: ProviderConnectionRow["capability"]): string {
  switch (value) {
    case "DataAndBrokerage":
      return "Data + Brokerage";
    case "Brokerage":
      return "Brokerage";
    default:
      return "Data";
  }
}

function providerCredentialLabel(value: ProviderConnectionRow["credentialState"]): string {
  switch (value) {
    case "NotRequired":
      return "Not required";
    default:
      return value.replace(/([a-z])([A-Z])/g, "$1 $2");
  }
}

function providerVerificationLabel(value: ProviderConnectionRow["verificationState"]): string {
  switch (value) {
    case "NotRequired":
      return "Not required";
    case "NotVerified":
      return "Not verified";
    default:
      return value;
  }
}

function providerHealthLabel(value: ProviderConnectionRow["health"]): string {
  return value === "Unknown" ? "Unknown" : value;
}

function providerCredentialSourceLabel(value: ProviderConnectionRow["credentialSource"]): string {
  switch (value) {
    case "LocalEncryptedStore":
      return "Encrypted local store";
    case "Environment":
      return "Legacy environment";
    case "ExternalVaultReference":
      return "External vault";
    case "NotRequired":
      return "Not required";
    default:
      return "Not configured";
  }
}

function providerCredentialTone(value: ProviderConnectionRow["credentialState"]): SettingsProviderConnectionRow["credentialTone"] {
  switch (value) {
    case "Verified":
    case "NotRequired":
      return "success";
    case "Configured":
      return "warning";
    case "Partial":
    case "Invalid":
      return "danger";
    case "Missing":
      return "warning";
    default:
      return "muted";
  }
}

function providerHealthTone(value: ProviderConnectionRow["health"]): SettingsProviderConnectionRow["healthTone"] {
  switch (value) {
    case "Healthy":
      return "success";
    case "Warning":
    case "Unknown":
      return "warning";
    case "Degraded":
    case "Blocked":
      return "danger";
    default:
      return "muted";
  }
}

export function buildSettingsScreenViewModel(payload: SettingsScreenPayload): SettingsScreenViewModel;
export function buildSettingsScreenViewModel(
  session: SessionInfo | null,
  overview: SystemOverviewResponse | null
): SettingsScreenViewModel;
export function buildSettingsScreenViewModel(
  payloadOrSession: SettingsScreenPayload | SessionInfo | null,
  overviewArg?: SystemOverviewResponse | null
): SettingsScreenViewModel {
  const payload: SettingsScreenPayload = isSettingsScreenPayload(payloadOrSession)
    ? payloadOrSession
    : {
        session: payloadOrSession,
        overview: overviewArg ?? null
      };
  const { session, overview } = payload;
  const sessionItems: SettingsSessionItem[] = session
    ? [
        { label: "Display name", value: session.displayName, tone: "default" },
        { label: "Role", value: session.role, tone: "default" },
        { label: "Environment", value: session.environment, tone: session.environment === "live" ? "warning" : "default" },
        { label: "Active workspace", value: session.activeWorkspace, tone: "muted" },
        { label: "Commands issued", value: String(session.commandCount), tone: "muted" }
      ]
    : [];

  const systemItems: SettingsSystemItem[] = overview
    ? [
        { label: "Status", value: overview.systemStatus, tone: systemTone(overview.systemStatus) },
        { label: "Providers online", value: `${overview.providersOnline} / ${overview.providersTotal}`, tone: overview.providersOnline === overview.providersTotal ? "success" : "warning" },
        { label: "Active runs", value: String(overview.activeRuns), tone: "default" },
        { label: "Open positions", value: String(overview.openPositions), tone: "default" },
        { label: "Symbols monitored", value: String(overview.symbolsMonitored), tone: "default" },
        { label: "Active backfills", value: String(overview.activeBackfills), tone: "muted" },
        { label: "Storage health", value: overview.storageHealth, tone: storageTone(overview.storageHealth) },
        { label: "Last heartbeat", value: formatSettingsUtcMinute(overview.lastHeartbeatUtc), tone: "muted" }
      ]
    : [];

  const sysTone = overview ? systemTone(overview.systemStatus) : "default";
  const sysSummary = overview
    ? `${overview.systemStatus} · ${overview.providersOnline}/${overview.providersTotal} providers · ${overview.activeRuns} active run${overview.activeRuns === 1 ? "" : "s"}`
    : "System overview unavailable.";
  const diagnosticSection = buildDiagnosticEndpointSection(payload);
  const backendCapabilitySection = buildBackendCapabilitySection(payload);
  const providerConnectionCenter = buildProviderConnectionCenter(payload.providerConnections ?? null);
  const alpacaConnectionPanel = buildAlpacaConnectionPanel(payload.brokerageConnection ?? null);

  return {
    headerChips: buildSettingsHeaderChips(session, overview, diagnosticSection.diagnosticStatusLabel),
    sessionTitle: session ? `Session - ${session.displayName}` : "Session",
    sessionItems,
    hasSession: session !== null,
    profileAuthenticationPanel: buildProfileAuthenticationPanel(
      session,
      payload.brokerageConnection ?? null,
      diagnosticSection.diagnosticStatusVariant
    ),
    systemTitle: "System posture",
    systemSummary: sysSummary,
    systemTone: sysTone,
    systemItems,
    hasOverview: overview !== null,
    recentEventsSection: buildRecentEventsSection(overview),
    providerConnectionCenter,
    alpacaConnectionPanel,
    ...diagnosticSection,
    ...backendCapabilitySection
  };
}

function isSettingsScreenPayload(value: SettingsScreenPayload | SessionInfo | null): value is SettingsScreenPayload {
  return value !== null && "session" in value && "overview" in value;
}

function buildDiagnosticEndpointSection(payload: SettingsScreenPayload): Pick<
  SettingsScreenViewModel,
  "diagnosticLinks" | "diagnosticSummary" | "diagnosticListLabel" | "diagnosticStatusLabel" | "diagnosticStatusVariant"
  | "diagnosticCounts"
> {
  const diagnosticLinks = DIAGNOSTIC_ENDPOINTS.map((endpoint) => buildDiagnosticLink(endpoint, payload));
  const counts = buildDiagnosticCounts(diagnosticLinks);

  const diagnosticStatusLabel = counts.checking > 0
    ? `${counts.checking} checking`
    : counts.failed > 0
      ? `${counts.failed} unavailable`
      : "All reachable";

  return {
    diagnosticLinks,
    diagnosticCounts: counts,
    diagnosticSummary: counts.checking > 0
      ? `Checking ${counts.checking} diagnostic endpoint${counts.checking === 1 ? "" : "s"}; ${counts.loaded} already loaded.`
      : counts.failed > 0
        ? `${counts.failed} diagnostic endpoint${counts.failed === 1 ? "" : "s"} failed to load in the workstation bootstrap. Open the endpoint card for raw API evidence.`
        : "All diagnostic endpoint payloads represented on this page are loaded.",
    diagnosticListLabel: "Diagnostic endpoint availability",
    diagnosticStatusLabel,
    diagnosticStatusVariant: counts.checking > 0 ? "warning" : counts.failed > 0 ? "danger" : "success"
  };
}

function buildSettingsHeaderChips(
  session: SessionInfo | null,
  overview: SystemOverviewResponse | null,
  diagnosticStatusLabel: string
): SettingsHeaderChip[] {
  return [
    { label: "Environment", value: session ? session.environment.toUpperCase() : "—" },
    { label: "Workspace", value: session?.activeWorkspace ?? "—" },
    { label: "Diagnostics", value: diagnosticStatusLabel },
    { label: "Heartbeat", value: overview ? formatSettingsUtcMinute(overview.lastHeartbeatUtc) : "—" }
  ];
}

function buildBackendCapabilitySection(payload: SettingsScreenPayload): Pick<
  SettingsScreenViewModel,
  "backendCapabilityGroups" | "backendCapabilitySummary" | "backendCapabilityListLabel" | "backendCapabilityStatusLabel" | "backendCapabilityStatusVariant"
> {
  const groups = BACKEND_CAPABILITY_GROUPS.map((group) => buildBackendCapabilityGroup(group, payload));
  const loaded = groups.filter((group) => group.statusVariant === "success").length;
  const failed = groups.filter((group) => group.statusVariant === "danger").length;
  const checking = payload.loading === true ? groups.length : 0;
  const statusLabel = checking > 0
    ? `${checking} checking`
    : failed > 0
      ? `${failed} unavailable`
      : "All surfaced";

  return {
    backendCapabilityGroups: groups,
    backendCapabilitySummary: checking > 0
      ? `Checking ${checking} backend capability group${checking === 1 ? "" : "s"} across the browser workstation.`
      : failed > 0
        ? `${failed} backend capability group${failed === 1 ? "" : "s"} needs API attention before the browser can claim full workflow reachability.`
        : `${loaded} backend capability group${loaded === 1 ? "" : "s"} are represented by browser routes and mapped API endpoints.`,
    backendCapabilityListLabel: "Backend capability coverage by workstation route",
    backendCapabilityStatusLabel: statusLabel,
    backendCapabilityStatusVariant: checking > 0 ? "warning" : failed > 0 ? "danger" : "success"
  };
}

function buildBackendCapabilityGroup(
  definition: BackendCapabilityDefinition,
  payload: SettingsScreenPayload
): SettingsBackendCapabilityGroup {
  const error = payload.workspaceErrors?.[definition.workspaceKey];
  const isLoading = payload.loading === true;
  const endpointCount = definition.endpoints.length;
  const endpoints = definition.endpoints.map((endpoint) => ({
    ...endpoint,
    isBrowserNavigable: isBrowserNavigableEndpoint(endpoint),
    interactionLabel: isBrowserNavigableEndpoint(endpoint) ? "Open" : "Reference",
    ariaLabel: isBrowserNavigableEndpoint(endpoint)
      ? `${endpoint.method} ${endpoint.href} for ${definition.workspaceLabel} ${endpoint.label}`
      : `Reference-only ${endpoint.method} ${endpoint.href} for ${definition.workspaceLabel} ${endpoint.label}`
  }));

  if (isLoading) {
    return {
      ...definition,
      endpointCountLabel: `${endpointCount} endpoint${endpointCount === 1 ? "" : "s"}`,
      loadedCountLabel: "Checking",
      statusLabel: "Checking",
      statusDetail: "Workstation bootstrap is refreshing this capability group.",
      statusVariant: "warning",
      endpoints
    };
  }

  if (error) {
    return {
      ...definition,
      endpointCountLabel: `${endpointCount} endpoint${endpointCount === 1 ? "" : "s"}`,
      loadedCountLabel: "0 loaded",
      statusLabel: "Unavailable",
      statusDetail: error,
      statusVariant: "danger",
      endpoints
    };
  }

  if (definition.isAvailable(payload)) {
    return {
      ...definition,
      endpointCountLabel: `${endpointCount} endpoint${endpointCount === 1 ? "" : "s"}`,
      loadedCountLabel: `${endpointCount} mapped`,
      statusLabel: "Surfaced",
      statusDetail: `${definition.workspaceLabel} has a browser route and mapped backend endpoints. Concrete GET endpoints open directly; templates and mutating endpoints stay reference-only.`,
      statusVariant: "success",
      endpoints
    };
  }

  return {
    ...definition,
    endpointCountLabel: `${endpointCount} endpoint${endpointCount === 1 ? "" : "s"}`,
    loadedCountLabel: "0 loaded",
    statusLabel: "Unavailable",
    statusDetail: payload.error ?? definition.unavailableDetail,
    statusVariant: "danger",
    endpoints
  };
}

function isBrowserNavigableEndpoint(endpoint: CapabilityEndpointDefinition): boolean {
  return endpoint.method === "GET" && !endpoint.href.includes("{");
}

function buildDiagnosticCounts(links: SettingsDiagnosticLink[]): SettingsDiagnosticCounts {
  const loaded = links.filter((link) => link.tone === "success").length;
  const failed = links.filter((link) => link.tone === "danger").length;
  const checking = links.filter((link) => link.isLoading).length;

  return {
    loaded,
    failed,
    checking,
    loadedLabel: String(loaded),
    failedLabel: String(failed),
    checkingLabel: String(checking)
  };
}

function buildDiagnosticLink(
  endpoint: DiagnosticEndpointDefinition,
  payload: SettingsScreenPayload
): SettingsDiagnosticLink {
  const error = endpoint.workspaceKey ? payload.workspaceErrors?.[endpoint.workspaceKey] : null;
  const isLoading = payload.loading === true;

  if (isLoading) {
    return {
      ...endpoint,
      statusLabel: "Checking",
      statusDetail: "Workstation bootstrap is refreshing this diagnostic payload.",
      tone: "warning",
      badgeVariant: "warning",
      isLoading
    };
  }

  if (error) {
    return {
      ...endpoint,
      statusLabel: "Failed",
      statusDetail: error,
      tone: "danger",
      badgeVariant: "danger",
      isLoading: false
    };
  }

  if (endpoint.isAvailable(payload)) {
    return {
      ...endpoint,
      statusLabel: "Loaded",
      statusDetail: "Payload is represented in the current workstation view model.",
      tone: "success",
      badgeVariant: "success",
      isLoading: false
    };
  }

  return {
    ...endpoint,
    statusLabel: "Unavailable",
    statusDetail: payload.error ?? endpoint.unavailableDetail,
    tone: "danger",
    badgeVariant: "danger",
    isLoading: false
  };
}
