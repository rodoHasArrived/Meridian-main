import { normalizeWorkspacePath, WORKSPACES, workspaceForPath, workspacePath } from "@/lib/workspace";
import type {
  DataOperationsWorkspaceResponse,
  GovernanceWorkspaceResponse,
  PortfolioWorkspaceResponse,
  ResearchWorkspaceResponse,
  SessionInfo,
  SystemOverviewResponse,
  TradingWorkspaceResponse,
  WorkspaceKey,
  WorkspaceSummary
} from "@/types";

export type ShellStatusTone = "loading" | "warning" | "danger";

export interface ShellStatusItem {
  key: WorkspaceKey | "workflow-catalog";
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

export interface AppShellWorkflowContinuityViewModel {
  title: string;
  summary: string;
  contextLabel: string;
  contextValue: string;
  subjectSymbol: string | null;
  clearSubjectAriaLabel: string | null;
  routeLabel: string;
  stepsLabel: string;
  ariaLabel: string;
  nextActionLabel: string;
  nextActionAriaLabel: string;
  nextActionHref: string;
  steps: AppShellWorkflowContinuityStep[];
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
      operatingContextSymbol
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
  operatingContextSymbol: string | null = null
): AppShellWorkflowContinuityViewModel {
  const subjectSymbol = normalizeSubjectSymbol(readSearchValue(search, "symbol"))
    ?? normalizeSubjectSymbol(operatingContextSymbol);
  const currentRoute = `${pathname}${search}${hash}`;
  const trail = selectWorkflowContinuityTrail(pathname, hash);
  const steps = trail.steps.map((step) => materializeContinuityStep(step, subjectSymbol));
  const activeIndex = findActiveWorkflowStepIndex(trail.steps, pathname, hash);
  const nextIndex = steps.length > 0 ? Math.min(activeIndex + 1, steps.length - 1) : 0;
  const activeStep = steps[activeIndex] ?? null;
  const nextStep = steps[nextIndex] ?? activeStep;
  const stepStatuses = steps.map((step) => buildWorkflowContinuityStepStatus(step.id, statusContext));
  const attentionCount = stepStatuses.filter((status) => status.tone === "blocked" || status.tone === "review").length;
  const contextValue = subjectSymbol
    ? `${activeWorkspace.label} / ${subjectSymbol}`
    : `${activeWorkspace.label} / ${activeStep?.label ?? "Workspace"}`;

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
    clearSubjectAriaLabel: subjectSymbol ? `Clear ${subjectSymbol} operating context` : null,
    routeLabel: currentRoute || "/",
    stepsLabel: `${trail.title} workflow steps`,
    ariaLabel: `${trail.title} continuity`,
    nextActionLabel: activeStep && nextStep && activeStep.id === nextStep.id ? `Stay on ${activeStep.label}` : `Next: ${nextStep?.label ?? activeWorkspace.label}`,
    nextActionAriaLabel: activeStep && nextStep && activeStep.id === nextStep.id
      ? `Stay on ${activeStep.label}`
      : `Continue workflow to ${nextStep?.label ?? activeWorkspace.label}`,
    nextActionHref: nextStep?.href ?? workspacePath(activeWorkspace.key),
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
      items: []
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
      secondaryActionHref: "/settings#backend-capability-coverage",
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
        href: "/data/watchlist",
        matchPath: "/data/watchlist"
      },
      {
        id: "quotes",
        label: "Live quotes",
        description: "Inspect quote, tape, depth, and historical trend evidence for the active symbol.",
        href: "/data/quotes",
        matchPath: "/data/quotes",
        preserveSymbol: true
      },
      {
        id: "alerts",
        label: "Price alerts",
        description: "Track threshold triggers and validate the quote feed behind watched symbols.",
        href: "/data/alerts",
        matchPath: "/data/alerts",
        preserveSymbol: true
      },
      {
        id: "readiness",
        label: "Readiness",
        description: "Review paper-operation blockers, execution controls, replay evidence, and work items.",
        href: "/trading/readiness",
        matchPath: "/trading/readiness"
      },
      {
        id: "provider-setup",
        label: "Provider setup",
        description: "Repair credentials, endpoint acknowledgement, and paper/live provider posture.",
        href: "/settings#alpaca-provider-setup",
        matchPath: "/settings",
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
        href: "/strategy",
        matchPath: "/strategy"
      },
      {
        id: "quant-lab",
        label: "Quant Lab",
        description: "Prototype scripts, parameters, plots, and diagnostics against trusted data.",
        href: "/strategy/quant-lab",
        matchPath: "/strategy/quant-lab"
      },
      {
        id: "covered-call",
        label: "Covered call",
        description: "Preview option chains, run covered-call scenarios, and inspect trade outcomes.",
        href: "/strategy/covered-call",
        matchPath: "/strategy/covered-call"
      },
      {
        id: "paper-readiness",
        label: "Paper readiness",
        description: "Confirm replay consistency, acceptance gates, execution controls, and approval blockers.",
        href: "/trading/readiness",
        matchPath: "/trading/readiness"
      },
      {
        id: "portfolio-review",
        label: "Portfolio review",
        description: "Check exposure, account sync, positions, cash, and run-to-portfolio continuity.",
        href: "/portfolio",
        matchPath: "/portfolio"
      },
      {
        id: "evidence-review",
        label: "Evidence review",
        description: "Package lineage, stale evidence, and packet completeness for governed review.",
        href: "/reporting/evidence",
        matchPath: "/reporting/evidence"
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
        href: "/trading/readiness",
        matchPath: "/trading/readiness"
      },
      {
        id: "trading-cockpit",
        label: "Trading cockpit",
        description: "Stage paper orders, inspect positions, monitor fills, and control strategy actions.",
        href: "/trading",
        matchPath: "/trading"
      },
      {
        id: "portfolio-exposure",
        label: "Exposure",
        description: "Review household positions, account sync, cash, buying power, and risk posture.",
        href: "/portfolio",
        matchPath: "/portfolio"
      },
      {
        id: "reconciliation",
        label: "Reconciliation",
        description: "Resolve ledger, security, cash, and position breaks before accepting readiness.",
        href: "/accounting/reconciliation",
        matchPath: "/accounting/reconciliation"
      },
      {
        id: "report-packs",
        label: "Report packs",
        description: "Review governed output targets, evidence readiness, and export posture.",
        href: "/reporting/report-packs",
        matchPath: "/reporting/report-packs"
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
        href: "/accounting/security-master",
        matchPath: "/accounting/security-master"
      },
      {
        id: "reconciliation",
        label: "Reconciliation",
        description: "Inspect run breaks, tolerance profile health, sign-off status, and recovery actions.",
        href: "/accounting/reconciliation",
        matchPath: "/accounting/reconciliation"
      },
      {
        id: "ledger",
        label: "Ledger",
        description: "Validate trial balance, cash-flow context, account detail, and accounting basis.",
        href: "/accounting/ledger",
        matchPath: "/accounting/ledger"
      },
      {
        id: "evidence",
        label: "Evidence",
        description: "Trace packet lineage, freshness, completeness, and unresolved evidence warnings.",
        href: "/reporting/evidence",
        matchPath: "/reporting/evidence"
      },
      {
        id: "report-packs",
        label: "Report packs",
        description: "Package close evidence into governed report outputs and approval-ready exports.",
        href: "/reporting/report-packs",
        matchPath: "/reporting/report-packs"
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
  subjectSymbol: string | null
): AppShellWorkflowContinuityStep {
  const href = step.preserveSymbol && subjectSymbol
    ? appendSearchValue(step.href, "symbol", subjectSymbol)
    : step.href;

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

export function readOperatingContextSymbolFromSearch(search: string): string | null {
  return normalizeSubjectSymbol(readSearchValue(search, "symbol"));
}

export function normalizeOperatingContextSymbol(value: string | null | undefined): string | null {
  return normalizeSubjectSymbol(value ?? null);
}

function appendSearchValue(route: string, key: string, value: string) {
  const hashIndex = route.indexOf("#");
  const routeWithoutHash = hashIndex >= 0 ? route.slice(0, hashIndex) : route;
  const hash = hashIndex >= 0 ? route.slice(hashIndex) : "";
  const separator = routeWithoutHash.includes("?") ? "&" : "?";
  return `${routeWithoutHash}${separator}${encodeURIComponent(key)}=${encodeURIComponent(value)}${hash}`;
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

const developmentFixtureDemoSteps = [
  {
    id: "watchlist",
    step: "1",
    href: "/data/watchlist",
    matchPath: "/data/watchlist",
    label: "Watchlist",
    ariaLabel: "Open sample watchlist demo lane"
  },
  {
    id: "quotes",
    step: "2",
    href: "/data/quotes?symbol=AAPL",
    matchPath: "/data/quotes",
    label: "Quotes",
    ariaLabel: "Open sample live quotes for AAPL"
  },
  {
    id: "readiness",
    step: "3",
    href: "/trading/readiness",
    matchPath: "/trading/readiness",
    label: "Readiness",
    ariaLabel: "Open sample readiness console"
  },
  {
    id: "connect",
    step: "4",
    href: "/settings#alpaca-provider-setup",
    matchPath: "/settings",
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
