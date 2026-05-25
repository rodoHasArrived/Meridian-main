# Chief of Staff runtime integration

**Last Updated:** 2026-05-22

## Purpose

The Chief of Staff (CoS) runtime is an additive orchestration layer. It does not replace Meridian ledger, reconciliation, readiness, or evidence source-of-truth services.

## Shared workstation API routes

- `POST /api/workstation/chief-of-staff/sessions`
- `GET /api/workstation/chief-of-staff/sessions`
- `GET /api/workstation/chief-of-staff/sessions/{sessionId}`
- `POST /api/workstation/chief-of-staff/sessions/{sessionId}/decisions`
- `POST /api/workstation/chief-of-staff/sessions/{sessionId}/export-trace`
- `GET /api/workstation/chief-of-staff/health`

## Product boundary

Chief of Staff is primarily an internal AI-development orchestration capability for coordinating,
organizing, and auditing development agents. Do not expose it as an end-user investment-operations
workstation feature without an explicit product-direction change.

Typed browser client helpers may exist for development tooling or internal diagnostics, but the
standard Reporting, Accounting, Trading, Portfolio, Strategy, Data, and Settings workspaces should
not present Chief of Staff as a fund-operator workflow.

## Configuration

Configure under `ChiefOfStaff`:

- `RuntimeBaseUrl`
- `RequestTimeout`
- `MaxConcurrentSessions`
- `EnableTraceRetention`
- `AllowedIntentKinds`
- `AllowedMcpServers`
- `EnableDecisionRouting`

## Evidence integration

- Evidence subject kind: `chief-of-staff-session`
- CoS trace exports use existing evidence manifest retention via `/api/workstation/evidence/.../export-manifest`.

## Runtime scaffold

The in-repo ADK scaffold lives at `tools/chief-of-staff-runtime/` and defines node boundaries for:

- intent analysis
- context assembly
- evidence aggregation
- recommendation synthesis
- decision preparation
- trace emission
