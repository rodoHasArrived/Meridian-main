# W9 Operator Acceptance Record — 2026-10-19

**Status:** active record
**Owner:** Core Team
**Accepting operator:** `rodoHasArrived` (repository owner)
**Decided on:** 2026-10-19
**Registry decision:** `DEC-W9-ACCEPTANCE-007` in
[`docs/roadmap/data/decision-log.yml`](../roadmap/data/decision-log.yml)

## What this record is

The roadmap [status taxonomy](../roadmap/status-taxonomy.md) moves a row to `accepted` only when
acceptance evidence exists and is linked from the roadmap item. This file is that evidence for
`W9-CORPACT-011`. It records who decided, what they accepted, and the reservations carried with the
decision.

It is not a release certification. The row does not move to `done`, and the P0
release-certification gate in [`implementation-todo-list.md`](implementation-todo-list.md) remains
open.

## History

- **2026-08-29, `DEC-W9-ACCEPTANCE-001`.** The row was first accepted on the understanding that
  unsupported and policy-dependent branches stay blocked. Review the same day found that no case
  could reach `ReadyForApproval`, so exit criterion four was unmet.
- **2026-08-30, `DEC-W9-ACCEPTANCE-002`.** The acceptance was reopened. Acceptance could be
  reconsidered only once the approval and posting lane was reachable, and criterion four was
  implemented and evidenced.
- **2026-09-22.** That condition was met, and the row reached `ready_for_acceptance`.

## The decision

On 2026-10-19 the repository owner instructed, in the
[PR #3190](https://github.com/rodoHasArrived/Meridian-main/pull/3190) working session, to accept
`W9-CORPACT-011` with the reservations presented and to record it the same way as `W9-GOV-008` and
`W9-INGEST-009`.

### W9-CORPACT-011 — durable corporate action case processing and accounting projection

Accepted. The five exit criteria and their evidence:

1. **Scoped case with separate layers.** A Corporate Action Case binds one canonical Security
   Master source event to exact tenant, account, book, basis, period and position scope. It keeps
   source facts, treatment decisions and generated consequences as separate authoritative layers
   ([domain model](../domain/corporate-action-case.md); `CorporateActionOperationsContractTests`).
2. **Persisted release gate.** `CorporateActionIngestOrchestrator` admits provider observations
   behind a persisted release gate that is rechecked at acceptance, so a review-only feed is never
   accepted as canonical fact (`CorporateActionIngestOrchestratorTests`).
3. **Durable, attributed, recoverable transitions.** `CorporateActionOperationsService` transitions
   are actor-attributed, optimistic-concurrency checked and idempotent. A failed acceptance or
   posting leaves the case recoverable in its prior state. `PostgresCorporateActionOperationsStore`
   and migrations 030 and 031 provide the durable store (`CorporateActionOperationsServiceTests`,
   `CorporateActionOperationsMigrationTests`).
4. **Governed posting.** Posting is refused without:
   - exact scope;
   - approved policy coverage;
   - an open period;
   - balanced journals and valid lot mutations;
   - retained evidence;
   - maker-checker approval.

   Posted journals and lot effects stay immutable; corrections add reversal, rebook or restatement
   lineage. `CorporateActionAccountingPostgresRoundTripTests` runs over the real Security Master,
   case, Asset Operations, accounting configuration and ledger stores. It proves:
   - attach and `ReadyForApproval`;
   - refusal of self-approval;
   - independent approval;
   - durable posting and replay from reconstructed services;
   - exactly one balanced journal carrying the resolved Security Master lineage;
   - the spine and case reloaded as `Posted`, with the 500-share lot unchanged.
5. **Golden-file coverage.** Projected accounting and backtest price adjustment are proven against
   worked examples (`CorporateActionGoldenLedgerTests`, `CorporateActionGoldenAdjustmentTests`).

**Reservations carried.**
- Unsupported and policy-dependent corporate-action branches stay deliberate blocked outcomes rather
  than being coerced into a generic sale or exchange. This is the intended operating envelope, not a
  gap.
- `PostCorporateActionAccounting` is granted to no built-in role. Before anyone can post, the
  permission must be assigned through a governed custom role profile.
- The evidence is the automated PostgreSQL round trip and golden-file coverage. No live-installation
  run was supplied or retained in this repository, and none is inferred.

## Unchanged by this record

- `DEC-W9-ACCEPTANCE-001` and `DEC-W9-ACCEPTANCE-002` remain the historical record for this row.
- No other W9 row changes status, health or evidence posture.
