# SOC 2 Scope Definition (Meridian)

**Last Updated:** 2026-05-27  
**Program Owner:** Security & Platform Engineering

## Objective

Define the Meridian systems and workflows that are in scope for SOC 2 readiness and audit execution, aligned to the Trust Services Criteria (Security as baseline, with Availability and Confidentiality where contracted).

## In-Scope System Boundaries

### 1) API host and backend command/runtime services

- Core host runtime (`src/Meridian/`) including API surfaces, orchestration, command handlers, and endpoint authorization behavior.
- Shared workstation endpoint surfaces under `src/Meridian.Ui.Shared/Endpoints/*` and related application service layers.
- Authentication/session controls, RBAC permission checks, and rate-limiting behaviors documented in the current threat model.

Initial evidence pointers:
- `docs/security/threat-model-current-state.md`
- `docs/operations/operator-runbook.md`
- `docs/operations/live-execution-controls.md`
- `docs/operations/preflight-checklist.md`

### 2) Operator interfaces (WPF + browser workstation)

- Desktop operator surface (`src/Meridian.Wpf/`) used for trading, portfolio, accounting, reporting, strategy, data, and settings workflows.
- Browser operator surface (`src/Meridian.Ui/dashboard/`) and built workstation asset lane (`src/Meridian.Ui/wwwroot/workstation/`).
- Role-aware operator flows and command execution paths that can mutate production-like state.

Initial evidence pointers:
- `docs/development/wpf-implementation-notes.md`
- `docs/operations/workstation-governance-approval-runbook.md`
- `docs/status/evidence/wave2-cockpit-evidence-packet.md`

### 3) Storage and data integrity surfaces

- Local and managed storage paths used for JSONL/Parquet, WAL, package import/export, and replay artifacts.
- Database-backed state (fund operations, reconciliation, account/business records) and storage migration/maintenance workflows.
- Data package lifecycle controls (create/list/validate/import/delete) and path-containment protections.

Initial evidence pointers:
- `docs/operations/reconciliation-operations.md`
- `docs/operations/reconciliation-runbook.md`
- `docs/operations/portable-data-packager.md`
- `docs/operations/fund-ops-persistence-cutover-runbook.md`

### 4) Provider and integration boundary controls

- Provider adapters and credentialed integrations (market data, brokerage, historical/backfill, statement and ETL ingress paths).
- Provider degradation calibration, promotion gates, and validation packets.
- External endpoint governance for integration host/port configuration.

Initial evidence pointers:
- `docs/operations/provider-credential-management.md`
- `docs/operations/provider-degradation-policy.md`
- `docs/operations/provider-degradation-calibration.md`
- `docs/status/provider-validation-matrix.md`
- `docs/status/evidence/dk1-pilot-parity-runbook.md`

### 5) CI/CD and release workflows

- Build/test/security gates from local automation and GitHub workflow execution.
- Security scanning and dependency/audit checks.
- Packaging/install artifacts for operator deployment pathways (MSIX and related publish outputs).

Initial evidence pointers:
- `docs/development/build-observability.md`
- `docs/development/documentation-automation.md`
- `docs/operations/deployment.md`
- `docs/operations/msix-packaging.md`
- `docs/status/doc-health-dashboard.md`

## Out-of-Scope (Current Program Iteration)

- Mobile clients (iOS, Android, MAUI, React Native, Flutter).
- Third-party systems not controlled by Meridian engineering (covered via vendor-management evidence instead of direct control testing).
- Historical archive-only docs/artifacts not tied to active operational control execution.

## Scope Governance

- Scope changes require approval by Security Lead + Engineering Manager.
- Any new production-significant subsystem must be tagged as:
  - **In scope now**, or
  - **Deferred with risk acceptance + target inclusion milestone**.
- Scope is reviewed quarterly alongside SOC evidence cadence.
