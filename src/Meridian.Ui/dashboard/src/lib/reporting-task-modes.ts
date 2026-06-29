import type { ReportingHubModel } from "@/lib/reporting-hub";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";

export type ReportingTaskModeId =
  | "cockpit"
  | "report-builder"
  | "run-status"
  | "delivery-evidence"
  | "exports"
  | "governance";

export type ReportingRouteFocusMode = ReportingTaskModeId | "legacy-broad";

export type ReportingTaskModeBadgeVariant = "default" | "outline" | "success" | "warning" | "danger";

export interface ReportingTaskMode {
  id: ReportingTaskModeId;
  label: string;
  detail: string;
  href: string;
  statusLabel: string;
  countLabel: string;
  badgeVariant: ReportingTaskModeBadgeVariant;
  recommended: boolean;
  active: boolean;
}

export interface ReportingTaskModeInput {
  pathname: string;
  hash: string;
  hubModel: ReportingHubModel;
  templateCount: number;
  runCount: number;
  writerGridCount: number;
  deliveryPlanCount: number;
  structuredExportCount: number;
  workflowCount: number;
  accessSummary: string;
}

export function buildReportingTaskModes(input: ReportingTaskModeInput): ReportingTaskMode[] {
  const normalizedPath = normalizeReportingTaskPath(input.pathname);
  const dailyKinds = new Set(input.hubModel.dailyWork.map((item) => normalizeReportingDailyWorkKind(item.kind)));
  const blockedDailyWork = input.hubModel.dailyWork.filter((item) => item.tone === "danger").length;
  const reviewDailyWork = input.hubModel.dailyWork.filter((item) => item.tone === "warning").length;
  const hasDailyGuidance = input.hubModel.dailyWork.length > 0;
  const runStatusRecommended = dailyKinds.has("blocked-package") || dailyKinds.has("delivery-failure") || dailyKinds.has("due-package");
  const deliveryRecommended = dailyKinds.has("delivery-failure") || dailyKinds.has("evidence-gap") || dailyKinds.has("restatement");
  const governanceRecommended = dailyKinds.has("approval-needed") || dailyKinds.has("restatement") || dailyKinds.has("evidence-gap");

  return [
    {
      id: "cockpit",
      label: "Daily cockpit",
      detail: "Blocked output, owner, next action, and proof support stay first on the default Reporting route.",
      href: WORKSTATION_ROUTE_CATALOG.reporting,
      statusLabel: blockedDailyWork > 0 ? "Blocked" : reviewDailyWork > 0 ? "Review" : "Clear",
      countLabel: input.hubModel.dailyWorkSummaryLabel,
      badgeVariant: blockedDailyWork > 0 ? "danger" : reviewDailyWork > 0 ? "warning" : "success",
      recommended: hasDailyGuidance,
      active: normalizedPath === WORKSTATION_ROUTE_CATALOG.reporting && input.hash !== "#run-status"
    },
    {
      id: "report-builder",
      label: "Report Builder",
      detail: "Governed templates, report-writer grids, preview, and report-pack generation stay in the builder lane.",
      href: WORKSTATION_ROUTE_CATALOG.reportingReportBuilder,
      statusLabel: input.writerGridCount > 0 ? "Source-backed" : "Template review",
      countLabel: `${input.templateCount} templates · ${input.writerGridCount} grids`,
      badgeVariant: input.writerGridCount > 0 ? "success" : "warning",
      recommended: dailyKinds.has("due-package"),
      active: isReportingReportBuilderRoute(normalizedPath) &&
        (input.hash === "" || input.hash === "#report-builder")
    },
    {
      id: "run-status",
      label: "Run Status",
      detail: "Generated runs, retry attempts, approval state, lineage, and executable next actions stay together.",
      href: WORKSTATION_ROUTE_CATALOG.reportingRunStatus,
      statusLabel: input.runCount > 0 ? "Runs loaded" : "No runs",
      countLabel: `${input.runCount} runs`,
      badgeVariant: input.runCount > 0 ? "success" : "warning",
      recommended: runStatusRecommended,
      active: normalizedPath === WORKSTATION_ROUTE_CATALOG.reportingRunStatus ||
        (normalizedPath === WORKSTATION_ROUTE_CATALOG.reporting && input.hash === "#run-status")
    },
    {
      id: "delivery-evidence",
      label: "Delivery Evidence",
      detail: "Retained evidence, delivery attempts, publication proof, and restatement support stay in the evidence lane.",
      href: WORKSTATION_ROUTE_CATALOG.reportingDeliveryEvidence,
      statusLabel: deliveryRecommended ? "Evidence review" : "Evidence",
      countLabel: `${input.deliveryPlanCount} delivery plans`,
      badgeVariant: deliveryRecommended ? "warning" : "outline",
      recommended: deliveryRecommended,
      active: normalizedPath === WORKSTATION_ROUTE_CATALOG.reportingDeliveryEvidence ||
        normalizedPath === WORKSTATION_ROUTE_CATALOG.reportingEvidence
    },
    {
      id: "exports",
      label: "Exports",
      detail: "Regulatory, data warehouse, and investment-decision exports stay separate from package authoring.",
      href: `${WORKSTATION_ROUTE_CATALOG.reportingExports}#structured-exports`,
      statusLabel: input.structuredExportCount > 0 ? "Exportable" : "Needs preset",
      countLabel: `${input.structuredExportCount} presets`,
      badgeVariant: input.structuredExportCount > 0 ? "success" : "warning",
      recommended: dailyKinds.has("due-package") || dailyKinds.has("delivery-failure"),
      active: normalizedPath === WORKSTATION_ROUTE_CATALOG.reportingExports
    },
    {
      id: "governance",
      label: "Governance",
      detail: "Access scope, branding themes, report-pack workflow records, approvals, and audit links stay visible before release.",
      href: WORKSTATION_ROUTE_CATALOG.reportingGovernance,
      statusLabel: input.accessSummary,
      countLabel: `${input.workflowCount} workflow records`,
      badgeVariant: governanceRecommended ? "warning" : "outline",
      recommended: governanceRecommended,
      active: normalizedPath === WORKSTATION_ROUTE_CATALOG.reportingGovernance ||
        (normalizedPath === WORKSTATION_ROUTE_CATALOG.reportingReportPacks && input.hash === "#reporting-governance")
    }
  ];
}

export function resolveReportingRouteFocusMode(route: Pick<ReportingTaskModeInput, "pathname" | "hash">): ReportingRouteFocusMode {
  const normalizedPath = normalizeReportingTaskPath(route.pathname);

  if (normalizedPath === WORKSTATION_ROUTE_CATALOG.reporting && route.hash === "#run-status") {
    return "run-status";
  }

  if (normalizedPath === WORKSTATION_ROUTE_CATALOG.reportingReportBuilder) {
    return "report-builder";
  }

  if (normalizedPath === WORKSTATION_ROUTE_CATALOG.reportingRunStatus) {
    return "run-status";
  }

  if (normalizedPath === WORKSTATION_ROUTE_CATALOG.reportingDeliveryEvidence) {
    return "delivery-evidence";
  }

  if (normalizedPath === WORKSTATION_ROUTE_CATALOG.reportingExports) {
    return "exports";
  }

  if (normalizedPath === WORKSTATION_ROUTE_CATALOG.reportingGovernance) {
    return "governance";
  }

  if (
    normalizedPath === WORKSTATION_ROUTE_CATALOG.reportingEvidence ||
    isReportingLegacyReportPackRoute(normalizedPath)
  ) {
    return "legacy-broad";
  }

  return "cockpit";
}

function normalizeReportingTaskPath(pathname: string): string {
  return pathname.split(/[?#]/)[0]?.replace(/\/+$/, "") || WORKSTATION_ROUTE_CATALOG.reporting;
}

function isReportingReportBuilderRoute(pathname: string): boolean {
  return pathname === WORKSTATION_ROUTE_CATALOG.reportingReportBuilder ||
    isReportingLegacyReportPackRoute(pathname);
}

function isReportingLegacyReportPackRoute(pathname: string): boolean {
  return pathname === WORKSTATION_ROUTE_CATALOG.reportingReportPacks ||
    pathname.startsWith(`${WORKSTATION_ROUTE_CATALOG.reportingReportPacks}/`);
}

function normalizeReportingDailyWorkKind(kind: string): string {
  const normalized = kind.trim().toLowerCase();
  return normalized === "readiness-warning" ? "evidence-gap" : normalized;
}
