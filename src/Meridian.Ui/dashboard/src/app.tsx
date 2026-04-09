import { useState } from "react";
import { Navigate, Route, Routes, useLocation } from "react-router-dom";
import { CommandPalette } from "@/components/meridian/command-palette";
import { WorkspaceHeader } from "@/components/meridian/workspace-header";
import { WorkspaceNav } from "@/components/meridian/workspace-nav";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { useWorkstationData } from "@/hooks/use-workstation-data";
import { WORKSPACES } from "@/lib/workspace";
import { DataOperationsScreen } from "@/screens/data-operations-screen";
import { GovernanceScreen } from "@/screens/governance-screen";
import { OverviewScreen } from "@/screens/overview-screen";
import { ResearchScreen } from "@/screens/research-screen";
import { TradingScreen } from "@/screens/trading-screen";
import type { WorkspaceKey } from "@/types";

export function App() {
  const [commandOpen, setCommandOpen] = useState(false);
  const { pathname } = useLocation();
  const { session, overview, research, trading, dataOperations, governance, loading, error, workspaceErrors, refresh } = useWorkstationData();
  const activeWorkspace = getWorkspaceForPath(pathname);
  const degradedWorkspaceCount = Object.keys(workspaceErrors).length;
  const bootstrapFailed = !loading && !session && !research && !trading;

  return (
    /* Edge-to-edge application shell: sidebar + main column */
    <div className="flex h-screen overflow-hidden">
      <WorkspaceNav />

      <div className="flex flex-1 flex-col overflow-hidden">
        <WorkspaceHeader
          workspace={activeWorkspace}
          session={session}
          onOpenCommandPalette={() => setCommandOpen(true)}
          onRefresh={refresh}
        />

        {/* Scrollable content area */}
        <main className="flex-1 overflow-auto p-5 lg:p-6">
          {!loading && degradedWorkspaceCount > 0 ? (
            <Card className="mb-4 border-warning/30">
              <CardHeader>
                <CardTitle>Workstation bootstrap is partially degraded</CardTitle>
              </CardHeader>
              <CardContent className="text-sm text-muted-foreground">
                {error ?? "Some prefetched workspace summaries did not load. Routes remain available while those slices recover."}
              </CardContent>
            </Card>
          ) : null}

          {loading ? (
            <Card>
              <CardHeader>
                <CardTitle>Booting workstation shell</CardTitle>
              </CardHeader>
              <CardContent className="text-sm text-muted-foreground">
                Loading session state, workspace summaries, and the initial research slice.
              </CardContent>
            </Card>
          ) : bootstrapFailed ? (
            <Card className="border-danger/20">
              <CardHeader>
                <CardTitle>Workstation bootstrap failed</CardTitle>
              </CardHeader>
              <CardContent className="text-sm text-danger">{error}</CardContent>
            </Card>
          ) : (
            <Routes>
              <Route path="/overview" element={<OverviewScreen data={overview} session={session} />} />
              <Route path="/" element={<ResearchScreen data={research} />} />
              <Route
                path="/trading/*"
                element={<TradingScreen data={trading} />}
              />
              <Route
                path="/data-operations/*"
                element={<DataOperationsScreen data={dataOperations} />}
              />
              <Route
                path="/governance/*"
                element={<GovernanceScreen data={governance} />}
              />
              <Route path="*" element={<Navigate to="/overview" replace />} />
            </Routes>
          )}
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
  if (pathname.startsWith("/overview")) {
    return "overview";
  }

  if (pathname.startsWith("/trading")) {
    return "trading";
  }

  if (pathname.startsWith("/data-operations")) {
    return "data-operations";
  }

  if (pathname.startsWith("/governance")) {
    return "governance";
  }

  if (pathname === "/") {
    return "research";
  }

  return "overview";
}
