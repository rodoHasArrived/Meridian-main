import { Filter, LayoutPanelTop, Save, ShieldCheck } from "lucide-react";
import type { ReactNode } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

export interface FinancialRecordExplorerScopeItem {
  id: string;
  label: string;
  value: string;
}

export interface FinancialRecordExplorerSavedView {
  id: string;
  label: string;
  detail: string;
  active?: boolean;
}

export interface FinancialRecordExplorerSummaryItem {
  id: string;
  label: string;
  value: string;
  tone?: "default" | "success" | "warning" | "danger";
}

export interface FinancialRecordExplorerAction {
  id: string;
  label: string;
  href?: string | null;
  ariaLabel?: string;
}

export function FinancialRecordExplorerShell({
  explorerLabel,
  title,
  titleId = "financial-record-explorer-title",
  description,
  scopeItems,
  savedViews,
  summaryItems,
  appliedFilters,
  actions,
  children,
  className
}: {
  explorerLabel: string;
  title: string;
  titleId?: string;
  description: string;
  scopeItems: FinancialRecordExplorerScopeItem[];
  savedViews: FinancialRecordExplorerSavedView[];
  summaryItems: FinancialRecordExplorerSummaryItem[];
  appliedFilters: FinancialRecordExplorerScopeItem[];
  actions?: FinancialRecordExplorerAction[];
  children: ReactNode;
  className?: string;
}) {
  const activeSavedView = savedViews.find((view) => view.active) ?? savedViews[0] ?? null;

  return (
    <section className={cn("workspace-section-band", className)} aria-labelledby={titleId}>
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <p className="eyebrow-label">{explorerLabel}</p>
          <h2 id={titleId} className="workspace-section-title">{title}</h2>
          <p className="workspace-section-summary">{description}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant="default" dot>{activeSavedView?.label ?? "Unsaved view"}</Badge>
          <Button size="sm" variant="outline" disabled disabledReason="Saved view persistence will use the shared explorer view store in the next slice.">
            <Save className="h-3.5 w-3.5" aria-hidden="true" />
            Save view
          </Button>
        </div>
      </div>

      <div className="grid gap-3 xl:grid-cols-[minmax(0,0.72fr)_minmax(260px,0.28fr)]">
        <div className="space-y-3">
          <ExplorerScopeBar items={scopeItems} />
          <SavedViewSelector views={savedViews} />
          <ExplorerSummaryStrip items={summaryItems} />
          <AppliedFilterStrip filters={appliedFilters} />
        </div>
        <div className="rounded-md border border-border/70 bg-secondary/15 p-3">
          <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
            <ShieldCheck className="h-4 w-4 text-primary" aria-hidden="true" />
            Proof drill-through
          </div>
          <p className="mt-1 text-xs leading-5 text-muted-foreground">
            Selected records keep links to source records, supporting documents, approvals, reconciliations, report usage, and audit history in the drawer below.
          </p>
          {actions && actions.length > 0 ? (
            <div className="mt-3 flex flex-wrap gap-2" aria-label={`${title} proof actions`}>
              {actions.map((action) => action.href ? (
                <Button key={action.id} asChild size="sm" variant="outline">
                  <a href={action.href} aria-label={action.ariaLabel ?? action.label}>{action.label}</a>
                </Button>
              ) : (
                <Badge key={action.id} variant="outline">{action.label}</Badge>
              ))}
            </div>
          ) : null}
        </div>
      </div>

      <div className="mt-4">
        {children}
      </div>
    </section>
  );
}

function ExplorerScopeBar({ items }: { items: FinancialRecordExplorerScopeItem[] }) {
  return (
    <dl className="grid gap-2 sm:grid-cols-2 xl:grid-cols-4" aria-label="Explorer scope">
      {items.map((item) => (
        <div key={item.id} className="rounded-md border border-border/70 bg-background/60 px-3 py-2">
          <dt className="text-[11px] font-semibold uppercase text-muted-foreground">{item.label}</dt>
          <dd className="mt-1 truncate font-mono text-sm text-foreground">{item.value}</dd>
        </div>
      ))}
    </dl>
  );
}

function SavedViewSelector({ views }: { views: FinancialRecordExplorerSavedView[] }) {
  return (
    <div className="rounded-md border border-border/70 bg-secondary/15 p-3" aria-label="Saved explorer views">
      <div className="flex items-center gap-2 text-xs font-semibold uppercase text-muted-foreground">
        <LayoutPanelTop className="h-3.5 w-3.5" aria-hidden="true" />
        Saved views
      </div>
      <div className="mt-2 flex flex-wrap gap-2">
        {views.map((view) => (
          <span
            key={view.id}
            className={cn("toolbar-chip", view.active ? "border-primary/35 bg-primary/10 text-primary" : "")}
            aria-current={view.active ? "true" : undefined}
            title={view.detail}
          >
            {view.label}
          </span>
        ))}
      </div>
    </div>
  );
}

function ExplorerSummaryStrip({ items }: { items: FinancialRecordExplorerSummaryItem[] }) {
  return (
    <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-4" aria-label="Explorer summary">
      {items.map((item) => (
        <div key={item.id} className={cn("rounded-md border px-3 py-2", summaryToneClass(item.tone))}>
          <div className="text-[11px] font-semibold uppercase text-muted-foreground">{item.label}</div>
          <div className="mt-1 font-mono text-lg font-semibold">{item.value}</div>
        </div>
      ))}
    </div>
  );
}

function AppliedFilterStrip({ filters }: { filters: FinancialRecordExplorerScopeItem[] }) {
  return (
    <div className="flex flex-wrap items-center gap-2 rounded-md border border-border/70 bg-background/60 px-3 py-2" aria-label="Applied explorer filters">
      <Filter className="h-3.5 w-3.5 text-muted-foreground" aria-hidden="true" />
      {filters.length > 0 ? filters.map((filter) => (
        <span key={filter.id} className="toolbar-chip">
          <span>{filter.label}</span>
          <b>{filter.value}</b>
        </span>
      )) : (
        <span className="text-sm text-muted-foreground">No filters applied.</span>
      )}
    </div>
  );
}

function summaryToneClass(tone: FinancialRecordExplorerSummaryItem["tone"]): string {
  switch (tone) {
    case "success":
      return "border-success/30 bg-success/10 text-success";
    case "warning":
      return "border-warning/30 bg-warning/10 text-warning";
    case "danger":
      return "border-danger/30 bg-danger/10 text-danger";
    default:
      return "border-border/70 bg-background/60 text-foreground";
  }
}
