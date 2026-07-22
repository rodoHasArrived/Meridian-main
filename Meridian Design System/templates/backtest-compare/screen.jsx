// Meridian backtest-compare — template screen. Two strategy runs side by side: KPI deltas,
// overlaid equity curves, paired drawdown/returns panes, and the configuration diff.
// Mounted by the DC via <x-import>; reads components from the compiled bundle.

const {
  WorkstationTopbar, NavRail, StatusBar, PanelSurface, Eyebrow, Button, Badge, Select,
  ChartCard, EquityCurve, DrawdownChart, Histogram, DenseDataTable, StatusBanner, DiffView,
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
    { id: "runs", label: "Strategy Runs", icon: "../../assets/icons/strategy-runs.svg", shortcut: "G R" },
  ]},
];

// ── run library (deterministic fixtures) ─────────────────────────────
const RUNS = {
  "1841": {
    id: "#1841", name: "momentum.v4", label: "momentum.v4 \u00b7 #1841",
    gen: { drift: 0.0052, w1: 26, w2: 14, dip: 22, mu: 0.08 },
    kpis: { ret: 24.61, cagr: 11.8, sharpe: 1.42, sortino: 1.98, maxdd: -9.84, win: 61.4, pf: 1.61, trades: 1284, hold: 34, fees: 18.4 },
    cfg: { Lookback: "20d", Universe: "HY corp \u00b7 412 names", "Max position": "2.0%", Rebalance: "Weekly", Stop: "\u22128% trail", "Fee model": "IBKR tiered" },
  },
  "1847": {
    id: "#1847", name: "momentum.v5", label: "momentum.v5 \u00b7 #1847",
    gen: { drift: 0.0061, w1: 18, w2: 10, dip: 14, mu: 0.10 },
    kpis: { ret: 31.08, cagr: 14.5, sharpe: 1.61, sortino: 2.31, maxdd: -7.42, win: 63.9, pf: 1.78, trades: 1412, hold: 28, fees: 19.6 },
    cfg: { Lookback: "34d", Universe: "HY + IG corp \u00b7 655 names", "Max position": "1.5%", Rebalance: "Daily", Stop: "\u22128% trail", "Fee model": "IBKR tiered" },
  },
  "1852": {
    id: "#1852", name: "momentum.v5 + earnings filter", label: "momentum.v5 + earn filter \u00b7 #1852",
    gen: { drift: 0.0058, w1: 20, w2: 9, dip: 12, mu: 0.095 },
    kpis: { ret: 28.91, cagr: 13.6, sharpe: 1.55, sortino: 2.18, maxdd: -6.98, win: 64.8, pf: 1.72, trades: 1105, hold: 31, fees: 16.2 },
    cfg: { Lookback: "34d", Universe: "HY + IG corp \u00b7 655 names", "Max position": "1.5%", Rebalance: "Daily", Stop: "\u22128% trail + earnings blackout", "Fee model": "IBKR tiered" },
  },
};

const N = 44, START = 1000;
const MON = ["Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec"];
const LABELS = Array.from({ length: N }, (_, i) => MON[i % 12]);

function seriesFor(key) {
  const g = RUNS[key].gen;
  const eq = [], dd = [], returns = [];
  let peak = START;
  for (let i = 0; i < N; i++) {
    const v = START * (1 + g.drift * i) + Math.sin(i / 3) * g.w1 + Math.sin(i / 1.6) * g.w2
      - Math.max(0, Math.sin(i / 7)) * g.dip;
    eq.push(v); peak = Math.max(peak, v); dd.push(((v - peak) / peak) * 100);
  }
  for (let i = 0; i < 130; i++) {
    returns.push(Math.sin(i + g.w1) * 0.95 + Math.sin(i * 1.31) * 0.6 + ((i % 9) - 4) * 0.16 + g.mu);
  }
  return { eq, dd, returns };
}

// ── metric metadata: how to format, and which direction wins ─────────
const pct2 = (v) => (v < 0 ? "\u2212" : "+") + Math.abs(v).toFixed(2) + "%";
const METRICS = [
  { key: "ret",     label: "Total return",   better: "higher", fmt: pct2, dfmt: (d) => (d < 0 ? "\u2212" : "+") + Math.abs(d).toFixed(2) + "pp" },
  { key: "cagr",    label: "CAGR",           better: "higher", fmt: (v) => v.toFixed(1) + "%", dfmt: (d) => (d < 0 ? "\u2212" : "+") + Math.abs(d).toFixed(1) + "pp" },
  { key: "sharpe",  label: "Sharpe",         better: "higher", fmt: (v) => v.toFixed(2), dfmt: (d) => (d < 0 ? "\u2212" : "+") + Math.abs(d).toFixed(2) },
  { key: "sortino", label: "Sortino",        better: "higher", fmt: (v) => v.toFixed(2), dfmt: (d) => (d < 0 ? "\u2212" : "+") + Math.abs(d).toFixed(2) },
  { key: "maxdd",   label: "Max drawdown",   better: "higher", fmt: (v) => v.toFixed(2) + "%", dfmt: (d) => (d < 0 ? "\u2212" : "+") + Math.abs(d).toFixed(2) + "pp" },
  { key: "win",     label: "Win rate",       better: "higher", fmt: (v) => v.toFixed(1) + "%", dfmt: (d) => (d < 0 ? "\u2212" : "+") + Math.abs(d).toFixed(1) + "pp" },
  { key: "pf",      label: "Profit factor",  better: "higher", fmt: (v) => v.toFixed(2), dfmt: (d) => (d < 0 ? "\u2212" : "+") + Math.abs(d).toFixed(2) },
  { key: "trades",  label: "Trades",         better: "none",   fmt: (v) => v.toLocaleString("en-US"), dfmt: (d) => (d < 0 ? "\u2212" : "+") + Math.abs(d).toLocaleString("en-US") },
  { key: "hold",    label: "Avg hold",       better: "none",   fmt: (v) => v + "d", dfmt: (d) => (d < 0 ? "\u2212" : "+") + Math.abs(d) + "d" },
  { key: "fees",    label: "Fees & costs",   better: "lower",  fmt: (v) => "$" + v.toFixed(1) + "k", dfmt: (d) => (d < 0 ? "\u2212" : "+") + "$" + Math.abs(d).toFixed(1) + "k" },
];

const RUN_OPTIONS = Object.keys(RUNS).map((k) => ({ value: k, label: RUNS[k].label }));

function BacktestCompareScreen({ environment = "FIXTURE", runA: runAProp = "1841", runB: runBProp = "1847", highlightDeltas = true }) {
  const [a, setA] = useState(String(runAProp));
  const [b, setB] = useState(String(runBProp));
  const A = RUNS[a] || RUNS["1841"], B = RUNS[b] || RUNS["1847"];
  const sA = seriesFor(a in RUNS ? a : "1841"), sB = seriesFor(b in RUNS ? b : "1847");
  const env = String(environment).toUpperCase();
  const mono = { fontFamily: "var(--font-data)", fontVariantNumeric: "tabular-nums" };

  const deltaColor = (m, d) => {
    if (!highlightDeltas || m.better === "none" || d === 0) return "var(--text-muted)";
    const good = m.better === "higher" ? d > 0 : d < 0;
    return good ? "var(--green-dim)" : "var(--red-dim)";
  };

  const kpiRows = METRICS.map((m) => {
    const va = A.kpis[m.key], vb = B.kpis[m.key], d = +(vb - va).toFixed(4);
    return { metric: m.label, a: m.fmt(va), b: m.fmt(vb), d: d === 0 ? "\u00b7" : m.dfmt(d), color: deltaColor(m, d) };
  });

  const diffChanges = Object.keys(A.cfg).map((k) => ({ field: k, before: A.cfg[k], after: B.cfg[k] }));
  const dSharpe = +(B.kpis.sharpe - A.kpis.sharpe).toFixed(2);
  const dDd = +(B.kpis.maxdd - A.kpis.maxdd).toFixed(2);
  const dFees = +(B.kpis.fees - A.kpis.fees).toFixed(1);
  const verdictTone = dSharpe > 0 && dDd > 0 ? "success" : dSharpe > 0 || dDd > 0 ? "info" : "warning";

  const fmtM = (v) => "$" + (v / 1000).toFixed(2) + "M";
  const cell = (txt, color) => <span style={{ ...mono, color: color || "var(--text-primary)" }}>{txt}</span>;

  return (
    <React.Fragment>
      <WorkstationTopbar moduleLabel="Backtest \u00b7 Compare" environment={env} clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />
      <div style={{ display: "flex", flex: 1, minHeight: 0 }}>
        <NavRail activeId="runs" sections={NAV} onSelect={() => {}} />
        <main style={{ flex: 1, minWidth: 0, overflowY: "auto", padding: 16, display: "flex", flexDirection: "column", gap: 12 }}>

          <div style={{ display: "flex", alignItems: "flex-end", gap: 10, flexWrap: "wrap" }}>
            <h1 style={{ font: "600 22px var(--font-display)", margin: 0, color: "var(--text-primary)", whiteSpace: "nowrap" }}>Compare runs</h1>
            <Badge variant={env.toLowerCase()} dot>{env}</Badge>
            <div style={{ flex: 1 }}></div>
            <div style={{ width: 250 }}><Select label="Run A \u00b7 baseline" value={a} onChange={setA} options={RUN_OPTIONS} /></div>
            <Button variant="ghost" size="sm" onClick={() => { setA(b); setB(a); }} style={{ marginBottom: 1 }}>Swap</Button>
            <div style={{ width: 250 }}><Select label="Run B \u00b7 candidate" value={b} onChange={setB} options={RUN_OPTIONS} /></div>
          </div>

          <StatusBanner tone={verdictTone} title={"Candidate " + B.id + " vs baseline " + A.id}
            detail={"Sharpe " + A.kpis.sharpe.toFixed(2) + " \u2192 " + B.kpis.sharpe.toFixed(2)
              + " \u00b7 max DD " + A.kpis.maxdd.toFixed(2) + "% \u2192 " + B.kpis.maxdd.toFixed(2) + "%"
              + " \u00b7 fees " + (dFees < 0 ? "\u2212" : "+") + "$" + Math.abs(dFees).toFixed(1) + "k \u00b7 same period, same seed"} />

          <div style={{ display: "grid", gridTemplateColumns: "minmax(0,1fr) 320px", gap: 12, alignItems: "start" }}>
            <div style={{ display: "flex", flexDirection: "column", gap: 12, minWidth: 0 }}>

              <PanelSurface style={{ padding: 0 }}>
                <div style={{ padding: "10px 14px", borderBottom: "1px solid var(--border)", display: "flex", alignItems: "center", gap: 8 }}>
                  <Eyebrow>KPI deltas</Eyebrow>
                  <div style={{ flex: 1 }}></div>
                  <span style={{ ...mono, fontSize: 11, color: "var(--text-muted)" }}>Δ = B − A · 2024-07-01 → 2026-06-30</span>
                </div>
                <DenseDataTable
                  columns={[
                    { key: "metric", label: "Metric" },
                    { key: "a", label: "A \u00b7 " + A.id, align: "right", render: (r) => cell(r.a, "var(--text-secondary)") },
                    { key: "b", label: "B \u00b7 " + B.id, align: "right", render: (r) => cell(r.b) },
                    { key: "d", label: "\u0394", align: "right", render: (r) => cell(r.d, r.color) },
                  ]}
                  rows={kpiRows} />
              </PanelSurface>

              <ChartCard title="Equity curves" subtitle={A.label + "  vs  " + B.label}
                readout={[
                  { label: "A final", value: fmtM(sA.eq[N - 1]) },
                  { label: "B final", value: fmtM(sB.eq[N - 1]), color: "var(--green-dim)" },
                ]}
                height={280}>
                <div style={{ height: 280 }}>
                  <EquityCurve
                    series={[
                      { label: "A \u00b7 " + A.name, color: "var(--chart-primary)", points: sA.eq },
                      { label: "B \u00b7 " + B.name, color: "var(--chart-equity)", points: sB.eq, area: false },
                    ]}
                    labels={LABELS} valueFmt={fmtM} crosshairIndex={N - 1} />
                </div>
              </ChartCard>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
                <ChartCard title={"Drawdown \u00b7 A " + A.id} readout={[{ label: "Max", value: A.kpis.maxdd.toFixed(2) + "%", color: "var(--red-dim)" }]} height={190}>
                  <DrawdownChart series={sA.dd} labels={LABELS} valueFmt={(v) => v.toFixed(0) + "%"} />
                </ChartCard>
                <ChartCard title={"Drawdown \u00b7 B " + B.id} readout={[{ label: "Max", value: B.kpis.maxdd.toFixed(2) + "%", color: "var(--red-dim)" }]} height={190}>
                  <DrawdownChart series={sB.dd} labels={LABELS} valueFmt={(v) => v.toFixed(0) + "%"} />
                </ChartCard>
                <ChartCard title={"Daily returns \u00b7 A " + A.id} readout={[{ label: "\u03bc", value: "+" + RUNS[a in RUNS ? a : "1841"].gen.mu.toFixed(2) + "%" }]} height={190}>
                  <Histogram values={sA.returns} binCount={22} valueFmt={(v) => v.toFixed(1) + "%"} />
                </ChartCard>
                <ChartCard title={"Daily returns \u00b7 B " + B.id} readout={[{ label: "\u03bc", value: "+" + RUNS[b in RUNS ? b : "1847"].gen.mu.toFixed(2) + "%" }]} height={190}>
                  <Histogram values={sB.returns} binCount={22} valueFmt={(v) => v.toFixed(1) + "%"} />
                </ChartCard>
              </div>
            </div>

            {/* Rail: configuration diff + provenance */}
            <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
              <PanelSurface style={{ padding: 0 }}>
                <div style={{ padding: "10px 14px", borderBottom: "1px solid var(--border)" }}>
                  <Eyebrow>Configuration · A → B</Eyebrow>
                </div>
                <div style={{ padding: 12 }}>
                  <DiffView changes={diffChanges} />
                </div>
              </PanelSurface>
              <PanelSurface style={{ padding: 0 }}>
                <div style={{ padding: "10px 14px", borderBottom: "1px solid var(--border)" }}>
                  <Eyebrow>Provenance</Eyebrow>
                </div>
                <div style={{ padding: "6px 14px 12px", display: "flex", flexDirection: "column" }}>
                  {[
                    ["Engine", "fixture replay \u00b7 seed 8841"],
                    ["Dataset", "corp-bond EOD \u00b7 1,284 sessions"],
                    ["A proof", A.id.slice(1) + "-c41a\u2026 \u00b7 2026-07-02 09:14Z"],
                    ["B proof", B.id.slice(1) + "-9f27\u2026 \u00b7 2026-07-04 16:41Z"],
                  ].map(([l, v]) => (
                    <div key={l} style={{ display: "flex", gap: 10, padding: "5px 0", borderBottom: "1px solid var(--border-divider, var(--border))" }}>
                      <span style={{ flex: "none", width: 64, fontSize: 10, fontWeight: 600, fontVariant: "all-small-caps", letterSpacing: ".04em", color: "var(--text-muted)" }}>{l}</span>
                      <span style={{ ...mono, fontSize: 11, color: "var(--text-secondary)", minWidth: 0 }}>{v}</span>
                    </div>
                  ))}
                  <Button variant="ghost" size="sm" style={{ marginTop: 10 }}>Open run traces</Button>
                </div>
              </PanelSurface>
            </div>
          </div>
        </main>
      </div>
      <StatusBar items={[
        { status: "ok", label: "Engine", value: "fixture replay" },
        { label: "Sessions", value: "1,284" },
        { label: "A", value: A.id },
        { label: "B", value: B.id },
        { status: dSharpe >= 0 ? "ok" : "warn", label: "\u0394 Sharpe", value: (dSharpe < 0 ? "\u2212" : "+") + Math.abs(dSharpe).toFixed(2), push: true },
      ]} />
    </React.Fragment>
  );
}

window.BacktestCompareScreen = BacktestCompareScreen;
if (typeof module !== "undefined") module.exports = { BacktestCompareScreen };
