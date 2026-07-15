import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type {
  AccountingWorkspaceResponse,
  ManualJournalEntryDraft,
  ReportingAccountingBasis,
  ReportingConsolidationLevel,
  ReportingEntityScopeKind,
  ReportingFinality,
  ReportingOutputFormat,
  ReportingRunParameters,
  ReportingRunReadiness
} from "@/types";

export type ReadinessGateTone = "success" | "warning";

export interface ReadinessGateItem {
  id: string;
  label: string;
  count: number;
  tone: ReadinessGateTone;
  href: string | null;
  linkLabel: string | null;
}

export interface ReadinessGateViewState {
  items: ReadinessGateItem[];
  isClear: boolean;
  disclaimer: string;
}

export interface ReportRunParameterDraftState {
  fundProfileId: string;
  entityScopeKind: ReportingEntityScopeKind;
  entityId: string;
  portfolioId: string;
  investorId: string;
  periodId: string;
  ledgerBookId: string;
  ledgerBookCode: string;
  accountingBasis: ReportingAccountingBasis;
  presentationCurrency: string;
  consolidationLevel: ReportingConsolidationLevel;
  outputFormat: ReportingOutputFormat;
  finality: ReportingFinality;
  includeSupportingSchedules: boolean;
  includeEvidenceAppendix: boolean;
  templateParametersJson: string;
}

export type ReportRunParameterDraftField = keyof ReportRunParameterDraftState;

export interface ReportingRunParameterValidation {
  parameters: ReportingRunParameters | null;
  issues: string[];
}

export interface AuthoritativeReadinessGateViewState {
  statusLabel: string;
  summary: string;
  canRun: boolean;
  blockingReasons: string[];
}

export function buildDefaultReportRunParameterDraft({
  fundProfileId,
  asOfDate,
  parameters
}: {
  fundProfileId?: string | null;
  asOfDate: string;
  parameters?: ReportingRunParameters | null;
}): ReportRunParameterDraftState {
  const retained = parameters ?? null;
  return {
    fundProfileId: retained?.scope.fundProfileId ?? fundProfileId?.trim() ?? "",
    entityScopeKind: retained?.scope.entityScopeKind ?? "AllEntities",
    entityId: retained?.scope.entityId ?? "",
    portfolioId: retained?.scope.portfolioId ?? "",
    investorId: retained?.scope.investorId ?? "",
    periodId: retained?.periodId ?? asOfDate.slice(0, 7),
    ledgerBookId: retained?.ledgerBook.ledgerBookId ?? "",
    ledgerBookCode: retained?.ledgerBook.ledgerBookCode ?? "Primary GL",
    accountingBasis: retained?.accountingBasis ?? "Gaap",
    presentationCurrency: retained?.presentationCurrency ?? "USD",
    consolidationLevel: retained?.consolidationLevel ?? "Fund",
    outputFormat: retained?.outputFormat ?? "Pdf",
    finality: retained?.finality ?? "Draft",
    includeSupportingSchedules: retained?.includeSupportingSchedules ?? true,
    includeEvidenceAppendix: retained?.includeEvidenceAppendix ?? true,
    templateParametersJson: JSON.stringify(retained?.templateParameters ?? {}, null, 2)
  };
}

export function validateAndBuildReportingRunParameters(
  draft: ReportRunParameterDraftState,
  asOfDate: string
): ReportingRunParameterValidation {
  const issues: string[] = [];
  const fundProfileId = draft.fundProfileId.trim();
  const periodId = draft.periodId.trim();
  const ledgerBookId = draft.ledgerBookId.trim();
  const ledgerBookCode = draft.ledgerBookCode.trim();
  const presentationCurrency = draft.presentationCurrency.trim().toUpperCase();
  const retainedAsOfDate = asOfDate.trim();

  if (!fundProfileId) {
    issues.push("Select or enter a fund profile.");
  }
  if (!periodId) {
    issues.push("Enter an accounting period ID.");
  }
  if (!retainedAsOfDate) {
    issues.push("Enter an as-of date.");
  }
  if (!ledgerBookId && !ledgerBookCode) {
    issues.push("Enter a ledger book ID or code.");
  }
  if (!presentationCurrency) {
    issues.push("Enter a presentation currency.");
  }
  if (draft.entityScopeKind === "Entity" && !draft.entityId.trim()) {
    issues.push("Enter the scoped entity ID.");
  }
  if (draft.entityScopeKind === "Portfolio" && !draft.portfolioId.trim()) {
    issues.push("Enter the scoped portfolio ID.");
  }
  if (draft.entityScopeKind === "Investor" && !draft.investorId.trim()) {
    issues.push("Enter the scoped investor ID.");
  }

  let templateParameters: Record<string, string> = {};
  try {
    const parsed = JSON.parse(draft.templateParametersJson.trim() || "{}");
    if (!parsed || Array.isArray(parsed) || typeof parsed !== "object") {
      issues.push("Template parameters must be a JSON object of string values.");
    } else if (Object.values(parsed).some((value) => typeof value !== "string")) {
      issues.push("Every template parameter value must be a string.");
    } else {
      templateParameters = Object.fromEntries(
        Object.entries(parsed)
          .filter(([key]) => key.trim().length > 0)
          .map(([key, value]) => [key.trim(), (value as string).trim()])
      );
    }
  } catch {
    issues.push("Template parameters must contain valid JSON.");
  }

  if (issues.length > 0) {
    return { parameters: null, issues };
  }

  return {
    parameters: {
      scope: {
        fundProfileId,
        entityScopeKind: draft.entityScopeKind,
        entityId: draft.entityScopeKind === "Entity" ? draft.entityId.trim() || null : null,
        portfolioId: draft.entityScopeKind === "Portfolio" ? draft.portfolioId.trim() || null : null,
        investorId: draft.entityScopeKind === "Investor" ? draft.investorId.trim() || null : null,
        dimensions: null
      },
      periodId,
      asOfDate: retainedAsOfDate,
      ledgerBook: {
        ledgerBookId: ledgerBookId || null,
        ledgerBookCode: ledgerBookCode || null
      },
      accountingBasis: draft.accountingBasis,
      presentationCurrency,
      consolidationLevel: draft.consolidationLevel,
      outputFormat: draft.outputFormat,
      finality: draft.finality,
      includeSupportingSchedules: draft.includeSupportingSchedules,
      includeEvidenceAppendix: draft.includeEvidenceAppendix,
      templateParameters
    },
    issues: []
  };
}

export function buildAuthoritativeReadinessGateViewState(
  readiness: ReportingRunReadiness,
  finality: ReportingFinality
): AuthoritativeReadinessGateViewState {
  const canRun = finality === "Final" ? readiness.canGenerateFinal : readiness.canGenerateDraft;
  const relevantBlockingChecks = readiness.checks.filter((check) =>
    check.status !== "Ready" && (finality === "Final" ? check.blocksFinal : check.blocksDraft));
  const blockingReasons = relevantBlockingChecks.map((check) => check.summary);

  if (canRun) {
    return {
      statusLabel: finality === "Final" ? "Final ready" : "Draft ready",
      summary: finality === "Final"
        ? "The server verified this exact template version and parameter set for final generation."
        : "The server verified this exact template version and parameter set for draft generation.",
      canRun: true,
      blockingReasons: []
    };
  }

  return {
    statusLabel: `${finality} blocked`,
    summary: `The server blocked this ${finality.toLowerCase()} run until the listed readiness issues are resolved.`,
    canRun: false,
    blockingReasons
  };
}

/**
 * Advisory-only readiness check. There is no backend endpoint that aggregates "open reconciliation
 * breaks + unposted journals for this report's exact fund/period" — this sums whatever
 * reconciliation-queue and manual-journal-draft data is already loaded client-side. It is a
 * heads-up for the operator, not a compliance-grade blocking gate; see the plan's cross-cutting
 * risk notes before treating a "clear" result as authoritative.
 */
export function buildReportRunReadinessGateViewState({
  reconciliationQueue,
  manualDrafts
}: {
  reconciliationQueue: AccountingWorkspaceResponse["reconciliationQueue"];
  manualDrafts: ManualJournalEntryDraft[];
}): ReadinessGateViewState {
  const openBreakCount = reconciliationQueue.reduce((sum, item) => sum + item.openBreakCount, 0);
  const unpostedJournalCount = manualDrafts.filter(
    (draft) => draft.status !== "Posted" && draft.status !== "Rejected" && draft.status !== "Reversed"
  ).length;

  const items: ReadinessGateItem[] = [
    {
      id: "open-breaks",
      label: "Open reconciliation breaks",
      count: openBreakCount,
      tone: openBreakCount > 0 ? "warning" : "success",
      href: openBreakCount > 0 ? WORKSTATION_ROUTE_CATALOG.accountingReconciliation : null,
      linkLabel: openBreakCount > 0 ? "Review breaks" : null
    },
    {
      id: "unposted-journals",
      label: "Unposted journal entries",
      count: unpostedJournalCount,
      tone: unpostedJournalCount > 0 ? "warning" : "success",
      href: unpostedJournalCount > 0 ? WORKSTATION_ROUTE_CATALOG.accountingJournalEntries : null,
      linkLabel: unpostedJournalCount > 0 ? "Review journal entries" : null
    }
  ];

  return {
    items,
    isClear: items.every((item) => item.count === 0),
    disclaimer: "Advisory only: this reflects accounting data already loaded in this workspace, not a fund/period-scoped compliance check. It does not block the run."
  };
}
