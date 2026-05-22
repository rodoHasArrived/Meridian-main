import { Component, lazy, Suspense, useEffect, useRef, useState, type ErrorInfo, type ReactNode } from "react";
import {
  ArrowRight,
  AlertTriangle,
  Bell,
  ChevronDown,
  GitBranch,
  LoaderCircle,
  Menu,
  RefreshCcw,
  Search,
  X
} from "lucide-react";
import { Link, Navigate, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import meridianMarkUrl from "@/assets/brand/meridian-mark.svg";
import {
  buildAppShellViewState,
  buildDevelopmentFixtureNoticeViewModel,
  isAppShellEditableShortcutTarget,
  readOperatingScopeFromSearch,
  removeOperatingScopeFromSearch,
  resolveAppShellCommandPaletteShortcut,
  type AppShellWorkflowContinuityViewModel,
  type AppShellWorkflowContinuityDisclosurePanel,
  type AppShellOperatingScopeInput,
  type AppShellTrustStripState,
  type ShellStatusPanel
} from "@/app-shell.view-model";
import { CommandPalette } from "@/components/meridian/command-palette";
import { WorkspaceHeader } from "@/components/meridian/workspace-header";
import { WorkspaceNav } from "@/components/meridian/workspace-nav";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Sheet,
  SheetBody,
  SheetCloseButton,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle
} from "@/components/ui/sheet";
import { useWorkstationData } from "@/hooks/use-workstation-data";
import { markWorkflowPresetUsed } from "@/lib/api";
import { PriceAlertsProvider, usePriceAlerts } from "@/lib/price-alerts/service";
import { cn } from "@/lib/utils";
import { legacyWorkspaceRedirect } from "@/lib/workspace";

const DataOperationsScreen = lazy(() => import("@/screens/data-operations-screen").then((module) => ({ default: module.DataOperationsScreen })));
const EvidenceWorkbenchScreen = lazy(() => import("@/screens/evidence-workbench-screen").then((module) => ({ default: module.EvidenceWorkbenchScreen })));
const GovernanceScreen = lazy(() => import("@/screens/governance-screen").then((module) => ({ default: module.GovernanceScreen })));
const LiveQuotesScreen = lazy(() => import("@/screens/live-quotes-screen").then((module) => ({ default: module.LiveQuotesScreen })));
const OperatorReadinessConsole = lazy(() => import("@/screens/operator-readiness-console").then((module) => ({ default: module.OperatorReadinessConsole })));
const OperationsContinuityScreen = lazy(() => import("@/screens/operations-continuity-screen").then((module) => ({ default: module.OperationsContinuityScreen })));
const OverviewScreen = lazy(() => import("@/screens/overview-screen").then((module) => ({ default: module.OverviewScreen })));
const PortfolioScreen = lazy(() => import("@/screens/portfolio-screen").then((module) => ({ default: module.PortfolioScreen })));
const CoveredCallScreen = lazy(() => import("@/screens/covered-call-screen").then((module) => ({ default: module.CoveredCallScreen })));
const PriceAlertsScreen = lazy(() => import("@/screens/price-alerts-screen").then((module) => ({ default: module.PriceAlertsScreen })));
const QuantLabScreen = lazy(() => import("@/screens/quant-lab-screen").then((module) => ({ default: module.QuantLabScreen })));
const ReportingScreen = lazy(() => import("@/screens/reporting-screen").then((module) => ({ default: module.ReportingScreen })));
const ResearchScreen = lazy(() => import("@/screens/research-screen").then((module) => ({ default: module.ResearchScreen })));
const StrategyDesignerScreen = lazy(() => import("@/screens/strategy-designer-screen").then((module) => ({ default: module.StrategyDesignerScreen })));
const SettingsScreen = lazy(() => import("@/screens/settings-screen").then((module) => ({ default: module.SettingsScreen })));
const TradingScreen = lazy(() => import("@/screens/trading-screen").then((module) => ({ default: module.TradingScreen })));
const WatchlistScreen = lazy(() => import("@/screens/watchlist-screen").then((module) => ({ default: module.WatchlistScreen })));

export function App() {
  return (
    <PriceAlertsProvider>
      <AppShell />
    </PriceAlertsProvider>
  );
}

function AppShell() {
  const [commandOpen, setCommandOpen] = useState(false);
  const [navOpen, setNavOpen] = useState(false);
  const [routeAnnouncement, setRouteAnnouncement] = useState("");
  const [storedOperatingScope, setStoredOperatingScope] = useState(readStoredOperatingScope);
  const workbenchRef = useRef<HTMLElement | null>(null);
  const previousRouteKeyRef = useRef<string | null>(null);
  const suppressScopePersistRef = useRef(false);
  const navigate = useNavigate();
  const { hash, pathname, search } = useLocation();
  const routeOperatingScope = readOperatingScopeFromSearch(search);
  const operatingScopeInput = mergeOperatingScopes(storedOperatingScope, routeOperatingScope);
  const operatingContextSymbol = operatingScopeInput.symbol ?? null;
  const {
    session,
    overview,
    research,
    trading,
    portfolio,
    dataOperations,
    governance,
    reporting,
    brokerageConnection,
    providerConnections,
    providerRoutingConnections,
    providerRoutingBindings,
    providerRoutingTrustSnapshots,
    providerRoutingRefreshing,
    brokeragePortfolio,
    workflowLibrary,
    workflowPresets,
    workflowError,
    usingDevelopmentFixtures,
    loading,
    error,
    workspaceErrors,
    refresh,
    refreshProviderRouting,
    upsertWorkflowPreset
  } = useWorkstationData();
  const handleWorkflowPresetUsed = (presetId: string) =>
    markWorkflowPresetUsed(presetId).then((preset) => {
      upsertWorkflowPreset(preset);
    });

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

  const shell = buildAppShellViewState({
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
      research,
      trading,
      portfolio,
      dataOperations,
      governance,
      reporting
    },
    operatingContextScope: operatingScopeInput
  });

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
              <div className="sub">Operator workstation</div>
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
          <PriceAlertsBell />
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
          tabIndex={-1}
        >
          <WorkspaceHeader
            workspace={shell.activeWorkspace}
            session={session}
            onOpenCommandPalette={() => setCommandOpen(true)}
            onRefresh={refresh}
            refreshing={loading}
          />
          <WorkflowContinuityDock
            viewModel={shell.workflowContinuity}
            onClearOperatingContext={handleClearOperatingContext}
          />

          <div className="workbench-scroll px-4 py-4 lg:px-6 lg:py-5">
            {usingDevelopmentFixtures ? <DevelopmentFixtureNotice refreshing={loading} onRetry={refresh} /> : null}
            {shell.statusPanel ? <ShellStatus panel={shell.statusPanel} onRetry={refresh} /> : null}
            {shell.canRenderRoutes ? (
              <RouteErrorBoundary routeKey={`${pathname}${search}${hash}`}>
                <Suspense fallback={<WorkspaceRouteFallback title={`Loading ${shell.activeWorkspace.label}`} />}>
                  <Routes>
                  <Route path="/" element={<OverviewScreen data={overview} session={session} trading={trading} portfolio={portfolio} />} />
                  <Route path="/trading/readiness" element={(
                    <OperatorReadinessConsole
                      research={research}
                      trading={trading}
                      dataOperations={dataOperations}
                      governance={governance}
                      reporting={reporting}
                    />
                  )} />
                  <Route path="/trading/*" element={<TradingScreen data={trading} />} />
                  <Route path="/portfolio/*" element={(
                    <PortfolioScreen
                      portfolio={portfolio}
                      trading={trading}
                      research={research}
                      governance={governance}
                      brokerageConnection={brokerageConnection}
                      brokeragePortfolio={brokeragePortfolio}
                    />
                  )} />
                  <Route path="/accounting/operations-continuity" element={<OperationsContinuityScreen />} />
                  <Route path="/accounting/*" element={<GovernanceScreen data={governance} />} />
                  <Route path="/reporting/evidence" element={<EvidenceWorkbenchScreen />} />
                  <Route path="/reporting/*" element={<ReportingScreen data={reporting} />} />
                  <Route path="/strategy/covered-call" element={<CoveredCallScreen />} />
                  <Route path="/strategy/designer" element={<StrategyDesignerScreen />} />
                  <Route path="/strategy/quant-lab" element={<QuantLabScreen />} />
                  <Route path="/strategy/*" element={<ResearchScreen data={research} />} />
                  <Route path="/data/quotes" element={<LiveQuotesScreen />} />
                  <Route path="/data/watchlist" element={<WatchlistScreen />} />
                  <Route path="/data/alerts" element={<PriceAlertsScreen />} />
                  <Route path="/data/security-master" element={<LegacyWorkspaceRedirect />} />
                  <Route path="/data/security-master/*" element={<LegacyWorkspaceRedirect />} />
                  <Route path="/data/*" element={(
                    <DataOperationsScreen
                      data={dataOperations}
                      providerConnections={providerConnections}
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
                      research={research}
                      trading={trading}
                      portfolio={portfolio}
                      dataOperations={dataOperations}
                      governance={governance}
                      reporting={reporting}
                      brokerageConnection={brokerageConnection}
                      providerConnections={providerConnections}
                      providerRoutingConnections={providerRoutingConnections}
                      providerRoutingBindings={providerRoutingBindings}
                      providerRoutingTrustSnapshots={providerRoutingTrustSnapshots}
                      providerRoutingRefreshing={providerRoutingRefreshing}
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

      <CommandPalette
        open={commandOpen}
        onOpenChange={setCommandOpen}
        workflowLibrary={workflowLibrary}
        workflowPresets={workflowPresets}
        workflowError={workflowError}
        operatorFocusItems={shell.workflowContinuity.operatorFocusCommandItems}
        operatingContextSymbol={operatingContextSymbol}
        operatingScope={operatingScopeInput}
        onPresetUsed={handleWorkflowPresetUsed}
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

function WorkflowContinuityDock({
  viewModel,
  onClearOperatingContext
}: {
  viewModel: AppShellWorkflowContinuityViewModel;
  onClearOperatingContext?: () => void;
}) {
  const visibleSteps = viewModel.steps.filter((step) => step.active || step.next);
  const dockSteps = visibleSteps.length > 0 ? visibleSteps : viewModel.steps.slice(0, 2);

  return (
    <section className="workflow-continuity-dock" aria-label={viewModel.ariaLabel}>
      <div className="workflow-continuity-context">
        <div className="flex min-w-0 items-center gap-2">
          <GitBranch className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
          <div className="min-w-0">
            <div className="eyebrow-label">{viewModel.contextLabel}</div>
            <h2 className="workflow-continuity-title">{viewModel.title}</h2>
          </div>
        </div>
        <p className="workflow-continuity-summary">{viewModel.summary}</p>
        <div className="workflow-continuity-meta" aria-label={`Current route ${viewModel.routeLabel}`}>
          <span>{viewModel.contextValue}</span>
          <span>{viewModel.routeLabel}</span>
          {viewModel.operatingScope.hasScope && viewModel.clearSubjectAriaLabel && onClearOperatingContext ? (
            <button
              type="button"
              className="workflow-continuity-clear focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
              onClick={onClearOperatingContext}
              aria-label={viewModel.clearSubjectAriaLabel}
              title={viewModel.clearSubjectAriaLabel}
            >
              <X className="h-3.5 w-3.5" aria-hidden="true" />
            </button>
          ) : null}
        </div>
        {viewModel.operatingScope.items.length > 0 ? (
          <dl className="workflow-continuity-scope" aria-label={viewModel.operatingScope.label}>
            {viewModel.operatingScope.items.map((item) => (
              <div key={item.id} className="workflow-continuity-scope-chip" aria-label={item.ariaLabel}>
                <dt>{item.label}</dt>
                <dd>{item.value}</dd>
              </div>
            ))}
          </dl>
        ) : null}
      </div>

      <DecisionBriefPanel decision={viewModel.decisionBrief} />

      <nav className="workflow-continuity-steps" aria-label={viewModel.stepsLabel}>
        {dockSteps.map((step) => (
          <Link
            key={step.id}
            to={step.href}
            aria-label={step.ariaLabel}
            aria-current={step.active ? "step" : undefined}
            className={cn(
              "workflow-continuity-step focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
              `workflow-continuity-step-${step.statusTone}`,
              step.active && "workflow-continuity-step-active",
              step.next && "workflow-continuity-step-next"
            )}
          >
            <span className="workflow-continuity-step-label">{step.label}</span>
            <span className="workflow-continuity-step-description">{step.description}</span>
            <span className="workflow-continuity-step-status">{step.statusLabel}</span>
          </Link>
        ))}
      </nav>

      <Button asChild variant="secondary" size="sm" className="workflow-continuity-next">
        <Link to={viewModel.nextActionHref} aria-label={viewModel.nextActionAriaLabel}>
          <span>{viewModel.nextActionLabel}</span>
          <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
        </Link>
      </Button>

      <div
        className="workflow-continuity-support"
        role="group"
        aria-label={viewModel.disclosure.label}
      >
        <p className="workflow-continuity-support-summary">{viewModel.disclosure.summary}</p>
        {viewModel.disclosure.panels.map((panel) => (
          <WorkflowContinuityDisclosurePanel
            key={panel.id}
            panel={panel}
            viewModel={viewModel}
          />
        ))}
      </div>
    </section>
  );
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
          detail="Meridian could not render this route. Return to Trading or retry after refreshing live data."
          actionLabel="Open Trading"
          actionHref="/trading"
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
      actionLabel="Open Trading"
      actionHref="/trading"
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
    <section
      role="alert"
      aria-labelledby="route-recovery-title"
      aria-describedby="route-recovery-detail"
      className="panel-surface flex flex-col gap-3 p-4 sm:flex-row sm:items-center sm:justify-between"
    >
      <div>
        <span className="eyebrow-label">Route recovery</span>
        <h2 id="route-recovery-title" className="mt-1 text-base font-semibold text-foreground">{title}</h2>
        <p id="route-recovery-detail" className="mt-1 text-sm text-muted-foreground">{detail}</p>
      </div>
      <Button asChild variant="default" size="sm" className="shrink-0">
        <Link to={actionHref}>
          <span>{actionLabel}</span>
          <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
        </Link>
      </Button>
    </section>
  );
}

function WorkflowContinuityDisclosurePanel({
  panel,
  viewModel
}: {
  panel: AppShellWorkflowContinuityDisclosurePanel;
  viewModel: AppShellWorkflowContinuityViewModel;
}) {
  return (
    <details className="workflow-continuity-disclosure" open={panel.defaultExpanded}>
      <summary aria-label={panel.ariaLabel}>
        <span className="workflow-continuity-disclosure-copy">
          <span>{panel.label}</span>
          <span>{panel.summary}</span>
        </span>
        <ChevronDown className="workflow-continuity-disclosure-icon h-3.5 w-3.5" aria-hidden="true" />
      </summary>
      <div className="workflow-continuity-disclosure-body">
        {panel.id === "linked-context" ? (
          <LinkedContextPanel viewModel={viewModel} />
        ) : panel.id === "operator-focus" ? (
          <OperatorFocusPanel viewModel={viewModel} />
        ) : (
          <EvidenceTimelinePanel viewModel={viewModel} />
        )}
      </div>
    </details>
  );
}

function DecisionBriefPanel({
  decision
}: {
  decision: AppShellWorkflowContinuityViewModel["decisionBrief"];
}) {
  return (
    <section
      className={cn("workflow-continuity-decision", `workflow-continuity-decision-${decision.statusTone}`)}
      aria-label={decision.label}
    >
      <div className="workflow-continuity-decision-copy">
        <div className="workflow-continuity-decision-head">
          <span className="eyebrow-label">{decision.label}</span>
          <span className="workflow-continuity-decision-status">{decision.statusLabel}</span>
        </div>
        <h3>{decision.title}</h3>
        <p>{decision.summary}</p>
        <div className="workflow-continuity-decision-reason">
          <span>{decision.reasonLabel}</span>
          <span>{decision.reason}</span>
        </div>
        {decision.evidenceLabel ? (
          <span className="workflow-continuity-decision-evidence">{decision.evidenceLabel}</span>
        ) : null}
      </div>
      <Button asChild variant="default" size="sm" className="workflow-continuity-decision-action">
        <Link to={decision.actionHref} aria-label={decision.actionAriaLabel}>
          <span>{decision.actionLabel}</span>
          <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
        </Link>
      </Button>
    </section>
  );
}

function OperatorFocusPanel({
  viewModel
}: {
  viewModel: AppShellWorkflowContinuityViewModel;
}) {
  return (
    <section className="workflow-continuity-focus" aria-label={viewModel.operatorFocusLabel}>
      <div className="workflow-continuity-focus-head">
        <span className="eyebrow-label">{viewModel.operatorFocusLabel}</span>
        <p className="workflow-continuity-focus-summary">{viewModel.operatorFocusSummary}</p>
      </div>
      {viewModel.operatorFocusItems.length > 0 ? (
        <ul className="workflow-continuity-focus-list" aria-label={viewModel.operatorFocusItemsLabel}>
          {viewModel.operatorFocusItems.map((item) => (
            <li key={item.id}>
              <Link
                to={item.route}
                aria-label={item.ariaLabel}
                className={cn(
                  "workflow-continuity-focus-item focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                  `workflow-continuity-focus-item-${item.tone}`
                )}
              >
                <span className="workflow-continuity-focus-route">{item.workspaceLabel}</span>
                <span className="workflow-continuity-focus-copy">
                  <span className="workflow-continuity-focus-title">{item.label}</span>
                  <span className="workflow-continuity-focus-detail">{item.detail}</span>
                  <span className="workflow-continuity-focus-action">{item.actionLabel}</span>
                </span>
              </Link>
            </li>
          ))}
        </ul>
      ) : (
        <p className="workflow-continuity-focus-empty">{viewModel.operatorFocusEmptyText}</p>
      )}
      {viewModel.operatorFocusOverflowLabel ? (
        <span className="workflow-continuity-focus-overflow">{viewModel.operatorFocusOverflowLabel}</span>
      ) : null}
    </section>
  );
}

function LinkedContextPanel({
  viewModel
}: {
  viewModel: AppShellWorkflowContinuityViewModel;
}) {
  return (
    <section className="workflow-continuity-linked" aria-label={viewModel.linkedContextLabel}>
      <div className="workflow-continuity-linked-head">
        <span className="eyebrow-label">{viewModel.linkedContextLabel}</span>
        <p className="workflow-continuity-linked-summary">{viewModel.linkedContextSummary}</p>
        <span className={cn(
          "workflow-continuity-linked-posture",
          `workflow-continuity-linked-posture-${viewModel.linkedContextPostureTone}`
        )}>
          {viewModel.linkedContextPostureLabel}
        </span>
        {viewModel.linkedContextPrimaryActionHref && viewModel.linkedContextPrimaryActionLabel ? (
          <Button asChild variant="outline" size="sm" className="workflow-continuity-linked-primary">
            <Link
              to={viewModel.linkedContextPrimaryActionHref}
              aria-label={viewModel.linkedContextPrimaryActionAriaLabel ?? viewModel.linkedContextPrimaryActionLabel}
            >
              <span>{viewModel.linkedContextPrimaryActionLabel}</span>
              <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
            </Link>
          </Button>
        ) : null}
      </div>
      {viewModel.linkedContextItems.length > 0 ? (
        <ul className="workflow-continuity-linked-list" aria-label={viewModel.linkedContextItemsLabel}>
          {viewModel.linkedContextItems.map((item) => (
            <li key={item.id}>
              <Link
                to={item.route}
                aria-label={item.ariaLabel}
                className={cn(
                  "workflow-continuity-linked-item focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                  `workflow-continuity-linked-item-${item.tone}`
                )}
              >
                <span className="workflow-continuity-linked-route">{item.workspaceLabel}</span>
                <span className="workflow-continuity-linked-copy">
                  <span>{item.label}</span>
                  <span>{item.detail}</span>
                </span>
                <span className="workflow-continuity-linked-status">{item.statusLabel}</span>
              </Link>
            </li>
          ))}
        </ul>
      ) : (
        <p className="workflow-continuity-linked-empty">{viewModel.linkedContextEmptyText}</p>
      )}
    </section>
  );
}

function EvidenceTimelinePanel({
  viewModel
}: {
  viewModel: AppShellWorkflowContinuityViewModel;
}) {
  return (
    <section className="workflow-continuity-evidence" aria-label={viewModel.evidenceTimelineLabel}>
      <div className="workflow-continuity-evidence-head">
        <span className="eyebrow-label">{viewModel.evidenceTimelineLabel}</span>
        <p className="workflow-continuity-evidence-summary">{viewModel.evidenceTimelineSummary}</p>
      </div>
      {viewModel.evidenceTimelineItems.length > 0 ? (
        <ul className="workflow-continuity-evidence-list" aria-label={viewModel.evidenceTimelineItemsLabel}>
          {viewModel.evidenceTimelineItems.map((item) => (
            <li key={item.id}>
              <Link
                to={item.route}
                aria-label={item.ariaLabel}
                className={cn(
                  "workflow-continuity-evidence-item focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                  `workflow-continuity-evidence-item-${item.tone}`
                )}
              >
                <span className="workflow-continuity-evidence-route">{item.workspaceLabel}</span>
                <span className="workflow-continuity-evidence-copy">
                  <span>{item.label}</span>
                  <span>{item.detail}</span>
                </span>
                <time dateTime={item.timestampIso}>{item.timestampLabel}</time>
              </Link>
            </li>
          ))}
        </ul>
      ) : (
        <p className="workflow-continuity-evidence-empty">{viewModel.evidenceTimelineEmptyText}</p>
      )}
      {viewModel.evidenceTimelineOverflowLabel ? (
        <span className="workflow-continuity-evidence-overflow">{viewModel.evidenceTimelineOverflowLabel}</span>
      ) : null}
    </section>
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

function PriceAlertsBell() {
  const { unacknowledgedCount } = usePriceAlerts();
  const hasUnread = unacknowledgedCount > 0;
  if (!hasUnread) {
    return null;
  }

  const label = `${unacknowledgedCount} unacknowledged price alert${unacknowledgedCount === 1 ? "" : "s"}`;

  return (
    <Link
      to="/data/alerts"
      aria-label={label}
      title={label}
      className="relative inline-flex h-9 w-9 items-center justify-center rounded-md border border-border/60 bg-transparent text-muted-foreground transition-colors hover:bg-secondary/60 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
    >
      <Bell className="h-4 w-4" aria-hidden="true" />
      <span
        aria-hidden="true"
        className="absolute -right-1 -top-1 inline-flex h-4 min-w-4 items-center justify-center rounded-full border border-background bg-warning px-1 font-mono text-[10px] font-semibold leading-none text-background"
      >
        {unacknowledgedCount > 99 ? "99+" : unacknowledgedCount}
      </span>
    </Link>
  );
}

function LegacyWorkspaceRedirect() {
  const location = useLocation();
  return <Navigate to={legacyWorkspaceRedirect(location.pathname, location.search, location.hash) ?? "/trading"} replace />;
}

function WorkspaceRouteFallback({ title }: { title: string }) {
  return (
    <section role="status" aria-live="polite" className="panel-surface flex items-center gap-3 p-4">
      <LoaderCircle className="h-4 w-4 animate-spin text-primary" aria-hidden="true" />
      <div>
        <h2 className="text-sm font-semibold text-foreground">{title}</h2>
        <p className="mt-1 text-xs text-muted-foreground">Preparing the workstation route.</p>
      </div>
    </section>
  );
}

function DevelopmentFixtureNotice({
  refreshing,
  onRetry
}: {
  refreshing: boolean;
  onRetry: () => void | Promise<void>;
}) {
  const location = useLocation();
  const viewModel = buildDevelopmentFixtureNoticeViewModel({
    pathname: location.pathname,
    hash: location.hash,
    refreshing
  });

  return (
    <div
      role={viewModel.role}
      aria-live={viewModel.ariaLive}
      className="mb-3 flex flex-col gap-2 rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-foreground lg:flex-row lg:items-center lg:justify-between"
    >
      <div className="flex min-w-0 flex-wrap items-center gap-2">
        <span className="font-mono text-[0.6875rem] font-semibold uppercase tracking-[0.14em] text-warning">
          {viewModel.title}
        </span>
        <span className="text-foreground/80">
          {viewModel.detail}
        </span>
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={viewModel.retryDisabled}
          onClick={() => void onRetry()}
          aria-label={viewModel.retryAriaLabel}
          className="bg-background/40"
        >
          <RefreshCcw className={`h-3.5 w-3.5 ${viewModel.retryBusy ? "animate-spin" : ""}`} aria-hidden="true" />
          <span>{viewModel.retryLabel}</span>
        </Button>
      </div>
    </div>
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
  const toneClass =
    panel.tone === "danger"
      ? "border-danger/30 bg-danger/10"
      : panel.tone === "warning"
        ? "border-warning/30 bg-warning/10"
        : "border-primary/25 bg-secondary/25";
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
        "mb-3 flex flex-col gap-3 rounded-md border px-3 py-2 text-sm md:flex-row md:items-center md:justify-between",
        toneClass,
        panel.tone === "loading" && "startup-status-panel"
      )}
    >
      <div className="min-w-0">
        <div className="flex min-w-0 flex-wrap items-center gap-2">
          <span className="eyebrow-label">Shell status</span>
          <span id={panel.titleId} className="inline-flex min-w-0 items-center gap-2 font-semibold text-foreground">
            <Icon aria-hidden="true" className={`h-4 w-4 shrink-0 ${panel.tone === "loading" ? "animate-spin" : ""}`} />
            <span className="min-w-0 truncate">{panel.title}</span>
          </span>
          {itemSummary ? (
            <span className="rounded-sm border border-border/60 bg-background/45 px-1.5 py-0.5 font-mono text-[0.625rem] uppercase tracking-[0.12em] text-muted-foreground">
              {itemSummary}
            </span>
          ) : null}
        </div>
        <p id={panel.detailId} className="mt-1 min-w-0 truncate text-foreground/80">{panel.detail}</p>
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
