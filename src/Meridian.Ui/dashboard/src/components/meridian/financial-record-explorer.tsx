import { Filter, GitBranch, LayoutPanelTop, Link2, Save, Search, ShieldCheck } from "lucide-react";
import { useEffect, useMemo, useState, type ReactNode } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import type {
  FinancialRecordExplorerDto,
  FinancialRecordExplorerFilterDto,
  FinancialRecordExplorerRowDto,
  FinancialRecordExplorerSavedViewSaveRequestDto,
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
  explorerLabel,
  title,
  titleId = "financial-record-explorer-title",
  description,
  scopeItems,
  savedViews,
  summaryItems,
  appliedFilters,
  actions,
  explorer,
  onSaveView,
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
  explorer?: FinancialRecordExplorerDto | null;
  onSaveView?: (request: FinancialRecordExplorerSavedViewSaveRequestDto) => void | Promise<void>;
  children: ReactNode;
  className?: string;
}) {
  const dtoMode = explorer ?? null;
  const normalizedViews = dtoMode
    ? dtoMode.savedViews.map((view) => ({
        id: view.viewId,
        label: view.label,
        detail: view.description,
        active: view.isActive
      }))
    : savedViews;
  const activeSavedView = normalizedViews.find((view) => view.active) ?? normalizedViews[0] ?? null;
  const [selectedViewId, setSelectedViewId] = useState(activeSavedView?.id ?? "");
  const [searchText, setSearchText] = useState("");
  const [selectedRecordId, setSelectedRecordId] = useState(dtoMode?.selectedRecord?.recordId ?? dtoMode?.rows[0]?.recordId ?? "");
  const [saving, setSaving] = useState(false);
  const selectedDtoSavedView = useMemo(
    () => dtoMode?.savedViews.find((view) => view.viewId === selectedViewId) ?? null,
    [dtoMode?.savedViews, selectedViewId]
  );

  useEffect(() => {
    setSelectedViewId(activeSavedView?.id ?? "");
    setSearchText("");
    setSelectedRecordId(dtoMode?.selectedRecord?.recordId ?? dtoMode?.rows[0]?.recordId ?? "");
  }, [activeSavedView?.id, dtoMode?.explorerId, dtoMode?.selectedRecord?.recordId, dtoMode?.rows]);

  function handleSelectSavedView(viewId: string) {
    setSelectedViewId(viewId);
    const savedView = dtoMode?.savedViews.find((view) => view.viewId === viewId);
    if (savedView) {
      setSearchText(savedView.searchText ?? "");
    }
  }

  const selectedFilters = selectedDtoSavedView?.filters ?? [];
  const selectedColumnIds = selectedDtoSavedView?.columnIds?.filter((columnId) => columnId.trim().length > 0) ?? [];
  const visibleColumns = useMemo(() => {
    if (!dtoMode || selectedColumnIds.length === 0) {
      return dtoMode?.columns ?? [];
    }

    const selected = new Set(selectedColumnIds.map((columnId) => columnId.toLowerCase()));
    const columns = dtoMode.columns.filter((column) => selected.has(column.columnId.toLowerCase()));
    return columns.length > 0 ? columns : dtoMode.columns;
  }, [dtoMode, selectedColumnIds]);

  const rows = useMemo(() => {
    if (!dtoMode) {
      return [];
    }

    const query = searchText.trim().toLowerCase();
    return dtoMode.rows.filter((row) =>
      rowMatchesSavedViewFilters(row, selectedFilters) &&
      (!query || rowMatchesSearch(row, query))
    );
  }, [dtoMode, searchText, selectedFilters]);

  const selectedRow = rows.find((row) => row.recordId === selectedRecordId) ?? rows[0] ?? null;
  const selectedRecord = selectedRow?.detail ?? null;
  const materialChange = Boolean(dtoMode && onSaveView && (searchText.trim() || (selectedViewId && selectedViewId !== activeSavedView?.id)));
  const headerTitle = dtoMode?.title ?? title;
  const headerDescription = dtoMode?.description ?? description;

  async function handleSaveView() {
    if (!dtoMode || !onSaveView || !materialChange) {
      return;
    }

    const selectedView = dtoMode.savedViews.find((view) => view.viewId === selectedViewId);
    const filters = selectedView?.filters ?? dtoMode.filters;
    setSaving(true);
    try {
      await onSaveView({
        label: `Explorer view ${new Date().toISOString().slice(0, 16).replace("T", " ")}`,
        description: searchText.trim()
          ? `Search: ${searchText.trim()}`
          : `Saved from ${selectedView?.label ?? "current explorer filters"}.`,
        searchText,
        filters,
        columnIds: selectedView?.columnIds ?? []
      });
      setSearchText("");
    } finally {
      setSaving(false);
    }
  }

  return (
    <section className={cn("workspace-section-band", className)} aria-labelledby={titleId}>
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <p className="eyebrow-label">{explorerLabel}</p>
          <h2 id={titleId} className="workspace-section-title">{headerTitle}</h2>
          <p className="workspace-section-summary">{headerDescription}</p>
          {dtoMode ? (
            <p className="mt-2 text-xs text-muted-foreground">{dtoMode.sourceState}</p>
          ) : null}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant={dtoMode?.isBlocked ? "danger" : "default"} dot>{selectedDtoSavedView?.label ?? activeSavedView?.label ?? "Unsaved view"}</Badge>
          <Button
            size="sm"
            variant="outline"
            disabled={!materialChange || saving || dtoMode?.isBlocked}
            disabledReason={dtoMode?.isBlocked ? dtoMode.blockedReason : "Change search text or select a different saved view before saving."}
            busy={saving}
            onClick={() => void handleSaveView()}
          >
            <Save className="h-3.5 w-3.5" aria-hidden="true" />
            Save view
          </Button>
        </div>
      </div>

      <div className="grid gap-3 xl:grid-cols-[minmax(0,0.72fr)_minmax(260px,0.28fr)]">
        <div className="space-y-3">
          <ExplorerScopeBar items={dtoMode ? dtoMode.scopeItems.map((item) => ({ id: item.label, label: item.label, value: item.value })) : scopeItems} />
          <SavedViewSelector views={normalizedViews} selectedViewId={selectedViewId} onSelect={handleSelectSavedView} />
          <ExplorerSummaryStrip items={dtoMode ? dtoMode.summaryItems.map((item) => ({ id: item.label, label: item.label, value: item.value, tone: normalizeTone(item.tone) })) : summaryItems} />
          <AppliedFilterStrip filters={dtoMode ? selectedFilters.length > 0 ? selectedFilters : dtoMode.filters : appliedFilters.map((filter) => ({ filterId: filter.id, label: filter.label, value: filter.value, operator: "equals", tone: "Default" as FinancialRecordExplorerTone }))} />
        </div>
        <ProofSummary title={headerTitle} actions={actions} explorer={dtoMode} />
      </div>

      {dtoMode ? (
        <div className="mt-4 grid gap-4 xl:grid-cols-[minmax(0,1fr)_360px]">
          <div className="space-y-3">
            <div className="flex flex-wrap items-center gap-2 rounded-md border border-border/70 bg-background/60 px-3 py-2">
              <Search className="h-3.5 w-3.5 text-muted-foreground" aria-hidden="true" />
              <input
                className="min-w-[220px] flex-1 bg-transparent text-sm outline-none placeholder:text-muted-foreground"
                value={searchText}
                onChange={(event) => setSearchText(event.target.value)}
                placeholder="Search financial records"
                aria-label={`Search ${headerTitle}`}
              />
              {dtoMode.isBlocked ? <Badge variant="danger">Blocked</Badge> : <Badge variant="outline">{rows.length} rows</Badge>}
            </div>
            <ExplorerGrid explorer={dtoMode} rows={rows} columns={visibleColumns} selectedRecordId={selectedRow?.recordId ?? null} onSelect={setSelectedRecordId} />
            <RecordGraph explorer={dtoMode} />
          </div>
          <ProofDrawer record={selectedRecord} blockedReason={dtoMode.isBlocked ? dtoMode.blockedReason : ""} />
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
            key={view.id}
            type="button"
            className={cn("toolbar-chip", selectedViewId === view.id || view.active ? "border-primary/35 bg-primary/10 text-primary" : "")}
            aria-current={selectedViewId === view.id ? "true" : undefined}
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

function AppliedFilterStrip({ filters }: { filters: FinancialRecordExplorerFilterDto[] }) {
  return (
    <div className="flex flex-wrap items-center gap-2 rounded-md border border-border/70 bg-background/60 px-3 py-2" aria-label="Applied explorer filters">
      <Filter className="h-3.5 w-3.5 text-muted-foreground" aria-hidden="true" />
      {filters.length > 0 ? filters.map((filter) => (
        <span key={filter.filterId} className="toolbar-chip">
          <span>{filter.label}</span>
          <b>{filter.value}</b>
        </span>
      )) : (
        <span className="text-sm text-muted-foreground">No filters applied.</span>
      )}
    </div>
  );
}

function ProofSummary({
  title,
  actions,
  explorer
}: {
  title: string;
  actions?: FinancialRecordExplorerAction[];
  explorer: FinancialRecordExplorerDto | null;
}) {
  const proofActions = explorer?.proofActions ?? [];

  return (
    <div className="rounded-md border border-border/70 bg-secondary/15 p-3">
      <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
        <ShieldCheck className="h-4 w-4 text-primary" aria-hidden="true" />
        Proof drill-through
      </div>
      <p className="mt-1 text-xs leading-5 text-muted-foreground">
        Selected records keep links to source records, supporting documents, approvals, reconciliations, report usage, and audit history in the drawer below.
      </p>
      <div className="mt-3 flex flex-wrap gap-2" aria-label={`${title} proof actions`}>
        {proofActions.length > 0 ? proofActions.map((action) => (
          <ProofActionButton key={action.actionId} action={action} />
        )) : actions && actions.length > 0 ? actions.map((action) => action.href ? (
          <Button key={action.id} asChild size="sm" variant="outline">
            <a href={action.href} aria-label={action.ariaLabel ?? action.label}>{action.label}</a>
          </Button>
        ) : (
          <Badge key={action.id} variant="outline">{action.label}</Badge>
        )) : null}
      </div>
    </div>
  );
}

function ExplorerGrid({
  explorer,
  rows,
  columns,
  selectedRecordId,
  onSelect
}: {
  explorer: FinancialRecordExplorerDto;
  rows: FinancialRecordExplorerRowDto[];
  columns: FinancialRecordExplorerDto["columns"];
  selectedRecordId: string | null;
  onSelect: (recordId: string) => void;
}) {
  if (explorer.isBlocked || rows.length === 0) {
    return (
      <div className="rounded-md border border-border/70 bg-background/60 p-6 text-sm text-muted-foreground" role="status">
        {explorer.isBlocked ? explorer.blockedReason : "No source-backed records are available for this explorer."}
      </div>
    );
  }

  return (
    <div className="overflow-x-auto rounded-md border border-border/70">
      <table className="min-w-full text-sm">
        <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
          <tr>
            {columns.map((column) => (
              <th key={column.columnId} className={cn("px-3 py-2 text-left", column.isRightAligned ? "text-right" : "")}>{column.header}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr
              key={row.recordId}
              className={cn("cursor-pointer border-t border-border/60 hover:bg-secondary/25", selectedRecordId === row.recordId ? "bg-primary/8" : "")}
              onClick={() => onSelect(row.recordId)}
            >
              {columns.map((column) => {
                const cell = row.cells.find((candidate) => candidate.columnId === column.columnId);
                return (
                  <td key={column.columnId} className={cn("px-3 py-2 align-top", column.isRightAligned ? "text-right font-mono" : "")}>
                    {cell?.linkHref ? (
                      <a href={cell.linkHref} className="font-medium text-primary hover:underline" onClick={(event) => event.stopPropagation()}>
                        {cell.displayValue}
                      </a>
                    ) : (
                      <span className={cn(toneTextClass(cell?.tone))}>{cell?.displayValue ?? "-"}</span>
                    )}
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function rowMatchesSavedViewFilters(row: FinancialRecordExplorerRowDto, filters: FinancialRecordExplorerFilterDto[]): boolean {
  if (filters.length === 0) {
    return true;
  }

  return filters.every((filter) => {
    const expected = normalizeSearchToken(filter.value);
    if (!expected) {
      return true;
    }

    const filterId = normalizeSearchToken(filter.filterId);
    const directCell = filterId ? row.cells.find((cell) => normalizeSearchToken(cell.columnId) === filterId) : undefined;
    if (directCell) {
      return cellMatchesToken(directCell, expected);
    }

    return rowMatchesSearch(row, expected);
  });
}

function rowMatchesSearch(row: FinancialRecordExplorerRowDto, query: string): boolean {
  const token = normalizeSearchToken(query);
  if (!token) {
    return true;
  }

  return [row.label, row.recordType, row.source, row.status].some((value) => normalizeSearchToken(value).includes(token)) ||
    row.cells.some((cell) => cellMatchesToken(cell, token));
}

function cellMatchesToken(cell: FinancialRecordExplorerRowDto["cells"][number], token: string): boolean {
  return [cell.displayValue, cell.rawValue].some((value) => normalizeSearchToken(value).includes(token));
}

function normalizeSearchToken(value: string): string {
  return value.trim().toLowerCase();
}

function ProofDrawer({
  record,
  blockedReason
}: {
  record: FinancialRecordExplorerSelectedRecordDto | null;
  blockedReason: string;
}) {
  if (!record) {
    return (
      <aside className="rounded-md border border-border/70 bg-background/60 p-4 text-sm text-muted-foreground">
        {blockedReason || "Select a source-backed row to inspect fields, proof actions, Used In, and Impacts."}
      </aside>
    );
  }

  return (
    <aside className="space-y-3 rounded-md border border-border/70 bg-background/60 p-4" aria-label={`${record.title} proof detail`}>
      <div>
        <Badge variant={toneToBadge(record.tone)}>{record.recordType}</Badge>
        <h3 className="mt-3 text-base font-semibold text-foreground">{record.title}</h3>
        <p className="mt-1 text-xs text-muted-foreground">{record.subtitle}</p>
        <p className="mt-2 text-sm leading-6 text-muted-foreground">{record.description}</p>
      </div>
      <ProofActionList actions={record.proofActions} />
      <FactList title="Fields" items={record.fields} />
      <RelationshipList title="Used In" items={record.usedIn} />
      <RelationshipList title="Impacts" items={record.impacts} />
      {record.fullRecordHref ? (
        <Button asChild size="sm" variant="outline">
          <a href={record.fullRecordHref}>
            <Link2 className="h-3.5 w-3.5" aria-hidden="true" />
            Full record
          </a>
        </Button>
      ) : null}
    </aside>
  );
}

function ProofActionList({ actions }: { actions: FinancialRecordExplorerSelectedRecordDto["proofActions"] }) {
  if (actions.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-wrap gap-2">
      {actions.map((action) => <ProofActionButton key={action.actionId} action={action} />)}
    </div>
  );
}

function ProofActionButton({ action }: { action: FinancialRecordExplorerSelectedRecordDto["proofActions"][number] }) {
  if (!action.isEnabled || !action.href) {
    return (
      <Button size="sm" variant="outline" disabled disabledReason={action.disabledReason || action.description}>
        {action.label}
      </Button>
    );
  }

  return (
    <Button asChild size="sm" variant="outline">
      <a href={action.href}>{action.label}</a>
    </Button>
  );
}

function FactList({ title, items }: { title: string; items: FinancialRecordExplorerSelectedRecordDto["fields"] }) {
  if (items.length === 0) {
    return null;
  }

  return (
    <dl className="grid gap-2">
      <dt className="text-xs font-semibold uppercase text-muted-foreground">{title}</dt>
      {items.map((item) => (
        <div key={`${item.label}-${item.value}`} className="rounded-md border border-border/60 px-3 py-2">
          <dt className="text-[11px] text-muted-foreground">{item.label}</dt>
          <dd className={cn("mt-1 font-mono text-sm", toneTextClass(item.tone))}>{item.value}</dd>
          {item.detail ? <p className="mt-1 text-xs text-muted-foreground">{item.detail}</p> : null}
        </div>
      ))}
    </dl>
  );
}

function RelationshipList({
  title,
  items
}: {
  title: string;
  items: FinancialRecordExplorerSelectedRecordDto["usedIn"];
}) {
  if (items.length === 0) {
    return null;
  }

  return (
    <section>
      <h4 className="text-xs font-semibold uppercase text-muted-foreground">{title}</h4>
      <div className="mt-2 space-y-2">
        {items.map((item) => (
          <div key={item.relationshipId} className="rounded-md border border-border/60 px-3 py-2">
            <div className="flex items-center justify-between gap-2">
              <span className="font-medium text-foreground">{item.label}</span>
              <Badge variant={toneToBadge(item.tone)}>{item.tone}</Badge>
            </div>
            <p className="mt-1 text-xs leading-5 text-muted-foreground">{item.description}</p>
          </div>
        ))}
      </div>
    </section>
  );
}

function RecordGraph({ explorer }: { explorer: FinancialRecordExplorerDto }) {
  if (explorer.recordGraph.nodes.length === 0) {
    return null;
  }

  return (
    <section className="rounded-md border border-border/70 bg-secondary/15 p-3" aria-label="Record graph">
      <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
        <GitBranch className="h-4 w-4 text-primary" aria-hidden="true" />
        Record graph
      </div>
      <div className="mt-3 flex flex-wrap gap-2">
        {explorer.recordGraph.nodes.slice(0, 18).map((node) => (
          <Badge key={node.nodeId} variant={toneToBadge(node.tone)}>{node.label}</Badge>
        ))}
      </div>
    </section>
  );
}

function normalizeTone(tone?: FinancialRecordExplorerTone): FinancialRecordExplorerSummaryItem["tone"] {
  switch (tone) {
    case "Success":
      return "success";
    case "Warning":
      return "warning";
    case "Danger":
      return "danger";
    default:
      return "default";
  }
}

function toneToBadge(tone?: FinancialRecordExplorerTone): "default" | "outline" | "success" | "warning" | "danger" {
  switch (tone) {
    case "Success":
      return "success";
    case "Warning":
      return "warning";
    case "Danger":
      return "danger";
    case "Info":
      return "default";
    default:
      return "outline";
  }
}

function toneTextClass(tone?: FinancialRecordExplorerTone): string {
  switch (tone) {
    case "Success":
      return "text-success";
    case "Warning":
      return "text-warning";
    case "Danger":
      return "text-danger";
    default:
      return "text-foreground";
  }
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
