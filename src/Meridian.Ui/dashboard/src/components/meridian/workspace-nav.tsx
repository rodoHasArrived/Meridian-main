import { BarChart3, DatabaseZap, Landmark, RadioTower, Search } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { cn } from "@/lib/utils";
import { WORKSPACES, workspacePath } from "@/lib/workspace";
import type { WorkspaceKey } from "@/types";

const icons: Record<WorkspaceKey, typeof Search> = {
  research: Search,
  trading: RadioTower,
  "data-operations": DatabaseZap,
  governance: Landmark
};

export function WorkspaceNav() {
  const location = useLocation();

  return (
    <aside className="panel-surface-strong flex min-h-[calc(100vh-3rem)] w-full flex-col gap-8 p-5 lg:w-[320px]">
      <div className="space-y-4">
        <div className="flex items-center gap-3">
          <div className="flex h-12 w-12 items-center justify-center rounded-2xl border border-primary/30 bg-primary/12 text-primary">
            <BarChart3 className="h-6 w-6" />
          </div>
          <div>
            <div className="font-display text-lg font-semibold">Meridian</div>
            <div className="text-sm text-muted-foreground">Operator Workstation</div>
          </div>
        </div>
        <div className="rounded-2xl border border-border/80 bg-secondary/35 px-4 py-4">
          <div className="eyebrow-label">Operating Model</div>
          <p className="mt-2 text-sm leading-6 text-muted-foreground">
            Workflow-centric shell for research, trading, operations, and governance with mode-aware visual posture.
          </p>
        </div>
      </div>

      <nav className="space-y-2" aria-label="Workspaces">
        {WORKSPACES.map((workspace) => {
          const Icon = icons[workspace.key];
          const active = location.pathname === workspacePath(workspace.key);
          return (
            <Link
              key={workspace.key}
              to={workspacePath(workspace.key)}
              className={cn(
                "flex items-start gap-3 rounded-2xl border px-4 py-3 transition-all duration-200",
                active
                  ? "border-primary/30 bg-primary/10 text-foreground shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]"
                  : "border-transparent bg-transparent text-muted-foreground hover:border-border hover:bg-secondary/55 hover:text-foreground"
              )}
            >
              <Icon className="mt-0.5 h-4 w-4 shrink-0" />
              <span className="space-y-1">
                <span className="block text-sm font-semibold">{workspace.label}</span>
                <span className="block text-xs leading-5">{workspace.status}</span>
              </span>
            </Link>
          );
        })}
      </nav>

      <div className="mt-auto rounded-2xl border border-border bg-secondary/45 px-4 py-5 text-sm text-slate-50">
        <div className="eyebrow-label">Phase 1 Delivery</div>
        <div className="mt-3 font-semibold text-foreground">Shared shell, premium posture, migration-safe route</div>
        <p className="mt-2 leading-6 text-muted-foreground">
          Research ships first with source-owned primitives and a migration-safe route under
          <code className="ml-1 rounded bg-black/20 px-1 py-0.5 text-xs text-foreground">/workstation</code>.
        </p>
      </div>
    </aside>
  );
}
