import {
  normalizeWorkspacePath,
  workspaceForPath
} from "@/lib/workspace";
import {
  buildCommandPaletteTriggerState,
  type AppShellCommandPaletteTriggerState
} from "@/app-shell.command-palette";
import {
  buildRouteFocusState,
  type AppShellRouteFocusState
} from "@/app-shell.route-focus";
import {
  buildShellFailureItems,
  buildShellStatusPanel,
  type AppShellWorkspaceErrorMap,
  type ShellStatusPanel
} from "@/app-shell.status-panel";
import {
  buildTrustStripState,
  type AppShellTrustStripState
} from "@/app-shell.trust-strip";
import { buildWorkflowContinuityViewModel } from "@/app-shell.workflow-continuity-view-model";
import type { AppShellOperatingScopeInput } from "@/app-shell.operating-scope";
import type { AppShellWorkflowContinuityViewModel } from "@/app-shell.workflow-continuity-types";
import type {
  DataWorkspaceResponse,
  AccountingWorkspaceResponse,
  OperatorWorkflowHomeSummary,
  ReportingWorkspaceResponse,
  PortfolioWorkspaceResponse,
  StrategyWorkspaceResponse,
  SessionInfo,
  SystemOverviewResponse,
  TradingWorkspaceResponse,
  WorkspaceKey,
  WorkspaceSummary
} from "@/types";

export {
  buildCommandPaletteTriggerState,
  COMMAND_PALETTE_DIALOG_ID,
  isAppShellEditableShortcutTarget,
  resolveAppShellCommandPaletteShortcut
} from "@/app-shell.command-palette";
export type {
  AppShellCommandPaletteShortcutCommand,
  AppShellCommandPaletteShortcutState,
  AppShellCommandPaletteTriggerState
} from "@/app-shell.command-palette";
export { buildDevelopmentFixtureNoticeViewModel } from "@/app-shell.development-fixture-notice";
export type { DevelopmentFixtureNoticeStep, DevelopmentFixtureNoticeViewModel } from "@/app-shell.development-fixture-notice";
export { buildDataProvenanceBadgeViewModel, normalizeDataProvenance } from "@/app-shell.data-provenance-badge";
export type { DataProvenanceBadgeViewModel, DataProvenanceKind } from "@/app-shell.data-provenance-badge";
export type { AppShellLinkedContextItem } from "@/app-shell.linked-context";
export type { AppShellRouteFocusState } from "@/app-shell.route-focus";
export type { ShellStatusItem, ShellStatusPanel, ShellStatusTone } from "@/app-shell.status-panel";
export type { AppShellTrustStripItem, AppShellTrustStripState, AppShellTrustStripTone } from "@/app-shell.trust-strip";
export type {
  AppShellDecisionBrief,
  AppShellEvidenceTimelineItem,
  AppShellOperatorFocusItem,
  AppShellPrimaryOperatorWorkflowStep,
  AppShellWorkflowContinuityDisclosurePanel,
  AppShellWorkflowContinuityDisclosurePanelId,
  AppShellWorkflowContinuityDisclosureState,
  AppShellWorkflowContinuityStatusTone,
  AppShellWorkflowContinuityStep,
  AppShellWorkflowContinuityViewModel
} from "@/app-shell.workflow-continuity-types";
export {
  appendOperatingScopeToRoute,
  buildOperatingScopeFromSearch,
  normalizeOperatingContextSymbol,
  readOperatingContextSymbolFromSearch,
  readOperatingScopeFromSearch,
  removeOperatingScopeFromSearch,
  summarizeOperatingScopeForRoute
} from "@/app-shell.operating-scope";
export type {
  AppShellOperatingScopeInput,
  AppShellOperatingScopeItem,
  AppShellOperatingScopeQueryKey,
  AppShellOperatingScopeQueryParam,
  AppShellOperatingScopeState
} from "@/app-shell.operating-scope";

export interface AppShellViewState {
  activeWorkspace: WorkspaceSummary;
  statusPanel: ShellStatusPanel | null;
  canRenderRoutes: boolean;
  routeFocus: AppShellRouteFocusState;
  trustStrip: AppShellTrustStripState;
  workflowContinuity: AppShellWorkflowContinuityViewModel;
  commandPaletteTrigger: AppShellCommandPaletteTriggerState;
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

export type WorkspaceErrorMap = AppShellWorkspaceErrorMap;

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
      session: payload.session,
      data: payload.data
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

export function getWorkspaceForPath(pathname: string): WorkspaceSummary {
  return workspaceForPath(pathname);
}

export function normalizeWorkspace(pathname: string): WorkspaceKey {
  return normalizeWorkspacePath(pathname);
}
