import type { ReactNode } from "react";
import { AlertTriangle, ChevronRight, Info, RefreshCcw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import type { BreadcrumbItem } from "@/components/ui/breadcrumb";
import type { DataProvenanceKind } from "@/app-shell.data-provenance-badge";
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
  dataProvenance?: DataProvenanceKind;
}

export function WorkspaceHeader({
  breadcrumbItems = [],
  workspace,
  session,
  onRefresh,
  refreshing = false,
  actions,
  context,
  dataProvenance
}: WorkspaceHeaderProps) {
  const viewModel = buildWorkspaceHeaderViewModel({
    workspace,
    session,
    canRefresh: Boolean(onRefresh),
    refreshing,
    dataProvenance
  });
  const routeItems = breadcrumbItems.filter((item) => item.label !== "Workstation");
  const titleItems = routeItems.length ? routeItems : [{ label: viewModel.title, current: true }];

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
          <div className="workstation-header-title-row">
            <nav aria-label="Breadcrumb">
              <h1 className="workstation-route-title font-display font-semibold text-foreground"
                aria-label={titleItems.every((item) => typeof item.label === "string") ? titleItems.map((item) => item.label).join(" › ") : undefined}>
                {titleItems.map((item, index) => (
                  <span key={index} className="workstation-route-segment">
                    {index > 0 ? <ChevronRight className="size-3.5 text-muted-foreground" aria-hidden="true" /> : null}
                    {item.current || index === titleItems.length - 1
                      ? <span aria-current="page">{item.label}</span>
                      : item.href ? <a href={item.href}>{item.label}</a>
                        : item.onClick ? <button type="button" onClick={item.onClick}>{item.label}</button>
                          : <span>{item.label}</span>}
                  </span>
                ))}
              </h1>
            </nav>
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
              <div className="workstation-header-about-panel">
                <p>{viewModel.description}</p>
                {viewModel.badges.map((badge) => (
                  <Badge key={badge.id} variant={badge.variant} aria-label={badge.ariaLabel}>{badge.label}</Badge>
                ))}
              </div>
            </details>
          </div>
        </div>
        <div className="workstation-header-actions">
          {context ? <div className="workstation-header-context">{context}</div> : null}
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
        <span className="sr-only" aria-label={viewModel.sessionPillAriaLabel}>{viewModel.sessionLabel}</span>
      </div>
      <span className="sr-only" aria-live="polite">{viewModel.liveAnnouncement}</span>
    </header>
  );
}
