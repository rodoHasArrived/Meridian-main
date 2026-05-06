import { useCallback, useMemo, useState } from "react";
import * as workstationApi from "@/lib/api";
import {
  buildSecurityMasterWorkspaceState,
  SECURITY_MASTER_DEFAULT_QUERY
} from "@/screens/data-operations-screen.security-master";
import type {
  SecurityMasterStatusFilter,
  SecurityMasterTab
} from "@/screens/data-operations-screen.security-master";
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
  describedBy?: string;
  autoFocus?: boolean;
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
  providerField: BackfillDialogFieldState;
  symbolsField: BackfillDialogFieldState;
  fromField: BackfillDialogFieldState;
  toField: BackfillDialogFieldState;
  previewAction: BackfillDialogActionState;
  runAction: BackfillDialogActionState;
  formStatusLabel: string;
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

export interface DataOperationsSectionState<T> {
  rows: T[];
  hasRows: boolean;
  emptyState: DataOperationsEmptyState;
}

export interface DataOperationsProviderRow {
  provider: string;
  status: DataOperationsProviderRecord["status"];
  capability: string;
  latencyText: string;
  note: string;
  statusTone: "success" | "warning" | "danger";
  trustFields: DataOperationsDetailField[];
  reasonCodeText: string;
  recommendedActionText: string;
  gateImpactText: string;
  ariaLabel: string;
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
  detailText: string;
  ariaLabel: string;
  detailDescription: string;
}

export interface DataOperationsBackfillDetailState {
  id: string;
  title: string;
  description: string;
  ariaLabel: string;
  rows: DataOperationsDetailField[];
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
  providerSection: DataOperationsSectionState<DataOperationsProviderRow>;
  backfillSection: DataOperationsSectionState<DataOperationsBackfillRow>;
  exportSection: DataOperationsSectionState<DataOperationsExportRow>;
  selectedBackfillDetail: DataOperationsBackfillDetailState | null;
  backfillDetailEmptyState: DataOperationsEmptyState | null;
}

export {
  buildSecurityMasterWorkspaceState,
  SECURITY_MASTER_DEFAULT_QUERY
} from "@/screens/data-operations-screen.security-master";
export type {
  SecurityMasterStatusFilter,
  SecurityMasterTab,
  SecurityMasterWorkspaceState
} from "@/screens/data-operations-screen.security-master";

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
  closeButtonLabel: string;
  closeButtonDisabledReason: string | null;
  submitAction: {
    label: string;
    ariaLabel: string;
    disabled: boolean;
    disabledReason: string | null;
    busy: boolean;
    busyLabel: string;
  };
  statusLabel: string;
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
  kind: "polygon",
  displayName: "Polygon.io",
  apiKey: "",
  apiSecret: "",
  endpoint: "",
  capabilities: ["streaming", "backfill", "reference"]
};

// --- Backfill setup defaults ---

const defaultBackfillServices: BackfillTriggerServices = {
  preview: (request) => workstationApi.previewBackfill(request),
  run: (request) => workstationApi.triggerBackfill(request),
  getProgress: () => workstationApi.getBackfillProgress()
};

const defaultBackfillForm: BackfillFormState = {
  provider: "polygon",
  symbols: "",
  from: "",
  to: ""
};

export function useDataOperationsViewModel(
  data: DataOperationsWorkspaceResponse | null,
  pathname: string,
  services: BackfillTriggerServices = defaultBackfillServices
) {
  const [selectedBackfillId, setSelectedBackfillId] = useState<string | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [form, setForm] = useState<BackfillFormState>(defaultBackfillForm);
  const [preview, setPreview] = useState<BackfillTriggerResult | null>(null);
  const [result, setResult] = useState<BackfillTriggerResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [phase, setPhase] = useState<BackfillPhase>("idle");
  const [securityMasterQuery, setSecurityMasterQuery] = useState(SECURITY_MASTER_DEFAULT_QUERY);
  const [selectedSecurityMasterId, setSelectedSecurityMasterId] = useState<string | null>(null);
  const [securityMasterTab, setSecurityMasterTab] = useState<SecurityMasterTab>("overview");
  const [securityMasterStatusFilter, setSecurityMasterStatusFilter] = useState<SecurityMasterStatusFilter>("active");

  // Provider setup state
  const [providerSetupOpen, setProviderSetupOpen] = useState(false);
  const [providerForm, setProviderForm] = useState<ProviderSetupFormState>(defaultProviderSetupForm);
  const [providerPhase, setProviderPhase] = useState<ProviderSetupPhase>("idle");
  const [providerSetupResult, setProviderSetupResult] = useState<ProviderSetupResult | null>(null);
  const [providerSetupError, setProviderSetupError] = useState<string | null>(null);

  const workstream = useMemo(() => resolveDataOperationsWorkstream(pathname), [pathname]);
  const selectedBackfill = useMemo(
    () => resolveSelectedBackfill(data?.backfills ?? [], selectedBackfillId),
    [data, selectedBackfillId]
  );
  const presentation = useMemo(
    () => buildDataOperationsPresentationState(data, selectedBackfill?.jobId ?? null, workstream),
    [data, selectedBackfill?.jobId, workstream]
  );
  const securityMaster = useMemo(
    () => buildSecurityMasterWorkspaceState({
      query: securityMasterQuery,
      selectedSecurityId: selectedSecurityMasterId,
      activeTab: securityMasterTab,
      statusFilter: securityMasterStatusFilter
    }),
    [securityMasterQuery, selectedSecurityMasterId, securityMasterStatusFilter, securityMasterTab]
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
    setDialogOpen(true);
    setPreview(null);
    setResult(null);
    setError(null);
    setPhase("idle");
  }, []);

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

    setBusy(true);
    setPhase("previewing");
    setError(null);
    setResult(null);

    try {
      const nextPreview = await services.preview(buildBackfillRequest(form));
      setPreview(nextPreview);
    } catch (err) {
      setPreview(null);
      setError(err instanceof Error ? err.message : "Backfill preview failed.");
    } finally {
      setBusy(false);
      setPhase("idle");
    }
  }, [form, services]);

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

    setBusy(true);
    setPhase("running");
    setError(null);

    try {
      const nextResult = await services.run(buildBackfillRequest(form));
      setResult(nextResult);
      await services.getProgress().catch(() => null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Backfill run failed.");
    } finally {
      setBusy(false);
      setPhase("idle");
    }
  }, [form, preview, services]);

  const openProviderSetup = useCallback(() => {
    setProviderSetupOpen(true);
    setProviderSetupResult(null);
    setProviderSetupError(null);
    setProviderPhase("idle");
  }, []);

  const closeProviderSetup = useCallback(() => {
    if (providerPhase === "submitting") return;
    setProviderSetupOpen(false);
  }, [providerPhase]);

  const updateProviderForm = useCallback((field: Exclude<keyof ProviderSetupFormState, "capabilities">, value: string) => {
    setProviderForm((current) => {
      if (field === "kind") {
        const meta = PROVIDER_KIND_CATALOG.find((p) => p.kind === value);
        return {
          ...current,
          kind: value,
          displayName: meta?.label ?? current.displayName,
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
      setProviderSetupResult(response);
      setProviderPhase(response.success ? "success" : "error");
    } catch (err) {
      const message = err instanceof Error ? err.message : "Provider setup failed.";
      setProviderSetupError(message);
      setProviderPhase("error");
    }
  }, [providerForm]);

  const providerSetupDialogState = useMemo(
    () => buildProviderSetupDialogState(providerPhase, providerForm),
    [providerPhase, providerForm]
  );

  const updateSecurityMasterQuery = useCallback((value: string) => {
    setSecurityMasterQuery(value);
    setSelectedSecurityMasterId(null);
  }, []);

  const selectSecurityMaster = useCallback((securityId: string) => {
    setSelectedSecurityMasterId(securityId);
  }, []);

  const selectSecurityMasterTab = useCallback((tab: SecurityMasterTab) => {
    setSecurityMasterTab(tab);
  }, []);

  const toggleSecurityMasterStatusFilter = useCallback(() => {
    setSecurityMasterStatusFilter((current) => current === "active" ? "all" : "active");
    setSelectedSecurityMasterId(null);
  }, []);

  return {
    workstream,
    securityMaster,
    securityMasterQuery,
    updateSecurityMasterQuery,
    selectSecurityMaster,
    securityMasterTab,
    selectSecurityMasterTab,
    securityMasterStatusFilter,
    toggleSecurityMasterStatusFilter,
    selectedBackfill,
    selectedBackfillId,
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

export function buildDataOperationsPresentationState(
  data: DataOperationsWorkspaceResponse | null,
  selectedBackfillId: string | null,
  workstream: "overview" | "backfills" = "overview"
): DataOperationsPresentationState {
  const providers = data?.providers ?? [];
  const backfills = data?.backfills ?? [];
  const exports = data?.exports ?? [];

  return {
    providerSection: buildProviderSection(providers),
    backfillSection: buildBackfillSection(backfills, selectedBackfillId, workstream),
    exportSection: buildExportSection(exports),
    selectedBackfillDetail: buildSelectedBackfillDetail(backfills, selectedBackfillId),
    backfillDetailEmptyState: backfills.length === 0
      ? {
          title: "No backfill activity yet",
          description: "Preview a historical repair or wait for queued jobs to appear before using this detail panel."
        }
      : null
  };
}

export function buildProviderSection(
  providers: DataOperationsProviderRecord[]
): DataOperationsSectionState<DataOperationsProviderRow> {
  return {
    rows: providers.map(buildProviderRow),
    hasRows: providers.length > 0,
    emptyState: {
      title: "No providers reported",
      description: "Check provider configuration or run provider detection before relying on live, backfill, or export data."
    }
  };
}

export function buildProviderRow(provider: DataOperationsProviderRecord): DataOperationsProviderRow {
  const latencyText = formatProviderValue(provider.latency, "Latency not reported");
  const trustScoreText = formatProviderValue(provider.trustScore, "Trust score not reported");
  const signalSourceText = formatProviderValue(provider.signalSource, "Signal source not reported");
  const reasonCodeText = formatProviderValue(provider.reasonCode, "Reason code not reported");
  const recommendedActionText = formatProviderValue(provider.recommendedAction, "No operator action reported");
  const gateImpactText = formatProviderValue(provider.gateImpact, "No gate impact reported");

  return {
    provider: provider.provider,
    status: provider.status,
    capability: provider.capability,
    latencyText,
    note: provider.note,
    statusTone: provider.status === "Healthy" ? "success" : provider.status === "Degraded" ? "danger" : "warning",
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
    ariaLabel: [
      `${provider.provider} provider ${provider.status}`,
      provider.capability,
      provider.note,
      `Latency ${latencyText}`,
      `Trust score ${trustScoreText}`,
      `Gate impact ${gateImpactText}`,
      `Recommended action ${recommendedActionText}`
    ].join(". ")
  };
}

export function buildBackfillSection(
  backfills: DataOperationsBackfillRecord[],
  selectedBackfillId: string | null,
  workstream: "overview" | "backfills" = "overview"
): DataOperationsSectionState<DataOperationsBackfillRow> {
  return {
    rows: backfills.map((backfill) => {
      const detailText = `${backfill.scope}. ${backfill.status}; ${backfill.progress}; updated ${backfill.updatedAt}.`;
      const selected = selectedBackfillId === backfill.jobId;
      const rowId = buildBackfillRowId(backfill.jobId);
      const detailPanelId = buildBackfillDetailPanelId(backfill.jobId);

      return {
        jobId: backfill.jobId,
        rowId,
        detailPanelId,
        scope: backfill.scope,
        provider: backfill.provider,
        status: backfill.status,
        progress: backfill.progress,
        updatedAt: backfill.updatedAt,
        selected,
        detailText,
        ariaLabel: `${selected ? "Selected" : "Inspect"} backfill ${backfill.jobId}: ${detailText}`,
        detailDescription: selected
          ? `Selected backfill ${backfill.jobId}; details are shown in the backfill detail panel.`
          : `Inspect backfill ${backfill.jobId}; details will replace the current backfill detail panel.`
      };
    }),
    hasRows: backfills.length > 0,
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
    id: buildBackfillDetailPanelId(selected.jobId),
    title: selected.scope,
    description,
    ariaLabel: `Backfill detail for ${selected.jobId}: ${selected.scope}. ${description}`,
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
    dialogState: buildBackfillDialogState({ busy, phase, validationError, preview, error, result })
  };
}

export function buildBackfillDialogState({
  busy,
  phase,
  validationError,
  preview,
  error = null,
  result = null
}: {
  busy: boolean;
  phase: BackfillPhase;
  validationError: string | null;
  preview: BackfillTriggerResult | null;
  error?: string | null;
  result?: BackfillTriggerResult | null;
}): BackfillDialogState {
  const previewDisabledReason = resolveBackfillPreviewDisabledReason({ busy, phase, validationError });
  const runDisabledReason = resolveBackfillRunDisabledReason({ busy, phase, validationError, preview });

  return {
    titleId: "backfill-dialog-title",
    descriptionId: "backfill-dialog-description",
    formLabel: "Backfill request form",
    closeButtonLabel: "Close backfill dialog",
    closeButtonDisabledReason: busy ? "Backfill request is running; wait for the current request to finish before closing." : null,
    providerField: {
      id: "backfill-provider",
      label: "Provider",
      ariaLabel: "Backfill provider"
    },
    symbolsField: {
      id: "backfill-symbols",
      label: "Symbols",
      ariaLabel: "Backfill symbols",
      describedBy: "backfill-symbols-help backfill-form-status backfill-form-feedback",
      autoFocus: true
    },
    fromField: {
      id: "backfill-from",
      label: "From",
      ariaLabel: "Backfill start date"
    },
    toField: {
      id: "backfill-to",
      label: "To",
      ariaLabel: "Backfill end date"
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
    formStatusLabel: buildBackfillFormStatusLabel({ busy, phase, validationError, preview, error, result })
  };
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

  const fromTime = form.from.trim() ? Date.parse(form.from) : null;
  const toTime = form.to.trim() ? Date.parse(form.to) : null;
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
  const trimmed = value.trim();
  return /^\d{4}-\d{2}-\d{2}$/.test(trimmed) && !Number.isNaN(Date.parse(trimmed));
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

function buildBackfillDetailPanelId(jobId: string): string {
  return `backfill-detail-${toDomId(jobId)}`;
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

  if (form.capabilities.length === 0) {
    return "Select at least one capability for this provider.";
  }

  return null;
}

export function buildProviderSetupDialogState(
  phase: ProviderSetupPhase,
  form: ProviderSetupFormState
): ProviderSetupDialogState {
  const submitting = phase === "submitting";
  const validationError = phase === "submitting" ? null : validateProviderSetupForm(form);

  return {
    titleId: "provider-setup-dialog-title",
    descriptionId: "provider-setup-dialog-description",
    formLabel: "Provider setup form",
    closeButtonLabel: "Close provider setup",
    closeButtonDisabledReason: submitting ? "Provider setup is in progress; wait before closing." : null,
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
    statusLabel: buildProviderSetupStatusLabel(phase, validationError)
  };
}

function buildProviderSetupStatusLabel(phase: ProviderSetupPhase, validationError: string | null): string {
  if (phase === "submitting") return "Registering provider with Meridian.";
  if (phase === "success") return "Provider configured successfully.";
  if (phase === "error") return "Provider setup encountered an error.";
  if (validationError) return validationError;
  return "Fill in provider details and click Configure provider.";
}
