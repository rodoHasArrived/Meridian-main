// Meridian trading-desk — template screen. Mounted by the DC via <x-import>; reads
// design-system components from the compiled bundle. The live-environment showcase:
// watchlist identity chips, depth ladder with ticket prefill, resolution + time-basis
// toolbar, order ticket with the real-capital confirm gate, and the blotter/fills tape.

const {
  WorkstationTopbar, NavRail, StatusBar, PanelSurface, Eyebrow,
  SegmentedControl, InstrumentChip, DepthLadder, TimeframeSwitcher, AsOfControl,
  OrderTicket, Blotter, FillsFeed, FreshnessIndicator, ToastProvider, HotkeysProvider,
} = window.MeridianDesignSystem_4f61be;
const { useState } = React;

const NOW = Date.UTC(2026, 6, 2, 14, 32, 8);

const WATCHLIST = [
  { symbol: "AAPL",   venue: "XNAS", assetClass: "eq",     last: 201.11 },
  { symbol: "NVDA",   venue: "XNAS", assetClass: "eq",     last: 172.44 },
  { symbol: "SPY",    venue: "ARCX", assetClass: "etf",    last: 618.72 },
  { symbol: "ESU6",   venue: "CME",  assetClass: "fut",    last: 6231.25 },
];

// Deterministic 8-level book around each instrument's last price.
const BID_SIZES = [1200, 3400, 2650, 5200, 1875, 7400, 2200, 4100];
const ASK_SIZES = [900, 2100, 3800, 1450, 6100, 2900, 1700, 5000];
function book(last, tick) {
  return {
    bids: BID_SIZES.map((size, i) => ({ price: +(last - tick * (i + 1)).toFixed(4), size })),
    asks: ASK_SIZES.map((size, i) => ({ price: +(last + tick * (i + 1)).toFixed(4), size })),
  };
}
const BOOKS = {
  AAPL: book(201.11, 0.01), NVDA: book(172.44, 0.01),
  SPY: book(618.72, 0.01),  ESU6: book(6231.25, 0.25),
};

const ORDERS = [
  { id: "ORD-1209", time: NOW - 128000,  symbol: "AAPL", side: "Buy",  qty: "400",  type: "Limit",  limit: "201.0500", filled: "0",    status: "Working" },
  { id: "ORD-1208", time: NOW - 341000,  symbol: "NVDA", side: "Sell", qty: "250",  type: "Limit",  limit: "172.6000", filled: "100",  status: "Partially filled" },
  { id: "ORD-1207", time: NOW - 1220000, symbol: "SPY",  side: "Buy",  qty: "120",  type: "Market", filled: "120",  status: "Filled" },
  { id: "ORD-1206", time: NOW - 2710000, symbol: "ESU6", side: "Sell", qty: "2",    type: "Limit",  limit: "6233.75",  filled: "0",    status: "Cancelled" },
  { id: "ORD-1205", time: NOW - 4150000, symbol: "AAPL", side: "Buy",  qty: "1000", type: "Limit",  limit: "199.8000", filled: "0",    status: "Rejected" },
];

const FILLS = [
  { id: "F-2214", time: NOW - 128000,  symbol: "NVDA", side: "Sell", qty: "100", price: "172.6000" },
  { id: "F-2213", time: NOW - 1220000, symbol: "SPY",  side: "Buy",  qty: "120", price: "618.7100" },
  { id: "F-2212", time: NOW - 1220000, symbol: "SPY",  side: "Buy",  qty: "80",  price: "618.7000" },
  { id: "F-2211", time: NOW - 5400000, symbol: "AAPL", side: "Buy",  qty: "400", price: "200.9800" },
  { id: "F-2210", time: NOW - 7300000, symbol: "ESU6", side: "Sell", qty: "1",   price: "6230.50" },
];

function TradingDeskScreen() {
  const [symbol, setSymbol] = useState("AAPL");
  const [tf, setTf] = useState("1m");
  const [tape, setTape] = useState("blotter"); // blotter | fills
  const inst = WATCHLIST.find((w) => w.symbol === symbol);
  const { bids, asks } = BOOKS[symbol];

  return (
    <React.Fragment>
      <ToastProvider />
      <HotkeysProvider bindings={[
        { keys: "j", label: "Next instrument", group: "Watchlist", action: () => setSymbol((s) => WATCHLIST[(WATCHLIST.findIndex((w) => w.symbol === s) + 1) % WATCHLIST.length].symbol) },
        { keys: "k", label: "Previous instrument", group: "Watchlist", action: () => setSymbol((s) => WATCHLIST[(WATCHLIST.findIndex((w) => w.symbol === s) + WATCHLIST.length - 1) % WATCHLIST.length].symbol) },
        { keys: "b", label: "Show blotter", group: "Tape", action: () => setTape("blotter") },
        { keys: "f", label: "Show fills", group: "Tape", action: () => setTape("fills") },
      ]} />
      <WorkstationTopbar moduleLabel="Trading Desk" environment="LIVE" clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />
      <div style={{ display: "flex", flex: 1, minHeight: 0 }}>
        <NavRail
          activeId="order-book"
          onSelect={() => {}}
          sections={[
            { label: "Operate", items: [
              { id: "dashboard", label: "Dashboard", icon: "../../assets/icons/dashboard.svg" },
              { id: "order-book", label: "Trading Desk", icon: "../../assets/icons/order-book.svg", shortcut: "G T" },
              { id: "alerting", label: "Alerting", icon: "../../assets/icons/data-quality.svg" },
            ]},
            { label: "Data", items: [
              { id: "collection", label: "Collection", icon: "../../assets/icons/collection-sessions.svg" },
            ]},
          ]}
        />
        <main style={{ flex: 1, minWidth: 0, minHeight: 0, display: "flex", flexDirection: "column", gap: 10, padding: 14 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <h1 style={{ font: "600 22px var(--font-display)", margin: 0, color: "var(--text-primary)" }}>Trading Desk</h1>
            <FreshnessIndicator source="NBBO" status="live" lastSeen={NOW - 1000} />
          </div>
          <div style={{ display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap" }}>
            <div style={{ display: "flex", gap: 6 }}>
              {WATCHLIST.map((w) => (
                <InstrumentChip key={w.symbol} symbol={w.symbol} venue={w.venue} assetClass={w.assetClass}
                  selected={w.symbol === symbol} onClick={() => setSymbol(w.symbol)} />
              ))}
            </div>
            <div style={{ marginLeft: "auto", display: "flex", alignItems: "center", gap: 10 }}>
              <TimeframeSwitcher value={tf} onChange={setTf} />
              <AsOfControl withDate={false} />
            </div>
          </div>
          <div style={{ flex: 1, minHeight: 0, overflowY: "auto" }}>
            <div style={{ display: "flex", flexWrap: "wrap", gap: 10, alignItems: "flex-start" }}>
              <PanelSurface flat style={{ flex: "0 0 auto", padding: 10 }}>
                <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
                  <Eyebrow>Depth · {symbol}</Eyebrow>
                  <DepthLadder bids={bids} asks={asks} lastPrice={inst.last}
                    priceDecimals={2} levels={8}
                    onPriceClick={(price, side) => window.MeridianToast.info("Ticket prefill", `${side === "bid" ? "BUY" : "SELL"} ${symbol} @ ${price.toFixed(2)}`)} />
                </div>
              </PanelSurface>
              <PanelSurface flat style={{ flex: "1 1 340px", minWidth: 300, padding: 10, display: "flex", flexDirection: "column", gap: 8 }}>
                <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                  <Eyebrow>Tape</Eyebrow>
                  <SegmentedControl size="sm" value={tape} onChange={setTape}
                    options={[{ value: "blotter", label: "Blotter" }, { value: "fills", label: "Fills" }]} />
                </div>
                {tape === "blotter"
                  ? <Blotter orders={ORDERS} onRowClick={() => {}} />
                  : <FillsFeed fills={FILLS} maxHeight={360} />}
              </PanelSurface>
              <div style={{ flex: "0 0 340px", maxWidth: "100%" }}>
                <OrderTicket symbol={symbol} lastPrice={inst.last} environment="live"
                  onSubmit={(o) => window.MeridianToast.success("Order submitted", `${o.side.toUpperCase()} ${o.qty} ${o.symbol} · ${o.type}${o.limitPrice ? " @ " + o.limitPrice : ""}`)} />
              </div>
            </div>
          </div>
        </main>
      </div>
      <StatusBar items={[
        { status: "ok", label: "gateway", value: "streaming" },
        { label: "Working", value: String(ORDERS.filter((o) => o.status === "Working" || o.status === "Partially filled").length) },
        { label: "Fills", value: String(FILLS.length) },
        { status: "ok", label: "Latency", value: "7ms", push: true },
      ]} />
    </React.Fragment>
  );
}

window.TradingDeskScreen = TradingDeskScreen;
if (typeof module !== "undefined") module.exports = { TradingDeskScreen };
