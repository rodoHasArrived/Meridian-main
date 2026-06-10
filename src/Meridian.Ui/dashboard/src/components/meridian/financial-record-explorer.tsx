import { Filter, GitBranch, LayoutPanelTop, Save, Search, ShieldCheck } from "lucide-react";
import { useEffect, useMemo, useState, type ReactNode } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import type {
  FinancialRecordExplorer,
  FinancialRecordExplorerProofActionDto,
  FinancialRecordExplorerSavedViewSaveRequest,
  FinancialRecordExplorerSelectedRecordDto,
  FinancialRecordExplorerTone
} from "@/types";

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
  explorer,
  explorerLabel,
  title,
  titleId = "financial-record-explorer-title",
  description,
  scopeItems,
  savedViews,
  summaryItems,
  appliedFilters,
  actions,
  onSaveView,
  children,
  className
}: {
  explorer?: FinancialRecordExplorer | null;
  explorerLabel: string;
  title: string;
  titleId?: string;
  description: string;
  scopeItems: FinancialRecordExplorerScopeItem[];
  savedViews: FinancialRecordExplorerSavedView[];
  summaryItems: FinancialRecordExplorerSummaryItem[];
  appliedFilters: FinancialRecordExplorerScopeItem[];
  actions?: FinancialRecordExplorerAction[];
  onSaveView?: (request: FinancialRecordExplorerSavedViewSaveRequest) => void | Promise<void>;
  children?: ReactNode;
  className?: string;
}) {
  const resolvedScopeItems = explorer?.scopeItems ?? scopeItems;
  const resolvedSavedViews = explorer
    ? explorer.savedViews.map((view) => ({
        id: view.savedViewId,
        label: view.name,
        detail: view.description ?? (view.isSystem ? "System view" : "Operator saved view"),
        active: view.isActive
      }))
    : savedViews;
  const resolvedSummaryItems = explorer
    ? explorer.summaryItems.map((item) => ({
        id: item.id,
        label: item.label,
        value: item.value,
        tone: normalizeTone(item.tone)
      }))
    : summaryItems;
  const resolvedAppliedFilters = explorer
    ? explorer.filters
        .filter((filter) => filter.isActive)
        .map((filter) => ({ id: filter.id, label: filter.label, value: filter.value }))
    : appliedFilters;
  const activeSavedView = resolvedSavedViews.find((view) => view.active) ?? resolvedSavedViews[0] ?? null;
  const [selectedSavedViewId, setSelectedSavedViewId] = useState(activeSavedView?.id ?? "");
  const [searchText, setSearchText] = useState("");
  const [selectedRecordId, setSelectedRecordId] = useState(explorer?.selectedRecord?.recordId ?? explorer?.rows[0]?.recordId ?? "");

  useEffect(() => {
    setSelectedSavedViewId(activeSavedView?.id ?? "");
  }, [activeSavedView?.id]);

  useEffect(() => {
    setSelectedRecordId(explorer?.selectedRecord?.recordId ?? explorer?.rows[0]?.recordId ?? "");
    setSearchText("");
  }, [explorer?.explorerId, explorer?.rows]);

  const filteredRows = useMemo(() => {
    if (!explorer) {
      return [];
    }

    const query = searchText.trim().toLowerCase();
    if (!query) {
      return explorer.rows;
    }

    return explorer.rows.filter((row) =>
      row.label.toLowerCase().includes(query) ||
      row.recordType.toLowerCase().includes(query) ||
      row.status.toLowerCase().includes(query) ||
      row.cells.some((cell) => (cell.displayValue ?? cell.value).toLowerCase().includes(query))
    );
  }, [explorer, searchText]);

  const selectedRecord = useMemo(() => {
    if (!explorer) {
      return null;
    }

    return explorer.rows.find((row) => row.recordId === selectedRecordId)?.detail
      ?? filteredRows[0]?.detail
      ?? explorer.selectedRecord
      ?? null;
  }, [explorer, filteredRows, selectedRecordId]);

  const resolvedActions = explorer
    ? mapProofActions(selectedRecord?.proofActions ?? [])
    : actions;
  const hasMaterialViewChange = Boolean(explorer && (searchText.trim() || selectedSavedViewId !== (activeSavedView?.id ?? "")));

  function handleSaveView() {
    if (!explorer || !onSaveView || !hasMaterialViewChange) {
      return;
    }

    void onSaveView({
      name: searchText.trim() ? `${explorer.title} search` : `${explorer.title} view`,
      description: searchText.trim() ? `Search: ${searchText.trim()}` : "Operator-created explorer view.",
      filters: searchText.trim()
        ? [{ id: "search", label: "Search", value: searchText.trim(), operator: "contains", isActive: true }]
        : explorer.filters,
      columnIds: explorer.columns.map((column) => column.columnId)
    });
  }

  return (
    <section className={cn("workspace-section-band", className)} aria-labelledby={titleId}>
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <p className="eyebrow-label">{explorer?.explorerLabel ?? explorerLabel}</p>
          <h2 id={titleId} className="workspace-section-title">{explorer?.title ?? title}</h2>
          <p className="workspace-section-summary">{explorer?.description ?? description}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant="default" dot>{activeSavedView?.label ?? "Unsaved view"}</Badge>
          <Button
            size="sm"
            variant="outline"
            disabled={!hasMaterialViewChange || !onSaveView}
            disabledReason={!onSaveView ? "Saved view persistence is unavailable for this surface." : "Change search or saved-view state before saving."}
            onClick={handleSaveView}
          >
            <Save className="h-3.5 w-3.5" aria-hidden="true" />
            Save view
          </Button>
        </div>
      </div>

      <div className="grid gap-3 xl:grid-cols-[minmax(0,0.72fr)_minmax(260px,0.28fr)]">
        <div className="space-y-3">
          <ExplorerScopeBar items={resolvedScopeItems} />
          <SavedViewSelector views={resolvedSavedViews} selectedViewId={selectedSavedViewId} onSelect={setSelectedSavedViewId} />
          <ExplorerSummaryStrip items={resolvedSummaryItems} />
          <AppliedFilterStrip filters={resolvedAppliedFilters} />
        </div>
        <div className="rounded-md border border-border/70 bg-secondary/15 p-3">
          <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
            <ShieldCheck className="h-4 w-4 text-primary" aria-hidden="true" />
            Proof drill-through
          </div>
          <p className="mt-1 text-xs leading-5 text-muted-foreground">
            Selected records keep links to source records, supporting documents, approvals, reconciliations, report usage, and audit history in the drawer below.
          </p>
          {resolvedActions && resolvedActions.length > 0 ? (
            <div className="mt-3 flex flex-wrap gap-2" aria-label={`${explorer?.title ?? title} proof actions`}>
              {resolvedActions.map((action) => action.href ? (
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

      {explorer ? (
        <div className="mt-4">
          <ExplorerRecordWorkbench
            explorer={explorer}
            rows={filteredRows}
            selectedRecord={selectedRecord}
            searchText={searchText}
            onSearchTextChange={setSearchText}
            onSelectRecord={setSelectedRecordId}
          />
        </div>
      ) : null}

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

function SavedViewSelector({
  views,
  selectedViewId,
  onSelect
}: {
  views: FinancialRecordExplorerSavedView[];
  selectedViewId: string;
  onSelect: (viewId: string) => void;
}) {
  return (
    <div className="rounded-md border border-border/70 bg-secondary/15 p-3" aria-label="Saved explorer views">
      <div className="flex items-center gap-2 text-xs font-semibold uppercase text-muted-foreground">
        <LayoutPanelTop className="h-3.5 w-3.5" aria-hidden="true" />
        Saved views
      </div>
      <div className="mt-2 flex flex-wrap gap-2">
        {views.map((view) => (
          <button
            type="button"
            key={view.id}
            className={cn("toolbar-chip", selectedViewId === view.id ? "border-primary/35 bg-primary/10 text-primary" : "")}
            aria-pressed={selectedViewId === view.id}
            title={view.detail}
            onClick={() => onSelect(view.id)}
          >
            {view.label}
          </button>
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

function ExplorerRecordWorkbench({
  explorer,
  rows,
  selectedRecord,
  searchText,
  onSearchTextChange,
  onSelectRecord
}: {
  explorer: FinancialRecordExplorer;
  rows: FinancialRecordExplorer["rows"];
  selectedRecord: FinancialRecordExplorerSelectedRecordDto | null;
  searchText: string;
  onSearchTextChange: (value: string) => void;
  onSelectRecord: (recordId: string) => void;
}) {
  return (
    <div className="grid gap-4 xl:grid-cols-[minmax(0,1.25fr)_minmax(280px,0.75fr)]">
      <div className="rounded-md border border-border/70 bg-background/55">
        <div className="border-b border-border/70 p-3">
          <div className="flex flex-col gap-2 md:flex-row md:items-center md:justify-between">
            <div>
              <div className="text-sm font-semibold text-foreground">{explorer.recordSetLabel}</div>
              <div className="mt-1 text-xs text-muted-foreground">{rows.length} visible row{rows.length === 1 ? "" : "s"}</div>
            </div>
            <label className="relative min-w-0 md:w-72">
              <span className="sr-only">Search explorer records</span>
              <Search className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
              <input
                value={searchText}
                onChange={(event) => onSearchTextChange(event.target.value)}
                placeholder="Search records"
                disabled={explorer.isBlocked}
                className="min-h-9 w-full rounded-md border border-border bg-background py-2 pl-8 pr-3 text-sm text-foreground outline-none transition focus:border-primary focus:ring-2 focus:ring-primary/25 disabled:cursor-not-allowed disabled:opacity-60"
              />
            </label>
          </div>
          {explorer.isBlocked ? (
            <p role="alert" className="mt-3 rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-sm text-danger">
              {explorer.blockedReason ?? explorer.emptyDetail}
            </p>
          ) : null}
        </div>
        {rows.length > 0 ? (
          <div className="overflow-x-auto">
            <table className="min-w-full border-collapse text-sm" aria-label={`${explorer.title} records`}>
              <thead>
                <tr className="border-b border-border/70 text-left text-[11px] uppercase text-muted-foreground">
                  {explorer.columns.map((column) => (
                    <th key={column.columnId} scope="col" className="px-3 py-2 font-semibold">
                      {column.header}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr
                    key={row.recordId}
                    className={cn(
                      "cursor-pointer border-b border-border/50 transition-colors hover:bg-secondary/30",
                      selectedRecord?.recordId === row.recordId ? "bg-primary/10" : ""
                    )}
                    aria-selected={selectedRecord?.recordId === row.recordId}
                    onClick={() => onSelectRecord(row.recordId)}
                  >
                    {explorer.columns.map((column) => {
                      const cell = row.cells.find((entry) => entry.columnId === column.columnId);
                      const value = cell?.displayValue ?? cell?.value ?? "—";
                      return (
                        <td key={`${row.recordId}-${column.columnId}`} className="max-w-[18rem] truncate px-3 py-2 align-top">
                          {cell?.href ? (
                            <a className={cn("font-medium underline-offset-2 hover:underline", toneTextClass(cell.tone))} href={cell.href}>
                              {value}
                            </a>
                          ) : (
                            <span className={toneTextClass(cell?.tone)}>{value}</span>
                          )}
                        </td>
                      );
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <div role="status" className="p-4 text-sm text-muted-foreground">
            <div className="font-semibold text-foreground">{explorer.emptyTitle}</div>
            <p className="mt-1">{explorer.emptyDetail}</p>
          </div>
        )}
      </div>
      <RecordProofDrawer record={selectedRecord} explorerTitle={explorer.title} />
    </div>
  );
}

function RecordProofDrawer({
  record,
  explorerTitle
}: {
  record: FinancialRecordExplorerSelectedRecordDto | null;
  explorerTitle: string;
}) {
  if (!record) {
    return (
      <aside className="rounded-md border border-border/70 bg-secondary/15 p-4" role="complementary" aria-label={`${explorerTitle} selected record`}>
        <div className="eyebrow-label">Record proof</div>
        <h3 className="mt-2 text-sm font-semibold text-foreground">No record selected</h3>
        <p className="mt-2 text-sm leading-6 text-muted-foreground">Select a source-backed row to inspect proof actions, relationships, and graph nodes.</p>
      </aside>
    );
  }

  return (
    <aside className="rounded-md border border-border/70 bg-secondary/15 p-4" role="complementary" aria-label={`${record.title} proof detail`}>
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="eyebrow-label">{record.recordType}</div>
          <h3 className="mt-2 text-sm font-semibold text-foreground">{record.title}</h3>
          <p className="mt-1 break-words font-mono text-xs text-muted-foreground">{record.subtitle}</p>
        </div>
        <Badge variant={record.tone === "default" ? "outline" : normalizeTone(record.tone)}>{record.status}</Badge>
      </div>
      <p className="mt-3 text-sm leading-6 text-muted-foreground">{record.description}</p>
      <dl className="mt-4 grid gap-2">
        {record.fields.map((field) => (
          <div key={`${field.label}-${field.value}`} className="grid grid-cols-[minmax(0,0.7fr)_minmax(0,1fr)] gap-3 rounded-md border border-border/60 bg-background/50 px-3 py-2">
            <dt className="text-xs text-muted-foreground">{field.label}</dt>
            <dd className={cn("text-right font-mono text-xs", toneTextClass(field.tone))}>{field.value}</dd>
          </div>
        ))}
      </dl>
      <RelationshipList title="Used In" items={record.usedIn} />
      <RelationshipList title="Impacts" items={record.impacts} />
      <div className="mt-4 rounded-md border border-border/70 bg-background/50 p-3">
        <div className="flex items-center gap-2 text-xs font-semibold uppercase text-muted-foreground">
          <GitBranch className="h-3.5 w-3.5" aria-hidden="true" />
          Record graph
        </div>
        <div className="mt-2 flex flex-wrap gap-2">
          {record.recordGraph.nodes.length > 0 ? record.recordGraph.nodes.map((node) => (
            <span key={node.nodeId} className={cn("toolbar-chip", node.tone === "warning" ? "border-warning/30 bg-warning/10 text-warning" : "")}>
              {node.label}
            </span>
          )) : <span className="text-xs text-muted-foreground">No graph nodes.</span>}
        </div>
      </div>
    </aside>
  );
}

function RelationshipList({
  title,
  items
}: {
  title: string;
  items: FinancialRecordExplorerSelectedRecordDto["usedIn"];
}) {
  return (
    <div className="mt-4 rounded-md border border-border/70 bg-background/50 p-3">
      <h4 className="text-sm font-semibold text-foreground">{title}</h4>
      {items.length > 0 ? (
        <div className="mt-2 space-y-2">
          {items.map((item) => (
            <div key={item.relationshipId} className="rounded-md border border-border/60 bg-secondary/20 px-3 py-2">
              <div className="flex items-start justify-between gap-3">
                <span className="min-w-0 text-sm font-medium text-foreground">{item.label}</span>
                <Badge variant={item.tone === "default" ? "outline" : normalizeTone(item.tone)}>{item.targetType}</Badge>
              </div>
              <p className="mt-1 text-xs leading-5 text-muted-foreground">{item.detail}</p>
              {item.href ? (
                <a className="mt-2 inline-block text-xs text-primary underline-offset-2 hover:underline" href={item.href}>
                  Open linked record
                </a>
              ) : null}
            </div>
          ))}
        </div>
      ) : (
        <p className="mt-2 text-xs text-muted-foreground">No relationships retained for this record.</p>
      )}
    </div>
  );
}

function mapProofActions(actions: FinancialRecordExplorerProofActionDto[]): FinancialRecordExplorerAction[] {
  return actions.map((action) => ({
    id: action.actionId,
    label: action.isEnabled ? action.label : `${action.label} unavailable`,
    href: action.isEnabled ? action.href : null,
    ariaLabel: action.disabledReason ?? action.label
  }));
}

function normalizeTone(tone: string | undefined): FinancialRecordExplorerTone {
  return tone === "success" || tone === "warning" || tone === "danger" ? tone : "default";
}

function toneTextClass(tone: string | undefined): string {
  switch (tone) {
    case "success":
      return "text-success";
    case "warning":
      return "text-warning";
    case "danger":
      return "text-danger";
    default:
      return "text-foreground";
  }
}
