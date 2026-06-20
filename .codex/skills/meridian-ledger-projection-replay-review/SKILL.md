---
name: meridian-ledger-projection-replay-review
description: Review Meridian ledger projection and replay behavior for duplicate and out-of-order events, idempotent replay, ordering, projection rebuilds, projection versioning, balance drift, immutable journal inputs, read-model correctness, close/reporting handoff, and audit replay evidence. Use when work changes or reviews ledger projections, replay pipelines, read-model rebuilds, report-line provenance, or projection/replay tests.
---

# Meridian Ledger Projection Replay Review

Use this skill to review the highest-risk projection and replay paths after immutable accounting
facts exist. Read `../_shared/project-context.md`, `../_shared/codex-execution-contract.md`,
`../meridian-event-accounting-architecture/SKILL.md`,
`docs/ai/context/accounting-context.md`, and `docs/ai/context/operational-evidence-context.md`
before architecture-sensitive replay or projection changes.

Read `references/projection-replay-checklist.md` when the task needs a reusable checklist for
ordering, duplicate handling, rebuild behavior, projection versioning, or report-line handoff.

## Use When

Use this skill for:

- Ledger projection, read-model rebuild, replay, materialized balance, capital-account roll-forward,
  close package, or report-line provenance behavior.
- Reviews of duplicate events, out-of-order events, idempotent replay, projection versioning,
  balance drift, immutable journal input boundaries, and rebuild determinism.
- Designs that must explain what is durable accounting fact versus rebuildable projection or UI
  snapshot.
- Scenario-first replay tests before rollout or assurance.

Trigger examples:

- "Review this ledger projection for duplicate and out-of-order replay risk."
- "Design projection rebuild behavior for journal facts used in close reporting."
- "Check whether report-line provenance survives projection version changes."

## Do Not Use When

Do not use this skill for:

- Posting command approval, period lock, or journal write gates before facts exist; use
  `meridian-accounting-posting-controls`.
- Broad event-accounting architecture before a projection/replay lane is selected; use
  `meridian-event-accounting-architecture`.
- Contract compatibility tracing without replay semantics; use `meridian-contract-governance`.
- UI-only display of projection state; use a UI lane, then hand off here only for replay semantics.

Non-trigger examples:

- "Review a posting command for missing approval state."
- "Add a dense WPF grid for balances."
- "Trace DTO consumers for a ledger endpoint rename."

## Workflow

1. Load the accounting context, operational evidence context, fund-event dictionary, operational
   evidence graph, and module map.
2. Identify the immutable journal input, event ordering source, projection target, read model,
   report or close consumer, and owner module.
3. Verify replay controls: idempotent replay, duplicate detection, out-of-order handling,
   correlation/causation identifiers, replay batch identity, and deterministic ordering.
4. Separate durable facts from rebuildable projections. Projection rebuilds may update read models,
   but they must not rewrite posted journal facts or retained evidence.
5. Review projection versioning, migration/backfill posture, balance drift detection, and report-line
   provenance before close or reporting handoff.
6. Return replay risk map, rebuild assumptions, impacted seams, narrow test scenarios, validation
   commands, and residual projection risk.

## Handoffs

- Hand off to `meridian-event-accounting-architecture` when projection/replay concerns expand into
  broader event-sourced accounting design.
- Hand off to `meridian-accounting-posting-controls` when replay reveals missing posting gates or
  journal write controls.
- Hand off to `meridian-contract-governance` when projection, endpoint, read-model, or report-line
  contracts change.
- Hand off to `meridian-test-writer` for scenario-first duplicate, out-of-order, rebuild,
  projection-version, balance-drift, and report-handoff tests.
- Hand off to `diagnostics-audit-timeline` when replay evidence or recovery presentation is the main
  concern.
- Hand off to `meridian-implementation-assurance` for rollout proof, docs sync, validation, and
  residual risk.

## Validation

- Run `python .codex/skills/meridian-ledger-projection-replay-review/scripts/projection_replay_check.py --summary`
  after changing this skill or using it to audit a design note.
- Run `python .codex/skills/meridian-ledger-projection-replay-review/scripts/run_evals.py --all --dry-run --summary`
  for deterministic package evals.
- Run `python .codex/skills/meridian-codex-skill-builder/scripts/skill_package_audit.py --skill meridian-ledger-projection-replay-review --summary`
  after package changes.
- For source changes, add the narrowest ledger, Financial Operations, UI shared, browser, WPF, or
  reporting test that covers the touched replay/projection path.
- Run `python build/scripts/docs/check-codex-skills.py --summary`,
  `python build/scripts/docs/check-ai-inventory.py --summary`, and
  `python build/scripts/docs/prompt-route-linter.py --summary` when discovery or route rules change.

## Output Standards

- State the immutable journal input, projection target, ordering source, replay batch identity, and
  close/reporting consumer.
- Distinguish posted facts, replay inputs, rebuildable read models, UI snapshots, and report-line
  usage.
- Report invariant coverage: idempotent replay, duplicate detection, out-of-order handling,
  deterministic ordering, projection rebuild, projection versioning, balance drift, report-line
  provenance, audit replay evidence, and recovery path.
- Name every impacted seam: contracts, services, read models, endpoints, WPF/browser consumers,
  tests, docs, diagnostics, and assurance handoff.
