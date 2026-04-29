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
      <div className="operator-rail-section">{viewModel.modelEyebrow}</div>
      <div className="mx-2 mb-3 rounded-lg border border-border/80 bg-card px-3 py-3 shadow-panel">
        <p className="text-xs leading-5 text-muted-foreground">
          {viewModel.modelDescription}
        </p>
      </div>

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
              <span className="operator-nav-status">{item.statusLabel}</span>
            </Link>
          );
        })}
      </nav>

      <div className="mx-2 mt-5 rounded-lg border border-border bg-secondary/35 px-3 py-3 text-sm">
        <div className="eyebrow-label">{viewModel.deliveryEyebrow}</div>
        <div className="mt-2 font-semibold text-foreground">{viewModel.deliveryTitle}</div>
        <p className="mt-2 text-xs leading-5 text-muted-foreground">
          {viewModel.deliveryDescription}
        </p>
      </div>
    </aside>
  );
}
