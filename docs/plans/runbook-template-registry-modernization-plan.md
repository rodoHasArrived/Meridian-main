# Runbook + Template Registry Modernization Plan

## Scope (Approved Candidate #1)

This plan introduces a composable Runbook and Template Registry capability to let operators save, reuse, execute, and audit multi-step workflows across CLI/API/web surfaces.

## Phase 1 (Implemented in this change set)

- Define the approved architecture and extension contracts at planning level.
- Add delivery checklist and implementation guardrails for incremental execution.
- Keep behavior unchanged (documentation-only phase).

## Target Capabilities

1. Runbook definitions with typed step payloads.
2. Template registry for reusable workflow blueprints.
3. Unified executor semantics across CLI, API, and MCP.
4. Durable audit logs for every run.
5. Import/export for runbook portability.

## Architecture Outline

- `IRunbookRegistry`: CRUD + version resolution for runbook definitions.
- `ITemplateRegistry`: catalog of built-in and user templates.
- `IRunbookExecutor`: validates and executes deterministic step pipelines.
- `IRunbookStepHandler`: pluggable step implementation seam.
- `IRunbookAuditStore`: immutable execution evidence.

## Delivery Phases

### Phase 2 — Contracts + Storage
- Add runbook contracts under `src/Meridian.Contracts/Runbooks`.
- Add application services under `src/Meridian.Application/Runbooks`.
- Add storage models/migrations under `src/Meridian.Storage/Runbooks`.

### Phase 3 — API + CLI
- Add runbook endpoints in `src/Meridian.Ui.Services`.
- Add CLI commands in `src/Meridian` for create/list/run/export.

### Phase 4 — Web Workstation UX
- Add runbook management screen in `src/Meridian.Ui/dashboard`.
- Add execution history and dry-run/confirm flows.

### Phase 5 — Hardening
- Feature flag and compatibility checks.
- Focused test slices and contract validation.

## Non-Goals

- No paid APIs.
- No replacement of existing command workflows.
- No speculative plugin system without active runbook usage.

## Validation Checklist

- Existing commands remain unchanged by default.
- Runbook execution supports dry-run and immutable audit output.
- Storage migration is additive and reversible.
- Web UI changes remain in active dashboard lane.

## Rollback

- Disable runbook feature flag.
- Keep runbook data but revert execution routing to existing direct workflows.

