// Interactive registry table — column manage/resize/reorder/pin, selection, pagination.
// Everything around it (header, views, filters, detail) lives in the DC as editable markup.
function RegistryTable(props) {
  const DS = window.MeridianDesignSystem_205d17;
  const { DataGrid, PanelSurface, EmptyState, SkeletonTable, Pagination, ColumnManager, Badge, SeverityBadge, Tooltip, TableHooks } = DS;
  const baseColumns = React.useMemo(() => [
    { key: "name", label: "Security Name", align: "left", width: 240, pinned: true, cell: (r) => (
      <div>
        <div style={{ fontWeight: 600, color: "var(--text-primary)", fontFamily: "var(--font-body)" }}>{r.name}</div>
        <div style={{ fontSize: 11, color: "var(--text-secondary)", marginTop: 2 }}>{r.shortCode}</div>
      </div>
    ) },
    { key: "cusip", label: "CUSIP", width: 112 },
    { key: "issuer", label: "Issuer", width: 190 },
    { key: "assetClass", label: "Asset Class", width: 138, cell: (r) => <Badge variant="neutral">{r.assetClass}</Badge> },
    { key: "currency", label: "Currency", width: 96 },
    { key: "rating", label: "Rating", width: 86 },
    { key: "sector", label: "Sector", width: 150 },
    { key: "fairValueLevel", label: "Val. Level", width: 104 },
    { key: "issueDate", label: "Issue Date", width: 118 },
    { key: "maturityDate", label: "Maturity Date", width: 124 },
    { key: "lifecycle", label: "Lifecycle", width: 104, cell: (r) => <Badge variant={r.lifecycleVariant || "neutral"} dot>{r.lifecycle}</Badge> },
    { key: "complianceStatus", label: "Compliance", width: 124, cell: (r) => <SeverityBadge status={r.complianceStatus} /> },
    { key: "flags", label: "Flags", width: 80, cell: (r) => r.flags.length
      ? <Tooltip content="Trading restricted"><Badge variant="danger" dot>R</Badge></Tooltip>
      : <span style={{ color: "var(--text-disabled)" }}>—</span> },
  ], []);
  const cols = TableHooks.useTableColumns(baseColumns, { persistKey: "smr.cols.v2" });
  const { rows = [], loading, sortKey, sortDir, onSort, selectedIds = [], onToggleRow, onSelectAllPage, cursorIndex, onRowClick, page, totalPages, onPageChange, totalShown = 0, pageSize = 8 } = props;
  React.useEffect(() => { const h = (e) => { window.__smrMod = { meta: e.metaKey || e.ctrlKey, shift: e.shiftKey }; }; window.addEventListener("pointerdown", h, true); window.addEventListener("click", h, true); return () => { window.removeEventListener("pointerdown", h, true); window.removeEventListener("click", h, true); }; }, []);
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
      <div style={{ display: "flex", justifyContent: "flex-end" }}><ColumnManager cols={cols} /></div>
      <PanelSurface flat style={{ overflow: "hidden" }}>
        {loading ? (
          <SkeletonTable rows={pageSize} columns={cols.visibleColumns.length + 1} />
        ) : rows.length ? (
          <DataGrid
            columns={cols.visibleColumns} rowKey="id"
            onColumnResize={cols.resize} onColumnReorder={cols.reorder} onColumnPin={cols.togglePin}
            sortKey={sortKey} sortDir={sortDir} onSort={onSort}
            select selected={selectedIds}
            onSelectedChange={(keys) => {
              if (keys.length === 0 && selectedIds.length > 1) return onSelectAllPage(false);
              if (keys.length === rows.length && rows.length - selectedIds.length > 1) return onSelectAllPage(true);
              const added = keys.find((k) => !selectedIds.includes(k));
              const removed = selectedIds.find((k) => !keys.includes(k));
              onToggleRow(added !== undefined ? added : removed);
            }}
            activeKey={(rows[cursorIndex] || {}).id} onRowClick={(r) => onRowClick(r.id, window.__smrMod || {})} rows={rows}
            label="Security master"
          />
        ) : (
          <EmptyState icon="search" title="No securities match your filters" detail="Clear a filter or try a different search term." />
        )}
      </PanelSurface>
      {!loading && totalPages > 1 && (
        <div style={{ display: "flex", justifyContent: "flex-end" }}>
          <Pagination currentPage={page} totalPages={totalPages} onPageChange={onPageChange} totalItems={totalShown} itemsPerPage={pageSize} />
        </div>
      )}
    </div>
  );
}
window.RegistryTable = RegistryTable;
if (typeof module !== "undefined") module.exports = { RegistryTable };
