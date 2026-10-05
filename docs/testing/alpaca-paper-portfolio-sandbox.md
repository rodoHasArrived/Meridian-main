# Alpaca paper portfolio synchronization validation

**Status:** supporting
**Owner:** core-team
**Reviewed:** 2026-10-05

`AlpacaPaperSandboxTests` exercises the production Alpaca portfolio adapter, account-scoped
synchronization service, OMS reconciliation service, exposure provider, and portfolio risk gate
against the real paper Trading API. It reads account balances, holdings and open orders, then
checks account isolation and the restart/disconnect blocks. It never submits, changes or cancels
an order. An HTTP guard allows only three GET endpoints on `paper-api.alpaca.markets` and disables
redirects, independently of configuration.

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

Without the explicit opt-in, xUnit reports the test as **skipped**. Once opted in, missing
credentials, a live environment selection, unavailable network, rejected authentication, missing
broker evidence, or unexplained discrepancies fail the check. A skipped run is not sandbox
validation. Retain the TRX output and the reported UTC run time with the change's evidence; the
diagnostic output contains state flags and request counts rather than credential values or account
identifiers.

## Evidence boundary

This test provides real paper-broker REST evidence. It does not establish live-money readiness,
execution-stream continuity, an actual post-acceptance disconnect, or real-broker partial fills.
The deterministic `BrokerageOrderRecoveryTests`, `LiveBrokeragePortfolioSyncServiceTests`,
`BrokerageExecutionReconciliationServiceTests`, `AlpacaBrokerageGatewayTests`, and
`AlpacaTradeUpdatesClientTests` cover retained dispatch recovery, uncertain acceptance, late fills,
partial fills, duplicate prevention, preserved exposure, and incomplete-state blocking. Those
fixture results and this opt-in smoke must be reported separately.

See [Brokerage Account Snapshot](../domain/brokerage-account-snapshot.md) and
[Alpaca onboarding](../operators/provider-onboarding-alpaca.md) for the operational contract.
