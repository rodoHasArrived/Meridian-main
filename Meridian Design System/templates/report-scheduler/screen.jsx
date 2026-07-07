// Meridian report-scheduler — template screen. Mounted by the DC via <x-import>; reads
// design-system components from the compiled bundle. The delivery-operations view: scheduled
// report packs, their recipients, run status, and the delivery-history audit trail — closing
// the loop on "who received what, when, with what evidence".

const {
  WorkstationTopbar, NavRail, StatusBar, PanelSurface, Eyebrow, Button, Badge,
  DenseDataTable, SeverityBadge, EventTimeline, Timestamp,
} = window.MeridianDesignSystem_4f61be;
const { useState } = React;

const NOW = Date.UTC(2026, 6, 2, 14, 32, 8);

const PACKS = [
  { id: "EOD-PNL",    name: "EOD P&L Pack",        cadence: "Daily · 17:05 ET", recipients: ["desk@", "risk@", "r.alvarez", "s.kwon"], last: NOW - 74000000,  lastStatus: "Delivered", next: NOW + 12000000 },
  { id: "RISK-WK",    name: "Weekly Risk Review",  cadence: "Mon · 08:00 ET",   recipients: ["risk@", "cio@"],                          last: NOW - 340000000, lastStatus: "Delivered", next: NOW + 210000000 },
  { id: "RECON-DLY",  name: "Reconciliation Log",  cadence: "Daily · 06:30 ET", recipients: ["ops@", "accounting@"],                    last: NOW - 28000000,  lastStatus: "Partial",   next: NOW + 58000000 },
  { id: "FILL-QLY",   name: "Execution Quality",   cadence: "Daily · 18:00 ET", recipients: ["desk@", "compliance@"],                   last: NOW - 70000000,  lastStatus: "Delivered", next: NOW + 15000000 },
  { id: "DATA-HLTH",  name: "Data Health Digest",  cadence: "Daily · 07:00 ET", recipients: ["data-eng@"],                              last: NOW - 26000000,  lastStatus: "Failed",    next: NOW + 60000000 },
];

const STATUS_BADGE = { Delivered: "Complete", Partial: "NeedsAttention", Failed: "Blocked" };

const HISTORY = [
  { ts: NOW - 26000000, action: "Data Health Digest failed", actor: "reporter", severity: "danger",
    detail: "SMTP relay refused · 1 recipient · retry scheduled 07:15 ET",
    evidence: { label: "delivery-9921.log", status: "Ready", onOpen: () => {} } },
  { ts: NOW - 28000000, action: "Reconciliation Log delivered (partial)", actor: "reporter", severity: "warning",
    detail: "2 of 2 recipients · 1 attachment truncated > 25MB" },
  { ts: NOW - 70000000, action: "Execution Quality delivered", actor: "reporter", severity: "success",
    detail: "2 recipients · 1.4MB · 3.2s" },
  { ts: NOW - 74000000, action: "EOD P&L Pack delivered", actor: "reporter", severity: "success",
    detail: "4 recipients · 0.9MB",
    evidence: { label: "delivery-9903.log", status: "Ready", onOpen: () => {} } },
  { ts: NOW - 88000000, action: "Schedule edited", actor: "s.kwon", severity: "accent",
    detail: "EOD P&L Pack · 17:00 → 17:05 ET · +2 recipients" },
];

function ReportSchedulerScreen() {
  const [selected, setSelected] = useState(0);
  const pack = PACKS[selected];

  return (
    <React.Fragment>
      <WorkstationTopbar moduleLabel="Report Scheduler" environment="PAPER" clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />
      <div style={{ display: "flex", flex: 1, minHeight: 0 }}>
        <NavRail
          activeId="reports"
          onSelect={() => {}}
          sections={[
            { label: "Operate", items: [
              { id: "dashboard", label: "Dashboard", icon: "../../assets/icons/dashboard.svg" },
              { id: "reports", label: "Reports", icon: "../../assets/icons/data-quality.svg", shortcut: "G R" },
            ]},
            { label: "Data", items: [
              { id: "collection", label: "Collection", icon: "../../assets/icons/collection-sessions.svg" },
            ]},
          ]}
        />
        <main style={{ flex: 1, minWidth: 0, minHeight: 0, display: "flex", flexDirection: "column", gap: 10, padding: 14, overflowY: "auto" }}>
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <h1 style={{ font: "600 22px var(--font-display)", margin: 0, color: "var(--text-primary)" }}>Report Scheduler</h1>
            <SeverityBadge status="Blocked" label={`${PACKS.filter((p) => p.lastStatus === "Failed").length} failed`} />
            <Button variant="primary" size="sm" style={{ marginLeft: "auto" }}>New report pack</Button>
          </div>

          <PanelSurface flat style={{ padding: 12 }}>
            <Eyebrow>Scheduled packs</Eyebrow>
            <div style={{ marginTop: 10 }}>
              <DenseDataTable
                selectedIndex={selected}
                onRowClick={(_, i) => setSelected(i)}
                rows={PACKS}
                columns={[
                  { key: "name", label: "Pack" },
                  { key: "cadence", label: "Schedule" },
                  { key: "recipients", label: "Recipients", render: (r) => (
                    <div style={{ display: "flex", gap: 4, flexWrap: "wrap" }}>
                      {r.recipients.map((x) => <Badge key={x} variant="neutral">{x}</Badge>)}
                    </div>
                  )},
                  { key: "lastStatus", label: "Last run", render: (r) => <SeverityBadge status={STATUS_BADGE[r.lastStatus]} label={r.lastStatus} /> },
                  { key: "next", label: "Next run", align: "right", render: (r) => <Timestamp value={r.next} format="relative" /> },
                ]}
              />
            </div>
          </PanelSurface>

          <div style={{ display: "grid", gridTemplateColumns: "minmax(0,1fr) 360px", gap: 10, alignItems: "start" }}>
            <PanelSurface flat style={{ padding: 12, minWidth: 0 }}>
              <Eyebrow>Delivery history</Eyebrow>
              <div style={{ marginTop: 10 }}>
                <EventTimeline events={HISTORY} />
              </div>
            </PanelSurface>
            <PanelSurface raised style={{ padding: 14, display: "flex", flexDirection: "column", gap: 12 }}>
              <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                <Eyebrow>{pack.id}</Eyebrow>
                <SeverityBadge status={STATUS_BADGE[pack.lastStatus]} label={pack.lastStatus} />
              </div>
              <div style={{ font: "600 15px var(--font-display)", color: "var(--text-primary)" }}>{pack.name}</div>
              <div style={{ display: "flex", flexDirection: "column", gap: 8, fontFamily: "var(--font-data)", fontSize: 12, color: "var(--text-secondary)" }}>
                <div style={{ display: "flex", justifyContent: "space-between" }}><span style={{ color: "var(--text-muted)" }}>Schedule</span><span>{pack.cadence}</span></div>
                <div style={{ display: "flex", justifyContent: "space-between" }}><span style={{ color: "var(--text-muted)" }}>Last run</span><Timestamp value={pack.last} format="relative" /></div>
                <div style={{ display: "flex", justifyContent: "space-between" }}><span style={{ color: "var(--text-muted)" }}>Next run</span><Timestamp value={pack.next} format="relative" /></div>
              </div>
              <div>
                <div style={{ fontSize: 10, fontWeight: 600, fontVariant: "all-small-caps", letterSpacing: ".04em", color: "var(--text-muted)", marginBottom: 6 }}>Recipients</div>
                <div style={{ display: "flex", gap: 4, flexWrap: "wrap" }}>
                  {pack.recipients.map((x) => <Badge key={x} variant="neutral">{x}</Badge>)}
                </div>
              </div>
              <div style={{ display: "flex", gap: 8, marginTop: 2 }}>
                <Button variant="primary" size="sm">Run now</Button>
                <Button variant="ghost" size="sm">Edit schedule</Button>
              </div>
            </PanelSurface>
          </div>
        </main>
      </div>
      <StatusBar items={[
        { status: "ok", label: "reporter", value: "idle" },
        { label: "Packs", value: String(PACKS.length) },
        { status: "err", label: "Failed", value: String(PACKS.filter((p) => p.lastStatus === "Failed").length) },
        { status: "ok", label: "Next", value: "EOD P&L · 2h", push: true },
      ]} />
    </React.Fragment>
  );
}

window.ReportSchedulerScreen = ReportSchedulerScreen;
if (typeof module !== "undefined") module.exports = { ReportSchedulerScreen };
