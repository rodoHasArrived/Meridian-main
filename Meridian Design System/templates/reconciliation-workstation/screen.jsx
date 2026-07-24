// Meridian reconciliation workstation — template screen. Mounted by the DC via <x-import>.
// Data shapes mirror Meridian.Ui.Shared: ReconciliationCaseSummaryDto (SLA block, confidence,
// rationale, sign-off), StatementImportSummaryDto, statement-run evidence, and
// ReconciliationQueueAccountStatusDto (queue state · next best action).

const {
  WorkstationTopbar, NavRail, StatusBar, PanelSurface, Eyebrow, Button, Badge, Callout,
  Tabs, TabPanel, DenseDataTable, SeverityBadge, KeyValueGrid, TrustStrip, EvidenceLink,
  CaseQueue, SlaChip, SegmentedControl
} = window.MeridianDesignSystem_4f61be;
const { useState } = React;

const NOW = "2026-07-05T14:32:00Z";

const NAV = [
  { label: "Reconciliation", items: [
    { id: "cases", label: "Break Queue", icon: "../../assets/icons/data-quality.svg", shortcut: "G Q" },
    { id: "runs", label: "Statement Runs", icon: "../../assets/icons/data-operations.svg" },
    { id: "signoff", label: "Sign-off", icon: "../../assets/icons/governance.svg" },
  ]},
  { label: "Books", items: [
    { id: "ledger", label: "Accounting", icon: "../../assets/icons/account-portfolio.svg" },
    { id: "journal", label: "Journaling", icon: "../../assets/icons/archive-health.svg" },
  ]},
];
const ROUTES = { ledger: "../accounting-workstation/index.html", journal: "../journaling-workstation/index.html" };

// ── ReconciliationCaseSummaryDto-shaped rows ─────────────────────────────────
const CASES = [
  { id: "CASE-4181", summary: "Custody cash break exceeds tolerance band", category: "QuantityMismatch",
    status: "Open", priority: "Critical", assignee: "D. Chen", openedAtUtc: "07-04 06:10Z",
    sla: { state: "Breached", dueAtUtc: "2026-07-05T09:00:00Z" },
    confidence: 0.94, rationale: "Statement shows $82,440.00 settlement; ledger has $81,940.00 — matcher found no candidate within $5 / 1 day.",
    rootCauseCode: null, importId: "IMP-0619", runId: "SRUN-1042", version: 3,
    stmt: "$82,440.00", book: "$81,940.00", diff: "$500.00" },
  { id: "CASE-4184", summary: "Dividend accrual unmatched — MSFT 06-12", category: "MissingCounterpart",
    status: "InProgress", priority: "High", assignee: null, openedAtUtc: "07-04 11:40Z",
    sla: { state: "Warning", dueAtUtc: "2026-07-05T17:00:00Z" },
    confidence: 0.71, rationale: "Broker statement carries a $1,290.00 dividend receipt with no ledger accrual in the window.",
    rootCauseCode: null, importId: "IMP-0619", runId: "SRUN-1042", version: 1,
    stmt: "$1,290.00", book: "—", diff: "$1,290.00" },
  { id: "CASE-4188", summary: "FX rate variance on EUR settlement", category: "PriceTolerance",
    status: "InProgress", priority: "Normal", assignee: "S. Patel", openedAtUtc: "07-05 08:02Z",
    sla: { state: "OnTrack", dueAtUtc: "2026-07-07T09:00:00Z" },
    confidence: 0.88, rationale: "EURUSD applied 1.0842 vs. custodian 1.0851 — €120k notional, $108 variance, above the $50 band.",
    rootCauseCode: "FX-RATE-SOURCE", importId: "IMP-0621", runId: "SRUN-1044", version: 2,
    stmt: "$130,212.00", book: "$130,104.00", diff: "$108.00" },
  { id: "CASE-4173", summary: "Statement row failed canonicalization", category: "MappingError",
    status: "Resolved", priority: "Normal", assignee: "D. Chen", openedAtUtc: "07-02 15:21Z",
    sla: { state: "OnTrack", ageBand: "2–3 days" },
    confidence: 0.99, rationale: "Unknown transaction code 'CDIVX' — mapped to Dividend after profile update (mapping profile v12 → v13).",
    rootCauseCode: "MAPPING-PROFILE", importId: "IMP-0611", runId: "SRUN-1038", version: 5,
    resolutionCode: "ProfileUpdated", stmt: "$96.40", book: "$96.40", diff: "$0.00" },
];

// ── StatementImportSummaryDto rows ───────────────────────────────────────────
const IMPORTS = [
  { importId: "IMP-0621", broker: "Northern Trust", statementDate: "2026-06-30", importedAtUtc: "07-05 05:02Z", rawRowCount: 1418, normalizedRowCount: 1412 },
  { importId: "IMP-0619", broker: "IBKR", statementDate: "2026-06-30", importedAtUtc: "07-04 05:01Z", rawRowCount: 2204, normalizedRowCount: 2204 },
  { importId: "IMP-0611", broker: "Plaid · FNB Operating", statementDate: "2026-06-30", importedAtUtc: "07-02 05:00Z", rawRowCount: 312, normalizedRowCount: 311 },
];

// ── Statement runs (evidence-link tuple fields) ──────────────────────────────
const RUNS = [
  { runId: "SRUN-1044", custodian: "Northern Trust", account: "NT-4417", period: "06-01 → 06-30",
    validation: "1,412 rows · 0 errors", match: "1,409 matched · 3 breaks", status: "BreaksDetected" },
  { runId: "SRUN-1042", custodian: "IBKR", account: "IB-9021", period: "06-01 → 06-30",
    validation: "2,204 rows · 0 errors", match: "2,202 matched · 2 breaks", status: "BreaksDetected" },
  { runId: "SRUN-1038", custodian: "Plaid · FNB", account: "FNB-0334", period: "06-01 → 06-30",
    validation: "311 rows · 1 warning", match: "311 matched · 0 breaks", status: "Matched" },
];

// ── ReconciliationQueueAccountStatusDto rows ─────────────────────────────────
const QUEUE = [
  { accountCode: "NT-4417", queueState: "BreaksDetected", unresolvedBreakCount: 2, signOffReady: false,
    nextBestAction: "Resolve CASE-4181 (breached SLA)", blockerReason: "Custody cash break above tolerance" },
  { accountCode: "IB-9021", queueState: "BreaksDetected", unresolvedBreakCount: 1, signOffReady: false,
    nextBestAction: "Assign CASE-4184", blockerReason: "Unassigned break aging toward SLA" },
  { accountCode: "FNB-0334", queueState: "Matched", unresolvedBreakCount: 0, signOffReady: true,
    nextBestAction: "Record sign-off", blockerReason: "—" },
];

function CaseDetail({ c }) {
  const canSignOff = c.status === "Resolved";
  return (
    <PanelSurface raised style={{ padding: 14, display: "flex", flexDirection: "column", gap: 10 }}>
      <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
        <Eyebrow>{c.id}</Eyebrow>
        <div style={{ flex: 1 }}></div>
        <SlaChip now={NOW} {...c.sla} />
        <SeverityBadge status={c.status} />
      </div>
      <KeyValueGrid columns={2} items={[
        { label: "Category", value: c.category },
        { label: "Confidence", value: (c.confidence * 100).toFixed(0) + "%" },
        { label: "Statement", value: c.stmt },
        { label: "Ledger", value: c.book },
        { label: "Difference", value: <span style={{ color: c.diff === "$0.00" ? "var(--green-dim)" : "var(--red-dim)" }}>{c.diff}</span> },
        { label: "Root cause", value: c.rootCauseCode || "—" },
        { label: "Opened", value: c.openedAtUtc },
        { label: "Version", value: "v" + c.version },
      ]} />
      <Callout tone={c.status === "Resolved" ? "success" : "info"} title="Matcher rationale">{c.rationale}</Callout>
      <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
        <EvidenceLink label={"Import " + c.importId} status="Ready" route={"evidence://imports/" + c.importId} href="#evidence" />
        <EvidenceLink label={"Run " + c.runId} status="Ready" route={"evidence://recon/" + c.runId} href="#evidence" />
      </div>
      <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
        <Button variant="ghost" size="sm">{c.assignee ? "Reassign" : "Assign to me"}</Button>
        <Button variant="ghost" size="sm" disabled={c.status === "Resolved"}>Resolve</Button>
        <div style={{ flex: 1 }}></div>
        <Button variant="primary" size="sm" disabled={!canSignOff}
          title={canSignOff ? "Record sign-off" : "Sign-off requires a resolution code on this case"}>Sign off</Button>
      </div>
      {!canSignOff && (
        <span style={{ fontSize: 10.5, color: "var(--text-muted)", fontFamily: "var(--font-data)" }}>
          Sign-off is gated on resolution · version v{c.version} must match server
        </span>
      )}
    </PanelSurface>
  );
}

function ReconciliationWorkstationScreen() {
  const [selId, setSelId] = useState("CASE-4181");
  const [filt, setFilt] = useState("all");
  const list = CASES.filter((c) =>
    filt === "all" ? true :
    filt === "breached" ? c.sla.state === "Breached" :
    filt === "unassigned" ? !c.assignee : c.status !== "Resolved");
  const sel = CASES.find((c) => c.id === selId) || CASES[0];
  const breached = CASES.filter((c) => c.sla.state === "Breached").length;
  const open = CASES.filter((c) => c.status !== "Resolved").length;

  return (
    <React.Fragment>
      <WorkstationTopbar moduleLabel="Reconciliation" environment="OPS" clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />
      <div style={{ display: "flex", flex: 1, minHeight: 0 }}>
        <NavRail activeId="cases" sections={NAV} onSelect={(id) => { const r = ROUTES[id]; if (r) window.location.href = r; }} />
        <main style={{ flex: 1, minWidth: 0, overflowY: "auto", padding: 16, display: "flex", flexDirection: "column", gap: 12 }}>

          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <h1 style={{ font: "600 22px var(--font-display)", margin: 0, color: "var(--text-primary)", whiteSpace: "nowrap" }}>Statement Reconciliation</h1>
            <Badge variant="neutral">June close</Badge>
            <div style={{ flex: 1 }}></div>
            <Button variant="ghost" size="sm">Run matcher</Button>
            <Button variant="primary" size="sm">Import statement</Button>
          </div>

          <TrustStrip items={[
            { label: "Imports", value: IMPORTS.length + " this cycle", state: "ready" },
            { label: "Runs", value: "1 matched / 3", state: "review" },
            { label: "Open cases", value: String(open), state: open ? "review" : "ready" },
            { label: "SLA", value: breached ? breached + " breached" : "on track", state: breached ? "blocked" : "ready" },
          ]} />

          <Tabs tabs={[{ label: "Break queue", count: open }, "Statement runs", "Sign-off queue"]}>
            <TabPanel>
              <div style={{ display: "grid", gridTemplateColumns: "1fr 380px", gap: 12, alignItems: "start", paddingTop: 10 }}>
                <div style={{ display: "flex", flexDirection: "column", gap: 8, minWidth: 0 }}>
                  <SegmentedControl size="sm" value={filt} onChange={setFilt}
                    options={[{ value: "all", label: "All" }, { value: "open", label: "Open" }, { value: "breached", label: "Breached" }, { value: "unassigned", label: "Unassigned" }]} />
                  <CaseQueue items={list} selectedId={selId} onSelect={setSelId} now={NOW} emptyLabel="No cases match this filter" />
                </div>
                <CaseDetail c={sel} />
              </div>
            </TabPanel>

            <TabPanel>
              <div style={{ display: "flex", flexDirection: "column", gap: 12, paddingTop: 10 }}>
                <PanelSurface style={{ padding: 0 }}>
                  <DenseDataTable
                    columns={[
                      { key: "runId", label: "Run" },
                      { key: "custodian", label: "Custodian" },
                      { key: "account", label: "Account" },
                      { key: "period", label: "Period" },
                      { key: "validation", label: "Validation" },
                      { key: "match", label: "Match summary" },
                      { key: "status", label: "Status", render: (r) => <SeverityBadge status={r.status} /> },
                    ]}
                    rows={RUNS} />
                </PanelSurface>
                <PanelSurface style={{ padding: 0 }}>
                  <DenseDataTable
                    columns={[
                      { key: "importId", label: "Import" },
                      { key: "broker", label: "Broker / source" },
                      { key: "statementDate", label: "Statement date" },
                      { key: "importedAtUtc", label: "Imported" },
                      { key: "rawRowCount", label: "Raw rows", align: "right" },
                      { key: "normalizedRowCount", label: "Normalized", align: "right",
                        render: (r) => <span style={{ color: r.normalizedRowCount < r.rawRowCount ? "var(--severity-action-fg)" : "inherit" }}>{r.normalizedRowCount.toLocaleString()}</span> },
                    ]}
                    rows={IMPORTS} />
                </PanelSurface>
              </div>
            </TabPanel>

            <TabPanel>
              <div style={{ paddingTop: 10 }}>
                <PanelSurface style={{ padding: 0 }}>
                  <DenseDataTable
                    columns={[
                      { key: "accountCode", label: "Account" },
                      { key: "queueState", label: "Queue state", render: (r) => <SeverityBadge status={r.queueState} /> },
                      { key: "unresolvedBreakCount", label: "Unresolved", align: "right" },
                      { key: "signOffReady", label: "Sign-off", render: (r) => <Badge variant={r.signOffReady ? "success" : "warning"}>{r.signOffReady ? "Ready" : "Blocked"}</Badge> },
                      { key: "nextBestAction", label: "Next best action" },
                      { key: "blockerReason", label: "Blocker" },
                    ]}
                    rows={QUEUE} />
                </PanelSurface>
              </div>
            </TabPanel>
          </Tabs>

        </main>
      </div>
      <StatusBar items={[
        { status: breached ? "err" : "ok", label: "Cases", value: open + " open · " + breached + " breached" },
        { label: "Selected", value: sel.id },
        { label: "Cycle", value: "2026-06 close" },
        { status: "ok", label: "Matcher", value: "idle · last run 05:02Z", push: true },
      ]} />
    </React.Fragment>
  );
}

window.ReconciliationWorkstationScreen = ReconciliationWorkstationScreen;
if (typeof module !== "undefined") module.exports = { ReconciliationWorkstationScreen };
