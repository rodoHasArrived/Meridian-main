# Posting Control Checklist

Use this checklist when a reviewed operational fact can create or promote accounting journal
consequences.

## Required Inputs

- Event identity: tenant, organization/entity/portfolio/account/fund scope where applicable, book,
  period, event type, event id, correlation id, and causation id.
- Evidence: retained source evidence or explicit operator rationale, source version/hash, reviewer
  state, and audit link.
- Dates: effective date and posting date are both explicit.
- Controls: approval state, segregation-of-duties posture, period lock posture, idempotency key,
  materiality policy, and version/concurrency guard.
- Posting impact: balanced debit/credit lines, account ids, currency, amount precision, and blocked
  output links.

## Fail-Closed Conditions

- Missing source evidence or operator rationale.
- Missing approval or reviewer state.
- Locked or unresolved period posture.
- Missing idempotency key, stale source version, or missing concurrency guard.
- Unbalanced debit and credit totals.
- Correction path mutates posted journal facts instead of appending reversal/rebook, amendment, or
  restatement evidence.

## Expected Output

Return a posting-control matrix, disabled reasons, impacted seams, required tests, validation
commands, and residual posting risk.
