import { AlertTriangle, RefreshCcw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { buildWorkspaceHeaderViewModel } from "@/components/meridian/workspace-header.view-model";
import type { SessionInfo, WorkspaceSummary } from "@/types";

/**
 * Top-of-workspace header strip rendered inside each workspace layout.
 *
 * Displays the workspace title, eyebrow, description, session context pill, and
 * an optional refresh action. When the active session environment is `"live"`,
 * a high-contrast alarm banner is prepended to warn operators they are in a
 * real-money environment.
 *
 * **Live banner:** built from `WorkspaceSummary.environment` — shown automatically
 * when `session.environment === "live"`. Uses `--state-live-*` CSS tokens and
 * `aria-live="assertive"` so screen readers announce it immediately on mount.
 *
 * **Refresh:** provide `onRefresh` to enable the refresh button. Pass `refreshing`
 * while the data fetch is in flight — the button shows a spinner and sets `aria-busy`
 * on the `<header>` element.
 *
 * **Command palette:** `onOpenCommandPalette` is wired to the "Search / commands"
 * secondary button in the action row.
 *
 * @example
 * <WorkspaceHeader
 *   workspace={workspaceSummary}
 *   session={session}
 *   onOpenCommandPalette={() => setPaletteOpen(true)}
 *   onRefresh={() => void refresh()}
 *   refreshing={isRefreshing}
 * />
 */
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
    <header className="border-b border-border bg-card" aria-busy={viewModel.ariaBusy}>
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
      <div className="workspace-header-shell px-4 py-3 lg:px-6">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex min-w-0 flex-col gap-1">
            <div className="flex flex-wrap items-center gap-2">
              <div className="eyebrow-label">{viewModel.eyebrow}</div>
              {viewModel.badges.map((badge) => (
                <Badge key={badge.id} variant={badge.variant} aria-label={badge.ariaLabel}>
                  {badge.label}
                </Badge>
              ))}
            </div>
            <h1 className="font-display text-xl font-semibold leading-snug text-foreground">{viewModel.title}</h1>
          </div>

          <div className="flex flex-shrink-0 flex-wrap items-center gap-2">
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
              </Button>
            ) : null}
            <Button
              variant="secondary"
              size="sm"
              onClick={onOpenCommandPalette}
              title={viewModel.commandAction.title}
              aria-label={viewModel.commandAction.ariaLabel}
              disabled={viewModel.commandAction.disabled}
            >
              {viewModel.commandAction.label}
            </Button>
          </div>
        </div>
      </div>
      <span className="sr-only" aria-live="polite">{viewModel.liveAnnouncement}</span>
    </header>
  );
}
