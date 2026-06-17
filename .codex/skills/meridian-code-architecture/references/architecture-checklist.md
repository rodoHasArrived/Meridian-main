# Architecture Checklist

Use this checklist after the main skill file when a task needs a formal architecture assessment.

## Required Evidence

- Current layer owner from `docs/architecture/module-map.md`.
- Physical source classification from `docs/architecture/project-structure.md`.
- Nearest registered source README for touched `src/**` paths.
- Existing ADRs when the decision touches storage durability, JSON serialization, channels,
  provider contracts, or shared UI/service seams.
- Project references from the relevant `.csproj` files.

## Boundary Rules

- Domain, core, contracts, storage, providers, execution, strategy, backtesting, and ledger projects
  must not depend on WPF, browser dashboard, or UI-specific services.
- Browser and WPF operator surfaces should consume shared DTOs, services, and route definitions
  instead of duplicating workflow rules.
- New architecture should strengthen the W1-W5 operational record baseline and W5X financial
  operations targets unless the roadmap explicitly opens a broader lane.
- Do not introduce mobile-specific projects, clients, or workflows.

## Output Checks

- Separate verified facts from recommendations.
- State whether the proposal preserves, narrows, or widens public contracts.
- Name the implementation owner skill for the next phase.
- Include the narrowest validation commands for changed projects and docs.
