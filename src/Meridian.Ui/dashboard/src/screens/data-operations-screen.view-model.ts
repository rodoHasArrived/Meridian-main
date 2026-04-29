import { useCallback, useMemo, useState } from "react";
import * as workstationApi from "@/lib/api";
import type {
  BackfillProgressResponse,
  BackfillTriggerRequest,
  BackfillTriggerResult,
  DataOperationsBackfillRecord,
  DataOperationsExportRecord,
  DataOperationsProviderRecord,
  DataOperationsWorkspaceResponse
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
  symbolsHelpText: string;
  statusAnnouncement: string;
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
  scope: string;
  provider: string;
  status: DataOperationsBackfillRecord["status"];
  progress: string;
  updatedAt: string;
  selected: boolean;
  detailText: string;
  ariaLabel: string;
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
  backfillDetailEmptyState: DataOperationsEmptyState | null;
}

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

  const workstream = useMemo(() => resolveDataOperationsWorkstream(pathname), [pathname]);
  const selectedBackfill = useMemo(
    () => resolveSelectedBackfill(data?.backfills ?? [], selectedBackfillId),
    [data, selectedBackfillId]
  );
  const selectedBackfillNarrative = selectedBackfill ? buildBackfillNarrative(selectedBackfill) : null;
  const presentation = useMemo(
    () => buildDataOperationsPresentationState(data, selectedBackfill?.jobId ?? null, workstream),
    [data, selectedBackfill?.jobId, workstream]
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

  return {
    workstream,
    selectedBackfill,
    selectedBackfillNarrative,
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
    ...triggerState
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

      return {
        jobId: backfill.jobId,
        scope: backfill.scope,
        provider: backfill.provider,
        status: backfill.status,
        progress: backfill.progress,
        updatedAt: backfill.updatedAt,
        selected: selectedBackfillId === backfill.jobId,
        detailText,
        ariaLabel: `Inspect backfill ${backfill.jobId}: ${detailText}`
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
    symbolsHelpText: "Separate symbols with spaces or commas. At least one symbol is required.",
    statusAnnouncement: buildBackfillStatusAnnouncement({ phase, error, preview, result })
  };
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
