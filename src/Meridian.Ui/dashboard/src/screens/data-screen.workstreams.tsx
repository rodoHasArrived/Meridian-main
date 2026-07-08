import { ClipboardCopy, Download, Save, Trash2 } from "lucide-react";
import { DenseRowDetailPanel } from "@/components/meridian/dense-row-detail-accessibility";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { EmptyState as ConcreteEmptyState } from "@/components/data/empty-state";
import { Pagination } from "@/components/data/pagination";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { StatusBanner } from "@/components/ui/status-banner";
import type { DataQueryPanelViewModel } from "@/screens/data-screen.query-panel.view-model";
import {
  DATA_BACKFILL_DETAIL_PANEL_ID,
  DATA_EXPORT_DETAIL_PANEL_ID,
  type DataOperationsBackfillDetailState,
  type DataOperationsBackfillRow,
  type DataOperationsDetailField,
  type DataOperationsEmptyState,
  type DataOperationsExportDetailState,
  type DataOperationsExportRow,
  type useDataViewModel
} from "@/screens/data-screen.view-model";

type DataOperationsVm = ReturnType<typeof useDataViewModel>;

const backfillQueueColumns: DenseDataTableColumn<DataOperationsBackfillRow>[] = [
  {
    id: "job",
    label: "Job",
    render: (backfill) => (
      <span className="block min-w-0">
        <span className="block font-mono font-semibold text-foreground">{backfill.jobId}</span>
        <span className="mt-1 block text-xs text-muted-foreground">{backfill.provider}</span>
      </span>
    )
  },
  {
    id: "scope",
    label: "Scope",
    render: (backfill) => <span className="text-muted-foreground">{backfill.scope}</span>
  },
  {
    id: "status",
    label: "Status",
    render: (backfill) => (
      <Badge
        variant={backfill.status === "Review" ? "warning" : backfill.status === "Running" ? "default" : "outline"}
      >
        {backfill.status}
      </Badge>
    )
  },
  {
    id: "progress",
    label: "Progress",
    render: (backfill) => (
      <span className="block min-w-[8rem]">
        <span className="mb-1 block font-mono text-xs text-muted-foreground">{backfill.progress}</span>
        <span className="block h-1 rounded-full bg-border/70">
          <span className="block h-1 rounded-full bg-primary transition-all" style={{ width: backfill.progress }} />
        </span>
      </span>
    )
  },
  {
    id: "updated",
    label: "Updated",
    render: (backfill) => <span className="font-mono text-xs text-muted-foreground">{backfill.updatedAt}</span>
  }
];

const exportColumns: DenseDataTableColumn<DataOperationsExportRow>[] = [
  {
    id: "profile",
    label: "Profile",
    render: (item) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{item.profile}</span>
        <span className="mt-1 block font-mono text-xs text-muted-foreground">{item.exportId}</span>
      </span>
    )
  },
  {
    id: "target",
    label: "Target",
    render: (item) => <span className="text-muted-foreground">{item.target}</span>
  },
  {
    id: "status",
    label: "Status",
    render: (item) => <Badge variant={item.statusVariant} dot>{item.statusLabel}</Badge>
  },
  {
    id: "rows",
    label: "Rows",
    align: "right",
    render: (item) => <span className="font-mono text-xs text-foreground">{item.rows}</span>
  },
  {
    id: "updated",
    label: "Updated",
    render: (item) => <span className="font-mono text-xs text-muted-foreground">{item.updatedAt}</span>
  }
];

export function DataBackfillWorkstream({ vm }: { vm: DataOperationsVm }) {
  return (
    <section aria-labelledby="data-backfill-queue-title" className="workspace-region">
      <CardHeader>
        <div className="flex items-center justify-between gap-3">
          <div>
            <CardTitle id="data-backfill-queue-title">Backfill queue</CardTitle>
            <CardDescription>Run or inspect historical repairs that support market-data quality and export readiness.</CardDescription>
          </div>
          <Button size="sm" onClick={vm.openBackfillDialog}>
            Trigger backfill
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="grid gap-3 xl:grid-cols-[minmax(0,1fr)_minmax(18rem,0.48fr)]">
          {vm.backfillSection.hasRows ? (
            <DenseDataTable
              columns={backfillQueueColumns}
              rows={vm.backfillSection.rows}
              getRowId={(backfill) => backfill.rowId}
              getRowAriaLabel={(backfill) => backfill.ariaLabel}
              getRowSelectAriaLabel={(backfill) => backfill.selectAriaLabel}
              getRowAriaControls={(backfill) => backfill.detailPanelId}
              getRowAriaExpanded={(backfill) => backfill.expanded}
              getRowClassName={(backfill) => backfill.rowClassName}
              selectedRowId={vm.selectedBackfillRowId}
              onRowSelect={(backfill) => vm.selectBackfill(backfill.jobId)}
              emptyText={vm.backfillSection.emptyState.description}
              ariaLabel={vm.backfillSection.tableLabel}
              caption={vm.backfillSection.description}
              maxVisibleRows={100}
            />
          ) : (
            <EmptyState state={vm.backfillSection.emptyState} />
          )}
          <BackfillDetailPanel
            detail={vm.selectedBackfillDetail}
            emptyState={vm.backfillDetailEmptyState ?? vm.backfillSection.emptyState}
          />
        </div>
      </CardContent>
    </section>
  );
}

export function DataExportWorkstream({ vm }: { vm: DataOperationsVm }) {
  return (
    <section aria-labelledby="data-recent-exports-title" className="workspace-region">
      <CardHeader>
        <CardTitle id="data-recent-exports-title">Recent exports</CardTitle>
        <CardDescription>Latest package and reporting outputs tied to Data workspace evidence.</CardDescription>
      </CardHeader>
      <CardContent>
        <div className="grid gap-3 xl:grid-cols-[minmax(0,1fr)_minmax(18rem,0.48fr)]">
          {vm.exportSection.hasRows ? (
            <DenseDataTable
              columns={exportColumns}
              rows={vm.exportSection.rows}
              getRowId={(item) => item.rowId}
              getRowAriaLabel={(item) => item.ariaLabel}
              getRowSelectAriaLabel={(item) => item.selectAriaLabel}
              getRowAriaControls={(item) => item.detailPanelId}
              getRowAriaExpanded={(item) => item.expanded}
              getRowClassName={(item) => item.rowClassName}
              selectedRowId={vm.exportSection.selectedRowId}
              onRowSelect={(item) => vm.selectExport(item.exportId)}
              emptyText={vm.exportSection.emptyState.description}
              ariaLabel={vm.exportSection.tableLabel}
              caption={vm.exportSection.description}
              maxVisibleRows={100}
            />
          ) : (
            <EmptyState state={vm.exportSection.emptyState} />
          )}
          <ExportDetailPanel
            detail={vm.exportSection.selectedDetail}
            emptyState={vm.exportSection.detailEmptyState ?? vm.exportSection.emptyState}
          />
        </div>
      </CardContent>
    </section>
  );
}

export function DataQueryWorkstream({
  queryPanel,
  savedQueryName,
  setSavedQueryName
}: {
  queryPanel: DataQueryPanelViewModel;
  savedQueryName: string;
  setSavedQueryName: (value: string) => void;
}) {
  return (
    <section aria-labelledby="data-query-title" className="workspace-region">
      <CardHeader>
        <CardTitle id="data-query-title">SQL query</CardTitle>
        <CardDescription>
          Read-only DuckDB analytics over the local store. Start from the <code>meridian_files</code> catalog
          view, then feed paths into <code>read_parquet</code> or <code>read_json_auto</code>.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <div className="grid gap-3">
          <textarea
            value={queryPanel.sql}
            onChange={(event) => queryPanel.setSql(event.target.value)}
            rows={5}
            spellCheck={false}
            aria-label="SQL statement"
            className="w-full rounded-md border border-border/70 bg-background p-2 font-mono text-sm"
          />
          <div className="grid gap-2 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
            <label className="grid gap-1 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
              Recent queries
              <select
                className="min-h-9 rounded-md border border-border/70 bg-background px-2 py-1 text-sm font-normal normal-case tracking-normal text-foreground"
                aria-label="Recent SQL query history"
                defaultValue=""
                onChange={(event) => {
                  if (event.currentTarget.value) {
                    queryPanel.loadQuery(event.currentTarget.value);
                    event.currentTarget.value = "";
                  }
                }}
              >
                <option value="">Select a recent query</option>
                {queryPanel.history.map((entry) => (
                  <option key={`${entry.lastUsedAt}-${entry.sql}`} value={entry.sql}>
                    {entry.sql}
                  </option>
                ))}
              </select>
            </label>
            <div className="grid gap-1">
              <label
                htmlFor="data-query-save-name"
                className="text-xs font-semibold uppercase tracking-wide text-muted-foreground"
              >
                Save query
              </label>
              <div className="flex flex-col gap-2 sm:flex-row">
                <input
                  id="data-query-save-name"
                  type="text"
                  value={savedQueryName}
                  onChange={(event) => setSavedQueryName(event.currentTarget.value)}
                  placeholder="Query name"
                  aria-label="Saved SQL query name"
                  className="min-h-9 flex-1 rounded-md border border-border/70 bg-background px-2 py-1 text-sm text-foreground"
                />
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => {
                    queryPanel.saveCurrentQuery(savedQueryName);
                    setSavedQueryName("");
                  }}
                  disabled={savedQueryName.trim().length === 0 || queryPanel.sql.trim().length === 0}
                >
                  <Save className="h-3.5 w-3.5" aria-hidden="true" />
                  Save
                </Button>
              </div>
            </div>
          </div>
          {queryPanel.savedQueries.length > 0 ? (
            <div className="flex flex-wrap gap-2" aria-label="Saved SQL queries">
              {queryPanel.savedQueries.map((savedQuery) => (
                <span
                  key={savedQuery.id}
                  className="inline-flex items-center gap-1 rounded-md border border-border/70 bg-secondary/20 px-2 py-1"
                >
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={() => queryPanel.loadQuery(savedQuery.sql)}
                    aria-label={`Load saved SQL query ${savedQuery.name}`}
                  >
                    {savedQuery.name}
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    onClick={() => queryPanel.deleteSavedQuery(savedQuery.id)}
                    aria-label={`Delete saved SQL query ${savedQuery.name}`}
                    title={`Delete ${savedQuery.name}`}
                  >
                    <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                  </Button>
                </span>
              ))}
            </div>
          ) : null}
          <div className="flex flex-wrap items-center gap-3">
            <Button type="button" onClick={() => void queryPanel.run()} disabled={queryPanel.busy}>
              {queryPanel.busy ? "Running..." : "Run query"}
            </Button>
            <Button
              type="button"
              variant="outline"
              onClick={() => void queryPanel.copyResults()}
              disabled={!queryPanel.canCopyOrExport}
            >
              <ClipboardCopy className="h-3.5 w-3.5" aria-hidden="true" />
              Copy CSV
            </Button>
            <Button
              type="button"
              variant="outline"
              onClick={() => queryPanel.exportCsv()}
              disabled={!queryPanel.canCopyOrExport}
            >
              <Download className="h-3.5 w-3.5" aria-hidden="true" />
              Download CSV
            </Button>
            {queryPanel.result ? (
              <span className="text-sm text-muted-foreground" role="status">
                {queryPanel.resultStatus}
              </span>
            ) : null}
          </div>
          {queryPanel.copyStatus ? (
            <p className="text-sm text-muted-foreground" role="status">{queryPanel.copyStatus}</p>
          ) : null}
          {queryPanel.truncationMessage ? (
            <StatusBanner tone="warning" title="Result window" detail={queryPanel.truncationMessage} />
          ) : null}
          {queryPanel.error ? (
            <StatusBanner tone="danger" title="Query failed" detail={queryPanel.error} />
          ) : null}
          {queryPanel.result && queryPanel.result.columns.length > 0 ? (
            <div className="overflow-x-auto rounded-md border border-border/70" aria-label="Query results">
              <table className="dense-data-table min-w-full">
                <thead>
                  <tr>
                    {queryPanel.result.columns.map((column) => (
                      <th key={column} scope="col">{column}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {queryPanel.visibleRows.map((row, rowIndex) => (
                    <tr key={rowIndex}>
                      {row.map((value, columnIndex) => (
                        <td key={`${rowIndex}-${queryPanel.result?.columns[columnIndex] ?? columnIndex}`}>
                          {value ?? "null"}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : null}
          {queryPanel.result && queryPanel.resultRowCount > queryPanel.rowsPerPage ? (
            <Pagination
              currentPage={queryPanel.currentPage}
              totalPages={queryPanel.totalPages}
              totalItems={queryPanel.resultRowCount}
              itemsPerPage={queryPanel.rowsPerPage}
              onPageChange={queryPanel.setPage}
            />
          ) : null}
        </div>
      </CardContent>
    </section>
  );
}

function BackfillDetailPanel({
  detail,
  emptyState
}: {
  detail: DataOperationsBackfillDetailState | null;
  emptyState: DataOperationsEmptyState | null;
}) {
  if (!detail) {
    return (
      <DenseRowDetailPanel
        id={DATA_BACKFILL_DETAIL_PANEL_ID}
        role="status"
        ariaLabel="Backfill detail empty state"
        className="row-detail-panel h-fit min-w-0"
      >
        <div className="eyebrow-label">Selected Backfill</div>
        <h3 className="mt-2 text-sm font-semibold text-foreground">
          {emptyState?.title ?? "No backfill selected"}
        </h3>
        <p className="mt-2 text-sm leading-6 text-muted-foreground">
          {emptyState?.description ?? "Select a backfill row to inspect repair scope, provider, progress, and update evidence."}
        </p>
      </DenseRowDetailPanel>
    );
  }

  return (
    <DenseRowDetailPanel
      id={detail.id}
      ariaLabel={detail.ariaLabel}
      className="row-detail-panel h-fit min-w-0"
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="eyebrow-label">Selected Backfill</div>
          <h3 className="mt-2 text-sm font-semibold text-foreground">{detail.title}</h3>
          <p className="mt-2 text-sm leading-6 text-muted-foreground">{detail.description}</p>
        </div>
        <Badge variant={detail.statusVariant} dot>
          {detail.statusLabel}
        </Badge>
      </div>
      <div className="mt-3 grid gap-2 sm:grid-cols-2 xl:grid-cols-1">
        {detail.rows.map((field) => (
          <FieldTile key={field.id} field={field} />
        ))}
      </div>
    </DenseRowDetailPanel>
  );
}

function ExportDetailPanel({
  detail,
  emptyState
}: {
  detail: DataOperationsExportDetailState | null;
  emptyState: DataOperationsEmptyState | null;
}) {
  if (!detail) {
    return (
      <aside
        id={DATA_EXPORT_DETAIL_PANEL_ID}
        role="status"
        aria-label="Export detail empty state"
        className="row-detail-panel h-fit min-w-0"
      >
        <div className="eyebrow-label">Selected Export</div>
        <h3 className="mt-2 text-sm font-semibold text-foreground">
          {emptyState?.title ?? "No export selected"}
        </h3>
        <p className="mt-2 text-sm leading-6 text-muted-foreground">
          {emptyState?.description ?? "Select an export row to inspect readiness, target, row count, and handoff guidance."}
        </p>
      </aside>
    );
  }

  return (
    <aside
      id={detail.id}
      role="region"
      aria-label={detail.ariaLabel}
      aria-live="polite"
      className="row-detail-panel h-fit min-w-0"
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="eyebrow-label">Selected Export</div>
          <h3 className="mt-2 truncate text-sm font-semibold text-foreground">{detail.title}</h3>
          <p className="mt-1 break-words font-mono text-xs text-muted-foreground">{detail.subtitle}</p>
        </div>
        <Badge variant={detail.statusVariant} dot>
          {detail.statusLabel}
        </Badge>
      </div>
      <p className="mt-3 text-sm leading-6 text-muted-foreground">{detail.description}</p>
      <div className="mt-3 grid gap-2 sm:grid-cols-2 xl:grid-cols-1">
        {detail.fields.map((field) => (
          <FieldTile key={field.id} field={field} />
        ))}
      </div>
      <div className="mt-3 rounded-md border border-border/60 bg-background/45 px-3 py-2">
        <div className="eyebrow-label">Next action</div>
        <p className="mt-1 text-xs leading-5 text-muted-foreground">{detail.actionText}</p>
      </div>
    </aside>
  );
}

function EmptyState({ state }: { state: DataOperationsEmptyState }) {
  return (
    <ConcreteEmptyState
      role="status"
      icon="table"
      title={state.title}
      detail={state.description}
      compact
      className="rounded-sm border border-dashed border-border/80 bg-secondary/20"
    />
  );
}

function FieldTile({ field }: { field: DataOperationsDetailField }) {
  return (
    <div className="rounded-md border border-border/60 bg-background/45 px-3 py-2">
      <div className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">{field.label}</div>
      <div className="mt-1 min-w-0 break-words text-sm font-medium text-foreground">{field.value}</div>
    </div>
  );
}
