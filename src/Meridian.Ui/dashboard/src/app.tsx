import { useState } from "react";
import { AlertTriangle, LoaderCircle, Search } from "lucide-react";
import { Navigate, Route, Routes, useLocation } from "react-router-dom";
import meridianMarkUrl from "@/assets/brand/meridian-mark.svg";
import { buildAppShellViewState, type ShellStatusPanel } from "@/app-shell.view-model";
import { CommandPalette } from "@/components/meridian/command-palette";
import { WorkspaceHeader } from "@/components/meridian/workspace-header";
import { WorkspaceNav } from "@/components/meridian/workspace-nav";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { useWorkstationData } from "@/hooks/use-workstation-data";
import { legacyWorkspaceRedirect, workspaceForKey } from "@/lib/workspace";
import { DataOperationsScreen } from "@/screens/data-operations-screen";
import { GovernanceScreen } from "@/screens/governance-screen";
import { OperatorReadinessConsole } from "@/screens/operator-readiness-console";
import { ResearchScreen } from "@/screens/research-screen";
import { TradingScreen } from "@/screens/trading-screen";
import { WorkspacePlaceholderScreen } from "@/screens/workspace-placeholder-screen";
import type { SessionInfo, SystemOverviewResponse, WorkspaceKey } from "@/types";

export function App() {
  const [commandOpen, setCommandOpen] = useState(false);
  const { pathname } = useLocation();
  const { session, overview, research, trading, dataOperations, governance, loading, error, workspaceErrors, refresh } = useWorkstationData();
  const shell = buildAppShellViewState({
    pathname,
    loading,
    error,
    workspaceErrors,
    payload: {
      session,
      overview,
      research,
      trading,
      dataOperations,
      governance
    }
  });

  return (
    <div className="workstation-frame">
      <header className="workstation-masthead">
        <div className="workstation-brand">
          <img src={meridianMarkUrl} alt="" aria-hidden="true" />
          <div className="min-w-0">
            <div className="name">Meridian</div>
            <div className="sub">Operator workstation</div>
          </div>
        </div>

        <button
          type="button"
          className="workstation-search focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
          onClick={() => setCommandOpen(true)}
          aria-label="Open workstation command palette"
        >
          <Search className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
          <span className="truncate">
            <b>{shell.activeWorkspace.label}</b> · {shell.activeWorkspace.status} · Command palette
          </span>
        </button>

        <div className="workstation-actions">
          {session ? (
            <>
              <Badge variant={session.environment}>{session.environment}</Badge>
              <span>{session.displayName}</span>
              <span className="text-muted-foreground">{session.role}</span>
            </>
          ) : (
            <span>Session loading</span>
          )}
        </div>
      </header>

      <div className="workstation-shell">
        <WorkspaceNav />

        <main className="workbench grid grid-rows-[auto_minmax(0,1fr)]">
          <WorkspaceHeader
            workspace={shell.activeWorkspace}
            session={session}
            onOpenCommandPalette={() => setCommandOpen(true)}
            onRefresh={refresh}
            refreshing={loading}
          />

          <div className="workbench-scroll px-4 py-4 lg:px-6 lg:py-5">
            {shell.statusPanel ? <ShellStatus panel={shell.statusPanel} onRetry={refresh} /> : null}
            {shell.canRenderRoutes ? (
              <Routes>
                <Route path="/" element={<Navigate to="/trading" replace />} />
                <Route path="/trading/readiness" element={(
                  <OperatorReadinessConsole
                    research={research}
                    trading={trading}
                    dataOperations={dataOperations}
                    governance={governance}
                  />
                )} />
                <Route path="/trading/*" element={<TradingScreen data={trading} />} />
                <Route path="/portfolio/*" element={<Placeholder workspaceKey="portfolio" session={session} overview={overview} />} />
                <Route path="/accounting/*" element={<GovernanceScreen data={governance} />} />
                <Route path="/reporting/*" element={<GovernanceScreen data={governance} />} />
                <Route path="/strategy/*" element={<ResearchScreen data={research} />} />
                <Route path="/data/*" element={<DataOperationsScreen data={dataOperations} />} />
                <Route path="/settings/*" element={<Placeholder workspaceKey="settings" session={session} overview={overview} />} />
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

      <CommandPalette open={commandOpen} onOpenChange={setCommandOpen} />
    </div>
  );
}

function Placeholder({
  workspaceKey,
  session,
  overview
}: {
  workspaceKey: WorkspaceKey;
  session: SessionInfo | null;
  overview: SystemOverviewResponse | null;
}) {
  return <WorkspacePlaceholderScreen workspace={workspaceForKey(workspaceKey)} session={session} overview={overview} />;
}

function LegacyWorkspaceRedirect() {
  const location = useLocation();
  return <Navigate to={legacyWorkspaceRedirect(location.pathname, location.search, location.hash) ?? "/trading"} replace />;
}

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
