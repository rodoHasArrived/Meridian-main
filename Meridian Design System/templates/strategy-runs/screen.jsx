// Meridian strategy-runs — template screen. Mounted by the DC via <x-import>; reads design-system
// components from the compiled bundle.

const {
  WorkstationTopbar, NavRail, StatusBar, PanelSurface, Eyebrow, Button, Badge,
  SegmentedControl, DenseDataTable, SeverityBadge, KeyValueGrid, GateRail, StatusBanner
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
  catalog: "../amx-governance/index.html", governance: "../amx-governance/index.html",
};

const RUNS = [
  { id: "RUN-2284", strat: "Bond Carry & Roll", env: "FIXTURE", started: "06-30 14:12:04Z", dur: "1.84s", status: "Passed",   ret: "+23.74%", sharpe: "1.42", trades: "1,284",
    eq: [100,101,103,102,105,107,106,110,112,111,115,118,122,121,124], trace: [
      { t: "14:12:04.0", sev: "Info", m: "Run started · fixture replay · HY Corp · 1,284 sessions" },
      { t: "14:12:04.3", sev: "Passed", m: "Data load · 0 gaps · 38 fields mapped" },
      { t: "14:12:05.1", sev: "Passed", m: "Strategy compiled · 38 cells · 1 warning suppressed" },
      { t: "14:12:05.8", sev: "Passed", m: "Backtest complete · 1,284 trades · proof recorded" },
    ] },
  { id: "RUN-2283", strat: "Bond Carry & Roll", env: "PAPER",   started: "06-30 09:02:11Z", dur: "0.92s", status: "ReviewRequired", ret: "+1.12%", sharpe: "0.88", trades: "14",
    eq: [100,100,101,101,102,101,102,103,102,103,103,104,103,104,101], trace: [
      { t: "09:02:11.0", sev: "Info", m: "Paper session opened · IBKR sim" },
      { t: "09:02:11.6", sev: "ReviewRequired", m: "Drawdown 1.4% breached soft limit 1.0% · review" },
      { t: "09:02:11.9", sev: "Passed", m: "Session checkpoint saved" },
    ] },
  { id: "RUN-2281", strat: "Cross-Sector Value", env: "FIXTURE", started: "06-29 20:40:55Z", dur: "2.31s", status: "Passed",   ret: "+17.90%", sharpe: "1.18", trades: "2,041",
    eq: [100,99,101,103,104,103,106,108,110,109,112,113,116,118,117], trace: [
      { t: "20:40:55.0", sev: "Info", m: "Run started · fixture replay · IG Corp" },
      { t: "20:40:57.3", sev: "Passed", m: "Backtest complete · 2,041 trades" },
    ] },
  { id: "RUN-2279", strat: "Duration Ladder", env: "FIXTURE", started: "06-29 16:18:02Z", dur: "1.06s", status: "Failed", ret: "—", sharpe: "—", trades: "0",
    eq: [100,100,100,100,100,100,100,100,100,100,100,100,100,100,100], trace: [
      { t: "16:18:02.0", sev: "Info", m: "Run started · fixture replay · Utility" },
      { t: "16:18:02.4", sev: "Blocked", m: "Cell E7 · #DIV/0 · =YTM(B7) / 0 · run aborted" },
    ] },
  { id: "RUN-2276", strat: "Bond Carry & Roll", env: "LIVE",    started: "06-27 14:00:00Z", dur: "6.4h",  status: "Complete", ret: "+0.38%", sharpe: "1.05", trades: "9",
    eq: [100,100,101,100,101,101,101,100,101,101,102,101,101,101,100], trace: [
      { t: "14:00:00", sev: "Info", m: "Live session · real capital · IBKR" },
      { t: "20:24:00", sev: "Passed", m: "Session closed · 9 fills · +$3,820 realized" },
    ] },
  { id: "RUN-2274", strat: "Cross-Sector Value", env: "FIXTURE", started: "06-26 11:32:40Z", dur: "2.19s", status: "Passed", ret: "+15.22%", sharpe: "1.09", trades: "1,902",
    eq: [100,101,102,101,103,105,104,107,109,108,110,112,113,115,116], trace: [
      { t: "11:32:40.0", sev: "Info", m: "Run started · fixture replay" },
      { t: "11:32:42.2", sev: "Passed", m: "Backtest complete · 1,902 trades" },
    ] },
];

const ENV_BADGE = { FIXTURE: "fixture", PAPER: "paper", LIVE: "live" };

function Spark({ pts, tone }) {
  const w = 240, h = 56, min = Math.min(...pts), max = Math.max(...pts), span = max - min || 1;
  const step = w / (pts.length - 1);
  const d = pts.map((p, i) => `${(i * step).toFixed(1)},${(h - 4 - ((p - min) / span) * (h - 8)).toFixed(1)}`).join(" ");
  return (
    <svg viewBox={`0 0 ${w} ${h}`} style={{ width: "100%", height: "auto", display: "block" }}>
      <polyline fill="none" stroke={tone} strokeWidth="1.5" points={d} />
      <circle cx={(pts.length - 1) * step} cy={h - 4 - ((pts[pts.length - 1] - min) / span) * (h - 8)} r="2.5" fill={tone} />
    </svg>
  );
}

function StrategyRunsScreen() {
  const [sel, setSel] = useState(0);
  const [filt, setFilt] = useState("all");
  const list = RUNS.filter((r) => filt === "all" || r.env.toLowerCase() === filt);
  const run = list[sel] || list[0] || RUNS[0];
  const pnl = (v) => <span style={{ color: v.startsWith("-") ? "var(--red-dim)" : v === "—" ? "var(--text-muted)" : "var(--green-dim)" }}>{v}</span>;
  const sparkTone = run.status === "Failed" ? "var(--red-dim)" : run.ret.startsWith("-") ? "var(--red-dim)" : "var(--chart-equity)";

  return (
    <React.Fragment>
      <WorkstationTopbar moduleLabel="Strategy Runs" environment="PAPER" clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />
      <div style={{ display: "flex", flex: 1, minHeight: 0 }}>
        <NavRail activeId="runs" sections={NAV} onSelect={(id) => { const r = ROUTES[id]; if (r) window.location.href = r; }} />
        <main style={{ flex: 1, minWidth: 0, overflowY: "auto", padding: 16, display: "flex", flexDirection: "column", gap: 12 }}>

          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <h1 style={{ font: "600 22px var(--font-display)", margin: 0, color: "var(--text-primary)", whiteSpace: "nowrap" }}>Strategy Runs</h1>
            <Badge variant="neutral">{RUNS.length} runs</Badge>
            <div style={{ flex: 1 }}></div>
            <SegmentedControl size="sm" value={filt} onChange={(v) => { setFilt(v); setSel(0); }}
              options={[{ value: "all", label: "All" }, { value: "fixture", label: "Fixture" }, { value: "paper", label: "Paper" }, { value: "live", label: "Live" }]} />
            <Button variant="primary" size="sm">New run</Button>
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 320px", gap: 12, alignItems: "start" }}>

            {/* Runs ledger */}
            <PanelSurface style={{ padding: 0 }}>
              <DenseDataTable
                selectedIndex={sel}
                onRowClick={(_, i) => setSel(i)}
                columns={[
                  { key: "id", label: "Run" },
                  { key: "strat", label: "Strategy" },
                  { key: "env", label: "Env", render: (r) => <Badge variant={ENV_BADGE[r.env]} dot>{r.env}</Badge> },
                  { key: "started", label: "Started" },
                  { key: "dur", label: "Dur", align: "right" },
                  { key: "status", label: "Status", render: (r) => <SeverityBadge status={r.status} /> },
                  { key: "ret", label: "Return", align: "right", render: (r) => pnl(r.ret) },
                  { key: "sharpe", label: "Sharpe", align: "right" },
                  { key: "trades", label: "Trades", align: "right" },
                ]}
                rows={list} />
            </PanelSurface>

            {/* Run detail */}
            <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
              <PanelSurface raised style={{ padding: 14, display: "flex", flexDirection: "column", gap: 10 }}>
                <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                  <Eyebrow>Run {run.id}</Eyebrow>
                  <div style={{ flex: 1 }}></div>
                  <SeverityBadge status={run.status} />
                </div>
                <Spark pts={run.eq} tone={sparkTone} />
                <KeyValueGrid columns={2} items={[
                  { label: "Strategy", value: run.strat },
                  { label: "Environment", value: run.env },
                  { label: "Return", value: <span style={{ color: run.ret.startsWith("-") ? "var(--red-dim)" : "var(--green-dim)" }}>{run.ret}</span> },
                  { label: "Sharpe", value: run.sharpe },
                  { label: "Trades", value: run.trades },
                  { label: "Duration", value: run.dur },
                ]} />
                <div style={{ display: "flex", gap: 8 }}>
                  <Button variant="ghost" size="sm">Open report</Button>
                  <Button variant="ghost" size="sm">Re-run</Button>
                </div>
              </PanelSurface>

              <PanelSurface raised style={{ padding: 14, display: "flex", flexDirection: "column", gap: 8 }}>
                <Eyebrow>Run trace</Eyebrow>
                {run.trace.map((e, i) => (
                  <div key={i} style={{ display: "flex", gap: 8, alignItems: "baseline", paddingBottom: 6, borderBottom: i < run.trace.length - 1 ? "1px solid var(--border)" : "none" }}>
                    <span style={{ fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-muted)", whiteSpace: "nowrap" }}>{e.t}</span>
                    <SeverityBadge status={e.sev} dot={true} />
                    <span style={{ fontSize: 12, color: "var(--text-primary)" }}>{e.m}</span>
                  </div>
                ))}
              </PanelSurface>
            </div>
          </div>

          <GateRail gates={[
            { key: "ingest", label: "Data ingest", status: run.status === "Failed" ? "Passed" : "Passed" },
            { key: "compile", label: "Compile", status: run.status === "Failed" ? "Blocked" : "Passed" },
            { key: "run", label: "Execute", status: run.status === "Failed" ? "NotStarted" : "Passed" },
            { key: "proof", label: "Proof", status: run.status === "Failed" ? "NotStarted" : run.status === "ReviewRequired" ? "ReviewRequired" : "Passed" },
            { key: "approve", label: "Approval", status: run.status === "Complete" ? "Passed" : "NotStarted" },
          ]} />

          {run.status === "Failed"
            ? <StatusBanner tone="danger" title={run.id + " failed — compile error"} detail="Cell E7 · #DIV/0 · =YTM(B7) / 0 · run aborted before execution. Fix the divisor and re-run." />
            : <StatusBanner tone="success" title={run.id + " · " + run.status} detail={run.strat + " · " + run.trades + " trades · " + run.ret + " return · proof recorded " + run.started} />}

        </main>
      </div>
      <StatusBar items={[
        { status: "ok", label: "Runs", value: String(RUNS.length) },
        { label: "Selected", value: run.id },
        { label: "Env", value: run.env },
        { status: "ok", label: "Feed", value: "AMX · 00:00:01 ago", push: true },
      ]} />
    </React.Fragment>
  );
}

window.StrategyRunsScreen = StrategyRunsScreen;
if (typeof module !== "undefined") module.exports = { StrategyRunsScreen };
