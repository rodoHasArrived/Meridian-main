import {
  normalizeWorkspacePath,
  WORKSPACES,
  WORKSTATION_ROUTE_CATALOG,
  workflowTargetPath,
  workspaceForPath
} from "@/lib/workspace";
import { type AppShellOperatingScopeInput } from "@/lib/app-shell-operating-scope";
import {
  developmentFixtureDemoSteps,
  type DevelopmentFixtureDemoStep
} from "@/lib/app-shell-workflow-catalog";
import {
  buildWorkflowContinuityViewModel,
  type AppShellWorkflowContinuityViewModel
} from "@/lib/app-shell-workflow-continuity";
import packageJson from "../package.json";
import type {
  DataProviderRecord,
  DataWorkspaceResponse,
  AccountingWorkspaceResponse,
  OperatorWorkflowHomeSummary,
  ReportingWorkspaceResponse,
  WorkspaceWorkflowSummary,
  PortfolioWorkspaceResponse,
  StrategyWorkspaceResponse,
  SessionInfo,
  SystemOverviewResponse,
  TradingWorkspaceResponse,
  WorkspaceKey,
  WorkspaceSummary
} from "@/types";

export type {
  AppShellDecisionBrief,
  AppShellEvidenceTimelineItem,
  AppShellLinkedContextItem,
  AppShellOperatorFocusItem,
  AppShellWorkflowContinuityDisclosurePanel,
  AppShellWorkflowContinuityDisclosurePanelId,
  AppShellWorkflowContinuityDisclosureState,
  AppShellWorkflowContinuityStatusTone,
  AppShellWorkflowContinuityStep,
  AppShellWorkflowContinuityViewModel
} from "@/lib/app-shell-workflow-continuity";

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
  trustStrip: AppShellTrustStripState;
  workflowContinuity: AppShellWorkflowContinuityViewModel;
  commandPaletteTrigger: AppShellCommandPaletteTriggerState;
}

export type AppShellTrustStripTone = "ready" | "review" | "blocked" | "pending";

export interface AppShellTrustStripItem {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: AppShellTrustStripTone;
  ariaLabel: string;
  href: string | null;
  actionLabel: string | null;
}

export interface AppShellTrustStripState {
  ariaLabel: string;
  items: AppShellTrustStripItem[];
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
  strategy: StrategyWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  portfolio: PortfolioWorkspaceResponse | null;
  data: DataWorkspaceResponse | null;
  accounting: AccountingWorkspaceResponse | null;
  reporting: ReportingWorkspaceResponse | null;
  workflowSummary: OperatorWorkflowHomeSummary | null;
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
  usingDevelopmentFixtures?: boolean;
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
  usingDevelopmentFixtures = false,
  payload
}: BuildAppShellViewStateOptions): AppShellViewState {
  const activeWorkspace = getWorkspaceForPath(pathname);
  const failedItems = buildShellFailureItems(workspaceErrors, workflowError);
  const hasAnyPayload = Boolean(
    payload.session
    || payload.overview
    || payload.strategy
    || payload.trading
    || payload.portfolio
    || payload.data
    || payload.accounting
    || payload.reporting
  );
  const bootstrapFailed = !loading && !hasAnyPayload;
  const canRenderRoutes = !bootstrapFailed && (!loading || activeWorkspace.key === "accounting");

  return {
    activeWorkspace,
    statusPanel: buildShellStatusPanel({
      loading,
      error,
      failedItems,
      bootstrapFailed
    }),
    canRenderRoutes,
    routeFocus: buildRouteFocusState(pathname, search, hash, activeWorkspace),
    trustStrip: buildTrustStripState({
      loading,
      bootstrapFailed,
      usingDevelopmentFixtures,
      workspaceErrors,
      payload
    }),
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

function buildTrustStripState({
  loading,
  bootstrapFailed,
  usingDevelopmentFixtures,
  workspaceErrors,
  payload
}: {
  loading: boolean;
  bootstrapFailed: boolean;
  usingDevelopmentFixtures: boolean;
  workspaceErrors: WorkspaceErrorMap;
  payload: AppShellWorkspacePayload;
}): AppShellTrustStripState {
  const session = payload.session;
  const providerPosture = buildProviderTrustStripItem(payload.data);
  const failedWorkspaceCount = Object.keys(workspaceErrors).length;

  const environmentValue = loading
    ? "Loading"
    : session?.environment
      ? titleCase(session.environment)
      : "Unknown";
  const environmentTone: AppShellTrustStripTone = session?.environment === "live"
    ? "blocked"
    : session?.environment === "paper"
      ? "ready"
      : loading
        ? "pending"
        : "review";

  const dataSourceValue = usingDevelopmentFixtures
    ? "Demo data"
    : bootstrapFailed
      ? "Unavailable"
      : failedWorkspaceCount > 0
        ? "Limited data"
        : "Connected";
  const dataSourceTone: AppShellTrustStripTone = usingDevelopmentFixtures
    ? "pending"
    : bootstrapFailed
      ? "blocked"
      : failedWorkspaceCount > 0
        ? "review"
        : "ready";
  const dataSourceDetail = usingDevelopmentFixtures
    ? "Demo data is visible; confirm live source status before making operating decisions."
    : bootstrapFailed
      ? "Workspace data is unavailable from this machine. Try again or open diagnostics."
      : failedWorkspaceCount > 0
        ? `${formatCount(failedWorkspaceCount, "workspace area")} did not load.`
        : "Workspace data loaded from Meridian.";

  return {
    ariaLabel: "Workstation build, mode, data source, and provider posture",
    items: [
      {
        id: "build",
        label: "Build",
        value: `v${packageJson.version}`,
        detail: "Current Meridian web release.",
        tone: "ready",
        ariaLabel: `Build ${packageJson.version}. Current Meridian web release.`,
        href: null,
        actionLabel: null
      },
      {
        id: "mode",
        label: "Mode",
        value: environmentValue,
        detail: session
          ? `Session ${session.displayName} is operating in ${environmentValue.toLowerCase()} mode.`
          : "Session environment is not loaded yet.",
        tone: environmentTone,
        ariaLabel: `Mode ${environmentValue}. ${session ? `Session ${session.displayName} is operating in ${environmentValue.toLowerCase()} mode.` : "Session environment is not loaded yet."}`,
        href: session?.environment === "live"
          ? WORKSTATION_ROUTE_CATALOG.tradingReadiness
          : null,
        actionLabel: session?.environment === "live" ? "Review readiness" : null
      },
      {
        id: "source",
        label: "Source",
        value: dataSourceValue,
        detail: dataSourceDetail,
        tone: dataSourceTone,
        ariaLabel: `Data source ${dataSourceValue}. ${dataSourceDetail}`,
        href: bootstrapFailed || failedWorkspaceCount > 0
          ? WORKSTATION_ROUTE_CATALOG.settingsBackendCapabilityCoverage
          : null,
        actionLabel: bootstrapFailed || failedWorkspaceCount > 0 ? "Open diagnostics" : null
      },
      providerPosture
    ]
  };
}

function buildProviderTrustStripItem(data: DataWorkspaceResponse | null): AppShellTrustStripItem {
  if (!data) {
    return {
      id: "providers",
      label: "Providers",
      value: "Pending",
      detail: "Provider posture has not loaded yet.",
      tone: "pending",
      ariaLabel: "Providers Pending. Provider posture has not loaded yet.",
      href: WORKSTATION_ROUTE_CATALOG.dataProviders,
      actionLabel: "Open provider posture"
    };
  }

  const providers = data.providers ?? [];
  if (providers.length === 0) {
    return {
      id: "providers",
      label: "Providers",
      value: "No providers",
      detail: "No provider status rows are available in the current workspace data.",
      tone: "review",
      ariaLabel: "Providers No providers. No provider status rows are available in the current workspace data.",
      href: WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup,
      actionLabel: "Configure provider"
    };
  }

  const degraded = providers.filter((provider) => provider.status === "Degraded").length;
  const warning = providers.filter((provider) => provider.status === "Warning").length;
  const healthy = providers.filter((provider) => provider.status === "Healthy").length;
  const value = degraded > 0
    ? `${degraded} degraded`
    : warning > 0
      ? `${warning} warning`
      : `${healthy}/${providers.length} healthy`;
  const detail = degraded > 0
    ? `${formatCount(degraded, "provider")} degraded; open Data provider posture before trading decisions.`
    : warning > 0
      ? `${formatCount(warning, "provider")} warning; review provider trust before relying on fresh data.`
      : `${formatCount(healthy, "provider")} healthy in the loaded data posture.`;
  const tone: AppShellTrustStripTone = degraded > 0 ? "blocked" : warning > 0 ? "review" : "ready";

  return {
    id: "providers",
    label: "Providers",
    value,
    detail,
    tone,
    ariaLabel: `Providers ${value}. ${detail}`,
    href: degraded > 0 || warning > 0
      ? WORKSTATION_ROUTE_CATALOG.dataProviders
      : null,
    actionLabel: degraded > 0 || warning > 0 ? "Open provider posture" : null
  };
}

function formatCount(count: number, singular: string, plural = `${singular}s`): string {
  return `${count} ${count === 1 ? singular : plural}`;
}

function titleCase(value: string): string {
  return value.length > 0 ? `${value.charAt(0).toUpperCase()}${value.slice(1).toLowerCase()}` : value;
}

export function buildCommandPaletteTriggerState(open: boolean): AppShellCommandPaletteTriggerState {
  return {
    label: open ? "Close workstation command palette (Ctrl K)" : "Open workstation command palette (Ctrl K)",
    placeholder: "Go to route, action, evidence...",
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
    detail: "Showing demo data because live Meridian data is unavailable; use the evidence path for watchlist, quotes, readiness, and Alpaca setup.",
    workflowLabel: "Evidence path",
    retryLabel: refreshing ? "Retrying live data" : "Retry live data",
    retryAriaLabel: refreshing
      ? "Retrying live Meridian workspace data"
      : "Retry live Meridian workspace data",
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
  const workspaceTitle = pathname === "/" ? "Daily Control Tower" : `${activeWorkspace.label} Workstation`;
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
      title: "Preparing workspace",
      detail: "Loading session state, operator workspaces, and the initial evidence views.",
      role: "status",
      ariaLive: "polite",
      actionLabel: null,
      actionAriaLabel: null,
      secondaryActionLabel: null,
      secondaryActionAriaLabel: null,
      secondaryActionHref: null,
      itemListLabel: "Workspace data loading status",
      items: [
        {
          key: "session-state",
          label: "Session state",
          detail: "Resolving operator context and environment guardrails.",
          ariaLabel: "Session state: resolving operator context and environment guardrails"
        },
        {
          key: "workspace-payloads",
          label: "Workspace data",
          detail: "Loading Trading, Portfolio, Accounting, Reporting, Strategy, Data, and Settings.",
          ariaLabel: "Workspace data: loading Trading, Portfolio, Accounting, Reporting, Strategy, Data, and Settings"
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
      title: "Workspace data unavailable",
      detail: formatUserVisibleWorkspaceError(error, "Meridian could not load workspace data. Try again or open diagnostics."),
      role: "alert",
      ariaLive: "assertive",
      actionLabel: "Retry workspace data",
      actionAriaLabel: "Retry workspace data",
      secondaryActionLabel: null,
      secondaryActionAriaLabel: null,
      secondaryActionHref: null,
      itemListLabel: "Workspace data issues",
      items: failedItems
    };
  }

  if (failedItems.length > 0) {
    const areaLabel = failedItems.length === 1 ? "area" : "areas";
    const recoveryLabel = failedItems.length === 1 ? "that area recovers" : "those areas recover";
    return {
      id: "workstation-shell-status-degraded",
      titleId: "workstation-shell-status-degraded-title",
      detailId: "workstation-shell-status-degraded-detail",
      tone: "warning",
      title: "Some workspace data is unavailable",
      detail: `${failedItems.length} workspace ${areaLabel} did not load. Available routes remain open while ${recoveryLabel}.`,
      role: "status",
      ariaLive: "polite",
      actionLabel: "Retry failed areas",
      actionAriaLabel: "Retry failed workspace areas",
      secondaryActionLabel: "Review diagnostics",
      secondaryActionAriaLabel: "Review Settings diagnostics for failed workspace areas",
      secondaryActionHref: WORKSTATION_ROUTE_CATALOG.settingsBackendCapabilityCoverage,
      itemListLabel: "Workspace data issues",
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
      const visibleDetail = formatUserVisibleWorkspaceError(detail, "Workspace data unavailable. Try again or open diagnostics.");
      return {
        key: workspaceKey,
        label,
        detail: visibleDetail,
        ariaLabel: `${label}: ${visibleDetail}`
      };
    })
    .sort((left, right) => left.label.localeCompare(right.label));

  if (workflowError) {
    const visibleDetail = formatUserVisibleWorkspaceError(workflowError, "Workflow data unavailable. Try again or open diagnostics.");
    items.push({
      key: "workflow-catalog",
      label: "Workflow catalog",
      detail: visibleDetail,
      ariaLabel: `Workflow catalog: ${visibleDetail}`
    });
  }

  return items;
}

function formatUserVisibleWorkspaceError(error: string | null | undefined, fallback: string): string {
  const detail = error?.trim();
  if (!detail) {
    return fallback;
  }

  return looksLikeRawTechnicalResponse(detail) ? fallback : detail;
}

function looksLikeRawTechnicalResponse(value: string): boolean {
  return /<!doctype\s+html/i.test(value)
    || /<html(?:\s|>)/i.test(value)
    || /\bfile not found\b/i.test(value)
    || /^404(?:\s|$|:|-)/i.test(value)
    || /\bhttp\s+error\s+404\b/i.test(value);
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

function isCurrentDevelopmentFixtureDemoStep(
  item: DevelopmentFixtureDemoStep,
  pathname: string,
  hash: string
) {
  if (item.matchPath !== pathname) {
    return false;
  }

  return !("matchHash" in item) || item.matchHash === hash;
}
