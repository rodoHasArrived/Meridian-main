---
name: meridian-brainstorm
description: Generate high-value ideas for Meridian features, UX improvements, architecture changes, onboarding, growth, or technical debt. Use when the user asks to brainstorm, wants ideas, explores what to build next, describes a pain point or persona, compares Meridian to competitors, or asks for quick wins, product bets, refactoring directions, or browser workstation opportunities.
---

# Meridian Brainstorm

Generate ideas that feel native to Meridian's current platform and product direction, not generic trading-app feature lists.

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before proposing
implementation hooks or naming concrete abstractions. Read `references/competitive-landscape.md`
when the user asks for differentiation, market positioning, or competitor-informed ideas.

## Use When

Use this skill when the user wants Meridian-native ideas, options, quick wins, product bets, UX
improvements, architecture directions, or persona-informed opportunity generation.

Trigger examples:

- "Brainstorm improvements to the reconciliation break queue."
- "What should we build next in the browser workstation?"
- "Give me quick wins for operator trust."
- "What's already built that we could just wire up?"

## Do Not Use When

Use `meridian-blueprint` after one idea is selected, `meridian-roadmap-strategist` for committed
delivery sequencing, and `meridian-code-review` for bug-risk assessment of an actual change.

Non-trigger examples:

- "Write the technical design for idea #2."
- "Update the roadmap document."
- "Review this diff for regressions."

## Workflow

1. Detect the mode: open exploration, problem-focused, activation, role-focused, quick wins, architecture, evidence and governance, adoption, UX, or technical debt.
2. State the detected mode in the skill selection receipt.
3. Start the brainstorm body with a compact summary table so the user can triage quickly. Use grouped audience codes: OPS, ACCT, INV, GOV, EXEC, STAKE, ADMIN (roles in `docs/product/meridian-design-document.md` §4.2).
4. Write ideas as short narratives that connect operator value, Meridian anchor points, likely implementation shape, evidence and governance impact, and tradeoffs.
5. End with synthesis: highest-leverage idea, enabling bets, roadmap fit, and suggested sequencing.

## Handoffs

- Hand off to `meridian-blueprint` when the user selects one idea for implementation design.
- Hand off to `meridian-roadmap-strategist` when ideas need sequencing into roadmap waves.
- Hand off to `meridian-simulated-user-panel` when ideas need persona critique before selection.

## Validation

- Ground ideas in current repo capabilities and known product direction before proposing new surfaces.
- Take live status only from `docs/roadmap/generated/ROADMAP_SUMMARY.md` and `docs/roadmap/data/*.yml`; take product framing from `docs/product/meridian-design-document.md`. Never assert wave or capability status from memory.
- Search or read relevant docs only as far as needed to avoid stale or generic suggestions.
- Do not claim delivery status or implementation completeness from brainstorming output; a capability that exists in source is not therefore on the live operator path.

## Automation Scripts

- `scripts/brainstorm_output_check.py` validates that brainstorm responses start with the skill
  selection receipt and compact idea triage table.
- `scripts/run_evals.py` runs deterministic output-shape eval fixtures by default and only runs live
  Codex traces with `--live-run`.
- `scripts/score_eval.py` scores brainstorm outputs for receipt/mode, triage, Meridian grounding,
  tradeoff clarity, and handoff quality.

## Idea Standards

Every strong idea should include:

- The anchor: what existing Meridian capability or abstraction it extends or wires up
- The operator moment: what the operator sees, clicks, resolves, or approves, and in which workspace
- The implementation shape: enough detail to scope follow-up work
- The evidence and governance impact: what proof it creates or exposes, what it blocks when absent, what approval it requires
- Honest tradeoffs: complexity, migration cost, or prerequisites
- Audience and effort: who benefits and roughly how big the work is

## Output Shape

```md
**Skill Selection**
- Skill: `meridian-brainstorm`
- Mode: `<detected mode>`
- Reason: <why this is ideation>
- Required Opening: compact triage table first

## Ideas at a Glance
| # | Idea | Effort | Audience | Impact | Depends On |

## Idea 1
...

## Synthesis
...
```

## Meridian-Specific Guidance

- Meridian sells proven numbers: every figure carries a reconstructable chain from source evidence
  through validation, reconciliation, ledger impact, approval, report usage, and delivery. Favor
  ideas that shorten, strengthen, or expose that chain along the operator spine
  (`Import → Validate → Reconcile → Investigate → Approve → Report`).
- Favor activation over expansion. The codebase is more capable than the running product; wiring a
  dormant capability and retiring its weaker predecessor beats building new surface beside it. See
  the activation register in `docs/product/meridian-design-document.md` §2.1.
- For UI-facing ideas, start from the browser workstation in `src/Meridian.Ui/dashboard/` or the
  co-equal WPF desktop workstation in `src/Meridian.Wpf/`; both consume the shared
  `Meridian.Ui.Services` / `Meridian.Ui.Shared` / `Meridian.Contracts` seam and neither may fork
  product state. Describe screen hierarchy and operator flow, not just backend plumbing.
- For workstation-related ideas, map them to the seven visible operator workspaces: Trading,
  Portfolio, Accounting, Reporting, Strategy, Data, or Settings. Never propose an eighth root.
- For refactoring ideas, reference real seams such as `EventPipeline`, `IStorageSink`,
  `BindableBase`, `WorkstationEndpoints`, provider contracts, or orchestration services.
- For competitive questions, focus on patterns Meridian can adapt naturally rather than
  cargo-culting product surfaces. Read `references/competitive-landscape.md` first.
- Respect truth discipline: simulated data stays loudly labeled, unsupported persistence fails
  closed, and missing evidence renders as `review-required` or `blocked`. Never propose a feature
  that makes unwired capability look finished.
- Respect governed autonomy: AI may extract, match, summarize, detect, and draft, but never bypass
  operator approval, retained evidence, ledger controls, period locks, segregation of duties,
  report release checks, or payment controls.
- Respect the deferred expansion boundaries in `docs/product/deferred-expansion-boundaries.md`
  (live treasury payments, enterprise risk, forecasting engines, capital-structure modeling, broad
  client portal, no-code workflow design). Brainstorm toward the acceptance boundary, not past it.
- Exclude mobile app ideas unless the user or roadmap explicitly asks to reopen mobile product
  development.

## Avoid

- Generic feature dumps with no Meridian anchor
- Ideas that require replacing the whole platform
- Backend-only proposals that never explain the user-facing value
- Repeating the same persona or same effort tier across the entire output unless the request demands it

## Output Standards

- Lead with the skill selection receipt, then a compact idea table for triage.
- For each idea, include user value, Meridian anchor, likely implementation shape, tradeoffs, audience, and effort.
- End with the strongest recommendation and the next skill or artifact that should follow.
