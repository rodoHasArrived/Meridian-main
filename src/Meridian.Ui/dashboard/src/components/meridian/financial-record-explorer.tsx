import { Filter, GitBranch, LayoutPanelTop, Link2, Save, Search, ShieldCheck } from "lucide-react";
import { useEffect, useMemo, useState, type KeyboardEvent, type ReactNode } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { FinancialRecordProofActionButton, FinancialRecordProofDrawer } from "@/components/meridian/financial-record-proof-drawer";
import {
  buildExplorerShareHref,
  buildSavedViewDescription,
  readExplorerUrlState,
  resolveInitialRecordId,
  resolveInitialSearchText,
  resolveInitialViewId,
  syncExplorerUrlState
} from "@/lib/financial-record-explorer-context";
import { financialRecordToneTextClass, financialRecordToneToBadge } from "@/lib/financial-record-explorer-tone";
import { cn } from "@/lib/utils";
import type {
  FinancialRecordExplorerDto,
  FinancialRecordExplorerFilterDto,
  FinancialRecordExplorerRowDto,
  FinancialRecordExplorerSavedViewSaveRequestDto,
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
  const initialUrlState = readExplorerUrlState(dtoMode?.explorerId ?? null);
  const initialSelectedViewId = resolveInitialViewId(dtoMode, activeSavedView?.id ?? "", initialUrlState.viewId);
  const [selectedViewId, setSelectedViewId] = useState(initialSelectedViewId);
  const [customFilters, setCustomFilters] = useState<FinancialRecordExplorerFilterDto[] | null>(() => initialUrlState.filters);
  const [customColumnIds, setCustomColumnIds] = useState<string[] | null>(() => initialUrlState.columnIds);
  const [searchText, setSearchText] = useState(() => resolveInitialSearchText(dtoMode, initialSelectedViewId, initialUrlState.searchText));
  const [selectedRecordId, setSelectedRecordId] = useState(() => resolveInitialRecordId(dtoMode, initialUrlState.recordId, initialSelectedViewId));
  const [saveViewLabel, setSaveViewLabel] = useState("");
  const [saving, setSaving] = useState(false);
  const selectedDtoSavedView = useMemo(
    () => dtoMode?.savedViews.find((view) => view.viewId === selectedViewId) ?? null,
    [dtoMode?.savedViews, selectedViewId]
  );

  useEffect(() => {
    const urlState = readExplorerUrlState(dtoMode?.explorerId ?? null);
    const nextSelectedViewId = resolveInitialViewId(dtoMode, activeSavedView?.id ?? "", urlState.viewId);
    setSelectedViewId(nextSelectedViewId);
    setCustomFilters(urlState.filters);
    setCustomColumnIds(urlState.columnIds);
    setSearchText(resolveInitialSearchText(dtoMode, nextSelectedViewId, urlState.searchText));
    setSelectedRecordId(resolveInitialRecordId(dtoMode, urlState.recordId, nextSelectedViewId));
    setSaveViewLabel("");
  }, [activeSavedView?.id, dtoMode?.explorerId, dtoMode?.selectedRecord?.recordId, dtoMode?.rows]);

  function handleSelectSavedView(viewId: string) {
    setSelectedViewId(viewId);
    setCustomFilters(null);
    setCustomColumnIds(null);
    const savedView = dtoMode?.savedViews.find((view) => view.viewId === viewId);
    if (savedView) {
      setSearchText(savedView.searchText ?? "");
      setSelectedRecordId(savedView.selectedRecordId ?? "");
    }
  }

  const selectedFilters = useMemo(
    () => customFilters ?? selectedDtoSavedView?.filters ?? [],
    [customFilters, selectedDtoSavedView?.filters]
  );
  const selectedColumnIds = useMemo(
    () => normalizeColumnIds(customColumnIds ?? selectedDtoSavedView?.columnIds ?? []),
    [customColumnIds, selectedDtoSavedView?.columnIds]
  );
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
  const selectedRecordValue = selectedRow?.recordId ?? selectedRecordId;
  const proofDetailId = dtoMode ? `financial-record-proof-detail-${toExplorerDomId(dtoMode.explorerId)}` : undefined;
  useEffect(() => {
    if (!dtoMode) {
      return;
    }

    const nextRecordId = selectedRow?.recordId ?? "";
    if (selectedRecordId !== nextRecordId) {
      setSelectedRecordId(nextRecordId);
    }
  }, [dtoMode, selectedRecordId, selectedRow?.recordId]);

  useEffect(() => {
    if (!dtoMode) {
      return;
    }

    syncExplorerUrlState({
      explorerId: dtoMode.explorerId,
      viewId: selectedViewId,
      searchText,
      recordId: selectedRecordValue,
      filters: selectedFilters,
      columnIds: selectedColumnIds
    });
  }, [dtoMode, searchText, selectedColumnIds, selectedFilters, selectedRecordValue, selectedViewId]);

  const hasNamedViewLabel = saveViewLabel.trim().length > 0;
  const hasCustomFilters = Boolean(customFilters && customFilters.length > 0);
  const hasCustomColumns = Boolean(customColumnIds && customColumnIds.length > 0);
  const selectedRecordBaselineId = selectedDtoSavedView?.selectedRecordId?.trim() ||
    dtoMode?.selectedRecord?.recordId ||
    "";
  const hasSelectedRecordChange = Boolean(selectedRecordValue && selectedRecordValue !== selectedRecordBaselineId);
  const hasExplorerStateChange = Boolean(
    hasCustomFilters ||
    hasCustomColumns ||
    hasSelectedRecordChange ||
    searchText.trim() ||
    (selectedViewId && selectedViewId !== activeSavedView?.id)
  );
  const canSaveView = Boolean(dtoMode && onSaveView && hasNamedViewLabel && hasExplorerStateChange);
  const saveViewDisabledReason = dtoMode?.isBlocked
    ? dtoMode.blockedReason
    : !hasNamedViewLabel
      ? "Name this saved view before saving."
      : "Change search, filters, columns, selected record, or saved-view context before saving.";
  const headerTitle = dtoMode?.title ?? title;
  const headerDescription = dtoMode?.description ?? description;
  const shareHref = dtoMode
    ? buildExplorerShareHref({
        explorerId: dtoMode.explorerId,
        viewId: selectedViewId,
        searchText,
        recordId: selectedRecordValue,
        filters: selectedFilters,
        columnIds: selectedColumnIds
      })
    : "#";

  async function handleSaveView() {
    if (!dtoMode || !onSaveView || !canSaveView) {
      return;
    }

    const selectedView = dtoMode.savedViews.find((view) => view.viewId === selectedViewId) ?? null;
    const filters = selectedFilters;
    const label = saveViewLabel.trim();
    setSaving(true);
    try {
      await onSaveView({
        label,
        description: buildSavedViewDescription(searchText, filters, selectedView?.label ?? activeSavedView?.label),
        searchText,
        filters,
        columnIds: selectedColumnIds,
        selectedRecordId: selectedRecordValue
      });
      setSaveViewLabel("");
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
          {dtoMode ? (
            <input
              className="h-8 min-w-[14rem] rounded-md border border-input bg-background px-2.5 text-xs text-foreground shadow-sm outline-none placeholder:text-muted-foreground focus-visible:ring-2 focus-visible:ring-ring"
              value={saveViewLabel}
              onChange={(event) => setSaveViewLabel(event.target.value)}
              placeholder="Name saved view"
              aria-label={`Saved view name for ${headerTitle}`}
            />
          ) : null}
          <Button
            size="sm"
            variant="outline"
            disabled={!canSaveView || saving || dtoMode?.isBlocked}
            disabledReason={saveViewDisabledReason}
            busy={saving}
            onClick={() => void handleSaveView()}
          >
            <Save className="h-3.5 w-3.5" aria-hidden="true" />
            Save view
          </Button>
          {dtoMode ? (
            <Button asChild size="sm" variant="ghost">
              <a href={shareHref} aria-label={`Share ${headerTitle} evidence state`}>
                <Link2 className="h-3.5 w-3.5" aria-hidden="true" />
                Share link
              </a>
            </Button>
          ) : null}
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
            <ExplorerGrid explorer={dtoMode} rows={rows} columns={visibleColumns} selectedRecordId={selectedRow?.recordId ?? null} proofDetailId={proofDetailId} onSelect={setSelectedRecordId} />
            <RecordGraph explorer={dtoMode} />
          </div>
          <FinancialRecordProofDrawer id={proofDetailId} record={selectedRecord} row={selectedRow} explorer={dtoMode} blockedReason={dtoMode.isBlocked ? dtoMode.blockedReason : ""} />
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
          <FinancialRecordProofActionButton key={action.actionId} action={action} />
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
  proofDetailId,
  onSelect
}: {
  explorer: FinancialRecordExplorerDto;
  rows: FinancialRecordExplorerRowDto[];
  columns: FinancialRecordExplorerDto["columns"];
  selectedRecordId: string | null;
  proofDetailId?: string;
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
          {rows.map((row) => {
            const selected = selectedRecordId === row.recordId;
            return (
              <tr
                key={row.recordId}
                className={cn("cursor-pointer border-t border-border/60 outline-none hover:bg-secondary/25 focus-visible:bg-secondary/30 focus-visible:ring-2 focus-visible:ring-ring", selected ? "bg-primary/8" : "")}
                tabIndex={0}
                aria-selected={selected}
                aria-controls={proofDetailId}
                onClick={() => onSelect(row.recordId)}
                onKeyDown={(event) => handleExplorerRowKeyDown(event, row.recordId, onSelect)}
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
                        <span className={cn(financialRecordToneTextClass(cell?.tone))}>{cell?.displayValue ?? "-"}</span>
                      )}
                    </td>
                  );
                })}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function handleExplorerRowKeyDown(
  event: KeyboardEvent<HTMLTableRowElement>,
  recordId: string,
  onSelect: (recordId: string) => void
) {
  if (event.currentTarget !== event.target) {
    return;
  }

  if (event.key !== "Enter" && event.key !== " ") {
    return;
  }

  event.preventDefault();
  onSelect(recordId);
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

function normalizeColumnIds(columnIds: readonly string[]): string[] {
  const seen = new Set<string>();
  const normalized: string[] = [];
  for (const columnId of columnIds) {
    const trimmed = columnId.trim();
    const key = trimmed.toLowerCase();
    if (!trimmed || seen.has(key)) {
      continue;
    }

    seen.add(key);
    normalized.push(trimmed);
  }

  return normalized;
}

function toExplorerDomId(value: string): string {
  return value.trim().toLowerCase().replace(/[^a-z0-9_-]+/g, "-") || "explorer";
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
          <Badge key={node.nodeId} variant={financialRecordToneToBadge(node.tone)}>{node.label}</Badge>
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
