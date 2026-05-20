import {
  normalizeLocalWorkstationRoute,
  normalizeWorkspacePath,
  WORKSPACES,
  WORKSTATION_ROUTE_CATALOG,
  workflowTargetPath,
  workstationRouteWithQuery,
  workspaceForPath,
  workspacePath
} from "@/lib/workspace";
import type {
  DataOperationsBackfillRecord,
  DataOperationsProviderRecord,
  DataOperationsWorkspaceResponse,
  GovernanceWorkspaceResponse,
  OperatorWorkItem,
  PortfolioWorkspaceResponse,
  ReconciliationBreakQueueItem,
  ResearchRunRecord,
  ResearchWorkspaceResponse,
  SessionInfo,
  SystemOverviewResponse,
  TradingAcceptanceGate,
  TradingWorkspaceResponse,
  WorkspaceKey,
  WorkspaceSummary
} from "@/types";

export type ShellStatusTone = "loading" | "warning" | "danger";

export interface ShellStatusItem {
  key: string;
  label: string;
  detail: string;
  ariaLabel: string;
}

export interface ShellStatusPanel {
  id: string;
  titleId: string;
  detailId: string;
  tone: ShellStatusTone;
  title: string;
  detail: string;
  role: "status" | "alert";
  ariaLive: "polite" | "assertive";
  actionLabel: string | null;
  actionAriaLabel: string | null;
  secondaryActionLabel: string | null;
  secondaryActionAriaLabel: string | null;
  secondaryActionHref: string | null;
  itemListLabel: string;
  items: ShellStatusItem[];
}

export interface AppShellViewState {
  activeWorkspace: WorkspaceSummary;
  statusPanel: ShellStatusPanel | null;
  canRenderRoutes: boolean;
  routeFocus: AppShellRouteFocusState;
  workflowContinuity: AppShellWorkflowContinuityViewModel;
  commandPaletteTrigger: AppShellCommandPaletteTriggerState;
}

export interface AppShellWorkflowContinuityStep {
  id: string;
  label: string;
  description: string;
  href: string;
  ariaLabel: string;
  statusLabel: string;
  statusTone: AppShellWorkflowContinuityStatusTone;
  active: boolean;
  next: boolean;
}

export type AppShellWorkflowContinuityStatusTone = "ready" | "review" | "blocked" | "pending";

export interface AppShellOperatingScopeInput {
  symbol?: string | null;
  fundAccountId?: string | null;
  runId?: string | null;
  provider?: string | null;
  from?: string | null;
  to?: string | null;
  date?: string | null;
  asOf?: string | null;
}

export interface AppShellOperatingScopeItem {
  id: string;
  label: string;
  value: string;
  ariaLabel: string;
}

export interface AppShellOperatingScopeQueryParam {
  key: string;
  value: string;
  scopeKey: AppShellOperatingScopeQueryKey;
}

export type AppShellOperatingScopeQueryKey = "symbol" | "fundAccountId" | "runId" | "provider" | "window";

export interface AppShellOperatingScopeState {
  label: string;
  summary: string;
  subjectSymbol: string | null;
  fundAccountId: string | null;
  runId: string | null;
  provider: string | null;
  hasScope: boolean;
  clearAriaLabel: string | null;
  items: AppShellOperatingScopeItem[];
  queryParams: AppShellOperatingScopeQueryParam[];
}

export interface AppShellDecisionBrief {
  label: string;
  title: string;
  summary: string;
  reasonLabel: string;
  reason: string;
  statusLabel: string;
  statusTone: AppShellWorkflowContinuityStatusTone;
  evidenceLabel: string | null;
  actionLabel: string;
  actionHref: string;
  actionAriaLabel: string;
}

export interface AppShellWorkflowContinuityViewModel {
  title: string;
  summary: string;
  contextLabel: string;
  contextValue: string;
  subjectSymbol: string | null;
  clearSubjectAriaLabel: string | null;
  operatingScope: AppShellOperatingScopeState;
  routeLabel: string;
  stepsLabel: string;
  ariaLabel: string;
  nextActionLabel: string;
  nextActionAriaLabel: string;
  nextActionHref: string;
  decisionBrief: AppShellDecisionBrief;
  steps: AppShellWorkflowContinuityStep[];
  operatorFocusLabel: string;
  operatorFocusSummary: string;
  operatorFocusEmptyText: string;
  operatorFocusItemsLabel: string;
  operatorFocusOverflowLabel: string | null;
  operatorFocusItems: AppShellOperatorFocusItem[];
  operatorFocusCommandItems: AppShellOperatorFocusItem[];
  evidenceTimelineLabel: string;
  evidenceTimelineSummary: string;
  evidenceTimelineEmptyText: string;
  evidenceTimelineItemsLabel: string;
  evidenceTimelineOverflowLabel: string | null;
  evidenceTimelineItems: AppShellEvidenceTimelineItem[];
  linkedContextLabel: string;
  linkedContextSummary: string;
  linkedContextPostureLabel: string;
  linkedContextPostureTone: AppShellWorkflowContinuityStatusTone;
  linkedContextPrimaryActionLabel: string | null;
  linkedContextPrimaryActionHref: string | null;
  linkedContextPrimaryActionAriaLabel: string | null;
  linkedContextEmptyText: string;
  linkedContextItemsLabel: string;
  linkedContextItems: AppShellLinkedContextItem[];
}

export interface AppShellOperatorFocusItem {
  id: string;
  label: string;
  detail: string;
  route: string;
  workspaceLabel: string;
  actionLabel: string;
  tone: AppShellWorkflowContinuityStatusTone;
  ariaLabel: string;
}

export interface AppShellEvidenceTimelineItem {
  id: string;
  label: string;
  detail: string;
  route: string;
  workspaceLabel: string;
  timestampLabel: string;
  timestampIso: string;
  tone: AppShellWorkflowContinuityStatusTone;
  ariaLabel: string;
}

export interface AppShellLinkedContextItem {
  id: string;
  label: string;
  detail: string;
  route: string;
  workspaceLabel: string;
  statusLabel: string;
  tone: AppShellWorkflowContinuityStatusTone;
  ariaLabel: string;
}

export interface AppShellCommandPaletteTriggerState {
  label: string;
  placeholder: string;
  shortcutLabel: string;
  controlsId: string;
  expanded: boolean;
  hasPopup: "dialog";
}

export interface DevelopmentFixtureNoticeStep {
  id: "watchlist" | "quotes" | "readiness" | "connect";
  step: string;
  href: string;
  label: string;
  ariaLabel: string;
  active: boolean;
}

export interface DevelopmentFixtureNoticeViewModel {
  role: "status";
  ariaLive: "polite";
  title: string;
  detail: string;
  workflowLabel: string;
  retryLabel: string;
  retryAriaLabel: string;
  retryDisabled: boolean;
  retryBusy: boolean;
  steps: DevelopmentFixtureNoticeStep[];
}

export interface AppShellRouteFocusState {
  routeKey: string;
  announcement: string;
  documentTitle: string;
  targetElementId: string | null;
  fallbackElementId: string;
}

export interface AppShellWorkspacePayload {
  session: SessionInfo | null;
  overview: SystemOverviewResponse | null;
  research: ResearchWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  portfolio: PortfolioWorkspaceResponse | null;
  dataOperations: DataOperationsWorkspaceResponse | null;
  governance: GovernanceWorkspaceResponse | null;
  reporting: GovernanceWorkspaceResponse | null;
}

export type WorkspaceErrorMap = Partial<Record<WorkspaceKey, string>>;

export interface BuildAppShellViewStateOptions {
  pathname: string;
  search?: string;
  hash?: string;
  operatingContextSymbol?: string | null;
  operatingContextScope?: AppShellOperatingScopeInput | null;
  commandPaletteOpen?: boolean;
  loading: boolean;
  error: string | null;
  workflowError?: string | null;
  workspaceErrors: WorkspaceErrorMap;
  payload: AppShellWorkspacePayload;
}

export interface AppShellCommandPaletteShortcutState {
  key: string;
  ctrlKey?: boolean;
  metaKey?: boolean;
  altKey?: boolean;
  shiftKey?: boolean;
  targetIsEditable?: boolean;
  commandPaletteOpen: boolean;
}

export type AppShellCommandPaletteShortcutCommand = "toggle-command-palette" | null;

export const COMMAND_PALETTE_DIALOG_ID = "command-palette-dialog";

export function buildAppShellViewState({
  pathname,
  search = "",
  hash = "",
  operatingContextSymbol = null,
  operatingContextScope = null,
  commandPaletteOpen = false,
  loading,
  error,
  workflowError = null,
  workspaceErrors,
  payload
}: BuildAppShellViewStateOptions): AppShellViewState {
  const activeWorkspace = getWorkspaceForPath(pathname);
  const failedItems = buildShellFailureItems(workspaceErrors, workflowError);
  const hasAnyPayload = Object.values(payload).some(Boolean);
  const bootstrapFailed = !loading && !hasAnyPayload;

  return {
    activeWorkspace,
    statusPanel: buildShellStatusPanel({
      loading,
      error,
      failedItems,
      bootstrapFailed
    }),
    canRenderRoutes: !loading && !bootstrapFailed,
    routeFocus: buildRouteFocusState(pathname, search, hash, activeWorkspace),
    workflowContinuity: buildWorkflowContinuityViewModel(
      pathname,
      search,
      hash,
      activeWorkspace,
      {
        loading,
        error,
        workflowError,
        workspaceErrors,
        payload
      },
      operatingContextSymbol,
      operatingContextScope
    ),
    commandPaletteTrigger: buildCommandPaletteTriggerState(commandPaletteOpen)
  };
}

export function buildCommandPaletteTriggerState(open: boolean): AppShellCommandPaletteTriggerState {
  return {
    label: open ? "Close workstation command palette (Ctrl K)" : "Open workstation command palette (Ctrl K)",
    placeholder: "Search workflows, routes, presets...",
    shortcutLabel: "Ctrl K",
    controlsId: COMMAND_PALETTE_DIALOG_ID,
    expanded: open,
    hasPopup: "dialog"
  };
}

export function resolveAppShellCommandPaletteShortcut({
  key,
  ctrlKey = false,
  metaKey = false,
  altKey = false,
  shiftKey = false,
  targetIsEditable = false,
  commandPaletteOpen
}: AppShellCommandPaletteShortcutState): AppShellCommandPaletteShortcutCommand {
  const isShortcut = (ctrlKey || metaKey) && !altKey && !shiftKey && key.toLowerCase() === "k";
  if (!isShortcut) {
    return null;
  }

  if (targetIsEditable && !commandPaletteOpen) {
    return null;
  }

  return "toggle-command-palette";
}

export function isAppShellEditableShortcutTarget(target: EventTarget | null): boolean {
  const element = target instanceof Element ? target : null;
  if (!element) {
    return false;
  }

  if (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement || element instanceof HTMLSelectElement) {
    return true;
  }

  const editableContainer = element.closest("[contenteditable]");
  if (!editableContainer) {
    return false;
  }

  return editableContainer.getAttribute("contenteditable") !== "false";
}

export function getWorkspaceForPath(pathname: string): WorkspaceSummary {
  return workspaceForPath(pathname);
}

export function normalizeWorkspace(pathname: string): WorkspaceKey {
  return normalizeWorkspacePath(pathname);
}

export function buildDevelopmentFixtureNoticeViewModel({
  pathname,
  hash = "",
  refreshing = false
}: {
  pathname: string;
  hash?: string;
  refreshing?: boolean;
}): DevelopmentFixtureNoticeViewModel {
  return {
    role: "status",
    ariaLive: "polite",
    title: "Demo data",
    detail: "Showing local fixture responses because the Meridian API host is unavailable.",
    workflowLabel: "Evidence path",
    retryLabel: refreshing ? "Retrying live data" : "Retry live data",
    retryAriaLabel: refreshing
      ? "Retrying Meridian API host and live workstation data"
      : "Retry Meridian API host and reload live workstation data",
    retryDisabled: refreshing,
    retryBusy: refreshing,
    steps: developmentFixtureDemoSteps.map((item) => ({
      ...item,
      active: isCurrentDevelopmentFixtureDemoStep(item, pathname, hash)
    }))
  };
}

export function buildRouteFocusState(
  pathname: string,
  search: string,
  hash: string,
  activeWorkspace: WorkspaceSummary
): AppShellRouteFocusState {
  const workspaceTitle = `${activeWorkspace.label} Workstation`;
  const targetElementId = normalizeHashTarget(hash);
  const targetLabel = targetElementId ? formatHashTargetLabel(targetElementId) : null;

  return {
    routeKey: `${pathname}${search}${hash}`,
    announcement: targetLabel
      ? `${workspaceTitle} loaded. Jumping to ${targetLabel}.`
      : `${workspaceTitle} loaded.`,
    documentTitle: `${workspaceTitle} - Meridian`,
    targetElementId,
    fallbackElementId: "workbench-content"
  };
}

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
  });
  const subjectSymbol = operatingScope.subjectSymbol;
  const currentRoute = `${pathname}${search}${hash}`;
  const trail = selectWorkflowContinuityTrail(pathname, hash);
  const steps = trail.steps.map((step) => materializeContinuityStep(step, operatingScope));
  const activeIndex = findActiveWorkflowStepIndex(trail.steps, pathname, hash);
  const nextIndex = steps.length > 0 ? Math.min(activeIndex + 1, steps.length - 1) : 0;
  const activeStep = steps[activeIndex] ?? null;
  const nextStep = steps[nextIndex] ?? activeStep;
  const stepStatuses = steps.map((step) => buildWorkflowContinuityStepStatus(step.id, statusContext));
  const attentionCount = stepStatuses.filter((status) => status.tone === "blocked" || status.tone === "review").length;
  const operatorFocus = buildOperatorFocusViewModel(statusContext, operatingScope);
  const evidenceTimeline = buildEvidenceTimelineViewModel(statusContext, operatingScope);
  const linkedContext = buildLinkedContextViewModel(statusContext, subjectSymbol, operatingScope);
  const contextValue = subjectSymbol
    ? `${activeWorkspace.label} / ${subjectSymbol}`
    : operatingScope.hasScope
      ? `${activeWorkspace.label} / Scoped workflow`
    : `${activeWorkspace.label} / ${activeStep?.label ?? "Workspace"}`;
  const nextActionLabel = activeStep && nextStep && activeStep.id === nextStep.id
    ? `Stay on ${activeStep.label}`
    : `Next: ${nextStep?.label ?? activeWorkspace.label}`;
  const nextActionAriaLabel = activeStep && nextStep && activeStep.id === nextStep.id
    ? `Stay on ${activeStep.label}`
    : `Continue workflow to ${nextStep?.label ?? activeWorkspace.label}`;
  const nextActionHref = nextStep?.href ?? workspacePath(activeWorkspace.key);
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
    title: trail.title,
    summary: buildWorkflowContinuitySummary(
      trail.summary,
      activeStep?.label ?? activeWorkspace.label,
      nextStep?.label ?? activeWorkspace.label,
      subjectSymbol,
      attentionCount
    ),
    contextLabel: "Operating context",
    contextValue,
    subjectSymbol,
    clearSubjectAriaLabel: operatingScope.clearAriaLabel,
    operatingScope,
    routeLabel: currentRoute || "/",
    stepsLabel: `${trail.title} workflow steps`,
    ariaLabel: `${trail.title} continuity`,
    nextActionLabel,
    nextActionAriaLabel,
    nextActionHref,
    decisionBrief,
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
      const active = index === activeIndex;
      const next = index === nextIndex && index !== activeIndex;
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

function buildShellStatusPanel({
  loading,
  error,
  failedItems,
  bootstrapFailed
}: {
  loading: boolean;
  error: string | null;
  failedItems: ShellStatusItem[];
  bootstrapFailed: boolean;
}): ShellStatusPanel | null {
  if (loading) {
    return {
      id: "workstation-shell-status-loading",
      titleId: "workstation-shell-status-loading-title",
      detailId: "workstation-shell-status-loading-detail",
      tone: "loading",
      title: "Booting workstation shell",
      detail: "Loading session state, operator workspaces, and the initial workstation evidence slices.",
      role: "status",
      ariaLive: "polite",
      actionLabel: null,
      actionAriaLabel: null,
      secondaryActionLabel: null,
      secondaryActionAriaLabel: null,
      secondaryActionHref: null,
      itemListLabel: "Workspace bootstrap status",
      items: [
        {
          key: "session-state",
          label: "Session state",
          detail: "Resolving operator context and environment guardrails.",
          ariaLabel: "Session state: resolving operator context and environment guardrails"
        },
        {
          key: "workspace-payloads",
          label: "Workspace payloads",
          detail: "Loading Trading, Portfolio, Accounting, Reporting, Strategy, Data, and Settings.",
          ariaLabel: "Workspace payloads: loading Trading, Portfolio, Accounting, Reporting, Strategy, Data, and Settings"
        },
        {
          key: "evidence-slices",
          label: "Evidence slices",
          detail: "Preparing readiness, reconciliation, provider, and report-pack evidence.",
          ariaLabel: "Evidence slices: preparing readiness, reconciliation, provider, and report-pack evidence"
        }
      ]
    };
  }

  if (bootstrapFailed) {
    return {
      id: "workstation-shell-status-failed",
      titleId: "workstation-shell-status-failed-title",
      detailId: "workstation-shell-status-failed-detail",
      tone: "danger",
      title: "Workstation bootstrap failed",
      detail: error ?? "No workstation payloads loaded. Retry the bootstrap before reviewing operator state.",
      role: "alert",
      ariaLive: "assertive",
      actionLabel: "Retry bootstrap",
      actionAriaLabel: "Retry workstation bootstrap",
      secondaryActionLabel: null,
      secondaryActionAriaLabel: null,
      secondaryActionHref: null,
      itemListLabel: "Bootstrap failure details",
      items: failedItems
    };
  }

  if (failedItems.length > 0) {
    const sliceLabel = failedItems.length === 1 ? "slice" : "slices";
    const recoveryLabel = failedItems.length === 1 ? "that slice recovers" : "those slices recover";
    return {
      id: "workstation-shell-status-degraded",
      titleId: "workstation-shell-status-degraded-title",
      detailId: "workstation-shell-status-degraded-detail",
      tone: "warning",
      title: "Workstation bootstrap is partially degraded",
      detail: `${failedItems.length} workstation ${sliceLabel} failed to load. Available routes remain open while ${recoveryLabel}.`,
      role: "status",
      ariaLive: "polite",
      actionLabel: "Retry failed slices",
      actionAriaLabel: "Retry failed workstation slices",
      secondaryActionLabel: "Review diagnostics",
      secondaryActionAriaLabel: "Review Settings capability coverage for failed workstation slices",
      secondaryActionHref: WORKSTATION_ROUTE_CATALOG.settingsBackendCapabilityCoverage,
      itemListLabel: "Failed workstation slices",
      items: failedItems
    };
  }

  return null;
}

function buildShellFailureItems(workspaceErrors: WorkspaceErrorMap, workflowError: string | null): ShellStatusItem[] {
  const items: ShellStatusItem[] = Object.entries(workspaceErrors)
    .map(([key, detail]) => {
      const workspaceKey = key as WorkspaceKey;
      const label = WORKSPACES.find((workspace) => workspace.key === workspaceKey)?.label ?? key;
      return {
        key: workspaceKey,
        label,
        detail: detail || "Workspace request failed.",
        ariaLabel: `${label}: ${detail || "Workspace request failed."}`
      };
    })
    .sort((left, right) => left.label.localeCompare(right.label));

  if (workflowError) {
    items.push({
      key: "workflow-catalog",
      label: "Workflow catalog",
      detail: workflowError,
      ariaLabel: `Workflow catalog: ${workflowError}`
    });
  }

  return items;
}

function normalizeHashTarget(hash: string): string | null {
  if (!hash.startsWith("#") || hash.length <= 1) {
    return null;
  }

  try {
    return decodeURIComponent(hash.slice(1));
  } catch {
    return hash.slice(1);
  }
}

function formatHashTargetLabel(targetElementId: string): string {
  return targetElementId
    .split(/[-_\s]+/)
    .filter(Boolean)
    .join(" ")
    .toLowerCase();
}

interface WorkflowContinuityTrailStepDefinition {
  id: string;
  label: string;
  description: string;
  href: string;
  matchPath: string;
  matchHash?: string;
  preserveSymbol?: boolean;
}

interface WorkflowContinuityTrailDefinition {
  id: string;
  title: string;
  summary: string;
  steps: WorkflowContinuityTrailStepDefinition[];
}

interface WorkflowContinuityStatusContext {
  loading: boolean;
  error: string | null;
  workflowError: string | null;
  workspaceErrors: WorkspaceErrorMap;
  payload: AppShellWorkspacePayload;
}

interface WorkflowContinuityStepStatus {
  label: string;
  tone: AppShellWorkflowContinuityStatusTone;
}

const emptyWorkflowContinuityStatusContext: WorkflowContinuityStatusContext = {
  loading: false,
  error: null,
  workflowError: null,
  workspaceErrors: {},
  payload: {
    session: null,
    overview: null,
    research: null,
    trading: null,
    portfolio: null,
    dataOperations: null,
    governance: null,
    reporting: null
  }
};

const workflowContinuityTrails: WorkflowContinuityTrailDefinition[] = [
  {
    id: "market-data-to-paper",
    title: "Market Data To Paper",
    summary: "Move from symbol selection through quote validation, alert monitoring, paper readiness, and provider repair without memorizing route order.",
    steps: [
      {
        id: "watchlist",
        label: "Watchlist",
        description: "Choose the monitored universe and starter packs before market-data validation.",
        href: WORKSTATION_ROUTE_CATALOG.dataWatchlist,
        matchPath: WORKSTATION_ROUTE_CATALOG.dataWatchlist
      },
      {
        id: "quotes",
        label: "Live quotes",
        description: "Inspect quote, tape, depth, and historical trend evidence for the active symbol.",
        href: WORKSTATION_ROUTE_CATALOG.dataQuotes,
        matchPath: WORKSTATION_ROUTE_CATALOG.dataQuotes,
        preserveSymbol: true
      },
      {
        id: "alerts",
        label: "Price alerts",
        description: "Track threshold triggers and validate the quote feed behind watched symbols.",
        href: WORKSTATION_ROUTE_CATALOG.dataAlerts,
        matchPath: WORKSTATION_ROUTE_CATALOG.dataAlerts,
        preserveSymbol: true
      },
      {
        id: "readiness",
        label: "Readiness",
        description: "Review paper-operation blockers, execution controls, replay evidence, and work items.",
        href: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
        matchPath: WORKSTATION_ROUTE_CATALOG.tradingReadiness
      },
      {
        id: "provider-setup",
        label: "Provider setup",
        description: "Repair credentials, endpoint acknowledgement, and paper/live provider posture.",
        href: WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup,
        matchPath: WORKSTATION_ROUTE_CATALOG.settings,
        matchHash: "#alpaca-provider-setup"
      }
    ]
  },
  {
    id: "strategy-to-paper",
    title: "Strategy To Paper",
    summary: "Keep research comparison, strategy design, backtest evidence, paper-session readiness, portfolio impact, and audit packet review connected.",
    steps: [
      {
        id: "strategy-runs",
        label: "Run library",
        description: "Compare runs, inspect promotion history, and select the evidence candidate.",
        href: WORKSTATION_ROUTE_CATALOG.strategy,
        matchPath: WORKSTATION_ROUTE_CATALOG.strategy
      },
      {
        id: "quant-lab",
        label: "Quant Lab",
        description: "Prototype scripts, parameters, plots, and diagnostics against trusted data.",
        href: WORKSTATION_ROUTE_CATALOG.strategyQuantLab,
        matchPath: WORKSTATION_ROUTE_CATALOG.strategyQuantLab
      },
      {
        id: "covered-call",
        label: "Covered call",
        description: "Preview option chains, run covered-call scenarios, and inspect trade outcomes.",
        href: WORKSTATION_ROUTE_CATALOG.strategyCoveredCall,
        matchPath: WORKSTATION_ROUTE_CATALOG.strategyCoveredCall
      },
      {
        id: "paper-readiness",
        label: "Paper readiness",
        description: "Confirm replay consistency, acceptance gates, execution controls, and approval blockers.",
        href: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
        matchPath: WORKSTATION_ROUTE_CATALOG.tradingReadiness
      },
      {
        id: "portfolio-review",
        label: "Portfolio review",
        description: "Check exposure, account sync, positions, cash, and run-to-portfolio continuity.",
        href: WORKSTATION_ROUTE_CATALOG.portfolio,
        matchPath: WORKSTATION_ROUTE_CATALOG.portfolio
      },
      {
        id: "evidence-review",
        label: "Evidence review",
        description: "Package lineage, stale evidence, and packet completeness for governed review.",
        href: WORKSTATION_ROUTE_CATALOG.reportingEvidence,
        matchPath: WORKSTATION_ROUTE_CATALOG.reportingEvidence
      }
    ]
  },
  {
    id: "trading-governance",
    title: "Trading Governance",
    summary: "Hold execution readiness, cockpit action, portfolio exposure, reconciliation, and report-pack review in one operational path.",
    steps: [
      {
        id: "trading-readiness",
        label: "Readiness",
        description: "Review gates, replay, execution controls, trust checks, and operator work items.",
        href: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
        matchPath: WORKSTATION_ROUTE_CATALOG.tradingReadiness
      },
      {
        id: "trading-cockpit",
        label: "Trading cockpit",
        description: "Stage paper orders, inspect positions, monitor fills, and control strategy actions.",
        href: WORKSTATION_ROUTE_CATALOG.trading,
        matchPath: WORKSTATION_ROUTE_CATALOG.trading
      },
      {
        id: "portfolio-exposure",
        label: "Exposure",
        description: "Review household positions, account sync, cash, buying power, and risk posture.",
        href: WORKSTATION_ROUTE_CATALOG.portfolio,
        matchPath: WORKSTATION_ROUTE_CATALOG.portfolio
      },
      {
        id: "reconciliation",
        label: "Reconciliation",
        description: "Resolve ledger, security, cash, and position breaks before accepting readiness.",
        href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
        matchPath: WORKSTATION_ROUTE_CATALOG.accountingReconciliation
      },
      {
        id: "report-packs",
        label: "Report packs",
        description: "Review governed output targets, evidence readiness, and export posture.",
        href: WORKSTATION_ROUTE_CATALOG.reportingReportPacks,
        matchPath: WORKSTATION_ROUTE_CATALOG.reportingReportPacks
      }
    ]
  },
  {
    id: "accounting-closeout",
    title: "Accounting Closeout",
    summary: "Move through reference-data coverage, reconciliation, ledger evidence, audit lineage, and report packaging with the close context intact.",
    steps: [
      {
        id: "security-master",
        label: "Security Master",
        description: "Review instrument identity, provider aliases, lots, conflicts, and coverage gaps.",
        href: WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster,
        matchPath: WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster
      },
      {
        id: "reconciliation",
        label: "Reconciliation",
        description: "Inspect run breaks, tolerance profile health, sign-off status, and recovery actions.",
        href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
        matchPath: WORKSTATION_ROUTE_CATALOG.accountingReconciliation
      },
      {
        id: "ledger",
        label: "Ledger",
        description: "Validate trial balance, cash-flow context, account detail, and accounting basis.",
        href: WORKSTATION_ROUTE_CATALOG.accountingLedger,
        matchPath: WORKSTATION_ROUTE_CATALOG.accountingLedger
      },
      {
        id: "evidence",
        label: "Evidence",
        description: "Trace packet lineage, freshness, completeness, and unresolved evidence warnings.",
        href: WORKSTATION_ROUTE_CATALOG.reportingEvidence,
        matchPath: WORKSTATION_ROUTE_CATALOG.reportingEvidence
      },
      {
        id: "report-packs",
        label: "Report packs",
        description: "Package close evidence into governed report outputs and approval-ready exports.",
        href: WORKSTATION_ROUTE_CATALOG.reportingReportPacks,
        matchPath: WORKSTATION_ROUTE_CATALOG.reportingReportPacks
      }
    ]
  }
];

const defaultWorkflowContinuityTrail = workflowContinuityTrails[0];

function selectWorkflowContinuityTrail(pathname: string, hash: string): WorkflowContinuityTrailDefinition {
  const workspaceKey = normalizeWorkspace(pathname);
  const scoredTrails = workflowContinuityTrails
    .map((trail, index) => ({
      trail,
      index,
      workspaceAffinity: scoreWorkflowTrailWorkspaceAffinity(trail.id, workspaceKey),
      score: Math.max(...trail.steps.map((step) => scoreWorkflowStepRouteMatch(step, pathname, hash)))
    }))
    .filter((match) => match.score > 0)
    .sort((left, right) => right.score - left.score || right.workspaceAffinity - left.workspaceAffinity || left.index - right.index);

  if (scoredTrails.length > 0) {
    return scoredTrails[0].trail;
  }

  switch (workspaceKey) {
    case "accounting":
    case "reporting":
      return workflowContinuityTrails.find((trail) => trail.id === "accounting-closeout") ?? defaultWorkflowContinuityTrail;
    case "strategy":
      return workflowContinuityTrails.find((trail) => trail.id === "strategy-to-paper") ?? defaultWorkflowContinuityTrail;
    case "trading":
    case "portfolio":
      return workflowContinuityTrails.find((trail) => trail.id === "trading-governance") ?? defaultWorkflowContinuityTrail;
    case "data":
    case "settings":
    default:
      return defaultWorkflowContinuityTrail;
  }
}

function scoreWorkflowTrailWorkspaceAffinity(trailId: string, workspaceKey: WorkspaceKey): number {
  if (workspaceKey === "data" || workspaceKey === "settings") {
    return trailId === "market-data-to-paper" ? 1 : 0;
  }

  if (workspaceKey === "strategy") {
    return trailId === "strategy-to-paper" ? 1 : 0;
  }

  if (workspaceKey === "accounting" || workspaceKey === "reporting") {
    return trailId === "accounting-closeout" ? 1 : 0;
  }

  if (workspaceKey === "trading" || workspaceKey === "portfolio") {
    return trailId === "trading-governance" ? 1 : 0;
  }

  return 0;
}

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

function findActiveWorkflowStepIndex(
  steps: WorkflowContinuityTrailStepDefinition[],
  pathname: string,
  hash: string
) {
  const scoredSteps = steps
    .map((step, index) => ({
      index,
      score: scoreWorkflowStepRouteMatch(step, pathname, hash)
    }))
    .filter((match) => match.score > 0)
    .sort((left, right) => right.score - left.score || left.index - right.index);

  return scoredSteps[0]?.index ?? 0;
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

  switch (stepId) {
    case "watchlist":
    case "quotes":
    case "alerts":
    case "trusted-data":
      return buildTrustedDataContinuityStatus(context);
    case "strategy-runs":
    case "quant-lab":
    case "covered-call":
    case "research":
      return buildResearchContinuityStatus(context);
    case "readiness":
    case "paper-readiness":
    case "trading-readiness":
    case "trading-cockpit":
      return buildPaperReadinessContinuityStatus(context);
    case "provider-setup":
      return buildProviderSetupContinuityStatus(context);
    case "portfolio-review":
    case "portfolio-exposure":
    case "portfolio-ledger":
      return buildPortfolioLedgerContinuityStatus(context);
    case "security-master":
    case "ledger":
    case "reconciliation":
      return buildReconciliationContinuityStatus(context);
    case "evidence":
    case "evidence-review":
    case "report-packs":
    case "governed-report":
      return buildGovernedReportContinuityStatus(context);
    default:
      return context.error || context.workflowError
        ? { label: "Review", tone: "review" }
        : { label: "Ready", tone: "ready" };
  }
}

const workflowContinuityWorkspaceErrors: Record<string, WorkspaceKey[]> = {
  watchlist: ["data"],
  quotes: ["data"],
  alerts: ["data"],
  "trusted-data": ["data"],
  "strategy-runs": ["strategy"],
  "quant-lab": ["strategy"],
  "covered-call": ["strategy"],
  research: ["strategy"],
  readiness: ["trading"],
  "paper-readiness": ["trading"],
  "trading-readiness": ["trading"],
  "trading-cockpit": ["trading"],
  "provider-setup": ["settings"],
  "portfolio-review": ["portfolio"],
  "portfolio-exposure": ["portfolio"],
  "portfolio-ledger": ["portfolio"],
  "security-master": ["accounting"],
  ledger: ["accounting"],
  reconciliation: ["accounting"],
  evidence: ["reporting"],
  "evidence-review": ["reporting"],
  "report-packs": ["reporting"],
  "governed-report": ["reporting"]
};

function buildTrustedDataContinuityStatus({ payload }: WorkflowContinuityStatusContext): WorkflowContinuityStepStatus {
  const data = payload.dataOperations;
  if (!data) {
    return { label: "Waiting", tone: "pending" };
  }

  const providerAttentionCount = (data.providers ?? []).filter((provider) => provider.status !== "Healthy").length;
  const backfillAttentionCount = (data.backfills ?? []).filter((backfill) => backfill.status === "Review").length;
  const attentionCount = providerAttentionCount + backfillAttentionCount;
  return attentionCount > 0
    ? { label: `${attentionCount} review`, tone: "review" }
    : { label: "Trusted", tone: "ready" };
}

function buildResearchContinuityStatus({ payload, workflowError }: WorkflowContinuityStatusContext): WorkflowContinuityStepStatus {
  if (workflowError) {
    return { label: "Catalog degraded", tone: "review" };
  }

  const research = payload.research;
  if (!research) {
    return { label: "Waiting", tone: "pending" };
  }

  const reviewCount = (research.runs ?? []).filter((run) => run.status === "Needs Review").length;
  if (reviewCount > 0) {
    return { label: `${reviewCount} review`, tone: "review" };
  }

  const activeCount = (research.runs ?? []).filter((run) => run.status === "Running" || run.status === "Queued").length;
  if (activeCount > 0) {
    return { label: `${activeCount} active`, tone: "review" };
  }

  return { label: (research.runs ?? []).length > 0 ? `${research.runs.length} runs` : "Ready", tone: "ready" };
}

function buildPaperReadinessContinuityStatus({ payload }: WorkflowContinuityStatusContext): WorkflowContinuityStepStatus {
  const readiness = payload.trading?.readiness ?? null;
  if (!readiness) {
    return payload.trading ? { label: "Review", tone: "review" } : { label: "Waiting", tone: "pending" };
  }

  const criticalCount = (readiness.workItems ?? []).filter((item) => item.tone === "Critical").length;
  if (readiness.overallStatus === "Blocked" || criticalCount > 0) {
    return { label: criticalCount > 0 ? `${criticalCount} critical` : "Blocked", tone: "blocked" };
  }

  const attentionCount = (readiness.workItems ?? []).filter((item) => item.tone === "Warning" || item.tone === "Info").length;
  if (readiness.overallStatus === "ReviewRequired" || attentionCount > 0) {
    return { label: attentionCount > 0 ? `${attentionCount} review` : "Review", tone: "review" };
  }

  return readiness.readyForPaperOperation
    ? { label: "Ready", tone: "ready" }
    : { label: "Review", tone: "review" };
}

function buildProviderSetupContinuityStatus({ payload, error, workflowError }: WorkflowContinuityStatusContext): WorkflowContinuityStepStatus {
  if (error || workflowError) {
    return { label: "Review", tone: "review" };
  }

  return payload.session || payload.overview
    ? { label: "Available", tone: "ready" }
    : { label: "Waiting", tone: "pending" };
}

function buildPortfolioLedgerContinuityStatus({ payload }: WorkflowContinuityStatusContext): WorkflowContinuityStepStatus {
  const portfolio = payload.portfolio;
  if (!portfolio) {
    return { label: "Waiting", tone: "pending" };
  }

  if (portfolio.risk.state === "Constrained") {
    return { label: "Constrained", tone: "blocked" };
  }

  if (portfolio.risk.state === "Observe") {
    return { label: "Observe", tone: "review" };
  }

  return (portfolio.positions ?? []).length > 0
    ? { label: `${portfolio.positions.length} positions`, tone: "ready" }
    : { label: "Ready", tone: "ready" };
}

function buildReconciliationContinuityStatus({ payload }: WorkflowContinuityStatusContext): WorkflowContinuityStepStatus {
  const governance = payload.governance;
  if (!governance) {
    return { label: "Waiting", tone: "pending" };
  }

  const breakCount = (governance.breakQueue ?? []).filter((item) => item.status === "Open" || item.status === "InReview").length;
  const runBreakCount = (governance.reconciliationQueue ?? []).reduce((total, row) => total + row.openBreakCount, 0);
  const attentionCount = Math.max(breakCount, runBreakCount);
  return attentionCount > 0
    ? { label: `${attentionCount} breaks`, tone: "review" }
    : { label: "Balanced", tone: "ready" };
}

function buildGovernedReportContinuityStatus({ payload }: WorkflowContinuityStatusContext): WorkflowContinuityStepStatus {
  const reporting = payload.reporting;
  if (!reporting) {
    return { label: "Waiting", tone: "pending" };
  }

  const targetCount = reporting.reporting?.reportPackTargets?.length ?? 0;
  return targetCount > 0
    ? { label: `${targetCount} packs`, tone: "ready" }
    : { label: "Needs target", tone: "review" };
}

interface OperatorFocusCandidate extends AppShellOperatorFocusItem {
  sourcePriority: number;
  sourceIndex: number;
}

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

interface EvidenceTimelineCandidate extends AppShellEvidenceTimelineItem {
  occurredAtMs: number;
  sourcePriority: number;
  sourceIndex: number;
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
      title: `Resolve ${focusItem.label}`,
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
  operatingScope: AppShellOperatingScopeState
): OperatorFocusViewModel {
  if (context.loading) {
    return {
      summary: "Loading cross-workspace operator posture.",
      emptyText: "Ranked focus actions will appear after workstation payloads load.",
      overflowLabel: null,
      items: [],
      commandItems: []
    };
  }

  const candidates = dedupeOperatorFocusCandidates([
    ...buildWorkspaceErrorFocusItems(context),
    ...buildTradingFocusItems(context),
    ...buildDataFocusItems(context),
    ...buildPortfolioFocusItems(context),
    ...buildAccountingFocusItems(context),
    ...buildReportingFocusItems(context),
    ...buildStrategyFocusItems(context)
  ]).sort(compareOperatorFocusCandidates);

  const visibleItems = candidates.slice(0, OPERATOR_FOCUS_VISIBLE_LIMIT).map((candidate) => toOperatorFocusItem(candidate, operatingScope));
  const blockedCount = candidates.filter((item) => item.tone === "blocked").length;
  const reviewCount = candidates.filter((item) => item.tone === "review").length;
  const overflowCount = Math.max(0, candidates.length - visibleItems.length);

  return {
    summary: candidates.length > 0
      ? `${formatCount(candidates.length, "focus item")} across workspaces: ${formatStatusCount(blockedCount, "blocked")} and ${formatStatusCount(reviewCount, "review")}.`
      : "No cross-workspace blockers in loaded workstation payloads.",
    emptyText: "Loaded workspaces have no ranked blockers.",
    overflowLabel: overflowCount > 0 ? `+${overflowCount} more focus ${overflowCount === 1 ? "item" : "items"}` : null,
    items: visibleItems,
    commandItems: candidates.map((candidate) => toOperatorFocusItem(candidate, operatingScope))
  };
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

function buildTradingFocusItems({ payload }: WorkflowContinuityStatusContext): OperatorFocusCandidate[] {
  const readiness = payload.trading?.readiness ?? null;
  if (!readiness) {
    return [];
  }

  const workItems = (readiness.workItems ?? [])
    .map((item, index) => buildOperatorFocusCandidateFromWorkItem(item, index))
    .filter((item): item is OperatorFocusCandidate => Boolean(item));

  const gateItems = (readiness.acceptanceGates ?? [])
    .map((gate, index) => buildOperatorFocusCandidateFromGate(gate, index))
    .filter((item): item is OperatorFocusCandidate => Boolean(item));

  const replayItems = readiness.replay && !readiness.replay.isConsistent
    ? [buildOperatorFocusCandidate({
        id: `trading-replay:${readiness.replay.sessionId}`,
        label: "Replay evidence inconsistent",
        detail: readiness.replay.mismatchReasons.length > 0
          ? readiness.replay.mismatchReasons.join("; ")
          : "Latest replay verification does not match the active paper session.",
        route: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
        workspaceLabel: "Trading",
        actionLabel: "Open replay evidence",
        tone: "blocked",
        sourcePriority: 4,
        sourceIndex: 0
      })]
    : [];

  const controlItems = readiness.controls?.circuitBreakerOpen
    ? [buildOperatorFocusCandidate({
        id: "trading-control:circuit-breaker",
        label: "Execution circuit breaker open",
        detail: readiness.controls.circuitBreakerReason ?? "Execution controls require operator review before paper operation.",
        route: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
        workspaceLabel: "Trading",
        actionLabel: "Open execution controls",
        tone: "blocked",
        sourcePriority: 4,
        sourceIndex: 1
      })]
    : [];

  const brokerage = readiness.brokerageSync;
  const brokerageItems = brokerage && brokerage.health !== "Healthy"
    ? [buildOperatorFocusCandidate({
        id: `brokerage-sync:${brokerage.fundAccountId}`,
        label: `Brokerage sync ${brokerage.health.toLowerCase()}`,
        detail: brokerage.lastError
          ?? ((brokerage.warnings ?? []).join("; ")
            || `${brokerage.positionCount} positions, ${brokerage.openOrderCount} open orders, and ${brokerage.securityMissingCount} missing securities in sync evidence.`),
        route: brokerage.health === "Unlinked" ? WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup : WORKSTATION_ROUTE_CATALOG.portfolioBrokerageSync,
        workspaceLabel: brokerage.health === "Unlinked" ? "Settings" : "Portfolio",
        actionLabel: brokerage.health === "Unlinked" ? "Fix provider setup" : "Open brokerage sync",
        tone: brokerage.health === "Stale" ? "review" : "blocked",
        sourcePriority: 5,
        sourceIndex: 0
      })]
    : [];

  return [...workItems, ...gateItems, ...replayItems, ...controlItems, ...brokerageItems];
}

function buildDataFocusItems({ payload }: WorkflowContinuityStatusContext): OperatorFocusCandidate[] {
  const data = payload.dataOperations;
  if (!data) {
    return [];
  }

  return [
    ...(data.providers ?? [])
      .map((provider, index) => buildOperatorFocusCandidateFromProvider(provider, index))
      .filter((item): item is OperatorFocusCandidate => Boolean(item)),
    ...(data.backfills ?? [])
      .map((backfill, index) => buildOperatorFocusCandidateFromBackfill(backfill, index))
      .filter((item): item is OperatorFocusCandidate => Boolean(item)),
    ...(data.exports ?? [])
      .filter((item) => item.status === "Attention")
      .map((item, index) => buildOperatorFocusCandidate({
        id: `data-export:${item.exportId}`,
        label: `${item.profile} export needs attention`,
        detail: `${item.target} export has ${item.rows} rows as of ${item.updatedAt}.`,
        route: WORKSTATION_ROUTE_CATALOG.data,
        workspaceLabel: "Data",
        actionLabel: "Open data exports",
        tone: "review",
        sourcePriority: 20,
        sourceIndex: index
      }))
  ];
}

function buildPortfolioFocusItems({ payload }: WorkflowContinuityStatusContext): OperatorFocusCandidate[] {
  const portfolio = payload.portfolio;
  if (!portfolio || portfolio.risk.state === "Healthy") {
    return [];
  }

  return [buildOperatorFocusCandidate({
    id: "portfolio-risk",
    label: `Portfolio risk ${portfolio.risk.state.toLowerCase()}`,
    detail: portfolio.risk.summary,
    route: WORKSTATION_ROUTE_CATALOG.portfolio,
    workspaceLabel: "Portfolio",
    actionLabel: "Open exposure",
    tone: portfolio.risk.state === "Constrained" ? "blocked" : "review",
    sourcePriority: 10,
    sourceIndex: 0
  })];
}

function buildAccountingFocusItems({ payload }: WorkflowContinuityStatusContext): OperatorFocusCandidate[] {
  const governance = payload.governance;
  if (!governance) {
    return [];
  }

  return [
    ...(governance.breakQueue ?? [])
      .map((item, index) => buildOperatorFocusCandidateFromBreak(item, index))
      .filter((item): item is OperatorFocusCandidate => Boolean(item)),
    ...(governance.reconciliationQueue ?? [])
      .filter((row) => row.openBreakCount > 0)
      .map((row, index) => buildOperatorFocusCandidate({
        id: `reconciliation-run:${row.runId}`,
        label: `${row.strategyName} reconciliation open`,
        detail: `${formatCount(row.openBreakCount, "open break")} across ${formatCount(row.breakCount, "total break")}. Status: ${row.reconciliationStatus}.`,
        route: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
        workspaceLabel: "Accounting",
        actionLabel: "Open reconciliation",
        tone: row.reconciliationStatus === "SecurityCoverageOpen" ? "blocked" : "review",
        sourcePriority: 13,
        sourceIndex: index
      })),
    governance.cashFlow?.tone === "danger" || governance.cashFlow?.tone === "warning"
      ? buildOperatorFocusCandidate({
          id: "cash-flow-variance",
          label: "Cash-flow variance open",
          detail: governance.cashFlow.summary,
          route: WORKSTATION_ROUTE_CATALOG.accounting,
          workspaceLabel: "Accounting",
          actionLabel: "Open ledger",
          tone: governance.cashFlow.tone === "danger" ? "blocked" : "review",
          sourcePriority: 14,
          sourceIndex: 0
        })
      : null
  ].filter((item): item is OperatorFocusCandidate => Boolean(item));
}

function buildReportingFocusItems({ payload }: WorkflowContinuityStatusContext): OperatorFocusCandidate[] {
  const reporting = payload.reporting;
  if (!reporting) {
    return [];
  }

  const targetCount = reporting.reporting?.reportPackTargets?.length ?? 0;
  return targetCount === 0
    ? [buildOperatorFocusCandidate({
        id: "reporting:missing-report-pack-target",
        label: "Report-pack target missing",
        detail: "No governed report-pack targets are loaded for approval-ready output review.",
        route: WORKSTATION_ROUTE_CATALOG.reportingReportPacks,
        workspaceLabel: "Reporting",
        actionLabel: "Open report packs",
        tone: "review",
        sourcePriority: 30,
        sourceIndex: 0
      })]
    : [];
}

function buildStrategyFocusItems({ payload }: WorkflowContinuityStatusContext): OperatorFocusCandidate[] {
  const research = payload.research;
  if (!research) {
    return [];
  }

  return (research.runs ?? [])
    .map((run, index) => buildOperatorFocusCandidateFromResearchRun(run, index))
    .filter((item): item is OperatorFocusCandidate => Boolean(item));
}

function buildOperatorFocusCandidateFromWorkItem(
  item: OperatorWorkItem,
  index: number
): OperatorFocusCandidate | null {
  const tone = toneFromOperatorWorkItem(item);
  if (!tone) {
    return null;
  }

  const route = routeForOperatorWorkItem(item);
  const actionLabel = actionLabelForOperatorWorkItem(item);
  return buildOperatorFocusCandidate({
    id: `work-item:${item.workItemId}`,
    label: item.label,
    detail: item.detail,
    route,
    workspaceLabel: workspaceLabelForRoute(route),
    actionLabel,
    tone,
    sourcePriority: 0,
    sourceIndex: index
  });
}

function buildOperatorFocusCandidateFromGate(
  gate: TradingAcceptanceGate,
  index: number
): OperatorFocusCandidate | null {
  if (gate.status === "Ready") {
    return null;
  }

  return buildOperatorFocusCandidate({
    id: `trading-gate:${gate.gateId}`,
    label: gate.label,
    detail: gate.detail,
    route: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
    workspaceLabel: "Trading",
    actionLabel: "Open readiness",
    tone: gate.status === "Blocked" ? "blocked" : "review",
    sourcePriority: 3,
    sourceIndex: index
  });
}

function buildOperatorFocusCandidateFromProvider(
  provider: DataOperationsProviderRecord,
  index: number
): OperatorFocusCandidate | null {
  if (provider.status === "Healthy") {
    return null;
  }

  return buildOperatorFocusCandidate({
    id: `provider:${provider.provider}`,
    label: `${provider.provider} provider ${provider.status.toLowerCase()}`,
    detail: provider.recommendedAction ?? provider.note,
    route: WORKSTATION_ROUTE_CATALOG.dataProviders,
    workspaceLabel: "Data",
    actionLabel: "Open provider trust",
    tone: provider.status === "Degraded" ? "blocked" : "review",
    sourcePriority: 15,
    sourceIndex: index
  });
}

function buildOperatorFocusCandidateFromBackfill(
  backfill: DataOperationsBackfillRecord,
  index: number
): OperatorFocusCandidate | null {
  if (backfill.status !== "Review") {
    return null;
  }

  return buildOperatorFocusCandidate({
    id: `backfill:${backfill.jobId}`,
    label: `${backfill.scope} backfill needs review`,
    detail: `${backfill.provider} backfill is ${backfill.progress}; updated ${backfill.updatedAt}.`,
    route: WORKSTATION_ROUTE_CATALOG.dataBackfills,
    workspaceLabel: "Data",
    actionLabel: "Open backfills",
    tone: "review",
    sourcePriority: 16,
    sourceIndex: index
  });
}

function buildOperatorFocusCandidateFromBreak(
  item: ReconciliationBreakQueueItem,
  index: number
): OperatorFocusCandidate | null {
  if (item.status !== "Open" && item.status !== "InReview") {
    return null;
  }

  return buildOperatorFocusCandidate({
    id: `break:${item.breakId}`,
    label: `${item.category} break ${item.status === "Open" ? "open" : "in review"}`,
    detail: item.recommendedAction ?? item.explainabilitySummary ?? item.reason,
    route: normalizeLocalWorkstationRoute(item.routingTarget) ?? WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
    workspaceLabel: "Accounting",
    actionLabel: "Open break queue",
    tone: item.status === "Open" ? "blocked" : "review",
    sourcePriority: 12,
    sourceIndex: index
  });
}

function buildOperatorFocusCandidateFromResearchRun(
  run: ResearchRunRecord,
  index: number
): OperatorFocusCandidate | null {
  if (run.status !== "Needs Review") {
    return null;
  }

  return buildOperatorFocusCandidate({
    id: `research-run:${run.id}`,
    label: `${run.strategyName} run needs review`,
    detail: run.notes || `${run.engine} ${run.mode} run on ${run.dataset}.`,
    route: WORKSTATION_ROUTE_CATALOG.strategy,
    workspaceLabel: "Strategy",
    actionLabel: "Open run library",
    tone: "review",
    sourcePriority: 35,
    sourceIndex: index
  });
}

function buildOperatorFocusCandidate({
  id,
  label,
  detail,
  route,
  workspaceLabel,
  actionLabel,
  tone,
  sourcePriority,
  sourceIndex
}: Omit<OperatorFocusCandidate, "ariaLabel">): OperatorFocusCandidate {
  return {
    id,
    label,
    detail,
    route,
    workspaceLabel,
    actionLabel,
    tone,
    sourcePriority,
    sourceIndex,
    ariaLabel: `${workspaceLabel}: ${label}. ${detail} ${actionLabel}.`
  };
}

function routeForOperatorWorkItem(item: OperatorWorkItem): string {
  if (item.kind === "BrokerageSync") {
    return WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup;
  }

  return normalizeLocalWorkstationRoute(item.targetRoute)
    ?? routeFromOperatorWorkItemTarget(item)
    ?? fallbackRouteForOperatorWorkItemKind(item.kind);
}

function routeFromOperatorWorkItemTarget(item: OperatorWorkItem): string | null {
  if (!item.targetPageTag && !item.workspace) {
    return null;
  }

  return workflowTargetPath(item.targetPageTag, item.workspace);
}

function fallbackRouteForOperatorWorkItemKind(kind: OperatorWorkItem["kind"]): string {
  switch (kind) {
    case "PaperReplay":
    case "PromotionReview":
    case "ExecutionControl":
      return WORKSTATION_ROUTE_CATALOG.tradingReadiness;
    case "BrokerageSync":
      return WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup;
    case "SecurityMasterCoverage":
      return WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster;
    case "ReconciliationBreak":
    case "LedgerPeriodClose":
      return WORKSTATION_ROUTE_CATALOG.accountingReconciliation;
    case "ReportPackApproval":
      return WORKSTATION_ROUTE_CATALOG.reportingReportPacks;
    case "ProviderTrustGate":
      return WORKSTATION_ROUTE_CATALOG.dataProviders;
  }
}

function actionLabelForOperatorWorkItem(item: OperatorWorkItem): string {
  switch (item.kind) {
    case "PaperReplay":
      return "Open replay evidence";
    case "PromotionReview":
      return "Open promotion review";
    case "BrokerageSync":
      return "Fix provider setup";
    case "SecurityMasterCoverage":
      return "Open Security Master";
    case "ReconciliationBreak":
      return "Open break queue";
    case "LedgerPeriodClose":
      return "Open reconciliation";
    case "ReportPackApproval":
      return "Open report packs";
    case "ProviderTrustGate":
      return "Open provider trust";
    case "ExecutionControl":
      return "Open execution controls";
  }
}

function toneFromOperatorWorkItem(item: OperatorWorkItem): AppShellWorkflowContinuityStatusTone | null {
  switch (item.tone) {
    case "Critical":
      return "blocked";
    case "Warning":
    case "Info":
      return "review";
    case "Success":
      return null;
  }
}

function workspaceLabelForRoute(route: string): string {
  return workspaceForPath(splitContinuityRoute(route).pathname).label;
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

function compareOperatorFocusCandidates(left: OperatorFocusCandidate, right: OperatorFocusCandidate): number {
  return operatorFocusTonePriority(left.tone) - operatorFocusTonePriority(right.tone)
    || left.sourcePriority - right.sourcePriority
    || left.sourceIndex - right.sourceIndex
    || left.label.localeCompare(right.label);
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
      emptyText: "Portfolio-aware context links will appear after workstation payloads load.",
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
    buildDataLinkedContextItem(context, symbol),
    buildTradingLinkedContextItem(context, symbol),
    buildPortfolioLinkedContextItem(context, symbol),
    buildAccountingLinkedContextItem(context, symbol),
    buildReportingLinkedContextItem(context, symbol)
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

function buildDataLinkedContextItem(
  { payload }: WorkflowContinuityStatusContext,
  symbol: string
): AppShellLinkedContextItem {
  const data = payload.dataOperations;
  const route = appendSearchValue(WORKSTATION_ROUTE_CATALOG.dataQuotes, "symbol", symbol);
  if (!data) {
    return buildLinkedContextItem({
      id: "data-quotes",
      label: "Quote evidence",
      detail: `Open quote, tape, depth, and history evidence for ${symbol}.`,
      route,
      workspaceLabel: "Data",
      statusLabel: "Waiting",
      tone: "pending"
    });
  }

  const providerAttention = (data.providers ?? []).find((provider) => provider.status !== "Healthy");
  if (providerAttention) {
    return buildLinkedContextItem({
      id: "data-quotes",
      label: "Quote evidence",
      detail: providerAttention.recommendedAction || providerAttention.note || `${providerAttention.provider} requires review before trusting ${symbol}.`,
      route,
      workspaceLabel: "Data",
      statusLabel: providerAttention.status === "Degraded" ? "Provider degraded" : "Provider review",
      tone: providerAttention.status === "Degraded" ? "blocked" : "review"
    });
  }

  const backfill = findSymbolBackfill(data.backfills ?? [], symbol);
  if (backfill) {
    return buildLinkedContextItem({
      id: "data-quotes",
      label: "Quote evidence",
      detail: `${backfill.provider} backfill ${backfill.status.toLowerCase()} at ${backfill.progress}.`,
      route,
      workspaceLabel: "Data",
      statusLabel: backfill.status === "Review" ? "Backfill review" : "Backfill active",
      tone: backfill.status === "Review" ? "review" : "pending"
    });
  }

  return buildLinkedContextItem({
    id: "data-quotes",
    label: "Quote evidence",
    detail: `Live quote, alert, and historical evidence routes retain ${symbol}.`,
    route,
    workspaceLabel: "Data",
    statusLabel: "Trusted",
    tone: "ready"
  });
}

function buildTradingLinkedContextItem(
  { payload }: WorkflowContinuityStatusContext,
  symbol: string
): AppShellLinkedContextItem {
  const trading = payload.trading;
  const route = appendSearchValue(WORKSTATION_ROUTE_CATALOG.trading, "symbol", symbol);
  if (!trading) {
    return buildLinkedContextItem({
      id: "trading-cockpit",
      label: "Trading cockpit",
      detail: `Open paper-session controls with ${symbol} kept as the operating subject.`,
      route,
      workspaceLabel: "Trading",
      statusLabel: "Waiting",
      tone: "pending"
    });
  }

  if (trading.risk?.state === "Constrained") {
    return buildLinkedContextItem({
      id: "trading-cockpit",
      label: "Trading cockpit",
      detail: trading.risk.summary,
      route,
      workspaceLabel: "Trading",
      statusLabel: "Risk constrained",
      tone: "blocked"
    });
  }

  const position = findSymbolRow(trading.positions ?? [], symbol);
  const orderCount = (trading.openOrders ?? []).filter((order) => isSameSymbol(order.symbol, symbol)).length;
  const fillCount = (trading.fills ?? []).filter((fill) => isSameSymbol(fill.symbol, symbol)).length;
  if (orderCount > 0) {
    return buildLinkedContextItem({
      id: "trading-cockpit",
      label: "Trading cockpit",
      detail: `${formatCount(orderCount, "open order")} and ${formatCount(fillCount, "recent fill")} are loaded for ${symbol}.`,
      route,
      workspaceLabel: "Trading",
      statusLabel: "Orders open",
      tone: "review"
    });
  }

  if (position) {
    return buildLinkedContextItem({
      id: "trading-cockpit",
      label: "Trading cockpit",
      detail: `${position.side} ${position.quantity} with ${position.exposure} exposure and ${position.unrealizedPnl} unrealized P&L.`,
      route,
      workspaceLabel: "Trading",
      statusLabel: "Position loaded",
      tone: trading.risk?.state === "Observe" ? "review" : "ready"
    });
  }

  return buildLinkedContextItem({
    id: "trading-cockpit",
    label: "Trading cockpit",
    detail: `No open trading rows are loaded for ${symbol}; review before staging orders.`,
    route,
    workspaceLabel: "Trading",
    statusLabel: "No row",
    tone: "review"
  });
}

function buildPortfolioLinkedContextItem(
  { payload }: WorkflowContinuityStatusContext,
  symbol: string
): AppShellLinkedContextItem {
  const portfolio = payload.portfolio;
  const route = appendSearchValue(WORKSTATION_ROUTE_CATALOG.portfolio, "symbol", symbol);
  if (!portfolio) {
    return buildLinkedContextItem({
      id: "portfolio-exposure",
      label: "Portfolio exposure",
      detail: `Open positions, cash, account sync, and risk posture with ${symbol} retained.`,
      route,
      workspaceLabel: "Portfolio",
      statusLabel: "Waiting",
      tone: "pending"
    });
  }

  if (portfolio.risk.state === "Constrained") {
    return buildLinkedContextItem({
      id: "portfolio-exposure",
      label: "Portfolio exposure",
      detail: portfolio.risk.summary,
      route,
      workspaceLabel: "Portfolio",
      statusLabel: "Risk constrained",
      tone: "blocked"
    });
  }

  const position = findSymbolRow(portfolio.positions ?? [], symbol);
  if (position) {
    return buildLinkedContextItem({
      id: "portfolio-exposure",
      label: "Portfolio exposure",
      detail: `${position.side} ${position.quantity} with ${position.exposure} exposure and ${position.unrealizedPnl} unrealized P&L.`,
      route,
      workspaceLabel: "Portfolio",
      statusLabel: portfolio.risk.state === "Observe" ? "Observe" : "Holding loaded",
      tone: portfolio.risk.state === "Observe" ? "review" : "ready"
    });
  }

  return buildLinkedContextItem({
    id: "portfolio-exposure",
    label: "Portfolio exposure",
    detail: `No portfolio position is loaded for ${symbol}; confirm sizing and account sync before trading.`,
    route,
    workspaceLabel: "Portfolio",
    statusLabel: "No holding",
    tone: "review"
  });
}

function buildAccountingLinkedContextItem(
  { payload }: WorkflowContinuityStatusContext,
  symbol: string
): AppShellLinkedContextItem {
  const governance = payload.governance;
  const route = appendSearchValue(WORKSTATION_ROUTE_CATALOG.accountingReconciliation, "symbol", symbol);
  if (!governance) {
    return buildLinkedContextItem({
      id: "accounting-reconciliation",
      label: "Reconciliation",
      detail: `Open break queue and ledger context with ${symbol} retained.`,
      route,
      workspaceLabel: "Accounting",
      statusLabel: "Waiting",
      tone: "pending"
    });
  }

  const breakCount = (governance.breakQueue ?? []).filter((item) => item.status === "Open" || item.status === "InReview").length;
  const runBreakCount = (governance.reconciliationQueue ?? []).reduce((total, row) => total + row.openBreakCount, 0);
  const attentionCount = Math.max(breakCount, runBreakCount);
  return buildLinkedContextItem({
    id: "accounting-reconciliation",
    label: "Reconciliation",
    detail: attentionCount > 0
      ? `${formatCount(attentionCount, "open break")} can affect ${symbol} ledger confidence.`
      : `No open reconciliation breaks are loaded while reviewing ${symbol}.`,
    route,
    workspaceLabel: "Accounting",
    statusLabel: attentionCount > 0 ? "Breaks open" : "Balanced",
    tone: attentionCount > 0 ? "review" : "ready"
  });
}

function buildReportingLinkedContextItem(
  { payload }: WorkflowContinuityStatusContext,
  symbol: string
): AppShellLinkedContextItem {
  const reporting = payload.reporting;
  const route = appendSearchValue(WORKSTATION_ROUTE_CATALOG.reportingEvidence, "symbol", symbol);
  if (!reporting) {
    return buildLinkedContextItem({
      id: "reporting-evidence",
      label: "Evidence packet",
      detail: `Open audit lineage and report evidence with ${symbol} retained.`,
      route,
      workspaceLabel: "Reporting",
      statusLabel: "Waiting",
      tone: "pending"
    });
  }

  const packCount = reporting.reporting?.reportPackTargets?.length ?? 0;
  return buildLinkedContextItem({
    id: "reporting-evidence",
    label: "Evidence packet",
    detail: packCount > 0
      ? `${formatCount(packCount, "report target")} can carry ${symbol} context into governed review.`
      : `No report-pack target is loaded while reviewing ${symbol}.`,
    route,
    workspaceLabel: "Reporting",
    statusLabel: packCount > 0 ? "Packet ready" : "Needs target",
    tone: packCount > 0 ? "ready" : "review"
  });
}

function buildLinkedContextItem({
  id,
  label,
  detail,
  route,
  workspaceLabel,
  statusLabel,
  tone
}: Omit<AppShellLinkedContextItem, "ariaLabel">): AppShellLinkedContextItem {
  return {
    id,
    label,
    detail,
    route,
    workspaceLabel,
    statusLabel,
    tone,
    ariaLabel: `${workspaceLabel}: ${label}. ${detail} ${statusLabel}.`
  };
}

function inferPrimaryOperatingSymbol(payload: AppShellWorkspacePayload): string | null {
  return normalizeSubjectSymbol(payload.portfolio?.positions?.[0]?.symbol ?? null)
    ?? normalizeSubjectSymbol(payload.trading?.positions?.[0]?.symbol ?? null)
    ?? normalizeSubjectSymbol(payload.trading?.openOrders?.[0]?.symbol ?? null)
    ?? normalizeSubjectSymbol(payload.trading?.fills?.[0]?.symbol ?? null);
}

function findSymbolBackfill(backfills: DataOperationsBackfillRecord[], symbol: string): DataOperationsBackfillRecord | null {
  return backfills.find((backfill) => backfill.scope.toUpperCase().includes(symbol)) ?? null;
}

function findSymbolRow<T extends { symbol: string }>(rows: T[], symbol: string): T | null {
  return rows.find((row) => isSameSymbol(row.symbol, symbol)) ?? null;
}

function isSameSymbol(value: string | null | undefined, symbol: string): boolean {
  return normalizeSubjectSymbol(value ?? null) === symbol;
}

function buildEvidenceTimelineViewModel(
  context: WorkflowContinuityStatusContext,
  operatingScope: AppShellOperatingScopeState
): EvidenceTimelineViewModel {
  if (context.loading) {
    return {
      summary: "Loading cross-workspace evidence timeline.",
      emptyText: "Recent audit and workflow events will appear after workstation payloads load.",
      overflowLabel: null,
      items: []
    };
  }

  const candidates = dedupeEvidenceTimelineCandidates([
    ...buildOverviewEvidenceTimelineItems(context),
    ...buildTradingEvidenceTimelineItems(context),
    ...buildDataEvidenceTimelineItems(context),
    ...buildStrategyEvidenceTimelineItems(context),
    ...buildPortfolioEvidenceTimelineItems(context),
    ...buildAccountingEvidenceTimelineItems(context)
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
      : "No timestamped evidence events in loaded workstation payloads.",
    emptyText: "Loaded payloads have no timestamped evidence events.",
    overflowLabel: overflowCount > 0 ? `+${overflowCount} older ${overflowCount === 1 ? "event" : "events"}` : null,
    items: visibleItems
  };
}

function buildOverviewEvidenceTimelineItems({ payload }: WorkflowContinuityStatusContext): EvidenceTimelineCandidate[] {
  return (payload.overview?.recentEvents ?? [])
    .map((event, index) => {
      const route = routeForSystemEvent(event.source);
      return buildEvidenceTimelineCandidate({
        id: `overview-event:${event.id}`,
        label: `${event.source} ${event.type}`,
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

function buildTradingEvidenceTimelineItems({ payload }: WorkflowContinuityStatusContext): EvidenceTimelineCandidate[] {
  const trading = payload.trading;
  if (!trading) {
    return [];
  }

  const items: EvidenceTimelineCandidate[] = [];
  const readiness = trading.readiness ?? null;
  if (readiness) {
    pushEvidenceTimelineCandidate(items, {
      id: "trading-readiness:snapshot",
      label: `Paper readiness ${readiness.overallStatus}`,
      detail: readiness.readyForPaperOperation
        ? "Ready for paper operation with the latest readiness snapshot."
        : `${formatCount((readiness.acceptanceGates ?? []).filter((gate) => gate.status !== "Ready").length, "gate")} and ${formatCount((readiness.workItems ?? []).length, "work item")} require operator review.`,
      route: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
      workspaceLabel: "Trading",
      timestamp: readiness.asOf,
      tone: toneFromReadinessStatus(readiness.overallStatus),
      sourcePriority: 0,
      sourceIndex: 0
    });

    (readiness.workItems ?? []).forEach((item, index) => {
      const route = routeForOperatorWorkItem(item);
      pushEvidenceTimelineCandidate(items, {
        id: `work-item:${item.workItemId}`,
        label: item.label,
        detail: item.auditReference ? `${item.detail} Audit: ${item.auditReference}.` : item.detail,
        route,
        workspaceLabel: workspaceLabelForRoute(route),
        timestamp: item.createdAt,
        tone: toneFromTimelineWorkItem(item.tone),
        sourcePriority: 1,
        sourceIndex: index
      });
    });

    if (readiness.replay) {
      pushEvidenceTimelineCandidate(items, {
        id: `trading-replay:${readiness.replay.sessionId}`,
        label: readiness.replay.isConsistent ? "Replay verification passed" : "Replay verification mismatch",
        detail: `${formatCount(readiness.replay.comparedFillCount, "fill")}, ${formatCount(readiness.replay.comparedOrderCount, "order")}, and ${formatCount(readiness.replay.comparedLedgerEntryCount, "ledger entry", "ledger entries")} compared.${readiness.replay.verificationAuditId ? ` Audit: ${readiness.replay.verificationAuditId}.` : ""}`,
        route: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
        workspaceLabel: "Trading",
        timestamp: readiness.replay.verifiedAt,
        tone: readiness.replay.isConsistent ? "ready" : "blocked",
        sourcePriority: 2,
        sourceIndex: 0
      });
    }

    if (readiness.controls?.circuitBreakerChangedAt) {
      pushEvidenceTimelineCandidate(items, {
        id: "trading-control:circuit-breaker",
        label: readiness.controls.circuitBreakerOpen ? "Execution circuit breaker open" : "Execution circuit breaker changed",
        detail: readiness.controls.circuitBreakerReason ?? "Execution controls changed for the paper cockpit.",
        route: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
        workspaceLabel: "Trading",
        timestamp: readiness.controls.circuitBreakerChangedAt,
        tone: readiness.controls.circuitBreakerOpen ? "blocked" : "ready",
        sourcePriority: 3,
        sourceIndex: 0
      });
    }

    if (readiness.brokerageSync) {
      pushEvidenceTimelineCandidate(items, {
        id: `brokerage-sync:${readiness.brokerageSync.fundAccountId}`,
        label: `Brokerage sync ${readiness.brokerageSync.health.toLowerCase()}`,
        detail: readiness.brokerageSync.lastError
          ?? ((readiness.brokerageSync.warnings ?? []).join("; ")
            || `${formatCount(readiness.brokerageSync.positionCount, "position")}, ${formatCount(readiness.brokerageSync.openOrderCount, "open order")}, and ${formatCount(readiness.brokerageSync.securityMissingCount, "missing security", "missing securities")} in sync evidence.`),
        route: readiness.brokerageSync.health === "Unlinked" ? WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup : WORKSTATION_ROUTE_CATALOG.portfolioBrokerageSync,
        workspaceLabel: readiness.brokerageSync.health === "Unlinked" ? "Settings" : "Portfolio",
        timestamp: readiness.brokerageSync.lastAttemptedSyncAt ?? readiness.brokerageSync.lastSuccessfulSyncAt,
        tone: readiness.brokerageSync.health === "Healthy" ? "ready" : readiness.brokerageSync.health === "Stale" ? "review" : "blocked",
        sourcePriority: 4,
        sourceIndex: 0
      });
    }
  }

  (trading.fills ?? []).forEach((fill, index) => {
    pushEvidenceTimelineCandidate(items, {
      id: `trading-fill:${fill.fillId}`,
      label: `${fill.side} fill ${fill.symbol}`,
      detail: `${fill.quantity} @ ${fill.price} on ${fill.venue}. Order: ${fill.orderId}.`,
      route: WORKSTATION_ROUTE_CATALOG.trading,
      workspaceLabel: "Trading",
      timestamp: fill.timestamp,
      tone: "ready",
      sourcePriority: 5,
      sourceIndex: index
    });
  });

  return items;
}

function buildDataEvidenceTimelineItems({ payload }: WorkflowContinuityStatusContext): EvidenceTimelineCandidate[] {
  const data = payload.dataOperations;
  if (!data) {
    return [];
  }

  return [
    ...(data.backfills ?? []).map((backfill, index) => buildEvidenceTimelineCandidate({
      id: `data-backfill:${backfill.jobId}`,
      label: `${backfill.scope} backfill ${backfill.status.toLowerCase()}`,
      detail: `${backfill.provider} backfill progress: ${backfill.progress}.`,
      route: WORKSTATION_ROUTE_CATALOG.dataBackfills,
      workspaceLabel: "Data",
      timestamp: backfill.updatedAt,
      tone: backfill.status === "Review" ? "review" : "pending",
      sourcePriority: 20,
      sourceIndex: index
    })),
    ...(data.exports ?? []).map((item, index) => buildEvidenceTimelineCandidate({
      id: `data-export:${item.exportId}`,
      label: `${item.profile} export ${item.status.toLowerCase()}`,
      detail: `${item.target} export has ${item.rows} rows.`,
      route: WORKSTATION_ROUTE_CATALOG.data,
      workspaceLabel: "Data",
      timestamp: item.updatedAt,
      tone: item.status === "Attention" ? "review" : item.status === "Running" ? "pending" : "ready",
      sourcePriority: 21,
      sourceIndex: index
    }))
  ].filter((item): item is EvidenceTimelineCandidate => Boolean(item));
}

function buildStrategyEvidenceTimelineItems({ payload }: WorkflowContinuityStatusContext): EvidenceTimelineCandidate[] {
  return (payload.research?.runs ?? [])
    .map((run, index) => buildEvidenceTimelineCandidate({
      id: `strategy-run:${run.id}`,
      label: `${run.strategyName} ${run.status.toLowerCase()}`,
      detail: `${run.engine} ${run.mode} run on ${run.dataset}. ${run.notes}`,
      route: WORKSTATION_ROUTE_CATALOG.strategy,
      workspaceLabel: "Strategy",
      timestamp: run.lastUpdated,
      tone: run.status === "Needs Review" ? "review" : run.status === "Running" || run.status === "Queued" ? "pending" : "ready",
      sourcePriority: 30,
      sourceIndex: index
    }))
    .filter((item): item is EvidenceTimelineCandidate => Boolean(item));
}

function buildPortfolioEvidenceTimelineItems({ payload }: WorkflowContinuityStatusContext): EvidenceTimelineCandidate[] {
  return (payload.portfolio?.runs ?? [])
    .map((run, index) => buildEvidenceTimelineCandidate({
      id: `portfolio-run:${run.runId}`,
      label: `${run.strategyName} portfolio ${run.status.toLowerCase()}`,
      detail: `${run.engine} ${run.mode} run, PnL ${run.pnl}, promotion ${run.promotionState ?? "none"}.`,
      route: WORKSTATION_ROUTE_CATALOG.portfolio,
      workspaceLabel: "Portfolio",
      timestamp: run.lastUpdated,
      tone: run.status.toLowerCase().includes("review") ? "review" : "ready",
      sourcePriority: 31,
      sourceIndex: index
    }))
    .filter((item): item is EvidenceTimelineCandidate => Boolean(item));
}

function buildAccountingEvidenceTimelineItems({ payload }: WorkflowContinuityStatusContext): EvidenceTimelineCandidate[] {
  const governance = payload.governance;
  if (!governance) {
    return [];
  }

  return [
    ...(governance.breakQueue ?? []).map((item, index) => buildEvidenceTimelineCandidate({
      id: `accounting-break:${item.breakId}`,
      label: `${item.category} break ${item.status.toLowerCase()}`,
      detail: item.recommendedAction ?? item.explainabilitySummary ?? item.reason,
      route: normalizeLocalWorkstationRoute(item.routingTarget) ?? WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
      workspaceLabel: "Accounting",
      timestamp: item.lastUpdatedAt,
      tone: item.status === "Open" ? "blocked" : item.status === "InReview" ? "review" : "ready",
      sourcePriority: 10,
      sourceIndex: index
    })),
    ...(governance.reconciliationQueue ?? []).map((row, index) => buildEvidenceTimelineCandidate({
      id: `accounting-reconciliation:${row.runId}`,
      label: `${row.strategyName} reconciliation ${row.reconciliationStatus}`,
      detail: `${formatCount(row.openBreakCount, "open break")} across ${formatCount(row.breakCount, "total break")}. Status: ${row.status}.`,
      route: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
      workspaceLabel: "Accounting",
      timestamp: row.lastUpdated,
      tone: row.openBreakCount > 0 ? row.reconciliationStatus === "SecurityCoverageOpen" ? "blocked" : "review" : "ready",
      sourcePriority: 11,
      sourceIndex: index
    }))
  ].filter((item): item is EvidenceTimelineCandidate => Boolean(item));
}

interface EvidenceTimelineCandidateInput {
  id: string;
  label: string;
  detail: string;
  route: string;
  workspaceLabel: string;
  timestamp: string | null | undefined;
  tone: AppShellWorkflowContinuityStatusTone;
  sourcePriority: number;
  sourceIndex: number;
}

function pushEvidenceTimelineCandidate(items: EvidenceTimelineCandidate[], input: EvidenceTimelineCandidateInput) {
  const item = buildEvidenceTimelineCandidate(input);
  if (item) {
    items.push(item);
  }
}

function buildEvidenceTimelineCandidate({
  id,
  label,
  detail,
  route,
  workspaceLabel,
  timestamp,
  tone,
  sourcePriority,
  sourceIndex
}: EvidenceTimelineCandidateInput): EvidenceTimelineCandidate | null {
  const timestampState = parseEvidenceTimestamp(timestamp);
  if (!timestampState) {
    return null;
  }

  return {
    id,
    label,
    detail,
    route,
    workspaceLabel,
    timestampLabel: timestampState.label,
    timestampIso: timestampState.iso,
    tone,
    ariaLabel: `${workspaceLabel}: ${label}. ${detail} ${timestampState.label}. Open evidence.`,
    occurredAtMs: timestampState.occurredAtMs,
    sourcePriority,
    sourceIndex
  };
}

function parseEvidenceTimestamp(value: string | null | undefined): { occurredAtMs: number; iso: string; label: string } | null {
  const occurredAtMs = Date.parse(value ?? "");
  if (!Number.isFinite(occurredAtMs)) {
    return null;
  }

  const iso = new Date(occurredAtMs).toISOString();
  return {
    occurredAtMs,
    iso,
    label: `${iso.slice(0, 10)} ${iso.slice(11, 16)} UTC`
  };
}

function toneFromReadinessStatus(status: TradingAcceptanceGate["status"]): AppShellWorkflowContinuityStatusTone {
  switch (status) {
    case "Blocked":
      return "blocked";
    case "ReviewRequired":
      return "review";
    case "Ready":
      return "ready";
  }
}

function toneFromTimelineWorkItem(tone: OperatorWorkItem["tone"]): AppShellWorkflowContinuityStatusTone {
  switch (tone) {
    case "Critical":
      return "blocked";
    case "Warning":
    case "Info":
      return "review";
    case "Success":
      return "ready";
  }
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
  return `${count} ${count === 1 ? singular : plural}`;
}

function formatStatusCount(count: number, label: string): string {
  return `${count} ${label}`;
}

function scoreWorkflowStepRouteMatch(
  step: WorkflowContinuityTrailStepDefinition,
  pathname: string,
  hash: string
) {
  const candidate = splitContinuityRoute(step.matchPath);
  const candidateHash = step.matchHash ?? candidate.hash;
  if (candidateHash && hash !== candidateHash) {
    return 0;
  }

  const matchPath = candidate.pathname;
  if (pathname === matchPath) {
    return 1000 + matchPath.length + (candidateHash ? 2000 : 0);
  }

  return pathname.startsWith(`${matchPath}/`)
    ? 100 + matchPath.length + (candidateHash ? 2000 : 0)
    : 0;
}

function splitContinuityRoute(route: string) {
  const hashIndex = route.indexOf("#");
  const routeWithoutHash = hashIndex >= 0 ? route.slice(0, hashIndex) : route;
  const hash = hashIndex >= 0 ? route.slice(hashIndex) : "";
  const searchIndex = routeWithoutHash.indexOf("?");
  return {
    pathname: searchIndex >= 0 ? routeWithoutHash.slice(0, searchIndex) : routeWithoutHash,
    hash
  };
}

function readSearchValue(search: string, key: string): string | null {
  if (!search) {
    return null;
  }

  try {
    return new URLSearchParams(search).get(key);
  } catch {
    return null;
  }
}

function normalizeSubjectSymbol(value: string | null): string | null {
  const normalized = value?.trim().toUpperCase().replace(/[^A-Z0-9._-]/g, "") ?? "";
  return normalized.length > 0 ? normalized.slice(0, 16) : null;
}

export function readOperatingScopeFromSearch(search: string): AppShellOperatingScopeInput {
  return {
    symbol: normalizeSubjectSymbol(readSearchValue(search, "symbol")),
    fundAccountId: normalizeScopeToken(readSearchValue(search, "fundAccountId"), 72),
    runId: normalizeScopeToken(readSearchValue(search, "runId"), 72),
    provider: normalizeScopeLabel(readSearchValue(search, "provider"), 40),
    from: normalizeDateScopeValue(readSearchValue(search, "from")),
    to: normalizeDateScopeValue(readSearchValue(search, "to")),
    date: normalizeDateScopeValue(readSearchValue(search, "date")),
    asOf: normalizeDateScopeValue(readSearchValue(search, "asOf"))
  };
}

export function buildOperatingScopeFromSearch(
  search: string,
  fallback: AppShellOperatingScopeInput | null = null
): AppShellOperatingScopeState {
  const routeScope = readOperatingScopeFromSearch(search);
  const subjectSymbol = routeScope.symbol ?? normalizeSubjectSymbol(fallback?.symbol ?? null);
  const fundAccountId = routeScope.fundAccountId ?? normalizeScopeToken(fallback?.fundAccountId, 72);
  const runId = routeScope.runId ?? normalizeScopeToken(fallback?.runId, 72);
  const provider = routeScope.provider ?? normalizeScopeLabel(fallback?.provider, 40);
  const from = routeScope.from ?? normalizeDateScopeValue(fallback?.from);
  const to = routeScope.to ?? normalizeDateScopeValue(fallback?.to);
  const date = routeScope.date ?? normalizeDateScopeValue(fallback?.date);
  const asOf = routeScope.asOf ?? normalizeDateScopeValue(fallback?.asOf);
  const windowValue = formatOperatingScopeWindow({ from, to, date, asOf });

  const items: AppShellOperatingScopeItem[] = [
    subjectSymbol ? buildOperatingScopeItem("symbol", "Subject", subjectSymbol) : null,
    fundAccountId ? buildOperatingScopeItem("fundAccountId", "Account", fundAccountId) : null,
    runId ? buildOperatingScopeItem("runId", "Run", runId) : null,
    provider ? buildOperatingScopeItem("provider", "Provider", provider) : null,
    windowValue ? buildOperatingScopeItem("window", "Window", windowValue) : null
  ].filter((item): item is AppShellOperatingScopeItem => Boolean(item));

  const queryParams: AppShellOperatingScopeQueryParam[] = [
    subjectSymbol ? { key: "symbol", value: subjectSymbol, scopeKey: "symbol" as const } : null,
    fundAccountId ? { key: "fundAccountId", value: fundAccountId, scopeKey: "fundAccountId" as const } : null,
    runId ? { key: "runId", value: runId, scopeKey: "runId" as const } : null,
    provider ? { key: "provider", value: provider, scopeKey: "provider" as const } : null,
    from ? { key: "from", value: from, scopeKey: "window" as const } : null,
    to ? { key: "to", value: to, scopeKey: "window" as const } : null,
    date ? { key: "date", value: date, scopeKey: "window" as const } : null,
    asOf ? { key: "asOf", value: asOf, scopeKey: "window" as const } : null
  ].filter((item): item is AppShellOperatingScopeQueryParam => Boolean(item));

  const summary = items.length > 0
    ? items.map((item) => `${item.label}: ${item.value}`).join(" / ")
    : "No operating scope selected";

  return {
    label: "Operating scope",
    summary,
    subjectSymbol,
    fundAccountId,
    runId,
    provider,
    hasScope: items.length > 0,
    clearAriaLabel: buildOperatingScopeClearAriaLabel(items, subjectSymbol),
    items,
    queryParams
  };
}

export function readOperatingContextSymbolFromSearch(search: string): string | null {
  return readOperatingScopeFromSearch(search).symbol ?? null;
}

export function normalizeOperatingContextSymbol(value: string | null | undefined): string | null {
  return normalizeSubjectSymbol(value ?? null);
}

export function appendOperatingScopeToRoute(route: string, operatingScope: AppShellOperatingScopeState): string {
  if (!operatingScope.hasScope) {
    return route;
  }

  const allowedScopeKeys = operatingScopeKeysForRoute(route);
  return operatingScope.queryParams
    .filter((item) => allowedScopeKeys.has(item.scopeKey))
    .reduce((current, item) => appendSearchValue(current, item.key, item.value), route);
}

export function summarizeOperatingScopeForRoute(
  route: string,
  operatingScope: AppShellOperatingScopeState
): string | null {
  if (!operatingScope.hasScope) {
    return null;
  }

  const allowedScopeKeys = operatingScopeKeysForRoute(route);
  const items = operatingScope.items.filter((item) => allowedScopeKeys.has(scopeKeyForOperatingScopeItem(item)));
  return items.length > 0
    ? items.map((item) => `${item.label}: ${item.value}`).join(" / ")
    : null;
}

export function removeOperatingScopeFromSearch(search: string): string {
  const params = new URLSearchParams(search);
  operatingScopeQueryParamKeys.forEach((key) => params.delete(key));
  const next = params.toString();
  return next ? `?${next}` : "";
}

function buildOperatingScopeItem(id: string, label: string, value: string): AppShellOperatingScopeItem {
  return {
    id,
    label,
    value,
    ariaLabel: `${label}: ${value}`
  };
}

function buildOperatingScopeClearAriaLabel(
  items: AppShellOperatingScopeItem[],
  subjectSymbol: string | null
): string | null {
  if (items.length === 0) {
    return null;
  }

  if (items.length === 1 && subjectSymbol) {
    return `Clear ${subjectSymbol} operating context`;
  }

  return `Clear operating scope: ${items.map((item) => `${item.label} ${item.value}`).join(", ")}`;
}

function scopeKeyForOperatingScopeItem(item: AppShellOperatingScopeItem): AppShellOperatingScopeQueryKey {
  switch (item.id) {
    case "symbol":
      return "symbol";
    case "fundAccountId":
      return "fundAccountId";
    case "runId":
      return "runId";
    case "provider":
      return "provider";
    case "window":
    default:
      return "window";
  }
}

function normalizeScopeToken(value: string | null | undefined, maxLength: number): string | null {
  const normalized = value?.trim().replace(/[^A-Za-z0-9._:-]/g, "") ?? "";
  return normalized.length > 0 ? normalized.slice(0, maxLength) : null;
}

function normalizeScopeLabel(value: string | null | undefined, maxLength: number): string | null {
  const normalized = value?.trim().replace(/[^A-Za-z0-9 ._-]/g, "").replace(/\s+/g, " ") ?? "";
  return normalized.length > 0 ? normalized.slice(0, maxLength) : null;
}

function normalizeDateScopeValue(value: string | null | undefined): string | null {
  const normalized = value?.trim().replace(/[^0-9A-Za-z:._+-]/g, "") ?? "";
  return normalized.length > 0 ? normalized.slice(0, 32) : null;
}

function formatOperatingScopeWindow({
  from,
  to,
  date,
  asOf
}: Pick<AppShellOperatingScopeInput, "from" | "to" | "date" | "asOf">): string | null {
  if (from || to) {
    return `${from ?? "Start"} to ${to ?? "Now"}`;
  }

  if (date) {
    return date;
  }

  return asOf ? `as of ${asOf}` : null;
}

function operatingScopeKeysForRoute(route: string): Set<AppShellOperatingScopeQueryKey> {
  const workspaceKey = workspaceForPath(splitContinuityRoute(route).pathname).key;
  switch (workspaceKey) {
    case "data":
      return new Set(["symbol", "provider", "window"]);
    case "strategy":
      return new Set(["symbol", "runId", "provider", "window"]);
    case "trading":
    case "portfolio":
      return new Set(["symbol", "fundAccountId", "runId", "provider", "window"]);
    case "accounting":
    case "reporting":
      return new Set(["symbol", "fundAccountId", "runId", "provider", "window"]);
    case "settings":
      return new Set(["fundAccountId", "provider"]);
  }
}

function appendSearchValue(route: string, key: string, value: string) {
  const hashIndex = route.indexOf("#");
  const routeWithoutHash = hashIndex >= 0 ? route.slice(0, hashIndex) : route;
  const hash = hashIndex >= 0 ? route.slice(hashIndex) : "";
  const searchIndex = routeWithoutHash.indexOf("?");
  const pathname = searchIndex >= 0 ? routeWithoutHash.slice(0, searchIndex) : routeWithoutHash;
  const params = new URLSearchParams(searchIndex >= 0 ? routeWithoutHash.slice(searchIndex) : "");
  params.set(key, value);
  const nextSearch = params.toString();
  return `${pathname}${nextSearch ? `?${nextSearch}` : ""}${hash}`;
}

const operatingScopeQueryParamKeys = ["symbol", "fundAccountId", "runId", "provider", "from", "to", "date", "asOf"] as const;

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

const developmentFixtureDemoSteps = [
  {
    id: "watchlist",
    step: "1",
    href: WORKSTATION_ROUTE_CATALOG.dataWatchlist,
    matchPath: WORKSTATION_ROUTE_CATALOG.dataWatchlist,
    label: "Watchlist",
    ariaLabel: "Open sample watchlist demo lane"
  },
  {
    id: "quotes",
    step: "2",
    href: workstationRouteWithQuery("dataQuotes", { symbol: "AAPL" }),
    matchPath: WORKSTATION_ROUTE_CATALOG.dataQuotes,
    label: "Quotes",
    ariaLabel: "Open sample live quotes for AAPL"
  },
  {
    id: "readiness",
    step: "3",
    href: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
    matchPath: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
    label: "Readiness",
    ariaLabel: "Open sample readiness console"
  },
  {
    id: "connect",
    step: "4",
    href: WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup,
    matchPath: WORKSTATION_ROUTE_CATALOG.settings,
    matchHash: "#alpaca-provider-setup",
    label: "Connect",
    ariaLabel: "Open Alpaca paper provider setup"
  }
] as const;

function isCurrentDevelopmentFixtureDemoStep(
  item: (typeof developmentFixtureDemoSteps)[number],
  pathname: string,
  hash: string
) {
  if (item.matchPath !== pathname) {
    return false;
  }

  return !("matchHash" in item) || item.matchHash === hash;
}
