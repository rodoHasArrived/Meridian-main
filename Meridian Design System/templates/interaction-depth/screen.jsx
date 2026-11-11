// Interaction-depth reference — the one work plane that exercises the whole interaction cluster:
// 50k-row virtualization, right-click, bulk edit, saved views, column chooser, a keyboard-only path,
// optimistic edits with revert, annotations, and comparison by selection. Normative source for the
// behaviours (docs/UX_DEPTH_4_1.md › D-3), the way analytical-workbench is for the figure grammar.
function InteractionDepthPlane({ rowCount = 50000, compareMax = 6, objectVersion = 3, onSummary, onApi, onAnnotationsChange }) {
  const DS = window.MeridianDesignSystem_205d17 || {};
  const { DataGrid, ContextMenu, SavedViews, ColumnChooser, SelectionToolbar, OptimisticProvider, OptimisticCell, OptimisticRowBar, OptimisticHooks,
    AnnotationProvider, AnnotationAnchor, AnnotationPanel, SelectionCompareProvider, CompareToggle, CompareTray, CompareSheet, Format, Eyebrow } = DS;
  const fmt = (v, d = 2) => (Format && Format.formatNumber ? Format.formatNumber(v, { decimals: d }) : Number(v).toFixed(d));
  const price = (v) => (Format && Format.formatPrice ? Format.formatPrice(v, { decimals: 2 }) : Number(v).toFixed(2));

  const SECTORS = ["Technology", "Financials", "Energy", "Health care", "Industrials", "Utilities", "Materials", "Consumer"];
  const VENUES = ["XNAS", "XNYS", "XLON", "XETR", "XTKS", "XASX"];
  const TENORS = ["1D", "1W", "1M", "3M", "1Y"];
  const rows = React.useMemo(() => {
    let s = 7;
    const rnd = () => { s = (s * 1103515245 + 12345) % 2147483648; return s / 2147483648; };
    const out = new Array(rowCount);
    for (let i = 0; i < rowCount; i++) {
      const sym = String.fromCharCode(65 + (i % 26)) + String.fromCharCode(65 + ((i / 26) | 0) % 26) + String.fromCharCode(65 + ((i / 676) | 0) % 26) + (i >= 17576 ? String((i / 17576) | 0) : "");
      const p = 4 + rnd() * 480;
      out[i] = { id: `${sym}-${i}`, symbol: sym, name: `${sym} Holdings ${(i % 97) + 1}`, sector: SECTORS[i % SECTORS.length], venue: VENUES[(i * 7) % VENUES.length],
        price: p, qty: Math.round(rnd() * 20000) * 5, limit: Math.round(50 + rnd() * 950) * 1000, budget: [180, 250, 400][i % 3],
        curve: TENORS.map((_, k) => (rnd() - 0.45) * (k + 1) * 1.6), status: i % 11 === 0 ? "Restricted" : i % 5 === 0 ? "Provisional" : "Confirmed" };
    }
    return out;
  }, [rowCount]);

  const [sector, setSector] = React.useState("All");
  const [query, setQuery] = React.useState("");
  const [sortKey, setSortKey] = React.useState("symbol");
  const [sortDir, setSortDir] = React.useState("asc");
  const ALL_COLS = [["cmp", "Compare"], ["symbol", "Symbol"], ["name", "Name"], ["sector", "Sector"], ["venue", "Venue"], ["price", "Price"], ["qty", "Quantity"], ["limit", "Exposure limit"], ["budget", "Ack budget"], ["status", "Condition"]];
  const [visible, setVisible] = React.useState(ALL_COLS.map((c) => c[0]).filter((k) => k !== "budget"));
  const [selected, setSelected] = React.useState([]);
  const [active, setActive] = React.useState(null);
  const [menu, setMenu] = React.useState(null);
  const [model, setModel] = React.useState({});
  const planeRef = React.useRef(null);

  const filtered = React.useMemo(() => {
    const q = query.trim().toLowerCase();
    let r = rows;
    if (sector !== "All") r = r.filter((x) => x.sector === sector);
    if (q) r = r.filter((x) => x.symbol.toLowerCase().startsWith(q) || x.name.toLowerCase().includes(q));
    const dir = sortDir === "asc" ? 1 : -1;
    r = r.slice().sort((a, b) => { const va = a[sortKey], vb = b[sortKey]; return (va < vb ? -1 : va > vb ? 1 : 0) * dir; });
    return r;
  }, [rows, sector, query, sortKey, sortDir]);
  const valueOf = (r, f) => (model[r.id] && model[r.id][f] != null ? model[r.id][f] : r[f]);

  // Fixture write path: ~700ms; policy rejects a budget under 100ms or a limit above 50m.
  const commit = React.useCallback((e) => new Promise((ok, no) => setTimeout(() => {
    const n = Number(String(e.value).replace(/[^0-9.]/g, ""));
    if (e.field === "budget" && n < 100) return no(new Error("Policy · acknowledgement budget floor is 100ms for this venue class (RISK-014)."));
    if (e.field === "limit" && n > 50000000) return no(new Error("Policy · a limit above 50,000,000 needs a second approver; route through Change control."));
    ok({ receipt: `chg-${1000 + Math.floor(Math.random() * 9000)}` });
  }, 700)), []);
  const revert = React.useCallback(() => new Promise((ok) => setTimeout(ok, 400)), []);
  const onSettle = React.useCallback((e) => {
    if (e.status === "confirmed") setModel((m) => ({ ...m, [e.rowId]: { ...(m[e.rowId] || {}), [e.field]: e.value } }));
    if (e.status === "reverted") setModel((m) => ({ ...m, [e.rowId]: { ...(m[e.rowId] || {}), [e.field]: e.prior } }));
  }, []);

  React.useEffect(() => { onSummary && onSummary({ total: rows.length, shown: filtered.length, selected: selected.length, sector, active }); }, [rows.length, filtered.length, selected.length, sector, active]);

  const item = (r) => ({ id: r.id, title: r.symbol, tag: r.venue, values: r.curve, footerValue: `${fmt(r.curve[4], 1)}%` });
  const num = (v) => (/^[0-9,.]+$/.test(String(v).trim()) ? true : "Digits only");
  const cell = (r, f, extra) => <OptimisticCell rowId={r.id} field={f} value={valueOf(r, f)} align="right" validator={num} {...extra} />;

  const COLS = {
    cmp: { key: "cmp", label: "", width: 44, sortable: false, cell: (r) => <CompareToggle compact item={item(r)} /> },
    symbol: { key: "symbol", label: "Symbol", width: 90, cell: (r) => <AnnotationAnchor anchor={{ kind: "cell", ref: `row:${r.id}/symbol`, label: r.symbol }} popover="left"><b style={{ font: "600 12px var(--font-data)", color: "var(--text-primary)" }}>{r.symbol}</b></AnnotationAnchor> },
    name: { key: "name", label: "Name" }, sector: { key: "sector", label: "Sector" }, venue: { key: "venue", label: "Venue", width: 70 },
    price: { key: "price", label: "Price", align: "right", width: 100, cell: (r) => <span style={{ fontVariantNumeric: "tabular-nums" }}>{price(r.price)}</span> },
    qty: { key: "qty", label: "Quantity", align: "right", width: 110, cell: (r) => <span style={{ fontVariantNumeric: "tabular-nums" }}>{fmt(r.qty, 0)}</span> },
    limit: { key: "limit", label: "Exposure limit", align: "right", width: 170, cell: (r) => cell(r, "limit", { format: (v) => fmt(String(v).replace(/,/g, ""), 0), hint: "Enter commits · ~0.7s write" }) },
    budget: { key: "budget", label: "Ack budget (ms)", align: "right", width: 150, cell: (r) => cell(r, "budget", { mode: "number", hint: "Below 100 rejects" }) },
    status: { key: "status", label: "Condition", width: 110 },
  };
  const columns = visible.map((k) => COLS[k]).filter(Boolean);

  const viewState = () => ({ sector, query, sortKey, sortDir, visible });
  const applyView = (s) => { if (!s) { setSector("All"); setQuery(""); setSortKey("symbol"); setSortDir("asc"); setVisible(ALL_COLS.map((c) => c[0]).filter((k) => k !== "budget")); return; } setSector(s.sector ?? "All"); setQuery(s.query ?? ""); setSortKey(s.sortKey ?? "symbol"); setSortDir(s.sortDir ?? "asc"); if (s.visible) setVisible(s.visible); };
  const PRESETS = [
    { name: "Limits review", state: { sector: "All", query: "", sortKey: "limit", sortDir: "desc", visible: ["cmp", "symbol", "name", "limit", "budget", "status"] } },
    { name: "Energy · by venue", state: { sector: "Energy", query: "", sortKey: "venue", sortDir: "asc", visible: ["cmp", "symbol", "venue", "price", "qty", "limit"] } },
  ];

  function Inner() {
    const ledger = OptimisticHooks.useOptimistic();
    const bulk = (field, value) => { const ids = new Set(selected); filtered.forEach((r) => { if (ids.has(r.id)) ledger.commit({ rowId: r.id, field, prior: valueOf(r, field), value }); }); };
    const move = (d) => { const i = filtered.findIndex((r) => r.id === active); const n = Math.min(filtered.length - 1, Math.max(0, (i < 0 ? -1 : i) + d)); if (filtered[n]) setActive(filtered[n].id); };
    React.useEffect(() => { onApi && onApi({ selectAll: () => setSelected(filtered.map((r) => r.id)), clear: () => setSelected([]), bulkBudget: (v) => bulk("budget", v), focusGrid: () => planeRef.current && planeRef.current.focus(), move }); }, [filtered, selected, active]);
    const onKey = (e) => {
      if (e.target.closest("input,textarea,[role=dialog]")) return;
      if (e.key === "j" || e.key === "ArrowDown") { e.preventDefault(); move(1); }
      else if (e.key === "k" || e.key === "ArrowUp") { e.preventDefault(); move(-1); }
      else if (e.key === "x" && active) { e.preventDefault(); setSelected((s) => (s.includes(active) ? s.filter((k) => k !== active) : [...s, active])); }
      else if (e.key === "c" && active) { e.preventDefault(); const r = filtered.find((x) => x.id === active); if (r) window.__mdsCompareToggle && window.__mdsCompareToggle(item(r)); }
      else if (e.key === "Escape") { setSelected([]); setMenu(null); }
    };
    const activeRow = filtered.find((r) => r.id === active);
    return (
      <div ref={planeRef} tabIndex={0} aria-label="Instrument grid · j/k move, x select, c compare, right-click for verbs" onKeyDown={onKey} style={{ height: "100%", display: "flex", flexDirection: "column", gap: 8, outline: "none", minHeight: 0 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap" }}>
          <SavedViews storageKey="mds.interaction-depth" getState={viewState} applyState={applyView} presets={PRESETS} defaultLabel="All instruments" />
          <label style={{ display: "inline-flex", alignItems: "center", gap: 6, font: "11px var(--font-data)", color: "var(--text-muted)" }}>Sector
            <select value={sector} onChange={(e) => setSector(e.target.value)} style={{ height: "var(--theme-control-height)", border: "1px solid var(--border)", borderRadius: "var(--radius-button)", background: "var(--bg-light)", color: "var(--text-primary)", font: "12px var(--font-body)", padding: "0 6px" }}>
              {["All", ...SECTORS].map((s) => <option key={s}>{s}</option>)}
            </select></label>
          <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Symbol or name" aria-label="Filter by symbol or name" style={{ height: "var(--theme-control-height)", width: 180, boxSizing: "border-box", border: "1px solid var(--border)", borderRadius: "var(--radius-button)", background: "var(--bg-light)", color: "var(--text-primary)", font: "12px var(--font-body)", padding: "0 8px" }} />
          <span style={{ flex: 1 }} />
          <span style={{ font: "11px var(--font-data)", color: "var(--text-muted)", fontVariantNumeric: "tabular-nums" }}>{fmt(filtered.length, 0)} of {fmt(rows.length, 0)} rows · windowed</span>
          <ColumnChooser columns={ALL_COLS.map(([key, label]) => ({ key, label }))} visible={visible} onChange={setVisible} />
        </div>
        {selected.length > 0 && (
          <SelectionToolbar count={selected.length} total={filtered.length} onSelectAll={() => setSelected(filtered.map((r) => r.id))} onClear={() => setSelected([])}
            primaryAction={{ label: "Set ack budget → 250", onClick: () => bulk("budget", "250") }}
            actions={[{ label: "Set budget → 80 (rejects)", onClick: () => bulk("budget", "80") }, { label: "Raise limits ×1.1", onClick: () => { const ids = new Set(selected); filtered.forEach((r) => { if (ids.has(r.id)) ledger.commit({ rowId: r.id, field: "limit", prior: valueOf(r, "limit"), value: String(Math.round(Number(valueOf(r, "limit")) * 1.1)) }); }); } }]} />
        )}
        <OptimisticRowBar rowIds={selected.length ? selected : undefined} label={selected.length ? `edits in ${selected.length} selected` : "edits on this screen"} />
        <div style={{ flex: 1, minHeight: 0 }} onContextMenu={(e) => { const tr = e.target.closest("tr[data-key],[data-row-key],tr"); e.preventDefault(); const key = tr && (tr.getAttribute("data-key") || tr.getAttribute("data-row-key")); if (key) setActive(key); setMenu({ x: e.clientX, y: e.clientY, key: key || active }); }}>
          <DataGrid rowKey="id" columns={columns} rows={filtered} select selected={selected} onSelectedChange={setSelected} activeKey={active} onRowClick={(r) => setActive(r.id)}
            sortKey={sortKey} sortDir={sortDir} onSort={(k) => { if (k === sortKey) setSortDir((d) => (d === "asc" ? "desc" : "asc")); else { setSortKey(k); setSortDir("asc"); } }}
            virtualize maxHeight="100%" label="Instruments" style={{ height: "100%" }} />
        </div>
        {menu && ContextMenu && (() => { const r = filtered.find((x) => x.id === menu.key) || activeRow; return (
          <ContextMenu x={menu.x} y={menu.y} onClose={() => setMenu(null)} items={r ? [
            { label: `Open ${r.symbol}`, onClick: () => setActive(r.id) },
            { label: selected.includes(r.id) ? "Deselect row" : "Select row", onClick: () => setSelected((s) => (s.includes(r.id) ? s.filter((k) => k !== r.id) : [...s, r.id])) },
            { label: "Add to comparison", onClick: () => window.__mdsCompareToggle && window.__mdsCompareToggle(item(r)) },
            { type: "divider" },
            { label: "Set ack budget → 250", onClick: () => ledger.commit({ rowId: r.id, field: "budget", prior: valueOf(r, "budget"), value: "250" }) },
            { label: "Revert this row's confirmed edits…", disabled: !ledger.forRow(r.id).some((e) => e.status === "confirmed"), onClick: () => ledger.revert(ledger.forRow(r.id).filter((e) => e.status === "confirmed").map((e) => e.id), "Reverted from row menu") },
          ] : [{ label: "No row under the pointer", disabled: true }]} />); })()}
      </div>
    );
  }

  function CompareBridge() { const c = DS.SelectionCompareHooks.useSelectionCompare(); React.useEffect(() => { window.__mdsCompareToggle = c.toggle; return () => { delete window.__mdsCompareToggle; }; }, [c.toggle]); return null; }

  return (
    <SelectionCompareProvider max={compareMax} categories={TENORS} format={(v) => `${v.toFixed(0)}%`}>
      <AnnotationProvider objectId="registry:instruments" objectVersion={objectVersion} author="you" onChange={onAnnotationsChange}
        annotations={[{ id: "seed-1", anchor: { kind: "cell", ref: `row:${rows[0].id}/symbol`, label: rows[0].symbol }, text: "Limit raised to 1.2m on 14 Aug under RISK-014 §3. Follows the registry — a re-import will mark this stale.", author: "a.patel", at: Date.now() - 7200000, version: 2, lifecycle: "follow" }]}>
        <OptimisticProvider commit={commit} revert={revert} onSettle={onSettle}>
          <CompareBridge />
          <div style={{ display: "grid", gridTemplateColumns: "minmax(0,1fr) 300px", gap: 10, height: "100%", minHeight: 0 }}>
            <Inner />
            <div style={{ display: "flex", flexDirection: "column", gap: 10, minHeight: 0 }}>
              <div style={{ border: "1px solid var(--border)", background: "var(--bg-light)", display: "flex", flexDirection: "column", minHeight: 0, flex: 1 }}>
                <AnnotationPanel style={{ flex: 1, minHeight: 0 }} title="Annotations · registry" />
              </div>
              <CompareTray emptyLabel="Tick ▢ on two rows, or press c on the active row." style={{ flexWrap: "wrap" }} />
              <div style={{ border: "1px solid var(--border)", background: "var(--bg-light)", padding: "8px 10px", font: "11px var(--font-body)", color: "var(--text-secondary)", lineHeight: 1.5 }}>
                {Eyebrow ? <Eyebrow>Keyboard-only path</Eyebrow> : <b>Keyboard-only path</b>}
                <div style={{ display: "grid", gridTemplateColumns: "auto 1fr", gap: "2px 10px", marginTop: 4, font: "11px var(--font-data)" }}>
                  <b>j / k</b><span>move the active row</span><b>x</b><span>select / deselect it</span><b>c</b><span>add to comparison</span><b>Enter</b><span>on a cell: edit · commit</span><b>Esc</b><span>cancel · clear</span><b>Ctrl K</b><span>palette (find a symbol)</span><b>?</b><span>the full sheet</span>
                </div>
              </div>
            </div>
          </div>
          <CompareSheet footerLabel="1Y" />
        </OptimisticProvider>
      </AnnotationProvider>
    </SelectionCompareProvider>
  );
}
window.InteractionDepthPlane = InteractionDepthPlane;
if (typeof module !== "undefined") module.exports = { InteractionDepthPlane };
