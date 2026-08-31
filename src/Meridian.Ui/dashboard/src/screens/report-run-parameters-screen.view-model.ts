import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type {
  AccountingWorkspaceResponse,
  LedgerDimensionSet,
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
  dimensionsJson: string;
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
    dimensionsJson: JSON.stringify(retained?.scope.dimensions ?? {}, null, 2),
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

  const dimensions = parseLedgerDimensions(draft.dimensionsJson, issues);
  if (
    dimensions?.fundId
    && fundProfileId
    && dimensions.fundId.localeCompare(fundProfileId, undefined, { sensitivity: "base" }) !== 0
  ) {
    issues.push("Ledger dimension fundId must match the selected fund profile.");
  }
  if (ledgerBookId && !guidPattern.test(ledgerBookId)) {
    issues.push("Ledger book ID must be a UUID.");
  }
  if (
    dimensions?.bookId
    && ledgerBookId
    && guidPattern.test(dimensions.bookId)
    && guidPattern.test(ledgerBookId)
    && dimensions.bookId.toLowerCase() !== ledgerBookId.toLowerCase()
  ) {
    issues.push("Ledger dimension bookId must match the selected ledger book ID.");
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
        dimensions
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

const ledgerDimensionScalarKeys = [
  "fundId",
  "entityId",
  "sleeveId",
  "strategyId",
  "investorId",
  "capitalAccountId",
  "instrumentId",
  "positionId",
  "taxLotId",
  "costCenterId",
  "counterpartyId",
  "organizationId",
  "portfolioId",
  "bookId",
  "accountId",
  "customerId",
  "vendorId",
  "projectId"
] as const satisfies ReadonlyArray<Exclude<keyof LedgerDimensionSet, "externalGlDimensions">>;

const ledgerDimensionKeys = new Set<string>([
  ...ledgerDimensionScalarKeys,
  "externalGlDimensions"
]);

const guidDimensionKeys = new Set<string>(["instrumentId", "positionId", "bookId"]);
const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function parseLedgerDimensions(
  json: string,
  issues: string[]
): LedgerDimensionSet | null {
  let parsed: unknown;
  try {
    parsed = JSON.parse(json.trim() || "{}");
  } catch {
    issues.push("Ledger dimensions must contain valid JSON.");
    return null;
  }

  if (!isPlainJsonObject(parsed)) {
    issues.push("Ledger dimensions must be a JSON object.");
    return null;
  }

  const unknownKeys = Object.keys(parsed).filter((key) => !ledgerDimensionKeys.has(key));
  if (unknownKeys.length > 0) {
    issues.push(`Unsupported ledger dimension field${unknownKeys.length === 1 ? "" : "s"}: ${unknownKeys.join(", ")}.`);
  }

  const dimensions: LedgerDimensionSet = {};
  for (const key of ledgerDimensionScalarKeys) {
    const value = parsed[key];
    if (value === undefined || value === null || value === "") {
      continue;
    }
    if (typeof value !== "string") {
      issues.push(`Ledger dimension ${key} must be a string or null.`);
      continue;
    }

    const normalized = value.trim();
    if (!normalized) {
      continue;
    }
    if (guidDimensionKeys.has(key) && !guidPattern.test(normalized)) {
      issues.push(`Ledger dimension ${key} must be a UUID.`);
      continue;
    }
    dimensions[key] = normalized;
  }

  const external = parsed.externalGlDimensions;
  if (external !== undefined && external !== null) {
    if (!isPlainJsonObject(external)) {
      issues.push("Ledger dimension externalGlDimensions must be a JSON object of string values.");
    } else {
      const normalizedExternalEntries: Array<[string, string]> = [];
      for (const [key, value] of Object.entries(external)) {
        if (!key.trim() || typeof value !== "string" || !value.trim()) {
          issues.push("Every external GL dimension key and value must be a non-empty string.");
          continue;
        }
        normalizedExternalEntries.push([key.trim(), value.trim()]);
      }
      if (normalizedExternalEntries.length > 0) {
        dimensions.externalGlDimensions = Object.fromEntries(normalizedExternalEntries);
      }
    }
  }

  return Object.keys(dimensions).length > 0 ? dimensions : null;
}

function isPlainJsonObject(value: unknown): value is Record<string, unknown> {
  return Boolean(value && typeof value === "object" && !Array.isArray(value));
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
 * Supplemental client-loaded readiness card. The authoritative server preflight above evaluates
 * the exact template, parameter set, ledger source, and reconciliation evidence for the requested
 * run. This card only summarizes reconciliation-queue and manual-journal data already loaded in
 * the browser, so it may add context but never weakens or replaces the server-owned decision.
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
