// Meridian charting-workstation — template screen. Mounted by the DC via <x-import>; reads design-system
// components from the compiled bundle.

const {
  WorkstationTopbar, StatusBar, PanelSurface, Eyebrow, Button, Badge, KeyValueGrid, CandleChart, ChartCard,
  ScatterChart, SegmentedControl, Input, Kbd
} = window.MeridianDesignSystem_4f61be;
const { useState, useMemo } = React;

// deterministic pseudo scatter sample — spread (bps) vs 3M implied vol (%)
function buildScatterSample() {
  const points = [];
  for (let i = 0; i < 260; i++) {
    const vol = 18 + Math.abs(Math.sin(i / 11)) * 42 + (i / 260) * 8;
    const spread = 40 + vol * 0.82 + Math.sin(i / 3) * 14 + (i % 5 - 2) * 3.2;
    points.push({ x: +vol.toFixed(2), y: +spread.toFixed(1) });
  }
  return points;
}
function linreg(points) {
  const n = points.length; let sx=0, sy=0, sxx=0, sxy=0, syy=0;
  for (const p of points) { sx+=p.x; sy+=p.y; sxx+=p.x*p.x; sxy+=p.x*p.y; syy+=p.y*p.y; }
  const b = (n*sxy - sx*sy) / (n*sxx - sx*sx);
  const r = (n*sxy - sx*sy) / Math.sqrt((n*sxx - sx*sx) * (n*syy - sy*sy));
  return { slope: b, r, r2: r*r };
}

// deterministic pseudo-OHLC
const BARS = (() => {
  let p = 182; const out = [];
  for (let i = 0; i < 64; i++) {
    const drift = Math.sin(i / 5) * 6 + (i / 64) * 22;
    const o = p; const c = 178 + drift + (i % 3 - 1) * 2.4; const hi = Math.max(o, c) + 1.8; const lo = Math.min(o, c) - 1.8;
    const hh = String(9 + Math.floor(i / 7)).padStart(2, "0");
    out.push({ t: `${hh}:30`, o, h: hi, l: lo, c, v: 1.1e6 + Math.abs(Math.sin(i)) * 2.6e6 }); p = c;
  }
  return out;
})();
const studies = [
  { id: "ma20", label: "MA(20)", on: true, color: "var(--accent)" },
  { id: "ma50", label: "MA(50)", on: true, color: "var(--orange)" },
  { id: "vwap", label: "VWAP", on: false, color: "var(--purple)" },
  { id: "bb", label: "Bollinger(20,2)", on: false, color: "var(--chart-secondary)" },
  { id: "rsi", label: "RSI(14)", on: true, color: "var(--green)" },
];

function ScatterAnalysis() {
  const [xExpr, setXExpr] = useState("AAPL.implied_volatility(3m, atm)");
  const [yExpr, setYExpr] = useState("AAPL.credit_spread(5y)");
  const points = useMemo(() => buildScatterSample(), []);
  const current = points[points.length - 1];
  const fit = useMemo(() => linreg(points), [points]);

  if (typeof ScatterChart !== "function") {
    return (
      <ChartCard title="Scatter analysis" subtitle="Expression-driven X/Y sample" style={{ flex: 1, minHeight: 0 }} height={undefined}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "100%", color: "var(--text-muted)", fontFamily: "var(--font-body)", fontSize: 12 }}>
          Loading chart component…
        </div>
      </ChartCard>
    );
  }

  return (
    <ChartCard
      title="Scatter analysis" subtitle="Expression-driven X/Y sample" style={{ flex: 1, minHeight: 0 }} height={undefined}
      readout={[
        { label: "Corr (r)", value: fit.r.toFixed(2) },
        { label: "R²", value: fit.r2.toFixed(2) },
        { label: "Slope", value: fit.slope.toFixed(2) },
        { label: "Current", value: `${current.x.toFixed(1)}%, ${current.y.toFixed(0)}bps` },
      ]}
    >
      <div style={{ display: "flex", flexDirection: "column", height: "100%", gap: 10 }}>
        <div style={{ display: "flex", gap: 10, padding: "2px 2px 0" }}>
          <div style={{ flex: 1, display: "flex", alignItems: "center", gap: 8 }}>
            <span style={{ fontFamily: "var(--font-body)", fontSize: 10, fontWeight: 700, color: "var(--text-muted)", width: 14 }}>X</span>
            <Input value={xExpr} onChange={(e) => setXExpr(e.target.value)} />
          </div>
          <div style={{ flex: 1, display: "flex", alignItems: "center", gap: 8 }}>
            <span style={{ fontFamily: "var(--font-body)", fontSize: 10, fontWeight: 700, color: "var(--text-muted)", width: 14 }}>Y</span>
            <Input value={yExpr} onChange={(e) => setYExpr(e.target.value)} />
          </div>
          <Kbd>↵ plot</Kbd>
        </div>
        <div style={{ flex: 1, minHeight: 0 }}>
          <ScatterChart points={points} current={current}
            xFmt={(v) => v.toFixed(0) + "%"} yFmt={(v) => v.toFixed(0) + "bps"} />
        </div>
      </div>
    </ChartCard>
  );
}

function ChartingWorkstationScreen() {
  const [tf, setTf] = useState("1D");
  const [mode, setMode] = useState("Chart");
  const [studyState, setStudyState] = useState(studies);
  const cross = 44;
  const toggle = (id) => setStudyState(s => s.map(x => x.id === id ? { ...x, on: !x.on } : x));
  const bar = BARS[cross];
  const overlays = studyState.filter(s => s.win && s.on).map(s => ({ label: s.label, color: s.color, win: s.win }));
  const chg = bar.c - BARS[cross - 1].c;
  return (
    <React.Fragment>
      <WorkstationTopbar moduleLabel="PlotTool" environment="RESEARCH" clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />
      <div style={{ display: "flex", flex: 1, minHeight: 0 }}>
        <main style={{ flex: 1, minWidth: 0, display: "flex", flexDirection: "column", padding: 16, gap: 12 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <h1 style={{ font: "600 16px var(--font-display)", margin: 0, color: "var(--text-primary)" }}>PlotTool</h1>
            <div style={{ flex: 1 }}></div>
            <SegmentedControl size="sm" options={["Chart", "Scatter Analysis"]} value={mode} onChange={setMode} />
          </div>
          {mode === "Chart" ? (
          <ChartCard
            title="AAPL · 1D" subtitle="Apple Inc." style={{ flex: 1, minHeight: 0 }} height={undefined}
            readout={[
              { label: "Last", value: bar.c.toFixed(2), color: chg >= 0 ? "var(--chart-equity)" : "var(--chart-drawdown)" },
              { label: "Chg", value: (chg >= 0 ? "+" : "") + chg.toFixed(2), color: chg >= 0 ? "var(--chart-equity)" : "var(--chart-drawdown)" },
              { label: "O", value: bar.o.toFixed(2) },
              { label: "H", value: bar.h.toFixed(2), color: "var(--chart-equity)" },
              { label: "L", value: bar.l.toFixed(2), color: "var(--chart-drawdown)" },
              { label: "Vol", value: "1.92M" },
            ]}
            actions={
              <div style={{ display: "flex", gap: 4 }}>
                {["1m", "5m", "1H", "1D", "1W"].map(t => (
                  <Button key={t} variant={t === tf ? "primary" : "ghost"} size="sm" onClick={() => setTf(t)}>{t}</Button>
                ))}
              </div>
            }
          >
            <CandleChart bars={BARS} overlays={overlays} crosshairIndex={cross} />
          </ChartCard>
          ) : (
            <ScatterAnalysis />
          )}
        </main>

        <aside style={{ width: 280, flexShrink: 0, borderLeft: "1px solid var(--border)", background: "var(--bg-medium)", overflowY: "auto", padding: 16, display: "flex", flexDirection: "column", gap: 16 }}>
          <div>
            <Eyebrow>Crosshair</Eyebrow>
            <div style={{ marginTop: 8 }}>
              <KeyValueGrid columns={2} items={[
                { label: "Time", value: bar.t + "Z" },
                { label: "Last", value: bar.c.toFixed(2) },
                { label: "Open", value: bar.o.toFixed(2) },
                { label: "High", value: bar.h.toFixed(2) },
                { label: "Low", value: bar.l.toFixed(2) },
                { label: "Vol", value: "1.92M" },
              ]} />
            </div>
          </div>
          <div style={{ height: 1, background: "var(--border)" }}></div>
          <div>
            <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
              <Eyebrow>Studies</Eyebrow>
              <Button variant="link">Add</Button>
            </div>
            <div style={{ display: "flex", flexDirection: "column", gap: 6, marginTop: 10 }}>
              {studyState.map(s => (
                <button key={s.id} onClick={() => toggle(s.id)} style={{
                  display: "flex", alignItems: "center", gap: 9, padding: "7px 10px",
                  border: "1px solid var(--border)", borderRadius: 6, cursor: "pointer",
                  background: s.on ? "var(--bg-light)" : "transparent", textAlign: "left",
                  fontFamily: "var(--font-data)", fontSize: 12, color: "var(--text-primary)"
                }}>
                  <span style={{ width: 10, height: 3, borderRadius: 2, background: s.color, opacity: s.on ? 1 : 0.25 }}></span>
                  <span style={{ flex: 1 }}>{s.label}</span>
                  <span style={{ fontSize: 10, fontVariant: "all-small-caps", color: s.on ? "var(--accent)" : "var(--text-muted)" }}>{s.on ? "on" : "off"}</span>
                </button>
              ))}
            </div>
          </div>
        </aside>
      </div>
      <StatusBar items={[
        { status: "ok", label: "Feed", value: "Polygon · realtime" },
        { label: "Bars", value: "64 · 1D" },
        { label: "Studies", value: studyState.filter(s => s.on).length + " active" },
        { status: "ok", label: "Render", value: "60fps", push: true },
      ]} />
    </React.Fragment>
  );
}

window.ChartingWorkstationScreen = ChartingWorkstationScreen;
if (typeof module !== "undefined") module.exports = { ChartingWorkstationScreen };
