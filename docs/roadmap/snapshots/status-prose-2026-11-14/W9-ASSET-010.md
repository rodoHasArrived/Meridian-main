# W9-ASSET-010 status-detail migration snapshot

Captured from `docs/roadmap/data/roadmap-items.yml` at `3503ae000e` on 2026-11-14.
This retains the original implementation detail, reservations and historical claims verbatim.
Read the current registry for status and decisions; this snapshot does not grant new acceptance.
Detailed boundaries remain applicable until a later scoped change replaces them.

```yaml
    current_summary: Completed 2026-07-28. One evidence-backed Asset Accounting Event Spine covers acquisition, capitalization, valuation, income, corporate action, impairment, depreciation/amortization, and disposal, resolving Security Master identity, versioned book position, ledger book, period, accounting basis, promoted rule pack, projection lineage, and complete typed retained evidence before candidate drafting. Expected, Projected, Drafted, Approved, Posted, Reconciled, and Reported remain distinct lifecycle states; only a retained immutable journal establishes Posted impact. Acquisition lot creation and versioned selected-lot disposal share one idempotent serializable journal-plus-lot transaction, and readiness and UI projections fail closed when retained evidence identity, hash, source, review, effective-date, version, or scope is incomplete. Focused contract, spine, storage, endpoint, shared-read-model, and readiness suites are green ahead of the authoritative GitHub Actions quality gate.
```
