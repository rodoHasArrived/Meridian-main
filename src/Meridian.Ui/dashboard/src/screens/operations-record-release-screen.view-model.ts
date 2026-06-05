import { evidenceWorkbenchPath, WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type {
  OperationsContinuityScreenViewModel,
  OperationsContinuityTone
} from "@/screens/operations-continuity-screen.view-model";
import type {
  ReportingPackTargetRow,
  ReportingRunStatusRow,
  ReportingScreenViewModel,
  ReportingTemplateRow
} from "@/screens/reporting-screen.view-model";
import type { DataWorkspaceResponse } from "@/types";

export type OperationsRecordReleaseTone = "ready" | "review" | "blocked" | "neutral";
export type OperationsRecordReleaseBadgeVariant = "success" | "warning" | "danger" | "outline";

export interface OperationsRecordReleaseMetric {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: OperationsRecordReleaseTone;
}

export interface OperationsRecordReleaseStep {
  id: string;
  label: string;
  detail: string;
  href: string;
  routeLabel: string;
  statusLabel: string;
  tone: OperationsRecordReleaseTone;
  badgeVariant: OperationsRecordReleaseBadgeVariant;
  ariaLabel: string;
}

export interface OperationsRecordReleaseEvidenceRow {
  id: string;
  label: string;
  value: string;
  detail: string;
  href: string | null;
  routeLabel: string;
  tone: OperationsRecordReleaseTone;
  badgeVariant: OperationsRecordReleaseBadgeVariant;
  ariaLabel: string;
}

export interface OperationsRecordReleasePanel {
  title: string;
  description: string;
  statusLabel: string;
  tone: OperationsRecordReleaseTone;
  badgeVariant: OperationsRecordReleaseBadgeVariant;
  primaryHref: string;
  primaryLabel: string;
  primaryAriaLabel: string;
  rows: OperationsRecordReleaseEvidenceRow[];
  emptyText: string;
}

export interface OperationsRecordReleaseViewModel {
  title: string;
  eyebrow: string;
  summary: string;
  statusLabel: string;
  statusDetail: string;
  tone: OperationsRecordReleaseTone;
  badgeVariant: OperationsRecordReleaseBadgeVariant;
  rootAreaSummary: string;
  routePathLabel: string;
  metrics: OperationsRecordReleaseMetric[];
  stepsLabel: string;
  steps: OperationsRecordReleaseStep[];
  sourcePanel: OperationsRecordReleasePanel;
  accountingPanel: OperationsRecordReleasePanel;
  reportPackPanel: OperationsRecordReleasePanel;
  distributionLabel: string;
  distributions: ReportingPackTargetRow[];
  distributionEmptyText: string;
  templateLabel: string;
  templates: ReportingTemplateRow[];
  templateEmptyText: string;
  recentRunsLabel: string;
  recentRuns: ReportingRunStatusRow[];
  recentRunsEmptyText: string;
}

export interface BuildOperationsRecordReleaseViewModelOptions {
  data: DataWorkspaceResponse | null;
  continuity: OperationsContinuityScreenViewModel;
  reporting: ReportingScreenViewModel;
}

export function buildOperationsRecordReleaseViewModel({
  data,
  continuity,
  reporting
}: BuildOperationsRecordReleaseViewModelOptions): OperationsRecordReleaseViewModel {
  const source = buildSourcePanel(data);
  const accounting = buildAccountingPanel(continuity);
  const reportPack = buildReportPackPanel(continuity, reporting);
  const steps = buildReleaseSteps({ source, continuity, accounting, reportPack });
  const reviewCount = steps.filter((step) => step.tone === "review").length;
  const blockedCount = steps.filter((step) => step.tone === "blocked").length;
  const neutralCount = steps.filter((step) => step.tone === "neutral").length;
  const tone = blockedCount > 0
    ? "blocked"
    : reviewCount > 0 || neutralCount > 0
      ? "review"
      : "ready";
  const statusLabel = tone === "ready"
    ? "Release ready"
    : blockedCount > 0
      ? "Release blocked"
      : "Release review";

  return {
    title: "Operations record release",
    eyebrow: "W1-W5 release path",
    summary:
      "One operator path ties source data, retained accounting-record evidence, close controls, and governed report packs without adding another root workspace.",
    statusLabel,
    statusDetail: buildStatusDetail(blockedCount, reviewCount, neutralCount),
    tone,
    badgeVariant: toneToBadgeVariant(tone),
    rootAreaSummary: "Root navigation remains Trading, Portfolio, Accounting, Reporting, Strategy, Data, and Settings.",
    routePathLabel: `${WORKSTATION_ROUTE_CATALOG.dataProviders} -> ${WORKSTATION_ROUTE_CATALOG.accountingOperationsContinuity} -> ${WORKSTATION_ROUTE_CATALOG.reportingReportPacks}`,
    metrics: [
      {
        id: "source",
        label: "Source data",
        value: source.statusLabel,
        detail: source.description,
        tone: source.tone
      },
      {
        id: "accounting-record",
        label: "Accounting record",
        value: accounting.statusLabel,
        detail: accounting.description,
        tone: accounting.tone
      },
      {
        id: "report-pack",
        label: "Report pack",
        value: reportPack.statusLabel,
        detail: reportPack.description,
        tone: reportPack.tone
      },
      {
        id: "root-areas",
        label: "Root areas",
        value: "7",
        detail: "The release route is nested under Reporting, not exposed as a new root.",
        tone: "ready"
      }
    ],
    stepsLabel: "Operations record demo path",
    steps,
    sourcePanel: source,
    accountingPanel: accounting,
    reportPackPanel: reportPack,
    distributionLabel: "Report-pack distributions",
    distributions: reporting.packTargets,
    distributionEmptyText: reporting.packTargetsEmptyState.text,
    templateLabel: "Governed templates",
    templates: reporting.templateRows,
    templateEmptyText: "No governed report templates are loaded for this release path.",
    recentRunsLabel: "Recent report runs",
    recentRuns: reporting.runStatusRows,
    recentRunsEmptyText: "No recent report runs are loaded for this release path."
  };
}

function buildStatusDetail(blockedCount: number, reviewCount: number, neutralCount: number): string {
  if (blockedCount > 0) {
    return `${blockedCount} release step${blockedCount === 1 ? "" : "s"} blocked; keep the demo path visible but do not call it closed.`;
  }

  const attentionCount = reviewCount + neutralCount;
  if (attentionCount > 0) {
    return `${attentionCount} release step${attentionCount === 1 ? "" : "s"} still need review before the W1-W5 path can be presented as closed.`;
  }

  return "All release steps are ready for the source-data to accounting-record to report-pack demo.";
}

function buildSourcePanel(data: DataWorkspaceResponse | null): OperationsRecordReleasePanel {
  if (!data) {
    return {
      title: "Source data",
      description: "Source data payload is unavailable; the release path must fail closed until provider posture loads.",
      statusLabel: "Payload pending",
      tone: "neutral",
      badgeVariant: "outline",
      primaryHref: WORKSTATION_ROUTE_CATALOG.dataProviders,
      primaryLabel: "Open Data providers",
      primaryAriaLabel: "Open Data providers for source data posture",
      rows: [],
      emptyText: "No source-data evidence is loaded."
    };
  }

  const providerSummary = summarizeProviderPosture(data);
  const backfillReviewCount = data.backfills.filter((backfill) => backfill.status === "Review").length;
  const exportReadyCount = data.exports.filter((record) => record.status === "Ready").length;

  return {
    title: "Source data",
    description: providerSummary.detail,
    statusLabel: providerSummary.statusLabel,
    tone: providerSummary.tone,
    badgeVariant: toneToBadgeVariant(providerSummary.tone),
    primaryHref: WORKSTATION_ROUTE_CATALOG.dataProviders,
    primaryLabel: "Open Data providers",
    primaryAriaLabel: "Open Data providers for source data posture",
    rows: [
      {
        id: "providers",
        label: "Provider posture",
        value: providerSummary.value,
        detail: providerSummary.detail,
        href: WORKSTATION_ROUTE_CATALOG.dataProviders,
        routeLabel: "Data providers",
        tone: providerSummary.tone,
        badgeVariant: toneToBadgeVariant(providerSummary.tone),
        ariaLabel: `Provider posture ${providerSummary.statusLabel}: ${providerSummary.value}`
      },
      {
        id: "backfills",
        label: "Backfill evidence",
        value: `${data.backfills.length} job${data.backfills.length === 1 ? "" : "s"}`,
        detail: backfillReviewCount > 0
          ? `${backfillReviewCount} backfill job${backfillReviewCount === 1 ? "" : "s"} require review before downstream reporting.`
          : "Backfill queue has no review-state jobs in the loaded payload.",
        href: WORKSTATION_ROUTE_CATALOG.dataBackfills,
        routeLabel: "Backfill queues",
        tone: backfillReviewCount > 0 ? "review" : "ready",
        badgeVariant: toneToBadgeVariant(backfillReviewCount > 0 ? "review" : "ready"),
        ariaLabel: `${data.backfills.length} source-data backfill jobs`
      },
      {
        id: "exports",
        label: "Publish data",
        value: `${exportReadyCount}/${data.exports.length} ready`,
        detail: data.exports.length > 0
          ? "Export rows show whether source data has a publishable handoff."
          : "No data export rows are loaded for the release path.",
        href: WORKSTATION_ROUTE_CATALOG.data,
        routeLabel: "Data workspace",
        tone: data.exports.length === 0 ? "review" : "ready",
        badgeVariant: toneToBadgeVariant(data.exports.length === 0 ? "review" : "ready"),
        ariaLabel: `${exportReadyCount} of ${data.exports.length} data exports ready`
      }
    ],
    emptyText: "No source-data evidence is loaded."
  };
}

function buildAccountingPanel(continuity: OperationsContinuityScreenViewModel): OperationsRecordReleasePanel {
  const summary = continuity.accountingRecordSummary;
  const recordId = recordIdFromSummary(summary.recordIdLabel);
  const href = recordId
    ? evidenceWorkbenchPath("accounting-record", recordId)
    : WORKSTATION_ROUTE_CATALOG.accountingOperationsContinuity;
  const rows: OperationsRecordReleaseEvidenceRow[] = continuity.accountingRecordEvidence.map((row) => ({
    id: row.id,
    label: row.label,
    value: row.statusLabel,
    detail: `${row.detail} Requires ${row.requiredEvidenceLabel}.`,
    href: row.routeHref ?? href,
    routeLabel: row.routeHref ? row.routeLabel : "Accounting record evidence",
    tone: continuityToneToReleaseTone(row.statusTone),
    badgeVariant: toneToBadgeVariant(continuityToneToReleaseTone(row.statusTone)),
    ariaLabel: row.ariaLabel
  }));

  return {
    title: "Accounting record",
    description: summary.summaryLabel,
    statusLabel: summary.statusLabel,
    tone: continuityToneToReleaseTone(summary.statusTone),
    badgeVariant: toneToBadgeVariant(continuityToneToReleaseTone(summary.statusTone)),
    primaryHref: href,
    primaryLabel: recordId ? "Open record evidence" : "Open continuity",
    primaryAriaLabel: recordId
      ? `Open accounting-record evidence for ${recordId}`
      : "Open Operations Continuity for accounting-record evidence",
    rows,
    emptyText: continuity.accountingRecordEmptyText
  };
}

function buildReportPackPanel(
  continuity: OperationsContinuityScreenViewModel,
  reporting: ReportingScreenViewModel
): OperationsRecordReleasePanel {
  const publication = reporting.workflowTaskPanel?.publicationReview ?? null;
  const published = continuity.closePackage.statusTone === "ready" || publication?.statusVariant === "success";
  const hasRecipients = reporting.hasPackTargets;
  const tone: OperationsRecordReleaseTone = published
    ? "ready"
    : hasRecipients
      ? "review"
      : "blocked";
  const statusLabel = published ? "Publication ready" : hasRecipients ? "Approval review" : "Recipients missing";
  const publicationSummary = publication?.summaryText ?? continuity.closePackage.rationaleLabel;

  return {
    title: "Report pack",
    description: publicationSummary,
    statusLabel,
    tone,
    badgeVariant: toneToBadgeVariant(tone),
    primaryHref: WORKSTATION_ROUTE_CATALOG.reportingReportPacks,
    primaryLabel: "Open report packs",
    primaryAriaLabel: "Open report packs for operations record release",
    rows: [
      {
        id: "close-package",
        label: "Close package",
        value: continuity.closePackage.statusLabel,
        detail: `${continuity.closePackage.reportPackLabel}; ${continuity.closePackage.evidenceLabel}.`,
        href: continuity.closePackage.manifestHref ?? WORKSTATION_ROUTE_CATALOG.accountingOperationsContinuity,
        routeLabel: continuity.closePackage.manifestHref ? "Close manifest" : "Operations continuity",
        tone: continuityToneToReleaseTone(continuity.closePackage.statusTone),
        badgeVariant: toneToBadgeVariant(continuityToneToReleaseTone(continuity.closePackage.statusTone)),
        ariaLabel: `Close package ${continuity.closePackage.statusLabel}`
      },
      {
        id: "publication",
        label: "Publication",
        value: publication?.statusLabel ?? "Publication pending",
        detail: publicationSummary,
        href: WORKSTATION_ROUTE_CATALOG.reportingReportPacks,
        routeLabel: "Report packs",
        tone: publication?.statusVariant === "success" ? "ready" : "review",
        badgeVariant: toneToBadgeVariant(publication?.statusVariant === "success" ? "ready" : "review"),
        ariaLabel: `Report-pack publication ${publication?.statusLabel ?? "pending"}`
      },
      {
        id: "recipients",
        label: "Recipients",
        value: reporting.packTargetsSummary,
        detail: hasRecipients
          ? "Distribution records keep recipients, owners, channels, and pending items attached to the pack."
          : reporting.packTargetsEmptyState.text,
        href: WORKSTATION_ROUTE_CATALOG.reportingReportPacks,
        routeLabel: "Report packs",
        tone: hasRecipients ? "ready" : "blocked",
        badgeVariant: toneToBadgeVariant(hasRecipients ? "ready" : "blocked"),
        ariaLabel: `Report-pack recipients ${reporting.packTargetsSummary}`
      }
    ],
    emptyText: "No report-pack evidence is loaded."
  };
}

function buildReleaseSteps({
  source,
  continuity,
  accounting,
  reportPack
}: {
  source: OperationsRecordReleasePanel;
  continuity: OperationsContinuityScreenViewModel;
  accounting: OperationsRecordReleasePanel;
  reportPack: OperationsRecordReleasePanel;
}): OperationsRecordReleaseStep[] {
  const brokerGate = continuity.gates.find((gate) => gate.gateKey === "BrokerIngest") ?? null;
  const ledgerGate = continuity.gates.find((gate) => gate.gateKey === "LedgerPosting") ?? null;
  const reconciliationGate = continuity.gates.find((gate) => gate.gateKey === "Reconciliation") ?? null;
  const approvalGate = continuity.gates.find((gate) => gate.gateKey === "Approval") ?? null;

  return [
    buildStep({
      id: "source-data",
      label: "Source data",
      detail: source.description,
      href: WORKSTATION_ROUTE_CATALOG.dataProviders,
      statusLabel: source.statusLabel,
      tone: source.tone
    }),
    buildStep({
      id: "broker-intake",
      label: "Import and normalize",
      detail: brokerGate?.detail ?? "Broker intake gate is not loaded for the selected close workflow.",
      href: WORKSTATION_ROUTE_CATALOG.accountingOperationsContinuity,
      statusLabel: brokerGate?.statusLabel ?? "Gate pending",
      tone: brokerGate ? continuityToneToReleaseTone(brokerGate.statusTone) : "neutral"
    }),
    buildStep({
      id: "ledger",
      label: "Accounting record",
      detail: ledgerGate?.detail ?? accounting.description,
      href: accounting.primaryHref,
      statusLabel: accounting.statusLabel,
      tone: combineTones([
        ledgerGate ? continuityToneToReleaseTone(ledgerGate.statusTone) : "neutral",
        accounting.tone
      ])
    }),
    buildStep({
      id: "reconcile",
      label: "Reconcile",
      detail: reconciliationGate?.detail ?? "Reconciliation gate is not loaded for the selected close workflow.",
      href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
      statusLabel: reconciliationGate?.statusLabel ?? "Gate pending",
      tone: reconciliationGate ? continuityToneToReleaseTone(reconciliationGate.statusTone) : "neutral"
    }),
    buildStep({
      id: "approve",
      label: "Approve",
      detail: approvalGate?.detail ?? "Approval gate is not loaded for the selected close workflow.",
      href: WORKSTATION_ROUTE_CATALOG.accountingApprovals,
      statusLabel: approvalGate?.statusLabel ?? "Gate pending",
      tone: approvalGate ? continuityToneToReleaseTone(approvalGate.statusTone) : "neutral"
    }),
    buildStep({
      id: "report",
      label: "Report pack",
      detail: reportPack.description,
      href: reportPack.primaryHref,
      statusLabel: reportPack.statusLabel,
      tone: reportPack.tone
    })
  ];
}

function buildStep({
  id,
  label,
  detail,
  href,
  statusLabel,
  tone
}: {
  id: string;
  label: string;
  detail: string;
  href: string;
  statusLabel: string;
  tone: OperationsRecordReleaseTone;
}): OperationsRecordReleaseStep {
  return {
    id,
    label,
    detail,
    href,
    routeLabel: href,
    statusLabel,
    tone,
    badgeVariant: toneToBadgeVariant(tone),
    ariaLabel: `${label}: ${statusLabel}. ${detail}`
  };
}

function summarizeProviderPosture(data: DataWorkspaceResponse): {
  statusLabel: string;
  value: string;
  detail: string;
  tone: OperationsRecordReleaseTone;
} {
  const providers = data.providers ?? [];
  if (providers.length === 0) {
    return {
      statusLabel: "Providers missing",
      value: "0 providers",
      detail: "No provider posture rows are loaded for the source-data step.",
      tone: "blocked"
    };
  }

  const degraded = providers.filter((provider) => provider.status === "Degraded").length;
  const blocked = providers.filter((provider) => provider.status === "Blocked").length;
  const warnings = providers.filter((provider) => provider.status === "Warning").length;
  const healthy = providers.filter((provider) => provider.status === "Healthy").length;
  if (blocked > 0 || degraded > 0) {
    const blockedTotal = blocked + degraded;
    return {
      statusLabel: "Provider blocked",
      value: `${blockedTotal} blocked`,
      detail: `${blockedTotal} provider${blockedTotal === 1 ? "" : "s"} blocked or degraded before downstream accounting/reporting use.`,
      tone: "blocked"
    };
  }

  if (warnings > 0) {
    return {
      statusLabel: "Provider review",
      value: `${warnings} warning`,
      detail: `${warnings} provider${warnings === 1 ? "" : "s"} need review before accepting source data as release evidence.`,
      tone: "review"
    };
  }

  return {
    statusLabel: "Providers ready",
    value: `${healthy}/${providers.length} healthy`,
    detail: `${healthy} provider${healthy === 1 ? "" : "s"} healthy in the loaded source-data posture.`,
    tone: "ready"
  };
}

function recordIdFromSummary(recordIdLabel: string): string | null {
  const trimmed = recordIdLabel.trim();
  if (!trimmed || /^no accounting record/i.test(trimmed) || /^record pending/i.test(trimmed)) {
    return null;
  }

  return trimmed;
}

function combineTones(tones: OperationsRecordReleaseTone[]): OperationsRecordReleaseTone {
  if (tones.some((tone) => tone === "blocked")) {
    return "blocked";
  }

  if (tones.some((tone) => tone === "review")) {
    return "review";
  }

  if (tones.some((tone) => tone === "neutral")) {
    return "neutral";
  }

  return "ready";
}

function continuityToneToReleaseTone(tone: OperationsContinuityTone): OperationsRecordReleaseTone {
  return tone;
}

function toneToBadgeVariant(tone: OperationsRecordReleaseTone): OperationsRecordReleaseBadgeVariant {
  switch (tone) {
    case "ready":
      return "success";
    case "review":
      return "warning";
    case "blocked":
      return "danger";
    default:
      return "outline";
  }
}
