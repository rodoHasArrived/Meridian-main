# Account oversight evidence

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-14

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

Within that exact scope, a tolerance profile ID and version bind the complete retained rules;
changing those rules requires a new version, even across different source pairs. Owner assignments
cannot precede the latest case observation or assignment. Rejected policy changes and backdated
assignments leave retained history unchanged.

Independence requires distinct retained evidence references and original observations, not just
different source labels/types or equal cash amounts. Shared references (ignoring surrounding
whitespace) and copied original row payloads are rejected before retention. For each exact
tenant/company/account/book scope, a source ID and version bind the complete retained source:
type, evidence reference, capture time, completeness, normalized rows and original payloads.
Reusing a version with changed content is rejected against all earlier runs under the same
cross-process store lease that appends a run. Source corrections need a new version; unchanged
revisions may be reused. A rejected attempt leaves existing runs, ownership and case history intact.
These checks identify retained observations; they do not independently authenticate an external
document supplied by a caller.

An approved close-price set binds one account, book and valuation date to retained selection
receipts, quotation units, reviewer rationale and overrides. Independent review freezes the set.
Corrections produce linked versions, while unsupported or stale inputs remain explicit blockers.
The pricing hierarchy remains Security Master's selection authority.
Unrepresentable derived dispersion remains an explicit blocker with the exact observations retained.
An optional stable creation request identity binds the normalized command to its immutable
snapshot. An identical retry returns the retained version and approval before reading later
quotes; conflicting command reuse fails. Optional command metadata is omitted from legacy
serialization so existing snapshot and approval digests remain unchanged.

An account cash forecast separates expected economics from observed cash receipts. Economic,
settlement and receipt dates are distinct; dated declared FX specifies base units per native unit.
Only validated observed flows reconcile opening settled cash to observed closing cash.
Opening and closing balances require separately identifiable source observations; the same source
bytes or retained evidence identity/version cannot establish both boundaries of a single-day
period. Independently retained equal balances are valid. Linked expected and observed flows must
agree on security identity, including its absence on both sides.
Corrections and factor restatements retain earlier predictions. Certification here is evidence
reconciliation and never authorizes payment execution.
Valid retained receipts replay unchanged. Earlier certifications that reused a balance observation
or linked asymmetric security identities fail replay closed; review the original evidence and
retain an explicitly linked correction rather than rewriting the prior receipt.
Temporarily omitting a flow does not reset its revision history: a later correction links its last
retained revision of the same flow and expected/actual status, while the snapshot links the latest run.

These are bounded first deliverables for `W10-RECON-001`, the extension of `W10-MARK-001`, and
the existing shared cash ladder. They do not establish wider roadmap or live-acceptance completion.
