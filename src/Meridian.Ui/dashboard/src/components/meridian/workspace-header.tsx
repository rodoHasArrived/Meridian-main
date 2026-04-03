import { Command, ChevronRight, RefreshCcw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import type { SessionInfo, WorkspaceSummary } from "@/types";

interface WorkspaceHeaderProps {
  workspace: WorkspaceSummary;
  session: SessionInfo | null;
  onOpenCommandPalette: () => void;
  onRefresh?: () => void;
}

export function WorkspaceHeader({ workspace, session, onOpenCommandPalette, onRefresh }: WorkspaceHeaderProps) {
  return (
    <header className="shrink-0">
      {/* ── Toolbar row (Marquee-style compact dark bar) ── */}
      <div className="mq-toolbar">
        {/* Breadcrumb + title */}
        <div className="flex flex-1 items-center gap-2 min-w-0">
          <span className="hidden text-[11px] font-bold uppercase tracking-[0.18em] text-muted-foreground/40 sm:block">
            Meridian
          </span>
          <ChevronRight className="hidden size-3 shrink-0 text-muted-foreground/25 sm:block" />
          <span className="truncate text-[13px] font-semibold text-foreground">
            {workspace.label}
          </span>

          {session ? (
            <Badge
              variant={session.environment === "research" ? "default" : session.environment}
              className="h-[18px] px-1.5 py-0 text-[10px]"
            >
              {session.environment.toUpperCase()}
            </Badge>
          ) : null}

          <Badge
            variant={workspace.status === "Live" ? "success" : "warning"}
            className="h-[18px] px-1.5 py-0 text-[10px]"
          >
            {workspace.status}
          </Badge>
        </div>

        {/* Action buttons */}
        <div className="flex shrink-0 items-center gap-1.5">
          {onRefresh && (
            <Button
              variant="ghost"
              size="sm"
              onClick={onRefresh}
              title="Refresh all workspace data"
              className="h-7 gap-1 px-2 text-xs text-muted-foreground hover:text-foreground"
            >
              <RefreshCcw className="size-3" />
              <span className="hidden sm:inline">Refresh</span>
            </Button>
          )}
          <Button
            variant="outline"
            size="sm"
            onClick={onOpenCommandPalette}
            aria-label="Open command palette (⌘K)"
            className="h-7 gap-1 border-border/50 px-2.5 text-xs"
          >
            <Command className="size-3" />
            <span className="hidden sm:inline">Command</span>
            <kbd className="ml-1 hidden text-[10px] text-muted-foreground sm:inline">⌘K</kbd>
          </Button>
        </div>
      </div>

      {/* ── Stat strip (dense session / workspace metadata row) ── */}
      {session && (
        <div className="mq-stat-strip">
          <div className="mq-stat-item">
            <span className="mq-stat-label">User</span>
            <span className="mq-stat-value">{session.displayName}</span>
          </div>
          <div className="mq-stat-item">
            <span className="mq-stat-label">Role</span>
            <span className="mq-stat-value">{session.role}</span>
          </div>
          <div className="mq-stat-item">
            <span className="mq-stat-label">Env</span>
            <span
              className={cn(
                "mq-stat-value",
                session.environment === "live" && "text-danger",
                session.environment === "paper" && "text-warning",
                session.environment === "research" && "text-blue-400"
              )}
            >
              {session.environment}
            </span>
          </div>
          <div className="mq-stat-item">
            <span className="mq-stat-label">Workspace</span>
            <span
              className={cn(
                "mq-stat-value",
                workspace.status === "Live" && "text-success"
              )}
            >
              {workspace.status}
            </span>
          </div>
          {/* Description pushed to the right */}
          <div className="ml-auto hidden shrink-0 lg:block">
            <span className="text-[10px] text-muted-foreground/35">{workspace.description}</span>
          </div>
        </div>
      )}
    </header>
  );
}
