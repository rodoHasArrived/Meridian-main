# Alpaca paper sandbox validation

**Status:** supporting
**Owner:** core-team
**Reviewed:** 2026-11-10

Four opt-in test classes exercise the production Alpaca adapters against the real paper Trading
API (`paper-api.alpaca.markets`) and, for market data, the market-data host
(`data.alpaca.markets`). They exist because fixtures can only restate what an adapter already
assumes about the wire contract; the first live runs found reference and options adapters whose
requests the real API refused or whose responses they could not read.

| Class | Sends | Opt-in |
| --- | --- | --- |
| `AlpacaPaperSandboxTests` | GET account, positions, open orders | `MERIDIAN_RUN_ALPACA_PAPER_SANDBOX=1` |
| `AlpacaPaperSandboxReferenceDataTests` | GET assets, option contracts, account activity | `MERIDIAN_RUN_ALPACA_PAPER_SANDBOX=1` |
| `AlpacaPaperSandboxOrderLifecycleTests` | Additionally submits, replaces and cancels one-share paper orders and takes a one-share position that it closes again | both `MERIDIAN_RUN_ALPACA_PAPER_SANDBOX=1` and `MERIDIAN_RUN_ALPACA_PAPER_SANDBOX_ORDERS=1` |
| `AlpacaOptionsMarketDataSandboxTests` | GET option contracts on the paper host; GET the underlying's latest trade and option snapshots on the market-data host | both `MERIDIAN_RUN_ALPACA_PAPER_SANDBOX=1` and `MERIDIAN_RUN_ALPACA_MARKET_DATA=1` |

Every class uses a transport guard that is independent of configuration: HTTPS only, no
redirects, and a host/method/path allow-list. The read-only classes can send nothing but GET to
the paper host; the options market-data class may also GET option snapshots and a latest trade
from the market-data host. The
order class may additionally POST `/v2/orders` and PATCH or DELETE one order by its broker id,
for at most two shares sized by quantity; bulk cancellation (`DELETE /v2/orders`), liquidation
(`DELETE /v2/positions`), dollar-sized orders and account configuration stay refused.

## What each class establishes

`AlpacaPaperSandboxTests` drives the portfolio adapter, account-scoped synchronization, OMS
reconciliation, exposure provider and portfolio risk gate, then checks account isolation and the
restart/disconnect blocks. It never submits, changes or cancels an order.

`AlpacaPaperSandboxReferenceDataTests` checks, on the paper host with paper credentials:

- symbol search and asset details for an equity and a crypto pair (string-encoded increments);
- option contract discovery: expirations across the forward horizon and the strikes of one
  expiration (string-encoded strikes, stated expiration window);
- the activity statement connector: fetch from the account, then parse through the production
  statement path with no error issues.

`AlpacaPaperSandboxOrderLifecycleTests` drives the production order manager, brokerage gateway,
recovery journal and portfolio synchronization:

- an order manager without brokerage configuration, without a readiness gate, or outside the
  approved run refuses to route, and nothing reaches the broker;
- a resting order is submitted, found by its client id, refused when dispatched twice (locally
  and by the broker), amended into a replacement chain, and cancelled, with the order manager
  holding exactly the terms the broker holds at each step;
- a submission whose acknowledgement is dropped after the broker accepted it stays retained,
  blocks further orders, cannot be dispatched again, and is recovered by lookup with exactly one
  submission;
- a marketable order fills; the exact FILL activity, holdings, activity snapshot and retained
  order state agree; a new gateway and order manager over the same recovery journal restore the
  order without booking the fill twice or submitting anything; a closing order returns holdings
  to where the run found them.

`AlpacaOptionsMarketDataSandboxTests` checks, with the free indicative-options feed, an option
chain snapshot for one expiration: an underlying price read from the latest trade, calls and puts
classified by the right in their symbols, open interest joined from the contract listing, greeks,
and the near-the-money strike filter; then one contract's quote from the single-contract route.

Corporate actions and historical bars, trades, quotes and auctions are certified separately, with
their own adapters' checks.

The order manager classifies every Alpaca route, the paper environment included, as live routing
(`AlpacaBrokerageGateway` does not report an execution mode, so the placement gate does not treat
it as a paper gateway). The order class therefore states the production admission evidence
explicitly for the paper gateway — brokerage configuration, a run id, and a readiness gate that
approves only that one-share run — rather than bypassing the gate. Whether Alpaca paper
credentials should instead be governed by the per-broker paper order-flow flag, as an Interactive
Brokers paper session is, is an open product decision; this document records current behaviour.

## Run the checks

Use a dedicated Alpaca paper account. The read-only class certifies the ready path only when the
account has no open orders. The order class tolerates open orders it does not own (it then
reports that readiness was withheld by foreign orders instead of asserting it), but its holdings
assertions assume nothing else trades its symbol while it runs. Changing marks or balances can
make a synchronization inconsistent; the tests retry a bounded number of times and then fail with
the reported reasons.

Configure these values through the execution environment's secret settings:

- `ALPACA_KEY_ID`: paper Trading API key.
- `ALPACA_SECRET_KEY`: corresponding paper secret.
- `ALPACA_TRADING_ENVIRONMENT`: exactly `paper`.
- `MERIDIAN_RUN_ALPACA_PAPER_SANDBOX`: `1` to enable the read-only classes.
- `MERIDIAN_RUN_ALPACA_PAPER_SANDBOX_ORDERS`: `1` to additionally enable the order class.
- `MERIDIAN_RUN_ALPACA_MARKET_DATA`: `1` to additionally enable the options market-data class.

Permit HTTPS access to `paper-api.alpaca.markets:443`, and to `data.alpaca.markets:443` for the
options market-data class. None of these tests opens a WebSocket.
Credentials remain in environment variables and are not printed or persisted. Run from the
repository root with the repository's .NET 10 SDK:

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj \
  --filter 'FullyQualifiedName~AlpacaPaperSandbox|FullyQualifiedName~AlpacaOptionsMarketDataSandbox' \
  --logger 'console;verbosity=normal' \
  --logger 'trx;LogFileName=alpaca-paper-sandbox.trx'
```

Without the opt-ins, xUnit reports the tests as **skipped**. Once opted in, missing credentials,
a live environment selection, unavailable network, rejected authentication, missing broker
evidence, or unexplained discrepancies fail the check. A skipped run is not sandbox validation.
The paper venue fills marketable orders only while an equity session is open (overnight,
pre-market, regular or after-hours); outside one, the fill test fails with that explanation
after cancelling its order, and an amendment is refused by the venue, which the resting-order
test accepts as long as broker and order manager agree. Each order test cancels its own orders
and closes whatever its own orders filled in a `finally` block, measured from the broker's record
of those orders, and a run that otherwise passed fails if that cleanup could not be completed. Retain the TRX output and the reported UTC run time privately with the change's
evidence; the diagnostic output contains state flags and counts rather than credential values,
account identifiers, balances or order ids.

## Broker behaviour observed on paper

Recorded because fixtures cannot show it and code that reads the broker must tolerate it:

- The open-order list is eventually consistent with single-order reads. Immediately after a
  confirmed cancel, `GET /v2/orders/{id}` reports `canceled` while `GET /v2/orders?status=open`
  can still list the order as `new` for a moment. A reconciliation that runs in that moment
  reports a break and withholds readiness; the next synchronization clears it.
- The FILL activity is published shortly after the order itself reports `filled`. Until then a
  reconnect reconciliation carries the fill from the order snapshot at its average price.
- An order outside any equity session rests in `accepted` and cannot be replaced
  (`422 cannot replace order in accepted status`); once routed it is `new` and replaceable.
- A replacement is a new broker order with a broker-assigned client id; the original keeps its
  client id, reports `replaced`, and links its successor through `replaced_by`.
- A reused client id is refused with `422 client_order_id must be unique`.
- Paper and live key pairs authenticate only against their own host.
- An underlying with no options answers with an empty contract array, not an error; without an
  expiration bound the contract listing stops at the upcoming weekend.
- On the market-data host, the option chain route refuses a `symbols` filter (single contracts
  have their own route), and option snapshots carry no underlying price and no open interest.

## Evidence boundary

[Provider evidence labels](../ai/solo-developer-workflow.md#provider-evidence-labels) for Alpaca
as of the 2026-11-10 paper run (dated record held privately by the owner):

| Surface | Label | Scope |
| --- | --- | --- |
| Brokerage REST: account, positions, orders, submit, replace, cancel, recovery lookup, FILL activity | `live-verified` | Paper environment, one-share US equity orders |
| Portfolio synchronization, reconciliation, risk blocking, restart recovery | `live-verified` | Paper environment, REST lane |
| Symbol search and asset details | `live-verified` | Paper host; equities and crypto pairs |
| Option contract discovery (expirations, strikes) | `live-verified` | Paper host |
| Activity statement connector | `live-verified` | Cash journal and trade fills observed |
| Option chain snapshot and option quote | `live-verified` | Market-data host; indicative feed |
| Trade-update WebSocket stream | `live-untested` | Not exercised by these tests |
| Market-data streaming (equities, options, crypto, news) | `live-untested` | WebSocket; not exercised by these tests |
| Live-money environment | `live-untested` | Never selected by these tests |

These tests provide real paper-broker and market-data REST evidence. They do not establish live-money readiness,
execution-stream continuity, a network-level disconnect (the lost acknowledgement is produced by
dropping one real response in the test transport), or real-broker partial fills: a one-share
order fills in one execution, and paper partial fills cannot be requested. The deterministic
`BrokerageOrderRecoveryTests`, `LiveBrokeragePortfolioSyncServiceTests`,
`BrokerageExecutionReconciliationServiceTests`, `AlpacaBrokerageGatewayTests`, and
`AlpacaTradeUpdatesClientTests` cover late fills, partial fills, duplicate prevention, preserved
exposure, and incomplete-state blocking. Fixture results and these opt-in checks must be reported
separately.

See [Brokerage Account Snapshot](../domain/brokerage-account-snapshot.md) and
[Alpaca onboarding](../operators/provider-onboarding-alpaca.md) for the operational contract.
