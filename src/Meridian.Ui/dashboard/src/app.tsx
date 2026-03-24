import { useState } from "react";
import { Navigate, Route, Routes, useLocation } from "react-router-dom";
import { CommandPalette } from "@/components/meridian/command-palette";
import { WorkspaceHeader } from "@/components/meridian/workspace-header";
import { WorkspaceNav } from "@/components/meridian/workspace-nav";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { useWorkstationData } from "@/hooks/use-workstation-data";
import { WORKSPACES } from "@/lib/workspace";
import { ResearchScreen } from "@/screens/research-screen";
import { TradingScreen } from "@/screens/trading-screen";
import { WorkspacePlaceholder } from "@/screens/workspace-placeholder";
import type { WorkspaceKey } from "@/types";

export function App() {
  const [commandOpen, setCommandOpen] = useState(false);
  const { pathname } = useLocation();
  const { session, research, trading, loading, error } = useWorkstationData();
  const activeWorkspace = getWorkspaceForPath(pathname);

  return (
    <div className="min-h-screen p-4 lg:p-6">
      <div className="mx-auto flex max-w-[1720px] flex-col gap-4 lg:flex-row">
        <WorkspaceNav />

        <main className="panel-surface min-h-[calc(100vh-3rem)] flex-1 overflow-hidden p-6 lg:p-8">
          <WorkspaceHeader
            workspace={activeWorkspace}
            session={session}
            onOpenCommandPalette={() => setCommandOpen(true)}
          />

          <div className="mt-8">
            {loading ? (
              <Card>
                <CardHeader>
                  <CardTitle>Booting workstation shell</CardTitle>
                </CardHeader>
                <CardContent className="text-sm text-muted-foreground">
                  Loading session state, workspace summaries, and the initial research slice.
                </CardContent>
              </Card>
            ) : error ? (
              <Card className="border-danger/20">
                <CardHeader>
                  <CardTitle>Workstation bootstrap failed</CardTitle>
                </CardHeader>
                <CardContent className="text-sm text-danger">{error}</CardContent>
              </Card>
            ) : (
              <Routes>
                <Route path="/" element={<ResearchScreen data={research} />} />
                <Route
                  path="/trading"
                  element={<TradingScreen data={trading} />}
                />
                <Route
                  path="/data-operations"
                  element={
                    <WorkspacePlaceholder
                      title="Data Operations Workspace"
                      description="Provider health, backfills, storage, and export workflows will share the same workstation primitives."
                    />
                  }
                />
                <Route
                  path="/governance"
                  element={
                    <WorkspacePlaceholder
                      title="Governance Workspace"
                      description="Ledger, diagnostics, notifications, and settings are reserved in the IA and ready for follow-on slices."
                    />
                  }
                />
                <Route path="*" element={<Navigate to="/" replace />} />
              </Routes>
            )}
          </div>
        </main>
      </div>

      <CommandPalette open={commandOpen} onOpenChange={setCommandOpen} />
    </div>
  );
}

function getWorkspaceForPath(pathname: string) {
  const key = normalizeWorkspace(pathname);
  return WORKSPACES.find((workspace) => workspace.key === key) ?? WORKSPACES[0];
}

function normalizeWorkspace(pathname: string): WorkspaceKey {
  if (pathname.startsWith("/trading")) {
    return "trading";
  }

  if (pathname.startsWith("/data-operations")) {
    return "data-operations";
  }

  if (pathname.startsWith("/governance")) {
    return "governance";
  }

  return "research";
}
