# Interactive Brokers and Alpaca Capability Expansion Review

**Status:** supporting review
**Owner:** core-team
**Reviewed:** 2026-07-24

## Purpose and review method

This review compares Meridian's implemented provider surfaces with the published Interactive
Brokers TWS API and Alpaca Trading/Market Data APIs. It is a prioritised implementation
backlog, not a claim that an upstream feature is entitled, enabled, or safe for live use. A
provider response, account permission, market-data subscription, and the existing Meridian
readiness gates remain the authority for a specific operator session.

The source review covered the adapter implementations, provider-capability catalog, shared
brokerage contract, provider read-model projection, and the two current operator runbooks. Vendor
facts were checked against the official documentation linked in the **External references**
section. Those pages describe vendor capability, not Meridian support.

## Current Meridian baseline

Meridian already has a stronger base than a basic order adapter:

| Surface | Alpaca already implemented | Interactive Brokers already implemented |
| --- | --- | --- |
| Market data | Asset-routed equities, options, crypto, and news sockets; historical stock bars, quotes, trades, and auctions; options-chain lookup; asset search; corporate-action ingestion. | Streaming trades, quotes, and depth; historical bars; and a request-correlated rich-data service for scanners, contract details, option definitions, news, fundamentals, dividends/earnings, tick-by-tick data, real-time bars, historical ticks, account P&L, market rules, and depth-exchange metadata. |
| Brokerage | Account/position/open-order/activity sync, a trade-update WebSocket with reconnect reconciliation, fractional and extended-hours orders, advanced equity orders, multi-leg options, and fixed-income validation. | Account, portfolio, activity, position, open-order, and execution-report sync through the TWS/Gateway socket. The vendor runtime is deliberately opt-in and cannot advertise live routing when the official SDK is absent. |
| Evidence and control | Asset-stream routing fails closed by entitlement; operator status retains selected feed/entitlement; execution uses the shared governed gateway. | Rich data carries request lineage including connection identity, availability, exchange, market rules, subscription descriptor, correlation, and de-duplication evidence; Flex remains the reconciliation backstop. |

The important conclusion is that the next investment should **complete and expose existing
capabilities before adding a third vendor-specific UI or bypass path**. `IProviderDataReadService`
and the shared brokerage/provider read models are the correct seams for shared browser and WPF
operator workflows.

## Highest-value common investments

| Priority | Expansion | Why it is valuable | Required Meridian guardrails |
| --- | --- | --- | --- |
| P0 | Finish live order-feedback evidence and reconciliation for every broker stream. | Live order, fill, partial-fill, cancel, reject, and disconnect recovery must update the OMS and durable trade-fill handoff deterministically. Alpaca is already the active W9 delivery item; IB should receive the same replay/duplicate/out-of-order test matrix. | Preserve current order-placement/readiness gates; do not infer a fill from an acknowledgement; retain broker execution ID, fill ID, account, timestamps, and evidence reference. |
| P0 | Provider capability/entitlement read model. | Operators need one projection that tells them whether a requested feed is live, delayed, frozen, absent, or only available in paper mode before they use it. | Project vendor evidence through ProviderSdk; do not turn a successful request into an entitlement claim. |
| P1 | Batch snapshots and market-session context. | Latest trade/quote/bar snapshots plus clock/calendar state improve watchlists, valuation freshness, order validation, and market-open explanations while avoiding a socket for every short-lived screen. | Tag source/receipt time and feed; cache with explicit freshness; fail closed for live action when the market/session evidence is stale. |
| P1 | A shared broker-account activity/statement model. | A normalized activity timeline for fills, fees, interest, dividends, transfers, corporate actions, and cash movements reduces reconciliation gaps and can serve both Trading and Accounting. | Keep the raw provider payload/evidence pointer, deterministic de-duplication, and source-specific transaction type; never turn a read-only import into a posting command. |
| P2 | Capability-driven UI actions. | Buttons such as “show depth,” “trade options,” or “load corporate actions” should only appear when the current provider and entitlement expose that exact capability. | Extend the capability descriptor/read model rather than hard-coding Alpaca/IBKR flags in browser or WPF views. |

## Alpaca opportunities

### What should be expanded first

1. **Complete the market-data event vocabulary (P1).** The stock stream currently subscribes to
   trades and quotes, while the asset router already makes options, crypto, and news independently
   connectable. Add normalized subscriptions and collectors for bars/updated bars, trade
   corrections/cancels, LULD indications where entitled, and crypto order-book updates. Add the
   corresponding historical/batch snapshot paths for options, crypto, and news. This gives the
   data-quality workflow better stale-price and correction evidence without presenting quote data
   as depth.
2. **Create read-only Trading API account context (P1).** Add provider-neutral adapters for
   account configuration, clock, calendar, assets (including tradability/status), and watchlists.
   Use them in the existing shared workstation services for market-hours warnings, symbol
   eligibility, and saved research/trading lists. These are read-model features; no account setting
   mutation should be exposed in the first release.
3. **Promote current order support into declared capabilities (P1).** The gateway already maps
   bracket/OCO/OTO, trailing stop, multi-leg options, fixed income, notional, and extended-hours
   constraints. Its public `BrokerageCapabilities` should accurately declare all supported order
   types, asset classes, order classes, and their conditional restrictions. This prevents a
   capability matrix/UI from understating or incorrectly offering the gateway's behavior.
4. **Broaden corporate-action coverage (P1).** The mapper understands merger and spin-off action
   shapes, but the fetch currently requests only dividends and splits. Add the vendor-supported
   announcement types after fixture-based mapping and then reconcile them against account activity
   snapshots. Preserve the original announcement identifier and revision/announcement time for
   idempotency and restatement handling.
5. **Add bulk latest/snapshot clients (P2).** A batching, rate-aware read service for the latest
   trade, quote, bar, and per-symbol snapshot endpoints would serve transient watchlists and
   valuation refresh more efficiently than opening streams. It must record feed and entitlement
   alongside each value and share the current rate-limit diagnostics.

### Deferred Alpaca scope

Alpaca's separate Broker API can support brokerage-platform workflows such as end-customer account
opening, transfers, funding, documents, and KYC-related state. That is a materially different
tenant, identity, custody, and compliance boundary from Meridian's current Trading API adapter.
Do not reuse the existing trading credentials or `AlpacaBrokerageGateway` for it. Treat it as a
separate, explicitly approved product/architecture initiative with credential-vault isolation,
actor authorization, immutable audit evidence, and legal/compliance review.

## Interactive Brokers opportunities

### What should be expanded first

1. **Finish the rich-data service's route into shared operator surfaces (P1).** `IBDataServices`
   has the right bounded, provenance-bearing read-model seam but should be registered and exposed
   consistently through the shared provider-data projection for both workstations. Deliver scanner,
   option-definition, contract-detail, market-rule, P&L, and news views as request-correlated
   panels, with cancellation and an explicit “availability unknown/delayed” state.
2. **Account and model-account P&L (P1).** Add `PnLSingle`, account-update/multi-account
   subscriptions, and a normalized portfolio/account-margin snapshot. This is the highest-value
   extension for the Portfolio and Risk workspaces because it makes account/model-account
   attribution and unrealized/realized P&L observable intraday. Capture the account/model ID,
   conId, currency, source time, and connection identity.
3. **Contract qualification and market-rule pre-trade validation (P1).** Turn returned contract
   details, option-chain definitions, and market-rule increment tables into a cache with explicit
   expiry and provenance. Feed a qualified contract identifier and current minimum price increment
   into the governed order-validation path before an IB order is submitted. This reduces symbol,
   exchange, and invalid-tick rejections without constructing contracts in UI code.
4. **Depth and tick-by-tick operator workflow (P2).** The IB connection manager and callback
   router already support depth, and the rich-data service records depth-exchange metadata and
   tick-by-tick observations. Add a capacity-aware subscription manager that enforces IB pacing,
   market-data-line limits, cancellation, and automatic resubscription, then expose depth only
   with the reported exchange/SmartDepth/availability evidence.
5. **Order-expression roadmap (P2/P3).** The current gateway declares a deliberately small order
   contract (market/limit/stop/stop-limit plus basic TIF). Model IB-only conditional, bracket,
   OCA, scale, algorithmic, and combo orders as a versioned *safe order-intent extension*, not as
   untyped metadata. Each intent needs a provider validation rule, preview, audit payload, and
   paper-first scenario tests. Do not make every TWS order field generally available just because
   the upstream API accepts it.
6. **Execution economics and reconciliation (P2).** Capture commission reports and execution
   corrections alongside execution details, then reconcile them to the existing Flex import.
   This closes the gap between intraday execution status and accounting-grade fees, interest, FX,
   and corporate-action evidence without replacing Flex as the controlled backstop.

### Explicitly preserve IB runtime posture

All IB additions must retain the existing official-SDK opt-in build and paper-first behavior. A
guidance or smoke build is simulation-only. Rich-data requests must remain entitlement-aware, and
the workstation must display the actual live/frozen/delayed classification reported by TWS/Gateway.

## Recommended delivery sequence

1. **Foundation (P0):** capability/entitlement projection, order-stream recovery test matrix, and
   evidence fields shared by both providers.
2. **Alpaca data and account context (P1):** event vocabulary, snapshots, calendar/clock/assets,
   declared gateway capabilities, and expanded corporate-action fixtures.
3. **IB portfolio quality (P1):** shared rich-data projection, PnL/account-model updates, contract
   qualification, and market-rule validation.
4. **Depth and advanced execution (P2):** controlled depth/tick subscriptions, commission
   reconciliation, and a separately approved typed IB order-intent design.
5. **Broker-platform decision (P3):** decide whether Alpaca Broker API is in product scope before
   building any onboarding, transfer, or KYC surface.

Each increment should add recorded vendor fixtures plus unit tests for capability denial,
entitlement changes, cancellation, reconnect/replay, duplicate/out-of-order events, and source
provenance. Live credentials are not a unit-test substitute; the existing protected paper/provider
validation lanes remain the integration evidence.

## External references

- Alpaca: [Trading API](https://docs.alpaca.markets/docs/trading-api),
  [Options trading overview](https://docs.alpaca.markets/us/docs/options-trading-overview),
  [market-data overview](https://docs.alpaca.markets/us/docs/about-market-data-api),
  [streaming market data](https://docs.alpaca.markets/us/docs/streaming-market-data), and
  [mandatory corporate actions](https://docs.alpaca.markets/us/docs/mandatory-corporate-actions).
- Interactive Brokers: [basic contracts](https://interactivebrokers.github.io/tws-api/basic_contracts.html),
  [placing orders](https://interactivebrokers.github.io/tws-api/order_submission.html),
  [streaming market data](https://interactivebrokers.github.io/tws-api/market_data.html),
  [historical data](https://interactivebrokers.github.io/tws-api/historical_data.html),
  [market depth](https://interactivebrokers.github.io/tws-api/market_depth.html), and
  [P&amp;L](https://interactivebrokers.github.io/tws-api/pnl.html).

## Meridian evidence

- `src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaProviderModule.cs`
- `src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaBrokerageGateway.cs`
- `src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaAssetStreamAdapters.cs`
- `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBDataServices.cs`
- `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBBrokerageGateway.cs`
- `src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs`
- `src/Meridian.ProviderSdk/IProviderDataReadService.cs`
- `docs/reference/interactive-brokers-api-compatibility.md`
- `docs/roadmap/data/roadmap-items.yml` (`W9-ALPACA-004`)
