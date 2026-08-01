import { Filter, GitBranch, LayoutPanelTop, Link2, Save, Search, ShieldCheck } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Drawer, DrawerBody } from "@/components/ui/drawer";
import { NumberPassport } from "@/components/meridian/number-passport";
import { cn } from "@/lib/utils";
import {
  buildShareableExplorerHref,
  buildShareableExplorerStateSummary,
  readSharedExplorerViewState,
  resolveSharedFilters,
  resolveSharedRecordId,
  resolveSharedViewId
} from "@/components/meridian/financial-record-explorer.view-state";
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
  onSelectRecord,
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
  onSelectRecord?: (recordId: string) => void;
  children: ReactNode;
  className?: string;
}) {
  const dtoMode = explorer ?? null;
  const normalizedViews = useMemo(() => dtoMode
    ? dtoMode.savedViews.map((view) => ({
        id: view.viewId,
        label: view.label,
        detail: view.description,
        active: view.isActive
      }))
    : savedViews, [dtoMode?.savedViews, savedViews]);
  const normalizedViewKey = normalizedViews.map((view) => view.id).join("|");
  const activeSavedView = normalizedViews.find((view) => view.active) ?? normalizedViews[0] ?? null;
  const sharedViewState = useMemo(() => readSharedExplorerViewState(dtoMode, getCurrentExplorerSearch()), [dtoMode?.explorerId]);
  const initialViewId = resolveSharedViewId(sharedViewState.viewId, normalizedViews) ?? activeSavedView?.id ?? "";
  const initialSharedRecordId = resolveSharedRecordId(sharedViewState.recordId, dtoMode?.rows ?? []);
  const initialRecordId = initialSharedRecordId ?? dtoMode?.selectedRecord?.recordId ?? dtoMode?.rows[0]?.recordId ?? "";
  const [selectedViewId, setSelectedViewId] = useState(initialViewId);
  const [searchText, setSearchText] = useState(sharedViewState.searchText ?? "");
  const [selectedRecordId, setSelectedRecordId] = useState(initialRecordId);
  const [inspectorOpen, setInspectorOpen] = useState(Boolean(initialSharedRecordId));
  const [sharedFilterState, setSharedFilterState] = useState(sharedViewState.filters);
  const [viewName, setViewName] = useState("");
  const [saving, setSaving] = useState(false);
  const selectedDtoSavedView = useMemo(
    () => dtoMode?.savedViews.find((view) => view.viewId === selectedViewId) ?? null,
    [dtoMode?.savedViews, selectedViewId]
  );

  useEffect(() => {
    const nextViewId = resolveSharedViewId(sharedViewState.viewId, normalizedViews) ?? activeSavedView?.id ?? "";
    const nextSearchText = sharedViewState.searchText ?? "";
    const nextRecordId = resolveSharedRecordId(sharedViewState.recordId, dtoMode?.rows ?? []) ?? dtoMode?.selectedRecord?.recordId ?? dtoMode?.rows[0]?.recordId ?? "";
    const nextSharedRecordId = resolveSharedRecordId(sharedViewState.recordId, dtoMode?.rows ?? []);
    setSelectedViewId((current) => current === nextViewId ? current : nextViewId);
    setSearchText((current) => current === nextSearchText ? current : nextSearchText);
    setSelectedRecordId((current) => current === nextRecordId ? current : nextRecordId);
    if (nextSharedRecordId) {
      setInspectorOpen(true);
    }
    setSharedFilterState(sharedViewState.filters);
  }, [
    activeSavedView?.id,
    dtoMode?.explorerId,
    dtoMode?.selectedRecord?.recordId,
    dtoMode?.rows,
    normalizedViewKey,
    sharedViewState.recordId,
    sharedViewState.filters,
    sharedViewState.searchText,
    sharedViewState.viewId
  ]);

  function handleSelectSavedView(viewId: string) {
    setSelectedViewId(viewId);
    setSharedFilterState([]);
    const savedView = dtoMode?.savedViews.find((view) => view.viewId === viewId);
    if (savedView) {
      setSearchText(savedView.searchText ?? "");
    }
  }

  const selectedFilters = useMemo(() => {
    if (sharedFilterState.length > 0 && dtoMode) {
      return resolveSharedFilters(sharedFilterState, dtoMode);
    }

    return selectedDtoSavedView?.filters ?? [];
  }, [dtoMode, selectedDtoSavedView?.filters, sharedFilterState]);
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
  const selectedRecordShareId = selectedRow?.recordId ?? selectedRecordId;

  useEffect(() => {
    if (selectedRow?.recordId) {
      onSelectRecord?.(selectedRow.recordId);
    }
  }, [onSelectRecord, selectedRow?.recordId]);
  const trimmedViewName = viewName.trim();
  const materialChange = Boolean(dtoMode && onSaveView && (trimmedViewName || searchText.trim() || (selectedViewId && selectedViewId !== activeSavedView?.id)));
  const canSaveView = Boolean(materialChange && trimmedViewName);
  const headerTitle = dtoMode?.title ?? title;
  const headerDescription = dtoMode?.description ?? description;
  const shareViewId = sharedFilterState.length > 0 && !sharedViewState.viewId ? "" : selectedViewId;
  const shareSavedViewLabel = shareViewId ? selectedDtoSavedView?.label ?? activeSavedView?.label ?? null : null;
  const shareStateSummary = buildShareableExplorerStateSummary({
    savedViewLabel: shareSavedViewLabel,
    searchText,
    selectedFilters,
    selectedRow
  });
  const shareHref = buildShareableExplorerHref({
    explorer: dtoMode,
    selectedViewId: shareViewId,
    searchText,
    selectedFilters,
    selectedRecordId: selectedRecordShareId,
    currentHref: getCurrentExplorerHref()
  });

  async function handleSaveView() {
    if (!dtoMode || !onSaveView || !canSaveView) {
      return;
    }

    const selectedView = dtoMode.savedViews.find((view) => view.viewId === selectedViewId);
    const filters = sharedFilterState.length > 0 ? selectedFilters : selectedView?.filters ?? dtoMode.filters;
    const savedViewDescription = searchText.trim()
      ? `Search: ${searchText.trim()}`
      : sharedFilterState.length > 0
        ? "Saved from shared explorer link."
        : `Saved from ${selectedView?.label ?? "current explorer filters"}.`;
    setSaving(true);
    try {
      await onSaveView({
        label: trimmedViewName,
        description: savedViewDescription,
        searchText,
        filters,
        columnIds: selectedView?.columnIds ?? []
      });
      setSearchText("");
      setViewName("");
    } finally {
      setSaving(false);
    }
  }

  function handleSelectRecord(recordId: string) {
    setSelectedRecordId(recordId);
    setInspectorOpen(true);
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
            <Button asChild size="sm" variant="outline">
              <a href={shareHref} aria-label={`Share ${headerTitle} evidence state: ${shareStateSummary}`}>
                <Link2 className="h-3.5 w-3.5" aria-hidden="true" />
                Share state
              </a>
            </Button>
          ) : null}
          {dtoMode && onSaveView ? (
            <input
              className="h-8 w-44 rounded-md border border-border/70 bg-background px-2 text-xs text-foreground outline-none placeholder:text-muted-foreground focus:border-primary/60"
              value={viewName}
              onChange={(event) => setViewName(event.target.value)}
              placeholder="View name"
              aria-label="Saved view name"
            />
          ) : null}
          <Button
            size="sm"
            variant="outline"
            disabled={!canSaveView || saving || dtoMode?.isBlocked}
            disabledReason={dtoMode?.isBlocked
              ? dtoMode.blockedReason
              : !trimmedViewName
                ? "Enter a saved view name before saving this exact explorer state."
                : "Change search text or select a different saved view before saving."}
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
        <div className="mt-4 space-y-4">
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
            <ExplorerGrid explorer={dtoMode} rows={rows} columns={visibleColumns} selectedRecordId={selectedRow?.recordId ?? null} onSelect={handleSelectRecord} />
            <RecordGraph explorer={dtoMode} />
          </div>
        </div>
      ) : null}

      {dtoMode ? (
        <Drawer
          open={inspectorOpen && Boolean(selectedRecord)}
          onClose={() => setInspectorOpen(false)}
          title={selectedRecord ? `${selectedRecord.title} proof detail` : "Proof detail"}
          className="max-w-[30rem]"
        >
          <DrawerBody>
            <ProofDrawer explorer={dtoMode} record={selectedRecord} blockedReason={dtoMode.isBlocked ? dtoMode.blockedReason : ""} />
          </DrawerBody>
        </Drawer>
      ) : null}

      <div className="mt-4">
        {children}
      </div>
    </section>
  );
}

function getCurrentExplorerSearch(): string {
  return typeof window === "undefined" ? "" : window.location.search;
}

function getCurrentExplorerHref(): string {
  return typeof window === "undefined" ? "" : window.location.href;
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
        Select a record to inspect source links, supporting documents, approvals, reconciliations, report usage, and audit history in the proof drawer.
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
  const rowElements = useRef(new Map<string, HTMLTableRowElement>());
  const [activeRowIndex, setActiveRowIndex] = useState(0);

  const registerRow = useCallback((recordId: string, element: HTMLTableRowElement | null) => {
    if (element) {
      rowElements.current.set(recordId, element);
    } else {
      rowElements.current.delete(recordId);
    }
  }, []);

  // Filtering can shrink the row set under the active index, and an external selection
  // (the explorer auto-selects a first record) should move the tab stop to that row so
  // Tab lands where the operator is already looking.
  const selectedIndex = rows.findIndex((row) => row.recordId === selectedRecordId);
  useEffect(() => {
    setActiveRowIndex((current) => {
      if (selectedIndex >= 0) return selectedIndex;
      if (rows.length === 0) return 0;
      return Math.min(current, rows.length - 1);
    });
  }, [selectedIndex, rows.length]);

  const focusRow = useCallback(
    (index: number) => {
      const target = rows[index];
      if (!target) return;
      setActiveRowIndex(index);
      rowElements.current.get(target.recordId)?.focus();
    },
    [rows]
  );

  const handleRowKeyDown = useCallback(
    (event: React.KeyboardEvent<HTMLTableRowElement>, recordId: string, rowIndex: number) => {
      // Only act on keys aimed at the row itself. A cell link is focusable, and its Enter
      // keydown bubbles here: preventing default and selecting the record meant the browser
      // never followed the link and the proof drawer opened instead. Mouse activation was
      // unaffected because the anchor stops click propagation, so this only broke keyboard use.
      if (event.target !== event.currentTarget) return;

      switch (event.key) {
        case "Enter":
        case " ":
          event.preventDefault();
          onSelect(recordId);
          return;
        case "ArrowDown":
          event.preventDefault();
          focusRow(Math.min(rowIndex + 1, rows.length - 1));
          return;
        case "ArrowUp":
          event.preventDefault();
          focusRow(Math.max(rowIndex - 1, 0));
          return;
        case "Home":
          event.preventDefault();
          focusRow(0);
          return;
        case "End":
          event.preventDefault();
          focusRow(rows.length - 1);
          return;
        default:
      }
    },
    [focusRow, onSelect, rows.length]
  );

  if (explorer.isBlocked || rows.length === 0) {
    return (
      <div className="rounded-md border border-border/70 bg-background/60 p-6 text-sm text-muted-foreground" role="status">
        {explorer.isBlocked ? explorer.blockedReason : "No source-backed records are available for this explorer."}
      </div>
    );
  }

  return (
    <div className="overflow-x-auto rounded-md border border-border/70">
      {/* role="grid" is what makes aria-selected meaningful on these rows: a plain table row
          has no selection semantics, so assistive technology could not report which record
          the operator had picked. It also commits the grid to roving tabindex below. */}
      {/* The header is a grid row too, so the count includes it and data rows start at 2.
          Numbering data rows from 1 would make the first record claim the header's position
          and announce every row one place too early. Matches ExplorerDenseGrid in
          components/meridian/ui-kit-primitives.tsx. */}
      <table
        role="grid"
        aria-label={`${explorer.title} records`}
        aria-rowcount={rows.length + 1}
        className="min-w-full text-sm"
      >
        <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
          <tr aria-rowindex={1}>
            {columns.map((column) => (
              <th key={column.columnId} className={cn("px-3 py-2 text-left", column.isRightAligned ? "text-right" : "")}>{column.header}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, rowIndex) => (
            <tr
              key={row.recordId}
              ref={(element) => registerRow(row.recordId, element)}
              // One tab stop for the whole grid. Making every row tabbable would put a
              // hundred-record explorer a hundred Tab presses away from the next control.
              tabIndex={rowIndex === activeRowIndex ? 0 : -1}
              aria-selected={selectedRecordId === row.recordId}
              aria-rowindex={rowIndex + 2}
              className={cn("cursor-pointer border-t border-border/60 hover:bg-secondary/25 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40", selectedRecordId === row.recordId ? "bg-primary/8" : "")}
              onFocus={() => setActiveRowIndex(rowIndex)}
              onClick={(event) => {
                event.currentTarget.focus();
                onSelect(row.recordId);
              }}
              onKeyDown={(event) => handleRowKeyDown(event, row.recordId, rowIndex)}
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
  explorer,
  record,
  blockedReason
}: {
  explorer: FinancialRecordExplorerDto;
  record: FinancialRecordExplorerSelectedRecordDto | null;
  blockedReason: string;
}) {
  if (!record) {
    return (
      <div role="status" className="rounded-md border border-border/70 bg-background/60 p-4 text-sm text-muted-foreground">
        {blockedReason || "Select a source-backed row to inspect fields, proof actions, Used In, and Impacts."}
      </div>
    );
  }

  return (
    <div role="region" className="space-y-3" aria-label={`${record.title} proof detail`}>
      <div>
        <Badge variant={toneToBadge(record.tone)}>{record.recordType}</Badge>
        <h3 className="mt-3 text-base font-semibold text-foreground">{record.title}</h3>
        <p className="mt-1 text-xs text-muted-foreground">{record.subtitle}</p>
        <p className="mt-2 text-sm leading-6 text-muted-foreground">{record.description}</p>
      </div>
      <ProofActionList actions={record.proofActions} />
      <NumberPassport explorer={explorer} record={record} />
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
    </div>
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
    <section className="grid gap-2" aria-label={title}>
      <h4 className="text-xs font-semibold uppercase text-muted-foreground">{title}</h4>
      {items.map((item) => (
        <dl key={`${item.label}-${item.value}`} className="rounded-md border border-border/60 px-3 py-2">
          <dt className="text-[11px] text-muted-foreground">{item.label}</dt>
          <dd className={cn("mt-1 font-mono text-sm", toneTextClass(item.tone))}>
            {item.value}
            {item.detail ? <span className="mt-1 block font-sans text-xs text-muted-foreground">{item.detail}</span> : null}
          </dd>
        </dl>
      ))}
    </section>
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
