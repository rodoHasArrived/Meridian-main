import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Card, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import { eyebrowClassName, usesTrackedCaps } from "@/design-system/label-case";
import { AccountingChip } from "./accounting-screen.workbench-context";
import type { buildAccountingLoadingViewState } from "./accounting-screen.view-model";

export function AccountingLoadingPanel({ loading }: { loading: ReturnType<typeof buildAccountingLoadingViewState> }) {
  return (
    <Card
      className="panel-surface-strong"
      role={loading.role}
      aria-busy={loading.ariaBusy}
      aria-live={loading.ariaLive}
      aria-labelledby={loading.titleId}
      aria-describedby={loading.detailId}
    >
      <CardHeader>
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0">
            <div className={eyebrowClassName(loading.eyebrow)}>{loading.eyebrow}</div>
            <CardTitle id={loading.titleId} className="mt-2 text-base">{loading.title}</CardTitle>
            <CardDescription id={loading.detailId} className="mt-2">{loading.detail}</CardDescription>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <AccountingChip label="Route" value={loading.routeLabel} />
            <AccountingChip label="Task mode" value={loading.workstreamLabel} />
          </div>
        </div>
        <div className="mt-4 grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(18rem,0.38fr)]">
          <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3">
            <div className={cn("text-xs font-semibold text-muted-foreground", usesTrackedCaps(loading.statusItemsLabel) && "uppercase")}>{loading.statusItemsLabel}</div>
            <div role="list" className="mt-3 grid gap-2 md:grid-cols-3">
              {loading.statusItems.map((item) => (
                <div key={item.id} role="listitem" className="rounded-md border border-border/60 bg-background/45 px-3 py-2">
                  <div className="text-sm font-semibold text-foreground">{item.label}</div>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">{item.detail}</p>
                </div>
              ))}
            </div>
          </div>
          <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3">
            <div className={cn("text-xs font-semibold text-muted-foreground", usesTrackedCaps(loading.actionsLabel) && "uppercase")}>{loading.actionsLabel}</div>
            <div className="mt-3 grid gap-2">
              {loading.actions.map((action) => (
                <Button key={action.id} asChild size="sm" variant="outline" className="h-auto justify-start py-2 text-left">
                  <Link to={action.href} aria-label={action.ariaLabel}>
                    <span className="min-w-0">
                      <span className="block font-semibold">{action.label}</span>
                      <span className="mt-1 block text-xs font-normal leading-5 text-muted-foreground">{action.detail}</span>
                    </span>
                  </Link>
                </Button>
              ))}
            </div>
          </div>
        </div>
      </CardHeader>
    </Card>
  );
}
