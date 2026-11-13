# Execution preview, journal, alerts and strategy parity

Implemented scope: single-leg USD US equities, the existing local paper simulator and Alpaca paper adapter, shared backend and browser workstation. Options, multi-leg preview economics, other currencies, short-lot attribution and automatic strategy promotion are outside this increment. Existing broker controls and accounting remain authoritative.

## Order preview and admission

`POST /api/execution/orders/preview` accepts the same `OrderRequest` as submission. It requires an execution fund account, global order-management permission and account-scoped authorization. The browser waits for a successful, current preview and operator acknowledgement. A client order ID is assigned to the draft and reused for its submission. Changing the draft invalidates its preview.

The ticket captures the submitted account and client order ID before sending the request. A definitive refusal, such as authorization failure or rate limiting, retires that ID and requires a fresh preview before another submission. An unconfirmed response holds the ticket even when the draft or selected account changes. Select **Refresh checks** to look up the submitted identity. The ticket clears the hold when the journal reports a terminal outcome for that order.

The response contains the normalized request, fingerprint, evaluation/expiry timestamps, executable-side quote and source, estimated notional, nullable estimated fees, approval requirement and explicit checks. A preview lasts at most 30 seconds and never outlives its quote. Unsupported instruments, unknown classification, stale quotes, unavailable risk observation and unassessed live readiness cannot yield a green result. Fees remain unknown until evidence exists.

Preview uses observation-only admission/risk evaluation. It does not submit, journal an order, reserve throttle/exposure capacity, consume approval tokens, park an escalation, purge overrides, trip controls or trigger broker recovery. Live readiness is observed from a recent already-evaluated cache; the operator must synchronize readiness separately. Submission always performs current admission and risk validation again. The shared admission policy rechecks the quote at the gateway boundary, after risk and retention waits, and requires a fresh executable-side quote for covered equities on direct submissions and risk-changing amendments as well as previews. Preview is evidence, never a routing capability or price guarantee. Governed escalation is shown as requiring approval and can still reach the existing approval parking path on submission.

`sessionIntent` is additive and omitted from old serialized requests/states when null:

- `RegularOnly` is the default for known equities and refuses submission outside the regular session.
- `QueueNextSession` permits the broker to queue eligible Day/GTC market or limit orders outside regular trading hours. Preview and submission block this intent during regular trading hours and for the local paper simulator, which matches orders immediately. The OMS checks again before dispatch.
- `ExtendedHours` requires an eligible Day/GTC limit order in an open premarket, regular or after-hours session. The Alpaca payload maps the intent explicitly.

The shared trading calendar supplies holidays, Eastern time and early closes. Existing policies for other instruments remain in place; the new preview does not claim coverage for them.

## Retained operational journal

`ExecutionJournal` uses a synchronous-flush write-ahead log under `<workstation-data>/execution/journal`. Startup replays immutable version-1 events. Local paper books retain an incarnation marker. A fresh portfolio with earlier retained fills records a durable economic coverage gap because its positions were not restored; prior orders remain visible, but FIFO P&L and strategy attribution remain unavailable across that boundary. Reopening the journal over the same portfolio preserves continuity. Each identity includes verified gateway/environment scope and fund account. Exact retries are idempotent; changed economic content under an existing identity fails closed. There is no destructive compaction or automatic deletion in this increment. Retain this directory with the account's recovery evidence; it currently grows with history and is indexed in memory.

The OMS retains intent before admission, an opening-position checkpoint before registering the order, and dispatch evidence before calling the gateway. It retains raw execution evidence before accounting/stream acknowledgement and state evidence before the recovery checkpoint. Once dequeued, financial report retention and accounting finish without caller cancellation; optional session bookkeeping can still stop during shutdown. Retention/accounting failure stops the report pump and closes admission through the existing terminal delivery-failure path, instead of silently consuming a report and restarting its stream. Recovery still requires authoritative broker/session evidence.

Replacement and cancel intents, uncertain operation outcomes, broker predecessor/successor identities, cumulative fills and confirmed states remain linked to the original client order ID and its submission attempt. Reused client IDs produce separate attempts, so a refusal or a completed order cannot supply the symbol or strategy tags for a later dispatch. If the operational journal is unavailable, cancellation and the kill-switch sweep still use broker acknowledgements and the ordinary recovery and accounting path. The failure is logged and closes admission pending recovery. Legacy account-bound OMS and paper-session states are imported as explicitly incomplete history. Missing original tags and individual fills are not invented.

The worker reconciles Alpaca activities every minute and on connection-generation changes. It retains fill IDs, canonical activities and complete-page cursors. A retained high watermark with a five-minute overlap supports restart backfill across trading days. Pagination must reach a short page; missing/repeated page tokens fail closed. Account aliases are checked against the connected broker identity. The journal joins an external fill to its submission attempt when retained execution reports establish the broker order identity, preserving the original fill evidence and counting the execution once.

Read routes, all account-scoped and bound to one captured current environment:

| Route | Purpose |
| --- | --- |
| `GET /api/execution/journal/orders?accountId=...` | Orders; optional symbol, strategy/run, tag, status, from, to and take (1–500) |
| `GET /api/execution/journal/orders/{orderId}?accountId=...` | Retained events for the latest attempt under an exact root ID, or for the attempt selected by `attemptId` |
| `GET /api/execution/journal/summary?accountId=...&from=...&to=...` | Operational aggregates for a half-open UTC date window |

FIFO uses the canonical ledger lot-consumption and basis-relief helpers, without posting accounting entries. Acquisition and disposal costs are allocated to opening strategy/run/tag lots. Reporting replays older lots before calculating a selected time window. Gross P&L, net P&L, commissions, fees and signed executable-reference slippage are separate nullable operands. Unknown cost is never zero. Unpriced execution, missing initial basis, unsupported economics, external/unassociated history, corporate actions, position transfers and trade corrections prevent complete attribution. Those cases require authoritative basis/reconciliation work; this journal does not rebuild tax basis. The UI labels coverage and leaves unavailable values explicit.

The Orders screen includes filters, an inspector for retained events and notification deep links. Account/date totals are labeled separately from order-list symbol/strategy/tag/status filters.

## Alerts and external delivery

The existing notification center receives durable account/environment-scoped execution alerts through snapshot and SSE endpoints. Streams use the existing per-session connection cap and stop across an environment change. Existing local read/dismiss behavior is retained; dismissing a notification does not clear execution controls.

Alerts cover fills, admission/operation rejections, unresolved replacements/cancels, stale working orders, loss-monitoring gaps, configured daily-loss breaches and an end-of-day digest. Staleness defaults to 15 eligible regular-session minutes; closed hours do not age queued orders. The digest is scheduled at regular close plus 15 minutes, including early closes, and can be recovered after restart. Stable condition identities prevent repeated alerts from repeated scans.

`GET/POST /api/execution/alert-settings` is account-scoped and requires configuration authority (and order-management authority to change settings). Daily-loss alerting and external webhooks are disabled by default. In Settings → Preferences, a positive USD loss threshold enables monitoring; a blank threshold disables it. Stale minutes must be 1–1440.

Daily equity P&L is `current equity - prior-close equity - net external cash flows`, including realized and unrealized changes. A breach requires USD denomination, a broker-supplied prior-close baseline, complete classified cash-flow coverage and a fresh observation. A withdrawal is not a trading loss. Activity is bounded at the equity response cutoff; transfers during the equity request are ambiguous and produce a monitoring gap. Malformed or missing equity cannot become a measured zero. Unknown transfers, ambiguous date-only transfers, stale valuation or unavailable baseline produce a monitoring-gap alert. An unqualified global local-paper portfolio cannot establish a fund-account prior-close baseline; it intentionally remains unassessed rather than inferring one. Account-qualified simulator observations can use the same calculation when their baseline/flow evidence is supplied.

Enabling a webhook requires an absolute non-loopback HTTPS URL without embedded credentials. Delivery runs on a separate worker so a slow receiver cannot delay monitoring. Attempts and successful delivery are retained in the outbox; eligible pending alerts are selected before batching, including alerts older than the notification center's 200-item display window. Delivery has four attempts and a 15-second HTTP timeout, with redirects disabled. After the limit, an in-app failure alert remains visible. A crash after an external receiver accepts a request but before the delivery record is flushed can deliver twice; receivers should deduplicate the stable notification ID. This increment does not provide exactly-once remote delivery, secret signing, or multi-process outbox ownership.

## Shared strategy and controlled comparison

`MovingAverageCrossoverStrategy` in `Meridian.Backtesting.Sdk` is the shared decision implementation. The existing `moving-average-crossover` live catalog ID delegates through `BacktestStrategyLiveAdapter`. Defaults remain fast 10, slow 30 and quantity 10. Existing catalog requests without `inputMode` keep trade input. Reference comparisons select `inputMode=bars` and retain `strategyVersion=sma-crossover-v1` explicitly. The strategy registry advertises these parameters.

The strategy warms up over the slow window, consumes one ordered observation per symbol/time, targets long-only equity market intents and maintains pending quantity through partial fills. A reversal waits until the entry completes. Confirmed rejection/cancellation/expiry releases its pending identity. The backtest engine and live adapter propagate terminal outcomes to the same observer contract.

`GET /api/execution/strategy-parity?accountId=...&runId=...` compares an authorized run with its authorized retained parent. It requires matching strategy/version/parameters, bar input, data provenance, execution realism, initial capital, universe and full ended-run evidence. It compares normalized fill sequences and net P&L within one cent. Missing evidence is `not-comparable`; mismatches are `observed-differences`. Agreement grants no promotion or live authority. Actual paper fills may differ from a replay even when the decision implementation is shared.

## Hermetic tests and opt-in broker probe

Ordinary .NET, Vitest and Python tests use synthetic/mocked evidence, frozen clocks and local WAL directories. They need no broker credentials and place no orders.

`scripts/dev/alpaca_paper_order_probe.py` is a separate, disabled-by-default smoke probe. It targets fixed Alpaca paper and data HTTPS origins, refuses redirects, verifies the exact account UUID, requires an initially flat account without working orders and acquires an OS account lock. Use one dedicated paper account and one runner; the lock does not coordinate different machines or unrelated external clients.

To run deliberately, configure paper credentials in the environment and set both `MERIDIAN_RUN_ALPACA_ORDER_PROBE=1` and `ALPACA_TRADING_ENVIRONMENT=paper`, then invoke:

```bash
python3 scripts/dev/alpaca_paper_order_probe.py --enable-order-probe --account-id <paper-account-uuid>
```

The probe checks regular hours and a fresh bounded quote, submits one SPY share at half the bid, and cancels its own order as soon as cleanup finds it working under the retained identity. It then proves final positions and working orders are empty. Non-marketable prices reduce fill likelihood but cannot guarantee no fill. Unexpected fills are cleaned up only when the entire position is provably that probe's single-share SPY exposure. Lost acknowledgement is never retried as a new entry. Failure, ambiguity or process termination leaves a quarantine marker; independently reconcile the account before removing it. A failed submission does not become a passing probe just because cleanup succeeded.

## Verification

Focused regression coverage is in `ExecutionWorkflowTests`, `EquityOrderSessionPolicyTests`, `MovingAverageParityTests`, `ExecutionGovernanceEndpointsTests`, the existing Alpaca/risk/OMS suites, browser ticket/notification tests and `tests/scripts/test_alpaca_paper_order_probe.py`. The browser smoke checks use explicitly synthetic HTTP responses; they do not verify a provider connection or actual broker fill behavior. No paper order probe or webhook is sent as part of development verification.
