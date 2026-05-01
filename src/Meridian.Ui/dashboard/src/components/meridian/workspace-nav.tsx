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

export function WorkspaceNav() {
  const location = useLocation();
  const viewModel = buildWorkspaceNavViewModel(location.pathname);

  return (
    <aside className="operator-rail" aria-label={`${viewModel.brandTitle} ${viewModel.brandSubtitle}`}>
      <header className="operator-rail-header">
        <div className="operator-rail-brand">
          <span className="operator-rail-mark" aria-hidden="true">M</span>
          <span>
            <p className="operator-rail-title">{viewModel.brandTitle}</p>
            <p className="operator-rail-subtitle">{viewModel.brandSubtitle}</p>
          </span>
        </div>
      </header>

      <div className="operator-rail-section">{viewModel.modelEyebrow}</div>
      <div className="operator-rail-card">
        <p className="text-xs leading-5 text-muted-foreground">
          {viewModel.modelDescription}
        </p>
      </div>

      <section className="operator-rail-current" aria-label={viewModel.currentWorkspace.ariaLabel}>
        <div className="eyebrow-label">Current workspace</div>
        <div className="mt-2 flex items-start justify-between gap-2">
          <p className="operator-rail-current-title">{viewModel.currentWorkspace.label}</p>
          <span className={`operator-nav-status operator-nav-status-${viewModel.currentWorkspace.statusTone}`}>
            <span className="operator-nav-status-dot" aria-hidden="true" />
            {viewModel.currentWorkspace.statusLabel}
          </span>
        </div>
        <p className="mt-2 text-xs leading-5 text-muted-foreground">
          {viewModel.currentWorkspace.description}
        </p>
      </section>

      <nav className="space-y-1" aria-label="Workspaces">
        <div className="operator-rail-section">{viewModel.navEyebrow}</div>
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
                item.active
                  ? "active"
                  : ""
              )}
            >
              <Icon className="h-4 w-4 shrink-0" />
              <span className="min-w-0">
                <span className="block truncate font-semibold">{item.label}</span>
                <span className="block truncate text-[11px] leading-4 text-muted-foreground">{item.description}</span>
              </span>
              <span className={`operator-nav-status operator-nav-status-${item.statusTone}`}>
                <span className="operator-nav-status-dot" aria-hidden="true" />
                {item.statusLabel}
              </span>
            </Link>
          );
        })}
      </nav>

      <div className="operator-rail-callout">
        <div className="eyebrow-label">{viewModel.deliveryEyebrow}</div>
        <div className="mt-2 font-semibold text-foreground">{viewModel.deliveryTitle}</div>
        <p className="mt-2 text-xs leading-5 text-muted-foreground">
          {viewModel.deliveryDescription}
        </p>
      </div>
    </aside>
  );
}
