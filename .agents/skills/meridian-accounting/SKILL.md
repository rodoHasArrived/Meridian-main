---
name: meridian-accounting
description: Design and review Meridian event-based accounting, posting controls, and ledger projection/replay, and answer ledger questions through the read-only ledger MCP tools. Use when work involves fund-event posting, journal drafts, immutable journals, double-entry invariants, approval gates, period locks, idempotency, reversal/rebook, ledger projections, replay ordering, balance drift, close or report-line provenance, or code in src/Meridian.Ledger, src/Meridian.FSharp.Ledger, or src/Meridian.FinancialOperations. Also use for questions about a strategy run's ledger or the posted accounting books, such as a trial balance, journal, running balance, ledger integrity, or posting completeness.
license: See repository LICENSE
compatibility: >
  Portable Agent Skill package for Agent Skills-compatible hosts. Reads markdown references
  on demand; edits accounting source, tests, and docs only when the host permits filesystem
  writes. Ledger data reads require the Meridian MCP host and an authorized workstation session.
metadata:
  owner: meridian-ai
  version: "1.0"
  spec: open-agent-skills-v1
---

# Meridian Accounting

Keep accounting work grounded in Meridian's operational record spine: reviewed events become
balanced, approved, immutable journal facts, and everything downstream is a rebuildable projection.

Read `../_shared/project-context.md` first. Before architecture-sensitive accounting changes, also
read `docs/ai/context/accounting-context.md`, `docs/ai/context/operational-evidence-context.md`,
`docs/domain/fund-event.md`, `docs/domain/operational-evidence-graph.md`,
`docs/architecture/meridian-domain-model.md`, and `docs/architecture/module-map.md`.

This is the Claude-side counterpart to three Codex skills:
`meridian-event-accounting-architecture`, `meridian-accounting-posting-controls`, and
`meridian-ledger-projection-replay-review` under `.codex/skills/`. It carries their host-neutral
workflow; their evals and check scripts stay Codex-only.

## Use When

- Designing or changing event-based accounting: fund-event posting pipelines, event-to-journal
  mapping, double-entry rules, reversal/rebook, close, or report-line provenance.
- Reviewing a posting path: commands, journal drafts, ledger writes, approval gates, period locks,
  idempotency, stale-version guards, or segregation of duties.
- Reviewing projections and replay: read-model rebuilds, duplicate or out-of-order events,
  projection versioning, balance drift, or close and reporting handoff.
- Answering a question from ledger data: a run's trial balance, journal, account balances, ledger
  integrity, or whether the posted books are complete.

Trigger examples:

- "Design event-based accounting for capital calls and distributions."
- "Review this posting command for missing approval and idempotency controls."
- "Check this ledger projection for duplicate and out-of-order replay risk."
- "What is the trial balance for run 42 at month end?"

## Do Not Use When

- General bug or architecture review with no accounting semantics: use `meridian-code-review`.
- Accounting screens where only presentation changes: use `meridian-browser-workstation` for the
  browser, or follow the WPF guidance in `src/Meridian.Wpf/`. Come back here only for posting or
  projection semantics.
- Market-data ingestion or providers before they affect ledger state: use
  `meridian-provider-builder`.
- A finished change that only needs proof and docs sync: use `meridian-implementation-assurance`.

Non-trigger examples:

- "Review the Strategy module dependency direction."
- "Build a dense grid for cash balances."
- "Add an Alpaca historical-data provider."

## Pick a Mode

| Mode | Choose it when | Checklist |
|---|---|---|
| Architecture | The event, journal, projection, or evidence model itself is being designed or changed | `references/event-accounting-patterns.md` |
| Posting controls | The risk is between a reviewed event and a posted journal: approval, period, idempotency, concurrency, corrections | `references/posting-control-checklist.md` |
| Projection and replay | Posted facts already exist and the risk is in rebuilds, ordering, duplicates, versioning, or report handoff | `references/projection-replay-checklist.md` |
| Ledger data question | The user wants numbers or status from a ledger, not a code change | The tool table below |

A task can need more than one mode. Start with the one closest to the change and note the others.

## Core Invariants

These hold in every mode:

1. **Ownership.** `src/Meridian.Ledger`, `src/Meridian.FSharp.Ledger`, and
   `src/Meridian.FinancialOperations` own posting rules, accounting semantics, and close controls.
   UI, reporting, and storage may project or persist snapshots. They must not redefine balance math
   or posting policy.
2. **Events are reviewed facts.** An event needs source evidence or explicit operator rationale,
   effective date, posting date, approval state, period posture, idempotency key,
   correlation/causation ids, and a version or concurrency guard before it has accounting impact.
3. **Posted journals are append-only.** Corrections append a reversal/rebook, amendment, or
   restatement that preserves the original entry and its evidence. Nothing mutates a posted entry.
4. **Fail closed.** Missing evidence, approval, period posture, idempotency key, or concurrency
   guard, or unbalanced debits and credits, blocks posting with a stated reason.
5. **Projections are rebuildable.** Balances, read models, and report lines can be rebuilt from
   posted facts and retained evidence. Replay never rewrites posted facts. Projection logic changes
   carry an explicit version.
6. **Thin clients.** Browser and WPF consume shared endpoints and read models, show disabled
   reasons, and route posting actions through shared services.

## Workflow

1. Check `git status --short` and keep unrelated changes intact.
2. Load the accounting docs listed above, then the owning module's source.
3. Pick the mode and open its checklist.
4. Map the path end to end: event, evidence, posting command, journal entry, projection, report or
   close consumer, and audit trail. Mark which parts are durable facts and which are rebuildable.
5. Check every item in the mode's checklist against the code or design. Record each gap with the
   file and the condition that would let bad data through.
6. For a code change, add the narrowest test that proves the touched invariant: balanced totals,
   idempotent replay, duplicate or out-of-order handling, lock or approval rejection, or rebuild
   determinism. Hand bigger test suites to `meridian-test-writer`.
7. Report using the output standard below.

## Ledger Data Questions

Use the read-only ledger MCP tools from `src/Meridian.Mcp`. They call the workstation API, which
enforces the operator session, tenant scope, and run visibility.

- **Strategy-run ledger, keyed by `runId`:** `get_run_trial_balance`, `get_ledger_trial_balance`,
  `get_ledger_accounts`, `get_ledger_running_balance`, `get_ledger_journal`, `get_symbol_ledger`,
  and `get_run_ledger_integrity`.
- **Posted accounting books, keyed by `periodId`:** `get_posted_ledger_journal`,
  `get_posted_trial_balance`, `get_posted_running_balance`, and `get_ledger_completeness`.

The "Ledger tool routing" table in `src/Meridian.Mcp/README.md` maps each question to its tool and
required inputs. Rules:

- No tool posts, corrects, or closes anything. Changing a ledger goes through the workstation's
  governed accounting flows.
- A refused or unavailable result means the session lacks access or the ledger is not retained. Say
  so. Never fall back to reading journal or archive files directly.
- Quote figures as returned, with the `runId` or `periodId` and the as-of instant used. Label any
  arithmetic you add on top as your own.

## Handoffs

| Hand off to | When |
|---|---|
| `meridian-test-writer` | Scenario-first tests for posting, replay, reversal/rebook, projection, or close |
| `meridian-code-review` | Findings-only review of a diff, including module boundaries and contract impact |
| `meridian-blueprint` | A full design document is needed before implementation |
| `meridian-implementation-assurance` | Rollout proof, docs sync, validation evidence, and residual risk |
| `meridian-simulated-user-panel` | Operator trust in blocked-posting reasons, close clarity, or audit readiness, using the fund-accountant, controller, auditor, or reconciliation-analyst personas |
| `meridian-docs` | Accounting docs under `docs/ai/context/` or `docs/domain/` need updating |

## Validation

- Source changes: run the narrowest test project that covers the touched path, for example
  `dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~<Class>"`,
  or the F# ledger tests in `tests/Meridian.FSharp.Tests`.
- Skill or guidance changes: `python3 build/scripts/docs/validate-skill-packages.py` and
  `python3 build/scripts/docs/check-ai-inventory.py --summary`, after
  `python3 build/scripts/ai/generate-guidance.py --write` syncs the `.agents/skills` mirror.

## Output Standard

- Name the event, its source evidence, approval and period gates, and the journal consequence.
- Separate posted facts from rebuildable projections and UI snapshots.
- Report invariant coverage: balance, evidence, approval, period lock, idempotency, ordering,
  concurrency, replay, reversal/rebook, projection version, and audit trail. Mark each covered,
  missing, or not applicable, with file references.
- Name every seam touched: contracts, services, read models, endpoints, browser and WPF consumers,
  tests, and docs.
- State what was validated and what was not.
