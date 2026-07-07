// Meridian family-office workspace — template screen. Mounted by the DC via <x-import>.
// Data shapes mirror Meridian.Ui.Shared FamilyOfficeContracts: FamilyBalanceSheetDto,
// FamilyOwnershipGraphDto (nodes/edges), FamilyAccountSummaryDto (with the provenance
// tuple), CapitalCommitmentDto / CapitalActivityDto, FamilyOfficeReadinessDto.

const {
  WorkstationTopbar, NavRail, StatusBar, PanelSurface, Eyebrow, Button, Badge, Callout,
  DenseDataTable, SeverityBadge, KeyValueGrid, MetricCard, EvidenceLink,
  OwnershipGraph, CommitmentBar, ProvenanceChip
} = window.MeridianDesignSystem_4f61be;
const { useState } = React;

const NAV = [
  { label: "Family Office", items: [
    { id: "overview", label: "Overview", icon: "../../assets/icons/dashboard.svg", shortcut: "G O" },
    { id: "entities", label: "Entities & Ownership", icon: "../../assets/icons/aggregate-portfolio.svg" },
    { id: "commitments", label: "Private Capital", icon: "../../assets/icons/account-portfolio.svg" },
  ]},
  { label: "Operations", items: [
    { id: "recon", label: "Reconciliation", icon: "../../assets/icons/data-quality.svg" },
    { id: "reports", label: "Reports", icon: "../../assets/icons/data-export.svg" },
  ]},
];
const ROUTES = { recon: "../reconciliation-workstation/index.html", reports: "../report-library/index.html" };

// ── FamilyOwnershipGraphDto ──────────────────────────────────────────────────
const NODES = [
  { id: "hh",   label: "Whitfield Household", type: "Household" },
  { id: "gen2", label: "A. Whitfield", type: "Individual" },
  { id: "tr1",  label: "Whitfield Family Trust", type: "Trust", jurisdiction: "SD" },
  { id: "llc",  label: "Blue Harbor LLC", type: "Operating", jurisdiction: "DE", currency: "USD" },
  { id: "fund", label: "Growth Fund III LP", type: "Fund", jurisdiction: "KY", currency: "USD" },
  { id: "nt",   label: "NT Custody 4417", type: "Account", currency: "USD" },
  { id: "ib",   label: "IBKR Margin 9021", type: "Account", currency: "USD" },
];
const EDGES = [
  { from: "hh",   to: "tr1",  relationship: "Grantor", percent: 100 },
  { from: "gen2", to: "llc",  relationship: "Member",  percent: 15 },
  { from: "tr1",  to: "llc",  relationship: "Member",  percent: 85 },
  { from: "tr1",  to: "nt",   relationship: "Owner",   percent: 100 },
  { from: "llc",  to: "fund", relationship: "LP",      percent: 4.2 },
  { from: "llc",  to: "ib",   relationship: "Owner",   percent: 100 },
];
const ENTITY_FACTS = {
  hh:   { type: "Household", jurisdiction: "—", taxResidency: "US", accounts: "—", evidence: "Complete" },
  gen2: { type: "Individual", jurisdiction: "—", taxResidency: "US", accounts: "—", evidence: "Complete" },
  tr1:  { type: "Trust (irrevocable)", jurisdiction: "South Dakota", taxResidency: "US", accounts: "NT-4417", evidence: "Complete" },
  llc:  { type: "Operating LLC", jurisdiction: "Delaware", taxResidency: "US", accounts: "IB-9021", evidence: "Complete" },
  fund: { type: "PE Fund (LP interest)", jurisdiction: "Cayman", taxResidency: "—", accounts: "—", evidence: "Partial" },
  nt:   { type: "Custody account", jurisdiction: "—", taxResidency: "—", accounts: "NT-4417", evidence: "Partial" },
  ib:   { type: "Brokerage account", jurisdiction: "—", taxResidency: "—", accounts: "IB-9021", evidence: "Complete" },
};

// ── FamilyAccountSummaryDto rows (with the provenance tuple) ─────────────────
const ACCOUNTS = [
  { accountId: "NT-4417", entity: "Whitfield Family Trust", custodian: "Northern Trust", currency: "USD",
    marketValue: "$148,204,110", cash: "$3,812,400", recon: "BreaksDetected",
    prov: { sourceSystem: "Northern Trust", asOfDate: "2026-06-30", completeness: "Partial", reconciliation: "BreaksDetected", sourceDocumentId: "nt-4417-2026-06.pdf" } },
  { accountId: "IB-9021", entity: "Blue Harbor LLC", custodian: "IBKR", currency: "USD",
    marketValue: "$36,918,220", cash: "$1,204,080", recon: "Matched",
    prov: { sourceSystem: "IBKR", asOfDate: "2026-06-30", completeness: "Complete", reconciliation: "Matched" } },
  { accountId: "FNB-0334", entity: "Blue Harbor LLC", custodian: "FNB Operating", currency: "USD",
    marketValue: "$2,410,330", cash: "$2,410,330", recon: "Matched",
    prov: { sourceSystem: "Plaid", asOfDate: "2026-07-01", completeness: "Complete", reconciliation: "Matched" } },
];

// ── CapitalCommitmentDto / CapitalActivityDto ────────────────────────────────
const COMMITMENTS = [
  { label: "Growth Fund III LP", vintage: 2021, commitment: 5000000, called: 3100000, distributed: 1240000, nav: 2900000 },
  { label: "Meridian Direct Lending II", vintage: 2023, commitment: 2500000, called: 1875000, distributed: 310000, nav: 1720000 },
  { label: "Cedar Ridge Real Assets", vintage: 2025, commitment: 3000000, called: 450000 },
];
const ACTIVITY = [
  { activityId: "ACT-0921", vehicle: "Growth Fund III LP", activityType: "CapitalCall", amount: "$250,000", noticeDate: "2026-06-18", dueDate: "2026-07-10", status: "Pending" },
  { activityId: "ACT-0917", vehicle: "Cedar Ridge Real Assets", activityType: "CapitalCall", amount: "$150,000", noticeDate: "2026-06-02", dueDate: "2026-06-30", status: "Settled" },
  { activityId: "ACT-0912", vehicle: "Growth Fund III LP", activityType: "Distribution", amount: "$180,000", noticeDate: "2026-05-28", dueDate: "2026-06-15", status: "Settled" },
];

function FamilyOfficeScreen() {
  const [sel, setSel] = useState("tr1");
  const facts = ENTITY_FACTS[sel] || {};
  const selNode = NODES.find((n) => n.id === sel);

  return (
    <React.Fragment>
      <WorkstationTopbar moduleLabel="Family Office" environment="OPS" clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />
      <div style={{ display: "flex", flex: 1, minHeight: 0 }}>
        <NavRail activeId="overview" sections={NAV} onSelect={(id) => { const r = ROUTES[id]; if (r) window.location.href = r; }} />
        <main style={{ flex: 1, minWidth: 0, overflowY: "auto", padding: 16, display: "flex", flexDirection: "column", gap: 12 }}>

          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <h1 style={{ font: "600 22px var(--font-display)", margin: 0, color: "var(--text-primary)", whiteSpace: "nowrap" }}>Whitfield Family Office</h1>
            <Badge variant="neutral">USD · as-of 2026-06-30</Badge>
            <SeverityBadge status="ReviewRequired" label="Review required" />
            <div style={{ flex: 1 }}></div>
            <Button variant="ghost" size="sm">Evidence binder</Button>
            <Button variant="primary" size="sm">Record review</Button>
          </div>

          {/* FamilyOfficeReadinessDto.Blockers */}
          <Callout tone="warning" title="2 blockers before this workspace can be trusted for decisions">
            NT-4417 has 1 unresolved custody cash break · Growth Fund III Q2 valuation statement not yet captured.
          </Callout>

          {/* FamilyBalanceSheetDto */}
          <div style={{ display: "grid", gridTemplateColumns: "repeat(5, 1fr)", gap: 12 }}>
            <MetricCard label="Net worth" value="$284.2M" delta="+1.2% vs May" tone="info" />
            <MetricCard label="Total assets" value="$312.6M" />
            <MetricCard label="Liabilities" value="$28.4M" />
            <MetricCard label="Liquid assets" value="$41.8M" />
            <MetricCard label="Unfunded commitments" value="$9.6M" delta="2 open calls" tone="warning" />
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "1.2fr 1fr", gap: 12, alignItems: "start" }}>
            {/* FamilyOwnershipGraphDto */}
            <PanelSurface style={{ padding: 0, display: "flex", flexDirection: "column" }}>
              <div style={{ display: "flex", alignItems: "center", gap: 8, padding: "10px 14px", borderBottom: "1px solid var(--border)" }}>
                <Eyebrow>Ownership &amp; control</Eyebrow>
                <div style={{ flex: 1 }}></div>
                <ProvenanceChip sourceSystem="Entity registry" asOfDate="2026-06-30" completeness="Complete" reconciliation="Matched" />
              </div>
              <OwnershipGraph nodes={NODES} edges={EDGES} selectedId={sel} onSelectNode={setSel} />
              <div style={{ borderTop: "1px solid var(--border)", padding: "10px 14px", display: "flex", flexDirection: "column", gap: 8 }}>
                <Eyebrow>{selNode ? selNode.label : "Entity"}</Eyebrow>
                <KeyValueGrid columns={2} items={[
                  { label: "Type", value: facts.type },
                  { label: "Jurisdiction", value: facts.jurisdiction },
                  { label: "Tax residency", value: facts.taxResidency },
                  { label: "Accounts", value: facts.accounts },
                ]} />
                <div style={{ display: "flex", gap: 8 }}>
                  <EvidenceLink label="Formation docs" status={facts.evidence === "Complete" ? "Ready" : "Missing"}
                    route={"evidence://entities/" + sel + "/formation"} href="#evidence" />
                </div>
              </div>
            </PanelSurface>

            <div style={{ display: "flex", flexDirection: "column", gap: 12, minWidth: 0 }}>
              {/* FamilyAccountSummaryDto */}
              <PanelSurface style={{ padding: 0 }}>
                <div style={{ padding: "10px 14px", borderBottom: "1px solid var(--border)" }}><Eyebrow>Accounts</Eyebrow></div>
                <DenseDataTable
                  columns={[
                    { key: "accountId", label: "Account" },
                    { key: "entity", label: "Entity" },
                    { key: "marketValue", label: "Market value", align: "right" },
                    { key: "cash", label: "Cash", align: "right" },
                    { key: "recon", label: "Recon", render: (r) => <SeverityBadge status={r.recon} /> },
                    { key: "prov", label: "Provenance", render: (r) => <ProvenanceChip {...r.prov} /> },
                  ]}
                  rows={ACCOUNTS} />
              </PanelSurface>

              {/* CapitalCommitmentDto */}
              <PanelSurface raised style={{ padding: 14, display: "flex", flexDirection: "column", gap: 16 }}>
                <Eyebrow>Private capital commitments</Eyebrow>
                {COMMITMENTS.map((c) => <CommitmentBar key={c.label} {...c} />)}
              </PanelSurface>
            </div>
          </div>

          {/* CapitalActivityDto */}
          <PanelSurface style={{ padding: 0 }}>
            <div style={{ padding: "10px 14px", borderBottom: "1px solid var(--border)" }}><Eyebrow>Recent capital activity</Eyebrow></div>
            <DenseDataTable
              columns={[
                { key: "activityId", label: "Activity" },
                { key: "vehicle", label: "Vehicle" },
                { key: "activityType", label: "Type", render: (r) => <Badge variant={r.activityType === "Distribution" ? "success" : "warning"}>{r.activityType}</Badge> },
                { key: "amount", label: "Amount", align: "right" },
                { key: "noticeDate", label: "Notice" },
                { key: "dueDate", label: "Due" },
                { key: "status", label: "Status", render: (r) => <SeverityBadge status={r.status} /> },
              ]}
              rows={ACTIVITY} />
          </PanelSurface>

        </main>
      </div>
      <StatusBar items={[
        { status: "warn", label: "Readiness", value: "Review required · 2 blockers" },
        { label: "Entities", value: String(NODES.length) },
        { label: "Selected", value: selNode ? selNode.label : "—" },
        { status: "ok", label: "Evidence", value: "last capture 05:02Z", push: true },
      ]} />
    </React.Fragment>
  );
}

window.FamilyOfficeScreen = FamilyOfficeScreen;
if (typeof module !== "undefined") module.exports = { FamilyOfficeScreen };
