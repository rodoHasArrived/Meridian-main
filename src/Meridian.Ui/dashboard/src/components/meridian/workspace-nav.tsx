import {
  DatabaseZap,
  FileCheck2,
  FlaskConical,
  Landmark,
  RadioTower,
  Settings,
  WalletCards
} from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { buildWorkspaceNavViewModel } from "@/components/meridian/workspace-nav.view-model";
import { cn } from "@/lib/utils";
import type { WorkspaceKey } from "@/types";

const icons: Record<WorkspaceKey, typeof RadioTower> = {
  trading: RadioTower,
  portfolio: WalletCards,
  accounting: Landmark,
  reporting: FileCheck2,
  strategy: FlaskConical,
  data: DatabaseZap,
  settings: Settings
};

/**
 * Left-rail operator navigation sidebar. Renders as a fixed-width `<aside>` inside the
 * `.workstation-shell` grid and uses `.operator-rail` CSS from the Track B surface system.
 *
 * All data is derived from `useLocation()` via `buildWorkspaceNavViewModel`. Optional props allow
 * host surfaces such as drawers to add wrapper classes and close after navigation.
 *
 * **Sections:**
 * - Brand header with the Meridian mark and subtitle.
 * - Model description card (current AI/strategy model context).
 * - Current workspace panel — shows the active workspace label, status badge, and description.
 * - Workspace nav list — 7 links: Trading, Portfolio, Accounting, Reporting, Strategy, Data, Settings.
 *   Each link shows a status badge (`operator-nav-status-*`) colored by workspace posture.
 * - Delivery callout at the bottom.
 *
 * **Status tones** for nav items and the current workspace panel are one of:
 * `"live"`, `"review"`, `"paper"`, `"preview"`, `"setup"`, or `"muted"` — each has a
 * matching `.operator-nav-status-*` CSS modifier.
 *
 * @example
 * // Mount inside the .workstation-shell grid:
 * <WorkspaceNav />
 */
interface WorkspaceNavProps {
  className?: string;
  onNavigate?: () => void;
}

export function WorkspaceNav({ className, onNavigate }: WorkspaceNavProps) {
  const location = useLocation();
  const viewModel = buildWorkspaceNavViewModel(location.pathname);

  return (
    <aside className={cn("operator-rail", className)} aria-label={`${viewModel.brandTitle} navigation`}>
      <div className="operator-rail-brand-bar">
        <span className="operator-rail-mark" aria-hidden="true">M</span>
        <span>
          <p className="operator-rail-title">{viewModel.brandTitle}</p>
          <p className="operator-rail-subtitle">{viewModel.brandSubtitle}</p>
        </span>
      </div>

      <section className="operator-rail-current" aria-label={viewModel.currentWorkspace.ariaLabel}>
        <div className="operator-rail-section">{viewModel.navEyebrow}</div>
        <div className="operator-rail-current-card">
          <div className="flex items-center justify-between gap-2">
            <div className="operator-rail-current-title">{viewModel.currentWorkspace.label}</div>
            <span className={`operator-nav-status operator-nav-status-${viewModel.currentWorkspace.statusTone}`}>
              <span className="operator-nav-status-dot" aria-hidden="true" />
              {viewModel.currentWorkspace.statusLabel}
            </span>
          </div>
          <p className="operator-rail-current-description">{viewModel.currentWorkspace.description}</p>
          <div className="operator-rail-meta">
            <span className="operator-rail-route" aria-label={viewModel.currentWorkspace.routeAriaLabel}>
              {viewModel.currentWorkspace.route}
            </span>
            <span className="operator-rail-shortcut" aria-label={viewModel.deliveryShortcutAriaLabel}>
              {viewModel.deliveryShortcutLabel}
            </span>
          </div>
        </div>
      </section>

      <nav className="operator-rail-nav" aria-label="Workspaces">
        {viewModel.items.map((item) => {
          const Icon = icons[item.key];
          return (
            <Link
              key={item.key}
              to={item.route}
              aria-current={item.ariaCurrent}
              aria-label={item.ariaLabel}
              className={cn(
                "operator-nav-item focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                item.active ? "active" : ""
              )}
              onClick={onNavigate}
            >
              <Icon className="h-4 w-4 shrink-0" aria-hidden="true" />
              <span className="truncate font-medium">{item.label}</span>
              <span className={`operator-nav-status operator-nav-status-${item.statusTone}`}>
                <span className="operator-nav-status-dot" aria-hidden="true" />
                {item.statusLabel}
              </span>
            </Link>
          );
        })}
      </nav>
    </aside>
  );
}
