# Workstation Sprint 1 Implementation Backlog

Reference blueprint: `docs/plans/workstation-release-readiness-blueprint.md`

## Goal

Complete Sprint 1 by making shell bootstrap resilient, extending prefetch coverage across all four workspaces, and replacing placeholder command-palette actions with real deep-link navigation that matches the workstation information architecture.

## Story 1.1: Expand shell bootstrap to all workspaces

### Outcome

The workstation shell should prefetch summary payloads for Research, Trading, Data Operations, and Governance while continuing to render even when one or more summary requests fail.

### Tasks

- Add `data-operations` and `governance` summary models in `src/Meridian.Ui/dashboard/src/types.ts`.
- Extend workstation client helpers in `src/Meridian.Ui/dashboard/src/lib/api.ts`.
- Refactor `src/Meridian.Ui/dashboard/src/hooks/use-workstation-data.ts` to use `Promise.allSettled`.
- Preserve successful payloads when one or more workspace requests fail.
- Expose per-workspace bootstrap errors for shell-level warning treatment.
- Add or extend backend endpoint coverage for `/api/workstation/data-operations`.

### Code areas

- `src/Meridian.Ui/dashboard/src/types.ts`
- `src/Meridian.Ui/dashboard/src/lib/api.ts`
- `src/Meridian.Ui/dashboard/src/hooks/use-workstation-data.ts`
- `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`

### Acceptance criteria

- Shell bootstrap prefetches `session`, `research`, `trading`, `data-operations`, and `governance`.
- Partial bootstrap failure does not blank already loaded workspace data.
- Top-level workspace switching still works after partial bootstrap failure.
- Shell shows a degraded-state warning rather than a fatal screen when at least one primary payload succeeds.

## Story 1.2: Add route and command-palette coverage for primary workflows

### Outcome

Every primary workstation slice should be reachable through both route navigation and the command palette, including Trading, Data Operations, and Governance deep links.

### Tasks

- Define a shared command catalog in `src/Meridian.Ui/dashboard/src/lib/workspace.ts`.
- Replace research-only quick actions in `src/Meridian.Ui/dashboard/src/components/meridian/command-palette.tsx`.
- Add wildcard route coverage in `src/Meridian.Ui/dashboard/src/app.tsx` for:
  - `/trading/*`
  - `/data-operations/*`
  - `/governance/*`
- Verify that command palette entries no longer deep-link non-research actions to `/`.

### Code areas

- `src/Meridian.Ui/dashboard/src/lib/workspace.ts`
- `src/Meridian.Ui/dashboard/src/components/meridian/command-palette.tsx`
- `src/Meridian.Ui/dashboard/src/app.tsx`

### Acceptance criteria

- Trading commands exist for orders, positions, and risk.
- Data Operations commands exist for providers, backfills, and exports.
- Governance commands exist for ledger, reconciliation, and security master.
- Command-palette tests cover at least one deep-link action from each non-research workspace.

## Story 1.3: Replace placeholder routes with MVP workspace slices

### Outcome

The release route model should support Data Operations and Governance summary screens instead of generic placeholders.

### Tasks

- Introduce `data-operations-screen.tsx` backed by the prefetched summary payload.
- Introduce `governance-screen.tsx` backed by the prefetched summary payload.
- Keep detail-heavy drill-ins lazy for later sprints.
- Add focused screen tests for each new route.

### Code areas

- `src/Meridian.Ui/dashboard/src/screens/data-operations-screen.tsx`
- `src/Meridian.Ui/dashboard/src/screens/governance-screen.tsx`
- `src/Meridian.Ui/dashboard/src/screens/*.test.tsx`

### Acceptance criteria

- Data Operations top-level route shows real provider, backfill, and export summary content.
- Governance top-level route shows real reconciliation, cash-flow, and reporting summary content.
- Placeholder copy is removed from release routes.
- Empty and error states remain panel-scoped and retry-friendly.

## Suggested implementation order

1. Land Story 1.1 first so shell bootstrap reflects the intended architecture.
2. Land Story 1.2 immediately after so routing and command discovery stay aligned.
3. Finish Story 1.3 once the summary payloads are stable and visible in the shell.

## Definition of ready for Sprint 2

- Shell bootstrap is resilient and prefetched across all workspaces.
- Command palette mirrors the information architecture.
- Data Operations has a real workstation endpoint.
- Route structure is stable enough to layer Trading write actions onto `/trading/*`.
