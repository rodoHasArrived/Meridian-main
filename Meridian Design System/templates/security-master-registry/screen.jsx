// Meridian security-master-registry — template screen. Mounted by the DC via <x-import>; reads design-system
// components from the compiled bundle.

const {
  WorkstationTopbar, NavRail, StatusBar, PanelSurface, Eyebrow, Button, Badge, SeverityBadge,
  DenseDataTable, KeyValueGrid, Breadcrumb, Tabs, TabPanel, Checkbox, Toggle, Callout,
  ValidationIssueList, FormSectionLabel, FormDivider, Input, TextArea, SegmentedControl,
  EmptyState, Tooltip, FilterBuilder, SelectionToolbar, ColumnManager, Pagination, SkeletonTable,
  EditableCell, EntitySummary, HotkeysProvider, Select, ToastProvider, Kbd, TableHooks,
} = window.MeridianDesignSystem_4f61be;
const { useState, useEffect, useMemo, useRef, useCallback } = React;
const { useTableColumns } = TableHooks;

const SECURITIES = window.SECURITIES;
const complianceChecks = window.complianceChecks;

const TODAY = new Date("2026-07-04");
const PAGE_SIZE = 8;
const VIEWS_KEY = "smr.savedViews.v1";

function toast(tone, title, detail) {
  if (window.MeridianToast) window.MeridianToast[tone] ? window.MeridianToast[tone](title, detail) : window.MeridianToast.show({ tone, title, detail });
}

// ---- data derivation -------------------------------------------------------
function anyFieldVal(row, label) {
  for (const s of row.sections) {
    if (s.type === "fields") { const f = s.fields.find(y => y.label === label); if (f) return f.value; }
  }
  return null;
}
function monthsUntil(d) { if (!d || d === "—") return null; return (new Date(d) - TODAY) / (1000 * 3600 * 24 * 30.44); }
function feedFor(src) {
  if (!src) return "—";
  if (/BVAL|Bloomberg/i.test(src)) return "Bloomberg BVAL";
  if (/QuantLib|Internal/i.test(src)) return "Internal model";
  if (/NAV/i.test(src)) return "GP NAV statement";
  if (/amortized/i.test(src)) return "Amortized cost";
  return src;
}
function confidenceFor(level) {
  if (level === "Level 1") return { label: "High — observable", color: "var(--green)" };
  if (level === "Level 2") return { label: "Medium — derived", color: "var(--orange)" };
  return { label: "Model-derived", color: "var(--red)" };
}

function enrichRow(row, ov) {
  const o = ov || {};
  const restricted = o.restricted != null ? o.restricted : (row.compliance.restricted || row.flags.includes("Restricted"));
  const m = monthsUntil(row.maturityDate);
  return {
    ...row,
    flags: o.flags || row.flags,
    restricted,
    restrictedLabel: restricted ? "Yes" : "No",
    watchlist: row.compliance.watchlist,
    watchlistLabel: row.compliance.watchlist ? "Yes" : "No",
    complianceStatus: o.complianceStatus || row.compliance.status,
    fairValueLevel: anyFieldVal(row, "Fair Value Level") || "—",
    pricingSource: anyFieldVal(row, "Pricing Source") || "—",
    dataSteward: o.dataSteward || anyFieldVal(row, "Data Steward") || "—",
    nearMaturityLabel: m != null && m <= 12 ? "Yes" : "No",
  };
}

const uniq = (arr) => [...new Set(arr.filter(Boolean))].sort();
const RAW_ENRICHED = SECURITIES.map(r => enrichRow(r));
const OPT = {
  assetClass: uniq(RAW_ENRICHED.map(r => r.assetClass)),
  sector: uniq(RAW_ENRICHED.map(r => r.sector)),
  rating: uniq(RAW_ENRICHED.map(r => r.rating)),
  currency: uniq(RAW_ENRICHED.map(r => r.currency)),
};

const FILTER_FIELDS = [
  { key: "name", label: "Security name", type: "text" },
  { key: "issuer", label: "Issuer", type: "text" },
  { key: "cusip", label: "CUSIP", type: "text" },
  { key: "assetClass", label: "Asset class", type: "enum", options: OPT.assetClass },
  { key: "sector", label: "Sector", type: "enum", options: OPT.sector },
  { key: "rating", label: "Rating", type: "enum", options: OPT.rating },
  { key: "currency", label: "Currency", type: "enum", options: OPT.currency },
  { key: "fairValueLevel", label: "Valuation level", type: "enum", options: ["Level 1", "Level 2", "Level 3"] },
  { key: "complianceStatus", label: "Compliance", type: "enum", options: ["Ready", "Review", "Blocked"] },
  { key: "restrictedLabel", label: "Restricted", type: "enum", options: ["Yes", "No"] },
  { key: "watchlistLabel", label: "On watchlist", type: "enum", options: ["Yes", "No"] },
  { key: "nearMaturityLabel", label: "Maturing ≤12mo", type: "enum", options: ["Yes", "No"] },
  { key: "issueDate", label: "Issue date", type: "date" },
  { key: "maturityDate", label: "Maturity date", type: "date" },
];

const BUILTIN_VIEWS = [
  { id: "all", name: "All securities", rows: [] },
  { id: "restricted", name: "Restricted only", rows: [{ field: "restrictedLabel", op: "is", value: "Yes" }] },
  { id: "maturing", name: "Maturing ≤ 12mo", rows: [{ field: "nearMaturityLabel", op: "is", value: "Yes" }] },
  { id: "level3", name: "Level 3 valuations", rows: [{ field: "fairValueLevel", op: "is", value: "Level 3" }] },
  { id: "watchlist", name: "On watchlist", rows: [{ field: "watchlistLabel", op: "is", value: "Yes" }] },
  { id: "blocked", name: "Compliance blocked", rows: [{ field: "complianceStatus", op: "is", value: "Blocked" }] },
];

function assetClassBadge(assetClass) { return <Badge variant="neutral">{assetClass}</Badge>; }
function flagsCell(r) {
  return r.flags.length
    ? <Tooltip content="Trading restricted"><Badge variant="danger" dot>R</Badge></Tooltip>
    : <span style={{ color: "var(--text-disabled)" }}>—</span>;
}

const BASE_COLUMNS = [
  { key: "name", label: "Security Name", align: "left", width: 240, pinned: true, render: (r) => (
    <div>
      <div style={{ fontWeight: 600, color: "var(--text-primary)", fontFamily: "var(--font-body)" }}>{r.name}</div>
      <div style={{ fontSize: 11, color: "var(--text-muted)", marginTop: 2 }}>{r.shortCode}</div>
    </div>
  ) },
  { key: "cusip", label: "CUSIP", width: 112 },
  { key: "issuer", label: "Issuer", width: 190 },
  { key: "assetClass", label: "Asset Class", width: 138, render: (r) => assetClassBadge(r.assetClass) },
  { key: "currency", label: "Currency", width: 96 },
  { key: "rating", label: "Rating", width: 86 },
  { key: "sector", label: "Sector", width: 150 },
  { key: "fairValueLevel", label: "Val. Level", width: 104 },
  { key: "issueDate", label: "Issue Date", width: 118 },
  { key: "maturityDate", label: "Maturity Date", width: 124 },
  { key: "complianceStatus", label: "Compliance", width: 124, render: (r) => <SeverityBadge status={r.complianceStatus} /> },
  { key: "flags", label: "Flags", width: 80, render: flagsCell },
];

const SORT_OPTIONS = [
  { value: "", label: "None" },
  { value: "maturityDate", label: "Maturity date" },
  { value: "rating", label: "Rating" },
  { value: "sector", label: "Sector" },
  { value: "assetClass", label: "Asset class" },
  { value: "issuer", label: "Issuer" },
];

// ---- detail: provenance ----------------------------------------------------
function ProvenanceBand({ row }) {
  const conf = confidenceFor(row.fairValueLevel);
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
      <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
        <Eyebrow>Lineage &amp; provenance</Eyebrow>
        <span style={{ fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-muted)" }}>
          as of {row.compliance.lastReview}
        </span>
      </div>
      <PanelSurface flat style={{ padding: "12px 14px" }}>
        <EntitySummary columns={5} items={[
          { label: "Primary source", value: row.pricingSource, mono: false },
          { label: "Ingest feed", value: feedFor(row.pricingSource), mono: false },
          { label: "Fair-value level", value: row.fairValueLevel },
          { label: "Data steward", value: row.dataSteward, mono: false },
          { label: "Record confidence", value: conf.label, color: conf.color, mono: false },
        ]} />
      </PanelSurface>
    </div>
  );
}

// ---- detail: overview (with inline editing) --------------------------------
function OverviewSection({ sec, i, editMode, overrides, onStage }) {
  if (sec.type === "fields") {
    const items = sec.fields.map(f => {
      const staged = overrides[f.label];
      const display = staged != null ? staged : f.value;
      if (editMode) {
        return {
          label: f.label,
          value: (
            <EditableCell value={display} mode="text"
              validator={(v) => v.trim().length ? true : "Cannot be empty"}
              onCommit={(v) => onStage(f.label, v.trim(), f.value)} />
          ),
        };
      }
      const changed = staged != null && staged !== f.value;
      return {
        label: f.label,
        value: <span style={{ color: changed ? "var(--accent)" : (f.accent ? "var(--accent)" : undefined), fontWeight: (changed || f.accent) ? 600 : undefined }}>{display}</span>,
      };
    });
    return (
      <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
        <FormSectionLabel>{sec.title}</FormSectionLabel>
        <KeyValueGrid columns={3} items={items} />
      </div>
    );
  }
  if (sec.type === "checklist") {
    return (
      <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
        <FormSectionLabel>{sec.title}</FormSectionLabel>
        <div style={{ display: "flex", flexWrap: "wrap", gap: "10px 24px" }}>
          {sec.items.map((it, j) => <Checkbox key={j} checked={it.checked} disabled label={it.label} onChange={() => {}} />)}
        </div>
      </div>
    );
  }
  if (sec.type === "callout") return <Callout tone={sec.tone} title={sec.title}>{sec.text}</Callout>;
  if (sec.type === "schedule") {
    return (
      <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
        <FormSectionLabel>{sec.title}</FormSectionLabel>
        {sec.rows && sec.rows.length ? (
          <PanelSurface flat style={{ overflow: "hidden" }}>
            <DenseDataTable
              columns={[
                { key: "eventType", label: "Event Type" }, { key: "paymentDate", label: "Payment Date" },
                { key: "index", label: "Index" }, { key: "spread", label: "Spread" },
                { key: "expected", label: "Expected", align: "right" }, { key: "actual", label: "Actual", align: "right" },
                { key: "posted", label: "Posted", render: (r) => <Badge variant={r.posted ? "success" : "neutral"} dot>{r.posted ? "Posted" : "Scheduled"}</Badge> },
              ]}
              rows={sec.rows}
            />
          </PanelSurface>
        ) : (
          <PanelSurface flat><EmptyState icon="table" compact title="No cash flow schedules available"
            detail="Import data or add schedules to view projected and actual amounts." /></PanelSurface>
        )}
      </div>
    );
  }
  return null;
}

function OverviewTab({ row, editMode, staged, onStage }) {
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 18, padding: "18px 2px" }}>
      <ProvenanceBand row={row} />
      <FormDivider />
      {editMode && (
        <Callout tone="info" title="Edit mode is on">
          Click any field to edit. Commits stage to the audit trail — click Save changes in the header to write them.
        </Callout>
      )}
      {row.sections.map((sec, i) => (
        <React.Fragment key={i}>
          {i > 0 && <FormDivider />}
          <OverviewSection sec={sec} i={i} editMode={editMode} overrides={staged} onStage={onStage} />
        </React.Fragment>
      ))}
    </div>
  );
}

function ValidationTab({ row }) {
  return <div style={{ padding: "18px 2px" }}><ValidationIssueList issues={row.validation} emptyLabel="No validation issues on file" /></div>;
}

function ComplianceTab({ row }) {
  const c = row.compliance;
  const checks = complianceChecks(c);
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16, padding: "18px 2px" }}>
      <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
        <Eyebrow>Compliance status</Eyebrow><SeverityBadge status={c.status} />
      </div>
      <KeyValueGrid columns={3} items={[
        { label: "Sanctions screened", value: c.sanctionsScreened ? "Yes" : "No" },
        { label: "On watchlist", value: c.watchlist ? "Yes" : "No" },
        { label: "Last review", value: c.lastReview },
        { label: "Reviewed by", value: c.reviewedBy },
      ]} />
      <FormDivider />
      <ValidationIssueList issues={checks} />
      {c.restricted && <Callout tone="danger" title="Restricted">
        This security is on the restricted list. See Operational flags on the Overview tab for detail.
      </Callout>}
    </div>
  );
}

function NotesTab({ row, notes, onAdd }) {
  const [draft, setDraft] = useState("");
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16, padding: "18px 2px", maxWidth: 720 }}>
      {notes.length === 0 ? (
        <EmptyState icon="inbox" compact title="No notes yet" detail="Add the first note below." />
      ) : (
        <div style={{ display: "flex", flexDirection: "column" }}>
          {notes.map((n, i) => (
            <div key={i} style={{ padding: "10px 0", borderTop: i > 0 ? "1px solid var(--border)" : "none" }}>
              <div style={{ display: "flex", gap: 8, alignItems: "baseline", marginBottom: 3 }}>
                <span style={{ fontWeight: 600, fontSize: 12, color: "var(--text-primary)" }}>{n.author}</span>
                <span style={{ fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-muted)" }}>{n.ts}</span>
              </div>
              <div style={{ fontSize: 13, color: "var(--text-secondary)", lineHeight: 1.5 }}>{n.text}</div>
            </div>
          ))}
        </div>
      )}
      <FormDivider />
      <TextArea label="Add a note" rows={3} value={draft} onChange={(e) => setDraft(e.target.value)}
        placeholder="Log a decision, review outcome, or context for the next operator…" />
      <div><Button variant="primary" size="sm" disabled={!draft.trim()}
        onClick={() => { onAdd(draft.trim()); setDraft(""); }}>Add note</Button></div>
    </div>
  );
}

function AuditTab({ auditRows }) {
  return (
    <div style={{ padding: "18px 2px" }}>
      <PanelSurface flat style={{ overflow: "hidden" }}>
        <DenseDataTable
          columns={[
            { key: "ts", label: "Timestamp" }, { key: "user", label: "User" }, { key: "field", label: "Field" },
            { key: "oldValue", label: "Old Value" }, { key: "newValue", label: "New Value" },
          ]}
          rows={auditRows}
        />
      </PanelSurface>
    </div>
  );
}

function DetailView({ row, onBack, notesById, onAddNote, auditExtra, onCommitEdits }) {
  const [tab, setTab] = useState(0);
  const [editMode, setEditMode] = useState(false);
  const [staged, setStaged] = useState({}); // label -> newValue
  const notes = notesById[row.id] || row.notesSeed;
  const auditRows = [...(auditExtra[row.id] || []), ...row.audit];
  const stagedCount = Object.keys(staged).length;

  const onStage = (label, value, original) => {
    setStaged(prev => {
      const next = { ...prev };
      if (value === original) delete next[label]; else next[label] = value;
      return next;
    });
  };
  const save = () => {
    onCommitEdits(row.id, staged);
    toast("success", `${stagedCount} field${stagedCount === 1 ? "" : "s"} updated`, "Changes written to the audit trail.");
    setStaged({}); setEditMode(false);
  };
  const discard = () => { setStaged({}); toast("info", "Changes discarded"); };

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 14, maxWidth: 1180 }}>
      <Breadcrumb items={[{ label: "Security Master", onClick: onBack }, { label: row.name }]} />
      <div style={{ display: "flex", alignItems: "flex-start", gap: 14 }}>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 10, flexWrap: "wrap" }}>
            <h1 style={{ font: "600 20px var(--font-display)", margin: 0, color: "var(--text-primary)" }}>{row.name}</h1>
            {assetClassBadge(row.assetClass)}
            {row.flags.map((f, i) => (
              <Tooltip key={i} content={f === "Restricted" ? "Trading restricted — see Compliance tab" : f}>
                <Badge variant="danger" dot>{f}</Badge>
              </Tooltip>
            ))}
          </div>
          <div style={{ fontFamily: "var(--font-data)", fontSize: 12, color: "var(--text-muted)", marginTop: 4 }}>
            {row.shortCode} · {row.issuer}
          </div>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 10, flexShrink: 0 }}>
          {editMode && stagedCount > 0 && (
            <React.Fragment>
              <Badge variant="info">{stagedCount} unsaved</Badge>
              <Button variant="ghost" size="sm" onClick={discard}>Discard</Button>
              <Button variant="primary" size="sm" onClick={save}>Save changes</Button>
            </React.Fragment>
          )}
          <Toggle checked={editMode} onChange={(v) => { setEditMode(v); if (!v) setStaged({}); }} label="Edit Mode" />
        </div>
      </div>

      <PanelSurface raised style={{ padding: "0 18px 18px" }}>
        <Tabs defaultTab={0} onChange={setTab} tabs={[
          { label: "Overview" }, { label: "Validation", count: row.validation.length },
          { label: "Compliance" }, { label: "Notes & Comments", count: notes.length },
          { label: "Audit History", count: auditRows.length },
        ]}>
          <TabPanel><OverviewTab row={row} editMode={editMode} staged={staged} onStage={onStage} /></TabPanel>
          <TabPanel><ValidationTab row={row} /></TabPanel>
          <TabPanel><ComplianceTab row={row} /></TabPanel>
          <TabPanel><NotesTab row={row} notes={notes} onAdd={(text) => onAddNote(row.id, text)} /></TabPanel>
          <TabPanel><AuditTab auditRows={auditRows} /></TabPanel>
        </Tabs>
      </PanelSurface>
    </div>
  );
}

// ---- saved-views bar -------------------------------------------------------
function SavedViews({ views, activeId, onSelect, onSave, onDelete }) {
  const [naming, setNaming] = useState(false);
  const [name, setName] = useState("");
  return (
    <div style={{ display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap" }}>
      <Eyebrow>Views</Eyebrow>
      <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
        {views.map(v => {
          const active = v.id === activeId;
          return (
            <span key={v.id} style={{ display: "inline-flex", alignItems: "center" }}>
              <Button variant={active ? "primary" : "ghost"} size="sm" onClick={() => onSelect(v)}>{v.name}</Button>
              {v.custom && (
                <button onClick={() => onDelete(v.id)} title="Delete view"
                  style={{ marginLeft: -2, marginRight: 2, border: "none", background: "transparent", cursor: "pointer",
                    color: "var(--text-muted)", fontSize: 13, lineHeight: 1, padding: "0 4px" }}>×</button>
              )}
            </span>
          );
        })}
      </div>
      <div style={{ flex: 1 }}></div>
      {naming ? (
        <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
          <div style={{ width: 180 }}>
            <Input autoFocus placeholder="View name…" value={name} onChange={(e) => setName(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter" && name.trim()) { onSave(name.trim()); setName(""); setNaming(false); } }} />
          </div>
          <Button variant="primary" size="sm" disabled={!name.trim()}
            onClick={() => { onSave(name.trim()); setName(""); setNaming(false); }}>Save</Button>
          <Button variant="ghost" size="sm" onClick={() => { setNaming(false); setName(""); }}>Cancel</Button>
        </div>
      ) : (
        <Button variant="ghost" size="sm" onClick={() => setNaming(true)}>Save current filter as view</Button>
      )}
    </div>
  );
}

// ---- list view -------------------------------------------------------------
function ListView(props) {
  const {
    rows, total, query, setQuery, density, setDensity, sortKey, sortDir, onSort, secondarySort, setSecondarySort,
    onOpen, cols, showFilters, setShowFilters, filterRows, setFilterRows, filterCount,
    views, activeViewId, onSelectView, onSaveView, onDeleteView,
    selectedIds, onToggleRow, onSelectAllPage, pageRows, cursorIndex, searchRef,
    page, totalPages, setPage, loading,
  } = props;

  const selectedRowsIdx = pageRows.map((r, i) => selectedIds.has(r.id) ? i : -1).filter(i => i >= 0);

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
      <div style={{ display: "flex", alignItems: "center", gap: 12, flexWrap: "wrap" }}>
        <h1 style={{ font: "600 22px var(--font-display)", margin: 0, color: "var(--text-primary)" }}>Security Master</h1>
        <span style={{ fontFamily: "var(--font-data)", fontSize: 12, color: "var(--text-muted)" }}>
          {rows.length} of {total} securities
        </span>
        <div style={{ flex: 1 }}></div>
        <Button variant="ghost" size="sm">Export all</Button>
        <Button variant="ghost" size="sm">Import data</Button>
        <Button variant="primary" size="sm">Add security</Button>
      </div>

      <SavedViews views={views} activeId={activeViewId} onSelect={onSelectView} onSave={onSaveView} onDelete={onDeleteView} />

      <div style={{ display: "flex", alignItems: "center", gap: 10, flexWrap: "wrap" }}>
        <div style={{ width: 280 }}>
          <Input ref={searchRef} placeholder="Search name, issuer, CUSIP, sector, rating…" value={query}
            onChange={(e) => setQuery(e.target.value)} />
        </div>
        <Button variant={showFilters ? "primary" : "ghost"} size="sm" onClick={() => setShowFilters(f => !f)}>
          Filters{filterCount ? ` · ${filterCount}` : ""}
        </Button>
        <div style={{ flex: 1 }}></div>
        <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
          <span style={{ fontSize: 11, color: "var(--text-muted)", fontVariant: "all-small-caps", letterSpacing: ".04em" }}>then by</span>
          <div style={{ width: 150 }}>
            <Select value={secondarySort} onChange={setSecondarySort} options={SORT_OPTIONS} />
          </div>
        </div>
        <ColumnManager cols={cols} />
        <SegmentedControl size="sm" options={["Comfortable", "Compact"]} value={density} onChange={setDensity} />
      </div>

      {showFilters && (
        <PanelSurface flat style={{ padding: 14 }}>
          <FilterBuilder fields={FILTER_FIELDS} value={filterRows} onChange={setFilterRows} showApply={false}
            summary={`${rows.length} of ${total} rows`} />
        </PanelSurface>
      )}

      <PanelSurface flat style={{ overflow: "hidden" }}>
        {loading ? (
          <SkeletonTable rows={PAGE_SIZE} columns={cols.visibleColumns.length + 1} />
        ) : pageRows.length ? (
          <DenseDataTable
            columns={cols.visibleColumns} pinnedKeys={cols.pinnedKeys}
            onColumnResize={cols.resize} onColumnReorder={cols.reorder} onColumnPin={cols.togglePin}
            sortKey={sortKey} sortDir={sortDir} onSort={onSort}
            selectable selectedRows={selectedRowsIdx}
            onSelectRow={(r) => onToggleRow(r.id)} onSelectAll={onSelectAllPage}
            selectedIndex={cursorIndex}
            onRowClick={(r) => onOpen(r.id)}
            rows={pageRows}
          />
        ) : (
          <EmptyState icon="search" title="No securities match your filters"
            detail="Clear a filter or try a different search term." />
        )}
      </PanelSurface>

      {!loading && totalPages > 1 && (
        <div style={{ display: "flex", justifyContent: "flex-end" }}>
          <Pagination currentPage={page} totalPages={totalPages} onPageChange={setPage}
            totalItems={rows.length} itemsPerPage={PAGE_SIZE} />
        </div>
      )}

      <div style={{ display: "flex", gap: 14, alignItems: "center", fontSize: 11, color: "var(--text-muted)", flexWrap: "wrap" }}>
        <span style={{ display: "inline-flex", gap: 5, alignItems: "center" }}><Kbd>J</Kbd><Kbd>K</Kbd> move</span>
        <span style={{ display: "inline-flex", gap: 5, alignItems: "center" }}><Kbd>Enter</Kbd> open</span>
        <span style={{ display: "inline-flex", gap: 5, alignItems: "center" }}><Kbd>Space</Kbd> select</span>
        <span style={{ display: "inline-flex", gap: 5, alignItems: "center" }}><Kbd>/</Kbd> search</span>
        <span style={{ display: "inline-flex", gap: 5, alignItems: "center" }}><Kbd>F</Kbd> filters</span>
      </div>
    </div>
  );
}

// ---- screen ----------------------------------------------------------------
function SecurityMasterRegistryScreen() {
  const [view, setView] = useState("list");
  const [selectedId, setSelectedId] = useState(null);
  const [query, setQuery] = useState("");
  const [density, setDensity] = useState("Comfortable");
  const [sortKey, setSortKey] = useState("name");
  const [sortDir, setSortDir] = useState("asc");
  const [secondarySort, setSecondarySort] = useState("");
  const [notesById, setNotesById] = useState({});
  const [showFilters, setShowFilters] = useState(false);
  const [filterRows, setFilterRows] = useState([]);
  const [activeViewId, setActiveViewId] = useState("all");
  const [customViews, setCustomViews] = useState([]);
  const [selectedIds, setSelectedIds] = useState(() => new Set());
  const [page, setPage] = useState(1);
  const [cursor, setCursor] = useState(0);
  const [loading, setLoading] = useState(true);
  const [overrides, setOverrides] = useState({}); // id -> { restricted, complianceStatus, dataSteward, flags }
  const [auditExtra, setAuditExtra] = useState({}); // id -> [audit rows]
  const [fieldEdits, setFieldEdits] = useState({}); // id -> { label: value } (committed)
  const searchRef = useRef(null);

  const cols = useTableColumns(BASE_COLUMNS, { persistKey: "smr.cols.v1" });

  useEffect(() => { document.body.dataset.themeDensity = density === "Compact" ? "compact" : ""; }, [density]);
  useEffect(() => { setLoading(true); const t = setTimeout(() => setLoading(false), 650); return () => clearTimeout(t); }, []);
  useEffect(() => {
    const stored = JSON.parse(localStorage.getItem(VIEWS_KEY) || "null");
    if (Array.isArray(stored)) setCustomViews(stored);
  }, []);

  const views = useMemo(() => [...BUILTIN_VIEWS, ...customViews], [customViews]);

  const onSort = (k) => { if (k === sortKey) setSortDir(d => d === "asc" ? "desc" : "asc"); else { setSortKey(k); setSortDir("asc"); } };

  const enriched = useMemo(() => SECURITIES.map(r => enrichRow(r, overrides[r.id])), [overrides]);

  const predicate = useMemo(() => {
    try { return FilterBuilder.predicate ? FilterBuilder.predicate(FILTER_FIELDS, filterRows) : () => true; }
    catch (e) { return () => true; }
  }, [filterRows]);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    let rows = enriched.filter(r => {
      if (!predicate(r)) return false;
      if (!q) return true;
      return [r.name, r.issuer, r.cusip, r.shortCode, r.sector, r.rating, r.assetClass, r.currency, r.isin, r.ticker]
        .some(v => (v || "").toLowerCase().includes(q));
    });
    rows = [...rows].sort((a, b) => {
      const p = String(a[sortKey] ?? "").localeCompare(String(b[sortKey] ?? ""));
      const pr = sortDir === "asc" ? p : -p;
      if (pr !== 0) return pr;
      if (secondarySort) { const s = String(a[secondarySort] ?? "").localeCompare(String(b[secondarySort] ?? "")); if (s !== 0) return s; }
      return String(a.name).localeCompare(String(b.name));
    });
    return rows;
  }, [enriched, predicate, query, sortKey, sortDir, secondarySort]);

  // reset page + cursor when the result set changes shape
  useEffect(() => { setPage(1); setCursor(0); }, [query, filterRows, sortKey, sortDir, secondarySort]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const pageRows = useMemo(() => filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE), [filtered, page]);

  // saved views
  const selectView = (v) => { setFilterRows(v.rows); setActiveViewId(v.id); if (v.rows.length) setShowFilters(true); };
  useEffect(() => {
    const match = views.find(v => JSON.stringify(v.rows) === JSON.stringify(filterRows));
    setActiveViewId(match ? match.id : null);
  }, [filterRows, views]);
  const saveView = (name) => {
    const v = { id: "cv-" + Date.now(), name, rows: filterRows, custom: true };
    const next = [...customViews, v];
    setCustomViews(next); localStorage.setItem(VIEWS_KEY, JSON.stringify(next));
    toast("success", "View saved", name);
  };
  const deleteView = (id) => {
    const next = customViews.filter(v => v.id !== id);
    setCustomViews(next); localStorage.setItem(VIEWS_KEY, JSON.stringify(next));
  };

  // selection
  const toggleRow = (id) => setSelectedIds(prev => { const n = new Set(prev); n.has(id) ? n.delete(id) : n.add(id); return n; });
  const selectAllPage = (isSel) => setSelectedIds(prev => {
    const n = new Set(prev); pageRows.forEach(r => isSel ? n.add(r.id) : n.delete(r.id)); return n;
  });
  const selectAllFiltered = () => setSelectedIds(new Set(filtered.map(r => r.id)));
  const clearSelection = () => setSelectedIds(new Set());

  const bulkFlagRestricted = () => {
    setOverrides(prev => {
      const n = { ...prev };
      selectedIds.forEach(id => {
        const base = enrichRow(SECURITIES.find(r => r.id === id), prev[id]);
        if (!base.restricted) n[id] = { ...n[id], restricted: true, flags: [...new Set([...base.flags, "Restricted"])] };
      });
      return n;
    });
    const now = new Date().toISOString().slice(0, 16).replace("T", " ") + "Z";
    setAuditExtra(prev => {
      const n = { ...prev };
      selectedIds.forEach(id => { n[id] = [{ ts: now, user: "You", field: "Restricted flag", oldValue: "false", newValue: "true" }, ...(n[id] || [])]; });
      return n;
    });
    toast("success", `${selectedIds.size} flagged restricted`, "Audit entries written.");
    clearSelection();
  };
  const bulkScreen = () => { toast("info", "Sanctions screen queued", `${selectedIds.size} securities submitted.`); clearSelection(); };
  const bulkExport = () => { toast("success", "Export started", `${selectedIds.size} securities → CSV.`); };

  const commitEdits = (id, edits) => {
    setFieldEdits(prev => ({ ...prev, [id]: { ...(prev[id] || {}), ...edits } }));
    const now = new Date().toISOString().slice(0, 16).replace("T", " ") + "Z";
    const src = SECURITIES.find(r => r.id === id);
    const rows = Object.entries(edits).map(([label, val]) => ({ ts: now, user: "You", field: label, oldValue: anyFieldVal(src, label) || "—", newValue: val }));
    setAuditExtra(prev => ({ ...prev, [id]: [...rows, ...(prev[id] || [])] }));
  };

  // keyboard: j/k cursor, enter open, space select
  useEffect(() => {
    if (view !== "list") return;
    const handler = (e) => {
      const typing = /^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement?.tagName || "") || document.activeElement?.isContentEditable;
      if (typing) return;
      if (e.key === "j" || e.key === "ArrowDown") { e.preventDefault(); setCursor(c => Math.min(pageRows.length - 1, c + 1)); }
      else if (e.key === "k" || e.key === "ArrowUp") { e.preventDefault(); setCursor(c => Math.max(0, c - 1)); }
      else if (e.key === "Enter") { const r = pageRows[cursor]; if (r) { setSelectedId(r.id); setView("detail"); } }
      else if (e.key === " ") { const r = pageRows[cursor]; if (r) { e.preventDefault(); toggleRow(r.id); } }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [view, pageRows, cursor]);

  // apply committed field edits onto the selected row for the detail view
  const selectedRow = useMemo(() => {
    const r = enriched.find(x => x.id === selectedId);
    if (!r) return null;
    const edits = fieldEdits[r.id];
    if (!edits) return r;
    return {
      ...r,
      sections: r.sections.map(s => s.type === "fields"
        ? { ...s, fields: s.fields.map(f => edits[f.label] != null ? { ...f, value: edits[f.label] } : f) }
        : s),
    };
  }, [enriched, selectedId, fieldEdits]);

  const onAddNote = (id, text) => setNotesById(prev => {
    const row = SECURITIES.find(r => r.id === id);
    const existing = prev[id] || row.notesSeed;
    const now = new Date().toISOString().slice(0, 16).replace("T", " ") + "Z";
    return { ...prev, [id]: [...existing, { author: "You", ts: now, text }] };
  });

  const bindings = [
    { keys: "g d", label: "Go to Dashboard", group: "Navigate", action: () => {} },
    { keys: "g s", label: "Go to Security Master", group: "Navigate", action: () => setView("list") },
    { keys: "/", label: "Focus search", group: "Registry", action: () => { setView("list"); setTimeout(() => searchRef.current?.focus?.(), 0); } },
    { keys: "f", label: "Toggle filters", group: "Registry", action: () => setShowFilters(f => !f) },
    { keys: "a", label: "Add security", group: "Registry", action: () => {} },
  ];

  return (
    <React.Fragment>
      <ToastProvider />
      <HotkeysProvider bindings={bindings} />
      <WorkstationTopbar moduleLabel="Security Master" environment="PAPER" clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />
      <div style={{ display: "flex", flex: 1, minHeight: 0 }} data-screen-label={view === "list" ? "Security Master — List" : "Security Master — Detail"}>
        <NavRail activeId="security-master" onSelect={() => {}} sections={[
          { label: "Data", items: [
            { id: "dashboard", label: "Dashboard", icon: "../../assets/icons/dashboard.svg", shortcut: "G D" },
            { id: "security-master", label: "Security Master", icon: "../../assets/icons/security-master.svg", shortcut: "G S" },
            { id: "data-browser", label: "Data Browser", icon: "../../assets/icons/data-browser.svg" },
            { id: "data-quality", label: "Data Quality", icon: "../../assets/icons/data-quality.svg" },
          ]},
          { label: "Research", items: [
            { id: "backtest", label: "Backtest", icon: "../../assets/icons/backtest.svg" },
            { id: "charting", label: "Charting", icon: "../../assets/icons/charting.svg" },
          ]},
        ]} />
        <main style={{ flex: 1, minWidth: 0, overflowY: "auto", padding: 20 }}>
          {view === "list" ? (
            <ListView
              rows={filtered} total={SECURITIES.length} query={query} setQuery={setQuery}
              density={density} setDensity={setDensity}
              sortKey={sortKey} sortDir={sortDir} onSort={onSort}
              secondarySort={secondarySort} setSecondarySort={setSecondarySort}
              onOpen={(id) => { setSelectedId(id); setView("detail"); }}
              cols={cols}
              showFilters={showFilters} setShowFilters={setShowFilters}
              filterRows={filterRows} setFilterRows={setFilterRows} filterCount={filterRows.filter(r => r.field && r.op).length}
              views={views} activeViewId={activeViewId} onSelectView={selectView} onSaveView={saveView} onDeleteView={deleteView}
              selectedIds={selectedIds} onToggleRow={toggleRow} onSelectAllPage={selectAllPage}
              pageRows={pageRows} cursorIndex={cursor} searchRef={searchRef}
              page={page} totalPages={totalPages} setPage={setPage} loading={loading}
            />
          ) : (
            <DetailView row={selectedRow} onBack={() => setView("list")} notesById={notesById} onAddNote={onAddNote}
              auditExtra={auditExtra} onCommitEdits={commitEdits} />
          )}
        </main>
      </div>

      {view === "list" && selectedIds.size > 0 && (
        <SelectionToolbar
          count={selectedIds.size} total={filtered.length}
          onSelectAll={selectAllFiltered} onClear={clearSelection}
          primaryAction={{ label: "Export selected", onClick: bulkExport, variant: "primary" }}
          actions={[
            { label: "Flag restricted", onClick: bulkFlagRestricted, variant: "danger", tooltip: "Add to the restricted list" },
            { label: "Screen sanctions", onClick: bulkScreen, tooltip: "Queue a sanctions screen" },
          ]}
        />
      )}

      <StatusBar items={[
        { status: "ok", label: "Connected", value: "IBKR · Polygon · Databento" },
        { label: "Master", value: `${SECURITIES.length} securities` },
        { label: "Shown", value: `${filtered.length}` },
        { label: "Selected", value: selectedIds.size ? `${selectedIds.size}` : "—" },
        { status: "ok", label: "Index", value: "fresh", push: true },
      ]} />
    </React.Fragment>
  );
}

window.SecurityMasterRegistryScreen = SecurityMasterRegistryScreen;
if (typeof module !== "undefined") module.exports = { SecurityMasterRegistryScreen };
