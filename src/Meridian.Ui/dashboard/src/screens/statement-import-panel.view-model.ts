import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  commitStatementImport,
  getStatementConnectors,
  listStatementMappingProfiles,
  previewStatementImport,
  upsertStatementMappingProfile
} from "@/lib/api";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import type {
  StatementColumnConfidence,
  StatementConnectorDescriptor,
  StatementImportCommitResult,
  StatementImportIssue,
  StatementImportPreview,
  StatementImportSourceKind,
  StatementKindSummary,
  StatementMappingProfile
} from "@/types";

export const STATEMENT_FORMAT_DRIFT_ISSUE_CODE = "FORMAT_DRIFT";

export interface StatementImportPanelServices {
  getConnectors: typeof getStatementConnectors;
  listMappingProfiles: typeof listStatementMappingProfiles;
  upsertMappingProfile: typeof upsertStatementMappingProfile;
  previewImport: typeof previewStatementImport;
  commitImport: typeof commitStatementImport;
}

const DEFAULT_SERVICES: StatementImportPanelServices = {
  getConnectors: getStatementConnectors,
  listMappingProfiles: listStatementMappingProfiles,
  upsertMappingProfile: upsertStatementMappingProfile,
  previewImport: previewStatementImport,
  commitImport: commitStatementImport
};

export interface StatementImportCommitFormState {
  sourceKind: StatementImportSourceKind;
  sourceInstitution: string;
  fundAccountId: string;
  externalAccountId: string;
  periodStart: string;
  periodEnd: string;
  toleranceProfileId: string;
}

export type StatementImportCommitFormField = keyof StatementImportCommitFormState;

export const DEFAULT_STATEMENT_IMPORT_COMMIT_FORM: StatementImportCommitFormState = {
  sourceKind: "broker",
  sourceInstitution: "",
  fundAccountId: "",
  externalAccountId: "",
  periodStart: "",
  periodEnd: "",
  toleranceProfileId: ""
};

export type StatementImportCommitFormErrors = Partial<Record<StatementImportCommitFormField | "file", string>>;

const ISO_DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

export function validateStatementImportCommitForm(
  form: StatementImportCommitFormState,
  file: File | null
): StatementImportCommitFormErrors {
  const errors: StatementImportCommitFormErrors = {};
  if (!file) {
    errors.file = "Select a statement file before committing the import.";
  }

  if (!form.sourceInstitution.trim()) {
    errors.sourceInstitution = "Source institution is required.";
  }

  if (!form.fundAccountId.trim()) {
    errors.fundAccountId = "Fund account is required.";
  }

  if (!form.externalAccountId.trim()) {
    errors.externalAccountId = "External account is required.";
  }

  if (!ISO_DATE_PATTERN.test(form.periodStart.trim())) {
    errors.periodStart = "Period start must be a YYYY-MM-DD date.";
  }

  if (!ISO_DATE_PATTERN.test(form.periodEnd.trim())) {
    errors.periodEnd = "Period end must be a YYYY-MM-DD date.";
  }

  if (!errors.periodStart && !errors.periodEnd && form.periodStart.trim() > form.periodEnd.trim()) {
    errors.periodEnd = "Period end must be on or after period start.";
  }

  return errors;
}

export interface StatementMappingProfileFieldDraft {
  canonicalField: string;
  sourceColumn: string;
  aliasesText: string;
  required: boolean;
}

export interface StatementMappingProfileActivityCodeDraft {
  sourceCode: string;
  canonicalActivityType: string;
}

export interface StatementMappingProfileDraft {
  schemaVersion: number;
  profileId: string;
  displayName: string;
  format: string;
  csvDelimiter: string;
  csvQuote: string;
  csvHasHeader: boolean;
  culture: string;
  dateFormatsText: string;
  fields: StatementMappingProfileFieldDraft[];
  activityCodes: StatementMappingProfileActivityCodeDraft[];
  notes: string;
  lastAcceptedFingerprint: string | null;
  /** Read-only: the draft mirrors a built-in profile and can only be cloned, not saved. */
  isBuiltIn: boolean;
  /** True when the draft was cloned from another profile and needs a fresh profile id. */
  isClone: boolean;
}

export function buildStatementMappingProfileDraft(
  profile: StatementMappingProfile,
  options: { clone?: boolean } = {}
): StatementMappingProfileDraft {
  const clone = options.clone === true;
  return {
    schemaVersion: profile.schemaVersion,
    profileId: clone ? `${profile.profileId}-copy` : profile.profileId,
    displayName: clone ? `${profile.displayName} (copy)` : profile.displayName,
    format: profile.format,
    csvDelimiter: profile.csv?.delimiter ?? ",",
    csvQuote: profile.csv?.quote ?? "\"",
    csvHasHeader: profile.csv?.hasHeader ?? true,
    culture: profile.culture ?? "",
    dateFormatsText: (profile.dateFormats ?? []).join(", "),
    fields: profile.fields.map((field) => ({
      canonicalField: field.canonicalField,
      sourceColumn: field.sourceColumn,
      aliasesText: (field.aliases ?? []).join(", "),
      required: field.required
    })),
    activityCodes: profile.activityCodes.map((code) => ({
      sourceCode: code.sourceCode,
      canonicalActivityType: code.canonicalActivityType
    })),
    notes: profile.notes ?? "",
    lastAcceptedFingerprint: clone ? null : profile.lastAcceptedFingerprint,
    isBuiltIn: clone ? false : profile.isBuiltIn,
    isClone: clone
  };
}

export function statementMappingProfileFromDraft(draft: StatementMappingProfileDraft): StatementMappingProfile {
  return {
    schemaVersion: draft.schemaVersion,
    profileId: draft.profileId.trim(),
    displayName: draft.displayName.trim(),
    format: draft.format.trim() || "csv",
    csv: draft.format.trim().toLowerCase() === "ofx"
      ? null
      : {
          delimiter: draft.csvDelimiter || ",",
          quote: draft.csvQuote || "\"",
          hasHeader: draft.csvHasHeader
        },
    culture: draft.culture.trim() || null,
    dateFormats: splitDraftList(draft.dateFormatsText),
    fields: draft.fields
      .filter((field) => field.canonicalField.trim() || field.sourceColumn.trim())
      .map((field) => ({
        canonicalField: field.canonicalField.trim(),
        sourceColumn: field.sourceColumn.trim(),
        aliases: splitDraftList(field.aliasesText),
        required: field.required
      })),
    activityCodes: draft.activityCodes
      .filter((code) => code.sourceCode.trim() || code.canonicalActivityType.trim())
      .map((code) => ({
        sourceCode: code.sourceCode.trim(),
        canonicalActivityType: code.canonicalActivityType.trim()
      })),
    lastAcceptedFingerprint: draft.lastAcceptedFingerprint,
    isBuiltIn: false,
    notes: draft.notes.trim() || null
  };
}

function splitDraftList(text: string): string[] | null {
  const values = text
    .split(/[,\n]/)
    .map((value) => value.trim())
    .filter(Boolean);
  return values.length > 0 ? values : null;
}

export type StatementColumnConfidenceBadgeVariant = "success" | "paper" | "warning" | "danger";

export function statementColumnConfidenceBadgeVariant(
  confidence: StatementColumnConfidence | string
): StatementColumnConfidenceBadgeVariant {
  switch (confidence) {
    case "Exact":
      return "success";
    case "Alias":
      return "paper";
    case "Fuzzy":
      return "warning";
    default:
      return "danger";
  }
}

export type StatementIssueBadgeVariant = "danger" | "warning" | "outline";

export function statementIssueBadgeVariant(severity: string): StatementIssueBadgeVariant {
  switch (severity) {
    case "Error":
      return "danger";
    case "Warning":
      return "warning";
    default:
      return "outline";
  }
}

export type StatementImportCommitOutcome = "committed" | "duplicate";

export interface StatementImportPanelViewModel {
  loading: boolean;
  loadError: ApiErrorDisplay | null;
  connectors: StatementConnectorDescriptor[];
  profiles: StatementMappingProfile[];
  selectedFile: File | null;
  selectedConnectorId: string;
  selectedProfileId: string;
  selectedConnector: StatementConnectorDescriptor | null;
  fileAccept: string;
  preview: StatementImportPreview | null;
  previewBusy: boolean;
  previewError: ApiErrorDisplay | null;
  issues: StatementImportIssue[];
  hasBlockingIssues: boolean;
  kindSummaries: StatementKindSummary[];
  selectedKind: string | null;
  selectedKindSummary: StatementKindSummary | null;
  selectKind: (kind: string) => void;
  selectFile: (file: File | null) => void;
  selectConnector: (connectorId: string) => void;
  selectProfile: (profileId: string) => void;
  applyProfileSuggestion: (profileId: string) => void;
  profileDraft: StatementMappingProfileDraft | null;
  profileDraftError: string | null;
  profileSaveBusy: boolean;
  profileSaveError: ApiErrorDisplay | null;
  profileSaveMessage: string | null;
  editProfile: (profileId: string) => void;
  cloneProfile: (profileId: string) => void;
  closeProfileEditor: () => void;
  updateProfileDraft: (patch: Partial<StatementMappingProfileDraft>) => void;
  addProfileDraftField: () => void;
  removeProfileDraftField: (index: number) => void;
  updateProfileDraftField: (index: number, patch: Partial<StatementMappingProfileFieldDraft>) => void;
  addProfileDraftActivityCode: () => void;
  removeProfileDraftActivityCode: (index: number) => void;
  updateProfileDraftActivityCode: (index: number, patch: Partial<StatementMappingProfileActivityCodeDraft>) => void;
  saveProfileDraft: () => Promise<void>;
  commitForm: StatementImportCommitFormState;
  commitFormErrors: StatementImportCommitFormErrors;
  updateCommitForm: (field: StatementImportCommitFormField, value: string) => void;
  commitBusy: boolean;
  commitError: ApiErrorDisplay | null;
  commitResult: StatementImportCommitResult | null;
  commitOutcome: StatementImportCommitOutcome | null;
  canCommit: boolean;
  commit: () => Promise<void>;
}

export interface UseStatementImportPanelOptions {
  services?: Partial<StatementImportPanelServices>;
}

export function useStatementImportPanelViewModel(
  options: UseStatementImportPanelOptions = {}
): StatementImportPanelViewModel {
  const optionsServices = options.services;
  const services: StatementImportPanelServices = useMemo(
    () => ({ ...DEFAULT_SERVICES, ...optionsServices }),
    [optionsServices]
  );

  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<ApiErrorDisplay | null>(null);
  const [connectors, setConnectors] = useState<StatementConnectorDescriptor[]>([]);
  const [profiles, setProfiles] = useState<StatementMappingProfile[]>([]);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [selectedConnectorId, setSelectedConnectorId] = useState("");
  const [selectedProfileId, setSelectedProfileId] = useState("");
  const [preview, setPreview] = useState<StatementImportPreview | null>(null);
  const [previewBusy, setPreviewBusy] = useState(false);
  const [previewError, setPreviewError] = useState<ApiErrorDisplay | null>(null);
  const [selectedKind, setSelectedKind] = useState<string | null>(null);
  const [profileDraft, setProfileDraft] = useState<StatementMappingProfileDraft | null>(null);
  const [profileDraftError, setProfileDraftError] = useState<string | null>(null);
  const [profileSaveBusy, setProfileSaveBusy] = useState(false);
  const [profileSaveError, setProfileSaveError] = useState<ApiErrorDisplay | null>(null);
  const [profileSaveMessage, setProfileSaveMessage] = useState<string | null>(null);
  const [commitForm, setCommitForm] = useState<StatementImportCommitFormState>(DEFAULT_STATEMENT_IMPORT_COMMIT_FORM);
  const [commitFormErrors, setCommitFormErrors] = useState<StatementImportCommitFormErrors>({});
  const [commitBusy, setCommitBusy] = useState(false);
  const [commitError, setCommitError] = useState<ApiErrorDisplay | null>(null);
  const [commitResult, setCommitResult] = useState<StatementImportCommitResult | null>(null);

  const previewRevisionRef = useRef(0);
  const commitFormRef = useRef(commitForm);
  commitFormRef.current = commitForm;
  const mountedRef = useRef(true);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
      previewRevisionRef.current += 1;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setLoadError(null);
    Promise.all([services.getConnectors(), services.listMappingProfiles()])
      .then(([nextConnectors, nextProfiles]) => {
        if (cancelled) {
          return;
        }

        setConnectors(nextConnectors);
        setProfiles(nextProfiles);
      })
      .catch((error) => {
        if (cancelled) {
          return;
        }

        setLoadError(describeApiError(error, "Statement connector library failed to load."));
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [services]);

  const runPreview = useCallback(async (args: { file: File | null; connectorId: string; profileId: string }) => {
    if (!args.file) {
      previewRevisionRef.current += 1;
      setPreview(null);
      setPreviewError(null);
      setPreviewBusy(false);
      setSelectedKind(null);
      return;
    }

    const revision = previewRevisionRef.current + 1;
    previewRevisionRef.current = revision;
    setPreviewBusy(true);
    setPreviewError(null);

    try {
      const next = await services.previewImport({
        file: args.file,
        connectorId: args.connectorId || undefined,
        mappingProfileId: args.profileId || undefined,
        externalAccountId: commitFormRef.current.externalAccountId.trim() || undefined
      });
      if (previewRevisionRef.current !== revision || !mountedRef.current) {
        return;
      }

      setPreview(next);
      setSelectedKind((current) => {
        if (current && next.kindSummaries.some((summary) => summary.kind === current)) {
          return current;
        }

        return next.kindSummaries[0]?.kind ?? null;
      });
    } catch (error) {
      if (previewRevisionRef.current !== revision || !mountedRef.current) {
        return;
      }

      setPreview(null);
      setPreviewError(describeApiError(error, "Statement import preview failed."));
    } finally {
      if (previewRevisionRef.current === revision && mountedRef.current) {
        setPreviewBusy(false);
      }
    }
  }, [services]);

  const selectFile = useCallback((file: File | null) => {
    setSelectedFile(file);
    setCommitResult(null);
    setCommitError(null);
    void runPreview({ file, connectorId: selectedConnectorId, profileId: selectedProfileId });
  }, [runPreview, selectedConnectorId, selectedProfileId]);

  const selectConnector = useCallback((connectorId: string) => {
    setSelectedConnectorId(connectorId);
    const connector = connectors.find((entry) => entry.connectorId === connectorId) ?? null;
    let nextProfileId = selectedProfileId;
    if (!nextProfileId && connector?.defaultProfileId) {
      nextProfileId = connector.defaultProfileId;
      setSelectedProfileId(nextProfileId);
    }

    void runPreview({ file: selectedFile, connectorId, profileId: nextProfileId });
  }, [connectors, runPreview, selectedFile, selectedProfileId]);

  const selectProfile = useCallback((profileId: string) => {
    setSelectedProfileId(profileId);
    void runPreview({ file: selectedFile, connectorId: selectedConnectorId, profileId });
  }, [runPreview, selectedConnectorId, selectedFile]);

  const applyProfileSuggestion = selectProfile;

  const editProfile = useCallback((profileId: string) => {
    const profile = profiles.find((entry) => entry.profileId === profileId);
    if (!profile) {
      return;
    }

    setProfileDraft(buildStatementMappingProfileDraft(profile));
    setProfileDraftError(null);
    setProfileSaveError(null);
    setProfileSaveMessage(null);
  }, [profiles]);

  const cloneProfile = useCallback((profileId: string) => {
    const profile = profiles.find((entry) => entry.profileId === profileId);
    if (!profile) {
      return;
    }

    setProfileDraft(buildStatementMappingProfileDraft(profile, { clone: true }));
    setProfileDraftError(null);
    setProfileSaveError(null);
    setProfileSaveMessage(null);
  }, [profiles]);

  const closeProfileEditor = useCallback(() => {
    setProfileDraft(null);
    setProfileDraftError(null);
    setProfileSaveError(null);
  }, []);

  const updateProfileDraft = useCallback((patch: Partial<StatementMappingProfileDraft>) => {
    setProfileDraft((current) => (current ? { ...current, ...patch } : current));
    setProfileDraftError(null);
    setProfileSaveMessage(null);
  }, []);

  const addProfileDraftField = useCallback(() => {
    setProfileDraft((current) => current
      ? {
          ...current,
          fields: [...current.fields, { canonicalField: "", sourceColumn: "", aliasesText: "", required: false }]
        }
      : current);
  }, []);

  const removeProfileDraftField = useCallback((index: number) => {
    setProfileDraft((current) => current
      ? { ...current, fields: current.fields.filter((_, i) => i !== index) }
      : current);
  }, []);

  const updateProfileDraftField = useCallback((index: number, patch: Partial<StatementMappingProfileFieldDraft>) => {
    setProfileDraft((current) => current
      ? {
          ...current,
          fields: current.fields.map((field, i) => (i === index ? { ...field, ...patch } : field))
        }
      : current);
  }, []);

  const addProfileDraftActivityCode = useCallback(() => {
    setProfileDraft((current) => current
      ? {
          ...current,
          activityCodes: [...current.activityCodes, { sourceCode: "", canonicalActivityType: "" }]
        }
      : current);
  }, []);

  const removeProfileDraftActivityCode = useCallback((index: number) => {
    setProfileDraft((current) => current
      ? { ...current, activityCodes: current.activityCodes.filter((_, i) => i !== index) }
      : current);
  }, []);

  const updateProfileDraftActivityCode = useCallback(
    (index: number, patch: Partial<StatementMappingProfileActivityCodeDraft>) => {
      setProfileDraft((current) => current
        ? {
            ...current,
            activityCodes: current.activityCodes.map((code, i) => (i === index ? { ...code, ...patch } : code))
          }
        : current);
    },
    []
  );

  const saveProfileDraft = useCallback(async () => {
    const draft = profileDraft;
    if (!draft || profileSaveBusy) {
      return;
    }

    if (draft.isBuiltIn) {
      setProfileDraftError("Built-in profiles are read-only. Clone the profile to edit a copy.");
      return;
    }

    if (!draft.profileId.trim()) {
      setProfileDraftError("Profile id is required.");
      return;
    }

    if (!draft.displayName.trim()) {
      setProfileDraftError("Display name is required.");
      return;
    }

    setProfileDraftError(null);
    setProfileSaveBusy(true);
    setProfileSaveError(null);
    setProfileSaveMessage(null);

    try {
      const saved = await services.upsertMappingProfile(statementMappingProfileFromDraft(draft));
      const nextProfiles = await services.listMappingProfiles();
      if (!mountedRef.current) {
        return;
      }

      setProfiles(nextProfiles);
      setProfileDraft(buildStatementMappingProfileDraft(saved));
      setProfileSaveMessage(`Saved mapping profile ${saved.displayName}.`);
      setSelectedProfileId(saved.profileId);
      void runPreview({ file: selectedFile, connectorId: selectedConnectorId, profileId: saved.profileId });
    } catch (error) {
      if (!mountedRef.current) {
        return;
      }

      setProfileSaveError(describeApiError(error, "Saving the mapping profile failed."));
    } finally {
      if (mountedRef.current) {
        setProfileSaveBusy(false);
      }
    }
  }, [profileDraft, profileSaveBusy, runPreview, selectedConnectorId, selectedFile, services]);

  const updateCommitForm = useCallback((field: StatementImportCommitFormField, value: string) => {
    setCommitForm((current) => ({ ...current, [field]: value }));
    setCommitFormErrors((current) => {
      if (!(field in current)) {
        return current;
      }

      const next = { ...current };
      delete next[field];
      return next;
    });
    setCommitResult(null);
    setCommitError(null);
  }, []);

  const commit = useCallback(async () => {
    if (commitBusy) {
      return;
    }

    const errors = validateStatementImportCommitForm(commitForm, selectedFile);
    setCommitFormErrors(errors);
    if (Object.keys(errors).length > 0) {
      setCommitError({
        summary: "Fix the highlighted fields before committing the statement import.",
        details: Object.values(errors)
      });
      return;
    }

    setCommitBusy(true);
    setCommitError(null);
    setCommitResult(null);

    try {
      const result = await services.commitImport({
        file: selectedFile!,
        connectorId: selectedConnectorId || undefined,
        mappingProfileId: selectedProfileId || undefined,
        externalAccountId: commitForm.externalAccountId.trim(),
        sourceKind: commitForm.sourceKind,
        sourceInstitution: commitForm.sourceInstitution.trim(),
        fundAccountId: commitForm.fundAccountId.trim(),
        periodStart: commitForm.periodStart.trim(),
        periodEnd: commitForm.periodEnd.trim(),
        toleranceProfileId: commitForm.toleranceProfileId.trim() || undefined
      });
      if (!mountedRef.current) {
        return;
      }

      setCommitResult(result);
    } catch (error) {
      if (!mountedRef.current) {
        return;
      }

      setCommitError(describeApiError(error, "Statement import commit failed."));
    } finally {
      if (mountedRef.current) {
        setCommitBusy(false);
      }
    }
  }, [commitBusy, commitForm, selectedConnectorId, selectedFile, selectedProfileId, services]);

  const selectedConnector = useMemo(
    () => connectors.find((entry) => entry.connectorId === selectedConnectorId) ?? null,
    [connectors, selectedConnectorId]
  );

  const fileAccept = useMemo(() => {
    const extensions = selectedConnector
      ? selectedConnector.fileExtensions
      : connectors.flatMap((connector) => connector.fileExtensions);
    const normalized = Array.from(new Set(
      extensions
        .map((extension) => extension.trim())
        .filter(Boolean)
        .map((extension) => (extension.startsWith(".") ? extension : `.${extension}`))
    ));
    return normalized.length > 0 ? normalized.join(",") : "*";
  }, [connectors, selectedConnector]);

  const issues = preview?.issues ?? [];
  const hasBlockingIssues = issues.some((issue) => issue.severity === "Error");
  const kindSummaries = preview?.kindSummaries ?? [];
  const selectedKindSummary = useMemo(
    () => kindSummaries.find((summary) => summary.kind === selectedKind) ?? null,
    [kindSummaries, selectedKind]
  );

  const commitOutcome: StatementImportCommitOutcome | null = commitResult
    ? (commitResult.duplicate ? "duplicate" : "committed")
    : null;

  return {
    loading,
    loadError,
    connectors,
    profiles,
    selectedFile,
    selectedConnectorId,
    selectedProfileId,
    selectedConnector,
    fileAccept,
    preview,
    previewBusy,
    previewError,
    issues,
    hasBlockingIssues,
    kindSummaries,
    selectedKind,
    selectedKindSummary,
    selectKind: setSelectedKind,
    selectFile,
    selectConnector,
    selectProfile,
    applyProfileSuggestion,
    profileDraft,
    profileDraftError,
    profileSaveBusy,
    profileSaveError,
    profileSaveMessage,
    editProfile,
    cloneProfile,
    closeProfileEditor,
    updateProfileDraft,
    addProfileDraftField,
    removeProfileDraftField,
    updateProfileDraftField,
    addProfileDraftActivityCode,
    removeProfileDraftActivityCode,
    updateProfileDraftActivityCode,
    saveProfileDraft,
    commitForm,
    commitFormErrors,
    updateCommitForm,
    commitBusy,
    commitError,
    commitResult,
    commitOutcome,
    canCommit: Boolean(selectedFile) && !commitBusy && !previewBusy,
    commit
  };
}
