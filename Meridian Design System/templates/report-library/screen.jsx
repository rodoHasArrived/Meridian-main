// Report Library — template screen. Mounted by the DC via <x-import>; reads design-system
// components from the compiled bundle (window.MeridianDesignSystem_4f61be) and the single
// global React. A plain report catalog answering "what report do I need to run?" — dense
// table of reports with readiness, a parameter dialog to run one, and a right rail summarizing
// readiness + recent runs. Advanced/internal fields (template id, manifest path, provenance,
// distribution job) stay hidden inside each row's details until opened.
const {
  WorkstationTopbar, NavRail, StatusBar, PanelSurface, Eyebrow, Button, Badge,
  Input, Select, SeverityBadge, EmptyState, ExpandableDataTable, EntitySummary,
  Accordion, Checkbox, Callout, EvidenceLink, Dialog, DialogHeader, DialogBody, DialogFooter,
  ToastProvider,
} = window.MeridianDesignSystem_4f61be;
const { useState, useMemo } = React;

/* ── screen-level CSS (self-injected once; components inject their own) ── */
(function injectScreenCss() {
  if (document.getElementById("rl-screen-css")) return;
  const el = document.createElement("style");
  el.id = "rl-screen-css";
  el.textContent = `
.rl-chip{display:inline-flex;align-items:center;border:1px solid var(--border,#D7DCE2);
  background:var(--bg-medium,#F5F7FA);color:var(--text-secondary,#4D5967);font-family:var(--font-data);
  font-size:10.5px;padding:2px 7px;border-radius:var(--radius-chip,2px);white-space:nowrap;}
.rl-chip--more{color:var(--text-muted,#59636F);background:transparent;}
.rl-fmt{font-family:var(--font-data);font-size:10px;color:var(--text-muted,#59636F);
  border:1px solid var(--border,#D7DCE2);padding:1px 5px;border-radius:var(--radius-chip,2px);letter-spacing:.02em;}
.rl-detail-grid{display:grid;grid-template-columns:1.6fr 1fr;gap:20px;}
.rl-detail-label{font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#59636F);margin-bottom:5px;}
.rl-detail-text{font-size:12.5px;line-height:1.55;color:var(--text-secondary,#4D5967);margin:0 0 12px;}
.rl-rail-row{display:flex;align-items:center;justify-content:space-between;gap:8px;padding:7px 0;
  border-bottom:1px solid var(--border-divider,#E5E9EE);}
.rl-rail-row:last-child{border-bottom:none;}
.rl-rail-link{background:none;border:none;padding:0;text-align:left;cursor:pointer;
  font-family:var(--font-body);font-size:12px;color:var(--text-primary,#22272E);}
.rl-rail-link:hover{color:var(--accent,#2F6F8F);text-decoration:underline;}
.rl-rail-time{font-family:var(--font-data);font-size:10.5px;color:var(--text-muted,#59636F);white-space:nowrap;}
`;
  document.head.appendChild(el);
})();

/* ───────────────────────── data ───────────────────────── */
const CATEGORIES = [
  "Financial Statements", "Investor Reporting", "Reconciliation", "Operations",
  "Exceptions", "Tax", "Audit", "Custom",
];

const PERIODS = ["Jun 2026 (open)", "May 2026", "Apr 2026", "Q2 2026", "FY 2025"];

const REPORTS = [
  {
    id: "trial-balance", name: "Trial Balance", category: "Financial Statements",
    produces: "Debit/credit balances for every GL account as of a selected date, proving total debits equal total credits.",
    requiredData: ["General ledger", "Chart of accounts"],
    lastRun: "2026-06-30 21:05:00Z", owner: "Controller — J. Alvarez",
    format: ["PDF", "XLSX"], status: "ready",
    templateId: "rpt.trial-balance.v3", manifestPath: "reports/manifests/trial-balance.json",
    provenance: "General ledger export → close snapshot", distJob: "dist-queue.reports.std.daily",
  },
  {
    id: "balance-sheet", name: "Balance Sheet", category: "Financial Statements",
    produces: "Statement of financial position — assets, liabilities, and net assets as of period end, with prior-period comparative.",
    requiredData: ["General ledger", "Chart of accounts", "Prior-period close"],
    lastRun: "2026-06-30 21:06:00Z", owner: "Controller — J. Alvarez",
    format: ["PDF", "XLSX"], status: "ready",
    templateId: "rpt.balance-sheet.v4", manifestPath: "reports/manifests/balance-sheet.json",
    provenance: "GL export → close snapshot → statement engine", distJob: "dist-queue.reports.std.daily",
  },
  {
    id: "income-statement", name: "Income Statement", category: "Financial Statements",
    produces: "Revenue, expenses, and realized/unrealized gains for the period, rolled to net income.",
    requiredData: ["General ledger", "Trade blotter"],
    lastRun: "2026-06-30 21:06:00Z", owner: "Controller — J. Alvarez",
    format: ["PDF", "XLSX"], status: "ready",
    templateId: "rpt.income-statement.v4", manifestPath: "reports/manifests/income-statement.json",
    provenance: "GL export → close snapshot → statement engine", distJob: "dist-queue.reports.std.daily",
  },
  {
    id: "cash-activity", name: "Cash Activity", category: "Financial Statements",
    produces: "Cash receipts, disbursements, and transfers across custodial accounts for the period.",
    requiredData: ["Bank feed", "General ledger"],
    lastRun: "2026-06-29 06:00:00Z", owner: "Treasury — M. Okafor",
    format: ["PDF", "CSV"], status: "ready",
    templateId: "rpt.cash-activity.v2", manifestPath: "reports/manifests/cash-activity.json",
    provenance: "Bank feed sync → GL cash accounts", distJob: "dist-queue.reports.std.daily",
  },
  {
    id: "investor-statement", name: "Investor Statement", category: "Investor Reporting",
    produces: "Investor-facing summary — NAV, performance, and capital activity for the reporting period.",
    requiredData: ["Capital account statement", "NAV pack"],
    lastRun: "2026-05-31 22:00:00Z", owner: "Investor Relations — S. Patel",
    format: ["PDF"], status: "review",
    statusDetail: "Needs review · NAV pack for 2026-06 not yet published",
    templateId: "rpt.investor-statement.v5", manifestPath: "reports/manifests/investor-statement.json",
    provenance: "Capital account statement → NAV pack → statement engine", distJob: "dist-queue.reports.investor.monthly",
  },
  {
    id: "capital-account-statement", name: "Capital Account Statement", category: "Investor Reporting",
    produces: "Per-investor capital roll-forward — contributions, distributions, allocated P&L, ending capital.",
    requiredData: ["Investor ledger", "Allocation run", "Capital calls"],
    lastRun: "2026-06-28 18:20:00Z", owner: "Investor Relations — S. Patel",
    format: ["PDF"], status: "review",
    statusDetail: "Needs review · Q2 allocation pending sign-off",
    templateId: "rpt.capital-account-statement.v3", manifestPath: "reports/manifests/capital-account-statement.json",
    provenance: "Investor ledger → allocation run → capital calls", distJob: "dist-queue.reports.investor.monthly",
  },
  {
    id: "reconciliation-summary", name: "Reconciliation Summary", category: "Reconciliation",
    produces: "Cross-source tie-out — book vs. custodian vs. bank — with each open break and its age.",
    requiredData: ["General ledger", "Custodian feed", "Bank feed"],
    lastRun: "2026-06-30 05:00:00Z", owner: "Ops — D. Chen",
    format: ["PDF", "XLSX"], status: "ready",
    templateId: "rpt.reconciliation-summary.v2", manifestPath: "reports/manifests/reconciliation-summary.json",
    provenance: "GL export → custodian feed diff → bank feed diff", distJob: "dist-queue.reports.ops.daily",
  },
  {
    id: "exception-report", name: "Exception Report", category: "Exceptions",
    produces: "Open data-quality, reconciliation, and posting exceptions across all workstations, by severity.",
    requiredData: ["Reconciliation breaks", "Data quality scans", "Unposted entries"],
    lastRun: "2026-06-30 21:10:00Z", owner: "Ops — D. Chen",
    format: ["PDF", "CSV"], status: "ready",
    templateId: "rpt.exception-report.v3", manifestPath: "reports/manifests/exception-report.json",
    provenance: "Reconciliation breaks → data quality scans → unposted entries", distJob: "dist-queue.reports.ops.daily",
  },
  {
    id: "evidence-binder", name: "Evidence Binder", category: "Audit",
    produces: "Compiled evidence package for a control or period — source documents, approvals, and reconciliation proof, indexed.",
    requiredData: ["Evidence links", "Approval trail", "Reconciliation summary"],
    lastRun: "2026-06-15 14:00:00Z", owner: "Audit — L. Novak",
    format: ["PDF"], status: "blocked",
    statusDetail: "Blocked · 2 reconciliations missing signed attestations",
    templateId: "rpt.evidence-binder.v2", manifestPath: "reports/manifests/evidence-binder.json",
    provenance: "Evidence links → approval trail → reconciliation summary", distJob: "dist-queue.reports.audit.ondemand",
  },
];

const STATUS_LABEL = { ready: "Ready", review: "Needs Review", blocked: "Blocked" };
const STATUS_DETAIL_DEFAULT = { ready: "Ready to run", review: "Needs review", blocked: "Blocked" };
const STATUS_COLOR = {
  ready: "var(--green-dim, #10663F)",
  review: "var(--accent, #2F6F8F)",
  blocked: "var(--red-dim, #8C2F40)",
};

/* ───────────────────────── row detail (hides advanced items) ───────────────────────── */
function ReportDetail({ report }) {
  return (
    <div className="rl-detail-grid">
      <div>
        <div className="rl-detail-label">Produces</div>
        <p className="rl-detail-text">{report.produces}</p>
        <div className="rl-detail-label">Requires</div>
        <div style={{ display: "flex", flexWrap: "wrap", gap: 6, marginBottom: 12 }}>
          {report.requiredData.map((d) => <span key={d} className="rl-chip">{d}</span>)}
        </div>
      </div>
      <div>
        <EntitySummary columns={1} items={[
          { label: "Owner", value: report.owner, mono: false },
          { label: "Formats", value: report.format.join(" · ") },
          { label: "Readiness", value: report.statusDetail || STATUS_DETAIL_DEFAULT[report.status], color: STATUS_COLOR[report.status], mono: false },
        ]} />
      </div>
      <div style={{ gridColumn: "1 / -1" }}>
        <Accordion items={[{
          title: "Technical details",
          content: (
            <EntitySummary columns={2} items={[
              { label: "Template ID", value: report.templateId },
              { label: "Manifest path", value: report.manifestPath },
              { label: "Provenance", value: report.provenance, mono: false },
              { label: "Distribution job", value: report.distJob },
            ]} />
          ),
        }]} />
      </div>
    </div>
  );
}

/* ───────────────────────── set-parameters dialog ───────────────────────── */
function ParamsDialog({ report, period, onClose, onRun }) {
  const [selPeriod, setSelPeriod] = useState(period);
  const [formats, setFormats] = useState(() => Object.fromEntries((report?.format || []).map((f) => [f, true])));

  if (!report) return null;
  const blocked = report.status === "blocked";
  const anyFormat = Object.values(formats).some(Boolean);

  return (
    <Dialog open={!!report} onClose={onClose} title={`Set parameters — ${report.name}`} maxWidth="480px">
      <DialogBody>
        {report.status !== "ready" && (
          <div style={{ marginBottom: 14 }}>
            <Callout tone={blocked ? "danger" : "warning"} title={STATUS_LABEL[report.status]}>
              {report.statusDetail || STATUS_DETAIL_DEFAULT[report.status]}
            </Callout>
          </div>
        )}
        <div style={{ marginBottom: 14 }}>
          <Select label="Period" value={selPeriod} onChange={setSelPeriod}
            options={PERIODS.map((p) => ({ value: p, label: p }))} />
        </div>
        <div>
          <div className="rl-detail-label" style={{ marginBottom: 8 }}>Output format</div>
          <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
            {report.format.map((f) => (
              <Checkbox key={f} label={f} checked={!!formats[f]}
                onChange={(v) => setFormats((prev) => ({ ...prev, [f]: v }))} />
            ))}
          </div>
        </div>
      </DialogBody>
      <DialogFooter>
        <Button variant="ghost" size="sm" onClick={onClose}>Cancel</Button>
        <Button variant="primary" size="sm" disabled={blocked || !anyFormat}
          onClick={() => onRun(report.id, selPeriod)}>
          Run report
        </Button>
      </DialogFooter>
    </Dialog>
  );
}

/* ───────────────────────── right rail ───────────────────────── */
function RightRail({ reports, onJump }) {
  const counts = { ready: 0, review: 0, blocked: 0 };
  reports.forEach((r) => { counts[r.status] = (counts[r.status] || 0) + 1; });
  const attention = reports.filter((r) => r.status !== "ready");
  const recent = [...reports].filter((r) => r.lastRun).sort((a, b) => (a.lastRun < b.lastRun ? 1 : -1)).slice(0, 5);

  return (
    <div style={{ width: 280, flexShrink: 0, display: "flex", flexDirection: "column", gap: 12 }}>
      <PanelSurface style={{ padding: 14 }}>
        <Eyebrow style={{ marginBottom: 10 }}>Readiness</Eyebrow>
        <div className="rl-rail-row">
          <SeverityBadge status="ready" label="Ready" />
          <span className="rl-rail-time">{counts.ready}</span>
        </div>
        <div className="rl-rail-row">
          <SeverityBadge status="review" label="Needs Review" />
          <span className="rl-rail-time">{counts.review}</span>
        </div>
        <div className="rl-rail-row">
          <SeverityBadge status="blocked" label="Blocked" />
          <span className="rl-rail-time">{counts.blocked}</span>
        </div>
        {attention.length > 0 && (
          <div style={{ display: "flex", flexDirection: "column", gap: 6, marginTop: 12 }}>
            {attention.map((r) => (
              <EvidenceLink key={r.id} label={r.name} status={r.status} route={r.category} onOpen={() => onJump(r.name)} />
            ))}
          </div>
        )}
      </PanelSurface>

      <PanelSurface style={{ padding: 14 }}>
        <Eyebrow style={{ marginBottom: 10 }}>Recent runs</Eyebrow>
        {recent.length === 0 ? (
          <div style={{ fontSize: 12, color: "var(--text-muted)" }}>No runs yet.</div>
        ) : recent.map((r) => (
          <div key={r.id} className="rl-rail-row">
            <button className="rl-rail-link" onClick={() => onJump(r.name)}>{r.name}</button>
            <span className="rl-rail-time">{r.lastRun.slice(0, 16).replace("T", " ")}</span>
          </div>
        ))}
      </PanelSurface>
    </div>
  );
}

/* ───────────────────────── app ───────────────────────── */
function ReportLibraryScreen() {
  const [reports, setReports] = useState(REPORTS);
  const [category, setCategory] = useState("all");
  const [period, setPeriod] = useState(PERIODS[0]);
  const [query, setQuery] = useState("");
  const [dialogReport, setDialogReport] = useState(null);

  const filtered = useMemo(() => {
    let list = reports;
    if (category !== "all") list = list.filter((r) => r.category === category);
    const q = query.trim().toLowerCase();
    if (q) {
      list = list.filter((r) =>
        r.name.toLowerCase().includes(q) ||
        r.produces.toLowerCase().includes(q) ||
        r.requiredData.some((d) => d.toLowerCase().includes(q))
      );
    }
    return list;
  }, [reports, category, query]);

  const runReport = (id, chosenPeriod) => {
    const now = new Date().toISOString().slice(0, 19).replace("T", " ") + "Z";
    const r = reports.find((x) => x.id === id);
    setReports((prev) => prev.map((x) => (x.id === id ? { ...x, lastRun: now } : x)));
    window.MeridianToast?.success("Report queued", `${r?.name} · ${chosenPeriod}`);
    setDialogReport(null);
  };

  const jumpTo = (name) => { setCategory("all"); setQuery(name); };

  const columns = [
    {
      key: "name", label: "Report",
      render: (r) => (
        <div>
          <div style={{ fontWeight: 600, color: "var(--text-primary)" }}>{r.name}</div>
        </div>
      ),
    },
    { key: "category", label: "Category", render: (r) => <span style={{ color: "var(--text-secondary)" }}>{r.category}</span> },
    { key: "lastRun", label: "Last run", render: (r) => r.lastRun || "Never run" },
    {
      key: "requiredData", label: "Required inputs", sortable: false,
      render: (r) => (
        <div style={{ display: "flex", flexWrap: "wrap", gap: 5, whiteSpace: "normal" }}>
          {r.requiredData.slice(0, 2).map((d) => <span key={d} className="rl-chip">{d}</span>)}
          {r.requiredData.length > 2 && <span className="rl-chip rl-chip--more">+{r.requiredData.length - 2}</span>}
        </div>
      ),
    },
    {
      key: "status", label: "Readiness", sortable: false,
      render: (r) => <SeverityBadge status={r.status} label={STATUS_LABEL[r.status]} />,
    },
    {
      key: "action", label: "", sortable: false,
      render: (r) => <Button variant="primary" size="sm" onClick={() => setDialogReport(r)}>Set parameters</Button>,
    },
  ];

  return (
    <div style={{ height: "100vh", display: "flex", flexDirection: "column", background: "var(--bg)", fontFamily: "var(--font-body)", color: "var(--text-secondary)", fontSize: 13 }}>
      <ToastProvider />
      <WorkstationTopbar moduleLabel="Reporting" environment="PAPER" clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />
      <div style={{ display: "flex", flex: 1, minHeight: 0 }}>
        <NavRail
          activeId="library"
          onSelect={() => {}}
          sections={[
            { label: "Reporting", items: [
              { id: "library", label: "Report Library", icon: "../../assets/icons/data-browser.svg", shortcut: "G R" },
            ]},
          ]}
        />
        <main style={{ flex: 1, minWidth: 0, overflowY: "auto", padding: 16, display: "flex", flexDirection: "column", gap: 12 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <h1 style={{ font: "600 22px var(--font-display)", margin: 0, color: "var(--text-primary)" }}>Report Library</h1>
            <Badge variant="paper" dot>PAPER</Badge>
          </div>

          <div style={{ display: "flex", alignItems: "flex-end", gap: 10 }}>
            <div style={{ flex: 1, minWidth: 220 }}>
              <Input label="Search" placeholder="Search reports…" value={query} onChange={(e) => setQuery(e.target.value)} />
            </div>
            <div style={{ width: 200 }}>
              <Select label="Category" value={category} onChange={setCategory}
                options={[{ value: "all", label: "All categories" }, ...CATEGORIES.map((c) => ({ value: c, label: c }))]} />
            </div>
            <div style={{ width: 180 }}>
              <Select label="Period" value={period} onChange={setPeriod}
                options={PERIODS.map((p) => ({ value: p, label: p }))} />
            </div>
          </div>

          <div style={{ display: "flex", gap: 16, flex: 1, minHeight: 0, alignItems: "flex-start" }}>
            <div style={{ flex: 1, minWidth: 0 }}>
              {filtered.length === 0 ? (
                category === "Custom" && !query ? (
                  <PanelSurface>
                    <EmptyState icon="docs" title="No custom reports in this library yet"
                      detail="Custom report configurations you save will appear here for one-click reruns." compact />
                  </PanelSurface>
                ) : (
                  <PanelSurface>
                    <EmptyState icon="search" title="No reports match" detail="Try a different search term or category." compact />
                  </PanelSurface>
                )
              ) : (
                <ExpandableDataTable
                  columns={columns}
                  rows={filtered}
                  expandable={(r) => <ReportDetail report={r} />}
                />
              )}
            </div>
            <RightRail reports={reports} onJump={jumpTo} />
          </div>
        </main>
      </div>
      <StatusBar items={[
        { status: "ok", label: "Connected", value: "General ledger · Custodian · Bank feed" },
        { label: "Period", value: period },
        { label: "Reports", value: String(reports.length), push: true },
      ]} />
      <ParamsDialog report={dialogReport} period={period} onClose={() => setDialogReport(null)} onRun={runReport} />
    </div>
  );
}

window.ReportLibraryScreen = ReportLibraryScreen;
if (typeof module !== "undefined") module.exports = { ReportLibraryScreen };
