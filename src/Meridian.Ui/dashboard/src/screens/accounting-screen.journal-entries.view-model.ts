import { useCallback, useEffect, useMemo, useState } from "react";
import {
  applyManualJournalEntryLifecycleAction,
  attachManualJournalEntryEvidence,
  getManualJournalEntryWorkbench,
  saveManualJournalEntryDraft,
  searchSecurities,
  submitManualJournalEntryApproval,
  validateManualJournalEntryDraft,
  type ManualJournalEntryWorkbenchQuery,
} from "@/lib/api";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import {
  formatCount,
  formatCurrency,
  formatCurrencyWithCode,
  formatDateTimeLabel,
} from "./accounting-screen.formatting";
import {
  DEFAULT_ACCOUNTING_BASIS,
  buildPrivateCapitalFundEventCommandCenterRoute,
  manualJournalPrivateCapitalReadinessTone,
  normalizeQueryValue,
} from "./accounting-screen.view-model.shared";
import type {
  AccountingConfigurationIssueViewModel,
  AccountingToolingTone,
  ManualJournalBalanceImpactRowViewModel,
  ManualJournalEvidenceAttachmentDraft,
  ManualJournalEntryWorkbenchServices,
  ManualJournalEntryWorkbenchViewModel,
  ManualJournalLifecycleChecklistItemViewModel,
  ManualJournalLifecycleCommandViewModel,
  ManualJournalLifecycleCorrectionViewModel,
  ManualJournalLifecycleTransitionViewModel,
  ManualJournalLineValidationBadge,
  ManualJournalPaymentIntentWorkflowRowViewModel,
  ManualJournalPrivateCapitalAccountRowViewModel,
  ManualJournalPrivateCapitalActivityViewModel,
  ManualJournalPrivateCapitalCapitalAccountSubledgerRowViewModel,
  ManualJournalPrivateCapitalEvidenceCategoryViewModel,
  ManualJournalPrivateCapitalFundEventLedgerRecordViewModel,
  ManualJournalPrivateCapitalFundEventRowViewModel,
  ManualJournalPrivateCapitalLedgerImpactRowViewModel,
  ManualJournalPrivateCapitalReportOutputRowViewModel,
  ManualJournalPrivateCapitalSubledgerEntryRowViewModel,
} from "./accounting-screen.view-model";
import type {
  AccountingTemplateLineSide,
  ChartOfAccountsNode,
  JournalEntryLifecycleAction,
  JournalEntryLifecycleActionResult,
  JournalEntryLifecycleTransition,
  ManualJournalEntryDraft,
  ManualJournalEntryEvidenceAttachment,
  ManualJournalEntryLine,
  ManualJournalEntryWorkbench,
  PaymentIntentWorkflow,
  PrivateCapitalActivityProjection,
  PrivateCapitalCapitalAccountActivity,
  PrivateCapitalCapitalAccountSubledger,
  PrivateCapitalCapitalAccountSubledgerEntry,
  PrivateCapitalEvidenceCategory,
  PrivateCapitalFundEvent,
  PrivateCapitalFundEventLedgerRecord,
  PrivateCapitalLedgerImpact,
  PrivateCapitalPaymentIntentEvidence,
  PrivateCapitalReportOutput,
  SecurityMasterEntry,
} from "@/types";

const defaultManualJournalEntryWorkbenchServices: ManualJournalEntryWorkbenchServices = {
  getWorkbench: (query) => getManualJournalEntryWorkbench(query),
  searchSecurities: (query) => searchSecurities(query, 8, true),
  saveDraft: (request) => saveManualJournalEntryDraft(request),
  validateDraft: (request) => validateManualJournalEntryDraft(request),
  submitApproval: (request) => submitManualJournalEntryApproval(request),
  attachEvidence: (request) => attachManualJournalEntryEvidence(request),
  applyLifecycleAction: (request) => applyManualJournalEntryLifecycleAction(request)
};

export function useManualJournalEntryWorkbenchViewModel(
  active: boolean,
  services: ManualJournalEntryWorkbenchServices = defaultManualJournalEntryWorkbenchServices,
  search = ""
): ManualJournalEntryWorkbenchViewModel {
  const [workbench, setWorkbench] = useState<ManualJournalEntryWorkbench | null>(null);
  const [draft, setDraft] = useState<ManualJournalEntryDraft>(() => createManualJournalEntryDraft());
  const [selectedLineId, setSelectedLineId] = useState(draft.lines[0]?.lineId ?? "line-1");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ApiErrorDisplay | null>(null);
  const [saveBusy, setSaveBusy] = useState(false);
  const [validateBusy, setValidateBusy] = useState(false);
  const [submitBusy, setSubmitBusy] = useState(false);
  const [attachEvidenceBusy, setAttachEvidenceBusy] = useState(false);
  const [attachEvidenceStatusText, setAttachEvidenceStatusText] = useState<string | null>(null);
  const [securitySearchQuery, setSecuritySearchQuery] = useState("");
  const [securitySearchResults, setSecuritySearchResults] = useState<SecurityMasterEntry[]>([]);
  const [securitySearchBusy, setSecuritySearchBusy] = useState(false);
  const [securitySearchError, setSecuritySearchError] = useState<ApiErrorDisplay | null>(null);
  const [attachmentDraft, setAttachmentDraft] = useState<ManualJournalEvidenceAttachmentDraft>(() => createManualJournalAttachmentDraft());
  const [lifecycleBusyAction, setLifecycleBusyAction] = useState<JournalEntryLifecycleAction | null>(null);
  const [lifecycleStatusText, setLifecycleStatusText] = useState<string | null>(null);
  const [lifecycleCorrectionDrafts, setLifecycleCorrectionDrafts] = useState<ManualJournalEntryDraft[]>([]);
  const query = useMemo(() => parseManualJournalEntryWorkbenchQuery(search), [search]);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const next = await services.getWorkbench(query);
      setWorkbench(next);
      const selected = ensureManualJournalDraftScope(
        next.drafts[0] ?? createManualJournalEntryDraft(next.fundProfileId || query.fundProfileId || "default-fund", next.ledgerBookId ?? query.ledgerBookId ?? null),
        next,
        query
      );
      setDraft(selected);
      setSelectedLineId(selected.lines[0]?.lineId ?? "line-1");
    } catch (err) {
      setError(describeApiError(err, "Manual journal entry workbench could not load."));
    } finally {
      setLoading(false);
    }
  }, [query, services]);

  useEffect(() => {
    if (!active) {
      return;
    }

    void refresh();
  }, [active, refresh]);

  const accountOptions = useMemo(
    () => (workbench?.chartOfAccounts ?? [])
      .filter((account) => !account.isArchived)
      .map((account) => ({
        value: account.path,
        label: `${account.path} - ${account.accountName}`
      })),
    [workbench]
  );

  const applyServerDraft = useCallback((next: ManualJournalEntryDraft) => {
    setDraft(next);
    setSelectedLineId(next.lines[0]?.lineId ?? selectedLineId);
    setWorkbench((current) => current
      ? {
        ...current,
        drafts: [next, ...current.drafts.filter((item) => item.journalEntryId !== next.journalEntryId)]
      }
      : current);
  }, [selectedLineId]);

  const applyLifecycleResult = useCallback((result: JournalEntryLifecycleActionResult) => {
    const generated = result.generatedJournalEntries ?? [];
    setDraft(result.journalEntry);
    setSelectedLineId(result.journalEntry.lines[0]?.lineId ?? selectedLineId);
    setLifecycleCorrectionDrafts(generated);
    setLifecycleStatusText(`${result.transition.action} recorded: ${result.transition.fromStatus} -> ${result.transition.toStatus}`);
    setWorkbench((current) => {
      if (!current) {
        return current;
      }

      const nextDrafts = [result.journalEntry, ...generated];
      const nextIds = new Set(nextDrafts.map((item) => item.journalEntryId));
      return {
        ...current,
        drafts: [...nextDrafts, ...current.drafts.filter((item) => !nextIds.has(item.journalEntryId))]
      };
    });
  }, [selectedLineId]);

  const updateHeader = useCallback<ManualJournalEntryWorkbenchViewModel["updateHeader"]>((field, value) => {
    setDraft((current) => ({ ...current, [field]: value }));
  }, []);

  const updateLine = useCallback<ManualJournalEntryWorkbenchViewModel["updateLine"]>((lineId, patch) => {
    setDraft((current) => ({
      ...withManualJournalTotals({
        ...current,
        lines: current.lines.map((line) => line.lineId === lineId ? { ...line, ...patch } : line)
      })
    }));
  }, []);

  const searchSecurityMaster = useCallback(async () => {
    const query = securitySearchQuery.trim();
    if (query.length < 2) {
      setSecuritySearchResults([]);
      setSecuritySearchError(null);
      return;
    }

    setSecuritySearchBusy(true);
    setSecuritySearchError(null);
    try {
      setSecuritySearchResults(await services.searchSecurities(query));
    } catch (err) {
      setSecuritySearchError(describeApiError(err, "Security Master search failed."));
      setSecuritySearchResults([]);
    } finally {
      setSecuritySearchBusy(false);
    }
  }, [securitySearchQuery, services]);

  const selectSecurity = useCallback<ManualJournalEntryWorkbenchViewModel["selectSecurity"]>((lineId, security) => {
    updateLine(lineId, {
      securityId: security.securityId,
      securityDisplayName: security.displayName
    });
    setSecuritySearchQuery(security.displayName);
    setSecuritySearchResults([]);
  }, [updateLine]);

  const clearSecurity = useCallback<ManualJournalEntryWorkbenchViewModel["clearSecurity"]>((lineId) => {
    updateLine(lineId, {
      securityId: null,
      securityDisplayName: null
    });
  }, [updateLine]);

  const addLine = useCallback((side: AccountingTemplateLineSide) => {
    const line = createManualJournalEntryLine(side, draft.currency, accountOptions[0]?.value ?? "");
    setDraft((current) => withManualJournalTotals({ ...current, lines: [...current.lines, line] }));
    setSelectedLineId(line.lineId);
  }, [accountOptions, draft.currency]);

  const removeLine = useCallback<ManualJournalEntryWorkbenchViewModel["removeLine"]>((lineId) => {
    setDraft((current) => {
      if (current.lines.length <= 2) {
        return current;
      }

      const nextLines = current.lines.filter((line) => line.lineId !== lineId);
      if (selectedLineId === lineId) {
        setSelectedLineId(nextLines[0]?.lineId ?? "line-1");
      }

      return withManualJournalTotals({ ...current, lines: nextLines });
    });
  }, [selectedLineId]);

  const updateAttachmentDraft = useCallback<ManualJournalEntryWorkbenchViewModel["updateAttachmentDraft"]>((patch) => {
    setAttachmentDraft((current) => ({ ...current, ...patch }));
  }, []);

  const addAttachment = useCallback(async () => {
    const displayName = attachmentDraft.displayName.trim();
    const uri = attachmentDraft.uri.trim();
    if (!displayName || !uri) {
      return;
    }

    const draftForAttachment = withManualJournalTotals(draft);
    const attachment: ManualJournalEntryEvidenceAttachment = {
      attachmentId: newClientId(),
      displayName,
      uri,
      evidenceKind: attachmentDraft.evidenceKind.trim() || "SourceDocument",
      sourceSystem: attachmentDraft.sourceSystem.trim() || "ManualUpload",
      addedAtUtc: new Date().toISOString(),
      addedBy: "browser-user",
      lineId: attachmentDraft.lineId,
      description: attachmentDraft.description.trim() || null
    };
    setAttachEvidenceBusy(true);
    setAttachEvidenceStatusText(null);
    setError(null);
    try {
      applyServerDraft(await services.attachEvidence({
        journalEntryId: draftForAttachment.journalEntryId,
        fundProfileId: draftForAttachment.fundProfileId,
        actor: "browser-user",
        version: draftForAttachment.version,
        attachment,
        correlationId: `manual-je-attach-evidence-${attachment.attachmentId}`,
        evidenceLinks: [uri],
        actionOrigin: "HumanOperator",
        periodIsLocked: draftForAttachment.status === "CloseLocked",
        ledgerBookId: draftForAttachment.ledgerBookId ?? query.ledgerBookId ?? null
      }));
      setAttachmentDraft(createManualJournalAttachmentDraft());
      setAttachEvidenceStatusText(`Evidence attached: ${displayName}.`);
    } catch (err) {
      const errorDisplay = describeApiError(err, "Manual journal evidence could not be attached.");
      setError(errorDisplay);
      setAttachEvidenceStatusText(errorDisplay.summary);
    } finally {
      setAttachEvidenceBusy(false);
    }
  }, [applyServerDraft, attachmentDraft, draft, query.ledgerBookId, services]);

  const removeAttachment = useCallback<ManualJournalEntryWorkbenchViewModel["removeAttachment"]>((attachmentId) => {
    setDraft((current) => {
      const nextAttachments = (current.evidenceAttachments ?? []).filter((item) => item.attachmentId !== attachmentId);
      const retainedUris = new Set(nextAttachments.map((item) => item.uri));
      return {
        ...current,
        evidenceAttachments: nextAttachments,
        evidenceLinks: current.evidenceLinks.filter((link) => retainedUris.has(link) || !(current.evidenceAttachments ?? []).some((item) => item.uri === link))
      };
    });
  }, []);

  const selectDraft = useCallback((journalEntryId: string) => {
    const selected = workbench?.drafts.find((item) => item.journalEntryId === journalEntryId);
    if (!selected) {
      return;
    }

    setDraft(selected);
    setSelectedLineId(selected.lines[0]?.lineId ?? selectedLineId);
  }, [selectedLineId, workbench?.drafts]);

  const validate = useCallback(async () => {
    setValidateBusy(true);
    setError(null);
    const draftForValidation = withManualJournalTotals(draft);
    try {
      applyServerDraft(await services.validateDraft({
        draft: draftForValidation,
        actor: "browser-user",
        correlationId: "manual-je-validate",
        ledgerBookId: draftForValidation.ledgerBookId ?? query.ledgerBookId ?? null
      }));
    } catch (err) {
      setError(describeApiError(err, "Manual journal entry validation failed."));
    } finally {
      setValidateBusy(false);
    }
  }, [applyServerDraft, draft, query.ledgerBookId, services]);

  const save = useCallback(async () => {
    setSaveBusy(true);
    setError(null);
    const draftForSave = withManualJournalTotals(draft);
    try {
      applyServerDraft(await services.saveDraft({
        draft: draftForSave,
        actor: "browser-user",
        correlationId: "manual-je-save",
        ledgerBookId: draftForSave.ledgerBookId ?? query.ledgerBookId ?? null
      }));
    } catch (err) {
      setError(describeApiError(err, "Manual journal entry draft could not be saved."));
    } finally {
      setSaveBusy(false);
    }
  }, [applyServerDraft, draft, query.ledgerBookId, services]);

  const submit = useCallback(async () => {
    setSubmitBusy(true);
    setError(null);
    const draftForSubmit = withManualJournalTotals(draft);
    try {
      applyServerDraft(await services.submitApproval({
        journalEntryId: draftForSubmit.journalEntryId,
        fundProfileId: draftForSubmit.fundProfileId,
        actor: "browser-user",
        version: draftForSubmit.version,
        correlationId: "manual-je-submit",
        ledgerBookId: draftForSubmit.ledgerBookId ?? query.ledgerBookId ?? null
      }));
    } catch (err) {
      setError(describeApiError(err, "Manual journal entry could not be submitted for approval."));
    } finally {
      setSubmitBusy(false);
    }
  }, [applyServerDraft, draft, query.ledgerBookId, services]);

  const applyLifecycleAction = useCallback<ManualJournalEntryWorkbenchViewModel["applyLifecycleAction"]>(async (action) => {
    if (lifecycleBusyAction) {
      return;
    }

    const draftForAction = withManualJournalTotals(draft);
    setLifecycleBusyAction(action);
    setLifecycleStatusText(null);
    setError(null);
    try {
      applyLifecycleResult(await services.applyLifecycleAction({
        journalEntryId: draftForAction.journalEntryId,
        fundProfileId: draftForAction.fundProfileId,
        action,
        actor: "browser-user",
        version: draftForAction.version,
        notes: lifecycleActionNotes(action, draftForAction),
        correlationId: `manual-je-${action.toLowerCase()}`,
        evidenceLinks: draftForAction.evidenceLinks ?? [],
        actionOrigin: "HumanOperator",
        periodIsLocked: action === "LockAfterClose",
        rebookLines: action === "Rebook" ? draftForAction.lines : [],
        ledgerBookId: draftForAction.ledgerBookId ?? query.ledgerBookId ?? null
      }));
    } catch (err) {
      const errorDisplay = describeApiError(err, `Manual journal entry lifecycle action ${action} failed.`);
      setError(errorDisplay);
      setLifecycleStatusText(errorDisplay.summary);
    } finally {
      setLifecycleBusyAction(null);
    }
  }, [applyLifecycleResult, draft, lifecycleBusyAction, query.ledgerBookId, services]);

  const validationIssues = draft.validationIssues.map<AccountingConfigurationIssueViewModel>((issue, index) => ({
    id: `${issue.code}-${index}`,
    label: issue.code,
    message: issue.message,
    detail: issue.suggestedAction ?? issue.targetId ?? "Review the journal entry.",
    tone: issue.severity === "Critical" ? "danger" : issue.severity === "Warning" ? "warning" : "default"
  }));
  const getLineBadges = useCallback<ManualJournalEntryWorkbenchViewModel["getLineBadges"]>((lineId) => {
    const line = draft.lines.find((item) => item.lineId === lineId);
    const serverIssues = draft.validationIssues
      .filter((issue) => issue.targetId === lineId)
      .map<ManualJournalLineValidationBadge>((issue) => ({
        id: `${lineId}-${issue.code}`,
        label: issue.severity === "Critical" ? "Blocked" : issue.severity,
        message: issue.message,
        tone: issue.severity === "Critical" ? "danger" : issue.severity === "Warning" ? "warning" : "default"
      }));
    if (!line) {
      return serverIssues;
    }

    const localBadges: ManualJournalLineValidationBadge[] = [];
    if (!line.accountPath) {
      localBadges.push({ id: `${lineId}-account-local`, label: "GL missing", message: "Select a GL account.", tone: "warning" });
    }
    if (line.amount <= 0) {
      localBadges.push({ id: `${lineId}-amount-local`, label: "Amount", message: "Enter a positive amount.", tone: "warning" });
    }
    if (line.securityId) {
      localBadges.push({ id: `${lineId}-security-local`, label: "Security", message: line.securityDisplayName ?? line.securityId, tone: "success" });
    }
    if ((draft.evidenceAttachments ?? []).some((item) => item.lineId === lineId) || line.evidenceLink) {
      localBadges.push({ id: `${lineId}-evidence-local`, label: "Evidence", message: "Line support is linked.", tone: "success" });
    }

    return [...serverIssues, ...localBadges];
  }, [draft.evidenceAttachments, draft.lines, draft.validationIssues]);
  const balancedDraft = withManualJournalTotals(draft);
  const balanceImpactRows = buildManualJournalBalanceImpactRows(balancedDraft, workbench?.chartOfAccounts ?? []);
  const hasEvidence = (balancedDraft.evidenceLinks?.length ?? 0) > 0 || (balancedDraft.evidenceAttachments?.length ?? 0) > 0;
  const canSubmit = balancedDraft.validationIssues.every((issue) => issue.severity !== "Critical") && Math.abs(balancedDraft.imbalance) === 0 && balancedDraft.lines.length >= 2 && hasEvidence;
  const balanceStatusLabel = Math.abs(balancedDraft.imbalance) === 0 ? "Balanced" : "Out by " + formatCurrency(Math.abs(balancedDraft.imbalance));
  const treasuryContextLabel = formatManualJournalTreasuryContext(draft);
  const privateCapitalActivity = useMemo(
    () => buildManualJournalPrivateCapitalActivityView(workbench?.privateCapitalActivity ?? null),
    [workbench?.privateCapitalActivity]
  );
  const lifecycleCommands = buildManualJournalLifecycleCommands(balancedDraft, lifecycleBusyAction);
  const lifecycleTransitions = (balancedDraft.lifecycleTransitions ?? []).slice().reverse().map(formatManualJournalLifecycleTransition);
  const lifecycleCorrectionRows = lifecycleCorrectionDrafts.map(formatManualJournalLifecycleCorrection);
  const lifecycleChecklist = buildManualJournalLifecycleChecklist(
    balancedDraft,
    lifecycleCommands,
    lifecycleTransitions,
    lifecycleCorrectionRows
  );
  const securitySearchStatusText = securitySearchBusy
    ? "Searching Security Master."
    : securitySearchError?.summary ?? (securitySearchResults.length > 0
      ? `${securitySearchResults.length} Security Master matches.`
      : securitySearchQuery.trim().length >= 2
        ? "No Security Master matches loaded."
        : "Enter at least two characters to search Security Master.");

  return {
    title: "Manual journal entry workbench",
    description: "Author controller-owned journal entries with GL account picks, line-level Security Master attribution, balancing validation, draft save, and approval submission.",
    loading,
    errorText: error?.summary ?? null,
    statusLabel: `${draft.status} v${draft.version}`,
    draft: balancedDraft,
    drafts: workbench?.drafts ?? [],
    accountOptions,
    selectedLineId,
    securitySearchQuery,
    securitySearchResults,
    securitySearchBusy,
    securitySearchErrorText: securitySearchError?.summary ?? null,
    securitySearchStatusText,
    attachmentDraft,
    totalsLabel: `Debits ${formatCurrency(balancedDraft.totalDebits)} / Credits ${formatCurrency(balancedDraft.totalCredits)}`,
    totalDebitsLabel: formatCurrency(balancedDraft.totalDebits),
    totalCreditsLabel: formatCurrency(balancedDraft.totalCredits),
    imbalanceLabel: `Imbalance ${formatCurrency(balancedDraft.imbalance)}`,
    balanceStatusLabel,
    balanceStatusTone: Math.abs(balancedDraft.imbalance) === 0 ? "success" : "warning",
    balanceImpactRows,
    treasuryContextLabel,
    privateCapitalActivity,
    validationIssues,
    lifecycleCommands,
    lifecycleChecklist,
    lifecycleTransitions,
    lifecycleCorrectionRows,
    lifecycleStatusText,
    lifecycleBusyAction,
    saveBusy,
    validateBusy,
    submitBusy,
    attachEvidenceBusy,
    attachEvidenceStatusText,
    canSubmit,
    submitDisabledReason: canSubmit ? null : "Resolve critical validation issues, balance debits to credits, and attach source evidence before approval submission.",
    refresh,
    updateHeader,
    selectDraft,
    selectLine: setSelectedLineId,
    updateLine,
    getLineBadges,
    updateSecuritySearchQuery: setSecuritySearchQuery,
    searchSecurityMaster,
    selectSecurity,
    clearSecurity,
    addLine,
    removeLine,
    updateAttachmentDraft,
    addAttachment,
    removeAttachment,
    applyLifecycleAction,
    save,
    validate,
    submit
  };
}

function parseManualJournalEntryWorkbenchQuery(search: string): ManualJournalEntryWorkbenchQuery {
  const params = new URLSearchParams(search.startsWith("?") ? search.slice(1) : search);
  return {
    fundProfileId: normalizeQueryValue(params.get("fundProfileId")),
    ledgerBookId: normalizeQueryValue(params.get("ledgerBookId"))
  };
}

function ensureManualJournalDraftScope(
  draft: ManualJournalEntryDraft,
  workbench: ManualJournalEntryWorkbench,
  query: ManualJournalEntryWorkbenchQuery
): ManualJournalEntryDraft {
  const fundProfileId = draft.fundProfileId || workbench.fundProfileId || query.fundProfileId || "default-fund";
  const ledgerBookId = draft.ledgerBookId ?? workbench.ledgerBookId ?? query.ledgerBookId ?? null;
  if (fundProfileId === draft.fundProfileId && ledgerBookId === draft.ledgerBookId) {
    return draft;
  }

  return {
    ...draft,
    fundProfileId,
    ledgerBookId
  };
}

function buildManualJournalPrivateCapitalActivityView(
  activity: PrivateCapitalActivityProjection | null
): ManualJournalPrivateCapitalActivityViewModel {
  if (!activity) {
    return {
      title: "Private-capital activity",
      statusLabel: "No activity loaded",
      projectedAtLabel: "Not loaded",
      emptyText: "No private-capital fund events are retained in the manual JE workbench yet.",
      summaryCards: [
        { id: "fund-events", label: "Fund events", value: "0", detail: "No retained fund-event rows", tone: "default" },
        { id: "capital-accounts", label: "Capital accounts", value: "0", detail: "No capital-account activity", tone: "default" },
        { id: "ledger-impacts", label: "Ledger impacts", value: "0", detail: "No GL impact rows", tone: "default" },
        { id: "report-outputs", label: "Report outputs", value: "0", detail: "No package candidates", tone: "default" },
        { id: "payment-intents", label: "Payment intents", value: "0", detail: "No pre-execution cash workflow", tone: "default" },
        { id: "net-activity", label: "Net activity", value: "$0", detail: "No balance movement", tone: "default" },
        { id: "projection-issues", label: "Data quality issues", value: "0", detail: "No workbench warnings", tone: "success" }
      ],
      fundEvents: [],
      capitalAccounts: [],
      capitalAccountSubledgers: [],
      capitalAccountSubledgerEntries: [],
      ledgerImpacts: [],
      reportOutputs: [],
      fundEventLedgerRecords: [],
      paymentIntents: [],
      validationIssues: []
    };
  }

  const currency = activity.currency || "USD";
  const fundEvents = activity.fundEvents ?? [];
  const capitalAccounts = activity.capitalAccounts ?? [];
  const capitalAccountSubledgers = activity.capitalAccountSubledgers ?? [];
  const capitalAccountSubledgerEntries = activity.capitalAccountSubledgerEntries ?? [];
  const ledgerImpacts = activity.ledgerImpacts ?? [];
  const reportOutputs = activity.reportOutputs ?? [];
  const fundEventLedgerRecords = activity.fundEventRecords ?? [];
  const paymentIntents = activity.paymentIntents ?? [];
  const fundEventLedgerRecordCount = fundEventLedgerRecords.length;
  const deferredPaymentIntentCount = paymentIntents.filter((item) => item.status === "ExecutionDeferred").length;
  const blockedPaymentIntentCount = paymentIntents.filter((item) => item.status === "Blocked" || item.status === "BankReturned").length;
  const postedFundEventCount = activity.postedFundEventCount ?? 0;
  const publishedReportOutputCount = activity.publishedReportOutputCount ?? 0;
  const validationIssues = (activity.validationIssues ?? []).map<AccountingConfigurationIssueViewModel>((issue, index) => ({
    id: `${issue.code}-${index}`,
    label: issue.code,
    message: issue.message,
    detail: issue.suggestedAction ?? issue.targetId ?? "Review private-capital context.",
    tone: issue.severity === "Critical" ? "danger" : issue.severity === "Warning" ? "warning" : "default"
  }));

  return {
    title: "Private-capital activity",
    statusLabel: `${activity.fundEventCount} fund events / ${activity.capitalAccountCount} capital accounts`,
    projectedAtLabel: formatDateTimeLabel(activity.projectedAtUtc),
    emptyText: "No private-capital fund events are retained in the manual JE workbench yet.",
    summaryCards: [
      {
        id: "fund-events",
        label: "Fund events",
        value: activity.fundEventCount.toLocaleString(),
        detail: `${postedFundEventCount.toLocaleString()} posted / ${activity.submittedFundEventCount.toLocaleString()} submitted or approved`,
        tone: activity.fundEventCount > 0 ? "success" : "default"
      },
      {
        id: "posted-fund-events",
        label: "Posted events",
        value: postedFundEventCount.toLocaleString(),
        detail: "Ledger-backed fund-event rows",
        tone: postedFundEventCount > 0 ? "success" : "default"
      },
      {
        id: "fund-event-ledger-records",
        label: "Event ledger records",
        value: fundEventLedgerRecordCount.toLocaleString(),
        detail: "Event, subledger, GL, evidence, approval, and report output",
        tone: fundEventLedgerRecordCount === activity.fundEventCount && activity.fundEventCount > 0 ? "success" : fundEventLedgerRecordCount > 0 ? "warning" : "default"
      },
      {
        id: "capital-accounts",
        label: "Capital accounts",
        value: activity.capitalAccountCount.toLocaleString(),
        detail: `${activity.approvalQueueCount.toLocaleString()} pending approval`,
        tone: activity.approvalQueueCount > 0 ? "warning" : activity.capitalAccountCount > 0 ? "success" : "default"
      },
      {
        id: "capital-account-subledger",
        label: "Subledger rows",
        value: capitalAccountSubledgerEntries.length.toLocaleString(),
        detail: `${capitalAccountSubledgers.length.toLocaleString()} account-level subledger(s)`,
        tone: capitalAccountSubledgerEntries.length > 0 ? "success" : "default"
      },
      {
        id: "report-outputs",
        label: "Report outputs",
        value: reportOutputs.length.toLocaleString(),
        detail: `${publishedReportOutputCount.toLocaleString()} published / ${reportOutputs.filter((item) => item.isReportReady).length.toLocaleString()} ready`,
        tone: reportOutputs.some((item) => !item.isReportReady) ? "warning" : reportOutputs.length > 0 ? "success" : "default"
      },
      {
        id: "payment-intents",
        label: "Payment intents",
        value: paymentIntents.length.toLocaleString(),
        detail: `${deferredPaymentIntentCount.toLocaleString()} execution-deferred / ${blockedPaymentIntentCount.toLocaleString()} blocked or returned`,
        tone: blockedPaymentIntentCount > 0 ? "danger" : paymentIntents.some((item) => item.status !== "ExecutionDeferred") ? "warning" : paymentIntents.length > 0 ? "success" : "default"
      },
      {
        id: "published-report-outputs",
        label: "Published outputs",
        value: publishedReportOutputCount.toLocaleString(),
        detail: "Governed report-pack outputs",
        tone: publishedReportOutputCount > 0 ? "success" : "default"
      },
      {
        id: "ledger-impacts",
        label: "Ledger impacts",
        value: ledgerImpacts.length.toLocaleString(),
        detail: `${ledgerImpacts.filter((item) => item.isPostingReady).length.toLocaleString()} posting-ready`,
        tone: ledgerImpacts.some((item) => !item.isPostingReady) ? "warning" : ledgerImpacts.length > 0 ? "success" : "default"
      },
      {
        id: "net-activity",
        label: "Net activity",
        value: formatCurrencyWithCode(activity.netCapitalActivity, currency, true),
        detail: "Ledger-derived capital activity",
        tone: activity.netCapitalActivity < 0 ? "warning" : activity.netCapitalActivity > 0 ? "success" : "default"
      },
      {
        id: "projection-issues",
        label: "Data quality issues",
        value: validationIssues.length.toLocaleString(),
        detail: validationIssues.length > 0 ? "Context needs review" : "Workbench context complete",
        tone: validationIssues.length > 0 ? "warning" : "success"
      }
    ],
    fundEvents: fundEvents.map(buildManualJournalPrivateCapitalFundEventRow),
    capitalAccounts: capitalAccounts.map(buildManualJournalPrivateCapitalAccountRow),
    capitalAccountSubledgers: capitalAccountSubledgers.map(buildManualJournalPrivateCapitalCapitalAccountSubledgerRow),
    capitalAccountSubledgerEntries: capitalAccountSubledgerEntries.map(buildManualJournalPrivateCapitalSubledgerEntryRow),
    ledgerImpacts: ledgerImpacts.map(buildManualJournalPrivateCapitalLedgerImpactRow),
    reportOutputs: reportOutputs.map(buildManualJournalPrivateCapitalReportOutputRow),
    fundEventLedgerRecords: fundEventLedgerRecords.map((record) =>
      buildManualJournalPrivateCapitalFundEventLedgerRecordRow(record, activity.fundProfileId, activity.ledgerBookId)
    ),
    paymentIntents: paymentIntents.map(buildManualJournalPaymentIntentWorkflowRow),
    validationIssues
  };
}

function buildManualJournalPrivateCapitalCapitalAccountSubledgerRow(
  subledger: PrivateCapitalCapitalAccountSubledger
): ManualJournalPrivateCapitalCapitalAccountSubledgerRowViewModel {
  const evidenceCategories = (subledger.evidenceCategories ?? []).map(buildManualJournalPrivateCapitalEvidenceCategoryRow);
  const readyEvidenceCategoryCount = evidenceCategories.filter((category) => category.statusLabel === "Ready").length;
  const paymentEvidence = buildManualJournalPaymentIntentEvidenceView(
    subledger.paymentIntentEvidence,
    subledger.evidenceLinks,
    subledger.currency,
    subledger.endingNetActivity,
    subledger.lastEffectiveDate ?? null
  );
  const hasCriticalIssue = subledger.validationIssues.some((issue) => issue.severity === "Critical");
  const hasWarnings = subledger.validationIssues.length > 0 || subledger.approvalQueueCount > 0 || evidenceCategories.some((category) => category.statusLabel !== "Ready");
  const readiness = subledger.readiness ?? (hasCriticalIssue ? "Blocked" : hasWarnings ? "ReportReview" : subledger.fundEventCount > 0 ? "Ready" : "EvidenceMissing");

  return {
    id: subledger.subledgerId,
    title: subledger.capitalAccountId,
    subtitle: `${subledger.investorId ?? "Investor not assigned"} / ${subledger.currency}`,
    statusLabel: subledger.readinessLabel || (hasCriticalIssue ? "Blocked" : hasWarnings ? "Review" : subledger.fundEventCount > 0 ? "Ready" : "Empty"),
    statusTone: manualJournalPrivateCapitalReadinessTone(readiness),
    readinessLabel: subledger.readinessLabel || readiness,
    readinessTone: manualJournalPrivateCapitalReadinessTone(readiness),
    readinessReasonLabel: subledger.readinessReason || "No subledger readiness reason",
    nextActionLabel: subledger.nextAction || "No next action",
    nextActionRouteLabel: subledger.nextActionRoute || "No next-action route",
    activityRouteLabel: subledger.activityRoute || "No subledger route",
    netActivityLabel: formatCurrencyWithCode(subledger.netCapitalActivity, subledger.currency, true),
    openingLabel: formatCurrencyWithCode(subledger.openingNetActivity, subledger.currency, true),
    endingLabel: formatCurrencyWithCode(subledger.endingNetActivity, subledger.currency, true),
    contributionLabel: formatCurrencyWithCode(subledger.contributions, subledger.currency),
    distributionLabel: formatCurrencyWithCode(subledger.distributions, subledger.currency),
    otherActivityLabel: [
      `Subscriptions ${formatCurrencyWithCode(subledger.subscriptions, subledger.currency)}`,
      `Redemptions ${formatCurrencyWithCode(subledger.redemptions, subledger.currency)}`,
      `Fees ${formatCurrencyWithCode(subledger.managementFees, subledger.currency)}`
    ].join(" / "),
    eventCountLabel: `${subledger.fundEventCount.toLocaleString()} fund event(s)`,
    approvalQueueLabel: `${subledger.approvalQueueCount.toLocaleString()} approval queue`,
    postedEventLabel: `${subledger.postedFundEventCount.toLocaleString()} posted event(s)`,
    publishedReportLabel: `${subledger.publishedReportOutputCount.toLocaleString()} published report output(s)`,
    dateRangeLabel: [
      subledger.firstEffectiveDate ? `first ${subledger.firstEffectiveDate}` : "no first effective date",
      subledger.lastEffectiveDate ? `last ${subledger.lastEffectiveDate}` : "no last effective date",
      subledger.lastFundEventType ?? "no last event type"
    ].join(" / "),
    evidenceLabel: `${subledger.evidenceLinkCount.toLocaleString()} evidence`,
    paymentEvidenceLabel: paymentEvidence.label,
    paymentEvidenceTone: paymentEvidence.tone,
    paymentEvidenceSummaryLabel: paymentEvidence.summary,
    paymentEvidenceRequiredLabel: paymentEvidence.required,
    evidenceCategorySummaryLabel: evidenceCategories.length > 0
      ? `${readyEvidenceCategoryCount.toLocaleString()}/${evidenceCategories.length.toLocaleString()} evidence categories ready`
      : "No evidence categories",
    evidenceCategories,
    issueLabel: subledger.validationIssueCount > 0
      ? `${subledger.validationIssueCount.toLocaleString()} subledger issue(s)`
      : "No subledger issues"
  };
}

export function buildManualJournalPrivateCapitalFundEventLedgerRecordRow(
  record: PrivateCapitalFundEventLedgerRecord,
  fundProfileId: string | null | undefined,
  ledgerBookId: string | null | undefined
): ManualJournalPrivateCapitalFundEventLedgerRecordViewModel {
  const evidenceCategories = (record.evidenceCategories ?? []).map(buildManualJournalPrivateCapitalEvidenceCategoryRow);
  const readyEvidenceCategoryCount = evidenceCategories.filter((category) => category.statusLabel === "Ready").length;
  const paymentEvidence = buildManualJournalPaymentIntentEvidenceView(
    record.paymentIntentEvidence,
    record.evidenceLinks,
    record.currency,
    record.netCapitalActivity,
    record.effectiveDate
  );
  return {
    id: record.fundEventRecordId,
    title: record.fundEventType || record.fundEventId,
    subtitle: `${record.capitalAccountId}${record.investorId ? ` / ${record.investorId}` : ""}`,
    statusLabel: record.isPosted ? "Posted" : record.approvalState,
    statusTone: record.isPosted
      ? "success"
      : record.validationIssues.some((issue) => issue.severity === "Critical")
        ? "danger"
        : record.validationIssues.length > 0
          ? "warning"
          : manualJournalPrivateCapitalStatusTone(record.approvalState),
    readinessLabel: record.readinessLabel || record.readiness,
    readinessTone: manualJournalPrivateCapitalReadinessTone(record.readiness),
    readinessReasonLabel: record.readinessReason || "No readiness reason",
    nextActionLabel: record.nextAction || "No next action",
    nextActionRouteLabel: record.nextActionRoute ?? "No next-action route",
    effectiveDateLabel: record.effectiveDate,
    netActivityLabel: formatCurrencyWithCode(record.netCapitalActivity, record.currency, true),
    grossActivityLabel: formatCurrencyWithCode(record.grossAmount, record.currency),
    capitalAccountRollForwardLabel: `${formatCurrencyWithCode(record.capitalAccountOpeningNetActivity, record.currency, true)} opening -> ${formatCurrencyWithCode(record.capitalAccountEndingNetActivity, record.currency, true)} ending`,
    memoLabel: record.memo || record.journalEntryId,
    referenceLabel: [record.paymentIntentId, record.settlementReference].filter((value): value is string => Boolean(value)).join(" / ") || record.journalEntryId,
    paymentEvidenceLabel: paymentEvidence.label,
    paymentEvidenceTone: paymentEvidence.tone,
    paymentEvidenceSummaryLabel: paymentEvidence.summary,
    paymentEvidenceRequiredLabel: paymentEvidence.required,
    activityRouteLabel: record.activityRoute || "No activity route",
    commandCenterRouteLabel: buildPrivateCapitalFundEventCommandCenterRoute(fundProfileId, ledgerBookId, record.fundEventId),
    evidenceRouteLabel: record.evidenceRoute || "No evidence route",
    approvalRouteLabel: record.approvalRoute ?? (record.approvalId ? `/accounting/approvals?approvalId=${encodeURIComponent(record.approvalId)}` : "No approval route"),
    evidenceLabel: `${record.evidenceLinkCount.toLocaleString()} evidence`,
    ledgerImpactLabel: `${record.ledgerImpactCount.toLocaleString()} ledger impact(s)${record.isPostingReady ? " / posting ready" : ""}`,
    subledgerLabel: `${record.capitalAccountSubledgerEntryCount.toLocaleString()} subledger movement(s)`,
    reportOutputLabel: `${record.reportOutputCount.toLocaleString()} report output(s)${record.isPublished ? " / published" : record.isReportReady ? " / ready" : ""}`,
    reportOutputDetailLabel: [
      record.primaryReportOutputType ?? "No primary output",
      record.reportWorkflowState ?? "No workflow state",
      `${record.reportLineProvenanceCount.toLocaleString()} provenance`
    ].join(" / "),
    reportOutputRouteLabel: record.primaryReportRoute ?? record.retainedManifestPath ?? record.publicationManifestId ?? "No report route",
    evidenceCategorySummaryLabel: evidenceCategories.length > 0
      ? `${readyEvidenceCategoryCount.toLocaleString()}/${evidenceCategories.length.toLocaleString()} evidence categories ready`
      : "No evidence categories",
    evidenceCategories,
    issueLabel: record.validationIssueCount > 0
      ? `${record.validationIssueCount.toLocaleString()} record issue(s)`
      : "No record issues"
  };
}

function buildManualJournalPaymentIntentWorkflowRow(
  workflow: PaymentIntentWorkflow
): ManualJournalPaymentIntentWorkflowRowViewModel {
  const movement = workflow.expectedCashMovement;
  const approvedCount = workflow.approvalChain.filter((step) => step.status === "Approved").length;
  const bankConfirmedCount = workflow.bankEvidence.filter((item) => item.status === "Confirmed").length;
  const bankRetainedCount = workflow.bankEvidence.filter((item) => item.status === "Retained").length;
  const bankReturnedCount = workflow.bankEvidence.filter((item) => item.status === "Returned").length;
  const reconciliationReadyCount = workflow.reconciliationLinks.filter((item) => item.status === "Ready").length;

  return {
    id: workflow.paymentIntentId,
    title: workflow.paymentIntentId,
    subtitle: [workflow.fundEventId, movement.capitalAccountId, movement.investorId].filter(Boolean).join(" / ") || workflow.journalEntryId,
    statusLabel: workflow.statusLabel || formatPaymentIntentWorkflowStatus(workflow.status),
    statusTone: paymentIntentWorkflowTone(workflow.status),
    requestedLabel: `${workflow.requester} / ${formatDateTimeLabel(workflow.requestedAtUtc)}`,
    expectedCashLabel: [
      movement.direction,
      formatCurrencyWithCode(Math.abs(movement.amount), movement.currency),
      movement.effectiveDate,
      movement.settlementReference ?? "no settlement"
    ].join(" / "),
    requestMetadataLabel: [
      `payee ${movement.payee ?? "not assigned"}`,
      `scope ${movement.accountScope ?? "not assigned"}`,
      `purpose ${movement.businessPurpose ?? movement.purpose}`,
      `policy ${movement.approvalPolicy ?? "not assigned"}`
    ].join(" / "),
    sourceEvidenceLabel: `${(movement.sourceEvidenceLinks ?? []).length.toLocaleString()} source evidence link(s)`,
    approvalLabel: `${approvedCount.toLocaleString()}/${workflow.approvalChain.length.toLocaleString()} approved`,
    bankEvidenceLabel: `${bankConfirmedCount.toLocaleString()} confirmed / ${bankRetainedCount.toLocaleString()} retained / ${bankReturnedCount.toLocaleString()} returned`,
    reconciliationLabel: `${reconciliationReadyCount.toLocaleString()}/${workflow.reconciliationLinks.length.toLocaleString()} reconciliation ready`,
    auditLabel: `${workflow.auditHistory.length.toLocaleString()} audit event(s)`,
    readinessReasonLabel: workflow.readinessReason || "Payment intent readiness requires review.",
    executionDeferredLabel: workflow.executionDeferredReason || "Full payment execution is deferred.",
    evidenceRouteLabel: workflow.evidenceRoute || "No evidence route",
    workbenchRouteLabel: workflow.workbenchRoute || "No workbench route",
    approvalSteps: workflow.approvalChain.map((step) => ({
      id: `${workflow.paymentIntentId}-approval-${step.sequence}-${step.role}`,
      sequenceLabel: `Step ${step.sequence.toLocaleString()}`,
      roleLabel: step.role || "Approval",
      actorLabel: step.actor || "No actor assigned",
      statusLabel: step.status || "No status",
      decidedLabel: step.decidedAtUtc ? formatDateTimeLabel(step.decidedAtUtc) : "Decision pending",
      evidenceRouteLabel: step.evidenceRoute || "No approval evidence route"
    })),
    bankEvidence: workflow.bankEvidence.map((item) => ({
      id: item.evidenceId,
      title: item.evidenceKind || item.evidenceId,
      statusLabel: item.status || "No status",
      summaryLabel: item.summary || "No cash evidence summary",
      amountLabel: item.amount !== null && item.amount !== undefined && item.currency
        ? formatCurrencyWithCode(Math.abs(item.amount), item.currency)
        : "No amount",
      effectiveDateLabel: item.effectiveDate ?? "No effective date",
      recordedLabel: item.recordedAtUtc ? formatDateTimeLabel(item.recordedAtUtc) : "No recorded timestamp",
      recorderLabel: item.recordedBy ? `Recorded by ${item.recordedBy}` : "No retained recorder",
      referenceLabel: [
        item.bankTransactionId ? `bank ${item.bankTransactionId}` : null,
        item.transactionType ?? null,
        item.externalRef ?? null
      ].filter((value): value is string => Boolean(value)).join(" / ") || "No external reference",
      evidenceRouteLabel: item.evidenceRoute || "No bank evidence route"
    })),
    reconciliationLinks: workflow.reconciliationLinks.map((link) => ({
      id: link.linkId,
      statusLabel: link.status || "No status",
      summaryLabel: link.summary || "No reconciliation summary",
      routeLabel: link.evidenceRoute || "No reconciliation evidence route",
      caseLabel: [
        link.reconciliationCaseId ? `case ${link.reconciliationCaseId}` : null,
        link.reconciliationRunId ? `run ${link.reconciliationRunId}` : null
      ].filter((value): value is string => Boolean(value)).join(" / ") || "No reconciliation case"
    })),
    auditEvents: workflow.auditHistory.map((event) => ({
      id: event.auditEventId,
      actionLabel: event.action || "No action",
      actorLabel: event.actor || "No actor",
      recordedLabel: formatDateTimeLabel(event.recordedAtUtc),
      summaryLabel: event.summary || "No audit summary",
      evidenceLabel: `${event.evidenceLinks.length.toLocaleString()} evidence link(s)`,
      evidenceRouteLabels: event.evidenceLinks
    }))
  };
}

function buildManualJournalPrivateCapitalEvidenceCategoryRow(
  category: PrivateCapitalEvidenceCategory
): ManualJournalPrivateCapitalEvidenceCategoryViewModel {
  return {
    id: category.categoryId,
    label: category.label || category.categoryId,
    statusLabel: category.isReady ? "Ready" : "Missing",
    tone: category.isReady ? "success" : "warning",
    summaryLabel: category.summary || "No category summary",
    evidenceLabel: `${category.evidenceLinkCount.toLocaleString()} evidence`,
    requiredEvidenceLabel: (category.requiredEvidence ?? []).filter(Boolean).join(" / ") || "No required evidence listed"
  };
}

export function buildManualJournalPaymentIntentEvidenceView(
  evidence: PrivateCapitalPaymentIntentEvidence | null | undefined,
  fallbackEvidenceLinks: string[],
  currency: string,
  amount: number,
  effectiveDate: string | null
): { label: string; tone: "success" | "warning"; summary: string; required: string } {
  if (!evidence) {
    return {
      label: `Missing intent / ${formatCurrencyWithCode(Math.abs(amount), currency)} / ${effectiveDate ?? "no effective date"}`,
      tone: "warning",
      summary: "Payment intent evidence is not included in the available accounting data.",
      required: "Payment intent id / Retained bank, cash, or settlement evidence"
    };
  }

  const required = (evidence.requiredEvidence ?? []).filter(Boolean);
  const fallbackCashCount = fallbackEvidenceLinks.filter((link) => /bank|cash|custodian|payment|plaid|settlement|treasury|wire/i.test(link)).length;
  const cashEvidenceCount = evidence.cashEvidenceLinkCount ?? fallbackCashCount;
  const labelParts = [
    formatPaymentIntentEvidenceStatus(evidence),
    evidence.direction,
    formatCurrencyWithCode(Math.abs(evidence.amount), evidence.currency),
    `${cashEvidenceCount.toLocaleString()} cash evidence`,
    evidence.settlementReference ? "settlement linked" : "no settlement"
  ].filter(Boolean);

  return {
    label: labelParts.join(" / "),
    tone: evidence.isReady ? "success" : "warning",
    summary: evidence.summary || "No payment intent evidence summary.",
    required: required.length > 0 ? required.join(" / ") : "No additional payment evidence required"
  };
}

function formatPaymentIntentEvidenceStatus(evidence: PrivateCapitalPaymentIntentEvidence): string {
  const status = evidence.status;
  if (status === "SettlementMatched") return "Settlement matched";
  if (status === "IntentCaptured") return "Cash evidence retained";
  if (status === "CashEvidenceMissing") return "Cash evidence missing";
  if (evidence.isReady && evidence.settlementReference) return "Settlement matched";
  if (evidence.isReady) return "Cash evidence retained";
  return "Payment intent missing";
}

function formatPaymentIntentWorkflowStatus(status: PaymentIntentWorkflow["status"]): string {
  if (status === "ExecutionDeferred") return "Ready, execution deferred";
  if (status === "ReconciliationPending") return "Reconciliation pending";
  if (status === "BankEvidencePending") return "Bank evidence pending";
  if (status === "BankReturned") return "Bank return captured";
  if (status === "ApprovalPending") return "Approval pending";
  if (status === "Blocked") return "Blocked";
  return "Intent evidence missing";
}

function paymentIntentWorkflowTone(status: PaymentIntentWorkflow["status"]): ManualJournalPaymentIntentWorkflowRowViewModel["statusTone"] {
  if (status === "ExecutionDeferred") return "success";
  if (status === "Blocked" || status === "BankReturned") return "danger";
  if (status === "EvidenceMissing" || status === "BankEvidencePending" || status === "ReconciliationPending" || status === "ApprovalPending") return "warning";
  return "outline";
}

function buildManualJournalPrivateCapitalFundEventRow(
  event: PrivateCapitalFundEvent
): ManualJournalPrivateCapitalFundEventRowViewModel {
  return {
    id: event.fundEventId,
    title: event.fundEventType || event.entryType,
    subtitle: `${event.capitalAccountId}${event.investorId ? ` / ${event.investorId}` : ""}`,
    statusLabel: event.isPosted ? "Posted" : event.journalStatus,
    statusTone: event.isPosted ? "success" : manualJournalPrivateCapitalStatusTone(event.journalStatus),
    effectiveDateLabel: event.effectiveDate,
    amountLabel: formatCurrencyWithCode(event.netCapitalActivity, event.currency, true),
    grossAmountLabel: formatCurrencyWithCode(event.grossAmount, event.currency),
    evidenceLabel: `${event.evidenceLinks.length.toLocaleString()} evidence`,
    memoLabel: event.memo || event.journalEntryId,
    paymentLabel: [event.paymentIntentId, event.settlementReference].filter((value): value is string => Boolean(value)).join(" / ") || "No payment link",
    validationLabel: event.validationIssues.length > 0
      ? `${event.validationIssues.length.toLocaleString()} validation issues`
      : "No validation issues"
  };
}

function buildManualJournalPrivateCapitalAccountRow(
  account: PrivateCapitalCapitalAccountActivity
): ManualJournalPrivateCapitalAccountRowViewModel {
  return {
    id: `${account.capitalAccountId}-${account.currency}`,
    title: account.capitalAccountId,
    subtitle: account.investorId ?? "Investor not assigned",
    netActivityLabel: formatCurrencyWithCode(account.netActivity, account.currency, true),
    contributionLabel: formatCurrencyWithCode(account.contributions, account.currency),
    distributionLabel: formatCurrencyWithCode(account.distributions, account.currency),
    subscriptionLabel: formatCurrencyWithCode(account.subscriptions, account.currency),
    redemptionLabel: formatCurrencyWithCode(account.redemptions, account.currency),
    managementFeeLabel: formatCurrencyWithCode(account.managementFees, account.currency),
    eventCountLabel: `${account.fundEventCount.toLocaleString()} events`,
    lastEventLabel: account.lastEffectiveDate
      ? `${account.lastFundEventType ?? "Fund event"} / ${account.lastEffectiveDate}`
      : "No effective date"
  };
}

function buildManualJournalPrivateCapitalSubledgerEntryRow(
  entry: PrivateCapitalCapitalAccountSubledgerEntry
): ManualJournalPrivateCapitalSubledgerEntryRowViewModel {
  return {
    id: entry.subledgerEntryId,
    title: entry.fundEventType || entry.entryType,
    subtitle: `${entry.capitalAccountId}${entry.investorId ? ` / ${entry.investorId}` : ""}`,
    statusLabel: entry.isPosted ? "Posted" : entry.approvalState,
    statusTone: entry.isPosted ? "success" : manualJournalPrivateCapitalStatusTone(entry.approvalState),
    effectiveDateLabel: entry.effectiveDate,
    netActivityLabel: formatCurrencyWithCode(entry.netCapitalActivity, entry.currency, true),
    runningBalanceLabel: formatCurrencyWithCode(entry.runningNetActivity, entry.currency, true),
    grossAmountLabel: formatCurrencyWithCode(entry.grossAmount, entry.currency),
    evidenceLabel: `${entry.evidenceLinks.length.toLocaleString()} evidence`,
    memoLabel: entry.memo || entry.journalEntryId,
    issueLabel: entry.validationIssues.length > 0
      ? `${entry.validationIssues.length.toLocaleString()} subledger issues`
      : "No subledger issues"
  };
}

function buildManualJournalPrivateCapitalLedgerImpactRow(
  impact: PrivateCapitalLedgerImpact
): ManualJournalPrivateCapitalLedgerImpactRowViewModel {
  return {
    id: impact.ledgerImpactId,
    title: impact.fundEventType || impact.fundEventId,
    subtitle: `${impact.capitalAccountId}${impact.investorId ? ` / ${impact.investorId}` : ""}`,
    readinessLabel: impact.isPostingReady ? "Posting ready" : impact.isBalanced ? "Review" : "Unbalanced",
    readinessTone: impact.isPostingReady ? "success" : impact.validationIssues.some((issue) => issue.severity === "Critical") ? "danger" : "warning",
    effectiveDateLabel: impact.effectiveDate,
    debitLabel: formatCurrencyWithCode(impact.totalDebits, impact.currency),
    creditLabel: formatCurrencyWithCode(impact.totalCredits, impact.currency),
    imbalanceLabel: formatCurrencyWithCode(impact.imbalance, impact.currency, true),
    evidenceLabel: `${impact.evidenceLinks.length.toLocaleString()} evidence`,
    lineLabel: `${impact.lineCount.toLocaleString()} GL lines`,
    issueLabel: impact.validationIssues.length > 0
      ? `${impact.validationIssues.length.toLocaleString()} ledger issues`
      : "No ledger issues"
  };
}

function buildManualJournalPrivateCapitalReportOutputRow(
  output: PrivateCapitalReportOutput
): ManualJournalPrivateCapitalReportOutputRowViewModel {
  return {
    id: output.reportOutputId,
    title: output.displayName || output.reportOutputType,
    subtitle: `${output.capitalAccountId}${output.investorId ? ` / ${output.investorId}` : ""}`,
    readinessLabel: output.readinessLabel || (output.isPublished ? "Published" : output.isReportReady ? "Ready" : "Review"),
    readinessTone: output.isPublished || output.isReportReady ? "success" : output.validationIssues.some((issue) => issue.severity === "Critical") ? "danger" : "warning",
    readinessReasonLabel: output.readinessReason || "No report-output readiness reason",
    nextActionLabel: output.nextAction || "No next action",
    nextActionRouteLabel: output.nextActionRoute ?? output.reportOutputRoute ?? output.reportRoute,
    effectiveDateLabel: output.effectiveDate,
    amountLabel: formatCurrencyWithCode(output.netCapitalActivity, output.currency, true),
    evidenceLabel: `${output.evidenceLinkCount.toLocaleString()} evidence`,
    routeLabel: output.reportOutputRoute ?? output.reportRoute,
    issueLabel: output.validationIssues.length > 0
      ? `${output.validationIssues.length.toLocaleString()} readiness issues`
      : "No readiness issues",
    workflowLabel: [output.reportPackId, output.reportWorkflowState].filter((value): value is string => Boolean(value)).join(" / ") || output.reportOutputType,
    publicationLabel: output.publishedAtUtc
      ? `${output.publishedBy ?? "publisher"} / ${formatDateTimeLabel(output.publishedAtUtc)}`
      : output.publicationManifestId ?? "No publication manifest",
    provenanceLabel: `${(output.reportLineProvenanceCount ?? 0).toLocaleString()} provenance line(s)`
  };
}

function manualJournalPrivateCapitalStatusTone(
  status: PrivateCapitalFundEvent["journalStatus"]
): ManualJournalPrivateCapitalFundEventRowViewModel["statusTone"] {
  if (status === "Approved") {
    return "success";
  }

  if (status === "Submitted") {
    return "warning";
  }

  if (status === "NeedsFix" || status === "Rejected") {
    return "danger";
  }

  return "outline";
}

function createManualJournalEntryDraft(
  fundProfileId = "default-fund",
  ledgerBookId: string | null = null
): ManualJournalEntryDraft {
  const now = new Date().toISOString();
  const currency = "USD";
  return {
    journalEntryId: newClientId(),
    status: "Draft",
    fundProfileId,
    ledgerBookId,
    accountingBasis: DEFAULT_ACCOUNTING_BASIS,
    accountingDate: now.slice(0, 10),
    periodId: null,
    entityId: null,
    fundNodeId: null,
    currency,
    memo: "Manual close adjustment",
    preparedBy: "browser-user",
    createdAtUtc: now,
    updatedAtUtc: now,
    version: 0,
    lines: [
      createManualJournalEntryLine("Debit", currency, ""),
      createManualJournalEntryLine("Credit", currency, "")
    ],
    evidenceLinks: [],
    evidenceAttachments: [],
    validationIssues: [],
    totalDebits: 0,
    totalCredits: 0,
    imbalance: 0,
    approvalId: null,
    submittedAtUtc: null,
    submittedBy: null,
    entryType: "General",
    treasuryContext: null,
    dimensions: null,
    lifecycleTransitions: [],
    reversalOfJournalEntryId: null,
    rebookedFromJournalEntryId: null,
    approvedAtUtc: null,
    approvedBy: null,
    postedAtUtc: null,
    postedBy: null,
    closedLockedAtUtc: null,
    closeLockedBy: null
  };
}

function withManualJournalTotals(draft: ManualJournalEntryDraft): ManualJournalEntryDraft {
  const totalDebits = draft.lines
    .filter((line) => line.side === "Debit")
    .reduce((sum, line) => sum + (Number.isFinite(line.amount) ? line.amount : 0), 0);
  const totalCredits = draft.lines
    .filter((line) => line.side === "Credit")
    .reduce((sum, line) => sum + (Number.isFinite(line.amount) ? line.amount : 0), 0);

  return {
    ...draft,
    totalDebits,
    totalCredits,
    imbalance: totalDebits - totalCredits
  };
}

function buildManualJournalBalanceImpactRows(
  draft: ManualJournalEntryDraft,
  chartOfAccounts: ChartOfAccountsNode[]
): ManualJournalBalanceImpactRowViewModel[] {
  const accountsByPath = new Map(chartOfAccounts.map((account) => [account.path, account]));
  const groups = new Map<string, { debit: number; credit: number; count: number }>();

  for (const line of draft.lines) {
    const accountPath = line.accountPath.trim();
    if (!accountPath) {
      continue;
    }

    const current = groups.get(accountPath) ?? { debit: 0, credit: 0, count: 0 };
    const amount = Number.isFinite(line.amount) ? Math.max(line.amount, 0) : 0;
    if (line.side === "Debit") {
      current.debit += amount;
    } else {
      current.credit += amount;
    }

    current.count += 1;
    groups.set(accountPath, current);
  }

  return [...groups.entries()]
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([accountPath, totals]) => {
      const account = accountsByPath.get(accountPath);
      const normalBalanceSide = manualJournalNormalBalanceSide(account?.accountType);
      const netEffect = normalBalanceSide === "Debit"
        ? totals.debit - totals.credit
        : totals.credit - totals.debit;
      const direction = netEffect > 0 ? "increases" : netEffect < 0 ? "decreases" : "does not change";
      const absoluteEffect = formatCurrency(Math.abs(netEffect));

      return {
        id: accountPath,
        accountPath,
        accountName: account?.accountName ?? accountPath,
        accountType: account?.accountType ?? "Unclassified",
        debitLabel: formatCurrency(totals.debit),
        creditLabel: formatCurrency(totals.credit),
        netEffectLabel: `${netEffect >= 0 ? "+" : "-"}${absoluteEffect}`,
        balanceDirectionLabel: `This draft ${direction} the ${normalBalanceSide.toLowerCase()}-normal account balance by ${absoluteEffect}.`,
        lineCountLabel: `${totals.count} line${totals.count === 1 ? "" : "s"}`,
        tone: netEffect === 0 ? "default" : totals.debit > 0 && totals.credit > 0 ? "warning" : "success"
      };
    });
}

function manualJournalNormalBalanceSide(accountType?: string | null): AccountingTemplateLineSide {
  const normalized = (accountType ?? "").toLowerCase();
  return normalized.includes("liabil")
    || normalized.includes("equity")
    || normalized.includes("revenue")
    || normalized.includes("income")
    ? "Credit"
    : "Debit";
}

function formatManualJournalTreasuryContext(draft: ManualJournalEntryDraft): string {
  const context = draft.treasuryContext;
  if (!context) {
    return `${draft.entryType} | No treasury context`;
  }

  const parts = [
    draft.entryType,
    context.effectiveDate ? `effective ${context.effectiveDate}` : null,
    context.fundEventType ?? context.fundEventId ?? null,
    context.capitalAccountId ? `capital ${context.capitalAccountId}` : null,
    context.paymentIntentId || context.settlementReference ? "payment-linked" : null,
    context.idempotencyKey ? "idempotent" : null
  ].filter((part): part is string => Boolean(part));

  return parts.join(" | ");
}

function lifecycleActionNotes(action: JournalEntryLifecycleAction, draft: ManualJournalEntryDraft): string {
  switch (action) {
    case "Approve":
      return `Controller approval for journal entry ${draft.journalEntryId}.`;
    case "Reject":
      return `Controller rejection for journal entry ${draft.journalEntryId}.`;
    case "Post":
      return `Post approved journal entry ${draft.journalEntryId}.`;
    case "Reverse":
      return `Create reversal draft for posted journal entry ${draft.journalEntryId}.`;
    case "Rebook":
      return `Create rebook draft for posted journal entry ${draft.journalEntryId}.`;
    case "LockAfterClose":
      return `Lock posted journal entry ${draft.journalEntryId} after close.`;
    case "Submit":
      return `Submit journal entry ${draft.journalEntryId} for approval.`;
    case "Validate":
      return `Validate journal entry ${draft.journalEntryId}.`;
    default:
      return `Apply ${action} to journal entry ${draft.journalEntryId}.`;
  }
}

function buildManualJournalLifecycleCommands(
  draft: ManualJournalEntryDraft,
  busyAction: JournalEntryLifecycleAction | null
): ManualJournalLifecycleCommandViewModel[] {
  const hasEvidence = (draft.evidenceLinks?.length ?? 0) > 0 || (draft.evidenceAttachments?.length ?? 0) > 0;
  const isBalanced = Math.abs(draft.imbalance) === 0;
  const hasCriticalIssues = draft.validationIssues.some((issue) => issue.severity === "Critical");
  const commonBlocker = !hasEvidence
    ? "Attach retained evidence before lifecycle transitions."
    : !isBalanced
      ? "Balance debits and credits before lifecycle transitions."
      : hasCriticalIssues
        ? "Resolve critical validation issues before lifecycle transitions."
        : null;

  return [
    lifecycleCommand("Approve", "Approve", "Move a submitted journal entry to approved.", draft.status === "Submitted" ? commonBlocker : `Requires Submitted status; current status is ${draft.status}.`, "success", busyAction),
    lifecycleCommand("Reject", "Reject", "Reject a submitted journal entry for correction.", draft.status === "Submitted" ? null : `Requires Submitted status; current status is ${draft.status}.`, "danger", busyAction),
    lifecycleCommand("Post", "Post", "Post an approved journal entry without mutating it afterward.", draft.status === "Approved" ? commonBlocker : `Requires Approved status; current status is ${draft.status}.`, "success", busyAction),
    lifecycleCommand("Reverse", "Reverse", "Generate a separate reversal draft for a posted journal entry.", draft.status === "Posted" ? commonBlocker : `Requires Posted status; current status is ${draft.status}.`, "warning", busyAction),
    lifecycleCommand("Rebook", "Rebook", "Generate a separate rebook draft using the current posted lines.", draft.status === "Posted" ? commonBlocker : `Requires Posted status; current status is ${draft.status}.`, "warning", busyAction),
    lifecycleCommand("LockAfterClose", "Lock after close", "Lock a posted journal entry after close.", draft.status === "Posted" ? commonBlocker : `Requires Posted status; current status is ${draft.status}.`, "default", busyAction)
  ];
}

function buildManualJournalLifecycleChecklist(
  draft: ManualJournalEntryDraft,
  commands: ManualJournalLifecycleCommandViewModel[],
  transitions: ManualJournalLifecycleTransitionViewModel[],
  correctionRows: ManualJournalLifecycleCorrectionViewModel[]
): ManualJournalLifecycleChecklistItemViewModel[] {
  const evidenceCount = new Set([
    ...(draft.evidenceLinks ?? []),
    ...(draft.evidenceAttachments ?? []).map((attachment) => attachment.uri)
  ].filter((value) => value.trim().length > 0)).size;
  const criticalIssueCount = draft.validationIssues.filter((issue) => issue.severity === "Critical").length;
  const warningIssueCount = draft.validationIssues.filter((issue) => issue.severity === "Warning").length;
  const isBalanced = Math.abs(draft.imbalance) === 0;
  const commandByAction = new Map(commands.map((command) => [command.action, command]));
  const lifecycleState = new Set([draft.status, ...transitions.map((transition) => transition.title)]);
  const approved = draft.status === "Approved" || draft.status === "Posted" || draft.status === "Reversed" || draft.status === "Rebooked" || draft.status === "CloseLocked" || Boolean(draft.approvedAtUtc);
  const posted = draft.status === "Posted" || draft.status === "Reversed" || draft.status === "Rebooked" || draft.status === "CloseLocked" || Boolean(draft.postedAtUtc);
  const reversed = draft.status === "Reversed" || Boolean(draft.reversalOfJournalEntryId) || correctionRows.some((row) => /reversal/i.test(row.sourceLabel));
  const rebooked = draft.status === "Rebooked" || Boolean(draft.rebookedFromJournalEntryId) || correctionRows.some((row) => /rebook/i.test(row.sourceLabel));
  const locked = draft.status === "CloseLocked" || Boolean(draft.closedLockedAtUtc);

  const commandReadiness = (action: JournalEntryLifecycleAction): { value: string; detail: string; tone: AccountingToolingTone } => {
    const command = commandByAction.get(action);
    if (!command) {
      return { value: "Unavailable", detail: "Lifecycle command is not surfaced for this journal entry.", tone: "default" };
    }

    if (command.disabledReason) {
      return { value: "Blocked", detail: command.disabledReason, tone: "warning" };
    }

    return { value: "Ready", detail: command.description, tone: command.tone === "danger" ? "danger" : command.tone === "success" ? "success" : "warning" };
  };
  const approveReadiness = commandReadiness("Approve");
  const postReadiness = commandReadiness("Post");
  const reverseReadiness = commandReadiness("Reverse");
  const rebookReadiness = commandReadiness("Rebook");
  const lockReadiness = commandReadiness("LockAfterClose");

  return [
    {
      id: "draft",
      label: "Draft",
      value: draft.version > 0 ? `v${draft.version}` : "Unsaved",
      detail: draft.version > 0
        ? `Retained draft ${draft.journalEntryId} is loaded for ${draft.accountingDate}.`
        : "Save the draft before relying on version-guarded lifecycle actions.",
      tone: draft.version > 0 ? "success" : "warning"
    },
    {
      id: "validate",
      label: "Validate",
      value: criticalIssueCount > 0 ? `${criticalIssueCount} critical` : warningIssueCount > 0 ? `${warningIssueCount} warning` : "Clear",
      detail: criticalIssueCount > 0
        ? "Critical validation issues block approval and posting."
        : warningIssueCount > 0
          ? "Warnings are retained for reviewer attention."
          : isBalanced
            ? "No validation issues are attached and debits equal credits."
            : `Entry is out of balance by ${formatCurrencyWithCode(Math.abs(draft.imbalance), draft.currency)}.`,
      tone: criticalIssueCount > 0 ? "danger" : warningIssueCount > 0 || !isBalanced ? "warning" : "success"
    },
    {
      id: "evidence",
      label: "Attach evidence",
      value: evidenceCount > 0 ? formatCount(evidenceCount, "evidence link") : "Missing",
      detail: evidenceCount > 0
        ? "Source support is retained on the header or line evidence set."
        : "Approval, posting, reversal, rebook, and close-lock transitions require retained evidence.",
      tone: evidenceCount > 0 ? "success" : "warning"
    },
    {
      id: "submit",
      label: "Submit",
      value: draft.status === "Submitted" || approved || posted ? "Submitted" : draft.status === "Draft" ? "Draft" : draft.status,
      detail: draft.status === "Submitted" || approved || posted
        ? "Journal entry has crossed the approval submission gate."
        : "Use Submit approval after validation, balance, and evidence are ready.",
      tone: draft.status === "Submitted" || approved || posted ? "success" : "warning"
    },
    {
      id: "approve",
      label: "Approve",
      value: approved ? "Approved" : approveReadiness.value,
      detail: approved
        ? `${draft.approvedBy ?? "Reviewer"} approved the entry${draft.approvedAtUtc ? ` on ${formatDateTimeLabel(draft.approvedAtUtc)}` : ""}.`
        : approveReadiness.detail,
      tone: approved ? "success" : approveReadiness.tone
    },
    {
      id: "post",
      label: "Post",
      value: posted ? "Posted" : postReadiness.value,
      detail: posted
        ? `${draft.postedBy ?? "Operator"} posted the immutable entry${draft.postedAtUtc ? ` on ${formatDateTimeLabel(draft.postedAtUtc)}` : ""}.`
        : postReadiness.detail,
      tone: posted ? "success" : postReadiness.tone
    },
    {
      id: "reverse",
      label: "Reverse",
      value: reversed ? "Linked" : reverseReadiness.value,
      detail: reversed
        ? "A reversal relationship or generated reversal draft is retained; the posted entry was not edited in place."
        : reverseReadiness.detail,
      tone: reversed ? "success" : reverseReadiness.tone
    },
    {
      id: "rebook",
      label: "Rebook",
      value: rebooked ? "Linked" : rebookReadiness.value,
      detail: rebooked
        ? "A rebook relationship or generated rebook draft is retained; the original posted entry remains immutable."
        : rebookReadiness.detail,
      tone: rebooked ? "success" : rebookReadiness.tone
    },
    {
      id: "lock-after-close",
      label: "Lock after close",
      value: locked ? "Locked" : lockReadiness.value,
      detail: locked
        ? `${draft.closeLockedBy ?? "Close controller"} locked the entry after close${draft.closedLockedAtUtc ? ` on ${formatDateTimeLabel(draft.closedLockedAtUtc)}` : ""}.`
        : lockReadiness.detail,
      tone: locked ? "success" : lockReadiness.tone
    },
    {
      id: "audit",
      label: "Audit transitions",
      value: transitions.length > 0 ? formatCount(transitions.length, "transition") : "None",
      detail: transitions.length > 0
        ? "Lifecycle transitions retain audit id, correlation id, actor, notes, and evidence routes."
        : "No lifecycle transition audit rows are retained on this journal entry yet.",
      tone: transitions.length > 0 ? "success" : lifecycleState.has("Draft") ? "warning" : "default"
    }
  ];
}

function lifecycleCommand(
  action: JournalEntryLifecycleAction,
  label: string,
  description: string,
  disabledReason: string | null,
  tone: ManualJournalLifecycleCommandViewModel["tone"],
  busyAction: JournalEntryLifecycleAction | null
): ManualJournalLifecycleCommandViewModel {
  return {
    action,
    label,
    description,
    disabledReason: busyAction && busyAction !== action ? `Lifecycle action ${busyAction} is already running.` : disabledReason,
    tone,
    busy: busyAction === action
  };
}

function formatManualJournalLifecycleTransition(
  transition: JournalEntryLifecycleTransition
): ManualJournalLifecycleTransitionViewModel {
  const evidenceRows = transition.evidenceLinks.length > 0
    ? transition.evidenceLinks
    : ["No transition evidence links retained."];

  return {
    id: transition.transitionId,
    title: `${transition.action}: ${transition.fromStatus} -> ${transition.toStatus}`,
    detail: `${transition.actor} / ${formatDateTimeLabel(transition.recordedAtUtc)}${transition.notes ? ` / ${transition.notes}` : ""}`,
    auditLabel: `Audit ${transition.transitionId}`,
    correlationLabel: transition.correlationId ? `Correlation ${transition.correlationId}` : "No correlation id retained",
    evidenceLabel: transition.evidenceLinks.length > 0
      ? `${transition.evidenceLinks.length.toLocaleString()} evidence link(s)`
      : "No transition evidence links",
    evidenceTone: transition.evidenceLinks.length > 0 ? "success" : "outline",
    evidenceRows
  };
}

function formatManualJournalLifecycleCorrection(
  draft: ManualJournalEntryDraft
): ManualJournalLifecycleCorrectionViewModel {
  const source = draft.reversalOfJournalEntryId
    ? `Reversal of ${draft.reversalOfJournalEntryId}`
    : draft.rebookedFromJournalEntryId
      ? `Rebook from ${draft.rebookedFromJournalEntryId}`
      : "Generated correction draft";

  return {
    id: draft.journalEntryId,
    title: draft.memo || "Generated correction draft",
    subtitle: `${draft.status} / v${draft.version} / ${draft.entryType}`,
    balanceLabel: `${formatCurrencyWithCode(draft.totalDebits, draft.currency)} debit / ${formatCurrencyWithCode(draft.totalCredits, draft.currency)} credit`,
    sourceLabel: source
  };
}

function createManualJournalAttachmentDraft(): ManualJournalEvidenceAttachmentDraft {
  return {
    displayName: "",
    uri: "",
    evidenceKind: "SourceDocument",
    sourceSystem: "ManualUpload",
    lineId: null,
    description: ""
  };
}

function createManualJournalEntryLine(
  side: AccountingTemplateLineSide,
  currency: string,
  accountPath: string
): ManualJournalEntryLine {
  return {
    lineId: newClientId(),
    side,
    amount: 0,
    currency,
    accountPath,
    entityId: null,
    fundAllocationId: null,
    securityId: null,
    securityDisplayName: null,
    taxLotId: null,
    description: null,
    evidenceLink: null
  };
}

function newClientId(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }

  return `manual-je-${Date.now()}-${Math.round(Math.random() * 1_000_000)}`;
}
