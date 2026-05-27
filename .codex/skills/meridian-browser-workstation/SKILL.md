---
name: meridian-browser-workstation
description: Guide TypeScript/React browser workstation work in src/Meridian.Ui/dashboard with Meridian-specific routing, guardrails, and validation commands.
---

# Meridian Browser Workstation

Use this skill when the task targets the TypeScript/React browser operator workstation in `src/Meridian.Ui/dashboard/`.

Read in order:
1. `../_shared/project-context.md`
2. `../_shared/codex-execution-contract.md`
3. `../../../docs/ai/generated/repo-navigation.md`
4. `../../../docs/ai/generated/recent-changes.md`

## Use When

- Dashboard pages, components, hooks, view models, routes, or browser workstation state handling.
- Browser workstation test/build failures.
- Browser workstation UX fixes that stay within existing product behavior.

## Do Not Use When

- The request is WPF-only (`src/Meridian.Wpf/`).
- The request is provider/storage/backend-only with no browser workstation touchpoint.
- The request is broad repo orientation only (use `meridian-repo-navigation` first).

## Workflow

1. Confirm the request is in `src/Meridian.Ui/dashboard/` and identify the affected feature slice.
2. Read the nearest dashboard docs/config (`package.json`, feature folders, route ownership) before edits.
3. Keep changes bounded to browser workstation TypeScript/React surfaces plus required shared contracts.
4. Run dashboard-local validation commands first.
5. Report changed files, validation output, and any residual risk.

## Handoffs

- Use `meridian-repo-navigation` when ownership is unclear.
- Use `meridian-test-writer` for missing or expanded test coverage.
- Use `meridian-code-review` for architecture/risk review.
- Use `meridian-implementation-assurance` for final scope/evidence certification.

## Validation

- `npm --prefix src/Meridian.Ui/dashboard run test`
- `npm --prefix src/Meridian.Ui/dashboard run build`
- Use narrower dashboard test commands when available for the touched slice.

## Meridian Rules

- Keep browser and desktop behavior aligned through shared contracts in `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/` when behavior is common.
- Preserve visible top-level navigation taxonomy: `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, `Settings`.
- Do not introduce mobile-specific product surfaces.

## Output Standards

- State the dashboard feature area and owner paths touched.
- Summarize behavior impact in plain language.
- Include exact browser workstation validation commands and outcomes.
