import { useCallback, useEffect, useMemo, useState } from "react";
import {
  getCapitalAccountWorkbench,
  type CapitalAccountWorkbenchQuery,
} from "@/lib/api";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import {
  formatCurrencyWithCode,
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

const defaultCapitalAccountWorkbenchServices: CapitalAccountWorkbenchServices = {
  getWorkbench: (query) => getCapitalAccountWorkbench(query)
};

export function useCapitalAccountWorkbenchViewModel(
  active: boolean,
  search = "",
  services: CapitalAccountWorkbenchServices = defaultCapitalAccountWorkbenchServices
): CapitalAccountWorkbenchViewModel {
  const [workbench, setWorkbench] = useState<CapitalAccountWorkbench | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ApiErrorDisplay | null>(null);
  const query = useMemo(() => parseCapitalAccountWorkbenchQuery(search), [search]);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setWorkbench(await services.getWorkbench(query));
    } catch (err) {
      setError(describeApiError(err, "Capital Account Workbench could not load."));
      setWorkbench(null);
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

function buildCapitalAccountWorkbenchView(workbench: CapitalAccountWorkbench | null): Omit<CapitalAccountWorkbenchViewModel, "loading" | "errorText" | "refresh"> {
  if (!workbench) {
    return {
      title: "Capital Account Workbench",
      description: "Investor-level capital account evidence, allocation rules, statement lineage, and audit drill-throughs load from Meridian private-capital workbench data.",
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

  return {
    title: "Capital Account Workbench",
    description: "Investor-level capital account evidence, allocation rules, statement and restatement lineage, and audit drill-throughs from Meridian private-capital workbench data.",
    statusLabel: workbench.statusLabel,
    statusTone: capitalAccountWorkbenchStatusTone(workbench.statusLabel),
    statusReason: workbench.statusReason,
    projectedAtLabel: `${formatDateTimeLabel(workbench.projectedAtUtc)} / ${workbench.currency}`,
    workbenchRouteLabel: workbench.workbenchRoute,
    emptyText: "No investor-level capital accounts matched the selected private-capital filters.",
    summaryCards: [
      {
        id: "investor-accounts",
        label: "Investor accounts",
        value: workbench.investorAccountCount.toLocaleString(),
        detail: `${formatCurrencyWithCode(workbench.netCapitalActivity, workbench.currency, true)} net activity`,
        tone: workbench.investorAccountCount > 0 ? "success" : "warning"
      },
      {
        id: "allocation-rules",
        label: "Allocation rules",
        value: workbench.allocationRules.length.toLocaleString(),
        detail: `${workbench.allocationRules.filter((item) => item.isSatisfied).length.toLocaleString()} satisfied`,
        tone: workbench.allocationRules.every((item) => item.isSatisfied) ? "success" : "warning"
      },
      {
        id: "statements",
        label: "Statements",
        value: workbench.statementCount.toLocaleString(),
        detail: `${workbench.restatementLineageCount.toLocaleString()} restatement lineage`,
        tone: workbench.statementCount > 0 ? "success" : "warning"
      },
      {
        id: "audit-drill-throughs",
        label: "Audit drill-throughs",
        value: workbench.auditDrillThroughCount.toLocaleString(),
        detail: `${workbench.auditDrillThroughs.filter((item) => item.isAvailable).length.toLocaleString()} available`,
        tone: workbench.auditDrillThroughs.every((item) => item.isAvailable) ? "success" : "warning"
      }
    ],
    investorAccounts: workbench.investorAccounts.map(buildCapitalAccountInvestorAccountRow),
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
    for (const record of account.fundEventRecords) {
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

function buildCapitalAccountInvestorAccountRow(
  account: CapitalAccountWorkbench["investorAccounts"][number]
): CapitalAccountWorkbenchInvestorAccountRowViewModel {
  const paymentEvidence = buildManualJournalPaymentIntentEvidenceView(
    account.paymentIntentEvidence,
    account.evidenceLinks,
    account.currency,
    account.netCapitalActivity,
    account.fundEventRecords[0]?.effectiveDate ?? null
  );

  return {
    id: account.accountKey,
    title: account.capitalAccountId,
    subtitle: `${account.investorId ?? "Unassigned investor"} / ${account.currency}`,
    statusLabel: account.readinessLabel || account.readiness,
    statusTone: capitalAccountReadinessTone(account.readiness),
    netActivityLabel: formatCurrencyWithCode(account.netCapitalActivity, account.currency, true),
    rollForwardLabel: `${formatCurrencyWithCode(account.openingNetActivity, account.currency, true)} opening -> ${formatCurrencyWithCode(account.endingNetActivity, account.currency, true)} ending`,
    activityMixLabel: [
      `Contributions ${formatCurrencyWithCode(account.contributions, account.currency)}`,
      `Distributions ${formatCurrencyWithCode(account.distributions, account.currency)}`,
      `Subscriptions ${formatCurrencyWithCode(account.subscriptions, account.currency)}`,
      `Redemptions ${formatCurrencyWithCode(account.redemptions, account.currency)}`,
      `Fees ${formatCurrencyWithCode(account.managementFees, account.currency)}`
    ].join(" / "),
    evidenceLabel: `${account.evidenceLinkCount.toLocaleString()} evidence / ${account.evidenceCategorySummary}`,
    eventLabel: `${account.fundEventCount.toLocaleString()} fund event(s) / ${account.postedFundEventCount.toLocaleString()} posted / ${account.publishedReportOutputCount.toLocaleString()} published`,
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
    accountLabel: `${rule.capitalAccountId} / ${rule.investorId ?? "Unassigned investor"}`,
    label: rule.label,
    statusLabel: rule.isSatisfied ? "Satisfied" : "Needs evidence",
    statusTone: rule.isSatisfied ? "success" : "warning",
    reason: rule.reason,
    basis: rule.basis,
    evidenceLabel: `${rule.evidenceLinkCount.toLocaleString()} evidence`,
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
    subtitle: `${lineage.reportOutputType} / ${lineage.capitalAccountId} / ${lineage.investorId ?? "Unassigned investor"}`,
    statusLabel: lineage.hasRestatementLineage ? "Restated" : lineage.isPublished ? "Published" : lineage.isReportReady ? "Ready" : "Review",
    statusTone: lineage.hasRestatementLineage ? "warning" : lineage.isPublished || lineage.isReportReady ? "success" : "warning",
    publicationLabel: [
      lineage.reportWorkflowState ?? "No workflow state",
      lineage.publishedBy ? `by ${lineage.publishedBy}` : null,
      lineage.publishedAtUtc ? formatDateTimeLabel(lineage.publishedAtUtc) : null
    ].filter(Boolean).join(" / "),
    provenanceLabel: `${lineage.reportLineProvenanceCount.toLocaleString()} provenance line(s)`,
    restatementLabel: lineage.hasRestatementLineage
      ? `${lineage.restatementReasonCode ?? "Restated"} / ${lineage.restatementChangedLineCount.toLocaleString()} changed line(s) / ${lineage.restatementEvidenceLinkCount.toLocaleString()} evidence`
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
    valueLabel: `${line.previousValue} -> ${line.currentValue}`,
    evidenceLabel: `${line.evidenceLinkCount.toLocaleString()} changed-line evidence`
  };
}

function formatCapitalAccountEffectiveWindow(from?: string | null, to?: string | null): string {
  if (from && to) {
    return `${from} -> ${to}`;
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
      : formatCurrencyWithCode(input.amount, input.currency ?? "USD", true);
    return `${input.kind} ${input.sourceId} ${amount}`;
  });
  const remaining = inputs.length > preview.length
    ? ` + ${inputs.length - preview.length} more`
    : "";
  return `${inputs.length.toLocaleString()} input(s): ${preview.join(" / ")}${remaining}`;
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
    evidenceLabel: `${drill.evidenceLinkCount.toLocaleString()} evidence`,
    routeLabel: drill.route ?? "No route",
    relatedLabel: drill.relatedIds.length > 0 ? drill.relatedIds.join(" / ") : "No related ids"
  };
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
