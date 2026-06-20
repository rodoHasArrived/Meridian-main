---
name: meridian-event-accounting-architecture
description: Design, review, and implement Meridian event-based accounting architecture. Use when work involves event-sourced accounting, fund-event posting flows, immutable journals, ledger projections, reversal/rebook, double-entry invariants, accounting evidence graphs, close/reporting handoff, or specialist review of ledger architecture across src/Meridian.Ledger, src/Meridian.FSharp.Ledger, src/Meridian.FinancialOperations, src/Meridian.Ui.Shared, browser, WPF, tests, and docs.
---

# Meridian Event Accounting Architecture

Use this skill to keep event-based accounting work grounded in Meridian's operational record spine.
Read `../_shared/project-context.md`, `../_shared/codex-execution-contract.md`,
`docs/architecture/meridian-development-intelligence-framework.md`,
`docs/ai/context/accounting-context.md`, and `docs/ai/context/operational-evidence-context.md`
before architecture-sensitive accounting changes.

For external background, read `references/event-accounting-patterns.md` when the task needs
event-sourcing or immutable-ledger rationale beyond repository context.

## Use When

Use this skill for:

- Event-sourced accounting designs, fund-event posting pipelines, journal/event mapping, or
  projection/replay behavior.
- Ledger, subledger, capital-account, close, reconciliation, report-line, or audit-evidence flows
  where events become accounting records.
- Reviews of idempotency, ordering, period locks, approvals, immutable journal entries,
  reversal/rebook, restatement, and balance projection correctness.
- Cross-surface accounting work that must keep browser and WPF clients thin over shared services,
  read models, and contracts.

Trigger examples:

- "Design event based accounting for capital calls and distributions."
- "Review this ledger projection for event-sourcing and audit risks."
- "Add a fund-event posting architecture that creates journal entries and report-line provenance."

## Do Not Use When

Do not use this skill for:

- General architecture review without accounting/event-ledger concerns; use `meridian-code-architecture`.
- DTO or endpoint compatibility tracing without accounting semantics; use `meridian-contract-governance`.
- Ordinary UI composition for accounting screens; use `meridian-browser-workstation` or
  `modular-desktop-mvvm`, then hand off here only for posting/control semantics.
- Provider ingestion or market-data workflows before they affect accounting evidence or ledger state.

Non-trigger examples:

- "Review the Strategy module dependency direction."
- "Build a dense WPF grid for cash balances."
- "Add an Alpaca historical-data provider."

## Workflow

1. Load the Meridian accounting spine:
   `docs/architecture/meridian-domain-model.md`, `docs/architecture/module-map.md`,
   `docs/ai/context/accounting-context.md`, `docs/ai/context/operational-evidence-context.md`,
   `docs/domain/fund-event.md`, and `docs/domain/operational-evidence-graph.md`.
2. Identify the accounting source of truth:
   `src/Meridian.Ledger`, `src/Meridian.FSharp.Ledger`, and
   `src/Meridian.FinancialOperations` own posting rules, accounting semantics, and close controls.
   UI shared, browser, WPF, reporting, and storage surfaces may project or persist snapshots, but
   must not redefine balance math or posting policy.
3. Model domain events as reviewed operational facts. Require source evidence or explicit operator
   rationale, effective date, posting date, approval state, period posture, idempotency key,
   correlation/causation identifiers, and version/concurrency guard before accounting impact.
4. Translate events into immutable accounting consequences. Posted journal entries are append-only;
   corrections use reversal/rebook, amendment, or restatement records that preserve the original
   event and evidence chain.
5. Separate the event log, posting command, journal record, projection/read model, report usage, and
   audit evidence. Projections may be rebuilt; posted journal facts and retained evidence are the
   durable accounting record.
6. Prove double-entry and ordering invariants with focused tests. Include idempotent replay,
   out-of-order or duplicate event handling, balanced debit/credit totals, lock/approval failures,
   and projection rebuild behavior.
7. Keep operator surfaces thin. Browser and WPF should consume shared endpoint/read-model state,
   expose validation and disabled reasons, and route posting actions through shared services.

## Handoffs

Use `meridian-event-accounting-architecture` as the accounting-semantics owner, then hand off to
these existing complementary lanes when their trigger condition appears:

| Complement | Trigger | Expected output | Validation owner |
| --- | --- | --- | --- |
| `meridian-accounting-posting-controls` | Posting command, journal draft, approval gate, period lock, idempotency, or reversal/rebook control is central | Posting-control matrix, disabled reasons, and fail-closed test scenarios | Posting controls lane |
| `meridian-ledger-projection-replay-review` | Projection rebuild, replay ordering, duplicate/out-of-order events, balance drift, or report handoff is central | Replay risk map, rebuild assumptions, and projection test scenarios | Projection/replay lane |
| `meridian-contract-governance` | Event, journal, read-model, endpoint, or API compatibility changes | Contract impact map and compatibility strategy | Contract governance lane |
| `meridian-test-writer` | Ledger, replay, reversal/rebook, projection, or close scenarios need proof | Scenario-first test plan or tests | Test lane plus event-accounting owner |
| `diagnostics-audit-timeline` | Evidence chain, replay trace, audit timeline, or recovery surface is central | Evidence/recovery timeline and missing provenance findings | Diagnostics/audit lane |
| `meridian-implementation-assurance` | Rollout proof, docs sync, validation evidence, or residual risk is required | Assurance summary with validation evidence and residual risk | Implementation assurance lane |
| `meridian-code-architecture` | Module boundary, dependency direction, ADR, or source-doc alignment is in doubt | Architecture finding map and boundary recommendation | Code architecture lane |

- Hand off to `meridian-contract-governance` when event, journal, read-model, or endpoint contracts
  change across consumers.
- Hand off to `meridian-code-architecture` for broader dependency, ADR, or module-boundary review.
- Hand off to `meridian-test-writer` for scenario-first ledger/replay tests.
- Hand off to `meridian-implementation-assurance` for rollout proof, docs sync, and residual risk.
- Hand off to `diagnostics-audit-timeline` when the task mainly presents evidence or recovery state.
- Pair with `meridian-user-testing-fund-accountant`, `meridian-user-testing-auditor`,
  `meridian-user-testing-controller`, `meridian-user-testing-reconciliation-analyst`, or
  `meridian-user-testing-reporting-analyst` when operator trust, blocked posting comprehension,
  close/reporting clarity, or audit-readiness is part of acceptance.

## Validation

- Run `python .codex/skills/meridian-event-accounting-architecture/scripts/accounting_architecture_check.py --summary`
  after changing this skill or using it to audit a design note.
- Run `python .codex/skills/meridian-event-accounting-architecture/scripts/run_evals.py --all --dry-run --summary`
  for deterministic package evals.
- Run `python .codex/skills/meridian-codex-skill-builder/scripts/skill_package_audit.py --skill meridian-event-accounting-architecture --summary`
  after package changes.
- For source changes, add the narrowest ledger, Financial Operations, UI shared, browser, or WPF
  test that covers the touched posting or projection path.
- Run `python build/scripts/docs/check-codex-skills.py --summary`,
  `python build/scripts/docs/check-ai-inventory.py --summary`, and
  `python build/scripts/docs/prompt-route-linter.py --summary` when discovery or route rules change.

## Output Standards

- State the selected accounting event, its source evidence, approval/period gates, and posting
  consequences.
- Distinguish immutable facts from rebuildable projections and UI snapshots.
- Name every cross-module seam touched: contracts, services, read models, endpoints, WPF/browser
  consumers, tests, and docs.
- Report invariant coverage: balance, idempotency, ordering, replay, reversal/rebook, evidence,
  approvals, period locks, and audit trail.
- Include external research only as rationale; Meridian source docs and contracts remain
  authoritative when they disagree.
