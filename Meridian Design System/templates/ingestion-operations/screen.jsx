// Meridian ingestion-operations — template screen. Mounted by the DC via <x-import>; reads
// design-system components from the compiled bundle. The data-pipeline operator view: provider
// freshness strip, backfill-queue table with inline progress, symbol×session coverage heat,
// and the raw run log — the platform's "backfills and quality scans" story end to end.

const {
  WorkstationTopbar, NavRail, StatusBar, PanelSurface, Eyebrow, Button,
  DenseDataTable, ProgressBar, SeverityBadge, CoverageMatrix, LogTail,
  FreshnessIndicator, Timestamp,
} = window.MeridianDesignSystem_4f61be;
const { useState } = React;

const NOW = Date.UTC(2026, 6, 2, 14, 32, 8);

const PROVIDERS = [
  { source: "Polygon",   status: "live",    lastSeen: NOW - 2000 },
  { source: "Databento", status: "live",    lastSeen: NOW - 5000 },
  { source: "IBKR",      status: "stale",   lastSeen: NOW - 61000 },
  { source: "OPRA",      status: "delayed", lastSeen: NOW - 900000 },
];

const QUEUE = [
  { id: "backfill-7742", provider: "Polygon",   scope: "XNAS daily · 780 sym", window: "2026-06-01..06-30", progress: 100, state: "Complete",  eta: "—" },
  { id: "backfill-7743", provider: "Databento", scope: "XNAS 1m · 120 sym",    window: "2026-06-23",         progress: 64,  state: "Running",   eta: "2m 10s" },
  { id: "backfill-7744", provider: "Databento", scope: "XCME 1m · ES/NQ",      window: "2026-06-23",         progress: 38,  state: "Running",   eta: "5m 40s" },
  { id: "backfill-7745", provider: "Polygon",   scope: "XNAS daily · AAPL",    window: "2026-06-26",         progress: 0,   state: "Queued",    eta: "—" },
  { id: "backfill-7746", provider: "IBKR",      scope: "FX ticks · EURUSD",    window: "2026-06-22..06-23",  progress: 12,  state: "Stalled",   eta: "—" },
];

const STATE_BADGE = { Complete: "Complete", Running: "InReview", Queued: "NeedsAttention", Stalled: "Blocked" };
const STATE_VARIANT = { Complete: "success", Running: "accent", Queued: "accent", Stalled: "danger" };

const DAYS = ["2026-06-17","2026-06-18","2026-06-19","2026-06-22","2026-06-23","2026-06-24","2026-06-25","2026-06-26"];
const COLS = DAYS.map((d) => ({ id: d, label: d.slice(5) }));
const SYMS = ["AAPL","MSFT","NVDA","SPY","ESU6","EURUSD"];
const COV = {};
for (const s of SYMS) {
  COV[s] = {};
  for (const d of DAYS) COV[s][d] = "full";
  if (s !== "EURUSD" && s !== "ESU6") COV[s]["2026-06-19"] = { status: "partial", detail: "210/390 bars · half session" };
}
COV["NVDA"]["2026-06-23"] = { status: "gap", detail: "0/390 · provider outage" };
COV["ESU6"]["2026-06-23"] = { status: "partial", detail: "112/460 · recovering" };
COV["AAPL"]["2026-06-26"] = { status: "pending", detail: "backfill-7745 queued" };
COV["EURUSD"]["2026-06-22"] = { status: "gap", detail: "0/1440 · IBKR stalled" };

const T0 = NOW - 640000;
const LOG = [
  { ts: T0,         level: "info",  source: "fetch",  text: "backfill-7743 · Databento XNAS 1m · 120 symbols · resume token none" },
  { ts: T0 + 4200,  level: "debug", source: "fetch",  text: "batch 41/120 · 3.1s · 100 rps" },
  { ts: T0 + 9900,  level: "warn",  source: "verify", text: "AAPL 2026-06-19 short session · expected 210 bars, schedule says 390" },
  { ts: T0 + 10400, level: "info",  source: "verify", text: "calendar override applied · XNAS half-day 2026-06-19" },
  { ts: T0 + 21000, level: "error", source: "conn",   text: "IBKR ws disconnect · backfill-7746 stalled at 12% · retry backoff 30s" },
  { ts: T0 + 41000, level: "info",  source: "scan",   text: "gap scan · NVDA 2026-06-23 · 0/390 bars · provider outage window confirmed" },
];

function IngestionOperationsScreen() {
  const [picked, setPicked] = useState(null);

  return (
    <React.Fragment>
      <WorkstationTopbar moduleLabel="Ingestion" environment="PAPER" clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />
      <div style={{ display: "flex", flex: 1, minHeight: 0 }}>
        <NavRail
          activeId="collection"
          onSelect={() => {}}
          sections={[
            { label: "Operate", items: [
              { id: "dashboard", label: "Dashboard", icon: "../../assets/icons/dashboard.svg" },
              { id: "order-book", label: "Trading Desk", icon: "../../assets/icons/order-book.svg" },
            ]},
            { label: "Data", items: [
              { id: "data-quality", label: "Data Quality", icon: "../../assets/icons/data-quality.svg" },
              { id: "collection", label: "Collection", icon: "../../assets/icons/collection-sessions.svg", shortcut: "G C" },
            ]},
          ]}
        />
        <main style={{ flex: 1, minWidth: 0, minHeight: 0, display: "flex", flexDirection: "column", gap: 10, padding: 14, overflowY: "auto" }}>
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <h1 style={{ font: "600 22px var(--font-display)", margin: 0, color: "var(--text-primary)" }}>Ingestion Operations</h1>
            <SeverityBadge status="InReview" label={`${QUEUE.filter((q) => q.state === "Running").length} running`} />
            <Button variant="ghost" size="sm" style={{ marginLeft: "auto" }}>Run quality scan</Button>
          </div>

          <PanelSurface flat style={{ padding: 12 }}>
            <Eyebrow>Provider feeds</Eyebrow>
            <div style={{ display: "flex", gap: 22, flexWrap: "wrap", marginTop: 10 }}>
              {PROVIDERS.map((p) => (
                <FreshnessIndicator key={p.source} source={p.source} status={p.status} lastSeen={p.lastSeen} timeFormat="relative" />
              ))}
            </div>
          </PanelSurface>

          <PanelSurface flat style={{ padding: 12 }}>
            <Eyebrow>Backfill queue</Eyebrow>
            <div style={{ marginTop: 10 }}>
              <DenseDataTable
                rows={QUEUE}
                columns={[
                  { key: "id", label: "Job" },
                  { key: "provider", label: "Provider" },
                  { key: "scope", label: "Scope" },
                  { key: "window", label: "Window" },
                  { key: "progress", label: "Progress", render: (r) => (
                    <div style={{ minWidth: 140 }}>
                      <ProgressBar value={r.progress} showValue variant={STATE_VARIANT[r.state]} size="sm" />
                    </div>
                  )},
                  { key: "state", label: "State", render: (r) => <SeverityBadge status={STATE_BADGE[r.state]} label={r.state} /> },
                  { key: "eta", label: "ETA", align: "right" },
                ]}
              />
            </div>
          </PanelSurface>

          <div style={{ display: "grid", gridTemplateColumns: "minmax(0,1fr) minmax(0,1fr)", gap: 10, alignItems: "start" }}>
            <PanelSurface flat style={{ padding: 12, minWidth: 0 }}>
              <Eyebrow>Coverage · bars on disk</Eyebrow>
              <div style={{ marginTop: 10 }}>
                <CoverageMatrix rows={SYMS} cols={COLS} data={COV} cellSize={18}
                  onCellClick={(row, col, cell) => setPicked({ row, col, cell })} />
              </div>
              <div style={{ marginTop: 8, fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-secondary)" }}>
                {picked
                  ? <span>{picked.row.id} · {picked.col.id} — {picked.cell.detail || picked.cell.status}</span>
                  : <span style={{ color: "var(--text-muted)" }}>click a cell to open its gap-scan</span>}
              </div>
            </PanelSurface>
            <PanelSurface flat style={{ padding: 12, minWidth: 0 }}>
              <Eyebrow>Run log</Eyebrow>
              <div style={{ marginTop: 10 }}>
                <LogTail title="backfill-7743 · Databento 1m" height={230} entries={LOG} />
              </div>
            </PanelSurface>
          </div>
        </main>
      </div>
      <StatusBar items={[
        { status: "ok", label: "ingest-gw", value: "streaming" },
        { label: "Queue", value: `${QUEUE.filter((q) => q.state === "Running" || q.state === "Queued").length} active` },
        { status: "err", label: "Stalled", value: String(QUEUE.filter((q) => q.state === "Stalled").length) },
        { status: "ok", label: "Lag", value: "1.2s", push: true },
      ]} />
    </React.Fragment>
  );
}

window.IngestionOperationsScreen = IngestionOperationsScreen;
if (typeof module !== "undefined") module.exports = { IngestionOperationsScreen };
