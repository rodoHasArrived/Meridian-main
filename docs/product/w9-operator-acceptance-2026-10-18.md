# W9 Operator Acceptance Record — 2026-10-18

**Status:** active record
**Owner:** Core Team
**Accepting operator:** `rodoHasArrived` (repository owner)
**Decided on:** 2026-10-18
**Registry decision:** `DEC-W9-ACCEPTANCE-006` in
[`docs/roadmap/data/decision-log.yml`](../roadmap/data/decision-log.yml)

## What this record is

The roadmap [status taxonomy](../roadmap/status-taxonomy.md) moves a row to `accepted` only when
acceptance evidence exists and is linked from the roadmap item. This file is that evidence for two
rows. It records who decided, what they accepted, and the reservations carried with the decision.

It is not a release certification. Neither row moves to `done`, and the P0 release-certification
gate in [`implementation-todo-list.md`](implementation-todo-list.md) remains open.

## The decision

On 2026-10-18 the repository owner instructed, in the PR #3190 working session:

> Accept W9-GOV-008, which also includes the tenancy cutover on a real installation.
> Accept W9-INGEST-009.

### W9-GOV-008 — route-level authorization, fail-closed tenancy, hash-chained audit

Accepted. The three exit criteria and their evidence:

1. **Route authorization coverage.** `EndpointAuthorizationCoverageTests`,
   `EndpointAuthorizationDeclarationTests` and `EndpointReadDeclarationTests` enumerate every
   mapped route, and their remediation baselines are empty sets. `MutationAuthorizationGuardMiddleware`
   enforces the declaration at runtime.
2. **Fail-closed tenancy.** `TenantScopeEnforcement` defaults to fail-closed, and
   `TenantReadPredicate` emits no `tenant_id is null` branch in that mode. Startup readiness blocks
   on missing, sentinel or inconsistent ownership. Legacy rows that cannot be attributed are retained
   as quarantine blockers rather than reattributed. The browser, WPF, background-worker and real
   PostgreSQL fixtures cover missing and mismatched authority, migration, quarantine resolution and
   restart readiness.
3. **Hash-chained accounting and ledger audit.**
   - The accounting-action chain runs on both the file-backed and the PostgreSQL store, with
     cross-process serialization.
   - The PostgreSQL ledger-event chain covers journal, period and close facts.
   - Tamper detection is covered by `FileAccountingAuditChainTests` and `LedgerEventAuditPostgresTests`.
   - Verification tooling is the read-only `GET /api/ledger/audit-integrity` route and the Accounting
     → Configuration "Audit chain integrity" panel, added in
     [PR #3190](https://github.com/rodoHasArrived/Meridian-main/pull/3190).

**Live-installation tenancy cutover.** The operator's instruction states that this acceptance
includes the tenancy cutover on a real installation. That cutover is recorded on the operator's
statement. No cutover log, installation identifier or post-cutover readiness output was supplied
or retained in this repository, and none is inferred. The bounded implementation proof of
2026-09-28 is unchanged.

**Reservations carried.**
- The verification tooling for criterion three lands with PR #3190. This record is merged in the
  same change, so the acceptance and the tooling it relies on reach `main` together.
- The ledger-event chain's own documented limits still apply. A coherent rollback of head, suffix
  and facts, or a coordinated rewrite of facts, event hashes and head, needs an external checkpoint
  to detect. A locally recomputed chain is not independent authentication of its history. The
  verification route carries those same limits.
- WPF parity for the audit-integrity panel is not part of this acceptance. The shared
  `AccountingAuditIntegrityService` seam is ready for it.

### W9-INGEST-009 — institutional file ingestion and sided reconciliation matcher

Accepted. The four exit criteria and their evidence:

1. **Connectors.** camt.053 and BAI2 normalize through the connector seam, with retained raw and
   canonical evidence and golden-file regression coverage. `StatementImportCaseworkEvidenceTests`
   runs the golden packs through `StatementImportService.CommitAsync`.
2. **Bounded parsing.** Both formats stream with the shared `StatementIngressLimits`. Each bound is
   proven as a named refusal in `StatementIngressLimitsTests`.
3. **Sided matching on the live path.**
   - `StatementMatchingEngine` routes every pair stage through
     `ReconciliationMatchKernel.SelectDeterministicAssignment`, with bounded one-to-many and
     many-to-one split matching.
   - Group membership survives into the durable match artifact.
   - The ledger-transaction population comes from `LedgerJournalInternalTransactionSource` under the
     run's accounting scope.
4. **Casework feed.** Sided results reach the shared `ReconciliationApiService` casework feed, with
   restart recovery from the retained checkpoint and artifact. This is evidenced in Targeted Test
   run 34596280479 and the 2026-09-25 recovery refresh.

**Reservation carried.** The casework evidence is shared-service and file-store evidence with a
controlled ledger-population boundary. It is not a PostgreSQL or production-certification claim,
and this acceptance does not convert it into one.

## Unchanged by this record

- `W9-CORPACT-011` receives no decision here. It remains `ready_for_acceptance` under
  `DEC-W9-ACCEPTANCE-002`.
- No other W9 row changes status, health or evidence posture.
