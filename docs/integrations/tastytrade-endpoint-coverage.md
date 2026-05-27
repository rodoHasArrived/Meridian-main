# Tastytrade endpoint catalog and Meridian coverage map

_Last reviewed: 2026-05-19 (UTC)._

This catalog focuses on the endpoint groups needed for Meridian brokerage integration planning:

- auth/session
- accounts/positions
- options instruments/chains
- orders
- fills
- account activity history

## 1) Endpoint catalog (by integration concern)

> Notes:
> - Path formats use the tastytrade API docs conventions (`:account-number`, `{underlying_symbol}`).
> - Endpoints marked **(inferred)** are documented in navigation/guides but should be validated against the live OpenAPI reference before production wiring.

### A. Auth / session

| Concern | Endpoint(s) | Meridian adapter role |
|---|---|---|
| Create API session | `POST /sessions` | `ITastytradeAuthClient.CreateSessionAsync` |
| Destroy session | `DELETE /sessions` **(inferred)** | `ITastytradeAuthClient.EndSessionAsync` |
| OAuth / token workflows | OAuth2 guide flows (interactive + refresh token lifecycle) | `ITastytradeAuthClient.RefreshSessionAsync` + credential store integration |

### B. Accounts / positions

| Concern | Endpoint(s) | Meridian adapter role |
|---|---|---|
| Enumerate accounts for principal | `GET /customers/me/accounts` | `ITastytradeAccountClient.GetAccountsAsync` |
| Account positions snapshot | `GET /accounts/:account-number/positions` | `ITastytradePositionsClient.GetPositionsAsync` |
| Account balances (risk + buying power context) | `GET /accounts/:account-number/balances` | `ITastytradeAccountClient.GetBalancesAsync` |

### C. Options instruments / chains

| Concern | Endpoint(s) | Meridian adapter role |
|---|---|---|
| Nested chain by underlying | `GET /option-chains/{underlying_symbol}/nested` | `ITastytradeOptionsInstrumentClient.GetNestedChainAsync` |
| Flat/detailed option instruments | `GET /option-chains/{underlying_symbol}` | `ITastytradeOptionsInstrumentClient.GetDetailedChainAsync` |
| Compact symbol list across expirations | `GET /option-chains/{underlying_symbol}/compact` | `ITastytradeOptionsInstrumentClient.GetCompactSymbolsAsync` |
| Underlying / symbol discovery for pre-trade resolution | Symbol Search endpoints **(inferred from API docs nav)** | `ITastytradeInstrumentSearchClient.SearchAsync` |

### D. Orders

| Concern | Endpoint(s) | Meridian adapter role |
|---|---|---|
| Submit order | `POST /accounts/:account-number/orders` **(inferred)** | `ITastytradeOrderClient.SubmitOrderAsync` |
| Live/open orders | `GET /accounts/:account-number/orders/live` **(inferred)** | `ITastytradeOrderClient.GetLiveOrdersAsync` |
| Order history / lookup | Search orders endpoint family **(inferred)** | `ITastytradeOrderClient.SearchOrdersAsync` |
| Cancel order | `DELETE /accounts/:account-number/orders/{order_id}` **(inferred)** | `ITastytradeOrderClient.CancelOrderAsync` |
| Cancel-replace | Cancel-replace endpoint family **(inferred)** | `ITastytradeOrderClient.ReplaceOrderAsync` |

### E. Fills / executions

| Concern | Endpoint(s) | Meridian adapter role |
|---|---|---|
| Trade/fill events in account ledger | `GET /accounts/:account-number/transactions` (filter to trade/fill-like records) | `ITastytradeExecutionClient.GetFillsAsync` |
| Near-real-time order lifecycle/fill updates | Streaming Account Data channels **(inferred from docs nav)** | `ITastytradeExecutionStreamClient.SubscribeAsync` |

### F. Account activity history

| Concern | Endpoint(s) | Meridian adapter role |
|---|---|---|
| Account transaction history | `GET /accounts/:account-number/transactions` | `ITastytradeActivityClient.GetTransactionsAsync` |
| Account-level status and snapshots used in operations runbooks | Account Status endpoints **(inferred from docs nav)** | `ITastytradeAccountClient.GetAccountStatusAsync` |

## 2) Meridian capability mapping + temporary limitations

| Meridian capability category | Tastytrade coverage | Equivalence status | Temporary limitation to declare |
|---|---|---|---|
| Identity & session bootstrap | Strong (`POST /sessions`, OAuth flows) | Equivalent for baseline session auth | None, beyond secure token handling and rotation policy |
| Brokerage account discovery | Strong (`GET /customers/me/accounts`) | Equivalent | None |
| Position ingestion (equity + options) | Strong (`GET /accounts/:account-number/positions`) | Equivalent for snapshot; streaming parity depends on stream subscription rollout | Start with polling snapshot cadence before stream-based intraday state convergence |
| Option universe/chain ingestion | Strong (`/option-chains/...`) | Equivalent for listed chain metadata | No OPRA-grade Greeks/vol surface from chain endpoint alone; require separate market data enrichment path |
| Order submission + lifecycle | Moderate/Strong (submit/search/live/cancel families) | Functionally equivalent once endpoint semantics confirmed | Phase 1 limitation: only supported order archetypes (single-leg + selected verticals) until strategy-leg validation matrix is certified |
| Fill capture / execution audit | Moderate (transactions + streaming account events) | Partially equivalent | Treat transactions endpoint as source-of-truth for durable reconciliation; stream events are advisory until replay/recovery proof is complete |
| Account activity / ledger history | Strong (`transactions`) | Equivalent for broker activity timeline | Phase 1: map only normalized transaction classes needed by current Meridian books/recon flows; unknown types routed to suspense queue |
| Pre-trade risk and margin explainability | Partial (balances + margin endpoints available, but semantics differ) | Non-equivalent today | Temporary limitation: “indicative broker risk only” badge; Meridian internal acceptance gates remain authoritative |
| Complex options strategy parity | Partial | Non-equivalent today | Temporary limitation: unsupported multi-leg patterns rejected at adapter boundary with explicit reason code |

## 3) Non-equivalent capabilities that need explicit rollout guardrails

1. **Risk explainability parity is not 1:1** between tastytrade account margin/risk artifacts and Meridian’s internal readiness + acceptance-gate vocabulary.
   - Guardrail: expose broker-risk data as external evidence, not as final gate authority.
2. **Execution replay durability** requires deterministic rebuild from persisted fills/transactions.
   - Guardrail: maintain a reconciliation pass that cross-checks order states against transaction history before session sign-off.
3. **Complex order shape coverage** (ratio spreads, broken-wing, calendars/diagonals, futures-option hybrids) is likely broader in broker API than initial Meridian policy.
   - Guardrail: adapter-side strategy whitelist with explicit `NotYetSupported` rejections.
4. **Instrument metadata completeness for options analytics** (exercise style, settlement type, DTE, multipliers) is present, but risk analytics require additional quote/greeks inputs.
   - Guardrail: mark analytics as “metadata-only” until quote/greeks enrichment is connected.

## 4) Adapter boundaries for options metadata and risk-relevant fields

Use explicit boundary interfaces so transport DTOs never leak into Meridian domain models.

### Boundary A: Transport clients (raw broker schema)

- `ITastytradeAuthClient`
- `ITastytradeAccountClient`
- `ITastytradePositionsClient`
- `ITastytradeOptionsInstrumentClient`
- `ITastytradeOrderClient`
- `ITastytradeExecutionClient`
- `ITastytradeActivityClient`

Responsibilities:
- HTTP/auth/session mechanics
- endpoint-specific request/response DTOs
- retry/throttle/backoff + broker error code normalization

### Boundary B: Normalization layer (broker → Meridian canonical contracts)

- `IOptionsContractMetadataMapper`
- `IOptionsPositionMapper`
- `IOrderLifecycleMapper`
- `IAccountActivityMapper`

Responsibilities:
- Convert tastytrade fields to Meridian canonical models.
- Apply enum and value-domain mapping with explicit unknown handling.
- Emit `MappingEvidence` diagnostics for auditability.

### Boundary C: Risk enrichment layer

- `IOptionsRiskEnrichmentService`
- `IBrokerMarginSnapshotMapper`

Responsibilities:
- Join chain metadata with quote/greeks feeds.
- Compute/derive risk-relevant projections used by readiness views.
- Preserve separation between broker-indicated margin and Meridian acceptance-gate outcomes.

### Required options contract metadata fields (minimum canonical set)

- `UnderlyingSymbol`
- `OptionSymbol` (broker symbol + canonical normalized symbol)
- `OptionType` (Call/Put)
- `StrikePrice`
- `ExpirationDate`
- `DaysToExpiration`
- `ExerciseStyle` (American/European)
- `SettlementType` (AM/PM/Cash/Physical as available)
- `ContractMultiplier` / `SharesPerContract`
- `RootSymbol`
- `ChainType`
- `IsClosingOnly`
- `StopsTradingAt`
- `ExpiresAt`
- `StreamerSymbol` (if supplied)

### Required risk-relevant fields (minimum phase-1 set)

- Position quantity + direction (long/short)
- Average open price / cost basis components
- Marking inputs (bid/ask/last source tag and timestamp)
- Realized and unrealized P/L components
- Buying power effect / margin requirement snapshots
- Concentration indicators (underlying-level gross + net exposure)
- Assignment/exercise risk indicators (near-expiry ITM flags)
- Data freshness indicators (`asOf`, source, staleness seconds)

## 5) Phase-1 implementation recommendation

1. Implement auth, accounts, balances, positions, option chains, order submit/cancel/live, transactions first.
2. Gate complex multi-leg options to an allowlist while collecting evidence on rejected patterns.
3. Drive fills and account history from transactions for durable audit, with stream updates as low-latency hints.
4. Keep broker risk metrics visually and semantically distinct from Meridian’s internal acceptance gates.
5. Add a capability matrix test fixture that asserts each mapped endpoint has:
   - transport contract test,
   - mapper test,
   - failure-mode test (auth, throttling, unknown enum, missing field).
