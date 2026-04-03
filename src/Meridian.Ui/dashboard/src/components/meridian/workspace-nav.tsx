import { BarChart3, DatabaseZap, FlaskConical, Landmark, LayoutDashboard, RadioTower } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { cn } from "@/lib/utils";
import { WORKSPACES, workspacePath } from "@/lib/workspace";
import type { WorkspaceKey } from "@/types";

const icons: Record<WorkspaceKey, typeof LayoutDashboard> = {
  overview: LayoutDashboard,
  research: FlaskConical,
  trading: RadioTower,
  "data-operations": DatabaseZap,
  governance: Landmark
};

export function WorkspaceNav() {
  const location = useLocation();

  function isActive(key: WorkspaceKey) {
    const path = workspacePath(key);
    if (key === "overview") return location.pathname === "/overview";
    if (key === "research") return location.pathname === "/";
    return location.pathname.startsWith(path);
  }

  return (
    <aside
      className="flex w-[220px] shrink-0 flex-col border-r border-border/45 bg-[hsl(var(--toolbar-bg))] min-h-screen"
    >
      {/* ── Logo / identity ── */}
      <div className="flex h-12 shrink-0 items-center gap-2.5 border-b border-border/45 px-4">
        <div className="flex h-7 w-7 items-center justify-center rounded-lg bg-primary/15 text-primary">
          <BarChart3 className="h-[15px] w-[15px]" />
        </div>
        <div>
          <div className="text-[13px] font-bold leading-none">Meridian</div>
          <div className="mt-0.5 text-[10px] text-muted-foreground/65">Operator Workstation</div>
        </div>
      </div>

      {/* ── Navigation items ── */}
      <nav className="flex-1 px-2 py-3" aria-label="Workspaces">
        <div className="space-y-px">
          {WORKSPACES.map((workspace) => {
            const Icon = icons[workspace.key];
            const active = isActive(workspace.key);
            return (
              <Link
                key={workspace.key}
                to={workspacePath(workspace.key)}
                className={cn(
                  "flex items-center gap-2.5 rounded-md px-3 py-2 text-[13px] transition-all duration-150",
                  active
                    ? "bg-primary/12 font-semibold text-primary"
                    : "font-medium text-muted-foreground hover:bg-secondary/40 hover:text-foreground"
                )}
              >
                <Icon
                  className={cn(
                    "h-4 w-4 shrink-0",
                    active ? "text-primary" : "text-muted-foreground/60"
                  )}
                />
                <span className="flex-1">{workspace.label}</span>
                {active && (
                  <div className="h-1.5 w-1.5 shrink-0 rounded-full bg-primary/80" />
                )}
              </Link>
            );
          })}
        </div>
      </nav>

      {/* ── Footer ── */}
      <div className="border-t border-border/35 px-4 py-3">
        <p className="text-[10px] text-muted-foreground/35">v1.7.2 · Meridian Platform</p>
      </div>
    </aside>
  );
}
