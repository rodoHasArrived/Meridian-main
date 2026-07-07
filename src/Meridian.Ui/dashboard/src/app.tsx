import { Component, lazy, memo, Suspense, useEffect, useMemo, useRef, useState, type ErrorInfo, type ReactNode } from "react";
import {
  ArrowRight,
  AlertTriangle,
  LoaderCircle,
  Menu,
  Search
} from "lucide-react";
import { Link, Navigate, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import "@/styles/app-shell.css";
import meridianMarkUrl from "@/assets/brand/meridian-mark-light.svg";
import {
  buildAppShellViewState,
  isAppShellEditableShortcutTarget,
  resolveAppShellCommandPaletteShortcut,
  type AppShellTrustStripState,
  type ShellStatusPanel
} from "@/app-shell.view-model";
import {
  appendOperatingScopeToRoute,
  buildOperatingScopeFromSearch,
  operatingScopeDimensionsForRoute,
  readOperatingScopeFromSearch,
  removeOperatingScopeFromSearch,
  type AppShellOperatingScopeInput
} from "@/app-shell.operating-scope";
import { CommandPalette } from "@/components/meridian/command-palette";
import { ScopePicker } from "@/components/meridian/scope-picker";
import { collectScopeFundAccounts } from "@/lib/operating-scope/fund-accounts";
import { WorkflowContinuityDock } from "@/components/meridian/workflow-continuity-dock";
import { WorkspaceHeader } from "@/components/meridian/workspace-header";
import { CompanionPaneWindow } from "@/components/meridian/companion-pane-window";
import { LayoutSwitcher } from "@/components/meridian/layout-switcher";
import { isCompanionPaneRoute, openCompanionPane } from "@/lib/companion-pane/pane-window";
import { setOpenCompanionPaneIds } from "@/lib/companion-pane/open-registry";
import { broadcastCompanionState } from "@/lib/companion-pane/opener-broadcast";
import { applyDensity, writeStoredDensity } from "@/lib/density";
import type { LayoutRestorePlan } from "@/lib/saved-layouts";
import { WorkspaceNav } from "@/components/meridian/workspace-nav";
import { Skeleton } from "@/components/data/skeleton";
import { Badge } from "@/components/ui/badge";
import type { BreadcrumbItem } from "@/components/ui/breadcrumb";
import { Button } from "@/components/ui/button";
import { PanelSurface } from "@/components/ui/panel-surface";
import {
  Sheet,
  SheetBody,
  SheetCloseButton,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle
} from "@/components/ui/sheet";
import { StatusBanner } from "@/components/ui/status-banner";
import { ToastProvider } from "@/components/ui/toast";
import { useWorkstationData } from "@/hooks/use-workstation-data";
import { markWorkflowPresetUsed, pinWorkflowPreset } from "@/lib/api";
import {
  useCommandPaletteActions,
  type CommandPaletteActionItem
} from "@/components/meridian/command-palette.actions";
import { CopyLinkButton } from "@/components/meridian/copy-link-button";
import { SaveViewButton } from "@/components/meridian/save-view-dialog";
import { NotificationCenter } from "@/components/meridian/notification-center";
import { ActivityCenter } from "@/components/meridian/activity-center";
import { ActivityLogProvider } from "@/lib/activity-log/store";
import {
  OnboardingCoachMark,
  OnboardingHeaderProgress,
  useOnboardingTour
} from "@/components/meridian/onboarding-tour";
import { PriceAlertsProvider } from "@/lib/price-alerts/service";
import { cn } from "@/lib/utils";
import { legacyWorkspaceRedirect, workspacePath } from "@/lib/workspace";
import type { WorkspaceKey, WorkspaceSummary } from "@/types";

const DataScreen = lazy(() => import("@/screens/data-screen").then((module) => ({ default: memo(module.DataScreen) })));
const DailyControlTowerScreen = lazy(() => import("@/screens/daily-control-tower-screen").then((module) => ({ default: memo(module.DailyControlTowerScreen) })));
const EvidenceWorkbenchScreen = lazy(() => import("@/screens/evidence-workbench-screen").then((module) => ({ default: module.EvidenceWorkbenchScreen })));
const AccountingScreen = lazy(() => import("@/screens/accounting-screen").then((module) => ({ default: memo(module.AccountingScreen) })));
const FamilyOfficeScreen = lazy(() => import("@/screens/family-office-screen").then((module) => ({ default: module.FamilyOfficeScreen })));
const CashLadderScreen = lazy(() => import("@/screens/cash-ladder-screen").then((module) => ({ default: module.CashLadderScreen })));
const LiveQuotesScreen = lazy(() => import("@/screens/live-quotes-screen").then((module) => ({ default: module.LiveQuotesScreen })));
const OperatorReadinessConsole = lazy(() => import("@/screens/operator-readiness-console").then((module) => ({ default: memo(module.OperatorReadinessConsole) })));
const OperationsContinuityScreen = lazy(() => import("@/screens/operations-continuity-screen").then((module) => ({ default: module.OperationsContinuityScreen })));
const TrialBalanceScreen = lazy(() => import("@/screens/trial-balance-screen").then((module) => ({ default: module.TrialBalanceScreen })));
const JournalEntryDetailScreen = lazy(() => import("@/screens/journal-entry-detail-screen").then((module) => ({ default: module.JournalEntryDetailScreen })));
const AssetDetailScreen = lazy(() => import("@/screens/asset-detail-screen").then((module) => ({ default: module.AssetDetailScreen })));
const AccountDetailScreen = lazy(() => import("@/screens/finance-standard-pages-screen").then((module) => ({ default: module.AccountDetailScreen })));
const ApprovalInboxScreen = lazy(() => import("@/screens/finance-standard-pages-screen").then((module) => ({ default: module.ApprovalInboxScreen })));
const CloseCalendarScreen = lazy(() => import("@/screens/finance-standard-pages-screen").then((module) => ({ default: module.CloseCalendarScreen })));
const EvidenceDetailScreen = lazy(() => import("@/screens/finance-standard-pages-screen").then((module) => ({ default: module.EvidenceDetailScreen })));
const LedgerExplorerScreen = lazy(() => import("@/screens/finance-standard-pages-screen").then((module) => ({ default: module.LedgerExplorerScreen })));
const ReconciliationMatchWorkbenchScreen = lazy(() => import("@/screens/finance-standard-pages-screen").then((module) => ({ default: module.ReconciliationMatchWorkbenchScreen })));
const StatementImportScreen = lazy(() => import("@/screens/statement-import-screen").then((module) => ({ default: module.StatementImportScreen })));
const ReportPreviewValidationScreen = lazy(() => import("@/screens/finance-standard-pages-screen").then((module) => ({ default: module.ReportPreviewValidationScreen })));
const ReportRunDetailScreen = lazy(() => import("@/screens/finance-standard-pages-screen").then((module) => ({ default: module.ReportRunDetailScreen })));
const ReportLibraryScreen = lazy(() => import("@/screens/report-library-screen").then((module) => ({ default: module.ReportLibraryScreen })));
const ReportRunParametersScreen = lazy(() => import("@/screens/report-run-parameters-screen").then((module) => ({ default: module.ReportRunParametersScreen })));
const OperationsRecordReleaseScreen = lazy(() => import("@/screens/operations-record-release-screen").then((module) => ({ default: module.OperationsRecordReleaseScreen })));
const EntitySetupWizard = lazy(() => import("@/features/fund-structure/entity-setup-wizard").then((module) => ({ default: module.EntitySetupWizard })));
const PortfolioScreen = lazy(() => import("@/screens/portfolio-screen").then((module) => ({ default: memo(module.PortfolioScreen) })));
const CoveredCallScreen = lazy(() => import("@/screens/covered-call-screen").then((module) => ({ default: module.CoveredCallScreen })));
const PriceAlertsScreen = lazy(() => import("@/screens/price-alerts-screen").then((module) => ({ default: module.PriceAlertsScreen })));
const QuantLabScreen = lazy(() => import("@/screens/quant-lab-screen").then((module) => ({ default: module.QuantLabScreen })));
const ReportingScreen = lazy(() => import("@/screens/reporting-screen").then((module) => ({ default: memo(module.ReportingScreen) })));
const StrategyScreen = lazy(() => import("@/screens/strategy-screen").then((module) => ({ default: memo(module.StrategyScreen) })));
const StrategyFormulaWorkbenchScreen = lazy(() => import("@/screens/strategy-formula-workbench-screen").then((module) => ({ default: module.StrategyFormulaWorkbenchScreen })));
const StrategyDesignerScreen = lazy(() => import("@/screens/strategy-designer-screen").then((module) => ({ default: module.StrategyDesignerScreen })));
const SettingsScreen = lazy(() => import("@/screens/settings-screen").then((module) => ({ default: memo(module.SettingsScreen) })));
const TradingScreen = lazy(() => import("@/screens/trading-screen").then((module) => ({ default: memo(module.TradingScreen) })));
const WatchlistScreen = lazy(() => import("@/screens/watchlist-screen").then((module) => ({ default: module.WatchlistScreen })));

export function App() {
  return (
    <PriceAlertsProvider>
      <ToastProvider>
        <ActivityLogProvider>
          <AppRoot />
        </ActivityLogProvider>
      </ToastProvider>
    </PriceAlertsProvider>
  );
}

/**
 * Chrome-less companion panes (`/panes/*`) render before — and instead of — the
 * full workstation shell, so a pop-out window carries no masthead, navigation, or
 * workstation data loading. Everything else renders the shell.
 */
function AppRoot() {
  const { pathname } = useLocation();
  if (isCompanionPaneRoute(pathname)) {
    return <CompanionPaneWindow />;
  }
  return <AppShell />;
}

function AppShell() {
  const [commandOpen, setCommandOpen] = useState(false);
  const [navOpen, setNavOpen] = useState(false);
  const [scopePickerOpen, setScopePickerOpen] = useState(false);
  const [routeAnnouncement, setRouteAnnouncement] = useState("");
  const [storedOperatingScope, setStoredOperatingScope] = useState(readStoredOperatingScope);
  const onboardingTour = useOnboardingTour();
  const workbenchRef = useRef<HTMLElement | null>(null);
  const previousRouteKeyRef = useRef<string | null>(null);
  const suppressScopePersistRef = useRef(false);
  const navigate = useNavigate();
  const { hash, pathname, search } = useLocation();
  const activeWorkspace = resolveWorkspaceKeyFromPath(pathname);
  const routeOperatingScope = useMemo(() => readOperatingScopeFromSearch(search), [search]);
  const operatingScopeInput = useMemo(
    () => mergeOperatingScopes(storedOperatingScope, routeOperatingScope),
    [storedOperatingScope, routeOperatingScope]
  );
  const operatingContextSymbol = operatingScopeInput.symbol ?? null;
  const {
    session,
    overview,
    strategy,
    trading,
    portfolio,
    portfolioMultiAssetCoverage,
    data,
    accounting,
    reporting,
    brokerageConnection,
    robinhoodConnection,
    providerConnections,
    providerReadiness,
    providerRoutingConnections,
    providerRoutingBindings,
    providerRoutingTrustSnapshots,
    providerRoutingRefreshing,
    rolePermissionCatalog,
    securityAssetProfiles,
    ledgerMappingWorkbench,
    operationsApprovalPolicyMatrix,
    operationsCloseCalendar,
    brokeragePortfolio,
    workflowLibrary,
    workflowPresets,
    workflowSummary,
    featureCapabilities,
    workflowError,
    usingDevelopmentFixtures,
    loading,
    error,
    workspaceErrors,
    refreshStatus,
    portfolioRefreshStatus,
    refresh,
    refreshPortfolio,
    refreshProviderRouting,
    updateFeatureCapability,
    upsertWorkflowPreset
  } = useWorkstationData({
    activeWorkspace,
    workflowSummaryScope: {
      hasOperatingContext: hasOperatingScopeValues(operatingScopeInput),
      fundAccountId: operatingScopeInput.fundAccountId
    }
  });
  const handleWorkflowPresetUsed = (presetId: string) =>
    markWorkflowPresetUsed(presetId).then((preset) => {
      upsertWorkflowPreset(preset);
    });

  const screenActionItems = useCommandPaletteActions();
  const presetPinActionItems = useMemo<CommandPaletteActionItem[]>(
    () => (workflowPresets?.presets ?? []).map((preset) => ({
      id: `preset-pin:${preset.presetId}`,
      verbLabel: preset.isPinned ? `Unpin preset ${preset.name}` : `Pin preset ${preset.name}`,
      description: preset.isPinned
        ? `Remove ${preset.name} from the pinned presets shown first in the palette.`
        : `Keep ${preset.name} at the top of the palette preset list.`,
      keywords: ["preset", "pin", preset.workflowTitle],
      run: () => pinWorkflowPreset(preset.presetId, !preset.isPinned).then((updated) => {
        upsertWorkflowPreset(updated);
        return {
          title: updated.isPinned ? `Pinned ${updated.name}.` : `Unpinned ${updated.name}.`,
          tone: "success" as const
        };
      })
    })),
    [upsertWorkflowPreset, workflowPresets]
  );
  const scopeActionItem = useMemo<CommandPaletteActionItem>(
    () => ({
      id: "operating-scope-edit",
      verbLabel: "Set operating scope…",
      description: "Set the subject, fund account, and provider carried across the workstation.",
      keywords: ["scope", "fund account", "subject", "filter"],
      run: async () => {
        setScopePickerOpen(true);
      }
    }),
    []
  );
  const paletteActionItems = useMemo(
    () => [scopeActionItem, ...presetPinActionItems, ...screenActionItems],
    [scopeActionItem, presetPinActionItems, screenActionItems]
  );

  useEffect(() => {
    if (suppressScopePersistRef.current) {
      if (!hasOperatingScopeValues(routeOperatingScope)) {
        suppressScopePersistRef.current = false;
      }

      return;
    }

    if (!hasOperatingScopeValues(routeOperatingScope)) {
      return;
    }

    const nextScope = mergeOperatingScopes(storedOperatingScope, routeOperatingScope);
    if (operatingScopesEqual(nextScope, storedOperatingScope)) {
      return;
    }

    writeStoredOperatingScope(nextScope);
    setStoredOperatingScope(nextScope);
  }, [routeOperatingScope, storedOperatingScope]);

  const handleClearOperatingContext = () => {
    const emptyScope: AppShellOperatingScopeInput = {};
    suppressScopePersistRef.current = true;
    writeStoredOperatingScope(emptyScope);
    setStoredOperatingScope(emptyScope);
    navigate(`${pathname}${removeOperatingScopeFromSearch(search)}${hash}`, { replace: true });
  };

  // Replace the operating scope from the picker: persist it and reflect the
  // applicable dimensions into the current route so URL-reading screens filter,
  // preserving any non-scope query params already on the route.
  const handleSetOperatingScope = (next: AppShellOperatingScopeInput) => {
    const compacted = compactOperatingScope(next);
    suppressScopePersistRef.current = false;
    writeStoredOperatingScope(compacted);
    setStoredOperatingScope(compacted);
    const baseRoute = `${pathname}${removeOperatingScopeFromSearch(search)}`;
    const scopeState = buildOperatingScopeFromSearch("", compacted);
    navigate(`${appendOperatingScopeToRoute(baseRoute, scopeState)}${hash}`, { replace: true });
  };

  // Replay a saved layout: apply density and operating scope, re-open the recorded
  // companion panes, then navigate the captured route. Window placement across
  // monitors is browser-permission-limited, so a restored pop-out may open where the
  // browser chooses and be dragged once — the arrangement otherwise restores whole.
  const handleRestoreLayout = (plan: LayoutRestorePlan) => {
    writeStoredDensity(plan.density);
    applyDensity(plan.density);

    const compacted = compactOperatingScope(plan.operatingScope);
    suppressScopePersistRef.current = false;
    writeStoredOperatingScope(compacted);
    setStoredOperatingScope(compacted);

    setOpenCompanionPaneIds(plan.panes);
    plan.panes.forEach((paneId) => openCompanionPane(paneId));

    navigate(plan.route);
  };

  const scopeFundAccountOptions = useMemo(() => collectScopeFundAccounts(brokeragePortfolio), [brokeragePortfolio]);
  const scopeDimensionsInEffect = useMemo(() => operatingScopeDimensionsForRoute(pathname), [pathname]);

  useEffect(() => {
    const handleCommandShortcut = (event: KeyboardEvent) => {
      const command = resolveAppShellCommandPaletteShortcut({
        key: event.key,
        ctrlKey: event.ctrlKey,
        metaKey: event.metaKey,
        altKey: event.altKey,
        shiftKey: event.shiftKey,
        targetIsEditable: isAppShellEditableShortcutTarget(event.target),
        commandPaletteOpen: commandOpen
      });
      if (command !== "toggle-command-palette") {
        return;
      }

      event.preventDefault();
      setCommandOpen((current) => !current);
    };

    window.addEventListener("keydown", handleCommandShortcut);
    return () => window.removeEventListener("keydown", handleCommandShortcut);
  }, [commandOpen, setCommandOpen]);

  // Tell any open companion panes when the main workstation window goes away, so
  // they can surface that their source is gone rather than silently going stale.
  useEffect(() => {
    const handlePageHide = () => broadcastCompanionState({ type: "session-expired" });
    window.addEventListener("pagehide", handlePageHide);
    return () => window.removeEventListener("pagehide", handlePageHide);
  }, []);

  const shell = useMemo(() => buildAppShellViewState({
    pathname,
    search,
    hash,
    operatingContextSymbol,
    commandPaletteOpen: commandOpen,
    loading,
    error,
    workflowError,
    workspaceErrors,
    usingDevelopmentFixtures,
    payload: {
      session,
      overview,
      strategy,
      trading,
      portfolio,
      data,
      accounting,
      reporting,
      workflowSummary
    },
    operatingContextScope: operatingScopeInput
  }), [
    pathname,
    search,
    hash,
    operatingContextSymbol,
    commandOpen,
    loading,
    error,
    workflowError,
    workspaceErrors,
    usingDevelopmentFixtures,
    session,
    overview,
    strategy,
    trading,
    portfolio,
    data,
    accounting,
    reporting,
    workflowSummary,
    operatingScopeInput
  ]);
  const breadcrumbItems = useMemo(
    () => buildWorkspaceBreadcrumbItems(pathname, shell.activeWorkspace, navigate),
    [pathname, shell.activeWorkspace, navigate]
  );
  const headerWorkspace = useMemo(
    () => buildHeaderWorkspaceSummary(pathname, shell.activeWorkspace),
    [pathname, shell.activeWorkspace]
  );

  useEffect(() => {
    const previousRouteKey = previousRouteKeyRef.current;
    previousRouteKeyRef.current = shell.routeFocus.routeKey;
    const routeChanged = previousRouteKey !== shell.routeFocus.routeKey;
    const canFocusRequestedTarget = !shell.routeFocus.targetElementId || shell.canRenderRoutes;

    if (previousRouteKey === null && !shell.routeFocus.targetElementId) {
      document.title = shell.routeFocus.documentTitle;
      return;
    }

    if (!routeChanged && !canFocusRequestedTarget) {
      return;
    }

    if (!routeChanged && !shell.routeFocus.targetElementId) {
      return;
    }

    if (!canFocusRequestedTarget) {
      return;
    }

    setRouteAnnouncement(shell.routeFocus.announcement);
    document.title = shell.routeFocus.documentTitle;

    const cleanupFocusWatcher = focusRouteTargetWhenReady(
      workbenchRef.current,
      shell.routeFocus.targetElementId,
      shell.routeFocus.fallbackElementId
    );
    return () => {
      cleanupFocusWatcher();
    };
  }, [
    shell.canRenderRoutes,
    shell.routeFocus.announcement,
    shell.routeFocus.documentTitle,
    shell.routeFocus.fallbackElementId,
    shell.routeFocus.routeKey,
    shell.routeFocus.targetElementId
  ]);

  return (
    <div className="workstation-frame">
      <a className="skip-link" href="#workbench-content">Skip to workbench</a>
      <div className="sr-only" role="status" aria-live="polite" aria-atomic="true">
        {routeAnnouncement}
      </div>
      <header className="workstation-masthead">
        <div className="workstation-brand-group">
          <button
            type="button"
            className="workstation-nav-toggle focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
            aria-label="Open workspace navigation"
            aria-expanded={navOpen}
            aria-haspopup="dialog"
            onClick={() => setNavOpen(true)}
          >
            <Menu className="h-4 w-4" aria-hidden="true" />
          </button>
          <div className="workstation-brand">
            <img src={meridianMarkUrl} alt="" aria-hidden="true" />
            <div className="workstation-brand-copy min-w-0">
              <div className="name">Meridian</div>
              <div className="sub" aria-hidden="true">
                <span className="workstation-brand-sep">/</span>
                {headerWorkspace.label}
              </div>
            </div>
          </div>
        </div>

        <button
          type="button"
          className="workstation-search focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
          onClick={() => setCommandOpen(true)}
          aria-label={shell.commandPaletteTrigger.label}
          aria-controls={shell.commandPaletteTrigger.controlsId}
          aria-expanded={shell.commandPaletteTrigger.expanded}
          aria-haspopup={shell.commandPaletteTrigger.hasPopup}
        >
          <Search className="h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
          <span className="workstation-search-placeholder">{shell.commandPaletteTrigger.placeholder}</span>
          <span className="workstation-search-kbd" aria-hidden="true">{shell.commandPaletteTrigger.shortcutLabel}</span>
        </button>

        <WorkstationTrustStrip viewModel={shell.trustStrip} />

        <div className="workstation-actions">
          <OnboardingHeaderProgress controller={onboardingTour} />
          <ActivityCenter />
          <NotificationCenter overview={overview} fundAccountId={operatingScopeInput.fundAccountId} />
          {session ? (
            <div
              className="workstation-session-card"
              role="group"
              aria-label={`Current session: ${session.environment}, ${session.displayName}, ${session.role}`}
            >
              <Badge variant={session.environment} dot>{session.environment}</Badge>
              <span className="workstation-session-name">{session.displayName}</span>
              <span className="workstation-session-role text-muted-foreground">{session.role}</span>
            </div>
          ) : (
            <span className="text-xs text-muted-foreground">Loading session…</span>
          )}
        </div>
      </header>

      <div className="workstation-shell">
        <WorkspaceNav
          className="workstation-rail-desktop"
          density="compact"
          operatingContextScope={operatingScopeInput}
        />

        <main
          ref={workbenchRef}
          id="workbench-content"
          className="workbench grid grid-rows-[auto_auto_minmax(0,1fr)]"
          aria-label={`${headerWorkspace.label} workbench`}
          aria-busy={loading || refreshStatus.inFlight}
          tabIndex={-1}
        >
          <WorkspaceHeader
            actions={(
              <>
                <LayoutSwitcher
                  route={`${pathname}${search}`}
                  operatingScope={operatingScopeInput}
                  onRestore={handleRestoreLayout}
                />
                <CopyLinkButton />
                <SaveViewButton
                  workflowLibrary={workflowLibrary}
                  workflowError={workflowError}
                  onPresetSaved={upsertWorkflowPreset}
                />
              </>
            )}
            breadcrumbItems={breadcrumbItems}
            workspace={headerWorkspace}
            session={session}
            onRefresh={refresh}
            refreshing={loading || refreshStatus.inFlight}
          />
          <WorkflowContinuityDock
            viewModel={shell.workflowContinuity}
            onClearOperatingContext={handleClearOperatingContext}
            onEditOperatingContext={() => setScopePickerOpen(true)}
            scopeDimensionsInEffect={scopeDimensionsInEffect}
          />

          <div className="workbench-scroll px-4 py-4 lg:px-6 lg:py-5">
            {shell.statusPanel ? <ShellStatus panel={shell.statusPanel} onRetry={refresh} /> : null}
            {shell.canRenderRoutes ? (
              <RouteErrorBoundary routeKey={`${pathname}${search}${hash}`}>
                <Suspense fallback={<WorkspaceRouteFallback title={`Loading ${shell.activeWorkspace.label}`} />}>
                  <Routes>
                  <Route path="/" element={(
                    <DailyControlTowerScreen
                      viewModel={shell.workflowContinuity}
                      trustStrip={shell.trustStrip}
                    />
                  )} />
                  <Route path="/trading/readiness" element={(
                    <OperatorReadinessConsole
                      strategy={strategy}
                      trading={trading}
                      data={data}
                      accounting={accounting}
                      reporting={reporting}
                      fundAccountId={operatingScopeInput.fundAccountId}
                    />
                  )} />
                  <Route path="/trading/*" element={<TradingScreen data={trading} fundAccountId={operatingScopeInput.fundAccountId} />} />
                  <Route path="/portfolio/family-office" element={<FamilyOfficeScreen />} />
                  <Route path="/portfolio/cash-ladder" element={<CashLadderScreen fundAccountId={operatingScopeInput.fundAccountId ?? undefined} />} />
                  <Route path="/portfolio/asset-detail" element={<AssetDetailScreen />} />
                  <Route path="/portfolio/*" element={(
                    <PortfolioScreen
                      portfolio={portfolio}
                      trading={trading}
                      strategy={strategy}
                      accounting={accounting}
                      brokerageConnection={brokerageConnection}
                      brokeragePortfolio={brokeragePortfolio}
                      multiAssetCoverage={portfolioMultiAssetCoverage}
                      refreshStatus={portfolioRefreshStatus}
                    />
                  )} />
                  <Route path="/accounting/operations-continuity" element={<OperationsContinuityScreen />} />
                  <Route path="/accounting/entity-setup" element={<EntitySetupWizard />} />
                  <Route path="/accounting/ledger" element={<LedgerExplorerScreen data={accounting} />} />
                  <Route path="/accounting/trial-balance" element={<TrialBalanceScreen data={accounting} />} />
                  <Route path="/accounting/accounts/detail" element={<AccountDetailScreen data={accounting} />} />
                  <Route path="/accounting/journal-entries/detail" element={<JournalEntryDetailScreen />} />
                  <Route path="/accounting/reconciliation/match" element={<ReconciliationMatchWorkbenchScreen data={accounting} />} />
                  <Route path="/accounting/statement-import" element={<StatementImportScreen />} />
                  <Route path="/accounting/close-calendar" element={<CloseCalendarScreen data={accounting} />} />
                  <Route path="/accounting/approvals/inbox" element={<ApprovalInboxScreen data={accounting} />} />
                  <Route path="/accounting/security-master/detail" element={<AssetDetailScreen />} />
                  <Route path="/accounting/evidence/detail" element={<EvidenceDetailScreen />} />
                  <Route path="/accounting/evidence" element={<EvidenceWorkbenchScreen />} />
                  <Route path="/accounting/*" element={<AccountingScreen data={accounting} multiAssetCoverage={portfolioMultiAssetCoverage} />} />
                  <Route path="/reporting/operations-record" element={<OperationsRecordReleaseScreen data={data} reporting={reporting} />} />
                  <Route path="/reporting/library" element={<ReportLibraryScreen data={reporting} />} />
                  <Route path="/reporting/run" element={<ReportRunParametersScreen data={reporting} accounting={accounting} />} />
                  <Route path="/reporting/preview" element={<ReportPreviewValidationScreen data={reporting} />} />
                  <Route path="/reporting/runs/detail" element={<ReportRunDetailScreen data={reporting} />} />
                  <Route path="/reporting/evidence" element={<EvidenceWorkbenchScreen />} />
                  <Route path="/reporting/*" element={<ReportingScreen data={reporting} onRefreshLivePortfolioViews={refreshPortfolio} />} />
                  <Route path="/strategy/covered-call" element={<CoveredCallScreen />} />
                  <Route path="/strategy/designer" element={<StrategyDesignerScreen />} />
                  <Route path="/strategy/formula-workbench" element={<StrategyFormulaWorkbenchScreen />} />
                  <Route path="/strategy/quant-lab" element={<QuantLabScreen />} />
                  <Route path="/strategy/*" element={<StrategyScreen data={strategy} />} />
                  <Route path="/data/quotes" element={<LiveQuotesScreen />} />
                  <Route path="/data/watchlist" element={<WatchlistScreen />} />
                  <Route path="/data/alerts" element={<PriceAlertsScreen />} />
                  <Route path="/data/evidence" element={<EvidenceWorkbenchScreen />} />
                  <Route path="/data/security-master" element={<LegacyWorkspaceRedirect />} />
                  <Route path="/data/security-master/*" element={<LegacyWorkspaceRedirect />} />
                  <Route path="/data/*" element={(
                    <DataScreen
                      data={data}
                      providerConnections={providerConnections}
                      providerReadiness={providerReadiness}
                      providerRoutingConnections={providerRoutingConnections}
                      providerRoutingBindings={providerRoutingBindings}
                      providerRoutingTrustSnapshots={providerRoutingTrustSnapshots}
                      providerRoutingRefreshing={providerRoutingRefreshing}
                      onProviderSetupConfigured={refreshProviderRouting}
                      onProviderRoutingRefresh={refreshProviderRouting}
                    />
                  )} />
                  <Route path="/settings/*" element={(
                    <SettingsScreen
                      session={session}
                      overview={overview}
                      strategy={strategy}
                      trading={trading}
                      portfolio={portfolio}
                      data={data}
                      accounting={accounting}
                      reporting={reporting}
                      brokerageConnection={brokerageConnection}
                      robinhoodConnection={robinhoodConnection}
                      providerConnections={providerConnections}
                      providerRoutingConnections={providerRoutingConnections}
                      providerRoutingBindings={providerRoutingBindings}
                      providerRoutingTrustSnapshots={providerRoutingTrustSnapshots}
                      providerRoutingRefreshing={providerRoutingRefreshing}
                      featureCapabilities={featureCapabilities}
                      rolePermissionCatalog={rolePermissionCatalog}
                      securityAssetProfiles={securityAssetProfiles}
                      ledgerMappingWorkbench={ledgerMappingWorkbench}
                      operationsApprovalPolicyMatrix={operationsApprovalPolicyMatrix}
                      operationsCloseCalendar={operationsCloseCalendar}
                      onFeatureCapabilityToggle={updateFeatureCapability}
                      onRefresh={refresh}
                      onProviderRoutingRefresh={refreshProviderRouting}
                      loading={loading}
                      error={error}
                      workspaceErrors={workspaceErrors}
                    />
                  )} />
                  <Route path="/overview/*" element={<LegacyWorkspaceRedirect />} />
                  <Route path="/research/*" element={<LegacyWorkspaceRedirect />} />
                  <Route path="/data-operations/*" element={<LegacyWorkspaceRedirect />} />
                  <Route path="/governance/*" element={<LegacyWorkspaceRedirect />} />
                    <Route path="*" element={<NotFoundScreen />} />
                  </Routes>
                </Suspense>
              </RouteErrorBoundary>
            ) : null}
          </div>
        </main>
      </div>

      <StatusBar
        items={buildShellStatusItems({
          session,
          workspaceLabel: headerWorkspace.label,
          usingDevelopmentFixtures,
          refreshing: loading || refreshStatus.inFlight,
          hasError: Boolean(error)
        })}
      />

      <CommandPalette
        open={commandOpen}
        onOpenChange={setCommandOpen}
        workflowLibrary={workflowLibrary}
        workflowPresets={workflowPresets}
        workflowError={workflowError}
        operatorFocusItems={shell.workflowContinuity.operatorFocusCommandItems}
        actionItems={paletteActionItems}
        operatingContextSymbol={operatingContextSymbol}
        operatingScope={operatingScopeInput}
        onPresetUsed={handleWorkflowPresetUsed}
      />
      <OnboardingCoachMark controller={onboardingTour} />
      <ScopePicker
        open={scopePickerOpen}
        onOpenChange={setScopePickerOpen}
        scope={operatingScopeInput}
        fundAccounts={scopeFundAccountOptions}
        onApply={handleSetOperatingScope}
      />
      <Sheet open={navOpen} onOpenChange={setNavOpen} side="left">
        <SheetContent
          side="left"
          aria-labelledby="workspace-navigation-title"
          aria-describedby="workspace-navigation-description"
          className="workstation-nav-sheet-dialog"
        >
          <SheetHeader>
            <SheetTitle id="workspace-navigation-title">Workspace navigation</SheetTitle>
            <SheetDescription id="workspace-navigation-description">
              Move between the seven operator workspaces without losing your current cockpit context.
            </SheetDescription>
            <SheetCloseButton onClick={() => setNavOpen(false)} label="Close workspace navigation" />
          </SheetHeader>
          <SheetBody className="p-0">
            <WorkspaceNav
              className="workstation-nav-sheet"
              operatingContextScope={operatingScopeInput}
              onNavigate={() => setNavOpen(false)}
            />
          </SheetBody>
        </SheetContent>
      </Sheet>
    </div>
  );
}

function resolveWorkspaceKeyFromPath(pathname: string): WorkspaceKey {
  const root = pathname.split("/").filter(Boolean)[0]?.toLowerCase();
  switch (root) {
    case "portfolio":
    case "accounting":
    case "reporting":
    case "strategy":
    case "data":
    case "settings":
      return root;
    case "trading":
    default:
      return "trading";
  }
}

function buildHeaderWorkspaceSummary(pathname: string, workspace: WorkspaceSummary): WorkspaceSummary {
  if (pathname !== "/") {
    return workspace;
  }

  return {
    ...workspace,
    label: "Daily Control Tower",
    description: "Ranked operator decisions across Trading, Portfolio, Accounting, Reporting, Strategy, Data, and Settings."
  };
}

function focusRouteTargetWhenReady(
  root: HTMLElement | null,
  targetElementId: string | null,
  fallbackElementId: string
): () => void {
  const requestedTargetId = targetElementId;
  if (!requestedTargetId) {
    focusElementById(fallbackElementId, root);
    return () => undefined;
  }

  if (focusElementById(requestedTargetId, root, false)) {
    return () => undefined;
  }

  let complete = false;
  let fallbackTimeout: number | null = null;
  let targetTimeout: number | null = null;
  let observer: MutationObserver | null = null;

  const cleanup = () => {
    complete = true;
    if (fallbackTimeout !== null) {
      window.clearTimeout(fallbackTimeout);
      fallbackTimeout = null;
    }
    if (targetTimeout !== null) {
      window.clearTimeout(targetTimeout);
      targetTimeout = null;
    }

    observer?.disconnect();
    observer = null;
  };

  const focusRequestedTarget = () => {
    if (complete || !focusElementById(requestedTargetId, root, false)) {
      return;
    }

    cleanup();
  };

  observer = new MutationObserver(focusRequestedTarget);
  observer.observe(root ?? document.body, { childList: true, subtree: true });

  fallbackTimeout = window.setTimeout(() => {
    if (complete) {
      return;
    }

    focusElementById(fallbackElementId, root);
    fallbackTimeout = null;
  }, 4000);

  targetTimeout = window.setTimeout(cleanup, 15000);

  return cleanup;
}

function buildWorkspaceBreadcrumbItems(
  pathname: string,
  workspace: WorkspaceSummary,
  navigate: ReturnType<typeof useNavigate>
): BreadcrumbItem[] {
  if (pathname === "/") {
    return [{ label: "Workstation", current: true }];
  }

  const routeLabel = resolveRouteBreadcrumbLabel(pathname, workspace);
  const workspaceIsCurrent = routeLabel === workspace.label;

  return [
    {
      label: "Workstation",
      onClick: () => navigate("/")
    },
    {
      label: workspace.label,
      current: workspaceIsCurrent,
      onClick: workspaceIsCurrent ? undefined : () => navigate(workspacePath(workspace.key))
    },
    ...(workspaceIsCurrent ? [] : [{ label: routeLabel, current: true }])
  ];
}

function resolveRouteBreadcrumbLabel(pathname: string, workspace: WorkspaceSummary): string {
  const segments = pathname.split("/").filter(Boolean);
  const routeSegments = segments[0] === workspace.key ? segments.slice(1) : segments.slice(2);
  if (routeSegments.length === 0) {
    return workspace.label;
  }

  return routeSegments.map(formatRouteSegmentLabel).join(" / ");
}

function formatRouteSegmentLabel(segment: string): string {
  const knownLabels: Record<string, string> = {
    alerts: "Alerts",
    approvals: "Close Cockpit",
    "asset-detail": "Asset Detail",
    "capital-accounts": "Capital Accounts",
    configure: "Governance",
    "covered-call": "Covered Call",
    designer: "Designer",
    "entity-setup": "Entity Setup",
    evidence: "Evidence",
    exceptions: "Reconciliation Casework",
    "family-office": "Family Office",
    "formula-workbench": "Formula Workbench",
    "journal-entries": "Journal Entry",
    ledger: "Ledger Explorer",
    "operations-continuity": "Operations Continuity",
    "operations-record": "Operations Record",
    exports: "Exports",
    providers: "Providers",
    "quant-lab": "Quant Lab",
    quotes: "Quotes",
    readiness: "Readiness",
    reconciliation: "Reconciliation Casework",
    "report-packs": "Delivery Evidence",
    run: "Run Report",
    "run-status": "Run Status",
    scheduled: "Scheduled Reports",
    "security-master": "Security Master",
    "statement-import": "Import Statement",
    watchlist: "Watchlist"
  };

  return knownLabels[segment] ?? segment.split("-").map((part) => (
    part.length > 0 ? `${part[0].toUpperCase()}${part.slice(1)}` : part
  )).join(" ");
}

class RouteErrorBoundary extends Component<
  { children: ReactNode; routeKey: string },
  { hasError: boolean; routeKey: string }
> {
  constructor(props: { children: ReactNode; routeKey: string }) {
    super(props);
    this.state = { hasError: false, routeKey: props.routeKey };
  }

  static getDerivedStateFromError(): { hasError: boolean } {
    return { hasError: true };
  }

  static getDerivedStateFromProps(
    props: { routeKey: string },
    state: { hasError: boolean; routeKey: string }
  ): { hasError: boolean; routeKey: string } | null {
    if (props.routeKey === state.routeKey) {
      return null;
    }

    return { hasError: false, routeKey: props.routeKey };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error("Meridian workstation route failed to render.", error, info.componentStack);
  }

  render() {
    if (this.state.hasError) {
      return (
        <RouteRecoveryPanel
          title="Workbench route failed"
          detail="Meridian could not render this route. Return to the Daily Control Tower or retry after refreshing live data."
          actionLabel="Open Daily Control Tower"
          actionHref="/"
        />
      );
    }

    return this.props.children;
  }
}

function NotFoundScreen() {
  return (
    <RouteRecoveryPanel
      title="Workbench route not found"
      detail="The requested workstation route is not available in this Meridian build."
      actionLabel="Open Daily Control Tower"
      actionHref="/"
    />
  );
}

function RouteRecoveryPanel({
  title,
  detail,
  actionLabel,
  actionHref
}: {
  title: string;
  detail: string;
  actionLabel: string;
  actionHref: string;
}) {
  return (
    <PanelSurface
      role="alert"
      aria-labelledby="route-recovery-title"
      aria-describedby="route-recovery-detail"
      className="flex flex-col gap-3 p-4 sm:flex-row sm:items-center sm:justify-between"
    >
      <StatusBanner
        tone="danger"
        title={<span id="route-recovery-title">{title}</span>}
        detail={<span id="route-recovery-detail">{detail}</span>}
        className="min-w-0 flex-1 shadow-none"
      />
      <Button asChild variant="default" size="sm" className="shrink-0">
        <Link to={actionHref}>
          <span>{actionLabel}</span>
          <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
        </Link>
      </Button>
    </PanelSurface>
  );
}

const OPERATING_CONTEXT_STORAGE_KEY = "meridian.workstation.operatingContext.v1";

function readStoredOperatingScope(): AppShellOperatingScopeInput {
  if (typeof window === "undefined") {
    return {};
  }

  try {
    const raw = window.localStorage.getItem(OPERATING_CONTEXT_STORAGE_KEY);
    if (!raw) {
      return {};
    }

    const parsed: unknown = raw.trim().startsWith("{") ? JSON.parse(raw) : raw;
    if (typeof parsed === "string") {
      return { symbol: parsed };
    }

    if (parsed && typeof parsed === "object") {
      const record = parsed as Record<string, unknown>;
      return {
        symbol: readStoredScopeString(record.symbol),
        fundAccountId: readStoredScopeString(record.fundAccountId),
        runId: readStoredScopeString(record.runId),
        provider: readStoredScopeString(record.provider),
        from: readStoredScopeString(record.from),
        to: readStoredScopeString(record.to),
        date: readStoredScopeString(record.date),
        asOf: readStoredScopeString(record.asOf)
      };
    }
  } catch {
    return {};
  }

  return {};
}

function writeStoredOperatingScope(scope: AppShellOperatingScopeInput) {
  if (typeof window === "undefined") {
    return;
  }

  try {
    const nextScope = compactOperatingScope(scope);
    if (!hasOperatingScopeValues(nextScope)) {
      window.localStorage.removeItem(OPERATING_CONTEXT_STORAGE_KEY);
      return;
    }

    window.localStorage.setItem(OPERATING_CONTEXT_STORAGE_KEY, JSON.stringify(nextScope));
  } catch {
    // Browser storage can be unavailable in private or locked-down contexts.
  }
}

function readStoredScopeString(value: unknown): string | null {
  return typeof value === "string" && value.trim().length > 0 ? value : null;
}

function mergeOperatingScopes(
  storedScope: AppShellOperatingScopeInput,
  routeScope: AppShellOperatingScopeInput
): AppShellOperatingScopeInput {
  return compactOperatingScope({
    symbol: routeScope.symbol ?? storedScope.symbol ?? null,
    fundAccountId: routeScope.fundAccountId ?? storedScope.fundAccountId ?? null,
    runId: routeScope.runId ?? storedScope.runId ?? null,
    provider: routeScope.provider ?? storedScope.provider ?? null,
    from: routeScope.from ?? storedScope.from ?? null,
    to: routeScope.to ?? storedScope.to ?? null,
    date: routeScope.date ?? storedScope.date ?? null,
    asOf: routeScope.asOf ?? storedScope.asOf ?? null
  });
}

function compactOperatingScope(scope: AppShellOperatingScopeInput): AppShellOperatingScopeInput {
  return {
    ...(scope.symbol ? { symbol: scope.symbol } : {}),
    ...(scope.fundAccountId ? { fundAccountId: scope.fundAccountId } : {}),
    ...(scope.runId ? { runId: scope.runId } : {}),
    ...(scope.provider ? { provider: scope.provider } : {}),
    ...(scope.from ? { from: scope.from } : {}),
    ...(scope.to ? { to: scope.to } : {}),
    ...(scope.date ? { date: scope.date } : {}),
    ...(scope.asOf ? { asOf: scope.asOf } : {})
  };
}

function hasOperatingScopeValues(scope: AppShellOperatingScopeInput): boolean {
  return Boolean(
    scope.symbol
    || scope.fundAccountId
    || scope.runId
    || scope.provider
    || scope.from
    || scope.to
    || scope.date
    || scope.asOf
  );
}

function operatingScopesEqual(left: AppShellOperatingScopeInput, right: AppShellOperatingScopeInput): boolean {
  const compactLeft = compactOperatingScope(left);
  const compactRight = compactOperatingScope(right);
  return JSON.stringify(compactLeft) === JSON.stringify(compactRight);
}

function WorkstationTrustStrip({
  viewModel
}: {
  viewModel: AppShellTrustStripState;
}) {
  return (
    <section className="workstation-trust-strip" aria-label={viewModel.ariaLabel}>
      {viewModel.items.map((item) => {
        const content = (
          <>
            <span className="workstation-trust-label">{item.label}</span>
            <span className="workstation-trust-value">{item.value}</span>
            <span className="sr-only">
              {item.detail}
              {item.actionLabel ? ` ${item.actionLabel}.` : ""}
            </span>
          </>
        );

        return item.href ? (
          <Link
            key={item.id}
            to={item.href}
            className={cn("workstation-trust-item", `workstation-trust-item-${item.tone}`)}
            aria-label={`${item.ariaLabel} ${item.actionLabel}.`}
          >
            {content}
          </Link>
        ) : (
          <span
            key={item.id}
            className={cn("workstation-trust-item", `workstation-trust-item-${item.tone}`)}
            aria-label={item.ariaLabel}
          >
            {content}
          </span>
        );
      })}
    </section>
  );
}

interface ShellStatusBarItem {
  key: string;
  label?: string;
  value: string;
  status?: "ok" | "warn" | "err";
  push?: boolean;
}

/**
 * Concrete workstation status bar — the near-black 28px telemetry footer that mirrors the
 * WPF StatusBar palette. Renders a row of label/value fields; items flagged `push` float to
 * the right edge. Purely presentational; all copy is derived by the caller.
 */
function StatusBar({ items }: { items: ShellStatusBarItem[] }) {
  return (
    <footer className="workstation-statusbar" aria-label="Workstation status">
      {items.map((item) => (
        <span
          key={item.key}
          className={cn("workstation-statusbar-item", item.push && "workstation-statusbar-item-push")}
        >
          {item.status ? (
            <span className={`workstation-statusbar-dot workstation-statusbar-dot-${item.status}`} aria-hidden="true" />
          ) : null}
          {item.label ? <span className="workstation-statusbar-label">{item.label}</span> : null}
          <span className="workstation-statusbar-value">{item.value}</span>
        </span>
      ))}
    </footer>
  );
}

function buildShellStatusItems({
  session,
  workspaceLabel,
  usingDevelopmentFixtures,
  refreshing,
  hasError
}: {
  session: { environment?: string } | null;
  workspaceLabel: string;
  usingDevelopmentFixtures: boolean;
  refreshing: boolean;
  hasError: boolean;
}): ShellStatusBarItem[] {
  const environment = session?.environment ?? "loading";
  const connectionStatus: ShellStatusBarItem["status"] = hasError ? "err" : session ? "ok" : "warn";
  const dataStatus: ShellStatusBarItem["status"] = usingDevelopmentFixtures ? "warn" : "ok";

  return [
    {
      key: "session",
      status: connectionStatus,
      label: "Session",
      value: session ? environment : "connecting"
    },
    {
      key: "data",
      status: dataStatus,
      label: "Data",
      value: usingDevelopmentFixtures ? "demo fixtures" : "live source"
    },
    {
      key: "sync",
      label: "Sync",
      value: refreshing ? "refreshing…" : "up to date"
    },
    {
      key: "workspace",
      label: "Workspace",
      value: workspaceLabel,
      push: true
    }
  ];
}

function LegacyWorkspaceRedirect() {
  const location = useLocation();
  return <Navigate to={legacyWorkspaceRedirect(location.pathname, location.search, location.hash) ?? "/"} replace />;
}

function WorkspaceRouteFallback({ title }: { title: string }) {
  return (
    <PanelSurface role="status" aria-live="polite" className="flex items-center gap-3 p-4">
      <Skeleton variant="circle" width={16} aria-hidden="true" />
      <div>
        <h2 className="text-sm font-semibold text-foreground">{title}</h2>
        <p className="mt-1 text-xs text-muted-foreground">Preparing the workstation route.</p>
      </div>
    </PanelSurface>
  );
}

function focusElementById(targetElementId: string, fallbackElement: HTMLElement | null, allowFallback = true): boolean {
  const target = document.getElementById(targetElementId) ?? (allowFallback ? fallbackElement : null);
  if (!target) {
    return false;
  }

  const previousTabIndex = target.getAttribute("tabindex");
  if (!target.hasAttribute("tabindex")) {
    target.tabIndex = -1;
  }

  if (typeof target.scrollIntoView === "function") {
    target.scrollIntoView({ block: "start", inline: "nearest" });
  }
  try {
    target.focus({ preventScroll: true });
  } catch {
    target.focus();
  }

  if (previousTabIndex === null) {
    target.addEventListener("blur", () => target.removeAttribute("tabindex"), { once: true });
  }

  return true;
}

function ShellStatus({ panel, onRetry }: { panel: ShellStatusPanel; onRetry: () => void }) {
  const Icon = panel.tone === "loading" ? LoaderCircle : AlertTriangle;
  const itemSummary = panel.items.length > 0
    ? `${panel.items.length} ${panel.items.length === 1 ? "detail" : "details"}`
    : null;

  return (
    <section
      id={panel.id}
      role={panel.role}
      aria-live={panel.ariaLive}
      aria-labelledby={panel.titleId}
      aria-describedby={panel.detailId}
      aria-busy={panel.tone === "loading"}
      className={cn(
        "shell-status-strip",
        `shell-status-strip-${panel.tone}`,
        panel.tone === "loading" && "startup-status-panel"
      )}
    >
      <div className="min-w-0">
        <StatusBanner
          tone={shellStatusToneToBannerTone(panel.tone)}
          title={(
            <span id={panel.titleId} className="inline-flex min-w-0 items-center gap-2">
              <Icon aria-hidden="true" className={`h-4 w-4 shrink-0 ${panel.tone === "loading" ? "animate-spin" : ""}`} />
              <span className="min-w-0 truncate">{panel.title}</span>
              {itemSummary ? (
                <span
                  aria-hidden="true"
                  className="rounded-sm border border-border/60 bg-background/45 px-1.5 py-0.5 font-mono text-[0.625rem] uppercase tracking-[0.12em] text-muted-foreground"
                >
                  {itemSummary}
                </span>
              ) : null}
            </span>
          )}
          detail={<span id={panel.detailId}>{panel.detail}</span>}
          className="shadow-none"
        />
        {panel.tone === "loading" ? (
          <div className="startup-status-meter mt-2" aria-hidden="true">
            <span />
          </div>
        ) : null}
        {panel.items.length > 0 ? (
          <ul aria-label={panel.itemListLabel} className="sr-only">
            {panel.items.map((item) => (
              <li key={item.key} aria-label={item.ariaLabel}>
                {item.label}: {item.detail}
              </li>
            ))}
          </ul>
        ) : null}
      </div>
      {panel.actionLabel || panel.secondaryActionHref ? (
        <div className="flex shrink-0 flex-wrap items-center gap-2">
          {panel.secondaryActionHref && panel.secondaryActionLabel ? (
            <Button asChild variant="outline" size="sm">
              <Link to={panel.secondaryActionHref} aria-label={panel.secondaryActionAriaLabel ?? panel.secondaryActionLabel}>
                {panel.secondaryActionLabel}
              </Link>
            </Button>
          ) : null}
          {panel.actionLabel ? (
            <Button
              variant="outline"
              size="sm"
              onClick={onRetry}
              aria-label={panel.actionAriaLabel ?? panel.actionLabel}
            >
              {panel.actionLabel}
            </Button>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}

function shellStatusToneToBannerTone(tone: ShellStatusPanel["tone"]): "danger" | "info" | "warning" {
  switch (tone) {
    case "danger":
      return "danger";
    case "warning":
      return "warning";
    case "loading":
    default:
      return "info";
  }
}
