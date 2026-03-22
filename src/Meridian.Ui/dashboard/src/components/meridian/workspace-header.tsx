import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import type { SessionInfo, WorkspaceSummary } from "@/types";

interface WorkspaceHeaderProps {
  workspace: WorkspaceSummary;
  session: SessionInfo | null;
  onOpenCommandPalette: () => void;
}

export function WorkspaceHeader({ workspace, session, onOpenCommandPalette }: WorkspaceHeaderProps) {
  return (
    <header className="rounded-[1.75rem] border border-border bg-panel-strong/80 p-5 lg:p-6">
      <div className="flex flex-col gap-6 lg:flex-row lg:items-end lg:justify-between">
      <div className="space-y-4">
        <div className="flex flex-wrap items-center gap-3">
          <Badge variant="outline">{workspace.label}</Badge>
          {session ? <Badge variant={session.environment}>{session.environment.toUpperCase()}</Badge> : null}
          <Badge variant={workspace.status === "Live" ? "success" : "warning"}>{workspace.status}</Badge>
        </div>
        <div className="space-y-2">
          <div className="eyebrow-label">Meridian Workspace</div>
          <h1 className="font-display text-4xl font-bold tracking-tight text-foreground">{workspace.label} Workstation</h1>
          <p className="max-w-3xl text-sm leading-6 text-muted-foreground">{workspace.description}</p>
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <div className="rounded-full border border-border bg-secondary/60 px-4 py-2 text-sm shadow-sm">
          {session ? (
            <span className="font-medium">
              {session.displayName}
              <span className="ml-2 text-muted-foreground">{session.role}</span>
            </span>
          ) : (
            <span className="text-muted-foreground">Loading session</span>
          )}
        </div>
        <Button variant="secondary" onClick={onOpenCommandPalette}>
          Open Command Palette
        </Button>
      </div>
      </div>
    </header>
  );
}
