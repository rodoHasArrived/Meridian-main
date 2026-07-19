import { useCallback, useEffect, useMemo, useState } from "react";
import {
  deleteStatementFetchSchedule,
  fetchStatementPreview,
  listStatementFetchSchedules,
  runStatementFetchSchedule,
  upsertStatementFetchSchedule
} from "@/lib/api";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import type {
  StatementConnectorDescriptor,
  StatementFetchDatasets,
  StatementFetchSchedule,
  StatementFetchScheduleUpsertRequest,
  StatementImportCommitResult,
  StatementImportPreview,
  StatementMappingProfile
} from "@/types";

export interface StatementFetchPanelServices {
  deleteSchedule: typeof deleteStatementFetchSchedule;
  fetchPreview: typeof fetchStatementPreview;
  listSchedules: typeof listStatementFetchSchedules;
  runSchedule: typeof runStatementFetchSchedule;
  upsertSchedule: typeof upsertStatementFetchSchedule;
}

const DEFAULT_SERVICES: StatementFetchPanelServices = {
  deleteSchedule: deleteStatementFetchSchedule,
  fetchPreview: fetchStatementPreview,
  listSchedules: listStatementFetchSchedules,
  runSchedule: runStatementFetchSchedule,
  upsertSchedule: upsertStatementFetchSchedule
};

export interface StatementFetchDraft {
  cadenceHours: string;
  connectorId: string;
  datasets: StatementFetchDatasets;
  enabled: boolean;
  externalAccountId: string;
  fundAccountId: string;
  mappingProfileId: string;
  scheduleId: string;
  sinceDate: string;
  sourceInstitution: string;
  sourceKind: "broker" | "custodian";
  toleranceProfileId: string;
}

export type StatementFetchDraftField = keyof StatementFetchDraft;
export type StatementFetchDraftErrors = Partial<Record<StatementFetchDraftField, string>>;

export interface StatementFetchPanelViewModel {
  canPreview: boolean;
  canSave: boolean;
  deleteBusyId: string | null;
  deleteError: ApiErrorDisplay | null;
  draft: StatementFetchDraft;
  draftErrors: StatementFetchDraftErrors;
  editSchedule: (schedule: StatementFetchSchedule) => void;
  loadError: ApiErrorDisplay | null;
  loading: boolean;
  newSchedule: () => void;
  preview: StatementImportPreview | null;
  previewBusy: boolean;
  previewDisabledReason: string | null;
  previewError: ApiErrorDisplay | null;
  previewFetch: () => Promise<void>;
  profiles: StatementMappingProfile[];
  refreshSchedules: () => Promise<void>;
  remoteConnectors: StatementConnectorDescriptor[];
  runBusyId: string | null;
  runError: ApiErrorDisplay | null;
  runResult: StatementImportCommitResult | null;
  runSchedule: (scheduleId: string) => Promise<void>;
  saveBusy: boolean;
  saveDisabledReason: string | null;
  saveError: ApiErrorDisplay | null;
  saveMessage: string | null;
  saveSchedule: () => Promise<void>;
  schedules: StatementFetchSchedule[];
  selectedKind: string | null;
  selectKind: (kind: string) => void;
  deleteSchedule: (scheduleId: string) => Promise<void>;
  updateDraft: <Field extends StatementFetchDraftField>(field: Field, value: StatementFetchDraft[Field]) => void;
}

export interface UseStatementFetchPanelOptions {
  connectors: StatementConnectorDescriptor[];
  profiles: StatementMappingProfile[];
  services?: Partial<StatementFetchPanelServices>;
}

export function validateStatementFetchDraft(
  draft: StatementFetchDraft,
  connectors: StatementConnectorDescriptor[],
  mode: "preview" | "schedule"
): StatementFetchDraftErrors {
  const errors: StatementFetchDraftErrors = {};
  const connector = connectors.find((candidate) => candidate.connectorId === draft.connectorId);
  if (!draft.connectorId.trim()) {
    errors.connectorId = "Select a fetch-capable connector.";
  } else if (!connector?.supportsRemoteFetch) {
    errors.connectorId = "The selected connector does not support remote statement fetches.";
  }

  if (!draft.externalAccountId.trim()) {
    errors.externalAccountId = "Enter the external broker or custodian account id.";
  }

  if (draft.sinceDate && !/^\d{4}-\d{2}-\d{2}$/.test(draft.sinceDate)) {
    errors.sinceDate = "Fetch start must use YYYY-MM-DD format.";
  }

  if (mode === "schedule") {
    if (!draft.fundAccountId.trim()) {
      errors.fundAccountId = "Enter the Meridian fund account id.";
    }
    if (!draft.sourceInstitution.trim()) {
      errors.sourceInstitution = "Enter the broker or custodian name.";
    }

    const cadence = Number(draft.cadenceHours);
    if (!Number.isInteger(cadence) || cadence < 1) {
      errors.cadenceHours = "Cadence must be a whole number of hours greater than zero.";
    }
  }

  return errors;
}

export function useStatementFetchPanelViewModel({
  connectors,
  profiles,
  services: serviceOverrides
}: UseStatementFetchPanelOptions): StatementFetchPanelViewModel {
  const services = useMemo(
    () => ({ ...DEFAULT_SERVICES, ...serviceOverrides }),
    [serviceOverrides]
  );
  const remoteConnectors = useMemo(
    () => connectors.filter((connector) => connector.supportsRemoteFetch),
    [connectors]
  );
  const [draft, setDraft] = useState<StatementFetchDraft>(() => createStatementFetchDraft([]));
  const [draftErrors, setDraftErrors] = useState<StatementFetchDraftErrors>({});
  const [schedules, setSchedules] = useState<StatementFetchSchedule[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<ApiErrorDisplay | null>(null);
  const [preview, setPreview] = useState<StatementImportPreview | null>(null);
  const [previewBusy, setPreviewBusy] = useState(false);
  const [previewError, setPreviewError] = useState<ApiErrorDisplay | null>(null);
  const [selectedKind, setSelectedKind] = useState<string | null>(null);
  const [saveBusy, setSaveBusy] = useState(false);
  const [saveError, setSaveError] = useState<ApiErrorDisplay | null>(null);
  const [saveMessage, setSaveMessage] = useState<string | null>(null);
  const [runBusyId, setRunBusyId] = useState<string | null>(null);
  const [runError, setRunError] = useState<ApiErrorDisplay | null>(null);
  const [runResult, setRunResult] = useState<StatementImportCommitResult | null>(null);
  const [deleteBusyId, setDeleteBusyId] = useState<string | null>(null);
  const [deleteError, setDeleteError] = useState<ApiErrorDisplay | null>(null);

  const refreshSchedules = useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    try {
      const next = await services.listSchedules();
      setSchedules(sortSchedules(next));
    } catch (error) {
      setLoadError(describeApiError(error, "Statement fetch schedules failed to load."));
    } finally {
      setLoading(false);
    }
  }, [services]);

  useEffect(() => {
    void refreshSchedules();
  }, [refreshSchedules]);

  useEffect(() => {
    if (draft.connectorId || remoteConnectors.length === 0) {
      return;
    }

    setDraft((current) => createStatementFetchDraft(remoteConnectors, current));
  }, [draft.connectorId, remoteConnectors]);

  const updateDraft = useCallback(<Field extends StatementFetchDraftField>(
    field: Field,
    value: StatementFetchDraft[Field]
  ) => {
    setDraft((current) => {
      const next = { ...current, [field]: value };
      if (field === "connectorId") {
        const connector = remoteConnectors.find((candidate) => candidate.connectorId === value);
        next.mappingProfileId = connector?.defaultProfileId ?? "";
        if (!next.sourceInstitution.trim() || current.sourceInstitution === currentConnectorName(current, remoteConnectors)) {
          next.sourceInstitution = connector?.displayName ?? "";
        }
      }
      return next;
    });
    setDraftErrors((current) => ({ ...current, [field]: undefined }));
    setSaveMessage(null);
    setSaveError(null);
    if (["connectorId", "externalAccountId", "mappingProfileId", "sinceDate", "datasets"].includes(field)) {
      setPreview(null);
      setPreviewError(null);
      setSelectedKind(null);
    }
  }, [remoteConnectors]);

  const previewFetch = useCallback(async () => {
    const errors = validateStatementFetchDraft(draft, remoteConnectors, "preview");
    setDraftErrors(errors);
    if (Object.keys(errors).length > 0) {
      return;
    }

    setPreviewBusy(true);
    setPreviewError(null);
    setRunResult(null);
    try {
      const next = await services.fetchPreview({
        connectorId: draft.connectorId.trim(),
        externalAccountId: draft.externalAccountId.trim(),
        mappingProfileId: draft.mappingProfileId.trim() || null,
        since: draft.sinceDate ? `${draft.sinceDate}T00:00:00Z` : null,
        datasets: draft.datasets
      });
      setPreview(next);
      setSelectedKind(next.kindSummaries[0]?.kind ?? null);
    } catch (error) {
      setPreview(null);
      setSelectedKind(null);
      setPreviewError(describeApiError(error, "Remote statement preview failed."));
    } finally {
      setPreviewBusy(false);
    }
  }, [draft, remoteConnectors, services]);

  const saveSchedule = useCallback(async () => {
    const errors = validateStatementFetchDraft(draft, remoteConnectors, "schedule");
    setDraftErrors(errors);
    if (Object.keys(errors).length > 0) {
      return;
    }

    setSaveBusy(true);
    setSaveError(null);
    setSaveMessage(null);
    try {
      const saved = await services.upsertSchedule(toScheduleRequest(draft));
      setSchedules((current) => sortSchedules([
        ...current.filter((schedule) => schedule.scheduleId !== saved.scheduleId),
        saved
      ]));
      setDraft((current) => createStatementFetchDraft(remoteConnectors, current, saved));
      setSaveMessage(`Schedule ${saved.scheduleId} saved. ${saved.enabled ? "Automatic fetches are enabled." : "The schedule is paused."}`);
    } catch (error) {
      setSaveError(describeApiError(error, "Statement fetch schedule could not be saved."));
    } finally {
      setSaveBusy(false);
    }
  }, [draft, remoteConnectors, services]);

  const editSchedule = useCallback((schedule: StatementFetchSchedule) => {
    setDraft((current) => createStatementFetchDraft(remoteConnectors, current, schedule));
    setDraftErrors({});
    setSaveError(null);
    setSaveMessage(null);
    setPreview(null);
    setPreviewError(null);
    setRunResult(null);
  }, [remoteConnectors]);

  const newSchedule = useCallback(() => {
    setDraft(createStatementFetchDraft(remoteConnectors));
    setDraftErrors({});
    setSaveError(null);
    setSaveMessage(null);
    setPreview(null);
    setPreviewError(null);
    setRunResult(null);
  }, [remoteConnectors]);

  const runSchedule = useCallback(async (scheduleId: string) => {
    setRunBusyId(scheduleId);
    setRunError(null);
    setRunResult(null);
    try {
      const result = await services.runSchedule(scheduleId);
      setRunResult(result);
    } catch (error) {
      setRunError(describeApiError(error, `Statement fetch schedule ${scheduleId} failed.`));
    } finally {
      setRunBusyId(null);
      try {
        const next = await services.listSchedules();
        setSchedules(sortSchedules(next));
      } catch {
        // The run result is authoritative; a subsequent explicit refresh can recover list state.
      }
    }
  }, [services]);

  const deleteSchedule = useCallback(async (scheduleId: string) => {
    setDeleteBusyId(scheduleId);
    setDeleteError(null);
    try {
      await services.deleteSchedule(scheduleId);
      setSchedules((current) => current.filter((schedule) => schedule.scheduleId !== scheduleId));
      if (draft.scheduleId === scheduleId) {
        setDraft(createStatementFetchDraft(remoteConnectors));
      }
    } catch (error) {
      setDeleteError(describeApiError(error, `Statement fetch schedule ${scheduleId} could not be deleted.`));
    } finally {
      setDeleteBusyId(null);
    }
  }, [draft.scheduleId, remoteConnectors, services]);

  const previewErrors = validateStatementFetchDraft(draft, remoteConnectors, "preview");
  const scheduleErrors = validateStatementFetchDraft(draft, remoteConnectors, "schedule");
  const previewDisabledReason = firstError(previewErrors);
  const saveDisabledReason = firstError(scheduleErrors);

  return {
    canPreview: previewDisabledReason === null,
    canSave: saveDisabledReason === null,
    deleteBusyId,
    deleteError,
    deleteSchedule,
    draft,
    draftErrors,
    editSchedule,
    loadError,
    loading,
    newSchedule,
    preview,
    previewBusy,
    previewDisabledReason,
    previewError,
    previewFetch,
    profiles,
    refreshSchedules,
    remoteConnectors,
    runBusyId,
    runError,
    runResult,
    runSchedule,
    saveBusy,
    saveDisabledReason,
    saveError,
    saveMessage,
    saveSchedule,
    schedules,
    selectedKind,
    selectKind: setSelectedKind,
    updateDraft
  };
}

function createStatementFetchDraft(
  connectors: StatementConnectorDescriptor[],
  current?: StatementFetchDraft,
  schedule?: StatementFetchSchedule
): StatementFetchDraft {
  const connector = schedule
    ? connectors.find((candidate) => candidate.connectorId === schedule.connectorId)
    : connectors[0];
  const defaultSinceDate = new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString().slice(0, 10);
  return {
    cadenceHours: schedule ? String(schedule.cadenceHours) : "24",
    connectorId: schedule?.connectorId ?? connector?.connectorId ?? "",
    datasets: current?.datasets ?? "all",
    enabled: schedule?.enabled ?? true,
    externalAccountId: schedule?.externalAccountId ?? current?.externalAccountId ?? "",
    fundAccountId: schedule?.fundAccountId ?? current?.fundAccountId ?? "",
    mappingProfileId: schedule ? schedule.mappingProfileId ?? "" : connector?.defaultProfileId ?? "",
    scheduleId: schedule?.scheduleId ?? "",
    sinceDate: schedule?.lastRunAtUtc?.slice(0, 10) ?? current?.sinceDate ?? defaultSinceDate,
    sourceInstitution: schedule?.sourceInstitution ?? connector?.displayName ?? current?.sourceInstitution ?? "",
    sourceKind: schedule?.sourceKind ?? current?.sourceKind ?? "broker",
    toleranceProfileId: schedule?.toleranceProfileId ?? current?.toleranceProfileId ?? "statement-default"
  };
}

function currentConnectorName(
  draft: StatementFetchDraft,
  connectors: StatementConnectorDescriptor[]
): string {
  return connectors.find((candidate) => candidate.connectorId === draft.connectorId)?.displayName ?? "";
}

function toScheduleRequest(draft: StatementFetchDraft): StatementFetchScheduleUpsertRequest {
  return {
    scheduleId: draft.scheduleId.trim() || null,
    connectorId: draft.connectorId.trim(),
    externalAccountId: draft.externalAccountId.trim(),
    fundAccountId: draft.fundAccountId.trim(),
    sourceInstitution: draft.sourceInstitution.trim(),
    mappingProfileId: draft.mappingProfileId.trim() || null,
    toleranceProfileId: draft.toleranceProfileId.trim() || null,
    cadenceHours: Number(draft.cadenceHours),
    enabled: draft.enabled,
    sourceKind: draft.sourceKind
  };
}

function sortSchedules(schedules: StatementFetchSchedule[]): StatementFetchSchedule[] {
  return [...schedules].sort((left, right) => left.scheduleId.localeCompare(right.scheduleId));
}

function firstError(errors: StatementFetchDraftErrors): string | null {
  return Object.values(errors).find((value): value is string => Boolean(value)) ?? null;
}
