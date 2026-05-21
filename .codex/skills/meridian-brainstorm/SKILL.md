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

- "Brainstorm improvements to provider credential management."
- "What should we build next in the browser workstation?"
- "Give me quick wins for operator trust."

## Do Not Use When

Use `meridian-blueprint` after one idea is selected, `meridian-roadmap-strategist` for committed
delivery sequencing, and `meridian-code-review` for bug-risk assessment of an actual change.

Non-trigger examples:

- "Write the technical design for idea #2."
- "Update the roadmap document."
- "Review this diff for regressions."

## Workflow

1. Detect the mode: open exploration, problem-focused, persona-focused, quick wins, architecture, UX, growth, or technical debt.
2. State the detected mode in one line.
3. Start with a compact summary table so the user can triage quickly.
4. Write ideas as short narratives that connect user value, Meridian anchor points, likely implementation shape, and tradeoffs.
5. End with synthesis: highest-leverage idea, enabling bets, and suggested sequencing.

## Handoffs

- Hand off to `meridian-blueprint` when the user selects one idea for implementation design.
- Hand off to `meridian-roadmap-strategist` when ideas need sequencing into roadmap waves.
- Hand off to `meridian-simulated-user-panel` when ideas need persona critique before selection.

## Validation

- Ground ideas in current repo capabilities and known product direction before proposing new surfaces.
- Search or read relevant docs only as far as needed to avoid stale or generic suggestions.
- Do not claim delivery status or implementation completeness from brainstorming output.

## Idea Standards

Every strong idea should include:

- The anchor: what existing Meridian capability or abstraction it extends
- The user moment: what the user sees, clicks, compares, or monitors
- The implementation shape: enough detail to scope follow-up work
- Honest tradeoffs: complexity, migration cost, or prerequisites
- Audience and effort: who benefits and roughly how big the work is

## Output Shape

```md
**Mode detected:** ...

## Ideas at a Glance
| # | Idea | Effort | Audience | Impact | Depends On |

## Idea 1
...

## Synthesis
...
```

## Meridian-Specific Guidance

- Favor ideas that connect multiple existing pillars: collection, backtesting, execution, strategies, ledger, and UI.
- For UI-facing ideas, start from the active browser workstation in `src/Meridian.Ui/dashboard/`; describe screen hierarchy and operator flow, not just backend plumbing.
- For workstation-related ideas, map them to the current visible operator workspaces: Trading, Portfolio, Accounting, Reporting, Strategy, Data, or Settings.
- For refactoring ideas, reference real seams such as `BindableBase`, `EventPipeline`, `IStorageSink`, provider contracts, or orchestration services.
- For competitive questions, focus on patterns Meridian can adapt naturally rather than cargo-culting product surfaces.
- Exclude mobile app ideas unless the user or roadmap explicitly asks to reopen mobile product development.

## Avoid

- Generic feature dumps with no Meridian anchor
- Ideas that require replacing the whole platform
- Backend-only proposals that never explain the user-facing value
- Repeating the same persona or same effort tier across the entire output unless the request demands it

## Output Standards

- Lead with a compact idea table for triage.
- For each idea, include user value, Meridian anchor, likely implementation shape, tradeoffs, audience, and effort.
- End with the strongest recommendation and the next skill or artifact that should follow.
