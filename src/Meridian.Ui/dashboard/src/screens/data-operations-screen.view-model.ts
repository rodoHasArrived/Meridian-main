import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import * as workstationApi from "@/lib/api";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import { WORKSTATION_ROUTE_CATALOG, workstationRouteWithQuery } from "@/lib/workspace";
import type {
  BackfillProgressResponse,
  BackfillTriggerRequest,
  BackfillTriggerResult,
  DataOperationsBackfillRecord,
  DataOperationsExportRecord,
  DataOperationsProviderRecord,
  DataOperationsWorkspaceResponse,
  ProviderConnectionRow,
  ProviderCredentialVerificationResult,
  ProviderKind,
  ProviderKindMeta,
  ProviderRoutingBinding,
  ProviderRoutingConnection,
  ProviderRoutingTrustSnapshot,
  ProviderSetupRequest,
  ProviderSetupResult
} from "@/types";

export interface BackfillFormState {
  provider: string;
  symbols: string;
  from: string;
  to: string;
}

export type BackfillPhase = "idle" | "previewing" | "running";

export interface BackfillTriggerState {
  validationError: string | null;
  feedbackText: string | null;
  feedbackDetails: string[];
  feedbackTone: "warning" | "danger" | null;
  canPreview: boolean;
  canRun: boolean;
  previewButtonLabel: string;
  runButtonLabel: string;
  previewButtonAriaLabel: string;
  runButtonAriaLabel: string;
  symbolsHelpText: string;
  statusAnnouncement: string;
  dialogState: BackfillDialogState;
}

export interface BackfillDialogFieldState {
  id: string;
  label: string;
  ariaLabel: string;
  placeholder?: string;
  describedBy?: string;
  autoFocus?: boolean;
  disabled: boolean;
  disabledReason: string | null;
}

export interface BackfillProviderOptionState {
  value: string;
  label: string;
  description: string;
  badge: string;
}

export interface BackfillDialogSummaryItemState {
  id: string;
  label: string;
  value: string;
  tone: "default" | "warning";
}

export interface BackfillDialogActionState {
  label: string;
  ariaLabel: string;
  disabled: boolean;
  disabledReason: string | null;
  busy: boolean;
  busyLabel: string;
}

export interface BackfillDialogState {
  titleId: string;
  descriptionId: string;
  formLabel: string;
  closeButtonLabel: string;
  closeButtonDisabledReason: string | null;
  summaryItems: BackfillDialogSummaryItemState[];
  providerField: BackfillDialogFieldState;
  providerOptions: BackfillProviderOptionState[];
  selectedProviderDetail: string;
  symbolsField: BackfillDialogFieldState;
  fromField: BackfillDialogFieldState;
  toField: BackfillDialogFieldState;
  previewAction: BackfillDialogActionState;
  runAction: BackfillDialogActionState;
  formStatusLabel: string;
  formStatusTone: "default" | "warning" | "danger" | "success";
}

export type BackfillResultCardTone = "warning" | "success" | "danger";

export interface BackfillResultDetailRow {
  id: string;
  label: string;
  value: string;
}

export interface BackfillResultCardState {
  title: string;
  statusLabel: string;
  tone: BackfillResultCardTone;
  ariaLabel: string;
  rows: BackfillResultDetailRow[];
  errorText: string | null;
}

export interface BackfillTriggerServices {
  preview: (request: BackfillTriggerRequest) => Promise<BackfillTriggerResult>;
  run: (request: BackfillTriggerRequest) => Promise<BackfillTriggerResult>;
  getProgress: () => Promise<BackfillProgressResponse>;
}

export interface ProviderSetupLifecycleServices {
  onConfigured?: () => Promise<void> | void;
}

export interface DataOperationsProviderEvidence {
  providerConnections?: ProviderConnectionRow[] | null;
  providerRoutingConnections?: ProviderRoutingConnection[] | null;
  providerRoutingBindings?: ProviderRoutingBinding[] | null;
  providerRoutingTrustSnapshots?: ProviderRoutingTrustSnapshot[] | null;
  providerRoutingRefreshing?: boolean;
  onProviderRoutingRefresh?: () => Promise<void> | void;
}

export interface DataOperationsEmptyState {
  title: string;
  description: string;
}

export interface DataOperationsRouteFocusActionState {
  label: string;
  href: string;
  ariaLabel: string;
}

export interface DataOperationsRouteFocusCardState {
  id: string;
  role: "region" | "status";
  ariaLabel: string;
  eyebrow: string;
  title: string;
  description: string;
  rows: DataOperationsDetailField[];
  action: DataOperationsRouteFocusActionState | null;
}

export interface DataOperationsSectionState<T> {
  rows: T[];
  hasRows: boolean;
  emptyState: DataOperationsEmptyState;
}

export interface DataOperationsBackfillSectionState extends DataOperationsSectionState<DataOperationsBackfillRow> {
  tableLabel: string;
  description: string;
}

export interface DataOperationsExportSectionState extends DataOperationsSectionState<DataOperationsExportRow> {
  tableLabel: string;
  description: string;
  selectedRowId: string | null;
  selectedDetail: DataOperationsExportDetailState | null;
  detailEmptyState: DataOperationsEmptyState | null;
}

export interface DataOperationsProviderSectionState extends DataOperationsSectionState<DataOperationsProviderRow> {
  title: string;
  subtitle: string;
  statusLabel: string;
  statusTone: "success" | "warning" | "danger";
  commandActions: DataOperationsProviderCommandActionState[];
  providerOptions: DataOperationsProviderOptionState[];
  summaryCards: DataOperationsProviderSummaryCardState[];
  tableLabel: string;
  description: string;
  detailPanelId: string;
  selectedRowId: string | null;
  selectedDetail: DataOperationsProviderDetailState | null;
  detailEmptyState: DataOperationsEmptyState | null;
  diagnosticsSummary: string;
  refreshAction: {
    label: string;
    ariaLabel: string;
    busy: boolean;
    disabled: boolean;
    disabledReason: string | null;
  };
}

export type DataOperationsProviderStatus = DataOperationsProviderRecord["status"] | "Blocked";

export interface DataOperationsProviderCommandActionState {
  id: "add" | "refresh" | "diagnostics" | "settings";
  label: string;
  ariaLabel: string;
  href: string | null;
  variant: "default" | "outline";
  disabled: boolean;
  disabledReason: string | null;
  busy: boolean;
}

export interface DataOperationsProviderOptionState {
  value: string;
  label: string;
  description: string;
}

export interface DataOperationsProviderSummaryCardState {
  id: "health" | "credentials" | "verification" | "latency" | "workflows" | "action";
  label: string;
  value: string;
  detail: string;
  tone: "default" | "success" | "warning" | "danger";
}

export interface DataOperationsProviderRow {
  providerId: string;
  provider: string;
  rowId: string;
  detailPanelId: string;
  status: DataOperationsProviderStatus;
  rowClassName: DataOperationsStatusRowClassName;
  capability: string;
  credentialText: string;
  verificationText: string;
  lastSuccessfulText: string;
  affectedWorkflowsText: string;
  actionLabel: string;
  actionHref: string;
  latencyText: string;
  trustScoreText: string;
  signalSourceText: string;
  note: string;
  statusTone: "success" | "warning" | "danger";
  trustFields: DataOperationsDetailField[];
  reasonCodeText: string;
  recommendedActionText: string;
  gateImpactText: string;
  selected: boolean;
  expanded: boolean;
  ariaLabel: string;
  selectAriaLabel: string;
  detailDescription: string;
}

export interface DataOperationsDetailField {
  id: string;
  label: string;
  value: string;
}

export type DataOperationsProviderTrustField = DataOperationsDetailField;

export interface DataOperationsBackfillRow {
  jobId: string;
  rowId: string;
  detailPanelId: string;
  scope: string;
  provider: string;
  status: DataOperationsBackfillRecord["status"];
  rowClassName: DataOperationsStatusRowClassName;
  progress: string;
  updatedAt: string;
  selected: boolean;
  expanded: boolean;
  detailText: string;
  ariaLabel: string;
  selectAriaLabel: string;
  detailDescription: string;
}

export interface DataOperationsBackfillDetailState {
  id: string;
  title: string;
  description: string;
  ariaLabel: string;
  statusLabel: DataOperationsBackfillRecord["status"];
  statusVariant: "default" | "outline" | "warning";
  rows: DataOperationsDetailField[];
}

export interface DataOperationsProviderDetailState {
  id: string;
  providerId: string;
  title: string;
  subtitle: string;
  description: string;
  ariaLabel: string;
  status: DataOperationsProviderStatus;
  statusTone: "success" | "warning" | "danger";
  fields: DataOperationsDetailField[];
  overviewFields: DataOperationsDetailField[];
  credentialFields: DataOperationsDetailField[];
  diagnostics: DataOperationsProviderDiagnosticRow[];
  diagnosticsEmptyState: DataOperationsEmptyState | null;
  activeTab: DataOperationsProviderTabId;
  tabs: DataOperationsProviderTabState[];
  verifyAction: DataOperationsProviderVerifyActionState;
  actionText: string;
  reasonCodeText: string;
  gateImpactText: string;
}

export type DataOperationsProviderTabId = "overview" | "credentials" | "diagnostics";

export interface DataOperationsProviderTabState {
  id: DataOperationsProviderTabId;
  label: string;
  selected: boolean;
  ariaControls: string;
}

export interface DataOperationsProviderDiagnosticRow {
  id: string;
  label: string;
  status: "pass" | "warning" | "fail" | "pending";
  statusLabel: string;
  detail: string;
}

export interface DataOperationsProviderVerifyActionState {
  label: string;
  ariaLabel: string;
  busy: boolean;
  disabled: boolean;
  disabledReason: string | null;
  statusLabel: string;
  statusTone: "default" | "success" | "warning" | "danger";
  details: string[];
}

export interface DataOperationsExportRow {
  exportId: string;
  rowId: string;
  detailPanelId: string;
  profile: string;
  target: string;
  status: DataOperationsExportRecord["status"];
  statusLabel: string;
  statusVariant: "success" | "warning" | "paper";
  statusTone: "success" | "warning" | "paper";
  rowClassName: DataOperationsStatusRowClassName;
  rows: string;
  updatedAt: string;
  summaryText: string;
  detailFields: DataOperationsDetailField[];
  actionText: string;
  selected: boolean;
  expanded: boolean;
  ariaLabel: string;
  selectAriaLabel: string;
  detailDescription: string;
}

export interface DataOperationsExportDetailState {
  id: string;
  title: string;
  subtitle: string;
  description: string;
  ariaLabel: string;
  statusLabel: DataOperationsExportRecord["status"];
  statusVariant: "success" | "warning" | "paper";
  fields: DataOperationsDetailField[];
  actionText: string;
}

export interface DataOperationsPresentationState {
  providerSection: DataOperationsProviderSectionState;
  backfillSection: DataOperationsBackfillSectionState;
  exportSection: DataOperationsExportSectionState;
  selectedBackfillDetail: DataOperationsBackfillDetailState | null;
  backfillDetailEmptyState: DataOperationsEmptyState | null;
  routeFocusCard: DataOperationsRouteFocusCardState;
}

export interface DataOperationsLoadingChipState {
  label: string;
  value: string;
}

export interface DataOperationsLoadingActionState {
  id: string;
  label: string;
  href: string;
  ariaLabel: string;
  variant: "default" | "outline";
}

export interface DataOperationsLoadingState {
  title: string;
  description: string;
  statusLabel: string;
  detail: string;
  regionLabel: string;
  role: "status";
  ariaLive: "polite";
  ariaBusy: true;
  chips: DataOperationsLoadingChipState[];
  actions: DataOperationsLoadingActionState[];
}

export type DataOperationsStatusRowClassName =
  | "bg-background/50"
  | "bg-success/5"
  | "bg-warning/5"
  | "bg-danger/5"
  | "bg-paper/5";

// --- Provider setup types ---

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
  providerKindField: ProviderSetupSelectFieldState;
  selectedProviderSummary: ProviderSetupSummaryState;
  displayNameField: ProviderSetupTextFieldState;
  environmentField: ProviderSetupEnvironmentFieldState;
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

export interface ProviderSetupSummaryState {
  providerLabel: string;
  description: string;
  rows: DataOperationsDetailField[];
  noCredentialMessage: string | null;
}

export interface ProviderSetupNextActionState {
  id: "live-quotes" | "backfill" | "readiness" | "security-master";
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

export const PROVIDER_KIND_CATALOG: ProviderKindMeta[] = [
  {
    kind: "polygon",
    label: "Polygon.io",
    description: "Real-time and historical US equities, options, forex, and crypto.",
    needsApiKey: true,
    needsApiSecret: false,
    needsEndpoint: false,
    defaultCapabilities: ["streaming", "backfill", "reference"]
  },
  {
    kind: "databento",
    label: "Databento",
    description: "High-quality historical bars and tick data across asset classes.",
    needsApiKey: true,
    needsApiSecret: false,
    needsEndpoint: false,
    defaultCapabilities: ["backfill", "reference"]
  },
  {
    kind: "alpaca",
    label: "Alpaca",
    description: "Commission-free US equities broker with market data and paper trading.",
    needsApiKey: true,
    needsApiSecret: true,
    needsEndpoint: false,
    defaultCapabilities: ["streaming", "backfill", "brokerage"]
  },
  {
    kind: "interactivebrokers",
    label: "Interactive Brokers",
    description: "Full-service brokerage with live order routing and market data.",
    needsApiKey: false,
    needsApiSecret: false,
    needsEndpoint: true,
    defaultCapabilities: ["streaming", "brokerage"]
  },
  {
    kind: "yahoo",
    label: "Yahoo Finance",
    description: "Free end-of-day historical prices. No API key required.",
    needsApiKey: false,
    needsApiSecret: false,
    needsEndpoint: false,
    defaultCapabilities: ["backfill"]
  },
  {
    kind: "custom",
    label: "Custom endpoint",
    description: "Connect a custom or internal data provider via REST endpoint.",
    needsApiKey: true,
    needsApiSecret: false,
    needsEndpoint: true,
    defaultCapabilities: []
  }
];

export const ALL_CAPABILITIES: Array<{ id: string; label: string; description: string }> = [
  { id: "streaming", label: "Live streaming", description: "Real-time price ticks and quote updates" },
  { id: "backfill", label: "Historical backfill", description: "OHLCV bars and tick history" },
  { id: "reference", label: "Reference data", description: "Security master, identifiers, corporate actions" },
  { id: "brokerage", label: "Brokerage / order routing", description: "Order submission and fill feed" }
];

const defaultProviderSetupForm: ProviderSetupFormState = {
  kind: "yahoo",
  displayName: "Yahoo Finance",
  environment: "paper",
  liveAcknowledged: false,
  apiKey: "",
  apiSecret: "",
  endpoint: "",
  capabilities: ["backfill"]
};

// --- Backfill setup defaults ---

export const BACKFILL_PROVIDER_OPTIONS: BackfillProviderOptionState[] = [
  {
    value: "yahoo",
    label: "Yahoo Finance",
    description: "Credential-free daily and intraday historical bars; best first backfill path.",
    badge: "No key"
  },
  {
    value: "stooq",
    label: "Stooq",
    description: "Credential-free daily historical fallback with conservative rate limits.",
    badge: "No key"
  },
  {
    value: "alpaca",
    label: "Alpaca",
    description: "Historical bars through Alpaca; requires valid paper or live API keys.",
    badge: "Key"
  },
  {
    value: "polygon",
    label: "Polygon.io",
    description: "Historical and reference backfill for paid Polygon plans.",
    badge: "Key"
  },
  {
    value: "composite",
    label: "Composite fallback",
    description: "Let Meridian rotate across configured historical providers.",
    badge: "Auto"
  }
];

const defaultBackfillServices: BackfillTriggerServices = {
  preview: (request) => workstationApi.previewBackfill(request),
  run: (request) => workstationApi.triggerBackfill(request),
  getProgress: () => workstationApi.getBackfillProgress()
};
const defaultProviderSetupLifecycle: ProviderSetupLifecycleServices = {};

const defaultBackfillForm: BackfillFormState = {
  provider: "",
  symbols: "",
  from: "",
  to: ""
};

const NO_CONFIGURED_BACKFILL_PROVIDER_MESSAGE =
  "Configure a provider before previewing a backfill.";

const SELECT_CONFIGURED_BACKFILL_PROVIDER_MESSAGE =
  "Select a configured provider before previewing a backfill.";

const BACKFILL_PROVIDER_ALIASES: Record<string, string> = {
  "alpaca": "alpaca",
  "composite": "composite",
  "composite fallback": "composite",
  "polygon": "polygon",
  "polygon.io": "polygon",
  "stooq": "stooq",
  "yahoo": "yahoo",
  "yahoo finance": "yahoo"
};

export const DATA_BACKFILL_DETAIL_PANEL_ID = "data-backfill-detail-panel";
export const DATA_BACKFILL_ROUTE_FOCUS_CARD_ID = "data-backfill-route-focus";
export const DATA_EXPORT_DETAIL_PANEL_ID = "data-export-detail-panel";
export const DATA_PROVIDER_DETAIL_PANEL_ID = "data-provider-detail-panel";

export function useDataOperationsViewModel(
  data: DataOperationsWorkspaceResponse | null,
  pathname: string,
  services: BackfillTriggerServices = defaultBackfillServices,
  providerSetupLifecycle: ProviderSetupLifecycleServices = defaultProviderSetupLifecycle,
  providerEvidence: DataOperationsProviderEvidence = {}
) {
  const [selectedProviderId, setSelectedProviderId] = useState<string | null>(null);
  const [selectedProviderTab, setSelectedProviderTab] = useState<DataOperationsProviderTabId>("overview");
  const [selectedBackfillId, setSelectedBackfillId] = useState<string | null>(null);
  const [selectedExportId, setSelectedExportId] = useState<string | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [form, setForm] = useState<BackfillFormState>(defaultBackfillForm);
  const [preview, setPreview] = useState<BackfillTriggerResult | null>(null);
  const [result, setResult] = useState<BackfillTriggerResult | null>(null);
  const [error, setError] = useState<ApiErrorDisplay | null>(null);
  const [busy, setBusy] = useState(false);
  const [phase, setPhase] = useState<BackfillPhase>("idle");
  const backfillCommandRevisionRef = useRef(0);

  // Provider setup state
  const [providerSetupOpen, setProviderSetupOpen] = useState(false);
  const [providerForm, setProviderForm] = useState<ProviderSetupFormState>(defaultProviderSetupForm);
  const [providerPhase, setProviderPhase] = useState<ProviderSetupPhase>("idle");
  const [providerSetupResult, setProviderSetupResult] = useState<ProviderSetupResult | null>(null);
  const [providerSetupError, setProviderSetupError] = useState<ApiErrorDisplay | null>(null);
  const providerSetupCommandRevisionRef = useRef(0);
  const [providerVerifyProviderId, setProviderVerifyProviderId] = useState<string | null>(null);
  const [providerVerifyBusy, setProviderVerifyBusy] = useState(false);
  const [providerVerifyResult, setProviderVerifyResult] = useState<ProviderCredentialVerificationResult | null>(null);
  const [providerVerifyError, setProviderVerifyError] = useState<ApiErrorDisplay | null>(null);
  const providerVerifyCommandRevisionRef = useRef(0);
  const configuredBackfillProviders = useMemo(
    () => data?.providers ?? [],
    [data?.providers]
  );

  const nextBackfillCommandRevision = useCallback(() => {
    const revision = backfillCommandRevisionRef.current + 1;
    backfillCommandRevisionRef.current = revision;
    return revision;
  }, []);

  const isCurrentBackfillCommand = useCallback((revision: number) => (
    backfillCommandRevisionRef.current === revision
  ), []);

  const nextProviderSetupCommandRevision = useCallback(() => {
    const revision = providerSetupCommandRevisionRef.current + 1;
    providerSetupCommandRevisionRef.current = revision;
    return revision;
  }, []);

  const isCurrentProviderSetupCommand = useCallback((revision: number) => (
    providerSetupCommandRevisionRef.current === revision
  ), []);

  useEffect(() => () => {
    backfillCommandRevisionRef.current += 1;
    providerSetupCommandRevisionRef.current += 1;
    providerVerifyCommandRevisionRef.current += 1;
  }, []);

  const workstream = useMemo(() => resolveDataOperationsWorkstream(pathname), [pathname]);
  const selectedProvider = useMemo(
    () => resolveSelectedProvider(data?.providers ?? [], selectedProviderId),
    [data, selectedProviderId]
  );
  const selectedProviderRowId = selectedProvider ? buildProviderRowId(selectedProvider.provider) : selectedProviderId;
  const selectedBackfill = useMemo(
    () => resolveSelectedBackfill(data?.backfills ?? [], selectedBackfillId),
    [data, selectedBackfillId]
  );
  const selectedExport = useMemo(
    () => resolveSelectedExport(data?.exports ?? [], selectedExportId),
    [data, selectedExportId]
  );
  const presentation = useMemo(
    () => buildDataOperationsPresentationState(
      data,
      selectedBackfill?.jobId ?? null,
      workstream,
      selectedProviderRowId,
      selectedExport?.exportId ?? null,
      providerEvidence,
      selectedProviderTab,
      buildProviderVerifyActionState(
        providerVerifyBusy,
        providerVerifyProviderId,
        providerVerifyResult,
        providerVerifyError
      )
    ),
    [
      data,
      providerEvidence,
      providerVerifyBusy,
      providerVerifyError,
      providerVerifyProviderId,
      providerVerifyResult,
      selectedBackfill?.jobId,
      selectedExport?.exportId,
      selectedProviderRowId,
      selectedProviderTab,
      workstream
    ]
  );
  const loadingState = useMemo(
    () => buildDataOperationsLoadingState(workstream),
    [workstream]
  );

  const triggerState = useMemo(
    () => buildBackfillTriggerState({
      form,
      busy,
      phase,
      error,
      preview,
      result,
      configuredProviders: configuredBackfillProviders
    }),
    [busy, configuredBackfillProviders, error, form, phase, preview, result]
  );
  const previewResultCard = useMemo(
    () => preview ? buildBackfillResultCardState(preview, "preview") : null,
    [preview]
  );
  const runResultCard = useMemo(
    () => result ? buildBackfillResultCardState(result, "result") : null,
    [result]
  );

  const openBackfillDialog = useCallback(() => {
    nextBackfillCommandRevision();
    setDialogOpen(true);
    setPreview(null);
    setResult(null);
    setError(null);
    setBusy(false);
    setPhase("idle");
  }, [nextBackfillCommandRevision]);

  useEffect(() => {
    setForm((current) => {
      const options = buildBackfillProviderOptions(current.provider, configuredBackfillProviders);
      if (options.length === 0) {
        return current.provider.length > 0 ? { ...current, provider: "" } : current;
      }

      const currentProvider = normalizeBackfillProviderValue(current.provider);
      const matchingOption = options.find((option) => option.value === currentProvider);
      if (matchingOption) {
        return current.provider === matchingOption.value ? current : { ...current, provider: matchingOption.value };
      }

      return { ...current, provider: options[0].value };
    });
  }, [configuredBackfillProviders]);

  const closeBackfillDialog = useCallback(() => {
    if (busy) {
      return;
    }

    setDialogOpen(false);
  }, [busy]);

  const updateBackfillForm = useCallback((field: keyof BackfillFormState, value: string) => {
    setForm((current) => ({ ...current, [field]: value }));
    setPreview(null);
    setResult(null);
    setError(null);
  }, []);

  const previewBackfill = useCallback(async () => {
    const validationError = validateBackfillForm(form, configuredBackfillProviders);
    if (validationError) {
      setError(buildDataOperationsErrorState(validationError));
      return;
    }

    const commandRevision = nextBackfillCommandRevision();
    setBusy(true);
    setPhase("previewing");
    setError(null);
    setResult(null);

    try {
      const nextPreview = await services.preview(buildBackfillRequest(form));
      if (!isCurrentBackfillCommand(commandRevision)) {
        return;
      }
      setPreview(nextPreview);
    } catch (err) {
      if (!isCurrentBackfillCommand(commandRevision)) {
        return;
      }
      setPreview(null);
      setError(buildDataOperationsErrorState(err, "Backfill preview failed."));
    } finally {
      if (isCurrentBackfillCommand(commandRevision)) {
        setBusy(false);
        setPhase("idle");
      }
    }
  }, [configuredBackfillProviders, form, isCurrentBackfillCommand, nextBackfillCommandRevision, services]);

  const runBackfill = useCallback(async () => {
    const validationError = validateBackfillForm(form, configuredBackfillProviders);
    if (validationError) {
      setError(buildDataOperationsErrorState(validationError));
      return;
    }

    if (!preview) {
      setError(buildDataOperationsErrorState("Preview the request before running the backfill."));
      return;
    }

    const commandRevision = nextBackfillCommandRevision();
    setBusy(true);
    setPhase("running");
    setError(null);

    try {
      const nextResult = await services.run(buildBackfillRequest(form));
      if (!isCurrentBackfillCommand(commandRevision)) {
        return;
      }
      setResult(nextResult);
      await services.getProgress().catch(() => null);
    } catch (err) {
      if (!isCurrentBackfillCommand(commandRevision)) {
        return;
      }
      setError(buildDataOperationsErrorState(err, "Backfill run failed."));
    } finally {
      if (isCurrentBackfillCommand(commandRevision)) {
        setBusy(false);
        setPhase("idle");
      }
    }
  }, [configuredBackfillProviders, form, isCurrentBackfillCommand, nextBackfillCommandRevision, preview, services]);

  const openProviderSetup = useCallback(() => {
    nextProviderSetupCommandRevision();
    setProviderSetupOpen(true);
    setProviderForm(clearProviderSetupCredentials);
    setProviderSetupResult(null);
    setProviderSetupError(null);
    setProviderPhase("idle");
  }, [nextProviderSetupCommandRevision]);

  const closeProviderSetup = useCallback(() => {
    if (providerPhase === "submitting") return;
    nextProviderSetupCommandRevision();
    setProviderForm(clearProviderSetupCredentials);
    setProviderSetupOpen(false);
    setProviderSetupResult(null);
    setProviderSetupError(null);
    setProviderPhase("idle");
  }, [nextProviderSetupCommandRevision, providerPhase]);

  const updateProviderForm = useCallback((field: Exclude<keyof ProviderSetupFormState, "capabilities" | "liveAcknowledged">, value: string) => {
    setProviderForm((current) => {
      if (field === "kind") {
        const meta = PROVIDER_KIND_CATALOG.find((p) => p.kind === value);
        return {
          ...current,
          kind: value,
          displayName: meta?.label ?? current.displayName,
          environment: value === "alpaca" ? "paper" : current.environment,
          liveAcknowledged: false,
          apiKey: "",
          apiSecret: "",
          endpoint: meta?.needsEndpoint ? current.endpoint : "",
          capabilities: meta?.defaultCapabilities ?? current.capabilities
        };
      }
      return { ...current, [field]: value, liveAcknowledged: field === "environment" && value !== "live" ? false : current.liveAcknowledged };
    });
    setProviderSetupResult(null);
    setProviderSetupError(null);
  }, []);

  const setProviderLiveAcknowledged = useCallback((value: boolean) => {
    setProviderForm((current) => ({ ...current, liveAcknowledged: value }));
    setProviderSetupResult(null);
    setProviderSetupError(null);
  }, []);

  const toggleProviderCapability = useCallback((capId: string) => {
    setProviderForm((current) => {
      const has = current.capabilities.includes(capId);
      return {
        ...current,
        capabilities: has
          ? current.capabilities.filter((c) => c !== capId)
          : [...current.capabilities, capId]
      };
    });
  }, []);

  const submitProviderSetup = useCallback(async () => {
    const validationError = validateProviderSetupForm(providerForm);
    if (validationError) {
      setProviderSetupError(buildDataOperationsErrorState(validationError));
      return;
    }

    const commandRevision = nextProviderSetupCommandRevision();
    setProviderPhase("submitting");
    setProviderSetupError(null);
    setProviderSetupResult(null);

    const request: ProviderSetupRequest = {
      kind: providerForm.kind,
      displayName: providerForm.displayName.trim(),
      apiKey: providerForm.apiKey.trim() || null,
      apiSecret: providerForm.apiSecret.trim() || null,
      endpoint: providerForm.endpoint.trim() || null,
      capabilities: providerForm.capabilities,
      environment: providerForm.environment
    };

    try {
      const response = await workstationApi.setupProvider(request);
      if (!isCurrentProviderSetupCommand(commandRevision)) {
        return;
      }
      setProviderSetupResult(response);
      setProviderPhase(response.success ? "success" : "error");
      if (response.success && providerSetupLifecycle.onConfigured) {
        try {
          void Promise.resolve(providerSetupLifecycle.onConfigured()).catch(() => undefined);
        } catch {
          // Provider setup remains successful even if a follow-up refresh cannot start.
        }
      }
    } catch (err) {
      if (!isCurrentProviderSetupCommand(commandRevision)) {
        return;
      }
      setProviderSetupError(buildDataOperationsErrorState(err, "Provider setup failed."));
      setProviderPhase("error");
    } finally {
      if (isCurrentProviderSetupCommand(commandRevision)) {
        setProviderForm(clearProviderSetupCredentials);
      }
    }
  }, [isCurrentProviderSetupCommand, nextProviderSetupCommandRevision, providerForm, providerSetupLifecycle]);

  const nextProviderVerifyCommandRevision = useCallback(() => {
    const revision = providerVerifyCommandRevisionRef.current + 1;
    providerVerifyCommandRevisionRef.current = revision;
    return revision;
  }, []);

  const isCurrentProviderVerifyCommand = useCallback((revision: number) => (
    providerVerifyCommandRevisionRef.current === revision
  ), []);

  const verifySelectedProvider = useCallback(async () => {
    const providerId = presentation.providerSection.selectedDetail?.providerId;
    if (!providerId) {
      setProviderVerifyError(buildDataOperationsErrorState("Select a provider before running verification."));
      return;
    }

    const commandRevision = nextProviderVerifyCommandRevision();
    setProviderVerifyProviderId(providerId);
    setProviderVerifyBusy(true);
    setProviderVerifyResult(null);
    setProviderVerifyError(null);

    try {
      const response = await workstationApi.verifyProviderConnection(providerId);
      if (!isCurrentProviderVerifyCommand(commandRevision)) {
        return;
      }

      setProviderVerifyResult(response);
      if (providerEvidence.onProviderRoutingRefresh) {
        try {
          void Promise.resolve(providerEvidence.onProviderRoutingRefresh()).catch(() => undefined);
        } catch {
          // Verification remains complete even if the follow-up refresh cannot start.
        }
      }
    } catch (err) {
      if (!isCurrentProviderVerifyCommand(commandRevision)) {
        return;
      }
      setProviderVerifyError(buildDataOperationsErrorState(err, "Provider verification failed."));
    } finally {
      if (isCurrentProviderVerifyCommand(commandRevision)) {
        setProviderVerifyBusy(false);
      }
    }
  }, [isCurrentProviderVerifyCommand, nextProviderVerifyCommandRevision, providerEvidence, presentation.providerSection.selectedDetail?.providerId]);

  const providerSetupDialogState = useMemo(
    () => buildProviderSetupDialogState(providerPhase, providerForm, providerSetupResult),
    [providerPhase, providerForm, providerSetupResult]
  );

  return {
    workstream,
    loadingState,
    selectedProvider,
    selectedProviderId,
    selectedProviderRowId,
    selectProvider: setSelectedProviderId,
    selectedProviderTab,
    selectProviderTab: setSelectedProviderTab,
    verifySelectedProvider,
    selectedBackfill,
    selectedBackfillId,
    selectedBackfillRowId: selectedBackfill ? buildBackfillRowId(selectedBackfill.jobId) : null,
    selectBackfill: setSelectedBackfillId,
    selectedExport,
    selectedExportId,
    selectedExportRowId: selectedExport ? buildExportRowId(selectedExport.exportId) : null,
    selectExport: setSelectedExportId,
    ...presentation,
    dialogOpen,
    openBackfillDialog,
    closeBackfillDialog,
    form,
    updateBackfillForm,
    preview,
    previewResultCard,
    result,
    runResultCard,
    error,
    busy,
    phase,
    previewBackfill,
    runBackfill,
    ...triggerState,
    // Provider setup
    providerSetupOpen,
    openProviderSetup,
    closeProviderSetup,
    providerForm,
    updateProviderForm,
    setProviderLiveAcknowledged,
    toggleProviderCapability,
    providerPhase,
    providerSetupResult,
    providerSetupError,
    submitProviderSetup,
    providerSetupDialogState
  };
}

export function resolveDataOperationsWorkstream(pathname: string): "overview" | "backfills" {
  return pathname.includes("/backfills") ? "backfills" : "overview";
}

export function buildDataOperationsLoadingState(
  workstream: "overview" | "backfills" = "overview"
): DataOperationsLoadingState {
  const backfillFocus = workstream === "backfills";

  return {
    title: backfillFocus ? "Loading backfill queue" : "Loading Data workspace",
    description: backfillFocus
      ? "Waiting for historical repair jobs, provider pressure, and review-required backfills."
      : "Waiting for provider posture, market data health, and export evidence.",
    statusLabel: "Bootstrap pending",
    detail: backfillFocus
      ? "Queued and review-required jobs will appear here as soon as the workstation payload is available."
      : "Provider health, data-quality handoffs, and export readiness will appear when the workstation payload is available.",
    regionLabel: backfillFocus ? "Data backfill loading state" : "Data workspace loading state",
    role: "status",
    ariaLive: "polite",
    ariaBusy: true,
    chips: [
      { label: "Providers", value: "Pending" },
      { label: "Data quality", value: "Pending" },
      { label: backfillFocus ? "Backfills" : "Exports", value: "Pending" }
    ],
    actions: [
      {
        id: "settings",
        label: "Check provider setup",
        href: WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup,
        ariaLabel: "Open Alpaca paper provider setup while Data workspace loads",
        variant: "default"
      },
      {
        id: "quotes",
        label: "Open live quotes",
        href: WORKSTATION_ROUTE_CATALOG.dataQuotes,
        ariaLabel: "Open live quotes while Data workspace loads",
        variant: "outline"
      }
    ]
  };
}

export function buildDataOperationsPresentationState(
  data: DataOperationsWorkspaceResponse | null,
  selectedBackfillId: string | null,
  workstream: "overview" | "backfills" = "overview",
  selectedProviderId: string | null = null,
  selectedExportId: string | null = null,
  providerEvidence: DataOperationsProviderEvidence = {},
  selectedProviderTab: DataOperationsProviderTabId = "overview",
  providerVerifyAction: DataOperationsProviderVerifyActionState = buildProviderVerifyActionState(false, null, null, null)
): DataOperationsPresentationState {
  const providers = data?.providers ?? [];
  const backfills = data?.backfills ?? [];
  const exports = data?.exports ?? [];
  const selectedBackfillDetail = buildSelectedBackfillDetail(backfills, selectedBackfillId);
  const backfillDetailEmptyState = backfills.length === 0
    ? {
        title: "No backfill activity yet",
        description: "Preview a historical repair or wait for queued jobs to appear before using this detail panel."
      }
    : null;

  return {
    providerSection: buildProviderSection(
      providers,
      selectedProviderId,
      providerEvidence,
      selectedProviderTab,
      providerVerifyAction
    ),
    backfillSection: buildBackfillSection(backfills, selectedBackfillId, workstream),
    exportSection: buildExportSection(exports, selectedExportId),
    selectedBackfillDetail,
    backfillDetailEmptyState,
    routeFocusCard: buildRouteFocusCardState({
      workstream,
      selectedBackfillDetail,
      backfillDetailEmptyState
    })
  };
}

export function buildRouteFocusCardState({
  workstream,
  selectedBackfillDetail,
  backfillDetailEmptyState
}: {
  workstream: "overview" | "backfills";
  selectedBackfillDetail: DataOperationsBackfillDetailState | null;
  backfillDetailEmptyState: DataOperationsEmptyState | null;
}): DataOperationsRouteFocusCardState {
  if (workstream === "backfills") {
    if (selectedBackfillDetail) {
      return {
        id: DATA_BACKFILL_ROUTE_FOCUS_CARD_ID,
        role: "region",
        ariaLabel: "Backfill route focus",
        eyebrow: "Backfill Detail",
        title: "Backfill queue focus",
        description: selectedBackfillDetail.description,
        rows: selectedBackfillDetail.rows,
        action: null
      };
    }

    const title = backfillDetailEmptyState?.title ?? "Backfill queue focus";
    const description = backfillDetailEmptyState?.description ?? "No backfill selected.";
    return {
      id: DATA_BACKFILL_ROUTE_FOCUS_CARD_ID,
      role: "status",
      ariaLabel: "Backfill route focus empty state",
      eyebrow: "Backfill Detail",
      title,
      description,
      rows: [],
      action: null
    };
  }

  return {
    id: "data-route-focus-overview",
    role: "region",
    ariaLabel: "Data workspace route focus",
    eyebrow: "Lane Evidence",
    title: "Provider and export readiness",
    description: "Keep provider recovery, backfill pressure, and export handoffs visible while Data prepares inputs for Accounting and Reporting.",
    rows: [
      { id: "primary-checks", label: "Primary checks", value: "Providers / backfills / exports" },
      { id: "security-coverage", label: "Security coverage", value: "Accounting lane" },
      { id: "operator-handoff", label: "Operator handoff", value: "Reporting export evidence" }
    ],
    action: {
      label: "Open Security Master",
      href: WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster,
      ariaLabel: "Open Security Master in Accounting"
    }
  };
}

export function buildProviderSection(
  providers: DataOperationsProviderRecord[],
  selectedProviderId: string | null = null,
  evidence: DataOperationsProviderEvidence = {},
  selectedTab: DataOperationsProviderTabId = "overview",
  verifyAction: DataOperationsProviderVerifyActionState = buildProviderVerifyActionState(false, null, null, null)
): DataOperationsProviderSectionState {
  const records = buildProviderCenterRecords(providers, evidence);
  const selectedRecord = resolveSelectedProviderCenterRecord(records, selectedProviderId);
  const selectedRowId = selectedRecord ? buildProviderCenterRowId(selectedRecord) : null;
  const rows = records.map((record) => buildProviderRow(record, selectedRowId));
  const selectedDetail = buildSelectedProviderDetail(records, selectedRowId, selectedTab, verifyAction);
  const blockedCount = rows.filter((row) => row.statusTone === "danger").length;
  const warningCount = rows.filter((row) => row.statusTone === "warning").length;
  const verifiedCount = rows.filter((row) => row.credentialText === "Verified" || row.credentialText === "Not required").length;
  const statusLabel = rows.length === 0
    ? "No providers"
    : blockedCount > 0
      ? `${blockedCount} blocked`
      : warningCount > 0
        ? `${warningCount} need review`
        : "Continuity ready";

  return {
    title: "Provider Management",
    subtitle: "Configure, verify, monitor, and route market-data and brokerage providers used by Meridian.",
    statusLabel,
    statusTone: rows.length === 0 ? "warning" : blockedCount > 0 ? "danger" : warningCount > 0 ? "warning" : "success",
    commandActions: [
      {
        id: "add",
        label: "Add Provider",
        ariaLabel: "Configure a new data provider",
        href: null,
        variant: "default",
        disabled: false,
        disabledReason: null,
        busy: false
      },
      {
        id: "refresh",
        label: evidence.providerRoutingRefreshing ? "Refreshing..." : "Refresh Status",
        ariaLabel: evidence.providerRoutingRefreshing ? "Provider status refresh in progress" : "Refresh provider status and routing evidence",
        href: null,
        variant: "outline",
        disabled: Boolean(evidence.providerRoutingRefreshing),
        disabledReason: evidence.providerRoutingRefreshing ? "Provider routing refresh is already in progress." : null,
        busy: Boolean(evidence.providerRoutingRefreshing)
      },
      {
        id: "diagnostics",
        label: "Run Diagnostics",
        ariaLabel: selectedDetail ? `Run diagnostics for ${selectedDetail.title}` : "Run diagnostics for the selected provider",
        href: null,
        variant: "outline",
        disabled: !selectedDetail,
        disabledReason: selectedDetail ? null : "Select a provider before running diagnostics.",
        busy: verifyAction.busy
      },
      {
        id: "settings",
        label: "Open Settings",
        ariaLabel: "Open provider settings and credential management",
        href: WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup,
        variant: "outline",
        disabled: false,
        disabledReason: null,
        busy: false
      }
    ],
    providerOptions: rows.map((row) => ({
      value: row.rowId,
      label: `${row.provider} — ${row.status} — ${row.capability}`,
      description: `${row.credentialText}; ${row.verificationText}; ${row.affectedWorkflowsText}`
    })),
    summaryCards: buildProviderSummaryCards(selectedDetail, rows, verifiedCount),
    rows,
    hasRows: records.length > 0,
    tableLabel: "Provider health",
    description: "Provider trust, credential, routing, fallback, and verification posture for downstream workflows.",
    detailPanelId: DATA_PROVIDER_DETAIL_PANEL_ID,
    selectedRowId,
    selectedDetail,
    detailEmptyState: records.length === 0
      ? {
          title: "No provider selected",
          description: "Configure a provider before inspecting credential state, routing evidence, diagnostics, or recovery actions."
        }
      : null,
    diagnosticsSummary: selectedDetail
      ? selectedDetail.diagnostics.map((item) => `${item.label}: ${item.statusLabel}`).join("; ")
      : "Select a provider before running diagnostics.",
    refreshAction: {
      label: evidence.providerRoutingRefreshing ? "Refreshing..." : "Refresh Status",
      ariaLabel: evidence.providerRoutingRefreshing ? "Provider status refresh in progress" : "Refresh provider status and routing evidence",
      busy: Boolean(evidence.providerRoutingRefreshing),
      disabled: Boolean(evidence.providerRoutingRefreshing),
      disabledReason: evidence.providerRoutingRefreshing ? "Provider routing refresh is already in progress." : null
    },
    emptyState: {
      title: "No providers configured",
      description: "Add a provider before relying on live quotes, historical backfill, brokerage, Security Master, or export data."
    }
  };
}

export function buildProviderRow(
  provider: DataOperationsProviderRecord | DataOperationsProviderCenterRecord,
  selectedProviderId: string | null = null
): DataOperationsProviderRow {
  const record = isProviderCenterRecord(provider) ? provider : buildProviderCenterRecordFromWorkspace(provider);
  const rowId = buildProviderCenterRowId(record);
  const providerRecord = record.workspaceProvider;
  const connection = record.connection;
  const trust = record.trustSnapshot;
  const bindings = record.bindings;
  const latencyText = formatProviderValue(providerRecord?.latency, connection ? formatLastGoodProviderResponse(connection) : "Latency not reported");
  const trustScoreText = trust ? `${Math.round(trust.score)}% · ${trust.healthStatus}` : formatProviderValue(providerRecord?.trustScore, "Trust score not reported");
  const signalSourceText = trust?.signals.length ? trust.signals.join(", ") : formatProviderValue(providerRecord?.signalSource, "Signal source not reported");
  const reasonCodeText = formatProviderValue(providerRecord?.reasonCode, record.routingConnection?.productionReady === false ? "CERTIFICATION_PENDING" : "Reason code not reported");
  const recommendedActionText = record.routingConnection
    ? providerRoutingRecommendedAction(record.routingConnection, trust, bindings, record.routingConnection.enabled)
    : connection
      ? connection.recommendedAction
    : formatProviderValue(providerRecord?.recommendedAction, "No operator action reported");
  const gateImpactText = formatProviderValue(providerRecord?.gateImpact, providerGateImpactText(record.routingConnection, trust));
  const selected = rowId === selectedProviderId;
  const status = resolveProviderDisplayStatus(record);
  const statusTone = resolveProviderStatusTone(status);
  const credentialText = connection ? providerCredentialLabel(connection.credentialState) : "Not reported";
  const verificationText = connection ? providerVerificationLabel(connection.verificationState) : providerRoutingVerificationLabel(record.routingConnection, trust);
  const affectedWorkflows = resolveProviderWorkflows(record);

  return {
    providerId: record.providerId,
    provider: record.displayName,
    rowId,
    detailPanelId: DATA_PROVIDER_DETAIL_PANEL_ID,
    status,
    rowClassName: providerRowClassName(statusTone),
    capability: resolveProviderCapability(record),
    credentialText,
    verificationText,
    lastSuccessfulText: formatLastGoodProviderResponse(connection),
    affectedWorkflowsText: affectedWorkflows.join(", "),
    actionLabel: connection?.providerId === "alpaca" ? "Manage Alpaca" : "View Details",
    actionHref: connection?.actionHref || `/settings#provider-${record.providerId}-connection`,
    latencyText,
    trustScoreText,
    signalSourceText,
    note: providerRecord?.note ?? recommendedActionText,
    statusTone,
    trustFields: [
      {
        id: "latency",
        label: "Latency",
        value: latencyText
      },
      {
        id: "trust-score",
        label: "Trust score",
        value: trustScoreText
      },
      {
        id: "signal-source",
        label: "Signal source",
        value: signalSourceText
      },
      {
        id: "gate-impact",
        label: "Gate impact",
        value: gateImpactText
      }
    ],
    reasonCodeText,
    recommendedActionText,
    gateImpactText,
    selected,
    expanded: selected,
    ariaLabel: [
      `${selected ? "Selected" : "Inspect"} provider ${record.displayName}`,
      `Status ${status}`,
      resolveProviderCapability(record),
      providerRecord?.note ?? recommendedActionText,
      `Credential ${credentialText}`,
      `Verification ${verificationText}`,
      `Latency ${latencyText}`,
      `Trust score ${trustScoreText}`,
      `Gate impact ${gateImpactText}`,
      `Recommended action ${recommendedActionText}`
    ].join(". "),
    selectAriaLabel: `Inspect provider ${record.displayName}`,
    detailDescription: selected
      ? `Selected provider ${record.displayName}; the provider detail panel is expanded for this row.`
      : `Inspect provider ${record.displayName}; activation updates the shared provider detail panel.`
  };
}

export function buildSelectedProviderDetail(
  providers: DataOperationsProviderRecord[] | DataOperationsProviderCenterRecord[],
  selectedProviderId: string | null,
  selectedTab: DataOperationsProviderTabId = "overview",
  verifyAction: DataOperationsProviderVerifyActionState = buildProviderVerifyActionState(false, null, null, null)
): DataOperationsProviderDetailState | null {
  const records = providers.length > 0 && isProviderCenterRecord(providers[0])
    ? providers as DataOperationsProviderCenterRecord[]
    : (providers as DataOperationsProviderRecord[]).map(buildProviderCenterRecordFromWorkspace);
  const selected = resolveSelectedProviderCenterRecord(records, selectedProviderId);

  if (!selected) {
    return null;
  }

  const rowId = buildProviderCenterRowId(selected);
  const row = buildProviderRow(selected, rowId);
  const overviewFields = buildProviderOverviewFields(selected, row);
  const credentialFields = buildProviderCredentialFieldsForDetail(selected, row);
  const diagnostics = buildProviderDiagnostics(selected);

  return {
    id: DATA_PROVIDER_DETAIL_PANEL_ID,
    providerId: selected.providerId,
    title: selected.displayName,
    subtitle: row.capability,
    description: `${row.note} ${row.gateImpactText}.`,
    ariaLabel: `Provider detail for ${selected.displayName}: ${row.status}. ${row.capability}. ${row.recommendedActionText}`,
    status: row.status,
    statusTone: row.statusTone,
    fields: overviewFields,
    overviewFields,
    credentialFields,
    diagnostics,
    diagnosticsEmptyState: buildProviderDiagnosticsEmptyState(selected),
    activeTab: selectedTab,
    tabs: (["overview", "credentials", "diagnostics"] satisfies DataOperationsProviderTabId[]).map((tab) => ({
      id: tab,
      label: tab === "overview" ? "Overview" : tab === "credentials" ? "Credentials" : "Diagnostics",
      selected: selectedTab === tab,
      ariaControls: `${DATA_PROVIDER_DETAIL_PANEL_ID}-${tab}`
    })),
    verifyAction: selected.connection ? verifyAction : {
      ...verifyAction,
      disabled: true,
      disabledReason: "This provider row does not have a credential connection record yet.",
      ariaLabel: `Provider verification unavailable for ${selected.displayName}`
    },
    actionText: row.recommendedActionText,
    reasonCodeText: row.reasonCodeText,
    gateImpactText: row.gateImpactText
  };
}

export interface DataOperationsProviderCenterRecord {
  providerId: string;
  displayName: string;
  workspaceProvider: DataOperationsProviderRecord | null;
  connection: ProviderConnectionRow | null;
  routingConnection: ProviderRoutingConnection | null;
  bindings: ProviderRoutingBinding[];
  trustSnapshot: ProviderRoutingTrustSnapshot | null;
}

function isProviderCenterRecord(value: unknown): value is DataOperationsProviderCenterRecord {
  return Boolean(value && typeof value === "object" && "providerId" in value && "displayName" in value && "workspaceProvider" in value);
}

function buildProviderCenterRecords(
  providers: DataOperationsProviderRecord[],
  evidence: DataOperationsProviderEvidence
): DataOperationsProviderCenterRecord[] {
  const providerConnections = evidence.providerConnections ?? [];
  const routingConnections = evidence.providerRoutingConnections ?? [];
  const routingBindings = evidence.providerRoutingBindings ?? [];
  const trustSnapshots = evidence.providerRoutingTrustSnapshots ?? [];
  const matchedWorkspace = new Set<string>();
  const matchedRouting = new Set<string>();
  const workspaceByProviderId = new Map(
    providers
      .filter((provider) => Boolean(provider.providerId))
      .map((provider) => [normalizeProviderToken(provider.providerId ?? ""), provider] as const)
  );

  const records = providerConnections.map((connection) => {
    const workspaceProvider = workspaceByProviderId.get(normalizeProviderToken(connection.providerId))
      ?? findWorkspaceProviderForConnection(connection, providers);
    if (workspaceProvider) {
      matchedWorkspace.add(normalizeProviderToken(workspaceProvider.providerId ?? workspaceProvider.provider));
    }

    const routingConnection = findRoutingConnectionForProviderConnection(connection, routingConnections);
    if (routingConnection) {
      matchedRouting.add(normalizeProviderToken(routingConnection.connectionId));
    }

    return buildProviderCenterRecord({
      providerId: connection.providerId,
      displayName: connection.displayName,
      workspaceProvider,
      connection,
      routingConnection,
      routingBindings,
      trustSnapshots
    });
  });

  for (const provider of providers) {
    const providerKey = normalizeProviderToken(provider.providerId ?? provider.provider);
    if (!matchedWorkspace.has(providerKey)) {
      records.push(buildProviderCenterRecordFromWorkspace(provider));
    }
  }

  for (const routingConnection of routingConnections) {
    if (!matchedRouting.has(normalizeProviderToken(routingConnection.connectionId))) {
      records.push(buildProviderCenterRecord({
        providerId: routingConnection.connectionId,
        displayName: routingConnection.displayName,
        workspaceProvider: null,
        connection: null,
        routingConnection,
        routingBindings,
        trustSnapshots
      }));
    }
  }

  return records;
}

function buildProviderCenterRecord({
  providerId,
  displayName,
  workspaceProvider,
  connection,
  routingConnection,
  routingBindings,
  trustSnapshots
}: {
  providerId: string;
  displayName: string;
  workspaceProvider: DataOperationsProviderRecord | null;
  connection: ProviderConnectionRow | null;
  routingConnection: ProviderRoutingConnection | null;
  routingBindings: ProviderRoutingBinding[];
  trustSnapshots: ProviderRoutingTrustSnapshot[];
}): DataOperationsProviderCenterRecord {
  const connectionId = routingConnection?.connectionId ?? connection?.providerId ?? providerId;
  return {
    providerId,
    displayName,
    workspaceProvider,
    connection,
    routingConnection,
    bindings: routingBindings.filter((binding) => normalizeProviderToken(binding.connectionId) === normalizeProviderToken(connectionId)),
    trustSnapshot: trustSnapshots.find((snapshot) => normalizeProviderToken(snapshot.connectionId) === normalizeProviderToken(connectionId)) ?? null
  };
}

function buildProviderCenterRecordFromWorkspace(provider: DataOperationsProviderRecord): DataOperationsProviderCenterRecord {
  return {
    providerId: provider.providerId ?? normalizeProviderToken(provider.provider) || provider.provider,
    displayName: provider.displayName ?? provider.provider,
    workspaceProvider: provider,
    connection: provider.connectionSummary ?? null,
    routingConnection: null,
    bindings: [],
    trustSnapshot: null
  };
}

function findWorkspaceProviderForConnection(
  connection: ProviderConnectionRow,
  providers: DataOperationsProviderRecord[]
): DataOperationsProviderRecord | null {
  const providerId = normalizeProviderToken(connection.providerId);
  const displayName = normalizeProviderToken(connection.displayName);
  return providers.find((provider) => {
    const directId = normalizeProviderToken(provider.providerId ?? "");
    if (directId.length > 0 && directId === providerId) {
      return true;
    }

    const normalized = normalizeProviderToken(provider.displayName ?? provider.provider);
    return normalized === providerId || normalized === displayName || normalized.includes(providerId) || providerId.includes(normalized);
  }) ?? null;
}

function findRoutingConnectionForProviderConnection(
  connection: ProviderConnectionRow,
  routingConnections: ProviderRoutingConnection[]
): ProviderRoutingConnection | null {
  const providerId = normalizeProviderToken(connection.providerId);
  const displayName = normalizeProviderToken(connection.displayName);
  return routingConnections.find((routingConnection) => {
    const connectionId = normalizeProviderToken(routingConnection.connectionId);
    const familyId = normalizeProviderToken(routingConnection.providerFamilyId);
    const routingName = normalizeProviderToken(routingConnection.displayName);
    return connectionId === providerId ||
      familyId === providerId ||
      routingName === displayName ||
      connectionId.includes(providerId) ||
      providerId.includes(familyId);
  }) ?? null;
}

function resolveSelectedProviderCenterRecord(
  records: DataOperationsProviderCenterRecord[],
  selectedProviderId: string | null
): DataOperationsProviderCenterRecord | null {
  if (records.length === 0) {
    return null;
  }

  if (!selectedProviderId) {
    return records[0];
  }

  const normalized = normalizeProviderToken(selectedProviderId.replace(/^provider-row-/, ""));
  return records.find((record) =>
    normalizeProviderToken(buildProviderCenterRowId(record)) === normalizeProviderToken(selectedProviderId) ||
    normalizeProviderToken(record.providerId) === normalized ||
    normalizeProviderToken(record.displayName) === normalized) ?? records[0];
}

function buildProviderCenterRowId(record: DataOperationsProviderCenterRecord): string {
  return buildProviderRowId(record.connection?.providerId ?? record.providerId ?? record.routingConnection?.connectionId ?? record.displayName);
}

function resolveProviderDisplayStatus(record: DataOperationsProviderCenterRecord): DataOperationsProviderStatus {
  if (record.connection) {
    switch (record.connection.health) {
      case "Healthy":
        return "Healthy";
      case "Blocked":
        return "Blocked";
      case "Degraded":
        return "Degraded";
      case "Warning":
        return "Warning";
      default:
        return record.workspaceProvider?.status ?? "Warning";
    }
  }

  if (record.trustSnapshot) {
    return record.trustSnapshot.isHealthy ? "Healthy" : "Degraded";
  }

  return record.workspaceProvider?.status ?? "Warning";
}

function resolveProviderCapability(record: DataOperationsProviderCenterRecord): string {
  if (record.connection) {
    switch (record.connection.capability) {
      case "DataAndBrokerage":
        return "Data + Brokerage";
      case "Brokerage":
        return "Brokerage";
      default:
        return "Data";
    }
  }

  const bindingCapabilities = record.bindings.map((binding) => formatProviderRoutingCapability(binding.capability));
  if (bindingCapabilities.length > 0) {
    return uniqueStrings(bindingCapabilities).slice(0, 2).join(" + ");
  }

  return record.workspaceProvider?.capability ?? record.routingConnection?.connectionType ?? "Provider";
}

function resolveProviderWorkflows(record: DataOperationsProviderCenterRecord): string[] {
  if (record.connection?.affectedWorkflows.length) {
    return record.connection.affectedWorkflows;
  }

  if (record.workspaceProvider?.connectionSummary?.affectedWorkflows?.length) {
    return record.workspaceProvider.connectionSummary.affectedWorkflows;
  }

  const workflows = record.bindings.map((binding) => formatProviderRoutingCapability(binding.capability));
  if (workflows.length > 0) {
    return uniqueStrings(workflows);
  }

  return ["Workflow impact not declared"];
}

function buildProviderSummaryCards(
  detail: DataOperationsProviderDetailState | null,
  rows: DataOperationsProviderRow[],
  verifiedCount: number
): DataOperationsProviderSummaryCardState[] {
  if (!detail) {
    return [
      { id: "health", label: "Connection Health", value: "No provider", detail: "Add or select a provider.", tone: "warning" },
      { id: "credentials", label: "Credential State", value: "Unavailable", detail: "No credential record loaded.", tone: "warning" },
      { id: "verification", label: "Verification State", value: "Unavailable", detail: "Verification needs a provider row.", tone: "warning" },
      { id: "latency", label: "Latency / Last Response", value: "Not reported", detail: "No response evidence loaded.", tone: "default" },
      { id: "workflows", label: "Affected Workflows", value: "None", detail: "No downstream workflow impact loaded.", tone: "default" },
      { id: "action", label: "Recommended Action", value: "Add provider", detail: "Configure a provider before routing workflows.", tone: "warning" }
    ];
  }

  const health = findField(detail.overviewFields, "health") ?? detail.status;
  const credential = findField(detail.credentialFields, "credential-state") ?? "Not reported";
  const verification = findField(detail.credentialFields, "verification-state") ?? "Not reported";
  const latency = findField(detail.overviewFields, "last-response") ?? "Not reported";
  const workflows = findField(detail.overviewFields, "workflows") ?? "Workflow impact not declared";
  return [
    { id: "health", label: "Connection Health", value: health, detail: detail.description, tone: detail.statusTone },
    { id: "credentials", label: "Credential State", value: credential, detail: `${verifiedCount}/${rows.length} providers are verified or credential-free.`, tone: credentialToneFromLabel(credential) },
    { id: "verification", label: "Verification State", value: verification, detail: "Verification is separate from configured state.", tone: verificationToneFromLabel(verification) },
    { id: "latency", label: "Latency / Last Response", value: latency, detail: "Last successful heartbeat or reported latency.", tone: "default" },
    { id: "workflows", label: "Affected Workflows", value: workflows, detail: "Workflow impact before routing changes.", tone: "default" },
    { id: "action", label: "Recommended Action", value: detail.actionText, detail: detail.gateImpactText, tone: detail.statusTone }
  ];
}

function buildProviderOverviewFields(
  record: DataOperationsProviderCenterRecord,
  row: DataOperationsProviderRow
): DataOperationsDetailField[] {
  const connection = record.connection;
  const workspaceRouting = record.workspaceProvider?.routingSummary;
  return [
    { id: "health", label: "Current health", value: row.status },
    { id: "capability", label: "Capability", value: row.capability },
    {
      id: "connection-state",
      label: "Connection state",
      value: connection ? row.status : workspaceRouting ? (workspaceRouting.healthStatus ?? "Workspace summary") : "Routing evidence only"
    },
    { id: "credential-state", label: "Credential state", value: row.credentialText },
    { id: "verification-state", label: "Verification", value: row.verificationText },
    { id: "last-verified", label: "Last verified", value: formatProviderUtcMinute(connection?.lastVerifiedAt) },
    { id: "last-response", label: "Last successful call", value: row.lastSuccessfulText },
    { id: "last-failure", label: "Last failure", value: formatProviderUtcMinute(connection?.lastFailureAt) },
    { id: "last-error", label: "Last error", value: connection?.lastError ?? "None reported" },
    {
      id: "fallback",
      label: "Fallback",
      value: connection?.fallbackActive
        ? "Fallback active"
        : workspaceRouting && workspaceRouting.fallbackRouteCount > 0
          ? `${workspaceRouting.fallbackRouteCount} fallback route${workspaceRouting.fallbackRouteCount === 1 ? "" : "s"}`
          : providerRoutingFallbackLabel(record.bindings)
    },
    { id: "workflows", label: "Affected workflows", value: row.affectedWorkflowsText },
    { id: "trust-score", label: "Trust score", value: row.trustScoreText }
  ];
}

function buildProviderCredentialFieldsForDetail(
  record: DataOperationsProviderCenterRecord,
  row: DataOperationsProviderRow
): DataOperationsDetailField[] {
  const connection = record.connection;
  return [
    { id: "credential-source", label: "Credential source", value: connection ? providerCredentialSourceLabel(connection.credentialSource) : "Routing API only" },
    { id: "credential-state", label: "Credential state", value: row.credentialText },
    { id: "verification-state", label: "Verification state", value: row.verificationText },
    { id: "masked-key", label: "Masked key preview", value: connection?.maskedKeyPreview ?? "Hidden after save" },
    { id: "environment", label: "Environment", value: connection?.environment ? connection.environment.toUpperCase() : providerRoutingEnvironmentLabel(record.routingConnection) },
    { id: "external-account", label: "External account ID", value: connection?.externalAccountId ?? record.routingConnection?.externalAccountId ?? "Not linked" },
    { id: "safe-action", label: "Safe action", value: connection ? "Verify credentials through the shared provider route." : "Open Settings to configure credentials first." }
  ];
}

function buildProviderDiagnostics(record: DataOperationsProviderCenterRecord): DataOperationsProviderDiagnosticRow[] {
  if (record.workspaceProvider?.diagnostics?.length) {
    return record.workspaceProvider.diagnostics;
  }

  const connection = record.connection;
  const hasCredentialRecord = Boolean(connection);
  const credentialPass = connection?.credentialState === "Verified" || connection?.credentialState === "NotRequired" || connection?.credentialState === "Configured";
  const verificationPass = connection?.verificationState === "Verified" || connection?.verificationState === "NotRequired";
  const endpointPass = record.trustSnapshot?.isHealthy ?? connection?.health === "Healthy";
  return [
    {
      id: "credential-presence",
      label: "Credential presence",
      status: !hasCredentialRecord ? "pending" : credentialPass ? "pass" : "fail",
      statusLabel: !hasCredentialRecord ? "Not loaded" : credentialPass ? "Pass" : "Fail",
      detail: !hasCredentialRecord ? "No credential connection row is loaded for this provider." : providerCredentialLabel(connection!.credentialState)
    },
    {
      id: "credential-verification",
      label: "Credential verification",
      status: !hasCredentialRecord ? "pending" : verificationPass ? "pass" : "warning",
      statusLabel: !hasCredentialRecord ? "Not loaded" : verificationPass ? "Pass" : "Review",
      detail: !hasCredentialRecord ? "Verification requires a configured provider connection." : providerVerificationLabel(connection!.verificationState)
    },
    {
      id: "endpoint-reachable",
      label: "Endpoint reachable",
      status: endpointPass ? "pass" : "pending",
      statusLabel: endpointPass ? "Pass" : "Placeholder",
      detail: record.trustSnapshot ? `${Math.round(record.trustSnapshot.score)}% trust snapshot` : "Provider-specific endpoint probes are not part of this MVP."
    },
    {
      id: "quote-test",
      label: "Sample quote test",
      status: "pending",
      statusLabel: "Placeholder",
      detail: "Quote diagnostics will use provider-specific checks in a follow-up phase."
    },
    {
      id: "backfill-test",
      label: "Sample backfill test",
      status: "pending",
      statusLabel: "Placeholder",
      detail: "Backfill diagnostics will use provider-specific checks in a follow-up phase."
    },
    {
      id: "reference-test",
      label: "Reference lookup test",
      status: "pending",
      statusLabel: "Placeholder",
      detail: "Reference-data diagnostics will use provider-specific checks in a follow-up phase."
    }
  ];
}

function buildProviderDiagnosticsEmptyState(record: DataOperationsProviderCenterRecord): DataOperationsEmptyState | null {
  if (record.workspaceProvider?.diagnostics?.length) {
    return null;
  }

  if (record.connection || record.trustSnapshot) {
    return null;
  }

  return {
    title: "Diagnostics not loaded yet",
    description: "Load credential or routing evidence before Meridian can run provider-specific verification, quote probes, or backfill checks."
  };
}

function buildProviderVerifyActionState(
  busy: boolean,
  providerId: string | null,
  result: ProviderCredentialVerificationResult | null,
  error: ApiErrorDisplay | null
): DataOperationsProviderVerifyActionState {
  if (busy) {
    return {
      label: "Verifying...",
      ariaLabel: providerId ? `Provider verification in progress for ${providerId}` : "Provider verification in progress",
      busy: true,
      disabled: true,
      disabledReason: "Provider verification is already running.",
      statusLabel: "Verification running.",
      statusTone: "default",
      details: []
    };
  }

  if (error) {
    return {
      label: "Run Verification",
      ariaLabel: "Run provider credential verification",
      busy: false,
      disabled: false,
      disabledReason: null,
      statusLabel: error.summary,
      statusTone: "danger",
      details: error.details
    };
  }

  if (result) {
    return {
      label: "Run Verification",
      ariaLabel: `Run provider credential verification for ${result.providerId}`,
      busy: false,
      disabled: false,
      disabledReason: null,
      statusLabel: result.success ? "Verification passed." : "Verification needs review.",
      statusTone: result.success ? "success" : "warning",
      details: [
        result.lastError,
        result.externalAccountId ? `External account: ${result.externalAccountId}` : null,
        ...result.warnings
      ].filter((value): value is string => Boolean(value))
    };
  }

  return {
    label: "Run Verification",
    ariaLabel: "Run provider credential verification",
    busy: false,
    disabled: false,
    disabledReason: null,
    statusLabel: "Credential verification can be run from this tab.",
    statusTone: "default",
    details: []
  };
}

function providerCredentialLabel(value: ProviderConnectionRow["credentialState"]): string {
  switch (value) {
    case "NotRequired":
      return "Not required";
    default:
      return splitProviderSetupPascalCase(value);
  }
}

function providerVerificationLabel(value: ProviderConnectionRow["verificationState"]): string {
  switch (value) {
    case "NotRequired":
      return "Not required";
    case "NotVerified":
      return "Not verified";
    default:
      return splitProviderSetupPascalCase(value);
  }
}

function providerCredentialSourceLabel(value: ProviderConnectionRow["credentialSource"]): string {
  switch (value) {
    case "LocalEncryptedStore":
      return "Encrypted local store";
    case "ExternalVaultReference":
      return "External vault reference";
    case "NotRequired":
      return "Not required";
    default:
      return splitProviderSetupPascalCase(value);
  }
}

function providerRoutingVerificationLabel(
  connection: ProviderRoutingConnection | null,
  trust: ProviderRoutingTrustSnapshot | null
): string {
  if (trust?.isCertificationFresh) {
    return "Certified";
  }

  if (connection?.productionReady) {
    return "Production ready";
  }

  return connection ? "Certification pending" : "Not reported";
}

function providerRoutingRecommendedAction(
  connection: ProviderRoutingConnection,
  trust: ProviderRoutingTrustSnapshot | null,
  bindings: ProviderRoutingBinding[],
  enabled: boolean
): string {
  if (!enabled) {
    return "Enable the routing connection before selecting it for provider workflows.";
  }

  if (bindings.length === 0) {
    return "Add a provider-routing binding before selecting this connection.";
  }

  if (!connection.productionReady) {
    return "Run provider certification before production routing.";
  }

  if (trust && !trust.isHealthy) {
    return "Inspect provider health before routing new workflow traffic.";
  }

  return "Provider routing is ready for supported capabilities.";
}

function providerGateImpactText(
  connection: ProviderRoutingConnection | null,
  trust: ProviderRoutingTrustSnapshot | null
): string {
  if (!connection) {
    return "No routing gate loaded";
  }

  if (!connection.enabled) {
    return "Disabled for routing";
  }

  if (!connection.productionReady) {
    return "Certification needed";
  }

  if (trust && !trust.isHealthy) {
    return "Health gate needs review";
  }

  return "No gate impact reported";
}

function providerRoutingFallbackLabel(bindings: ProviderRoutingBinding[]): string {
  const count = bindings.reduce((total, binding) => total + binding.failoverConnectionIds.length, 0);
  return count > 0 ? `${count} fallback route${count === 1 ? "" : "s"}` : "No fallback active";
}

function providerRoutingEnvironmentLabel(connection: ProviderRoutingConnection | null): string {
  if (!connection) {
    return "Not set";
  }

  if (connection.connectionMode.toLowerCase().includes("paper")) {
    return "PAPER";
  }

  if (connection.connectionMode.toLowerCase().includes("live")) {
    return "LIVE";
  }

  return connection.connectionMode;
}

function formatProviderRoutingCapability(value: string): string {
  switch (value) {
    case "RealtimeMarketData":
      return "Live quotes";
    case "HistoricalBars":
      return "Historical backfill";
    case "ReferenceData":
      return "Reference data";
    case "BrokerageOrders":
      return "Brokerage/order routing";
    case "PortfolioSync":
      return "Portfolio sync";
    case "ReportingExport":
      return "Reporting exports";
    default:
      return splitProviderSetupPascalCase(value);
  }
}

function formatLastGoodProviderResponse(connection: ProviderConnectionRow | null): string {
  return formatProviderUtcMinute(connection?.lastSuccessfulAt ?? connection?.lastVerifiedAt);
}

function formatProviderUtcMinute(value: string | null | undefined): string {
  if (!value) {
    return "Never";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return new Intl.DateTimeFormat("en", {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    timeZone: "UTC",
    timeZoneName: "short"
  }).format(date);
}

function normalizeProviderToken(value: string | null | undefined): string {
  return (value ?? "").trim().toLowerCase().replace(/[^a-z0-9]/g, "");
}

function uniqueStrings(values: string[]): string[] {
  return values.filter((value, index) => values.indexOf(value) === index);
}

function findField(fields: DataOperationsDetailField[], id: string): string | null {
  return fields.find((field) => field.id === id)?.value ?? null;
}

function credentialToneFromLabel(value: string): DataOperationsProviderSummaryCardState["tone"] {
  return value === "Verified" || value === "Not required"
    ? "success"
    : value === "Missing" || value === "Partial" || value === "Invalid"
      ? "danger"
      : "warning";
}

function verificationToneFromLabel(value: string): DataOperationsProviderSummaryCardState["tone"] {
  return value === "Verified" || value === "Not required"
    ? "success"
    : value === "Failed"
      ? "danger"
      : "warning";
}

export function buildBackfillSection(
  backfills: DataOperationsBackfillRecord[],
  selectedBackfillId: string | null,
  workstream: "overview" | "backfills" = "overview"
): DataOperationsBackfillSectionState {
  return {
    rows: backfills.map((backfill) => {
      const detailText = `${backfill.scope}. ${backfill.status}; ${backfill.progress}; updated ${backfill.updatedAt}.`;
      const selected = selectedBackfillId === backfill.jobId;
      const rowId = buildBackfillRowId(backfill.jobId);

      return {
        jobId: backfill.jobId,
        rowId,
        detailPanelId: DATA_BACKFILL_DETAIL_PANEL_ID,
        scope: backfill.scope,
        provider: backfill.provider,
        status: backfill.status,
        rowClassName: backfillRowClassName(backfill.status),
        progress: backfill.progress,
        updatedAt: backfill.updatedAt,
        selected,
        expanded: selected,
        detailText,
        ariaLabel: `${selected ? "Selected" : "Inspect"} backfill ${backfill.jobId}: ${detailText}`,
        selectAriaLabel: `Inspect backfill ${backfill.jobId}`,
        detailDescription: selected
          ? `Selected backfill ${backfill.jobId}; the backfill detail panel is expanded for this row.`
          : `Inspect backfill ${backfill.jobId}; activation updates the shared backfill detail panel.`
      };
    }),
    hasRows: backfills.length > 0,
    tableLabel: "Backfill queue",
    description: "Queued and recently completed historical repair jobs",
    emptyState: {
      title: "No backfills queued",
      description: workstream === "backfills"
        ? "Use Trigger backfill to preview a historical repair; queued and review-required jobs will appear here."
        : "Historical repair jobs will appear here after a previewed backfill is submitted."
    }
  };
}

export function buildSelectedBackfillDetail(
  backfills: DataOperationsBackfillRecord[],
  selectedBackfillId: string | null
): DataOperationsBackfillDetailState | null {
  const selected = resolveSelectedBackfill(backfills, selectedBackfillId);

  if (!selected) {
    return null;
  }

  const description = buildBackfillNarrative(selected);

  return {
    id: DATA_BACKFILL_DETAIL_PANEL_ID,
    title: selected.scope,
    description,
    ariaLabel: `Backfill detail for ${selected.jobId}: ${selected.scope}. ${description}`,
    statusLabel: selected.status,
    statusVariant: selected.status === "Review" ? "warning" : selected.status === "Running" ? "default" : "outline",
    rows: [
      { id: "provider", label: "Provider", value: selected.provider },
      { id: "status", label: "Status", value: selected.status },
      { id: "progress", label: "Progress", value: selected.progress },
      { id: "updated", label: "Updated", value: selected.updatedAt }
    ]
  };
}

export function buildExportSection(
  exports: DataOperationsExportRecord[],
  selectedExportId: string | null = null
): DataOperationsExportSectionState {
  const selectedExport = resolveSelectedExport(exports, selectedExportId);
  const selectedRowId = selectedExport ? buildExportRowId(selectedExport.exportId) : null;

  return {
    rows: exports.map((item) => {
      const statusVariant = exportStatusVariant(item.status);
      const actionText = exportActionText(item.status);
      const summaryText = `${item.target} · ${item.rows} · ${item.updatedAt}`;
      const rowId = buildExportRowId(item.exportId);
      const selected = rowId === selectedRowId;
      const detailFields = [
        { id: "export-id", label: "Export ID", value: item.exportId },
        { id: "target", label: "Target", value: item.target },
        { id: "rows", label: "Rows", value: item.rows },
        { id: "updated", label: "Updated", value: item.updatedAt }
      ];

      return {
        exportId: item.exportId,
        rowId,
        detailPanelId: DATA_EXPORT_DETAIL_PANEL_ID,
        profile: item.profile,
        target: item.target,
        status: item.status,
        statusLabel: item.status,
        statusVariant,
        statusTone: statusVariant,
        rowClassName: exportRowClassName(item.status),
        rows: item.rows,
        updatedAt: item.updatedAt,
        summaryText,
        detailFields,
        actionText,
        selected,
        expanded: selected,
        ariaLabel: [
          `${selected ? "Selected" : "Inspect"} export ${item.exportId}: ${item.profile} ${item.status}`,
          `Target ${item.target}`,
          `Rows ${item.rows}`,
          `Updated ${item.updatedAt}`,
          `Next action ${actionText}`
        ].join(". "),
        selectAriaLabel: `Inspect export ${item.exportId}`,
        detailDescription: selected
          ? "This export detail panel is expanded."
          : "Select this export row to update the export detail panel."
      };
    }),
    hasRows: exports.length > 0,
    emptyState: {
      title: "No exports available",
      description: "Generated packages and reporting outputs will appear here with target, row count, and readiness status."
    },
    tableLabel: "Recent exports",
    description: "Latest package and reporting outputs tied to data operations evidence",
    selectedRowId,
    selectedDetail: buildSelectedExportDetail(exports, selectedExport?.exportId ?? null),
    detailEmptyState: exports.length === 0
      ? {
          title: "No export selected",
          description: "Generated packages and governed export evidence will appear here after a report or package job runs."
        }
      : null
  };
}

export function buildSelectedExportDetail(
  exports: DataOperationsExportRecord[],
  selectedExportId: string | null
): DataOperationsExportDetailState | null {
  const selected = resolveSelectedExport(exports, selectedExportId);

  if (!selected) {
    return null;
  }

  const actionText = exportActionText(selected.status);
  const description = `${selected.profile} export targets ${selected.target} with ${selected.rows} rows. ${actionText}`;

  return {
    id: DATA_EXPORT_DETAIL_PANEL_ID,
    title: selected.profile,
    subtitle: `${selected.exportId} · ${selected.target}`,
    description,
    ariaLabel: `Export detail for ${selected.exportId}: ${selected.profile} ${selected.status}. ${actionText}`,
    statusLabel: selected.status,
    statusVariant: exportStatusVariant(selected.status),
    fields: [
      { id: "export-id", label: "Export ID", value: selected.exportId },
      { id: "target", label: "Target", value: selected.target },
      { id: "rows", label: "Rows", value: selected.rows },
      { id: "updated", label: "Updated", value: selected.updatedAt }
    ],
    actionText
  };
}

function exportStatusVariant(status: DataOperationsExportRecord["status"]): DataOperationsExportRow["statusVariant"] {
  if (status === "Ready") {
    return "success";
  }

  if (status === "Running") {
    return "paper";
  }

  return "warning";
}

function providerRowClassName(statusTone: DataOperationsProviderRow["statusTone"]): DataOperationsStatusRowClassName {
  if (statusTone === "success") {
    return "bg-success/5";
  }

  if (statusTone === "danger") {
    return "bg-danger/5";
  }

  return "bg-warning/5";
}

function backfillRowClassName(status: DataOperationsBackfillRecord["status"]): DataOperationsStatusRowClassName {
  if (status === "Running") {
    return "bg-paper/5";
  }

  if (status === "Review") {
    return "bg-warning/5";
  }

  return "bg-background/50";
}

function exportRowClassName(status: DataOperationsExportRecord["status"]): DataOperationsStatusRowClassName {
  if (status === "Ready") {
    return "bg-success/5";
  }

  if (status === "Running") {
    return "bg-paper/5";
  }

  return "bg-warning/5";
}

function exportActionText(status: DataOperationsExportRecord["status"]): string {
  if (status === "Ready") {
    return "Attach export to the report pack or hand off the package.";
  }

  if (status === "Running") {
    return "Wait for the package writer to finish before handoff.";
  }

  return "Review export profile and target before report-pack use.";
}

export function resolveSelectedProvider(
  providers: DataOperationsProviderRecord[],
  selectedProviderId: string | null
): DataOperationsProviderRecord | null {
  return providers.find((provider) => (
    provider.provider === selectedProviderId || buildProviderRowId(provider.provider) === selectedProviderId
  )) ?? providers[0] ?? null;
}

export function resolveSelectedBackfill(
  backfills: DataOperationsBackfillRecord[],
  selectedBackfillId: string | null
): DataOperationsBackfillRecord | null {
  return backfills.find((job) => job.jobId === selectedBackfillId) ?? backfills[0] ?? null;
}

export function resolveSelectedExport(
  exports: DataOperationsExportRecord[],
  selectedExportId: string | null
): DataOperationsExportRecord | null {
  return exports.find((item) => (
    item.exportId === selectedExportId || buildExportRowId(item.exportId) === selectedExportId
  )) ?? exports[0] ?? null;
}

export function buildBackfillTriggerState({
  form,
  busy,
  phase,
  error,
  preview,
  result,
  configuredProviders = []
}: {
  form: BackfillFormState;
  busy: boolean;
  phase: BackfillPhase;
  error: ApiErrorDisplay | null;
  preview: BackfillTriggerResult | null;
  result: BackfillTriggerResult | null;
  configuredProviders?: DataOperationsProviderRecord[];
}): BackfillTriggerState {
  const validationError = validateBackfillForm(form, configuredProviders);
  const feedbackText = error?.summary ?? null;
  const feedbackDetails = error?.details ?? [];
  const feedbackTone = error
    ? feedbackText === validationError
      ? "warning"
      : "danger"
    : null;

  return {
    validationError,
    feedbackText,
    feedbackDetails,
    feedbackTone,
    canPreview: !busy && validationError === null,
    canRun: !busy && preview !== null && validationError === null,
    previewButtonLabel: phase === "previewing" ? "Previewing..." : "Preview",
    runButtonLabel: phase === "running" ? "Running..." : "Run backfill",
    previewButtonAriaLabel: phase === "previewing"
      ? "Previewing backfill request"
      : validationError
        ? `Preview backfill unavailable: ${validationError}`
        : "Preview backfill request",
    runButtonAriaLabel: phase === "running"
      ? "Running backfill request"
      : preview === null
        ? "Run backfill unavailable until preview completes"
        : validationError
          ? `Run backfill unavailable: ${validationError}`
          : "Run previewed backfill request",
    symbolsHelpText: "Separate symbols with spaces or commas. At least one symbol is required.",
    statusAnnouncement: buildBackfillStatusAnnouncement({ phase, error, preview, result }),
    dialogState: buildBackfillDialogState({ form, busy, phase, validationError, preview, error, result, configuredProviders })
  };
}

export function buildBackfillDialogState({
  form,
  busy,
  phase,
  validationError,
  preview,
  error = null,
  result = null,
  configuredProviders = []
}: {
  form: BackfillFormState;
  busy: boolean;
  phase: BackfillPhase;
  validationError: string | null;
  preview: BackfillTriggerResult | null;
  error?: ApiErrorDisplay | null;
  result?: BackfillTriggerResult | null;
  configuredProviders?: DataOperationsProviderRecord[];
}): BackfillDialogState {
  const previewDisabledReason = resolveBackfillPreviewDisabledReason({ busy, phase, validationError });
  const runDisabledReason = resolveBackfillRunDisabledReason({ busy, phase, validationError, preview });
  const providerOptions = buildBackfillProviderOptions(form.provider, configuredProviders);
  const fieldDisabledReason = busy
    ? "Backfill request is running; wait for the current request to finish before editing."
    : providerOptions.length === 0
      ? NO_CONFIGURED_BACKFILL_PROVIDER_MESSAGE
    : null;

  return {
    titleId: "backfill-dialog-title",
    descriptionId: "backfill-dialog-description",
    formLabel: "Backfill request form",
    closeButtonLabel: "Close backfill dialog",
    closeButtonDisabledReason: busy ? "Backfill request is running; wait for the current request to finish before closing." : null,
    summaryItems: buildBackfillDialogSummaryItems(form, configuredProviders),
    providerField: {
      id: "backfill-provider",
      label: "Provider",
      ariaLabel: "Backfill provider",
      placeholder: "Select a provider",
      disabled: busy || providerOptions.length === 0,
      disabledReason: fieldDisabledReason
    },
    providerOptions,
    selectedProviderDetail: buildBackfillProviderDetail(form.provider, configuredProviders),
    symbolsField: {
      id: "backfill-symbols",
      label: "Symbols",
      ariaLabel: "Backfill symbols",
      placeholder: "Type symbols, e.g. AAPL, MSFT, SPY",
      describedBy: "backfill-symbols-help backfill-form-status backfill-form-feedback",
      autoFocus: true,
      disabled: busy,
      disabledReason: fieldDisabledReason
    },
    fromField: {
      id: "backfill-from",
      label: "From",
      ariaLabel: "Backfill start date",
      disabled: busy,
      disabledReason: fieldDisabledReason
    },
    toField: {
      id: "backfill-to",
      label: "To",
      ariaLabel: "Backfill end date",
      disabled: busy,
      disabledReason: fieldDisabledReason
    },
    previewAction: {
      label: phase === "previewing" ? "Previewing..." : "Preview",
      ariaLabel: phase === "previewing"
        ? "Previewing backfill request"
        : validationError
          ? `Preview backfill unavailable: ${validationError}`
          : "Preview backfill request",
      disabled: busy || validationError !== null,
      disabledReason: previewDisabledReason,
      busy: phase === "previewing",
      busyLabel: "Previewing..."
    },
    runAction: {
      label: phase === "running" ? "Running..." : "Run backfill",
      ariaLabel: phase === "running"
        ? "Running backfill request"
        : preview === null
          ? "Run backfill unavailable until preview completes"
          : validationError
            ? `Run backfill unavailable: ${validationError}`
            : "Run previewed backfill request",
      disabled: busy || preview === null || validationError !== null,
      disabledReason: runDisabledReason,
      busy: phase === "running",
      busyLabel: "Running..."
    },
    formStatusLabel: buildBackfillFormStatusLabel({ busy, phase, validationError, preview, error, result }),
    formStatusTone: resolveBackfillFormStatusTone({ phase, validationError, error, preview, result })
  };
}

export function buildBackfillDialogSummaryItems(
  form: BackfillFormState,
  configuredProviders: DataOperationsProviderRecord[] = []
): BackfillDialogSummaryItemState[] {
  const providerOptions = buildBackfillProviderOptions(form.provider, configuredProviders);
  const provider = providerOptions.length > 0
    ? resolveBackfillProviderLabel(form.provider, configuredProviders)
    : "None configured";
  const symbols = parseSymbols(form.symbols);
  const range = formatBackfillRange(form.from.trim() || null, form.to.trim() || null);

  return [
    { id: "provider", label: "Provider", value: provider, tone: providerOptions.length > 0 ? "default" : "warning" },
    {
      id: "symbols",
      label: "Symbols",
      value: symbols.length > 0 ? `${symbols.length} selected` : "None yet",
      tone: symbols.length > 0 ? "default" : "warning"
    },
    { id: "range", label: "Range", value: range, tone: "default" }
  ];
}

export function buildBackfillProviderOptions(
  selectedProvider: string,
  configuredProviders: DataOperationsProviderRecord[] = []
): BackfillProviderOptionState[] {
  const options = new Map<string, BackfillProviderOptionState>();

  for (const provider of configuredProviders) {
    const value = normalizeBackfillProviderValue(provider.provider);
    if (!value || options.has(value)) {
      continue;
    }

    const knownProvider = BACKFILL_PROVIDER_OPTIONS.find((option) => option.value === value);
    const label = provider.provider.trim() || knownProvider?.label || value;
    const capability = provider.capability.trim() || "historical backfill";
    options.set(value, {
      value,
      label,
      description: `${label} is configured for ${capability}; current status is ${provider.status.toLowerCase()}.`,
      badge: "Configured"
    });
  }

  const selected = normalizeBackfillProviderValue(selectedProvider);
  const result = Array.from(options.values());
  if (!selected || options.has(selected)) {
    return result;
  }

  return result;
}

export function buildBackfillProviderDetail(
  provider: string,
  configuredProviders: DataOperationsProviderRecord[] = []
): string {
  const options = buildBackfillProviderOptions(provider, configuredProviders);
  if (options.length === 0) {
    return NO_CONFIGURED_BACKFILL_PROVIDER_MESSAGE;
  }

  const selected = normalizeBackfillProviderValue(provider);
  const option = options.find((item) => item.value === selected) ?? options[0];
  return option.description;
}

function resolveBackfillProviderLabel(
  provider: string,
  configuredProviders: DataOperationsProviderRecord[] = []
): string {
  const trimmed = provider.trim();
  const options = buildBackfillProviderOptions(provider, configuredProviders);
  const option = options.find((item) => item.value === normalizeBackfillProviderValue(trimmed));
  return option?.label ?? (trimmed.length > 0 ? trimmed : "Default provider");
}

function normalizeBackfillProviderValue(provider: string): string {
  const normalized = provider.trim().toLowerCase();
  return BACKFILL_PROVIDER_ALIASES[normalized] ?? normalized;
}

export function resolveBackfillPreviewDisabledReason({
  busy,
  phase,
  validationError
}: {
  busy: boolean;
  phase: BackfillPhase;
  validationError: string | null;
}): string | null {
  if (phase === "previewing") {
    return "Preview is already running.";
  }

  if (busy) {
    return "Wait for the current backfill request to finish.";
  }

  return validationError;
}

export function resolveBackfillRunDisabledReason({
  busy,
  phase,
  validationError,
  preview
}: {
  busy: boolean;
  phase: BackfillPhase;
  validationError: string | null;
  preview: BackfillTriggerResult | null;
}): string | null {
  if (phase === "running") {
    return "Backfill is already running.";
  }

  if (busy) {
    return "Wait for the current backfill request to finish.";
  }

  if (validationError) {
    return validationError;
  }

  if (!preview) {
    return "Preview the request before running the backfill.";
  }

  return null;
}

export function buildBackfillFormStatusLabel({
  busy,
  phase,
  validationError,
  preview,
  error,
  result
}: {
  busy: boolean;
  phase: BackfillPhase;
  validationError: string | null;
  preview: BackfillTriggerResult | null;
  error: ApiErrorDisplay | null;
  result: BackfillTriggerResult | null;
}): string {
  if (phase === "previewing") {
    return "Previewing the backfill request.";
  }

  if (phase === "running") {
    return "Running the previewed backfill request.";
  }

  if (busy) {
    return "Backfill request is busy.";
  }

  if (error) {
    return error.summary;
  }

  if (result?.success) {
    return "Backfill completed successfully.";
  }

  if (result && !result.success) {
    return "Backfill completed with an error.";
  }

  if (preview) {
    return "Preview is ready. Review the summary before running.";
  }

  if (validationError) {
    return validationError;
  }

  return "Backfill request is ready to preview.";
}

function resolveBackfillFormStatusTone({
  phase,
  validationError,
  error,
  preview,
  result
}: {
  phase: BackfillPhase;
  validationError: string | null;
  error: ApiErrorDisplay | null;
  preview: BackfillTriggerResult | null;
  result: BackfillTriggerResult | null;
}): BackfillDialogState["formStatusTone"] {
  if (error || (result && !result.success)) {
    return "danger";
  }

  if (result?.success) {
    return "success";
  }

  if (phase === "previewing" || phase === "running" || preview || validationError) {
    return "warning";
  }

  return "default";
}

export function buildBackfillResultCardState(
  result: BackfillTriggerResult,
  kind: "preview" | "result"
): BackfillResultCardState {
  const providerText = formatBackfillValue(result.provider, "Provider not reported");
  const symbolsText = result.symbols.length > 0 ? result.symbols.join(", ") : "No symbols reported";
  const barsText = result.barsWritten.toLocaleString();
  const rangeText = formatBackfillRange(result.from, result.to);
  const timingText = formatBackfillTiming(result.startedUtc, result.completedUtc);
  const tone = resolveBackfillResultTone(result, kind);
  const statusLabel = resolveBackfillResultStatusLabel(result, kind);
  const title = kind === "preview"
    ? `Preview ready — ${providerText}`
    : result.success
      ? `Backfill complete — ${providerText}`
      : `Backfill failed — ${providerText}`;
  const rows = [
    { id: "provider", label: "Provider", value: providerText },
    { id: "symbols", label: "Symbols", value: symbolsText },
    { id: "range", label: "Range", value: rangeText },
    { id: "bars", label: "Bars", value: barsText },
    { id: "timing", label: "Timing", value: timingText }
  ];

  return {
    title,
    statusLabel,
    tone,
    rows,
    errorText: result.error,
    ariaLabel: [
      title,
      `Status ${statusLabel}`,
      `Symbols ${symbolsText}`,
      `Bars ${barsText}`,
      `Range ${rangeText}`,
      `Timing ${timingText}`,
      result.error ? `Error ${result.error}` : null
    ].filter(Boolean).join(". ")
  };
}

export function buildBackfillRequest(form: BackfillFormState): BackfillTriggerRequest {
  return {
    provider: form.provider.trim() || null,
    symbols: parseSymbols(form.symbols),
    from: form.from.trim() || null,
    to: form.to.trim() || null
  };
}

export function validateBackfillForm(
  form: BackfillFormState,
  configuredProviders: DataOperationsProviderRecord[] = []
): string | null {
  const providerOptions = buildBackfillProviderOptions(form.provider, configuredProviders);
  if (providerOptions.length === 0) {
    return NO_CONFIGURED_BACKFILL_PROVIDER_MESSAGE;
  }

  if (!providerOptions.some((provider) => provider.value === normalizeBackfillProviderValue(form.provider))) {
    return SELECT_CONFIGURED_BACKFILL_PROVIDER_MESSAGE;
  }

  if (parseSymbols(form.symbols).length === 0) {
    return "Enter at least one symbol before previewing a backfill.";
  }

  if (form.from.trim() && !isValidDateInput(form.from)) {
    return "Use YYYY-MM-DD for the From date.";
  }

  if (form.to.trim() && !isValidDateInput(form.to)) {
    return "Use YYYY-MM-DD for the To date.";
  }

  const fromTime = form.from.trim() ? parseStrictDateInput(form.from) : null;
  const toTime = form.to.trim() ? parseStrictDateInput(form.to) : null;
  if (fromTime !== null && toTime !== null && fromTime > toTime) {
    return "From date must be before or equal to To date.";
  }

  return null;
}

export function buildBackfillNarrative(backfill: DataOperationsBackfillRecord): string {
  if (backfill.status === "Running") {
    return `Replay is currently advancing for ${backfill.scope}; monitor provider pressure before adding more symbols.`;
  }

  if (backfill.status === "Review") {
    return `${backfill.scope} is waiting on operator review before it can be treated as complete.`;
  }

  return `${backfill.scope} is queued behind active data operations work.`;
}

function parseSymbols(value: string): string[] {
  return value
    .split(/[\s,]+/)
    .map((symbol) => symbol.trim().toUpperCase())
    .filter(Boolean);
}

function isValidDateInput(value: string): boolean {
  return parseStrictDateInput(value) !== null;
}

function parseStrictDateInput(value: string): number | null {
  const trimmed = value.trim();
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(trimmed);
  if (!match) {
    return null;
  }

  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const date = new Date(Date.UTC(year, month - 1, day));

  if (
    date.getUTCFullYear() !== year ||
    date.getUTCMonth() !== month - 1 ||
    date.getUTCDate() !== day
  ) {
    return null;
  }

  return date.getTime();
}

function formatProviderValue(value: string | null | undefined, fallback: string): string {
  const trimmed = value?.trim();
  return trimmed ? trimmed : fallback;
}

function formatBackfillValue(value: string | null | undefined, fallback: string): string {
  const trimmed = value?.trim();
  return trimmed ? trimmed : fallback;
}

function formatBackfillRange(from: string | null, to: string | null): string {
  const fromText = formatBackfillValue(from, "");
  const toText = formatBackfillValue(to, "");

  if (fromText && toText) {
    return `${fromText} to ${toText}`;
  }

  if (fromText) {
    return `From ${fromText}`;
  }

  if (toText) {
    return `Through ${toText}`;
  }

  return "Full available history";
}

function formatBackfillTiming(startedUtc: string, completedUtc: string): string {
  const started = new Date(startedUtc);
  const completed = new Date(completedUtc);

  if (Number.isNaN(started.getTime()) || Number.isNaN(completed.getTime())) {
    return "Timing unavailable";
  }

  const elapsedSeconds = Math.max(0, Math.round((completed.getTime() - started.getTime()) / 1000));
  return `${formatUtcMinute(started)} · ${elapsedSeconds}s elapsed`;
}

function formatUtcMinute(date: Date): string {
  const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
  const month = months[date.getUTCMonth()];
  const day = String(date.getUTCDate()).padStart(2, "0");
  const year = date.getUTCFullYear();
  const hour = String(date.getUTCHours()).padStart(2, "0");
  const minute = String(date.getUTCMinutes()).padStart(2, "0");

  return `${month} ${day}, ${year} ${hour}:${minute} UTC`;
}

function buildBackfillRowId(jobId: string): string {
  return `backfill-row-${toDomId(jobId)}`;
}

function buildExportRowId(exportId: string): string {
  return `export-row-${toDomId(exportId)}`;
}

function buildProviderRowId(provider: string): string {
  return `provider-row-${toDomId(provider)}`;
}

function resolveProviderStatusTone(
  status: DataOperationsProviderStatus
): DataOperationsProviderRow["statusTone"] {
  if (status === "Healthy") {
    return "success";
  }

  if (status === "Degraded" || status === "Blocked") {
    return "danger";
  }

  return "warning";
}

function toDomId(value: string): string {
  const normalized = value.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
  return normalized || "item";
}

function resolveBackfillResultTone(
  result: BackfillTriggerResult,
  kind: "preview" | "result"
): BackfillResultCardTone {
  if (!result.success || result.error) {
    return "danger";
  }

  return kind === "preview" ? "warning" : "success";
}

function resolveBackfillResultStatusLabel(result: BackfillTriggerResult, kind: "preview" | "result"): string {
  if (!result.success || result.error) {
    return "Failed";
  }

  return kind === "preview" ? "Preview only" : "Written";
}

function buildBackfillStatusAnnouncement({
  phase,
  error,
  preview,
  result
}: {
  phase: BackfillPhase;
  error: ApiErrorDisplay | null;
  preview: BackfillTriggerResult | null;
  result: BackfillTriggerResult | null;
}): string {
  if (phase === "previewing") {
    return "Previewing backfill request.";
  }

  if (phase === "running") {
    return "Running backfill request.";
  }

  if (error) {
    return `Backfill request failed: ${error.summary}`;
  }

  if (result) {
    return `Backfill complete for ${result.symbols.join(", ")}.`;
  }

  if (preview) {
    return `Backfill preview ready for ${preview.symbols.join(", ")}.`;
  }

  return "";
}

// --- Provider setup helpers ---

export function validateProviderSetupForm(form: ProviderSetupFormState): string | null {
  if (!form.displayName.trim()) {
    return "Enter a display name for the provider.";
  }

  const meta = PROVIDER_KIND_CATALOG.find((p) => p.kind === form.kind);

  if (meta?.needsApiKey && !form.apiKey.trim()) {
    return `An API key is required for ${meta.label}.`;
  }

  if (meta?.needsApiSecret && !form.apiSecret.trim()) {
    return `An API secret is required for ${meta.label}.`;
  }

  if (meta?.needsEndpoint && !form.endpoint.trim()) {
    return `An endpoint URL is required for ${meta.label ?? "this provider"}.`;
  }

  if (meta?.needsEndpoint && !isValidEndpointUrl(form.endpoint)) {
    return `Enter a valid http or https endpoint URL for ${meta.label ?? "this provider"}.`;
  }

  if (form.environment === "live" && !form.liveAcknowledged) {
    return "Acknowledge live provider access before configuring a live provider.";
  }

  if (form.capabilities.length === 0) {
    return "Select at least one capability for this provider.";
  }

  return null;
}

export function clearProviderSetupCredentials(form: ProviderSetupFormState): ProviderSetupFormState {
  const next: ProviderSetupFormState = {
    ...form,
    apiKey: "",
    apiSecret: ""
  };
  if (form.environment === "live" || form.liveAcknowledged !== undefined) {
    next.liveAcknowledged = form.environment === "live" ? false : Boolean(form.liveAcknowledged);
  }
  return next;
}

export function buildProviderSetupDialogState(
  phase: ProviderSetupPhase,
  form: ProviderSetupFormState,
  result: ProviderSetupResult | null = null
): ProviderSetupDialogState {
  const submitting = phase === "submitting";
  const validationError = phase === "submitting" ? null : validateProviderSetupForm(form);
  const providerMeta = resolveProviderKindMeta(form.kind);
  const fieldDisabledReason = submitting ? "Provider setup is in progress; wait before editing." : null;
  const closeDisabledReason = submitting ? "Provider setup is in progress; wait before closing." : null;

  return {
    titleId: "provider-setup-dialog-title",
    descriptionId: "provider-setup-dialog-description",
    formLabel: "Provider setup form",
    providerKindField: {
      id: "provider-setup-kind",
      label: "Provider type",
      ariaLabel: "Select provider type",
      description: providerMeta?.description ?? "Custom provider type selected.",
      options: PROVIDER_KIND_CATALOG.map((provider) => ({
        value: provider.kind,
        label: provider.label
      })),
      disabled: submitting,
      disabledReason: fieldDisabledReason
    },
    selectedProviderSummary: buildProviderSetupSummary(form, providerMeta),
    displayNameField: {
      id: "provider-setup-name",
      label: "Display name",
      ariaLabel: "Provider display name",
      field: "displayName",
      value: form.displayName,
      disabled: submitting,
      disabledReason: fieldDisabledReason
    },
    environmentField: {
      id: "provider-setup-environment",
      label: "Environment",
      ariaLabel: "Provider environment",
      description: "Keep paper or sandbox selected until the provider is verified and safe to route.",
      value: form.environment ?? "paper",
      options: [
        { value: "paper", label: "Paper" },
        { value: "live", label: "Live" },
        { value: "sandbox", label: "Sandbox" },
        { value: "custom", label: "Custom" }
      ],
      disabled: submitting,
      disabledReason: fieldDisabledReason
    },
    liveAcknowledgement: {
      id: "provider-setup-live-acknowledgement",
      label: "I understand this provider may access live brokerage or real-money account data.",
      detail: "Live provider setup stays gated until this acknowledgement is checked.",
      visible: form.environment === "live",
      checked: Boolean(form.liveAcknowledged),
      disabled: submitting,
      disabledReason: fieldDisabledReason,
      ariaLabel: "Acknowledge live provider access before setup"
    },
    credentialFields: buildProviderCredentialFields(form, providerMeta, fieldDisabledReason),
    capabilityOptions: ALL_CAPABILITIES.map((capability) => ({
      ...capability,
      selected: form.capabilities.includes(capability.id),
      disabled: submitting,
      disabledReason: fieldDisabledReason
    })),
    closeButtonLabel: "Close provider setup",
    closeButtonDisabledReason: closeDisabledReason,
    cancelAction: {
      label: "Cancel",
      ariaLabel: closeDisabledReason
        ? `Cancel provider setup unavailable: ${closeDisabledReason}`
        : "Cancel provider setup",
      disabled: closeDisabledReason !== null,
      disabledReason: closeDisabledReason
    },
    submitAction: {
      label: submitting ? "Configuring..." : phase === "success" ? "Configure another" : "Configure provider",
      ariaLabel: submitting
        ? "Configuring provider"
        : validationError
          ? `Configure provider unavailable: ${validationError}`
          : "Configure and register provider",
      disabled: submitting || (phase !== "success" && validationError !== null),
      disabledReason: submitting ? "Setup is in progress." : validationError,
      busy: submitting,
      busyLabel: "Configuring..."
    },
    statusLabel: buildProviderSetupStatusLabel(phase, validationError),
    successPanel: {
      title: "Next validation",
      ariaLabel: "Provider setup next validation"
    },
    successMetadata: buildProviderSetupSuccessMetadata(result),
    successActions: buildProviderSetupSuccessActions(form)
  };
}

function buildDataOperationsErrorState(error: unknown, fallback?: string): ApiErrorDisplay {
  if (typeof error === "string") {
    const summary = error.trim() || (fallback ?? "Request failed.");
    return { summary, details: [] };
  }

  return describeApiError(error, fallback ?? "Request failed.");
}

function buildProviderSetupStatusLabel(phase: ProviderSetupPhase, validationError: string | null): string {
  if (phase === "submitting") return "Registering provider with Meridian.";
  if (phase === "success") return "Provider configured successfully.";
  if (phase === "error") return "Provider setup encountered an error.";
  if (validationError) return validationError;
  return "Provider setup is ready to submit.";
}

export function buildProviderSetupSuccessMetadata(result: ProviderSetupResult | null): ProviderSetupSuccessMetadataState {
  const rows: DataOperationsDetailField[] = [];
  const bindingIds = normalizeProviderSetupStringArray(result?.bindingIds);
  const warnings = normalizeProviderSetupStringArray(result?.warnings);
  const connectionId = normalizeProviderSetupString(result?.connectionId) ?? normalizeProviderSetupString(result?.providerId);

  if (connectionId) {
    rows.push({ id: "connection-id", label: "Connection", value: connectionId });
  }

  if (bindingIds.length > 0) {
    rows.push({ id: "binding-ids", label: "Bindings", value: bindingIds.join(", ") });
  } else if (result?.success) {
    rows.push({ id: "binding-ids", label: "Bindings", value: "No routing binding returned" });
  }

  const credentialState = normalizeProviderSetupString(result?.credentialState);
  if (credentialState) {
    rows.push({ id: "credential-state", label: "Credential", value: formatProviderSetupCredentialState(credentialState) });
  }

  const credentialSource = normalizeProviderSetupString(result?.credentialSource);
  if (credentialSource) {
    rows.push({ id: "credential-source", label: "Source", value: formatProviderSetupCredentialSource(credentialSource) });
  }

  const environment = normalizeProviderSetupString(result?.environment);
  if (environment) {
    rows.push({ id: "environment", label: "Environment", value: environment.toUpperCase() });
  }

  return {
    rows,
    warnings,
    metadataAriaLabel: "Provider setup routing and credential posture",
    warningsAriaLabel: "Provider setup warnings"
  };
}

export function buildProviderSetupSummary(
  form: ProviderSetupFormState,
  meta: ProviderKindMeta | undefined
): ProviderSetupSummaryState {
  const providerLabel = meta?.label ?? (form.displayName.trim() || "Custom provider");
  const credentialText = meta
    ? [
        meta.needsApiKey ? "API key" : null,
        meta.needsApiSecret ? "secret" : null,
        meta.needsEndpoint ? "endpoint URL" : null
      ].filter(Boolean).join(" + ") || "No credentials required"
    : "Depends on custom endpoint";
  const capabilityText = form.capabilities.length > 0
    ? form.capabilities.map(formatProviderCapabilityLabel).join(", ")
    : "No capabilities selected";

  return {
    providerLabel,
    description: meta?.description ?? "Custom provider type selected.",
    rows: [
      { id: "credentials", label: "Required", value: credentialText },
      { id: "capabilities", label: "Enabled for", value: capabilityText },
      { id: "next-step", label: "After save", value: resolveProviderSetupNextStep(form.capabilities) }
    ],
    noCredentialMessage: meta && !meta.needsApiKey && !meta.needsApiSecret && !meta.needsEndpoint
      ? `${providerLabel} can be configured without pasting a secret.`
      : null
  };
}

function normalizeProviderSetupString(value: unknown): string | null {
  return typeof value === "string" && value.trim().length > 0 ? value.trim() : null;
}

function normalizeProviderSetupStringArray(values: readonly unknown[] | null | undefined): string[] {
  return (values ?? [])
    .map(normalizeProviderSetupString)
    .filter((value): value is string => value !== null);
}

function formatProviderSetupCredentialState(value: string): string {
  switch (value) {
    case "NotRequired":
      return "Not required";
    case "NotVerified":
      return "Not verified";
    default:
      return splitProviderSetupPascalCase(value);
  }
}

function formatProviderSetupCredentialSource(value: string): string {
  switch (value) {
    case "ExternalVaultReference":
      return "External vault reference";
    case "LocalEncryptedStore":
      return "Local encrypted store";
    case "NotRequired":
      return "Not required";
    default:
      return splitProviderSetupPascalCase(value);
  }
}

function splitProviderSetupPascalCase(value: string): string {
  return value
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/_/g, " ")
    .trim() || value;
}

function isValidEndpointUrl(value: string): boolean {
  try {
    const url = new URL(value.trim());
    return url.protocol === "http:" || url.protocol === "https:";
  } catch {
    return false;
  }
}

function resolveProviderKindMeta(kind: ProviderSetupFormState["kind"]): ProviderKindMeta | undefined {
  return PROVIDER_KIND_CATALOG.find((provider) => provider.kind === kind);
}

function formatProviderCapabilityLabel(capabilityId: string): string {
  return ALL_CAPABILITIES.find((capability) => capability.id === capabilityId)?.label ?? capabilityId;
}

function resolveProviderSetupNextStep(capabilities: string[]): string {
  if (capabilities.includes("backfill")) {
    return "Preview a historical backfill";
  }

  if (capabilities.includes("streaming")) {
    return "Validate live quotes";
  }

  if (capabilities.includes("brokerage")) {
    return "Check Trading readiness";
  }

  if (capabilities.includes("reference")) {
    return "Review Security Master";
  }

  return "Select a capability";
}

export function buildProviderSetupSuccessActions(form: ProviderSetupFormState): ProviderSetupNextActionState[] {
  const providerLabel = form.displayName.trim() || resolveProviderKindMeta(form.kind)?.label || "configured";
  const capabilities = new Set(form.capabilities);
  const actions: ProviderSetupNextActionState[] = [];

  if (capabilities.has("streaming")) {
    actions.push({
      id: "live-quotes",
      label: "Validate live quotes",
      href: workstationRouteWithQuery("dataQuotes", { symbol: "AAPL" }),
      ariaLabel: `Validate live quotes after configuring ${providerLabel}`,
      variant: "default"
    });
  }

  if (capabilities.has("backfill")) {
    actions.push({
      id: "backfill",
      label: "Preview a backfill",
      href: WORKSTATION_ROUTE_CATALOG.dataBackfills,
      ariaLabel: `Preview a historical backfill after configuring ${providerLabel}`,
      variant: actions.length === 0 ? "default" : "outline"
    });
  }

  if (capabilities.has("brokerage")) {
    actions.push({
      id: "readiness",
      label: "Check Trading readiness",
      href: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
      ariaLabel: `Check Trading readiness after configuring ${providerLabel}`,
      variant: actions.length === 0 ? "default" : "outline"
    });
  }

  if (actions.length === 0 || capabilities.has("reference")) {
    actions.push({
      id: "security-master",
      label: "Review Security Master",
      href: WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster,
      ariaLabel: `Review Security Master coverage after configuring ${providerLabel}`,
      variant: actions.length === 0 ? "default" : "outline"
    });
  }

  return actions;
}

function buildProviderCredentialFields(
  form: ProviderSetupFormState,
  meta: ProviderKindMeta | undefined,
  disabledReason: string | null
): ProviderSetupCredentialFieldState[] {
  const disabled = disabledReason !== null;
  const fields: ProviderSetupCredentialFieldState[] = [];

  if (meta?.needsApiKey !== false) {
    fields.push({
      id: "provider-setup-apikey",
      label: "API key",
      ariaLabel: "Provider API key",
      field: "apiKey",
      type: "password",
      value: form.apiKey,
      autoComplete: "new-password",
      placeholder: "Stored server-side; never sent to the browser after save",
      disabled,
      disabledReason
    });
  }

  if (meta?.needsApiSecret) {
    fields.push({
      id: "provider-setup-apisecret",
      label: "API secret",
      ariaLabel: "Provider API secret",
      field: "apiSecret",
      type: "password",
      value: form.apiSecret,
      autoComplete: "new-password",
      placeholder: null,
      disabled,
      disabledReason
    });
  }

  if (meta?.needsEndpoint) {
    fields.push({
      id: "provider-setup-endpoint",
      label: "Endpoint URL",
      ariaLabel: "Provider endpoint URL",
      field: "endpoint",
      type: "url",
      value: form.endpoint,
      autoComplete: "off",
      placeholder: "https://localhost:7497 or https://api.yourprovider.com",
      disabled,
      disabledReason
    });
  }

  return fields;
}
