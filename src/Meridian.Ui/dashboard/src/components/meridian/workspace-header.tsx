import { AlertTriangle, RefreshCcw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { buildWorkspaceHeaderViewModel } from "@/components/meridian/workspace-header.view-model";
import type { SessionInfo, WorkspaceSummary } from "@/types";

interface WorkspaceHeaderProps {
  workspace: WorkspaceSummary;
  session: SessionInfo | null;
  onOpenCommandPalette: () => void;
  onRefresh?: () => void;
  refreshing?: boolean;
}

export function WorkspaceHeader({
  workspace,
  session,
  onOpenCommandPalette,
  onRefresh,
  refreshing = false
}: WorkspaceHeaderProps) {
  const viewModel = buildWorkspaceHeaderViewModel({
    workspace,
    session,
    canRefresh: Boolean(onRefresh),
    refreshing
  });

  return (
    <header className="border-b border-border bg-[#0B1520]" aria-busy={viewModel.ariaBusy}>
      {viewModel.liveBanner ? (
        <div
          className="flex items-center gap-3 border-b px-4 py-2 lg:px-6"
          style={{
            backgroundColor: "var(--state-live-bg)",
            borderColor: "var(--state-live-bd)",
            color: "var(--state-live-fg)"
          }}
          role={viewModel.liveBanner.role}
          aria-live={viewModel.liveBanner.ariaLive}
          aria-label={viewModel.liveBanner.ariaLabel}
        >
          <AlertTriangle className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
          <span className="font-mono text-[10px] font-semibold uppercase tracking-[0.14em]">
            {viewModel.liveBanner.label}
          </span>
          <span className="text-xs leading-5 opacity-80">{viewModel.liveBanner.detail}</span>
        </div>
      ) : null}
      <div className="flex flex-col gap-4 px-4 py-4 lg:flex-row lg:items-end lg:justify-between lg:px-6">
        <div className="space-y-3">
          <div className="flex flex-wrap items-center gap-3">
            {viewModel.badges.map((badge) => (
              <Badge key={badge.id} variant={badge.variant} aria-label={badge.ariaLabel}>
                {badge.label}
              </Badge>
            ))}
          </div>
          <div className="space-y-2">
            <div className="eyebrow-label">{viewModel.eyebrow}</div>
            <h1 className="font-display text-[2rem] font-semibold leading-tight text-foreground">{viewModel.title}</h1>
            <p className="max-w-3xl text-sm leading-6 text-muted-foreground">{viewModel.description}</p>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-3">
          <div
            className="toolbar-chip"
            aria-label={viewModel.sessionPillAriaLabel}
          >
            <span className="font-mono">
              {viewModel.sessionLabel}
              {viewModel.sessionRoleLabel ? (
                <span className="ml-2 text-muted-foreground">{viewModel.sessionRoleLabel}</span>
              ) : null}
            </span>
          </div>
          {viewModel.refreshAction && onRefresh ? (
            <Button
              variant="ghost"
              size="sm"
              onClick={onRefresh}
              title={viewModel.refreshAction.title}
              aria-label={viewModel.refreshAction.ariaLabel}
              disabled={viewModel.refreshAction.disabled}
            >
              <RefreshCcw className={`size-4 ${viewModel.refreshAction.disabled ? "animate-spin" : ""}`} />
              {viewModel.refreshAction.label}
            </Button>
          ) : null}
          <Button
            variant="secondary"
            onClick={onOpenCommandPalette}
            title={viewModel.commandAction.title}
            aria-label={viewModel.commandAction.ariaLabel}
            disabled={viewModel.commandAction.disabled}
          >
            {viewModel.commandAction.label}
          </Button>
        </div>
      </div>
      <span className="sr-only" aria-live="polite">{viewModel.liveAnnouncement}</span>
    </header>
  );
}
