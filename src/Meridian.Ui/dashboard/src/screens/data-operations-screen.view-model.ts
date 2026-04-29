import { useCallback, useMemo, useState } from "react";
import * as workstationApi from "@/lib/api";
import type {
  BackfillProgressResponse,
  BackfillTriggerRequest,
  BackfillTriggerResult,
  DataOperationsBackfillRecord,
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

export interface BackfillTriggerServices {
  preview: (request: BackfillTriggerRequest) => Promise<BackfillTriggerResult>;
  run: (request: BackfillTriggerRequest) => Promise<BackfillTriggerResult>;
  getProgress: () => Promise<BackfillProgressResponse>;
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

  const triggerState = useMemo(
    () => buildBackfillTriggerState({ form, busy, phase, error, preview, result }),
    [busy, error, form, phase, preview, result]
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
    dialogOpen,
    openBackfillDialog,
    closeBackfillDialog,
    form,
    updateBackfillForm,
    preview,
    result,
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
