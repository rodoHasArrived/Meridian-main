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

Trigger examples:

- "Fix failing dashboard tests after a React state change."
- "Update a workstation route and confirm browser build output."
- "Improve a dashboard panel workflow without touching WPF."

## Do Not Use When

- The request is WPF-only (`src/Meridian.Wpf/`).
- The request is provider/storage/backend-only with no browser workstation touchpoint.
- The request is broad repo orientation only (use `meridian-repo-navigation` first).

Non-trigger examples:

- "Add a new WPF workspace tab."
- "Implement a provider adapter in Meridian.Infrastructure."
- "Only refresh AI inventory docs."

## Workflow

1. Confirm the request is in `src/Meridian.Ui/dashboard/` and identify the affected feature slice.
2. Read the nearest dashboard docs/config (`package.json`, feature folders, route ownership) before edits.
3. Keep changes bounded to browser workstation TypeScript/React surfaces plus required shared contracts.
4. Preserve accessibility, accessible names, keyboard behavior, live-region semantics, route/deep-link state, and shared read-model semantics.
5. Run dashboard-local validation commands first.
6. When the issue is visual, interactive, layout-sensitive, or only visible in rendered state, start
   the local dev server and use the Codex Browser plugin on an unauthenticated local route or
   file-backed preview. Keep the browser pass scoped to the named route and state.
7. Report changed files, validation output, Browser plugin evidence when used, and any residual risk.

## Handoffs

- Use `meridian-repo-navigation` when ownership is unclear.
- Use `meridian-test-writer` for missing or expanded test coverage.
- Use `meridian-code-review` for architecture/risk review.
- Use `meridian-implementation-assurance` for final scope/evidence certification.

## Validation

- `npm --prefix src/Meridian.Ui/dashboard run test`
- `npm --prefix src/Meridian.Ui/dashboard run build`
- Use narrower dashboard test commands when available for the touched slice.
- Use the Codex Browser plugin for rendered local UI checks after tests when the task needs visual,
  DOM, console, network, or interaction evidence. Do not use it for signed-in flows or secret entry.

## Meridian Rules

- Keep browser and desktop behavior aligned through shared contracts in `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/` when behavior is common.
- Preserve accessible names, live-region semantics, keyboard selection, and route/deep-link behavior when changing workstation state.
- Preserve visible top-level navigation taxonomy: `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, `Settings`.
- Do not introduce mobile-specific product surfaces; there is no mobile development lane for browser workstation work.

## Output Standards

- State the dashboard feature area and owner paths touched.
- Summarize behavior impact in plain language.
- Include exact browser workstation validation commands, accessibility or route/deep-link coverage, and outcomes.
