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
    workflowContinuity: buildWorkflowContinuityViewModel(pathname, search, hash, activeWorkspace, {
      loading,
      error,
      workflowError,
      workspaceErrors,
      payload
    }),
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
  statusContext: WorkflowContinuityStatusContext = emptyWorkflowContinuityStatusContext
): AppShellWorkflowContinuityViewModel {
  const subjectSymbol = normalizeSubjectSymbol(readSearchValue(search, "symbol"));
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
    id: "investment-operations-path",
    title: "Investment Operations Path",
    summary: "Move from trusted data through research, paper validation, books, reconciliation, and governed reporting without losing operating context.",
    steps: [
      {
        id: "trusted-data",
        label: "Trusted data",
        description: "Curate symbols, validate providers, inspect quotes, and repair backfill evidence.",
        href: "/data/watchlist",
        matchPath: "/data"
      },
      {
        id: "research",
        label: "Research",
        description: "Compare runs, inspect research evidence, and prepare promotion candidates.",
        href: "/strategy",
        matchPath: "/strategy"
      },
      {
        id: "paper-readiness",
        label: "Paper readiness",
        description: "Review replay consistency, execution controls, promotion gates, and operator work items.",
        href: "/trading/readiness",
        matchPath: "/trading"
      },
      {
        id: "portfolio-ledger",
        label: "Portfolio ledger",
        description: "Confirm exposure, brokerage sync, positions, and run-level portfolio continuity.",
        href: "/portfolio",
        matchPath: "/portfolio"
      },
      {
        id: "reconciliation",
        label: "Reconciliation",
        description: "Resolve breaks, reference-data gaps, ledger variance, and required sign-off detail.",
        href: "/accounting/reconciliation",
        matchPath: "/accounting"
      },
      {
        id: "governed-report",
        label: "Governed report",
        description: "Package evidence, review report readiness, and export governed outputs.",
        href: "/reporting/report-packs",
        matchPath: "/reporting"
      }
    ]
  }
];

const defaultWorkflowContinuityTrail = workflowContinuityTrails[0];

function selectWorkflowContinuityTrail(pathname: string, hash: string): WorkflowContinuityTrailDefinition {
  void pathname;
  void hash;
  return defaultWorkflowContinuityTrail;
}

function materializeContinuityStep(
  step: WorkflowContinuityTrailStepDefinition,
  subjectSymbol: string | null
): AppShellWorkflowContinuityStep {
  const href = step.preserveSymbol && subjectSymbol
    ? `${step.href}?symbol=${encodeURIComponent(subjectSymbol)}`
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
    case "trusted-data":
      return buildTrustedDataContinuityStatus(context);
    case "research":
      return buildResearchContinuityStatus(context);
    case "paper-readiness":
      return buildPaperReadinessContinuityStatus(context);
    case "portfolio-ledger":
      return buildPortfolioLedgerContinuityStatus(context);
    case "reconciliation":
      return buildReconciliationContinuityStatus(context);
    case "governed-report":
      return buildGovernedReportContinuityStatus(context);
    default:
      return context.error || context.workflowError
        ? { label: "Review", tone: "review" }
        : { label: "Ready", tone: "ready" };
  }
}

const workflowContinuityWorkspaceErrors: Record<string, WorkspaceKey[]> = {
  "trusted-data": ["data"],
  research: ["strategy"],
  "paper-readiness": ["trading"],
  "portfolio-ledger": ["portfolio"],
  reconciliation: ["accounting"],
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

function routeMatchesStep(pathname: string, hash: string, step: AppShellWorkflowContinuityStep): boolean {
  const candidate = splitContinuityRoute(step.href);
  if (candidate.hash) {
    return pathname === candidate.pathname && hash === candidate.hash;
  }

  return pathname === candidate.pathname || pathname.startsWith(`${candidate.pathname}/`);
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
