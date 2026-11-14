import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  getCapitalAccountWorkbench,
  type CapitalAccountWorkbenchQuery,
} from "@/lib/api";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import {
  formatDateTimeLabel,
} from "./accounting-screen.formatting";
import {
  manualJournalPrivateCapitalReadinessTone,
  normalizeQueryValue,
} from "./accounting-screen.view-model.shared";
import {
  buildManualJournalPaymentIntentEvidenceView,
  buildManualJournalPrivateCapitalFundEventLedgerRecordRow,
} from "./accounting-screen.journal-entries.view-model";
import type {
  AccountingConfigurationIssueViewModel,
  AccountingToolingTone,
  CapitalAccountWorkbenchAllocationRuleRowViewModel,
  CapitalAccountWorkbenchAuditDrillThroughRowViewModel,
  CapitalAccountWorkbenchFundEventCommandRowViewModel,
  CapitalAccountWorkbenchInvestorAccountRowViewModel,
  CapitalAccountWorkbenchRestatementChangedLineRowViewModel,
  CapitalAccountWorkbenchServices,
  CapitalAccountWorkbenchStatementLineageRowViewModel,
  CapitalAccountWorkbenchViewModel,
} from "./accounting-screen.view-model";
import type {
  CapitalAccountWorkbench,
} from "@/types";
import { formatCount, formatCurrencyWithCode } from "@/lib/format";
import { formatMoney } from "@/components/accounting/money";
import {
  proveRollforward,
  rollforwardVerdict,
  usableAmount,
  type RollforwardVerdict,
} from "@/components/accounting/proof-arithmetic";

/** One movement in an investor capital account rollforward; the amount stays numeric. */
export interface CapitalAccountRollforwardMovementViewModel {
  id: string;
  label: string;
  /** Signed net capital activity. `null` when the wire value is not a usable number. */
  value: number | null;
  note?: string;
}

/**
 * Numeric rollforward for one investor capital account: opening net activity, the movements
 * that explain the change, and the stated ending. Amounts are carried as numbers so the
 * RollforwardTrace can prove the arithmetic instead of re-parsing `rollForwardLabel`.
 */
export interface CapitalAccountRollforwardViewModel {
  openingLabel: string;
  closingLabel: string;
  opening: number | null;
  closing: number | null;
  currency: string;
  movements: CapitalAccountRollforwardMovementViewModel[];
  /** The workbench never supplies an independently stated opening, so the trace says how it arose. */
  openingDerivation: string;
  /**
   * Set when the account has no subledger entries. The one net movement then restates the
   * ending, so agreement proves nothing and the trace says "Not proved" with this sentence.
   */
  unsupportedReason?: string;
  /**
   * What the proof says, computed by the same arithmetic the trace renders. `unsupported` is
   * the no-entry case that agrees: it is never reported as `tied`.
   */
  verdict: RollforwardVerdict;
  /** Short verdict for the disclosure summary, e.g. "Ties", "Not itemised" or "Out by $20.00". */
  verdictLabel: string;
}

/** Why an account with no subledger entries cannot be proved. Completes "Not proved — …". */
export const CAPITAL_ACCOUNT_ROLLFORWARD_NOT_ITEMISED_REASON =
  "no subledger entries are loaded for this account, so the one net movement restates the ending instead of explaining it.";

const CAPITAL_ACCOUNT_ROLLFORWARD_VERDICT_LABEL: Record<Exclude<RollforwardVerdict, "out">, string> = {
  tied: "Ties",
  unsupported: "Not itemised",
  indeterminate: "Not provable"
};

export interface CapitalAccountWorkbenchInvestorAccountProofRowViewModel
  extends CapitalAccountWorkbenchInvestorAccountRowViewModel {
  rollforward: CapitalAccountRollforwardViewModel;
}

/** The Capital Account Workbench view model with a numeric rollforward on every investor row. */
export interface CapitalAccountWorkbenchProofViewModel extends Omit<CapitalAccountWorkbenchViewModel, "investorAccounts"> {
  investorAccounts: CapitalAccountWorkbenchInvestorAccountProofRowViewModel[];
}

const defaultCapitalAccountWorkbenchServices: CapitalAccountWorkbenchServices = {
  getWorkbench: (query) => getCapitalAccountWorkbench(query)
};

export function useCapitalAccountWorkbenchViewModel(
  active: boolean,
  search = "",
  services: CapitalAccountWorkbenchServices = defaultCapitalAccountWorkbenchServices
): CapitalAccountWorkbenchProofViewModel {
  const [loadedWorkbench, setLoadedWorkbench] = useState<{
    query: CapitalAccountWorkbenchQuery;
    workbench: CapitalAccountWorkbench;
  } | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ApiErrorDisplay | null>(null);
  const query = useMemo(() => parseCapitalAccountWorkbenchQuery(search), [search]);
  const refreshRevision = useRef(0);
  // A workbench is only shown for the scope it was requested for, so a changed fund or
  // period never renders the previous scope's capital accounts while its own load runs.
  const workbench = loadedWorkbench?.query === query ? loadedWorkbench.workbench : null;

  const refresh = useCallback(async () => {
    // Only the latest request may apply its outcome; a slower response for an earlier
    // scope must not replace the current one.
    const requestRevision = ++refreshRevision.current;
    const isCurrent = () => refreshRevision.current === requestRevision;
    setLoading(true);
    setError(null);
    try {
      const next = await services.getWorkbench(query);
      if (isCurrent()) {
        setLoadedWorkbench({ query, workbench: next });
      }
    } catch (err) {
      if (isCurrent()) {
        setError(describeApiError(err, "Capital Account Workbench could not load."));
        setLoadedWorkbench(null);
      }
    } finally {
      if (isCurrent()) {
        setLoading(false);
      }
    }
  }, [query, services]);

  useEffect(() => {
    if (!active) {
      return;
    }

    void refresh();
  }, [active, refresh]);

  return useMemo(() => ({
    ...buildCapitalAccountWorkbenchView(workbench),
    loading,
    errorText: error?.summary ?? null,
    refresh
  }), [error?.summary, loading, refresh, workbench]);
}

function parseCapitalAccountWorkbenchQuery(search: string): CapitalAccountWorkbenchQuery {
  const params = new URLSearchParams(search.startsWith("?") ? search.slice(1) : search);
  return {
    fundProfileId: normalizeQueryValue(params.get("fundProfileId")),
    ledgerBookId: normalizeQueryValue(params.get("ledgerBookId")),
    fundEventId: normalizeQueryValue(params.get("fundEventId")),
    capitalAccountId: normalizeQueryValue(params.get("capitalAccountId")),
    investorId: normalizeQueryValue(params.get("investorId")),
    currency: normalizeQueryValue(params.get("currency"))
  };
}

function buildCapitalAccountWorkbenchView(workbench: CapitalAccountWorkbench | null): Omit<CapitalAccountWorkbenchProofViewModel, "loading" | "errorText" | "refresh"> {
  if (!workbench) {
    return {
      title: "Capital Account Workbench",
      description: "Investor-level capital account evidence, allocation rules, statement lineage, and audit drill-throughs load from Meridian private-capital workbench data.",
      available: false,
      statusLabel: "Not loaded",
      statusTone: "default",
      statusReason: "Capital-account workbench data has not loaded yet.",
      projectedAtLabel: "Not loaded",
      workbenchRouteLabel: "No workbench route",
      emptyText: "No capital-account workbench data has loaded yet.",
      summaryCards: [
        { id: "investor-accounts", label: "Investor accounts", value: "0", detail: "No investor account rows loaded", tone: "default" },
        { id: "allocation-rules", label: "Allocation rules", value: "0", detail: "No allocation evidence checks loaded", tone: "default" },
        { id: "statements", label: "Statements", value: "0", detail: "No statement lineage loaded", tone: "default" },
        { id: "audit-drill-throughs", label: "Audit drill-throughs", value: "0", detail: "No drill-through targets loaded", tone: "default" }
      ],
      investorAccounts: [],
      allocationRules: [],
      statementLineage: [],
      auditDrillThroughs: [],
      fundEventCommandRows: [],
      validationIssues: [],
      liveCapabilities: [],
      plannedCapabilities: []
    };
  }

  const hasOperatorRows = workbench.investorAccounts.length > 0
    || workbench.allocationRules.length > 0
    || workbench.statementLineage.length > 0
    || workbench.auditDrillThroughs.length > 0;
  const hasInvestorAccounts = workbench.investorAccounts.length > 0;
  const investorAccounts = workbench.investorAccounts.map(buildCapitalAccountInvestorAccountRow);
  const cashSupportReviewCount = investorAccounts.filter((row) => row.paymentEvidenceTone !== "success").length;
  const requiresCashSupportReview = cashSupportReviewCount > 0;

  return {
    title: "Capital Account Workbench",
    description: "Investor-level capital account evidence, allocation rules, statement and restatement lineage, and audit drill-throughs from Meridian private-capital workbench data.",
    available: true,
    statusLabel: hasOperatorRows
      ? !hasInvestorAccounts || requiresCashSupportReview ? "Review required" : workbench.statusLabel
      : "No activity loaded",
    statusTone: hasOperatorRows
      ? !hasInvestorAccounts || requiresCashSupportReview ? "warning" : capitalAccountWorkbenchStatusTone(workbench.statusLabel)
      : "warning",
    statusReason: hasOperatorRows
      ? !hasInvestorAccounts
        ? "Supporting workbench rows loaded, but no investor capital accounts matched this scope. Select or create an investor capital account before treating the workbench as ready."
        : requiresCashSupportReview
        ? `${cashSupportReviewCount} investor capital account${cashSupportReviewCount === 1 ? " requires" : "s require"} cash-support evidence review before the workbench is ready.`
        : workbench.statusReason
      : "The workbench request succeeded, but no investor accounts, allocation evidence, statement lineage, or audit routes matched this scope.",
    projectedAtLabel: `${formatDateTimeLabel(workbench.projectedAtUtc)} / ${workbench.currency}`,
    workbenchRouteLabel: workbench.workbenchRoute,
    emptyText: "No investor-level capital activity matched this scope. Create or select a private-capital journal event, or adjust the fund and investor scope.",
    summaryCards: [
      {
        id: "investor-accounts",
        label: "Investor accounts",
        value: formatCount(workbench.investorAccountCount),
        detail: `${formatCurrencyWithCode(workbench.netCapitalActivity, workbench.currency, { signed: true })} net activity`,
        tone: workbench.investorAccountCount > 0 ? "success" : "warning"
      },
      {
        id: "allocation-rules",
        label: "Allocation rules",
        value: formatCount(workbench.allocationRules.length),
        detail: `${formatCount(workbench.allocationRules.filter((item) => item.isSatisfied).length)} satisfied`,
        tone: workbench.allocationRules.length > 0 && workbench.allocationRules.every((item) => item.isSatisfied) ? "success" : "warning"
      },
      {
        id: "statements",
        label: "Statements",
        value: formatCount(workbench.statementCount),
        detail: `${formatCount(workbench.restatementLineageCount)} restatement lineage`,
        tone: workbench.statementCount > 0 ? "success" : "warning"
      },
      {
        id: "audit-drill-throughs",
        label: "Audit drill-throughs",
        value: formatCount(workbench.auditDrillThroughCount),
        detail: `${formatCount(workbench.auditDrillThroughs.filter((item) => item.isAvailable).length)} available`,
        tone: workbench.auditDrillThroughs.length > 0 && workbench.auditDrillThroughs.every((item) => item.isAvailable) ? "success" : "warning"
      }
    ],
    investorAccounts,
    allocationRules: workbench.allocationRules.map(buildCapitalAccountAllocationRuleRow),
    statementLineage: workbench.statementLineage.map(buildCapitalAccountStatementLineageRow),
    auditDrillThroughs: workbench.auditDrillThroughs.map(buildCapitalAccountAuditDrillThroughRow),
    fundEventCommandRows: buildCapitalAccountFundEventCommandRows(workbench),
    validationIssues: workbench.validationIssues.map<AccountingConfigurationIssueViewModel>((issue, index) => ({
      id: `${issue.code}-${index}`,
      label: issue.code,
      message: issue.message,
      detail: issue.suggestedAction ?? issue.targetId ?? "Review the capital-account workbench.",
      tone: issue.severity === "Critical" ? "danger" : issue.severity === "Warning" ? "warning" : "default"
    })),
    liveCapabilities: workbench.liveCapabilities,
    plannedCapabilities: workbench.plannedCapabilities
  };
}

function buildCapitalAccountFundEventCommandRows(
  workbench: CapitalAccountWorkbench
): CapitalAccountWorkbenchFundEventCommandRowViewModel[] {
  const seen = new Set<string>();
  const rows: CapitalAccountWorkbenchFundEventCommandRowViewModel[] = [];

  for (const account of workbench.investorAccounts) {
    for (const record of account.fundEventRecords ?? []) {
      if (seen.has(record.fundEventId)) {
        continue;
      }

      seen.add(record.fundEventId);
      rows.push(buildManualJournalPrivateCapitalFundEventLedgerRecordRow(
        record,
        workbench.fundProfileId,
        workbench.ledgerBookId
      ));
    }
  }

  return rows;
}

/**
 * Build the numeric rollforward for one investor capital account.
 *
 * Movements are the account's subledger entries (one per entry, in wire order), each carrying
 * its own `netCapitalActivity`. With no entries the trace shows one "Net capital activity"
 * movement. The contributions / distributions / subscriptions / redemptions / fees buckets are
 * deliberately not used: their signs are not guaranteed to sum to the net.
 *
 * The opening is not an independently stated balance. The private-capital projection derives it
 * from the first subledger entry's running net activity (zero when there are no entries), so the
 * trace labels it as derived rather than presenting it as a bare opening balance.
 *
 * With no entries the three figures are not independent at all: the projection reports a zero
 * opening and the same number as both the net activity and the ending, so they agree for every
 * value. That agreement is reported as `unsupported` ("Not itemised"), never as a passed proof.
 * An out-by or an unusable amount in that case is still reported as such.
 */
export function buildCapitalAccountRollforward(
  account: CapitalAccountWorkbench["investorAccounts"][number]
): CapitalAccountRollforwardViewModel {
  const entries = account.subledgerEntries ?? [];
  const movements: CapitalAccountRollforwardMovementViewModel[] = entries.length > 0
    ? entries.map((entry, index) => ({
      id: entry.subledgerEntryId || `${account.accountKey}:entry:${index}`,
      label: entry.fundEventType || entry.fundEventId || "Capital movement",
      value: usableAmount(entry.netCapitalActivity),
      note: [entry.effectiveDate, entry.isPosted === false ? "not posted" : null].filter(Boolean).join(" · ") || undefined
    }))
    : [{
      id: `${account.accountKey}:net-capital-activity`,
      label: "Net capital activity",
      value: usableAmount(account.netCapitalActivity)
    }];
  const openingLabel = "Opening net activity";
  const closingLabel = "Ending net activity";
  const opening = usableAmount(account.openingNetActivity);
  // A null ending stays null: the workbench always states one, so a missing value is a gap.
  const closing = usableAmount(account.endingNetActivity);
  const itemised = entries.length > 0;
  const proof = proveRollforward({ openingLabel, opening, movements, closingLabel, closing });
  const verdict = rollforwardVerdict(proof, { independent: itemised });

  return {
    openingLabel,
    closingLabel,
    opening,
    closing,
    currency: account.currency,
    movements,
    openingDerivation: itemised
      ? "Opening is derived by the capital-account projection from the subledger's running net activity; it is not a separately stated opening balance."
      : "Opening is set by the capital-account projection with no subledger entries behind it; it is not a separately stated opening balance.",
    ...(itemised ? {} : { unsupportedReason: CAPITAL_ACCOUNT_ROLLFORWARD_NOT_ITEMISED_REASON }),
    verdict,
    verdictLabel: verdict === "out"
      ? `Out by ${formatMoney(Math.abs(proof.difference ?? 0), { currency: account.currency })}`
      : CAPITAL_ACCOUNT_ROLLFORWARD_VERDICT_LABEL[verdict]
  };
}

function buildCapitalAccountInvestorAccountRow(
  account: CapitalAccountWorkbench["investorAccounts"][number]
): CapitalAccountWorkbenchInvestorAccountProofRowViewModel {
  const paymentEvidence = buildManualJournalPaymentIntentEvidenceView(
    account.paymentIntentEvidence,
    account.evidenceLinks,
    account.currency,
    account.netCapitalActivity,
    account.fundEventRecords?.[0]?.effectiveDate ?? null
  );

  return {
    id: account.accountKey,
    title: `${formatCapitalAccountPartyLabel(account.investorId)} capital account`,
    rollforward: buildCapitalAccountRollforward(account),
    subtitle: `${formatCapitalAccountPartyLabel(account.investorId)} / ${account.currency}`,
    statusLabel: paymentEvidence.tone === "success" ? account.readinessLabel || account.readiness : "Cash support review",
    statusTone: paymentEvidence.tone === "success" ? capitalAccountReadinessTone(account.readiness) : "warning",
    netActivityLabel: formatCurrencyWithCode(account.netCapitalActivity, account.currency, { signed: true }),
    rollForwardLabel: `${formatCurrencyWithCode(account.openingNetActivity, account.currency, { signed: true })} opening → ${formatCurrencyWithCode(account.endingNetActivity, account.currency, { signed: true })} ending`,
    activityMixLabel: [
      `Contributions ${formatCurrencyWithCode(account.contributions, account.currency)}`,
      `Distributions ${formatCurrencyWithCode(account.distributions, account.currency)}`,
      `Subscriptions ${formatCurrencyWithCode(account.subscriptions, account.currency)}`,
      `Redemptions ${formatCurrencyWithCode(account.redemptions, account.currency)}`,
      `Fees ${formatCurrencyWithCode(account.managementFees, account.currency)}`
    ].join(" / "),
    evidenceLabel: `${formatCount(account.evidenceLinkCount)} evidence / ${account.evidenceCategorySummary}`,
    eventLabel: `${formatCount(account.fundEventCount)} fund event(s) / ${formatCount(account.postedFundEventCount)} posted / ${formatCount(account.publishedReportOutputCount)} published`,
    paymentEvidenceLabel: paymentEvidence.label,
    paymentEvidenceTone: paymentEvidence.tone,
    paymentEvidenceSummaryLabel: paymentEvidence.summary,
    paymentEvidenceRequiredLabel: paymentEvidence.required,
    routeLabel: account.activityRoute
  };
}

function buildCapitalAccountAllocationRuleRow(
  rule: CapitalAccountWorkbench["allocationRules"][number]
): CapitalAccountWorkbenchAllocationRuleRowViewModel {
  const inputs = rule.inputs ?? [];
  const relatedFundEventIds = rule.relatedFundEventIds ?? [];
  return {
    id: rule.ruleId,
    accountLabel: `${formatCapitalAccountPartyLabel(rule.investorId)} capital account`,
    label: rule.label,
    statusLabel: rule.isSatisfied ? "Satisfied" : "Needs evidence",
    statusTone: rule.isSatisfied ? "success" : "warning",
    reason: rule.reason,
    basis: rule.basis,
    evidenceLabel: `${formatCount(rule.evidenceLinkCount)} evidence`,
    routeLabel: rule.route ?? "No route",
    requiredLabel: rule.requiredEvidence.length > 0 ? rule.requiredEvidence.join(" / ") : "No explicit evidence requirement",
    policyLabel: rule.ruleVersion ?? "No policy version",
    effectiveWindowLabel: formatCapitalAccountEffectiveWindow(rule.effectiveFrom, rule.effectiveTo),
    formulaLabel: rule.formula ?? "No allocation formula",
    approvalLabel: [
      rule.approvalState ?? "No approval state",
      rule.approvalReference ?? null
    ].filter(Boolean).join(" / "),
    traceLabel: rule.replayTrace ?? "No allocation replay trace",
    inputSummaryLabel: formatAllocationInputSummary(inputs),
    relatedFundEventLabel: relatedFundEventIds.length > 0 ? relatedFundEventIds.join(" / ") : "No related fund events"
  };
}

function buildCapitalAccountStatementLineageRow(
  lineage: CapitalAccountWorkbench["statementLineage"][number]
): CapitalAccountWorkbenchStatementLineageRowViewModel {
  const changedLineRows = (lineage.restatementChangedLines ?? []).map(buildCapitalAccountRestatementChangedLineRow);
  return {
    id: lineage.lineageId,
    title: lineage.displayName,
    subtitle: `${formatCapitalAccountPartyLabel(lineage.investorId)} capital account statement`,
    statusLabel: lineage.hasRestatementLineage ? "Restated" : lineage.isPublished ? "Published" : lineage.isReportReady ? "Ready" : "Review",
    statusTone: lineage.hasRestatementLineage ? "warning" : lineage.isPublished || lineage.isReportReady ? "success" : "warning",
    publicationLabel: [
      lineage.reportWorkflowState ?? "No workflow state",
      lineage.publishedBy ? `by ${lineage.publishedBy}` : null,
      lineage.publishedAtUtc ? formatDateTimeLabel(lineage.publishedAtUtc) : null
    ].filter(Boolean).join(" / "),
    provenanceLabel: `${formatCount(lineage.reportLineProvenanceCount)} provenance line(s)`,
    restatementLabel: lineage.hasRestatementLineage
      ? `${lineage.restatementReasonCode ?? "Restated"} / ${formatCount(lineage.restatementChangedLineCount)} changed line(s) / ${formatCount(lineage.restatementEvidenceLinkCount)} evidence`
      : lineage.restatementStatus,
    manifestLabel: [lineage.publicationManifestId, lineage.retainedManifestPath, lineage.publicationEvidenceHash].filter(Boolean).join(" / ") || "No retained manifest metadata",
    routeLabel: lineage.reportOutputRoute ?? lineage.reportRoute,
    changedLineRows
  };
}

function buildCapitalAccountRestatementChangedLineRow(
  line: NonNullable<CapitalAccountWorkbench["statementLineage"][number]["restatementChangedLines"]>[number]
): CapitalAccountWorkbenchRestatementChangedLineRowViewModel {
  return {
    id: line.lineKey,
    lineKey: line.lineKey,
    valueLabel: `${line.previousValue} → ${line.currentValue}`,
    evidenceLabel: `${formatCount(line.evidenceLinkCount)} changed-line evidence`
  };
}

function formatCapitalAccountEffectiveWindow(from?: string | null, to?: string | null): string {
  if (from && to) {
    return `${from} → ${to}`;
  }

  if (from) {
    return `From ${from}`;
  }

  if (to) {
    return `Through ${to}`;
  }

  return "No effective window";
}

function formatAllocationInputSummary(
  inputs: NonNullable<CapitalAccountWorkbench["allocationRules"][number]["inputs"]>
): string {
  if (inputs.length === 0) {
    return "No allocation inputs";
  }

  const preview = inputs.slice(0, 3).map((input) => {
    const amount = input.amount == null
      ? "no amount"
      : formatCurrencyWithCode(input.amount, input.currency ?? "USD", { signed: true });
    return `${input.kind} ${input.sourceId} ${amount}`;
  });
  const remaining = inputs.length > preview.length
    ? ` + ${inputs.length - preview.length} more`
    : "";
  return `${formatCount(inputs.length)} input(s): ${preview.join(" / ")}${remaining}`;
}

function buildCapitalAccountAuditDrillThroughRow(
  drill: CapitalAccountWorkbench["auditDrillThroughs"][number]
): CapitalAccountWorkbenchAuditDrillThroughRowViewModel {
  return {
    id: drill.drillThroughId,
    kind: drill.kind,
    title: drill.label,
    summary: drill.summary,
    statusLabel: drill.isAvailable ? "Available" : "Missing route",
    statusTone: drill.isAvailable ? "success" : "warning",
    evidenceLabel: `${formatCount(drill.evidenceLinkCount)} evidence`,
    routeLabel: drill.route ?? "No route",
    relatedLabel: drill.relatedIds.length > 0
      ? `${drill.relatedIds.length} related record${drill.relatedIds.length === 1 ? "" : "s"}`
      : "No related records"
  };
}

function formatCapitalAccountPartyLabel(value: string | null | undefined): string {
  const token = value?.trim().split(/[:/]/).filter(Boolean).pop();
  if (!token) {
    return "Unassigned investor";
  }

  return token
    .split(/[-_\s]+/)
    .filter(Boolean)
    .map((part) => part.toLowerCase() === "lp" ? "LP" : part.replace(/^\w/, (character) => character.toUpperCase()))
    .join(" ");
}

function capitalAccountWorkbenchStatusTone(statusLabel: string): AccountingToolingTone {
  const normalized = statusLabel.toLowerCase();
  if (normalized.includes("blocked")) {
    return "danger";
  }

  if (normalized.includes("ready") || normalized.includes("restated")) {
    return "success";
  }

  if (normalized.includes("review") || normalized.includes("missing") || normalized.includes("no capital")) {
    return "warning";
  }

  return "default";
}

function capitalAccountReadinessTone(readiness: CapitalAccountWorkbench["investorAccounts"][number]["readiness"]): AccountingToolingTone {
  const tone = manualJournalPrivateCapitalReadinessTone(readiness);
  return tone === "outline" ? "default" : tone;
}
