// Meridian covered-call lab — template screen. Mounted by the DC via <x-import>.
// Data shapes mirror Meridian.Ui.Shared CoveredCallContracts: CoveredCallBacktestRequest
// (the 19-param request), CoveredCallMetricsDto, CoveredCallTradeDto, CoveredCallChainRow
// (MeetsAllFilters / RejectReason), CoveredCallRunSummary.

const {
  WorkstationTopbar, NavRail, StatusBar, PanelSurface, Eyebrow, Button, Badge,
  Tabs, TabPanel, DenseDataTable, SeverityBadge, KeyValueGrid, MetricCard,
  SegmentedControl, Slider, Input, FormField, EquityCurve
} = window.MeridianDesignSystem_4f61be;
const { useState } = React;

const NAV = [
  { label: "Strategy", items: [
    { id: "builder", label: "Strategy Builder", icon: "../../assets/icons/strategy-builder.svg" },
    { id: "backtest", label: "Backtest Builder", icon: "../../assets/icons/backtest.svg" },
    { id: "cclab", label: "Covered-Call Lab", icon: "../../assets/icons/order-book.svg", shortcut: "G C" },
  ]},
  { label: "Review", items: [
    { id: "runs", label: "Strategy Runs", icon: "../../assets/icons/strategy-runs.svg" },
  ]},
];
const ROUTES = { builder: "../strategy-builder/index.html", backtest: "../backtest-builder/index.html", runs: "../strategy-runs/index.html" };

// ── Equity curves (CoveredCallEquityPoint: strategy vs. underlying) ──────────
const STRAT = [100,100.8,101.5,101.2,102.4,103.1,103.8,103.5,104.6,105.2,105.9,106.4,105.8,106.9,107.8,108.2,108.9,109.6,109.1,110.2,111.0,111.6,112.3,111.8,112.9,113.6,114.2,114.8,115.5,114.8];
const UNDER = [100,101.2,102.4,101.1,103.0,104.2,105.6,104.0,105.8,107.1,108.0,109.2,107.4,109.0,110.6,111.2,112.4,113.8,112.0,113.6,115.0,116.2,117.6,115.8,117.4,118.8,120.0,121.4,122.8,120.6];
const LABELS = ["Jan","","","","","Feb","","","","Mar","","","","Apr","","","","May","","","","Jun","","","","Jul","","","",""];

// ── CoveredCallMetricsDto (numeric, formatted at render) ─────────────────────
const METRICS = {
  cagr: 0.148, annualizedVolatility: 0.162, sharpeRatio: 1.21, sortinoRatio: 1.64,
  calmarRatio: 1.19, maxDrawdownPct: -0.124, winRate: 0.82, assignmentRate: 0.11,
  averageHoldingDays: 23.4, totalOptionTrades: 63, assignedTrades: 7,
  totalPremiumCollected: 48210, totalOptionPnl: 31480, upCapture: 0.71, downCapture: 0.55,
  monthlyVar5Pct: -0.031, returnSkewness: -0.42, annualizedTurnover: 3.1,
};
const pct = (v) => (v * 100).toFixed(1) + "%";
const usd = (v) => "$" + v.toLocaleString("en-US");

// ── CoveredCallTradeDto rows ─────────────────────────────────────────────────
const TRADES = [
  { strike: "$470", expiration: "2026-06-19", contracts: 8, entryDate: "2026-05-26", entryCredit: "$4.85", exitDate: "2026-06-19", exitDebit: "$0.00", exitReason: "Expired", netPnl: "+$3,880", holdingDays: 24, isWin: true, wasAssigned: false },
  { strike: "$465", expiration: "2026-05-15", contracts: 8, entryDate: "2026-04-20", entryCredit: "$5.20", exitDate: "2026-05-08", exitDebit: "$1.04", exitReason: "TakeProfit", netPnl: "+$3,328", holdingDays: 18, isWin: true, wasAssigned: false },
  { strike: "$450", expiration: "2026-04-17", contracts: 8, entryDate: "2026-03-23", entryCredit: "$6.10", exitDate: "2026-04-14", exitDebit: "$8.42", exitReason: "Rolled", netPnl: "−$1,856", holdingDays: 22, isWin: false, wasAssigned: false },
  { strike: "$445", expiration: "2026-03-20", contracts: 8, entryDate: "2026-02-24", entryCredit: "$5.65", exitDate: "2026-03-20", exitDebit: "$12.30", exitReason: "Assigned", netPnl: "−$5,320", holdingDays: 25, isWin: false, wasAssigned: true },
  { strike: "$430", expiration: "2026-02-20", contracts: 8, entryDate: "2026-01-26", entryCredit: "$4.95", exitDate: "2026-02-13", exitDebit: "$0.99", exitReason: "TakeProfit", netPnl: "+$3,168", holdingDays: 18, isWin: true, wasAssigned: false },
];

// ── CoveredCallChainRow (MeetsAllFilters / RejectReason) ─────────────────────
const CHAIN = [
  { strike: "$475", expiration: "2026-07-24", dte: 19, bid: "$3.85", ask: "$3.95", delta: "0.31", iv: "24.2%", oi: 14210, vol: 3120, meets: true, reject: null },
  { strike: "$480", expiration: "2026-07-24", dte: 19, bid: "$2.90", ask: "$3.00", delta: "0.26", iv: "23.8%", oi: 18440, vol: 4890, meets: true, reject: null },
  { strike: "$485", expiration: "2026-08-21", dte: 47, bid: "$4.10", ask: "$4.25", delta: "0.29", iv: "24.9%", oi: 9210, vol: 1240, meets: true, reject: null },
  { strike: "$470", expiration: "2026-07-24", dte: 19, bid: "$4.95", ask: "$5.10", delta: "0.38", iv: "24.6%", oi: 11020, vol: 2410, meets: false, reject: "Delta 0.38 > max 0.35" },
  { strike: "$500", expiration: "2026-08-21", dte: 47, bid: "$1.55", ask: "$1.80", delta: "0.14", iv: "23.1%", oi: 820, vol: 64, meets: false, reject: "OI 820 < min 1,000" },
  { strike: "$490", expiration: "2026-07-10", dte: 5, bid: "$0.85", ask: "$0.92", delta: "0.18", iv: "22.4%", oi: 6110, vol: 980, meets: false, reject: "DTE 5 < min 7" },
];

// ── CoveredCallRunSummary rows ───────────────────────────────────────────────
const LIBRARY = [
  { runId: "CC-0118", underlying: "MSFT", period: "2026-01-02 → 2026-06-30", label: "Baseline 0.35Δ", status: "Completed", cagr: "14.8%", sharpe: "1.21", winRate: "82%" },
  { runId: "CC-0117", underlying: "MSFT", period: "2026-01-02 → 2026-06-30", label: "Tight 0.25Δ", status: "Completed", cagr: "11.2%", sharpe: "1.34", winRate: "89%" },
  { runId: "CC-0115", underlying: "AAPL", period: "2025-07-01 → 2026-06-30", label: null, status: "Failed", cagr: "—", sharpe: "—", winRate: "—" },
  { runId: "CC-0112", underlying: "MSFT", period: "2025-01-02 → 2025-12-31", label: "FY25 replay", status: "Completed", cagr: "9.6%", sharpe: "0.98", winRate: "78%" },
];

// ── CoveredCallBacktestRequest params panel ──────────────────────────────────
function ParamsPanel() {
  const [ratio, setRatio] = useState(0.75);
  const [maxDelta, setMaxDelta] = useState(0.35);
  const [minIv, setMinIv] = useState(50);
  const [maxSpread, setMaxSpread] = useState(5);
  const [takeProfit, setTakeProfit] = useState(80);
  const [rollDelta, setRollDelta] = useState(0.55);
  const [scoring, setScoring] = useState("relative");
  return (
    <PanelSurface raised style={{ padding: 14, display: "flex", flexDirection: "column", gap: 12, alignSelf: "start" }}>
      <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
        <Eyebrow>Backtest request</Eyebrow>
        <div style={{ flex: 1 }}></div>
        <SeverityBadge status="Completed" label="Run CC-0118" />
      </div>
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
        <FormField label="Underlying"><Input value="MSFT" readOnly /></FormField>
        <FormField label="Min strike"><Input value="440" readOnly /></FormField>
        <FormField label="From"><Input value="2026-01-02" readOnly /></FormField>
        <FormField label="To"><Input value="2026-06-30" readOnly /></FormField>
      </div>
      <Slider label="Overwrite ratio" value={ratio} onChange={setRatio} min={0} max={1} step={0.05} showValue valueFmt={(v) => (v * 100).toFixed(0) + "%"} />
      <Slider label="Max delta" value={maxDelta} onChange={setMaxDelta} min={0.1} max={0.6} step={0.01} showValue valueFmt={(v) => v.toFixed(2) + "Δ"} />
      <Slider label="Min IV percentile" value={minIv} onChange={setMinIv} min={0} max={100} step={5} showValue valueFmt={(v) => v.toFixed(0)} />
      <Slider label="Max spread" value={maxSpread} onChange={setMaxSpread} min={1} max={15} step={0.5} showValue valueFmt={(v) => v.toFixed(1) + "%"} />
      <Slider label="Take-profit capture" value={takeProfit} onChange={setTakeProfit} min={40} max={100} step={5} showValue valueFmt={(v) => v.toFixed(0) + "%"} />
      <Slider label="Roll at delta" value={rollDelta} onChange={setRollDelta} min={0.4} max={0.8} step={0.01} showValue valueFmt={(v) => v.toFixed(2) + "Δ"} />
      <KeyValueGrid columns={2} items={[
        { label: "DTE window", value: "7 – 60" },
        { label: "Min OI / vol", value: "1,000 / 100" },
        { label: "Ex-div window", value: "7 days" },
        { label: "Initial", value: "$100k + 100 sh" },
      ]} />
      <FormField label="Scoring mode">
        <SegmentedControl size="sm" value={scoring} onChange={setScoring}
          options={[{ value: "basic", label: "Basic" }, { value: "relative", label: "Relative" }]} />
      </FormField>
      <div style={{ display: "flex", gap: 8 }}>
        <Button variant="ghost" size="sm" style={{ flex: 1 }}>Preview chain</Button>
        <Button variant="primary" size="sm" style={{ flex: 1 }}>Queue run</Button>
      </div>
    </PanelSurface>
  );
}

function CoveredCallLabScreen() {
  const pnlCell = (v) => <span style={{ color: v.startsWith("−") || v.startsWith("-") ? "var(--red-dim)" : "var(--green-dim)", fontFamily: "var(--font-data)" }}>{v}</span>;
  return (
    <React.Fragment>
      <WorkstationTopbar moduleLabel="Covered-Call Lab" environment="FIXTURE" clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />
      <div style={{ display: "flex", flex: 1, minHeight: 0 }}>
        <NavRail activeId="cclab" sections={NAV} onSelect={(id) => { const r = ROUTES[id]; if (r) window.location.href = r; }} />
        <main style={{ flex: 1, minWidth: 0, overflowY: "auto", padding: 16, display: "flex", flexDirection: "column", gap: 12 }}>

          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <h1 style={{ font: "600 22px var(--font-display)", margin: 0, color: "var(--text-primary)", whiteSpace: "nowrap" }}>Covered-Call Lab</h1>
            <Badge variant="neutral">MSFT · 2026 H1</Badge>
            <Badge variant="fixture" dot>FIXTURE</Badge>
            <div style={{ flex: 1 }}></div>
            <Button variant="ghost" size="sm">Compare runs</Button>
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "320px 1fr", gap: 12, alignItems: "start" }}>
            <ParamsPanel />

            <Tabs tabs={["Result", "Chain preview", "Run library"]}>
              <TabPanel>
                <div style={{ display: "flex", flexDirection: "column", gap: 12, paddingTop: 10 }}>
                  <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 12 }}>
                    <MetricCard label="CAGR" value={pct(METRICS.cagr)} delta={"vs underlying " + pct(0.206)} tone="info" />
                    <MetricCard label="Sharpe" value={METRICS.sharpeRatio.toFixed(2)} delta={"Sortino " + METRICS.sortinoRatio.toFixed(2)} />
                    <MetricCard label="Max drawdown" value={pct(METRICS.maxDrawdownPct)} tone="danger" />
                    <MetricCard label="Win rate" value={pct(METRICS.winRate)} delta={METRICS.totalOptionTrades + " trades · " + pct(METRICS.assignmentRate) + " assigned"} tone="success" />
                  </div>

                  <PanelSurface raised style={{ padding: 14, flexShrink: 0 }}>
                    <Eyebrow>Strategy vs. underlying — equity</Eyebrow>
                    <div style={{ height: 240, marginTop: 8 }}>
                      <EquityCurve
                        series={[
                          { label: "Covered-call strategy", color: "var(--chart-equity, #2F6F8F)", points: STRAT },
                          { label: "MSFT buy & hold", color: "var(--chart-benchmark, #6E7781)", points: UNDER, dashed: true },
                        ]}
                        labels={LABELS}
                        valueFmt={(v) => v.toFixed(0)} />
                    </div>
                  </PanelSurface>

                  <div style={{ display: "grid", gridTemplateColumns: "1fr 280px", gap: 12, alignItems: "start" }}>
                    <PanelSurface style={{ padding: 0 }}>
                      <div style={{ padding: "10px 14px", borderBottom: "1px solid var(--border)" }}><Eyebrow>Closed trades</Eyebrow></div>
                      <DenseDataTable
                        columns={[
                          { key: "strike", label: "Strike", align: "right" },
                          { key: "expiration", label: "Expiration" },
                          { key: "contracts", label: "Qty", align: "right" },
                          { key: "entryCredit", label: "Credit", align: "right" },
                          { key: "exitDebit", label: "Debit", align: "right" },
                          { key: "exitReason", label: "Exit", render: (r) => <Badge variant={r.wasAssigned ? "warning" : r.isWin ? "success" : "neutral"}>{r.exitReason}</Badge> },
                          { key: "netPnl", label: "Net P&L", align: "right", render: (r) => pnlCell(r.netPnl) },
                          { key: "holdingDays", label: "Days", align: "right" },
                        ]}
                        rows={TRADES} />
                    </PanelSurface>
                    <PanelSurface raised style={{ padding: 14 }}>
                      <Eyebrow>Full metrics</Eyebrow>
                      <KeyValueGrid columns={1} items={[
                        { label: "Premium collected", value: usd(METRICS.totalPremiumCollected) },
                        { label: "Option P&L", value: usd(METRICS.totalOptionPnl) },
                        { label: "Volatility (ann.)", value: pct(METRICS.annualizedVolatility) },
                        { label: "Calmar", value: METRICS.calmarRatio.toFixed(2) },
                        { label: "Up / down capture", value: pct(METRICS.upCapture) + " / " + pct(METRICS.downCapture) },
                        { label: "Monthly VaR 5%", value: pct(METRICS.monthlyVar5Pct) },
                        { label: "Skew", value: METRICS.returnSkewness.toFixed(2) },
                        { label: "Turnover (ann.)", value: METRICS.annualizedTurnover.toFixed(1) + "x" },
                        { label: "Avg holding", value: METRICS.averageHoldingDays.toFixed(1) + " days" },
                      ]} />
                    </PanelSurface>
                  </div>
                </div>
              </TabPanel>

              <TabPanel>
                <div style={{ display: "flex", flexDirection: "column", gap: 10, paddingTop: 10 }}>
                  <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                    <Eyebrow>Chain preview · MSFT as-of 2026-07-02 · underlying $468.20</Eyebrow>
                    <div style={{ flex: 1 }}></div>
                    <Badge variant="neutral">412 contracts scanned · 3 pass</Badge>
                  </div>
                  <PanelSurface style={{ padding: 0 }}>
                    <DenseDataTable
                      columns={[
                        { key: "strike", label: "Strike", align: "right" },
                        { key: "expiration", label: "Expiration" },
                        { key: "dte", label: "DTE", align: "right" },
                        { key: "bid", label: "Bid", align: "right" },
                        { key: "ask", label: "Ask", align: "right" },
                        { key: "delta", label: "Delta", align: "right" },
                        { key: "iv", label: "IV", align: "right" },
                        { key: "oi", label: "OI", align: "right", render: (r) => r.oi.toLocaleString() },
                        { key: "vol", label: "Vol", align: "right", render: (r) => r.vol.toLocaleString() },
                        { key: "meets", label: "Filters", render: (r) => r.meets
                          ? <SeverityBadge status="Passed" label="Pass" />
                          : <SeverityBadge status="Blocked" label={r.reject} /> },
                      ]}
                      rows={CHAIN} />
                  </PanelSurface>
                </div>
              </TabPanel>

              <TabPanel>
                <div style={{ paddingTop: 10 }}>
                  <PanelSurface style={{ padding: 0 }}>
                    <DenseDataTable
                      columns={[
                        { key: "runId", label: "Run" },
                        { key: "underlying", label: "Underlying" },
                        { key: "period", label: "Period" },
                        { key: "label", label: "Label", render: (r) => r.label || <span style={{ color: "var(--text-muted)" }}>—</span> },
                        { key: "status", label: "Status", render: (r) => <SeverityBadge status={r.status} /> },
                        { key: "cagr", label: "CAGR", align: "right" },
                        { key: "sharpe", label: "Sharpe", align: "right" },
                        { key: "winRate", label: "Win rate", align: "right" },
                      ]}
                      rows={LIBRARY} />
                  </PanelSurface>
                </div>
              </TabPanel>
            </Tabs>
          </div>

        </main>
      </div>
      <StatusBar items={[
        { status: "ok", label: "Run", value: "CC-0118 · Completed · 2.3s" },
        { label: "Underlying", value: "MSFT $468.20" },
        { label: "Chain", value: "412 scanned · 3 pass" },
        { status: "ok", label: "Data", value: "fixture · 2026-07-02", push: true },
      ]} />
    </React.Fragment>
  );
}

window.CoveredCallLabScreen = CoveredCallLabScreen;
if (typeof module !== "undefined") module.exports = { CoveredCallLabScreen };
