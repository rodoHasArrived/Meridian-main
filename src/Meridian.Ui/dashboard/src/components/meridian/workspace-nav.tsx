import {
  BarChart3,
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
    <aside className="panel-surface-strong flex min-h-[calc(100vh-3rem)] w-full flex-col gap-6 p-5 lg:w-[320px]">
      <div className="space-y-4">
        <div className="flex items-center gap-3">
          <div className="flex h-11 w-11 items-center justify-center rounded-lg border border-primary/30 bg-primary/12 text-primary">
            <BarChart3 className="h-6 w-6" />
          </div>
          <div>
            <div className="font-display text-lg font-semibold">{viewModel.brandTitle}</div>
            <div className="text-sm text-muted-foreground">{viewModel.brandSubtitle}</div>
          </div>
        </div>
        <div className="rounded-lg border border-border/80 bg-secondary/35 px-4 py-4">
          <div className="eyebrow-label">{viewModel.modelEyebrow}</div>
          <p className="mt-2 text-sm leading-6 text-muted-foreground">
            {viewModel.modelDescription}
          </p>
        </div>
      </div>

      <nav className="space-y-2" aria-label="Workspaces">
        {viewModel.items.map((item) => {
          const Icon = icons[item.key];
          return (
            <Link
              key={item.key}
              to={item.route}
              aria-current={item.ariaCurrent}
              aria-label={item.ariaLabel}
              className={cn(
                "flex items-start gap-3 rounded-lg border px-4 py-3 transition-all duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                item.active
                  ? "border-primary/30 bg-primary/10 text-foreground shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]"
                  : "border-transparent bg-transparent text-muted-foreground hover:border-border hover:bg-secondary/55 hover:text-foreground"
              )}
            >
              <Icon className="mt-0.5 h-4 w-4 shrink-0" />
              <span className="space-y-1">
                <span className="block text-sm font-semibold">{item.label}</span>
                <span className="block text-xs leading-5">{item.statusLabel}</span>
              </span>
            </Link>
          );
        })}
      </nav>

      <div className="mt-auto rounded-lg border border-border bg-secondary/45 px-4 py-5 text-sm text-slate-50">
        <div className="eyebrow-label">{viewModel.deliveryEyebrow}</div>
        <div className="mt-3 font-semibold text-foreground">{viewModel.deliveryTitle}</div>
        <p className="mt-2 leading-6 text-muted-foreground">
          {viewModel.deliveryDescription}
        </p>
      </div>
    </aside>
  );
}
