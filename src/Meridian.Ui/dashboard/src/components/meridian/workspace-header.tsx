import type { ReactNode } from "react";
import { AlertTriangle, ChevronRight, RefreshCcw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import type { BreadcrumbItem } from "@/components/ui/breadcrumb";
import { Button } from "@/components/ui/button";
import { WorkspaceOverflowMenu } from "@/components/meridian/workspace-views-menu";
import { buildWorkspaceHeaderViewModel } from "@/components/meridian/workspace-header.view-model";
import type { SessionInfo, WorkspaceSummary } from "@/types";
import { cn } from "@/lib/utils";
import { usesTrackedCaps } from "@/design-system/label-case";

/**
 * Single-row workspace header rendered above every route.
 *
 * Left: the breadcrumb, whose last crumb is the page name and the page heading
 * (`Trading > Orders`). Centre: the operating-scope chip. Right: Refresh, the shell actions
 * (Views) and an overflow menu holding Copy link and the About text. The route body carries the
 * task title ("Orders blotter"); the header never repeats the workspace name as a second heading.
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
 *   breadcrumbItems={[{ label: "Trading", onClick: openTrading }, { label: "Orders" }]}
 *   workspace={workspaceSummary}
 *   session={session}
 *   scope={<OperatingScopeChip viewModel={continuity} onEditOperatingContext={openScopePicker} />}
 *   onRefresh={() => void refresh()}
 *   refreshing={isRefreshing}
 * />
 */
interface WorkspaceHeaderProps {
  /** Ancestors followed by the current page; the last item renders as the heading. */
  breadcrumbItems?: BreadcrumbItem[];
  workspace: WorkspaceSummary;
  session: SessionInfo | null;
  onRefresh?: () => void;
  refreshing?: boolean;
  /** Shell-provided header actions (e.g. Views), rendered after refresh. */
  actions?: ReactNode;
  /** Rarely used actions (e.g. Copy link), rendered inside the overflow menu. */
  overflowActions?: ReactNode;
  /** Closes the overflow menu when the route changes. */
  routeKey?: string;
  /** The operating-scope chip, centred in the row. */
  scope?: ReactNode;
}

export function WorkspaceHeader({
  breadcrumbItems = [],
  workspace,
  session,
  onRefresh,
  refreshing = false,
  actions,
  overflowActions,
  routeKey,
  scope
}: WorkspaceHeaderProps) {
  const currentCrumb = breadcrumbItems.at(-1);
  const ancestors = breadcrumbItems.slice(0, -1);
  const viewModel = buildWorkspaceHeaderViewModel({
    workspace,
    session,
    canRefresh: Boolean(onRefresh),
    refreshing,
    pageTitle: typeof currentCrumb?.label === "string" ? currentCrumb.label : undefined
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
          <span className={cn("text-[10px] font-semibold", usesTrackedCaps(viewModel.liveBanner.label) && "font-mono uppercase tracking-[0.14em]")}>
            {viewModel.liveBanner.label}
          </span>
          <span className="text-xs leading-5 opacity-80">{viewModel.liveBanner.detail}</span>
        </div>
      ) : null}
      {/* One row, in visual and focus order: breadcrumb ending in the page heading, the scope
          chip, then the actions. */}
      <div className="workspace-header-shell workstation-header-row">
        <nav aria-label="Breadcrumb" className="workstation-header-crumbs">
          <ol>
            {ancestors.map((item, index) => (
              <li key={`${index}-${String(item.label)}`}>
                {item.href ? (
                  <a className="workstation-header-crumb" href={item.href}>{item.label}</a>
                ) : (
                  <button type="button" className="workstation-header-crumb" onClick={item.onClick} disabled={!item.onClick}>
                    {item.label}
                  </button>
                )}
                <ChevronRight className="h-3 w-3 shrink-0 text-muted-foreground/60" aria-hidden="true" />
              </li>
            ))}
            <li>
              <h1 className="type-page-title text-foreground" aria-current="page">{currentCrumb?.label ?? viewModel.title}</h1>
              {viewModel.badges.map((badge) => (
                <Badge key={badge.id} variant={badge.variant} aria-label={badge.ariaLabel}>
                  {badge.label}
                </Badge>
              ))}
            </li>
          </ol>
        </nav>
        {scope ? <div className="workstation-header-scope">{scope}</div> : null}
        <div className="workstation-header-actions">
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
          {actions}
          <WorkspaceOverflowMenu routeKey={routeKey}>
            {overflowActions}
            <section className="workstation-header-about" aria-label={viewModel.aboutLabel}>
              <p>{viewModel.description}</p>
              <p className="text-muted-foreground">{viewModel.maturityNote}</p>
            </section>
          </WorkspaceOverflowMenu>
        </div>
        <span className="sr-only" aria-label={viewModel.sessionPillAriaLabel}>{viewModel.sessionLabel}</span>
      </div>
      <span className="sr-only" aria-live="polite">{viewModel.liveAnnouncement}</span>
    </header>
  );
}
