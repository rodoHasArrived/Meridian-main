// Meridian amx-governance — template screen. Mounted by the DC via <x-import>; reads design-system
// components from the compiled bundle.

const {
  WorkstationTopbar, NavRail, StatusBar, PanelSurface, Eyebrow, Button, Badge,
  SegmentedControl, Input, KeyValueGrid, SeverityBadge, GateRail, Callout, Tooltip
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

const TYPE_TONE = { number: "info", percentage: "neutral", text: "neutral", date: "warning" };
const FREQ = { "real-time": "Ready", "daily": "ReviewRequired", "static": "Draft" };
const FREQ_LABEL = { "real-time": "real-time", "daily": "daily", "static": "static" };

const FIELDS = [
  { id: "price", name: "Last Price", fn: "PRICE", cat: "market", type: "number", freq: "real-time",
    desc: "Most recent trading price.", ex: "175.43", fallback: "CLEAN_PRICE if unavailable",
    lineage: "AMX Market Feed → Pricing Engine → PRICE",
    payload: { cusip: "912828YK0", value: 175.43, source: "AMX_REALTIME", staleness_seconds: 1 } },
  { id: "ytm", name: "Yield to Maturity", fn: "YTM", cat: "fixed-income", type: "percentage", freq: "real-time",
    desc: "Total return if held to maturity.", ex: "5.61%", fallback: "Approximation via coupon + spread",
    lineage: "AMX Analytics → Newton-Raphson solver → YTM",
    payload: { cusip: "912828YK0", ytm: 0.0561, price_used: 98.75, day_count: "ACT/ACT", compounding: "semiannual" } },
  { id: "spread_tsy", name: "Spread to Treasury", fn: "SPREAD_TSY", cat: "risk", type: "number", freq: "real-time",
    desc: "Yield spread over comparable Treasury.", ex: "125", fallback: "OAS spread if TSY unavailable",
    lineage: "AMX Market Feed → Treasury Curve → SPREAD_TSY",
    payload: { cusip: "912828YK0", spread_bps: 125, benchmark_tenor: "5Y", bond_yield: 0.0561 } },
  { id: "rating", name: "Credit Rating", fn: "RATING", cat: "risk", type: "text", freq: "daily",
    desc: "Composite credit quality rating.", ex: "AA-", fallback: "NR if unavailable",
    lineage: "Moody's / S&P / Fitch → AMX Rating Service → RATING",
    payload: { cusip: "912828YK0", moodys: "Aaa", sp: "AA+", composite: "AA+", outlook: "stable" } },
  { id: "coupon", name: "Coupon Rate", fn: "COUPON", cat: "fixed-income", type: "percentage", freq: "static",
    desc: "Annual interest rate paid by bond.", ex: "5.25%", fallback: null, lineage: null, payload: null },
  { id: "cusip", name: "CUSIP", fn: "CUSIP", cat: "identity", type: "text", freq: "static",
    desc: "Uniform securities identifier.", ex: "912828YK0", fallback: null, lineage: null, payload: null },
  { id: "duration", name: "Duration", fn: "DURATION", cat: "risk", type: "number", freq: "daily",
    desc: "Macaulay duration in years.", ex: "4.25", fallback: null, lineage: null, payload: null },
];

const CATS = [
  { value: "all", label: "All" }, { value: "identity", label: "Identity" }, { value: "market", label: "Market" },
  { value: "fixed-income", label: "Fixed income" }, { value: "risk", label: "Risk" },
];

function FieldCard({ f, expanded, toggle }) {
  return (
    <PanelSurface style={{ padding: 12, display: "flex", flexDirection: "column", gap: 8 }}>
      <div style={{ display: "flex", alignItems: "flex-start", gap: 8 }}>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 3 }}>
            <span style={{ color: "var(--text-disabled)", fontFamily: "var(--font-data)" }}>⋮⋮</span>
            <span style={{ font: "600 13px var(--font-body)", color: "var(--text-primary)" }}>{f.name}</span>
            <Badge variant={TYPE_TONE[f.type]}>{f.type}</Badge>
          </div>
          <code style={{ font: "12px var(--font-data)", color: "var(--accent)" }}>{f.fn}(cusip)</code>
        </div>
        <SeverityBadge status={FREQ[f.freq]} label={FREQ_LABEL[f.freq]} />
      </div>
      <p style={{ margin: 0, fontSize: 12, color: "var(--text-secondary)" }}>{f.desc}</p>
      <div style={{ display: "flex", gap: 8, fontSize: 12 }}>
        <span style={{ color: "var(--text-muted)" }}>Example</span>
        <code style={{ fontFamily: "var(--font-data)", color: "var(--text-primary)" }}>{f.ex}</code>
      </div>
      {f.fallback && (
        <div style={{ display: "flex", gap: 8, fontSize: 12, paddingTop: 6, borderTop: "1px solid var(--border)" }}>
          <span style={{ color: "var(--text-muted)", flexShrink: 0 }}>Fallback</span>
          <span style={{ color: "var(--text-secondary)" }}>{f.fallback}</span>
        </div>
      )}
      {f.lineage && (
        <div style={{ display: "flex", gap: 8, fontSize: 12 }}>
          <span style={{ color: "var(--text-muted)", flexShrink: 0 }}>Lineage</span>
          <code style={{ fontFamily: "var(--font-data)", color: "var(--text-muted)", fontSize: 11 }}>{f.lineage}</code>
        </div>
      )}
      {f.payload && (
        <div>
          <Button variant="link" size="sm" onClick={toggle}>{expanded ? "▾" : "▸"} Sample payload</Button>
          {expanded && (
            <pre style={{ margin: "6px 0 0", padding: 10, fontSize: 11, fontFamily: "var(--font-data)", lineHeight: 1.6,
              background: "var(--bg-medium)", border: "1px solid var(--border)", color: "var(--text-secondary)", overflowX: "auto" }}>{JSON.stringify(f.payload, null, 2)}</pre>
          )}
        </div>
      )}
      <div style={{ fontSize: 11, fontStyle: "italic", color: "var(--text-disabled)" }}>Drag to insert into formula</div>
    </PanelSurface>
  );
}

function AmxGovernanceScreen() {
  const [cat, setCat] = useState("all");
  const [q, setQ] = useState("");
  const [expanded, setExpanded] = useState({ ytm: true });

  const list = FIELDS.filter((f) => (cat === "all" || f.cat === cat) &&
    (q === "" || (f.name + f.fn + f.desc).toLowerCase().includes(q.toLowerCase())));

  return (
    <React.Fragment>
      <WorkstationTopbar moduleLabel="AMX Catalog" environment="PAPER" clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />
      <div style={{ display: "flex", flex: 1, minHeight: 0 }}>
        <NavRail activeId="catalog" sections={NAV} onSelect={(id) => { const r = ROUTES[id]; if (r) window.location.href = r; }} />
        <main style={{ flex: 1, minWidth: 0, overflowY: "auto", padding: 16, display: "flex", flexDirection: "column", gap: 12 }}>

          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <h1 style={{ font: "600 22px var(--font-display)", margin: 0, color: "var(--text-primary)", whiteSpace: "nowrap" }}>AMX Data Catalog</h1>
            <Badge variant="neutral">{FIELDS.length} fields</Badge>
            <div style={{ flex: 1 }}></div>
            <Badge variant="success" dot>AMX live</Badge>
          </div>

          <PanelSurface flat style={{ padding: 10 }}>
            <GateRail gates={[
              { key: "ingest", label: "AMX ingest", status: "Passed" },
              { key: "mapping", label: "Field mapping", status: "Passed" },
              { key: "proof", label: "Backtest proof", status: "InProgress" },
              { key: "review", label: "Review", status: "ReviewRequired" },
              { key: "approval", label: "Approval", status: "NotStarted" },
            ]} />
          </PanelSurface>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 320px", gap: 12, alignItems: "start" }}>

            {/* Catalog */}
            <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
              <Input placeholder="⌕  Search AMX data fields…" value={q} onChange={(e) => setQ(e.target.value)} />
              <SegmentedControl size="sm" value={cat} onChange={setCat} options={CATS} />
              <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
                {list.length === 0
                  ? <PanelSurface style={{ padding: 24, textAlign: "center", color: "var(--text-muted)" }}>No fields match your search.</PanelSurface>
                  : list.map((f) => <FieldCard key={f.id} f={f} expanded={!!expanded[f.id]} toggle={() => setExpanded((p) => ({ ...p, [f.id]: !p[f.id] }))} />)}
              </div>
            </div>

            {/* Governance rail */}
            <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
              <PanelSurface raised style={{ padding: 14, display: "flex", flexDirection: "column", gap: 12 }}>
                <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                  <Eyebrow>Governance</Eyebrow>
                  <div style={{ flex: 1 }}></div>
                  <SeverityBadge status="InReview" label="in review" />
                </div>
                <KeyValueGrid columns={1} items={[
                  { label: "Version", value: "v4" },
                  { label: "Owner", value: "j.okafor" },
                  { label: "Published", value: "—" },
                  { label: "Current proof", value: <SeverityBadge status="Stale" label="stale" dot={false} /> },
                ]} />
                <Callout tone="warning" title="Proof is stale">Worksheet changed since the last backtest. Re-run before approval — Approve is gated on a current proof.</Callout>
              </PanelSurface>

              <PanelSurface raised style={{ padding: 14, display: "flex", flexDirection: "column", gap: 8 }}>
                <Eyebrow>Reviewers</Eyebrow>
                <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
                  <Badge variant="neutral">a.mehta ×</Badge>
                  <Badge variant="neutral">risk-desk ×</Badge>
                  <Badge variant="neutral">d.chen ×</Badge>
                </div>
                <div style={{ display: "flex", gap: 6, marginTop: 4 }}>
                  <div style={{ flex: 1 }}><Input placeholder="Add reviewer" /></div>
                  <Button variant="ghost" size="sm">Add</Button>
                </div>
              </PanelSurface>

              <PanelSurface raised style={{ padding: 14, display: "flex", flexDirection: "column", gap: 8 }}>
                <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                  <Eyebrow>Audit log</Eyebrow>
                  <div style={{ flex: 1 }}></div>
                  <Badge variant="neutral">4</Badge>
                </div>
                {[
                  { who: "j.okafor", t: "06-29 14:12Z", a: "Status changed: draft → in_review" },
                  { who: "a.mehta", t: "06-29 13:50Z", a: "Reviewer added: risk-desk" },
                  { who: "j.okafor", t: "06-29 11:04Z", a: "Edited cell E7 formula" },
                  { who: "system", t: "06-28 20:00Z", a: "Backtest proof generated · 1,284 trades" },
                ].map((e, i) => (
                  <div key={i} style={{ display: "flex", flexDirection: "column", gap: 2, paddingBottom: 6, borderBottom: i < 3 ? "1px solid var(--border)" : "none" }}>
                    <div style={{ display: "flex", justifyContent: "space-between", fontSize: 11 }}>
                      <span style={{ fontWeight: 600, color: "var(--text-primary)" }}>{e.who}</span>
                      <span style={{ fontFamily: "var(--font-data)", color: "var(--text-muted)" }}>{e.t}</span>
                    </div>
                    <span style={{ fontSize: 12, color: "var(--text-secondary)" }}>{e.a}</span>
                  </div>
                ))}
                <div style={{ display: "flex", gap: 6, marginTop: 4 }}>
                  <Button variant="danger" size="sm">Reject</Button>
                  <div style={{ flex: 1 }}></div>
                  <Tooltip content="Requires a current proof"><Button variant="primary" size="sm" disabled>Approve</Button></Tooltip>
                </div>
              </PanelSurface>
            </div>
          </div>
        </main>
      </div>
      <StatusBar items={[
        { status: "ok", label: "AMX", value: "27 fields mapped" },
        { label: "Review", value: "in review" },
        { status: "warn", label: "Proof", value: "stale" },
        { status: "ok", label: "Feed", value: "AMX · 00:00:01 ago", push: true },
      ]} />
    </React.Fragment>
  );
}

window.AmxGovernanceScreen = AmxGovernanceScreen;
if (typeof module !== "undefined") module.exports = { AmxGovernanceScreen };
