// Meridian strategy-builder — template screen. Mounted by the DC via <x-import>; reads design-system
// components from the compiled bundle.

const {
  WorkstationTopbar, NavRail, StatusBar, PanelSurface, Eyebrow, Button, Badge,
  SegmentedControl, Input, KeyValueGrid, StatusBanner, SeverityBadge, WorksheetGrid
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

const COLS_DEF = [
  { key: "A", label: "Security", width: 176, align: "left" },
  { key: "B", label: "CUSIP", width: 128, align: "left" },
  { key: "C", label: "Price", width: 86 },
  { key: "D", label: "Coupon", width: 80 },
  { key: "E", label: "YTM", width: 78 },
  { key: "F", label: "Dur", width: 70 },
  { key: "G", label: "Signal", width: 96 },
  { key: "H", label: "Weight", width: 80 },
];

// Worksheet model — keyed "<col><row>". t: label|text|num|pct|formula|error
const GRID = {
  A1: { t: "label", v: "Security" }, B1: { t: "label", v: "CUSIP" }, C1: { t: "label", v: "Price" },
  D1: { t: "label", v: "Coupon" }, E1: { t: "label", v: "YTM" }, F1: { t: "label", v: "Dur" },
  G1: { t: "label", v: "Signal" }, H1: { t: "label", v: "Weight" },

  A2: { t: "text", v: "UST 4.25 '30" },   B2: { t: "text", v: "912828YK0" }, C2: { t: "num", v: "98.7500" }, D2: { t: "pct", v: "4.250%" },
  E2: { t: "formula", f: "=YTM(B2)", v: "4.61%" }, F2: { t: "formula", f: "=DURATION(B2)", v: "4.25" },
  G2: { t: "formula", f: '=IF(E2>$min_yield,"BUY","HOLD")', v: "BUY" }, H2: { t: "formula", f: "=E2/SUM(E2:E9)", v: "12.4%" },

  A3: { t: "text", v: "AAPL 3.85 '43" },  B3: { t: "text", v: "037833DK6" }, C3: { t: "num", v: "91.2200" }, D3: { t: "pct", v: "3.850%" },
  E3: { t: "formula", f: "=YTM(B3)", v: "4.92%" }, F3: { t: "formula", f: "=DURATION(B3)", v: "11.80" },
  G3: { t: "formula", f: '=IF(E3>$min_yield,"BUY","HOLD")', v: "BUY" }, H3: { t: "formula", f: "=E3/SUM(E3:E9)", v: "13.2%" },

  A4: { t: "text", v: "MSFT 2.92 '52" },  B4: { t: "text", v: "594918BR8" }, C4: { t: "num", v: "76.4400" }, D4: { t: "pct", v: "2.921%" },
  E4: { t: "formula", f: "=YTM(B4)", v: "4.48%" }, F4: { t: "formula", f: "=DURATION(B4)", v: "18.62" },
  G4: { t: "formula", f: '=IF(F4<$max_dur,"BUY","TRIM")', v: "TRIM" }, H4: { t: "formula", f: "=E4/SUM(E4:E9)", v: "10.1%" },

  A5: { t: "text", v: "PCG-PA util" },    B5: { t: "text", v: "69331CAE6" }, C5: { t: "num", v: "102.110" }, D5: { t: "pct", v: "5.250%" },
  E5: { t: "formula", f: "=COUPON(B5)/PRICE(B5)", v: "5.14%" }, F5: { t: "formula", f: "=DURATION(B5)", v: "6.07" },
  G5: { t: "formula", f: '=IF(E5>$min_yield,"BUY","HOLD")', v: "BUY" }, H5: { t: "formula", f: "=E5/SUM(E5:E9)", v: "13.8%" },

  A6: { t: "text", v: "F 6.10 '32" },     B6: { t: "text", v: "345370CR9" }, C6: { t: "num", v: "104.880" }, D6: { t: "pct", v: "6.100%" },
  E6: { t: "formula", f: "=YTW(B6)", v: "5.38%" }, F6: { t: "formula", f: "=DURATION(B6)", v: "5.41" },
  G6: { t: "formula", f: '=IF(RATING(B6)>="BBB","BUY","SKIP")', v: "BUY" }, H6: { t: "formula", f: "=E6/SUM(E6:E9)", v: "14.4%" },

  A7: { t: "text", v: "T 1.50 '30" },     B7: { t: "text", v: "91282CGT0" }, C7: { t: "num", v: "84.0600" }, D7: { t: "pct", v: "1.500%" },
  E7: { t: "error", f: "=YTM(B7) / 0", v: "#DIV/0" }, F7: { t: "formula", f: "=DURATION(B7)", v: "5.62" },
  G7: { t: "formula", f: '=IF(E7>$min_yield,"BUY","HOLD")', v: "#REF" }, H7: { t: "num", v: "" },

  A8: { t: "text", v: "GS 4.80 '34" },    B8: { t: "text", v: "38141GYL9" }, C8: { t: "num", v: "96.5500" }, D8: { t: "pct", v: "4.800%" },
  E8: { t: "formula", f: "=YTM(B8)", v: "5.27%" }, F8: { t: "formula", f: "=DURATION(B8)", v: "7.94" },
  G8: { t: "formula", f: '=IF(E8>$min_yield,"BUY","HOLD")', v: "BUY" }, H8: { t: "formula", f: "=E8/SUM(E2:E9)", v: "12.9%" },

  A9: { t: "text", v: "VZ 2.55 '31" },    B9: { t: "text", v: "92343VFM3" }, C9: { t: "num", v: "87.9300" }, D9: { t: "pct", v: "2.550%" },
  E9: { t: "formula", f: "=YTM(B9)", v: "4.55%" }, F9: { t: "formula", f: "=DURATION(B9)", v: "6.71" },
  G9: { t: "formula", f: '=IF(E9>$min_yield,"BUY","HOLD")', v: "HOLD" }, H9: { t: "formula", f: "=E9/SUM(E2:E9)", v: "11.2%" },

  A11: { t: "label", v: "Portfolio YTM" }, E11: { t: "formula", f: "=SUMPRODUCT(E2:E9,H2:H9)", v: "4.87%" },
  A12: { t: "label", v: "Portfolio Dur" }, F12: { t: "formula", f: "=SUMPRODUCT(F2:F9,H2:H9)", v: "8.34" },
};

const TOTAL_ROWS = 13;

// Map the worksheet model to the WorksheetGrid cell shape ({ type, value, formula }).
const CELLS = Object.fromEntries(Object.entries(GRID).map(([k, c]) => [k, { type: c.t, value: c.v, formula: c.f }]));

function cellRefsIn(formula) {
  if (!formula) return [];
  const m = formula.match(/\$?[A-H][0-9]{1,2}(?::[A-H][0-9]{1,2})?|\$[a-z_]+/g);
  return m ? Array.from(new Set(m)) : [];
}

function StrategyBuilderScreen() {
  const [active, setActive] = useState("E2");
  const [mode, setMode] = useState("formula");
  const [env] = useState("PAPER");
  const [cells, setCells] = useState(CELLS);
  const cell = cells[active] || {};

  const deps = cellRefsIn(cell.formula);

  return (
    <React.Fragment>
      <WorkstationTopbar moduleLabel="Strategy Builder" environment={env} clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />
      <div style={{ display: "flex", flex: 1, minHeight: 0 }}>
        <NavRail activeId="builder" sections={NAV} onSelect={(id) => { const r = ROUTES[id]; if (r) window.location.href = r; }} />
        <main style={{ flex: 1, minWidth: 0, overflowY: "auto", padding: 16, display: "flex", flexDirection: "column", gap: 12 }}>

          {/* Title + toolbar */}
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <h1 style={{ font: "600 22px var(--font-display)", margin: 0, color: "var(--text-primary)", whiteSpace: "nowrap" }}>Bond Carry &amp; Roll</h1>
            <Badge variant="paper" dot>PAPER</Badge>
            <Badge variant="neutral">v4 · draft</Badge>
            <div style={{ flex: 1 }}></div>
            <SegmentedControl size="sm" value={mode} onChange={setMode}
              options={[{ value: "visual", label: "Visual" }, { value: "formula", label: "Formula" }, { value: "code", label: "Code" }]} />
            <Button variant="ghost" size="sm">Save</Button>
            <Button variant="primary" size="sm">Run strategy</Button>
          </div>

          {/* Grid + formula bar — Meridian WorksheetGrid component */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr 280px", gap: 12, alignItems: "start" }}>

            <WorksheetGrid columns={COLS_DEF} rows={TOTAL_ROWS} cells={cells}
              activeCell={active} onActiveCellChange={setActive}
              editable onCellCommit={(ref, val) => setCells((p) => {
                const isF = val.trim().startsWith("=");
                const prevType = p[ref] && p[ref].type;
                const type = isF ? "formula" : prevType === "label" ? "label" : isNaN(parseFloat(val)) ? "text" : "num";
                return { ...p, [ref]: { value: val, formula: isF ? val : undefined, type } };
              })} />

            {/* Right rail */}
            <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
              <PanelSurface raised style={{ padding: 14, display: "flex", flexDirection: "column", gap: 10 }}>
                <Eyebrow>Parameters</Eyebrow>
                <Input label="min_yield" defaultValue="4.50%" />
                <Input label="max_dur" defaultValue="12.0" />
                <Input label="target_weight" defaultValue="equal" />
              </PanelSurface>

              <PanelSurface raised style={{ padding: 14, display: "flex", flexDirection: "column", gap: 10 }}>
                <Eyebrow>Cell inspector</Eyebrow>
                <KeyValueGrid columns={1} items={[
                  { label: "Reference", value: active },
                  { label: "Type", value: cell.type === "error" ? <SeverityBadge status="Blocked" label="error" /> : (cell.type || "empty") },
                  { label: "Formula", value: cell.formula ? <code style={{ fontFamily: "var(--font-data)", fontSize: 12, color: "var(--text-primary)" }}>{cell.formula}</code> : "—" },
                  { label: "Value", value: <span style={{ fontFamily: "var(--font-data)", color: cell.type === "error" ? "var(--red-dim)" : "var(--text-primary)" }}>{cell.value || "—"}</span> },
                  { label: "References", value: deps.length ? deps.join(" · ") : "none" },
                ]} />
              </PanelSurface>
            </div>
          </div>

          {cell.type === "error"
            ? <StatusBanner tone="danger" title={"Cell " + active + " — division by zero"} detail="=YTM(B7) / 0 · fix the divisor or guard with IF before running. 1 blocking error across 38 cells." />
            : <StatusBanner tone="success" title="Worksheet recalculated" detail="38 cells · 1 error · last run 14:12:04Z · portfolio YTM 4.87% · duration 8.34" />}

        </main>
      </div>
      <StatusBar items={[
        { status: "ok", label: "Engine", value: "AMX formula · ready" },
        { label: "Cells", value: "38" },
        { status: "err", label: "Errors", value: "1" },
        { label: "Recalc", value: "12ms" },
        { status: "ok", label: "Feed", value: "AMX · 00:00:01 ago", push: true },
      ]} />
    </React.Fragment>
  );
}

window.StrategyBuilderScreen = StrategyBuilderScreen;
if (typeof module !== "undefined") module.exports = { StrategyBuilderScreen };
