import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import * as workstationApi from "@/lib/api";
import type {
  BackfillProgressResponse,
  BackfillTriggerRequest,
  BackfillTriggerResult,
  DataOperationsBackfillRecord,
  DataOperationsExportRecord,
  DataOperationsProviderRecord,
  DataOperationsWorkspaceResponse,
  ProviderKind,
  ProviderKindMeta,
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

export interface DataOperationsProviderSectionState extends DataOperationsSectionState<DataOperationsProviderRow> {
  tableLabel: string;
  description: string;
  detailPanelId: string;
  selectedRowId: string | null;
  selectedDetail: DataOperationsProviderDetailState | null;
  detailEmptyState: DataOperationsEmptyState | null;
}

export interface DataOperationsProviderRow {
  provider: string;
  rowId: string;
  detailPanelId: string;
  status: DataOperationsProviderRecord["status"];
  capability: string;
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
  title: string;
  subtitle: string;
  description: string;
  ariaLabel: string;
  status: DataOperationsProviderRecord["status"];
  statusTone: "success" | "warning" | "danger";
  fields: DataOperationsDetailField[];
  actionText: string;
  reasonCodeText: string;
  gateImpactText: string;
}

export interface DataOperationsExportRow {
  exportId: string;
  profile: string;
  target: string;
  status: DataOperationsExportRecord["status"];
  statusLabel: string;
  statusVariant: "success" | "warning" | "paper";
  statusTone: "success" | "warning" | "paper";
  rows: string;
  updatedAt: string;
  summaryText: string;
  detailFields: DataOperationsDetailField[];
  actionText: string;
  ariaLabel: string;
}

export interface DataOperationsPresentationState {
  providerSection: DataOperationsProviderSectionState;
  backfillSection: DataOperationsBackfillSectionState;
  exportSection: DataOperationsSectionState<DataOperationsExportRow>;
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

// --- Provider setup types ---

export interface ProviderSetupFormState {
  kind: ProviderKind | string;
  displayName: string;
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

const defaultBackfillForm: BackfillFormState = {
  provider: "yahoo",
  symbols: "",
  from: "",
  to: ""
};

export const DATA_BACKFILL_DETAIL_PANEL_ID = "data-backfill-detail-panel";
export const DATA_BACKFILL_ROUTE_FOCUS_CARD_ID = "data-backfill-route-focus";
export const DATA_PROVIDER_DETAIL_PANEL_ID = "data-provider-detail-panel";

export function useDataOperationsViewModel(
  data: DataOperationsWorkspaceResponse | null,
  pathname: string,
  services: BackfillTriggerServices = defaultBackfillServices
) {
  const [selectedProviderId, setSelectedProviderId] = useState<string | null>(null);
  const [selectedBackfillId, setSelectedBackfillId] = useState<string | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [form, setForm] = useState<BackfillFormState>(defaultBackfillForm);
  const [preview, setPreview] = useState<BackfillTriggerResult | null>(null);
  const [result, setResult] = useState<BackfillTriggerResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [phase, setPhase] = useState<BackfillPhase>("idle");
  const backfillCommandRevisionRef = useRef(0);

  // Provider setup state
  const [providerSetupOpen, setProviderSetupOpen] = useState(false);
  const [providerForm, setProviderForm] = useState<ProviderSetupFormState>(defaultProviderSetupForm);
  const [providerPhase, setProviderPhase] = useState<ProviderSetupPhase>("idle");
  const [providerSetupResult, setProviderSetupResult] = useState<ProviderSetupResult | null>(null);
  const [providerSetupError, setProviderSetupError] = useState<string | null>(null);
  const providerSetupCommandRevisionRef = useRef(0);

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
  }, []);

  const workstream = useMemo(() => resolveDataOperationsWorkstream(pathname), [pathname]);
  const selectedProvider = useMemo(
    () => resolveSelectedProvider(data?.providers ?? [], selectedProviderId),
    [data, selectedProviderId]
  );
  const selectedProviderRowId = selectedProvider ? buildProviderRowId(selectedProvider.provider) : null;
  const selectedBackfill = useMemo(
    () => resolveSelectedBackfill(data?.backfills ?? [], selectedBackfillId),
    [data, selectedBackfillId]
  );
  const presentation = useMemo(
    () => buildDataOperationsPresentationState(data, selectedBackfill?.jobId ?? null, workstream, selectedProviderRowId),
    [data, selectedBackfill?.jobId, selectedProviderRowId, workstream]
  );
  const loadingState = useMemo(
    () => buildDataOperationsLoadingState(workstream),
    [workstream]
  );

  const triggerState = useMemo(
    () => buildBackfillTriggerState({ form, busy, phase, error, preview, result }),
    [busy, error, form, phase, preview, result]
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
    const validationError = validateBackfillForm(form);
    if (validationError) {
      setError(validationError);
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
      setError(err instanceof Error ? err.message : "Backfill preview failed.");
    } finally {
      if (isCurrentBackfillCommand(commandRevision)) {
        setBusy(false);
        setPhase("idle");
      }
    }
  }, [form, isCurrentBackfillCommand, nextBackfillCommandRevision, services]);

  const runBackfill = useCallback(async () => {
    const validationError = validateBackfillForm(form);
    if (validationError) {
      setError(validationError);
      return;
    }

    if (!preview) {
      setError("Preview the request before running the backfill.");
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
      setError(err instanceof Error ? err.message : "Backfill run failed.");
    } finally {
      if (isCurrentBackfillCommand(commandRevision)) {
        setBusy(false);
        setPhase("idle");
      }
    }
  }, [form, isCurrentBackfillCommand, nextBackfillCommandRevision, preview, services]);

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

  const updateProviderForm = useCallback((field: Exclude<keyof ProviderSetupFormState, "capabilities">, value: string) => {
    setProviderForm((current) => {
      if (field === "kind") {
        const meta = PROVIDER_KIND_CATALOG.find((p) => p.kind === value);
        return {
          ...current,
          kind: value,
          displayName: meta?.label ?? current.displayName,
          apiKey: "",
          apiSecret: "",
          endpoint: meta?.needsEndpoint ? current.endpoint : "",
          capabilities: meta?.defaultCapabilities ?? current.capabilities
        };
      }
      return { ...current, [field]: value };
    });
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
      setProviderSetupError(validationError);
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
      capabilities: providerForm.capabilities
    };

    try {
      const response = await workstationApi.setupProvider(request);
      if (!isCurrentProviderSetupCommand(commandRevision)) {
        return;
      }
      setProviderSetupResult(response);
      setProviderPhase(response.success ? "success" : "error");
    } catch (err) {
      if (!isCurrentProviderSetupCommand(commandRevision)) {
        return;
      }
      const message = err instanceof Error ? err.message : "Provider setup failed.";
      setProviderSetupError(message);
      setProviderPhase("error");
    } finally {
      if (isCurrentProviderSetupCommand(commandRevision)) {
        setProviderForm(clearProviderSetupCredentials);
      }
    }
  }, [isCurrentProviderSetupCommand, nextProviderSetupCommandRevision, providerForm]);

  const providerSetupDialogState = useMemo(
    () => buildProviderSetupDialogState(providerPhase, providerForm),
    [providerPhase, providerForm]
  );

  return {
    workstream,
    loadingState,
    selectedProvider,
    selectedProviderId,
    selectedProviderRowId,
    selectProvider: setSelectedProviderId,
    selectedBackfill,
    selectedBackfillId,
    selectedBackfillRowId: selectedBackfill ? buildBackfillRowId(selectedBackfill.jobId) : null,
    selectBackfill: setSelectedBackfillId,
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
        href: "/settings#alpaca-provider-setup",
        ariaLabel: "Open Alpaca paper provider setup while Data workspace loads",
        variant: "default"
      },
      {
        id: "quotes",
        label: "Open live quotes",
        href: "/data/quotes",
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
  selectedProviderId: string | null = null
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
    providerSection: buildProviderSection(providers, selectedProviderId),
    backfillSection: buildBackfillSection(backfills, selectedBackfillId, workstream),
    exportSection: buildExportSection(exports),
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
      href: "/accounting/security-master",
      ariaLabel: "Open Security Master in Accounting"
    }
  };
}

export function buildProviderSection(
  providers: DataOperationsProviderRecord[],
  selectedProviderId: string | null = null
): DataOperationsProviderSectionState {
  const selectedProvider = resolveSelectedProvider(providers, selectedProviderId);
  const selectedRowId = selectedProvider ? buildProviderRowId(selectedProvider.provider) : null;

  return {
    rows: providers.map((provider) => buildProviderRow(provider, selectedRowId)),
    hasRows: providers.length > 0,
    tableLabel: "Provider health",
    description: "Provider trust, latency, gate impact, and recommended recovery actions.",
    detailPanelId: DATA_PROVIDER_DETAIL_PANEL_ID,
    selectedRowId,
    selectedDetail: buildSelectedProviderDetail(providers, selectedRowId),
    detailEmptyState: providers.length === 0
      ? {
          title: "No provider selected",
          description: "Configure a provider before inspecting trust evidence, latency, gate impact, or recovery actions."
        }
      : null,
    emptyState: {
      title: "No providers configured",
      description: "Check provider configuration or run provider detection before relying on live, backfill, or export data."
    }
  };
}

export function buildProviderRow(
  provider: DataOperationsProviderRecord,
  selectedProviderId: string | null = null
): DataOperationsProviderRow {
  const rowId = buildProviderRowId(provider.provider);
  const latencyText = formatProviderValue(provider.latency, "Latency not reported");
  const trustScoreText = formatProviderValue(provider.trustScore, "Trust score not reported");
  const signalSourceText = formatProviderValue(provider.signalSource, "Signal source not reported");
  const reasonCodeText = formatProviderValue(provider.reasonCode, "Reason code not reported");
  const recommendedActionText = formatProviderValue(provider.recommendedAction, "No operator action reported");
  const gateImpactText = formatProviderValue(provider.gateImpact, "No gate impact reported");
  const selected = rowId === selectedProviderId;
  const statusTone = resolveProviderStatusTone(provider.status);

  return {
    provider: provider.provider,
    rowId,
    detailPanelId: DATA_PROVIDER_DETAIL_PANEL_ID,
    status: provider.status,
    capability: provider.capability,
    latencyText,
    trustScoreText,
    signalSourceText,
    note: provider.note,
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
      `${selected ? "Selected" : "Inspect"} provider ${provider.provider}`,
      `Status ${provider.status}`,
      provider.capability,
      provider.note,
      `Latency ${latencyText}`,
      `Trust score ${trustScoreText}`,
      `Gate impact ${gateImpactText}`,
      `Recommended action ${recommendedActionText}`
    ].join(". "),
    selectAriaLabel: `Inspect provider ${provider.provider}`,
    detailDescription: selected
      ? `Selected provider ${provider.provider}; the provider detail panel is expanded for this row.`
      : `Inspect provider ${provider.provider}; activation updates the shared provider detail panel.`
  };
}

export function buildSelectedProviderDetail(
  providers: DataOperationsProviderRecord[],
  selectedProviderId: string | null
): DataOperationsProviderDetailState | null {
  const selected = resolveSelectedProvider(providers, selectedProviderId);

  if (!selected) {
    return null;
  }

  const row = buildProviderRow(selected, buildProviderRowId(selected.provider));

  return {
    id: DATA_PROVIDER_DETAIL_PANEL_ID,
    title: selected.provider,
    subtitle: selected.capability,
    description: `${selected.note} ${row.gateImpactText}.`,
    ariaLabel: `Provider detail for ${selected.provider}: ${selected.status}. ${selected.capability}. ${row.recommendedActionText}`,
    status: selected.status,
    statusTone: row.statusTone,
    fields: row.trustFields,
    actionText: row.recommendedActionText,
    reasonCodeText: row.reasonCodeText,
    gateImpactText: row.gateImpactText
  };
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
  exports: DataOperationsExportRecord[]
): DataOperationsSectionState<DataOperationsExportRow> {
  return {
    rows: exports.map((item) => {
      const statusVariant = exportStatusVariant(item.status);
      const actionText = exportActionText(item.status);
      const summaryText = `${item.target} · ${item.rows} · ${item.updatedAt}`;
      const detailFields = [
        { id: "export-id", label: "Export ID", value: item.exportId },
        { id: "target", label: "Target", value: item.target },
        { id: "rows", label: "Rows", value: item.rows },
        { id: "updated", label: "Updated", value: item.updatedAt }
      ];

      return {
        exportId: item.exportId,
        profile: item.profile,
        target: item.target,
        status: item.status,
        statusLabel: item.status,
        statusVariant,
        statusTone: statusVariant,
        rows: item.rows,
        updatedAt: item.updatedAt,
        summaryText,
        detailFields,
        actionText,
        ariaLabel: [
          `${item.profile} export ${item.status}`,
          `Target ${item.target}`,
          `Rows ${item.rows}`,
          `Updated ${item.updatedAt}`,
          `Next action ${actionText}`
        ].join(". ")
      };
    }),
    hasRows: exports.length > 0,
    emptyState: {
      title: "No exports available",
      description: "Generated packages and reporting outputs will appear here with target, row count, and readiness status."
    }
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

export function buildBackfillTriggerState({
  form,
  busy,
  phase,
  error,
  preview,
  result
}: {
  form: BackfillFormState;
  busy: boolean;
  phase: BackfillPhase;
  error: string | null;
  preview: BackfillTriggerResult | null;
  result: BackfillTriggerResult | null;
}): BackfillTriggerState {
  const validationError = validateBackfillForm(form);
  const feedbackText = error;
  const feedbackTone = error
    ? error === validationError
      ? "warning"
      : "danger"
    : null;

  return {
    validationError,
    feedbackText,
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
    dialogState: buildBackfillDialogState({ form, busy, phase, validationError, preview, error, result })
  };
}

export function buildBackfillDialogState({
  form,
  busy,
  phase,
  validationError,
  preview,
  error = null,
  result = null
}: {
  form: BackfillFormState;
  busy: boolean;
  phase: BackfillPhase;
  validationError: string | null;
  preview: BackfillTriggerResult | null;
  error?: string | null;
  result?: BackfillTriggerResult | null;
}): BackfillDialogState {
  const previewDisabledReason = resolveBackfillPreviewDisabledReason({ busy, phase, validationError });
  const runDisabledReason = resolveBackfillRunDisabledReason({ busy, phase, validationError, preview });
  const fieldDisabledReason = busy
    ? "Backfill request is running; wait for the current request to finish before editing."
    : null;

  return {
    titleId: "backfill-dialog-title",
    descriptionId: "backfill-dialog-description",
    formLabel: "Backfill request form",
    closeButtonLabel: "Close backfill dialog",
    closeButtonDisabledReason: busy ? "Backfill request is running; wait for the current request to finish before closing." : null,
    summaryItems: buildBackfillDialogSummaryItems(form),
    providerField: {
      id: "backfill-provider",
      label: "Provider",
      ariaLabel: "Backfill provider",
      placeholder: "Select a provider",
      disabled: busy,
      disabledReason: fieldDisabledReason
    },
    providerOptions: buildBackfillProviderOptions(form.provider),
    selectedProviderDetail: buildBackfillProviderDetail(form.provider),
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

export function buildBackfillDialogSummaryItems(form: BackfillFormState): BackfillDialogSummaryItemState[] {
  const provider = resolveBackfillProviderLabel(form.provider);
  const symbols = parseSymbols(form.symbols);
  const range = formatBackfillRange(form.from.trim() || null, form.to.trim() || null);

  return [
    { id: "provider", label: "Provider", value: provider, tone: "default" },
    {
      id: "symbols",
      label: "Symbols",
      value: symbols.length > 0 ? `${symbols.length} selected` : "None yet",
      tone: symbols.length > 0 ? "default" : "warning"
    },
    { id: "range", label: "Range", value: range, tone: "default" }
  ];
}

export function buildBackfillProviderOptions(selectedProvider: string): BackfillProviderOptionState[] {
  const selected = selectedProvider.trim().toLowerCase();
  const hasSelectedOption = BACKFILL_PROVIDER_OPTIONS.some((option) => option.value === selected);
  const options = BACKFILL_PROVIDER_OPTIONS;

  if (!selected || hasSelectedOption) {
    return options;
  }

  return [
    ...options,
    {
      value: selected,
      label: selectedProvider.trim(),
      description: "Custom provider id. Meridian will submit it exactly as selected.",
      badge: "Custom"
    }
  ];
}

export function buildBackfillProviderDetail(provider: string): string {
  const selected = provider.trim().toLowerCase();
  const option = BACKFILL_PROVIDER_OPTIONS.find((item) => item.value === selected);
  return option?.description ?? "Custom provider id. Use this only when the host is configured for that provider.";
}

function resolveBackfillProviderLabel(provider: string): string {
  const trimmed = provider.trim();
  const option = BACKFILL_PROVIDER_OPTIONS.find((item) => item.value === trimmed.toLowerCase());
  return option?.label ?? (trimmed.length > 0 ? trimmed : "Default provider");
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
  error: string | null;
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
    return error;
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
  error: string | null;
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

export function validateBackfillForm(form: BackfillFormState): string | null {
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

function buildProviderRowId(provider: string): string {
  return `provider-row-${toDomId(provider)}`;
}

function resolveProviderStatusTone(
  status: DataOperationsProviderRecord["status"]
): DataOperationsProviderRow["statusTone"] {
  if (status === "Healthy") {
    return "success";
  }

  if (status === "Degraded") {
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
  error: string | null;
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
    return `Backfill request failed: ${error}`;
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

  if (form.capabilities.length === 0) {
    return "Select at least one capability for this provider.";
  }

  return null;
}

export function clearProviderSetupCredentials(form: ProviderSetupFormState): ProviderSetupFormState {
  return {
    ...form,
    apiKey: "",
    apiSecret: ""
  };
}

export function buildProviderSetupDialogState(
  phase: ProviderSetupPhase,
  form: ProviderSetupFormState
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
    successActions: buildProviderSetupSuccessActions(form)
  };
}

function buildProviderSetupStatusLabel(phase: ProviderSetupPhase, validationError: string | null): string {
  if (phase === "submitting") return "Registering provider with Meridian.";
  if (phase === "success") return "Provider configured successfully.";
  if (phase === "error") return "Provider setup encountered an error.";
  if (validationError) return validationError;
  return "Provider setup is ready to submit.";
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
      href: "/data/quotes?symbol=AAPL",
      ariaLabel: `Validate live quotes after configuring ${providerLabel}`,
      variant: "default"
    });
  }

  if (capabilities.has("backfill")) {
    actions.push({
      id: "backfill",
      label: "Preview a backfill",
      href: "/data/backfills",
      ariaLabel: `Preview a historical backfill after configuring ${providerLabel}`,
      variant: actions.length === 0 ? "default" : "outline"
    });
  }

  if (capabilities.has("brokerage")) {
    actions.push({
      id: "readiness",
      label: "Check Trading readiness",
      href: "/trading/readiness",
      ariaLabel: `Check Trading readiness after configuring ${providerLabel}`,
      variant: actions.length === 0 ? "default" : "outline"
    });
  }

  if (actions.length === 0 || capabilities.has("reference")) {
    actions.push({
      id: "security-master",
      label: "Review Security Master",
      href: "/accounting/security-master",
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
