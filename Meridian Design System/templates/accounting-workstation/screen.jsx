// Accounting workstation — template screen. Mounted by the DC via <x-import>; reads design-system
// components from the compiled bundle (window.MeridianDesignSystem_4f61be) and the single global
// React. Functional: posting a journal entry appends to the ledger, reconciliation resolves open
// items, tax lots switch realized/unrealized, and the ledger filters live.
const {
  WorkstationTopbar, NavRail, StatusBar, Eyebrow, Button, Badge, MetricCard,
  AmountCell, LedgerTable, AccountTree, StatementTable, ReconciliationPanel, JournalEntryForm, TaxLotTable
} = window.MeridianDesignSystem_4f61be;
const { useState, useMemo } = React;

/* ── screen-level CSS (self-injected once; components inject their own) ── */
(function injectScreenCss() {
  if (document.getElementById("acct-screen-css")) return;
  const el = document.createElement("style");
  el.id = "acct-screen-css";
  el.textContent = `
.acct-tabs{display:flex;gap:2px;border-bottom:1px solid var(--border);}
.acct-tab{appearance:none;border:none;background:transparent;cursor:pointer;font-family:var(--font-body);
  font-size:13px;color:var(--text-secondary);padding:9px 14px 11px;border-bottom:2px solid transparent;
  margin-bottom:-1px;display:inline-flex;align-items:center;gap:7px;transition:color .12s,border-color .12s;}
.acct-tab:hover{color:var(--text-primary);}
.acct-tab--on{color:var(--text-primary);font-weight:600;border-bottom-color:var(--accent);}
.acct-tab__count{font-family:var(--font-data);font-size:10px;color:var(--text-muted);
  border:1px solid var(--border);border-radius:3px;padding:0 5px;line-height:1.5;}
.sec-head{display:flex;align-items:baseline;gap:10px;flex-wrap:wrap;}
.sec-sub{font-family:var(--font-data);font-size:11px;color:var(--text-muted);}
.acct-toolbar{display:flex;align-items:center;gap:8px;flex-wrap:wrap;}
.acct-search{height:30px;box-sizing:border-box;padding:5px 10px;border:1px solid var(--border);
  border-radius:var(--radius-button,6px);background:var(--bg-light);color:var(--text-primary);
  font-family:var(--font-data);font-size:12px;min-width:220px;transition:border-color .12s,box-shadow .12s;}
.acct-search:focus{outline:none;border-color:var(--border-focus,#2F6F8F);box-shadow:0 0 0 2px rgba(47,111,143,.2);}
.acct-search::placeholder{color:var(--text-disabled,#9AA4AF);}
.seg{display:inline-flex;border:1px solid var(--border);border-radius:var(--radius-button,6px);overflow:hidden;}
.seg button{appearance:none;border:none;background:var(--bg-light);cursor:pointer;font-family:var(--font-body);
  font-size:12px;color:var(--text-secondary);padding:6px 12px;border-right:1px solid var(--border);transition:all .12s;}
.seg button:last-child{border-right:none;}
.seg button:hover{background:var(--bg-hover);color:var(--text-primary);}
.seg button[aria-pressed="true"]{background:var(--bg-active,#E6EEF5);color:var(--accent);font-weight:600;box-shadow:inset 0 -2px 0 var(--accent);}
.acct-overlay{position:fixed;inset:0;background:rgba(23,26,31,.42);z-index:40;display:flex;justify-content:flex-end;animation:acct-fade .14s ease;}
@keyframes acct-fade{from{opacity:0;}}
.acct-drawer{width:660px;max-width:92vw;height:100%;background:var(--bg);overflow-y:auto;
  box-shadow:-12px 0 32px rgba(23,26,31,.18);display:flex;flex-direction:column;animation:acct-slide .18s cubic-bezier(.2,.7,.3,1);}
@keyframes acct-slide{from{transform:translateX(28px);opacity:.6;}}
.acct-drawer__hd{display:flex;align-items:center;justify-content:space-between;padding:15px 18px;border-bottom:1px solid var(--border);background:var(--bg-light);}
.acct-x{appearance:none;border:1px solid var(--border);background:var(--bg-light);width:28px;height:28px;
  border-radius:var(--radius-button,6px);cursor:pointer;color:var(--text-muted);font-size:16px;line-height:1;
  display:inline-flex;align-items:center;justify-content:center;}
.acct-x:hover{background:var(--bg-hover);color:var(--text-secondary);}
.acct-empty{padding:22px;text-align:center;font-family:var(--font-body);font-size:12px;color:var(--text-muted);
  border:1px dashed var(--border);border-radius:var(--radius-chip,4px);}
.acct-flash{animation:acct-flash 1.1s ease;}
@keyframes acct-flash{0%{background:var(--green-a10,rgba(22,136,95,.18));}100%{background:transparent;}}
`;
  document.head.appendChild(el);
})();

/* ───────────────────────── seed data ───────────────────────── */
const SEED_LEDGER = [
  { date: "06-02", ref: "JE-1042", account: "1100 · Cash", memo: "Client settlement — AAPL block", debit: 82440, credit: "" },
  { date: "06-02", ref: "JE-1042", account: "1200 · Brokerage rec.", memo: "Client settlement — AAPL block", debit: "", credit: 82440 },
  { date: "06-03", ref: "JE-1043", account: "5100 · Commissions", memo: "Commission & exchange fees", debit: 318.5, credit: "" },
  { date: "06-03", ref: "JE-1043", account: "1100 · Cash", memo: "Commission & exchange fees", debit: "", credit: 318.5 },
  { date: "06-05", ref: "JE-1051", account: "1500 · Custody acct", memo: "Wire to custodian", debit: 40000, credit: "" },
  { date: "06-05", ref: "JE-1051", account: "1100 · Cash", memo: "Wire to custodian", debit: "", credit: 40000 },
  { date: "06-09", ref: "JE-1060", account: "1200 · Brokerage rec.", memo: "Dividend receivable — MSFT", debit: 1290, credit: "" },
  { date: "06-09", ref: "JE-1060", account: "4200 · Dividend income", memo: "Dividend receivable — MSFT", debit: "", credit: 1290 },
];

const COA = [
  { code: "1000", name: "Assets", children: [
    { code: "1100", name: "Cash & equivalents", balance: 163411.5 },
    { code: "1200", name: "Brokerage receivable", balance: 42580 },
    { code: "1500", name: "Custody account", balance: 40000 },
    { code: "1300", name: "Marketable securities", children: [
      { code: "1310", name: "Equities", balance: 884002 },
      { code: "1320", name: "Fixed income", balance: 312500 },
      { code: "1330", name: "Derivatives", balance: 18240 },
    ]},
  ]},
  { code: "2000", name: "Liabilities", children: [
    { code: "2100", name: "Margin payable", balance: -210000 },
    { code: "2200", name: "Accrued fees", balance: -3850 },
    { code: "2300", name: "Taxes payable", balance: -64120 },
  ]},
  { code: "3000", name: "Equity", children: [
    { code: "3100", name: "Contributed capital", balance: -1000000 },
    { code: "3200", name: "Retained earnings", balance: -182863.5 },
  ]},
  { code: "4000", name: "Revenue", children: [
    { code: "4100", name: "Realized trading gains", balance: -412800 },
    { code: "4200", name: "Dividend & interest income", balance: -38420 },
  ]},
  { code: "5000", name: "Expenses", children: [
    { code: "5100", name: "Commissions & fees", balance: 28440 },
    { code: "5200", name: "Data & infrastructure", balance: 19500 },
    { code: "5300", name: "Financing cost", balance: 41200 },
  ]},
];

const STMT_SECTIONS = [
  { label: "Revenue", rows: [
    { label: "Realized trading gains", values: { q2: 412800, q1: 388110 } },
    { label: "Dividend & interest income", values: { q2: 38420, q1: 35900 } },
  ], subtotal: { label: "Total revenue", values: { q2: 451220, q1: 424010 } } },
  { label: "Operating expenses", rows: [
    { label: "Commissions & fees", values: { q2: -28440, q1: -26900 } },
    { label: "Data & infrastructure", values: { q2: -19500, q1: -19500 } },
    { label: "Financing cost", values: { q2: -41200, q1: -38750 }, indent: 1, muted: true },
  ], subtotal: { label: "Total operating expenses", values: { q2: -89140, q1: -85150 } } },
];

const SEED_REC_LEFT = [
  { id: 1, date: "06-02", ref: "DTC-88421", memo: "Settlement credit", amount: 82440, matched: true },
  { id: 2, date: "06-03", ref: "FEE-0093", memo: "Commission", amount: -318.5, matched: true },
  { id: 3, date: "06-05", ref: "WIRE-5510", memo: "Custodian wire", amount: -40000, matched: true },
  { id: 4, date: "06-06", ref: "INT-0210", memo: "Interest accrual", amount: 142.18, matched: false },
  { id: 5, date: "06-09", ref: "DIV-7741", memo: "Dividend MSFT", amount: 1290, matched: true },
];
const SEED_REC_RIGHT = [
  { id: 1, date: "06-02", ref: "JE-1042", memo: "Client settlement", amount: 82440, matched: true },
  { id: 2, date: "06-03", ref: "JE-1043", memo: "Commission & fees", amount: -318.5, matched: true },
  { id: 3, date: "06-05", ref: "JE-1051", memo: "Wire to custodian", amount: -40000, matched: true },
  { id: 5, date: "06-09", ref: "JE-1060", memo: "Dividend receivable", amount: 1290, matched: true },
];

const LOTS = [
  { acquired: "2025-02-14", symbol: "AAPL", quantity: 400, costBasis: 68240, marketValue: 79120, proceeds: 79120 },
  { acquired: "2026-01-09", symbol: "NVDA", quantity: 150, costBasis: 121500, marketValue: 138900, proceeds: 138900 },
  { acquired: "2026-04-22", symbol: "TSLA", quantity: 200, costBasis: 49800, marketValue: 44120, proceeds: 44120 },
  { acquired: "2024-11-03", symbol: "MSFT", quantity: 120, costBasis: 49200, marketValue: 53880, proceeds: 53880 },
  { acquired: "2026-03-17", symbol: "TLT",  quantity: 320, costBasis: 28227, marketValue: 29136, proceeds: 29136 },
];

const ACCOUNT_OPTIONS = [
  "1100 · Cash", "1200 · Brokerage receivable", "1500 · Custody account",
  "4100 · Realized trading gains", "4200 · Dividend income",
  "5100 · Commissions & fees", "5300 · Financing cost",
];

/* ───────────────────────── views ───────────────────────── */
function LedgerView({ rows, lastRef }) {
  const [q, setQ] = useState("");
  const filtered = useMemo(() => {
    const s = q.trim().toLowerCase();
    if (!s) return rows;
    return rows.filter((r) =>
      [r.account, r.memo, r.ref].filter(Boolean).join(" ").toLowerCase().includes(s));
  }, [rows, q]);
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
      <div className="sec-head" style={{ justifyContent: "space-between" }}>
        <div className="sec-head">
          <Eyebrow>General journal · all accounts · June 2026</Eyebrow>
          <span className="sec-sub">{rows.length} postings · USD · debits = credits</span>
        </div>
        <div className="acct-toolbar">
          <input className="acct-search" placeholder="Filter by account, memo, or ref…"
            value={q} onChange={(e) => setQ(e.target.value)} />
        </div>
      </div>
      {filtered.length === 0
        ? <div className="acct-empty">No postings match “{q}”.</div>
        : <LedgerTable rows={filtered} currency="USD" showAccount opening={120000} />}
      {lastRef && <span className="sec-sub">Last posted entry: {lastRef}</span>}
    </div>
  );
}

function TrialView() {
  const [sel, setSel] = useState("1100");
  return (
    <div style={{ display: "grid", gridTemplateColumns: "minmax(0,1fr) 320px", gap: 14, alignItems: "start" }}>
      <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
        <div className="sec-head">
          <Eyebrow>Chart of accounts · trial balance</Eyebrow>
          <span className="sec-sub">As of 2026-06-30 · click an account to inspect</span>
        </div>
        <AccountTree nodes={COA} currency="USD" defaultExpandedDepth={1} selectedCode={sel} onSelect={(n) => setSel(n.code)} valueLabel="Balance" />
      </div>
      <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
        <MetricCard label={"Account " + sel} value="$163,411.50" delta="Closing balance" tone="info" />
        <MetricCard label="Net debits" value="$83,730.00" delta="3 postings" tone="neutral" />
        <MetricCard label="Net credits" value="$40,318.50" delta="2 postings" tone="neutral" />
        <div style={{ display: "flex", gap: 8 }}>
          <Button variant="ghost" size="sm">View postings</Button>
          <Button variant="ghost" size="sm">Export</Button>
        </div>
      </div>
    </div>
  );
}

function ReconcileView({ left, right, onMatchAll }) {
  const open = left.filter((i) => !i.matched).length;
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
      <div className="sec-head" style={{ justifyContent: "space-between" }}>
        <div className="sec-head">
          <Eyebrow>Cash reconciliation · custodian statement vs. ledger</Eyebrow>
          <span className="sec-sub">Account 1100 · period 06-01 → 06-09 · {open} unmatched</span>
        </div>
        {open > 0 && <Button variant="primary" size="sm" onClick={onMatchAll}>Auto-match {open} item{open > 1 ? "s" : ""}</Button>}
      </div>
      <ReconciliationPanel left={{ title: "Statement", items: left }} right={{ title: "Ledger", items: right }} currency="USD" />
    </div>
  );
}

function StatementView() {
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 12, maxWidth: 760 }}>
      <div className="sec-head">
        <Eyebrow>Statement of operations · quarter comparison</Eyebrow>
        <span className="sec-sub">USD · negatives in parentheses</span>
      </div>
      <StatementTable
        currency="USD"
        columns={[{ key: "q2", label: "Q2 2026" }, { key: "q1", label: "Q1 2026" }]}
        sections={STMT_SECTIONS}
        total={{ label: "Net income", values: { q2: 362080, q1: 338860 } }}
      />
    </div>
  );
}

function TaxLotView() {
  const [mode, setMode] = useState("unrealized");
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
      <div className="sec-head" style={{ justifyContent: "space-between" }}>
        <div className="sec-head">
          <Eyebrow>Tax lots · cost basis · {mode}</Eyebrow>
          <span className="sec-sub">As of 2026-06-17 · long-term ≥ 365 days</span>
        </div>
        <div className="seg" role="group" aria-label="P&L basis">
          <button aria-pressed={mode === "unrealized"} onClick={() => setMode("unrealized")}>Unrealized</button>
          <button aria-pressed={mode === "realized"} onClick={() => setMode("realized")}>Realized</button>
        </div>
      </div>
      <TaxLotTable lots={LOTS} currency="USD" asOf="2026-06-17" mode={mode} showSymbol />
    </div>
  );
}

const TABS = [
  { id: "ledger", label: "General ledger" },
  { id: "trial", label: "Trial balance" },
  { id: "reconcile", label: "Reconciliation" },
  { id: "statement", label: "Statements" },
  { id: "taxlots", label: "Tax lots" },
];

/* ───────────────────────── app ───────────────────────── */
function AccountingWorkstationScreen() {
  const [tab, setTab] = useState("ledger");
  const [drawer, setDrawer] = useState(false);
  const [ledger, setLedger] = useState(SEED_LEDGER);
  const [recLeft, setRecLeft] = useState(SEED_REC_LEFT);
  const [lastRef, setLastRef] = useState(null);

  const recOpen = recLeft.filter((i) => !i.matched).length;
  const counts = {
    ledger: ledger.length + " lines",
    trial: "16 accts",
    reconcile: recOpen ? recOpen + " open" : "clear",
    taxlots: LOTS.length + " lots",
  };

  const postEntry = ({ header, lines }) => {
    const rows = lines
      .filter((l) => l.account && (l.debit || l.credit))
      .map((l) => ({
        date: (header.date || "").slice(5) || "06-17",
        ref: header.ref || "JE-—",
        account: l.account,
        memo: header.memo || "Manual entry",
        debit: l.debit || "",
        credit: l.credit || "",
      }));
    if (!rows.length) return;
    setLedger((prev) => [...prev, ...rows]);
    setLastRef(header.ref || "(unreferenced)");
    setDrawer(false);
    setTab("ledger");
  };

  const matchAll = () => setRecLeft((prev) => prev.map((i) => ({ ...i, matched: true })));

  return (
    <div style={{ height: "100vh", display: "flex", flexDirection: "column", background: "var(--bg)", fontFamily: "var(--font-body)", color: "var(--text-secondary)", fontSize: 13 }}>
      <WorkstationTopbar moduleLabel="Accounting" environment="PAPER" clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />
      <div style={{ display: "flex", flex: 1, minHeight: 0 }}>
        <NavRail
          activeId="ledger"
          onSelect={() => {}}
          sections={[
            { label: "Books", items: [
              { id: "ledger", label: "General Ledger", icon: "../../assets/icons/run-ledger.svg", shortcut: "G L" },
              { id: "journals", label: "Journals", icon: "../../assets/icons/data-operations.svg" },
              { id: "coa", label: "Chart of Accounts", icon: "../../assets/icons/aggregate-portfolio.svg" },
            ]},
            { label: "Close", items: [
              { id: "reconcile", label: "Reconciliation", icon: "../../assets/icons/archive-health.svg" },
              { id: "statements", label: "Statements", icon: "../../assets/icons/governance.svg" },
              { id: "taxlots", label: "Tax Lots", icon: "../../assets/icons/account-portfolio.svg" },
            ]},
            { label: "Output", items: [
              { id: "export", label: "Exports", icon: "../../assets/icons/data-export.svg" },
              { id: "audit", label: "Audit Trail", icon: "../../assets/icons/retention-assurance.svg" },
            ]},
          ]}
        />
        <main style={{ flex: 1, minWidth: 0, overflowY: "auto", padding: 16, display: "flex", flexDirection: "column", gap: 12 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <h1 style={{ font: "600 22px var(--font-display)", margin: 0, color: "var(--text-primary)" }}>Accounting</h1>
            <Badge variant="paper" dot>PAPER</Badge>
            <span className="sec-sub" style={{ marginLeft: 2 }}>Fiscal period 2026-Q2 · open</span>
            <div style={{ flex: 1 }}></div>
            <Button variant="ghost" size="sm">Run close</Button>
            <Button variant="primary" size="sm" onClick={() => setDrawer(true)}>New journal entry</Button>
          </div>

          <div className="acct-tabs" role="tablist">
            {TABS.map((t) => (
              <button key={t.id} role="tab" aria-selected={tab === t.id}
                className={"acct-tab" + (tab === t.id ? " acct-tab--on" : "")}
                onClick={() => setTab(t.id)}>
                {t.label}
                {counts[t.id] && <span className="acct-tab__count">{counts[t.id]}</span>}
              </button>
            ))}
          </div>

          {tab === "ledger" && <LedgerView rows={ledger} lastRef={lastRef} />}
          {tab === "trial" && <TrialView />}
          {tab === "reconcile" && <ReconcileView left={recLeft} right={SEED_REC_RIGHT} onMatchAll={matchAll} />}
          {tab === "statement" && <StatementView />}
          {tab === "taxlots" && <TaxLotView />}
        </main>
      </div>

      <StatusBar items={[
        { label: "Ledger", value: "balanced", status: "ok" },
        { label: "Last post", value: lastRef || "JE-1060 · 14:09:55" },
        { label: "Postings", value: ledger.length + " lines" },
        { label: "Recon", value: recOpen ? recOpen + " unmatched" : "reconciled", status: recOpen ? "warn" : "ok" },
        { label: "Period", value: "2026-Q2 open", push: true },
        { label: "Books", value: "USD · accrual" },
      ]} />

      {drawer && (
        <div className="acct-overlay" onClick={(e) => { if (e.target === e.currentTarget) setDrawer(false); }}>
          <div className="acct-drawer" role="dialog" aria-label="New journal entry">
            <div className="acct-drawer__hd">
              <div style={{ display: "flex", flexDirection: "column", gap: 3 }}>
                <Eyebrow>New journal entry</Eyebrow>
                <span style={{ font: "600 16px var(--font-display)", color: "var(--text-primary)" }}>Record a balanced posting</span>
              </div>
              <button className="acct-x" aria-label="Close" onClick={() => setDrawer(false)}>×</button>
            </div>
            <div style={{ padding: 18 }}>
              <JournalEntryForm
                currency="USD"
                accounts={ACCOUNT_OPTIONS}
                initialHeader={{ date: "2026-06-17", ref: "JE-" + (1061 + 0), memo: "" }}
                initialLines={[{ account: "", debit: "", credit: "" }, { account: "", debit: "", credit: "" }]}
                onPost={postEntry}
              />
              <p className="sec-sub" style={{ marginTop: 12 }}>Posting appends balanced lines to the general ledger and jumps to the ledger tab.</p>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

window.AccountingWorkstationScreen = AccountingWorkstationScreen;
if (typeof module !== "undefined") module.exports = { AccountingWorkstationScreen };
