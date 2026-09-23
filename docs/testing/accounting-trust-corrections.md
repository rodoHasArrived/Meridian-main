# Accounting trust correction acceptance

**Status:** implementation validation; operator acceptance pending  
**Owner:** Accounting and Ledger / Security / Workstation  
**Reviewed:** 2026-09-22

This checklist covers the accounting-trust correction branch. It does not accept a roadmap
row, authorize a production migration, or certify a release. The roadmap registry and the
production-readiness tracker retain those responsibilities.

| Area | Required behavior | Evidence to retain |
| --- | --- | --- |
| Effective-date lots | A delayed paydown uses quantity held at the event's effective date, including lots disposed after that date. Missing retained mutation dates block the candidate. Current stored quantities are not rewritten by this read. | Partial and full later-disposal cases, replay, incomplete-history refusal, and PostgreSQL execution results. |
| Tenant attribution | Reviewed migration derives tenants from retained authority, persists inferred legacy account nodes atomically, rejects unscoped sentinels, and replays committed receipts without reacquiring mutation locks. Workers obey the final configured tenant posture. | Dry-run hash, independent review reference, transaction/replay/concurrency tests, and strict-worker lifecycle tests. Do not apply the migration to production as part of this checklist. |
| First trusted close | Imported statement scope stays bound to its workflow, account, book and period. Report readiness comes from retained server evidence and the canonical scoped journal; postings outside Operations invalidate prior review. The compatibility Operations close command uses the Accounting Close coordinator and its controller, period, approval and evidence checks. | One statement-to-report walkthrough plus stale, missing, foreign-scope and denied-controller cases. |
| Cash flows and valuation | Missing contractual economics are blocked, normalized analytical schedules cannot post, lifecycle bindings are explicit, and price selection retains effective-date, knowledge-cutoff and quote-unit meaning. | Asset-class term fixtures, real-zero versus missing-rate cases, hierarchy/price history and unit tests. |
| Reconciliation and proof | Stable lineage survives changed amounts and run dates. Recurrence has a new occurrence identity. Unavailable populations, changed comparison policies and older runs cannot clear newer observations; comparison clearing does not resolve governed casework. Amount proof uses retained subject identity. | Restart, replay, recurrence, failed-run and scope tests, plus keyboard and missing-evidence checks for the proof drawer. |
| Trading calendar | Completeness, heatmap, recommendations and exports use one operational calendar policy. | Juneteenth, observed holidays, the 2026/2027 boundary and an injected shared closure policy. |
| Certification discovery | Every ledger database fact belongs to the integration selection. Date fixtures test the intended behavior within a valid posting period. | Test discovery guard plus zero-skip PostgreSQL TRX results from the actual tested commit. |

## Operator walkthrough

1. Use a designated non-production tenant, fund account, primary ledger book and open period.
   Record the source statement hash and the exact returned workflow and accounting scope.
2. Import the statement and review the resulting breaks. Repeat with a changed amount and date;
   verify lineage continuity. A failed run must leave the prior observation open. A cleared break
   that recurs must retain its lineage and acquire a distinct occurrence.
3. Open an amount's proof. Check the retained subject and evidence identifiers. Remove or stale
   the supporting evidence in the test fixture and verify a visible refusal, not implied proof.
4. Resolve the correction through the existing maker/checker path. Generate retained report
   support and verify that a caller-supplied readiness flag cannot substitute for it.
5. Attempt close as an unauthorized role, with a wrong-scope package, and after a material
   prerequisite change. Each must fail with a repairable reason. Rebuild support, review it and
   use the Accounting Close controller path for the authorized close.
6. Verify the final period state, retained report handoff and proof package agree on the same
   tenant, company, fund, account, book and period. Reconstruct services and replay the operation;
   no duplicate posting, close or evidence record may appear.

Exercise the affected browser interface at desktop and smaller-laptop sizes and the shared
workflow through the WPF client where available. Automated service tests are not a substitute
for the recorded operator decision or installed Windows acceptance.

## Remaining roadmap boundaries

The dated-lot correction does not enable AverageCost basis redistribution, amortization basis
adjustments, or corporate-action predecessor/successor mutations. Those require coherent typed
mutation, persistence, projection and approval support. Their existing unsupported-operation
guards remain authoritative. Likewise, this branch does not declare complete multi-asset
valuation coverage or infer historical data that was never retained.

Run the canonical repository CI and the relevant focused suites, then obtain PostgreSQL
certification on the same commit. Report blocked execution and skipped database tests separately
from passing checks. Preserve source changes and human-review status independently of any
environment-specific validation limitation.
