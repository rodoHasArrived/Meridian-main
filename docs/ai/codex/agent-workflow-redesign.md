# Codex Agent Workflow Redesign

**Status:** proposed
**Owner:** core-team
**Reviewed:** 2026-06-19

This blueprint redesigns Meridian's Codex agent workflow around one explicit coordinator, a small
set of durable lane types, deterministic routing artifacts, and proof-first promotion rules. It
does not replace the shared provider-agnostic contract in
[`../assistant-workflow-contract.md`](../assistant-workflow-contract.md); it defines the Codex target
state that can be implemented in reviewable batches.

## Summary

Codex should operate as a routed, evidence-producing execution system instead of a loose catalog of
agent profiles. The redesigned workflow keeps `.codex/skills/` as the canonical Codex behavior
surface, uses `.codex/agents/*.toml` only as compact entrypoints, and promotes every meaningful
agent/workflow change through route evidence, handoff evidence, validation evidence, and index
sync.

The target model has four durable layers:

1. **Coordinator layer:** one active coordinator owns scope, context budget, worktree safety, lane
   routing, validation ownership, and final evidence.
2. **Specialist lane layer:** skills and agent profiles execute one bounded concern at a time:
   orient, design, implement, test, review, docs, roadmap, archive, provider, browser, WPF,
   performance, or simulated user feedback.
3. **Evidence layer:** deterministic route, handoff, validation, and working-memory artifacts record
   why a lane ran, what it touched, and what proof exists.
4. **Promotion layer:** agent, prompt, skill, hook, and route changes move from candidate to active
   only after checks pass and the Codex indexes remain synchronized.

## Scope

In scope:

- Codex task startup, skill selection, and progress disclosure.
- `.codex/skills/` as the canonical repo-local skill system.
- `.codex/agents/*.toml` profile usage and profile lifecycle.
- Codex route rules, route evidence, handoff packets, working memory, and validation ownership.
- Codex lifecycle-hook policy for safe guardrails and validation reminders.
- Promotion rules for Codex agents, prompts, skills, route rules, and eval rubrics.
- Index and validation changes needed to keep Codex discoverability current.

Out of scope for this blueprint:

- Replacing the provider-agnostic rules in `docs/ai/assistant-workflow-contract.md`.
- Changing Claude, Copilot, or Agent Skills portable packages except when a future implementation
  batch intentionally promotes shared policy.
- Adding new AI providers, hosted services, vector databases, or graph stores.
- Implementing product UI changes in WPF or the browser workstation.
- Creating mobile or mobile-first workflows.

Assumptions:

- Existing public Codex skill names remain stable unless a future migration explicitly declares a
  breaking rename.
- The current route and handoff scripts remain the deterministic backbone:
  `prompt-route-linter.py`, `handoff-packet-generator.py`, `check-handoff-packet-schema.py`,
  `check-validation-floor.py`, `check-mode-escalation.py`, and `check-ai-routing-parity.py`.
- Existing dirty worktree changes are user-owned and should not be rewritten while implementing this
  redesign.

## Architecture

### Layer 1: Coordinator

The coordinator is the only lane that owns cross-cutting decisions. It should:

- run `git status --short` before edits and classify unrelated changes as user-owned
- choose a work mode from `../work-modes.md`
- select the narrowest applicable skill and emit the skill selection receipt
- decide whether work is single-lane or multi-lane
- create or update a parallel manifest only when multiple lanes are actually active
- maintain `../working-memory.md` style task state for concurrent or long-running work
- assign validation ownership before any specialist lane edits files
- integrate lane handoffs and produce the final evidence summary

The coordinator should not perform specialist work after delegating it unless it is integrating,
resolving conflicts, or validating the final result.

### Layer 2: Specialist Lanes

Specialist lanes stay intentionally narrow. Each lane loads only its owning skill, the shared Codex
context, and the minimum route-specific docs needed for its task.

| Lane | Canonical skill or surface | Primary output |
| --- | --- | --- |
| Orient | `meridian-repo-navigation` | route, owner docs, entrypoints, next lane |
| Design | `meridian-blueprint` | implementation-ready blueprint |
| Implement or certify | `meridian-implementation-assurance` | completed change with evidence |
| Review | `meridian-code-review` | findings first, severity ordered |
| Test | `meridian-test-writer` or `desktop-test-generation` | scenario-first test coverage |
| Docs | `meridian-docs` | canonical docs and index updates |
| Browser workstation | `meridian-browser-workstation` | React/workstation change and rendered proof when needed |
| WPF workstation | `modular-desktop-mvvm`, `workstation-screen-composition`, or desktop-specific skills | MVVM-bound desktop change |
| Provider | `meridian-provider-builder` or `provider-management-workflow` | provider contract or workflow change |
| Contract impact | `meridian-contract-governance` | DTO/API/read-model consumer map |
| Performance | `performance-resource-review` | resource and lifecycle risk review |
| Archive | `meridian-archive-organizer` | archive classification and references |
| Roadmap | `meridian-roadmap-strategist` | roadmap and target-state reconciliation |
| Simulated user feedback | `meridian-simulated-user-panel` | persona-grounded artifact critique |
| Skill/package work | `meridian-codex-skill-builder` | package, metadata, eval, and route coverage |

Each lane returns a compact handoff with inspected files, edited files, decisions, validation run or
validation owner, reusable evidence, and residual risk.

### Layer 3: Evidence

The redesigned workflow treats evidence artifacts as first-class outputs:

- **Route evidence:** `docs/status/prompt-route-lint-report.json`
- **Handoff evidence:** `docs/status/ai-handoff-packet.json`
- **Working state:** `../working-memory.md` structure for active claims, facts, assumptions, merge
  order, drift, and validation reuse
- **Parallel ownership:** `../parallel-task-manifest-template.md`
- **Validation evidence:** command output summarized in the final response, with generated JSON or
  status files when the validator already emits them
- **Promotion evidence:** eval score, script audit, package audit, route parity, inventory, and
  rollback note for Codex-owned behavior changes

Evidence should be summarized, not pasted wholesale, unless the user asks for raw logs.

### Layer 4: Promotion

Codex agent changes follow the self-improving loop:

1. Record the current baseline file, route, prompt, skill, eval, and accepted behavior.
2. Apply the smallest candidate change.
3. Run route, package, inventory, eval, and docs checks that match the touched surface.
4. Promote only after validation passes and indexes are current.
5. Leave a rollback note in the changed doc, PR description, or implementation handoff.

Candidate changes that fail validation stay unpromoted. Do not hide failed experiments in active
skills, active route rules, or agent profiles.

## Interfaces and Models

The redesign should be implemented through existing document and script surfaces before adding new
runtime code.

### Route Contract

`docs/ai/codex/prompt-route-rules.json` remains the route source. Each route must keep:

- `modelRouteId`
- lane and skill mapping
- validation floor
- validation scripts
- required telemetry
- escalation triggers

Add fields only when the validators enforce them and the handoff packet can preserve them.

### Handoff Contract

`docs/status/ai-handoff-packet.json` remains the local generated handoff artifact. A Codex handoff
must preserve:

- task scope
- selected lane and next lane
- changed files
- required context and optional context
- validation evidence
- telemetry fields required by route schema v2
- residual risks and rerun triggers

### Working-Memory Contract

For long or concurrent work, Codex lanes should use the `../working-memory.md` shape:

- active claims
- inspected files
- validated facts
- assumptions
- codebase drift
- merge order
- validation owner
- reusable validation and rerun triggers

The coordinator owns integration of this state. Specialist lanes own updates for their scoped
claims.

### Agent Profile Contract

`.codex/agents/*.toml` files should stay compact:

- `name`
- `description`
- `developer_instructions`
- deliberate Codex config overrides only when the lane needs them

Agent profiles should route to skills; they should not duplicate full project rules, command
catalogs, or broad repository maps.

### Skill Package Contract

Each current Codex skill should keep:

- minimal frontmatter: `name`, `description`
- links to `_shared/project-context.md` and `_shared/codex-execution-contract.md`
- `Use When`, `Do Not Use When`, `Workflow`, `Handoffs`, `Validation`, and `Output Standards`
- trigger and non-trigger examples
- synchronized `agents/openai.yaml`
- bundled scripts only when repeated, fragile, or validation-critical

## Data Flow

### Single-Lane Task

1. Coordinator runs `git status --short`.
2. Coordinator classifies scope and selects work mode.
3. Coordinator reads navigation and the narrowest skill.
4. Coordinator emits skill selection receipt.
5. Specialist behavior runs in the coordinator lane when no parallelism is needed.
6. Coordinator edits the smallest safe file set.
7. Coordinator runs the narrowest validation command.
8. Coordinator reports files changed, validation, excluded dirty worktree changes, and residual
   risk.

### Multi-Lane Task

1. Coordinator runs startup checks and chooses Standard or Deep-review mode.
2. Coordinator creates a parallel manifest with disjoint file ownership.
3. Each specialist lane receives one concern and explicit file ownership.
4. Each specialist lane records inspected files, decisions, validation owner, and residual risk.
5. Coordinator merges outputs in the manifest order.
6. Coordinator reruns validation for the integrated change, not just lane-local proof.
7. Coordinator emits the final evidence summary and leaves unresolved assumptions explicit.

### Agent or Skill Improvement

1. Record baseline skill/profile/prompt/rule and existing eval behavior.
2. Draft one candidate change.
3. Run package audit or script advisor if skill scripts are touched.
4. Run route, inventory, package, eval, and docs checks.
5. Update indexes only after the candidate is coherent.
6. Promote the candidate or report why it stays unpromoted.

### Hook-Backed Guardrail

1. Confirm the hook is deterministic, repository-relative, and safe for a trusted clone.
2. Document the hook in `advanced-configuration.md`.
3. Add or update validation evidence for the hook behavior.
4. Keep user-level preferences and secrets outside repository hooks.
5. Use hooks for reminders and deterministic blockers, not hidden implementation choices.

## Edge Cases and Risks

- **Agent sprawl:** new profiles can duplicate existing skills. Mitigation: require a route and
  skill owner before adding a profile.
- **Prompt-only policy drift:** instructions can diverge across Codex, Claude, Copilot, and Agent
  Skills. Mitigation: shared policy changes start in `../assistant-workflow-contract.md`; Codex-only
  mechanics stay in this folder.
- **Over-parallelization:** multiple lanes can inspect or edit the same surfaces. Mitigation:
  require disjoint ownership in the parallel manifest and coordinator-owned merge order.
- **False validation confidence:** lane-local checks can pass while integrated behavior fails.
  Mitigation: coordinator reruns integrated validation after merging lane outputs.
- **Generated artifact churn:** generated route, handoff, or navigation files can obscure manual
  doc changes. Mitigation: update source rules first, regenerate narrowly, and keep generated files
  out of hand edits.
- **Hook opacity:** hooks can silently steer behavior. Mitigation: hooks must be documented,
  reviewed through Codex `/hooks`, and backed by deterministic validation.
- **Memory or graph overreach:** retrieval systems can become speculative infrastructure. Mitigation:
  require a product need, retention/security review, and shadow comparison before production use.

## Test Plan

For the proposal-only documentation batch:

```bash
python build/scripts/docs/check-ai-inventory.py --summary
python build/scripts/docs/check-codex-skills.py --summary
python build/scripts/docs/validate-docs-structure.py --top-level ai --summary
python build/scripts/docs/repair-links.py --summary
git diff --check -- docs/ai/codex/agent-workflow-redesign.md docs/ai/codex/README.md
```

For future implementation batches that change active Codex routing, handoff, skills, profiles, or
hooks, add the relevant checks:

```bash
python build/scripts/docs/prompt-route-linter.py --summary
python build/scripts/docs/handoff-packet-generator.py --summary --route-json docs/status/prompt-route-lint-report.json
python build/scripts/docs/check-handoff-packet-schema.py --packet-json docs/status/ai-handoff-packet.json --summary
python build/scripts/docs/check-validation-floor.py --summary-json docs/status/docs-automation-summary.json --route-json docs/status/prompt-route-lint-report.json --summary
python build/scripts/docs/check-mode-escalation.py --route-json docs/status/prompt-route-lint-report.json --summary-json docs/status/docs-automation-summary.json --summary
python build/scripts/docs/check-ai-routing-parity.py --summary
python .codex/skills/meridian-codex-skill-builder/scripts/skill_package_audit.py --skill <skill> --summary
python .codex/skills/meridian-implementation-assurance/scripts/run_evals.py --all --dry-run --json
```

## Rollout Plan

### Phase 1: Adopt The Operating Model

- Publish this blueprint and link it from `docs/ai/codex/README.md`.
- Keep existing active policy unchanged.
- Use this file as the target-state reference for future Codex workflow edits.

### Phase 2: Normalize Route and Handoff Evidence

- Audit `prompt-route-rules.json` for complete route schema v2 fields.
- Ensure handoff packets preserve required telemetry and validation floors.
- Add missing route-card coverage only where current tasks need it.

### Phase 3: Profile and Skill Rationalization

- Review `.codex/agents/*.toml` for duplicated broad project rules.
- Keep profile files compact and skill-backed.
- Preserve public skill IDs; use metadata-only cleanup unless a breaking rename is explicitly
  approved.

### Phase 4: Promotion and Eval Hardening

- Attach eval or deterministic proof to any repeated agent improvement path.
- Use `skill_script_advisor.py` before adding bundled skill scripts.
- Require package audit and index sync before promotion.

### Phase 5: Hook Review

- Keep hooks enabled but conservative.
- Add project-local hooks only for deterministic guardrails with owners, docs, and validation.
- Prefer reminders and evidence checks over hidden behavior changes.

## Open Questions

- Should this target model become active policy after one implementation batch, or remain a design
  reference until route and handoff validators are tightened?
- Should the coordinator role be represented as a dedicated `.codex/agents/` profile, or should it
  remain the default behavior documented in the Codex execution contract?
- Which existing Codex profiles are high enough traffic to justify route-specific eval fixtures
  first?

