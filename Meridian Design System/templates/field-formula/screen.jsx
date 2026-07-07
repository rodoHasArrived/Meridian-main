// Meridian field-formula — template screen. Mounted by the DC via <x-import>; reads design-system
// components from the compiled bundle.

const {
  WorkstationTopbar, NavRail, StatusBar, PanelSurface, Eyebrow, Button, Badge,
  SegmentedControl, Combobox, Kbd, Callout, KeyValueGrid
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

const FIELDS = [
  { value: "PRICE", label: "PRICE — last price (912828YK0 → 175.43)" },
  { value: "CLEAN_PRICE", label: "CLEAN_PRICE — ex accrued (98.75)" },
  { value: "COUPON", label: "COUPON — annual rate (5.25%)" },
  { value: "YTM", label: "YTM — yield to maturity (5.61%)" },
  { value: "CURRENT_YIELD", label: "CURRENT_YIELD — coupon / price (5.32%)" },
  { value: "DURATION", label: "DURATION — Macaulay (4.25)" },
  { value: "MOD_DURATION", label: "MOD_DURATION — price sensitivity (4.11)" },
  { value: "RATING", label: "RATING — composite credit (AA-)" },
  { value: "SPREAD_TSY", label: "SPREAD_TSY — spread to Treasury (125bp)" },
];

const SUGGESTIONS = [
  { formula: "annual_coupon / market_price", desc: "Current yield calculation", cat: "yield",
    params: ["annual_coupon — annual coupon in dollars", "market_price — current market price"],
    returns: "Decimal yield (0.0532 = 5.32%)", example: "52.50 / 985 → 0.0533",
    notes: "Simple yield; ignores capital gains and time value." },
  { formula: "face_value * coupon_rate / clean_price", desc: "Current yield from face value", cat: "yield",
    params: ["face_value — par (typically 1000)", "coupon_rate — annual rate as decimal", "clean_price — ex accrued"],
    returns: "Current yield as decimal", example: "1000 * 0.0525 / 985 → 0.0533",
    notes: "Use clean_price to avoid double-counting accrued interest." },
  { formula: "COUPON(cusip) / PRICE(cusip)", desc: "Current yield using AMX data", cat: "yield",
    params: ["cusip — security identifier"], returns: "Current yield from live AMX feed",
    example: 'COUPON("912828YK0") / PRICE("912828YK0")', notes: "Fetches real-time data from AMX feeds automatically." },
  { formula: "YTM(cusip)", desc: "Yield to maturity", cat: "yield",
    params: ["cusip — security identifier"], returns: "YTM as decimal (0.0561 = 5.61%)",
    example: 'YTM("912828YK0")', notes: "Newton-Raphson solver over all coupons + principal." },
  { formula: "SPREAD_TSY(cusip)", desc: "Spread to Treasury", cat: "yield",
    params: ["cusip — security identifier"], returns: "Spread in basis points over comparable Treasury",
    example: 'SPREAD_TSY("037833DK6") → 125', notes: "Matches the nearest Treasury benchmark by maturity." },
];

const CAT_TONE = { yield: "info", comparison: "success", risk: "warning" };

function FieldFormulaScreen() {
  const [sel, setSel] = useState(2);
  const [field, setField] = useState("YTM");
  const [mode, setMode] = useState("formula");
  const s = SUGGESTIONS[sel];
  const lhs = "Let current_yield = ";

  return (
    <React.Fragment>
      <WorkstationTopbar moduleLabel="Field & Formula" environment="PAPER" clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />
      <div style={{ display: "flex", flex: 1, minHeight: 0 }}>
        <NavRail activeId="fields" sections={NAV} onSelect={(id) => { const r = ROUTES[id]; if (r) window.location.href = r; }} />
        <main style={{ flex: 1, minWidth: 0, overflowY: "auto", padding: 16, display: "flex", flexDirection: "column", gap: 12 }}>

          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <h1 style={{ font: "600 22px var(--font-display)", margin: 0, color: "var(--text-primary)", whiteSpace: "nowrap" }}>Field &amp; Formula</h1>
            <Badge variant="neutral">cell C6</Badge>
            <div style={{ flex: 1 }}></div>
            <SegmentedControl size="sm" value={mode} onChange={setMode}
              options={[{ value: "visual", label: "Visual" }, { value: "formula", label: "Formula" }, { value: "code", label: "Code" }]} />
            <Button variant="ghost" size="sm">Insert</Button>
            <Button variant="primary" size="sm">Run · ⌘↵</Button>
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 320px", gap: 12, alignItems: "start" }}>

            {/* Composer */}
            <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
              <PanelSurface raised style={{ padding: 12, display: "flex", alignItems: "flex-end", gap: 10 }}>
                <div style={{ flex: 1 }}>
                  <Combobox label="Field picker — search & insert" options={FIELDS} value={field} onChange={setField} placeholder="Search fields (e.g. 'coupon')…" />
                </div>
                <Button variant="ghost" size="sm">Insert {field}(cusip)</Button>
              </PanelSurface>

              {/* Editor with autocomplete */}
              <PanelSurface flat style={{ padding: 0, position: "relative", overflow: "visible" }}>
                <div style={{ display: "flex", alignItems: "center", gap: 8, padding: "8px 12px", borderBottom: "1px solid var(--border)" }}>
                  <Eyebrow>Formula editor</Eyebrow>
                  <div style={{ flex: 1 }}></div>
                  <span style={{ font: "11px var(--font-data)", color: "var(--text-muted)" }}>1 : {lhs.length + s.formula.length}</span>
                </div>
                <div style={{ display: "flex", font: "14px var(--font-data)", minHeight: 96 }}>
                  <div style={{ width: 40, padding: "12px 0", textAlign: "center", color: "var(--text-disabled)", background: "var(--bg-medium)", borderRight: "1px solid var(--border)" }}>1</div>
                  <div style={{ padding: "12px 14px", color: "var(--text-primary)", lineHeight: 1.7 }}>
                    <span style={{ color: "var(--purple-dim)" }}>Let </span>
                    <span>current_yield = </span>
                    <span style={{ color: "var(--accent)" }}>{s.formula}</span>
                    <span style={{ display: "inline-block", width: 2, height: 18, background: "var(--accent)", verticalAlign: "-3px", marginLeft: 1 }}></span>
                  </div>
                </div>

                {/* Autocomplete popup */}
                <div style={{ position: "absolute", left: 40, top: 84, width: 460, zIndex: 5,
                  background: "var(--card-surface)", border: "1px solid var(--border)", boxShadow: "0 2px 6px rgba(0,0,0,.18)" }}>
                  <div style={{ display: "flex", alignItems: "center", gap: 8, padding: "6px 10px", borderBottom: "1px solid var(--border)", background: "var(--bg-medium)" }}>
                    <Eyebrow>Suggestions</Eyebrow>
                    <div style={{ flex: 1 }}></div>
                    <Kbd>↑</Kbd><Kbd>↓</Kbd><span style={{ fontSize: 11, color: "var(--text-muted)" }}>browse</span>
                    <Kbd>↵</Kbd><span style={{ fontSize: 11, color: "var(--text-muted)" }}>insert</span>
                  </div>
                  {SUGGESTIONS.map((it, i) => (
                    <div key={i} onClick={() => setSel(i)} style={{ display: "flex", alignItems: "center", gap: 10, padding: "8px 12px 8px 10px", cursor: "pointer",
                      borderBottom: i < SUGGESTIONS.length - 1 ? "1px solid var(--border)" : "none",
                      borderLeft: i === sel ? "4px solid var(--accent)" : "4px solid transparent",
                      background: i === sel ? "var(--bg-active)" : "transparent" }}>
                      <code style={{ font: "13px var(--font-data)", color: "var(--text-primary)", flex: 1, whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{it.formula}</code>
                      <span style={{ fontSize: 11, color: "var(--text-muted)", whiteSpace: "nowrap" }}>{it.desc}</span>
                      <Badge variant={CAT_TONE[it.cat]}>{it.cat}</Badge>
                    </div>
                  ))}
                </div>
              </PanelSurface>

              {/* push signature below popup */}
              <div style={{ height: 150 }}></div>

              <PanelSurface raised style={{ padding: 14, display: "flex", flexDirection: "column", gap: 10 }}>
                <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                  <Eyebrow>Signature</Eyebrow>
                  <code style={{ font: "13px var(--font-data)", color: "var(--accent)" }}>{s.formula}</code>
                  <Badge variant={CAT_TONE[s.cat]}>{s.cat}</Badge>
                </div>
                <KeyValueGrid columns={1} items={[
                  { label: "Parameters", value: <div style={{ display: "flex", flexDirection: "column", gap: 3 }}>{s.params.map((p, i) => <span key={i} style={{ fontFamily: "var(--font-data)", fontSize: 12, color: "var(--text-primary)" }}>{p}</span>)}</div> },
                  { label: "Returns", value: <span style={{ fontFamily: "var(--font-data)", fontSize: 12 }}>{s.returns}</span> },
                  { label: "Example", value: <code style={{ fontFamily: "var(--font-data)", fontSize: 12, color: "var(--text-primary)" }}>{s.example}</code> },
                ]} />
                <Callout tone="info">{s.notes}</Callout>
              </PanelSurface>
            </div>

            {/* Function reference */}
            <PanelSurface raised style={{ padding: 14, display: "flex", flexDirection: "column", gap: 12 }}>
              <Eyebrow>Function reference</Eyebrow>
              {[
                { g: "Yield", fns: ["YTM(cusip)", "YTC(cusip)", "YTW(cusip)", "CURRENT_YIELD(cusip)", "SPREAD_TSY(cusip)"] },
                { g: "Price & market", fns: ["PRICE(cusip)", "CLEAN_PRICE(cusip)", "BID(cusip)", "ASK(cusip)", "VOLUME(cusip)"] },
                { g: "Risk", fns: ["DURATION(cusip)", "MOD_DURATION(cusip)", "CONVEXITY(cusip)", "RATING(cusip)"] },
                { g: "Logic", fns: ["IF(cond, a, b)", "AND(...)", "OR(...)", "SUM(range)", "SUMPRODUCT(a, b)"] },
              ].map((grp) => (
                <div key={grp.g} style={{ display: "flex", flexDirection: "column", gap: 5 }}>
                  <div style={{ font: "600 10px var(--font-body)", letterSpacing: ".08em", textTransform: "uppercase", color: "var(--text-muted)" }}>{grp.g}</div>
                  {grp.fns.map((fn) => (
                    <code key={fn} style={{ font: "12px var(--font-data)", color: "var(--text-primary)", padding: "2px 0" }}>{fn}</code>
                  ))}
                </div>
              ))}
            </PanelSurface>
          </div>
        </main>
      </div>
      <StatusBar items={[
        { status: "ok", label: "Autocomplete", value: "context · current_yield" },
        { label: "Suggestions", value: String(SUGGESTIONS.length) },
        { label: "Mode", value: "Formula" },
        { status: "ok", label: "Feed", value: "AMX · 00:00:01 ago", push: true },
      ]} />
    </React.Fragment>
  );
}

window.FieldFormulaScreen = FieldFormulaScreen;
if (typeof module !== "undefined") module.exports = { FieldFormulaScreen };
