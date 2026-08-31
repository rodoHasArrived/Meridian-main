// Meridian basket-builder — template screen. Mounted by the DC via <x-import>; reads design-system
// components from the compiled bundle.

const {
  WorkstationTopbar, NavRail, StatusBar, PanelSurface, Eyebrow, Button, Badge, SeverityBadge,
  DenseDataTable, KeyValueGrid, Tabs, TabPanel, Input, Select, SegmentedControl, Callout,
  Sparkline, CorrelationHeatmap, BulkActionBar, EmptyState,
} = window.MeridianDesignSystem_4f61be;
const { useState, useMemo } = React;

const BASKETS = window.BASKETS;
const summarize = window.summarizeBasketRows;
const fmtUSD = (v) => "$" + v.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

function directionBadge(d) {
  return <Badge variant={d === "Buy" ? "success" : "danger"} dot>{d}</Badge>;
}

function SummaryTable({ rows }) {
  const cols = [
    { key: "row", label: "" },
    { key: "count", label: "Bonds", align: "right" },
    { key: "mv", label: "Market Value", align: "right", render: (r) => fmtUSD(r.mv) },
    { key: "dv01", label: "DV01", align: "right", render: (r) => "$" + Math.round(r.dv01).toLocaleString("en-US") },
    { key: "liquidityScore", label: "Liquidity Score", align: "right", render: (r) => r.liquidityScore.toFixed(2) },
    { key: "illiquidPct", label: "Illiquid", align: "right", render: (r) => r.illiquidPct.toFixed(2) + "%" },
    { key: "estCostBps", label: "Est. Cost (bps)", align: "right", render: (r) => r.estCostBps.toFixed(2) },
    { key: "modDuration", label: "Mod Duration", align: "right", render: (r) => r.modDuration.toFixed(2) },
    { key: "ytw", label: "Yield", align: "right", render: (r) => r.ytw.toFixed(2) + "%" },
    { key: "rating", label: "Rating", align: "right" },
  ];
  return (
    <PanelSurface flat style={{ overflow: "hidden" }}>
      <DenseDataTable columns={cols} rows={rows} />
    </PanelSurface>
  );
}

function ConstituentsTab({ basket, rows, setRows }) {
  const [selected, setSelected] = useState([]);
  const [groupBy, setGroupBy] = useState("none");
  const [addCode, setAddCode] = useState("");

  const totalMv = rows.reduce((s, r) => s + r.mv, 0);
  const trade = summarize(rows);
  const buys = summarize(rows.filter((r) => r.direction === "Buy"));
  const sells = summarize(rows.filter((r) => r.direction === "Sell"));

  const flipSelected = () => {
    setRows(rows.map((r, i) => selected.includes(i) ? { ...r, direction: r.direction === "Buy" ? "Sell" : "Buy" } : r));
    setSelected([]);
  };
  const removeSelected = () => {
    setRows(rows.filter((_, i) => !selected.includes(i)));
    setSelected([]);
  };

  const grouped = useMemo(() => {
    if (groupBy === "none") return [{ key: null, rows }];
    const map = new Map();
    for (const r of rows) {
      const k = r[groupBy];
      if (!map.has(k)) map.set(k, []);
      map.get(k).push(r);
    }
    return [...map.entries()].map(([key, rs]) => ({ key, rows: rs }));
  }, [rows, groupBy]);

  const cols = [
    { key: "name", label: "Name", align: "left", render: (r) => (
      <div>
        <div style={{ fontWeight: 600, color: "var(--text-primary)", fontFamily: "var(--font-body)" }}>{r.name}</div>
        <div style={{ fontSize: 11, color: "var(--text-muted)" }}>{r.shortCode}</div>
      </div>
    ) },
    { key: "isin", label: "ISIN" },
    { key: "cusip", label: "CUSIP" },
    { key: "direction", label: "Direction", render: (r) => directionBadge(r.direction) },
    { key: "faceValue", label: "Face Value", align: "right", render: (r) => r.faceValue.toLocaleString("en-US") },
    { key: "mv", label: "Market Value", align: "right", render: (r) => fmtUSD(r.mv) },
    { key: "weight", label: "Weight (MV%)", align: "right", render: (r) => ((r.mv / totalMv) * 100).toFixed(2) + "%" },
    { key: "accrued", label: "Accrued Int.", align: "right", render: (r) => r.accrued.toFixed(2) },
    { key: "benchmark", label: "Benchmark" },
    { key: "indQuantity", label: "Ind. Quantity", align: "right", render: (r) => r.indQuantity.toLocaleString("en-US") },
  ];

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16, padding: "18px 2px" }}>
      <div style={{ display: "flex", alignItems: "center", gap: 10, flexWrap: "wrap" }}>
        <Button variant="ghost" size="sm">Upload assets</Button>
        <Button variant="ghost" size="sm">Screen</Button>
        <div style={{ width: 220 }}>
          <Input placeholder="Add by CUSIP or ticker…" value={addCode} onChange={(e) => setAddCode(e.target.value)} />
        </div>
        <div style={{ flex: 1 }}></div>
        <div style={{ width: 170 }}>
          <Select value={groupBy} onChange={setGroupBy}
            options={[{ value: "none", label: "Group by: None" }, { value: "sector", label: "Group by: Sector" }, { value: "rating", label: "Group by: Rating" }]} />
        </div>
        <Button variant="ghost" size="sm">Export</Button>
      </div>

      <div>
        <Eyebrow style={{ marginBottom: 8 }}>Summary</Eyebrow>
        <SummaryTable rows={[
          { row: "Trade", ...trade },
          { row: "Buys", ...buys },
          { row: "Sells", ...sells },
        ]} />
      </div>

      <div>
        <div style={{ display: "flex", alignItems: "baseline", gap: 10, marginBottom: 8 }}>
          <Eyebrow>Constituents</Eyebrow>
          <span style={{ fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-muted)" }}>{rows.length} lines</span>
        </div>
        {rows.length === 0 ? (
          <PanelSurface flat>
            <EmptyState icon="table" title="No constituents" detail="Upload assets or add a CUSIP to build this basket." />
          </PanelSurface>
        ) : (
          <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
            {grouped.map((g, gi) => (
              <div key={gi}>
                {g.key != null && (
                  <div style={{ fontFamily: "var(--font-body)", fontSize: 10, fontWeight: 700, fontVariant: "all-small-caps", letterSpacing: ".03em", color: "var(--text-muted)", marginBottom: 6 }}>{g.key}</div>
                )}
                <PanelSurface flat style={{ overflow: "hidden" }}>
                  <DenseDataTable
                    columns={cols}
                    rows={g.rows}
                    selectable={groupBy === "none"}
                    selectedRows={groupBy === "none" ? selected : []}
                    onSelectRow={(_, i, on) => setSelected((s) => on ? [...s, i] : s.filter((x) => x !== i))}
                    onSelectAll={(on) => setSelected(on ? rows.map((_, i) => i) : [])}
                  />
                </PanelSurface>
              </div>
            ))}
          </div>
        )}
      </div>

      <BulkActionBar
        selectedCount={selected.length}
        onAction={(id) => id === "flip" ? flipSelected() : id === "remove" ? removeSelected() : null}
        actions={[{ id: "flip", label: "Flip direction" }, { id: "remove", label: "Remove", danger: true }]}
      />
    </div>
  );
}

function AnalyticsTab({ basket, rows }) {
  const trade = summarize(rows);
  const top = [...rows].sort((a, b) => b.mv - a.mv).slice(0, 6);
  const corrLabels = top.map((r) => r.shortCode);
  const corr = corrLabels.map((_, i) => corrLabels.map((__, j) => {
    if (i === j) return 1;
    const seed = Math.sin(i * 12.9898 + j * 78.233) * 43758.5453;
    return +(0.35 + 0.55 * Math.abs(seed - Math.floor(seed))).toFixed(2);
  }));

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 18, padding: "18px 2px" }}>
      <KeyValueGrid columns={4} items={[
        { label: "Market Value", value: fmtUSD(trade.mv) },
        { label: "DV01", value: "$" + Math.round(trade.dv01).toLocaleString("en-US") },
        { label: "Mod Duration", value: trade.modDuration.toFixed(2) },
        { label: "Yield to Worst", value: trade.ytw.toFixed(2) + "%" },
        { label: "Liquidity Score", value: trade.liquidityScore.toFixed(2) },
        { label: "Est. Transaction Cost", value: trade.estCostBps.toFixed(2) + " bps" },
        { label: "Weighted Rating", value: trade.rating },
        { label: "Illiquid Share", value: trade.illiquidPct.toFixed(2) + "%" },
      ]} />
      <div style={{ height: 1, background: "var(--border)" }}></div>
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 20 }}>
        <div>
          <Eyebrow style={{ marginBottom: 10 }}>Basket NAV · 30 sessions</Eyebrow>
          <div style={{ height: 90 }}>
            <Sparkline points={basket.navSeries} width={420} height={90} variant="area" color="var(--chart-equity)" />
          </div>
        </div>
        <div>
          <Eyebrow style={{ marginBottom: 10 }}>Top 6 holdings · correlation</Eyebrow>
          <CorrelationHeatmap labels={corrLabels} matrix={corr} cellSize={40} headerSize={64} />
        </div>
      </div>
    </div>
  );
}

function RiskTab({ rows }) {
  const totalMv = rows.reduce((s, r) => s + r.mv, 0);
  const bySector = useMemo(() => {
    const map = new Map();
    for (const r of rows) {
      if (!map.has(r.sector)) map.set(r.sector, { sector: r.sector, count: 0, mv: 0, dv01: 0 });
      const e = map.get(r.sector);
      e.count += 1; e.mv += r.mv; e.dv01 += r.mv * r.modDuration / 10000;
    }
    return [...map.values()].sort((a, b) => b.mv - a.mv);
  }, [rows]);
  const concentrated = bySector.filter((s) => s.mv / totalMv > 0.4);

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16, padding: "18px 2px" }}>
      {concentrated.length > 0 && (
        <Callout tone="warning" title="Sector concentration">
          {concentrated.map((s) => s.sector).join(", ")} exceeds the 40% single-sector policy limit.
        </Callout>
      )}
      <PanelSurface flat style={{ overflow: "hidden" }}>
        <DenseDataTable
          columns={[
            { key: "sector", label: "Sector", align: "left" },
            { key: "count", label: "Bonds", align: "right" },
            { key: "mv", label: "Market Value", align: "right", render: (r) => fmtUSD(r.mv) },
            { key: "weight", label: "Weight", align: "right", render: (r) => ((r.mv / totalMv) * 100).toFixed(2) + "%" },
            { key: "dv01", label: "DV01 Contribution", align: "right", render: (r) => "$" + Math.round(r.dv01).toLocaleString("en-US") },
          ]}
          rows={bySector}
        />
      </PanelSurface>
    </div>
  );
}

function BasketsSidebar({ baskets, activeId, onSelect, filter, setFilter, query, setQuery }) {
  const counts = { All: baskets.length, Credit: baskets.filter(b => b.category === "Credit").length, Rates: baskets.filter(b => b.category === "Rates").length };
  const visible = baskets.filter((b) =>
    (filter === "All" || b.category === filter) &&
    b.name.toLowerCase().includes(query.toLowerCase()));

  return (
    <aside style={{ width: 260, flexShrink: 0, borderRight: "1px solid var(--border)", background: "var(--bg-medium)", overflowY: "auto", padding: 14, display: "flex", flexDirection: "column", gap: 12 }}>
      <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
        <Eyebrow>Baskets</Eyebrow>
        <div style={{ flex: 1 }}></div>
        <Button variant="link">+ New</Button>
      </div>
      <Input placeholder="Search baskets…" value={query} onChange={(e) => setQuery(e.target.value)} />
      <SegmentedControl fullWidth size="sm"
        options={["All", "Credit", "Rates"].map((k) => ({ value: k, label: k, count: counts[k] }))}
        value={filter} onChange={setFilter} />
      <div style={{ height: 1, background: "var(--border)" }}></div>
      <div style={{ display: "flex", flexDirection: "column", gap: 2 }}>
        {visible.map((b) => (
          <button key={b.id} onClick={() => onSelect(b.id)} style={{
            display: "flex", flexDirection: "column", gap: 3, textAlign: "left",
            padding: "8px 10px", border: "none", borderLeft: "3px solid transparent",
            background: b.id === activeId ? "var(--bg-active)" : "transparent",
            borderLeftColor: b.id === activeId ? "var(--accent)" : "transparent",
            cursor: "pointer", fontFamily: "var(--font-body)",
          }}>
            <span style={{ fontSize: 13, fontWeight: b.id === activeId ? 600 : 500, color: "var(--text-primary)" }}>{b.name}</span>
            <span style={{ fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-muted)" }}>{b.constituents.length} lines · {b.category}</span>
          </button>
        ))}
      </div>
    </aside>
  );
}

function BasketBuilderScreen() {
  const [activeId, setActiveId] = useState(BASKETS[0].id);
  const [filter, setFilter] = useState("All");
  const [query, setQuery] = useState("");
  const [rowsById, setRowsById] = useState(() => Object.fromEntries(BASKETS.map((b) => [b.id, b.constituents])));

  const basket = BASKETS.find((b) => b.id === activeId);
  const rows = rowsById[activeId];
  const setRows = (next) => setRowsById((prev) => ({ ...prev, [activeId]: next }));

  return (
    <React.Fragment>
      <WorkstationTopbar moduleLabel="Basket Builder" environment="PAPER" clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />
      <div style={{ display: "flex", flex: 1, minHeight: 0 }} data-screen-label="Basket Builder">
        <NavRail
          activeId="basket-builder"
          sections={[
            { label: "Trading", items: [
              { id: "dashboard", label: "Dashboard", icon: "../../assets/icons/dashboard.svg" },
              { id: "basket-builder", label: "Basket Builder", icon: "../../assets/icons/aggregate-portfolio.svg" },
            ]},
            { label: "Data", items: [
              { id: "security-master", label: "Security Master", icon: "../../assets/icons/security-master.svg" },
            ]},
          ]}
          onSelect={() => {}}
        />
        <BasketsSidebar baskets={BASKETS} activeId={activeId} onSelect={setActiveId}
          filter={filter} setFilter={setFilter} query={query} setQuery={setQuery} />
        <main style={{ flex: 1, minWidth: 0, overflowY: "auto", padding: 20 }}>
          <div style={{ display: "flex", flexDirection: "column", gap: 14, maxWidth: 1240 }}>
            <div style={{ display: "flex", alignItems: "center", gap: 12, flexWrap: "wrap" }}>
              <h1 style={{ font: "600 20px var(--font-display)", margin: 0, color: "var(--text-primary)" }}>{basket.name}</h1>
              <SeverityBadge status={basket.status} />
              <div style={{ flex: 1 }}></div>
              <div style={{ width: 100 }}>
                <Select value={basket.currency} onChange={() => {}} options={["USD", "EUR", "GBP"]} />
              </div>
              <span style={{ fontFamily: "var(--font-data)", fontSize: 12, color: "var(--text-muted)" }}>Pricing date {basket.asOf}</span>
            </div>

            <PanelSurface raised style={{ padding: "0 18px 18px" }}>
              <Tabs tabs={[{ label: "Constituents" }, { label: "Analytics" }, { label: "Risk" }]}>
                <TabPanel><ConstituentsTab basket={basket} rows={rows} setRows={setRows} /></TabPanel>
                <TabPanel><AnalyticsTab basket={basket} rows={rows} /></TabPanel>
                <TabPanel><RiskTab rows={rows} /></TabPanel>
              </Tabs>
            </PanelSurface>
          </div>
        </main>
      </div>
      <StatusBar items={[
        { status: "ok", label: "Pricing", value: "Bloomberg BVAL · 07:00 UTC" },
        { label: "Basket", value: basket.name },
        { label: "Lines", value: String(rows.length) },
        { status: "ok", label: "Draft", value: "saved 09:50Z", push: true },
      ]} />
    </React.Fragment>
  );
}

window.BasketBuilderScreen = BasketBuilderScreen;
if (typeof module !== "undefined") module.exports = { BasketBuilderScreen };
