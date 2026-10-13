# Account oversight evidence

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-13

Account oversight retains comparisons, valuation inputs and cash predictions independently of
ledger posting or payment execution. Exact tenant, company, account and book scope is mandatory.

A cash-balance continuity case compares two independently identified, complete sources for one
currency. Its identity excludes amounts, source row IDs, run dates and tolerance versions. Each
observation retains the original source payload, normalized row, executed tolerance and matcher
evidence. Corrected evidence changes the same case to `SourceCleared`; this records a source
resolution and does not grant governed case sign-off or period-close authority. A recurrence
reopens the same case and retains previous resolutions. Aging uses elapsed UTC calendar days
from the first observed break to the latest observation, or source resolution while cleared.
Missing balances on both sides cannot clear an existing case. Historical replay uses retained
inputs and tolerance rather than current configuration.

An approved close-price set binds one account, book and valuation date to retained selection
receipts, quotation units, reviewer rationale and overrides. Independent review freezes the set.
Corrections produce linked versions, while unsupported or stale inputs remain explicit blockers.
The pricing hierarchy remains Security Master's selection authority.

An account cash forecast separates expected economics from observed cash receipts. Economic,
settlement and receipt dates are distinct; dated declared FX specifies base units per native unit.
Only validated observed flows reconcile opening settled cash to observed closing cash.
Corrections and factor restatements retain earlier predictions. Certification here is evidence
reconciliation and never authorizes payment execution.

These are bounded first deliverables for `W10-RECON-001`, the extension of `W10-MARK-001`, and
the existing shared cash ladder. They do not establish wider roadmap or live-acceptance completion.
