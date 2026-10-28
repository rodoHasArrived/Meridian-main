import type { ReactNode } from "react";
import { AlertTriangle, Info, RefreshCcw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Breadcrumb, type BreadcrumbItem } from "@/components/ui/breadcrumb";
import { Button } from "@/components/ui/button";
import { buildWorkspaceHeaderViewModel } from "@/components/meridian/workspace-header.view-model";
import type { SessionInfo, WorkspaceSummary } from "@/types";

/**
 * Top-of-workspace header strip rendered inside each workspace layout.
 *
 * Displays the workspace title, posture, description, and an optional refresh action. When the
 * active session environment is `"live"`,
 * a high-contrast alarm banner is prepended to warn operators they are in a
 * real-money environment.
 *
 * **Live banner:** built from `WorkspaceSummary.environment` — shown automatically
 * when `session.environment === "live"`. Uses `--state-live-*` CSS tokens and
 * `aria-live="assertive"` so screen readers announce it immediately on mount.
 *
 * **Refresh:** provide `onRefresh` to enable the refresh button. Pass `refreshing`
 * while the data fetch is in flight — the button exposes a view-model disabled reason,
 * shows a spinner, and sets `aria-busy` on the `<header>` element.
 *
 * @example
 * <WorkspaceHeader
 *   workspace={workspaceSummary}
 *   session={session}
 *   onRefresh={() => void refresh()}
 *   refreshing={isRefreshing}
 * />
 */
interface WorkspaceHeaderProps {
  breadcrumbItems?: BreadcrumbItem[];
  workspace: WorkspaceSummary;
  session: SessionInfo | null;
  onRefresh?: () => void;
  refreshing?: boolean;
  /** Shell-provided header actions (e.g. copy link, save view), rendered before refresh. */
  actions?: ReactNode;
  /** Persistent scope and optional workflow disclosure in the compact header. */
  context?: ReactNode;
}

export function WorkspaceHeader({
  breadcrumbItems = [],
  workspace,
  session,
  onRefresh,
  refreshing = false,
  actions,
  context
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
      <div className="workspace-header-shell workstation-header-compact">
        <div className="workstation-header-location">
          {breadcrumbItems.length > 0 ? <Breadcrumb items={breadcrumbItems} /> : null}
          <div className="workstation-header-title-row">
            <h1 className="font-display font-semibold text-foreground">{viewModel.title}</h1>
            {viewModel.badges.map((badge) => (
              <Badge key={badge.id} variant={badge.variant} aria-label={badge.ariaLabel}>
                {badge.label}
              </Badge>
            ))}
            <details className="workstation-header-info" onKeyDown={(event) => {
              if (event.key === "Escape") {
                event.preventDefault();
                event.currentTarget.open = false;
                event.currentTarget.querySelector("summary")?.focus();
              }
            }}>
              <summary aria-label={`About ${workspace.label}`} title={`About ${workspace.label}`}>
                <Info className="size-4" aria-hidden="true" />
              </summary>
              <p>{viewModel.description}</p>
            </details>
          </div>
        </div>
        <div className="workstation-header-actions">
          {actions}
          {viewModel.refreshAction && onRefresh ? (
            <Button
              variant="ghost"
              size="sm"
              onClick={onRefresh}
              title={viewModel.refreshAction.title}
              aria-label={viewModel.refreshAction.ariaLabel}
              disabled={viewModel.refreshAction.disabled}
              disabledReason={viewModel.refreshAction.disabledReason}
            >
              <RefreshCcw
                className={`size-4 ${viewModel.refreshAction.busy ? "animate-spin" : ""}`}
                aria-hidden="true"
              />
              <span>{refreshing ? "Refreshing" : "Refresh"}</span>
            </Button>
          ) : null}
        </div>
        {context ? <div className="workstation-header-context">{context}</div> : null}
        <span className="sr-only" aria-label={viewModel.sessionPillAriaLabel}>{viewModel.sessionLabel}</span>
      </div>
      <span className="sr-only" aria-live="polite">{viewModel.liveAnnouncement}</span>
    </header>
  );
}
