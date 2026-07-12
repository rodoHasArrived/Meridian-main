// Meridian backtest-builder — template screen. Mounted by the DC via <x-import>; reads design-system
// components from the compiled bundle.

const {
  WorkstationTopbar, NavRail, StatusBar, PanelSurface, Eyebrow, Button, Badge,
  Select, Input, MetricCard, ChartCard, EquityCurve, DrawdownChart, Histogram,
  DenseDataTable, StatusBanner, SegmentedControl
} = window.MeridianDesignSystem_4f61be;
const { useState } = React;

const NAV = [
  { label: "Strategy", items: [
    { id: "builder", label: "Strategy Builder", icon: "../../assets/icons/strategy-builder.svg", shortcut: "G S" },
    { id: "fields", label: "Field & Formula", icon: "../../assets/icons/formula.svg" },
    { id: "backtest", label: "Backtest", icon: "../../assets/icons/backtest.svg", shortcut: "G B" },
  ]},
  { label: "Data & Review", items: [
    { id: "catalog", label: "AMX Catalog", icon: "../../assets/icons/data-sources.svg" },
    { id: "governance", label: "Governance", icon: "../../assets/icons/governance.svg" },
    { id: "runs", label: "Strategy Runs", icon: "../../assets/icons/strategy-runs.svg" },
  ]},
];

const ROUTES = {
  builder: "../strategy-builder/index.html", fields: "../field-formula/index.html", backtest: "../backtest-builder/index.html",
  catalog: "../amx-governance/index.html", governance: "../amx-governance/index.html", runs: "../strategy-runs/index.html",
};

// Deterministic series (thousands of dollars; start 1,000)
const N = 44, START = 1000;
const eq = [], bench = [], dd = [], labels = [], returns = [];
const MON = ["Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec"];
let peak = START;
for (let i = 0; i < N; i++) {
  const base = START * (1 + 0.0052 * i);
  const wob = Math.sin(i / 3) * 26 + Math.sin(i / 1.6) * 14 - Math.max(0, Math.sin(i / 7)) * 22;
  const v = base + wob;
  eq.push(v); peak = Math.max(peak, v); dd.push(((v - peak) / peak) * 100);
  bench.push(START * (1 + 0.0036 * i) + Math.sin(i / 4) * 10);
  labels.push(MON[i % 12]);
}
for (let i = 0; i < 130; i++) {
  returns.push(Math.sin(i) * 0.95 + Math.sin(i * 1.31) * 0.6 + ((i % 9) - 4) * 0.16 + 0.08);
}
const fmtM = (v) => "$" + (v / 1000).toFixed(2) + "M";
const totalReturn = ((eq[N - 1] - START) / START) * 100;
const maxDD = Math.min(...dd);

const TRADES = [
  { date: "2026-01-12", sym: "912828YK0", side: "BUY",  qty: "1,200", px: "98.7500", pnl: "+4,180.00" },
  { date: "2026-02-03", sym: "037833DK6", side: "BUY",  qty: "800",   px: "91.2200", pnl: "+2,944.00" },
  { date: "2026-03-17", sym: "594918BR8", side: "SELL", qty: "600",   px: "78.1100", pnl: "-1,002.00" },
  { date: "2026-04-08", sym: "69331CAE6", side: "BUY",  qty: "1,500", px: "101.880", pnl: "+6,330.00" },
  { date: "2026-05-21", sym: "345370CR9", side: "SELL", qty: "900",   px: "105.440", pnl: "+1,508.00" },
  { date: "2026-06-14", sym: "38141GYL9", side: "BUY",  qty: "1,100", px: "96.5500", pnl: "-744.00" },
];
const pnl = (v) => <span style={{ color: v.startsWith("-") ? "var(--red-dim)" : "var(--green-dim)" }}>{v}</span>;
const sideBadge = (s) => <Badge variant={s === "BUY" ? "success" : "danger"}>{s}</Badge>;

function BacktestBuilderScreen() {
  const [ds, setDs] = useState("hy-corp");
  const [tf, setTf] = useState("all");

  return (
    <React.Fragment>
      <WorkstationTopbar moduleLabel="Backtest" environment="FIXTURE" clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />
      <div style={{ display: "flex", flex: 1, minHeight: 0 }}>
        <NavRail activeId="backtest" sections={NAV} onSelect={(id) => { const r = ROUTES[id]; if (r) window.location.href = r; }} />
        <main style={{ flex: 1, minWidth: 0, overflowY: "auto", padding: 16, display: "flex", flexDirection: "column", gap: 12 }}>

          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <h1 style={{ font: "600 22px var(--font-display)", margin: 0, color: "var(--text-primary)", whiteSpace: "nowrap" }}>Backtest — Bond Carry &amp; Roll</h1>
            <Badge variant="fixture" dot>FIXTURE</Badge>
            <div style={{ flex: 1 }}></div>
            <Button variant="ghost" size="sm">Export run</Button>
            <Button variant="primary" size="sm">Run backtest</Button>
          </div>

          {/* Config */}
          <PanelSurface flat style={{ padding: 12, display: "grid", gridTemplateColumns: "1.4fr 1fr 1fr 1fr", gap: 12, alignItems: "end" }}>
            <Select label="Dataset" value={ds} onChange={setDs} options={[
              { value: "hy-corp", label: "HY Corp Bonds — 1,284 sessions" },
              { value: "ig-corp", label: "IG Corp Bonds — 1,284 sessions" },
              { value: "util", label: "Utility Sector — 988 sessions" },
            ]} />
            <Input label="Start capital" defaultValue="$1,000,000" />
            <Input label="From" defaultValue="2024-07-01" />
            <Input label="To" defaultValue="2026-06-30" />
          </PanelSurface>

          {/* KPIs */}
          <div style={{ display: "grid", gridTemplateColumns: "repeat(6, 1fr)", gap: 10 }}>
            <MetricCard label="Total return" value={"+" + totalReturn.toFixed(2) + "%"} delta="vs +16.4% SPX" tone="success" />
            <MetricCard label="CAGR" value="11.8%" delta="2-yr annualized" tone="neutral" />
            <MetricCard label="Max drawdown" value={maxDD.toFixed(2) + "%"} delta="−$48,200 trough" tone="danger" />
            <MetricCard label="Sharpe" value="1.42" delta="rf 4.30%" tone="info" />
            <MetricCard label="Win rate" value="61.4%" delta="1,284 trades" tone="neutral" />
            <MetricCard label="Avg hold" value="34d" delta="turnover 2.1×" tone="neutral" />
          </div>

          {/* Equity curve */}
          <ChartCard title="Equity curve" subtitle="Strategy vs SPX benchmark"
            style={{ flexShrink: 0 }}
            readout={[{ label: "Final", value: fmtM(eq[N - 1]) }, { label: "Return", value: "+" + totalReturn.toFixed(1) + "%", color: "var(--green-dim)" }]}
            actions={<SegmentedControl size="sm" value={tf} onChange={setTf} options={[{ value: "1y", label: "1Y" }, { value: "2y", label: "2Y" }, { value: "all", label: "All" }]} />}
            height={300}>
            <div style={{ height: 300 }}>
            <EquityCurve
              series={[
                { label: "Bond Carry & Roll", color: "var(--chart-equity)", points: eq },
                { label: "SPX", color: "var(--chart-secondary)", points: bench, dashed: true, area: false },
              ]}
              labels={labels} valueFmt={fmtM} crosshairIndex={N - 1} />
            </div>
          </ChartCard>

          {/* Drawdown + returns */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
            <ChartCard title="Drawdown" subtitle="Peak-to-trough decline"
              readout={[{ label: "Max", value: maxDD.toFixed(2) + "%", color: "var(--red-dim)" }]} height={240}>
              <DrawdownChart series={dd} labels={labels} threshold={-6} valueFmt={(v) => v.toFixed(0) + "%"} />
            </ChartCard>
            <ChartCard title="Daily returns" subtitle="Distribution · 130 sessions"
              readout={[{ label: "μ", value: "+0.09%" }, { label: "σ", value: "0.71%" }]} height={240}>
              <Histogram values={returns} binCount={22} valueFmt={(v) => v.toFixed(1) + "%"} />
            </ChartCard>
          </div>

          {/* Trades */}
          <PanelSurface style={{ padding: 0 }}>
            <div style={{ padding: "10px 14px", borderBottom: "1px solid var(--border)", display: "flex", alignItems: "center", gap: 8 }}>
              <Eyebrow>Trade blotter</Eyebrow><div style={{ flex: 1 }}></div><Badge variant="neutral">6 of 1,284</Badge>
            </div>
            <DenseDataTable
              columns={[
                { key: "date", label: "Date" },
                { key: "sym", label: "CUSIP" },
                { key: "side", label: "Side", render: (r) => sideBadge(r.side) },
                { key: "qty", label: "Qty", align: "right" },
                { key: "px", label: "Fill", align: "right" },
                { key: "pnl", label: "Realized P&L", align: "right", render: (r) => pnl(r.pnl) },
              ]}
              rows={TRADES} />
          </PanelSurface>

          <StatusBanner tone="success" title="Backtest complete" detail="HY Corp · 1,284 sessions · 1,284 trades · 0 data gaps · ran in 1.84s · proof recorded 14:12:04Z" />
        </main>
      </div>
      <StatusBar items={[
        { status: "ok", label: "Engine", value: "fixture replay" },
        { label: "Sessions", value: "1,284" },
        { label: "Trades", value: "1,284" },
        { status: "ok", label: "Runtime", value: "1.84s" },
        { status: "ok", label: "Proof", value: "current", push: true },
      ]} />
    </React.Fragment>
  );
}

window.BacktestBuilderScreen = BacktestBuilderScreen;
if (typeof module !== "undefined") module.exports = { BacktestBuilderScreen };
