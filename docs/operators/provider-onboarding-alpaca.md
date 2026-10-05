# Provider Onboarding: Alpaca

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-05-31

This is the canonical operator procedure lane for configuring and validating Alpaca provider onboarding in Meridian.

## Scope

- credential setup and storage,
- paper-first validation,
- feed selection and reconciliation impact,
- operator-level validation handoff and rollback behavior.

## Quick operator flow

1. Create paper account and API credentials in the Alpaca dashboard.
2. Set credentials via secure environment variables or approved credential store.
3. Start Meridian with standard workstation mode.
4. Confirm effective configuration and provider status.
5. Run provider validation packet check before enabling production feed mode.

## Prerequisites

- Active Alpaca account (paper for development).
- Secure credentials for `ALPACA_KEY_ID` and `ALPACA_SECRET_KEY`.
- Appropriate host port and environment routing for your deployment target.

## Configuration

Use the environment-first pattern:

```powershell
$env:ALPACA_KEY_ID = "<your-api-key-id>"
$env:ALPACA_SECRET_KEY = "<your-secret-key>"
$env:ALPACA_PAPER = "true"
```

Then in runtime config, ensure provider is explicitly enabled and paper mode is intended for non-production.

Recommended defaults:

- start with `Paper=true`
- begin with `DataFeed=iex` for verification
- only increase to `sip` after stable packet + reconciliation checks

## Mandatory validation sequence

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080
curl http://localhost:8080/api/config/effective
curl http://localhost:8080/api/workstation/operator/inbox
```

- Confirm credentials are loaded from environment (not repo files).
- Confirm provider readiness rows are non-blocking for required workflow.
- Confirm no active critical break introduced by feed mode.

## Account portfolio recovery

The Trading workspace's **Brokerage recovery** panel uses the active Alpaca execution gateway
and the selected account's retained brokerage link. Configure a paper execution gateway, link the
local account to its discovered Alpaca account, and choose **Synchronize and reconcile**. This
requires account-scoped trade-write permission. The command reads broker state and recovers
retained executions; it does not submit replacement orders or resume strategy runs.

The panel shows connection health, latest attempt and successful synchronization, broker cash,
buying power, currency, completeness, affected runs, and retained-local versus broker discrepancies.
Evidence expires after 30 seconds. Disconnects, credential/environment changes, order changes,
fills and process restarts revoke readiness. Incomplete or inconsistent evidence remains blocked.
The current execution portfolio lane supports Alpaca USD equities and standard US equity options;
unsupported assets or missing provider fields block rather than supply guessed exposure.

Broker dispatch identities and processed order watermarks are retained under the configured data
root's `execution` directory and bound to the verified broker account/environment. Keep these files
with the durable execution inbox. Do not delete them to clear a blocked order or share a data root
between concurrently running execution hosts. Account/environment mismatch fails closed.

Run deterministic regression fixtures before using a paper account:

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter 'FullyQualifiedName~BrokerageOrderRecoveryTests|FullyQualifiedName~BrokerageExecutionReconciliationServiceTests|FullyQualifiedName~LiveBrokeragePortfolioSyncServiceTests|FullyQualifiedName~AlpacaBrokerageGatewayTests|FullyQualifiedName~AlpacaStreamedFillLoopTests|FullyQualifiedName~TradingBrokerageRecoveryEndpointTests'
```

Run the opt-in [paper portfolio sandbox check](../testing/alpaca-paper-portfolio-sandbox.md)
for real broker account, holdings, reconciliation, and risk evidence. For external
paper-sandbox execution acceptance, retain sanitized account and client-order evidence:

1. Confirm the endpoint is `paper-api.alpaca.markets`, verify the account, synchronize, and compare
   holdings, cash, equity, buying power and USD currency to Alpaca's account and positions responses.
2. Submit a uniquely identified paper order through the governed execution path. Interrupt the
   connection after acceptance and verify that the same client ID remains retained and trading blocks.
3. Reconnect and synchronize. Verify authoritative lookup recovers the original broker order with
   exactly one submission. Repeat for a partial fill followed by a late fill; compare cumulative
   quantity and cash/exposure, including fills at different prices.
4. Restart using the same data root. Verify recovery looks up retained identities before readiness
   and does not repeat processed fill quantities or submit duplicate orders.
5. Confirm a missing broker order, malformed/incomplete snapshot, foreign account, or unresolved
   discrepancy stays visibly blocked. Reconciliation must supply evidence before readiness clears.

Deterministic HTTP/stream fixtures and browser fixtures are separate evidence from this external
sandbox procedure. Paper fills depend on broker behavior; report any scenario not observed rather
than treating fixture success as an external-broker result.

## Evidence requirements

Before production promotion, require:

- `wave1-validation-summary` row for Alpaca provider,
- `dk1-operator-signoff.json` when the provider is part of release scope,
- one complete readiness packet with status timestamp and approver.

Keep evidence packets under sanitized paths used by team policy (for example: `artifacts/provider-validation/...`).

## Runbook links

- [Provider Credential Operations](./provider-credentials.md)
- [Operator Preflight Checklist](./preflight-checklist.md)
- [Provider Integration Status](../reference/provider-integration-status.md)
- [Provider Validation Matrix](../reference/provider-validation-matrix.md)
- [Provider Validation Evidence Schema](../reference/provider-validation-evidence-schema.md)

## Source and archive

- Legacy source archived at [archive/docs/providers/alpaca-setup.md](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/providers/alpaca-setup.md)
