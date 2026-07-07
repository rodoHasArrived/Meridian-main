// Meridian dashboard-workstation — template screen. Mounted by the DC via <x-import>; reads design-system
// components from the compiled bundle.

const {
  WorkstationTopbar, NavRail, StatusBar, PanelSurface, Eyebrow, Button, Badge,
  MetricCard, DenseDataTable, EntitySummary, StatusBanner
} = window.MeridianDesignSystem_4f61be;
const { useState } = React;

const POSITIONS = [
  { symbol: "AAPL", qty: "400",  avg: "182.4400", last: "201.1200", pnl: "+7,472.00",  pnlPct: "+10.24%", figi: "BBG000B9XRY4", exch: "XNAS", since: "2025-11-04" },
  { symbol: "MSFT", qty: "150",  avg: "411.0200", last: "404.8800", pnl: "-921.00",    pnlPct: "-1.49%",  figi: "BBG000BPH459", exch: "XNAS", since: "2026-01-12" },
  { symbol: "NVDA", qty: "80",   avg: "118.3300", last: "131.0700", pnl: "+1,019.20",  pnlPct: "+10.77%", figi: "BBG000BBJQV0", exch: "XNAS", since: "2026-02-20" },
  { symbol: "SPY",  qty: "-200", avg: "598.1100", last: "601.4400", pnl: "-666.00",    pnlPct: "-0.56%",  figi: "BBG000BDTBL9", exch: "ARCX", since: "2026-05-30" },
  { symbol: "TLT",  qty: "320",  avg: "88.2100",  last: "91.0500",  pnl: "+908.80",    pnlPct: "+3.22%",  figi: "BBG000BJKYW3", exch: "XNAS", since: "2026-03-17" },
];

const EQUITY = [62,60,61,58,55,57,54,50,52,47,44,46,41,43,38,36,39,33,30,32,27,25,28,22,20];
const BENCH  = [62,61,61,60,58,58,57,55,56,53,52,52,50,51,49,48,49,46,45,46,43,42,43,41,40];

function pnlCell(v) {
  return <span style={{ color: v.startsWith("-") ? "var(--red-dim)" : "var(--green-dim)" }}>{v}</span>;
}

function EquityChart() {
  const w = 760, h = 180, gut = 60, plotW = w - gut, step = plotW / (EQUITY.length - 1);
  const sy = (v) => v * 2.4;
  const pts = (arr) => arr.map((v, i) => `${(i * step).toFixed(1)},${sy(v).toFixed(1)}`).join(" ");
  const lastX = (EQUITY.length - 1) * step, lastY = sy(EQUITY[EQUITY.length - 1]);
  const baseY = sy(EQUITY[0]);                       // opening equity = cost basis
  const areaPts = `0,${h} ${pts(EQUITY)} ${plotW},${h}`;
  return (
    <svg viewBox={`0 0 ${w} ${h}`} style={{ width: "100%", height: "auto", display: "block" }} aria-label="Equity curve vs benchmark">
      <g stroke="var(--chart-grid)" strokeWidth="1">
        {[36, 78, 120, 162].map((y) => <line key={y} x1="0" y1={y} x2={plotW} y2={y} />)}
      </g>
      {/* cost-basis reference */}
      <line x1="0" x2={plotW} y1={baseY} y2={baseY} stroke="var(--chart-axis)" strokeWidth="1" strokeDasharray="3 3" opacity="0.55" />
      <text x="3" y={baseY - 5} fill="var(--chart-axis)" style={{ font: "500 10px var(--font-data)" }}>cost basis</text>
      {/* flat alpha wash under the equity line */}
      <polygon points={areaPts} fill="var(--chart-equity)" opacity="0.10" />
      <polyline fill="none" stroke="var(--chart-secondary)" strokeWidth="1.5" strokeDasharray="5 4" points={pts(BENCH)} />
      <polyline fill="none" stroke="var(--chart-equity)" strokeWidth="2" points={pts(EQUITY)} />
      {/* last-value marker + right-axis chip */}
      <line x1="0" x2={lastX} y1={lastY} y2={lastY} stroke="var(--chart-equity)" strokeWidth="0.8" strokeDasharray="2 3" opacity="0.5" />
      <circle cx={lastX} cy={lastY} r="3.5" fill="var(--chart-equity)" />
      <rect x={lastX + 6} y={lastY - 9} width="52" height="18" fill="var(--chart-equity)" />
      <text x={lastX + 32} y={lastY + 4} textAnchor="middle" fill="#fff" style={{ font: "600 11px var(--font-data)", fontVariantNumeric: "slashed-zero tabular-nums" }}>+23.7%</text>
    </svg>
  );
}

function DashboardWorkstationScreen() {
  const [selected, setSelected] = useState(0);
  const [env, setEnv] = useState("PAPER");
  const [sortKey, setSortKey] = useState("pnl");
  const [sortDir, setSortDir] = useState("desc");
  const onSort = (key) => {
    if (key === sortKey) setSortDir((d) => (d === "asc" ? "desc" : "asc"));
    else { setSortKey(key); setSortDir("desc"); }
  };
  const num = (s) => parseFloat(String(s).replace(/[$,%+]/g, ""));
  const sorted = [...POSITIONS].sort((a, b) => {
    const av = num(a[sortKey]), bv = num(b[sortKey]);
    const cmp = isNaN(av) || isNaN(bv) ? String(a[sortKey]).localeCompare(String(b[sortKey])) : av - bv;
    return sortDir === "asc" ? cmp : -cmp;
  });
  const row = sorted[selected] || sorted[0];
  return (
    <React.Fragment>
      <style>{`
        .kpi-row{display:grid;grid-template-columns:repeat(auto-fit, minmax(190px, 1fr));gap:10px;}
        .kpi-row .mds-metric{padding:14px;}
        .kpi-row .mds-metric__value{font-size:20px;}
        .kpi-row .mds-metric--hero{grid-column:span 2;}
        .kpi-row .mds-metric--hero .mds-metric__value{font-size:26px;}
        .kpi-row .mds-metric--hero .mds-metric__label{font-size:11px;}
      `}</style>
      <WorkstationTopbar moduleLabel="Dashboard" environment={env} clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />
      <div style={{ display: "flex", flex: 1, minHeight: 0 }}>
        <NavRail
          activeId="dashboard"
          onSelect={() => {}}
          sections={[
            { label: "Operate", items: [
              { id: "dashboard", label: "Dashboard", icon: "../../assets/icons/dashboard.svg", shortcut: "G D" },
              { id: "trading", label: "Trading", icon: "../../assets/icons/trading.svg" },
              { id: "watchlist", label: "Watchlist", icon: "../../assets/icons/watchlist.svg" },
              { id: "order-book", label: "Order Book", icon: "../../assets/icons/order-book.svg" },
            ]},
            { label: "Data", items: [
              { id: "security-master", label: "Security Master", icon: "../../assets/icons/security-master.svg" },
              { id: "data-browser", label: "Data Browser", icon: "../../assets/icons/data-browser.svg" },
              { id: "data-quality", label: "Data Quality", icon: "../../assets/icons/data-quality.svg" },
            ]},
            { label: "Research", items: [
              { id: "backtest", label: "Backtest", icon: "../../assets/icons/backtest.svg" },
              { id: "charting", label: "Charting", icon: "../../assets/icons/charting.svg" },
              { id: "strategy-runs", label: "Strategy Runs", icon: "../../assets/icons/strategy-runs.svg" },
            ]},
          ]}
        />
        <main style={{ flex: 1, minWidth: 0, overflowY: "auto", padding: 16, display: "flex", flexDirection: "column", gap: 12 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <h1 style={{ font: "600 22px var(--font-display)", margin: 0, color: "var(--text-primary)" }}>Portfolio overview</h1>
            <div style={{ flex: 1 }}></div>
            <Button variant="ghost" size="sm" onClick={() => setEnv(env === "PAPER" ? "LIVE" : "PAPER")}>
              Switch to {env === "PAPER" ? "live" : "paper"}
            </Button>
            <Button variant="primary" size="sm">New order</Button>
          </div>

          <div className="kpi-row">
            <MetricCard hero tone="info"
              label="Net liquidation" value="$1,284,002.18" delta="+1.84%" context="today"
              sparkline={[...EQUITY].reverse()} />
            <MetricCard tone="danger" label="Day P&L" value="-$4,118.22" delta="-0.32%"
              sparkline={[9, 8, 8, 7, 7, 6, 6, 5, 4]} />
            <MetricCard tone="neutral" label="Buying power" value="$2,402,114.00" context="Reg-T margin" />
            <MetricCard tone="neutral" label="Open positions" value="5" context="1 short" />
            <MetricCard tone="success" label="Data freshness" value="00:00:04" context="all healthy" />
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "2fr 1fr", gap: 12, alignItems: "stretch" }}>
            <PanelSurface strong style={{ display: "flex", flexDirection: "column", minHeight: 0 }}>
              <div style={{ display: "flex", alignItems: "baseline", gap: 10, padding: "8px 12px", background: "var(--bg-medium)", borderBottom: "1px solid var(--border-strong)" }}>
                <Eyebrow>Equity curve · YTD</Eyebrow>
                <span style={{ font: "500 11px var(--font-data)", color: "var(--chart-secondary)" }}>— — benchmark SPX</span>
              </div>
              <div style={{ padding: 16 }}><EquityChart /></div>
            </PanelSurface>
            <PanelSurface raised strong style={{ display: "flex", flexDirection: "column", borderLeft: "3px solid var(--accent)" }}>
              <div style={{ display: "flex", alignItems: "center", gap: 8, padding: "8px 12px", background: "var(--bg-medium)", borderBottom: "1px solid var(--border-strong)" }}>
                <Eyebrow>Selected position</Eyebrow>
                <span style={{ font: "600 11px var(--font-data)", color: "var(--accent)", marginLeft: "auto" }}>{row.symbol}</span>
              </div>
              <div style={{ padding: 16, display: "flex", flexDirection: "column", gap: 10, flex: 1 }}>
                <EntitySummary columns={2} items={[
                  { label: "Symbol", value: row.symbol },
                  { label: "FIGI", value: row.figi },
                  { label: "Exchange", value: row.exch },
                  { label: "Held since", value: row.since },
                  { label: "Unrlzd P&L", value: row.pnl, color: row.pnl.startsWith("-") ? "var(--red-dim)" : "var(--green-dim)" },
                  { label: "Return", value: row.pnlPct, color: row.pnlPct.startsWith("-") ? "var(--red-dim)" : "var(--green-dim)" },
                ]} />
                <div style={{ display: "flex", gap: 8, marginTop: "auto" }}>
                  <Button variant="ghost" size="sm">Open chart</Button>
                  <Button variant="danger" size="sm">Close position</Button>
                </div>
              </div>
            </PanelSurface>
          </div>

          <DenseDataTable
            selectedIndex={selected}
            sortKey={sortKey} sortDir={sortDir} onSort={onSort}
            onRowClick={(_, i) => setSelected(i)}
            columns={[
              { key: "symbol", label: "Symbol" },
              { key: "qty", label: "Qty", align: "right" },
              { key: "avg", label: "Avg cost", align: "right" },
              { key: "last", label: "Last", align: "right" },
              { key: "pnl", label: "Unrlzd P&L", align: "right", render: (r) => pnlCell(r.pnl) },
              { key: "pnlPct", label: "Return", align: "right", render: (r) => pnlCell(r.pnlPct) },
            ]}
            rows={sorted}
          />

          <StatusBanner tone="success" title="All data providers healthy" detail="Polygon 4s · IBKR 2s · last gap scan 13:50:00Z — 0 gaps" />
        </main>
      </div>
      <StatusBar items={[
        { status: "ok", label: "Connected", value: "IBKR · Polygon · Databento" },
        { label: "Sync", value: "00:00:04 ago" },
        { label: "Positions", value: "5" },
        { label: "Session", value: "RTH · 14:32:08 UTC" },
        { status: "ok", label: "Latency", value: "12ms", push: true },
      ]} />
    </React.Fragment>
  );
}

window.DashboardWorkstationScreen = DashboardWorkstationScreen;
if (typeof module !== "undefined") module.exports = { DashboardWorkstationScreen };
