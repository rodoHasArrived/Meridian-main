import { WORKSPACES, WORKSTATION_ROUTE_CATALOG, workspacePath } from "@/lib/workspace";
import { pluralizeCount } from "@/lib/format";
import {
  buildEvidenceTimelineCandidate,
  type EvidenceTimelineCandidate
} from "@/app-shell.evidence-timeline";
import type { AppShellLinkedContextItem } from "@/app-shell.linked-context";
import { buildOperatorFocusCandidate, type OperatorFocusCandidate } from "@/app-shell.operator-focus";
import {
  primaryOperatorWorkflowStepDefinitions,
  resolvePrimaryOperatorWorkflowStepId,
  resolveWorkflowContinuityRoute,
  type WorkflowContinuityStepStatus,
  type WorkflowContinuityTrailStepDefinition
} from "@/app-shell.workflow-continuity";
import { workspaceLabelForRoute } from "@/app-shell.workflow-routing";
import {
  appendOperatingScopeToRoute,
  buildOperatingScopeFromSearch,
  normalizeOperatingContextSymbol,
  type AppShellOperatingScopeInput,
  type AppShellOperatingScopeState
} from "@/app-shell.operating-scope";
import { buildAccountingEvidenceTimelineItems } from "@/screens/accounting-screen.evidence-timeline";
import { buildAccountingLinkedContextItem } from "@/screens/accounting-screen.linked-context";
import { buildAccountingOperatorFocusItems } from "@/screens/accounting-screen.operator-focus";
import {
  buildAccountingCloseSupportContinuityStatus,
  buildFinancialOperationsWorkflowStepStatus,
  buildAccountingReconciliationContinuityStatus
} from "@/screens/accounting-screen.workflow-continuity";
import { buildDataEvidenceTimelineItems } from "@/screens/data-screen.evidence-timeline";
import { buildDataLinkedContextItem } from "@/screens/data-screen.linked-context";
import { buildDataOperatorFocusItems } from "@/screens/data-screen.operator-focus";
import { buildTrustedDataContinuityStatus } from "@/screens/data-screen.workflow-continuity";
import { buildPortfolioEvidenceTimelineItems } from "@/screens/portfolio-screen.evidence-timeline";
import { buildPortfolioLinkedContextItem } from "@/screens/portfolio-screen.linked-context";
import { buildPortfolioOperatorFocusItems } from "@/screens/portfolio-screen.operator-focus";
import { buildPortfolioLedgerContinuityStatus } from "@/screens/portfolio-screen.workflow-continuity";
import { buildReportingLinkedContextItem } from "@/screens/reporting-screen.linked-context";
import { buildReportingOperatorFocusItems } from "@/screens/reporting-screen.operator-focus";
import { buildReportingGovernedReportContinuityStatus } from "@/screens/reporting-screen.workflow-continuity";
import { buildProviderSetupContinuityStatus } from "@/screens/settings-screen.workflow-continuity";
import { buildStrategyEvidenceTimelineItems } from "@/screens/strategy-screen.evidence-timeline";
import { buildStrategyOperatorFocusItems } from "@/screens/strategy-screen.operator-focus";
import { buildStrategyContinuityStatus } from "@/screens/strategy-screen.workflow-continuity";
import { buildTradingEvidenceTimelineItems } from "@/screens/trading-screen.evidence-timeline";
import { buildTradingLinkedContextItem } from "@/screens/trading-screen.linked-context";
import { buildTradingOperatorFocusItems } from "@/screens/trading-screen.operator-focus";
import { buildPaperReadinessContinuityStatus } from "@/screens/trading-screen.workflow-continuity";
import type {
  AppShellDecisionBrief,
  AppShellEvidenceTimelineItem,
  AppShellOperatorFocusItem,
  AppShellPrimaryOperatorWorkflowStep,
  AppShellWorkflowContinuityDisclosureState,
  AppShellWorkflowContinuityStatusTone,
  AppShellWorkflowContinuityStep,
  AppShellWorkflowContinuityViewModel
} from "@/app-shell.workflow-continuity-types";
import type {
  AppShellWorkspacePayload,
  WorkspaceErrorMap
} from "@/app-shell.view-model";
import type { WorkspaceKey, WorkspaceSummary } from "@/types";

export function buildWorkflowContinuityViewModel(
  pathname: string,
  search: string,
  hash: string,
  activeWorkspace: WorkspaceSummary,
  statusContext: WorkflowContinuityStatusContext = emptyWorkflowContinuityStatusContext,
  operatingContextSymbol: string | null = null,
  operatingContextScope: AppShellOperatingScopeInput | null = null
): AppShellWorkflowContinuityViewModel {
  const operatingScope = buildOperatingScopeFromSearch(search, {
    ...(operatingContextScope ?? {}),
    symbol: operatingContextScope?.symbol ?? operatingContextSymbol
  }, pathname);
  const subjectSymbol = operatingScope.subjectSymbol;
  const currentRoute = `${pathname}${search}${hash}`;
  const routeResolution = resolveWorkflowContinuityRoute(pathname, hash);
  const trail = routeResolution.trail;
  const steps = (trail?.steps ?? []).map((step) => materializeContinuityStep(step, operatingScope));
  const activeIndex = routeResolution.activeStepIndex;
  const nextIndex = activeIndex === null || steps.length === 0
    ? null
    : Math.min(activeIndex + 1, steps.length - 1);
  const activeStep = activeIndex === null ? null : steps[activeIndex] ?? null;
  const nextStep = nextIndex === null ? null : steps[nextIndex] ?? activeStep;
  const stepStatuses = steps.map((step) => buildWorkflowContinuityStepStatus(step.id, statusContext));
  const attentionCount = stepStatuses.filter((status) => status.tone === "blocked" || status.tone === "review").length;
  const operatorFocus = buildOperatorFocusViewModel(statusContext, operatingScope, pathname === "/");
  const evidenceTimeline = buildEvidenceTimelineViewModel(statusContext, operatingScope);
  const linkedContext = buildLinkedContextViewModel(statusContext, subjectSymbol, operatingScope);
  const disclosure = buildWorkflowContinuityDisclosureState(statusContext, operatorFocus, evidenceTimeline, linkedContext);
  const primaryOperatorFlowSteps = buildPrimaryOperatorWorkflowSteps(pathname, operatingScope);
  const title = trail?.title ?? "Choose a task";
  const summary = trail
    ? buildWorkflowContinuitySummary(
      trail.summary,
      activeStep?.label ?? activeWorkspace.label,
      nextStep?.label ?? activeWorkspace.label,
      subjectSymbol,
      attentionCount
    )
    : routeResolution.mode === "choose-task"
      ? `No continuity step is selected for ${activeWorkspace.label}. Choose a task from the local workspace navigation; the current operating scope will be preserved.`
      : "The requested route is not part of a Meridian workspace. Open the Daily Control Tower to choose a task.";
  const contextValue = subjectSymbol
    ? `${activeWorkspace.label} / ${subjectSymbol}`
    : operatingScope.hasScope
      ? `${activeWorkspace.label} / Scoped workflow`
      : `${activeWorkspace.label} / ${activeStep?.label ?? "Choose a task"}`;
  const nextActionLabel = routeResolution.mode === "matched"
    ? activeStep && nextStep && activeStep.id === nextStep.id
      ? `Stay on ${activeStep.label}`
      : `Next: ${nextStep?.label ?? activeWorkspace.label}`
    : routeResolution.mode === "choose-task"
      ? "Choose a task"
      : "Open Daily Control Tower";
  const nextActionAriaLabel = routeResolution.mode === "matched"
    ? activeStep && nextStep && activeStep.id === nextStep.id
      ? `Stay on ${activeStep.label}`
      : `Continue workflow to ${nextStep?.label ?? activeWorkspace.label}`
    : routeResolution.mode === "choose-task"
      ? `Choose a task in ${activeWorkspace.label}`
      : "Open Daily Control Tower to choose a task";
  const nextActionHref = routeResolution.mode === "matched"
    ? nextStep?.href ?? appendOperatingScopeToRoute(workspacePath(activeWorkspace.key), operatingScope)
    : routeResolution.mode === "choose-task"
      ? appendOperatingScopeToRoute(workspacePath(activeWorkspace.key), operatingScope)
      : appendOperatingScopeToRoute("/", operatingScope);
  const decisionBrief = buildDecisionBriefViewModel({
    activeWorkspace,
    nextActionLabel,
    nextActionAriaLabel,
    nextActionHref,
    operatorFocus,
    evidenceTimeline,
    linkedContext,
    subjectSymbol
  });

  return {
    mode: routeResolution.mode,
    title,
    summary,
    primaryOperatorFlowLabel: "Primary operator workflow",
    primaryOperatorFlowSummary: "Import -> Validate -> Reconcile -> Investigate -> Approve -> Report",
    primaryOperatorFlowStepsLabel: "Primary operator workflow steps",
    primaryOperatorFlowSteps,
    contextLabel: "Operating context",
    contextValue,
    subjectSymbol,
    clearSubjectAriaLabel: operatingScope.clearAriaLabel,
    operatingScope,
    routeLabel: currentRoute || "/",
    stepsLabel: trail ? `${trail.title} workflow steps` : `${activeWorkspace.label} task workflow steps`,
    ariaLabel: trail ? `${trail.title} continuity` : `${activeWorkspace.label} task choice`,
    nextActionLabel,
    nextActionAriaLabel,
    nextActionHref,
    decisionBrief,
    disclosure,
    operatorFocusLabel: "Operator focus",
    operatorFocusSummary: operatorFocus.summary,
    operatorFocusEmptyText: operatorFocus.emptyText,
    operatorFocusItemsLabel: "Ranked cross-workspace operator focus items",
    operatorFocusOverflowLabel: operatorFocus.overflowLabel,
    operatorFocusItems: operatorFocus.items,
    operatorFocusCommandItems: operatorFocus.commandItems,
    evidenceTimelineLabel: "Evidence timeline",
    evidenceTimelineSummary: evidenceTimeline.summary,
    evidenceTimelineEmptyText: evidenceTimeline.emptyText,
    evidenceTimelineItemsLabel: "Recent cross-workspace evidence events",
    evidenceTimelineOverflowLabel: evidenceTimeline.overflowLabel,
    evidenceTimelineItems: evidenceTimeline.items,
    linkedContextLabel: "Linked context",
    linkedContextSummary: linkedContext.summary,
    linkedContextPostureLabel: linkedContext.postureLabel,
    linkedContextPostureTone: linkedContext.postureTone,
    linkedContextPrimaryActionLabel: linkedContext.primaryActionLabel,
    linkedContextPrimaryActionHref: linkedContext.primaryActionHref,
    linkedContextPrimaryActionAriaLabel: linkedContext.primaryActionAriaLabel,
    linkedContextEmptyText: linkedContext.emptyText,
    linkedContextItemsLabel: "Risk-ranked portfolio-aware linked context routes",
    linkedContextItems: linkedContext.items,
    steps: steps.map((step, index) => {
      const status = stepStatuses[index] ?? { label: "Route", tone: "pending" as const };
      const active = activeIndex !== null && index === activeIndex;
      const next = nextIndex !== null && index === nextIndex && index !== activeIndex;
      return {
        ...step,
        active,
        next,
        statusLabel: active ? `Current / ${status.label}` : next ? `Next / ${status.label}` : status.label,
        statusTone: status.tone,
        ariaLabel: active
          ? `${step.label}, current workflow step, ${status.label}`
          : next
            ? `${step.label}, next workflow step, ${status.label}`
            : `Open ${step.label}, ${status.label}`
      };
    })
  };
}

function buildPrimaryOperatorWorkflowSteps(
  pathname: string,
  operatingScope: AppShellOperatingScopeState
): AppShellPrimaryOperatorWorkflowStep[] {
  const activeStepId = resolvePrimaryOperatorWorkflowStepId(pathname);

  return primaryOperatorWorkflowStepDefinitions.map((step) => {
    const active = step.id === activeStepId;
    const href = appendOperatingScopeToRoute(step.href, operatingScope);
    const statusLabel = active ? "Current" : "Available";
    const statusTone: AppShellWorkflowContinuityStatusTone = active ? "review" : "pending";

    return {
      ...step,
      href,
      active,
      statusLabel,
      statusTone,
      ariaLabel: active
        ? `${step.label}, current primary operator workflow step, ${statusLabel}`
        : `Open ${step.label}, primary operator workflow step, ${statusLabel}`
    };
  });
}

interface WorkflowContinuityStatusContext {
  loading: boolean;
  error: string | null;
  workflowError: string | null;
  workspaceErrors: WorkspaceErrorMap;
  payload: AppShellWorkspacePayload;
}

const emptyWorkflowContinuityStatusContext: WorkflowContinuityStatusContext = {
  loading: false,
  error: null,
  workflowError: null,
  workspaceErrors: {},
  payload: {
    session: null,
    overview: null,
    strategy: null,
    trading: null,
    portfolio: null,
    data: null,
    accounting: null,
    reporting: null,
    workflowSummary: null
  }
};

function materializeContinuityStep(
  step: WorkflowContinuityTrailStepDefinition,
  operatingScope: AppShellOperatingScopeState
): AppShellWorkflowContinuityStep {
  const href = appendOperatingScopeToRoute(step.href, operatingScope);

  return {
    id: step.id,
    label: step.label,
    description: step.description,
    href,
    ariaLabel: `Open ${step.label}`,
    statusLabel: "Route",
    statusTone: "pending",
    active: false,
    next: false
  };
}

function buildWorkflowContinuityStepStatus(
  stepId: string,
  context: WorkflowContinuityStatusContext
): WorkflowContinuityStepStatus {
  const workspaceError = (workflowContinuityWorkspaceErrors[stepId] ?? [])
    .map((workspaceKey) => context.workspaceErrors[workspaceKey])
    .find(Boolean);

  if (workspaceError) {
    return { label: "Degraded", tone: "blocked" };
  }

  if (context.loading) {
    return { label: "Loading", tone: "pending" };
  }

  const financialOperationsStatus = buildFinancialOperationsWorkflowStepStatus(stepId, context.payload.workflowSummary);
  if (financialOperationsStatus) {
    return financialOperationsStatus;
  }

  switch (stepId) {
    case "today":
      return context.error || context.workflowError
        ? { label: "Review", tone: "review" }
        : { label: "Finance queue", tone: "ready" };
    case "market-data":
    case "trusted-data":
    case "data-health":
      return buildTrustedDataContinuityStatus(context.payload.data);
    case "strategy-runs":
    case "quant-lab":
    case "covered-call":
    case "strategy":
      return buildStrategyContinuityStatus(context.payload.strategy, context.workflowError);
    case "readiness":
    case "paper-readiness":
    case "trading-readiness":
    case "trading-cockpit":
      return buildPaperReadinessContinuityStatus(context.payload.trading);
    case "provider-setup":
      return buildProviderSetupContinuityStatus({
        session: context.payload.session,
        overview: context.payload.overview,
        error: context.error,
        workflowError: context.workflowError
      });
    case "portfolio-review":
    case "portfolio-exposure":
    case "portfolio-ledger":
      return buildPortfolioLedgerContinuityStatus(context.payload.portfolio);
    case "receive-activity":
    case "match-records":
    case "resolve-exceptions":
    case "approve-results":
    case "security-master":
    case "ledger":
    case "reconciliation":
    case "exceptions":
      return buildAccountingReconciliationContinuityStatus(context.payload.accounting);
    case "close-support":
    case "close":
      if (context.error || context.workflowError || !context.payload) {
        return { label: "Review", tone: "review" };
      }
      return buildAccountingCloseSupportContinuityStatus(context.payload.accounting);
    case "produce-evidence":
    case "evidence":
    case "evidence-review":
    case "report-packs":
    case "governed-report":
    case "reports":
      return buildReportingGovernedReportContinuityStatus(context.payload.reporting);
    default:
      return context.error || context.workflowError
        ? { label: "Review", tone: "review" }
        : { label: "Ready", tone: "ready" };
  }
}

const workflowContinuityWorkspaceErrors: Record<string, WorkspaceKey[]> = {
  today: ["accounting", "reporting", "data"],
  exceptions: ["accounting"],
  close: ["accounting"],
  reports: ["reporting"],
  "data-health": ["data"],
  "market-data": ["data"],
  "trusted-data": ["data"],
  "strategy-runs": ["strategy"],
  "quant-lab": ["strategy"],
  "covered-call": ["strategy"],
  strategy: ["strategy"],
  readiness: ["trading"],
  "paper-readiness": ["trading"],
  "trading-readiness": ["trading"],
  "trading-cockpit": ["trading"],
  "provider-setup": ["settings"],
  "portfolio-review": ["portfolio"],
  "portfolio-exposure": ["portfolio"],
  "portfolio-ledger": ["portfolio"],
  "receive-activity": ["accounting"],
  "match-records": ["accounting"],
  "resolve-exceptions": ["accounting"],
  "approve-results": ["accounting"],
  "security-master": ["accounting"],
  ledger: ["accounting"],
  reconciliation: ["accounting"],
  "produce-evidence": ["reporting"],
  evidence: ["reporting"],
  "evidence-review": ["reporting"],
  "report-packs": ["reporting"],
  "governed-report": ["reporting"]
};

interface OperatorFocusViewModel {
  summary: string;
  emptyText: string;
  overflowLabel: string | null;
  items: AppShellOperatorFocusItem[];
  commandItems: AppShellOperatorFocusItem[];
}

interface EvidenceTimelineViewModel {
  summary: string;
  emptyText: string;
  overflowLabel: string | null;
  items: AppShellEvidenceTimelineItem[];
}

interface LinkedContextViewModel {
  summary: string;
  postureLabel: string;
  postureTone: AppShellWorkflowContinuityStatusTone;
  primaryActionLabel: string | null;
  primaryActionHref: string | null;
  primaryActionAriaLabel: string | null;
  emptyText: string;
  items: AppShellLinkedContextItem[];
}

const OPERATOR_FOCUS_VISIBLE_LIMIT = 3;
const EVIDENCE_TIMELINE_VISIBLE_LIMIT = 4;

interface DecisionBriefInput {
  activeWorkspace: WorkspaceSummary;
  nextActionLabel: string;
  nextActionAriaLabel: string;
  nextActionHref: string;
  operatorFocus: OperatorFocusViewModel;
  evidenceTimeline: EvidenceTimelineViewModel;
  linkedContext: LinkedContextViewModel;
  subjectSymbol: string | null;
}

function buildDecisionBriefViewModel({
  activeWorkspace,
  nextActionLabel,
  nextActionAriaLabel,
  nextActionHref,
  operatorFocus,
  evidenceTimeline,
  linkedContext,
  subjectSymbol
}: DecisionBriefInput): AppShellDecisionBrief {
  const latestEvidence = evidenceTimeline.items[0] ?? null;
  const focusItem = operatorFocus.commandItems[0] ?? operatorFocus.items[0] ?? null;

  if (focusItem) {
    return {
      label: "Decision brief",
      // The masthead pill already behaves as the action. Keep its visible title to the
      // operator-owned issue so common labels remain readable at the supported desktop
      // viewport instead of truncating a redundant "Resolve" prefix.
      title: focusItem.label,
      summary: `${focusItem.workspaceLabel} is the highest-priority loaded issue. ${operatorFocus.summary}`,
      reasonLabel: "Why now",
      reason: focusItem.detail,
      statusLabel: formatDecisionStatusLabel(focusItem.tone),
      statusTone: focusItem.tone,
      evidenceLabel: formatDecisionEvidenceLabel(latestEvidence),
      actionLabel: focusItem.actionLabel,
      actionHref: focusItem.route,
      actionAriaLabel: focusItem.ariaLabel
    };
  }

  if (linkedContext.primaryActionHref && linkedContext.primaryActionLabel) {
    const leadingContextItem = linkedContext.items[0] ?? null;
    return {
      label: "Decision brief",
      title: subjectSymbol ? `Continue ${subjectSymbol} decision` : "Continue linked context",
      summary: linkedContext.summary,
      reasonLabel: "Context",
      reason: leadingContextItem?.detail ?? linkedContext.emptyText,
      statusLabel: linkedContext.postureLabel,
      statusTone: linkedContext.postureTone,
      evidenceLabel: formatDecisionEvidenceLabel(latestEvidence),
      actionLabel: linkedContext.primaryActionLabel,
      actionHref: linkedContext.primaryActionHref,
      actionAriaLabel: linkedContext.primaryActionAriaLabel ?? linkedContext.primaryActionLabel
    };
  }

  if (latestEvidence) {
    return {
      label: "Decision brief",
      title: "Review latest evidence",
      summary: `${latestEvidence.workspaceLabel} changed at ${latestEvidence.timestampLabel}.`,
      reasonLabel: "Evidence",
      reason: latestEvidence.detail,
      statusLabel: formatDecisionStatusLabel(latestEvidence.tone),
      statusTone: latestEvidence.tone,
      evidenceLabel: formatDecisionEvidenceLabel(latestEvidence),
      actionLabel: "Open latest evidence",
      actionHref: latestEvidence.route,
      actionAriaLabel: latestEvidence.ariaLabel
    };
  }

  return {
    label: "Decision brief",
    title: `Continue ${activeWorkspace.label}`,
    summary: "No higher-priority cross-workspace issue is loaded.",
    reasonLabel: "Next step",
    reason: `${linkedContext.summary} ${evidenceTimeline.summary}`,
    statusLabel: "Workflow",
    statusTone: "pending",
    evidenceLabel: null,
    actionLabel: nextActionLabel,
    actionHref: nextActionHref,
    actionAriaLabel: nextActionAriaLabel
  };
}

function formatDecisionStatusLabel(tone: AppShellWorkflowContinuityStatusTone): string {
  switch (tone) {
    case "blocked":
      return "Blocked";
    case "review":
      return "Review";
    case "pending":
      return "Pending";
    case "ready":
      return "Ready";
  }
}

function formatDecisionEvidenceLabel(item: AppShellEvidenceTimelineItem | null): string | null {
  return item ? `Latest evidence: ${item.workspaceLabel} ${item.timestampLabel}` : null;
}

function buildOperatorFocusViewModel(
  context: WorkflowContinuityStatusContext,
  operatingScope: AppShellOperatingScopeState,
  financeFirst = false
): OperatorFocusViewModel {
  if (context.loading) {
    return {
      summary: "Loading cross-workspace operator posture.",
      emptyText: "Ranked focus actions will appear after workspace data loads.",
      overflowLabel: null,
      items: [],
      commandItems: []
    };
  }

  const candidates = dedupeOperatorFocusCandidates([
    ...buildWorkspaceErrorFocusItems(context),
    ...buildTradingOperatorFocusItems(context.payload.trading),
    ...buildDataOperatorFocusItems(context.payload.data),
    ...buildPortfolioOperatorFocusItems(context.payload.portfolio),
    ...buildAccountingOperatorFocusItems({
      accounting: context.payload.accounting,
      workflowSummary: context.payload.workflowSummary
    }),
    ...buildReportingOperatorFocusItems(context.payload.reporting),
    ...buildStrategyOperatorFocusItems(context.payload.strategy)
  ]).sort((left, right) => compareOperatorFocusCandidates(left, right, financeFirst));

  const visibleItems = candidates.slice(0, OPERATOR_FOCUS_VISIBLE_LIMIT).map((candidate) => toOperatorFocusItem(candidate, operatingScope));
  const blockedCount = candidates.filter((item) => item.tone === "blocked").length;
  const reviewCount = candidates.filter((item) => item.tone === "review").length;
  const overflowCount = Math.max(0, candidates.length - visibleItems.length);

  return {
    summary: candidates.length > 0
      ? `${formatCount(candidates.length, "focus item")} across workspaces: ${formatStatusCount(blockedCount, "blocked")} and ${formatStatusCount(reviewCount, "review")}.`
      : "No cross-workspace blockers in loaded workspace data.",
    emptyText: "Loaded workspaces have no ranked blockers.",
    overflowLabel: overflowCount > 0 ? `+${overflowCount} more focus ${overflowCount === 1 ? "item" : "items"}` : null,
    items: visibleItems,
    commandItems: candidates.map((candidate) => toOperatorFocusItem(candidate, operatingScope))
  };
}

function buildWorkflowContinuityDisclosureState(
  statusContext: WorkflowContinuityStatusContext,
  operatorFocus: OperatorFocusViewModel,
  evidenceTimeline: EvidenceTimelineViewModel,
  linkedContext: LinkedContextViewModel
): AppShellWorkflowContinuityDisclosureState {
  const degraded = statusContext.loading || Boolean(statusContext.error) || Boolean(statusContext.workflowError)
    || Object.keys(statusContext.workspaceErrors).length > 0;
  const defaultExpanded = !degraded;
  const linkedCount = linkedContext.items.length;
  const focusCount = operatorFocus.items.length;
  const evidenceCount = evidenceTimeline.items.length;

  return {
    label: "Supporting workflow evidence",
    summary: degraded
      ? "Supporting context is collapsed while the workstation recovers. Expand sections for diagnostics and handoffs."
      : "Expand supporting context when you need linked routes, ranked focus items, or recent evidence.",
    panels: [
      {
        id: "linked-context",
        label: "Linked context",
        summary: linkedCount === 0 ? linkedContext.emptyText : summarizeDisclosureCount(linkedCount, "linked route"),
        ariaLabel: linkedCount === 0
          ? "Expand linked context. No linked routes are loaded."
          : `Expand linked context. ${summarizeDisclosureCount(linkedCount, "linked route")} loaded.`,
        defaultExpanded
      },
      {
        id: "operator-focus",
        label: "Operator focus",
        summary: focusCount === 0 ? operatorFocus.emptyText : summarizeDisclosureCount(focusCount, "focus item"),
        ariaLabel: focusCount === 0
          ? "Expand operator focus. No focus items are loaded."
          : `Expand operator focus. ${summarizeDisclosureCount(focusCount, "focus item")} loaded.`,
        defaultExpanded
      },
      {
        id: "evidence-timeline",
        label: "Evidence timeline",
        summary: evidenceCount === 0 ? evidenceTimeline.emptyText : summarizeDisclosureCount(evidenceCount, "evidence event"),
        ariaLabel: evidenceCount === 0
          ? "Expand evidence timeline. No evidence events are loaded."
          : `Expand evidence timeline. ${summarizeDisclosureCount(evidenceCount, "evidence event")} loaded.`,
        defaultExpanded
      }
    ]
  };
}

function summarizeDisclosureCount(count: number, singular: string): string {
  return formatCount(count, singular);
}

function buildWorkspaceErrorFocusItems(context: WorkflowContinuityStatusContext): OperatorFocusCandidate[] {
  const candidates = Object.entries(context.workspaceErrors)
    .map(([key, detail], index) => {
      const workspaceKey = key as WorkspaceKey;
      const workspace = WORKSPACES.find((item) => item.key === workspaceKey);
      const label = `${workspace?.label ?? key} slice degraded`;
      return buildOperatorFocusCandidate({
        id: `workspace-error:${key}`,
        label,
        detail: detail || "Workspace request failed.",
        route: WORKSTATION_ROUTE_CATALOG.settingsBackendCapabilityCoverage,
        workspaceLabel: workspace?.label ?? key,
        actionLabel: "Review diagnostics",
        tone: "blocked",
        sourcePriority: 1,
        sourceIndex: index
      });
    });

  if (context.workflowError) {
    candidates.push(buildOperatorFocusCandidate({
      id: "workspace-error:workflow-catalog",
      label: "Workflow catalog degraded",
      detail: context.workflowError,
      route: WORKSTATION_ROUTE_CATALOG.settingsBackendCapabilityCoverage,
      workspaceLabel: "Settings",
      actionLabel: "Review diagnostics",
      tone: "review",
      sourcePriority: 2,
      sourceIndex: candidates.length
    }));
  }

  return candidates;
}

function dedupeOperatorFocusCandidates(candidates: OperatorFocusCandidate[]): OperatorFocusCandidate[] {
  const byId = new Map<string, OperatorFocusCandidate>();
  candidates.forEach((candidate) => {
    const existing = byId.get(candidate.id);
    if (!existing || compareOperatorFocusCandidates(candidate, existing) < 0) {
      byId.set(candidate.id, candidate);
    }
  });

  return Array.from(byId.values());
}

function compareOperatorFocusCandidates(left: OperatorFocusCandidate, right: OperatorFocusCandidate, financeFirst = false): number {
  return (financeFirst ? financeWorkspacePriority(left.workspaceLabel) - financeWorkspacePriority(right.workspaceLabel) : 0)
    || operatorFocusTonePriority(left.tone) - operatorFocusTonePriority(right.tone)
    || left.sourcePriority - right.sourcePriority
    || left.sourceIndex - right.sourceIndex
    || left.label.localeCompare(right.label);
}

function financeWorkspacePriority(workspaceLabel: string): number {
  switch (workspaceLabel.toLowerCase()) {
    case "accounting":
      return 0;
    case "reporting":
      return 1;
    case "data":
      return 2;
    case "portfolio":
      return 3;
    case "settings":
      return 4;
    case "trading":
      return 5;
    case "strategy":
      return 6;
    default:
      return 7;
  }
}

function operatorFocusTonePriority(tone: AppShellWorkflowContinuityStatusTone): number {
  switch (tone) {
    case "blocked":
      return 0;
    case "review":
      return 1;
    case "pending":
      return 2;
    case "ready":
      return 3;
  }
}

function toOperatorFocusItem(
  candidate: OperatorFocusCandidate,
  operatingScope: AppShellOperatingScopeState
): AppShellOperatorFocusItem {
  const { sourcePriority: _sourcePriority, sourceIndex: _sourceIndex, route, ...item } = candidate;
  return {
    ...item,
    route: appendOperatingScopeToRoute(route, operatingScope)
  };
}

function buildLinkedContextViewModel(
  context: WorkflowContinuityStatusContext,
  subjectSymbol: string | null,
  operatingScope: AppShellOperatingScopeState
): LinkedContextViewModel {
  if (context.loading) {
    return {
      summary: "Loading linked operating context.",
      postureLabel: "Loading",
      postureTone: "pending",
      primaryActionLabel: null,
      primaryActionHref: null,
      primaryActionAriaLabel: null,
      emptyText: "Portfolio-aware context links will appear after workspace data loads.",
      items: []
    };
  }

  const symbol = subjectSymbol ?? inferPrimaryOperatingSymbol(context.payload);
  if (!symbol) {
    return {
      summary: "No linked symbol selected.",
      postureLabel: "No subject",
      postureTone: "pending",
      primaryActionLabel: null,
      primaryActionHref: null,
      primaryActionAriaLabel: null,
      emptyText: "Open a symbol route or load portfolio positions to link data, trading, portfolio, accounting, and evidence routes.",
      items: []
    };
  }

  const items = orderLinkedContextItems([
    buildDataLinkedContextItem(context.payload.data, symbol),
    buildTradingLinkedContextItem(context.payload.trading, symbol),
    buildPortfolioLinkedContextItem(context.payload.portfolio, symbol),
    buildAccountingLinkedContextItem(context.payload.accounting, symbol),
    buildReportingLinkedContextItem(context.payload.reporting, symbol)
  ]).map((item) => materializeScopedLinkedContextItem(item, operatingScope));
  const blockedCount = items.filter((item) => item.tone === "blocked").length;
  const reviewCount = items.filter((item) => item.tone === "review").length;
  const pendingCount = items.filter((item) => item.tone === "pending").length;
  const attentionCount = blockedCount + reviewCount + pendingCount;
  const statusSummary = [
    blockedCount > 0 ? formatStatusCount(blockedCount, "blocked") : null,
    reviewCount > 0 ? formatStatusCount(reviewCount, "review") : null,
    pendingCount > 0 ? formatStatusCount(pendingCount, "pending") : null
  ].filter(Boolean).join(", ");
  const contextSource = subjectSymbol ? "active subject" : "suggested from loaded positions";
  const primaryItem = items.find((item) => item.tone !== "ready") ?? items[0] ?? null;
  const primaryActionLabel = primaryItem
    ? (primaryItem.tone === "ready" ? `Review ${symbol} context` : `Open ${primaryItem.label}`)
    : null;

  return {
    summary: statusSummary
      ? `${symbol} needs ${formatCount(attentionCount, "check")} before action across ${formatCount(items.length, "workspace")}; ${statusSummary}.`
      : `${symbol} context is clear across ${formatCount(items.length, "workspace")}.`,
    postureLabel: buildLinkedContextPostureLabel(blockedCount, reviewCount, pendingCount),
    postureTone: primaryItem?.tone ?? "ready",
    primaryActionLabel,
    primaryActionHref: primaryItem?.route ?? null,
    primaryActionAriaLabel: primaryItem && primaryActionLabel
      ? `${primaryActionLabel} from ${contextSource}; ${primaryItem.workspaceLabel} status ${primaryItem.statusLabel}.`
      : null,
    emptyText: `${symbol} has no linked workstation context yet.`,
    items: items.map((item) => ({
      ...item,
      ariaLabel: `${item.workspaceLabel}: ${item.label}. ${item.detail} ${item.statusLabel}. ${contextSource}.`
    }))
  };
}

function orderLinkedContextItems(items: AppShellLinkedContextItem[]): AppShellLinkedContextItem[] {
  return items
    .map((item, index) => ({ item, index }))
    .sort((left, right) =>
      linkedContextTonePriority(left.item.tone) - linkedContextTonePriority(right.item.tone)
      || left.index - right.index
    )
    .map(({ item }) => item);
}

function linkedContextTonePriority(tone: AppShellWorkflowContinuityStatusTone): number {
  switch (tone) {
    case "blocked":
      return 0;
    case "review":
      return 1;
    case "pending":
      return 2;
    case "ready":
      return 3;
  }
}

function buildLinkedContextPostureLabel(blockedCount: number, reviewCount: number, pendingCount: number): string {
  if (blockedCount > 0) {
    return `${blockedCount} blocked`;
  }

  if (reviewCount > 0) {
    return `${reviewCount} review`;
  }

  if (pendingCount > 0) {
    return `${pendingCount} pending`;
  }

  return "Ready";
}

function inferPrimaryOperatingSymbol(payload: AppShellWorkspacePayload): string | null {
  return normalizeOperatingContextSymbol(payload.portfolio?.positions?.[0]?.symbol ?? null)
    ?? normalizeOperatingContextSymbol(payload.trading?.positions?.[0]?.symbol ?? null)
    ?? normalizeOperatingContextSymbol(payload.trading?.openOrders?.[0]?.symbol ?? null)
    ?? normalizeOperatingContextSymbol(payload.trading?.fills?.[0]?.symbol ?? null);
}

function buildEvidenceTimelineViewModel(
  context: WorkflowContinuityStatusContext,
  operatingScope: AppShellOperatingScopeState
): EvidenceTimelineViewModel {
  if (context.loading) {
    return {
      summary: "Loading cross-workspace evidence timeline.",
      emptyText: "Recent audit and workflow events will appear after workspace data loads.",
      overflowLabel: null,
      items: []
    };
  }

  const candidates = dedupeEvidenceTimelineCandidates([
    ...buildOverviewEvidenceTimelineItems(context),
    ...buildTradingEvidenceTimelineItems(context.payload.trading),
    ...buildDataEvidenceTimelineItems(context.payload.data),
    ...buildStrategyEvidenceTimelineItems(context.payload.strategy),
    ...buildPortfolioEvidenceTimelineItems(context.payload.portfolio),
    ...buildAccountingEvidenceTimelineItems(context.payload.accounting)
  ]).sort(compareEvidenceTimelineCandidates);

  const visibleItems = candidates
    .slice(0, EVIDENCE_TIMELINE_VISIBLE_LIMIT)
    .map((candidate) => toEvidenceTimelineItem(candidate, operatingScope));
  const overflowCount = Math.max(0, candidates.length - visibleItems.length);
  const workspaceCount = new Set(candidates.map((item) => item.workspaceLabel)).size;
  const latest = visibleItems[0] ?? null;

  return {
    summary: candidates.length > 0 && latest
      ? `${formatCount(candidates.length, "evidence event")} across ${formatCount(workspaceCount, "workspace")}. Latest: ${latest.workspaceLabel} at ${latest.timestampLabel}.`
      : "No timestamped evidence events in loaded workspace data.",
    emptyText: "Loaded workspace data has no timestamped evidence events.",
    overflowLabel: overflowCount > 0 ? `+${overflowCount} older ${overflowCount === 1 ? "event" : "events"}` : null,
    items: visibleItems
  };
}

function buildOverviewEvidenceTimelineItems({ payload }: WorkflowContinuityStatusContext): EvidenceTimelineCandidate[] {
  return (payload.overview?.recentEvents ?? [])
    .map((event, index) => {
      const route = routeForSystemEvent(event.source);
      const sourceLabel = labelForSystemEventSource(event.source);
      return buildEvidenceTimelineCandidate({
        id: `overview-event:${event.id}`,
        label: `${sourceLabel} ${event.type}`,
        detail: event.message,
        route,
        workspaceLabel: workspaceLabelForRoute(route),
        timestamp: event.timestamp,
        tone: toneFromSystemEventType(event.type),
        sourcePriority: 40,
        sourceIndex: index
      });
    })
    .filter((item): item is EvidenceTimelineCandidate => Boolean(item));
}

function toneFromSystemEventType(type: "info" | "warning" | "error"): AppShellWorkflowContinuityStatusTone {
  switch (type) {
    case "error":
      return "blocked";
    case "warning":
      return "review";
    case "info":
      return "ready";
  }
}

function routeForSystemEvent(source: string): string {
  const normalized = source.toLowerCase();
  if (normalized.includes("trading") || normalized.includes("execution") || normalized.includes("paper")) {
    return WORKSTATION_ROUTE_CATALOG.tradingReadiness;
  }

  if (normalized.includes("portfolio") || normalized.includes("brokerage")) {
    return WORKSTATION_ROUTE_CATALOG.portfolio;
  }

  if (normalized.includes("reconciliation") || normalized.includes("ledger") || normalized.includes("accounting")) {
    return WORKSTATION_ROUTE_CATALOG.accountingReconciliation;
  }

  if (normalized.includes("report") || normalized.includes("evidence")) {
    return WORKSTATION_ROUTE_CATALOG.reportingEvidence;
  }

  if (normalized.includes("strategy") || normalized.includes("research")) {
    return WORKSTATION_ROUTE_CATALOG.strategy;
  }

  if (normalized.includes("data") || normalized.includes("provider") || normalized.includes("backfill")) {
    return WORKSTATION_ROUTE_CATALOG.dataProviders;
  }

  return WORKSTATION_ROUTE_CATALOG.settingsBackendCapabilityCoverage;
}

function labelForSystemEventSource(source: string): string {
  const normalized = source.trim().toLowerCase();
  if (normalized === "research") {
    return "Strategy";
  }

  if (normalized === "data operations") {
    return "Data";
  }

  if (normalized === "governance") {
    return "Accounting";
  }

  return source;
}

function dedupeEvidenceTimelineCandidates(candidates: EvidenceTimelineCandidate[]): EvidenceTimelineCandidate[] {
  const byId = new Map<string, EvidenceTimelineCandidate>();
  candidates.forEach((candidate) => {
    const existing = byId.get(candidate.id);
    if (!existing || compareEvidenceTimelineCandidates(candidate, existing) < 0) {
      byId.set(candidate.id, candidate);
    }
  });

  return Array.from(byId.values());
}

function compareEvidenceTimelineCandidates(left: EvidenceTimelineCandidate, right: EvidenceTimelineCandidate): number {
  return right.occurredAtMs - left.occurredAtMs
    || operatorFocusTonePriority(left.tone) - operatorFocusTonePriority(right.tone)
    || left.sourcePriority - right.sourcePriority
    || left.sourceIndex - right.sourceIndex
    || left.label.localeCompare(right.label);
}

function toEvidenceTimelineItem(
  candidate: EvidenceTimelineCandidate,
  operatingScope: AppShellOperatingScopeState
): AppShellEvidenceTimelineItem {
  const {
    occurredAtMs: _occurredAtMs,
    sourcePriority: _sourcePriority,
    sourceIndex: _sourceIndex,
    route,
    ...item
  } = candidate;
  return {
    ...item,
    route: appendOperatingScopeToRoute(route, operatingScope)
  };
}

function materializeScopedLinkedContextItem(
  item: AppShellLinkedContextItem,
  operatingScope: AppShellOperatingScopeState
): AppShellLinkedContextItem {
  return {
    ...item,
    route: appendOperatingScopeToRoute(item.route, operatingScope)
  };
}
function formatCount(count: number, singular: string, plural = `${singular}s`): string {
  return pluralizeCount(count, singular, { plural });
}

function formatStatusCount(count: number, label: string): string {
  return `${count} ${label}`;
}

function buildWorkflowContinuitySummary(
  trailSummary: string,
  activeLabel: string,
  nextLabel: string,
  subjectSymbol: string | null,
  attentionCount = 0
) {
  const subject = subjectSymbol ? ` Subject: ${subjectSymbol}.` : "";
  const attention = attentionCount > 0
    ? ` ${attentionCount} step${attentionCount === 1 ? "" : "s"} need operator attention.`
    : " All loaded steps are clear.";
  return `${trailSummary} Current: ${activeLabel}. Next: ${nextLabel}.${subject}${attention}`;
}
