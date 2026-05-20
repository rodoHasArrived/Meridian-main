# Provider Degradation Policy

**Last Updated:** 2026-05-20  
**Scope:** Runtime provider failover, operator readiness posture, and escalation governance for streaming and historical provider degradation.

## Purpose

This policy defines how Meridian detects provider degradation, executes fallback behavior, projects operator-visible readiness impact, and escalates unresolved incidents. It is intentionally bound to implemented runtime components so operator and engineering decisions reflect live system behavior.

## Runtime Components and Behavior Mapping

- **`StreamingFailoverService`** is the runtime orchestrator for streaming failover rules. It evaluates provider health on a timer, tracks consecutive failures/successes, and switches active providers for a rule when thresholds are breached. `EnableFailover=false` or zero configured rules means failover does not activate.  
  _Implementation anchor:_ `src/Meridian.Infrastructure/Adapters/Failover/StreamingFailoverService.cs`.
- **`FailoverAwareMarketDataClient`** delegates streaming actions to the current active provider for a rule and records success/failure signals into the failover service. On provider failure during connect/subscribe, it attempts immediate failover provider connection before returning failure.  
  _Implementation anchor:_ `src/Meridian.Infrastructure/Adapters/Failover/FailoverAwareMarketDataClient.cs`.
- **`CompositeHistoricalDataProvider`** handles historical backfill fallback by chaining providers in priority order and moving to the next provider when one is unavailable/unhealthy.  
  _Implementation anchor:_ `src/Meridian.Infrastructure/Adapters/Core/CompositeHistoricalDataProvider.cs`.
- **`ProviderRegistry`** resolves the “best available” historical/search providers based on priority and health checks; this is the main non-streaming fallback selector.  
  _Implementation anchor:_ `src/Meridian.Infrastructure/Adapters/Core/ProviderRegistry.cs`.
- **`FailoverEndpoints`** expose both configuration and live runtime state (when `StreamingFailoverRegistry` is present), including active rule provider and provider health/degradation score snapshots.  
  _Implementation anchor:_ `src/Meridian.Ui.Shared/Endpoints/FailoverEndpoints.cs`.

## Trigger Conditions

A provider degradation incident is considered active when one or more of the following conditions are true:

1. **Consecutive failures reach threshold** for an active failover rule (`ConsecutiveFailures >= FailoverThreshold`).
2. **Latency breach** occurs for a rule with configured `MaxLatencyMs` and average latency exceeds that bound.
3. **Heartbeat degradation** is detected (`OnHeartbeatMissed` with missed count >= 2), contributing failure records.
4. **Connection loss events** are raised by the connection health monitor and recorded as failures.
5. **Provider-scoring risk elevation** is observed in operator diagnostics via degradation score reasons, even when formal failover has not yet switched providers.

## Fallback and Rollback Selection Policy

### Streaming fallback selection

For each active failover rule:

- The selection order is **primary first, then backups in configured order**.
- On threshold breach, the service chooses the **next provider whose consecutive failures are below threshold**; untracked providers are treated as not-yet-failed and eligible.
- If no eligible provider remains, the rule enters **provider-exhausted state** and emits operational error logs; no automatic provider is available.

### Streaming rollback (auto-recovery)

- When in failover state, if the primary provider reaches `RecoveryThreshold` consecutive successes, the runtime automatically switches back to primary.
- Manual rollback/failover is also supported through `ForceFailover` API/operations tooling when runtime service is available.

### Historical/backfill fallback

- Historical provider fallback must use `CompositeHistoricalDataProvider` and `ProviderRegistry` priority + health-based selection.
- Any provider participating in historical workflows must declare an explicit fallback order and expected degraded-mode behavior (e.g., reduced granularity, delayed retries, or bounded “empty” responses).

## Operator-Visible Readiness Status Impact

Provider degradation must surface in operator-visible readiness and diagnostics as follows:

- **Failover state visibility:** APIs under failover endpoints must show whether each rule is currently in failover and which provider is active.
- **Health and degradation diagnostics:** Provider health responses must include consecutive failure/success counters and degradation score signals when available.
- **Readiness warning posture:** Trading/operator readiness projections must treat unresolved provider degradation as warning or blocking evidence depending on gate impact (for example, inability to maintain replay/readiness data fidelity).
- **Exhausted failover escalation:** If all providers for a rule are exhausted, readiness must be treated as degraded until manual intervention restores healthy coverage.

## Escalation Paths

Escalation is mandatory when any condition below is met:

1. **Automatic failover exhausted** (no healthy backup available).
2. **Repeated failover oscillation** (multiple failovers in short succession without stable recovery).
3. **Readiness impact persists** beyond one operations cycle or blocks paper/live promotion checks.
4. **Manual force failover fails** due to missing runtime failover service or invalid target provider mapping.

Escalation sequence:

- **L1 Operator:** confirm active provider/rule state via failover APIs and readiness endpoints; capture timestamped evidence.
- **L2 Platform Operations:** validate configuration (`EnableFailover`, rules, thresholds), provider credentials/connectivity, and fallback ordering correctness.
- **L3 Engineering On-Call:** investigate adapter/runtime defects, threshold miscalibration, or provider-side instability; patch and validate with focused test evidence.
- **Governance Sign-Off:** for promotion-impacting incidents, attach updated evidence artifacts and record disposition in provider validation evidence bundle.

## Capability Matrix Policy Requirement

Every provider row in `docs/status/provider-validation-matrix.md` must include:

1. **Declared fallback strategy** (primary backup path, order, and degraded-mode expectation).
2. **Declared rollback strategy** (auto-recovery threshold behavior and/or manual rollback procedure).
3. **Validation status** (`✅ Closed`, `⚠️ Bounded`, or explicit blocker posture) linked to executable evidence.

Rows missing fallback/rollback declaration or validation status are **policy-incomplete** and cannot be used as a promotion-ready provider claim.

## Validation and Change Control

When changing failover/degradation behavior:

- Update failover rule/config and runtime verification evidence.
- Run narrowest focused tests that cover changed adapters/orchestration endpoints.
- Update both this policy and provider validation matrix in the same change set when behavior, thresholds, or escalation contracts change.
