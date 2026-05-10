import { useEffect, useRef, useState } from "react";
import {
  AlertTriangle,
  Database,
  KeyRound,
  LineChart,
  LoaderCircle,
  Menu,
  RefreshCcw,
  Search,
  ShieldCheck,
  type LucideIcon
} from "lucide-react";
import { Link, Navigate, Route, Routes, useLocation } from "react-router-dom";
import meridianMarkUrl from "@/assets/brand/meridian-mark.svg";
import {
  buildAppShellViewState,
  buildDevelopmentFixtureNoticeViewModel,
  type DevelopmentFixtureNoticeStep,
  type ShellStatusPanel
} from "@/app-shell.view-model";
import { CommandPalette } from "@/components/meridian/command-palette";
import { MegaMenu } from "@/components/meridian/mega-menu";
import { WorkspaceHeader } from "@/components/meridian/workspace-header";
import { WorkspaceNav } from "@/components/meridian/workspace-nav";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
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
import { legacyWorkspaceRedirect } from "@/lib/workspace";
import { DataOperationsScreen } from "@/screens/data-operations-screen";
import { LiveQuotesScreen } from "@/screens/live-quotes-screen";
import { WatchlistScreen } from "@/screens/watchlist-screen";
import { EvidenceWorkbenchScreen } from "@/screens/evidence-workbench-screen";
import { GovernanceScreen } from "@/screens/governance-screen";
import { OperatorReadinessConsole } from "@/screens/operator-readiness-console";
import { OverviewScreen } from "@/screens/overview-screen";
import { PortfolioScreen } from "@/screens/portfolio-screen";
import { QuantLabScreen } from "@/screens/quant-lab-screen";
import { ReportingScreen } from "@/screens/reporting-screen";
import { ResearchScreen } from "@/screens/research-screen";
import { SettingsScreen } from "@/screens/settings-screen";
import { TradingScreen } from "@/screens/trading-screen";

export function App() {
  const [commandOpen, setCommandOpen] = useState(false);
  const [navOpen, setNavOpen] = useState(false);
  const [routeAnnouncement, setRouteAnnouncement] = useState("");
  const workbenchRef = useRef<HTMLElement | null>(null);
  const previousRouteKeyRef = useRef<string | null>(null);
  const { hash, pathname } = useLocation();
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
    brokeragePortfolio,
    workflowLibrary,
    workflowPresets,
    workflowError,
    usingDevelopmentFixtures,
    loading,
    error,
    workspaceErrors,
    refresh,
    upsertWorkflowPreset
  } = useWorkstationData();
  const handleWorkflowPresetUsed = (presetId: string) =>
    markWorkflowPresetUsed(presetId).then((preset) => {
      upsertWorkflowPreset(preset);
    });

  useEffect(() => {
    const handleCommandShortcut = (event: KeyboardEvent) => {
      if (!isCommandPaletteShortcut(event)) {
        return;
      }

      if (isEditableTarget(event.target) && !commandOpen) {
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
    hash,
    loading,
    error,
    workspaceErrors,
    payload: {
      session,
      overview,
      research,
      trading,
      portfolio,
      dataOperations,
      governance,
      reporting
    }
  });

  useEffect(() => {
    const previousRouteKey = previousRouteKeyRef.current;
    previousRouteKeyRef.current = shell.routeFocus.routeKey;
    const routeChanged = previousRouteKey !== shell.routeFocus.routeKey;
    const canFocusRequestedTarget = !shell.routeFocus.targetElementId || shell.canRenderRoutes;

    if (previousRouteKey === null && !shell.routeFocus.targetElementId) {
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

    const focusRouteTarget = () => {
      focusElementById(shell.routeFocus.targetElementId ?? shell.routeFocus.fallbackElementId, workbenchRef.current);
    };
    focusRouteTarget();
    if (typeof window.requestAnimationFrame === "function") {
      const frame = window.requestAnimationFrame(focusRouteTarget);
      return () => window.cancelAnimationFrame(frame);
    }

    const timeout = window.setTimeout(focusRouteTarget, 0);
    return () => window.clearTimeout(timeout);
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
          aria-label="Open workstation command palette (Ctrl K)"
        >
          <Search className="h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
          <span className="workstation-search-placeholder">Search workflows, presets, workspaces…</span>
          <span className="workstation-search-kbd" aria-hidden="true">Ctrl K</span>
        </button>

        <div className="workstation-actions">
          <MegaMenu />
          {session ? (
            <div className="workstation-session-card">
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
        <WorkspaceNav className="workstation-rail-desktop" />

        <main
          ref={workbenchRef}
          id="workbench-content"
          className="workbench grid grid-rows-[auto_minmax(0,1fr)]"
          tabIndex={-1}
        >
          <WorkspaceHeader
            workspace={shell.activeWorkspace}
            session={session}
            onOpenCommandPalette={() => setCommandOpen(true)}
            onRefresh={refresh}
            refreshing={loading}
          />

          <div className="workbench-scroll px-4 py-4 lg:px-6 lg:py-5">
            {usingDevelopmentFixtures ? <DevelopmentFixtureNotice refreshing={loading} onRetry={refresh} /> : null}
            {shell.statusPanel ? <ShellStatus panel={shell.statusPanel} onRetry={refresh} /> : null}
            {shell.canRenderRoutes ? (
              <Routes>
                <Route path="/" element={<OverviewScreen data={overview} session={session} />} />
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
                <Route path="/accounting/*" element={<GovernanceScreen data={governance} />} />
                <Route path="/reporting/evidence" element={<EvidenceWorkbenchScreen />} />
                <Route path="/reporting/*" element={<ReportingScreen data={reporting} />} />
                <Route path="/strategy/quant-lab" element={<QuantLabScreen />} />
                <Route path="/strategy/*" element={<ResearchScreen data={research} />} />
                <Route path="/data/quotes" element={<LiveQuotesScreen />} />
                <Route path="/data/watchlist" element={<WatchlistScreen />} />
                <Route path="/data/*" element={<DataOperationsScreen data={dataOperations} />} />
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
                    onRefresh={refresh}
                    loading={loading}
                    error={error}
                    workspaceErrors={workspaceErrors}
                  />
                )} />
                <Route path="/overview/*" element={<LegacyWorkspaceRedirect />} />
                <Route path="/research/*" element={<LegacyWorkspaceRedirect />} />
                <Route path="/data-operations/*" element={<LegacyWorkspaceRedirect />} />
                <Route path="/governance/*" element={<LegacyWorkspaceRedirect />} />
                <Route path="*" element={<Navigate to="/trading" replace />} />
              </Routes>
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
            <WorkspaceNav className="workstation-nav-sheet" onNavigate={() => setNavOpen(false)} />
          </SheetBody>
        </SheetContent>
      </Sheet>
    </div>
  );
}

function LegacyWorkspaceRedirect() {
  const location = useLocation();
  return <Navigate to={legacyWorkspaceRedirect(location.pathname, location.search, location.hash) ?? "/trading"} replace />;
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
      className="mb-4 flex flex-col gap-3 rounded-lg border border-warning/35 bg-warning/10 px-3 py-3 text-sm text-foreground lg:flex-row lg:items-center lg:justify-between"
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
        <nav aria-label="Demo workflow" className="flex flex-wrap items-center gap-2">
          <span className="text-xs font-semibold uppercase tracking-normal text-muted-foreground">
            {viewModel.workflowLabel}
          </span>
          {viewModel.steps.map((item) => {
            const Icon = developmentFixtureDemoIcons[item.id];
            return (
              <Button
                key={item.href}
                asChild
                variant={item.active ? "default" : "outline"}
                size="sm"
                className={item.active ? "" : "bg-background/40"}
              >
                <Link to={item.href} aria-label={item.ariaLabel} aria-current={item.active ? "step" : undefined}>
                  <span
                    aria-hidden="true"
                    className={item.active
                      ? "font-mono text-[0.6875rem] text-primary-foreground/80"
                      : "font-mono text-[0.6875rem] text-muted-foreground"}
                  >
                    {item.step}
                  </span>
                  <Icon className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>{item.label}</span>
                </Link>
              </Button>
            );
          })}
        </nav>
      </div>
    </div>
  );
}

function focusElementById(targetElementId: string, fallbackElement: HTMLElement | null): void {
  const target = document.getElementById(targetElementId) ?? fallbackElement;
  if (!target) {
    return;
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
}

const developmentFixtureDemoIcons: Record<DevelopmentFixtureNoticeStep["id"], LucideIcon> = {
  watchlist: Database,
  quotes: LineChart,
  readiness: ShieldCheck,
  connect: KeyRound
};

function ShellStatus({ panel, onRetry }: { panel: ShellStatusPanel; onRetry: () => void }) {
  const toneClass =
    panel.tone === "danger"
      ? "border-danger/30 bg-danger/10 text-danger"
      : panel.tone === "warning"
        ? "border-warning/30 bg-warning/10 text-warning"
        : "border-border/70 bg-secondary/25 text-muted-foreground";
  const Icon = panel.tone === "loading" ? LoaderCircle : AlertTriangle;

  return (
    <Card
      id={panel.id}
      role={panel.role}
      aria-live={panel.ariaLive}
      aria-labelledby={panel.titleId}
      aria-describedby={panel.detailId}
      className={`mb-4 ${toneClass}`}
    >
      <CardHeader className="flex flex-col gap-3 space-y-0 md:flex-row md:items-start md:justify-between">
        <div>
          <div className="eyebrow-label">Shell status</div>
          <CardTitle id={panel.titleId} className="mt-2 flex items-center gap-2 text-base text-foreground">
            <Icon
              aria-hidden="true"
              className={`h-4 w-4 shrink-0 ${panel.tone === "loading" ? "animate-spin" : ""}`}
            />
            {panel.title}
          </CardTitle>
        </div>
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
      </CardHeader>
      <CardContent className="space-y-3 text-sm">
        <p id={panel.detailId} className="leading-6 text-foreground/80">{panel.detail}</p>
        {panel.items.length > 0 ? (
          <ul aria-label={panel.itemListLabel} className="grid gap-2 md:grid-cols-2">
            {panel.items.map((item) => (
              <li key={item.key} aria-label={item.ariaLabel} className="rounded-md border border-border/60 bg-background/45 px-3 py-2">
                <div className="font-semibold text-foreground">{item.label}</div>
                <div className="mt-1 text-xs leading-5 text-foreground/70">{item.detail}</div>
              </li>
            ))}
          </ul>
        ) : null}
      </CardContent>
    </Card>
  );
}


function isEditableTarget(target: EventTarget | null): boolean {
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


function isCommandPaletteShortcut(event: KeyboardEvent): boolean {
  return (event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey && event.key.toLowerCase() === "k";
}
