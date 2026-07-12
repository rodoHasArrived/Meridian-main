import { useEffect, useRef, useState, type FormEvent } from "react";
import { pluralizeCount } from "@/lib/format";
import {
  connectAlpacaConnection,
  revokeAlpacaConnection,
  startRobinhoodConnection,
  revokeRobinhoodConnection
} from "@/lib/api";
import type { ApiRequestOptions } from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { settingsProviderConnectionRoute, WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import {
  AUTH_API_ENDPOINTS,
  ACCOUNTING_SYSTEM_API_ENDPOINTS,
  BACKFILL_API_ENDPOINTS,
  CONFIG_API_ENDPOINTS,
  EXECUTION_API_ENDPOINTS,
  EXPORT_API_ENDPOINTS,
  FUND_STRUCTURE_API_ENDPOINTS,
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
  DataWorkspaceResponse,
  FeatureCapabilitySettingsResponse,
  AccountingWorkspaceResponse,
  ReportingWorkspaceResponse,
  PortfolioWorkspaceResponse,
  ProviderConnectionRow,
  ProviderCredentialFieldMetadata,
  ProviderEnvironmentOption,
  ProviderRoutingBinding,
  ProviderRoutingConnection,
  ProviderRoutingTrustSnapshot,
  StrategyWorkspaceResponse,
  SessionInfo,
  SystemOverviewResponse,
  LedgerMappingWorkbench,
  OperationsApprovalPolicyMatrix,
  OperationsCloseCalendar,
  RolePermissionCatalog,
  SecurityAssetProfileDefinition,
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
  actionDetails: string[];
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
  statusDetails: string[];
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
  endpointLabel: string;
  description: string;
  descriptionId: string;
  isSelected: boolean;
  disabled: boolean;
  disabledReason: string | null;
  disabledReasonId: string | null;
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
  disabledReasonId: string | null;
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
  actionDetails: [],
  actionTone: "default"
};

function joinDescribedBy(...parts: Array<string | null | undefined>): string {
  return parts.filter((part): part is string => Boolean(part)).join(" ");
}

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
  const environmentDisabledReasonId = busy ? "alpaca-environment-disabled-reason" : null;
  const liveAcknowledgementDisabledReasonId = busy ? "alpaca-live-acknowledgement-disabled-reason" : null;
  const keyIdHelpText = busy
    ? editDisabledReason!
    : keyIdError
    ? "Key ID is required before Meridian can test the Alpaca account."
    : "Stored values remain masked after refresh.";
  const secretKeyHelpText = busy
    ? editDisabledReason!
    : secretKeyError
    ? "Secret key is required and is cleared after a connection test."
    : "Secret key is never displayed after submit.";
  const environmentOptions: SettingsAlpacaEnvironmentOption[] = [
    {
      id: "alpaca-environment-paper",
      value: "paper",
      label: "Paper",
      badgeLabel: "Default",
      endpointLabel: "https://paper-api.alpaca.markets/v2",
      description: "Paper endpoint for workstation validation and readiness rehearsal.",
      descriptionId: "alpaca-environment-paper-description",
      isSelected: form.environment === "paper",
      disabled: busy,
      disabledReason: editDisabledReason,
      disabledReasonId: environmentDisabledReasonId,
      ariaLabel: "Use Alpaca paper endpoint for workstation validation",
      tone: "paper"
    },
    {
      id: "alpaca-environment-live",
      value: "live",
      label: "Live",
      badgeLabel: "Real money",
      endpointLabel: "https://api.alpaca.markets/v2",
      description: "Live endpoint for production brokerage verification.",
      descriptionId: "alpaca-environment-live-description",
      isSelected: form.environment === "live",
      disabled: busy,
      disabledReason: editDisabledReason,
      disabledReasonId: environmentDisabledReasonId,
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
      describedBy: joinDescribedBy(fieldHelpIds.keyId, formPanelId),
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
      describedBy: joinDescribedBy(fieldHelpIds.secretKey, formPanelId),
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
    statusDetails: form.actionDetails,
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
      disabledReasonId: liveAcknowledgementDisabledReasonId,
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
    setForm((current) => ({ ...current, keyId, actionMessage: null, actionDetails: [], actionTone: "default" }));
  };

  const setSecretKey = (secretKey: string) => {
    setClearConfirmationPending(false);
    setForm((current) => ({ ...current, secretKey, actionMessage: null, actionDetails: [], actionTone: "default" }));
  };

  const setEnvironment = (environment: AlpacaEnvironment) => {
    setClearConfirmationPending(false);
    setForm((current) => ({
      ...current,
      environment,
      liveAcknowledged: environment === "live" ? current.liveAcknowledged : false,
      actionMessage: null,
      actionDetails: [],
      actionTone: "default"
    }));
  };

  const setLiveAcknowledged = (liveAcknowledged: boolean) => {
    setClearConfirmationPending(false);
    setForm((current) => ({ ...current, liveAcknowledged, actionMessage: null, actionDetails: [], actionTone: "default" }));
  };

  const connect = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    const submittedForm = { ...form, submitted: true, actionMessage: null, actionDetails: [], actionTone: "default" as const };
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
        actionDetails: [],
        actionTone: status.isConnected ? "success" : "danger"
      }));
    } catch (err) {
      if (!isActiveAction(mountedRef, actionRevisionRef, revision)) {
        return;
      }

      const display = describeApiError(err, "Alpaca connection request failed.");
      setForm((current) => ({
        ...current,
        busyAction: null,
        actionMessage: display.summary,
        actionDetails: display.details,
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
      setForm((current) => ({ ...current, actionMessage: null, actionDetails: [], actionTone: "default" }));
      return;
    }

    const revision = actionRevisionRef.current + 1;
    actionRevisionRef.current = revision;
    actionAbortRef.current?.abort();
    const controller = new AbortController();
    actionAbortRef.current = controller;
    setClearConfirmationPending(false);
    setForm((current) => ({ ...current, busyAction: "clear", actionMessage: null, actionDetails: [], actionTone: "default" }));

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
        actionDetails: [],
        actionTone: "success"
      });
    } catch (err) {
      if (!isActiveAction(mountedRef, actionRevisionRef, revision)) {
        return;
      }

      const display = describeApiError(err, "Alpaca clear request failed.");
      setForm((current) => ({
        ...current,
        busyAction: null,
        actionMessage: display.summary,
        actionDetails: display.details,
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

interface SettingsRobinhoodConnectionDependencies {
  startConnection?: () => Promise<BrokerageConnectionStatus>;
  revokeConnection?: () => Promise<BrokerageConnectionStatus>;
  openAuthorizationUrl?: (url: string) => void;
}

export interface SettingsRobinhoodConnectionFormViewModel {
  busy: boolean;
  busyAction: "connect" | "disconnect" | null;
  actionMessage: string | null;
  actionDetails: string[];
  actionTone: "default" | "success" | "warning" | "danger";
  statusRole: "status" | "alert";
  statusClassName: string;
  authorizationUrl: string | null;
  connect: () => Promise<void>;
  disconnect: () => Promise<void>;
}

function defaultOpenAuthorizationUrl(url: string): void {
  if (typeof window !== "undefined" && typeof window.open === "function") {
    window.open(url, "_blank", "noopener");
  }
}

export function useRobinhoodConnectionViewModel({
  onRefresh,
  canConnect,
  canDisconnect,
  startConnection = startRobinhoodConnection,
  revokeConnection = revokeRobinhoodConnection,
  openAuthorizationUrl = defaultOpenAuthorizationUrl
}: {
  onRefresh?: () => Promise<void> | void;
  canConnect: boolean;
  canDisconnect: boolean;
} & SettingsRobinhoodConnectionDependencies): SettingsRobinhoodConnectionFormViewModel {
  const [busyAction, setBusyAction] = useState<"connect" | "disconnect" | null>(null);
  const [actionMessage, setActionMessage] = useState<string | null>(null);
  const [actionDetails, setActionDetails] = useState<string[]>([]);
  const [actionTone, setActionTone] = useState<"default" | "success" | "warning" | "danger">("default");
  // The Robinhood status endpoint only ever returns authorizationUrl on the connect
  // response (status refreshes return null), so retain it here to keep the manual
  // fallback link available after the post-connect refresh and across status polls.
  const [authorizationUrl, setAuthorizationUrl] = useState<string | null>(null);
  const mountedRef = useRef(true);
  const actionRevisionRef = useRef(0);

  useEffect(() => () => {
    mountedRef.current = false;
    actionRevisionRef.current += 1;
  }, []);

  const connect = async () => {
    if (!canConnect || busyAction !== null) {
      return;
    }

    const revision = actionRevisionRef.current + 1;
    actionRevisionRef.current = revision;
    setBusyAction("connect");
    setActionMessage(null);
    setActionDetails([]);
    setActionTone("default");

    try {
      const status = await startConnection();
      if (!isActiveAction(mountedRef, actionRevisionRef, revision)) {
        return;
      }

      const nextAuthorizationUrl = status.authorizationUrl?.trim() || null;
      if (nextAuthorizationUrl) {
        setAuthorizationUrl(nextAuthorizationUrl);
        openAuthorizationUrl(nextAuthorizationUrl);
      }

      await onRefresh?.();
      if (!isActiveAction(mountedRef, actionRevisionRef, revision)) {
        return;
      }

      setBusyAction(null);
      setActionMessage(
        nextAuthorizationUrl
          ? "Complete Robinhood authorization in the opened tab (or the link below if a popup was blocked), then refresh."
          : status.isConnected
            ? "Robinhood connection is active."
            : status.lastError ?? status.warnings[0] ?? "Robinhood connection updated."
      );
      setActionDetails([]);
      setActionTone(nextAuthorizationUrl || status.isConnected ? "success" : "warning");
    } catch (err) {
      if (!isActiveAction(mountedRef, actionRevisionRef, revision)) {
        return;
      }

      const display = describeApiError(err, "Robinhood connection request failed.");
      setBusyAction(null);
      setActionMessage(display.summary);
      setActionDetails(display.details);
      setActionTone("danger");
    }
  };

  const disconnect = async () => {
    if (!canDisconnect || busyAction !== null) {
      return;
    }

    const revision = actionRevisionRef.current + 1;
    actionRevisionRef.current = revision;
    setBusyAction("disconnect");
    setActionMessage(null);
    setActionDetails([]);
    setActionTone("default");

    try {
      await revokeConnection();
      if (!isActiveAction(mountedRef, actionRevisionRef, revision)) {
        return;
      }

      await onRefresh?.();
      if (!isActiveAction(mountedRef, actionRevisionRef, revision)) {
        return;
      }

      setAuthorizationUrl(null);
      setBusyAction(null);
      setActionMessage("Robinhood connection revoked.");
      setActionDetails([]);
      setActionTone("success");
    } catch (err) {
      if (!isActiveAction(mountedRef, actionRevisionRef, revision)) {
        return;
      }

      const display = describeApiError(err, "Robinhood disconnect request failed.");
      setBusyAction(null);
      setActionMessage(display.summary);
      setActionDetails(display.details);
      setActionTone("danger");
    }
  };

  return {
    busy: busyAction !== null,
    busyAction,
    actionMessage,
    actionDetails,
    actionTone,
    statusRole: actionTone === "danger" ? "alert" : "status",
    statusClassName: actionTone === "danger" ? "text-sm text-danger" : "text-sm text-muted-foreground",
    authorizationUrl,
    connect,
    disconnect
  };
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

export interface SettingsRobinhoodConnectionPanel {
  providerLabel: string;
  stateLabel: string;
  statusDetail: string;
  statusTone: "default" | "success" | "warning" | "danger";
  badgeVariant: "outline" | "success" | "warning" | "danger";
  accountLabel: string;
  connectedAtLabel: string;
  expiresAtLabel: string;
  scopesLabel: string;
  authorizationUrl: string | null;
  warnings: string[];
  isConfigured: boolean;
  canConnect: boolean;
  canDisconnect: boolean;
}

export interface SettingsProviderConnectionRow {
  providerId: string;
  integrationConnectionId: string;
  rowAnchorId: string;
  displayName: string;
  capabilityLabel: string;
  capabilityGroup: "brokerage" | "data" | "accounting";
  credentialLabel: string;
  credentialTone: "default" | "success" | "warning" | "danger" | "muted";
  credentialStatus: "present" | "missing" | "not-required";
  verificationLabel: string;
  verificationStatus: "verified" | "pending" | "failed";
  healthLabel: string;
  healthTone: "default" | "success" | "warning" | "danger" | "muted";
  sourceLabel: string;
  environmentLabel: string;
  maskedKeyPreviewLabel: string;
  lastHeartbeatLabel: string;
  fallbackLabel: string;
  fallbackStatus: "active" | "available" | "missing";
  routingBindingsLabel: string;
  trustScoreLabel: string;
  productionStateLabel: string;
  affectedWorkflowsLabel: string;
  affectedWorkflows: string[];
  recommendedAction: string;
  actionHref: string;
  actionLabel: string;
  actionAriaLabel: string;
  credentialFields: ProviderCredentialFieldMetadata[];
  environmentOptions: ProviderEnvironmentOption[];
}

export interface SettingsProviderConnectionGroup {
  id: "brokerage" | "data" | "accounting";
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
  routingSummaryLabel: string;
  refreshAction: {
    label: string;
    ariaLabel: string;
    busy: boolean;
    disabled: boolean;
    disabledReason: string | null;
  };
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

export interface SettingsRuntimeCapabilityToggle {
  capabilityKey: string;
  displayName: string;
  description: string;
  isEnabled: boolean;
  statusLabel: string;
  statusVariant: "default" | "success" | "warning" | "danger" | "outline";
  defaultLabel: string;
  overrideLabel: string;
  canToggle: boolean;
  disabledReason: string | null;
  ariaLabel: string;
}

export interface SettingsRuntimeCapabilitySection {
  title: string;
  description: string;
  statusLabel: string;
  statusVariant: "default" | "success" | "warning" | "danger" | "outline";
  summary: string;
  listLabel: string;
  toggles: SettingsRuntimeCapabilityToggle[];
}

export interface SettingsOperationsControlMetric {
  label: string;
  value: string;
  tone: "default" | "success" | "warning" | "danger" | "muted";
}

export interface SettingsOperationsControlCard {
  id: string;
  title: string;
  description: string;
  statusLabel: string;
  statusVariant: "default" | "success" | "warning" | "danger" | "outline";
  endpointHref: string;
  routeHref: string;
  routeLabel: string;
  routeAriaLabel: string;
  metrics: SettingsOperationsControlMetric[];
  detail: string;
}

export interface SettingsOperationsControlCenter {
  title: string;
  summary: string;
  statusLabel: string;
  statusVariant: "default" | "success" | "warning" | "danger" | "outline";
  loadedCountLabel: string;
  reviewCountLabel: string;
  listLabel: string;
  cards: SettingsOperationsControlCard[];
}

export interface SettingsAssetProfileRow {
  profileId: string;
  versionLabel: string;
  name: string;
  categoryLabel: string;
  statusLabel: string;
  statusVariant: "default" | "success" | "warning" | "danger" | "outline";
  fieldCountLabel: string;
  projectedFieldLabel: string;
  requiredCloseIdentifierLabel: string;
  accountingImpactLabel: string;
  effectiveLabel: string;
}

export interface SettingsAssetProfileGovernancePanel {
  title: string;
  summary: string;
  statusLabel: string;
  statusVariant: "default" | "success" | "warning" | "danger" | "outline";
  approvedCountLabel: string;
  projectedFieldCountLabel: string;
  closeIdentifierCountLabel: string;
  listLabel: string;
  canCreateSecurity: boolean;
  createDisabledReason: string | null;
  rows: SettingsAssetProfileRow[];
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
  robinhoodConnectionPanel: SettingsRobinhoodConnectionPanel;
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
  runtimeCapabilitySection: SettingsRuntimeCapabilitySection;
  operationsControlCenter: SettingsOperationsControlCenter;
  assetProfileGovernancePanel: SettingsAssetProfileGovernancePanel;
}

export interface SettingsScreenPayload {
  session: SessionInfo | null;
  overview: SystemOverviewResponse | null;
  strategy?: StrategyWorkspaceResponse | null;
  trading?: TradingWorkspaceResponse | null;
  portfolio?: PortfolioWorkspaceResponse | null;
  data?: DataWorkspaceResponse | null;
  accounting?: AccountingWorkspaceResponse | null;
  reporting?: ReportingWorkspaceResponse | null;
  brokerageConnection?: BrokerageConnectionStatus | null;
  robinhoodConnection?: BrokerageConnectionStatus | null;
  providerConnections?: ProviderConnectionRow[] | null;
  providerRoutingConnections?: ProviderRoutingConnection[] | null;
  providerRoutingBindings?: ProviderRoutingBinding[] | null;
  providerRoutingTrustSnapshots?: ProviderRoutingTrustSnapshot[] | null;
  featureCapabilities?: FeatureCapabilitySettingsResponse | null;
  rolePermissionCatalog?: RolePermissionCatalog | null;
  securityAssetProfiles?: SecurityAssetProfileDefinition[] | null;
  ledgerMappingWorkbench?: LedgerMappingWorkbench | null;
  operationsApprovalPolicyMatrix?: OperationsApprovalPolicyMatrix | null;
  operationsCloseCalendar?: OperationsCloseCalendar | null;
  providerRoutingRefreshing?: boolean;
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
    ariaLabel: "Open System overview diagnostic service",
    isAvailable: (payload) => payload.overview !== null,
    unavailableDetail: "System overview has not loaded in this workstation session."
  },
  {
    id: "session-info",
    label: "Session info",
    href: WORKSTATION_API_ENDPOINTS.session,
    description: "Current operator session context and environment.",
    ariaLabel: "Open Session info diagnostic service",
    isAvailable: (payload) => payload.session !== null,
    unavailableDetail: "Operator session context has not loaded."
  },
  {
    id: "data-workspace",
    label: "Data workspace",
    href: WORKSTATION_API_ENDPOINTS.data,
    description: "Provider posture, backfill queues, and export readiness.",
    ariaLabel: "Open Data workspace diagnostic service",
    workspaceKey: "data",
    isAvailable: (payload) => payload.data !== null && payload.data !== undefined,
    unavailableDetail: "Data workspace provider posture has not loaded."
  },
  {
    id: "strategy-workspace",
    label: "Strategy workspace",
    href: WORKSTATION_API_ENDPOINTS.strategy,
    description: "Strategy run metrics and active run rows.",
    ariaLabel: "Open Strategy workspace diagnostic service",
    workspaceKey: "strategy",
    isAvailable: (payload) => payload.strategy !== null && payload.strategy !== undefined,
    unavailableDetail: "Strategy run data has not loaded."
  },
  {
    id: "trading-workspace",
    label: "Trading workspace",
    href: WORKSTATION_API_ENDPOINTS.trading,
    description: "Live trading positions, orders, fills, and risk.",
    ariaLabel: "Open Trading workspace diagnostic service",
    workspaceKey: "trading",
    isAvailable: (payload) => payload.trading !== null && payload.trading !== undefined,
    unavailableDetail: "Trading workspace data has not loaded."
  },
  {
    id: "accounting-workspace",
    label: "Accounting workspace",
    href: WORKSTATION_API_ENDPOINTS.accounting,
    description: "Reconciliation queue, cash flow, and accounting evidence.",
    ariaLabel: "Open Accounting workspace diagnostic service",
    workspaceKey: "accounting",
    isAvailable: (payload) => payload.accounting !== null && payload.accounting !== undefined,
    unavailableDetail: "Accounting workspace data has not loaded."
  },
  {
    id: "reporting-workspace",
    label: "Reporting workspace",
    href: WORKSTATION_API_ENDPOINTS.reporting,
    description: "Reporting profiles and governed report-pack recipients.",
    ariaLabel: "Open Reporting workspace diagnostic service",
    workspaceKey: "reporting",
    isAvailable: (payload) => payload.reporting !== null && payload.reporting !== undefined,
    unavailableDetail: "Reporting workspace data has not loaded."
  }
];

const BACKEND_CAPABILITY_GROUPS: BackendCapabilityDefinition[] = [
  {
    id: "trading",
    workspaceKey: "trading",
    workspaceLabel: "Trading",
    route: WORKSTATION_ROUTE_CATALOG.trading,
    title: "Paper trading cockpit",
    description: "Trading positions, orders, sessions, replay, promotion, controls, and operator inbox readiness.",
    isAvailable: (payload) => payload.trading !== null && payload.trading !== undefined,
    unavailableDetail: "Trading cockpit data has not loaded.",
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
    route: WORKSTATION_ROUTE_CATALOG.portfolio,
    title: "Portfolio and run continuity",
    description: "Aggregate exposure, symbol exposure, run fills, ledger, attribution, continuity, and review packets.",
    isAvailable: (payload) => payload.portfolio !== null && payload.portfolio !== undefined,
    unavailableDetail: "Portfolio workspace data has not loaded.",
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
    route: WORKSTATION_ROUTE_CATALOG.accounting,
    title: "Accounting and reconciliation",
    description: "Reconciliation run creation, break queues, audit history, calibration summary, cash flow, ledger drill-ins, and Security Master coverage.",
    isAvailable: (payload) => payload.accounting !== null && payload.accounting !== undefined,
    unavailableDetail: "Accounting workspace data has not loaded.",
    endpoints: [
      { id: "accounting-workspace", method: "GET", label: "Workspace", href: WORKSTATION_API_ENDPOINTS.accounting },
      { id: "private-capital-activity", method: "GET", label: "Private-capital activity", href: WORKSTATION_API_ENDPOINTS.privateCapitalActivity },
      { id: "private-capital-fund-event-record", method: "GET", label: "Fund-event ledger record", href: WORKSTATION_API_ENDPOINTS.privateCapitalFundEventRecord },
      { id: "private-capital-capital-account-subledger", method: "GET", label: "Capital-account subledger", href: WORKSTATION_API_ENDPOINTS.privateCapitalCapitalAccountSubledger },
      { id: "private-capital-report-output", method: "GET", label: "Report output", href: WORKSTATION_API_ENDPOINTS.privateCapitalReportOutput },
      { id: "recon-runs", method: "POST", label: "Run reconciliation", href: RECONCILIATION_API_ENDPOINTS.runs },
      { id: "break-queue", method: "GET", label: "Break queue", href: RECONCILIATION_API_ENDPOINTS.breakQueue },
      { id: "calibration", method: "GET", label: "Calibration", href: RECONCILIATION_API_ENDPOINTS.calibrationSummary },
      { id: "gl-providers", method: "GET", label: "GL providers", href: ACCOUNTING_SYSTEM_API_ENDPOINTS.providers },
      { id: "gl-reconciliation", method: "GET", label: "External GL", href: ACCOUNTING_SYSTEM_API_ENDPOINTS.reconciliationLatest },
      { id: "break-audit", method: "GET", label: "Break audit", href: `${RECONCILIATION_API_ENDPOINTS.breakQueue}/{breakId}/audit` },
      { id: "security-master", method: "GET", label: "Security Master", href: SECURITY_MASTER_API_ENDPOINTS.workstationSecurities }
    ]
  },
  {
    id: "reporting",
    workspaceKey: "reporting",
    workspaceLabel: "Reporting",
    route: WORKSTATION_ROUTE_CATALOG.reporting,
    title: "Governed reports and exports",
    description: "Reporting workspace posture, analysis exports, report-pack recipients, data dictionaries, and approval lanes.",
    isAvailable: (payload) => payload.reporting !== null && payload.reporting !== undefined,
    unavailableDetail: "Reporting workspace data has not loaded.",
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
    route: WORKSTATION_ROUTE_CATALOG.strategy,
    title: "Strategy run library",
    description: "Strategy workspace data, run history, timeline, sweeps, comparisons, diffs, and promotion actions.",
    isAvailable: (payload) => payload.strategy !== null && payload.strategy !== undefined,
    unavailableDetail: "Strategy workspace data has not loaded.",
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
    route: WORKSTATION_ROUTE_CATALOG.data,
    title: "Data trust and provider operations",
    description: "Provider status, backfill trigger and preview, symbols, storage quality, and data-quality queues.",
    isAvailable: (payload) => payload.data !== null && payload.data !== undefined,
    unavailableDetail: "Data workspace data has not loaded.",
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
    route: WORKSTATION_ROUTE_CATALOG.settings,
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
      statusDetail: "No system events reported for the active session. Diagnostic services remain available below.",
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

function formatSettingsDateOnly(value: string | null | undefined, unavailableLabel = "No date"): string {
  if (!value) {
    return unavailableLabel;
  }

  const [year, month, day] = value.split("-").map((part) => Number(part));
  if (!year || !month || !day) {
    return unavailableLabel;
  }

  return `${UTC_MONTH_LABELS[month - 1] ?? "Month"} ${day}, ${year}`;
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

function buildRobinhoodConnectionPanel(connection: BrokerageConnectionStatus | null): SettingsRobinhoodConnectionPanel {
  const state = connection?.state ?? "NotConfigured";
  const tone: SettingsRobinhoodConnectionPanel["statusTone"] = state === "Connected"
    ? "success"
    : state === "Degraded" || state === "ReauthorizationRequired"
      ? "danger"
      : state === "Disconnected" || state === "AuthorizationPending"
        ? "warning"
        : "default";
  const scopes = connection?.scopes ?? [];
  const isConfigured = connection?.isConfigured === true;

  return {
    providerLabel: connection?.displayName ?? "Robinhood",
    stateLabel: connectionStateLabel(state),
    statusDetail: robinhoodConnectionStatusDetail(connection),
    statusTone: tone,
    badgeVariant: tone === "default" ? "outline" : tone,
    accountLabel: connection?.externalAccountId?.trim() || "Not linked",
    connectedAtLabel: connection?.connectedAt?.trim() || "Not connected",
    expiresAtLabel: connection?.expiresAt?.trim() || "No expiry recorded",
    scopesLabel: scopes.length > 0 ? scopes.join(", ") : "No scopes granted",
    authorizationUrl: connection?.authorizationUrl?.trim() || null,
    warnings: connection?.warnings ?? [],
    isConfigured,
    canConnect: state !== "Connected",
    canDisconnect: state !== "NotConfigured" && state !== "Disconnected"
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
      actionHref: isConnected ? WORKSTATION_ROUTE_CATALOG.tradingReadiness : null,
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

function robinhoodConnectionStatusDetail(connection: BrokerageConnectionStatus | null): string {
  if (connection?.isConnected) {
    const account = connection.externalAccountId?.trim();
    return account
      ? `Read-only Robinhood account ${account} is linked via OAuth.`
      : "Read-only Robinhood account is linked via OAuth.";
  }

  if (connection?.lastError) {
    return connection.lastError;
  }

  if (connection?.state === "AuthorizationPending" || connection?.authorizationUrl?.trim()) {
    return "Robinhood authorization is pending. Complete the OAuth consent to finish linking.";
  }

  if (connection?.isConfigured) {
    return "Robinhood OAuth is configured but no account is connected yet.";
  }

  return "No read-only Robinhood connection is configured. Set the ROBINHOOD_BROKERAGE_* OAuth environment variables.";
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
    ? "Operator identity has not loaded, so authorization-sensitive workflows should stay blocked until session data returns."
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
        detail: session ? `${session.displayName} is recognized as ${session.role}.` : "Session data has not loaded from Meridian.",
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
            : "Operating mode is unknown until session data returns.",
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
        actionHref: isConnected ? WORKSTATION_ROUTE_CATALOG.tradingReadiness : WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup,
        actionAriaLabel: isConnected
          ? "Open Trading readiness from verified profile authentication posture"
          : "Review Alpaca provider setup from profile authentication posture"
      },
      {
        id: "audit-diagnostics",
        label: "Audit and diagnostics",
        statusLabel: diagnosticBlocked ? "Review" : "Reachable",
        detail: diagnosticBlocked
          ? "At least one diagnostic data source failed; inspect service reachability before relying on profile state."
          : "Session, diagnostics, and provider evidence can be inspected without leaving Settings.",
        tone: diagnosticBlocked ? "warning" : "success",
        badgeVariant: diagnosticBlocked ? "warning" : "success",
        actionLabel: "Open diagnostics",
        actionHref: WORKSTATION_ROUTE_CATALOG.settingsDiagnostics,
        actionAriaLabel: "Open Settings diagnostic services from profile authentication posture"
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

interface ProviderRoutingRowContext {
  connection: ProviderRoutingConnection | null;
  bindings: ProviderRoutingBinding[];
  trustSnapshot: ProviderRoutingTrustSnapshot | null;
}

function buildProviderConnectionCenter(
  connections: ProviderConnectionRow[] | null | undefined,
  routingConnections: ProviderRoutingConnection[] | null | undefined,
  routingBindings: ProviderRoutingBinding[] | null | undefined,
  trustSnapshots: ProviderRoutingTrustSnapshot[] | null | undefined,
  refreshing: boolean
): SettingsProviderConnectionCenter {
  const routingConnectionRows = routingConnections ?? [];
  const bindingRows = routingBindings ?? [];
  const trustRows = trustSnapshots ?? [];
  const matchedRoutingConnectionIds = new Set<string>();

  const rows = [
    ...(connections ?? []).map((row) => {
      const connection = findRoutingConnectionForProviderRow(row, routingConnectionRows);
      if (connection) {
        matchedRoutingConnectionIds.add(normalizeProviderRoutingId(connection.connectionId));
      }
      return buildProviderConnectionRow(row, buildProviderRoutingRowContext(connection, bindingRows, trustRows));
    }),
    ...routingConnectionRows
      .filter((connection) => !matchedRoutingConnectionIds.has(normalizeProviderRoutingId(connection.connectionId)))
      .map((connection) => buildProviderRoutingConnectionRow(
        connection,
        buildProviderRoutingRowContext(connection, bindingRows, trustRows)
      ))
  ];
  const brokerageRows = rows.filter((row) => row.capabilityGroup === "brokerage");
  const accountingRows = rows.filter((row) => row.capabilityGroup === "accounting");
  const dataRows = rows.filter((row) => row.capabilityGroup === "data");
  const blockedCount = rows.filter((row) => row.healthTone === "danger").length;
  const warningCount = rows.filter((row) => row.healthTone === "warning").length;
  const verifiedCount = rows.filter((row) => row.credentialLabel === "Verified" || row.credentialLabel === "Not required").length;
  const routingSummaryLabel = routingConnectionRows.length === 0
    ? "Routing catalog unavailable"
    : `${formatCount(routingConnectionRows.length, "routing connection")} · ${formatCount(bindingRows.length, "binding")} · ${formatCount(trustRows.length, "trust snapshot")}`;

  const statusLabel = rows.length === 0
    ? "Unavailable"
    : refreshing
      ? "Refreshing"
    : blockedCount > 0
      ? `${blockedCount} blocked`
      : warningCount > 0
        ? `${warningCount} need review`
        : "Continuity ready";

  return {
    title: "Provider Connection Center",
    description: rows.length === 0
      ? "Provider connection evidence has not loaded for this Settings session."
      : `${verifiedCount}/${rows.length} providers are verified or credential-free; ${routingSummaryLabel}.`,
    statusLabel,
    statusVariant: rows.length === 0 ? "warning" : blockedCount > 0 ? "danger" : warningCount > 0 ? "warning" : "success",
    routingSummaryLabel,
    refreshAction: {
      label: refreshing ? "Refreshing..." : "Refresh routing",
      ariaLabel: refreshing ? "Provider routing refresh in progress" : "Refresh Provider Connection Center routing data",
      busy: refreshing,
      disabled: refreshing,
      disabledReason: refreshing ? "Provider routing refresh is already in progress." : null
    },
    groups: [
      {
        id: "brokerage",
        label: "Brokerage capable",
        summary: "Trading and account-sync providers with credential or gateway posture.",
        rows: brokerageRows,
        emptyLabel: "No brokerage-capable provider rows loaded."
      },
      {
        id: "accounting",
        label: "Accounting systems",
        summary: "External GL and accounting-system providers used by accounting evidence and close reconciliation.",
        rows: accountingRows,
        emptyLabel: "No accounting-system provider rows loaded."
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

function buildProviderConnectionRow(
  row: ProviderConnectionRow,
  routingContext: ProviderRoutingRowContext
): SettingsProviderConnectionRow {
  const healthTone = providerHealthTone(row.health);
  const credentialTone = providerCredentialTone(row.credentialState);
  const routingCapabilityLabels = buildProviderRoutingCapabilityLabels(routingContext.bindings);
  const workflows = row.affectedWorkflows.length > 0
    ? row.affectedWorkflows
    : routingCapabilityLabels.length > 0
      ? routingCapabilityLabels
      : ["Workflow impact not declared"];
  return {
    providerId: row.providerId,
    integrationConnectionId: routingContext.connection?.connectionId ?? row.providerId,
    rowAnchorId: row.providerId === "alpaca" ? "alpaca-provider-setup" : `provider-${row.providerId}-connection`,
    displayName: row.displayName,
    capabilityLabel: providerCapabilityLabel(row.capability),
    capabilityGroup: row.capability === "AccountingSystem"
      ? "accounting"
      : row.capability === "Brokerage" || row.capability === "DataAndBrokerage"
        ? "brokerage"
        : "data",
    credentialLabel: providerCredentialLabel(row.credentialState),
    credentialTone,
    credentialStatus: row.credentialState === "NotRequired"
      ? "not-required"
      : row.credentialState === "Missing" || row.credentialState === "Partial" || row.credentialState === "Invalid"
        ? "missing"
        : "present",
    verificationLabel: providerVerificationLabel(row.verificationState),
    verificationStatus: row.verificationState === "Failed"
      ? "failed"
      : row.verificationState === "Verified" || row.verificationState === "NotRequired"
        ? "verified"
        : "pending",
    healthLabel: providerHealthLabel(row.health),
    healthTone,
    sourceLabel: providerCredentialSourceLabel(row.credentialSource),
    environmentLabel: row.environment ? row.environment.toUpperCase() : "Not set",
    maskedKeyPreviewLabel: row.maskedKeyPreview ?? "Masked after save",
    lastHeartbeatLabel: formatSettingsUtcMinute(row.lastSuccessfulAt ?? row.lastVerifiedAt),
    fallbackLabel: row.fallbackActive ? "Fallback active" : providerRoutingFallbackLabel(routingContext.bindings),
    fallbackStatus: row.fallbackActive ? "active" : routingContext.bindings.length > 0 ? "available" : "missing",
    routingBindingsLabel: providerRoutingBindingsLabel(routingContext.bindings),
    trustScoreLabel: providerRoutingTrustScoreLabel(routingContext.trustSnapshot),
    productionStateLabel: providerRoutingProductionStateLabel(routingContext.connection),
    affectedWorkflowsLabel: workflows.join(", "),
    affectedWorkflows: workflows,
    recommendedAction: row.recommendedAction,
    actionHref: row.actionHref || settingsProviderConnectionRoute(row.providerId),
    actionLabel: row.providerId === "alpaca" ? "Manage Alpaca" : "Open provider row",
    actionAriaLabel: `Open ${row.displayName} provider connection row`,
    credentialFields: row.credentialFields ?? [],
    environmentOptions: row.environmentOptions ?? []
  };
}

function buildProviderRoutingConnectionRow(
  connection: ProviderRoutingConnection,
  routingContext: ProviderRoutingRowContext
): SettingsProviderConnectionRow {
  const routingCapabilityLabels = buildProviderRoutingCapabilityLabels(routingContext.bindings);
  const credentialConfigured = Boolean(connection.credentialReference?.trim());
  const healthTone = providerRoutingHealthTone(connection, routingContext.trustSnapshot);
  const credentialTone: SettingsProviderConnectionRow["credentialTone"] = credentialConfigured
    ? connection.productionReady ? "success" : "warning"
    : "success";
  const workflows = routingCapabilityLabels.length > 0 ? routingCapabilityLabels : ["Routing capability not bound"];

  return {
    providerId: connection.connectionId,
    integrationConnectionId: connection.connectionId,
    rowAnchorId: `provider-${connection.connectionId}-connection`,
    displayName: connection.displayName,
    capabilityLabel: providerRoutingCapabilityLabel(routingContext.bindings, connection),
    capabilityGroup: routingContext.bindings.some((binding) => providerRoutingCapabilityGroup(binding.capability) === "brokerage")
      ? "brokerage"
      : "data",
    credentialLabel: credentialConfigured ? "Configured" : "Not required",
    credentialTone,
    credentialStatus: credentialConfigured ? "present" : "not-required",
    verificationLabel: connection.productionReady ? "Certified" : "Certification pending",
    verificationStatus: connection.productionReady ? "verified" : "pending",
    healthLabel: providerRoutingHealthLabel(connection, routingContext.trustSnapshot),
    healthTone,
    sourceLabel: credentialConfigured ? "Vault reference" : "Not required",
    environmentLabel: credentialReferenceEnvironmentLabel(connection.credentialReference),
    maskedKeyPreviewLabel: "Hidden by routing API",
    lastHeartbeatLabel: "Live routing snapshot",
    fallbackLabel: providerRoutingFallbackLabel(routingContext.bindings),
    fallbackStatus: routingContext.bindings.length === 0
      ? "missing"
      : routingContext.bindings.some((binding) => binding.failoverConnectionIds.length > 0)
        ? "active"
        : "available",
    routingBindingsLabel: providerRoutingBindingsLabel(routingContext.bindings),
    trustScoreLabel: providerRoutingTrustScoreLabel(routingContext.trustSnapshot),
    productionStateLabel: providerRoutingProductionStateLabel(connection),
    affectedWorkflowsLabel: workflows.join(", "),
    affectedWorkflows: workflows,
    recommendedAction: providerRoutingRecommendedAction(connection, routingContext),
    actionHref: settingsProviderConnectionRoute(connection.connectionId),
    actionLabel: "Open provider row",
    actionAriaLabel: `Open ${connection.displayName} provider connection row`,
    credentialFields: [],
    environmentOptions: []
  };
}

function buildProviderRoutingRowContext(
  connection: ProviderRoutingConnection | null,
  bindings: ProviderRoutingBinding[],
  trustSnapshots: ProviderRoutingTrustSnapshot[]
): ProviderRoutingRowContext {
  if (!connection) {
    return { connection: null, bindings: [], trustSnapshot: null };
  }

  return {
    connection,
    bindings: bindings.filter((binding) =>
      normalizeProviderRoutingId(binding.connectionId) === normalizeProviderRoutingId(connection.connectionId)),
    trustSnapshot: trustSnapshots.find((snapshot) =>
      normalizeProviderRoutingId(snapshot.connectionId) === normalizeProviderRoutingId(connection.connectionId)) ?? null
  };
}

function findRoutingConnectionForProviderRow(
  row: ProviderConnectionRow,
  routingConnections: ProviderRoutingConnection[]
): ProviderRoutingConnection | null {
  const providerId = normalizeProviderRoutingId(row.providerId);
  const displayName = normalizeProviderRoutingId(row.displayName);
  return routingConnections.find((connection) =>
    normalizeProviderRoutingId(connection.connectionId) === providerId ||
    normalizeProviderRoutingId(connection.providerFamilyId) === providerId ||
    normalizeProviderRoutingId(connection.displayName) === displayName) ?? null;
}

function normalizeProviderRoutingId(value: string | null | undefined): string {
  return (value ?? "").trim().toLowerCase();
}

function buildProviderRoutingCapabilityLabels(bindings: ProviderRoutingBinding[]): string[] {
  return bindings
    .map((binding) => formatProviderRoutingCapability(binding.capability))
    .filter((value, index, values) => values.indexOf(value) === index);
}

function providerRoutingCapabilityLabel(
  bindings: ProviderRoutingBinding[],
  connection: ProviderRoutingConnection
): string {
  if (bindings.some((binding) => providerRoutingCapabilityGroup(binding.capability) === "brokerage")) {
    return "Brokerage";
  }

  const labels = buildProviderRoutingCapabilityLabels(bindings);
  if (labels.length > 0) {
    return labels.slice(0, 2).join(" + ");
  }

  return connection.connectionType.replace(/([a-z])([A-Z])/g, "$1 $2");
}

function providerRoutingBindingsLabel(bindings: ProviderRoutingBinding[]): string {
  const labels = buildProviderRoutingCapabilityLabels(bindings);
  return labels.length > 0 ? labels.join(", ") : "No routing binding loaded";
}

function providerRoutingFallbackLabel(bindings: ProviderRoutingBinding[]): string {
  const fallbackCount = bindings.reduce((count, binding) => count + (binding.failoverConnectionIds?.length ?? 0), 0);
  return fallbackCount > 0 ? `${formatCount(fallbackCount, "failover route")}` : "Primary route";
}

function providerRoutingTrustScoreLabel(snapshot: ProviderRoutingTrustSnapshot | null): string {
  if (!snapshot) {
    return "No trust snapshot";
  }

  return `${formatProviderRoutingScore(snapshot.score)} · ${snapshot.healthStatus}`;
}

function providerRoutingProductionStateLabel(connection: ProviderRoutingConnection | null): string {
  if (!connection) {
    return "Not in routing catalog";
  }

  return connection.productionReady ? "Production ready" : "Certification needed";
}

function providerRoutingHealthLabel(
  connection: ProviderRoutingConnection,
  snapshot: ProviderRoutingTrustSnapshot | null
): string {
  if (!connection.enabled) {
    return "Disabled";
  }

  if (snapshot?.healthStatus?.trim()) {
    return snapshot.healthStatus.trim();
  }

  return connection.productionReady ? "Routable" : "Certification needed";
}

function providerRoutingHealthTone(
  connection: ProviderRoutingConnection,
  snapshot: ProviderRoutingTrustSnapshot | null
): SettingsProviderConnectionRow["healthTone"] {
  if (!connection.enabled) {
    return "danger";
  }

  if (!connection.productionReady) {
    return "warning";
  }

  if (!snapshot) {
    return "warning";
  }

  if (snapshot.isHealthy) {
    return "success";
  }

  const status = snapshot.healthStatus.toLowerCase();
  return status.includes("blocked") || status.includes("degraded") ? "danger" : "warning";
}

function providerRoutingRecommendedAction(
  connection: ProviderRoutingConnection,
  routingContext: ProviderRoutingRowContext
): string {
  if (!connection.enabled) {
    return "Enable the routing connection before selecting it for provider workflows.";
  }

  if (routingContext.bindings.length === 0) {
    return "Add a provider-routing binding before selecting this connection.";
  }

  if (!connection.productionReady) {
    return "Run provider certification before production routing.";
  }

  if (routingContext.trustSnapshot && !routingContext.trustSnapshot.isHealthy) {
    return "Inspect provider health before routing new workflow traffic.";
  }

  return "Provider routing is ready for supported capabilities.";
}

function credentialReferenceEnvironmentLabel(reference: string | null | undefined): string {
  const value = reference?.trim();
  if (!value) {
    return "Not set";
  }

  const parts = value.split("/");
  const environment = parts.length > 1 ? parts[parts.length - 1]?.trim() : "";
  return environment ? environment.toUpperCase() : "Configured";
}

function formatProviderRoutingCapability(capability: string): string {
  switch (capability) {
    case "RealtimeMarketData":
      return "Realtime";
    case "HistoricalBars":
      return "Historical bars";
    case "ReferenceData":
      return "Reference data";
    case "SecurityMasterSeed":
      return "Security Master";
    case "OrderExecution":
      return "Order routing";
    case "ExecutionHistory":
      return "Execution history";
    case "AccountBalances":
      return "Balances";
    case "AccountPositions":
      return "Positions";
    case "ReconciliationFeed":
      return "Reconciliation";
    case "CashTransactions":
      return "Cash activity";
    case "BankStatements":
      return "Statements";
    default:
      return capability.replace(/([a-z])([A-Z])/g, "$1 $2");
  }
}

function providerRoutingCapabilityGroup(capability: string): "brokerage" | "data" {
  switch (capability) {
    case "OrderExecution":
    case "ExecutionHistory":
    case "AccountBalances":
    case "AccountPositions":
    case "ReconciliationFeed":
    case "CashTransactions":
    case "BankStatements":
      return "brokerage";
    default:
      return "data";
  }
}

function formatProviderRoutingScore(score: number): string {
  const percentage = score <= 1 ? score * 100 : score;
  return `${Math.round(Math.max(0, Math.min(100, percentage)))}%`;
}

function formatCount(value: number, singular: string): string {
  return pluralizeCount(value, singular);
}

function providerCapabilityLabel(value: ProviderConnectionRow["capability"]): string {
  switch (value) {
    case "DataAndBrokerage":
      return "Data + Brokerage";
    case "Brokerage":
      return "Brokerage";
    case "AccountingSystem":
      return "Accounting System";
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
  const runtimeCapabilitySection = buildRuntimeCapabilitySection(payload.featureCapabilities ?? null);
  const operationsControlCenter = buildOperationsControlCenter(payload);
  const assetProfileGovernancePanel = buildAssetProfileGovernancePanel(
    payload.securityAssetProfiles ?? null,
    payload.loading === true
  );
  const providerConnectionCenter = buildProviderConnectionCenter(
    payload.providerConnections ?? null,
    payload.providerRoutingConnections ?? null,
    payload.providerRoutingBindings ?? null,
    payload.providerRoutingTrustSnapshots ?? null,
    payload.providerRoutingRefreshing === true
  );
  const alpacaConnectionPanel = buildAlpacaConnectionPanel(payload.brokerageConnection ?? null);
  const robinhoodConnectionPanel = buildRobinhoodConnectionPanel(payload.robinhoodConnection ?? null);

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
    robinhoodConnectionPanel,
    runtimeCapabilitySection,
    operationsControlCenter,
    assetProfileGovernancePanel,
    ...diagnosticSection,
    ...backendCapabilitySection
  };
}

function isSettingsScreenPayload(value: SettingsScreenPayload | SessionInfo | null): value is SettingsScreenPayload {
  return value !== null && "session" in value && "overview" in value;
}

function buildAssetProfileGovernancePanel(
  profiles: SecurityAssetProfileDefinition[] | null,
  loading: boolean
): SettingsAssetProfileGovernancePanel {
  if (!profiles) {
    return {
      title: "Asset Profile accounting",
      summary: loading
        ? "Asset profiles are loading from Security Master."
        : "Asset profile catalog has not loaded.",
      statusLabel: loading ? "Checking" : "Unavailable",
      statusVariant: loading ? "warning" : "outline",
      approvedCountLabel: "0",
      projectedFieldCountLabel: "0",
      closeIdentifierCountLabel: "0",
      listLabel: "Asset profile accounting rows",
      canCreateSecurity: false,
      createDisabledReason: loading
        ? "Asset profile catalog is still loading."
        : "Asset profile catalog has not loaded.",
      rows: []
    };
  }

  const approvedProfiles = profiles.filter((profile) => profile.status === "Approved");
  const projectedFieldCount = approvedProfiles.reduce(
    (sum, profile) => sum + profile.fields.filter((field) => field.isProjected || field.isSearchable).length,
    0
  );
  const requiredIdentifierCount = approvedProfiles.reduce(
    (sum, profile) => sum + profile.identifierPreferences.filter((preference) => preference.isRequiredForClose).length,
    0
  );
  const statusVariant: SettingsAssetProfileGovernancePanel["statusVariant"] =
    approvedProfiles.length > 0 ? "success" : "warning";

  return {
    title: "Asset Profile accounting",
    summary: approvedProfiles.length > 0
      ? `${approvedProfiles.length} approved alternative-asset profile${approvedProfiles.length === 1 ? "" : "s"} are available for governed Security Master creation.`
      : "No approved asset profiles are available for Security Master creation.",
    statusLabel: approvedProfiles.length > 0 ? `${approvedProfiles.length} approved` : "Approval needed",
    statusVariant,
    approvedCountLabel: String(approvedProfiles.length),
    projectedFieldCountLabel: String(projectedFieldCount),
    closeIdentifierCountLabel: String(requiredIdentifierCount),
    listLabel: `${profiles.length} asset profile${profiles.length === 1 ? "" : "s"}`,
    canCreateSecurity: approvedProfiles.length > 0,
    createDisabledReason: approvedProfiles.length > 0 ? null : "Approve an asset profile before creating custom assets.",
    rows: profiles.map((profile) => {
      const projectedFields = profile.fields.filter((field) => field.isProjected || field.isSearchable).length;
      const requiredIdentifiers = profile.identifierPreferences
        .filter((preference) => preference.isRequiredForClose)
        .map((preference) => preference.kind);
      return {
        profileId: profile.profileId,
        versionLabel: `v${profile.version}`,
        name: profile.name,
        categoryLabel: profile.subType ? `${profile.category} / ${profile.subType}` : profile.category,
        statusLabel: profile.status,
        statusVariant: assetProfileStatusVariant(profile.status),
        fieldCountLabel: `${profile.fields.length} field${profile.fields.length === 1 ? "" : "s"}`,
        projectedFieldLabel: `${projectedFields} projected`,
        requiredCloseIdentifierLabel: requiredIdentifiers.length > 0
          ? requiredIdentifiers.join(", ")
          : "No close identifier",
        accountingImpactLabel: profile.accountingImpactHints.length > 0
          ? profile.accountingImpactHints.join(", ")
          : "No accounting hints",
        effectiveLabel: formatSettingsDateOnly(profile.effectiveFrom)
      };
    })
  };
}

function assetProfileStatusVariant(
  status: SecurityAssetProfileDefinition["status"]
): SettingsAssetProfileRow["statusVariant"] {
  switch (status) {
    case "Approved":
      return "success";
    case "Draft":
      return "warning";
    case "Retired":
      return "danger";
    case "Superseded":
      return "outline";
    default:
      return "default";
  }
}

function buildOperationsControlCenter(payload: SettingsScreenPayload): SettingsOperationsControlCenter {
  const cards = [
    buildLedgerMappingControlCard(payload.ledgerMappingWorkbench ?? null, payload.loading === true),
    buildRolePermissionControlCard(payload.rolePermissionCatalog ?? null, payload.session, payload.loading === true),
    buildApprovalPolicyControlCard(payload.operationsApprovalPolicyMatrix ?? null, payload.loading === true),
    buildCloseCalendarControlCard(payload.operationsCloseCalendar ?? null, payload.loading === true)
  ];
  const loadedCount = cards.filter((card) => card.statusVariant !== "outline").length;
  const reviewCount = cards.filter((card) => card.statusVariant === "warning" || card.statusVariant === "danger").length;
  const checkingCount = cards.length - loadedCount;
  const statusVariant: SettingsOperationsControlCenter["statusVariant"] = checkingCount > 0
    ? "warning"
    : reviewCount > 0
      ? "warning"
      : "success";

  return {
    title: "Fund operations control center",
    summary: checkingCount > 0
      ? `${checkingCount} configuration surface${checkingCount === 1 ? "" : "s"} still loading; ${loadedCount} loaded.`
      : reviewCount > 0
        ? `${reviewCount} configuration surface${reviewCount === 1 ? "" : "s"} need operator review before close accounting is clean.`
        : "Ledger mappings, role authority, approval rules, and close posture are loaded for operator review.",
    statusLabel: checkingCount > 0
      ? `${checkingCount} checking`
      : reviewCount > 0
        ? `${reviewCount} review`
        : "Ready",
    statusVariant,
    loadedCountLabel: `${loadedCount} / ${cards.length}`,
    reviewCountLabel: String(reviewCount),
    listLabel: "Fund operations configuration surfaces",
    cards
  };
}

function buildLedgerMappingControlCard(
  workbench: LedgerMappingWorkbench | null,
  loading: boolean
): SettingsOperationsControlCard {
  if (!workbench) {
    return buildUnavailableOperationsControlCard(
      "ledger-mapping",
      "Ledger Mapping Workbench",
      "Maps fund accounts to ledger groups and exposes unmapped posting destinations.",
      FUND_STRUCTURE_API_ENDPOINTS.ledgerMappingWorkbench,
      "/settings#fund-operations-control-center",
      loading
    );
  }

  const unmapped = workbench.unmappedAccountCount;
  const statusVariant: SettingsOperationsControlCard["statusVariant"] = unmapped > 0 ? "warning" : "success";
  const firstUnmapped = workbench.accounts.find((account) => account.mapping.requiresUserMapping);
  return {
    id: "ledger-mapping",
    title: "Ledger Mapping Workbench",
    description: "Maps fund accounts to ledger groups and exposes unmapped posting destinations.",
    statusLabel: unmapped > 0 ? `${unmapped} unmapped` : "All mapped",
    statusVariant,
    endpointHref: FUND_STRUCTURE_API_ENDPOINTS.ledgerMappingWorkbench,
    routeHref: "/settings#fund-operations-control-center",
    routeLabel: "Review mappings",
    routeAriaLabel: "Review ledger mapping workbench",
    metrics: [
      { label: "Accounts", value: String(workbench.accountCount), tone: "default" },
      { label: "Mapped", value: String(workbench.mappedAccountCount), tone: unmapped === 0 ? "success" : "default" },
      { label: "Unmapped", value: String(unmapped), tone: unmapped > 0 ? "warning" : "muted" },
      { label: "Ledger groups", value: String(workbench.ledgerGroups.length), tone: "muted" }
    ],
    detail: firstUnmapped
      ? `${firstUnmapped.accountCode} needs mapping. ${firstUnmapped.recommendedAction}`
      : `Mapping view generated ${formatSettingsUtcMinute(workbench.asOf)}.`
  };
}

function buildRolePermissionControlCard(
  catalog: RolePermissionCatalog | null,
  session: SessionInfo | null,
  loading: boolean
): SettingsOperationsControlCard {
  if (!catalog) {
    return buildUnavailableOperationsControlCard(
      "role-permissions",
      "Role and Permission Studio",
      "Shows built-in roles, permission groups, and the active operator authority profile.",
      AUTH_API_ENDPOINTS.roles,
      AUTH_API_ENDPOINTS.roles,
      loading
    );
  }

  const activeRole = session ? catalog.roles.find((role) => (
    role.role === session.role || role.displayName === session.role
  )) : null;
  const permissionCount = activeRole?.permissions.length ?? 0;
  const statusVariant: SettingsOperationsControlCard["statusVariant"] = session && !activeRole ? "warning" : "success";
  return {
    id: "role-permissions",
    title: "Role and Permission Studio",
    description: "Shows built-in roles, permission groups, and the active operator authority profile.",
    statusLabel: activeRole ? `${activeRole.displayName} active` : `${catalog.roles.length} roles`,
    statusVariant,
    endpointHref: AUTH_API_ENDPOINTS.roles,
    routeHref: AUTH_API_ENDPOINTS.roles,
    routeLabel: "Open catalog",
    routeAriaLabel: "Open role and permission catalog service",
    metrics: [
      { label: "Roles", value: String(catalog.roles.length), tone: "default" },
      { label: "Permissions", value: String(catalog.permissions.length), tone: "default" },
      { label: "Current grants", value: activeRole ? String(permissionCount) : "—", tone: activeRole ? "success" : "warning" },
      { label: "Built-in", value: String(catalog.roles.filter((role) => role.isBuiltIn).length), tone: "muted" }
    ],
    detail: activeRole
      ? activeRole.description
      : "Active session role was not found in the loaded role catalog."
  };
}

function buildApprovalPolicyControlCard(
  matrix: OperationsApprovalPolicyMatrix | null,
  loading: boolean
): SettingsOperationsControlCard {
  if (!matrix) {
    return buildUnavailableOperationsControlCard(
      "approval-policy",
      "Approval Policy Matrix",
      "Shows required permissions, reviewer separation, report-pack, and checklist-control approval rules.",
      WORKSTATION_API_ENDPOINTS.operationsContinuityApprovalPolicyMatrix,
      WORKSTATION_API_ENDPOINTS.operationsContinuityApprovalPolicyMatrix,
      loading
    );
  }

  const independentRules = matrix.rows.filter((row) => row.requiresIndependentReviewer).length;
  const reportPackRules = matrix.rows.filter((row) => row.requiresReportPack).length;
  const checklistRules = matrix.rows.filter((row) => row.requiresChecklistControlApprovals).length;
  return {
    id: "approval-policy",
    title: "Approval Policy Matrix",
    description: "Shows required permissions, reviewer separation, report-pack, and checklist-control approval rules.",
    statusLabel: `${matrix.rows.length} rules`,
    statusVariant: "success",
    endpointHref: WORKSTATION_API_ENDPOINTS.operationsContinuityApprovalPolicyMatrix,
    routeHref: WORKSTATION_API_ENDPOINTS.operationsContinuityApprovalPolicyMatrix,
    routeLabel: "Open matrix",
    routeAriaLabel: "Open approval policy matrix service",
    metrics: [
      { label: "Version", value: matrix.version, tone: "muted" },
      { label: "Rules", value: String(matrix.rows.length), tone: "default" },
      { label: "Independent", value: String(independentRules), tone: independentRules > 0 ? "success" : "warning" },
      { label: "Report pack", value: String(reportPackRules), tone: "default" }
    ],
    detail: checklistRules > 0
      ? `${checklistRules} rule${checklistRules === 1 ? "" : "s"} require checklist-control approvals before close.`
      : `Policy generated ${formatSettingsUtcMinute(matrix.generatedAtUtc)}.`
  };
}

function buildCloseCalendarControlCard(
  calendar: OperationsCloseCalendar | null,
  loading: boolean
): SettingsOperationsControlCard {
  if (!calendar) {
    return buildUnavailableOperationsControlCard(
      "close-calendar",
      "Account Close Calendar",
      "Tracks period close due dates, blockers, checklist work, and approval progress by fund account.",
      WORKSTATION_API_ENDPOINTS.operationsContinuityCloseCalendar,
      "/accounting/operations-continuity",
      loading
    );
  }

  const blocked = calendar.items.filter((item) => item.blockerCount > 0 || item.status === "Blocked").length;
  const ready = calendar.items.filter((item) => item.isReadyToClose).length;
  const openChecklist = calendar.items.reduce((sum, item) => sum + item.openChecklistCount, 0);
  const nextDue = [...calendar.items]
    .filter((item) => item.nextDueDate)
    .sort((left, right) => String(left.nextDueDate).localeCompare(String(right.nextDueDate)))[0];
  const statusVariant: SettingsOperationsControlCard["statusVariant"] = blocked > 0
    ? "danger"
    : openChecklist > 0
      ? "warning"
      : "success";

  return {
    id: "close-calendar",
    title: "Account Close Calendar",
    description: "Tracks period close due dates, blockers, checklist work, and approval progress by fund account.",
    statusLabel: blocked > 0 ? `${blocked} blocked` : `${ready}/${calendar.items.length} ready`,
    statusVariant,
    endpointHref: WORKSTATION_API_ENDPOINTS.operationsContinuityCloseCalendar,
    routeHref: nextDue?.route ?? "/accounting/operations-continuity",
    routeLabel: "Open close workflow",
    routeAriaLabel: "Open account close workflow",
    metrics: [
      { label: "Workflows", value: String(calendar.items.length), tone: "default" },
      { label: "Ready", value: String(ready), tone: ready > 0 ? "success" : "muted" },
      { label: "Open checks", value: String(openChecklist), tone: openChecklist > 0 ? "warning" : "success" },
      { label: "Blockers", value: String(blocked), tone: blocked > 0 ? "danger" : "success" }
    ],
    detail: nextDue
      ? `${nextDue.periodId}: ${nextDue.nextDueLabel ?? "Next close task"} due ${formatSettingsDateOnly(nextDue.nextDueDate)} for ${nextDue.nextDueOwner ?? "unassigned"}.`
      : `Close calendar generated ${formatSettingsUtcMinute(calendar.generatedAtUtc)}.`
  };
}

function buildUnavailableOperationsControlCard(
  id: string,
  title: string,
  description: string,
  endpointHref: string,
  routeHref: string,
  loading: boolean
): SettingsOperationsControlCard {
  return {
    id,
    title,
    description,
    statusLabel: loading ? "Checking" : "Unavailable",
    statusVariant: "outline",
    endpointHref,
    routeHref,
    routeLabel: "Open service",
    routeAriaLabel: `Open ${title} service detail`,
    metrics: [
      { label: "Loaded", value: "No", tone: loading ? "muted" : "warning" },
      { label: "Access", value: "Read", tone: "muted" }
    ],
    detail: loading
      ? "Waiting for workspace settings to finish loading."
      : "This configuration data did not load during the workspace refresh."
  };
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
      ? `Checking ${counts.checking} diagnostic service${counts.checking === 1 ? "" : "s"}; ${counts.loaded} already loaded.`
      : counts.failed > 0
        ? `${counts.failed} diagnostic service${counts.failed === 1 ? "" : "s"} did not load during the workspace refresh. Open diagnostics for technical evidence.`
        : "All diagnostic services represented on this page are loaded.",
    diagnosticListLabel: "Diagnostic service availability",
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

function buildRuntimeCapabilitySection(
  capabilities: FeatureCapabilitySettingsResponse | null
): SettingsRuntimeCapabilitySection {
  if (!capabilities) {
    return {
      title: "Runtime feature capabilities",
      description: "Toggle module-declared workstation feature gates without editing configuration by hand.",
      statusLabel: "Checking",
      statusVariant: "warning",
      summary: "Capability settings are still loading from Meridian.",
      listLabel: "Runtime feature capability toggles",
      toggles: []
    };
  }

  const toggles = capabilities.capabilities.map((capability): SettingsRuntimeCapabilityToggle => ({
    capabilityKey: capability.capabilityKey,
    displayName: capability.displayName,
    description: capability.description,
    isEnabled: capability.isEnabled,
    statusLabel: capability.isEnabled ? "Enabled" : "Disabled",
    statusVariant: capability.isEnabled ? "success" : "warning",
    defaultLabel: capability.defaultEnabled ? "Default on" : "Default off",
    overrideLabel: capability.isOverridden ? "Configured override" : "Using default",
    canToggle: capability.canToggle,
    disabledReason: capability.disabledReason,
    ariaLabel: `${capability.isEnabled ? "Disable" : "Enable"} ${capability.displayName}`
  }));
  const disabled = toggles.filter((toggle) => !toggle.isEnabled).length;
  const permanent = toggles.filter((toggle) => !toggle.canToggle).length;

  return {
    title: "Runtime feature capabilities",
    description: "Toggle module-declared workstation feature gates without editing configuration by hand.",
    statusLabel: disabled > 0 ? `${disabled} disabled` : "All enabled",
    statusVariant: disabled > 0 ? "warning" : "success",
    summary: disabled > 0
      ? `${disabled} optional capability ${disabled === 1 ? "is" : "are"} disabled; ${permanent} required ${permanent === 1 ? "capability stays" : "capabilities stay"} locked on.`
      : `${toggles.length} declared capability ${toggles.length === 1 ? "is" : "are"} enabled; ${permanent} required ${permanent === 1 ? "capability is" : "capabilities are"} locked on.`,
    listLabel: "Runtime feature capability toggles",
    toggles
  };
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
      ? `Checking ${checking} service coverage group${checking === 1 ? "" : "s"} across the browser workstation.`
      : failed > 0
        ? `${failed} service coverage group${failed === 1 ? "" : "s"} needs attention before the browser can claim full workflow reachability.`
        : `${loaded} service coverage group${loaded === 1 ? "" : "s"} are represented by browser routes and service access points.`,
    backendCapabilityListLabel: "Service coverage by workstation route",
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
      endpointCountLabel: `${endpointCount} service${endpointCount === 1 ? "" : "s"}`,
      loadedCountLabel: "Checking",
      statusLabel: "Checking",
      statusDetail: "Workspace data is refreshing this service group.",
      statusVariant: "warning",
      endpoints
    };
  }

  if (error) {
    return {
      ...definition,
      endpointCountLabel: `${endpointCount} service${endpointCount === 1 ? "" : "s"}`,
      loadedCountLabel: "0 loaded",
      statusLabel: "Unavailable",
      statusDetail: formatSettingsVisibleWorkspaceError(error),
      statusVariant: "danger",
      endpoints
    };
  }

  if (definition.isAvailable(payload)) {
    return {
      ...definition,
      endpointCountLabel: `${endpointCount} service${endpointCount === 1 ? "" : "s"}`,
      loadedCountLabel: `${endpointCount} mapped`,
      statusLabel: "Surfaced",
      statusDetail: `${definition.workspaceLabel} has a browser route and mapped service access points. Read-only services open directly; templates and change actions stay reference-only.`,
      statusVariant: "success",
      endpoints
    };
  }

  return {
    ...definition,
    endpointCountLabel: `${endpointCount} service${endpointCount === 1 ? "" : "s"}`,
    loadedCountLabel: "0 loaded",
    statusLabel: "Unavailable",
    statusDetail: formatSettingsVisibleWorkspaceError(payload.error ?? definition.unavailableDetail),
    statusVariant: "danger",
    endpoints
  };
}

function isBrowserNavigableEndpoint(endpoint: CapabilityEndpointDefinition): boolean {
  return endpoint.method === "GET" && !endpoint.href.includes("{");
}

function formatSettingsVisibleWorkspaceError(error: string | null | undefined): string {
  const detail = error?.trim();
  if (!detail) {
    return "Workspace data unavailable. Try again or open diagnostics.";
  }

  return looksLikeRawSettingsTechnicalResponse(detail)
    ? "Workspace data unavailable. Try again or open diagnostics."
    : detail;
}

function looksLikeRawSettingsTechnicalResponse(value: string): boolean {
  return /<!doctype\s+html/i.test(value)
    || /<html(?:\s|>)/i.test(value)
    || /\bfile not found\b/i.test(value)
    || /^404(?:\s|$|:|-)/i.test(value)
    || /\bhttp\s+error\s+404\b/i.test(value);
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
      statusDetail: "Workspace data is refreshing this diagnostic service.",
      tone: "warning",
      badgeVariant: "warning",
      isLoading
    };
  }

  if (error) {
    return {
      ...endpoint,
      statusLabel: "Failed",
      statusDetail: formatSettingsVisibleWorkspaceError(error),
      tone: "danger",
      badgeVariant: "danger",
      isLoading: false
    };
  }

  if (endpoint.isAvailable(payload)) {
    return {
      ...endpoint,
      statusLabel: "Loaded",
      statusDetail: "Data is represented in the current workstation view.",
      tone: "success",
      badgeVariant: "success",
      isLoading: false
    };
  }

  return {
    ...endpoint,
    statusLabel: "Unavailable",
    statusDetail: formatSettingsVisibleWorkspaceError(payload.error ?? endpoint.unavailableDetail),
    tone: "danger",
    badgeVariant: "danger",
    isLoading: false
  };
}
