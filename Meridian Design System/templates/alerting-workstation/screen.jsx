// Meridian alerting-workstation — template screen. Mounted by the DC via <x-import>; reads
// design-system components from the compiled bundle. Demonstrates the full data-state ladder
// in situ (live / loading / empty / error via the toolbar switch) plus the SplitPane
// list-|-inspector pattern, DiffView audit rail, and Timestamp/Delta content primitives.

const {
  WorkstationTopbar, NavRail, StatusBar, PanelSurface, Eyebrow, Button,
  DenseDataTable, SeverityBadge, StatusBanner, EmptyState, SkeletonTable,
  SplitPane, Toolbar, ToolbarGroup, ToolbarSpacer, Input, SegmentedControl,
  Timestamp, Delta, DiffView, KeyValueGrid, FreshnessIndicator, ToastProvider,
  HotkeysProvider,
} = window.MeridianDesignSystem_4f61be;
const { useState } = React;

const NOW = Date.UTC(2026, 6, 2, 14, 32, 8);
const ALERTS = [
  { id: "ALR-2214", rule: "Drawdown breach", scope: "momentum.v4", severity: "Critical", state: "Open",  fired: NOW - 372000,  value: "-8.4%",  threshold: "-8.0%" },
  { id: "ALR-2213", rule: "Feed gap",        scope: "Polygon · XNAS", severity: "Warning",  state: "Open",  fired: NOW - 1260000, value: "42s",    threshold: "30s" },
  { id: "ALR-2212", rule: "Order reject rate", scope: "IBKR paper",  severity: "Warning",  state: "Acked", fired: NOW - 4380000, value: "3.1%",   threshold: "2.0%" },
  { id: "ALR-2211", rule: "Position limit",  scope: "SPY short",     severity: "Critical", state: "Acked", fired: NOW - 8100000, value: "-200",   threshold: "-150" },
  { id: "ALR-2210", rule: "Stale mark",      scope: "TLT",           severity: "Info",     state: "Closed", fired: NOW - 21600000, value: "6m",    threshold: "5m" },
  { id: "ALR-2209", rule: "Backfill overrun", scope: "Databento · daily", severity: "Info", state: "Closed", fired: NOW - 43200000, value: "18m",  threshold: "15m" },
];

const RULE_DIFF = [
  { field: "Threshold", before: "-5.0%", after: "-8.0%" },
  { field: "Window", before: "5m", after: "15m" },
  { field: "Channels", before: "email", after: "email · pager" },
  { field: "Owner", before: "r.alvarez", after: "r.alvarez" },
];

function AlertingWorkstationScreen() {
  const [dataState, setDataState] = useState("live"); // live | loading | empty | error
  const [selected, setSelected] = useState(0);
  const [query, setQuery] = useState("");
  const rows = dataState === "live"
    ? ALERTS.filter((a) => !query || (a.rule + a.scope + a.id).toLowerCase().includes(query.toLowerCase()))
    : [];
  const row = rows[selected] || rows[0];

  const table = () => {
    if (dataState === "loading") return <PanelSurface flat style={{ padding: 12 }}><SkeletonTable rows={6} columns={6} /></PanelSurface>;
    if (dataState === "empty") return (
      <PanelSurface flat style={{ padding: 24 }}>
        <EmptyState icon="inbox" title="No open alerts" detail="All rules are quiet. New alerts appear here the moment a rule fires." action="Review alert rules" onAction={() => {}} />
      </PanelSurface>
    );
    if (dataState === "error") return (
      <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
        <StatusBanner tone="danger" title="Alert stream disconnected" detail="alerts-svc unreachable since 14:29:41Z — showing nothing rather than stale state" />
        <Button variant="ghost" size="sm" onClick={() => setDataState("live")} style={{ alignSelf: "flex-start" }}>Retry connection</Button>
      </div>
    );
    if (rows.length === 0) return (
      <PanelSurface flat style={{ padding: 24 }}>
        <EmptyState icon="search" title="No alerts match" detail={`Nothing matches "${query}".`} action="Clear search" onAction={() => setQuery("")} compact />
      </PanelSurface>
    );
    return (
      <DenseDataTable
        selectedIndex={selected}
        onRowClick={(_, i) => setSelected(i)}
        columns={[
          { key: "id", label: "Alert" },
          { key: "severity", label: "Severity", render: (r) => <SeverityBadge status={r.severity} /> },
          { key: "rule", label: "Rule" },
          { key: "scope", label: "Scope" },
          { key: "state", label: "State", render: (r) => <SeverityBadge status={r.state === "Open" ? "NeedsAttention" : r.state === "Acked" ? "InReview" : "Complete"} label={r.state} /> },
          { key: "fired", label: "Fired", align: "right", render: (r) => <Timestamp value={r.fired} format="relative" /> },
        ]}
        rows={rows}
      />
    );
  };

  return (
    <React.Fragment>
      <ToastProvider />
      <HotkeysProvider bindings={[
        { keys: "a", label: "Acknowledge selected alert", group: "Alerts", action: () => row && window.MeridianToast.success("Alert acknowledged", `${row.id} · ${row.rule}`) },
        { keys: "s", label: "Silence selected alert 1h", group: "Alerts", action: () => row && window.MeridianToast.info("Silenced 1h", `${row.id}`) },
        { keys: "j", label: "Next alert", group: "Navigate", action: () => setSelected((i) => Math.min(rows.length - 1, i + 1)) },
        { keys: "k", label: "Previous alert", group: "Navigate", action: () => setSelected((i) => Math.max(0, i - 1)) },
      ]} />
      <WorkstationTopbar moduleLabel="Alerting" environment="PAPER" clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />
      <div style={{ display: "flex", flex: 1, minHeight: 0 }}>
        <NavRail
          activeId="alerting"
          onSelect={() => {}}
          sections={[
            { label: "Operate", items: [
              { id: "dashboard", label: "Dashboard", icon: "../../assets/icons/dashboard.svg" },
              { id: "alerting", label: "Alerting", icon: "../../assets/icons/data-quality.svg", shortcut: "G A" },
              { id: "order-book", label: "Order Book", icon: "../../assets/icons/order-book.svg" },
            ]},
            { label: "Data", items: [
              { id: "data-quality", label: "Data Quality", icon: "../../assets/icons/data-quality.svg" },
              { id: "collection", label: "Collection", icon: "../../assets/icons/collection-sessions.svg" },
            ]},
          ]}
        />
        <main style={{ flex: 1, minWidth: 0, minHeight: 0, display: "flex", flexDirection: "column", gap: 10, padding: 14 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <h1 style={{ font: "600 22px var(--font-display)", margin: 0, color: "var(--text-primary)" }}>Alerts</h1>
            <SeverityBadge status="NeedsAttention" label={`${ALERTS.filter((a) => a.state === "Open").length} open`} />
            <FreshnessIndicator source="alerts-svc" status={dataState === "error" ? "offline" : "live"} lastSeen={NOW - 4000} />
          </div>
          <Toolbar>
            <ToolbarGroup style={{ flex: 1, maxWidth: 380 }}>
              <Input placeholder="Search alerts…" value={query} onChange={(e) => setQuery(e.target.value)} />
            </ToolbarGroup>
            <ToolbarSpacer />
            <ToolbarGroup>
              <Eyebrow>Data state</Eyebrow>
              <SegmentedControl size="sm" value={dataState} onChange={setDataState}
                options={[{ value: "live", label: "Live" }, { value: "loading", label: "Loading" }, { value: "empty", label: "Empty" }, { value: "error", label: "Error" }]} />
            </ToolbarGroup>
          </Toolbar>
          <div style={{ flex: 1, minHeight: 0 }}>
            <SplitPane direction="horizontal" primary="end" initial={340} min={260} max={480} persistKey="alerting-inspector">
              <div style={{ paddingRight: 10, height: "100%", overflowY: "auto" }}>{table()}</div>
              <PanelSurface raised style={{ padding: 14, height: "100%", boxSizing: "border-box", overflowY: "auto", display: "flex", flexDirection: "column", gap: 12 }}>
                {row ? (
                  <React.Fragment>
                    <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                      <Eyebrow>{row.id}</Eyebrow>
                      <SeverityBadge status={row.severity} />
                    </div>
                    <KeyValueGrid columns={2} items={[
                      { label: "Rule", value: row.rule },
                      { label: "Scope", value: row.scope },
                      { label: "Observed", value: <Delta value={parseFloat(row.value)} suffix={row.value.replace(/^[-+0-9.]+/, "")} tone={row.severity === "Critical" ? "down" : "flat"} /> },
                      { label: "Threshold", value: row.threshold },
                      { label: "Fired", value: <Timestamp value={row.fired} format="time" /> },
                      { label: "State", value: row.state },
                    ]} />
                    <div style={{ display: "flex", gap: 8 }}>
                      <Button variant="primary" size="sm" onClick={() => window.MeridianToast.success("Alert acknowledged", `${row.id} · ${row.rule}`)}>Acknowledge</Button>
                      <Button variant="ghost" size="sm" onClick={() => window.MeridianToast.info("Silenced 1h", `${row.id} suppressed until 15:32:08Z`)}>Silence 1h</Button>
                    </div>
                    <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
                      <Eyebrow>Rule revision · pending approval</Eyebrow>
                      <DiffView changes={RULE_DIFF} />
                    </div>
                  </React.Fragment>
                ) : (
                  <EmptyState icon="inbox" title="No alert selected" detail="Select an alert row to inspect it." compact />
                )}
              </PanelSurface>
            </SplitPane>
          </div>
        </main>
      </div>
      <StatusBar items={[
        { status: dataState === "error" ? "err" : "ok", label: "alerts-svc", value: dataState === "error" ? "disconnected" : "streaming" },
        { label: "Rules", value: "24 active" },
        { label: "Open", value: String(ALERTS.filter((a) => a.state === "Open").length) },
        { status: "ok", label: "Latency", value: "9ms", push: true },
      ]} />
    </React.Fragment>
  );
}

window.AlertingWorkstationScreen = AlertingWorkstationScreen;
if (typeof module !== "undefined") module.exports = { AlertingWorkstationScreen };
