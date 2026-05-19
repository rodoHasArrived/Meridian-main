# Provider Integration Status Board

**Run Date:** 2026-05-19  
**Source snapshots:** `docs/status/provider-validation-matrix.md` (Last Updated 2026-04-27), `docs/status/kernel-readiness-dashboard.md` (Last Updated 2026-04-27)  
**Purpose:** single status document that shows each broker/provider phase, blockers, latest evidence timestamp, and refresh ownership/cadence for active integrations.

## How to read this board

- **Phase:**
  - **Read-only** = data ingestion/research use only; no order routing.
  - **Paper** = simulation/paper-trading workflows active; no production funds.
  - **Production** = approved for production-path execution within Meridian governance gates.
- **Latest evidence timestamp (UTC):** the most recent dated evidence run or snapshot currently referenced by source-of-truth status documents.
- **Blockers:** explicit conditions that must be resolved before phase promotion.

## Unified Provider Status

| Provider/Broker | Current phase | Blockers to next phase | Latest evidence timestamp (UTC) | Evidence sources |
| --- | --- | --- | --- | --- |
| Alpaca | Paper | Production promotion checklist not yet represented in the active DK2 gate set and operator sign-off packet flow | 2026-04-27 | Wave 1 closure row in provider validation matrix; DK readiness board references the 2026-04-27 automation evidence pack |
| Robinhood | Paper (bounded) | Unofficial API posture plus required manual broker-session/runtime evidence (`auth-session`, `quote-polling`, `order-submit-cancel`, `throttling-reconnect`) must be regenerated/attached for the review run | 2026-04-27 (latest signed Wave 1 packet set); bounded scenario packet noted as not retained in current repo | Robinhood bounded row in provider validation matrix; DK readiness board inherits Wave 1 packet date |
| Yahoo Finance (historical/fallback) | Read-only | No execution lane; keep scoped to historical/fallback provider role unless roadmap explicitly expands scope | 2026-04-27 | Yahoo historical/fallback row in provider validation matrix; DK readiness board links same Wave 1 evidence window |
| Interactive Brokers | Read-only (deferred from active Wave 1 gate) | Deferred-provider status in active gate; no current Wave 1 closure claim | 2026-04-27 snapshot date for current gate posture | Deferred-provider note in provider validation matrix |
| Polygon | Read-only (deferred from active Wave 1 gate) | Deferred-provider status in active gate; no current Wave 1 closure claim | 2026-04-27 snapshot date for current gate posture | Deferred-provider note in provider validation matrix |
| NYSE | Read-only (deferred from active Wave 1 gate) | Deferred-provider status in active gate; no current Wave 1 closure claim | 2026-04-27 snapshot date for current gate posture | Deferred-provider note in provider validation matrix |
| StockSharp | Read-only (deferred from active Wave 1 gate) | Deferred-provider status in active gate; no current Wave 1 closure claim | 2026-04-27 snapshot date for current gate posture | Deferred-provider note in provider validation matrix |

## Ownership and refresh cadence (active integrations)

| Surface | Primary owner | Backup owner | Minimum refresh cadence | Refresh trigger |
| --- | --- | --- | --- | --- |
| `docs/status/provider-integration-status.md` (this board) | Data Operations & Provider Reliability owner | Trading Workstation owner | **Twice weekly during active integrations** (Monday + Thursday UTC) | Any provider phase move, blocker state change, new bounded-runtime evidence, or operator sign-off state change |
| `docs/status/provider-validation-matrix.md` | Data Operations & Provider Reliability owner | Shared Platform Interop owner | Weekly minimum (or same-day when evidence changes) | New `run-wave1-provider-validation.ps1` output, DK1 packet/sign-off updates, or deferred-provider scope changes |
| `docs/status/kernel-readiness-dashboard.md` | Trading Workstation owner | Governance/Fund Ops owner | Weekly minimum (Mon cadence rule already defined) | Any gate-status/readiness change, operator-sign-off movement, or milestone target-date update |

## Refresh workflow

1. Run provider evidence generation for the current run date (`yyyy-mm-dd`) and collect artifacts under `artifacts/provider-validation/_automation/<yyyy-mm-dd>/`.
2. Update `provider-validation-matrix.md` with new evidence references and bounded/manual notes.
3. Update `kernel-readiness-dashboard.md` if any gate/readiness/commitment state changes.
4. Update this status board last so phase + blockers + timestamps reflect those two source snapshots.
5. Validate consistency with `python3 scripts/check_program_state_consistency.py` before publishing status updates.

## Run-date accuracy rule

- Do not advance any `Latest evidence timestamp (UTC)` in this board unless the corresponding dated artifact or snapshot update exists.
- If a source snapshot still points to an older signed packet (for example 2026-04-27), preserve that date here and list the current blocker rather than inferring freshness.
