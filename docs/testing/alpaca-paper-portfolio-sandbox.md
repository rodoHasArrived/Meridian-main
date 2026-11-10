# Alpaca paper broker and market-data validation

**Status:** supporting
**Owner:** core-team
**Reviewed:** 2026-11-10

`AlpacaPaperSandboxTests` exercises the production Alpaca portfolio adapter, account-scoped
synchronization service, OMS reconciliation service, exposure provider, and portfolio risk gate
against the real paper Trading API. It reads account balances, holdings and open orders, then
checks account isolation and the restart/disconnect blocks. A second test fetches the account's
activity through `AlpacaActivityStatementConnector`, parses it on the same path as an imported
statement, checks period bounds and account scoping, and verifies the credentials through
`AlpacaBrokerageConnectionService` against a scratch credential store. Neither test submits,
changes or cancels an order. An HTTP guard allows only the account, positions, open-order and
account-activity GET endpoints on `paper-api.alpaca.markets` and disables redirects, independently
of configuration.

## Run the real-broker check

Use a dedicated Alpaca paper account. A stable account with no open orders exercises the ready
path. Existing open orders are compared with a fresh OMS and must remain visibly blocked; that
outcome certifies discrepancy detection, not a ready account. Changing marks or balances can
cause the one-cent consistency check to block; the test retries up to three times and then fails
with the reported reasons if it cannot obtain stable evidence.

Configure these values through the execution environment's secret settings:

- `ALPACA_KEY_ID`: paper Trading API key.
- `ALPACA_SECRET_KEY`: corresponding paper secret.
- `ALPACA_TRADING_ENVIRONMENT`: exactly `paper`.
- `MERIDIAN_RUN_ALPACA_PAPER_SANDBOX`: `1` to explicitly enable this test.

Permit HTTPS access to `paper-api.alpaca.markets:443`. This read-only smoke does not open a
WebSocket. Credentials remain in environment variables and are not printed or persisted by the
test. Run from the repository root with the repository's .NET 10 SDK:

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj \
  --filter 'FullyQualifiedName~AlpacaPaperSandboxTests' \
  --logger 'console;verbosity=normal' \
  --logger 'trx;LogFileName=alpaca-paper-portfolio.trx'
```

## Run the real-broker order check

`AlpacaPaperOrderLifecycleTests` drives the production `AlpacaBrokerageGateway` against the same
paper Trading API with real orders. It is a separate opt-in because it changes account state:

- A resting one-share limit order is submitted, listed, recovered by client order ID and cancelled.
- An extended-hours order is replaced, then cancelled through its original client order ID.
- A reused client order ID, an unknown asset and insufficient buying power are each refused.
- Bracket, stop, stop-limit, trailing-stop and good-til-cancelled orders are accepted and cancelled.
- A single-leg option order and a two-leg option package are accepted and cancelled.
- One share is bought and sold with marketable extended-hours limit orders. The fills must appear
  in the position, in the activity snapshot, and exactly once in the durable trade-update inbox
  across repeated REST reconciliations and restarts.

Set `MERIDIAN_RUN_ALPACA_PAPER_ORDERS=1` with the same three credential variables, then run:

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj \
  --filter 'FullyQualifiedName~AlpacaPaperOrderLifecycleTests' \
  --logger 'console;verbosity=normal' \
  --logger 'trx;LogFileName=alpaca-paper-orders.trx'
```

An HTTP guard allows only `GET` requests and single-order submit, replace and cancel requests on
`paper-api.alpaca.markets`; it refuses the cancel-all and close-position routes. Resting orders
are priced at 1.00 so they cannot execute. Every order carries a per-run `mrd-it-` client order ID
prefix, and each test cancels or flattens only what it created, so other orders and positions in
the account are left alone. The round trip costs the paper spread on one share.

Requirements and limits:

- The option cases need options approval level 3 on the paper account.
- The round trip fills only while an Alpaca session is trading (overnight, pre-market, regular or
  after-hours). Outside one, that test fails with a message saying so.
- Alpaca refuses to replace an order it has only queued, such as a regular-session order submitted
  while the market is closed. The replacement test accepts either outcome and always verifies that
  no order is left working.
- The OMS admission path is not exercised: live routing through `OrderManagementSystem` requires
  a W7-approved run, which this suite does not fabricate.

## Run the real market-data check

`AlpacaMarketDataSandboxTests` runs the production `AlpacaHistoricalDataProvider` and
`AlpacaCorporateActionProvider` against the real Market Data API. It is read-only: an HTTP guard
allows only `GET` requests to `data.alpaca.markets`. Every expectation is a settled historical
fact (Apple's 2020 split, General Electric's 2021 reverse split, one week of 2025 sessions), so
the result does not depend on when it runs.

- Corporate actions: known splits and dividends, with evidence identity on every record.
- Daily bars: every session of a week, split adjustment against raw prices, and five years of paging.
- Quotes, trades and auctions for one symbol, bounded by the requested limit.

Set `MERIDIAN_RUN_ALPACA_MARKET_DATA=1` with `ALPACA_KEY_ID` and `ALPACA_SECRET_KEY` (paper keys
are accepted by the Market Data API), then run:

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj \
  --filter 'FullyQualifiedName~AlpacaMarketDataSandboxTests' \
  --logger 'console;verbosity=normal' \
  --logger 'trx;LogFileName=alpaca-market-data.trx'
```

The account's market-data plan decides the feed; the default `iex` feed is enough for this check.
Option snapshots, symbol search and the market-data WebSocket are outside it.

### Credentials held outside the test process

Some managed environments keep the secret out of the process and add the Alpaca authentication
header at the network boundary. The tests still require the credential variables to be present,
so set whichever value the environment supplies itself to a placeholder there, and keep the real
value for anything it does not supply. Such an environment cannot run the trade-update WebSocket,
which authenticates inside the socket rather than in a header.

## Broker behaviour these checks established

Observed against the paper Trading API on 2026-11-10 and now covered by deterministic fixtures:

- **Multi-leg parents carry no instrument.** Alpaca returns an option package parent with an
  empty `symbol` and `side`; only its legs name instruments. The gateway and the trade-update
  client leave the parent unnormalized instead of emitting a report the durable inbox refuses,
  which previously failed the post-connect reconciliation for every other order. Recovering or
  amending a package parent is refused with that reason. Its legs are separate broker orders.
- **A replaced order cannot be cancelled.** Alpaca answers 422 for an order in `replaced` state
  while its successor keeps working. `CancelOrderAsync` now follows the broker's `replaced_by`
  links and cancels the live successor; if a link cannot be verified, the cancellation stays
  nonterminal so the order remains in cancellation sweeps.
- **422 does not distinguish its causes.** A reused client order ID and an unknown asset return
  the same status and code, so both stay uncertain until recovery reads the client order ID.
- **Corporate actions come from the Market Data API.** The provider previously called a
  `/v2/corporate-actions/announcements` path that does not exist and expected a response shape
  the broker never sends, so it returned nothing. It now reads
  `data.alpaca.markets/v1/corporate-actions`, which groups records by action type, reports
  numeric rates and no currency, and omits record and payable dates on older dividends.
- **One-symbol and many-symbol tick requests answer differently.** The single-symbol quote and
  trade routes return a bare array, the multi-symbol routes a map keyed by symbol, and equity
  trade IDs are numbers. The historical provider now always uses the keyed routes.
- **A limit is a page size to Alpaca.** The provider now treats the caller's limit as a bound on
  the result and reports that more remains, instead of paging through the whole window.
- **Auctions exist only on SIP.** Alpaca rejects an auctions request that names another feed, so
  none is sent. A date-only end is inclusive; asking for the following day reaches into today
  when the range ends yesterday, which plans without recent SIP data refuse.
- **Listings trail lookups.** The open-order listing can still show an order for a moment after
  its own lookup reports it cancelled, and a `FILL` activity can appear a moment after the order
  reports filled. Until the activity lands, the order snapshot carries the fill at its average
  price.

## Reading the result

Without the explicit opt-in, xUnit reports each test as **skipped**. Once opted in, missing
credentials, a live environment selection, unavailable network, rejected authentication, missing
broker evidence, or unexplained discrepancies fail the check. A skipped run is not sandbox
validation. Retain the TRX output and the reported UTC run time with the change's evidence; the
diagnostic output contains state flags and request counts rather than credential values or account
identifiers.

## Evidence boundary

These tests provide real paper-broker REST evidence: account state, the order lifecycle, complete
fills, REST fill recovery into the durable inbox, and historical and corporate-action market data.
They do not establish live-money readiness, trade-update or market-data WebSocket continuity, an
actual post-acceptance disconnect, real-broker partial fills, or filled multi-leg packages.
The deterministic `BrokerageOrderRecoveryTests`, `LiveBrokeragePortfolioSyncServiceTests`,
`BrokerageExecutionReconciliationServiceTests`, `AlpacaBrokerageGatewayTests`, and
`AlpacaTradeUpdatesClientTests` cover retained dispatch recovery, uncertain acceptance, late fills,
partial fills, duplicate prevention, preserved exposure, and incomplete-state blocking. Those
fixture results and the opt-in runs must be reported separately.

See [Brokerage Account Snapshot](../domain/brokerage-account-snapshot.md) and
[Alpaca onboarding](../operators/provider-onboarding-alpaca.md) for the operational contract.
