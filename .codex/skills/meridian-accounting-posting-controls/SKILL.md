---
name: meridian-accounting-posting-controls
description: Review Meridian accounting posting controls for event-to-journal commands, approval gates, period locks, idempotency, stale-version/concurrency guards, double-entry balance, source evidence, reversal/rebook, and fail-closed posting safeguards. Use when work changes or reviews posting commands, journal drafts, ledger writes, capital-call/distribution posting, close-blocking accounting controls, or accounting approval paths before implementation or rollout.
---

# Meridian Accounting Posting Controls

Use this skill to review the highest-risk posting-control path between reviewed fund events and
immutable journal consequences. Read `../_shared/project-context.md`,
`../_shared/codex-execution-contract.md`, `../meridian-event-accounting-architecture/SKILL.md`,
`docs/ai/context/accounting-context.md`, and `docs/ai/context/operational-evidence-context.md`
before architecture-sensitive posting changes.

Read `references/posting-control-checklist.md` when the task needs a reusable checklist for
approval gates, period posture, idempotency, concurrency, or reversal/rebook controls.

## Use When

Use this skill for:

- Fund-event posting commands, journal draft promotion, ledger write gates, or accounting policy
  checks before a record can affect balances.
- Capital call, contribution, distribution, fee, expense, valuation, or close adjustment posting
  flows that must retain source evidence and explicit approval state.
- Reviews of double-entry balance, effective date, posting date, period lock posture, stale-version
  guard, idempotency key, segregation-of-duties, reversal/rebook, amendment, and restatement paths.
- Designs that need a fail-closed posting-control matrix before contract governance, test writing,
  or implementation assurance.

Trigger examples:

- "Review this accounting posting command for missing approval and idempotency controls."
- "Design posting controls for capital call and distribution journal drafts."
- "Check whether reversal/rebook can bypass period locks or source evidence."

## Do Not Use When

Do not use this skill for:

- Projection rebuild, replay ordering, or read-model drift without a posting gate concern; use
  `meridian-ledger-projection-replay-review`.
- Broad event-accounting architecture before a posting-control lane is selected; use
  `meridian-event-accounting-architecture`.
- DTO or endpoint compatibility tracing without posting semantics; use `meridian-contract-governance`.
- UI-only presentation of posting state; use a UI lane, then hand off here only for control semantics.

Non-trigger examples:

- "Review a React table for accounting balances."
- "Trace DTO consumers for a new ledger endpoint."
- "Check projection rebuild performance after replay."

## Workflow

1. Load the accounting context, operational evidence context, fund-event dictionary, and module map.
2. Identify the posting command, event type, source evidence, journal consequence, and owner module:
   `src/Meridian.Ledger`, `src/Meridian.FSharp.Ledger`, or `src/Meridian.FinancialOperations`.
3. Build a posting-control matrix with required evidence, approval state, period posture,
   idempotency key, correlation/causation identifiers, effective date, posting date, and
   version/concurrency guard.
4. Verify double-entry and balance impact before posting. Missing source evidence, reviewer state,
   period posture, idempotency, or stale-version guard must fail closed.
5. Confirm corrections append reversal/rebook, amendment, or restatement facts and never mutate
   posted journal entries in place.
6. Return blocked outputs, disabled reasons, impacted seams, narrow test scenarios, validation
   commands, and residual posting risk.

## Handoffs

- Hand off to `meridian-event-accounting-architecture` when the posting question expands into
  broader event-sourced accounting design.
- Hand off to `meridian-contract-governance` when posting command, journal, endpoint, or read-model
  contracts change.
- Hand off to `meridian-test-writer` for scenario-first posting, approval, period lock,
  idempotency, reversal/rebook, and double-entry tests.
- Hand off to `diagnostics-audit-timeline` when blocked posting, recovery, or evidence timeline
  presentation is the main concern.
- Hand off to `meridian-implementation-assurance` for rollout proof, docs sync, and residual risk.

## Validation

- Run `python .codex/skills/meridian-accounting-posting-controls/scripts/posting_controls_check.py --summary`
  after changing this skill or using it to audit a design note.
- Run `python .codex/skills/meridian-accounting-posting-controls/scripts/run_evals.py --all --dry-run --summary`
  for deterministic package evals.
- Run `python .codex/skills/meridian-codex-skill-builder/scripts/skill_package_audit.py --skill meridian-accounting-posting-controls --summary`
  after package changes.
- For source changes, add the narrowest ledger, Financial Operations, UI shared, browser, or WPF
  test that covers the touched posting-control path.
- Run `python build/scripts/docs/check-codex-skills.py --summary`,
  `python build/scripts/docs/check-ai-inventory.py --summary`, and
  `python build/scripts/docs/prompt-route-linter.py --summary` when discovery or route rules change.

## Output Standards

- State the posting event, source evidence, approval gate, period posture, idempotency key, and
  version/concurrency guard.
- Distinguish draft commands, blocked outputs, immutable posted facts, and rebuildable projections.
- Report invariant coverage: double-entry balance, source evidence, approval, period locks,
  idempotency, ordering/correlation, stale-version guard, reversal/rebook, amendment/restatement,
  audit trail, and disabled reasons.
- Name every impacted seam: contracts, services, read models, endpoints, WPF/browser consumers,
  tests, docs, and assurance handoff.
